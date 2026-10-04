"""Rig a cetacean in headless Blender, sample its swim cycle, export it for the mod, and render preview videos.

usage: uv run --project ~/tools/blender-env python tools/blender_rig_swimmer.py assets/models/whale.tam
         --out assets/models/whale.rig --preview <dir> [--params whale]

The mesh is imported from the mod's own .tam, so vertex indices match the game exactly. Then:
  * skeleton: a spine chain along the centre line, joints packed toward the tail; a Fluke joint at the peduncle
    (narrowest point of the tail stock); one bone per pectoral flipper;
  * skin: Blender's automatic (heat) weights, top 4 per vertex, normalised;
  * swim cycle: the centre line y(s, t) = A(s) sin(2 pi t - k s) (A grows over the rear of the body and peaks at the
    peduncle), each spine bone gets the angle that makes its segment follow it, the flukes feather within
    +-pitch of the path (a quarter cycle behind the heave), the flippers paddle a little; sampled at 32 phases;
  * export (.rig, JSON): bones (name, parent, head in .tam model space), weights per vertex, curves per bone
    (pitch degrees for each phase), cycle metadata; the C# side plays the curves and adds speed, turns and roll;
  * preview: the cycle rendered from several angles (workbench matcap), a contact sheet per view, an MP4 each,
    and measurements (fluke-tip travel, minimum tail thickness over the cycle vs rest).
Axes in Blender after import: X side, Y = -forward (head toward -Y), Z up.
"""
import json
import math
import struct
import subprocess
import sys
from pathlib import Path

import bpy  # noqa: E402
import bmesh  # noqa: F401,E402
from mathutils import Matrix, Vector

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
tam = Path(args[0]).resolve()


def opt(name, default, cast=str):
    return cast(args[args.index(name) + 1]) if name in args else default


out_rig = Path(opt("--out", str(tam.with_suffix(".rig")))).resolve()
preview = Path(opt("--preview", "")).resolve() if "--preview" in args else None
species = opt("--params", "whale")
P = {
    # tip: heave amplitude at the peduncle (fraction of length); k: wave number (rad per length); rigid: where the
    # bending starts; pitch: fluke angle to the path (deg); flipper: paddle (deg); n: spine joints
    "whale": dict(tip=0.105, k=5.0, rigid=0.50, pitch=20.0, flipper=5.0, n=10),
    "orca": dict(tip=0.10, k=4.2, rigid=0.40, pitch=20.0, flipper=4.0, n=10),
}[species]
PHASES = 32
FFMPEG = __import__("os").environ.get("UM_FFMPEG_WIN", "ffmpeg")

# ---------------------------------------------------------------- mesh from .tam (game vertex order)
bpy.ops.wm.read_factory_settings(use_empty=True)
data = tam.read_bytes()
nv, ni = struct.unpack("<ii", data[4:12])
o = 12
pos = struct.unpack(f"<{nv * 3}f", data[o:o + nv * 12]); o += nv * 24
uv = struct.unpack(f"<{nv * 2}f", data[o:o + nv * 8]); o += nv * 8
idx = struct.unpack(f"<{ni}i", data[o:o + ni * 4])


SCALE = 10.0                          # heat weighting fails on small models: work at 10x, export back


def to_blender(x, y, z):            # .tam (Unity: x right, y up, z forward) -> Blender (x, y = -z, z = y), x mirrored
    return Vector((-x, -z, y)) * SCALE


def to_tam(v):
    return (-v.x / SCALE, v.z / SCALE, -v.y / SCALE)


verts = [to_blender(pos[i * 3], pos[i * 3 + 1], pos[i * 3 + 2]) for i in range(nv)]
faces = [(idx[i], idx[i + 2], idx[i + 1]) for i in range(0, ni, 3)]
me = bpy.data.meshes.new("body")
me.from_pydata(verts, [], faces)
me.update()
body = bpy.data.objects.new("body", me)
bpy.context.scene.collection.objects.link(body)
for p in me.polygons:
    p.use_smooth = True

ys = [v.y for v in verts]
yhead, ytail = min(ys), max(ys)            # head toward -Y
L = ytail - yhead
zs = sorted(v.z for v in verts)


def s_of(v):                                # 0 head .. 1 tail tip
    return (v.y - yhead) / L


def at_s(s):
    return yhead + s * L


