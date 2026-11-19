"""Pure-NumPy + ONNX Runtime engine for NVIDIA MotionBricks-SOMA-RP-v1 on Apple Silicon / CPU.

Ported from NVIDIA's Apache-2.0 MotionBricks SOMA engine (`motionbricks/model/soma/kernels.py`
and `motionbricks/utils/pose_utils.py`) to run `mmm_v6_soma_fp32.onnx` (NVIDIA Open Model
Agreement) natively via `onnxruntime` without requiring CUDA or TensorRT.
"""

from __future__ import annotations

import json
import time
from dataclasses import dataclass
from pathlib import Path

import numpy as np
import onnxruntime as ort
from pxr import Usd, UsdSkel
from scipy.spatial.transform import Rotation, Slerp


MODEL_SAMPLE_RATE = 30.0
MODEL_POSES_PER_TOKEN = 4
MODEL_BOUNDARY_FRAMES = 3
FOOT_CONTACT_COUNT = 4


# Canonical 22-bone Unity HumanBodyBones skeleton mapping from SOMA's 30 model joints.
# Format: (unity_bone_name, unity_parent_name, soma_joint_name, optional_extra_soma_joint)
UNITY_HUMANOID_BONES: tuple[tuple[str, str | None, str, str | None], ...] = (
    ("Hips", None, "Hips", None),
    ("Spine", "Hips", "Spine1", None),
    ("Chest", "Spine", "Spine2", None),
    ("UpperChest", "Chest", "Chest", None),
    ("Neck", "UpperChest", "Neck1", "Neck2"),
    ("Head", "Neck", "Head", None),
    ("LeftShoulder", "UpperChest", "LeftShoulder", None),
    ("LeftUpperArm", "LeftShoulder", "LeftArm", None),
    ("LeftLowerArm", "LeftUpperArm", "LeftForeArm", None),
    ("LeftHand", "LeftLowerArm", "LeftHand", None),
    ("RightShoulder", "UpperChest", "RightShoulder", None),
    ("RightUpperArm", "RightShoulder", "RightArm", None),
    ("RightLowerArm", "RightUpperArm", "RightForeArm", None),
    ("RightHand", "RightLowerArm", "RightHand", None),
    ("LeftUpperLeg", "Hips", "LeftLeg", None),
    ("LeftLowerLeg", "LeftUpperLeg", "LeftShin", None),
    ("LeftFoot", "LeftLowerLeg", "LeftFoot", None),
    ("LeftToes", "LeftFoot", "LeftToeBase", None),
    ("RightUpperLeg", "Hips", "RightLeg", None),
    ("RightLowerLeg", "RightUpperLeg", "RightShin", None),
    ("RightFoot", "RightLowerLeg", "RightFoot", None),
    ("RightToes", "RightFoot", "RightToeBase", None),
)


def orthonormalize(R: np.ndarray) -> np.ndarray:
    """Project a 3x3 matrix onto SO(3)."""
    U, _, Vt = np.linalg.svd(R)
    M = U @ Vt
    if np.linalg.det(M) < 0.0:
        U[:, -1] *= -1.0
        M = U @ Vt
    return M


def rot_from_6d(x_raw: np.ndarray, y_raw: np.ndarray) -> np.ndarray:
    """Reconstruct a 3x3 rotation matrix from MotionBricks 6D (x, y) columns."""
    x = x_raw / max(float(np.linalg.norm(x_raw)), 1e-8)
    z = np.cross(x, y_raw)
    z_norm = float(np.linalg.norm(z))
    if z_norm < 1e-8:
        fallback = np.array([0.0, 1.0, 0.0]) if abs(x[1]) < 0.9 else np.array([1.0, 0.0, 0.0])
        z = np.cross(x, fallback)
        z_norm = float(np.linalg.norm(z))
    z = z / z_norm
    y = np.cross(z, x)
    return np.stack([x, y, z], axis=1)


def heading_from_hip_positions(right_hip: np.ndarray, left_hip: np.ndarray) -> float:
    """Compute yaw heading in SOMA Y-up space (+Z forward, +X left) matching kernels.py."""
    across = np.array([right_hip[0] - left_hip[0], 0.0, right_hip[2] - left_hip[2]], dtype=np.float64)
    length = float(np.linalg.norm(across))
    if length <= 1e-6:
        return 0.0
    across /= length
    forward = np.cross(np.array([0.0, 1.0, 0.0]), across)
    return float(np.arctan2(forward[0], forward[2]))


