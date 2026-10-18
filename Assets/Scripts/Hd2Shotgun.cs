using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

/// <summary>
/// One-handed sawed-off. Each trigger pull fires one barrel as eight pellets.
/// Two shots, then a reload. A pellet stops at maxRangeMeters.
/// Close pellets deal closeBodyDamage, or closeHeadDamage on a head.
/// Past closeRangeMeters the damage is multiplied by farDamageMultiplier.
/// </summary>
public class Hd2Shotgun : MonoBehaviour
{
    public Hd2Pistol.Hand hand = Hd2Pistol.Hand.Right;

    public int rounds = 2;
    public int pelletsPerShot = 8;
    public float closeRangeMeters = 3f;
    public float closeBodyDamage = 8f;
    public float closeHeadDamage = 12f;
    public float farDamageMultiplier = 0.5f;
    public float maxRangeMeters = 10f;
    public float reloadSeconds = 1.67f;
    [Tooltip("Full width of the pellet cone, in degrees.")]
    public float spreadAngle = 16f;
    public float pelletSpeed = 90f;
    public float kickDegrees = 8f;
    public float kickReturnSpeed = 16f;
    public Color pelletColor = new Color(1f, 0.82f, 0.45f, 1f);

    public Transform muzzle;

    public const int PatternCount = 8;
    const int PatternPellets = 8;

    /// <summary>
    /// Raised with the pattern index byte when this gun fires. That byte is the shotgun fire message.
    /// Receivers call ApplyPattern with the same byte. The patterns below are baked offsets, identical on every client.
    /// </summary>
    public static event System.Action<Hd2Shotgun, byte> PatternFired;

    // Unit-disk offsets, eight pellets per pattern. Scaled by the spread cone when the shot is simulated.
    static readonly Vector2[] Patterns =
    {
        new Vector2(0.08f, 0.14f), new Vector2(-0.22f, 0.31f), new Vector2(0.41f, -0.11f), new Vector2(-0.06f, -0.37f),
        new Vector2(0.33f, 0.26f), new Vector2(-0.46f, -0.09f), new Vector2(0.17f, -0.51f), new Vector2(-0.28f, 0.43f),
        new Vector2(-0.11f, 0.07f), new Vector2(0.29f, 0.18f), new Vector2(-0.38f, -0.24f), new Vector2(0.14f, -0.44f),
        new Vector2(0.52f, 0.06f), new Vector2(-0.19f, 0.48f), new Vector2(0.07f, 0.33f), new Vector2(-0.41f, -0.36f),
        new Vector2(0.21f, -0.08f), new Vector2(-0.33f, 0.12f), new Vector2(0.05f, 0.46f), new Vector2(0.44f, 0.29f),
        new Vector2(-0.15f, -0.49f), new Vector2(0.36f, -0.38f), new Vector2(-0.52f, 0.22f), new Vector2(0.12f, 0.19f),
        new Vector2(-0.04f, -0.16f), new Vector2(0.27f, -0.29f), new Vector2(-0.43f, 0.08f), new Vector2(0.16f, 0.51f),
        new Vector2(-0.31f, -0.41f), new Vector2(0.48f, -0.14f), new Vector2(-0.09f, 0.36f), new Vector2(0.23f, 0.09f),
        new Vector2(0.35f, 0.13f), new Vector2(-0.26f, -0.18f), new Vector2(0.02f, -0.42f), new Vector2(-0.49f, 0.27f),
        new Vector2(0.18f, 0.39f), new Vector2(-0.07f, 0.22f), new Vector2(0.41f, -0.33f), new Vector2(-0.37f, -0.46f),
        new Vector2(-0.18f, 0.44f), new Vector2(0.46f, 0.17f), new Vector2(-0.29f, -0.07f), new Vector2(0.09f, -0.28f),
        new Vector2(0.31f, -0.47f), new Vector2(-0.44f, -0.31f), new Vector2(0.13f, 0.11f), new Vector2(-0.02f, 0.53f),
        new Vector2(0.24f, 0.34f), new Vector2(-0.36f, 0.19f), new Vector2(0.51f, -0.21f), new Vector2(-0.14f, -0.33f),
        new Vector2(0.06f, -0.12f), new Vector2(-0.47f, 0.38f), new Vector2(0.19f, 0.27f), new Vector2(0.33f, -0.05f),
        new Vector2(-0.21f, -0.52f), new Vector2(0.39f, 0.41f), new Vector2(-0.08f, 0.16f), new Vector2(0.15f, -0.36f),
        new Vector2(0.11f, 0.48f), new Vector2(-0.39f, -0.14f), new Vector2(0.28f, 0.04f), new Vector2(-0.16f, 0.29f)
    };

    int loaded;
    int patternCursor;
    bool reloading;
    float reloadEndsAt;
    bool waitForTriggerRelease = true;
    float kick;
    Quaternion restLocalRotation;
    InputAction triggerAction;
    InputAction reloadAction;
    InputAction editorFireAction;
    InputAction editorReloadAction;
    AudioSource voice;
    AudioClip shotClip;
    AudioClip reloadClip;
    Hd2ControlMap controlMap;

