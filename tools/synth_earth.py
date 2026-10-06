"""Synthesizes the Earthbreaker quake and the Fox Fang rustle (16-bit mono WAV) into sfx/.

- quake_*.wav: a sub-bass boom and a crack as the mace hits, the ground tearing open along the crack, a long
  rolling rumble, stones and pebbles raining down; soft-clipped for weight. Noise only, no tone.
- rustle_*.wav: dead leaves swirling, dry crackles over a soft breathy whoosh of forest air.
Run: uv run --with numpy --with scipy python tools/synth_earth.py
"""
import wave
from pathlib import Path

import numpy as np
from scipy.signal import butter, sosfilt

RATE = 44100
OUT = Path(__file__).resolve().parent.parent / "sfx"


def band(x, lo, hi, order=2):
    return sosfilt(butter(order, [lo, hi], "bandpass", fs=RATE, output="sos"), x)


def low(x, f, order=2):
    return sosfilt(butter(order, f, "lowpass", fs=RATE, output="sos"), x)


def norm(x):
    return x / (np.max(np.abs(x)) + 1e-9)


def quake(rng, seconds=4.2):
    """The mace hits the ground and the earth tears open: a deep sub-bass boom with a hard crack on top, the
    ground ripping along the crack (a dense, gritty tearing that runs away), a long rolling rumble that swells
    after the hit, and stones and pebbles raining back down. Soft-clipped for weight. Noise only, no tone."""
    n = int(RATE * seconds)
    t = np.arange(n) / RATE
    # impact: sub-bass boom (felt more than heard), a mid thump, a sharp broadband crack
    boom = norm(low(rng.standard_normal(n), 70, 4)) * np.exp(-t / 0.45) * np.clip(t / 0.006, 0, 1)
    thump = norm(band(rng.standard_normal(n), 120, 450, 4)) * np.exp(-t / 0.15) * np.clip(t / 0.003, 0, 1)
    crack = norm(band(rng.standard_normal(n), 900, 9000)) * np.exp(-t / 0.035)
    # the tear: dense grains of gritty noise along 0.9 s, darker and quieter as the crack runs away
    tear = np.zeros(n)
    tc = 0.02
    while tc < 0.9:
        i = int(RATE * tc)
        L = int(RATE * rng.uniform(0.006, 0.03))
        if i + L < n:
            dist = tc / 0.9
            seg = band(rng.standard_normal(L + 256), 150, 5000 - 3800 * dist)[256:]
            tear[i:i + L] += norm(seg) * np.exp(-np.arange(L) / (RATE * 0.01)) * (1 - 0.65 * dist) * rng.uniform(0.4, 1)
        tc += rng.exponential(0.012)
    # rumble: rolling, swelling for a moment after the hit, then dying away
    roll = 1 + 0.35 * np.interp(t, np.linspace(0, seconds, 16), rng.uniform(-1, 1, 16))
    swell = np.clip(t / 0.25, 0, 1) * np.exp(-np.clip(t - 0.25, 0, None) / 1.3)
    rumble = norm(band(rng.standard_normal(n), 25, 160, 4)) * swell * roll
    # debris: stones (bigger, duller) then pebbles (small, bright), falling back over 2.5 s
    debris = np.zeros(n)
    for big, count, (lo, hi), dec in ((True, 30, (300, 2500), 0.012), (False, 70, (1500, 7000), 0.004)):
        for _ in range(count):
            i = int(RATE * rng.uniform(0.35, 2.8))
            L = int(RATE * rng.uniform(0.008, 0.04) if big else RATE * rng.uniform(0.003, 0.012))
            if i + L < n:
                seg = band(rng.standard_normal(L + 128), lo, hi)[128:]
                debris[i:i + L] += norm(seg) * np.exp(-np.arange(L) / (RATE * dec)) * rng.uniform(0.2, 0.8) * np.exp(-(i / RATE - 0.35) / 1.4)
    x = 1.3 * boom + 0.8 * thump + 0.5 * crack + 0.7 * norm(tear) + 1.0 * rumble + 0.3 * norm(debris)
    x = np.tanh(1.6 * x / np.max(np.abs(x))) / np.tanh(1.6)       # soft clip: punch and loudness
    fade = int(RATE * 0.3)
    x[-fade:] *= np.linspace(1, 0, fade)
    return x


def rustle(rng, seconds=1.1):
    n = int(RATE * seconds)
    t = np.arange(n) / RATE
    env = np.sin(np.pi * np.clip(t / seconds, 0, 1)) ** 1.5
    air = norm(band(rng.standard_normal(n), 300, 2500)) * env * 0.35
    crackles = np.zeros(n)
    for _ in range(int(220 * seconds)):
        i = int(rng.uniform(0, n - 400))
        L = int(RATE * rng.uniform(0.001, 0.006))
        seg = band(rng.standard_normal(L + 128), 1500, 9000)[128:]
        crackles[i:i + L] += norm(seg) * np.hanning(L) * rng.uniform(0.2, 1) * env[i]
    return air + 0.6 * norm(crackles)


def save(name, x):
    x = norm(x) * 0.9
    OUT.mkdir(exist_ok=True)
    with wave.open(str(OUT / name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes((x * 32767).astype("<i2").tobytes())
    print("wrote", OUT / name)


if __name__ == "__main__":
    for k in range(3):
        save("quake_%d.wav" % k, quake(np.random.default_rng(500 + k)))
    for k in range(2):
        save("rustle_%d.wav" % k, rustle(np.random.default_rng(600 + k)))
