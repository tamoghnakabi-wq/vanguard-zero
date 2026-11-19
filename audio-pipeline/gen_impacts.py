"""
VANGUARD ZERO — Impacts, Combat Feedback & Explosion Synthesis Module (13 WAVs)
"""

from __future__ import annotations

import math
import os
import numpy as np
from dsp_core import (
    SR,
    make_time,
    mix_at,
    mix_at_circular,
    white_noise,
    pink_noise,
    brown_noise,
    crackle_noise,
    butter_lp,
    butter_hp,
    butter_bp,
    biquad_peaking,
    biquad_lowshelf,
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
    haas_stereo,
    stereo_widen,
    finalize_and_save,
)


def synth_impact_concrete(variant: int) -> np.ndarray:
    rng = np.random.default_rng(2000 + variant * 43)
    p = 1.0 + rng.uniform(-0.07, 0.07)
    n = int(0.65 * SR)
    out = np.zeros(n, dtype=np.float64)

    # 1. Sharp supersonic bullet stone chip transient
    chip_n = int(0.06 * SR)
    fc = 1100.0 + 11500.0 * np.exp(-make_time(0.06) / (0.009 * p))
    chip = time_varying_lp(white_noise(chip_n, rng), fc, q=1.1, block_size=48) * env_ad(chip_n, 0.0002, 0.025)
    chip = soft_saturate(chip, drive=2.8)

    # 2. Dense concrete thud
    thud_n = int(0.08 * SR)
    thud = pitched_sine_sweep(thud_n, 250.0 * p, 64.0 * p, 0.014) * env_ad(thud_n, 0.0004, 0.045)
    stone_body = butter_bp(pink_noise(thud_n, rng), 260.0, 1900.0, order=2) * env_ad(thud_n, 0.0005, 0.055)

    # 3. Cascading concrete chips & gravel debris scatter
    deb_n = int(0.46 * SR)
    cr = crackle_noise(deb_n, 290.0, rng)
    debris = butter_bp(cr, 1400.0, 7800.0, order=2) * env_ad(deb_n, 0.015, 0.34, curve=0.75)
    # Discrete pebble bounces
    for i in range(6):
        bt = 0.045 + i * rng.uniform(0.032, 0.062)
        peb = modal_impulse(0.04, [1850 * p * rng.uniform(0.85, 1.25), 3900 * p], [0.008, 0.005], [0.7, 0.5], rng=rng)
        mix_at(out, peb, bt, gain=0.45 * (0.72 ** i))

    mix_at(out, chip, 0.0, gain=1.05)
    mix_at(out, thud * 0.78 + stone_body * 0.85, 0.0004, gain=0.95)
    mix_at(out, debris, 0.018, gain=0.58)

    ir = make_outdoor_compound_ir(rt60=0.42, predelay_ms=10.0, slap_ms=52.0, slap_gain=0.22, rng=rng)
    return convolve_reverb(out, ir, wet=0.22, dry=0.95, extend=True)


