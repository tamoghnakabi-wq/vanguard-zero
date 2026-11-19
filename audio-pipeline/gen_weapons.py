"""
VANGUARD ZERO — Weapons Audio Synthesis Module (19 Mono 3D WAVs)
"""

from __future__ import annotations

import math
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
    butter_hp,
    butter_bp,
    biquad_peaking,
    biquad_lowshelf,
    biquad_highshelf,
    time_varying_lp,
    time_varying_bp,
    env_ad,
    env_trapezoid,
    pitched_sine_sweep,
    modal_resonator,
    modal_impulse,
    make_outdoor_compound_ir,
    make_metallic_chamber_ir,
    convolve_reverb,
    soft_saturate,
    transient_shaper,
    finalize_and_save,
)


def _mech_click(rng: np.random.Generator, freqs, decays, amps, dur: float = 0.08, noise_band=(1800, 8500)) -> np.ndarray:
    n = int(round(dur * SR))
    exc = white_noise(n, rng) * env_ad(n, 0.0004, 0.004, curve=1.2)
    res = modal_resonator(exc, freqs, decays, amps)
    snap = butter_bp(white_noise(n, rng), noise_band[0], noise_band[1], order=3) * env_ad(n, 0.0003, 0.007)
    sig = res * 0.72 + snap * 0.45
    return sig / max(np.max(np.abs(sig)), 1e-6)


def _slide_friction(rng: np.random.Generator, dur: float, f_start: float, f_end: float, q: float = 3.0) -> np.ndarray:
    n = int(round(dur * SR))
    t = make_time(dur)
    src = pink_noise(n, rng) * 0.7 + crackle_noise(n, 380.0, rng) * 0.55
    fc = np.linspace(f_start, f_end, n)
    flt = time_varying_bp(src, fc, q=q, block_size=96)
    env = env_trapezoid(n, dur * 0.14, dur * 0.52, dur * 0.34)
    # Add subtle micro-ridge modulation (Picatinny / magwell rib texture)
    mod = 0.78 + 0.22 * np.sin(2.0 * math.pi * (42.0 + 18.0 * t / max(dur, 1e-4)) * t)
    sig = flt * env * mod
    return sig / max(np.max(np.abs(sig)), 1e-6)


def synth_vx47_fire(variant: int) -> np.ndarray:
    rng = np.random.default_rng(1000 + variant * 37)
    p = 1.0 + rng.uniform(-0.045, 0.045)
    dur = 0.55
    n = int(round(dur * SR))
    t = make_time(dur)

    # 1. Supersonic muzzle crack (0-25ms, piercing shock front)
    crack_n = int(0.065 * SR)
    w = white_noise(crack_n, rng)
    fc_crack = 1400.0 + 14500.0 * np.exp(-make_time(0.065) / (0.011 * p))
    crack = time_varying_lp(w, fc_crack, q=0.95, block_size=64)
    crack = butter_hp(crack, 1150.0, order=2) * env_ad(crack_n, 0.0002, 0.024, curve=0.85)
    crack = soft_saturate(crack, drive=3.2, asym=0.18)

    # 2. High-pressure 5.56/7.62 chamber blast & mid-bass punch
    body_n = int(0.18 * SR)
    sub_thump = pitched_sine_sweep(body_n, 235.0 * p, 54.0 * p, 0.019) * env_ad(body_n, 0.0005, 0.085, curve=1.05)
    blast_noise = pink_noise(body_n, rng) * 0.65 + white_noise(body_n, rng) * 0.35
    fc_blast = 320.0 + 6800.0 * np.exp(-make_time(0.18) / (0.032 * p))
    blast = time_varying_lp(blast_noise, fc_blast, q=0.8, block_size=64) * env_ad(body_n, 0.0006, 0.115, curve=0.9)
    blast = biquad_peaking(blast, 280.0 * p, 5.5, q=1.1)
    blast = biquad_peaking(blast, 2400.0 * p, 4.0, q=1.2)
    blast = soft_saturate(blast * 1.15 + sub_thump * 0.85, drive=2.9, asym=0.12)

    # 3. Mechanical bolt carrier cycle (distinctive metallic steel teeth)
    bolt_rear = _mech_click(
        rng,
        freqs=[980 * p, 1940 * p, 3420 * p, 5650 * p],
        decays=[0.022, 0.018, 0.014, 0.009],
        amps=[0.8, 1.0, 0.75, 0.5],
        dur=0.06,
    )
    bolt_fwd = _mech_click(
        rng,
        freqs=[740 * p, 1520 * p, 2980 * p, 4880 * p],
        decays=[0.028, 0.020, 0.015, 0.010],
        amps=[0.9, 0.95, 0.7, 0.45],
        dur=0.07,
    )

    dry = np.zeros(n, dtype=np.float64)
    mix_at(dry, crack, 0.0, gain=1.05)
    mix_at(dry, blast, 0.0005, gain=1.0)
    mix_at(dry, bolt_rear, 0.007 + rng.uniform(-0.001, 0.0015), gain=0.42)
    mix_at(dry, bolt_fwd, 0.043 + rng.uniform(-0.002, 0.0025), gain=0.34)
    dry = transient_shaper(dry, attack_boost=1.45, sustain_scale=0.92)

    # 4. BLACKSITE-09 concrete compound outdoor tail + slapback echo
    ir = make_outdoor_compound_ir(
        rt60=0.86 + rng.uniform(-0.05, 0.06),
        predelay_ms=11.5 + rng.uniform(-1.0, 1.5),
        slap_ms=65.0 + rng.uniform(-3.0, 4.0),
        slap_gain=0.42,
        rng=rng,
    )
    wet_src = butter_bp(dry, 120.0, 8200.0, order=2)
    full = convolve_reverb(wet_src, ir, wet=0.52, dry=0.0, extend=True)
    mix_at(full, dry, 0.0, gain=1.0)

    # Master rifle EQ: crisp bite + muscular 120Hz punch
    full = biquad_lowshelf(full, 130.0, 2.5)
    full = biquad_peaking(full, 3200.0, 2.8, q=1.1)
    full = soft_saturate(full, drive=1.55)
    return full


