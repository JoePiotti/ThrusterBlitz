using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Blue pad with a red pistol inside the sphere. A hand inside the sphere
/// swaps that hand's gun for Shocks while that hand's grip is held.
/// </summary>
[ExecuteAlways]
public class Hd2ShocksPickup : MonoBehaviour
{
    public GameObject gripLeft;
    public GameObject gripRight;
    public float cooldown = 10f;

    static readonly Color pickupColor = new Color(0.22f, 0.4f, 0.95f, 1f);
    static readonly Vector3 equippedPosition = new Vector3(0f, 0.01f, 0.05f);
    const float equippedScale = 0.65f;
    const float displayScale = 1.1f;
    static readonly Vector3 displayEuler = new Vector3(-90f, 0f, 0f);

    Transform sphere;
    Transform mini;
    LineRenderer ring;
    bool ready = true;
    float cooldownLeft;
    InputAction rightGrip;
    InputAction leftGrip;

    void OnEnable()
    {
        BuildVisuals();
        RepairSavedMaterials();
        Hd2PickupGlow.Ensure(transform, pickupColor);
        SetReady(true);
        if (!Application.isPlaying)
            return;

        rightGrip = GripAction("RightHand");
        leftGrip = GripAction("LeftHand");
    }

    void OnDisable()
    {
        Dispose(ref rightGrip);
        Dispose(ref leftGrip);
    }

    static InputAction GripAction(string node)
    {
        var action = new InputAction("Hd2ShocksPickup" + node, InputActionType.Button);
        action.AddBinding("<XRController>{" + node + "}/gripPressed");
        action.Enable();
        return action;
    }

    void Update()
    {
        if (mini == null && sphere != null && (gripLeft != null || gripRight != null))
            BuildMini();

        var view = Camera.main;
        if (ring != null && view != null)
            ring.transform.rotation = Quaternion.LookRotation(ring.transform.position - view.transform.position);

        if (mini != null && ready)
            mini.Rotate(0f, 40f * Time.deltaTime, 0f, Space.World);

        if (!ready)
        {
            cooldownLeft -= Time.deltaTime;
            SetRing(1f - Mathf.Clamp01(cooldownLeft / Mathf.Max(0.01f, cooldown)));
            if (cooldownLeft <= 0f)
                SetReady(true);
            return;
        }

        if (!Application.isPlaying || sphere == null)
            return;

        var player = FindFirstObjectByType<Hd2Locomotion>();
        if (player == null)
            return;

        Transform hand = HandInside(player);
        if (hand != null)
            TryCollect(hand);
    }

    Transform HandInside(Hd2Locomotion player)
    {
        float radius = sphere.lossyScale.x * 0.5f;
        float reach = radius * radius;
        Transform best = null;
        float bestDistance = float.MaxValue;
        Consider(player.transform, "RightHand", ref best, ref bestDistance, reach);
        Consider(player.transform, "LeftHand", ref best, ref bestDistance, reach);
        return best;
    }

    void Consider(Transform root, string handName, ref Transform best, ref float bestDistance, float reachSqr)
    {
        Transform hand = FindNamed(root, handName);
        if (hand == null || !GripHeld(handName))
            return;
        if (hand.GetComponentInChildren<Hd2Shocks>(true) != null)
            return;

        float distance = (hand.position - sphere.position).sqrMagnitude;
        if (distance > reachSqr || distance >= bestDistance)
            return;

        best = hand;
        bestDistance = distance;
    }

    static Transform FindNamed(Transform root, string handName)
    {
        var transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            if (transforms[i].name == handName)
                return transforms[i];
        }

