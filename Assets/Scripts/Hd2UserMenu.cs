using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR;

/// <summary>
/// World-space menu for this player only. The left controller menu button opens and closes it.
/// Game is the first tab, with the match scoreboard. Controls rebinds the buttons, the microphone mode, and the HUD side.
/// </summary>
[DefaultExecutionOrder(-50)]
public class Hd2UserMenu : MonoBehaviour
{
    static readonly Hd2ControlMap.Button[] leftButtons =
    {
        Hd2ControlMap.Button.LeftStickPress,
        Hd2ControlMap.Button.LeftTrigger,
        Hd2ControlMap.Button.LeftGrip,
        Hd2ControlMap.Button.LeftX,
        Hd2ControlMap.Button.LeftY
    };

    static readonly Hd2ControlMap.Button[] rightButtons =
    {
        Hd2ControlMap.Button.RightStickPress,
        Hd2ControlMap.Button.RightTrigger,
        Hd2ControlMap.Button.RightGrip,
        Hd2ControlMap.Button.RightA,
        Hd2ControlMap.Button.RightB
    };

    const float width = 1180f;
    const float height = 760f;

    Hd2ControlMap controls;
    Hd2Hud hud;
    InputAction menuButton;
    InputAction editorMenu;
    Camera ownerCamera;
    Canvas canvas;
    RectTransform panel;
    GameObject gamePage;
    GameObject controlsPage;
    Image gameTabImage;
    Image controlsTabImage;
    Text pageTitle;
    GameObject actionList;
    Text actionListTitle;
    readonly List<Click> clicks = new List<Click>();
    readonly List<Click> actionClicks = new List<Click>();
    readonly Text[] actionLabels = new Text[Hd2ControlMap.ButtonCount];
    readonly Image[] micImages = new Image[3];
    readonly Image[] hudImages = new Image[3];
    Image switchTeams;
    readonly List<ScoreRow> scoreRows = new List<ScoreRow>();

    // Name and clan tag, edited with an on-screen keyboard.
    Text nameButtonLabel;
    Text clanButtonLabel;
    GameObject keyboard;
    Text keyboardTitle;
    Text keyboardField;
    Text keyboardMessage;
    readonly List<Click> keyboardClicks = new List<Click>();
    readonly List<KeyCap> keyCaps = new List<KeyCap>();
    Image shiftKey;
    bool editingClan;
    bool shift;
    string typed = "";

    class KeyCap
    {
        public char lower;
        public Text label;
        public Image image;
    }

    static readonly string[] keyRows = { "1234567890", "qwertyuiop", "asdfghjkl'", "zxcvbnm.-_" };
    static readonly Color KeyColor = new Color(0.16f, 0.2f, 0.26f, 1f);
    static readonly Color KeyOffColor = new Color(0.1f, 0.11f, 0.13f, 1f);
    static readonly Color HintColor = new Color(0.65f, 0.78f, 0.9f, 1f);
    readonly List<TeamHeader> teamHeaders = new List<TeamHeader>();
    float nextScoreRefresh;

    // Game tab scoreboard: Rank, Name, Kills, Deaths, Score.
    const int ScoreRowCount = 16;
    const float ScoreLeft = 344f;
    const float ScoreRowHeight = 34f;
    const float ScoreRowGap = 3f;
    const float ScoreTeamGap = 12f;
    const float TeamHeaderHeight = 36f;
    static readonly float[] scoreColumns = { 0f, 70f, 470f, 570f, 680f };
    static readonly float[] scoreWidths = { 66f, 396f, 96f, 106f, 106f };
    static readonly string[] scoreHeaders = { "Rank", "Name", "Kills", "Deaths", "Score" };

    class TeamHeader
    {
        public Image background;
        public Text name;
        public Text score;
    }

    class ScoreRow
    {
        public Image background;
        public Text[] cells;
    }
    LineRenderer laser;
    bool open;
    Hd2ControlMap.Button picking;
    bool pickingValid;
    static Sprite white;
    static readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();

    class Click
    {
        public RectTransform rect;
        public System.Action action;
        // Sliders: called with 0..1 across the rect while the trigger is held on it.
        public System.Action<float> slide;
    }

    RectTransform gripTrack;
    Image gripFill;
    RectTransform gripHandle;
    Text gripValue;
    Image pickupBox;
    Image pickupCheck;
    readonly Image[] presetImages = new Image[Hd2ControlMap.PresetCount];
    static readonly Color OffColor = new Color(0.14f, 0.17f, 0.22f, 1f);
    static readonly Color OnColor = new Color(0.2f, 0.5f, 0.78f, 1f);

    void Awake()
    {
        controls = GetComponent<Hd2ControlMap>();
        if (controls == null)
            controls = gameObject.AddComponent<Hd2ControlMap>();
        controls.Changed += RefreshLabels;
        Hd2Identity.Changed += RefreshIdentity;
        Build();
        SetOpen(false);
    }

    void Start()
    {
        PaintHud();
    }

    void OnEnable()
    {
        menuButton = new InputAction("Hd2UserMenu", InputActionType.Button);
        menuButton.AddBinding("<XRController>{LeftHand}/menuButton");
        menuButton.Enable();

        editorMenu = new InputAction("Hd2UserMenuEditor", InputActionType.Button);
        editorMenu.AddBinding("<Keyboard>/m");
        editorMenu.Enable();

        RenderPipelineManager.beginCameraRendering += CullToOwner;
        RenderPipelineManager.endCameraRendering += RestoreAfterCamera;
    }

    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= CullToOwner;
        RenderPipelineManager.endCameraRendering -= RestoreAfterCamera;
        if (menuButton != null)
        {
            menuButton.Disable();
            menuButton.Dispose();
            menuButton = null;
        }

