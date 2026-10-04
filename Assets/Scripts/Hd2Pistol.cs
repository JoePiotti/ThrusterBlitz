using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

/// <summary>
/// One pistol on one hand. A quick trigger release fires a normal shot.
/// Holding the trigger for chargeHoldTime, then releasing, fires a charged shot.
/// Grip starts a timed reload on that gun only. An empty magazine starts the same reload.
/// </summary>
public class Hd2Pistol : MonoBehaviour
{
    public enum Hand
    {
        Right = 0,
        Left = 1
    }

    [Header("Hand")]
    public Hand hand = Hand.Right;

    [Header("Magazine")]
    public int magazineSize = 16;
    public float shotsPerSecond = 8f;
    public float reloadDuration = 2f;

    [Header("Shot")]
    public float shotSpeed = 100f;
    public float shotLifetime = 2f;
    public float shotRadius = 0.04f;
    public float normalDamage = 10f;
    public Color shotColor = new Color(0.55f, 0.88f, 1f, 1f);

    [Header("Charged shot")]
    public float chargeHoldTime = 1f;
    public float chargedRadius = 0.06f;
    public float chargedSpeedMultiplier = 0.7f;
    public int chargedAmmoCost = 3;
    public int chargedBounces = 6;

    [Header("Kick")]
    public float kickDegrees = 4f;
    public float kickReturnSpeed = 18f;

    [Header("Haptics")]
    [Tooltip("Strength of the tick on each shot, from 0 to 1.")]
    public float shotHapticAmplitude = 0.7f;
    [Tooltip("Length of the tick on each shot, in seconds.")]
    public float shotHapticDuration = 0.04f;
    [Tooltip("Strength of the pulse when a charged shot becomes ready, from 0 to 1.")]
    public float chargeReadyHapticAmplitude = 0.5f;
    [Tooltip("Length of the pulse when a charged shot becomes ready, in seconds.")]
    public float chargeReadyHapticDuration = 0.16f;

    [Tooltip("Bore tip. Local euler (90, 0, 0) so forward is the pistol's -Y, out the barrel away from the player.")]
    public Transform muzzle;

    int rounds;
    float nextFireTime;
    float triggerPressedAt = -1f;
    bool chargeReadyPulsed;
    bool reloading;
    int reloadPulsesLeft;
    float nextReloadPulseAt;
    float reloadEndsAt;
    float kick;
    Quaternion restLocalRotation;

    InputAction triggerAction;
    InputAction reloadAction;
    InputAction editorFireAction;
    InputAction editorReloadAction;
    AudioSource shotSource;
    AudioClip shotClip;
    AudioClip chargedShotClip;

    static readonly System.Collections.Generic.List<XRDisplaySubsystem> displays =
        new System.Collections.Generic.List<XRDisplaySubsystem>();
    static readonly System.Collections.Generic.List<UnityEngine.XR.InputDevice> hapticDevices =
        new System.Collections.Generic.List<UnityEngine.XR.InputDevice>();

    public int Rounds => rounds;
    public bool IsReloading => reloading;

    void Awake()
    {
        if (muzzle == null)
        {
            var found = transform.Find("Muzzle");
            if (found != null)
                muzzle = found;
        }

        restLocalRotation = transform.localRotation;
        rounds = magazineSize;

        shotClip = Resources.Load<AudioClip>("Audio/PistolShot");
        chargedShotClip = Resources.Load<AudioClip>("Audio/ChargedPistolShot");
        if (shotClip == null && chargedShotClip == null)
            return;

        shotSource = gameObject.AddComponent<AudioSource>();
        shotSource.playOnAwake = false;
        shotSource.spatialBlend = 1f;
        shotSource.minDistance = 0.4f;
        shotSource.maxDistance = 12f;
    }

