import bpy

src = r"c:\Users\joepi\Downloads\Meshy_AI_Crimson_Vector_SMG_1002124053_texture.glb"
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)

obj = next(o for o in bpy.data.objects if o.type == "MESH")
print("DIM", [round(v, 4) for v in obj.dimensions])
print("LOC", [round(v, 4) for v in obj.location])
print("ROT", [round(v, 4) for v in obj.rotation_euler])
print("SCALE", [round(v, 4) for v in obj.scale])

mat = obj.data.materials[0]
print("MAT", mat.name, "nodes", mat.use_nodes)
if mat.use_nodes:
    for node in mat.node_tree.nodes:
        extra = ""
        if node.type == "TEX_IMAGE" and node.image:
            extra = f" {node.image.name} {node.image.size[0]}x{node.image.size[1]}"
        print(" NODE", node.type, node.name, extra)
    for link in mat.node_tree.links:
        print(" LINK", link.from_node.name, link.from_socket.name, "->", link.to_node.name, link.to_socket.name)
