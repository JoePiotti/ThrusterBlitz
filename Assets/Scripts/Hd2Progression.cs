using System;
using UnityEngine;

/// <summary>
/// Player level, saved on this device (PlayerPrefs).
///
/// Experience per match:
///   (score + finish bonus + outcome bonus) x opponent factor, plus a first-win-of-the-day bonus.
///   - Score: the match score, end-of-match bonuses included (Hd2MatchStats).
///   - Finish bonus: FinishXp for being there when the match ends.
///   - Outcome bonus: WinXp, DrawXp, or LossXp (free-for-all counts as a loss here).
///   - Opponent factor: 1 with another human in the match. Bot-only matches scale by the
///     hardest bot (easy 0.5, medium 0.75, hard 1) and give nothing from level
///     BotOnlyLevelLimit on.
///   - Leaving early: half the score so far, no bonuses.
///
/// Levels: going from level L to L+1 costs 2000 + 500 x (L - 1) XP. Levels stop at MaxLevel.
/// Experience past the cap is not banked, so raising MaxLevel later (planned: 999) makes
/// level 100 players earn their way up from 100. The curve continues unchanged above 100.
///
/// Tiers: every 10 levels, each with a badge in Resources/Badges/TierNN.
/// Level rewards are cosmetics earned only by levelling (Hd2LevelRewards), never sold.
/// </summary>
public static class Hd2Progression
{
    public const int MaxLevel = 100;
    public const int BotOnlyLevelLimit = 10;

    public const int FinishXp = 250;
    public const int WinXp = 500;
    public const int DrawXp = 250;
    public const int LossXp = 100;
    public const int FirstWinOfDayXp = 1000;
    public const float LeaveEarlyShare = 0.5f;

    const int FirstStepXp = 2000;
    const int StepGrowthXp = 500;

    const string XpKey = "hd2.progress.xp";
    const string MatchesKey = "hd2.progress.matches";
    const string WinsKey = "hd2.progress.wins";
    const string FirstWinDayKey = "hd2.progress.firstWinDay";

    public static readonly string[] TierNames =
    {
        "Cadet", "Pilot", "Thruster", "Striker", "Ace",
        "Vanguard", "Commander", "Elite", "Legend", "Blitz Master"
    };

    public enum Outcome { Win, Draw, Loss }

    public struct MatchXp
    {
        public int Score;
        public int Finish;
        public int OutcomeBonus;
        public float Factor;
        public int FirstWin;
        public int Total;
        public int LevelBefore;
        public int LevelAfter;
        /// <summary>A bot-only match at or above BotOnlyLevelLimit: no XP.</summary>
        public bool BotOnlyNoXp;
        public bool LeftEarly;
        public bool LeveledUp => LevelAfter > LevelBefore;
    }

    /// <summary>Raised after XP is added (the profile page listens).</summary>
    public static event Action Changed;

    public static long TotalXp
    {
        get
        {
            // PlayerPrefs has no long; XP is stored as a string.
            return long.TryParse(PlayerPrefs.GetString(XpKey, "0"), out long xp) ? Math.Max(0L, xp) : 0L;
        }
        private set { PlayerPrefs.SetString(XpKey, Math.Max(0L, value).ToString()); }
    }

    public static int Matches => PlayerPrefs.GetInt(MatchesKey, 0);
    public static int Wins => PlayerPrefs.GetInt(WinsKey, 0);
    public static int Level => LevelFor(TotalXp);
    public static int Tier => TierOf(Level);
    public static string TierName => TierNames[Tier];

    /// <summary>Total XP needed to reach a level (level 1 is 0).</summary>
    public static long XpToReach(int level)
    {
        long n = Math.Max(0, Math.Min(level, MaxLevel) - 1);
        // Sum of 2000 + 500 (l - 1) for l = 1 .. n.
        return FirstStepXp * n + StepGrowthXp * n * (n - 1) / 2;
    }

    public static int LevelFor(long xp)
    {
        int level = 1;
        while (level < MaxLevel && xp >= XpToReach(level + 1))
            level++;
        return level;
    }

