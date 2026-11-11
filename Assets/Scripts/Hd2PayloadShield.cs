using UnityEngine;

/// <summary>
/// Glass on the payload. Shots stop here without leaving a mark. The hit is
/// heard at the shield, and a blue ripple spreads across the glass.
/// </summary>
public class Hd2PayloadShield : MonoBehaviour
{
    const int RipplePool = 6;
    const float RippleLife = 0.5f;
    const float RippleStart = 0.12f;
    const float RippleEnd = 0.72f;
    static readonly Color RippleColor = new Color(0.45f, 0.86f, 1f, 1f);

    AudioSource voice;
    AudioClip clip;
    Transform follow;
    Ripple[] ripples;
    int liveRipples;
    static Mesh rippleMesh;
    static Material rippleMaterial;
    static readonly Hd2PayloadShield[] shields = new Hd2PayloadShield[8];
    static int shieldCount;

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
        clip = Resources.Load<AudioClip>("Audio/ShieldHit");
        voice = gameObject.AddComponent<AudioSource>();
        voice.playOnAwake = false;
        voice.spatialBlend = 1f;
        voice.dopplerLevel = 0f;
        voice.rolloffMode = AudioRolloffMode.Linear;
        voice.minDistance = 1.5f;
        voice.maxDistance = 28f;

