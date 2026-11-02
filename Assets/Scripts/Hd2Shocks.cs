using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

/// <summary>
/// Shocks on the gripped hand. Same hold as the pistol.
/// A trigger tap fires one bolt. Holding the trigger for chargeHoldTime, then
/// releasing, fires a slower sphere that bursts. The charge glow fills the span
/// between the two glowyChargeUp markers, then stays bright until the shot fires.
/// </summary>
public class Hd2Shocks : MonoBehaviour
{
    public Hd2Pistol.Hand hand = Hd2Pistol.Hand.Right;

    public int magazineSize = 10;
    public float shotsPerSecond = 2f;
    public float reloadDuration = 1f;

    public float boltSpeed = 140f;
    public float boltDamage = 20f;
    public float boltThickness = 0.084f;
    public float boltLength = 9.2f;
    public float boltMaxLength = 14f;
    public Color boltColor = new Color(1f, 0.16f, 0.08f, 1f);

    public float chargeHoldTime = 1.25f;
    public float orbSpeed = 35f;
    public float orbRadius = 0.0882f;
    public float innerRadius = 1.5f;
    public float innerDamage = 72f;
    public float outerRadius = 2.75f;
    public float outerDamage = 35f;

    public float kickDegrees = 4f;
    public float kickReturnSpeed = 18f;

    public Transform muzzle;

    int rounds;
    float nextFireTime;
    float triggerPressedAt = -1f;
    bool waitForTriggerRelease = true;
    bool chargeReadyPulsed;
    bool chargeRumbling;
    float nextChargeHapticAt;
    bool reloading;
    float reloadEndsAt;
    float kick;
    Quaternion restLocalRotation;
    InputAction triggerAction;
    InputAction reloadAction;
    InputAction editorFireAction;
    InputAction editorReloadAction;
    AudioSource voice;
    AudioSource chargeSource;
    TextMesh ammoLabel;

    public int Rounds => rounds;
    [SerializeField] bool gripMirrored;

    Transform chargeGlow;
    Transform chargeGlowEnd;
    Vector3 chargeEndLocal;
    Light chargeLight;
    ParticleSystem chargeSparks;
    Transform chargeCore;
    Renderer chargeCoreRenderer;
    Material chargeCoreMaterial;
    Material chargeSparkMaterial;
    bool ownSparkMaterial;
    float chargeShown;

    static readonly Color ChargeRed = new Color(1f, 0.07f, 0.02f, 1f);

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
        voice = AddVoice();
        chargeSource = AddVoice();
        AudioClip charge = Resources.Load<AudioClip>("Audio/ShocksCharge");
        if (charge != null)
        {
            chargeSource.clip = charge;
            chargeSource.loop = false;
        }

