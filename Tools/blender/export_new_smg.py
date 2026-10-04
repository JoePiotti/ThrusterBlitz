"""Decimate the new SMG and export it in the same pose as the current one.

Barrel along Blender -Y, grip at the origin, length 0.5 m. The in-game
held and pickup scales stay as they are, so the gun stays the same size.
"""
import math
import os

import bpy
import bmesh
import numpy as np
from mathutils import Matrix, Vector

src = r"c:\Users\joepi\Downloads\smg.glb"
out_dir = r"c:\Users\joepi\Repos\HD2\Assets\Models\SMG"
preview_dir = r"c:\Users\joepi\Repos\HD2\Tools\blender\smg_preview"
target_tris = 2000
os.makedirs(preview_dir, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
obj = next(o for o in bpy.data.objects if o.type == "MESH")
obj.name = "Smg"

bpy.context.view_layer.objects.active = obj
obj.select_set(True)
mesh = obj.data
mesh.transform(obj.matrix_world)
obj.matrix_world = Matrix.Identity(4)
mesh.update()

# The download stores each triangle on its own vertices, so the surface is
# already full of gaps. Weld those before collapsing, or decimation punches holes.
bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.mesh.select_all(action="SELECT")
bpy.ops.mesh.remove_doubles(threshold=0.0001)
bpy.ops.mesh.fill_holes(sides=8)
bpy.ops.object.mode_set(mode="OBJECT")

mesh.calc_loop_triangles()
before = len(mesh.loop_triangles)
mod = obj.modifiers.new(name="Decimate", type="DECIMATE")
mod.decimate_type = "COLLAPSE"
mod.ratio = min(1.0, target_tris / before)
mod.use_collapse_triangulate = True
bpy.ops.object.modifier_apply(modifier=mod.name)
for poly in mesh.polygons:
    poly.use_smooth = True
mesh.calc_loop_triangles()
bm = bmesh.new()
bm.from_mesh(mesh)
open_edges = sum(1 for edge in bm.edges if edge.is_boundary)
bm.free()
print("TRIS", before, "->", len(mesh.loop_triangles), "VERTS", len(mesh.vertices), "OPEN_EDGES", open_edges)

# Barrel is the thin high end on +X. -90 Z swings +X onto -Y. Up stays +Z.
mesh.transform(Matrix.Rotation(math.radians(-90), 4, "Z"))
mesh.update()
bpy.context.view_layer.update()
length = obj.dimensions.y
print("LENGTH", round(length, 4))
mesh.transform(Matrix.Diagonal((0.50 / length, 0.50 / length, 0.50 / length, 1.0)))
mesh.update()
bpy.context.view_layer.update()

world = np.array([tuple(obj.matrix_world @ v.co) for v in mesh.vertices])
min_z = float(world[:, 2].min())
span_z = float(world[:, 2].max() - min_z)
grip_verts = world[world[:, 2] <= min_z + span_z * 0.30]
grip = Vector(grip_verts.mean(axis=0))
print("GRIP", [round(v, 4) for v in grip], "count", len(grip_verts))

mesh.transform(Matrix.Translation(-grip))
mesh.update()
bpy.context.view_layer.update()

world = np.array([tuple(obj.matrix_world @ v.co) for v in mesh.vertices])
min_y = float(world[:, 1].min())
span_y = float(world[:, 1].max() - min_y)
tip = world[world[:, 1] <= min_y + span_y * 0.02]
muzzle_point = Vector(tip.mean(axis=0))

bpy.ops.object.empty_add(type="PLAIN_AXES", location=muzzle_point)
muzzle = bpy.context.active_object
muzzle.name = "Muzzle"
muzzle.empty_display_size = 0.015
muzzle.parent = obj

print("DIM", [round(v, 4) for v in obj.dimensions])
print("BOUNDS", [round(float(v), 4) for v in world.min(0)], [round(float(v), 4) for v in (world - np.array(grip)).max(0)])
print("MUZZLE", [round(v, 4) for v in muzzle_point])

base = None
for mat in mesh.materials:
    if mat and mat.use_nodes:
        for node in mat.node_tree.nodes:
            if node.type == "TEX_IMAGE" and node.image:
                base = node.image
if base is None:
    raise SystemExit("no base color")


def save_image(image, path, colorspace):
    image.colorspace_settings.name = colorspace
    image.filepath_raw = path
    image.file_format = "PNG"
    image.save()
    print("TEX", path, image.size[:])


save_image(base, os.path.join(out_dir, "Smg_BaseColor.png"), "sRGB")


def save_solid(name, path, rgba):
    size = 16
    image = bpy.data.images.new(name, size, size, alpha=True)
    pixels = np.tile(np.array(rgba, dtype=np.float32), size * size)
    image.pixels.foreach_set(pixels)
    save_image(image, path, "Non-Color")


save_solid("Smg_Normal", os.path.join(out_dir, "Smg_Normal.png"), (0.5, 0.5, 1.0, 1.0))
save_solid("Smg_Metallic", os.path.join(out_dir, "Smg_Metallic.png"), (0.0, 0.0, 0.0, 0.35))

bpy.ops.object.select_all(action="DESELECT")
obj.select_set(True)
muzzle.select_set(True)
bpy.context.view_layer.objects.active = obj
bpy.ops.export_scene.fbx(
    filepath=os.path.join(out_dir, "Smg.fbx"),
    use_selection=True,
    apply_scale_options="FBX_SCALE_ALL",
    axis_forward="-Z",
    axis_up="Y",
    object_types={"MESH", "EMPTY"},
    mesh_smooth_type="FACE",
    use_mesh_modifiers=True,
    add_leaf_bones=False,
    path_mode="STRIP",
    embed_textures=False,
)
print("EXPORTED")


def render(name, location, rotation):
    scene = bpy.context.scene
    scene.render.resolution_x = 640
    scene.render.resolution_y = 480
    scene.render.filepath = os.path.join(preview_dir, name)
    cam = scene.camera
    if cam is None:
        bpy.ops.object.camera_add()
        cam = bpy.context.active_object
        scene.camera = cam
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = 0.7
    center = Vector((0.0, -0.05, 0.08))
    cam.location = center + location
    cam.rotation_euler = rotation
    bpy.ops.render.render(write_still=True)


if not any(o.type == "LIGHT" for o in bpy.data.objects):
    bpy.ops.object.light_add(type="SUN", location=(1, -1, 2))
    bpy.context.active_object.data.energy = 4

render("new_side.png", Vector((0.8, 0, 0)), (math.radians(90), 0, math.radians(90)))
render("new_front.png", Vector((0, -0.8, 0)), (math.radians(90), 0, 0))
print("PREVIEWS")
