"""Replace GripShotgun gun meshes with the textured export, in the same local space.

Hand, wrist, muzzle, and object transforms stay put. Textures are written beside the model.
"""
import os

import bpy
import numpy as np
from mathutils import Euler, Matrix, Vector

GLB = r"C:\Users\joepi\Downloads\shotgun+with+texture.glb"
GRIP = r"C:\Users\joepi\Repos\HD2\Assets\Models\Shotgun\GripShotgun.fbx"
TEX = r"C:\Users\joepi\Repos\HD2\Assets\Models\Shotgun\Textures"
os.makedirs(TEX, exist_ok=True)


def norm_name(name):
    if name.lower() == "trigger":
        return "Trigger"
    return name


def capture_glb():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=GLB)
    parts = {}
    for obj in bpy.context.scene.objects:
        if obj.type != "MESH":
            continue
        me = obj.data
        uv_layer = me.uv_layers.active.data
        faces = []
        uvs = []
        for poly in me.polygons:
            face = []
            for li in poly.loop_indices:
                face.append(me.loops[li].vertex_index)
                uvs.append(tuple(uv_layer[li].uv))
            faces.append(tuple(face))
        world = np.array([tuple(obj.matrix_world @ v.co) for v in me.vertices], dtype=np.float64)
        image = None
        if obj.material_slots and obj.material_slots[0].material:
            mat = obj.material_slots[0].material
            if mat.use_nodes:
                for node in mat.node_tree.nodes:
                    if node.type == "TEX_IMAGE" and node.image and node.image.size[0] > 0:
                        image = node.image
                        break
        parts[norm_name(obj.name)] = {
            "world": world,
            "faces": faces,
            "uvs": uvs,
            "image": image,
        }
        if image is not None:
            path = os.path.join(TEX, norm_name(obj.name) + ".png")
            image.filepath_raw = path
            image.file_format = "PNG"
            image.save()
            print("TEX", path, image.size[0], image.size[1])
    return parts


def object_matrix(obj):
    return np.array(obj.matrix_world, dtype=np.float64)


def fit_transform(src_parts, dst_objects):
    names = sorted(set(src_parts) & set(dst_objects))
    src = np.array([src_parts[n]["world"].mean(0) for n in names])
    dst = []
    for n in names:
        obj = dst_objects[n]
        co = np.array([tuple(obj.matrix_world @ v.co) for v in obj.data.vertices], dtype=np.float64)
        dst.append(co.mean(0))
    dst = np.array(dst)
    best = None
    for mirror in (1.0, -1.0):
        mirrored = src * np.array([mirror, 1.0, 1.0])
        for rx in (0, 90, 180, 270):
            for ry in (0, 90, 180, 270):
                for rz in (0, 90, 180, 270):
                    rot = np.array(Euler((np.radians(rx), np.radians(ry), np.radians(rz)), "XYZ").to_matrix(), dtype=np.float64)
                    moved = mirrored @ rot.T
                    a = moved - moved.mean(0)
                    b = dst - dst.mean(0)
                    scale = float((a * b).sum() / max((a * a).sum(), 1e-12))
                    if scale <= 0:
                        continue
                    trans = dst.mean(0) - scale * moved.mean(0)
                    pred = scale * moved + trans
                    err = float(np.linalg.norm(pred - dst, axis=1).mean())
                    if best is None or err < best[0]:
                        best = (err, scale, rot, trans, mirror, (rx, ry, rz))
    return best


parts = capture_glb()

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=GRIP)
gun_objects = {}
for obj in bpy.context.scene.objects:
    if obj.type == "MESH" and not obj.name.startswith("Hand"):
        gun_objects[obj.name] = obj

missing = sorted(set(gun_objects) - set(parts))
extra = sorted(set(parts) - set(gun_objects))
print("MISSING", missing)
print("EXTRA", extra)
if missing or extra:
    raise SystemExit("part names do not match")

wrist_before = tuple(bpy.data.objects["Wrist"].matrix_world.translation)
err, scale, rot, trans, mirror, euler = fit_transform(parts, gun_objects)
print("FIT", "err", round(err, 5), "scale", round(scale, 5), "mirror", mirror, "euler", euler)
if err > 0.02:
    raise SystemExit("alignment error is too large")

linear = Matrix(rot.tolist())
align = Matrix.Translation(Vector(trans)) @ Matrix.Diagonal((scale, scale, scale, 1.0)) @ linear.to_4x4()
if mirror < 0:
    align = align @ Matrix.Diagonal((-1.0, 1.0, 1.0, 1.0))

for name, obj in gun_objects.items():
    src = parts[name]
    inv = obj.matrix_world.inverted()
    world_to_local = inv @ align
    verts = [world_to_local @ Vector(p) for p in src["world"]]
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], [list(f) for f in src["faces"]])
    uv = mesh.uv_layers.new(name="UVMap")
    corner = 0
    for poly in mesh.polygons:
        for li in poly.loop_indices:
            uv.data[li].uv = src["uvs"][corner]
            corner += 1
    mesh.update()
    obj.data = mesh
    print("MESH", name, "verts", len(mesh.vertices))

wrist_after = tuple(bpy.data.objects["Wrist"].matrix_world.translation)
print("WRIST", tuple(round(float(v), 4) for v in wrist_before), tuple(round(float(v), 4) for v in wrist_after))

bpy.ops.object.select_all(action="SELECT")
bpy.context.view_layer.objects.active = bpy.data.objects["GripShotgun"]
bpy.ops.export_scene.fbx(
    filepath=GRIP,
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
print("EXPORTED", GRIP)