# centre line and peduncle from slices
def slice_stats(s, half=0.012):
    sl = [v for v in verts if abs(s_of(v) - s) < half and abs(v.x) < 0.06 * L]
    if len(sl) < 3:
        return None
    zlo, zhi = min(v.z for v in sl), max(v.z for v in sl)
    wide = [v for v in verts if abs(s_of(v) - s) < half]
    return (zlo + zhi) / 2, zhi - zlo, max(abs(v.x) for v in wide)


best, best_w = 0.88, 1e9
for i in range(70, 95):
    st = slice_stats(i / 100)
    if st and st[2] < best_w:
        best, best_w = i / 100, st[2]
s_ped = best
print(f"length {L:.3f}, peduncle at s = {s_ped:.2f}")

# spine joints: fractions of head..peduncle, packed toward the rear
fr = [0.0, 0.18, 0.34, 0.48, 0.60, 0.70, 0.78, 0.85, 0.91, 0.96][:P["n"]]
spine_s = [f * s_ped for f in fr]
joint_s = spine_s + [s_ped]

# ---------------------------------------------------------------- armature
arm_data = bpy.data.armatures.new("rig")
arm = bpy.data.objects.new("rig", arm_data)
bpy.context.scene.collection.objects.link(arm)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="EDIT")
eb = arm_data.edit_bones
names, parents = [], []
prev = None
centres = []
for j, s in enumerate(joint_s):
    st = slice_stats(s) or slice_stats(s, 0.03)
    centres.append(Vector((0, at_s(s), st[0] if st else 0)))
for j, s in enumerate(joint_s):
    name = f"Spine{j}" if j < len(spine_s) else "Fluke"
    b = eb.new(name)
    b.head = centres[j]
    b.tail = centres[j + 1] if j + 1 < len(centres) else Vector((0, ytail, centres[j].z))
    if prev:
        b.parent = prev
        b.use_connect = False
    names.append(name)
    parents.append(prev.name if prev else "")
    prev = b
