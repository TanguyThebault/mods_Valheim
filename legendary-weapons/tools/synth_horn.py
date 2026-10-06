"""Synthesised sounds for the Fog Horn (no recordings, no fal) -> sfx/*.wav (16-bit mono, 44.1 kHz).

usage: uv run --with numpy python tools/synth_horn.py [prefixes...]
- horn_call_*: the summoning call. A large animal horn is a lip-buzzed tube: a pulse-like source whose brightness
  grows with the blowing pressure (more harmonics when louder, the "brassy" edge), shaped by the bore and bell
  (broad resonances), with a slightly flat start that rises into tune, a little wavering, breath noise, and the
  call echoing off the hills (a few distant reflections and a long diffuse tail). One long low note that swells,
  steps up to the next natural tone and falls off.
- horn_short_*: a plain blow, one shorter note.
- ghost_rise_*: spirits rising from the mist: an airy rising whoosh, a cold shimmering chord that blooms, a low swell.
- ghost_fade_*: spirits dissolving: the shimmer falls and thins out into breath.
"""
from pathlib import Path
import sys
import wave

import numpy as np

SR = 44100
OUT = Path(__file__).resolve().parent.parent / "sfx"


def trim(x, floor_db=-60.0, fade=0.05):
    """Cut the silent padding after the last sample above floor_db (re the peak), with a short fade."""
    x = np.asarray(x, dtype=np.float64)
    thr = np.abs(x).max() * 10 ** (floor_db / 20)
    idx = np.nonzero(np.abs(x) > thr)[0]
    end = min(len(x), (idx[-1] if len(idx) else len(x) - 1) + int(fade * SR))
    x = x[:end].copy()
    n = int(fade * SR)
    x[-n:] *= np.linspace(1, 0, n)
    return x


