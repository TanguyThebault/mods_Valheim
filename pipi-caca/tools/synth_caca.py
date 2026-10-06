"""Synthesised sounds for the Caca mod (no recordings, no fal).

usage: uv run --with numpy python tools/synth_caca.py [prefixes...]   -> assets/sfx/*.wav (16-bit mono, 44.1 kHz)

- fart_*: a flapping valve. Each opening of the sphincter lets out a pulse of air: an asymmetric pulse (fast
  opening, slower closing) that excites broad resonances of the buttocks (~150-300 Hz, ~700 Hz, a weak ~1.8 kHz),
  plus leak turbulence that follows the pulses. The pulse rate (the pitch) follows the pressure, with jitter and
  shimmer; at the end the pressure drops and the valve sputters (irregular pulses and gaps). Variants: a classic
  one, a short squeaky "prrt", a long low one that turns wet (crackles), a double "brap-brap".
- plop: the poop landing on grass: a soft low thud and a short wet squish.
- splat_*: a thrown poop bursting: thump, squelch (a falling formant), then fragments pattering down.
- pee_ground: a seamless loop of a stream on soil and grass: a dense rain of tiny impacts plus hiss, slow wobble.
- pee_water: a seamless loop of a stream into water: many small bubbles (rising-pitch decaying tones), a splashy
  hiss and a deeper gurgle.
Checked with tools/sound_report.py (spectrograms + measures) by an evaluator agent; see MODLOG.md.
"""
from pathlib import Path
import wave

import numpy as np

SR = 44100
OUT = Path(__file__).resolve().parent.parent / "assets" / "sfx"


