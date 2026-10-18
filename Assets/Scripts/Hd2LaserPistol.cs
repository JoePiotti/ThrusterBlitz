using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

/// <summary>
/// Stand-in laser pistol. Same gripped pistol, tinted green, until it has its own model.
/// A full trigger pull draws a solid green beam to the hit and starts the pistol charge.
/// Letting go before chargeSeconds cancels with no cooldown. Holding through that second
/// thickens the beam, deals damage once, then the beam lasts beamSeconds and the gun
/// waits cooldownSeconds. A trigger that is still held when that wait ends starts the
/// pointer again. Discharge and cooldown clips are optional until they exist.
/// </summary>
public class Hd2LaserPistol : MonoBehaviour
{
    public enum Phase
    {
        Ready = 0,
        Pointing = 1,
        Firing = 2,
        Cooling = 3
    }

    public Hd2Pistol.Hand hand = Hd2Pistol.Hand.Right;

    [Tooltip("Full pull. A lighter touch does not show the beam.")]
    public float fullTrigger = 0.9f;
    public float chargeSeconds = 1f;
    public float beamSeconds = 0.5f;
    public float cooldownSeconds = 1.5f;
    public float damage = 80f;
    public float pointerWidth = 0.015f;
    public float reach = 200f;
    public float kickDegrees = 4f;
    public float kickReturnSpeed = 18f;

    public Transform muzzle;

    static readonly Color BodyColor = new Color(0.1f, 0.55f, 0.18f, 1f);
    static readonly Color BeamColor = new Color(0.3f, 1f, 0.35f, 1f);

    Phase phase = Phase.Ready;
    bool waitForTriggerRelease = true;
    public bool botHeld;
    float pointStartedAt;
    float phaseEndsAt;
    float kick;
    Quaternion restLocalRotation;
    LineRenderer pointer;
    LineRenderer shot;
    InputAction triggerAxis;
    InputAction editorFire;
    AudioSource chargeSource;
    AudioSource voice;
    AudioClip fireClip;
    AudioClip cooldownClip;
    Hd2ControlMap controlMap;
    readonly RaycastHit[] hits = new RaycastHit[32];

    static readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();

    public Phase CurrentPhase => phase;

    void Awake()
    {
        if (muzzle == null)
        {
            Transform found = transform.Find("Muzzle");
            if (found != null)
                muzzle = found;
        }

        TintGreen(gameObject);
        restLocalRotation = transform.localRotation;
        pointer = CreateBeam("LaserPointer", false);
        shot = CreateBeam("LaserShot", true);

        AudioClip charge = Resources.Load<AudioClip>("Audio/PistolCharge");
        fireClip = Resources.Load<AudioClip>("Audio/LaserFire");
        cooldownClip = Resources.Load<AudioClip>("Audio/LaserReload");
        if (charge != null)
        {
            chargeSource = AddVoice();
            chargeSource.clip = charge;
            chargeSource.loop = false;
        }

        if (fireClip != null || cooldownClip != null)
            voice = AddVoice();
    }

    public void SetHand(Hd2Pistol.Hand value)
    {
        hand = value;
        if (isActiveAndEnabled)
            BindInputs();
    }

    public static void TintGreen(GameObject root)
    {
        if (root == null)
            return;

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || renderer is ParticleSystemRenderer || renderer is LineRenderer)
                continue;
            if (renderer.GetComponent<TextMesh>() != null)
                continue;

