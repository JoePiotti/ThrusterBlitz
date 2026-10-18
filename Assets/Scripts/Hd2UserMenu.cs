using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.XR;

/// <summary>
/// World-space menu for this player only. The left controller menu button opens and closes it.
/// More tabs can be added beside Controls. Controls rebinds the buttons and the microphone mode.
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
    InputAction menuButton;
    InputAction editorMenu;
    Camera ownerCamera;
    Canvas canvas;
    RectTransform panel;
    GameObject controlsPage;
    GameObject lookPage;
    Image controlsTabImage;
    Image lookTabImage;
    readonly Image[] themeImages = new Image[3];
    GameObject actionList;
    Text actionListTitle;
    readonly List<Click> clicks = new List<Click>();
    readonly List<Click> actionClicks = new List<Click>();
    readonly Text[] actionLabels = new Text[Hd2ControlMap.ButtonCount];
    readonly Image[] micImages = new Image[3];
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
    }

    void Awake()
    {
        controls = GetComponent<Hd2ControlMap>();
        if (controls == null)
            controls = gameObject.AddComponent<Hd2ControlMap>();
        controls.Changed += RefreshLabels;
        Build();
        SetOpen(false);
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
    }

    void Update()
    {
        bool toggle = menuButton != null && menuButton.WasPressedThisFrame();
        if (!toggle && EditorFallback && editorMenu != null)
            toggle = editorMenu.WasPressedThisFrame();
        if (toggle)
            SetOpen(!open);

        if (!open)
            return;

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
            HideActionList();
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
        if (click == null || !controls.ButtonPressed(trigger))
            return click != null;

        controls.Consume(trigger);
        click.action();
        return true;
    }

    Click Pick(Vector2 screen, Camera cam)
    {
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

        var tab = MakeButton(panel, "Controls", new Vector2(16f, -16f), new Vector2(180f, 48f), new Color(0.2f, 0.5f, 0.78f, 1f));
        controlsTabImage = tab.GetComponent<Image>();
        Stretch(tab.GetComponentInChildren<Text>().rectTransform, 8f);
        clicks.Add(new Click { rect = tab, action = ShowControls });

        var lookTab = MakeButton(panel, "Look", new Vector2(208f, -16f), new Vector2(140f, 48f), new Color(0.14f, 0.17f, 0.22f, 1f));
        lookTabImage = lookTab.GetComponent<Image>();
        Stretch(lookTab.GetComponentInChildren<Text>().rectTransform, 8f);
        clicks.Add(new Click { rect = lookTab, action = ShowLook });

        var title = MakeText(panel, "Controls", 34, TextAnchor.UpperLeft, Color.white);
        var titleRect = title.rectTransform;
        titleRect.anchorMin = titleRect.anchorMax = new Vector2(0f, 1f);
        titleRect.pivot = new Vector2(0f, 1f);
        titleRect.anchoredPosition = new Vector2(364f, -18f);
        titleRect.sizeDelta = new Vector2(500f, 46f);

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
        BuildLook();
        RefreshLabels();
    }

    void ShowControls()
    {
        HideActionList();
        if (controlsPage != null)
            controlsPage.SetActive(true);
        if (lookPage != null)
            lookPage.SetActive(false);
        PaintTabs(true);
    }

    void ShowLook()
    {
        HideActionList();
        if (controlsPage != null)
            controlsPage.SetActive(false);
        if (lookPage != null)
            lookPage.SetActive(true);
        PaintTabs(false);
        PaintThemes();
    }

    void BuildLook()
    {
        lookPage = new GameObject("LookPage");
        var page = lookPage.AddComponent<RectTransform>();
        page.SetParent(panel, false);
        page.anchorMin = Vector2.zero;
        page.anchorMax = Vector2.one;
        page.offsetMin = new Vector2(24f, 24f);
        page.offsetMax = new Vector2(-24f, -84f);

        var intro = MakeText(page, "Map skin", 28, TextAnchor.MiddleLeft, Color.white);
        Place(intro.rectTransform, 0f, -8f, 700f, 40f);

        string[] names = { "Nature", "Sci-Fi", "Quarry" };
        string[] notes =
        {
            "Moss, timber, and stone.",
            "Hull plating and cyan light.",
            "Limestone and rusted steel."
        };
        for (int i = 0; i < names.Length; i++)
        {
            float y = -64f - i * 120f;
            var image = MakeImage(page, new Color(0.14f, 0.17f, 0.22f, 1f));
            Place(image.rectTransform, 0f, y, 1080f, 104f);
            themeImages[i] = image;
            var name = MakeText(image.rectTransform, names[i], 28, TextAnchor.UpperLeft, Color.white);
            Place(name.rectTransform, 24f, -16f, 500f, 36f);
            var note = MakeText(image.rectTransform, notes[i], 22, TextAnchor.UpperLeft, new Color(0.75f, 0.82f, 0.88f, 1f));
            Place(note.rectTransform, 24f, -54f, 900f, 32f);
            int captured = i;
            clicks.Add(new Click { rect = image.rectTransform, action = () => ChooseTheme(captured) });
        }

        var colorLabel = MakeText(page, "Player color", 28, TextAnchor.MiddleLeft, Color.white);
        Place(colorLabel.rectTransform, 0f, -440f, 700f, 40f);

        AddColorChoice(page, "Red", new Color(0.82f, 0.1f, 0.08f, 1f), 0f);
        AddColorChoice(page, "Blue", new Color(0.16f, 0.38f, 0.95f, 1f), 220f);

        lookPage.SetActive(false);
    }

    void AddColorChoice(RectTransform page, string label, Color color, float x)
    {
        var image = MakeImage(page, color);
        Place(image.rectTransform, x, -488f, 200f, 72f);
        Stretch(MakeText(image.rectTransform, label, 24, TextAnchor.MiddleCenter, Color.white).rectTransform, 8f);
        clicks.Add(new Click { rect = image.rectTransform, action = () => TintPlayer(color) });
    }

    void TintPlayer(Color color)
    {
        var avatar = GetComponent<Hd2Body>();
        if (avatar != null)
            avatar.SetArmorColor(color);
    }

    void ChooseTheme(int index)
    {
        var theme = Hd2MapTheme.Find();
        if (theme != null)
            theme.Apply(index);
        PaintThemes();
    }

    void PaintTabs(bool controls)
    {
        if (controlsTabImage != null)
            controlsTabImage.color = controls
                ? new Color(0.2f, 0.5f, 0.78f, 1f)
                : new Color(0.14f, 0.17f, 0.22f, 1f);
        if (lookTabImage != null)
            lookTabImage.color = controls
                ? new Color(0.14f, 0.17f, 0.22f, 1f)
                : new Color(0.2f, 0.5f, 0.78f, 1f);
    }

    void PaintThemes()
    {
        int selected = 0;
        var theme = Hd2MapTheme.Find();
        if (theme != null)
            selected = theme.current;
        for (int i = 0; i < themeImages.Length; i++)
        {
            if (themeImages[i] == null)
                continue;
            themeImages[i].color = i == selected
                ? new Color(0.2f, 0.5f, 0.78f, 1f)
                : new Color(0.14f, 0.17f, 0.22f, 1f);
        }
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
        Place(label.rectTransform, 0f, -390f, 240f, 40f);

        for (int i = 0; i < 3; i++)
        {
            var mode = (Hd2ControlMap.MicMode)i;
            var image = MakeImage(page, new Color(0.14f, 0.17f, 0.22f, 1f));
            Place(image.rectTransform, 250f + i * 280f, -384f, 260f, 52f);
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

        var hint = MakeText(page, "Press the left menu button to close", 22, TextAnchor.MiddleLeft, new Color(0.75f, 0.8f, 0.85f, 1f));
        Place(hint.rectTransform, 0f, -470f, 760f, 36f);

        var reset = MakeImage(page, new Color(0.2f, 0.5f, 0.78f, 1f));
        Place(reset.rectTransform, 0f, -520f, 200f, 52f);
        Stretch(MakeText(reset.rectTransform, "Reset", 22, TextAnchor.MiddleCenter, Color.white).rectTransform, 8f);
        clicks.Add(new Click { rect = reset.rectTransform, action = ResetControls });
    }

    void ResetControls()
    {
        controls.SetDefaults();
        controls.MicrophoneMode = Hd2ControlMap.MicMode.Toggle;
        HideActionList();
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
