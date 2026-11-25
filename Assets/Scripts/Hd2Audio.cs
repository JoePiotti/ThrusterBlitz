using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Volume channels. Master is the listener volume. Everything else plays through the
/// Hd2Mixer (Resources/Audio): announcer voices, music and voice chat are routed to their
/// group where they are created (Route); every other sound is routed to Game by a sweep that
/// runs twice a second, so new sound sources need no changes. Volumes come from Hd2Settings.
/// </summary>
public static class Hd2Audio
{
    public enum Channel
    {
        Game,
        Announcer,
        Music,
        Voice
    }

    static readonly string[] groupNames = { "Game", "Announcer", "Music", "Voice" };
    static AudioMixer mixer;
    static AudioMixerGroup[] groups;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        var runner = new GameObject("Hd2Audio");
        Object.DontDestroyOnLoad(runner);
        runner.hideFlags = HideFlags.HideInHierarchy;
        runner.AddComponent<Hd2AudioRunner>();
        Apply();
    }

    static bool Ready()
    {
        if (mixer != null)
            return true;
        mixer = Resources.Load<AudioMixer>("Audio/Hd2Mixer");
        if (mixer == null)
            return false;
        groups = new AudioMixerGroup[groupNames.Length];
        for (int i = 0; i < groupNames.Length; i++)
        {
            AudioMixerGroup[] found = mixer.FindMatchingGroups(groupNames[i]);
            groups[i] = found != null && found.Length > 0 ? found[0] : null;
        }

        return true;
    }

    public static AudioMixerGroup Group(Channel channel)
    {
        return Ready() ? groups[(int)channel] : null;
    }

    /// <summary>Plays this source through a channel (call where the source is created).</summary>
    public static void Route(AudioSource source, Channel channel)
    {
        if (source != null)
            source.outputAudioMixerGroup = Group(channel);
    }

    /// <summary>Pushes the saved volumes to the listener and the mixer.</summary>
    public static void Apply()
    {
        AudioListener.volume = Hd2Settings.Get(Hd2Settings.Volume.Master);
        if (!Ready())
            return;
        mixer.SetFloat("GameVolume", Decibels(Hd2Settings.Get(Hd2Settings.Volume.Game)));
        mixer.SetFloat("AnnouncerVolume", Decibels(Hd2Settings.Get(Hd2Settings.Volume.Announcer)));
        mixer.SetFloat("MusicVolume", Decibels(Hd2Settings.Get(Hd2Settings.Volume.Music)));
        mixer.SetFloat("VoiceVolume", Decibels(Hd2Settings.Get(Hd2Settings.Volume.Voice)));
    }

    static float Decibels(float level)
    {
        return level <= 0.0001f ? -80f : Mathf.Log10(level) * 20f;
    }

    /// <summary>Sends every unrouted source to Game.</summary>
    internal static void Sweep()
    {
        AudioMixerGroup game = Group(Channel.Game);
        if (game == null)
            return;
        AudioSource[] sources = Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < sources.Length; i++)
        {
            if (sources[i] != null && sources[i].outputAudioMixerGroup == null)
                sources[i].outputAudioMixerGroup = game;
        }
    }
}
