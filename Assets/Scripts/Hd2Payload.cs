using UnityEngine;

/// <summary>
/// Rides a grind rail on its front and back feet. Moves forward only while living
/// blue players stand on the deck. One living red player stops it. Riders are
/// carried with the cart, including its turns. A grinder who catches the tail
/// steps onto the rear deck. A thin volume in front of the glass runs down anyone
/// who crosses it. Touching the glass itself does not.
/// </summary>
[DefaultExecutionOrder(1000)]
public class Hd2Payload : MonoBehaviour
{
    const float BaseSpeed = 0.5f;
    const int MaxRiders = 16;

    public Hd2GrindRail rail;
    public Transform frontFoot;
    public Transform backFoot;

    float frontDistance;
    float span;
    Vector3 frontLocal;
    Vector3 backLocal;
    bool posed;

    Vector3 lastPosition;
    Quaternion lastRotation;
    bool hasLast;

    readonly Collider[] overlap = new Collider[32];
    readonly Collider[] footHits = new Collider[8];
    readonly Component[] seen = new Component[MaxRiders];
    readonly Transform[] riders = new Transform[MaxRiders];
    int riderCount;
    Transform floor;
    UnityEngine.Mesh floorMesh;
    BoxCollider frontKill;
    Collider glass;
    Vector3 noseDir;
    Vector3 noseRight;
    Vector3 noseOrigin;
    float noseFace;
    float noseHalfWidth;
    float noseBottom;
    float noseTop;
    bool noseReady;
    static Hd2Payload current;

    public static Hd2Payload Current => current;
    Vector3 noseLocal;
    Vector3 tailLocal;
    static readonly RaycastHit[] deckHits = new RaycastHit[16];

    void Start()
    {
        if (rail == null)
            rail = FindFirstObjectByType<Hd2GrindRail>();
        if (frontFoot == null)
            frontFoot = FindNamed("Front Foot");
        if (backFoot == null)
            backFoot = FindNamed("Back Foot");
        floor = FindNamed("Floor");
        if (floor != null)
        {
            var filter = floor.GetComponent<MeshFilter>();
            if (filter != null)
                floorMesh = filter.sharedMesh;
        }

        if (rail == null || frontFoot == null || backFoot == null)
            return;

        frontLocal = transform.InverseTransformPoint(frontFoot.position);
        backLocal = transform.InverseTransformPoint(backFoot.position);
        span = Vector3.Distance(frontLocal, backLocal);
        frontDistance = Mathf.Max(span, rail.DistanceAlong(frontFoot.position));
        if (frontDistance > rail.length)
            frontDistance = rail.length;
        Pose(frontDistance);
        posed = true;
        MeasureEnds();
        EnsureFrontKill();
        PlaceFrontKill();
        lastPosition = transform.position;
        lastRotation = transform.rotation;
        hasLast = true;
    }

    void Update()
    {
        if (!posed || rail == null || Hd2SpawnField.Locked)
            return;

        float rate = Speed();
        if (rate <= 0f || frontDistance >= rail.length)
            return;

        frontDistance = Mathf.Min(rail.length, frontDistance + rate * Time.deltaTime);
        Pose(frontDistance);
    }

    public Vector3 FlatForward
    {
        get
        {
            if (frontFoot == null || backFoot == null)
                return transform.forward;

            Vector3 forward = frontFoot.position - backFoot.position;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
                return transform.forward;
            return forward.normalized;
        }
    }

    void OnEnable()
    {
        current = this;
    }

    void OnDisable()
    {
        if (current == this)
            current = null;
    }

    void LateUpdate()
    {
        if (!posed)
            return;

        PlaceFrontKill();
        CatchGrinders();
        FrontContact();
        int blue = 0;
        int red = 0;
        CountRiders(ref blue, ref red);
        CarryRiders();
        lastPosition = transform.position;
        lastRotation = transform.rotation;
        hasLast = true;
    }

