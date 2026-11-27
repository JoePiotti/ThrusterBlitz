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
/// - Control Point: the fighter who turned the point to their team's color gets 20 points
///   (ClaimPoints) for every team point it produces until the other team takes it.
/// - Domination: 20 points (ClaimPoints) for every button turned to your team's color, even
///   if the other team turns it back. When the team wins the round, whoever turned each of
///   its buttons gets 200 more (DominationHoldPoints).
/// - Payload: 10 points (PayloadPoints) for every 0.1 m (PayloadStep) the cart moves forward
///   to each fighter riding it.
/// - Multi-kills, kills within MultiKillSeconds of each other: a double is worth 50, a
///   triple 100, a quad 150, five or more 200 (MultiKillPoints). Points are paid as each
///   kill lands, so a triple pays 50 on the second kill and 50 more on the third.
/// - Match end: 100 points (StreakBonusPoints) times the best kill streak of the match. Ping is for the HUD (LocalPingMs).
/// </summary>
public static class Hd2MatchStats
{
    public struct Row
    {
        public int Rank;
        public string Name;
        public int Kills;
        public int Deaths;
        public int Streak;
        public int Score;
        public Hd2Team Team;
        public bool IsYou;
        public bool Dead;
    }

    static bool IsDown(Component who)
    {
        Hd2Health life = who != null ? who.GetComponent<Hd2Health>() : null;
        return life != null && life.IsDead;
    }

    public const int KillPoints = 100;
    public const float DamagePoints = 1f;
    public const int ClaimPoints = 20;
    public const int DominationHoldPoints = 200;
    public const int PayloadPoints = 10;
    public const float PayloadStep = 0.1f;
    public const float MultiKillSeconds = 1f;
    public const int StreakBonusPoints = 100;
    // Indexed by kills in the chain: [2] double, [3] triple, [4] quad, [5] five or more.
    static readonly int[] MultiKillPoints = { 0, 0, 50, 100, 150, 200 };

    static bool matchEndPaid;

    /// <summary>True once the match has ended (the end-of-match bonus has been paid).</summary>
    public static bool MatchOver => matchEndPaid;

    /// <summary>The team that won the finished match. None before the end, on a draw, and in free-for-all.</summary>
    public static Hd2Team Winner { get; private set; }

    struct Tally
    {
        public int Kills;
        public int Deaths;
        public int Streak;
        public int BestStreak;
        public int Chain;
        public float LastKillAt;
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
        {
            PayLeaverXp();
            tallies.Clear();
            matchEndPaid = false;
            Winner = Hd2Team.None;
        }
    }

    /// <summary>Called once when a fighter dies. Self-kills and team kills count only the death.</summary>
    /// <summary>An enemy kill (no suicides or team kills): victim, then killer.</summary>
    public static event System.Action<Hd2Health, Hd2Health> EnemyKilled;

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
        EnemyKilled?.Invoke(victim, killerHealth);

