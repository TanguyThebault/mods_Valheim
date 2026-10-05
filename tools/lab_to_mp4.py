"""Model lab sheets (one row per view, frames across) -> a looping MP4 per sheet, all views side by side.

    uv run --with pillow python tools/lab_to_mp4.py <sheet.png>... --seconds 0.6 [--out dir] [--loops 4] [--tile 256]

The lab (src/Lab.cs) renders a clip as frames inside the game itself; played back at the clip's real speed this is
the "video" check of an animation as Unity plays it, before the in-world test.
"""
import argparse
import shutil
import subprocess
import tempfile
from pathlib import Path

from PIL import Image

ap = argparse.ArgumentParser()
ap.add_argument("sheets", nargs="+")
ap.add_argument("--seconds", type=float, required=True, help="length of the clip the sheet spans")
ap.add_argument("--tile", type=int, default=256)
ap.add_argument("--loops", type=int, default=4)
ap.add_argument("--out", default=None)
a = ap.parse_args()
ffmpeg = shutil.which("ffmpeg") or "ffmpeg"

for sheet in map(Path, a.sheets):
    im = Image.open(sheet).convert("RGB")
    cols, rows = im.width // a.tile, im.height // a.tile
    out = Path(a.out or sheet.parent) / (sheet.stem + ".mp4")
    out.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory() as tmp:
        n = 0
        for _ in range(a.loops):
            for c in range(cols):
                frame = Image.new("RGB", (a.tile * rows, a.tile))
                for r in range(rows):
                    frame.paste(im.crop((c * a.tile, r * a.tile, (c + 1) * a.tile, (r + 1) * a.tile)), (r * a.tile, 0))
                frame.save(Path(tmp) / f"f{n:04d}.png")
                n += 1
        fps = cols / a.seconds
        subprocess.run([ffmpeg, "-y", "-loglevel", "error", "-framerate", f"{fps:.4f}", "-i", str(Path(tmp) / "f%04d.png"),
                        "-c:v", "libx264", "-pix_fmt", "yuv420p", "-r", "30", str(out)], check=True)
    print(out, f"{cols} frames, {fps:.1f} fps, {a.loops} loops")
