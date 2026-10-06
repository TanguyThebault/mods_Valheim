"""Clean up a generated whale in headless Blender: symmetrize, longer flippers, smoother body, game polycount.

usage: uv run --project ~/tools/blender-env python tools/blender_fix_whale.py in.glb out.glb
       [--keep +|-] [--flipper 1.5] [--faces 12000]
- symmetrize: cut on the centre plane, keep one side (--keep: + or - X), mirror it (welded seam). Removes one-sided
  defects (the Rodin whale had a pocket at one flipper root) and makes the model exactly symmetric.
- flippers: stretched from their root along their own axis (smooth fade-in), tapered toward the tip.
- body smoothing: a few Laplacian passes on the body only (flukes, flippers and the head left sharp).
- decimate to about --faces triangles (symmetry kept), then export GLB (texture kept for reference; the mod
  repaints from geometry).
Axes after glTF import: Blender X = model side, Y = length, Z = up.
"""
import sys
from pathlib import Path

import bpy  # noqa: E402  (bpy first: it makes bmesh importable)
import bmesh
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
src, dst = Path(args[0]), Path(args[1])


def opt(name, default, cast=float):
    return cast(args[args.index(name) + 1]) if name in args else default


keep = opt("--keep", "+", str)
flipper_scale = opt("--flipper", 1.35)
target_faces = opt("--faces", 12000, int)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(src))
obj = [o for o in bpy.context.scene.objects if o.type == "MESH"][0]
bpy.context.view_layer.objects.active = obj
obj.select_set(True)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
me = obj.data
# glTF splits vertices at UV seams: weld them first, or every cut / mirror / decimation opens cracks there
bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.mesh.select_all(action="SELECT")
bpy.ops.mesh.remove_doubles(threshold=1e-5 * max(obj.dimensions))
bpy.ops.object.mode_set(mode="OBJECT")
me = obj.data

