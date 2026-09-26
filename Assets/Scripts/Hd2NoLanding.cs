using UnityEngine;

/// <summary>
/// Marks a collider as an illegal thrust landing (water, a pit, and similar).
/// The aim arc turns yellow and releasing the thrust button does not move the player.
/// A grind rail hit still wins if the arc hits the rail itself.
/// </summary>
public class Hd2NoLanding : MonoBehaviour
{
}
