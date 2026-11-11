using UnityEngine;

/// <summary>
/// Solid for players and the jump pointer. Gun fire and rockets pass through.
/// </summary>
public class Hd2NoJump : MonoBehaviour
{
    public static bool Passes(Collider collider)
    {
        return collider != null && collider.GetComponentInParent<Hd2NoJump>() != null;
    }
}
