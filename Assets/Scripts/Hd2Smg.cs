using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

/// <summary>
/// One-handed automatic SMG. Numbers are inspector parameters.
/// Hold the trigger to fire. Grip reloads, and an empty magazine reloads on its own.
/// </summary>
public class Hd2Smg : MonoBehaviour
{
    [Header("Hand")]
    public Hd2Pistol.Hand hand = Hd2Pistol.Hand.Right;

    [Header("Magazine")]
    public int magazineSize = 30;
    public float shotsPerSecond = 12f;
    public float reloadDuration = 1.5f;

    [Header("Shot")]
    public float bulletSpeed = 80f;
    public float shotLifetime = 1.2f;
    public float shotRadius = 0.03f;
    public float bodyDamage = 10f;
    public float headDamage = 20f;
    public Color shotColor = new Color(1f, 0.7f, 0.18f, 1f);
    [Tooltip("Strength of the tick on each shot, from 0 to 1.")]
    public float shotHapticAmplitude = 0.22f;
    [Tooltip("Length of the tick on each shot, in seconds.")]
    public float shotHapticDuration = 0.02f;

    [Header("Recoil")]
    [Tooltip("How many rounds at the start of a burst climb the barrel.")]
    public int recoilClimbRounds = 10;
    [Tooltip("Degrees added to the climb for each of those rounds.")]
    public float recoilClimbPerRound = 0.45f;
    [Tooltip("Side-to-side wobble while firing, in degrees.")]
    public float vibrationDegrees = 1f;
    public float kickReturnSpeed = 18f;

    public Transform muzzle;

    int rounds;
    int climbedRounds;
    float nextFireTime;
    bool reloading;
    float reloadEndsAt;
    float climb;
    bool firing;
    Quaternion restLocalRotation;
    AudioSource shotSource;
    AudioClip shotClip;

    InputAction triggerAction;
    InputAction reloadAction;
    InputAction editorFireAction;
    InputAction editorReloadAction;

    static readonly System.Collections.Generic.List<XRDisplaySubsystem> displays =
        new System.Collections.Generic.List<XRDisplaySubsystem>();
    static readonly System.Collections.Generic.List<UnityEngine.XR.InputDevice> hapticDevices =
        new System.Collections.Generic.List<UnityEngine.XR.InputDevice>();

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

        shotClip = Resources.Load<AudioClip>("Audio/SmgShot");
        if (shotClip == null)
            return;

        shotSource = gameObject.AddComponent<AudioSource>();
        shotSource.playOnAwake = false;
        shotSource.spatialBlend = 1f;
        shotSource.minDistance = 0.4f;
        shotSource.maxDistance = 12f;
    }

    void OnEnable()
    {
        BindInputs();
    }

    public void SetHand(Hd2Pistol.Hand value)
    {
        hand = value;
        if (isActiveAndEnabled)
            BindInputs();
    }

    void BindInputs()
    {
        Dispose(ref triggerAction);
        Dispose(ref reloadAction);
        Dispose(ref editorFireAction);
        Dispose(ref editorReloadAction);

        string node = hand == Hd2Pistol.Hand.Left ? "LeftHand" : "RightHand";

        triggerAction = new InputAction("Hd2SmgFire" + node, InputActionType.Button);
        triggerAction.AddBinding("<XRController>{" + node + "}/triggerPressed");
        triggerAction.Enable();

        reloadAction = new InputAction("Hd2SmgReload" + node, InputActionType.Button);
        reloadAction.AddBinding("<XRController>{" + node + "}/gripPressed");
        reloadAction.Enable();

        if (hand == Hd2Pistol.Hand.Right)
        {
            editorFireAction = new InputAction("Hd2SmgFireEditor", InputActionType.Button);
            editorFireAction.AddBinding("<Mouse>/rightButton");
            editorFireAction.Enable();

            editorReloadAction = new InputAction("Hd2SmgReloadEditor", InputActionType.Button);
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
        if (reloading && Time.time >= reloadEndsAt)
            FinishReload();

        bool editorReload = EditorFallback && editorReloadAction != null && editorReloadAction.WasPressedThisFrame();
        if (reloadAction.WasPressedThisFrame() || editorReload)
            Reload();

        bool held = triggerAction.IsPressed();
        if (EditorFallback && editorFireAction != null)
            held = held || editorFireAction.IsPressed();

        firing = held && !reloading && rounds > 0;
        if (firing)
            TryFire();
        else
        {
            climbedRounds = 0;
            climb = Mathf.MoveTowards(climb, 0f, kickReturnSpeed * Time.deltaTime);
        }

        float wobble = firing ? Mathf.Sin(Time.time * 36f) * vibrationDegrees : 0f;
        transform.localRotation = restLocalRotation * Quaternion.Euler(climb, wobble, 0f);
    }

    public void Reload()
    {
        if (reloading || rounds >= magazineSize)
            return;

        reloading = true;
        reloadEndsAt = Time.time + Mathf.Max(0f, reloadDuration);
        firing = false;
    }

    void FinishReload()
    {
        rounds = magazineSize;
        reloading = false;
        climbedRounds = 0;
        climb = 0f;
    }

    void TryFire()
    {
        if (Time.time < nextFireTime)
            return;

        rounds--;
        nextFireTime = Time.time + 1f / Mathf.Max(0.01f, shotsPerSecond);
        if (climbedRounds < recoilClimbRounds)
        {
            climbedRounds++;
            climb += recoilClimbPerRound;
        }

        SpawnShot();
        if (shotSource != null && shotClip != null)
            shotSource.PlayOneShot(shotClip);
        Pulse(shotHapticAmplitude, shotHapticDuration);
        if (rounds <= 0)
            Reload();
    }

    void SpawnShot()
    {
        Transform origin = muzzle != null ? muzzle : transform;
        Vector3 direction = origin.forward;
        var shot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        shot.name = "SmgShot";
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
        projectile.velocity = direction * bulletSpeed;
        projectile.lifetime = shotLifetime;
        projectile.owner = transform.root;
        projectile.damage = bodyDamage;
        projectile.headMultiplier = bodyDamage > 0.01f ? headDamage / bodyDamage : 1f;
        projectile.bouncesRemaining = 0;
        projectile.UsePlasma(shotColor, shotRadius * 1.8f);
        projectile.minBoltLength = bulletSpeed * 0.016f;
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

        float side = hand == Hd2Pistol.Hand.Right ? -1f : 1f;
        flash.transform.localPosition = new Vector3(
            -side * ScaleOf(parentScale.x, 0.025f),
            0f,
            ScaleOf(parentScale.z, 0.06f));
        flash.transform.localRotation = Quaternion.identity;
        flash.transform.localScale = new Vector3(
            ScaleOf(parentScale.x, 0.08f),
            ScaleOf(parentScale.y, 0.08f),
            ScaleOf(parentScale.z, 0.22f));
        var glow = flash.AddComponent<Hd2Shot>();
        glow.UsePlasma(shotColor, 0.08f);
        glow.enabled = false;
        flash.AddComponent<Hd2MuzzleFlash>();
    }

    void Pulse(float amplitude, float duration)
    {
        if (amplitude <= 0f || duration <= 0f)
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
