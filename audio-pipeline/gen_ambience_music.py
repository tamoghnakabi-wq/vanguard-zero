"""
VANGUARD ZERO — Ambience & Cinematic Music Synthesis Module (4 Stereo 2D WAVs)
All loops use circular synthesis and circular FFT convolution for zero-click sample-exact loop seams.
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
    butter_lp,
    butter_hp,
    butter_bp,
    biquad_peaking,
    biquad_lowshelf,
    time_varying_lp,
    env_ad,
    env_trapezoid,
    pitched_sine_sweep,
    modal_impulse,
    make_cinematic_hall_ir,
    convolve_reverb,
    convolve_reverb_circular,
    soft_saturate,
    stereo_widen,
    circular_apply,
    finalize_and_save,
)


def midi_to_hz(midi_note: float) -> float:
    return 440.0 * (2.0 ** ((midi_note - 69.0) / 12.0))


def _circular_bp_noise(n: int, rng: np.random.Generator, f_lo: float, f_hi: float, exponent: float = 0.5) -> np.ndarray:
    """Generate 100% periodic bandpass colored noise via rFFT (zero filter startup/edge seam)."""
    w = rng.standard_normal(n)
    spec = np.fft.rfft(w)
    freqs = np.fft.rfftfreq(n, d=1.0 / SR)
    scale = np.zeros_like(freqs)
    valid = freqs > 1.0
    scale[valid] = (freqs[valid] / 100.0) ** (-exponent)
    # Smooth Butterworth-like spectral bandpass mask
    hp_mask = 1.0 / np.sqrt(1.0 + (f_lo / np.maximum(freqs, 1e-3)) ** 6)
    lp_mask = 1.0 / np.sqrt(1.0 + (freqs / max(f_hi, 20.0)) ** 6)
    out = np.fft.irfft(spec * scale * hp_mask * lp_mask, n=n)
    std = np.std(out)
    return out / max(std, 1e-9)


def synth_ambience_blacksite_rain_loop() -> np.ndarray:
    """
    20.000s Stereo seamless loop for BLACKSITE-09:
    - Steady dusk rainfall canopy (wide stereo)
    - Hundreds of spatialized raindrops hitting concrete, puddles, and steel containers
    - Periodic wind gusts howling softly around concrete radar structures
    - Subtle deep industrial radar substation transformer hum (50Hz + harmonics)
    """
    rng = np.random.default_rng(4001)
    dur = 20.000
    n = int(round(dur * SR))
    t = np.arange(n, dtype=np.float64) / SR
    tau = 2.0 * math.pi / dur  # fundamental angular frequency for 100% periodic modulation

    left = np.zeros(n, dtype=np.float64)
    right = np.zeros(n, dtype=np.float64)

    # 1. Steady dusk rainfall bed (decorrelated L/R multi-band circular noise)
    rain_mid_l = _circular_bp_noise(n, rng, 650.0, 4200.0, exponent=0.45)
    rain_mid_r = _circular_bp_noise(n, rng, 650.0, 4200.0, exponent=0.45)
    rain_air_l = _circular_bp_noise(n, rng, 3500.0, 10500.0, exponent=0.25)
    rain_air_r = _circular_bp_noise(n, rng, 3500.0, 10500.0, exponent=0.25)
    rain_body_l = _circular_bp_noise(n, rng, 220.0, 820.0, exponent=0.65)
    rain_body_r = _circular_bp_noise(n, rng, 220.0, 820.0, exponent=0.65)

    # Subtle periodic rain sheet intensity breathing (integer cycles over 20s)
    sheet_mod_l = 0.88 + 0.12 * np.sin(2.0 * tau * t + 0.3) + 0.06 * np.sin(5.0 * tau * t + 1.1)
    sheet_mod_r = 0.88 + 0.12 * np.sin(2.0 * tau * t + 1.9) + 0.06 * np.sin(5.0 * tau * t + 2.8)

    left += (rain_body_l * 0.18 + rain_mid_l * 0.24 + rain_air_l * 0.11) * sheet_mod_l
    right += (rain_body_r * 0.18 + rain_mid_r * 0.24 + rain_air_r * 0.11) * sheet_mod_r

    # 2. Distant wind gusts sweeping across concrete bunkers & radar lattice
    wind_low_l = _circular_bp_noise(n, rng, 75.0, 290.0, exponent=0.9)
    wind_low_r = _circular_bp_noise(n, rng, 75.0, 290.0, exponent=0.9)
    wind_howl_l = _circular_bp_noise(n, rng, 240.0, 620.0, exponent=0.7)
    wind_howl_r = _circular_bp_noise(n, rng, 240.0, 620.0, exponent=0.7)

    gust_l = np.clip(0.52 + 0.34 * np.sin(1.0 * tau * t) + 0.14 * np.cos(3.0 * tau * t + 0.5), 0.05, 1.0)
    gust_r = np.clip(0.52 + 0.34 * np.sin(1.0 * tau * t - 0.6) + 0.14 * np.cos(3.0 * tau * t + 1.4), 0.05, 1.0)

    left += wind_low_l * gust_l * 0.26 + wind_howl_l * (gust_l ** 1.5) * 0.15
    right += wind_low_r * gust_r * 0.26 + wind_howl_r * (gust_r ** 1.5) * 0.15

    # 3. Hundreds of spatialized raindrops hitting wet concrete, puddles & metal containers
    # Concrete / puddle drops
    for _ in range(520):
        dt = float(rng.uniform(0.0, dur))
        pan = float(rng.uniform(0.1, 0.9))
        f0 = float(rng.uniform(950.0, 3200.0))
        # Water bubble upward chirp + tiny splatter click
        td = make_time(0.018)
        d_n = len(td)
        f_chirp = f0 * (1.0 + 0.45 * (td / 0.018))
        drop = np.sin(2.0 * math.pi * np.cumsum(f_chirp) / SR) * env_ad(d_n, 0.001, 0.012)
        amp = float(rng.exponential(0.032))
        mix_at_circular(left, drop, dt, gain=amp * math.sqrt(1.0 - pan))
        mix_at_circular(right, drop, dt, gain=amp * math.sqrt(pan))

    # Distant metallic shipping container / catwalk roof drips
    for _ in range(75):
        dt = float(rng.uniform(0.0, dur))
        pan = float(rng.uniform(0.05, 0.95))
        p = float(rng.uniform(0.82, 1.25))
        drip = modal_impulse(0.055, [1150 * p, 2420 * p, 4150 * p], [0.016, 0.011, 0.007], [0.7, 1.0, 0.55], rng=rng)
        amp = float(rng.uniform(0.012, 0.038))
        mix_at_circular(left, drip, dt, gain=amp * math.sqrt(1.0 - pan))
        mix_at_circular(right, drip, dt, gain=amp * math.sqrt(pan))

    # 4. Subtle deep industrial radar substation transformer hum (50 Hz + odd/even harmonics, integer cycles in 20s!)
    hum_mod = 0.85 + 0.15 * np.sin(2.0 * tau * t)
    hum = (
        0.55 * np.sin(2.0 * math.pi * 50.0 * t)
        + 0.32 * np.sin(2.0 * math.pi * 100.0 * t + 0.4)
        + 0.18 * np.sin(2.0 * math.pi * 150.0 * t + 1.1)
        + 0.07 * np.sin(2.0 * math.pi * 250.0 * t + 0.7)
    ) * hum_mod * 0.065
    left += hum
    right += hum

    stereo = np.vstack([left, right])
    return circular_apply(stereo, lambda x: stereo_widen(x, width=1.18, mono_below_hz=95.0), pad_sec=1.0)


def _synth_bowed_cello_voice(
    dur: float,
    freq: float,
    rng: np.random.Generator,
    brightness: float = 1.0,
    attack_s: float = 0.28,
    release_s: float = 0.45,
) -> np.ndarray:
    """Rich physical-inspired bowed cello/contrabass ensemble note with stereo unison detune."""
    n = int(round(dur * SR))
    t = make_time(dur)
    left = np.zeros(n, dtype=np.float64)
    right = np.zeros(n, dtype=np.float64)

    # 6 unison detuned bowed string voices with organic vibrato
    detunes_cents = [-11.0, -5.5, -1.8, 1.8, 5.5, 11.0]
    pans = [0.15, 0.30, 0.45, 0.55, 0.70, 0.85]
    for cents, pan in zip(detunes_cents, pans):
        ratio = 2.0 ** (cents / 1200.0)
        f_v = freq * ratio
        vib_rate = rng.uniform(4.4, 5.3)
        vib_depth = 0.0032 * np.clip((t - 0.18) / 0.4, 0.0, 1.0)
        f_inst = f_v * (1.0 + vib_depth * np.sin(2.0 * math.pi * vib_rate * t + rng.uniform(0, 6.28)))
        ph = rng.uniform(0, 6.28) + 2.0 * math.pi * np.cumsum(f_inst) / SR
        # Band-limited bowed waveform (fundamental + rich warm low harmonics)
        wave_v = np.zeros(n, dtype=np.float64)
        max_h = min(18, int((SR * 0.42) / max(f_v, 20.0)))
        for h in range(1, max_h + 1):
            # Bowed string 1/h roll-off with wooden body dip around h=4 and peak around h=2,3
            amp_h = (1.0 / (h ** 1.12)) * (1.25 if h in (2, 3) else 1.0)
            wave_v += amp_h * np.sin(h * ph + (h * 0.37))
        left += wave_v * math.sqrt(1.0 - pan)
        right += wave_v * math.sqrt(pan)

    # Subtle bow rosin friction texture
    rosin_l = butter_bp(pink_noise(n, rng), 320.0, 2400.0, order=2) * 0.08
    rosin_r = butter_bp(pink_noise(n, rng), 320.0, 2400.0, order=2) * 0.08
    left += rosin_l
    right += rosin_r

    # Cello wooden body formant EQ (chest resonance at 195Hz, warm bridge peak at 520Hz, dark rolloff)
    hold_s = max(0.02, dur - attack_s - release_s)
    env = env_trapezoid(n, attack_s, hold_s, release_s)
    fc_curve = (380.0 + 1650.0 * brightness * env)
    for ch_arr in (left, right):
        ch_arr[:] = time_varying_lp(ch_arr, fc_curve, q=0.85, block_size=256)
        ch_arr[:] = biquad_peaking(ch_arr, 195.0, 4.5, q=1.3)
        ch_arr[:] = biquad_peaking(ch_arr, 520.0, 3.2, q=1.4)
        ch_arr[:] *= env

    stereo = np.vstack([left, right])
    pk = np.max(np.abs(stereo))
    return stereo / max(pk, 1e-6)


def _synth_tactical_pulse_bass(dur: float, freq: float, rng: np.random.Generator, accent: float = 1.0) -> np.ndarray:
    """Muted analog tactical hybrid sub-bass pulse (Sicario / Modern Warfare style)."""
    n = int(round(dur * SR))
    t = make_time(dur)
    ph1 = 2.0 * math.pi * freq * t
    ph2 = 2.0 * math.pi * (freq * 1.004) * t + 0.5
    # Sub sine + saturated saw/square growl
    sub = np.sin(ph1)
    growl = np.tanh(2.4 * (np.sin(ph1) + 0.55 * np.sin(2.0 * ph2) + 0.35 * np.sin(3.0 * ph1)))
    env = env_ad(n, 0.006, dur * 0.82, curve=0.95)
    fc = 95.0 + (480.0 * accent) * np.exp(-t / (dur * 0.32))
    shaped = time_varying_lp(growl, fc, q=1.15, block_size=64)
    sig = (sub * 0.85 + shaped * 0.65) * env
    return sig / max(np.max(np.abs(sig)), 1e-6)


def _synth_cinematic_taiko(rng: np.random.Generator, heavy: bool = True) -> np.ndarray:
    """Deep cinematic taiko / tactical war drum hit (Stereo)."""
    dur = 0.85 if heavy else 0.45
    n = int(round(dur * SR))
    f0, f1 = (98.0, 34.0) if heavy else (132.0, 48.0)
    sub = pitched_sine_sweep(n, f0, f1, 0.055 if heavy else 0.032) * env_ad(n, 0.002, dur * 0.78)
    skin_l = butter_bp(pink_noise(n, rng), 120.0, 1450.0, order=2) * env_ad(n, 0.001, 0.14)
    skin_r = butter_bp(pink_noise(n, rng), 120.0, 1450.0, order=2) * env_ad(n, 0.001, 0.14)
    attack = butter_bp(white_noise(n, rng), 1200.0, 4800.0, order=2) * env_ad(n, 0.0005, 0.022)
    l = soft_saturate(sub * 1.1 + skin_l * 0.55 + attack * 0.28, drive=1.9)
    r = soft_saturate(sub * 1.1 + skin_r * 0.55 + attack * 0.28, drive=1.9)
    return np.vstack([l, r])


def _synth_metallic_tick(rng: np.random.Generator, bright: bool = False) -> np.ndarray:
    """Muted industrial tactical percussion tick."""
    dur = 0.12
    p = 1.25 if bright else 1.0
    m_l = modal_impulse(dur, [1280 * p, 2740 * p, 5120 * p, 8400 * p], [0.028, 0.019, 0.012, 0.007], [0.7, 1.0, 0.8, 0.5], rng=rng)
    m_r = modal_impulse(dur, [1292 * p, 2760 * p, 5150 * p, 8450 * p], [0.028, 0.019, 0.012, 0.007], [0.7, 1.0, 0.8, 0.5], rng=rng)
    return np.vstack([m_l, m_r])


def synth_music_main_menu_loop() -> np.ndarray:
    """
    40.000s (16 bars of 4/4 at 96.0 BPM) seamless dark tactical hybrid orchestral/electronic theme.
    Key: D Minor / D Phrygian (D - Bb - G - A(b9)).
    Every note, drum, and circular reverb tail wraps mathematically across the 40.000s boundary.
    """
    rng = np.random.default_rng(4100)
    bpm = 96.0
    beat_s = 60.0 / bpm          # 0.625s exact
    step_s = beat_s / 4.0        # 0.15625s (16th note)
    bar_s = beat_s * 4.0         # 2.500s exact
    num_bars = 16
    total_dur = num_bars * bar_s  # 40.000s exact (1,920,000 samples)
    n = int(round(total_dur * SR))

    dry = np.zeros((2, n), dtype=np.float64)
    rev_send = np.zeros((2, n), dtype=np.float64)

    # 4-bar chord root progression repeated 4 times across 16 bars:
    # Bar 0: D2 (38), Bar 1: Bb1 (34), Bar 2: G1 (31), Bar 3: A1 (33)
    prog_roots = [38, 34, 31, 33] * 4
    # Tension harmony fifths / minor thirds for cello ensemble
    prog_harm = [
        (38, 45, 50),  # Dm (D2, A2, D3)
        (34, 41, 46),  # Bb (Bb1, F2, Bb2)
        (31, 38, 43),  # Gm (G1, D2, G2)
        (33, 40, 46),  # A7b9 tension (A1, E2, Bb2 Phrygian bite!)
    ] * 4

    for bar in range(num_bars):
        bar_t0 = bar * bar_s
        root_midi = prog_roots[bar]
        chord_midis = prog_harm[bar]
        # Arrangement intensity across the 16-bar arc:
        # Bars 0-3: brooding stealth intro; Bars 4-7: tactical pulse build;
        # Bars 8-11: full dark orchestral hybrid climax; Bars 12-15: tension bridge back to Bar 0
        section = bar // 4  # 0, 1, 2, 3
        intensity = [0.65, 0.85, 1.0, 0.78][section]

        # 1. Deep bowed contrabass + cello foundation swell on every bar
        bass_cello = _synth_bowed_cello_voice(
            dur=bar_s + 0.45,
            freq=midi_to_hz(chord_midis[0]),
            rng=rng,
            brightness=0.75 * intensity,
            attack_s=0.35,
            release_s=0.55,
        )
        mix_at_circular(dry, bass_cello, bar_t0, gain=0.34 * intensity)
        mix_at_circular(rev_send, bass_cello, bar_t0, gain=0.22 * intensity)

        # Upper bowed cello harmony voices (enter in section 1, 2, 3 and softly in bar 2-3)
        if section >= 1 or bar >= 2:
            for v_idx, m_note in enumerate(chord_midis[1:]):
                vc = _synth_bowed_cello_voice(
                    dur=bar_s + 0.4,
                    freq=midi_to_hz(m_note),
                    rng=rng,
                    brightness=0.9 * intensity,
                    attack_s=0.42,
                    release_s=0.55,
                )
                g_v = (0.20 if v_idx == 0 else 0.15) * intensity
                mix_at_circular(dry, vc, bar_t0, gain=g_v)
                mix_at_circular(rev_send, vc, bar_t0, gain=g_v * 0.85)

        # 2. Muted tactical 16th-note analog sub-bass pulse ostinato
        # Pattern: driving syncopated tactical pulse (steps 0, 2, 3, 6, 8, 10, 11, 14)
        pulse_accents = [1.0, 0.35, 0.75, 0.85, 0.3, 0.65, 0.9, 0.35, 0.95, 0.35, 0.75, 0.85, 0.35, 0.65, 0.85, 0.45]
        for step in range(16):
            acc = pulse_accents[step] * intensity
            if section == 0 and step not in (0, 3, 6, 8, 11, 14):
                continue
            p_note = root_midi if step < 14 else (root_midi + 7)
            p_sig = _synth_tactical_pulse_bass(step_s * 0.92, midi_to_hz(p_note), rng, accent=acc)
            st_t = bar_t0 + step * step_s
            mix_at_circular(dry, p_sig, st_t, gain=0.32 * acc)

        # 3. Cinematic Taiko / Sub Impacts & Tactical Metallic Clockwork Percussion
        # Heavy downbeat war drum on Beat 1 and syncopated Beat 3.5
        taiko_heavy = _synth_cinematic_taiko(rng, heavy=True)
        mix_at_circular(dry, taiko_heavy, bar_t0, gain=0.46 * intensity)
        mix_at_circular(rev_send, taiko_heavy, bar_t0, gain=0.22 * intensity)

        if section in (1, 2) or (bar % 2 == 1):
            taiko_mid = _synth_cinematic_taiko(rng, heavy=False)
            mix_at_circular(dry, taiko_mid, bar_t0 + 2.5 * beat_s, gain=0.34 * intensity)
            if section == 2:
                mix_at_circular(dry, taiko_mid, bar_t0 + 1.5 * beat_s, gain=0.26)
                mix_at_circular(dry, taiko_heavy, bar_t0 + 3.0 * beat_s, gain=0.35)

        # Metallic tactical clockwork pulse (8th/16th industrial texture)
        for step in range(16):
            if step % 2 == 0 or (section >= 1 and step in (3, 7, 11, 15)):
                tick = _synth_metallic_tick(rng, bright=(step % 4 == 0))
                pan_l = 0.65 if (step % 4 < 2) else 0.35
                tick_panned = np.vstack([tick[0] * pan_l, tick[1] * (1.0 - pan_l)])
                t_gain = (0.14 if step % 4 == 0 else 0.08) * intensity
                mix_at_circular(dry, tick_panned, bar_t0 + step * step_s, gain=t_gain)
                mix_at_circular(rev_send, tick_panned, bar_t0 + step * step_s, gain=t_gain * 0.5)

        # 4. Signature Blacksite Radar Sonar Ping (Beat 2 of every 2nd bar — haunting D5 / A5 metallic ping)
        if bar % 2 == 0:
            ping_freq = midi_to_hz(74 if (bar % 4 == 0) else 69)  # D5 or A4
            ping_l = modal_impulse(1.4, [ping_freq, ping_freq * 2.0, ping_freq * 3.01], [0.55, 0.25, 0.11], [1.0, 0.35, 0.12], rng=rng)
            ping_r = modal_impulse(1.4, [ping_freq * 1.002, ping_freq * 2.003, ping_freq * 3.015], [0.55, 0.25, 0.11], [1.0, 0.35, 0.12], rng=rng)
            ping_st = np.vstack([ping_l, ping_r])
            mix_at_circular(dry, ping_st, bar_t0 + beat_s, gain=0.14)
            mix_at_circular(rev_send, ping_st, bar_t0 + beat_s, gain=0.28)
            # Dotted-8th stereo echo
            mix_at_circular(dry, ping_st[::-1], bar_t0 + beat_s + 3 * step_s, gain=0.08)

    # 5. Haunting Dark Tactical Lead Motif (Bars 4..15: bowed solo cello + dark analog brass lead)
    # Phrygian tactical melody notes: (bar_offset_beats, duration_beats, midi_note, gain)
    motif_events = [
        # Phrase 1 (Bars 4-7)
        (16.0, 2.5, 62, 0.22),  # D4
        (18.5, 1.5, 65, 0.24),  # F4
        (20.0, 3.0, 69, 0.26),  # A4
        (23.0, 1.0, 70, 0.28),  # Bb4 (Phrygian tension!)
        (24.0, 2.5, 69, 0.26),  # A4
        (26.5, 1.5, 67, 0.24),  # G4
        (28.0, 4.0, 64, 0.25),  # E4 -> resolves into Bar 8 climax
        # Phrase 2 Climax (Bars 8-11: octave reinforced)
        (32.0, 2.0, 74, 0.30),  # D5
        (34.0, 1.0, 76, 0.28),  # E5
        (35.0, 1.0, 77, 0.30),  # F5
        (36.0, 2.5, 70, 0.32),  # Bb4
        (38.5, 1.5, 69, 0.28),  # A4
        (40.0, 2.5, 67, 0.28),  # G4
        (42.5, 1.5, 65, 0.26),  # F4
        (44.0, 3.0, 69, 0.30),  # A4
        (47.0, 1.0, 70, 0.28),  # Bb4
        # Phrase 3 Dark Bridge (Bars 12-15: descending back to D3/D4 for seamless loop into Bar 0)
        (48.0, 3.0, 69, 0.24),  # A4
        (51.0, 1.0, 65, 0.22),  # F4
        (52.0, 4.0, 62, 0.24),  # D4
        (56.0, 4.0, 58, 0.22),  # Bb3
        (60.0, 4.0, 57, 0.22),  # A3 (leads smoothly into Bar 0 D minor!)
    ]
    for beat_pos, beat_dur, m_note, g_lead in motif_events:
        ev_t0 = beat_pos * beat_s
        ev_dur = beat_dur * beat_s + 0.25
        lead_voice = _synth_bowed_cello_voice(
            dur=ev_dur,
            freq=midi_to_hz(m_note),
            rng=rng,
            brightness=1.25,
            attack_s=0.14,
            release_s=0.32,
        )
        mix_at_circular(dry, lead_voice, ev_t0, gain=g_lead)
        mix_at_circular(rev_send, lead_voice, ev_t0, gain=g_lead * 1.1)

    # 6. Apply Circular Lush Scoring Stage Reverb so loop boundary is 100% seamless!
    hall_ir = make_cinematic_hall_ir(rt60=2.7, rng=rng)
    wet_hall = convolve_reverb_circular(rev_send, hall_ir, wet=1.0, dry=0.0)
    full = dry + wet_hall * 0.55

    # Mastering polish (circularly applied so loop boundary stays sample-continuous!)
    def _master_chain(x: np.ndarray) -> np.ndarray:
        y = np.zeros_like(x)
        for ch in range(2):
            c = biquad_lowshelf(x[ch], 95.0, 2.5)
            c = biquad_peaking(c, 2400.0, 1.8, q=1.0)
            y[ch] = soft_saturate(c, drive=1.35)
        return stereo_widen(y, width=1.22, mono_below_hz=105.0)

    mastered = circular_apply(full, _master_chain, pad_sec=1.0)
    # Roll by +18ms so the loop seam sits on the smooth sustained reverb/cello tail
    # just before Bar 0's downbeat taiko transient (zero transient split across seam)
    return np.roll(mastered, int(round(0.018 * SR)), axis=1)


def synth_music_match_victory() -> np.ndarray:
    """7.2s Cinematic dark-tactical triumphant match victory stinger (Stereo 2D)."""
    rng = np.random.default_rng(4201)
    dur = 7.4
    n = int(round(dur * SR))
    dry = np.zeros((2, n), dtype=np.float64)
    rev = np.zeros((2, n), dtype=np.float64)

    # Heroic dark-tactical chord progression:
    # 0.0s: Dm9 power impact -> 1.2s: Bbmaj7 -> 2.4s: C6/9 -> 3.6s: D major / Picardy triumphant resolution!
    chords = [
        (0.02, 1.35, [38, 45, 50, 57, 65], 0.90),        # Dm
        (1.22, 1.35, [34, 46, 53, 58, 65], 0.92),        # Bb
        (2.42, 1.35, [36, 48, 55, 60, 67], 0.96),        # C
        (3.62, 3.10, [38, 45, 50, 62, 66, 69, 74], 1.05), # D Major triumphant final swell
    ]
    for t0, cdur, notes, gain in chords:
        taiko = _synth_cinematic_taiko(rng, heavy=True)
        mix_at(dry, taiko, t0, gain=0.62 * gain)
        mix_at(rev, taiko, t0, gain=0.35 * gain)
        for idx, mn in enumerate(notes):
            v = _synth_bowed_cello_voice(
                dur=cdur,
                freq=midi_to_hz(mn),
                rng=rng,
                brightness=1.15 + 0.1 * idx,
                attack_s=0.06 if idx == 0 else 0.14,
                release_s=0.45 if cdur < 2.0 else 1.35,
            )
            vg = (0.34 if idx < 2 else 0.22) * gain
            mix_at(dry, v, t0, gain=vg)
            mix_at(rev, v, t0, gain=vg * 0.95)

    # Crystalline tactical brass/chime accents on rising melody (A4 -> C5 -> E5 -> F#5 -> A5)
    mel = [(0.02, 69), (0.62, 72), (1.22, 74), (1.82, 76), (2.42, 79), (3.02, 81), (3.62, 86)]
    for mt, mn in mel:
        f = midi_to_hz(mn)
        ch_l = modal_impulse(1.1, [f, f * 2.0, f * 3.0], [0.45, 0.22, 0.10], [0.9, 0.45, 0.18], rng=rng)
        ch_r = modal_impulse(1.1, [f * 1.002, f * 2.004, f * 3.005], [0.45, 0.22, 0.10], [0.9, 0.45, 0.18], rng=rng)
        st = np.vstack([ch_l, ch_r])
        mix_at(dry, st, mt, gain=0.18)
        mix_at(rev, st, mt, gain=0.32)

    hall_ir = make_cinematic_hall_ir(rt60=2.4, rng=rng)
    wet = convolve_reverb(rev, hall_ir, wet=1.0, dry=0.0, extend=False)
    full = dry + wet * 0.58
    return stereo_widen(full, width=1.25, mono_below_hz=100.0)


def synth_music_match_defeat() -> np.ndarray:
    """7.2s Cinematic dark brooding match defeat stinger (Stereo 2D)."""
    rng = np.random.default_rng(4202)
    dur = 7.4
    n = int(round(dur * SR))
    dry = np.zeros((2, n), dtype=np.float64)
    rev = np.zeros((2, n), dtype=np.float64)

    # Ominous sub-bass downer sweep at 0.02s
    sub_drop = pitched_sine_sweep(int(2.2 * SR), 96.0, 27.0, 0.55) * env_ad(int(2.2 * SR), 0.01, 1.85)
    mix_at(dry, np.vstack([sub_drop, sub_drop]), 0.02, gain=0.58)

    # Dark descending Phrygian mourning progression: Dm -> Bbm/Db -> Eb(b2) -> D1 hollow fifth
    chords = [
        (0.02, 1.85, [38, 45, 53, 62, 65], 0.92),     # Dm (F4 -> E4 descent)
        (1.65, 1.85, [34, 41, 49, 58, 61], 0.88),     # Bbm (dark minor turn)
        (3.25, 3.60, [26, 38, 45, 50, 58], 0.95),     # Low D1 + Eb/Bb Phrygian tension fading to dark fifth
    ]
    for t0, cdur, notes, gain in chords:
        taiko = _synth_cinematic_taiko(rng, heavy=True)
        mix_at(dry, taiko, t0, gain=0.48 * gain)
        mix_at(rev, taiko, t0, gain=0.38 * gain)
        for idx, mn in enumerate(notes):
            v = _synth_bowed_cello_voice(
                dur=cdur,
                freq=midi_to_hz(mn),
                rng=rng,
                brightness=0.72,
                attack_s=0.18,
                release_s=0.65 if cdur < 2.5 else 1.65,
            )
            vg = (0.32 if idx < 2 else 0.20) * gain
            mix_at(dry, v, t0, gain=vg)
            mix_at(rev, v, t0, gain=vg * 1.1)

    # Lonely distant descending sonar pings (F4 -> Eb4 -> D4)
    for mt, mn in [(0.35, 65), (1.95, 63), (3.55, 62)]:
        f = midi_to_hz(mn)
        p_l = modal_impulse(1.6, [f, f * 2.0], [0.65, 0.28], [0.9, 0.3], rng=rng)
        p_r = modal_impulse(1.6, [f * 1.002, f * 2.003], [0.65, 0.28], [0.9, 0.3], rng=rng)
        st = np.vstack([p_l, p_r])
        mix_at(dry, st, mt, gain=0.14)
        mix_at(rev, st, mt, gain=0.36)

    hall_ir = make_cinematic_hall_ir(rt60=2.6, rng=rng)
    wet = convolve_reverb(rev, hall_ir, wet=1.0, dry=0.0, extend=False)
    full = dry + wet * 0.65
    return stereo_widen(full, width=1.22, mono_below_hz=100.0)


def build_ambience_music(exports_dir: str) -> list[dict]:
    specs = [
        (
            "ambience_blacksite_rain_loop.wav",
            synth_ambience_blacksite_rain_loop(),
            -2.5,
            "Ambience",
            "Ambience",
            True,
            "Seamless 20s BLACKSITE-09 dusk rainfall, wind gusts, concrete/container drops, and radar hum",
        ),
        (
            "music_main_menu_loop.wav",
            synth_music_main_menu_loop(),
            -1.4,
            "Music",
            "Music",
            True,
            "Seamless 40s (16-bar 96 BPM) dark tactical hybrid orchestral/electronic main menu theme",
        ),
        (
            "music_match_victory.wav",
            synth_music_match_victory(),
            -1.4,
            "Music",
            "Music",
            False,
            "Cinematic dark-tactical triumphant match victory stinger",
        ),
        (
            "music_match_defeat.wav",
            synth_music_match_defeat(),
            -1.6,
            "Music",
            "Music",
            False,
            "Cinematic brooding Phrygian match defeat stinger",
        ),
    ]

    entries = []
    for filename, sig, peak_db, cat, mixer, is_loop, desc in specs:
        out_path = os.path.join(exports_dir, filename)
        meta = finalize_and_save(
            out_path,
            sig,
            target_peak_db=peak_db,
            stereo=True,
            is_loop=is_loop,
            do_trim=(not is_loop),
            fade_in_ms=2.0,
            fade_out_ms=45.0,
        )
        entries.append({
            "file": f"exports/{filename}",
            "id": os.path.splitext(filename)[0],
            "category": cat,
            "mixerGroup": mixer,
            "spatialBlend": 0.0,
            "loadType": "Streaming",
            "loop": is_loop,
            "description": desc,
            **meta,
        })
    return entries
