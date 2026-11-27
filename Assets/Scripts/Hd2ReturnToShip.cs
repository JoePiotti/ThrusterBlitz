using UnityEngine;

/// <summary>
/// After any match ends, waits while the end-of-match summary is on screen, then leaves the
/// match and returns to the spaceship cabin (Title) so a new game can be picked.
/// Hd2MatchStats starts it when the end-of-match bonus is paid.
/// </summary>
public class Hd2ReturnToShip : MonoBehaviour
{
    /// <summary>Matches the HUD's end-of-match summary time.</summary>
    public const float DelaySeconds = 10f;

    float leaveAt;

    public static void Begin()
    {
        if (FindFirstObjectByType<Hd2ReturnToShip>() != null)
            return;
        var go = new GameObject("ReturnToShip");
        go.AddComponent<Hd2ReturnToShip>().leaveAt = Time.unscaledTime + DelaySeconds;
    }

    void Update()
    {
        if (Time.unscaledTime < leaveAt)
            return;
        enabled = false;
        // Stops the match, shuts down the network session and loads Title.
        Hd2Locomotion.EndMatch();
    }
}
