using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

/// <summary>
/// One-handed automatic SMG. Numbers are inspector parameters.
/// Hold the trigger to fire. Grip reloads, and an empty magazine reloads on its own.
/// </summary>
public class Hd2Smg : MonoBehaviour
{
    [Header("Hand")]
    public Hd2Pistol.Hand hand = Hd2Pistol.Hand.Right;

    [Header("Magazine")]
    public int magazineSize = 30;
    public float shotsPerSecond = 12f;
    public float reloadDuration = 1.5f;

    [Header("Shot")]
    public float bulletSpeed = 80f;
    public float shotLifetime = 1.2f;
    public float shotRadius = 0.03f;
    public float bodyDamage = 10f;
    public float headDamage = 20f;
    public Color shotColor = new Color(1f, 0.82f, 0.45f, 1f);

    [Header("Recoil")]
    [Tooltip("How many rounds at the start of a burst climb the barrel.")]
    public int recoilClimbRounds = 10;
    [Tooltip("Degrees added to the climb for each of those rounds.")]
    public float recoilClimbPerRound = 0.45f;
    [Tooltip("Side-to-side wobble while firing, in degrees.")]
    public float vibrationDegrees = 1f;
    public float kickReturnSpeed = 18f;

    public Transform muzzle;

    int rounds;
    int climbedRounds;
    float nextFireTime;
    bool reloading;
    float reloadEndsAt;
    float climb;
    bool firing;
    Quaternion restLocalRotation;

    InputAction triggerAction;
    InputAction reloadAction;
    InputAction editorFireAction;
    InputAction editorReloadAction;

    static readonly System.Collections.Generic.List<XRDisplaySubsystem> displays =
        new System.Collections.Generic.List<XRDisplaySubsystem>();

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
        BindInputs();
    }

    public void SetHand(Hd2Pistol.Hand value)
    {
        hand = value;
        if (isActiveAndEnabled)
            BindInputs();
    }

    void BindInputs()
    {
        Dispose(ref triggerAction);
        Dispose(ref reloadAction);
        Dispose(ref editorFireAction);
        Dispose(ref editorReloadAction);

        string node = hand == Hd2Pistol.Hand.Left ? "LeftHand" : "RightHand";

        triggerAction = new InputAction("Hd2SmgFire" + node, InputActionType.Button);
        triggerAction.AddBinding("<XRController>{" + node + "}/triggerPressed");
        triggerAction.Enable();

        reloadAction = new InputAction("Hd2SmgReload" + node, InputActionType.Button);
        reloadAction.AddBinding("<XRController>{" + node + "}/gripPressed");
        reloadAction.Enable();

        if (hand == Hd2Pistol.Hand.Right)
        {
            editorFireAction = new InputAction("Hd2SmgFireEditor", InputActionType.Button);
            editorFireAction.AddBinding("<Mouse>/rightButton");
            editorFireAction.Enable();

            editorReloadAction = new InputAction("Hd2SmgReloadEditor", InputActionType.Button);
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
        if (reloading && Time.time >= reloadEndsAt)
            FinishReload();

        bool editorReload = EditorFallback && editorReloadAction != null && editorReloadAction.WasPressedThisFrame();
        if (reloadAction.WasPressedThisFrame() || editorReload)
            Reload();

        bool held = triggerAction.IsPressed();
        if (EditorFallback && editorFireAction != null)
            held = held || editorFireAction.IsPressed();

        firing = held && !reloading && rounds > 0;
        if (firing)
            TryFire();
        else
        {
            climbedRounds = 0;
            climb = Mathf.MoveTowards(climb, 0f, kickReturnSpeed * Time.deltaTime);
        }

        float wobble = firing ? Mathf.Sin(Time.time * 36f) * vibrationDegrees : 0f;
        transform.localRotation = restLocalRotation * Quaternion.Euler(-climb, wobble, 0f);
    }

    public void Reload()
    {
        if (reloading || rounds >= magazineSize)
            return;

        reloading = true;
        reloadEndsAt = Time.time + Mathf.Max(0f, reloadDuration);
        firing = false;
    }

    void FinishReload()
    {
        rounds = magazineSize;
        reloading = false;
        climbedRounds = 0;
        climb = 0f;
    }

    void TryFire()
    {
        if (Time.time < nextFireTime)
            return;

        rounds--;
        nextFireTime = Time.time + 1f / Mathf.Max(0.01f, shotsPerSecond);
        if (climbedRounds < recoilClimbRounds)
        {
            climbedRounds++;
            climb += recoilClimbPerRound;
        }

        SpawnShot();
        if (rounds <= 0)
            Reload();
    }

    void SpawnShot()
    {
        Transform origin = muzzle != null ? muzzle : transform;
        Vector3 direction = origin.forward;
        var shot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        shot.name = "SmgShot";
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
        projectile.velocity = direction * bulletSpeed;
        projectile.lifetime = shotLifetime;
        projectile.owner = transform.root;
        projectile.damage = bodyDamage;
        projectile.headMultiplier = bodyDamage > 0.01f ? headDamage / bodyDamage : 1f;
        projectile.bouncesRemaining = 0;
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
