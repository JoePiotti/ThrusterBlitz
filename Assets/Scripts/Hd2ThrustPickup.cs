using UnityEngine;

/// <summary>
/// Ground pad with a glowing jetpack sphere. Touching it fills thrust and, if the
/// meter max is still 3, raises it to 5 until the player respawns.
/// A full meter does not start the cooldown. After a successful pickup the sphere
/// hides for 5 seconds while a ring fills, then the sphere returns.
/// </summary>
[ExecuteAlways]
public class Hd2ThrustPickup : MonoBehaviour
{
    public Texture2D icon;
    public float cooldown = 5f;
    public int upgradedMax = 5;

    static readonly Color pickupColor = new Color(0.25f, 0.7f, 1f, 1f);

    Transform sphere;
    Transform iconQuad;
    LineRenderer ring;
    bool ready = true;
    float cooldownLeft;
    Material sphereMaterial;
    Material iconMaterial;

    void OnEnable()
    {
        BuildVisuals();
        SetReady(true);
    }

    void Update()
    {
        var view = Camera.main;
        if (iconQuad != null && view != null)
            iconQuad.rotation = Quaternion.LookRotation(iconQuad.position - view.transform.position);
        if (ring != null && view != null)
            ring.transform.rotation = Quaternion.LookRotation(ring.transform.position - view.transform.position);

        if (!ready)
        {
            cooldownLeft -= Time.deltaTime;
            SetRing(1f - Mathf.Clamp01(cooldownLeft / Mathf.Max(0.01f, cooldown)));
            if (cooldownLeft <= 0f)
                SetReady(true);
            return;
        }

        if (!Application.isPlaying)
            return;

        var player = FindFirstObjectByType<Hd2Locomotion>();
        if (player == null || sphere == null)
            return;

        Vector3 feet = player.transform.position;
        Vector3 pad = sphere.position;
        float horizontal = Vector2.Distance(new Vector2(feet.x, feet.z), new Vector2(pad.x, pad.z));
        bool tallEnough = feet.y < pad.y + 0.5f && feet.y + 2f > pad.y - 0.4f;
        if (horizontal < 0.65f && tallEnough)
            TryCollect(player.GetComponent<Hd2ThrustMeter>());
    }

    void TryCollect(Hd2ThrustMeter meter)
    {
        if (meter == null || meter.Charges >= upgradedMax)
            return;

        int maximum = meter.MaxCharges < upgradedMax ? upgradedMax : meter.MaxCharges;
        meter.SetMaxCharges(maximum, true);
        ready = false;
        cooldownLeft = cooldown;
        if (sphere != null)
            sphere.gameObject.SetActive(false);
        if (ring != null)
            ring.enabled = true;
        SetRing(0f);
    }

    void SetReady(bool value)
    {
        ready = value;
        if (sphere != null)
            sphere.gameObject.SetActive(value);
        if (ring != null)
            ring.enabled = !value;
    }

    void BuildVisuals()
    {
        if (transform.Find("Pad") != null)
        {
            sphere = transform.Find("Sphere");
            iconQuad = transform.Find("Sphere/Icon");
            ring = transform.Find("Ring") != null ? transform.Find("Ring").GetComponent<LineRenderer>() : null;
            return;
        }

        var pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pad.name = "Pad";
        pad.transform.SetParent(transform, false);
        pad.transform.localPosition = new Vector3(0f, 0.03f, 0f);
        pad.transform.localScale = new Vector3(0.7f, 0.03f, 0.7f);
        DestroyCollider(pad);
        Paint(pad, pickupColor, false);

        var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ball.name = "Sphere";
        ball.transform.SetParent(transform, false);
        ball.transform.localPosition = new Vector3(0f, 1.05f, 0f);
        ball.transform.localScale = Vector3.one * 0.42f;
        DestroyCollider(ball);
        sphereMaterial = Paint(ball, new Color(pickupColor.r, pickupColor.g, pickupColor.b, 0.35f), true);
        sphere = ball.transform;

        var iconObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
        iconObject.name = "Icon";
        iconObject.transform.SetParent(sphere, false);
        iconObject.transform.localPosition = Vector3.zero;
        iconObject.transform.localScale = Vector3.one * 0.62f;
        DestroyCollider(iconObject);
        iconMaterial = new Material(Shader.Find("HD2/JetpackIcon"));
        if (icon != null)
            iconMaterial.mainTexture = icon;
        iconObject.GetComponent<Renderer>().sharedMaterial = iconMaterial;
        iconQuad = iconObject.transform;

        var ringObject = new GameObject("Ring");
        ringObject.transform.SetParent(transform, false);
        ringObject.transform.localPosition = new Vector3(0f, 1.05f, 0f);
        ring = ringObject.AddComponent<LineRenderer>();
        ring.useWorldSpace = false;
        ring.loop = false;
        ring.widthMultiplier = 0.025f;
        ring.positionCount = 0;
        ring.material = new Material(Shader.Find("Sprites/Default"));
        ring.startColor = ring.endColor = new Color(0.3f, 0.85f, 1f, 1f);
        ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ring.enabled = false;
    }

