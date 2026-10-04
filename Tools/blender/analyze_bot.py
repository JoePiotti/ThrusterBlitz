import bpy
import numpy as np

src = r"c:\Users\joepi\Downloads\assets\Ready for VR\bot_white.glb"
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
obj = next(o for o in bpy.data.objects if o.type == "MESH")
from mathutils import Matrix
height = obj.dimensions.z
scale = 1.75 / height
obj.data.transform(Matrix.Diagonal((scale, scale, scale, 1)))
obj.data.update()

verts = np.array([v.co[:] for v in obj.data.vertices])
z = verts[:, 2]
x = verts[:, 0]
print("HEIGHT", round(z.max() - z.min(), 3), "X", round(x.min(), 3), round(x.max(), 3))
edges = np.linspace(z.min(), z.max(), 28)
print("Z_SLICE half_x count")
for i in range(len(edges) - 1):
    m = (z >= edges[i]) & (z < edges[i + 1])
    if m.sum() < 15:
        continue
    print(f"  {edges[i]:.3f} {np.percentile(np.abs(x[m]), 95):.3f} {int(m.sum())}")