        // The local player's kill feed.
        Hd2Hud hud = killerHealth.GetComponent<Hd2Hud>();
        if (hud != null)
        {
            NameAndColor(victim, out string victimName, out Color victimColor);
            hud.AddKill(victimName, victimColor);
        }
    }

    /// <summary>A fighter's shown name and team color (bot, network player or local player).</summary>
    public static void NameAndColor(Hd2Health who, out string name, out Color color)
    {
        name = who.name;
        color = Color.white;
        Hd2EnemyBot bot = who.GetComponent<Hd2EnemyBot>();
        if (bot != null)
        {
            name = bot.FullName;
            color = bot.useBodyColor ? bot.bodyColor : Hd2Locomotion.ArmorOf(bot.team);
            return;
        }

        Hd2NetPlayer net = who.GetComponentInParent<Hd2NetPlayer>();
        if (net != null)
        {
            name = net.DisplayName;
            color = Hd2Locomotion.ArmorOf(net.team);
            return;
        }

        Hd2Locomotion player = who.GetComponent<Hd2Locomotion>();
        if (player != null)
        {
            name = Hd2Identity.Display;
            color = player.teamColor;
        }
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

    public const int WinBonusPoints = 1000;
    public const int DrawBonusPoints = 500;

    /// <summary>
    /// End of a match with no teams: only the kill streak bonus.
    /// </summary>
    public static void AwardMatchEnd()
    {
        AwardMatchEnd(Hd2Team.None, false);
    }

    /// <summary>
    /// End of a team match. Every fighter gets StreakBonusPoints times their best kill
    /// streak, plus the game bonus: WinBonusPoints on the winning team, DrawBonusPoints each
    /// for a draw (winner None), nothing for a loss. Paid once per match; the game modes call
    /// it when they finish. The local player sees the breakdown in a toast.
    /// </summary>
    public static void AwardMatchEnd(Hd2Team winner)
    {
        AwardMatchEnd(winner, true);
    }

    static void AwardMatchEnd(Hd2Team winner, bool teams)
    {
        if (matchEndPaid)
            return;
        matchEndPaid = true;
        Winner = teams ? winner : Hd2Team.None;
        // Every mode ends here: after the summary, back to the spaceship to pick a new game.
        Hd2ReturnToShip.Begin();

        var fighters = UnityEngine.Object.FindObjectsByType<Hd2Health>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < fighters.Length; i++)
        {
            Hd2Health fighter = fighters[i];
            if (fighter == null)
                continue;
            Hd2Team side = Hd2SpawnField.TeamOf(fighter);
            if (teams && side == Hd2Team.None)
                continue;

            int key = fighter.GetInstanceID();
            tallies.TryGetValue(key, out Tally tally);
            int before = tally.Score;
            int streakBonus = StreakBonusPoints * tally.BestStreak;
            int gameBonus = !teams ? 0 : winner == Hd2Team.None ? DrawBonusPoints : side == winner ? WinBonusPoints : 0;
            tally.Score += streakBonus + gameBonus;
            tallies[key] = tally;

            // The local player earns experience toward their level (Hd2Progression).
            bool local = fighter.GetComponent<Hd2Locomotion>() != null;
            Hd2Progression.MatchXp xp = default;
            if (local)
            {
                if (!factorKnown)
                {
                    factorKnown = true;
                    matchFactor = OpponentFactor(out matchNoXp);
                }

                var outcome = teams && winner == Hd2Team.None ? Hd2Progression.Outcome.Draw
                    : teams && side == winner ? Hd2Progression.Outcome.Win
                    : Hd2Progression.Outcome.Loss;
                xp = Hd2Progression.AwardMatch(tally.Score, outcome, matchFactor, matchNoXp);
            }

            Hd2Hud hud = fighter.GetComponent<Hd2Hud>();
            if (hud != null)
                hud.ShowMatchSummary(before, streakBonus, gameBonus, tally.Score, !teams ? "" : winner == Hd2Team.None ? "Draw" : side == winner ? "Win" : "Loss", local ? (Hd2Progression.MatchXp?)xp : null);
        }
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
        NoteLocal(health);
    }

    // ---- Player level (Hd2Progression) ----
    // The local player's tally key and this match's opponent factor, kept so leaving before
    // the end can still pay XP once the match scene is gone.
    static int localKey;
    static bool factorKnown;
    static float matchFactor = 1f;
    static bool matchNoXp;

    static void NoteLocal(Hd2Health health)
    {
        if (health == null || health.GetComponent<Hd2Locomotion>() == null)
            return;
        localKey = health.GetInstanceID();
        if (!factorKnown)
        {
            factorKnown = true;
            matchFactor = OpponentFactor(out matchNoXp);
        }
    }

    static float OpponentFactor(out bool noXp)
    {
        bool anotherHuman = false;
        var others = UnityEngine.Object.FindObjectsByType<Hd2NetPlayer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < others.Length; i++)
        {
            Hd2NetPlayer other = others[i];
            if (other != null && other.Object != null && other.Object.IsValid && !other.Object.HasInputAuthority)
                anotherHuman = true;
        }

        var bots = UnityEngine.Object.FindObjectsByType<Hd2EnemyBot>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        Hd2BotDifficulty hardest = Hd2BotDifficulty.Easy;
        for (int i = 0; i < bots.Length; i++)
        {
            if (bots[i] != null && RankOf(bots[i].difficulty) > RankOf(hardest))
                hardest = bots[i].difficulty;
        }

        return Hd2Progression.OpponentFactor(anotherHuman, bots.Length > 0, hardest, out noXp);
    }

    // Leaving a match before it ends (any scene change): half the score so far as XP.
    static void PayLeaverXp()
    {
        if (!matchEndPaid && localKey != 0 && tallies.TryGetValue(localKey, out Tally tally) && tally.Score > 0)
            Hd2Progression.AwardLeftEarly(tally.Score, matchFactor, matchNoXp);
        localKey = 0;
        factorKnown = false;
    }

    static void Add(Hd2Health who, int kills, int deaths)
    {
        NoteLocal(who);
        int key = who.GetInstanceID();
        tallies.TryGetValue(key, out Tally tally);
        tally.Kills += kills;
        tally.Deaths += deaths;
        // Kill streak: kills since this fighter last died.
        if (deaths > 0)
        {
            tally.Streak = 0;
            tally.Chain = 0;
        }

        if (kills > 0)
        {
            tally.Streak += kills;
            tally.BestStreak = Mathf.Max(tally.BestStreak, tally.Streak);
            float now = Time.time;
            tally.Chain = tally.Chain > 0 && now - tally.LastKillAt <= MultiKillSeconds ? tally.Chain + 1 : 1;
            tally.LastKillAt = now;
            // Pay the step up to this chain's value, so the total matches the biggest tier.
            int reached = Mathf.Min(tally.Chain, MultiKillPoints.Length - 1);
            int before = Mathf.Min(tally.Chain - 1, MultiKillPoints.Length - 1);
            tally.Score += MultiKillPoints[reached] - MultiKillPoints[before];
        }

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
                Rank = Hd2Progression.Level,
                Name = Hd2Identity.Display,
                Kills = tally.Kills,
                Deaths = tally.Deaths,
                Streak = tally.Streak,
                Score = tally.Score,
                Team = players[i].team,
                IsYou = true,
                Dead = IsDown(players[i])
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
                Streak = tally.Streak,
                Score = tally.Score,
                Team = other.team,
                Dead = IsDown(other)
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
                Streak = tally.Streak,
                Score = tally.Score,
                Team = Hd2SpawnField.TeamOf(bots[i]),
                Dead = IsDown(bots[i])
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
