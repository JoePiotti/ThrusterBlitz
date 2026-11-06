using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>
/// Player-only readout, low in the view. The controls menu puts it on the left, the right, or hides it.
/// K/D and the current life's kill streak sit under the microphone.
/// Health is a whole percent, such as 84%. Below 10 health that number turns red and blinks.
/// Thrust is whole charges out of the current maximum, such as 2/3 or 2/5.
/// The microphone is green while open and gray while closed, with an On or Off label beside the icon.
/// It follows the button mapped to the microphone.
/// During domination, A B C sit just above that stack. Each letter is white, blue, or red
/// with the matching point. The round score and match clock sit above those letters.
/// Control point shows the score and the clock, and leaves those letters off.
/// A shot flashes translucent red around the edges of this view. Left and right
/// follow the side the shot came from. The top strengthens from the front, the bottom from behind.
/// </summary>
public class Hd2Hud : MonoBehaviour
{
    public enum Side
    {
        Left,
        Right,
        None
    }
    const float CriticalHealth = 10f;
    static readonly Color healthGreen = new Color(0.35f, 1f, 0.45f, 1f);
    static readonly Color healthRed = new Color(1f, 0.18f, 0.16f, 1f);
    static readonly Color blitzColor = new Color(0.45f, 0.9f, 1f, 1f);
    static readonly Color micGreen = new Color(0.3f, 0.95f, 0.4f, 1f);
    static readonly Color micGray = new Color(0.42f, 0.44f, 0.46f, 1f);
    static readonly string[] percents = BuildPercents();
    static readonly string[] redLines = { "Red 0", "Red 1", "Red 2", "Red 3" };
    static readonly string[] blueLines = { "Blue 0", "Blue 1", "Blue 2", "Blue 3" };
    static readonly Color scoreRed = new Color(0.9f, 0.22f, 0.16f, 1f);
    static readonly Color scoreBlue = new Color(0.35f, 0.62f, 1f, 1f);
    static string[] clocks;

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
    TextMesh scoreReadout;
    int kills;
    int deaths;
    int streak;
    int bestStreak;
    bool scoreDirty = true;
    string killText;
    string deathText;
    float killUntil;
    float deathUntil;
    Material micMaterial;
    bool shownMic;
    bool micReady;
    int shownPercent = -1;
    int shownCharges = -1;
    int shownMax = -1;
    bool shownCritical;
    bool blinkOn = true;
    bool dominationReady;
    TextMesh pointA;
    TextMesh pointB;
    TextMesh pointC;
    Hd2TeamButton buttonA;
    Hd2TeamButton buttonB;
    Hd2TeamButton buttonC;
    int shownA = -1;
    int shownB = -1;
    int shownC = -1;
    TextMesh redScore;
    TextMesh blueScore;
    TextMesh clockReadout;
    int shownRed = -1;
    int shownBlue = -1;
    int shownClock = -1;
    Side side = Side.Left;
    const float HitFadeSeconds = 0.45f;
    const float HitBaseAlpha = 0.2f;
    const float HitSideAlpha = 0.4f;
    Transform hitLeft;
    Transform hitRight;
    Transform hitTop;
    Transform hitBottom;
    Material hitMaterial;
    Texture2D hitFade;
    readonly MaterialPropertyBlock[] hitBlocks = new MaterialPropertyBlock[4];
    readonly Renderer[] hitRenderers = new Renderer[4];
    float hitStrength;
    float trackedHealth = -1f;
    bool watchingHealth;
    float fromLeft;
    float fromRight;
    float fromFront;
    float fromBack;

    public Side Placement
    {
        get { return side; }
        set
        {
            if (side == value)
                return;
            side = value;
            ApplyPlacement();
        }
    }

    void Awake()
    {
        Build();
    }

    void Start()
    {
        Bind();
    }

    void Bind()
    {
        if (health == null)
            health = GetComponent<Hd2Health>();
        if (meter == null)
            meter = GetComponent<Hd2ThrustMeter>();
        if (controls == null)
            controls = GetComponent<Hd2ControlMap>();
        if (health != null && !watchingHealth)
        {
            health.Changed += OnDamaged;
            trackedHealth = health.Health;
            watchingHealth = true;
        }
    }

