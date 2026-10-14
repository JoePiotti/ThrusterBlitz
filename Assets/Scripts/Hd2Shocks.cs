using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

/// <summary>
/// Stand-in Shocks. Same gripped pistol, tinted red, until it has its own model.
/// A trigger tap fires one blue bolt. Holding the trigger for chargeHoldTime, then
/// releasing, fires a slower blue sphere that bursts.
/// </summary>
public class Hd2Shocks : MonoBehaviour
{
    public Hd2Pistol.Hand hand = Hd2Pistol.Hand.Right;

    public int magazineSize = 10;
    public float shotsPerSecond = 2f;
    public float reloadDuration = 1f;

    public float boltSpeed = 140f;
    public float boltDamage = 20f;
    public float boltThickness = 0.03f;
    public float boltLength = 3.6f;
    public float boltMaxLength = 5.5f;
    public Color boltColor = new Color(1f, 0.16f, 0.08f, 1f);

    public float chargeHoldTime = 1.25f;
    public float orbSpeed = 35f;
    public float orbRadius = 0.14f;
    public float innerRadius = 1.5f;
    public float innerDamage = 72f;
    public float outerRadius = 2.75f;
    public float outerDamage = 35f;

    public float kickDegrees = 4f;
    public float kickReturnSpeed = 18f;

    public Transform muzzle;

    static readonly Color BodyColor = new Color(0.78f, 0.08f, 0.07f, 1f);

    int rounds;
    float nextFireTime;
    float triggerPressedAt = -1f;
    bool waitForTriggerRelease = true;
    bool chargeReadyPulsed;
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

    void Awake()
    {
        if (muzzle == null)
        {
            Transform found = transform.Find("Muzzle");
            if (found != null)
                muzzle = found;
        }

        TintRed(gameObject);
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
    }

    public void SetHand(Hd2Pistol.Hand which)
    {
        hand = which;
    }

    public static void TintRed(GameObject root)
    {
        if (root == null)
            return;

        var renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || renderer is ParticleSystemRenderer)
                continue;
            if (renderer.GetComponent<TextMesh>() != null)
                continue;

            Material source = renderer.sharedMaterial;
            if (source == null)
                continue;

            var copy = new Material(source);
            copy.SetTexture("_BaseMap", null);
            copy.SetTexture("_MainTex", null);
            if (copy.HasProperty("_BaseColor"))
                copy.SetColor("_BaseColor", BodyColor);
            if (copy.HasProperty("_Color"))
                copy.SetColor("_Color", BodyColor);
            renderer.material = copy;
        }
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
            PlayCharge();
        }

        if (!chargeReadyPulsed && triggerPressedAt >= 0f && !reloading && rounds > 0
            && Time.time - triggerPressedAt >= chargeHoldTime)
        {
            chargeReadyPulsed = true;
            Pulse(0.5f, 0.16f);
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
        PlayClip("Audio/PistolShot");
        var projectile = Hd2Shot.Rent();
        projectile.gameObject.name = "ShockBolt";
        projectile.transform.position = origin.position;
        projectile.transform.rotation = Quaternion.LookRotation(direction);
        projectile.velocity = direction * boltSpeed;
        projectile.lifetime = 1.6f;
        projectile.owner = transform.root;
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
        projectile.owner = transform.root;
        projectile.color = boltColor;
        projectile.innerRadius = innerRadius;
        projectile.innerDamage = innerDamage;
        projectile.outerRadius = outerRadius;
        projectile.outerDamage = outerDamage;
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
        Transform existing = transform.Find("AmmoReadout");
        if (existing != null)
            ammoLabel = existing.GetComponent<TextMesh>();

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
        }

        ammoLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        ammoLabel.color = new Color(0.55f, 0.75f, 1f, 1f);
        var renderer = ammoLabel.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            if (ammoLabel.font != null)
                renderer.sharedMaterial = ammoLabel.font.material;
        }

        RefreshAmmo();
    }

    void RefreshAmmo()
    {
        if (ammoLabel != null)
            ammoLabel.text = reloading ? "R" : rounds.ToString();
    }

    void Pulse(float amplitude, float duration)
    {
        if (GetComponentInParent<Hd2EnemyBot>() != null)
            return;

        var node = hand == Hd2Pistol.Hand.Left ? XRNode.LeftHand : XRNode.RightHand;
        var devices = new List<UnityEngine.XR.InputDevice>();
        InputDevices.GetDevicesAtXRNode(node, devices);
        for (int i = 0; i < devices.Count; i++)
        {
            if (devices[i].TryGetHapticCapabilities(out HapticCapabilities caps) && caps.supportsImpulse)
                devices[i].SendHapticImpulse(0, Mathf.Clamp01(amplitude), duration);
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

    public void Paint()
    {
        var renderer = GetComponent<Renderer>();
        if (renderer == null)
            return;

        Material material = Resources.Load<Material>("PlasmaBolt");
        if (material != null)
            material = new Material(material);
        else
        {
            Shader shader = Shader.Find("HD2/PlasmaBolt");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                return;
            material = new Material(shader);
        }
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
        if (material.HasProperty("_Core"))
            material.SetColor("_Core", Color.white);
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
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
            if (Hit(origin, step / distance, distance, out RaycastHit hit))
            {
                Burst(hit.point);
                return;
            }
        }

        transform.position += step;
        traveled += distance;
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
        var seen = new HashSet<Hd2Health>();
        Collider[] colliders = Physics.OverlapSphere(point, outerRadius, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] == null)
                continue;

            Hd2Health health = colliders[i].GetComponentInParent<Hd2Health>();
            if (health == null || !seen.Add(health))
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
            health.ApplyHit(amount, false);
        }

        var burst = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        burst.name = "ShockBurst";
        var collider = burst.GetComponent<Collider>();
        if (collider != null)
            Destroy(collider);
        burst.transform.position = point;
        burst.transform.localScale = Vector3.one * 0.2f;
        var renderer = burst.GetComponent<Renderer>();
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (renderer != null && shader != null)
        {
            var material = new Material(shader);
            Color tint = color;
            tint.a = 0.55f;
            material.SetColor("_BaseColor", tint);
            material.color = tint;
            renderer.sharedMaterial = material;
        }

        var fade = burst.AddComponent<Hd2RocketBurst>();
        fade.diameter = outerRadius * 2f;
        fade.color = color;
        fade.alpha = 0.55f;

        AudioClip clip = Resources.Load<AudioClip>("Audio/RocketExplosion");
        if (clip != null)
        {
            var sound = new GameObject("ShockBurstSound");
            sound.transform.position = point;
            var source = sound.AddComponent<AudioSource>();
            source.spatialBlend = 1f;
            source.minDistance = 4f;
            source.maxDistance = 28f;
            source.PlayOneShot(clip);
            Destroy(sound, clip.length + 0.1f);
        }

        Destroy(gameObject);
    }
}
