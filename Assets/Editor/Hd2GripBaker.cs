using UnityEditor;
using UnityEngine;

/// <summary>
/// Bakes a grip from a posed robot hand into a GripWeapons prefab.
///
/// Edit mode (no headset):
/// 1. Tools > HD2 > Grip Baker. Pick the grip prefab, press Start Posing.
///    A temporary gun and robot appear at the scene origin (never saved with the scene).
/// 2. Rotate or move the hand and its finger bones in the Scene view.
/// 3. Bake. The prefab's Wrist and GripFingers markers take the hand's pose and the prefab
///    gets an Hd2GripPose marked baked. Build and test, then repeat.
///
/// Play mode works too: hold the gun, pause, pose Hand.L / Hand.R, Bake.
/// </summary>
public class Hd2GripBaker : EditorWindow
{
    const string Folder = "Assets/Prefabs/GripWeapons/";
    const string DefaultRobotPath = "Assets/Models/Bot/PlayerBotRobot.fbx";
    const string PosingName = "~GripPosing (temporary)";

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
    GameObject robotModel;
    Transform lastGun;

    // Edit-mode posing session. Serialized so it survives script recompiles.
    [SerializeField] GameObject posingRoot;
    [SerializeField] Transform posingGun;
    Hd2GripHand posingGrip;

    [MenuItem("Tools/HD2/Grip Baker")]
    static void Open()
    {
        GetWindow<Hd2GripBaker>("Grip Baker");
    }

