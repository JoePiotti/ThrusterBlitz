using UnityEditor;
using UnityEngine;

/// <summary>
/// Points the rocket pickup prefab at the gripped launcher. Scene instances keep their own transforms.
/// </summary>
[InitializeOnLoad]
public static class Hd2RocketPickupSetup
{
    const string ModelPath = "Assets/Prefabs/GripRocket.prefab";
    const string PrefabPath = "Assets/Prefabs/Pickups/RocketPickup.prefab";

    static Hd2RocketPickupSetup()
    {
        EditorApplication.delayCall += AssignModel;
    }

    static void AssignModel()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
        {
            EditorApplication.delayCall += AssignModel;
            return;
        }

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null || AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
            return;

        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var pickup = root.GetComponent<Hd2RocketPickup>();
            if (pickup == null || pickup.rocketModel == model)
                return;

            pickup.rocketModel = model;
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
