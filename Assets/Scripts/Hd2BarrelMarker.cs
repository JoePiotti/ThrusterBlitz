using UnityEngine;

/// <summary>
/// Shot origin for the shotgun. Place it at the muzzle. The blue axis is the
/// direction the pellets travel.
/// </summary>
public class Hd2BarrelMarker : MonoBehaviour
{
    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.85f, 0.2f, 1f);
        Gizmos.DrawSphere(transform.position, 0.012f);
        Gizmos.DrawRay(transform.position, transform.forward * 0.35f);
    }
}
