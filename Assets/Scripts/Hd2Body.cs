using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

/// <summary>
/// The head follows the headset. The body yaw stays put until the head is more
/// than bodyFollowAngle degrees off, then it turns to stay at that limit.
/// Legs walk along the stick direction and stand sideways on a rail.
/// </summary>
[DefaultExecutionOrder(200)]
public class Hd2Body : MonoBehaviour
{
    public Transform head;
    public Transform body;
    public Transform leftHand;
    public Transform rightHand;

    public Transform upperArmL;
    public Transform forearmL;
    public Transform handL;
    public Transform upperArmR;
    public Transform forearmR;
    public Transform handR;
    [Tooltip("Meters to shift the body behind the headset so the chest does not block looking down.")]
    public float bodyBackOffset = 0.2f;
    [Tooltip("Extra rotation applied before the body yaws with the headset. The blockout robot needs -90 on X.")]
    public Vector3 bodyRestEuler = new Vector3(-90f, 0f, 0f);
    [Tooltip("Scale the whole skinned body so the head stays with the headset. Blockout legs scale on their own.")]
    public bool scaleWholeBody;
    [Tooltip("Bend of a finger, in degrees, when the trigger or grip is fully pulled and the hand is empty.")]
    public float fingerCurlDegrees = 65f;
    [Tooltip("Index curl while a gun is held. The finger stays on the trigger instead of following the pull.")]
    public float armedIndexCurl = 0.4f;
    [Tooltip("Index curl on the pistol. Lower keeps that finger extended onto the trigger.")]
    public float pistolIndexCurl = 0.45f;
    [Tooltip("Curl of the other three fingers around the pistol grip.")]
    public float pistolGripCurl = 1.25f;
    [Tooltip("Degrees the gun trigger swings at a full pull.")]
    public float gunTriggerDegrees = 25f;
    [Tooltip("The body starts turning once the head is this far off to either side.")]
    public float bodyFollowAngle = 30f;
    [Tooltip("How fast the walk cycle plays, in steps per meter.")]
    public float walkStepRate = 1.6f;
    [Tooltip("Resting length of each arm as a fraction of the rig. 0.6 is three fifths, so a normal gun hold stretches the arm out.")]
    public float armReachScale = 0.6f;
    [Tooltip("How many times longer the upper arm and the forearm may each get. Extra reach is split between them.")]
    public float forearmMaxStretch = 2.5f;
    [Tooltip("Drag these in the prefab. They are not skinned, so the arm mesh stays put. Play mode swings the arm around them.")]
    public Transform shoulderPivotL;
    [Tooltip("Drag these in the prefab. They are not skinned, so the arm mesh stays put. Play mode swings the arm around them.")]
    public Transform shoulderPivotR;
    [Tooltip("Where the end of the left forearm should meet the gun. Drag this while the game is stopped.")]
    public Transform gripPivotL;
    [Tooltip("Where the end of the right forearm should meet the gun. Drag this while the game is stopped.")]
    public Transform gripPivotR;

    Transform hips;
    Transform neck;
    Transform thighL;
    Transform thighR;
    Transform footL;
    Transform footR;
    ParticleSystem sparkL;
    ParticleSystem sparkR;
    Transform[] chainL;
    Transform[] chainR;
    Vector3 restHipsLocal;
    float restNeckAboveHip;
    float restFootOffset;
    int thighAxisL = 1;
    int thighAxisR = 1;
    bool posed;
    bool fingersCached;
    bool poseReady;
    bool bodyYawReady;
    float bodyYawOffset;
    float gait;
    float gaitWeight;
    Hd2Locomotion locomotion;
    Transform neckBone;
    Transform headBone;
    Transform shinL;
    Transform shinR;
    Transform toeL;
    Transform toeR;
    Quaternion restNeck = Quaternion.identity;
    Quaternion restHead = Quaternion.identity;
    Quaternion restThighL = Quaternion.identity;
    Quaternion restThighR = Quaternion.identity;
    Quaternion restShinL = Quaternion.identity;
    Quaternion restShinR = Quaternion.identity;
    Quaternion restFootL = Quaternion.identity;
    Quaternion restFootR = Quaternion.identity;
    readonly List<Finger> fingers = new List<Finger>();
    readonly Dictionary<Transform, Quaternion> fingerRest = new Dictionary<Transform, Quaternion>();
    readonly Dictionary<Transform, Vector3> fingerHinge = new Dictionary<Transform, Vector3>();
    readonly Dictionary<Transform, Quaternion> triggerRest = new Dictionary<Transform, Quaternion>();
    readonly Dictionary<Transform, Vector3> triggerRestPos = new Dictionary<Transform, Vector3>();
    readonly Dictionary<Transform, Vector3> fingerAxis = new Dictionary<Transform, Vector3>();
    readonly Dictionary<Transform, Vector3> palmAxis = new Dictionary<Transform, Vector3>();
    readonly Dictionary<Transform, Vector3> lengthAxis = new Dictionary<Transform, Vector3>();
    readonly Dictionary<Transform, Vector3> indexSpread = new Dictionary<Transform, Vector3>();
    InputAction leftTrigger;
    InputAction leftGrip;
    InputAction rightTrigger;
    InputAction rightGrip;
    static readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();

    void Awake()
    {
        if (GetComponent<Hd2Health>() == null)
            gameObject.AddComponent<Hd2Health>();
        if (GetComponent<Hd2ThrustMeter>() == null)
            gameObject.AddComponent<Hd2ThrustMeter>();
        if (GetComponent<Hd2ControlMap>() == null)
            gameObject.AddComponent<Hd2ControlMap>();
        if (GetComponent<Hd2UserMenu>() == null)
            gameObject.AddComponent<Hd2UserMenu>();
        if (GetComponent<Hd2PlayerDeath>() == null)
            gameObject.AddComponent<Hd2PlayerDeath>();
        if (body != null)
        {
            var animators = body.GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < animators.Length; i++)
                animators[i].enabled = false;
        }

