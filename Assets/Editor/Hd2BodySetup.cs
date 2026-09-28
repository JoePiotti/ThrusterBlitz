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
    public const string RobotPath = "Assets/Models/Robot/Robot.fbx";
    const string PlayerPath = "Assets/Prefabs/VRPlayer.prefab";
    const string SessionKey = "HD2_BODY_RIG_V2";

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
            if (existing != null && FindChild(existing, "UpperArm_L") == null)
            {
                Object.DestroyImmediate(existing.gameObject);
                existing = null;
            }

            if (existing == null)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(robot, root.transform);
                instance.name = "Body";
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                instance.transform.localScale = Vector3.one;
                existing = instance.transform;
            }
            else
            {
                existing.localRotation = Quaternion.Euler(-90f, 0f, 0f);
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
            body.upperArmL = FindChild(existing, "UpperArm_L");
            body.forearmL = FindChild(existing, "Forearm_L");
            body.handL = FindChild(existing, "Hand_L");
            body.upperArmR = FindChild(existing, "UpperArm_R");
            body.forearmR = FindChild(existing, "Forearm_R");
            body.handR = FindChild(existing, "Hand_R");

            SetActive(root.transform, "LeftHandVisual", false);
            SetActive(root.transform, "RightHandVisual", false);

            PrefabUtility.SaveAsPrefabAsset(root, PlayerPath);
            Debug.Log("HD2 robot body attached to VRPlayer.");
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

    static void SetActive(Transform root, string name, bool active)
    {
        var child = FindChild(root, name);
        if (child != null)
            child.gameObject.SetActive(active);
    }
}
