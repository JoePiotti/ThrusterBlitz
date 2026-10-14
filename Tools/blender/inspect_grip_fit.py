import bpy
import numpy as np
from mathutils import Vector

def load(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    return meshes

def dump(label, meshes, name_filter=None):
    pts = []
    for obj in meshes:
        if name_filter and not name_filter(obj.name):
            continue
        co = np.array([tuple(obj.matrix_world @ v.co) for v in obj.data.vertices], dtype=np.float64)
        if len(co) == 0:
            continue
        pts.append(co)
        print(label, obj.name, "n", len(co),
              "min", np.round(co.min(0), 3),
              "max", np.round(co.max(0), 3),
              "mean", np.round(co.mean(0), 3))
    if not pts:
        print(label, "NONE")
        return None
    allp = np.vstack(pts)
    print(label, "ALL", "min", np.round(allp.min(0), 3), "max", np.round(allp.max(0), 3), "mean", np.round(allp.mean(0), 3))
    return allp

shot = load(r"C:\Users\joepi\Repos\HD2\Assets\Models\Shotgun\Shotgun.fbx")
print("--- SHOTGUN ---")
dump("SG", shot)
for obj in bpy.context.scene.objects:
    if obj.type == "EMPTY":
        print("EMPTY", obj.name, "loc", tuple(round(v, 4) for v in obj.matrix_world.translation))

rocket = load(r"C:\Users\joepi\Repos\HD2\Assets\Models\Rocket\GripRocket.fbx")
print("--- ROCKET HAND ---")
dump("HAND", rocket, lambda n: n.startswith("Hand_"))
print("--- ROCKET GUN ---")
dump("GUN", rocket, lambda n: not n.startswith("Hand_"))
for obj in bpy.context.scene.objects:
    if obj.type == "EMPTY":
        print("EMPTY", obj.name, "parent", obj.parent.name if obj.parent else "-", "loc", tuple(round(v, 4) for v in obj.matrix_world.translation))
