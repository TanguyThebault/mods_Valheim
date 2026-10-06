"""Convert the generated models of the new fauna (fal Trellis 2) into the mod's tiny mesh format, with previews.

Per animal: loose shards dropped, turned to the mod's frame (y up, facing +z, the animal's left towards -x),
decimated to at most twice the triangles of the vanilla mesh it stands in for (pymeshlab, UVs kept), then
assets/models/<name>.tam + .png (the format of build_models.py: 'TAM1', counts, positions, normals, uvs, indices;
Unity's left-handed frame: x mirrored, winding reversed). Previews: side, front and top views on green.

    uv run --with trimesh --with numpy --with pillow --with scipy --with matplotlib --with networkx --with pymeshlab
        python tools/build_fauna.py [previews_dir] [only_name]
"""
import struct
import sys
import tempfile
from pathlib import Path

import numpy as np
import trimesh
from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
GEN, OUT = ROOT / "assets" / "gen", ROOT / "assets" / "models"

# name: glb, yaw (degrees about y so the animal faces +z), max triangles (2 x the vanilla mesh: Hare 2164,
# Deer 2418, Wolf 1962, Crow 394 + 280), loose-shard threshold (share of faces)
FAUNA = {
    "frog": dict(glb="frog_3d.glb", yaw=0, tris=4300, clean=0.02),
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


def texture_of(mesh):
    mat = mesh.visual.material
    img = getattr(mat, "baseColorTexture", None) or getattr(mat, "image", None)
    img = img.convert("RGB")
    return img.resize((512, 512), Image.LANCZOS) if max(img.size) > 512 else img


def clean(mesh, share):
    """Drops loose pieces: connected parts (by position, UV seams welded) with fewer than `share` of the faces."""
    welded = mesh.copy()
    welded.merge_vertices(merge_tex=True, merge_norm=True)
    comps = trimesh.graph.connected_components(welded.face_adjacency, nodes=np.arange(len(welded.faces)))
    keep = [c for c in comps if len(c) >= share * len(mesh.faces)]
    faces = np.sort(np.concatenate(keep))
    print(f"  clean: kept {len(keep)} of {len(comps)} parts, {len(faces)} of {len(mesh.faces)} faces")
    return mesh.submesh([faces], append=True)


def decimate(mesh, tex, target):
    """Quadric edge collapse keeping the texture coordinates, after welding the UV-seam duplicates."""
    import pymeshlab
    with tempfile.TemporaryDirectory() as d:
        d = Path(d)
        tex.save(d / "tex.png")
        trimesh.Trimesh(mesh.vertices, mesh.faces, process=False,
                        visual=trimesh.visual.TextureVisuals(uv=mesh.visual.uv, image=tex)).export(d / "in.obj")
        ms = pymeshlab.MeshSet()
        ms.load_new_mesh(str(d / "in.obj"))
        ms.apply_filter("meshing_merge_close_vertices", threshold=pymeshlab.PercentageValue(0.05))
        for _ in range(8):
            if ms.current_mesh().face_number() <= target:
                break
            ms.apply_filter("meshing_decimation_quadric_edge_collapse_with_texture", targetfacenum=int(target),
                            qualitythr=0.3, preserveboundary=True, extratcoordw=0.5, optimalplacement=True)
        ms.save_current_mesh(str(d / "out.obj"))
        out = trimesh.load(d / "out.obj", process=False)
        if isinstance(out, trimesh.Scene):
            out = trimesh.util.concatenate(list(out.geometry.values()))
    return out


def preview(path, pos, faces, uv, tex):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    from matplotlib.collections import PolyCollection
    t = np.asarray(tex, dtype=np.float32) / 255.0
    h, w = t.shape[:2]
    cuv = uv[faces].mean(1)
    col = t[np.clip(((1 - cuv[:, 1]) * (h - 1)).astype(int), 0, h - 1), np.clip((cuv[:, 0] * (w - 1)).astype(int), 0, w - 1)]
    tri = pos[faces]
    n = np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0])
    n /= np.linalg.norm(n, axis=1, keepdims=True) + 1e-9
    fig, axes = plt.subplots(1, 3, figsize=(13, 4.5))
    # side (from +x: z to the right... the animal faces +z), front (from +z), top (from +y)
    for ax, (a, b, d, sign, title) in zip(axes, [(2, 1, 0, 1, "side (faces right = +z)"), (0, 1, 2, 1, "front (from +z)"),
                                                (0, 2, 1, 1, "top (head up = +z)")]):
        order = np.argsort(sign * tri[:, :, d].mean(1))
        shade = 0.45 + 0.55 * np.abs(n[:, d])
        ax.add_collection(PolyCollection(tri[order][:, :, [a, b]], facecolors=np.clip(col[order] * shade[order, None], 0, 1), edgecolors="none"))
        ax.set_xlim(pos[:, a].min() - 0.05, pos[:, a].max() + 0.05)
        ax.set_ylim(pos[:, b].min() - 0.05, pos[:, b].max() + 0.05)
        ax.set_aspect("equal")
        ax.set_facecolor((0.2, 0.75, 0.3))
        ax.set_title(title)
    fig.tight_layout()
    fig.savefig(path, dpi=80)
    plt.close(fig)


