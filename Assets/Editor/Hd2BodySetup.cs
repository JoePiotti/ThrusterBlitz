using UnityEditor;
using UnityEngine;

public class Hd2BodyImporter : AssetPostprocessor
{
    void OnPreprocessModel()
    {
        if (assetPath != Hd2BodySetup.RobotPath)
            return;

        var importer = (ModelImporter)assetImporter;
        importer.isReadable = true;
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.importAnimation = false;
        importer.optimizeBones = false;
    }

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
    public const string RobotPath = "Assets/Models/Bot/HumanoidRobot.fbx";
    const string TexturePath = "Assets/Models/Bot/HumanoidRobot_BaseColor.png";
    const string MaterialPath = "Assets/Models/Bot/HumanoidRobot.mat";
    const string PlayerPath = "Assets/Prefabs/VRPlayer.prefab";
    const string SessionKey = "HD2_BODY_RIG_V12";
    static int rigTries;

    static Hd2BodySetup()
    {
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

        var importer = AssetImporter.GetAtPath(RobotPath) as ModelImporter;
        if (importer != null && (!importer.isReadable || importer.animationType != ModelImporterAnimationType.Generic))
        {
            importer.isReadable = true;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = false;
            importer.optimizeBones = false;
            importer.SaveAndReimport();
            return;
        }

        var root = PrefabUtility.LoadPrefabContents(PlayerPath);
        try
        {
            var existing = root.transform.Find("Body");
            if (existing != null && !UsesModel(existing.gameObject, RobotPath))
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
                AssignPartMaterials(instance);
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

            var upperArmL = FindChild(existing, "Arm.L");
            if (upperArmL == null)
            {
                if (rigTries++ < 30)
                    EditorApplication.delayCall += AttachIfNeeded;
                return;
            }

            body.body = existing;
            body.bodyRestEuler = Vector3.zero;
            body.scaleWholeBody = true;
            body.upperArmL = upperArmL;
            body.forearmL = FindChild(existing, "Arm.L.002");
            body.handL = FindChild(existing, "Hand.L");
            body.upperArmR = FindChild(existing, "Arm.R");
            body.forearmR = FindChild(existing, "Arm.R.002");
            body.handR = FindChild(existing, "Hand.R");
            var chest = FindChild(existing, "Chest");
            body.shoulderPivotL = EnsurePivot(chest, upperArmL, "LeftShoulderPivot");
            body.shoulderPivotR = EnsurePivot(chest, body.upperArmR, "RightShoulderPivot");
            var locomotionHands = root.GetComponent<Hd2Locomotion>();
            if (locomotionHands != null)
            {
                body.gripPivotL = EnsureGripPivot(locomotionHands.leftHand, false);
                body.gripPivotR = EnsureGripPivot(locomotionHands.rightHand, true);
            }

            AssignPartMaterials(existing.gameObject);

            // Boxes are hand-tuned after the rig settles. Only fill them in
            // when this body has none yet, and never replace ones already there.
            if (existing.GetComponentInChildren<Collider>() == null)
                Hd2PracticeBot.FitHitboxes(existing);

            SetActive(root.transform, "LeftHandVisual", false);
            SetActive(root.transform, "RightHandVisual", false);

            PrefabUtility.SaveAsPrefabAsset(root, PlayerPath);
            Debug.Log("HD2 humanoid robot attached to VRPlayer.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        SessionState.SetBool(SessionKey, true);
    }

    static Transform EnsurePivot(Transform chest, Transform arm, string name)
    {
        if (chest == null || arm == null)
            return null;

        var pivot = chest.Find(name);
        if (pivot == null)
        {
            var marker = new GameObject(name);
            pivot = marker.transform;
            pivot.SetParent(chest, false);
            pivot.SetPositionAndRotation(arm.position, arm.rotation);
        }

        if (pivot.GetComponent<Hd2ShoulderPivot>() == null)
            pivot.gameObject.AddComponent<Hd2ShoulderPivot>();
        return pivot;
    }

    static Transform EnsureGripPivot(Transform hand, bool right)
    {
        if (hand == null)
            return null;

        string name = right ? "RightGripPivot" : "LeftGripPivot";
        var pivot = FindChild(hand, name);
        var gun = hand.GetComponentInChildren<Hd2Pistol>(true);
        if (pivot == null)
        {
            var marker = new GameObject(name);
            pivot = marker.transform;
            pivot.SetParent(gun != null ? gun.transform : hand, false);
            float side = right ? -1f : 1f;
            pivot.localPosition = gun != null
                ? new Vector3(0.014f * side, 0.101f, 0.088f)
                : new Vector3(0f, 0.01f, 0.05f);
            pivot.localRotation = Quaternion.identity;
        }

        if (pivot.GetComponent<Hd2ShoulderPivot>() == null)
            pivot.gameObject.AddComponent<Hd2ShoulderPivot>();
        return pivot;
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

    static bool UsesModel(GameObject instance, string assetPath)
    {
        var source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(instance);
        return source != null && AssetDatabase.GetAssetPath(source) == assetPath;
    }

    static void AssignPartMaterials(GameObject instance)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        var renderer = instance.GetComponentInChildren<SkinnedMeshRenderer>();
        if (renderer == null || renderer.sharedMesh == null || shader == null)
            return;

        int count = renderer.sharedMesh.subMeshCount;
        var materials = new Material[count];
        for (int i = 0; i < count; i++)
        {
            string texturePath = "Assets/Models/Bot/Parts/tripo_part_" + i + ".png";
            materials[i] = PartMaterial(i, texturePath, shader);
        }

        renderer.sharedMaterials = materials;
    }

    static Material PartMaterial(int index, string texturePath, Shader shader)
    {
        const string folder = "Assets/Models/Bot/Materials";
        if (!AssetDatabase.IsValidFolder(folder))
            AssetDatabase.CreateFolder("Assets/Models/Bot", "Materials");

        string path = folder + "/Part_" + index + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }

        material.shader = shader;
        var color = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        if (color != null)
        {
            material.SetTexture("_BaseMap", color);
            material.SetTexture("_MainTex", color);
        }

        EditorUtility.SetDirty(material);
        return material;
    }

    static void AssignBotMaterial(GameObject instance)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null && shader != null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, MaterialPath);
        }

        if (material == null)
            return;

        var color = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        if (color != null)
        {
            material.SetTexture("_BaseMap", color);
            material.SetTexture("_MainTex", color);
        }

        EditorUtility.SetDirty(material);
        var renderers = instance.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
            renderers[i].sharedMaterial = material;
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
