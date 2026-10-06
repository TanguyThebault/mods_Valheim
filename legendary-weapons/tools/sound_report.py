"""Sound report for an evaluator (a second agent that can't listen): one PNG per sound with the waveform, the
amplitude envelope, a log-frequency spectrogram and the long-term spectrum, plus a JSON of measurements.

usage: uv run --with numpy --with matplotlib python tools/sound_report.py <out dir> [names...]
Loop files (pee_*) also get the seam measured: the jump between the last and first samples, and a spectrogram of
the end glued to the start.
"""
import json
import sys
import wave
from pathlib import Path

import numpy as np
import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt  # noqa: E402

SFX = Path(__file__).resolve().parent.parent / "sfx"


def load(p):
    with wave.open(str(p)) as w:
        sr = w.getframerate()
        x = np.frombuffer(w.readframes(w.getnframes()), "<i2").astype(np.float64) / 32768
    return x, sr


def stft(x, sr, nfft=2048, hop=256):
    win = np.hanning(nfft)
    frames = [x[i:i + nfft] * win for i in range(0, max(1, len(x) - nfft), hop)]
    S = np.abs(np.fft.rfft(np.array(frames), axis=1)).T
    f = np.fft.rfftfreq(nfft, 1 / sr)
    tt = np.arange(S.shape[1]) * hop / sr
    return S, f, tt


def measures(x, sr, S, f):
    env = np.sqrt(np.convolve(x ** 2, np.ones(441) / 441, "same"))
    mag = S.mean(axis=1)
    centroid = float((f * mag).sum() / (mag.sum() + 1e-12))
    cum = np.cumsum(mag) / mag.sum()
    roll85 = float(f[np.searchsorted(cum, 0.85)])
    low = float(mag[f < 250].sum() / mag.sum())
    # pulse rate (pitch of buzzy sounds): autocorrelation of the envelope-free signal, 20-500 Hz
    seg = x[: min(len(x), sr)]
    ac = np.correlate(seg, seg, "full")[len(seg) - 1:]
    lo, hi = int(sr / 500), int(sr / 20)
    lag = lo + int(np.argmax(ac[lo:hi])) if hi < len(ac) else 0
    onsets = int(((env[1:] > 0.25 * env.max()) & (env[:-1] <= 0.25 * env.max())).sum())
    return {
        "duration_s": round(len(x) / sr, 3),
        "peak": round(float(np.abs(x).max()), 3),
        "rms": round(float(np.sqrt((x ** 2).mean())), 4),
        "crest_db": round(float(20 * np.log10(np.abs(x).max() / (np.sqrt((x ** 2).mean()) + 1e-12))), 1),
        "spectral_centroid_hz": round(centroid),
        "rolloff85_hz": round(roll85),
        "energy_below_250hz": round(low, 3),
        "dominant_periodicity_hz": round(sr / lag, 1) if lag else None,
        "envelope_onsets_25pct": onsets,
        "seam_jump": round(float(abs(x[-1] - x[0])), 4),
    }


def plot(name, x, sr, out):
    S, f, tt = stft(x, sr)
    m = measures(x, sr, S, f)
    fig, ax = plt.subplots(4, 1, figsize=(11, 11), gridspec_kw={"height_ratios": [1, 1, 3, 1.4]})
    t = np.arange(len(x)) / sr
    ax[0].plot(t, x, lw=0.4)
    ax[0].set_title(f"{name}  waveform")
    ax[0].set_xlim(0, t[-1])
    # zoom on the first 60 ms of the loudest part (pulse shapes)
    c = int(np.argmax(np.convolve(np.abs(x), np.ones(2205), "same")))
    a, b = max(0, c - int(0.03 * sr)), min(len(x), c + int(0.03 * sr))
    ax[1].plot(np.arange(a, b) / sr * 1000, x[a:b], lw=0.7)
    ax[1].set_title("60 ms zoom around the loudest part (ms)")
    db = 20 * np.log10(S + 1e-6)
    ax[2].pcolormesh(tt, f[1:], db[1:], vmin=db.max() - 70, vmax=db.max(), shading="auto", cmap="magma")
    ax[2].set_yscale("log")
    ax[2].set_ylim(30, sr / 2)
    ax[2].set_title("spectrogram (log frequency, 70 dB range)")
    ax[2].set_ylabel("Hz")
    mag = 20 * np.log10(S.mean(axis=1) + 1e-9)
    ax[3].semilogx(f[1:], mag[1:])
    ax[3].set_xlim(30, sr / 2)
    ax[3].set_title("long-term spectrum (dB)")
    fig.tight_layout()
    fig.savefig(out / f"{name}.png", dpi=80)
    plt.close(fig)
    if name.startswith("loop_"):
        glued = np.concatenate([x[-sr // 2:], x[: sr // 2]])
        S2, f2, t2 = stft(glued, sr, 1024, 128)
        fig, ax = plt.subplots(2, 1, figsize=(10, 6))
        ax[0].plot(np.arange(len(glued)) / sr - 0.5, glued, lw=0.4)
        ax[0].axvline(0, color="r")
        ax[0].set_title(f"{name}: loop seam (end | start), red line = the loop point")
        d2 = 20 * np.log10(S2 + 1e-6)
        ax[1].pcolormesh(t2 - 0.5, f2[1:], d2[1:], vmin=d2.max() - 70, vmax=d2.max(), shading="auto", cmap="magma")
        ax[1].set_yscale("log")
        ax[1].set_ylim(30, sr / 2)
        fig.tight_layout()
        fig.savefig(out / f"{name}_seam.png", dpi=80)
        plt.close(fig)
    return m


def main():
    out = Path(sys.argv[1])
    out.mkdir(parents=True, exist_ok=True)
    names = sys.argv[2:]
    files = sorted(SFX.glob("*.wav"))
    if names:
        files = [p for p in files if any(p.stem.startswith(n) for n in names)]
    report = {}
    for p in files:
        x, sr = load(p)
        report[p.stem] = plot(p.stem, x, sr, out)
        print(p.stem, report[p.stem])
    (out / "measures.json").write_text(json.dumps(report, indent=1))


if __name__ == "__main__":
    main()
