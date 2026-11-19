"""
VANGUARD ZERO — Tactical UI & Military VHF Radio Announcer Synthesis Module
5 Stereo 2D Tactical UI WAVs + 10 Stereo 2D Radio-Filtered Announcer WAVs.
"""

from __future__ import annotations

import math
import os
import subprocess
import wave
import numpy as np
from dsp_core import (
    SR,
    make_time,
    mix_at,
    white_noise,
    pink_noise,
    butter_bp,
    butter_hp,
    time_varying_lp,
    env_ad,
    env_trapezoid,
    pitched_sine_sweep,
    modal_resonator,
    modal_impulse,
    make_cinematic_hall_ir,
    convolve_reverb,
    soft_saturate,
    haas_stereo,
    stereo_widen,
    process_tactical_radio,
    finalize_and_save,
)


def synth_ui_hover() -> np.ndarray:
    """Ultra-crisp ruggedized military tactile micro-tick (~45ms)."""
    rng = np.random.default_rng(5001)
    n = int(0.06 * SR)
    exc = white_noise(int(0.015 * SR), rng) * env_ad(int(0.015 * SR), 0.0002, 0.003)
    tick = modal_resonator(
        np.pad(exc, (0, n - int(0.015 * SR))),
        freqs=[2200.0, 4650.0, 7800.0],
        decays=[0.010, 0.007, 0.004],
        amps=[0.85, 1.0, 0.55],
    )
    air = butter_bp(white_noise(n, rng), 3200.0, 9500.0, order=2) * env_ad(n, 0.0002, 0.008)
    mono = tick * 0.85 + air * 0.35
    return haas_stereo(mono, delay_ms=1.8, side_mix=0.25)


def synth_ui_click() -> np.ndarray:
    """Solid military ruggedized button click + damped tactile body (~85ms)."""
    rng = np.random.default_rng(5002)
    n = int(0.11 * SR)
    out = np.zeros(n, dtype=np.float64)

    # Tactile switch press + subtle release snap 14ms later
    exc1 = white_noise(int(0.018 * SR), rng) * env_ad(int(0.018 * SR), 0.0002, 0.004)
    sw1 = modal_resonator(
        np.pad(exc1, (0, int(0.04 * SR))),
        freqs=[1180.0, 2650.0, 5100.0],
        decays=[0.016, 0.011, 0.007],
        amps=[0.9, 1.0, 0.65],
    )
    body = pitched_sine_sweep(int(0.04 * SR), 280.0, 92.0, 0.008) * env_ad(int(0.04 * SR), 0.0003, 0.022)
    sw2 = modal_impulse(0.035, [2850.0, 5600.0], [0.009, 0.006], [0.7, 0.45], rng=rng)

    mix_at(out, sw1, 0.0, gain=0.9)
    mix_at(out, body, 0.0, gain=0.68)
    mix_at(out, sw2, 0.015, gain=0.45)
    return haas_stereo(out, delay_ms=2.4, side_mix=0.30)


def synth_ui_confirm() -> np.ndarray:
    """Authoritative tactical lock-in confirmation cue (~320ms)."""
    rng = np.random.default_rng(5003)
    n = int(0.42 * SR)
    left = np.zeros(n, dtype=np.float64)
    right = np.zeros(n, dtype=np.float64)

    # Crisp tactile switch transient + low confirm thump
    thump = pitched_sine_sweep(int(0.09 * SR), 195.0, 72.0, 0.016) * env_ad(int(0.09 * SR), 0.0005, 0.055)
    sw = modal_impulse(0.04, [1650.0, 3450.0, 6200.0], [0.012, 0.009, 0.006], [0.8, 1.0, 0.55], rng=rng)

    # Clean futuristic military glass/radar rising fifth (D5=587.3 -> A5=880.0)
    p1_l = modal_impulse(0.18, [587.3, 1174.6, 2349.2], [0.095, 0.05, 0.024], [0.85, 0.35, 0.14], rng=rng)
    p1_r = modal_impulse(0.18, [589.2, 1178.4, 2356.8], [0.095, 0.05, 0.024], [0.85, 0.35, 0.14], rng=rng)
    p2_l = modal_impulse(0.28, [880.0, 1760.0, 3520.0], [0.16, 0.09, 0.042], [1.0, 0.45, 0.18], rng=rng)
    p2_r = modal_impulse(0.28, [882.8, 1765.6, 3531.2], [0.16, 0.09, 0.042], [1.0, 0.45, 0.18], rng=rng)

    mix_at(left, thump, 0.0, gain=0.72)
    mix_at(left, sw, 0.0, gain=0.65)
    mix_at(right, thump, 0.0, gain=0.72)
    mix_at(right, sw, 0.0, gain=0.65)
    mix_at(left, p1_l, 0.004, gain=0.68)
    mix_at(right, p1_r, 0.006, gain=0.68)
    mix_at(left, p2_l, 0.068, gain=0.88)
    mix_at(right, p2_r, 0.070, gain=0.88)
    return stereo_widen(np.vstack([left, right]), width=1.2)


