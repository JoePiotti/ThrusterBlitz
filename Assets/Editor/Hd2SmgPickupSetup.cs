using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Points every SMG pickup at the decimated SMG model once Unity has imported it.
/// </summary>
[InitializeOnLoad]
public static class Hd2SmgPickupSetup
{
    const string ModelPath = "Assets/Models/SMG/Smg.fbx";
    const string GripLeftPath = "Assets/Models/SMG/GripSmgLeft.fbx";
    const string GripRightPath = "Assets/Models/SMG/GripSmgRight.fbx";

    static Hd2SmgPickupSetup()
    {
        EditorApplication.delayCall += AssignModel;
        EditorSceneManager.sceneOpened += (_, __) => EditorApplication.delayCall += AssignModel;
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
        if (model == null)
        {
            EditorApplication.delayCall += AssignModel;
            return;
        }

        var pickups = Object.FindObjectsByType<Hd2SmgPickup>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < pickups.Length; i++)
        {
            bool changed = false;
            if (pickups[i].smgModel != model)
            {
                pickups[i].smgModel = model;
                changed = true;
            }

            if (left != null && pickups[i].gripLeft != left)
            {
                pickups[i].gripLeft = left;
                changed = true;
            }

            if (right != null && pickups[i].gripRight != right)
            {
                pickups[i].gripRight = right;
                changed = true;
            }

            if (!changed)
                continue;

            EditorUtility.SetDirty(pickups[i]);
            EditorSceneManager.MarkSceneDirty(pickups[i].gameObject.scene);
        }
    }
}
