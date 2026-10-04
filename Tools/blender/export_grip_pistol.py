"""Decimate the left-hand pistol and export a mirrored right-hand copy.

Barrel along Blender -Y. The grip sits at the origin. Overall length is
about 0.42 m so the pistol matches the size of the starting pistol.
"""
import math
import os

import bpy
import bmesh
import numpy as np
from mathutils import Matrix, Vector

src = r"c:\Users\joepi\Downloads\futuristic+pistol+3d+model.glb"
out_dir = r"c:\Users\joepi\Repos\HD2\Assets\Models\Pistol"
preview_dir = r"c:\Users\joepi\Repos\HD2\Tools\blender\smg_preview"
target_tris = 3500
os.makedirs(out_dir, exist_ok=True)
os.makedirs(preview_dir, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
obj = next(o for o in bpy.data.objects if o.type == "MESH")
obj.name = "GripPistolLeft"
bpy.context.view_layer.objects.active = obj
obj.select_set(True)
mesh = obj.data
mesh.transform(obj.matrix_world)
obj.matrix_world = Matrix.Identity(4)
mesh.update()

bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.mesh.select_all(action="SELECT")
bpy.ops.mesh.remove_doubles(threshold=0.0001)
bpy.ops.mesh.fill_holes(sides=8)
bpy.ops.object.mode_set(mode="OBJECT")

mesh.calc_loop_triangles()
before = len(mesh.loop_triangles)
mod = obj.modifiers.new(name="Decimate", type="DECIMATE")
mod.decimate_type = "COLLAPSE"
mod.ratio = min(1.0, target_tris / max(1, before))
mod.use_collapse_triangulate = True
bpy.ops.object.modifier_apply(modifier=mod.name)
for poly in mesh.polygons:
    poly.use_smooth = True

# Barrel is the thin high end on +X. -90 Z swings that onto -Y. Up stays +Z.
mesh.transform(Matrix.Rotation(math.radians(-90), 4, "Z"))
mesh.update()
bpy.context.view_layer.update()
length = obj.dimensions.y
mesh.transform(Matrix.Diagonal((0.42 / length, 0.42 / length, 0.42 / length, 1.0)))
mesh.update()
bpy.context.view_layer.update()

world = np.array([tuple(obj.matrix_world @ v.co) for v in mesh.vertices])
min_z = float(world[:, 2].min())
span_z = float(world[:, 2].max() - min_z)
grip_verts = world[world[:, 2] <= min_z + span_z * 0.35]
grip = Vector(grip_verts.mean(axis=0))
mesh.transform(Matrix.Translation(-grip))
mesh.update()
bpy.context.view_layer.update()

world = np.array([tuple(v.co) for v in mesh.vertices])
min_y = float(world[:, 1].min())
span_y = float(world[:, 1].max() - min_y)
tip = world[world[:, 1] <= min_y + span_y * 0.03]
muzzle_point = Vector(tip.mean(axis=0))

bm = bmesh.new()
bm.from_mesh(mesh)
open_edges = sum(1 for edge in bm.edges if edge.is_boundary)
bm.free()
mesh.calc_loop_triangles()
print("LEFT", "TRIS", before, "->", len(mesh.loop_triangles), "VERTS", len(mesh.vertices), "OPEN", open_edges)
print("DIM", [round(v, 4) for v in obj.dimensions])
print("MUZZLE", [round(v, 4) for v in muzzle_point])


def add_muzzle(parent, location):
    bpy.ops.object.empty_add(type="PLAIN_AXES", location=location)
    empty = bpy.context.active_object
    empty.name = "Muzzle"
    empty.empty_display_size = 0.012
    empty.parent = parent
    return empty


add_muzzle(obj, muzzle_point)

right_mesh = mesh.copy()
right = bpy.data.objects.new("GripPistolRight", right_mesh)
bpy.context.collection.objects.link(right)
right_mesh.transform(Matrix.Diagonal((-1.0, 1.0, 1.0, 1.0)))
right_mesh.flip_normals()
for poly in right_mesh.polygons:
    poly.use_smooth = True
right_muzzle = Vector((-muzzle_point.x, muzzle_point.y, muzzle_point.z))

base = None
for mat in mesh.materials:
    if mat and mat.use_nodes:
        for node in mat.node_tree.nodes:
            if node.type == "TEX_IMAGE" and node.image:
                base = node.image
if base is None:
    raise SystemExit("no base color")
base.colorspace_settings.name = "sRGB"
base.filepath_raw = os.path.join(out_dir, "GripPistol_BaseColor.png")
base.file_format = "PNG"
base.save()
print("TEX", base.filepath_raw, list(base.size))


def export(root, extra, filename):
    bpy.ops.object.select_all(action="DESELECT")
    root.select_set(True)
    if extra is not None:
        extra.select_set(True)
    bpy.context.view_layer.objects.active = root
    path = os.path.join(out_dir, filename)
    bpy.ops.export_scene.fbx(
        filepath=path,
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
    print("EXPORTED", path)


left_muzzle = obj.children[0] if obj.children else None
export(obj, left_muzzle, "GripPistolLeft.fbx")
if left_muzzle is not None:
    left_muzzle.name = "MuzzleLeft"
muzzle_r = add_muzzle(right, right_muzzle)
muzzle_r.name = "Muzzle"
export(right, muzzle_r, "GripPistolRight.fbx")


def render(name, hidden):
    for item in hidden:
        item.hide_render = True
    scene = bpy.context.scene
    scene.render.resolution_x = 640
    scene.render.resolution_y = 480
    scene.render.filepath = os.path.join(preview_dir, name)
    if scene.camera is None:
        bpy.ops.object.camera_add()
        scene.camera = bpy.context.active_object
    cam = scene.camera
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = 0.55
    center = Vector((0.0, -0.05, 0.04))
    cam.location = center + Vector((0.7, 0.15, 0.25))
    cam.rotation_euler = (math.radians(70), 0, math.radians(80))
    bpy.ops.render.render(write_still=True)
    for item in hidden:
        item.hide_render = False


if not any(o.type == "LIGHT" for o in bpy.data.objects):
    bpy.ops.object.light_add(type="SUN", location=(1, -1, 2))
    bpy.context.active_object.data.energy = 5

render("grip_left.png", [right, muzzle_r])
render("grip_right.png", [obj] + list(obj.children))
print("PREVIEWS")
