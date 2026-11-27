using UnityEngine;

public enum Hd2Team
{
    None = 0,
    Red = 1,
    Blue = 2
}

/// <summary>
/// One-way barrier across a spawn-room opening.
/// Transform.forward is the outward normal: it points out of the room, toward the map.
/// Inside is the opposite side (the room).
///
/// Shots from inside pass through. Shots from outside stop on this collider.
/// Before the round opens, nothing crosses and nothing shoots through.
/// A shield with a team lets only that team cross. A shield with no team still lets
/// the local player through and stops everyone else.
/// Keep this object on the default layer with a solid (non-trigger) collider so
/// Hd2Shot, which raycasts every layer and ignores triggers, can hit it. Do not add Hd2NoLanding.
/// </summary>
public class Hd2SpawnField : MonoBehaviour
{
    [Tooltip("Who may cross this shield. None lets the local player through and stops everyone else.")]
    public Hd2Team team;

    [Tooltip("Shots from both teams stop on both sides. The matching team can still walk and blitz through.")]
    public bool blockAllShots;
    const float InsideSlack = 0.05f;

    /// <summary>
    /// Match start. While set, every shield stops every body and every shot.
    /// </summary>
    public static bool Locked { get; private set; }

    static readonly Hd2SpawnField[] fields = new Hd2SpawnField[16];
    static readonly CharacterController[] movers = new CharacterController[32];
    static readonly Component[] moverBodies = new Component[32];
    static int fieldCount;
    static int moverCount;

    static readonly RaycastHit[] groundHits = new RaycastHit[16];
    static readonly RaycastHit[] blockHits = new RaycastHit[16];
    static Material markMaterial;

    Vector3 outward;
    bool outwardKnown;

    /// <summary>
    /// Out of the base, across the shield. The shields are flat panels placed at any
    /// rotation (Shootout's are turned 90 degrees on X, so transform.forward points up on
    /// one and down on the other), so this is the collider's thinnest axis, turned to point
    /// away from this team's spawn points.
    /// </summary>
    public Vector3 Outward
    {
        get
        {
            if (!outwardKnown || !Application.isPlaying)
                outward = FindOutward();
            outwardKnown = true;
            return outward;
        }
    }

    Vector3 FindOutward()
    {
        Vector3 axis = transform.forward;
        var box = GetComponent<BoxCollider>();
        if (box != null)
        {
            Vector3 lossy = transform.lossyScale;
            Vector3 size = new Vector3(Mathf.Abs(box.size.x * lossy.x), Mathf.Abs(box.size.y * lossy.y), Mathf.Abs(box.size.z * lossy.z));
            axis = size.x <= size.y && size.x <= size.z ? transform.right
                : size.y <= size.z ? transform.up
                : transform.forward;
        }
        else
        {
            var mesh = GetComponent<MeshCollider>();
            if (mesh != null)
            {
                Vector3 size = mesh.bounds.size;
                axis = size.x <= size.y && size.x <= size.z ? Vector3.right
                    : size.y <= size.z ? Vector3.up
                    : Vector3.forward;
            }
        }

        axis.Normalize();
        // The room is where this team spawns.
        var spawns = FindObjectsByType<Hd2SpawnPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        float nearest = float.MaxValue;
        Vector3 room = Vector3.zero;
        bool found = false;
        for (int i = 0; i < spawns.Length; i++)
        {
            Hd2SpawnPoint spawn = spawns[i];
            if (spawn == null || spawn.team != team || spawn.gameObject.scene != gameObject.scene)
                continue;
            float d = (spawn.transform.position - transform.position).sqrMagnitude;
            if (d < nearest)
            {
                nearest = d;
                room = spawn.transform.position;
                found = true;
            }
        }

        if (found && Vector3.Dot(room - transform.position, axis) > 0f)
            axis = -axis;
        else if (!found && Vector3.Dot(axis, transform.forward) < 0f)
            axis = -axis;
        return axis;
    }

    /// <summary>
    /// True on the room side of the barrier, including a few centimeters of slack
    /// so a sample inside the thin volume still counts as inside.
    /// </summary>
    public bool IsInside(Vector3 worldPosition)
    {
        float side = Vector3.Dot(worldPosition - transform.position, Outward);
        return side <= InsideSlack;
    }