def synth_ui_cancel() -> np.ndarray:
    """Clean muted descending tactical cancel/back cue (~220ms)."""
    rng = np.random.default_rng(5004)
    n = int(0.28 * SR)
    left = np.zeros(n, dtype=np.float64)
    right = np.zeros(n, dtype=np.float64)

    # Soft mechanical back-switch click
    sw = modal_impulse(0.035, [980.0, 2150.0, 4100.0], [0.012, 0.008, 0.005], [0.85, 0.9, 0.45], rng=rng)
    # Descending muted two-step (F4=349.2 -> D4=293.7)
    d1_l = modal_impulse(0.11, [349.2, 698.4], [0.055, 0.028], [0.85, 0.35], rng=rng)
    d1_r = modal_impulse(0.11, [350.5, 701.0], [0.055, 0.028], [0.85, 0.35], rng=rng)
    d2_l = modal_impulse(0.16, [293.7, 587.3], [0.085, 0.04], [0.95, 0.32], rng=rng)
    d2_r = modal_impulse(0.16, [294.8, 589.6], [0.085, 0.04], [0.95, 0.32], rng=rng)

    mix_at(left, sw * 0.65, 0.0, gain=1.0)
    mix_at(right, sw * 0.65, 0.0, gain=1.0)
    mix_at(left, d1_l, 0.003, gain=0.72)
    mix_at(right, d1_r, 0.005, gain=0.72)
    mix_at(left, d2_l, 0.058, gain=0.88)
    mix_at(right, d2_r, 0.060, gain=0.88)
    return stereo_widen(np.vstack([left, right]), width=1.15)


def synth_ui_match_found() -> np.ndarray:
    """Dramatic AAA tactical matchmaking lock-on alert (~1.85s)."""
    rng = np.random.default_rng(5005)
    dur = 2.0
    n = int(round(dur * SR))
    dry = np.zeros((2, n), dtype=np.float64)
    rev = np.zeros((2, n), dtype=np.float64)

    # 1. Deep cinematic lock-on sub-impact at 0.01s and 0.42s
    for idx, t0 in enumerate([0.01, 0.42]):
        sub = pitched_sine_sweep(int(0.45 * SR), 145.0, 36.0, 0.075) * env_ad(int(0.45 * SR), 0.002, 0.32)
        boom = soft_saturate(sub, drive=1.8)
        g = 0.72 if idx == 0 else 1.0
        mix_at(dry, np.vstack([boom, boom]), t0, gain=g)

    # 2. Rapid tactical encryption telemetry ticks (0.01s - 0.38s)
    for k in range(5):
        tk = 0.01 + k * 0.072
        sw = modal_impulse(0.04, [1800 + k * 240, 3900 + k * 360], [0.012, 0.008], [0.8, 0.9], rng=rng)
        mix_at(dry, haas_stereo(sw, delay_ms=2.2, side_mix=0.4), tk, gain=0.38 + k * 0.06)

    # 3. Signature rising tactical radar lock chord (D5 -> F5 -> A5 -> D6 lock-in!)
    pings = [
        (0.02, 587.33, 0.55),   # D5
        (0.15, 698.46, 0.62),   # F5
        (0.28, 880.00, 0.72),   # A5
        (0.42, 1174.66, 1.00),  # D6 LOCK!
        (0.42, 587.33, 0.70),   # D5 octave anchor
        (0.42, 880.00, 0.65),   # A5 fifth power
    ]
    for pt, freq, g in pings:
        p_l = modal_impulse(1.15, [freq, freq * 2.0, freq * 3.01], [0.55, 0.24, 0.11], [1.0, 0.42, 0.16], rng=rng)
        p_r = modal_impulse(1.15, [freq * 1.0025, freq * 2.004, freq * 3.016], [0.55, 0.24, 0.11], [1.0, 0.42, 0.16], rng=rng)
        st = np.vstack([p_l, p_r])
        mix_at(dry, st, pt, gain=0.42 * g)
        mix_at(rev, st, pt, gain=0.65 * g)

    # Subtle radio relay hiss burst on lock
    hiss = butter_bp(white_noise(int(0.22 * SR), rng), 1800.0, 7500.0, order=2) * env_ad(int(0.22 * SR), 0.005, 0.14)
    mix_at(dry, haas_stereo(hiss, delay_ms=4.5, side_mix=0.5), 0.42, gain=0.22)

    hall = make_cinematic_hall_ir(rt60=1.5, rng=rng)
    wet = convolve_reverb(rev, hall, wet=1.0, dry=0.0, extend=False)
    full = dry + wet * 0.48
    return stereo_widen(full, width=1.25, mono_below_hz=110.0)


