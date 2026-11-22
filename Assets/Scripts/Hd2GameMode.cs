using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Which game mode to play on a map, and switching the map's per-mode content.
///
/// A map that serves several modes has a "Game Modes" object with one child per mode,
/// named as in NodeName ("Team Death Match", "Elimination", "Control Point"). Everything
/// that belongs to only one mode (pickups, the control point button, center pieces) lives
/// under that mode's child. When the scene loads, the chosen mode's child is switched on,
/// the others off, and that mode's controller is added to it:
///   Team Death Match: Hd2Shootout
///   Elimination:      Hd2Shootout, which adds Hd2Elimination
///   Control Point:    Hd2ControlPoint (teams are filled and placed as in Team Death Match,
///                     and every bot plays the objective)
/// The title screen sets Selected before loading the map. A scene without a "Game Modes"
/// object is left alone.
/// </summary>
public static class Hd2GameMode
{
    public enum Mode
    {
        TeamDeathmatch,
        Elimination,
        ControlPoint
    }

    public const string NodesName = "Game Modes";

    /// <summary>The mode the next map load plays. Team Death Match unless the title picks another.</summary>
    public static Mode Selected = Mode.TeamDeathmatch;

    public static string NodeName(Mode mode)
    {
        switch (mode)
        {
            case Mode.Elimination: return "Elimination";
            case Mode.ControlPoint: return "Control Point";
            default: return "Team Death Match";
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    // Runs after the scene's objects wake up and before any of them start.
    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Transform nodes = FindNodes(scene);
        if (nodes == null)
            return;

        string wanted = NodeName(Selected);
        Transform chosen = null;
        for (int i = 0; i < nodes.childCount; i++)
        {
            Transform child = nodes.GetChild(i);
            bool on = child.name == wanted;
            if (on)
                chosen = child;
            if (child.gameObject.activeSelf != on)
                child.gameObject.SetActive(on);
        }

        if (chosen == null)
        {
            Debug.LogWarning("Hd2 game mode: no '" + wanted + "' under " + NodesName + " in " + scene.name);
            return;
        }

        switch (Selected)
        {
            case Mode.TeamDeathmatch:
            case Mode.Elimination:
                if (chosen.GetComponent<Hd2Shootout>() == null)
                    chosen.gameObject.AddComponent<Hd2Shootout>();
                break;

            case Mode.ControlPoint:
                Hd2Shootout.SetUpTeams();
                PlayObjective();
                if (chosen.GetComponent<Hd2ControlPoint>() == null)
                    chosen.gameObject.AddComponent<Hd2ControlPoint>();
                break;
        }
    }

    static void PlayObjective()
    {
        var bots = Object.FindObjectsByType<Hd2EnemyBot>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < bots.Length; i++)
        {
            if (bots[i] != null)
                bots[i].domination = true;
        }
    }

    static Transform FindNodes(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            return null;
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            Transform found = Find(roots[i].transform, NodesName);
            if (found != null)
                return found;
        }

        return null;
    }

    static Transform Find(Transform root, string name)
    {
        if (root.name == name)
            return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = Find(root.GetChild(i), name);
            if (found != null)
                return found;
        }

        return null;
    }
}
