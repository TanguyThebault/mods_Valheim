"""Convert the generated weapon GLBs (fal Trellis 2) into the plugin's tiny mesh format + texture, with previews.

Per weapon: optional reshaping (a thicker mace haft, a longer spear shaft), decimation to at most twice the
triangles of the vanilla weapon it replaces (pymeshlab, UVs kept), then models/<name>.tam ('TAM1', int32 vertex
count, int32 index count, float32 positions xyz, normals xyz, uv xy, int32 indices; Unity's left-handed frame: x
mirrored, winding reversed) and models/<name>.png (512 px). Frame: length along +y, head up, grip down; blade width
along x; thickness along z (the sword and the knife were drawn upside down and are turned over).

With a previews folder and the vanilla meshes exported from the game (read-only, outside the repo), it also
replays the plugin's fitting (WeaponModels.cs, same maths, same per-weapon LengthScale / GripShift) and draws ours
over the vanilla weapon, with the hand.

    uv run --with trimesh --with numpy --with pillow --with scipy --with matplotlib --with networkx --with pymeshlab
        python tools/build_weapons.py [previews_dir vanilla_dir]
"""
import json
import os
import struct
import sys
import tempfile
from pathlib import Path

import numpy as np
import trimesh
from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(Path(__file__).resolve().parent))
GEN, OUT = ROOT / "assets" / "gen", ROOT / "models"

# name: glb, upside down, max triangles (2 x vanilla), vanilla prefab, reshaping, in-game fit (as in WeaponModels.cs)
WEAPONS = {
    "axe": dict(glb="axe_3d.glb", flip=False, tris=1000, vanilla="AxeIron"),
    "spear": dict(glb="spear2_3d.glb", flip=False, tris=560, vanilla="SpearWolfFang", stretch=(0.0, 2.6),
                  tex=dict(gamma=0.62, gain=1.2)),                 # was nearly black in game
    # the Wind Blade, redrawn lighter (Lekinox, v0.13.2): concept swordwind2_23 (slender pale blade, white grip, blue cord)
    "sword": dict(glb="swordwind2_3d.glb", flip=False, tris=610, vanilla="SwordBlackmetal", clean=0.02, upright=True,
                  tex=dict(tint=(0.9, 0.97, 1.1), gamma=0.9)),
    "mace": dict(glb="mace_3d.glb", flip=False, tris=770, vanilla="MaceBronze", thicken=(0.08, 1.7)),
    # the generated knife (knife_3d.glb) lost its blade at 460 triangles: built from code instead
    "knife": dict(procedural=True, flip=False, tris=460, vanilla="KnifeCopper"),
    # the Mistlands fog horn: concept horn_11 (curved horn, iron bands, bronze mouthpiece); loose strap dropped
    "horn": dict(glb="horn_3d.glb", flip=False, tris=400, vanilla="Club", clean=0.04, upright=True,
                 tex=dict(blue_to=(0.78, 0.7, 0.56))),             # stray blue patches on the bone
    # (a rod model was built here, rod_3d.glb with clean=0.04 and upright=True; Lekinox kept the vanilla rod's look)
    # Surtrbrand, the Ashlands greatsword: concept surtr_29 (black volcanic blade, magma cracks, flame guard)
    "surtr": dict(glb="surtr_3d.glb", flip=False, tris=1020, vanilla="THSwordSlayer", clean=0.02, upright=True,
                  tex=dict(gamma=0.8, emit=True)),                 # dark steel lifted a little; the magma glows
    # Ymir's Bite, the Deep North atgeir: concept ymirbone_7 (a giant's bone shaft, glacier-ice blade, bone hook);
    # the first one (ymir_42, wood and fur) had a patchy texture
    "ymir": dict(glb="ymirbone_3d.glb", flip=False, tris=2890, vanilla="AtgeirGold", clean=0.02, upright=True),
}
# name -> (length scale, grip shift, head towards the hand); keep in sync with WeaponModels.cs
FIT = {"axe": (1.0, 0.08, False), "sword": (1.0, 0.03, False), "mace": (1.0, 0.1, False), "knife": (1.45, 0.08, False),
       "spear": (1.0, 0.0, True), "horn": (0.5, 0.0, False), "surtr": (1.0, 0.13, False), "ymir": (1.0, 0.0, False)}


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


