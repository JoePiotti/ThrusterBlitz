using UnityEngine;

/// <summary>
/// Sits on the root of a grip weapon prefab (GripSmg, GripPistol, ...).
/// Once baked, the Wrist marker holds the exact world pose of the left Hand bone and every
/// GripFingers marker holds the exact pose of its finger bone, so the runtime copies them
/// instead of aiming bones at the markers. Bake with Tools > HD2 > Grip Baker.
/// </summary>
[DisallowMultipleComponent]
public class Hd2GripPose : MonoBehaviour
{
    [Tooltip("True after Tools > HD2 > Grip Baker wrote the Wrist and GripFingers poses from the robot hand.")]
    public bool baked;

    [Tooltip("Hand world scale divided by this root's world scale at bake time. Keeps the hand sized to the gun.")]
    public float handScale = 1f;
}
