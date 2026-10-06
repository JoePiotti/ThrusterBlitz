"""Rig the segmented humanoid. Each piece is weighted only to its own bone.

The torso, arms, and hands do not share triangles, so an arm swing cannot
pull a sheet of chest with it.
"""
import os

import bpy
import bmesh
import numpy as np
from mathutils import Matrix, Vector

src = r"C:\Users\joepi\Downloads\humanoid+robot+3d+model (1).glb"
out_dir = r"C:\Users\joepi\Repos\HD2\Assets\Models\Bot"
tex_dir = os.path.join(out_dir, "Parts")
dst = os.path.join(out_dir, "HumanoidRobot.fbx")
os.makedirs(tex_dir, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)

parts = {}
for obj in list(bpy.data.objects):
    if obj.type != "MESH":
        continue
    world = obj.matrix_world.copy()
    for vertex in obj.data.vertices:
        vertex.co = world @ vertex.co
    obj.matrix_world = Matrix.Identity(4)
    parts[int(obj.name.rsplit("_", 1)[-1])] = obj

height = max(v.co.z for obj in parts.values() for v in obj.data.vertices)
height -= min(v.co.z for obj in parts.values() for v in obj.data.vertices)
scale = 1.75 / height
floor = min(v.co.z for obj in parts.values() for v in obj.data.vertices) * scale
for obj in parts.values():
    for vertex in obj.data.vertices:
        vertex.co = vertex.co * scale
        vertex.co.z -= floor
    obj.data.update()


def cloud(index):
    return np.array([tuple(v.co) for v in parts[index].data.vertices])


def end(points, low):
    z = points[:, 2]
    if low:
        band = points[z <= np.percentile(z, 18)]
    else:
        band = points[z >= np.percentile(z, 82)]
    return band.mean(axis=0)


def boundary_loops(index):
    obj = parts[index]
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.edges.ensure_lookup_table()
    remaining = {edge for edge in bm.edges if edge.is_boundary}
    found = []
    while remaining:
        edge = remaining.pop()
        loop = [edge]
        start = edge.verts[0]
        prev = edge.verts[1]
        guard = 0
        while prev != start and guard < 4000:
            guard += 1
            nxt = None
            for link in prev.link_edges:
                if link in remaining and link.is_boundary:
                    nxt = link
                    break
            if nxt is None:
                break
            remaining.discard(nxt)
            loop.append(nxt)
            prev = nxt.other_vert(prev)
        seen = {}
        for edge in loop:
            for vertex in edge.verts:
                seen[vertex.index] = np.array(vertex.co)
        if len(seen) >= 8:
            found.append(np.array(list(seen.values())))
    bm.free()
    return found


def shoulder_center(sign, arm_points):
    # The round hole in the side of the chest, then the arm verts that sit in it.
    best = None
    best_span = 0.0
    for pts in boundary_loops(0):
        center = pts.mean(axis=0)
        if center[0] * sign < 0.04 or not (1.05 < center[2] < 1.40):
            continue
        span = float(pts[:, 1].max() - pts[:, 1].min())
        if span > best_span:
            best_span = span
            best = center
    torso_c = best if best is not None else end(arm_points, False)
    dist = np.linalg.norm(arm_points - torso_c, axis=1)
    arm_c = arm_points[dist <= np.percentile(dist, 15)].mean(axis=0)
    return (torso_c + arm_c) * 0.5


upper_l, upper_r = cloud(2), cloud(1)
fore_l, fore_r = cloud(10), cloud(11)
hand_l, hand_r = cloud(13), cloud(12)
thigh_l, thigh_r = cloud(7), cloud(6)
shin_l, shin_r = cloud(4), cloud(3)
foot_l, foot_r = cloud(9), cloud(8)
head = cloud(5)

l_sh, l_el = shoulder_center(1, upper_l), end(upper_l, True)
r_sh, r_el = shoulder_center(-1, upper_r), end(upper_r, True)
# The gun meets the hand bone. Put that bone in the middle of the hand mesh,
# not at the forearm.
l_wr, r_wr = hand_l.mean(axis=0), hand_r.mean(axis=0)
l_tip, r_tip = end(hand_l, True), end(hand_r, True)
print("ARM_L", [round(float(v), 3) for v in (*l_sh, *l_el, *l_wr, *l_tip)])
print("ARM_R", [round(float(v), 3) for v in (*r_sh, *r_el, *r_wr, *r_tip)])

arm = bpy.data.armatures.new("HumanoidRig")
arm_obj = bpy.data.objects.new("HumanoidRig", arm)
bpy.context.collection.objects.link(arm_obj)
bpy.context.view_layer.objects.active = arm_obj
bpy.ops.object.mode_set(mode="EDIT")


def bone(name, head_co, tail_co, parent=None):
    edit = arm.edit_bones.new(name)
    edit.head = Vector(head_co)
    edit.tail = Vector(tail_co)
    if parent is not None:
        edit.parent = parent
    return edit


hips = bone("Hips", (0, 0, 0.92), (0, 0, 1.08))
spine = bone("Spine", (0, 0, 1.08), (0, 0, 1.28), hips)
chest = bone("Chest", (0, 0, 1.28), (0, 0, 1.48), spine)
bone("Back", (0, 0.12, 1.36), (0, 0.22, 1.36), chest)
neck = bone("Neck", (0, -0.02, 1.48), (0, -0.03, 1.58), chest)
bone("Head", (0, -0.03, 1.58), (0, -0.05, float(head[:, 2].max())), neck)

