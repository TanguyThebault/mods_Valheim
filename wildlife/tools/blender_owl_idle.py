"""Rig the perched owl, author its idle clips, export them for the mod, and render previews (headless Blender).

usage: uv run --project ~/tools/blender-env python tools/blender_owl_idle.py assets/models/owl_perched.tam
         [--out assets/models/owl_perched.rig] [--preview <dir>] [--size 384]

Everything is computed in the .tam's own space (the game's: x side, y up, z forward, height 1) with the same maths
as Unity: bones with identity bind rotations, local pose = T(offset) R(Euler ZXY like Quaternion.Euler) S, linear
blend skinning with bindpose T(-head). The preview deforms the mesh with exactly that, so what is checked here is
what the game shows.

  * skeleton: Root (feet), Body (hips), Chest, Neck, Head, WingL/WingR (shoulders), Tail;
  * skin: smooth geometric regions (feet on Root, a neck band blending Chest -> Neck -> Head, folded wings on the
    flanks, tail at the lower back), 4 influences, normalised;
  * clips (30 fps): breathe (loop), look (snappy head swivel and hold; the game scales and mirrors it), tilt
    (curious head tilt), bob (the owl's sideways head bob that judges distance), ruffle (feathers puffed and shaken);
  * export: RIG1 bones + weights (as the swimmers) and `clip` blocks: `key <bone> <channel> <values...>` with
    channels rx ry rz (degrees), px py pz (model units), s (uniform scale);
  * QA: per clip, the worst triangle edge stretch and squash against the rest pose;
  * preview: a demo sequence from 3 views, MP4 + contact sheets (ffmpeg).
"""
import json
import math
import struct
import subprocess
import sys
from pathlib import Path

import numpy as np

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
tam = Path(args[0]).resolve()


def opt(name, default, cast=str):
    return cast(args[args.index(name) + 1]) if name in args else default


out_rig = Path(opt("--out", str(tam.with_suffix(".rig")))).resolve()
preview = Path(opt("--preview", "")).resolve() if "--preview" in args else None
size = opt("--size", 384, int)
FPS = 30
FFMPEG = __import__("os").environ.get("UM_FFMPEG_WIN", "ffmpeg")

# ---------------------------------------------------------------- mesh (.tam, game space)
data = tam.read_bytes()
nv, ni = struct.unpack("<ii", data[4:12])
o = 12
P = np.frombuffer(data, "<f4", nv * 3, o).reshape(-1, 3).astype(np.float64); o += nv * 24
UV = np.frombuffer(data, "<f4", nv * 2, o).reshape(-1, 2); o += nv * 8
IDX = np.frombuffer(data, "<i4", ni, o).reshape(-1, 3)
lo, hi = P.min(0), P.max(0)
H = hi[1] - lo[1]
f = (P[:, 1] - lo[1]) / H                     # 0 feet .. 1 top of the head
ax = np.abs(P[:, 0])
z = P[:, 2]


def at(fy, x=0.0, zz=0.0):
    return np.array([x, lo[1] + fy * H, zz])


def ss(x, a, b):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


def mid_z(fy, half=0.03):
    sl = P[np.abs(f - fy) < half]
    return (sl[:, 2].min() + sl[:, 2].max()) / 2


def side_x(fy, half=0.04):
    sl = P[np.abs(f - fy) < half]
    return sl[:, 0].max()


# ---------------------------------------------------------------- skeleton
shoulder = 0.60
BONES = [
    ("Root", None, at(0.0, 0, mid_z(0.05))),
    ("Body", "Root", at(0.14, 0, mid_z(0.14))),
    ("Chest", "Body", at(0.40, 0, mid_z(0.40))),
    ("Neck1", "Chest", at(0.6, 0, mid_z(0.62))),
    ("Neck2", "Neck1", at(0.65, 0, mid_z(0.665))),
    ("Neck3", "Neck2", at(0.7, 0, mid_z(0.715))),
    ("Head", "Neck3", at(0.75, 0, mid_z(0.80))),
    ("WingL", "Chest", at(shoulder, side_x(shoulder) * 0.75, mid_z(shoulder) - 0.04)),
    ("WingR", "Chest", at(shoulder, -side_x(shoulder) * 0.75, mid_z(shoulder) - 0.04)),
    ("Tail", "Body", at(0.16, 0, P[np.abs(f - 0.16) < 0.05][:, 2].min() + 0.03)),
]
NAMES = [b[0] for b in BONES]
PARENT = [NAMES.index(b[1]) if b[1] else -1 for b in BONES]
HEAD = np.array([b[2] for b in BONES])
print("bones", {n: np.round(HEAD[i], 3).tolist() for i, n in enumerate(NAMES)})