        EnsureAmmoLabel();
        EnsureChargeGlow();
    }

    public void SetHand(Hd2Pistol.Hand which)
    {
        hand = which;
    }

    AudioSource AddVoice()
    {
        var source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 1f;
        source.dopplerLevel = 0f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 2f;
        source.maxDistance = 36f;
        return source;
    }

    void OnEnable()
    {
        string node = hand == Hd2Pistol.Hand.Left ? "LeftHand" : "RightHand";
        triggerAction = Button("Hd2ShocksFire", "<XRController>{" + node + "}/triggerPressed");
        reloadAction = Button("Hd2ShocksReload", "<XRController>{" + node + "}/gripPressed");
        if (hand == Hd2Pistol.Hand.Right)
        {
            editorFireAction = Button("Hd2ShocksFireEditor", "<Mouse>/rightButton");
            editorReloadAction = Button("Hd2ShocksReloadEditor", "<Keyboard>/r");
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
        StopChargeRumble();
        StopCharge();
        Dispose(ref triggerAction);
        Dispose(ref reloadAction);
        Dispose(ref editorFireAction);
        Dispose(ref editorReloadAction);
    }

    void Update()
    {
        if (reloading && Time.time >= reloadEndsAt)
            FinishReload();

        if (GetComponentInParent<Hd2EnemyBot>() == null)
        {
            bool editor = EditorFallback();
            if (Pressed(hand == Hd2Pistol.Hand.Left ? Hd2ControlMap.Action.ReloadLeft : Hd2ControlMap.Action.ReloadRight, reloadAction)
                || (editor && editorReloadAction != null && editorReloadAction.WasPressedThisFrame()))
                Reload();

            bool menuOpen = Map() != null && Map().MenuOpen;
            if (menuOpen && triggerPressedAt >= 0f)
            {
                triggerPressedAt = -1f;
                StopCharge();
            }

            bool pressed = !menuOpen && Pressed(hand == Hd2Pistol.Hand.Left ? Hd2ControlMap.Action.FireLeft : Hd2ControlMap.Action.FireRight, triggerAction);
            bool released = !menuOpen && Released(hand == Hd2Pistol.Hand.Left ? Hd2ControlMap.Action.FireLeft : Hd2ControlMap.Action.FireRight, triggerAction);
            if (editor && editorFireAction != null)
            {
                pressed = pressed || editorFireAction.WasPressedThisFrame();
                released = released || editorFireAction.WasReleasedThisFrame();
            }

            if (waitForTriggerRelease)
            {
                pressed = false;
                released = false;
                if (!TriggerIsDown(editor))
                    waitForTriggerRelease = false;
            }

            if (pressed)
            {
                triggerPressedAt = Time.time;
                chargeReadyPulsed = false;
                nextChargeHapticAt = 0f;
                PlayCharge();
            }

            bool charging = triggerPressedAt >= 0f && !chargeReadyPulsed && !reloading && rounds > 0
                && Time.time - triggerPressedAt < chargeHoldTime;
            if (charging)
            {
                if (Time.time >= nextChargeHapticAt)
                {
                    chargeRumbling = true;
                    Pulse(0.5f, 0.05f);
                    nextChargeHapticAt = Time.time + 0.05f;
                }
            }
            else if (chargeRumbling)
                StopChargeRumble();

            if (!chargeReadyPulsed && triggerPressedAt >= 0f && !reloading && rounds > 0
                && Time.time - triggerPressedAt >= chargeHoldTime)
                chargeReadyPulsed = true;

            if (released)
            {
                float held = triggerPressedAt >= 0f ? Time.time - triggerPressedAt : 0f;
                triggerPressedAt = -1f;
                StopCharge();
                TryFire(held >= chargeHoldTime);
            }
        }

        if (kick > 0f)
        {
            kick = Mathf.MoveTowards(kick, 0f, kickReturnSpeed * Time.deltaTime);
            transform.localRotation = restLocalRotation * Quaternion.Euler(-kick, 0f, 0f);
        }

        UpdateChargeGlow();
    }

    public void Reload()
    {
        if (reloading || rounds >= magazineSize)
            return;

        reloading = true;
        reloadEndsAt = Time.time + Mathf.Max(0f, reloadDuration);
        AudioClip clip = Resources.Load<AudioClip>("Audio/PistolReload");
        if (voice != null && clip != null)
            voice.PlayOneShot(clip);
        RefreshAmmo();
    }

    void FinishReload()
    {
        rounds = magazineSize;
        reloading = false;
        Pulse(0.7f, 0.04f);
        RefreshAmmo();
    }

    public void BotTap()
    {
        waitForTriggerRelease = false;
        TryFire(false);
    }

    void TryFire(bool charged)
    {
        if (reloading || rounds <= 0 || Time.time < nextFireTime)
            return;

        rounds--;
        RefreshAmmo();
        nextFireTime = Time.time + 1f / Mathf.Max(0.01f, shotsPerSecond);
        kick = charged ? kickDegrees * 1.6f : kickDegrees;
        Pulse(0.7f, 0.04f);
        if (charged)
            SpawnOrb();
        else
            SpawnBolt();

        if (rounds <= 0)
            Reload();
    }

    void SpawnBolt()
    {
        Transform origin = muzzle != null ? muzzle : transform;
        Vector3 direction = origin.forward;
        PlayClip("Audio/ShockShot");
        var projectile = Hd2Shot.Rent();
        projectile.gameObject.name = "ShockBolt";
        projectile.transform.position = origin.position;
        projectile.transform.rotation = Quaternion.LookRotation(direction);
        projectile.velocity = direction * boltSpeed;
        projectile.lifetime = 1.6f;
        projectile.owner = Hd2SpawnField.FighterBody(this);
        projectile.damage = boltDamage;
        projectile.headMultiplier = 1.5f;
        projectile.gravel = true;
        projectile.UsePlasma(boltColor, boltThickness);
        projectile.minBoltLength = boltLength;
        projectile.maxBoltLength = boltMaxLength;
        projectile.ReleaseFromMuzzle(origin.position, direction);
        SpawnFlash(origin);
    }

    void SpawnOrb()
    {
        Transform origin = muzzle != null ? muzzle : transform;
        Vector3 direction = origin.forward;
        PlayClip("Audio/ChargedPistolShot");
        var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        orb.name = "ShockOrb";
        orb.transform.position = origin.position + direction * 0.2f;
        orb.transform.localScale = Vector3.one * (orbRadius * 2f);
        StripCollider(orb);

        var projectile = orb.AddComponent<Hd2ShockOrb>();
        projectile.velocity = direction * orbSpeed;
        projectile.owner = Hd2SpawnField.FighterBody(this);
        projectile.color = boltColor;
        projectile.innerRadius = innerRadius;
        projectile.innerDamage = innerDamage;
        projectile.outerRadius = outerRadius;
        projectile.outerDamage = outerDamage;
        projectile.Begin();
        projectile.Paint();
        SpawnFlash(origin);
    }

    void SpawnFlash(Transform barrel)
    {
        if (barrel == null)
            return;

        Vector3 scale = barrel.lossyScale;
        float Axis(float value, float world)
        {
            return Mathf.Abs(value) > 0.001f ? world / value : world;
        }

        Hd2MuzzleFlash.Show(
            barrel,
            new Vector3(0f, 0f, Axis(scale.z, 0.06f)),
            new Vector3(Axis(scale.x, 0.08f), Axis(scale.y, 0.08f), Axis(scale.z, 0.22f)),
            boltColor,
            0.08f);
    }

    void PlayCharge()
    {
        if (chargeSource == null || chargeSource.clip == null || reloading || rounds <= 0)
            return;
        chargeSource.Play();
    }

    void StopCharge()
    {
        if (chargeSource != null && chargeSource.isPlaying)
            chargeSource.Stop();
    }

    void PlayClip(string path)
    {
        AudioClip clip = Resources.Load<AudioClip>(path);
        if (voice != null && clip != null)
            voice.PlayOneShot(clip);
    }

    void EnsureAmmoLabel()
    {
        ammoLabel = Hd2AmmoReadout.Bind(transform);
        if (ammoLabel == null)
        {
            var root = new GameObject("AmmoReadout");
            root.transform.SetParent(transform, false);
            root.transform.localPosition = new Vector3(0f, 0.04f, -0.06f);
            root.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            ammoLabel = root.AddComponent<TextMesh>();
            ammoLabel.fontSize = 32;
            ammoLabel.characterSize = 0.005f;
            ammoLabel.anchor = TextAnchor.MiddleCenter;
            ammoLabel.alignment = TextAlignment.Center;
            Hd2AmmoReadout.Style(ammoLabel);
        }

        RefreshAmmo();
    }

    void RefreshAmmo()
    {
        Hd2AmmoReadout.Show(ammoLabel, rounds, reloading);
    }

    static readonly List<UnityEngine.XR.InputDevice> hapticDevices = new List<UnityEngine.XR.InputDevice>();

    void StopChargeRumble()
    {
        if (!chargeRumbling)
            return;
        chargeRumbling = false;
        Pulse(0f, 0.02f);
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

    Hd2ControlMap map;

    Hd2ControlMap Map()
    {
        if (map == null)
            map = GetComponentInParent<Hd2ControlMap>();
        return map;
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

    bool Released(Hd2ControlMap.Action action, InputAction fallback)
    {
        Hd2ControlMap controls = Map();
        if (controls != null)
            return controls.Released(action);
        return fallback != null && fallback.WasReleasedThisFrame();
    }

    static bool EditorFallback()
    {
        var displays = new List<XRDisplaySubsystem>();
        SubsystemManager.GetSubsystems(displays);
        for (int i = 0; i < displays.Count; i++)
        {
            if (displays[i].running)
                return false;
        }

        return true;
    }

    static void Dispose(ref InputAction action)
    {
        if (action == null)
            return;
        action.Disable();
        action.Dispose();
        action = null;
    }

    static void StripCollider(GameObject target)
    {
        var collider = target.GetComponent<Collider>();
        if (collider != null)
            Destroy(collider);
    }

    void EnsureChargeGlow()
    {
        chargeGlow = FindNamed(transform, "glowyChargeUp");
        if (chargeGlow == null)
            return;

        chargeGlowEnd = FindGlowEnd(transform, chargeGlow);
        chargeEndLocal = chargeGlowEnd != null
            ? chargeGlow.InverseTransformPoint(chargeGlowEnd.position)
            : Vector3.zero;

        chargeSparks = chargeGlow.GetComponent<ParticleSystem>();
        if (chargeSparks == null)
            chargeSparks = chargeGlow.gameObject.AddComponent<ParticleSystem>();
        ConfigureChargeSparks(chargeSparks);

        Transform core = chargeGlow.Find("GlowCore");
        if (core == null)
        {
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "GlowCore";
            sphere.transform.SetParent(chargeGlow, false);
            StripCollider(sphere);
            core = sphere.transform;
        }

        chargeCore = core;
        chargeCore.localPosition = chargeEndLocal * 0.5f;
        chargeCore.localRotation = chargeEndLocal.sqrMagnitude > 0.0001f
            ? Quaternion.FromToRotation(Vector3.up, chargeEndLocal)
            : Quaternion.identity;
        chargeCore.localScale = Vector3.zero;
        chargeCoreRenderer = chargeCore.GetComponent<Renderer>();
        if (chargeCoreRenderer != null)
        {
            chargeCoreMaterial = ChargeGlowMaterial();
            if (chargeCoreMaterial != null)
                chargeCoreRenderer.sharedMaterial = chargeCoreMaterial;
            chargeCoreRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            chargeCoreRenderer.receiveShadows = false;
            chargeCoreRenderer.enabled = false;
        }

        chargeLight = chargeCore.GetComponent<Light>();
        if (chargeLight == null)
            chargeLight = chargeCore.gameObject.AddComponent<Light>();
        chargeLight.type = LightType.Point;
        chargeLight.color = ChargeRed;
        chargeLight.shadows = LightShadows.None;
        chargeLight.bounceIntensity = 0f;
        chargeLight.renderMode = LightRenderMode.ForcePixel;
        chargeLight.renderingLayerMask = (int)GlowLightMask();
        chargeLight.cullingMask = ~0;
        chargeLight.range = 0.6f;
        chargeLight.intensity = 0f;
        chargeLight.enabled = false;
    }

    static Material ChargeGlowMaterial()
    {
        Material source = Resources.Load<Material>("ShockOrbGlow");
        Material material = source != null ? new Material(source) : null;
        if (material == null)
        {
            Shader shader = Shader.Find("HD2/ShockOrbGlow");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                return null;
            material = new Material(shader);
        }

        if (material.HasProperty("_Color"))
            material.SetColor("_Color", ChargeRed);
        if (material.HasProperty("_Core"))
            material.SetColor("_Core", Color.white);
        return material;
    }

    uint GlowLightMask()
    {
        Transform barrel = FindNamed(transform, "Barrel");
        Renderer renderer = barrel != null ? barrel.GetComponent<Renderer>() : null;
        if (renderer != null && renderer.renderingLayerMask != 0)
            return renderer.renderingLayerMask;
        return 1;
    }

    static Transform FindGlowEnd(Transform root, Transform first)
    {
        if (root != first && root.name.StartsWith("glowyChargeUp"))
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindGlowEnd(root.GetChild(i), first);
            if (found != null)
                return found;
        }

        return null;
    }

    float GlowLocalSize(float worldSize)
    {
        float unit = chargeGlow != null ? Mathf.Abs(chargeGlow.lossyScale.x) : 1f;
        if (unit < 0.0001f)
            unit = 1f;
        return worldSize / unit;
    }

    void ConfigureChargeSparks(ParticleSystem sparks)
    {
        sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = sparks.main;
        main.playOnAwake = false;
        main.loop = true;
        main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.08f, 0.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.35f, 0.9f);
        main.startSize = new ParticleSystem.MinMaxCurve(GlowLocalSize(0.0035f), GlowLocalSize(0.007f));
        main.startColor = Color.white;
        main.gravityModifier = 0.15f;
        main.maxParticles = 36;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;

        var emission = sparks.emission;
        emission.rateOverTime = 0f;

        var shape = sparks.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.position = chargeEndLocal * 0.5f;
        shape.rotation = chargeEndLocal.sqrMagnitude > 0.0001f
            ? Quaternion.FromToRotation(Vector3.right, chargeEndLocal.normalized).eulerAngles
            : Vector3.zero;
        shape.scale = new Vector3(Mathf.Max(0.001f, chargeEndLocal.magnitude), 0.004f, 0.004f);
        shape.randomDirectionAmount = 1f;

        var velocity = sparks.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        // All three axes stay on one constant. A two-constant range on any axis
        // makes Unity reject the velocity until every axis matches.
        velocity.x = new ParticleSystem.MinMaxCurve(0f);
        velocity.y = new ParticleSystem.MinMaxCurve(0.3f);
        velocity.z = new ParticleSystem.MinMaxCurve(0f);

        var color = sparks.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f)
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 0.35f),
                new GradientAlphaKey(0f, 1f)
            });
        color.color = gradient;

        var size = sparks.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.35f),
            new Keyframe(0.12f, 1f),
            new Keyframe(1f, 0f)));

        var custom = sparks.customData;
        custom.SetMode(ParticleSystemCustomData.Custom1, ParticleSystemCustomDataMode.Vector);
        custom.SetVectorComponentCount(ParticleSystemCustomData.Custom1, 1);
        custom.SetVector(ParticleSystemCustomData.Custom1, 0, new ParticleSystem.MinMaxCurve(0f, 1f));

        var renderer = sparks.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.maxParticleSize = 0.012f;
        renderer.minParticleSize = 0f;
        var streams = new System.Collections.Generic.List<ParticleSystemVertexStream>(4);
        streams.Add(ParticleSystemVertexStream.Position);
        streams.Add(ParticleSystemVertexStream.Color);
        streams.Add(ParticleSystemVertexStream.UV);
        streams.Add(ParticleSystemVertexStream.Custom1X);
        renderer.SetActiveVertexStreams(streams);
        chargeSparkMaterial = ChargeSparkMaterial(out ownSparkMaterial);
        if (chargeSparkMaterial != null)
            renderer.sharedMaterial = chargeSparkMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    static Material ChargeSparkMaterial(out bool owned)
    {
        owned = false;
        Material source = Resources.Load<Material>("ChargeSpark");
        if (source != null)
            return source;

        Shader shader = Shader.Find("HD2/ChargeSpark");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null)
            return null;

        owned = true;
        var material = new Material(shader);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", Color.white);
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", Color.white);
        return material;
    }

    void UpdateChargeGlow()
    {
        if (chargeGlow == null)
            return;

        float amount = ChargeGlowAmount();
        if (amount <= 0f && chargeShown <= 0f)
            return;

        chargeShown = amount;
        // Ease out so the glow is readable during the hold, and still peaks when the shot is ready.
        float hot = amount * (2f - amount);
        bool on = hot > 0.001f;
        float span = chargeEndLocal.magnitude;
        if (span < 0.001f)
            span = 0.04f;
        float reach = span * hot;
        Vector3 mid = chargeEndLocal * 0.5f;
        if (chargeLight != null)
        {
            chargeLight.enabled = on;
            chargeLight.intensity = 70f * hot;
        }

        if (chargeCore != null)
        {
            chargeCore.localPosition = mid;
            float thick = reach * 0.55f;
            chargeCore.localScale = new Vector3(thick, reach, thick);
            if (chargeCoreRenderer != null)
                chargeCoreRenderer.enabled = on;
            if (chargeCoreMaterial != null)
            {
                chargeCoreMaterial.SetColor("_Color", ChargeRed * hot);
                chargeCoreMaterial.SetColor("_Core", Color.white * hot);
            }
        }

        if (chargeSparks == null)
            return;

        var shape = chargeSparks.shape;
        shape.position = mid;
        shape.scale = new Vector3(Mathf.Max(0.001f, reach), 0.004f, 0.004f);
        var emission = chargeSparks.emission;
        emission.rateOverTime = 48f * hot;
        if (on)
        {
            if (!chargeSparks.isPlaying)
                chargeSparks.Play();
        }
        else if (chargeSparks.isEmitting)
            chargeSparks.Stop(false, ParticleSystemStopBehavior.StopEmitting);
    }

    float ChargeGlowAmount()
    {
        if (triggerPressedAt < 0f || reloading || rounds <= 0)
            return 0f;

        return Mathf.Clamp01((Time.time - triggerPressedAt) / Mathf.Max(0.05f, chargeHoldTime));
    }

    static Transform FindNamed(Transform root, string name)
    {
        if (root.name == name)
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindNamed(root.GetChild(i), name);
            if (found != null)
                return found;
        }

        return null;
    }

    void OnDestroy()
    {
        if (chargeCoreMaterial != null)
            Destroy(chargeCoreMaterial);
        if (ownSparkMaterial && chargeSparkMaterial != null)
            Destroy(chargeSparkMaterial);
    }
}

