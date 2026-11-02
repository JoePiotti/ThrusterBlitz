using UnityEngine;

/// <summary>
/// Blue rounds text on a gun. Uses the display empty on the grip when there is one.
/// Shows the rounds left, or R while reloading. Does not move that empty.
/// </summary>
public static class Hd2AmmoReadout
{
    public static TextMesh Bind(Transform gun)
    {
        if (gun == null)
            return null;

        Transform display = gun.Find("display");
        if (display != null)
            return Style(display);

        Transform existing = gun.Find("AmmoReadout");
        if (existing == null)
            return null;

        TextMesh label = existing.GetComponent<TextMesh>();
        if (label == null)
            label = existing.GetComponentInChildren<TextMesh>(true);
        if (label != null)
            Style(label);
        return label;
    }

    public static TextMesh Style(Transform host)
    {
        if (host == null)
            return null;

        TextMesh label = host.GetComponent<TextMesh>();
        if (label == null)
            label = host.GetComponentInChildren<TextMesh>(true);
        if (label == null)
        {
            label = host.gameObject.AddComponent<TextMesh>();
            label.fontSize = 32;
            label.characterSize = 0.005f;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
        }

        Style(label);
        return label;
    }

    public static void Style(TextMesh label)
    {
        if (label == null)
            return;

        if (label.GetComponent<MeshFilter>() == null)
            label.gameObject.AddComponent<MeshFilter>();

        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.color = new Color(0.3f, 0.75f, 1f, 1f);
        MeshRenderer renderer = label.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.enabled = true;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            if (label.font != null)
                renderer.sharedMaterial = label.font.material;
        }
    }

    public static void Show(TextMesh label, int rounds, bool reloading)
    {
        if (label == null)
            return;

        label.text = reloading ? "R" : rounds.ToString();
    }
}