    public void RepairSavedMaterials()
    {
        EnsureMaterial(transform.Find("Pad"), pickupColor, false);
        var ball = transform.Find("Sphere");
        if (ball != null)
        {
            var renderer = ball.GetComponent<Renderer>();
            if (renderer == null || renderer.sharedMaterial == null || renderer.sharedMaterial.shader == null)
                sphereMaterial = Paint(ball.gameObject, new Color(pickupColor.r, pickupColor.g, pickupColor.b, 0.35f), true);
        }

        Transform iconTransform = transform.Find("Sphere/Icon");
        if (iconTransform != null)
        {
            var renderer = iconTransform.GetComponent<Renderer>();
            if (renderer != null && (renderer.sharedMaterial == null || renderer.sharedMaterial.shader == null))
            {
                iconMaterial = new Material(Shader.Find("HD2/JetpackIcon"));
                if (icon != null)
                    iconMaterial.mainTexture = icon;
                renderer.sharedMaterial = iconMaterial;
            }
        }

        Transform ringTransform = transform.Find("Ring");
        if (ringTransform != null)
        {
            var line = ringTransform.GetComponent<LineRenderer>();
            if (line != null && (line.sharedMaterial == null || line.sharedMaterial.shader == null))
            {
                line.material = new Material(Shader.Find("Sprites/Default"));
                line.startColor = line.endColor = new Color(0.3f, 0.85f, 1f, 1f);
            }
        }
    }

    static void EnsureMaterial(Transform target, Color color, bool transparent)
    {
        if (target == null)
            return;
        var renderer = target.GetComponent<Renderer>();
        if (renderer != null && renderer.sharedMaterial != null && renderer.sharedMaterial.shader != null)
            return;
        Paint(target.gameObject, color, transparent);
    }

    void SetRing(float amount)
    {
        if (ring == null)
            return;

        const int segments = 40;
        int count = Mathf.Max(2, Mathf.CeilToInt(segments * Mathf.Clamp01(amount)));
        ring.positionCount = count;
        float radius = 0.28f;
        for (int i = 0; i < count; i++)
        {
            float t = count == 1 ? 0f : i / (float)(segments - 1);
            float angle = t * Mathf.PI * 2f - Mathf.PI * 0.5f;
            ring.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f));
        }
    }

    static void ColorPad(Renderer renderer)
    {
        if (renderer == null)
            return;

        var shared = renderer.sharedMaterial;
        if (shared != null && shared.HasProperty("_BaseColor") && shared.GetColor("_BaseColor") == pickupColor)
            return;

        var material = shared != null ? new Material(shared) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.color = pickupColor;
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", pickupColor);
        renderer.sharedMaterial = material;
    }

    static void DestroyCollider(GameObject target)
    {
        var collider = target.GetComponent<Collider>();
        if (collider != null)
        {
            if (Application.isPlaying)
                Destroy(collider);
            else
                DestroyImmediate(collider);
        }
    }

    static Material Paint(GameObject target, Color color, bool transparent)
    {
        var shader = Shader.Find(transparent ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
        var material = new Material(shader);
        material.color = color;
        if (transparent)
        {
            material.SetFloat("_Surface", 1f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.renderQueue = 3000;
        }

        target.GetComponent<Renderer>().sharedMaterial = material;
        return material;
    }
}
