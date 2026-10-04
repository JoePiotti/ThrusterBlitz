"""Decimate the Meshy SMG in several passes and render preview angles."""
import bpy
import math
import os
from mathutils import Vector

src = r"c:\Users\joepi\Downloads\Meshy_AI_Crimson_Vector_SMG_1002124053_texture.glb"
preview_dir = r"c:\Users\joepi\Repos\HD2\Tools\blender\smg_preview"
os.makedirs(preview_dir, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)

obj = next(o for o in bpy.data.objects if o.type == "MESH")
obj.name = "Smg"

def tri_count(mesh_obj):
    mesh_obj.data.calc_loop_triangles()
    return len(mesh_obj.data.loop_triangles)

print("START_TRIS", tri_count(obj))

bpy.context.view_layer.objects.active = obj
obj.select_set(True)
bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.mesh.select_all(action="SELECT")
bpy.ops.mesh.remove_doubles(threshold=0.00015)
bpy.ops.object.mode_set(mode="OBJECT")
print("AFTER_WELD", tri_count(obj))

# Several collapse passes keep the silhouette better than one hard cut.
target = 4500
passes = 0
while tri_count(obj) > target and passes < 6:
    current = tri_count(obj)
    ratio = max(0.28, min(0.45, target / current))
    if current * ratio > target * 1.8:
        ratio = 0.4
    mod = obj.modifiers.new(name="Decimate", type="DECIMATE")
    mod.decimate_type = "COLLAPSE"
    mod.ratio = ratio
    mod.use_collapse_triangulate = True
    bpy.ops.object.modifier_apply(modifier=mod.name)
    passes += 1
    print(f"PASS {passes} ratio={ratio:.3f} tris={tri_count(obj)}")

# One last pass if we are still well above the target.
if tri_count(obj) > target * 1.15:
    current = tri_count(obj)
    ratio = max(0.35, target / current)
    mod = obj.modifiers.new(name="Decimate", type="DECIMATE")
    mod.decimate_type = "COLLAPSE"
    mod.ratio = ratio
    mod.use_collapse_triangulate = True
    bpy.ops.object.modifier_apply(modifier=mod.name)
    print(f"FINAL ratio={ratio:.3f} tris={tri_count(obj)}")

print("DIM", [round(v, 4) for v in obj.dimensions])
print("DONE_TRIS", tri_count(obj), "VERTS", len(obj.data.vertices))

# Preview renders so the silhouette can be checked before export.
scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE_NEXT"
scene.render.resolution_x = 640
scene.render.resolution_y = 480
scene.render.film_transparent = False
scene.eevee.taa_render_samples = 16
world = bpy.data.worlds.new("Preview")
scene.world = world
world.use_nodes = True
bg = world.node_tree.nodes.get("Background")
if bg:
    bg.inputs[0].default_value = (0.18, 0.18, 0.2, 1)
    bg.inputs[1].default_value = 1.0

bpy.ops.object.light_add(type="SUN", location=(2, -2, 4))
sun = bpy.context.active_object
sun.data.energy = 3.5
bpy.ops.object.light_add(type="AREA", location=(-2, 2, 1))
fill = bpy.context.active_object
fill.data.energy = 200
fill.data.size = 3

bpy.ops.object.camera_add()
cam = bpy.context.active_object
scene.camera = cam
cam.data.type = "ORTHO"

center = sum((Vector(corner) for corner in obj.bound_box), Vector()) / 8
center = obj.matrix_world @ center
extent = max(obj.dimensions) * 0.65
cam.data.ortho_scale = max(obj.dimensions) * 1.35

views = {
    "front": (center + Vector((0, -extent * 2, 0)), (math.radians(90), 0, 0)),
    "side": (center + Vector((extent * 2, 0, 0)), (math.radians(90), 0, math.radians(90))),
    "top": (center + Vector((0, 0, extent * 2)), (0, 0, 0)),
    "threequarter": (center + Vector((extent, -extent, extent * 0.7)), (math.radians(60), 0, math.radians(45))),
}

for name, (loc, rot) in views.items():
    cam.location = loc
    cam.rotation_euler = rot
    scene.render.filepath = os.path.join(preview_dir, name + ".png")
    bpy.ops.render.render(write_still=True)
    print("RENDERED", name)

bpy.ops.wm.save_as_mainfile(filepath=r"c:\Users\joepi\Repos\HD2\Tools\blender\smg.blend")
print("SAVED_BLEND")
