"""
VANGUARD ZERO — AAA Tactical FPS Audio DSP Core Library
48 kHz / 64-bit internal float processing -> 16-bit TPDF-dithered PCM WAV output.
"""

from __future__ import annotations

import math
import os
import wave
from typing import Iterable, Sequence

import numpy as np
from scipy import signal

SR: int = 48000


def db_to_lin(db: float) -> float:
    return float(10.0 ** (db / 20.0))


def lin_to_db(lin: float, floor_db: float = -120.0) -> float:
    return float(20.0 * math.log10(max(abs(lin), 10.0 ** (floor_db / 20.0))))


def make_time(duration: float, sr: int = SR) -> np.ndarray:
    n = max(1, int(round(duration * sr)))
    return np.arange(n, dtype=np.float64) / sr


def ensure_len(sig: np.ndarray, n: int) -> np.ndarray:
    if sig.ndim == 1:
        if len(sig) >= n:
            return sig[:n].copy()
        out = np.zeros(n, dtype=np.float64)
        out[: len(sig)] = sig
        return out
    else:
        if sig.shape[1] >= n:
            return sig[:, :n].copy()
        out = np.zeros((sig.shape[0], n), dtype=np.float64)
        out[:, : sig.shape[1]] = sig
        return out


def mix_at(canvas: np.ndarray, layer: np.ndarray, offset_sec: float, gain: float = 1.0, sr: int = SR) -> np.ndarray:
    """Add a 1D or 2D (ch, N) layer into canvas at offset_sec."""
    idx = int(round(offset_sec * sr))
    if canvas.ndim == 1:
        if idx >= len(canvas):
            return canvas
        start_dst = max(0, idx)
        start_src = max(0, -idx)
        avail = min(len(canvas) - start_dst, len(layer) - start_src)
        if avail > 0:
            canvas[start_dst : start_dst + avail] += layer[start_src : start_src + avail] * gain
    else:
        n_total = canvas.shape[1]
        if idx >= n_total:
            return canvas
        if layer.ndim == 1:
            layer_2d = np.vstack([layer, layer])
        else:
            layer_2d = layer
        start_dst = max(0, idx)
        start_src = max(0, -idx)
        avail = min(n_total - start_dst, layer_2d.shape[1] - start_src)
        if avail > 0:
            canvas[:, start_dst : start_dst + avail] += layer_2d[:, start_src : start_src + avail] * gain
    return canvas


def mix_at_circular(canvas: np.ndarray, layer: np.ndarray, offset_sec: float, gain: float = 1.0, sr: int = SR) -> np.ndarray:
    """Add layer into canvas with circular wraparound for mathematically seamless loops."""
    idx = int(round(offset_sec * sr))
    if canvas.ndim == 1:
        n = len(canvas)
        indices = (idx + np.arange(len(layer))) % n
        np.add.at(canvas, indices, layer * gain)
    else:
        n = canvas.shape[1]
        if layer.ndim == 1:
            layer_2d = np.vstack([layer, layer])
        else:
            layer_2d = layer
        indices = (idx + np.arange(layer_2d.shape[1])) % n
        for ch in range(canvas.shape[0]):
            np.add.at(canvas[ch], indices, layer_2d[ch] * gain)
    return canvas


# ---------------------------------------------------------------------------
# Noise & Grain Generators
# ---------------------------------------------------------------------------

def white_noise(n: int, rng: np.random.Generator) -> np.ndarray:
    return rng.standard_normal(n).astype(np.float64)


def colored_noise(n: int, exponent: float, rng: np.random.Generator) -> np.ndarray:
    """Spectral-shaped 1/f^exponent noise via rFFT (exponent=0.5 pink, 1.0 brown)."""
    w = rng.standard_normal(n)
    spec = np.fft.rfft(w)
    freqs = np.fft.rfftfreq(n, d=1.0 / SR)
    scale = np.ones_like(freqs)
    scale[1:] = (freqs[1:] / 100.0) ** (-exponent)
    scale[0] = 0.0
    out = np.fft.irfft(spec * scale, n=n)
    std = np.std(out)
    return out / std if std > 1e-12 else out


def pink_noise(n: int, rng: np.random.Generator) -> np.ndarray:
    return colored_noise(n, 0.5, rng)


def brown_noise(n: int, rng: np.random.Generator) -> np.ndarray:
    return colored_noise(n, 1.0, rng)


def crackle_noise(n: int, density_hz: float, rng: np.random.Generator, sr: int = SR) -> np.ndarray:
    """Sparse bipolar impulses with heavy-tailed amplitudes (grit, raindrops, embers, debris)."""
    out = np.zeros(n, dtype=np.float64)
    expected = max(1, int(round(density_hz * (n / sr))))
    pos = rng.integers(0, n, size=expected)
    amps = rng.exponential(scale=1.0, size=expected) * rng.choice([-1.0, 1.0], size=expected)
    np.add.at(out, pos, amps)
    peak = np.max(np.abs(out))
    return out / peak if peak > 1e-12 else out


def velvet_noise(n: int, density_hz: float, rng: np.random.Generator, sr: int = SR) -> np.ndarray:
    """Smooth decorrelated velvet noise for lush non-metallic reverb tails."""
    grid = max(1, int(round(sr / density_hz)))
    num_pulses = n // grid
    out = np.zeros(n, dtype=np.float64)
    offsets = rng.integers(0, grid, size=num_pulses)
    positions = np.arange(num_pulses) * grid + offsets
    positions = positions[positions < n]
    signs = rng.choice([-1.0, 1.0], size=len(positions))
    out[positions] = signs
    return out


