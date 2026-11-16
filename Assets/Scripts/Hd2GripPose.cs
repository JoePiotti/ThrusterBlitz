using UnityEngine;

/// <summary>
/// Sits on the root of a grip weapon prefab (GripSmg, GripPistol, ...) and holds its grip.
/// The Wrist child is the exact pose of the left Hand bone; fingers holds each finger bone's
/// rotation relative to the bone before it (Thumb, Thumb.001, Thumb.Tip, Index, ... Pinky.Tip).
/// Edit it by opening the prefab and posing the robot hand shown there.
/// </summary>
[DisallowMultipleComponent]
public class Hd2GripPose : MonoBehaviour
{
    [Tooltip("True once the Wrist child holds the hand bone's pose (set when you pose the robot hand in Prefab Mode).")]
    public bool baked;

    [Tooltip("Unused. The hand is always a fixed size (Hd2GripHand.HandWorldScale).")]
    public float handScale = 1f;

    [Tooltip("Left-hand finger rotations, each relative to the bone before it. Written by the Prefab Mode hand preview.")]
    public Quaternion[] fingers = new Quaternion[0];

    [Tooltip("Shifts the gun inside the controller when it is picked up, so the robot hand lines up with your real hand. " +
             "Meters in the controller's space, for the LEFT hand: +x moves the gun right. The right hand mirrors x.")]
    public Vector3 handOffset = new Vector3(0.03f, 0f, 0f);

    public bool HasFingers => fingers != null && fingers.Length == Hd2GripFingers.BoneCount;

    bool offsetApplied;

    // Runs once on the copy that a pickup (or a bot) puts in a hand named LeftHand / RightHand.
    // Not in the editor, so the prefab itself never moves.
    void Start()
    {
        if (offsetApplied || !Application.isPlaying)
            return;
        offsetApplied = true;

        Transform holder = transform.parent;
        if (holder == null)
            return;
        bool right = holder.name.IndexOf("Right", System.StringComparison.OrdinalIgnoreCase) >= 0;
        Vector3 offset = handOffset;
        if (right)
            offset.x = -offset.x;
        transform.localPosition += offset;
    }
}
