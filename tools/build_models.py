"""Convert generated GLB models (fal Trellis) into the mod's tiny mesh format + texture.

Output per model: <name>.tam (magic 'TAM1', int32 vertex count, int32 index count, float32 positions xyz,
normals xyz, uv xy, int32 indices) and <name>.png (base colour, max 512 px).
Converted to Unity's left-handed convention (x mirrored, triangle winding reversed). Models keep their
generated frame: y up, facing +z, the animal's left towards -x.

    uv run --with trimesh --with numpy --with pillow --with scipy python tools/build_models.py assets/gen assets/models
"""
import struct
import sys
from pathlib import Path

import numpy as np
import trimesh
from PIL import Image

# name -> (glb, degrees around y applied first so the animal faces +z)
MODELS = {
    "owl_perched": ("owl_perched_3d.glb", 0),
    "owl_flying": ("owl_flying_3d.glb", 0),
    "mouse": ("mouse_3d.glb", 0),
    # Trellis 2 with a 5000-face target (decimating its 94k-face output left shards on the back)
    "whale": ("whale3_3d.glb", 90),   # generated lying along x, head towards -x
    "orca": ("orca3_3d.glb", 0),      # Trellis 2, 5000 faces (the first trellis orca had a stray flap under the belly)
}


def load(path):
    scene = trimesh.load(path)
    if not hasattr(scene, "graph"):
        return scene
    parts = []
    for node in scene.graph.nodes_geometry:
        transform, name = scene.graph[node]
        m = scene.geometry[name].copy()
        m.apply_transform(transform)
        parts.append(m)
    return parts[0] if len(parts) == 1 else trimesh.util.concatenate(parts)


