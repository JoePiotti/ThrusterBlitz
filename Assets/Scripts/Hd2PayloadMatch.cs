using UnityEngine;

/// <summary>
/// Payload opening. Same countdown and base lock as control point.
/// Shields stay shut until the countdown ends, and the cart does not roll before that.
/// </summary>
public class Hd2PayloadMatch : MonoBehaviour
{
    AudioSource matchVoice;
    AudioClip matchBegins;
    bool gatesOpen;
    float gatesOpenAt;

    void Awake()
    {
        matchVoice = gameObject.AddComponent<AudioSource>();
        matchVoice.playOnAwake = false;
        matchVoice.loop = false;
        matchVoice.spatialBlend = 0f;
        matchVoice.dopplerLevel = 0f;
        matchBegins = Resources.Load<AudioClip>("Audio/MatchBegins");
    }

    void Start()
    {
        Hd2SpawnField.SetLocked(true);
        gatesOpen = false;
        gatesOpenAt = Time.time + Hd2ControlPoint.GateSeconds;
        if (matchVoice != null && matchBegins != null)
        {
            matchVoice.Stop();
            matchVoice.clip = matchBegins;
            matchVoice.Play();
        }

        var players = FindObjectsByType<Hd2Locomotion>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] != null)
                players[i].ReturnToBase();
        }

        var bots = FindObjectsByType<Hd2EnemyBot>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < bots.Length; i++)
        {
            if (bots[i] != null)
                bots[i].ReturnToBase();
        }
    }

    void LateUpdate()
    {
        if (gatesOpen || Time.time < gatesOpenAt)
            return;

        gatesOpen = true;
        Hd2SpawnField.SetLocked(false);
    }
}
