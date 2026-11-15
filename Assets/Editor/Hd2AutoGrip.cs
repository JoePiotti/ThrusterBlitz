using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor-only first pass at a grip, used by the Grip Baker's Auto Grip button.
/// Keeps the hand where the Wrist marker puts it, straightens the fingers, then:
/// - the index finger bends until its tip reaches the trigger,
/// - the thumb and the other three fingers curl until each segment touches the gun.
/// The direction each finger curls toward comes from the GripFingers markers you placed.
/// Temporary mesh colliders are added to the gun copy for the contact checks and removed after.
/// </summary>
public static class Hd2AutoGrip
{
    const float StepDegrees = 2f;
    const int MaxSteps = 60;

    // Max bend per joint: base, middle, tip.
    static readonly float[] MaxBend = { 95f, 105f, 85f };

    public static string Apply(Hd2GripHand grip, Transform gun, GameObject robotModel)
    {
        if (grip == null || grip.hand == null || gun == null)
            return "Nothing to grip.";

        var colliders = new List<MeshCollider>();
        var gunColliders = new HashSet<Collider>();
        foreach (MeshFilter filter in gun.GetComponentsInChildren<MeshFilter>(true))
        {
            var renderer = filter.GetComponent<MeshRenderer>();
            if (filter.sharedMesh == null || renderer == null || !renderer.enabled || !filter.gameObject.activeInHierarchy)
                continue;
            var collider = filter.gameObject.AddComponent<MeshCollider>();
            collider.hideFlags = HideFlags.DontSave;
            collider.sharedMesh = filter.sharedMesh;
            colliders.Add(collider);
            gunColliders.Add(collider);
        }

        if (colliders.Count == 0)
            return "The gun has no meshes to grip.";

        Physics.SyncTransforms();
        PhysicsScene physics = gun.gameObject.scene.GetPhysicsScene();
        var report = new System.Text.StringBuilder();
        try
        {
            ResetFingers(grip, robotModel);
            Transform trigger = Hd2GripFingers.Find(gun, "Trigger");
            Vector3? triggerPoint = TriggerPoint(trigger);

            for (int finger = 0; finger < Hd2GripFingers.Names.Length; finger++)
            {
                string name = Hd2GripFingers.Names[finger];
                int index = finger * 3;
                Transform[] joints = { grip.Bone(index), grip.Bone(index + 1), grip.Bone(index + 2) };
                if (joints[0] == null || joints[1] == null || joints[2] == null)
                {
                    report.AppendLine(name + ": missing bones");
                    continue;
                }

                Vector3 hinge = Hinge(grip, index, joints, gun);
                if (hinge.sqrMagnitude < 0.0001f)
                {
                    report.AppendLine(name + ": no curl direction");
                    continue;
                }

                float radius = Radius(joints);
                if (name == "Index" && triggerPoint.HasValue)
                {
                    float miss = ReachTrigger(joints, hinge, triggerPoint.Value);
                    report.AppendLine($"Index: tip {miss * 1000f:0} mm from the trigger");
                }
                else
                {
                    int touching = CurlUntilContact(joints, hinge, radius, physics, gunColliders);
                    report.AppendLine($"{name}: {touching} of 3 segments touching");
                }
            }
        }
        finally
        {
            for (int i = 0; i < colliders.Count; i++)
                Object.DestroyImmediate(colliders[i]);
        }

        return report.ToString().TrimEnd();
    }

    // Straight fingers to start: the rest rotations from the robot model asset.
    static void ResetFingers(Hd2GripHand grip, GameObject robotModel)
    {
        if (robotModel == null)
            return;
        for (int i = 0; i < Hd2GripFingers.BoneCount; i++)
        {
            Transform bone = grip.Bone(i);
            if (bone == null)
                continue;
            Transform rest = Hd2GripFingers.Find(robotModel.transform, bone.name);
            if (rest != null)
                bone.localRotation = rest.localRotation;
        }
    }

    static Vector3? TriggerPoint(Transform trigger)
    {
        if (trigger == null)
            return null;
        var renderer = trigger.GetComponent<Renderer>();
        return renderer != null ? renderer.bounds.center : trigger.position;
    }

    // The axis a finger bends around: across the finger, so the bend swings it toward
    // where you put that finger's markers (or, without markers, toward the gun).
    static Vector3 Hinge(Hd2GripHand grip, int index, Transform[] joints, Transform gun)
    {
        Vector3 along = joints[1].position - joints[0].position;
        if (along.sqrMagnitude < 0.000001f)
            return Vector3.zero;
        along.Normalize();

        Vector3 goal = Vector3.zero;
        int count = 0;
        for (int i = 0; i < 3; i++)
        {
            Transform marker = grip.Marker(index + i);
            if (marker == null)
                continue;
            goal += marker.position;
            count++;
        }

        if (count > 0)
            goal /= count;
        else
        {
            var renderers = gun.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return Vector3.zero;
            goal = renderers[0].bounds.center;
        }

        Vector3 toward = Vector3.ProjectOnPlane(goal - joints[0].position, along);
        if (toward.sqrMagnitude < 0.000001f)
            return Vector3.zero;

        Vector3 hinge = Vector3.Cross(along, toward.normalized).normalized;
        // Unity's rotation handedness is easy to get backwards: test it.
        Vector3 bent = Quaternion.AngleAxis(10f, hinge) * along;
        if (Vector3.Dot(bent, toward) < Vector3.Dot(along, toward))
            hinge = -hinge;
        return hinge;
    }

