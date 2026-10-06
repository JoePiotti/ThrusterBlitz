using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

/// <summary>
/// Moves the VR player root (the tracking origin). Never writes the camera local pose.
/// CameraOffset local Y stays 0.
///
/// Quest controls:
/// - Left stick: walk, head-yaw relative (stick up = head forward on the floor plane).
/// - Right stick left/right: snap turn around the headset. One snap per deflection; the stick
///   must return near center before the next snap. Holding the stick does not spin.
/// - Sprint: left stick click held, and only while there is move input. Stick magnitude does not sprint.
/// - Thrust: hold left primary (X) or right primary (A). An arc is drawn from that hand. If the
///   arc ends in the air or on a wall, a straight line drops to the surface below and the marker
///   sits there. The arc stops
///   at the first solid hit. Release moves the root toward that point at
///   thrustSpeed, phasing through geometry. No hit, a hit past thrustMaxDistance, or a hit on
///   Hd2NoLanding does nothing.
///   The local player's aim arc ignores Hd2SpawnField and continues to a landing beyond it.
///   The shield still stops shots from outside and is meant to block enemies, not this player.
///   Arc color: the rail's own color on a grind rail, green on other legal landings, red when the landing
///   is illegal or there is no valid hit. The arc stops at thrustMaxDistance instead of turning red
///   for being too far. With less than one thrust charge the lines keep that color,
///   and the robot preview is hidden.
/// - Grind: only by thrusting onto an Hd2GrindRail. Stay on until the rail ends (then gravity)
///   or until the next thrust starts. Walking onto a rail steps up onto it or over it.
///   Grip is not grind.
/// - Jump: hold B. Smoke vents from the two backpack nozzles and the player rises,
///   up to 1.5 meters, for as long as the button is held, thrust remains, and 1.5 seconds
///   have not passed. Thrust is spent at 1 bar per second. Releasing B, running out of
///   thrust, or reaching the cap ends the rise. Touching the ground clears the jump
///   so it can be used again. It is not required to start one, including while falling
///   or grinding. A second jump in the air does not start.
///   Walk, sprint, and a blitz still work in the air and on a rail. There is no double jump.
///
/// Editor fallback when no headset is running: WASD / arrows walk, Left Shift sprint,
/// Q snap left, E snap right (one snap per key press),
/// hold Space or left mouse to aim an arc along the view, release to thrust.
/// C is the jump.
/// Mouse look turns the player root so the view can aim. It does not run while a headset is active.
/// </summary>
[DefaultExecutionOrder(100)]
public class Hd2Locomotion : MonoBehaviour
{
    enum AimSource
    {
        None,
        Left,
        Right,
        View
    }

    enum AimKind
    {
        None,
        Rail,
        Legal,
        Illegal
    }

    [Header("References")]
    [Tooltip("Head used for yaw. Locomotion reads this. It does not write the camera local pose.")]
    public Transform head;
    [Tooltip("Left controller pose. X aims the thrust arc from here.")]
    public Transform leftHand;
    [Tooltip("Right controller pose. A aims the thrust arc from here.")]
    public Transform rightHand;

    [Header("Walk / Sprint")]
    public float walkSpeed = 2.5f;
    public float sprintSpeed = 5.5f;
    public float stickDeadzone = 0.15f;

    [Header("Snap Turn")]
    [Tooltip("Yaw in degrees for one right-stick snap. Positive is right.")]
    public float snapAngle = 45f;

    [Header("Thrust")]
    [Tooltip("Straight-line travel speed in meters per second. Time is distance divided by this speed.")]
    public float thrustSpeed = 30f;
    public float thrustMaxDistance = 13f;
    [Tooltip("Extra height at the middle of a thrust. A level jump rises by this much, then comes back down.")]
    public float thrustHopHeight = 0.5f;
    [Tooltip("Initial speed of the aim arc, meters per second.")]
    public float arcSpeed = 16f;
    [Tooltip("Downward acceleration of the aim arc. The arc stops at the first solid hit.")]
    public float arcGravity = 34f;
    [Tooltip("Offset of the thrust arc from the gun aim. -65 lifts the arc slightly above the barrel.")]
    public float arcLaunchAngle = -65f;

    [Header("Grind")]
    public float grindSpeed = 8f;

    [Header("Jump")]
    [Tooltip("Highest the B-button jump lifts the feet, in meters.")]
    public float jumpHeight = 1.5f;
    [Tooltip("Longest the jump can rise, in seconds. Thrust is spent at jumpThrustPerSecond for this whole rise.")]
    public float jumpMaxTime = 1.5f;
    [Tooltip("Thrust bars spent per second while rising.")]
    public float jumpThrustPerSecond = 1f;

    [Header("Team")]
    [Tooltip("Blitz sparkle color. Use red or blue for a team. Gold is the demo default.")]
    public Color teamColor = new Color(1f, 0.76f, 0.22f, 1f);

    [Header("Body")]
    public float bodyRadius = 0.25f;
    public float bodyHeight = 1.7f;
    public float gravity = 15f;
    public float terminalVelocity = 25f;
    [Tooltip("Seconds of uninterrupted falling before the player dies.")]
    public float fallDeathSeconds = 5f;
    public float skinWidth = 0.05f;
    public float groundProbe = 0.3f;
    [Tooltip("Floor changes this tall or shorter are walked over. Taller faces still stop the player.")]
    public float stepHeight = 0.2f;

    const int ArcPointCapacity = 64;
    const float SnapEngage = 0.6f;
    const float SnapRearm = 0.3f;

    static readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();

    InputAction moveAction;
    InputAction turnAction;
    InputAction sprintAction;
    InputAction leftThrustAction;
    InputAction rightThrustAction;
    InputAction viewThrustAction;
    InputAction jumpAction;

    float verticalVelocity;
    float fallTime;
    bool snapArmed = true;
    float editorYaw;
    float editorPitch = -18f;
    bool editorTiltApplied;
    bool editorCursorReady;

    bool thrusting;
    float thrustElapsed;
    Vector3 thrustStart;
    Vector3 thrustEnd;
    Hd2GrindRail thrustRail;
    float thrustGrindSign = 1f;

    bool grinding;
    Hd2GrindRail grindRail;

    bool jumpBoosting;
    bool jumpSpent;
    float jumpOriginY;
    float jumpTime;
    ParticleSystem[] jumpExhaust;
    Vector3[] jumpNozzles;
    bool jumpNozzlesReady;
    AudioSource jumpThrustSource;
    float jumpThrustFade;
    const float JumpThrustFadeTime = 0.75f;

    public void HaltTravel()
    {
        thrusting = false;
        grinding = false;
        grindRail = null;
        thrustRail = null;
        verticalVelocity = 0f;
        fallTime = 0f;
        wasOffGround = false;
        EndJumpBoost();
        StopBlitzTrail();
        jumpSpent = false;
    }
    float grindSign = 1f;
    float grindDistance;

    AimSource aimSource = AimSource.None;
    AimKind aimKind = AimKind.None;
    bool aimValid;
    bool aimShowOutline;
    Vector3 aimLanding;
    Hd2GrindRail aimRail;
    float aimGrindSign = 1f;
    readonly Vector3[] arcPoints = new Vector3[ArcPointCapacity];
    int arcPointCount;
    static readonly Collider[] railOverlap = new Collider[16];
    static readonly RaycastHit[] bodyHits = new RaycastHit[24];

    LineRenderer arcLine;
    LineRenderer[] wireLines;
    MeshFilter[] wireParts;
    readonly Vector3[] wireCorners = new Vector3[8];
    GameObject thrustGhost;
    Material wireMaterial;
    AudioSource grindLoopSource;
    AudioSource walkLoopSource;
    AudioSource landSource;
    AudioSource blitzSource;
    ParticleSystem blitzTrail;
    bool wasOffGround;
    Material previewMaterial;

    static readonly int[] boxEdges =
    {
        0, 1, 1, 2, 2, 3, 3, 0,
        4, 5, 5, 6, 6, 7, 7, 4,
        0, 4, 1, 5, 2, 6, 3, 7
    };

    static readonly Color legalArcColor = new Color(0.2f, 0.92f, 0.28f, 1f);
    static readonly Color illegalArcColor = new Color(0.95f, 0.12f, 0.1f, 1f);

    public bool IsGrinding => grinding;
    public Vector3 PlanarVelocity { get; private set; }

    public Vector3 GrindDirection
    {
        get
        {
            if (!grinding || grindRail == null)
                return Vector3.zero;
            Vector3 tangent = grindRail.PlanarTangent(grindDistance) * grindSign;
            tangent.y = 0f;
            return tangent.sqrMagnitude > 0.0001f ? tangent.normalized : Vector3.zero;
        }
    }
    public bool IsThrusting => thrusting;
    public bool IsAirborne => jumpBoosting || jumpSpent;

    public bool IsSprinting
    {
        get
        {
            if (sprintAction == null || !sprintAction.IsPressed())
                return false;
            return ApplyDeadzone(ReadMove(), stickDeadzone).sqrMagnitude > 0.0001f;
        }
    }

    void Awake()
    {
        if (head == null)
        {
            var camera = GetComponentInChildren<Camera>();
            if (camera != null)
                head = camera.transform;
        }

        if (leftHand == null)
            leftHand = transform.Find("LeftHand");
        if (rightHand == null)
            rightHand = transform.Find("RightHand");

        CreatePreview();
        CreateGrindLoop();
        CreateWalkLoop();
        CreateLandSound();
        CreateBlitzSound();
        CreateJumpThrust();
    }

    void CreateGrindLoop()
    {
        var clip = Resources.Load<AudioClip>("Audio/RailGrindLoop");
        if (clip == null)
            return;

        grindLoopSource = gameObject.AddComponent<AudioSource>();
        grindLoopSource.clip = clip;
        grindLoopSource.loop = true;
        grindLoopSource.playOnAwake = false;
        grindLoopSource.spatialBlend = 1f;
        grindLoopSource.dopplerLevel = 0f;
        grindLoopSource.minDistance = 2f;
        grindLoopSource.maxDistance = 14f;
        grindLoopSource.rolloffMode = AudioRolloffMode.Linear;
    }

