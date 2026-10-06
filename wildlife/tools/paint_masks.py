"""Paint each creature's colour mask in its base model's UV layout, from that model's own 3D geometry.

For every UV pixel we find the 3D point it lands on and colour it by anatomy (up / forward / side, normalised
to the model's bounds). In game the mask is multiplied with the base texture's shading (Painter.Paint), so
the shipped PNGs hold only our colours. Bring your own game: meshes are read from your install at build time
and never committed.

    uv run --with UnityPy --with numpy --with pillow python tools/paint_masks.py <valheim> assets [preview_dir]

Masks:
- sparrow (on the Crow): grey-brown crown, red breast, buff cheeks and belly, brown back, dark wing tips.
- fox (on the Wolf): red coat, white chest, muzzle and tail tip, black socks, dark ear tips and nose.
- rabbit (on the Hare): grey-brown coat, cream belly and chin, white cottontail, dark ear tips.
- owl (on the Crow, reshaped in game): tawny, pale facial disc, streaked breast, barred wings and tail.
- mouse (on the Hare, ears shrunk and tail stretched in game): warm brown, pale belly, pinkish ears and tail.
"""
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

# ------------------------------------------------------------------ colours

CROWN = (0.47, 0.42, 0.38)
RED = (0.80, 0.20, 0.12)
BUFF = (0.86, 0.76, 0.60)
BROWN = (0.52, 0.35, 0.21)
DARK = (0.30, 0.20, 0.12)
BEAK = (0.45, 0.40, 0.33)
LEGS = (0.62, 0.48, 0.40)

FOX = (0.85, 0.42, 0.14)
WHITE = (0.93, 0.91, 0.86)
CREAM = (0.88, 0.80, 0.68)
BLACK = (0.16, 0.12, 0.10)

HARE = (0.58, 0.45, 0.32)
HARE_DARK = (0.30, 0.22, 0.15)

OWL = (0.46, 0.33, 0.20)
OWL_DARK = (0.30, 0.20, 0.12)
OWL_FACE = (0.80, 0.68, 0.52)
OWL_BREAST = (0.74, 0.61, 0.45)
OWL_BEAK = (0.78, 0.72, 0.56)

MOUSE = (0.54, 0.41, 0.29)
MOUSE_BELLY = (0.86, 0.81, 0.73)
MOUSE_PINK = (0.72, 0.56, 0.52)


def mottle(c, up, fwd, side, amount=0.18):
    """Deterministic speckle so feathers don't look flat."""
    n = np.sin(up * 127.1 + fwd * 311.7 + side * 74.7) * 43758.5453
    n = n - np.floor(n)
    k = 1 - amount + 2 * amount * n
    return tuple(min(1.0, x * k) for x in c)

# -------------------------------------------------------------- anatomy rules
# Each rule gets (up, fwd, side): up 0 = feet .. 1 = top, fwd 0 = tail .. 1 = nose, side 0 = centre .. 1 = flank.


def sparrow_sitting(up, fwd, side):
    if up < 0.17:
        return LEGS
    if fwd > 0.84 and up > 0.72:
        return BEAK
    if side > 0.78 and 0.30 < up < 0.80:
        return DARK if fwd < 0.45 else BROWN       # folded wing, darker flight feathers at the back
    if up > 0.82 and fwd > 0.58:
        return CROWN
    if up > 0.70 and fwd > 0.60:
        return BUFF                                # cheeks
    if fwd > 0.58 and 0.40 < up <= 0.70:
        return RED                                 # throat and breast
    if fwd < 0.17:
        return DARK                                # tail
    if up < 0.42:
        return BUFF                                # belly
    return BROWN


def sparrow_wings(up, fwd, side):
    return DARK if side > 0.62 else BROWN


def fox(up, fwd, side):
    if fwd > 0.975:
        return BLACK                               # nose
    if fwd < 0.07:
        return WHITE                               # tail tip
    if up < 0.28:
        return BLACK                               # socks
    if fwd > 0.80:
        if up > 0.92:
            return BLACK                           # ear tips
        if up < 0.74:
            return WHITE                           # muzzle, cheeks, throat
    if 0.62 < fwd <= 0.86 and 0.36 < up < 0.62 and side < 0.45:
        return WHITE                               # chest (between the front legs, not on them)
    if 0.25 < fwd <= 0.62 and 0.30 < up < 0.42 and side < 0.5:
        return CREAM                               # belly
    return FOX


def rabbit(up, fwd, side):
    if up > 0.93:
        return HARE_DARK                           # ear tips
    if fwd < 0.08 and up > 0.22:
        return WHITE                               # cottontail
    if fwd > 0.72 and up < 0.58 and side < 0.6:
        return CREAM                               # chin and throat
    if 0.18 < fwd < 0.8 and up < 0.32 and side < 0.75:
        return CREAM                               # belly
    return HARE


