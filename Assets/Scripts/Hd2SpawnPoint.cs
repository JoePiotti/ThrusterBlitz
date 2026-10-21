using UnityEngine;

/// <summary>
/// Marker for a player spawn pose. Place under an Hd2SpawnDirector.
/// Leave team empty for a deathmatch spawn. Set Red or Blue to reserve it.
/// </summary>
public class Hd2SpawnPoint : MonoBehaviour
{
    public int index;

    [Tooltip("None leaves this point open for deathmatch. Red or Blue reserves it for that team.")]
    [SerializeField]
    Hd2Team reservedFor = Hd2Team.None;

    /// <summary>
    /// Empty for a deathmatch spawn. Red or Blue reserves this point for that team.
    /// None is stored as empty so opening spawns skip reserved points.
    /// </summary>
    public Hd2Team? team
    {
        get { return reservedFor == Hd2Team.None ? (Hd2Team?)null : reservedFor; }
        set { reservedFor = value.HasValue && value.Value != Hd2Team.None ? value.Value : Hd2Team.None; }
    }

    void OnDrawGizmos()
    {
        if (team == Hd2Team.Red)
            Gizmos.color = new Color(0.9f, 0.15f, 0.12f, 0.9f);
        else if (team == Hd2Team.Blue)
            Gizmos.color = new Color(0.2f, 0.45f, 0.95f, 0.9f);
        else
            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, 0.2f);
        Gizmos.DrawLine(transform.position, transform.position + transform.forward * 0.45f);
    }
}