# flippers: root = vertex of the flipper zone closest to the flank, tip = farthest
zmid = zs[len(zs) // 2]
for side, sx in (("L", -1), ("R", 1)):
    fl = [v for v in verts if 0.15 < s_of(v) < 0.6 and v.x * sx > 0.12 * L and v.z < zmid]
    if not fl:
        continue
    root = min(fl, key=lambda v: abs(v.x))
    tip = max(fl, key=lambda v: abs(v.x))
    par = min(range(len(spine_s)), key=lambda j: abs(spine_s[j] - s_of(root)))
    b = eb.new("Fin" + side)
    b.head, b.tail = root.copy(), tip.copy()
    b.parent = eb[f"Spine{par}"]
    names.append("Fin" + side)
    parents.append(f"Spine{par}")
bpy.ops.object.mode_set(mode="OBJECT")

# ---------------------------------------------------------------- skin (heat weights)
bpy.ops.object.select_all(action="DESELECT")
body.select_set(True)
arm.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.parent_set(type="ARMATURE_AUTO")
groups = {g.index: g.name for g in body.vertex_groups}
for n in names:
    if body.vertex_groups.get(n) is None:
        body.vertex_groups.new(name=n)
        groups = {g.index: g.name for g in body.vertex_groups}


fin_frames = {}
for side in ("L", "R"):
    b = arm_data.bones.get("Fin" + side)
    if b:
        fin_frames[side] = (b.head_local.copy(), b.tail_local.copy())


def fallback(co):
    """Smooth spine weights (tent kernels ~1.6 joint spacings wide), flukes rigid behind the peduncle, flippers
    on their own bone (fading in over the first third of the flipper from the root)."""
    s = s_of(co)
    for side, (h, t) in fin_frames.items():
        sx = -1 if side == "L" else 1
        if co.x * sx > 0.12 * L and co.z < zmid and 0.15 < s < 0.65:
            ax = t - h
            u = max(0.0, min(1.0, (co - h).dot(ax) / ax.length_squared))
            wf = min(1.0, u / 0.33)
            near = min(range(len(spine_s)), key=lambda j: abs(spine_s[j] - s))
            return [("Fin" + side, wf), (f"Spine{near}", 1 - wf)] if wf < 1 else [("Fin" + side, 1.0)]
    if s >= s_ped:
        return [("Fluke", 1.0)]
    out = []
    for j, sj in enumerate(joint_s):
        h = 0.5 * ((sj - joint_s[j - 1]) if j > 0 else (joint_s[1] - sj)) + 0.5 * (
            (joint_s[j + 1] - sj) if j + 1 < len(joint_s) else (sj - joint_s[j - 1]))
        k = 1 - abs(s - sj) / (1.6 * h)
        if k > 0:
            out.append((names[j], k))
    return sorted(out, key=lambda t: -t[1])[:4] or [("Spine0", 1.0)]


weights = []
unweighted = 0
for v in me.vertices:
    ws = sorted(((groups[g.group], g.weight) for g in v.groups if g.weight > 1e-4), key=lambda t: -t[1])[:4]
    tot = sum(w for _, w in ws)
    if tot <= 0:
        unweighted += 1
        ws = fallback(v.co)
        tot = sum(w for _, w in ws)
        for n, w in ws:                                   # write them so the Blender preview deforms like the game
            body.vertex_groups[n].add([v.index], w / tot, "REPLACE")
    weights.append([[names.index(n), round(w / tot, 4)] for n, w in ws])
print(f"heat weights: {unweighted} of {nv} vertices had none (smooth spine weights used instead)")

# ---------------------------------------------------------------- swim cycle
def env(s, prm=None):
    prm = prm or P
    r = max(0.0, (s - prm["rigid"]) / max(s_ped - prm["rigid"], 0.05))
    return prm["tip"] * (0.015 + 0.985 * min(r, 1.15) ** 2)


def line(s, ph, prm=None):
    prm = prm or P
    return env(s, prm) * math.sin(2 * math.pi * ph - prm["k"] * s)


def pose(ph, prm=None):
    """Local pitch (deg, + = tail up) of each spine joint and the fluke, flipper paddle, at phase ph in [0, 1)."""
    prm = prm or P
    pts = joint_s + [1.0]
    out, prev_pitch = {}, 0.0
    n = len(spine_s)
    for i in range(n):
        ds = max(pts[i + 1] - pts[i], 1e-3)
        pitch = math.degrees(math.atan2(line(pts[i + 1], ph, prm) - line(pts[i], ph, prm), ds))
        pitch = prev_pitch + max(-12.0, min(12.0, pitch - prev_pitch))     # no single joint over 12 degrees
        out[f"Spine{i}"] = pitch - prev_pitch
        prev_pitch = pitch
    heave_vel = math.cos(2 * math.pi * ph - prm["k"] * s_ped)
    to_path = -prm["pitch"] * heave_vel
    rel = to_path - prev_pitch
    # spread the fluke turn: 60 % at the fluke joint (capped at 14 degrees), the rest over the last two spine joints,
    # so the flukes grow out of the tail stock instead of hinging on it
    fl = max(-14.0, min(14.0, 0.6 * rel))
    rest = rel - fl
    out[f"Spine{n - 1}"] += 0.6 * rest
    out[f"Spine{n - 2}"] += 0.4 * rest
    out["Fluke"] = fl
    fin = prm["flipper"] * math.sin(2 * math.pi * ph - 1.25)
    out["FinL"], out["FinR"] = fin, -fin
    return out


curves = {n: [] for n in names}
for f in range(PHASES):
    p = pose(f / PHASES)
    for n in names:
        curves[n].append(round(p.get(n, 0.0), 3))

# ---------------------------------------------------------------- export
bone_heads = {}
for b in arm_data.bones:
    bone_heads[b.name] = to_tam(arm.matrix_world @ b.head_local)
rig = {
    "species": species, "vertices": nv, "phases": PHASES, "params": P, "peduncle_s": s_ped,
    "bones": [{"name": n, "parent": parents[i], "head": [round(c, 5) for c in bone_heads[n]],
               "axis": "pitch" if not n.startswith("Fin") else "roll"} for i, n in enumerate(names)],
    "weights": weights, "curves": curves,
}
lines = ["RIG1", f"vertices {nv}", f"phases {PHASES}", f"tip {P['tip']}", f"bones {len(names)}"]
for i, n in enumerate(names):
    h = bone_heads[n]
    lines.append(f"{n} {parents[i] or '-'} {h[0]:.5f} {h[1]:.5f} {h[2]:.5f}")
for n in names:
    lines.append("curve " + n + " " + " ".join(f"{x:.3f}" for x in curves[n]))
lines.append(f"weights {nv}")
for ws in weights:
    lines.append(" ".join(f"{bi} {w:.4f}" for bi, w in ws))
out_rig.write_text(chr(10).join(lines) + chr(10))
(out_rig.with_suffix(".rig.json")).write_text(json.dumps(rig, separators=(",", ":")))
print(f"rig written: {out_rig} ({len(names)} bones)")

# ---------------------------------------------------------------- preview + measurements
if preview:
    preview.mkdir(parents=True, exist_ok=True)
    pb = arm.pose.bones

    def apply(ph):
        p = pose(ph)
        for n in names:
            ang = math.radians(p.get(n, 0.0))
            b = pb[n]
            b.rotation_mode = "XYZ"
            # Blender bones point along their own Y (head -> tail, toward the tail): pitch is a rotation about X
            b.rotation_euler = (ang, 0, 0) if not n.startswith("Fin") else (0, ang, 0)
        bpy.context.view_layer.update()

    def evaluated_coords():
        dg = bpy.context.evaluated_depsgraph_get()
        ev = body.evaluated_get(dg)
        m = ev.to_mesh()
        co = [body.matrix_world @ v.co for v in m.vertices]
        ev.to_mesh_clear()
        return co

    # measurements over the cycle: fluke-tip vertical travel and tail-stock thickness at the peduncle
    tip_v = max(range(nv), key=lambda i: verts[i].y)
    ped = [i for i in range(nv) if abs(s_of(verts[i]) - (s_ped - 0.04)) < 0.01]
    rest_th = max(verts[i].z for i in ped) - min(verts[i].z for i in ped)
    tips, ths = [], []
    for f in range(PHASES):
        apply(f / PHASES)
        co = evaluated_coords()
        tips.append(co[tip_v].z)
        zz = [co[i].z for i in ped]
        # thickness measured perpendicular to the local axis: project on the plane normal to the stock direction
        ths.append(max(zz) - min(zz))
    meas = {"tail_tip_travel_pct_L": round((max(tips) - min(tips)) / L * 100, 1),
            "tail_stock_thickness_min_pct_rest": round(min(ths) / rest_th * 100, 1),
            "max_joint_bend_deg": round(max(abs(x) for n in names if n.startswith("Spine") for x in curves[n]), 1),
            "fluke_rel_max_deg": round(max(abs(x) for x in curves["Fluke"]), 1)}
    print("measurements", json.dumps(meas))
    (preview / f"{species}_measurements.json").write_text(json.dumps(meas, indent=1))

    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.render.resolution_x, scene.render.resolution_y = 640, 400
    sh = scene.display.shading
    sh.light, sh.color_type = "MATCAP", "SINGLE"
    sh.single_color = (0.8, 0.8, 0.8)
    sh.show_cavity = True
    scene.world = bpy.data.worlds.new("w")
    cam_data = bpy.data.cameras.new("cam")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = L * 1.25
    cam = bpy.data.objects.new("cam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    centre = Vector((0, (yhead + ytail) / 2, zmid))
    views = {"side": (90, 0), "threequarter": (55, 25), "below": (120, -35), "rear": (160, 15), "top": (90, 80)}
    frames = 48
    for vname, (az, el) in views.items():
        a, e = math.radians(az), math.radians(el)
        d = Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)))
        d = Vector((math.cos(a) * math.cos(e) * -1, math.sin(a) * math.cos(e) * 0 + -math.cos(a) * 0, 0)) if False else d
        # az measured from the head direction (-Y) around Z: 90 = from the animal's right side (+X)
        d = Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)))
        cam.location = centre + d * L * 3
        cam.rotation_euler = (centre - cam.location).to_track_quat("-Z", "Z" if abs(el) > 70 else "Y").to_euler() \
            if abs(el) > 70 else (centre - cam.location).to_track_quat("-Z", "Y").to_euler()
        vdir = preview / f"{species}_{vname}"
        vdir.mkdir(exist_ok=True)
        for f in range(frames):
            apply(f / frames)
            scene.render.filepath = str(vdir / f"f{f:03d}.png")
            bpy.ops.render.render(write_still=True)
        mp4 = preview / f"{species}_{vname}.mp4"
        subprocess.run([FFMPEG, "-y", "-loglevel", "error", "-framerate", "16", "-stream_loop", "2", "-i",
                        str(vdir / "f%03d.png"), "-c:v", "libx264", "-pix_fmt", "yuv420p", str(mp4)], check=False)
        print("preview", vname, mp4.exists())
