using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds the gripped SMG prefab from the reduced gun and the rocket-launcher hand.
/// The saved prefab is a real hierarchy, so Wrist can be dragged.
/// </summary>
public class Hd2GripSmgImporter : AssetPostprocessor
{
    void OnPreprocessModel()
    {
        if (assetPath.Replace('\\', '/') != Hd2GripSmgSetup.ModelPath)
            return;

        var importer = (ModelImporter)assetImporter;
        importer.isReadable = true;
    }

    void OnPostprocessModel(GameObject root)
    {
        if (assetPath.Replace('\\', '/') != Hd2GripSmgSetup.ModelPath)
            return;

        var body = AssetDatabase.LoadAssetAtPath<Material>(Hd2SmgSetup.MaterialPath);
        var trigger = AssetDatabase.LoadAssetAtPath<Material>(Hd2SmgSetup.TriggerMaterialPath);
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

        if (bone.name == "Trigger")
            return trigger != null ? trigger : body;
        return body;
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
public static class Hd2GripSmgSetup
{
    public const string ModelPath = "Assets/Models/SMG/GripSmg.fbx";
    public const string PrefabPath = "Assets/Prefabs/GripSmg.prefab";

    static Hd2GripSmgSetup()
    {
        EditorApplication.delayCall += Ensure;
    }

    static void Ensure()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
        {
            EditorApplication.delayCall += Ensure;
            return;
        }

        if (!File.Exists(ModelPath))
            return;

        if (AssetDatabase.LoadAssetAtPath<Material>(Hd2SmgSetup.MaterialPath) == null
            || AssetDatabase.LoadAssetAtPath<Material>(Hd2SmgSetup.TriggerMaterialPath) == null)
        {
            EditorApplication.delayCall += Ensure;
            return;
        }

        string full = Path.GetFullPath(PrefabPath);
        bool nested = File.Exists(full) && File.ReadAllText(full).Contains("PrefabInstance:");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null && !nested)
            return;

        AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null)
        {
            EditorApplication.delayCall += Ensure;
            return;
        }

        if (!nested)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            instance.name = "GripSmg";
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

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
