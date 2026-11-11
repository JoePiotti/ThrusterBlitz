using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum Hd2PayloadRole
{
    None = 0,
    Ride = 1,
    Defend = 2,
    Stop = 3
}

/// <summary>
/// Deathmatch bot. Wanders until it sees an enemy or takes a hit, then shoots the body
/// while sprinting, strafing, or blitzing. Walk, blitz, and guns match the player.
/// A jump spends thrust the same way a player's does, and the meter refills only
/// while the bot is not sprinting. Combat sidesteps are a little slower than a player sprint.
/// With domination on, the same body holds a point, walks to a weak or contested one,
/// fights, and falls back to heal.
/// </summary>
[DefaultExecutionOrder(-50)]
public class Hd2EnemyBot : MonoBehaviour
{
    public GameObject robotPrefab;
    public GameObject pistolModel;
    public Color bodyColor = Color.white;
    [Tooltip("Spawn shields of this team let this bot through.")]
    public Hd2Team team;
    public string displayName = "bot";
    public bool useBodyColor;
    [Tooltip("Rome domination. Holds and walks points, fights enemies, and falls back to heal.")]
    public bool domination;
    [Tooltip("Payload match. The bot walks to one of the four points nearest the cart, or onto the cart.")]
    public Hd2PayloadRole payloadRole;
    Hd2Poi payloadPoi;
    bool payloadGoalOnCart;
    bool payloadGoalReady;
    bool payloadRailRide;
    float payloadGoalUntil;
    float payloadClosestGap = float.MaxValue;
    float payloadProgressAt;
    Transform returnFire;
    [Tooltip("0 walks to the nearest point. Higher values prefer a contested or enemy-held point.")]
    public float objectiveWeight = 1f;
    [Tooltip("Leaves the fight when health falls to this fraction of max.")]
    public float retreatFraction = 0.34f;
    [Tooltip("Health restored each second while falling back.")]
    public float retreatHealPerSecond = 8f;
    [Tooltip("Tie break so this bot leans toward one point.")]
    public string pointBias = "A";
    [Tooltip("Which reserved spawn of this team this bot uses.")]
    public int spawnSlot;

    static readonly Hd2Poi[] poiNear = new Hd2Poi[4];
    static readonly float[] poiNearDist = new float[4];
    static Component[] cachedWeapons;
    static bool weaponListReady;

    const float WalkSpeed = 2.5f;
    const float ObjectiveSpeed = 6.5f;
    const float SidestepSpeed = 4.6f;
    const float BlitzSpeed = 30f;
    const float BlitzRange = 13f;
    const float BlitzHop = 0.5f;
    const float GrindSpeed = 8f;
    const float SeeRange = 20f;
    const float SeeHalfAngle = 15f;
    const float AimError = 6.5f;
    const float NoticeDelay = 0.4f;
    const float DropCombatRange = 30f;
    const float RagdollSeconds = 5f;
    const float TurnStep = 45f;
    const float TurnRate = 120f;
    const float JumpGravity = 15f;
    const float JumpAccelGs = 3f;
    const float JumpMaxTime = 0.3f;
    const float JumpThrustPerSecond = 1f;

    enum Mode
    {
        Wander,
        Combat
    }

    enum WanderKind
    {
        Walk,
        Rail,
        Blitz,
        Pickup,
        Turn
    }

    enum CombatKind
    {
        Sprint,
        Blitz,
        Strafe
    }

    CharacterController body;
    Hd2Health health;
    Hd2ThrustMeter meter;
    Transform rightHand;
    Transform leftHand;
    Transform eye;
    readonly List<Transform> seen = new List<Transform>();
    readonly Transform[] fightChoices = new Transform[12];
    readonly float[] fightDistance = new float[12];
    readonly int[] fightShooters = new int[12];
    readonly bool[] fightOnPoint = new bool[12];
    int fightCount;
    static readonly RaycastHit[] sightHits = new RaycastHit[32];
    static readonly RaycastHit[] stepHits = new RaycastHit[8];
    // Floor, water, and wall probes run one at a time. RaycastAll allocated a new array on every
    // probe, and a control-point match keeps every bot doing that for the whole clock.
    static readonly RaycastHit[] probeHits = new RaycastHit[96];
    static readonly Collider[] platformHits = new Collider[32];
    static readonly Hd2Locomotion[] huntPlayers = new Hd2Locomotion[8];
    static int huntPlayerCount;
    static float huntPlayerAt;
    readonly List<Hd2Smg> heldSmgs = new List<Hd2Smg>(4);
    readonly List<Hd2LaserPistol> heldLasers = new List<Hd2LaserPistol>(4);
    readonly List<Hd2Pistol> heldPistols = new List<Hd2Pistol>(4);
    readonly List<Hd2Shotgun> heldShotguns = new List<Hd2Shotgun>(2);
    readonly List<Hd2Shocks> heldShocks = new List<Hd2Shocks>(2);
    readonly List<Hd2BurstPistol> heldBursts = new List<Hd2BurstPistol>(2);
    readonly List<Hd2RocketLauncher> heldRockets = new List<Hd2RocketLauncher>(2);
    Mode mode = Mode.Wander;
    WanderKind wander = WanderKind.Walk;
    CombatKind combat = CombatKind.Sprint;
    Transform target;
    float lostTargetAt = -1f;

    public Transform CombatTarget => mode == Mode.Combat ? target : null;
    Vector3 moveDir = Vector3.forward;
    Vector3 smoothedMove;
    Vector3 mapCenter;
    float playRadius;
    bool hasCenter;
    float modeUntil;
    float verticalVelocity;
    float fallTime;
    bool jumpBoosting;
    float jumpTime;
    float lastMovedAt;
    Vector3 stuckAnchor;
    float stuckSince;
    float nextPump;
    float nextPistolFire;
    float strafeFlipAt;
    int strafeSign = 1;
    bool wallReversed;
    bool traveling;
    public bool IsBlitzing => traveling;

    public bool OnRail(Hd2GrindRail rail)
    {
        return grinding && grindRail == rail;
    }

    public float RailDistance => grindDistance;

    public float RailDirection => grindSign;

    public void BoardFromRear(Vector3 deck)
    {
        bool keepGoing = !payloadGoalOnCart && GoalAheadOfCart();
        DismountRail();
        transform.position = deck;
        payloadRailRide = false;
        moveDir = Vector3.zero;
        if (keepGoing)
            BlitzOverFront();
    }
    public bool PassingFront { get; set; }
    static readonly List<Hd2EnemyBot> active = new List<Hd2EnemyBot>();
    static readonly Collider[] shieldTouch = new Collider[16];

    public static int ActiveCount => active.Count;

    public static Hd2EnemyBot ActiveAt(int index)
    {
        return active[index];
    }

