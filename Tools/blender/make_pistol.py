"""Original low-poly blockout pistol for HD2. Not based on any existing game gun."""
import bpy
import os

bpy.ops.wm.read_factory_settings(use_empty=True)

def box(name, loc, size):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = size
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    return obj

# Built +Y forward, then turned 180° around Z so the barrel lies on -Y.
# Unity bakes the Blender FBX -90° X node rotation into the mesh and resets
# the transform. That bake maps Blender -Y -> Unity +Z and Blender +Z -> Unity +Y.
# After import: barrel +Z (forward), sights +Y, grip -Y. Instance rotation stays identity.
parts = [
    box("Grip", (0.0, 0.012, -0.048), (0.030, 0.038, 0.092)),
    box("Slide", (0.0, -0.028, 0.012), (0.028, 0.145, 0.034)),
    box("Barrel", (0.0, -0.108, 0.006), (0.016, 0.070, 0.016)),
    box("FrontSight", (0.0, -0.136, 0.034), (0.006, 0.010, 0.014)),
    box("RearSightL", (0.010, 0.038, 0.034), (0.006, 0.014, 0.014)),
    box("RearSightR", (-0.010, 0.038, 0.034), (0.006, 0.014, 0.014)),
    box("Trigger", (0.0, -0.012, -0.010), (0.008, 0.012, 0.022)),
]

bpy.ops.object.select_all(action="DESELECT")
for part in parts:
    part.select_set(True)
bpy.context.view_layer.objects.active = parts[0]
bpy.ops.object.join()
gun = bpy.context.active_object
gun.name = "Pistol"

mat = bpy.data.materials.new("PistolDark")
mat.use_nodes = True
bsdf = mat.node_tree.nodes.get("Principled BSDF")
if bsdf is not None:
    bsdf.inputs["Base Color"].default_value = (0.16, 0.16, 0.17, 1.0)
    bsdf.inputs["Roughness"].default_value = 0.65
gun.data.materials.append(mat)

bpy.ops.object.empty_add(type="PLAIN_AXES", location=(0.0, -0.146, 0.006))
muzzle = bpy.context.active_object
muzzle.name = "Muzzle"
muzzle.empty_display_size = 0.02
muzzle.parent = gun

gun.data.calc_loop_triangles()
print("PISTOL_TRIS", len(gun.data.loop_triangles))

out_dir = r"c:\Users\joepi\Repos\HD2\Assets\Models\Pistol"
blend_dir = r"c:\Users\joepi\Repos\HD2\Tools\blender"
os.makedirs(out_dir, exist_ok=True)
os.makedirs(blend_dir, exist_ok=True)

bpy.ops.export_scene.fbx(
    filepath=os.path.join(out_dir, "Pistol.fbx"),
    use_selection=False,
    apply_scale_options="FBX_SCALE_ALL",
    axis_forward="-Z",
    axis_up="Y",
    object_types={"MESH", "EMPTY"},
    mesh_smooth_type="FACE",
    use_mesh_modifiers=True,
    add_leaf_bones=False,
)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(blend_dir, "pistol.blend"))
print("PISTOL_EXPORT_OK")
