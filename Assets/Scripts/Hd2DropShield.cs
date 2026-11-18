using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// A hand-held generator. The trigger drops it straight down. It lands feet-down,
/// raises a golden bubble, and the gun that was in that hand comes back.
/// The bubble stops shots and blasts. Bodies walk and thrust through it.
/// </summary>
public class Hd2DropShield : MonoBehaviour
{
    public const float ShieldRadius = 3f;
    public const float ShieldSeconds = 30f;
    public const float ShieldHealth = 300f;
    public const float EmitterLimit = 60f;
    public const float FailSeconds = 5f;

    public Mesh bubbleMesh;
    public Material bubbleMaterial;

    enum Phase
    {
        Held,
        Falling,
        Live,
        Dying
    }

    const int SlotCount = 16;
    const int RipplePool = 6;
    const int HitVoices = 4;
    const float RippleLife = 0.5f;
    const float RippleStart = 0.12f;
    const float RippleEnd = 0.72f;
    static readonly Color RippleColor = new Color(0.45f, 0.86f, 1f, 1f);
    static readonly Hd2DropShield[] active = new Hd2DropShield[SlotCount];
    static readonly RaycastHit[] groundHits = new RaycastHit[16];
    static int activeCount;
    static Mesh sharedBubble;
    static Mesh rippleMesh;
    static Material rippleMaterial;
    static Material smokeMaterial;
    static bool layerOpen;

    Phase phase = Phase.Held;
    Hd2Pistol.Hand hand = Hd2Pistol.Hand.Right;
    Hd2ControlMap map;
    InputAction fireAction;
    InputAction editorFire;
    bool useEditorFire;
    bool gunReturned;
    GameObject[] stashed;
    int stashedCount;
    Transform bubble;
    MeshCollider bubbleCollider;
    CapsuleCollider emitterCollider;
    Light glow;
    float feetLocalY;
    float topLocalY;
    Vector3 headLocal;
    float emitterRadius = 0.06f;
    float fallSpeed;
    float fallTime;
    float life;
    float shieldDamage;
    float emitterDamage;
    AudioSource[] hitVoices;
    int hitVoice;
    AudioClip hitClip;
    AudioSource shieldOn;
    AudioSource shieldEnd;
    AudioClip shieldOnClip;
    AudioClip shieldEndClip;
    Ripple[] ripples;
    int liveRipples;
    bool measured;

    sealed class Ripple
    {
        public Transform transform;
        public Renderer renderer;
        public MaterialPropertyBlock block;
        public float age;
        public bool live;
    }

    void Awake()
    {
        stashed = new GameObject[8];
        bubble = transform.Find("Bubble");
        if (!Application.isPlaying)
            return;
        MeasureDevice();
        OpenLayer();
        EnsureShieldAudio();
    }

    void OnEnable()
    {
        if (!Application.isPlaying)
            return;
        for (int i = 0; i < activeCount; i++)
        {
            if (active[i] == this)
                return;
        }

        if (activeCount < active.Length)
            active[activeCount++] = this;
    }

    void OnDestroy()
    {
        if (!gunReturned)
            ReturnGun();
    }

    void OnDisable()
    {
        for (int i = 0; i < activeCount; i++)
        {
            if (active[i] != this)
                continue;
            activeCount--;
            active[i] = active[activeCount];
            active[activeCount] = null;
            break;
        }

        DisposeFire();
    }

    public void SetHand(Hd2Pistol.Hand value)
    {
        hand = value;
        phase = Phase.Held;
        if (bubble != null)
            bubble.gameObject.SetActive(false);
        BindFire();
    }

    public void StashWeapons(Transform handRoot)
    {
        Stash<Hd2Pistol>(handRoot);
        Stash<Hd2Smg>(handRoot);
        Stash<Hd2RocketLauncher>(handRoot);
        Stash<Hd2Shocks>(handRoot);
        Stash<Hd2LaserPistol>(handRoot);
        Stash<Hd2Shotgun>(handRoot);
        Stash<Hd2BurstPistol>(handRoot);
    }

    /// <summary>
    /// Another weapon is taking this hand. Drop the generator and leave the stashed gun alone.
    /// </summary>
    public static void DiscardHeld(Transform handRoot)
    {
        if (handRoot == null)
            return;
        Hd2DropShield[] held = handRoot.GetComponentsInChildren<Hd2DropShield>(true);
        for (int i = 0; i < held.Length; i++)
        {
            Hd2DropShield shield = held[i];
            if (shield == null || shield.phase != Phase.Held)
                continue;
            shield.Abandon();
        }
    }

    public void Abandon()
    {
        gunReturned = true;
        stashedCount = 0;
        if (Application.isPlaying)
            Destroy(gameObject);
        else
            DestroyImmediate(gameObject);
    }

