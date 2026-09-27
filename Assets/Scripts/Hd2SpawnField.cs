using UnityEngine;

/// <summary>
/// One-way barrier across a spawn-room opening.
/// Transform.forward is the outward normal: it points out of the room, toward the map.
/// Inside is the opposite side (the room).
///
/// Shots from inside pass through. Shots from outside stop on this collider.
/// Teammates may enter. Enemies must be blocked from crossing into the room.
/// There are no enemies yet, and there is no enemy layer. Keep this object on the
/// default layer with a solid (non-trigger) collider so Hd2Shot, which raycasts
/// every layer and ignores triggers, can hit it. Do not add Hd2NoLanding.
/// A later enemy mover that sets position directly must call MayEnter and refuse
/// the move when it returns false. Physics bodies are already stopped by the collider.
/// The local player is a teammate: Hd2Locomotion does not treat this collider as a wall,
/// and MayEnter returns true for a collider under Hd2Locomotion.
/// </summary>
public class Hd2SpawnField : MonoBehaviour
{
    const float InsideSlack = 0.05f;

    static readonly RaycastHit[] groundHits = new RaycastHit[16];
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
    /// Whether this body may cross into the room.
    /// Teammates may enter. Enemies must be blocked: anything that is not a teammate returns false.
    /// </summary>
    public bool MayEnter(Collider body)
    {
        return IsTeammate(body);
    }

    /// <summary>
    /// The local player counts as a teammate. A future enemy collider does not.
    /// </summary>
    public static bool IsTeammate(Collider body)
    {
        return body != null && body.GetComponentInParent<Hd2Locomotion>() != null;
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
    }

    static Material MarkMaterial()
    {
        if (markMaterial != null)
            return markMaterial;

        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Lit");
        markMaterial = shader != null ? new Material(shader) : null;
        if (markMaterial != null)
            markMaterial.color = new Color(0.02f, 0.04f, 0.06f, 1f);
        return markMaterial;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.95f);
        Vector3 origin = transform.position;
        Gizmos.DrawLine(origin, origin + Outward * 0.8f);
    }
}
