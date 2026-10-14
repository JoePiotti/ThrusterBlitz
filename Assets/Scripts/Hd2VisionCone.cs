using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks the people a bot can see. The test is a 20 m, 15 degree cone in code.
/// A mesh collider for that cone was large enough to drag the headset to a few frames per second.
/// </summary>
public class Hd2VisionCone : MonoBehaviour
{
    static readonly List<Transform> fighters = new List<Transform>();

    public static void Register(Transform root)
    {
        if (root == null || fighters.Contains(root))
            return;
        fighters.Add(root);
    }

    public static void Unregister(Transform root)
    {
        fighters.Remove(root);
    }

    public static void CopyFighters(List<Transform> into)
    {
        into.Clear();
        for (int i = 0; i < fighters.Count; i++)
        {
            if (fighters[i] != null)
                into.Add(fighters[i]);
        }
    }
}