def synth_vx47_reload_magout() -> np.ndarray:
    rng = np.random.default_rng(1101)
    n = int(0.55 * SR)
    out = np.zeros(n, dtype=np.float64)

    # Mag release steel lever click
    latch = _mech_click(rng, [1450, 2850, 4900, 7600], [0.015, 0.012, 0.009, 0.006], [0.7, 1.0, 0.85, 0.55], 0.05)
    mix_at(out, latch, 0.01, gain=0.85)

    # Polymer mag sliding down aluminum magwell (downward pitch sweep)
    scrape = _slide_friction(rng, 0.16, 2600.0, 920.0, q=2.6)
    mix_at(out, scrape, 0.055, gain=0.68)

    # Hollow receiver resonance as mag clears well + minor knock
    clear_pop = pitched_sine_sweep(int(0.07 * SR), 340.0, 155.0, 0.016) * env_ad(int(0.07 * SR), 0.002, 0.042)
    clear_clack = _mech_click(rng, [480, 1120, 2400], [0.025, 0.016, 0.010], [0.9, 0.7, 0.4], 0.05, (600, 3200))
    mix_at(out, clear_pop, 0.195, gain=0.75 * 0.55)
    mix_at(out, clear_clack, 0.195, gain=0.75 * 0.6)

    ir = make_metallic_chamber_ir(0.16, rng=rng)
    return convolve_reverb(out, ir, wet=0.18, dry=0.95, extend=True)


def synth_vx47_reload_magin() -> np.ndarray:
    rng = np.random.default_rng(1102)
    n = int(0.62 * SR)
    out = np.zeros(n, dtype=np.float64)

    # Guided polymer insertion upward scrape
    slide_in = _slide_friction(rng, 0.14, 1050.0, 2450.0, q=2.8)
    mix_at(out, slide_in, 0.015, gain=0.58)

    # Solid polymer-on-receiver seat clunk (authoritative low-mid thud)
    clunk_n = int(0.11 * SR)
    thud = pitched_sine_sweep(clunk_n, 210.0, 82.0, 0.018) * env_ad(clunk_n, 0.001, 0.055)
    knock = _mech_click(rng, [340, 760, 1520, 2750], [0.032, 0.022, 0.014, 0.009], [1.0, 0.85, 0.65, 0.4], 0.07, (450, 3600))
    mix_at(out, thud, 0.145, gain=0.85)
    mix_at(out, knock, 0.145, gain=0.78)

    # Sharp spring-steel mag-catch lock *SNICK* right after seat
    lock = _mech_click(rng, [1850, 3450, 5800, 8900], [0.016, 0.012, 0.009, 0.006], [0.75, 1.0, 0.85, 0.6], 0.055)
    mix_at(out, lock, 0.168, gain=0.88)

    ir = make_metallic_chamber_ir(0.17, rng=rng)
    return convolve_reverb(out, ir, wet=0.18, dry=0.95, extend=True)