    public bool IsHeld => phase == Phase.Held;
    public bool IsLive => phase == Phase.Live;
    public bool IsPlanted => phase == Phase.Falling || phase == Phase.Live;

    public Vector3 ShelterCenter => BubbleCenter();

    public float ShelterReach => BubbleRadius();

    public void BotDrop()
    {
        if (phase == Phase.Held)
            Release();
    }

    public static void IgnoreMover(Collider mover)
    {
        if (mover == null)
            return;
        for (int i = 0; i < activeCount; i++)
        {
            Hd2DropShield shield = active[i];
            if (shield == null || shield.phase != Phase.Live)
                continue;
            shield.IgnoreOne(mover);
        }
    }

    // A falling shield only lands on the level, never on a player or a bot. It falls
    // through them to the ground below.
    static bool IsCharacter(Collider collider)
    {
        if (collider is CharacterController)
            return true;
        return collider.GetComponentInParent<Hd2Locomotion>() != null
            || collider.GetComponentInParent<Hd2Body>() != null
            || collider.GetComponentInParent<Hd2EnemyBot>() != null
            || collider.GetComponentInParent<Hd2PracticeBot>() != null
            || collider.GetComponentInParent<Hd2NetPlayer>() != null;
    }

    public static bool PassesBody(Collider collider)
    {
        if (collider == null)
            return false;
        Hd2DropShield shield = collider.GetComponentInParent<Hd2DropShield>();
        return shield != null && shield.phase == Phase.Live;
    }

    public static float ExitDistance(Vector3 origin, Vector3 direction, float maxDistance)
    {
        float best = 0f;
        for (int i = 0; i < activeCount; i++)
        {
            Hd2DropShield shield = active[i];
            if (shield == null || shield.phase != Phase.Live)
                continue;
            float far = shield.FarExit(origin, direction);
            if (far > best && far <= maxDistance + shield.BubbleRadius() + 0.2f)
                best = far;
        }

        return best;
    }

    public static bool Blocks(Vector3 from, Vector3 to)
    {
        for (int i = 0; i < activeCount; i++)
        {
            Hd2DropShield shield = active[i];
            if (shield != null && shield.phase == Phase.Live && shield.Crosses(from, to))
                return true;
        }

        return false;
    }

    public static void ApplyBlast(Vector3 point, float innerRadius, float innerDamage, float outerRadius, float outerDamage)
    {
        for (int i = 0; i < activeCount; i++)
        {
            Hd2DropShield shield = active[i];
            if (shield == null || shield.phase != Phase.Live)
                continue;
            shield.TakeBlast(point, innerRadius, innerDamage, outerRadius, outerDamage);
        }
    }

    public void AbsorbShot(Collider collider, Vector3 point, Vector3 normal, Vector3 incoming, float amount)
    {
        if (phase != Phase.Live || collider == null)
            return;
        if (amount < 0f)
            amount = 0f;
        if (IsBubble(collider))
        {
            ShowRipple(point, normal, incoming);
            shieldDamage += amount;
            if (shieldDamage >= ShieldHealth)
                Fail();
            return;
        }

        Hd2Shot.Sparks(point, normal);
        emitterDamage += amount;
        if (emitterDamage > EmitterLimit)
            Fail();
    }

    public void NoteImpact(Collider collider, Vector3 point, Vector3 normal, Vector3 incoming)
    {
        if (phase != Phase.Live || collider == null)
            return;
        if (IsBubble(collider))
            ShowRipple(point, normal, incoming);
        else
            Hd2Shot.Sparks(point, normal);
    }

    void Update()
    {
        if (!Application.isPlaying)
            return;
        if (phase == Phase.Held)
        {
            if (FirePressed())
                Release();
            return;
        }

        if (phase == Phase.Falling)
        {
            TickFall();
            return;
        }

        if (phase != Phase.Live)
            return;

        TickRipples();
        life += Time.deltaTime;
        if (life >= ShieldSeconds)
            Fail();
    }

    void Release()
    {
        phase = Phase.Falling;
        fallSpeed = 0f;
        fallTime = 0f;
        if (map == null)
            map = GetComponentInParent<Hd2ControlMap>();
        float yaw = transform.eulerAngles.y;
        transform.SetParent(null, true);
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        if (bubble != null)
            bubble.gameObject.SetActive(false);
    }