# ---------------------------------------------------------------- skin (smooth regions)
W = np.zeros((nv, len(NAMES)))
w_root = 1 - ss(f, 0.04, 0.11)
rest = 1 - w_root
# folded wings: flanks and back between the hips and the shoulders, not the breast
zc = np.array([mid_z(min(max(v, 0.03), 0.97)) for v in f])
wing = ss(ax, 0.10, 0.19) * ss(f, 0.15, 0.30) * (1 - ss(f, 0.5, 0.58)) * (1 - ss(z - zc, 0.02, 0.12)) * 0.9
tail = ss(-(z - zc), 0.12, 0.20) * (1 - ss(f, 0.20, 0.32)) * 0.95
wing = np.minimum(wing, 1 - tail)
w_wing = rest * wing
w_tail = rest * tail
rest2 = rest - w_wing - w_tail
# the trunk: smooth blending along the height between consecutive joints, so a head turn is shared out over
# Chest -> Neck1 -> Neck2 -> Head and no single band of skin twists far (linear skinning collapses big twists)
chain = [("Body", 0.14), ("Chest", 0.44), ("Neck1", 0.6), ("Neck2", 0.655), ("Neck3", 0.71), ("Head", 0.79)]
for k, (n, c) in enumerate(chain):
    lo_c = chain[k - 1][1] if k > 0 else -1.0
    hi_c = chain[k + 1][1] if k + 1 < len(chain) else 2.0
    up = ss(f, lo_c, c) if k > 0 else np.ones(nv)
    down = 1 - ss(f, c, hi_c) if k + 1 < len(chain) else np.ones(nv)
    W[:, NAMES.index(n)] = rest2 * up * down
W[:, NAMES.index("Root")] = w_root
W[:, NAMES.index("Tail")] = w_tail
W[:, NAMES.index("WingL")] = np.where(P[:, 0] > 0, w_wing, 0)
W[:, NAMES.index("WingR")] = np.where(P[:, 0] <= 0, w_wing, 0)
# top 4, normalised
order = np.argsort(-W, axis=1)[:, :4]
W4 = np.take_along_axis(W, order, 1)
W4 /= W4.sum(1, keepdims=True)
WF = np.zeros_like(W)
np.put_along_axis(WF, order, W4, 1)
print("vertices per main bone", {n: int((WF.argmax(1) == i).sum()) for i, n in enumerate(NAMES)})


# ---------------------------------------------------------------- clips
def keys(points, dur, ease="smooth"):
    """Sample keyframes [(t, v), ...] at FPS over dur seconds, smoothstep between keys."""
    n = int(round(dur * FPS))
    t = np.arange(n) / FPS
    ts = np.array([p[0] for p in points])
    vs = np.array([p[1] for p in points], dtype=float)
    out = np.empty(n)
    for i, x in enumerate(t):
        j = np.searchsorted(ts, x, side="right") - 1
        if j < 0:
            out[i] = vs[0]
        elif j >= len(ts) - 1:
            out[i] = vs[-1]
        else:
            u = (x - ts[j]) / (ts[j + 1] - ts[j])
            if ease == "smooth":
                u = u * u * (3 - 2 * u)
            out[i] = vs[j] + (vs[j + 1] - vs[j]) * u
    return out


def snap(points, dur):
    """Owl head moves: fast out (ease-out), long holds. Keys [(t, v)] with ease-out cubic between them."""
    n = int(round(dur * FPS))
    t = np.arange(n) / FPS
    out = np.empty(n)
    for i, x in enumerate(t):
        j = max(0, min(len(points) - 2, int(np.searchsorted([p[0] for p in points], x, side="right") - 1)))
        (t0, v0), (t1, v1) = points[j], points[j + 1]
        u = min(max((x - t0) / (t1 - t0), 0), 1)
        u = 1 - (1 - u) ** 3
        out[i] = v0 + (v1 - v0) * u
    return out