def fix_texture(name, tex, cfg):
    """Per-weapon texture touch-ups (cfg["tex"]): gamma/gain to lift a texture too dark in game, bluish stray
    patches recoloured (luminance kept), and an emission mask of the hot orange parts (models/<name>_emit.png)."""
    t = cfg.get("tex")
    if not t:
        return tex
    a = np.asarray(tex, dtype=np.float32) / 255.0
    if "blue_to" in t:
        blue = (a[..., 2] > a[..., 0] + 0.06) & (a[..., 2] > a[..., 1])
        lum = a @ np.array([0.299, 0.587, 0.114], dtype=np.float32)
        target = np.array(t["blue_to"], dtype=np.float32)
        tl = float(target @ np.array([0.299, 0.587, 0.114]))
        a[blue] = np.clip(target[None, :] * (lum[blue] / tl)[:, None], 0, 1)
        print(f"  {name}: {blue.mean() * 100:.1f} % bluish pixels recoloured")
    if t.get("emit"):
        r, g, b = a[..., 0], a[..., 1], a[..., 2]
        hot = np.clip((r - b - 0.25) * 3.0, 0, 1) * np.clip((r - 0.45) * 3.0, 0, 1)
        Image.fromarray((np.stack([hot] * 3, -1) * a * 255).astype(np.uint8)).save(OUT / f"{name}_emit.png")
        print(f"  {name}: emission mask, {(hot > 0.2).mean() * 100:.1f} % hot")
    a = np.clip(a ** t.get("gamma", 1.0) * t.get("gain", 1.0) * np.array(t.get("tint", (1, 1, 1)), dtype=np.float32), 0, 1)
    return Image.fromarray((a * 255).astype(np.uint8))


def thicken(pos, below, factor):
    """A thicker haft: x and z scaled round the haft's axis, fully below `below` (normalised y), easing out above."""
    y = (pos[:, 1] - pos[:, 1].min()) / np.ptp(pos[:, 1]) - 0.5
    low = pos[y < below - 0.1]
    cx, cz = np.median(low[:, 0]), np.median(low[:, 2])
    w = np.clip((below - y) / 0.06, 0, 1)
    f = 1 + (factor - 1) * w
    pos = pos.copy()
    pos[:, 0] = cx + (pos[:, 0] - cx) * f
    pos[:, 2] = cz + (pos[:, 2] - cz) * f
    return pos


def clean(mesh, share):
    """Drops loose pieces: connected parts (by position, UV seams welded) with fewer than `share` of the faces."""
    welded = mesh.copy()
    welded.merge_vertices(merge_tex=True, merge_norm=True)
    comps = trimesh.graph.connected_components(welded.face_adjacency, nodes=np.arange(len(welded.faces)))
    total = len(mesh.faces)
    keep = [c for c in comps if len(c) >= share * total]
    faces = np.sort(np.concatenate(keep))
    print(f"  clean: kept {len(keep)} of {len(comps)} parts, {len(faces)} of {total} faces")
    return mesh.submesh([faces], append=True)


def upright(pos):
    """Rotates the model so its principal (length) axis is y, head still up."""
    c = pos.mean(0)
    w, v = np.linalg.eigh(np.cov((pos - c).T))
    axis = v[:, np.argmax(w)]
    if axis[1] < 0:
        axis = -axis
    rot = trimesh.geometry.align_vectors(axis, [0, 1, 0])[:3, :3]
    return ((pos - c) @ rot.T + c).astype(np.float32)


def stretch(pos, split, factor):
    """A longer shaft: everything below `split` (normalised y) spread out downwards by `factor`."""
    y0, L = pos[:, 1].min(), np.ptp(pos[:, 1])
    ys = y0 + (split + 0.5) * L
    pos = pos.copy()
    below = pos[:, 1] < ys
    pos[below, 1] = ys - (ys - pos[below, 1]) * factor
    return pos


