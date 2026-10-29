using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.SceneManagement;

/// <summary>
/// Title card. Picks one of the two title tracks at random and loops it.
/// Point at the start button and pull the trigger, or click it.
/// The left menu button, Enter, or Space also loads Rome.
/// </summary>
public class Hd2TitleScreen : MonoBehaviour
{
    const string SceneName = "Rome";
    const float PosterDistance = 3.2f;
    const float PosterWidth = 2.6f;

    InputAction startButton;
    InputAction leftTrigger;
    InputAction rightTrigger;
    InputAction mouseClick;
    TextMesh prompt;
    Transform leftHand;
    Transform rightHand;
    Collider startPad;
    Material padMaterial;
    LineRenderer laser;
    Camera view;
    bool loading;
    bool aimed;
    readonly RaycastHit[] aimHits = new RaycastHit[8];

    void Awake()
    {
        view = EnsureCamera();
        BuildPoster(view);
        PlayMusic();

        startButton = new InputAction("Hd2TitleStart", InputActionType.Button);
        startButton.AddBinding("<XRController>{LeftHand}/menuButton");
        startButton.AddBinding("<Keyboard>/enter");
        startButton.AddBinding("<Keyboard>/space");
        startButton.Enable();
        leftTrigger = new InputAction("Hd2TitleLeft", InputActionType.Button, "<XRController>{LeftHand}/triggerPressed");
        rightTrigger = new InputAction("Hd2TitleRight", InputActionType.Button, "<XRController>{RightHand}/triggerPressed");
        mouseClick = new InputAction("Hd2TitleMouse", InputActionType.Button, "<Mouse>/leftButton");
        leftTrigger.Enable();
        rightTrigger.Enable();
        mouseClick.Enable();
        leftHand = CreateHand("LeftHand", "<XRController>{LeftHand}/devicePosition", "<XRController>{LeftHand}/deviceRotation", new Vector3(-0.22f, 1.25f, 0.35f));
        rightHand = CreateHand("RightHand", "<XRController>{RightHand}/devicePosition", "<XRController>{RightHand}/deviceRotation", new Vector3(0.22f, 1.25f, 0.35f));
    }

    void OnDestroy()
    {
        Dispose(ref startButton);
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
        if (loading)
            return;
        if (startButton != null && startButton.WasPressedThisFrame())
        {
            Begin();
            return;
        }

        aimed = false;
        bool hit = TryHand(leftHand, leftTrigger);
        if (!hit)
            hit = TryHand(rightHand, rightTrigger);
        if (!hit)
        {
            HideLaser();
            TryMouse();
        }
    }

    void Begin()
    {
        if (loading)
            return;
        loading = true;
        aimed = false;
        if (prompt != null)
            prompt.text = "";
        HideLaser();
        SceneManager.LoadScene(SceneName);
    }

    bool TryHand(Transform hand, InputAction trigger)
    {
        if (hand == null || startPad == null)
            return false;

        Vector3 direction = hand.TransformDirection(Vector3.down);
        if (!HitPad(hand.position, direction, 8f, out Vector3 point))
            return false;

        aimed = true;
        ShowLaser(hand.position, point);
        if (trigger != null && trigger.WasPressedThisFrame())
            Begin();
        return true;
    }

    void TryMouse()
    {
        if (view == null || mouseClick == null || Mouse.current == null || startPad == null)
            return;

        Ray ray = view.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (!HitPad(ray.origin, ray.direction, 30f, out _))
            return;

        aimed = true;
        if (mouseClick.WasPressedThisFrame())
            Begin();
    }

    bool HitPad(Vector3 origin, Vector3 direction, float distance, out Vector3 point)
    {
        point = default;
        if (direction.sqrMagnitude < 0.0001f)
            return false;

        int count = Physics.RaycastNonAlloc(origin, direction, aimHits, distance, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            if (aimHits[i].collider != startPad || aimHits[i].distance >= best)
                continue;
            best = aimHits[i].distance;
            point = aimHits[i].point;
            found = true;
        }

        return found;
    }

    void ShowLaser(Vector3 from, Vector3 to)
    {
        if (laser == null)
        {
            var lineObject = new GameObject("TitleLaser");
            lineObject.transform.SetParent(transform, false);
            laser = lineObject.AddComponent<LineRenderer>();
            laser.positionCount = 2;
            laser.widthMultiplier = 0.004f;
            laser.useWorldSpace = true;
            laser.numCapVertices = 4;
            laser.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            laser.receiveShadows = false;
            laser.material = ColorMaterial(new Color(0.45f, 0.85f, 1f, 1f));
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

    void LateUpdate()
    {
        float pulse = 0.65f + 0.35f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 2.4f));
        if (prompt != null)
            prompt.color = new Color(0.45f * pulse, 0.92f * pulse, 1f, 1f);
        if (padMaterial == null)
            return;

        Color pad = aimed
            ? new Color(0.28f, 0.62f, 1f, 1f)
            : new Color(0.08f * pulse, 0.28f * pulse, 0.72f, 1f);
        padMaterial.color = pad;
        padMaterial.SetColor("_BaseColor", pad);
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
        tipRenderer.receiveShadows = false;
        tipRenderer.sharedMaterial = ColorMaterial(new Color(0.45f, 0.85f, 1f, 1f));
        return hand.transform;
    }

