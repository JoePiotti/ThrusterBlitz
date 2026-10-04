import bpy
import os

files = [
    r"c:\Users\joepi\Downloads\assets\Ready for VR\starting pistol.glb",
    r"c:\Users\joepi\Downloads\assets\Ready for VR\bot_white.glb",
]

for src in files:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=src)
    print("===", os.path.basename(src), "===")
    for obj in bpy.data.objects:
        extra = ""
        if obj.type == "MESH":
            obj.data.calc_loop_triangles()
            extra = f" verts={len(obj.data.vertices)} tris={len(obj.data.loop_triangles)} mats={len(obj.data.materials)} dim={[round(v,3) for v in obj.dimensions]}"
        if obj.type == "ARMATURE":
            names = [b.name for b in obj.data.bones]
            extra = f" bones={len(names)} " + ", ".join(names[:40])
        print(f"  {obj.type:10} {obj.name} parent={obj.parent.name if obj.parent else '-'}{extra}")
    print("  IMAGES")
    for img in bpy.data.images:
        print(f"    {img.name} {img.size[0]}x{img.size[1]}")
    print("  MATERIALS")
    for mat in bpy.data.materials:
        print(f"    {mat.name}")
