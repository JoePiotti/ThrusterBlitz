import bpy
import numpy as np

files = [
    r"c:\Users\joepi\Repos\HD2\Assets\Models\Pistol\StartingPistol.fbx",
    r"c:\Users\joepi\Repos\HD2\Assets\Models\SMG\Smg.fbx",
]
for src in files:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=src)
    print("===", src.split("\\")[-1], "===")
    for obj in bpy.data.objects:
        if obj.type != "MESH":
            print(" ", obj.type, obj.name)
            continue
        verts = np.array([tuple(obj.matrix_world @ v.co) for v in obj.data.vertices])
        print(obj.name, "n", len(verts), "dim", [round(v, 3) for v in obj.dimensions])
        print("  X", verts[:, 0].min().round(3), verts[:, 0].max().round(3))
        print("  Y", verts[:, 1].min().round(3), verts[:, 1].max().round(3))
        print("  Z", verts[:, 2].min().round(3), verts[:, 2].max().round(3))
