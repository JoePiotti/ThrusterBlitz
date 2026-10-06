using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Saves the practice robot with its hit boxes so they can be edited on the prefab.
/// Bones that already have a collider are left alone at play time.
/// </summary>
public static class Hd2PracticeBotSetup
{
    public const string PrefabPath = "Assets/Prefabs/HumanoidPractice.prefab";

    [MenuItem("HD2/Bake Practice Bot Hitboxes")]
    public static void BakeFromMenu()
    {
        Bake();
        WireOpenScenes();
    }

    [InitializeOnLoadMethod]
    static void AutoBake()
    {
        EditorSceneManager.sceneOpened += (_, _) => EditorApplication.delayCall += Ensure;
        EditorApplication.delayCall += Ensure;
    }

    static void Ensure()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
            Bake();
        WireOpenScenes();
    }

    public static void Bake()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
            return;

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(Hd2BodySetup.RobotPath);
        if (model == null)
            return;

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        instance.name = "PracticeRobot";
        Hd2PracticeBot.FitHitboxes(instance.transform);
        PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
        Object.DestroyImmediate(instance);
        AssetDatabase.SaveAssets();
    }

    static void WireOpenScenes()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null || EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        var bots = Object.FindObjectsByType<Hd2PracticeBot>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < bots.Length; i++)
        {
            var practice = bots[i];
            if (practice.gameObject.scene.path != "Assets/Scenes/Deathmatch.unity")
                continue;

            var existing = practice.transform.Find("PracticeRobot");
            if (existing != null)
            {
                string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(existing.gameObject);
                if (path == PrefabPath)
                {
                    if (practice.robotPrefab != prefab)
                    {
                        practice.robotPrefab = prefab;
                        EditorUtility.SetDirty(practice);
                        EditorSceneManager.MarkSceneDirty(practice.gameObject.scene);
                    }
                    continue;
                }

                Object.DestroyImmediate(existing.gameObject);
            }

            var placed = (GameObject)PrefabUtility.InstantiatePrefab(prefab, practice.transform);
            placed.name = "PracticeRobot";
            placed.transform.localPosition = Vector3.zero;
            placed.transform.localRotation = Quaternion.identity;
            placed.transform.localScale = Vector3.one;
            practice.robotPrefab = prefab;
            EditorUtility.SetDirty(practice);
            EditorSceneManager.MarkSceneDirty(practice.gameObject.scene);
        }
    }
}