def save(name, x, peak=0.9):
    OUT.mkdir(parents=True, exist_ok=True)
    x = np.asarray(x, dtype=np.float64)
    x = x - np.mean(x)
    x = x / (np.abs(x).max() + 1e-9) * peak
    with wave.open(str(OUT / f"{name}.wav"), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes((x * 32767).astype("<i2").tobytes())
    print(f"{name:12s} {len(x) / SR:.2f}s")


# ---------------------------------------------------------------------------------------------------- filters
def biquad(x, b, a):
    y = np.zeros_like(x)
    x1 = x2 = y1 = y2 = 0.0
    b0, b1, b2 = b
    _, a1, a2 = a
    for i in range(len(x)):
        v = x[i]
        o = b0 * v + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2
        x2, x1, y2, y1 = x1, v, y1, o
        y[i] = o
    return y


def bandpass(x, f, q):
    w = 2 * np.pi * f / SR
    al = np.sin(w) / (2 * q)
    a0 = 1 + al
    return biquad(x, (al / a0, 0.0, -al / a0), (1.0, -2 * np.cos(w) / a0, (1 - al) / a0))


def lowpass(x, f, q=0.707):
    w = 2 * np.pi * f / SR
    al = np.sin(w) / (2 * q)
    c = np.cos(w)
    a0 = 1 + al
    return biquad(x, ((1 - c) / 2 / a0, (1 - c) / a0, (1 - c) / 2 / a0), (1.0, -2 * c / a0, (1 - al) / a0))


def highpass(x, f, q=0.707):
    w = 2 * np.pi * f / SR
    al = np.sin(w) / (2 * q)
    c = np.cos(w)
    a0 = 1 + al
    return biquad(x, ((1 + c) / 2 / a0, -(1 + c) / a0, (1 + c) / 2 / a0), (1.0, -2 * c / a0, (1 - al) / a0))


def smooth_noise(n, rate, rng):
    k = max(2, int(n / SR * rate) + 2)
    pts = rng.uniform(-1, 1, k)
    xs = np.linspace(0, k - 1, n)
    i = np.floor(xs).astype(int).clip(0, k - 2)
    f = xs - i
    f = f * f * (3 - 2 * f)
    return pts[i] * (1 - f) + pts[i + 1] * f


def fade(x, a=0.004, r=0.02):
    n = len(x)
    e = np.ones(n)
    na, nr = int(a * SR), int(r * SR)
    if na:
        e[:na] = np.linspace(0, 1, na)
    if nr:
        e[-nr:] *= np.linspace(1, 0, nr)
    return x * e


# ---------------------------------------------------------------------------------------------------- farts
def fart(seed, dur, f_lo, f_hi, shape="arch", wet=0.0, sputter=0.25,
         formants=((220, 1.6), (720, 3.0), (2500, 3.0)), breath=0.25, close=0.006, onset=0.08, drift=0.18,
         gains=(1.0, 0.6, 0.3), rise_from=0.7, end_drop=0.0, wet_grow=False, blorps=0, push=0.0, flutter=0.45,
         push_at=0.5, wobble=0.3):
    """Pulse-excited resonances. `shape` gives the pressure curve; the pulse rate goes from f_lo to f_hi with it."""
    rng = np.random.default_rng(seed)
    n = int(SR * dur)
    t = np.arange(n) / SR
    u = t / dur
    if shape == "arch":            # rises fast, holds, fades
        press = np.minimum(1, u / onset) * (1 - 0.35 * u) * np.clip((1 - u) / 0.18, 0, 1) ** 0.6
    elif shape == "rise":          # squeak: tension builds, pitch climbs
        press = np.minimum(1, u / onset) * (rise_from + (1 - rise_from) * u) * np.clip((1 - u) / 0.1, 0, 1) ** 0.5
    else:                          # "long": slow decay into a sputter
        press = np.minimum(1, u / onset) * np.exp(-1.3 * u) * np.clip((1 - u) / 0.12, 0, 1) ** 0.5
    press *= 1 + 0.3 * np.exp(-t / 0.04)               # the first push overshoots
    press *= 1 + wobble * smooth_noise(n, 9, rng)
    if push:                                           # a second push
        press *= 1 + push * np.exp(-((u - push_at) / 0.08) ** 2)
    press = np.clip(press, 0, None)
    pitch_drift = 1 - drift * u if shape != "rise" else 1 + 0 * u
    if end_drop:                                       # a final "pft": the pitch sags in the last 60 ms
        pitch_drift = pitch_drift * (1 - end_drop * np.clip((t - (dur - 0.06)) / 0.06, 0, 1))

    # pulse train with jitter, shimmer and stretches of period doubling; the end sputters in separate pops
    exc = np.zeros(n)
    leak = np.zeros(n)
    onsets = []
    pos = 0.0
    tens = smooth_noise(n, 3, rng)
    alt, alt_left = 0.0, 0
    gap_until = -1
    npulse = 0
    while True:
        i = int(pos)
        if i >= n:
            break
        p = press[i]
        late = max(0.0, (u[i] - (1 - sputter)) / max(sputter, 1e-3))
        if shape == "rise":                            # a squeak glides up with time, not pressure
            glide = 0.15 + 0.85 * u[i] ** 1.3
        else:
            glide = np.clip(p, 0, 1.3) ** 1.5
        f0 = (f_lo + (f_hi - f_lo) * glide) * pitch_drift[i] + (25 if shape == "rise" else 8) * tens[i]
        jitter = 0.07 + 0.3 * late + (0.06 * u[i] if wet_grow else 0.0)
        period = 1.0 / max(f0, 20) * (1 + rng.normal(0, jitter))
        if late > 0 and i > gap_until and rng.random() < 0.07 + 0.2 * late:
            gap_until = i + int(SR * rng.uniform(0.015, 0.08))  # the valve shuts for a moment
        if alt_left <= 0 and rng.random() < 0.02:
            alt, alt_left = rng.uniform(0.15, 0.3), int(rng.integers(6, 20))
        alt_left -= 1
        if i > gap_until and (p > 0.02 or late > 0):
            amp = max(p, 0.35 * late) * (1 + rng.normal(0, 0.12 + 0.2 * late))
            if alt_left > 0:
                amp *= 1 + (alt if npulse & 1 else -alt)
            L = int(SR * min(period, 0.012))
            k = np.arange(L) / SR
            open_t = 0.0008
            close_t = min(period * 0.55, close)
            pulse = np.where(k < open_t, k / open_t, np.exp(-(k - open_t) / (close_t * 0.4)))
            end = min(n, i + L)
            exc[i:end] += amp * pulse[: end - i]
            leak[i:end] += amp * np.exp(-k[: end - i] / 0.004)
            onsets.append((i, amp))
            npulse += 1
        pos += max(period, 1.0 / 500) * SR
    exc = lowpass(np.diff(exc, prepend=0), 3500)       # the sound is the flow's derivative
    y = np.zeros(n)
    pmix = np.clip(press, 0, 1)
    for j, ((f, q), g) in enumerate(zip(formants, gains)):
        ff = f * (1 + 0.04 * rng.normal())
        if j == 0:                                     # a wider opening sounds brighter: F1 follows the pressure
            y += g * (bandpass(exc, ff * 0.8, q) * (1 - pmix) + bandpass(exc, ff * 1.2, q) * pmix)
        else:
            y += g * bandpass(exc, ff, q)
    # flap roughness: short stretches of fast flutter
    stretch = np.clip((smooth_noise(n, 4, rng) + 0.1) * 2, 0, 1)
    flut = 18 + 10 * (smooth_noise(n, 2, rng) * 0.5 + 0.5)
    y *= 1 - flutter * stretch * (0.5 + 0.5 * np.sin(2 * np.pi * np.cumsum(flut) / SR))
    y /= np.abs(y).max() + 1e-9
    # turbulence of the air leaking through, gated by the pulses
    gate = lowpass(leak, 80)
    gate /= gate.max() + 1e-9
    noise = bandpass(rng.normal(0, 1, n), 1500, 0.7) * gate
    y += noise / (np.abs(noise).max() + 1e-9) * breath
    if wet > 0:                                        # wet: broadband splutter fired on the pulse openings
        cr = np.zeros(n)
        rate = wet * 150 * dur
        if onsets:
            w = np.array([(o[0] / n) ** 1.5 + 0.1 if wet_grow else 1.0 for o in onsets])
            chosen = rng.choice(len(onsets), size=min(len(onsets), int(rate)), replace=False, p=w / w.sum())
        else:
            chosen = []
        for j in chosen:
            c, a = onsets[j]
            L = int(SR * rng.uniform(0.002, 0.006))
            if c + L < n:
                cr[c:c + L] += rng.normal(0, 1, L) * np.hanning(L) * a * rng.uniform(0.3, 1.0)
        cr = lowpass(highpass(cr, 1000), 6000)
        y += cr / (np.abs(cr).max() + 1e-9) * wet * 0.45
    for _ in range(blorps):                            # wet "blorp" pops riding the pulses
        c = int(rng.uniform(0.45, 0.8) * n)
        L = int(SR * rng.uniform(0.015, 0.025))
        if c + L < n:
            k = np.arange(L) / SR
            w = int(0.01 * SR)
            local = np.sqrt((y[max(0, c - w):c + w] ** 2).mean())
            burst = bandpass(rng.normal(0, 1, L), rng.uniform(150, 400), 5.0) * np.exp(-k / (L / SR * 0.35))
            y[c:c + L] += burst / (np.abs(burst).max() + 1e-9) * local * rng.uniform(0.3, 0.45) * 4.5
    y = np.tanh(y / (np.abs(y).max() + 1e-9) * 0.7)   # a little slap, the dynamics stay
    y = lowpass(y, 5000)
    y = highpass(y, 35)
    return fade(y, 0.0015, 0.03)


def plop(seed, thud_hz=120, sweep=(600, 1400)):
    rng = np.random.default_rng(seed)
    dur = 0.2
    n = int(SR * dur)
    t = np.arange(n) / SR
    f = thud_hz + 60 * np.exp(-t * 60)
    thud = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t * 70) * 0.7
    body = bandpass(rng.normal(0, 1, n), 250, 1.0) * np.exp(-t * 40) * 0.8
    # a sticky squish: a resonance sweeping 600 -> 1400 Hz over 40 ms, rippling
    sq = np.zeros(n)
    noise = rng.normal(0, 1, n)
    seg = int(0.005 * SR)
    for s0 in range(0, int(0.045 * SR), seg):
        fc = sweep[0] + (sweep[1] - sweep[0]) * min(1.0, s0 / SR / 0.04)
        part = bandpass(noise[s0:s0 + seg * 3], fc, 3.0)
        e = min(n, s0 + len(part))
        sq[s0:e] += part[: e - s0] * np.hanning(len(part))[: e - s0]
    sq *= (1 + 0.6 * np.sin(2 * np.pi * 70 * t)) * np.exp(-t * 30) * 0.6
    d = int(rng.uniform(0.008, 0.012) * SR)            # the sticky release comes just after the hit
    sq = np.concatenate([np.zeros(d), sq[:-d]])
    grass = highpass(rng.normal(0, 1, n), 3000) * np.exp(-t * 35) * 0.2
    clicks = np.zeros(n)
    for _ in range(7):
        c = int(rng.uniform(0.0, 0.05) * SR)
        L = int(SR * 0.0015)
        clicks[c:c + L] += rng.normal(0, 1, L) * rng.uniform(0.2, 0.6)
    clicks = bandpass(clicks, 6000, 1.0)
    hit = thud + body * 2.5 + sq + grass * 0.15 + clicks * 0.05
    d2 = int(rng.uniform(0.025, 0.04) * SR)          # it settles: a second, smaller flop (its own sound)
    f2 = thud_hz * 0.85 + 40 * np.exp(-t * 60)
    flop2 = np.sin(2 * np.pi * np.cumsum(f2) / SR) * np.exp(-t * 80) * 0.7 \
        + bandpass(rng.normal(0, 1, n), 230, 1.0) * np.exp(-t * 45) * 2.0
    flop = np.concatenate([np.zeros(d2), flop2[:-d2]]) * 0.32
    tack = np.zeros(n)                                 # a faint sticky tack as it lets go
    c = int(rng.uniform(0.06, 0.09) * SR)
    Lt = int(0.005 * SR)
    tack[c:c + Lt] = bandpass(rng.normal(0, 1, Lt), 1200, 2.0) * np.hanning(Lt)
    tack *= np.abs(hit).max() * 0.1 / (np.abs(tack).max() + 1e-9)
    return fade(lowpass(hit + flop + tack, 4000), 0.0005, 0.04)