        var payload = GetComponentInParent<Hd2Payload>();
        follow = payload != null ? payload.transform : transform;
        BuildRipples();
    }

    void OnEnable()
    {
        for (int i = 0; i < shieldCount; i++)
        {
            if (shields[i] == this)
                return;
        }

        if (shieldCount < shields.Length)
            shields[shieldCount++] = this;
    }

    void OnDisable()
    {
        for (int i = 0; i < shieldCount; i++)
        {
            if (shields[i] != this)
                continue;
            shieldCount--;
            shields[i] = shields[shieldCount];
            shields[shieldCount] = null;
            return;
        }
    }

    public static bool Touches(Vector3 point, float radius)
    {
        float reach = radius * radius;
        for (int i = 0; i < shieldCount; i++)
        {
            Hd2PayloadShield shield = shields[i];
            if (shield == null)
                continue;
            if (Hd2SpawnField.NearCollider(shield.GetComponent<BoxCollider>(), point, reach))
                return true;
            if (Hd2SpawnField.NearCollider(shield.GetComponent<MeshCollider>(), point, reach))
                return true;
        }

        return false;
    }

    static readonly RaycastHit[] crossHits = new RaycastHit[16];

    /// <summary>
    /// True when the gun or any part of its owner is on the far side of this glass.
    /// Same idea as a hand that has crossed a wall: the shield sits between that part and the chest.
    /// </summary>
    public static bool BlocksShot(Component gun, Vector3 muzzle)
    {
        if (gun == null)
            return false;

        Transform owner = Hd2SpawnField.FighterBody(gun);
        if (owner == null)
            owner = gun.transform.root;
        Vector3 chest = owner.position + Vector3.up * 0.9f;
        if (Crossed(muzzle, chest, owner, gun.transform))
            return true;
        if (Crossed(gun.transform.position, chest, owner, gun.transform))
            return true;

        Hd2Body body = owner.GetComponent<Hd2Body>();
        if (body != null)
        {
            if (Crossed(body.head, chest, owner, gun.transform))
                return true;
            if (Crossed(body.handL, chest, owner, gun.transform))
                return true;
            if (Crossed(body.handR, chest, owner, gun.transform))
                return true;
            if (Crossed(body.forearmL, chest, owner, gun.transform))
                return true;
            if (Crossed(body.forearmR, chest, owner, gun.transform))
                return true;
            if (Crossed(body.upperArmL, chest, owner, gun.transform))
                return true;
            if (Crossed(body.upperArmR, chest, owner, gun.transform))
                return true;
        }

        Hd2Locomotion locomotion = owner.GetComponent<Hd2Locomotion>();
        if (locomotion != null)
        {
            if (Crossed(locomotion.leftHand, chest, owner, gun.transform))
                return true;
            if (Crossed(locomotion.rightHand, chest, owner, gun.transform))
                return true;
        }

        return false;
    }

    static bool Crossed(Transform part, Vector3 chest, Transform owner, Transform gun)
    {
        if (part == null)
            return false;
        return Crossed(part.position, chest, owner, gun);
    }

    static bool Crossed(Vector3 point, Vector3 chest, Transform owner, Transform gun)
    {
        Vector3 delta = chest - point;
        float distance = delta.magnitude;
        if (distance < 0.12f)
            return false;

        Vector3 direction = delta / distance;
        bool previous = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true;
        int count = Physics.RaycastNonAlloc(point, direction, crossHits, distance, ~0, QueryTriggerInteraction.Ignore);
        Physics.queriesHitBackfaces = previous;

        float best = distance;
        bool shield = false;
        for (int i = 0; i < count; i++)
        {
            Collider collider = crossHits[i].collider;
            if (collider == null || crossHits[i].distance >= best)
                continue;
            Transform hit = collider.transform;
            if (owner != null && (hit == owner || hit.IsChildOf(owner)))
                continue;
            if (gun != null && (hit == gun || hit.IsChildOf(gun)))
                continue;
            best = crossHits[i].distance;
            shield = collider.GetComponentInParent<Hd2PayloadShield>() != null;
        }

        return shield;
    }

    public static int Cast(Vector3 origin, Vector3 direction, RaycastHit[] results, float distance)
    {
        bool previous = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true;
        int count = Physics.RaycastNonAlloc(origin, direction, results, distance, ~0, QueryTriggerInteraction.Ignore);
        Physics.queriesHitBackfaces = previous;
        return count;
    }

    public static bool SkipBackface(RaycastHit hit, Vector3 direction)
    {
        if (hit.collider == null || Vector3.Dot(hit.normal, direction) <= 0.05f)
            return false;
        return hit.collider.GetComponentInParent<Hd2PayloadShield>() == null;
    }

    public void PlayHit(Vector3 point, Vector3 normal, Vector3 incoming)
    {
        if (incoming.sqrMagnitude > 0.0001f && Vector3.Dot(normal, incoming) > 0.05f)
            normal = -normal;
        if (voice != null && clip != null)
            voice.PlayOneShot(clip);
        ShowRipple(point, normal);
    }

    void Update()
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
            float life = ripple.age / RippleLife;
            if (life >= 1f)
            {
                ripple.live = false;
                ripple.renderer.enabled = false;
                liveRipples--;
                continue;
            }

            float spread = 1f - (1f - life) * (1f - life);
            float size = Mathf.Lerp(RippleStart, RippleEnd, spread);
            Vector3 lossy = follow.lossyScale;
            ripple.transform.localScale = new Vector3(
                size / Mathf.Max(0.0001f, Mathf.Abs(lossy.x)),
                size / Mathf.Max(0.0001f, Mathf.Abs(lossy.y)),
                1f);
            Color color = RippleColor;
            color.a = (1f - life) * (1f - life);
            ripple.block.SetColor("_BaseColor", color);
            ripple.renderer.SetPropertyBlock(ripple.block);
        }
    }

    void ShowRipple(Vector3 point, Vector3 normal)
    {
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
            normal = transform.forward;
        normal.Normalize();
        ripple.transform.SetPositionAndRotation(point + normal * 0.03f, Quaternion.LookRotation(normal));
        Vector3 lossy = follow.lossyScale;
        ripple.transform.localScale = new Vector3(
            RippleStart / Mathf.Max(0.0001f, Mathf.Abs(lossy.x)),
            RippleStart / Mathf.Max(0.0001f, Mathf.Abs(lossy.y)),
            1f);
        Color color = RippleColor;
        color.a = 1f;
        ripple.block.SetColor("_BaseColor", color);
        ripple.renderer.SetPropertyBlock(ripple.block);
        ripple.renderer.enabled = true;
    }

    void BuildRipples()
    {
        Mesh mesh = RippleMesh();
        Material material = RippleMaterial();
        if (mesh == null || material == null)
            return;

        ripples = new Ripple[RipplePool];
        for (int i = 0; i < ripples.Length; i++)
        {
            var rippleObject = new GameObject("ShieldRipple");
            rippleObject.transform.SetParent(follow, false);
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

    static Mesh RippleMesh()
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

    static Material RippleMaterial()
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
}