    void CreateWalkLoop()
    {
        var clip = Resources.Load<AudioClip>("Audio/RobotWalk");
        if (clip == null)
            return;

        walkLoopSource = gameObject.AddComponent<AudioSource>();
        walkLoopSource.clip = clip;
        walkLoopSource.loop = true;
        walkLoopSource.playOnAwake = false;
        walkLoopSource.volume = 0.32f;
        walkLoopSource.spatialBlend = 1f;
        walkLoopSource.dopplerLevel = 0f;
        walkLoopSource.minDistance = 2f;
        walkLoopSource.maxDistance = 14f;
        walkLoopSource.rolloffMode = AudioRolloffMode.Linear;
    }

    void CreateLandSound()
    {
        var clip = Resources.Load<AudioClip>("Audio/RobotLand");
        if (clip == null)
            return;

        landSource = gameObject.AddComponent<AudioSource>();
        landSource.clip = clip;
        landSource.loop = false;
        landSource.playOnAwake = false;
        landSource.spatialBlend = 1f;
        landSource.dopplerLevel = 0f;
        landSource.minDistance = 2f;
        landSource.maxDistance = 14f;
        landSource.rolloffMode = AudioRolloffMode.Linear;
    }

    void CreateBlitzSound()
    {
        var clip = Resources.Load<AudioClip>("Audio/RobotBlitz");
        if (clip == null)
            return;

        blitzSource = gameObject.AddComponent<AudioSource>();
        blitzSource.clip = clip;
        blitzSource.loop = false;
        blitzSource.playOnAwake = false;
        blitzSource.spatialBlend = 1f;
        blitzSource.dopplerLevel = 0f;
        blitzSource.minDistance = 2f;
        blitzSource.maxDistance = 14f;
        blitzSource.rolloffMode = AudioRolloffMode.Linear;
    }

    void NoteLanding()
    {
        if (thrusting)
            return;

        bool planted = FeetPlanted();
        if (!planted)
        {
            wasOffGround = true;
            return;
        }

        if (!wasOffGround)
            return;

        wasOffGround = false;
        if (landSource != null && landSource.clip != null)
            landSource.PlayOneShot(landSource.clip, 0.85f);
    }

    void UpdateWalkAudio()
    {
        if (walkLoopSource == null)
            return;

        bool stepping = !thrusting && !grinding && !IsAirborne && FeetPlanted() && PlanarVelocity.sqrMagnitude > 0.04f;
        if (!stepping)
        {
            if (walkLoopSource.isPlaying)
                walkLoopSource.Stop();
            return;
        }

        walkLoopSource.pitch = IsSprinting ? 1.55f : 1f;
        walkLoopSource.volume = 0.32f;
        if (!walkLoopSource.isPlaying)
            walkLoopSource.Play();
    }

    void CreateJumpThrust()
    {
        var clip = Resources.Load<AudioClip>("Audio/JumpThrust");
        if (clip == null)
            return;

        jumpThrustSource = gameObject.AddComponent<AudioSource>();
        jumpThrustSource.clip = clip;
        jumpThrustSource.loop = true;
        jumpThrustSource.playOnAwake = false;
        jumpThrustSource.spatialBlend = 1f;
        jumpThrustSource.dopplerLevel = 0f;
        jumpThrustSource.minDistance = 2f;
        jumpThrustSource.maxDistance = 14f;
        jumpThrustSource.rolloffMode = AudioRolloffMode.Linear;
    }

    void UpdateJumpThrustAudio(float dt)
    {
        if (jumpThrustSource == null)
            return;

        if (jumpBoosting)
        {
            jumpThrustFade = 0f;
            jumpThrustSource.volume = 1f;
            if (!jumpThrustSource.isPlaying)
                jumpThrustSource.Play();
            return;
        }

        if (jumpThrustFade <= 0f)
            return;

        jumpThrustFade -= dt;
        jumpThrustSource.volume = Mathf.Clamp01(jumpThrustFade / JumpThrustFadeTime);
        if (jumpThrustFade > 0f)
            return;

        jumpThrustSource.Stop();
        jumpThrustSource.volume = 1f;
    }

    void UpdateGrindAudio()
    {
        if (grindLoopSource != null)
        {
            bool onRail = grinding && grindRail != null;
            if (onRail)
            {
                if (!grindLoopSource.isPlaying)
                    grindLoopSource.Play();
            }
            else if (grindLoopSource.isPlaying)
            {
                grindLoopSource.Stop();
            }
        }

        UpdateWalkAudio();
    }

    void OnEnable()
    {
        moveAction = new InputAction("Hd2Move", InputActionType.Value, expectedControlType: "Vector2");
        moveAction.AddBinding("<XRController>{LeftHand}/thumbstick");
        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w")
            .With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a")
            .With("Right", "<Keyboard>/d");
        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/upArrow")
            .With("Down", "<Keyboard>/downArrow")
            .With("Left", "<Keyboard>/leftArrow")
            .With("Right", "<Keyboard>/rightArrow");
        moveAction.Enable();

        turnAction = new InputAction("Hd2Turn", InputActionType.Value, expectedControlType: "Vector2");
        turnAction.AddBinding("<XRController>{RightHand}/thumbstick");
        turnAction.Enable();

        sprintAction = new InputAction("Hd2Sprint", InputActionType.Button);
        sprintAction.AddBinding("<XRController>{LeftHand}/thumbstickClicked");
        sprintAction.AddBinding("<Keyboard>/leftShift");
        sprintAction.Enable();

        leftThrustAction = new InputAction("Hd2ThrustLeft", InputActionType.Button);
        leftThrustAction.AddBinding("<XRController>{LeftHand}/primaryButton");
        leftThrustAction.Enable();

        rightThrustAction = new InputAction("Hd2ThrustRight", InputActionType.Button);
        rightThrustAction.AddBinding("<XRController>{RightHand}/primaryButton");
        rightThrustAction.Enable();

        viewThrustAction = new InputAction("Hd2ThrustView", InputActionType.Button);
        viewThrustAction.AddBinding("<Keyboard>/space");
        viewThrustAction.AddBinding("<Mouse>/leftButton");
        viewThrustAction.Enable();

        jumpAction = new InputAction("Hd2Jump", InputActionType.Button);
        jumpAction.AddBinding("<XRController>{RightHand}/secondaryButton");
        jumpAction.AddBinding("<Keyboard>/c");
        jumpAction.Enable();

        Vector3 euler = transform.eulerAngles;
        editorYaw = euler.y;
        editorPitch = -18f;
    }

    void OnDisable()
    {
        if (editorTiltApplied)
        {
            transform.rotation = Quaternion.Euler(0f, editorYaw, 0f);
            editorTiltApplied = false;
        }

        editorCursorReady = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        HidePreview();
        if (grindLoopSource != null && grindLoopSource.isPlaying)
            grindLoopSource.Stop();
        if (walkLoopSource != null && walkLoopSource.isPlaying)
            walkLoopSource.Stop();
        if (jumpThrustSource != null)
        {
            jumpThrustSource.Stop();
            jumpThrustSource.volume = 1f;
        }
        jumpThrustFade = 0f;
        Dispose(ref moveAction);
        Dispose(ref turnAction);
        Dispose(ref sprintAction);
        Dispose(ref leftThrustAction);
        Dispose(ref rightThrustAction);
        Dispose(ref viewThrustAction);
        Dispose(ref jumpAction);
        EndJumpBoost();
    }

    void OnDestroy()
    {
        if (previewMaterial != null)
            Destroy(previewMaterial);
    }

    static void Dispose(ref InputAction action)
    {
        if (action == null)
            return;
        action.Disable();
        action.Dispose();
        action = null;
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        PlanarVelocity = Vector3.zero;
        if (dt <= 0f)
            return;

        UpdateJumpThrustAudio(dt);

        float snap = ReadSnapDegrees();
        if (EditorFallback)
            editorYaw += snap;

        EditorLook();

        if (!EditorFallback && Mathf.Abs(snap) > 0.01f)
            YawAroundHead(snap);

        if (thrusting)
        {
            HidePreview();
            AdvanceThrust(dt);
            UpdateGrindAudio();
            return;
        }

        AimSource sample = UpdateAimSource(out bool released);
        if (sample != AimSource.None)
        {
            AimSource held = aimSource;
            aimSource = sample;
            UpdateAimArc();
            aimSource = held;
        }
        else
        {
            HidePreview();
        }

        if (released && aimSource == AimSource.None && aimValid)
        {
            HidePreview();
            if (BeginThrust())
            {
                AdvanceThrust(dt);
                UpdateGrindAudio();
                return;
            }
        }

        bool planted = FeetPlanted();
        if (planted && !jumpBoosting)
            jumpSpent = false;

        if (!jumpSpent && !jumpBoosting && jumpAction != null && jumpAction.WasPressedThisFrame())
            BeginJump();

        if (grinding && grindRail != null && !jumpBoosting)
        {
            if (TickGrind(dt))
            {
                UpdateGrindAudio();
                return;
            }
        }

        if (thrusting)
        {
            UpdateGrindAudio();
            return;
        }

        PlanarVelocity = PlanarStick() * CurrentSpeed();
        MoveHorizontal(PlanarVelocity * dt);
        if (jumpBoosting)
            TickJump(dt);
        else
            ApplyGravity(dt);
        PopOutOfWalls();
        NoteLanding();
        UpdateGrindAudio();
    }

    static bool HeadsetPresent()
    {
        displays.Clear();
        SubsystemManager.GetSubsystems(displays);
        for (int i = 0; i < displays.Count; i++)
        {
            if (displays[i].running)
                return true;
        }

        return false;
    }

    bool EditorFallback => Application.isEditor && !HeadsetPresent();