def slerp_rot(R0: np.ndarray, R1: np.ndarray, t: float) -> np.ndarray:
    """Spherical linear interpolation between two 3x3 rotation matrices."""
    t = float(np.clip(t, 0.0, 1.0))
    if t <= 0.0:
        return R0.copy()
    if t >= 1.0:
        return R1.copy()
    rots = Rotation.from_matrix(np.stack([R0, R1], axis=0))
    return Slerp([0.0, 1.0], rots)([t]).as_matrix()[0]


@dataclass
class GeneratedBrick:
    """Decoded 30 Hz SOMA motion output from one or more MotionBricks generations."""

    world_p: np.ndarray      # [T, J_all, 3] meters, SOMA Y-up (+Z fwd, +X left)
    world_R: np.ndarray      # [T, J_all, 3, 3] rotation matrices in SOMA space
    local_p: np.ndarray      # [T, J_all, 3]
    local_R: np.ndarray      # [T, J_all, 3, 3]
    foot_contacts: np.ndarray  # [T, 4] clamped [0, 1]: [LHeel, LToe, RHeel, RToe]
    inference_ms: float
    token_counts: list[int]


class SOMASkeleton:
    """77-joint SOMA skeleton loaded from NVIDIA's Apache-2.0 USD asset."""

    def __init__(self, usd_path: Path, manifest_path: Path) -> None:
        stage = Usd.Stage.Open(str(usd_path))
        sk = UsdSkel.Skeleton(stage.GetPrimAtPath("/OUTPUT/c_skeleton_grp/Hips"))
        joint_paths = [str(j) for j in sk.GetJointsAttr().Get()]
        self.joint_names: list[str] = [p.rsplit("/", 1)[-1] for p in joint_paths]
        path_to_idx = {p: i for i, p in enumerate(joint_paths)}
        self.parents: list[int] = [
            -1 if "/" not in p else path_to_idx[p.rsplit("/", 1)[0]] for p in joint_paths
        ]
        self.num_joints = len(self.joint_names)
        self.name_to_idx = {n: i for i, n in enumerate(self.joint_names)}

        rest_mats = [np.array(m, dtype=np.float64) for m in sk.GetRestTransformsAttr().Get()]
        self.ref_local_p = np.array([m[3, :3] * 0.01 for m in rest_mats], dtype=np.float64)
        self.ref_local_R = np.stack([orthonormalize(m[:3, :3].T) for m in rest_mats], axis=0)

        with manifest_path.open("r", encoding="utf-8") as f:
            manifest = json.load(f)
        self.model_joint_names: list[str] = manifest["joint_names"]
        self.model_to_pose = np.array([self.name_to_idx[n] for n in self.model_joint_names], dtype=np.int32)

        self.pose_to_model = np.full(self.num_joints, -1, dtype=np.int32)
        for mj, pj in enumerate(self.model_to_pose):
            par = self.parents[pj]
            if par == -1 or self.pose_to_model[par] >= 0:
                self.pose_to_model[pj] = mj

        self.right_leg_idx = self.name_to_idx["RightLeg"]
        self.left_leg_idx = self.name_to_idx["LeftLeg"]
        self.left_foot_idx = self.name_to_idx["LeftFoot"]
        self.right_foot_idx = self.name_to_idx["RightFoot"]
        self.left_toe_idx = self.name_to_idx["LeftToeBase"]
        self.right_toe_idx = self.name_to_idx["RightToeBase"]

        # Exact reference standing height of ankle in SOMA USD bind pose
        bind_wp, _ = self.fk(self.ref_local_p, self.ref_local_R)
        self.ankle_ground_y = float(min(bind_wp[self.left_foot_idx, 1], bind_wp[self.right_foot_idx, 1]))
        self.hips_stand_y = float(bind_wp[0, 1])

        # Construct exact canonical T-Pose on SOMA (arms horizontal along ±X, palms down,
        # legs vertical, feet flat on ground) for zero-distortion Unity Humanoid retargeting.
        self.tpose_local_p, self.tpose_local_R = self._build_tpose()
        self.tpose_world_p, self.tpose_world_R = self.fk(self.tpose_local_p, self.tpose_local_R)

    def _build_tpose(self) -> tuple[np.ndarray, np.ndarray]:
        loc_p = self.ref_local_p.copy()
        loc_R = self.ref_local_R.copy()
        # Left arm chain along +X (Left), palm normal -Y (Down), thumb +Z (Forward)
        R_left_arm = np.array([[1.0, 0.0, 0.0], [0.0, 0.0, 1.0], [0.0, -1.0, 0.0]], dtype=np.float64)
        # Right arm chain along -X (Right = -x_axis), palm normal -Y (Down = -z_axis)
        R_right_arm = np.array([[1.0, 0.0, 0.0], [0.0, 0.0, -1.0], [0.0, 1.0, 0.0]], dtype=np.float64)

        for name in ("LeftShoulder", "LeftArm", "LeftForeArm", "LeftHand"):
            j = self.name_to_idx[name]
            p = self.parents[j]
            _, wR = self.fk(loc_p, loc_R)
            loc_R[j] = wR[p].T @ R_left_arm
        for name in ("RightShoulder", "RightArm", "RightForeArm", "RightHand"):
            j = self.name_to_idx[name]
            p = self.parents[j]
            _, wR = self.fk(loc_p, loc_R)
            loc_R[j] = wR[p].T @ R_right_arm
        return loc_p, loc_R

    def fk(self, local_p: np.ndarray, local_R: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
        """Forward kinematics for one frame [J, 3], [J, 3, 3]."""
        wp = np.zeros((self.num_joints, 3), dtype=np.float64)
        wR = np.zeros((self.num_joints, 3, 3), dtype=np.float64)
        for j in range(self.num_joints):
            p = self.parents[j]
            if p < 0:
                wp[j] = local_p[j]
                wR[j] = local_R[j]
            else:
                wp[j] = wp[p] + wR[p] @ local_p[j]
                wR[j] = wR[p] @ local_R[j]
        return wp, wR

    def apply_world_rot(
        self,
        local_p: np.ndarray,
        local_R: np.ndarray,
        joint_name: str,
        euler_xyz_deg: tuple[float, float, float],
    ) -> None:
        """Rotate a joint's subtree around SOMA world axes (+X=Left/pitch, +Y=Up/yaw, +Z=Fwd/roll)."""
        j = self.name_to_idx[joint_name]
        _, wR = self.fk(local_p, local_R)
        R_delta = Rotation.from_euler("xyz", euler_xyz_deg, degrees=True).as_matrix()
        p = self.parents[j]
        Rp = wR[p] if p >= 0 else np.eye(3)
        local_R[j] = orthonormalize(Rp.T @ (R_delta @ wR[j]))

    def compose_pose(
        self,
        joint_rotations_deg: dict[str, tuple[float, float, float]],
        ground_feet: bool = True,
        hips_y_override: float | None = None,
        root_yaw_deg: float = 0.0,
        root_xz: tuple[float, float] = (0.0, 0.0),
    ) -> tuple[np.ndarray, np.ndarray, np.ndarray, np.ndarray]:
        """Author a clean kinematic pose on SOMA from world-axis Euler deltas (relative to T-pose)."""
        loc_p = self.tpose_local_p.copy()
        loc_R = self.tpose_local_R.copy()

        # Apply rotations in topological (parent-before-child) order
        ordered = sorted(joint_rotations_deg.items(), key=lambda kv: self.name_to_idx[kv[0]])
        for jname, euler in ordered:
            self.apply_world_rot(loc_p, loc_R, jname, euler)

        wp, _ = self.fk(loc_p, loc_R)
        if ground_feet:
            lowest_ankle = min(wp[self.left_foot_idx, 1], wp[self.right_foot_idx, 1])
            loc_p[0, 1] += self.ankle_ground_y - lowest_ankle
        if hips_y_override is not None:
            loc_p[0, 1] = hips_y_override

        # Apply root yaw and XZ placement
        if abs(root_yaw_deg) > 1e-6:
            R_yaw = Rotation.from_euler("y", root_yaw_deg, degrees=True).as_matrix()
            loc_R[0] = orthonormalize(R_yaw @ loc_R[0])
        loc_p[0, 0] += root_xz[0]
        loc_p[0, 2] += root_xz[1]

        wp, wR = self.fk(loc_p, loc_R)
        return loc_p, loc_R, wp, wR


class MotionBricksSOMAOnnxEngine:
    """Runs MotionBricks-SOMA-RP-v1 FP32 ONNX inference natively on Apple Silicon / CPU."""

    def __init__(self, onnx_path: Path, skeleton: SOMASkeleton) -> None:
        self.onnx_path = onnx_path
        self.skeleton = skeleton
        opts = ort.SessionOptions()
        opts.graph_optimization_level = ort.GraphOptimizationLevel.ORT_ENABLE_ALL
        self.session = ort.InferenceSession(
            str(onnx_path),
            sess_options=opts,
            providers=["CPUExecutionProvider"],
        )

    def generate(
        self,
        ctx_local_p: np.ndarray,
        ctx_local_R: np.ndarray,
        tgt_local_p: np.ndarray,
        tgt_local_R: np.ndarray,
        *,
        num_target_poses: int = 4,
        constrain_target_pose: bool = True,
        constrain_target_root_vel: bool = True,
        allowed_tokens: tuple[int, ...] = tuple(range(6, 17)),
        endpoint_correct: bool = True,
    ) -> GeneratedBrick:
        """Run one MotionBricks neural generation from 4 context poses to 1..4 target poses."""
        sk = self.skeleton
        assert ctx_local_p.shape[0] == 4
        assert tgt_local_p.shape[0] == 4

        ctx_wp = np.zeros((4, sk.num_joints, 3), dtype=np.float64)
        ctx_wR = np.zeros((4, sk.num_joints, 3, 3), dtype=np.float64)
        tgt_wp = np.zeros((4, sk.num_joints, 3), dtype=np.float64)
        tgt_wR = np.zeros((4, sk.num_joints, 3, 3), dtype=np.float64)
        for f in range(4):
            ctx_wp[f], ctx_wR[f] = sk.fk(ctx_local_p[f], ctx_local_R[f])
            tgt_wp[f], tgt_wR[f] = sk.fk(tgt_local_p[f], tgt_local_R[f])

        # Heading alignment: align last context frame hips XZ -> origin and heading -> +X
        cur_angle = heading_from_hip_positions(ctx_wp[3, sk.right_leg_idx], ctx_wp[3, sk.left_leg_idx])
        desired_angle = np.pi / 2.0  # +X in SOMA model space
        align_p = np.array([ctx_wp[3, 0, 0], 0.0, ctx_wp[3, 0, 2]], dtype=np.float64)
        align_R = Rotation.from_euler("y", cur_angle - desired_angle).as_matrix()
        align_R_inv = align_R.T

        wp_all = np.concatenate([ctx_wp, tgt_wp], axis=0)
        wR_all = np.concatenate([ctx_wR, tgt_wR], axis=0)

        global_root = np.zeros((1, 8, 5), dtype=np.float32)
        local_root = np.zeros((1, 8, 4), dtype=np.float32)
        local_poses = np.zeros((1, 8, 267), dtype=np.float32)

        for f in range(8):
            nf = f if f == 3 else min(f + 1, 7)
            cp = (wp_all[f] - align_p) @ align_R_inv.T
            np_ = (wp_all[nf] - align_p) @ align_R_inv.T
            ch = heading_from_hip_positions(cp[sk.right_leg_idx], cp[sk.left_leg_idx])
            nh = heading_from_hip_positions(np_[sk.right_leg_idx], np_[sk.left_leg_idx])
            hd = float(np.arctan2(np.sin(nh - ch), np.cos(nh - ch)))

            global_root[0, f] = [cp[0, 0], cp[0, 1], cp[0, 2], np.cos(ch), np.sin(ch)]
            local_root[0, f] = [
                hd * MODEL_SAMPLE_RATE,
                (np_[0, 0] - cp[0, 0]) * MODEL_SAMPLE_RATE,
                (np_[0, 2] - cp[0, 2]) * MODEL_SAMPLE_RATE,
                cp[0, 1],
            ]

            root_xz = np.array([cp[0, 0], 0.0, cp[0, 2]], dtype=np.float64)
            for mj in range(len(sk.model_joint_names)):
                pj = int(sk.model_to_pose[mj])
                if mj > 0:
                    local_poses[0, f, (mj - 1) * 3 : mj * 3] = cp[pj] - root_xz
                R_al = align_R_inv @ wR_all[f, pj]
                off = 87 + mj * 6
                local_poses[0, f, off : off + 3] = R_al[:, 0]
                local_poses[0, f, off + 3 : off + 6] = R_al[:, 1]

        has_gr = np.zeros((1, 8), dtype=np.int32)
        has_lp = np.zeros((1, 8), dtype=np.int32)
        has_lr = np.zeros((1, 8), dtype=np.int32)
        for idx in range(8):
            active = 1 if idx < 4 + num_target_poses else 0
            has_gr[0, idx] = active
            has_lp[0, idx] = active if (idx < 4 or constrain_target_pose) else 0
            if idx == 3 or idx == 4 + num_target_poses - 1:
                has_lr[0, idx] = 0
            elif idx >= 4 and not constrain_target_root_vel:
                has_lr[0, idx] = 0
            else:
                has_lr[0, idx] = active

        allowed = np.zeros((1, 11), dtype=np.int32)
        for t in allowed_tokens:
            if 6 <= t <= 16:
                allowed[0, t - 6] = 1

        t0 = time.perf_counter()
        pred_m, pred_tok = self.session.run(
            None,
            {
                "global_root_values": global_root,
                "has_global_root_values": has_gr,
                "local_root_values": local_root,
                "has_local_root_values": has_lr,
                "local_poses": local_poses,
                "has_local_poses": has_lp,
                "allowed_pred_num_tokens": allowed,
            },
        )
        infer_ms = (time.perf_counter() - t0) * 1000.0

        tok_count = int(pred_tok[0])
        n_raw = min(tok_count * MODEL_POSES_PER_TOKEN, pred_m.shape[1])
        n_valid = max(2, n_raw - 2 * MODEL_BOUNDARY_FRAMES)
        raw = pred_m[0, MODEL_BOUNDARY_FRAMES : MODEL_BOUNDARY_FRAMES + n_valid]
        foot_contacts = np.clip(raw[:, -FOOT_CONTACT_COUNT:], 0.0, 1.0).astype(np.float64)

        out_loc_p = np.zeros((n_valid, sk.num_joints, 3), dtype=np.float64)
        out_loc_R = np.zeros((n_valid, sk.num_joints, 3, 3), dtype=np.float64)
        rot_off = 5 + 87

        for f in range(n_valid):
            out_loc_p[f] = sk.ref_local_p
            out_loc_R[f] = sk.ref_local_R
            hips_p = align_p + align_R @ raw[f, 0:3]
            hips_R = orthonormalize(align_R @ rot_from_6d(raw[f, rot_off : rot_off + 3], raw[f, rot_off + 3 : rot_off + 6]))
            out_loc_p[f, 0] = hips_p
            out_loc_R[f, 0] = hips_R

            model_R = [
                rot_from_6d(raw[f, rot_off + mj * 6 : rot_off + mj * 6 + 3], raw[f, rot_off + mj * 6 + 3 : rot_off + mj * 6 + 6])
                for mj in range(len(sk.model_joint_names))
            ]
            for j in range(1, sk.num_joints):
                mj = int(sk.pose_to_model[j])
                if mj < 0:
                    continue
                par_mj = int(sk.pose_to_model[sk.parents[j]])
                out_loc_R[f, j] = orthonormalize(model_R[par_mj].T @ model_R[mj])

        # Exact endpoint delta correction (matching kernels.py lines 245-317)
        if endpoint_correct and n_valid >= 2:
            # Start delta always matches last context frame (ctx[3])
            start_dp = ctx_local_p[3, 0] - out_loc_p[0, 0]
            start_dR = [out_loc_R[0, j].T @ ctx_local_R[3, j] for j in range(sk.num_joints)]
            # End delta matches first target frame if pose is constrained; otherwise root-only!
            end_dp = tgt_local_p[0, 0] - out_loc_p[-1, 0]
            end_dR = [
                (out_loc_R[-1, j].T @ tgt_local_R[0, j]) if (constrain_target_pose or j == 0) else np.eye(3)
                for j in range(sk.num_joints)
            ]
            I3 = np.eye(3)
            for f in range(n_valid):
                ew = f / float(n_valid - 1)
                sw = 1.0 - ew
                out_loc_p[f, 0] = out_loc_p[f, 0] + start_dp * sw + (end_dp * ew if constrain_target_pose else 0.0)
                for j in range(sk.num_joints):
                    if j == 0 and not constrain_target_pose:
                        s_corr = slerp_rot(I3, start_dR[j], sw)
                        out_loc_R[f, j] = orthonormalize(out_loc_R[f, j] @ s_corr)
                    else:
                        s_corr = slerp_rot(I3, start_dR[j], sw)
                        e_corr = slerp_rot(I3, end_dR[j], ew)
                        out_loc_R[f, j] = orthonormalize(out_loc_R[f, j] @ s_corr @ e_corr)

        out_wp = np.zeros((n_valid, sk.num_joints, 3), dtype=np.float64)
        out_wR = np.zeros((n_valid, sk.num_joints, 3, 3), dtype=np.float64)
        for f in range(n_valid):
            out_wp[f], out_wR[f] = sk.fk(out_loc_p[f], out_loc_R[f])

        return GeneratedBrick(
            world_p=out_wp,
            world_R=out_wR,
            local_p=out_loc_p,
            local_R=out_loc_R,
            foot_contacts=foot_contacts,
            inference_ms=infer_ms,
            token_counts=[tok_count],
        )


def soma_to_unity_pos(p_soma: np.ndarray) -> np.ndarray:
    """Convert right-handed SOMA Y-up (+Z fwd, +X left) position to Unity left-handed (+Z fwd, +X right)."""
    out = p_soma.copy()
    out[..., 0] = -out[..., 0]
    return out


def soma_delta_R_to_unity_quat(dR_soma: np.ndarray) -> np.ndarray:
    """Convert SOMA world-space delta 3x3 rotation matrix to Unity left-handed quaternion [x, y, z, w].

    Uses exact reflection conjugation S * dR * S where S = diag(-1, 1, 1), which guarantees
    det(R_unity) == +1 with zero Euler/handedness ambiguity.
    """
    S = np.diag([-1.0, 1.0, 1.0])
    R_unity = orthonormalize(S @ dR_soma @ S)
    # Note: scipy Rotation.from_matrix assumes right-handed active rotation v' = R v.
    # For a left-handed coordinate system (Unity), Unity's Quaternion(qx, qy, qz, qw) applies
    # the exact same matrix v' = R_unity v when (qx, qy, qz, qw) matches Rotation.from_matrix(R_unity)
    # EXCEPT the sign of the imaginary part flips for left-handed cross product!
    # Wait — verify: in Unity, rotating vector (0, 1, 0) by +90 deg around +X gives (0, 0, +1).
    # In Unity left-handed rule: thumb along +X (Right), fingers curl +Y -> +Z!
    # Matrix mapping (0,1,0)->(0,0,1) and (0,0,1)->(0,-1,0) is [[1,0,0],[0,0,-1],[0,1,0]].
    # In right-handed rule, Rotation.from_euler('x', +90 deg) ALSO maps (0,1,0)->(0,0,1) and has matrix [[1,0,0],[0,0,-1],[0,1,0]]
    # and quaternion (sin(45), 0, 0, cos(45))!
    # In Unity: Quaternion.AngleAxis(+90, Vector3.right) is ALSO (+sin(45), 0, 0, cos(45)) and maps (0,1,0)->(0,0,1)!
    # Because Unity's Quaternion multiplication uses the standard Hamilton formula (i*j = k) AND
    # left-handed axes (X x_LH Y = Z matches i*j=k algebraically!), q*v*q^-1 produces the EXACT same
    # 3x3 matrix M(q) as scipy Rotation.from_quat(q).as_matrix()!
    q = Rotation.from_matrix(R_unity).as_quat()  # [x, y, z, w]
    return q / max(float(np.linalg.norm(q)), 1e-12)
