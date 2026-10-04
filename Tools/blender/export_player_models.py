"""Orient the ready-for-VR pistol and white bot, then export FBX for Unity.

Pistol barrel is Blender -Y (Unity +Z) with the grip at the origin.
Bot faces Blender -Y, feet on Z=0, about 1.75 m tall.
"""
import os

import bpy
from mathutils import Matrix, Vector

pistol_src = r"c:\Users\joepi\Downloads\assets\Ready for VR\starting pistol.glb"
bot_src = r"c:\Users\joepi\Downloads\assets\Ready for VR\bot_white.glb"
pistol_dir = r"c:\Users\joepi\Repos\HD2\Assets\Models\Pistol"
bot_dir = r"c:\Users\joepi\Repos\HD2\Assets\Models\Bot"
os.makedirs(pistol_dir, exist_ok=True)
os.makedirs(bot_dir, exist_ok=True)


def save_image(image, path):
    image.filepath_raw = path
    image.file_format = "PNG"
    image.save()
    print("TEX", path)


def export_fbx(path, root):
    bpy.ops.object.select_all(action="DESELECT")
    root.select_set(True)
    for child in root.children_recursive:
        child.select_set(True)
    bpy.context.view_layer.objects.active = root
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
        path_mode="COPY",
        embed_textures=False,
    )
    print("FBX", path)


def bounds(obj):
    verts = [obj.matrix_world @ v.co for v in obj.data.vertices]
    xs = [v.x for v in verts]
    ys = [v.y for v in verts]
    zs = [v.z for v in verts]
    return verts, (min(xs), max(xs)), (min(ys), max(ys)), (min(zs), max(zs))


# --- pistol ---
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=pistol_src)
gun = next(o for o in bpy.data.objects if o.type == "MESH")
gun.name = "StartingPistol"
mesh = gun.data
# Barrel is +X, up is +Z. -90 Z swings the barrel onto -Y.
mesh.transform(Matrix.Rotation(-1.5707963, 4, "Z"))
mesh.update()
bpy.context.view_layer.update()
length = gun.dimensions.y
mesh.transform(Matrix.Diagonal((0.24 / length, 0.24 / length, 0.24 / length, 1.0)))
mesh.update()
bpy.context.view_layer.update()

verts, xs, ys, zs = bounds(gun)
span_y = ys[1] - ys[0]
span_z = zs[1] - zs[0]
grip_verts = [v for v in verts if v.y > ys[1] - span_y * 0.28 and v.z < zs[0] + span_z * 0.55]
if len(grip_verts) < 8:
    grip_verts = verts
grip = sum(grip_verts, Vector()) / len(grip_verts)
tip = [v for v in verts if v.y <= ys[0] + span_y * 0.02]
muzzle_point = sum(tip, Vector()) / len(tip)
mesh.transform(Matrix.Translation(-grip))
mesh.update()
muzzle_point -= grip

bpy.ops.object.empty_add(type="PLAIN_AXES", location=muzzle_point)
muzzle = bpy.context.active_object
muzzle.name = "Muzzle"
muzzle.empty_display_size = 0.01
muzzle.parent = gun

image = next(iter(bpy.data.images))
save_image(image, os.path.join(pistol_dir, "StartingPistol_BaseColor.png"))
for poly in gun.data.polygons:
    poly.use_smooth = True
print("PISTOL_DIM", [round(v, 4) for v in gun.dimensions], "MUZZLE", [round(v, 4) for v in muzzle_point])
export_fbx(os.path.join(pistol_dir, "StartingPistol.fbx"), gun)

# --- bot ---
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=bot_src)
bot = next(o for o in bpy.data.objects if o.type == "MESH")
bot.name = "BotWhiteMesh"
mesh = bot.data
height = bot.dimensions.z
mesh.transform(Matrix.Diagonal((1.75 / height, 1.75 / height, 1.75 / height, 1.0)))
mesh.update()
bpy.context.view_layer.update()
image = next(iter(bpy.data.images))
save_image(image, os.path.join(bot_dir, "BotWhite_BaseColor.png"))
for poly in bot.data.polygons:
    poly.use_smooth = True
print("BOT_DIM", [round(v, 4) for v in bot.dimensions])
export_fbx(os.path.join(bot_dir, "BotWhite.fbx"), bot)