arm_l = bone("Arm.L", l_sh, l_el, chest)
fore_bone_l = bone("Arm.L.002", l_el, l_wr, arm_l)
hand_bone_l = bone("Hand.L", l_wr, l_tip, fore_bone_l)
bone("Hand.L.Tip", l_tip, l_tip + (l_tip - l_wr) * 0.25, hand_bone_l)
arm_r = bone("Arm.R", r_sh, r_el, chest)
fore_bone_r = bone("Arm.R.002", r_el, r_wr, arm_r)
hand_bone_r = bone("Hand.R", r_wr, r_tip, fore_bone_r)
bone("Hand.R.Tip", r_tip, r_tip + (r_tip - r_wr) * 0.25, hand_bone_r)

hip_l = end(thigh_l, False)
knee_l = end(thigh_l, True)
ankle_l = end(shin_l, True)
toe_l = foot_l[np.argmin(foot_l[:, 1])]
hip_r = end(thigh_r, False)
knee_r = end(thigh_r, True)
ankle_r = end(shin_r, True)
toe_r = foot_r[np.argmin(foot_r[:, 1])]
thigh_bone_l = bone("Thigh.L", hip_l, knee_l, hips)
shin_bone_l = bone("Shin.L", knee_l, ankle_l, thigh_bone_l)
foot_bone_l = bone("Foot.L", ankle_l, toe_l, shin_bone_l)
bone("Foot.L.Tip", toe_l, toe_l + np.array((0, -0.06, 0)), foot_bone_l)
thigh_bone_r = bone("Thigh.R", hip_r, knee_r, hips)
shin_bone_r = bone("Shin.R", knee_r, ankle_r, thigh_bone_r)
foot_bone_r = bone("Foot.R", ankle_r, toe_r, shin_bone_r)
bone("Foot.R.Tip", toe_r, toe_r + np.array((0, -0.06, 0)), foot_bone_r)
bpy.ops.object.mode_set(mode="OBJECT")


def paint(obj, name):
    group = obj.vertex_groups.new(name=name)
    group.add(list(range(len(obj.data.vertices))), 1.0, "REPLACE")


def paint_torso(obj):
    groups = {
        "Hips": obj.vertex_groups.new(name="Hips"),
        "Spine": obj.vertex_groups.new(name="Spine"),
        "Chest": obj.vertex_groups.new(name="Chest"),
    }
    for index, vertex in enumerate(obj.data.vertices):
        if vertex.co.z < 1.02:
            name = "Hips"
        elif vertex.co.z < 1.30:
            name = "Spine"
        else:
            name = "Chest"
        groups[name].add([index], 1.0, "REPLACE")


paint_torso(parts[0])
paint(parts[2], "Arm.L")
paint(parts[1], "Arm.R")
paint(parts[10], "Arm.L.002")
paint(parts[11], "Arm.R.002")
# These pieces are the rest of the forearm, not hands.
paint(parts[13], "Arm.L.002")
paint(parts[12], "Arm.R.002")
paint(parts[7], "Thigh.L")
paint(parts[6], "Thigh.R")
paint(parts[4], "Shin.L")
paint(parts[3], "Shin.R")
paint(parts[9], "Foot.L")
paint(parts[8], "Foot.R")
paint(parts[5], "Head")
paint(parts[14], "Head")

base = parts[0]
for index in range(1, 15):
    bpy.ops.object.select_all(action="DESELECT")
    base.select_set(True)
    parts[index].select_set(True)
    bpy.context.view_layer.objects.active = base
    bpy.ops.object.join()
bot = base
bot.name = "HumanoidRobot"

for image in bpy.data.images:
    if image.size[0] == 0:
        continue
    # tripo_part_12 stays in the image name from the glb.
    digits = ""
    for ch in reversed(image.name):
        if ch.isdigit():
            digits = ch + digits
        elif digits:
            break
    path = os.path.join(tex_dir, "tripo_part_%s.png" % digits)
    image.filepath_raw = path
    image.file_format = "PNG"
    image.save()
    print("TEX", path)

bot.parent = arm_obj
modifier = bot.modifiers.new("Armature", "ARMATURE")
modifier.object = arm_obj

bpy.context.view_layer.objects.active = arm_obj
bpy.ops.object.mode_set(mode="POSE")
arm_obj.pose.bones["Arm.L"].rotation_mode = "XYZ"
arm_obj.pose.bones["Arm.L"].rotation_euler.x = 1.2
bpy.context.view_layer.update()
depsgraph = bpy.context.evaluated_depsgraph_get()
evaluated = bot.evaluated_get(depsgraph)
bent = evaluated.to_mesh()
body_groups = {"Hips", "Spine", "Chest", "Neck", "Head"}
wing = 0
for index, vertex in enumerate(bent.vertices):
    if (vertex.co - bot.data.vertices[index].co).length <= 0.01:
        continue
    owner = ""
    best = -1.0
    for element in bot.data.vertices[index].groups:
        if element.weight > best:
            best = element.weight
            owner = bot.vertex_groups[element.group].name
    if owner in body_groups:
        wing += 1
evaluated.to_mesh_clear()
print("WING", wing, "TRIS", len(bot.data.polygons))
for pose_bone in arm_obj.pose.bones:
    pose_bone.rotation_euler = (0.0, 0.0, 0.0)
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
    path_mode="COPY",
    embed_textures=False,
)
print("EXPORTED", dst)