def decimate(mesh, tex, target, merge=0.05, quality=0.3, tcw=0.5, planar=False):
    """Quadric edge collapse keeping the texture coordinates (pymeshlab)."""
    import pymeshlab
    with tempfile.TemporaryDirectory() as d:
        d = Path(d)
        tex.save(d / "tex.png")
        m = trimesh.Trimesh(mesh.vertices, mesh.faces, process=False,
                            visual=trimesh.visual.TextureVisuals(uv=mesh.visual.uv, image=tex))
        m.export(d / "in.obj")
        ms = pymeshlab.MeshSet()
        ms.load_new_mesh(str(d / "in.obj"))
        # the OBJ duplicates vertices along UV seams: weld them back (UVs stay per corner), or the seams stall
        # the collapse and tear the surface
        ms.apply_filter("meshing_merge_close_vertices", threshold=pymeshlab.PercentageValue(merge))
        # Trellis cuts its UVs into many small islands: their seams stop a single pass early. Several passes,
        # letting seam (boundary) edges collapse too, with less weight on the texture coordinates.
        for _ in range(8):
            if ms.current_mesh().face_number() <= target:
                break
            ms.apply_filter("meshing_decimation_quadric_edge_collapse_with_texture", targetfacenum=int(target),
                            qualitythr=quality, preserveboundary=True, extratcoordw=tcw, optimalplacement=True,
                            planarquadric=planar)
        ms.save_current_mesh(str(d / "out.obj"))
        out = trimesh.load(d / "out.obj", process=False)
        if isinstance(out, trimesh.Scene):
            out = trimesh.util.concatenate(list(out.geometry.values()))
    return out


def fit(pos, vb_center, vb_ext, hand, length_scale, grip_shift, flip=False):
    """WeaponModels.Apply's mapping, in the vanilla mesh's space (Unity frame)."""
    e = vb_ext
    k = int(np.argmax(e))
    j = max([i for i in range(3) if i != k], key=lambda i: e[i])
    l = 3 - k - j
    y0, L = pos[:, 1].min(), np.ptp(pos[:, 1])
    grip = pos[pos[:, 1] < y0 + 0.25 * L]
    head = pos[pos[:, 1] > y0 + 0.6 * L]
    gx, gz = grip[:, 0].mean(), grip[:, 2].mean()
    blade = head[:, 0].mean() - gx
    s = 1.0 if vb_center[k] - hand[k] >= 0 else -1.0
    if flip:
        s = -s
    sj = 1.0 if (vb_center[j] - hand[j]) * blade >= 0 else -1.0
    ax = np.eye(3)
    sl = np.sign(np.dot(np.cross(ax[j] * sj, ax[k] * s), ax[l]))
    scale = 2 * e[k] / L * length_scale
    grip_end = vb_center[k] - s * e[k] - s * grip_shift * 2 * e[k]
    M = np.stack([ax[j] * sj * scale, ax[k] * s * scale, ax[l] * sl * scale], axis=1)
    origin = ax[k] * grip_end + ax[j] * hand[j] + ax[l] * hand[l] - M @ np.array([gx, y0, gz])
    return pos @ M.T + origin, (k, j)


def quat_inv_rotate(q, v):
    x, y, z, w = q
    r = trimesh.transformations.quaternion_matrix([w, x, y, z])[:3, :3]
    return r.T @ v


def fit_preview(path, name, pos, faces, col, vanilla_dir):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    from matplotlib.collections import PolyCollection
    report = json.loads((vanilla_dir / "report.json").read_text())
    vname = WEAPONS[name]["vanilla"]
    entry = report[vname][0]
    vm = trimesh.load(vanilla_dir / Path(entry["obj"]).name, process=False)
    vpos = np.array(vm.vertices, dtype=np.float64)
    vpos[:, 0] *= -1                                   # UnityPy's OBJ export mirrors x back to right-handed
    vb_min, vb_max = vpos.min(0), vpos.max(0)
    c = entry["chain"][-1]
    hand = quat_inv_rotate(c["rot"], -np.array(c["pos"])) / np.array(c["scale"])
    ls, gs, fl = FIT.get(name, (1.0, 0.0, False))
    fitted, (k, j) = fit(pos.astype(np.float64), (vb_min + vb_max) / 2, (vb_max - vb_min) / 2, hand, ls, gs, fl)
    fig, ax = plt.subplots(figsize=(4, 8))
    vt = vpos[np.array(vm.faces)]
    ax.add_collection(PolyCollection(vt[:, :, [j, k]], facecolors=(0.2, 0.2, 0.2, 0.25), edgecolors="none"))
    tri = fitted[faces]
    order = np.argsort(tri[:, :, 3 - k - j].mean(1))
    ax.add_collection(PolyCollection(tri[order][:, :, [j, k]], facecolors=np.clip(col[order], 0, 1), edgecolors="none", alpha=0.85))
    ax.plot([hand[j]], [hand[k]], "r+", markersize=18, mew=3)
    allp = np.vstack([fitted[:, [j, k]], vpos[:, [j, k]]])
    ax.set_xlim(allp[:, 0].min() - 0.05, allp[:, 0].max() + 0.05)
    ax.set_ylim(allp[:, 1].min() - 0.05, allp[:, 1].max() + 0.05)
    ax.set_aspect("equal")
    ax.set_title(f"{name} on {vname} (grey), hand +")
    fig.tight_layout()
    fig.savefig(path, dpi=80)
    plt.close(fig)