def _load_wav_mono(path: str) -> np.ndarray:
    with wave.open(path, "rb") as wf:
        n_ch = wf.getnchannels()
        sw = wf.getsampwidth()
        sr = wf.getframerate()
        raw = wf.readframes(wf.getnframes())
    if sw != 2 or sr != SR:
        raise ValueError(f"Expected 48kHz 16-bit WAV from say, got sr={sr}, sw={sw}")
    data = np.frombuffer(raw, dtype=np.int16).astype(np.float64) / 32767.0
    if n_ch > 1:
        data = data.reshape(-1, n_ch).mean(axis=1)
    return data


def _synth_radio_callout(text: str, tmp_dir: str, seed: int, rate: int = 172) -> np.ndarray | None:
    os.makedirs(tmp_dir, exist_ok=True)
    tmp_wav = os.path.join(tmp_dir, f"raw_vo_{seed}.wav")
    cmd = ["say", "-v", "Daniel", "-r", str(rate), "-o", tmp_wav, "--data-format=LEI16@48000", text]
    res = subprocess.run(cmd, capture_output=True, text=True)
    if res.returncode != 0 or not os.path.exists(tmp_wav):
        return None
    raw_mono = _load_wav_mono(tmp_wav)
    rng = np.random.default_rng(seed)
    return process_tactical_radio(raw_mono, rng=rng, sr=SR)


def build_ui_and_voice(exports_dir: str, tmp_dir: str) -> tuple[list[dict], dict]:
    entries = []

    ui_specs = [
        ("ui_hover.wav", synth_ui_hover(), -3.2, "Tactical UI hover micro-switch tick"),
        ("ui_click.wav", synth_ui_click(), -2.4, "Tactical UI ruggedized switch click"),
        ("ui_confirm.wav", synth_ui_confirm(), -2.0, "Tactical UI lock-in confirmation chime"),
        ("ui_cancel.wav", synth_ui_cancel(), -2.6, "Tactical UI descending cancel cue"),
        ("ui_match_found.wav", synth_ui_match_found(), -1.5, "Dramatic tactical matchmaking lock-on alert"),
    ]

    for filename, sig, peak_db, desc in ui_specs:
        out_path = os.path.join(exports_dir, filename)
        meta = finalize_and_save(out_path, sig, target_peak_db=peak_db, stereo=True, is_loop=False)
        entries.append({
            "file": f"exports/{filename}",
            "id": os.path.splitext(filename)[0],
            "category": "UI",
            "mixerGroup": "UI",
            "spatialBlend": 0.0,
            "loadType": "DecompressOnLoad",
            "loop": False,
            "description": desc,
            **meta,
        })

    vo_specs = [
        ("vo_match_start.wav", "Match starting. Weapons free.", 5101, 168),
        ("vo_elimination.wav", "Target eliminated.", 5102, 174),
        ("vo_headshot.wav", "Headshot.", 5103, 176),
        ("vo_double_kill.wav", "Double kill.", 5104, 176),
        ("vo_alpha_leads.wav", "Alpha team takes the lead.", 5105, 170),
        ("vo_bravo_leads.wav", "Bravo team takes the lead.", 5106, 170),
        ("vo_victory.wav", "Mission accomplished. Victory.", 5107, 165),
        ("vo_defeat.wav", "Mission failed. Fall back.", 5108, 165),
        ("vo_reloading.wav", "Reloading!", 5109, 180),
        ("vo_enemy_spotted.wav", "Contact! Enemy spotted.", 5110, 176),
    ]

    vo_generated = 0
    for filename, line, seed, rate in vo_specs:
        sig = _synth_radio_callout(line, tmp_dir, seed=seed, rate=rate)
        if sig is None:
            continue
        out_path = os.path.join(exports_dir, filename)
        meta = finalize_and_save(
            out_path,
            sig,
            target_peak_db=-1.6,
            stereo=True,
            is_loop=False,
            do_trim=True,
            fade_in_ms=2.0,
            fade_out_ms=20.0,
        )
        entries.append({
            "file": f"exports/{filename}",
            "id": os.path.splitext(filename)[0],
            "category": "Voice",
            "mixerGroup": "Voice",
            "spatialBlend": 0.0,
            "loadType": "DecompressOnLoad",
            "loop": False,
            "transcript": line,
            "description": f"Military VHF radio announcer callout: '{line}'",
            **meta,
        })
        vo_generated += 1

    vo_notes = {
        "elevenLabsMcpAvailable": False,
        "voiceSynthesisMethod": "macOS Daniel (en_GB) 48kHz PCM + Custom Python Military VHF Radio DSP Chain (PTT key-up chirp, 340-3100Hz bandpass, 1.48kHz presence horn, opto AGC compression, asymmetric transistor saturation, RF noise floor, VHF squelch tail)",
        "voiceClipsGenerated": vo_generated,
    }
    return entries, vo_notes
