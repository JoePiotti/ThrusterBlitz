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

        var trigger = AssetDatabase.LoadAssetAtPath<Material>(Hd2SmgSetup.TriggerMaterialPath);
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
            renderers[i].sharedMaterial = renderers[i].gameObject.name == "Trigger" && trigger != null
                ? trigger
                : material;
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
    public const string TriggerColorPath = "Assets/Models/SMG/Smg_Trigger_BaseColor.png";
    public const string TriggerMaterialPath = "Assets/Models/SMG/SmgTrigger.mat";

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

        // The new SMG only has a base color. A flat normal and metal map hide that paint.
        material.SetTexture("_BumpMap", null);
        material.SetTexture("_MetallicGlossMap", null);
        material.SetFloat("_Metallic", 0f);
        material.SetFloat("_Smoothness", 0.5f);
        material.DisableKeyword("_NORMALMAP");
        material.DisableKeyword("_METALLICSPECGLOSSMAP");

        EditorUtility.SetDirty(material);
        var triggerMaterial = EnsureTrigger(shader);

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model != null && MaterialsMatch(model, material, triggerMaterial))
            return;

        AssetDatabase.ImportAsset(ModelPath);
    }

    static Material EnsureTrigger(Shader shader)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(TriggerMaterialPath);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, TriggerMaterialPath);
        }

        var color = AssetDatabase.LoadAssetAtPath<Texture2D>(TriggerColorPath);
        if (color != null)
        {
            material.SetTexture("_BaseMap", color);
            material.SetTexture("_MainTex", color);
        }

        EditorUtility.SetDirty(material);
        return material;
    }

    static bool MaterialsMatch(GameObject model, Material body, Material trigger)
    {
        var renderers = model.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return false;

        for (int i = 0; i < renderers.Length; i++)
        {
            Material expected = renderers[i].gameObject.name == "Trigger" ? trigger : body;
            if (renderers[i].sharedMaterial != expected)
                return false;
        }

        return true;
    }
}
