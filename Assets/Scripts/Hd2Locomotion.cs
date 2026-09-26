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
/// - Thrust: hold left primary (X) or right primary (A). An arc is drawn from that hand and stops
///   at the first solid hit. Release moves the root in a straight line to that point at
///   thrustSpeed, phasing through geometry. No hit, a hit past thrustMaxDistance, or a hit on
///   Hd2NoLanding does nothing.
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
    public float arcGravity = 18f;
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

    LineRenderer arcLine;
    LineRenderer[] outlineLines;
    Material previewMaterial;

    static readonly Color railArcColor = new Color(0.2f, 0.45f, 1f, 1f);
    static readonly Color legalArcColor = new Color(0.2f, 0.92f, 0.28f, 1f);
    static readonly Color illegalArcColor = new Color(1f, 0.92f, 0.12f, 1f);

    public bool IsGrinding => grinding;
    public bool IsThrusting => thrusting;

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
        if (!TryGetAim(out Vector3 origin, out Vector3 direction))
        {
            HidePreview();
            return;
        }

        SimulateArc(origin, direction);
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

        Color color = illegalArcColor;
        if (aimKind == AimKind.Rail)
            color = railArcColor;
        else if (aimKind == AimKind.Legal)
            color = legalArcColor;
        ApplyPreviewColor(color);
        if (aimShowOutline)
            ShowOutline(aimLanding);
        else
            HideOutline();
    }

    bool TryGetAim(out Vector3 origin, out Vector3 direction)
    {
        origin = transform.position + Vector3.up;
        direction = Vector3.forward;
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
        if (handAim && Mathf.Abs(arcLaunchAngle) > 0.01f)
        {
            direction = Quaternion.AngleAxis(arcLaunchAngle, aim.right) * direction;
            if (direction.sqrMagnitude < 0.0001f)
                direction = aim.forward;
            direction.Normalize();
        }

        if (origin.y < 0.08f)
            origin.y = 0.08f;
        origin += direction * 0.08f;
        return true;
    }

    void SimulateArc(Vector3 origin, Vector3 direction)
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
            velocity += Vector3.down * gravityScale * step;

            if (Physics.Linecast(pos, next, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore))
            {
                Vector3 landing = LandingFromHit(hit, out Hd2GrindRail rail, out float railSign);
                bool inRange = (landing - player).sqrMagnitude <= maxSqr;
                bool blocked = IsNoLanding(hit.collider);
                if (arcPointCount < ArcPointCapacity)
                    arcPoints[arcPointCount++] = hit.point;

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

                return;
            }

            if ((next - player).sqrMagnitude > maxSqr)
            {
                if (arcPointCount < ArcPointCapacity)
                    arcPoints[arcPointCount++] = ClipToRange(pos, next, player, maxSqr);
                return;
            }

            pos = next;
            if (arcPointCount < ArcPointCapacity)
                arcPoints[arcPointCount++] = pos;
        }
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
        delta.y = 0f;
        for (int slide = 0; slide < 2; slide++)
        {
            if (delta.sqrMagnitude < 0.0000001f)
                return;

            float distance = delta.magnitude;
            Vector3 direction = delta / distance;
            CapsuleEnds(out Vector3 bottom, out Vector3 top);
            if (Physics.CapsuleCast(bottom, top, bodyRadius * 0.9f, direction, out RaycastHit hit, distance + skinWidth, ~0, QueryTriggerInteraction.Ignore))
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
        if (Physics.CapsuleCast(bottom, top, bodyRadius * 0.9f, direction, out RaycastHit hit, distance + skinWidth, ~0, QueryTriggerInteraction.Ignore))
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
        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 0.2f + groundProbe, ~0, QueryTriggerInteraction.Ignore))
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
        float bottom = radius;
        float top = height - radius;
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
    }
}