def synth_vx47_reload_bolt() -> np.ndarray:
    rng = np.random.default_rng(1103)
    n = int(0.68 * SR)
    out = np.zeros(n, dtype=np.float64)

    # Stage 1: Charging handle unlatch + pull back against buffer spring
    unlatch = _mech_click(rng, [1600, 3200, 5400], [0.012, 0.010, 0.007], [0.7, 0.9, 0.65], 0.04)
    mix_at(out, unlatch, 0.01, gain=0.55)

    rail_pull = _slide_friction(rng, 0.145, 1500.0, 3600.0, q=3.8)
    # Buffer spring coiled metallic zip
    t_sp = make_time(0.145)
    spring_zip = modal_resonator(
        crackle_noise(len(t_sp), 520.0, rng) * env_trapezoid(len(t_sp), 0.015, 0.09, 0.04),
        [1180, 2360, 4120],
        [0.014, 0.011, 0.008],
        [0.5, 0.8, 0.6],
    )
    mix_at(out, rail_pull * 0.6 + spring_zip * 0.45, 0.032, gain=0.72)

    # Hard metallic stop at full rear travel
    rear_stop = _mech_click(rng, [680, 1490, 2950, 5100], [0.025, 0.018, 0.013, 0.009], [0.85, 1.0, 0.8, 0.55], 0.06)
    mix_at(out, rear_stop, 0.172, gain=0.82)

    # Stage 2: Rapid spring-driven forward slam & chamber lockup (*CHACK!*)
    rail_fwd = _slide_friction(rng, 0.075, 3400.0, 1850.0, q=3.5)
    mix_at(out, rail_fwd, 0.255, gain=0.65)

    slam_thump = pitched_sine_sweep(int(0.08 * SR), 260.0, 95.0, 0.014) * env_ad(int(0.08 * SR), 0.0005, 0.045)
    slam_steel = _mech_click(
        rng,
        [820, 1740, 3380, 5920, 8600],
        [0.036, 0.028, 0.020, 0.013, 0.008],
        [0.95, 1.0, 0.9, 0.72, 0.45],
        0.10,
    )
    mix_at(out, slam_thump, 0.322, gain=1.05 * 0.65)
    mix_at(out, slam_steel, 0.322, gain=1.05 * 1.05)

    out = transient_shaper(out, attack_boost=1.35, sustain_scale=0.95)
    ir = make_metallic_chamber_ir(0.18, rng=rng)
    return convolve_reverb(out, ir, wet=0.20, dry=0.95, extend=True)


