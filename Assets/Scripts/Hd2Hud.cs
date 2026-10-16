using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>
/// Player-only readout, low and to the left of the view. More rows can be added later.
/// Health is a green percent. Below 10 health that number turns red and blinks.
/// Blitz is the charge to a tenth, such as 2.3. The microphone is green while open and gray while closed.
/// Icons match the pickups. No labels.
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
    Text healthText;
    Text blitzText;
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
    }

    void RefreshHealth()
    {
        if (health == null || healthText == null)
            return;

        float current = health.Health;
        int percent = Mathf.RoundToInt(100f * current / Mathf.Max(1f, health.maxHealth));
        percent = Mathf.Clamp(percent, 0, percents.Length - 1);
        if (percent != shownPercent)
        {
            shownPercent = percent;
            healthText.text = percents[percent];
        }

        bool critical = current < CriticalHealth;
        bool on = !critical || Mathf.Repeat(Time.unscaledTime, 0.5f) < 0.28f;
        if (critical == shownCritical && on == blinkOn)
            return;

        shownCritical = critical;
        blinkOn = on;
        if (!critical)
            healthText.color = healthGreen;
        else if (on)
            healthText.color = healthRed;
        else
            healthText.color = new Color(healthRed.r, healthRed.g, healthRed.b, 0f);
    }

    void RefreshBlitz()
    {
        if (meter == null || blitzText == null)
            return;

        int tenth = Mathf.Clamp(Mathf.RoundToInt(meter.Charges * 10f), 0, 99);
        if (tenth == shownTenth)
            return;

        shownTenth = tenth;
        blitzText.text = tenths[tenth];
    }

    void RefreshMic()
    {
        if (micMaterial == null || controls == null)
            return;

        bool open = controls.MicOpen;
        if (micReady && open == shownMic)
            return;

        micReady = true;
        shownMic = open;
        micMaterial.color = open ? micGreen : micGray;
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

    void Build()
    {
        ownerCamera = GetComponentInChildren<Camera>();
        Transform view = ownerCamera != null ? ownerCamera.transform : transform;

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

        healthText = AddRow(rect, "Health", Resources.Load<Material>("HealthHudIcon"), 0f, healthGreen);
        blitzText = AddRow(rect, "Blitz", Resources.Load<Material>("BlitzHudIcon"), -64f, blitzColor);
        AddMic(rect, -128f);
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

    static Text AddRow(RectTransform parent, string rowName, Material iconMaterial, float y, Color color)
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

        var textObject = new GameObject(rowName);
        var textRect = textObject.AddComponent<RectTransform>();
        textRect.SetParent(parent, false);
        textRect.anchorMin = textRect.anchorMax = new Vector2(0f, 1f);
        textRect.pivot = new Vector2(0f, 1f);
        textRect.anchoredPosition = new Vector2(60f, y);
        textRect.sizeDelta = new Vector2(180f, 52f);
        var text = textObject.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 36;
        text.alignment = TextAnchor.MiddleLeft;
        text.color = color;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        var shadow = textObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
        shadow.effectDistance = new Vector2(1.5f, -1.5f);
        return text;
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