    void EditorLook()
    {
        if (!EditorFallback)
        {
            if (editorTiltApplied)
            {
                transform.rotation = Quaternion.Euler(0f, editorYaw, 0f);
                editorTiltApplied = false;
            }

            return;
        }

        if (!editorCursorReady)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            editorCursorReady = true;
        }

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else if (Cursor.lockState != CursorLockMode.Locked && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        if (Mouse.current != null && Cursor.lockState == CursorLockMode.Locked)
        {
            Vector2 delta = Mouse.current.delta.ReadValue();
            editorYaw += delta.x * 0.12f;
            editorPitch = Mathf.Clamp(editorPitch - delta.y * 0.08f, -75f, 75f);
        }

        transform.rotation = Quaternion.Euler(editorPitch, editorYaw, 0f);
        editorTiltApplied = true;
    }

    AimSource UpdateAimSource(out bool released)
    {
        AimSource releasedSource = AimSource.None;
        if (rightThrustAction != null && rightThrustAction.WasReleasedThisFrame())
            releasedSource = AimSource.Right;
        else if (leftThrustAction != null && leftThrustAction.WasReleasedThisFrame())
            releasedSource = AimSource.Left;
        else if (viewThrustAction != null && viewThrustAction.WasReleasedThisFrame())
            releasedSource = AimSource.View;

        if (rightThrustAction != null && rightThrustAction.WasPressedThisFrame())
            aimSource = AimSource.Right;
        else if (leftThrustAction != null && leftThrustAction.WasPressedThisFrame())
            aimSource = AimSource.Left;
        else if (viewThrustAction != null && viewThrustAction.WasPressedThisFrame())
            aimSource = AimSource.View;

        if (!SourceHeld(aimSource))
        {
            if (rightThrustAction != null && rightThrustAction.IsPressed())
                aimSource = AimSource.Right;
            else if (leftThrustAction != null && leftThrustAction.IsPressed())
                aimSource = AimSource.Left;
            else if (viewThrustAction != null && viewThrustAction.IsPressed())
                aimSource = AimSource.View;
            else
                aimSource = AimSource.None;
        }

        released = releasedSource != AimSource.None && aimSource == AimSource.None;
        if (aimSource != AimSource.None)
            return aimSource;
        return released ? releasedSource : AimSource.None;
    }

    bool SourceHeld(AimSource source)
    {
        switch (source)
        {
            case AimSource.Right:
                return rightThrustAction != null && rightThrustAction.IsPressed();
            case AimSource.Left:
                return leftThrustAction != null && leftThrustAction.IsPressed();
            case AimSource.View:
                return viewThrustAction != null && viewThrustAction.IsPressed();
            default:
                return false;
        }
    }

    void UpdateAimArc()
    {
        aimValid = false;
        aimKind = AimKind.None;
        aimShowOutline = false;
        aimRail = null;
        arcPointCount = 0;
        if (!TryGetAim(out Vector3 origin, out Vector3 direction, out Vector3 bend))
        {
            HidePreview();
            return;
        }

        SimulateArc(origin, direction, bend);
        TrimArcToRange(transform.position, Mathf.Max(0.5f, thrustMaxDistance));
        bool showLine = arcPointCount >= 2;
        if (arcLine != null)
        {
            arcLine.enabled = showLine;
            if (showLine)
            {
                arcLine.positionCount = arcPointCount;
                for (int i = 0; i < arcPointCount; i++)
                    arcLine.SetPosition(i, arcPoints[i]);
            }
        }

        var meter = GetComponent<Hd2ThrustMeter>();
        bool noThrust = meter != null && meter.Charges < 1;
        Color color = illegalArcColor;
        if (aimKind == AimKind.Rail && aimRail != null)
            color = aimRail.color;
        else if (aimKind == AimKind.Legal)
            color = legalArcColor;
        if (aimShowOutline && !noThrust)
            ShowAvatarWire(aimLanding);
        else
            HideAvatarWire();

        ApplyPreviewColor(color);
        if (aimShowOutline)
        {
            if (meter != null)
                meter.ShowUnderTarget(aimLanding, head);
        }
        else if (meter != null)
            meter.Hide();
    }

    bool TryGetAim(out Vector3 origin, out Vector3 direction, out Vector3 bend)
    {
        origin = transform.position + Vector3.up;
        direction = Vector3.forward;
        bend = Vector3.down;
        Transform aim = null;
        if (aimSource == AimSource.Right)
            aim = rightHand;
        else if (aimSource == AimSource.Left)
            aim = leftHand;
        else
            aim = head;

        if (aim != null)
        {
            origin = aim.position;
            direction = aim.forward;
        }
        else if (head != null)
        {
            origin = head.position;
            direction = head.forward;
        }

        if (direction.sqrMagnitude < 0.0001f)
            direction = Vector3.forward;
        direction.Normalize();

        bool handAim = aim != null && (aimSource == AimSource.Left || aimSource == AimSource.Right);
        if (handAim)
        {
            bend = -aim.up;
            if (bend.sqrMagnitude < 0.0001f)
                bend = Vector3.down;
            bend.Normalize();

            if (Mathf.Abs(arcLaunchAngle) > 0.01f)
            {
                // -65 on this axis lifts the arc slightly above where the gun is aiming.
                direction = Quaternion.AngleAxis(-arcLaunchAngle, aim.right) * direction;
                if (direction.sqrMagnitude < 0.0001f)
                    direction = aim.forward;
                direction.Normalize();
            }
        }

        origin += direction * 0.08f;
        return true;
    }

    void SimulateArc(Vector3 origin, Vector3 direction, Vector3 bend)
    {
        float step = 0.025f;
        float speed = Mathf.Max(0.5f, arcSpeed);
        float gravityScale = Mathf.Max(0f, arcGravity);
        float maxDistance = Mathf.Max(0.5f, thrustMaxDistance);
        float maxSqr = maxDistance * maxDistance;
        Vector3 player = transform.position;

        Vector3 pos = origin;
        Vector3 velocity = direction * speed;
        arcPoints[arcPointCount++] = pos;

        for (int i = 0; i < ArcPointCapacity - 2; i++)
        {
            Vector3 next = pos + velocity * step;
            velocity += bend * gravityScale * step;

            if (TryArcHit(pos, next, out RaycastHit hit))
            {
                Hd2SpawnField field = hit.collider != null
                    ? hit.collider.GetComponentInParent<Hd2SpawnField>()
                    : null;
                if (field != null)
                {
                    // The local player may aim through the spawn shield. Enemies do not use this arc.
                    Vector3 travel = next - pos;
                    if (travel.sqrMagnitude < 0.0000001f)
                        travel = velocity;
                    if (travel.sqrMagnitude < 0.0000001f)
                        travel = field.Outward;
                    travel.Normalize();
                    pos = hit.point + travel * 1.25f;
                    if (arcPointCount < ArcPointCapacity)
                        arcPoints[arcPointCount++] = pos;
                    continue;
                }

                if ((hit.point - player).sqrMagnitude > maxSqr)
                {
                    KeepSurfaceWithinRange(pos, hit, player, maxSqr);
                    return;
                }

                if (arcPointCount < ArcPointCapacity)
                    arcPoints[arcPointCount++] = hit.point;

                Hd2GrindRail rail = hit.collider != null ? hit.collider.GetComponentInParent<Hd2GrindRail>() : null;
                if (rail == null && !(grinding && hit.normal.y > 0.55f))
                    rail = RailAtPoint(hit.point);
                if (rail != null || hit.normal.y > 0.55f)
                {
                    AcceptSurfaceHit(hit, player, maxSqr);
                    return;
                }

                DropToSurfaceBelow(hit.point + hit.normal * 0.04f, player, maxSqr);
                return;
            }

            if ((next - player).sqrMagnitude > maxSqr)
            {
                if (arcPointCount < ArcPointCapacity)
                    arcPoints[arcPointCount++] = ClipToRange(pos, next, player, maxSqr);
                DropToSurfaceBelow(arcPoints[arcPointCount - 1], player, maxSqr);
                return;
            }

            pos = next;
            if (arcPointCount < ArcPointCapacity)
                arcPoints[arcPointCount++] = pos;
        }

        if (arcPointCount > 0)
            DropToSurfaceBelow(arcPoints[arcPointCount - 1], player, maxSqr);
    }

    void KeepSurfaceWithinRange(Vector3 from, RaycastHit hit, Vector3 player, float maxSqr)
    {
        if (hit.normal.y <= 0.55f)
        {
            Vector3 clipped = ClipToRange(from, hit.point, player, maxSqr);
            if (arcPointCount < ArcPointCapacity)
                arcPoints[arcPointCount++] = clipped;
            DropToSurfaceBelow(clipped, player, maxSqr);
            return;
        }

        // The lowest floor is past the jump limit. Walk back along the arc and
        // stay on that same floor, instead of dropping onto a deck or into the air.
        Vector3 landing = hit.point;
        bool found = false;
        for (int i = 0; i <= 16; i++)
        {
            Vector3 sample = Vector3.Lerp(hit.point, from, i / 16f);
            if ((sample - player).sqrMagnitude > maxSqr)
                continue;

            Vector3 origin = new Vector3(sample.x, hit.point.y + 0.4f, sample.z);
            if (!RaycastBody(origin, Vector3.down, 1.2f, out RaycastHit down))
                continue;
            if (down.collider != hit.collider || down.normal.y <= 0.55f)
                continue;

            landing = down.point;
            found = true;
            break;
        }

        if (!found)
        {
            landing = ClampToRange(player, hit.point, Mathf.Sqrt(maxSqr));
            landing.y = hit.point.y;
        }

        if (arcPointCount < ArcPointCapacity)
            arcPoints[arcPointCount++] = landing;

        var grounded = hit;
        grounded.point = landing;
        AcceptSurfaceHit(grounded, player, maxSqr);
    }

