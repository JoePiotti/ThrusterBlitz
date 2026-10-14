"""Pose the rocket-launcher fist on the shotgun grip and export GripShotgun."""
import math
import os

import bpy
import numpy as np
from mathutils import Euler, Matrix, Vector

SHOTGUN = r"C:\Users\joepi\Repos\HD2\Assets\Models\Shotgun\Shotgun.fbx"
ROCKET = r"C:\Users\joepi\Repos\HD2\Assets\Models\Rocket\GripRocket.fbx"
OUT = r"C:\Users\joepi\Repos\HD2\Assets\Models\Shotgun\GripShotgun.fbx"
PREVIEW = r"C:\Users\joepi\Repos\HD2\Tools\blender\shotgun_grip_preview"
os.makedirs(PREVIEW, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SHOTGUN)
shot_meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
shot_muzzle = next(o for o in bpy.context.scene.objects if o.type == "EMPTY" and o.name == "Muzzle")

bpy.ops.import_scene.fbx(filepath=ROCKET)
hand_meshes = [o for o in bpy.context.scene.objects if o.type == "MESH" and o.name.startswith("Hand_")]

# Same pose the rocket prefab applies to its Hand group.
pose = Matrix.Translation((0.0, -0.113, 0.0)) @ Euler((math.radians(-90), 0.0, 0.0), "XYZ").to_matrix().to_4x4()

def world_co(obj):
    return np.array([tuple(obj.matrix_world @ v.co) for v in obj.data.vertices], dtype=np.float64)

def bake(obj, points):
    for i, v in enumerate(obj.data.vertices):
        v.co = Vector(points[i])
    obj.matrix_world = Matrix.Identity(4)
    obj.data.update()

for obj in hand_meshes:
    baked = np.array([tuple(pose @ obj.matrix_world @ v.co) for v in obj.data.vertices], dtype=np.float64)
    bake(obj, baked)

shot_pts = np.vstack([world_co(o) for o in shot_meshes])
grip = shot_pts[shot_pts[:, 1] > -0.16]
grip_center = grip.mean(0)
print("GRIP", "n", len(grip), "min", np.round(grip.min(0), 3), "max", np.round(grip.max(0), 3), "mean", np.round(grip_center, 3))

hand_pts = np.vstack([world_co(o) for o in hand_meshes])
palm = next(o for o in hand_meshes if o.name == "Hand_hand_palm")
palm_c = world_co(palm).mean(0)
tips = [o for o in hand_meshes if o.name.endswith("_tip") or o.name.endswith("_tip")]
finger = np.vstack([world_co(o) for o in hand_meshes if "palm" not in o.name and "thumb" not in o.name and "thum" not in o.name]).mean(0)
thumb = np.vstack([world_co(o) for o in hand_meshes if "thumb" in o.name or "thum" in o.name]).mean(0)
print("PALM", np.round(palm_c, 3), "FINGER", np.round(finger, 3), "THUMB", np.round(thumb, 3))

to_fingers = finger - palm_c
to_thumb = thumb - palm_c
to_fingers = to_fingers / np.linalg.norm(to_fingers)
to_thumb = to_thumb - to_fingers * np.dot(to_thumb, to_fingers)
to_thumb = to_thumb / np.linalg.norm(to_thumb)
grip_axis = np.cross(to_fingers, to_thumb)
grip_axis = grip_axis / np.linalg.norm(grip_axis)
print("TO_FINGERS", np.round(to_fingers, 3), "TO_THUMB", np.round(to_thumb, 3), "GRIP_AXIS", np.round(grip_axis, 3))

# The fist already closes around grip_axis. Stand that axis down the pistol
# grip (+Z, away from the barrel tops) and point the wrist back (+Y).
want_back = np.array([0.0, 1.0, 0.0])
want_down = np.array([0.0, 0.0, 1.0])
back = -to_fingers
src = Matrix((Vector(back), Vector(grip_axis), Vector(np.cross(back, grip_axis)))).transposed()
dst = Matrix((Vector(want_back), Vector(want_down), Vector(np.cross(want_back, want_down)))).transposed()
rot = (dst @ src.inverted()).to_4x4()

