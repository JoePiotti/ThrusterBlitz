using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Team wall for Shootout. The named team walks and blitzes through.
/// Shots from both teams stop on both sides. A hit ripples and uses the payload shield sound.
/// </summary>
public class Hd2ShootoutShield : MonoBehaviour
{
    const int RipplePool = 6;
    const float RippleLife = 0.5f;
    const float RippleStart = 0.12f;
    const float RippleEnd = 0.72f;
    static readonly Color RedTint = new Color(0.9f, 0.16f, 0.12f, 0.38f);
    static readonly Color BlueTint = new Color(0.22f, 0.48f, 0.98f, 0.38f);

    AudioSource voice;
    AudioClip clip;
    Ripple[] ripples;
    int liveRipples;
    Color teamColor = BlueTint;
    static Mesh rippleMesh;
    static Material rippleMaterial;

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
        Hd2Team team = NameTeam();
        Hd2SpawnField field = GetComponent<Hd2SpawnField>();
        if (field == null)
            field = gameObject.AddComponent<Hd2SpawnField>();
        field.team = team;
        field.blockAllShots = true;
        teamColor = team == Hd2Team.Red ? RedTint : BlueTint;
        EnsureBox();
        Paint(teamColor);
        clip = Resources.Load<AudioClip>("Audio/ShieldHit");
        voice = gameObject.AddComponent<AudioSource>();
        voice.playOnAwake = false;
        voice.spatialBlend = 1f;
        voice.dopplerLevel = 0f;
        voice.rolloffMode = AudioRolloffMode.Linear;
        voice.minDistance = 1.5f;
        voice.maxDistance = 28f;
        BuildRipples();
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
            ripple.transform.localScale = Vector3.one * size;
            Color color = teamColor;
            color.a = (1f - life) * (1f - life);
            ripple.block.SetColor("_BaseColor", color);
            ripple.renderer.SetPropertyBlock(ripple.block);
        }
    }

    Hd2Team NameTeam()
    {
        string n = name.ToLowerInvariant();
        if (n.Contains("red"))
            return Hd2Team.Red;
        return Hd2Team.Blue;
    }

    void EnsureBox()
    {
        MeshCollider meshCollider = GetComponent<MeshCollider>();
        if (meshCollider != null)
            meshCollider.enabled = false;

        BoxCollider box = GetComponent<BoxCollider>();
        if (box == null)
            box = gameObject.AddComponent<BoxCollider>();

        MeshFilter filter = GetComponent<MeshFilter>();
        if (filter != null && filter.sharedMesh != null)
        {
            Bounds bounds = filter.sharedMesh.bounds;
            Vector3 size = bounds.size;
            if (size.x < 0.02f)
                size.x = 0.08f;
            if (size.y < 0.02f)
                size.y = 0.08f;
            if (size.z < 0.02f)
                size.z = 0.08f;
            box.center = bounds.center;
            box.size = size;
        }

        box.isTrigger = false;
    }

    void Paint(Color tint)
    {
        Renderer renderer = GetComponent<Renderer>();
        if (renderer == null)
            return;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            return;

        Material material = new Material(shader);
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", 0f);
        material.SetFloat("_Cull", (float)CullMode.Off);
        material.SetOverrideTag("RenderType", "Transparent");
        material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        material.SetInt("_ZWrite", 0);
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = 3000;
        material.SetColor("_BaseColor", tint);
        material.SetColor("_Color", tint);
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
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
        ripple.transform.localScale = Vector3.one * RippleStart;
        Color color = teamColor;
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
            var filter = rippleObject.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = rippleObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
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
        return rippleMaterial;
    }
}