def splat(seed, thump_hz=90, sweep_from=1500, frags=18):
    rng = np.random.default_rng(seed)
    dur = 0.36
    n = int(SR * dur)
    t = np.arange(n) / SR
    slap = np.zeros(n)
    L = int(SR * rng.uniform(0.003, 0.008))
    slap[:L] = highpass(rng.normal(0, 1, L), 1500) * np.hanning(L * 2)[L:] * 0.6
    thump = np.sin(2 * np.pi * np.cumsum(thump_hz + 60 * np.exp(-t * 50)) / SR) * np.exp(-t * 60) * 1.0
    # squelch: noise through a resonance that falls toward ~400 Hz, bubbling (AM 20-40 Hz)
    sq = np.zeros(n)
    noise = rng.normal(0, 1, n)
    seg = int(0.01 * SR)
    for s0 in range(0, int(0.30 * SR), seg):
        fc = sweep_from * np.exp(-s0 / SR / 0.07) + 380
        part = bandpass(noise[s0:s0 + seg * 3], fc, 4.0)
        e = min(n, s0 + len(part))
        sq[s0:e] += part[: e - s0] * np.hanning(len(part))[: e - s0]
    am = 1 + 0.6 * np.sin(2 * np.pi * np.cumsum(20 + 20 * smooth_noise(n, 10, rng) ** 2) / SR)
    sq *= np.exp(-t * 14) * 1.4 * am
    # fragments landing around: early ones loud, then fewer and softer
    frag = np.zeros(n)
    for _ in range(frags):
        tt = 0.05 + rng.exponential(0.08)
        c = int(tt * SR)
        if c >= n - 2000:
            continue
        a = rng.uniform(0.4, 1.0) * np.exp(-(tt - 0.06) / 0.25)
        Lp = int(SR * rng.uniform(0.008, 0.02))
        pat = lowpass(rng.normal(0, 1, Lp), 700 if tt <= 0.2 else 400) * np.hanning(Lp) * 1.5
        Lt = int(SR * rng.uniform(0.002, 0.004))
        tick = bandpass(rng.normal(0, 1, Lt), rng.uniform(1500, 4000), 1.5) * np.hanning(Lt)
        frag[c:c + Lp] += pat * a
        frag[c:c + Lt] += tick * a * 0.8 * np.exp(-(tt - 0.06) / 0.08)   # late ones land softer
    for _ in range(int(rng.integers(2, 4))):         # the last pieces settle, dull and soft
        c = int(rng.uniform(0.22, 0.30) * SR)
        Lp = int(SR * rng.uniform(0.008, 0.02))
        frag[c:c + Lp] += lowpass(rng.normal(0, 1, Lp), 400) * np.hanning(Lp) * 1.5 * rng.uniform(0.12, 0.2)
    spray = np.zeros(n)
    for _ in range(int(rng.integers(25, 36))):       # micro-droplets flying off
        c = int(rng.uniform(0.01, 0.12) * SR)
        Ls = int(SR * rng.uniform(0.001, 0.003))
        spray[c:c + Ls] += bandpass(rng.normal(0, 1, Ls), rng.uniform(1000, 4000), 1.2) * np.hanning(Ls) * rng.uniform(0.1, 0.35)
    ooze = lowpass(rng.normal(0, 1, n), 1000) * np.exp(-t / 0.3) * 0.04
    return fade(highpass(slap + thump + sq + frag + spray + ooze, 60), 0.0003, 0.06)


