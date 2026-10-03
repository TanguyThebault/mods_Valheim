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


RECOLOR = {"whale": slate}


def texture_of(mesh):
    mat = mesh.visual.material
    img = getattr(mat, "baseColorTexture", None) or getattr(mat, "image", None)
    img = img.convert("RGB")
    if max(img.size) > 512:
        img = img.resize((512, 512), Image.LANCZOS)
    return img


def convert(src, out_dir, name, yaw=0):
    mesh = load(src)
    pos = np.array(mesh.vertices, dtype=np.float32)
    nrm = np.array(mesh.vertex_normals, dtype=np.float32)
    if yaw:
        a = np.radians(yaw)
        r = np.array([[np.cos(a), 0, np.sin(a)], [0, 1, 0], [-np.sin(a), 0, np.cos(a)]], dtype=np.float32)
        pos, nrm = pos @ r.T, nrm @ r.T
    uv = np.array(mesh.visual.uv, dtype=np.float32)          # trimesh: OpenGL convention, like Unity
    faces = np.array(mesh.faces, dtype=np.int32)
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
