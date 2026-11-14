using UnityEngine;

/// <summary>
/// Local ears inside the drop-shield bubble hear a short room and a slight mute.
/// One reverb zone sits on the shield-sphere center and stays on only while this
/// component is enabled. Disable or destroy it when the shield fails, times out,
/// or the object goes away.
/// </summary>
public class Hd2DropShieldInterior : MonoBehaviour
{
    public const float DefaultRadius = 3f;

    // Wet master, in millibels. -350 mB is -3.5 dB.
    public const int RoomLevel = -350;

    const float Shell = 0.15f;

    // Same -3.5 dB cut on the listener mix. RoomLevel is the wet master.
    const float MuteDb = 3.5f;
    const int Capacity = 16;

    static readonly Hd2DropShieldInterior[] active = new Hd2DropShieldInterior[Capacity];
    static int activeCount;
    static AudioListener ears;
    static AudioListener held;
    static float baseVolume = 1f;
    static bool ducked;
    static float muteGain = -1f;

    AudioReverbZone zone;
    float radius = DefaultRadius;

    /// <summary>
    /// Turns the interior on at the shield sphere. The transform is the sphere center.
    /// </summary>
    public static Hd2DropShieldInterior Open(Transform sphereCenter, float radius)
    {
        if (sphereCenter == null)
            return null;

        Hd2DropShieldInterior interior = sphereCenter.GetComponent<Hd2DropShieldInterior>();
        if (interior == null)
            interior = sphereCenter.gameObject.AddComponent<Hd2DropShieldInterior>();
        interior.SetRadius(radius);
        if (!interior.enabled)
            interior.enabled = true;
        else
            interior.Arm();
        return interior;
    }

    public void Close()
    {
        if (this == null)
            return;
        if (zone != null)
            zone.enabled = false;
        if (enabled)
            enabled = false;
        else
        {
            Unregister();
            Apply();
        }
    }

    public void SetRadius(float value)
    {
        radius = value > 0.05f ? value : DefaultRadius;
        if (zone == null)
            return;
        zone.minDistance = radius;
        zone.maxDistance = radius + Shell;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Restore();
        for (int i = 0; i < active.Length; i++)
            active[i] = null;
        activeCount = 0;
        ears = null;
        baseVolume = 1f;
        muteGain = -1f;
    }

    void Awake()
    {
        EnsureZone();
        Tune();
        if (zone != null)
            zone.enabled = false;
    }

    void OnEnable()
    {
        EnsureZone();
        if (zone == null || !Application.isPlaying)
        {
            if (zone != null)
                zone.enabled = false;
            return;
        }

        Arm();
        Register();
        Apply();
    }

    void OnDisable()
    {
        if (zone != null)
            zone.enabled = false;
        Unregister();
        Apply();
    }

    void OnDestroy()
    {
        if (zone != null)
            zone.enabled = false;
        Unregister();
        Apply();
    }

    void LateUpdate()
    {
        Apply();
    }

    void Arm()
    {
        if (!Application.isPlaying)
            return;
        EnsureZone();
        Tune();
        if (zone != null)
            zone.enabled = true;
    }

    void EnsureZone()
    {
        if (zone != null)
            return;

        // The bubble transform is scaled up to the shield radius. The zone lives on a
        // unit-scale child so its distances stay in meters at that same center.
        Transform host = transform.Find("InteriorReverb");
        if (host == null)
        {
            var go = new GameObject("InteriorReverb");
            go.transform.SetParent(transform, false);
            host = go.transform;
        }

        host.localPosition = Vector3.zero;
        host.localRotation = Quaternion.identity;
        Flatten(host);
        zone = host.GetComponent<AudioReverbZone>();
        if (zone == null)
            zone = host.gameObject.AddComponent<AudioReverbZone>();
        zone.enabled = false;
    }

    static void Flatten(Transform host)
    {
        host.localScale = Vector3.one;
        Vector3 lossy = host.lossyScale;
        host.localScale = new Vector3(Inv(lossy.x), Inv(lossy.y), Inv(lossy.z));
    }