# ---------------------------------------------------------------------------------------------------- pee loops
def seamless(x, xf=0.4):
    """The last `xf` seconds crossfaded into the start (equal power); returns len - xf seconds."""
    m = int(xf * SR)
    head, body, tail = x[:m], x[m:-m], x[-m:]
    k = np.linspace(0, 1, m)
    mix = tail * np.cos(k * np.pi / 2) + head * np.sin(k * np.pi / 2)
    return np.concatenate([mix, body])


def soft_limit(y):
    r = np.sqrt((y ** 2).mean()) + 1e-9
    return np.tanh(y / (6 * r)) * 6 * r


def pee_ground(seed, dur=6.0):
    rng = np.random.default_rng(seed)
    total = dur + 0.4
    n = int(SR * total)
    t = np.arange(n) / SR
    wob = (1 + 0.35 * smooth_noise(n, 1.5, rng) + 0.1 * np.sin(2 * np.pi * 0.7 * t)) \
        * (1 + 0.15 * smooth_noise(n, 6, rng))
    # drops hitting soil and grass: noisy ticks and short wet tones, a few loud ones among many soft
    grains = np.zeros(n)
    tones = np.zeros(n)
    for _ in range(int(total * 250)):
        c = int(rng.uniform(0, 1) * n)
        L = int(SR * rng.uniform(0.003, 0.01))
        if c + L >= n:
            continue
        a = min(0.3, 0.08 * rng.pareto(2.2) + 0.05)
        ramp = np.minimum(1, np.arange(L) / (0.0005 * SR))
        k = np.arange(L) / SR
        if rng.random() < 0.4:
            f = rng.uniform(800, 3000)
            tones[c:c + L] += np.sin(2 * np.pi * f * k) * np.exp(-k / (L / SR * 0.3)) * a * ramp
        else:
            grains[c:c + L] += rng.normal(0, 1, L) * np.exp(-k / (L / SR * 0.3)) * a * ramp
    grains = bandpass(grains, 1800, 0.8) + 0.3 * bandpass(grains, 3000, 1.0)
    fizz = np.zeros(n)                                 # foam soaking into the soil
    for _ in range(int(total * 400)):
        c = int(rng.uniform(0, 1) * (n - 50))
        Lf = int(SR * rng.uniform(0.0005, 0.001))
        fizz[c:c + Lf] += rng.normal(0, 1, Lf) * rng.uniform(0.2, 1.0)
    fizz = bandpass(fizz, 3500, 1.0)
    hiss = bandpass(rng.normal(0, 1, n), 2500, 0.7) * 0.15
    patter = bandpass(rng.normal(0, 1, n), 300, 0.9) * 0.6 * (1 + 0.5 * smooth_noise(n, 8, rng))
    y = (grains * 3 + tones * 1.5 + hiss + patter * 0.15) * wob
    y += fizz / (np.abs(fizz).max() + 1e-9) * np.abs(y).max() * 0.08
    y = highpass(y, 120)
    soil = smooth_noise(n, 0.3, rng) * 0.5 + 0.5      # drifting between darker soil and brighter grass
    y = lowpass(y, 3000) * soil + y * (1 - soil)
    y = lowpass(lowpass(soft_limit(y), 6000), 6000)   # limit first: tanh would add highs back
    return seamless(y)


