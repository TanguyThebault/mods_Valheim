"""The Skyfisher's portal: an unsettling sound as it opens and as it closes -> sfx/portal_*.wav.

usage: uv run --with numpy python tools/synth_portal.py
- portal_open (~5.5 s, as long as the vortex takes to form): a low resonant drone with irregular beating (kept low so
  it doesn't swamp the rest), a pressure of noise rising through a slowly opening filter, inharmonic metallic partials
  that hardly move in pitch but stretch apart (their ratios widen) and surface and vanish one by one, whispers (noise
  through drifting vowel formants), glassy veils high up (narrow resonances on air, each with its own slow swell),
  all in a cold far reverb; it fades out, no cut.
- portal_close (~3 s): the partials converge and sink a little, air is sucked back in, a reverse-swelling inward gulp
  and a soft low implosion (added dry, so no sub rumble lingers in the reverb).
Uses the filters and the reverb of synth_horn.py. Checked by an evaluator agent (see MODLOG).
"""
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
from synth_horn import (SR, save, bandpass, lowpass, highpass, smooth_noise, svf_sweep, echo_tail)  # noqa: E402


def drone(rng, n, base=41.2):
    """Three low partials, each wandering on its own (irregular beating), made audible with gentle saturation."""
    t = np.arange(n) / SR
    y = np.zeros(n)
    for mult, g in ((1.0, 1.0), (1.006, 0.8), (2.0, 0.4)):   # an octave: no 20 Hz difference tone
        f = base * mult * (1 + 0.004 * smooth_noise(n, 0.15, rng))
        y += g * np.sin(2 * np.pi * np.cumsum(f) / SR + rng.uniform(0, 6.28))
    y /= np.abs(y).max() + 1e-9
    return lowpass(np.tanh(4 * y), 600)


def metal(rng, n, f0, f1, ratios=(1.0, 2.76, 4.07, 5.4, 6.8, 8.9), stretch=0.06, vanish=False):
    """Inharmonic partials: the pitch barely moves, the ratios widen (stretch>0) or converge (stretch<0), each partial
    wanders and surfaces/vanishes on its own slow gate."""
    t = np.arange(n) / SR
    u = t / (n / SR)
    base = f0 + (f1 - f0) * u
    out = np.zeros(n)
    for k, r in enumerate(ratios):
        rr = r ** (1 + stretch * u)
        f = base * rr * (1 + 0.03 * smooth_noise(n, 0.2, rng))
        gate = 0.3 + 0.7 * np.clip(smooth_noise(n, rng.uniform(0.5, 1.5), rng) * 1.5, 0, 1)
        if vanish:                                   # the collapse: the highest partials vanish first
            gate = gate * np.clip((1 - u) * 1.6 - 0.1 * k, 0, 1)
        out += np.sin(2 * np.pi * np.cumsum(f) / SR + rng.uniform(0, 6.28)) * gate * (0.45 if k == 0 else 1 / (1 + 0.6 * k))   # the cluster carries it, not one tone
    return out / (np.abs(out).max() + 1e-9)


def whispers(rng, n, amount=1.0):
    """Breathy voiceless murmurs: noise through vowel formants that drift, in swells."""
    noise = highpass(rng.normal(0, 1, n), 300)
    f1 = 500 + 250 * smooth_noise(n, 0.7, rng)
    f2 = 1500 + 600 * smooth_noise(n, 0.5, rng)
    v = svf_sweep(noise, f1, 6.0) + svf_sweep(noise, f2, 8.0) * 0.6
    gate = np.clip(smooth_noise(n, 1.8, rng) * 1.6, 0, 1) ** 2
    v /= np.abs(v).max() + 1e-9
    return v * gate * amount