def slate(img):
    """Whale recolour: the generated navy-black becomes a lighter, warm slate grey with soft mottling (so it
    reads differently from the black-and-white orca); the white belly and fins stay."""
    a = np.asarray(img, dtype=np.float32) / 255.0
    lum = a @ np.array([0.3, 0.59, 0.11], dtype=np.float32)
    dark = np.array([0.24, 0.25, 0.26], dtype=np.float32)
    mid = np.array([0.50, 0.50, 0.48], dtype=np.float32)
    t = np.clip(lum / 0.55, 0, 1)[..., None]
    grey = dark + (mid - dark) * t
    rng = np.random.default_rng(7)
    small = rng.normal(0, 1, (a.shape[0] // 24 + 2, a.shape[1] // 24 + 2)).astype(np.float32)
    smooth = Image.fromarray(((small - small.min()) / (np.ptp(small) + 1e-6) * 255).astype("uint8"))
    noise = np.asarray(smooth.resize((a.shape[1], a.shape[0]), Image.BICUBIC), dtype=np.float32) / 127.5 - 1.0
    grey = grey * (1 + 0.06 * noise[..., None])
    w = np.clip((lum - 0.5) / 0.2, 0, 1)[..., None]          # keep whites
    out = grey * (1 - w) + a * w
    return Image.fromarray((np.clip(out, 0, 1) * 255).astype("uint8"))


def _noise(P, scale, seed=0):
    """Smooth-ish value noise from 3D positions (sum of sines), in 0..1."""
    x, y, z = P[..., 0] * scale, P[..., 1] * scale, P[..., 2] * scale
    v = (np.sin(x * 1.7 + y * 2.3 + seed) + np.sin(y * 1.9 - z * 2.9 + seed * 1.3) + np.sin(z * 2.1 + x * 1.1 - seed * 0.7)
         + 0.5 * np.sin(x * 5.3 - z * 4.1 + seed) + 0.5 * np.sin(y * 6.1 + z * 3.7 - seed))
    return (v / 4.0 + 1) / 2


def whale_colour(P, N):
    """Humpback: slate back, white belly with throat grooves, long white flippers (grey on top), flukes grey on top
    and white mottled with black underneath, a few darker tubercles on the head. P normalised (x side, y up,
    z 0 tail .. 1 head), N unit normals."""
    slate = np.array([0.36, 0.38, 0.40])
    white = np.array([0.88, 0.89, 0.88])
    groove = np.array([0.62, 0.64, 0.65])
    dark = np.array([0.16, 0.17, 0.18])
    x, y, z = P[..., 0], P[..., 1], P[..., 2]
    side = np.abs(x - 0.5)
    under = np.clip((-N[..., 1] - 0.05) / 0.45, 0, 1)                 # facing down
    belly = np.clip(under * np.clip((0.55 - y) / 0.25, 0, 1) * 1.4, 0, 1)
    back = slate * (0.88 + 0.24 * _noise(P, 9.0, 1.0))[..., None]
    col = back * (1 - belly[..., None]) + white * belly[..., None]
    # throat grooves: thin darker lines along the body under the head
    throat = (z > 0.6) & (belly > 0.5) & ((np.mod(x * 46.0, 1.0)) < 0.18)
    col[throat] = groove
    # flippers
    # flippers: white on both faces with a faint grey marbling (thin sheets: the normal's sign is unreliable)
    fin = (side > 0.16) & (z > 0.4) & (z < 0.82) & (y < 0.6)
    fin_col = white * (0.86 + 0.12 * _noise(P, 18.0, 7.0))[..., None]
    root_shade = np.clip((0.34 - side) / 0.16, 0, 1)[..., None]           # darker toward the body: reads as 3D
    fin_col = fin_col * (1 - root_shade) + slate * 1.25 * root_shade
    col[fin] = fin_col[fin]
    # flukes (the procedural crescent has separate top and underside islands): slate on top, white underneath
    # with black mottling, like a humpback's ID pattern
    fl = z < 0.17
    fl_under = fl & (N[..., 1] < 0)
    col[fl & ~fl_under] = back[fl & ~fl_under]
    mott = _noise(P, 22.0, 3.0) > 0.6
    col[fl_under] = np.where(mott[fl_under][..., None], dark, white)
    # tubercles on the head
    tub = (z > 0.8) & (N[..., 1] > 0.5) & (_noise(P, 60.0, 5.0) > 0.86)
    col[tub] = col[tub] * 0.55
    return np.clip(col, 0, 1)


def paint_from_geometry(pos, nrm, uv, faces, colour, size=1024):
    """Rasterise every triangle into the UV atlas and colour each texel from the 3D point and normal it maps to."""
    lo, hi = pos.min(0), pos.max(0)
    P = (pos - lo) / np.maximum(hi - lo, 1e-9)
    # unpainted texels (degenerate UV triangles still sample them) get the base colour, not black
    base = colour(np.array([[0.5, 0.9, 0.5]]), np.array([[0.0, 1.0, 0.0]]))[0]
    img = np.tile(base.astype(np.float32), (size, size, 1))
    owner = np.zeros((size, size), bool)
    U = uv * size
    U[:, 1] = size - U[:, 1]
    for tri in faces:
        a, b, c = U[tri]
        x0, y0 = np.floor(np.minimum(np.minimum(a, b), c)).astype(int)
        x1, y1 = np.ceil(np.maximum(np.maximum(a, b), c)).astype(int)
        x0, y0, x1, y1 = max(x0, 0), max(y0, 0), min(x1, size - 1), min(y1, size - 1)
        if x1 < x0 or y1 < y0:
            continue
        gx, gy = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
        den = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])
        if abs(den) < 1e-12:
            continue
        w0 = ((b[1] - c[1]) * (gx - c[0]) + (c[0] - b[0]) * (gy - c[1])) / den
        w1 = ((c[1] - a[1]) * (gx - c[0]) + (a[0] - c[0]) * (gy - c[1])) / den
        w2 = 1 - w0 - w1
        inside = (w0 >= -0.01) & (w1 >= -0.01) & (w2 >= -0.01)
        if not inside.any():
            continue
        W = np.stack([w0[inside], w1[inside], w2[inside]], -1)
        pp = W @ P[tri]
        nn = W @ nrm[tri]
        nn /= np.linalg.norm(nn, axis=-1, keepdims=True) + 1e-9
        ys, xs = (gy[inside] - 0.5).astype(int), (gx[inside] - 0.5).astype(int)
        img[ys, xs] = colour(pp, nn)
        owner[ys, xs] = True
    for _ in range(24):   # bleed into the gutters so seams don't show
        grown, out = owner.copy(), img.copy()
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            src = np.roll(np.roll(owner, dy, 0), dx, 1)
            take = src & ~grown
            out[take] = np.roll(np.roll(img, dy, 0), dx, 1)[take]
            grown |= take
        img, owner = out, grown
    return Image.fromarray((img * 255).astype("uint8"))


