"""Synthesizes Surtrbrand's sounds (16-bit mono WAV) into sfx/.

- surtr_plant_*.wav: the greatsword driven into ash and rock: a heavy stab (low thud and a gritty crunch), a short
  ring of hot steel, a hiss, then a fire catching with a soft low whoomp.
- surtr_dome_*.wav: the dome rising from the sword: a fire roar swelling and sweeping upwards, crackles, and a
  warm, slowly beating hum under it that settles as the dome stands.
- surtr_fade_*.wav: the dome burning out: the roar sinking and thinning, crackles dying, a last sizzle of ash.
- surtr_pull_*.wav: the sword pulled out of the ground: grinding steel on stone and a short hiss.
Filters run over whole signals (never block by block) and sweeps use a per-sample state-variable filter, so no
clicks between blocks.
Run: uv run --with numpy --with scipy python tools/synth_ember.py
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
    """Band-pass with a centre frequency that moves every sample (Chamberlin state-variable filter)."""
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


def roar(rng, n, centre, q=0.6):
    """Fire's roar: noise through a moving band, its loudness wavering like flames."""
    t = np.arange(n) / RATE
    flicker = 1 + 0.3 * np.interp(t, np.linspace(0, t[-1] + 1e-3, max(4, int(t[-1] * 9))), rng.uniform(-1, 1, max(4, int(t[-1] * 9))))
    return norm(svf_band(rng.standard_normal(n), centre, q)) * flicker


def crackles(rng, n, rate_per_s, env, lo=1200, hi=9000):
    """Wood-fire crackle: short bright pops, a few bigger snaps."""
    x = np.zeros(n)
    count = int(rate_per_s * n / RATE)
    for _ in range(count):
        i = int(rng.uniform(0, n - 800))
        if rng.uniform() > env[i]:
            continue
        big = rng.uniform() < 0.15
        L = int(RATE * (rng.uniform(0.004, 0.012) if big else rng.uniform(0.0008, 0.004)))
        seg = band(rng.standard_normal(L + 256), lo * (0.5 if big else 1), hi)[256:]
        x[i:i + L] += norm(seg) * np.exp(-np.arange(L) / (L / 3)) * rng.uniform(0.3, 1) * (1.6 if big else 1)
    return x


def plant(rng, seconds=1.6):
    n = int(RATE * seconds)
    t = np.arange(n) / RATE
    thud = high(norm(low(rng.standard_normal(n), 140, 4)), 55, 4) * np.exp(-t / 0.12) * np.clip(t / 0.001, 0, 1)
    click = norm(band(rng.standard_normal(n), 1500, 6000)) * np.exp(-t / 0.006)      # the stab's attack
    crunch = np.zeros(n)
    tc = 0.0
    while tc < 0.22:                                   # the blade biting into gravel and ash
        i = int(RATE * tc)
        L = int(RATE * rng.uniform(0.004, 0.02))
        seg = band(rng.standard_normal(L + 256), 400, 4500)[256:]
        crunch[i:i + L] += norm(seg) * np.hanning(L) * rng.uniform(0.4, 1) * (1 - tc / 0.3)
        tc += rng.exponential(0.008)
    # a short ring of steel: a few inharmonic partials, damped fast
    ring = np.zeros(n)
    for f, a, d in ((523, 0.5, 0.22), (1290, 0.35, 0.2), (2410, 0.25, 0.15), (3720, 0.15, 0.1)):
        f *= rng.uniform(0.98, 1.02)
        ring += a * np.sin(2 * np.pi * f * t + rng.uniform(0, 6.28)) * np.exp(-t / d)
    ring *= np.clip(t / 0.003, 0, 1)
    # hot steel hissing in the ash
    hiss = norm(band(rng.standard_normal(n), 3000, 11000)) * np.clip((t - 0.05) / 0.08, 0, 1) * np.exp(-np.clip(t - 0.15, 0, None) / 0.4)
    # the fire catching: a low whoomp and a short swell of roar
    w0 = 0.2
    whoomp_env = np.clip((t - w0) / 0.12, 0, 1) * np.exp(-np.clip(t - w0 - 0.12, 0, None) / 0.35)
    whoomp = high(norm(low(rng.standard_normal(n), 220, 4)), 45, 2) * whoomp_env
    fire = roar(rng, n, 400 + 900 * np.clip((t - w0) / 0.5, 0, 1)) * whoomp_env
    pops = crackles(rng, n, 60, np.clip((t - w0) / 0.2, 0, 1) * np.exp(-np.clip(t - w0, 0, None) / 0.8))
    x = 0.9 * thud + 0.5 * click + 0.8 * norm(crunch) + 0.35 * norm(ring) + 0.35 * hiss + 0.55 * whoomp + 0.5 * fire + 0.25 * norm(pops)
    x *= np.clip(t / 0.0015, 0, 1)
    x = np.tanh(1.4 * x / np.max(np.abs(x))) / np.tanh(1.4)
    fade = int(RATE * 0.2)
    x[-fade:] *= np.linspace(1, 0, fade)
    return x


