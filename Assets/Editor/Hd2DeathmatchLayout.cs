using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Puts handmade Deathmatch content under Manual and leaves Generated empty
/// for scripts. Turns scene pickups into prefab instances so their positions
/// are not rewritten when a pickup prefab changes.
/// </summary>
[InitializeOnLoad]
public static class Hd2DeathmatchLayout
{
    const string ScenePath = "Assets/Scenes/Deathmatch.unity";
    const string PickupFolder = "Assets/Prefabs/Pickups";

    static Hd2DeathmatchLayout()
    {
        EditorApplication.delayCall += Ensure;
        EditorSceneManager.sceneOpened += (_, __) => EditorApplication.delayCall += Ensure;
    }

    static void Ensure()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
        {
            EditorApplication.delayCall += Ensure;
            return;
        }

        var scene = EditorSceneManager.GetSceneByPath(ScenePath);
        if (!scene.isLoaded)
            return;

        var map = Object.FindObjectsByType<Hd2DeathmatchMap>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Hd2DeathmatchMap deathmatch = null;
        for (int i = 0; i < map.Length; i++)
        {
            if (map[i].gameObject.scene == scene)
            {
                deathmatch = map[i];
                break;
            }
        }

        if (deathmatch == null)
            return;

        bool changed = EnsureFolders(deathmatch.transform);
        changed |= Prefabize<Hd2SmgPickup>(scene, PickupFolder + "/SmgPickup.prefab");
        changed |= Prefabize<Hd2RocketPickup>(scene, PickupFolder + "/RocketPickup.prefab");
        changed |= Prefabize<Hd2HealthPickup>(scene, PickupFolder + "/HealthPickup.prefab");
        changed |= Prefabize<Hd2ThrustPickup>(scene, PickupFolder + "/ThrustPickup.prefab");
        if (!changed)
            return;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    static bool EnsureFolders(Transform map)
    {
        bool changed = false;
        Transform manual = map.Find("Manual");
        if (manual == null)
        {
            var manualObject = new GameObject("Manual");
            manual = manualObject.transform;
            manual.SetParent(map, false);
            changed = true;
        }

        if (map.Find("Generated") == null)
        {
            var generatedObject = new GameObject("Generated");
            generatedObject.transform.SetParent(map, false);
            changed = true;
        }

        var moving = new List<Transform>();
        for (int i = 0; i < map.childCount; i++)
        {
            Transform child = map.GetChild(i);
            if (child.name == "Manual" || child.name == "Generated")
                continue;
            moving.Add(child);
        }

        for (int i = 0; i < moving.Count; i++)
        {
            moving[i].SetParent(manual, true);
            changed = true;
        }

        return changed;
    }

    static bool Prefabize<T>(UnityEngine.SceneManagement.Scene scene, string prefabPath) where T : Component
    {
        var found = Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var pickups = new List<T>();
        for (int i = 0; i < found.Length; i++)
        {
            if (found[i].gameObject.scene == scene)
                pickups.Add(found[i]);
        }

        if (pickups.Count == 0)
            return false;

        if (!AssetDatabase.IsValidFolder(PickupFolder))
            AssetDatabase.CreateFolder("Assets/Prefabs", "Pickups");

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
            prefab = PrefabUtility.SaveAsPrefabAsset(pickups[0].gameObject, prefabPath);

        bool changed = false;
        for (int i = 0; i < pickups.Count; i++)
        {
            GameObject current = pickups[i].gameObject;
            if (current == null)
                continue;
            if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(current) == prefabPath)
                continue;

            Transform parent = current.transform.parent;
            int sibling = current.transform.GetSiblingIndex();
            Vector3 position = current.transform.position;
            Quaternion rotation = current.transform.rotation;
            Vector3 scale = current.transform.localScale;
            string name = current.name;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.name = name;
            instance.transform.SetPositionAndRotation(position, rotation);
            instance.transform.localScale = scale;
            instance.transform.SetSiblingIndex(sibling);
            Object.DestroyImmediate(current);
            changed = true;
        }

        return changed;
    }
}