    void OnDestroy()
    {
        if (health != null && watchingHealth)
            health.Changed -= OnDamaged;
        if (micMaterial != null)
            Destroy(micMaterial);
        if (hitMaterial != null)
            Destroy(hitMaterial);
        if (hitFade != null)
            Destroy(hitFade);
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
        Bind();
        RefreshHealth();
        RefreshBlitz();
        RefreshMic();
        RefreshDomination();
        RefreshMatch();
        RefreshBanner();
        RefreshHitFlash();
        RefreshScore();
    }

    public int BestKillStreak => bestStreak;

    public void AddKill()
    {
        kills++;
        streak++;
        if (streak > bestStreak)
            bestStreak = streak;
        scoreDirty = true;
        RefreshScore();
    }

    public void AddDeath()
    {
        deaths++;
        scoreDirty = true;
        RefreshScore();
    }

    public void BeginLife()
    {
        if (streak == 0)
            return;
        streak = 0;
        scoreDirty = true;
        RefreshScore();
    }

    void RefreshScore()
    {
        if (!scoreDirty || scoreReadout == null)
            return;
        scoreDirty = false;
        scoreReadout.text = "K/D " + kills + "/" + deaths + "  KS " + streak;
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
        deathText = string.IsNullOrEmpty(killerName) ? "You died" : "You were killed by " + killerName;
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

        int whole = Mathf.FloorToInt(meter.Charges + 0.0001f);
        int max = Mathf.Max(1, meter.MaxCharges);
        whole = Mathf.Clamp(whole, 0, max);
        if (whole == shownCharges && max == shownMax)
            return;

        shownCharges = whole;
        shownMax = max;
        blitzReadout.text = whole + "/" + max;
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

    void RefreshDomination()
    {
        if (!dominationReady)
        {
            var all = Hd2TeamButton.All;
            for (int i = 0; i < all.Count; i++)
            {
                Hd2TeamButton point = all[i];
                if (point == null)
                    continue;
                if (point.Label == "A")
                    buttonA = point;
                else if (point.Label == "B")
                    buttonB = point;
                else if (point.Label == "C")
                    buttonC = point;
            }

            if (buttonA == null && buttonB == null && buttonC == null)
                return;

            Transform parent = view != null ? view : transform;
            if (!Hd2ControlPoint.Present)
            {
                if (buttonA != null)
                    pointA = MakePoint(parent, new Vector3(-0.40f, -0.145f, 0.9f), "A");
                if (buttonB != null)
                    pointB = MakePoint(parent, new Vector3(-0.33f, -0.145f, 0.9f), "B");
                if (buttonC != null)
                    pointC = MakePoint(parent, new Vector3(-0.26f, -0.145f, 0.9f), "C");
            }
            EnsureClocks();
            redScore = MakeReadout(parent, new Vector3(-0.35f, -0.46f, 0.9f), scoreRed, TextAnchor.MiddleLeft);
            redScore.text = redLines[0];
            blueScore = MakeReadout(parent, new Vector3(-0.18f, -0.46f, 0.9f), scoreBlue, TextAnchor.MiddleLeft);
            blueScore.text = blueLines[0];
            clockReadout = MakePoint(parent, new Vector3(-0.33f, -0.09f, 0.9f), "MatchClock");
            clockReadout.color = Color.white;
            clockReadout.characterSize = 0.0032f;
            clockReadout.text = clocks[900];
            dominationReady = true;
            ApplyPlacement();
        }

        PaintPoint(buttonA, pointA, ref shownA);
        PaintPoint(buttonB, pointB, ref shownB);
        PaintPoint(buttonC, pointC, ref shownC);
    }

    void RefreshMatch()
    {
        if (!dominationReady || clockReadout == null)
            return;

        bool control = Hd2ControlPoint.Present;
        int red = control ? Hd2ControlPoint.RedPoints : Hd2Domination.RedPoints;
        int blue = control ? Hd2ControlPoint.BluePoints : Hd2Domination.BluePoints;
        int cap = control ? Hd2ControlPoint.PointsToWin : 3;
        if (red < 0)
            red = 0;
        if (red > cap)
            red = cap;
        if (blue < 0)
            blue = 0;
        if (blue > cap)
            blue = cap;
        float left = control ? Hd2ControlPoint.TimeLeft : Hd2Domination.TimeLeft;
        int second = left <= 0f ? 0 : Mathf.CeilToInt(left);
        if (second > 900)
            second = 900;
        if (red != shownRed)
        {
            shownRed = red;
            redScore.text = control ? ControlLine(true, red) : redLines[red];
        }

        if (blue != shownBlue)
        {
            shownBlue = blue;
            blueScore.text = control ? ControlLine(false, blue) : blueLines[blue];
        }

        if (second != shownClock)
        {
            shownClock = second;
            clockReadout.text = clocks[second];
        }
    }

    static string[] controlRed;
    static string[] controlBlue;

    static string ControlLine(bool red, int score)
    {
        if (controlRed == null)
        {
            int count = Hd2ControlPoint.PointsToWin + 1;
            controlRed = new string[count];
            controlBlue = new string[count];
            for (int i = 0; i < count; i++)
            {
                string number = i.ToString();
                controlRed[i] = "Red " + number;
                controlBlue[i] = "Blue " + number;
            }
        }

        if (score < 0)
            score = 0;
        if (score >= controlRed.Length)
            score = controlRed.Length - 1;
        return red ? controlRed[score] : controlBlue[score];
    }

    static void EnsureClocks()
    {
        if (clocks != null)
            return;

        clocks = new string[901];
        for (int i = 0; i < clocks.Length; i++)
        {
            int minute = i / 60;
            int second = i % 60;
            clocks[i] = minute.ToString() + ":" + (second < 10 ? "0" : "") + second.ToString();
        }
    }

    static void PaintPoint(Hd2TeamButton point, TextMesh text, ref int shown)
    {
        if (point == null || text == null)
            return;

        int id = (int)point.Owner;
        if (id == shown)
            return;

        shown = id;
        text.color = point.MarkerColor;
    }

    static TextMesh MakePoint(Transform parent, Vector3 local, string letter)
    {
        TextMesh text = MakeReadout(parent, local, Color.white, TextAnchor.MiddleCenter);
        text.gameObject.name = letter;
        text.text = letter;
        text.fontSize = 120;
        text.characterSize = 0.0045f;
        text.alignment = TextAlignment.Center;
        text.anchor = TextAnchor.MiddleCenter;
        return text;
    }

    static string[] BuildPercents()
    {
        var values = new string[101];
        for (int i = 0; i < values.Length; i++)
            values[i] = i + "%";
        return values;
    }

    void RefreshBanner()
    {
        if (side == Side.None)
        {
            if (banner != null && banner.gameObject.activeSelf)
                banner.gameObject.SetActive(false);
            return;
        }

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
        blitzReadout.text = "3/3";
        micReadout.text = "Off";
        banner = MakeReadout(view, new Vector3(0.36f, -0.2f, 0.9f), Color.white, TextAnchor.MiddleRight);
        banner.gameObject.SetActive(false);
        scoreReadout = MakeReadout(view, new Vector3(-0.35f, -0.4f, 0.9f), Color.white, TextAnchor.MiddleLeft);
        scoreReadout.text = "K/D 0/0  KS 0";
        BuildHitFrame();
        ApplyPlacement();
    }

    void ApplyPlacement()
    {
        bool show = side != Side.None;
        if (canvas != null)
            canvas.gameObject.SetActive(show);
        SetShown(healthReadout, show);
        SetShown(blitzReadout, show);
        SetShown(micReadout, show);
        SetShown(pointA, show);
        SetShown(pointB, show);
        SetShown(pointC, show);
        SetShown(redScore, show);
        SetShown(blueScore, show);
        SetShown(clockReadout, show);
        if (!show)
        {
            if (banner != null && banner.gameObject.activeSelf)
                banner.gameObject.SetActive(false);
            return;
        }

        bool right = side == Side.Right;
        if (canvas != null)
            canvas.transform.localPosition = right
                ? new Vector3(0.368f, -0.18f, 0.9f)
                : new Vector3(-0.42f, -0.18f, 0.9f);

        TextAnchor valueAnchor = right ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
        float valueX = right ? 0.35f : -0.35f;
        PlaceReadout(healthReadout, new Vector3(valueX, -0.206f, 0.9f), valueAnchor);
        PlaceReadout(blitzReadout, new Vector3(valueX, -0.27f, 0.9f), valueAnchor);
        PlaceReadout(micReadout, new Vector3(valueX, -0.334f, 0.9f), valueAnchor);
        PlaceReadout(scoreReadout, new Vector3(valueX, -0.4f, 0.9f), valueAnchor);
        PlacePoint(pointA, 0);
        PlacePoint(pointB, 1);
        PlacePoint(pointC, 2);
        PlaceMatch();
        PlaceBanner();
    }

    void PlaceMatch()
    {
        float center = side == Side.Right ? 0.33f : -0.33f;
        bool right = side == Side.Right;
        float scoreX = right ? 0.35f : -0.35f;
        TextAnchor scoreAnchor = right ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
        float shift = right ? -0.16f : 0.16f;
        PlaceReadout(redScore, new Vector3(right ? scoreX + shift : scoreX, -0.46f, 0.9f), scoreAnchor);
        PlaceReadout(blueScore, new Vector3(right ? scoreX : scoreX + shift, -0.46f, 0.9f), scoreAnchor);
        PlaceReadout(clockReadout, new Vector3(center, -0.09f, 0.9f), TextAnchor.MiddleCenter);
        if (redScore != null)
            redScore.color = scoreRed;
        if (blueScore != null)
            blueScore.color = scoreBlue;
    }

    void PlacePoint(TextMesh text, int index)
    {
        if (text == null)
            return;
        float x = side == Side.Right ? 0.26f + 0.07f * index : -0.40f + 0.07f * index;
        text.transform.localPosition = new Vector3(x, -0.145f, 0.9f);
    }

    void PlaceBanner()
    {
        if (banner == null || side == Side.None)
            return;
        bool right = side == Side.Right;
        PlaceReadout(banner,
            new Vector3(right ? -0.36f : 0.36f, -0.2f, 0.9f),
            right ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight);
    }

    static void SetShown(TextMesh text, bool show)
    {
        if (text != null && text.gameObject.activeSelf != show)
            text.gameObject.SetActive(show);
    }

    static void PlaceReadout(TextMesh text, Vector3 local, TextAnchor anchor)
    {
        if (text == null)
            return;
        text.transform.localPosition = local;
        text.anchor = anchor;
        if (anchor == TextAnchor.MiddleLeft)
            text.alignment = TextAlignment.Left;
        else if (anchor == TextAnchor.MiddleRight)
            text.alignment = TextAlignment.Right;
        else
            text.alignment = TextAlignment.Center;
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

    void OnDamaged(Hd2Health current)
    {
        float now = current.Health;
        bool shot = trackedHealth >= 0f && now < trackedHealth && Time.time - current.KillLineTime <= 0.05f;
        trackedHealth = now;
        if (!shot || view == null)
            return;

        Vector3 local = view.InverseTransformPoint(current.LastShotOrigin);
        Vector2 flat = new Vector2(local.x, local.z);
        if (flat.sqrMagnitude < 0.0004f)
        {
            fromLeft = 0f;
            fromRight = 0f;
            fromFront = 0f;
            fromBack = 0f;
        }
        else
        {
            flat.Normalize();
            fromLeft = Mathf.Clamp01(-flat.x);
            fromRight = Mathf.Clamp01(flat.x);
            fromFront = Mathf.Clamp01(flat.y);
            fromBack = Mathf.Clamp01(-flat.y);
        }

        hitStrength = 1f;
    }

    void BuildHitFrame()
    {
        hitFade = new Texture2D(64, 4, TextureFormat.RGBA32, false);
        hitFade.name = "HitEdge";
        hitFade.wrapMode = TextureWrapMode.Clamp;
        hitFade.filterMode = FilterMode.Bilinear;
        var pixels = new Color[64 * 4];
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                float falloff = 1f - x / 63f;
                pixels[y * 64 + x] = new Color(1f, 1f, 1f, falloff * falloff);
            }
        }

        hitFade.SetPixels(pixels);
        hitFade.Apply(false, true);

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Unlit/Transparent");
        hitMaterial = new Material(shader);
        hitMaterial.SetTexture("_BaseMap", hitFade);
        hitMaterial.SetTexture("_MainTex", hitFade);
        hitMaterial.SetFloat("_Surface", 1f);
        hitMaterial.SetOverrideTag("RenderType", "Transparent");
        hitMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        hitMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        hitMaterial.SetInt("_ZWrite", 0);
        hitMaterial.SetInt("_Cull", (int)CullMode.Off);
        hitMaterial.renderQueue = 4000;

        hitLeft = MakeHitEdge("HitLeft", 0);
        hitRight = MakeHitEdge("HitRight", 1);
        hitTop = MakeHitEdge("HitTop", 2);
        hitBottom = MakeHitEdge("HitBottom", 3);
        PlaceHitFrame();
    }