def dome(rng, seconds=3.2):
    n = int(RATE * seconds)
    t = np.arange(n) / RATE
    # the roar rises (its band sweeps from 250 Hz up to 1.8 kHz over 1.3 s), then settles to a steady glow
    rise = np.clip(t / 1.3, 0, 1)
    centre = 250 + 2300 * (1 - (1 - rise) ** 2) - 900 * np.clip((t - 1.3) / 1.2, 0, 1)
    env = np.clip(t / 0.5, 0, 1) ** 1.5 * (1 - 0.55 * np.clip((t - 1.3) / 1.4, 0, 1))
    body = roar(rng, n, centre, 1.3) * env
    rumble = norm(low(rng.standard_normal(n), 120, 4)) * env
    # the warm hum of the dome: low partials beating slowly, coming in as the roar peaks
    hum_env = np.clip((t - 0.6) / 0.9, 0, 1) * np.clip((seconds - t) / 0.8, 0, 1)
    hum = np.zeros(n)
    for f, a in ((110.0, 1.0), (110.0 * 1.503, 0.45), (220.5, 0.5), (330.0, 0.25), (440.5, 0.12)):
        beat = f * 1.006
        hum += a * (np.sin(2 * np.pi * f * t) + 0.7 * np.sin(2 * np.pi * beat * t + 1.1))
    hum = norm(hum) * hum_env
    # a soft shimmer on top as the dome closes over (high, airy, quiet)
    shimmer = norm(band(rng.standard_normal(n), 5000, 12000)) * np.clip((t - 0.9) / 0.4, 0, 1) * np.exp(-np.clip(t - 1.3, 0, None) / 0.7)
    pops = crackles(rng, n, 90, np.clip((t - 0.15) / 0.4, 0, 1) * (1 - 0.6 * np.clip((t - 1.5) / 1.5, 0, 1)))
    rumble = high(rumble, 45, 2) * np.exp(-np.clip(t - 1.3, 0, None) / 0.8)   # weight at the peak, not under the glow
    hum = norm(np.tanh(1.5 * hum))                   # harmonics small speakers can play
    x = 1.0 * body + 0.2 * rumble + 0.15 * hum + 0.12 * shimmer + 0.45 * norm(pops)
    x = np.tanh(1.2 * x / np.max(np.abs(x))) / np.tanh(1.2)
    x *= np.clip(t / 0.02, 0, 1) * np.clip((seconds - t) / 0.6, 0, 1)
    return x


def fade(rng, seconds=2.6):
    n = int(RATE * seconds)
    t = np.arange(n) / RATE
    sink = np.clip(t / 2.0, 0, 1)
    centre = 1300 - 1050 * sink
    env = (1 - sink) ** 1.6
    body = roar(rng, n, centre, 0.7) * env * np.clip(t / 0.05, 0, 1)
    rumble = high(norm(low(rng.standard_normal(n), 100, 4)), 40, 2) * env * 0.35
    pops = crackles(rng, n, 80, (1 - sink) ** 2)
    # the last of it: ash settling, a thin sizzle that trails off
    sizzle = norm(band(rng.standard_normal(n), 4000, 10000)) * np.clip((t - 0.8) / 0.4, 0, 1) * np.exp(-np.clip(t - 1.2, 0, None) / 0.7)
    x = body + rumble + 0.5 * norm(pops) + 0.3 * sizzle
    x = norm(x)
    x *= np.clip(t / 0.12, 0, 1)
    x *= np.clip((seconds - t) / 0.3, 0, 1)
    return x


