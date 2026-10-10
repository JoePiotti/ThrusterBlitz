import bpy
import os
import numpy as np
from mathutils import Vector

HAND_SRC = r"C:\Users\joepi\Downloads\gun+and+hand.glb"
ROCKET_SRC = r"C:\Users\joepi\Downloads\rocket+launcher+segmented.glb"
PREVIEW = r"C:\Users\joepi\Repos\HD2\Tools\blender\rocket_preview"
FBX_PATH = r"C:\Users\joepi\Repos\HD2\Assets\Models\Rocket\GripRocket.fbx"

HAND_NAMES = {
    "hand_palm",
    "index_joint1", "index_joint2", "index_mid", "index_tip",
    "middle_joint1", "middle_joint2", "middle_joint3", "middle_tip",
    "pinky_base", "pinky_joint1", "pinky_joint2", "pinky_joint3", "pinky_mid", "pinky_tip",
    "ring_base", "ring_joint1", "ring_joint2", "ring_joint3", "ring_mid", "ring_tip",
    "thumb_base", "thumb_joint1", "thum_joint2", "thum_mid", "thum_tip",
}

def bake_world():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

def coords(obj):
    return np.array([tuple(v.co) for v in obj.data.vertices], dtype=np.float64)

def write_coords(obj, arr):
    for i, v in enumerate(obj.data.vertices):
        v.co = Vector(arr[i])

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=HAND_SRC)
bake_world()

hands = [o for o in bpy.data.objects if o.type == "MESH" and o.name in HAND_NAMES]
hand_co = np.vstack([coords(o) for o in hands])
# The closed fist. Its center is the grip it was wrapped around.
hand_center = hand_co.mean(0)
print("HAND CENTER", np.round(hand_center, 4))

for obj in list(bpy.data.objects):
    if obj.type == "MESH" and obj not in hands:
        bpy.data.objects.remove(obj, do_unlink=True)

bpy.ops.import_scene.gltf(filepath=ROCKET_SRC)
bake_world()
rocket = [o for o in bpy.data.objects if o.type == "MESH" and o not in hands]
tube = next(o for o in rocket if o.name == "launcher")
tube_co = coords(tube)
# Hanging pistol grip: behind the trigger (higher X, muzzle is -X) and below the body.
grip = tube_co[(tube_co[:, 0] > 0.18) & (tube_co[:, 0] < 0.42) & (tube_co[:, 2] < 0.30)]
grip_center = grip.mean(0)
print("ROCKET GRIP", np.round(grip_center, 4))

# Pistol barrel is +X and the rocket muzzle is -X, so yaw the fist 180 degrees.
# Keep the grip axis (Z) so the fingers still hang down the handle.
scale = 1.12
# The fist's centroid sits in the fingers, so lift it onto the handle
# and slide it back toward the stock until the fingers close on the grip.
shift = grip_center - hand_center + np.array([0.02, 0.0, 0.12])

def place(points):
    q = (points - hand_center) * scale
    q[:, 0] *= -1.0
    q[:, 1] *= -1.0
    return q + hand_center + shift

for obj in hands:
    write_coords(obj, place(coords(obj)))
    obj.name = "Hand_" + obj.name

front = tube_co[tube_co[:, 0] < np.quantile(tube_co[:, 0], 0.02)]
muzzle_co = front.mean(0)
muzzle_co[0] = float(tube_co[:, 0].min())
empty = bpy.data.objects.new("Muzzle", None)
empty.empty_display_type = "PLAIN_AXES"
empty.empty_display_size = 0.04
empty.location = Vector(muzzle_co)
bpy.context.collection.objects.link(empty)

def group(name, children):
    root = bpy.data.objects.new(name, None)
    root.empty_display_type = "PLAIN_AXES"
    root.empty_display_size = 0.06
    bpy.context.collection.objects.link(root)
    for child in children:
        world = child.matrix_world.copy()
        child.parent = root
        child.matrix_world = world
    return root

group("Hand", hands)
group("Gun", rocket + [empty])
print("HAND CHILDREN", len(hands), "GUN CHILDREN", len(rocket) + 1)

os.makedirs(os.path.dirname(FBX_PATH), exist_ok=True)
bpy.ops.object.select_all(action="DESELECT")
for obj in bpy.data.objects:
    if obj.type in {"MESH", "EMPTY"}:
        obj.select_set(True)
bpy.context.view_layer.objects.active = hands[0]
bpy.ops.export_scene.fbx(
    filepath=FBX_PATH,
    use_selection=True,
    object_types={"MESH", "EMPTY"},
    apply_scale_options="FBX_SCALE_ALL",
    axis_forward="-Z",
    axis_up="Y",
    path_mode="AUTO",
    embed_textures=False,
    mesh_smooth_type="FACE",
    add_leaf_bones=False,
    bake_anim=False,
)
print("EXPORTED", FBX_PATH)