def synth_m890_fire(variant: int) -> np.ndarray:
    rng = np.random.default_rng(1200 + variant * 53)
    p = 1.0 + rng.uniform(-0.04, 0.04)
    dur = 0.70
    n = int(round(dur * SR))

    # 1. Massive 12-gauge sub-bass chest punch
    sub_n = int(0.28 * SR)
    sub = pitched_sine_sweep(sub_n, 165.0 * p, 33.0 * p, 0.042) * env_ad(sub_n, 0.0006, 0.175, curve=0.95)
    sub_rumble = butter_lp(brown_noise(sub_n, rng), 155.0, order=3) * env_ad(sub_n, 0.001, 0.21, curve=0.9)
    sub_layer = soft_saturate(sub * 1.15 + sub_rumble * 0.85, drive=2.6, asym=0.14)

    # 2. Broad thunderous ballistic shotgun boom (fatter, wider, lower than rifle)
    boom_n = int(0.32 * SR)
    src = pink_noise(boom_n, rng) * 0.62 + white_noise(boom_n, rng) * 0.38
    fc_boom = 240.0 + 10800.0 * np.exp(-make_time(0.32) / (0.055 * p))
    boom = time_varying_lp(src, fc_boom, q=0.75, block_size=64) * env_ad(boom_n, 0.0004, 0.22, curve=0.82)
    boom = biquad_peaking(boom, 140.0 * p, 6.0, q=0.9)
    boom = biquad_peaking(boom, 1450.0 * p, 3.8, q=1.0)
    boom = soft_saturate(boom, drive=3.4, asym=0.16)

    # 3. Initial wide muzzle crack / wad burst
    crack_n = int(0.05 * SR)
    crack = butter_bp(white_noise(crack_n, rng), 1400.0, 11500.0, order=2) * env_ad(crack_n, 0.0002, 0.018)
    crack = soft_saturate(crack, drive=3.0)

    # 4. Heavy steel shotgun receiver & barrel shroud vibration
    mech = _mech_click(rng, [410 * p, 890 * p, 1720 * p, 3100 * p], [0.04, 0.028, 0.020, 0.012], [0.95, 0.9, 0.65, 0.4], 0.09)

    dry = np.zeros(n, dtype=np.float64)
    mix_at(dry, crack, 0.0, gain=0.95)
    mix_at(dry, sub_layer, 0.0, gain=1.08)
    mix_at(dry, boom, 0.0008, gain=1.05)
    mix_at(dry, mech, 0.009, gain=0.38)
    dry = transient_shaper(dry, attack_boost=1.42, sustain_scale=1.0)

    # 5. Deep echoing outdoor compound blast tail
    ir = make_outdoor_compound_ir(
        rt60=1.22 + rng.uniform(-0.06, 0.07),
        predelay_ms=13.0,
        slap_ms=74.0 + rng.uniform(-3.0, 4.0),
        slap_gain=0.48,
        rng=rng,
    )
    tail_src = butter_bp(dry, 75.0, 6500.0, order=2)
    full = convolve_reverb(tail_src, ir, wet=0.58, dry=0.0, extend=True)
    mix_at(full, dry, 0.0, gain=1.0)

    full = biquad_lowshelf(full, 105.0, 3.8)
    full = soft_saturate(full, drive=1.65)
    return full


def synth_m890_pump() -> np.ndarray:
    """Signature dual-stroke heavy metallic shotgun rack-clack (CHK - CHAK!)."""
    rng = np.random.default_rng(1210)
    n = int(0.62 * SR)
    out = np.zeros(n, dtype=np.float64)

    # Stroke 1: Rear pull (eject spent hull)
    s1_slide = _slide_friction(rng, 0.12, 1250.0, 3200.0, q=3.2)
    s1_stop = _mech_click(
        rng,
        [520, 1180, 2480, 4450, 7200],
        [0.032, 0.025, 0.018, 0.012, 0.008],
        [0.9, 1.0, 0.92, 0.65, 0.4],
        0.08,
    )
    s1_thud = pitched_sine_sweep(int(0.06 * SR), 220.0, 90.0, 0.015) * env_ad(int(0.06 * SR), 0.001, 0.035)
    mix_at(out, s1_slide, 0.012, gain=0.68)
    mix_at(out, s1_stop, 0.118, gain=0.92 * 0.95)
    mix_at(out, s1_thud, 0.118, gain=0.92 * 0.5)

    # Ejected plastic shell hollow flick during pull
    eject = _mech_click(rng, [920, 2150, 3800], [0.015, 0.011, 0.007], [0.6, 0.8, 0.45], 0.04)
    mix_at(out, eject, 0.148, gain=0.35)

    # Stroke 2: Forward slam (chamber fresh 12g shell) — punchier & slightly brighter!
    s2_slide = _slide_friction(rng, 0.11, 3100.0, 1550.0, q=3.4)
    s2_lock = _mech_click(
        rng,
        [610, 1390, 2860, 5150, 8100],
        [0.038, 0.028, 0.020, 0.013, 0.008],
        [0.95, 1.0, 0.95, 0.72, 0.48],
        0.095,
    )
    s2_thud = pitched_sine_sweep(int(0.07 * SR), 270.0, 105.0, 0.015) * env_ad(int(0.07 * SR), 0.0005, 0.042)
    mix_at(out, s2_slide, 0.228, gain=0.72)
    mix_at(out, s2_lock, 0.325, gain=1.05 * 1.05)
    mix_at(out, s2_thud, 0.325, gain=1.05 * 0.65)

    out = transient_shaper(out, attack_boost=1.38)
    ir = make_metallic_chamber_ir(0.19, rng=rng)
    return convolve_reverb(out, ir, wet=0.20, dry=0.95, extend=True)