        leftTrigger = FingerAxis("LeftHand", "trigger");
        leftGrip = FingerAxis("LeftHand", "grip");
        rightTrigger = FingerAxis("RightHand", "trigger");
        rightGrip = FingerAxis("RightHand", "grip");
    }

    void OnDestroy()
    {
        Dispose(ref leftTrigger);
        Dispose(ref leftGrip);
        Dispose(ref rightTrigger);
        Dispose(ref rightGrip);
        DestroySpark(sparkL);
        DestroySpark(sparkR);
    }

    static InputAction FingerAxis(string node, string control)
    {
        var action = new InputAction("Hd2Finger" + node + control, InputActionType.Value, expectedControlType: "Axis");
        action.AddBinding("<XRController>{" + node + "}/" + control);
        action.Enable();
        return action;
    }

    bool shoulderPivotApplied;

    void ApplyShoulderPivots()
    {
        if (shoulderPivotApplied)
            return;
        if (shoulderPivotL == null && shoulderPivotR == null)
        {
            shoulderPivotApplied = true;
            return;
        }

        var skin = body.GetComponentInChildren<SkinnedMeshRenderer>();
        if (skin == null || skin.sharedMesh == null || upperArmL == null || upperArmR == null)
            return;

        Vector3 scale = body.localScale;
        body.localScale = Vector3.one;
        Vector3 leftPivot = shoulderPivotL != null ? shoulderPivotL.position : upperArmL.position;
        Vector3 rightPivot = shoulderPivotR != null ? shoulderPivotR.position : upperArmR.position;
        var mesh = Instantiate(skin.sharedMesh);
        mesh.name = skin.sharedMesh.name + " Pivot";
        var poses = mesh.bindposes;
        var bones = skin.bones;
        ShiftPivot(upperArmL, leftPivot, bones, poses);
        ShiftPivot(upperArmR, rightPivot, bones, poses);
        mesh.bindposes = poses;
        skin.sharedMesh = mesh;
        body.localScale = scale;
        shoulderPivotApplied = true;
    }

    static void ShiftPivot(Transform bone, Vector3 worldPivot, Transform[] bones, Matrix4x4[] poses)
    {
        if (bone == null || bone.parent == null)
            return;

        Vector3 worldDelta = worldPivot - bone.position;
        if (worldDelta.sqrMagnitude < 0.0000001f)
            return;

        int index = -1;
        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i] == bone)
            {
                index = i;
                break;
            }
        }

        if (index < 0)
            return;

        int childCount = bone.childCount;
        var childPositions = new Vector3[childCount];
        var childRotations = new Quaternion[childCount];
        for (int i = 0; i < childCount; i++)
        {
            Transform child = bone.GetChild(i);
            childPositions[i] = child.position;
            childRotations[i] = child.rotation;
        }

        Matrix4x4 oldWorld = bone.localToWorldMatrix;
        bone.localPosition += bone.parent.InverseTransformVector(worldDelta);
        Matrix4x4 newWorld = bone.localToWorldMatrix;
        poses[index] = newWorld.inverse * oldWorld * poses[index];
        for (int i = 0; i < childCount; i++)
            bone.GetChild(i).SetPositionAndRotation(childPositions[i], childRotations[i]);
    }

    void LateUpdate()
    {
        if (head == null || body == null)
            return;

        Vector3 look = Vector3.ProjectOnPlane(head.forward, Vector3.up);
        Vector3 feet = new Vector3(head.position.x, transform.position.y, head.position.z);
        if (look.sqrMagnitude > 0.0001f)
            feet -= look.normalized * bodyBackOffset;
        body.position = feet;

        UpdateBodyYaw(look);

        EnsureBones();
        ApplyShoulderPivots();
        if (!fingersCached)
            CacheFingers();
        if (!poseReady)
            CachePoseRest();
        var skins = body.GetComponentsInChildren<SkinnedMeshRenderer>();
        for (int i = 0; i < skins.Length; i++)
            skins[i].updateWhenOffscreen = true;
        if (scaleWholeBody)
            FitWholeBodyToHead();
        else
            FitLegsToHead();
        SolveArmChain(chainL, upperArmL, forearmL, handL, leftHand, -1f);
        SolveArmChain(chainR, upperArmR, forearmR, handR, rightHand, 1f);
        PoseHead(look);
        PoseLegs();
        UpdateGrindSparks();
    }

    void UpdateBodyYaw(Vector3 look)
    {
        Vector3 rootForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (look.sqrMagnitude < 0.0001f || rootForward.sqrMagnitude < 0.0001f)
            return;

        float headOffset = Vector3.SignedAngle(rootForward, look, Vector3.up);
        if (!bodyYawReady)
        {
            bodyYawOffset = headOffset;
            bodyYawReady = true;
        }

        float gap = Mathf.DeltaAngle(bodyYawOffset, headOffset);
        if (Mathf.Abs(gap) > bodyFollowAngle)
            bodyYawOffset = headOffset - Mathf.Sign(gap) * bodyFollowAngle;

        body.localRotation = Quaternion.Euler(0f, bodyYawOffset, 0f) * Quaternion.Euler(bodyRestEuler);
    }

    void CachePoseRest()
    {
        if (headBone == null || thighL == null || thighR == null || shinL == null || shinR == null)
            return;

        restHead = headBone.localRotation;
        if (neckBone != null)
            restNeck = neckBone.localRotation;
        restThighL = thighL.localRotation;
        restThighR = thighR.localRotation;
        restShinL = shinL.localRotation;
        restShinR = shinR.localRotation;
        if (footL != null)
            restFootL = footL.localRotation;
        if (footR != null)
            restFootR = footR.localRotation;
        poseReady = true;
    }

    void PoseHead(Vector3 look)
    {
        if (!poseReady || headBone == null)
            return;

        Vector3 rootForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (look.sqrMagnitude < 0.0001f || rootForward.sqrMagnitude < 0.0001f)
            return;

        float headOffset = Vector3.SignedAngle(rootForward, look, Vector3.up);
        float yaw = Mathf.DeltaAngle(bodyYawOffset, headOffset);
        float pitch = -Mathf.Asin(Mathf.Clamp(head.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
        ApplyLook(neckBone, restNeck, yaw * 0.35f, pitch * 0.35f);
        ApplyLook(headBone, restHead, yaw * 0.65f, pitch * 0.65f);
    }

    void ApplyLook(Transform bone, Quaternion rest, float yaw, float pitch)
    {
        if (bone == null)
            return;

        bone.localRotation = rest;
        Quaternion offset = Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.AngleAxis(pitch, body.right);
        bone.rotation = offset * bone.rotation;
    }

    void PoseLegs()
    {
        if (!poseReady)
            return;
        if (locomotion == null)
            locomotion = GetComponent<Hd2Locomotion>();
        if (locomotion == null)
            return;

        Vector3 rail = locomotion.GrindDirection;
        if (locomotion.IsGrinding && rail.sqrMagnitude > 0.0001f)
        {
            ClearHangStretch();
            gaitWeight = 0f;
            PoseSkate(rail);
            return;
        }

        if (locomotion.IsAirborne)
        {
            gaitWeight = 0f;
            PoseHang();
            return;
        }

        ClearHangStretch();

        Vector3 move = locomotion.PlanarVelocity;
        move.y = 0f;
        float speed = move.magnitude;
        float target = speed > 0.25f ? 1f : 0f;
        gaitWeight = Mathf.MoveTowards(gaitWeight, target, Time.deltaTime * 5f);
        if (gaitWeight <= 0.001f)
        {
            ResetLegs();
            return;
        }

        Vector3 bodyForward = Vector3.ProjectOnPlane(body.forward, Vector3.up);
        if (bodyForward.sqrMagnitude < 0.0001f)
            bodyForward = Vector3.forward;
        bodyForward.Normalize();
        Vector3 bodyRight = Vector3.Cross(Vector3.up, bodyForward);
        float forward = Vector3.Dot(move, bodyForward);
        float strafe = Vector3.Dot(move, bodyRight);
        // Backing up plays the forward step in reverse. Feet stay pointed forward.
        bool backward = forward < 0f;
        Vector3 stride = bodyForward * Mathf.Abs(forward) + bodyRight * strafe;
        if (stride.sqrMagnitude < 0.0001f)
            stride = bodyForward;
        else
            stride.Normalize();

        gait += (backward ? -speed : speed) * walkStepRate * Time.deltaTime;
        PoseWalkLeg(thighL, shinL, footL, toeL, restThighL, restShinL, restFootL, Mathf.Sin(gait), Mathf.Max(0f, Mathf.Cos(gait)), stride);
        PoseWalkLeg(thighR, shinR, footR, toeR, restThighR, restShinR, restFootR, Mathf.Sin(gait + Mathf.PI), Mathf.Max(0f, Mathf.Cos(gait + Mathf.PI)), stride);
    }

    bool legsHung;

    void PoseHang()
    {
        PoseHangLeg(thighL, shinL, footL, restThighL, restShinL, restFootL);
        PoseHangLeg(thighR, shinR, footR, restThighR, restShinR, restFootR);
        legsHung = true;
    }

    void PoseHangLeg(
        Transform thigh,
        Transform shin,
        Transform foot,
        Quaternion thighRest,
        Quaternion shinRest,
        Quaternion footRest)
    {
        SetLocal(thigh, thighRest);
        SetLocal(shin, shinRest);
        SetLocal(foot, footRest);
        if (thigh == null || shin == null)
            return;

        Vector3 thighDir = shin.position - thigh.position;
        AimBone(thigh, thighDir, Vector3.down);
        if (foot == null)
            return;

        Vector3 shinDir = foot.position - shin.position;
        AimBone(shin, shinDir, Vector3.down);
        SetLocal(foot, footRest);
        SetAxisScale(thigh, LengthAxis(thigh, shin), 1.15f);
        SetAxisScale(shin, LengthAxis(shin, foot), 1.15f);
    }

    void ClearHangStretch()
    {
        if (!legsHung)
            return;

        if (thighL != null)
            thighL.localScale = Vector3.one;
        if (thighR != null)
            thighR.localScale = Vector3.one;
        if (shinL != null)
            shinL.localScale = Vector3.one;
        if (shinR != null)
            shinR.localScale = Vector3.one;
        legsHung = false;
    }

    void ResetLegs()
    {
        SetLocal(thighL, restThighL);
        SetLocal(thighR, restThighR);
        SetLocal(shinL, restShinL);
        SetLocal(shinR, restShinR);
        SetLocal(footL, restFootL);
        SetLocal(footR, restFootR);
    }

    static void SetLocal(Transform bone, Quaternion rest)
    {
        if (bone != null)
            bone.localRotation = rest;
    }

    void PoseWalkLeg(
        Transform thigh,
        Transform shin,
        Transform foot,
        Transform toe,
        Quaternion thighRest,
        Quaternion shinRest,
        Quaternion footRest,
        float cycle,
        float lift,
        Vector3 stride)
    {
        SetLocal(thigh, thighRest);
        SetLocal(shin, shinRest);
        SetLocal(foot, footRest);
        if (thigh == null || shin == null)
            return;

        float amount = 0.5f * gaitWeight;
        Vector3 thighDir = shin.position - thigh.position;
        Vector3 thighAim = Vector3.down + stride * cycle * amount;
        AimBone(thigh, thighDir, thighAim);

        if (foot == null)
            return;

        Vector3 shinDir = foot.position - shin.position;
        Vector3 shinAim = Vector3.down + stride * cycle * amount * 0.35f + Vector3.up * lift * amount * 0.25f;
        if (shinAim.y > -0.15f)
            shinAim.y = -0.15f;
        AimBone(shin, shinDir, shinAim);

        // Leave the foot on its rest pose relative to the shin so it cannot
        // flip sole-up while the step blends out.
        SetLocal(foot, footRest);
    }

    void PoseSkate(Vector3 along)
    {
        Vector3 across = Vector3.Cross(Vector3.up, along);
        if (across.sqrMagnitude < 0.0001f)
            return;
        across.Normalize();
        PoseSkateLeg(thighL, shinL, footL, toeL, restThighL, restShinL, restFootL, across, along * 0.18f);
        PoseSkateLeg(thighR, shinR, footR, toeR, restThighR, restShinR, restFootR, across, -along * 0.1f);
    }

    void UpdateGrindSparks()
    {
        bool grinding = locomotion != null && locomotion.IsGrinding;
        Vector3 along = grinding ? locomotion.GrindDirection : Vector3.zero;
        if (!grinding || along.sqrMagnitude < 0.0001f || footL == null || footR == null)
        {
            StopSpark(sparkL);
            StopSpark(sparkR);
            return;
        }

        Vector3 side = Vector3.Cross(Vector3.up, along);
        PlaceSpark(ref sparkL, footL.position, (-along + side * 0.45f + Vector3.down * 0.2f).normalized);
        PlaceSpark(ref sparkR, footR.position, (-along - side * 0.45f + Vector3.down * 0.2f).normalized);
    }

    void PlaceSpark(ref ParticleSystem spark, Vector3 foot, Vector3 spray)
    {
        if (spark == null)
            spark = CreateGrindSpark();
        spark.transform.SetPositionAndRotation(foot + Vector3.down * 0.05f, Quaternion.LookRotation(spray));
        if (!spark.isPlaying)
            spark.Play();
    }

    static void StopSpark(ParticleSystem spark)
    {
        if (spark != null && spark.isPlaying)
            spark.Stop(false, ParticleSystemStopBehavior.StopEmitting);
    }

    static void DestroySpark(ParticleSystem spark)
    {
        if (spark != null)
            Destroy(spark.gameObject);
    }

    static ParticleSystem CreateGrindSpark()
    {
        var sparkObject = new GameObject("GrindSparks");
        sparkObject.hideFlags = HideFlags.DontSave;
        var spark = sparkObject.AddComponent<ParticleSystem>();
        spark.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = spark.main;
        main.playOnAwake = false;
        main.loop = true;
        main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.28f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2.4f, 5.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.016f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(1f, 0.97f, 0.85f),
            new Color(1f, 0.62f, 0.12f));
        main.gravityModifier = 1.4f;
        main.maxParticles = 48;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = spark.emission;
        emission.rateOverTime = 46f;

        var shape = spark.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 22f;
        shape.radius = 0.02f;

        var color = spark.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1f, 0.98f, 0.9f), 0f),
                new GradientColorKey(new Color(1f, 0.45f, 0.05f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(0f, 1f)
            });
        color.color = gradient;

        var size = spark.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 1f),
            new Keyframe(1f, 0.2f)));

        var renderer = sparkObject.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.lengthScale = 4f;
        renderer.velocityScale = 0.04f;
        renderer.material = GrindSparkMaterial();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return spark;
    }

    static Material grindSparkMaterial;

    static Material GrindSparkMaterial()
    {
        if (grindSparkMaterial != null)
            return grindSparkMaterial;

        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null)
            shader = Shader.Find("Particles/Standard Unlit");
        grindSparkMaterial = new Material(shader);
        if (grindSparkMaterial.HasProperty("_BaseColor"))
            grindSparkMaterial.SetColor("_BaseColor", Color.white);
        if (grindSparkMaterial.HasProperty("_Color"))
            grindSparkMaterial.SetColor("_Color", Color.white);
        return grindSparkMaterial;
    }

    void PoseSkateLeg(
        Transform thigh,
        Transform shin,
        Transform foot,
        Transform toe,
        Quaternion thighRest,
        Quaternion shinRest,
        Quaternion footRest,
        Vector3 across,
        Vector3 lead)
    {
        SetLocal(thigh, thighRest);
        SetLocal(shin, shinRest);
        SetLocal(foot, footRest);
        if (thigh == null || shin == null || foot == null)
            return;

        Vector3 thighDir = shin.position - thigh.position;
        AimBone(thigh, thighDir, Vector3.down + across * 0.55f + lead);

        Vector3 shinDir = foot.position - shin.position;
        AimBone(shin, shinDir, Vector3.down - across * 0.28f);

        if (toe == null)
            return;

        Vector3 toeDir = toe.position - foot.position;
        AimBone(foot, toeDir, across);
    }

    static void AimBone(Transform bone, Vector3 from, Vector3 to)
    {
        if (bone == null)
            return;
        if (from.sqrMagnitude < 0.0001f || to.sqrMagnitude < 0.0001f)
            return;

        from.Normalize();
        to.Normalize();
        float dot = Vector3.Dot(from, to);
        if (dot > 0.9999f)
            return;

        // FromToRotation logs an error when the two directions are opposite.
        if (dot < -0.9999f)
        {
            Vector3 axis = Vector3.Cross(from, Vector3.up);
            if (axis.sqrMagnitude < 0.0001f)
                axis = Vector3.Cross(from, Vector3.forward);
            bone.rotation = Quaternion.AngleAxis(180f, axis.normalized) * bone.rotation;
            return;
        }

        bone.rotation = Quaternion.FromToRotation(from, to) * bone.rotation;
    }

    void EnsureBones()
    {
        if (body == null)
            return;

        if (upperArmL == null)
            upperArmL = Find(body, "Arm.L");
        if (upperArmL == null)
            upperArmL = Find(body, "UpperArm_L");
        if (forearmL == null)
            forearmL = Find(body, "Arm.L.002");
        if (forearmL == null)
            forearmL = Find(body, "Forearm_L");
        if (handL == null)
            handL = Find(body, "Hand.L");
        if (upperArmR == null)
            upperArmR = Find(body, "Arm.R");
        if (upperArmR == null)
            upperArmR = Find(body, "UpperArm_R");
        if (forearmR == null)
            forearmR = Find(body, "Arm.R.002");
        if (forearmR == null)
            forearmR = Find(body, "Forearm_R");
        if (handR == null)
            handR = Find(body, "Hand.R");
        if (neckBone == null)
            neckBone = Find(body, "Neck");
        if (headBone == null)
            headBone = Find(body, "Head");
        if (thighL == null)
            thighL = Find(body, "Thigh.L");
        if (shinL == null)
            shinL = Find(body, "Shin.L");
        if (footL == null)
            footL = Find(body, "Foot.L");
        if (toeL == null && footL != null)
            toeL = Find(footL, "Foot.L.Tip");
        if (thighR == null)
            thighR = Find(body, "Thigh.R");
        if (shinR == null)
            shinR = Find(body, "Shin.R");
        if (footR == null)
            footR = Find(body, "Foot.R");
        if (toeR == null && footR != null)
            toeR = Find(footR, "Foot.R.Tip");
        if (Find(body, "Arm.L") != null)
            scaleWholeBody = true;
        if (chainL == null)
            chainL = BuildChain(upperArmL, handL);
        if (chainR == null)
            chainR = BuildChain(upperArmR, handR);
    }

    static Transform[] BuildChain(Transform upper, Transform hand)
    {
        if (upper == null || hand == null)
            return null;

        var links = new System.Collections.Generic.List<Transform>();
        Transform bone = hand;
        while (bone != null)
        {
            links.Add(bone);
            if (bone == upper)
                break;
            bone = bone.parent;
        }

        if (links.Count < 2 || links[links.Count - 1] != upper)
            return null;

        links.Reverse();
        return links.ToArray();
    }

    void SolveArmChain(Transform[] chain, Transform upper, Transform forearm, Transform hand, Transform target, float side)
    {
        if (upper != null && forearm != null && hand != null)
        {
            SolveArm(upper, forearm, hand, target, side);
            return;
        }

        if (chain == null || chain.Length < 2)
        {
            SolveArm(upper, forearm, hand, target, side);
            return;
        }

        if (target == null || hand == null)
            return;

        Vector3 wrist = WristPoint(target);
        StretchArm(upper, forearm, hand, wrist);
        for (int iteration = 0; iteration < 8; iteration++)
        {
            for (int i = chain.Length - 2; i >= 0; i--)
                PointAt(chain[i], hand, wrist);
        }

        PlaceHandOnGun(hand, target);
    }

    void FitWholeBodyToHead()
    {
        if (neck == null)
            neck = Find(body, "Head");
        if (neck == null || head == null)
            return;

        body.localScale = Vector3.one;
        float current = neck.position.y - body.position.y;
        float target = head.position.y - body.position.y;
        float scale = current > 0.05f ? Mathf.Clamp(target / current, 0.35f, 1.75f) : 1f;
        body.localScale = Vector3.one * scale;

        Vector3 look = Vector3.ProjectOnPlane(head.forward, Vector3.up);
        Vector3 desired = head.position;
        if (look.sqrMagnitude > 0.0001f)
            desired -= look.normalized * bodyBackOffset;
        Vector3 shift = desired - neck.position;
        shift.y = 0f;
        body.position += shift;
    }

    void FitLegsToHead()
    {
        if (hips == null)
            BindLegs();
        if (hips == null || neck == null || thighL == null || thighR == null)
            return;

        hips.localPosition = restHipsLocal;
        SetAxisScale(thighL, thighAxisL, 1f);
        SetAxisScale(thighR, thighAxisR, 1f);

        if (!posed)
        {
            restNeckAboveHip = neck.position.y - hips.position.y;
            restFootOffset = (footL != null ? footL.position.y : hips.position.y - 0.8f) - hips.position.y;
            thighAxisL = LengthAxis(thighL, footL != null ? footL : thighL);
            thighAxisR = LengthAxis(thighR, footR != null ? footR : thighR);
            posed = Mathf.Abs(restNeckAboveHip) > 0.05f && restFootOffset < -0.05f;
            if (!posed)
                return;
        }

        float groundY = body.position.y;
        float targetHipY = head.position.y - restNeckAboveHip;
        targetHipY = Mathf.Clamp(targetHipY, groundY + 0.25f, groundY + 2.1f);

        Vector3 hip = hips.position;
        hip.y = targetHipY;
        hips.position = hip;

        float scale = (groundY - targetHipY) / restFootOffset;
        scale = Mathf.Clamp(scale, 0.35f, 1.75f);
        SetAxisScale(thighL, thighAxisL, scale);
        SetAxisScale(thighR, thighAxisR, scale);
    }

    void BindLegs()
    {
        if (body == null)
            return;

        hips = Find(body, "Hips");
        neck = Find(body, "Head");
        if (neck == null)
            neck = Find(body, "Neck");
        if (neck == null)
            neck = Find(body, "Chest");
        HideHeadFromInside();
        thighL = Find(body, "Thigh_L");
        thighR = Find(body, "Thigh_R");
        footL = Find(body, "Foot_L");
        footR = Find(body, "Foot_R");
        if (hips != null)
            restHipsLocal = hips.localPosition;
    }

    static int LengthAxis(Transform bone, Transform end)
    {
        Vector3 along = bone.InverseTransformVector(end.position - bone.position);
        float ax = Mathf.Abs(along.x);
        float ay = Mathf.Abs(along.y);
        float az = Mathf.Abs(along.z);
        if (ay >= ax && ay >= az)
            return 1;
        if (az >= ax)
            return 2;
        return 0;
    }

    static void SetAxisScale(Transform bone, int axis, float scale)
    {
        Vector3 size = Vector3.one;
        if (axis == 0)
            size.x = scale;
        else if (axis == 1)
            size.y = scale;
        else
            size.z = scale;
        bone.localScale = size;
    }

    void HideHeadFromInside()
    {
        Shader shader = Shader.Find("HD2/AvatarHead");
        if (shader == null || body == null)
            return;

        var material = new Material(shader);
        ApplyHeadShader(body, material);
    }

    static void ApplyHeadShader(Transform root, Material material)
    {
        if (root.name == "Head" || root.name == "Neck")
        {
            var renderer = root.GetComponent<Renderer>();
            if (renderer != null)
                renderer.sharedMaterial = material;
        }

        for (int i = 0; i < root.childCount; i++)
            ApplyHeadShader(root.GetChild(i), material);
    }

    static Transform Find(Transform root, string name)
    {
        if (root.name == name)
            return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = Find(root.GetChild(i), name);
            if (found != null)
                return found;
        }

        return null;
    }

    void SolveArm(Transform upper, Transform forearm, Transform hand, Transform target, float side)
    {
        if (upper == null || forearm == null || hand == null || target == null || body == null)
            return;

        Vector3 wrist = GripAtForearmEnd(forearm, hand, GripPoint(target));
        StretchArm(upper, forearm, hand, wrist);
        float upperLength = Vector3.Distance(upper.position, forearm.position);
        float foreLength = Vector3.Distance(forearm.position, hand.position);
        if (upperLength < 0.02f || foreLength < 0.02f)
            return;

        EnsureMedial();

        Vector3 shoulder = upper.position;
        Vector3 toTarget = wrist - shoulder;
        float reach = upperLength + foreLength - 0.01f;
        float distance = Mathf.Clamp(toTarget.magnitude, 0.05f, reach);
        Vector3 direction = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : body.forward;
        Vector3 aim = direction * distance;

        Vector3 pole = shoulder + body.right * side + body.forward * -0.35f + Vector3.up * -0.15f;
        Vector3 bend = Vector3.Cross(aim, pole - shoulder);
        if (bend.sqrMagnitude < 0.0001f)
            bend = Vector3.Cross(aim, Vector3.up);
        Vector3 poleDirection = Vector3.Cross(bend, aim).normalized;

        float cosShoulder = Mathf.Clamp(
            (upperLength * upperLength + distance * distance - foreLength * foreLength) / (2f * upperLength * distance),
            -1f,
            1f);
        float sinShoulder = Mathf.Sqrt(Mathf.Max(0f, 1f - cosShoulder * cosShoulder));
        Vector3 elbow = shoulder + direction * (upperLength * cosShoulder) + poleDirection * (upperLength * sinShoulder);
        if (Vector3.Dot(elbow - shoulder, pole - shoulder) < 0f)
            elbow = shoulder + direction * (upperLength * cosShoulder) - poleDirection * (upperLength * sinShoulder);
        elbow = KeepElbowOnOwnSide(shoulder, elbow, upperLength, side);
        float stopped = Vector3.Distance(shoulder, elbow);
        if (stopped < upperLength - 0.001f)
        {
            int upperAxis = LengthAxis(upper, forearm);
            SetAxisScale(upper, upperAxis, Mathf.Clamp(stopped / upperLength, 0.3f, 1f));
            int foreAxis = LengthAxis(forearm, hand);
            Vector3 foreScaleNow = forearm.localScale;
            float foreScale = foreAxis == 0 ? foreScaleNow.x : foreAxis == 1 ? foreScaleNow.y : foreScaleNow.z;
            float restFore = foreScale > 0.01f ? foreLength / foreScale : foreLength;
            float needed = Vector3.Distance(elbow, wrist);
            float maxScale = Mathf.Max(Mathf.Clamp(armReachScale, 0.2f, 1f), forearmMaxStretch);
            SetAxisScale(forearm, foreAxis, Mathf.Clamp(needed / Mathf.Max(restFore, 0.02f), 0.35f, maxScale));
        }

        PointAt(upper, forearm, elbow);
        SeatAgainstBody(upper, forearm, upper == upperArmL);
        PointAt(forearm, hand, wrist);
        KeepForearmStraight(hand);
        PlaceHandOnGun(hand, target);
    }

    // Upper arms may meet in front of the chest, but neither elbow may cross
    // the center line. The forearm bends in to cover whatever is left.
    Vector3 KeepElbowOnOwnSide(Vector3 shoulder, Vector3 elbow, float upperLength, float side)
    {
        Vector3 across = body.right;
        float pastCenter = Vector3.Dot(elbow - body.position, across);
        bool crossed = side < 0f ? pastCenter > 0f : pastCenter < 0f;
        if (!crossed)
            return elbow;

        Vector3 parked = elbow - across * pastCenter;
        Vector3 dir = parked - shoulder;
        if (dir.sqrMagnitude < 0.0001f)
            dir = Vector3.ProjectOnPlane(body.forward, across);
        if (dir.sqrMagnitude < 0.0001f)
            return elbow;
        // The bone is a fixed length, so aiming at the plane still swings the
        // elbow through it. Stop the elbow on the plane instead.
        float stop = Mathf.Min(upperLength, dir.magnitude);
        return shoulder + dir.normalized * stop;
    }

    Vector3 medialUpperL;
    Vector3 medialUpperR;
    bool medialReady;

    void EnsureMedial()
    {
        if (medialReady || upperArmL == null || forearmL == null)
            return;
        if (upperArmR == null || forearmR == null)
            return;

        CaptureMedial(upperArmL, forearmL, ShoulderInward(true), ref medialUpperL);
        CaptureMedial(upperArmR, forearmR, ShoulderInward(false), ref medialUpperR);
        medialReady = true;
    }

    // Horizontal, toward the chest. The player root is at the feet, so a
    // direction to body.position points down and rolls the shoulder cap.
    Vector3 ShoulderInward(bool left)
    {
        float side = left ? -1f : 1f;
        Vector3 inward = body.right * -side;
        inward.y = 0f;
        if (inward.sqrMagnitude < 0.0001f)
            return Vector3.right;
        return inward.normalized;
    }

    void CaptureMedial(Transform bone, Transform child, Vector3 inward, ref Vector3 local)
    {
        Vector3 axis = child.position - bone.position;
        Vector3 toward = Vector3.ProjectOnPlane(inward, axis);
        if (axis.sqrMagnitude < 0.0001f || toward.sqrMagnitude < 0.0001f)
            return;
        local = Quaternion.Inverse(bone.rotation) * toward.normalized;
    }

    void SeatAgainstBody(Transform bone, Transform child, bool left)
    {
        Vector3 local = left ? medialUpperL : medialUpperR;
        if (local.sqrMagnitude < 0.0001f)
            return;

        Vector3 axis = child.position - bone.position;
        Vector3 desired = Vector3.ProjectOnPlane(ShoulderInward(left), axis);
        Vector3 current = Vector3.ProjectOnPlane(bone.rotation * local, axis);
        if (desired.sqrMagnitude < 0.0001f || current.sqrMagnitude < 0.0001f)
            return;

        bone.rotation = Quaternion.FromToRotation(current.normalized, desired.normalized) * bone.rotation;
    }

    void StretchArm(Transform upper, Transform forearm, Transform hand, Vector3 wrist)
    {
        if (upper == null || forearm == null || hand == null)
            return;

        upper.localScale = Vector3.one;
        forearm.localScale = Vector3.one;
        float upperLength = Vector3.Distance(upper.position, forearm.position);
        float foreLength = Vector3.Distance(forearm.position, hand.position);
        if (upperLength < 0.02f || foreLength < 0.02f)
            return;

        float reachScale = Mathf.Clamp(armReachScale, 0.2f, 1f);
        float gap = Vector3.Distance(upper.position, wrist);
        float maxScale = Mathf.Max(reachScale, forearmMaxStretch);
        // Leave the upper arm at its real length. Scaling it stretches the
        // shoulder into a wing. The forearm takes whatever extra reach is needed.
        float foreScale = Mathf.Clamp((gap - upperLength) / foreLength, 0.35f, maxScale);
        SetAxisScale(forearm, LengthAxis(forearm, hand), foreScale);
    }

    Quaternion restHandL = Quaternion.identity;
    Quaternion restHandR = Quaternion.identity;
    bool handRestReady;

    Vector3 GripPoint(Transform target)
    {
        Transform gun = GunInHand(target);
        if (gun != null && gun.GetComponent<Hd2RocketLauncher>() != null)
        {
            Transform rocketWrist = Find(gun, "Wrist");
            if (rocketWrist != null)
                return rocketWrist.position;
        }

        Transform pivot = target == rightHand ? gripPivotR : gripPivotL;
        if (pivot != null)
            return pivot.position;
        return WristPoint(target);
    }

    public Vector3 ForearmEndPosition(bool left)
    {
        Transform hand = left ? handL : handR;
        if (hand == null)
            return Vector3.zero;
        for (int i = 0; i < hand.childCount; i++)
        {
            Transform child = hand.GetChild(i);
            if (child.name.Contains("Tip"))
                return child.position;
        }
        return hand.position;
    }

    // Hand.L/R is the end of the forearm. The grip should meet that tip,
    // not the bone origin in the middle of the piece.
    Vector3 GripAtForearmEnd(Transform forearm, Transform hand, Vector3 grip)
    {
        Transform tip = null;
        for (int i = 0; i < hand.childCount; i++)
        {
            Transform child = hand.GetChild(i);
            if (child.name.Contains("Tip"))
                tip = child;
        }

        float extra = tip != null ? Vector3.Distance(hand.position, tip.position) : 0.06f;
        Vector3 along = hand.position - forearm.position;
        if (along.sqrMagnitude < 0.0001f)
            along = grip - forearm.position;
        if (along.sqrMagnitude < 0.0001f)
            return grip;
        return grip - along.normalized * extra;
    }

    void KeepForearmStraight(Transform hand)
    {
        if (hand == null || fingers.Count > 0)
            return;

        if (!handRestReady)
        {
            if (handL != null)
                restHandL = handL.localRotation;
            if (handR != null)
                restHandR = handR.localRotation;
            handRestReady = true;
        }

        hand.localRotation = hand == handR ? restHandR : restHandL;
    }

    void PlaceHandOnGun(Transform hand, Transform target)
    {
        Transform gun = GunInHand(target);
        bool pistol = gun != null && gun.GetComponent<Hd2Pistol>() != null;
        if (fingers.Count == 0)
        {
            if (pistol)
                PullGunTrigger(gun, ReadFinger(hand == handR ? rightTrigger : leftTrigger, true, hand == handR));
            else if (gun != null)
                PullGunTrigger(gun, ReadFinger(hand == handR ? rightTrigger : leftTrigger, true, hand == handR));
            return;
        }
        if (pistol)
            AimPistolGrip(hand, gun);
        else if (gun != null)
            AimHandAtGun(hand, target, gun);
        else
            hand.rotation = target.rotation * Quaternion.Euler(0f, 180f, 0f);

        bool right = hand == handR;
        float index = ReadFinger(right ? rightTrigger : leftTrigger, true, right);
        float others = ReadFinger(right ? rightGrip : leftGrip, false, right);
        if (pistol)
        {
            PullGunTrigger(gun, index);
            index = pistolIndexCurl;
            others = pistolGripCurl;
        }
        else if (gun != null)
        {
            PullGunTrigger(gun, index);
            index = armedIndexCurl;
            others = Mathf.Max(others, 1f);
        }

        CurlFingers(hand, index, others);
    }

    Vector3 WristPoint(Transform target)
    {
        Transform gun = GunInHand(target);
        if (gun == null)
            return target.position - target.forward * 0.08f;

        if (gun.GetComponent<Hd2Pistol>() != null)
            return PistolWrist(target, gun);

        if (gun.GetComponent<Hd2Smg>() != null)
            return SmgWrist(target, gun);

        if (gun.GetComponent<Hd2RocketLauncher>() != null)
        {
            Transform rocketWrist = Find(gun, "Wrist");
            if (rocketWrist != null)
                return rocketWrist.position;
            return SmgWrist(target, gun);
        }

        // The wrist bone is the root of the hand mesh. Sit it just behind the grip
        // so the fingers land on the gun instead of stopping short of it.
        Vector3 barrel = GunBarrel(gun);
        return gun.position - barrel * 0.07f;
    }

    Vector3 PistolWrist(Transform target, Transform gun)
    {
        // The hand on the gun ends in a flat cut. That face sits at this local
        // point, with its normal straight out the back of the hand. The right
        // gun is the left one mirrored on X. Local Z is up across that face.
        float side = target == rightHand ? -1f : 1f;
        Vector3 wrist = gun.TransformPoint(new Vector3(0.014f * side, 0.101f, 0.048f));
        Vector3 up = gun.TransformDirection(new Vector3(0f, 0f, 1f));
        return wrist + up * 0.04f;
    }

    Vector3 SmgWrist(Transform target, Transform gun)
    {
        // The flat cut is the low end of this model. The barrel is the other way.
        float side = target == rightHand ? -1f : 1f;
        Vector3 wrist = gun.TransformPoint(new Vector3(-0.012f * side, -0.166f, 0.053f));
        Vector3 up = gun.TransformDirection(new Vector3(0f, 0f, 1f));
        return wrist + up * 0.04f;
    }

    void AimPistolGrip(Transform hand, Transform gun)
    {
        if (TryPistolGrip(hand, gun, out _, out _, out Quaternion rotation, out _, out _))
            hand.rotation = rotation;
    }

    bool TryPistolGrip(
        Transform hand,
        Transform gun,
        out Vector3 finger,
        out Vector3 palm,
        out Quaternion rotation,
        out Vector3 up,
        out Vector3 barrel)
    {
        finger = Vector3.forward;
        palm = Vector3.up;
        rotation = Quaternion.identity;
        up = Vector3.up;
        barrel = Vector3.forward;
        if (hand == null || gun == null)
            return false;
        if (!lengthAxis.TryGetValue(hand, out Vector3 length) || length.sqrMagnitude < 0.0001f)
            return false;
        if (!indexSpread.TryGetValue(hand, out Vector3 spread) || spread.sqrMagnitude < 0.0001f)
            return false;
        if (!palmAxis.TryGetValue(hand, out Vector3 palmLocal) || palmLocal.sqrMagnitude < 0.0001f)
            return false;

        barrel = GunBarrel(gun);
        up = Vector3.ProjectOnPlane(gun.up, barrel);
        if (up.sqrMagnitude < 0.0001f)
            up = Vector3.ProjectOnPlane(Vector3.up, barrel);
        if (up.sqrMagnitude < 0.0001f || barrel.sqrMagnitude < 0.0001f)
            return false;

        up.Normalize();
        Vector3 right = Vector3.Cross(up, barrel).normalized;
        float side = hand == handR ? 1f : -1f;
        Vector3 fingerWant = (up + barrel * 0.55f).normalized;
        Vector3 indexWant = right * side;
        rotation = MapAxes(length, spread, fingerWant, indexWant);
        finger = rotation * length.normalized;
        palm = rotation * palmLocal.normalized;
        return finger.sqrMagnitude > 0.0001f;
    }

    static Quaternion MapAxes(Vector3 fromPrimary, Vector3 fromSecondary, Vector3 toPrimary, Vector3 toSecondary)
    {
        Vector3 from = fromPrimary.normalized;
        Vector3 fromSide = Vector3.ProjectOnPlane(fromSecondary, from).normalized;
        Vector3 to = toPrimary.normalized;
        Vector3 toSide = Vector3.ProjectOnPlane(toSecondary, to).normalized;
        if (fromSide.sqrMagnitude < 0.0001f || toSide.sqrMagnitude < 0.0001f)
            return Quaternion.FromToRotation(from, to);

        Quaternion swing = Quaternion.FromToRotation(from, to);
        Quaternion roll = Quaternion.FromToRotation(swing * fromSide, toSide);
        return roll * swing;
    }

    void AimHandAtGun(Transform hand, Transform target, Transform gun)
    {
        hand.rotation = target.rotation * Quaternion.Euler(0f, 180f, 0f);
        if (!fingerAxis.TryGetValue(hand, out Vector3 fingers) || fingers.sqrMagnitude < 0.0001f)
            return;

        Vector3 barrel = GunBarrel(gun);
        Vector3 current = hand.TransformDirection(fingers);
        if (current.sqrMagnitude < 0.0001f || barrel.sqrMagnitude < 0.0001f)
            return;

        hand.rotation = Quaternion.FromToRotation(current, barrel) * hand.rotation;
        if (!palmAxis.TryGetValue(hand, out Vector3 palm) || palm.sqrMagnitude < 0.0001f)
            return;

        Vector3 palmNow = Vector3.ProjectOnPlane(hand.TransformDirection(palm), barrel);
        Vector3 palmWant = Vector3.ProjectOnPlane(target.up, barrel);
        if (palmNow.sqrMagnitude < 0.0001f || palmWant.sqrMagnitude < 0.0001f)
            return;

        hand.rotation = Quaternion.FromToRotation(palmNow, palmWant) * hand.rotation;
    }

    static Vector3 GunBarrel(Transform gun)
    {
        Transform muzzle = Find(gun, "Muzzle");
        if (muzzle == null)
            return gun.forward;

        Vector3 along = muzzle.position - gun.position;
        if (along.sqrMagnitude > 0.0004f)
            return along.normalized;
        return muzzle.forward;
    }

    static Transform GunInHand(Transform controller)
    {
        if (controller == null)
            return null;
        var pistol = controller.GetComponentInChildren<Hd2Pistol>(true);
        if (pistol != null)
            return pistol.transform;
        var smg = controller.GetComponentInChildren<Hd2Smg>(true);
        if (smg != null)
            return smg.transform;
        var rocket = controller.GetComponentInChildren<Hd2RocketLauncher>(true);
        if (rocket != null)
            return rocket.transform;
        return null;
    }

    void PullGunTrigger(Transform gun, float amount)
    {
        Transform trigger = FindTrigger(gun);
        if (trigger == null)
            return;
        if (!triggerRest.TryGetValue(trigger, out Quaternion rest))
        {
            rest = trigger.localRotation;
            triggerRest[trigger] = rest;
        }

        var launcher = gun.GetComponent<Hd2RocketLauncher>();
        if (launcher != null)
        {
            if (!triggerRestPos.TryGetValue(trigger, out Vector3 restPos))
            {
                restPos = trigger.localPosition;
                triggerRestPos[trigger] = restPos;
            }

            Vector3 back = -launcher.Barrel();
            Vector3 localBack = trigger.parent != null
                ? trigger.parent.InverseTransformVector(back.normalized * (0.015f * amount))
                : back.normalized * (0.015f * amount);
            trigger.localRotation = rest;
            trigger.localPosition = restPos + localBack;
            return;
        }

        trigger.localRotation = rest * Quaternion.Euler(gunTriggerDegrees * amount, 0f, 0f);
    }

    static Transform FindTrigger(Transform gun)
    {
        Transform mesh = null;
        Transform[] all = gun.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            if (!string.Equals(all[i].name, "trigger", System.StringComparison.OrdinalIgnoreCase))
                continue;

            bool visual = all[i].GetComponent<Renderer>() != null || all[i].GetComponent<MeshFilter>() != null;
            if (!visual)
                return all[i];
            if (mesh == null)
                mesh = all[i];
        }

        return mesh;
    }

    void CacheFingers()
    {
        if (handL == null && handR == null)
            return;

        fingers.Clear();
        fingerRest.Clear();
        fingerHinge.Clear();
        lengthAxis.Clear();
        indexSpread.Clear();
        CollectFingers(handL);
        CollectFingers(handR);
        if (fingers.Count == 0)
        {
            CacheSolidHand(handL);
            CacheSolidHand(handR);
        }

        fingersCached = true;
    }

    void CacheSolidHand(Transform hand)
    {
        if (hand == null)
            return;

        Transform tip = null;
        for (int i = 0; i < hand.childCount; i++)
        {
            Transform child = hand.GetChild(i);
            if (child.name.Contains("Tip"))
                tip = child;
        }

        if (tip == null)
            return;

        Vector3 length = tip.position - hand.position;
        if (length.sqrMagnitude < 0.0001f)
            return;

        Vector3 towardBody = Vector3.ProjectOnPlane(body.position - hand.position, length);
        if (towardBody.sqrMagnitude < 0.0001f)
            towardBody = Vector3.down;
        Vector3 side = Vector3.Cross(length, towardBody);
        lengthAxis[hand] = hand.InverseTransformDirection(length.normalized);
        fingerAxis[hand] = lengthAxis[hand];
        palmAxis[hand] = hand.InverseTransformDirection(towardBody.normalized);
        if (side.sqrMagnitude > 0.0001f)
            indexSpread[hand] = hand.InverseTransformDirection(side.normalized);
    }

    void CollectFingers(Transform hand)
    {
        if (hand == null)
            return;

        string side = hand.name.EndsWith("R") ? ".R" : ".L";
        string[] names = { "Index", "Middle", "Ring", "Pinky" };
        for (int i = 0; i < names.Length; i++)
        {
            Transform bone = Find(hand, names[i] + side);
            if (bone == null)
                continue;
            Transform mid = Find(bone, names[i] + side + ".001");
            if (mid == null)
                continue;
            Transform tip = Find(mid, names[i] + side + ".Tip");
            if (tip == null)
                continue;

            RememberHinge(hand, bone, mid);
            RememberHinge(hand, mid, tip);
            if (!fingerAxis.ContainsKey(hand))
            {
                fingerAxis[hand] = hand.InverseTransformDirection(bone.position - hand.position).normalized;
                palmAxis[hand] = hand.InverseTransformDirection(Vector3.down);
            }

            if (names[i] == "Index")
                lengthAxis[hand] = hand.InverseTransformDirection(mid.position - bone.position).normalized;
            if (names[i] == "Middle" && lengthAxis.ContainsKey(hand))
            {
                Transform indexBone = Find(hand, "Index" + side);
                if (indexBone != null)
                    indexSpread[hand] = hand.InverseTransformDirection(indexBone.position - bone.position).normalized;
            }
            fingers.Add(new Finger
            {
                hand = hand,
                bone = bone,
                mid = mid,
                tip = tip,
                index = names[i] == "Index"
            });
        }
    }

    void RememberHinge(Transform hand, Transform bone, Transform child)
    {
        fingerRest[bone] = bone.localRotation;
        Vector3 dir = child.position - bone.position;
        Vector3 axis = Vector3.Cross(dir, Vector3.down);
        if (axis.sqrMagnitude < 0.0001f)
            axis = Vector3.Cross(dir, Vector3.forward);
        fingerHinge[bone] = hand.InverseTransformDirection(axis.normalized);
    }

    void CurlFingers(Transform hand, float indexAmount, float otherAmount)
    {
        for (int i = 0; i < fingers.Count; i++)
        {
            Finger finger = fingers[i];
            if (finger.hand != hand)
                continue;

            float amount = finger.index ? indexAmount : otherAmount;
            float degrees = fingerCurlDegrees * amount;
            Bend(finger.bone, finger.mid, hand, degrees);
            Bend(finger.mid, finger.tip, hand, degrees);
        }
    }

    void Bend(Transform bone, Transform child, Transform hand, float degrees)
    {
        if (!fingerRest.TryGetValue(bone, out Quaternion rest))
            return;
        if (!fingerHinge.TryGetValue(bone, out Vector3 hingeLocal))
            return;

        bone.localRotation = rest;
        Vector3 dir = child.position - bone.position;
        if (dir.sqrMagnitude < 0.0000001f)
            return;

        Vector3 axis = hand.TransformDirection(hingeLocal);
        if (axis.sqrMagnitude < 0.0000001f)
            return;

        Vector3 bent = Quaternion.AngleAxis(degrees, axis.normalized) * dir.normalized;
        bone.rotation = Quaternion.FromToRotation(dir.normalized, bent) * bone.rotation;
    }

    float ReadFinger(InputAction action, bool index, bool rightHand)
    {
        float value = action != null ? action.ReadValue<float>() : 0f;
        if (rightHand && EditorFallback)
        {
            if (index && Mouse.current != null && Mouse.current.rightButton.isPressed)
                value = 1f;
            if (!index && Keyboard.current != null && Keyboard.current.gKey.isPressed)
                value = 1f;
        }

        return Mathf.Clamp01(value);
    }

    static bool EditorFallback
    {
        get
        {
            if (!Application.isEditor)
                return false;
            displays.Clear();
            SubsystemManager.GetSubsystems(displays);
            for (int i = 0; i < displays.Count; i++)
            {
                if (displays[i].running)
                    return false;
            }

            return true;
        }
    }

    static void Dispose(ref InputAction action)
    {
        if (action == null)
            return;
        action.Disable();
        action.Dispose();
        action = null;
    }

    struct Finger
    {
        public Transform hand;
        public Transform bone;
        public Transform mid;
        public Transform tip;
        public bool index;
    }

    static void PointAt(Transform bone, Transform child, Vector3 worldPoint)
    {
        Vector3 current = child.position - bone.position;
        Vector3 desired = worldPoint - bone.position;
        if (current.sqrMagnitude < 0.0000001f || desired.sqrMagnitude < 0.0000001f)
            return;

        bone.rotation = Quaternion.FromToRotation(current, desired) * bone.rotation;
    }
}
