using Fusion;
using UnityEngine;

/// <summary>
/// Stand-in body for the first networked room. The server spawns one per player.
/// </summary>
public class Hd2NetPlayer : NetworkBehaviour
{
    static readonly Color[] Colors =
    {
        new Color(0.2f, 0.45f, 0.95f, 1f),
        new Color(0.9f, 0.35f, 0.15f, 1f),
        new Color(0.2f, 0.75f, 0.35f, 1f),
        new Color(0.85f, 0.75f, 0.2f, 1f)
    };

    public override void Spawned()
    {
        var renderer = GetComponent<Renderer>();
        if (renderer == null)
            return;

        int index = Object.InputAuthority.PlayerId;
        if (index < 0)
            index = 0;
        renderer.material.color = Colors[index % Colors.Length];
    }
}
