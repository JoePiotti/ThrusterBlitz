using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR;

/// <summary>
/// The start menu. The player stands in a small spaceship cabin (Hd2ShipRoom) in front of a
/// console with a menu screen; they can look around but not move. Point with either
/// controller (the beam comes out the front, as on the old title) and pull the trigger.
/// Tabs down the left: Multiplayer, Profile, Options, Controls, Customize, Quit, with the
/// build version under them.
/// </summary>
public class Hd2MainMenu : MonoBehaviour
{
    const float PixelsWide = 1600f;
    const float PixelsHigh = 950f;
    const float TabWidth = 280f;
    const float PageLeft = TabWidth + 40f;
    const float PageWidth = PixelsWide - PageLeft - 32f;
    const string MapScene = "Shootout";

    static readonly Color PanelColor = new Color(0.05f, 0.06f, 0.09f, 0.97f);
    static readonly Color OffColor = new Color(0.14f, 0.17f, 0.23f, 1f);
    static readonly Color OnColor = new Color(0.25f, 0.52f, 0.95f, 1f);
    static readonly Color TabColor = new Color(0.1f, 0.12f, 0.17f, 1f);
    static readonly Color HintColor = new Color(0.65f, 0.78f, 0.9f, 1f);
    static readonly Color HeaderColor = new Color(0.55f, 0.7f, 0.95f, 1f);
    static readonly Color KeyColor = new Color(0.16f, 0.2f, 0.27f, 1f);
    static readonly Color KeyOffColor = new Color(0.1f, 0.11f, 0.14f, 1f);
    static readonly Color JoinColor = new Color(0.15f, 0.55f, 0.32f, 1f);
    static readonly Color DangerColor = new Color(0.62f, 0.15f, 0.13f, 1f);

    sealed class Click
    {
        public RectTransform rect;
        public System.Action action;
        public System.Action<float> slide;
    }

    sealed class Slider
    {
        public RectTransform track;
        public Image fill;
        public RectTransform handle;
        public TMP_Text value;
        public System.Func<float> get;      // 0..1
        public System.Func<string> label;
        public bool centered;
    }

    struct Server
    {
        public string name;
        public string map;
        public Hd2GameMode.Mode mode;
        public int players;
        public int capacity;
        public bool online;
    }

    sealed class KeyCap
    {
        public char lower;
        public TMP_Text label;
        public Image image;
    }

    // ---- scene objects
    Camera view;
    Transform cabin;
    Transform leftHand;
    Transform rightHand;
    Canvas canvas;
    RectTransform panel;
    LineRenderer laser;
    Image hover;
    InputAction leftTrigger;
    InputAction rightTrigger;
    InputAction mouseClick;
    AudioSource clickSound;
    Hd2ControlMap controls;
    float placedAt = -1f;
    Vector3 placedHead;
    bool loading;
    bool joining;

    // ---- menu state
    readonly List<Click> clicks = new List<Click>();
    readonly List<Click> keyboardClicks = new List<Click>();
    readonly List<Click> actionClicks = new List<Click>();
    readonly List<Click> createClicks = new List<Click>();
    readonly List<Slider> sliders = new List<Slider>();
    readonly List<GameObject> pages = new List<GameObject>();
    readonly List<Image> tabImages = new List<Image>();
    int currentTab;
    TMP_Text status;
    GameObject createPanel;
    Hd2GameMode.Mode createMode = Hd2GameMode.Mode.TeamDeathmatch;
    readonly Image[] createModeImages = new Image[3];

    // profile + keyboard
    TMP_Text nameValue;
    TMP_Text clanValue;
    TMP_Text shownAs;
    GameObject keyboard;
    TMP_Text keyboardTitle;
    TMP_Text keyboardField;
    TMP_Text keyboardMessage;
    Image shiftKey;
    readonly List<KeyCap> keyCaps = new List<KeyCap>();
    static readonly string[] keyRows = { "1234567890", "qwertyuiop", "asdfghjkl'", "zxcvbnm.-_" };
    string typed = "";
    bool editingClan;
    bool shift;
    bool editorTyping;

    // options
    Image smoothBox;
    Image smoothCheck;
    readonly Image[] micImages = new Image[3];
    readonly Image[] hudImages = new Image[3];

    // controls
    static readonly Hd2ControlMap.Button[] leftButtons =
    {
        Hd2ControlMap.Button.LeftStickPress, Hd2ControlMap.Button.LeftTrigger, Hd2ControlMap.Button.LeftGrip,
        Hd2ControlMap.Button.LeftX, Hd2ControlMap.Button.LeftY
    };

    static readonly Hd2ControlMap.Button[] rightButtons =
    {
        Hd2ControlMap.Button.RightStickPress, Hd2ControlMap.Button.RightTrigger, Hd2ControlMap.Button.RightGrip,
        Hd2ControlMap.Button.RightA, Hd2ControlMap.Button.RightB
    };

    readonly TMP_Text[] actionLabels = new TMP_Text[Hd2ControlMap.ButtonCount];
    GameObject actionList;
    TMP_Text actionListTitle;
    Hd2ControlMap.Button picking;
    bool pickingValid;
    Image pickupBox;
    Image pickupCheck;
    readonly Image[] presetImages = new Image[Hd2ControlMap.PresetCount];

    static Sprite white;
    static readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();
    static readonly List<XRInputSubsystem> inputs = new List<XRInputSubsystem>();

    // ================================================================ lifecycle
    void Awake()
    {
        Hd2GameMode.Selected = Hd2GameMode.Mode.TeamDeathmatch;
        FloorLevelTracking();
        controls = GetComponent<Hd2ControlMap>();
        if (controls == null)
            controls = gameObject.AddComponent<Hd2ControlMap>();
        controls.Changed += RefreshAll;
        Hd2Identity.Changed += RefreshProfile;
        Hd2Settings.Changed += RefreshAll;

        view = EnsureCamera();
        Space();
        // The cabin is saved in Title.unity (child "Cabin") so it can be edited in the scene.
        // If it is missing, build the default room in code.
        cabin = transform.Find("Cabin");
        if (cabin == null)
        {
            cabin = new GameObject("Cabin").transform;
            cabin.SetParent(transform, false);
            Hd2ShipRoom.Build(cabin);
        }
        leftHand = CreateHand("LeftHand", "<XRController>{LeftHand}/devicePosition", "<XRController>{LeftHand}/deviceRotation", new Vector3(-0.2f, 1.1f, 0.3f));
        rightHand = CreateHand("RightHand", "<XRController>{RightHand}/devicePosition", "<XRController>{RightHand}/deviceRotation", new Vector3(0.2f, 1.1f, 0.3f));
        BuildCanvas();
        PlayMusic();
        clickSound = gameObject.AddComponent<AudioSource>();
        clickSound.playOnAwake = false;
        clickSound.spatialBlend = 0f;
        clickSound.clip = Resources.Load<AudioClip>("Audio/ButtonClick");
        Hd2Audio.Route(clickSound, Hd2Audio.Channel.Game);

        leftTrigger = new InputAction("Hd2MenuLeft", InputActionType.Button, "<XRController>{LeftHand}/triggerPressed");
        rightTrigger = new InputAction("Hd2MenuRight", InputActionType.Button, "<XRController>{RightHand}/triggerPressed");
        mouseClick = new InputAction("Hd2MenuMouse", InputActionType.Button, "<Mouse>/leftButton");
        leftTrigger.Enable();
        rightTrigger.Enable();
        mouseClick.Enable();

        string note = Hd2Session.TakeTitleNote();
        if (!string.IsNullOrEmpty(note))
            SetStatus(note);
    }

