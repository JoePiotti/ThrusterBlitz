using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// On death the visible body drops as a ragdoll, and the guns leave the
/// tracked hands and fall with the ragdoll's hands. The camera stays at that
/// spot and can still look around, but room-scale walking and locomotion do not
/// move it. After a few seconds the ragdoll is removed, the player respawns,
/// and both hands are back on the starting pistols with full magazines.
/// </summary>
[DefaultExecutionOrder(500)]
public class Hd2PlayerDeath : MonoBehaviour
{
    const float RespawnDelay = 5f;

    Hd2Health health;
    Hd2Body bodyDriver;
    Hd2Locomotion locomotion;
    Transform head;
    GameObject ragdoll;
    bool downed;
    bool holdCamera;
    bool quietDeath;
    Vector3 frozenCameraPosition;
    readonly List<Renderer> hiddenRenderers = new List<Renderer>();
    readonly List<Collider> hiddenColliders = new List<Collider>();
    StartingPistol rightPistol;
    StartingPistol leftPistol;
    LineRenderer killLine;
    Material killLineMaterial;
    Hd2Payload dragCart;
    Vector3 dragFromPos;
    Quaternion dragFromRot;
    Rigidbody[] dragBones;
    float dragSlack;
    float dragTravel;

    struct StartingPistol
    {
        public GameObject template;
        public Vector3 localPosition;
        public Quaternion localRotation;
        public Vector3 localScale;
    }

    void Awake()
    {
        bodyDriver = GetComponent<Hd2Body>();
        locomotion = GetComponent<Hd2Locomotion>();
        health = GetComponent<Hd2Health>();
        if (health == null)
            health = gameObject.AddComponent<Hd2Health>();
        health.autoRespawn = false;
        if (bodyDriver != null)
            head = bodyDriver.head;
    }

    void Start()
    {
        rightPistol = CaptureStartingPistol(Hand("RightHand"));
        leftPistol = CaptureStartingPistol(Hand("LeftHand"));
    }

    void OnEnable()
    {
        if (health != null)
            health.Changed += OnHealthChanged;
        Application.onBeforeRender += HoldCamera;
        RenderPipelineManager.beginCameraRendering += CullKillLine;
    }

    void OnDisable()
    {
        if (health != null)
            health.Changed -= OnHealthChanged;
        Application.onBeforeRender -= HoldCamera;
        RenderPipelineManager.beginCameraRendering -= CullKillLine;
    }

    void CullKillLine(ScriptableRenderContext context, Camera camera)
    {
        if (killLine == null)
            return;

        bool mine = camera != null && camera.transform.IsChildOf(transform);
        killLine.forceRenderingOff = !mine;
    }

    void OnDestroy()
    {
        holdCamera = false;
        if (locomotion != null)
            locomotion.MovementLocked = false;
        if (ragdoll != null)
            Destroy(ragdoll);
        if (killLine != null)
            Destroy(killLine.gameObject);
        if (killLineMaterial != null)
            Destroy(killLineMaterial);
    }

    void OnHealthChanged(Hd2Health current)
    {
        if (quietDeath || current == null || !current.IsDead || downed)
            return;

        AnnounceDeath(current.LastAttacker);
        ShowKillingBullet(current);
        StartCoroutine(DeathWatch());
    }

