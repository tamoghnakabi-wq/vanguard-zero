# VANGUARD ZERO — MotionBricks Neural Character Motion Pipeline

> **Owner:** `motion-lead` (Character Motion & Animation Lead)  
> **Upstream Technology:** [NVlabs/motionbricks](https://github.com/NVlabs/motionbricks) (SIGGRAPH 2026 — *MotionBricks: Scalable Real-Time Motions with Modular Latent Generative Model and Smart Primitives*)  
> **Target Engine & Rig:** Unity 6 (`6000.3.22f1`, URP 3D) + `blender-assets/exports/characters/TacticalOperator.fbx` (22-bone Unity Humanoid T-Pose)

---

## 1. Investigation & Architecture Summary

### What We Found in `NVlabs/motionbricks`
- **Model:** `MotionBricks-SOMA-RP-v1` — a ~200M-parameter modular latent generative model (Pose VQ-VAE 23.4M + 16-layer Masked Pose Token Transformer 136.5M + Root Predictor 40.4M) trained on **313,526 studio mocap clips (700 hours)** of Bones RigPlay 1 (RP1) on the **30-joint Y-up SOMA skeleton** at 30 Hz.
- **Native macOS Apple Silicon M4 Inference:** Although NVIDIA's reference Python demos use CUDA + TensorRT 10.8 + Warp + Newton, the repository bundles pre-exported **ONNX opset 17** models (`mmm_v6_soma_fp32.onnx` and `mmm_v6_soma_fp16.onnx`) with a documented batched ONNX interface (`docs/onnx_inference.md`).
- **Measured M4 Performance:** We ported NVIDIA's Apache-2.0 SOMA input encoding, 6D rotation decoding, FK reconstruction, and endpoint delta-correction kernels (`motionbricks/model/soma/kernels.py`) to pure NumPy + `onnxruntime` (`scripts/motionbricks_onnx_engine.py`). On this **Apple Silicon M4 (arm64)** CPU:
  - Model load (`mmm_v6_soma_fp32.onnx`): **~0.45 s**
  - Neural brick inference (24–64 frames @ 30 Hz + 4 neural foot-contact channels): **~20.5 ms / brick average**
  - Full 18-clip tactical motion suite (60 chained neural bricks): **~1.23 s total ONNX compute** (23.3 s full pipeline including 60 Hz SLERP upsampling, stride cycle extraction, and dual MBClip/BVH export)!

### License Compliance
| Component | License | Usage in VANGUARD ZERO |
|---|---|---|
| `MotionBricks-SOMA-RP-v1` (`mmm_v6_soma_fp32.onnx`) | **NVIDIA Open Model Agreement** (permits commercial use & generated outputs) | Used locally on M4 to neurally synthesize all 18 tactical FPS clips |
| `motionbricks` runtime code & `soma_base_skel_minimal_no_root.usd` | **Apache 2.0** | Reference SOMA skeleton & ported NumPy/ONNX encode/decode math |
| `assets/motions/soma/*.bvh` (sample clips) | NVIDIA Sample Data Evaluation License (eval only, non-redistributable) | **NOT USED.** Zero sample BVH clips were copied or redistributed; all conditioning poses were authored programmatically on the Apache-2.0 SOMA USD skeleton. |

---

## 2. Generated 18-Clip Tactical Operator Motion Suite (`exports/`)

Every clip is generated from **real `MotionBricks-SOMA-RP-v1` FP32 neural rollouts**, automatically stride-cycle-extracted using 3D pose + neural foot-contact periodicity, conditioned with flip-free tactical combat upper-body rifle momentum, upsampled from **30 Hz → 60 Hz** via cubic spline + SLERP with continuous quaternion hemispheres, and exported in **two formats**:
1. **`exports/mbclips/*.mbclip.json`** — Unity-ready 60 Hz world-delta quaternions (Unity left-handed `+X Right, +Y Up, +Z Forward`), `Hips` positions, neural `[LeftHeel, LeftToe, RightHeel, RightToe]` contact curves, and extracted footstep SFX event timestamps.
2. **`exports/bvh/*.bvh`** — Standard 60 Hz Humanoid BVH clips on the canonical 22-bone T-Pose skeleton (directly inspectable/importable in Blender and standard mocap tools).

### Clip Inventory
| Category | Clip Name | Loop | Role in Unity Animator |
|---|---|---:|---|
| **Locomotion** | `Tactical_Idle_Aim` | Yes | Standing 2D BlendTree center `(0, 0)` |
| **Locomotion** | `Tactical_Walk_Fwd` | Yes | Standing 2D BlendTree `(0, +0.5)` (~1.9 m/s) |
| **Locomotion** | `Tactical_Walk_Bwd` | Yes | Standing 2D BlendTree `(0, -0.5)` (~1.6 m/s) |
| **Locomotion** | `Tactical_Strafe_Left` | Yes | Standing 2D BlendTree `(-0.6, 0)` (~2.0 m/s) |
| **Locomotion** | `Tactical_Strafe_Right` | Yes | Standing 2D BlendTree `(+0.6, 0)` (~2.0 m/s) |
| **Locomotion** | `Tactical_Jog_Fwd` | Yes | Standing 2D BlendTree `(0, +0.75)` (~3.5 m/s) |
| **Locomotion** | `Tactical_Sprint_Fwd` | Yes | Standing 2D BlendTree `(0, +1.0)` (~5.0 m/s, low-ready carry) |
| **Crouch** | `Tactical_Crouch_Idle` | Yes | Crouching 2D BlendTree center `(0, 0)` |
| **Crouch** | `Tactical_Crouch_Walk_Fwd` | Yes | Crouching 2D BlendTree `(0, +0.5)` (~1.55 m/s) |
| **Crouch** | `Tactical_Crouch_Walk_Bwd` | Yes | Crouching 2D BlendTree `(0, -0.5)` (~1.36 m/s) |
| **Crouch** | `Tactical_Crouch_Strafe_Left` | Yes | Crouching 2D BlendTree `(-0.5, 0)` (~1.55 m/s) |
| **Crouch** | `Tactical_Crouch_Strafe_Right` | Yes | Crouching 2D BlendTree `(+0.5, 0)` (~1.55 m/s) |
| **Action** | `Tactical_Slide` | No | Sprint-to-crouch knee-slide state |
| **Action** | `Tactical_Jump_Vault` | No | Explosive jump / barrier vault (`JumpVault` trigger) |
| **Action** | `Tactical_Turn_Left_90` | No | Planted 90° left turn-in-place (`TurnLeft90` trigger) |
| **Action** | `Tactical_Turn_Right_90` | No | Planted 90° right turn-in-place (`TurnRight90` trigger) |
| **Combat** | `Tactical_HitReact_Torso` | No | Layer 1 Upper-Body override ballistic flinch (`HitReact` trigger) |
| **Combat** | `Tactical_Death_Fall` | No | Weight-collapse elimination fall (`IsDead == true`) |

Full per-clip metrics (frame counts, exact velocities, ONNX timings, footstep timestamps) are recorded in [`manifest.json`](./manifest.json).

---

## 3. Locked Rig Contract (`TacticalOperator.fbx`)

Shared with `@blender-artist` and verified against `blender-assets/exports/characters/TacticalOperator.fbx`:
- **Rest Pose:** Clean T-Pose (`1.80m` height, `Hips` at `Y ≈ 0.96m`, facing `+Z` in Unity / `-Y` in Blender, Scale `1.0`).
- **22 Core Unity `HumanBodyBones`:**
  - `Hips` → `Spine` → `Chest` → `UpperChest` → `Neck` → `Head`
  - Under `UpperChest`: `LeftShoulder` → `LeftUpperArm` → `LeftLowerArm` → `LeftHand`; `RightShoulder` → `RightUpperArm` → `RightLowerArm` → `RightHand`
  - Under `Hips`: `LeftUpperLeg` → `LeftLowerLeg` → `LeftFoot` → `LeftToes`; `RightUpperLeg` → `RightLowerLeg` → `RightFoot` → `RightToes`
- **Sockets:** `RightHandWeaponMount` (under `RightHand`), `HeadEyePoint` (under `Head`), plus weapon `LeftHandGrip` anchors on `VX47`, `M890`, and `M19`.

---

## 4. Drop-In Unity 6 Integration Guide (for `@unity-core`)

1. **Copy Scripts into `unity-game/`:**
   - Copy `exports/unity-scripts/Runtime/*.cs` (5 scripts: `MotionBricksContracts`, `MotionBricksAnimatorDriver`, `TacticalUpperBodyAimIK`, `MotionBricksFootIK`, `MotionBricksAnimEventRelay`) → `unity-game/Assets/Scripts/Animation/`
   - Copy `exports/unity-scripts/Editor/MotionBricksUnityImporter.cs` → `unity-game/Assets/Editor/`
   - *(Optional)* Copy `exports/mbclips/*.mbclip.json` → `unity-game/Assets/Animation/MotionBricks/Data/` (the importer also auto-discovers `../motion-pipeline/exports/mbclips/` directly!).
2. **Import Clips & Build Animator (Menu or Unity CLI):**
   - Run `VanguardZero.Animation.Editor.MotionBricksUnityImporter.ImportAllFromMotionPipeline` (after `TacticalOperator.fbx` is imported in `Assets/`).
   - **Important Rig Mode (`Generic`):** Because the baked `.anim` clips write direct 1-to-1 `Transform` curves (`localPosition`/`localRotation`) matched to `TacticalOperator.fbx`'s exact 22-bone hierarchy (which Unity's Animator ignores on Humanoid avatars), `MotionBricksUnityImporter` automatically configures `TacticalOperator.fbx` to **`ModelImporterAnimationType.Generic`** + **`CreateFromThisModel` Avatar** — do **not** switch `TacticalOperator.fbx` back to Humanoid!
   - Outputs generated automatically:
     - `Assets/Animation/MotionBricks/Clips/*.anim` (18 native 60 Hz clips with `LeftFootContact` / `RightFootContact` float curves and `OnMotionBricksFootstep` AnimationEvents)
     - `Assets/Animation/MotionBricks/Masks/UpperBody_CombatMask.mask`
     - `Assets/Animation/MotionBricks/Controllers/VanguardOperator.controller`
