"""Push the 2000-tri SMG down once more and render a check."""
import bpy
import math
from mathutils import Vector

bpy.ops.wm.open_mainfile(filepath=r"c:\Users\joepi\Repos\HD2\Tools\blender\smg_low.blend")
obj = bpy.data.objects["Smg"]
bpy.context.view_layer.objects.active = obj
obj.select_set(True)

def tri_count():
    obj.data.calc_loop_triangles()
    return len(obj.data.loop_triangles)

before = tri_count()
mod = obj.modifiers.new(name="Decimate", type="DECIMATE")
mod.decimate_type = "COLLAPSE"
mod.ratio = 800 / before
mod.use_collapse_triangulate = True
bpy.ops.object.modifier_apply(modifier=mod.name)
print("BEFORE", before, "AFTER", tri_count())

scene = bpy.context.scene
cam = scene.camera
center = obj.matrix_world @ (sum((Vector(c) for c in obj.bound_box), Vector()) / 8)
extent = max(obj.dimensions) * 0.65
cam.location = center + Vector((extent, -extent, extent * 0.7))
cam.rotation_euler = (math.radians(60), 0, math.radians(45))
scene.render.filepath = r"c:\Users\joepi\Repos\HD2\Tools\blender\smg_preview\lower.png"
bpy.ops.render.render(write_still=True)
bpy.ops.wm.save_as_mainfile(filepath=r"c:\Users\joepi\Repos\HD2\Tools\blender\smg_lower.blend")
print("SAVED")