def owl_sitting(up, fwd, side):
    if up < 0.17:
        return OWL_FACE                                        # feathered feet
    if fwd > 0.84 and up > 0.72:
        return OWL_BEAK
    if side > 0.78 and 0.30 < up < 0.80:                       # folded wing, barred
        return mottle(OWL_DARK if int(fwd * 14) % 2 else OWL, up, fwd, side)
    if fwd > 0.60 and up > 0.68:
        return OWL_DARK if up > 0.93 else mottle(OWL_FACE, up, fwd, side, 0.08)   # facial disc, dark rim
    if up > 0.68:
        return mottle(OWL, up, fwd, side)                      # crown and nape
    if fwd > 0.50 and 0.30 < up <= 0.68:                       # breast with dark streaks
        return mottle(OWL if (side * 18) % 2 < 0.5 else OWL_BREAST, up, fwd, side, 0.1)
    if fwd < 0.17:
        return OWL_DARK if int(fwd * 30) % 2 else OWL          # barred tail
    return mottle(OWL, up, fwd, side)


def owl_wings(up, fwd, side):
    return mottle(OWL_DARK if int(side * 10) % 2 else OWL, up, fwd, side)


def mouse(up, fwd, side):
    if up > 0.85 and fwd > 0.6:
        return MOUSE_PINK                                      # ears
    if fwd < 0.08:
        return MOUSE_PINK                                      # tail
    if up < 0.35 and side < 0.75:
        return MOUSE_BELLY
    if fwd > 0.72 and up < 0.55 and side < 0.6:
        return MOUSE_BELLY                                     # chin
    return MOUSE


# ------------------------------------------------------------------ meshes

def parse_obj(text):
    V, T, F = [], [], []
    for line in text.splitlines():
        p = line.split()
        if not p:
            continue
        if p[0] == "v":
            V.append([float(x) for x in p[1:4]])
        elif p[0] == "vt":
            T.append([float(x) for x in p[1:3]])
        elif p[0] == "f":
            idx = [[int(i) - 1 for i in q.split("/")[:2]] for q in p[1:]]
            for k in range(1, len(idx) - 1):
                F.append((idx[0], idx[k], idx[k + 1]))
    return np.array(V), np.array(T), F


def export(valheim, prefab):
    """Meshes of a prefab, keyed by renderer type ('MeshFilter' / 'SkinnedMeshRenderer')."""
    import UnityPy
    bundles = Path(valheim) / "valheim_Data/StreamingAssets/SoftRef/Bundles"
    for b in sorted(bundles.iterdir()):
        if prefab.encode() not in b.read_bytes():
            continue
        env = UnityPy.load(str(b))
        root = next((o.read() for o in env.objects if o.type.name == "GameObject" and o.read().m_Name == prefab), None)
        if root is None:
            continue
        comp = lambda c: c.component if hasattr(c, "component") else c.second
        meshes = {}

        def walk(t):
            go = t.m_GameObject.read()
            for c in go.m_Component:
                cp = comp(c)
                if cp.type.name in ("SkinnedMeshRenderer", "MeshFilter"):
                    meshes.setdefault(cp.type.name, parse_obj(cp.read().m_Mesh.read().export()))
            for ch in t.m_Children:
                walk(ch.read())

        try:
            walk(next(comp(c) for c in root.m_Component if comp(c).type.name == "Transform").read())
        except FileNotFoundError:
            continue  # a variant whose meshes live in another bundle; keep looking
        if meshes:
            return meshes
    raise SystemExit(f"{prefab} not found in the game bundles")


def axes(V, up_axis, fwd_axis, fwd_sign):
    """Normalised (up, fwd, side) per vertex."""
    lo, hi = V.min(0), V.max(0)
    N = (V - lo) / np.maximum(hi - lo, 1e-12)
    side_axis = 3 - up_axis - fwd_axis
    up = N[:, up_axis]
    fwd = N[:, fwd_axis] if fwd_sign > 0 else 1 - N[:, fwd_axis]
    side = np.abs(N[:, side_axis] - 0.5) * 2
    return np.stack([up, fwd, side], 1)


