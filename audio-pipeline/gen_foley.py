"""
VANGUARD ZERO — Movement & Tactical Foley Synthesis Module (11 Mono 3D WAVs)
"""

from __future__ import annotations

import os
import numpy as np
from dsp_core import (
    SR,
    make_time,
    mix_at,
    white_noise,
    pink_noise,
    brown_noise,
    crackle_noise,
    butter_lp,
    butter_bp,
    biquad_peaking,
    time_varying_bp,
    env_ad,
    env_trapezoid,
    pitched_sine_sweep,
    modal_resonator,
    modal_impulse,
    make_metallic_chamber_ir,
    make_outdoor_compound_ir,
    convolve_reverb,
    soft_saturate,
    finalize_and_save,
)


def synth_footstep_concrete(variant: int) -> np.ndarray:
    rng = np.random.default_rng(3000 + variant * 47)
    p = 1.0 + rng.uniform(-0.08, 0.08)
    n = int(0.32 * SR)
    out = np.zeros(n, dtype=np.float64)

    # 1. Heel strike: heavy rubber combat boot sole impact + coarse concrete grit
    heel_n = int(0.085 * SR)
    heel_thud = pitched_sine_sweep(heel_n, 155.0 * p, 48.0 * p, 0.015) * env_ad(heel_n, 0.002, 0.052)
    heel_grit = (
        pink_noise(heel_n, rng) * 0.55
        + crackle_noise(heel_n, 520.0 + variant * 65.0, rng) * 0.72
    )
    heel_grit = butter_bp(heel_grit, 480.0, 5200.0, order=2) * env_ad(heel_n, 0.002, 0.044)
    heel_grit = biquad_peaking(heel_grit, 1350.0 * p, 4.0, q=1.1)

    # 2. Toe-roll & weight transfer (+30..40ms later): brighter rubber sole + fine gravel crunch
    toe_offset = 0.031 + rng.uniform(-0.004, 0.007)
    toe_n = int(0.095 * SR)
    toe_thud = pitched_sine_sweep(toe_n, 125.0 * p, 52.0 * p, 0.016) * env_ad(toe_n, 0.004, 0.05)
    toe_crunch = (
        pink_noise(toe_n, rng) * 0.45
        + crackle_noise(toe_n, 680.0, rng) * 0.85
    )
    toe_crunch = butter_bp(toe_crunch, 820.0, 6800.0, order=2) * env_ad(toe_n, 0.004, 0.058)

    # 3. Subtle wet-blacksite concrete micro-dampness + tactical trouser gear whisper
    gear_n = int(0.11 * SR)
    gear = butter_bp(pink_noise(gear_n, rng), 420.0, 1650.0, order=2) * env_trapezoid(gear_n, 0.015, 0.03, 0.065)

    mix_at(out, heel_thud * 1.0 + heel_grit * 0.82, 0.0, gain=1.0)
    mix_at(out, toe_thud * 0.55 + toe_crunch * 0.72, toe_offset, gain=0.85)
    mix_at(out, gear / max(np.max(np.abs(gear)), 1e-6), 0.015, gain=0.14)

    out = soft_saturate(out, drive=1.55)
    ir = make_outdoor_compound_ir(rt60=0.28, predelay_ms=8.0, slap_ms=42.0, slap_gain=0.12, rng=rng)
    return convolve_reverb(out, ir, wet=0.14, dry=0.95, extend=True)


