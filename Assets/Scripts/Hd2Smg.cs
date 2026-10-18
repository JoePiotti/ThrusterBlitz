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

    public void BindMuzzle(Transform muzzlePoint)
    {
        muzzle = muzzlePoint;
        if (ammoLabel != null)
        {
            Destroy(ammoLabel.gameObject);
            ammoLabel = null;
        }

        EnsureAmmoScreen();
    }

    int rounds;
    int climbedRounds;
    float nextFireTime;
    bool reloading;
    float reloadEndsAt;
    float climb;
    bool firing;
    bool waitForTriggerRelease = true;
    Quaternion restLocalRotation;
    AudioSource shotSource;
    AudioClip shotClip;
    AudioClip reloadClip;
    TextMesh ammoLabel;

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
        reloadClip = Resources.Load<AudioClip>("Audio/SmgReload");
        if (shotClip != null || reloadClip != null)
        {
            shotSource = gameObject.AddComponent<AudioSource>();
            shotSource.playOnAwake = false;
            shotSource.loop = false;
            shotSource.spatialBlend = 1f;
            shotSource.minDistance = 0.4f;
            shotSource.maxDistance = 12f;
        }

        EnsureAmmoScreen();
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
        var fire = hand == Hd2Pistol.Hand.Left ? Hd2ControlMap.Action.FireLeft : Hd2ControlMap.Action.FireRight;
        var reload = hand == Hd2Pistol.Hand.Left ? Hd2ControlMap.Action.ReloadLeft : Hd2ControlMap.Action.ReloadRight;
        if (ActionPressed(reload, reloadAction) || editorReload)
            Reload();

        bool menuOpen = ControlMap() != null && ControlMap().MenuOpen;
        bool held = !menuOpen && ActionHeld(fire, triggerAction);
        if (!menuOpen && EditorFallback && editorFireAction != null)
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
        transform.localRotation = restLocalRotation * Quaternion.Euler(climb, wobble, 0f);
    }

    public bool botHeld;

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
        projectile.gameObject.name = "SmgShot";
        projectile.transform.SetParent(null, true);
        projectile.transform.position = origin.position;
        projectile.transform.rotation = Quaternion.LookRotation(direction);
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

        Vector3 parentScale = barrel.lossyScale;
        float ScaleOf(float axis, float worldSize)
        {
            return Mathf.Abs(axis) > 0.001f ? worldSize / axis : worldSize;
        }

        float side = hand == Hd2Pistol.Hand.Right ? -1f : 1f;
        Hd2MuzzleFlash.Show(
            barrel,
            new Vector3(-side * ScaleOf(parentScale.x, 0.025f), 0f, ScaleOf(parentScale.z, 0.06f)),
            new Vector3(ScaleOf(parentScale.x, 0.08f), ScaleOf(parentScale.y, 0.08f), ScaleOf(parentScale.z, 0.22f)),
            shotColor,
            0.08f);
    }

    void EnsureAmmoScreen()
    {
        if (ammoLabel == null)
        {
            Transform existing = transform.Find("AmmoReadout");
            if (existing != null)
                ammoLabel = existing.GetComponent<TextMesh>() ?? existing.GetComponentInChildren<TextMesh>(true);
        }

        if (ammoLabel != null)
        {
            ApplyAmmoFont();
            RefreshAmmo();
            return;
        }

        Vector3 back = Vector3.back;
        if (muzzle != null)
        {
            Vector3 toMuzzle = transform.InverseTransformPoint(muzzle.position);
            if (toMuzzle.sqrMagnitude > 0.0004f)
                back = -toMuzzle.normalized;
        }

        var samples = new Vector3[32];
        int sampleCount = 0;
        var filters = GetComponentsInChildren<MeshFilter>();
        for (int f = 0; f < filters.Length && sampleCount < samples.Length; f++)
        {
            Mesh mesh = filters[f].sharedMesh;
            if (mesh == null)
                continue;

            Bounds box = mesh.bounds;
            Vector3 center = box.center;
            Vector3 extents = box.extents;
            for (int i = 0; i < 8 && sampleCount < samples.Length; i++)
            {
                Vector3 corner = center + new Vector3(
                    (i & 1) == 0 ? -extents.x : extents.x,
                    (i & 2) == 0 ? -extents.y : extents.y,
                    (i & 4) == 0 ? -extents.z : extents.z);
                samples[sampleCount++] = transform.InverseTransformPoint(filters[f].transform.TransformPoint(corner));
            }
        }

        Vector3 pos = back * 0.12f;
        if (sampleCount > 0)
        {
            float furthest = float.NegativeInfinity;
            for (int i = 0; i < sampleCount; i++)
                furthest = Mathf.Max(furthest, Vector3.Dot(samples[i], back));

            Vector3 sum = Vector3.zero;
            int count = 0;
            for (int i = 0; i < sampleCount; i++)
            {
                if (Vector3.Dot(samples[i], back) < furthest - 0.012f)
                    continue;
                sum += samples[i];
                count++;
            }

            pos = sum / Mathf.Max(1, count);
            Vector3 up = Vector3.ProjectOnPlane(Vector3.up, back);
            if (up.sqrMagnitude > 0.0001f)
                pos += up.normalized * 0.121f;
        }

        pos -= back * 0.042f;

        var root = new GameObject("AmmoReadout");
        root.transform.SetParent(transform, false);
        root.transform.localPosition = pos;
        root.transform.localRotation = Quaternion.LookRotation(-back, Vector3.up);
        ammoLabel = root.AddComponent<TextMesh>();
        ApplyAmmoFont();
        ammoLabel.fontSize = 32;
        ammoLabel.characterSize = 0.005f;
        ammoLabel.anchor = TextAnchor.MiddleCenter;
        ammoLabel.alignment = TextAlignment.Center;
        ammoLabel.color = new Color(0.3f, 0.75f, 1f, 1f);
        var textRenderer = ammoLabel.GetComponent<MeshRenderer>();
        textRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        textRenderer.receiveShadows = false;
        RefreshAmmo();
    }

    void ApplyAmmoFont()
    {
        if (ammoLabel == null)
            return;

        if (ammoLabel.GetComponent<MeshFilter>() == null)
            ammoLabel.gameObject.AddComponent<MeshFilter>();

        ammoLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        ammoLabel.color = new Color(0.3f, 0.75f, 1f, 1f);
        var renderer = ammoLabel.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.enabled = true;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            if (ammoLabel.font != null)
                renderer.sharedMaterial = ammoLabel.font.material;
        }

        string text = ammoLabel.text;
        ammoLabel.text = string.IsNullOrEmpty(text) ? magazineSize.ToString() : text;
    }

    void RefreshAmmo()
    {
        if (ammoLabel == null)
            return;

        ammoLabel.text = reloading ? "R" : rounds.ToString();
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
