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
/// A shield with a team lets only that team cross. A shield with no team still lets
/// the local player through and stops everyone else.
/// Keep this object on the default layer with a solid (non-trigger) collider so
/// Hd2Shot, which raycasts every layer and ignores triggers, can hit it. Do not add Hd2NoLanding.
/// </summary>
public class Hd2SpawnField : MonoBehaviour
{
    [Tooltip("Who may cross this shield. None lets the local player through and stops everyone else.")]
    public Hd2Team team;
    const float InsideSlack = 0.05f;

    static readonly RaycastHit[] groundHits = new RaycastHit[16];
    static readonly RaycastHit[] blockHits = new RaycastHit[16];
    static Material markMaterial;

    public Vector3 Outward => transform.forward;

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
    /// Inside shots pass. Outside shots are stopped by the collider.
    /// </summary>
    public bool ShotPasses(Vector3 worldPosition)
    {
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
        if (body == null)
            return false;
        if (team == Hd2Team.None)
            return body.GetComponentInParent<Hd2Locomotion>() != null;
        return TeamOf(body) == team;
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

        return Hd2Team.None;
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

        Hd2SpawnField[] fields = Object.FindObjectsByType<Hd2SpawnField>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < fields.Length; i++)
        {
            Hd2SpawnField field = fields[i];
            if (field == null)
                continue;
            Collider shield = field.GetComponent<Collider>();
            if (shield == null || shield == moverCollider)
                continue;
            Physics.IgnoreCollision(moverCollider, shield, field.Allows(mover));
        }
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
