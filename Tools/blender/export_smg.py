"""Orient the decimated SMG for Unity and export it with textures.

Barrel points along Blender -Y, which the project FBX settings turn into Unity +Z.
Grip is down (-Z). Origin sits in the grip. Overall length is 0.5 m.
"""
import math
import os

import bpy
import numpy as np
from mathutils import Matrix, Vector

src_blend = r"c:\Users\joepi\Repos\HD2\Tools\blender\smg_lower.blend"
out_dir = r"c:\Users\joepi\Repos\HD2\Assets\Models\SMG"
preview = r"c:\Users\joepi\Repos\HD2\Tools\blender\smg_preview\oriented.png"
os.makedirs(out_dir, exist_ok=True)

bpy.ops.wm.open_mainfile(filepath=src_blend)
obj = bpy.data.objects["Smg"]

for extra in list(bpy.data.objects):
    if extra.type in {"CAMERA", "LIGHT"}:
        bpy.data.objects.remove(extra, do_unlink=True)

bpy.context.view_layer.objects.active = obj
obj.select_set(True)
mesh = obj.data
# Muzzle is on -X. +90 Z swings that onto -Y. Up stays +Z.
# Edit the mesh itself. Object rotation does not stick from a background apply.
mesh.transform(Matrix.Rotation(math.radians(90), 4, "Z"))
mesh.update()
bpy.context.view_layer.update()

length = obj.dimensions.y
print("LENGTH_BEFORE_SCALE", round(length, 4), "DIM", [round(v, 4) for v in obj.dimensions])
mesh.transform(Matrix.Diagonal((0.50 / length, 0.50 / length, 0.50 / length, 1.0)))
mesh.update()
bpy.context.view_layer.update()
bpy.ops.object.shade_smooth()

world_verts = [obj.matrix_world @ v.co for v in obj.data.vertices]
xs = [v.x for v in world_verts]
ys = [v.y for v in world_verts]
zs = [v.z for v in world_verts]
min_y, max_y = min(ys), max(ys)
min_z, max_z = min(zs), max(zs)
span_y = max_y - min_y
span_z = max_z - min_z

tip = [v for v in world_verts if v.y <= min_y + span_y * 0.015]
muzzle_point = sum(tip, Vector()) / len(tip)
grip = Vector((
    (min(xs) + max(xs)) * 0.5,
    min_y + span_y * 0.62,
    min_z + span_z * 0.22,
))

mesh.transform(Matrix.Translation(-grip))
mesh.update()
bpy.context.view_layer.update()
muzzle_point -= grip

bpy.ops.object.empty_add(type="PLAIN_AXES", location=muzzle_point)
muzzle = bpy.context.active_object
muzzle.name = "Muzzle"
muzzle.empty_display_size = 0.015
muzzle.parent = obj

obj.data.calc_loop_triangles()
print("TRIS", len(obj.data.loop_triangles), "VERTS", len(obj.data.vertices))
print("DIM", [round(v, 4) for v in obj.dimensions])
print("MUZZLE", [round(v, 4) for v in muzzle_point])

def save_image(image, path, colorspace):
    image.colorspace_settings.name = colorspace
    image.filepath_raw = path
    image.file_format = "PNG"
    image.save()
    print("TEX", path)

base = bpy.data.images["Image_0"]
mask = bpy.data.images["Image_1"]
normal = bpy.data.images["Image_2"]
save_image(base, os.path.join(out_dir, "Smg_BaseColor.png"), "sRGB")
save_image(normal, os.path.join(out_dir, "Smg_Normal.png"), "Non-Color")

# URP stores metallic in R and smoothness in A. The GLB kept roughness in G and metallic in B.
pixels = np.array(mask.pixels[:], dtype=np.float32).reshape((-1, 4))
packed = np.zeros_like(pixels)
packed[:, 0] = pixels[:, 2]
packed[:, 3] = 1.0 - pixels[:, 1]
metal = bpy.data.images.new("Smg_Metallic", mask.size[0], mask.size[1], alpha=True)
metal.colorspace_settings.name = "Non-Color"
metal.pixels.foreach_set(packed.ravel())
save_image(metal, os.path.join(out_dir, "Smg_Metallic.png"), "Non-Color")

# Point the material at the exported files so the FBX references them.
mat = obj.data.materials[0]
for node in mat.node_tree.nodes:
    if node.type != "TEX_IMAGE" or node.image is None:
        continue
    if node.image.name == "Image_0":
        node.image = base
    elif node.image.name == "Image_2":
        node.image = normal

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
    path_mode="COPY",
    embed_textures=False,
)
print("EXPORTED")

# Camera on +X, so the picture is the side: Y is length, Z is up.
bpy.ops.object.light_add(type="SUN", location=(1, -1, 2))
bpy.context.active_object.data.energy = 4
bpy.ops.object.camera_add()
cam = bpy.context.active_object
scene = bpy.context.scene
scene.camera = cam
cam.data.type = "ORTHO"
cam.data.ortho_scale = max(obj.dimensions) * 1.5
center = obj.matrix_world @ (sum((Vector(c) for c in obj.bound_box), Vector()) / 8)
cam.location = center + Vector((0.8, 0, 0))
cam.rotation_euler = (math.radians(90), 0, math.radians(90))
scene.render.filepath = preview
bpy.ops.render.render(write_still=True)
print("ORIENTED_PREVIEW")
