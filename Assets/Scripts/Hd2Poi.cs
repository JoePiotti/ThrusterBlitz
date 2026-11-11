using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A spot payload bots may walk toward. Bots choose among the four of these
/// closest to the cart, or the cart itself.
/// </summary>
public class Hd2Poi : MonoBehaviour
{
    static readonly List<Hd2Poi> all = new List<Hd2Poi>(32);
    static bool adopted;

    public static List<Hd2Poi> All => all;

    public static void AdoptNamed()
    {
        if (adopted)
            return;
        adopted = true;

        Transform[] transforms = Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform point = transforms[i];
            if (point == null || !Matches(point.name) || point.GetComponent<Hd2Poi>() != null)
                continue;
            point.gameObject.AddComponent<Hd2Poi>();
        }
    }

    public static bool Matches(string name)
    {
        if (name == null || name.Length < 3)
            return false;
        char a = name[0];
        char b = name[1];
        char c = name[2];
        bool poi = (a == 'p' || a == 'P') && (b == 'o' || b == 'O') && (c == 'i' || c == 'I');
        if (!poi)
            return false;
        if (name.Length == 3)
            return true;
        if (name.Length < 6 || name[3] != ' ' || name[4] != '(' || name[name.Length - 1] != ')')
            return false;
        for (int i = 5; i < name.Length - 1; i++)
        {
            if (name[i] < '0' || name[i] > '9')
                return false;
        }

        return true;
    }

    void OnEnable()
    {
        if (!all.Contains(this))
            all.Add(this);
    }

    void OnDisable()
    {
        all.Remove(this);
    }
}