# ---------------------------------------------------------------------------
# Filters & Parametric EQ
# ---------------------------------------------------------------------------

def _clamp_freq(fc: float, sr: int = SR) -> float:
    return float(np.clip(fc, 12.0, sr * 0.47))


def butter_lp(sig: np.ndarray, fc: float, order: int = 4, sr: int = SR) -> np.ndarray:
    sos = signal.butter(order, _clamp_freq(fc, sr), btype="low", fs=sr, output="sos")
    return signal.sosfilt(sos, sig)


def butter_hp(sig: np.ndarray, fc: float, order: int = 4, sr: int = SR) -> np.ndarray:
    sos = signal.butter(order, _clamp_freq(fc, sr), btype="high", fs=sr, output="sos")
    return signal.sosfilt(sos, sig)


def butter_bp(sig: np.ndarray, f_lo: float, f_hi: float, order: int = 3, sr: int = SR) -> np.ndarray:
    lo = _clamp_freq(f_lo, sr)
    hi = _clamp_freq(max(f_hi, lo + 25.0), sr)
    sos = signal.butter(order, [lo, hi], btype="band", fs=sr, output="sos")
    return signal.sosfilt(sos, sig)


def butter_bs(sig: np.ndarray, f_lo: float, f_hi: float, order: int = 3, sr: int = SR) -> np.ndarray:
    lo = _clamp_freq(f_lo, sr)
    hi = _clamp_freq(max(f_hi, lo + 25.0), sr)
    sos = signal.butter(order, [lo, hi], btype="bandstop", fs=sr, output="sos")
    return signal.sosfilt(sos, sig)


def biquad_peaking(sig: np.ndarray, fc: float, gain_db: float, q: float = 1.2, sr: int = SR) -> np.ndarray:
    fc = _clamp_freq(fc, sr)
    A = 10.0 ** (gain_db / 40.0)
    w0 = 2.0 * math.pi * fc / sr
    alpha = math.sin(w0) / (2.0 * max(q, 0.1))
    cosw = math.cos(w0)
    b0 = 1.0 + alpha * A
    b1 = -2.0 * cosw
    b2 = 1.0 - alpha * A
    a0 = 1.0 + alpha / A
    a1 = -2.0 * cosw
    a2 = 1.0 - alpha / A
    b = np.array([b0, b1, b2]) / a0
    a = np.array([1.0, a1 / a0, a2 / a0])
    return signal.lfilter(b, a, sig)


def biquad_lowshelf(sig: np.ndarray, fc: float, gain_db: float, slope: float = 0.85, sr: int = SR) -> np.ndarray:
    fc = _clamp_freq(fc, sr)
    A = 10.0 ** (gain_db / 40.0)
    w0 = 2.0 * math.pi * fc / sr
    cosw = math.cos(w0)
    sinw = math.sin(w0)
    alpha = sinw / 2.0 * math.sqrt(max(0.01, (A + 1.0 / A) * (1.0 / slope - 1.0) + 2.0))
    sq = 2.0 * math.sqrt(A) * alpha
    b0 = A * ((A + 1.0) - (A - 1.0) * cosw + sq)
    b1 = 2.0 * A * ((A - 1.0) - (A + 1.0) * cosw)
    b2 = A * ((A + 1.0) - (A - 1.0) * cosw - sq)
    a0 = (A + 1.0) + (A - 1.0) * cosw + sq
    a1 = -2.0 * ((A - 1.0) + (A + 1.0) * cosw)
    a2 = (A + 1.0) + (A - 1.0) * cosw - sq
    b = np.array([b0, b1, b2]) / a0
    a = np.array([1.0, a1 / a0, a2 / a0])
    return signal.lfilter(b, a, sig)


def biquad_highshelf(sig: np.ndarray, fc: float, gain_db: float, slope: float = 0.85, sr: int = SR) -> np.ndarray:
    fc = _clamp_freq(fc, sr)
    A = 10.0 ** (gain_db / 40.0)
    w0 = 2.0 * math.pi * fc / sr
    cosw = math.cos(w0)
    sinw = math.sin(w0)
    alpha = sinw / 2.0 * math.sqrt(max(0.01, (A + 1.0 / A) * (1.0 / slope - 1.0) + 2.0))
    sq = 2.0 * math.sqrt(A) * alpha
    b0 = A * ((A + 1.0) + (A - 1.0) * cosw + sq)
    b1 = -2.0 * A * ((A - 1.0) + (A + 1.0) * cosw)
    b2 = A * ((A + 1.0) + (A - 1.0) * cosw - sq)
    a0 = (A + 1.0) - (A - 1.0) * cosw + sq
    a1 = 2.0 * ((A - 1.0) - (A + 1.0) * cosw)
    a2 = (A + 1.0) - (A - 1.0) * cosw - sq
    b = np.array([b0, b1, b2]) / a0
    a = np.array([1.0, a1 / a0, a2 / a0])
    return signal.lfilter(b, a, sig)