    /// <summary>
    /// Inside shots pass once the round is open. Outside shots stop.
    /// A locked shield stops every shot.
    /// </summary>
    public bool ShotPasses(Vector3 worldPosition)
    {
        if (Locked || blockAllShots)
            return false;
        return IsInside(worldPosition);
    }

    /// <summary>
    /// Whether this body may cross the shield.
    /// </summary>
    public bool MayEnter(Collider body)
    {
        return Allows(body);
    }

    public bool Allows(Component body)
    {
        if (Locked || body == null)
            return false;
        if (team == Hd2Team.None)
            return body.GetComponentInParent<Hd2Locomotion>() != null;
        return TeamOf(body) == team;
    }

    static bool watchingGate;
    static float openAt;

    void Start()
    {
        if (watchingGate)
            return;
        if (FindFirstObjectByType<Hd2Domination>() != null
            || FindFirstObjectByType<Hd2ControlPoint>() != null
            || FindFirstObjectByType<Hd2PayloadMatch>() != null
            || FindFirstObjectByType<Hd2Shootout>() != null)
            return;

        SetLocked(true);
        openAt = Time.time + Hd2Domination.GateSeconds;
        watchingGate = true;
    }

    void LateUpdate()
    {
        if (!watchingGate || Time.time < openAt)
            return;

        watchingGate = false;
        SetLocked(false);
    }

    void OnEnable()
    {
        for (int i = 0; i < fieldCount; i++)
        {
            if (fields[i] == this)
                return;
        }

        if (fieldCount < fields.Length)
            fields[fieldCount++] = this;
    }

    void OnDisable()
    {
        for (int i = 0; i < fieldCount; i++)
        {
            if (fields[i] != this)
                continue;
            fieldCount--;
            fields[i] = fields[fieldCount];
            fields[fieldCount] = null;
            return;
        }
    }

    /// <summary>
    /// Close every base shield, or open them again. Bodies registered with ApplyPass
    /// get their pass-through updated without a search.
    /// </summary>
    public static void SetLocked(bool locked)
    {
        Locked = locked;
        for (int i = 0; i < moverCount; i++)
        {
            if (movers[i] == null)
                continue;
            ApplyPass(moverBodies[i], movers[i]);
        }
    }

    public static Hd2Team TeamOf(Component body)
    {
        if (body == null)
            return Hd2Team.None;

        Hd2Locomotion player = body.GetComponentInParent<Hd2Locomotion>();
        if (player != null)
            return player.team;

        Hd2EnemyBot bot = body.GetComponentInParent<Hd2EnemyBot>();
        if (bot != null)
            return bot.team;

        Hd2NetPlayer net = body.GetComponentInParent<Hd2NetPlayer>();
        if (net != null)
            return net.team;

        return Hd2Team.None;
    }

    /// <summary>
    /// The bot or player who owns this part. Guns under a shared parent must not use the scene root,
    /// or a shot treats every bot in that group as the shooter and passes through them.
    /// </summary>
    public static Transform FighterBody(Component part)
    {
        if (part == null)
            return null;

        Hd2EnemyBot bot = part.GetComponentInParent<Hd2EnemyBot>();
        if (bot != null)
            return bot.transform;

        Hd2Locomotion player = part.GetComponentInParent<Hd2Locomotion>();
        if (player != null)
            return player.transform;

        return part.transform.root;
    }

    /// <summary>
    /// The local player counts as a teammate of an unassigned shield.
    /// </summary>
    public static bool IsTeammate(Collider body)
    {
        return body != null && body.GetComponentInParent<Hd2Locomotion>() != null;
    }

