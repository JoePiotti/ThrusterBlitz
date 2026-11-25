using UnityEngine;

/// <summary>
/// Player options saved on the headset: the five volume sliders and turning.
/// Controls, microphone mode and weapon (grip) angle live in Hd2ControlMap; the HUD side in
/// Hd2Hud; name and clan tag in Hd2Identity.
/// </summary>
public static class Hd2Settings
{
    public enum Volume
    {
        Master,
        Game,
        Announcer,
        Music,
        Voice
    }

    public const int VolumeCount = 5;
    public const float SnapAngleMin = 15f;
    public const float SnapAngleMax = 90f;
    /// <summary>Degrees per second at full stick when smooth turning is on.</summary>
    public const float SmoothTurnSpeed = 120f;

    static readonly string[] volumeKeys = { "Hd2VolMaster", "Hd2VolGame", "Hd2VolAnnouncer", "Hd2VolMusic", "Hd2VolVoice" };
    static readonly float[] volumeDefaults = { 1f, 1f, 1f, 0.8f, 1f };
    static readonly string[] volumeLabels = { "Master", "Game", "Announcer", "Music", "Voice Chat" };
    const string SmoothTurnKey = "Hd2SmoothTurn";
    const string SnapAngleKey = "Hd2SnapAngle";

    static float[] volumes;
    static bool loaded;
    static bool smoothTurn;
    static float snapAngle = 45f;

    /// <summary>Raised after any setting here changes.</summary>
    public static event System.Action Changed;

    public static string Label(Volume which)
    {
        return volumeLabels[(int)which];
    }

    public static float Get(Volume which)
    {
        Load();
        return volumes[(int)which];
    }

    public static void Set(Volume which, float value)
    {
        Load();
        value = Mathf.Clamp01(Mathf.Round(value * 100f) / 100f);
        if (Mathf.Approximately(volumes[(int)which], value))
            return;
        volumes[(int)which] = value;
        PlayerPrefs.SetFloat(volumeKeys[(int)which], value);
        PlayerPrefs.Save();
        Hd2Audio.Apply();
        Changed?.Invoke();
    }

    public static bool SmoothTurn
    {
        get
        {
            Load();
            return smoothTurn;
        }
        set
        {
            Load();
            if (smoothTurn == value)
                return;
            smoothTurn = value;
            PlayerPrefs.SetInt(SmoothTurnKey, value ? 1 : 0);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }
    }

    /// <summary>Degrees per snap turn, 15 to 90 in steps of 5.</summary>
    public static float SnapAngle
    {
        get
        {
            Load();
            return snapAngle;
        }
        set
        {
            Load();
            float clamped = Mathf.Clamp(Mathf.Round(value / 5f) * 5f, SnapAngleMin, SnapAngleMax);
            if (Mathf.Approximately(snapAngle, clamped))
                return;
            snapAngle = clamped;
            PlayerPrefs.SetFloat(SnapAngleKey, clamped);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }
    }

    static void Load()
    {
        if (loaded)
            return;
        loaded = true;
        volumes = new float[VolumeCount];
        for (int i = 0; i < VolumeCount; i++)
            volumes[i] = Mathf.Clamp01(PlayerPrefs.GetFloat(volumeKeys[i], volumeDefaults[i]));
        smoothTurn = PlayerPrefs.GetInt(SmoothTurnKey, 0) == 1;
        snapAngle = Mathf.Clamp(PlayerPrefs.GetFloat(SnapAngleKey, 45f), SnapAngleMin, SnapAngleMax);
    }
}
