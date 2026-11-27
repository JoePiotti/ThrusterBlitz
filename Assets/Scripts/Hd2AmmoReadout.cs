using UnityEngine;

/// <summary>
/// Blue rounds text on a gun. Uses the display empty on the grip when there is one.
/// Shows the rounds left, or R while reloading. Does not move that empty.
/// </summary>
public static class Hd2AmmoReadout
{
    public static Hd2Text Bind(Transform gun)
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

        Hd2Text label = Hd2Text.InChildren(existing, false);
        if (label != null)
            Style(label);
        return label;
    }

    public static Hd2Text Style(Transform host)
    {
        if (host == null)
            return null;

        Hd2Text label = Hd2Text.InChildren(host, false);
        if (label == null)
        {
            label = Hd2Text.On(host.gameObject, false);
            label.fontSize = 32;
            label.characterSize = 0.005f;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
        }

        Style(label);
        return label;
    }

    public static void Style(Hd2Text label)
    {
        if (label == null)
            return;

        // World text: walls hide it (a gun display must not show enemies through walls).
        label.overlay = false;
        label.color = new Color(0.3f, 0.75f, 1f, 1f);
        label.visible = true;
    }

    static void HideDisplays(Transform gun)
    {
        TextMesh[] texts = gun.GetComponentsInChildren<TextMesh>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            var renderer = texts[i].GetComponent<MeshRenderer>();
            if (renderer != null)
                renderer.enabled = false;
        }

        Hd2Text[] labels = gun.GetComponentsInChildren<Hd2Text>(true);
        for (int i = 0; i < labels.Length; i++)
            labels[i].visible = false;
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
    public static void Ring(Hd2Text label, float fill)
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

    public static void Show(Hd2Text label, int rounds, bool reloading)
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
