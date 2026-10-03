"""Synthesise red fox calls (mono 16-bit WAV): barks/yelps, hurt yelps, attack gekkering and a death whine.

Fox calls are harsh and nasal: a high, fast-gliding fundamental (500-1200 Hz) with strong harmonics, some
breath noise, shaped by two formant resonators. No wolf howls.

    uv run --with numpy python tools/synth_fox.py assets/sfx_fox
"""
import sys
import wave
from pathlib import Path

import numpy as np

SR = 44100


def biquad_bandpass(x, f0, q):
    w = 2 * np.pi * f0 / SR
    alpha = np.sin(w) / (2 * q)
    b0, b1, b2 = alpha, 0.0, -alpha
    a0, a1, a2 = 1 + alpha, -2 * np.cos(w), 1 - alpha
    b0, b1, b2, a1, a2 = b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0
    y = np.zeros_like(x)
    x1 = x2 = y1 = y2 = 0.0
    for i, xi in enumerate(x):
        yi = b0 * xi + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2
        x2, x1, y2, y1 = x1, xi, y1, yi
        y[i] = yi
    return y


def voice(f_curve, rng, rasp=0.25, harmonics=12):
    """Harmonic source following f_curve (Hz per sample), plus breath noise, through two formants."""
    phase = 2 * np.pi * np.cumsum(f_curve) / SR
    # jitter makes it rough rather than flute-like
    phase += 0.15 * np.cumsum(rng.normal(0, 1, len(f_curve))) / np.sqrt(SR) * 40
    src = sum(np.sin(k * phase) / k ** 0.9 for k in range(1, harmonics + 1))
    src = src / np.max(np.abs(src))
    # nasal colour: the dry voice plus two formant-boosted copies; only a touch of filtered breath noise
    f1 = biquad_bandpass(src, rng.uniform(1100, 1500), 3.0)
    f2 = biquad_bandpass(src, rng.uniform(2600, 3300), 4.0)
    voiced = src + 1.5 * f1 / (np.max(np.abs(f1)) + 1e-9) + 0.8 * f2 / (np.max(np.abs(f2)) + 1e-9)
    breath = biquad_bandpass(rng.normal(0, 1, len(src)), 2000, 1.0)
    breath = breath / (np.max(np.abs(breath)) + 1e-9)
    return voiced / np.max(np.abs(voiced)) + 0.12 * rasp * breath


def envelope(n, attack=0.08, release=0.5):
    t = np.linspace(0, 1, n)
    a = np.clip(t / attack, 0, 1)
    r = np.clip((1 - t) / release, 0, 1)
    return (a * r) ** 0.8


def bark(rng):
    dur = rng.uniform(0.16, 0.26)
    n = int(SR * dur)
    t = np.linspace(0, 1, n)
    f0 = rng.uniform(650, 900)
    # "waow": rises fast, then falls
    f = f0 * (1 + 0.45 * np.sin(np.pi * np.clip(t * 1.6, 0, 1)) - 0.25 * t)
    return voice(f, rng) * envelope(n, 0.12, 0.45)


def yelp(rng):
    dur = rng.uniform(0.12, 0.2)
    n = int(SR * dur)
    t = np.linspace(0, 1, n)
    f = rng.uniform(1000, 1300) * (1.25 - 0.55 * t)
    return voice(f, rng, rasp=0.15) * envelope(n, 0.05, 0.7)


def gekker(rng):
    pulses = []
    for _ in range(rng.integers(6, 11)):
        n = int(SR * rng.uniform(0.025, 0.04))
        f = np.full(n, rng.uniform(450, 650))
        pulses += [voice(f, rng, rasp=0.6, harmonics=8) * envelope(n, 0.2, 0.5), np.zeros(int(SR * rng.uniform(0.012, 0.025)))]
    return np.concatenate(pulses)


def whine(rng):
    dur = rng.uniform(0.7, 1.0)
    n = int(SR * dur)
    t = np.linspace(0, 1, n)
    f = 1100 * (1 - 0.55 * t) * (1 + 0.03 * np.sin(2 * np.pi * 7 * t * dur))
    return voice(f, rng, rasp=0.2) * envelope(n, 0.05, 0.6)


def gap(a, b):
    return np.zeros(int(SR * np.random.default_rng().uniform(a, b)))


def finish(sig):
    sig = np.concatenate([np.zeros(int(SR * 0.01)), sig, np.zeros(int(SR * 0.08))])
    fade = int(SR * 0.005)
    sig[:fade] *= np.linspace(0, 1, fade)
    sig[-fade:] *= np.linspace(1, 0, fade)
    return sig / np.max(np.abs(sig)) * 0.6


def write(path, sig):
    data = (np.clip(sig, -1, 1) * 32767).astype("<i2").tobytes()
    with wave.open(str(path), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(data)


if __name__ == "__main__":
    out = Path(sys.argv[1] if len(sys.argv) > 1 else "assets/sfx_fox")
    out.mkdir(parents=True, exist_ok=True)
    clips = {}
    for i, seed in enumerate((3, 14, 15, 92), 1):          # 1-3 barks: idle / alerted calls
        rng = np.random.default_rng(seed)
        parts = []
        for _ in range(rng.integers(1, 4)):
            parts += [bark(rng), gap(0.18, 0.35)]
        clips[f"call{i}"] = np.concatenate(parts)
    for i, seed in enumerate((65, 35), 1):                 # hurt
        rng = np.random.default_rng(seed)
        clips[f"hurt{i}"] = yelp(rng)
    for i, seed in enumerate((89, 79), 1):                 # attack
        rng = np.random.default_rng(seed)
        clips[f"attack{i}"] = gekker(rng)
    clips["death1"] = whine(np.random.default_rng(32))
    for name, sig in clips.items():
        sig = finish(sig)
        write(out / f"fox_{name}.wav", sig)
        print(f"fox_{name}.wav  {len(sig) / SR:.2f} s")