def synth_impact_metal(variant: int) -> np.ndarray:
    rng = np.random.default_rng(2100 + variant * 59)
    p = 1.0 + (0.09 if variant == 2 else -0.05)
    n = int(0.75 * SR)
    out = np.zeros(n, dtype=np.float64)

    # 1. Hard steel armor/container strike + modal clang
    imp_n = int(0.05 * SR)
    exc = white_noise(imp_n, rng) * env_ad(imp_n, 0.0002, 0.008)
    clang = modal_resonator(
        np.pad(exc, (0, int(0.42 * SR))),
        freqs=[520 * p, 1185 * p, 2240 * p, 3890 * p, 6450 * p, 9200 * p],
        decays=[0.24, 0.19, 0.14, 0.095, 0.055, 0.03],
        amps=[0.85, 1.0, 0.92, 0.78, 0.52, 0.3],
    )
    clang = soft_saturate(clang, drive=2.2)

    # 2. High-velocity tumbling bullet ricochet zip/ping
    ric_dur = 0.24 if variant == 1 else 0.19
    t_r = make_time(ric_dur)
    ric_n = len(t_r)
    f_start = 4200.0 * p if variant == 1 else 3450.0 * p
    f_end = 920.0 * p if variant == 1 else 1350.0 * p
    # Curved Doppler pitch drop + subtle tumbling spin vibrato
    f_inst = f_end + (f_start - f_end) * np.exp(-t_r / (ric_dur * 0.42))
    f_inst *= 1.0 + 0.032 * np.sin(2.0 * math.pi * 68.0 * t_r)
    ph = 2.0 * math.pi * np.cumsum(f_inst) / SR
    ric = (np.sin(ph) + 0.35 * np.sin(2.0 * ph + 0.4) + 0.15 * np.sin(3.0 * ph)) * env_ad(ric_n, 0.004, ric_dur * 0.85, curve=1.15)
    ric = butter_bp(ric, 750.0, 7500.0, order=2)

    # 3. Impact spark sizzle burst
    sparks = butter_bp(crackle_noise(int(0.16 * SR), 420.0, rng), 3500.0, 13000.0, order=2) * env_ad(int(0.16 * SR), 0.0005, 0.095)

    mix_at(out, clang, 0.0, gain=1.0)
    mix_at(out, ric, 0.006, gain=0.52)
    mix_at(out, sparks, 0.002, gain=0.45)

    ir = make_outdoor_compound_ir(rt60=0.48, predelay_ms=11.0, slap_ms=58.0, slap_gain=0.25, rng=rng)
    return convolve_reverb(out, ir, wet=0.24, dry=0.95, extend=True)


def synth_impact_flesh() -> np.ndarray:
    rng = np.random.default_rng(2201)
    n = int(0.24 * SR)
    out = np.zeros(n, dtype=np.float64)

    # Damped blunt kinetic impact thud
    thud_n = int(0.11 * SR)
    thud = pitched_sine_sweep(thud_n, 175.0, 46.0, 0.018) * env_ad(thud_n, 0.0008, 0.068)
    # Dense low-mid muted slap (tactical fabric + tissue, heavily damped above 1.9kHz)
    slap_n = int(0.08 * SR)
    slap = butter_bp(pink_noise(slap_n, rng), 180.0, 1750.0, order=3) * env_ad(slap_n, 0.0006, 0.036)
    slap = biquad_peaking(slap, 420.0, 6.0, q=1.2)
    # Tiny crisp fabric tear transient
    tear = butter_bp(white_noise(int(0.03 * SR), rng), 1200.0, 3800.0, order=2) * env_ad(int(0.03 * SR), 0.0003, 0.012)

    mix_at(out, thud, 0.0, gain=1.05)
    mix_at(out, slap, 0.0, gain=0.9)
    mix_at(out, tear, 0.001, gain=0.38)
    return soft_saturate(out, drive=1.9)


def synth_impact_armor() -> np.ndarray:
    rng = np.random.default_rng(2202)
    n = int(0.32 * SR)
    out = np.zeros(n, dtype=np.float64)

    # Ceramic ballistic plate strike crack (composite high-density knock)
    plate_exc = white_noise(int(0.04 * SR), rng) * env_ad(int(0.04 * SR), 0.0002, 0.008)
    plate = modal_resonator(
        np.pad(plate_exc, (0, int(0.14 * SR))),
        freqs=[760, 1590, 2940, 4850],
        decays=[0.042, 0.030, 0.020, 0.012],
        amps=[0.9, 1.0, 0.85, 0.55],
    )
    # Blunt kinetic chest-plate thud
    thud = pitched_sine_sweep(int(0.11 * SR), 195.0, 54.0, 0.016) * env_ad(int(0.11 * SR), 0.0005, 0.062)
    # Kevlar weave snap + carrier pouch rattle
    kevlar = butter_bp(crackle_noise(int(0.09 * SR), 350.0, rng), 1500.0, 6200.0, order=2) * env_ad(int(0.09 * SR), 0.0004, 0.045)

    mix_at(out, plate, 0.0, gain=0.95)
    mix_at(out, thud, 0.0, gain=0.92)
    mix_at(out, kevlar, 0.002, gain=0.52)

    out = soft_saturate(out, drive=2.1)
    ir = make_metallic_chamber_ir(0.13, rng=rng)
    return convolve_reverb(out, ir, wet=0.14, dry=0.95, extend=True)


