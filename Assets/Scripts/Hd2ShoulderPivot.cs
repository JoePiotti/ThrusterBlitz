using UnityEngine;

/// <summary>
/// An unskinned marker. Move this object to place the shoulder pivot.
/// The arm mesh does not follow it.
/// </summary>
public class Hd2ShoulderPivot : MonoBehaviour
{
    void OnDrawGizmos()
    {
        Gizmos.color = name.Contains("Left") ? new Color(0.3f, 0.75f, 1f) : new Color(1f, 0.55f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, 0.045f);
    }
}
