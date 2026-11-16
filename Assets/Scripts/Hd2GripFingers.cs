using UnityEngine;

/// <summary>
/// Poses robot finger bones from the GripFingers markers on a held gun.
/// Unbaked guns aim each bone along its markers (legacy). Baked guns (see Hd2GripPose)
/// copy the marker rotations directly, so the hand matches what was posed in the editor.
/// </summary>
public static class Hd2GripFingers
{
    public const int BoneCount = 15;

    public static readonly string[] Names = { "Thumb", "Index", "Middle", "Ring", "Pinky" };

    /// <summary>".R" for a right hand bone, ".L" otherwise.</summary>
    public static string Side(Transform hand)
    {
        return hand.name.EndsWith("R") || hand.name.EndsWith("_R") ? ".R" : ".L";
    }

    /// <summary>Fills bones with Thumb, Thumb.001, Thumb.Tip, Index, ... under the hand.</summary>
    public static bool FindBones(Transform hand, Transform[] bones)
    {
        if (hand == null || bones == null)
            return false;

        string side = Side(hand);
        bool any = false;
        for (int finger = 0; finger < Names.Length; finger++)
        {
            string name = Names[finger];
            Transform bone = Find(hand, name + side);
            Transform boneMid = bone != null ? Find(bone, name + side + ".001") : null;
            Transform boneTip = boneMid != null ? Find(boneMid, name + side + ".Tip") : null;
            int index = finger * 3;
            bones[index] = bone;
            bones[index + 1] = boneMid;
            bones[index + 2] = boneTip;
            any |= bone != null;
        }

        return any;
    }

    /// <summary>Fills markers with Thumb, Thumb.001, Thumb.Tip, Index, ... under the gun's GripFingers.</summary>
    public static bool FindMarkers(Transform gun, Transform[] markers)
    {
        if (gun == null || markers == null)
            return false;

        Transform root = Find(gun, "GripFingers");
        if (root == null)
            return false;

        bool any = false;
        for (int finger = 0; finger < Names.Length; finger++)
        {
            string name = Names[finger];
            Transform marker = Find(root, name);
            Transform markerMid = marker != null ? Find(marker, name + ".001") : null;
            Transform markerTip = markerMid != null ? Find(markerMid, name + ".Tip") : null;
            int index = finger * 3;
            markers[index] = marker;
            markers[index + 1] = markerMid;
            markers[index + 2] = markerTip;
            any |= marker != null && markerMid != null;
        }

        return any;
    }

    public static bool Bind(Transform hand, Transform gun, Transform[] bones, Transform[] markers, Vector3[] axes)
    {
        if (hand == null || gun == null || bones == null || markers == null || axes == null)
            return false;
        if (!FindMarkers(gun, markers))
            return false;

        FindBones(hand, bones);
        bool any = false;
        for (int finger = 0; finger < Names.Length; finger++)
        {
            int index = finger * 3;
            axes[index] = LengthAxis(bones[index], bones[index + 1]);
            axes[index + 1] = LengthAxis(bones[index + 1], bones[index + 2]);
            axes[index + 2] = LengthAxis(bones[index + 2], null, bones[index + 1]);
            if (bones[index] != null && markers[index] != null && markers[index + 1] != null)
                any = true;
        }

        return any;
    }

    /// <summary>Legacy: points each bone along its markers. Used until a gun is baked.</summary>
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

    /// <summary>
    /// Reflects a rotation across the plane with this normal the same way MirrorGrip does:
    /// forward and up are reflected, right is rebuilt so the result stays a proper rotation.
    /// </summary>
    public static Quaternion MirrorRotation(Quaternion rotation, Vector3 normal)
    {
        Vector3 forward = Vector3.Reflect(rotation * Vector3.forward, normal);
        Vector3 up = Vector3.Reflect(rotation * Vector3.up, normal);
        return Quaternion.LookRotation(forward, up);
    }

