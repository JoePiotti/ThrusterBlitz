using TMPro;
using UnityEngine;

/// <summary>
/// 3D text drawn with TextMeshPro (signed-distance-field glyphs that stay sharp at any size
/// and distance in VR), with the same settings the game used on Unity's legacy TextMesh:
/// text, color, fontSize, characterSize, anchor, alignment, fontStyle and richText. The
/// numbers mean the same thing: the TMP font (LiberationSans) measures like the legacy
/// Arial, so fontSize is passed through, characterSize becomes the scale, and rich-text
/// &lt;size=N&gt; tags keep their meaning.
///
/// The TextMeshPro lives on a child ("Text") so this object's transform, and any old
/// MeshRenderer left on it, are untouched. Overlay text (HUD, banners) draws on top of the
/// world like the legacy font material did; world text (gun displays, scoreboards) is hidden
/// by walls. Materials: Resources/Text/Hd2TextOverlay and Hd2TextWorld.
/// </summary>
[DisallowMultipleComponent]
public class Hd2Text : MonoBehaviour
{
    const int LegacyDefaultSize = 13;

    string textValue = "";
    Color colorValue = Color.white;
    int fontSizeValue;
    float characterSizeValue = 1f;
    TextAnchor anchorValue = TextAnchor.UpperLeft;
    TextAlignment alignmentValue = TextAlignment.Left;
    FontStyle fontStyleValue = FontStyle.Normal;
    bool richTextValue = true;
    bool overlayValue;
    TextMeshPro tmp;
    bool dirty = true;

    static Material overlayMaterial;
    static Material worldMaterial;

    public string text { get => textValue; set { if (textValue != value) { textValue = value ?? ""; dirty = true; Apply(); } } }
    public Color color { get => colorValue; set { colorValue = value; if (tmp != null) tmp.color = value; } }
    public int fontSize { get => fontSizeValue; set { if (fontSizeValue != value) { fontSizeValue = value; dirty = true; Apply(); } } }
    public float characterSize { get => characterSizeValue; set { characterSizeValue = value; if (tmp != null) tmp.transform.localScale = Vector3.one * value; } }
    public TextAnchor anchor { get => anchorValue; set { if (anchorValue != value) { anchorValue = value; dirty = true; Apply(); } } }
    public TextAlignment alignment { get => alignmentValue; set { if (alignmentValue != value) { alignmentValue = value; dirty = true; Apply(); } } }
    public FontStyle fontStyle { get => fontStyleValue; set { if (fontStyleValue != value) { fontStyleValue = value; dirty = true; Apply(); } } }
    public bool richText { get => richTextValue; set { if (richTextValue != value) { richTextValue = value; dirty = true; Apply(); } } }

    /// <summary>True: drawn over the world (HUD, banners). False: hidden by walls.</summary>
    public bool overlay
    {
        get => overlayValue;
        set
        {
            overlayValue = value;
            if (tmp != null)
                tmp.fontSharedMaterial = MaterialFor(value);
        }
    }

    /// <summary>The TextMeshPro that draws this text.</summary>
    public TextMeshPro Tmp { get { Ensure(); return tmp; } }

    /// <summary>The renderer that draws this text (on the "Text" child).</summary>
    public MeshRenderer Renderer { get { Ensure(); return tmp != null ? tmp.GetComponent<MeshRenderer>() : null; } }

    /// <summary>Shows or hides the text without touching the GameObject.</summary>
    public bool visible
    {
        get { var r = Renderer; return r != null && r.enabled; }
        set { var r = Renderer; if (r != null) r.enabled = value; }
    }

    /// <summary>
    /// The text on this object: an existing Hd2Text, a legacy TextMesh (from a prefab) taken
    /// over with its settings, or a new one.
    /// </summary>
    public static Hd2Text On(GameObject go, bool overlay)
    {
        if (go == null)
            return null;
        var text = go.GetComponent<Hd2Text>();
        if (text != null)
            return text;

        var legacy = go.GetComponent<TextMesh>();
        text = go.AddComponent<Hd2Text>();
        text.overlayValue = overlay;
        if (legacy != null)
        {
            text.textValue = legacy.text ?? "";
            text.colorValue = legacy.color;
            text.fontSizeValue = legacy.fontSize;
            text.characterSizeValue = legacy.characterSize;
            text.anchorValue = legacy.anchor;
            text.alignmentValue = legacy.alignment;
            text.fontStyleValue = legacy.fontStyle;
            text.richTextValue = legacy.richText;
            var oldRenderer = go.GetComponent<MeshRenderer>();
            if (oldRenderer != null)
                oldRenderer.enabled = false;
            Destroy(legacy);
        }

        text.Ensure();
        return text;
    }