    void AcceptSurfaceHit(RaycastHit hit, Vector3 player, float maxSqr)
    {
        Vector3 landing = LandingFromHit(hit, out Hd2GrindRail rail, out float railSign);
        bool blocked = IsNoLanding(hit.collider);
        bool onFloor = rail == null && hit.normal.y > 0.55f;
        if (!onFloor && (landing - player).sqrMagnitude > maxSqr)
            landing = ClampToRange(player, landing, Mathf.Sqrt(maxSqr));

        aimLanding = landing;
        aimShowOutline = true;
        if (rail != null && !blocked)
        {
            aimKind = AimKind.Rail;
            aimValid = true;
            aimRail = rail;
            aimGrindSign = railSign;
        }
        else if (!blocked)
        {
            aimKind = AimKind.Legal;
            aimValid = true;
            aimRail = null;
        }
        else
        {
            aimKind = AimKind.Illegal;
            aimValid = false;
            aimRail = null;
        }
    }

    bool TryArcHit(Vector3 from, Vector3 to, out RaycastHit hit)
    {
        if (!CastArcSegment(from, to, out hit))
            return false;
        if (!PassThroughCurrentRail(hit))
            return true;

        Collider railCollider = grindRail != null ? grindRail.GetComponent<Collider>() : null;
        if (railCollider == null || !railCollider.enabled)
            return false;

        // The beam under the player was the first hit, and stepping along it
        // skipped the floor. Cast the same step again with that beam hidden.
        railCollider.enabled = false;
        bool found = CastArcSegment(from, to, out hit);
        railCollider.enabled = true;
        return found;
    }

    bool CastArcSegment(Vector3 from, Vector3 to, out RaycastHit hit)
    {
        hit = default;
        Vector3 delta = to - from;
        float distance = delta.magnitude;
        if (distance < 0.0001f)
            return false;

        // A ray along a nearly flat arc misses the big floor or returns a point
        // far off the step. A small sphere stays on the step and still hits.
        const float radius = 0.05f;
        Vector3 direction = delta / distance;
        float traveled = 0f;
        while (traveled < distance - 0.001f)
        {
            Vector3 origin = from + direction * traveled;
            float remain = distance - traveled;
            if (!Physics.SphereCast(origin, radius, direction, out hit, remain, ~0, QueryTriggerInteraction.Ignore))
                return false;
            if (hit.distance <= 0.0001f || IsSelf(hit.collider))
            {
                traveled += Mathf.Max(0.05f, hit.distance + 0.02f);
                continue;
            }

            if (hit.collider != null && hit.collider.GetComponentInParent<Hd2SpawnField>() != null)
                return true;

            Vector3 center = origin + direction * hit.distance;
            float slack = radius + 0.08f;
            if ((hit.point - center).sqrMagnitude <= slack * slack)
                return true;

            traveled += Mathf.Max(0.02f, hit.distance + 0.05f);
        }

        return false;
    }

    bool PassThroughCurrentRail(RaycastHit hit)
    {
        if (!grinding || grindRail == null || hit.collider == null)
            return false;
        if (hit.collider.GetComponentInParent<Hd2GrindRail>() != grindRail)
            return false;

        // A low aim skims the beam and was coming back red. Only a hit on the
        // top of the rail, clearly ahead and still in range, is another grind.
        bool onTop = hit.normal.y > 0.55f;
        float ahead = (hit.point - transform.position).sqrMagnitude;
        float maxSqr = Mathf.Max(0.5f, thrustMaxDistance);
        maxSqr *= maxSqr;
        return !(onTop && ahead > 1.5f * 1.5f && ahead <= maxSqr);
    }

    bool TryFindSurfaceAtOrBelow(Vector3 from, out RaycastHit hit)
    {
        if (TryRayDown(from + Vector3.up * 0.05f, from.y + 0.08f, out hit))
            return true;

        // A shallow arc from the rail can end inside the floor. Start the
        // search again from above that surface.
        if (from.y < 0.5f)
        {
            Vector3 raised = from;
            raised.y = 0.5f;
            if (TryRayDown(raised, raised.y + 0.08f, out hit))
                return true;
        }

        // The arc step can end inside the floor after a shallow miss. Search back
        // up the arc until a downward ray starts above the surface.
        for (int i = arcPointCount - 1; i >= 0; i--)
        {
            Vector3 sample = arcPoints[i];
            if (sample.y <= from.y + 0.01f)
                continue;
            if (TryRayDown(sample + Vector3.up * 0.05f, sample.y + 0.08f, out hit))
                return true;
        }

        return false;
    }

    bool TryRayDown(Vector3 origin, float maxPointY, out RaycastHit hit)
    {
        Collider railCollider = null;
        bool hidden = false;
        if (grinding && grindRail != null)
        {
            railCollider = grindRail.GetComponent<Collider>();
            if (railCollider != null && railCollider.enabled)
            {
                railCollider.enabled = false;
                hidden = true;
            }
        }

        bool found = RayDown(origin, maxPointY, out hit);
        if (hidden)
            railCollider.enabled = true;
        return found;
    }

    bool RayDown(Vector3 origin, float maxPointY, out RaycastHit hit)
    {
        hit = default;
        float remaining = 40f;
        for (int attempt = 0; attempt < 8 && remaining > 0.01f; attempt++)
        {
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, bodyHits, remaining, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            int bestIndex = -1;
            for (int i = 0; i < count; i++)
            {
                if (bodyHits[i].collider != null && bodyHits[i].collider.GetComponentInParent<Hd2SpawnField>() != null)
                    continue;
                if (IsSelf(bodyHits[i].collider))
                    continue;
                if (bodyHits[i].distance >= best)
                    continue;
                best = bodyHits[i].distance;
                bestIndex = i;
            }

            if (bestIndex < 0)
                return false;

            RaycastHit nearest = bodyHits[bestIndex];
            if (nearest.point.y <= maxPointY)
            {
                hit = nearest;
                return true;
            }

            float advance = nearest.distance + 0.05f;
            origin += Vector3.down * advance;
            remaining -= advance;
        }

        return false;
    }

    void DropToSurfaceBelow(Vector3 from, Vector3 player, float maxSqr)
    {
        // Cast from the arc end itself. Starting meters above it makes the closest
        // hit the block the player is standing on, and that hit gets thrown away,
        // so any drop shorter than that offset never finds the floor.
        if (!TryFindSurfaceAtOrBelow(from, out RaycastHit hit))
        {
            aimKind = AimKind.Illegal;
            aimValid = false;
            aimShowOutline = false;
            aimRail = null;
            return;
        }

        while (arcPointCount > 1 && arcPoints[arcPointCount - 1].y < hit.point.y - 0.02f)
            arcPointCount--;
        if (arcPointCount > 0 && arcPointCount < ArcPointCapacity
            && arcPoints[arcPointCount - 1].y - hit.point.y > 0.015f)
            arcPoints[arcPointCount++] = hit.point;

        Hd2SpawnField field = hit.collider != null ? hit.collider.GetComponentInParent<Hd2SpawnField>() : null;
        if (field != null)
        {
            Vector3 passLanding = field.PassThroughPoint(hit.point, Vector3.down, bodyRadius, out Collider ground);
            if (arcPointCount < ArcPointCapacity)
                arcPoints[arcPointCount++] = passLanding;
            aimLanding = passLanding;
            aimShowOutline = true;
            aimRail = null;
            if ((passLanding - player).sqrMagnitude > maxSqr)
            {
                passLanding = ClampToRange(player, passLanding, Mathf.Sqrt(maxSqr));
                aimLanding = passLanding;
            }

            if (!IsNoLanding(ground))
            {
                aimKind = AimKind.Legal;
                aimValid = true;
            }
            else
            {
                aimKind = AimKind.Illegal;
                aimValid = false;
            }

            return;
        }

        AcceptSurfaceHit(hit, player, maxSqr);
    }

    Vector3 LandingFromHit(RaycastHit hit, out Hd2GrindRail rail, out float railSign)
    {
        railSign = 1f;
        rail = hit.collider != null ? hit.collider.GetComponentInParent<Hd2GrindRail>() : null;
        if (rail == null && !(grinding && hit.normal.y > 0.55f))
            rail = RailAtPoint(hit.point);
        if (rail != null)
        {
            Vector3 point = rail.PointAlong(rail.DistanceAlong(hit.point));
            railSign = rail.EntrySign(transform.position, point);
            return point;
        }

        if (hit.normal.y > 0.55f)
            return hit.point;

        return hit.point + hit.normal * (Mathf.Max(0.05f, bodyRadius) + 0.02f);
    }

    static bool IsNoLanding(Collider collider)
    {
        return collider != null && collider.GetComponentInParent<Hd2NoLanding>() != null;
    }

    // The spawn field stays solid so outside shots hit it. The local player walks and
    // falls through it. Thrust aims through it in SimulateArc.
    bool CastBody(Vector3 bottom, Vector3 top, float radius, Vector3 direction, float distance, out RaycastHit hit)
    {
        int count = Physics.CapsuleCastNonAlloc(bottom, top, radius, direction, bodyHits, distance, ~0, QueryTriggerInteraction.Ignore);
        return NearestBodyHit(count, out hit);
    }

    bool RaycastBody(Vector3 origin, Vector3 direction, float distance, out RaycastHit hit)
    {
        int count = Physics.RaycastNonAlloc(origin, direction, bodyHits, distance, ~0, QueryTriggerInteraction.Ignore);
        return NearestBodyHit(count, out hit);
    }

    bool IsSelf(Collider collider)
    {
        return collider != null && (collider.transform == transform || collider.transform.IsChildOf(transform));
    }

    bool NearestBodyHit(int count, out RaycastHit hit)
    {
        hit = default;
        float best = float.MaxValue;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            Collider collider = bodyHits[i].collider;
            if (IsSelf(collider))
                continue;
            if (collider != null && collider.GetComponentInParent<Hd2SpawnField>() != null)
                continue;
            if (bodyHits[i].distance >= best)
                continue;

            best = bodyHits[i].distance;
            hit = bodyHits[i];
            found = true;
        }

