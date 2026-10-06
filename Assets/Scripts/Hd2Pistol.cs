using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

/// <summary>
/// One pistol on one hand. A quick trigger release fires a normal shot.
/// Holding the trigger plays the charge sound once, without looping, then releasing
/// after chargeHoldTime fires a charged shot.
/// Grip starts a timed reload on that gun only. An empty magazine starts the same reload.
/// Blue text on the back shows the rounds left, or R while reloading.
/// Move AmmoReadout on each pistol in the VRPlayer prefab. Play mode does not move it.
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
    [Tooltip("Rounds text on the back of this pistol. Drag AmmoReadout in the VRPlayer prefab while play mode is off.")]
    public TextMesh ammoLabel;

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
    AudioSource chargeSource;
    AudioClip shotClip;
    AudioClip chargedShotClip;
    AudioClip chargeClip;
    AudioClip reloadClip;

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
        chargeClip = Resources.Load<AudioClip>("Audio/PistolCharge");
        reloadClip = Resources.Load<AudioClip>("Audio/PistolReload");
        if (shotClip != null || chargedShotClip != null || reloadClip != null)
            shotSource = AddGunVoice();
        if (chargeClip != null)
        {
            chargeSource = AddGunVoice();
            chargeSource.clip = chargeClip;
            chargeSource.loop = false;
        }

        EnsureAmmoScreen();
    }

    AudioSource AddGunVoice()
    {
        var source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 1f;
        source.minDistance = 0.4f;
        source.maxDistance = 12f;
        return source;
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
        StopCharge();
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
            PlayCharge();
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
            StopCharge();
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
        PlayReload();
        RefreshAmmo();
    }

    void FinishReload()
    {
        rounds = magazineSize;
        reloading = false;
        Pulse(shotHapticAmplitude, shotHapticDuration);
        reloadPulsesLeft = 1;
        nextReloadPulseAt = Time.time + 0.09f;
        RefreshAmmo();
    }

    void TryFire(bool charged)
    {
        if (reloading || rounds <= 0)
            return;
        if (Time.time < nextFireTime)
            return;

        int cost = charged ? Mathf.Min(Mathf.Max(1, chargedAmmoCost), rounds) : 1;
        rounds -= cost;
        RefreshAmmo();
        nextFireTime = Time.time + 1f / Mathf.Max(0.01f, shotsPerSecond);
        kick = charged ? kickDegrees * 1.5f : kickDegrees;
        Pulse(shotHapticAmplitude, shotHapticDuration);
        PlayShot(charged);
        SpawnShot(charged);

        if (rounds <= 0)
            Reload();
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
        ammoLabel.text = string.IsNullOrEmpty(text) ? "16" : text;
    }

    void RefreshAmmo()
    {
        if (ammoLabel == null)
            return;

        ammoLabel.text = reloading ? "R" : rounds.ToString();
    }

    void PlayReload()
    {
        if (shotSource == null || reloadClip == null)
            return;

        shotSource.PlayOneShot(reloadClip);
    }

    void PlayCharge()
    {
        if (chargeSource == null || reloading || rounds <= 0)
            return;

        chargeSource.loop = false;
        chargeSource.Play();
    }

    void StopCharge()
    {
        if (chargeSource != null && chargeSource.isPlaying)
            chargeSource.Stop();
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
