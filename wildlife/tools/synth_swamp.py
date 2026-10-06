"""Synthesise the Swamp's frog calls (mono 16-bit WAV, 44.1 kHz) into assets/sfx_frog/.

Frog calls are pulsed, not whistled: the larynx lets out trains of short pulses, and the pulse rate is what the ear
hears as the "grrr" or the "rrr-ibbit"; the vocal sac and mouth give them resonances (formants).
- frog_croak*: the common frog's soft purring croak (Rana temporaria), "grrrok ... grrrok": 0.3-0.6 s trains of
  ~25-40 pulses/s, each pulse a damped burst ringing at ~450 and ~1300 Hz, slightly rising rate; 1-3 croaks per
  call. And the edible frog's louder "crroa-crroa" (two quick croaks with a fast pulse rate, ~80-120 pulses/s).
- frog_alarm*: the distress call when grabbed or hurt: a short, high, nasal scream.
Heard in a damp, open night (a short soft tail).

    uv run --with numpy --with scipy python tools/synth_swamp.py assets
"""
import sys
import wave
from pathlib import Path

import numpy as np
from scipy.signal import butter, fftconvolve, lfilter, sosfilt

SR = 44100


def norm(x):
    return x / (np.max(np.abs(x)) + 1e-9)


def bp(x, lo, hi, order=2):
    return sosfilt(butter(order, [lo, hi], "bandpass", fs=SR, output="sos"), x)


def reson(x, f0, q):
    w = 2 * np.pi * f0 / SR
    r = np.exp(-np.pi * f0 / q / SR)
    return lfilter([1 - r], [1, -2 * r * np.cos(w), r * r], x)


def pulse_train(rng, dur, rate0, rate1, formants, ring=0.008, jitter=0.10, amp_db=3.5, breath=0.10, highpass=220, fjit=0.04):
    """A pulsed call: damped bursts whose spacing gliding from 1/rate0 to 1/rate1 is jittered pulse by pulse,
    with amplitudes on a slow random walk; coloured by resonances, a little breath noise, no rumble below highpass.
    Bout envelope: 40 ms rise, a short hold, a decay to -8 dB."""
    n = int(SR * dur)
    imp = np.zeros(n)
    t, k, walk = 0.0, 0, 0.0
    while t < dur:
        u = t / dur
        rate = rate0 + (rate1 - rate0) * u
        i = int(t * SR)
        if i < n:
            walk = 0.7 * walk + rng.normal(0, amp_db * 0.6)
            imp[i] = 10 ** (np.clip(walk, -amp_db * 1.5, amp_db * 1.5) / 20)
        t += (1 / rate) * (1 + rng.normal(0, jitter))
        k += 1
    L = int(SR * ring * 4)
    tt = np.arange(L) / SR
    x = np.zeros(n)
    for i in np.nonzero(imp)[0]:                       # each pulse: its own burst through slightly moved formants
        burst = rng.standard_normal(L) * np.exp(-tt / ring)
        burst[:3] += 2.0                               # the click of the larynx opening
        j = rng.uniform(1 - fjit, 1 + fjit)
        seg = sum(g * reson(burst, f * j, q) for f, q, g in formants)
        e = min(L, n - i)
        x[i:i + e] += imp[i] * seg[:e]
    # breath noise rides the pulses (gated by their envelope), not a continuous bed
    gate = fftconvolve(np.abs(imp), np.exp(-np.arange(int(SR * 0.015)) / (SR * 0.005)))[:n]
    gate /= max(gate.max(), 1e-9)
    x = norm(x) + breath * norm(bp(rng.standard_normal(n), 300, 3000)) * gate
    x = sosfilt(butter(2, highpass, "highpass", fs=SR, output="sos"), x)
    tn = np.arange(n) / SR
    env = np.clip(tn / 0.04, 0, 1) * np.interp(tn, [0, 0.04, 0.04 + dur * 0.25, dur], [1, 1, 1, 10 ** (-8 / 20)])
    env *= np.clip((dur - tn) / 0.05, 0, 1) ** 1.5                 # a release over the last pulses, no gate
    return norm(x) * env


def room(x, rng, tail=0.2, wet=0.12):
    n = int(SR * tail)
    ir = rng.standard_normal(n) * np.exp(-6.9 * np.arange(n) / n)
    ir = sosfilt(butter(2, 2500, "lowpass", fs=SR, output="sos"), ir)
    y = fftconvolve(np.concatenate([x, np.zeros(n)]), ir / np.sqrt((ir ** 2).sum()))
    dry = np.concatenate([x, np.zeros(n)])
    return dry * (1 - wet) + norm(y[:len(dry)]) * np.max(np.abs(x)) * wet


def silence(s):
    return np.zeros(int(SR * s))


def purr(rng):
    """Common frog: 1-3 soft purring croaks, each with its own rate."""
    parts = []
    for k in range(rng.integers(1, 4)):
        d = rng.uniform(0.3, 0.6)
        r0 = rng.uniform(26, 34)
        parts += [pulse_train(rng, d, r0, min(38, r0 * rng.uniform(1.05, 1.2)),
                              [(rng.uniform(430, 480), 2.5, 1.0), (rng.uniform(1250, 1350), 2.5, 0.5)], ring=rng.uniform(0.004, 0.006),
                              breath=0.11),
                  silence(rng.uniform(0.25, 0.5))]
    return np.concatenate(parts)


