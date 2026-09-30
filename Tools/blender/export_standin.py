"""Clean the downloaded stand-in robot for Unity. Feet on the ground, facing Unity +Z, no run clip."""
import bpy
import os

src = r"c:\Users\joepi\Downloads\draugr-general-purpose-humanoid-bot\source\Robot Running.fbx"
dst_dir = r"c:\Users\joepi\Repos\HD2\Assets\Models\StandIn"
os.makedirs(dst_dir, exist_ok=True)
dst = os.path.join(dst_dir, "StandInRobot.fbx")

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)

for obj in list(bpy.data.objects):
    if obj.type in {"CAMERA", "LIGHT"} or obj.name == "Cube":
        bpy.data.objects.remove(obj, do_unlink=True)

for action in list(bpy.data.actions):
    bpy.data.actions.remove(action)

arm = next(obj for obj in bpy.data.objects if obj.type == "ARMATURE")
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="POSE")
bpy.ops.pose.select_all(action="SELECT")
bpy.ops.pose.transforms_clear()
bpy.ops.object.mode_set(mode="OBJECT")

min_z = 1e9
for obj in bpy.data.objects:
    if obj.type != "MESH":
        continue
    for vert in obj.data.vertices:
        min_z = min(min_z, (obj.matrix_world @ vert.co).z)

arm.location.z -= min_z
bpy.context.view_layer.update()

bpy.ops.object.select_all(action="DESELECT")
arm.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.transform_apply(location=True, rotation=False, scale=False)

bpy.ops.export_scene.fbx(
    filepath=dst,
    use_selection=False,
    apply_scale_options="FBX_SCALE_ALL",
    bake_space_transform=True,
    axis_forward="-Z",
    axis_up="Y",
    use_armature_deform_only=True,
    add_leaf_bones=False,
    bake_anim=False,
    embed_textures=True,
    path_mode="COPY",
    object_types={"ARMATURE", "MESH"},
)
print("WROTE", dst)
