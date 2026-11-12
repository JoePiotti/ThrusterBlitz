using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.SceneManagement;

/// <summary>
/// Title card. Picks one of the two title tracks at random and loops it.
/// Point at a mode and pull the trigger, or click it.
/// If the player looks away from the card for a few seconds, it swings in front of them.
/// </summary>
public class Hd2TitleScreen : MonoBehaviour
{
    const float PosterDistance = 3.2f;
    const float PosterWidth = 2.6f;
    const float FaceAngle = 45f;
    const float LookAwaySeconds = 5f;
    const float SwingDegreesPerSecond = 140f;

    sealed class Choice
    {
        public Collider pad;
        public Material material;
        public string scene;
        public Color rest;
        public Color hot;
    }

    InputAction leftTrigger;
    InputAction rightTrigger;
    InputAction mouseClick;
    Transform leftHand;
    Transform rightHand;
    Choice[] choices;
    Choice aimed;
    LineRenderer laser;
    Camera view;
    Transform board;
    Vector3 boardFocus;
    float lookedAwayFor;
    bool loading;
    readonly RaycastHit[] aimHits = new RaycastHit[8];

    void Awake()
    {
        view = EnsureCamera();
        BuildPoster(view);
        PlayMusic();

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

        aimed = null;
        bool hit = TryHand(leftHand, leftTrigger);
        if (!hit)
            hit = TryHand(rightHand, rightTrigger);
        if (!hit)
        {
            HideLaser();
            TryMouse();
        }

        KeepBoardInView();
    }

    void KeepBoardInView()
    {
        if (view == null || board == null)
            return;

        Vector3 toBoard = board.TransformPoint(boardFocus) - view.transform.position;
        Vector3 flatTo = toBoard;
        flatTo.y = 0f;
        Vector3 flatForward = view.transform.forward;
        flatForward.y = 0f;
        if (flatTo.sqrMagnitude < 0.0001f || flatForward.sqrMagnitude < 0.0001f)
            return;

        float yaw = Vector3.SignedAngle(flatTo, flatForward, Vector3.up);
        if (Mathf.Abs(yaw) <= FaceAngle)
        {
            lookedAwayFor = 0f;
            return;
        }

        lookedAwayFor += Time.deltaTime;
        if (lookedAwayFor < LookAwaySeconds)
            return;

        float step = Mathf.Min(Mathf.Abs(yaw), SwingDegreesPerSecond * Time.deltaTime);
        board.RotateAround(view.transform.position, Vector3.up, Mathf.Sign(yaw) * step);
    }

    void Begin(string sceneName)
    {
        if (loading || string.IsNullOrEmpty(sceneName))
            return;
        aimed = null;
        HideLaser();
        if (sceneName == Hd2Session.JoinMarker)
        {
            loading = true;
            Hd2Session.Join();
            return;
        }

        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError("Hd2 title cannot load " + sceneName);
            return;
        }