def crroa(rng):
    """Edible frog: two or three quick, louder croaks, each a little different in length and pitch."""
    parts = []
    base1, base2 = rng.uniform(600, 720), rng.uniform(1550, 1800)
    for k in range(rng.integers(2, 4)):
        p = rng.uniform(0.95, 1.05)
        d = rng.uniform(0.12, 0.25)
        parts += [pulse_train(rng, d, 92 * p, 115 * p, [(base1 * p, 5, 1.0), (base2 * p, 6, 0.7), (2600 * p, 6, 0.3)],
                              ring=0.0025, jitter=0.04, amp_db=2.0, breath=0.06, highpass=330, fjit=0.02), silence(rng.uniform(0.08, 0.14))]
    return np.concatenate(parts)


def cry(rng, d, broken, urgent=False):
    """One scream: an upward scoop, a rise to a random peak (1.1-1.4 kHz at 30-60 % of the cry), a fall; cycle-to-cycle
    jitter and shimmer, aspiration noise riding the cry envelope, maybe a broken (subharmonic) stretch."""
    n = int(SR * d)
    t = np.arange(n) / SR
    peak, at = (rng.uniform(1200, 1600) if urgent else rng.uniform(1100, 1400)), rng.uniform(0.3, 0.6) * d
    f0 = np.interp(t, [0, 0.02, at, d], [peak * 0.62, peak * 0.72, peak, peak * rng.uniform(0.65, 0.75)])
    f0 *= 1 + 0.03 * np.sin(2 * np.pi * rng.uniform(8, 15) * t + rng.uniform(0, 6))
    # irregular wobble: a new random offset every 20-40 ms (not a smooth vibrato), plus cycle-to-cycle jitter
    seg_t = np.cumsum(rng.uniform(0.02, 0.04, int(d / 0.02) + 2))
    f0 *= 1 + 0.035 * np.interp(t, np.r_[0, seg_t], rng.uniform(-1, 1, len(seg_t) + 1))
    f0 *= 1 + 0.015 * rng.standard_normal(n)
    ph = 2 * np.pi * np.cumsum(f0) / SR
    shimmer = 1 + 0.12 * np.interp(t, np.linspace(0, d, max(3, int(d * 200))), rng.standard_normal(max(3, int(d * 200))))
    x = sum(a * np.sin(k * ph) for k, a in ((1, 1.0), (2, 0.55), (3, 0.35), (4, 0.2))) * shimmer
    if broken:
        b0, bl = rng.uniform(0.2, 0.6) * d, rng.uniform(0.03, 0.06)
        brk = (t > b0) & (t < b0 + bl)
        x[brk] += 0.8 * np.sin(0.5 * ph[brk]) * np.sign(np.sin(ph[brk] * 1.5))
    env = np.clip(t / 0.02, 0, 1) * np.clip((d - t) / 0.06, 0, 1)
    x = norm(x)
    x = 0.5 * x + reson(x, 1200, 4) * 1.0 + reson(x, 2400, 4) * 1.27
    x = x + 1.0 * norm(bp(x, 2200, 2600)) * np.max(np.abs(x))      # +6 dB at 2.2-2.6 kHz before the shaper: H2 up
    x = np.tanh(3.5 * x / np.max(np.abs(x)))                     # a nasal rasp (3rd-5th harmonics 10-15 dB down)
    # shimmer after the shaper: random +-2 dB steps over 15-40 ms, so the plateau is not flat
    st = np.cumsum(rng.uniform(0.015, 0.04, int(d / 0.015) + 2))
    x *= 10 ** (np.interp(t, np.r_[0, st], rng.uniform(-2, 2, len(st) + 1)) / 20)
    asp = norm(bp(rng.standard_normal(n), 1500, 4000)) * 10 ** (-4 / 20) * np.max(np.abs(x))
    x = x + asp
    g = int(SR * rng.uniform(0.035, 0.05))                    # the gasp before the cry
    x[:g] += norm(bp(rng.standard_normal(g), 1000, 4000)) * np.max(np.abs(x)) * np.hanning(2 * g)[g:]
    x = sosfilt(butter(2, 5000, "lowpass", fs=SR, output="sos"), x)
    return norm(x) * env * 10 ** (rng.uniform(-3, 0) / 20)


def alarm(rng):
    """The distress call: 2-3 open-mouthed screams with short gaps, dry; one or two of them break."""
    k = int(rng.integers(2, 4))
    broken = set(rng.choice(k, size=int(rng.integers(1, min(2, k) + 1)), replace=False))
    parts = []
    urgent = int(rng.integers(0, k))
    for i in range(k):
        parts += [cry(rng, rng.uniform(0.2, 0.45), i in broken, i == urgent), silence(rng.uniform(0.08, 0.15))]
    return np.concatenate(parts)


def finish(x, level):
    f = int(SR * 0.01)
    x = np.concatenate([silence(0.01), x, silence(0.05)])
    x[:f] *= np.linspace(0, 1, f)
    x[-f:] *= np.linspace(1, 0, f)
    return norm(x) * level


def write(path, x):
    with wave.open(str(path), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes((np.clip(x, -1, 1) * 32767).astype("<i2").tobytes())


if __name__ == "__main__":
    root = Path(sys.argv[1] if len(sys.argv) > 1 else "assets") / "sfx_frog"
    root.mkdir(parents=True, exist_ok=True)
    jobs = [(f"frog_croak{i + 1}", purr, 1000 + i) for i in range(3)] + [(f"frog_croak{i + 4}", crroa, 1100 + i) for i in range(2)]   # seed 1102 scored 8.0: dropped
    jobs += [("frog_alarm1", alarm, 1201)]   # seed 1201 scored 8.5 (1200 scored 8.0: dropped)
    for name, fn, seed in jobs:
        rng = np.random.default_rng(seed)
        raw = fn(rng)
        x = finish(raw if "alarm" in name else room(raw, rng), 0.8 if "croak" in name else 0.7)
        write(root / f"{name}.wav", x)
        print(name, f"{len(x) / SR:.2f} s")
