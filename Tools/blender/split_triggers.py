"""Rebuild the pistol and separate only the red trigger, not the guard."""
import os

import bpy
import bmesh
import numpy as np
from mathutils import Matrix, Vector

src = r"c:\Users\joepi\Downloads\assets\Ready for VR\starting pistol.glb"
dst = r"c:\Users\joepi\Repos\HD2\Assets\Models\Pistol\StartingPistol.fbx"
tex = os.path.join(os.path.dirname(dst), "StartingPistol_BaseColor.png")

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
gun = next(o for o in bpy.data.objects if o.type == "MESH")
gun.name = "StartingPistol"
mesh = gun.data
mesh.transform(Matrix.Rotation(-1.5707963, 4, "Z"))
mesh.update()
bpy.context.view_layer.update()
length = gun.dimensions.y
mesh.transform(Matrix.Diagonal((0.24 / length, 0.24 / length, 0.24 / length, 1.0)))
mesh.update()
bpy.context.view_layer.update()

world = [gun.matrix_world @ v.co for v in mesh.vertices]
ys = [v.y for v in world]
zs = [v.z for v in world]
span_y = max(ys) - min(ys)
span_z = max(zs) - min(zs)
grip_verts = [v for v in world if v.y > max(ys) - span_y * 0.28 and v.z < min(zs) + span_z * 0.55]
grip = sum(grip_verts, Vector()) / len(grip_verts)
tip = [v for v in world if v.y <= min(ys) + span_y * 0.02]
muzzle_point = sum(tip, Vector()) / len(tip) - grip
mesh.transform(Matrix.Translation(-grip))
mesh.update()

bpy.ops.object.empty_add(type="PLAIN_AXES", location=muzzle_point)
muzzle = bpy.context.active_object
muzzle.name = "Muzzle"
muzzle.empty_display_size = 0.01
muzzle.parent = gun

image = None
for mat in mesh.materials:
    if mat and mat.use_nodes:
        for node in mat.node_tree.nodes:
            if node.type == "TEX_IMAGE" and node.image:
                image = node.image
if image is None:
    raise SystemExit("no texture")
image.filepath_raw = tex
image.file_format = "PNG"
image.save()

w, h = image.size
pixels = np.array(image.pixels[:], dtype=np.float32).reshape((h, w, 4))
uv_layer = mesh.uv_layers.active.data
red = set()
for poly in mesh.polygons:
    for li in poly.loop_indices:
        vi = mesh.loops[li].vertex_index
        if vi in red:
            continue
        u, v = uv_layer[li].uv
        px = min(w - 1, max(0, int(u * (w - 1))))
        py = min(h - 1, max(0, int(v * (h - 1))))
        r, g, b, a = pixels[py, px]
        if r > 0.45 and r > g * 1.7 and r > b * 1.7:
            red.add(vi)

points = [(vi, mesh.vertices[vi].co.copy()) for vi in red]
print("RED_VERTS", len(points))

# Group the red paint into nearby pieces. The trigger is the compact piece
# by the grip, not the long accent along the slide.
clusters = []
for vi, co in points:
    placed = False
    for cluster in clusters:
        if any((co - other).length < 0.012 for _, other in cluster):
            cluster.append((vi, co))
            placed = True
            break
    if not placed:
        clusters.append([(vi, co)])

# Merge clusters that grew into each other.
changed = True
while changed:
    changed = False
    for i in range(len(clusters)):
        for j in range(i + 1, len(clusters)):
            close = False
            for _, a in clusters[i]:
                for _, b in clusters[j]:
                    if (a - b).length < 0.012:
                        close = True
                        break
                if close:
                    break
            if close:
                clusters[i].extend(clusters[j])
                del clusters[j]
                changed = True
                break
        if changed:
            break