def clean_shells(pos, nrm, uv, faces, min_faces=20):
    """Generated meshes can carry many stray fragments (the whale: 145 bits under 20 faces, poking through as dark
    spots). Drop them; the material's two-sided normals light the thin fins and flukes from both sides."""
    import trimesh
    g = trimesh.Trimesh(vertices=pos.copy(), faces=faces.copy(), process=False)
    g.merge_vertices(merge_tex=True, merge_norm=True, digits_vertex=4)      # weld UV seams to find real parts
    labels = trimesh.graph.connected_component_labels(g.face_adjacency, node_count=len(g.faces))
    sizes = np.bincount(labels)
    main = int(np.argmax(sizes))
    keep = sizes[labels] >= min_faces
    sheet = keep & (labels != main)
    # thin sheets (fins, flukes) keep their normals: the material draws both faces with flipped normals behind
    nrm2 = nrm
    pos2, uv2 = pos, uv
    faces2 = faces[keep].astype(np.int32)
    print(f"  cleaned: dropped {int((~keep).sum())} faces in {int((sizes < min_faces).sum())} fragments, "
          f"{len(set(labels[sheet]))} thin sheets kept")
    return pos2.astype(np.float32), nrm2.astype(np.float32), uv2.astype(np.float32), faces2


CLEAN = {"whale": clean_shells}


def straighten_tail(pos, nrm, z_from):
    """Level a drooping tail: the generated whale was frozen mid-dive, its tail 10 % of its length below the back,
    so even a good swimming stroke never seemed to lift it. Measures the centre line (middle of top and bottom of
    a central strip) and shifts each slice behind z_from (normalised, 0 = tail tip) back onto the line's height
    at z_from. Normals follow the shear."""
    L = np.ptp(pos[:, 2])
    zn = (pos[:, 2] - pos[:, 2].min()) / L
    xc = np.median(pos[:, 0])
    strip = np.abs(pos[:, 0] - xc) < 0.03 * L
    grid = np.linspace(0, 1, 101)
    mid = np.full(len(grid), np.nan)
    for i, z in enumerate(grid):
        m = strip & (np.abs(zn - z) < 0.015)
        if m.sum() > 2:
            mid[i] = (pos[m, 1].max() + pos[m, 1].min()) / 2
    ok = ~np.isnan(mid)
    mid = np.interp(grid, grid[ok], mid[ok])
    mid = np.convolve(np.pad(mid, 3, mode="edge"), np.ones(7) / 7, mode="valid")      # smooth
    ref = np.interp(z_from, grid, mid)
    off = np.where(grid < z_from, mid - ref, 0.0)
    dy = np.interp(zn, grid, off)
    slope = np.gradient(off, grid) / L                                               # d(offset)/dz
    pos = pos.copy()
    pos[:, 1] -= dy
    nrm = nrm.copy()
    nrm[:, 2] += np.interp(zn, grid, slope) * nrm[:, 1]
    nrm /= np.linalg.norm(nrm, axis=1, keepdims=True) + 1e-9
    print(f"  straightened tail: up to {off.min() / L * 100:+.1f} % of the length moved back up")
    return pos.astype(np.float32), nrm.astype(np.float32)


