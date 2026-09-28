using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

/// <summary>
/// One pistol on one hand. A quick trigger release fires a normal shot.
/// Holding the trigger for chargeHoldTime, then releasing, fires a charged shot.
/// Grip starts a timed reload on that gun only. An empty magazine starts the same reload.
/// </summary>
public class Hd2Pistol : MonoBehaviour
{
    public enum Hand
    {
        Right = 0,
        Left = 1
    }

    [Header("Hand")]
    public Hand hand = Hand.Right;

    [Header("Magazine")]
    public int magazineSize = 16;
    public float shotsPerSecond = 8f;
    public float reloadDuration = 2f;

    [Header("Shot")]
    public float shotSpeed = 45f;
    public float shotLifetime = 2f;
    public float shotRadius = 0.03f;
    public float normalDamage = 10f;
    public Color shotColor = new Color(1f, 0.85f, 0.25f, 1f);

    [Header("Charged shot")]
    public float chargeHoldTime = 1f;
    public float chargedRadius = 0.045f;
    public float chargedSpeedMultiplier = 0.7f;
    public int chargedAmmoCost = 3;
    public int chargedBounces = 6;

    [Header("Kick")]
    public float kickDegrees = 4f;
    public float kickReturnSpeed = 18f;

    [Tooltip("Bore tip. Local euler (90, 0, 0) so forward is the pistol's -Y, out the barrel away from the player.")]
    public Transform muzzle;

    int rounds;
    float nextFireTime;
    float triggerPressedAt = -1f;
    bool reloading;
    float reloadEndsAt;
    float kick;
    Quaternion restLocalRotation;

    InputAction triggerAction;
    InputAction reloadAction;
    InputAction editorFireAction;
    InputAction editorReloadAction;

    static readonly System.Collections.Generic.List<XRDisplaySubsystem> displays =
        new System.Collections.Generic.List<XRDisplaySubsystem>();

    public int Rounds => rounds;
    public bool IsReloading => reloading;

    void Awake()
    {
        if (muzzle == null)
        {
            var found = transform.Find("Muzzle");
            if (found != null)
                muzzle = found;
        }

        restLocalRotation = transform.localRotation;
        rounds = magazineSize;
    }

    void OnEnable()
    {
        string node = hand == Hand.Left ? "LeftHand" : "RightHand";

        triggerAction = new InputAction("Hd2Fire", InputActionType.Button);
        triggerAction.AddBinding("<XRController>{" + node + "}/triggerPressed");
        triggerAction.Enable();

        reloadAction = new InputAction("Hd2Reload", InputActionType.Button);
        reloadAction.AddBinding("<XRController>{" + node + "}/gripPressed");
        reloadAction.Enable();

        // Editor mouse/keyboard stays on the right gun so the two magazines stay separate.
        if (hand == Hand.Right)
        {
            editorFireAction = new InputAction("Hd2FireEditor", InputActionType.Button);
            editorFireAction.AddBinding("<Mouse>/rightButton");
            editorFireAction.Enable();

            editorReloadAction = new InputAction("Hd2ReloadEditor", InputActionType.Button);
            editorReloadAction.AddBinding("<Keyboard>/r");
            editorReloadAction.Enable();
        }
    }

    void OnDisable()
    {
        Dispose(ref triggerAction);
        Dispose(ref reloadAction);
        Dispose(ref editorFireAction);
        Dispose(ref editorReloadAction);
    }

    void Update()
    {
        // No ammo HUD. While reloading, this gun simply will not fire.
        if (reloading && Time.time >= reloadEndsAt)
            FinishReload();

        bool editorReload = EditorFallback && editorReloadAction != null && editorReloadAction.WasPressedThisFrame();
        if (reloadAction.WasPressedThisFrame() || editorReload)
            Reload();

        bool pressed = triggerAction.WasPressedThisFrame();
        bool released = triggerAction.WasReleasedThisFrame();
        if (EditorFallback && editorFireAction != null)
        {
            pressed = pressed || editorFireAction.WasPressedThisFrame();
            released = released || editorFireAction.WasReleasedThisFrame();
        }

        if (pressed)
            triggerPressedAt = Time.time;

        if (released)
        {
            float held = triggerPressedAt >= 0f ? Time.time - triggerPressedAt : 0f;
            triggerPressedAt = -1f;
            TryFire(held >= chargeHoldTime);
        }

        if (kick > 0f)
        {
            kick = Mathf.MoveTowards(kick, 0f, kickReturnSpeed * Time.deltaTime);
            transform.localRotation = restLocalRotation * Quaternion.Euler(-kick, 0f, 0f);
        }
    }

    public void Reload()
    {
        // A reload already in progress is left alone, including one started by an empty magazine.
        if (reloading || rounds >= magazineSize)
            return;

        reloading = true;
        reloadEndsAt = Time.time + Mathf.Max(0f, reloadDuration);
    }

    void FinishReload()
    {
        rounds = magazineSize;
        reloading = false;
    }

    void TryFire(bool charged)
    {
        if (reloading || rounds <= 0)
            return;
        if (Time.time < nextFireTime)
            return;

        int cost = charged ? Mathf.Min(Mathf.Max(1, chargedAmmoCost), rounds) : 1;
        rounds -= cost;
        nextFireTime = Time.time + 1f / Mathf.Max(0.01f, shotsPerSecond);
        kick = charged ? kickDegrees * 1.5f : kickDegrees;
        SpawnShot(charged);

        if (rounds <= 0)
            Reload();
    }

    void SpawnShot(bool charged)
    {
        Transform origin = muzzle != null ? muzzle : transform;
        Vector3 direction = origin.forward;
        float radius = charged ? chargedRadius : shotRadius;
        float speed = shotSpeed * (charged ? chargedSpeedMultiplier : 1f);
        var shot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        shot.name = charged ? "ChargedShot" : "Shot";
        shot.transform.SetParent(null, true);
        shot.transform.position = origin.position + direction * 0.08f;
        shot.transform.localScale = Vector3.one * (radius * 2f);

        var collider = shot.GetComponent<Collider>();
        if (collider != null)
        {
            collider.enabled = false;
            Destroy(collider);
        }

        var renderer = shot.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            material.color = shotColor;
            renderer.sharedMaterial = material;
        }

        var projectile = shot.AddComponent<Hd2Shot>();
        projectile.velocity = direction * speed;
        projectile.lifetime = shotLifetime;
        projectile.owner = transform.root;
        projectile.damage = normalDamage * (charged ? chargedAmmoCost : 1);
        projectile.bouncesRemaining = charged ? chargedBounces : 0;
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

    static void Dispose(ref InputAction action)
    {
        if (action == null)
            return;
        action.Disable();
        action.Dispose();
        action = null;
    }
}
