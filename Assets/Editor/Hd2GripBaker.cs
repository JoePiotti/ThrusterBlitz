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
    [SerializeField] Transform posingUpper;
    [SerializeField] Transform posingFore;
    [SerializeField] Transform posingArmEnd;
    [SerializeField] Vector3 posingRight;
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
        // Keep the arm reaching for the hand while you move it around.
        if (!EditorApplication.isPlaying && posingRoot != null && posingGrip != null)
            ReachPosingArm();
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
        {
            PoseFromMarkers(posingGrip, posingGun);
            ReachPosingArm();
        }

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

        Transform wristMarker = Hd2GripFingers.Find(gun.transform, "Wrist");
        posingUpper = Hd2GripFingers.Find(robot.transform, "Arm.L");
        if (posingUpper == null)
            posingUpper = Hd2GripFingers.Find(robot.transform, "UpperArm_L");
        posingFore = Hd2GripFingers.Find(robot.transform, "Arm.L.002");
        if (posingFore == null)
            posingFore = Hd2GripFingers.Find(robot.transform, "Forearm_L");
        Transform handR = Hd2GripFingers.Find(robot.transform, "Hand.R");
        if (handR == null)
            handR = Hd2GripFingers.Find(robot.transform, "Hand_R");
        PlaceRobot(robot.transform, gun.transform, wristMarker, hand, handR);

        // Same as the game: the hand bone hangs off the body, not the forearm, and an
        // empty ArmEnd keeps its old place on the forearm.
        posingArmEnd = new GameObject("Hand.L ArmEnd").transform;
        posingArmEnd.SetParent(hand.parent, false);
        posingArmEnd.localPosition = hand.localPosition;
        posingArmEnd.localRotation = hand.localRotation;
        hand.SetParent(posingRoot.transform, true);
        SetHideFlagsRecursive(posingRoot.transform, HideFlags.DontSave);

        posingGun = gun.transform;
        posingGrip = new Hd2GripHand(hand, false);
        posingGrip.Track(posingGun);
        if (posingGrip.Wrist == null)
            Debug.LogWarning($"Grip Baker: {prefab.name} has no Wrist marker. Bake needs one.");
        PoseFromMarkers(posingGrip, posingGun);
        ReachPosingArm();

        Selection.activeTransform = hand;
        SceneView.lastActiveSceneView?.FrameSelected();
    }

    void EndPosing()
    {
        if (posingRoot != null)
            DestroyImmediate(posingRoot);
        posingRoot = null;
        posingGun = null;
        posingUpper = null;
        posingFore = null;
        posingArmEnd = null;
        posingGrip = null;
    }

    // Only for looks: the bake stores the hand relative to the gun, not the body.
    // Turns the robot to face down the barrel and stands it so the left shoulder is
    // behind and above the grip, with the elbow bent.
    void PlaceRobot(Transform robot, Transform gun, Transform wrist, Transform hand, Transform handR)
    {
        posingRight = Vector3.right;
        if (wrist == null)
            return;

        if (posingUpper == null || posingFore == null || handR == null)
        {
            robot.position += wrist.position - hand.position;
            return;
        }

        Vector3 right = Vector3.ProjectOnPlane(handR.position - hand.position, Vector3.up);
        Transform muzzle = Hd2GripFingers.Find(gun, "Muzzle");
        Vector3 barrel = Vector3.ProjectOnPlane(muzzle != null ? muzzle.position - wrist.position : gun.forward, Vector3.up);
        if (right.sqrMagnitude > 0.0001f && barrel.sqrMagnitude > 0.0001f)
        {
            Vector3 facing = Vector3.Cross(right.normalized, Vector3.up);
            robot.rotation = Quaternion.FromToRotation(facing, barrel.normalized) * robot.rotation;
            right = Quaternion.FromToRotation(facing, barrel.normalized) * right;
        }

        posingRight = right.sqrMagnitude > 0.0001f ? right.normalized : Vector3.right;
        Vector3 forward = barrel.sqrMagnitude > 0.0001f ? barrel.normalized : Vector3.Cross(posingRight, Vector3.up);
        float reach = Vector3.Distance(posingUpper.position, posingFore.position)
            + Vector3.Distance(posingFore.position, hand.position);
        Vector3 shoulder = wrist.position
            - forward * (reach * 0.65f)
            + Vector3.up * (reach * 0.3f)
            - posingRight * (reach * 0.15f);
        robot.position += shoulder - posingUpper.position;
    }

    // Two-bone reach from the shoulder to the hand bone, elbow down and out to the left,
    // then the forearm rolls to match the hand.
    void ReachPosingArm()
    {
        if (posingUpper == null || posingFore == null || posingArmEnd == null || posingGrip == null || posingGrip.hand == null)
            return;

        Transform hand = posingGrip.hand;
        Vector3 shoulder = posingUpper.position;
        float upperLength = Vector3.Distance(shoulder, posingFore.position);
        float foreLength = Vector3.Distance(posingFore.position, posingArmEnd.position);
        if (upperLength < 0.01f || foreLength < 0.01f)
            return;

        Vector3 target = hand.position;
        Vector3 toTarget = target - shoulder;
        if (toTarget.sqrMagnitude < 0.0001f)
            return;
        float distance = Mathf.Clamp(toTarget.magnitude, 0.02f, upperLength + foreLength - 0.001f);
        Vector3 direction = toTarget.normalized;
        Vector3 pole = Vector3.ProjectOnPlane(Vector3.down - posingRight * 0.6f, direction);
        if (pole.sqrMagnitude < 0.0001f)
            pole = Vector3.ProjectOnPlane(-posingRight, direction);
        pole.Normalize();

        float cos = Mathf.Clamp(
            (upperLength * upperLength + distance * distance - foreLength * foreLength) / (2f * upperLength * distance),
            -1f,
            1f);
        float sin = Mathf.Sqrt(Mathf.Max(0f, 1f - cos * cos));
        Vector3 elbow = shoulder + direction * (upperLength * cos) + pole * (upperLength * sin);

        PointAt(posingUpper, posingFore, elbow);
        PointAt(posingFore, posingArmEnd, target);
        TwistToHand(posingFore, posingArmEnd, hand);
    }

    static void PointAt(Transform bone, Transform child, Vector3 point)
    {
        Vector3 current = child.position - bone.position;
        Vector3 desired = point - bone.position;
        if (current.sqrMagnitude < 0.000001f || desired.sqrMagnitude < 0.000001f)
            return;
        bone.rotation = Quaternion.FromToRotation(current, desired) * bone.rotation;
    }

    static void TwistToHand(Transform forearm, Transform end, Transform hand)
    {
        Vector3 axis = end.position - forearm.position;
        if (axis.sqrMagnitude < 0.000001f)
            return;
        axis.Normalize();

        Quaternion delta = hand.rotation * Quaternion.Inverse(end.rotation);
        Vector3 along = Vector3.Dot(new Vector3(delta.x, delta.y, delta.z), axis) * axis;
        var twist = new Quaternion(along.x, along.y, along.z, delta.w);
        float length = Mathf.Sqrt(twist.x * twist.x + twist.y * twist.y + twist.z * twist.z + twist.w * twist.w);
        if (length < 0.0001f)
            return;
        forearm.rotation = new Quaternion(twist.x / length, twist.y / length, twist.z / length, twist.w / length) * forearm.rotation;
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
        if (path.StartsWith(Folder) && path.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase))
            return picked;

        // Models, and older copies elsewhere (Assets/Prefabs/GripSmg.prefab), are not what the
        // pickups equip. Use the GripWeapons prefab with the same name.
        var match = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + picked.name + ".prefab");
        if (match != null)
        {
            if (match != picked)
                Debug.Log($"Grip Baker: using {Folder}{picked.name}.prefab instead of {path}.");
            return match;
        }

        Debug.LogWarning($"Grip Baker: {path} is not in {Folder}. Pick the grip prefab the pickup equips.");
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
