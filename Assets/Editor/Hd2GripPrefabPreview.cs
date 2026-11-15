using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// While a grip prefab (one with Wrist and GripFingers) is open in Prefab Mode, shows the
/// real robot hand on it at its in-game size, driven live by the markers you edit.
/// The preview is never saved into the prefab and can't be clicked, so clicks still pick markers.
/// Toggle with Tools > HD2 > Show Robot Hand In Grip Prefabs.
/// </summary>
[InitializeOnLoad]
static class Hd2GripPrefabPreview
{
    const string RobotPath = "Assets/Models/Bot/PlayerBotRobot.fbx";
    const string MenuPath = "Tools/HD2/Show Robot Hand In Grip Prefabs";
    const string PrefKey = "Hd2GripPrefabPreview";
    const double Interval = 0.05;

    static GameObject container;
    static PrefabStage shownFor;
    static Hd2GripHand grip;
    static Transform upper;
    static Transform fore;
    static Transform armEnd;
    static Vector3 lateral = Vector3.right;
    static double nextUpdate;

    static Hd2GripPrefabPreview()
    {
        EditorApplication.update += Update;
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

    static void Update()
    {
        if (EditorApplication.timeSinceStartup < nextUpdate)
            return;
        nextUpdate = EditorApplication.timeSinceStartup + Interval;

        PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
        if (!Enabled || stage == null || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Clear();
            return;
        }

        Transform gun = stage.prefabContentsRoot != null ? stage.prefabContentsRoot.transform : null;
        Transform wrist = gun != null ? Hd2GripFingers.Find(gun, "Wrist") : null;
        if (wrist == null || Hd2GripFingers.Find(gun, "GripFingers") == null)
        {
            Clear();
            return;
        }

        if (container == null || shownFor != stage || grip == null)
            Build(stage, gun, wrist);
        if (grip == null)
            return;

        // Markers can be added, renamed or reparented while editing, so look them up again.
        Hd2GripBaker.PoseFromMarkers(grip, gun);
        Reach();
        SceneView.RepaintAll();
    }

    static void Build(PrefabStage stage, Transform gun, Transform wrist)
    {
        Clear();
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(RobotPath);
        if (model == null)
            return;

        container = new GameObject("~Robot hand preview (not saved)");
        container.hideFlags = HideFlags.DontSave;
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
        SceneVisibilityManager.instance.DisablePicking(container, true);
        shownFor = stage;
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
        Hd2GripBaker.PointAt(upper, fore, elbow);
        Hd2GripBaker.PointAt(fore, armEnd, grip.hand.position);
        Hd2GripBaker.TwistToHand(fore, armEnd, grip.hand);
    }

    static void SetHideFlags(Transform root, HideFlags flags)
    {
        root.gameObject.hideFlags = flags;
        for (int i = 0; i < root.childCount; i++)
            SetHideFlags(root.GetChild(i), flags);
    }
}
