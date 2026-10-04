import bpy
import numpy as np
from mathutils import Matrix

src = r"c:\Users\joepi\Downloads\assets\Ready for VR\bot_white.glb"
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
obj = next(o for o in bpy.data.objects if o.type == "MESH")
scale = 1.75 / obj.dimensions.z
obj.data.transform(Matrix.Diagonal((scale, scale, scale, 1.0)))
obj.data.update()
verts = np.array([tuple(v.co) for v in obj.data.vertices])
# Left hand, past the wrist.
hand = verts[(verts[:, 0] > 0.62) & (verts[:, 2] > 1.2) & (verts[:, 2] < 1.55)]
print("HAND", len(hand))
print("X", hand[:, 0].min(), hand[:, 0].max())
print("Y", hand[:, 1].min(), hand[:, 1].max())
print("Z", hand[:, 2].min(), hand[:, 2].max())
xs = np.linspace(hand[:, 0].min(), hand[:, 0].max(), 8)
print("X_SLICES y z count")
for i in range(len(xs) - 1):
    m = (hand[:, 0] >= xs[i]) & (hand[:, 0] < xs[i + 1])
    if m.sum() < 3:
        continue
    sl = hand[m]
    print(f"  {xs[i]:.3f} y {sl[:,1].min():.3f}..{sl[:,1].max():.3f} z {sl[:,2].min():.3f}..{sl[:,2].max():.3f} n {m.sum()}")

# Fingers spread along Z or Y at the outer hand.
outer = hand[hand[:, 0] > 0.70]
print("OUTER", len(outer))
if len(outer):
    zs = np.linspace(outer[:, 2].min(), outer[:, 2].max(), 6)
    print("OUTER_Z")
    for i in range(len(zs) - 1):
        m = (outer[:, 2] >= zs[i]) & (outer[:, 2] < zs[i + 1])
        if m.sum() < 2:
            continue
        sl = outer[m]
        print(f"  z {zs[i]:.3f} n {m.sum()} y {sl[:,1].mean():.3f} x {sl[:,0].mean():.3f}")
    ys = np.linspace(outer[:, 1].min(), outer[:, 1].max(), 6)
    print("OUTER_Y")
    for i in range(len(ys) - 1):
        m = (outer[:, 1] >= ys[i]) & (outer[:, 1] < ys[i + 1])
        if m.sum() < 2:
            continue
        sl = outer[m]
        print(f"  y {ys[i]:.3f} n {m.sum()} z {sl[:,2].mean():.3f} x {sl[:,0].mean():.3f}")
