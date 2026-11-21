using UnityEngine;

/// <summary>
/// Blue ring around a gun display's number (or R). It fills clockwise from the top to
/// show progress: a reload, a pistol or shocks charge, the laser's charge or its cooldown.
/// Guns call Hd2AmmoReadout.Ring every frame with 0..1; 0 hides it. Not drawn on bots' guns.
/// </summary>
public class Hd2AmmoRing : MonoBehaviour
{
    const int Segments = 64;
    static readonly Color RingColor = new Color(0.3f, 0.75f, 1f, 1f);
    static Material material;

    TextMesh label;
    MeshRenderer textRenderer;
    MeshRenderer ringRenderer;
    Mesh mesh;
    float radius;
    Vector3 center;
    int shownSteps = -1;
    bool onBot;

    public static void Set(TextMesh label, float fill)
    {
        if (label == null)
            return;
        var ring = label.GetComponent<Hd2AmmoRing>();
        if (ring == null)
        {
            if (fill <= 0f)
                return;
            ring = label.gameObject.AddComponent<Hd2AmmoRing>();
            ring.Init(label);
        }

        ring.Show(fill);
    }

    void Init(TextMesh text)
    {
        label = text;
        textRenderer = text.GetComponent<MeshRenderer>();
        onBot = GetComponentInParent<Hd2EnemyBot>() != null;
        if (onBot)
            return;

        var holder = new GameObject("AmmoRing");
        holder.hideFlags = HideFlags.DontSave;
        holder.transform.SetParent(transform, false);
        var filter = holder.AddComponent<MeshFilter>();
        mesh = new Mesh { name = "AmmoRing" };
        mesh.MarkDynamic();
        filter.sharedMesh = mesh;
        ringRenderer = holder.AddComponent<MeshRenderer>();
        ringRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ringRenderer.receiveShadows = false;
        ringRenderer.sharedMaterial = Material();
        ringRenderer.enabled = false;
    }

    void Show(float fill)
    {
        if (onBot || ringRenderer == null)
            return;

        int steps = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(fill) * Segments), 0, Segments);
        if (steps == 0)
        {
            if (ringRenderer.enabled)
                ringRenderer.enabled = false;
            shownSteps = 0;
            return;
        }

        bool resized = Measure();
        if (!ringRenderer.enabled)
            ringRenderer.enabled = true;
        if (steps == shownSteps && !resized)
            return;
        shownSteps = steps;
        Build(steps);
    }

    // Sized from the text: big enough to circle two digits, and it never shrinks back.
    bool Measure()
    {
        if (textRenderer == null || label == null || string.IsNullOrEmpty(label.text))
            return false;

        Bounds box = textRenderer.localBounds;
        if (box.size.sqrMagnitude < 1e-8f)
            return false;

        float wanted = Mathf.Max(box.extents.x, box.extents.y) * 1.3f;
        wanted = Mathf.Max(wanted, box.extents.y * 1.7f);
        if (wanted <= radius + 1e-5f)
            return false;
        radius = wanted;
        center = new Vector3(box.center.x, box.center.y, 0f);
        return true;
    }

    void Build(int steps)
    {
        if (radius <= 0f)
            radius = Mathf.Max(0.001f, label.characterSize * label.fontSize * 0.08f);

        float thickness = radius * 0.14f;
        float outer = radius;
        float inner = radius - thickness;
        var vertices = new Vector3[(steps + 1) * 2];
        var triangles = new int[steps * 6];
        for (int i = 0; i <= steps; i++)
        {
            // Start at the top and run clockwise as seen from the front of the display.
            float angle = Mathf.PI * 0.5f - i * (Mathf.PI * 2f / Segments);
            var direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
            vertices[i * 2] = center + direction * outer;
            vertices[i * 2 + 1] = center + direction * inner;
        }

        for (int i = 0; i < steps; i++)
        {
            int a = i * 2;
            int t = i * 6;
            triangles[t] = a;
            triangles[t + 1] = a + 2;
            triangles[t + 2] = a + 1;
            triangles[t + 3] = a + 1;
            triangles[t + 4] = a + 2;
            triangles[t + 5] = a + 3;
        }

        mesh.Clear();
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
    }

    static Material Material()
    {
        if (material != null)
            return material;
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        material = new Material(shader) { name = "AmmoRing" };
        material.color = RingColor;
        return material;
    }

    void OnDestroy()
    {
        if (mesh != null)
            Destroy(mesh);
    }
}