def synth_m890_shell_insert() -> np.ndarray:
    rng = np.random.default_rng(1211)
    n = int(0.36 * SR)
    out = np.zeros(n, dtype=np.float64)

    # Brass rim touches steel loading lifter
    rim_tap = _mech_click(rng, [2400, 4800, 7900], [0.012, 0.009, 0.006], [0.65, 0.9, 0.6], 0.035)
    mix_at(out, rim_tap, 0.01, gain=0.52)

    # Plastic hull push into tubular magazine against spring
    hull_glide = _slide_friction(rng, 0.085, 1100.0, 2300.0, q=2.5)
    mix_at(out, hull_glide, 0.032, gain=0.60)

    # Crisp shell-stop latch *CLICK-SNICK* + hollow steel tube ring
    latch = _mech_click(rng, [840, 1950, 3920, 6750], [0.026, 0.018, 0.012, 0.007], [0.75, 1.0, 0.95, 0.65], 0.065)
    tube = modal_impulse(0.09, [460.0, 915.0], [0.035, 0.022], [0.45, 0.28], rng=rng)
    mix_at(out, latch, 0.112, gain=0.95)
    mix_at(out, tube, 0.112, gain=0.95 * 0.35)

    ir = make_metallic_chamber_ir(0.14, rng=rng)
    return convolve_reverb(out, ir, wet=0.16, dry=0.95, extend=True)


def synth_m19_fire(variant: int) -> np.ndarray:
    rng = np.random.default_rng(1300 + variant * 41)
    p = 1.0 + rng.uniform(-0.04, 0.04)
    dur = 0.42
    n = int(round(dur * SR))

    # 1. Sharp snappy 9mm bark (higher fundamental, shorter decay than rifle)
    snap_n = int(0.055 * SR)
    fc_snap = 1800.0 + 13500.0 * np.exp(-make_time(0.055) / (0.009 * p))
    snap = time_varying_lp(white_noise(snap_n, rng), fc_snap, q=1.0, block_size=48)
    snap = butter_hp(snap, 950.0, order=2) * env_ad(snap_n, 0.0002, 0.019, curve=0.9)
    snap = soft_saturate(snap, drive=3.0, asym=0.15)

    # 2. Compact 9mm pressure pop
    pop_n = int(0.11 * SR)
    thump = pitched_sine_sweep(pop_n, 320.0 * p, 78.0 * p, 0.012) * env_ad(pop_n, 0.0004, 0.055)
    bark = butter_bp(pink_noise(pop_n, rng), 180.0, 4200.0, order=2) * env_ad(pop_n, 0.0004, 0.068, curve=0.95)
    bark = biquad_peaking(bark, 920.0 * p, 5.0, q=1.2)
    pop = soft_saturate(thump * 0.9 + bark * 1.1, drive=2.6, asym=0.1)

    # 3. Crisp machined steel pistol slide recoil & return
    slide_back = _mech_click(rng, [1350 * p, 2750 * p, 4950 * p, 8200 * p], [0.016, 0.013, 0.010, 0.006], [0.8, 1.0, 0.85, 0.55], 0.05)
    slide_fwd = _mech_click(rng, [1080 * p, 2320 * p, 4350 * p], [0.018, 0.014, 0.009], [0.85, 0.9, 0.65], 0.05)

    dry = np.zeros(n, dtype=np.float64)
    mix_at(dry, snap, 0.0, gain=1.05)
    mix_at(dry, pop, 0.0004, gain=1.0)
    mix_at(dry, slide_back, 0.011, gain=0.45)
    mix_at(dry, slide_fwd, 0.048, gain=0.35)
    dry = transient_shaper(dry, attack_boost=1.45)

    # 4. Tighter courtyard slapback tail
    ir = make_outdoor_compound_ir(
        rt60=0.62 + rng.uniform(-0.04, 0.05),
        predelay_ms=10.0,
        slap_ms=56.0 + rng.uniform(-3.0, 3.0),
        slap_gain=0.36,
        rng=rng,
    )
    full = convolve_reverb(butter_bp(dry, 160.0, 8000.0, order=2), ir, wet=0.42, dry=0.0, extend=True)
    mix_at(full, dry, 0.0, gain=1.0)
    full = biquad_peaking(full, 2600.0, 2.5, q=1.2)
    return soft_saturate(full, drive=1.45)