    /// <summary>0-based tier: 0 for levels 1-10, 9 for 91-100.</summary>
    public static int TierOf(int level)
    {
        return Mathf.Clamp((level - 1) / 10, 0, TierNames.Length - 1);
    }

    /// <summary>XP into the current level, and the size of the level (0 at the cap).</summary>
    public static void Progress(out long into, out long span)
    {
        long xp = TotalXp;
        int level = LevelFor(xp);
        if (level >= MaxLevel)
        {
            into = 0;
            span = 0;
            return;
        }

        long start = XpToReach(level);
        into = xp - start;
        span = XpToReach(level + 1) - start;
    }

    static Texture2D[] badges;

    /// <summary>The badge for a 0-based tier, from Resources/Badges/Tier01..Tier10.</summary>
    public static Texture2D Badge(int tier)
    {
        if (badges == null)
            badges = new Texture2D[TierNames.Length];
        tier = Mathf.Clamp(tier, 0, TierNames.Length - 1);
        if (badges[tier] == null)
            badges[tier] = Resources.Load<Texture2D>("Badges/Tier" + (tier + 1).ToString("00"));
        return badges[tier];
    }

    /// <summary>
    /// Opponent factor for the match: 1 with another human, otherwise by the hardest bot,
    /// and 0 for a bot-only match once the player reaches BotOnlyLevelLimit.
    /// </summary>
    public static float OpponentFactor(bool anotherHuman, bool anyBot, Hd2BotDifficulty hardest, out bool botOnlyNoXp)
    {
        botOnlyNoXp = false;
        if (anotherHuman || !anyBot)
            return 1f;
        if (Level >= BotOnlyLevelLimit)
        {
            botOnlyNoXp = true;
            return 0f;
        }

        switch (hardest)
        {
            case Hd2BotDifficulty.Easy: return 0.5f;
            case Hd2BotDifficulty.Medium: return 0.75f;
            default: return 1f;
        }
    }

    /// <summary>A finished match: works out the XP, saves it, and returns the breakdown.</summary>
    public static MatchXp AwardMatch(int score, Outcome outcome, float factor, bool botOnlyNoXp)
    {
        var result = new MatchXp
        {
            Score = Mathf.Max(0, score),
            Finish = FinishXp,
            OutcomeBonus = outcome == Outcome.Win ? WinXp : outcome == Outcome.Draw ? DrawXp : LossXp,
            Factor = factor,
            BotOnlyNoXp = botOnlyNoXp
        };

        int earned = Mathf.RoundToInt((result.Score + result.Finish + result.OutcomeBonus) * factor);
        if (outcome == Outcome.Win && factor > 0f)
        {
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            if (PlayerPrefs.GetString(FirstWinDayKey, "") != today)
            {
                PlayerPrefs.SetString(FirstWinDayKey, today);
                result.FirstWin = FirstWinOfDayXp;
                earned += FirstWinOfDayXp;
            }
        }

        PlayerPrefs.SetInt(MatchesKey, Matches + 1);
        if (outcome == Outcome.Win)
            PlayerPrefs.SetInt(WinsKey, Wins + 1);
        return Add(result, earned);
    }

    /// <summary>Leaving before the end: half the score so far, no bonuses.</summary>
    public static MatchXp AwardLeftEarly(int scoreSoFar, float factor, bool botOnlyNoXp)
    {
        var result = new MatchXp
        {
            Score = Mathf.Max(0, scoreSoFar),
            Factor = factor,
            BotOnlyNoXp = botOnlyNoXp,
            LeftEarly = true
        };

        int earned = Mathf.RoundToInt(result.Score * LeaveEarlyShare * factor);
        PlayerPrefs.SetInt(MatchesKey, Matches + 1);
        return Add(result, earned);
    }

    static MatchXp Add(MatchXp result, int earned)
    {
        long before = TotalXp;
        // XP past the level cap is not banked (see the class summary).
        long after = Math.Min(before + Math.Max(0, earned), XpToReach(MaxLevel));
        result.Total = (int)(after - before);
        result.LevelBefore = LevelFor(before);
        result.LevelAfter = LevelFor(after);
        TotalXp = after;
        PlayerPrefs.Save();
        Changed?.Invoke();
        return result;
    }
}
