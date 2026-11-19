"""Generate the full 18-clip VANGUARD ZERO Tactical Operator Motion Suite using NVIDIA MotionBricks-SOMA-RP-v1.

Runs real `mmm_v6_soma_fp32.onnx` neural inference locally on Apple Silicon / CPU via
`onnxruntime` using original authored tactical conditioning poses (zero NVIDIA evaluation-only
sample BVH files used), extracts optimal gait cycles via neural foot contacts, upsamples
30 Hz -> 60 Hz, retargets to Unity's left-handed Y-up/+Z-forward Humanoid T-pose skeleton,
and exports:
  - exports/mbclips/*.mbclip.json  (Unity native clip importer format + foot contacts + events)
  - exports/bvh/*.bvh              (Standard 60 Hz Humanoid BVH clips)
  - manifest.json                  (Full machine-readable catalog & verification metrics)
"""

from __future__ import annotations

import json
import math
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Any

import numpy as np
from scipy.interpolate import CubicSpline
from scipy.spatial.transform import Rotation, Slerp

from motionbricks_onnx_engine import (
    MODEL_SAMPLE_RATE,
    UNITY_HUMANOID_BONES,
    MotionBricksSOMAOnnxEngine,
    SOMASkeleton,
    orthonormalize,
    slerp_rot,
    soma_delta_R_to_unity_quat,
    soma_to_unity_pos,
)


ROOT_DIR = Path(__file__).resolve().parents[1]
VENDOR_DIR = ROOT_DIR / "vendor" / "motionbricks"
ONNX_FP32_PATH = VENDOR_DIR / "motionbricks" / "assets" / "onnx" / "soma" / "mmm_v6_soma_fp32.onnx"
SOMA_MANIFEST_PATH = VENDOR_DIR / "motionbricks" / "assets" / "onnx" / "soma" / "soma_n1.json"
SOMA_USD_PATH = VENDOR_DIR / "motionbricks" / "assets" / "human" / "soma_base_skel_minimal_no_root.usd"

EXPORTS_DIR = ROOT_DIR / "exports"
MBCLIPS_DIR = EXPORTS_DIR / "mbclips"
BVH_DIR = EXPORTS_DIR / "bvh"

TARGET_FPS = 60.0


# Canonical Unity Humanoid T-pose bone offsets (in meters, Unity left-handed +X Right, +Y Up, +Z Forward)
# Matches the locked TacticalOperator.fbx rig contract shared with @blender-artist.
UNITY_TPOSE_OFFSETS_M: dict[str, tuple[float, float, float]] = {
    "Hips": (0.0, 0.96, 0.0),
    "Spine": (0.0, 0.11, 0.0),
    "Chest": (0.0, 0.14, 0.0),
    "UpperChest": (0.0, 0.14, 0.0),
    "Neck": (0.0, 0.14, 0.0),
    "Head": (0.0, 0.09, 0.0),
    "LeftShoulder": (-0.06, 0.09, 0.0),
    "LeftUpperArm": (-0.12, 0.0, 0.0),
    "LeftLowerArm": (-0.28, 0.0, 0.0),
    "LeftHand": (-0.26, 0.0, 0.0),
    "RightShoulder": (0.06, 0.09, 0.0),
    "RightUpperArm": (0.12, 0.0, 0.0),
    "RightLowerArm": (0.28, 0.0, 0.0),
    "RightHand": (0.26, 0.0, 0.0),
    "LeftUpperLeg": (-0.10, -0.03, 0.0),
    "LeftLowerLeg": (0.0, -0.43, 0.0),
    "LeftFoot": (0.0, -0.42, 0.0),
    "LeftToes": (0.0, -0.06, 0.13),
    "RightUpperLeg": (0.10, -0.03, 0.0),
    "RightLowerLeg": (0.0, -0.43, 0.0),
    "RightFoot": (0.0, -0.42, 0.0),
    "RightToes": (0.0, -0.06, 0.13),
}


# SOMA world-axis tactical arm pose deltas (relative to T-pose) for combat rifle holds.
# Remember SOMA right-handed world axes: +X=Left, +Y=Up, +Z=Forward.
# LeftArm T-pose points +X (Left): -Z lowers down, -Y swings forward (+Z).
# RightArm T-pose points -X (Right): +Z lowers down, +Y swings forward (+Z).
TACTICAL_ARM_POSES: dict[str, dict[str, tuple[float, float, float]]] = {
    "rifle_ready": {
        "LeftShoulder": (0.0, -10.0, -4.0),
        "LeftArm": (10.0, -48.0, -52.0),
        "LeftForeArm": (-18.0, -58.0, 12.0),
        "LeftHand": (0.0, -15.0, 15.0),
        "RightShoulder": (0.0, 8.0, 4.0),
        "RightArm": (8.0, 44.0, 56.0),
        "RightForeArm": (-24.0, 62.0, -10.0),
        "RightHand": (0.0, 12.0, -12.0),
    },
    "rifle_sprint": {
        "LeftShoulder": (0.0, -14.0, -8.0),
        "LeftArm": (16.0, -42.0, -64.0),
        "LeftForeArm": (-26.0, -64.0, 16.0),
        "LeftHand": (0.0, -20.0, 20.0),
        "RightShoulder": (0.0, 14.0, 8.0),
        "RightArm": (18.0, 34.0, 66.0),
        "RightForeArm": (-32.0, 68.0, -14.0),
        "RightHand": (0.0, 15.0, -18.0),
    },
    "rifle_crouch": {
        "LeftShoulder": (0.0, -12.0, -5.0),
        "LeftArm": (14.0, -52.0, -48.0),
        "LeftForeArm": (-22.0, -56.0, 14.0),
        "LeftHand": (0.0, -12.0, 15.0),
        "RightShoulder": (0.0, 10.0, 5.0),
        "RightArm": (12.0, 48.0, 52.0),
        "RightForeArm": (-28.0, 62.0, -12.0),
        "RightHand": (0.0, 10.0, -12.0),
    },
}


# Original authored tactical keyframe poses in SOMA world-axis Euler deltas (degrees from T-pose).
def make_standing_ready_pose(sk: SOMASkeleton, yaw_deg: float = 0.0, xz: tuple[float, float] = (0.0, 0.0)):
    return sk.compose_pose(
        {
            "Hips": (4.0, -8.0, 0.0),
            "Spine1": (3.0, 4.0, 0.0),
            "Chest": (4.0, 4.0, 0.0),
            "Neck1": (-4.0, 0.0, 0.0),
            "LeftLeg": (-10.0, 4.0, 3.0),
            "LeftShin": (18.0, 0.0, 0.0),
            "LeftFoot": (-8.0, 0.0, -3.0),
            "RightLeg": (-6.0, -8.0, -4.0),
            "RightShin": (16.0, 0.0, 0.0),
            "RightFoot": (-10.0, 0.0, 4.0),
            **TACTICAL_ARM_POSES["rifle_ready"],
        },
        ground_feet=True,
        root_yaw_deg=yaw_deg,
        root_xz=xz,
    )


def make_crouch_ready_pose(sk: SOMASkeleton, yaw_deg: float = 0.0, xz: tuple[float, float] = (0.0, 0.0)):
    return sk.compose_pose(
        {
            "Hips": (10.0, -6.0, 0.0),
            "Spine1": (8.0, 3.0, 0.0),
            "Chest": (6.0, 3.0, 0.0),
            "Neck1": (-16.0, 0.0, 0.0),
            "LeftLeg": (-62.0, 6.0, 4.0),
            "LeftShin": (108.0, 0.0, 0.0),
            "LeftFoot": (-46.0, 0.0, -4.0),
            "RightLeg": (-52.0, -8.0, -4.0),
            "RightShin": (102.0, 0.0, 0.0),
            "RightFoot": (-50.0, 0.0, 4.0),
            **TACTICAL_ARM_POSES["rifle_crouch"],
        },
        ground_feet=True,
        root_yaw_deg=yaw_deg,
        root_xz=xz,
    )


