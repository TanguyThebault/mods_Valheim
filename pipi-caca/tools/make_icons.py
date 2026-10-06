"""The mod's icons -> assets/icons/*.png (shipped next to the DLL in icons/).

usage: uv run --with pillow --with numpy python tools/make_icons.py <poop_render.png> [preview.png]
- caca.png (inventory): the Blender render (tools/blender_icon.py), trimmed, fitted with a margin, a dark
  outline and a soft drop shadow so it reads on the inventory slot, 128 px.
- se_caca.png (status effect): a small poop with three wavy stink lines.
- se_pipi.png (status effect): a yellow drop with a highlight.
Status icons follow Valheim's: a coloured glyph with a dark outline on a transparent background, 128 px.
The optional preview shows them on dark slots at 64 and 32 px, about their size in game.
"""
import math
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

OUT = Path(__file__).resolve().parent.parent / "assets" / "icons"
N = 128
SS = 4                                   # supersampling for drawn icons


def outline(img, width, color, blur=0.0):
    """A dark outline (dilated alpha) under the image, optionally with a soft shadow."""
    a = img.split()[3]
    grown = a.filter(ImageFilter.MaxFilter(width * 2 + 1))
    if blur:
        grown = grown.filter(ImageFilter.GaussianBlur(blur))
    base = Image.new("RGBA", img.size, color + (0,))
    base.putalpha(grown)
    base.alpha_composite(img)
    return base


