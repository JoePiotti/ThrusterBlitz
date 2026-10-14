using UnityEditor;
using UnityEngine;

/// <summary>
/// The shotgun comes in as many parts. Each part keeps its own texture, in URP.
/// </summary>
public class Hd2ShotgunImporter : AssetPostprocessor
{
    void OnPreprocessModel()
    {
        string path = assetPath.Replace('\\', '/');
        if (path != "Assets/Models/Shotgun/Shotgun.fbx" && path != "Assets/Models/Shotgun/GripShotgun.fbx")
            return;

        var importer = (ModelImporter)assetImporter;
        importer.isReadable = true;
    }

    void OnPostprocessModel(GameObject root)
    {
        string path = assetPath.Replace('\\', '/');
        if (path != "Assets/Models/Shotgun/Shotgun.fbx" && path != "Assets/Models/Shotgun/GripShotgun.fbx")
            return;

        var metal = AssetDatabase.LoadAssetAtPath<Material>("Assets/Models/Shotgun/Shotgun.mat");
        var skin = AssetDatabase.LoadAssetAtPath<Material>("Assets/Models/Rocket/Materials/Hand_hand_palm.mat");
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            bool hand = IsHand(renderers[i].transform);
            Material material = hand
                ? HandMaterial(renderers[i].gameObject.name, skin)
                : PartMaterial(renderers[i].gameObject.name, metal);
            if (material == null)
                continue;
            var slots = renderers[i].sharedMaterials;
            for (int m = 0; m < slots.Length; m++)
                slots[m] = material;
            renderers[i].sharedMaterials = slots;
        }
    }

    static bool IsHand(Transform bone)
    {
        while (bone != null)
        {
            if (bone.name == "Hand" || bone.name.StartsWith("Hand_"))
                return true;
            bone = bone.parent;
        }

        return false;
    }

    static Material PartMaterial(string name, Material fallback)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Models/Shotgun/Materials/" + name + ".mat");
        return material != null ? material : fallback;
    }

    static Material HandMaterial(string name, Material fallback)
    {
        const string folder = "Assets/Models/Rocket/Materials/";
        var material = AssetDatabase.LoadAssetAtPath<Material>(folder + name + ".mat");
        return material != null ? material : fallback;
    }
}