    void OnDestroy()
    {
        if (controls != null)
            controls.Changed -= RefreshAll;
        Hd2Identity.Changed -= RefreshProfile;
        Hd2Settings.Changed -= RefreshAll;
        StopEditorTyping();
        Dispose(ref leftTrigger);
        Dispose(ref rightTrigger);
        Dispose(ref mouseClick);
    }

    static void Dispose(ref InputAction action)
    {
        if (action == null)
            return;
        action.Disable();
        action.Dispose();
        action = null;
    }

    void Update()
    {
        KeepCabinAroundPlayer();
        if (loading || Hd2CompanyLogo.Showing)
        {
            HideLaser();
            SetHover(null);
            return;
        }

        if (KeyboardOpen)
            EditorKeys();

        Click aimed = null;
        bool hit = TryHand(rightHand, rightTrigger, ref aimed);
        if (!hit)
            hit = TryHand(leftHand, leftTrigger, ref aimed);
        if (!hit)
        {
            HideLaser();
            TryMouse(ref aimed);
        }

        SetHover(aimed);
    }

    // ================================================================ room and camera
    static void FloorLevelTracking()
    {
        inputs.Clear();
        SubsystemManager.GetSubsystems(inputs);
        for (int i = 0; i < inputs.Count; i++)
            inputs[i].TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);
    }

    // The cabin is placed around wherever the player stands, facing the way they look, and
    // moves with them if the headset recenters (they can't walk in here, so it should
    // always be around them).
    void KeepCabinAroundPlayer()
    {
        if (view == null || cabin == null)
            return;
        Vector3 head = view.transform.position;
        Vector3 flat = new Vector3(head.x, 0f, head.z);
        bool first = placedAt < 0f;
        if (!first && (flat - placedHead).sqrMagnitude < 1f)
            return;
        if (first && Time.unscaledTime < 0.4f && !EditorFallback)
            return;

        Vector3 look = Vector3.ProjectOnPlane(view.transform.forward, Vector3.up);
        float yaw = look.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(look).eulerAngles.y : 0f;
        Quaternion turn = Quaternion.Euler(0f, yaw, 0f);
        cabin.SetPositionAndRotation(flat, turn);

        placedHead = flat;
        placedAt = Time.unscaledTime;
    }

    static void Space()
    {
        Material sky = Resources.Load<Material>("MainMenu/SpaceSky");
        if (sky != null)
            RenderSettings.skybox = sky;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.3f, 0.33f, 0.4f, 1f);
        RenderSettings.fog = false;
        var lights = FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < lights.Length; i++)
        {
            if (lights[i] != null && lights[i].type == LightType.Directional)
            {
                lights[i].intensity = 0.35f;
                lights[i].color = new Color(0.85f, 0.9f, 1f);
            }
        }
    }

    static Camera EnsureCamera()
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cam = cameraObject.AddComponent<Camera>();
        }

        cam.clearFlags = CameraClearFlags.Skybox;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 500f;
        if (cam.GetComponent<AudioListener>() == null)
            cam.gameObject.AddComponent<AudioListener>();
        if (cam.GetComponent<TrackedPoseDriver>() == null)
        {
            var driver = cam.gameObject.AddComponent<TrackedPoseDriver>();
            var position = new InputAction("MenuEyePos", InputActionType.Value, "<XRHMD>/centerEyePosition", expectedControlType: "Vector3");
            var rotation = new InputAction("MenuEyeRot", InputActionType.Value, "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion");
            driver.positionInput = new InputActionProperty(position);
            driver.rotationInput = new InputActionProperty(rotation);
            driver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            driver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
        }

        if (EditorFallback)
            cam.transform.SetPositionAndRotation(new Vector3(0f, 1.6f, 0f), Quaternion.identity);
        return cam;
    }

    Transform CreateHand(string name, string positionBinding, string rotationBinding, Vector3 place)
    {
        var hand = new GameObject(name);
        hand.transform.SetParent(transform, false);
        hand.transform.localPosition = place;
        var driver = hand.AddComponent<TrackedPoseDriver>();
        var position = new InputAction(name + "-pos", InputActionType.Value, positionBinding, expectedControlType: "Vector3");
        var rotation = new InputAction(name + "-rot", InputActionType.Value, rotationBinding, expectedControlType: "Quaternion");
        driver.positionInput = new InputActionProperty(position);
        driver.rotationInput = new InputActionProperty(rotation);
        driver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
        driver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;

        var tip = GameObject.CreatePrimitive(PrimitiveType.Cube);
        tip.name = name + "Tip";
        tip.transform.SetParent(hand.transform, false);
        tip.transform.localRotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
        tip.transform.localPosition = Vector3.down * 0.06f;
        tip.transform.localScale = new Vector3(0.02f, 0.02f, 0.1f);
        var tipCollider = tip.GetComponent<Collider>();
        if (tipCollider != null)
            Destroy(tipCollider);
        var tipRenderer = tip.GetComponent<MeshRenderer>();
        tipRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        tipRenderer.sharedMaterial = BeamMaterial();
        return hand.transform;
    }

    static Material beamMaterial;

    static Material BeamMaterial()
    {
        if (beamMaterial != null)
            return beamMaterial;
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        beamMaterial = new Material(shader) { color = new Color(0.45f, 0.85f, 1f, 1f) };
        return beamMaterial;
    }

    void PlayMusic()
    {
        AudioClip first = Resources.Load<AudioClip>("Audio/TitleV1");
        AudioClip second = Resources.Load<AudioClip>("Audio/TitleV2");
        AudioClip clip = first != null && second != null ? (Random.value < 0.5f ? first : second) : first != null ? first : second;
        if (clip == null)
            return;
        var source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = true;
        source.spatialBlend = 0f;
        source.volume = 0.85f;
        source.clip = clip;
        Hd2Audio.Route(source, Hd2Audio.Channel.Music);
        source.Play();
    }

    // ================================================================ pointing
    bool TryHand(Transform hand, InputAction trigger, ref Click aimed)
    {
        if (hand == null || canvas == null)
            return false;
        Vector3 direction = hand.TransformDirection(Vector3.down);
        if (!Aim(new Ray(hand.position, direction), 4f, out Vector3 point))
            return false;
        ShowLaser(hand.position, point);
        Use(point, trigger != null && trigger.WasPressedThisFrame(), trigger != null && trigger.IsPressed(), ref aimed);
        return true;
    }

    void TryMouse(ref Click aimed)
    {
        if (!EditorFallback || view == null || Mouse.current == null)
            return;
        Ray ray = view.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (!Aim(ray, 20f, out Vector3 point))
            return;
        Use(point, mouseClick.WasPressedThisFrame(), mouseClick.IsPressed(), ref aimed);
    }

    bool Aim(Ray ray, float reach, out Vector3 point)
    {
        point = default;
        Plane plane = new Plane(-canvas.transform.forward, canvas.transform.position);
        if (!plane.Raycast(ray, out float distance) || distance > reach)
            return false;
        point = ray.GetPoint(distance);
        Vector3 local = panel.InverseTransformPoint(point);
        return panel.rect.Contains(new Vector2(local.x, local.y));
    }

    void Use(Vector3 point, bool pressed, bool held, ref Click aimed)
    {
        Click click = Pick(point);
        aimed = click;
        if (click == null)
            return;
        if (click.slide != null && held)
        {
            Vector3 local = click.rect.InverseTransformPoint(point);
            Rect area = click.rect.rect;
            click.slide(Mathf.InverseLerp(area.xMin, area.xMax, local.x));
            return;
        }

        if (!pressed || click.action == null)
            return;
        if (clickSound != null && clickSound.clip != null)
            clickSound.PlayOneShot(clickSound.clip, 0.6f);
        click.action();
    }

    Click Pick(Vector3 point)
    {
        if (KeyboardOpen)
            return First(keyboardClicks, point, true);
        if (createPanel != null && createPanel.activeSelf)
            return First(createClicks, point, true);
        if (actionList != null && actionList.activeSelf)
        {
            Click inList = First(actionClicks, point, false);
            if (inList != null)
                return inList;
        }

        return First(clicks, point, false);
    }

    static Click First(List<Click> list, Vector3 point, bool modal)
    {
        // A modal panel ends its list with a click area that has no action and covers the whole
        // panel, so clicks behind it are swallowed. It is the last entry, so it would win every
        // pick and block the panel's own keys. Use it only when nothing usable is under the point.
        Click blocker = null;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            RectTransform rect = list[i].rect;
            if (rect == null || !rect.gameObject.activeInHierarchy)
                continue;
            Vector3 local = rect.InverseTransformPoint(point);
            if (!rect.rect.Contains(new Vector2(local.x, local.y)))
                continue;
            if (list[i].action == null && list[i].slide == null)
            {
                if (blocker == null)
                    blocker = list[i];
                continue;
            }

            return list[i];
        }

        return blocker;
    }

    void SetHover(Click click)
    {
        if (hover == null)
            return;
        bool show = click != null && click.rect != null && click.action != null && click.rect != panel;
        hover.gameObject.SetActive(show);
        if (!show)
            return;
        hover.transform.SetParent(click.rect, false);
        Stretch(hover.rectTransform, 0f);
        hover.rectTransform.offsetMin = Vector2.zero;
        hover.rectTransform.offsetMax = Vector2.zero;
        hover.transform.SetAsLastSibling();
    }

    void ShowLaser(Vector3 from, Vector3 to)
    {
        if (laser == null)
        {
            var go = new GameObject("MenuLaser");
            go.transform.SetParent(transform, false);
            laser = go.AddComponent<LineRenderer>();
            laser.positionCount = 2;
            laser.widthMultiplier = 0.004f;
            laser.useWorldSpace = true;
            laser.numCapVertices = 4;
            laser.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            laser.receiveShadows = false;
            laser.material = BeamMaterial();
        }

        laser.enabled = true;
        laser.SetPosition(0, from);
        laser.SetPosition(1, to);
    }

    void HideLaser()
    {
        if (laser != null)
            laser.enabled = false;
    }

    // ================================================================ canvas and tabs
    void BuildCanvas()
    {
        // The screen is saved in the cabin as "MenuScreen" (a world-space canvas): move, turn or
        // scale it in the scene and the menu fills it. Its contents are built here at runtime.
        Transform saved = cabin.Find("MenuScreen");
        GameObject root = saved != null ? saved.gameObject : null;
        if (root == null)
        {
            root = new GameObject("MenuScreen");
            root.transform.SetParent(cabin, false);
            root.transform.localPosition = Hd2ShipRoom.ScreenCenter;
            root.transform.localRotation = Quaternion.Euler(-Hd2ShipRoom.ScreenTilt, 0f, 0f);
            root.transform.localScale = Vector3.one * (Hd2ShipRoom.ScreenWidth / PixelsWide);
        }

        canvas = root.GetComponent<Canvas>();
        if (canvas == null)
            canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = view;
        var scaler = root.GetComponent<CanvasScaler>();
        if (scaler == null)
            scaler = root.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 10f;
        panel = root.GetComponent<RectTransform>();
        panel.sizeDelta = new Vector2(PixelsWide, PixelsHigh);
        var background = root.GetComponent<Image>();
        if (background == null)
            background = root.AddComponent<Image>();
        background.sprite = White();
        background.color = PanelColor;
        background.raycastTarget = false;

        var side = MakeImage(panel, TabColor);
        Place(side.rectTransform, 0f, 0f, TabWidth, PixelsHigh);
        var brand = MakeText(panel, "THRUSTER BLITZ", 30, TextAnchor.MiddleLeft, Color.white);
        brand.fontStyle = FontStyles.Bold | FontStyles.Italic;
        Place(brand.rectTransform, 24f, -22f, TabWidth - 30f, 50f);

        string[] tabs = { "Multiplayer", "Profile", "Options", "Controls", "Customize", "Quit" };
        for (int i = 0; i < tabs.Length; i++)
        {
            int index = i;
            var tab = MakeImage(panel, OffColor);
            Place(tab.rectTransform, 16f, -96f - i * 84f, TabWidth - 32f, 72f);
            var label = MakeText(tab.rectTransform, tabs[i], 28, TextAnchor.MiddleLeft, Color.white);
            Stretch(label.rectTransform, 22f);
            tabImages.Add(tab);
            clicks.Add(new Click { rect = tab.rectTransform, action = () => ShowTab(index) });

            var page = new GameObject(tabs[i] + "Page");
            var rect = page.AddComponent<RectTransform>();
            rect.SetParent(panel, false);
            Place(rect, PageLeft, -24f, PageWidth, PixelsHigh - 48f);
            pages.Add(page);
        }

        var version = MakeText(panel, "Version " + VersionLabel(), 20, TextAnchor.UpperLeft, HintColor);
        Place(version.rectTransform, 24f, -96f - tabs.Length * 84f - 4f, TabWidth - 30f, 30f);

        BuildMultiplayer(pages[0].GetComponent<RectTransform>());
        BuildProfile(pages[1].GetComponent<RectTransform>());
        BuildOptions(pages[2].GetComponent<RectTransform>());
        BuildControls(pages[3].GetComponent<RectTransform>());
        BuildCustomize(pages[4].GetComponent<RectTransform>());
        BuildQuit(pages[5].GetComponent<RectTransform>());
        BuildKeyboard();

        hover = MakeImage(panel, new Color(1f, 1f, 1f, 0.12f));
        hover.gameObject.SetActive(false);
        ShowTab(0);
        RefreshAll();
    }

    void ShowTab(int index)
    {
        currentTab = index;
        HideActionList();
        if (createPanel != null)
            createPanel.SetActive(false);
        for (int i = 0; i < pages.Count; i++)
        {
            pages[i].SetActive(i == index);
            tabImages[i].color = i == index ? OnColor : OffColor;
        }
    }

    static TMP_Text Heading(RectTransform page, string text, float y)
    {
        var heading = MakeText(page, text, 34, TextAnchor.MiddleLeft, Color.white);
        heading.fontStyle = FontStyles.Bold;
        Place(heading.rectTransform, 0f, y, PageWidth, 48f);
        return heading;
    }

    // ================================================================ Multiplayer
    void BuildMultiplayer(RectTransform page)
    {
        Heading(page, "Servers", 0f);
        float[] columns = { 0f, 400f, 620f, 900f, 1060f };
        string[] headers = { "Server", "Map", "Game mode", "Players", "" };
        for (int i = 0; i < headers.Length; i++)
        {
            var label = MakeText(page, headers[i], 22, TextAnchor.MiddleLeft, HeaderColor);
            Place(label.rectTransform, columns[i] + 16f, -64f, 220f, 32f);
        }

        var servers = new List<Server>
        {
            new Server { name = "Practice Arena 1", map = MapScene, mode = Hd2GameMode.Mode.TeamDeathmatch, players = 1, capacity = 10 },
            new Server { name = "Practice Arena 2", map = MapScene, mode = Hd2GameMode.Mode.Elimination, players = 1, capacity = 10 },
            new Server { name = "Practice Arena 3", map = MapScene, mode = Hd2GameMode.Mode.ControlPoint, players = 1, capacity = 10 },
            new Server { name = "hd2-test (online)", map = MapScene, mode = Hd2GameMode.Mode.TeamDeathmatch, players = -1, capacity = 10, online = true },
        };

        for (int r = 0; r < servers.Count; r++)
        {
            Server server = servers[r];
            float y = -104f - r * 76f;
            var row = MakeImage(page, r % 2 == 0 ? new Color(0.09f, 0.11f, 0.15f, 1f) : new Color(0.07f, 0.085f, 0.12f, 1f));
            Place(row.rectTransform, 0f, y, PageWidth, 68f);
            Cell(row.rectTransform, server.name, columns[0]);
            Cell(row.rectTransform, server.map, columns[1]);
            Cell(row.rectTransform, ModeLabel(server.mode), columns[2]);
            Cell(row.rectTransform, server.players < 0 ? "? / " + server.capacity : server.players + " / " + server.capacity, columns[3]);
            bool full = server.players >= server.capacity;
            if (full)
            {
                Cell(row.rectTransform, "Full", columns[4]);
                continue;
            }

            var join = MakeImage(row.rectTransform, JoinColor);
            Place(join.rectTransform, columns[4], -8f, 150f, 52f);
            Stretch(MakeText(join.rectTransform, "Join", 24, TextAnchor.MiddleCenter, Color.white).rectTransform, 4f);
            Server captured = server;
            clicks.Add(new Click { rect = join.rectTransform, action = () => JoinServer(captured) });
        }

        status = MakeText(page, "", 22, TextAnchor.MiddleLeft, new Color(1f, 0.86f, 0.4f, 1f));
        Place(status.rectTransform, 0f, -420f, PageWidth - 300f, 40f);

        var create = MakeImage(page, OnColor);
        Place(create.rectTransform, PageWidth - 280f, -810f, 280f, 70f);
        Stretch(MakeText(create.rectTransform, "Create Server", 26, TextAnchor.MiddleCenter, Color.white).rectTransform, 4f);
        clicks.Add(new Click { rect = create.rectTransform, action = OpenCreate });

        BuildCreatePanel(page);
    }

    static void Cell(RectTransform row, string text, float x)
    {
        var label = MakeText(row, text, 24, TextAnchor.MiddleLeft, Color.white);
        Place(label.rectTransform, x + 16f, 0f, 380f, 68f);
    }

    static string ModeLabel(Hd2GameMode.Mode mode)
    {
        switch (mode)
        {
            case Hd2GameMode.Mode.Elimination:
                return "Elimination";
            case Hd2GameMode.Mode.ControlPoint:
                return "Control Point";
            default:
                return "Team Deathmatch";
        }
    }

    void BuildCreatePanel(RectTransform page)
    {
        createPanel = new GameObject("CreateServer");
        var rect = createPanel.AddComponent<RectTransform>();
        rect.SetParent(page, false);
        Place(rect, 0f, 0f, PageWidth, PixelsHigh - 48f);
        var back = createPanel.AddComponent<Image>();
        back.sprite = White();
        back.color = new Color(0.05f, 0.06f, 0.09f, 0.99f);
        back.raycastTarget = false;

        Heading(rect, "Create Server", 0f);
        var mapLabel = MakeText(rect, "Map", 26, TextAnchor.MiddleLeft, Color.white);
        Place(mapLabel.rectTransform, 0f, -90f, 200f, 52f);
        var map = MakeImage(rect, OnColor);
        Place(map.rectTransform, 220f, -90f, 260f, 52f);
        Stretch(MakeText(map.rectTransform, MapScene, 24, TextAnchor.MiddleCenter, Color.white).rectTransform, 4f);

        var modeLabel = MakeText(rect, "Game mode", 26, TextAnchor.MiddleLeft, Color.white);
        Place(modeLabel.rectTransform, 0f, -170f, 200f, 52f);
        for (int i = 0; i < 3; i++)
        {
            var mode = (Hd2GameMode.Mode)i;
            var image = MakeImage(rect, OffColor);
            Place(image.rectTransform, 220f + i * 280f, -170f, 260f, 52f);
            Stretch(MakeText(image.rectTransform, ModeLabel(mode), 24, TextAnchor.MiddleCenter, Color.white).rectTransform, 4f);
            createModeImages[i] = image;
            createClicks.Add(new Click { rect = image.rectTransform, action = () => { createMode = mode; PaintCreate(); } });
        }

        var note = MakeText(rect, "Online hosting is coming soon. For now this starts the match on this headset, with bots filling both teams.", 22, TextAnchor.UpperLeft, HintColor);
        note.textWrappingMode = TextWrappingModes.Normal;
        Place(note.rectTransform, 0f, -260f, PageWidth - 40f, 80f);

        var cancel = MakeImage(rect, OffColor);
        Place(cancel.rectTransform, PageWidth - 580f, -810f, 280f, 70f);
        Stretch(MakeText(cancel.rectTransform, "Cancel", 26, TextAnchor.MiddleCenter, Color.white).rectTransform, 4f);
        createClicks.Add(new Click { rect = cancel.rectTransform, action = () => createPanel.SetActive(false) });

        var go = MakeImage(rect, JoinColor);
        Place(go.rectTransform, PageWidth - 280f, -810f, 280f, 70f);
        Stretch(MakeText(go.rectTransform, "Create", 26, TextAnchor.MiddleCenter, Color.white).rectTransform, 4f);
        createClicks.Add(new Click { rect = go.rectTransform, action = () => StartLocal(createMode) });

        createClicks.Add(new Click { rect = rect, action = null });
        PaintCreate();
        createPanel.SetActive(false);
    }

    void OpenCreate()
    {
        createPanel.SetActive(true);
        createPanel.transform.SetAsLastSibling();
        PaintCreate();
    }

    void PaintCreate()
    {
        for (int i = 0; i < createModeImages.Length; i++)
        {
            if (createModeImages[i] != null)
                createModeImages[i].color = i == (int)createMode ? OnColor : OffColor;
        }
    }

    void JoinServer(Server server)
    {
        if (loading || joining)
            return;
        if (server.online)
        {
            joining = true;
            SetStatus(Hd2Session.JoiningMessage);
            Hd2Session.Join();
            return;
        }

        StartLocal(server.mode);
    }

    void StartLocal(Hd2GameMode.Mode mode)
    {
        if (loading || joining)
            return;
        if (!Application.CanStreamedLevelBeLoaded(MapScene))
        {
            SetStatus("Cannot load " + MapScene);
            return;
        }

        Hd2GameMode.Selected = mode;
        loading = true;
        HideLaser();
        SceneManager.LoadScene(MapScene);
    }

    /// <summary>Hd2Session calls this when the online join fails.</summary>
    public void CancelJoin()
    {
        joining = false;
        loading = false;
        SetStatus(Hd2Session.JoinFailMessage);
    }

    void SetStatus(string message)
    {
        if (status != null && !string.IsNullOrEmpty(message))
            status.text = message;
    }

    // ================================================================ Profile
    void BuildProfile(RectTransform page)
    {
        Heading(page, "Profile", 0f);
        var nameCaption = MakeText(page, "Name", 24, TextAnchor.MiddleLeft, HintColor);
        Place(nameCaption.rectTransform, 0f, -210f, 400f, 34f);
        var name = MakeImage(page, KeyColor);
        Place(name.rectTransform, 0f, -248f, 560f, 70f);
        nameValue = MakeText(name.rectTransform, "", 28, TextAnchor.MiddleLeft, Color.white);
        Stretch(nameValue.rectTransform, 18f);
        clicks.Add(new Click { rect = name.rectTransform, action = () => OpenKeyboard(false) });

        var clanCaption = MakeText(page, "Clan tag", 24, TextAnchor.MiddleLeft, HintColor);
        Place(clanCaption.rectTransform, 0f, -80f, 400f, 34f);
        var clan = MakeImage(page, KeyColor);
        Place(clan.rectTransform, 0f, -118f, 560f, 70f);
        clanValue = MakeText(clan.rectTransform, "", 28, TextAnchor.MiddleLeft, Color.white);
        Stretch(clanValue.rectTransform, 18f);
        clicks.Add(new Click { rect = clan.rectTransform, action = () => OpenKeyboard(true) });

        var shownCaption = MakeText(page, "Other players see you as", 24, TextAnchor.MiddleLeft, HintColor);
        Place(shownCaption.rectTransform, 0f, -350f, 600f, 34f);
        shownAs = MakeText(page, "", 40, TextAnchor.MiddleLeft, Color.white);
        shownAs.fontStyle = FontStyles.Bold;
        Place(shownAs.rectTransform, 0f, -390f, PageWidth, 60f);

        var hint = MakeText(page, "Point at a field and pull the trigger to type.", 22, TextAnchor.MiddleLeft, HintColor);
        Place(hint.rectTransform, 0f, -480f, PageWidth, 34f);
        RefreshProfile();
    }

    void RefreshProfile()
    {
        if (nameValue != null)
            nameValue.text = Hd2Identity.Name;
        if (clanValue != null)
            clanValue.text = string.IsNullOrEmpty(Hd2Identity.Clan) ? "None" : Hd2Identity.Clan;
        if (shownAs != null)
            shownAs.text = Hd2Identity.Display;
    }

    bool KeyboardOpen => keyboard != null && keyboard.activeSelf;

    void BuildKeyboard()
    {
        keyboard = new GameObject("Keyboard");
        var rect = keyboard.AddComponent<RectTransform>();
        rect.SetParent(panel, false);
        Place(rect, PageLeft - 16f, 0f, PageWidth + 48f, PixelsHigh);
        var back = keyboard.AddComponent<Image>();
        back.sprite = White();
        back.color = new Color(0.05f, 0.06f, 0.08f, 0.995f);
        back.raycastTarget = false;

        const float left = 40f;
        const float key = 108f;
        const float gap = 12f;
        keyboardTitle = MakeText(rect, "Name", 32, TextAnchor.MiddleLeft, Color.white);
        Place(keyboardTitle.rectTransform, left, -24f, 1100f, 44f);
        var field = MakeImage(rect, new Color(0.13f, 0.15f, 0.2f, 1f));
        Place(field.rectTransform, left, -78f, 1188f, 74f);
        keyboardField = MakeText(field.rectTransform, "", 36, TextAnchor.MiddleLeft, Color.white);
        Stretch(keyboardField.rectTransform, 18f);
        keyboardMessage = MakeText(rect, "", 22, TextAnchor.MiddleLeft, HintColor);
        Place(keyboardMessage.rectTransform, left, -160f, 1188f, 36f);

        for (int row = 0; row < keyRows.Length; row++)
        {
            string chars = keyRows[row];
            for (int col = 0; col < chars.Length; col++)
            {
                char lower = chars[col];
                var image = MakeImage(rect, KeyColor);
                Place(image.rectTransform, left + col * (key + gap), -214f - row * 92f, key, 80f);
                var label = MakeText(image.rectTransform, lower.ToString(), 32, TextAnchor.MiddleCenter, Color.white);
                Stretch(label.rectTransform, 0f);
                var cap = new KeyCap { lower = lower, label = label, image = image };
                keyCaps.Add(cap);
                keyboardClicks.Add(new Click { rect = image.rectTransform, action = () => TypeKey(cap) });
            }
        }

        float bottom = -214f - keyRows.Length * 92f;
        shiftKey = KeyboardButton(rect, "Shift", left, bottom, 228f, ToggleShift);
        KeyboardButton(rect, "Space", left + 240f, bottom, 468f, () => TypeChar(' '));
        KeyboardButton(rect, "Back", left + 720f, bottom, 228f, Backspace);
        KeyboardButton(rect, "Clear", left + 960f, bottom, 228f, () => { typed = ""; RefreshKeyboard(); });
        float actions = bottom - 100f;
        KeyboardButton(rect, "Cancel", left + 480f, actions, 340f, CloseKeyboard).color = new Color(0.3f, 0.32f, 0.36f, 1f);
        KeyboardButton(rect, "Done", left + 848f, actions, 340f, FinishKeyboard).color = OnColor;
        keyboardClicks.Add(new Click { rect = rect, action = null });
        keyboard.SetActive(false);
    }

    Image KeyboardButton(RectTransform parent, string text, float x, float y, float w, System.Action action)
    {
        var image = MakeImage(parent, KeyColor);
        Place(image.rectTransform, x, y, w, 80f);
        Stretch(MakeText(image.rectTransform, text, 28, TextAnchor.MiddleCenter, Color.white).rectTransform, 4f);
        keyboardClicks.Add(new Click { rect = image.rectTransform, action = action });
        return image;
    }

    void OpenKeyboard(bool clan)
    {
        editingClan = clan;
        typed = clan ? Hd2Identity.Clan : Hd2Identity.Name;
        shift = clan || typed.Length == 0;
        keyboardTitle.text = clan ? "Clan tag" : "Name";
        keyboard.SetActive(true);
        keyboard.transform.SetAsLastSibling();
        if (hover != null)
            hover.transform.SetParent(panel, false);
        ShowRules();
        RefreshKeyboard();
        StartEditorTyping();
    }

    void ShowRules()
    {
        keyboardMessage.color = HintColor;
        keyboardMessage.text = editingClan
            ? "Up to " + Hd2Identity.ClanMax + " letters, numbers or periods, shown before your name. Leave empty for none."
            : Hd2Identity.NameMin + " to " + Hd2Identity.NameMax + " characters: letters, numbers, spaces and . - _ '";
    }

    void CloseKeyboard()
    {
        StopEditorTyping();
        if (keyboard != null)
            keyboard.SetActive(false);
    }

    void FinishKeyboard()
    {
        string problem = editingClan ? Hd2Identity.SetClan(typed) : Hd2Identity.SetName(typed);
        if (problem != null)
        {
            keyboardMessage.color = new Color(1f, 0.4f, 0.35f, 1f);
            keyboardMessage.text = problem;
            return;
        }

        CloseKeyboard();
        RefreshProfile();
    }

    void TypeKey(KeyCap cap)
    {
        TypeChar(shift ? char.ToUpperInvariant(cap.lower) : cap.lower);
    }

    void TypeChar(char c)
    {
        bool allowed = editingClan ? Hd2Identity.ClanChar(c) : Hd2Identity.NameChar(c);
        int max = editingClan ? Hd2Identity.ClanMax : Hd2Identity.NameMax;
        if (!allowed || typed.Length >= max)
            return;
        typed += c;
        if (!editingClan && char.IsLetter(c))
            shift = false;
        ShowRules();
        RefreshKeyboard();
    }

    void Backspace()
    {
        if (typed.Length > 0)
            typed = typed.Substring(0, typed.Length - 1);
        RefreshKeyboard();
    }

    void ToggleShift()
    {
        shift = !shift;
        RefreshKeyboard();
    }

    void RefreshKeyboard()
    {
        int max = editingClan ? Hd2Identity.ClanMax : Hd2Identity.NameMax;
        keyboardField.text = typed + "_   (" + typed.Length + "/" + max + ")";
        if (shiftKey != null)
            shiftKey.color = shift ? OnColor : KeyColor;
        for (int i = 0; i < keyCaps.Count; i++)
        {
            KeyCap cap = keyCaps[i];
            char shown = shift ? char.ToUpperInvariant(cap.lower) : cap.lower;
            cap.label.text = shown.ToString();
            bool usable = editingClan ? Hd2Identity.ClanChar(shown) : Hd2Identity.NameChar(shown);
            cap.image.color = usable ? KeyColor : KeyOffColor;
            cap.label.color = usable ? Color.white : new Color(1f, 1f, 1f, 0.25f);
        }
    }

    void StartEditorTyping()
    {
        if (editorTyping || !EditorFallback || Keyboard.current == null)
            return;
        Keyboard.current.onTextInput += EditorText;
        editorTyping = true;
    }

    void StopEditorTyping()
    {
        if (!editorTyping)
            return;
        if (Keyboard.current != null)
            Keyboard.current.onTextInput -= EditorText;
        editorTyping = false;
    }

    void EditorText(char c)
    {
        if (KeyboardOpen && c >= ' ')
            TypeChar(c);
    }

    void EditorKeys()
    {
        if (!editorTyping || Keyboard.current == null)
            return;
        if (Keyboard.current.backspaceKey.wasPressedThisFrame)
            Backspace();
        else if (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame)
            FinishKeyboard();
        else if (Keyboard.current.escapeKey.wasPressedThisFrame)
            CloseKeyboard();
    }

    // ================================================================ Options
    void BuildOptions(RectTransform page)
    {
        Heading(page, "Options", 0f);
        float y = -64f;
        Section(page, "Sound", y);
        y -= 44f;
        for (int i = 0; i < Hd2Settings.VolumeCount; i++)
        {
            var which = (Hd2Settings.Volume)i;
            AddSlider(page, Hd2Settings.Label(which), y,
                () => Hd2Settings.Get(which),
                t => Hd2Settings.Set(which, t),
                () => Mathf.RoundToInt(Hd2Settings.Get(which) * 100f) + "%");
            y -= 62f;
        }

        y -= 10f;
        Section(page, "Turning", y);
        y -= 44f;
        var label = MakeText(page, "Smooth turning", 26, TextAnchor.MiddleLeft, Color.white);
        Place(label.rectTransform, 0f, y, 280f, 52f);
        var row = MakeImage(page, new Color(0f, 0f, 0f, 0f));
        Place(row.rectTransform, 300f, y, 560f, 52f);
        smoothBox = MakeImage(row.rectTransform, OffColor);
        Place(smoothBox.rectTransform, 0f, 0f, 52f, 52f);
        smoothCheck = MakeImage(smoothBox.rectTransform, Color.white);
        Place(smoothCheck.rectTransform, 14f, -14f, 24f, 24f);
        var text = MakeText(row.rectTransform, "Right stick turns smoothly instead of in steps", 22, TextAnchor.MiddleLeft, HintColor);
        Place(text.rectTransform, 66f, 0f, 490f, 52f);
        clicks.Add(new Click { rect = row.rectTransform, action = () => Hd2Settings.SmoothTurn = !Hd2Settings.SmoothTurn });
        y -= 62f;
        AddSlider(page, "Snap turn angle", y,
            () => Mathf.InverseLerp(Hd2Settings.SnapAngleMin, Hd2Settings.SnapAngleMax, Hd2Settings.SnapAngle),
            t => Hd2Settings.SnapAngle = Mathf.Lerp(Hd2Settings.SnapAngleMin, Hd2Settings.SnapAngleMax, t),
            () => Hd2Settings.SnapAngle.ToString("0") + "°" + (Hd2Settings.SmoothTurn ? " (off)" : ""));
        y -= 72f;

        Section(page, "Weapons, microphone and HUD", y);
        y -= 44f;
        var angle = AddSlider(page, "Weapon angle", y,
            () => Mathf.InverseLerp(-Hd2ControlMap.GripAngleLimit, Hd2ControlMap.GripAngleLimit, controls.GripAngle),
            t => controls.GripAngle = Mathf.Lerp(-Hd2ControlMap.GripAngleLimit, Hd2ControlMap.GripAngleLimit, t),
            () => (controls.GripAngle > 0f ? "+" : "") + controls.GripAngle.ToString("0") + "°" + (controls.GripAngle > 0f ? " up" : controls.GripAngle < 0f ? " down" : ""));
        angle.centered = true;
        y -= 62f;

        var mic = MakeText(page, "Microphone", 26, TextAnchor.MiddleLeft, Color.white);
        Place(mic.rectTransform, 0f, y, 280f, 52f);
        for (int i = 0; i < 3; i++)
        {
            var mode = (Hd2ControlMap.MicMode)i;
            var image = MakeImage(page, OffColor);
            Place(image.rectTransform, 300f + i * 250f, y, 236f, 52f);
            Stretch(MakeText(image.rectTransform, Hd2ControlMap.MicLabel(mode), 22, TextAnchor.MiddleCenter, Color.white).rectTransform, 6f);
            micImages[i] = image;
            clicks.Add(new Click { rect = image.rectTransform, action = () => controls.MicrophoneMode = mode });
        }

        y -= 62f;
        var hud = MakeText(page, "HUD", 26, TextAnchor.MiddleLeft, Color.white);
        Place(hud.rectTransform, 0f, y, 280f, 52f);
        string[] sides = { "Left side", "Right side", "None" };
        for (int i = 0; i < 3; i++)
        {
            var side = (Hd2Hud.Side)i;
            var image = MakeImage(page, OffColor);
            Place(image.rectTransform, 300f + i * 250f, y, 236f, 52f);
            Stretch(MakeText(image.rectTransform, sides[i], 22, TextAnchor.MiddleCenter, Color.white).rectTransform, 6f);
            hudImages[i] = image;
            clicks.Add(new Click { rect = image.rectTransform, action = () => { Hd2Hud.SavedSide = side; PaintOptions(); } });
        }
    }

    static void Section(RectTransform page, string text, float y)
    {
        var label = MakeText(page, text.ToUpperInvariant(), 20, TextAnchor.MiddleLeft, HeaderColor);
        label.fontStyle = FontStyles.Bold;
        Place(label.rectTransform, 0f, y, PageWidth, 34f);
    }

    Slider AddSlider(RectTransform page, string caption, float y, System.Func<float> get, System.Action<float> set, System.Func<string> value)
    {
        var label = MakeText(page, caption, 26, TextAnchor.MiddleLeft, Color.white);
        Place(label.rectTransform, 0f, y, 280f, 52f);
        var track = MakeImage(page, OffColor);
        Place(track.rectTransform, 300f, y, 640f, 52f);
        var slider = new Slider
        {
            track = track.rectTransform,
            fill = MakeImage(track.rectTransform, OnColor),
            get = get,
            label = value
        };
        slider.handle = MakeImage(track.rectTransform, Color.white).rectTransform;
        slider.value = MakeText(page, "", 24, TextAnchor.MiddleLeft, Color.white);
        Place(slider.value.rectTransform, 960f, y, 240f, 52f);
        clicks.Add(new Click { rect = track.rectTransform, action = () => { }, slide = set });
        sliders.Add(slider);
        return slider;
    }

    void PaintSliders()
    {
        for (int i = 0; i < sliders.Count; i++)
        {
            Slider s = sliders[i];
            float width = s.track.rect.width > 1f ? s.track.rect.width : 640f;
            float t = Mathf.Clamp01(s.get());
            float x = t * width;
            if (s.centered)
            {
                float mid = width * 0.5f;
                Place(s.fill.rectTransform, Mathf.Min(x, mid), 0f, Mathf.Abs(x - mid), 52f);
            }
            else
                Place(s.fill.rectTransform, 0f, 0f, x, 52f);
            Place(s.handle, Mathf.Clamp(x - 5f, 0f, width - 10f), 0f, 10f, 52f);
            s.value.text = s.label();
        }
    }

    void PaintOptions()
    {
        if (smoothBox != null)
        {
            smoothBox.color = Hd2Settings.SmoothTurn ? OnColor : OffColor;
            smoothCheck.enabled = Hd2Settings.SmoothTurn;
        }

        for (int i = 0; i < micImages.Length; i++)
        {
            if (micImages[i] != null)
                micImages[i].color = (int)controls.MicrophoneMode == i ? OnColor : OffColor;
        }

        for (int i = 0; i < hudImages.Length; i++)
        {
            if (hudImages[i] != null)
                hudImages[i].color = (int)Hd2Hud.SavedSide == i ? OnColor : OffColor;
        }

        PaintSliders();
    }

    // ================================================================ Controls
    void BuildControls(RectTransform page)
    {
        Heading(page, "Button mappings", 0f);
        string[] headers = { "Left", "Action", "Right", "Action" };
        float[] columns = { 0f, 190f, 600f, 790f };
        float[] sizes = { 180f, 380f, 180f, 380f };
        for (int i = 0; i < headers.Length; i++)
        {
            var label = MakeText(page, headers[i], 24, TextAnchor.MiddleLeft, HeaderColor);
            Place(label.rectTransform, columns[i], -64f, sizes[i], 34f);
        }

        for (int row = 0; row < leftButtons.Length; row++)
        {
            float y = -106f - row * 66f;
            var leftName = MakeText(page, Hd2ControlMap.ButtonLabel(leftButtons[row]), 24, TextAnchor.MiddleLeft, Color.white);
            Place(leftName.rectTransform, 0f, y, 180f, 56f);
            actionLabels[(int)leftButtons[row]] = MakeChoice(page, leftButtons[row], 190f, y);
            var rightName = MakeText(page, Hd2ControlMap.ButtonLabel(rightButtons[row]), 24, TextAnchor.MiddleLeft, Color.white);
            Place(rightName.rectTransform, 600f, y, 180f, 56f);
            actionLabels[(int)rightButtons[row]] = MakeChoice(page, rightButtons[row], 790f, y);
        }

        float pickY = -106f - leftButtons.Length * 66f - 20f;
        var pickLabel = MakeText(page, "Pickups", 26, TextAnchor.MiddleLeft, Color.white);
        Place(pickLabel.rectTransform, 0f, pickY, 180f, 52f);
        var row2 = MakeImage(page, new Color(0f, 0f, 0f, 0f));
        Place(row2.rectTransform, 190f, pickY, 760f, 52f);
        pickupBox = MakeImage(row2.rectTransform, OffColor);
        Place(pickupBox.rectTransform, 0f, 0f, 52f, 52f);
        pickupCheck = MakeImage(pickupBox.rectTransform, Color.white);
        Place(pickupCheck.rectTransform, 14f, -14f, 24f, 24f);
        var text = MakeText(row2.rectTransform, "Hold Reload (Grip) to pick up weapons", 22, TextAnchor.MiddleLeft, Color.white);
        Place(text.rectTransform, 66f, 0f, 690f, 52f);
        clicks.Add(new Click { rect = row2.rectTransform, action = () => controls.PickupWithReload = !controls.PickupWithReload });

        float presetY = pickY - 72f;
        var presets = MakeText(page, "Defaults", 26, TextAnchor.MiddleLeft, Color.white);
        Place(presets.rectTransform, 0f, presetY, 180f, 52f);
        for (int i = 0; i < Hd2ControlMap.PresetCount; i++)
        {
            int preset = i + 1;
            var button = MakeImage(page, OffColor);
            Place(button.rectTransform, 190f + i * 210f, presetY, 196f, 52f);
            Stretch(MakeText(button.rectTransform, "Default " + preset, 22, TextAnchor.MiddleCenter, Color.white).rectTransform, 4f);
            presetImages[i] = button;
            clicks.Add(new Click { rect = button.rectTransform, action = () => { controls.ApplyPreset(preset); HideActionList(); } });
        }

        var hint = MakeText(page, "Point at an action to change what that button does.", 22, TextAnchor.MiddleLeft, HintColor);
        Place(hint.rectTransform, 0f, presetY - 70f, PageWidth, 34f);
        BuildActionList(page);
    }

    TMP_Text MakeChoice(RectTransform page, Hd2ControlMap.Button button, float x, float y)
    {
        var image = MakeImage(page, OffColor);
        Place(image.rectTransform, x, y, 380f, 56f);
        var label = MakeText(image.rectTransform, "", 22, TextAnchor.MiddleLeft, new Color(0.85f, 0.93f, 1f, 1f));
        Stretch(label.rectTransform, 14f);
        var captured = button;
        clicks.Add(new Click { rect = image.rectTransform, action = () => ShowActionList(captured, x) });
        return label;
    }

    void BuildActionList(RectTransform page)
    {
        actionList = new GameObject("ActionList");
        var rect = actionList.AddComponent<RectTransform>();
        rect.SetParent(page, false);
        var back = actionList.AddComponent<Image>();
        back.sprite = White();
        back.color = new Color(0.1f, 0.12f, 0.16f, 0.99f);
        back.raycastTarget = false;
        rect.sizeDelta = new Vector2(420f, 70f + Hd2ControlMap.ActionCount * 60f);
        actionListTitle = MakeText(rect, "Action", 24, TextAnchor.UpperLeft, Color.white);
        Place(actionListTitle.rectTransform, 16f, -14f, 388f, 36f);
        for (int i = 0; i < Hd2ControlMap.ActionCount; i++)
        {
            var action = (Hd2ControlMap.Action)i;
            var image = MakeImage(rect, KeyColor);
            Place(image.rectTransform, 16f, -60f - i * 60f, 388f, 52f);
            Stretch(MakeText(image.rectTransform, Hd2ControlMap.ActionLabel(action), 22, TextAnchor.MiddleLeft, Color.white).rectTransform, 12f);
            actionClicks.Add(new Click { rect = image.rectTransform, action = () => Choose(action) });
        }

        actionClicks.Add(new Click { rect = rect, action = null });
        actionList.SetActive(false);
    }

    void ShowActionList(Hd2ControlMap.Button button, float x)
    {
        picking = button;
        pickingValid = true;
        actionList.SetActive(true);
        actionList.transform.SetAsLastSibling();
        actionListTitle.text = Hd2ControlMap.ButtonLabel(button);
        var rect = actionList.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x < 500f ? 600f : 160f, -40f);
    }

    void HideActionList()
    {
        pickingValid = false;
        if (actionList != null)
            actionList.SetActive(false);
    }

    void Choose(Hd2ControlMap.Action action)
    {
        if (!pickingValid)
            return;
        controls.SetBinding(picking, action);
        HideActionList();
    }

    void PaintControls()
    {
        for (int i = 0; i < actionLabels.Length; i++)
        {
            if (actionLabels[i] != null)
                actionLabels[i].text = Hd2ControlMap.ActionLabel(controls.Binding((Hd2ControlMap.Button)i));
        }

        if (pickupBox != null)
        {
            pickupBox.color = controls.PickupWithReload ? OnColor : OffColor;
            pickupCheck.enabled = controls.PickupWithReload;
        }

        for (int i = 0; i < presetImages.Length; i++)
        {
            if (presetImages[i] != null)
                presetImages[i].color = i + 1 == controls.Preset ? OnColor : OffColor;
        }
    }

    // ================================================================ Customize and Quit
    void BuildCustomize(RectTransform page)
    {
        Heading(page, "Customize", 0f);
        string[] cards = { "Robot Bodies", "Heads", "Accessories" };
        float cardWidth = (PageWidth - 40f) / 3f;
        for (int i = 0; i < cards.Length; i++)
        {
            var card = MakeImage(page, new Color(0.09f, 0.11f, 0.16f, 1f));
            Place(card.rectTransform, i * (cardWidth + 20f), -80f, cardWidth, 560f);
            var title = MakeText(card.rectTransform, cards[i], 30, TextAnchor.UpperCenter, Color.white);
            title.fontStyle = FontStyles.Bold;
            Place(title.rectTransform, 0f, -24f, cardWidth, 44f);
            var art = MakeImage(card.rectTransform, new Color(0.13f, 0.16f, 0.23f, 1f));
            Place(art.rectTransform, 30f, -90f, cardWidth - 60f, 330f);
            var soon = MakeText(card.rectTransform, "Coming soon", 28, TextAnchor.MiddleCenter, HintColor);
            Place(soon.rectTransform, 0f, -460f, cardWidth, 50f);
        }
    }

    void BuildQuit(RectTransform page)
    {
        Heading(page, "Quit", 0f);
        var ask = MakeText(page, "Leave Thruster Blitz?", 30, TextAnchor.MiddleLeft, Color.white);
        Place(ask.rectTransform, 0f, -100f, PageWidth, 50f);
        var quit = MakeImage(page, DangerColor);
        Place(quit.rectTransform, 0f, -180f, 320f, 80f);
        Stretch(MakeText(quit.rectTransform, "Quit game", 28, TextAnchor.MiddleCenter, Color.white).rectTransform, 4f);
        clicks.Add(new Click { rect = quit.rectTransform, action = Quit });
        var version = MakeText(page, "Version " + VersionLabel(), 22, TextAnchor.MiddleLeft, HintColor);
        Place(version.rectTransform, 0f, -290f, PageWidth, 36f);
    }

    static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    static string VersionLabel()
    {
        TextAsset stamp = Resources.Load<TextAsset>("BuildVersion");
        string version = stamp != null && !string.IsNullOrEmpty(stamp.text) ? stamp.text.Trim() : Application.version;
#if UNITY_EDITOR
        return version + " editor";
#else
        return version;
#endif
    }

    void RefreshAll()
    {
        PaintOptions();
        PaintControls();
        PaintCreate();
        RefreshProfile();
    }

    // ================================================================ UI helpers
    static void Place(RectTransform rect, float x, float y, float w, float h)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(w, h);
    }

    static void Stretch(RectTransform rect, float pad)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(pad, 0f);
        rect.offsetMax = new Vector2(-pad, 0f);
    }

    static Image MakeImage(RectTransform parent, Color color)
    {
        var go = new GameObject("Cell");
        var rect = go.AddComponent<RectTransform>();
        rect.SetParent(parent, false);
        var image = go.AddComponent<Image>();
        image.sprite = White();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    static TMP_Text MakeText(RectTransform parent, string value, int size, TextAnchor anchor, Color color)
    {
        // TextMeshPro (signed-distance-field glyphs) stays sharp at any size and distance in
        // VR; the old UI Text font texture has no mipmaps and shimmered on the screen.
        var go = new GameObject("Label");
        var rect = go.AddComponent<RectTransform>();
        rect.SetParent(parent, false);
        var text = go.AddComponent<TextMeshProUGUI>();
        if (TMP_Settings.defaultFontAsset != null)
            text.font = TMP_Settings.defaultFontAsset;
        text.text = value;
        text.fontSize = size;
        text.alignment = Align(anchor);
        text.color = color;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        return text;
    }

    static TextAlignmentOptions Align(TextAnchor anchor)
    {
        switch (anchor)
        {
            case TextAnchor.UpperLeft:
                return TextAlignmentOptions.TopLeft;
            case TextAnchor.UpperCenter:
                return TextAlignmentOptions.Top;
            case TextAnchor.UpperRight:
                return TextAlignmentOptions.TopRight;
            case TextAnchor.MiddleCenter:
                return TextAlignmentOptions.Center;
            case TextAnchor.MiddleRight:
                return TextAlignmentOptions.Right;
            case TextAnchor.LowerLeft:
                return TextAlignmentOptions.BottomLeft;
            case TextAnchor.LowerCenter:
                return TextAlignmentOptions.Bottom;
            case TextAnchor.LowerRight:
                return TextAlignmentOptions.BottomRight;
            default:
                return TextAlignmentOptions.Left;
        }
    }

    static Sprite White()
    {
        if (white != null)
            return white;
        var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        tex.hideFlags = HideFlags.DontSave;
        white = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
        white.hideFlags = HideFlags.DontSave;
        return white;
    }

    static bool EditorFallback
    {
        get
        {
            if (!Application.isEditor)
                return false;
            displays.Clear();
            SubsystemManager.GetSubsystems(displays);
            for (int i = 0; i < displays.Count; i++)
            {
                if (displays[i].running)
                    return false;
            }

            return true;
        }
    }
}
