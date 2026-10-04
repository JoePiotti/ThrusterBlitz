"""Rig the handless low-poly robot with the same bone names the player drives."""
import os

import bpy
import numpy as np
from mathutils import Matrix, Vector

src = r"c:\Users\joepi\Downloads\low+poly+robot.glb"
out_dir = r"c:\Users\joepi\Repos\HD2\Assets\Models\Bot"
dst = os.path.join(out_dir, "LowPolyRobot.fbx")
os.makedirs(out_dir, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
bot = next(o for o in bpy.data.objects if o.type == "MESH")
bot.name = "LowPolyRobot"
height = bot.dimensions.z
scale = 1.75 / height
bot.data.transform(Matrix.Diagonal((scale, scale, scale, 1.0)))
bot.data.update()
bpy.context.view_layer.update()
# Feet on the ground. The mesh already faces Blender -Y.
verts = np.array([tuple(v.co) for v in bot.data.vertices])
bot.data.transform(Matrix.Translation((0.0, 0.0, -float(verts[:, 2].min()))))
bot.data.update()
verts = np.array([tuple(v.co) for v in bot.data.vertices])
x, y, z = verts[:, 0], verts[:, 1], verts[:, 2]
print("SCALED", [round(float(v), 3) for v in (x.min(), x.max(), y.min(), y.max(), z.min(), z.max())])


def arm_side(sign):
    arm = verts[(x * sign > 0.35) & (z > 1.15)]
    outward = arm[:, 0] * sign
    inner = float(np.percentile(outward, 8))
    outer = float(np.percentile(outward, 98))
    shoulder = inner + 0.08
    elbow = shoulder + (outer - shoulder) * 0.46
    wrist = outer - 0.015
    height_z = float(np.median(arm[:, 2]))
    depth = float(np.median(arm[:, 1]))
    return shoulder, elbow, wrist, depth, height_z


l_sh, l_el, l_wr, l_y, l_z = arm_side(1)
r_sh, r_el, r_wr, r_y, r_z = arm_side(-1)
print("ARM_L", [round(v, 3) for v in (l_sh, l_el, l_wr, l_y, l_z)])
print("ARM_R", [round(v, 3) for v in (r_sh, r_el, r_wr, r_y, r_z)])

leg = verts[(np.abs(x) > 0.06) & (np.abs(x) < 0.38) & (z < 0.9) & (z > 0.04)]
leg_x = float(np.median(np.abs(leg[:, 0]))) if len(leg) else 0.16
leg_y = float(np.median(leg[:, 1])) if len(leg) else 0.0
print("LEG", round(leg_x, 3), round(leg_y, 3), "count", len(leg))

foot = verts[(np.abs(x) > 0.05) & (np.abs(x) < 0.4) & (z < 0.12)]
foot_y = float(np.percentile(foot[:, 1], 8)) if len(foot) else leg_y - 0.16
ankle_z = 0.08
print("FOOT_Y", round(foot_y, 3))

arm_data = bpy.data.armatures.new("BotRig")
arm_obj = bpy.data.objects.new("BotRig", arm_data)
bpy.context.collection.objects.link(arm_obj)
bpy.context.view_layer.objects.active = arm_obj
bpy.ops.object.mode_set(mode="EDIT")


def bone(name, head, tail, parent=None):
    edit = arm_data.edit_bones.new(name)
    edit.head = Vector(head)
    edit.tail = Vector(tail)
    edit.envelope_distance = 0.05
    edit.head_radius = 0.04
    edit.tail_radius = 0.035
    if parent is not None:
        edit.parent = parent
    return edit


hips = bone("Hips", (0, leg_y, 0.92), (0, leg_y, 1.05))
spine = bone("Spine", (0, leg_y, 1.05), (0, 0.0, 1.22), hips)
chest = bone("Chest", (0, 0.0, 1.22), (0, l_y, 1.40), spine)
neck = bone("Neck", (0, l_y, 1.48), (0, l_y - 0.02, 1.56), chest)
bone("Head", (0, l_y - 0.02, 1.56), (0, l_y - 0.05, 1.72), neck)

arm_l = bone("Arm.L", (l_sh, l_y, l_z), (l_el, l_y, l_z), chest)
fore_l = bone("Arm.L.002", (l_el, l_y, l_z), (l_wr, l_y, l_z), arm_l)
bone("Hand.L", (l_wr, l_y, l_z), (l_wr + 0.04, l_y, l_z), fore_l)

arm_r = bone("Arm.R", (-r_sh, r_y, r_z), (-r_el, r_y, r_z), chest)
fore_r = bone("Arm.R.002", (-r_el, r_y, r_z), (-r_wr, r_y, r_z), arm_r)
bone("Hand.R", (-r_wr, r_y, r_z), (-(r_wr + 0.04), r_y, r_z), fore_r)

thigh_l = bone("Thigh.L", (leg_x, leg_y, 0.92), (leg_x, leg_y, 0.48), hips)
shin_l = bone("Shin.L", (leg_x, leg_y, 0.48), (leg_x, leg_y, ankle_z), thigh_l)
foot_l = bone("Foot.L", (leg_x, leg_y, 0.05), (leg_x, foot_y, 0.03), shin_l)
bone("Foot.L.Tip", (leg_x, foot_y, 0.03), (leg_x, foot_y - 0.04, 0.03), foot_l)
thigh_r = bone("Thigh.R", (-leg_x, leg_y, 0.92), (-leg_x, leg_y, 0.48), hips)
shin_r = bone("Shin.R", (-leg_x, leg_y, 0.48), (-leg_x, leg_y, ankle_z), thigh_r)
foot_r = bone("Foot.R", (-leg_x, leg_y, 0.05), (-leg_x, foot_y, 0.03), shin_r)
bone("Foot.R.Tip", (-leg_x, foot_y, 0.03), (-leg_x, foot_y - 0.04, 0.03), foot_r)

bpy.ops.object.mode_set(mode="OBJECT")
bot.parent = arm_obj
modifier = bot.modifiers.get("Armature")
if modifier is None:
    modifier = bot.modifiers.new("Armature", "ARMATURE")
modifier.object = arm_obj

while bot.vertex_groups:
    bot.vertex_groups.remove(bot.vertex_groups[0])
segments = []
for edit in arm_obj.data.bones:
    if edit.name.endswith("Tip") or edit.name.startswith("Hand"):
        continue
    segments.append((edit.name, np.array(edit.head_local), np.array(edit.tail_local)))
groups = {name: bot.vertex_groups.new(name=name) for name, _, _ in segments}
# Hand bones stay in the rig for the arm reach, with no mesh of their own.
bot.vertex_groups.new(name="Hand.L")
bot.vertex_groups.new(name="Hand.R")


def segment_distance(point, start, end):
    span = end - start
    length_sq = float(np.dot(span, span)) + 1e-8
    t = float(np.clip(np.dot(point - start, span) / length_sq, 0.0, 1.0))
    closest = start + span * t
    dist = float(np.linalg.norm(point - closest))
    if t <= 0.001 or t >= 0.999:
        dist += 0.03
    return dist


def forearm_name(co):
    outward = abs(co[0])
    if co[2] < l_z - 0.12 or co[2] > l_z + 0.12:
        return None
    if co[0] >= 0:
        elbow, wrist, name = l_el, l_wr, "Arm.L.002"
    else:
        elbow, wrist, name = r_el, r_wr, "Arm.R.002"
    if elbow + 0.02 <= outward <= wrist + 0.02:
        return name
    return None


forearm_counts = {"Arm.L.002": 0, "Arm.R.002": 0}
for index, co in enumerate(verts):
    point = np.array(co)
    forced = forearm_name(co)
    if forced is not None:
        groups[forced].add([index], 1.0, "REPLACE")
        forearm_counts[forced] += 1
        continue
    # The backpack sits behind the chest. +Y is the back.
    if co[1] > 0.04 and abs(co[0]) < 0.32 and 1.15 < co[2] < 1.55:
        groups["Chest"].add([index], 1.0, "REPLACE")
        continue
    ranked = sorted((segment_distance(point, start, end), name) for name, start, end in segments)
    first, first_name = ranked[0]
    second, second_name = ranked[1]
    w0 = 1.0 / (first + 0.02)
    w1 = 0.0 if second > first * 2.4 else 1.0 / (second + 0.02)
    total = w0 + w1
    groups[first_name].add([index], w0 / total, "REPLACE")
    if w1 > 0.0:
        groups[second_name].add([index], w1 / total, "REPLACE")
print("WEIGHTS", len(verts), "FOREARM", forearm_counts)

bpy.context.view_layer.objects.active = arm_obj
bpy.ops.object.mode_set(mode="POSE")
forearm = arm_obj.pose.bones["Arm.L.002"]
forearm.rotation_mode = "XYZ"
forearm.rotation_euler.z = -0.8
bpy.context.view_layer.update()
depsgraph = bpy.context.evaluated_depsgraph_get()
evaluated = bot.evaluated_get(depsgraph)
bent = evaluated.to_mesh()
moved = 0
torso_moved = 0
for index, vertex in enumerate(bent.vertices):
    if (vertex.co - bot.data.vertices[index].co).length <= 0.01:
        continue
    moved += 1
    co = bot.data.vertices[index].co
    if abs(co.x) < 0.2 and 0.9 < co.z < 1.3:
        torso_moved += 1
evaluated.to_mesh_clear()
print("BEND_MOVED", moved, "TORSO", torso_moved)
bpy.ops.pose.transforms_clear()
bpy.ops.object.mode_set(mode="OBJECT")

image = None
for mat in bot.data.materials:
    if mat and mat.use_nodes:
        for node in mat.node_tree.nodes:
            if node.type == "TEX_IMAGE" and node.image:
                image = node.image
if image is not None:
    image.colorspace_settings.name = "sRGB"
    image.filepath_raw = os.path.join(out_dir, "LowPolyRobot_BaseColor.png")
    image.file_format = "PNG"
    image.save()
    print("TEX", image.filepath_raw)

bot.data.calc_loop_triangles()
print("TRIS", len(bot.data.loop_triangles))

bpy.ops.object.select_all(action="DESELECT")
arm_obj.select_set(True)
bot.select_set(True)
bpy.context.view_layer.objects.active = arm_obj
bpy.ops.export_scene.fbx(
    filepath=dst,
    use_selection=True,
    apply_scale_options="FBX_SCALE_ALL",
    axis_forward="-Z",
    axis_up="Y",
    object_types={"ARMATURE", "MESH"},
    use_armature_deform_only=True,
    add_leaf_bones=False,
    bake_anim=False,
    mesh_smooth_type="FACE",
    path_mode="STRIP",
    embed_textures=False,
)
print("EXPORTED", dst)
