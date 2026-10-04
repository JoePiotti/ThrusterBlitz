using UnityEditor;
using UnityEngine;

/// <summary>
/// Puts the gripped-pistol models on the URP lit shader. Does not equip them.
/// </summary>
public class Hd2GripPistolImporter : AssetPostprocessor
{
    void OnPostprocessModel(GameObject root)
    {
        if (assetPath != Hd2GripPistolSetup.LeftPath && assetPath != Hd2GripPistolSetup.RightPath)
            return;

        var material = AssetDatabase.LoadAssetAtPath<Material>(Hd2GripPistolSetup.MaterialPath);
        if (material == null)
            return;

        var renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
            renderers[i].sharedMaterial = material;
    }
}

[InitializeOnLoad]
public static class Hd2GripPistolSetup
{
    public const string LeftPath = "Assets/Models/Pistol/GripPistolLeft.fbx";
    public const string RightPath = "Assets/Models/Pistol/GripPistolRight.fbx";
    public const string ColorPath = "Assets/Models/Pistol/GripPistol_BaseColor.png";
    public const string MaterialPath = "Assets/Models/Pistol/GripPistol.mat";

    static Hd2GripPistolSetup()
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
