using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Bot names. Each difficulty has its own pool, so a name always belongs to the same
/// skill level. Each match shuffles the pools and hands names out in that order, so which
/// names end up on which team changes from match to match. If a match has more bots of one
/// level than names, names repeat with a number ("Rita 2").
/// </summary>
public static class Hd2BotNames
{
    static readonly string[] Hard = { "B. Shears", "Rita", "Lucy", "Mr. Kite", "Mr. Mustard" };
    static readonly string[] Medium = { "Mr. Wilson", "Mr. Heath", "Dr. Roberts", "D. Jones", "M. Jones", "Rocky", "Magill", "Dan" };
    static readonly string[] Easy = { "Sadie", "Prudence", "Martha", "E. Rigby", "Michelle", "Vera", "Chuck", "Dave" };

    static readonly Dictionary<Hd2BotDifficulty, List<string>> shuffled = new Dictionary<Hd2BotDifficulty, List<string>>();
    static readonly Dictionary<Hd2BotDifficulty, int> handedOut = new Dictionary<Hd2BotDifficulty, int>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Hook()
    {
        Reset();
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Single)
            Reset();
    }

    static void Reset()
    {
        shuffled.Clear();
        handedOut.Clear();
    }

    public static string Take(Hd2BotDifficulty level)
    {
        if (!shuffled.TryGetValue(level, out List<string> pool))
        {
            pool = new List<string>(PoolOf(level));
            for (int i = pool.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }

            shuffled[level] = pool;
        }

        handedOut.TryGetValue(level, out int count);
        handedOut[level] = count + 1;
        string name = pool[count % pool.Count];
        int round = count / pool.Count;
        return round == 0 ? name : name + " " + (round + 1);
    }

    static string[] PoolOf(Hd2BotDifficulty level)
    {
        switch (level)
        {
            case Hd2BotDifficulty.Easy: return Easy;
            case Hd2BotDifficulty.Medium: return Medium;
            default: return Hard;
        }
    }
}
