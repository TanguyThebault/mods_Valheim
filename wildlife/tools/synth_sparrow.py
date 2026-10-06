"""Synthesise short sparrow songs (mono 16-bit WAV) for the Meadow sparrow.

Each song is a phrase of notes built from three bird-like gestures:
- chirp: a fast downward (or upward) frequency sweep, 40-90 ms;
- trill: the same short note repeated quickly, 20-35 ms each;
- warble: a held note with fast frequency modulation.
Notes are sine waves with a quiet 2nd harmonic, a smooth envelope, and a short echo tail.

    uv run --with numpy python tools/synth_sparrow.py assets/sfx
"""
import sys
import wave
from pathlib import Path

import numpy as np

SR = 44100


def env(n):
    t = np.linspace(0, np.pi, n)
    return np.sin(t) ** 1.5


def tone(f0, f1, dur, fm_rate=0.0, fm_depth=0.0, rng=None):
    n = int(SR * dur)
    t = np.arange(n) / SR
    # exponential glide f0 -> f1
    f = f0 * (f1 / f0) ** (t / dur)
    if fm_rate:
        f = f * (1 + fm_depth * np.sin(2 * np.pi * fm_rate * t))
    phase = 2 * np.pi * np.cumsum(f) / SR
    sig = np.sin(phase) + 0.18 * np.sin(2 * phase) + 0.05 * np.sin(3 * phase)
    return sig * env(n)


def silence(dur):
    return np.zeros(int(SR * dur))


def chirp(rng, down=True):
    hi, lo = rng.uniform(5200, 7000), rng.uniform(2800, 3800)
    f0, f1 = (hi, lo) if down else (lo, hi)
    return tone(f0, f1, rng.uniform(0.04, 0.09))


def trill(rng):
    f = rng.uniform(3800, 5600)
    d = rng.uniform(0.02, 0.035)
    reps = rng.integers(5, 11)
    parts = []
    for _ in range(reps):
        parts += [tone(f * 1.08, f * 0.95, d), silence(d * 0.5)]
    return np.concatenate(parts)


def warble(rng):
    f = rng.uniform(3500, 5000)
    return tone(f, f * rng.uniform(0.9, 1.15), rng.uniform(0.15, 0.3),
                fm_rate=rng.uniform(25, 45), fm_depth=rng.uniform(0.06, 0.12))


def song(seed):
    rng = np.random.default_rng(seed)
    parts = []
    for _ in range(rng.integers(4, 8)):
        kind = rng.choice(["chirp", "chirp", "trill", "warble"])
        if kind == "chirp":
            for _ in range(rng.integers(1, 4)):
                parts += [chirp(rng, down=rng.random() < 0.75), silence(rng.uniform(0.03, 0.07))]
        elif kind == "trill":
            parts.append(trill(rng))
        else:
            parts.append(warble(rng))
        parts.append(silence(rng.uniform(0.06, 0.18)))
    s = np.concatenate(parts)
    # light echo, like singing from a branch in the open
    out = np.concatenate([s, silence(0.25)])
    for delay, gain in ((0.045, 0.25), (0.11, 0.12), (0.19, 0.06)):
        k = int(SR * delay)
        out[k:k + len(s)] += gain * s
    # 6 ms fades, normalise to -6 dBFS
    fade = int(SR * 0.006)
    out[:fade] *= np.linspace(0, 1, fade)
    out[-fade:] *= np.linspace(1, 0, fade)
    return out / np.max(np.abs(out)) * 0.5


def write(path, sig):
    data = (np.clip(sig, -1, 1) * 32767).astype("<i2").tobytes()
    with wave.open(str(path), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(data)


if __name__ == "__main__":
    out = Path(sys.argv[1] if len(sys.argv) > 1 else "assets/sfx")
    out.mkdir(parents=True, exist_ok=True)
    for i, seed in enumerate((11, 23, 37, 58, 71, 94), 1):
        sig = song(seed)
        write(out / f"sparrow_song{i}.wav", sig)
        print(f"sparrow_song{i}.wav  {len(sig) / SR:.2f} s")
