using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

/// <summary>
/// Grip burst pistol. Hold the trigger to fire. Eight rounds, one every 0.05 seconds.
/// Body hits are 8 and head hits are 16 inside 10 meters. Each further 10 meters
/// takes 2 off a body hit and 4 off a head hit, and both stop falling at 2 and 4 after 30 meters.
/// </summary>
public class Hd2BurstPistol : MonoBehaviour
{
    public Hd2Pistol.Hand hand = Hd2Pistol.Hand.Right;

    public int magazineSize = 8;
    public float shotsPerSecond = 20f;
    public float reloadDuration = 0.75f;

    public float bulletSpeed = 120f;
    public float shotLifetime = 0.55f;
    public float shotRadius = 0.028f;
    public float bodyDamage = 8f;
    public float headDamage = 16f;
    public float falloffStartMeters = 10f;
    public float falloffStepMeters = 10f;
    public float falloffBodyLoss = 2f;
    public float falloffMinBody = 2f;
    public Color shotColor = new Color(0.82f, 0.9f, 1f, 1f);

    public int recoilClimbRounds = 5;
    public float recoilClimbPerRound = 0.35f;
    public float vibrationDegrees = 0.6f;
    public float kickReturnSpeed = 22f;
    public float shotHapticAmplitude = 0.2f;
    public float shotHapticDuration = 0.02f;

    public Transform muzzle;
    public bool botHeld;

    int rounds;
    int climbedRounds;
    float nextFireTime;
    bool reloading;
    float reloadEndsAt;
    float climb;
    bool firing;
    bool waitForTriggerRelease = true;
    [SerializeField] bool gripMirrored;
    Quaternion restLocalRotation;
    AudioSource shotSource;
    AudioClip shotClip;
    AudioClip reloadClip;
    TextMesh ammoLabel;

    InputAction triggerAction;
    InputAction reloadAction;
    InputAction editorFireAction;
    InputAction editorReloadAction;

    static readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();
    static readonly List<UnityEngine.XR.InputDevice> hapticDevices = new List<UnityEngine.XR.InputDevice>();

    public int Rounds => rounds;

    void Awake()
    {
        if (hand == Hd2Pistol.Hand.Right && !gripMirrored && transform.Find("Hand") != null)
        {
            Hd2ShotgunPickup.MirrorGrip(transform);
            gripMirrored = true;
        }

        if (muzzle == null)
        {
            Transform found = transform.Find("Muzzle");
            if (found != null)
                muzzle = found;
        }

        restLocalRotation = transform.localRotation;
        rounds = magazineSize;
        shotClip = Resources.Load<AudioClip>("Audio/SmgShot");
        reloadClip = Resources.Load<AudioClip>("Audio/PistolReload");
        if (shotClip != null || reloadClip != null)
        {
            shotSource = gameObject.AddComponent<AudioSource>();
            shotSource.playOnAwake = false;
            shotSource.loop = false;
            shotSource.spatialBlend = 1f;
            shotSource.dopplerLevel = 0f;
            shotSource.rolloffMode = AudioRolloffMode.Linear;
            shotSource.minDistance = 2f;
            shotSource.maxDistance = 36f;
        }

        ammoLabel = Hd2AmmoReadout.Bind(transform);
        if (ammoLabel != null)
            Hd2AmmoReadout.Style(ammoLabel);
        RefreshAmmo();
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
        triggerAction = Button("Hd2BurstFire" + node, "<XRController>{" + node + "}/triggerPressed");
        reloadAction = Button("Hd2BurstReload" + node, "<XRController>{" + node + "}/gripPressed");
        if (hand == Hd2Pistol.Hand.Right)
        {
            editorFireAction = Button("Hd2BurstFireEditor", "<Mouse>/rightButton");
            editorReloadAction = Button("Hd2BurstReloadEditor", "<Keyboard>/r");
        }
    }

