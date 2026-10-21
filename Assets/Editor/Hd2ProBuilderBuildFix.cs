using System.Reflection;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
using UnityEngine.ProBuilder;

/// <summary>
/// ProBuilder's build step rebuilds every mesh, then crashes when one has no faces.
/// Repair those meshes first and keep the mesh that is already saved on the object.
/// </summary>
static class Hd2ProBuilderBuildFix
{
    [PostProcessScene(-1000)]
    public static void RepairMeshes()
    {
        var versionField = typeof(ProBuilderMesh).GetField("m_MeshFormatVersion", BindingFlags.Instance | BindingFlags.NonPublic);
        var facesField = typeof(ProBuilderMesh).GetField("m_Faces", BindingFlags.Instance | BindingFlags.NonPublic);
        var positionsField = typeof(ProBuilderMesh).GetField("m_Positions", BindingFlags.Instance | BindingFlags.NonPublic);
        var meshes = Resources.FindObjectsOfTypeAll<ProBuilderMesh>();
        for (int i = 0; i < meshes.Length; i++)
        {
            ProBuilderMesh mesh = meshes[i];
            if (mesh == null || EditorUtility.IsPersistent(mesh))
                continue;

            int version = versionField != null ? (int)versionField.GetValue(mesh) : 2;
            bool facesMissing = facesField != null && facesField.GetValue(mesh) == null;
            MeshRenderer renderer = mesh.GetComponent<MeshRenderer>();
            if (version >= 2 && !facesMissing && renderer != null && renderer.sharedMaterials != null && renderer.sharedMaterials.Length > 0)
                continue;

            MeshFilter filter = mesh.GetComponent<MeshFilter>();
            bool baked = filter != null && filter.sharedMesh != null && filter.sharedMesh.vertexCount > 0;
            if (baked && (version < 1 || facesMissing))
            {
                Debug.LogWarning("Kept the saved mesh on " + mesh.name + " and removed broken ProBuilder data so the build can finish.");
                Component[] components = mesh.GetComponents<Component>();
                for (int c = 0; c < components.Length; c++)
                {
                    Component component = components[c];
                    if (component == null || component == mesh)
                        continue;
                    if (component.GetType().Name == "ProBuilderShape" || component is PolyShape)
                        Object.DestroyImmediate(component);
                }

                Object.DestroyImmediate(mesh);
                continue;
            }

            if (renderer == null)
                renderer = mesh.gameObject.AddComponent<MeshRenderer>();
            if (renderer.sharedMaterials == null || renderer.sharedMaterials.Length == 0)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader != null)
                    renderer.sharedMaterial = new Material(shader);
            }

            if (facesMissing)
                facesField.SetValue(mesh, System.Array.Empty<Face>());
            if (positionsField != null && positionsField.GetValue(mesh) == null)
                positionsField.SetValue(mesh, System.Array.Empty<Vector3>());
            if (version < 2 && versionField != null)
                versionField.SetValue(mesh, 2);
        }
    }
}