def pee_water(seed, dur=6.0):
    rng = np.random.default_rng(seed)
    total = dur + 0.4
    n = int(SR * total)
    t = np.arange(n) / SR
    wob = 1 + 0.2 * smooth_noise(n, 1.2, rng) + 0.08 * np.sin(2 * np.pi * 0.9 * t)
    bubbles = np.zeros(n)
    placed = 0
    while placed < int(total * 230):
        c = int(rng.uniform(0, 1) * n)
        if rng.random() * 1.4 > wob[c]:                    # denser when the stream surges
            continue
        placed += 1
        if rng.random() < 0.15:
            f0 = float(rng.uniform(2000, 4000))                    # bright plinks
            plink = 1.5
        else:
            f0 = float(400 * (2500 / 400) ** rng.random())
            plink = 0.7 if f0 < 600 else 1.0
        decay = 0.13 * f0 + 0.0072 * f0 ** 1.5                     # small bubbles ring short (van den Doel)
        L = min(int(SR * 6 / decay), int(0.06 * SR))
        if c + L >= n or L < 8:
            continue
        k = np.arange(L) / SR
        f = f0 * (1 + 0.1 * decay * k)                             # pitch rises as the bubble nears the surface
        a = min(0.5, 0.1 * rng.pareto(2.0) + 0.04) * plink
        bubbles[c:c + L] += np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-k * decay) * a
    pops = np.zeros(n)                                 # tiny bubble pops fizzing at the surface
    for _ in range(int(total * 1500)):
        c = int(rng.uniform(0, 1) * (n - 200))
        Lp = int(SR * rng.uniform(0.001, 0.003))
        pops[c:c + Lp] += rng.normal(0, 1, Lp) * np.hanning(Lp) * min(0.5, 0.1 * rng.pareto(2.0) + 0.03)
    pops = highpass(lowpass(pops, 8000), 3000)
    splash = pops / (np.sqrt((pops ** 2).mean()) + 1e-9) * np.sqrt((bandpass(rng.normal(0, 1, n), 3000, 0.7) ** 2).mean()) * 0.12
    # the plunging jet drums the water: low impulses through a 150-400 Hz body
    imp = np.zeros(n)
    for _ in range(int(total * 120)):
        imp[int(rng.uniform(0, 1) * (n - 1))] += rng.uniform(0.2, 1.0)
    rumble = bandpass(lowpass(imp, 600), 250, 0.8) * 2.0 * wob
    y = (bubbles * 1.2 + splash + rumble * 0.25) * wob
    y = lowpass(soft_limit(highpass(y, 90)), 8000)
    return seamless(y)


