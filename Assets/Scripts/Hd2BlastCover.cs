using UnityEngine;

/// <summary>
/// How much of an explosion (rocket, shocks) reaches a fighter or a body through what
/// stands in between. Walls, drop shields and base shields stop it. Light cover (the
/// signs, and anything under 1.3 m tall) only takes the edge off: half the blast gets
/// through. A fighter is hit by the more exposed of their chest and head, so ducking
/// behind something low still leaves the head out. A couple of rays per fighter in range,
/// only when something explodes.
/// </summary>
public static class Hd2BlastCover
{
    public const float LightCoverShare = 0.5f;
    const float LowCoverHeight = 1.3f;

    static readonly RaycastHit[] hits = new RaycastHit[24];

    /// <summary>Share of the blast that reaches the fighter: 1 in the open, 0 behind a wall.</summary>
    public static float Exposure(Vector3 point, Transform fighter, Transform blastSource = null)
    {
        if (fighter == null)
            return 1f;
        Vector3 chest = fighter.position + Vector3.up * 0.9f;
        Vector3 head = fighter.position + Vector3.up * 1.5f;
        if (Hd2DropShield.Blocks(point, chest))
            return 0f;
        return Mathf.Max(Reach(point, chest, fighter, blastSource), Reach(point, head, fighter, blastSource));
    }

    /// <summary>Share of the blast that reaches one point: 1 clear, LightCoverShare through light cover, 0 blocked.</summary>
    public static float Reach(Vector3 point, Vector3 target, Transform ignore, Transform blastSource = null)
    {
        if (Hd2DropShield.Blocks(point, target))
            return 0f;

        Vector3 delta = target - point;
        float distance = delta.magnitude;
        if (distance < 0.1f)
            return 1f;
        Vector3 direction = delta / distance;
        // Start just off the impact point so the surface the rocket hit doesn't count,
        // unless the target is on its far side.
        Vector3 from = point + direction * 0.05f;
        bool previous = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true;
        int count = Physics.RaycastNonAlloc(from, direction, hits, distance - 0.05f, ~0, QueryTriggerInteraction.Ignore);
        Physics.queriesHitBackfaces = previous;

        float share = 1f;
        for (int i = 0; i < count; i++)
        {
            float cover = Cover(hits[i].collider, ignore, blastSource);
            if (cover >= 1f)
                return 0f;
            if (cover > 0f)
                share = Mathf.Min(share, LightCoverShare);
        }

        return share;
    }

    /// <summary>1 when cover is solid; true when anything solid stands in the way.</summary>
    public static bool Covered(Vector3 point, Vector3 target, Transform ignore, Transform blastSource = null)
    {
        return Reach(point, target, ignore, blastSource) < 1f;
    }

    // 0 not cover, 0.5 light cover, 1 solid cover.
    static float Cover(Collider collider, Transform ignore, Transform blastSource)
    {
        if (collider == null || collider.isTrigger)
            return 0f;
        Transform t = collider.transform;
        if (ignore != null && t.IsChildOf(ignore))
            return 0f;
        if (blastSource != null && t.IsChildOf(blastSource))
            return 0f;
        // Base shields stop blasts both ways.
        if (collider.GetComponentInParent<Hd2SpawnField>() != null)
            return 1f;
        // Fighters, bodies and the things they hold are not cover.
        if (collider.GetComponentInParent<Hd2Health>() != null)
            return 0f;
        if (collider.GetComponentInParent<Hd2GrindRail>() != null)
            return 0f;
        if (collider.attachedRigidbody != null && !collider.attachedRigidbody.isKinematic)
            return 0f;
        if (IsSign(t) || collider.bounds.size.y <= LowCoverHeight)
            return 0.5f;
        return 1f;
    }

    static bool IsSign(Transform t)
    {
        for (int i = 0; i < 4 && t != null; i++, t = t.parent)
        {
            if (t.name.IndexOf("Sign", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }
}
