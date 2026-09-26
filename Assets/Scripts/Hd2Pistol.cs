using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

/// <summary>
/// One blockout pistol on one hand. Trigger release fires one shot.
/// Grip starts a timed reload on that gun only. An empty magazine starts the same reload.
/// Charge and bounce shots are not in this slice.
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
    public float shotRadius = 0.04f;
    public Color shotColor = new Color(1f, 0.85f, 0.25f, 1f);

    [Header("Kick")]
    public float kickDegrees = 4f;
    public float kickReturnSpeed = 18f;

    [Tooltip("Bore tip. Local euler (90, 0, 0) so forward is the pistol's -Y, out the barrel away from the player.")]
    public Transform muzzle;

    int rounds;
    float nextFireTime;
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

        bool fire = triggerAction.WasReleasedThisFrame();
        if (EditorFallback && editorFireAction != null)
            fire = fire || editorFireAction.WasReleasedThisFrame();

        if (fire)
            TryFire();

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

    void TryFire()
    {
        if (reloading || rounds <= 0)
            return;
        if (Time.time < nextFireTime)
            return;

        rounds--;
        nextFireTime = Time.time + 1f / Mathf.Max(0.01f, shotsPerSecond);
        kick = kickDegrees;
        SpawnShot();

        if (rounds <= 0)
            Reload();
    }

    void SpawnShot()
    {
        Transform origin = muzzle != null ? muzzle : transform;
        Vector3 direction = origin.forward;
        var shot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        shot.name = "Shot";
        shot.transform.SetParent(null, true);
        shot.transform.position = origin.position + direction * 0.08f;
        shot.transform.localScale = Vector3.one * (shotRadius * 2f);

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
        projectile.velocity = direction * shotSpeed;
        projectile.lifetime = shotLifetime;
        projectile.owner = transform.root;
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
