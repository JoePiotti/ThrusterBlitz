"""Original rigged blockout robot for HD2. Not based on any existing game character.

Blender Z is up and -Y is forward. The FBX export bakes that into Unity's Y-up, +Z-forward.
"""
import bpy
import os
from mathutils import Vector

bpy.ops.wm.read_factory_settings(use_empty=True)

bpy.ops.object.armature_add(enter_editmode=True, location=(0, 0, 0))
arm = bpy.context.object
arm.name = "RobotArmature"
arm.data.name = "RobotArmature"
bpy.ops.armature.select_all(action="SELECT")
bpy.ops.armature.delete()

def bone(name, head, tail, parent=None):
    b = arm.data.edit_bones.new(name)
    b.head = Vector(head)
    b.tail = Vector(tail)
    if parent is not None:
        b.parent = parent
    return b

hips = bone("Hips", (0, 0, 0.86), (0, -0.02, 1.02))
chest = bone("Chest", (0, -0.02, 1.02), (0, -0.02, 1.32), hips)
bone("Neck", (0, -0.02, 1.32), (0, -0.02, 1.42), chest)

upper_l = bone("UpperArm_L", (0.2, -0.02, 1.26), (0.34, -0.08, 1.08), chest)
fore_l = bone("Forearm_L", (0.34, -0.08, 1.08), (0.42, -0.16, 0.92), upper_l)
bone("Hand_L", (0.42, -0.16, 0.92), (0.48, -0.22, 0.86), fore_l)

upper_r = bone("UpperArm_R", (-0.2, -0.02, 1.26), (-0.34, -0.08, 1.08), chest)
fore_r = bone("Forearm_R", (-0.34, -0.08, 1.08), (-0.42, -0.16, 0.92), upper_r)
bone("Hand_R", (-0.42, -0.16, 0.92), (-0.48, -0.22, 0.86), fore_r)

thigh_l = bone("Thigh_L", (0.11, 0, 0.84), (0.12, 0.02, 0.46), hips)
shin_l = bone("Shin_L", (0.12, 0.02, 0.46), (0.12, 0.02, 0.08), thigh_l)
bone("Foot_L", (0.12, 0.02, 0.08), (0.12, -0.12, 0.04), shin_l)

thigh_r = bone("Thigh_R", (-0.11, 0, 0.84), (-0.12, 0.02, 0.46), hips)
shin_r = bone("Shin_R", (-0.12, 0.02, 0.46), (-0.12, 0.02, 0.08), thigh_r)
bone("Foot_R", (-0.12, 0.02, 0.08), (-0.12, -0.12, 0.04), shin_r)

bpy.ops.object.mode_set(mode="OBJECT")

def add_box(name, bone_name, center, size):
    bpy.ops.mesh.primitive_cube_add(size=1, location=center)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = size
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    world = obj.matrix_world.copy()
    obj.parent = arm
    obj.parent_type = "BONE"
    obj.parent_bone = bone_name
    obj.matrix_world = world
    return obj

parts = [
    add_box("Pelvis", "Hips", (0.0, 0.0, 0.92), (0.28, 0.16, 0.16)),
    add_box("Chest", "Chest", (0.0, -0.03, 1.16), (0.34, 0.18, 0.26)),
    add_box("ChestPlate", "Chest", (0.0, -0.13, 1.18), (0.2, 0.04, 0.14)),
    add_box("Neck", "Neck", (0.0, -0.02, 1.36), (0.1, 0.1, 0.08)),
    add_box("Head", "Neck", (0.0, -0.04, 1.5), (0.22, 0.24, 0.26)),
    add_box("UpperArm_L", "UpperArm_L", (0.27, -0.05, 1.17), (0.09, 0.09, 0.22)),
    add_box("Forearm_L", "Forearm_L", (0.38, -0.12, 1.0), (0.08, 0.08, 0.2)),
    add_box("Hand_L", "Hand_L", (0.45, -0.19, 0.89), (0.08, 0.1, 0.06)),
    add_box("UpperArm_R", "UpperArm_R", (-0.27, -0.05, 1.17), (0.09, 0.09, 0.22)),
    add_box("Forearm_R", "Forearm_R", (-0.38, -0.12, 1.0), (0.08, 0.08, 0.2)),
    add_box("Hand_R", "Hand_R", (-0.45, -0.19, 0.89), (0.08, 0.1, 0.06)),
    add_box("Thigh_L", "Thigh_L", (0.115, 0.01, 0.64), (0.12, 0.12, 0.36)),
    add_box("Shin_L", "Shin_L", (0.12, 0.02, 0.26), (0.1, 0.1, 0.34)),
    add_box("Foot_L", "Foot_L", (0.12, -0.05, 0.05), (0.12, 0.2, 0.08)),
    add_box("Thigh_R", "Thigh_R", (-0.115, 0.01, 0.64), (0.12, 0.12, 0.36)),
    add_box("Shin_R", "Shin_R", (-0.12, 0.02, 0.26), (0.1, 0.1, 0.34)),
    add_box("Foot_R", "Foot_R", (-0.12, -0.05, 0.05), (0.12, 0.2, 0.08)),
]

mat = bpy.data.materials.new("RobotBody")
mat.use_nodes = True
bsdf = mat.node_tree.nodes.get("Principled BSDF")
if bsdf is not None:
    bsdf.inputs["Base Color"].default_value = (0.22, 0.24, 0.27, 1.0)
    bsdf.inputs["Roughness"].default_value = 0.55
for part in parts:
    part.data.materials.append(mat)

out_dir = r"c:\Users\joepi\Repos\HD2\Assets\Models\Robot"
blend_dir = r"c:\Users\joepi\Repos\HD2\Tools\blender"
os.makedirs(out_dir, exist_ok=True)
os.makedirs(blend_dir, exist_ok=True)

bpy.ops.export_scene.fbx(
    filepath=os.path.join(out_dir, "Robot.fbx"),
    use_selection=False,
    apply_scale_options="FBX_SCALE_ALL",
    bake_space_transform=True,
    axis_forward="-Z",
    axis_up="Y",
    object_types={"ARMATURE", "MESH"},
    mesh_smooth_type="FACE",
    use_mesh_modifiers=True,
    add_leaf_bones=False,
    use_armature_deform_only=False,
)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(blend_dir, "robot.blend"))
print("ROBOT_EXPORT_OK")