    static float Inv(float scale)
    {
        float abs = scale < 0f ? -scale : scale;
        if (abs < 0.0001f)
            return 1f;
        return 1f / scale;
    }

    void Tune()
    {
        EnsureZone();
        if (zone == null)
            return;
        Flatten(zone.transform);
        if (radius < 0.05f)
            radius = DefaultRadius;

        // Short bubble: a close-shell reflection and a quiet tail, not a long hall.
        zone.reverbPreset = AudioReverbPreset.Room;
        zone.room = RoomLevel;
        zone.roomHF = -500;
        zone.roomLF = 0;
        zone.decayTime = 0.4f;
        zone.decayHFRatio = 0.75f;
        zone.reflections = -1450;
        zone.reflectionsDelay = 0.012f;
        zone.reverb = -1650;
        zone.reverbDelay = 0.015f;
        zone.HFReference = 5000f;
        zone.LFReference = 250f;
        zone.diffusion = 60f;
        zone.density = 50f;
        zone.minDistance = radius;
        zone.maxDistance = radius + Shell;
        zone.reverbPreset = AudioReverbPreset.User;
    }

    void Register()
    {
        for (int i = 0; i < activeCount; i++)
        {
            if (active[i] == this)
                return;
        }

        if (activeCount >= active.Length)
            return;
        active[activeCount++] = this;
    }

    void Unregister()
    {
        for (int i = 0; i < activeCount; i++)
        {
            if (active[i] != this)
                continue;
            activeCount--;
            active[i] = active[activeCount];
            active[activeCount] = null;
            return;
        }
    }

    float Weight(Vector3 earPoint)
    {
        float min = zone != null ? zone.minDistance : radius;
        float max = zone != null ? zone.maxDistance : radius + Shell;
        if (max <= min)
            max = min + Shell;

        Vector3 center = zone != null ? zone.transform.position : transform.position;
        float reach = max * max;
        float distSqr = (earPoint - center).sqrMagnitude;
        if (distSqr >= reach)
            return 0f;
        float inner = min * min;
        if (distSqr <= inner)
            return 1f;
        float dist = Mathf.Sqrt(distSqr);
        return (max - dist) / (max - min);
    }

    static void Apply()
    {
        if (!Application.isPlaying)
        {
            Restore();
            return;
        }

        if (activeCount == 0)
        {
            Restore();
            return;
        }

        AudioListener listener = Ears();
        if (listener == null)
            return;

        Vector3 at = listener.transform.position;
        float weight = 0f;
        for (int i = 0; i < activeCount; i++)
        {
            Hd2DropShieldInterior interior = active[i];
            if (interior == null)
                continue;
            float sample = interior.Weight(at);
            if (sample > weight)
                weight = sample;
        }

        if (weight <= 0f)
        {
            Restore();
            return;
        }

        if (!ducked || held != listener)
        {
            if (ducked && held != null && held != listener)
                AudioListener.volume = baseVolume;
            baseVolume = AudioListener.volume;
            held = listener;
            ducked = true;
        }

        AudioListener.volume = baseVolume * Mathf.Lerp(1f, Gain(), weight);
    }

    static void Restore()
    {
        if (ducked && held != null)
            AudioListener.volume = baseVolume;
        ducked = false;
        held = null;
    }

    static float Gain()
    {
        if (muteGain < 0f)
            muteGain = Mathf.Pow(10f, -MuteDb / 20f);
        return muteGain;
    }

    static AudioListener Ears()
    {
        if (ears != null && ears.isActiveAndEnabled)
            return ears;

        Camera view = Camera.main;
        if (view == null)
        {
            ears = null;
            return null;
        }

        AudioListener listener = view.GetComponent<AudioListener>();
        if (listener != null && listener.isActiveAndEnabled)
        {
            ears = listener;
            return ears;
        }

        ears = null;
        return null;
    }
}
