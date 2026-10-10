using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Points the rocket pickup at the gripped launcher once Unity has imported it.
/// Does not move a pickup that is already in the scene.
/// </summary>
[InitializeOnLoad]
public static class Hd2RocketPickupSetup
{
    const string PrefabPath = "Assets/Prefabs/GripRocket.prefab";

    static Hd2RocketPickupSetup()
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

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (model == null)
        {
            EditorApplication.delayCall += AssignModel;
            return;
        }

        var pickups = Object.FindObjectsByType<Hd2RocketPickup>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < pickups.Length; i++)
        {
            if (pickups[i].rocketModel == model)
                continue;

            pickups[i].rocketModel = model;
            EditorUtility.SetDirty(pickups[i]);
            if (pickups[i].gameObject.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(pickups[i].gameObject.scene);
        }
    }
}
