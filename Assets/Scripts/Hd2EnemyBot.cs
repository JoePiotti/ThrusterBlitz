using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Deathmatch bot. Wanders until it sees an enemy or takes a hit, then shoots the body
/// while sprinting, strafing, or blitzing. Uses the same speeds, blitz, and guns as the player.
/// </summary>
[DefaultExecutionOrder(-50)]
public class Hd2EnemyBot : MonoBehaviour
{
    public GameObject robotPrefab;
    public GameObject pistolModel;
    public Color bodyColor = Color.white;
    public bool useBodyColor;

    const float WalkSpeed = 2.5f;
    const float SprintSpeed = 5.5f;
    const float BlitzSpeed = 30f;
    const float BlitzRange = 13f;
    const float BlitzHop = 0.5f;
    const float GrindSpeed = 8f;
    const float SeeRange = 20f;
    const float SeeHalfAngle = 15f;
    const float AimJitter = 5f;
    const float DropCombatRange = 30f;
    const float RagdollSeconds = 5f;

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
    static readonly RaycastHit[] sightHits = new RaycastHit[16];
    Mode mode = Mode.Wander;
    WanderKind wander = WanderKind.Walk;
    CombatKind combat = CombatKind.Sprint;
    Transform target;
    Vector3 moveDir = Vector3.forward;
    Vector3 mapCenter;
    bool hasCenter;
    float modeUntil;
    float verticalVelocity;
    float fallTime;
    float lastMovedAt;
    float nextPump;
    float strafeFlipAt;
    int strafeSign = 1;
    bool wallReversed;
    bool traveling;
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
    Transform thighL;
    Transform thighR;
    Transform shinL;
    Transform shinR;
    Transform footL;
    Transform footR;
    Quaternion restThighL;
    Quaternion restThighR;
    Quaternion restShinL;
    Quaternion restShinR;
    Quaternion restFootL;
    Quaternion restFootR;
    float gait;
    float gaitWeight;
    bool ragdolling;
    float killMarkUntil;
    Transform killMark;
    readonly List<Transform> poseBones = new List<Transform>();
    readonly List<Vector3> posePositions = new List<Vector3>();
    readonly List<Quaternion> poseRotations = new List<Quaternion>();
    readonly List<Rigidbody> ragdollBodies = new List<Rigidbody>();
    readonly List<CharacterJoint> ragdollJoints = new List<CharacterJoint>();
    readonly List<Collider> silencedGunColliders = new List<Collider>();

    void Start()
    {
        body = gameObject.AddComponent<CharacterController>();
        body.height = 1.7f;
        body.radius = 0.25f;
        body.center = new Vector3(0f, 0.9f, 0f);
        body.stepOffset = 0.4f;
        body.skinWidth = 0.05f;

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
            GivePistol(leftHand, LeftPistolModel());
        }

        if (robotPrefab != null)
        {
            var robot = Instantiate(robotPrefab, transform);
            robot.name = "Body";
            robot.transform.localPosition = Vector3.zero;
            robot.transform.localRotation = Quaternion.identity;
            robot.transform.localScale = Vector3.one;
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
        }

