using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

/// <summary>
/// Moves the VR player root (the tracking origin). Never writes the camera local pose.
/// CameraOffset local Y stays 0.
///
/// Quest controls:
/// - Left stick: walk, head-yaw relative (stick up = head forward on the floor plane).
/// - Right stick left/right: snap turn around the headset. One snap per deflection; the stick
///   must return near center and stay there briefly before the next snap. Holding the stick does not spin.
/// - Sprint: left stick click held, and only while there is move input. Stick magnitude does not sprint.
/// - Thrust: hold left primary (X) or right primary (A). An arc is drawn from that hand
///   and stays parented to it while the body moves. If the
///   arc ends in the air or on a wall, a straight line drops to the surface below and the marker
///   sits there. If a floor at least 2 m higher is found by going up 0.4 m from that drop and
///   then 0.2 m farther from the player, the marker sits on that floor and the drop line is hidden.
///   A second drop 0.2 m closer to the player cancels that upper floor when it lands at the
///   same height, which is a floor seen from underneath rather than a cliff lip.
///   The arc stops
///   at the first solid hit. A hand inside a wall hides the pointer, so the release
///   cannot start a thrust from inside that wall. Release moves the root toward that point at
///   thrustSpeed, phasing through geometry. No hit, a hit past thrustMaxDistance, a hit on
///   Hd2NoLanding, or a hit on water does nothing. The pointer stays red on water.
///   The aim arc passes a spawn shield only when this body is on that shield's team.
///   The shield still stops shots from outside.
///   Arc color: the rail's own color on a grind rail, green on other legal landings, red when the landing
///   is illegal or there is no valid hit. A 0.5 m tube around the arc and the drop line is not drawn.
///   If that tube touches a grind rail, the marker and the line end on that rail instead of the ground.
///   The arc stops at thrustMaxDistance instead of turning red
///   for being too far. With less than one thrust charge the lines keep that color,
///   and the robot preview is hidden.
///   The arc, landing wireframe, and charge marks render only for this player's camera.
/// - Grind: only by thrusting onto an Hd2GrindRail. Stay on until the rail ends (then gravity)
///   or until the next thrust starts. Walking onto a rail steps up onto it or over it.
///   Grip is not grind. A sprint press while grinding raises speed by 20% for 2 seconds.
///   Another press during that burst does nothing. It does not stack.
/// - Jump: hold B. Smoke vents from the two backpack nozzles. While the button is
///   held, thrust remains, and 0.3 seconds have not passed, vertical speed accelerates
///   upward at 3 times gravity. That change is added to the current vertical speed, so
///   a jump while falling slows the drop before it climbs. Thrust is spent at 1 bar per
///   second of burn. Releasing B, running out of thrust, or reaching 0.3 seconds ends
///   the burn, and gravity then acts on whatever vertical speed is left. Touching the
///   ground clears the jump so it can be used again. It can start in the air, while
///   falling, or while grinding. Grinding ends and the rail's horizontal speed is
///   dropped. A second jump in the air does not start.
///   Walk, sprint, and a blitz still work in the air and on a rail. There is no double jump.
/// - Crouch: right stick click. The view eases down until the avatar's butt is just
///   above its feet, and the knees bend with it. Walk speed is halved.
///   Jump does not start while crouched. Standing back up waits if the ceiling is too low.
///   A player shorter than 1.7 m, including someone seated, is lifted so the floor stays
///   1.7 m below the headset. A player taller than 1.9 m is lowered so the floor stays
///   1.9 m below. The avatar is 1.9 m with straight legs and only bends its knees
///   below that. It does not grow or shrink.
///
/// Editor fallback when no headset is running: WASD / arrows walk, Left Shift sprint,
/// Q snap left, E snap right (one snap per key press),
/// hold Space or left mouse to aim an arc along the view, release to thrust.
/// C is the jump. Left Ctrl toggles crouch.
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
    public float arcGravity = 37.1f;
    [Tooltip("Offset of the thrust arc from the gun aim. -64.5 lifts the arc slightly above the barrel.")]
    public float arcLaunchAngle = -64.5f;

    [Header("Grind")]
    public float grindSpeed = 8f;
    const float GrindBoostScale = 1.2f;
    const float GrindBoostSeconds = 2f;

    [Header("Jump")]
    [Tooltip("Upward acceleration while the jump burn is held, in multiples of gravity.")]
    public float jumpAccelGs = 3f;
    [Tooltip("Longest the jump burn can run, in seconds. Thrust is spent at jumpThrustPerSecond for this whole burn.")]
    public float jumpMaxTime = 0.3f;
    [Tooltip("Thrust bars spent per second while rising.")]
    public float jumpThrustPerSecond = 1f;

    [Header("Team")]
    [Tooltip("Blitz sparkle color. Use red or blue for a team. Gold is the demo default.")]
    public Color teamColor = new Color(1f, 0.76f, 0.22f, 1f);
    [Tooltip("Spawn shields of this team let this player through. None is stopped by red and blue shields.")]
    public Hd2Team team;

    [Header("Body")]
    public float bodyRadius = 0.25f;
    [Tooltip("Capsule height. Play mode sets this from the headset above the floor.")]
    public float bodyHeight = 1.7f;
    public float gravity = 15f;
    public float terminalVelocity = 25f;
    [Tooltip("Seconds of uninterrupted falling before the player dies.")]
    public float fallDeathSeconds = 5f;
    public float skinWidth = 0.05f;
    public float groundProbe = 0.3f;
    [Tooltip("Floor changes this tall or shorter are walked over. Taller faces still stop the player.")]
    public float stepHeight = 0.5f;

    const int ArcPointCapacity = 64;
    const float SnapEngage = 0.6f;
    const float SnapRearm = 0.3f;
    const float SnapRearmDelay = 0.1f;

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
    float snapCenterHeld;
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
    AudioSource jumpThrustSource;
    float jumpThrustFade;
    const float JumpThrustFadeTime = 0.75f;

    public bool MovementLocked { get; set; }

    static readonly List<Hd2Locomotion> active = new List<Hd2Locomotion>();

    public static int ActiveCount => active.Count;

    public static Hd2Locomotion ActiveAt(int index)
    {
        return active[index];
    }

    public bool OnRail(Hd2GrindRail rail)
    {
        return grinding && grindRail == rail;
    }

    public float RailDistance => grindDistance;

    public float RailDirection => grindSign;

    /// <summary>
    /// Leaves the rail and stands at a deck point, as if the rider stepped off the back.
    /// </summary>
    public void StepOffRail(Vector3 feet)
    {
        grinding = false;
        grindRail = null;
        grindBoostUntil = -1f;
        thrusting = false;
        thrustRail = null;
        verticalVelocity = 0f;
        fallTime = 0f;
        wasOffGround = false;
        transform.position = feet;
        UpdateGrindAudio();
    }

    public void HaltTravel()
    {
        thrusting = false;
        grinding = false;
        grindRail = null;
        grindBoostUntil = -1f;
        thrustRail = null;
        verticalVelocity = 0f;
        fallTime = 0f;
        wasOffGround = false;
        EndJumpBoost();
        StopBlitzTrail();
        jumpSpent = false;
        UpdateGrindAudio();
    }
    float grindSign = 1f;
    float grindDistance;
    float grindBoostUntil = -1f;

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
    static readonly Collider[] railGrab = new Collider[32];
    static readonly RaycastHit[] bodyHits = new RaycastHit[24];

    LineRenderer arcLine;
    LineRenderer[] wireLines;
    MeshFilter[] wireParts;
    readonly Vector3[] wireCorners = new Vector3[8];
    GameObject thrustGhost;
    Renderer[] ghostRenderers = System.Array.Empty<Renderer>();
    Camera ownerCamera;
    Material wireMaterial;
    GameObject heldGhostRoot;
    Renderer[] heldSources = System.Array.Empty<Renderer>();
    Transform[] heldClones = System.Array.Empty<Transform>();
    Mesh[] heldBakes = System.Array.Empty<Mesh>();
    int[][] heldRemaps = System.Array.Empty<int[]>();
    Vector3[][] heldWireVerts = System.Array.Empty<Vector3[]>();
    readonly List<Renderer> heldScan = new List<Renderer>();
    readonly List<MeshRenderer> heldScratch = new List<MeshRenderer>();
    readonly List<SkinnedMeshRenderer> skinScratch = new List<SkinnedMeshRenderer>();
    readonly List<Vector3> bakeScratch = new List<Vector3>();
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
    public bool PassingFront { get; set; }

    /// <summary>
    /// Guns stay quiet through a payload shield, while an arm or the gun is touching a shield,
    /// or while the shooter is inside a wall.
    /// </summary>
    public static bool ShotBlocked(Component gun, Vector3 muzzle)
    {
        if (Hd2PayloadShield.BlocksShot(gun, muzzle))
            return true;
        if (TouchesShield(gun, muzzle))
            return true;

        Transform owner = Hd2SpawnField.FighterBody(gun);
        if (owner == null)
            return false;
        Hd2Locomotion locomotion = owner.GetComponent<Hd2Locomotion>();
        if (locomotion == null)
            return false;
        return locomotion.EmbeddedInWall() || locomotion.PointPastWall(muzzle);
    }

    const float ShieldTouchRadius = 0.05f;
    static readonly Vector3[] shieldPoints = new Vector3[64];
    static readonly List<Renderer> shieldGunRenderers = new List<Renderer>(12);

    static bool TouchesShield(Component gun, Vector3 muzzle)
    {
        int count = AddShieldPoint(shieldPoints, 0, muzzle);
        if (gun != null)
        {
            count = AddShieldPoint(shieldPoints, count, gun.transform.position);
            shieldGunRenderers.Clear();
            gun.GetComponentsInChildren(false, shieldGunRenderers);
            for (int i = 0; i < shieldGunRenderers.Count; i++)
            {
                Renderer renderer = shieldGunRenderers[i];
                if (renderer == null || renderer is LineRenderer || renderer is ParticleSystemRenderer || !renderer.enabled)
                    continue;
                count = AddBounds(renderer.bounds, shieldPoints, count);
            }

            Transform owner = Hd2SpawnField.FighterBody(gun);
            if (owner != null)
            {
                Hd2Body avatar = owner.GetComponent<Hd2Body>();
                if (avatar != null)
                {
                    count = AddArm(avatar.upperArmL, avatar.forearmL, shieldPoints, count);
                    count = AddArm(avatar.forearmL, avatar.handL, shieldPoints, count);
                    count = AddArm(avatar.upperArmR, avatar.forearmR, shieldPoints, count);
                    count = AddArm(avatar.forearmR, avatar.handR, shieldPoints, count);
                }

                Hd2EnemyBot bot = owner.GetComponent<Hd2EnemyBot>();
                if (bot != null)
                    count = bot.CopyArmPoints(shieldPoints, count);

                Hd2Locomotion locomotion = owner.GetComponent<Hd2Locomotion>();
                if (locomotion != null)
                {
                    if (locomotion.leftHand != null)
                        count = AddShieldPoint(shieldPoints, count, locomotion.leftHand.position);
                    if (locomotion.rightHand != null)
                        count = AddShieldPoint(shieldPoints, count, locomotion.rightHand.position);
                }
            }
        }

        for (int i = 0; i < count; i++)
        {
            if (Hd2SpawnField.Touches(shieldPoints[i], ShieldTouchRadius))
                return true;
            if (Hd2PayloadShield.Touches(shieldPoints[i], ShieldTouchRadius))
                return true;
        }

        return false;
    }

    static int AddArm(Transform from, Transform to, Vector3[] points, int count)
    {
        if (from == null || to == null || count >= points.Length - 2)
            return count;
        points[count++] = from.position;
        points[count++] = Vector3.Lerp(from.position, to.position, 0.5f);
        if (count < points.Length)
            points[count++] = to.position;
        return count;
    }

    static int AddBounds(Bounds bounds, Vector3[] points, int count)
    {
        count = AddShieldPoint(points, count, bounds.center);
        Vector3 c = bounds.center;
        Vector3 e = bounds.extents;
        for (int x = -1; x <= 1; x += 2)
        {
            for (int y = -1; y <= 1; y += 2)
            {
                for (int z = -1; z <= 1; z += 2)
                    count = AddShieldPoint(points, count, c + Vector3.Scale(e, new Vector3(x, y, z)));
            }
        }

        return count;
    }

    static int AddShieldPoint(Vector3[] points, int count, Vector3 point)
    {
        if (count >= points.Length)
            return count;
        points[count] = point;
        return count + 1;
    }
    public const float MinStandHeight = 1.7f;
    public const float MaxStandHeight = 1.9f;
    // Deepest squat. Robot at scale 1: head bone 1.580, hips 0.920, butt 0.080 below
    // the hips, foot mesh top 0.156. Standing scale puts the head bone on a 1.9 m
    // headset. The butt then sits 2 cm above the foot top.
    // 1.9 * (0.156 + 0.02 + 0.08 + 1.580 - 0.920) / 1.580 = 1.10
    public const float MinCrouchEye = 1.10f;

    bool crouched;
    float crouchBlend;
    float standingEye = 1.7f;
    bool stanceReady;
    Transform crouchRig;
    Transform roomRig;
    public bool IsAirborne => jumpBoosting || jumpSpent;

    public bool IsCrouching => crouched;
    public float CrouchAmount => crouchBlend;
    public float StandingEye => standingEye;

    public bool IsSprinting
    {
        get
        {
            if (crouched || !SprintHeld())
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
        if (GetComponent<Hd2Hud>() == null)
            gameObject.AddComponent<Hd2Hud>();
        CreateGrindLoop();
        CreateWalkLoop();
        CreateLandSound();
        CreateBlitzSound();
        CreateJumpThrust();
    }

    void Start()
    {
        if (FindFirstObjectByType<Hd2Deathmatch>() != null)
            return;

        if (!Hd2Shootout.Present)
        {
            team = Hd2Team.Blue;
            teamColor = BlueArmor;
            PaintTeamRoster();
        }

        PlantOnFloor();
        var health = GetComponent<Hd2Health>();
        if (health != null)
            health.RememberSpawn();
        Hd2TeleportArrive.Play(transform.position);
    }

    public static readonly Color BlueArmor = new Color(0.16f, 0.38f, 0.95f, 1f);
    public static readonly Color RedArmor = new Color(0.82f, 0.1f, 0.08f, 1f);

    static readonly Color[] BlueShades =
    {
        new Color(0.16f, 0.38f, 0.95f, 1f),
        new Color(0.25f, 0.88f, 1.00f, 1f),
        new Color(0.05f, 0.12f, 0.42f, 1f),
        new Color(0.42f, 0.28f, 0.98f, 1f),
        new Color(0.62f, 0.84f, 1.00f, 1f),
        new Color(0.05f, 0.55f, 0.68f, 1f)
    };

    static readonly Color[] RedShades =
    {
        new Color(0.92f, 0.08f, 0.06f, 1f),
        new Color(1.00f, 0.48f, 0.05f, 1f),
        new Color(0.38f, 0.02f, 0.04f, 1f),
        new Color(0.95f, 0.12f, 0.48f, 1f),
        new Color(0.70f, 0.28f, 0.08f, 1f),
        new Color(1.00f, 0.42f, 0.38f, 1f)
    };

    public static Color ArmorOf(Hd2Team side)
    {
        return side == Hd2Team.Red ? RedArmor : BlueArmor;
    }

    public static Color BlueShade(int index)
    {
        return Shade(BlueShades, index);
    }

    public static Color RedShade(int index)
    {
        return Shade(RedShades, index);
    }

    static Color Shade(Color[] shades, int index)
    {
        int slot = index % shades.Length;
        if (slot < 0)
            slot += shades.Length;
        return shades[slot];
    }

    /// <summary>
    /// Gives each teammate their own shade of the team color. A shade already chosen
    /// is left alone, so a respawn keeps the color from the start of the match.
    /// </summary>
    public static void PaintTeamRoster()
    {
        if (FindFirstObjectByType<Hd2Deathmatch>() != null)
            return;

        PaintSide(Hd2Team.Blue);
        PaintSide(Hd2Team.Red);
    }

    static void PaintSide(Hd2Team side)
    {
        Color armor = ArmorOf(side);
        int next = 0;
        var players = FindObjectsByType<Hd2Locomotion>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        System.Array.Sort(players, (a, b) => string.CompareOrdinal(a.name, b.name));
        for (int i = 0; i < players.Length; i++)
        {
            Hd2Locomotion player = players[i];
            if (player == null || player.team != side)
                continue;
            if (IsBaseColor(player.teamColor, armor))
                player.teamColor = TeamShade(side, next);
            var avatar = player.GetComponent<Hd2Body>();
            if (avatar != null)
                avatar.SetArmorColor(player.teamColor);
            next++;
        }

        var bots = FindObjectsByType<Hd2EnemyBot>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        System.Array.Sort(bots, (a, b) =>
        {
            int slot = a.spawnSlot.CompareTo(b.spawnSlot);
            if (slot != 0)
                return slot;
            return string.CompareOrdinal(a.name, b.name);
        });
        for (int i = 0; i < bots.Length; i++)
        {
            Hd2EnemyBot bot = bots[i];
            if (bot == null || bot.team != side)
                continue;
            if (IsBaseColor(bot.bodyColor, armor))
            {
                bot.bodyColor = TeamShade(side, next);
                bot.useBodyColor = true;
            }

            bot.ApplyBodyColor();
            next++;
        }
    }

    static Color TeamShade(Hd2Team side, int index)
    {
        return side == Hd2Team.Red ? RedShade(index) : BlueShade(index);
    }

    static bool IsBaseColor(Color color, Color armor)
    {
        return Mathf.Abs(color.r - armor.r) < 0.02f
            && Mathf.Abs(color.g - armor.g) < 0.02f
            && Mathf.Abs(color.b - armor.b) < 0.02f;
    }

    public bool CanSwitchTeam()
    {
        if (FindFirstObjectByType<Hd2Deathmatch>() != null)
            return false;

        Hd2Team next = OtherTeam(team);
        if (Hd2Shootout.Present && HumanCount(next) >= Hd2Shootout.RosterSize)
            return false;
        return BotOn(next) != null;
    }

    public static int HumanCount(Hd2Team side)
    {
        var players = FindObjectsByType<Hd2Locomotion>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        int count = 0;
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] != null && players[i].team == side)
                count++;
        }

        return count;
    }

    public void SwitchTeam()
    {
        if (!CanSwitchTeam())
            return;

        Hd2Team next = OtherTeam(team);
        Hd2EnemyBot bot = BotOn(next);
        if (bot == null)
            return;

        Hd2Team previous = team;
        Transform mySpawn = HomeSpawn(previous);
        int mySlot = SpawnSlot(mySpawn, previous);
        Transform theirSpawn = bot.Home;
        if (theirSpawn == null)
            theirSpawn = bot.transform;

        Color botColor = TeamShade(previous, bot.spawnSlot + 1);
        bot.TakePlace(previous, botColor, mySlot, mySpawn);

        team = next;
        teamColor = TeamShade(next, 0);
        var avatar = GetComponent<Hd2Body>();
        if (avatar != null)
            avatar.SetArmorColor(teamColor);

        var life = GetComponent<Hd2Health>();
        if (life != null)
            life.PinSpawn(theirSpawn.position, theirSpawn.rotation);

        var death = GetComponent<Hd2PlayerDeath>();
        if (death != null)
            death.DieImmediately();
        else if (life != null)
            life.Respawn();
    }

    /// <summary>
    /// Leave the match. A bot respawns on the vacated base. The last human ends the match.
    /// </summary>
    public void LeaveMatch()
    {
        var humans = FindObjectsByType<Hd2Locomotion>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        if (humans != null && humans.Length > 1)
        {
            SpawnReplacement();
            Hd2VisionCone.Unregister(transform);
            Destroy(gameObject);
            return;
        }

        EndMatch();
    }

    public static void EndMatch()
    {
        var shootout = FindFirstObjectByType<Hd2Shootout>();
        if (shootout != null)
            shootout.StopMatch();

        Hd2Session.CloseRoom();
    }

    void SpawnReplacement()
    {
        Hd2EnemyBot template = FindFirstObjectByType<Hd2EnemyBot>();
        Transform spot = HomeSpawn(team);
        var go = new GameObject(team == Hd2Team.Red ? "Red bot" : team == Hd2Team.Blue ? "Blue bot" : "Bot");
        if (template != null && template.transform.parent != null)
            go.transform.SetParent(template.transform.parent, false);
        if (spot != null)
            go.transform.SetPositionAndRotation(spot.position, spot.rotation);

        var bot = go.AddComponent<Hd2EnemyBot>();
        if (template != null)
        {
            bot.robotPrefab = template.robotPrefab;
            bot.pistolModel = template.pistolModel;
            bot.domination = template.domination;
            bot.objectiveWeight = template.objectiveWeight;
            bot.retreatFraction = template.retreatFraction;
            bot.retreatHealPerSecond = template.retreatHealPerSecond;
        }
        else if (FindFirstObjectByType<Hd2Domination>() != null || FindFirstObjectByType<Hd2ControlPoint>() != null)
            bot.domination = true;

        bot.team = team;
        bot.bodyColor = teamColor;
        bot.useBodyColor = true;
        bot.spawnSlot = SpawnSlot(spot, team);
        bot.displayName = team == Hd2Team.Red ? "red bot" : team == Hd2Team.Blue ? "blue bot" : "bot";
        if (spot != null)
            bot.TakePlace(team, teamColor, bot.spawnSlot, spot);
    }

    Transform HomeSpawn(Hd2Team side)
    {
        var life = GetComponent<Hd2Health>();
        if (life != null && life.TryPinnedSpawn(out Vector3 pinned, out _))
        {
            Transform home = NearestSpawn(side, pinned);
            if (home != null)
                return home;
        }

        return NearestSpawn(side, transform.position);
    }

    /// <summary>
    /// Stand this player at their team's base spawn with full health.
    /// </summary>
    public void ReturnToBase()
    {
        Transform spawn = NearestSpawn(team, transform.position);
        var life = GetComponent<Hd2Health>();
        if (spawn != null && life != null)
            life.PinSpawn(spawn.position, spawn.rotation);

        var death = GetComponent<Hd2PlayerDeath>();
        if (death != null)
            death.RecoverAtSpawn();
        else if (life != null)
            life.Respawn();
    }

    static Hd2Team OtherTeam(Hd2Team side)
    {
        return side == Hd2Team.Red ? Hd2Team.Blue : Hd2Team.Red;
    }

    static Hd2EnemyBot BotOn(Hd2Team side)
    {
        if (side != Hd2Team.Red && side != Hd2Team.Blue)
            return null;

        var bots = FindObjectsByType<Hd2EnemyBot>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        Hd2EnemyBot waiting = null;
        for (int i = 0; i < bots.Length; i++)
        {
            Hd2EnemyBot bot = bots[i];
            if (bot == null || bot.team != side)
                continue;
            var life = bot.GetComponent<Hd2Health>();
            if (life == null || !life.IsDead)
                return bot;
            if (waiting == null)
                waiting = bot;
        }

        return waiting;
    }

    static Transform NearestSpawn(Hd2Team side, Vector3 from)
    {
        var points = FindObjectsByType<Hd2SpawnPoint>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        Transform best = null;
        float bestSqr = float.MaxValue;
        for (int i = 0; i < points.Length; i++)
        {
            Hd2SpawnPoint point = points[i];
            if (point == null || point.team != side)
                continue;
            float sqr = (point.transform.position - from).sqrMagnitude;
            if (sqr >= bestSqr)
                continue;
            bestSqr = sqr;
            best = point.transform;
        }

        return best;
    }

    static int SpawnSlot(Transform spawn, Hd2Team side)
    {
        if (spawn == null)
            return 0;

        var points = FindObjectsByType<Hd2SpawnPoint>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        int slot = 0;
        int index = int.MaxValue;
        for (int i = 0; i < points.Length; i++)
        {
            if (points[i] != null && points[i].transform == spawn)
            {
                index = points[i].index;
                break;
            }
        }

        for (int i = 0; i < points.Length; i++)
        {
            Hd2SpawnPoint point = points[i];
            if (point == null || point.transform == spawn || point.team != side)
                continue;
            if (point.index < index || (point.index == index && point.transform.GetSiblingIndex() < spawn.GetSiblingIndex()))
                slot++;
        }

        return slot;
    }

    void PlantOnFloor()
    {
        Vector3 origin = transform.position + Vector3.up * 0.2f;
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 80f, ~0, QueryTriggerInteraction.Ignore);
        float nearest = float.MaxValue;
        float floorY = transform.position.y;
        bool found = false;
        for (int i = 0; i < hits.Length; i++)
        {
            Collider collider = hits[i].collider;
            if (collider == null || collider.transform == transform || collider.transform.IsChildOf(transform))
                continue;
            if (hits[i].normal.y < 0.5f || hits[i].distance >= nearest)
                continue;
            if (collider.GetComponentInParent<Hd2Water>() != null)
                continue;
            nearest = hits[i].distance;
            floorY = hits[i].point.y;
            found = true;
        }

        if (!found || transform.position.y - floorY < 0.2f)
            return;

        Vector3 planted = transform.position;
        planted.y = floorY;
        transform.position = planted;
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
        grindLoopSource.volume = 0.5f;
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
        if (!active.Contains(this))
            active.Add(this);

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
        sprintAction.AddBinding("<Keyboard>/leftShift");
        sprintAction.Enable();

        leftThrustAction = new InputAction("Hd2ThrustLeft", InputActionType.Button);
        leftThrustAction.Enable();

        rightThrustAction = new InputAction("Hd2ThrustRight", InputActionType.Button);
        rightThrustAction.Enable();

        viewThrustAction = new InputAction("Hd2ThrustView", InputActionType.Button);
        viewThrustAction.AddBinding("<Keyboard>/space");
        viewThrustAction.AddBinding("<Mouse>/leftButton");
        viewThrustAction.Enable();

        jumpAction = new InputAction("Hd2Jump", InputActionType.Button);
        jumpAction.AddBinding("<Keyboard>/c");
        jumpAction.Enable();

        Vector3 euler = transform.eulerAngles;
        editorYaw = euler.y;
        editorPitch = -18f;

        RenderPipelineManager.beginCameraRendering += CullPreviewToOwner;
        RenderPipelineManager.endCameraRendering += RestorePreviewAfterCamera;
    }

    void OnDisable()
    {
        active.Remove(this);
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
        RenderPipelineManager.beginCameraRendering -= CullPreviewToOwner;
        RenderPipelineManager.endCameraRendering -= RestorePreviewAfterCamera;
        SetPreviewForcedOff(false);
    }

    void OnDestroy()
    {
        if (previewMaterial != null)
            Destroy(previewMaterial);
        if (wireMaterial != null)
            Destroy(wireMaterial);
        if (thrustGhost != null)
            Destroy(thrustGhost);
        ClearHeldGhost();
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

        UpdateCrouch(dt);

        if (MovementLocked)
        {
            aimSource = AimSource.None;
            aimValid = false;
            HidePreview();
            if (EditorFallback)
                EditorLook();
            PlaceCrouchRig();
            return;
        }

        UpdateJumpThrustAudio(dt);

        float snap = ReadSnapDegrees();
        if (EditorFallback)
            editorYaw += snap;

        EditorLook();

        if (!EditorFallback && Mathf.Abs(snap) > 0.01f)
            YawAroundHead(snap);
        PlaceCrouchRig();
        AlignRoom();

        if (thrusting)
        {
            HidePreview();
            AdvanceThrust(dt);
            UpdateGrindAudio();
            DrownIfTouchingWater();
            return;
        }

        AimSource sample = UpdateAimSource(out bool released);
        if (released && aimSource == AimSource.None && aimValid)
        {
            HidePreview();
            if (!TrackedAimInsideWall(sample) && BeginThrust())
            {
                AdvanceThrust(dt);
                UpdateGrindAudio();
                DrownIfTouchingWater();
                return;
            }
        }

        bool planted = FeetPlanted();
        if (planted && !jumpBoosting)
            jumpSpent = false;

        if (CrouchPressed() && (!crouched || RoomToStand()))
            crouched = !crouched;

        if (!crouched && !jumpSpent && !jumpBoosting && JumpPressed())
            BeginJump();

        if (grinding && grindRail != null && !jumpBoosting)
        {
            if (SprintPressed() && Time.time >= grindBoostUntil)
                grindBoostUntil = Time.time + GrindBoostSeconds;
            if (TickGrind(dt))
            {
                UpdateGrindAudio();
                DrownIfTouchingWater();
                ShowHeldAim(sample);
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
        float burn = jumpBoosting ? BurnJump(dt) : 0f;
        ApplyGravity(dt, burn);
        PopOutOfWalls();
        NoteLanding();
        UpdateGrindAudio();
        DrownIfTouchingWater();
        ShowHeldAim(sample);
    }

    void ShowHeldAim(AimSource sample)
    {
        if (sample == AimSource.None || thrusting)
        {
            if (!thrusting)
                HidePreview();
            return;
        }

        AimSource held = aimSource;
        aimSource = sample;
        UpdateAimArc();
        aimSource = held;
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
        if (PointerReleased(true))
            releasedSource = AimSource.Right;
        else if (PointerReleased(false))
            releasedSource = AimSource.Left;
        else if (viewThrustAction != null && viewThrustAction.WasReleasedThisFrame())
            releasedSource = AimSource.View;

        if (PointerPressed(true))
            aimSource = AimSource.Right;
        else if (PointerPressed(false))
            aimSource = AimSource.Left;
        else if (viewThrustAction != null && viewThrustAction.WasPressedThisFrame())
            aimSource = AimSource.View;

        if (!SourceHeld(aimSource))
        {
            if (PointerHeld(true))
                aimSource = AimSource.Right;
            else if (PointerHeld(false))
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

    Hd2ControlMap Controls()
    {
        return GetComponent<Hd2ControlMap>();
    }

    bool SprintHeld()
    {
        if (sprintAction != null && sprintAction.IsPressed())
            return true;
        var map = Controls();
        return map != null && map.Held(Hd2ControlMap.Action.Sprint);
    }

    bool SprintPressed()
    {
        if (sprintAction != null && sprintAction.WasPressedThisFrame())
            return true;
        var map = Controls();
        return map != null && map.Pressed(Hd2ControlMap.Action.Sprint);
    }

    bool CrouchPressed()
    {
        if (Controls() != null && Controls().Pressed(Hd2ControlMap.Action.Crouch))
            return true;
        return EditorFallback && Keyboard.current != null && Keyboard.current.leftCtrlKey.wasPressedThisFrame;
    }

    bool JumpPressed()
    {
        if (jumpAction != null && jumpAction.WasPressedThisFrame())
            return true;
        var map = Controls();
        return map != null && map.Pressed(Hd2ControlMap.Action.Jump);
    }

    bool JumpHeld()
    {
        if (crouched)
            return false;
        if (jumpAction != null && jumpAction.IsPressed())
            return true;
        var map = Controls();
        return map != null && map.Held(Hd2ControlMap.Action.Jump);
    }

    bool PointerPressed(bool right)
    {
        var map = Controls();
        if (map == null)
            return false;
        return map.Pressed(right ? Hd2ControlMap.Action.RightPointer : Hd2ControlMap.Action.LeftPointer);
    }

    bool PointerReleased(bool right)
    {
        var map = Controls();
        if (map == null)
            return false;
        return map.Released(right ? Hd2ControlMap.Action.RightPointer : Hd2ControlMap.Action.LeftPointer);
    }

    bool PointerHeld(bool right)
    {
        var map = Controls();
        if (map == null)
            return false;
        return map.Held(right ? Hd2ControlMap.Action.RightPointer : Hd2ControlMap.Action.LeftPointer);
    }

    bool SourceHeld(AimSource source)
    {
        switch (source)
        {
            case AimSource.Right:
                return PointerHeld(true);
            case AimSource.Left:
                return PointerHeld(false);
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

        if (TrackedAimInsideWall(aimSource))
        {
            HidePreview();
            return;
        }

        SimulateArc(origin, direction, bend);
        float reach = Mathf.Max(0.5f, thrustMaxDistance);
        PreferRailAlongArc(transform.position, reach * reach);
        TrimArcToRange(transform.position, reach);
        PoseArcOnHand();

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
                // -64.5 on this axis lifts the arc slightly above where the gun is aiming.
                direction = Quaternion.AngleAxis(-arcLaunchAngle, aim.right) * direction;
                if (direction.sqrMagnitude < 0.0001f)
                    direction = aim.forward;
                direction.Normalize();
            }
        }

        origin += direction * 0.08f;
        return true;
    }

    static readonly Collider[] handOverlap = new Collider[16];
    static readonly RaycastHit[] handHits = new RaycastHit[16];

    bool TrackedAimInsideWall(AimSource source)
    {
        Transform aim = source == AimSource.Right ? rightHand
            : source == AimSource.Left ? leftHand
            : head;
        Vector3 point = aim != null ? aim.position : transform.position + Vector3.up;
        return PointPastWall(point);
    }

    bool PointPastWall(Vector3 point)
    {
        if (InsideSolidVolume(point))
            return true;

        // A shell the hand has crossed is between the hand and the chest. The near
        // side of that shell faces away from the hand, so the pointer stays off.
        Vector3 chest = transform.position + Vector3.up * 0.9f;
        Vector3 delta = chest - point;
        float dist = delta.magnitude;
        if (dist < 0.12f)
            return false;

        Vector3 dir = delta / dist;
        bool previous = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true;
        int count = Physics.RaycastNonAlloc(point, dir, handHits, dist, ~0, QueryTriggerInteraction.Ignore);
        Physics.queriesHitBackfaces = previous;

        float best = dist;
        bool backface = false;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            if (!CountsAsWall(handHits[i].collider) || handHits[i].distance >= best)
                continue;
            best = handHits[i].distance;
            backface = Vector3.Dot(handHits[i].normal, dir) > 0.05f;
            found = true;
        }

        return found && backface;
    }

    bool InsideSolidVolume(Vector3 point)
    {
        int count = Physics.OverlapSphereNonAlloc(point, 0.02f, handOverlap, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider collider = handOverlap[i];
            if (!CountsAsWall(collider))
                continue;
            if (collider is MeshCollider mesh && !mesh.convex)
                continue;
            if ((collider.ClosestPoint(point) - point).sqrMagnitude < 0.000001f)
                return true;
        }

        return false;
    }

    bool CountsAsWall(Collider collider)
    {
        if (collider == null || IsSelf(collider))
            return false;
        if (collider.GetComponentInParent<Hd2Health>() != null)
            return false;
        if (collider.GetComponentInParent<Hd2Water>() != null)
            return false;
        Hd2SpawnField field = collider.GetComponentInParent<Hd2SpawnField>();
        return field == null || !field.Allows(this);
    }

    void PoseArcOnHand()
    {
        if (arcLine == null)
            return;

        Transform aim = AimTransform();
        bool showLine = arcPointCount >= 2 && aim != null;
        arcLine.enabled = showLine;
        if (!showLine)
            return;

        if (arcLine.transform.parent != aim)
            arcLine.transform.SetParent(aim, false);
        arcLine.useWorldSpace = false;
        arcLine.positionCount = arcPointCount;
        for (int i = 0; i < arcPointCount; i++)
            arcLine.SetPosition(i, aim.InverseTransformPoint(arcPoints[i]));
    }

    Transform AimTransform()
    {
        if (aimSource == AimSource.Right && rightHand != null)
            return rightHand;
        if (aimSource == AimSource.Left && leftHand != null)
            return leftHand;
        if (head != null)
            return head;
        return transform;
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
                if (field != null && field.Allows(this))
                {
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

                Hd2GrindRail rail = RailFromHit(hit);
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

        if (TryUpperFloor(from, player, hit.point.y, maxSqr, out RaycastHit upper))
        {
            AcceptSurfaceHit(upper, player, maxSqr);
            return;
        }

        while (arcPointCount > 1 && arcPoints[arcPointCount - 1].y < hit.point.y - 0.02f)
            arcPointCount--;
        if (arcPointCount > 0 && arcPointCount < ArcPointCapacity
            && arcPoints[arcPointCount - 1].y - hit.point.y > 0.015f)
            arcPoints[arcPointCount++] = hit.point;

        Hd2SpawnField field = hit.collider != null ? hit.collider.GetComponentInParent<Hd2SpawnField>() : null;
        if (field != null && field.Allows(this))
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

    bool TryUpperFloor(Vector3 dropStart, Vector3 player, float primaryY, float maxSqr, out RaycastHit landing)
    {
        landing = default;
        Vector3 raised = dropStart + Vector3.up * 0.4f;
        Vector3 away = raised - player;
        away.y = 0f;
        if (away.sqrMagnitude < 0.0001f)
            return false;

        away.Normalize();
        Vector3 farOrigin = raised + away * 0.2f;
        if (!TryRayDown(farOrigin, farOrigin.y + 0.02f, out RaycastHit far))
            return false;
        if (far.normal.y <= 0.55f || IsNoLanding(far.collider))
            return false;
        if (far.point.y < primaryY + 2f)
            return false;
        if ((far.point - player).sqrMagnitude > maxSqr)
            return false;

        // The far sample is on top of a floor we are aiming through when a drop
        // back toward the player lands on that same floor. Keep the low target.
        Vector3 nearOrigin = raised - away * 0.2f;
        if (TryRayDown(nearOrigin, nearOrigin.y + 0.02f, out RaycastHit near)
            && near.normal.y > 0.55f
            && !IsNoLanding(near.collider)
            && Mathf.Abs(near.point.y - far.point.y) <= 0.35f)
            return false;

        landing = far;
        return true;
    }

    Vector3 LandingFromHit(RaycastHit hit, out Hd2GrindRail rail, out float railSign)
    {
        railSign = 1f;
        rail = RailFromHit(hit);
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
        if (collider == null)
            return false;
        return collider.GetComponentInParent<Hd2NoLanding>() != null
            || collider.GetComponentInParent<Hd2Water>() != null;
    }

    // The spawn field stays solid so outside shots hit it. This body walks through
    // a shield only when that shield allows this team. Other walls are often a
    // single face, so the cast must hit the back of that face too.
    bool CastBody(Vector3 bottom, Vector3 top, float radius, Vector3 direction, float distance, out RaycastHit hit)
    {
        bool previous = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true;
        int count = Physics.CapsuleCastNonAlloc(bottom, top, radius, direction, bodyHits, distance, ~0, QueryTriggerInteraction.Ignore);
        Physics.queriesHitBackfaces = previous;
        return NearestBodyHit(count, out hit);
    }

    bool RaycastBody(Vector3 origin, Vector3 direction, float distance, out RaycastHit hit)
    {
        bool previous = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true;
        int count = Physics.RaycastNonAlloc(origin, direction, bodyHits, distance, ~0, QueryTriggerInteraction.Ignore);
        Physics.queriesHitBackfaces = previous;
        return NearestBodyHit(count, out hit);
    }

    bool IsSelf(Collider collider)
    {
        return collider != null && (collider.transform == transform || collider.transform.IsChildOf(transform));
    }

    // The button is narrower than the body. A blitz lands inside it, and the mesh
    // then blocks every step. Hands still press it. The body walks through.
    static bool IsButton(Collider collider)
    {
        return collider != null && collider.GetComponentInParent<Hd2TeamButton>() != null;
    }

    bool NearestBodyHit(int count, out RaycastHit hit)
    {
        hit = default;
        float best = float.MaxValue;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            Collider collider = bodyHits[i].collider;
            if (IsSelf(collider) || IsButton(collider))
                continue;
            Hd2SpawnField field = collider != null ? collider.GetComponentInParent<Hd2SpawnField>() : null;
            if (field != null && field.Allows(this))
                continue;
            if (bodyHits[i].distance >= best)
                continue;

            best = bodyHits[i].distance;
            hit = bodyHits[i];
            found = true;
        }

        return found;
    }

    Hd2GrindRail RailFromHit(RaycastHit hit)
    {
        Hd2GrindRail rail = hit.collider != null ? hit.collider.GetComponentInParent<Hd2GrindRail>() : null;
        if (rail == null)
            rail = RailAtPoint(hit.point);
        if (rail == null || !grinding || rail != grindRail)
            return rail;

        Vector3 point = rail.PointAlong(rail.DistanceAlong(hit.point));
        if ((point - transform.position).sqrMagnitude < 2.25f)
            return null;
        return rail;
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

    const float RailGrabRadius = 0.5f;

    /// <summary>
    /// The drawn arc and drop stay thin. A 0.5 m tube around that same path grabs a rail
    /// the thin line missed, and the landing moves onto that rail.
    /// </summary>
    void PreferRailAlongArc(Vector3 player, float maxSqr)
    {
        if (aimRail != null || arcPointCount < 2)
            return;

        // Short pieces keep the tube on the drawn line. One capsule from mid-arc
        // to the ground would cut across and miss the drop.
        const float chunk = 0.8f;
        float traveled = 0f;
        for (int i = 1; i < arcPointCount; i++)
        {
            Vector3 from = arcPoints[i - 1];
            Vector3 to = arcPoints[i];
            float seg = Vector3.Distance(from, to);
            if (seg < 0.0001f)
                continue;

            float covered = 0f;
            while (covered < seg - 0.0001f)
            {
                float piece = Mathf.Min(chunk, seg - covered);
                float t0 = covered / seg;
                float t1 = (covered + piece) / seg;
                Vector3 a = Vector3.Lerp(from, to, t0);
                Vector3 b = Vector3.Lerp(from, to, t1);
                if (RailInTube(a, b, player, maxSqr, out Hd2GrindRail rail, out Vector3 onRail))
                {
                    float along = ClosestAlong(a, b, traveled + covered, onRail);
                    CutArcAt(along);
                    if (arcPointCount > 0 && (arcPoints[arcPointCount - 1] - onRail).sqrMagnitude > 0.01f)
                    {
                        if (arcPointCount < ArcPointCapacity)
                            arcPoints[arcPointCount++] = onRail;
                        else
                            arcPoints[arcPointCount - 1] = onRail;
                    }

                    aimLanding = onRail;
                    aimShowOutline = true;
                    aimKind = AimKind.Rail;
                    aimValid = true;
                    aimRail = rail;
                    aimGrindSign = rail.EntrySign(player, onRail);
                    return;
                }

                covered += piece;
            }

            traveled += seg;
        }
    }

    bool RailInTube(Vector3 from, Vector3 to, Vector3 player, float maxSqr, out Hd2GrindRail rail, out Vector3 onRail)
    {
        rail = null;
        onRail = to;
        Vector3 span = to - from;
        float length = span.magnitude;
        if (length < 0.0001f)
            return false;

        int count = Physics.OverlapCapsuleNonAlloc(from, to, RailGrabRadius, railGrab, ~0, QueryTriggerInteraction.Ignore);
        // The beam is about 0.18 m thick, so a centerline just outside the 0.5 m tube still counts.
        float limit = (RailGrabRadius + 0.2f) * (RailGrabRadius + 0.2f);
        float bestT = float.MaxValue;
        float bestSqr = limit;
        bool found = false;
        int samples = Mathf.Max(1, Mathf.CeilToInt(length / 0.25f));
        for (int i = 0; i < count; i++)
        {
            Collider collider = railGrab[i];
            if (collider == null || IsSelf(collider))
                continue;

            Hd2GrindRail candidate = collider.GetComponentInParent<Hd2GrindRail>();
            if (candidate == null)
                continue;

            float candidateT = 1f;
            float candidateSqr = limit;
            Vector3 candidatePoint = to;
            bool near = false;
            for (int s = 0; s <= samples; s++)
            {
                Vector3 sample = from + span * (s / (float)samples);
                Vector3 closest = candidate.PointAlong(candidate.DistanceAlong(sample));
                float sqr = (NearestOnSegment(from, to, closest) - closest).sqrMagnitude;
                if (sqr >= candidateSqr)
                    continue;

                candidateSqr = sqr;
                candidatePoint = closest;
                candidateT = ClosestAlong(from, to, 0f, closest) / length;
                near = true;
            }

            if (!near || candidateSqr >= limit)
                continue;
            if ((candidatePoint - player).sqrMagnitude > maxSqr)
                continue;
            // The beam under the rider is not a new landing. A point farther
            // along this rail, or on another rail, still is.
            if (grinding && candidate == grindRail && (candidatePoint - player).sqrMagnitude < 2.25f)
                continue;
            if (candidateT > bestT + 0.02f)
                continue;
            if (candidateT >= bestT - 0.02f && candidateSqr >= bestSqr)
                continue;

            bestT = candidateT;
            bestSqr = candidateSqr;
            rail = candidate;
            onRail = candidatePoint;
            found = true;
        }

        return found;
    }

    static Vector3 NearestOnSegment(Vector3 from, Vector3 to, Vector3 point)
    {
        Vector3 end = to - from;
        float length = end.magnitude;
        if (length < 0.0001f)
            return from;

        float t = Mathf.Clamp01(Vector3.Dot(point - from, end) / (length * length));
        return Vector3.Lerp(from, to, t);
    }

    static float ClosestAlong(Vector3 from, Vector3 to, float startAlong, Vector3 point)
    {
        Vector3 end = to - from;
        float length = end.magnitude;
        if (length < 0.0001f)
            return startAlong;

        float t = Mathf.Clamp01(Vector3.Dot(point - from, end) / (length * length));
        return startAlong + t * length;
    }

    void CutArcAt(float along)
    {
        float traveled = 0f;
        for (int i = 1; i < arcPointCount; i++)
        {
            float seg = Vector3.Distance(arcPoints[i - 1], arcPoints[i]);
            if (traveled + seg < along && i < arcPointCount - 1)
            {
                traveled += seg;
                continue;
            }

            float t = seg > 0.0001f ? Mathf.Clamp01((along - traveled) / seg) : 1f;
            arcPoints[i] = Vector3.Lerp(arcPoints[i - 1], arcPoints[i], t);
            arcPointCount = i + 1;
            return;
        }
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
        if (Hd2SpawnField.Blocks(this, transform.position + Vector3.up * 0.9f, aimLanding + Vector3.up * 0.9f))
            return false;

        var meter = GetComponent<Hd2ThrustMeter>();
        if (meter != null && !meter.TrySpend())
            return false;

        EndJumpBoost();
        grinding = false;
        grindRail = null;
        grindBoostUntil = -1f;
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

        if (HitsShield(null))
            PassingFront = true;

        thrustRail = null;
        StopBlitzTrail();
        thrustLanded = true;
        PopOutOfWalls();
        thrustLanded = false;
    }

    void PlayBlitzTrail()
    {
        EnsureBlitzTrail();
        if (blitzTrail == null)
            return;

        var main = blitzTrail.main;
        Color edge = teamColor;
        edge.a = 0.95f;
        Color spark = Color.Lerp(teamColor, Color.white, 0.18f);
        spark.a = 1f;
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
        // World-space particles stay on the path for about four to five seconds.
        main.startLifetime = new ParticleSystem.MinMaxCurve(4.2f, 5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.04f, 0.16f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.1f);
        main.gravityModifier = 0f;
        main.maxParticles = 240;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = blitzTrail.emission;
        emission.rateOverTime = 150f;

        var shape = blitzTrail.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.22f;

        var size = blitzTrail.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 1f),
            new Keyframe(0.75f, 0.85f),
            new Keyframe(1f, 0.2f)));

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
                new GradientAlphaKey(0.75f, 0.78f),
                new GradientAlphaKey(0f, 1f)
            });
        color.color = gradient;

        var renderer = trail.GetComponent<ParticleSystemRenderer>();
        Material trailMaterial = BlitzTrailMaterial();
        if (trailMaterial != null)
            renderer.sharedMaterial = trailMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    static Material blitzTrailMaterial;

    static Material BlitzTrailMaterial()
    {
        if (blitzTrailMaterial != null)
            return blitzTrailMaterial;

        blitzTrailMaterial = Hd2Shot.ParticleMaterial();
        return blitzTrailMaterial;
    }

    bool TickGrind(float dt)
    {
        if (grindRail == null)
        {
            grinding = false;
            grindBoostUntil = -1f;
            return false;
        }

        float speed = Time.time < grindBoostUntil ? grindSpeed * GrindBoostScale : grindSpeed;
        float next = grindDistance + grindSign * speed * dt;
        if (next < 0f || next > grindRail.length)
        {
            float end = Mathf.Clamp(grindDistance, 0f, grindRail.length);
            float clearance = Mathf.Max(0.05f, bodyRadius) + 0.2f;
            transform.position = grindRail.PointAlong(end) + grindRail.PlanarTangent(end) * grindSign * clearance;
            grinding = false;
            grindRail = null;
            grindBoostUntil = -1f;
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
        if (crouched)
            return walkSpeed * 0.5f;

        bool sprinting = SprintHeld();
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

        // A one-frame dropout below SnapRearm while the stick is held must not rearm.
        // The stick has to stay near center for SnapRearmDelay before the next snap.
        // One sample cannot finish that dwell, even if the frame itself is long.
        if (Mathf.Abs(x) < SnapRearm)
        {
            if (!snapArmed)
            {
                snapCenterHeld += Mathf.Min(Time.deltaTime, SnapRearmDelay * 0.5f);
                if (snapCenterHeld >= SnapRearmDelay)
                    snapArmed = true;
            }
        }
        else
        {
            snapCenterHeld = 0f;
        }

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
        snapCenterHeld = 0f;
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
    static readonly RaycastHit[] exitHits = new RaycastHit[8];
    static readonly Vector3[] exitDirs =
    {
        Vector3.forward, Vector3.back, Vector3.left, Vector3.right,
        new Vector3(1f, 0f, 1f), new Vector3(-1f, 0f, 1f),
        new Vector3(1f, 0f, -1f), new Vector3(-1f, 0f, -1f)
    };
    bool thrustLanded;

    void PopOutOfWalls()
    {
        if (TryPopOntoPayload())
            return;

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
                break;
        }

        if (thrustLanded && EmbeddedInWall())
            StepBackFromBlitz();
    }

    void StepBackFromBlitz()
    {
        Vector3 back = thrustStart - transform.position;
        back.y = 0f;
        if (back.sqrMagnitude < 0.04f)
            return;
        back.Normalize();
        for (int i = 0; i < 8 && EmbeddedInWall(); i++)
            transform.position += back * 0.2f;
    }

    bool EmbeddedInWall()
    {
        CapsuleEnds(out Vector3 bottom, out Vector3 top);
        float radius = Mathf.Max(0.05f, bodyRadius) * 0.9f;
        int count = Physics.OverlapCapsuleNonAlloc(bottom, top, radius, stuckOverlap, ~0, QueryTriggerInteraction.Ignore);
        Vector3 mid = (bottom + top) * 0.5f;
        for (int i = 0; i < count; i++)
        {
            Collider collider = stuckOverlap[i];
            if (!BlocksBody(collider))
                continue;
            if (SidePenetration(collider, mid, radius))
                return true;
        }

        return false;
    }

    bool BlocksBody(Collider collider)
    {
        if (collider == null || collider.isTrigger || IsSelf(collider) || IsButton(collider))
            return false;
        if (collider.GetComponentInParent<Hd2GrindRail>() != null)
            return false;
        if (collider.GetComponentInParent<Hd2Water>() != null)
            return false;
        Hd2SpawnField field = collider.GetComponentInParent<Hd2SpawnField>();
        return field == null || !field.Allows(this);
    }

    bool SidePenetration(Collider collider, Vector3 sample, float radius)
    {
        bool previous = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true;
        bool inside = false;
        float reach = radius + 0.6f;
        for (int d = 0; d < exitDirs.Length && !inside; d++)
        {
            Vector3 dir = exitDirs[d];
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f)
                continue;
            dir.Normalize();
            int count = Physics.RaycastNonAlloc(sample, dir, exitHits, reach, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (exitHits[i].collider != collider)
                    continue;
                bool back = Vector3.Dot(exitHits[i].normal, dir) > 0.05f;
                if (back && exitHits[i].distance < radius + 0.35f)
                    inside = true;
                else if (!back && exitHits[i].distance < radius * 0.65f)
                    inside = true;
            }
        }

        Physics.queriesHitBackfaces = previous;
        return inside;
    }

    bool TryWallPush(Collider collider, Vector3 bottom, Vector3 top, float radius, out Vector3 push)
    {
        push = Vector3.zero;
        if (collider == null || collider.isTrigger)
            return false;
        if (collider.transform == transform || collider.transform.IsChildOf(transform))
            return false;
        Hd2SpawnField field = collider.GetComponentInParent<Hd2SpawnField>();
        if (field != null && field.Allows(this))
            return false;
        if (collider.GetComponentInParent<Hd2GrindRail>() != null || IsButton(collider))
            return false;

        Vector3 mid = (bottom + top) * 0.5f;
        Vector3[] samples = { bottom, mid, top };
        Vector3 meshExit = Vector3.zero;
        float meshBest = float.MaxValue;
        for (int i = 0; i < samples.Length; i++)
        {
            Vector3 sample = samples[i];
            if (collider is BoxCollider box && TryBoxExit(box, sample, radius, out Vector3 boxExit))
            {
                if (Vector3.Dot(boxExit, Vector3.up) > 0.45f * boxExit.magnitude)
                    continue;
                push += boxExit;
                continue;
            }

            Vector3 closest = collider.ClosestPoint(sample);
            float distance = (sample - closest).magnitude;
            if (distance < 0.0001f)
            {
                if (MeshExit(collider, sample, radius, out Vector3 buried) && buried.sqrMagnitude < meshBest)
                {
                    meshBest = buried.sqrMagnitude;
                    meshExit = buried;
                }

                continue;
            }

            if (distance >= radius)
                continue;
            Vector3 away = (sample - closest) / distance;
            Vector3 step = away * (radius - distance + skinWidth);
            if (Vector3.Dot(step, Vector3.up) > 0.45f * step.magnitude)
                continue;
            push += step;
        }

        if (meshExit.sqrMagnitude > 0.0000001f)
            push += meshExit;
        return push.sqrMagnitude > 0.0000001f;
    }

    bool MeshExit(Collider collider, Vector3 sample, float radius, out Vector3 exit)
    {
        exit = Vector3.zero;
        float best = float.MaxValue;
        bool previous = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true;
        float reach = radius + 1.5f;
        for (int d = 0; d < exitDirs.Length; d++)
        {
            Vector3 dir = exitDirs[d];
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f)
                continue;
            dir.Normalize();
            int count = Physics.RaycastNonAlloc(sample, dir, exitHits, reach, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (exitHits[i].collider != collider)
                    continue;
                bool back = Vector3.Dot(exitHits[i].normal, dir) > 0.05f;
                if (!back && exitHits[i].distance >= radius)
                    continue;
                float travel = back
                    ? exitHits[i].distance + radius + skinWidth
                    : radius - exitHits[i].distance + skinWidth;
                if (travel < best)
                {
                    best = travel;
                    exit = dir * travel;
                }
            }
        }

        Physics.queriesHitBackfaces = previous;
        return best < 20f;
    }

    // A blitz onto the ground under the cart leaves the deck in the player's
    // legs. Put them on the Floor instead of leaving them stuck inside it.
    bool TryPopOntoPayload()
    {
        CapsuleEnds(out Vector3 bottom, out Vector3 top);
        float radius = Mathf.Max(0.05f, bodyRadius) * 0.9f;
        int count = Physics.OverlapCapsuleNonAlloc(bottom, top, radius, stuckOverlap, ~0, QueryTriggerInteraction.Ignore);
        Hd2Payload payload = null;
        bool shield = false;
        for (int i = 0; i < count; i++)
        {
            Collider collider = stuckOverlap[i];
            if (collider == null || collider.isTrigger)
                continue;
            if (collider.transform == transform || collider.transform.IsChildOf(transform))
                continue;
            if (collider.GetComponentInParent<Hd2PayloadShield>() != null)
                shield = true;
            if (payload == null)
                payload = collider.GetComponentInParent<Hd2Payload>();
        }

        if (payload == null)
            return false;

        Vector3 feet = transform.position;
        if (shield && !payload.DeckUnder(feet, feet.y, 1.75f, out _))
            return false;
        if (!DeckNear(payload, feet, out Vector3 stand))
            return false;

        float rise = stand.y - feet.y;
        if (rise < 0.12f || rise > 1.75f)
            return false;

        Vector3 landed = stand + Vector3.up * 0.04f;
        if (!CapsuleFitsAbove(landed))
            return false;

        transform.position = landed;
        grinding = false;
        grindRail = null;
        grindBoostUntil = -1f;
        verticalVelocity = 0f;
        fallTime = 0f;
        return true;
    }

    bool DeckNear(Hd2Payload payload, Vector3 feet, out Vector3 stand)
    {
        if (payload.DeckUnder(feet, feet.y, 1.75f, out stand))
            return true;

        const float reach = 0.5f;
        for (int i = 0; i < 4; i++)
        {
            float angle = i * Mathf.PI * 0.5f;
            Vector3 sample = feet + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * reach;
            if (payload.DeckUnder(sample, feet.y, 1.75f, out stand))
                return true;
        }

        return false;
    }

    public bool HitsShield(Hd2Payload cart)
    {
        CapsuleEnds(out Vector3 bottom, out Vector3 top);
        float radius = Mathf.Max(0.05f, bodyRadius) + skinWidth;
        int count = Physics.OverlapCapsuleNonAlloc(bottom, top, radius, shieldTouch, ~0, QueryTriggerInteraction.Collide);
        for (int i = 0; i < count; i++)
        {
            Collider collider = shieldTouch[i];
            if (collider == null)
                continue;
            Hd2PayloadFront front = collider.GetComponent<Hd2PayloadFront>();
            if (front == null)
                continue;
            if (cart == null || front.payload == cart)
                return true;
        }

        return false;
    }

    static readonly Collider[] shieldTouch = new Collider[16];

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

                var payload = hit.collider != null ? hit.collider.GetComponentInParent<Hd2Payload>() : null;
                bool shield = hit.collider != null && hit.collider.GetComponentInParent<Hd2PayloadShield>() != null;
                if (!shield && payload != null && TryStepOntoPayload(payload, direction, distance, travel))
                    return;

                if (TryStep(direction, distance, travel, hit))
                    return;

                transform.position += direction * travel;
                delta = Vector3.ProjectOnPlane(direction * (distance - travel), hit.normal);
                delta.y = 0f;
            }
            else
            {
                transform.position += delta;
                StickToLowerFloor();
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

    // The payload deck is taller than a normal step. Walking into it plants the
    // player on the Floor instead of stopping at the side.
    bool TryStepOntoPayload(Hd2Payload payload, Vector3 direction, float distance, float blockedTravel)
    {
        const float maxRise = 1.75f;
        float feetY = transform.position.y;
        float reach = Mathf.Max(distance, blockedTravel + Mathf.Max(0.2f, bodyRadius));
        Vector3 ahead = transform.position + direction * Mathf.Min(reach, 0.55f);
        Vector3 further = transform.position + direction * 0.9f;
        if (!payload.DeckUnder(ahead, feetY, maxRise, out Vector3 stand)
            && !payload.DeckUnder(further, feetY, maxRise, out stand))
            return false;

        float rise = stand.y - feetY;
        if (rise < 0.08f || rise > maxRise)
            return false;

        Vector3 flat = stand - transform.position;
        flat.y = 0f;
        if (flat.sqrMagnitude > 1.4f * 1.4f)
            return false;
        if (flat.sqrMagnitude > 0.04f && Vector3.Dot(flat.normalized, direction) < 0.15f)
            return false;

        Vector3 landed = stand + direction * 0.12f;
        landed.y = stand.y + 0.03f;
        if (payload.DeckUnder(landed, stand.y, 0.4f, out Vector3 planted))
        {
            landed.x = planted.x;
            landed.z = planted.z;
            landed.y = planted.y + 0.03f;
        }

        if (!CapsuleFitsAbove(landed))
            return false;

        transform.position = landed;
        verticalVelocity = 0f;
        fallTime = 0f;
        return true;
    }

    // A lip up to half a meter is floor, not a wall. The capsule sphere hits the
    // face early, so the walkable surface is sampled past that contact.
    bool TryStep(Vector3 direction, float distance, float blockedTravel, RaycastHit block)
    {
        float maxStep = Mathf.Max(0.5f, stepHeight);
        float reach = blockedTravel + Mathf.Max(0.12f, bodyRadius) + 0.08f;
        Vector3 past = transform.position + direction * reach;
        Vector3 contact = block.point + direction * 0.12f;
        if (!FloorBeside(past, maxStep, out float floorY) && !FloorBeside(contact, maxStep, out floorY))
            return false;

        float change = floorY - transform.position.y;
        if (change > maxStep || change < -maxStep)
            return false;
        if (change > -0.004f && change < 0.004f)
            return false;

        if (change < 0f)
        {
            transform.position += direction * distance;
            return true;
        }

        float forward = Mathf.Max(distance, blockedTravel + 0.08f);
        if (!HeadroomForStep(direction, forward, change))
            return false;

        Vector3 landed = transform.position + direction * forward;
        landed.y = floorY;
        if (!CapsuleFitsAbove(landed))
            return false;

        transform.position = landed;
        return true;
    }

    bool FloorBeside(Vector3 point, float maxStep, out float floorY)
    {
        floorY = point.y;
        Vector3 origin = new Vector3(point.x, transform.position.y + maxStep + 0.08f, point.z);
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, bodyHits, maxStep * 2f + 0.2f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            Collider collider = bodyHits[i].collider;
            if (IsSelf(collider) || IsButton(collider))
                continue;
            Hd2SpawnField field = collider != null ? collider.GetComponentInParent<Hd2SpawnField>() : null;
            if (field != null && field.Allows(this))
                continue;
            if (bodyHits[i].normal.y < 0.45f || bodyHits[i].distance >= best)
                continue;
            best = bodyHits[i].distance;
            floorY = bodyHits[i].point.y;
            found = true;
        }

        return found;
    }

    bool HeadroomForStep(Vector3 direction, float forward, float change)
    {
        CapsuleEnds(out Vector3 bottom, out Vector3 top);
        Vector3 raised = Vector3.up * (change + 0.03f);
        float radius = Mathf.Max(0.05f, bodyRadius) * 0.85f;
        if (!CastBody(bottom + raised, top + raised, radius, direction, forward + skinWidth, out RaycastHit wall))
            return true;
        return wall.distance >= 0.04f;
    }

    void StickToLowerFloor()
    {
        if (jumpBoosting || jumpSpent)
            return;

        float maxStep = Mathf.Max(0.5f, stepHeight);
        Vector3 origin = transform.position + Vector3.up * 0.12f;
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, bodyHits, 0.12f + maxStep, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        float floorY = transform.position.y;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            Collider collider = bodyHits[i].collider;
            if (IsSelf(collider) || IsButton(collider))
                continue;
            if (bodyHits[i].normal.y < 0.45f || bodyHits[i].distance >= best)
                continue;
            best = bodyHits[i].distance;
            floorY = bodyHits[i].point.y;
            found = true;
        }

        if (!found)
            return;

        float drop = transform.position.y - floorY;
        if (drop < 0.004f || drop > maxStep)
            return;

        Vector3 planted = transform.position;
        planted.y = floorY;
        transform.position = planted;
    }

    bool CapsuleFitsAbove(Vector3 feet)
    {
        float radius = Mathf.Max(0.05f, bodyRadius);
        float height = Mathf.Max(bodyHeight, radius * 2f + 0.01f);
        float castRadius = radius * 0.9f;
        Vector3 bottom = feet + Vector3.up * (radius + skinWidth);
        Vector3 top = feet + Vector3.up * (height - radius);
        int count = Physics.OverlapCapsuleNonAlloc(bottom, top, castRadius, stuckOverlap, ~0, QueryTriggerInteraction.Ignore);
        float limit = skinWidth * skinWidth;
        Vector3 sample = feet + Vector3.up * (radius + 0.05f);
        for (int i = 0; i < count; i++)
        {
            Collider collider = stuckOverlap[i];
            if (collider == null || collider.isTrigger)
                continue;
            if (collider.ClosestPoint(sample).y < feet.y + 0.03f)
                continue;
            if (TryWallPush(collider, bottom, top, castRadius, out Vector3 push) && push.sqrMagnitude > limit)
                return false;
        }

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
        grindBoostUntil = -1f;
        jumpOriginY = transform.position.y;
        jumpTime = 0f;
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

    float BurnJump(float dt)
    {
        var meter = GetComponent<Hd2ThrustMeter>();
        if (!JumpHeld() || jumpTime >= jumpMaxTime || meter == null || meter.Charges <= 0.001f)
        {
            EndJumpBoost();
            return 0f;
        }

        float timeLeft = Mathf.Max(0f, jumpMaxTime) - jumpTime;
        float burn = Mathf.Min(dt, timeLeft);
        float rate = Mathf.Max(0.01f, jumpThrustPerSecond);
        float spent = meter.SpendUpTo(burn * rate);
        burn = spent / rate;
        if (burn <= 0.0001f)
        {
            EndJumpBoost();
            return 0f;
        }

        jumpTime += burn;
        if (jumpTime >= jumpMaxTime - 0.0001f || meter.Charges <= 0.001f)
            EndJumpBoost();
        return burn;
    }

    void EndJumpBoost()
    {
        bool wasBoosting = jumpBoosting;
        if (!wasBoosting)
            return;

        jumpBoosting = false;
        SetJumpExhaust(false);
        if (wasBoosting && jumpThrustSource != null && jumpThrustSource.isPlaying)
            jumpThrustFade = JumpThrustFadeTime;
    }

    void SetJumpExhaust(bool playing)
    {
        EnsureJumpExhaust();
        DriveJumpExhaust(jumpExhaust, playing);
    }

    public static void DriveJumpExhaust(ParticleSystem[] exhaust, bool playing)
    {
        if (exhaust == null)
            return;

        for (int i = 0; i < exhaust.Length; i++)
        {
            ParticleSystem particles = exhaust[i];
            if (particles == null)
                continue;

            var emission = particles.emission;
            emission.enabled = playing;
            if (!playing)
                continue;
            if (!particles.isPlaying)
                particles.Play();
            particles.Emit(56);
        }
    }

    void EnsureJumpExhaust()
    {
        if (jumpExhaust != null)
            return;

        var avatar = GetComponent<Hd2Body>();
        Transform root = avatar != null && avatar.body != null ? avatar.body : transform;
        jumpExhaust = BuildJumpExhaust(root);
    }

    public static ParticleSystem[] BuildJumpExhaust(Transform root)
    {
        if (root == null)
            return new ParticleSystem[0];

        Transform leftNozzle = FindNamed(root, "JumpNozzleL");
        Transform rightNozzle = FindNamed(root, "JumpNozzleR");
        if (leftNozzle != null && rightNozzle != null)
        {
            return new[]
            {
                CreateJumpExhaust(AttachVent(leftNozzle, "JumpExhaustL")),
                CreateJumpExhaust(AttachVent(rightNozzle, "JumpExhaustR"))
            };
        }

        Vector3[] nozzles = FindJumpNozzles(root);
        var exhaust = new ParticleSystem[nozzles.Length];
        for (int i = 0; i < nozzles.Length; i++)
        {
            var vent = new GameObject(i == 0 ? "JumpExhaustL" : "JumpExhaustR");
            vent.hideFlags = HideFlags.DontSave;
            vent.transform.SetParent(root, false);
            vent.transform.localPosition = nozzles[i];
            vent.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            exhaust[i] = CreateJumpExhaust(vent);
        }

        return exhaust;
    }

    static GameObject AttachVent(Transform nozzle, string name)
    {
        var vent = new GameObject(name);
        vent.hideFlags = HideFlags.DontSave;
        vent.transform.SetParent(nozzle, false);
        vent.transform.localPosition = Vector3.zero;
        vent.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
        return vent;
    }

    static Transform FindNamed(Transform root, string name)
    {
        if (root.name == name)
            return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindNamed(root.GetChild(i), name);
            if (found != null)
                return found;
        }

        return null;
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
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 1f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.8f, 3.4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.22f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.62f, 0.64f, 0.66f, 1f),
            new Color(0.86f, 0.88f, 0.9f, 1f));
        main.gravityModifier = 0.08f;
        main.maxParticles = 150;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = particles.emission;
        emission.rateOverTime = 240f;

        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 36f;
        shape.radius = 0.035f;

        var size = particles.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.7f),
            new Keyframe(0.35f, 1.6f),
            new Keyframe(1f, 2.4f)));

        var color = particles.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.88f, 0.9f, 0.92f), 0f),
                new GradientColorKey(new Color(0.55f, 0.58f, 0.6f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(0.7f, 0f),
                new GradientAlphaKey(0.5f, 0.25f),
                new GradientAlphaKey(0.16f, 0.7f),
                new GradientAlphaKey(0f, 1f)
            });
        color.color = gradient;

        var renderer = vent.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.enableGPUInstancing = false;
        Material exhaustMaterial = JumpExhaustMaterial();
        if (exhaustMaterial != null)
            renderer.sharedMaterial = exhaustMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return particles;
    }

    static Material jumpExhaustMaterial;

    static Material JumpExhaustMaterial()
    {
        if (jumpExhaustMaterial != null)
            return jumpExhaustMaterial;

        jumpExhaustMaterial = Resources.Load<Material>("SoftSmoke");
        if (jumpExhaustMaterial != null)
            return jumpExhaustMaterial;

        Shader shader = Shader.Find("HD2/SoftSmoke");
        if (shader != null)
        {
            jumpExhaustMaterial = new Material(shader);
            jumpExhaustMaterial.SetColor("_Color", Color.white);
            return jumpExhaustMaterial;
        }

        jumpExhaustMaterial = Hd2Shot.ParticleMaterial();
        return jumpExhaustMaterial;
    }

    void ApplyGravity(float dt, float burn = 0f)
    {
        burn = Mathf.Clamp(burn, 0f, dt);
        float coast = dt - burn;
        if (burn > 0f)
            verticalVelocity += gravity * Mathf.Max(0f, jumpAccelGs) * burn;
        if (coast > 0f)
        {
            verticalVelocity -= gravity * coast;
            if (verticalVelocity < -terminalVelocity)
                verticalVelocity = -terminalVelocity;
        }

        bool fallingBack = jumpSpent && transform.position.y > jumpOriginY + 0.02f;
        if (burn <= 0f && !fallingBack && verticalVelocity <= 0f && TryGround(out float groundY))
        {
            verticalVelocity = 0f;
            fallTime = 0f;
            Vector3 grounded = transform.position;
            grounded.y = groundY;
            transform.position = grounded;
            return;
        }

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
        if (dy < 0f && burn <= 0f)
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
        {
            health.MarkHit(transform.position + Vector3.up * 1.1f);
            health.ApplyHit(health.Health, false);
        }
    }

    static readonly Collider[] waterHits = new Collider[8];

    void DrownIfTouchingWater()
    {
        var health = GetComponent<Hd2Health>();
        if (health == null || health.IsDead || !TouchingWater(out Vector3 surface))
            return;

        Hd2Water.Splash(surface, true);
        health.MarkHit(surface + Vector3.up * 0.4f);
        health.ApplyHit(health.Health, false);
    }

    bool TouchingWater(out Vector3 surface)
    {
        surface = transform.position;
        Vector3 origin = transform.position + Vector3.up * 0.15f;
        if (RaycastBody(origin, Vector3.down, 0.15f + groundProbe, out RaycastHit hit)
            && hit.collider.GetComponentInParent<Hd2Water>() != null)
        {
            float feetGap = transform.position.y - hit.point.y;
            if (feetGap <= groundProbe && feetGap >= -0.05f)
            {
                surface = hit.point;
                return true;
            }
        }

        int count = Physics.OverlapSphereNonAlloc(transform.position, 0.2f, waterHits, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            if (waterHits[i] == null || waterHits[i].GetComponentInParent<Hd2Water>() == null)
                continue;

            surface = new Vector3(transform.position.x, waterHits[i].bounds.max.y, transform.position.z);
            return true;
        }

        return false;
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

    // Drops a parent of the headset and both hands. CameraOffset local Y stays 0,
    // and the camera local pose is never written. The player root stays on the floor.
    void UpdateCrouch(float dt)
    {
        EnsureCrouchRig();
        float eye = EyeAboveRig();
        if (eye > 0.75f)
        {
            if (!stanceReady)
            {
                standingEye = eye;
                stanceReady = true;
            }
            else if (eye >= standingEye - 0.06f)
                standingEye = Mathf.Lerp(standingEye, eye, 1f - Mathf.Exp(-8f * dt));
        }

        crouchBlend = Mathf.MoveTowards(crouchBlend, crouched ? 1f : 0f, dt / 0.16f);
        PlaceCrouchRig();
        float live = head != null ? head.position.y - transform.position.y : VirtualStand();
        bodyHeight = Mathf.Clamp(live, 0.8f, MaxStandHeight);
    }

    float VirtualStand()
    {
        if (!stanceReady)
            return MinStandHeight;
        return Mathf.Clamp(standingEye, MinStandHeight, MaxStandHeight);
    }

    void PlaceCrouchRig()
    {
        if (crouchRig == null)
            return;

        float lift = 0f;
        float squatDrop = 0f;
        float real = EyeAboveRig();
        if (real > 0.75f && stanceReady)
        {
            float stand = VirtualStand();
            lift = stand - standingEye;
            float sink = Mathf.Max(0f, stand - MinCrouchEye);
            squatDrop = sink * crouchBlend;
        }

        crouchRig.position = transform.position + Vector3.up * (lift - squatDrop);
        crouchRig.localRotation = Quaternion.identity;
        crouchRig.localScale = Vector3.one;
    }

    // The headset walks around the tracking origin. Pull the root under the
    // headset so the capsule, water, and avatar stay with the player.
    // Camera local pose is never written, and CameraOffset local Y stays 0.
    // While grinding, room steps are stored and the view stays on the rail.
    // Leaving the rail keeps that pose, so the next step starts from the rail.
    void AlignRoom()
    {
        EnsureRoomRig();
        if (head == null || roomRig == null)
            return;

        Vector3 delta = head.position - transform.position;
        delta.y = 0f;
        if (delta.sqrMagnitude < 0.00000004f)
            return;

        if (grinding)
        {
            roomRig.position -= delta;
            return;
        }

        if (thrusting)
        {
            transform.position += delta;
            roomRig.position -= delta;
            return;
        }

        Vector3 before = transform.position;
        MoveHorizontal(delta);
        Vector3 moved = transform.position - before;
        moved.y = 0f;
        if (moved.sqrMagnitude < 0.00000004f)
            return;

        roomRig.position -= moved;
    }

    void EnsureRoomRig()
    {
        if (roomRig != null || crouchRig == null)
            return;

        var rigObject = new GameObject("RoomRig");
        roomRig = rigObject.transform;
        roomRig.SetParent(crouchRig, false);
        roomRig.localPosition = Vector3.zero;
        roomRig.localRotation = Quaternion.identity;
        roomRig.localScale = Vector3.one;

        var children = new List<Transform>(crouchRig.childCount);
        for (int i = 0; i < crouchRig.childCount; i++)
        {
            Transform child = crouchRig.GetChild(i);
            if (child != roomRig)
                children.Add(child);
        }

        for (int i = 0; i < children.Count; i++)
            children[i].SetParent(roomRig, true);
    }

    float EyeAboveRig()
    {
        if (head == null)
            return 0f;
        Transform origin = crouchRig != null ? crouchRig : transform;
        return head.position.y - origin.position.y;
    }

    void EnsureCrouchRig()
    {
        if (crouchRig != null || head == null)
            return;

        var rigObject = new GameObject("CrouchRig");
        crouchRig = rigObject.transform;
        crouchRig.SetParent(transform, false);
        crouchRig.localPosition = Vector3.zero;
        crouchRig.localRotation = Quaternion.identity;
        crouchRig.localScale = Vector3.one;

        Transform view = head.parent != null && head.parent != transform ? head.parent : head;
        if (view.parent == transform)
            view.SetParent(crouchRig, true);
        if (leftHand != null && leftHand.parent == transform)
            leftHand.SetParent(crouchRig, true);
        if (rightHand != null && rightHand.parent == transform)
            rightHand.SetParent(crouchRig, true);
        EnsureRoomRig();
    }

    bool RoomToStand()
    {
        float radius = Mathf.Max(0.05f, bodyRadius);
        float stand = VirtualStand();
        float low = MinCrouchEye;
        float extra = stand - low;
        if (extra <= 0.02f)
            return true;

        Vector3 bottom = transform.position + Vector3.up * (radius + skinWidth);
        float topHeight = Mathf.Max(low - radius, radius + skinWidth);
        Vector3 top = transform.position + Vector3.up * topHeight;
        return !CastBody(bottom, top, radius * 0.9f, Vector3.up, extra + skinWidth, out _);
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
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
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
        bool unlit = previewMaterial != null && previewMaterial.shader != null
            && previewMaterial.shader.name.IndexOf("Unlit", System.StringComparison.Ordinal) >= 0;
        Color vertex = unlit ? Color.white : color;
        if (previewMaterial != null)
        {
            if (previewMaterial.HasProperty("_BaseColor"))
                previewMaterial.SetColor("_BaseColor", color);
            if (previewMaterial.HasProperty("_Color"))
                previewMaterial.SetColor("_Color", color);
        }

        if (arcLine != null)
        {
            arcLine.startColor = vertex;
            arcLine.endColor = vertex;
        }

        if (wireMaterial != null)
        {
            Color wire = color;
            wire.a = 0.2f;
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
            wireLines[i].startColor = vertex;
            wireLines[i].endColor = vertex;
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

        // Crouching keeps the head on the headset and drops the body root under the floor.
        // Shift from the planted feet so the preview stands on the landing.
        var avatar = source.GetComponentInParent<Hd2Body>();
        float sole = avatar != null ? avatar.SoleHeight() : source.position.y;
        Vector3 delta = landing - source.position;
        delta.y = landing.y - sole;
        thrustGhost.SetActive(true);
        CopyPose(source, thrustGhost.transform, delta);
        if (avatar != null)
            ShowHeldGhost(avatar, delta);
    }

    void ShowHeldGhost(Hd2Body avatar, Vector3 delta)
    {
        if (wireMaterial == null)
            return;

        heldScan.Clear();
        AppendHeldRenderers(avatar.leftHand);
        AppendHeldRenderers(avatar.rightHand);
        if (!HeldSetMatches())
            RebuildHeldGhost();

        if (heldGhostRoot != null)
            heldGhostRoot.SetActive(true);

        for (int i = 0; i < heldSources.Length; i++)
        {
            Renderer source = heldSources[i];
            Transform clone = heldClones[i];
            if (source == null || clone == null)
                continue;
            if (source is SkinnedMeshRenderer skin)
                PoseSkinnedWire(skin, clone, i);
            clone.SetPositionAndRotation(source.transform.position + delta, source.transform.rotation);
            // BakeMesh already writes the transform scale into the vertices. Applying it
            // again shrinks the launcher toward its pivot and pulls it off the hand.
            clone.localScale = source is SkinnedMeshRenderer ? Vector3.one : source.transform.lossyScale;
        }
    }

    void AppendHeldRenderers(Transform hand)
    {
        if (hand == null)
            return;

        heldScratch.Clear();
        hand.GetComponentsInChildren(false, heldScratch);
        for (int i = 0; i < heldScratch.Count; i++)
        {
            MeshRenderer renderer = heldScratch[i];
            if (renderer == null || !renderer.enabled || renderer.name == "MuzzleFlash")
                continue;
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable)
                continue;
            heldScan.Add(renderer);
        }

        skinScratch.Clear();
        hand.GetComponentsInChildren(false, skinScratch);
        for (int i = 0; i < skinScratch.Count; i++)
        {
            SkinnedMeshRenderer skin = skinScratch[i];
            if (skin == null || !skin.enabled || skin.sharedMesh == null || !skin.sharedMesh.isReadable)
                continue;
            heldScan.Add(skin);
        }
    }

    bool HeldSetMatches()
    {
        if (heldScan.Count != heldSources.Length)
            return false;
        for (int i = 0; i < heldScan.Count; i++)
        {
            if (heldScan[i] != heldSources[i])
                return false;
        }

        return true;
    }

    void RebuildHeldGhost()
    {
        ClearHeldGhost();
        if (heldScan.Count == 0 || wireMaterial == null)
            return;

        heldGhostRoot = new GameObject("ThrustHeldGhost");
        heldGhostRoot.hideFlags = HideFlags.DontSave;
        heldSources = heldScan.ToArray();
        heldClones = new Transform[heldSources.Length];
        heldBakes = new Mesh[heldSources.Length];
        heldRemaps = new int[heldSources.Length][];
        heldWireVerts = new Vector3[heldSources.Length][];
        for (int i = 0; i < heldSources.Length; i++)
        {
            Renderer source = heldSources[i];
            Mesh sourceMesh = source is SkinnedMeshRenderer skin
                ? skin.sharedMesh
                : source.GetComponent<MeshFilter>().sharedMesh;
            var copy = new GameObject(source.name + " Wire");
            copy.hideFlags = HideFlags.DontSave;
            copy.transform.SetParent(heldGhostRoot.transform, false);
            var filter = copy.AddComponent<MeshFilter>();
            filter.sharedMesh = MakeWireMesh(sourceMesh);
            var renderer = copy.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = wireMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            heldClones[i] = copy.transform;
            if (source is SkinnedMeshRenderer)
            {
                var bake = new Mesh();
                bake.hideFlags = HideFlags.DontSave;
                heldBakes[i] = bake;
                heldRemaps[i] = sourceMesh.triangles;
                heldWireVerts[i] = new Vector3[heldRemaps[i].Length];
            }
        }
    }

    void ClearHeldGhost()
    {
        if (heldGhostRoot != null)
        {
            var filters = heldGhostRoot.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                Mesh mesh = filters[i].sharedMesh;
                if (mesh != null && (mesh.hideFlags & HideFlags.DontSave) != 0)
                    Destroy(mesh);
            }

            Destroy(heldGhostRoot);
        }

        for (int i = 0; i < heldBakes.Length; i++)
        {
            if (heldBakes[i] != null)
                Destroy(heldBakes[i]);
        }

        heldGhostRoot = null;
        heldSources = System.Array.Empty<Renderer>();
        heldClones = System.Array.Empty<Transform>();
        heldBakes = System.Array.Empty<Mesh>();
        heldRemaps = System.Array.Empty<int[]>();
        heldWireVerts = System.Array.Empty<Vector3[]>();
    }

    void PoseSkinnedWire(SkinnedMeshRenderer skin, Transform clone, int index)
    {
        Mesh bake = index < heldBakes.Length ? heldBakes[index] : null;
        MeshFilter filter = clone.GetComponent<MeshFilter>();
        if (bake == null || filter == null || filter.sharedMesh == null || skin.sharedMesh == null)
            return;

        skin.BakeMesh(bake, false);
        bake.GetVertices(bakeScratch);
        int[] remap = heldRemaps[index];
        Vector3[] wireVerts = heldWireVerts[index];
        int posed = bakeScratch.Count;
        for (int i = 0; i < wireVerts.Length; i++)
        {
            int src = remap[i];
            wireVerts[i] = src >= 0 && src < posed ? bakeScratch[src] : Vector3.zero;
        }

        filter.sharedMesh.vertices = wireVerts;
        filter.sharedMesh.RecalculateBounds();
    }

    bool EnsureGhost(Transform source)
    {
        if (thrustGhost != null)
            return wireMaterial != null;

        Material sourceMaterial = Resources.Load<Material>("WireAvatar");
        Shader shader = sourceMaterial != null ? sourceMaterial.shader : Shader.Find("HD2/WireAvatar");
        bool hasSkin = source.GetComponentInChildren<SkinnedMeshRenderer>(true) != null;
        bool hasMesh = source.GetComponentInChildren<MeshRenderer>(true) != null;
        if (shader == null || (!hasSkin && !hasMesh))
            return false;

        thrustGhost = Instantiate(source.gameObject);
        thrustGhost.name = "ThrustAvatarGhost";
        thrustGhost.hideFlags = HideFlags.DontSave;
        thrustGhost.transform.SetParent(null, true);
        Hd2RobotCleanup.StripImportedDevices(thrustGhost);

        var cones = thrustGhost.GetComponentsInChildren<Hd2VisionCone>(true);
        for (int i = 0; i < cones.Length; i++)
        {
            if (cones[i] != null)
                Destroy(cones[i].gameObject);
        }
        var colliders = thrustGhost.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
            Destroy(colliders[i]);
        var bodies = thrustGhost.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < bodies.Length; i++)
            Destroy(bodies[i]);
        var animators = thrustGhost.GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < animators.Length; i++)
            animators[i].enabled = false;

        wireMaterial = sourceMaterial != null ? new Material(sourceMaterial) : new Material(shader);
        wireMaterial.hideFlags = HideFlags.DontSave;

        var skins = thrustGhost.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < skins.Length; i++)
        {
            SkinnedMeshRenderer skin = skins[i];
            if (skin.sharedMesh != null && skin.sharedMesh.isReadable)
                skin.sharedMesh = MakeWireMesh(skin.sharedMesh);
            skin.sharedMaterials = new Material[] { wireMaterial };
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
            renderer.sharedMaterials = new Material[] { wireMaterial };
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        thrustGhost.SetActive(false);
        ghostRenderers = thrustGhost.GetComponentsInChildren<Renderer>(true);
        return true;
    }

    void CullPreviewToOwner(ScriptableRenderContext context, Camera camera)
    {
        SetPreviewForcedOff(camera != OwnerCamera());
    }

    void RestorePreviewAfterCamera(ScriptableRenderContext context, Camera camera)
    {
        SetPreviewForcedOff(false);
    }

    Camera OwnerCamera()
    {
        if (ownerCamera == null)
            ownerCamera = GetComponentInChildren<Camera>();
        return ownerCamera;
    }

    void SetPreviewForcedOff(bool off)
    {
        if (arcLine != null)
            arcLine.forceRenderingOff = off;
        if (wireLines != null)
        {
            for (int i = 0; i < wireLines.Length; i++)
            {
                if (wireLines[i] != null)
                    wireLines[i].forceRenderingOff = off;
            }
        }

        for (int i = 0; i < ghostRenderers.Length; i++)
        {
            if (ghostRenderers[i] != null)
                ghostRenderers[i].forceRenderingOff = off;
        }

        for (int i = 0; i < heldClones.Length; i++)
        {
            if (heldClones[i] == null)
                continue;
            var renderer = heldClones[i].GetComponent<Renderer>();
            if (renderer != null)
                renderer.forceRenderingOff = off;
        }

        var meter = GetComponent<Hd2ThrustMeter>();
        if (meter != null)
            meter.SetOwnerOnly(!off);
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
        var uvs = new Vector2[count];
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
            uvs[i] = corner == 0 ? new Vector2(1f, 0f) : corner == 1 ? new Vector2(0f, 1f) : Vector2.zero;
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
        mesh.uv = uvs;
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
        if (heldGhostRoot != null)
            heldGhostRoot.SetActive(false);
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
