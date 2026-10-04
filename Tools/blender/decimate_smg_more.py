"""One more collapse pass from the 4500-tri blend, then a check render."""
import bpy
import math
import os
from mathutils import Vector

bpy.ops.wm.open_mainfile(filepath=r"c:\Users\joepi\Repos\HD2\Tools\blender\smg.blend")
obj = bpy.data.objects["Smg"]
bpy.context.view_layer.objects.active = obj
obj.select_set(True)

def tri_count():
    obj.data.calc_loop_triangles()
    return len(obj.data.loop_triangles)

print("BEFORE", tri_count())
mod = obj.modifiers.new(name="Decimate", type="DECIMATE")
mod.decimate_type = "COLLAPSE"
mod.ratio = 2000 / tri_count()
mod.use_collapse_triangulate = True
bpy.ops.object.modifier_apply(modifier=mod.name)
print("AFTER", tri_count(), "VERTS", len(obj.data.vertices))

scene = bpy.context.scene
cam = scene.camera
center = obj.matrix_world @ (sum((Vector(c) for c in obj.bound_box), Vector()) / 8)
extent = max(obj.dimensions) * 0.65
cam.location = center + Vector((extent, -extent, extent * 0.7))
cam.rotation_euler = (math.radians(60), 0, math.radians(45))
scene.render.filepath = r"c:\Users\joepi\Repos\HD2\Tools\blender\smg_preview\low.png"
bpy.ops.render.render(write_still=True)
bpy.ops.wm.save_as_mainfile(filepath=r"c:\Users\joepi\Repos\HD2\Tools\blender\smg_low.blend")
print("SAVED_LOW")
