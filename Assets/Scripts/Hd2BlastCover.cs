using UnityEngine;

/// <summary>
/// Whether something stands between an explosion (rocket, shocks) and a fighter or a body:
/// a live drop shield, a base shield, or a wall. A fighter takes no blast damage when both
/// the line to their chest and the line to their head are covered, so ducking behind a
/// waist-high wall still leaves the head exposed. A couple of rays per fighter in range,
/// only when something explodes.
/// </summary>
public static class Hd2BlastCover
{
    static readonly RaycastHit[] hits = new RaycastHit[24];

    /// <summary>True when the fighter is behind cover from the blast at point.</summary>
    public static bool Sheltered(Vector3 point, Transform fighter, Transform blastSource = null)
    {
        if (fighter == null)
            return false;
        Vector3 chest = fighter.position + Vector3.up * 0.9f;
        Vector3 head = fighter.position + Vector3.up * 1.5f;
        if (Hd2DropShield.Blocks(point, chest) && Hd2DropShield.Blocks(point, head))
            return true;
        return Covered(point, chest, fighter, blastSource) && Covered(point, head, fighter, blastSource);
    }

    /// <summary>True when cover stands on the line from the blast to the target point.</summary>
    public static bool Covered(Vector3 point, Vector3 target, Transform ignore, Transform blastSource = null)
    {
        if (Hd2DropShield.Blocks(point, target))
            return true;

        Vector3 delta = target - point;
        float distance = delta.magnitude;
        if (distance < 0.1f)
            return false;
        Vector3 direction = delta / distance;
        // Start just off the impact point so the surface the rocket hit doesn't count,
        // unless the target is on its far side.
        Vector3 from = point + direction * 0.05f;
        bool previous = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true;
        int count = Physics.RaycastNonAlloc(from, direction, hits, distance - 0.05f, ~0, QueryTriggerInteraction.Ignore);
        Physics.queriesHitBackfaces = previous;
        for (int i = 0; i < count; i++)
        {
            if (IsCover(hits[i].collider, ignore, blastSource))
                return true;
        }

        return false;
    }

    static bool IsCover(Collider collider, Transform ignore, Transform blastSource)
    {
        if (collider == null || collider.isTrigger)
            return false;
        Transform t = collider.transform;
        if (ignore != null && t.IsChildOf(ignore))
            return false;
        if (blastSource != null && t.IsChildOf(blastSource))
            return false;
        // Base shields stop blasts both ways.
        if (collider.GetComponentInParent<Hd2SpawnField>() != null)
            return true;
        // Fighters, bodies and the things they hold are not cover.
        if (collider.GetComponentInParent<Hd2Health>() != null)
            return false;
        if (collider.GetComponentInParent<Hd2GrindRail>() != null)
            return false;
        if (collider.attachedRigidbody != null && !collider.attachedRigidbody.isKinematic)
            return false;
        return true;
    }
}
