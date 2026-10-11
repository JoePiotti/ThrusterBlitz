import bpy

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=r"C:\Users\joepi\Repos\HD2\Assets\Models\Ramp\RampWedge.fbx")
for obj in bpy.data.objects:
    if obj.type != "MESH":
        continue
    print("OBJECT", obj.name)
    print("MATRIX", [round(x, 3) for row in obj.matrix_world for x in row])
    for vert in obj.data.vertices:
        world = obj.matrix_world @ vert.co
        print("V", round(world.x, 3), round(world.y, 3), round(world.z, 3))