def make_slide_pose(sk: SOMASkeleton, xz: tuple[float, float] = (0.0, 0.0)):
    return sk.compose_pose(
        {
            "Hips": (-16.0, -14.0, -8.0),
            "Spine1": (12.0, 8.0, 4.0),
            "Chest": (14.0, 6.0, 4.0),
            "Neck1": (-10.0, 0.0, 0.0),
            "LeftLeg": (-72.0, 8.0, 6.0),     # lead leg extended forward
            "LeftShin": (24.0, 0.0, 0.0),
            "LeftFoot": (18.0, 0.0, 0.0),
            "RightLeg": (-38.0, -18.0, -14.0), # trailing knee tucked under
            "RightShin": (118.0, 0.0, 0.0),
            "RightFoot": (-25.0, 0.0, 0.0),
            **TACTICAL_ARM_POSES["rifle_ready"],
        },
        ground_feet=False,
        hips_y_override=0.34,
        root_xz=xz,
    )


def make_jump_apex_pose(sk: SOMASkeleton, xz: tuple[float, float] = (0.0, 0.0)):
    return sk.compose_pose(
        {
            "Hips": (12.0, 0.0, 0.0),
            "Spine1": (8.0, 0.0, 0.0),
            "Chest": (8.0, 0.0, 0.0),
            "Neck1": (-14.0, 0.0, 0.0),
            "LeftLeg": (-70.0, 4.0, 5.0),
            "LeftShin": (110.0, 0.0, 0.0),
            "LeftFoot": (-15.0, 0.0, 0.0),
            "RightLeg": (-45.0, -4.0, -5.0),
            "RightShin": (95.0, 0.0, 0.0),
            "RightFoot": (-10.0, 0.0, 0.0),
            **TACTICAL_ARM_POSES["rifle_sprint"],
        },
        ground_feet=False,
        hips_y_override=1.42,
        root_xz=xz,
    )


def make_hit_flinch_pose(sk: SOMASkeleton, xz: tuple[float, float] = (0.0, 0.0)):
    return sk.compose_pose(
        {
            "Hips": (-8.0, -14.0, 5.0),
            "Spine1": (-12.0, -8.0, 6.0),
            "Chest": (-16.0, -10.0, 8.0),
            "Neck1": (-12.0, 6.0, -4.0),
            "Head": (-10.0, 8.0, -4.0),
            "LeftLeg": (-18.0, 6.0, 4.0),
            "LeftShin": (32.0, 0.0, 0.0),
            "LeftFoot": (-14.0, 0.0, -4.0),
            "RightLeg": (-8.0, -10.0, -5.0),
            "RightShin": (22.0, 0.0, 0.0),
            "RightFoot": (-14.0, 0.0, 5.0),
            "LeftShoulder": (0.0, -5.0, 12.0),
            "LeftArm": (-18.0, -32.0, -40.0),
            "LeftForeArm": (-28.0, -45.0, 18.0),
            "RightShoulder": (0.0, 6.0, 10.0),
            "RightArm": (-14.0, 36.0, 44.0),
            "RightForeArm": (-32.0, 52.0, -14.0),
        },
        ground_feet=True,
        root_xz=xz,
    )


def make_death_collapse_mid_pose(sk: SOMASkeleton, xz: tuple[float, float] = (0.0, 0.0)):
    return sk.compose_pose(
        {
            "Hips": (-28.0, -18.0, 16.0),
            "Spine1": (-18.0, -8.0, 10.0),
            "Chest": (-16.0, -6.0, 8.0),
            "Neck1": (-18.0, 10.0, 12.0),
            "LeftLeg": (-42.0, 12.0, 8.0),
            "LeftShin": (95.0, 0.0, 0.0),
            "LeftFoot": (-20.0, 0.0, 0.0),
            "RightLeg": (-22.0, -15.0, -12.0),
            "RightShin": (82.0, 0.0, 0.0),
            "RightFoot": (-15.0, 0.0, 0.0),
            "LeftArm": (-25.0, -15.0, -30.0),
            "LeftForeArm": (-15.0, -25.0, 10.0),
            "RightArm": (-20.0, 18.0, 35.0),
            "RightForeArm": (-18.0, 30.0, -12.0),
        },
        ground_feet=False,
        hips_y_override=0.52,
        root_xz=xz,
    )


def make_death_ground_pose(sk: SOMASkeleton, xz: tuple[float, float] = (0.0, 0.0)):
    return sk.compose_pose(
        {
            "Hips": (-84.0, -22.0, 18.0),
            "Spine1": (-4.0, -4.0, 4.0),
            "Chest": (-4.0, -4.0, 4.0),
            "Neck1": (8.0, 14.0, 10.0),
            "Head": (6.0, 12.0, 8.0),
            "LeftLeg": (12.0, 14.0, 14.0),
            "LeftShin": (26.0, 0.0, 0.0),
            "LeftFoot": (16.0, 0.0, 0.0),
            "RightLeg": (6.0, -16.0, -10.0),
            "RightShin": (18.0, 0.0, 0.0),
            "RightFoot": (14.0, 0.0, 0.0),
            "LeftArm": (10.0, -12.0, -24.0),
            "LeftForeArm": (0.0, -22.0, 8.0),
            "RightArm": (8.0, 22.0, 36.0),
            "RightForeArm": (0.0, 28.0, -10.0),
        },
        ground_feet=False,
        hips_y_override=0.14,
        root_xz=xz,
    )


def build_4frame_token(
    base_loc_p: np.ndarray,
    base_loc_R: np.ndarray,
    vx: float,
    vz: float,
    yaw_rate_rad: float = 0.0,
    start_offset_frames: int = 0,
) -> tuple[np.ndarray, np.ndarray]:
    """Create a 4-frame 30 Hz PoseToken moving at (vx, vz) and yaw_rate_rad."""
    dt = 1.0 / MODEL_SAMPLE_RATE
    ps = np.repeat(base_loc_p[None, :, :], 4, axis=0)
    Rs = np.repeat(base_loc_R[None, :, :, :], 4, axis=0)
    for f in range(4):
        t = (f + start_offset_frames) * dt
        ps[f, 0, 0] += vx * t
        ps[f, 0, 2] += vz * t
        if abs(yaw_rate_rad) > 1e-6:
            R_dy = Rotation.from_euler("y", yaw_rate_rad * t).as_matrix()
            Rs[f, 0] = orthonormalize(R_dy @ base_loc_R[0])
    return ps, Rs