    void TickFall()
    {
        float dt = Time.deltaTime;
        fallTime += dt;
        fallSpeed = Mathf.Min(30f, fallSpeed + 18f * dt);
        float step = fallSpeed * dt;
        Vector3 feet = transform.TransformPoint(new Vector3(0f, feetLocalY, 0f));
        int count = Physics.RaycastNonAlloc(feet, Vector3.down, groundHits, step + 0.08f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        float groundY = feet.y;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            Collider collider = groundHits[i].collider;
            if (collider == null || collider.transform == transform || collider.transform.IsChildOf(transform))
                continue;
            if (PassesBody(collider))
                continue;
            if (IsCharacter(collider))
                continue;
            if (groundHits[i].normal.y < 0.45f || groundHits[i].distance >= best)
                continue;
            best = groundHits[i].distance;
            groundY = groundHits[i].point.y;
            found = true;
        }

        if (found && best <= step + 0.06f)
        {
            Land(groundY);
            return;
        }

        transform.position += Vector3.down * step;
        if (fallTime < 4f)
            return;
        feet = transform.TransformPoint(new Vector3(0f, feetLocalY, 0f));
        Land(feet.y);
    }

    void Land(float groundY)
    {
        if (phase == Phase.Dying)
            return;
        phase = Phase.Live;
        transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        DropGrip();
        measured = false;
        MeasureDevice();
        Vector3 feet = transform.TransformPoint(new Vector3(0f, feetLocalY, 0f));
        transform.position += Vector3.up * (groundY - feet.y);
        ArmShield();
        ReturnGun();
        life = 0f;
        shieldDamage = 0f;
        emitterDamage = 0f;
    }

    void ArmShield()
    {
        OpenLayer();
        if (bubble == null)
            bubble = CreateBubble();
        float parent = Mathf.Max(0.0001f, Mathf.Abs(transform.lossyScale.x));
        bubble.localPosition = new Vector3(0f, topLocalY, 0f);
        bubble.localRotation = Quaternion.identity;
        bubble.localScale = Vector3.one * (ShieldRadius / parent);
        MeshFilter filter = bubble.GetComponent<MeshFilter>();
        Mesh mesh = filter != null ? filter.sharedMesh : null;
        if (mesh == null)
            mesh = bubbleMesh != null ? bubbleMesh : SharedBubble();
        if (filter == null)
            filter = bubble.gameObject.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        MeshRenderer renderer = bubble.GetComponent<MeshRenderer>();
        if (renderer == null)
            renderer = bubble.gameObject.AddComponent<MeshRenderer>();
        Material material = bubbleMaterial != null ? bubbleMaterial : renderer.sharedMaterial;
        if (material != null)
            renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        int layer = LayerMask.NameToLayer("DropShield");
        if (layer < 0)
            layer = 0;
        bubble.gameObject.layer = layer;
        bubbleCollider = bubble.GetComponent<MeshCollider>();
        if (bubbleCollider == null)
            bubbleCollider = bubble.gameObject.AddComponent<MeshCollider>();
        bubbleCollider.sharedMesh = mesh;
        bubbleCollider.convex = false;
        bubbleCollider.isTrigger = false;
        bubble.gameObject.SetActive(true);
        EnsureEmitter();
        if (emitterCollider != null)
            emitterCollider.enabled = true;
        IgnoreBodies();
        BuildVoices();
        BuildRipples();
        Hd2DropShieldInterior.Open(bubble, BubbleRadius());
        StartShieldBed();
        SetGlow(true);
    }

    void EnsureEmitter()
    {
        if (emitterCollider != null)
            return;
        var capsule = gameObject.AddComponent<CapsuleCollider>();
        float height = Mathf.Max(0.05f, topLocalY - feetLocalY);
        float radius = Mathf.Max(0.02f, emitterRadius);
        if (radius * 2f > height)
            height = radius * 2f + 0.001f;
        capsule.direction = 1;
        capsule.height = height;
        capsule.radius = Mathf.Min(radius, height * 0.5f - 0.0005f);
        capsule.center = new Vector3(0f, (feetLocalY + topLocalY) * 0.5f, 0f);
        capsule.isTrigger = false;
        emitterCollider = capsule;
    }

    void SetGlow(bool on)
    {
        if (glow == null)
        {
            var lamp = new GameObject("ShieldGlow");
            lamp.transform.SetParent(transform, false);
            glow = lamp.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = new Color(1f, 0.76f, 0.28f);
            glow.intensity = 18f;
            glow.shadows = LightShadows.None;
        }

        float reach = Mathf.Max(0.16f, (topLocalY - headLocal.y) * 2.4f);
        glow.range = reach;
        glow.transform.localPosition = headLocal;
        glow.transform.localRotation = Quaternion.identity;
        glow.enabled = on;
    }

    void DropGrip()
    {
        RemoveChild("Hand");
        RemoveChild("Wrist");
    }

    void RemoveChild(string name)
    {
        Transform node = transform.Find(name);
        if (node == null)
            return;
        DestroyImmediate(node.gameObject);
    }

