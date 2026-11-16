using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Grip editing in Prefab Mode. Open a grip prefab (one with a Wrist child) and the robot's
/// left hand and arm appear on it at their in-game size.
/// - Click a joint sphere in the Scene view to select that hand or finger bone, then move/rotate it.
/// - Whatever you pose is written straight into the prefab: the Wrist takes the hand bone's pose
///   and Hd2GripPose stores the fingers. Save the prefab as usual. The old GripFingers markers
///   are removed the first time you pose, since the hand replaces them.
/// - Auto Grip (Scene view, top left) curls the fingers onto the gun with the index on the trigger.
/// The preview itself is never saved into the prefab. Toggle under Tools > HD2.
/// </summary>
[InitializeOnLoad]
static class Hd2GripPrefabPreview
{
    const string RobotPath = "Assets/Models/Bot/PlayerBotRobot.fbx";
    const string MenuPath = "Tools/HD2/Show Robot Hand In Grip Prefabs";
    const string PrefKey = "Hd2GripPrefabPreview";
    const double Interval = 0.05;

    static readonly Color[] FingerColors =
    {
        new Color(1f, 0.82f, 0.25f),
        new Color(0.35f, 0.85f, 1f),
        new Color(0.45f, 0.95f, 0.45f),
        new Color(1f, 0.55f, 0.25f),
        new Color(0.85f, 0.45f, 1f),
    };

    static GameObject container;
    static PrefabStage shownFor;
    static Hd2GripHand grip;
    static Transform upper;
    static Transform fore;
    static Transform armEnd;
    static Vector3 lateral = Vector3.right;
    static double nextUpdate;
    static string status;

    // What the preview looked like the last time we set it, to tell your edits from ours.
    static Vector3 shotHandPosition;
    static Quaternion shotHandRotation;
    static readonly Quaternion[] shotFingers = new Quaternion[Hd2GripFingers.BoneCount];
    static bool shotTaken;

    static Hd2GripPrefabPreview()
    {
        EditorApplication.update += Update;
        SceneView.duringSceneGui += OnSceneGUI;
        PrefabStage.prefabStageClosing += _ => Clear();
        AssemblyReloadEvents.beforeAssemblyReload += Clear;
        EditorApplication.playModeStateChanged += _ => Clear();
    }

    static bool Enabled
    {
        get => EditorPrefs.GetBool(PrefKey, true);
        set => EditorPrefs.SetBool(PrefKey, value);
    }

    [MenuItem(MenuPath)]
    static void Toggle()
    {
        Enabled = !Enabled;
        if (!Enabled)
            Clear();
    }

    [MenuItem(MenuPath, true)]
    static bool ToggleValidate()
    {
        Menu.SetChecked(MenuPath, Enabled);
        return true;
    }

    // ---------- Sync ----------

    static bool TryStage(out PrefabStage stage, out Transform gun, out Transform wrist)
    {
        stage = PrefabStageUtility.GetCurrentPrefabStage();
        gun = null;
        wrist = null;
        if (!Enabled || stage == null || stage.prefabContentsRoot == null || EditorApplication.isPlayingOrWillChangePlaymode)
            return false;
        gun = stage.prefabContentsRoot.transform;
        wrist = Hd2GripFingers.Find(gun, "Wrist");
        return wrist != null;
    }

    static void Update()
    {
        if (EditorApplication.timeSinceStartup < nextUpdate)
            return;
        nextUpdate = EditorApplication.timeSinceStartup + Interval;

        if (!TryStage(out PrefabStage stage, out Transform gun, out Transform wrist))
        {
            Clear();
            return;
        }

        if (container == null || shownFor != stage || grip == null || grip.hand == null)
            Build(stage, gun, wrist);
        if (grip == null)
            return;

        if (shotTaken && PreviewChanged())
            WriteToPrefab(stage, gun, wrist);
        else
            DriveFromPrefab(gun, wrist);

        Reach();
        TakeShot();
        SceneView.RepaintAll();
    }

