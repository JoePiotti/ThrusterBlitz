using System.Collections.Generic;

/// <summary>
/// Cosmetics earned only by levelling up. None of these is ever sold: every id starts with
/// "level." and a store catalog must reject any item for which IsLevelReward is true, so a
/// bought cosmetic can never be the same item as an earned one.
///
/// One reward every 5 levels to MaxLevel. The tier badge itself comes with every tenth level.
/// The Customize tab will list these (it says "coming soon" for now); nothing equips them yet.
/// When MaxLevel goes past 100, add rewards above 100 here.
/// </summary>
public static class Hd2LevelRewards
{
    public const string IdPrefix = "level.";

    public enum Kind { Badge, NameColor, ThrusterTrail, ArmorTrim, VictoryPose }

    public struct Reward
    {
        public int Level;
        public string Id;
        public string Name;
        public Kind Kind;
    }

    public static readonly Reward[] All =
    {
        R(5,   "namecolor.steel",     "Steel Name",              Kind.NameColor),
        R(10,  "badge.pilot",         "Pilot Badge",             Kind.Badge),
        R(15,  "trail.mint",          "Mint Thruster Trail",     Kind.ThrusterTrail),
        R(20,  "badge.thruster",      "Thruster Badge",          Kind.Badge),
        R(25,  "trim.cyan",           "Cyan Armor Trim",         Kind.ArmorTrim),
        R(30,  "badge.striker",       "Striker Badge",           Kind.Badge),
        R(35,  "namecolor.azure",     "Azure Name",              Kind.NameColor),
        R(40,  "badge.ace",           "Ace Badge",               Kind.Badge),
        R(45,  "trail.violet",        "Violet Thruster Trail",   Kind.ThrusterTrail),
        R(50,  "badge.vanguard",      "Vanguard Badge",          Kind.Badge),
        R(55,  "pose.salute",         "Salute Victory Pose",     Kind.VictoryPose),
        R(60,  "badge.commander",     "Commander Badge",         Kind.Badge),
        R(65,  "trim.crimson",        "Crimson Armor Trim",      Kind.ArmorTrim),
        R(70,  "badge.elite",         "Elite Badge",             Kind.Badge),
        R(75,  "trail.ember",         "Ember Thruster Trail",    Kind.ThrusterTrail),
        R(80,  "badge.legend",        "Legend Badge",            Kind.Badge),
        R(85,  "namecolor.gold",      "Gold Name",               Kind.NameColor),
        R(90,  "badge.blitzmaster",   "Blitz Master Badge",      Kind.Badge),
        R(95,  "trim.gold",           "Gold Armor Trim",         Kind.ArmorTrim),
        R(100, "pose.blitzmaster",    "Blitz Master Victory Pose", Kind.VictoryPose),
    };

    static Reward R(int level, string id, string name, Kind kind)
    {
        return new Reward { Level = level, Id = IdPrefix + id, Name = name, Kind = kind };
    }

    public static bool IsLevelReward(string id)
    {
        return !string.IsNullOrEmpty(id) && id.StartsWith(IdPrefix);
    }

    public static bool Unlocked(Reward reward, int level)
    {
        return level >= reward.Level;
    }

    /// <summary>Rewards unlocked by going from one level to another (for the level-up line).</summary>
    public static List<Reward> Between(int fromLevel, int toLevel)
    {
        var list = new List<Reward>();
        for (int i = 0; i < All.Length; i++)
        {
            if (All[i].Level > fromLevel && All[i].Level <= toLevel)
                list.Add(All[i]);
        }

        return list;
    }

    /// <summary>The next reward above this level, or false at the end of the list.</summary>
    public static bool Next(int level, out Reward reward)
    {
        for (int i = 0; i < All.Length; i++)
        {
            if (All[i].Level > level)
            {
                reward = All[i];
                return true;
            }
        }

        reward = default;
        return false;
    }
}
