import bpy

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=r"c:\Users\joepi\Repos\HD2\Assets\Models\SMG\Smg.fbx")
obj = next(o for o in bpy.data.objects if o.type == "MESH")
print("faces", len(obj.data.polygons))
for poly in obj.data.polygons:
    c = poly.center
    if -0.08 < c.y < 0.08 and -0.02 < c.z < 0.1 and abs(c.x) < 0.02:
        print(f"  {c.x: .4f} {c.y: .4f} {c.z: .4f} n {len(poly.vertices)}")