    static bool PreviewChanged()
    {
        Transform hand = grip.hand;
        if ((hand.position - shotHandPosition).sqrMagnitude > 1e-10f || Quaternion.Angle(hand.rotation, shotHandRotation) > 0.01f)
            return true;
        for (int i = 0; i < Hd2GripFingers.BoneCount; i++)
        {
            Transform bone = grip.Bone(i);
            if (bone != null && Quaternion.Angle(bone.localRotation, shotFingers[i]) > 0.01f)
                return true;
        }

        return false;
    }

    static void TakeShot()
    {
        shotHandPosition = grip.hand.position;
        shotHandRotation = grip.hand.rotation;
        for (int i = 0; i < Hd2GripFingers.BoneCount; i++)
        {
            Transform bone = grip.Bone(i);
            shotFingers[i] = bone != null ? bone.localRotation : Quaternion.identity;
        }

        shotTaken = true;
    }

    // Prefab -> preview.
    static void DriveFromPrefab(Transform gun, Transform wrist)
    {
        grip.Rebind();
        grip.Track(gun);
        var pose = gun.GetComponent<Hd2GripPose>();
        if (pose != null && pose.baked)
            grip.PinHandTo(wrist);
        else
            LegacyHand(grip, wrist);
        grip.ApplyFingers();
    }

    // Preview -> prefab. The GripFingers markers go away the first time, since the hand replaces them.
    static void WriteToPrefab(PrefabStage stage, Transform gun, Transform wrist)
    {
        Transform hand = grip.hand;
        wrist.SetPositionAndRotation(hand.position, hand.rotation);
        EditorUtility.SetDirty(wrist);

        var pose = gun.GetComponent<Hd2GripPose>();
        if (pose == null)
            pose = Undo.AddComponent<Hd2GripPose>(gun.gameObject);
        pose.baked = true;
        pose.fingers = grip.ReadFingerData();
        EditorUtility.SetDirty(pose);

        Transform markers = Hd2GripFingers.Find(gun, "GripFingers");
        if (markers != null)
            Undo.DestroyObjectImmediate(markers.gameObject);

        EditorSceneManager.MarkSceneDirty(stage.scene);
        status = "Posed. Save the prefab to keep it.";
    }

    // Unposed guns: Wrist forward runs along the fingers, Wrist right toward the index side.
    static void LegacyHand(Hd2GripHand hand, Transform wrist)
    {
        hand.hand.position = wrist.position;
        Transform index = hand.Bone(3);
        Transform indexMid = hand.Bone(4);
        Transform middle = hand.Bone(6);
        if (index != null && indexMid != null && middle != null)
        {
            Vector3 length = hand.hand.InverseTransformDirection(indexMid.position - index.position).normalized;
            Vector3 spread = hand.hand.InverseTransformDirection(index.position - middle.position).normalized;
            hand.hand.rotation = MapAxes(length, spread, wrist.forward, wrist.right);
        }

        Hd2GripHand.SetWorldScale(hand.hand, Hd2GripHand.HandWorldScale);
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
        return Quaternion.FromToRotation(swing * fromSide, toSide) * swing;
    }

    // ---------- Scene view ----------