def synth_m19_reload() -> np.ndarray:
    """Complete crisp tactical pistol reload (mag drop -> insert -> slide release)."""
    rng = np.random.default_rng(1310)
    n = int(1.48 * SR)
    out = np.zeros(n, dtype=np.float64)

    # 0.03s: Mag release button click + slick metal mag slide-out
    btn = _mech_click(rng, [2100, 4400, 7200], [0.010, 0.008, 0.005], [0.8, 1.0, 0.6], 0.035)
    slide_out = _slide_friction(rng, 0.11, 3200.0, 1450.0, q=3.6)
    mix_at(out, btn, 0.02, gain=0.72)
    mix_at(out, slide_out, 0.045, gain=0.58)

    # 0.48s: Fresh steel magazine inserted into polymer grip + solid palm slap seat
    slide_in = _slide_friction(rng, 0.10, 1550.0, 3100.0, q=3.5)
    seat_thud = pitched_sine_sweep(int(0.07 * SR), 250.0, 95.0, 0.014) * env_ad(int(0.07 * SR), 0.001, 0.04)
    seat_click = _mech_click(rng, [1150, 2550, 4800, 7900], [0.018, 0.014, 0.010, 0.006], [0.85, 1.0, 0.85, 0.55], 0.055)
    mix_at(out, slide_in, 0.46, gain=0.56)
    mix_at(out, seat_thud, 0.555, gain=0.95 * 0.72)
    mix_at(out, seat_click, 0.555, gain=0.95 * 0.92)

    # 0.96s: Slide release snap forward (*CHACK!*)
    sl_rail = _slide_friction(rng, 0.055, 3800.0, 2100.0, q=4.0)
    sl_lock = _mech_click(rng, [1050, 2240, 4280, 7400, 10500], [0.025, 0.019, 0.014, 0.009, 0.006], [0.9, 1.0, 0.92, 0.7, 0.4], 0.075)
    sl_thud = pitched_sine_sweep(int(0.06 * SR), 310.0, 115.0, 0.011) * env_ad(int(0.06 * SR), 0.0005, 0.032)
    mix_at(out, sl_rail, 0.94, gain=0.62)
    mix_at(out, sl_lock, 0.988, gain=1.05 * 1.05)
    mix_at(out, sl_thud, 0.988, gain=1.05 * 0.55)

    ir = make_metallic_chamber_ir(0.15, rng=rng)
    return convolve_reverb(out, ir, wet=0.18, dry=0.95, extend=True)


def synth_weapon_dryfire() -> np.ndarray:
    rng = np.random.default_rng(1401)
    n = int(0.18 * SR)
    out = np.zeros(n, dtype=np.float64)

    # Trigger sear micro-release
    sear = _mech_click(rng, [2600, 5400, 8800], [0.008, 0.006, 0.004], [0.7, 1.0, 0.6], 0.025)
    # Crisp firing pin / hammer strike on empty chamber
    pin = _mech_click(rng, [1780, 3680, 6250, 9600], [0.018, 0.014, 0.010, 0.006], [0.85, 1.0, 0.9, 0.65], 0.055)
    mix_at(out, sear, 0.003, gain=0.42)
    mix_at(out, pin, 0.015, gain=1.0)
    ir = make_metallic_chamber_ir(0.11, rng=rng)
    return convolve_reverb(out, ir, wet=0.14, dry=0.95, extend=True)


