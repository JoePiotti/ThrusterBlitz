using UnityEngine;

/// <summary>
/// Marker for a player spawn pose. Place under an Hd2SpawnDirector.
/// </summary>
public class Hd2SpawnPoint : MonoBehaviour
{
    public int index;

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, 0.2f);
        Gizmos.DrawLine(transform.position, transform.position + transform.forward * 0.45f);
    }
}