    public bool HitsShield(Hd2Payload cart)
    {
        if (body == null)
            return false;

        Vector3 up = transform.up;
        Vector3 center = transform.TransformPoint(body.center);
        float half = Mathf.Max(body.height * 0.5f - body.radius, 0f);
        float radius = Mathf.Max(0.05f, body.radius + body.skinWidth);
        int count = Physics.OverlapCapsuleNonAlloc(center - up * half, center + up * half, radius, shieldTouch, ~0, QueryTriggerInteraction.Collide);
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
    Vector3 travelStart;
    Vector3 travelEnd;
    float travelElapsed;
    Hd2GrindRail travelRail;
    float travelSign = 1f;
    bool grinding;
    Hd2GrindRail grindRail;
    float grindDistance;
    float grindSign = 1f;
    Component pickup;
    float lastHealth = 60f;
    bool combatBlitzSent;
    Transform upperL;
    Transform foreL;
    Transform handL;
    Transform upperR;
    Transform foreR;
    Transform handR;
    float tipReachL = 0.06f;
    float tipReachR = 0.06f;
    Vector3 tipLocalL = new Vector3(0f, 0.06f, 0f);
    Vector3 tipLocalR = new Vector3(0f, 0.06f, 0f);
    SkinnedMeshRenderer fistSkinR;
    SkinnedMeshRenderer fistSkinL;
    Vector3 fistLocalR;
    Vector3 fistLocalL;
    float fistAlongR = 0.12f;
    float fistAlongL = 0.12f;
    bool fistReadyR;
    bool fistReadyL;
    Transform thighL;
    Transform thighR;
    Transform shinL;
    Transform shinR;
    Transform footL;
    Transform footR;
    Transform toeL;
    Transform toeR;
    Quaternion restThighL;
    Quaternion restThighR;
    Quaternion restShinL;
    Quaternion restShinR;
    Quaternion restFootL;
    Quaternion restFootR;
    float gait;
    float gaitWeight;
    int turnSign;
    Vector3 aimDir;
    bool aimReady;
    bool aimOnTarget;
    Quaternion lastFacing;
    bool facingReady;
    float attackAt;
    Transform noticed;
    bool ragdolling;
    float killMarkUntil;
    Transform killMark;
    readonly List<Transform> poseBones = new List<Transform>();
    readonly List<Vector3> posePositions = new List<Vector3>();
    readonly List<Quaternion> poseRotations = new List<Quaternion>();
    readonly List<Rigidbody> ragdollBodies = new List<Rigidbody>();
    readonly List<CharacterJoint> ragdollJoints = new List<CharacterJoint>();
    readonly List<Collider> silencedGunColliders = new List<Collider>();
    bool retreating;
    Hd2TeamButton objective;
    float nextObjectiveAt;
    float nextHopAt;
    float nextPlatformAt;
    float huntRefreshAt;
    Transform huntTarget;
    Transform homeSpawn;
    Transform pressHand;
    bool pressingButton;
    AudioSource walkLoop;
    AudioSource grindLoop;
    AudioSource thrustLoop;
    AudioSource blitzSource;
    AudioSource landSource;
    float airborneTime;
    ParticleSystem[] jumpExhaust;
    ParticleSystem blitzTrail;
    bool jumpExhaustOn;

    void Start()
    {
        body = gameObject.AddComponent<CharacterController>();
        body.height = 1.8f;
        body.radius = 0.25f;
        body.center = new Vector3(0f, 0.95f, 0f);
        body.stepOffset = 0.5f;
        body.skinWidth = 0.02f;
        body.minMoveDistance = 0.001f;
        Hd2SpawnField.ApplyPass(this, body);

        health = gameObject.AddComponent<Hd2Health>();
        health.autoRespawn = false;
        health.Changed += OnHealth;
        meter = gameObject.AddComponent<Hd2ThrustMeter>();
        lastHealth = health.Health;

        var eyeObject = new GameObject("Eye");
        eyeObject.transform.SetParent(transform, false);
        eyeObject.transform.localPosition = new Vector3(0f, 1.45f, 0.1f);
        eye = eyeObject.transform;

        rightHand = MakeHand("RightHand", new Vector3(0.22f, 1.2f, 0.28f));
        leftHand = MakeHand("LeftHand", new Vector3(-0.22f, 1.2f, 0.28f));
        if (pistolModel != null)
        {
            GivePistol(rightHand, pistolModel);
            GivePistol(leftHand, pistolModel);
        }

        if (robotPrefab != null)
        {
            var robot = Instantiate(robotPrefab, transform);
            robot.name = "Body";
            robot.transform.localPosition = Vector3.zero;
            robot.transform.localRotation = Quaternion.identity;
            robot.transform.localScale = Vector3.one;
            Hd2RobotCleanup.StripImportedDevices(robot);
            var animators = robot.GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < animators.Length; i++)
                animators[i].enabled = false;
            StandBody(robot.transform);
            FitHeight(robot.transform);
            Hd2PracticeBot.FitHitboxes(robot.transform);
            IgnoreOwnColliders(robot);
            BindLimbs(robot.transform);
            SeatHands();
            if (useBodyColor)
                TintRenderers(robot.transform, bodyColor);
            AddFillers(robot.transform);
        }

        Hd2VisionCone.Register(transform);
        var players = FindObjectsByType<Hd2Locomotion>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] != null)
                Hd2VisionCone.Register(players[i].transform);
        }

        CacheCenter();
        CacheHome();
        CreateBodyAudio();
        EnterWander();
        lastMovedAt = Time.time;
        stuckAnchor = transform.position;
        stuckSince = Time.time;
    }

    void CreateBodyAudio()
    {
        walkLoop = LoopVoice("Audio/RobotWalk", 0.32f);
        grindLoop = LoopVoice("Audio/RailGrindLoop", 0.5f);
        thrustLoop = LoopVoice("Audio/JumpThrust", 1f);
        blitzSource = ShotVoice("Audio/RobotBlitz");
        landSource = ShotVoice("Audio/RobotLand");
    }

    AudioSource LoopVoice(string path, float volume)
    {
        AudioClip clip = Resources.Load<AudioClip>(path);
        if (clip == null)
            return null;

        var source = gameObject.AddComponent<AudioSource>();
        source.clip = clip;
        source.loop = true;
        source.playOnAwake = false;
        source.volume = volume;
        PlaceVoice(source);
        return source;
    }

    AudioSource ShotVoice(string path)
    {
        AudioClip clip = Resources.Load<AudioClip>(path);
        if (clip == null)
            return null;

        var source = gameObject.AddComponent<AudioSource>();
        source.clip = clip;
        source.loop = false;
        source.playOnAwake = false;
        PlaceVoice(source);
        return source;
    }

    static void PlaceVoice(AudioSource source)
    {
        source.spatialBlend = 1f;
        source.dopplerLevel = 0f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 2f;
        source.maxDistance = 14f;
    }

    void UpdateBodyAudio()
    {
        bool dead = health != null && health.IsDead;
        bool walking = !dead && !traveling && !grinding && body != null && body.enabled && body.isGrounded
            && moveDir.sqrMagnitude > 0.04f;
        SetLoop(walkLoop, walking);
        if (walking && walkLoop != null)
        {
            bool fast = (mode == Mode.Combat && combat == CombatKind.Sprint)
                || (domination && mode == Mode.Wander && !retreating);
            walkLoop.pitch = fast ? 1.55f : 1f;
            walkLoop.volume = 0.32f;
        }

        SetLoop(grindLoop, !dead && grinding && grindRail != null);
        SetLoop(thrustLoop, !dead && (traveling || jumpBoosting));
        SetJumpExhaust(!dead && jumpBoosting);
        if (dead)
        {
            airborneTime = 0f;
            return;
        }

        bool airborne = !traveling && !grinding && body != null && body.enabled && !body.isGrounded;
        if (airborne)
        {
            airborneTime += Time.deltaTime;
            return;
        }

        if (airborneTime > 0.2f && landSource != null && landSource.clip != null)
            landSource.PlayOneShot(landSource.clip, 0.85f);
        airborneTime = 0f;
    }

    void SetJumpExhaust(bool playing)
    {
        if (playing == jumpExhaustOn && jumpExhaust != null)
            return;

        if (playing && jumpExhaust == null)
        {
            Transform robot = transform.Find("Body");
            if (robot == null)
                return;
            jumpExhaust = Hd2Locomotion.BuildJumpExhaust(robot);
        }

        if (jumpExhaust == null)
            return;

        Hd2Locomotion.DriveJumpExhaust(jumpExhaust, playing);
        jumpExhaustOn = playing;
    }

    static void SetLoop(AudioSource source, bool play)
    {
        if (source == null)
            return;
        if (play)
        {
            if (!source.isPlaying)
                source.Play();
        }
        else if (source.isPlaying)
            source.Stop();
    }

    /// <summary>
    /// Round reset. Clears a ragdoll and stands the bot on its base spawn.
    /// </summary>
    public void ReturnToBase()
    {
        StopAllCoroutines();
        if (ragdolling)
        {
            FreezeRagdoll();
            for (int i = ragdollJoints.Count - 1; i >= 0; i--)
            {
                if (ragdollJoints[i] != null)
                    Destroy(ragdollJoints[i]);
            }

            ragdollJoints.Clear();
            RestorePose();
            for (int i = ragdollBodies.Count - 1; i >= 0; i--)
            {
                if (ragdollBodies[i] != null)
                    Destroy(ragdollBodies[i]);
            }

            ragdollBodies.Clear();
            RestoreGunColliders();
            ragdolling = false;
        }

        retreating = false;
        if (homeSpawn == null)
            CacheHome();
        if (health != null)
        {
            health.Respawn();
            lastHealth = health.Health;
        }

        if (homeSpawn != null)
        {
            if (body != null)
                body.enabled = false;
            transform.SetPositionAndRotation(homeSpawn.position, homeSpawn.rotation);
            if (body != null)
                body.enabled = true;
            if (health != null)
                health.RememberSpawn();
        }

        ApplyBodyColor();
        EnterWander();
        lastMovedAt = Time.time;
    }

    Transform MakeHand(string handName, Vector3 local)
    {
        var hand = new GameObject(handName);
        hand.transform.SetParent(transform, false);
        hand.transform.localPosition = local;
        hand.transform.localRotation = Quaternion.identity;
        return hand.transform;
    }

    void GivePistol(Transform hand, GameObject model)
    {
        var gun = Instantiate(model, hand);
        gun.name = "Pistol";
        gun.transform.localPosition = new Vector3(0f, 0.01f, 0.05f);
        gun.SetActive(false);
        var pistol = gun.AddComponent<Hd2Pistol>();
        pistol.hand = hand.name == "LeftHand" ? Hd2Pistol.Hand.Left : Hd2Pistol.Hand.Right;
        Transform muzzle = FindNamed(gun.transform, "Muzzle");
        if (muzzle != null)
            pistol.muzzle = muzzle;

        gun.SetActive(true);
    }

    void RestorePistols()
    {
        if (pistolModel == null)
            return;

        ClearGuns(rightHand);
        ClearGuns(leftHand);
        if (rightHand != null)
            GivePistol(rightHand, pistolModel);
        if (leftHand != null)
            GivePistol(leftHand, pistolModel);
        SeatOneHand(rightHand);
        SeatOneHand(leftHand);
    }

    static void ClearGuns(Transform hand)
    {
        if (hand == null)
            return;

        DestroyGuns<Hd2Smg>(hand);
        DestroyGuns<Hd2RocketLauncher>(hand);
        DestroyGuns<Hd2Shocks>(hand);
        DestroyGuns<Hd2LaserPistol>(hand);
        DestroyGuns<Hd2Shotgun>(hand);
        DestroyGuns<Hd2BurstPistol>(hand);
        DestroyGuns<Hd2Pistol>(hand);
    }

    static void DestroyGuns<T>(Transform hand) where T : Component
    {
        T[] guns = hand.GetComponentsInChildren<T>(true);
        for (int i = 0; i < guns.Length; i++)
        {
            if (guns[i] != null)
                Object.Destroy(guns[i].gameObject);
        }
    }

    void OnHealth(Hd2Health current)
    {
        if (current.Health < lastHealth)
        {
            Transform attacker = FighterBody(current.LastAttacker);
            if (attacker != null && attacker != transform)
            {
                if (payloadRole != Hd2PayloadRole.None)
                    returnFire = attacker;
                else
                    Engage(attacker);
            }
        }

        if (current.Health <= 0f)
        {
            returnFire = null;
            noticed = null;
            payloadGoalReady = false;
            StopMotion();
            if (body != null)
                body.enabled = false;
            lastHealth = current.maxHealth;
            if (!ragdolling)
                StartCoroutine(Die(current.LastAttacker, current.LastHitPoint));
            return;
        }

        lastHealth = current.Health;
    }

    void Update()
    {
        NoteFacingJump();
        pressingButton = false;
        if (Hd2Deathmatch.Over || Hd2Shootout.Decided || (domination && MatchDecided()))
        {
            RetractPress();
            StopMotion();
            if (body != null && !body.enabled && health != null && health.Health > 0f)
                body.enabled = true;
            ReleaseGuns();
            UpdateBodyAudio();
            return;
        }

        FaceKillMark();
        if (health != null && health.IsDead)
        {
            RetractPress();
            ReleaseGuns();
            UpdateBodyAudio();
            return;
        }

        if (body != null && !body.enabled && health != null && health.Health > 0f)
        {
            body.enabled = true;
            EnterWander();
            stuckAnchor = transform.position;
            stuckSince = Time.time;
        }

        if (domination)
            TickDominationGate();

        if (health == null || !health.IsDead)
            GrabNearbyWeapon();

        if (!retreating)
        {
            if (domination)
                TrackEnemies();
            else if (payloadRole == Hd2PayloadRole.None && mode == Mode.Wander && SeesEnemy(out Transform seen))
                Engage(seen);
        }

        if (mode == Mode.Combat && domination && target != null && Friendly(target))
            EnterWander();

        if (mode == Mode.Combat && !TargetAlive())
            EnterWander();

        bool finishingRail = grinding || (traveling && travelRail != null);
        bool waitingToAttack = mode == Mode.Combat && Time.time < attackAt;
        if (!Hd2Shootout.Present && payloadRole == Hd2PayloadRole.None && Time.time >= modeUntil && !finishingRail && !waitingToAttack && (!domination || mode == Mode.Combat))
            PickSubmode();

        if (retreating)
            TickRetreat();
        else if (payloadRole != Hd2PayloadRole.None)
            TickPayload();
        else if (Hd2Shootout.Present)
            TickShootout();
        else if (domination && mode == Mode.Wander)
            TickObjective();
        else if (mode == Mode.Wander)
            TickWander();
        else
            TickCombat();

        TickTravel();
        if (!traveling && !grinding)
            TickWalk();
        EscapeIfStuck();
        DrownIfOnWater();
        if (!pressingButton)
            RetractPress();
        UpdateBodyAudio();
        lastFacing = transform.rotation;
        facingReady = true;
    }

    void NoteFacingJump()
    {
        if (!facingReady)
            return;

        // A respawn or other teleport can yaw the body in one frame. Start the
        // aim sweep from the new facing instead of the pre-teleport direction.
        float jumped = Quaternion.Angle(lastFacing, transform.rotation);
        if (jumped > TurnBudget() + 2f)
            aimReady = false;
    }

    static float TurnBudget()
    {
        return TurnRate * Mathf.Min(Time.deltaTime, 0.05f);
    }

    void LateUpdate()
    {
        FaceKillMark();
        if (health != null && health.IsDead)
            return;
        PoseLegs();
    }

    void OnEnable()
    {
        if (!active.Contains(this))
            active.Add(this);
    }

    void OnDisable()
    {
        active.Remove(this);
    }

    void OnDestroy()
    {
        Hd2VisionCone.Unregister(transform);
        if (killMark != null)
            Destroy(killMark.gameObject);
    }

    void EnterWander()
    {
        mode = Mode.Wander;
        target = null;
        noticed = null;
        lostTargetAt = -1f;
        StopMotion();
        ReleaseGuns();
        if (domination)
        {
            moveDir = Vector3.zero;
            modeUntil = Time.time;
            return;
        }

        PickSubmode();
    }

    void TickDominationGate()
    {
        if (health == null || health.maxHealth <= 0f)
            return;

        float frac = health.Health / health.maxHealth;
        if (frac <= retreatFraction)
        {
            if (!retreating)
            {
                retreating = true;
                StopMotion();
            }

            target = null;
            noticed = null;
            mode = Mode.Wander;
            ReleaseGuns();
        }
        else if (retreating && frac >= retreatFraction + 0.3f)
        {
            retreating = false;
        }
    }

    void TickRetreat()
    {
        ReleaseGuns();
        Vector3 at = FallbackPoint();
        float gap = Horizontal(at);
        moveDir = gap < 1.5f ? Vector3.zero : Flat(at - transform.position);
        if (gap < 1.5f && health != null)
            health.Heal(retreatHealPerSecond * Time.deltaTime);
        moveDir = ClearOfWater(moveDir);
        if (WallAhead(1.6f, out Vector3 away) && moveDir.sqrMagnitude > 0.0001f)
            moveDir = Vector3.Slerp(moveDir, away, 0.5f).normalized;
        if (moveDir.sqrMagnitude > 0.0001f)
            Face(moveDir);
        AimAt(transform.position + Vector3.up * 1.2f + transform.forward * 6f, false);
    }

    void TickObjective()
    {
        ReleaseGuns();
        if (Time.time >= nextObjectiveAt || objective == null)
        {
            objective = ChoosePoint();
            nextObjectiveAt = Time.time + 1.2f;
        }

        Vector3 at = objective != null ? objective.transform.position : HomePosition();
        float gap = Horizontal(at);
        Hd2Team enemy = EnemyOf(team);
        bool ours = objective != null && team != Hd2Team.None && objective.Owner == team
            && objective.OccupantTeam != enemy;
        if (gap < 1.35f && ours)
        {
            moveDir = Vector3.zero;
            AimAt(transform.position + Vector3.up * 1.2f + transform.forward * 8f, false);
            return;
        }

        moveDir = gap < 0.55f ? Vector3.zero : Flat(at - transform.position);
        if (WallAhead(1.6f, out Vector3 away) && moveDir.sqrMagnitude > 0.0001f)
        {
            Vector3 blended = Vector3.Slerp(moveDir, away, 0.5f).normalized;
            if (!WaterAlong(blended, 2.4f))
                moveDir = blended;
        }

        moveDir = ClearOfWater(moveDir);
        if (moveDir.sqrMagnitude > 0.0001f)
            Face(moveDir);
        if (gap < 1.6f && objective != null)
            AimAt(at + Vector3.up * 0.7f, false);
        else
            AimAt(transform.position + Vector3.up * 1.2f + transform.forward * 8f, false);

        if (objective != null && !ours && gap < 1.25f)
            ReachButton(objective);

        if (!traveling && !grinding && gap > 14f && meter != null && meter.Charges >= 1f && moveDir.sqrMagnitude > 0.0001f)
            TryBlitz(BlitzLanding(moveDir, false), null, 1f);
    }

    void TickPayload()
    {
        Hd2Payload cart = FindFirstObjectByType<Hd2Payload>();
        if (cart == null)
        {
            TickWander();
            return;
        }

        RefreshPayloadGoal(cart);
        Vector3 goal = payloadGoalOnCart || payloadPoi == null
            ? cart.StandOnDeck(spawnSlot)
            : payloadPoi.transform.position;
        float gap = Horizontal(goal);
        bool onDeck = cart.FeetOnDeck(transform.position);
        bool riding = false;
        bool holdRail = grinding && grindRail == cart.rail && grindSign >= 0f && StillBehindCart(cart);
        float hopOff = payloadGoalOnCart ? 3.5f : 8f;
        if (!holdRail && (onDeck || gap <= hopOff))
        {
            payloadRailRide = false;
            if (grinding)
                DismountRail();
        }
        else if (cart.rail != null && (payloadRailRide || (team == Hd2Team.Blue && payloadGoalOnCart) || PathBlocked(goal) || PayloadStalled(gap)))
        {
            payloadRailRide = true;
            riding = GuideToRail(cart.rail, goal);
        }

        if (!riding)
        {
            bool ahead = onDeck && !payloadGoalOnCart && GoalAheadOfCart();
            if (ahead && !traveling)
            {
                moveDir = Vector3.zero;
                BlitzOverFront();
            }
            else if (onDeck || gap < 1.6f)
                moveDir = Vector3.zero;
            else
                moveDir = Flat(goal - transform.position);

            moveDir = ClearOfWater(moveDir);
            if (WallAhead(1.6f, out Vector3 away) && moveDir.sqrMagnitude > 0.0001f)
                moveDir = Vector3.Slerp(moveDir, away, 0.5f).normalized;

            if (!onDeck && !traveling && !grinding && gap > 7f && gap < 30f && meter != null && meter.Charges >= 1f)
                TryBlitz(goal, null, 1f);
        }
        else if (!grinding)
        {
            moveDir = ClearOfWater(moveDir);
            if (WallAhead(1.6f, out Vector3 away) && moveDir.sqrMagnitude > 0.0001f)
                moveDir = Vector3.Slerp(moveDir, away, 0.5f).normalized;
        }

        Transform foe = PayloadFoe();
        if (foe != null)
        {
            ArmNotice(FighterBody(foe));
            Vector3 to = Flat(foe.position - transform.position);
            if (to.sqrMagnitude > 0.0001f)
                Face(to);
            AimAt(foe.position + Vector3.up * 1.1f, true);
            Shoot(true);
        }
        else
        {
            noticed = null;
            ReleaseGuns();
            if (moveDir.sqrMagnitude > 0.0001f)
                Face(moveDir);
            if (grinding && grindRail != null)
            {
                Vector3 ahead = grindRail.PointAlong(Mathf.Clamp(grindDistance + grindSign * 2f, 0f, grindRail.length));
                Face(Flat(ahead - transform.position));
            }
            AimAt(transform.position + Vector3.up * 1.2f + transform.forward * 8f, false);
        }
    }

    bool GuideToRail(Hd2GrindRail rail, Vector3 goal)
    {
        if (rail == null || rail.length < 1f || traveling)
            return false;

        float along = grinding && grindRail == rail ? grindDistance : rail.DistanceAlong(transform.position);
        float bestAlong = rail.DistanceAlong(goal);
        if (!payloadGoalOnCart && FlatDistance(rail.PointAlong(bestAlong), goal) + 2f >= FlatDistance(transform.position, goal))
        {
            payloadRailRide = false;
            if (grinding && grindRail == rail)
                DismountRail();
            return false;
        }
        if (grinding && grindRail == rail)
        {
            float nextAlong = Mathf.Clamp(along + grindSign * 4f, 0f, rail.length);
            Vector3 nextPoint = rail.PointAlong(nextAlong);
            Hd2Payload cart = Hd2Payload.Current;
            bool intoCart = cart != null && cart.rail == rail && grindSign >= 0f && StillBehindCart(cart);
            if (!intoCart && Hd2Payload.BlocksRoute(transform.position, nextPoint))
            {
                payloadRailRide = false;
                DismountRail();
                return false;
            }

            float here = FlatDistance(transform.position, goal);
            float next = FlatDistance(nextPoint, goal);
            if (!payloadGoalOnCart && next > here + 0.75f)
            {
                payloadRailRide = false;
                DismountRail();
                return false;
            }

            grindSign = SignToward(rail, along, goal);
            return true;
        }

        Vector3 mount = rail.PointAlong(along);
        float sign = SignToward(rail, along, goal);
        float mountGap = Horizontal(mount);
        if (mountGap >= 0.8f && mountGap <= BlitzRange)
        {
            if (meter != null && meter.Charges >= 1f)
                meter.TrySpend();
            BeginTravel(mount, rail, sign);
            return true;
        }

        if (mountGap > BlitzRange)
        {
            moveDir = Flat(mount - transform.position);
            return true;
        }

        grinding = true;
        grindRail = rail;
        grindDistance = along;
        grindSign = sign;
        moveDir = Vector3.zero;
        smoothedMove = Vector3.zero;
        if (body != null)
            body.enabled = false;
        return true;
    }

    static float SignToward(Hd2GrindRail rail, float along, Vector3 goal)
    {
        float targetAlong = rail.DistanceAlong(goal);
        if (targetAlong >= along)
            return 1f;
        return -1f;
    }

    static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    bool StillBehindCart(Hd2Payload cart)
    {
        if (cart == null || cart.rail == null)
            return false;
        cart.RailSpan(out _, out float nose);
        return grindDistance < nose - 0.5f;
    }

    bool GoalAheadOfCart()
    {
        Hd2Payload cart = Hd2Payload.Current;
        if (cart == null || cart.rail == null || payloadPoi == null)
            return false;
        cart.RailSpan(out _, out float nose);
        return cart.rail.DistanceAlong(payloadPoi.transform.position) > nose + 2f;
    }

    void BlitzOverFront()
    {
        Hd2Payload cart = Hd2Payload.Current;
        if (cart == null || cart.rail == null || traveling)
            return;
        if (meter != null && !meter.TrySpend())
            return;
        BeginTravel(cart.PointPastNose(4f), cart.rail, 1f);
    }

    bool CatchCart(Hd2Payload cart)
    {
        if (!cart.TryPlaceFromRear(grindDistance, grindSign, out Vector3 deck))
            return false;
        BoardFromRear(deck);
        return true;
    }

    void DismountRail()
    {
        grinding = false;
        grindRail = null;
        traveling = false;
        travelRail = null;
        smoothedMove = Vector3.zero;
        StopBlitzTrail();
        if (body != null)
            body.enabled = true;
    }

    void PlayBlitzTrail()
    {
        EnsureBlitzTrail();
        if (blitzTrail == null)
            return;

        var main = blitzTrail.main;
        Color edge = bodyColor;
        edge.a = 0.95f;
        Color spark = Color.Lerp(bodyColor, Color.white, 0.18f);
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
        Material trailMaterial = Hd2Shot.ParticleMaterial();
        if (trailMaterial != null)
            renderer.sharedMaterial = trailMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    bool PayloadStalled(float gap)
    {
        if (grinding || payloadRailRide)
            return false;
        if (gap + 0.75f < payloadClosestGap)
        {
            payloadClosestGap = gap;
            payloadProgressAt = Time.time;
            return false;
        }

        if (payloadProgressAt <= 0f)
            payloadProgressAt = Time.time;
        return Time.time - payloadProgressAt > 2.5f;
    }

    bool PathBlocked(Vector3 goal)
    {
        Vector3 flat = goal - transform.position;
        flat.y = 0f;
        float distance = flat.magnitude;
        if (distance < 3f)
            return false;

        Vector3 dir = flat / distance;
        int count = Probe(transform.position + Vector3.up * 0.9f, dir, distance);
        for (int i = 0; i < count; i++)
        {
            Collider collider = probeHits[i].collider;
            if (collider == null || collider.transform.root == transform)
                continue;
            if (probeHits[i].normal.y > 0.5f)
                continue;
            if (collider.GetComponentInParent<Hd2GrindRail>() != null)
                continue;
            if (collider.GetComponentInParent<Hd2Payload>() != null)
                continue;
            if (probeHits[i].distance < distance - 1.5f)
                return true;
        }

        return false;
    }

    void RefreshPayloadGoal(Hd2Payload cart)
    {
        if (payloadGoalReady && Time.time < payloadGoalUntil && (payloadGoalOnCart || payloadPoi != null))
            return;

        Hd2Poi.AdoptNamed();
        int count = ClosestPois(cart.transform.position);
        float payloadWeight = team == Hd2Team.Blue ? 2f : 1f;
        float total = payloadWeight + count;
        float roll = Random.value * total;
        payloadGoalOnCart = count == 0 || roll < payloadWeight;
        payloadPoi = payloadGoalOnCart ? null : poiNear[Mathf.Min(count - 1, (int)(roll - payloadWeight))];
        payloadGoalReady = true;
        payloadGoalUntil = Time.time + Random.Range(6f, 10f);
        payloadClosestGap = float.MaxValue;
        payloadProgressAt = Time.time;
    }

    static int ClosestPois(Vector3 cart)
    {
        for (int i = 0; i < poiNear.Length; i++)
        {
            poiNear[i] = null;
            poiNearDist[i] = float.MaxValue;
        }

        List<Hd2Poi> points = Hd2Poi.All;
        int count = 0;
        for (int i = 0; i < points.Count; i++)
        {
            Hd2Poi point = points[i];
            if (point == null)
                continue;
            Vector3 delta = point.transform.position - cart;
            delta.y = 0f;
            float distance = delta.sqrMagnitude;
            int slot = count < poiNear.Length ? count : poiNear.Length - 1;
            if (count < poiNear.Length)
                count++;
            else if (distance >= poiNearDist[slot])
                continue;

            while (slot > 0 && distance < poiNearDist[slot - 1])
            {
                poiNear[slot] = poiNear[slot - 1];
                poiNearDist[slot] = poiNearDist[slot - 1];
                slot--;
            }

            poiNear[slot] = point;
            poiNearDist[slot] = distance;
        }

        return count;
    }

    Transform PayloadFoe()
    {
        if (returnFire != null)
        {
            var life = returnFire.GetComponent<Hd2Health>();
            bool gone = returnFire == transform
                || Friendly(returnFire)
                || (life != null && life.IsDead)
                || Horizontal(returnFire.position) > DropCombatRange;
            if (gone)
                returnFire = null;
            else
                return returnFire;
        }

        if (LookForEnemy(out Transform seen, 22f, 75f))
            return seen;
        return null;
    }

    void ReachButton(Hd2TeamButton point)
    {
        if (point == null)
            return;

        if (pressHand == null)
        {
            var hand = new GameObject("RightHand");
            hand.transform.SetParent(transform, false);
            var sphere = hand.AddComponent<SphereCollider>();
            sphere.isTrigger = true;
            sphere.radius = 0.05f;
            pressHand = hand.transform;
        }

        pressingButton = true;
        if (!pressHand.gameObject.activeSelf)
            pressHand.gameObject.SetActive(true);
        pressHand.position = point.TouchPoint;
        point.OfferTouch(this);
    }

    void RetractPress()
    {
        pressingButton = false;
        if (pressHand != null && pressHand.gameObject.activeSelf)
            pressHand.gameObject.SetActive(false);
    }

    static bool MatchDecided()
    {
        if (Hd2Shootout.Present)
            return Hd2Shootout.Decided;
        if (Hd2ControlPoint.Present)
            return Hd2ControlPoint.Decided;
        return Hd2Domination.Decided;
    }

    Hd2TeamButton ChoosePoint()
    {
        if (Hd2ControlPoint.Present)
        {
            if (Hd2ControlPoint.Point != null)
                return Hd2ControlPoint.Point;

            var buttons = Hd2TeamButton.All;
            for (int i = 0; i < buttons.Count; i++)
            {
                if (buttons[i] != null)
                    return buttons[i];
            }

            return null;
        }

        var all = Hd2TeamButton.All;
        Hd2TeamButton best = null;
        float bestScore = float.NegativeInfinity;
        float weight = Mathf.Clamp(objectiveWeight, 0f, 2f) * 0.5f;
        for (int i = 0; i < all.Count; i++)
        {
            Hd2TeamButton point = all[i];
            if (point == null)
                continue;
            float score = ScorePoint(point, weight);
            if (score <= bestScore)
                continue;
            bestScore = score;
            best = point;
        }

        return best;
    }

    float ScorePoint(Hd2TeamButton point, float weight)
    {
        float distance = Horizontal(point.transform.position);
        float near = 1f / (1f + distance * 0.04f);
        Hd2Team enemy = EnemyOf(team);
        float weakness = point.Owner == enemy ? 1f : point.Owner == Hd2Team.None ? 0.55f : 0.12f;
        float contested = 0f;
        if (point.Occupied && point.OccupantTeam == enemy)
            contested = 1f;
        else if (point.Occupied && point.Owner == Hd2Team.None)
            contested = 0.45f;

        float score = Mathf.Lerp(near, weakness + contested * 0.85f, weight);
        if (!string.IsNullOrEmpty(pointBias) && point.Label == pointBias)
            score += 0.08f;
        return score;
    }

    Vector3 FallbackPoint()
    {
        var all = Hd2TeamButton.All;
        Vector3 pick = HomePosition();
        float best = float.MaxValue;
        bool found = false;
        for (int i = 0; i < all.Count; i++)
        {
            Hd2TeamButton point = all[i];
            if (point == null || point.Owner != team)
                continue;
            float distance = Horizontal(point.transform.position);
            if (distance >= best)
                continue;
            best = distance;
            pick = point.transform.position;
            found = true;
        }

        if (!found && homeSpawn != null)
            return homeSpawn.position;
        return pick;
    }

    Vector3 HomePosition()
    {
        return homeSpawn != null ? homeSpawn.position : transform.position;
    }

    public Transform Home => homeSpawn;

    public int CopyArmPoints(Vector3[] points, int count)
    {
        count = ArmSegment(upperL, foreL, points, count);
        count = ArmSegment(foreL, handL, points, count);
        count = ArmSegment(upperR, foreR, points, count);
        count = ArmSegment(foreR, handR, points, count);
        return count;
    }

    static int ArmSegment(Transform from, Transform to, Vector3[] points, int count)
    {
        if (from == null || to == null || count >= points.Length - 2)
            return count;
        points[count++] = from.position;
        points[count++] = Vector3.Lerp(from.position, to.position, 0.5f);
        if (count < points.Length)
            points[count++] = to.position;
        return count;
    }

    public void TakePlace(Hd2Team side, Color color, int slot, Transform spawn)
    {
        team = side;
        bodyColor = color;
        useBodyColor = true;
        spawnSlot = slot;
        if (side == Hd2Team.Red && displayName != null && displayName.StartsWith("blue"))
            displayName = "red" + displayName.Substring(4);
        else if (side == Hd2Team.Blue && displayName != null && displayName.StartsWith("red"))
            displayName = "blue" + displayName.Substring(3);

        CacheHome();
        if (spawn != null)
            homeSpawn = spawn;

        Transform suit = transform.Find("Body");
        if (suit != null)
            TintRenderers(suit, color);
        if (health != null && health.IsDead)
            return;
        if (homeSpawn == null)
            return;

        retreating = false;
        if (body != null)
            body.enabled = false;
        transform.SetPositionAndRotation(homeSpawn.position, homeSpawn.rotation);
        if (body != null)
            body.enabled = true;
        if (health != null)
            health.RememberSpawn();
        EnterWander();
    }

    void CacheHome()
    {
        if (team == Hd2Team.None)
            return;
        if (!domination && payloadRole == Hd2PayloadRole.None && !Hd2Shootout.Present)
            return;

        var points = FindObjectsByType<Hd2SpawnPoint>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < points.Length; i++)
        {
            Hd2SpawnPoint point = points[i];
            if (point == null || point.team != team)
                continue;

            int rank = 0;
            for (int j = 0; j < points.Length; j++)
            {
                Hd2SpawnPoint other = points[j];
                if (other == null || other == point || other.team != team)
                    continue;
                if (other.index < point.index || (other.index == point.index && j < i))
                    rank++;
            }

            if (rank != spawnSlot)
                continue;

            homeSpawn = point.transform;
            return;
        }
    }

    bool Friendly(Transform other)
    {
        if (other == null || team == Hd2Team.None)
            return false;

        Hd2EnemyBot bot = other.GetComponent<Hd2EnemyBot>();
        if (bot != null)
            return bot.team == team;

        Hd2Locomotion player = other.GetComponent<Hd2Locomotion>();
        return player != null && player.team == team;
    }

    static Hd2Team EnemyOf(Hd2Team side)
    {
        if (side == Hd2Team.Red)
            return Hd2Team.Blue;
        if (side == Hd2Team.Blue)
            return Hd2Team.Red;
        return Hd2Team.None;
    }

    void Engage(Transform enemy)
    {
        if (retreating)
            return;

        Transform body = FighterBody(enemy);
        if (body == null || body == transform)
            return;
        if (domination && Friendly(body))
            return;
        bool same = mode == Mode.Combat && noticed == body;
        target = body;
        mode = Mode.Combat;
        lostTargetAt = -1f;
        ArmNotice(body);
        if (same)
            return;

        moveDir = Vector3.zero;
    }

    void ArmNotice(Transform body)
    {
        if (body == null)
        {
            noticed = null;
            return;
        }

        if (noticed == body)
            return;

        if (noticed == null || Time.time < attackAt)
        {
            attackAt = Time.time + NoticeDelay;
            modeUntil = attackAt;
        }

        noticed = body;
    }

    static Transform FighterBody(Transform enemy)
    {
        if (enemy == null)
            return null;

        Hd2EnemyBot bot = enemy.GetComponentInParent<Hd2EnemyBot>();
        if (bot != null)
            return bot.transform;

        Hd2Locomotion player = enemy.GetComponentInParent<Hd2Locomotion>();
        if (player != null)
            return player.transform;

        return enemy;
    }

    bool TargetAlive()
    {
        if (target == null)
            return false;
        var life = target.GetComponentInChildren<Hd2Health>();
        if (life != null && life.IsDead)
            return false;
        return Vector3.Distance(transform.position, target.position) <= DropCombatRange;
    }

    void PickSubmode()
    {
        if (mode == Mode.Wander)
        {
            bool chosen = false;
            for (int attempt = 0; attempt < 8 && !chosen; attempt++)
            {
                wander = (WanderKind)Random.Range(0, 5);
                if (wander == WanderKind.Rail)
                {
                    chosen = TryStartRail();
                    continue;
                }

                if (wander == WanderKind.Pickup)
                {
                    chosen = FindWeapon(10f, out pickup);
                    continue;
                }

                chosen = true;
            }

            if (!chosen)
                wander = WanderKind.Walk;
            if (wander == WanderKind.Walk || wander == WanderKind.Blitz)
                moveDir = WanderDirection();
            else if (wander == WanderKind.Turn)
            {
                Turn();
                moveDir = Vector3.zero;
            }

            modeUntil = Time.time + Random.Range(1f, 10f);
        }
        else
        {
            combat = (CombatKind)Random.Range(0, 3);
            modeUntil = Time.time + Random.Range(2f, 5f);
            BeginSideStep();
            combatBlitzSent = false;
            if (combat == CombatKind.Blitz)
                combatBlitzSent = TryBlitzBehind();
        }
    }

    void TickShootout()
    {
        RefreshHuntPlayers();
        if (huntTarget == null || !HuntAlive(huntTarget) || Time.time >= huntRefreshAt)
        {
            huntRefreshAt = Time.time + 0.4f;
            huntTarget = NearestFoe();
        }

        if (huntTarget == null)
        {
            noticed = null;
            Shoot(false);
            if (moveDir.sqrMagnitude < 0.01f)
                moveDir = transform.forward;
            AimAt(transform.position + Vector3.up * 1.2f + transform.forward * 8f, false);
            TryMountPlatform();
            return;
        }

        mode = Mode.Combat;
        ArmNotice(huntTarget);
        target = huntTarget;
        Vector3 to = huntTarget.position - transform.position;
        Vector3 flat = to;
        flat.y = 0f;
        float distance = flat.magnitude;
        if (!traveling && distance > 0.05f)
            Face(flat);

        bool clear = eye != null && distance > 0.4f && distance < 32f && !Blocked(eye.position, to.normalized, to.magnitude, huntTarget);
        AimAt(huntTarget.position + Vector3.up * 1.1f, true);
        Shoot(clear);
        if (!traveling && !grinding)
        {
            Vector3 forward = distance > 0.05f ? flat / distance : transform.forward;
            if (distance < 6f)
            {
                float side = (spawnSlot & 1) == 0 ? 1f : -1f;
                moveDir = (forward * 0.25f + Vector3.Cross(Vector3.up, forward) * side).normalized;
            }
            else
                moveDir = forward;
            moveDir = ClearOfWater(moveDir);
        }

        TryMountPlatform();
    }

    static void RefreshHuntPlayers()
    {
        if (Time.time < huntPlayerAt)
            return;

        huntPlayerAt = Time.time + 0.5f;
        huntPlayerCount = 0;
        var found = FindObjectsByType<Hd2Locomotion>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < found.Length && huntPlayerCount < huntPlayers.Length; i++)
        {
            if (found[i] != null)
                huntPlayers[huntPlayerCount++] = found[i];
        }
    }

    Transform NearestFoe()
    {
        Transform best = null;
        float bestSqr = 80f * 80f;
        for (int i = 0; i < active.Count; i++)
        {
            Hd2EnemyBot other = active[i];
            if (other == null || other == this || other.team == team || other.team == Hd2Team.None)
                continue;
            if (!HuntAlive(other.transform))
                continue;
            float sqr = (other.transform.position - transform.position).sqrMagnitude;
            if (sqr >= bestSqr)
                continue;
            bestSqr = sqr;
            best = other.transform;
        }

        for (int i = 0; i < huntPlayerCount; i++)
        {
            Hd2Locomotion player = huntPlayers[i];
            if (player == null || player.team == team || player.team == Hd2Team.None)
                continue;
            if (!HuntAlive(player.transform))
                continue;
            float sqr = (player.transform.position - transform.position).sqrMagnitude;
            if (sqr >= bestSqr)
                continue;
            bestSqr = sqr;
            best = player.transform;
        }

        return best;
    }

    static bool HuntAlive(Transform body)
    {
        if (body == null)
            return false;
        Hd2Health life = body.GetComponent<Hd2Health>();
        return life == null || !life.IsDead;
    }

    void TryMountPlatform()
    {
        if (traveling || grinding || body == null || !body.isGrounded || Time.time < nextPlatformAt)
            return;
        if (meter == null || meter.Charges < 1f)
            return;

        int count = Physics.OverlapSphereNonAlloc(transform.position, 12f, platformHits, ~0, QueryTriggerInteraction.Ignore);
        Vector3 best = transform.position;
        float bestRise = 0.8f;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            Collider collider = platformHits[i];
            platformHits[i] = null;
            if (collider == null || !IsPlatform(collider))
                continue;

            Bounds box = collider.bounds;
            float rise = box.max.y - transform.position.y;
            if (rise < 0.8f || rise > 6f)
                continue;
            if (transform.position.y > box.max.y - 0.35f
                && transform.position.x > box.min.x && transform.position.x < box.max.x
                && transform.position.z > box.min.z && transform.position.z < box.max.z)
                continue;

            Vector3 stand = new Vector3(
                Mathf.Clamp(transform.position.x, box.min.x + 0.4f, box.max.x - 0.4f),
                box.max.y,
                Mathf.Clamp(transform.position.z, box.min.z + 0.4f, box.max.z - 0.4f));
            float gap = Horizontal(stand);
            if (gap < 1.2f || gap > 12f || rise <= bestRise)
                continue;
            bestRise = rise;
            best = stand;
            found = true;
        }

        if (!found)
            return;

        nextPlatformAt = Time.time + 2.5f;
        if (!TryBlitz(best, null, 1f) && bestRise < 1.4f)
        {
            jumpBoosting = true;
            jumpTime = 0f;
        }
    }

    static bool IsPlatform(Collider collider)
    {
        Transform node = collider.transform;
        while (node != null)
        {
            if (node.GetComponent<Hd2Platform>() != null)
                return true;
            if (node.name.IndexOf("platform", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            node = node.parent;
        }

        return false;
    }

    void TickWander()
    {
        ReleaseGuns();
        if (wander == WanderKind.Pickup)
        {
            if (pickup == null || !FindWeapon(10f, out pickup))
            {
                wander = WanderKind.Walk;
                return;
            }

            Vector3 at = PickupPoint(pickup);
            moveDir = Flat(at - transform.position);
            if (moveDir.sqrMagnitude < 0.0001f || Horizontal(at) < 0.7f)
            {
                Transform hand = PistolHand();
                if (hand != null)
                    TakeWeapon(pickup, hand);
                wander = WanderKind.Walk;
                moveDir = WanderDirection();
            }
        }
        else if (wander == WanderKind.Turn)
        {
            moveDir = Vector3.zero;
        }
        else if (wander == WanderKind.Walk || wander == WanderKind.Blitz)
        {
            if (WallAhead(1.6f, out Vector3 away))
            {
                Vector3 blended = Vector3.Slerp(moveDir, away, 0.5f).normalized;
                if (!WaterAlong(blended, 2.4f))
                    moveDir = blended;
            }

            moveDir = ClearOfWater(moveDir);
            if (wander == WanderKind.Blitz && !traveling && !grinding && meter != null && meter.Charges >= 1f)
                TryBlitz(BlitzLanding(moveDir, true), null, 1f);
        }

        if (!traveling && !grinding)
            moveDir = ClearOfWater(moveDir);
        if (moveDir.sqrMagnitude > 0.0001f && !traveling && !grinding)
            Face(moveDir);
        AimAt(transform.position + Vector3.up * 1.2f + transform.forward * 8f, false);
    }

    void TickCombat()
    {
        if (target == null)
            return;

        if (Time.time < attackAt)
        {
            moveDir = Vector3.zero;
            Shoot(false);
            return;
        }

        Vector3 to = Flat(target.position - transform.position);
        if (!traveling && to.sqrMagnitude > 0.0001f)
            Face(to);
        AimAt(target.position + Vector3.up * 1.1f, true);
        Shoot(true);

        if (combat == CombatKind.Blitz)
        {
            moveDir = Vector3.zero;
            if (!combatBlitzSent && !traveling && meter != null && meter.Charges >= 1f)
                combatBlitzSent = TryBlitzBehind();
            return;
        }

        if (combat == CombatKind.Sprint || combat == CombatKind.Strafe)
        {
            if (Time.time >= strafeFlipAt)
                FlipSideStep(false);
            else if (combat == CombatKind.Strafe && !wallReversed && WallAhead(1.2f, out _))
                FlipSideStep(true);

            Vector3 side = Vector3.Cross(Vector3.up, to.sqrMagnitude > 0.0001f ? to : transform.forward).normalized * strafeSign;
            moveDir = combat == CombatKind.Strafe ? (side + to.normalized * 0.45f).normalized : side;
            moveDir = ClearOfWater(moveDir);
        }
    }

    void BeginSideStep()
    {
        strafeSign = Random.value < 0.5f ? -1 : 1;
        wallReversed = false;
        ScheduleSideStep();
    }

    void FlipSideStep(bool fromWall)
    {
        strafeSign = -strafeSign;
        wallReversed = fromWall;
        ScheduleSideStep();
    }

    void ScheduleSideStep()
    {
        float hold = combat == CombatKind.Sprint ? Random.Range(0.1f, 0.5f) : 0.35f;
        strafeFlipAt = Time.time + hold;
    }

    void Shoot(bool combatFire)
    {
        if (combatFire && (Time.time < attackAt || !aimOnTarget))
            combatFire = false;

        GetComponentsInChildren(heldSmgs);
        for (int i = 0; i < heldSmgs.Count; i++)
            heldSmgs[i].botHeld = combatFire;
        GetComponentsInChildren(heldBursts);
        for (int i = 0; i < heldBursts.Count; i++)
            heldBursts[i].botHeld = combatFire;
        GetComponentsInChildren(heldLasers);
        for (int i = 0; i < heldLasers.Count; i++)
            heldLasers[i].botHeld = combatFire;
        if (!combatFire)
            return;

        if (Time.time >= nextPistolFire)
        {
            nextPistolFire = Time.time + 0.2f;
            GetComponentsInChildren(heldPistols);
            for (int i = 0; i < heldPistols.Count; i++)
                heldPistols[i].BotTap();
        }

        if (Time.time < nextPump)
            return;
        nextPump = Time.time + 0.85f;
        GetComponentsInChildren(heldShotguns);
        for (int i = 0; i < heldShotguns.Count; i++)
            heldShotguns[i].BotTap();
        GetComponentsInChildren(heldShocks);
        for (int i = 0; i < heldShocks.Count; i++)
            heldShocks[i].BotTap();
        GetComponentsInChildren(heldRockets);
        for (int i = 0; i < heldRockets.Count; i++)
            heldRockets[i].BotTap();
    }

    void StopMotion()
    {
        traveling = false;
        grinding = false;
        grindRail = null;
        travelRail = null;
        jumpBoosting = false;
        jumpTime = 0f;
        verticalVelocity = 0f;
        moveDir = Vector3.zero;
    }

    void ReleaseGuns()
    {
        Shoot(false);
    }

    void AimAt(Vector3 point, bool shake)
    {
        Vector3 aim = point;
        Vector3 fromBody = point - transform.position;
        if (shake && fromBody.sqrMagnitude > 0.01f)
        {
            float yaw = Mathf.Sin(Time.time * 1.9f) * AimError;
            float pitch = Mathf.Sin(Time.time * 2.5f + 1.7f) * AimError * 0.55f;
            aim = transform.position + Quaternion.Euler(pitch, yaw, 0f) * fromBody;
        }

        Vector3 desired = aim - transform.position;
        if (desired.sqrMagnitude < 0.0001f)
            desired = transform.forward;
        desired.Normalize();
        if (!aimReady || aimDir.sqrMagnitude < 0.0001f)
        {
            aimDir = transform.forward;
            aimReady = true;
        }

        float maxRadians = TurnBudget() * Mathf.Deg2Rad;
        aimDir = Vector3.RotateTowards(aimDir.normalized, desired, maxRadians, 0f);
        aimOnTarget = Vector3.Angle(aimDir, desired) <= AimError + 2f;

        float distance = fromBody.magnitude;
        if (distance < 0.5f)
            distance = 8f;
        Vector3 swept = transform.position + aimDir * distance;
        AimHand(rightHand, upperR, foreR, handR, swept, 0.12f);
        AimHand(leftHand, upperL, foreL, handL, swept, -0.12f);
    }

    void AimHand(Transform hand, Transform upper, Transform fore, Transform bone, Vector3 aim, float side)
    {
        Transform gun = GunOn(hand);
        if (gun == null)
        {
            if (hand != null)
            {
                hand.localPosition = Vector3.zero;
                hand.localRotation = Quaternion.identity;
            }

            HangArm(upper, fore, bone);
            return;
        }

        bool pistol = IsPistolGrip(gun);
        Transform muzzle = FindNamed(gun, "Muzzle");
        if (muzzle == null)
            muzzle = FindNamed(gun, "Barrel");
        Vector3 from = muzzle != null ? muzzle.position : gun.position;
        Vector3 dir = aim - from;
        if (dir.sqrMagnitude < 0.01f)
            dir = transform.forward;
        dir.Normalize();

        if (upper != null && fore != null)
        {
            Vector3 shoulder = upper.position;
            float extra = pistol ? (bone == handR ? tipReachR : tipReachL) : 0f;
            Vector3 wrist = aim + transform.right * side - dir * extra;
            Vector3 reach = wrist - shoulder;
            if (reach.sqrMagnitude > 0.01f)
                PoseArm(upper, fore, bone, shoulder + reach.normalized * Mathf.Min(reach.magnitude, 0.62f));
        }

        if (pistol)
            StraightenHand(fore, bone);

        if (hand == null)
            return;

        if (pistol && bone != null)
            hand.localRotation = Quaternion.identity;
        else
            hand.localPosition = Vector3.zero;

        from = muzzle != null ? muzzle.position : gun.position;
        dir = aim - from;
        if (dir.sqrMagnitude < 0.01f)
        {
            KeepFistOnArm(hand, fore, bone);
            return;
        }
        dir.Normalize();
        Vector3 barrel = muzzle != null ? muzzle.forward : hand.forward;
        if (barrel.sqrMagnitude < 0.0001f || Vector3.Dot(barrel, dir) > 0.9995f)
        {
            KeepFistOnArm(hand, fore, bone);
            return;
        }
        hand.rotation = Quaternion.FromToRotation(barrel, dir) * hand.rotation;
        KeepFistOnArm(hand, fore, bone);
    }

    static bool IsPistolGrip(Transform gun)
    {
        return gun.GetComponent<Hd2Pistol>() != null
            || gun.GetComponent<Hd2Shocks>() != null
            || gun.GetComponent<Hd2LaserPistol>() != null
            || gun.GetComponent<Hd2BurstPistol>() != null;
    }

    static void StraightenHand(Transform fore, Transform bone)
    {
        if (fore == null || bone == null)
            return;

        Transform tip = null;
        for (int i = 0; i < bone.childCount; i++)
        {
            Transform child = bone.GetChild(i);
            if (child.name.IndexOf("Tip", System.StringComparison.Ordinal) >= 0)
                tip = child;
        }

        Vector3 along = bone.position - fore.position;
        if (along.sqrMagnitude < 0.0001f)
            return;
        Vector3 tipDir = tip != null ? tip.position - bone.position : along;
        if (tipDir.sqrMagnitude < 0.0001f)
            return;
        bone.rotation = Quaternion.FromToRotation(tipDir, along) * bone.rotation;
    }

    static void RememberHandTip(Transform bone, ref float reach, ref Vector3 local)
    {
        reach = 0.06f;
        local = new Vector3(0f, 0.06f, 0f);
        if (bone == null)
            return;

        for (int i = 0; i < bone.childCount; i++)
        {
            Transform child = bone.GetChild(i);
            if (child.name.IndexOf("Tip", System.StringComparison.Ordinal) < 0)
                continue;
            local = child.localPosition;
            reach = Mathf.Max(0.02f, local.magnitude);
            return;
        }
    }

    static Transform GunOn(Transform hand)
    {
        if (hand == null)
            return null;
        Transform gun = GunChild<Hd2Shotgun>(hand);
        if (gun != null)
            return gun;
        gun = GunChild<Hd2Smg>(hand);
        if (gun != null)
            return gun;
        gun = GunChild<Hd2RocketLauncher>(hand);
        if (gun != null)
            return gun;
        gun = GunChild<Hd2Shocks>(hand);
        if (gun != null)
            return gun;
        gun = GunChild<Hd2LaserPistol>(hand);
        if (gun != null)
            return gun;
        gun = GunChild<Hd2BurstPistol>(hand);
        if (gun != null)
            return gun;
        return GunChild<Hd2Pistol>(hand);
    }

    static Transform GunChild<T>(Transform hand) where T : Component
    {
        T gun = hand.GetComponentInChildren<T>();
        return gun != null ? gun.transform : null;
    }

    bool SeesEnemy(out Transform enemy)
    {
        return LookForEnemy(out enemy, domination ? 28f : SeeRange, domination ? 70f : SeeHalfAngle);
    }

    bool LookForEnemy(out Transform enemy, float range, float halfAngle)
    {
        enemy = null;
        if (eye == null)
            return false;

        Hd2VisionCone.CopyFighters(seen);
        float best = range;
        float minDot = Mathf.Cos(halfAngle * Mathf.Deg2Rad);
        bool skipFriends = domination || payloadRole != Hd2PayloadRole.None;
        for (int i = 0; i < seen.Count; i++)
        {
            Transform other = seen[i];
            if (other == null || other == transform)
                continue;
            var life = other.GetComponent<Hd2Health>();
            if (life != null && life.IsDead)
                continue;
            if (skipFriends && Friendly(other))
                continue;
            Vector3 to = other.position + Vector3.up * 1.1f - eye.position;
            float distance = to.magnitude;
            if (distance > best || distance < 0.4f)
                continue;
            Vector3 dir = to / distance;
            if (Vector3.Dot(transform.forward, dir) < minDot)
                continue;
            if (Blocked(eye.position, dir, distance, other))
                continue;
            best = distance;
            enemy = other;
        }

        return enemy != null;
    }

    void GrabNearbyWeapon()
    {
        EnsureWeaponList();
        Transform hand = rightHand != null ? rightHand : leftHand;
        if (hand == null || cachedWeapons == null)
            return;
        if (homeSpawn != null && Horizontal(homeSpawn.position) < 4f)
            return;

        const float reach = 3f;
        for (int i = 0; i < cachedWeapons.Length; i++)
        {
            Component pickup = cachedWeapons[i];
            if (pickup == null)
                continue;
            Transform sphere = pickup.transform.Find("Sphere");
            if (sphere != null && !sphere.gameObject.activeInHierarchy)
                continue;
            if (Horizontal(sphere != null ? sphere.position : pickup.transform.position) > reach)
                continue;
            if (TakeWeapon(pickup, hand))
                return;
        }
    }

    static void EnsureWeaponList()
    {
        if (weaponListReady)
            return;

        var found = new List<Component>(32);
        AddWeapons<Hd2SmgPickup>(found);
        AddWeapons<Hd2RocketPickup>(found);
        AddWeapons<Hd2ShocksPickup>(found);
        AddWeapons<Hd2LaserPickup>(found);
        AddWeapons<Hd2ShotgunPickup>(found);
        AddWeapons<Hd2BurstPickup>(found);
        cachedWeapons = found.ToArray();
        weaponListReady = true;
    }

    static void AddWeapons<T>(List<Component> found) where T : Component
    {
        T[] all = FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null)
                found.Add(all[i]);
        }
    }

    void TrackEnemies()
    {
        if (BestEnemy(out Transform seenEnemy))
        {
            lostTargetAt = -1f;
            Engage(seenEnemy);
            return;
        }

        if (mode != Mode.Combat)
            return;
        if (lostTargetAt < 0f)
            lostTargetAt = Time.time;
        if (Time.time - lostTargetAt > 0.4f)
            EnterWander();
    }

    /// <summary>
    /// Picks a living enemy. People on the button this bot is fighting stay in the list
    /// even when they stand outside the look cone, and fire spreads across that group
    /// instead of every bot locking the nearest body.
    /// </summary>
    bool BestEnemy(out Transform enemy)
    {
        enemy = null;
        GatherFightChoices();
        if (fightCount == 0)
            return false;

        for (int i = 0; i < fightCount; i++)
            fightShooters[i] = Shooters(fightChoices[i]);

        int openIndex = 0;
        for (int i = 1; i < fightCount; i++)
        {
            if (BetterChoice(i, openIndex))
                openIndex = i;
        }

        Transform open = fightChoices[openIndex];
        int targetIndex = target != null ? ChoiceIndex(target) : -1;
        if (targetIndex >= 0)
        {
            int onTarget = fightShooters[targetIndex];
            int onOpen = fightShooters[openIndex];
            bool crowded = open != target && onTarget > onOpen + 1;
            bool leaveForPoint = open != target && fightOnPoint[openIndex] && !fightOnPoint[targetIndex] && onOpen <= onTarget;
            if ((!crowded && !leaveForPoint) || !ShouldPeel(target))
            {
                enemy = target;
                return true;
            }
        }

        enemy = open;
        return enemy != null;
    }

    void GatherFightChoices()
    {
        fightCount = 0;
        if (eye == null)
            return;

        float cone = Mathf.Cos(70f * Mathf.Deg2Rad);
        int count = Hd2VisionCone.FighterCount;
        for (int i = 0; i < count; i++)
        {
            Transform other = Hd2VisionCone.FighterAt(i);
            if (other == null || other == transform || Friendly(other))
                continue;
            var life = other.GetComponent<Hd2Health>();
            if (life != null && life.IsDead)
                continue;

            Vector3 to = other.position + Vector3.up * 1.1f - eye.position;
            float distance = to.magnitude;
            if (distance < 0.4f || distance > 32f)
                continue;

            bool onPoint = OnFoughtPoint(other);
            if (!onPoint && Vector3.Dot(transform.forward, to / distance) < cone)
                continue;
            if (Blocked(eye.position, to / distance, distance, other))
                continue;
            if (fightCount >= fightChoices.Length)
                break;

            fightChoices[fightCount] = other;
            fightDistance[fightCount] = distance;
            fightOnPoint[fightCount] = onPoint;
            fightCount++;
        }
    }

    bool BetterChoice(int candidate, int best)
    {
        if (fightShooters[candidate] != fightShooters[best])
            return fightShooters[candidate] < fightShooters[best];
        if (fightOnPoint[candidate] != fightOnPoint[best])
            return fightOnPoint[candidate];
        return fightDistance[candidate] < fightDistance[best];
    }

    int ChoiceIndex(Transform body)
    {
        for (int i = 0; i < fightCount; i++)
        {
            if (fightChoices[i] == body)
                return i;
        }

        return -1;
    }

    bool OnFoughtPoint(Transform other)
    {
        var points = Hd2TeamButton.All;
        float ox = other.position.x;
        float oz = other.position.z;
        for (int i = 0; i < points.Count; i++)
        {
            Hd2TeamButton point = points[i];
            if (point == null)
                continue;
            Vector3 at = point.transform.position;
            float dx = ox - at.x;
            float dz = oz - at.z;
            if (dx * dx + dz * dz > 56.25f)
                continue;

            float mine = Horizontal(at);
            if (point == objective && mine <= 26f)
                return true;
            if (mine <= 12f)
                return true;
        }

        return false;
    }

    int Shooters(Transform victim)
    {
        if (victim == null)
            return 0;

        int count = 0;
        int fighters = Hd2VisionCone.FighterCount;
        for (int i = 0; i < fighters; i++)
        {
            Transform body = Hd2VisionCone.FighterAt(i);
            if (body == null || body == transform)
                continue;
            Hd2EnemyBot bot = body.GetComponent<Hd2EnemyBot>();
            if (bot == null || bot.team != team || bot.CombatTarget != victim)
                continue;
            count++;
        }

        return count;
    }

    bool ShouldPeel(Transform victim)
    {
        int mine = spawnSlot;
        int id = GetInstanceID();
        int fighters = Hd2VisionCone.FighterCount;
        for (int i = 0; i < fighters; i++)
        {
            Transform body = Hd2VisionCone.FighterAt(i);
            if (body == null || body == transform)
                continue;
            Hd2EnemyBot bot = body.GetComponent<Hd2EnemyBot>();
            if (bot == null || bot.team != team || bot.CombatTarget != victim)
                continue;
            if (bot.spawnSlot > mine || (bot.spawnSlot == mine && bot.GetInstanceID() > id))
                return false;
        }

        return true;
    }

    bool Blocked(Vector3 origin, Vector3 dir, float distance, Transform root)
    {
        int count = Physics.RaycastNonAlloc(origin, dir, sightHits, distance, ~0, QueryTriggerInteraction.Ignore);
        float nearest = distance;
        bool wall = false;
        for (int i = 0; i < count; i++)
        {
            if (sightHits[i].collider.GetComponentInParent<Hd2TeamButton>() != null)
                continue;
            Transform hit = sightHits[i].collider.transform;
            if (hit == transform || hit.IsChildOf(transform))
                continue;
            if (root != null && (hit == root || hit.IsChildOf(root)))
                continue;
            if (FriendlyBody(hit))
                continue;
            if (sightHits[i].distance < nearest)
            {
                nearest = sightHits[i].distance;
                wall = true;
            }
        }

        return wall;
    }

    bool FriendlyBody(Transform hit)
    {
        if (team == Hd2Team.None)
            return false;

        Hd2EnemyBot bot = hit.GetComponentInParent<Hd2EnemyBot>();
        if (bot != null)
            return bot.team == team;

        Hd2Locomotion player = hit.GetComponentInParent<Hd2Locomotion>();
        return player != null && player.team == team;
    }

    Vector3 WanderDirection()
    {
        Vector3 dir = Random.insideUnitSphere;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f)
            dir = transform.forward;
        dir.Normalize();
        if (hasCenter)
        {
            Vector3 home = Flat(mapCenter - transform.position);
            if (home.sqrMagnitude > 0.01f)
                dir = (dir + home.normalized * 0.35f).normalized;
        }

        if (WallAhead(2f, out Vector3 away))
            dir = (dir + away).normalized;
        return ClearOfWater(dir);
    }

    void Turn()
    {
        int sign = Random.value < 0.5f ? -1 : 1;
        if (WallAlong(transform.forward, 2f, out Vector3 away))
        {
            float side = Vector3.Dot(transform.right, away);
            if (Mathf.Abs(side) > 0.2f)
                sign = side > 0f ? 1 : -1;
        }

        Face(Quaternion.Euler(0f, 45f * sign, 0f) * transform.forward);
    }

    bool TryStartRail()
    {
        if (!ClosestRail(out Hd2GrindRail rail, out float along, out float sign))
            return false;
        Vector3 landing = rail.PointAlong(along);
        if (Horizontal(landing) > BlitzRange)
            return false;
        if (!TryBlitz(landing, rail, sign))
            return false;
        grindRail = rail;
        grindDistance = along;
        grindSign = sign;
        return true;
    }

    bool ClosestRail(out Hd2GrindRail rail, out float along, out float sign)
    {
        rail = null;
        along = 0f;
        sign = 1f;
        float best = BlitzRange;
        var rails = FindObjectsByType<Hd2GrindRail>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < rails.Length; i++)
        {
            if (rails[i] == null || rails[i].length < 1f)
                continue;
            for (float d = 0f; d <= rails[i].length; d += 1.5f)
            {
                float gap = Horizontal(rails[i].PointAlong(d));
                if (gap >= best)
                    continue;
                best = gap;
                rail = rails[i];
                along = d;
            }
        }

        if (rail == null)
            return false;
        float remain = rail.length - along;
        sign = remain >= along ? 1f : -1f;
        return true;
    }

    Vector3 BlitzLanding(Vector3 direction, bool preferUp)
    {
        if (direction.sqrMagnitude < 0.0001f)
            direction = transform.forward;
        direction.Normalize();
        Vector3 best = transform.position + direction * 6f;
        float bestScore = float.NegativeInfinity;
        bool found = false;
        float[] distances = { 6f, 9f, 13f };
        for (int i = 0; i < distances.Length; i++)
        {
            Vector3 probe = transform.position + direction * distances[i];
            if (!FloorBelow(probe, out Vector3 floor))
                continue;
            if (OutsidePlay(floor) || Horizontal(floor) > BlitzRange)
                continue;
            float rise = floor.y - transform.position.y;
            float score = distances[i] + (preferUp ? rise * 0.45f : 0f);
            if (score <= bestScore)
                continue;
            bestScore = score;
            best = floor;
            found = true;
        }

        return found ? best : transform.position;
    }

    bool TryBlitzBehind()
    {
        if (target == null)
            return false;
        Vector3 back = Flat(transform.position - target.position);
        if (back.sqrMagnitude < 0.01f)
            back = -transform.forward;
        back.Normalize();
        Vector3 side = Vector3.Cross(Vector3.up, back) * (Random.value < 0.5f ? -3.5f : 3.5f);
        Vector3 probe = target.position + back * 4f + side;
        Vector3 offset = probe - transform.position;
        offset.y = 0f;
        if (offset.magnitude > BlitzRange)
            probe = transform.position + offset.normalized * (BlitzRange - 0.5f);
        if (!FloorBelow(probe, out Vector3 floor))
            return false;
        return TryBlitz(floor, null, 1f);
    }

    bool TryBlitz(Vector3 landing, Hd2GrindRail rail, float sign)
    {
        Vector3 flat = landing - transform.position;
        flat.y = 0f;
        if (flat.magnitude < 0.8f || flat.magnitude > BlitzRange || StandingOnWater(landing) || OutsidePlay(landing))
            return false;
        if (Hd2Payload.BlocksRoute(transform.position, landing))
            return false;
        if (meter != null && !meter.TrySpend())
            return false;

        BeginTravel(landing, rail, sign);
        return true;
    }

    void BeginTravel(Vector3 landing, Hd2GrindRail rail, float sign)
    {
        grinding = false;
        grindRail = null;
        traveling = true;
        smoothedMove = Vector3.zero;
        PlayBlitzTrail();
        if (blitzSource != null && blitzSource.clip != null)
            blitzSource.PlayOneShot(blitzSource.clip);
        travelStart = transform.position;
        travelEnd = landing;
        travelElapsed = 0f;
        travelRail = rail;
        travelSign = sign;
        if (rail != null)
        {
            grindRail = rail;
            grindSign = sign;
            grindDistance = rail.DistanceAlong(landing);
        }
    }

    const float StuckSeconds = 8f;

    void EscapeIfStuck()
    {
        const float still = 0.4f;
        if (traveling || grinding)
        {
            stuckAnchor = transform.position;
            stuckSince = Time.time;
            return;
        }

        Vector3 flat = transform.position - stuckAnchor;
        flat.y = 0f;
        if (flat.sqrMagnitude > still * still)
        {
            stuckAnchor = transform.position;
            stuckSince = Time.time;
            return;
        }

        if (Time.time - stuckSince < StuckSeconds)
            return;

        stuckSince = Time.time;
        BlitzRandom();
    }

    void BlitzRandom()
    {
        for (int attempt = 0; attempt < 6; attempt++)
        {
            Vector3 dir = Random.insideUnitSphere;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f)
                continue;
            dir.Normalize();
            Vector3 landing = BlitzLanding(dir, false);
            Vector3 flat = landing - transform.position;
            flat.y = 0f;
            if (flat.magnitude < 0.8f)
            {
                Vector3 probe = transform.position + dir * 6f;
                if (!FloorBelow(probe, out landing))
                    landing = probe;
            }

            flat = landing - transform.position;
            flat.y = 0f;
            if (flat.magnitude < 0.8f || flat.magnitude > BlitzRange || StandingOnWater(landing) || OutsidePlay(landing))
                continue;

            if (meter != null && meter.Charges < 1f)
            {
                BeginTravel(landing, null, 1f);
                moveDir = flat.normalized;
                return;
            }

            if (TryBlitz(landing, null, 1f))
            {
                moveDir = flat.normalized;
                return;
            }
        }
    }

    void TickTravel()
    {
        if (traveling)
        {
            float distance = Vector3.Distance(travelStart, travelEnd);
            float duration = distance / BlitzSpeed;
            travelElapsed += Time.deltaTime;
            float t = duration <= 0.01f ? 1f : Mathf.Clamp01(travelElapsed / duration);
            float hop = 4f * BlitzHop * t * (1f - t);
            Vector3 next = Vector3.Lerp(travelStart, travelEnd, t) + Vector3.up * hop;
            if (Hd2SpawnField.Blocks(this, transform.position + Vector3.up * 0.9f, next + Vector3.up * 0.9f))
            {
                traveling = false;
                StopBlitzTrail();
                if (body != null)
                    body.enabled = true;
                return;
            }

            if (body != null)
                body.enabled = false;
            transform.position = next;
            if (t < 1f)
                return;

            transform.position = travelEnd;
            traveling = false;
            StopBlitzTrail();
            if (HitsShield(null))
                PassingFront = true;
            if (body != null)
                body.enabled = true;
            if (travelRail != null)
            {
                grinding = true;
                grindRail = travelRail;
                grindDistance = travelRail.DistanceAlong(travelEnd);
                grindSign = travelSign;
            }

            travelRail = null;
            if (mode == Mode.Combat && target != null)
                Face(Flat(target.position - transform.position));
            return;
        }

        if (!grinding || grindRail == null)
            return;

        Hd2Payload cart = Hd2Payload.Current;
        if (cart != null && cart.rail == grindRail && grindSign >= 0f && CatchCart(cart))
            return;

        grindDistance += grindSign * GrindSpeed * Time.deltaTime;
        bool ended = grindSign > 0f ? grindDistance >= grindRail.length - 0.3f : grindDistance <= 0.3f;
        grindDistance = Mathf.Clamp(grindDistance, 0f, grindRail.length);
        if (body != null)
            body.enabled = false;
        transform.position = grindRail.PointAlong(grindDistance);
        Vector3 ahead = grindRail.PointAlong(Mathf.Clamp(grindDistance + grindSign, 0f, grindRail.length));
        if (payloadRole == Hd2PayloadRole.None)
            Face(Flat(ahead - transform.position));
        if (!ended)
            return;

        grinding = false;
        Hd2GrindRail finished = grindRail;
        float sign = grindSign;
        grindRail = null;
        if (body != null)
            body.enabled = true;
        if (payloadRole != Hd2PayloadRole.None)
        {
            payloadRailRide = false;
            return;
        }
        Vector3 exit = Flat(finished.PointAlong(Mathf.Clamp(grindDistance + sign * 2f, 0f, finished.length)) - transform.position);
        if (exit.sqrMagnitude < 0.01f)
            exit = transform.forward;
        Vector3 probe = transform.position + exit.normalized * 8f;
        if (!FloorBelow(probe, out Vector3 floor) || !TryBlitz(floor, null, 1f))
            wander = WanderKind.Walk;
    }

    void TickWalk()
    {
        if (body == null || !body.enabled)
            return;

        float speed = WalkSpeed;
        if (Hd2Shootout.Present && mode == Mode.Combat)
            speed = ObjectiveSpeed;
        else if ((domination || payloadRole != Hd2PayloadRole.None) && mode == Mode.Wander)
            speed = retreating ? SidestepSpeed : ObjectiveSpeed;
        else if (mode == Mode.Combat && combat == CombatKind.Sprint)
            speed = SidestepSpeed;
        else if (mode == Mode.Combat && combat == CombatKind.Strafe)
            speed = WalkSpeed * 0.85f;
        if (moveDir.sqrMagnitude > 0.0001f && WaterAlong(moveDir, 0.9f))
            moveDir = Vector3.zero;
        moveDir = StayOnMap(moveDir);
        moveDir = Hd2Payload.SteerBot(transform.position, moveDir);
        Vector3 wish = moveDir;
        if (wish.sqrMagnitude > 1f)
            wish.Normalize();
        float blend = 1f - Mathf.Exp(-6f * Time.deltaTime);
        smoothedMove = Vector3.Lerp(smoothedMove, wish, blend);
        if (wish.sqrMagnitude < 0.0001f && smoothedMove.sqrMagnitude < 0.04f)
            smoothedMove = Vector3.zero;
        moveDir = smoothedMove;
        Vector3 step = moveDir * speed;
        if (fallTime >= 5f && health != null)
        {
            health.MarkHit(transform.position + Vector3.up * 1.1f);
            health.ApplyHit(health.Health, false);
            fallTime = 0f;
            return;
        }

        ClearSmallLedge();
        if (!jumpBoosting)
            ConsiderHop();

        float dt = Time.deltaTime;
        float burn = BurnJump(dt);
        float coast = dt - burn;
        if (burn > 0f)
            verticalVelocity += JumpGravity * JumpAccelGs * burn;
        if (body.isGrounded && verticalVelocity <= 0f && burn <= 0f)
        {
            verticalVelocity = -2f;
            fallTime = 0f;
        }
        else if (coast > 0f)
        {
            verticalVelocity -= JumpGravity * coast;
            verticalVelocity = Mathf.Max(verticalVelocity, -25f);
            if (!body.isGrounded)
                fallTime += dt;
        }
        else if (!body.isGrounded)
            fallTime += dt;
        Vector3 before = transform.position;
        body.Move((step + Vector3.up * verticalVelocity) * Time.deltaTime);
        if ((transform.position - before).sqrMagnitude > 0.0004f)
        {
            lastMovedAt = Time.time;
        }
        else if (Time.time - lastMovedAt > 1.5f && moveDir.sqrMagnitude > 0.01f)
        {
            moveDir = WanderDirection();
            lastMovedAt = Time.time;
        }
    }

    void ClearSmallLedge()
    {
        if (body == null || !body.isGrounded || moveDir.sqrMagnitude < 0.01f)
            return;

        Vector3 dir = moveDir;
        Vector3 origin = transform.position + Vector3.up * 0.03f;
        int count = Physics.RaycastNonAlloc(origin, dir, stepHits, 0.4f, ~0, QueryTriggerInteraction.Ignore);
        float nearest = float.MaxValue;
        bool blocked = false;
        for (int i = 0; i < count; i++)
        {
            Collider collider = stepHits[i].collider;
            if (collider == null || collider.transform.IsChildOf(transform))
                continue;
            if (stepHits[i].distance >= nearest)
                continue;
            nearest = stepHits[i].distance;
            blocked = true;
        }

        if (!blocked)
            return;

        Vector3 over = origin + dir * (nearest + 0.12f) + Vector3.up * 0.55f;
        count = Physics.RaycastNonAlloc(over, Vector3.down, stepHits, 0.7f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        float floorY = transform.position.y;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            Collider collider = stepHits[i].collider;
            if (collider == null || collider.transform.IsChildOf(transform))
                continue;
            if (stepHits[i].distance >= best)
                continue;
            best = stepHits[i].distance;
            floorY = stepHits[i].point.y;
            found = true;
        }

        if (!found)
            return;

        float rise = floorY - transform.position.y;
        if (rise < 0.004f || rise > 0.45f)
            return;

        body.enabled = false;
        transform.position += Vector3.up * (rise + 0.02f);
        body.enabled = true;
    }

    void ConsiderHop()
    {
        if (traveling || grinding || body == null || !body.enabled || !body.isGrounded)
            return;
        if (moveDir.sqrMagnitude < 0.01f || Time.time < nextHopAt)
            return;

        bool ledge = RouteAhead(out float rise, out float span);
        bool wanderHop = !ledge && Random.value < 0.012f;
        if (!ledge && !wanderHop)
            return;

        bool blitz = ledge && meter != null && meter.Charges >= 1f && (rise > 0.4f || span > 1.3f);
        if (blitz)
        {
            Vector3 landing = transform.position + moveDir * Mathf.Clamp(span + 1.4f, 2f, 8f);
            if (FloorBelow(landing, out Vector3 floor) && TryBlitz(floor, null, 1f))
            {
                nextHopAt = Time.time + 2.2f;
                return;
            }
        }

        if (meter == null || meter.Charges <= 0.001f)
            return;

        nextHopAt = Time.time + (ledge ? 2.2f : Random.Range(6f, 12f));
        jumpBoosting = true;
        jumpTime = 0f;
    }

    float BurnJump(float dt)
    {
        if (!jumpBoosting)
            return 0f;
        if (jumpTime >= JumpMaxTime || meter == null || meter.Charges <= 0.001f)
        {
            jumpBoosting = false;
            return 0f;
        }

        float timeLeft = Mathf.Max(0f, JumpMaxTime) - jumpTime;
        float burn = Mathf.Min(dt, timeLeft);
        float rate = Mathf.Max(0.01f, JumpThrustPerSecond);
        float spent = meter.SpendUpTo(burn * rate);
        burn = spent / rate;
        if (burn <= 0.0001f)
        {
            jumpBoosting = false;
            return 0f;
        }

        jumpTime += burn;
        if (jumpTime >= JumpMaxTime - 0.0001f || meter.Charges <= 0.001f)
            jumpBoosting = false;
        return burn;
    }

    public bool IsSprinting
    {
        get
        {
            if (moveDir.sqrMagnitude < 0.01f || traveling || grinding)
                return false;
            if (mode == Mode.Combat && combat == CombatKind.Sprint)
                return true;
            return domination && mode == Mode.Wander && !retreating;
        }
    }

    bool RouteAhead(out float rise, out float span)
    {
        rise = 0f;
        span = 0f;
        Vector3 dir = moveDir;
        Vector3 feet = transform.position + Vector3.up * 0.08f;
        int count = Physics.RaycastNonAlloc(feet, dir, stepHits, 1.3f, ~0, QueryTriggerInteraction.Ignore);
        float nearest = float.MaxValue;
        Vector3 hitPoint = feet + dir;
        bool wall = false;
        for (int i = 0; i < count; i++)
        {
            Collider collider = stepHits[i].collider;
            if (collider == null || collider.transform.IsChildOf(transform))
                continue;
            if (stepHits[i].distance >= nearest)
                continue;
            nearest = stepHits[i].distance;
            hitPoint = stepHits[i].point;
            wall = true;
        }

        if (wall)
        {
            Vector3 over = hitPoint + dir * 0.2f + Vector3.up * 1.1f;
            count = Physics.RaycastNonAlloc(over, Vector3.down, stepHits, 1.6f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            bool found = false;
            float topY = transform.position.y;
            for (int i = 0; i < count; i++)
            {
                Collider collider = stepHits[i].collider;
                if (collider == null || collider.transform.IsChildOf(transform))
                    continue;
                if (stepHits[i].distance >= best)
                    continue;
                best = stepHits[i].distance;
                topY = stepHits[i].point.y;
                found = true;
            }

            rise = found ? topY - transform.position.y : 0.8f;
            span = nearest;
            return rise > 0.12f && rise < 1.6f;
        }

        Vector3 probe = transform.position + dir * 1.2f + Vector3.up * 0.3f;
        count = Physics.RaycastNonAlloc(probe, Vector3.down, stepHits, 2.4f, ~0, QueryTriggerInteraction.Ignore);
        float ground = float.MaxValue;
        bool floor = false;
        float groundY = transform.position.y;
        for (int i = 0; i < count; i++)
        {
            Collider collider = stepHits[i].collider;
            if (collider == null || collider.transform.IsChildOf(transform))
                continue;
            if (stepHits[i].distance >= ground)
                continue;
            ground = stepHits[i].distance;
            groundY = stepHits[i].point.y;
            floor = true;
        }

        if (floor && groundY >= transform.position.y - 0.3f)
            return false;

        rise = 0.35f;
        span = 1.6f;
        return true;
    }

    static int Probe(Vector3 origin, Vector3 direction, float distance)
    {
        return Physics.RaycastNonAlloc(origin, direction, probeHits, distance, ~0, QueryTriggerInteraction.Ignore);
    }

    bool FloorBelow(Vector3 around, out Vector3 point)
    {
        point = around;
        int count = Probe(around + Vector3.up * 4f, Vector3.down, 12f);
        float best = float.MaxValue;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            if (probeHits[i].collider.transform.root == transform)
                continue;
            if (probeHits[i].collider.GetComponentInParent<Hd2Water>() != null)
                continue;
            if (probeHits[i].normal.y < 0.55f || probeHits[i].distance >= best)
                continue;
            best = probeHits[i].distance;
            point = probeHits[i].point;
            found = true;
        }

        return found;
    }

    Vector3 ClearOfWater(Vector3 dir)
    {
        if (dir.sqrMagnitude < 0.0001f || !WaterAlong(dir, 2.4f))
            return dir;

        Vector3 right = Vector3.Cross(Vector3.up, dir.normalized);
        if (!WaterAlong(right, 2.4f))
            return right;
        if (!WaterAlong(-right, 2.4f))
            return -right;
        if (!WaterAlong(-dir, 2.4f))
            return -dir.normalized;
        return Vector3.zero;
    }

    bool WaterAlong(Vector3 dir, float distance)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f)
            return false;
        dir.Normalize();
        Vector3 side = Vector3.Cross(Vector3.up, dir) * 0.45f;
        Vector3 ahead = transform.position + dir * distance;
        return DropsToWater(ahead) || DropsToWater(ahead + side) || DropsToWater(ahead - side);
    }

    bool DropsToWater(Vector3 feet)
    {
        Vector3 origin = feet + Vector3.up * 2.5f;
        int count = Probe(origin, Vector3.down, 16f);
        float nearest = float.MaxValue;
        bool water = false;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            if (probeHits[i].collider == null || probeHits[i].collider.transform.root == transform)
                continue;
            if (probeHits[i].normal.y < 0.45f || probeHits[i].distance >= nearest)
                continue;
            nearest = probeHits[i].distance;
            found = true;
            water = probeHits[i].collider.GetComponentInParent<Hd2Water>() != null;
        }

        return !found || water;
    }

    bool OutsidePlay(Vector3 point)
    {
        if (!hasCenter || playRadius < 4f)
            return false;
        return Flat(point - mapCenter).magnitude > playRadius + 1.5f;
    }

    Vector3 StayOnMap(Vector3 dir)
    {
        dir = ClearOfWater(dir);
        if (!hasCenter || playRadius < 4f || dir.sqrMagnitude < 0.0001f)
            return dir;

        Vector3 fromCenter = Flat(transform.position - mapCenter);
        float dist = fromCenter.magnitude;
        if (dist <= playRadius + 1.5f)
            return dir;

        Vector3 outward = fromCenter / dist;
        if (Vector3.Dot(dir.normalized, outward) <= 0f)
            return dir;

        Vector3 tangent = Vector3.Cross(Vector3.up, outward);
        if (Vector3.Dot(tangent, dir) < 0f)
            tangent = -tangent;
        Vector3 back = (-outward + tangent * 0.45f).normalized;
        Vector3 turned = ClearOfWater(back);
        return turned.sqrMagnitude > 0.0001f ? turned : -outward;
    }

    bool StandingOnWater(Vector3 point)
    {
        Vector3 origin = point + Vector3.up * 0.35f;
        int count = Probe(origin, Vector3.down, 0.8f);
        float nearest = float.MaxValue;
        bool water = false;
        for (int i = 0; i < count; i++)
        {
            if (probeHits[i].collider == null || probeHits[i].collider.transform.root == transform)
                continue;
            if (probeHits[i].distance >= nearest)
                continue;
            nearest = probeHits[i].distance;
            water = probeHits[i].collider.GetComponentInParent<Hd2Water>() != null;
        }

        return water;
    }

    bool WallAhead(float distance, out Vector3 away)
    {
        Vector3 dir = moveDir.sqrMagnitude > 0.01f ? moveDir : transform.forward;
        return WallAlong(dir, distance, out away);
    }

    bool WallAlong(Vector3 dir, float distance, out Vector3 away)
    {
        away = Vector3.zero;
        Vector3 origin = transform.position + Vector3.up * 0.9f;
        int count = Probe(origin, dir, distance);
        float best = distance;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            if (probeHits[i].collider.transform.root == transform)
                continue;
            if (probeHits[i].normal.y > 0.45f || probeHits[i].distance >= best)
                continue;
            best = probeHits[i].distance;
            away = probeHits[i].normal;
            away.y = 0f;
            found = away.sqrMagnitude > 0.0001f;
        }

        if (found)
            away.Normalize();
        return found;
    }

    bool FindWeapon(float range, out Component found)
    {
        found = null;
        if (PistolHand() == null)
            return false;
        float best = range;
        Scan<Hd2SmgPickup>(ref found, ref best);
        Scan<Hd2RocketPickup>(ref found, ref best);
        Scan<Hd2ShocksPickup>(ref found, ref best);
        Scan<Hd2LaserPickup>(ref found, ref best);
        Scan<Hd2ShotgunPickup>(ref found, ref best);
        Scan<Hd2BurstPickup>(ref found, ref best);
        return found != null;
    }

    void Scan<T>(ref Component found, ref float best) where T : Component
    {
        T[] all = FindObjectsByType<T>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] == null)
                continue;
            Transform sphere = all[i].transform.Find("Sphere");
            if (sphere != null && !sphere.gameObject.activeInHierarchy)
                continue;
            float gap = Horizontal(PickupPoint(all[i]));
            if (gap >= best)
                continue;
            best = gap;
            found = all[i];
        }
    }

    bool TakeWeapon(Component source, Transform hand)
    {
        bool took = false;
        if (source is Hd2SmgPickup smg)
            took = smg.BotTake(hand);
        else if (source is Hd2RocketPickup rocket)
            took = rocket.BotTake(hand);
        else if (source is Hd2ShocksPickup shocks)
            took = shocks.BotTake(hand);
        else if (source is Hd2LaserPickup laser)
            took = laser.BotTake(hand);
        else if (source is Hd2ShotgunPickup shotgun)
            took = shotgun.BotTake(hand);
        else if (source is Hd2BurstPickup burst)
            took = burst.BotTake(hand);
        if (took)
            SeatOneHand(hand);
        return took;
    }

    Transform PistolHand()
    {
        if (rightHand != null && rightHand.GetComponentInChildren<Hd2Pistol>(true) != null)
            return rightHand;
        if (leftHand != null && leftHand.GetComponentInChildren<Hd2Pistol>(true) != null)
            return leftHand;
        return null;
    }

    static Vector3 PickupPoint(Component pickup)
    {
        return pickup.transform.position + Vector3.up * 1.05f;
    }

    void CacheCenter()
    {
        var points = FindObjectsByType<Hd2SpawnPoint>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        if (points == null || points.Length == 0)
            return;
        Vector3 sum = Vector3.zero;
        int count = 0;
        for (int i = 0; i < points.Length; i++)
        {
            if (points[i] == null)
                continue;
            sum += points[i].transform.position;
            count++;
        }

        if (count == 0)
            return;
        mapCenter = sum / count;
        hasCenter = true;
        playRadius = 0f;
        for (int i = 0; i < points.Length; i++)
        {
            if (points[i] == null)
                continue;
            playRadius = Mathf.Max(playRadius, Flat(points[i].transform.position - mapCenter).magnitude);
        }

        var buttons = FindObjectsByType<Hd2TeamButton>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] == null)
                continue;
            playRadius = Mathf.Max(playRadius, Flat(buttons[i].transform.position - mapCenter).magnitude);
        }

        var rails = FindObjectsByType<Hd2GrindRail>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < rails.Length; i++)
        {
            if (rails[i] == null)
                continue;
            for (float distance = 0f; distance <= rails[i].length; distance += 4f)
                playRadius = Mathf.Max(playRadius, Flat(rails[i].PointAlong(distance) - mapCenter).magnitude);
        }
    }

    void Face(Vector3 flat)
    {
        flat.y = 0f;
        if (flat.sqrMagnitude < 0.0001f)
            return;

        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;

        float delta = Vector3.SignedAngle(forward.normalized, flat.normalized, Vector3.up);
        float maxStep = TurnBudget();
        if (Mathf.Abs(delta) > 135f && turnSign != 0 && Mathf.Sign(delta) != turnSign)
            delta = -delta;

        float step = Mathf.Clamp(delta, -maxStep, maxStep);
        if (Mathf.Abs(step) < 0.05f)
        {
            turnSign = 0;
            return;
        }

        turnSign = step > 0f ? 1 : -1;
        transform.Rotate(0f, step, 0f, Space.World);
    }

    float Horizontal(Vector3 world)
    {
        Vector3 gap = world - transform.position;
        gap.y = 0f;
        return gap.magnitude;
    }

    static Vector3 Flat(Vector3 value)
    {
        value.y = 0f;
        return value.sqrMagnitude > 0.0001f ? value.normalized : Vector3.zero;
    }

    void StandBody(Transform robot)
    {
        Transform head = FindNamed(robot, "Head");
        Transform hips = FindNamed(robot, "Hips");
        if (head == null || hips == null)
            return;
        if (head.position.y > hips.position.y + 0.25f)
            return;

        robot.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        if (head.position.y > hips.position.y + 0.25f)
            return;
        robot.localRotation = Quaternion.Euler(90f, 0f, 0f);
    }

    void FitHeight(Transform robot)
    {
        Transform head = FindNamed(robot, "Head");
        if (head == null)
            return;
        float height = head.position.y - transform.position.y;
        if (height < 0.2f)
            return;

        // Halfway between the player's 1.7 m and 1.9 m standing limits.
        const float target = 1.8f;
        robot.localScale = Vector3.one * (target / height);
        if (eye != null)
            eye.localPosition = new Vector3(0f, target - 0.08f, 0.1f);
    }

    void IgnoreOwnColliders(GameObject robot)
    {
        if (body == null)
            return;
        Collider[] cols = robot.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] != null && cols[i] != body)
                Physics.IgnoreCollision(body, cols[i], true);
        }
    }

    void BindLimbs(Transform robot)
    {
        upperL = FindBone(robot, "Arm.L", "UpperArm_L");
        foreL = FindBone(robot, "Arm.L.002", "Forearm_L");
        handL = FindBone(robot, "Hand.L", "Hand_L");
        upperR = FindBone(robot, "Arm.R", "UpperArm_R");
        foreR = FindBone(robot, "Arm.R.002", "Forearm_R");
        handR = FindBone(robot, "Hand.R", "Hand_R");
        RememberHandTip(handL, ref tipReachL, ref tipLocalL);
        RememberHandTip(handR, ref tipReachR, ref tipLocalR);
        thighL = FindBone(robot, "Thigh.L", "Thigh_L");
        shinL = FindBone(robot, "Shin.L", "Shin_L");
        footL = FindBone(robot, "Foot.L", "Foot_L");
        toeL = footL != null ? FindNamed(footL, "Foot.L.Tip") : null;
        if (toeL == null && footL != null)
            toeL = FindNamed(footL, "Foot_L.Tip");
        thighR = FindBone(robot, "Thigh.R", "Thigh_R");
        shinR = FindBone(robot, "Shin.R", "Shin_R");
        footR = FindBone(robot, "Foot.R", "Foot_R");
        toeR = footR != null ? FindNamed(footR, "Foot.R.Tip") : null;
        if (toeR == null && footR != null)
            toeR = FindNamed(footR, "Foot_R.Tip");
        restThighL = Local(thighL);
        restThighR = Local(thighR);
        restShinL = Local(shinL);
        restShinR = Local(shinR);
        restFootL = Local(footL);
        restFootR = Local(footR);
    }

    void SeatHands()
    {
        PlaceHolder(rightHand, handR);
        PlaceHolder(leftHand, handL);
        SeatOneHand(rightHand);
        SeatOneHand(leftHand);
    }

    void SeatOneHand(Transform holder)
    {
        if (holder == rightHand)
            AimGripAlongArm(holder, foreR, handR, -1f);
        else if (holder == leftHand)
            AimGripAlongArm(holder, foreL, handL, 1f);
    }

    static void PlaceHolder(Transform holder, Transform bone)
    {
        if (holder == null || bone == null)
            return;

        holder.SetParent(bone, false);
        holder.localPosition = Vector3.zero;
        holder.localRotation = Quaternion.identity;
    }

    void AimGripAlongArm(Transform holder, Transform fore, Transform bone, float side)
    {
        if (holder == null || fore == null || bone == null)
            return;

        Transform gun = GunOn(holder);
        if (gun == null && holder.childCount > 0)
            gun = holder.GetChild(0);
        if (gun == null)
            return;

        ShowGripHand(gun);

        Transform muzzle = FindNamed(gun, "Muzzle");
        Vector3 outDir = bone.position - fore.position;
        if (outDir.sqrMagnitude < 0.0001f)
            outDir = holder.forward;
        if (outDir.sqrMagnitude < 0.0001f)
            return;
        outDir.Normalize();

        if (muzzle != null)
        {
            Vector3 muzzleLocal = gun.InverseTransformDirection(muzzle.forward);
            if (muzzleLocal.sqrMagnitude > 0.0001f)
                gun.rotation = Quaternion.FromToRotation(muzzleLocal.normalized, outDir);

            Vector3 palm = Vector3.ProjectOnPlane(gun.forward, outDir);
            Vector3 wantPalm = Vector3.ProjectOnPlane(transform.up, outDir);
            if (palm.sqrMagnitude > 0.0001f && wantPalm.sqrMagnitude > 0.0001f)
                gun.rotation = Quaternion.FromToRotation(palm.normalized, wantPalm.normalized) * gun.rotation;
        }

        Transform wristMarker = FindNamed(gun, "Wrist");
        if (wristMarker != null)
            gun.position += bone.position - wristMarker.position;
        else
        {
            float scale = Mathf.Abs(gun.lossyScale.x);
            if (scale < 0.0001f)
                scale = 1f;
            Vector3 wristLocal = new Vector3(0.014f * side, 0.101f, 0.048f);
            gun.position = bone.position - gun.rotation * (wristLocal * scale);
        }

        // The robot mesh has no hand. The grip fist was left inside the sleeve
        // because the wrist marker is not the center of that mesh.
        CacheFist(holder, gun);
        ShowGripHand(gun);
        KeepFistOnArm(holder, fore, bone);
    }

    void CacheFist(Transform holder, Transform gun)
    {
        SkinnedMeshRenderer skin = null;
        SkinnedMeshRenderer[] skins = gun.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < skins.Length; i++)
        {
            if (skins[i] != null && skins[i].name == "HandSkin")
                skin = skins[i];
        }

        bool right = holder == rightHand;
        if (right)
        {
            fistSkinR = skin;
            fistReadyR = false;
        }
        else
        {
            fistSkinL = skin;
            fistReadyL = false;
        }

        RememberFist(holder, gun, skin);
    }

    void RememberFist(Transform holder, Transform gun, SkinnedMeshRenderer skin)
    {
        if (holder == null || gun == null || skin == null)
            return;

        Bounds box = skin.bounds;
        float size = box.size.magnitude;
        if (size < 0.02f || size > 0.5f)
            return;

        bool right = holder == rightHand;
        float reach = Mathf.Max(box.extents.x, Mathf.Max(box.extents.y, box.extents.z));
        if (right)
        {
            fistLocalR = gun.InverseTransformPoint(box.center);
            fistAlongR = 0.05f + reach;
            fistReadyR = true;
        }
        else
        {
            fistLocalL = gun.InverseTransformPoint(box.center);
            fistAlongL = 0.05f + reach;
            fistReadyL = true;
        }
    }

    void KeepFistOnArm(Transform holder, Transform fore, Transform bone)
    {
        if (holder == null || fore == null || bone == null)
            return;

        bool right = holder == rightHand;
        SkinnedMeshRenderer skin = right ? fistSkinR : fistSkinL;
        Transform gun = GunOn(holder);
        if (gun == null)
            return;
        if (right ? !fistReadyR : !fistReadyL)
            RememberFist(holder, gun, skin);
        if (right ? !fistReadyR : !fistReadyL)
            return;

        Vector3 outDir = bone.position - fore.position;
        if (outDir.sqrMagnitude < 0.0001f)
            return;
        outDir.Normalize();

        Vector3 center = gun.TransformPoint(right ? fistLocalR : fistLocalL);
        Vector3 delta = center - bone.position;
        float along = Vector3.Dot(delta, outDir);
        Vector3 lateral = delta - outDir * along;
        // The forearm mesh ends about 5 cm past the wrist bone. Keep the fist center just beyond that.
        float push = (right ? fistAlongR : fistAlongL) - along;
        if (push > 0.25f)
            push = 0.25f;
        if (push < -0.25f)
            push = -0.25f;
        if (lateral.sqrMagnitude < 0.0000001f && Mathf.Abs(push) < 0.0001f)
            return;

        gun.position += -lateral + outDir * push;
    }

    static void ShowGripHand(Transform gun)
    {
        Transform hand = FindNamed(gun, "Hand");
        if (hand != null)
            hand.gameObject.SetActive(true);
        SkinnedMeshRenderer[] skins = gun.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < skins.Length; i++)
        {
            if (skins[i] == null)
                continue;
            skins[i].enabled = true;
            skins[i].updateWhenOffscreen = true;
            skins[i].gameObject.SetActive(true);
        }
    }

    void PoseLegs()
    {
        if (thighL == null || thighR == null)
            return;

        if (traveling)
        {
            gaitWeight = 0f;
            SetLocal(thighL, restThighL);
            SetLocal(thighR, restThighR);
            SetLocal(shinL, restShinL);
            SetLocal(shinR, restShinR);
            SetLocal(footL, restFootL);
            SetLocal(footR, restFootR);
            return;
        }

        if (grinding && grindRail != null)
        {
            Vector3 along = grindRail.PlanarTangent(grindDistance) * grindSign;
            along.y = 0f;
            gaitWeight = 0f;
            if (along.sqrMagnitude > 0.0001f)
                PoseSkate(along.normalized);
            else
            {
                SetLocal(thighL, restThighL);
                SetLocal(thighR, restThighR);
                SetLocal(shinL, restShinL);
                SetLocal(shinR, restShinR);
                SetLocal(footL, restFootL);
                SetLocal(footR, restFootR);
            }
            return;
        }

        Vector3 velocity = Vector3.zero;
        if (traveling)
            velocity = travelEnd - travelStart;
        else if (body != null && body.enabled)
            velocity = body.velocity;
        velocity.y = 0f;
        float speed = velocity.magnitude;
        float target = speed > 0.25f ? 1f : 0f;
        gaitWeight = Mathf.MoveTowards(gaitWeight, target, Time.deltaTime * 6f);
        if (gaitWeight <= 0.001f)
        {
            SetLocal(thighL, restThighL);
            SetLocal(thighR, restThighR);
            SetLocal(shinL, restShinL);
            SetLocal(shinR, restShinR);
            SetLocal(footL, restFootL);
            SetLocal(footR, restFootR);
            return;
        }

        Vector3 stride = speed > 0.01f ? velocity / speed : transform.forward;
        float side = 0f;
        Vector3 right = transform.right;
        right.y = 0f;
        if (speed > 0.01f && right.sqrMagnitude > 0.0001f)
            side = Mathf.Clamp01(Mathf.Abs(Vector3.Dot(stride, right.normalized)));
        stride *= Mathf.Lerp(1f, 1.85f, side);
        gait += speed * 1.6f * Time.deltaTime;
        PoseWalkLeg(thighL, shinL, footL, restThighL, restShinL, restFootL, Mathf.Sin(gait), Mathf.Max(0f, Mathf.Cos(gait)), stride);
        PoseWalkLeg(thighR, shinR, footR, restThighR, restShinR, restFootR, Mathf.Sin(gait + Mathf.PI), Mathf.Max(0f, Mathf.Cos(gait + Mathf.PI)), stride);
    }

    void PoseWalkLeg(
        Transform thigh,
        Transform shin,
        Transform foot,
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

        float amount = 0.55f * gaitWeight;
        Vector3 thighDir = shin.position - thigh.position;
        AimBone(thigh, thighDir, Vector3.down + stride * cycle * amount);
        if (foot == null)
            return;

        Vector3 shinDir = foot.position - shin.position;
        Vector3 shinAim = Vector3.down + stride * cycle * amount * 0.35f + Vector3.up * lift * amount * 0.25f;
        if (shinAim.y > -0.15f)
            shinAim.y = -0.15f;
        AimBone(shin, shinDir, shinAim);
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

    void HangArm(Transform upper, Transform fore, Transform hand)
    {
        if (upper == null || fore == null)
            return;
        Vector3 from = fore.position - upper.position;
        AimBone(upper, from, Vector3.down + transform.forward * 0.2f);
        if (hand == null)
            return;
        Vector3 along = hand.position - fore.position;
        AimBone(fore, along, Vector3.down + transform.forward * 0.08f);
    }

    void PoseArm(Transform upper, Transform fore, Transform hand, Vector3 reach)
    {
        if (upper == null || fore == null)
            return;
        Vector3 from = fore.position - upper.position;
        Vector3 toShoulder = reach - upper.position;
        AimBone(upper, from, toShoulder.sqrMagnitude > 0.0001f ? toShoulder : transform.forward);
        if (hand == null)
            return;
        Vector3 along = hand.position - fore.position;
        Vector3 toHand = reach - fore.position;
        AimBone(fore, along, toHand.sqrMagnitude > 0.0001f ? toHand : transform.forward);
    }

    void DrownIfOnWater()
    {
        if (health == null || health.IsDead)
            return;
        Vector3 origin = transform.position + Vector3.up * 0.35f;
        int count = Probe(origin, Vector3.down, 0.7f);
        float nearest = float.MaxValue;
        bool water = false;
        Vector3 surface = transform.position;
        for (int i = 0; i < count; i++)
        {
            if (probeHits[i].collider == null || probeHits[i].collider.transform.root == transform)
                continue;
            if (probeHits[i].distance >= nearest)
                continue;
            nearest = probeHits[i].distance;
            surface = probeHits[i].point;
            water = probeHits[i].collider.GetComponentInParent<Hd2Water>() != null;
        }

        if (!water)
            return;
        Hd2Water.Splash(surface, true);
        health.MarkHit(surface + Vector3.up * 0.4f);
        health.ApplyHit(health.Health, false);
    }

    static Mesh fillerSphere;
    static Mesh fillerCapsule;
    static Material fillerBlack;

    void AddFillers(Transform robot)
    {
        Transform chest = FindBone(robot, "Chest", "Chest");
        Transform neck = FindBone(robot, "Neck", "Neck");
        Material black = FillerBlack(robot);
        AddFiller(chest, PrimitiveType.Sphere, ref fillerSphere, "Shoulder Left Filler",
            new Vector3(-0.167f, 0.119f, -0.016f), Vector3.one * 0.11f, Quaternion.identity, black);
        AddFiller(chest, PrimitiveType.Sphere, ref fillerSphere, "Shoulder Right Filler",
            new Vector3(0.14f, 0.119f, -0.032f), Vector3.one * 0.11f, Quaternion.identity, black);
        AddFiller(neck, PrimitiveType.Capsule, ref fillerCapsule, "neck filler",
            new Vector3(0f, 0.019f, -0.031f), new Vector3(0.1f, 0.08f, 0.1f), Quaternion.Euler(7.94f, 0f, 0f), black);
    }

    static Material FillerBlack(Transform robot)
    {
        if (fillerBlack != null)
            return fillerBlack;

        Renderer source = robot.GetComponentInChildren<Renderer>();
        if (source == null || source.sharedMaterial == null)
            return null;

        fillerBlack = new Material(source.sharedMaterial);
        if (fillerBlack.HasProperty("_BaseColor"))
            fillerBlack.SetColor("_BaseColor", Color.black);
        if (fillerBlack.HasProperty("_Color"))
            fillerBlack.SetColor("_Color", Color.black);
        if (fillerBlack.HasProperty("_EmissionColor"))
            fillerBlack.SetColor("_EmissionColor", Color.black);
        return fillerBlack;
    }

    static void AddFiller(
        Transform parent,
        PrimitiveType kind,
        ref Mesh cached,
        string fillerName,
        Vector3 localPosition,
        Vector3 localScale,
        Quaternion localRotation,
        Material material)
    {
        if (parent == null || material == null)
            return;

        if (cached == null)
        {
            var temp = GameObject.CreatePrimitive(kind);
            MeshFilter source = temp.GetComponent<MeshFilter>();
            if (source != null)
                cached = source.sharedMesh;
            if (Application.isPlaying)
                Destroy(temp);
            else
                DestroyImmediate(temp);
        }

        if (cached == null)
            return;

        var filler = new GameObject(fillerName);
        filler.transform.SetParent(parent, false);
        filler.transform.localPosition = localPosition;
        filler.transform.localRotation = localRotation;
        filler.transform.localScale = localScale;
        filler.AddComponent<MeshFilter>().sharedMesh = cached;
        var renderer = filler.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    void SilenceBodyAudio()
    {
        AudioSource[] sources = GetComponentsInChildren<AudioSource>(true);
        for (int i = 0; i < sources.Length; i++)
        {
            if (sources[i] != null && sources[i].isPlaying)
                sources[i].Stop();
        }
    }

    static Transform FindBone(Transform root, string dotted, string underscored)
    {
        Transform found = FindNamed(root, dotted);
        return found != null ? found : FindNamed(root, underscored);
    }

    static Quaternion Local(Transform bone)
    {
        return bone != null ? bone.localRotation : Quaternion.identity;
    }

    static void SetLocal(Transform bone, Quaternion rest)
    {
        if (bone != null)
            bone.localRotation = rest;
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

    public void ApplyBodyColor()
    {
        if (!useBodyColor)
            return;
        Transform suit = transform.Find("Body");
        if (suit != null)
            TintRenderers(suit, bodyColor);
    }

    static void TintRenderers(Transform root, Color color)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (Hd2Body.SkipArmorTint(renderer))
                continue;
            if (renderer.gameObject.name.IndexOf("filler", System.StringComparison.OrdinalIgnoreCase) >= 0)
                continue;
            Material[] materials = renderer.materials;
            for (int m = 0; m < materials.Length; m++)
            {
                Material material = materials[m];
                if (material == null)
                    continue;
                if (material.HasProperty("_BaseColor"))
                    material.SetColor("_BaseColor", color);
                if (material.HasProperty("_Color"))
                    material.SetColor("_Color", color);
            }
        }
    }

    static Transform FindNamed(Transform root, string childName)
    {
        if (root.name == childName)
            return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindNamed(root.GetChild(i), childName);
            if (found != null)
                return found;
        }

        return null;
    }

    IEnumerator Die(Transform killer, Vector3 hitPoint)
    {
        ragdolling = true;
        ReleaseGuns();
        SilenceBodyAudio();
        ShowKillMark(killer, hitPoint);
        CachePose();
        SilenceGunColliders();
        if (body != null)
            body.enabled = false;
        EnableRagdoll();
        if (health != null && health.KilledByBlast)
            Hd2Health.ThrowFromBlast(ragdollBodies, health.BlastOrigin, transform.position);
        yield return new WaitForSeconds(RagdollSeconds);
        if (this == null)
            yield break;

        FreezeRagdoll();
        for (int i = ragdollJoints.Count - 1; i >= 0; i--)
        {
            if (ragdollJoints[i] != null)
                Destroy(ragdollJoints[i]);
        }

        ragdollJoints.Clear();
        yield return null;
        if (this == null)
            yield break;

        RestorePose();
        for (int i = ragdollBodies.Count - 1; i >= 0; i--)
        {
            if (ragdollBodies[i] != null)
                Destroy(ragdollBodies[i]);
        }

        ragdollBodies.Clear();
        yield return null;
        if (this == null)
            yield break;

        RestoreGunColliders();
        if (Hd2Deathmatch.Over || health == null)
        {
            ragdolling = false;
            yield break;
        }

        health.Respawn();
        RestorePistols();
        ApplyBodyColor();
        if (domination && homeSpawn != null)
            transform.SetPositionAndRotation(homeSpawn.position, homeSpawn.rotation);
        if (body != null)
        {
            Vector3 pos = transform.position;
            Quaternion rot = transform.rotation;
            body.enabled = false;
            transform.SetPositionAndRotation(pos, rot);
            body.enabled = true;
        }

        ragdolling = false;
    }

    void FreezeRagdoll()
    {
        for (int i = 0; i < ragdollBodies.Count; i++)
        {
            Rigidbody rigidbody = ragdollBodies[i];
            if (rigidbody == null)
                continue;
            rigidbody.isKinematic = true;
            rigidbody.linearVelocity = Vector3.zero;
            rigidbody.angularVelocity = Vector3.zero;
            rigidbody.interpolation = RigidbodyInterpolation.None;
        }
    }

    void ShowKillMark(Transform killer, Vector3 point)
    {
        Hd2Locomotion player = killer != null ? killer.GetComponentInParent<Hd2Locomotion>() : null;
        if (player == null || killer.GetComponentInParent<Hd2EnemyBot>() != null)
            return;
        if (Friendly(player.transform))
            return;

        if (killMark != null)
            Destroy(killMark.gameObject);

        Vector3 chest = transform.position + Vector3.up * 1.1f;
        if (point.sqrMagnitude < 0.01f)
            point = chest;
        Vector3 outward = point - chest;
        if (outward.sqrMagnitude > 0.0001f)
            point += outward.normalized * 0.12f;

        var mark = new GameObject("KillMark");
        mark.transform.position = point;
        killMark = mark.transform;
        killMarkUntil = Time.time + RagdollSeconds;
        CreateKillBar(mark.transform, 42f);
        CreateKillBar(mark.transform, -42f);
    }

    static void CreateKillBar(Transform parent, float roll)
    {
        var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bar.name = "Bar";
        Collider box = bar.GetComponent<Collider>();
        if (box != null)
            Destroy(box);
        bar.transform.SetParent(parent, false);
        bar.transform.localRotation = Quaternion.Euler(0f, 0f, roll);
        bar.transform.localScale = new Vector3(0.5f, 0.08f, 0.03f);
        var renderer = bar.GetComponent<Renderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");
        if (shader == null)
            return;
        var material = new Material(shader);
        Color red = new Color(0.92f, 0.04f, 0.04f, 1f);
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", red);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", red);
        renderer.sharedMaterial = material;
    }

    void FaceKillMark()
    {
        if (killMark == null)
            return;
        if (Time.time >= killMarkUntil)
        {
            Destroy(killMark.gameObject);
            killMark = null;
            return;
        }

        Camera cam = Camera.main;
        if (cam == null)
            return;
        Vector3 to = cam.transform.position - killMark.position;
        if (to.sqrMagnitude < 0.0001f)
            return;
        killMark.rotation = Quaternion.LookRotation(to);
    }

    void CachePose()
    {
        poseBones.Clear();
        posePositions.Clear();
        poseRotations.Clear();
        Transform visual = transform.Find("Body");
        if (visual == null)
            return;
        Transform[] bones = visual.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < bones.Length; i++)
        {
            poseBones.Add(bones[i]);
            posePositions.Add(bones[i].localPosition);
            poseRotations.Add(bones[i].localRotation);
        }
    }

    void RestorePose()
    {
        for (int i = 0; i < poseBones.Count; i++)
        {
            if (poseBones[i] == null)
                continue;
            poseBones[i].localPosition = posePositions[i];
            poseBones[i].localRotation = poseRotations[i];
        }
    }

    void SilenceGunColliders()
    {
        silencedGunColliders.Clear();
        Collider[] colliders = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null || !collider.enabled || !UnderGun(collider.transform))
                continue;
            collider.enabled = false;
            silencedGunColliders.Add(collider);
        }
    }

    void RestoreGunColliders()
    {
        for (int i = 0; i < silencedGunColliders.Count; i++)
        {
            if (silencedGunColliders[i] != null)
                silencedGunColliders[i].enabled = true;
        }

        silencedGunColliders.Clear();
    }

    static bool UnderGun(Transform bone)
    {
        return bone.GetComponentInParent<Hd2Pistol>(true) != null
            || bone.GetComponentInParent<Hd2Smg>(true) != null
            || bone.GetComponentInParent<Hd2Shotgun>(true) != null
            || bone.GetComponentInParent<Hd2Shocks>(true) != null
            || bone.GetComponentInParent<Hd2LaserPistol>(true) != null
            || bone.GetComponentInParent<Hd2RocketLauncher>(true) != null
            || bone.GetComponentInParent<Hd2BurstPistol>(true) != null;
    }

    void EnableRagdoll()
    {
        Transform visual = transform.Find("Body");
        if (visual == null)
            return;

        Transform[] bones = visual.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < bones.Length; i++)
        {
            Transform bone = bones[i];
            if (bone.GetComponent<Collider>() == null || bone.GetComponent<Rigidbody>() != null)
                continue;
            if (UnderGun(bone))
                continue;
            var rigidbody = bone.gameObject.AddComponent<Rigidbody>();
            rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            ragdollBodies.Add(rigidbody);
        }

        var colliders = new List<Collider>(ragdollBodies.Count);
        for (int i = 0; i < ragdollBodies.Count; i++)
        {
            Collider collider = ragdollBodies[i].GetComponent<Collider>();
            if (collider != null)
                colliders.Add(collider);
        }

        for (int i = 0; i < colliders.Count; i++)
        {
            for (int j = i + 1; j < colliders.Count; j++)
                Physics.IgnoreCollision(colliders[i], colliders[j], true);
        }

        for (int i = 0; i < ragdollBodies.Count; i++)
        {
            Rigidbody rigidbody = ragdollBodies[i];
            Rigidbody parentBody = null;
            Transform ancestor = rigidbody.transform.parent;
            while (ancestor != null && ancestor != transform)
            {
                parentBody = ancestor.GetComponent<Rigidbody>();
                if (parentBody != null)
                    break;
                ancestor = ancestor.parent;
            }

            if (parentBody == null)
                continue;

            var joint = rigidbody.gameObject.AddComponent<CharacterJoint>();
            joint.connectedBody = parentBody;
            joint.enableProjection = true;
            ragdollJoints.Add(joint);
        }
    }

}
