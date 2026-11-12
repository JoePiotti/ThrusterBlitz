using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Puts the sci-fi pistol on URP lit materials and builds the gripped prefab.
/// GripPistol is the pistol in the player's hands, with the fist, wrist, and muzzle.
/// The left and right gun-only files stay for bots.
/// </summary>
public class Hd2GripPistolImporter : AssetPostprocessor
{
    void OnPreprocessModel()
    {
        string path = assetPath.Replace('\\', '/');
        if (!Hd2GripPistolSetup.IsPistolModel(path))
            return;

        var importer = (ModelImporter)assetImporter;
        importer.isReadable = true;
        importer.userData = "scifi-pistol-2";
    }

    void OnPostprocessModel(GameObject root)
    {
        string path = assetPath.Replace('\\', '/');
        if (!Hd2GripPistolSetup.IsPistolModel(path))
            return;

        NormalizeNames(root);
        var body = AssetDatabase.LoadAssetAtPath<Material>(Hd2GripPistolSetup.MaterialPath);
        var trigger = AssetDatabase.LoadAssetAtPath<Material>(Hd2GripPistolSetup.TriggerMaterialPath);
        var skin = AssetDatabase.LoadAssetAtPath<Material>("Assets/Models/Rocket/Materials/Hand_hand_palm.mat");
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Material material = MaterialFor(renderers[i].transform, body, trigger, skin);
            if (material == null)
                continue;
            var slots = renderers[i].sharedMaterials;
            for (int m = 0; m < slots.Length; m++)
                slots[m] = material;
            renderers[i].sharedMaterials = slots;
        }
    }

    static Material MaterialFor(Transform bone, Material body, Material trigger, Material skin)
    {
        if (IsHand(bone))
        {
            var hand = AssetDatabase.LoadAssetAtPath<Material>("Assets/Models/Rocket/Materials/" + bone.name + ".mat");
            return hand != null ? hand : skin;
        }

        if (bone.name.StartsWith("Trigger"))
            return trigger != null ? trigger : body;
        return body;
    }

    static void NormalizeNames(GameObject root)
    {
        var all = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            string name = all[i].name;
            if (name.StartsWith("Barrel"))
                all[i].name = "Barrel";
            else if (name.StartsWith("Trigger"))
                all[i].name = "Trigger";
            else if (name.StartsWith("Muzzle"))
                all[i].name = "Muzzle";
        }
    }

    static bool IsHand(Transform bone)
    {
        while (bone != null)
        {
            if (bone.name == "Hand" || bone.name.StartsWith("Hand_"))
                return true;
            bone = bone.parent;
        }

        return false;
    }
}

[InitializeOnLoad]
public static class Hd2GripPistolSetup
{
    public const string LeftPath = "Assets/Models/Pistol/GripPistolLeft.fbx";
    public const string RightPath = "Assets/Models/Pistol/GripPistolRight.fbx";
    public const string GripPath = "Assets/Models/Pistol/GripPistol.fbx";
    public const string PrefabPath = "Assets/Prefabs/GripWeapons/GripPistol.prefab";
    public const string ColorPath = "Assets/Models/Pistol/GripPistol_BaseColor.png";
    public const string TriggerColorPath = "Assets/Models/Pistol/GripPistol_Trigger_BaseColor.png";
    public const string MaterialPath = "Assets/Models/Pistol/GripPistol.mat";
    public const string TriggerMaterialPath = "Assets/Models/Pistol/GripPistolTrigger.mat";

    static Hd2GripPistolSetup()
    {
        EditorApplication.delayCall += Ensure;
    }

    public static bool IsPistolModel(string path)
    {
        return path == LeftPath || path == RightPath || path == GripPath;
    }

    static void Ensure()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
        {
            EditorApplication.delayCall += Ensure;
            return;
        }

        if (AssetDatabase.LoadAssetAtPath<Texture2D>(ColorPath) == null)
            return;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            return;

        var body = EnsureLit(shader, MaterialPath, ColorPath);
        var trigger = EnsureLit(shader, TriggerMaterialPath, TriggerColorPath);
        ImportIfNeeded(LeftPath, body, trigger);
        ImportIfNeeded(RightPath, body, trigger);
        ImportIfNeeded(GripPath, body, trigger);
        EnsurePrefab();
    }

    static Material EnsureLit(Shader shader, string materialPath, string texturePath)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, materialPath);
        }

        var color = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        if (color != null)
        {
            material.SetTexture("_BaseMap", color);
            material.SetTexture("_MainTex", color);
        }

        material.SetFloat("_Metallic", 0f);
        material.SetFloat("_Smoothness", 0.45f);
        EditorUtility.SetDirty(material);
        return material;
    }

    static void ImportIfNeeded(string path, Material body, Material trigger)
    {
        if (!File.Exists(path))
            return;

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (model != null && MaterialsMatch(model, body, trigger))
            return;

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
    }

    static bool MaterialsMatch(GameObject model, Material body, Material trigger)
    {
        var renderers = model.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return false;

        var skin = AssetDatabase.LoadAssetAtPath<Material>("Assets/Models/Rocket/Materials/Hand_hand_palm.mat");
        for (int i = 0; i < renderers.Length; i++)
        {
            Transform bone = renderers[i].transform;
            Material expected = body;
            if (bone.name.StartsWith("Trigger"))
                expected = trigger != null ? trigger : body;
            else if (bone.name.StartsWith("Hand_"))
            {
                var hand = AssetDatabase.LoadAssetAtPath<Material>("Assets/Models/Rocket/Materials/" + bone.name + ".mat");
                expected = hand != null ? hand : skin;
            }

            if (expected == null || renderers[i].sharedMaterial != expected)
                return false;
        }

        return true;
    }

    static void EnsurePrefab()
    {
        if (!File.Exists(GripPath))
            return;

        string full = Path.GetFullPath(PrefabPath);
        bool nested = File.Exists(full) && File.ReadAllText(full).Contains("!u!1001");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null && !nested)
            return;

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(GripPath);
        if (model == null)
            return;

        if (!nested)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            instance.name = "GripPistol";
            PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
            Object.DestroyImmediate(instance);
        }

        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            if (PrefabUtility.IsPartOfPrefabInstance(root))
                PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            if (root.transform.Find("Wrist") == null)
            {
                var wrist = new GameObject("Wrist");
                wrist.transform.SetParent(root.transform, false);
            }

            if (root.transform.Find("Muzzle") == null)
            {
                var muzzle = new GameObject("Muzzle");
                muzzle.transform.SetParent(root.transform, false);
            }

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