    void OnEnable()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        if (robotModel == null)
            robotModel = AssetDatabase.LoadAssetAtPath<GameObject>(DefaultRobotPath);
        if (prefab == null)
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "GripSmg.prefab");
    }

    void OnDisable()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
    }

    void OnDestroy()
    {
        EndPosing();
    }

    void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.ExitingEditMode)
            EndPosing();
    }

    void OnInspectorUpdate()
    {
        Repaint();
    }

    void OnGUI()
    {
        if (EditorApplication.isPlaying)
            PlayModeGUI();
        else
            EditModeGUI();
    }

    // ---------- Edit mode ----------

    void EditModeGUI()
    {
        RestorePosing();
        bool posing = posingRoot != null && posingGrip != null && posingGun != null;
        EditorGUILayout.HelpBox(
            posing
                ? "Pose the hand and finger bones in the Scene view, then Bake. The posing objects are never saved with the scene."
                : "Pick a grip prefab and press Start Posing. A temporary gun and robot appear at the scene origin, with the left hand on the gun.",
            MessageType.Info);

        using (new EditorGUI.DisabledScope(posing))
        {
            prefab = ResolvePrefab((GameObject)EditorGUILayout.ObjectField("Grip prefab", prefab, typeof(GameObject), false));
            robotModel = (GameObject)EditorGUILayout.ObjectField("Robot model", robotModel, typeof(GameObject), false);
        }

        if (!posing)
        {
            using (new EditorGUI.DisabledScope(prefab == null || robotModel == null))
            {
                if (GUILayout.Button("Start Posing", GUILayout.Height(32)))
                    StartPosing();
            }

            return;
        }

        EditorGUILayout.LabelField("Baked", posingGrip.Baked ? "yes" : "no (starting from the old markers)");
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Select hand bone"))
                Selection.activeTransform = posingGrip.hand;
            if (GUILayout.Button("Frame"))
            {
                Selection.activeTransform = posingGrip.hand;
                SceneView.lastActiveSceneView?.FrameSelected();
            }
        }

        if (GUILayout.Button("Reset to prefab markers"))
            PoseFromMarkers(posingGrip, posingGun);

        if (GUILayout.Button("Bake", GUILayout.Height(32)))
            Bake(posingGrip, posingGun, prefab);

        if (GUILayout.Button("End Posing"))
            EndPosing();
    }

    // After a recompile the plain C# grip state is gone; rebuild it from the scene objects.
    void RestorePosing()
    {
        if (posingGrip != null || posingRoot == null || posingGun == null)
            return;

        Transform hand = posingRoot.transform.Find("Hand.L");
        if (hand == null)
            hand = posingRoot.transform.Find("Hand_L");
        if (hand == null)
            return;

        posingGrip = new Hd2GripHand(hand, false);
        posingGrip.Track(posingGun);
    }

    void StartPosing()
    {
        EndPosing();
        GameObject stale = GameObject.Find(PosingName);
        if (stale != null)
            DestroyImmediate(stale);

        posingRoot = new GameObject(PosingName);
        posingRoot.hideFlags = HideFlags.DontSave;

        // The gun keeps its prefab root rotation and scale, as it does when equipped.
        GameObject gun = Instantiate(prefab);
        gun.name = prefab.name;
        gun.transform.SetParent(posingRoot.transform, false);
        gun.transform.localPosition = Vector3.zero;
        gun.transform.localRotation = prefab.transform.localRotation;
        gun.transform.localScale = prefab.transform.localScale;
        DisableBehaviours(gun);

        // A whole robot; only its left hand bone is posed on the gun.
        GameObject robot = Instantiate(robotModel);
        robot.name = "Robot";
        robot.transform.SetParent(posingRoot.transform, false);
        Animator[] animators = robot.GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < animators.Length; i++)
            animators[i].enabled = false;
        SkinnedMeshRenderer[] skins = robot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < skins.Length; i++)
            skins[i].updateWhenOffscreen = true;

        Transform hand = Hd2GripFingers.Find(robot.transform, "Hand.L");
        if (hand == null)
            hand = Hd2GripFingers.Find(robot.transform, "Hand_L");
        if (hand == null)
        {
            Debug.LogError("Grip Baker: the robot model has no Hand.L bone.");
            EndPosing();
            return;
        }

        // Bring the robot over so its wrist starts at the gun, which keeps the skinned
        // wrist from stretching across the scene.
        Transform wristMarker = Hd2GripFingers.Find(gun.transform, "Wrist");
        if (wristMarker != null)
            robot.transform.position += wristMarker.position - hand.position;

        // Same as the game: the hand bone hangs off the body, not the forearm.
        hand.SetParent(posingRoot.transform, true);
        SetHideFlagsRecursive(posingRoot.transform, HideFlags.DontSave);

        posingGun = gun.transform;
        posingGrip = new Hd2GripHand(hand, false);
        posingGrip.Track(posingGun);
        if (posingGrip.Wrist == null)
            Debug.LogWarning($"Grip Baker: {prefab.name} has no Wrist marker. Bake needs one.");
        PoseFromMarkers(posingGrip, posingGun);

        Selection.activeTransform = hand;
        SceneView.lastActiveSceneView?.FrameSelected();
    }

    void EndPosing()
    {
        if (posingRoot != null)
            DestroyImmediate(posingRoot);
        posingRoot = null;
        posingGun = null;
        posingGrip = null;
    }

    // Starting pose: the baked pose if there is one, otherwise the old Wrist convention
    // (forward = along the fingers, right = toward the index side) and aimed fingers.
    static void PoseFromMarkers(Hd2GripHand grip, Transform gun)
    {
        grip.Rebind();
        grip.Track(gun);
        if (grip.Wrist == null)
            return;

        if (!grip.PinHand())
        {
            Transform hand = grip.hand;
            hand.position = grip.Wrist.position;
            if (TryLegacyAxes(grip, out Vector3 length, out Vector3 spread))
                hand.rotation = MapAxes(length, spread, grip.Wrist.forward, grip.Wrist.right);
            Hd2GripHand.SetWorldScale(hand, 1f);
        }

        grip.ApplyFingers();
    }

    // The hand's length and index-spread axes in hand space, measured like Hd2Body does.
    static bool TryLegacyAxes(Hd2GripHand grip, out Vector3 length, out Vector3 spread)
    {
        length = Vector3.zero;
        spread = Vector3.zero;
        Transform index = grip.Bone(3);
        Transform indexMid = grip.Bone(4);
        Transform middle = grip.Bone(6);
        if (index == null || indexMid == null || middle == null)
            return false;

        Transform hand = grip.hand;
        length = hand.InverseTransformDirection(indexMid.position - index.position).normalized;
        spread = hand.InverseTransformDirection(index.position - middle.position).normalized;
        return length.sqrMagnitude > 0.5f && spread.sqrMagnitude > 0.5f;
    }

    static Quaternion MapAxes(Vector3 fromPrimary, Vector3 fromSecondary, Vector3 toPrimary, Vector3 toSecondary)
    {
        Vector3 from = fromPrimary.normalized;
        Vector3 fromSide = Vector3.ProjectOnPlane(fromSecondary, from).normalized;
        Vector3 to = toPrimary.normalized;
        Vector3 toSide = Vector3.ProjectOnPlane(toSecondary, to).normalized;
        Quaternion swing = Quaternion.FromToRotation(from, to);
        if (fromSide.sqrMagnitude < 0.0001f || toSide.sqrMagnitude < 0.0001f)
            return swing;
        Quaternion roll = Quaternion.FromToRotation(swing * fromSide, toSide);
        return roll * swing;
    }

    static void DisableBehaviours(GameObject root)
    {
        MonoBehaviour[] parts = root.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i] != null && !(parts[i] is Hd2GripPose))
                parts[i].enabled = false;
        }
    }

    static void SetHideFlagsRecursive(Transform root, HideFlags flags)
    {
        root.gameObject.hideFlags = flags;
        for (int i = 0; i < root.childCount; i++)
            SetHideFlagsRecursive(root.GetChild(i), flags);
    }

    // ---------- Play mode ----------

    void PlayModeGUI()
    {
        EditorGUILayout.HelpBox(
            "Hold the gun, pause, then pose the hand and finger bones in the Scene view. " +
            "Bake writes that pose into the prefab's Wrist and GripFingers markers.",
            MessageType.Info);

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

        prefab = ResolvePrefab((GameObject)EditorGUILayout.ObjectField("Grip prefab", prefab, typeof(GameObject), false));

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

    // The model files share names with the grip prefabs (GripSmg.fbx vs GripSmg.prefab),
    // so a model picked by mistake is swapped for the prefab of the same name.
    static GameObject ResolvePrefab(GameObject picked)
    {
        if (picked == null)
            return null;

        string path = AssetDatabase.GetAssetPath(picked);
        if (path.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase))
            return picked;

        var match = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + picked.name + ".prefab");
        if (match != null)
            return match;

        Debug.LogWarning($"Grip Baker: {path} is a model, not a prefab. Pick the prefab from {Folder}.");
        return null;
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

    // ---------- Bake ----------

    struct Baked
    {
        public string path;
        public Vector3 position;
        public Quaternion rotation;
    }

    static void Bake(Hd2GripHand grip, Transform gun, GameObject prefabAsset)
    {
        if (grip.Wrist == null)
        {
            Debug.LogError("Grip Baker: the gun has no Wrist marker.");
            return;
        }

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

        prefabAsset = ResolvePrefab(prefabAsset);
        if (prefabAsset == null)
        {
            Debug.LogError("Grip Baker: pick a .prefab from " + Folder + ", not a model file.");
            return;
        }

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

    // The gun in hand (or on the posing stand) is a copy of the prefab. Give it the same
    // markers so it matches the bake right away.
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
        {
            pose = gun.gameObject.AddComponent<Hd2GripPose>();
            pose.hideFlags = gun.gameObject.hideFlags;
        }

        pose.baked = true;
        pose.handScale = handScale;
        grip.Rebind();
        grip.Track(gun);
    }
}
