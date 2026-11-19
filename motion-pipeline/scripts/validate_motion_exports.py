"""Automated QA validation suite for VANGUARD ZERO MotionBricks exports."""

from __future__ import annotations

import json
import math
import sys
from pathlib import Path

import numpy as np


ROOT_DIR = Path(__file__).resolve().parents[1]
MANIFEST_PATH = ROOT_DIR / "manifest.json"


def quat_angle_deg(q0: np.ndarray, q1: np.ndarray) -> float:
    n0 = max(float(np.linalg.norm(q0)), 1e-12)
    n1 = max(float(np.linalg.norm(q1)), 1e-12)
    dot = abs(float(np.clip(np.dot(q0 / n0, q1 / n1), -1.0, 1.0)))
    return float(2.0 * math.degrees(math.acos(dot)))


def validate_bvh(path: Path, expected_frames: int, expected_bones: list[str]) -> list[str]:
    errs: list[str] = []
    if not path.exists() or path.stat().st_size == 0:
        return [f"Missing or empty BVH: {path}"]
    lines = path.read_text(encoding="utf-8").splitlines()
    if not lines or lines[0].strip() != "HIERARCHY":
        errs.append(f"{path.name}: missing HIERARCHY header")
    try:
        motion_idx = lines.index("MOTION")
    except ValueError:
        return [f"{path.name}: missing MOTION section"]

    header_text = "\n".join(lines[:motion_idx])
    for b in expected_bones:
        if f" {b}" not in header_text:
            errs.append(f"{path.name}: missing bone {b} in BVH hierarchy")

    frames_line = lines[motion_idx + 1].strip()
    ft_line = lines[motion_idx + 2].strip()
    if not frames_line.startswith("Frames:"):
        errs.append(f"{path.name}: bad Frames line '{frames_line}'")
    else:
        n_frames = int(frames_line.split()[1])
        if n_frames != expected_frames:
            errs.append(f"{path.name}: BVH Frames={n_frames} != expected {expected_frames}")
    if not ft_line.startswith("Frame Time:"):
        errs.append(f"{path.name}: bad Frame Time line '{ft_line}'")

    data_rows = [ln.strip() for ln in lines[motion_idx + 3 :] if ln.strip()]
    if len(data_rows) != expected_frames:
        errs.append(f"{path.name}: BVH has {len(data_rows)} data rows != expected {expected_frames}")

    expected_cols = 6 + (len(expected_bones) - 1) * 3
    for r_idx, row in enumerate(data_rows):
        vals = row.split()
        if len(vals) != expected_cols:
            errs.append(f"{path.name}: row {r_idx} has {len(vals)} cols != {expected_cols}")
            break
        arr = np.asarray([float(v) for v in vals], dtype=np.float64)
        if not np.all(np.isfinite(arr)):
            errs.append(f"{path.name}: NaN/Inf at BVH row {r_idx}")
            break
    return errs


