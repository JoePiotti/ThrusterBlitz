using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// This player's name and optional clan tag, saved on the headset.
/// Name: 2 to 20 characters. Clan tag: up to 4 letters, numbers or periods, shown in brackets to the left
/// of the name. BOT is reserved for bots. Both are checked against the blocklist in
/// Resources/Hd2NameBlocklist.txt.
/// The name should default to the Meta account name. That needs the Meta Platform SDK,
/// which is not in the project yet, so until then the default is Player plus four digits.
/// </summary>
public static class Hd2Identity
{
    public const int NameMin = 2;
    public const int NameMax = 20;
    public const int ClanMax = 4;
    public const string BotClan = "BOT";

    const string NameKey = "Hd2PlayerName";
    const string ClanKey = "Hd2PlayerClan";

    static string playerName;
    static string clan;

    /// <summary>Raised when this player's name or clan tag changes.</summary>
    public static event System.Action Changed;

    public static string Name
    {
        get
        {
            Load();
            return playerName;
        }
    }

    public static string Clan
    {
        get
        {
            Load();
            return clan;
        }
    }

    /// <summary>Name with the clan tag in front, for example "[ABC] Joe".</summary>
    public static string Display => Format(Clan, Name);

    public static string Format(string clanTag, string name)
    {
        string shown = string.IsNullOrEmpty(name) ? "Player" : name;
        return string.IsNullOrEmpty(clanTag) ? shown : "[" + clanTag + "] " + shown;
    }

    static void Load()
    {
        if (playerName != null)
            return;

        playerName = PlayerPrefs.GetString(NameKey, "");
        clan = PlayerPrefs.GetString(ClanKey, "");
        if (CheckName(playerName) != null)
        {
            playerName = DefaultName();
            PlayerPrefs.SetString(NameKey, playerName);
        }

        if (CheckClan(clan) != null)
            clan = "";
        PlayerPrefs.Save();
    }

    static string DefaultName()
    {
        // Meta account name goes here once the Meta Platform SDK is in.
        return "Player" + Random.Range(1000, 10000);
    }

    /// <summary>Saves a new name. Returns null on success, or why it was refused.</summary>
    public static string SetName(string value)
    {
        value = Clean(value);
        string problem = CheckName(value);
        if (problem != null)
            return problem;

        Load();
        playerName = value;
        PlayerPrefs.SetString(NameKey, value);
        PlayerPrefs.Save();
        Changed?.Invoke();
        return null;
    }

    /// <summary>Saves a new clan tag (empty removes it). Returns null on success, or why it was refused.</summary>
    public static string SetClan(string value)
    {
        value = Clean(value);
        string problem = CheckClan(value);
        if (problem != null)
            return problem;

        Load();
        clan = value;
        PlayerPrefs.SetString(ClanKey, value);
        PlayerPrefs.Save();
        Changed?.Invoke();
        return null;
    }

    /// <summary>Trims and collapses repeated spaces.</summary>
    public static string Clean(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        var built = new StringBuilder(value.Length);
        bool space = false;
        foreach (char c in value.Trim())
        {
            if (c == ' ')
            {
                if (!space)
                    built.Append(c);
                space = true;
                continue;
            }

            space = false;
            built.Append(c);
        }

        return built.ToString();
    }

    public static bool NameChar(char c)
    {
        return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')
            || c == ' ' || c == '.' || c == '-' || c == '_' || c == '\'';
    }

    public static bool ClanChar(char c)
    {
        return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '.';
    }

    /// <summary>Null when the name is allowed, otherwise the reason.</summary>
    public static string CheckName(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length < NameMin)
            return "Name needs at least " + NameMin + " characters";
        if (value.Length > NameMax)
            return "Name can be at most " + NameMax + " characters";
        foreach (char c in value)
        {
            if (!NameChar(c))
                return "Letters, numbers, spaces and . - _ ' only";
        }

        if (Blocked(value))
            return "That name is not allowed";
        return null;
    }

    /// <summary>Null when the clan tag is allowed (empty is allowed), otherwise the reason.</summary>
    public static string CheckClan(string value)
    {
        if (string.IsNullOrEmpty(value))
            return null;
        if (value.Length > ClanMax)
            return "Clan tag can be at most " + ClanMax + " characters";
        foreach (char c in value)
        {
            if (!ClanChar(c))
                return "Clan tag is letters, numbers and periods only";
        }

        if (Squash(value) == "bot")
            return "BOT is reserved for bots";
        if (Blocked(value))
            return "That clan tag is not allowed";
        return null;
    }

    // ---- Blocklist ----

    static List<string> containsWords;
    static HashSet<string> exactWords;

    /// <summary>
    /// True when the text contains a blocked word. Letters are lowercased and common
    /// look-alikes are mapped (0→o, 1→i, 3→e, 4→a, 5→s, 7→t, 8→b, 9→g, @→a, $→s)
    /// before checking, and separators are ignored.
    /// </summary>
    public static bool Blocked(string text)
    {
        LoadBlocklist();
        string squashed = Squash(text);
        if (squashed.Length == 0)
            return false;

        for (int i = 0; i < containsWords.Count; i++)
        {
            if (squashed.Contains(containsWords[i]))
                return true;
        }

        if (exactWords.Contains(squashed))
            return true;

        // Short words only count as whole words, so "Bass", "Hancock" or "Therapist" stay allowed.
        string[] parts = Words(text);
        for (int i = 0; i < parts.Length; i++)
        {
            if (exactWords.Contains(parts[i]))
                return true;
        }

        return false;
    }

    static string[] Words(string text)
    {
        var words = new List<string>();
        var current = new StringBuilder();
        foreach (char raw in text.ToLowerInvariant())
        {
            char c = LookAlike(raw);
            if (c >= 'a' && c <= 'z')
                current.Append(c);
            else if (current.Length > 0)
            {
                words.Add(current.ToString());
                current.Length = 0;
            }
        }

        if (current.Length > 0)
            words.Add(current.ToString());
        return words.ToArray();
    }

    static string Squash(string text)
    {
        var built = new StringBuilder(text.Length);
        foreach (char raw in text.ToLowerInvariant())
        {
            char c = LookAlike(raw);
            if (c >= 'a' && c <= 'z')
                built.Append(c);
        }

        return built.ToString();
    }

    static char LookAlike(char c)
    {
        switch (c)
        {
            case '0': return 'o';
            case '1': return 'i';
            case '3': return 'e';
            case '4': return 'a';
            case '5': return 's';
            case '7': return 't';
            case '8': return 'b';
            case '9': return 'g';
            case '@': return 'a';
            case '$': return 's';
            case '!': return 'i';
            default: return c;
        }
    }

    static void LoadBlocklist()
    {
        if (containsWords != null)
            return;

        containsWords = new List<string>();
        exactWords = new HashSet<string>();
        TextAsset file = Resources.Load<TextAsset>("Hd2NameBlocklist");
        if (file == null)
            return;

        // One word per line. "=word" blocks only the whole word; plain lines block the
        // word anywhere in the name. Lines starting with # are notes.
        string[] lines = file.text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;
            bool exact = line[0] == '=';
            string word = Squash(exact ? line.Substring(1) : line);
            if (word.Length == 0)
                continue;
            if (exact)
                exactWords.Add(word);
            else
                containsWords.Add(word);
        }
    }
}
