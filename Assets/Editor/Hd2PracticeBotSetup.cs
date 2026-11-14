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
    public const string ModelPath = "Assets/Models/Bot/PlayerBotRobot.fbx";

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

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
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
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null || EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        var bots = Object.FindObjectsByType<Hd2PracticeBot>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < bots.Length; i++)
        {
            var practice = bots[i];
            string scenePath = practice.gameObject.scene.path;
            if (scenePath.Contains("/Archive/") || scenePath.Contains("/_Recovery/"))
                continue;
            if (UnderManual(practice.transform))
                continue;

            var existing = practice.transform.Find("PracticeRobot");
            if (existing != null)
            {
                string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(existing.gameObject);
                if (path == ModelPath)
                {
                    AssignRobot(practice, model);
                    continue;
                }

                if (!IsLegacyRobotPath(path))
                    continue;

                Object.DestroyImmediate(existing.gameObject);
            }

            var placed = (GameObject)PrefabUtility.InstantiatePrefab(model, practice.transform);
            placed.name = "PracticeRobot";
            placed.transform.localPosition = Vector3.zero;
            placed.transform.localRotation = Quaternion.identity;
            placed.transform.localScale = Vector3.one;
            AssignRobot(practice, model);
        }
    }

    static void AssignRobot(Hd2PracticeBot practice, GameObject model)
    {
        if (practice.robotPrefab == model)
            return;
        practice.robotPrefab = model;
        EditorUtility.SetDirty(practice);
        EditorSceneManager.MarkSceneDirty(practice.gameObject.scene);
    }

    static bool IsLegacyRobotPath(string path)
    {
        return path == PrefabPath
            || path == "Assets/Prefabs/PracticeRobot.prefab"
            || path == "Assets/Models/Bot/HumanoidRobot.fbx"
            || path == "Assets/Models/Bot/LowPolyRobot.fbx";
    }

    static bool UnderManual(Transform target)
    {
        while (target != null)
        {
            if (target.name == "Manual")
                return true;
            target = target.parent;
        }

        return false;
    }
}
