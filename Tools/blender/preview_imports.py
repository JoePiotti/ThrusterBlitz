import bpy
import math
import os
from mathutils import Vector

pairs = [
    (r"c:\Users\joepi\Downloads\assets\Ready for VR\starting pistol.glb", "pistol"),
    (r"c:\Users\joepi\Downloads\assets\Ready for VR\bot_white.glb", "bot"),
]
out_dir = r"c:\Users\joepi\Repos\HD2\Tools\blender\import_preview"
os.makedirs(out_dir, exist_ok=True)

for src, name in pairs:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=src)
    obj = next(o for o in bpy.data.objects if o.type == "MESH")
    print(name, "DIM", [round(v, 4) for v in obj.dimensions], "LOC", [round(v, 4) for v in obj.location])
    # bounds
    verts = [obj.matrix_world @ v.co for v in obj.data.vertices]
    xs = [v.x for v in verts]
    ys = [v.y for v in verts]
    zs = [v.z for v in verts]
    print(name, "X", round(min(xs), 3), round(max(xs), 3), "Y", round(min(ys), 3), round(max(ys), 3), "Z", round(min(zs), 3), round(max(zs), 3))

    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE_NEXT"
    scene.render.resolution_x = 640
    scene.render.resolution_y = 480
    scene.eevee.taa_render_samples = 8
    world = bpy.data.worlds.new("Preview")
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs[0].default_value = (0.18, 0.18, 0.2, 1)
    bpy.ops.object.light_add(type="SUN", location=(2, -2, 4))
    bpy.context.active_object.data.energy = 3
    bpy.ops.object.camera_add()
    cam = bpy.context.active_object
    scene.camera = cam
    cam.data.type = "ORTHO"
    center = obj.matrix_world @ (sum((Vector(c) for c in obj.bound_box), Vector()) / 8)
    extent = max(obj.dimensions)
    cam.data.ortho_scale = extent * 1.4
    views = {
        "side": (center + Vector((0, -extent, 0)), (math.radians(90), 0, 0)),
        "front": (center + Vector((extent, 0, 0)), (math.radians(90), 0, math.radians(90))),
        "top": (center + Vector((0, 0, extent)), (0, 0, 0)),
    }
    for view, (loc, rot) in views.items():
        cam.location = loc
        cam.rotation_euler = rot
        scene.render.filepath = os.path.join(out_dir, f"{name}_{view}.png")
        bpy.ops.render.render(write_still=True)
        print("RENDER", name, view)
