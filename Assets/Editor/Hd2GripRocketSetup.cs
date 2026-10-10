using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Turns the gripped rocket launcher model into a prefab once Unity has imported it.
/// The saved prefab is a real hierarchy, so Wrist can be dragged in the prefab.
/// </summary>
[InitializeOnLoad]
public static class Hd2GripRocketSetup
{
    const string ModelPath = "Assets/Models/Rocket/GripRocket.fbx";
    const string PrefabPath = "Assets/Prefabs/GripRocket.prefab";

    static Hd2GripRocketSetup()
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

        if (AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath) == null)
            return;

        string full = Path.GetFullPath(PrefabPath);
        bool nested = File.Exists(full) && File.ReadAllText(full).Contains("PrefabInstance:");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null && !nested)
            return;

        if (!nested)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                EditorApplication.delayCall += Ensure;
                return;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            instance.name = "GripRocket";
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
                wrist.transform.localPosition = new Vector3(-0.012f, -0.166f, 0.093f);
            }

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