    static readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();
    static readonly List<UnityEngine.XR.InputDevice> hapticDevices = new List<UnityEngine.XR.InputDevice>();

    void Awake()
    {
        if (muzzle == null)
            muzzle = FindChild(transform, "Barrel");
        if (muzzle == null)
            muzzle = FindChild(transform, "Muzzle");

        restLocalRotation = transform.localRotation;
        loaded = Mathf.Max(1, rounds);
        shotClip = Resources.Load<AudioClip>("Audio/ShotgunShot");
        reloadClip = Resources.Load<AudioClip>("Audio/ShotgunReload");
        if (shotClip != null || reloadClip != null)
            voice = AddVoice();
    }

    public void SetHand(Hd2Pistol.Hand value)
    {
        hand = value;
        if (isActiveAndEnabled)
            BindInputs();
    }

    void OnEnable()
    {
        BindInputs();
    }

    void BindInputs()
    {
        Dispose(ref triggerAction);
        Dispose(ref reloadAction);
        Dispose(ref editorFireAction);
        Dispose(ref editorReloadAction);

        string node = hand == Hd2Pistol.Hand.Left ? "LeftHand" : "RightHand";
        triggerAction = Button("Hd2ShotgunFire" + node, "<XRController>{" + node + "}/triggerPressed");
        reloadAction = Button("Hd2ShotgunReload" + node, "<XRController>{" + node + "}/gripPressed");
        if (hand == Hd2Pistol.Hand.Right)
        {
            editorFireAction = Button("Hd2ShotgunFireEditor", "<Mouse>/rightButton");
            editorReloadAction = Button("Hd2ShotgunReloadEditor", "<Keyboard>/r");
        }
    }

    static InputAction Button(string name, string binding)
    {
        var action = new InputAction(name, InputActionType.Button);
        action.AddBinding(binding);
        action.Enable();
        return action;
    }

    void OnDisable()
    {
        Dispose(ref triggerAction);
        Dispose(ref reloadAction);
        Dispose(ref editorFireAction);
        Dispose(ref editorReloadAction);
    }

    void Update()
    {
        if (reloading && Time.time >= reloadEndsAt)
            FinishReload();

        bool editor = EditorFallback;
        var fire = hand == Hd2Pistol.Hand.Left ? Hd2ControlMap.Action.FireLeft : Hd2ControlMap.Action.FireRight;
        var reload = hand == Hd2Pistol.Hand.Left ? Hd2ControlMap.Action.ReloadLeft : Hd2ControlMap.Action.ReloadRight;
        if (Pressed(reload, reloadAction) || (editor && editorReloadAction != null && editorReloadAction.WasPressedThisFrame()))
            Reload();

        bool menuOpen = Map() != null && Map().MenuOpen;
        bool pressed = !menuOpen && Pressed(fire, triggerAction);
        if (editor && editorFireAction != null)
            pressed = pressed || editorFireAction.WasPressedThisFrame();

        if (waitForTriggerRelease)
        {
            pressed = false;
            if (!TriggerIsDown(editor))
                waitForTriggerRelease = false;
        }

        if (pressed)
            TryFire();

        if (kick > 0f)
        {
            kick = Mathf.MoveTowards(kick, 0f, kickReturnSpeed * Time.deltaTime);
            transform.localRotation = restLocalRotation * Quaternion.Euler(-kick, 0f, 0f);
        }
    }

    void TryFire()
    {
        if (reloading || loaded <= 0)
            return;

        Transform origin = muzzle != null ? muzzle : transform;

        byte pattern = (byte)patternCursor;
        patternCursor = (patternCursor + 1) % PatternCount;
        PatternFired?.Invoke(this, pattern);
        ApplyPattern(pattern);

        SpawnFlash(origin);
        if (voice != null && shotClip != null)
            voice.PlayOneShot(shotClip);
        Pulse(0.85f, 0.07f);
        kick = kickDegrees;
        loaded--;
        if (loaded <= 0)
            Reload();
    }

    /// <summary>
    /// Spawns this gun's pellets for a pattern index. The index is the only value a fire message needs.
    /// </summary>
    public void ApplyPattern(byte patternIndex)
    {
        Transform origin = muzzle != null ? muzzle : transform;
        Vector3 forward = origin.forward;
        if (forward.sqrMagnitude < 0.0001f)
            forward = -transform.up;
        forward.Normalize();

        Vector3 right = Vector3.Cross(forward, Vector3.up);
        if (right.sqrMagnitude < 0.0001f)
            right = transform.right;
        right.Normalize();
        Vector3 up = Vector3.Cross(right, forward).normalized;
        float barrel = (patternIndex & 1) == 0 ? -1f : 1f;
        Vector3 from = origin.position + right * (0.018f * barrel);

        int pattern = patternIndex % PatternCount;
        float radius = Mathf.Tan(Mathf.Max(0f, spreadAngle) * 0.5f * Mathf.Deg2Rad);
        int count = Mathf.Clamp(pelletsPerShot, 1, PatternPellets);
        int start = pattern * PatternPellets;
        for (int i = 0; i < count; i++)
        {
            Vector2 disk = Patterns[start + i] * radius;
            Vector3 direction = (forward + right * disk.x + up * disk.y).normalized;
            SpawnPellet(from, direction);
        }
    }

