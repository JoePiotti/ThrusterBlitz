"""Skin the white bot with a simple T-pose skeleton the player script can drive."""
import bpy
import numpy as np
from mathutils import Matrix, Vector

src = r"c:\Users\joepi\Downloads\assets\Ready for VR\bot_white.glb"
dst = r"c:\Users\joepi\Repos\HD2\Assets\Models\Bot\BotWhite.fbx"

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
bot = next(o for o in bpy.data.objects if o.type == "MESH")
bot.name = "BotWhiteMesh"
scale = 1.75 / bot.dimensions.z
bot.data.transform(Matrix.Diagonal((scale, scale, scale, 1.0)))
bot.data.update()
bpy.context.view_layer.update()

verts = np.array([tuple(v.co) for v in bot.data.vertices])
x = verts[:, 0]
y = verts[:, 1]
z = verts[:, 2]


def arm_side(sign):
    arm = verts[(x * sign > 0.22) & (z > 1.18) & (z < 1.50)]
    outward = arm[:, 0] * sign
    inner = float(np.percentile(outward, 6))
    outer = float(np.percentile(outward, 99))
    span = outer - inner
    # Sit the shoulder farther out so the arms stay apart when both reach forward.
    shoulder = inner + 0.09
    elbow = inner + span * 0.42
    wrist = inner + span * 0.78
    hand = outer
    height = float(np.median(arm[:, 2]))
    depth = float(np.median(arm[:, 1]))
    return shoulder, elbow, wrist, hand, depth, height


l_sh, l_el, l_wr, l_hand, l_y, l_z = arm_side(1)
r_sh, r_el, r_wr, r_hand, r_y, r_z = arm_side(-1)
print("ARM_L", [round(v, 3) for v in (l_sh, l_el, l_wr, l_hand, l_y, l_z)])
print("ARM_R", [round(v, 3) for v in (r_sh, r_el, r_wr, r_hand, r_y, r_z)])

leg = verts[(np.abs(x) > 0.04) & (np.abs(x) < 0.22) & (z < 0.95) & (z > 0.02)]
leg_x = float(np.median(np.abs(leg[:, 0]))) if len(leg) else 0.1
leg_y = float(np.median(leg[:, 1])) if len(leg) else 0.0
print("LEG", round(leg_x, 3), round(leg_y, 3), "count", len(leg))

arm_data = bpy.data.armatures.new("BotRig")
arm_obj = bpy.data.objects.new("BotRig", arm_data)
bpy.context.collection.objects.link(arm_obj)
bpy.context.view_layer.objects.active = arm_obj
bpy.ops.object.mode_set(mode="EDIT")


def add_fingers(side, hand_bone, wrist, tip, center_y, height, sign):
    # The sculpt is one solid hand. Four chains across it still let the index
    # curl on its own and the other three curl together.
    knuckle = wrist + (tip - wrist) * 0.38
    mid = wrist + (tip - wrist) * 0.70
    hand_bone.tail = Vector((sign * knuckle, center_y, height))
    spreads = (("Index", -0.040), ("Middle", -0.012), ("Ring", 0.014), ("Pinky", 0.036))
    for name, offset in spreads:
        y = center_y + offset
        base = bone(name + "." + side, (sign * knuckle, y, height), (sign * mid, y, height), hand_bone)
        distal = bone(name + "." + side + ".001", (sign * mid, y, height), (sign * tip, y, height), base)
        bone(name + "." + side + ".Tip", (sign * tip, y, height), (sign * (tip + 0.01), y, height), distal)


def bone(name, head, tail, parent=None):
    edit = arm_data.edit_bones.new(name)
    edit.head = Vector(head)
    edit.tail = Vector(tail)
    edit.envelope_distance = 0.06
    edit.head_radius = 0.05
    edit.tail_radius = 0.04
    if parent is not None:
        edit.parent = parent
    return edit


hips = bone("Hips", (0, leg_y, 0.90), (0, leg_y, 1.02))
spine = bone("Spine", (0, leg_y, 1.02), (0, 0.0, 1.20), hips)
chest = bone("Chest", (0, 0.0, 1.20), (0, l_y, 1.38), spine)
neck = bone("Neck", (0, l_y, 1.46), (0, l_y - 0.02, 1.54), chest)
head = bone("Head", (0, l_y - 0.02, 1.54), (0, l_y - 0.04, 1.70), neck)

