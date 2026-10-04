import bpy
from mathutils import Euler, Quaternion
from math import radians

e = Euler((radians(180), radians(180), radians(180)), "YXZ")
print("BLENDER_YXZ", e.to_quaternion())
e = Euler((radians(180), radians(180), radians(180)), "ZXY")
print("BLENDER_ZXY", e.to_quaternion())

for src in [
    r"c:\Users\joepi\Repos\HD2\Assets\Models\SMG\Smg.fbx",
    r"c:\Users\joepi\Repos\HD2\Assets\Models\Pistol\StartingPistol.fbx",
]:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=src)
    print("===", src.split("\\")[-1])
    for obj in bpy.data.objects:
        q = obj.matrix_world.to_quaternion()
        print(f"  {obj.name} quat {[round(v, 3) for v in q]} euler_xyz {[round(v, 1) for v in obj.matrix_world.to_euler()]}")
