"""Synthesise crow caws, owl hoots and field-mouse squeaks (mono 16-bit WAV).

- crow: harsh "kraa": 450-650 Hz fundamental, many harmonics, nasal formants, rough, falling; 1-4 caws.
- owl (tawny): soft low hoots, near-sine 330-420 Hz with breathy onset; "hoo... hoo-hoo-hoooo" with a
  quavering last note.
- mouse: tiny high squeaks, 4-7 kHz chirps.

    uv run --with numpy python tools/synth_birds.py assets
"""
import sys
import wave
from pathlib import Path

import numpy as np

SR = 44100


def bandpass(x, f0, q):
    w = 2 * np.pi * f0 / SR
    alpha = np.sin(w) / (2 * q)
    b0, b2 = alpha, -alpha
    a0, a1, a2 = 1 + alpha, -2 * np.cos(w), 1 - alpha
    b0, b2, a1, a2 = b0 / a0, b2 / a0, a1 / a0, a2 / a0
    y = np.zeros_like(x)
    x1 = x2 = y1 = y2 = 0.0
    for i, xi in enumerate(x):
        yi = b0 * xi + b2 * x2 - a1 * y1 - a2 * y2
        x2, x1, y2, y1 = x1, xi, y1, yi
        y[i] = yi
    return y


def env(n, attack, release, power=1.0):
    t = np.linspace(0, 1, n)
    return (np.clip(t / attack, 0, 1) * np.clip((1 - t) / release, 0, 1)) ** power


def gap(rng, a, b):
    return np.zeros(int(SR * rng.uniform(a, b)))


# -------------------------------------------------------------------- crow

def caw(rng):
    dur = rng.uniform(0.28, 0.45)
    n = int(SR * dur)
    t = np.linspace(0, 1, n)
    f = rng.uniform(480, 620) * (1.08 - 0.22 * t)
    phase = 2 * np.pi * np.cumsum(f) / SR
    phase += 0.25 * np.cumsum(rng.normal(0, 1, n)) / np.sqrt(SR) * 25      # roughness
    src = sum(np.sin(k * phase) / k ** 0.5 for k in range(1, 18))
    src /= np.max(np.abs(src))
    voiced = src + 1.4 * norm(bandpass(src, rng.uniform(1300, 1600), 4)) + 0.9 * norm(bandpass(src, rng.uniform(2400, 2900), 5))
    noise = norm(bandpass(rng.normal(0, 1, n), 1800, 1.5))
    return (norm(voiced) + 0.08 * noise) * env(n, 0.06, 0.35, 0.7)


def crow_call(rng):
    parts = []
    for _ in range(rng.integers(1, 5)):
        parts += [caw(rng), gap(rng, 0.15, 0.3)]
    return np.concatenate(parts)


# --------------------------------------------------------------------- owl

def hoot(rng, f0, dur, quaver=0.0):
    n = int(SR * dur)
    t = np.linspace(0, 1, n)
    f = f0 * (1 + 0.04 * np.sin(np.pi * t)) * (1 + quaver * np.sin(2 * np.pi * 9 * t * dur))
    phase = 2 * np.pi * np.cumsum(f) / SR
    tone = np.sin(phase) + 0.12 * np.sin(2 * phase) + 0.03 * np.sin(3 * phase)
    breath = norm(bandpass(rng.normal(0, 1, n), f0 * 2, 2)) * env(n, 0.02, 0.9) * 0.15
    a = np.clip(t / 0.15, 0, 1) ** 2
    r = np.clip((1 - t) / 0.35, 0, 1)
    return tone * a * r + breath


def owl_call(rng, long):
    f0 = rng.uniform(340, 410)
    if long:                 # "hoo ... hoo-hoo-hoooo"
        parts = [hoot(rng, f0, 0.7), gap(rng, 1.2, 1.6), hoot(rng, f0 * 1.02, 0.25), gap(rng, 0.15, 0.2),
                 hoot(rng, f0 * 1.04, 0.25), gap(rng, 0.1, 0.15), hoot(rng, f0, 1.1, quaver=0.025)]
    else:                    # short double hoot
        parts = [hoot(rng, f0, 0.45), gap(rng, 0.35, 0.5), hoot(rng, f0 * 0.97, 0.8, quaver=0.02)]
    return np.concatenate(parts)


# ------------------------------------------------------------------- mouse

def squeak(rng):
    parts = []
    for _ in range(rng.integers(1, 4)):
        n = int(SR * rng.uniform(0.03, 0.07))
        t = np.linspace(0, 1, n)
        f = rng.uniform(4500, 6500) * (1 + 0.25 * np.sin(np.pi * t))
        phase = 2 * np.pi * np.cumsum(f) / SR
        parts += [(np.sin(phase) + 0.2 * np.sin(2 * phase)) * env(n, 0.15, 0.5), gap(rng, 0.02, 0.06)]
    return np.concatenate(parts)


# ------------------------------------------------------------------ output

def norm(x):
    return x / (np.max(np.abs(x)) + 1e-9)


def finish(sig, level):
    sig = np.concatenate([np.zeros(int(SR * 0.01)), sig, np.zeros(int(SR * 0.1))])
    fade = int(SR * 0.005)
    sig[:fade] *= np.linspace(0, 1, fade)
    sig[-fade:] *= np.linspace(1, 0, fade)
    return norm(sig) * level


def write(path, sig):
    data = (np.clip(sig, -1, 1) * 32767).astype("<i2").tobytes()
    with wave.open(str(path), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(data)


if __name__ == "__main__":
    root = Path(sys.argv[1] if len(sys.argv) > 1 else "assets")
    sets = {
        "sfx_crow": ("crow_caw", crow_call, (5, 17, 29, 41), 0.7),
        "sfx_owl": ("owl_hoot", owl_call, (7, 19, 33, 47), 0.6),
        "sfx_mouse": ("mouse_squeak", squeak, (2, 13, 24), 0.5),
    }
    for folder, (stem, fn, seeds, level) in sets.items():
        (root / folder).mkdir(parents=True, exist_ok=True)
        for i, seed in enumerate(seeds, 1):
            rng = np.random.default_rng(seed)
            sig = finish(fn(rng, i % 2 == 1) if fn is owl_call else fn(rng), level)
            write(root / folder / f"{stem}{i}.wav", sig)
            print(f"{folder}/{stem}{i}.wav  {len(sig) / SR:.2f} s")