# ---------------------------------------------------------------- extra clips (surface behaviours)
# Played by ProcSwimmer instead of the swim cycle while SeaSwimmer is in that mode. Each clip carries its own water
# level (`water`, model units above the body centre = the creature's transform), so the game holds the animal at
# exactly the depth checked here, against the real wave height under it.
#   orca  "glide":   slow, shallow strokes just under the surface, the back awash and the dorsal fin out;
#   whale "lobtail": head dips, the tail stock rises out of the water and slams the flukes flat on the surface,
#                    three times (events "slap" at each impact: splash and sound in game).
L_tam = L / SCALE
centre_b = Vector((0, (yhead + ytail) / 2, (zs[0] + zs[-1]) / 2))       # bbox centre = the game object's origin
pivot = bpy.data.objects.new("pivot", None)
bpy.context.scene.collection.objects.link(pivot)
pivot.location = centre_b
bpy.context.view_layer.update()
arm.parent = pivot
arm.matrix_parent_inverse = pivot.matrix_world.inverted()
pbones = arm.pose.bones


def apply_full(p, root_rx=0.0, root_py=0.0):
    for n in names:
        ang = math.radians(p.get(n, 0.0))
        b = pbones[n]
        b.rotation_mode = "XYZ"
        b.rotation_euler = (ang, 0, 0) if not n.startswith("Fin") else (0, ang, 0)
    pivot.rotation_euler = (math.radians(root_rx), 0, 0)
    pivot.location = centre_b + Vector((0, 0, root_py * SCALE))
    bpy.context.view_layer.update()


