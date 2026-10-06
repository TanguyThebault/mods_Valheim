"""Synthesizes the sounds of Ymir's Bite (16-bit mono WAV) into sfx/.

- ymir_cast_*.wav: Ymir's blood rising: a deep, cold breath swelling, frost crackling outwards (tiny bright ice
  ticks that spread and thin), and a glassy shimmer of inharmonic partials, like struck ice, ringing out.
- ymir_end_*.wav: the cold letting go: a soft high shimmer falling away, a few drips and small ice cracks.
Filters run over whole signals; the breath's sweep uses a per-sample state-variable filter (no block clicks).
Run: uv run --with numpy --with scipy python tools/synth_frost.py
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


def high(x, f, order=2):
    return sosfilt(butter(order, f, "highpass", fs=RATE, output="sos"), x)


def norm(x):
    return x / (np.max(np.abs(x)) + 1e-9)


def svf_band(x, freq, q=0.7):
    y = np.zeros_like(x)
    lo = bp = 0.0
    damp = 1.0 / q
    for i in range(len(x)):
        f = 2.0 * np.sin(np.pi * min(freq[i], RATE / 6) / RATE)
        hp = x[i] - lo - damp * bp
        bp += f * hp
        lo += f * bp
        y[i] = bp
    return y


def ice_ticks(rng, n, start, spread, count, lo=2500, hi=12000):
    """Frost crackling: very short bright ticks, each with a tiny resonance, denser at first then thinning."""
    x = np.zeros(n)
    for _ in range(count):
        at = start + rng.gamma(2.0, spread / 2)
        i = int(RATE * at)
        L = int(RATE * rng.uniform(0.008, 0.014))
        if i + L >= n:
            continue
        f = rng.uniform(3000, 8000)
        tt = np.arange(L) / RATE
        tick = np.sin(2 * np.pi * f * tt) * np.exp(-tt / rng.uniform(0.0015, 0.004))
        noise = band(rng.standard_normal(L + 128), lo, hi)[128:] * np.exp(-tt / 0.001)
        tick *= np.clip((L - np.arange(L)) / (RATE * 0.0015), 0, 1)       # no cut-off clicks
        noise *= np.clip((L - np.arange(L)) / (RATE * 0.0015), 0, 1)
        x[i:i + L] += (0.6 * tick + 0.4 * norm(noise)) * rng.uniform(0.2, 1) * np.exp(-(at - start) / (spread * 2.5))
    return x


def glass(rng, n, t0, partials, decay):
    """Struck ice: inharmonic partials with slight detuned pairs (a slow shimmer), long decays for the low ones."""
    t = np.arange(n) / RATE
    x = np.zeros(n)
    on = (t >= t0)
    tt = np.clip(t - t0, 0, None)
    for f, a in partials:
        f *= rng.uniform(0.995, 1.005)
        d = decay * (1200 / f) ** 0.3
        x += a * (np.sin(2 * np.pi * f * tt) + 0.6 * np.sin(2 * np.pi * f * 1.0035 * tt + 0.7)) * np.exp(-tt / d)
    return x * on * np.clip(tt / 0.004, 0, 1)


def cast(rng, seconds=2.6):
    n = int(RATE * seconds)
    t = np.arange(n) / RATE
    # a deep cold breath drawn in then blown out: noise in a band rising then falling
    centre = 300 + 900 * np.sin(np.pi * np.clip(t / 2.0, 0, 1))
    breath_env = np.clip(t / 0.15, 0, 1) * np.sin(np.pi * np.clip(t / 1.8, 0, 1)) ** 1.5
    breath = norm(svf_band(rng.standard_normal(n), centre, 0.9)) * breath_env
    air = norm(band(rng.standard_normal(n), 2000, 9000)) * breath_env ** 2
    sub = high(norm(low(rng.standard_normal(n), 140, 4)), 50, 2) * np.clip(t / 0.4, 0, 1) * np.exp(-np.clip(t - 0.4, 0, None) / 0.6)
    ticks = ice_ticks(rng, n, 0.2, 0.35, 260)
    ring = glass(rng, n, 0.4, ((1180, 1.0), (1985, 0.6), (2790, 0.45), (4210, 0.3), (5530, 0.2), (7380, 0.12)), 0.45)
    ring2 = glass(rng, n, 0.62, ((1560, 0.6), (2620, 0.4), (3950, 0.25), (6100, 0.15)), 0.45)
    x = 0.55 * breath + 0.18 * air + 0.14 * sub + 0.35 * norm(ticks) + 0.3 * norm(ring + ring2)
    x = norm(x)
    x *= np.clip(t / 0.03, 0, 1) * np.clip((seconds - t) / 0.4, 0, 1)
    return x


def end(rng, seconds=1.6):
    n = int(RATE * seconds)
    t = np.arange(n) / RATE
    # a shimmer that falls away: partials gliding down a little as they fade
    x = np.zeros(n)
    for f, a in ((2400, 0.4), (3610, 0.45), (5020, 0.3), (6880, 0.2)):
        glide = f * (1 - 0.15 * np.clip(t / seconds, 0, 1))
        phase = 2 * np.pi * np.cumsum(glide) / RATE
        x += a * (np.sin(phase) + 0.6 * np.sin(phase * 1.004 + 0.9)) * np.exp(-t / 0.4)
    x *= np.clip(t / 0.15, 0, 1)
    # melting: soft drips (a short falling sine blip) and a few small cracks
    drips = np.zeros(n)
    for _ in range(6):
        i = int(RATE * rng.uniform(0.2, 1.0))
        L = int(RATE * 0.05)
        if i + L >= n:
            continue
        tt = np.arange(L) / RATE
        f = rng.uniform(900, 1600) * (1 + 1.5 * tt / 0.05)
        drips[i:i + L] += np.sin(2 * np.pi * np.cumsum(f) / RATE) * np.exp(-tt / 0.012) * np.clip(tt / 0.002, 0, 1) * rng.uniform(0.3, 0.8) * np.exp(-i / RATE / 0.8)
    cracks = ice_ticks(rng, n, 0.15, 0.4, 25, 1500, 8000)
    air = norm(band(rng.standard_normal(n), 3000, 9000)) * np.exp(-t / 0.4) * 0.15
    y = 0.45 * norm(x) + 0.35 * norm(drips) + 0.3 * norm(cracks) + air
    y = norm(y)
    y *= np.clip(t / 0.02, 0, 1)
    y *= np.clip((seconds - t) / 0.3, 0, 1)
    return y


def giant(rng, seconds=1.9):
    """The body of a giant swelling: a deep, muffled boom with a falling thump (90 to 45 Hz, saturated so its
    harmonics carry the depth on small speakers), a slow groaning swell like ice under strain, dull ice cracks."""
    n = int(RATE * seconds)
    t = np.arange(n) / RATE
    f = 45 + 45 * np.exp(-t / 0.5)
    thump = np.tanh(2.5 * np.sin(2 * np.pi * np.cumsum(f) / RATE)) * np.exp(-t / 0.45) * np.clip(t / 0.01, 0, 1)
    boom = norm(band(rng.standard_normal(n), 60, 350, 2)) * np.exp(-t / 0.45) * np.clip(t / 0.015, 0, 1)
    swell_env = np.sin(np.pi * np.clip((t - 0.15) / 1.5, 0, 1)) ** 1.5
    groan_f = 90 + 25 * np.sin(2 * np.pi * 0.7 * t) + 15 * np.interp(t, np.linspace(0, seconds, 12), rng.uniform(-1, 1, 12))
    groan = np.sin(2 * np.pi * np.cumsum(groan_f) / RATE)
    groan = low(np.tanh(3 * groan) * (1 + 0.3 * rng.standard_normal(n)), 600, 4) * swell_env
    rumble = high(norm(low(rng.standard_normal(n), 200, 4)), 30, 2) * swell_env
    cracks = np.zeros(n)
    for _ in range(7):
        i = int(RATE * rng.uniform(0.3, 1.4))
        L = int(RATE * 0.08)
        if i + L >= n:
            continue
        seg = low(rng.standard_normal(L + 256), 1500, 2)[256:]
        cracks[i:i + L] += norm(seg) * np.exp(-np.arange(L) / (RATE * 0.015)) * np.clip(np.arange(L) / (RATE * 0.002), 0, 1) * rng.uniform(0.4, 1)
    x = 0.7 * thump + 0.7 * boom + 0.75 * norm(groan) + 0.3 * rumble + 0.55 * norm(cracks)
    x = high(low(x, 1200, 2), 60, 2)
    x *= np.clip(t / 0.005, 0, 1) * np.clip((seconds - t) / 0.35, 0, 1)
    return norm(x)                                   # no final saturation: the boom keeps its punch

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
        save("ymir_cast_%d.wav" % k, cast(np.random.default_rng(800 + k)))
        save("ymir_end_%d.wav" % k, end(np.random.default_rng(810 + k)))
        save("ymir_giant_%d.wav" % k, giant(np.random.default_rng(820 + k)))
