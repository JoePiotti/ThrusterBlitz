using UnityEngine;

/// <summary>
/// Thrust pickup look: the item floats in a column of light over a glowing hex pad, bobbing
/// and turning slowly, with a scan ring sweeping up and down it. Once taken, the pad's hex
/// outline redraws itself as the pickup recharges and the column brightens with it, so the
/// timer reads from across the map.
/// </summary>
public class Hd2HoloCradle : MonoBehaviour
{
    static readonly Color Cyan = new Color(0.25f, 0.75f, 1f, 1f);
    const float ItemHeight = 1.05f;

    Hd2ThrustPickup pickup;
    Transform sphere;
    LineRenderer hexOuter;
    LineRenderer hexInner;
    LineRenderer scan;
    Material beam;
    float lastFill = -1f;

    void Start()
    {
        pickup = GetComponent<Hd2ThrustPickup>();
        sphere = transform.Find("Sphere");
        Hd2PickupLook.QuietOldLook(transform, 0f);

        hexOuter = Hd2PickupLook.Line(transform, "HoloHexOuter", Cyan, 0.03f, 6, true);
        hexOuter.transform.localPosition = new Vector3(0f, 0.02f, 0f);
        Hd2PickupLook.Polygon(hexOuter, 6, 0.45f);
        hexInner = Hd2PickupLook.Line(transform, "HoloHexInner", Cyan, 0.015f, 6, true);
        hexInner.transform.localPosition = new Vector3(0f, 0.025f, 0f);
        Hd2PickupLook.Polygon(hexInner, 6, 0.3f);

        var column = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        column.name = "HoloBeam";
        column.transform.SetParent(transform, false);
        column.transform.localPosition = new Vector3(0f, 0.75f, 0f);
        column.transform.localScale = new Vector3(0.5f, 0.75f, 0.5f);
        beam = Hd2PickupLook.Ghost(column, new Color(Cyan.r, Cyan.g, Cyan.b, 0.08f));

        scan = Hd2PickupLook.Line(transform, "HoloScan", Cyan, 0.012f, 32, true);
        Hd2PickupLook.Polygon(scan, 32, 0.26f);
    }

    void LateUpdate()
    {
        if (pickup == null)
            return;
        Hd2PickupLook.HideOldRing(transform);

        bool ready = pickup.Ready;
        float progress = pickup.Progress;
        float time = Time.time;
        float pulse = 0.5f + 0.5f * Mathf.Sin(time * 3f);

        // The pad outline redraws as it recharges; full and pulsing when ready.
        float fill = ready ? 1f : progress;
        if (!Mathf.Approximately(fill, lastFill))
        {
            Hd2PickupLook.Polygon(hexOuter, 6, 0.45f, fill);
            lastFill = fill;
        }

        Hd2PickupLook.Tint(hexOuter, Cyan, ready ? 0.65f + 0.35f * pulse : 0.35f + 0.4f * progress);
        Hd2PickupLook.Tint(hexInner, Cyan, ready ? 0.4f : 0.12f);
        Hd2PickupLook.SetColor(beam, new Color(Cyan.r, Cyan.g, Cyan.b, ready ? 0.1f + 0.05f * pulse : 0.02f + 0.06f * progress));

        // Scan ring sweeps the item while it's there.
        scan.enabled = ready;
        if (ready)
        {
            float sweep = Mathf.PingPong(time * 0.6f, 1f);
            scan.transform.localPosition = new Vector3(0f, ItemHeight - 0.22f + sweep * 0.44f, 0f);
            Hd2PickupLook.Tint(scan, Cyan, 0.8f - 0.5f * Mathf.Abs(sweep - 0.5f) * 2f);
        }

        if (sphere != null && sphere.gameObject.activeSelf)
        {
            sphere.localPosition = new Vector3(0f, ItemHeight + Mathf.Sin(time * 1.6f) * 0.04f, 0f);
            sphere.localRotation = Quaternion.Euler(0f, time * 40f, 0f);
        }
    }
}
