using System.Collections.Generic;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Kills, deaths and score for every fighter in the current match, plus the roster rows the
/// player menu's Game tab shows. Counts reset when a scene loads. Rank is a placeholder:
/// 1 for players, 15/30/45 for easy/medium/hard bots.
///
/// Score:
/// - 1 point per point of damage dealt to an enemy (DamagePoints).
/// - 100 points per kill (KillPoints). Suicides and team kills give nothing.
/// - Control Point / Domination: the fighter who turned a point to their team's color gets
///   20 points (ClaimPoints) for every team point it produces until the other team takes it.
/// - Payload: 10 points (PayloadPoints) for every 0.1 m (PayloadStep) the cart moves forward
///   to each fighter riding it. Ping is for the HUD (LocalPingMs).
/// </summary>
public static class Hd2MatchStats
{
    public struct Row
    {
        public int Rank;
        public string Name;
        public int Kills;
        public int Deaths;
        public int Score;
        public Hd2Team Team;
        public bool IsYou;
    }

    public const int KillPoints = 100;
    public const float DamagePoints = 1f;
    public const int ClaimPoints = 20;
    public const int PayloadPoints = 10;
    public const float PayloadStep = 0.1f;

    struct Tally
    {
        public int Kills;
        public int Deaths;
        public int Score;
        public float ScoreCarry;
    }

