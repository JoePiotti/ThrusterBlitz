using UnityEngine;

/// <summary>
/// The look of every pickup in play: the item hovers inside two crossing rings that orbit
/// it, over a glowing disc on the floor. Once taken, the item is gone, the rings slow and
/// dim, and the floor disc becomes a pie that fills clockwise as the pickup recharges, so
/// standing near it you can look down and see how long is left. The rings speed up and
/// brighten as it gets close, so the timer also reads from across the map.
/// </summary>
public class Hd2FloatRings : MonoBehaviour
{
    const int PieSegments = 48;
    const float PieRadius = 0.42f;

    IHd2Pickup pickup;
    Color color = Color.white;
    Transform sphere;
    float itemHeight = 1.05f;
    Transform gimbal;
    Transform ringA;
    Transform ringB;
    LineRenderer lineA;
    LineRenderer lineB;
    Material discBack;
    Material pieMaterial;
    Mesh pie;
    float pieShown = -1f;
    float spin;

    void Start()
    {
        pickup = GetComponent<IHd2Pickup>();
        if (pickup != null)
            color = Bright(pickup.LookColor);
        sphere = transform.Find("Sphere");
        if (sphere != null)
            itemHeight = sphere.localPosition.y;
        Hd2PickupLook.QuietOldLook(transform, 0.12f);

        gimbal = new GameObject("RingGimbal").transform;
        gimbal.SetParent(transform, false);
        gimbal.localPosition = new Vector3(0f, itemHeight, 0f);

        lineA = Hd2PickupLook.Line(gimbal, "RingA", color, 0.022f, 48, true);
        Hd2PickupLook.Polygon(lineA, 48, 0.33f);
        ringA = lineA.transform;
        lineB = Hd2PickupLook.Line(gimbal, "RingB", color, 0.022f, 48, true);
        Hd2PickupLook.Polygon(lineB, 48, 0.33f);
        ringB = lineB.transform;

        // Floor: a faint full disc, with the refill pie drawn over it.
        discBack = FloorDisc("RingGlow", 0.012f, out Mesh back);
        BuildPie(back, 1f);
        pieMaterial = FloorDisc("RefillPie", 0.016f, out pie);
        BuildPie(pie, 1f);
    }

    // Pale pickup colors (the weapons are near white) still need to read as glowing lines.
    static Color Bright(Color c)
    {
        float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
        return max > 0.001f ? new Color(c.r / max, c.g / max, c.b / max, 1f) : Color.white;
    }

    Material FloorDisc(string discName, float height, out Mesh mesh)
    {
        var go = new GameObject(discName);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, height, 0f);
        mesh = new Mesh { name = discName };
        mesh.MarkDynamic();
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>();
        return Hd2PickupLook.Ghost(go, new Color(color.r, color.g, color.b, 0.1f));
    }

    // A fan from 12 o'clock, clockwise seen from above, covering `fill` of the circle.
    static readonly Vector3[] pieVerts = new Vector3[PieSegments + 2];
    static readonly int[] pieTris = new int[PieSegments * 3];

    static void BuildPie(Mesh mesh, float fill)
    {
        int segments = Mathf.Clamp(Mathf.CeilToInt(PieSegments * Mathf.Clamp01(fill)), 0, PieSegments);
        pieVerts[0] = Vector3.zero;
        for (int i = 0; i <= segments; i++)
        {
            float t = Mathf.Min(i / (float)PieSegments, fill);
            float angle = t * Mathf.PI * 2f;
            pieVerts[i + 1] = new Vector3(Mathf.Sin(angle) * PieRadius, 0f, Mathf.Cos(angle) * PieRadius);
        }

        for (int i = 0; i < PieSegments; i++)
        {
            bool used = i < segments;
            pieTris[i * 3] = 0;
            pieTris[i * 3 + 1] = used ? i + 1 : 0;
            pieTris[i * 3 + 2] = used ? i + 2 : 0;
        }

        for (int i = segments + 2; i < pieVerts.Length; i++)
            pieVerts[i] = Vector3.zero;

        mesh.Clear();
        mesh.vertices = pieVerts;
        mesh.triangles = pieTris;
        mesh.RecalculateBounds();
    }

    void LateUpdate()
    {
        if (pickup == null)
            return;
        Hd2PickupLook.HideOldRing(transform);

        bool ready = pickup.Ready;
        float progress = pickup.Progress;
        float time = Time.time;

        // Faster and brighter the closer it is to ready.
        float speed = ready ? 1f : Mathf.Lerp(0.15f, 0.8f, progress);
        spin += Time.deltaTime * 90f * speed;
        ringA.localRotation = Quaternion.Euler(60f, spin, 0f);
        ringB.localRotation = Quaternion.Euler(-60f, -spin * 0.8f + 90f, 0f);
        gimbal.localRotation = Quaternion.Euler(0f, spin * 0.25f, 0f);
        float bob = Mathf.Sin(time * 1.6f) * 0.04f;
        gimbal.localPosition = new Vector3(0f, itemHeight + bob, 0f);

        float pulse = 0.5f + 0.5f * Mathf.Sin(time * 3f);
        float alpha = ready ? 0.75f + 0.25f * pulse : 0.12f + 0.5f * progress;
        Hd2PickupLook.Tint(lineA, color, alpha);
        Hd2PickupLook.Tint(lineB, color, alpha);

        // The pie: full and pulsing when ready; while recharging it fills clockwise and
        // brightens as it goes.
        float fill = ready ? 1f : progress;
        if (Mathf.Abs(fill - pieShown) > 0.004f)
        {
            BuildPie(pie, fill);
            pieShown = fill;
        }

        Hd2PickupLook.SetColor(pieMaterial, new Color(color.r, color.g, color.b,
            ready ? 0.16f + 0.08f * pulse : 0.1f + 0.2f * progress));
        Hd2PickupLook.SetColor(discBack, new Color(color.r, color.g, color.b, ready ? 0f : 0.05f));

        if (sphere != null && sphere.gameObject.activeSelf)
            sphere.localPosition = new Vector3(sphere.localPosition.x, itemHeight + bob, sphere.localPosition.z);
    }

    void OnDestroy()
    {
        if (pie != null)
            Destroy(pie);
    }
}
