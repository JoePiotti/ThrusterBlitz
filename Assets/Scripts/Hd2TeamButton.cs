using UnityEngine;

/// <summary>
/// Team button. ButtonShaft and ButtonTop sit under ButtonBase.
/// A living fighter's hand or arm collider depresses the shaft 20 cm,
/// plays a click, and claims the point. The claim stays after they let go.
/// While that fighter is alive and still touching, nobody else can take it.
/// </summary>
public class Hd2TeamButton : MonoBehaviour
{
    public const float DepressMeters = 0.2f;
    public const float BeamHeight = 80f;
    public const float RingRadius = 15f;
    const float RingPulseSeconds = 1.5f;
    const float RingThickness = 0.45f;
    const int RingSegments = 64;

    static readonly float[] ringCos = new float[RingSegments];
    static readonly float[] ringSin = new float[RingSegments];

    static readonly Color Red = new Color(0.85f, 0.12f, 0.12f, 1f);
    static readonly Color Blue = new Color(0.16f, 0.38f, 0.95f, 1f);
    static readonly Collider[] overlap = new Collider[24];
    static readonly Component[] touches = new Component[8];

    public static readonly System.Collections.Generic.List<Hd2TeamButton> All = new System.Collections.Generic.List<Hd2TeamButton>();

    [SerializeField]
    string pointLabel = "A";

    Transform shaft;
    Transform top;
    float restY;
    Vector3 volumeCenter;
    Vector3 volumeHalf;
    Component holder;
    Hd2Team owner = Hd2Team.None;
    Color glow = Color.white;
    Material glowMaterial;
    Renderer beamRenderer;
    Transform beamTransform;
    LineRenderer ring;
    float beamRadius;
    float pulse;
    AudioSource clickSource;
    AudioClip clickClip;
    bool painted;
    int touchCount;

    static Hd2TeamButton()
    {
        float step = Mathf.PI * 2f / RingSegments;
        for (int i = 0; i < RingSegments; i++)
        {
            float angle = i * step;
            ringCos[i] = Mathf.Cos(angle);
            ringSin[i] = Mathf.Sin(angle);
        }
    }

    public string Label => pointLabel;
    public Hd2Team Owner => owner;

    /// <summary>
    /// White until a team claims it, then that claim's color.
    /// </summary>
    public Color MarkerColor => owner == Hd2Team.None ? Color.white : glow;

    void OnEnable()
    {
        if (!All.Contains(this))
            All.Add(this);
    }

    void OnDisable()
    {
        All.Remove(this);
    }

    void Awake()
    {
        shaft = transform.Find("ButtonShaft");
        if (shaft == null)
            shaft = transform.Find("buttonShaft");
        if (shaft != null)
        {
            top = shaft.Find("ButtonTop");
            if (top == null)
                top = shaft.Find("buttonTop");
            restY = shaft.localPosition.y;
        }

        MeasureVolume();

        if (!Application.isPlaying)
            return;

        clickClip = Resources.Load<AudioClip>("Audio/ButtonClick");
        clickSource = gameObject.AddComponent<AudioSource>();
        clickSource.playOnAwake = false;
        clickSource.spatialBlend = 1f;
        clickSource.minDistance = 0.6f;
        clickSource.maxDistance = 24f;
        clickSource.dopplerLevel = 0f;

        Material source = Resources.Load<Material>("TeamButton");
        if (source != null)
            glowMaterial = new Material(source);
        BuildBeam();
    }

    void OnDestroy()
    {
        if (glowMaterial != null)
            Destroy(glowMaterial);
    }

    void Update()
    {
        if (!Application.isPlaying || shaft == null)
            return;

        bool held = false;
        if (!Hd2Domination.Decided)
        {
            Collect();
            Component next = NextHolder();
            if (next != holder)
            {
                holder = next;
                if (next != null)
                    Press(next);
            }

            held = holder != null;
        }

        Vector3 pos = shaft.localPosition;
        float target = held ? restY - DepressMeters : restY;
        pos.y = Mathf.MoveTowards(pos.y, target, 1.6f * Time.deltaTime);
        shaft.localPosition = pos;
        PulseRing();
    }

    void Press(Component body)
    {
        Hd2Team team = Hd2SpawnField.TeamOf(body);
        if (team == Hd2Team.Red || team == Hd2Team.Blue)
        {
            owner = team;
            glow = ColorOf(body, team);
            ApplyGlow();
        }

        if (clickClip != null && clickSource != null)
            clickSource.PlayOneShot(clickClip);
    }

    void ApplyGlow()
    {
        if (glowMaterial == null)
            return;

        if (glowMaterial.HasProperty("_BaseColor"))
            glowMaterial.SetColor("_BaseColor", glow);
        if (glowMaterial.HasProperty("_Color"))
            glowMaterial.SetColor("_Color", glow);

        if (!painted && beamRenderer != null)
        {
            painted = true;
            beamRenderer.sharedMaterial = glowMaterial;
        }

        if (beamRenderer != null)
            beamRenderer.enabled = true;
        if (ring != null)
            ring.enabled = true;
    }

    Component NextHolder()
    {
        if (holder != null && Living(holder) && Contains(holder))
            return holder;
        if (touchCount > 0)
            return touches[0];
        return null;
    }

    bool Contains(Component body)
    {
        for (int i = 0; i < touchCount; i++)
        {
            if (touches[i] == body)
                return true;
        }

        return false;
    }

