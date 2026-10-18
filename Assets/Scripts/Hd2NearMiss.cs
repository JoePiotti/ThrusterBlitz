using UnityEngine;

/// <summary>
/// Plays when a projectile passes the headset without hitting the player.
/// The zone is a sphere around the head, wider than the body capsule, and it never deals damage.
/// </summary>
public static class Hd2NearMiss
{
    const float radius = 0.5f;
    const float cooldown = 0.12f;

    static Hd2Locomotion player;
    static AudioClip clip;
    static AudioSource source;
    static float nextPlay;

    public static bool HitsPlayer(Component thing)
    {
        if (thing == null || !Ready())
            return false;

        Transform hit = thing.transform;
        Transform body = player.transform;
        return hit == body || hit.IsChildOf(body) || body.IsChildOf(hit);
    }

    /// <summary>
    /// True when this pass should not be tested again: it was the player's own shot,
    /// it hit the player, or it crossed the head sphere.
    /// </summary>
    public static bool TryPass(Vector3 from, Vector3 to, Transform owner, bool struckPlayer)
    {
        if (struckPlayer || !Ready())
            return struckPlayer;

        if (owner != null && (owner == player.transform || owner.IsChildOf(player.transform)))
            return true;

        Vector3 head = player.head != null ? player.head.position : player.transform.position;
        if (!Crosses(from, to, head, out Vector3 closest))
            return false;

        if (Time.time >= nextPlay)
            Play(closest, head);
        return true;
    }

    static bool Ready()
    {
        if (player != null)
            return true;

        player = Object.FindFirstObjectByType<Hd2Locomotion>();
        return player != null;
    }

    static bool Crosses(Vector3 from, Vector3 to, Vector3 head, out Vector3 closest)
    {
        Vector3 segment = to - from;
        float lengthSqr = segment.sqrMagnitude;
        float t = 0f;
        if (lengthSqr > 0.0000001f)
            t = Mathf.Clamp01(Vector3.Dot(head - from, segment) / lengthSqr);
        closest = from + segment * t;
        return (closest - head).sqrMagnitude <= radius * radius;
    }

    static void Play(Vector3 closest, Vector3 head)
    {
        if (clip == null)
            clip = Resources.Load<AudioClip>("Audio/NearMiss");
        if (clip == null)
            return;

        if (source == null)
        {
            var speaker = new GameObject("NearMiss");
            speaker.hideFlags = HideFlags.DontSave;
            Object.DontDestroyOnLoad(speaker);
            source = speaker.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.dopplerLevel = 0f;
            source.minDistance = 0.4f;
            source.maxDistance = 8f;
        }

        Vector3 beside = closest - head;
        if (beside.sqrMagnitude < 0.01f)
            beside = Vector3.right;
        source.transform.position = head + beside.normalized * Mathf.Max(beside.magnitude, 0.2f);
        source.PlayOneShot(clip);
        nextPlay = Time.time + cooldown;
    }
}
