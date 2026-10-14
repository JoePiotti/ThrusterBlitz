using UnityEngine;

/// <summary>
/// The chime that plays when the player takes a health or blitz pad.
/// </summary>
public static class Hd2PickupChime
{
    static AudioClip clip;
    static AudioSource source;

    public static void Play()
    {
        if (clip == null)
            clip = Resources.Load<AudioClip>("Audio/PickupChime");
        if (clip == null)
            return;

        if (source == null)
        {
            var speaker = new GameObject("PickupChime");
            speaker.hideFlags = HideFlags.DontSave;
            Object.DontDestroyOnLoad(speaker);
            source = speaker.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.dopplerLevel = 0f;
        }

        source.PlayOneShot(clip);
    }
}