        Hd2VisionCone.Register(transform);
        var players = FindObjectsByType<Hd2Locomotion>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] != null)
                Hd2VisionCone.Register(players[i].transform);
        }

        CacheCenter();
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

    GameObject LeftPistolModel()
    {
        var lasers = FindObjectsByType<Hd2LaserPickup>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < lasers.Length; i++)
        {
            if (lasers[i] != null && lasers[i].gripLeft != null)
                return lasers[i].gripLeft;
        }

        var shocks = FindObjectsByType<Hd2ShocksPickup>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < shocks.Length; i++)
        {
            if (shocks[i] != null && shocks[i].gripLeft != null)
                return shocks[i].gripLeft;
        }

        var smgs = FindObjectsByType<Hd2SmgPickup>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < smgs.Length; i++)
        {
            if (smgs[i] != null && smgs[i].gripLeft != null)
                return smgs[i].gripLeft;
        }

        return pistolModel;
    }

    void GivePistol(Transform hand, GameObject model)
    {
        var gun = Instantiate(model, hand);
        gun.name = "Pistol";
        gun.transform.localPosition = new Vector3(0f, 0.01f, 0.05f);
        gun.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        gun.transform.localScale = Vector3.one * 0.65f;
        gun.SetActive(false);
        var pistol = gun.AddComponent<Hd2Pistol>();
        pistol.hand = hand.name == "LeftHand" ? Hd2Pistol.Hand.Left : Hd2Pistol.Hand.Right;
        Transform muzzle = FindNamed(gun.transform, "Muzzle");
        if (muzzle != null)
        {
            muzzle.localRotation = Quaternion.Euler(90f, 0f, 0f);
            pistol.muzzle = muzzle;
        }

        gun.SetActive(true);
    }

    void OnHealth(Hd2Health current)
    {
        if (current.Health < lastHealth)
        {
            Transform attacker = current.LastAttacker;
            if (attacker != null && attacker.root != transform)
                Engage(attacker.root);
        }

        if (current.Health <= 0f)
        {
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
        if (Hd2Deathmatch.Over)
        {
            StopMotion();
            if (body != null && !body.enabled && health != null && health.Health > 0f)
                body.enabled = true;
            ReleaseGuns();
            return;
        }

        FaceKillMark();
        if (health != null && health.IsDead)
        {
            ReleaseGuns();
            return;
        }

        if (body != null && !body.enabled && health != null && health.Health > 0f)
        {
            body.enabled = true;
            EnterWander();
        }

        if (mode == Mode.Wander && SeesEnemy(out Transform seen))
            Engage(seen);

        if (mode == Mode.Combat && !TargetAlive())
            EnterWander();

        bool finishingRail = grinding || (traveling && travelRail != null);
        if (Time.time >= modeUntil && !finishingRail)
            PickSubmode();

        if (mode == Mode.Wander)
            TickWander();
        else
            TickCombat();

        TickTravel();
        if (!traveling && !grinding)
            TickWalk();
        DrownIfOnWater();
    }

    void LateUpdate()
    {
        FaceKillMark();
        if (health != null && health.IsDead)
            return;
        PoseLegs();
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
        StopMotion();
        ReleaseGuns();
        PickSubmode();
    }

    void Engage(Transform enemy)
    {
        if (enemy == null || enemy.root == transform)
            return;
        bool fresh = mode != Mode.Combat || target != enemy.root;
        target = enemy.root;
        mode = Mode.Combat;
        if (fresh)
            PickSubmode();
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
        var smgs = GetComponentsInChildren<Hd2Smg>();
        for (int i = 0; i < smgs.Length; i++)
            smgs[i].botHeld = combatFire;
        var lasers = GetComponentsInChildren<Hd2LaserPistol>();
        for (int i = 0; i < lasers.Length; i++)
            lasers[i].botHeld = combatFire;
        if (!combatFire)
            return;

        var pistols = GetComponentsInChildren<Hd2Pistol>();
        for (int i = 0; i < pistols.Length; i++)
            pistols[i].BotTap();

        if (Time.time < nextPump)
            return;
        nextPump = Time.time + 0.5f;
        var shotguns = GetComponentsInChildren<Hd2Shotgun>();
        for (int i = 0; i < shotguns.Length; i++)
            shotguns[i].BotTap();
        var shocks = GetComponentsInChildren<Hd2Shocks>();
        for (int i = 0; i < shocks.Length; i++)
            shocks[i].BotTap();
        var rockets = GetComponentsInChildren<Hd2RocketLauncher>();
        for (int i = 0; i < rockets.Length; i++)
            rockets[i].BotTap();
    }

    void StopMotion()
    {
        traveling = false;
        grinding = false;
        grindRail = null;
        travelRail = null;
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
            aim = transform.position + Quaternion.Euler(Random.Range(-AimJitter, AimJitter), Random.Range(-AimJitter, AimJitter), 0f) * fromBody;

        AimHand(rightHand, upperR, foreR, handR, aim, 0.12f);
        AimHand(leftHand, upperL, foreL, handL, aim, -0.12f);
    }

    void AimHand(Transform hand, Transform upper, Transform fore, Transform bone, Vector3 aim, float side)
    {
        Transform gun = GunOn(hand);
        if (gun == null)
        {
            HangArm(upper, fore, bone);
            return;
        }

        if (upper != null && fore != null)
        {
            Vector3 shoulder = upper.position;
            Vector3 reach = aim + transform.right * side - shoulder;
            if (reach.sqrMagnitude > 0.01f)
                PoseArm(upper, fore, bone, shoulder + reach.normalized * Mathf.Min(reach.magnitude, 0.62f));
        }

        if (hand == null)
            return;
        Transform muzzle = FindNamed(gun, "Muzzle");
        Vector3 from = muzzle != null ? muzzle.position : gun.position;
        Vector3 dir = aim - from;
        if (dir.sqrMagnitude < 0.01f)
            return;
        dir.Normalize();
        Vector3 barrel = muzzle != null ? muzzle.forward : hand.forward;
        if (barrel.sqrMagnitude < 0.0001f || Vector3.Dot(barrel, dir) > 0.9995f)
            return;
        hand.rotation = Quaternion.FromToRotation(barrel, dir) * hand.rotation;
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
        return GunChild<Hd2Pistol>(hand);
    }

    static Transform GunChild<T>(Transform hand) where T : Component
    {
        T gun = hand.GetComponentInChildren<T>();
        return gun != null ? gun.transform : null;
    }

    bool SeesEnemy(out Transform enemy)
    {
        enemy = null;
        if (eye == null)
            return false;

        Hd2VisionCone.CopyFighters(seen);
        float best = SeeRange;
        float minDot = Mathf.Cos(SeeHalfAngle * Mathf.Deg2Rad);
        for (int i = 0; i < seen.Count; i++)
        {
            Transform other = seen[i];
            if (other == null || other == transform)
                continue;
            var life = other.GetComponent<Hd2Health>();
            if (life != null && life.IsDead)
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

    bool Blocked(Vector3 origin, Vector3 dir, float distance, Transform root)
    {
        int count = Physics.RaycastNonAlloc(origin, dir, sightHits, distance, ~0, QueryTriggerInteraction.Ignore);
        float nearest = distance;
        bool wall = false;
        for (int i = 0; i < count; i++)
        {
            Transform hit = sightHits[i].collider.transform;
            if (hit.root == transform || hit.root == root)
                continue;
            if (sightHits[i].distance < nearest)
            {
                nearest = sightHits[i].distance;
                wall = true;
            }
        }

        return wall;
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
            if (Horizontal(floor) > BlitzRange)
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
        if (flat.magnitude < 0.8f || flat.magnitude > BlitzRange || StandingOnWater(landing))
            return false;
        if (meter != null && !meter.TrySpend())
            return false;

        grinding = false;
        grindRail = null;
        traveling = true;
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

        return true;
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
            if (body != null)
                body.enabled = false;
            transform.position = next;
            if (t < 1f)
                return;

            transform.position = travelEnd;
            traveling = false;
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

        grindDistance += grindSign * GrindSpeed * Time.deltaTime;
        bool ended = grindSign > 0f ? grindDistance >= grindRail.length - 0.3f : grindDistance <= 0.3f;
        grindDistance = Mathf.Clamp(grindDistance, 0f, grindRail.length);
        if (body != null)
            body.enabled = false;
        transform.position = grindRail.PointAlong(grindDistance);
        Vector3 ahead = grindRail.PointAlong(Mathf.Clamp(grindDistance + grindSign, 0f, grindRail.length));
        Face(Flat(ahead - transform.position));
        if (!ended)
            return;

        grinding = false;
        Hd2GrindRail finished = grindRail;
        float sign = grindSign;
        grindRail = null;
        if (body != null)
            body.enabled = true;
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

        float speed = mode == Mode.Combat && combat == CombatKind.Sprint ? SprintSpeed : WalkSpeed;
        if (moveDir.sqrMagnitude > 0.0001f && WaterAlong(moveDir, 0.9f))
            moveDir = Vector3.zero;
        Vector3 step = moveDir * speed;
        if (body.isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = -2f;
            fallTime = 0f;
        }
        else
        {
            verticalVelocity -= 15f * Time.deltaTime;
            verticalVelocity = Mathf.Max(verticalVelocity, -25f);
            if (!body.isGrounded)
                fallTime += Time.deltaTime;
        }

        if (fallTime >= 5f && health != null)
        {
            health.MarkHit(transform.position + Vector3.up * 1.1f);
            health.ApplyHit(health.Health, false);
            fallTime = 0f;
            return;
        }

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

    bool FloorBelow(Vector3 around, out Vector3 point)
    {
        point = around;
        RaycastHit[] hits = Physics.RaycastAll(around + Vector3.up * 4f, Vector3.down, 12f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        bool found = false;
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].collider.transform.root == transform)
                continue;
            if (hits[i].collider.GetComponentInParent<Hd2Water>() != null)
                continue;
            if (hits[i].normal.y < 0.55f || hits[i].distance >= best)
                continue;
            best = hits[i].distance;
            point = hits[i].point;
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
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 16f, ~0, QueryTriggerInteraction.Ignore);
        float nearest = float.MaxValue;
        bool water = false;
        bool found = false;
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].collider == null || hits[i].collider.transform.root == transform)
                continue;
            if (hits[i].normal.y < 0.45f || hits[i].distance >= nearest)
                continue;
            nearest = hits[i].distance;
            found = true;
            water = hits[i].collider.GetComponentInParent<Hd2Water>() != null;
        }

        return found && water;
    }

    bool StandingOnWater(Vector3 point)
    {
        Vector3 origin = point + Vector3.up * 0.35f;
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 0.8f, ~0, QueryTriggerInteraction.Ignore);
        float nearest = float.MaxValue;
        bool water = false;
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].collider == null || hits[i].collider.transform.root == transform)
                continue;
            if (hits[i].distance >= nearest)
                continue;
            nearest = hits[i].distance;
            water = hits[i].collider.GetComponentInParent<Hd2Water>() != null;
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
        RaycastHit[] hits = Physics.RaycastAll(origin, dir, distance, ~0, QueryTriggerInteraction.Ignore);
        float best = distance;
        bool found = false;
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].collider.transform.root == transform)
                continue;
            if (hits[i].normal.y > 0.45f || hits[i].distance >= best)
                continue;
            best = hits[i].distance;
            away = hits[i].normal;
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

    void TakeWeapon(Component source, Transform hand)
    {
        if (source is Hd2SmgPickup smg)
            smg.BotTake(hand);
        else if (source is Hd2RocketPickup rocket)
            rocket.BotTake(hand);
        else if (source is Hd2ShocksPickup shocks)
            shocks.BotTake(hand);
        else if (source is Hd2LaserPickup laser)
            laser.BotTake(hand);
        else if (source is Hd2ShotgunPickup shotgun)
            shotgun.BotTake(hand);
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
    }

    void Face(Vector3 flat)
    {
        flat.y = 0f;
        if (flat.sqrMagnitude < 0.0001f)
            return;
        transform.rotation = Quaternion.LookRotation(flat.normalized);
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
        if (height < 0.3f || height > 2.4f)
            robot.localScale = Vector3.one * (1.55f / Mathf.Max(0.2f, height));
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
        thighL = FindBone(robot, "Thigh.L", "Thigh_L");
        shinL = FindBone(robot, "Shin.L", "Shin_L");
        footL = FindBone(robot, "Foot.L", "Foot_L");
        thighR = FindBone(robot, "Thigh.R", "Thigh_R");
        shinR = FindBone(robot, "Shin.R", "Shin_R");
        footR = FindBone(robot, "Foot.R", "Foot_R");
        restThighL = Local(thighL);
        restThighR = Local(thighR);
        restShinL = Local(shinL);
        restShinR = Local(shinR);
        restFootL = Local(footL);
        restFootR = Local(footR);
    }

    void SeatHands()
    {
        if (handR != null && rightHand != null)
        {
            rightHand.SetParent(handR, false);
            rightHand.localPosition = Vector3.zero;
            rightHand.localRotation = Quaternion.identity;
        }

        if (handL != null && leftHand != null)
        {
            leftHand.SetParent(handL, false);
            leftHand.localPosition = Vector3.zero;
            leftHand.localRotation = Quaternion.identity;
        }
    }

    void PoseLegs()
    {
        if (thighL == null || thighR == null)
            return;

        Vector3 velocity = Vector3.zero;
        if (traveling)
            velocity = travelEnd - travelStart;
        else if (grinding)
            velocity = transform.forward * GrindSpeed;
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
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 0.7f, ~0, QueryTriggerInteraction.Ignore);
        float nearest = float.MaxValue;
        bool water = false;
        Vector3 surface = transform.position;
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].collider == null || hits[i].collider.transform.root == transform)
                continue;
            if (hits[i].distance >= nearest)
                continue;
            nearest = hits[i].distance;
            surface = hits[i].point;
            water = hits[i].collider.GetComponentInParent<Hd2Water>() != null;
        }

        if (!water)
            return;
        Hd2Water.Splash(surface, true);
        health.MarkHit(surface + Vector3.up * 0.4f);
        health.ApplyHit(health.Health, false);
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

    static void TintRenderers(Transform root, Color color)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || renderer is ParticleSystemRenderer)
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
        ShowKillMark(killer, hitPoint);
        CachePose();
        SilenceGunColliders();
        if (body != null)
            body.enabled = false;
        EnableRagdoll();
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
        if (killer == null || killer.GetComponentInParent<Hd2Locomotion>() == null)
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
            || bone.GetComponentInParent<Hd2RocketLauncher>(true) != null;
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