def save_raw(name, x):
    """Written as is (shared gain set by the caller), trimmed, clipped for safety."""
    OUT.mkdir(parents=True, exist_ok=True)
    x = np.clip(trim(x), -1, 1)
    with wave.open(str(OUT / f"{name}.wav"), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes((x * 32767).astype("<i2").tobytes())
    print(f"{name:14s} {len(x) / SR:.2f}s")


def save(name, x, peak=0.9):
    OUT.mkdir(parents=True, exist_ok=True)
    x = trim(x)
    x = x - np.mean(x)
    x = x / (np.abs(x).max() + 1e-9) * peak
    with wave.open(str(OUT / f"{name}.wav"), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes((x * 32767).astype("<i2").tobytes())
    print(f"{name:14s} {len(x) / SR:.2f}s")


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


def onepole(x, f):
    a = np.exp(-2 * np.pi * f / SR)
    y = np.zeros_like(x)
    acc = 0.0
    for i in range(len(x)):
        acc = (1 - a) * x[i] + a * acc
        y[i] = acc
    return y


def follower(x, att=0.02, rel=0.35):
    """Envelope follower, fast attack, slow release (seconds)."""
    a_att, a_rel = np.exp(-1 / (att * SR)), np.exp(-1 / (rel * SR))
    y = np.zeros_like(x)
    acc = 0.0
    for i, v in enumerate(np.abs(x)):
        a = a_att if v > acc else a_rel
        acc = a * acc + (1 - a) * v
        y[i] = acc
    return y


def svf_sweep(x, fc, q):
    """Band-pass with a cutoff that moves per sample (state-variable filter, cascaded twice): no block restarts."""
    out = x
    for _ in range(2):
        low = band = 0.0
        y = np.zeros_like(out)
        for i in range(len(out)):
            F = 2 * np.sin(np.pi * min(fc[i], SR / 6) / SR)
            high = out[i] - low - band / q
            band += F * high
            low += F * band
            y[i] = band
        out = y
    return out


def echo_tail(x, rng, taps=((0.45, 0.16), (0.95, 0.11), (1.6, 0.07)), tail=2.2, wet=0.18, bright=False, shimmer=0.0,
              decay=6.0, duck=0.6, tap_fade=0.0):
    """Outdoor call: hill echoes (late, soft, smeared so they don't beat with the note) and a diffuse tail; the wet part
    is ducked under the dry note, so the echoes bloom after each tone."""
    n = len(x) + int(SR * (max(t for t, _ in taps) + tail + 0.2))
    y = np.zeros(n)
    cut = (1800, 1200, 800, 600)
    xt = x
    if tap_fade > 0:                                  # echoes of a note already sounding swell in
        m = int(tap_fade * SR)
        xt = x.copy()
        xt[:m] *= 0.5 - 0.5 * np.cos(np.pi * np.arange(m) / m)
    for k, (t, g) in enumerate(taps):
        x_ = xt
        src = x_ if bright else highpass(onepole(x_, cut[min(k, 3)]), (90, 110, 140, 170)[min(k, 3)])   # far echoes are thin
        for off in (-0.008, 0.0, 0.008):
            d = int((t + off) * SR)
            y[d:d + len(x)] += src * g / 3
    L = int(tail * SR)
    ir = rng.normal(0, 1, L) * np.exp(-np.arange(L) / SR / (tail / decay))
    ir = lowpass(ir, 7000 if bright else 3000)
    if not bright:
        ir = highpass(ir, 80)
    ir /= np.sqrt((ir ** 2).sum()) + 1e-9
    src = x                                           # (no octave shimmer: a 2x resample is a sped-up copy)
    m = len(src) + L - 1
    nfft = 1 << (m - 1).bit_length()
    diffuse = np.fft.irfft(np.fft.rfft(src, nfft) * np.fft.rfft(ir, nfft), nfft)[:m]
    pre = int(0.06 * SR)
    end = min(n, pre + m)
    y[pre:end] += diffuse[:end - pre] * wet
    e = follower(x)
    e /= e.max() + 1e-9
    e = np.concatenate([e, np.zeros(n - len(e))])
    y *= 1 - duck * e                                  # y holds only the wet part here
    y[:len(x)] += x
    return y


def limit(x):
    """Loudness without clipping the waves: a gain envelope (fast attack, slow release) pulls the loud parts down to
    half the peak, then the whole is normalised again."""
    x = x / (np.abs(x).max() + 1e-9)
    g = np.minimum(1.0, 0.5 / (follower(x, 0.005, 0.25) + 1e-9))
    y = x * g
    return y / (np.abs(y).max() + 1e-9)


def horn_note(rng, f0, dur, swell=0.6, scoop=0.11, bright=1.0, release=0.35, droop=0.04, glide_to=None, glide_at=None, rel_exp=1.5, attack_on=True, swell_time=None, breathing=False):
    """One lip-buzzed note: brighter (steeper waves) when louder, through the bore and bell resonances."""
    n = int(SR * dur)
    t = np.arange(n) / SR
    u = t / dur
    attack = 1 - np.exp(-t / 0.05) if attack_on else np.ones(n)
    if swell_time is None:
        rise = np.minimum(1, u / 0.7)
    else:
        rise = np.minimum(1, t / swell_time)
        rise = rise * rise * (3 - 2 * rise)
    env = attack * (1 - swell + swell * rise) * np.clip((dur - t) / release, 0, 1) ** rel_exp
    if breathing:                                    # a player's breath: slow pressure drift and a catch now and then
        env *= 1 + 0.05 * smooth_noise(n, 0.3, rng)
    env *= 1 + 0.01 * smooth_noise(n, 1, rng)
    f = f0 * (1 - scoop * np.exp(-t / 0.05)) * (1 + 0.003 * smooth_noise(n, 0.5, rng))
    f *= 1 + 0.006 * env                                                             # blown harder, a little sharper
    f *= 1 - droop * np.clip((t - (dur - release)) / release, 0, 1) ** 2            # falls off at the end
    if glide_to is not None:                                                         # slur to the next tone
        g = np.clip((t - glide_at) / 0.07, 0, 1)
        g = g * g * (3 - 2 * g)
        f *= 1 + (glide_to - 1) * g
        dip = np.exp(-((t - glide_at - 0.035) / 0.06) ** 2)
        env *= 1 - 0.2 * dip
    ph = np.cumsum(f) / SR
    y = np.zeros(n)
    for k in range(1, 160):
        if f0 * k > 9500:
            break
        roll = np.exp(-(k - 1) / (1.2 + 12 * bright * env ** 2))                     # loud -> brassy
        roll = roll * (1 + 0.03 * smooth_noise(n, rng.uniform(0.3, 0.8), rng))           # each partial lives its own life
        roll = roll * (0.5 if k == 1 else 0.8 if k == 2 else 1.0)                    # big horns: weak fundamental
        y += roll * np.sin(2 * np.pi * k * ph + rng.uniform(0, 2 * np.pi))
    y /= np.abs(y).max() + 1e-9
    # the blat: a rough half-period subharmonic and a noise puff at the onset
    if attack_on:
        y += 0.3 * np.sin(np.pi * ph) * np.exp(-t / 0.05)
    pass                                                                             # (no cycle jitter: it read as a tremor)                          # lips flap unevenly, cycle to cycle
    y = y * env
    pass                                                                             # (no growl)   # a growl, comes and goes
    steep = np.tanh(y * (1 + 3 * bright * env)) / np.tanh(1 + 3 * bright)           # wave steepening
    y = 0.6 * y + 0.4 * steep
    # a big horn: the bore and bell resonances sit low
    body = (bandpass(y, 150, 1.2) * 0.6 + bandpass(y, 300, 1.0) * 1.1 + bandpass(y, 720, 1.4) * 0.7 + bandpass(y, 1150, 1.5) * 0.45
            + bandpass(y, 2000, 2.2) * 0.35 + y * 0.6)
    breath = bandpass(rng.normal(0, 1, n), 1800, 0.8) * env * 0.14 * (0.5 + 0.5 * np.maximum(0, np.sin(2 * np.pi * ph)) ** 4)
    breath += bandpass(rng.normal(0, 1, n), 4000, 1.0) * env ** 2 * 0.05             # hiss only when blown hard
    puff = lowpass(bandpass(rng.normal(0, 1, n), 1200, 0.7), 3000) * np.exp(-t / 0.04) * (0.12 if attack_on else 0.0)
    if glide_to is not None:
        puff += bandpass(bandpass(rng.normal(0, 1, n), 900, 1.5), 900, 1.5) * np.exp(-((t - glide_at) / 0.03) ** 2) * 0.05
    out = body + breath + puff
    out = highpass(highpass(out, 45, 0.9), 45, 0.9)
    return lowpass(out, 8000)


def horn_call(seed, f0, first, step=1.49):
    rng = np.random.default_rng(seed)
    dur = first + 2.0
    y = horn_note(rng, f0, dur, swell=0.6, scoop=0.11, bright=1.1, release=1.0, droop=0.045,
                  glide_to=step, glide_at=first)
    # it carries far: echoes off distant hills, up to 3 s away
    y = echo_tail(y, rng, taps=((0.6, 0.18), (1.3, 0.12), (2.1, 0.08), (3.0, 0.05)), tail=3.0, duck=0.75)
    return limit(y)


def horn_short(seed, f0, dur=1.3):
    rng = np.random.default_rng(seed)
    y = horn_note(rng, f0, dur, swell=0.3, scoop=0.1, bright=0.95, release=0.5, droop=0.035, rel_exp=2.0)
    y = echo_tail(y, rng, taps=((0.6, 0.14), (1.4, 0.08), (2.3, 0.05)), tail=2.4, wet=0.2, duck=0.75)
    return limit(y)


def horn_hold(seed, f0=61.7, dur=5.3):
    """The held blow: one long dry note (no release, no echo): the game cuts it when the button is let go."""
    rng = np.random.default_rng(seed)
    y = horn_note(rng, f0, dur, swell=0.6, scoop=0.08, bright=0.95, release=0.08, droop=0.0, rel_exp=1.0,
                  swell_time=1.0, breathing=True)
    return y


def horn_release(seed, f0=61.7):
    """Played when the held blow stops: the note falling off from full, then its echoes."""
    rng = np.random.default_rng(seed)
    y = horn_note(rng, f0, 0.3, swell=0.0, scoop=0.0, bright=0.95, release=0.25, droop=0.03, rel_exp=1.5, attack_on=False)
    y = echo_tail(y, rng, taps=((0.6, 0.14), (1.4, 0.08), (2.3, 0.05)), tail=2.4, wet=0.25, duck=0.5, tap_fade=0.2)
    m = int(0.01 * SR)                               # no click at the splice
    y[:m] *= 0.5 - 0.5 * np.cos(np.pi * np.arange(m) / m)
    return y


def vowel(rng, n, f_from, f_to, f1=(300, 450), f2=(750, 950), cluster=((0, 1.0), (1, 0.4), (-6, 0.6))):
    """Breathy "ooh" of a few spirits, an eerie cluster (unison, a semitone above, a fifth below), each line two
    slightly detuned voices with their own wander and staggered entries; a rich source with a weak fundamental, plus
    breath, through formants that open (F1, F2) over the sound."""
    t = np.arange(n) / SR
    u = t / (n / SR)
    base = f_from * (f_to / f_from) ** (u ** 0.8)
    src = np.zeros(n)
    for semis, gain in cluster:
        for det in (-0.006, 0.007):
            f = base * 2 ** (semis / 12) * (1 + det) * (1 + 0.006 * smooth_noise(n, 4, rng))
            ph = np.cumsum(f) / SR
            v = np.zeros(n)
            for k in range(1, 16):
                v += np.sin(2 * np.pi * k * ph + rng.uniform(0, 6.28)) * np.exp(-(k - 1) / 5) * (0.4 if k == 1 else 1.0)
            delay = rng.uniform(0, 0.6)
            v *= np.clip((t - delay) / 0.5, 0, 1)
            src += v * gain
    src /= np.abs(src).max() + 1e-9
    breath = highpass(highpass(rng.normal(0, 1, n), 150), 150)
    breath /= np.abs(breath).max() + 1e-9

    def formants(x, a):
        return (bandpass(x, f1[0] + (f1[1] - f1[0]) * a, 1.5) * 0.7 + bandpass(x, f2[0] + (f2[1] - f2[0]) * a, 3.0) * 0.6
                + bandpass(x, 2700, 4.0) * 0.25)
    closed = formants(src, 0.0) + formants(breath, 0.0) * 0.35
    opened = formants(src, 1.0) + formants(breath, 1.0) * 0.35
    w = np.clip(u / 0.8, 0, 1)
    return closed * (1 - w) + opened * w


def bells(rng, n, f, start, end, count=5):
    """A few sparse inharmonic partials, bowed glass rather than pings: slow bloom, varied ratios, slight detune."""
    out = np.zeros(n)
    for _ in range(count):
        c = int(rng.uniform(start, end) * n)
        L = int(rng.uniform(0.3, 0.6) * SR * 2)
        k = np.arange(min(L, n - c)) / SR
        if len(k) == 0:
            continue
        ratio = rng.choice([2.76, 3.9, 5.4, 6.8]) * (1 + rng.uniform(-0.02, 0.02))
        dec = rng.uniform(0.3, 0.6)
        att = rng.uniform(0.03, 0.06)
        out[c:c + len(k)] += np.sin(2 * np.pi * f * ratio * k + rng.uniform(0, 6.28)) * np.exp(-k / dec) * np.minimum(1, k / att)
    return out


RISE_TO = 1.19       # the spirits' voice rises 3 semitones; the fade starts where the rise ends


def ghost_rise(seed, f_from=220.0):
    rng = np.random.default_rng(seed)
    dur = 2.9
    n = int(SR * dur)
    t = np.arange(n) / SR
    u = t / dur
    env = u ** 1.5 * np.exp(-(u / 0.9) ** 8) * np.clip((1 - u) / 0.08, 0, 1)   # builds and blooms late, ends at 0
    whoosh = highpass(highpass(svf_sweep(rng.normal(0, 1, n), 250 + 2250 * u ** 2, 4.0), 150), 150)   # eases in, then rises
    whoosh /= np.abs(whoosh).max() + 1e-9
    voice = vowel(rng, n, f_from, f_from * RISE_TO)
    voice /= np.abs(voice).max() + 1e-9
    low = highpass(highpass(lowpass(rng.normal(0, 1, n), 90), 45), 45)
    low /= np.abs(low).max() + 1e-9
    air = highpass(highpass(rng.normal(0, 1, n), 4500), 4500)                  # broad air, not a hiss band
    air /= np.abs(air).max() + 1e-9
    low = low * lowpass(np.abs(rng.normal(0, 1, n)), 3)
    low /= np.abs(low).max() + 1e-9
    y = (whoosh * 0.6 + voice * 0.35 + low * 0.025 + air * 0.04 + bells(rng, n, f_from * RISE_TO, 0.5, 0.9) * 0.02) * env
    return echo_tail(highpass(y, 60), rng, taps=((0.35, 0.12), (0.8, 0.07)), tail=2.5, wet=0.45, bright=True,
                     decay=4.0, duck=0.3)


def ghost_fade(seed, f0=220.0 * RISE_TO):
    rng = np.random.default_rng(seed)
    dur = 2.0
    n = int(SR * dur)
    t = np.arange(n) / SR
    u = t / dur
    env = np.exp(-u * 1.8) * np.minimum(1, t / 0.25) ** 1.5 * np.clip((1 - u) / 0.3, 0, 1) ** 2   # swells in, then thins
    voice = vowel(rng, n, f0, f0 * 1.04, f1=(450, 550), f2=(850, 1000))   # the same voice, evaporating slightly upward
    voice /= np.abs(voice).max() + 1e-9
    # it dissolves: from 35 % on, the sound survives only in overlapping Hann grains, 30 per second thinning to 4
    grain = np.zeros(n)
    start = int(0.35 * n)
    grain[:start] = 1.0
    pos = start - int(0.06 * SR)
    while pos < n:
        L = int(rng.uniform(0.04, 0.12) * SR)
        w = np.hanning(L)[: n - pos] if pos + L > n else np.hanning(L)
        grain[pos:pos + len(w)] = np.maximum(grain[pos:pos + len(w)], w)
        density = 30 - 26 * ((pos - start) / max(1, n - start))
        pos += int(SR / max(4.0, density) * rng.uniform(0.6, 1.4))
    breath = highpass(highpass(bandpass(bandpass(rng.normal(0, 1, n), 1200, 2.0), 1200, 2.0), 150), 150)
    breath /= np.abs(breath).max() + 1e-9
    low = highpass(highpass(lowpass(rng.normal(0, 1, n), 140), 45), 45)
    low /= np.abs(low).max() + 1e-9
    scatter = svf_sweep(rng.normal(0, 1, n), 1500 + 2500 * u, 4.0)        # the mist breaking up, upward
    scatter /= np.abs(scatter).max() + 1e-9
    air = highpass(highpass(rng.normal(0, 1, n), 4500), 4500)
    air /= np.abs(air).max() + 1e-9
    y = (voice * 0.5 * grain + breath * 0.25 * (1 - u) ** 0.5 * grain + low * 0.02 + scatter * 0.2 * np.sin(np.pi * u)
         + air * 0.04 * (1 - u)) * env
    y = (1 - u) * lowpass(y, 8000) + u * lowpass(y, 2000)   # the highs close down (crossfade: no filter resets)
    send = highpass(y * (1 - u) ** 1.5 * grain, 70)          # the tail breaks up too
    out = echo_tail(send, rng, taps=((0.3, 0.1),), tail=2.0, wet=0.4, bright=True, decay=5.0, duck=0.0)
    out[:n] += highpass(y, 70) - send                        # dry: the full sound (echo_tail added the send)
    return out


if __name__ == "__main__":
    only = set(sys.argv[1:])

    def want(name):
        return not only or any(name.startswith(o) for o in only)

    if want("horn_call"):
        save("horn_call_0", horn_call(1, 58.0, 2.7, 1.49))
        # (call: first tone + 2 s, the second tone and its release)
        save("horn_call_1", horn_call(2, 55.0, 2.4, 1.45))
    if want("horn_short"):
        save("horn_short_0", horn_short(3, 65.4, 1.3))
        save("horn_short_1", horn_short(4, 61.7, 1.1))
    if want("horn_hold") or want("horn_release"):
        # one gain for both, from the hold's plateau, so the release starts at the level the hold was cut at
        hold, rel = horn_hold(8), horn_release(9)
        plateau = np.sqrt((hold[int(1.5 * SR):int(4.8 * SR)] ** 2).mean())
        head = np.sqrt((rel[int(0.012 * SR):int(0.06 * SR)] ** 2).mean())
        rel = rel * plateau / (head + 1e-9)
        g = 0.9 / max(np.abs(hold).max(), np.abs(rel).max())
        save_raw("horn_hold_0", hold * g)
        save_raw("horn_release_0", rel * g)
    if want("ghost_rise"):
        save("ghost_rise_0", ghost_rise(5, 220.0))
        save("ghost_rise_1", ghost_rise(6, 207.7))
    if want("ghost_fade"):
        save("ghost_fade_0", ghost_fade(7))