def synth_footstep_metal(variant: int) -> np.ndarray:
    """Heavy combat boot striking industrial steel catwalk grating."""
    rng = np.random.default_rng(3100 + variant * 61)
    p = 1.0 + rng.uniform(-0.08, 0.08)
    n = int(0.38 * SR)
    out = np.zeros(n, dtype=np.float64)

    # 1. Boot heel strike exciting catwalk steel plate modes
    exc_n = int(0.06 * SR)
    exc = (white_noise(exc_n, rng) * 0.5 + pink_noise(exc_n, rng) * 0.5) * env_ad(exc_n, 0.001, 0.014)
    steel_ring = modal_resonator(
        np.pad(exc, (0, int(0.24 * SR))),
        freqs=[162 * p, 338 * p, 585 * p, 970 * p, 1640 * p, 2720 * p, 4380 * p],
        decays=[0.14, 0.12, 0.095, 0.072, 0.048, 0.030, 0.018],
        amps=[0.85, 1.0, 0.92, 0.80, 0.62, 0.42, 0.25],
    )
    heel_thud = pitched_sine_sweep(int(0.09 * SR), 175.0 * p, 56.0 * p, 0.018) * env_ad(int(0.09 * SR), 0.001, 0.055)

    # 2. Secondary toe-plate clack + metallic grate grit (+36ms)
    toe_offset = 0.034 + rng.uniform(-0.004, 0.007)
    toe_exc = white_noise(int(0.04 * SR), rng) * env_ad(int(0.04 * SR), 0.001, 0.012)
    toe_clang = modal_resonator(
        np.pad(toe_exc, (0, int(0.16 * SR))),
        freqs=[245 * p, 510 * p, 1180 * p, 2450 * p, 4100 * p],
        decays=[0.09, 0.075, 0.055, 0.035, 0.02],
        amps=[0.7, 0.9, 0.78, 0.55, 0.3],
    )
    grate_scuff = butter_bp(crackle_noise(int(0.07 * SR), 420.0, rng), 1400.0, 7200.0, order=2) * env_ad(int(0.07 * SR), 0.002, 0.04)

    mix_at(out, heel_thud, 0.0, gain=0.85)
    mix_at(out, steel_ring, 0.0, gain=1.05)
    mix_at(out, toe_clang, toe_offset, gain=0.82 * 0.68)
    mix_at(out, grate_scuff, toe_offset, gain=0.82 * 0.42)

    out = soft_saturate(out, drive=1.65)
    ir = make_metallic_chamber_ir(rt60=0.22, rng=rng)
    return convolve_reverb(out, ir, wet=0.22, dry=0.92, extend=True)


def synth_jump_effort() -> np.ndarray:
    """Boot push-off scuff + armored kit rustle + quick athletic exhale."""
    rng = np.random.default_rng(3201)
    n = int(0.36 * SR)
    out = np.zeros(n, dtype=np.float64)

    # Push-off sole scuff on concrete
    scuff_n = int(0.11 * SR)
    scuff = time_varying_bp(
        pink_noise(scuff_n, rng) * 0.6 + crackle_noise(scuff_n, 480.0, rng) * 0.6,
        np.linspace(620.0, 1850.0, scuff_n),
        q=1.8,
    ) * env_ad(scuff_n, 0.006, 0.078)
    push_thud = pitched_sine_sweep(int(0.09 * SR), 140.0, 55.0, 0.018) * env_ad(int(0.09 * SR), 0.004, 0.055)

    # Tactical vest / Cordura cloth lift + strap micro-rattle
    cloth_n = int(0.22 * SR)
    cloth = time_varying_bp(pink_noise(cloth_n, rng), np.linspace(480.0, 1320.0, cloth_n), q=1.3) * env_trapezoid(cloth_n, 0.02, 0.07, 0.13)
    strap = modal_impulse(0.07, [1450, 2950, 5200], [0.018, 0.012, 0.008], [0.6, 0.85, 0.5], rng=rng)

    # Subtle athletic push-off breath (formant-filtered noise, grounded & non-cartoonish)
    br_n = int(0.16 * SR)
    br_src = pink_noise(br_n, rng)
    br = (butter_bp(br_src, 420.0, 760.0, order=3) * 0.8 + butter_bp(br_src, 1100.0, 1750.0, order=3) * 0.5) * env_ad(br_n, 0.018, 0.11)

    mix_at(out, push_thud, 0.0, gain=0.82)
    mix_at(out, scuff / max(np.max(np.abs(scuff)), 1e-6), 0.0, gain=0.72)
    mix_at(out, cloth / max(np.max(np.abs(cloth)), 1e-6), 0.025, gain=0.52)
    mix_at(out, strap, 0.055, gain=0.25)
    mix_at(out, br / max(np.max(np.abs(br)), 1e-6), 0.032, gain=0.35)
    return out