def face_colours(uv, faces, tex):
    t = np.asarray(tex, dtype=np.float32) / 255.0
    h, w = t.shape[:2]
    cuv = uv[faces].mean(1)
    return t[np.clip(((1 - cuv[:, 1]) * (h - 1)).astype(int), 0, h - 1), np.clip((cuv[:, 0] * (w - 1)).astype(int), 0, w - 1)]


def convert(name, cfg, previews, vanilla_dir, tex_only=False):
    if cfg.get("procedural"):
        import knife_procedural
        mesh = knife_procedural.build()
    else:
        mesh = load(GEN / cfg["glb"])
    tex = texture_of(mesh)
    if tex_only:
        OUT.mkdir(exist_ok=True)
        fix_texture(name, tex, cfg).save(OUT / f"{name}.png")
        print(f"{name}: texture only")
        return
    if "clean" in cfg:
        uv0 = mesh.visual.uv
        mesh = trimesh.Trimesh(mesh.vertices, mesh.faces, process=False, visual=trimesh.visual.TextureVisuals(uv=uv0, image=tex))
        mesh = clean(mesh, cfg["clean"])
    pos = np.array(mesh.vertices, dtype=np.float32)
    if cfg.get("upright"):
        pos = upright(pos)
    if cfg["flip"]:                                    # half a turn around z: head up, grip down
        pos[:, :2] *= -1
    if "thicken" in cfg:
        pos = thicken(pos, *cfg["thicken"])
    if "stretch" in cfg:
        pos = stretch(pos, *cfg["stretch"])
    mesh = trimesh.Trimesh(pos, mesh.faces, process=False, visual=trimesh.visual.TextureVisuals(uv=mesh.visual.uv, image=tex))
    before = len(mesh.faces)
    if before > cfg["tris"]:
        mesh = decimate(mesh, tex, cfg["tris"], **cfg.get("dec", {}))
        trimesh.repair.fix_normals(mesh, multibody=True)
    pos = np.array(mesh.vertices, dtype=np.float32)
    nrm = np.array(mesh.vertex_normals, dtype=np.float32)
    nrm[np.linalg.norm(nrm, axis=1) < 0.5] = [0, 1, 0]
    uv = np.array(mesh.visual.uv, dtype=np.float32)
    faces = np.array(mesh.faces, dtype=np.int32)
    pos[:, 0] *= -1                                    # right-handed glTF -> left-handed Unity
    nrm[:, 0] *= -1
    faces_u = faces[:, [0, 2, 1]]
    OUT.mkdir(exist_ok=True)
    with open(OUT / f"{name}.tam", "wb") as f:
        f.write(b"TAM1")
        f.write(struct.pack("<ii", len(pos), faces_u.size))
        f.write(pos.tobytes())
        f.write(nrm.tobytes())
        f.write(uv.tobytes())
        f.write(faces_u.astype("<i4").tobytes())
    fix_texture(name, tex, cfg).save(OUT / f"{name}.png")
    print(f"{name}: {before} -> {len(faces)} tris (max {cfg['tris']}), {len(pos)} verts")
    if previews and vanilla_dir:
        fit_preview(previews / f"fit_{name}.png", name, pos, faces_u, face_colours(uv, faces, tex), vanilla_dir)


if __name__ == "__main__":
    only = None
    if "--only" in sys.argv:                           # --only horn,axe: rebuild just those (decimation isn't stable)
        i = sys.argv.index("--only")
        only = set(sys.argv[i + 1].split(","))
        del sys.argv[i:i + 2]
    tex_only = "--tex-only" in sys.argv                 # rewrite the textures, keep the meshes as they are
    if tex_only:
        sys.argv.remove("--tex-only")
    previews = Path(sys.argv[1]) if len(sys.argv) > 1 else None
    vanilla_dir = Path(sys.argv[2]) if len(sys.argv) > 2 else None
    if previews:
        previews.mkdir(parents=True, exist_ok=True)
    for name, cfg in WEAPONS.items():
        if only is None or name in only:
            convert(name, cfg, previews, vanilla_dir, tex_only)