def pull(rng, seconds=0.9):
    n = int(RATE * seconds)
    t = np.arange(n) / RATE
    # grinding: a scrape whose pitch rises as the blade comes up
    scrape_c = 900 + 1800 * np.clip(t / 0.45, 0, 1)
    scrape_env = np.clip(t / 0.05, 0, 1) * np.exp(-np.clip(t - 0.3, 0, None) / 0.12)
    grain = 1 + 0.5 * np.tanh(4 * np.sin(2 * np.pi * 37 * t))      # judder of steel on stone, soft-edged
    scrape = norm(svf_band(rng.standard_normal(n), scrape_c, 1.6)) * scrape_env * grain
    stone = norm(band(rng.standard_normal(n), 250, 1200)) * scrape_env
    grit = np.zeros(n)
    for _ in range(40):
        i = int(rng.uniform(0, 0.4) * RATE)
        L = int(RATE * rng.uniform(0.002, 0.008))
        seg = band(rng.standard_normal(L + 128), 1500, 7000)[128:]
        grit[i:i + L] += norm(seg) * np.hanning(L) * rng.uniform(0.2, 0.8)
    ring = np.zeros(n)
    for f, a, d in ((1290, 0.4, 0.12), (2410, 0.3, 0.09), (3720, 0.2, 0.07)):
        ring += a * (np.sin(2 * np.pi * f * t) + 0.5 * np.sin(2 * np.pi * f * 1.004 * t)) * np.exp(-np.clip(t - 0.4, 0, None) / d) * (t >= 0.4)
    hiss = norm(band(rng.standard_normal(n), 3500, 11000)) * np.clip((t - 0.35) / 0.05, 0, 1) * np.exp(-np.clip(t - 0.4, 0, None) / 0.2)
    x = 0.8 * scrape + 0.3 * stone + 0.35 * norm(grit) + 0.22 * norm(ring) + 0.3 * hiss
    x = norm(x)
    x *= np.clip(t / 0.01, 0, 1) * np.clip((seconds - t) / 0.15, 0, 1)
    return x


def camp(rng, seconds=12.0):
    """The planted sword burning quietly, a loop: a soft flame flutter with lulls, crackles on top. The last half
    second is crossfaded (equal power) into the start so the loop has no seam. Saved quieter (peak 0.5)."""
    xf = 0.5
    n = int(RATE * (seconds + xf))
    t = np.arange(n) / RATE
    flutter = (1 + 0.4 * np.interp(t, np.linspace(0, t[-1], 25), rng.uniform(-1, 1, 25))) ** 2
    body = norm(band(rng.standard_normal(n), 150, 600, 2)) * flutter
    hiss = norm(band(rng.standard_normal(n), 2500, 8000)) * 0.05
    pops = crackles(rng, n, 12, np.ones(n))
    x = 0.3 * norm(body) + hiss + 0.8 * norm(pops)
    m = int(RATE * seconds)
    k = int(RATE * xf)
    head, tail = x[:k].copy(), x[m:m + k]
    ph = 0.5 * np.pi * np.linspace(0, 1, k)
    x = x[:m]
    x[:k] = head * np.sin(ph) + tail * np.cos(ph)
    return norm(x) * (0.5 / 0.9)

def blast(rng, seconds=2.5):
    """The camp catching: a deflagration. Air sucked in (0.25 s), a whoomp, a roaring burst of flame sweeping down
    from bright to dark, a low boom under it, then embers crackling as it settles."""
    n = int(RATE * seconds)
    t = np.arange(n) / RATE
    suck = norm(band(rng.standard_normal(n), 200, 2000)) * np.clip(t / 0.22, 0, 1) ** 3 * np.clip((0.27 - t) / 0.03, 0, 1)
    boom = high(norm(low(rng.standard_normal(n), 120, 4)), 50, 2) * np.exp(-np.clip(t - 0.25, 0, None) / 0.35) * np.clip((t - 0.25) / 0.005, 0, 1)
    centre = 3000 * np.exp(-np.clip(t - 0.25, 0, None) / 0.4) + 300
    roar_env = np.clip((t - 0.24) / 0.02, 0, 1) * np.exp(-np.clip(t - 0.31, 0, None) / 0.55)
    burst = roar(rng, n, centre, 0.7) * roar_env
    pops = crackles(rng, n, 120, np.clip((t - 0.31) / 0.1, 0, 1) * np.exp(-np.clip(t - 0.41, 0, None) / 0.8))
    x = 0.35 * suck + 0.8 * boom + 1.2 * burst + 0.35 * norm(pops)
    x *= np.clip(t / 0.01, 0, 1) * np.clip((seconds - t) / 0.4, 0, 1)
    x = np.tanh(1.5 * x / np.max(np.abs(x))) / np.tanh(1.5)
    return x

def save(name, x, peak=0.9):
    x = norm(x) * peak
    OUT.mkdir(exist_ok=True)
    with wave.open(str(OUT / name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes((x * 32767).astype("<i2").tobytes())
    print("wrote", OUT / name)


if __name__ == "__main__":
    for k in range(2):
        save("surtr_plant_%d.wav" % k, plant(np.random.default_rng(700 + k)))
        save("surtr_dome_%d.wav" % k, dome(np.random.default_rng(710 + k)))
        save("surtr_fade_%d.wav" % k, fade(np.random.default_rng(720 + k)))
        save("surtr_pull_%d.wav" % k, pull(np.random.default_rng(730 + k)))
        save("surtr_blast_%d.wav" % k, blast(np.random.default_rng(740 + k)))
    save("surtr_camp_0.wav", camp(np.random.default_rng(750)), peak=0.5)