def synth_hitmarker_body() -> np.ndarray:
    """Crisp, ultra-fast modern AAA tactical FPS hitmarker tick (Stereo 2D)."""
    rng = np.random.default_rng(2301)
    n = int(0.09 * SR)
    out = np.zeros(n, dtype=np.float64)

    # Tight modern tactile double-tick (like MW/Valorant: sharp damped percussive click, zero synth cheese!)
    t1_exc = white_noise(int(0.025 * SR), rng) * env_ad(int(0.025 * SR), 0.0002, 0.005)
    t1 = modal_resonator(
        np.pad(t1_exc, (0, int(0.03 * SR))),
        freqs=[1120.0, 2380.0, 4150.0],
        decays=[0.012, 0.009, 0.006],
        amps=[0.85, 1.0, 0.65],
    )
    knock = pitched_sine_sweep(int(0.025 * SR), 420.0, 140.0, 0.005) * env_ad(int(0.025 * SR), 0.0002, 0.014)
    # Subtle micro-second tap 11ms later gives iconic tactile "x-mark" crispness
    t2 = modal_impulse(0.03, [1950.0, 3650.0], [0.009, 0.006], [0.8, 0.55], rng=rng)

    mix_at(out, t1, 0.0, gain=0.9)
    mix_at(out, knock, 0.0, gain=0.55)
    mix_at(out, t2, 0.011, gain=0.48)
    out = transient_shaper(out, attack_boost=1.4)
    return haas_stereo(out, delay_ms=2.8, side_mix=0.35)


def synth_hitmarker_headshot() -> np.ndarray:
    """Iconic AAA visceral helmet crunch + crystalline metallic chime (Stereo 2D)."""
    rng = np.random.default_rng(2302)
    n = int(0.42 * SR)
    mono = np.zeros(n, dtype=np.float64)

    # 1. Visceral ballistic helmet crack + bone/composite crunch (0-55ms)
    crunch_n = int(0.09 * SR)
    cr = crackle_noise(crunch_n, 720.0, rng) * 0.75 + white_noise(crunch_n, rng) * 0.45
    crunch = butter_bp(cr, 950.0, 6800.0, order=2) * env_ad(crunch_n, 0.0003, 0.048, curve=0.9)
    crunch = biquad_peaking(crunch, 2200.0, 5.5, q=1.2)
    crunch = soft_saturate(crunch, drive=2.8)

    # Heavy dome thump punch
    punch = pitched_sine_sweep(int(0.065 * SR), 320.0, 88.0, 0.012) * env_ad(int(0.065 * SR), 0.0003, 0.038)

    # 2. Iconic crisp steel helmet ping-chime (cuts cleanly through any firefight!)
    chime_l = modal_impulse(
        0.34,
        freqs=[1980.0, 2970.0, 4455.0, 6280.0, 8910.0],
        decays=[0.20, 0.16, 0.12, 0.075, 0.042],
        amps=[0.55, 1.0, 0.72, 0.45, 0.25],
        rng=rng,
    )
    chime_r = modal_impulse(
        0.34,
        freqs=[1988.0, 2982.0, 4472.0, 6310.0, 8950.0],
        decays=[0.20, 0.16, 0.12, 0.075, 0.042],
        amps=[0.55, 1.0, 0.72, 0.45, 0.25],
        rng=rng,
    )

    mix_at(mono, crunch, 0.0, gain=1.05)
    mix_at(mono, punch, 0.0, gain=0.72)

    stereo = np.vstack([mono.copy(), mono.copy()])
    mix_at(stereo[0], chime_l, 0.003, gain=0.82)
    mix_at(stereo[1], chime_r, 0.004, gain=0.82)
    stereo = stereo_widen(stereo, width=1.25)
    return stereo