CLIPS = {}


def clip(name, dur, loop, chans):
    n = int(round(dur * FPS))
    CLIPS[name] = {"frames": n, "loop": loop, "keys": {k: np.asarray(v, float)[:n] for k, v in chans.items()}}


tt = lambda dur: np.arange(int(round(dur * FPS))) / FPS  # noqa: E731

# breathe: a slow swell of the chest, the head stays level
d = 3.2
ph = 2 * np.pi * tt(d) / d
clip("breathe", d, True, {
    ("Chest", "s"): 1 + 0.012 * np.sin(ph),
    ("Body", "rx"): 0.8 * np.sin(ph),
    ("Head", "rx"): -0.8 * np.sin(ph),
    ("WingL", "rz"): 0.8 * np.sin(ph), ("WingR", "rz"): -0.8 * np.sin(ph),
})

# look: swivel to 90 degrees in ~0.3 s with a small overshoot, a live hold (sharp micro-snaps of the head at
# irregular times), swivel back. Owls turn from the neck: the shoulders follow by under 10 degrees.
d = 3.0
yaw = 0.88 * snap([(0, 0), (0.08, 0), (0.36, 95), (0.5, 90), (0.9, 90), (1.0, 84), (1.55, 84), (1.65, 92), (2.05, 92),
            (2.15, 88), (2.45, 88), (2.75, 0), (3.0, 0)], d)
nod = snap([(0, 0), (0.36, -3), (0.9, -3), (1.0, 3), (1.55, 3), (1.65, -2), (2.15, -2), (2.25, 2), (2.45, 2),
            (2.75, 0), (3.0, 0)], d)
tilt = snap([(0, 0), (0.36, 4), (1.65, 4), (1.75, 9), (2.45, 9), (2.75, 0), (3.0, 0)], d)
clip("look", d, False, {
    ("Body", "ry"): 0.03 * yaw, ("Chest", "ry"): 0.07 * yaw, ("Neck1", "ry"): 0.14 * yaw, ("Neck2", "ry"): 0.22 * yaw,
    ("Neck3", "ry"): 0.24 * yaw, ("Head", "ry"): 0.30 * yaw,
    ("Head", "rx"): nod, ("Head", "rz"): 0.7 * tilt, ("Neck3", "rz"): 0.3 * tilt,
})

# tilt: a clear curious tilt (about 38 degrees, mostly the head), hold with a small re-tilt, a counter-tilt, back
d = 2.6
roll = snap([(0, 0), (0.1, 0), (0.42, 32), (0.95, 28), (1.05, 34), (1.6, 32), (1.8, -6), (2.2, 0), (2.6, 0)], d)
clip("tilt", d, False, {
    ("Head", "rz"): 0.45 * roll, ("Neck3", "rz"): 0.25 * roll, ("Neck2", "rz"): 0.18 * roll, ("Neck1", "rz"): 0.12 * roll,
    ("Head", "ry"): 0.08 * roll,
})

# bob: three distinct sideways strokes of the head (about half a head wide) with pauses, a little up and down;
# the head counter-rolls so the eyes stay level on the target
d = 2.6
strokes = [(0.15, 1), (0.75, -1), (1.35, 1), (1.95, -1)]
x = tt(d)
side = np.zeros_like(x)
lift = np.zeros_like(x)
fore = np.zeros_like(x)
for t0, sg in strokes:              # quick strokes (0.33 s) with short pauses; the head traces a small loop
    u = np.clip((x - t0) / 0.33, 0, 1)
    on = (u > 0) & (u < 1)
    side += sg * np.sin(np.pi * u) * on
    lift += np.sin(2 * np.pi * u) * on
    fore += sg * np.sin(np.pi * u - np.pi / 2) * on
side = side * 0.045
lift = lift * 0.02
fore = fore * 0.015
clip("bob", d, False, {
    ("Head", "px"): side, ("Neck3", "px"): 0.4 * side, ("Neck2", "px"): 0.15 * side,
    ("Head", "py"): lift, ("Neck3", "py"): 0.3 * lift, ("Head", "pz"): fore,
    ("Head", "rz"): -120 * side, ("Neck3", "rz"): 60 * side,
})

