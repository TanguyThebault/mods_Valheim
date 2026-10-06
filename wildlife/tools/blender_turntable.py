"""Headless Blender turntable of a model: 16 azimuths x 3 elevations (above, level, below), textured and matcap.

usage (Blender as a Python module, see MODLOG):
  uv run --project ~/tools/blender-env python tools/blender_turntable.py model.glb out_dir [--size 512]
writes out_dir/<stem>_textured.png and out_dir/<stem>_matcap.png (rows: elevation +35, 0, -40 degrees;
columns: azimuth 0..337.5 degrees, 0 = looking at the model's right side)
"""
import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
model, out = Path(args[0]).resolve(), Path(args[1]).resolve()
size = int(args[args.index("--size") + 1]) if "--size" in args else 512
out.mkdir(parents=True, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
def import_tam(path):
    """The mod's own format (build_models.py): exactly what the game loads. Unity is left-handed: mirror x back
    and restore the winding; glTF-style Y-up becomes Blender Z-up."""
    import struct
    data = path.read_bytes()
    nv, ni = struct.unpack("<ii", data[4:12])
    o = 12
    pos = struct.unpack(f"<{nv * 3}f", data[o:o + nv * 12]); o += nv * 12
    o += nv * 12                                                    # normals: recomputed (smooth) by Blender
    uv = struct.unpack(f"<{nv * 2}f", data[o:o + nv * 8]); o += nv * 8
    idx = struct.unpack(f"<{ni}i", data[o:o + ni * 4])
    verts = [(-pos[i * 3], -pos[i * 3 + 2], pos[i * 3 + 1]) for i in range(nv)]
    faces = [(idx[i], idx[i + 2], idx[i + 1]) for i in range(0, ni, 3)]
    me = bpy.data.meshes.new(path.stem)
    me.from_pydata(verts, [], faces)
    uvl = me.uv_layers.new()
    for poly in me.polygons:
        for li in poly.loop_indices:
            vi = me.loops[li].vertex_index
            uvl.data[li].uv = (uv[vi * 2], uv[vi * 2 + 1])
    me.shade_smooth() if hasattr(me, "shade_smooth") else None
    ob = bpy.data.objects.new(path.stem, me)
    bpy.context.scene.collection.objects.link(ob)
    png = path.with_suffix(".png")
    if png.exists():
        mat = bpy.data.materials.new("m")
        mat.use_nodes = True
        tex = mat.node_tree.nodes.new("ShaderNodeTexImage")
        tex.image = bpy.data.images.load(str(png))
        bsdf = mat.node_tree.nodes.get("Principled BSDF")
        mat.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
        me.materials.append(mat)


if model.suffix.lower() in (".glb", ".gltf"):
    bpy.ops.import_scene.gltf(filepath=str(model))
elif model.suffix.lower() == ".tam":
    import_tam(model)
else:
    bpy.ops.wm.obj_import(filepath=str(model))
meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
# bounds in world space
pts = [o.matrix_world @ Vector(c) for o in meshes for c in o.bound_box]
lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
centre, radius = (lo + hi) / 2, (hi - lo).length / 2

scene = bpy.context.scene
scene.render.engine = "BLENDER_WORKBENCH"
scene.render.resolution_x = scene.render.resolution_y = size
scene.render.film_transparent = False
scene.world = bpy.data.worlds.new("w")
scene.display.shading.background_type = "VIEWPORT" if hasattr(scene.display.shading, "background_type") else None
cam_data = bpy.data.cameras.new("cam")
cam_data.type = "ORTHO"
cam_data.ortho_scale = radius * 2.1
cam = bpy.data.objects.new("cam", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam

# Blender is Z-up; glTF Y-up files arrive rotated so the model's up is +Z. Length axis: larger of X/Y extents.
ext = hi - lo
length_is_x = ext.x >= ext.y


def shoot(az, el, path):
    a, e = math.radians(az), math.radians(el)
    # azimuth 0 = from the model's right side (perpendicular to the length axis)
    side = Vector((0, -1, 0)) if length_is_x else Vector((1, 0, 0))
    fwd = Vector((1, 0, 0)) if length_is_x else Vector((0, 1, 0))
    d = (side * math.cos(a) + fwd * math.sin(a)) * math.cos(e) + Vector((0, 0, 1)) * math.sin(e)
    cam.location = centre + d * radius * 4
    cam.rotation_euler = (centre - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)


def sheet(mode):
    sh = scene.display.shading
    if mode == "textured":
        sh.light, sh.color_type = "STUDIO", "TEXTURE"
    else:
        sh.light, sh.color_type = "MATCAP", "SINGLE"
        sh.single_color = (0.8, 0.8, 0.8)
    sh.show_cavity = mode == "matcap"
    sh.background_type = "VIEWPORT"
    sh.background_color = (0.0, 1.0, 0.15)
    tiles = []
    for row, el in enumerate((35, 0, -40)):
        for col in range(16):
            p = out / f"_t_{mode}_{row}_{col}.png"
            shoot(col * 22.5, el, p)
            tiles.append((row, col, p))
    # assemble with Blender's image API (no PIL in this env)
    W, H = size * 16, size * 3
    big = bpy.data.images.new(f"sheet_{mode}", W, H)
    px = [0.0] * (W * H * 4)
    for row, col, p in tiles:
        img = bpy.data.images.load(str(p))
        src = list(img.pixels)
        for y in range(size):
            dy = (2 - row) * size + y            # Blender images are bottom-up
            base = (dy * W + col * size) * 4
            px[base:base + size * 4] = src[y * size * 4:(y + 1) * size * 4]
        bpy.data.images.remove(img)
        p.unlink()
    big.pixels = px
    big.filepath_raw = str(out / f"{model.stem}_{mode}.png")
    big.file_format = "PNG"
    big.save()


for mode in ("textured", "matcap"):
    sheet(mode)
print("done", out)