def _vertex_normals(pos, faces):
    import trimesh
    return np.array(trimesh.Trimesh(vertices=pos, faces=faces, process=False).vertex_normals, dtype=np.float32)


def weld_normals(pos, nrm, L):
    """Average the normals of vertices that share a position (UV seams split them, which shaded the generated
    meshes as visible facets and creases). Skips groups whose normals disagree (the two faces of a thin sheet)."""
    key = np.round(pos / (L * 1e-4)).astype(np.int64)
    _, inv = np.unique(key, axis=0, return_inverse=True)
    inv = inv.ravel()
    acc = np.zeros((inv.max() + 1, 3))
    cnt = np.zeros(inv.max() + 1)
    np.add.at(acc, inv, nrm)
    np.add.at(cnt, inv, 1)
    mean = acc[inv]
    ok = np.linalg.norm(mean, axis=1) > 0.6 * cnt[inv]
    out = nrm.copy()
    m = ok & (cnt[inv] > 1)
    out[m] = (mean[m] / np.linalg.norm(mean[m], axis=1, keepdims=True)).astype(np.float32)
    print(f"  welded normals at {int(m.sum())} seam vertices")
    return out


def rebuild_whale(pos, nrm, uv, faces):
    """The generated humpback had two loose round pads for flukes and stubby flippers (15 % of its length; a real
    humpback's are about 30 %). Replace the flukes with a procedural crescent (swept back, central notch, span
    31 % of the length, thin hydrofoil section) and stretch the flippers from their roots. The flukes get their
    own UV islands (top and underside) in a strip freed on the right of the atlas, so the underside can be painted
    white and mottled without showing on top."""
    import trimesh
    L = np.ptp(pos[:, 2])
    z0 = pos[:, 2].min()
    zn = (pos[:, 2] - z0) / L
    xc = float(np.median(pos[:, 0]))
    # --- old flukes out (every face entirely behind the tail stock)
    cut = 0.125
    keep = ~(zn[faces] < cut).all(1)
    faces = faces[keep]
    # --- flippers: stretch each loose sheet in the flipper zone away from its root
    g = trimesh.Trimesh(vertices=pos.copy(), faces=faces.copy(), process=False)
    g.merge_vertices(merge_tex=True, merge_norm=True, digits_vertex=4)
    labels = trimesh.graph.connected_component_labels(g.face_adjacency, node_count=len(g.faces))
    pos = pos.copy()
    stretched = []
    # group the loose pieces in the flipper zone by side (one flipper came as two pieces), one root per side
    groups = {-1: [], 1: []}
    for lab in np.unique(labels):
        fv = np.unique(faces[labels == lab])
        c = pos[fv].mean(0)
        czn = (c[2] - z0) / L
        if 0.45 < czn < 0.85 and abs(c[0] - xc) > 0.08 * L and 10 <= len(fv) < 400:
            groups[1 if c[0] > xc else -1].append(fv)
    # part of each flipper is welded to the body: add every vertex of the flipper zone beyond the body's flank
    used_v = np.unique(faces)
    zv = (pos[used_v, 2] - z0) / L
    ymid = float(np.median(pos[used_v, 1]))
    flank = used_v[(zv > 0.45) & (zv < 0.85) & (np.abs(pos[used_v, 0] - xc) > 0.105 * L) & (pos[used_v, 1] < ymid)]
    flipper_v = set()
    for side, parts in groups.items():
        extra = flank[np.sign(pos[flank, 0] - xc) == side]
        if not parts and len(extra) == 0:
            continue
        fv = np.unique(np.concatenate(parts + [extra]))
        root = pos[fv[np.argmin(np.abs(pos[fv, 0] - xc))]].copy()
        tip = pos[fv[np.argmax(np.abs(pos[fv, 0] - xc))]]
        axis = (tip - root) / np.linalg.norm(tip - root)
        d = pos[fv] - root
        along = d @ axis
        rest = d - np.outer(along, axis)
        rest[:, 1] *= 0.75                                   # flatter
        # humpback pose: swept back 35 degrees, dipped 20 degrees below horizontal
        sweep, dip = np.radians(35), np.radians(20)
        level = np.array([side * np.cos(dip) * np.cos(sweep), -np.sin(dip), -np.cos(dip) * np.sin(sweep)])
        # rotate the cross-section with the axis (Rodrigues), so the blade keeps its orientation around it
        k = np.cross(axis, level)
        sn, cs = np.linalg.norm(k), float(axis @ level)
        if sn > 1e-6:
            k /= sn
            K = np.array([[0, -k[2], k[1]], [k[2], 0, -k[0]], [-k[1], k[0], 0]])
            R = np.eye(3) + sn * K + (1 - cs) * (K @ K)
            rest = rest @ R.T
        t = np.clip(along / max(along.max(), 1e-6), 0, 1)
        rest *= (1.15 * (1 - 0.6 * t))[:, None]              # tapers to 40 % of the root chord at the tip
        # tubercles: bumps along the leading edge (the forward side of the blade)
        fwd = np.array([0.0, 0.0, 1.0]) - level * level[2]
        fwd /= np.linalg.norm(fwd)
        lead = np.clip(rest @ fwd / (0.02 * L), 0, 1)
        rest += np.outer(lead * 0.009 * L * np.maximum(0, np.sin(t * np.pi * 9)) ** 2, fwd)
        moved_to = root + np.outer(along * 1.9, level) + rest
        w = np.clip(along / (0.05 * L), 0, 1)[:, None]          # fade in from the root: no torn skirt at the flank
        w = w * w * (3 - 2 * w)
        pos[fv] = pos[fv] * (1 - w) + moved_to * w
        stretched.append(len(fv))
        flipper_v.update(fv[w[:, 0] > 0.5].tolist())
    # a flap of the generated mesh stuck out of the right flank behind the flipper root: make the body outline
    # symmetric there (each side clamped to the narrower of the two, flippers excepted)
    used_v = np.unique(faces)
    body = np.array([v for v in used_v if v not in flipper_v])
    zb = (pos[body, 2] - z0) / L
    zone = body[(zb > 0.36) & (zb < 0.72)]
    bins = np.linspace(0.36, 0.72, 19)
    zz = (pos[zone, 2] - z0) / L
    off = pos[zone, 0] - xc
    env = np.zeros((len(bins) - 1, 2))
    for i in range(len(bins) - 1):
        m = (zz >= bins[i]) & (zz < bins[i + 1])
        left, right = -off[m & (off < 0)], off[m & (off > 0)]
        env[i] = [np.percentile(left, 97) if len(left) else 0, np.percentile(right, 97) if len(right) else 0]
    limit = np.convolve(np.pad(env.min(1), 1, mode="edge"), np.ones(3) / 3, mode="valid") * 1.03
    lim_v = np.interp(zz, (bins[:-1] + bins[1:]) / 2, limit)
    over = np.abs(off) > lim_v
    pos[zone[over], 0] = xc + np.sign(off[over]) * lim_v[over]
    print(f"  flank made symmetric: {int(over.sum())} vertices pulled in")
    # --- the crescent
    stock = (np.abs(zn - 0.15) < 0.02) & (np.abs(pos[:, 0] - xc) < 0.03 * L)
    yc = float((pos[stock, 1].max() + pos[stock, 1].min()) / 2)
    S = 0.155 * L                                           # half span
    ns, nc = 41, 11
    sgrid = np.cos(np.linspace(np.pi, 0, ns))               # -1..1, denser at the tips
    vgrid = (1 - np.cos(np.linspace(0, np.pi, nc))) / 2     # 0 leading edge .. 1 trailing edge
    new_pos, new_uv, new_faces = [], [], []
    base = len(pos)
    for side, (u_lo, u_hi, v_lo, v_hi) in ((+1, (0.905, 0.995, 0.02, 0.48)), (-1, (0.905, 0.995, 0.52, 0.98))):
        start = base + len(new_pos)
        for si, sv in enumerate(sgrid):
            u = abs(sv)
            zle = 0.17 - 0.085 * u ** 1.5 - 0.05 * u ** 8          # tips swept back
            chord = 0.1 * max(1 - u * u, 0) ** 0.45 * (1 - 0.5 * u ** 4) + 0.004 * (1 - u)   # pointed tips
            notch = 0.03 * max(0.0, 1 - u / 0.14) ** 2
            for ci, cv in enumerate(vgrid):
                scallop = 0.007 * abs(np.sin(u * np.pi * 4)) * (u > 0.15) * cv ** 4   # small notches on the trailing edge
                z = zle - cv * (chord - notch * cv) + scallop
                thick = (0.016 * (1 - u) + 0.003) * np.sin(np.pi * cv) ** 0.8
                y = yc + side * thick * L / 2 - 0.01 * L * u * u   # slight droop at the tips
                new_pos.append([xc + sv * S, y, z0 + z * L])
                new_uv.append([u_lo + (u_hi - u_lo) * cv, v_lo + (v_hi - v_lo) * (si / (ns - 1))])
        for si in range(ns - 1):
            for ci in range(nc - 1):
                a = start + si * nc + ci
                b, c, d = a + 1, a + nc, a + nc + 1
                tri = [[a, c, b], [b, c, d]] if side > 0 else [[a, b, c], [b, d, c]]
                new_faces += tri
    new_pos = np.array(new_pos, np.float32)
    pos = np.concatenate([pos, new_pos])
    faces = np.concatenate([faces, np.array(new_faces, np.int32)])
    # make the crescent's faces point outward (top up, underside down) whatever the winding above did
    vn = _vertex_normals(pos, faces)
    top = np.arange(base, base + ns * nc)
    if vn[top, 1].mean() < 0:
        tail_faces = faces[:, 0] >= base
        faces[tail_faces] = faces[tail_faces][:, [0, 2, 1]]
        vn = _vertex_normals(pos, faces)
    # old atlas squeezed into the left 90 %, the crescent's islands on the right
    uv = np.concatenate([uv * np.array([0.9, 1.0], np.float32), np.array(new_uv, np.float32)])
    nrm = np.concatenate([nrm, vn[base:]])
    print(f"  whale rebuilt: {int((~keep).sum())} fluke faces replaced by a {len(new_faces)}-face crescent, "
          f"flippers stretched {stretched}")
    return pos.astype(np.float32), nrm.astype(np.float32), uv.astype(np.float32), faces.astype(np.int32), stretched