arm_l = bone("Arm.L", (l_sh, l_y, l_z), (l_el, l_y, l_z), chest)
fore_l = bone("Arm.L.002", (l_el, l_y, l_z), (l_wr, l_y, l_z), arm_l)
hand_l = bone("Hand.L", (l_wr, l_y, l_z), (l_hand, l_y, l_z), fore_l)
add_fingers("L", hand_l, l_wr, l_hand, l_y, l_z, 1)

arm_r = bone("Arm.R", (-r_sh, r_y, r_z), (-r_el, r_y, r_z), chest)
fore_r = bone("Arm.R.002", (-r_el, r_y, r_z), (-r_wr, r_y, r_z), arm_r)
hand_r = bone("Hand.R", (-r_wr, r_y, r_z), (-r_hand, r_y, r_z), fore_r)
add_fingers("R", hand_r, r_wr, r_hand, r_y, r_z, -1)

thigh_l = bone("Thigh.L", (leg_x, leg_y, 0.90), (leg_x, leg_y, 0.50), hips)
shin_l = bone("Shin.L", (leg_x, leg_y, 0.50), (leg_x, leg_y, 0.08), thigh_l)
foot_l = bone("Foot.L", (leg_x, leg_y, 0.05), (leg_x, leg_y - 0.16, 0.03), shin_l)
bone("Foot.L.Tip", (leg_x, leg_y - 0.16, 0.03), (leg_x, leg_y - 0.2, 0.03), foot_l)
thigh_r = bone("Thigh.R", (-leg_x, leg_y, 0.90), (-leg_x, leg_y, 0.50), hips)
shin_r = bone("Shin.R", (-leg_x, leg_y, 0.50), (-leg_x, leg_y, 0.08), thigh_r)
foot_r = bone("Foot.R", (-leg_x, leg_y, 0.05), (-leg_x, leg_y - 0.16, 0.03), shin_r)
bone("Foot.R.Tip", (-leg_x, leg_y - 0.16, 0.03), (-leg_x, leg_y - 0.2, 0.03), foot_r)

bpy.ops.object.mode_set(mode="OBJECT")
bot.parent = arm_obj
modifier = bot.modifiers.get("Armature")
if modifier is None:
    modifier = bot.modifiers.new("Armature", "ARMATURE")
modifier.object = arm_obj

# Bone heat cannot solve this closed armor mesh. Weight each vertex to the
# nearest bone segments instead.
while bot.vertex_groups:
    bot.vertex_groups.remove(bot.vertex_groups[0])
segments = []
for edit in arm_obj.data.bones:
    if edit.name.endswith("Tip"):
        continue
    segments.append((edit.name, np.array(edit.head_local), np.array(edit.tail_local)))
groups = {name: bot.vertex_groups.new(name=name) for name, _, _ in segments}


def segment_distance(point, start, end):
    span = end - start
    length_sq = float(np.dot(span, span)) + 1e-8
    t = float(np.clip(np.dot(point - start, span) / length_sq, 0.0, 1.0))
    closest = start + span * t
    dist = float(np.linalg.norm(point - closest))
    if t <= 0.001 or t >= 0.999:
        dist += 0.03
    return dist


forearm_counts = {"Arm.L.002": 0, "Arm.R.002": 0}


def forearm_name(co):
    # The nearest-bone pass leaves the forearm on the upper arm, so the hand
    # looks glued to the elbow. Force the span between elbow and wrist.
    outward = abs(co[0])
    if co[2] < 1.15 or co[2] > 1.55:
        return None
    if co[0] >= 0:
        elbow, wrist, name = l_el, l_wr, "Arm.L.002"
    else:
        elbow, wrist, name = r_el, r_wr, "Arm.R.002"
    if elbow + 0.02 <= outward <= wrist - 0.01:
        return name
    return None


for index, co in enumerate(verts):
    point = np.array(co)
    forced = forearm_name(co)
    if forced is not None:
        groups[forced].add([index], 1.0, "REPLACE")
        forearm_counts[forced] += 1
        continue
    if co[1] > 0.06 and abs(co[0]) < 0.28 and 1.05 < co[2] < 1.55:
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
print("WEIGHTS distance", len(verts), "FOREARM", forearm_counts)

# Bend a forearm and count how many vertices actually move.
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
    delta = (vertex.co - bot.data.vertices[index].co).length
    if delta <= 0.01:
        continue
    moved += 1
    co = bot.data.vertices[index].co
    if abs(co.x) < 0.15 and 0.9 < co.z < 1.25:
        torso_moved += 1
evaluated.to_mesh_clear()
print("BEND_MOVED", moved, "TORSO", torso_moved)
bpy.ops.pose.transforms_clear()
bpy.ops.object.mode_set(mode="OBJECT")

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
