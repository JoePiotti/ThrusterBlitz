using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

public static class Hd2VrPlayerSetup
{
    const string PrefabPath = "Assets/Prefabs/VRPlayer.prefab";
    const string ScenePath = "Assets/Scenes/Greybox.unity";

    public static void CreateVrPlayer()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
            AssetDatabase.CreateFolder("Assets", "Prefabs");

        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (existing == null)
        {
            var root = BuildRig();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
        }

        if (System.IO.File.Exists(ScenePath))
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            if (GameObject.Find("VRPlayer") == null)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                instance.name = "VRPlayer";
                instance.transform.position = Vector3.zero;
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log("HD2 VR player prefab created.");
    }

    static GameObject BuildRig()
    {
        var root = new GameObject("VRPlayer");

        var offset = new GameObject("CameraOffset");
        offset.transform.SetParent(root.transform, false);
        offset.transform.localPosition = new Vector3(0f, 1.6f, 0f);

        var eye = new GameObject("CenterEye");
        eye.tag = "MainCamera";
        eye.transform.SetParent(offset.transform, false);
        var camera = eye.AddComponent<Camera>();
        camera.nearClipPlane = 0.01f;
        camera.stereoTargetEye = StereoTargetEyeMask.Both;
        if (eye.GetComponent<AudioListener>() == null)
            eye.AddComponent<AudioListener>();
        AddTrackedPose(eye, "<XRHMD>/centerEyePosition", "<XRHMD>/centerEyeRotation");

        CreateHand(root.transform, "LeftHand", "<XRController>{LeftHand}/devicePosition", "<XRController>{LeftHand}/deviceRotation", new Vector3(-0.2f, 1.2f, 0.3f));
        CreateHand(root.transform, "RightHand", "<XRController>{RightHand}/devicePosition", "<XRController>{RightHand}/deviceRotation", new Vector3(0.2f, 1.2f, 0.3f));
        return root;
    }

    static void CreateHand(Transform parent, string name, string positionBinding, string rotationBinding, Vector3 editorPlaceholder)
    {
        var hand = new GameObject(name);
        hand.transform.SetParent(parent, false);
        hand.transform.localPosition = editorPlaceholder;
        AddTrackedPose(hand, positionBinding, rotationBinding);

        var vis = GameObject.CreatePrimitive(PrimitiveType.Cube);
        vis.name = name + "Visual";
        vis.transform.SetParent(hand.transform, false);
        vis.transform.localScale = new Vector3(0.03f, 0.03f, 0.08f);
        var collider = vis.GetComponent<Collider>();
        if (collider != null)
            Object.DestroyImmediate(collider);
    }

    static void AddTrackedPose(GameObject target, string positionBinding, string rotationBinding)
    {
        var driver = target.AddComponent<TrackedPoseDriver>();
        var position = new InputAction(target.name + "-pos", InputActionType.Value, positionBinding, expectedControlType: "Vector3");
        var rotation = new InputAction(target.name + "-rot", InputActionType.Value, rotationBinding, expectedControlType: "Quaternion");
        driver.positionInput = new InputActionProperty(position);
        driver.rotationInput = new InputActionProperty(rotation);
        driver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
        driver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
    }
}