        if (editorMenu != null)
        {
            editorMenu.Disable();
            editorMenu.Dispose();
            editorMenu = null;
        }
    }

    void OnDestroy()
    {
        if (controls != null)
            controls.Changed -= RefreshLabels;
        Hd2Identity.Changed -= RefreshIdentity;
        StopEditorTyping();
    }

    void Update()
    {
        bool toggle = menuButton != null && menuButton.WasPressedThisFrame();
        if (!toggle && EditorFallback && editorMenu != null && !KeyboardOpen)
            toggle = editorMenu.WasPressedThisFrame();
        if (toggle)
        {
            nextScoreRefresh = 0f;
            SetOpen(!open);
        }

        if (!open)
            return;

        RefreshSwitchTeams();
        if (KeyboardOpen)
            EditorKeys();
        if (gamePage != null && gamePage.activeSelf && Time.unscaledTime >= nextScoreRefresh)
            RefreshScoreboard();
        Aim();
    }

    void SetOpen(bool value)
    {
        open = value;
        if (controls != null)
            controls.MenuOpen = value;
        if (canvas != null)
            canvas.gameObject.SetActive(value);
        if (laser != null)
            laser.enabled = false;
        if (!value)
        {
            HideActionList();
            CloseKeyboard();
        }
        if (!value)
            return;

        var cam = OwnerCamera();
        if (cam == null)
            return;
        Transform view = cam.transform;
        Vector3 forward = Vector3.ProjectOnPlane(view.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.ProjectOnPlane(view.up, Vector3.up);
        forward.Normalize();
        Vector3 place = view.position + forward * 1.15f;
        canvas.transform.SetPositionAndRotation(place, Quaternion.LookRotation(forward, Vector3.up));
    }

    void Aim()
    {
        var cam = OwnerCamera();
        if (cam == null || panel == null)
            return;

        var move = GetComponent<Hd2Locomotion>();
        Transform left = move != null ? move.leftHand : transform.Find("LeftHand");
        Transform right = move != null ? move.rightHand : transform.Find("RightHand");
        bool hit = TryHand(left, Hd2ControlMap.Button.LeftTrigger, cam);
        if (!hit)
            TryHand(right, Hd2ControlMap.Button.RightTrigger, cam);
    }

    static Vector3 HandPointer(Transform hand)
    {
        // The controller's forward axis points up the grip. The barrel is level
        // with the hand: the muzzle's forward, which is the gun's local -Y.
        Transform muzzle = null;
        var pistol = hand.GetComponentInChildren<Hd2Pistol>();
        if (pistol != null && pistol.muzzle != null)
            muzzle = pistol.muzzle;
        if (muzzle == null)
        {
            var laser = hand.GetComponentInChildren<Hd2LaserPistol>();
            if (laser != null && laser.muzzle != null)
                muzzle = laser.muzzle;
        }

        if (muzzle == null)
        {
            var shocks = hand.GetComponentInChildren<Hd2Shocks>();
            if (shocks != null && shocks.muzzle != null)
                muzzle = shocks.muzzle;
        }

        if (muzzle == null)
        {
            var shotgun = hand.GetComponentInChildren<Hd2Shotgun>();
            if (shotgun != null && shotgun.muzzle != null)
                muzzle = shotgun.muzzle;
        }

        if (muzzle == null)
        {
            var smg = hand.GetComponentInChildren<Hd2Smg>();
            if (smg != null && smg.muzzle != null)
                muzzle = smg.muzzle;
        }

        if (muzzle == null)
        {
            var rocket = hand.GetComponentInChildren<Hd2RocketLauncher>();
            if (rocket != null && rocket.muzzle != null)
                muzzle = rocket.muzzle;
        }

        if (muzzle != null)
            return muzzle.forward;
        return hand.TransformDirection(Vector3.down);
    }

    bool TryHand(Transform hand, Hd2ControlMap.Button trigger, Camera cam)
    {
        if (hand == null)
            return false;

        Vector3 direction = HandPointer(hand);
        if (direction.sqrMagnitude < 0.0001f)
            return false;

        Ray ray = new Ray(hand.position, direction);
        Plane plane = new Plane(-canvas.transform.forward, canvas.transform.position);
        if (!plane.Raycast(ray, out float distance) || distance > 3f)
        {
            if (laser != null)
                laser.enabled = false;
            return false;
        }

        Vector3 point = ray.GetPoint(distance);
        ShowLaser(hand.position, point);
        Vector2 screen = cam.WorldToScreenPoint(point);
        Click click = Pick(screen, cam);
        if (click != null && click.slide != null && controls.ButtonHeld(trigger))
        {
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(click.rect, screen, cam, out Vector2 local))
            {
                Rect area = click.rect.rect;
                click.slide(Mathf.InverseLerp(area.xMin, area.xMax, local.x));
            }

            controls.Consume(trigger);
            return true;
        }

        if (click == null || !controls.ButtonPressed(trigger))
            return click != null;

        controls.Consume(trigger);
        click.action();
        return true;
    }

    Click Pick(Vector2 screen, Camera cam)
    {
        if (KeyboardOpen)
        {
            for (int i = 0; i < keyboardClicks.Count; i++)
            {
                if (keyboardClicks[i].rect != null && keyboardClicks[i].rect.gameObject.activeInHierarchy
                    && Inside(keyboardClicks[i].rect, screen, cam))
                    return keyboardClicks[i];
            }

            // The keyboard covers the menu; nothing behind it can be clicked.
            return null;
        }

        if (actionList != null && actionList.activeSelf)
        {
            for (int i = 0; i < actionClicks.Count; i++)
            {
                if (actionClicks[i].rect != null && actionClicks[i].rect.gameObject.activeInHierarchy
                    && Inside(actionClicks[i].rect, screen, cam))
                    return actionClicks[i];
            }
        }

        for (int i = clicks.Count - 1; i >= 0; i--)
        {
            if (clicks[i].rect != null && clicks[i].rect.gameObject.activeInHierarchy
                && Inside(clicks[i].rect, screen, cam))
                return clicks[i];
        }

        return null;
    }

    static bool Inside(RectTransform rect, Vector2 screen, Camera cam)
    {
        return rect != null && RectTransformUtility.RectangleContainsScreenPoint(rect, screen, cam);
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
            var shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader != null)
            {
                laser.material = new Material(shader);
                laser.material.color = new Color(0.45f, 0.8f, 1f, 1f);
            }
        }

        laser.enabled = true;
        laser.SetPosition(0, from);
        laser.SetPosition(1, to);
    }

    void CullToOwner(ScriptableRenderContext context, Camera camera)
    {
        if (canvas == null)
            return;
        bool mine = open && camera == OwnerCamera();
        canvas.enabled = mine;
        if (laser != null)
            laser.forceRenderingOff = !mine;
    }

    void RestoreAfterCamera(ScriptableRenderContext context, Camera camera)
    {
        if (canvas != null)
            canvas.enabled = open;
        if (laser != null)
            laser.forceRenderingOff = false;
    }

    Camera OwnerCamera()
    {
        if (ownerCamera == null)
            ownerCamera = GetComponentInChildren<Camera>();
        return ownerCamera;
    }

    void Build()
    {
        var root = new GameObject("UserMenu");
        root.transform.SetParent(transform, false);
        canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = OwnerCamera();
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 10f;
        root.AddComponent<GraphicRaycaster>();
        var rootRect = root.GetComponent<RectTransform>();
        rootRect.sizeDelta = new Vector2(width, height);
        root.transform.localScale = Vector3.one * 0.001f;

        panel = rootRect;
        var background = root.AddComponent<Image>();
        background.sprite = White();
        background.color = new Color(0.07f, 0.08f, 0.1f, 0.94f);
        background.raycastTarget = false;

        var gameTab = MakeButton(panel, "Game", new Vector2(16f, -16f), new Vector2(140f, 48f), new Color(0.2f, 0.5f, 0.78f, 1f));
        gameTabImage = gameTab.GetComponent<Image>();
        Stretch(gameTab.GetComponentInChildren<Text>().rectTransform, 8f);
        clicks.Add(new Click { rect = gameTab, action = ShowGame });

        var tab = MakeButton(panel, "Controls", new Vector2(168f, -16f), new Vector2(180f, 48f), new Color(0.14f, 0.17f, 0.22f, 1f));
        controlsTabImage = tab.GetComponent<Image>();
        Stretch(tab.GetComponentInChildren<Text>().rectTransform, 8f);
        clicks.Add(new Click { rect = tab, action = ShowControls });

        var title = MakeText(panel, "Game", 34, TextAnchor.UpperLeft, Color.white);
        var titleRect = title.rectTransform;
        titleRect.anchorMin = titleRect.anchorMax = new Vector2(0f, 1f);
        titleRect.pivot = new Vector2(0f, 1f);
        titleRect.anchoredPosition = new Vector2(364f, -18f);
        titleRect.sizeDelta = new Vector2(500f, 46f);
        pageTitle = title;

        controlsPage = new GameObject("ControlsPage");
        var page = controlsPage.AddComponent<RectTransform>();
        page.SetParent(panel, false);
        page.anchorMin = Vector2.zero;
        page.anchorMax = Vector2.one;
        page.offsetMin = new Vector2(24f, 24f);
        page.offsetMax = new Vector2(-24f, -84f);

        BuildTable(page);
        BuildMicrophone(page);
        BuildActionList(page);
        BuildGame();
        ShowGame();
        RefreshLabels();
    }

    void ShowGame()
    {
        HideActionList();
        nextScoreRefresh = 0f;
        if (gamePage != null)
            gamePage.SetActive(true);
        if (controlsPage != null)
            controlsPage.SetActive(false);
        if (pageTitle != null)
            pageTitle.text = "Game";
        PaintTabs(true);
    }

    void ShowControls()
    {
        HideActionList();
        if (gamePage != null)
            gamePage.SetActive(false);
        if (controlsPage != null)
            controlsPage.SetActive(true);
        if (pageTitle != null)
            pageTitle.text = "Controls";
        PaintTabs(false);
    }

    void BuildGame()
    {
        gamePage = new GameObject("GamePage");
        var page = gamePage.AddComponent<RectTransform>();
        page.SetParent(panel, false);
        page.anchorMin = Vector2.zero;
        page.anchorMax = Vector2.one;
        page.offsetMin = new Vector2(24f, 24f);
        page.offsetMax = new Vector2(-24f, -84f);

        switchTeams = MakeImage(page, new Color(0.2f, 0.5f, 0.78f, 1f));
        Place(switchTeams.rectTransform, 0f, -8f, 320f, 72f);
        Stretch(MakeText(switchTeams.rectTransform, "Switch teams", 24, TextAnchor.MiddleCenter, Color.white).rectTransform, 8f);
        clicks.Add(new Click { rect = switchTeams.rectTransform, action = SwitchTeams });
        switchTeams.gameObject.SetActive(false);

        var quit = MakeImage(page, new Color(0.55f, 0.12f, 0.1f, 1f));
        Place(quit.rectTransform, 0f, -96f, 320f, 72f);
        Stretch(MakeText(quit.rectTransform, "Quit", 24, TextAnchor.MiddleCenter, Color.white).rectTransform, 8f);
        clicks.Add(new Click { rect = quit.rectTransform, action = QuitMatch });

        var home = MakeImage(page, new Color(0.2f, 0.5f, 0.78f, 1f));
        Place(home.rectTransform, 0f, -184f, 320f, 72f);
        Stretch(MakeText(home.rectTransform, "Return to start", 24, TextAnchor.MiddleCenter, Color.white).rectTransform, 8f);
        clicks.Add(new Click { rect = home.rectTransform, action = ReturnToStart });

        BuildIdentity(page);
        BuildScoreboard(page);
        BuildKeyboard();
    }

    void BuildIdentity(RectTransform page)
    {
        var nameCaption = MakeText(page, "Name", 22, TextAnchor.MiddleLeft, HintColor);
        Place(nameCaption.rectTransform, 0f, -276f, 320f, 30f);
        var name = MakeImage(page, KeyColor);
        Place(name.rectTransform, 0f, -308f, 320f, 60f);
        nameButtonLabel = MakeText(name.rectTransform, "", 22, TextAnchor.MiddleLeft, Color.white);
        Stretch(nameButtonLabel.rectTransform, 14f);
        clicks.Add(new Click { rect = name.rectTransform, action = () => OpenKeyboard(false) });

        var clanCaption = MakeText(page, "Clan tag", 22, TextAnchor.MiddleLeft, HintColor);
        Place(clanCaption.rectTransform, 0f, -380f, 320f, 30f);
        var clan = MakeImage(page, KeyColor);
        Place(clan.rectTransform, 0f, -412f, 320f, 60f);
        clanButtonLabel = MakeText(clan.rectTransform, "", 22, TextAnchor.MiddleLeft, Color.white);
        Stretch(clanButtonLabel.rectTransform, 14f);
        clicks.Add(new Click { rect = clan.rectTransform, action = () => OpenKeyboard(true) });

        RefreshIdentity();
    }

    void RefreshIdentity()
    {
        if (nameButtonLabel != null)
            nameButtonLabel.text = Hd2Identity.Name;
        if (clanButtonLabel != null)
            clanButtonLabel.text = string.IsNullOrEmpty(Hd2Identity.Clan) ? "None" : Hd2Identity.Clan;
        nextScoreRefresh = 0f;
    }

    bool KeyboardOpen => keyboard != null && keyboard.activeSelf;

    void BuildKeyboard()
    {
        keyboard = new GameObject("Keyboard");
        var rect = keyboard.AddComponent<RectTransform>();
        rect.SetParent(panel, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        var back = keyboard.AddComponent<Image>();
        back.sprite = White();
        back.color = new Color(0.06f, 0.07f, 0.09f, 0.99f);
        back.raycastTarget = false;

        keyboardTitle = MakeText(rect, "Name", 30, TextAnchor.MiddleLeft, Color.white);
        Place(keyboardTitle.rectTransform, 65f, -20f, 1050f, 44f);

        var field = MakeImage(rect, new Color(0.13f, 0.15f, 0.19f, 1f));
        Place(field.rectTransform, 65f, -72f, 1050f, 70f);
        keyboardField = MakeText(field.rectTransform, "", 34, TextAnchor.MiddleLeft, Color.white);
        Stretch(keyboardField.rectTransform, 18f);

        keyboardMessage = MakeText(rect, "", 22, TextAnchor.MiddleLeft, HintColor);
        Place(keyboardMessage.rectTransform, 65f, -150f, 1050f, 36f);

        const float left = 65f;
        const float key = 96f;
        const float gap = 9.5f;
        for (int row = 0; row < keyRows.Length; row++)
        {
            string chars = keyRows[row];
            for (int col = 0; col < chars.Length; col++)
            {
                char lower = chars[col];
                var image = MakeImage(rect, KeyColor);
                Place(image.rectTransform, left + col * (key + gap), -200f - row * 80f, key, 70f);
                var label = MakeText(image.rectTransform, lower.ToString(), 30, TextAnchor.MiddleCenter, Color.white);
                Stretch(label.rectTransform, 0f);
                var cap = new KeyCap { lower = lower, label = label, image = image };
                keyCaps.Add(cap);
                keyboardClicks.Add(new Click { rect = image.rectTransform, action = () => TypeKey(cap) });
            }
        }

        float bottom = -200f - keyRows.Length * 80f;
        shiftKey = KeyboardButton(rect, "Shift", left, bottom, 202f, ToggleShift);
        KeyboardButton(rect, "Space", left + 212f, bottom, 414f, () => TypeChar(' '));
        KeyboardButton(rect, "Back", left + 636f, bottom, 202f, Backspace);
        KeyboardButton(rect, "Clear", left + 848f, bottom, 202f, () => { typed = ""; RefreshKeyboard(); });

        float actions = bottom - 84f;
        var cancel = KeyboardButton(rect, "Cancel", left + 430f, actions, 300f, CloseKeyboard);
        cancel.color = new Color(0.3f, 0.32f, 0.36f, 1f);
        var done = KeyboardButton(rect, "Done", left + 750f, actions, 300f, FinishKeyboard);
        done.color = new Color(0.2f, 0.5f, 0.78f, 1f);

        keyboard.SetActive(false);
    }

    Image KeyboardButton(RectTransform parent, string text, float x, float y, float w, System.Action action)
    {
        var image = MakeImage(parent, KeyColor);
        Place(image.rectTransform, x, y, w, 70f);
        Stretch(MakeText(image.rectTransform, text, 26, TextAnchor.MiddleCenter, Color.white).rectTransform, 4f);
        keyboardClicks.Add(new Click { rect = image.rectTransform, action = action });
        return image;
    }

    void OpenKeyboard(bool clan)
    {
        HideActionList();
        editingClan = clan;
        typed = clan ? Hd2Identity.Clan : Hd2Identity.Name;
        // Tags are usually capitals; names start with one.
        shift = clan || typed.Length == 0;
        keyboardTitle.text = clan ? "Clan tag" : "Name";
        keyboard.SetActive(true);
        keyboard.transform.SetAsLastSibling();
        ShowRules();
        RefreshKeyboard();
        StartEditorTyping();
    }

    void ShowRules()
    {
        keyboardMessage.color = HintColor;
        keyboardMessage.text = editingClan
            ? "Up to " + Hd2Identity.ClanMax + " letters or numbers, shown before your name. Leave empty for none."
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
            shiftKey.color = shift ? new Color(0.2f, 0.5f, 0.78f, 1f) : KeyColor;
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

    // In the editor without a headset, the real keyboard types too.
    bool editorTyping;

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
        if (!KeyboardOpen || c < ' ')
            return;
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

    void BuildScoreboard(RectTransform page)
    {
        for (int c = 0; c < scoreHeaders.Length; c++)
        {
            var header = MakeText(page, scoreHeaders[c], 22, c == 1 ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter,
                new Color(0.65f, 0.78f, 0.9f, 1f));
            Place(header.rectTransform, ScoreLeft + scoreColumns[c] + (c == 1 ? 8f : 0f), -8f, scoreWidths[c], 32f);
        }

        float rowWidth = scoreColumns[scoreColumns.Length - 1] + scoreWidths[scoreWidths.Length - 1];
        for (int r = 0; r < ScoreRowCount; r++)
        {
            var row = new ScoreRow { cells = new Text[scoreHeaders.Length] };
            row.background = MakeImage(page, Color.clear);
            Place(row.background.rectTransform, ScoreLeft, 0f, rowWidth, ScoreRowHeight);
            for (int c = 0; c < scoreHeaders.Length; c++)
            {
                var cell = MakeText(row.background.rectTransform, "", 21, c == 1 ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, Color.white);
                Place(cell.rectTransform, scoreColumns[c] + (c == 1 ? 8f : 0f), 0f, scoreWidths[c] - (c == 1 ? 8f : 0f), ScoreRowHeight);
                row.cells[c] = cell;
            }

            row.background.gameObject.SetActive(false);
            scoreRows.Add(row);
        }

        // One header per team group: team name on the left, the team's score on the right.
        for (int h = 0; h < 3; h++)
        {
            var header = new TeamHeader();
            header.background = MakeImage(page, Color.clear);
            Place(header.background.rectTransform, ScoreLeft, 0f, rowWidth, TeamHeaderHeight);
            header.name = MakeText(header.background.rectTransform, "", 24, TextAnchor.MiddleLeft, Color.white);
            header.name.fontStyle = FontStyle.Bold;
            Stretch(header.name.rectTransform, 10f);
            header.score = MakeText(header.background.rectTransform, "", 24, TextAnchor.MiddleRight, Color.white);
            header.score.fontStyle = FontStyle.Bold;
            Stretch(header.score.rectTransform, 12f);
            header.background.gameObject.SetActive(false);
            teamHeaders.Add(header);
        }
    }

    static string TeamLabel(Hd2Team team)
    {
        return team == Hd2Team.Red ? "Red" : team == Hd2Team.Blue ? "Blue" : "No team";
    }

    void RefreshScoreboard()
    {
        nextScoreRefresh = Time.unscaledTime + 0.5f;
        List<Hd2MatchStats.Row> roster = Hd2MatchStats.Roster();
        float y = -46f;
        Hd2Team lastTeam = Hd2Team.None;
        int headersUsed = 0;
        for (int r = 0; r < scoreRows.Count; r++)
        {
            ScoreRow row = scoreRows[r];
            bool used = r < roster.Count;
            if (row.background.gameObject.activeSelf != used)
                row.background.gameObject.SetActive(used);
            if (!used)
                continue;

            Hd2MatchStats.Row data = roster[r];
            if ((r == 0 || data.Team != lastTeam) && headersUsed < teamHeaders.Count)
            {
                if (r > 0)
                    y -= ScoreTeamGap;
                TeamHeader header = teamHeaders[headersUsed++];
                if (!header.background.gameObject.activeSelf)
                    header.background.gameObject.SetActive(true);
                header.background.rectTransform.anchoredPosition = new Vector2(ScoreLeft, y);
                Color shade = data.Team == Hd2Team.None
                    ? new Color(0.22f, 0.24f, 0.28f, 1f)
                    : Hd2Locomotion.ArmorOf(data.Team);
                header.background.color = new Color(shade.r * 0.55f, shade.g * 0.55f, shade.b * 0.55f, 1f);
                header.name.text = TeamLabel(data.Team);
                header.score.text = data.Team == Hd2Team.None ? "" : Hd2MatchStats.TeamScore(data.Team).ToString();
                y -= TeamHeaderHeight + ScoreRowGap;
            }
            lastTeam = data.Team;
            row.background.rectTransform.anchoredPosition = new Vector2(ScoreLeft, y);
            y -= ScoreRowHeight + ScoreRowGap;

            row.background.color = data.Team == Hd2Team.None
                ? new Color(0.22f, 0.24f, 0.28f, 1f)
                : Hd2Locomotion.ArmorOf(data.Team);
            FontStyle style = data.IsYou ? FontStyle.Bold : FontStyle.Normal;
            SetCell(row, 0, data.Rank.ToString(), style);
            SetCell(row, 1, data.Name, style);
            SetCell(row, 2, data.Kills.ToString(), style);
            SetCell(row, 3, data.Deaths.ToString(), style);
            SetCell(row, 4, data.Score.ToString(), style);
        }

        for (int h = headersUsed; h < teamHeaders.Count; h++)
        {
            if (teamHeaders[h].background.gameObject.activeSelf)
                teamHeaders[h].background.gameObject.SetActive(false);
        }
    }

    static void SetCell(ScoreRow row, int column, string value, FontStyle style)
    {
        Text cell = row.cells[column];
        if (cell.text != value)
            cell.text = value;
        if (cell.fontStyle != style)
            cell.fontStyle = style;
    }

    void ReturnToStart()
    {
        SetOpen(false);
        SceneManager.LoadScene("Title");
    }

    void QuitMatch()
    {
        var move = GetComponent<Hd2Locomotion>();
        if (move == null)
            return;

        SetOpen(false);
        move.LeaveMatch();
    }

    void RefreshSwitchTeams()
    {
        if (switchTeams == null)
            return;

        var move = GetComponent<Hd2Locomotion>();
        bool show = move != null && move.CanSwitchTeam();
        if (switchTeams.gameObject.activeSelf != show)
            switchTeams.gameObject.SetActive(show);
    }

    void SwitchTeams()
    {
        var move = GetComponent<Hd2Locomotion>();
        if (move == null || !move.CanSwitchTeam())
            return;

        SetOpen(false);
        move.SwitchTeam();
    }

    void PaintTabs(bool game)
    {
        if (gameTabImage != null)
            gameTabImage.color = game
                ? new Color(0.2f, 0.5f, 0.78f, 1f)
                : new Color(0.14f, 0.17f, 0.22f, 1f);
        if (controlsTabImage != null)
            controlsTabImage.color = game
                ? new Color(0.14f, 0.17f, 0.22f, 1f)
                : new Color(0.2f, 0.5f, 0.78f, 1f);
    }

    void BuildTable(RectTransform page)
    {
        string[] headers = { "Left", "Action", "Right", "Action" };
        float[] columns = { 0f, 180f, 560f, 740f };
        float[] sizes = { 170f, 360f, 170f, 360f };
        for (int i = 0; i < headers.Length; i++)
        {
            var label = MakeText(page, headers[i], 26, TextAnchor.MiddleLeft, new Color(0.65f, 0.78f, 0.9f, 1f));
            Place(label.rectTransform, columns[i], -8f, sizes[i], 36f);
        }

        for (int row = 0; row < leftButtons.Length; row++)
        {
            float y = -52f - row * 62f;
            var leftName = MakeText(page, Hd2ControlMap.ButtonLabel(leftButtons[row]), 24, TextAnchor.MiddleLeft, Color.white);
            Place(leftName.rectTransform, 0f, y, 170f, 52f);
            actionLabels[(int)leftButtons[row]] = MakeChoice(page, leftButtons[row], 180f, y);

            var rightName = MakeText(page, Hd2ControlMap.ButtonLabel(rightButtons[row]), 24, TextAnchor.MiddleLeft, Color.white);
            Place(rightName.rectTransform, 560f, y, 170f, 52f);
            actionLabels[(int)rightButtons[row]] = MakeChoice(page, rightButtons[row], 740f, y);
        }
    }

    Text MakeChoice(RectTransform page, Hd2ControlMap.Button button, float x, float y)
    {
        var image = MakeImage(page, new Color(0.14f, 0.17f, 0.22f, 1f));
        Place(image.rectTransform, x, y, 360f, 52f);
        var label = MakeText(image.rectTransform, Hd2ControlMap.ActionLabel(controls.Binding(button)), 22, TextAnchor.MiddleLeft, new Color(0.85f, 0.93f, 1f, 1f));
        Stretch(label.rectTransform, 12f);
        var captured = button;
        clicks.Add(new Click
        {
            rect = image.rectTransform,
            action = () => ShowActionList(captured)
        });
        return label;
    }

    void BuildMicrophone(RectTransform page)
    {
        var label = MakeText(page, "Microphone", 26, TextAnchor.MiddleLeft, Color.white);
        Place(label.rectTransform, 0f, -366f, 240f, 40f);

        for (int i = 0; i < 3; i++)
        {
            var mode = (Hd2ControlMap.MicMode)i;
            var image = MakeImage(page, new Color(0.14f, 0.17f, 0.22f, 1f));
            Place(image.rectTransform, 250f + i * 280f, -360f, 260f, 52f);
            Stretch(MakeText(image.rectTransform, Hd2ControlMap.MicLabel(mode), 22, TextAnchor.MiddleCenter, Color.white).rectTransform, 8f);
            micImages[i] = image;
            var captured = mode;
            clicks.Add(new Click
            {
                rect = image.rectTransform,
                action = () => controls.MicrophoneMode = captured
            });
        }

        PaintMic();
        BuildHudChoice(page);
        BuildGripAngle(page);
        BuildPickupToggle(page);
        BuildPresets(page);

        var hint = MakeText(page, "Left menu button closes", 20, TextAnchor.MiddleRight, new Color(0.75f, 0.8f, 0.85f, 1f));
        Place(hint.rectTransform, 830f, -594f, 302f, 52f);
    }

    void BuildGripAngle(RectTransform page)
    {
        var label = MakeText(page, "Grip angle", 26, TextAnchor.MiddleLeft, Color.white);
        Place(label.rectTransform, 0f, -482f, 240f, 40f);

        var track = MakeImage(page, OffColor);
        Place(track.rectTransform, 250f, -476f, 540f, 52f);
        gripTrack = track.rectTransform;

        gripFill = MakeImage(gripTrack, OnColor);
        var middle = MakeImage(gripTrack, new Color(1f, 1f, 1f, 0.35f));
        Place(middle.rectTransform, 269f, -6f, 2f, 40f);
        var handle = MakeImage(gripTrack, Color.white);
        gripHandle = handle.rectTransform;

        clicks.Add(new Click
        {
            rect = gripTrack,
            action = () => { },
            slide = t => controls.GripAngle = Mathf.Lerp(-Hd2ControlMap.GripAngleLimit, Hd2ControlMap.GripAngleLimit, t)
        });

        var down = MakeButton(page, "-", new Vector2(800f, -476f), new Vector2(52f, 52f), OffColor);
        Stretch(down.GetComponentInChildren<Text>().rectTransform, 0f);
        clicks.Add(new Click { rect = down, action = () => controls.GripAngle -= 1f });
        var up = MakeButton(page, "+", new Vector2(860f, -476f), new Vector2(52f, 52f), OffColor);
        Stretch(up.GetComponentInChildren<Text>().rectTransform, 0f);
        clicks.Add(new Click { rect = up, action = () => controls.GripAngle += 1f });

        gripValue = MakeText(page, "", 24, TextAnchor.MiddleLeft, Color.white);
        Place(gripValue.rectTransform, 924f, -476f, 200f, 52f);
        PaintGripAngle();
    }

    void PaintGripAngle()
    {
        if (gripTrack == null || controls == null)
            return;
        float angle = controls.GripAngle;
        float width = gripTrack.rect.width > 1f ? gripTrack.rect.width : 540f;
        float t = Mathf.InverseLerp(-Hd2ControlMap.GripAngleLimit, Hd2ControlMap.GripAngleLimit, angle);
        float x = t * width;
        float mid = width * 0.5f;
        Place(gripFill.rectTransform, Mathf.Min(x, mid), 0f, Mathf.Abs(x - mid), 52f);
        Place(gripHandle, Mathf.Clamp(x - 5f, 0f, width - 10f), 0f, 10f, 52f);
        gripValue.text = (angle > 0f ? "+" : "") + angle.ToString("0") + "\u00b0" + (angle > 0f ? " up" : angle < 0f ? " down" : "");
    }

    void BuildPickupToggle(RectTransform page)
    {
        var label = MakeText(page, "Pickups", 26, TextAnchor.MiddleLeft, Color.white);
        Place(label.rectTransform, 0f, -540f, 240f, 40f);

        var row = MakeImage(page, new Color(0f, 0f, 0f, 0f));
        Place(row.rectTransform, 250f, -534f, 660f, 52f);
        pickupBox = MakeImage(row.rectTransform, OffColor);
        Place(pickupBox.rectTransform, 0f, 0f, 52f, 52f);
        pickupCheck = MakeImage(pickupBox.rectTransform, Color.white);
        Place(pickupCheck.rectTransform, 14f, -14f, 24f, 24f);
        var text = MakeText(row.rectTransform, "Hold Reload (Grip) to pick up weapons", 22, TextAnchor.MiddleLeft, Color.white);
        Place(text.rectTransform, 66f, 0f, 590f, 52f);
        clicks.Add(new Click { rect = row.rectTransform, action = () => controls.PickupWithReload = !controls.PickupWithReload });
        PaintPickupToggle();
    }

    void PaintPickupToggle()
    {
        if (pickupBox == null || controls == null)
            return;
        bool on = controls.PickupWithReload;
        pickupBox.color = on ? OnColor : OffColor;
        pickupCheck.enabled = on;
    }

    void BuildPresets(RectTransform page)
    {
        var label = MakeText(page, "Defaults", 26, TextAnchor.MiddleLeft, Color.white);
        Place(label.rectTransform, 0f, -600f, 240f, 40f);
        for (int i = 0; i < Hd2ControlMap.PresetCount; i++)
        {
            int preset = i + 1;
            var button = MakeButton(page, "Default " + preset, new Vector2(250f + i * 190f, -594f), new Vector2(180f, 52f), OffColor);
            presetImages[i] = button.GetComponent<Image>();
            Stretch(button.GetComponentInChildren<Text>().rectTransform, 4f);
            clicks.Add(new Click { rect = button, action = () => ResetControls(preset) });
        }

        PaintPresets();
    }

    void PaintPresets()
    {
        for (int i = 0; i < presetImages.Length; i++)
        {
            if (presetImages[i] != null)
                presetImages[i].color = controls != null && i + 1 == controls.Preset ? OnColor : OffColor;
        }
    }

    void BuildHudChoice(RectTransform page)
    {
        var label = MakeText(page, "HUD", 26, TextAnchor.MiddleLeft, Color.white);
        Place(label.rectTransform, 0f, -424f, 240f, 40f);

        string[] names = { "Left side", "Right side", "None" };
        for (int i = 0; i < names.Length; i++)
        {
            var image = MakeImage(page, new Color(0.14f, 0.17f, 0.22f, 1f));
            Place(image.rectTransform, 250f + i * 280f, -418f, 260f, 52f);
            Stretch(MakeText(image.rectTransform, names[i], 22, TextAnchor.MiddleCenter, Color.white).rectTransform, 8f);
            hudImages[i] = image;
            int captured = i;
            clicks.Add(new Click
            {
                rect = image.rectTransform,
                action = () => ChooseHud(captured)
            });
        }

        PaintHud();
    }

    void ChooseHud(int index)
    {
        var readout = Hud();
        if (readout != null)
            readout.Placement = (Hd2Hud.Side)index;
        PaintHud();
    }

    Hd2Hud Hud()
    {
        if (hud == null)
            hud = GetComponent<Hd2Hud>();
        return hud;
    }

    void ResetControls(int preset)
    {
        controls.ApplyPreset(preset);
        controls.MicrophoneMode = Hd2ControlMap.MicMode.Toggle;
        var readout = Hud();
        if (readout != null)
            readout.Placement = Hd2Hud.Side.Left;
        HideActionList();
        PaintHud();
        PaintPresets();
    }

    void BuildActionList(RectTransform parent)
    {
        actionList = new GameObject("ActionList");
        var rect = actionList.AddComponent<RectTransform>();
        rect.SetParent(parent, false);
        var back = actionList.AddComponent<Image>();
        back.sprite = White();
        back.color = new Color(0.1f, 0.12f, 0.15f, 0.98f);
        back.raycastTarget = false;
        rect.sizeDelta = new Vector2(420f, 700f);
        actionListTitle = MakeText(rect, "Action", 24, TextAnchor.UpperLeft, Color.white);
        Place(actionListTitle.rectTransform, 16f, -12f, 388f, 36f);

        for (int i = 0; i < Hd2ControlMap.ActionCount; i++)
        {
            var action = (Hd2ControlMap.Action)i;
            var image = MakeImage(rect, new Color(0.16f, 0.2f, 0.26f, 1f));
            Place(image.rectTransform, 16f, -56f - i * 56f, 388f, 50f);
            Stretch(MakeText(image.rectTransform, Hd2ControlMap.ActionLabel(action), 22, TextAnchor.MiddleLeft, Color.white).rectTransform, 12f);
            var captured = action;
            actionClicks.Add(new Click
            {
                rect = image.rectTransform,
                action = () => Choose(captured)
            });
        }

        actionClicks.Add(new Click { rect = rect, action = () => { } });

        actionList.SetActive(false);
    }

    void ShowActionList(Hd2ControlMap.Button button)
    {
        picking = button;
        pickingValid = true;
        actionList.SetActive(true);
        actionListTitle.text = Hd2ControlMap.ButtonLabel(button);
        var rect = actionList.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(360f, -8f);
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
        PaintPresets();
        HideActionList();
    }

    void RefreshLabels()
    {
        if (controls == null)
            return;
        for (int i = 0; i < actionLabels.Length; i++)
        {
            if (actionLabels[i] != null)
                actionLabels[i].text = Hd2ControlMap.ActionLabel(controls.Binding((Hd2ControlMap.Button)i));
        }

        PaintMic();
        PaintHud();
        PaintGripAngle();
        PaintPickupToggle();
    }

    void PaintHud()
    {
        int selected = 0;
        var readout = Hud();
        if (readout != null)
            selected = (int)readout.Placement;
        for (int i = 0; i < hudImages.Length; i++)
        {
            if (hudImages[i] == null)
                continue;
            hudImages[i].color = i == selected
                ? new Color(0.2f, 0.5f, 0.78f, 1f)
                : new Color(0.14f, 0.17f, 0.22f, 1f);
        }
    }

    void PaintMic()
    {
        for (int i = 0; i < micImages.Length; i++)
        {
            if (micImages[i] == null || controls == null)
                continue;
            bool on = (int)controls.MicrophoneMode == i;
            micImages[i].color = on
                ? new Color(0.2f, 0.5f, 0.78f, 1f)
                : new Color(0.14f, 0.17f, 0.22f, 1f);
        }
    }

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
        rect.offsetMin = new Vector2(pad, 0f);
        rect.offsetMax = new Vector2(-pad, 0f);
    }

    RectTransform MakeButton(RectTransform parent, string label, Vector2 pos, Vector2 size, Color color)
    {
        var image = MakeImage(parent, color);
        Place(image.rectTransform, pos.x, pos.y, size.x, size.y);
        MakeText(image.rectTransform, label, 24, TextAnchor.MiddleCenter, Color.white);
        return image.rectTransform;
    }

    Image MakeImage(RectTransform parent, Color color)
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

    Text MakeText(RectTransform parent, string value, int size, TextAnchor anchor, Color color)
    {
        var go = new GameObject("Label");
        var rect = go.AddComponent<RectTransform>();
        rect.SetParent(parent, false);
        var text = go.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = value;
        text.fontSize = size;
        text.alignment = anchor;
        text.color = color;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
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
