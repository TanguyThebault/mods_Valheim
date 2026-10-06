"""Synthesizes the Wind Blade's air slash (16-bit mono WAV) into sfx/wind_slash_*.wav.

A slash of compressed air: a short metallic "shing" of the blade, then a fast whoosh whose band sweeps up as the
air is cut and down as the blade of air flies off (a passing gust), with a soft airy tail. Unvoiced, no tone.
Run: uv run --with numpy --with scipy python tools/synth_wind.py
"""
import wave
from pathlib import Path

import numpy as np
from scipy.signal import butter, sosfilt

RATE = 44100
OUT = Path(__file__).resolve().parent.parent / "sfx"


def band(x, lo, hi):
    return sosfilt(butter(2, [lo, hi], "bandpass", fs=RATE, output="sos"), x)


def sweep_noise(rng, n, f_of_t, width=0.9):
    """Noise through a band whose centre follows f_of_t (block-wise), one octave * width wide."""
    out = np.zeros(n)
    white = rng.standard_normal(n + 512)
    block = 256
    for i in range(0, n, block):
        f = f_of_t(i / RATE)
        lo, hi = f / 2 ** (width / 2), min(f * 2 ** (width / 2), RATE / 2 - 200)
        seg = band(white[i:i + block + 512], lo, hi)[512:512 + block]
        out[i:i + len(seg)] = seg[: n - i]
    return out


def slash(rng, seconds=0.75):
    n = int(RATE * seconds)
    t = np.arange(n) / RATE
    peak = rng.uniform(0.09, 0.13)
    # the cut: centre rises fast to ~4 kHz at the peak, then falls as the air blade flies away
    f = lambda s: 700 + 3600 * min(s / peak, 1.0) ** 1.5 if s < peak else 4300 * np.exp(-(s - peak) / 0.22) + 500
    whoosh = sweep_noise(rng, n, f)
    env = np.where(t < peak, (t / peak) ** 2, np.exp(-(t - peak) / 0.17))
    whoosh = whoosh / (np.max(np.abs(whoosh)) + 1e-9) * env
    # body: lower rush of displaced air under it
    body = band(rng.standard_normal(n), 250, 1200)
    body = body / (np.max(np.abs(body)) + 1e-9) * np.where(t < peak, t / peak, np.exp(-(t - peak) / 0.25)) * 0.45
    # the blade's "shing": a few inharmonic partials, very short
    ring = sum(np.sin(2 * np.pi * fr * t + rng.uniform(0, 6)) * a for fr, a in
               ((rng.uniform(5200, 5600), 1.0), (rng.uniform(7400, 7900), 0.6), (rng.uniform(9800, 10400), 0.35)))
    ring *= np.clip(t / 0.004, 0, 1) * np.exp(-t / 0.06) * 0.22
    x = whoosh + body + ring
    fade = int(RATE * 0.05)
    x[-fade:] *= np.linspace(1, 0, fade)
    return x


def save(name, x):
    x = x / (np.max(np.abs(x)) + 1e-9) * 0.9
    OUT.mkdir(exist_ok=True)
    with wave.open(str(OUT / name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes((x * 32767).astype("<i2").tobytes())
    print("wrote", OUT / name)


if __name__ == "__main__":
    for k in range(3):
        save("wind_slash_%d.wav" % k, slash(np.random.default_rng(300 + k)))
