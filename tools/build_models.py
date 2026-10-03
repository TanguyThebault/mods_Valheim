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
    fin = (side > 0.22) & (z > 0.4) & (z < 0.82) & (y < 0.6)
    fin_col = white * (1 - 0.5 * np.clip(N[..., 1], 0, 1))[..., None] + slate * 0.5 * np.clip(N[..., 1], 0, 1)[..., None]
    col[fin] = fin_col[fin]
    # flukes: grey on top, white underneath with black mottling
    fl = z < 0.1
    fl_under = fl & (N[..., 1] < -0.45)
    col[fl & ~fl_under] = back[fl & ~fl_under]
    mott = _noise(P, 26.0, 3.0) > 0.72
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
    spots) and one-sided sheets for fins and flukes (black when seen from behind). Drop the fragments; give every
    part other than the main body a back side (reversed faces with flipped normals). The material then culls back
    faces, so each side shows its own, correctly lit face."""
    import trimesh
    g = trimesh.Trimesh(vertices=pos.copy(), faces=faces.copy(), process=False)
    g.merge_vertices(merge_tex=True, merge_norm=True, digits_vertex=4)      # weld UV seams to find real parts
    labels = trimesh.graph.connected_component_labels(g.face_adjacency, node_count=len(g.faces))
    sizes = np.bincount(labels)
    main = int(np.argmax(sizes))
    keep = sizes[labels] >= min_faces
    sheet = keep & (labels != main)
    kept = faces[keep]
    extra = faces[sheet]
    # duplicate the vertices of the sheets so the back side can have its own (flipped) normals
    used = np.unique(extra)
    remap = -np.ones(len(pos), dtype=np.int64)
    remap[used] = np.arange(len(used)) + len(pos)
    back = remap[extra][:, [0, 2, 1]]
    pos2 = np.concatenate([pos, pos[used]])
    nrm2 = np.concatenate([nrm, -nrm[used]])
    uv2 = np.concatenate([uv, uv[used]])
    faces2 = np.concatenate([kept, back]).astype(np.int32)
    print(f"  cleaned: dropped {int((~keep).sum())} faces in {int((sizes < min_faces).sum())} fragments, "
          f"back sides for {int(sheet.sum())} faces of {len(set(labels[sheet]))} sheets")
    return pos2.astype(np.float32), nrm2.astype(np.float32), uv2.astype(np.float32), faces2


CLEAN = {"whale": clean_shells}

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
    uv = np.array(mesh.visual.uv, dtype=np.float32)          # trimesh: OpenGL convention, like Unity
    faces = np.array(mesh.faces, dtype=np.int32)
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