    void ShowKillingBullet(Hd2Health current)
    {
        bool fresh = Time.time - current.KillLineTime <= 0.05f;
        int pathCount = current.KillPathCount;
        Vector3 from = current.LastShotOrigin;
        Vector3 to = current.LastHitPoint;
        bool curved = pathCount >= 2;
        if (!fresh || (!curved && (from - to).sqrMagnitude < 0.04f))
        {
            if (killLine != null)
                killLine.enabled = false;
            return;
        }

        if (killLine == null)
        {
            var lineObject = new GameObject("KillLine");
            killLine = lineObject.AddComponent<LineRenderer>();
            killLine.startWidth = 0.03f;
            killLine.endWidth = 0.03f;
            killLine.numCapVertices = 4;
            killLine.useWorldSpace = true;
            killLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            killLine.receiveShadows = false;
            Material source = Resources.Load<Material>("Spark");
            if (source != null)
            {
                killLineMaterial = new Material(source);
                killLineMaterial.SetColor("_BaseColor", Color.red);
                killLineMaterial.SetColor("_Color", Color.red);
                killLine.material = killLineMaterial;
            }

            killLine.startColor = Color.red;
            killLine.endColor = Color.red;
            killLine.forceRenderingOff = true;
        }

        if (curved)
        {
            killLine.positionCount = pathCount;
            for (int i = 0; i < pathCount; i++)
                killLine.SetPosition(i, current.KillPathPoint(i));
        }
        else
        {
            killLine.positionCount = 2;
            killLine.SetPosition(0, from);
            killLine.SetPosition(1, to);
        }

        killLine.enabled = true;
    }

    void AnnounceDeath(Transform killer)
    {
        Hd2Hud hud = GetComponent<Hd2Hud>();
        if (hud == null)
            return;

        Hd2EnemyBot bot = killer != null ? killer.GetComponentInParent<Hd2EnemyBot>() : null;
        if (bot != null && !string.IsNullOrEmpty(bot.displayName))
            hud.ShowDeath(bot.displayName);
        else
            hud.ShowSuicide();
    }

    public void DieImmediately()
    {
        quietDeath = true;
        if (health != null && !health.IsDead)
            health.ApplyHit(health.Health, false);
        quietDeath = false;

        if (downed)
        {
            StopAllCoroutines();
            holdCamera = false;
            ClearRagdoll();
            if (locomotion != null)
                locomotion.MovementLocked = false;
            downed = false;
        }

        if (killLine != null)
            killLine.enabled = false;
        if (health != null)
            health.Respawn();
        BeginLife();
        RestoreStartingPistols();
    }

    /// <summary>
    /// Round reset. Clears a ragdoll and stands the player up at the pinned spawn.
    /// </summary>
    public void RecoverAtSpawn()
    {
        StopAllCoroutines();
        holdCamera = false;
        ClearRagdoll();
        if (locomotion != null)
        {
            locomotion.MovementLocked = false;
            locomotion.HaltTravel();
        }

        downed = false;
        quietDeath = true;
        if (killLine != null)
            killLine.enabled = false;
        if (health != null)
            health.Respawn();
        BeginLife();
        RestoreStartingPistols();
        quietDeath = false;
    }

    IEnumerator DeathWatch()
    {
        downed = true;
        if (locomotion != null)
        {
            locomotion.HaltTravel();
            locomotion.MovementLocked = true;
        }

        if (head != null)
        {
            frozenCameraPosition = head.position;
            holdCamera = true;
        }

        SpawnRagdoll();
        yield return new WaitForSeconds(RespawnDelay);

        holdCamera = false;
        ClearRagdoll();
        if (locomotion != null)
            locomotion.MovementLocked = false;
        if (health != null)
            health.Respawn();
        BeginLife();
        RestoreStartingPistols();
        downed = false;
    }

    /// <summary>
    /// The payload shield runs this player down. The ragdoll is pulled with the cart
    /// until respawn, and a stopped cart still drags the body a short way.
    /// </summary>
    public void CrushUnderPayload(Hd2Payload cart)
    {
        if (downed || cart == null || health == null || health.IsDead)
            return;

        health.LastAttacker = null;
        health.ApplyHit(health.Health, false);
        if (ragdoll == null)
            return;

        dragCart = cart;
        dragFromPos = cart.transform.position;
        dragFromRot = cart.transform.rotation;
        dragSlack = 2.2f;
        dragTravel = 0f;
        dragBones = ragdoll.GetComponentsInChildren<Rigidbody>();
        for (int i = 0; i < dragBones.Length; i++)
        {
            Rigidbody bone = dragBones[i];
            if (bone == null)
                continue;
            bone.linearVelocity = Vector3.zero;
            bone.angularVelocity = Vector3.zero;
            bone.isKinematic = true;
        }

        IgnoreColliders(ragdoll, cart.gameObject);
    }

