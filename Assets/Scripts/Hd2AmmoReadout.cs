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

        // Bots' guns show no display: nobody reads it, and it saves the text updates.
        if (gun.GetComponentInParent<Hd2EnemyBot>() != null)
        {
            HideDisplays(gun);
            return null;
        }

        Transform display = FindChild(gun, "display");
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
                renderer.sharedMaterial = WorldTextMaterial(label.font);
        }
    }

    static void HideDisplays(Transform gun)
    {
        TextMesh[] texts = gun.GetComponentsInChildren<TextMesh>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            var renderer = texts[i].GetComponent<MeshRenderer>();
            if (renderer != null)
                renderer.enabled = false;
            texts[i].enabled = false;
        }
    }

    static Material worldText;
    static Font worldTextFont;

    /// <summary>
    /// Material for 3D text that walls hide, like any other object. The built-in font
    /// material draws on top of everything, so gun displays showed enemies through walls.
    /// </summary>
    public static Material WorldTextMaterial(Font font)
    {
        if (font == null)
            return null;
        if (worldText != null && worldTextFont == font)
            return worldText;

        Material source = Resources.Load<Material>("WorldText");
        if (source == null)
            return font.material;
        worldText = new Material(source) { name = "WorldText (" + font.name + ")" };
        worldTextFont = font;
        worldText.mainTexture = font.material.mainTexture;
        // A dynamic font can rebuild its glyph texture; keep pointing at the current one.
        Font.textureRebuilt -= OnFontRebuilt;
        Font.textureRebuilt += OnFontRebuilt;
        return worldText;
    }

    static void OnFontRebuilt(Font font)
    {
        if (worldText != null && font == worldTextFont)
            worldText.mainTexture = font.material.mainTexture;
    }

    /// <summary>Progress ring around the display, 0..1 (0 hides it). Call every frame.</summary>
    public static void Ring(TextMesh label, float fill)
    {
        Hd2AmmoRing.Set(label, fill);
    }

    /// <summary>How far a timed wait has run, 0..1, from when it ends and how long it lasts.</summary>
    public static float Progress(float endsAt, float duration)
    {
        if (duration <= 0.0001f)
            return 0f;
        return Mathf.Clamp01(1f - (endsAt - Time.time) / duration);
    }

    public static void Show(TextMesh label, int rounds, bool reloading)
    {
        if (label == null)
            return;

        label.text = reloading ? "R" : rounds.ToString();
    }

    static Transform FindChild(Transform root, string name)
    {
        if (root == null)
            return null;
        if (root.name == name)
            return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindChild(root.GetChild(i), name);
            if (found != null)
                return found;
        }

        return null;
    }
}