def synth_kill_confirm() -> np.ndarray:
    """Subtle, deeply satisfying AAA low-high tactical elimination confirm (Stereo 2D)."""
    rng = np.random.default_rng(2303)
    n = int(0.45 * SR)
    left = np.zeros(n, dtype=np.float64)
    right = np.zeros(n, dtype=np.float64)

    # Low tactile confirm weight (instant)
    low_thump = pitched_sine_sweep(int(0.14 * SR), 148.0, 62.0, 0.025) * env_ad(int(0.14 * SR), 0.002, 0.095)
    # Softened tactical two-step metallic-glass pulse (D5=587.3 -> A5=880.0, subtle and authoritative)
    tone1_l = modal_impulse(0.22, [587.3, 1174.6, 2349.2], [0.11, 0.065, 0.03], [0.9, 0.45, 0.18], rng=rng)
    tone1_r = modal_impulse(0.22, [589.0, 1178.0, 2356.0], [0.11, 0.065, 0.03], [0.9, 0.45, 0.18], rng=rng)

    tone2_l = modal_impulse(0.32, [880.0, 1760.0, 3520.0], [0.18, 0.11, 0.055], [0.95, 0.55, 0.24], rng=rng)
    tone2_r = modal_impulse(0.32, [883.0, 1766.0, 3532.0], [0.18, 0.11, 0.055], [0.95, 0.55, 0.24], rng=rng)

    # Crisp transient micro-snap on each step
    snap = butter_bp(white_noise(int(0.02 * SR), rng), 2400.0, 7200.0, order=2) * env_ad(int(0.02 * SR), 0.0003, 0.007)

    mix_at(left, low_thump, 0.0, gain=0.85)
    mix_at(right, low_thump, 0.0, gain=0.85)
    mix_at(left, tone1_l, 0.002, gain=0.68)
    mix_at(left, snap, 0.002, gain=0.68 * 0.35)
    mix_at(right, tone1_r, 0.004, gain=0.68)
    mix_at(right, snap, 0.004, gain=0.68 * 0.35)
    mix_at(left, tone2_l, 0.058, gain=0.88)
    mix_at(left, snap, 0.058, gain=0.88 * 0.45)
    mix_at(right, tone2_r, 0.060, gain=0.88)
    mix_at(right, snap, 0.060, gain=0.88 * 0.45)

    return stereo_widen(np.vstack([left, right]), width=1.2)


def synth_player_hurt() -> np.ndarray:
    """Visceral kinetic hit on player vest + short stifled breath knock (Mono 3D)."""
    rng = np.random.default_rng(2401)
    n = int(0.38 * SR)
    out = np.zeros(n, dtype=np.float64)

    # Heavy blunt chest strike
    thud = pitched_sine_sweep(int(0.14 * SR), 160.0, 42.0, 0.024) * env_ad(int(0.14 * SR), 0.001, 0.085)
    vest = butter_bp(pink_noise(int(0.09 * SR), rng), 220.0, 1850.0, order=3) * env_ad(int(0.09 * SR), 0.001, 0.048)

    # Organic stifled breath-exhale / chest grunt via vocal-tract formants (F1=520Hz, F2=1180Hz, F3=2450Hz)
    gr_n = int(0.22 * SR)
    t = make_time(0.22)
    # Low strained glottal pulse train + breath turbulence
    f0 = 112.0 - 24.0 * (t / 0.22)
    ph = 2.0 * math.pi * np.cumsum(f0) / SR
    glottal = np.tanh(2.5 * np.sin(ph) + 1.2 * np.sin(2.0 * ph) + 0.6 * np.sin(3.0 * ph))
    breath = pink_noise(gr_n, rng) * 0.75 + glottal * 0.32
    f1 = butter_bp(breath, 380.0, 680.0, order=3) * 0.9
    f2 = butter_bp(breath, 980.0, 1450.0, order=3) * 0.6
    f3 = butter_bp(breath, 2100.0, 2900.0, order=2) * 0.28
    grunt = (f1 + f2 + f3) * env_ad(gr_n, 0.012, 0.16, curve=1.1)

    mix_at(out, thud, 0.0, gain=1.05)
    mix_at(out, vest, 0.0, gain=0.72)
    mix_at(out, grunt / max(np.max(np.abs(grunt)), 1e-6), 0.022, gain=0.65)
    return soft_saturate(out, drive=1.7)