def raster(img, owner, T, F, A, rule, size, accept=None):
    for tri in F:
        vi = [a for a, _ in tri]
        ti = [b for _, b in tri]
        if accept is not None and not accept(A[vi]):
            continue
        uv = T[ti] * [size, size]
        uv[:, 1] = size - uv[:, 1]
        x0, y0 = np.floor(uv.min(0)).astype(int)
        x1, y1 = np.ceil(uv.max(0)).astype(int)
        (ax, ay), (bx, by), (cx, cy) = uv
        den = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy)
        if abs(den) < 1e-9:
            continue
        for py in range(max(y0, 0), min(y1 + 1, size)):
            for px in range(max(x0, 0), min(x1 + 1, size)):
                qx, qy = px + 0.5, py + 0.5
                w0 = ((by - cy) * (qx - cx) + (cx - bx) * (qy - cy)) / den
                w1 = ((cy - ay) * (qx - cx) + (ax - cx) * (qy - cy)) / den
                w2 = 1 - w0 - w1
                if min(w0, w1, w2) < -0.02:
                    continue
                img[py, px] = rule(*(w0 * A[vi[0]] + w1 * A[vi[1]] + w2 * A[vi[2]]))
                owner[py, px] = True


def dilate(img, owner, fill, steps=6):
    for _ in range(steps):
        grown, out = owner.copy(), img.copy()
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            src = np.roll(np.roll(owner, dy, 0), dx, 1)
            col = np.roll(np.roll(img, dy, 0), dx, 1)
            take = src & ~grown
            out[take] = col[take]
            grown |= take
        img, owner = out, grown
    img[~owner] = fill
    return img


def preview(path, V, T, F, A, mask):
    """Side view (fwd vs up) and underside view (fwd vs side, seen from below) coloured by the mask."""
    size = mask.shape[0]
    canvas = Image.new("RGB", (600, 300), (40, 40, 40))
    for k, (h, v, key) in enumerate(((1, 0, lambda t: A[t, 2].mean()), (1, 2, lambda t: -A[t, 0].mean()))):
        im = Image.new("RGB", (300, 300), (40, 40, 40))
        d = ImageDraw.Draw(im)
        for tri in sorted(F, key=lambda t: key([a for a, _ in t])):
            vi = [a for a, _ in tri]
            uv = T[[b for _, b in tri]].mean(0)
            c = mask[min(int((1 - uv[1]) * size), size - 1), min(int(uv[0] * size), size - 1)]
            pts = [(A[a][h] * 260 + 20, (1 - A[a][v]) * 260 + 20) for a in vi]
            d.polygon(pts, fill=tuple(int(x * 255) for x in c), outline=(20, 20, 20))
        canvas.paste(im, (k * 300, 0))
    canvas.save(path)


def paint(valheim, out_dir, preview_dir, name, prefab, size, layers, fill):
    meshes = export(valheim, prefab)
    img = np.zeros((size, size, 3))
    owner = np.zeros((size, size), bool)
    first = None
    for kind, up_axis, fwd_axis, fwd_sign, rule, accept in layers:
        V, T, F = meshes[kind]
        A = axes(V, up_axis, fwd_axis, fwd_sign)
        raster(img, owner, T, F, A, rule, size, accept)
        first = first or (V, T, F, A)
    img = dilate(img, owner, fill)
    out = Path(out_dir) / f"{name}_colors.png"
    Image.fromarray((img * 255).astype("uint8")).save(out)
    print("wrote", out)
    if preview_dir:
        preview(Path(preview_dir) / f"{name}_preview.png", *first, img)


if __name__ == "__main__":
    valheim, out_dir = sys.argv[1], sys.argv[2]
    prev = sys.argv[3] if len(sys.argv) > 3 else None
    Path(out_dir).mkdir(parents=True, exist_ok=True)
    # Crow, sitting mesh: y up, beak towards -z. Flying mesh: only its spread wings (own UV islands).
    paint(valheim, out_dir, prev, "sparrow", "Crow", 256, [
        ("MeshFilter", 1, 2, -1, sparrow_sitting, None),
        ("SkinnedMeshRenderer", 2, 1, 1, sparrow_wings, lambda a: a[:, 2].mean() > 0.24),  # x = wingspan
    ], BROWN)
    # Wolf: x up, head towards -z, y sideways.
    paint(valheim, out_dir, prev, "fox", "Wolf", 512, [("SkinnedMeshRenderer", 0, 2, -1, fox, None)], FOX)
    # Hare: z up, head towards +y, x sideways.
    paint(valheim, out_dir, prev, "rabbit", "Hare", 512, [("SkinnedMeshRenderer", 2, 1, 1, rabbit, None)], HARE)
    paint(valheim, out_dir, prev, "owl", "Crow", 256, [
        ("MeshFilter", 1, 2, -1, owl_sitting, None),
        ("SkinnedMeshRenderer", 2, 1, 1, owl_wings, lambda a: a[:, 2].mean() > 0.24),
    ], OWL)
    paint(valheim, out_dir, prev, "mouse", "Hare", 512, [("SkinnedMeshRenderer", 2, 1, 1, mouse, None)], MOUSE)