# ruffle: feathers puff up (the head keeps its size), a shake runs from the head down to the body in roll, the wings
# lift and settle, a sharp tail flick
d = 2.2
x = tt(d)
puff = keys([(0, 0), (0.3, 1), (1.2, 1), (1.7, 0), (2.2, 0)], d)


def shake(delay):
    return np.sin(2 * np.pi * 8.5 * (x - delay)) * keys([(0, 0), (0.35 + delay, 0), (0.45 + delay, 1), (0.95 + delay, 0.6),
                                                       (1.3 + delay, 0), (2.2, 0)], d)


wl = keys([(0, 0), (0.35, 11), (1.1, 9), (1.6, 0), (2.2, 0)], d)
flick = snap([(0, 0), (1.2, 0), (1.28, 14), (1.45, 0), (2.2, 0)], d)
clip("ruffle", d, False, {
    # the swell fades up the neck (chest 16 %, then 10 % and 6 % cumulated), no hard step at the seam
    ("Chest", "s"): 1 + 0.16 * puff, ("Neck1", "s"): (1 + 0.10 * puff) / (1 + 0.16 * puff),
    ("Neck2", "s"): (1 + 0.06 * puff) / (1 + 0.10 * puff),
    ("Head", "rz"): 10 * shake(0.0), ("Neck3", "rz"): 6 * shake(0.03),
    ("Chest", "rz"): 4 * shake(0.08), ("Body", "rz"): 2.5 * shake(0.11),
    ("WingL", "rz"): wl + 3 * shake(0.1), ("WingR", "rz"): -wl + 3 * shake(0.1),
    ("Tail", "rx"): 5 * puff + flick, ("Tail", "ry"): 6 * shake(0.12),
})


def sample(name, frame, chan_scale=1.0, mirror=False):
    """Pose of a clip at a frame: {(bone, channel): value}. Mirror = left/right swapped (negated yaw/roll/x)."""
    c = CLIPS[name]
    out = {}
    for (b, ch), v in c["keys"].items():
        val = v[frame % c["frames"] if c["loop"] else min(frame, c["frames"] - 1)]
        if ch != "s":
            val *= chan_scale
        if mirror:
            if ch in ("ry", "rz", "px"):
                val = -val
            if b in ("WingL", "WingR"):
                b = "WingR" if b == "WingL" else "WingL"
        out[(b, ch)] = val
    return out


# ---------------------------------------------------------------- skinning (Unity maths)
def euler_unity(x, y, zz):
    """Quaternion.Euler(x, y, z) as a matrix: rotate about Z, then X, then Y."""
    x, y, zz = map(math.radians, (x, y, zz))
    cx, sx, cy, sy, cz, sz = math.cos(x), math.sin(x), math.cos(y), math.sin(y), math.cos(zz), math.sin(zz)
    Rx = np.array([[1, 0, 0], [0, cx, -sx], [0, sx, cx]])
    Ry = np.array([[cy, 0, sy], [0, 1, 0], [-sy, 0, cy]])
    Rz = np.array([[cz, -sz, 0], [sz, cz, 0], [0, 0, 1]])
    return Ry @ Rx @ Rz


def skin(pose, mats=False):
    world = [None] * len(NAMES)
    for i, n in enumerate(NAMES):
        g = lambda ch, dflt=0.0: pose.get((n, ch), dflt)  # noqa: E731
        local = np.eye(4)
        par = HEAD[PARENT[i]] if PARENT[i] >= 0 else np.zeros(3)
        local[:3, 3] = HEAD[i] - par + np.array([g("px"), g("py"), g("pz")])
        local[:3, :3] = euler_unity(g("rx"), g("ry"), g("rz")) * g("s", 1.0)
        world[i] = (world[PARENT[i]] @ local) if PARENT[i] >= 0 else local
    skinm = np.stack([world[i] @ np.block([[np.eye(3), -HEAD[i][:, None]], [np.zeros((1, 3)), np.ones((1, 1))]])
                      for i in range(len(NAMES))])
    ph = np.c_[P, np.ones(nv)]
    per = np.einsum("bij,vj->vbi", skinm, ph)[:, :, :3]
    V = (per * WF[:, :, None]).sum(1)
    return (V, skinm) if mats else V