def fit(img, size, margin):
    box = img.getbbox()
    img = img.crop(box)
    w, h = img.size
    s = (size - 2 * margin) / max(w, h)
    img = img.resize((max(1, round(w * s)), max(1, round(h * s))), Image.LANCZOS)
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    canvas.alpha_composite(img, ((size - img.width) // 2, (size - img.height) // 2))
    return canvas


def item_icon(render):
    src = Image.open(render).convert("RGBA")
    big = fit(src, N * 4, 4 * 10)
    # a soft shadow below-right, then a dark brown outline
    shadow = Image.new("RGBA", big.size, (0, 0, 0, 0))
    sh_alpha = big.split()[3].filter(ImageFilter.GaussianBlur(10)).point(lambda v: int(v * 0.55))
    shadow.putalpha(sh_alpha)
    shadow = shadow.transform(big.size, Image.AFFINE, (1, 0, -8, 0, 1, -12))
    out = outline(big, 7, (24, 13, 5))
    shadow.alpha_composite(out)
    return shadow.resize((N, N), Image.LANCZOS)


def stink_icon():
    S = N * SS
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    # a squat poop: three stacked blobs, the top one with a little curl
    brown, dark = (122, 74, 34, 255), (70, 40, 16, 255)
    cx, base = S * 0.5, S * 0.86
    tiers = [(0.36, 0.13), (0.27, 0.12), (0.17, 0.11)]
    y = base
    for i, (rw, rh) in enumerate(tiers):
        w, h = S * rw, S * rh
        d.ellipse((cx - w, y - 2 * h, cx + w, y), fill=brown)
        d.ellipse((cx - w * 0.75, y - 2 * h + h * 0.15, cx + w * 0.35, y - h * 1.1), fill=(160, 104, 52, 255))
        y -= h * 1.45
    d.polygon([(cx - S * 0.05, y + S * 0.03), (cx + S * 0.09, y - S * 0.07), (cx + S * 0.06, y + S * 0.05)], fill=brown)
    # three wavy stink lines above
    green = (150, 190, 70, 255)
    for k, x0 in enumerate((cx - S * 0.2, cx, cx + S * 0.2)):
        pts = []
        top, bot = S * 0.05, S * 0.36 - (k == 1) * S * 0.02
        for j in range(40):
            tt = j / 39
            yy = bot + (top - bot) * tt
            xx = x0 + math.sin(tt * math.pi * 2.2 + k) * S * 0.035
            pts.append((xx, yy))
        d.line(pts, fill=green, width=int(S * 0.035), joint="curve")
    img = img.resize((N, N), Image.LANCZOS)
    return outline(img, 3, (20, 14, 8))


def drop_icon():
    S = N * SS
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    # signed shape: a circle bottom and a pointed top
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float64) / S
    cx, cy, r = 0.5, 0.64, 0.25
    dx, dy = xx - cx, yy - cy
    inside = dx * dx + dy * dy < r * r
    # the tip: between the circle's tangents and the apex at y = 0.1
    apex = 0.1
    t = (cy - yy) / (cy - apex)
    half = r * np.sqrt(np.clip(1 - t, 0, 1)) * np.clip(t * 3, 0, 1) ** 0.0
    tip = (yy >= apex) & (yy <= cy) & (np.abs(dx) < r * np.clip(1 - t, 0, 1) ** 1.2)
    mask = inside | tip
    # shading: lighter top-left, deeper bottom-right
    shade = np.clip(0.75 + 0.6 * (-(dx) * 0.8 - (dy) * 0.9), 0.4, 1.2)
    col = np.zeros((S, S, 4))
    base = np.array([250, 214, 60], dtype=np.float64)
    col[..., :3] = np.clip(base[None, None, :] * shade[..., None], 0, 255)
    hl = np.exp(-(((xx - 0.41) / 0.05) ** 2 + ((yy - 0.56) / 0.09) ** 2))
    col[..., :3] = col[..., :3] * (1 - hl[..., None] * 0.85) + 255 * hl[..., None] * 0.85
    col[..., 3] = mask * 255
    img = Image.fromarray(col.astype(np.uint8), "RGBA").resize((N, N), Image.LANCZOS)
    return outline(img, 3, (40, 28, 6))


def preview(icons, path):
    rows = []
    for sz in (64, 32):
        row = Image.new("RGBA", ((sz + 16) * len(icons) + 16, sz + 32), (40, 34, 28, 255))
        for i, ic in enumerate(icons):
            x = 16 + i * (sz + 16)
            slot = Image.new("RGBA", (sz + 8, sz + 8), (22, 19, 16, 255))
            row.alpha_composite(slot, (x - 4, 12))
            row.alpha_composite(ic.resize((sz, sz), Image.LANCZOS), (x, 16))
        rows.append(row)
    w = max(r.width for r in rows)
    sheet = Image.new("RGBA", (w, sum(r.height for r in rows)), (40, 34, 28, 255))
    y = 0
    for r in rows:
        sheet.alpha_composite(r, (0, y))
        y += r.height
    sheet = sheet.resize((sheet.width * 2, sheet.height * 2), Image.NEAREST)
    sheet.save(path)


def mod_icon(item):
    """Thunderstore icon (256 px): the poop with its stink lines and a drop of pee, on a dark round plate."""
    S = 256 * SS
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle((0, 0, S - 1, S - 1), radius=S // 6, fill=(38, 30, 22, 255))
    d.rounded_rectangle((S * 0.03, S * 0.03, S * 0.97, S * 0.97), radius=S // 7, outline=(92, 70, 44, 255), width=SS * 4)
    poop = item.resize((int(S * 0.78), int(S * 0.78)), Image.LANCZOS)
    img.alpha_composite(poop, (int(S * 0.06), int(S * 0.2)))
    green = (150, 190, 70, 255)
    for k, x0 in enumerate((S * 0.36, S * 0.5, S * 0.64)):
        pts = []
        top, bot = S * 0.08, S * 0.3
        for j in range(40):
            tt = j / 39
            pts.append((x0 + math.sin(tt * math.pi * 2.2 + k) * S * 0.025, bot + (top - bot) * tt))
        d.line(pts, fill=green, width=int(S * 0.025), joint="curve")
    drop = drop_icon().resize((int(S * 0.3), int(S * 0.3)), Image.LANCZOS)
    img.alpha_composite(drop, (int(S * 0.66), int(S * 0.62)))
    return img.resize((256, 256), Image.LANCZOS)


if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    icons = {"caca": item_icon(sys.argv[1]), "se_caca": stink_icon(), "se_pipi": drop_icon()}
    mod_icon(icons["caca"]).save(OUT / "mod_icon.png")
    print("wrote", OUT / "mod_icon.png")
    for k, v in icons.items():
        v.save(OUT / f"{k}.png")
        print("wrote", OUT / f"{k}.png")
    if len(sys.argv) > 2:
        preview(list(icons.values()), sys.argv[2])