    public static Vector3 MirrorPoint(Vector3 point, Vector3 planePoint, Vector3 normal)
    {
        return point - 2f * Vector3.Dot(point - planePoint, normal) * normal;
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

    public static Transform Find(Transform root, string name)
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

/// <summary>
/// One robot hand's grip state: its bones, the held gun's markers, and (for the right hand)
/// the rest-pose offsets that turn a mirrored left-hand marker into this hand's bone rotation.
/// Create one per hand at rest pose, then call Track, PinHand and ApplyFingers each frame.
/// </summary>
public sealed class Hd2GripHand
{
    public const int HandSlot = Hd2GripFingers.BoneCount;

    /// <summary>World scale of a hand holding a gun. Fixed, so every gun gets the same size hand.</summary>
    public const float HandWorldScale = 1f;

    public readonly Transform hand;
    public readonly bool right;
    readonly Transform[] bones = new Transform[Hd2GripFingers.BoneCount];
    readonly Transform[] markers = new Transform[Hd2GripFingers.BoneCount];
    readonly Vector3[] axes = new Vector3[Hd2GripFingers.BoneCount];
    readonly Quaternion[] mirror = new Quaternion[Hd2GripFingers.BoneCount + 1];
    // Each bone's direction to its child bone, in its own space, from the rest pose.
    readonly Vector3[] lengthLocal = new Vector3[Hd2GripFingers.BoneCount];
    Transform gun;
    Transform wrist;
    Hd2GripPose pose;
    bool fingersReady;

    public Hd2GripHand(Transform hand, bool right)
    {
        this.hand = hand;
        this.right = right;
        for (int i = 0; i < mirror.Length; i++)
            mirror[i] = Quaternion.identity;
        Hd2GripFingers.FindBones(hand, bones);
        for (int i = 0; i < Hd2GripFingers.BoneCount; i++)
        {
            Transform bone = bones[i];
            Transform child = i % 3 < 2 ? bones[i + 1] : null;
            if (bone == null || child == null)
                continue;
            Vector3 along = child.position - bone.position;
            if (along.sqrMagnitude > 0.0000001f)
                lengthLocal[i] = bone.InverseTransformDirection(along).normalized;
        }
    }

    public Transform Gun => gun;
    public Transform Wrist => wrist;
    public Hd2GripPose Pose => pose;
    public bool HasFingers => fingersReady;
    public bool Baked => pose != null && pose.baked && wrist != null && hand != null;
    public Transform Bone(int index) => bones[index];
    public Transform Marker(int index) => markers[index];
    public Quaternion MirrorOffset(int slot) => mirror[slot];

    /// <summary>
    /// Call once, with both hands still in the imported rest pose. Records how each right bone
    /// differs from its mirrored left twin, so mirrored markers can drive the right hand.
    /// </summary>
    public static void BuildMirror(Transform frame, Hd2GripHand left, Hd2GripHand right)
    {
        if (left == null || right == null || left.hand == null || right.hand == null)
            return;

        Vector3 normal = LateralNormal(frame, left.hand.position, right.hand.position);
        if (normal.sqrMagnitude < 0.0001f)
            return;

        right.mirror[HandSlot] = Offset(left.hand, right.hand, normal);
        for (int i = 0; i < Hd2GripFingers.BoneCount; i++)
            right.mirror[i] = Offset(left.bones[i], right.bones[i], normal);
    }

    static Quaternion Offset(Transform leftBone, Transform rightBone, Vector3 normal)
    {
        if (leftBone == null || rightBone == null)
            return Quaternion.identity;
        Quaternion mirroredLeft = Hd2GripFingers.MirrorRotation(leftBone.rotation, normal);
        return Quaternion.Inverse(mirroredLeft) * rightBone.rotation;
    }

    // The body axis the two hands are spread along. Snapping to the frame's axis keeps
    // small rest-pose asymmetry from tilting the mirror plane.
    static Vector3 LateralNormal(Transform frame, Vector3 left, Vector3 right)
    {
        Vector3 across = right - left;
        if (frame == null)
            return across.sqrMagnitude > 0.0001f ? across.normalized : Vector3.zero;

        Vector3 local = frame.InverseTransformDirection(across);
        Vector3 axis = Mathf.Abs(local.x) >= Mathf.Abs(local.y) && Mathf.Abs(local.x) >= Mathf.Abs(local.z)
            ? Vector3.right
            : Mathf.Abs(local.y) >= Mathf.Abs(local.z) ? Vector3.up : Vector3.forward;
        return frame.TransformDirection(axis).normalized;
    }

    /// <summary>Forces the next Track call to look the gun's markers up again.</summary>
    public void Rebind()
    {
        gun = null;
        wrist = null;
        pose = null;
        fingersReady = false;
    }

    /// <summary>Rebinds markers when the held gun changes. Pass null when the hand is empty.</summary>
    public void Track(Transform heldGun)
    {
        if (heldGun == gun)
            return;

        gun = heldGun;
        wrist = null;
        pose = null;
        fingersReady = false;
        if (gun == null)
            return;

        pose = gun.GetComponentInChildren<Hd2GripPose>(true);
        if (pose == null)
            pose = gun.GetComponentInParent<Hd2GripPose>();
        wrist = Hd2GripFingers.Find(gun, "Wrist");
        fingersReady = Hd2GripFingers.Bind(hand, gun, bones, markers, axes)
            || (pose != null && pose.HasFingers);
    }

    /// <summary>Baked guns only: puts the hand bone exactly on the Wrist marker at the fixed hand size.</summary>
    public bool PinHand()
    {
        if (!Baked)
            return false;

        PinHandTo(wrist);
        return true;
    }

    public void PinHandTo(Transform wristMarker)
    {
        if (hand == null || wristMarker == null)
            return;
        hand.SetPositionAndRotation(wristMarker.position, wristMarker.rotation * mirror[HandSlot]);
        SetWorldScale(hand, HandWorldScale);
    }

    /// <summary>Poses the fingers from the markers. Returns false when the gun has no GripFingers.</summary>
    public bool ApplyFingers()
    {
        if (!fingersReady)
            return false;

        if (pose != null && pose.HasFingers && wrist != null)
        {
            ApplyFingerData();
            return true;
        }

        if (pose != null && pose.baked)
        {
            // Copy each marker's rotation, then swing the bone to point at the next marker.
            // A fresh bake makes the swing zero; it only matters after you move markers by
            // hand in the prefab, so those edits show up in game without baking again.
            for (int i = 0; i < Hd2GripFingers.BoneCount; i++)
            {
                if (bones[i] == null || markers[i] == null)
                    continue;

                Quaternion rotation = markers[i].rotation * mirror[i];
                Transform next = i % 3 < 2 ? markers[i + 1] : null;
                if (next != null && lengthLocal[i].sqrMagnitude > 0.5f)
                {
                    Vector3 want = next.position - markers[i].position;
                    if (want.sqrMagnitude > 0.0000001f)
                        rotation = Quaternion.FromToRotation(rotation * lengthLocal[i], want) * rotation;
                }

                bones[i].rotation = rotation;
            }

            return true;
        }

        Hd2GripFingers.Apply(bones, markers, axes);
        return true;
    }

    // Rebuilds each finger from the stored left-hand rotations, starting from the Wrist.
    // On the mirrored right-hand gun the Wrist is already mirrored, and mirroring a chain
    // of rotations is the same as mirroring each link, so each stored rotation is mirrored
    // (x, -y, -z, w) and the right rig's rest offset applied last.
    void ApplyFingerData()
    {
        Quaternion[] data = pose.fingers;
        for (int finger = 0; finger < Hd2GripFingers.Names.Length; finger++)
        {
            Quaternion world = wrist.rotation;
            for (int joint = 0; joint < 3; joint++)
            {
                int i = finger * 3 + joint;
                Quaternion local = data[i];
                if (right)
                    local = new Quaternion(local.x, -local.y, -local.z, local.w);
                world = world * local;
                if (bones[i] != null)
                    bones[i].rotation = world * mirror[i];
            }
        }
    }

    /// <summary>Left-hand finger rotations, each relative to the bone before it, as Hd2GripPose stores them.</summary>
    public Quaternion[] ReadFingerData()
    {
        var data = new Quaternion[Hd2GripFingers.BoneCount];
        for (int finger = 0; finger < Hd2GripFingers.Names.Length; finger++)
        {
            Quaternion parent = hand.rotation;
            for (int joint = 0; joint < 3; joint++)
            {
                int i = finger * 3 + joint;
                Quaternion world = bones[i] != null ? bones[i].rotation * Quaternion.Inverse(mirror[i]) : parent;
                data[i] = Quaternion.Inverse(parent) * world;
                parent = world;
            }
        }

        return data;
    }

    /// <summary>
    /// Middle of the closed fist: the average of the index-to-pinky joints, which wrap around
    /// whatever the hand holds. That is where the controller sits in a real hand.
    /// </summary>
    public bool TryGripCenter(out Vector3 center)
    {
        center = Vector3.zero;
        int count = 0;
        for (int i = 3; i < Hd2GripFingers.BoneCount; i++)
        {
            if (bones[i] == null)
                continue;
            center += bones[i].position;
            count++;
        }

        if (count == 0)
            return false;
        center /= count;
        return true;
    }

    public static void SetWorldScale(Transform bone, float world)
    {
        Transform parent = bone.parent;
        Vector3 parentScale = parent != null ? parent.lossyScale : Vector3.one;
        bone.localScale = new Vector3(
            world / Mathf.Max(0.0001f, Mathf.Abs(parentScale.x)),
            world / Mathf.Max(0.0001f, Mathf.Abs(parentScale.y)),
            world / Mathf.Max(0.0001f, Mathf.Abs(parentScale.z)));
    }
}
