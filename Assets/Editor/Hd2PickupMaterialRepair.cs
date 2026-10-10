using UnityEditor;
using UnityEngine;

/// <summary>
/// The pickup meshes were saved without the materials that used to be created in the editor.
/// Those materials were not assets, so the prefab stored them as empty. Write them as
/// material assets and assign them. Scene instances keep their positions.
/// </summary>
[InitializeOnLoad]
public static class Hd2PickupMaterialRepair
{
    const string Folder = "Assets/Prefabs/Pickups/Materials";

    static readonly string[] prefabs =
    {
        "Assets/Prefabs/Pickups/SmgPickup.prefab",
        "Assets/Prefabs/Pickups/RocketPickup.prefab",
        "Assets/Prefabs/Pickups/HealthPickup.prefab",
        "Assets/Prefabs/Pickups/ThrustPickup.prefab"
    };

    static Hd2PickupMaterialRepair()
    {
        EditorApplication.delayCall += Repair;
    }

    static void Repair()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
        {
            EditorApplication.delayCall += Repair;
            return;
        }

        if (!AssetDatabase.IsValidFolder(Folder))
            AssetDatabase.CreateFolder("Assets/Prefabs/Pickups", "Materials");

        for (int i = 0; i < prefabs.Length; i++)
            RepairPrefab(prefabs[i]);

        AssetDatabase.SaveAssets();
        RevertSceneMaterialOverrides();
    }

    static void RepairPrefab(string path)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (asset == null || !MissingMaterial(asset))
            return;

        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var smg = root.GetComponent<Hd2SmgPickup>();
            if (smg != null)
                smg.RepairSavedMaterials();
            var rocket = root.GetComponent<Hd2RocketPickup>();
            if (rocket != null)
                rocket.RepairSavedMaterials();
            var health = root.GetComponent<Hd2HealthPickup>();
            if (health != null)
                health.RepairSavedMaterials();
            var thrust = root.GetComponent<Hd2ThrustPickup>();
            if (thrust != null)
                thrust.RepairSavedMaterials();

            string prefix = System.IO.Path.GetFileNameWithoutExtension(path);
            Persist(root, prefix);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Debug.Log("Restored pickup materials on " + path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static void Persist(GameObject root, string prefix)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            var materials = renderers[i].sharedMaterials;
            for (int m = 0; m < materials.Length; m++)
            {
                var material = materials[m];
                if (material == null || AssetDatabase.Contains(material))
                    continue;

                string name = renderers[i].gameObject.name.Replace(" ", "");
                string assetPath = AssetDatabase.GenerateUniqueAssetPath(Folder + "/" + prefix + "_" + name + ".mat");
                AssetDatabase.CreateAsset(material, assetPath);
            }
        }
    }

    static bool MissingMaterial(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            var materials = renderers[i].sharedMaterials;
            for (int m = 0; m < materials.Length; m++)
            {
                if (materials[m] == null || materials[m].shader == null)
                    return true;
            }
        }

        return false;
    }

    static void RevertSceneMaterialOverrides()
    {
        Revert<Hd2SmgPickup>();
        Revert<Hd2RocketPickup>();
        Revert<Hd2HealthPickup>();
        Revert<Hd2ThrustPickup>();
    }

    static void Revert<T>() where T : Component
    {
        var pickups = Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < pickups.Length; i++)
        {
            if (!PrefabUtility.IsPartOfPrefabInstance(pickups[i]))
                continue;

            var renderers = pickups[i].GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < renderers.Length; r++)
                PrefabUtility.RevertObjectOverride(renderers[r], InteractionMode.AutomatedAction);
        }
    }
}