print("CLUSTERS", len(clusters))
best = None
for cluster in clusters:
    coords = [co for _, co in cluster]
    min_y = min(c.y for c in coords)
    max_y = max(c.y for c in coords)
    min_z = min(c.z for c in coords)
    max_z = max(c.z for c in coords)
    span = max(max_y - min_y, max_z - min_z, max(c.x for c in coords) - min(c.x for c in coords))
    center_y = sum(c.y for c in coords) / len(coords)
    print(f"  n {len(cluster)} y {min_y:.3f}..{max_y:.3f} z {min_z:.3f}..{max_z:.3f} span {span:.3f}")
    # Near the hand (y close to 0), small, and tall enough to be the hanging trigger.
    if len(cluster) < 6 or span > 0.05 or center_y < -0.12:
        continue
    if best is None or len(cluster) < len(best):
        best = cluster

if best is None:
    raise SystemExit("no trigger cluster")

chosen_ids = {vi for vi, _ in best}
print("PICKED", len(chosen_ids))

bm = bmesh.new()
bm.from_mesh(mesh)
bm.faces.ensure_lookup_table()
bm_uv = bm.loops.layers.uv.active
faces = [face for face in bm.faces if all(loop.vert.index in chosen_ids for loop in face.loops)]
print("TRIGGER_FACES", len(faces))

new_verts = []
new_uvs = []
new_faces = []
index_of = {}
for face in faces:
    corners = []
    for loop in face.loops:
        key = loop.vert.index
        if key not in index_of:
            index_of[key] = len(new_verts)
            new_verts.append(loop.vert.co.copy())
        corners.append(index_of[key])
        if bm_uv is not None:
            new_uvs.append(loop[bm_uv].uv.copy())
    new_faces.append(corners)

bmesh.ops.delete(bm, geom=faces, context="FACES")
bm.to_mesh(mesh)
mesh.update()
bm.free()

trigger_mesh = bpy.data.meshes.new("TriggerMesh")
trigger_mesh.from_pydata(new_verts, [], new_faces)
trigger_mesh.update()
if new_uvs:
    layer = trigger_mesh.uv_layers.new(name="UVMap")
    for i, value in enumerate(new_uvs):
        layer.data[i].uv = value
if mesh.materials:
    trigger_mesh.materials.append(mesh.materials[0])

trigger = bpy.data.objects.new("Trigger", trigger_mesh)
bpy.context.collection.objects.link(trigger)
top_z = max(v.z for v in new_verts)
top = [v for v in new_verts if v.z > top_z - 0.003]
pivot = sum(top, Vector()) / len(top)
bpy.context.scene.cursor.location = pivot
bpy.ops.object.select_all(action="DESELECT")
trigger.select_set(True)
bpy.context.view_layer.objects.active = trigger
bpy.ops.object.origin_set(type="ORIGIN_CURSOR")

before = min((trigger.matrix_world @ v.co).y for v in trigger.data.vertices)
trigger.rotation_euler.x = 0.45
bpy.context.view_layer.update()
after = min((trigger.matrix_world @ v.co).y for v in trigger.data.vertices)
trigger.rotation_euler.x = 0.0
bpy.context.view_layer.update()
print("BOTTOM_Y", round(before, 4), "->", round(after, 4), "VERTS", len(new_verts))

bpy.ops.object.select_all(action="DESELECT")
trigger.select_set(True)
gun.select_set(True)
bpy.context.view_layer.objects.active = gun
bpy.ops.object.parent_set(type="OBJECT", keep_transform=True)

bpy.ops.object.select_all(action="DESELECT")
gun.select_set(True)
for child in gun.children:
    child.select_set(True)
bpy.context.view_layer.objects.active = gun
bpy.ops.export_scene.fbx(
    filepath=dst,
    use_selection=True,
    apply_scale_options="FBX_SCALE_ALL",
    axis_forward="-Z",
    axis_up="Y",
    object_types={"MESH", "EMPTY"},
    add_leaf_bones=False,
    bake_anim=False,
    mesh_smooth_type="FACE",
    path_mode="STRIP",
    embed_textures=False,
)
print("WROTE", dst)
