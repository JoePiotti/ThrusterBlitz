using UnityEngine;

/// <summary>
/// Points robot finger bones along the GripFingers markers on a held gun.
/// Marker and bone arrays are caller-owned and filled only when the gun instance changes.
/// </summary>
public static class Hd2GripFingers
{
    public const int BoneCount = 15;

    static readonly string[] Names = { "Thumb", "Index", "Middle", "Ring", "Pinky" };

    public static bool Bind(Transform hand, Transform gun, Transform[] bones, Transform[] markers, Vector3[] axes)
    {
        if (hand == null || gun == null || bones == null || markers == null || axes == null)
            return false;

        Transform root = Find(gun, "GripFingers");
        if (root == null)
            return false;

        string side = hand.name.EndsWith("R") || hand.name.EndsWith("_R") ? ".R" : ".L";
        bool any = false;
        for (int finger = 0; finger < Names.Length; finger++)
        {
            string name = Names[finger];
            Transform marker = Find(root, name);
            Transform markerMid = marker != null ? Find(marker, name + ".001") : null;
            Transform markerTip = markerMid != null ? Find(markerMid, name + ".Tip") : null;
            Transform bone = Find(hand, name + side);
            Transform boneMid = bone != null ? Find(bone, name + side + ".001") : null;
            Transform boneTip = boneMid != null ? Find(boneMid, name + side + ".Tip") : null;
            int index = finger * 3;
            markers[index] = marker;
            markers[index + 1] = markerMid;
            markers[index + 2] = markerTip;
            bones[index] = bone;
            bones[index + 1] = boneMid;
            bones[index + 2] = boneTip;
            axes[index] = LengthAxis(bone, boneMid);
            axes[index + 1] = LengthAxis(boneMid, boneTip);
            axes[index + 2] = LengthAxis(boneTip, null, boneMid);
            if (bone != null && marker != null && markerMid != null)
                any = true;
        }

        return any;
    }

    public static void Apply(Transform[] bones, Transform[] markers, Vector3[] axes)
    {
        if (bones == null || markers == null || axes == null)
            return;

        for (int finger = 0; finger < Names.Length; finger++)
        {
            int index = finger * 3;
            Aim(bones[index], bones[index + 1], markers[index], markers[index + 1], axes[index]);
            Aim(bones[index + 1], bones[index + 2], markers[index + 1], markers[index + 2], axes[index + 1]);
            Aim(bones[index + 2], null, markers[index + 1], markers[index + 2], axes[index + 2]);
        }
    }

    static void Aim(Transform bone, Transform child, Transform from, Transform to, Vector3 localAxis)
    {
        if (bone == null || from == null || to == null)
            return;

        Vector3 desired = to.position - from.position;
        if (desired.sqrMagnitude < 0.0000001f)
            return;

        if (child != null)
        {
            PointAt(bone, child, bone.position + desired);
            return;
        }

        if (localAxis.sqrMagnitude < 0.0000001f)
            return;

        Vector3 current = bone.TransformDirection(localAxis);
        if (current.sqrMagnitude < 0.0000001f)
            return;

        bone.rotation = Quaternion.FromToRotation(current, desired) * bone.rotation;
    }

    static Vector3 LengthAxis(Transform bone, Transform child, Transform parent = null)
    {
        if (bone == null)
            return Vector3.zero;

        Vector3 world = Vector3.zero;
        if (child != null)
            world = child.position - bone.position;
        else if (parent != null)
            world = bone.position - parent.position;
        if (world.sqrMagnitude < 0.0000001f)
            return Vector3.zero;
        return bone.InverseTransformDirection(world.normalized);
    }

    static void PointAt(Transform bone, Transform child, Vector3 worldPoint)
    {
        Vector3 current = child.position - bone.position;
        Vector3 desired = worldPoint - bone.position;
        if (current.sqrMagnitude < 0.0000001f || desired.sqrMagnitude < 0.0000001f)
            return;

        bone.rotation = Quaternion.FromToRotation(current, desired) * bone.rotation;
    }

    static Transform Find(Transform root, string name)
    {
        if (root == null)
            return null;
        if (root.name == name)
            return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = Find(root.GetChild(i), name);
            if (found != null)
                return found;
        }

        return null;
    }
}
