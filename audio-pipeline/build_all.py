"""
VANGUARD ZERO — Master Audio Build Pipeline
Synthesizes all 62 production 48kHz 16-bit PCM WAV assets, writes manifest.json,
cleans scratch temp files, and runs automated QA verification.
"""

from __future__ import annotations

import json
import os
import shutil
import sys
import time
from datetime import datetime, timezone

from gen_weapons import build_weapons
from gen_impacts import build_impacts
from gen_foley import build_foley
from gen_ambience_music import build_ambience_music
from gen_ui_voice import build_ui_and_voice
from verify_audio import verify_pipeline


def main() -> None:
    t0 = time.time()
    root_dir = os.path.dirname(os.path.abspath(__file__))
    exports_dir = os.path.join(root_dir, "exports")
    tmp_dir = os.path.join(root_dir, "tmp")

    os.makedirs(exports_dir, exist_ok=True)
    # Clean any stale WAVs in exports before regenerating cleanly
    for fn in os.listdir(exports_dir):
        if fn.endswith(".wav"):
            os.remove(os.path.join(exports_dir, fn))

    print("[1/5] Synthesizing Weapons suite (19 Mono 3D WAVs)...")
    weapons = build_weapons(exports_dir)

    print("[2/5] Synthesizing Impacts & Combat Feedback suite (13 WAVs)...")
    impacts = build_impacts(exports_dir)

    print("[3/5] Synthesizing Movement & Foley suite (11 Mono 3D WAVs)...")
    foley = build_foley(exports_dir)

    print("[4/5] Synthesizing Ambience & Cinematic Music suite (4 Stereo 2D WAVs)...")
    amb_music = build_ambience_music(exports_dir)

    print("[5/5] Synthesizing Tactical UI & VHF Radio Voice Announcer suite (15 Stereo 2D WAVs)...")
    ui_voice, vo_notes = build_ui_and_voice(exports_dir, tmp_dir)

    all_assets = weapons + impacts + foley + amb_music + ui_voice

    manifest = {
        "project": "VANGUARD ZERO",
        "mapAcousticProfile": "BLACKSITE-09 (rain-swept dusk brutalist military concrete radar compound)",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "format": {
            "container": "WAV (RIFF PCM)",
            "sampleRate": 48000,
            "bitDepth": 16,
            "encoding": "Signed 16-bit PCM (TPDF dithered)",
            "headroomRule": "All peaks strictly < -0.5 dBFS; zero DC offset",
            "channelConvention": "Mono (1ch) for 3D spatialBlend=1.0 SFX; Stereo (2ch) for 2D spatialBlend=0.0 UI/Music/Ambience/Voice/HUD",
        },
        "unityMixerGroups": [
            {"name": "Master", "parent": None, "defaultVolumeDb": 0.0},
            {"name": "Weapons", "parent": "Master", "defaultVolumeDb": -1.0},
            {"name": "Impacts", "parent": "Master", "defaultVolumeDb": -1.5},
            {"name": "Foley", "parent": "Master", "defaultVolumeDb": -3.5},
            {"name": "Ambience", "parent": "Master", "defaultVolumeDb": -11.0},
            {"name": "Music", "parent": "Master", "defaultVolumeDb": -8.0},
            {"name": "UI", "parent": "Master", "defaultVolumeDb": -4.0},
            {"name": "Voice", "parent": "Master", "defaultVolumeDb": -2.0},
        ],
        "voiceSynthesisNotes": vo_notes,
        "totalAssets": len(all_assets),
        "assets": all_assets,
    }

    manifest_path = os.path.join(root_dir, "manifest.json")
    with open(manifest_path, "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=2)

    # Clean scratch tmp directory
    if os.path.exists(tmp_dir):
        shutil.rmtree(tmp_dir, ignore_errors=True)

    elapsed = time.time() - t0
    print(f"\nGenerated {len(all_assets)} WAV assets + manifest.json in {elapsed:.2f}s.\n")

    ok, errs = verify_pipeline(root_dir)
    if not ok:
        print("\nQA VERIFICATION FAILED:")
        for e in errs:
            print("  -", e)
        sys.exit(1)
    print("\nALL AUDIO QA GATES PASSED!")


if __name__ == "__main__":
    main()
