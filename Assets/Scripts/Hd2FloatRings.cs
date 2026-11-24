using UnityEngine;

/// <summary>
/// Health pickup look: the item hovers inside two crossing rings that orbit it, with a soft
/// glow disc on the floor. Once taken, the item is gone and the rings slow and dim, then
/// speed up and brighten as it recharges, so the timer reads from across the map.
/// </summary>
public class Hd2FloatRings : MonoBehaviour
{
    static readonly Color Green = new Color(0.3f, 0.95f, 0.45f, 1f);
    const float ItemHeight = 1.05f;

    Hd2HealthPickup pickup;
    Transform sphere;
    Transform gimbal;
    Transform ringA;
    Transform ringB;
    LineRenderer lineA;
    LineRenderer lineB;
    Material floorGlow;
    float spin;

    void Start()
    {
        pickup = GetComponent<Hd2HealthPickup>();
        sphere = transform.Find("Sphere");
        Hd2PickupLook.QuietOldLook(transform, 0.12f);

        gimbal = new GameObject("RingGimbal").transform;
        gimbal.SetParent(transform, false);
        gimbal.localPosition = new Vector3(0f, ItemHeight, 0f);

        lineA = Hd2PickupLook.Line(gimbal, "RingA", Green, 0.022f, 48, true);
        Hd2PickupLook.Polygon(lineA, 48, 0.33f);
        ringA = lineA.transform;
        lineB = Hd2PickupLook.Line(gimbal, "RingB", Green, 0.022f, 48, true);
        Hd2PickupLook.Polygon(lineB, 48, 0.33f);
        ringB = lineB.transform;

        var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        disc.name = "RingGlow";
        disc.transform.SetParent(transform, false);
        disc.transform.localPosition = new Vector3(0f, 0.015f, 0f);
        disc.transform.localScale = new Vector3(0.8f, 0.004f, 0.8f);
        floorGlow = Hd2PickupLook.Ghost(disc, new Color(Green.r, Green.g, Green.b, 0.15f));
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
        gimbal.localPosition = new Vector3(0f, ItemHeight + Mathf.Sin(time * 1.6f) * 0.04f, 0f);

        float pulse = 0.5f + 0.5f * Mathf.Sin(time * 3f);
        float alpha = ready ? 0.75f + 0.25f * pulse : 0.12f + 0.5f * progress;
        Hd2PickupLook.Tint(lineA, Green, alpha);
        Hd2PickupLook.Tint(lineB, Green, alpha);
        Hd2PickupLook.SetColor(floorGlow, new Color(Green.r, Green.g, Green.b, ready ? 0.14f + 0.06f * pulse : 0.04f + 0.08f * progress));

        if (sphere != null && sphere.gameObject.activeSelf)
            sphere.localPosition = new Vector3(0f, ItemHeight + Mathf.Sin(time * 1.6f) * 0.04f, 0f);
    }
}