edges = np.unique(np.sort(np.r_[IDX[:, [0, 1]], IDX[:, [1, 2]], IDX[:, [2, 0]]], 1), axis=0)
rest_len = np.linalg.norm(P[edges[:, 0]] - P[edges[:, 1]], axis=1)
ok = rest_len > 1e-6
def normals(V):
    n = np.cross(V[IDX[:, 1]] - V[IDX[:, 0]], V[IDX[:, 2]] - V[IDX[:, 0]])
    return n / np.maximum(np.linalg.norm(n, axis=1, keepdims=True), 1e-12)


N0 = normals(P)
MAIN = WF[IDX[:, 0]].argmax(1)          # a triangle's reference frame: the main bone of its first corner
qa = {}
for name, c in CLIPS.items():
    worst_s, worst_q, flips = 1.0, 1.0, 0
    for fr in range(c["frames"]):
        V, M = skin(sample(name, fr), True)
        ref = np.einsum("tij,tj->ti", M[MAIN][:, :3, :3], N0)
        fl = (normals(V) * ref).sum(1) < 0
        flips = max(flips, int(fl.sum()))
        if "--diag" in args and fr == c["frames"] // 3 and fl.any():
            tri = IDX[fl]
            fv = f[tri].mean(1)
            print(name, "flipped:", int(fl.sum()), "height hist", np.histogram(fv, bins=10, range=(0, 1))[0].tolist(),
                  "main bones", {NAMES[k]: int(v) for k, v in zip(*np.unique(WF[tri[:, 0]].argmax(1), return_counts=True))},
                  "rest area min", float(np.linalg.norm(np.cross(P[tri[:, 1]] - P[tri[:, 0]], P[tri[:, 2]] - P[tri[:, 0]]), axis=1).min()))
        r = np.linalg.norm(V[edges[ok, 0]] - V[edges[ok, 1]], axis=1) / rest_len[ok]
        worst_s, worst_q = max(worst_s, r.max()), min(worst_q, r.min())
        if "--diag" in args and fr == c["frames"] // 3:
            E = edges[ok]
            print(name, fr, "edges > 1.5:", int((r > 1.5).sum()), "< 0.6:", int((r < 0.6).sum()), "of", len(r))
            for k in np.argsort(-np.abs(np.log(r)))[:6]:
                a_, b_ = E[k]
                print(f"   r={r[k]:.2f} rest={rest_len[ok][k]:.4f} f={f[a_]:.2f}/{f[b_]:.2f} x={P[a_, 0]:.2f} z={P[a_, 2]:.2f}"
                      f" {NAMES[WF[a_].argmax()]}/{NAMES[WF[b_].argmax()]}")
    qa[name] = {"seconds": round(c["frames"] / FPS, 2), "max_edge_stretch": round(worst_s, 3),
                "min_edge_squash": round(worst_q, 3), "max_flipped_triangles": flips, "triangles": len(IDX)}
print("QA", json.dumps(qa))

# ---------------------------------------------------------------- export
lines = ["RIG1", f"vertices {nv}", "phases 0", "tip 0", f"bones {len(NAMES)}"]
for i, n in enumerate(NAMES):
    h = HEAD[i]
    lines.append(f"{n} {NAMES[PARENT[i]] if PARENT[i] >= 0 else '-'} {h[0]:.5f} {h[1]:.5f} {h[2]:.5f}")
lines.append(f"weights {nv}")
for v in range(nv):
    lines.append(" ".join(f"{bi} {WF[v, bi]:.4f}" for bi in order[v] if WF[v, bi] > 0) or "0 1.0000")
for name, c in CLIPS.items():
    lines.append(f"clip {name} {c['frames']} {FPS} {1 if c['loop'] else 0}")
    for (b, ch), v in c["keys"].items():
        lines.append(f"key {b} {ch} " + " ".join(f"{x:.4f}" for x in v))
    lines.append("endclip")
out_rig.write_text("\n".join(lines) + "\n")
(out_rig.parent / (out_rig.stem + "_qa.json")).write_text(json.dumps(qa, indent=1))
print("rig written", out_rig)

if not preview:
    sys.exit(0)

# ---------------------------------------------------------------- preview (Blender)
import bpy  # noqa: E402
from mathutils import Vector  # noqa: E402

