"""Unit ramp for HD2. 1 m wide, 1 m tall, 1 m long. Square back and bottom, triangle sides."""
import bpy
import os

dst_dir = r"C:\Users\joepi\Repos\HD2\Assets\Models\Ramp"
os.makedirs(dst_dir, exist_ok=True)
dst = os.path.join(dst_dir, "RampWedge.fbx")

bpy.ops.wm.read_factory_settings(use_empty=True)

# Blender is Z-up. Height is Z, run is Y, width is X.
# The lip is at +Y. The tall back is at -Y. Export below maps this to Unity Y-up, +Z lip.
verts = [
    (-0.5, -0.5, 0.0),
    (0.5, -0.5, 0.0),
    (0.5, 0.5, 0.0),
    (-0.5, 0.5, 0.0),
    (-0.5, -0.5, 1.0),
    (0.5, -0.5, 1.0),
]
faces = [
    (0, 3, 2, 1),  # bottom
    (0, 1, 5, 4),  # back
    (4, 5, 2, 3),  # slope
    (0, 4, 3),     # left
    (1, 2, 5),     # right
]
uvs = {
    (0, 3, 2, 1): [(0, 0), (0, 1), (1, 1), (1, 0)],
    (0, 1, 5, 4): [(0, 0), (1, 0), (1, 1), (0, 1)],
    (4, 5, 2, 3): [(0, 1), (1, 1), (1, 0), (0, 0)],
    (0, 4, 3): [(0, 0), (0, 1), (1, 1)],
    (1, 2, 5): [(1, 0), (0, 1), (1, 1)],
}

mesh = bpy.data.meshes.new("RampWedge")
obj = bpy.data.objects.new("RampWedge", mesh)
bpy.context.collection.objects.link(obj)
mesh.from_pydata(verts, [], faces)
mesh.update()

uv_layer = mesh.uv_layers.new(name="UVMap")
for poly in mesh.polygons:
    coords = uvs[tuple(poly.vertices)]
    for loop_index, coord in zip(poly.loop_indices, coords):
        uv_layer.data[loop_index].uv = coord

bpy.context.view_layer.objects.active = obj
obj.select_set(True)
bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.mesh.normals_make_consistent(inside=False)
bpy.ops.object.mode_set(mode="OBJECT")

bpy.ops.export_scene.fbx(
    filepath=dst,
    use_selection=True,
    object_types={"MESH"},
    apply_unit_scale=True,
    bake_space_transform=True,
    axis_forward="-Z",
    axis_up="Y",
    global_scale=1.0,
    mesh_smooth_type="FACE",
)
print("exported", dst)