    void OnEnable()
    {
        string node = hand == Hand.Left ? "LeftHand" : "RightHand";

        triggerAction = new InputAction("Hd2Fire", InputActionType.Button);
        triggerAction.AddBinding("<XRController>{" + node + "}/triggerPressed");
        triggerAction.Enable();

        reloadAction = new InputAction("Hd2Reload", InputActionType.Button);
        reloadAction.AddBinding("<XRController>{" + node + "}/gripPressed");
        reloadAction.Enable();

        // Editor mouse/keyboard stays on the right gun so the two magazines stay separate.
        if (hand == Hand.Right)
        {
            editorFireAction = new InputAction("Hd2FireEditor", InputActionType.Button);
            editorFireAction.AddBinding("<Mouse>/rightButton");
            editorFireAction.Enable();

            editorReloadAction = new InputAction("Hd2ReloadEditor", InputActionType.Button);
            editorReloadAction.AddBinding("<Keyboard>/r");
            editorReloadAction.Enable();
        }
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
        // No ammo HUD. While reloading, this gun simply will not fire.
        if (reloading && Time.time >= reloadEndsAt)
            FinishReload();

        if (reloadPulsesLeft > 0 && Time.time >= nextReloadPulseAt)
        {
            reloadPulsesLeft--;
            Pulse(shotHapticAmplitude, shotHapticDuration);
            nextReloadPulseAt = Time.time + 0.09f;
        }

        bool editorReload = EditorFallback && editorReloadAction != null && editorReloadAction.WasPressedThisFrame();
        if (reloadAction.WasPressedThisFrame() || editorReload)
            Reload();

        bool pressed = triggerAction.WasPressedThisFrame();
        bool released = triggerAction.WasReleasedThisFrame();
        if (EditorFallback && editorFireAction != null)
        {
            pressed = pressed || editorFireAction.WasPressedThisFrame();
            released = released || editorFireAction.WasReleasedThisFrame();
        }

        if (pressed)
        {
            triggerPressedAt = Time.time;
            chargeReadyPulsed = false;
        }

        if (!chargeReadyPulsed && triggerPressedAt >= 0f && !reloading && rounds > 0
            && Time.time - triggerPressedAt >= chargeHoldTime)
        {
            chargeReadyPulsed = true;
            Pulse(chargeReadyHapticAmplitude, chargeReadyHapticDuration);
        }

        if (released)
        {
            float held = triggerPressedAt >= 0f ? Time.time - triggerPressedAt : 0f;
            triggerPressedAt = -1f;
            TryFire(held >= chargeHoldTime);
        }

        if (kick > 0f)
        {
            kick = Mathf.MoveTowards(kick, 0f, kickReturnSpeed * Time.deltaTime);
            transform.localRotation = restLocalRotation * Quaternion.Euler(-kick, 0f, 0f);
        }
    }

    public void Reload()
    {
        // A reload already in progress is left alone, including one started by an empty magazine.
        if (reloading || rounds >= magazineSize)
            return;

        reloading = true;
        reloadEndsAt = Time.time + Mathf.Max(0f, reloadDuration);
    }

    void FinishReload()
    {
        rounds = magazineSize;
        reloading = false;
        Pulse(shotHapticAmplitude, shotHapticDuration);
        reloadPulsesLeft = 1;
        nextReloadPulseAt = Time.time + 0.09f;
    }

    void TryFire(bool charged)
    {
        if (reloading || rounds <= 0)
            return;
        if (Time.time < nextFireTime)
            return;

        int cost = charged ? Mathf.Min(Mathf.Max(1, chargedAmmoCost), rounds) : 1;
        rounds -= cost;
        nextFireTime = Time.time + 1f / Mathf.Max(0.01f, shotsPerSecond);
        kick = charged ? kickDegrees * 1.5f : kickDegrees;
        Pulse(shotHapticAmplitude, shotHapticDuration);
        PlayShot(charged);
        SpawnShot(charged);

        if (rounds <= 0)
            Reload();
    }

    void PlayShot(bool charged)
    {
        if (shotSource == null)
            return;

        AudioClip clip = charged ? chargedShotClip : shotClip;
        if (clip == null)
            clip = shotClip;
        if (clip != null)
            shotSource.PlayOneShot(clip);
    }