        return null;
    }

    bool GripHeld(string handName)
    {
        InputAction action = handName == "LeftHand" ? leftGrip : rightGrip;
        return action != null && action.IsPressed();
    }

    public bool BotTake(Transform hand)
    {
        if (!ready || hand == null)
            return false;
        TryCollect(hand);
        return !ready;
    }

    void TryCollect(Transform hand)
    {
        if (!Equip(hand))
            return;

        ready = false;
        cooldownLeft = cooldown;
        if (sphere != null)
            sphere.gameObject.SetActive(false);
        if (ring != null)
            ring.enabled = true;
        SetRing(0f);
    }

    bool Equip(Transform hand)
    {
        GameObject model = hand.name == "LeftHand" ? gripLeft : gripRight;
        if (model == null)
            model = gripRight != null ? gripRight : gripLeft;
        if (model == null || hand.GetComponentInChildren<Hd2Shocks>(true) != null)
            return false;

        DestroyGuns<Hd2Pistol>(hand);
        DestroyGuns<Hd2Smg>(hand);
        DestroyGuns<Hd2RocketLauncher>(hand);
        DestroyGuns<Hd2Shocks>(hand);
        DestroyGuns<Hd2LaserPistol>(hand);
        DestroyGuns<Hd2Shotgun>(hand);

        var gun = Instantiate(model, hand);
        gun.name = "Shocks";
        gun.transform.localPosition = equippedPosition;
        gun.transform.localRotation = Quaternion.identity;
        gun.transform.localScale = Vector3.one * equippedScale;
        StripColliders(gun);

        var shocks = gun.AddComponent<Hd2Shocks>();
        shocks.SetHand(hand.name == "LeftHand" ? Hd2Pistol.Hand.Left : Hd2Pistol.Hand.Right);
        Transform muzzle = gun.transform.Find("Muzzle");
        if (muzzle != null)
        {
            muzzle.localRotation = Quaternion.Euler(90f, 0f, 0f);
            shocks.muzzle = muzzle;
        }

        return true;
    }

    static void DestroyGuns<T>(Transform hand) where T : Component
    {
        T[] guns = hand.GetComponentsInChildren<T>(true);
        for (int i = 0; i < guns.Length; i++)
        {
            if (guns[i] != null)
                Destroy(guns[i].gameObject);
        }
    }

    public void RepairSavedMaterials()
    {
        EnsureMaterial(transform.Find("Pad"), pickupColor, false);
        EnsureMaterial(transform.Find("Sphere"), new Color(pickupColor.r, pickupColor.g, pickupColor.b, 0.35f), true);
        EnsureLine(transform.Find("Ring"));
    }

    static void EnsureMaterial(Transform target, Color color, bool transparent)
    {
        if (target == null)
            return;
        var renderer = target.GetComponent<Renderer>();
        if (renderer != null && renderer.sharedMaterial != null && renderer.sharedMaterial.shader != null)
            return;
        Paint(target.gameObject, color, transparent);
    }

    void EnsureLine(Transform target)
    {
        if (target == null)
            return;
        var line = target.GetComponent<LineRenderer>();
        if (line == null || (line.sharedMaterial != null && line.sharedMaterial.shader != null))
            return;
        line.material = new Material(Shader.Find("Sprites/Default"));
        line.startColor = line.endColor = pickupColor;
    }

    void SetReady(bool value)
    {
        ready = value;
        if (sphere != null)
            sphere.gameObject.SetActive(value);
        if (ring != null)
            ring.enabled = !value;
    }

    void BuildVisuals()
    {
        if (transform.Find("Pad") != null)
        {
            sphere = transform.Find("Sphere");
            mini = transform.Find("Sphere/Mini");
            ring = transform.Find("Ring") != null ? transform.Find("Ring").GetComponent<LineRenderer>() : null;
            return;
        }

        var pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pad.name = "Pad";
        pad.transform.SetParent(transform, false);
        pad.transform.localPosition = new Vector3(0f, 0.03f, 0f);
        pad.transform.localScale = new Vector3(0.7f, 0.03f, 0.7f);
        RemoveCollider(pad);
        Paint(pad, pickupColor, false);

        var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ball.name = "Sphere";
        ball.transform.SetParent(transform, false);
        ball.transform.localPosition = new Vector3(0f, 1.05f, 0f);
        ball.transform.localScale = Vector3.one * 0.42f;
        RemoveCollider(ball);
        Paint(ball, new Color(pickupColor.r, pickupColor.g, pickupColor.b, 0.35f), true);
        sphere = ball.transform;

        var ringObject = new GameObject("Ring");
        ringObject.transform.SetParent(transform, false);
        ringObject.transform.localPosition = new Vector3(0f, 1.05f, 0f);
        ring = ringObject.AddComponent<LineRenderer>();
        ring.useWorldSpace = false;
        ring.loop = false;
        ring.widthMultiplier = 0.025f;
        ring.positionCount = 0;
        ring.material = new Material(Shader.Find("Sprites/Default"));
        ring.startColor = ring.endColor = pickupColor;
        ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ring.enabled = false;
        BuildMini();
    }

    void BuildMini()
    {
        if (sphere == null || sphere.Find("Mini") != null)
            return;

        GameObject model = gripRight != null ? gripRight : gripLeft;
        if (model == null)
            return;

        var display = Instantiate(model, sphere);
        display.name = "Mini";
        display.transform.localPosition = Vector3.zero;
        display.transform.localRotation = Quaternion.Euler(displayEuler);
        display.transform.localScale = Vector3.one * displayScale;
        StripColliders(display);
        Hd2Shocks.TintRed(display);
        mini = display.transform;
    }

    void SetRing(float amount)
    {
        if (ring == null)
            return;

        const int segments = 40;
        int count = Mathf.Max(2, Mathf.CeilToInt(segments * Mathf.Clamp01(amount)));
        ring.positionCount = count;
        float radius = 0.28f;
        for (int i = 0; i < count; i++)
        {
            float t = count == 1 ? 0f : i / (float)(segments - 1);
            float angle = t * Mathf.PI * 2f - Mathf.PI * 0.5f;
            ring.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f));
        }
    }

    static void StripColliders(GameObject target)
    {
        var colliders = target.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
            RemoveCollider(colliders[i].gameObject);
    }

    static void RemoveCollider(GameObject target)
    {
        var collider = target.GetComponent<Collider>();
        if (collider == null)
            return;
        if (Application.isPlaying)
            Destroy(collider);
        else
            DestroyImmediate(collider);
    }

    static void Paint(GameObject target, Color color, bool transparent)
    {
        Shader shader = Shader.Find(transparent ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
        var material = new Material(shader);
        material.color = color;
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        if (transparent)
        {
            material.SetFloat("_Surface", 1f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.renderQueue = 3000;
        }

        target.GetComponent<Renderer>().sharedMaterial = material;
    }

    static void Dispose(ref InputAction action)
    {
        if (action == null)
            return;
        action.Disable();
        action.Dispose();
        action = null;
    }
}
