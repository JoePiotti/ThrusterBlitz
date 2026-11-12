using UnityEditor;
using UnityEngine;

/// <summary>
/// Points the SMG pickup at the reduced model and the gripped prefab.
/// Scene instances keep their own transforms.
/// </summary>
[InitializeOnLoad]
public static class Hd2SmgPickupSetup
{
    const string ModelPath = "Assets/Models/SMG/Smg.fbx";
    const string GripPath = "Assets/Prefabs/GripWeapons/GripSmg.prefab";
    const string PrefabPath = "Assets/Prefabs/Pickups/SmgPickup.prefab";

    static Hd2SmgPickupSetup()
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
        var grip = AssetDatabase.LoadAssetAtPath<GameObject>(GripPath);
        if (model == null || grip == null || AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
        {
            if (model != null && AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null && grip == null)
                EditorApplication.delayCall += AssignModel;
            return;
        }

        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var pickup = root.GetComponent<Hd2SmgPickup>();
            if (pickup == null)
                return;

            bool changed = false;
            if (pickup.smgModel != model)
            {
                pickup.smgModel = model;
                changed = true;
            }

            if (pickup.grip != grip)
            {
                pickup.grip = grip;
                changed = true;
            }

            if (pickup.gripLeft != null)
            {
                pickup.gripLeft = null;
                changed = true;
            }

            if (pickup.gripRight != null)
            {
                pickup.gripRight = null;
                changed = true;
            }

            if (changed)
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