    void LateUpdate()
    {
        // Tracking can update the headset pose in onBeforeRender. Stay last in
        // that list so the camera is put back after the pose is applied.
        Application.onBeforeRender -= HoldCamera;
        Application.onBeforeRender += HoldCamera;
        HoldCamera();
        StepDrag();
    }

    void StepDrag()
    {
        if (dragCart == null || dragBones == null)
            return;

        Transform cart = dragCart.transform;
        Quaternion rotDelta = cart.rotation * Quaternion.Inverse(dragFromRot);
        Vector3 moved = cart.position - dragFromPos;
        moved.y = 0f;
        Vector3 extra = Vector3.zero;
        if (moved.sqrMagnitude < 0.000001f && dragSlack > 0f)
        {
            float step = Mathf.Min(1.1f * Time.deltaTime, dragSlack);
            extra = dragCart.FlatForward * step;
            dragSlack -= step;
        }

        dragTravel += moved.magnitude + extra.magnitude;
        for (int i = 0; i < dragBones.Length; i++)
        {
            Rigidbody bone = dragBones[i];
            if (bone == null)
                continue;

            bone.position = cart.position + rotDelta * (bone.position - dragFromPos) + extra;
            bone.rotation = rotDelta * bone.rotation;
        }

        dragFromPos = cart.position;
        dragFromRot = cart.rotation;
        bool cartMoving = moved.sqrMagnitude >= 0.000001f;
        if (dragTravel < 2.6f && (cartMoving || dragSlack > 0f))
            return;

        for (int i = 0; i < dragBones.Length; i++)
        {
            Rigidbody bone = dragBones[i];
            if (bone != null)
                bone.isKinematic = false;
        }

        dragCart = null;
        dragBones = null;
    }

    static void IgnoreColliders(GameObject corpse, GameObject other)
    {
        Collider[] body = corpse.GetComponentsInChildren<Collider>();
        Collider[] cart = other.GetComponentsInChildren<Collider>();
        for (int i = 0; i < body.Length; i++)
        {
            if (body[i] == null)
                continue;
            for (int j = 0; j < cart.Length; j++)
            {
                if (cart[j] != null)
                    Physics.IgnoreCollision(body[i], cart[j], true);
            }
        }
    }

    void HoldCamera()
    {
        if (!holdCamera || head == null)
            return;

        Vector3 drift = head.position - frozenCameraPosition;
        if (drift.sqrMagnitude < 0.0000001f)
            return;

        transform.position -= drift;
    }

    Transform Hand(string name)
    {
        if (locomotion != null)
        {
            if (name == "RightHand" && locomotion.rightHand != null)
                return locomotion.rightHand;
            if (name == "LeftHand" && locomotion.leftHand != null)
                return locomotion.leftHand;
        }

        Transform[] transforms = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            if (transforms[i].name == name)
                return transforms[i];
        }