def synth_land_heavy() -> np.ndarray:
    """Heavy tactical drop landing: dual-boot concrete slam + gravel scatter + armored kit clatter."""
    rng = np.random.default_rng(3202)
    n = int(0.58 * SR)
    out = np.zeros(n, dtype=np.float64)

    # Boot 1 & staggered Boot 2 heavy impact (+24ms)
    for idx, (offset, weight, pitch) in enumerate([(0.0, 1.0, 1.0), (0.024, 0.85, 0.93)]):
        b_n = int(0.16 * SR)
        sub = pitched_sine_sweep(b_n, 138.0 * pitch, 34.0 * pitch, 0.032) * env_ad(b_n, 0.001, 0.125)
        grit = (
            pink_noise(b_n, rng) * 0.55
            + brown_noise(b_n, rng) * 0.45
            + crackle_noise(b_n, 580.0, rng) * 0.75
        )
        grit = butter_bp(grit, 260.0, 5600.0, order=2) * env_ad(b_n, 0.0015, 0.095)
        mix_at(out, sub * 1.1 + grit * 0.78, offset, gain=weight)

    # Scattered pebbles / dust skid after impact
    deb_n = int(0.28 * SR)
    deb = butter_bp(crackle_noise(deb_n, 360.0, rng), 1100.0, 6800.0, order=2) * env_ad(deb_n, 0.015, 0.21)
    mix_at(out, deb, 0.03, gain=0.45)

    # Heavy tactical plate carrier, ammo pouches, and rifle sling jolt
    kit_n = int(0.24 * SR)
    kit_cloth = butter_bp(pink_noise(kit_n, rng), 320.0, 1600.0, order=2) * env_ad(kit_n, 0.012, 0.16)
    kit_clack1 = modal_impulse(0.09, [880, 1940, 3850, 6400], [0.024, 0.018, 0.012, 0.007], [0.8, 1.0, 0.7, 0.4], rng=rng)
    kit_clack2 = modal_impulse(0.07, [1180, 2550, 4900], [0.018, 0.012, 0.008], [0.7, 0.85, 0.5], rng=rng)
    mix_at(out, kit_cloth / max(np.max(np.abs(kit_cloth)), 1e-6), 0.02, gain=0.38)
    mix_at(out, kit_clack1, 0.032, gain=0.42)
    mix_at(out, kit_clack2, 0.092, gain=0.28)

    out = soft_saturate(out, drive=1.75)
    ir = make_outdoor_compound_ir(rt60=0.38, predelay_ms=9.0, slap_ms=48.0, slap_gain=0.18, rng=rng)
    return convolve_reverb(out, ir, wet=0.18, dry=0.95, extend=True)