    static InputAction Button(string actionName, string binding)
    {
        var action = new InputAction(actionName, InputActionType.Button);
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

        bool bot = GetComponentInParent<Hd2EnemyBot>() != null;
        var fire = hand == Hd2Pistol.Hand.Left ? Hd2ControlMap.Action.FireLeft : Hd2ControlMap.Action.FireRight;
        var reload = hand == Hd2Pistol.Hand.Left ? Hd2ControlMap.Action.ReloadLeft : Hd2ControlMap.Action.ReloadRight;
        bool editorReload = !bot && EditorFallback && editorReloadAction != null && editorReloadAction.WasPressedThisFrame();
        if (!bot && (ActionPressed(reload, reloadAction) || editorReload))
            Reload();

        bool menuOpen = ControlMap() != null && ControlMap().MenuOpen;
        bool held = !bot && !menuOpen && ActionHeld(fire, triggerAction);
        if (!bot && !menuOpen && EditorFallback && editorFireAction != null)
            held = held || editorFireAction.IsPressed();

        if (waitForTriggerRelease)
        {
            if (held)
                held = false;
            else
                waitForTriggerRelease = false;
        }

        if (botHeld)
        {
            waitForTriggerRelease = false;
            held = true;
        }

        firing = held && !reloading && rounds > 0;
        if (firing)
            TryFire();
        else
        {
            climbedRounds = 0;
            climb = Mathf.MoveTowards(climb, 0f, kickReturnSpeed * Time.deltaTime);
        }

        float wobble = firing ? Mathf.Sin(Time.time * 36f) * vibrationDegrees : 0f;
        transform.localRotation = restLocalRotation * Quaternion.Euler(-climb, wobble, 0f);
    }

    public void Reload()
    {
        if (reloading || rounds >= magazineSize)
            return;

        reloading = true;
        reloadEndsAt = Time.time + Mathf.Max(0f, reloadDuration);
        firing = false;
        if (shotSource != null && reloadClip != null)
            shotSource.PlayOneShot(reloadClip);
        RefreshAmmo();
    }

    void FinishReload()
    {
        rounds = magazineSize;
        reloading = false;
        climbedRounds = 0;
        climb = 0f;
        RefreshAmmo();
    }

    void TryFire()
    {
        if (Time.time < nextFireTime)
            return;

        rounds--;
        RefreshAmmo();
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
        var projectile = Hd2Shot.Rent();
        projectile.gameObject.name = "BurstShot";
        projectile.transform.SetParent(null, true);
        projectile.transform.position = origin.position;
        projectile.transform.rotation = Quaternion.LookRotation(direction);
        projectile.velocity = direction * bulletSpeed;
        projectile.lifetime = shotLifetime;
        projectile.owner = Hd2SpawnField.FighterBody(this);
        projectile.damage = bodyDamage;
        projectile.headMultiplier = bodyDamage > 0.01f ? headDamage / bodyDamage : 1f;
        projectile.rangeFalloffStart = falloffStartMeters;
        projectile.falloffInterval = falloffStepMeters;
        projectile.falloffBodyLoss = falloffBodyLoss;
        projectile.falloffMinDamage = falloffMinBody;
        projectile.UsePlasma(shotColor, shotRadius * 1.8f);
        projectile.minBoltLength = bulletSpeed * 0.012f;
        projectile.ReleaseFromMuzzle(origin.position, direction);
        SpawnFlash(origin);
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
            new Vector3(0f, 0f, ScaleOf(parentScale.z, 0.05f)),
            new Vector3(ScaleOf(parentScale.x, 0.06f), ScaleOf(parentScale.y, 0.06f), ScaleOf(parentScale.z, 0.16f)),
            shotColor,
            0.05f);
    }

    void RefreshAmmo()
    {
        Hd2AmmoReadout.Show(ammoLabel, rounds, reloading);
    }

    void Pulse(float amplitude, float duration)
    {
        if (GetComponentInParent<Hd2EnemyBot>() != null)
            return;

        var node = hand == Hd2Pistol.Hand.Left ? XRNode.LeftHand : XRNode.RightHand;
        InputDevices.GetDevicesAtXRNode(node, hapticDevices);
        for (int i = 0; i < hapticDevices.Count; i++)
        {
            if (hapticDevices[i].TryGetHapticCapabilities(out HapticCapabilities caps) && caps.supportsImpulse)
                hapticDevices[i].SendHapticImpulse(0, Mathf.Clamp01(amplitude), duration);
        }
    }

    Hd2ControlMap controlMap;

    Hd2ControlMap ControlMap()
    {
        if (controlMap == null)
            controlMap = GetComponentInParent<Hd2ControlMap>();
        return controlMap;
    }

    bool ActionPressed(Hd2ControlMap.Action action, InputAction fallback)
    {
        var map = ControlMap();
        if (map != null)
            return map.Pressed(action);
        return fallback != null && fallback.WasPressedThisFrame();
    }

    bool ActionHeld(Hd2ControlMap.Action action, InputAction fallback)
    {
        var map = ControlMap();
        if (map != null)
            return map.Held(action);
        return fallback != null && fallback.IsPressed();
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