def coords():
    dg = bpy.context.evaluated_depsgraph_get()
    ev = body.evaluated_get(dg)
    m = ev.to_mesh()
    co = [body.matrix_world @ v.co for v in m.vertices]
    ev.to_mesh_clear()
    return co


def ease(u):
    u = min(max(u, 0.0), 1.0)
    return u * u * (3 - 2 * u)


clips = {}       # name -> dict(frames, fps, loop, keys {(bone, ch): [..]}, water, events [(name, t)])
CFPS = 30

if species == "orca":
    pg = dict(P, tip=0.055, pitch=13.0, flipper=2.0, k=P["k"] * 0.9)
    keys = {}
    for f in range(PHASES):
        p = pose(f / PHASES, pg)
        for n in names:
            keys.setdefault((n, "rz" if n.startswith("Fin") else "rx"), []).append(round(p.get(n, 0.0), 3))
        # the whole body rises and rocks a little with each stroke, so the fin bobs instead of sliding on rails
        keys.setdefault(("Root", "py"), []).append(round(0.008 * L_tam * math.sin(2 * math.pi * f / PHASES), 5))
        keys.setdefault(("Root", "rx"), []).append(round(2.0 * math.sin(2 * math.pi * f / PHASES - math.pi / 2), 3))
    # water: the back ahead of the fin (s 0.25-0.42) stays awash, ~1.5 % of the length under the surface (the game
    # also brings it up to breathe every few seconds)
    back = max(v.z for v in verts if 0.25 < s_of(v) < 0.42)
    water_b = back + 0.015 * L
    clips["glide"] = dict(frames=PHASES, fps=PHASES, loop=True, keys=keys, events=[],
                          water=round((water_b - centre_b.z) / SCALE, 5))

