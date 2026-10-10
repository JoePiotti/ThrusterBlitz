using UnityEditor;
using UnityEngine;

/// <summary>
/// Points the SMG pickup prefab at the grip models. Scene instances keep their own transforms.
/// </summary>
[InitializeOnLoad]
public static class Hd2SmgPickupSetup
{
    const string ModelPath = "Assets/Models/SMG/Smg.fbx";
    const string GripLeftPath = "Assets/Models/SMG/GripSmgLeft.fbx";
    const string GripRightPath = "Assets/Models/SMG/GripSmgRight.fbx";
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
        var left = AssetDatabase.LoadAssetAtPath<GameObject>(GripLeftPath);
        var right = AssetDatabase.LoadAssetAtPath<GameObject>(GripRightPath);
        if (model == null || AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
            return;

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

            if (left != null && pickup.gripLeft != left)
            {
                pickup.gripLeft = left;
                changed = true;
            }

            if (right != null && pickup.gripRight != right)
            {
                pickup.gripRight = right;
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