preview.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)


def to_b(v):                       # game space -> Blender (x mirrored back, z up)
    return np.c_[-v[:, 0], -v[:, 2], v[:, 1]]


me = bpy.data.meshes.new("owl")
me.from_pydata(to_b(P).tolist(), [], IDX[:, [0, 2, 1]].tolist())
uvl = me.uv_layers.new()
loops = np.zeros(len(me.loops), int)
me.loops.foreach_get("vertex_index", loops)
uvl.data.foreach_set("uv", UV[loops].astype(np.float32).ravel())
for p in me.polygons:
    p.use_smooth = True
ob = bpy.data.objects.new("owl", me)
bpy.context.scene.collection.objects.link(ob)
png = tam.with_suffix(".png")
mat = bpy.data.materials.new("m")
mat.use_nodes = True
tex = mat.node_tree.nodes.new("ShaderNodeTexImage")
tex.image = bpy.data.images.load(str(png))
mat.node_tree.links.new(tex.outputs["Color"], mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"])
me.materials.append(mat)

scene = bpy.context.scene
scene.render.engine = "BLENDER_WORKBENCH"
scene.render.resolution_x = scene.render.resolution_y = size
sh = scene.display.shading
sh.light, sh.color_type = "STUDIO", "TEXTURE"
sh.background_type = "VIEWPORT"
sh.background_color = (0.55, 0.62, 0.55)
scene.world = bpy.data.worlds.new("w")
cam_data = bpy.data.cameras.new("cam")
cam_data.type = "ORTHO"
cam_data.ortho_scale = H * 1.35
cam = bpy.data.objects.new("cam", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam
centre = Vector((0, 0, (lo[1] + hi[1]) / 2 + 0.04))

# demo: breathing throughout, gestures one after another (look right, tilt, bob, ruffle, look left at 70 %)
seq = [("look", 1.0, False), ("tilt", 1.0, False), ("bob", 1.0, False), ("ruffle", 1.0, False), ("look", 0.7, True)]
timeline = []
for name, sc, mi in seq:
    for fr in range(CLIPS[name]["frames"]):
        timeline.append((name, fr, sc, mi))
    for fr in range(int(0.4 * FPS)):
        timeline.append((None, 0, 1, False))
step = 2                                   # render at 15 fps
views = {"front34": (35, 8), "side": (90, 5), "back34": (215, 12)}
for vname, (az, el) in views.items():
    a, e = math.radians(az), math.radians(el)
    # az 0 = in front of the owl (game +z = Blender -y), 90 = from its side
    dvec = Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)))
    cam.location = centre + dvec * 5
    cam.rotation_euler = (centre - cam.location).to_track_quat("-Z", "Y").to_euler()
    vdir = preview / f"owl_{vname}"
    vdir.mkdir(exist_ok=True)
    for k, i in enumerate(range(0, len(timeline), step)):
        name, fr, sc, mi = timeline[i]
        pose = sample("breathe", i)
        if name:
            g = sample(name, fr, sc, mi)
            for key, val in g.items():
                pose[key] = pose.get(key, 1.0) * val if key[1] == "s" else pose.get(key, 0.0) + val
        V = to_b(skin(pose))
        me.vertices.foreach_set("co", V.astype(np.float32).ravel())
        me.update()
        scene.render.filepath = str(vdir / f"f{k:03d}.png")
        bpy.ops.render.render(write_still=True)
    subprocess.run([FFMPEG, "-y", "-loglevel", "error", "-framerate", str(FPS // step), "-i", str(vdir / "f%03d.png"),
                    "-c:v", "libx264", "-pix_fmt", "yuv420p", str(preview / f"owl_idle_{vname}.mp4")], check=False)
    total = len(range(0, len(timeline), step))
    for part, start in enumerate(range(0, total, 120)):
        subprocess.run([FFMPEG, "-y", "-loglevel", "error", "-start_number", str(start), "-i", str(vdir / "f%03d.png"),
                        "-vf", "select=not(mod(n\\,3)),scale=192:-1,tile=10x4", "-frames:v", "1",
                        str(preview / f"owl_sheet_{vname}_{part + 1}.png")], check=False)
    print("preview", vname)
print("done")
