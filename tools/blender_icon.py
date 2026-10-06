"""Renders the poop for its inventory icon, headless (Blender as a Python module).

usage: uv run --project <a uv project with bpy> python tools/blender_icon.py <out.png> [--size 512]
The shape is the one the game builds (src/PoopArt.cs Mesh(): an elongated, bent, lumpy stone, flat belly), only
finer, with the same mottled browns plus a wet sheen; 3/4 view from above, soft key light, rim light, transparent
background. tools/make_icons.py then adds the outline and makes the final 128 px icon.
"""
import math
import sys
from pathlib import Path

import bpy
import bmesh
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
out = Path(args[0]).resolve()
size = int(args[args.index("--size") + 1]) if "--size" in args else 512
out.parent.mkdir(parents=True, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene


def noise(p, seed):
    x, y, z = p
    return (math.sin(x * 13.1 + seed) * 0.5 + math.sin(y * 17.3 - seed * 1.7) * 0.3
            + math.sin(z * 9.7 + x * 5.1 + seed * 0.6) * 0.4 + math.sin(z * 31 + y * 23) * 0.12)


def poop_mesh(rings=64, segs=64):
    """Same formula as PoopArt.Mesh() (Unity: y up, z along); returned in Blender axes (z up)."""
    bm = bmesh.new()
    grid = []
    for r in range(rings + 1):
        v = r / rings
        lat = -math.pi / 2 + math.pi * v
        row = []
        for s in range(segs):
            u = s / segs
            lon = u * math.pi * 2
            p = (math.cos(lat) * math.cos(lon), math.cos(lat) * math.sin(lon), math.sin(lat))
            taper = 1 - 0.35 * v
            constrict = 1 - 0.3 * (0.5 + 0.5 * math.cos(v * math.pi * 6)) ** 3
            lump = 1 + 0.1 * noise((p[0] * 1.3, p[1] * 1.3, p[2] * 1.3), 2.1)
            qx = p[0] * 0.042 * taper * lump * constrict
            qy = p[1] * 0.036 * taper * lump * constrict
            qz = p[2] * 0.1 * (1 + 0.05 * noise(p, 5))
            qy = max(qy, -0.026)
            qy += 0.012 * (1 - (qz / 0.1) ** 2)
            qy += 0.018 * (max(0.0, v - 0.7) / 0.3) ** 2
            qx += 0.01 * math.sin(qz * 25)
            row.append(bm.verts.new((qx, qz, qy)))          # Unity (x, y, z) -> Blender (x, z, y)
        grid.append(row)
    for r in range(rings):
        for s in range(segs):
            a, b = grid[r][s], grid[r][(s + 1) % segs]
            c, d = grid[r + 1][(s + 1) % segs], grid[r + 1][s]
            try:
                bm.faces.new((a, b, c, d))
            except ValueError:
                pass
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new("poop")
    bm.to_mesh(me)
    for poly in me.polygons:
        poly.use_smooth = True
    ob = bpy.data.objects.new("poop", me)
    scene.collection.objects.link(ob)
    return ob


poop = poop_mesh()
sm = poop.modifiers.new("smooth", "SMOOTH")   # the icon is small: keep the segments, drop the fine wrinkles
sm.factor = 1.0
sm.iterations = 6
poop.modifiers.new("sub", "SUBSURF").levels = 1

# material: mottled browns, a few undigested bits, a wet sheen, gentle bumps
mat = bpy.data.materials.new("poop")
mat.use_nodes = True
nt = mat.node_tree
bsdf = nt.nodes["Principled BSDF"]
coord = nt.nodes.new("ShaderNodeTexCoord")
mottle = nt.nodes.new("ShaderNodeTexNoise")
mottle.inputs["Scale"].default_value = 38.0
mottle.inputs["Detail"].default_value = 6.0
nt.links.new(coord.outputs["Object"], mottle.inputs["Vector"])
ramp = nt.nodes.new("ShaderNodeValToRGB")
ramp.color_ramp.elements[0].position = 0.35
ramp.color_ramp.elements[0].color = (0.032, 0.014, 0.004, 1)      # dark brown (linear)
ramp.color_ramp.elements[1].position = 0.7
ramp.color_ramp.elements[1].color = (0.11, 0.048, 0.013, 1)       # brown
nt.links.new(mottle.outputs["Fac"], ramp.inputs["Fac"])
specks = nt.nodes.new("ShaderNodeTexVoronoi")
specks.inputs["Scale"].default_value = 70.0
nt.links.new(coord.outputs["Object"], specks.inputs["Vector"])
speck_ramp = nt.nodes.new("ShaderNodeValToRGB")
speck_ramp.color_ramp.elements[0].position = 0.0
speck_ramp.color_ramp.elements[0].color = (1, 1, 1, 1)
speck_ramp.color_ramp.elements[1].position = 0.09
speck_ramp.color_ramp.elements[1].color = (0, 0, 0, 1)
nt.links.new(specks.outputs["Distance"], speck_ramp.inputs["Fac"])
mix = nt.nodes.new("ShaderNodeMix")
mix.data_type = "RGBA"
mix.inputs["B"].default_value = (0.16, 0.1, 0.03, 1)              # undigested bits
nt.links.new(speck_ramp.outputs["Color"], mix.inputs["Factor"])
nt.links.new(ramp.outputs["Color"], mix.inputs["A"])
nt.links.new(mix.outputs["Result"], bsdf.inputs["Base Color"])
bsdf.inputs["Roughness"].default_value = 0.4
bsdf.inputs["Specular IOR Level"].default_value = 0.35
bump_noise = nt.nodes.new("ShaderNodeTexNoise")
bump_noise.inputs["Scale"].default_value = 90.0
nt.links.new(coord.outputs["Object"], bump_noise.inputs["Vector"])
bump = nt.nodes.new("ShaderNodeBump")
bump.inputs["Strength"].default_value = 0.06
bump.inputs["Distance"].default_value = 0.002
nt.links.new(bump_noise.outputs["Fac"], bump.inputs["Height"])
nt.links.new(bump.outputs["Normal"], bsdf.inputs["Normal"])
poop.data.materials.append(mat)

# camera: 3/4 view from above, the poop diagonal in the frame
cam_data = bpy.data.cameras.new("cam")
cam_data.type = "ORTHO"
cam = bpy.data.objects.new("cam", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam
target = Vector((0, 0, 0.005))
d = Vector((math.cos(math.radians(-35)) * math.cos(math.radians(38)),
            math.sin(math.radians(-35)) * math.cos(math.radians(38)),
            math.sin(math.radians(38))))
cam.location = target + d * 1.0
cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
cam.rotation_euler.rotate_axis("Z", math.radians(-28))            # diagonal in the frame
cam_data.ortho_scale = 0.235


def light(name, kind, energy, loc, size=0.3, color=(1, 1, 1)):
    ld = bpy.data.lights.new(name, kind)
    ld.energy = energy
    ld.color = color
    if kind == "AREA":
        ld.size = size
    ob = bpy.data.objects.new(name, ld)
    ob.location = loc
    ob.rotation_euler = (target - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
    scene.collection.objects.link(ob)


light("key", "AREA", 14, (0.35, -0.55, 0.6), 0.5, (1.0, 0.93, 0.82))
light("fill", "AREA", 5, (-0.6, -0.2, 0.3), 0.8, (0.8, 0.85, 1.0))
light("rim", "AREA", 12, (-0.2, 0.6, 0.35), 0.25, (1.0, 0.9, 0.75))

world = bpy.data.worlds.new("w")
world.use_nodes = True
world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.05, 0.05, 0.05, 1)
world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.35
scene.world = world

scene.render.engine = "CYCLES"
scene.cycles.samples = 96
scene.cycles.use_denoising = True
scene.render.film_transparent = True
scene.render.resolution_x = scene.render.resolution_y = size
scene.render.image_settings.file_format = "PNG"
scene.render.image_settings.color_mode = "RGBA"
scene.view_settings.view_transform = "Standard"
scene.render.filepath = str(out)
bpy.ops.render.render(write_still=True)
print("wrote", out)