    Transform MakeHitEdge(string edgeName, int index)
    {
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = edgeName;
        quad.transform.SetParent(view, false);
        var collider = quad.GetComponent<Collider>();
        if (collider != null)
            Destroy(collider);
        var renderer = quad.GetComponent<Renderer>();
        renderer.sharedMaterial = hitMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.enabled = false;
        hitRenderers[index] = renderer;
        return quad.transform;
    }

    void PlaceHitFrame()
    {
        const float dist = 0.18f;
        float halfH = 0.16f;
        float halfW = 0.2f;
        if (ownerCamera != null && ownerCamera.fieldOfView > 20f)
        {
            halfH = Mathf.Tan(ownerCamera.fieldOfView * 0.5f * Mathf.Deg2Rad) * dist;
            float aspect = ownerCamera.aspect > 0.5f ? ownerCamera.aspect : 1f;
            halfW = halfH * aspect;
        }

        halfH *= 1.08f;
        halfW *= 1.08f;
        float thickX = halfW * 0.46f;
        float thickY = halfH * 0.46f;
        PlaceHitEdge(hitLeft, new Vector3(-(halfW - thickX * 0.5f), 0f, dist), new Vector3(thickX, halfH * 2f, 1f), Quaternion.identity);
        PlaceHitEdge(hitRight, new Vector3(halfW - thickX * 0.5f, 0f, dist), new Vector3(-thickX, halfH * 2f, 1f), Quaternion.identity);
        PlaceHitEdge(hitTop, new Vector3(0f, halfH - thickY * 0.5f, dist), new Vector3(thickY, halfW * 2f, 1f), Quaternion.Euler(0f, 0f, 90f));
        PlaceHitEdge(hitBottom, new Vector3(0f, -(halfH - thickY * 0.5f), dist), new Vector3(thickY, halfW * 2f, 1f), Quaternion.Euler(0f, 0f, -90f));
    }