    void Collect()
    {
        touchCount = 0;
        Vector3 scale = transform.lossyScale;
        Vector3 half = new Vector3(
            volumeHalf.x * Mathf.Abs(scale.x),
            volumeHalf.y * Mathf.Abs(scale.y),
            volumeHalf.z * Mathf.Abs(scale.z));
        int count = Physics.OverlapBoxNonAlloc(
            transform.TransformPoint(volumeCenter),
            half,
            overlap,
            transform.rotation,
            ~0,
            QueryTriggerInteraction.Collide);

        for (int i = 0; i < count; i++)
        {
            Collider collider = overlap[i];
            if (collider == null || collider is CharacterController)
                continue;
            if (collider.transform == transform || collider.transform.IsChildOf(transform))
                continue;

            Component body = LimbBody(collider.transform);
            if (body == null || !Living(body) || Contains(body))
                continue;
            if (touchCount >= touches.Length)
                break;
            touches[touchCount++] = body;
        }
    }

    static Component LimbBody(Transform bone)
    {
        bool limb = false;
        Transform cursor = bone;
        while (cursor != null)
        {
            if (IsLimb(cursor.name))
                limb = true;

            Hd2Locomotion player = cursor.GetComponent<Hd2Locomotion>();
            if (player != null)
                return limb ? player : null;

            Hd2EnemyBot bot = cursor.GetComponent<Hd2EnemyBot>();
            if (bot != null)
                return limb ? bot : null;

            cursor = cursor.parent;
        }

        return null;
    }

    static bool IsLimb(string name)
    {
        if (name == "LeftHand" || name == "RightHand")
            return true;
        if (name.StartsWith("Hand.") || name.StartsWith("Hand_"))
            return true;
        return name.StartsWith("Arm.") || name.StartsWith("Arm_");
    }

    static bool Living(Component body)
    {
        Hd2Health health = body.GetComponent<Hd2Health>();
        return health == null || !health.IsDead;
    }

    static Color ColorOf(Component body, Hd2Team team)
    {
        Hd2Locomotion player = body as Hd2Locomotion;
        if (player == null)
            player = body.GetComponent<Hd2Locomotion>();
        if (player != null && player.team == team)
            return player.teamColor;
        return team == Hd2Team.Red ? Red : Blue;
    }

    void MeasureVolume()
    {
        Vector3 shaftPos = shaft != null ? shaft.localPosition : new Vector3(0f, 0.57f, 0f);
        Vector3 topPos = shaftPos + Vector3.up * 0.11f;
        if (top != null)
            topPos = transform.InverseTransformPoint(top.position);

        Vector3 min = Vector3.Min(shaftPos, topPos) - new Vector3(0.16f, 0.18f, 0.16f);
        Vector3 max = Vector3.Max(shaftPos, topPos) + new Vector3(0.16f, 0.16f, 0.16f);
        min.y -= DepressMeters;
        volumeCenter = (min + max) * 0.5f;
        volumeHalf = (max - min) * 0.5f;
    }

    void BuildBeam()
    {
        float width = 0.5f;
        Renderer pedestal = GetComponent<Renderer>();
        if (pedestal != null)
            width = Mathf.Clamp(Mathf.Max(pedestal.bounds.size.x, pedestal.bounds.size.z), 0.2f, 2f);

        float bottom = 0.8f;
        if (top != null)
        {
            Renderer cap = top.GetComponent<Renderer>();
            if (cap != null)
            {
                Vector3 worldTop = cap.bounds.center;
                worldTop.y = cap.bounds.max.y;
                bottom = transform.InverseTransformPoint(worldTop).y;
            }
        }

        var beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        beam.name = "TeamBeam";
        beam.transform.SetParent(transform, false);
        var collider = beam.GetComponent<Collider>();
        if (collider != null)
            Destroy(collider);

        beam.transform.localRotation = Quaternion.identity;
        beam.transform.localScale = new Vector3(width, BeamHeight * 0.5f, width);
        beam.transform.localPosition = new Vector3(0f, bottom + BeamHeight * 0.5f, 0f);

        beamRenderer = beam.GetComponent<Renderer>();
        if (beamRenderer != null)
        {
            beamRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            beamRenderer.receiveShadows = false;
            if (glowMaterial != null)
                beamRenderer.sharedMaterial = glowMaterial;
            beamRenderer.enabled = false;
        }

        BuildRing(beam.transform);
    }

    void BuildRing(Transform beam)
    {
        beamTransform = beam;
        beamRadius = beam.lossyScale.x * 0.5f;

        var go = new GameObject("TeamRing");
        go.transform.SetParent(transform, false);
        ring = go.AddComponent<LineRenderer>();
        ring.positionCount = RingSegments;
        ring.loop = true;
        ring.useWorldSpace = true;
        ring.widthMultiplier = RingThickness;
        ring.numCornerVertices = 2;
        ring.numCapVertices = 0;
        ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ring.receiveShadows = false;
        ring.alignment = LineAlignment.View;
        ring.startColor = Color.white;
        ring.endColor = Color.white;
        if (glowMaterial != null)
            ring.sharedMaterial = glowMaterial;
        ring.enabled = false;
    }

    void PulseRing()
    {
        if (ring == null || beamTransform == null)
            return;

        bool show = beamRenderer != null && beamRenderer.enabled;
        if (ring.enabled != show)
            ring.enabled = show;
        if (!show)
            return;

        pulse += Time.deltaTime;
        if (pulse >= RingPulseSeconds)
            pulse -= RingPulseSeconds;

        float radius = Mathf.Lerp(beamRadius, RingRadius, pulse / RingPulseSeconds);
        Vector3 center = beamTransform.position + beamTransform.up * beamTransform.lossyScale.y;
        float y = center.y;
        for (int i = 0; i < RingSegments; i++)
        {
            ring.SetPosition(i, new Vector3(
                center.x + ringCos[i] * radius,
                y,
                center.z + ringSin[i] * radius));
        }
    }
}
