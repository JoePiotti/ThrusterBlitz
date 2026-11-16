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

    public bool HasFingers => fingers != null && fingers.Length == Hd2GripFingers.BoneCount;
}