        return null;
    }

    StartingPistol CaptureStartingPistol(Transform hand)
    {
        var saved = new StartingPistol();
        if (hand == null)
            return saved;

        var pistol = hand.GetComponentInChildren<Hd2Pistol>(true);
        if (pistol == null)
            return saved;

        saved.localPosition = pistol.transform.localPosition;
        saved.localRotation = pistol.transform.localRotation;
        saved.localScale = pistol.transform.localScale;
        saved.template = Instantiate(pistol.gameObject, transform);
        saved.template.name = "PistolTemplate";
        saved.template.SetActive(false);
        return saved;
    }

    void BeginLife()
    {
        if (locomotion != null && bodyDriver != null)
            bodyDriver.SetArmorColor(locomotion.teamColor);

        var hud = GetComponent<Hd2Hud>();
        if (hud != null)
            hud.BeginLife();
    }

    void RestoreStartingPistols()
    {
        RestoreHand(Hand("RightHand"), rightPistol);
        RestoreHand(Hand("LeftHand"), leftPistol);
    }

    static void RestoreHand(Transform hand, StartingPistol saved)
    {
        if (hand == null || saved.template == null)
            return;

        ClearGuns(hand);
        var gun = Instantiate(saved.template, hand);
        gun.name = "Pistol";
        gun.transform.localPosition = saved.localPosition;
        gun.transform.localRotation = saved.localRotation;
        gun.transform.localScale = saved.localScale;
        gun.SetActive(true);
        var pistol = gun.GetComponent<Hd2Pistol>();
        if (pistol != null)
            pistol.AdoptHandPose();
    }

    static void ClearGuns(Transform hand)
    {
        DestroyGuns<Hd2Smg>(hand);
        DestroyGuns<Hd2RocketLauncher>(hand);
        DestroyGuns<Hd2Shocks>(hand);
        DestroyGuns<Hd2LaserPistol>(hand);
        DestroyGuns<Hd2Shotgun>(hand);
        DestroyGuns<Hd2BurstPistol>(hand);
        DestroyGuns<Hd2Pistol>(hand);
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

    void SpawnRagdoll()
    {
        Transform body = bodyDriver != null ? bodyDriver.body : null;
        if (body == null)
            return;

        ragdoll = Instantiate(body.gameObject);
        ragdoll.name = "Ragdoll";
        ragdoll.transform.SetParent(null, false);
        ragdoll.transform.SetPositionAndRotation(body.position, body.rotation);
        ragdoll.transform.localScale = body.lossyScale;

        var animators = ragdoll.GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < animators.Length; i++)
            animators[i].enabled = false;

        var skins = ragdoll.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < skins.Length; i++)
        {
            skins[i].enabled = true;
            skins[i].updateWhenOffscreen = true;
        }

        HideLiveBody(body);
        bool blast = health != null && health.KilledByBlast;
        ActivateRagdoll(ragdoll, blast ? health.BlastOrigin : Vector3.zero, blast);
        IgnorePlayerColliders(ragdoll);
        CarryGuns(ragdoll);
    }

    void CarryGuns(GameObject corpse)
    {
        CarryGun(Hand("RightHand"), FindNamed(corpse.transform, "Hand.R"));
        CarryGun(Hand("LeftHand"), FindNamed(corpse.transform, "Hand.L"));
    }

    static void CarryGun(Transform controller, Transform ragdollHand)
    {
        Transform gun = EquippedGun(controller);
        if (gun == null || ragdollHand == null)
            return;

        gun.SetParent(ragdollHand, true);
        Silence(gun.gameObject);
    }

    static Transform EquippedGun(Transform hand)
    {
        if (hand == null)
            return null;

        var pistol = hand.GetComponentInChildren<Hd2Pistol>(true);
        if (pistol != null)
            return pistol.transform;
        var shocks = hand.GetComponentInChildren<Hd2Shocks>(true);
        if (shocks != null)
            return shocks.transform;
        var burst = hand.GetComponentInChildren<Hd2BurstPistol>(true);
        if (burst != null)
            return burst.transform;
        var laser = hand.GetComponentInChildren<Hd2LaserPistol>(true);
        if (laser != null)
            return laser.transform;
        var shotgun = hand.GetComponentInChildren<Hd2Shotgun>(true);
        if (shotgun != null)
            return shotgun.transform;
        var smg = hand.GetComponentInChildren<Hd2Smg>(true);
        if (smg != null)
            return smg.transform;
        var rocket = hand.GetComponentInChildren<Hd2RocketLauncher>(true);
        if (rocket != null)
            return rocket.transform;
        return null;
    }

    static void Silence(GameObject gun)
    {
        Behaviour[] behaviours = gun.GetComponentsInChildren<Behaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
            behaviours[i].enabled = false;

        LineRenderer[] lines = gun.GetComponentsInChildren<LineRenderer>(true);
        for (int i = 0; i < lines.Length; i++)
            lines[i].enabled = false;

        ParticleSystem[] particles = gun.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < particles.Length; i++)
            particles[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        Collider[] colliders = gun.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
            colliders[i].enabled = false;
    }

    static Transform FindNamed(Transform root, string boneName)
    {
        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            if (transforms[i].name == boneName)
                return transforms[i];
        }

        return null;
    }

    void HideLiveBody(Transform body)
    {
        hiddenRenderers.Clear();
        hiddenColliders.Clear();

        var renderers = body.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (!renderers[i].enabled)
                continue;
            renderers[i].enabled = false;
            hiddenRenderers.Add(renderers[i]);
        }

        var colliders = body.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (!colliders[i].enabled)
                continue;
            colliders[i].enabled = false;
            hiddenColliders.Add(colliders[i]);
        }
    }

    void ClearRagdoll()
    {
        for (int i = 0; i < hiddenRenderers.Count; i++)
        {
            if (hiddenRenderers[i] != null)
                hiddenRenderers[i].enabled = true;
        }

        for (int i = 0; i < hiddenColliders.Count; i++)
        {
            if (hiddenColliders[i] != null)
                hiddenColliders[i].enabled = true;
        }

        hiddenRenderers.Clear();
        hiddenColliders.Clear();
        dragCart = null;
        dragBones = null;
        if (killLine != null)
            killLine.enabled = false;

        if (ragdoll == null)
            return;

        ragdoll.SetActive(false);
        Destroy(ragdoll);
        ragdoll = null;
    }

    static void ActivateRagdoll(GameObject corpse, Vector3 blastOrigin, bool blast)
    {
        var bodies = new List<Rigidbody>();
        var transforms = corpse.GetComponentsInChildren<Transform>();
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform bone = transforms[i];
            if (bone.GetComponent<Collider>() == null || bone.GetComponent<Rigidbody>() != null)
                continue;

            var rigidbody = bone.gameObject.AddComponent<Rigidbody>();
            rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            bodies.Add(rigidbody);
        }

        var colliders = new List<Collider>(bodies.Count);
        for (int i = 0; i < bodies.Count; i++)
        {
            var collider = bodies[i].GetComponent<Collider>();
            if (collider != null)
                colliders.Add(collider);
        }

        for (int i = 0; i < colliders.Count; i++)
        {
            for (int j = i + 1; j < colliders.Count; j++)
                Physics.IgnoreCollision(colliders[i], colliders[j], true);
        }

        for (int i = 0; i < bodies.Count; i++)
        {
            Rigidbody rigidbody = bodies[i];
            if (rigidbody.transform == corpse.transform)
                continue;

            Rigidbody parentBody = null;
            Transform ancestor = rigidbody.transform.parent;
            while (ancestor != null)
            {
                parentBody = ancestor.GetComponent<Rigidbody>();
                if (parentBody != null)
                    break;
                ancestor = ancestor.parent;
            }

            if (parentBody == null)
                continue;

            var joint = rigidbody.gameObject.AddComponent<CharacterJoint>();
            joint.connectedBody = parentBody;
            joint.enableProjection = true;
        }

        if (blast)
            Hd2Health.ThrowFromBlast(bodies, blastOrigin, corpse.transform.position);
    }

    void IgnorePlayerColliders(GameObject corpse)
    {
        var corpseColliders = corpse.GetComponentsInChildren<Collider>();
        var playerColliders = GetComponentsInChildren<Collider>();
        for (int i = 0; i < corpseColliders.Length; i++)
        {
            if (corpseColliders[i] == null)
                continue;
            for (int j = 0; j < playerColliders.Length; j++)
            {
                if (playerColliders[j] == null || !playerColliders[j].enabled)
                    continue;
                Physics.IgnoreCollision(corpseColliders[i], playerColliders[j], true);
            }
        }
    }
}
