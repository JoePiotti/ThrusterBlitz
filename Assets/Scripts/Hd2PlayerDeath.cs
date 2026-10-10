using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// On death the visible body drops as a ragdoll. The camera stays at that
/// spot and can still look around, but room-scale walking and locomotion do not
/// move it. After a few seconds the ragdoll is removed and the player respawns.
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
    Vector3 frozenCameraPosition;
    readonly List<Renderer> hiddenRenderers = new List<Renderer>();
    readonly List<Collider> hiddenColliders = new List<Collider>();

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

    void OnEnable()
    {
        if (health != null)
            health.Changed += OnHealthChanged;
        Application.onBeforeRender += HoldCamera;
    }

    void OnDisable()
    {
        if (health != null)
            health.Changed -= OnHealthChanged;
        Application.onBeforeRender -= HoldCamera;
    }

    void OnDestroy()
    {
        holdCamera = false;
        if (locomotion != null)
            locomotion.MovementLocked = false;
        if (ragdoll != null)
            Destroy(ragdoll);
    }

    void OnHealthChanged(Hd2Health current)
    {
        if (current == null || !current.IsDead || downed)
            return;

        StartCoroutine(DeathWatch());
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
        downed = false;
    }

    void LateUpdate()
    {
        // Tracking can update the headset pose in onBeforeRender. Stay last in
        // that list so the camera is put back after the pose is applied.
        Application.onBeforeRender -= HoldCamera;
        Application.onBeforeRender += HoldCamera;
        HoldCamera();
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
        ActivateRagdoll(ragdoll);
        IgnorePlayerColliders(ragdoll);
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

        if (ragdoll == null)
            return;

        ragdoll.SetActive(false);
        Destroy(ragdoll);
        ragdoll = null;
    }

    static void ActivateRagdoll(GameObject corpse)
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
