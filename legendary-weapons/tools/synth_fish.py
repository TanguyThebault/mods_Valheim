"""Synthesizes the Skyfisher's Rod sounds (16-bit mono WAV) into sfx/.

- reel_*.wav: the reel spinning out as the line flies up (ratchet clicks speeding up, then slowing), and a
  sharp double tug when something bites.
- fishfall_*.wav: the giant fish coming down: a rising rush of air with a cartoonish falling whistle on top
  (a descending glide), getting louder as it nears.
- fishimpact_*.wav: the giant fish bursts: a huge boom, a wet crack, gore splattering all around, chunks and blood
  raining down (impact_big; the first, smaller fishimpact() is kept for reference).
- strain_*.wav: the struggle before the yank: the rod creaking, the line humming taut, grunts of effort.
Run: uv run --with numpy --with scipy python tools/synth_fish.py
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


def reel(rng, seconds=1.4):
    n = int(RATE * seconds)
    x = np.zeros(n)
    t, rate = 0.0, 18.0
    while t < 0.75:                                   # spinning out: clicks speeding up then easing
        i = int(RATE * t)
        L = int(RATE * 0.004)
        x[i:i + L] += band(rng.standard_normal(L + 64), 2500, 7000)[64:] * np.exp(-np.arange(L) / 30) * rng.uniform(0.6, 1)
        rate = min(rate * 1.09, 70) if t < 0.45 else rate * 0.93
        t += 1 / rate
    for tb in (0.95, 1.1):                            # the bite: two sharp tugs on the line
        i = int(RATE * tb)
        L = int(RATE * 0.06)
        seg = band(rng.standard_normal(L), 200, 1800) * np.exp(-np.arange(L) / (RATE * 0.015))
        x[i:i + L] += 1.6 * norm(seg)
    return x


def fishfall(rng, seconds=3.4):
    n = int(RATE * seconds)
    t = np.arange(n) / RATE
    swell = (t / seconds) ** 2
    rush = norm(band(rng.standard_normal(n), 300, 3000)) * (0.15 + swell)
    f = 1500 * (0.35 / 1.5) ** (t / seconds)          # a falling whistle, 1500 -> 350 Hz
    phase = 2 * np.pi * np.cumsum(f) / RATE
    whistle = np.sin(phase) * (0.3 + 0.5 * swell) * (1 + 0.04 * np.sin(2 * np.pi * 6 * t))
    x = 0.7 * rush + 0.5 * whistle
    fade = int(RATE * 0.05)
    x[:fade] *= np.linspace(0, 1, fade)
    return x


def fishimpact(rng, seconds=2.6):
    n = int(RATE * seconds)
    t = np.arange(n) / RATE
    boom = norm(low(rng.standard_normal(n), 90, 4)) * np.exp(-t / 0.4) * np.clip(t / 0.005, 0, 1)
    slap = norm(band(rng.standard_normal(n), 250, 2200, 4)) * np.exp(-t / 0.06)       # wet, fleshy smack
    spray = norm(band(rng.standard_normal(n), 2000, 9000)) * np.clip((t - 0.05) / 0.1, 0, 1) * np.exp(-np.clip(t - 0.15, 0, None) / 0.5)
    bits = np.zeros(n)
    for _ in range(60):
        i = int(RATE * rng.uniform(0.3, 2.0))
        L = int(RATE * rng.uniform(0.004, 0.015))
        if i + L < n:
            bits[i:i + L] += band(rng.standard_normal(L + 64), 600, 4000)[64:] * np.exp(-np.arange(L) / (RATE * 0.004)) * rng.uniform(0.2, 0.8)
    x = 1.3 * boom + 0.9 * slap + 0.4 * spray + 0.3 * norm(bits)
    x = np.tanh(1.5 * x / np.max(np.abs(x))) / np.tanh(1.5)
    fade = int(RATE * 0.2)
    x[-fade:] *= np.linspace(1, 0, fade)
    return x


def strain(rng, seconds=2.9):
    """The struggle: the rod creaking under load, the line humming taut and wavering, and heavy grunts of effort."""
    n = int(RATE * seconds)
    t = np.arange(n) / RATE
    creak = np.zeros(n)
    tc = 0.0
    while tc < seconds - 0.1:                         # wood creaks: bursts of slow, irregular clicks
        i = int(RATE * tc)
        L = int(RATE * rng.uniform(0.15, 0.4))
        if i + L < n:
            k = np.zeros(L)
            for c in np.cumsum(rng.uniform(0.004, 0.02, 60)):
                j = int(RATE * c)
                if j < L - 40:
                    k[j:j + 40] += np.exp(-np.arange(40) / 8) * rng.uniform(0.3, 1)
            creak[i:i + L] += band(k, 250, 1400)
        tc += rng.uniform(0.3, 0.7)
    f = 190 * (1 + 0.06 * np.sin(2 * np.pi * 7 * t) + 0.04 * np.sin(2 * np.pi * 2.3 * t)) * (1 + 0.3 * t / seconds)
    hum = np.sin(2 * np.pi * np.cumsum(f) / RATE) + 0.4 * np.sin(4 * np.pi * np.cumsum(f) / RATE)
    hum *= 0.25 * np.clip(t / 0.3, 0, 1)
    grunts = np.zeros(n)
    for g0 in (0.2, 1.1, 2.0):                        # "hnnngh": a buzzing voice through an /uh/ vowel
        L = int(RATE * rng.uniform(0.55, 0.75))
        i = int(RATE * g0)
        tt = np.arange(L) / RATE
        f0 = rng.uniform(95, 120) * (1 + 0.15 * tt / tt[-1])
        src = np.sign(np.sin(2 * np.pi * np.cumsum(f0) / RATE)) + 0.3 * rng.standard_normal(L)
        v = sum(g * band(src, fc * 0.85, fc * 1.15) for fc, g in ((620, 1.0), (1180, 0.6), (2500, 0.25)))
        env = np.clip(tt / 0.08, 0, 1) * np.clip((tt[-1] - tt) / 0.15, 0, 1) * (1 + 0.25 * np.sin(2 * np.pi * 9 * tt))
        grunts[i:i + L] += norm(v) * env
    return 0.5 * norm(creak) + hum + 0.7 * grunts


def impact_big(rng, seconds=4.5):
    """The giant fish bursts: a huge boom, a wet crack, gore splattering everywhere, chunks and blood raining down."""
    n = int(RATE * seconds)
    t = np.arange(n) / RATE
    boom = norm(low(rng.standard_normal(n), 60, 4)) * np.exp(-t / 0.8) * np.clip(t / 0.006, 0, 1)
    crack = norm(band(rng.standard_normal(n), 600, 9000)) * np.exp(-t / 0.05)
    slap = norm(band(rng.standard_normal(n), 200, 1800, 4)) * np.exp(-t / 0.12)
    splats = np.zeros(n)
    for _ in range(90):                               # wet chunks landing all around
        i = int(RATE * rng.uniform(0.25, 3.5))
        L = int(RATE * rng.uniform(0.02, 0.08))
        if i + L < n:
            seg = band(rng.standard_normal(L + 128), 150, rng.uniform(900, 2500))[128:]
            splats[i:i + L] += norm(seg) * np.exp(-np.arange(L) / (RATE * 0.015)) * rng.uniform(0.2, 1) * np.exp(-(i / RATE) / 1.6)
    rain = norm(band(rng.standard_normal(n), 1500, 7000)) * np.clip((t - 0.6) / 0.4, 0, 1) * np.exp(-np.clip(t - 1.0, 0, None) / 1.2)
    rumble = norm(band(rng.standard_normal(n), 25, 140, 4)) * np.clip(t / 0.2, 0, 1) * np.exp(-t / 1.6)
    x = 1.5 * boom + 0.6 * crack + 0.9 * slap + 0.6 * norm(splats) + 0.25 * rain + 0.9 * rumble
    x = np.tanh(1.8 * x / np.max(np.abs(x))) / np.tanh(1.8)
    fade = int(RATE * 0.3)
    x[-fade:] *= np.linspace(1, 0, fade)
    return x


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
    for k in range(2):
        save("reel_%d.wav" % k, reel(np.random.default_rng(700 + k)))
        save("fishfall_%d.wav" % k, fishfall(np.random.default_rng(710 + k)))
        save("fishimpact_%d.wav" % k, impact_big(np.random.default_rng(720 + k)))
        save("strain_%d.wav" % k, strain(np.random.default_rng(730 + k)))