        loading = true;
        SceneManager.LoadScene(sceneName);
    }

    bool TryHand(Transform hand, InputAction trigger)
    {
        if (hand == null)
            return false;

        Vector3 direction = hand.TransformDirection(Vector3.down);
        if (!HitChoice(hand.position, direction, 8f, out Choice choice, out Vector3 point))
            return false;

        aimed = choice;
        ShowLaser(hand.position, point);
        if (trigger != null && trigger.WasPressedThisFrame())
            Begin(choice.scene);
        return true;
    }

    void TryMouse()
    {
        if (view == null || mouseClick == null || Mouse.current == null)
            return;

        Ray ray = view.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (!HitChoice(ray.origin, ray.direction, 30f, out Choice choice, out _))
            return;

        aimed = choice;
        if (mouseClick.WasPressedThisFrame())
            Begin(choice.scene);
    }

    bool HitChoice(Vector3 origin, Vector3 direction, float distance, out Choice choice, out Vector3 point)
    {
        choice = null;
        point = default;
        if (choices == null || direction.sqrMagnitude < 0.0001f)
            return false;

        int count = Physics.RaycastNonAlloc(origin, direction, aimHits, distance, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            Choice match = Match(aimHits[i].collider);
            if (match == null || aimHits[i].distance >= best)
                continue;
            best = aimHits[i].distance;
            point = aimHits[i].point;
            choice = match;
            found = true;
        }

        return found;
    }

    Choice Match(Collider collider)
    {
        if (collider == null || choices == null)
            return null;
        for (int i = 0; i < choices.Length; i++)
        {
            if (choices[i] != null && choices[i].pad == collider)
                return choices[i];
        }

        return null;
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
        if (choices == null)
            return;

        float pulse = 0.65f + 0.35f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 2.4f));
        for (int i = 0; i < choices.Length; i++)
        {
            Choice choice = choices[i];
            if (choice == null || choice.material == null)
                continue;
            Color pad = choice == aimed ? choice.hot : choice.rest * pulse;
            pad.a = 1f;
            choice.material.color = pad;
            choice.material.SetColor("_BaseColor", pad);
        }
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

        var boardObject = new GameObject("TitleBoard");
        boardObject.transform.SetParent(transform, false);
        board = boardObject.transform;
        boardFocus = board.InverseTransformPoint(center);

        var poster = GameObject.CreatePrimitive(PrimitiveType.Quad);
        poster.name = "TitleArt";
        poster.transform.SetParent(board, false);
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

        Vector3 first = center + new Vector3(0f, -height * 0.5f - 0.26f, 0f);
        Vector3 second = first + new Vector3(0f, -0.44f, 0f);
        choices = new Choice[2];
        choices[0] = BuildChoice("SHOOTOUT", "Shootout", first, new Color(0.55f, 0.12f, 0.1f, 1f), new Color(1f, 0.42f, 0.28f, 1f));
        choices[1] = BuildChoice("JOIN TEST", Hd2Session.JoinMarker, second, new Color(0.08f, 0.32f, 0.18f, 1f), new Color(0.25f, 0.85f, 0.4f, 1f));
        BuildVersionLabel(second + new Vector3(0f, -0.32f, -0.04f));

        if (view != null && view.transform.position.sqrMagnitude < 0.01f)
            view.transform.position = new Vector3(0f, 1.6f, 0f);
    }

    Choice BuildChoice(string label, string sceneName, Vector3 center, Color rest, Color hot)
    {
        var pad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        pad.name = label;
        pad.transform.SetParent(board, false);
        pad.transform.position = center;
        pad.transform.rotation = Quaternion.identity;
        pad.transform.localScale = new Vector3(1.7f, 0.38f, 1f);
        var padCollider = pad.GetComponent<Collider>();
        if (padCollider != null)
            Destroy(padCollider);
        var box = pad.AddComponent<BoxCollider>();
        box.size = new Vector3(1f, 1f, 0.3f);
        var padRenderer = pad.GetComponent<MeshRenderer>();
        padRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        padRenderer.receiveShadows = false;
        Material material = ColorMaterial(rest);
        padRenderer.sharedMaterial = material;

        var labelObject = new GameObject(label + " Label");
        labelObject.transform.SetParent(board, false);
        labelObject.transform.position = center + new Vector3(0f, 0f, -0.04f);
        labelObject.transform.rotation = Quaternion.identity;
        var text = labelObject.AddComponent<TextMesh>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = label;
        text.fontSize = 64;
        text.characterSize = 0.04f;
        text.anchor = TextAnchor.MiddleCenter;
        text.alignment = TextAlignment.Center;
        text.color = new Color(0.93f, 0.96f, 1f, 1f);
        var textRenderer = text.GetComponent<MeshRenderer>();
        if (textRenderer != null)
        {
            textRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            textRenderer.receiveShadows = false;
            if (text.font != null)
                textRenderer.sharedMaterial = text.font.material;
        }

        return new Choice
        {
            pad = box,
            material = material,
            scene = sceneName,
            rest = rest,
            hot = hot
        };
    }

    void BuildVersionLabel(Vector3 center)
    {
        var labelObject = new GameObject("BuildVersion");
        labelObject.transform.SetParent(board, false);
        labelObject.transform.position = center;
        labelObject.transform.rotation = Quaternion.identity;
        var text = labelObject.AddComponent<TextMesh>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = VersionLabel();
        text.fontSize = 48;
        text.characterSize = 0.02f;
        text.anchor = TextAnchor.MiddleCenter;
        text.alignment = TextAlignment.Center;
        text.color = new Color(0.75f, 0.8f, 0.86f, 1f);
        var textRenderer = text.GetComponent<MeshRenderer>();
        if (textRenderer != null)
        {
            textRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            textRenderer.receiveShadows = false;
            if (text.font != null)
                textRenderer.sharedMaterial = text.font.material;
        }
    }

    static string VersionLabel()
    {
#if UNITY_EDITOR
        return Application.version + " editor";
#else
        return Application.version;
#endif
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