if __name__ == "__main__":
    import sys
    only = set(sys.argv[1:])

    def want(name):
        return not only or any(name.startswith(o) for o in only)

    if want("fart"):
        save("fart_1", fart(1, 1.0, 70, 135, "arch", sputter=0.3, push=0.35, breath=0.35))
        save("fart_2", fart(2, 0.5, 200, 450, "rise", sputter=0.25, close=0.0025, drift=0, end_drop=0.15,
                            formants=((420, 3.5), (1100, 3.5), (2600, 5.0), (200, 1.5)),
                            gains=(1.0, 0.7, 0.3, 0.4), breath=0.3))
        save("fart_3", fart(3, 1.7, 45, 90, "long", wet=1.0, sputter=0.4, wet_grow=True, blorps=4, push=0.3, push_at=0.6, wobble=0.45,
                            formants=((170, 1.4), (600, 2.5), (1800, 3.0))))
        a = fart(4, 0.2, 95, 150, "arch", sputter=0.05, onset=0.01, formants=((260, 1.8), (850, 3.0), (2400, 3.0)))
        b = fart(5, 0.3, 65, 110, "arch", sputter=0.45, end_drop=0.1, onset=0.01, formants=((190, 1.5), (650, 2.6), (2000, 3.0)))
        k = np.arange(int(0.07 * SR)) / SR                 # the tail of the first brap, then silence
        gap = bandpass(np.random.default_rng(6).normal(0, 1, len(k)), 1200, 0.7) * 0.03 * np.exp(-k / 0.02)
        save("fart_4", np.concatenate([a, gap, b]))
    if want("plop"):
        save("plop_1", plop(9, 120, (600, 1400)))
        save("plop_2", plop(10, 105, (500, 1200)))
        save("plop_3", plop(13, 140, (700, 1600)))
    if want("splat"):
        save("splat_1", splat(11, 90, 1200, 18))
        save("splat_2", splat(12, 70, 1900, 20))
    if want("pee"):
        save("pee_ground", pee_ground(21), 0.8)
        save("pee_water", pee_water(22), 0.8)
