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
    public Vector3 robotEuler = new Vector3(-90f, 0f, 0f);
    public float resetDelay = 3f;

    static readonly string[] ragdollBones =
    {
        "Hips", "Spine", "Chest", "Head",
        "Arm.L", "Arm.L.002", "Arm.R", "Arm.R.002",
        "Thigh.L", "Shin.L", "Foot.L",
        "Thigh.R", "Shin.R", "Foot.R"
    };

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
            instance.transform.localRotation = Quaternion.Euler(robotEuler);
            instance.transform.localScale = Vector3.one;
            body = instance.transform;
        }

        if (body == null)
            return;

        var skins = body.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < skins.Length; i++)
        {
            skins[i].enabled = true;
            skins[i].updateWhenOffscreen = true;
        }

        AddHitColliders(body);
        FitHitboxes(body);

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

    public static void FitHitboxes(Transform root)
    {
        var skin = root.GetComponentInChildren<SkinnedMeshRenderer>();
        if (skin == null || skin.sharedMesh == null || !skin.sharedMesh.isReadable)
        {
            AddFallbackBoneColliders(root);
            return;
        }

        Mesh mesh = skin.sharedMesh;
        Transform[] skinBones = skin.bones;
        Vector3[] verts = mesh.vertices;
        Matrix4x4[] bindposes = mesh.bindposes;
        var bonesPerVertex = mesh.GetBonesPerVertex();
        var allWeights = mesh.GetAllBoneWeights();
        if (skinBones == null || bonesPerVertex.Length != verts.Length || bindposes.Length != skinBones.Length)
        {
            AddFallbackBoneColliders(root);
            return;
        }

        var min = new Vector3[skinBones.Length];
        var max = new Vector3[skinBones.Length];
        var count = new int[skinBones.Length];
        for (int i = 0; i < skinBones.Length; i++)
        {
            min[i] = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            max[i] = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        }

        int weightCursor = 0;
        for (int v = 0; v < verts.Length; v++)
        {
            int influences = bonesPerVertex[v];
            for (int k = 0; k < influences; k++)
            {
                var influence = allWeights[weightCursor++];
                IncludeVertex(influence.boneIndex, influence.weight, verts[v]);
            }
        }

        for (int i = 0; i < skinBones.Length; i++)
        {
            Transform bone = skinBones[i];
            if (bone == null || count[i] == 0 || bone.GetComponent<Collider>() != null)
                continue;

            Vector3 size = max[i] - min[i];
            const float pad = 0.03f;
            size += new Vector3(pad, pad, pad);
            size.x = Mathf.Max(size.x, 0.07f);
            size.y = Mathf.Max(size.y, 0.07f);
            size.z = Mathf.Max(size.z, 0.07f);

            var box = bone.gameObject.AddComponent<BoxCollider>();
            box.center = (min[i] + max[i]) * 0.5f;
            box.size = size;
        }

        void IncludeVertex(int boneIndex, float weight, Vector3 vertex)
        {
            if (weight < 0.12f || boneIndex < 0 || boneIndex >= skinBones.Length)
                return;

            Vector3 local = bindposes[boneIndex].MultiplyPoint3x4(vertex);
            count[boneIndex]++;
            min[boneIndex] = Vector3.Min(min[boneIndex], local);
            max[boneIndex] = Vector3.Max(max[boneIndex], local);
        }
    }

    static void AddFallbackBoneColliders(Transform root)
    {
        for (int i = 0; i < ragdollBones.Length; i++)
        {
            Transform bone = FindNamed(root, ragdollBones[i]);
            if (bone == null || bone.GetComponent<Collider>() != null)
                continue;

            var capsule = bone.gameObject.AddComponent<CapsuleCollider>();
            Vector3 end = bone.childCount > 0 ? bone.GetChild(0).localPosition : Vector3.up * 0.2f;
            float length = end.magnitude;
            int direction = 1;
            float ax = Mathf.Abs(end.x);
            float ay = Mathf.Abs(end.y);
            float az = Mathf.Abs(end.z);
            if (ax >= ay && ax >= az)
                direction = 0;
            else if (az >= ax && az >= ay)
                direction = 2;
            capsule.direction = direction;
            bool torso = ragdollBones[i] == "Hips" || ragdollBones[i] == "Spine" || ragdollBones[i] == "Chest";
            bool head = ragdollBones[i] == "Head";
            capsule.height = Mathf.Max(length, head ? 0.28f : 0.16f);
            capsule.radius = head ? 0.14f : torso ? 0.2f : 0.09f;
            capsule.center = length > 0.01f ? end * 0.5f : Vector3.zero;
        }
    }

    static Transform FindNamed(Transform root, string boneName)
    {
        if (root.name == boneName)
            return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindNamed(root.GetChild(i), boneName);
            if (found != null)
                return found;
        }

        return null;
    }

    void EnableRagdoll()
    {
        var transforms = body.GetComponentsInChildren<Transform>();
        foreach (var bone in transforms)
        {
            if (bone.GetComponent<Collider>() == null)
                continue;
            if (bone.GetComponent<Rigidbody>() != null)
                continue;
            var rigidbody = bone.gameObject.AddComponent<Rigidbody>();
            rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            bodies.Add(rigidbody);
        }

        var colliders = new List<Collider>(bodies.Count);
        foreach (var rigidbody in bodies)
        {
            var collider = rigidbody.GetComponent<Collider>();
            if (collider != null)
                colliders.Add(collider);
        }

        for (int i = 0; i < colliders.Count; i++)
        {
            for (int j = i + 1; j < colliders.Count; j++)
                Physics.IgnoreCollision(colliders[i], colliders[j], true);
        }

        foreach (var rigidbody in bodies)
        {
            if (rigidbody.transform == body)
                continue;
            Rigidbody parentBody = null;
            var ancestor = rigidbody.transform.parent;
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