def synth_player_death() -> np.ndarray:
    """Fatal strike + sub-bass blackout drop + heavy armored body/gear collapse (Mono 3D)."""
    rng = np.random.default_rng(2402)
    n = int(1.35 * SR)
    out = np.zeros(n, dtype=np.float64)

    # 1. Fatal impact & heavy chest shockwave drop
    hit = pitched_sine_sweep(int(0.28 * SR), 140.0, 30.0, 0.065) * env_ad(int(0.28 * SR), 0.001, 0.22)
    slap = butter_bp(pink_noise(int(0.14 * SR), rng), 160.0, 1950.0, order=2) * env_ad(int(0.14 * SR), 0.001, 0.08)

    # 2. Brief low-pressure blackout swoosh / fading breath
    drop_n = int(0.55 * SR)
    blackout = butter_lp(brown_noise(drop_n, rng), 190.0, order=3) * env_ad(drop_n, 0.03, 0.44)

    # 3. Heavy armored body & tactical gear collapse onto concrete at 0.36s
    fall_thud = pitched_sine_sweep(int(0.22 * SR), 115.0, 34.0, 0.045) * env_ad(int(0.22 * SR), 0.008, 0.16)
    fall_grit = butter_bp(pink_noise(int(0.26 * SR), rng) + crackle_noise(int(0.26 * SR), 180.0, rng) * 0.5, 180.0, 2600.0, order=2) * env_ad(int(0.26 * SR), 0.012, 0.18)
    # Rifle / tactical gear clatter settle at 0.44s & 0.56s
    gear1 = modal_impulse(0.12, [680, 1450, 2900, 4800], [0.025, 0.018, 0.012, 0.008], [0.8, 1.0, 0.7, 0.4], rng=rng)
    gear2 = modal_impulse(0.09, [920, 2100, 4200], [0.02, 0.014, 0.009], [0.7, 0.85, 0.5], rng=rng)

    mix_at(out, hit, 0.0, gain=1.05)
    mix_at(out, slap, 0.0, gain=0.75)
    mix_at(out, blackout / max(np.max(np.abs(blackout)), 1e-6), 0.04, gain=0.62)
    mix_at(out, fall_thud, 0.36, gain=0.92)
    mix_at(out, fall_grit / max(np.max(np.abs(fall_grit)), 1e-6), 0.365, gain=0.55)
    mix_at(out, gear1, 0.42, gain=0.42)
    mix_at(out, gear2, 0.54, gain=0.28)

    ir = make_outdoor_compound_ir(rt60=0.45, predelay_ms=10.0, slap_ms=54.0, slap_gain=0.2, rng=rng)
    return convolve_reverb(out, ir, wet=0.2, dry=0.95, extend=True)


def synth_low_health_heartbeat_loop() -> np.ndarray:
    """
    Seamless 3.000s (4 stressed lub-dub beats at 80 BPM) tactical low-health heartbeat loop.
    Uses circular placement so boundary continuity is mathematically exact.
    """
    rng = np.random.default_rng(2403)
    dur = 3.000  # 4 beats * 0.750s exact
    n = int(round(dur * SR))
    mono = np.zeros(n, dtype=np.float64)

    # Subtle continuous muffled arterial blood-flow murmur (circularly filtered brown noise)
    raw = brown_noise(n, rng)
    # Circular spectral bandpass 35-140Hz so loop seam has zero filter edge jump
    spec = np.fft.rfft(raw)
    freqs = np.fft.rfftfreq(n, d=1.0 / SR)
    mask = np.exp(-0.5 * ((freqs - 72.0) / 36.0) ** 2) * (freqs > 22.0)
    blood_bed = np.fft.irfft(spec * mask, n=n)
    blood_bed /= max(np.max(np.abs(blood_bed)), 1e-6)
    mono += blood_bed * 0.14

    # 4 lub-dub cycles with realistic subtle cardiac cadence
    beat_period = 0.750
    for b in range(4):
        t0 = b * beat_period + 0.08
        vel = 1.0 if (b % 2 == 0) else 0.93

        # LUB (S1): deep resonant ventricular closure thump
        lub_n = int(0.19 * SR)
        lub_tone = pitched_sine_sweep(lub_n, 74.0, 35.0, 0.048) * env_ad(lub_n, 0.014, 0.14, curve=1.2)
        lub_whoosh = butter_lp(brown_noise(lub_n, rng), 135.0, order=3) * env_ad(lub_n, 0.015, 0.12)
        lub = soft_saturate(lub_tone * 0.95 + lub_whoosh * 0.35, drive=1.45)

        # DUB (S2): slightly tighter, higher valve closure 215ms after LUB
        dub_n = int(0.16 * SR)
        dub_tone = pitched_sine_sweep(dub_n, 92.0, 44.0, 0.036) * env_ad(dub_n, 0.010, 0.115, curve=1.25)
        dub_whoosh = butter_lp(brown_noise(dub_n, rng), 165.0, order=3) * env_ad(dub_n, 0.010, 0.095)
        dub = soft_saturate(dub_tone * 0.85 + dub_whoosh * 0.28, drive=1.35)

        mix_at_circular(mono, lub, t0, gain=1.0 * vel)
        mix_at_circular(mono, dub, t0 + 0.215, gain=0.76 * vel)

    # Subtle stereo chest pressure warmth (kept mono-safe below 100Hz!)
    left = mono.copy()
    right = np.roll(mono, 18)  # 0.375ms micro-phase shift, wraparound-safe!
    # Keep sub strictly mono via FFT circular M/S
    mid = 0.5 * (left + right)
    side = 0.5 * (left - right)
    s_spec = np.fft.rfft(side)
    s_freqs = np.fft.rfftfreq(n, d=1.0 / SR)
    s_spec[s_freqs < 105.0] = 0.0
    side_clean = np.fft.irfft(s_spec, n=n) * 0.45
    return np.vstack([mid + side_clean, mid - side_clean])