    /// <summary>Like GetComponentInChildren for Hd2Text, taking over a legacy TextMesh if that is all there is.</summary>
    public static Hd2Text InChildren(Transform root, bool overlay)
    {
        if (root == null)
            return null;
        var text = root.GetComponentInChildren<Hd2Text>(true);
        if (text != null)
            return text;
        var legacy = root.GetComponentInChildren<TextMesh>(true);
        return legacy != null ? On(legacy.gameObject, overlay) : null;
    }

    /// <summary>World-space bounds of the drawn text.</summary>
    public Bounds bounds
    {
        get
        {
            var r = Renderer;
            return r != null ? r.bounds : new Bounds(transform.position, Vector3.zero);
        }
    }

    void Awake()
    {
        Ensure();
    }

    void Ensure()
    {
        if (tmp != null)
            return;

        var child = new GameObject("Text");
        child.layer = gameObject.layer;
        child.transform.SetParent(transform, false);
        tmp = child.AddComponent<TextMeshPro>();
        if (TMP_Settings.defaultFontAsset != null)
            tmp.font = TMP_Settings.defaultFontAsset;
        tmp.fontSharedMaterial = MaterialFor(overlayValue);
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.isOrthographic = false;
        tmp.enableKerning = true;
        tmp.extraPadding = true;
        var renderer = child.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        tmp.color = colorValue;
        tmp.transform.localScale = Vector3.one * characterSizeValue;
        dirty = true;
        Apply();
    }

    static Material MaterialFor(bool overlay)
    {
        if (overlay)
        {
            if (overlayMaterial == null)
                overlayMaterial = Resources.Load<Material>("Text/Hd2TextOverlay");
            return overlayMaterial != null ? overlayMaterial : TMP_Settings.defaultFontAsset != null ? TMP_Settings.defaultFontAsset.material : null;
        }

        if (worldMaterial == null)
            worldMaterial = Resources.Load<Material>("Text/Hd2TextWorld");
        return worldMaterial != null ? worldMaterial : TMP_Settings.defaultFontAsset != null ? TMP_Settings.defaultFontAsset.material : null;
    }

    void Apply()
    {
        if (tmp == null || !dirty)
            return;
        dirty = false;

        tmp.richText = richTextValue;
        tmp.fontSize = fontSizeValue > 0 ? fontSizeValue : LegacyDefaultSize;
        tmp.fontStyle = Style(fontStyleValue);
        tmp.alignment = Alignment(anchorValue, alignmentValue);
        tmp.text = textValue;

        // The text block sits on this object's origin the way the legacy anchor placed it:
        // the rect is exactly the text's size, pivoted at the anchor's corner or edge.
        var rect = tmp.rectTransform;
        rect.pivot = Pivot(anchorValue);
        rect.anchoredPosition = Vector2.zero;
        rect.localPosition = Vector3.zero;
        Vector2 size = string.IsNullOrEmpty(textValue) ? Vector2.zero : tmp.GetPreferredValues(textValue);
        rect.sizeDelta = size;
    }

    static FontStyles Style(FontStyle style)
    {
        switch (style)
        {
            case FontStyle.Bold: return FontStyles.Bold;
            case FontStyle.Italic: return FontStyles.Italic;
            case FontStyle.BoldAndItalic: return FontStyles.Bold | FontStyles.Italic;
            default: return FontStyles.Normal;
        }
    }

    static Vector2 Pivot(TextAnchor anchor)
    {
        switch (anchor)
        {
            case TextAnchor.UpperLeft: return new Vector2(0f, 1f);
            case TextAnchor.UpperCenter: return new Vector2(0.5f, 1f);
            case TextAnchor.UpperRight: return new Vector2(1f, 1f);
            case TextAnchor.MiddleLeft: return new Vector2(0f, 0.5f);
            case TextAnchor.MiddleCenter: return new Vector2(0.5f, 0.5f);
            case TextAnchor.MiddleRight: return new Vector2(1f, 0.5f);
            case TextAnchor.LowerLeft: return new Vector2(0f, 0f);
            case TextAnchor.LowerCenter: return new Vector2(0.5f, 0f);
            default: return new Vector2(1f, 0f);
        }
    }

    // Lines line up by the TextMesh alignment inside a block placed by the anchor.
    static TextAlignmentOptions Alignment(TextAnchor anchor, TextAlignment alignment)
    {
        int row = anchor <= TextAnchor.UpperRight ? 0 : anchor <= TextAnchor.MiddleRight ? 1 : 2;
        if (alignment == TextAlignment.Center)
            return row == 0 ? TextAlignmentOptions.Top : row == 1 ? TextAlignmentOptions.Center : TextAlignmentOptions.Bottom;
        if (alignment == TextAlignment.Right)
            return row == 0 ? TextAlignmentOptions.TopRight : row == 1 ? TextAlignmentOptions.Right : TextAlignmentOptions.BottomRight;
        return row == 0 ? TextAlignmentOptions.TopLeft : row == 1 ? TextAlignmentOptions.Left : TextAlignmentOptions.BottomLeft;
    }
}
