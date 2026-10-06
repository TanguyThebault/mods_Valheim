"""Synthesizes the Thunder Spear sounds (16-bit mono WAV) into sfx/:
- thunder_charge.wav: 4 s of rising electric crackle while the lightning is called.
- thunder_strike_*.wav: a lightning crack followed by a rolling thunder rumble.
Run: uv run --with numpy python tools/synth_thunder.py
"""
import wave
from pathlib import Path

import numpy as np

RATE = 44100
OUT = Path(__file__).resolve().parent.parent / "sfx"


def lowpass(x, cutoff):
    a = np.exp(-2 * np.pi * cutoff / RATE)
    y = np.empty_like(x)
    acc = 0.0
    for i, v in enumerate(x):
        acc = (1 - a) * v + a * acc
        y[i] = acc
    return y


def highpass(x, cutoff):
    return x - lowpass(x, cutoff)


def save(name, x):
    x = x / (np.max(np.abs(x)) + 1e-9) * 0.95
    OUT.mkdir(exist_ok=True)
    with wave.open(str(OUT / name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes((x * 32767).astype("<i2").tobytes())
    print("wrote", OUT / name)


def charge(rng, seconds=4.0):
    n = int(RATE * seconds)
    t = np.arange(n) / RATE
    ramp = (t / seconds) ** 1.6
    # Mains-like hum rising in pitch, with a buzzy (clipped) edge.
    freq = 55 + 70 * ramp
    phase = 2 * np.pi * np.cumsum(freq) / RATE
    hum = np.tanh(3 * np.sin(phase)) * 0.25 + np.sin(2 * phase) * 0.1
    # Crackles: short highpassed noise ticks, more and more frequent.
    crack = np.zeros(n)
    i = 0
    while i < n:
        gap = int(RATE * rng.uniform(0.012, 0.09) * (1.2 - ramp[i]))
        i += max(gap, 60)
        if i >= n:
            break
        length = int(RATE * rng.uniform(0.002, 0.012))
        seg = rng.standard_normal(length) * np.exp(-np.linspace(0, 6, length))
        end = min(n, i + length)
        crack[i:end] += seg[: end - i] * rng.uniform(0.3, 1.0)
    crack = highpass(crack, 2500)
    x = hum * (0.2 + 0.8 * ramp) + crack * (0.3 + 0.9 * ramp)
    fade = np.minimum(1, t / 0.15) * np.minimum(1, (seconds - t) / 0.05)
    return x * fade


def strike(rng, seconds=4.5):
    n = int(RATE * seconds)
    t = np.arange(n) / RATE
    # Crack: a few very short broadband bursts in the first 80 ms.
    crack = np.zeros(n)
    for k in range(rng.integers(3, 6)):
        start = int(RATE * rng.uniform(0, 0.08))
        length = int(RATE * rng.uniform(0.01, 0.05))
        seg = rng.standard_normal(length) * np.exp(-np.linspace(0, 5, length))
        crack[start:start + length] += seg[: n - start] * rng.uniform(0.6, 1.0)
    crack = highpass(crack, 600) * 1.4
    # Rumble: brown-ish noise, lowpassed, with slow random swells, decaying over seconds.
    noise = rng.standard_normal(n)
    rumble = lowpass(lowpass(noise, 180), 120)
    swells = lowpass(rng.standard_normal(n), 3)
    swells = 0.6 + 0.4 * swells / (np.max(np.abs(swells)) + 1e-9)
    env = np.exp(-t / 1.3) * np.minimum(1, t / 0.03)
    rumble = rumble / (np.max(np.abs(rumble)) + 1e-9) * env * swells
    # Body: a mid-band roar right after the crack.
    body = lowpass(highpass(rng.standard_normal(n), 150), 1500) * np.exp(-t / 0.35)
    x = crack + rumble * 1.1 + body * 0.5
    x = np.tanh(x * 1.5)
    return x * np.minimum(1, (seconds - t) / 0.3)


def main():
    rng = np.random.default_rng(7)
    save("thunder_charge.wav", charge(rng))
    for k in range(3):
        save("thunder_strike_%d.wav" % k, strike(np.random.default_rng(100 + k)))


if __name__ == "__main__":
    main()