span = float(np.max(hand_pts.max(0) - hand_pts.min(0)))
scale = 0.20 / max(span, 0.001)
print("SPAN", round(span, 3), "SCALE", round(float(scale), 3))

center = hand_pts.mean(0)
target = grip_center + np.array([0.0, -0.015, 0.012])

def place_hand(points):
    out = []
    for p in points:
        q = Vector((p - center) * scale)
        q = rot @ q
        out.append(np.array(q) + target)
    return np.array(out)

for obj in hand_meshes:
    bake(obj, place_hand(world_co(obj)))

keep = set(shot_meshes + hand_meshes + [shot_muzzle])
for obj in keep:
    world = obj.matrix_world.copy()
    obj.parent = None
    obj.matrix_world = world
for obj in list(bpy.data.objects):
    if obj in keep or obj.type not in {"MESH", "EMPTY"}:
        continue
    bpy.data.objects.remove(obj, do_unlink=True)

palm_c = world_co(palm).mean(0)
finger = np.vstack([world_co(o) for o in hand_meshes if "palm" not in o.name]).mean(0)
back = palm_c - finger
back = back / np.linalg.norm(back)
wrist_co = palm_c + back * 0.045

root = bpy.data.objects.new("GripShotgun", None)
gun = bpy.data.objects.new("Gun", None)
hand = bpy.data.objects.new("Hand", None)
for empty in (root, gun, hand):
    bpy.context.collection.objects.link(empty)

def parent_keep(child, parent):
    world = child.matrix_world.copy()
    child.parent = parent
    child.matrix_world = world

for obj in shot_meshes:
    parent_keep(obj, gun)
parent_keep(shot_muzzle, root)
for obj in hand_meshes:
    parent_keep(obj, hand)
parent_keep(gun, root)
parent_keep(hand, root)

wrist = bpy.data.objects.new("Wrist", None)
wrist.empty_display_type = "PLAIN_AXES"
wrist.empty_display_size = 0.03
bpy.context.collection.objects.link(wrist)
wrist.location = Vector(wrist_co)
bpy.context.view_layer.update()
parent_keep(wrist, root)
print("WRIST", tuple(round(float(v), 4) for v in wrist_co))

bpy.ops.object.select_all(action="DESELECT")
for obj in (root, gun, hand, shot_muzzle, wrist):
    obj.select_set(True)
for obj in shot_meshes + hand_meshes:
    obj.select_set(True)
bpy.context.view_layer.objects.active = root
bpy.ops.export_scene.fbx(
    filepath=OUT,
    use_selection=True,
    object_types={"MESH", "EMPTY"},
    apply_scale_options="FBX_SCALE_ALL",
    axis_forward="-Z",
    axis_up="Y",
    mesh_smooth_type="FACE",
    add_leaf_bones=False,
    bake_anim=False,
    path_mode="AUTO",
    embed_textures=False,
)
print("EXPORTED", OUT)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=OUT)
check = next(o for o in bpy.context.scene.objects if o.name == "Wrist")
print("WRIST_CHECK", tuple(round(float(v), 4) for v in check.matrix_world.translation))

scene = bpy.context.scene
scene.render.engine = "BLENDER_WORKBENCH"
scene.render.resolution_x = 900
scene.render.resolution_y = 900
scene.render.film_transparent = False
scene.display.shading.light = "STUDIO"
scene.display.shading.color_type = "MATERIAL"
scene.view_settings.view_transform = "Standard"

cam_data = bpy.data.cameras.new("Preview")
cam_data.type = "ORTHO"
cam_data.ortho_scale = 0.55
cam = bpy.data.objects.new("PreviewCam", cam_data)
bpy.context.collection.objects.link(cam)
scene.camera = cam

views = {
    "side": ((0.9, -0.2, -0.05), (-0.02, -0.2, -0.05)),
    "front": ((0.0, -1.1, -0.05), (-0.02, -0.2, -0.05)),
    "top": ((-0.02, -0.2, -0.9), (-0.02, -0.2, -0.05)),
}
for name, (loc, look) in views.items():
    cam.location = Vector(loc)
    direction = Vector(look) - Vector(loc)
    cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = os.path.join(PREVIEW, name + ".png")
    bpy.ops.render.render(write_still=True)
    print("RENDER", name)
