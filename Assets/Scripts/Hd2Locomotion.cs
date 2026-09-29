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
///   at the first solid hit. Release moves the root in a straight line to that point at
///   thrustSpeed, phasing through geometry. No hit, a hit past thrustMaxDistance, or a hit on
///   Hd2NoLanding does nothing.
///   The local player's aim arc ignores Hd2SpawnField and continues to a landing beyond it.
///   The shield still stops shots from outside and is meant to block enemies, not this player.
///   Arc color: blue on a grind rail, green on other legal landings, yellow when the landing
///   is illegal or there is no valid hit.
/// - Grind: only by thrusting onto an Hd2GrindRail. Stay on until the rail ends (then gravity)
///   or until the next thrust starts. Grip is not grind.
///
/// Editor fallback when no headset is running: WASD / arrows walk, Left Shift sprint,
/// Q snap left, E snap right (one snap per key press),
/// hold Space or left mouse to aim an arc along the view, release to thrust.
/// Mouse look turns the player root so the view can aim. It does not run while a headset is active.
/// </summary>
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
    public float thrustSpeed = 20f;
    public float thrustMaxDistance = 10f;
    [Tooltip("Initial speed of the aim arc, meters per second.")]
    public float arcSpeed = 16f;
    [Tooltip("Downward acceleration of the aim arc. The arc stops at the first solid hit.")]
    public float arcGravity = 28f;
    [Tooltip("Degrees to pitch the thrust arc down from the controller forward. 0 follows the hand. Higher values leave the hand more forward and less upward.")]
    public float arcLaunchAngle = 40f;

    [Header("Grind")]
    public float grindSpeed = 8f;

    [Header("Body")]
    public float bodyRadius = 0.25f;
    public float bodyHeight = 1.7f;
    public float gravity = 15f;
    public float terminalVelocity = 25f;
    public float skinWidth = 0.05f;
    public float groundProbe = 0.3f;

    const int ArcPointCapacity = 64;
    const int RingSegments = 20;
    const float SnapEngage = 0.6f;
    const float SnapRearm = 0.3f;

    static readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();

    InputAction moveAction;
    InputAction turnAction;
    InputAction sprintAction;
    InputAction leftThrustAction;
    InputAction rightThrustAction;
    InputAction viewThrustAction;

    float verticalVelocity;
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

    public void HaltTravel()
    {
        thrusting = false;
        grinding = false;
        grindRail = null;
        thrustRail = null;
        verticalVelocity = 0f;
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
    LineRenderer[] outlineLines;
    Material previewMaterial;

    static readonly Color railArcColor = new Color(0.2f, 0.45f, 1f, 1f);
    static readonly Color legalArcColor = new Color(0.2f, 0.92f, 0.28f, 1f);
    static readonly Color illegalArcColor = new Color(1f, 0.92f, 0.12f, 1f);
    static readonly Color noThrustArcColor = new Color(0.42f, 0.42f, 0.42f, 1f);

    public bool IsGrinding => grinding;
    public bool IsThrusting => thrusting;

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
        Dispose(ref moveAction);
        Dispose(ref turnAction);
        Dispose(ref sprintAction);
        Dispose(ref leftThrustAction);
        Dispose(ref rightThrustAction);
        Dispose(ref viewThrustAction);
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
        if (dt <= 0f)
            return;

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
            BeginThrust();
            HidePreview();
            AdvanceThrust(dt);
            return;
        }

        if (grinding && grindRail != null)
        {
            if (TickGrind(dt))
                return;
        }

        if (thrusting)
            return;

        Vector3 horizontal = PlanarStick() * CurrentSpeed() * dt;
        MoveHorizontal(horizontal);
        ApplyGravity(dt);
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
        if (noThrust)
            color = noThrustArcColor;
        else if (aimKind == AimKind.Rail)
            color = railArcColor;
        else if (aimKind == AimKind.Legal)
            color = legalArcColor;
        ApplyPreviewColor(color);
        if (aimShowOutline)
        {
            ShowOutline(aimLanding);
            if (meter != null)
                meter.ShowUnderTarget(aimLanding, head);
        }
        else
        {
            HideOutline();
            if (meter != null)
                meter.Hide();
        }
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
                direction = Quaternion.AngleAxis(arcLaunchAngle, aim.right) * direction;
                if (direction.sqrMagnitude < 0.0001f)
                    direction = aim.forward;
                direction.Normalize();
            }
        }

        if (origin.y < 0.08f)
            origin.y = 0.08f;
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

                if (arcPointCount < ArcPointCapacity)
                    arcPoints[arcPointCount++] = hit.point;

                Hd2GrindRail rail = hit.collider != null ? hit.collider.GetComponentInParent<Hd2GrindRail>() : null;
                if (rail == null)
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

    void AcceptSurfaceHit(RaycastHit hit, Vector3 player, float maxSqr)
    {
        Vector3 landing = LandingFromHit(hit, out Hd2GrindRail rail, out float railSign);
        bool inRange = (landing - player).sqrMagnitude <= maxSqr;
        bool blocked = IsNoLanding(hit.collider);
        aimLanding = landing;
        aimShowOutline = true;
        if (rail != null && inRange)
        {
            aimKind = AimKind.Rail;
            aimValid = true;
            aimRail = rail;
            aimGrindSign = railSign;
        }
        else if (!blocked && inRange)
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
        hit = default;
        Vector3 delta = to - from;
        float distance = delta.magnitude;
        if (distance < 0.0001f)
            return false;

        // A ray along a nearly flat arc misses the big floor or returns a point
        // far off the step. A small sphere stays on the step and still hits.
        const float radius = 0.05f;
        Vector3 direction = delta / distance;
        if (!Physics.SphereCast(from, radius, direction, out hit, distance, ~0, QueryTriggerInteraction.Ignore))
            return false;
        if (hit.distance <= 0.0001f)
            return false;
        if (hit.collider != null && hit.collider.GetComponentInParent<Hd2SpawnField>() != null)
            return true;

        Vector3 center = from + direction * hit.distance;
        float slack = radius + 0.08f;
        return (hit.point - center).sqrMagnitude <= slack * slack;
    }

    bool TryFindSurfaceAtOrBelow(Vector3 from, out RaycastHit hit)
    {
        if (TryRayDown(from + Vector3.up * 0.05f, from.y + 0.08f, out hit))
            return true;

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
            bool passInRange = (passLanding - player).sqrMagnitude <= maxSqr;
            if (!IsNoLanding(ground) && passInRange)
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
        if (rail == null)
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
    static bool CastBody(Vector3 bottom, Vector3 top, float radius, Vector3 direction, float distance, out RaycastHit hit)
    {
        int count = Physics.CapsuleCastNonAlloc(bottom, top, radius, direction, bodyHits, distance, ~0, QueryTriggerInteraction.Ignore);
        return NearestBodyHit(count, out hit);
    }

    static bool RaycastBody(Vector3 origin, Vector3 direction, float distance, out RaycastHit hit)
    {
        int count = Physics.RaycastNonAlloc(origin, direction, bodyHits, distance, ~0, QueryTriggerInteraction.Ignore);
        return NearestBodyHit(count, out hit);
    }

    static bool NearestBodyHit(int count, out RaycastHit hit)
    {
        hit = default;
        float best = float.MaxValue;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            Collider collider = bodyHits[i].collider;
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

    void BeginThrust()
    {
        var meter = GetComponent<Hd2ThrustMeter>();
        if (meter != null && !meter.TrySpend())
            return;

        grinding = false;
        grindRail = null;
        thrusting = true;
        thrustElapsed = 0f;
        thrustStart = transform.position;
        thrustEnd = aimLanding;
        thrustRail = aimRail;
        thrustGrindSign = aimGrindSign;
        verticalVelocity = 0f;
        aimValid = false;
    }

    void AdvanceThrust(float dt)
    {
        float distance = Vector3.Distance(thrustStart, thrustEnd);
        float speed = Mathf.Max(0.01f, thrustSpeed);
        float duration = distance / speed;
        thrustElapsed += dt;
        float t = duration <= 0f ? 1f : Mathf.Clamp01(thrustElapsed / duration);
        transform.position = Vector3.Lerp(thrustStart, thrustEnd, t);
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

    void ApplyGravity(float dt)
    {
        if (verticalVelocity <= 0f && TryGround(out float groundY))
        {
            verticalVelocity = 0f;
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
        }
        else
        {
            transform.position += Vector3.up * dy;
        }
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
        outlineLines = new LineRenderer[6];
        outlineLines[0] = CreateLine("ThrustOutlineBottom", 0.02f);
        outlineLines[1] = CreateLine("ThrustOutlineTop", 0.02f);
        for (int i = 0; i < 4; i++)
            outlineLines[2 + i] = CreateLine("ThrustOutlineSide" + i, 0.02f);
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

        if (outlineLines == null)
            return;

        for (int i = 0; i < outlineLines.Length; i++)
        {
            if (outlineLines[i] == null)
                continue;
            outlineLines[i].startColor = color;
            outlineLines[i].endColor = color;
        }
    }

    void ShowOutline(Vector3 feet)
    {
        if (outlineLines == null)
            return;

        float radius = Mathf.Max(0.05f, bodyRadius);
        float height = Mathf.Max(bodyHeight, radius * 2f + 0.01f);
        float bottom = 0.04f;
        float top = bottom + (height - radius * 2f);
        FillRing(outlineLines[0], feet + Vector3.up * bottom, radius);
        FillRing(outlineLines[1], feet + Vector3.up * top, radius);
        for (int i = 0; i < 4; i++)
        {
            float angle = i * Mathf.PI * 0.5f;
            Vector3 radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
            LineRenderer side = outlineLines[2 + i];
            side.enabled = true;
            side.positionCount = 2;
            side.SetPosition(0, feet + Vector3.up * bottom + radial);
            side.SetPosition(1, feet + Vector3.up * top + radial);
        }
    }

    static void FillRing(LineRenderer line, Vector3 center, float radius)
    {
        if (line == null)
            return;

        line.enabled = true;
        line.positionCount = RingSegments;
        for (int i = 0; i < RingSegments; i++)
        {
            float angle = i * Mathf.PI * 2f / (RingSegments - 1);
            line.SetPosition(i, center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius);
        }
    }

    void HideOutline()
    {
        if (outlineLines == null)
            return;
        for (int i = 0; i < outlineLines.Length; i++)
        {
            if (outlineLines[i] != null)
                outlineLines[i].enabled = false;
        }
    }

    void HidePreview()
    {
        if (arcLine != null)
            arcLine.enabled = false;
        HideOutline();
        var meter = GetComponent<Hd2ThrustMeter>();
        if (meter != null)
            meter.Hide();
    }
}