        return found;
    }

    static Hd2GrindRail RailAtPoint(Vector3 point)
    {
        const float reach = 0.3f;
        int count = Physics.OverlapSphereNonAlloc(point, reach, railOverlap, ~0, QueryTriggerInteraction.Ignore);
        Hd2GrindRail best = null;
        float bestSqr = reach * reach;
        for (int i = 0; i < count; i++)
        {
            Collider collider = railOverlap[i];
            if (collider == null)
                continue;
            Hd2GrindRail rail = collider.GetComponentInParent<Hd2GrindRail>();
            if (rail == null)
                continue;

            Vector3 closest = rail.PointAlong(rail.DistanceAlong(point));
            float vertical = Mathf.Abs(point.y - closest.y);
            if (vertical > 0.6f)
                continue;

            Vector3 flat = point - closest;
            flat.y = 0f;
            float sqr = flat.sqrMagnitude;
            if (sqr >= bestSqr)
                continue;

            bestSqr = sqr;
            best = rail;
        }

        return best;
    }

    static Vector3 ClipToRange(Vector3 from, Vector3 to, Vector3 player, float maxSqr)
    {
        float lo = 0f;
        float hi = 1f;
        for (int i = 0; i < 8; i++)
        {
            float mid = (lo + hi) * 0.5f;
            Vector3 point = Vector3.Lerp(from, to, mid);
            if ((point - player).sqrMagnitude > maxSqr)
                hi = mid;
            else
                lo = mid;
        }

        return Vector3.Lerp(from, to, lo);
    }

    static Vector3 ClampToRange(Vector3 player, Vector3 point, float maxDistance)
    {
        Vector3 offset = point - player;
        float distance = offset.magnitude;
        if (distance <= maxDistance || distance < 0.0001f)
            return point;
        return player + offset * (maxDistance / distance);
    }

    void TrimArcToRange(Vector3 player, float maxDistance)
    {
        float maxSqr = maxDistance * maxDistance;
        int last = arcPointCount - 1;
        for (int i = 1; i < arcPointCount; i++)
        {
            if ((arcPoints[i] - player).sqrMagnitude <= maxSqr)
                continue;

            // The floor under a high player can sit just past the limit. Keep that
            // landing on the ground instead of pulling the marker up into the air.
            if (i == last && aimShowOutline && (arcPoints[i] - aimLanding).sqrMagnitude < 0.05f)
                return;

            arcPoints[i] = ClipToRange(arcPoints[i - 1], arcPoints[i], player, maxSqr);
            arcPointCount = i + 1;
            return;
        }
    }

    bool BeginThrust()
    {
        var meter = GetComponent<Hd2ThrustMeter>();
        if (meter != null && !meter.TrySpend())
            return false;

        EndJumpBoost();
        grinding = false;
        grindRail = null;
        thrusting = true;
        wasOffGround = false;
        thrustElapsed = 0f;
        thrustStart = transform.position;
        thrustEnd = aimLanding;
        thrustRail = aimRail;
        thrustGrindSign = aimGrindSign;
        verticalVelocity = 0f;
        aimValid = false;
        if (blitzSource != null && blitzSource.clip != null)
            blitzSource.PlayOneShot(blitzSource.clip);
        PlayBlitzTrail();
        return true;
    }

    void AdvanceThrust(float dt)
    {
        float distance = Vector3.Distance(thrustStart, thrustEnd);
        float speed = Mathf.Max(0.01f, thrustSpeed);
        float duration = distance / speed;
        thrustElapsed += dt;
        float t = duration <= 0f ? 1f : Mathf.Clamp01(thrustElapsed / duration);
        float hop = 4f * thrustHopHeight * t * (1f - t);
        transform.position = Vector3.Lerp(thrustStart, thrustEnd, t) + Vector3.up * hop;
        if (t < 1f)
            return;

        transform.position = thrustEnd;
        thrusting = false;
        if (thrustRail != null)
        {
            float along = thrustRail.DistanceAlong(thrustEnd);
            transform.position = thrustRail.PointAlong(along);
            grindRail = thrustRail;
            grindSign = thrustGrindSign;
            grindDistance = along;
            grinding = true;
            verticalVelocity = 0f;
        }

        thrustRail = null;
        StopBlitzTrail();
        PopOutOfWalls();
    }

    void PlayBlitzTrail()
    {
        EnsureBlitzTrail();
        if (blitzTrail == null)
            return;

        var main = blitzTrail.main;
        Color spark = Color.Lerp(teamColor, Color.white, 0.62f);
        spark.a = 1f;
        Color edge = teamColor;
        edge.a = 0.9f;
        main.startColor = new ParticleSystem.MinMaxGradient(spark, edge);
        blitzTrail.Play();
    }

    void StopBlitzTrail()
    {
        if (blitzTrail != null && blitzTrail.isPlaying)
            blitzTrail.Stop(false, ParticleSystemStopBehavior.StopEmitting);
    }

    void EnsureBlitzTrail()
    {
        if (blitzTrail != null)
            return;

        var trail = new GameObject("BlitzTrail");
        trail.hideFlags = HideFlags.DontSave;
        trail.transform.SetParent(transform, false);
        trail.transform.localPosition = new Vector3(0f, 1.05f, 0f);
        trail.transform.localRotation = Quaternion.identity;

        blitzTrail = trail.AddComponent<ParticleSystem>();
        blitzTrail.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = blitzTrail.main;
        main.playOnAwake = false;
        main.loop = true;
        main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.7f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.028f);
        main.gravityModifier = 0.05f;
        main.maxParticles = 96;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = blitzTrail.emission;
        emission.rateOverTime = 70f;

        var shape = blitzTrail.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.12f;

        var size = blitzTrail.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 1f),
            new Keyframe(1f, 0.15f)));

        var color = blitzTrail.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f)
            },
            new[]
            {
                new GradientAlphaKey(0.9f, 0f),
                new GradientAlphaKey(0f, 1f)
            });
        color.color = gradient;

        var renderer = trail.GetComponent<ParticleSystemRenderer>();
        renderer.material = BlitzTrailMaterial();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    static Material blitzTrailMaterial;

    static Material BlitzTrailMaterial()
    {
        if (blitzTrailMaterial != null)
            return blitzTrailMaterial;

        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null)
            shader = Shader.Find("Particles/Standard Unlit");
        blitzTrailMaterial = new Material(shader);
        if (blitzTrailMaterial.HasProperty("_BaseColor"))
            blitzTrailMaterial.SetColor("_BaseColor", Color.white);
        return blitzTrailMaterial;
    }

    bool TickGrind(float dt)
    {
        if (grindRail == null)
        {
            grinding = false;
            return false;
        }

        float next = grindDistance + grindSign * grindSpeed * dt;
        if (next < 0f || next > grindRail.length)
        {
            float end = Mathf.Clamp(grindDistance, 0f, grindRail.length);
            float clearance = Mathf.Max(0.05f, bodyRadius) + 0.2f;
            transform.position = grindRail.PointAlong(end) + grindRail.PlanarTangent(end) * grindSign * clearance;
            grinding = false;
            grindRail = null;
            verticalVelocity = 0f;
            return false;
        }

        grindDistance = next;
        transform.position = grindRail.PointAlong(next);
        verticalVelocity = 0f;
        return true;
    }

    float CurrentSpeed()
    {
        bool sprinting = sprintAction != null && sprintAction.IsPressed();
        if (!sprinting)
            return walkSpeed;

        Vector2 stick = ApplyDeadzone(ReadMove(), stickDeadzone);
        return stick.sqrMagnitude > 0.0001f ? sprintSpeed : walkSpeed;
    }

    Vector3 PlanarStick()
    {
        Vector2 stick = ApplyDeadzone(ReadMove(), stickDeadzone);
        Vector3 planar = HeadRight() * stick.x + HeadForward() * stick.y;
        planar.y = 0f;
        return planar;
    }

    float ReadSnapDegrees()
    {
        if (EditorFallback)
        {
            if (Keyboard.current == null)
                return 0f;
            if (Keyboard.current.qKey.wasPressedThisFrame)
                return -Mathf.Abs(snapAngle);
            if (Keyboard.current.eKey.wasPressedThisFrame)
                return Mathf.Abs(snapAngle);
            return 0f;
        }

        float x = 0f;
        if (turnAction != null)
            x = turnAction.ReadValue<Vector2>().x;

        if (Mathf.Abs(x) < SnapRearm)
            snapArmed = true;

        if (!snapArmed)
            return 0f;

        float direction = 0f;
        if (x <= -SnapEngage)
            direction = -1f;
        else if (x >= SnapEngage)
            direction = 1f;

        if (direction == 0f)
            return 0f;

        snapArmed = false;
        return direction * Mathf.Abs(snapAngle);
    }

    void YawAroundHead(float degrees)
    {
        Quaternion yaw = Quaternion.AngleAxis(degrees, Vector3.up);
        Vector3 pivot = head != null ? head.position : transform.position;
        transform.position = pivot + yaw * (transform.position - pivot);
        transform.rotation = yaw * transform.rotation;
    }

    Vector2 ReadMove()
    {
        if (moveAction == null)
            return Vector2.zero;

        Vector2 raw = moveAction.ReadValue<Vector2>();
        if (raw.sqrMagnitude > 1f)
            raw.Normalize();
        return raw;
    }

    static Vector2 ApplyDeadzone(Vector2 value, float deadzone)
    {
        float magnitude = value.magnitude;
        if (magnitude < deadzone || magnitude < 0.0001f)
            return Vector2.zero;

        float remapped = Mathf.Clamp01((magnitude - deadzone) / (1f - deadzone));
        return value / magnitude * remapped;
    }

    Vector3 HeadForward()
    {
        if (head == null)
            return FlattenForward(transform.forward);

        return FlattenForward(head.forward);
    }

    static Vector3 FlattenForward(Vector3 forward)
    {
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
            return Vector3.forward;
        return forward.normalized;
    }

    Vector3 HeadRight()
    {
        return Vector3.Cross(Vector3.up, HeadForward());
    }

    static readonly Collider[] stuckOverlap = new Collider[16];

    void PopOutOfWalls()
    {
        for (int pass = 0; pass < 4; pass++)
        {
            CapsuleEnds(out Vector3 bottom, out Vector3 top);
            float radius = Mathf.Max(0.05f, bodyRadius) * 0.9f;
            int count = Physics.OverlapCapsuleNonAlloc(bottom, top, radius, stuckOverlap, ~0, QueryTriggerInteraction.Ignore);
            bool moved = false;
            for (int i = 0; i < count; i++)
            {
                Collider collider = stuckOverlap[i];
                if (!TryWallPush(collider, bottom, top, radius, out Vector3 push))
                    continue;
                transform.position += push;
                moved = true;
            }

            if (!moved)
                return;
        }
    }

    bool TryWallPush(Collider collider, Vector3 bottom, Vector3 top, float radius, out Vector3 push)
    {
        push = Vector3.zero;
        if (collider == null || collider.isTrigger)
            return false;
        if (collider.transform == transform || collider.transform.IsChildOf(transform))
            return false;
        if (collider.GetComponentInParent<Hd2SpawnField>() != null)
            return false;
        if (collider.GetComponentInParent<Hd2GrindRail>() != null)
            return false;

        Vector3 mid = (bottom + top) * 0.5f;
        Vector3[] samples = { bottom, mid, top };
        for (int i = 0; i < samples.Length; i++)
        {
            Vector3 sample = samples[i];
            if (collider is BoxCollider box && TryBoxExit(box, sample, radius, out Vector3 exit))
            {
                if (Vector3.Dot(exit, Vector3.up) > 0.45f * exit.magnitude)
                    continue;
                push += exit;
                continue;
            }

            Vector3 closest = collider.ClosestPoint(sample);
            Vector3 away = sample - closest;
            float distance = away.magnitude;
            if (distance >= radius || distance < 0.0001f)
                continue;
            Vector3 step = away / distance * (radius - distance + skinWidth);
            if (Vector3.Dot(step, Vector3.up) > 0.45f * step.magnitude)
                continue;
            push += step;
        }

        return push.sqrMagnitude > 0.0000001f;
    }

    static bool TryBoxExit(BoxCollider box, Vector3 worldPoint, float radius, out Vector3 exit)
    {
        exit = Vector3.zero;
        Vector3 local = box.transform.InverseTransformPoint(worldPoint) - box.center;
        Vector3 half = box.size * 0.5f;
        if (Mathf.Abs(local.x) > half.x || Mathf.Abs(local.y) > half.y || Mathf.Abs(local.z) > half.z)
            return false;

        Vector3 scale = box.transform.lossyScale;
        float penX = (half.x - Mathf.Abs(local.x)) * Mathf.Abs(scale.x) + radius;
        float penY = (half.y - Mathf.Abs(local.y)) * Mathf.Abs(scale.y) + radius;
        float penZ = (half.z - Mathf.Abs(local.z)) * Mathf.Abs(scale.z) + radius;
        Vector3 localDir;
        float penetration;
        if (penX <= penY && penX <= penZ)
        {
            localDir = new Vector3(Mathf.Sign(local.x == 0f ? 1f : local.x), 0f, 0f);
            penetration = penX;
        }
        else if (penY <= penZ)
        {
            localDir = new Vector3(0f, Mathf.Sign(local.y == 0f ? 1f : local.y), 0f);
            penetration = penY;
        }
        else
        {
            localDir = new Vector3(0f, 0f, Mathf.Sign(local.z == 0f ? 1f : local.z));
            penetration = penZ;
        }

        Vector3 worldDir = box.transform.TransformDirection(localDir);
        if (worldDir.sqrMagnitude < 0.0001f)
            return false;
        exit = worldDir.normalized * (penetration + 0.02f);
        return true;
    }

    void MoveHorizontal(Vector3 delta)
    {
        if (thrusting)
            return;

        delta.y = 0f;
        for (int slide = 0; slide < 2; slide++)
        {
            if (delta.sqrMagnitude < 0.0000001f)
                return;

            float distance = delta.magnitude;
            Vector3 direction = delta / distance;
            CapsuleEnds(out Vector3 bottom, out Vector3 top);
            if (CastBody(bottom, top, bodyRadius * 0.9f, direction, distance + skinWidth, out RaycastHit hit))
            {
                float travel = Mathf.Max(0f, hit.distance - skinWidth);
                Hd2GrindRail rail = hit.collider != null ? hit.collider.GetComponentInParent<Hd2GrindRail>() : null;
                if (rail != null && TryWalkOnRail(rail, direction, distance))
                    return;

                if (TryStep(direction, distance, travel))
                    return;

                transform.position += direction * travel;
                delta = Vector3.ProjectOnPlane(direction * (distance - travel), hit.normal);
                delta.y = 0f;
            }
            else
            {
                transform.position += delta;
                return;
            }
        }
    }

    // A rail is a floor you can step onto and walk along. It only becomes a grind
    // when a blitz lands on it. A face taller than a step still stops the player.
    bool TryWalkOnRail(Hd2GrindRail rail, Vector3 direction, float distance)
    {
        const float railStep = 0.4f;
        const float beamReach = 0.22f;

        Vector3 feet = transform.position;
        Vector3 probe = feet + direction * Mathf.Min(distance, 0.45f);
        Vector3 aheadDeck = rail.PointAlong(rail.DistanceAlong(probe));
        float aheadRise = aheadDeck.y - feet.y;
        if (aheadRise > railStep)
            return false;

        Collider beam = rail.GetComponent<Collider>();
        bool hidden = beam != null && beam.enabled;
        if (hidden)
            beam.enabled = false;
        transform.position += direction * distance;
        if (hidden)
            beam.enabled = true;

        Vector3 stand = rail.PointAlong(rail.DistanceAlong(transform.position));
        Vector3 off = transform.position - stand;
        off.y = 0f;
        if (off.sqrMagnitude <= beamReach * beamReach && stand.y <= feet.y + railStep && stand.y >= feet.y - railStep)
        {
            Vector3 planted = transform.position;
            planted.y = stand.y;
            transform.position = planted;
        }

        return true;
    }

    // A lip of stepHeight or less is not a wall. Lift over it, finish the step,
    // then put the feet on the floor on the other side, up or down.
    bool TryStep(Vector3 direction, float distance, float blockedTravel)
    {
        float rise = stepHeight;
        if (rise < 0.01f)
            return false;

        float remaining = distance - blockedTravel;
        if (remaining < 0.001f)
            return false;

        CapsuleEnds(out Vector3 bottom, out Vector3 top);
        float radius = bodyRadius * 0.9f;
        if (CastBody(bottom, top, radius, Vector3.up, rise + skinWidth, out RaycastHit ceiling))
        {
            rise = Mathf.Max(0f, ceiling.distance - skinWidth);
            if (rise < 0.02f)
                return false;
        }

        Vector3 raised = Vector3.up * rise;
        if (CastBody(bottom + raised, top + raised, radius, direction, remaining + skinWidth, out RaycastHit wall))
        {
            remaining = Mathf.Max(0f, wall.distance - skinWidth);
            if (remaining < 0.01f)
                return false;
        }

        Vector3 feet = transform.position;
        Vector3 landed = feet + direction * (blockedTravel + remaining) + raised;
        if (!RaycastBody(landed + Vector3.up * 0.05f, Vector3.down, rise + stepHeight + 0.15f, out RaycastHit floor))
            return false;

        float change = floor.point.y - feet.y;
        if (change > stepHeight + 0.001f || change < -stepHeight - 0.001f)
            return false;
        if (Mathf.Abs(change) < 0.01f)
            return false;

        landed.y = floor.point.y;
        transform.position = landed;
        return true;
    }

    bool FeetPlanted()
    {
        if (!TryGround(out float groundY))
            return false;

        return Mathf.Abs(transform.position.y - groundY) <= 0.04f;
    }

    void BeginJump()
    {
        var meter = GetComponent<Hd2ThrustMeter>();
        if (meter == null || meter.Charges <= 0.001f)
            return;

        jumpBoosting = true;
        jumpSpent = true;
        grinding = false;
        grindRail = null;
        jumpOriginY = transform.position.y;
        jumpTime = 0f;
        verticalVelocity = 0f;
        fallTime = 0f;
        SetJumpExhaust(true);
        if (jumpThrustSource != null)
        {
            jumpThrustFade = 0f;
            jumpThrustSource.volume = 1f;
            if (!jumpThrustSource.isPlaying)
                jumpThrustSource.Play();
        }
    }

    void TickJump(float dt)
    {
        var meter = GetComponent<Hd2ThrustMeter>();
        bool held = jumpAction != null && jumpAction.IsPressed();
        float risen = transform.position.y - jumpOriginY;
        float timeLeft = Mathf.Max(0.05f, jumpMaxTime) - jumpTime;
        if (!held || timeLeft <= 0f || risen >= jumpHeight - 0.001f || meter == null || meter.Charges <= 0.001f)
        {
            EndJumpBoost();
            return;
        }

        float speed = jumpHeight / Mathf.Max(0.05f, jumpMaxTime);
        float want = Mathf.Min(speed * Mathf.Min(dt, timeLeft), jumpHeight - risen);
        CapsuleEnds(out Vector3 bottom, out Vector3 top);
        if (CastBody(bottom, top, bodyRadius * 0.9f, Vector3.up, want + skinWidth, out RaycastHit ceiling))
            want = Mathf.Max(0f, ceiling.distance - skinWidth);
        if (want <= 0.0001f)
        {
            EndJumpBoost();
            return;
        }

        float cost = want / speed * Mathf.Max(0.01f, jumpThrustPerSecond);
        float spent = meter.SpendUpTo(cost);
        float rise = spent / Mathf.Max(0.01f, jumpThrustPerSecond) * speed;
        if (rise <= 0.0001f)
        {
            EndJumpBoost();
            return;
        }

        transform.position += Vector3.up * rise;
        jumpTime += rise / speed;
        verticalVelocity = 0f;
        fallTime = 0f;
    }

    void EndJumpBoost()
    {
        bool wasBoosting = jumpBoosting;
        if (!wasBoosting && (jumpExhaust == null || !jumpExhaust[0].isPlaying))
            return;

        jumpBoosting = false;
        verticalVelocity = 0f;
        SetJumpExhaust(false);
        if (wasBoosting && jumpThrustSource != null && jumpThrustSource.isPlaying)
            jumpThrustFade = JumpThrustFadeTime;
    }

    void SetJumpExhaust(bool playing)
    {
        EnsureJumpExhaust();
        if (jumpExhaust == null)
            return;

        for (int i = 0; i < jumpExhaust.Length; i++)
        {
            if (jumpExhaust[i] == null)
                continue;
            if (playing)
                jumpExhaust[i].Play();
            else
                jumpExhaust[i].Stop(false, ParticleSystemStopBehavior.StopEmitting);
        }
    }

    void EnsureJumpExhaust()
    {
        if (jumpExhaust != null)
            return;

        var avatar = GetComponent<Hd2Body>();
        Transform root = avatar != null && avatar.body != null ? avatar.body : transform;
        if (!jumpNozzlesReady)
            jumpNozzles = FindJumpNozzles(root);
        jumpNozzlesReady = true;

        jumpExhaust = new ParticleSystem[jumpNozzles.Length];
        for (int i = 0; i < jumpNozzles.Length; i++)
        {
            var vent = new GameObject(i == 0 ? "JumpExhaustL" : "JumpExhaustR");
            vent.hideFlags = HideFlags.DontSave;
            vent.transform.SetParent(root, false);
            vent.transform.localPosition = jumpNozzles[i];
            vent.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            jumpExhaust[i] = CreateJumpExhaust(vent);
        }
    }

    static Vector3[] FindJumpNozzles(Transform root)
    {
        var fallback = new[]
        {
            new Vector3(-0.14f, 1.03f, 0.3f),
            new Vector3(0.14f, 1.03f, 0.3f)
        };
        var skin = root.GetComponentInChildren<SkinnedMeshRenderer>();
        if (skin == null || skin.sharedMesh == null || !skin.sharedMesh.isReadable)
            return fallback;

        Vector3[] verts = skin.sharedMesh.vertices;
        var upper = new List<Vector3>(256);
        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 local = root.InverseTransformPoint(skin.transform.TransformPoint(verts[i]));
            if (local.y < 0.95f || local.y > 1.55f || Mathf.Abs(local.x) > 0.55f)
                continue;
            upper.Add(local);
        }

        if (upper.Count < 24)
            return fallback;

        Vector3 center = Vector3.zero;
        for (int i = 0; i < upper.Count; i++)
            center += upper[i];
        center /= upper.Count;

        Vector3 tip = upper[0];
        float best = 0f;
        for (int i = 0; i < upper.Count; i++)
        {
            Vector3 flat = upper[i] - center;
            flat.y = 0f;
            float distance = flat.sqrMagnitude;
            if (distance <= best)
                continue;
            best = distance;
            tip = upper[i];
        }

        Vector3 back = tip - center;
        back.y = 0f;
        if (back.sqrMagnitude < 0.0001f)
            return fallback;
        back.Normalize();
        Vector3 side = Vector3.Cross(Vector3.up, back);

        Vector3 left = Vector3.zero;
        Vector3 right = Vector3.zero;
        float leftY = float.PositiveInfinity;
        float rightY = float.PositiveInfinity;
        int leftCount = 0;
        int rightCount = 0;
        for (int i = 0; i < upper.Count; i++)
        {
            Vector3 point = upper[i];
            if (Vector3.Dot(point - center, back) < 0.1f)
                continue;
            float lateral = Vector3.Dot(point - center, side);
            if (lateral < -0.04f && point.y < leftY)
            {
                leftY = point.y;
                left = point;
                leftCount++;
            }
            else if (lateral > 0.04f && point.y < rightY)
            {
                rightY = point.y;
                right = point;
                rightCount++;
            }
        }

        if (leftCount == 0 || rightCount == 0)
            return fallback;

        left.y -= 0.02f;
        right.y -= 0.02f;
        return new[] { left, right };
    }

    static ParticleSystem CreateJumpExhaust(GameObject vent)
    {
        var particles = vent.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main;
        main.playOnAwake = false;
        main.loop = true;
        main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.28f, 0.55f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.4f, 2.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.08f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.55f, 0.58f, 0.6f, 0.45f),
            new Color(0.82f, 0.84f, 0.86f, 0.7f));
        main.gravityModifier = 0.15f;
        main.maxParticles = 48;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = particles.emission;
        emission.rateOverTime = 36f;

        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 14f;
        shape.radius = 0.015f;

        var size = particles.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.45f),
            new Keyframe(1f, 1.5f)));

        var color = particles.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.75f, 0.78f, 0.8f), 0f),
                new GradientColorKey(new Color(0.45f, 0.48f, 0.5f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(0.65f, 0f),
                new GradientAlphaKey(0f, 1f)
            });
        color.color = gradient;

        var renderer = vent.GetComponent<ParticleSystemRenderer>();
        renderer.material = JumpExhaustMaterial();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return particles;
    }

    static Material jumpExhaustMaterial;

    static Material JumpExhaustMaterial()
    {
        if (jumpExhaustMaterial != null)
            return jumpExhaustMaterial;

        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null)
            shader = Shader.Find("Particles/Standard Unlit");
        jumpExhaustMaterial = new Material(shader);
        if (jumpExhaustMaterial.HasProperty("_BaseColor"))
            jumpExhaustMaterial.SetColor("_BaseColor", Color.white);
        return jumpExhaustMaterial;
    }

    void ApplyGravity(float dt)
    {
        bool fallingBack = jumpSpent && transform.position.y > jumpOriginY + 0.02f;
        if (!fallingBack && verticalVelocity <= 0f && TryGround(out float groundY))
        {
            verticalVelocity = 0f;
            fallTime = 0f;
            Vector3 grounded = transform.position;
            grounded.y = groundY;
            transform.position = grounded;
            return;
        }

        verticalVelocity -= gravity * dt;
        if (verticalVelocity < -terminalVelocity)
            verticalVelocity = -terminalVelocity;

        float dy = verticalVelocity * dt;
        float distance = Mathf.Abs(dy);
        if (distance < 0.0000001f)
            return;

        Vector3 direction = dy < 0f ? Vector3.down : Vector3.up;
        CapsuleEnds(out Vector3 bottom, out Vector3 top);
        if (CastBody(bottom, top, bodyRadius * 0.9f, direction, distance + skinWidth, out RaycastHit hit))
        {
            float travel = Mathf.Max(0f, hit.distance - skinWidth);
            transform.position += direction * travel;
            verticalVelocity = 0f;
            fallTime = 0f;
            return;
        }

        transform.position += Vector3.up * dy;
        if (dy < 0f)
            CountFall(dt);
    }

    void CountFall(float dt)
    {
        if (thrusting || grinding)
        {
            fallTime = 0f;
            return;
        }

        fallTime += dt;
        if (fallTime < fallDeathSeconds)
            return;

        fallTime = 0f;
        var health = GetComponent<Hd2Health>();
        if (health != null && !health.IsDead)
            health.ApplyHit(health.Health, false);
    }

    bool TryGround(out float groundY)
    {
        groundY = transform.position.y;
        Vector3 origin = transform.position + Vector3.up * 0.2f;
        if (!RaycastBody(origin, Vector3.down, 0.2f + groundProbe, out RaycastHit hit))
            return false;

        float feetGap = transform.position.y - hit.point.y;
        if (feetGap > groundProbe || feetGap < -0.05f)
            return false;

        groundY = hit.point.y;
        return true;
    }

    void CapsuleEnds(out Vector3 bottom, out Vector3 top)
    {
        float radius = Mathf.Max(0.05f, bodyRadius);
        float height = Mathf.Max(bodyHeight, radius * 2f + 0.01f);
        bottom = transform.position + Vector3.up * (radius + skinWidth);
        top = transform.position + Vector3.up * (height - radius);
    }

    void CreatePreview()
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Lit");
        previewMaterial = shader != null ? new Material(shader) : null;
        if (previewMaterial != null)
            previewMaterial.hideFlags = HideFlags.DontSave;

        arcLine = CreateLine("ThrustArc", 0.035f);
        HidePreview();
    }

    LineRenderer CreateLine(string name, float width)
    {
        var go = new GameObject(name);
        go.hideFlags = HideFlags.DontSave;
        go.transform.SetParent(transform, false);
        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.loop = false;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        line.numCapVertices = 4;
        line.numCornerVertices = 2;
        line.widthMultiplier = width;
        line.positionCount = 0;
        if (previewMaterial != null)
            line.sharedMaterial = previewMaterial;
        line.enabled = false;
        return line;
    }

    void ApplyPreviewColor(Color color)
    {
        if (previewMaterial != null)
        {
            if (previewMaterial.HasProperty("_BaseColor"))
                previewMaterial.SetColor("_BaseColor", color);
            if (previewMaterial.HasProperty("_Color"))
                previewMaterial.SetColor("_Color", color);
        }

        if (arcLine != null)
        {
            arcLine.startColor = color;
            arcLine.endColor = color;
        }

        if (wireMaterial != null)
        {
            Color wire = color;
            wire.a = 0.25f;
            if (wireMaterial.HasProperty("_BaseColor"))
                wireMaterial.SetColor("_BaseColor", wire);
            if (wireMaterial.HasProperty("_Color"))
                wireMaterial.SetColor("_Color", wire);
        }

        if (wireLines == null)
            return;

        for (int i = 0; i < wireLines.Length; i++)
        {
            if (wireLines[i] == null)
                continue;
            wireLines[i].startColor = color;
            wireLines[i].endColor = color;
        }
    }

    void ShowAvatarWire(Vector3 landing)
    {
        var avatar = GetComponent<Hd2Body>();
        if (avatar == null || avatar.body == null)
        {
            HideAvatarWire();
            return;
        }

        var skin = avatar.body.GetComponentInChildren<SkinnedMeshRenderer>(true);
        var shaped = avatar.body.GetComponentInChildren<MeshRenderer>(true);
        if ((skin != null && skin.sharedMesh != null) || (shaped != null && shaped.enabled))
        {
            ShowSkinnedGhost(avatar.body, landing);
            return;
        }

        if (wireParts == null || wireParts.Length == 0)
            wireParts = avatar.body.GetComponentsInChildren<MeshFilter>(true);

        int needed = 0;
        for (int i = 0; i < wireParts.Length; i++)
        {
            MeshFilter part = wireParts[i];
            if (part != null && part.sharedMesh != null && part.gameObject.activeInHierarchy)
                needed += 12;
        }

        EnsureWireLines(needed);
        Vector3 delta = landing - transform.position;
        int line = 0;
        for (int i = 0; i < wireParts.Length; i++)
        {
            MeshFilter part = wireParts[i];
            if (part == null || part.sharedMesh == null || !part.gameObject.activeInHierarchy)
                continue;

            WriteBoxCorners(part.sharedMesh.bounds);
            Matrix4x4 toWorld = part.transform.localToWorldMatrix;
            for (int c = 0; c < wireCorners.Length; c++)
                wireCorners[c] = toWorld.MultiplyPoint3x4(wireCorners[c]) + delta;

            for (int e = 0; e < boxEdges.Length; e += 2)
            {
                LineRenderer edge = wireLines[line++];
                edge.enabled = true;
                edge.positionCount = 2;
                edge.SetPosition(0, wireCorners[boxEdges[e]]);
                edge.SetPosition(1, wireCorners[boxEdges[e + 1]]);
            }
        }

        for (int i = line; i < wireLines.Length; i++)
            wireLines[i].enabled = false;
    }

    void ShowSkinnedGhost(Transform source, Vector3 landing)
    {
        if (!EnsureGhost(source))
        {
            HideAvatarWire();
            return;
        }

        if (wireLines != null)
        {
            for (int i = 0; i < wireLines.Length; i++)
            {
                if (wireLines[i] != null)
                    wireLines[i].enabled = false;
            }
        }

        // The preview body's origin is its feet. Put those feet on the landing.
        // Offset from the tracking origin instead shifted the robot with real-world facing.
        Vector3 delta = landing - source.position;
        thrustGhost.SetActive(true);
        CopyPose(source, thrustGhost.transform, delta);
    }

    bool EnsureGhost(Transform source)
    {
        if (thrustGhost != null)
            return wireMaterial != null;

        Shader shader = Shader.Find("HD2/WireAvatar");
        bool hasSkin = source.GetComponentInChildren<SkinnedMeshRenderer>(true) != null;
        bool hasMesh = source.GetComponentInChildren<MeshRenderer>(true) != null;
        if (shader == null || (!hasSkin && !hasMesh))
            return false;

        thrustGhost = Instantiate(source.gameObject);
        thrustGhost.name = "ThrustAvatarGhost";
        thrustGhost.hideFlags = HideFlags.DontSave;
        thrustGhost.transform.SetParent(null, true);

        var colliders = thrustGhost.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
            Destroy(colliders[i]);
        var animators = thrustGhost.GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < animators.Length; i++)
            animators[i].enabled = false;

        wireMaterial = new Material(shader);
        wireMaterial.hideFlags = HideFlags.DontSave;

        var skins = thrustGhost.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < skins.Length; i++)
        {
            SkinnedMeshRenderer skin = skins[i];
            if (skin.sharedMesh != null && skin.sharedMesh.isReadable)
                skin.sharedMesh = MakeWireMesh(skin.sharedMesh);
            skin.sharedMaterial = wireMaterial;
            skin.updateWhenOffscreen = true;
            skin.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            skin.receiveShadows = false;
        }

        var filters = thrustGhost.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < filters.Length; i++)
        {
            MeshFilter filter = filters[i];
            var renderer = filter.GetComponent<MeshRenderer>();
            if (renderer == null)
                continue;
            if (filter.sharedMesh != null && filter.sharedMesh.isReadable)
                filter.sharedMesh = MakeWireMesh(filter.sharedMesh);
            renderer.sharedMaterial = wireMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        thrustGhost.SetActive(false);
        return true;
    }

    static Mesh MakeWireMesh(Mesh source)
    {
        Vector3[] srcVerts = source.vertices;
        Vector3[] srcNormals = source.normals;
        BoneWeight[] srcWeights = source.boneWeights;
        int[] tris = source.triangles;
        int count = tris.Length;
        var verts = new Vector3[count];
        var normals = new Vector3[count];
        var colors = new Color32[count];
        var weights = new BoneWeight[count];
        var indices = new int[count];
        bool hasNormals = srcNormals != null && srcNormals.Length == srcVerts.Length;
        bool hasWeights = srcWeights != null && srcWeights.Length == srcVerts.Length;
        for (int i = 0; i < count; i++)
        {
            int sourceIndex = tris[i];
            verts[i] = srcVerts[sourceIndex];
            if (hasNormals)
                normals[i] = srcNormals[sourceIndex];
            if (hasWeights)
                weights[i] = srcWeights[sourceIndex];
            int corner = i % 3;
            colors[i] = corner == 0
                ? new Color32(255, 0, 0, 255)
                : corner == 1 ? new Color32(0, 255, 0, 255) : new Color32(0, 0, 255, 255);
            indices[i] = i;
        }

        var mesh = new Mesh();
        mesh.name = source.name + " Wire";
        mesh.hideFlags = HideFlags.DontSave;
        if (count > 65535)
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = verts;
        if (hasNormals)
            mesh.normals = normals;
        mesh.colors32 = colors;
        if (hasWeights)
            mesh.boneWeights = weights;
        if (source.bindposes != null && source.bindposes.Length > 0)
            mesh.bindposes = source.bindposes;
        mesh.triangles = indices;
        return mesh;
    }

    static void CopyPose(Transform source, Transform dest, Vector3 delta)
    {
        dest.SetPositionAndRotation(source.position + delta, source.rotation);
        dest.localScale = source.lossyScale;
        int count = Mathf.Min(source.childCount, dest.childCount);
        for (int i = 0; i < count; i++)
            CopyLocal(source.GetChild(i), dest.GetChild(i));
    }

    static void CopyLocal(Transform source, Transform dest)
    {
        dest.localPosition = source.localPosition;
        dest.localRotation = source.localRotation;
        dest.localScale = source.localScale;
        int count = Mathf.Min(source.childCount, dest.childCount);
        for (int i = 0; i < count; i++)
            CopyLocal(source.GetChild(i), dest.GetChild(i));
    }

    void ShowBoneWire(Transform[] bones, Vector3 landing)
    {
        int needed = 0;
        for (int i = 0; i < bones.Length; i++)
        {
            Transform bone = bones[i];
            if (bone != null && bone.parent != null && System.Array.IndexOf(bones, bone.parent) >= 0)
                needed++;
        }

        EnsureWireLines(needed);
        Vector3 delta = landing - transform.position;
        int line = 0;
        for (int i = 0; i < bones.Length; i++)
        {
            Transform bone = bones[i];
            if (bone == null || bone.parent == null || System.Array.IndexOf(bones, bone.parent) < 0)
                continue;

            LineRenderer edge = wireLines[line++];
            edge.enabled = true;
            edge.positionCount = 2;
            edge.SetPosition(0, bone.parent.position + delta);
            edge.SetPosition(1, bone.position + delta);
        }

        for (int i = line; i < wireLines.Length; i++)
            wireLines[i].enabled = false;
    }

    void WriteBoxCorners(Bounds bounds)
    {
        Vector3 min = bounds.min;
        Vector3 max = bounds.max;
        wireCorners[0] = new Vector3(min.x, min.y, min.z);
        wireCorners[1] = new Vector3(max.x, min.y, min.z);
        wireCorners[2] = new Vector3(max.x, max.y, min.z);
        wireCorners[3] = new Vector3(min.x, max.y, min.z);
        wireCorners[4] = new Vector3(min.x, min.y, max.z);
        wireCorners[5] = new Vector3(max.x, min.y, max.z);
        wireCorners[6] = new Vector3(max.x, max.y, max.z);
        wireCorners[7] = new Vector3(min.x, max.y, max.z);
    }

    void EnsureWireLines(int count)
    {
        if (wireLines != null && wireLines.Length >= count)
            return;

        int previous = wireLines != null ? wireLines.Length : 0;
        var next = new LineRenderer[Mathf.Max(count, 12)];
        for (int i = 0; i < previous; i++)
            next[i] = wireLines[i];
        for (int i = previous; i < next.Length; i++)
        {
            LineRenderer edge = CreateLine("ThrustAvatarWire" + i, 0.007f);
            edge.numCapVertices = 0;
            edge.numCornerVertices = 0;
            next[i] = edge;
        }

        wireLines = next;
    }

    void HideAvatarWire()
    {
        if (thrustGhost != null)
            thrustGhost.SetActive(false);
        if (wireLines == null)
            return;
        for (int i = 0; i < wireLines.Length; i++)
        {
            if (wireLines[i] != null)
                wireLines[i].enabled = false;
        }
    }

    void HidePreview()
    {
        if (arcLine != null)
            arcLine.enabled = false;
        HideAvatarWire();
        var meter = GetComponent<Hd2ThrustMeter>();
        if (meter != null)
            meter.Hide();
    }
}