3. **Wire onto `Player_Networked` (3P Operator) & `Bot_Networked`:**
   - Assign `VanguardOperator.controller` to the `TacticalOperator` `Animator`.
   - Attach `MotionBricksAnimatorDriver`, `TacticalUpperBodyAimIK`, and `MotionBricksFootIK` to the operator root.
   - Each frame, call `driver.SetLocomotionState(vel, pitch, grounded, crouch, sprint, slide, ads, reload, dead, weaponSlot)` (or `driver.SetDriveState(...)`).
   - On weapon equip, pass the active weapon's `LeftHandGrip` transform to `upperBodyAimIK.SetLeftHandForegripTarget(leftHandGrip)`.
   - On gunfire / damage / jump, call `driver.TriggerWeaponRecoil(...)`, `driver.TriggerHitReact(...)`, and `driver.TriggerJumpVault()`.
   - Subscribe to `footIK.OnFootstep` to trigger `audio-pipeline`'s `footstep_concrete_01..04` / `footstep_metal_01..04` 3D audio clips!

---

## 5. Reproducing & Validating Locally

```bash
# Regenerate all 18 clips via MotionBricks-SOMA-RP-v1 FP32 ONNX
./.venv/bin/python scripts/generate_vanguard_motions.py

# Run automated QA checks (unit quats, loop continuity, angular velocity, BVH integrity)
./.venv/bin/python scripts/validate_motion_exports.py
```