def main() -> int:
    if not MANIFEST_PATH.exists():
        print(f"FAIL: manifest missing at {MANIFEST_PATH}")
        return 1

    manifest = json.loads(MANIFEST_PATH.read_text(encoding="utf-8"))
    clips = manifest["clips"]
    expected_bones = [b["name"] for b in manifest["targetRigContract"]["bones"]]

    all_errors: list[str] = []
    print(f"Validating {len(clips)} MotionBricks clips + 5 Unity C# scripts...\n")

    for entry in clips:
        name = entry["name"]
        mb_path = ROOT_DIR / entry["mbclipPath"]
        bvh_path = ROOT_DIR / entry["bvhPath"]

        if not mb_path.exists() or mb_path.stat().st_size == 0:
            all_errors.append(f"{name}: missing/empty {mb_path}")
            continue

        data = json.loads(mb_path.read_text(encoding="utf-8"))
        T = data["frameCount"]
        hips = np.asarray(data["hipsPositions"], dtype=np.float64)
        quats = np.asarray(data["worldDeltaQuats"], dtype=np.float64)
        fc = np.asarray(data["footContacts"], dtype=np.float64)

        clip_errs: list[str] = []
        if data["boneNames"] != expected_bones:
            clip_errs.append("boneNames mismatch with manifest rig contract")
        if hips.shape != (T, 3):
            clip_errs.append(f"hips shape {hips.shape} != {(T, 3)}")
        if quats.shape != (T, len(expected_bones), 4):
            clip_errs.append(f"quats shape {quats.shape} != {(T, len(expected_bones), 4)}")
        if fc.shape != (T, 4):
            clip_errs.append(f"footContacts shape {fc.shape} != {(T, 4)}")

        if not (np.all(np.isfinite(hips)) and np.all(np.isfinite(quats)) and np.all(np.isfinite(fc))):
            clip_errs.append("NaN or Inf detected in animation arrays")

        norms = np.linalg.norm(quats, axis=-1)
        max_norm_err = float(np.max(np.abs(norms - 1.0)))
        if max_norm_err > 1e-3:
            clip_errs.append(f"non-unit quaternion norm error {max_norm_err:.6f}")

        # Hemisphere continuity & max frame-to-frame step angle
        quats_n = quats / np.maximum(np.linalg.norm(quats, axis=-1, keepdims=True), 1e-12)
        dots = np.sum(quats_n[:-1] * quats_n[1:], axis=-1)
        min_dot = float(np.min(dots))
        if min_dot < -1e-4:
            clip_errs.append(f"quaternion hemisphere flip detected (min dot={min_dot:.4f})")

        max_step_deg = float(2.0 * math.degrees(math.acos(float(np.clip(np.min(np.abs(dots)), -1.0, 1.0)))))
        if max_step_deg > 22.0:
            clip_errs.append(f"excessive angular velocity jump {max_step_deg:.2f} deg/frame @60Hz (>22 deg)")

        # Foot contacts range
        if float(fc.min()) < -1e-4 or float(fc.max()) > 1.0 + 1e-4:
            clip_errs.append(f"footContacts out of [0,1]: [{fc.min():.4f}, {fc.max():.4f}]")

        # Loop continuity
        loop_pos_err = float(np.linalg.norm(hips[0] - hips[-1]))
        loop_rot_err = max(quat_angle_deg(quats[0, b], quats[-1, b]) for b in range(len(expected_bones)))
        if data["isLoop"]:
            if loop_pos_err > 1e-3:
                clip_errs.append(f"loop hips position gap {loop_pos_err:.5f}m")
            if loop_rot_err > 0.25:
                clip_errs.append(f"loop rotation seam gap {loop_rot_err:.3f} deg")

        # Check footstep events
        dur = data["duration"]
        for ev in data["footstepEvents"]:
            if not (0.0 <= ev["time"] <= dur + 1e-3):
                clip_errs.append(f"footstep event time {ev['time']} out of [0, {dur}]")
        if entry["category"] in ("locomotion", "crouch") and "Idle" not in name:
            if len(data["footstepEvents"]) < 2:
                clip_errs.append(f"locomotion loop has only {len(data['footstepEvents'])} footstep events (<2)")

        # Validate matching BVH
        clip_errs.extend(validate_bvh(bvh_path, T, expected_bones))

        status = "PASS" if not clip_errs else "FAIL"
        print(
            f"  [{status}] {name:28s} | {T:3d}f @60Hz | hipsY=[{hips[:,1].min():.2f},{hips[:,1].max():.2f}] "
            f"| maxStep={max_step_deg:5.2f}deg | loopErr=({loop_pos_err:.4f}m,{loop_rot_err:.2f}deg) "
            f"| bvh={bvh_path.stat().st_size//1024:3d}KB"
        )
        for e in clip_errs:
            print(f"         ERROR: {e}")
            all_errors.append(f"{name}: {e}")

    # Validate Unity C# scripts exist & are non-empty
    scripts = [manifest["unityScripts"]["editorImporter"], *manifest["unityScripts"]["runtimeScripts"]]
    for rel in scripts:
        p = ROOT_DIR / rel
        if not p.exists() or p.stat().st_size < 200:
            all_errors.append(f"Missing or tiny C# script: {rel}")
        else:
            print(f"  [PASS] C# script: {rel} ({p.stat().st_size} bytes)")

    print()
    if all_errors:
        print(f"VALIDATION FAILED ({len(all_errors)} errors):")
        for e in all_errors:
            print(" -", e)
        return 1

    print(f"ALL CHECKS PASSED: {len(clips)} neural MotionBricks clips (.mbclip.json + .bvh) & {len(scripts)} Unity C# scripts verified!")
    return 0


if __name__ == "__main__":
    sys.exit(main())