    void SpawnShot(bool charged)
    {
        Transform origin = muzzle != null ? muzzle : transform;
        Vector3 direction = origin.forward;
        float radius = charged ? chargedRadius : shotRadius;
        float speed = shotSpeed * (charged ? chargedSpeedMultiplier : 1f);
        var shot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        shot.name = charged ? "ChargedShot" : "Shot";
        shot.transform.SetParent(null, true);
        shot.transform.position = origin.position;
        shot.transform.rotation = Quaternion.LookRotation(direction);
        var collider = shot.GetComponent<Collider>();
        if (collider != null)
        {
            collider.enabled = false;
            Destroy(collider);
        }

        var projectile = shot.AddComponent<Hd2Shot>();
        projectile.velocity = direction * speed;
        projectile.lifetime = shotLifetime;
        projectile.owner = transform.root;
        projectile.damage = normalDamage * (charged ? chargedAmmoCost : 1);
        projectile.bouncesRemaining = charged ? chargedBounces : 0;
        projectile.gravel = !charged;
        projectile.UsePlasma(shotColor, charged ? radius * 2.2f : radius * 1.8f);
        projectile.minBoltLength = shotSpeed * 0.016f;
        projectile.ReleaseFromMuzzle(origin.position, direction);
        SpawnFlash(origin);
    }

    void SpawnFlash(Transform barrel)
    {
        if (barrel == null)
            return;

        var flash = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        flash.name = "MuzzleFlash";
        var collider = flash.GetComponent<Collider>();
        if (collider != null)
        {
            collider.enabled = false;
            Destroy(collider);
        }

        flash.transform.SetParent(barrel, false);
        Vector3 parentScale = barrel.lossyScale;
        float ScaleOf(float axis, float worldSize)
        {
            return Mathf.Abs(axis) > 0.001f ? worldSize / axis : worldSize;
        }

        flash.transform.localPosition = new Vector3(0f, 0f, ScaleOf(parentScale.z, 0.06f));
        flash.transform.localRotation = Quaternion.identity;
        flash.transform.localScale = new Vector3(
            ScaleOf(parentScale.x, 0.1f),
            ScaleOf(parentScale.y, 0.1f),
            ScaleOf(parentScale.z, 0.28f));
        var glow = flash.AddComponent<Hd2Shot>();
        glow.UsePlasma(shotColor, 0.1f);
        glow.enabled = false;
        flash.AddComponent<Hd2MuzzleFlash>();
    }

    void Pulse(float amplitude, float duration)
    {
        if (amplitude <= 0f || duration <= 0f)
            return;

        var node = hand == Hand.Left ? XRNode.LeftHand : XRNode.RightHand;
        InputDevices.GetDevicesAtXRNode(node, hapticDevices);
        for (int i = 0; i < hapticDevices.Count; i++)
        {
            UnityEngine.XR.InputDevice device = hapticDevices[i];
            if (device.TryGetHapticCapabilities(out HapticCapabilities caps) && caps.supportsImpulse)
                device.SendHapticImpulse(0, Mathf.Clamp01(amplitude), duration);
        }
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

    static void Dispose(ref InputAction action)
    {
        if (action == null)
            return;
        action.Disable();
        action.Dispose();
        action = null;
    }
}

/// <summary>
/// A short glow at the barrel so the bolt can begin a meter ahead without a visible gap.
/// </summary>
public class Hd2MuzzleFlash : MonoBehaviour
{
    const float life = 0.05f;
    float age;
    Vector3 startScale;

    void Awake()
    {
        startScale = transform.localScale;
    }

    void Update()
    {
        age += Time.deltaTime;
        float t = age / life;
        if (t >= 1f)
        {
            Destroy(gameObject);
            return;
        }

        transform.localScale = startScale * Mathf.Lerp(1.15f, 0.15f, t);
    }
}