if species == "whale":
    n_sp = len(spine_s)
    rear = [i for i in range(n_sp) if spine_s[i] / s_ped >= 0.45]
    share = [1.0 + 0.4 * k for k in range(len(rear))]      # one arc over the rear third, not a kink at the stock
    share = [x / sum(share) for x in share]
    CURL = 58.0                    # total tail-up bend of the rear body at full raise, degrees
    ROOT = 24.0                    # head-down pitch while slapping
    cyc, intro, outro, slaps = 3.4, 1.4, 2.0, 3
    total = intro + slaps * cyc + outro

    def curl_root(t):
        """Rear-body curl (1 = tail fully up), head-down pitch, and the fluke-lag fade, at time t."""
        if t < intro:
            u = ease(t / intro)
            return 0.3 * u, ROOT * u, 1.0
        k, tc = divmod(t - intro, cyc)
        if k < slaps:
            start = 0.3 if k == 0 else -0.25
            if tc < 1.3:                                                     # raise
                return start + (1.0 - start) * ease(tc / 1.3), ROOT + 2 * ease(tc / 1.3), 1.0
            if tc < 1.6:                                                     # short hold, flowing into the slam
                return 1.0, ROOT + 2, 1.0
            fade = 1.0 - ease((tc - 2.05) / 0.35)                            # flukes flatten into the impact
            if tc < 2.25:                                                    # accelerating slam
                u = (tc - 1.6) / 0.65
                return 1.0 - 1.15 * u ** 1.8, ROOT + 2 - 6 * u, fade
            if tc < 2.5:                                                     # follow-through into the water
                u = 1 - (1 - (tc - 2.25) / 0.25) ** 2
                return -0.15 - 0.2 * u, ROOT - 4, fade
            u = ease((tc - 2.5) / 0.9)                                       # the flukes sink, the stock comes back
            return -0.35 + 0.1 * u, ROOT - 4 + 4 * u, 0.0 if tc < 2.6 else ease((tc - 2.6) / 0.6)
        u = ease((t - intro - slaps * cyc) / outro)
        return -0.25 * (1 - u), ROOT * (1 - u), 1.0

    n = int(round(total * CFPS))
    keys = {}
    prev_c = curl_root(0)[0]
    for f in range(n):
        t = f / CFPS
        c, r, lag = curl_root(t)
        rate = (c - prev_c) * CFPS
        prev_c = c
        p = {f"Spine{i}": 0.0 for i in range(n_sp)}
        for i, w in zip(rear, share):
            p[f"Spine{i}"] = CURL * c * w
        # the flukes trail the tail: down while it rises, up during the slam, flat at the impact
        p["Fluke"] = max(-15.0, min(15.0, -5.0 * rate)) * lag
        fin = 6.0 * math.sin(2 * math.pi * t / 2.2)
        p["FinL"], p["FinR"] = fin, -fin
        for nm in names:
            keys.setdefault((nm, "rz" if nm.startswith("Fin") else "rx"), []).append(round(p.get(nm, 0.0), 3))
        keys.setdefault(("Root", "rx"), []).append(round(r, 3))
        keys.setdefault(("Root", "py"), []).append(round(-0.02 * L_tam * max(c, 0.0), 5))
    # water: the flukes' lowest point at the end of the first slam sits 2 % of the length under the surface
    tip_v = max(range(nv), key=lambda i: verts[i].y)
    f_imp = int(round((intro + 2.25) * CFPS))

    def pose_at(f):
        p = {nm: keys[(nm, "rz" if nm.startswith("Fin") else "rx")][f] for nm in names}
        return p, keys[("Root", "rx")][f], keys[("Root", "py")][f]

    p, rr, py = pose_at(f_imp)
    apply_full(p, rr, py)
    co = coords()
    fl = [i for i in range(nv) if s_of(verts[i]) > s_ped + 0.02]
    water_b = min(co[i].z for i in fl) + 0.035 * L
    # slap events: when the fluke tip comes down through the surface
    events, prev = [], None
    for f in range(n):
        p, rr, py = pose_at(f)
        apply_full(p, rr, py)
        z = coords()[tip_v].z
        if prev is not None and prev > water_b >= z and f / CFPS < intro + slaps * cyc:
            events.append(("slap", round(f / CFPS, 3)))
        prev = z
    clips["lobtail"] = dict(frames=n, fps=CFPS, loop=False, keys=keys, events=events,
                            water=round((water_b - centre_b.z) / SCALE, 5))
    print("lobtail slaps at", events)