            Material source = renderer.sharedMaterial;
            Material copy;
            if (source == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                    continue;
                copy = new Material(shader);
            }
            else
            {
                copy = new Material(source);
                copy.SetTexture("_BaseMap", null);
                copy.SetTexture("_MainTex", null);
            }
            if (copy.HasProperty("_BaseColor"))
                copy.SetColor("_BaseColor", BodyColor);
            if (copy.HasProperty("_Color"))
                copy.SetColor("_Color", BodyColor);
            renderer.material = copy;
        }
    }

    LineRenderer CreateBeam(string name, bool world)
    {
        var lineObject = new GameObject(name);
        Transform parent = !world && muzzle != null ? muzzle : transform;
        lineObject.transform.SetParent(parent, false);
        lineObject.transform.localPosition = Vector3.zero;
        lineObject.transform.localRotation = Quaternion.identity;
        FitScale(lineObject.transform, parent);

        var line = lineObject.AddComponent<LineRenderer>();
        line.useWorldSpace = world;
        line.positionCount = 2;
        line.numCapVertices = 4;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.startWidth = 1f;
        line.endWidth = 1f;
        line.widthMultiplier = pointerWidth;
        Shader shader = Shader.Find("Sprites/Default");
        if (shader != null)
            line.material = new Material(shader);
        line.startColor = BeamColor;
        line.endColor = BeamColor;
        line.enabled = false;
        return line;
    }

    static void FitScale(Transform line, Transform parent)
    {
        Vector3 scale = parent.lossyScale;
        line.localScale = new Vector3(
            1f / Mathf.Max(0.0001f, scale.x),
            1f / Mathf.Max(0.0001f, scale.y),
            1f / Mathf.Max(0.0001f, scale.z));
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
        BindInputs();
    }

    void BindInputs()
    {
        Dispose(ref triggerAxis);
        Dispose(ref editorFire);

        string node = hand == Hd2Pistol.Hand.Left ? "LeftHand" : "RightHand";
        triggerAxis = new InputAction("Hd2LaserPull" + node, InputActionType.Value);
        triggerAxis.expectedControlType = "Axis";
        triggerAxis.AddBinding("<XRController>{" + node + "}/trigger");
        triggerAxis.Enable();

        if (hand == Hd2Pistol.Hand.Right)
        {
            editorFire = new InputAction("Hd2LaserEditor", InputActionType.Button);
            editorFire.AddBinding("<Mouse>/rightButton");
            editorFire.Enable();
        }
    }

    void OnDisable()
    {
        StopCharge();
        if (pointer != null)
            pointer.enabled = false;
        if (shot != null)
            shot.enabled = false;
        Dispose(ref triggerAxis);
        Dispose(ref editorFire);
    }

    void Update()
    {
        bool menuOpen = Map() != null && Map().MenuOpen;
        float pull = menuOpen ? 0f : ReadPull();
        if (botHeld)
            pull = 1f;
        bool full = pull >= fullTrigger;
        if (botHeld)
            waitForTriggerRelease = false;
        else if (waitForTriggerRelease)
        {
            if (pull >= 0.2f)
                full = false;
            else
                waitForTriggerRelease = false;
        }

        if (phase == Phase.Pointing)
        {
            if (!full)
                CancelPoint();
            else if (Time.time - pointStartedAt >= chargeSeconds)
                Discharge();
        }
        else if (phase == Phase.Firing)
        {
            if (Time.time >= phaseEndsAt)
                BeginCooldown();
        }
        else if (phase == Phase.Cooling && Time.time >= phaseEndsAt)
        {
            phase = Phase.Ready;
        }

        if (phase == Phase.Ready && full)
            BeginPoint();

        if (kick > 0f)
        {
            kick = Mathf.MoveTowards(kick, 0f, kickReturnSpeed * Time.deltaTime);
            transform.localRotation = restLocalRotation * Quaternion.Euler(-kick, 0f, 0f);
        }
    }

    void LateUpdate()
    {
        if (phase == Phase.Pointing)
            AimPointer();
    }

    void BeginPoint()
    {
        phase = Phase.Pointing;
        pointStartedAt = Time.time;
        PlayCharge();
        AimPointer();
    }

    void CancelPoint()
    {
        phase = Phase.Ready;
        StopCharge();
        if (pointer != null)
            pointer.enabled = false;
    }

    void Discharge()
    {
        StopCharge();
        Vector3 origin = MuzzlePosition();
        Vector3 direction = MuzzleForward();
        Vector3 end = origin + direction * 80f;
        Hd2Health target = null;
        RaycastHit hit = default;
        if (Trace(origin, direction, out hit))
        {
            end = hit.point;
            if (hit.collider != null)
                target = hit.collider.GetComponentInParent<Hd2Health>();
        }

        if (pointer != null)
            pointer.enabled = false;
        PlaceShot(origin, end);
        Hd2NearMiss.TryPass(origin, end, transform.root, target != null && Hd2NearMiss.HitsPlayer(target));

        Hd2Health attacker = GetComponentInParent<Hd2Health>();
        if (target != null && target != attacker)
        {
            if (attacker != null)
                target.LastAttacker = attacker.transform;
            target.MarkHit(hit.point);
            target.ApplyHit(damage, false, true);
        }

        if (voice != null && fireClip != null)
            voice.PlayOneShot(fireClip);
        Pulse(0.85f, 0.08f);
        kick = kickDegrees;
        phase = Phase.Firing;
        phaseEndsAt = Time.time + Mathf.Max(0f, beamSeconds);
    }

    void BeginCooldown()
    {
        if (shot != null)
            shot.enabled = false;
        if (voice != null && cooldownClip != null)
            voice.PlayOneShot(cooldownClip);
        phase = Phase.Cooling;
        phaseEndsAt = Time.time + Mathf.Max(0f, cooldownSeconds);
    }

    void AimPointer()
    {
        if (pointer == null)
            return;

        Transform parent = muzzle != null ? muzzle : transform;
        if (pointer.transform.parent != parent)
        {
            pointer.transform.SetParent(parent, false);
            pointer.transform.localPosition = Vector3.zero;
            pointer.transform.localRotation = Quaternion.identity;
            FitScale(pointer.transform, parent);
        }

        Vector3 origin = MuzzlePosition();
        Vector3 direction = MuzzleForward();
        float distance = 80f;
        if (Trace(origin, direction, out RaycastHit hit))
            distance = Mathf.Max(0.05f, hit.distance);

        pointer.enabled = true;
        pointer.useWorldSpace = false;
        pointer.widthMultiplier = pointerWidth;
        pointer.startColor = BeamColor;
        pointer.endColor = BeamColor;
        pointer.SetPosition(0, Vector3.zero);
        pointer.SetPosition(1, new Vector3(0f, 0f, distance));
    }

    void PlaceShot(Vector3 origin, Vector3 end)
    {
        if (shot == null)
            return;

        shot.enabled = true;
        shot.useWorldSpace = true;
        shot.widthMultiplier = pointerWidth * 3f;
        shot.startColor = BeamColor;
        shot.endColor = BeamColor;
        shot.SetPosition(0, origin);
        shot.SetPosition(1, end);
    }

    bool Trace(Vector3 origin, Vector3 direction, out RaycastHit closest)
    {
        closest = default;
        int count = Physics.RaycastNonAlloc(origin, direction, hits, reach, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        bool found = false;
        Transform owner = transform.root;
        for (int i = 0; i < count; i++)
        {
            Collider collider = hits[i].collider;
            if (collider == null)
                continue;
            if (collider.transform.IsChildOf(transform) || collider.transform.IsChildOf(owner))
                continue;
            if (hits[i].distance >= best)
                continue;
            best = hits[i].distance;
            closest = hits[i];
            found = true;
        }

        return found;
    }

    Vector3 MuzzlePosition()
    {
        return muzzle != null ? muzzle.position : transform.position;
    }

    Vector3 MuzzleForward()
    {
        Vector3 direction = muzzle != null ? muzzle.forward : -transform.up;
        if (direction.sqrMagnitude < 0.0001f)
            direction = transform.forward;
        return direction.normalized;
    }

    float ReadPull()
    {
        if (EditorFallback)
        {
            if (hand == Hd2Pistol.Hand.Right && editorFire != null && editorFire.IsPressed())
                return 1f;
            return 0f;
        }

        if (triggerAxis == null)
            return 0f;
        return Mathf.Clamp01(triggerAxis.ReadValue<float>());
    }

    void PlayCharge()
    {
        if (chargeSource == null || chargeSource.clip == null)
            return;
        chargeSource.Play();
    }

    void StopCharge()
    {
        if (chargeSource != null && chargeSource.isPlaying)
            chargeSource.Stop();
    }

    void Pulse(float amplitude, float duration)
    {
        if (amplitude <= 0f || duration <= 0f || GetComponentInParent<Hd2EnemyBot>() != null)
            return;

        var node = hand == Hd2Pistol.Hand.Left ? XRNode.LeftHand : XRNode.RightHand;
        var devices = new List<UnityEngine.XR.InputDevice>();
        InputDevices.GetDevicesAtXRNode(node, devices);
        for (int i = 0; i < devices.Count; i++)
        {
            UnityEngine.XR.InputDevice device = devices[i];
            if (device.TryGetHapticCapabilities(out HapticCapabilities caps) && caps.supportsImpulse)
                device.SendHapticImpulse(0, Mathf.Clamp01(amplitude), duration);
        }
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

    static void Dispose(ref InputAction action)
    {
        if (action == null)
            return;
        action.Disable();
        action.Dispose();
        action = null;
    }
}