/// <summary>
/// Slow red sphere. It bursts on the first thing it hits, and when it runs out of flight.
/// Inside 1.5 m deals the inner damage. Out to 2.75 m deals the outer damage.
/// </summary>
public class Hd2ShockOrb : MonoBehaviour
{
    public Vector3 velocity;
    public float lifetime = 3f;
    public Transform owner;
    public Color color = new Color(1f, 0.16f, 0.08f, 1f);
    public float innerRadius = 1.5f;
    public float innerDamage = 72f;
    public float outerRadius = 2.75f;
    public float outerDamage = 35f;
    public float armDistance = 0.8f;

    float age;
    float traveled;
    bool exploded;
    bool nearMissPlayed;
    Material solidMaterial;
    Material glowMaterial;
    readonly Vector3[] flight = new Vector3[96];
    int flightCount;
    float flightSpacing = 0.45f;

    public void Begin()
    {
        flightCount = 0;
        flightSpacing = 0.45f;
        Remember(transform.position);
    }

    void Remember(Vector3 point)
    {
        if (flightCount > 0 && (point - flight[flightCount - 1]).sqrMagnitude < flightSpacing * flightSpacing)
            return;
        if (flightCount >= flight.Length)
            ThinFlight();
        flight[flightCount++] = point;
    }