STRAIGHTEN = {"whale": 0.40}
FLAT_FLUKES = {}   # was {"whale": 0.13}: forcing normals up broke two-sided lighting (black fluke)

RECOLOR = {}
# Textures painted from the model's own geometry, replacing the generated texture (the whale's had odd blotches).
PAINT = {"whale": whale_colour}


def texture_of(mesh):
    mat = mesh.visual.material
    img = getattr(mat, "baseColorTexture", None) or getattr(mat, "image", None)
    img = img.convert("RGB")
    if max(img.size) > 512:
        img = img.resize((512, 512), Image.LANCZOS)
    return img


def convert(src, out_dir, name, yaw=0):
    mesh = load(src)
    # Safety net: consistent, outward winding (the current models were already consistent).
    trimesh.repair.fix_winding(mesh)
    trimesh.repair.fix_inversion(mesh, multibody=True)
    trimesh.repair.fix_normals(mesh, multibody=True)
    pos = np.array(mesh.vertices, dtype=np.float32)
    nrm = np.array(mesh.vertex_normals, dtype=np.float32)
    # Degenerate vertices can get a zero normal: Unity then lights their triangles black (the whale's dark
    # spots). Give them the average normal of their faces (or straight up).
    bad = np.linalg.norm(nrm, axis=1) < 0.5
    if bad.any():
        fn = np.array(mesh.face_normals, dtype=np.float32)
        for vi in np.where(bad)[0]:
            faces_of = [f for f in mesh.vertex_faces[vi] if f >= 0]
            v = fn[faces_of].sum(0) if faces_of else np.array([0, 1, 0], np.float32)
            nrm[vi] = v / (np.linalg.norm(v) + 1e-9) if np.linalg.norm(v) > 1e-6 else np.array([0, 1, 0], np.float32)
        print(f"  {name}: fixed {int(bad.sum())} zero-length normals")
    if yaw:
        a = np.radians(yaw)
        r = np.array([[np.cos(a), 0, np.sin(a)], [0, 1, 0], [-np.sin(a), 0, np.cos(a)]], dtype=np.float32)
        pos, nrm = pos @ r.T, nrm @ r.T
    if name in STRAIGHTEN:
        pos, nrm = straighten_tail(pos, nrm, STRAIGHTEN[name])
    if name == "whale":
        uv0 = np.array(mesh.visual.uv, dtype=np.float32)
        faces0 = np.array(mesh.faces, dtype=np.int32)
        before = pos.copy()
        pos, nrm, uv_override, faces_override, _ = rebuild_whale(pos, nrm, uv0, faces0)
        # flippers moved: recompute their normals
        moved = np.zeros(len(pos), bool)
        moved[:len(before)] = np.linalg.norm(pos[:len(before)] - before, axis=1) > 1e-6
        if moved.any():
            nrm[moved] = _vertex_normals(pos, faces_override)[moved]
        nrm = weld_normals(pos, nrm, np.ptp(pos[:, 2]))
    else:
        uv_override = faces_override = None
    if name in FLAT_FLUKES:
        # the flukes are a thin blade with few, large triangles: one vertex normal pointing down darkened a whole
        # fluke once it pitched. Give the blade a smooth, mostly upward normal.
        zn = (pos[:, 2] - pos[:, 2].min()) / np.ptp(pos[:, 2])
        f = zn < FLAT_FLUKES[name]
        n = nrm[f].copy()
        n[:, 1] = np.abs(n[:, 1])
        n = 0.25 * n + 0.75 * np.array([0, 1, 0], np.float32)
        nrm[f] = n / np.linalg.norm(n, axis=1, keepdims=True)
        print(f"  {name}: {int(f.sum())} fluke normals smoothed upward")
    uv = np.array(mesh.visual.uv, dtype=np.float32) if uv_override is None else uv_override   # OpenGL convention, like Unity
    faces = np.array(mesh.faces, dtype=np.int32) if faces_override is None else faces_override
    # paint first, from the original faces only (the back-side copies share their UVs)
    tex = paint_from_geometry(pos, nrm, uv, faces, PAINT[name]) if name in PAINT else None
    if name in CLEAN:
        pos, nrm, uv, faces = CLEAN[name](pos, nrm, uv, faces)
    pos[:, 0] *= -1                                          # right-handed glTF -> left-handed Unity
    nrm[:, 0] *= -1
    faces = faces[:, [0, 2, 1]]                              # keep faces pointing outwards after the mirror
    with open(out_dir / f"{name}.tam", "wb") as f:
        f.write(b"TAM1")
        f.write(struct.pack("<ii", len(pos), faces.size))
        f.write(pos.tobytes())
        f.write(nrm.tobytes())
        f.write(uv.tobytes())
        f.write(faces.astype("<i4").tobytes())
    if tex is None:
        tex = texture_of(mesh)
        if name in RECOLOR:
            tex = RECOLOR[name](tex)
    tex.save(out_dir / f"{name}.png")
    lo, hi = pos.min(0), pos.max(0)
    print(f"{name}: {len(pos)} verts, {len(faces)} tris, bounds {lo.round(3)} .. {hi.round(3)}")


if __name__ == "__main__":
    src, out = Path(sys.argv[1]), Path(sys.argv[2])
    out.mkdir(parents=True, exist_ok=True)
    for name, (glb, yaw) in MODELS.items():
        convert(src / glb, out, name, yaw)