    void IgnoreBodies()
    {
        CharacterController[] bodies = Object.FindObjectsByType<CharacterController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < bodies.Length; i++)
        {
            if (bodies[i] != null)
                IgnoreOne(bodies[i]);
        }
    }

    void IgnoreOne(Collider mover)
    {
        if (mover == null)
            return;
        Collider[] colliders = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null || collider == mover || !collider.enabled)
                continue;
            Physics.IgnoreCollision(mover, collider, true);
        }
    }

    void Fail()
    {
        if (phase == Phase.Dying)
            return;
        phase = Phase.Dying;
        if (bubble != null)
            bubble.gameObject.SetActive(false);
        if (emitterCollider != null)
            emitterCollider.enabled = false;
        SetGlow(false);
        ClearRipples();
        ReturnGun();
        EndShieldBed();
        StartFailureFx();
        Destroy(gameObject, FailSeconds);
    }

    void ReturnGun()
    {
        if (gunReturned)
            return;
        gunReturned = true;
        for (int i = 0; i < stashedCount; i++)
        {
            GameObject gun = stashed[i];
            stashed[i] = null;
            if (gun != null)
                gun.SetActive(true);
        }

        stashedCount = 0;
    }

    void TakeBlast(Vector3 point, float innerRadius, float innerDamage, float outerRadius, float outerDamage)
    {
        if (phase != Phase.Live)
            return;
        Vector3 center = BubbleCenter();
        float radius = BubbleRadius();
        Vector3 offset = point - center;
        float dist = offset.magnitude;
        float gap = Mathf.Abs(dist - radius);
        float bubbleAmount = Band(gap, innerRadius, innerDamage, outerRadius, outerDamage);
        if (bubbleAmount > 0f)
        {
            if (gap > 0.2f)
            {
                Vector3 outward = dist > 0.0001f ? offset / dist : Vector3.up;
                Vector3 surface = center + outward * radius;
                if (dist < radius)
                    outward = -outward;
                ShowRipple(surface, outward, point - surface);
            }

            shieldDamage += bubbleAmount;
            if (shieldDamage >= ShieldHealth)
            {
                Fail();
                return;
            }
        }

        Vector3 emitter = EmitterCenter();
        if (Crosses(point, emitter))
            return;
        float emitterAmount = Band(Vector3.Distance(point, emitter), innerRadius, innerDamage, outerRadius, outerDamage);
        if (emitterAmount <= 0f)
            return;
        emitterDamage += emitterAmount;
        if (emitterDamage > EmitterLimit)
            Fail();
    }

    static float Band(float distance, float innerRadius, float innerDamage, float outerRadius, float outerDamage)
    {
        if (distance <= innerRadius)
            return innerDamage;
        if (distance <= outerRadius)
            return outerDamage;
        return 0f;
    }

    bool Crosses(Vector3 from, Vector3 to)
    {
        Vector3 center = BubbleCenter();
        float radius = BubbleRadius();
        float inner = Mathf.Max(0.1f, radius - 0.08f);
        float innerSqr = inner * inner;
        bool fromIn = (from - center).sqrMagnitude < innerSqr;
        bool toIn = (to - center).sqrMagnitude < innerSqr;
        if (fromIn && toIn)
            return false;
        if (fromIn != toIn)
            return true;
        return SegmentHits(from, to, center, radius);
    }

    static bool SegmentHits(Vector3 from, Vector3 to, Vector3 center, float radius)
    {
        Vector3 delta = to - from;
        float length = delta.magnitude;
        if (length < 0.0001f)
            return false;
        Vector3 direction = delta / length;
        Vector3 m = from - center;
        float b = Vector3.Dot(m, direction);
        float c = Vector3.Dot(m, m) - radius * radius;
        float disc = b * b - c;
        if (disc < 0f)
            return false;
        float near = -b - Mathf.Sqrt(disc);
        return near > 0.02f && near < length - 0.02f;
    }

    float FarExit(Vector3 origin, Vector3 direction)
    {
        Vector3 center = BubbleCenter();
        float radius = BubbleRadius();
        Vector3 m = origin - center;
        float b = Vector3.Dot(m, direction);
        float c = Vector3.Dot(m, m) - radius * radius;
        float disc = b * b - c;
        if (disc < 0f)
            return 0f;
        float far = -b + Mathf.Sqrt(disc);
        if (far < 0f)
            return 0f;
        return far + 0.08f;
    }

    Vector3 BubbleCenter()
    {
        return bubble != null ? bubble.position : transform.TransformPoint(new Vector3(0f, topLocalY, 0f));
    }

    float BubbleRadius()
    {
        if (bubble == null)
            return ShieldRadius;
        return Mathf.Max(0.01f, bubble.lossyScale.x);
    }

    Vector3 EmitterCenter()
    {
        return transform.TransformPoint(new Vector3(0f, (feetLocalY + topLocalY) * 0.5f, 0f));
    }

    bool IsBubble(Collider collider)
    {
        return bubble != null && (collider.transform == bubble || collider.transform.IsChildOf(bubble));
    }

    void Stash<T>(Transform handRoot) where T : Component
    {
        if (handRoot == null)
            return;
        T[] found = handRoot.GetComponentsInChildren<T>(true);
        for (int i = 0; i < found.Length; i++)
        {
            if (found[i] == null)
                continue;
            Transform node = found[i].transform;
            if (node == transform || node.IsChildOf(transform) || node == handRoot)
                continue;
            Hide(found[i].gameObject);
        }
    }

    void Hide(GameObject gun)
    {
        if (gun == null || stashedCount >= stashed.Length)
            return;
        for (int i = 0; i < stashedCount; i++)
        {
            if (stashed[i] == gun)
                return;
        }

        gun.SetActive(false);
        stashed[stashedCount++] = gun;
    }

    bool FirePressed()
    {
        if (map == null)
            map = GetComponentInParent<Hd2ControlMap>();
        if (map != null && map.MenuOpen)
            return false;
        var action = hand == Hd2Pistol.Hand.Left ? Hd2ControlMap.Action.FireLeft : Hd2ControlMap.Action.FireRight;
        if (map != null && map.Pressed(action))
            return true;
        if (useEditorFire && editorFire != null && editorFire.WasPressedThisFrame())
            return true;
        return map == null && fireAction != null && fireAction.WasPressedThisFrame();
    }

    void BindFire()
    {
        DisposeFire();
        string node = hand == Hd2Pistol.Hand.Left ? "LeftHand" : "RightHand";
        fireAction = new InputAction("Hd2DropShieldFire", InputActionType.Button);
        fireAction.AddBinding("<XRController>{" + node + "}/triggerPressed");
        fireAction.Enable();
        useEditorFire = Application.isEditor && !HeadsetRunning();
        if (!useEditorFire)
            return;
        editorFire = new InputAction("Hd2DropShieldEditorFire", InputActionType.Button);
        editorFire.AddBinding(hand == Hd2Pistol.Hand.Left ? "<Mouse>/leftButton" : "<Mouse>/rightButton");
        editorFire.Enable();
    }

    void DisposeFire()
    {
        Dispose(ref fireAction);
        Dispose(ref editorFire);
    }

    static void Dispose(ref InputAction action)
    {
        if (action == null)
            return;
        action.Disable();
        action.Dispose();
        action = null;
    }

    static readonly System.Collections.Generic.List<UnityEngine.XR.XRDisplaySubsystem> displays =
        new System.Collections.Generic.List<UnityEngine.XR.XRDisplaySubsystem>();

    static bool HeadsetRunning()
    {
        displays.Clear();
        SubsystemManager.GetSubsystems(displays);
        for (int i = 0; i < displays.Count; i++)
        {
            if (displays[i].running)
                return true;
        }

        return false;
    }

    void MeasureDevice()
    {
        bool any = false;
        float minY = 0f;
        float maxY = 0f;
        float maxFlat = 0f;
        MeshFilter[] filters = GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < filters.Length; i++)
            Consider(filters[i].transform, filters[i].sharedMesh, ref any, ref minY, ref maxY, ref maxFlat);
        if (!any)
        {
            feetLocalY = 0f;
            topLocalY = 0.25f;
            headLocal = new Vector3(0f, 0.18f, 0f);
            emitterRadius = 0.06f;
            measured = true;
            return;
        }

        feetLocalY = minY;
        topLocalY = maxY;
        emitterRadius = Mathf.Max(0.03f, maxFlat);
        headLocal = HeadCenter(minY, maxY);
        measured = true;
    }

    void Consider(Transform node, Mesh mesh, ref bool any, ref float minY, ref float maxY, ref float maxFlat)
    {
        if (node == null || mesh == null || IsBubbleNode(node) || IsGripNode(node))
            return;
        Bounds box = mesh.bounds;
        Vector3 center = box.center;
        Vector3 extents = box.extents;
        for (int corner = 0; corner < 8; corner++)
        {
            Vector3 local = center + new Vector3(
                (corner & 1) == 0 ? -extents.x : extents.x,
                (corner & 2) == 0 ? -extents.y : extents.y,
                (corner & 4) == 0 ? -extents.z : extents.z);
            Vector3 root = transform.InverseTransformPoint(node.TransformPoint(local));
            if (!any)
            {
                any = true;
                minY = maxY = root.y;
            }
            else
            {
                if (root.y < minY)
                    minY = root.y;
                if (root.y > maxY)
                    maxY = root.y;
            }

            float flat = Mathf.Max(Mathf.Abs(root.x), Mathf.Abs(root.z));
            if (flat > maxFlat)
                maxFlat = flat;
        }
    }

    bool IsBubbleNode(Transform node)
    {
        return bubble != null && (node == bubble || node.IsChildOf(bubble));
    }

    static bool IsGripNode(Transform node)
    {
        while (node != null)
        {
            if (node.name == "Hand" || node.name == "Wrist")
                return true;
            node = node.parent;
        }

        return false;
    }

    Vector3 HeadCenter(float minY, float maxY)
    {
        float gate = Mathf.Lerp(minY, maxY, 0.55f);
        Vector3 sum = Vector3.zero;
        int count = 0;
        MeshFilter[] filters = GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < filters.Length; i++)
            AddHead(filters[i].transform, filters[i].sharedMesh, gate, ref sum, ref count);
        if (count == 0)
            return new Vector3(0f, Mathf.Lerp(minY, maxY, 0.72f), 0f);
        return sum / count;
    }

    void AddHead(Transform node, Mesh mesh, float gate, ref Vector3 sum, ref int count)
    {
        if (node == null || mesh == null || !mesh.isReadable || IsBubbleNode(node) || IsGripNode(node))
            return;
        Vector3[] verts = mesh.vertices;
        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 root = transform.InverseTransformPoint(node.TransformPoint(verts[i]));
            if (root.y < gate)
                continue;
            sum += root;
            count++;
        }
    }

    Transform CreateBubble()
    {
        var bubbleObject = new GameObject("Bubble");
        bubbleObject.transform.SetParent(transform, false);
        bubbleObject.SetActive(false);
        return bubbleObject.transform;
    }

    static void OpenLayer()
    {
        int layer = LayerMask.NameToLayer("DropShield");
        if (layer < 0)
            return;
        if (layerOpen)
            return;
        layerOpen = true;
        for (int i = 0; i < 32; i++)
            Physics.IgnoreLayerCollision(layer, i, true);
    }

    public static Mesh SharedBubble()
    {
        if (sharedBubble != null)
            return sharedBubble;
        sharedBubble = CreateBubbleMesh();
        return sharedBubble;
    }

    public static Mesh CreateBubbleMesh()
    {
        var verts = new System.Collections.Generic.List<Vector3>(42);
        var tris = new System.Collections.Generic.List<int>(240);
        float golden = (1f + Mathf.Sqrt(5f)) * 0.5f;
        Vector3[] seed =
        {
            new Vector3(-1f, golden, 0f), new Vector3(1f, golden, 0f),
            new Vector3(-1f, -golden, 0f), new Vector3(1f, -golden, 0f),
            new Vector3(0f, -1f, golden), new Vector3(0f, 1f, golden),
            new Vector3(0f, -1f, -golden), new Vector3(0f, 1f, -golden),
            new Vector3(golden, 0f, -1f), new Vector3(golden, 0f, 1f),
            new Vector3(-golden, 0f, -1f), new Vector3(-golden, 0f, 1f)
        };
        for (int i = 0; i < seed.Length; i++)
            verts.Add(seed[i].normalized);
        int[] faces =
        {
            0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
            1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
            4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1
        };
        for (int i = 0; i < faces.Length; i++)
            tris.Add(faces[i]);

        var mids = new System.Collections.Generic.Dictionary<long, int>(80);
        var next = new System.Collections.Generic.List<int>(320);
        for (int i = 0; i < tris.Count; i += 3)
        {
            int a = tris[i];
            int b = tris[i + 1];
            int c = tris[i + 2];
            int ab = Midpoint(mids, verts, a, b);
            int bc = Midpoint(mids, verts, b, c);
            int ca = Midpoint(mids, verts, c, a);
            next.Add(a); next.Add(ab); next.Add(ca);
            next.Add(b); next.Add(bc); next.Add(ab);
            next.Add(c); next.Add(ca); next.Add(bc);
            next.Add(ab); next.Add(bc); next.Add(ca);
        }

        var mesh = new Mesh();
        mesh.name = "DropShieldBubble";
        mesh.SetVertices(verts);
        mesh.SetTriangles(next, 0);
        Vector3 outward = Vector3.Cross(verts[next[1]] - verts[next[0]], verts[next[2]] - verts[next[0]]);
        if (Vector3.Dot(outward, verts[next[0]]) < 0f)
        {
            for (int i = 0; i < next.Count; i += 3)
            {
                int swap = next[i + 1];
                next[i + 1] = next[i + 2];
                next[i + 2] = swap;
            }

            mesh.SetTriangles(next, 0);
        }

        var normals = new Vector3[verts.Count];
        for (int i = 0; i < verts.Count; i++)
            normals[i] = verts[i];
        mesh.SetNormals(normals);
        mesh.RecalculateBounds();
        return mesh;
    }

    static int Midpoint(System.Collections.Generic.Dictionary<long, int> mids, System.Collections.Generic.List<Vector3> verts, int a, int b)
    {
        if (a > b)
        {
            int swap = a;
            a = b;
            b = swap;
        }

        long key = ((long)a << 32) | (uint)b;
        int index;
        if (mids.TryGetValue(key, out index))
            return index;
        index = verts.Count;
        verts.Add((verts[a] + verts[b]).normalized);
        mids.Add(key, index);
        return index;
    }

    void EnsureShieldAudio()
    {
        if (shieldOn != null)
            return;
        shieldOnClip = Resources.Load<AudioClip>("Audio/DropShieldStartOn");
        shieldEndClip = Resources.Load<AudioClip>("Audio/DropShieldEnd");
        var host = new GameObject("DropShieldAudio");
        host.transform.SetParent(transform, false);
        shieldOn = AddShieldVoice(host, shieldOnClip, false);
        shieldEnd = AddShieldVoice(host, shieldEndClip, false);
        PlaceShieldAudio();
    }

    AudioSource AddShieldVoice(GameObject host, AudioClip clip, bool loop)
    {
        AudioSource source = host.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = loop;
        source.spatialBlend = 1f;
        source.dopplerLevel = 0f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 1.5f;
        source.maxDistance = 28f;
        source.clip = clip;
        return source;
    }

    void PlaceShieldAudio()
    {
        if (shieldOn == null)
            return;
        shieldOn.transform.localPosition = new Vector3(0f, (feetLocalY + topLocalY) * 0.5f, 0f);
    }

    void StartShieldBed()
    {
        if (phase != Phase.Live)
            return;
        EnsureShieldAudio();
        PlaceShieldAudio();
        if (shieldOn == null || shieldOnClip == null || shieldOn.isPlaying)
            return;
        // Loop only when the clip is shorter than the shield life. A longer bed plays once.
        shieldOn.loop = shieldOnClip.length > 0f && shieldOnClip.length < ShieldSeconds;
        shieldOn.Play();
    }

    void EndShieldBed()
    {
        EnsureShieldAudio();
        PlaceShieldAudio();
        if (shieldOn != null)
            shieldOn.Stop();
        if (shieldEnd == null || shieldEndClip == null)
            return;
        shieldEnd.Play();
    }

    void BuildVoices()
    {
        if (hitVoices != null)
            return;
        hitClip = Resources.Load<AudioClip>("Audio/ShieldHit");
        hitVoices = new AudioSource[HitVoices];
        for (int i = 0; i < HitVoices; i++)
        {
            var voiceObject = new GameObject("ShieldHitSound");
            voiceObject.transform.SetParent(transform, false);
            AudioSource source = voiceObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.dopplerLevel = 0f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 1.5f;
            source.maxDistance = 28f;
            source.clip = hitClip;
            hitVoices[i] = source;
        }
    }

    void PlayRippleSound(Vector3 point)
    {
        if (hitClip == null || hitVoices == null)
            return;
        AudioSource source = hitVoices[hitVoice];
        hitVoice = (hitVoice + 1) % hitVoices.Length;
        source.transform.position = point;
        source.Play();
    }

    void BuildRipples()
    {
        if (ripples != null)
            return;
        Mesh mesh = RippleQuad();
        Material material = RippleMat();
        if (mesh == null || material == null)
            return;
        ripples = new Ripple[RipplePool];
        for (int i = 0; i < ripples.Length; i++)
        {
            var rippleObject = new GameObject("ShieldRipple");
            rippleObject.transform.SetParent(transform, false);
            var filter = rippleObject.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = rippleObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.enabled = false;
            ripples[i] = new Ripple
            {
                transform = rippleObject.transform,
                renderer = renderer,
                block = new MaterialPropertyBlock(),
                live = false
            };
        }
    }

    void ShowRipple(Vector3 point, Vector3 normal, Vector3 incoming)
    {
        if (incoming.sqrMagnitude > 0.0001f && Vector3.Dot(normal, incoming) > 0.05f)
            normal = -normal;
        PlayRippleSound(point);
        if (ripples == null || rippleMaterial == null)
            return;
        int slot = 0;
        float oldest = -1f;
        for (int i = 0; i < ripples.Length; i++)
        {
            if (!ripples[i].live)
            {
                slot = i;
                oldest = -1f;
                break;
            }

            if (ripples[i].age > oldest)
            {
                oldest = ripples[i].age;
                slot = i;
            }
        }

        Ripple ripple = ripples[slot];
        if (!ripple.live)
            liveRipples++;
        ripple.live = true;
        ripple.age = 0f;
        if (normal.sqrMagnitude < 0.0001f)
            normal = Vector3.up;
        normal.Normalize();
        ripple.transform.SetPositionAndRotation(point + normal * 0.03f, Quaternion.LookRotation(normal));
        FitRipple(ripple.transform, RippleStart);
        Color color = RippleColor;
        color.a = 1f;
        ripple.block.SetColor("_BaseColor", color);
        ripple.renderer.SetPropertyBlock(ripple.block);
        ripple.renderer.enabled = true;
    }

    void ClearRipples()
    {
        if (ripples == null)
            return;
        for (int i = 0; i < ripples.Length; i++)
        {
            Ripple ripple = ripples[i];
            if (ripple == null)
                continue;
            ripple.live = false;
            if (ripple.renderer != null)
                ripple.renderer.enabled = false;
        }

        liveRipples = 0;
    }

    void TickRipples()
    {
        if (liveRipples == 0 || ripples == null)
            return;
        float dt = Time.deltaTime;
        for (int i = 0; i < ripples.Length; i++)
        {
            Ripple ripple = ripples[i];
            if (!ripple.live)
                continue;
            ripple.age += dt;
            float t = ripple.age / RippleLife;
            if (t >= 1f)
            {
                ripple.live = false;
                ripple.renderer.enabled = false;
                liveRipples--;
                continue;
            }

            float spread = 1f - (1f - t) * (1f - t);
            FitRipple(ripple.transform, Mathf.Lerp(RippleStart, RippleEnd, spread));
            Color color = RippleColor;
            color.a = (1f - t) * (1f - t);
            ripple.block.SetColor("_BaseColor", color);
            ripple.renderer.SetPropertyBlock(ripple.block);
        }
    }

    void FitRipple(Transform ripple, float size)
    {
        Vector3 lossy = transform.lossyScale;
        ripple.localScale = new Vector3(
            size / Mathf.Max(0.0001f, Mathf.Abs(lossy.x)),
            size / Mathf.Max(0.0001f, Mathf.Abs(lossy.y)),
            1f);
    }

    static Mesh RippleQuad()
    {
        if (rippleMesh != null)
            return rippleMesh;
        var temp = GameObject.CreatePrimitive(PrimitiveType.Quad);
        var filter = temp.GetComponent<MeshFilter>();
        rippleMesh = filter != null ? filter.sharedMesh : null;
        if (Application.isPlaying)
            Destroy(temp);
        else
            DestroyImmediate(temp);
        return rippleMesh;
    }

    static Material RippleMat()
    {
        if (rippleMaterial != null)
            return rippleMaterial;
        rippleMaterial = Resources.Load<Material>("ShieldRipple");
        if (rippleMaterial != null)
            return rippleMaterial;
        Shader shader = Shader.Find("HD2/ShieldRipple");
        if (shader == null)
            return null;
        rippleMaterial = new Material(shader);
        rippleMaterial.SetColor("_BaseColor", RippleColor);
        return rippleMaterial;
    }

    void StartFailureFx()
    {
        Vector3 origin = transform.TransformPoint(new Vector3(0f, (feetLocalY + topLocalY) * 0.5f, 0f));
        var anchor = new GameObject("Failure");
        anchor.transform.SetParent(transform, false);
        anchor.transform.position = origin;
        AddSparks(anchor.transform);
        AddSmoke(anchor.transform);
    }

    static void AddSparks(Transform parent)
    {
        var sparkObject = new GameObject("Sparks");
        sparkObject.transform.SetParent(parent, false);
        var particles = sparkObject.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = FailSeconds;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.42f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.4f, 3.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.03f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.95f, 0.75f), new Color(1f, 0.72f, 0.22f));
        main.gravityModifier = 0.6f;
        main.maxParticles = 48;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = particles.emission;
        emission.rateOverTime = 22f;
        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.06f;
        var renderer = sparkObject.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = Hd2Shot.ParticleMaterial();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        particles.Play();
    }

    static void AddSmoke(Transform parent)
    {
        var smokeObject = new GameObject("Smoke");
        smokeObject.transform.SetParent(parent, false);
        var particles = smokeObject.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = FailSeconds;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.3f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.45f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.18f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.75f, 0.72f, 0.66f, 0.35f), new Color(0.55f, 0.52f, 0.48f, 0.28f));
        main.gravityModifier = -0.05f;
        main.maxParticles = 36;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = particles.emission;
        emission.rateOverTime = 14f;
        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.05f;
        var renderer = smokeObject.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = SmokeMaterial();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        particles.Play();
    }

    static Material SmokeMaterial()
    {
        if (smokeMaterial != null)
            return smokeMaterial;
        smokeMaterial = Resources.Load<Material>("SoftSmoke");
        if (smokeMaterial != null)
            return smokeMaterial;
        return Hd2Shot.ParticleMaterial();
    }
}
