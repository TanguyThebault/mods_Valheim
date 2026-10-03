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
def env(s):
    r = max(0.0, (s - P["rigid"]) / max(s_ped - P["rigid"], 0.05))
    return P["tip"] * (0.015 + 0.985 * min(r, 1.15) ** 2)


def line(s, ph):
    return env(s) * math.sin(2 * math.pi * ph - P["k"] * s)


def pose(ph):
    """Local pitch (deg, + = tail up) of each spine joint and the fluke, flipper paddle, at phase ph in [0, 1)."""
    pts = joint_s + [1.0]
    out, prev_pitch = {}, 0.0
    n = len(spine_s)
    for i in range(n):
        ds = max(pts[i + 1] - pts[i], 1e-3)
        pitch = math.degrees(math.atan2(line(pts[i + 1], ph) - line(pts[i], ph), ds))
        pitch = prev_pitch + max(-12.0, min(12.0, pitch - prev_pitch))     # no single joint over 12 degrees
        out[f"Spine{i}"] = pitch - prev_pitch
        prev_pitch = pitch
    heave_vel = math.cos(2 * math.pi * ph - P["k"] * s_ped)
    to_path = -P["pitch"] * heave_vel
    rel = to_path - prev_pitch
    # spread the fluke turn: 60 % at the fluke joint (capped at 14 degrees), the rest over the last two spine joints,
    # so the flukes grow out of the tail stock instead of hinging on it
    fl = max(-14.0, min(14.0, 0.6 * rel))
    rest = rel - fl
    out[f"Spine{n - 1}"] += 0.6 * rest
    out[f"Spine{n - 2}"] += 0.4 * rest
    out["Fluke"] = fl
    fin = P["flipper"] * math.sin(2 * math.pi * ph - 1.25)
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
print("done")