    void CatchGrinders()
    {
        if (rail == null)
            return;

        float tail = TailDistance();
        float nose = NoseDistance();
        if (nose < tail)
        {
            float swap = nose;
            nose = tail;
            tail = swap;
        }
        CatchBots();
        int count = Hd2Locomotion.ActiveCount;
        for (int i = 0; i < count; i++)
        {
            Hd2Locomotion mover = Hd2Locomotion.ActiveAt(i);
            if (mover == null || !mover.OnRail(rail))
                continue;

            float reach = Mathf.Max(0.2f, mover.bodyRadius);
            float distance = mover.RailDistance;
            bool fromRear = mover.RailDirection >= 0f;
            if (fromRear)
            {
                if (distance + reach < tail || distance > nose)
                    continue;
                var health = mover.GetComponent<Hd2Health>();
                if (health != null && health.IsDead)
                    continue;
                mover.StepOffRail(RearDeckPoint());
            }
            else if (distance - reach <= nose && distance > tail)
            {
                if (mover.IsThrusting || mover.PassingFront)
                    continue;
                var death = mover.GetComponent<Hd2PlayerDeath>();
                if (death != null)
                    death.CrushUnderPayload(this);
                else
                {
                    var health = mover.GetComponent<Hd2Health>();
                    if (health != null && !health.IsDead)
                        health.ApplyHit(health.Health, false);
                }
            }
        }
    }

    void CatchBots()
    {
        if (rail == null)
            return;

        int count = Hd2EnemyBot.ActiveCount;
        for (int i = 0; i < count; i++)
        {
            Hd2EnemyBot bot = Hd2EnemyBot.ActiveAt(i);
            if (bot == null || !bot.OnRail(rail))
                continue;
            if (!TryPlaceFromRear(bot.RailDistance, bot.RailDirection, out Vector3 deck))
                continue;
            var health = bot.GetComponent<Hd2Health>();
            if (health != null && health.IsDead)
                continue;
            bot.BoardFromRear(deck);
        }
    }

    public bool TryPlaceFromRear(float distance, float direction, out Vector3 deck)
    {
        deck = Vector3.zero;
        if (rail == null || direction < 0f)
            return false;
        RailSpan(out float tail, out float nose);
        if (distance + 1.1f < tail || distance > nose)
            return false;
        deck = RearDeckPoint();
        return true;
    }

    public void RailSpan(out float tail, out float nose)
    {
        tail = 0f;
        nose = 0f;
        if (rail == null)
            return;
        tail = TailDistance();
        nose = NoseDistance();
        if (nose < tail)
        {
            float swap = nose;
            nose = tail;
            tail = swap;
        }
    }

    public Vector3 PointPastNose(float extra)
    {
        if (rail == null)
            return transform.position + FlatForward * extra;
        RailSpan(out _, out float nose);
        return rail.PointAlong(Mathf.Clamp(nose + extra, 0f, rail.length));
    }

    void EnsureFrontKill()
    {
        var shield = GetComponentInChildren<Hd2PayloadShield>(true);
        glass = shield != null ? shield.GetComponent<Collider>() : null;

        if (frontKill != null)
            return;

        var volume = new GameObject("Front Kill");
        volume.transform.SetParent(transform, false);
        frontKill = volume.AddComponent<BoxCollider>();
        frontKill.isTrigger = true;
        var marker = volume.AddComponent<Hd2PayloadFront>();
        marker.payload = this;
    }

    void PlaceFrontKill()
    {
        if (frontKill == null || glass == null)
            return;

        Vector3 forward = FlatForward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
            return;
        forward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, forward);

        Bounds glassBox = glass.bounds;
        float face = Vector3.Dot(glassBox.center, forward) + AxisReach(glassBox.extents, forward);
        float mid = Vector3.Dot(glassBox.center, right);
        float width = AxisReach(glassBox.extents, right) * 2f;
        float bottom = glassBox.min.y;
        if (floor != null)
        {
            Collider deck = floor.GetComponent<Collider>();
            if (deck != null)
            {
                Bounds deckBox = deck.bounds;
                float deckWidth = AxisReach(deckBox.extents, right) * 2f;
                if (deckWidth > width)
                    width = deckWidth;
                bottom = deckBox.min.y;
            }
        }

        float top = glassBox.max.y;
        if (top < bottom + 0.2f)
            top = bottom + 0.2f;

        const float thickness = 0.05f;
        Vector3 center = right * mid + forward * (face + thickness * 0.5f);
        center.y = (bottom + top) * 0.5f;
        frontKill.transform.SetPositionAndRotation(center, Quaternion.LookRotation(forward, Vector3.up));
        frontKill.size = new Vector3(Mathf.Max(0.5f, width), top - bottom, thickness);