    void ThinFlight()
    {
        int kept = 0;
        for (int i = 0; i < flightCount; i += 2)
            flight[kept++] = flight[i];
        if ((flightCount & 1) == 0)
            flight[kept++] = flight[flightCount - 1];
        flightCount = kept;
        flightSpacing *= 2f;
    }

    void RememberEnd(Vector3 point)
    {
        if (flightCount > 0 && (point - flight[flightCount - 1]).sqrMagnitude < 0.0004f)
            return;
        if (flightCount >= flight.Length)
            ThinFlight();
        flight[flightCount++] = point;
    }

    public void Paint()
    {
        var renderer = GetComponent<Renderer>();
        if (renderer == null)
            return;

        Color solid = color;
        solid.a = 1f;
        solidMaterial = OrbMaterial("ShockOrb", "HD2/ShockOrb", solid);
        if (solidMaterial == null)
            return;

        renderer.sharedMaterial = solidMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        var halo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        halo.name = "Glow";
        halo.transform.SetParent(transform, false);
        halo.transform.localScale = Vector3.one * 1.75f;
        Collider haloCollider = halo.GetComponent<Collider>();
        if (haloCollider != null)
            Destroy(haloCollider);
        var glowRenderer = halo.GetComponent<Renderer>();
        glowMaterial = OrbMaterial("ShockOrbGlow", "HD2/ShockOrbGlow", solid);
        if (glowMaterial == null || glowRenderer == null)
            return;
        glowRenderer.sharedMaterial = glowMaterial;
        glowRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        glowRenderer.receiveShadows = false;
    }

