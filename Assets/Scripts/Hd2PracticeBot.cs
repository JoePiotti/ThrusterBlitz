using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A standing robot next to the practice target. Health is shown above it.
/// At 0 health the body ragdolls, waits, then stands back up at full health.
/// </summary>
public class Hd2PracticeBot : MonoBehaviour
{
    public GameObject robotPrefab;
    public float resetDelay = 3f;

    Hd2Health health;
    TextMesh label;
    Transform body;
    readonly List<Transform> bones = new List<Transform>();
    readonly List<Vector3> localPositions = new List<Vector3>();
    readonly List<Quaternion> localRotations = new List<Quaternion>();
    readonly List<Rigidbody> bodies = new List<Rigidbody>();
    readonly List<CharacterJoint> joints = new List<CharacterJoint>();
    bool ragdolling;
    bool posed;

    void Start()
    {
        var existing = transform.Find("PracticeRobot");
        if (existing != null)
        {
            body = existing;
        }
        else if (robotPrefab != null)
        {
            var instance = Instantiate(robotPrefab, transform);
            instance.name = "PracticeRobot";
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            instance.transform.localScale = Vector3.one;
            body = instance.transform;
        }

        if (body == null)
            return;

        AddHitColliders(body);

        health = gameObject.AddComponent<Hd2Health>();
        health.autoRespawn = false;
        health.Changed += OnHealthChanged;
        health.RememberSpawn();

        var labelObject = new GameObject("HealthLabel");
        labelObject.transform.SetParent(transform, false);
        labelObject.transform.localPosition = new Vector3(0f, 2.15f, 0f);
        label = labelObject.AddComponent<TextMesh>();
        label.fontSize = 64;
        label.characterSize = 0.045f;
        label.anchor = TextAnchor.MiddleCenter;
        label.alignment = TextAlignment.Center;
        label.color = Color.white;
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.text = "60";

        CachePose();
    }

    void LateUpdate()
    {
        if (label == null)
            return;

        var cam = Camera.main;
        if (cam == null)
            return;

        Vector3 toCamera = cam.transform.position - label.transform.position;
        toCamera.y = 0f;
        if (toCamera.sqrMagnitude > 0.001f)
            label.transform.rotation = Quaternion.LookRotation(-toCamera);
    }

    void OnHealthChanged(Hd2Health current)
    {
        if (label != null)
            label.text = Mathf.CeilToInt(current.Health).ToString();

        if (current.Health <= 0f && !ragdolling)
            StartCoroutine(RagdollAndReset());
    }

    IEnumerator RagdollAndReset()
    {
        ragdolling = true;
        CachePose();
        EnableRagdoll();
        yield return new WaitForSeconds(resetDelay);
        DisableRagdoll();
        RestorePose();
        health.Respawn();
        if (label != null)
            label.text = "60";
        ragdolling = false;
    }

    void AddHitColliders(Transform root)
    {
        var mesh = root.GetComponent<MeshFilter>();
        if (mesh != null && mesh.sharedMesh != null && root.GetComponent<Collider>() == null)
        {
            var box = root.gameObject.AddComponent<BoxCollider>();
            box.center = mesh.sharedMesh.bounds.center;
            box.size = mesh.sharedMesh.bounds.size;
        }

        for (int i = 0; i < root.childCount; i++)
            AddHitColliders(root.GetChild(i));
    }

    void EnableRagdoll()
    {
        var transforms = body.GetComponentsInChildren<Transform>();
        foreach (var bone in transforms)
        {
            if (bone.GetComponent<Rigidbody>() != null)
                continue;
            var rigidbody = bone.gameObject.AddComponent<Rigidbody>();
            rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            bodies.Add(rigidbody);
        }

        foreach (var rigidbody in bodies)
        {
            if (rigidbody.transform == body)
                continue;
            var parent = rigidbody.transform.parent;
            var parentBody = parent != null ? parent.GetComponent<Rigidbody>() : null;
            if (parentBody == null)
                continue;

            var joint = rigidbody.gameObject.AddComponent<CharacterJoint>();
            joint.connectedBody = parentBody;
            joint.enableProjection = true;
            joints.Add(joint);
        }
    }

    void DisableRagdoll()
    {
        for (int i = joints.Count - 1; i >= 0; i--)
        {
            if (joints[i] != null)
                Destroy(joints[i]);
        }

        for (int i = bodies.Count - 1; i >= 0; i--)
        {
            if (bodies[i] == null)
                continue;
            bodies[i].isKinematic = true;
            bodies[i].linearVelocity = Vector3.zero;
            bodies[i].angularVelocity = Vector3.zero;
            Destroy(bodies[i]);
        }

        joints.Clear();
        bodies.Clear();
    }

    void CachePose()
    {
        if (body == null || posed)
            return;

        bones.Clear();
        localPositions.Clear();
        localRotations.Clear();
        foreach (var bone in body.GetComponentsInChildren<Transform>())
        {
            bones.Add(bone);
            localPositions.Add(bone.localPosition);
            localRotations.Add(bone.localRotation);
        }

        posed = true;
    }

    void RestorePose()
    {
        for (int i = 0; i < bones.Count; i++)
        {
            if (bones[i] == null)
                continue;
            bones[i].localPosition = localPositions[i];
            bones[i].localRotation = localRotations[i];
        }
    }
}