def chain_locomotion(
    engine: MotionBricksSOMAOnnxEngine,
    seed_loc_p: np.ndarray,
    seed_loc_R: np.ndarray,
    vx: float,
    vz: float,
    *,
    num_bricks: int = 5,
    step_horizon_s: float = 0.72,
    constrain_target_pose: bool = False,
    allowed_tokens: tuple[int, ...] = (7, 8, 9, 10),
    hips_target_y: float | None = None,
) -> tuple[np.ndarray, np.ndarray, np.ndarray, float, list[int]]:
    """Chain multiple neural MotionBricks generations into steady-state locomotion."""
    ctx_p, ctx_R = build_4frame_token(seed_loc_p, seed_loc_R, vx, vz, start_offset_frames=-3)
    all_wp, all_wR, all_fc = [], [], []
    total_ms = 0.0
    tokens: list[int] = []

    for b in range(num_bricks):
        last_hips = ctx_p[3, 0].copy()
        tgt_base_p = seed_loc_p.copy()
        tgt_base_p[0, 0] = last_hips[0] + vx * step_horizon_s
        tgt_base_p[0, 2] = last_hips[2] + vz * step_horizon_s
        if hips_target_y is not None:
            tgt_base_p[0, 1] = hips_target_y
        tgt_p, tgt_R = build_4frame_token(tgt_base_p, seed_loc_R, vx, vz, start_offset_frames=0)

        brick = engine.generate(
            ctx_p,
            ctx_R,
            tgt_p,
            tgt_R,
            num_target_poses=4,
            constrain_target_pose=constrain_target_pose,
            constrain_target_root_vel=True,
            allowed_tokens=allowed_tokens,
            endpoint_correct=True,
        )
        # Drop frame 0 of subsequent bricks to avoid duplicate boundary frame
        sl = slice(0, None) if b == 0 else slice(1, None)
        all_wp.append(brick.world_p[sl])
        all_wR.append(brick.world_R[sl])
        all_fc.append(brick.foot_contacts[sl])
        total_ms += brick.inference_ms
        tokens.extend(brick.token_counts)

        ctx_p = brick.local_p[-4:].copy()
        ctx_R = brick.local_R[-4:].copy()

    return (
        np.concatenate(all_wp, axis=0),
        np.concatenate(all_wR, axis=0),
        np.concatenate(all_fc, axis=0),
        total_ms,
        tokens,
    )


def extract_best_stride_cycle(
    sk: SOMASkeleton,
    wp: np.ndarray,
    wR: np.ndarray,
    fc: np.ndarray,
    *,
    warmup_frames: int = 20,
    min_len: int = 18,
    max_len: int = 44,
) -> tuple[np.ndarray, np.ndarray, np.ndarray, tuple[int, int], float]:
    """Find the cleanest two-step cyclic gait window [i, i+L] using 3D pose + foot contact similarity."""
    T = wp.shape[0]
    start_min = min(warmup_frames, max(0, T - min_len - 2))
    key_joints = [
        sk.name_to_idx[n]
        for n in ("Hips", "Chest", "LeftLeg", "LeftShin", "LeftFoot", "RightLeg", "RightShin", "RightFoot")
    ]
    # Root-XZ-centered positions for pose comparison
    rel = wp[:, key_joints, :].copy()
    rel[:, :, 0] -= wp[:, 0:1, 0]
    rel[:, :, 2] -= wp[:, 0:1, 2]

    best_score = float("inf")
    best_window = (start_min, min(T - 1, start_min + min_len))

    for i in range(start_min, max(start_min + 1, T - min_len)):
        for L in range(min_len, min(max_len, T - i)):
            j = i + L
            pose_err = float(np.mean(np.sum((rel[j] - rel[i]) ** 2, axis=-1)))
            fc_err = float(np.mean((fc[j] - fc[i]) ** 2))
            # Check velocity match at endpoints too
            if i > 0 and j < T - 1:
                v_i = rel[i + 1] - rel[i - 1]
                v_j = rel[j] - rel[j - 2]
                vel_err = float(np.mean(np.sum((v_j - v_i) ** 2, axis=-1)))
            else:
                vel_err = 0.0
            score = pose_err + 0.25 * fc_err + 0.5 * vel_err
            if score < best_score:
                best_score = score
                best_window = (i, j)

    i0, i1 = best_window
    return wp[i0 : i1 + 1].copy(), wR[i0 : i1 + 1].copy(), fc[i0 : i1 + 1].copy(), best_window, best_score