def synth_weapon_equip() -> np.ndarray:
    rng = np.random.default_rng(1402)
    n = int(0.45 * SR)
    out = np.zeros(n, dtype=np.float64)

    # Tactical Cordura gear / sling brush
    cloth_n = int(0.24 * SR)
    cloth = time_varying_bp(pink_noise(cloth_n, rng), np.linspace(650.0, 1450.0, cloth_n), q=1.4)
    cloth *= env_trapezoid(cloth_n, 0.03, 0.10, 0.11)
    mix_at(out, cloth / max(np.max(np.abs(cloth)), 1e-6), 0.01, gain=0.48)

    # Buttstock shoulder seat bump
    bump = pitched_sine_sweep(int(0.09 * SR), 145.0, 58.0, 0.022) * env_ad(int(0.09 * SR), 0.005, 0.06)
    bump = butter_lp(bump + pink_noise(int(0.09 * SR), rng) * env_ad(int(0.09 * SR), 0.004, 0.04) * 0.35, 650.0)
    mix_at(out, bump / max(np.max(np.abs(bump)), 1e-6), 0.14, gain=0.78)

    # Polymer/steel sling swivel clink + safety selector flick
    swivel = _mech_click(rng, [1250, 2650, 4800], [0.016, 0.012, 0.008], [0.8, 0.9, 0.55], 0.045)
    safety = _mech_click(rng, [2150, 4600, 7900], [0.011, 0.008, 0.005], [0.75, 1.0, 0.7], 0.035)
    mix_at(out, swivel, 0.165, gain=0.52)
    mix_at(out, safety, 0.235, gain=0.68)

    ir = make_metallic_chamber_ir(0.14, rng=rng)
    return convolve_reverb(out, ir, wet=0.14, dry=0.95, extend=True)


def synth_weapon_ads(entering: bool) -> np.ndarray:
    rng = np.random.default_rng(1405 if entering else 1406)
    dur = 0.24 if entering else 0.21
    n = int(round(dur * SR))
    out = np.zeros(n, dtype=np.float64)

    # Subtle tactical sleeve / gloved grip movement (realistic, never cartoonish swoosh)
    c_n = int(0.16 * SR)
    fc = np.linspace(550.0, 1280.0, c_n) if entering else np.linspace(1150.0, 480.0, c_n)
    cloth = time_varying_bp(pink_noise(c_n, rng), fc, q=1.35) * env_trapezoid(c_n, 0.025, 0.05, 0.085)
    cloth /= max(np.max(np.abs(cloth)), 1e-6)
    mix_at(out, cloth, 0.01, gain=0.58)

    # Cheek-weld / grip settle thump
    th_n = int(0.075 * SR)
    f0, f1 = (135.0, 62.0) if entering else (110.0, 52.0)
    thump = pitched_sine_sweep(th_n, f0, f1, 0.018) * env_ad(th_n, 0.004, 0.045)
    mix_at(out, thump, 0.075 if entering else 0.045, gain=0.68 if entering else 0.55)

    # Subtle optic/handguard micro-tick on ADS lock-in
    if entering:
        tick = _mech_click(rng, [1950, 3900, 6400], [0.009, 0.007, 0.005], [0.6, 0.9, 0.5], 0.03)
        mix_at(out, tick, 0.115, gain=0.45)

    return out


def synth_shell_casing_concrete(variant: int) -> np.ndarray:
    """Rigid-body bouncing brass casing physics on concrete/asphalt."""
    rng = np.random.default_rng(1500 + variant * 71)
    dur = 0.62
    n = int(round(dur * SR))
    out = np.zeros(n, dtype=np.float64)

    # Base cylindrical brass modal spectrum (5.56/9mm spent casing)
    base_pitch = 1.0 + (0.08 if variant == 2 else -0.03) + rng.uniform(-0.02, 0.02)
    base_modes = np.array([4260.0, 6180.0, 8640.0, 11750.0, 14900.0]) * base_pitch
    base_decays = np.array([0.075, 0.058, 0.044, 0.032, 0.022])
    base_amps = np.array([0.72, 1.0, 0.85, 0.62, 0.38])

    # Gravity bounce sequence with realistic restitution (~0.58) & spin modulation
    bounce_times = [0.005]
    dt = 0.115 + rng.uniform(-0.012, 0.015)
    for _ in range(5):
        bounce_times.append(bounce_times[-1] + dt)
        dt *= rng.uniform(0.54, 0.66)

    for i, bt in enumerate(bounce_times):
        energy = (0.68 ** i) * rng.uniform(0.85, 1.12)
        spin_shift = rng.uniform(0.97, 1.035)
        modes = base_modes * spin_shift
        # First bounce is sharpest & rings longest; trailing bounces dampen faster as casing rolls flat
        decays = base_decays * (0.92 ** i)
        ring = modal_impulse(0.18, modes, decays, base_amps, rng=rng)
        # Microscopic stone contact click at impact instant
        click_n = int(0.012 * SR)
        stone_click = butter_bp(white_noise(click_n, rng), 2800.0, 12000.0, order=2) * env_ad(click_n, 0.0002, 0.004)
        mix_at(out, ring * 0.85, bt, gain=energy)
        mix_at(out, stone_click * 0.45, bt, gain=energy)

    # Subtle concrete floor early bounce
    ir = make_metallic_chamber_ir(0.15, rng=rng)
    out = convolve_reverb(out, ir, wet=0.16, dry=0.95, extend=True)
    return butter_hp(out, 650.0, order=2)


