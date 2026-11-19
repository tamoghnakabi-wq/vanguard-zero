"""
VANGUARD ZERO — Automated Audio QA & Verification Suite
Inspects manifest.json and every exported WAV in audio-pipeline/exports/.
"""

from __future__ import annotations

import json
import math
import os
import sys
import wave
import numpy as np

REQUIRED_IDS = [
    # Weapons (19)
    "vx47_fire_01", "vx47_fire_02", "vx47_fire_03",
    "vx47_reload_magout", "vx47_reload_magin", "vx47_reload_bolt",
    "m890_fire_01", "m890_fire_02", "m890_pump", "m890_shell_insert",
    "m19_fire_01", "m19_fire_02", "m19_reload",
    "weapon_dryfire", "weapon_equip", "weapon_ads_in", "weapon_ads_out",
    "shell_casing_concrete_01", "shell_casing_concrete_02",
    # Impacts & Combat Feedback (13)
    "impact_concrete_01", "impact_concrete_02",
    "impact_metal_01", "impact_metal_02",
    "impact_flesh_01", "impact_armor_01",
    "hitmarker_body", "hitmarker_headshot", "kill_confirm",
    "player_hurt", "player_death", "low_health_heartbeat", "explosion_frag",
    # Movement & Foley (11)
    "footstep_concrete_01", "footstep_concrete_02", "footstep_concrete_03", "footstep_concrete_04",
    "footstep_metal_01", "footstep_metal_02", "footstep_metal_03", "footstep_metal_04",
    "jump_effort", "land_heavy", "slide_tactical",
    # Ambience & Music (4)
    "ambience_blacksite_rain_loop", "music_main_menu_loop",
    "music_match_victory", "music_match_defeat",
    # UI (5)
    "ui_hover", "ui_click", "ui_confirm", "ui_cancel", "ui_match_found",
]

VALID_MIXER_GROUPS = {"Weapons", "Impacts", "Foley", "Ambience", "Music", "UI", "Voice"}


def db(val: float) -> float:
    return 20.0 * math.log10(max(abs(val), 1e-12))


def verify_pipeline(root_dir: str) -> tuple[bool, list[str]]:
    manifest_path = os.path.join(root_dir, "manifest.json")
    exports_dir = os.path.join(root_dir, "exports")
    errors: list[str] = []

    if not os.path.exists(manifest_path):
        return False, [f"Missing manifest.json at {manifest_path}"]

    with open(manifest_path, "r", encoding="utf-8") as f:
        manifest = json.load(f)

    assets = manifest.get("assets", [])
    by_id = {a["id"]: a for a in assets}

    for req_id in REQUIRED_IDS:
        if req_id not in by_id:
            errors.append(f"Missing required asset in manifest: {req_id}")

    # Check no stray unregistered WAVs
    manifest_files = {os.path.basename(a["file"]) for a in assets}
    disk_files = {fn for fn in os.listdir(exports_dir) if fn.endswith(".wav")}
    extra = disk_files - manifest_files
    if extra:
        errors.append(f"Unregistered WAV files on disk: {sorted(extra)}")

    print(f"{'ID':<32} {'CH':>2} {'DUR':>6} {'PEAK dB':>8} {'RMS dB':>8} {'DC':>8} {'LOOP_JMP':>9}  STATUS")
    print("-" * 92)

    for a in assets:
        aid = a["id"]
        rel_path = a["file"]
        wav_path = os.path.join(root_dir, rel_path)
        item_errs = []

        if not os.path.exists(wav_path):
            errors.append(f"{aid}: file missing ({wav_path})")
            continue
        if os.path.getsize(wav_path) < 128:
            item_errs.append("file too small")

        with wave.open(wav_path, "rb") as wf:
            ch = wf.getnchannels()
            sw = wf.getsampwidth()
            sr = wf.getframerate()
            nf = wf.getnframes()
            raw = wf.readframes(nf)

        if sr != 48000:
            item_errs.append(f"sr={sr} != 48000")
        if sw != 2:
            item_errs.append(f"bitDepth={sw*8} != 16")

        expected_ch = 1 if a["spatialBlend"] == 1.0 else 2
        if ch != expected_ch:
            item_errs.append(f"channels={ch} != expected {expected_ch} for spatialBlend={a['spatialBlend']}")
        if a["mixerGroup"] not in VALID_MIXER_GROUPS:
            item_errs.append(f"invalid mixerGroup={a['mixerGroup']}")

        pcm = np.frombuffer(raw, dtype=np.int16).astype(np.float64) / 32767.0
        if ch == 2:
            data = np.vstack([pcm[0::2], pcm[1::2]])
        else:
            data = pcm

        if not np.all(np.isfinite(data)):
            item_errs.append("non-finite samples")

        peak = float(np.max(np.abs(data)))
        rms = float(np.sqrt(np.mean(data ** 2)))
        peak_db = db(peak)
        rms_db = db(rms)
        dc = float(np.max(np.abs(np.mean(data, axis=-1))))
        dur = nf / sr

        if peak_db >= -0.5:
            item_errs.append(f"peak {peak_db:.2f} dBFS >= -0.5 dBFS")
        if peak_db < -12.0:
            item_errs.append(f"peak too quiet ({peak_db:.2f} dBFS)")
        if rms_db < -42.0:
            item_errs.append(f"RMS too quiet / silent ({rms_db:.2f} dBFS)")
        if dc > 0.002:
            item_errs.append(f"DC offset too high ({dc:.5f})")

        is_loop = bool(a.get("loop")) or aid.endswith("_loop")
        loop_jmp_str = "   n/a   "
        if is_loop:
            if ch == 2:
                jmp = float(np.max(np.abs(data[:, -1] - data[:, 0])))
                slope_seam = float(np.max(np.abs((data[:, 0] - data[:, -1]) - (data[:, -1] - data[:, -2]))))
                p99_d2 = float(np.percentile(np.abs(np.diff(data, n=2, axis=1)), 99))
            else:
                jmp = float(abs(data[-1] - data[0]))
                slope_seam = float(abs((data[0] - data[-1]) - (data[-1] - data[-2])))
                p99_d2 = float(np.percentile(np.abs(np.diff(data, n=2)), 99))
            loop_jmp_str = f"{jmp:9.5f}"
            if jmp > 0.025:
                item_errs.append(f"loop boundary click delta={jmp:.5f} > 0.025")
            if slope_seam > max(0.04, p99_d2 * 1.15):
                item_errs.append(f"loop slope discontinuity={slope_seam:.5f} > {max(0.04, p99_d2 * 1.15):.5f}")

        status = "PASS" if not item_errs else "FAIL: " + "; ".join(item_errs)
        print(f"{aid:<32} {ch:>2} {dur:>6.2f} {peak_db:>8.2f} {rms_db:>8.2f} {dc:>8.5f} {loop_jmp_str}  {status}")
        if item_errs:
            errors.append(f"{aid}: " + "; ".join(item_errs))

    print("-" * 92)
    print(f"Total assets checked: {len(assets)} | Errors: {len(errors)}")
    return len(errors) == 0, errors


if __name__ == "__main__":
    root = os.path.dirname(os.path.abspath(__file__))
    ok, errs = verify_pipeline(root)
    if not ok:
        print("\nVERIFICATION FAILED:")
        for e in errs:
            print("  -", e)
        sys.exit(1)
    print("\nALL AUDIO QA GATES PASSED!")