    static Camera EnsureCamera()
    {
        Camera view = Camera.main;
        if (view == null)
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            view = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
        }

        view.clearFlags = CameraClearFlags.SolidColor;
        view.backgroundColor = new Color(0.015f, 0.03f, 0.07f, 1f);
        view.nearClipPlane = 0.05f;
        view.farClipPlane = 40f;
        if (view.GetComponent<AudioListener>() == null)
            view.gameObject.AddComponent<AudioListener>();
        if (view.GetComponent<TrackedPoseDriver>() == null)
        {
            var driver = view.gameObject.AddComponent<TrackedPoseDriver>();
            var position = new InputAction("TitleEyePos", InputActionType.Value, "<XRHMD>/centerEyePosition", expectedControlType: "Vector3");
            var rotation = new InputAction("TitleEyeRot", InputActionType.Value, "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion");
            driver.positionInput = new InputActionProperty(position);
            driver.rotationInput = new InputActionProperty(rotation);
            driver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            driver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
        }

        return view;
    }

    void BuildPoster(Camera view)
    {
        Texture2D art = Resources.Load<Texture2D>("Title/ThrusterBlitz");
        float aspect = art != null && art.height > 0 ? (float)art.width / art.height : 1.77f;
        float height = PosterWidth / aspect;
        Vector3 center = new Vector3(0f, 1.5f, PosterDistance);

        var poster = GameObject.CreatePrimitive(PrimitiveType.Quad);
        poster.name = "TitleArt";
        poster.transform.SetParent(transform, false);
        poster.transform.position = center;
        poster.transform.rotation = Quaternion.identity;
        poster.transform.localScale = new Vector3(PosterWidth, height, 1f);
        var collider = poster.GetComponent<Collider>();
        if (collider != null)
            Destroy(collider);

        var renderer = poster.GetComponent<MeshRenderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sharedMaterial = ArtMaterial(art);

        Vector3 buttonCenter = center + new Vector3(0f, -height * 0.5f - 0.36f, 0f);
        var pad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        pad.name = "StartButton";
        pad.transform.SetParent(transform, false);
        pad.transform.position = buttonCenter;
        pad.transform.rotation = Quaternion.identity;
        pad.transform.localScale = new Vector3(1.45f, 0.4f, 1f);
        var padCollider = pad.GetComponent<Collider>();
        if (padCollider != null)
            Destroy(padCollider);
        var box = pad.AddComponent<BoxCollider>();
        box.size = new Vector3(1f, 1f, 0.3f);
        startPad = box;
        var padRenderer = pad.GetComponent<MeshRenderer>();
        padRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        padRenderer.receiveShadows = false;
        padMaterial = ColorMaterial(new Color(0.08f, 0.28f, 0.72f, 1f));
        padRenderer.sharedMaterial = padMaterial;

        var promptObject = new GameObject("PressStart");
        promptObject.transform.SetParent(transform, false);
        promptObject.transform.position = buttonCenter + new Vector3(0f, 0f, -0.04f);
        promptObject.transform.rotation = Quaternion.identity;
        prompt = promptObject.AddComponent<TextMesh>();
        prompt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        prompt.text = "PRESS START";
        prompt.fontSize = 72;
        prompt.characterSize = 0.055f;
        prompt.anchor = TextAnchor.MiddleCenter;
        prompt.alignment = TextAlignment.Center;
        prompt.color = new Color(0.45f, 0.92f, 1f, 1f);
        var textRenderer = prompt.GetComponent<MeshRenderer>();
        if (textRenderer != null)
        {
            textRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            textRenderer.receiveShadows = false;
            if (prompt.font != null)
                textRenderer.sharedMaterial = prompt.font.material;
        }

        if (view != null && view.transform.position.sqrMagnitude < 0.01f)
            view.transform.position = new Vector3(0f, 1.6f, 0f);
    }

    static Material ArtMaterial(Texture2D art)
    {
        Material source = Resources.Load<Material>("Title/TitleArt");
        Material material = source != null
            ? new Material(source)
            : new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        if (art != null)
        {
            material.SetTexture("_BaseMap", art);
            material.SetTexture("_MainTex", art);
            material.mainTexture = art;
        }

        material.color = Color.white;
        material.SetColor("_BaseColor", Color.white);
        return material;
    }

    static Material ColorMaterial(Color color)
    {
        Material source = Resources.Load<Material>("Title/TitleArt");
        Material material = source != null
            ? new Material(source)
            : new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        material.mainTexture = null;
        material.color = color;
        material.SetColor("_BaseColor", color);
        return material;
    }

    void PlayMusic()
    {
        AudioClip first = Resources.Load<AudioClip>("Audio/TitleV1");
        AudioClip second = Resources.Load<AudioClip>("Audio/TitleV2");
        AudioClip clip = first;
        if (first != null && second != null)
            clip = Random.value < 0.5f ? first : second;
        else if (second != null)
            clip = second;
        if (clip == null)
            return;

        var source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = true;
        source.spatialBlend = 0f;
        source.dopplerLevel = 0f;
        source.volume = 0.85f;
        source.clip = clip;
        source.Play();
    }
}