def convert_soma_seq_to_unity_deltas(
    sk: SOMASkeleton,
    wp: np.ndarray,
    wR: np.ndarray,
    fc: np.ndarray,
    *,
    is_loop: bool,
    strip_horizontal_root: bool,
    arm_style: str | None = None,
    arm_blend_weight: float = 0.78,
    torso_pitch_bias_deg: float = 0.0,
) -> dict[str, Any]:
    """Retarget SOMA 30 Hz world transforms -> Unity 60 Hz left-handed world-delta quaternions + Hips pos."""
    T_30 = wp.shape[0]
    dt_30 = 1.0 / MODEL_SAMPLE_RATE

    # Measure true neural horizontal velocity BEFORE stripping root travel
    disp_soma = wp[-1, 0] - wp[0, 0]
    dur_30 = max((T_30 - 1) * dt_30, dt_30)
    avg_vel_soma = np.array([disp_soma[0] / dur_30, 0.0, disp_soma[2] / dur_30], dtype=np.float64)
    avg_vel_unity = soma_to_unity_pos(avg_vel_soma)

    # Strip linear horizontal travel for in-place Unity locomotion clips (keeping neural hip bob & sway!)
    hips_p_soma = wp[:, 0, :].copy()
    if strip_horizontal_root:
        t_frac = np.linspace(0.0, 1.0, T_30)
        lin_x = hips_p_soma[0, 0] + (hips_p_soma[-1, 0] - hips_p_soma[0, 0]) * t_frac
        lin_z = hips_p_soma[0, 2] + (hips_p_soma[-1, 2] - hips_p_soma[0, 2]) * t_frac
        hips_p_soma[:, 0] -= lin_x
        hips_p_soma[:, 2] -= lin_z
    else:
        hips_p_soma[:, 0] -= hips_p_soma[0, 0]
        hips_p_soma[:, 2] -= hips_p_soma[0, 2]

    # Ground-safety lift: if any foot dips below ankle ground clearance on non-death/non-slide clips,
    # lift Hips gently so feet stay cleanly grounded on Y >= 0.0.
    lf_min_y = float(wp[:, sk.left_foot_idx, 1].min())
    rf_min_y = float(wp[:, sk.right_foot_idx, 1].min())
    min_ankle_y = min(lf_min_y, rf_min_y)
    if min_ankle_y < 0.055 and hips_p_soma[:, 1].min() > 0.45:
        lift = 0.068 - min_ankle_y
        hips_p_soma[:, 1] += lift

    # Scale Hips height ratio from SOMA stand height (~1.012m) to Unity TacticalOperator stand height (0.96m)
    height_scale = 0.96 / sk.hips_stand_y
    hips_p_unity_30 = soma_to_unity_pos(hips_p_soma) * height_scale

    # Precompute tactical arm target world deltas (in SOMA space) if requested
    arm_target_wR: dict[str, np.ndarray] = {}
    if arm_style is not None and arm_style in TACTICAL_ARM_POSES:
        _, _, _, arm_wR_pose = sk.compose_pose(TACTICAL_ARM_POSES[arm_style], ground_feet=False)
        for u_name, _, s_name, _ in UNITY_HUMANOID_BONES:
            if u_name in (
                "LeftShoulder", "LeftUpperArm", "LeftLowerArm", "LeftHand",
                "RightShoulder", "RightUpperArm", "RightLowerArm", "RightHand",
            ):
                sj = sk.name_to_idx[s_name]
                arm_target_wR[u_name] = arm_wR_pose[sj] @ sk.tpose_world_R[sj].T

    # Precompute continuous neural mean rotation per arm bone so tactical rifle arms
    # carry genuine MotionBricks neural stride momentum with ZERO antipodal slerp flips!
    bone_names = [b[0] for b in UNITY_HUMANOID_BONES]
    num_bones = len(bone_names)
    arm_bone_set = {
        "LeftShoulder", "LeftUpperArm", "LeftLowerArm", "LeftHand",
        "RightShoulder", "RightUpperArm", "RightLowerArm", "RightHand",
    }
    raw_dR_soma = np.zeros((T_30, num_bones, 3, 3), dtype=np.float64)
    for f in range(T_30):
        for bi, (u_name, _, s_name, extra_s) in enumerate(UNITY_HUMANOID_BONES):
            sj = sk.name_to_idx[s_name]
            dR = wR[f, sj] @ sk.tpose_world_R[sj].T
            if extra_s is not None:
                sj2 = sk.name_to_idx[extra_s]
                dR2 = wR[f, sj2] @ sk.tpose_world_R[sj2].T
                dR = slerp_rot(dR, dR2, 0.5)
            raw_dR_soma[f, bi] = orthonormalize(dR)

    # Smooth any isolated 1-frame neural decoder twist spikes (> 28 deg at 30 Hz) before retargeting
    for bi in range(num_bones):
        q_seq = np.array([Rotation.from_matrix(raw_dR_soma[f, bi]).as_quat() for f in range(T_30)])
        for f in range(1, T_30):
            if float(np.dot(q_seq[f - 1], q_seq[f])) < 0.0:
                q_seq[f] = -q_seq[f]
        # Iterative geodesic median / clamping filter for any frame jumping > 26 deg @ 30 Hz
        for _ in range(3):
            for f in range(1, T_30 - 1):
                q_mid = q_seq[f - 1] + q_seq[f + 1]
                n_mid = float(np.linalg.norm(q_mid))
                if n_mid < 1e-8:
                    continue
                q_mid /= n_mid
                dot_prev = abs(float(np.clip(np.dot(q_seq[f - 1], q_seq[f]), -1.0, 1.0)))
                dot_next = abs(float(np.clip(np.dot(q_seq[f], q_seq[f + 1]), -1.0, 1.0)))
                ang_prev = 2.0 * math.degrees(math.acos(dot_prev))
                ang_next = 2.0 * math.degrees(math.acos(dot_next))
                if ang_prev > 26.0 or ang_next > 26.0:
                    q_seq[f] = q_mid
            # Also smooth sharp transitions by gentle 3-tap spherical filter on upper-body joints
            if bone_names[bi] in arm_bone_set:
                q_new = q_seq.copy()
                for f in range(1, T_30 - 1):
                    avg = 0.25 * q_seq[f - 1] + 0.50 * q_seq[f] + 0.25 * q_seq[f + 1]
                    q_new[f] = avg / max(float(np.linalg.norm(avg)), 1e-12)
                q_seq = q_new
        for f in range(T_30):
            raw_dR_soma[f, bi] = orthonormalize(Rotation.from_quat(q_seq[f]).as_matrix())

    # Compute mean neural arm orientation (in chest-local space) for flip-free residual momentum
    arm_mean_in_chest: dict[int, np.ndarray] = {}
    chest_soma_idx = sk.name_to_idx["Chest"]
    chest_deltas = np.array([orthonormalize(wR[f, chest_soma_idx] @ sk.tpose_world_R[chest_soma_idx].T) for f in range(T_30)])
    for bi, u_name in enumerate(bone_names):
        if u_name in arm_target_wR:
            qs = []
            for f in range(T_30):
                rel_R = orthonormalize(chest_deltas[f].T @ raw_dR_soma[f, bi])
                q = Rotation.from_matrix(rel_R).as_quat()
                if qs and float(np.dot(qs[0], q)) < 0.0:
                    q = -q
                qs.append(q)
            q_mean = np.mean(qs, axis=0)
            q_mean /= max(float(np.linalg.norm(q_mean)), 1e-12)
            arm_mean_in_chest[bi] = orthonormalize(Rotation.from_quat(q_mean).as_matrix())

    delta_R_soma_30 = np.zeros((T_30, num_bones, 3, 3), dtype=np.float64)
    R_torso_bias = (
        Rotation.from_euler("x", torso_pitch_bias_deg, degrees=True).as_matrix()
        if abs(torso_pitch_bias_deg) > 1e-4
        else np.eye(3)
    )

    for f in range(T_30):
        chest_delta = chest_deltas[f]
        for bi, u_name in enumerate(bone_names):
            dR = raw_dR_soma[f, bi]

            if u_name in ("Spine", "Chest", "UpperChest") and abs(torso_pitch_bias_deg) > 1e-4:
                frac = 0.35 if u_name == "Spine" else (0.70 if u_name == "Chest" else 1.0)
                dR = slerp_rot(np.eye(3), R_torso_bias, frac) @ dR

            if u_name in arm_target_wR:
                # Attach tactical rifle arm pose to live neural chest + smoothly bounded neural residual sway
                # Using exponential map (rotvec) with smooth tanh bound eliminates 180-deg antipodal flips!
                rel_neural = orthonormalize(chest_delta.T @ dR)
                sway_delta = orthonormalize(arm_mean_in_chest[bi].T @ rel_neural)
                rv = Rotation.from_matrix(sway_delta).as_rotvec()
                ang = float(np.linalg.norm(rv))
                if ang > 1e-6:
                    sway_weight = (1.0 - arm_blend_weight) * (1.0 if "Shoulder" in u_name else 0.70)
                    max_rad = math.radians(18.0)
                    scaled_ang = max_rad * math.tanh((ang * sway_weight) / max_rad)
                    sway_clamped = Rotation.from_rotvec(rv * (scaled_ang / ang)).as_matrix()
                else:
                    sway_clamped = np.eye(3)
                dR = orthonormalize(chest_delta @ arm_target_wR[u_name] @ sway_clamped)

            delta_R_soma_30[f, bi] = orthonormalize(dR)

    # Seamless cyclic loop closure over final 5 frames if looping
    if is_loop and T_30 >= 12:
        blend_n = min(6, T_30 // 4)
        hips_delta = hips_p_unity_30[0] - hips_p_unity_30[-1]
        fc_delta = fc[0] - fc[-1]
        for k in range(blend_n):
            alpha = 0.5 * (1.0 - math.cos(math.pi * (k + 1) / blend_n))
            idx = T_30 - blend_n + k
            hips_p_unity_30[idx] += hips_delta * alpha
            fc[idx] = np.clip(fc[idx] + fc_delta * alpha, 0.0, 1.0)
            for bi in range(num_bones):
                d_seam = delta_R_soma_30[-1, bi].T @ delta_R_soma_30[0, bi]
                corr = slerp_rot(np.eye(3), d_seam, alpha)
                delta_R_soma_30[idx, bi] = orthonormalize(delta_R_soma_30[idx, bi] @ corr)
        # Guarantee exact frame[0] == frame[-1]
        hips_p_unity_30[-1] = hips_p_unity_30[0]
        fc[-1] = fc[0]
        delta_R_soma_30[-1] = delta_R_soma_30[0]

    # Convert to Unity left-handed quaternions [T_30, num_bones, 4] (xyzw) with hemisphere continuity
    quats_30 = np.zeros((T_30, num_bones, 4), dtype=np.float64)
    for bi in range(num_bones):
        for f in range(T_30):
            q = soma_delta_R_to_unity_quat(delta_R_soma_30[f, bi])
            if f > 0 and float(np.dot(quats_30[f - 1, bi], q)) < 0.0:
                q = -q
            quats_30[f, bi] = q

    # Upsample 30 Hz -> 60 Hz cleanly (CubicSpline for positions & contacts, Slerp for rotations)
    t_30 = np.arange(T_30, dtype=np.float64) / MODEL_SAMPLE_RATE
    duration = float(t_30[-1])
    T_60 = max(2, int(round(duration * TARGET_FPS)) + 1)
    t_60 = np.linspace(0.0, duration, T_60, dtype=np.float64)

    bc = "periodic" if is_loop else "natural"
    hips_p_60 = np.stack([CubicSpline(t_30, hips_p_unity_30[:, c], bc_type=bc)(t_60) for c in range(3)], axis=-1)
    fc_60 = np.clip(
        np.stack([CubicSpline(t_30, fc[:, c], bc_type=bc)(t_60) for c in range(4)], axis=-1),
        0.0,
        1.0,
    )

    quats_60 = np.zeros((T_60, num_bones, 4), dtype=np.float64)
    for bi in range(num_bones):
        rots = Rotation.from_quat(quats_30[:, bi])
        q_interp = Slerp(t_30, rots)(t_60).as_quat()
        for f in range(T_60):
            q = q_interp[f] / max(float(np.linalg.norm(q_interp[f])), 1e-12)
            if f > 0 and float(np.dot(quats_60[f - 1, bi], q)) < 0.0:
                q = -q
            quats_60[f, bi] = q
        if is_loop:
            if float(np.dot(quats_60[0, bi], quats_60[-1, bi])) < 0.0:
                quats_60[-1, bi] = -quats_60[0, bi]
            else:
                quats_60[-1, bi] = quats_60[0, bi]

    # Geodesic smoothness guard at 60 Hz: gently smooth any localized >16 deg/frame brick-seam jumps
    for bi in range(num_bones):
        for _ in range(8):
            changed = False
            for f in range(T_60 - 1):
                dot = float(np.clip(np.dot(quats_60[f, bi], quats_60[f + 1, bi]), -1.0, 1.0))
                ang_deg = 2.0 * math.degrees(math.acos(abs(dot)))
                if ang_deg > 16.0:
                    changed = True
                    # Light zero-phase diffusion around the high-velocity transition
                    lo = max(0, f - 2)
                    hi = min(T_60 - 1, f + 3)
                    q_copy = quats_60[:, bi].copy()
                    for k in range(lo, hi + 1):
                        if is_loop and (k == 0 or k == T_60 - 1):
                            continue
                        km = (k - 1) % (T_60 - 1) if is_loop else max(0, k - 1)
                        kp = (k + 1) % (T_60 - 1) if is_loop else min(T_60 - 1, k + 1)
                        q_m = q_copy[km] if float(np.dot(q_copy[km], q_copy[k])) >= 0.0 else -q_copy[km]
                        q_p = q_copy[kp] if float(np.dot(q_copy[kp], q_copy[k])) >= 0.0 else -q_copy[kp]
                        avg = 0.25 * q_m + 0.50 * q_copy[k] + 0.25 * q_p
                        quats_60[k, bi] = avg / max(float(np.linalg.norm(avg)), 1e-12)
            if not changed:
                break
        # Re-enforce positive hemisphere continuity and exact loop closure
        for f in range(1, T_60):
            if float(np.dot(quats_60[f - 1, bi], quats_60[f, bi])) < 0.0:
                quats_60[f, bi] = -quats_60[f, bi]
        if is_loop:
            quats_60[-1, bi] = quats_60[0, bi] if float(np.dot(quats_60[-2, bi], quats_60[0, bi])) >= 0.0 else -quats_60[0, bi]

    # Extract neural footstep audio events from Left/Right foot contacts
    left_c = np.maximum(fc_60[:, 0], fc_60[:, 1])
    right_c = np.maximum(fc_60[:, 2], fc_60[:, 3])
    footstep_events = extract_footstep_events(left_c, right_c, TARGET_FPS, is_loop=is_loop)

    return {
        "fps": TARGET_FPS,
        "frameCount": int(T_60),
        "duration": round(duration, 4),
        "isLoop": is_loop,
        "averageVelocity": [round(float(avg_vel_unity[0]), 4), 0.0, round(float(avg_vel_unity[2]), 4)],
        "averageSpeed": round(float(math.hypot(avg_vel_unity[0], avg_vel_unity[2])), 4),
        "boneNames": bone_names,
        "hipsPositions": np.round(hips_p_60, 6).tolist(),
        "worldDeltaQuats": np.round(quats_60, 6).tolist(),  # [frame][bone][x,y,z,w] in Unity LH space
        "footContacts": np.round(fc_60, 4).tolist(),        # [frame][LHeel, LToe, RHeel, RToe]
        "footstepEvents": footstep_events,
    }


def extract_footstep_events(
    left_c: np.ndarray,
    right_c: np.ndarray,
    fps: float,
    *,
    is_loop: bool,
    threshold: float = 0.52,
    min_gap_s: float = 0.24,
) -> list[dict[str, Any]]:
    """Detect clean heel/toe plant events from MotionBricks neural foot contact curves."""
    events: list[dict[str, Any]] = []
    min_gap_frames = int(round(min_gap_s * fps))
    for foot_label, curve in (("Left", left_c), ("Right", right_c)):
        # Light 5-frame box filter to remove single-frame threshold chatter
        kernel = np.ones(5, dtype=np.float64) / 5.0
        smooth = np.convolve(np.pad(curve, 2, mode="wrap" if is_loop else "edge"), kernel, mode="valid")
        last_idx = -min_gap_frames - 1
        for f in range(1, len(smooth)):
            if smooth[f - 1] < threshold <= smooth[f] and (f - last_idx) >= min_gap_frames:
                events.append({
                    "time": round(f / fps, 4),
                    "normalizedTime": round(f / max(len(smooth) - 1, 1), 4),
                    "foot": foot_label,
                    "intensity": round(float(np.clip(smooth[f : min(f + 6, len(smooth))].max(), 0.4, 1.0)), 3),
                })
                last_idx = f
    events.sort(key=lambda e: e["time"])
    return events


def write_bvh_file(out_path: Path, clip_data: dict[str, Any]) -> None:
    """Export standard 60 Hz Humanoid BVH on the canonical Unity Y-up/+Z-forward T-pose skeleton."""
    bone_names: list[str] = clip_data["boneNames"]
    parent_map = {b[0]: b[1] for b in UNITY_HUMANOID_BONES}
    children_map: dict[str, list[str]] = {n: [] for n in bone_names}
    for n in bone_names:
        p = parent_map[n]
        if p is not None:
            children_map[p].append(n)

    # Emit clean HIERARCHY block in centimeters (standard BVH convention)
    lines: list[str] = ["HIERARCHY"]
    order: list[str] = []

    def emit_joint(name: str, depth: int) -> None:
        indent = "  " * depth
        kind = "ROOT" if depth == 0 else "JOINT"
        order.append(name)
        ox, oy, oz = UNITY_TPOSE_OFFSETS_M[name]
        # Convert Unity LH (+X Right) -> standard BVH RH (+X Left) for Blender compatibility
        lines.append(f"{indent}{kind} {name}")
        lines.append(f"{indent}{{")
        lines.append(f"{indent}  OFFSET {-ox * 100.0:.4f} {oy * 100.0:.4f} {oz * 100.0:.4f}")
        if depth == 0:
            lines.append(f"{indent}  CHANNELS 6 Xposition Yposition Zposition Zrotation Xrotation Yrotation")
        else:
            lines.append(f"{indent}  CHANNELS 3 Zrotation Xrotation Yrotation")
        if children_map[name]:
            for ch in children_map[name]:
                emit_joint(ch, depth + 1)
        else:
            # Leaf End Site
            ex, ey, ez = (0.0, 0.14, 0.0) if name == "Head" else ((0.0, 0.0, 0.07) if "Toes" in name else (0.08 if "Left" in name else -0.08, 0.0, 0.0))
            lines.append(f"{indent}  End Site")
            lines.append(f"{indent}  {{")
            lines.append(f"{indent}    OFFSET {ex * 100.0:.4f} {ey * 100.0:.4f} {ez * 100.0:.4f}")
            lines.append(f"{indent}  }}")
        lines.append(f"{indent}}}")

    emit_joint("Hips", 0)

    T = clip_data["frameCount"]
    fps = clip_data["fps"]
    hips_pos = np.asarray(clip_data["hipsPositions"], dtype=np.float64)
    world_q = np.asarray(clip_data["worldDeltaQuats"], dtype=np.float64)
    name_to_bi = {n: i for i, n in enumerate(bone_names)}
    S = np.diag([-1.0, 1.0, 1.0])

    lines.append("MOTION")
    lines.append(f"Frames: {T}")
    lines.append(f"Frame Time: {1.0 / fps:.6f}")

    for f in range(T):
        # Convert Unity world delta rotations back to standard RH world rotations for BVH
        wR_rh = [
            orthonormalize(S @ Rotation.from_quat(world_q[f, name_to_bi[n]]).as_matrix() @ S)
            for n in bone_names
        ]
        vals: list[str] = []
        for name in order:
            bi = name_to_bi[name]
            p_name = parent_map[name]
            if p_name is None:
                px, py, pz = hips_pos[f]
                vals.extend([f"{-px * 100.0:.4f}", f"{py * 100.0:.4f}", f"{pz * 100.0:.4f}"])
                loc_R = wR_rh[bi]
            else:
                p_bi = name_to_bi[p_name]
                loc_R = orthonormalize(wR_rh[p_bi].T @ wR_rh[bi])
            rz, rx, ry = Rotation.from_matrix(loc_R).as_euler("ZXY", degrees=True)
            vals.extend([f"{rz:.4f}", f"{rx:.4f}", f"{ry:.4f}"])
        lines.append(" ".join(vals))

    out_path.write_text("\n".join(lines) + "\n", encoding="utf-8")


@dataclass
class ClipSpec:
    name: str
    category: str
    description: str
    is_loop: bool
    recommended_blend_in_s: float
    layer: str


def generate_all() -> None:
    MBCLIPS_DIR.mkdir(parents=True, exist_ok=True)
    BVH_DIR.mkdir(parents=True, exist_ok=True)

    t_start = time.perf_counter()
    print(f"[MotionBricks] Loading SOMA USD skeleton from {SOMA_USD_PATH.name}...")
    sk = SOMASkeleton(SOMA_USD_PATH, SOMA_MANIFEST_PATH)
    print(f"[MotionBricks] Loading FP32 ONNX model from {ONNX_FP32_PATH.name}...")
    engine = MotionBricksSOMAOnnxEngine(ONNX_FP32_PATH, sk)

    stand_lp, stand_lR, _, _ = make_standing_ready_pose(sk)
    crouch_lp, crouch_lR, _, _ = make_crouch_ready_pose(sk)

    manifest_clips: list[dict[str, Any]] = []
    total_infer_ms = 0.0
    total_bricks = 0

    def save_clip(
        spec: ClipSpec,
        wp: np.ndarray,
        wR: np.ndarray,
        fc: np.ndarray,
        infer_ms: float,
        tokens: list[int],
        *,
        strip_horizontal_root: bool,
        arm_style: str | None,
        arm_blend: float,
        torso_pitch_bias_deg: float = 0.0,
        cycle_meta: dict[str, Any] | None = None,
    ) -> None:
        nonlocal total_infer_ms, total_bricks
        total_infer_ms += infer_ms
        total_bricks += len(tokens)

        clip_data = convert_soma_seq_to_unity_deltas(
            sk,
            wp,
            wR,
            fc,
            is_loop=spec.is_loop,
            strip_horizontal_root=strip_horizontal_root,
            arm_style=arm_style,
            arm_blend_weight=arm_blend,
            torso_pitch_bias_deg=torso_pitch_bias_deg,
        )
        clip_data["name"] = spec.name
        clip_data["category"] = spec.category
        clip_data["description"] = spec.description
        clip_data["recommendedBlendInSeconds"] = spec.recommended_blend_in_s
        clip_data["layer"] = spec.layer
        clip_data["generator"] = {
            "model": "MotionBricks-SOMA-RP-v1",
            "onnxFile": "mmm_v6_soma_fp32.onnx",
            "license": "NVIDIA Open Model Agreement",
            "inferenceMs": round(infer_ms, 2),
            "bricksChained": len(tokens),
            "predictedTokens": tokens,
            **(cycle_meta or {}),
        }

        mb_path = MBCLIPS_DIR / f"{spec.name}.mbclip.json"
        bvh_path = BVH_DIR / f"{spec.name}.bvh"
        mb_path.write_text(json.dumps(clip_data, separators=(",", ":")), encoding="utf-8")
        write_bvh_file(bvh_path, clip_data)

        manifest_clips.append({
            "name": spec.name,
            "category": spec.category,
            "layer": spec.layer,
            "description": spec.description,
            "isLoop": spec.is_loop,
            "fps": clip_data["fps"],
            "frameCount": clip_data["frameCount"],
            "duration": clip_data["duration"],
            "averageVelocity": clip_data["averageVelocity"],
            "averageSpeed": clip_data["averageSpeed"],
            "recommendedBlendInSeconds": spec.recommended_blend_in_s,
            "footstepEvents": clip_data["footstepEvents"],
            "mbclipPath": f"exports/mbclips/{spec.name}.mbclip.json",
            "bvhPath": f"exports/bvh/{spec.name}.bvh",
            "neuralMetrics": clip_data["generator"],
        })
        print(
            f"  [OK] {spec.name:28s} | {clip_data['frameCount']:3d}f ({clip_data['duration']:.2f}s @60Hz) "
            f"| speed={clip_data['averageSpeed']:.2f}m/s | steps={len(clip_data['footstepEvents']):2d} "
            f"| ONNX={infer_ms:5.1f}ms ({len(tokens)} bricks)"
        )

    # =========================================================================
    # 1. STANDING & CROUCH IDLE LOOPS (Neural weight-shift breathing cycles)
    # =========================================================================
    for spec, base_lp, base_lR, sway_x, arm_st in [
        (
            ClipSpec("Tactical_Idle_Aim", "locomotion", "Alert standing combat-ready idle with subtle neural weight shift & breathing", True, 0.18, "Base"),
            stand_lp, stand_lR, 0.035, "rifle_ready",
        ),
        (
            ClipSpec("Tactical_Crouch_Idle", "crouch", "Low-profile tactical crouch idle behind cover with subtle balance shift", True, 0.16, "Base"),
            crouch_lp, crouch_lR, 0.025, "rifle_crouch",
        ),
    ]:
        # Generate a 2-brick gentle sway left -> right -> center via MotionBricks neural inbetweening
        p_mid = base_lp.copy(); p_mid[0, 0] += sway_x; p_mid[0, 1] -= 0.012
        ctx_p, ctx_R = build_4frame_token(base_lp, base_lR, 0.0, 0.0)
        tgt_p1, tgt_R1 = build_4frame_token(p_mid, base_lR, 0.0, 0.0)
        b1 = engine.generate(ctx_p, ctx_R, tgt_p1, tgt_R1, constrain_target_pose=True, allowed_tokens=(8, 9, 10))
        tgt_p2, tgt_R2 = build_4frame_token(base_lp, base_lR, 0.0, 0.0)
        b2 = engine.generate(b1.local_p[-4:], b1.local_R[-4:], tgt_p2, tgt_R2, constrain_target_pose=True, allowed_tokens=(8, 9, 10))
        wp = np.concatenate([b1.world_p, b2.world_p[1:]], axis=0)
        wR = np.concatenate([b1.world_R, b2.world_R[1:]], axis=0)
        fc = np.concatenate([b1.foot_contacts, b2.foot_contacts[1:]], axis=0)
        save_clip(
            spec, wp, wR, fc, b1.inference_ms + b2.inference_ms, b1.token_counts + b2.token_counts,
            strip_horizontal_root=False, arm_style=arm_st, arm_blend=0.82,
        )

    # =========================================================================
    # 2. STANDING DIRECTIONAL LOCOMOTION & SPRINT (Chained Neural Rollouts)
    # =========================================================================
    standing_gaits = [
        ("Tactical_Walk_Fwd", "locomotion", "Tactical forward advance walk (~2.0 m/s) with weapon raised", 0.0, 2.2, 0.94, 18, 42, "rifle_ready", 0.80, 4.0),
        ("Tactical_Walk_Bwd", "locomotion", "Tactical combat backpedal (~1.6 m/s) keeping threat forward", 0.0, -1.8, 0.93, 18, 42, "rifle_ready", 0.80, 2.0),
        ("Tactical_Strafe_Left", "locomotion", "Combat lateral strafe left (~2.2 m/s) for corner peeking", 2.3, 0.0, 0.93, 18, 42, "rifle_ready", 0.80, 4.0),
        ("Tactical_Strafe_Right", "locomotion", "Combat lateral strafe right (~2.2 m/s) for corner peeking", -2.3, 0.0, 0.93, 18, 42, "rifle_ready", 0.80, 4.0),
        ("Tactical_Jog_Fwd", "locomotion", "Brisk combat jog (~3.2 m/s) for mid-pace repositioning & AI patrol", 0.0, 3.4, 0.92, 16, 36, "rifle_ready", 0.72, 8.0),
        ("Tactical_Sprint_Fwd", "locomotion", "Full athletic tactical sprint (~4.8 m/s) with low-ready cross-chest rifle carry", 0.0, 4.8, 0.90, 15, 34, "rifle_sprint", 0.65, 14.0),
    ]
    for name, cat, desc, vx, vz, hy, min_l, max_l, arm_st, arm_b, pitch_bias in standing_gaits:
        wp_raw, wR_raw, fc_raw, ms, toks = chain_locomotion(
            engine, stand_lp, stand_lR, vx, vz,
            num_bricks=5, step_horizon_s=0.72, constrain_target_pose=False,
            allowed_tokens=(7, 8, 9, 10), hips_target_y=hy,
        )
        wp_c, wR_c, fc_c, win, score = extract_best_stride_cycle(
            sk, wp_raw, wR_raw, fc_raw, warmup_frames=22, min_len=min_l, max_len=max_l,
        )
        save_clip(
            ClipSpec(name, cat, desc, True, 0.14, "Base"),
            wp_c, wR_c, fc_c, ms, toks,
            strip_horizontal_root=True, arm_style=arm_st, arm_blend=arm_b,
            torso_pitch_bias_deg=pitch_bias,
            cycle_meta={"extractedWindow30Hz": list(win), "cyclePoseError": round(score, 5)},
        )

    # =========================================================================
    # 3. CROUCH DIRECTIONAL LOCOMOTION (Chained Neural Crouch Rollouts)
    # =========================================================================
    crouch_gaits = [
        ("Tactical_Crouch_Walk_Fwd", "crouch", "Stealth forward crouch-walk (~1.5 m/s) behind low cover", 0.0, 1.6, 18, 42),
        ("Tactical_Crouch_Walk_Bwd", "crouch", "Stealth backward crouch-walk (~1.3 m/s)", 0.0, -1.4, 18, 42),
        ("Tactical_Crouch_Strafe_Left", "crouch", "Crouch lateral strafe left (~1.5 m/s) for low-cover peeking", 1.6, 0.0, 18, 42),
        ("Tactical_Crouch_Strafe_Right", "crouch", "Crouch lateral strafe right (~1.5 m/s) for low-cover peeking", -1.6, 0.0, 18, 42),
    ]
    crouch_hy = float(crouch_lp[0, 1])
    for name, cat, desc, vx, vz, min_l, max_l in crouch_gaits:
        # Use target pose conditioning softly via crouch seed so neural model stays cleanly crouched!
        wp_raw, wR_raw, fc_raw, ms, toks = chain_locomotion(
            engine, crouch_lp, crouch_lR, vx, vz,
            num_bricks=4, step_horizon_s=0.68, constrain_target_pose=True,
            allowed_tokens=(7, 8, 9), hips_target_y=crouch_hy,
        )
        wp_c, wR_c, fc_c, win, score = extract_best_stride_cycle(
            sk, wp_raw, wR_raw, fc_raw, warmup_frames=16, min_len=min_l, max_len=max_l,
        )
        save_clip(
            ClipSpec(name, cat, desc, True, 0.16, "Base"),
            wp_c, wR_c, fc_c, ms, toks,
            strip_horizontal_root=True, arm_style="rifle_crouch", arm_blend=0.80,
            torso_pitch_bias_deg=4.0,
            cycle_meta={"extractedWindow30Hz": list(win), "cyclePoseError": round(score, 5)},
        )

    # =========================================================================
    # 4. TACTICAL ONE-SHOT ACTIONS (Neural Inbetweening Between Tactical Key Poses)
    # =========================================================================

    # 4a. Tactical_Slide: Sprint entry -> low knee-slide -> crouch-ready exit
    slide_mid_lp, slide_mid_lR, _, _ = make_slide_pose(sk, xz=(0.0, 1.35))
    slide_end_lp, slide_end_lR, _, _ = make_crouch_ready_pose(sk, xz=(0.0, 2.50))
    ctx_p, ctx_R = build_4frame_token(stand_lp, stand_lR, 0.0, 4.2, start_offset_frames=-3)
    t1_p, t1_R = build_4frame_token(slide_mid_lp, slide_mid_lR, 0.0, 3.0)
    b1 = engine.generate(ctx_p, ctx_R, t1_p, t1_R, constrain_target_pose=True, allowed_tokens=(6, 7))
    t2_p, t2_R = build_4frame_token(slide_end_lp, slide_end_lR, 0.0, 0.8)
    b2 = engine.generate(b1.local_p[-4:], b1.local_R[-4:], t2_p, t2_R, constrain_target_pose=True, allowed_tokens=(6, 7))
    save_clip(
        ClipSpec("Tactical_Slide", "action", "High-speed tactical knee-slide from sprint into low crouch", False, 0.10, "Base"),
        np.concatenate([b1.world_p, b2.world_p[1:]], axis=0),
        np.concatenate([b1.world_R, b2.world_R[1:]], axis=0),
        np.concatenate([b1.foot_contacts, b2.foot_contacts[1:]], axis=0),
        b1.inference_ms + b2.inference_ms, b1.token_counts + b2.token_counts,
        strip_horizontal_root=True, arm_style="rifle_ready", arm_blend=0.72,
    )

    # 4b. Tactical_Jump_Vault: Takeoff -> tucked mid-air vault apex -> cushioned landing
    apex_lp, apex_lR, _, _ = make_jump_apex_pose(sk, xz=(0.0, 0.90))
    land_lp, land_lR, _, _ = make_crouch_ready_pose(sk, xz=(0.0, 1.85))
    land_lp[0, 1] = 0.80  # athletic landing absorption
    ctx_p, ctx_R = build_4frame_token(stand_lp, stand_lR, 0.0, 2.6, start_offset_frames=-3)
    t1_p, t1_R = build_4frame_token(apex_lp, apex_lR, 0.0, 2.4)
    b1 = engine.generate(ctx_p, ctx_R, t1_p, t1_R, constrain_target_pose=True, allowed_tokens=(6,))
    t2_p, t2_R = build_4frame_token(land_lp, land_lR, 0.0, 1.5)
    b2 = engine.generate(b1.local_p[-4:], b1.local_R[-4:], t2_p, t2_R, constrain_target_pose=True, allowed_tokens=(6,))
    save_clip(
        ClipSpec("Tactical_Jump_Vault", "action", "Explosive tactical jump / low-barrier vault with tucked knees and absorbed landing", False, 0.08, "Base"),
        np.concatenate([b1.world_p, b2.world_p[1:]], axis=0),
        np.concatenate([b1.world_R, b2.world_R[1:]], axis=0),
        np.concatenate([b1.foot_contacts, b2.foot_contacts[1:]], axis=0),
        b1.inference_ms + b2.inference_ms, b1.token_counts + b2.token_counts,
        strip_horizontal_root=True, arm_style="rifle_sprint", arm_blend=0.55,
    )

    # 4c & 4d. Tactical_Turn_Left_90 & Tactical_Turn_Right_90
    for name, yaw_deg, desc in [
        ("Tactical_Turn_Left_90", 90.0, "Crisp 90-degree left tactical turn-in-place with planted pivot footwork"),
        ("Tactical_Turn_Right_90", -90.0, "Crisp 90-degree right tactical turn-in-place with planted pivot footwork"),
    ]:
        turn_lp, turn_lR, _, _ = make_standing_ready_pose(sk, yaw_deg=yaw_deg)
        ctx_p, ctx_R = build_4frame_token(stand_lp, stand_lR, 0.0, 0.0)
        tgt_p, tgt_R = build_4frame_token(turn_lp, turn_lR, 0.0, 0.0)
        b1 = engine.generate(ctx_p, ctx_R, tgt_p, tgt_R, constrain_target_pose=True, allowed_tokens=(6, 7))
        save_clip(
            ClipSpec(name, "action", desc, False, 0.12, "Base"),
            b1.world_p, b1.world_R, b1.foot_contacts, b1.inference_ms, b1.token_counts,
            strip_horizontal_root=False, arm_style="rifle_ready", arm_blend=0.78,
        )

    # 4e. Tactical_HitReact_Torso: Ballistic impact flinch & recovery (continuous 2-brick chain)
    flinch_lp, flinch_lR, _, _ = make_hit_flinch_pose(sk, xz=(0.0, -0.10))
    ctx_p, ctx_R = build_4frame_token(stand_lp, stand_lR, 0.0, 0.0)
    t1_p, t1_R = build_4frame_token(flinch_lp, flinch_lR, 0.0, 0.0)
    b1 = engine.generate(ctx_p, ctx_R, t1_p, t1_R, constrain_target_pose=True, allowed_tokens=(6,))
    t2_p, t2_R = build_4frame_token(stand_lp, stand_lR, 0.0, 0.0)
    b2 = engine.generate(b1.local_p[-4:], b1.local_R[-4:], t2_p, t2_R, constrain_target_pose=True, allowed_tokens=(6,))
    wp_hr = np.concatenate([b1.world_p, b2.world_p[1:]], axis=0)
    wR_hr = np.concatenate([b1.world_R, b2.world_R[1:]], axis=0)
    fc_hr = np.concatenate([b1.foot_contacts, b2.foot_contacts[1:]], axis=0)
    save_clip(
        ClipSpec("Tactical_HitReact_Torso", "combat", "Visceral upper-body ballistic flinch and tactical recovery", False, 0.05, "CombatAdditive"),
        wp_hr, wR_hr, fc_hr, b1.inference_ms + b2.inference_ms, b1.token_counts + b2.token_counts,
        strip_horizontal_root=False, arm_style="rifle_ready", arm_blend=0.45,
    )

    # 4f. Tactical_Death_Fall: Knees buckle -> dramatic neural collapse onto ground
    d_mid_lp, d_mid_lR, _, _ = make_death_collapse_mid_pose(sk, xz=(0.08, -0.28))
    d_end_lp, d_end_lR, _, _ = make_death_ground_pose(sk, xz=(0.18, -0.62))
    ctx_p, ctx_R = build_4frame_token(stand_lp, stand_lR, 0.0, 0.0)
    t1_p, t1_R = build_4frame_token(d_mid_lp, d_mid_lR, 0.0, -0.6)
    b1 = engine.generate(ctx_p, ctx_R, t1_p, t1_R, constrain_target_pose=True, allowed_tokens=(6,))
    t2_p, t2_R = build_4frame_token(d_end_lp, d_end_lR, 0.0, 0.0)
    b2 = engine.generate(b1.local_p[-4:], b1.local_R[-4:], t2_p, t2_R, constrain_target_pose=True, allowed_tokens=(6, 7))
    save_clip(
        ClipSpec("Tactical_Death_Fall", "combat", "Weight-collapse tactical elimination fall to ground", False, 0.08, "Base"),
        np.concatenate([b1.world_p, b2.world_p[1:]], axis=0),
        np.concatenate([b1.world_R, b2.world_R[1:]], axis=0),
        np.concatenate([b1.foot_contacts, b2.foot_contacts[1:]], axis=0),
        b1.inference_ms + b2.inference_ms, b1.token_counts + b2.token_counts,
        strip_horizontal_root=False, arm_style=None, arm_blend=0.0,
    )

    # =========================================================================
    # 5. WRITE MASTER MANIFEST
    # =========================================================================
    manifest = {
        "schemaVersion": 1,
        "project": "VANGUARD ZERO",
        "pipeline": "NVIDIA MotionBricks-SOMA-RP-v1 (ONNX FP32 on Apple Silicon M4 CPU)",
        "sourceRepository": "https://github.com/NVlabs/motionbricks",
        "licensing": {
            "neuralModel": "NVIDIA Open Model Agreement (MotionBricks-SOMA-RP-v1 ONNX FP32 — permits commercial use & generated motion redistribution)",
            "runtimeAndSkeletonReference": "Apache 2.0 (NVlabs/motionbricks code & SOMA USD skeleton)",
            "evaluationSampleBvhsUsed": False,
            "note": "All 18 animation clips were neurally synthesized from original authored tactical conditioning poses via MotionBricks-SOMA-RP-v1; zero NVIDIA evaluation-only sample BVH files were copied or redistributed.",
        },
        "targetRigContract": {
            "characterFbx": "blender-assets/exports/characters/TacticalOperator.fbx",
            "unityModelImporterAnimationType": "Generic (required — MotionBricksUnityImporter auto-enforces Generic + CreateFromThisModel Avatar because Unity ignores Transform curves on Humanoid avatars)",
            "restPose": "T-Pose (1.80m standing height, Hips at Y=0.96m, facing +Z in Unity / -Y in Blender)",
            "coordinateSystem": "Unity Left-Handed (+X Right, +Y Up, +Z Forward, meters)",
            "boneCount": len(UNITY_HUMANOID_BONES),
            "bones": [
                {"name": b[0], "parent": b[1], "somaSource": b[2], "tposeOffsetMeters": list(UNITY_TPOSE_OFFSETS_M[b[0]])}
                for b in UNITY_HUMANOID_BONES
            ],
            "sockets": ["RightHandWeaponMount", "HeadEyePoint"],
        },
        "unityScripts": {
            "editorImporter": "exports/unity-scripts/Editor/MotionBricksUnityImporter.cs",
            "editorEntryMethod": "VanguardZero.Animation.Editor.MotionBricksUnityImporter.ImportAllFromMotionPipeline",
            "runtimeScripts": [
                "exports/unity-scripts/Runtime/MotionBricksContracts.cs",
                "exports/unity-scripts/Runtime/MotionBricksAnimatorDriver.cs",
                "exports/unity-scripts/Runtime/TacticalUpperBodyAimIK.cs",
                "exports/unity-scripts/Runtime/MotionBricksFootIK.cs",
                "exports/unity-scripts/Runtime/MotionBricksAnimEventRelay.cs",
            ],
        },
        "summary": {
            "totalClips": len(manifest_clips),
            "totalNeuralBricksGenerated": total_bricks,
            "totalOnnxInferenceMs": round(total_infer_ms, 2),
            "avgOnnxInferenceMsPerBrick": round(total_infer_ms / max(total_bricks, 1), 2),
            "totalPipelineWallSeconds": round(time.perf_counter() - t_start, 2),
        },
        "clips": manifest_clips,
    }
    manifest_path = ROOT_DIR / "manifest.json"
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(
        f"\n[MotionBricks] DONE! Generated {len(manifest_clips)} tactical clips "
        f"({total_bricks} neural bricks, {total_infer_ms:.1f} ms total ONNX, "
        f"{total_infer_ms / max(total_bricks, 1):.1f} ms/brick) in {time.perf_counter() - t_start:.2f}s."
    )
    print(f"[MotionBricks] Manifest written to {manifest_path}")


if __name__ == "__main__":
    generate_all()