        noseDir = forward;
        noseRight = right;
        noseOrigin = right * mid + forward * face;
        noseOrigin.y = center.y;
        noseFace = face;
        noseHalfWidth = Mathf.Max(0.5f, width) * 0.5f;
        noseBottom = bottom;
        noseTop = top;
        noseReady = true;
    }

    public static Vector3 SteerBot(Vector3 feet, Vector3 move)
    {
        if (current == null)
            return move;
        return current.SteerClear(feet, move);
    }

    public static bool BlocksRoute(Vector3 from, Vector3 to)
    {
        if (current == null)
            return false;
        return current.CrossesNose(from, to);
    }

    Vector3 SteerClear(Vector3 feet, Vector3 move)
    {
        if (!noseReady || move.sqrMagnitude < 0.0001f)
            return move;

        Vector3 step = feet + move.normalized * 1.6f;
        bool intoNose = Vector3.Dot(move, -noseDir) > 0.2f;
        if (!CrossesNose(feet, step) && !(InFrontCorridor(feet) && intoNose))
            return move;

        float side = Vector3.Dot(feet - noseOrigin, noseRight);
        Vector3 around = side >= 0f ? noseRight : -noseRight;
        float along = Vector3.Dot(feet, noseDir);
        if (along < noseFace + 1.6f)
            around = (around + noseDir * 0.7f).normalized;
        return around;
    }

    bool InFrontCorridor(Vector3 feet)
    {
        if (!noseReady)
            return false;
        float along = Vector3.Dot(feet, noseDir);
        float side = Mathf.Abs(Vector3.Dot(feet - noseOrigin, noseRight));
        return along > noseFace - 0.15f && along < noseFace + 4.5f
            && side < noseHalfWidth + 0.8f
            && feet.y > noseBottom - 0.4f && feet.y < noseTop + 0.6f;
    }

    bool CrossesNose(Vector3 from, Vector3 to)
    {
        if (!noseReady)
            return false;
        float fromAlong = Vector3.Dot(from, noseDir);
        float toAlong = Vector3.Dot(to, noseDir);
        if (fromAlong < noseFace - 0.2f || toAlong > fromAlong)
            return false;
        float span = fromAlong - toAlong;
        if (span < 0.001f)
            return false;
        float t = Mathf.Clamp01((fromAlong - noseFace) / span);
        Vector3 cross = Vector3.Lerp(from, to, t);
        float side = Mathf.Abs(Vector3.Dot(cross - noseOrigin, noseRight));
        return side <= noseHalfWidth + 0.7f
            && cross.y > noseBottom - 0.4f && cross.y < noseTop + 0.6f;
    }

    static float AxisReach(Vector3 extents, Vector3 axis)
    {
        return Mathf.Abs(extents.x * axis.x) + Mathf.Abs(extents.y * axis.y) + Mathf.Abs(extents.z * axis.z);
    }

    void FrontContact()
    {
        int players = Hd2Locomotion.ActiveCount;
        for (int i = 0; i < players; i++)
            FrontTouch(Hd2Locomotion.ActiveAt(i));

        int bots = Hd2EnemyBot.ActiveCount;
        for (int i = 0; i < bots; i++)
            FrontTouch(Hd2EnemyBot.ActiveAt(i));
    }

    void FrontTouch(Hd2Locomotion mover)
    {
        if (mover == null)
            return;
        var health = mover.GetComponent<Hd2Health>();
        if (health != null && health.IsDead)
            return;

        bool touching = mover.HitsShield(this);
        if (mover.IsThrusting)
        {
            if (touching)
                mover.PassingFront = true;
            return;
        }

        if (mover.PassingFront)
        {
            if (!touching)
                mover.PassingFront = false;
            return;
        }

        if (touching)
            KillByFront(mover);
    }

    void FrontTouch(Hd2EnemyBot bot)
    {
        if (bot == null)
            return;
        var health = bot.GetComponent<Hd2Health>();
        if (health != null && health.IsDead)
            return;

        bool touching = bot.HitsShield(this);
        if (bot.IsBlitzing)
        {
            if (touching)
                bot.PassingFront = true;
            return;
        }

        if (bot.PassingFront)
        {
            if (!touching)
                bot.PassingFront = false;
            return;
        }

        if (touching)
            KillByFront(bot);
    }

    void KillByFront(Component body)
    {
        var death = body.GetComponent<Hd2PlayerDeath>();
        if (death != null)
        {
            death.CrushUnderPayload(this);
            return;
        }

        var health = body.GetComponent<Hd2Health>();
        if (health != null && !health.IsDead)
            health.ApplyHit(health.Health, false);
    }

    void MeasureEnds()
    {
        Vector3 axis = frontLocal - backLocal;
        if (axis.sqrMagnitude < 0.0001f)
            axis = Vector3.forward;
        axis.Normalize();
        float nose = Vector3.Dot(frontLocal, axis);
        float tail = Vector3.Dot(backLocal, axis);
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer))
                continue;
            Bounds box = renderer.localBounds;
            Vector3 center = box.center;
            Vector3 extents = box.extents;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 localCorner = center + new Vector3(
                    (corner & 1) == 0 ? -extents.x : extents.x,
                    (corner & 2) == 0 ? -extents.y : extents.y,
                    (corner & 4) == 0 ? -extents.z : extents.z);
                Vector3 onCart = transform.InverseTransformPoint(renderer.transform.TransformPoint(localCorner));
                float along = Vector3.Dot(onCart, axis);
                if (along > nose)
                    nose = along;
                if (along < tail)
                    tail = along;
            }
        }

        noseLocal = axis * nose;
        tailLocal = axis * tail;
    }

    float NoseDistance()
    {
        return rail.DistanceAlong(transform.TransformPoint(noseLocal));
    }

    float TailDistance()
    {
        return rail.DistanceAlong(transform.TransformPoint(tailLocal));
    }

    public bool DeckUnder(Vector3 sample, float feetY, float maxRise, out Vector3 stand)
    {
        stand = sample;
        if (floor == null || maxRise <= 0f)
            return false;

        Vector3 origin = new Vector3(sample.x, feetY + maxRise + 0.35f, sample.z);
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, deckHits, maxRise + 0.7f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            Collider collider = deckHits[i].collider;
            if (collider == null || collider.transform != floor)
                continue;
            if (deckHits[i].normal.y < 0.45f || deckHits[i].distance >= best)
                continue;
            best = deckHits[i].distance;
            stand = deckHits[i].point;
            found = true;
        }

        return found;
    }

    public Vector3 StandOnDeck(int slot)
    {
        float angle = slot * 2.15f;
        Vector3 center = floor != null ? floor.position : transform.position;
        Vector3 offset = floor != null
            ? floor.TransformVector(new Vector3(Mathf.Cos(angle) * 0.7f, 0f, Mathf.Sin(angle) * 1.2f))
            : new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        Vector3 probe = center + offset;
        if (DeckUnder(probe, probe.y + 1f, 4f, out Vector3 stand))
            return stand + Vector3.up * 0.05f;
        return probe;
    }

    Vector3 RearDeckPoint()
    {
        Vector3 forward = FlatForward;
        Vector3 stand = backFoot != null ? backFoot.position + forward * 0.75f : transform.position;
        if (floor != null && floorMesh != null)
        {
            Bounds box = floorMesh.bounds;
            Vector3 rear = floor.TransformPoint(new Vector3(box.center.x, box.max.y, box.min.z));
            stand = rear + forward * 0.7f;
        }

        Vector3 origin = stand + Vector3.up * 3f;
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, deckHits, 6f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            Collider collider = deckHits[i].collider;
            if (collider == null || deckHits[i].distance >= best)
                continue;
            if (collider.transform != transform && !collider.transform.IsChildOf(transform))
                continue;
            best = deckHits[i].distance;
            stand = deckHits[i].point;
        }

        stand += Vector3.up * 0.04f;
        return stand;
    }

    float Speed()
    {
        int blue = 0;
        int red = 0;
        CountRiders(ref blue, ref red);
        if (red > 0 || blue <= 0)
            return 0f;
        if (blue == 1)
            return BaseSpeed;
        if (blue == 2)
            return BaseSpeed * 1.1f;
        return BaseSpeed * 1.2f;
    }

    void CountRiders(ref int blue, ref int red)
    {
        riderCount = 0;
        if (floor == null || floorMesh == null)
            return;

        Bounds box = floorMesh.bounds;
        Vector3 scale = floor.lossyScale;
        Vector3 half = new Vector3(
            box.extents.x * Mathf.Abs(scale.x) + 0.3f,
            1.3f,
            box.extents.z * Mathf.Abs(scale.z) + 0.3f);
        Vector3 center = floor.TransformPoint(new Vector3(box.center.x, box.max.y, box.center.z));
        center += floor.up * 1.1f;
        int count = Physics.OverlapBoxNonAlloc(center, half, overlap, floor.rotation, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider collider = overlap[i];
            if (collider == null || collider.transform == transform || collider.transform.IsChildOf(transform))
                continue;

            Component body = collider.GetComponentInParent<Hd2Locomotion>();
            if (body == null)
                body = collider.GetComponentInParent<Hd2EnemyBot>();
            if (body == null || AlreadySeen(body))
                continue;

            var health = body.GetComponent<Hd2Health>();
            if (health != null && health.IsDead)
                continue;
            if (!FeetOnDeck(body.transform.position))
                continue;

            Hd2Team team = Hd2SpawnField.TeamOf(body);
            if (team == Hd2Team.Blue)
                blue++;
            else if (team == Hd2Team.Red)
                red++;
            else
                continue;

            if (riderCount < riders.Length)
                riders[riderCount++] = body.transform;
        }
    }

    public bool FeetOnDeck(Vector3 feet)
    {
        if (floor == null)
            return false;

        int count = Physics.OverlapSphereNonAlloc(
            feet + Vector3.up * 0.06f,
            0.18f,
            footHits,
            ~0,
            QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider hit = footHits[i];
            if (hit == null)
                continue;
            Transform touched = hit.transform;
            if (touched == floor || touched.IsChildOf(floor))
                return true;
        }

        return false;
    }

    bool AlreadySeen(Component body)
    {
        for (int i = 0; i < riderCount; i++)
        {
            if (riders[i] == body.transform)
                return true;
        }

        return false;
    }

    void CarryRiders()
    {
        if (!hasLast || riderCount == 0)
            return;

        Quaternion delta = transform.rotation * Quaternion.Inverse(lastRotation);
        for (int i = 0; i < riderCount; i++)
        {
            Transform rider = riders[i];
            if (rider == null)
                continue;
            Vector3 offset = rider.position - lastPosition;
            rider.position = transform.position + delta * offset;
            rider.rotation = delta * rider.rotation;
        }
    }

    void Pose(float distance)
    {
        Vector3 front = rail.PointAlong(distance);
        float backDistance = BackDistance(distance);
        Vector3 back = rail.PointAlong(backDistance);
        Vector3 desired = front - back;
        Vector3 localSpan = frontLocal - backLocal;
        if (desired.sqrMagnitude < 0.0001f || localSpan.sqrMagnitude < 0.0001f)
            return;

        Quaternion rot = Quaternion.LookRotation(desired.normalized, Vector3.up)
            * Quaternion.Inverse(Quaternion.LookRotation(localSpan.normalized, Vector3.up));
        transform.SetPositionAndRotation(front - rot * frontLocal, rot);
    }

    float BackDistance(float front)
    {
        float target = span;
        float lo = 0f;
        float hi = front;
        if (Vector3.Distance(rail.PointAlong(front), rail.PointAlong(0f)) <= target)
            return 0f;

        for (int i = 0; i < 14; i++)
        {
            float mid = (lo + hi) * 0.5f;
            float chord = Vector3.Distance(rail.PointAlong(front), rail.PointAlong(mid));
            if (chord < target)
                hi = mid;
            else
                lo = mid;
        }

        return lo;
    }

    Transform FindNamed(string childName)
    {
        var transforms = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            if (transforms[i].name == childName)
                return transforms[i];
        }

        return null;
    }
}

/// <summary>
/// The thin volume in front of the payload glass. Crossing it kills. The glass does not.
/// </summary>
public class Hd2PayloadFront : MonoBehaviour
{
    public Hd2Payload payload;
}