    static readonly Dictionary<int, Tally> tallies = new Dictionary<int, Tally>();
    static readonly List<Row> rows = new List<Row>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Hook()
    {
        tallies.Clear();
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Single)
            tallies.Clear();
    }

    /// <summary>Called once when a fighter dies. Self-kills and team kills count only the death.</summary>
    public static void NoteDeath(Hd2Health victim, Transform killer)
    {
        if (victim == null)
            return;

        Add(victim, 0, 1);
        if (killer == null)
            return;

        Hd2Health killerHealth = killer.GetComponentInParent<Hd2Health>();
        if (killerHealth == null || killerHealth == victim)
            return;

        Hd2Team victimSide = Hd2SpawnField.TeamOf(victim);
        Hd2Team killerSide = Hd2SpawnField.TeamOf(killerHealth);
        if (victimSide != Hd2Team.None && victimSide == killerSide)
            return;

        Add(killerHealth, 1, 0);
        AddScore(killerHealth, KillPoints);
    }

    /// <summary>Damage actually taken by an enemy of the attacker (after headshots, capped at the health left).</summary>
    public static void NoteDamage(Hd2Health victim, Transform attacker, float dealt)
    {
        if (victim == null || attacker == null || dealt <= 0f)
            return;

        Hd2Health attackerHealth = attacker.GetComponentInParent<Hd2Health>();
        if (attackerHealth == null || attackerHealth == victim)
            return;

        Hd2Team victimSide = Hd2SpawnField.TeamOf(victim);
        Hd2Team attackerSide = Hd2SpawnField.TeamOf(attackerHealth);
        if (victimSide != Hd2Team.None && victimSide == attackerSide)
            return;

        int key = attackerHealth.GetInstanceID();
        tallies.TryGetValue(key, out Tally tally);
        tally.ScoreCarry += dealt * DamagePoints;
        int whole = Mathf.FloorToInt(tally.ScoreCarry);
        tally.Score += whole;
        tally.ScoreCarry -= whole;
        tallies[key] = tally;
    }

    /// <summary>Adds points to a fighter (any component on or under its Hd2Health).</summary>
    public static void AddScore(Component who, int points)
    {
        if (who == null || points == 0)
            return;
        Hd2Health health = who as Hd2Health;
        if (health == null)
            health = who.GetComponentInParent<Hd2Health>();
        if (health == null)
            health = who.GetComponentInChildren<Hd2Health>();
        if (health == null)
            return;

        int key = health.GetInstanceID();
        tallies.TryGetValue(key, out Tally tally);
        tally.Score += points;
        tallies[key] = tally;
    }

    static void Add(Hd2Health who, int kills, int deaths)
    {
        int key = who.GetInstanceID();
        tallies.TryGetValue(key, out Tally tally);
        tally.Kills += kills;
        tally.Deaths += deaths;
        tallies[key] = tally;
    }

    static Tally TallyOf(Component who)
    {
        Hd2Health health = who != null ? who.GetComponentInChildren<Hd2Health>(true) : null;
        if (health != null && tallies.TryGetValue(health.GetInstanceID(), out Tally tally))
            return tally;
        return default;
    }

    /// <summary>The team's current score in this game mode (kills, points held, or points won).</summary>
    public static int TeamScore(Hd2Team team)
    {
        if (team == Hd2Team.None)
            return 0;
        bool red = team == Hd2Team.Red;
        int score;
        if (Hd2Shootout.Present)
            score = red ? Hd2Shootout.RedKills : Hd2Shootout.BlueKills;
        else if (Hd2ControlPoint.Present)
            score = red ? Hd2ControlPoint.RedPoints : Hd2ControlPoint.BluePoints;
        else
            score = red ? Hd2Domination.RedPoints : Hd2Domination.BluePoints;
        return Mathf.Max(0, score);
    }

    public static int RankOf(Hd2BotDifficulty difficulty)
    {
        switch (difficulty)
        {
            case Hd2BotDifficulty.Easy: return 15;
            case Hd2BotDifficulty.Medium: return 30;
            default: return 45;
        }
    }

    /// <summary>Everyone in the match, grouped Red, Blue, then no team; best first within a team.</summary>
    public static List<Row> Roster()
    {
        rows.Clear();
        var players = UnityEngine.Object.FindObjectsByType<Hd2Locomotion>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < players.Length; i++)
        {
            Tally tally = TallyOf(players[i]);
            rows.Add(new Row
            {
                Rank = 1,
                Name = Hd2Identity.Display,
                Kills = tally.Kills,
                Deaths = tally.Deaths,
                Score = tally.Score,
                Team = players[i].team,
                IsYou = true
            });
        }

        var others = UnityEngine.Object.FindObjectsByType<Hd2NetPlayer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < others.Length; i++)
        {
            Hd2NetPlayer other = others[i];
            if (other.Object == null || !other.Object.IsValid || other.Object.HasInputAuthority)
                continue;

            Tally tally = TallyOf(other);
            rows.Add(new Row
            {
                Rank = 1,
                Name = other.DisplayName,
                Kills = tally.Kills,
                Deaths = tally.Deaths,
                Score = tally.Score,
                Team = other.team
            });
        }

        var bots = UnityEngine.Object.FindObjectsByType<Hd2EnemyBot>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < bots.Length; i++)
        {
            Tally tally = TallyOf(bots[i]);
            rows.Add(new Row
            {
                Rank = RankOf(bots[i].difficulty),
                Name = bots[i].FullName,
                Kills = tally.Kills,
                Deaths = tally.Deaths,
                Score = tally.Score,
                Team = Hd2SpawnField.TeamOf(bots[i])
            });
        }

        rows.Sort(Compare);
        return rows;
    }

    /// <summary>This headset's round trip to the server in ms, or -1 when not online. For the HUD.</summary>
    public static int LocalPingMs()
    {
        NetworkRunner runner = UnityEngine.Object.FindFirstObjectByType<NetworkRunner>();
        if (runner == null || !runner.IsRunning || runner.LocalPlayer == PlayerRef.None)
            return -1;
        return Mathf.RoundToInt((float)(runner.GetPlayerRtt(runner.LocalPlayer) * 1000.0));
    }

    static int TeamOrder(Hd2Team team)
    {
        return team == Hd2Team.Red ? 0 : team == Hd2Team.Blue ? 1 : 2;
    }

    static int Compare(Row a, Row b)
    {
        int byTeam = TeamOrder(a.Team).CompareTo(TeamOrder(b.Team));
        if (byTeam != 0)
            return byTeam;
        int byScore = b.Score.CompareTo(a.Score);
        if (byScore != 0)
            return byScore;
        int byKills = b.Kills.CompareTo(a.Kills);
        if (byKills != 0)
            return byKills;
        int byDeaths = a.Deaths.CompareTo(b.Deaths);
        if (byDeaths != 0)
            return byDeaths;
        return string.CompareOrdinal(a.Name, b.Name);
    }
}
