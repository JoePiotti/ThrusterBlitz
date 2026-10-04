using UnityEditor;
using UnityEngine;

/// <summary>
/// Keeps the decimated SMG on the URP lit shader with its own textures.
/// Does not attach it to a hand.
/// </summary>
public class Hd2SmgImporter : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        if (assetPath == Hd2SmgSetup.NormalPath)
        {
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.NormalMap;
            importer.sRGBTexture = false;
        }
        else if (assetPath == Hd2SmgSetup.MetalPath)
        {
            var importer = (TextureImporter)assetImporter;
            importer.sRGBTexture = false;
        }
    }

    void OnPostprocessModel(GameObject root)
    {
        if (assetPath != Hd2SmgSetup.ModelPath)
            return;

        var material = AssetDatabase.LoadAssetAtPath<Material>(Hd2SmgSetup.MaterialPath);
        if (material == null)
            return;

        var renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
            renderers[i].sharedMaterial = material;
    }
}

[InitializeOnLoad]
public static class Hd2SmgSetup
{
    public const string ModelPath = "Assets/Models/SMG/Smg.fbx";
    public const string ColorPath = "Assets/Models/SMG/Smg_BaseColor.png";
    public const string NormalPath = "Assets/Models/SMG/Smg_Normal.png";
    public const string MetalPath = "Assets/Models/SMG/Smg_Metallic.png";
    public const string MaterialPath = "Assets/Models/SMG/Smg.mat";

    static Hd2SmgSetup()
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

        if (AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath) == null)
            return;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            return;

        ForceLinear(NormalPath, true);
        ForceLinear(MetalPath, false);

        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, MaterialPath);
        }

        var color = AssetDatabase.LoadAssetAtPath<Texture2D>(ColorPath);
        var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(NormalPath);
        var metal = AssetDatabase.LoadAssetAtPath<Texture2D>(MetalPath);
        if (color != null)
        {
            material.SetTexture("_BaseMap", color);
            material.SetTexture("_MainTex", color);
        }

        if (normal != null)
        {
            material.SetTexture("_BumpMap", normal);
            material.SetFloat("_BumpScale", 1f);
            material.EnableKeyword("_NORMALMAP");
        }

        if (metal != null)
        {
            material.SetTexture("_MetallicGlossMap", metal);
            material.SetFloat("_Metallic", 1f);
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
        }

        EditorUtility.SetDirty(material);

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        var renderer = model.GetComponentInChildren<Renderer>(true);
        if (renderer != null && renderer.sharedMaterial == material)
            return;

        AssetDatabase.ImportAsset(ModelPath);
    }

    static void ForceLinear(string path, bool normalMap)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
            return;

        bool changed = false;
        if (normalMap && importer.textureType != TextureImporterType.NormalMap)
        {
            importer.textureType = TextureImporterType.NormalMap;
            changed = true;
        }

        if (importer.sRGBTexture)
        {
            importer.sRGBTexture = false;
            changed = true;
        }

        if (changed)
            importer.SaveAndReimport();
    }
}