def synth_slide_tactical() -> np.ndarray:
    """High-energy tactical knee-pad slide across wet gritty concrete (~0.82s)."""
    rng = np.random.default_rng(3203)
    dur = 0.85
    n = int(round(dur * SR))
    out = np.zeros(n, dtype=np.float64)

    # 1. Initial knee-pad drop impact (0.01s)
    drop_n = int(0.11 * SR)
    drop_thud = pitched_sine_sweep(drop_n, 155.0, 45.0, 0.022) * env_ad(drop_n, 0.002, 0.075)
    drop_pad = butter_bp(pink_noise(drop_n, rng), 350.0, 2400.0, order=2) * env_ad(drop_n, 0.002, 0.055)
    mix_at(out, drop_thud * 0.92 + drop_pad * 0.65, 0.005, gain=0.95)

    # 2. Continuous gritty Cordura + hard polymer knee-pad concrete scrape (decelerating pitch sweep!)
    sl_dur = 0.72
    sl_n = int(round(sl_dur * SR))
    t_sl = make_time(sl_dur)
    # Momentum curve: fast start -> gritty deceleration
    speed = np.exp(-t_sl / 0.42)

    # Coarse gravel & asphalt scrape bed
    grit_src = pink_noise(sl_n, rng) * 0.55 + crackle_noise(sl_n, 640.0, rng) * 0.85
    fc_grit = 520.0 + 1850.0 * speed
    grit_scrape = time_varying_bp(grit_src, fc_grit, q=1.45, block_size=96)
    grit_env = env_trapezoid(sl_n, 0.035, 0.34, 0.345) * (0.55 + 0.45 * speed)

    # Wet rain-slick hiss layer (Blacksite-09 wet asphalt texture!)
    wet_src = white_noise(sl_n, rng)
    wet_hiss = time_varying_bp(wet_src, 2200.0 + 3600.0 * speed, q=1.1, block_size=96) * grit_env * 0.36

    # Low friction rumble transmitted through armor pads
    low_rub = butter_lp(brown_noise(sl_n, rng), 210.0, order=3) * grit_env * 0.42

    slide_bed = grit_scrape * grit_env * 0.95 + wet_hiss + low_rub
    slide_bed /= max(np.max(np.abs(slide_bed)), 1e-6)
    mix_at(out, slide_bed, 0.025, gain=0.95)

    # 3. Boot plant deceleration stop & tactical gear settle at 0.64s
    stop_n = int(0.11 * SR)
    stop_scuff = butter_bp(pink_noise(stop_n, rng) + crackle_noise(stop_n, 420.0, rng) * 0.6, 480.0, 4200.0, order=2) * env_ad(stop_n, 0.008, 0.07)
    stop_thud = pitched_sine_sweep(stop_n, 130.0, 50.0, 0.018) * env_ad(stop_n, 0.004, 0.055)
    gear_tick = modal_impulse(0.08, [1320, 2740, 4950], [0.02, 0.014, 0.009], [0.7, 0.85, 0.45], rng=rng)
    mix_at(out, stop_thud * 0.55 + stop_scuff / max(np.max(np.abs(stop_scuff)), 1e-6) * 0.52, 0.62, gain=0.68)
    mix_at(out, gear_tick, 0.14, gain=0.22)
    mix_at(out, gear_tick, 0.48, gain=0.16)

    out = soft_saturate(out, drive=1.45)
    ir = make_outdoor_compound_ir(rt60=0.32, predelay_ms=9.0, slap_ms=46.0, slap_gain=0.15, rng=rng)
    return convolve_reverb(out, ir, wet=0.16, dry=0.95, extend=True)


def build_foley(exports_dir: str) -> list[dict]:
    specs = [
        ("footstep_concrete_01.wav", synth_footstep_concrete(1), -2.2, "Combat boot concrete footstep variation 1"),
        ("footstep_concrete_02.wav", synth_footstep_concrete(2), -2.2, "Combat boot concrete footstep variation 2"),
        ("footstep_concrete_03.wav", synth_footstep_concrete(3), -2.2, "Combat boot concrete footstep variation 3"),
        ("footstep_concrete_04.wav", synth_footstep_concrete(4), -2.2, "Combat boot concrete footstep variation 4"),
        ("footstep_metal_01.wav", synth_footstep_metal(1), -2.0, "Combat boot steel catwalk footstep variation 1"),
        ("footstep_metal_02.wav", synth_footstep_metal(2), -2.0, "Combat boot steel catwalk footstep variation 2"),
        ("footstep_metal_03.wav", synth_footstep_metal(3), -2.0, "Combat boot steel catwalk footstep variation 3"),
        ("footstep_metal_04.wav", synth_footstep_metal(4), -2.0, "Combat boot steel catwalk footstep variation 4"),
        ("jump_effort.wav", synth_jump_effort(), -2.6, "Tactical jump boot push-off and gear lift"),
        ("land_heavy.wav", synth_land_heavy(), -1.5, "Heavy armored dual-boot drop landing"),
        ("slide_tactical.wav", synth_slide_tactical(), -1.8, "Tactical knee-pad slide on gritty wet concrete"),
    ]

    entries = []
    for filename, sig, peak_db, desc in specs:
        out_path = os.path.join(exports_dir, filename)
        meta = finalize_and_save(out_path, sig, target_peak_db=peak_db, stereo=False, is_loop=False)
        entries.append({
            "file": f"exports/{filename}",
            "id": os.path.splitext(filename)[0],
            "category": "Foley",
            "mixerGroup": "Foley",
            "spatialBlend": 1.0,
            "loadType": "DecompressOnLoad",
            "loop": False,
            "description": desc,
            **meta,
        })
    return entries
