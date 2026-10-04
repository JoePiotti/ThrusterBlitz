using UnityEditor;
using UnityEngine;

/// <summary>
/// Puts the gripped SMG models on the URP lit shader. The pickup sphere keeps the old SMG.
/// </summary>
public class Hd2GripSmgImporter : AssetPostprocessor
{
    void OnPostprocessModel(GameObject root)
    {
        if (assetPath != Hd2GripSmgSetup.LeftPath && assetPath != Hd2GripSmgSetup.RightPath)
            return;

        var material = AssetDatabase.LoadAssetAtPath<Material>(Hd2GripSmgSetup.MaterialPath);
        if (material == null)
            return;

        var renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
            renderers[i].sharedMaterial = material;
    }
}

[InitializeOnLoad]
public static class Hd2GripSmgSetup
{
    public const string LeftPath = "Assets/Models/SMG/GripSmgLeft.fbx";
    public const string RightPath = "Assets/Models/SMG/GripSmgRight.fbx";
    public const string ColorPath = "Assets/Models/SMG/GripSmg_BaseColor.png";
    public const string MaterialPath = "Assets/Models/SMG/GripSmg.mat";

    static Hd2GripSmgSetup()
    {
        EditorApplication.delayCall += EnsureMaterial;
    }

    static void EnsureMaterial()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
        {
            EditorApplication.delayCall += EnsureMaterial;
            return;
        }

        if (AssetDatabase.LoadAssetAtPath<GameObject>(LeftPath) == null)
            return;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            return;

        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, MaterialPath);
        }

        var color = AssetDatabase.LoadAssetAtPath<Texture2D>(ColorPath);
        if (color != null)
        {
            material.SetTexture("_BaseMap", color);
            material.SetTexture("_MainTex", color);
        }

        EditorUtility.SetDirty(material);
        AssignIfNeeded(LeftPath, material);
        AssignIfNeeded(RightPath, material);
    }

    static void AssignIfNeeded(string path, Material material)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        var renderer = model != null ? model.GetComponentInChildren<Renderer>(true) : null;
        if (renderer != null && renderer.sharedMaterial == material)
            return;

        AssetDatabase.ImportAsset(path);
    }
}