def time_varying_lp(sig: np.ndarray, fc_curve: np.ndarray, q: float = 0.707, block_size: int = 128, sr: int = SR) -> np.ndarray:
    """Fast block-interpolated time-varying 2nd-order lowpass filter (zero zipper noise)."""
    n = len(sig)
    out = np.zeros(n, dtype=np.float64)
    zi = np.zeros(2, dtype=np.float64)
    for start in range(0, n, block_size):
        end = min(n, start + block_size)
        fc = _clamp_freq(float(fc_curve[(start + end) // 2]), sr)
        w0 = 2.0 * math.pi * fc / sr
        cosw = math.cos(w0)
        alpha = math.sin(w0) / (2.0 * max(q, 0.25))
        b0 = (1.0 - cosw) * 0.5
        b1 = 1.0 - cosw
        b2 = b0
        a0 = 1.0 + alpha
        a1 = -2.0 * cosw
        a2 = 1.0 - alpha
        b = np.array([b0, b1, b2]) / a0
        a = np.array([1.0, a1 / a0, a2 / a0])
        block_out, zi = signal.lfilter(b, a, sig[start:end], zi=zi)
        out[start:end] = block_out
    return out


def time_varying_bp(sig: np.ndarray, fc_curve: np.ndarray, q: float = 2.5, block_size: int = 128, sr: int = SR) -> np.ndarray:
    """Fast block-interpolated time-varying 2nd-order constant-skirt bandpass filter."""
    n = len(sig)
    out = np.zeros(n, dtype=np.float64)
    zi = np.zeros(2, dtype=np.float64)
    for start in range(0, n, block_size):
        end = min(n, start + block_size)
        fc = _clamp_freq(float(fc_curve[(start + end) // 2]), sr)
        w0 = 2.0 * math.pi * fc / sr
        cosw = math.cos(w0)
        alpha = math.sin(w0) / (2.0 * max(q, 0.25))
        b0 = alpha
        b1 = 0.0
        b2 = -alpha
        a0 = 1.0 + alpha
        a1 = -2.0 * cosw
        a2 = 1.0 - alpha
        b = np.array([b0, b1, b2]) / a0
        a = np.array([1.0, a1 / a0, a2 / a0])
        block_out, zi = signal.lfilter(b, a, sig[start:end], zi=zi)
        out[start:end] = block_out
    return out


# ---------------------------------------------------------------------------
# Envelopes & Pitch Sweeps
# ---------------------------------------------------------------------------

def env_ad(n: int, attack_sec: float, decay_sec: float, curve: float = 1.0, sr: int = SR) -> np.ndarray:
    """Exponential-attack / power-exponential-decay envelope."""
    t = np.arange(n, dtype=np.float64) / sr
    atk = max(attack_sec, 1.0 / sr)
    dec = max(decay_sec, 1.0 / sr)
    env = np.where(
        t < atk,
        (np.maximum(0.0, t) / atk) ** 0.7,
        np.exp(-((np.maximum(0.0, t - atk) / dec) ** curve) * 4.6),
    )
    return np.clip(env, 0.0, 1.0)


def env_trapezoid(n: int, attack_sec: float, hold_sec: float, release_sec: float, sr: int = SR) -> np.ndarray:
    t = np.arange(n, dtype=np.float64) / sr
    atk = max(attack_sec, 1.0 / sr)
    rel = max(release_sec, 1.0 / sr)
    env = np.ones(n, dtype=np.float64)
    mask_a = t < atk
    env[mask_a] = 0.5 * (1.0 - np.cos(math.pi * t[mask_a] / atk))
    t_rel = t - (atk + hold_sec)
    mask_r = t_rel > 0
    env[mask_r] = np.clip(0.5 * (1.0 + np.cos(math.pi * np.minimum(t_rel[mask_r] / rel, 1.0))), 0.0, 1.0)
    return env


def pitched_sine_sweep(n: int, f_start: float, f_end: float, tau_sec: float, phase0: float = 0.0, sr: int = SR) -> np.ndarray:
    """Exponentially decaying frequency sine sweep (for sub-bass punch, shell thump, impact weight)."""
    t = np.arange(n, dtype=np.float64) / sr
    freq = f_end + (f_start - f_end) * np.exp(-t / max(tau_sec, 1e-4))
    phase = phase0 + 2.0 * math.pi * np.cumsum(freq) / sr
    return np.sin(phase)


# ---------------------------------------------------------------------------
# Physical Resonators (Modal Metal, Polymer, Ceramic, Formants)
# ---------------------------------------------------------------------------

def modal_resonator(
    excitation: np.ndarray,
    freqs: Sequence[float],
    decays: Sequence[float],
    amps: Sequence[float],
    sr: int = SR,
) -> np.ndarray:
    """Parallel bank of damped resonant 2nd-order modes driven by excitation."""
    out = np.zeros_like(excitation, dtype=np.float64)
    for f, dec, a in zip(freqs, decays, amps):
        if f >= sr * 0.46 or f <= 18.0:
            continue
        r = math.exp(-3.0 / max(dec * sr, 1.0))  # decay ~ -26 dB in dec seconds
        w0 = 2.0 * math.pi * f / sr
        b = np.array([1.0 - r, 0.0, -(1.0 - r)]) * a
        denom = np.array([1.0, -2.0 * r * math.cos(w0), r * r])
        out += signal.lfilter(b, denom, excitation)
    return out


def modal_impulse(
    duration: float,
    freqs: Sequence[float],
    decays: Sequence[float],
    amps: Sequence[float],
    rng: np.random.Generator | None = None,
    sr: int = SR,
) -> np.ndarray:
    """Direct analytical damped sinusoid modal bank with randomized phases and click-free micro-onset."""
    t = make_time(duration, sr)
    out = np.zeros_like(t)
    for f, dec, a in zip(freqs, decays, amps):
        if f >= sr * 0.46 or f <= 18.0:
            continue
        ph = float(rng.uniform(0, 2 * math.pi)) if rng is not None else 0.0
        out += a * np.sin(2.0 * math.pi * f * t + ph) * np.exp(-t / max(dec, 1e-4))
    # Smooth 0.4ms onset ramp prevents sample-0 step clicks on circular loops
    ramp_n = min(len(out) // 4, max(4, int(round(0.0004 * sr))))
    out[:ramp_n] *= 0.5 * (1.0 - np.cos(np.linspace(0.0, math.pi, ramp_n)))
    return out


# ---------------------------------------------------------------------------
# Acoustic Spaces & Convolution Reverbs
# ---------------------------------------------------------------------------

def make_outdoor_compound_ir(
    rt60: float = 0.85,
    predelay_ms: float = 11.0,
    slap_ms: float = 64.0,
    slap_gain: float = 0.38,
    rng: np.random.Generator | None = None,
    sr: int = SR,
) -> np.ndarray:
    """
    BLACKSITE-09 brutalist outdoor concrete compound Impulse Response:
    - Near concrete bunker early reflections
    - Signature outdoor wall slapback (~64-85ms)
    - Air-absorbed diffuse courtyard/valley tail
    """
    if rng is None:
        rng = np.random.default_rng(9001)
    n = int(round((rt60 + 0.25) * sr))
    ir = np.zeros(n, dtype=np.float64)

    # Early specular concrete reflections (bunker walls, containers, radar tower)
    early_taps = [
        (predelay_ms, 0.65),
        (predelay_ms + 9.5, -0.48),
        (predelay_ms + 21.0, 0.42),
        (predelay_ms + 34.5, -0.35),
        (slap_ms, slap_gain),
        (slap_ms + 13.0, -slap_gain * 0.7),
        (slap_ms + 29.0, slap_gain * 0.45),
        (slap_ms * 1.95, slap_gain * 0.28),
    ]
    for ms, g in early_taps:
        idx = int(round(ms * 0.001 * sr))
        if 0 <= idx < n:
            ir[idx] += g

    # Diffuse tail using velvet noise shaped with air absorption
    tail = velvet_noise(n, 2400.0, rng, sr)
    t = np.arange(n, dtype=np.float64) / sr
    delay_s = predelay_ms * 0.001
    env = np.where(t >= delay_s, np.exp(-6.91 * (t - delay_s) / max(rt60, 0.08)), 0.0)
    # Smooth onset into diffuse tail
    onset = np.clip((t - delay_s) / 0.035, 0.0, 1.0)
    tail = tail * env * onset

    # Air absorption: progressive lowpass sweep from 7.5kHz down to 650Hz
    fc_curve = 650.0 + 6800.0 * np.exp(-t / max(rt60 * 0.35, 0.05))
    tail = time_varying_lp(tail, fc_curve, q=0.7, block_size=256, sr=sr)
    tail = butter_hp(tail, 85.0, order=2, sr=sr)

    ir = butter_lp(ir, 9500.0, order=2, sr=sr) + tail * 0.55
    norm = np.sqrt(np.sum(ir ** 2))
    return ir / max(norm, 1e-8)


def make_metallic_chamber_ir(rt60: float = 0.22, rng: np.random.Generator | None = None, sr: int = SR) -> np.ndarray:
    """Tight industrial bunker / metallic corridor IR for reloads & indoor footsteps."""
    if rng is None:
        rng = np.random.default_rng(9002)
    n = int(round((rt60 + 0.08) * sr))
    ir = velvet_noise(n, 3200.0, rng, sr)
    t = np.arange(n, dtype=np.float64) / sr
    env = np.exp(-6.91 * t / max(rt60, 0.04))
    ir = ir * env
    # Subtle concrete/steel room early bounce
    for ms, g in [(4.2, 0.5), (9.8, -0.35), (17.5, 0.24), (28.0, -0.18)]:
        idx = int(round(ms * 0.001 * sr))
        if idx < n:
            ir[idx] += g
    ir = butter_bp(ir, 160.0, 6200.0, order=2, sr=sr)
    norm = np.sqrt(np.sum(ir ** 2))
    return ir / max(norm, 1e-8)


def make_cinematic_hall_ir(rt60: float = 2.6, rng: np.random.Generator | None = None, sr: int = SR) -> np.ndarray:
    """Rich stereo scoring-stage / dark cathedral IR (2, N) for cinematic music & stingers."""
    if rng is None:
        rng = np.random.default_rng(9003)
    n = int(round((rt60 + 0.3) * sr))
    t = np.arange(n, dtype=np.float64) / sr
    out = np.zeros((2, n), dtype=np.float64)
    for ch in range(2):
        v = velvet_noise(n, 2800.0, rng, sr)
        predelay = 0.022 + ch * 0.004
        env = np.where(t >= predelay, np.exp(-6.91 * (t - predelay) / max(rt60, 0.2)), 0.0)
        onset = np.clip((t - predelay) / 0.06, 0.0, 1.0)
        tail = v * env * onset
        # Early reflections
        for k in range(8):
            ms = 18.0 + k * (11.5 + ch * 1.7) + rng.uniform(-2.0, 2.0)
            idx = int(round(ms * 0.001 * sr))
            if idx < n:
                tail[idx] += (0.45 / (1.0 + k * 0.35)) * rng.choice([-1.0, 1.0])
        fc = 900.0 + 7500.0 * np.exp(-t / (rt60 * 0.4))
        tail = time_varying_lp(tail, fc, q=0.7, block_size=512, sr=sr)
        tail = butter_hp(tail, 75.0, order=2, sr=sr)
        norm = np.sqrt(np.sum(tail ** 2))
        out[ch] = tail / max(norm, 1e-8)
    return out


def convolve_reverb(sig: np.ndarray, ir: np.ndarray, wet: float = 0.35, dry: float = 1.0, extend: bool = True) -> np.ndarray:
    """Mono or Stereo FFT convolution reverb."""
    if sig.ndim == 1 and ir.ndim == 1:
        conv = signal.fftconvolve(sig, ir, mode="full")
        if extend:
            out = np.zeros(len(conv), dtype=np.float64)
            out[: len(sig)] += sig * dry
            out += conv * wet
            return out
        else:
            return sig * dry + conv[: len(sig)] * wet
    elif sig.ndim == 2 and ir.ndim == 2:
        n_out = sig.shape[1] + ir.shape[1] - 1 if extend else sig.shape[1]
        out = np.zeros((2, n_out), dtype=np.float64)
        for ch in range(2):
            conv = signal.fftconvolve(sig[ch], ir[ch], mode="full")
            out[ch, : sig.shape[1]] += sig[ch] * dry
            out[ch] += conv[:n_out] * wet
        return out
    elif sig.ndim == 1 and ir.ndim == 2:
        return convolve_reverb(np.vstack([sig, sig]), ir, wet=wet, dry=dry, extend=extend)
    else:
        return convolve_reverb(sig, np.vstack([ir, ir]), wet=wet, dry=dry, extend=extend)


def convolve_reverb_circular(sig: np.ndarray, ir: np.ndarray, wet: float = 0.35, dry: float = 1.0) -> np.ndarray:
    """Wraparound circular FFT convolution so reverb tails loop with zero discontinuity."""
    if sig.ndim == 1:
        n = len(sig)
        ir_pad = ensure_len(ir, n)
        wet_sig = np.fft.irfft(np.fft.rfft(sig) * np.fft.rfft(ir_pad), n=n)
        return sig * dry + wet_sig * wet
    else:
        n = sig.shape[1]
        ir_2d = ir if ir.ndim == 2 else np.vstack([ir, ir])
        out = np.zeros_like(sig)
        for ch in range(2):
            ir_pad = ensure_len(ir_2d[ch], n)
            wet_sig = np.fft.irfft(np.fft.rfft(sig[ch]) * np.fft.rfft(ir_pad), n=n)
            out[ch] = sig[ch] * dry + wet_sig * wet
        return out


# ---------------------------------------------------------------------------
# Dynamics, Saturation, Stereo & Looping
# ---------------------------------------------------------------------------

def soft_saturate(sig: np.ndarray, drive: float = 2.0, asym: float = 0.0) -> np.ndarray:
    """Analog-style hyperbolic saturation with optional asymmetric 2nd-harmonic bias."""
    if asym != 0.0:
        x = sig * drive + asym
        out = np.tanh(x) - math.tanh(asym)
    else:
        out = np.tanh(sig * drive)
    norm = math.tanh(drive)
    return out / max(norm, 1e-6)


def transient_shaper(sig: np.ndarray, attack_boost: float = 1.4, sustain_scale: float = 1.0, sr: int = SR) -> np.ndarray:
    """Dual-envelope transient enhancer (adds snap to gunshots, impacts, footsteps)."""
    rect = np.abs(sig)
    # Fast envelope (1.5 ms) vs slow envelope (22 ms)
    a_fast = math.exp(-1.0 / (0.0015 * sr))
    a_slow = math.exp(-1.0 / (0.022 * sr))
    env_fast = signal.lfilter([1.0 - a_fast], [1.0, -a_fast], rect)
    env_slow = signal.lfilter([1.0 - a_slow], [1.0, -a_slow], rect)
    diff = np.maximum(0.0, env_fast - env_slow)
    norm_diff = diff / (env_slow + 0.05)
    gain = sustain_scale + (attack_boost - 1.0) * np.clip(norm_diff * 1.8, 0.0, 2.0)
    return sig * gain


def compressor(
    sig: np.ndarray,
    thresh_db: float = -14.0,
    ratio: float = 4.0,
    attack_ms: float = 4.0,
    release_ms: float = 90.0,
    makeup_db: float = 0.0,
    sr: int = SR,
) -> np.ndarray:
    """Smooth feed-forward compressor (mono or linked stereo)."""
    mono_det = np.abs(sig) if sig.ndim == 1 else np.max(np.abs(sig), axis=0)
    a_atk = math.exp(-1.0 / max(attack_ms * 0.001 * sr, 1.0))
    a_rel = math.exp(-1.0 / max(release_ms * 0.001 * sr, 1.0))

    # Block-accelerated peak-hold envelope follower
    block = 32
    n = len(mono_det)
    env = np.zeros(n, dtype=np.float64)
    cur = 0.0
    for i in range(0, n, block):
        pk = float(np.max(mono_det[i : min(n, i + block)]))
        if pk > cur:
            cur = a_atk ** block * cur + (1.0 - a_atk ** block) * pk
        else:
            cur = a_rel ** block * cur + (1.0 - a_rel ** block) * pk
        env[i : min(n, i + block)] = cur

    env_db = 20.0 * np.log10(np.maximum(env, 1e-6))
    over_db = np.maximum(0.0, env_db - thresh_db)
    gr_db = -over_db * (1.0 - 1.0 / max(ratio, 1.0))
    gain = 10.0 ** ((gr_db + makeup_db) / 20.0)
    return sig * gain if sig.ndim == 1 else sig * gain[None, :]


def true_peak_limit(sig: np.ndarray, ceiling_db: float = -1.0, knee_db: float = 2.0) -> np.ndarray:
    """Smooth soft-knee peak limiter guaranteeing output stays strictly below ceiling_db."""
    ceiling = db_to_lin(ceiling_db)
    knee_start = db_to_lin(ceiling_db - knee_db)
    mag = np.abs(sig) if sig.ndim == 1 else np.max(np.abs(sig), axis=0)
    peak = float(np.max(mag))
    if peak <= ceiling:
        return sig
    # Smoothly compress peaks above knee_start into [knee_start, ceiling]
    over = np.maximum(0.0, mag - knee_start)
    room = max(ceiling - knee_start, 1e-6)
    shaped = knee_start + room * np.tanh(over / room)
    scale = np.where(mag > knee_start, shaped / np.maximum(mag, 1e-12), 1.0)
    out = sig * scale if sig.ndim == 1 else sig * scale[None, :]
    # Hard safety guarantee
    out_peak = float(np.max(np.abs(out)))
    if out_peak > ceiling:
        out = out * (ceiling / out_peak)
    return out


def stereo_widen(sig_stereo: np.ndarray, width: float = 1.25, mono_below_hz: float = 110.0, sr: int = SR) -> np.ndarray:
    """Mid/Side stereo widener keeping sub-bass mono-tight."""
    left, right = sig_stereo[0], sig_stereo[1]
    mid = 0.5 * (left + right)
    side = 0.5 * (left - right)
    # Remove sub-bass from Side channel so low end stays centered
    if mono_below_hz > 20.0:
        side = butter_hp(side, mono_below_hz, order=3, sr=sr)
    side = side * width
    return np.vstack([mid + side, mid - side])


def circular_apply(sig: np.ndarray, fn, pad_sec: float = 0.75, sr: int = SR) -> np.ndarray:
    """Apply any causal IIR filter/EQ/widener circularly so loop seam stays sample-continuous."""
    pad = min(sig.shape[-1] // 2, int(round(pad_sec * sr)))
    if sig.ndim == 1:
        padded = np.concatenate([sig[-pad:], sig])
        return fn(padded)[pad:]
    else:
        padded = np.concatenate([sig[:, -pad:], sig], axis=1)
        return fn(padded)[:, pad:]


def haas_stereo(mono: np.ndarray, delay_ms: float = 8.5, side_mix: float = 0.45, sr: int = SR) -> np.ndarray:
    """Convert mono cue to crisp wide stereo using subtle Haas delay + inverted high-shelf decorrelation."""
    d = max(1, int(round(delay_ms * 0.001 * sr)))
    delayed = np.zeros_like(mono)
    delayed[d:] = mono[:-d]
    delayed_hi = butter_hp(delayed, 260.0, order=2, sr=sr)
    left = mono + delayed_hi * (0.25 * side_mix)
    right = mono * (1.0 - 0.1 * side_mix) + delayed_hi * (0.55 * side_mix)
    return np.vstack([left, right])


def make_seamless_loop(sig: np.ndarray, xfade_sec: float = 2.0, sr: int = SR) -> np.ndarray:
    """
    Takes a signal of length (target + xfade_sec) and wraps the trailing xfade_sec
    over the opening xfade_sec with an equal-power cos/sin curve so loop boundary
    is sample-continuous with zero energy dip and zero click.
    """
    xf = int(round(xfade_sec * sr))
    if sig.ndim == 1:
        target_n = len(sig) - xf
        assert target_n > xf, "Signal must be longer than 2x crossfade"
        theta = np.linspace(0.0, 0.5 * math.pi, xf, endpoint=False)
        fade_in = np.sin(theta)
        fade_out = np.cos(theta)
        out = sig[:target_n].copy()
        out[:xf] = sig[:xf] * fade_in + sig[target_n : target_n + xf] * fade_out
        return out
    else:
        target_n = sig.shape[1] - xf
        assert target_n > xf, "Signal must be longer than 2x crossfade"
        theta = np.linspace(0.0, 0.5 * math.pi, xf, endpoint=False)
        fade_in = np.sin(theta)[None, :]
        fade_out = np.cos(theta)[None, :]
        out = sig[:, :target_n].copy()
        out[:, :xf] = sig[:, :xf] * fade_in + sig[:, target_n : target_n + xf] * fade_out
        return out


# ---------------------------------------------------------------------------
# Military VHF Tactical Radio Processor
# ---------------------------------------------------------------------------

def process_tactical_radio(voice_mono: np.ndarray, rng: np.random.Generator, sr: int = SR) -> np.ndarray:
    """
    Transforms clean dry speech into authentic military encrypted VHF/UHF tactical comms:
    - Slight pitch drop (-1.0 semitone) for authoritative grit
    - PTT mic key-up transient + dual-tone tactical beep chirp (subtle, authentic)
    - Aggressive 340Hz-3050Hz comm bandpass + 1.55kHz vocal horn presence
    - Subtle warm transistor saturation + fast AGC opto compression
    - Faint continuous RF carrier noise floor (ducked/gated during speech)
    - Authentic VHF squelch burst tail at transmission release
    """
    # Subtle -4.5% speed/pitch drop via cubic resampling for deeper authoritative comms timbre
    n_orig = len(voice_mono)
    n_pitched = int(round(n_orig * 1.045))
    x_old = np.linspace(0.0, 1.0, n_orig)
    x_new = np.linspace(0.0, 1.0, n_pitched)
    v = np.interp(x_new, x_old, voice_mono)

    # Trim dead silence at edges of raw TTS first
    pk = np.max(np.abs(v))
    if pk > 1e-6:
        v = v / pk
        active = np.where(np.abs(v) > 0.012)[0]
        if len(active) > 0:
            s0 = max(0, active[0] - int(0.025 * sr))
            s1 = min(len(v), active[-1] + int(0.055 * sr))
            v = v[s0:s1]

    # Voice radio EQ & nonlinear drive
    v = butter_bp(v, 340.0, 3100.0, order=4, sr=sr)
    v = biquad_peaking(v, 1480.0, 6.5, q=1.35, sr=sr)
    v = biquad_peaking(v, 2450.0, 3.5, q=1.8, sr=sr)
    v = compressor(v, thresh_db=-18.0, ratio=6.5, attack_ms=2.0, release_ms=55.0, makeup_db=6.0, sr=sr)
    v = soft_saturate(v, drive=2.6, asym=0.14)
    v = butter_bp(v, 360.0, 3000.0, order=3, sr=sr)

    # Subtle helmet/mic capsule early reflection
    ir_mic = make_metallic_chamber_ir(rt60=0.09, rng=rng, sr=sr)
    v = convolve_reverb(v, ir_mic, wet=0.14, dry=0.95, extend=False)

    # Normalize speech bed
    v = v / max(np.max(np.abs(v)), 1e-6) * 0.85

    # Build full transmission timeline: [PTT open 95ms] + [Speech] + [VHF squelch tail 145ms]
    pre_s = 0.095
    post_s = 0.145
    total_n = int(round((pre_s + post_s) * sr)) + len(v)
    out = np.zeros(total_n, dtype=np.float64)

    # 1. PTT key-up click + encrypted comms open chirp (ultra-brief, authentic Harris PRC-152 style)
    ptt_n = int(0.085 * sr)
    t_ptt = make_time(0.085, sr)
    click_burst = butter_bp(white_noise(ptt_n, rng), 900.0, 3400.0, order=3, sr=sr) * env_ad(ptt_n, 0.001, 0.012, sr=sr)
    chirp = (
        np.sin(2.0 * math.pi * 1760.0 * t_ptt) * env_trapezoid(ptt_n, 0.002, 0.018, 0.008, sr=sr)
        * (t_ptt > 0.012)
        * (t_ptt < 0.044)
    )
    ptt_sig = click_burst * 0.55 + chirp * 0.14
    mix_at(out, ptt_sig, 0.005, gain=1.0, sr=sr)

    # 2. Speech
    mix_at(out, v, pre_s, gain=1.0, sr=sr)

    # 3. Subtle continuous RF carrier hiss + occasional micro-crackle during active transmission
    rf_bed = butter_bp(white_noise(total_n, rng), 500.0, 2900.0, order=3, sr=sr)
    rf_crackle = butter_bp(crackle_noise(total_n, 45.0, rng, sr), 800.0, 3200.0, order=2, sr=sr)
    tx_env = env_trapezoid(total_n, 0.015, max(0.05, total_n / sr - 0.065), 0.04, sr=sr)
    out += (rf_bed * 0.022 + rf_crackle * 0.035) * tx_env

    # 4. VHF squelch tail release burst + mic drop click
    sq_n = int(0.11 * sr)
    sq_noise = butter_bp(white_noise(sq_n, rng), 650.0, 3100.0, order=3, sr=sr)
    sq_env = env_ad(sq_n, 0.003, 0.052, curve=1.2, sr=sr)
    sq_click = butter_bp(white_noise(sq_n, rng), 1200.0, 4200.0, order=2, sr=sr) * env_ad(sq_n, 0.001, 0.008, sr=sr)
    squelch = soft_saturate(sq_noise * sq_env * 0.42 + sq_click * 0.35, drive=2.0)
    mix_at(out, squelch, pre_s + len(v) / sr + 0.01, gain=0.75, sr=sr)

    # Convert to tight stereo with subtle radio-room imaging
    stereo = haas_stereo(out, delay_ms=3.2, side_mix=0.22, sr=sr)
    return stereo


# ---------------------------------------------------------------------------
# Finalization, QA Trimming & 48kHz 16-bit PCM WAV Export
# ---------------------------------------------------------------------------

def remove_dc(sig: np.ndarray, hp_hz: float = 16.0, sr: int = SR) -> np.ndarray:
    """Remove DC offset cleanly via mean subtraction + subsonic HP filter."""
    if sig.ndim == 1:
        x = sig - np.mean(sig)
        return butter_hp(x, hp_hz, order=2, sr=sr)
    else:
        out = np.zeros_like(sig)
        for ch in range(sig.shape[0]):
            x = sig[ch] - np.mean(sig[ch])
            out[ch] = butter_hp(x, hp_hz, order=2, sr=sr)
        return out


def trim_silence(
    sig: np.ndarray,
    start_thresh_db: float = -60.0,
    end_thresh_db: float = -68.0,
    pad_start_ms: float = 1.5,
    pad_end_ms: float = 25.0,
    sr: int = SR,
) -> np.ndarray:
    """Trim leading and trailing silence while preserving sharp onset and natural tail."""
    mag = np.abs(sig) if sig.ndim == 1 else np.max(np.abs(sig), axis=0)
    peak = float(np.max(mag))
    if peak < 1e-9:
        return sig
    s_thr = peak * db_to_lin(start_thresh_db)
    e_thr = peak * db_to_lin(end_thresh_db)
    above_s = np.where(mag >= s_thr)[0]
    above_e = np.where(mag >= e_thr)[0]
    if len(above_s) == 0 or len(above_e) == 0:
        return sig
    i0 = max(0, int(above_s[0]) - int(round(pad_start_ms * 0.001 * sr)))
    i1 = min(len(mag), int(above_e[-1]) + int(round(pad_end_ms * 0.001 * sr)))
    if i1 <= i0 + 64:
        return sig
    return sig[i0:i1] if sig.ndim == 1 else sig[:, i0:i1]


def apply_edge_fades(sig: np.ndarray, fade_in_ms: float = 0.8, fade_out_ms: float = 15.0, sr: int = SR) -> np.ndarray:
    out = sig.copy()
    n = out.shape[-1]
    fi = min(n // 4, int(round(fade_in_ms * 0.001 * sr)))
    fo = min(n // 3, int(round(fade_out_ms * 0.001 * sr)))
    if fi > 1:
        ramp = 0.5 * (1.0 - np.cos(np.linspace(0.0, math.pi, fi)))
        if out.ndim == 1:
            out[:fi] *= ramp
        else:
            out[:, :fi] *= ramp[None, :]
    if fo > 1:
        ramp = 0.5 * (1.0 + np.cos(np.linspace(0.0, math.pi, fo)))
        if out.ndim == 1:
            out[-fo:] *= ramp
        else:
            out[:, -fo:] *= ramp[None, :]
    return out


def finalize_and_save(
    path: str,
    sig: np.ndarray,
    target_peak_db: float = -1.0,
    stereo: bool = False,
    is_loop: bool = False,
    do_trim: bool = True,
    fade_in_ms: float = 0.5,
    fade_out_ms: float = 18.0,
    hp_hz: float = 16.0,
    sr: int = SR,
) -> dict:
    """
    Master, DC-remove, peak-normalize with safety headroom (< -0.6 dBFS),
    TPDF-dither, and save as 48 kHz 16-bit signed PCM WAV.
    Returns audio telemetry dictionary.
    """
    x = np.asarray(sig, dtype=np.float64)
    if not np.all(np.isfinite(x)):
        raise ValueError(f"Non-finite audio samples in {path}")

    # Enforce requested channel layout
    if stereo:
        if x.ndim == 1:
            x = np.vstack([x, x])
        elif x.shape[0] != 2:
            raise ValueError(f"Expected (2, N) for stereo {path}, got {x.shape}")
    else:
        if x.ndim == 2:
            x = np.mean(x, axis=0)

    # Remove DC offset (for loops use simple mean subtraction to preserve loop continuity!)
    if is_loop:
        if x.ndim == 1:
            x = x - np.mean(x)
        else:
            x = x - np.mean(x, axis=1, keepdims=True)
    else:
        x = remove_dc(x, hp_hz=hp_hz, sr=sr)

    # Trim non-loops
    if (not is_loop) and do_trim:
        x = trim_silence(x, sr=sr)
        x = apply_edge_fades(x, fade_in_ms=fade_in_ms, fade_out_ms=fade_out_ms, sr=sr)
    elif not is_loop:
        x = apply_edge_fades(x, fade_in_ms=fade_in_ms, fade_out_ms=fade_out_ms, sr=sr)

    # Safety clamp target peak below -0.65 dBFS (strictly satisfying < -0.5 dBFS spec)
    safe_peak_db = min(target_peak_db, -0.65)
    target_lin = db_to_lin(safe_peak_db)

    peak = float(np.max(np.abs(x)))
    if peak < 1e-8:
        raise ValueError(f"Audio signal is silent for {path}")
    x = x * (target_lin / peak)

    # Extra safety peak ceiling
    x = true_peak_limit(x, ceiling_db=safe_peak_db, knee_db=0.4)

    # Final DC polish on quantized domain
    if x.ndim == 1:
        x = x - np.mean(x)
    else:
        x = x - np.mean(x, axis=1, keepdims=True)

    # TPDF dither at +/- 1 LSB (16-bit = 32767)
    dither_rng = np.random.default_rng(abs(hash(os.path.basename(path))) % (2**32))
    lsb = 1.0 / 32767.0
    dither = (dither_rng.uniform(-1.0, 1.0, size=x.shape) + dither_rng.uniform(-1.0, 1.0, size=x.shape)) * (0.5 * lsb)
    x_d = np.clip(x + dither, -0.99, 0.99)
    pcm = np.round(x_d * 32767.0).astype(np.int16)

    os.makedirs(os.path.dirname(path), exist_ok=True)
    n_channels = 2 if stereo else 1
    if stereo:
        interleaved = np.empty(pcm.shape[1] * 2, dtype=np.int16)
        interleaved[0::2] = pcm[0]
        interleaved[1::2] = pcm[1]
        raw_bytes = interleaved.tobytes()
        n_frames = pcm.shape[1]
    else:
        raw_bytes = pcm.tobytes()
        n_frames = len(pcm)

    with wave.open(path, "wb") as wf:
        wf.setnchannels(n_channels)
        wf.setsampwidth(2)
        wf.setframerate(sr)
        wf.writeframes(raw_bytes)

    # Compute exact telemetry from written PCM
    f_pcm = pcm.astype(np.float64) / 32767.0
    actual_peak = float(np.max(np.abs(f_pcm)))
    actual_rms = float(np.sqrt(np.mean(f_pcm ** 2)))
    dc_off = float(np.max(np.abs(np.mean(f_pcm, axis=-1))))

    loop_jump = None
    if is_loop:
        if stereo:
            loop_jump = float(np.max(np.abs(f_pcm[:, -1] - f_pcm[:, 0])))
        else:
            loop_jump = float(abs(f_pcm[-1] - f_pcm[0]))

    return {
        "durationSec": round(n_frames / sr, 4),
        "sampleRate": sr,
        "channels": n_channels,
        "bitDepth": 16,
        "peakDbfs": round(lin_to_db(actual_peak), 2),
        "rmsDbfs": round(lin_to_db(actual_rms), 2),
        "dcOffset": round(dc_off, 6),
        "loopBoundaryDelta": round(loop_jump, 5) if loop_jump is not None else None,
    }