def build_weapons(exports_dir: str) -> list[dict]:
    specs = [
        ("vx47_fire_01.wav", synth_vx47_fire(1), -0.9, "VX-47 assault rifle gunshot variation 1"),
        ("vx47_fire_02.wav", synth_vx47_fire(2), -0.9, "VX-47 assault rifle gunshot variation 2"),
        ("vx47_fire_03.wav", synth_vx47_fire(3), -0.9, "VX-47 assault rifle gunshot variation 3"),
        ("vx47_reload_magout.wav", synth_vx47_reload_magout(), -2.2, "VX-47 magazine release and pull out"),
        ("vx47_reload_magin.wav", synth_vx47_reload_magin(), -2.0, "VX-47 magazine insertion and catch lock"),
        ("vx47_reload_bolt.wav", synth_vx47_reload_bolt(), -1.8, "VX-47 charging handle pull and bolt slam"),
        ("m890_fire_01.wav", synth_m890_fire(1), -0.8, "M890 12-gauge shotgun blast variation 1"),
        ("m890_fire_02.wav", synth_m890_fire(2), -0.8, "M890 12-gauge shotgun blast variation 2"),
        ("m890_pump.wav", synth_m890_pump(), -1.8, "M890 dual-stroke pump action rack-clack"),
        ("m890_shell_insert.wav", synth_m890_shell_insert(), -2.5, "M890 tubular magazine shell insert"),
        ("m19_fire_01.wav", synth_m19_fire(1), -1.0, "M19 9mm sidearm shot variation 1"),
        ("m19_fire_02.wav", synth_m19_fire(2), -1.0, "M19 9mm sidearm shot variation 2"),
        ("m19_reload.wav", synth_m19_reload(), -2.0, "M19 tactical pistol full reload sequence"),
        ("weapon_dryfire.wav", synth_weapon_dryfire(), -2.5, "Dry fire firing pin hammer click"),
        ("weapon_equip.wav", synth_weapon_equip(), -2.8, "Weapon draw, shoulder seat, and safety click"),
        ("weapon_ads_in.wav", synth_weapon_ads(True), -3.5, "Aim down sights raise and cheek-weld"),
        ("weapon_ads_out.wav", synth_weapon_ads(False), -3.8, "Aim down sights lower"),
        ("shell_casing_concrete_01.wav", synth_shell_casing_concrete(1), -3.0, "Spent brass casing bouncing on concrete variation 1"),
        ("shell_casing_concrete_02.wav", synth_shell_casing_concrete(2), -3.0, "Spent brass casing bouncing on concrete variation 2"),
    ]

    entries = []
    for filename, sig, peak_db, desc in specs:
        out_path = os.path.join(exports_dir, filename)
        meta = finalize_and_save(out_path, sig, target_peak_db=peak_db, stereo=False, is_loop=False)
        entries.append({
            "file": f"exports/{filename}",
            "id": os.path.splitext(filename)[0],
            "category": "Weapons",
            "mixerGroup": "Weapons",
            "spatialBlend": 1.0,
            "loadType": "DecompressOnLoad",
            "loop": False,
            "description": desc,
            **meta,
        })
    return entries