    static float Radius(Transform[] joints)
    {
        float length = Vector3.Distance(joints[0].position, joints[1].position)
            + Vector3.Distance(joints[1].position, joints[2].position);
        return Mathf.Clamp(length * 0.12f, 0.002f, 0.02f);
    }

    static Vector3 TipEnd(Transform[] joints)
    {
        Transform tip = joints[2];
        if (tip.childCount > 0)
            return tip.GetChild(0).position;
        return tip.position + (tip.position - joints[1].position);
    }

    // Segment i runs from joint i to the next joint, or to the fingertip.
    static void Segment(Transform[] joints, int i, out Vector3 from, out Vector3 to)
    {
        from = joints[i].position;
        to = i < 2 ? joints[i + 1].position : TipEnd(joints);
    }

    // Each joint keeps bending until its segment (or a segment past it) would hit the gun.
    static int CurlUntilContact(Transform[] joints, Vector3 hinge, float radius, PhysicsScene physics, HashSet<Collider> gun)
    {
        var bent = new float[3];
        var stopped = new bool[3];
        var startedInside = new bool[3];
        for (int i = 0; i < 3; i++)
            startedInside[i] = Hits(joints, i, radius, physics, gun);

        for (int step = 0; step < MaxSteps; step++)
        {
            bool moved = false;
            for (int j = 0; j < 3; j++)
            {
                if (stopped[j])
                    continue;
                if (bent[j] + StepDegrees > MaxBend[j])
                {
                    stopped[j] = true;
                    continue;
                }

                Quaternion turn = Quaternion.AngleAxis(StepDegrees, hinge);
                joints[j].rotation = turn * joints[j].rotation;
                bent[j] += StepDegrees;

                bool hit = false;
                for (int s = j; s < 3; s++)
                {
                    bool inside = Hits(joints, s, radius, physics, gun);
                    if (inside && !startedInside[s])
                        hit = true;
                    else if (!inside)
                        startedInside[s] = false;
                }

                if (hit)
                {
                    joints[j].rotation = Quaternion.Inverse(turn) * joints[j].rotation;
                    bent[j] -= StepDegrees;
                    stopped[j] = true;
                }
                else
                    moved = true;
            }

            if (!moved)
                break;
        }

        int touching = 0;
        for (int i = 0; i < 3; i++)
        {
            if (stopped[i] && bent[i] + StepDegrees <= MaxBend[i])
                touching++;
        }

        return touching;
    }

    static readonly RaycastHit[] hits = new RaycastHit[16];
    static readonly Collider[] overlaps = new Collider[16];

    static bool Hits(Transform[] joints, int segment, float radius, PhysicsScene physics, HashSet<Collider> gun)
    {
        Segment(joints, segment, out Vector3 from, out Vector3 to);
        Vector3 delta = to - from;
        float length = delta.magnitude;
        if (length < 0.0001f)
            return false;

        int count = physics.OverlapSphere(to, radius, overlaps, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            if (gun.Contains(overlaps[i]))
                return true;
        }

        count = physics.SphereCast(from, radius, delta / length, hits, length, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            if (gun.Contains(hits[i].collider))
                return true;
        }

        return false;
    }

    // Bends the three index joints about one hinge so the fingertip lands on the trigger.
    static float ReachTrigger(Transform[] joints, Vector3 hinge, Vector3 target)
    {
        var bent = new float[3];
        for (int iteration = 0; iteration < 30; iteration++)
        {
            for (int j = 2; j >= 0; j--)
            {
                Vector3 pivot = joints[j].position;
                Vector3 tip = Vector3.ProjectOnPlane(TipEnd(joints) - pivot, hinge);
                Vector3 want = Vector3.ProjectOnPlane(target - pivot, hinge);
                if (tip.sqrMagnitude < 0.000001f || want.sqrMagnitude < 0.000001f)
                    continue;

                float angle = Vector3.SignedAngle(tip, want, hinge);
                float next = Mathf.Clamp(bent[j] + angle, -10f, MaxBend[j]);
                angle = next - bent[j];
                bent[j] = next;
                joints[j].rotation = Quaternion.AngleAxis(angle, hinge) * joints[j].rotation;
            }
        }

        return Vector3.Distance(TipEnd(joints), target);
    }
}