    static void OnSceneGUI(SceneView view)
    {
        if (grip == null || grip.hand == null || container == null)
            return;
        if (!TryStage(out _, out Transform gun, out _))
            return;

        var previousZ = Handles.zTest;
        Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;

        JointButton(grip.hand, Color.white, 0.09f);
        for (int finger = 0; finger < Hd2GripFingers.Names.Length; finger++)
        {
            for (int joint = 0; joint < 3; joint++)
            {
                Transform bone = grip.Bone(finger * 3 + joint);
                if (bone == null)
                    continue;
                Vector3 next = joint < 2 && grip.Bone(finger * 3 + joint + 1) != null
                    ? grip.Bone(finger * 3 + joint + 1).position
                    : bone.childCount > 0 ? bone.GetChild(0).position : bone.position;
                Handles.color = FingerColors[finger];
                Handles.DrawAAPolyLine(4f, bone.position, next);
                JointButton(bone, FingerColors[finger], 0.06f);
            }
        }

        Handles.zTest = previousZ;

        Handles.BeginGUI();
        GUILayout.BeginArea(new Rect(10, 10, 260, 140), EditorStyles.helpBox);
        GUILayout.Label("Robot hand: click a joint, then rotate it", EditorStyles.boldLabel);
        if (GUILayout.Button("Auto Grip (index on trigger)"))
            RunAutoGrip(gun);
        if (GUILayout.Button("Straighten fingers"))
            Straighten();
        if (!string.IsNullOrEmpty(status))
            GUILayout.Label(status, EditorStyles.wordWrappedMiniLabel);
        GUILayout.EndArea();
        Handles.EndGUI();
    }

    static void JointButton(Transform bone, Color color, float size)
    {
        float handleSize = HandleUtility.GetHandleSize(bone.position) * size;
        Handles.color = Selection.activeTransform == bone ? Color.red : color;
        if (Handles.Button(bone.position, Quaternion.identity, handleSize, handleSize, Handles.SphereHandleCap))
        {
            Selection.activeTransform = bone;
            Tools.current = Tool.Rotate;
            Tools.pivotRotation = PivotRotation.Local;
        }
    }

