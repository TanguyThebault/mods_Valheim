"""Paint the sparrow colour mask in the crow's UV layout, from the crow's own 3D geometry.

The vanilla crow is black and its 64x64 texture has no readable anatomy, but its two meshes (sitting and
flying) share one UV layout. So every UV pixel is coloured from the 3D point it lands on: blue crown, red
throat and breast, buff cheeks and belly, brown back, blue flight feathers and wing tips. In game the mask is
multiplied with the crow texture's shading (see Sparrows.Colorize), so the shipped PNG holds only our colours.

Bring your own game: the meshes are exported from your install at build time and never committed.

    uv run --with UnityPy --with numpy --with pillow python tools/paint_sparrow_mask.py <valheim> assets/sparrow_colors.png [preview.png]
"""
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

SIZE = 256
BLUE = (0.22, 0.38, 0.85)
RED = (0.80, 0.20, 0.12)
BUFF = (0.86, 0.76, 0.60)
BROWN = (0.52, 0.35, 0.21)
DARK = (0.30, 0.20, 0.12)
BEAK = (0.45, 0.40, 0.33)
LEGS = (0.62, 0.48, 0.40)


def export_meshes(valheim):
    import UnityPy
    bundles = Path(valheim) / "valheim_Data/StreamingAssets/SoftRef/Bundles"
    for b in bundles.iterdir():
        if b"crow_d" not in b.read_bytes():
            continue
        env = UnityPy.load(str(b))
        crow = next((o.read() for o in env.objects if o.type.name == "GameObject" and o.read().m_Name == "Crow"), None)
        if crow is None:
            continue
        meshes = {}

        def walk(t):
            go = t.m_GameObject.read()
            for c in go.m_Component:
                cp = c.component if hasattr(c, "component") else c.second
                if cp.type.name in ("SkinnedMeshRenderer", "MeshFilter"):
                    meshes[cp.type.name] = parse_obj(cp.read().m_Mesh.read().export())
            for ch in t.m_Children:
                walk(ch.read())

        for c in crow.m_Component:
            cp = c.component if hasattr(c, "component") else c.second
            if cp.type.name == "Transform":
                walk(cp.read())
        return meshes["MeshFilter"], meshes["SkinnedMeshRenderer"]  # sitting, flying
    raise SystemExit("Crow prefab not found in the game bundles")


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


def sitting_colour(p):
    """p = normalised position: x 0..1 (0.5 centre), y 0..1 up, z 0..1 with 0 at the beak."""
    x, y, z = p
    side = abs(x - 0.5) * 2
    if y < 0.17:
        return LEGS
    if z < 0.16 and y > 0.72:
        return BEAK
    if side > 0.78 and 0.30 < y < 0.80:
        return BLUE if z > 0.55 else BROWN  # folded wing: blue flight feathers at the back
    if y > 0.82 and z < 0.42:
        return BLUE                       # crown
    if y > 0.70 and z < 0.40:
        return BUFF                       # cheeks
    if z < 0.42 and 0.40 < y <= 0.70:
        return RED                        # throat and breast
    if z > 0.83:
        return DARK                       # tail
    if y < 0.42:
        return BUFF                       # belly
    return BROWN                          # back and flanks


def flying_wing_colour(p):
    x = p[0]
    side = abs(x - 0.5) * 2
    return BLUE if side > 0.62 else BROWN  # blue wing tips


def raster(img, owner, V, T, F, colour_of, accept=None):
    lo, hi = V.min(0), V.max(0)
    N = (V - lo) / np.maximum(hi - lo, 1e-9)
    for tri in F:
        vi = [a for a, _ in tri]
        ti = [b for _, b in tri]
        if accept is not None and not accept(N[vi]):
            continue
        uv = T[ti] * [SIZE, SIZE]
        uv[:, 1] = SIZE - uv[:, 1]
        x0, y0 = np.floor(uv.min(0)).astype(int)
        x1, y1 = np.ceil(uv.max(0)).astype(int)
        (ax, ay), (bx, by), (cx, cy) = uv
        den = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy)
        if abs(den) < 1e-9:
            continue
        for py in range(max(y0, 0), min(y1 + 1, SIZE)):
            for px in range(max(x0, 0), min(x1 + 1, SIZE)):
                qx, qy = px + 0.5, py + 0.5
                w0 = ((by - cy) * (qx - cx) + (cx - bx) * (qy - cy)) / den
                w1 = ((cy - ay) * (qx - cx) + (ax - cx) * (qy - cy)) / den
                w2 = 1 - w0 - w1
                if min(w0, w1, w2) < -0.02:
                    continue
                img[py, px] = colour_of(w0 * N[vi[0]] + w1 * N[vi[1]] + w2 * N[vi[2]])
                owner[py, px] = True


def dilate(img, owner, steps=6):
    for _ in range(steps):
        grown = owner.copy()
        out = img.copy()
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            src = np.roll(np.roll(owner, dy, 0), dx, 1)
            col = np.roll(np.roll(img, dy, 0), dx, 1)
            take = src & ~grown
            out[take] = col[take]
            grown |= take
        img, owner = out, grown
    img[~owner] = BROWN
    return img


def preview(path, sitting, mask):
    V, T, F = sitting
    lo, hi = V.min(0), V.max(0)
    N = (V - lo) / (hi - lo)
    canvas = Image.new("RGB", (600, 300), (40, 40, 40))
    for k, (a, b, depth, flip) in enumerate(((2, 1, 0, False), (0, 1, 2, True))):
        im = Image.new("RGB", (300, 300), (40, 40, 40))
        d = ImageDraw.Draw(im)
        order = sorted(F, key=lambda tri: N[[v for v, _ in tri], depth].mean() * (-1 if flip else 1))
        for tri in order:
            vi = [v for v, _ in tri]
            uv = T[[t for _, t in tri]].mean(0)
            c = mask[min(int((1 - uv[1]) * SIZE), SIZE - 1), min(int(uv[0] * SIZE), SIZE - 1)]
            pts = [(N[v][a] * 260 + 20, (1 - N[v][b]) * 260 + 20) for v in vi]
            d.polygon(pts, fill=tuple(int(x * 255) for x in c), outline=(20, 20, 20))
        canvas.paste(im, (k * 300, 0))
    canvas.save(path)


if __name__ == "__main__":
    sitting, flying = export_meshes(sys.argv[1])
    img = np.zeros((SIZE, SIZE, 3))
    owner = np.zeros((SIZE, SIZE), bool)
    raster(img, owner, *sitting, sitting_colour)
    # Only the flying mesh's spread wings have their own UV islands; the body shares the sitting islands.
    raster(img, owner, *flying, flying_wing_colour, accept=lambda n: abs(n[:, 0].mean() - 0.5) > 0.12)
    img = dilate(img, owner)
    Path(sys.argv[2]).parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray((img * 255).astype("uint8")).save(sys.argv[2])
    print("wrote", sys.argv[2])
    if len(sys.argv) > 3:
        preview(sys.argv[3], sitting, img)