def synth_explosion_frag() -> np.ndarray:
    """Massive AAA fragmentation grenade detonation: shockwave + concussion + shrapnel + debris."""
    rng = np.random.default_rng(2501)
    n = int(2.2 * SR)
    dry = np.zeros(n, dtype=np.float64)

    # 1. Blinding supersonic initial blast crack (0-55ms)
    crack_n = int(0.08 * SR)
    fc_c = 1500.0 + 15000.0 * np.exp(-make_time(0.08) / 0.014)
    crack = time_varying_lp(white_noise(crack_n, rng), fc_c, q=0.9, block_size=64) * env_ad(crack_n, 0.0002, 0.038)
    crack = soft_saturate(crack, drive=3.6, asym=0.18)

    # 2. Massive sub-bass concussion shockwave (0-900ms)
    sub_n = int(1.15 * SR)
    sub_sweep = pitched_sine_sweep(sub_n, 128.0, 23.0, 0.145) * env_ad(sub_n, 0.001, 0.78, curve=0.92)
    sub_roar = butter_lp(brown_noise(sub_n, rng), 130.0, order=3) * env_ad(sub_n, 0.003, 0.92, curve=0.85)
    sub_layer = soft_saturate(sub_sweep * 1.15 + sub_roar * 1.05, drive=2.8, asym=0.12)

    # 3. Fiery expanding explosion roar (time-varying spectral darkening)
    roar_n = int(1.55 * SR)
    r_src = pink_noise(roar_n, rng) * 0.65 + brown_noise(roar_n, rng) * 0.45 + white_noise(roar_n, rng) * 0.25
    fc_r = 180.0 + 9200.0 * np.exp(-make_time(1.55) / 0.185)
    roar = time_varying_lp(r_src, fc_r, q=0.72, block_size=128) * env_ad(roar_n, 0.002, 1.15, curve=0.78)
    roar = biquad_peaking(roar, 110.0, 5.5, q=0.85)
    roar = soft_saturate(roar, drive=3.0, asym=0.15)

    # 4. Flying steel shrapnel whizzes & metallic fragments (60ms - 650ms)
    for k in range(5):
        st = 0.055 + k * rng.uniform(0.055, 0.11)
        sdur = rng.uniform(0.11, 0.21)
        ts = make_time(sdur)
        sn = len(ts)
        f0 = rng.uniform(2200.0, 4800.0)
        f1 = rng.uniform(620.0, 1400.0)
        fi = f1 + (f0 - f1) * np.exp(-ts / (sdur * 0.45))
        whiz = np.sin(2.0 * math.pi * np.cumsum(fi) / SR) * env_ad(sn, 0.012, sdur * 0.75)
        mix_at(dry, whiz, st, gain=0.16 * (0.85 ** k))

    # 5. Cascading concrete & steel debris rain (180ms - 1.95s)
    deb_n = int(1.8 * SR)
    deb_cr = crackle_noise(deb_n, 220.0, rng)
    debris = butter_bp(deb_cr, 750.0, 6800.0, order=2) * env_ad(deb_n, 0.12, 1.45, curve=0.8)
    mix_at(dry, debris, 0.16, gain=0.38)
    for k in range(10):
        dt = 0.28 + rng.uniform(0.0, 1.35)
        cl = modal_impulse(0.09, [rng.uniform(900, 2600), rng.uniform(2800, 6200)], [0.025, 0.014], [0.7, 0.5], rng=rng)
        mix_at(dry, cl, dt, gain=rng.uniform(0.08, 0.22))

    mix_at(dry, crack, 0.0, gain=1.1)
    mix_at(dry, sub_layer, 0.0, gain=1.15)
    mix_at(dry, roar, 0.002, gain=1.05)

    # Massive outdoor compound explosion tail & concrete canyon reflection
    ir = make_outdoor_compound_ir(rt60=1.95, predelay_ms=18.0, slap_ms=86.0, slap_gain=0.52, rng=rng)
    full = convolve_reverb(butter_bp(dry, 45.0, 6200.0, order=2), ir, wet=0.62, dry=0.0, extend=True)
    mix_at(full, dry, 0.0, gain=1.0)
    full = biquad_lowshelf(full, 95.0, 4.0)
    return soft_saturate(full, drive=1.65)