    /// <summary>
    /// Matching teams do not collide with this solid shield. Everyone else does.
    /// </summary>
    public static void ApplyPass(Component mover, Collider moverCollider)
    {
        if (mover == null || moverCollider == null)
            return;

        RememberMover(mover, moverCollider as CharacterController);
        for (int i = 0; i < fieldCount; i++)
        {
            Hd2SpawnField field = fields[i];
            if (field == null)
                continue;
            bool pass = field.Allows(mover);
            Collider[] shields = field.GetComponents<Collider>();
            for (int c = 0; c < shields.Length; c++)
            {
                Collider shield = shields[c];
                if (shield == null || shield == moverCollider || !shield.enabled)
                    continue;
                Physics.IgnoreCollision(moverCollider, shield, pass);
            }
        }

        Hd2DropShield.IgnoreMover(moverCollider);
    }

    static void RememberMover(Component mover, CharacterController body)
    {
        if (body == null)
            return;
        for (int i = 0; i < moverCount; i++)
        {
            if (movers[i] == body)
            {
                moverBodies[i] = mover;
                return;
            }
        }

        if (moverCount >= movers.Length)
            return;
        movers[moverCount] = body;
        moverBodies[moverCount] = mover;
        moverCount++;
    }

    /// <summary>
    /// True when the segment crosses a shield this body may not enter.
    /// </summary>
    public static bool Blocks(Component mover, Vector3 from, Vector3 to)
    {
        Vector3 delta = to - from;
        float distance = delta.magnitude;
        if (distance < 0.02f)
            return false;

        int count = Physics.RaycastNonAlloc(from, delta / distance, blockHits, distance, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider collider = blockHits[i].collider;
            if (collider == null)
                continue;
            Hd2SpawnField field = collider.GetComponentInParent<Hd2SpawnField>();
            if (field != null && !field.Allows(mover))
                return true;
        }

        return false;
    }

    /// <summary>
    /// True when a point is inside a shield or within radius of its surface.
    /// </summary>
    public static bool Touches(Vector3 point, float radius)
    {
        float reach = radius * radius;
        for (int i = 0; i < fieldCount; i++)
        {
            Hd2SpawnField field = fields[i];
            if (field == null)
                continue;
            if (NearCollider(field.GetComponent<BoxCollider>(), point, reach))
                return true;
            if (NearCollider(field.GetComponent<MeshCollider>(), point, reach))
                return true;
        }

        return false;
    }

    /// <summary>
    /// True when an active base shield stands between a blast and a target, either way.
    /// A rocket or shock orb that hits a shield goes off on its face. Only shots from
    /// outside stop on a shield, so a blast within BlastFaceSlack of it counts as outside:
    /// it still reaches attackers next to the shield, never the room behind it. Raycasts
    /// miss that case: the shield has almost no thickness and the blast starts on it.
    /// </summary>
    public static bool BlocksBlast(Vector3 point, Vector3 target)
    {
        const float BlastFaceSlack = 0.2f;
        const float EdgeReach = 0.5f;
        for (int i = 0; i < fieldCount; i++)
        {
            Hd2SpawnField field = fields[i];
            if (field == null)
                continue;
            Collider shape = field.GetComponent<BoxCollider>();
            if (shape == null)
                shape = field.GetComponent<MeshCollider>();
            if (shape == null || !shape.enabled || !shape.gameObject.activeInHierarchy)
                continue;

            Vector3 normal = field.Outward;
            float a = Vector3.Dot(point - field.transform.position, normal);
            float b = Vector3.Dot(target - field.transform.position, normal);
            bool onFace = Mathf.Abs(a) < BlastFaceSlack;
            float blastSide = onFace ? 1f : Mathf.Sign(a);
            if (blastSide == Mathf.Sign(b))
                continue;

            // Where the line crosses the shield plane; on the face, the blast point itself.
            Vector3 crossing = onFace || Mathf.Abs(a - b) < 0.0001f
                ? point
                : Vector3.Lerp(point, target, a / (a - b));
            if (NearCollider(shape, crossing, EdgeReach * EdgeReach))
                return true;
        }

        return false;
    }