    void SpawnPellet(Vector3 from, Vector3 direction)
    {
        var projectile = Hd2Shot.Rent();
        projectile.gameObject.name = "Pellet";
        projectile.transform.SetParent(null, true);
        projectile.transform.position = from;
        projectile.transform.rotation = Quaternion.LookRotation(direction);

        float speed = Mathf.Max(1f, pelletSpeed);
        float range = Mathf.Max(0.5f, maxRangeMeters);
        projectile.velocity = direction * speed;
        projectile.lifetime = range / speed + 0.05f;
        projectile.owner = transform.root;
        projectile.damage = closeBodyDamage;
        projectile.headMultiplier = closeBodyDamage > 0.01f ? closeHeadDamage / closeBodyDamage : 1f;
        projectile.gravel = true;
        projectile.maxTravel = range;
        projectile.rangeFalloffStart = Mathf.Max(0f, closeRangeMeters);
        projectile.rangeFalloffMultiplier = farDamageMultiplier;
        projectile.maxBoltLength = 0.7f;
        projectile.UsePlasma(pelletColor, 0.02f);
        projectile.BeginAt(from);
    }

    void SpawnFlash(Transform barrel)
    {
        if (barrel == null)
            return;

        Vector3 parentScale = barrel.lossyScale;
        float ScaleOf(float axis, float worldSize)
        {
            return Mathf.Abs(axis) > 0.001f ? worldSize / axis : worldSize;
        }

        Hd2MuzzleFlash.Show(
            barrel,
            new Vector3(0f, 0f, ScaleOf(parentScale.z, 0.04f)),
            new Vector3(ScaleOf(parentScale.x, 0.08f), ScaleOf(parentScale.y, 0.08f), ScaleOf(parentScale.z, 0.16f)),
            pelletColor,
            0.08f);
    }

    public void BotTap()
    {
        waitForTriggerRelease = false;
        TryFire();
    }

    public void Reload()
    {
        if (reloading || loaded >= rounds)
            return;

        reloading = true;
        reloadEndsAt = Time.time + Mathf.Max(0f, reloadSeconds);
        if (voice != null && reloadClip != null)
            voice.PlayOneShot(reloadClip);
    }

    void FinishReload()
    {
        reloading = false;
        loaded = Mathf.Max(1, rounds);
    }

    AudioSource AddVoice()
    {
        var source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 1f;
        source.minDistance = 0.4f;
        source.maxDistance = 18f;
        return source;
    }

    void Pulse(float amplitude, float duration)
    {
        if (amplitude <= 0f || duration <= 0f || GetComponentInParent<Hd2EnemyBot>() != null)
            return;

        var node = hand == Hd2Pistol.Hand.Left ? XRNode.LeftHand : XRNode.RightHand;
        InputDevices.GetDevicesAtXRNode(node, hapticDevices);
        for (int i = 0; i < hapticDevices.Count; i++)
        {
            UnityEngine.XR.InputDevice device = hapticDevices[i];
            if (device.TryGetHapticCapabilities(out HapticCapabilities caps) && caps.supportsImpulse)
                device.SendHapticImpulse(0, Mathf.Clamp01(amplitude), duration);
        }
    }

    bool TriggerIsDown(bool editor)
    {
        var fire = hand == Hd2Pistol.Hand.Left ? Hd2ControlMap.Action.FireLeft : Hd2ControlMap.Action.FireRight;
        Hd2ControlMap controls = Map();
        if (controls != null && controls.Held(fire))
            return true;
        if (editor && editorFireAction != null && editorFireAction.IsPressed())
            return true;
        return controls == null && triggerAction != null && triggerAction.IsPressed();
    }

    bool Pressed(Hd2ControlMap.Action action, InputAction fallback)
    {
        Hd2ControlMap controls = Map();
        if (controls != null)
            return controls.Pressed(action);
        return fallback != null && fallback.WasPressedThisFrame();
    }

    Hd2ControlMap Map()
    {
        if (controlMap == null)
            controlMap = GetComponentInParent<Hd2ControlMap>();
        return controlMap;
    }

    static bool EditorFallback
    {
        get
        {
            if (!Application.isEditor)
                return false;
            displays.Clear();
            SubsystemManager.GetSubsystems(displays);
            for (int i = 0; i < displays.Count; i++)
            {
                if (displays[i].running)
                    return false;
            }

            return true;
        }
    }

    static Transform FindChild(Transform root, string childName)
    {
        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            if (transforms[i].name == childName)
                return transforms[i];
        }

        return null;
    }

    static void Dispose(ref InputAction action)
    {
        if (action == null)
            return;
        action.Disable();
        action.Dispose();
        action = null;
    }
}