xs = [v.co.x for v in me.vertices]
ys = [v.co.y for v in me.vertices]
zs = [v.co.z for v in me.vertices]
cx = sorted(xs)[len(xs) // 2]
ylo, yhi = min(ys), max(ys)
L = yhi - ylo
print(f"length {L:.3f}, centre x {cx:.4f}, faces {len(me.polygons)}")

# --- symmetrize: bisect at x = cx, drop one side, mirror
bm = bmesh.new()
bm.from_mesh(me)
geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
res = bmesh.ops.bisect_plane(bm, geom=geom, plane_co=(cx, 0, 0), plane_no=(1, 0, 0), clear_outer=(keep == "-"),
                             clear_inner=(keep == "+"))
bm.to_mesh(me)
bm.free()
for v in me.vertices:
    v.co.x -= cx                                   # centre plane at x = 0
mod = obj.modifiers.new("mirror", "MIRROR")
mod.use_axis[0] = True
mod.use_clip = True
mod.use_mirror_merge = True
mod.merge_threshold = L * 0.0005
bpy.ops.object.modifier_apply(modifier=mod.name)
me = obj.data
print("symmetrized, faces", len(me.polygons))

# --- which end is the tail: flukes are wide and flat
def ends():
    lo_band = [v for v in me.vertices if v.co.y < ylo + 0.1 * L]
    hi_band = [v for v in me.vertices if v.co.y > yhi - 0.1 * L]
    def ratio(b):
        w = max(v.co.x for v in b) - min(v.co.x for v in b)
        h = max(v.co.z for v in b) - min(v.co.z for v in b)
        return w / max(h, 1e-9)
    return ratio(lo_band) > ratio(hi_band)
tail_low = ends()
def t_of(co):                                       # 0 = tail tip, 1 = head
    return (co.y - ylo) / L if tail_low else (yhi - co.y) / L

# --- flippers: below the mid-line and beyond the body's local width (measured on the upper half of each slice,
# since the flippers attach low; the throat is wider than the tail stock, so one global width grabbed the flanks)
zmid = sorted(v.co.z for v in me.vertices)[len(me.vertices) // 2]
nb = 40
upper = [[] for _ in range(nb)]
for v in me.vertices:
    if v.co.z > zmid:
        upper[min(nb - 1, int(t_of(v.co) * nb))].append(abs(v.co.x))
local = [sorted(b)[int(len(b) * 0.97)] if b else 0.0 for b in upper]
local = [max(local[max(0, i - 1):i + 2]) for i in range(nb)]       # a little margin across bins
def flank(co):
    return local[min(nb - 1, int(t_of(co) * nb))]
body_half = sorted(local[int(0.2 * nb):int(0.4 * nb)])[-1]
for sign in (1, -1):
    fl = [v for v in me.vertices if 0.4 < t_of(v.co) < 0.9 and v.co.z < zmid and v.co.x * sign > flank(v.co) * 1.06]
    if not fl:
        continue
    root = min(fl, key=lambda v: abs(v.co.x)).co.copy()
    tip = max(fl, key=lambda v: abs(v.co.x)).co.copy()
    axis = (tip - root).normalized()
    reach = (tip - root).length
    # the blade's own frame: thickness = smallest principal axis of the flipper vertices, chord = the remaining one
    import numpy as np
    pts = np.array([[*(v.co - root)] for v in fl])
    pts_c = pts - pts.mean(0)
    _, _, vt = np.linalg.svd(pts_c, full_matrices=False)
    thick = Vector(vt[2].tolist())
    thick = (thick - axis * thick.dot(axis)).normalized()
    chord = axis.cross(thick).normalized()
    tail_dir = Vector((0, 1, 0)) if tail_low else Vector((0, -1, 0))
    for v in fl:
        d = v.co - root
        along = d.dot(axis)
        c = d.dot(chord)
        th = d.dot(thick)
        u = max(0.0, min(1.0, along / reach))
        w = max(0.0, min(1.0, along / (0.06 * L)))
        w = w * w * (3 - 2 * w)                     # fade in from the root
        # a paddle, not a javelin: the chord grows with the length (thickness kept), only the last quarter rounds
        # off, and the blade curves gently back toward the tail
        tip_round = 1.0 - 0.55 * max(0.0, (u - 0.75) / 0.25) ** 2
        new = (root + axis * (along * flipper_scale) + chord * (c * flipper_scale * tip_round) + thick * th
               + tail_dir * (0.10 * reach * flipper_scale * u * u))
        v.co = v.co.lerp(new, w)
    print(f"flipper {sign:+d}: {len(fl)} verts, reach {reach / L:.3f} L -> {reach * flipper_scale / L:.3f} L")

# --- smooth the body only (vertex group), a few passes
vg = obj.vertex_groups.new(name="body")
body_idx = [v.index for v in me.vertices
            if 0.16 < t_of(v.co) < 0.85 and abs(v.co.x) < flank(v.co) * 1.02]
vg.add(body_idx, 1.0, "REPLACE")
sm = obj.modifiers.new("smooth", "LAPLACIANSMOOTH")
sm.vertex_group = "body"
sm.iterations = 6
sm.lambda_factor = 0.6
sm.use_volume_preserve = True
bpy.ops.object.modifier_apply(modifier=sm.name)

# --- decimate with symmetry
tris = sum(len(p.vertices) - 2 for p in obj.data.polygons)
if tris > target_faces:
    dec = obj.modifiers.new("dec", "DECIMATE")
    dec.ratio = target_faces / tris
    dec.use_symmetry = True
    dec.symmetry_axis = "X"
    bpy.ops.object.modifier_apply(modifier=dec.name)
bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.mesh.select_all(action="SELECT")
bpy.ops.mesh.quads_convert_to_tris()
bpy.ops.mesh.normals_make_consistent(inside=False)
bpy.ops.object.mode_set(mode="OBJECT")
bpy.ops.object.shade_smooth()
print("final faces", len(obj.data.polygons))
bpy.ops.export_scene.gltf(filepath=str(dst), export_format="GLB", use_selection=True)
print("written", dst)