def convert(name, cfg, previews):
    mesh = load(GEN / cfg["glb"])
    tex = texture_of(mesh)
    mesh = trimesh.Trimesh(mesh.vertices, mesh.faces, process=False, visual=trimesh.visual.TextureVisuals(uv=mesh.visual.uv, image=tex))
    if cfg.get("clean"):
        mesh = clean(mesh, cfg["clean"])
    before = len(mesh.faces)
    if before > cfg["tris"]:
        mesh = decimate(mesh, tex, cfg["tris"])
    trimesh.repair.fix_normals(mesh, multibody=True)
    pos = np.array(mesh.vertices, dtype=np.float32)
    nrm = np.array(mesh.vertex_normals, dtype=np.float32)
    nrm[np.linalg.norm(nrm, axis=1) < 0.5] = [0, 1, 0]
    if cfg.get("yaw"):
        a = np.radians(cfg["yaw"])
        r = np.array([[np.cos(a), 0, np.sin(a)], [0, 1, 0], [-np.sin(a), 0, np.cos(a)]], dtype=np.float32)
        pos, nrm = pos @ r.T, nrm @ r.T
    uv = np.array(mesh.visual.uv, dtype=np.float32)
    faces = np.array(mesh.faces, dtype=np.int32)
    if previews:
        preview(previews / f"{name}.png", pos, faces, uv, tex)
    pos[:, 0] *= -1                                    # right-handed glTF -> left-handed Unity
    nrm[:, 0] *= -1
    faces_u = faces[:, [0, 2, 1]]
    OUT.mkdir(parents=True, exist_ok=True)
    with open(OUT / f"{name}.tam", "wb") as f:
        f.write(b"TAM1")
        f.write(struct.pack("<ii", len(pos), faces_u.size))
        f.write(pos.tobytes())
        f.write(nrm.tobytes())
        f.write(uv.tobytes())
        f.write(faces_u.astype("<i4").tobytes())
    tex.save(OUT / f"{name}.png")
    print(f"{name}: {before} -> {len(faces)} tris (max {cfg['tris']}), {len(pos)} verts, bounds {pos.min(0).round(3)} .. {pos.max(0).round(3)}")


if __name__ == "__main__":
    previews = Path(sys.argv[1]) if len(sys.argv) > 1 else None
    only = sys.argv[2] if len(sys.argv) > 2 else None
    if previews:
        previews.mkdir(parents=True, exist_ok=True)
    for name, cfg in FAUNA.items():
        if only is None or only == name:
            convert(name, cfg, previews)
