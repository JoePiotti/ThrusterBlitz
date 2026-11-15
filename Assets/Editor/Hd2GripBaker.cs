using UnityEditor;
using UnityEngine;

/// <summary>
/// Bakes a grip from the robot hand you posed in Play mode into a GripWeapons prefab.
/// 1. Play, pick the gun up, then pause.
/// 2. Rotate or move Hand.L / Hand.R and its finger bones in the Scene view until the grip looks right.
/// 3. Tools > HD2 > Grip Baker > Bake. The Wrist and GripFingers markers in the prefab take the
///    hand's pose (mirrored back to left-hand space if you posed the right hand), and the prefab
///    gets an Hd2GripPose marked baked. The gun in hand updates right away.
/// </summary>
public class Hd2GripBaker : EditorWindow
{
    const string Folder = "Assets/Prefabs/GripWeapons/";

    static readonly string[,] PrefabByComponent =
    {
        { "Hd2Smg", "GripSmg" },
        { "Hd2BurstPistol", "GripBurst" },
        { "Hd2LaserPistol", "GripLaser" },
        { "Hd2Shocks", "GripShock" },
        { "Hd2Shotgun", "GripShotgun" },
        { "Hd2RocketLauncher", "GripRocket" },
        { "Hd2DropShield", "GripDropShield" },
        { "Hd2Pistol", "GripPistol" },
    };

    bool right;
    GameObject prefab;
    Transform lastGun;

    [MenuItem("Tools/HD2/Grip Baker")]
    static void Open()
    {
        GetWindow<Hd2GripBaker>("Grip Baker");
    }

    void OnInspectorUpdate()
    {
        Repaint();
    }

