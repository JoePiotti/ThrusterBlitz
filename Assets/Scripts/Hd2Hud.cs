using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>
/// Player-only readout, low and to the left of the view. More rows can be added later.
/// Health is a green percent. Below 10 health that number turns red and blinks.
/// Blitz is the charge to a tenth, such as 2.3. The microphone is green while open and gray while closed,
/// with an On or Off label beside the icon.
/// </summary>
public class Hd2Hud : MonoBehaviour
{
    const float CriticalHealth = 10f;
    static readonly Color healthGreen = new Color(0.35f, 1f, 0.45f, 1f);
    static readonly Color healthRed = new Color(1f, 0.18f, 0.16f, 1f);
    static readonly Color blitzColor = new Color(0.45f, 0.9f, 1f, 1f);
    static readonly Color micGreen = new Color(0.3f, 0.95f, 0.4f, 1f);
    static readonly Color micGray = new Color(0.42f, 0.44f, 0.46f, 1f);
    static readonly string[] percents = BuildPercents();
    static readonly string[] tenths = BuildTenths();

    Hd2Health health;
    Hd2ThrustMeter meter;
    Hd2ControlMap controls;
    Camera ownerCamera;
    Canvas canvas;
    Transform view;
    TextMesh healthReadout;
    TextMesh blitzReadout;
    TextMesh micReadout;
    TextMesh banner;
    string killText;
    string deathText;
    float killUntil;
    float deathUntil;
    Material micMaterial;
    bool shownMic;
    bool micReady;
    int shownPercent = -1;
    int shownTenth = -1;
    bool shownCritical;
    bool blinkOn = true;

    void Awake()
    {
        health = GetComponent<Hd2Health>();
        meter = GetComponent<Hd2ThrustMeter>();
        controls = GetComponent<Hd2ControlMap>();
        Build();
    }

    void OnDestroy()
    {
        if (micMaterial != null)
            Destroy(micMaterial);
    }