    static Material OrbMaterial(string resource, string shaderName, Color solid)
    {
        Material source = Resources.Load<Material>(resource);
        Material material = source != null ? new Material(source) : null;
        if (material == null)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                return null;
            material = new Material(shader);
        }

        if (material.HasProperty("_Color"))
            material.SetColor("_Color", solid);
        if (material.HasProperty("_Core"))
            material.SetColor("_Core", Color.white);
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", solid);
        return material;
    }

    void OnDestroy()
    {
        if (solidMaterial != null)
            Destroy(solidMaterial);
        if (glowMaterial != null)
            Destroy(glowMaterial);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        age += dt;
        Vector3 step = velocity * dt;
        float distance = step.magnitude;
        if (distance > 0.0001f)
        {
            Vector3 origin = transform.position;
            Vector3 end = origin + step;
            if (Hit(origin, step / distance, distance, out RaycastHit hit))
            {
                if (!nearMissPlayed && Hd2NearMiss.TryPass(origin, hit.point, owner, Hd2NearMiss.HitsPlayer(hit.collider)))
                    nearMissPlayed = true;
                Burst(hit.point);
                return;
            }

            if (!nearMissPlayed && Hd2NearMiss.TryPass(origin, end, owner, false))
                nearMissPlayed = true;
        }

        transform.position += step;
        traveled += distance;
        Remember(transform.position);
        if (age >= lifetime)
            Burst(transform.position);
    }

    bool Hit(Vector3 origin, Vector3 direction, float distance, out RaycastHit closest)
    {
        closest = default;
        RaycastHit[] hits = Physics.RaycastAll(origin, direction, distance, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        bool found = false;
        for (int i = 0; i < hits.Length; i++)
        {
            Transform hitTransform = hits[i].transform;
            if (hitTransform.IsChildOf(transform))
                continue;
            if (owner != null && hitTransform.IsChildOf(owner) && traveled < armDistance)
                continue;

            var field = hits[i].collider.GetComponentInParent<Hd2SpawnField>();
            if (field != null && field.ShotPasses(origin))
                continue;

            if (hits[i].distance < best)
            {
                best = hits[i].distance;
                closest = hits[i];
                found = true;
            }
        }

        return found;
    }

    void Burst(Vector3 point)
    {
        if (exploded)
            return;

        exploded = true;
        RememberEnd(point);
        var seen = new HashSet<Hd2Health>();
        Collider[] colliders = Physics.OverlapSphere(point, outerRadius, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] == null)
                continue;

            Hd2Health health = colliders[i].GetComponentInParent<Hd2Health>();
            if (health == null || !seen.Add(health))
                continue;
            if (health.IsAlly(owner))
                continue;

            float reach = Vector3.Distance(colliders[i].ClosestPoint(point), point);
            float amount = 0f;
            if (reach <= innerRadius)
                amount = innerDamage;
            else if (reach <= outerRadius)
                amount = outerDamage;
            if (amount <= 0f)
                continue;

            if (owner != null)
                health.LastAttacker = owner;
            health.MarkHit(colliders[i].ClosestPoint(point));
            health.MarkBlast(point);
            if (health.Health > 0f && health.Health <= amount)
                health.MarkKillPath(flight, flightCount);
            health.ApplyHit(amount, false, true);
        }

        Hd2Explosion.Shock(point, color);

        AudioClip clip = Resources.Load<AudioClip>("Audio/RocketExplosion");
        if (clip != null)
        {
            var sound = new GameObject("ShockBurstSound");
            sound.transform.position = point;
            var source = sound.AddComponent<AudioSource>();
            source.spatialBlend = 1f;
            source.dopplerLevel = 0f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 4f;
            source.maxDistance = 36f;
            source.PlayOneShot(clip);
            Destroy(sound, clip.length + 0.1f);
        }

        Destroy(gameObject);
    }
}