    public static bool NearCollider(Collider collider, Vector3 point, float reach)
    {
        if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy)
            return false;
        Vector3 closest = collider.ClosestPoint(point);
        return (closest - point).sqrMagnitude <= reach;
    }

    /// <summary>
    /// Feet position just past this barrier along the thrust, on the floor when one is under that point.
    /// Aiming out lands outside. Aiming in lands inside.
    /// </summary>
    public Vector3 PassThroughPoint(Vector3 hitPoint, Vector3 travel, float bodyClearance, out Collider ground)
    {
        ground = null;
        Vector3 outward = Outward;
        Vector3 dir = travel;
        if (dir.sqrMagnitude < 0.000001f)
            dir = outward;
        dir.Normalize();

        float crossing = Vector3.Dot(dir, outward);
        float destSign = crossing >= 0f ? 1f : -1f;
        if (Mathf.Abs(crossing) < 0.08f)
        {
            float fromInside = Vector3.Dot(hitPoint - transform.position, outward);
            destSign = fromInside <= 0f ? 1f : -1f;
        }

        float gap = Mathf.Max(0.4f, bodyClearance + 0.2f);
        Vector3 beyond = hitPoint + dir * gap;
        float signed = Vector3.Dot(beyond - transform.position, outward);
        if (signed * destSign < gap)
            beyond += outward * (destSign * gap - signed);

        Vector3 probe = new Vector3(beyond.x, Mathf.Max(beyond.y, hitPoint.y) + 3f, beyond.z);
        int count = Physics.RaycastNonAlloc(probe, Vector3.down, groundHits, 8f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        Vector3 planted = new Vector3(beyond.x, 0f, beyond.z);
        for (int i = 0; i < count; i++)
        {
            Collider collider = groundHits[i].collider;
            if (collider == null)
                continue;
            if (collider.GetComponentInParent<Hd2SpawnField>() != null)
                continue;
            if (groundHits[i].normal.y < 0.45f)
                continue;
            if (groundHits[i].distance >= best)
                continue;

            best = groundHits[i].distance;
            planted = groundHits[i].point;
            ground = collider;
        }

        return planted;
    }

    public void RegisterBlockedShot(Vector3 point, Vector3 normal)
    {
        if (normal.sqrMagnitude < 0.0001f)
            normal = Outward;
        normal.Normalize();

        var mark = GameObject.CreatePrimitive(PrimitiveType.Quad);
        mark.name = "FieldHit";
        var collider = mark.GetComponent<Collider>();
        if (collider != null)
            Destroy(collider);

        mark.transform.SetParent(transform, true);
        mark.transform.position = point + normal * 0.03f;
        mark.transform.rotation = Quaternion.LookRotation(normal);
        mark.transform.localScale = Vector3.one * 0.09f;

        var renderer = mark.GetComponent<MeshRenderer>();
        if (renderer == null)
            return;

        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sharedMaterial = MarkMaterial();
        mark.AddComponent<Hd2FieldHit>();
    }

    static Material MarkMaterial()
    {
        if (markMaterial != null)
            return markMaterial;

        markMaterial = Resources.Load<Material>("FieldHit");
        if (markMaterial != null)
            return markMaterial;

        var shader = Shader.Find("HD2/FieldHit");
        markMaterial = shader != null ? new Material(shader) : null;
        if (markMaterial != null)
            markMaterial.SetColor("_BaseColor", new Color(0.02f, 0.04f, 0.06f, 1f));
        return markMaterial;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.95f);
        Vector3 origin = transform.position;
        Gizmos.DrawLine(origin, origin + Outward * 0.8f);
    }
}

/// <summary>
/// Dark shield hit, visible from either side. Fades out over 15 seconds, then goes away.
/// </summary>
sealed class Hd2FieldHit : MonoBehaviour
{
    const float Life = 15f;
    static readonly Color ink = new Color(0.02f, 0.04f, 0.06f, 1f);

    MeshRenderer markRenderer;
    MaterialPropertyBlock block;
    float age;

    void Awake()
    {
        markRenderer = GetComponent<MeshRenderer>();
        block = new MaterialPropertyBlock();
    }

    void Update()
    {
        age += Time.deltaTime;
        if (markRenderer != null && block != null)
        {
            Color color = ink;
            color.a = 1f - Mathf.Clamp01(age / Life);
            block.SetColor("_BaseColor", color);
            markRenderer.SetPropertyBlock(block);
        }

        if (age >= Life)
            Destroy(gameObject);
    }
}