    void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += CullToOwner;
        RenderPipelineManager.endCameraRendering += RestoreAfterCamera;
    }

    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= CullToOwner;
        RenderPipelineManager.endCameraRendering -= RestoreAfterCamera;
    }

    void LateUpdate()
    {
        RefreshHealth();
        RefreshBlitz();
        RefreshMic();
        RefreshBanner();
    }

    public void ShowKill(string enemyName)
    {
        if (string.IsNullOrEmpty(enemyName))
            return;

        killText = "You killed " + enemyName;
        killUntil = Time.unscaledTime + 3.5f;
        RefreshBanner();
    }

    public void ShowDeath(string killerName)
    {
        if (string.IsNullOrEmpty(killerName))
            return;

        deathText = "You were killed by " + killerName;
        deathUntil = Time.unscaledTime + 3.5f;
        RefreshBanner();
    }

    void RefreshHealth()
    {
        if (health == null || healthReadout == null)
            return;

        float current = health.Health;
        int percent = Mathf.RoundToInt(100f * current / Mathf.Max(1f, health.maxHealth));
        percent = Mathf.Clamp(percent, 0, percents.Length - 1);
        if (percent != shownPercent)
        {
            shownPercent = percent;
            healthReadout.text = percents[percent];
        }

        bool critical = current < CriticalHealth;
        bool on = !critical || Mathf.Repeat(Time.unscaledTime, 0.5f) < 0.28f;
        if (critical == shownCritical && on == blinkOn)
            return;

        shownCritical = critical;
        blinkOn = on;
        if (!critical)
            healthReadout.color = healthGreen;
        else if (on)
            healthReadout.color = healthRed;
        else
            healthReadout.color = new Color(healthRed.r, healthRed.g, healthRed.b, 0f);
    }

    void RefreshBlitz()
    {
        if (meter == null || blitzReadout == null)
            return;

        int tenth = Mathf.Clamp(Mathf.RoundToInt(meter.Charges * 10f), 0, 99);
        if (tenth == shownTenth)
            return;

        shownTenth = tenth;
        blitzReadout.text = tenths[tenth];
    }

    void RefreshMic()
    {
        if (controls == null)
            return;

        bool open = controls.MicOpen;
        if (micReady && open == shownMic)
            return;

        micReady = true;
        shownMic = open;
        Color tint = open ? micGreen : micGray;
        if (micMaterial != null)
            micMaterial.color = tint;
        if (micReadout != null)
        {
            micReadout.text = open ? "On" : "Off";
            micReadout.color = tint;
        }
    }

    static string[] BuildPercents()
    {
        var values = new string[101];
        for (int i = 0; i < values.Length; i++)
            values[i] = i + "%";
        return values;
    }

    static string[] BuildTenths()
    {
        var values = new string[100];
        for (int i = 0; i < values.Length; i++)
            values[i] = (i / 10) + "." + (i % 10);
        return values;
    }

    void RefreshBanner()
    {
        bool kill = !string.IsNullOrEmpty(killText) && Time.unscaledTime < killUntil;
        bool death = !string.IsNullOrEmpty(deathText) && Time.unscaledTime < deathUntil;
        if (!kill && !death)
        {
            if (banner != null)
                banner.gameObject.SetActive(false);
            return;
        }

        if (banner == null)
            banner = MakeReadout(view != null ? view : transform, new Vector3(0.36f, -0.2f, 0.9f), Color.white, TextAnchor.MiddleRight);
        banner.gameObject.SetActive(true);
        if (kill && death)
            banner.text = killText + "\n" + deathText;
        else
            banner.text = kill ? killText : deathText;
    }

    void Build()
    {
        ownerCamera = GetComponentInChildren<Camera>();
        view = ownerCamera != null ? ownerCamera.transform : transform;

        var root = new GameObject("Hud");
        root.transform.SetParent(view, false);
        root.transform.localPosition = new Vector3(-0.42f, -0.18f, 0.9f);
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale = Vector3.one * 0.001f;

        canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = ownerCamera;
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 16f;

        var rect = root.GetComponent<RectTransform>();
        rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = new Vector2(260f, 192f);

        AddIcon(rect, "Health", Resources.Load<Material>("HealthHudIcon"), 0f);
        AddIcon(rect, "Blitz", Resources.Load<Material>("BlitzHudIcon"), -64f);
        AddMic(rect, -128f);
        healthReadout = MakeReadout(view, new Vector3(-0.35f, -0.206f, 0.9f), healthGreen, TextAnchor.MiddleLeft);
        blitzReadout = MakeReadout(view, new Vector3(-0.35f, -0.27f, 0.9f), blitzColor, TextAnchor.MiddleLeft);
        micReadout = MakeReadout(view, new Vector3(-0.35f, -0.334f, 0.9f), micGray, TextAnchor.MiddleLeft);
        healthReadout.text = percents[100];
        blitzReadout.text = tenths[0];
        micReadout.text = "Off";
        banner = MakeReadout(view, new Vector3(0.36f, -0.2f, 0.9f), Color.white, TextAnchor.MiddleRight);
        banner.gameObject.SetActive(false);
    }

    static TextMesh MakeReadout(Transform parent, Vector3 local, Color color, TextAnchor anchor)
    {
        bool bannerStyle = anchor != TextAnchor.MiddleLeft;
        var readout = new GameObject(bannerStyle ? "Banner" : "Value");
        readout.transform.SetParent(parent, false);
        readout.transform.localPosition = local;
        readout.transform.localRotation = Quaternion.identity;
        readout.transform.localScale = Vector3.one;
        var text = readout.AddComponent<TextMesh>();
        if (readout.GetComponent<MeshFilter>() == null)
            readout.AddComponent<MeshFilter>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = bannerStyle ? 72 : 96;
        text.characterSize = bannerStyle ? 0.006f : 0.0023f;
        text.anchor = anchor;
        text.alignment = anchor == TextAnchor.MiddleLeft ? TextAlignment.Left : TextAlignment.Right;
        text.color = color;
        var renderer = text.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            if (text.font != null)
                renderer.sharedMaterial = text.font.material;
        }

        return text;
    }

    void AddMic(RectTransform parent, float y)
    {
        var shared = Resources.Load<Material>("MicHudIcon");
        var iconObject = new GameObject("Mic");
        var iconRect = iconObject.AddComponent<RectTransform>();
        iconRect.SetParent(parent, false);
        iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 1f);
        iconRect.pivot = new Vector2(0f, 1f);
        iconRect.anchoredPosition = new Vector2(0f, y);
        iconRect.sizeDelta = new Vector2(52f, 52f);
        var image = iconObject.AddComponent<RawImage>();
        image.raycastTarget = false;
        image.color = Color.white;
        if (shared == null)
            return;

        micMaterial = new Material(shared);
        image.texture = shared.mainTexture;
        image.material = micMaterial;
        micMaterial.color = micGray;
    }

    static void AddIcon(RectTransform parent, string rowName, Material iconMaterial, float y)
    {
        var iconObject = new GameObject(rowName + "Icon");
        var iconRect = iconObject.AddComponent<RectTransform>();
        iconRect.SetParent(parent, false);
        iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 1f);
        iconRect.pivot = new Vector2(0f, 1f);
        iconRect.anchoredPosition = new Vector2(0f, y);
        iconRect.sizeDelta = new Vector2(52f, 52f);
        var image = iconObject.AddComponent<RawImage>();
        image.raycastTarget = false;
        image.color = Color.white;
        if (iconMaterial != null)
        {
            image.texture = iconMaterial.mainTexture;
            image.material = iconMaterial;
        }
    }

    void CullToOwner(ScriptableRenderContext context, Camera camera)
    {
        if (canvas == null)
            return;
        canvas.enabled = camera == ownerCamera;
    }

    void RestoreAfterCamera(ScriptableRenderContext context, Camera camera)
    {
        if (canvas != null)
            canvas.enabled = true;
    }
}