    void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "Play, pick the gun up, pause, then pose the hand and finger bones in the Scene view. " +
            "Bake writes that pose into the prefab's Wrist and GripFingers markers.",
            MessageType.Info);

        if (!EditorApplication.isPlaying)
        {
            EditorGUILayout.HelpBox("Enter Play mode and hold the gun to bake.", MessageType.Warning);
            return;
        }

        var body = FindFirstObjectByType<Hd2Body>();
        if (body == null)
        {
            EditorGUILayout.HelpBox("No Hd2Body in the scene.", MessageType.Warning);
            return;
        }

        right = GUILayout.Toolbar(right ? 1 : 0, new[] { "Left hand", "Right hand" }) == 1;
        Hd2GripHand grip = body.GripHand(right);
        if (grip == null || grip.hand == null)
        {
            EditorGUILayout.HelpBox("That hand has not been set up yet. Let a frame run.", MessageType.Warning);
            return;
        }

        Transform gun = grip.Gun;
        EditorGUILayout.LabelField("Hand bone", grip.hand.name);
        EditorGUILayout.LabelField("Held gun", gun != null ? gun.name : "(none)");
        if (gun != null)
            EditorGUILayout.LabelField("Baked", grip.Baked ? "yes" : "no (legacy aim)");

        if (gun != lastGun)
        {
            lastGun = gun;
            prefab = GuessPrefab(gun);
        }

        prefab = (GameObject)EditorGUILayout.ObjectField("Grip prefab", prefab, typeof(GameObject), false);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Select hand bone"))
                Selection.activeTransform = grip.hand;
            if (GUILayout.Button(EditorApplication.isPaused ? "Resume" : "Pause"))
                EditorApplication.isPaused = !EditorApplication.isPaused;
        }

        using (new EditorGUI.DisabledScope(gun == null || prefab == null || grip.Wrist == null))
        {
            if (GUILayout.Button("Bake", GUILayout.Height(32)))
                Bake(grip, gun, prefab);
        }

        if (gun != null && grip.Wrist == null)
            EditorGUILayout.HelpBox("The held gun has no Wrist marker.", MessageType.Error);
    }

    static GameObject GuessPrefab(Transform gun)
    {
        if (gun == null)
            return null;
        for (int i = 0; i < PrefabByComponent.GetLength(0); i++)
        {
            if (gun.GetComponent(PrefabByComponent[i, 0]) != null)
                return AssetDatabase.LoadAssetAtPath<GameObject>(Folder + PrefabByComponent[i, 1] + ".prefab");
        }

        return null;
    }

    struct Baked
    {
        public string path;
        public Vector3 position;
        public Quaternion rotation;
    }

    static void Bake(Hd2GripHand grip, Transform gun, GameObject prefabAsset)
    {
        Transform root = gun;
        int count = Hd2GripFingers.BoneCount + 1;
        var baked = new Baked[count];
        int used = 0;
        for (int slot = 0; slot < count; slot++)
        {
            bool isHand = slot == Hd2GripHand.HandSlot;
            Transform bone = isHand ? grip.hand : grip.Bone(slot);
            Transform marker = isHand ? grip.Wrist : grip.Marker(slot);
            if (bone == null || marker == null)
                continue;

            Vector3 position = bone.position;
            Quaternion rotation = bone.rotation;
            if (grip.right)
            {
                // The held right gun was mirrored across its root's X. Undo that and the
                // right rig's offset, so the prefab keeps left-hand markers.
                rotation = Hd2GripFingers.MirrorRotation(rotation * Quaternion.Inverse(grip.MirrorOffset(slot)), root.right);
                position = Hd2GripFingers.MirrorPoint(position, root.position, root.right);
            }

            baked[used++] = new Baked
            {
                path = AnimationUtility.CalculateTransformPath(marker, root),
                position = root.InverseTransformPoint(position),
                rotation = Quaternion.Inverse(root.rotation) * rotation,
            };
        }

        float handScale = Mathf.Abs(grip.hand.lossyScale.x) / Mathf.Max(0.0001f, Mathf.Abs(root.lossyScale.x));

        string assetPath = AssetDatabase.GetAssetPath(prefabAsset);
        GameObject contents = PrefabUtility.LoadPrefabContents(assetPath);
        try
        {
            Transform prefabRoot = contents.transform;
            int written = 0;
            for (int i = 0; i < used; i++)
            {
                Transform target = prefabRoot.Find(baked[i].path);
                if (target == null)
                {
                    Debug.LogWarning($"Grip Baker: {assetPath} has no {baked[i].path}.");
                    continue;
                }

                target.SetPositionAndRotation(
                    prefabRoot.TransformPoint(baked[i].position),
                    prefabRoot.rotation * baked[i].rotation);
                written++;
            }

            var pose = contents.GetComponent<Hd2GripPose>();
            if (pose == null)
                pose = contents.AddComponent<Hd2GripPose>();
            pose.baked = true;
            pose.handScale = handScale;
            PrefabUtility.SaveAsPrefabAsset(contents, assetPath);
            Debug.Log($"Grip Baker: wrote {written} markers and hand scale {handScale:0.###} to {assetPath}.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }

        UpdateHeldGun(grip, gun, handScale);
    }

    // The gun in hand is a copy of the prefab, so give it the same markers to see the bake now.
    static void UpdateHeldGun(Hd2GripHand grip, Transform gun, float handScale)
    {
        int count = Hd2GripFingers.BoneCount + 1;
        for (int slot = 0; slot < count; slot++)
        {
            bool isHand = slot == Hd2GripHand.HandSlot;
            Transform bone = isHand ? grip.hand : grip.Bone(slot);
            Transform marker = isHand ? grip.Wrist : grip.Marker(slot);
            if (bone == null || marker == null)
                continue;
            marker.SetPositionAndRotation(bone.position, bone.rotation * Quaternion.Inverse(grip.MirrorOffset(slot)));
        }

        var pose = gun.GetComponent<Hd2GripPose>();
        if (pose == null)
            pose = gun.gameObject.AddComponent<Hd2GripPose>();
        pose.baked = true;
        pose.handScale = handScale;
        grip.Rebind();
    }
}