def build_impacts(exports_dir: str) -> list[dict]:
    specs = [
        ("impact_concrete_01.wav", synth_impact_concrete(1), -1.4, False, False, 1.0, "Bullet impact on concrete with stone chip debris v1"),
        ("impact_concrete_02.wav", synth_impact_concrete(2), -1.4, False, False, 1.0, "Bullet impact on concrete with stone chip debris v2"),
        ("impact_metal_01.wav", synth_impact_metal(1), -1.2, False, False, 1.0, "Bullet impact on steel with ricochet ping v1"),
        ("impact_metal_02.wav", synth_impact_metal(2), -1.2, False, False, 1.0, "Bullet impact on steel with ricochet ping v2"),
        ("impact_flesh_01.wav", synth_impact_flesh(), -1.8, False, False, 1.0, "Dull kinetic body impact thud"),
        ("impact_armor_01.wav", synth_impact_armor(), -1.5, False, False, 1.0, "Ballistic Kevlar and ceramic armor plate strike"),
        ("hitmarker_body.wav", synth_hitmarker_body(), -1.6, True, False, 0.0, "Crisp modern tactical body hitmarker tick"),
        ("hitmarker_headshot.wav", synth_hitmarker_headshot(), -1.1, True, False, 0.0, "Visceral helmet crunch and metallic headshot chime"),
        ("kill_confirm.wav", synth_kill_confirm(), -1.8, True, False, 0.0, "Subtle low-high tactical elimination confirm cue"),
        ("player_hurt.wav", synth_player_hurt(), -1.5, False, False, 1.0, "Player hit chest impact and stifled breath"),
        ("player_death.wav", synth_player_death(), -1.2, False, False, 1.0, "Player elimination blackout drop and gear collapse"),
        ("low_health_heartbeat.wav", synth_low_health_heartbeat_loop(), -2.0, True, True, 0.0, "Seamless 80 BPM stressed low-health heartbeat loop"),
        ("explosion_frag.wav", synth_explosion_frag(), -0.8, False, False, 1.0, "Massive frag grenade shockwave, blast, and shrapnel tail"),
    ]

    entries = []
    for filename, sig, peak_db, is_stereo, is_loop, spatial, desc in specs:
        out_path = os.path.join(exports_dir, filename)
        meta = finalize_and_save(
            out_path,
            sig,
            target_peak_db=peak_db,
            stereo=is_stereo,
            is_loop=is_loop,
            do_trim=(not is_loop),
        )
        entries.append({
            "file": f"exports/{filename}",
            "id": os.path.splitext(filename)[0],
            "category": "Impacts",
            "mixerGroup": "Impacts",
            "spatialBlend": spatial,
            "loadType": "DecompressOnLoad",
            "loop": is_loop,
            "description": desc,
            **meta,
        })
    return entries
