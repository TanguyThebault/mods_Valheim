"""Synthesise cetacean blows (the spout from the blowhole), mono 16-bit WAV.

A blow is a burst of breath: band-passed noise with a fast attack and a long exhale, plus (for whales) a low
rumble of air through a big chest. Whales: deep, ~2 s. Orcas: shorter and brighter "pfff".

    uv run --with numpy python tools/synth_sea.py assets
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


def norm(x):
    return x / (np.max(np.abs(x)) + 1e-9)


def blow(rng, dur, centre, q, rumble):
    n = int(SR * dur)
    t = np.linspace(0, 1, n)
    noise = rng.normal(0, 1, n)
    # the breath brightens at the burst then darkens as it fades
    hi = norm(bandpass(noise, centre * 1.6, q))
    lo = norm(bandpass(noise, centre * 0.6, q))
    mix = hi * (1 - t) ** 1.5 + lo * (0.4 + 0.6 * t)
    env = np.clip(t / 0.05, 0, 1) * (1 - t) ** 1.2
    out = norm(mix) * env
    if rumble:
        r = norm(bandpass(rng.normal(0, 1, n), 70, 2.0)) * np.clip(t / 0.1, 0, 1) * (1 - t) ** 2
        out = out + 0.6 * r
    # a little turbulence flutter
    out *= 1 + 0.15 * np.sin(2 * np.pi * rng.uniform(9, 14) * t * dur)
    return out


def finish(sig, level):
    sig = np.concatenate([np.zeros(int(SR * 0.01)), sig, np.zeros(int(SR * 0.1))])
    fade = int(SR * 0.01)
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
    root = Path(sys.argv[1] if len(sys.argv) > 1 else "assets") / "sfx_sea"
    root.mkdir(parents=True, exist_ok=True)
    for i, seed in enumerate((3, 9, 21), 1):
        rng = np.random.default_rng(seed)
        write(root / f"whale_blow{i}.wav", finish(blow(rng, rng.uniform(1.8, 2.4), 650, 0.9, True), 0.8))
    for i, seed in enumerate((4, 10, 22), 1):
        rng = np.random.default_rng(seed)
        write(root / f"orca_blow{i}.wav", finish(blow(rng, rng.uniform(0.6, 0.85), 1400, 1.2, False), 0.7))
    print("wrote", sorted(p.name for p in root.iterdir()))