    static void PlaceHitEdge(Transform edge, Vector3 local, Vector3 scale, Quaternion rotation)
    {
        if (edge == null)
            return;
        edge.localPosition = local;
        edge.localRotation = rotation;
        edge.localScale = scale;
    }

    void RefreshHitFlash()
    {
        if (hitLeft == null)
            return;
        if (hitStrength <= 0.001f)
        {
            hitStrength = 0f;
            SetHitVisible(false);
            return;
        }

        PlaceHitFrame();
        hitStrength = Mathf.MoveTowards(hitStrength, 0f, Time.deltaTime / HitFadeSeconds);
        SetHitVisible(true);
        PaintHitEdge(0, fromLeft);
        PaintHitEdge(1, fromRight);
        PaintHitEdge(2, fromFront);
        PaintHitEdge(3, fromBack);
    }

    void PaintHitEdge(int index, float side)
    {
        Renderer renderer = hitRenderers[index];
        if (renderer == null)
            return;
        if (hitBlocks[index] == null)
            hitBlocks[index] = new MaterialPropertyBlock();
        float alpha = hitStrength * (HitBaseAlpha + HitSideAlpha * side);
        var color = new Color(0.92f, 0.04f, 0.03f, alpha);
        hitBlocks[index].SetColor("_BaseColor", color);
        hitBlocks[index].SetColor("_Color", color);
        renderer.SetPropertyBlock(hitBlocks[index]);
    }

    void SetHitVisible(bool show)
    {
        for (int i = 0; i < hitRenderers.Length; i++)
        {
            Renderer renderer = hitRenderers[i];
            if (renderer != null && renderer.enabled != show)
                renderer.enabled = show;
        }
    }

    void CullToOwner(ScriptableRenderContext context, Camera camera)
    {
        bool mine = ownerCamera == null || camera == ownerCamera;
        if (canvas != null)
            canvas.enabled = mine;
        SetHitVisible(mine && hitStrength > 0.001f);
    }

    void RestoreAfterCamera(ScriptableRenderContext context, Camera camera)
    {
        if (canvas != null)
            canvas.enabled = true;
        SetHitVisible(hitStrength > 0.001f);
    }
}