def veils(rng, n):
    """Glassy, vaporous veils high up: air through three narrow resonances, each swelling on its own."""
    air = highpass(rng.normal(0, 1, n), 3000)
    out = np.zeros(n)
    for f, g in ((2300.0, 1.0), (3700.0, 0.8), (5100.0, 0.4)):
        v = svf_sweep(air, f * (1 + 0.05 * smooth_noise(n, 0.3, rng)), 8.0)    # drifting: no steady whistle
        v /= np.abs(v).max() + 1e-9
        out += v * g * np.clip(0.5 + 0.5 * smooth_noise(n, 0.4, rng), 0, 1)
    return out / 2.2


def portal_open(seed, dur=5.6):
    rng = np.random.default_rng(seed)
    n = int(SR * dur)
    t = np.arange(n) / SR
    u = t / dur
    swell = 0.2 + 0.8 * np.clip(u / 0.82, 0, 1) ** 1.3                 # heard from the start, peaks at ~4.6 s
    pressure = svf_sweep(rng.normal(0, 1, n), 150 + 2600 * u ** 1.8, 2.5)
    pressure /= np.abs(pressure).max() + 1e-9
    m = metal(rng, n, 260.0, 300.0, stretch=0.06) * np.clip((u - 0.15) / 0.6, 0, 1) ** 2
    y = (drone(rng, n) * 0.09 * (0.5 + 0.5 * swell) + pressure * 0.35 * swell + m * 0.3
         + whispers(rng, n) * 0.25 * swell + veils(rng, n) * 0.1 * swell)
    y *= np.clip((dur - t) / 0.9, 0, 1) ** 1.5                            # fades out, no cut
    y = highpass(y, 30)
    return echo_tail(y, rng, taps=((0.5, 0.12), (1.2, 0.07)), tail=3.0, wet=0.45, bright=True, decay=4.0, duck=0.2)


def portal_close(seed, dur=3.0):
    rng = np.random.default_rng(seed)
    n = int(SR * dur)
    t = np.arange(n) / SR
    u = t / dur
    env = np.minimum(1, t / 0.15) * np.clip((1 - u) / 0.15, 0, 1)
    suck = svf_sweep(rng.normal(0, 1, n), 3000 * (1 - u) ** 1.5 + 120, 3.0)      # air rushing back in, falling
    suck /= np.abs(suck).max() + 1e-9
    m = metal(rng, n, 300.0, 255.0, stretch=-0.2, vanish=True)                      # converging, vanishing
    y = (suck * 0.45 * (0.3 + 0.7 * u) + m * 0.3 * (1 - u) + whispers(rng, n, 0.6) * 0.2 * (1 - u)
         + drone(rng, n) * 0.07 * (1 - u) + veils(rng, n) * 0.08 * (1 - u)) * env
    y = highpass(y, 28)
    out = echo_tail(y, rng, taps=((0.5, 0.12), (1.2, 0.07)), tail=2.5, wet=0.45, bright=True, decay=4.0, duck=0.2)
    # the implosion, dry: a reverse-swelling inward gulp, then a soft low thump
    c = int(0.88 * n)
    L = int(0.25 * SR)
    k = np.arange(L) / L
    gulp = rng.normal(0, 1, L) * k ** 3
    seg = np.zeros(L)
    for i in range(0, L, 441):
        seg[i:i + 441] = lowpass(gulp[i:i + 441 + 200], 300 - 180 * (i / L))[:len(seg[i:i + 441])]
    seg = lowpass(seg, 300)
    out[c - L:c] += seg / (np.abs(seg).max() + 1e-9) * 0.25
    kk = np.arange(int(0.6 * SR)) / SR
    thump = np.sin(2 * np.pi * np.cumsum(20 * np.exp(-kk * 6) + 35) / SR) * np.exp(-kk * 8) * np.minimum(1, kk / 0.008)
    thump += lowpass(rng.normal(0, 1, len(kk)), 200) * np.exp(-kk * 12) * 0.3
    out[c:c + len(kk)] += thump * 0.35 * np.abs(out).max() / (np.abs(thump).max() + 1e-9)
    return out


if __name__ == "__main__":
    save("portal_open_0", portal_open(31))
    save("portal_close_0", portal_close(32))