    static void RunAutoGrip(Transform gun)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(RobotPath);
        Undo.RegisterFullObjectHierarchyUndo(grip.hand.gameObject, "Auto Grip");
        string report = Hd2AutoGrip.Apply(grip, gun, model);
        status = report.Replace("\n", " · ");
        Debug.Log("Auto Grip:\n" + report);
    }

    static void Straighten()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(RobotPath);
        if (model == null)
            return;
        Undo.RegisterFullObjectHierarchyUndo(grip.hand.gameObject, "Straighten fingers");
        for (int i = 0; i < Hd2GripFingers.BoneCount; i++)
        {
            Transform bone = grip.Bone(i);
            Transform rest = bone != null ? Hd2GripFingers.Find(model.transform, bone.name) : null;
            if (rest != null)
                bone.localRotation = rest.localRotation;
        }
    }

    // ---------- Preview robot ----------

    static void Build(PrefabStage stage, Transform gun, Transform wrist)
    {
        Clear();
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(RobotPath);
        if (model == null)
            return;

        container = new GameObject("~Robot hand preview (not saved)");
        SceneManager.MoveGameObjectToScene(container, stage.scene);

        GameObject robot = Object.Instantiate(model);
        robot.name = "Robot";
        SceneManager.MoveGameObjectToScene(robot, stage.scene);
        robot.transform.SetParent(container.transform, false);
        foreach (Animator animator in robot.GetComponentsInChildren<Animator>(true))
            animator.enabled = false;
        foreach (SkinnedMeshRenderer skin in robot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            skin.updateWhenOffscreen = true;

        Transform hand = Hd2GripFingers.Find(robot.transform, "Hand.L");
        Transform handR = Hd2GripFingers.Find(robot.transform, "Hand.R");
        upper = Hd2GripFingers.Find(robot.transform, "Arm.L");
        fore = Hd2GripFingers.Find(robot.transform, "Arm.L.002");
        if (hand == null)
        {
            Clear();
            return;
        }

        grip = new Hd2GripHand(hand, false);
        Place(robot.transform, gun, wrist, hand, handR);

        armEnd = new GameObject("Hand.L ArmEnd").transform;
        armEnd.SetParent(hand.parent, false);
        armEnd.localPosition = hand.localPosition;
        armEnd.localRotation = hand.localRotation;
        hand.SetParent(container.transform, true);

        SetHideFlags(container.transform, HideFlags.DontSave);
        // Clicking the robot's mesh shouldn't select it; the joint spheres select bones.
        SceneVisibilityManager.instance.DisablePicking(container, true);
        shownFor = stage;
        shotTaken = false;
        status = null;
        DriveFromPrefab(gun, wrist);
    }

    static void Clear()
    {
        if (container != null)
            Object.DestroyImmediate(container);
        container = null;
        shownFor = null;
        grip = null;
        upper = null;
        fore = null;
        armEnd = null;
        shotTaken = false;
    }

    // Stands the robot behind the gun, facing down the barrel, left shoulder behind and above the grip.
    static void Place(Transform robot, Transform gun, Transform wrist, Transform hand, Transform handR)
    {
        lateral = Vector3.right;
        if (upper == null || fore == null || handR == null)
        {
            robot.position += wrist.position - hand.position;
            return;
        }

        Vector3 right = Vector3.ProjectOnPlane(handR.position - hand.position, Vector3.up);
        Transform muzzle = Hd2GripFingers.Find(gun, "Muzzle");
        Vector3 barrel = Vector3.ProjectOnPlane(muzzle != null ? muzzle.position - wrist.position : gun.forward, Vector3.up);
        if (right.sqrMagnitude > 0.0001f && barrel.sqrMagnitude > 0.0001f)
        {
            Quaternion turn = Quaternion.FromToRotation(Vector3.Cross(right.normalized, Vector3.up), barrel.normalized);
            robot.rotation = turn * robot.rotation;
            right = turn * right;
        }

        lateral = right.sqrMagnitude > 0.0001f ? right.normalized : Vector3.right;
        Vector3 forward = barrel.sqrMagnitude > 0.0001f ? barrel.normalized : Vector3.Cross(lateral, Vector3.up);
        float reach = Vector3.Distance(upper.position, fore.position) + Vector3.Distance(fore.position, hand.position);
        Vector3 shoulder = wrist.position - forward * (reach * 0.65f) + Vector3.up * (reach * 0.3f) - lateral * (reach * 0.15f);
        robot.position += shoulder - upper.position;
    }

    // Two-bone reach from the shoulder to the hand, elbow down and out, forearm rolled to the hand.
    static void Reach()
    {
        if (upper == null || fore == null || armEnd == null || grip == null || grip.hand == null)
            return;

        Vector3 shoulder = upper.position;
        float upperLength = Vector3.Distance(shoulder, fore.position);
        float foreLength = Vector3.Distance(fore.position, armEnd.position);
        Vector3 toTarget = grip.hand.position - shoulder;
        if (upperLength < 0.01f || foreLength < 0.01f || toTarget.sqrMagnitude < 0.0001f)
            return;

        float distance = Mathf.Clamp(toTarget.magnitude, 0.02f, upperLength + foreLength - 0.001f);
        Vector3 direction = toTarget.normalized;
        Vector3 pole = Vector3.ProjectOnPlane(Vector3.down - lateral * 0.6f, direction);
        if (pole.sqrMagnitude < 0.0001f)
            pole = Vector3.ProjectOnPlane(-lateral, direction);
        pole.Normalize();

        float cos = Mathf.Clamp(
            (upperLength * upperLength + distance * distance - foreLength * foreLength) / (2f * upperLength * distance),
            -1f,
            1f);
        float sin = Mathf.Sqrt(Mathf.Max(0f, 1f - cos * cos));
        Vector3 elbow = shoulder + direction * (upperLength * cos) + pole * (upperLength * sin);
        PointAt(upper, fore, elbow);
        PointAt(fore, armEnd, grip.hand.position);
        TwistToHand(fore, armEnd, grip.hand);
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

    static void SetHideFlags(Transform root, HideFlags flags)
    {
        root.gameObject.hideFlags = flags;
        for (int i = 0; i < root.childCount; i++)
            SetHideFlags(root.GetChild(i), flags);
    }
}
