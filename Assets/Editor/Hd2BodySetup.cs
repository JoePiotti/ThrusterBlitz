using UnityEditor;
using UnityEngine;

public class Hd2BodyImporter : AssetPostprocessor
{
    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        for (int i = 0; i < imported.Length; i++)
        {
            if (imported[i] == Hd2BodySetup.RobotPath)
                EditorApplication.delayCall += Hd2BodySetup.AttachIfNeeded;
        }
    }
}

[InitializeOnLoad]
public static class Hd2BodySetup
{
    public const string RobotPath = "Assets/Models/StandIn/StandInRobot.fbx";
    const string PlayerPath = "Assets/Prefabs/VRPlayer.prefab";
    const string SessionKey = "HD2_BODY_RIG_V3";

    static Hd2BodySetup()
    {
        EditorApplication.delayCall += AttachIfNeeded;
    }

    public static void AttachIfNeeded()
    {
        if (SessionState.GetBool(SessionKey, false))
            return;
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        var robot = AssetDatabase.LoadAssetAtPath<GameObject>(RobotPath);
        var player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
        if (robot == null || player == null)
            return;

        var root = PrefabUtility.LoadPrefabContents(PlayerPath);
        try
        {
            var existing = root.transform.Find("Body");
            if (existing != null && FindChild(existing, "Arm.L") == null)
            {
                Object.DestroyImmediate(existing.gameObject);
                existing = null;
            }

            if (existing == null)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(robot, root.transform);
                instance.name = "Body";
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one;
                AssignStandInMaterials(instance);
                existing = instance.transform;
            }

            var body = root.GetComponent<Hd2Body>();
            if (body == null)
                body = root.AddComponent<Hd2Body>();

            var locomotion = root.GetComponent<Hd2Locomotion>();
            if (locomotion != null)
            {
                body.head = locomotion.head;
                body.leftHand = locomotion.leftHand;
                body.rightHand = locomotion.rightHand;
            }

            body.body = existing;
            body.bodyRestEuler = Vector3.zero;
            body.scaleWholeBody = true;
            body.upperArmL = FindChild(existing, "Arm.L");
            body.forearmL = FindChild(existing, "Arm.L.002");
            body.handL = FindChild(existing, "Hand.L");
            body.upperArmR = FindChild(existing, "Arm.R");
            body.forearmR = FindChild(existing, "Arm.R.002");
            body.handR = FindChild(existing, "Hand.R");

            SetActive(root.transform, "LeftHandVisual", false);
            SetActive(root.transform, "RightHandVisual", false);

            PrefabUtility.SaveAsPrefabAsset(root, PlayerPath);
            Debug.Log("HD2 stand-in robot attached to VRPlayer.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        SessionState.SetBool(SessionKey, true);
    }

    static Transform FindChild(Transform root, string name)
    {
        if (root.name == name)
            return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var found = FindChild(root.GetChild(i), name);
            if (found != null)
                return found;
        }

        return null;
    }

    static void AssignStandInMaterials(GameObject instance)
    {
        var renderers = instance.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            string name = renderers[i].name;
            string colorPath = null;
            string emissionPath = null;
            if (name.Contains("Top"))
                colorPath = "Assets/Models/StandIn/Textures/Primary_Top.009_Colored_t.png";
            else if (name.Contains("Bottom"))
                colorPath = "Assets/Models/StandIn/Textures/Primary_Bottom.009_Colored_t.png";
            else if (name.Contains("Secondary"))
            {
                colorPath = "Assets/Models/StandIn/Textures/Secondary_t.png";
                emissionPath = "Assets/Models/StandIn/Textures/Secondary_e.png";
            }

            if (colorPath == null)
                continue;

            renderers[i].sharedMaterial = StandInMaterial(renderers[i].name, colorPath, emissionPath);
        }
    }

    static Material StandInMaterial(string assetName, string colorPath, string emissionPath)
    {
        const string folder = "Assets/Models/StandIn/Materials";
        if (!AssetDatabase.IsValidFolder(folder))
            AssetDatabase.CreateFolder("Assets/Models/StandIn", "Materials");

        string path = folder + "/" + assetName + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (material == null && shader != null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }

        if (material == null)
            return null;

        var color = AssetDatabase.LoadAssetAtPath<Texture2D>(colorPath);
        if (color != null)
            material.SetTexture("_BaseMap", color);
        var emission = emissionPath != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(emissionPath) : null;
        if (emission != null)
        {
            material.SetTexture("_EmissionMap", emission);
            material.SetColor("_EmissionColor", Color.white);
            material.EnableKeyword("_EMISSION");
        }

        return material;
    }

    static void SetActive(Transform root, string name, bool active)
    {
        var child = FindChild(root, name);
        if (child != null)
            child.gameObject.SetActive(active);
    }
}