if clips:
    extra = []
    for cname, c in clips.items():
        extra.append(f"clip {cname} {c['frames']} {c['fps']} {1 if c['loop'] else 0}")
        extra.append(f"water {c['water']:.5f}")
        for ev, t in c["events"]:
            extra.append(f"event {ev} {t:.3f}")
        for (b, ch), v in c["keys"].items():
            extra.append(f"key {b} {ch} " + " ".join(f"{x:.4f}" for x in v))
        extra.append("endclip")
    with open(out_rig, "a") as fh:
        fh.write("\n".join(extra) + "\n")
    print("clips appended:", list(clips))

    # measurements against the water, in metres for the game size (whale 14 m, orca 7 m)
    metres = {"whale": 14.0, "orca": 7.0}[species] / L
    meas = {}
    for cname, c in clips.items():
        wz = centre_b.z + c["water"] * SCALE
        above_max, tip_speed, prev = 0.0, 0.0, None
        fins_out = []
        step_f = 1 if c["loop"] else 2
        for f in range(0, c["frames"], step_f):
            p = {nm: c["keys"][(nm, "rz" if nm.startswith("Fin") else "rx")][f] for nm in names}
            rr = c["keys"].get(("Root", "rx"), [0.0] * c["frames"])[f]
            py = c["keys"].get(("Root", "py"), [0.0] * c["frames"])[f]
            apply_full(p, rr, py)
            co = coords()
            top = max(v.z for v in co)
            above_max = max(above_max, top - wz)
            if cname == "glide":
                fins_out.append(top - wz)
            if not c["loop"]:
                tz = co[max(range(nv), key=lambda i: verts[i].y)].z
                if prev is not None:
                    tip_speed = max(tip_speed, (prev - tz) / (step_f / c["fps"]))
                prev = tz
        m = {"highest_point_above_water_m": round(above_max * metres, 2)}
        if cname == "glide":
            m["dorsal_fin_out_m_min_max"] = [round(min(fins_out) * metres, 2), round(max(fins_out) * metres, 2)]
            back = max(v.z for v in verts if 0.25 < s_of(v) < 0.42)
            m["back_under_water_m"] = round((wz - back) * metres, 2)
        else:
            m["fluke_tip_max_down_speed_mps"] = round(tip_speed * metres, 1)
            m["slaps"] = [t for _, t in c["events"]]
            m["seconds"] = round(c["frames"] / c["fps"], 1)
        meas[cname] = m
    print("clip measurements", json.dumps(meas))

    if preview:
        (preview / f"{species}_clip_measurements.json").write_text(json.dumps(meas, indent=1))
        scene = bpy.context.scene
        sh = scene.display.shading
        sh.light, sh.color_type = "STUDIO", "OBJECT"
        body.color = (0.75, 0.75, 0.78, 1)
        bpy.ops.mesh.primitive_plane_add(size=L * 16, location=(0, centre_b.y, 0))
        water = bpy.context.active_object
        water.color = (0.12, 0.32, 0.5, 1)
        cam = scene.camera
        cam.data.ortho_scale = L * 1.5
        for cname, c in clips.items():
            water.location.z = centre_b.z + c["water"] * SCALE
            views = {"side": (90, 6), "threequarter": (50, 22), "front": (15, 12)}
            step_f = 1 if c["loop"] else 2
            for vname, (az, el) in views.items():
                a, e = math.radians(az), math.radians(el)
                d = Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)))
                ctr = Vector((0, centre_b.y, water.location.z))
                cam.location = ctr + d * L * 3
                cam.rotation_euler = (ctr - cam.location).to_track_quat("-Z", "Y").to_euler()
                vdir = preview / f"{species}_{cname}_{vname}"
                vdir.mkdir(exist_ok=True)
                frames = list(range(0, c["frames"], step_f)) * (3 if c["loop"] else 1)
                for k, f in enumerate(frames):
                    p = {nm: c["keys"][(nm, "rz" if nm.startswith("Fin") else "rx")][f] for nm in names}
                    rr = c["keys"].get(("Root", "rx"), [0.0] * c["frames"])[f]
                    py = c["keys"].get(("Root", "py"), [0.0] * c["frames"])[f]
                    apply_full(p, rr, py)
                    scene.render.filepath = str(vdir / f"f{k:03d}.png")
                    bpy.ops.render.render(write_still=True)
                fps_out = 16 if c["loop"] else CFPS // step_f
                subprocess.run([FFMPEG, "-y", "-loglevel", "error", "-framerate", str(fps_out), "-i",
                                str(vdir / "f%03d.png"), "-c:v", "libx264", "-pix_fmt", "yuv420p",
                                str(preview / f"{species}_{cname}_{vname}.mp4")], check=False)
                count = len(frames)
                every = max(1, count // 40)
                subprocess.run([FFMPEG, "-y", "-loglevel", "error", "-i", str(vdir / "f%03d.png"), "-vf",
                                f"select=not(mod(n\\,{every})),scale=256:-1,tile=8x5", "-frames:v", "1",
                                str(preview / f"{species}_{cname}_{vname}_sheet.png")], check=False)
                print("clip preview", cname, vname, f"every {every} frames")
print("done")
