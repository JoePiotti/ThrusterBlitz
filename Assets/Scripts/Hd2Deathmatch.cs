using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Free-for-all deathmatch. Eight fighters, each a different color.
/// First to 40 kills wins. At 10 minutes the leader wins.
/// </summary>
[DefaultExecutionOrder(-100)]
public class Hd2Deathmatch : MonoBehaviour
{
    public const int Fighters = 8;
    public const int KillsToWin = 40;
    public const float MatchSeconds = 600f;

    public static bool Over { get; private set; }

    struct ColorName
    {
        public string Name;
        public Color Color;
    }

    static readonly ColorName[] Palette =
    {
        new ColorName { Name = "White", Color = Color.white },
        new ColorName { Name = "Red", Color = new Color(0.85f, 0.12f, 0.12f) },
        new ColorName { Name = "Blue", Color = new Color(0.15f, 0.35f, 0.95f) },
        new ColorName { Name = "Green", Color = new Color(0.1f, 0.72f, 0.25f) },
        new ColorName { Name = "Yellow", Color = new Color(0.95f, 0.85f, 0.1f) },
        new ColorName { Name = "Purple", Color = new Color(0.55f, 0.2f, 0.85f) },
        new ColorName { Name = "Orange", Color = new Color(0.95f, 0.45f, 0.08f) },
        new ColorName { Name = "Black", Color = new Color(0.08f, 0.08f, 0.08f) }
    };

    class Fighter
    {
        public Transform Body;
        public Hd2Health Health;
        public string Name;
        public Color Color;
        public int Kills;
        public bool IsPlayer;
    }

    readonly List<Fighter> fighters = new List<Fighter>();
    float matchEndsAt;
    bool hooked;
    TextMesh banner;
    Transform bannerAnchor;

    void Awake()
    {
        Over = false;
    }

    void Start()
    {
        var director = GetComponent<Hd2SpawnDirector>();
        if (director == null)
            director = gameObject.AddComponent<Hd2SpawnDirector>();

        Hd2Locomotion player = FindPlayer();
        EnsureBots();
        Place(director, player);
        Paint(player);
        matchEndsAt = Time.time + MatchSeconds;
    }

    void Update()
    {
        if (!hooked)
        {
            HookHealth();
            hooked = true;
        }

        if (Over)
            return;

        if (Time.time >= matchEndsAt)
            Finish("Time");
    }

    void LateUpdate()
    {
        if (banner == null || bannerAnchor == null)
            return;
        banner.transform.position = bannerAnchor.position + bannerAnchor.forward * 2f;
        banner.transform.rotation = bannerAnchor.rotation;
    }

    static Hd2Locomotion FindPlayer()
    {
        var players = FindObjectsByType<Hd2Locomotion>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        if (players == null || players.Length == 0)
            return null;
        System.Array.Sort(players, (a, b) => string.CompareOrdinal(a.name, b.name));
        return players[0];
    }

    void EnsureBots()
    {
        var bots = FindObjectsByType<Hd2EnemyBot>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        if (bots == null || bots.Length == 0)
            return;

        System.Array.Sort(bots, (a, b) => string.CompareOrdinal(a.name, b.name));
        Hd2EnemyBot template = bots[0];
        Transform root = template.transform.parent != null ? template.transform.parent : transform;
        int count = bots.Length;
        int next = count + 1;
        while (count < Fighters - 1)
        {
            var go = new GameObject("EnemyBot " + next);
            go.transform.SetParent(root, false);
            var bot = go.AddComponent<Hd2EnemyBot>();
            bot.robotPrefab = template.robotPrefab;
            bot.pistolModel = template.pistolModel;
            count++;
            next++;
        }
    }

    void Place(Hd2SpawnDirector director, Hd2Locomotion player)
    {
        if (player != null)
        {
            Transform spawn = director.PickOpening(0);
            director.MoveToSpawn(player.transform, spawn);
            var health = player.GetComponent<Hd2Health>();
            if (health != null)
                health.RememberSpawn();
        }

        var bots = FindObjectsByType<Hd2EnemyBot>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        System.Array.Sort(bots, (a, b) => string.CompareOrdinal(a.name, b.name));
        int used = 0;
        for (int i = 0; i < bots.Length && used < Fighters - 1; i++)
        {
            Transform spawn = director.PickOpening(used + 1);
            if (spawn != null)
                bots[i].transform.SetPositionAndRotation(spawn.position, spawn.rotation);
            used++;
        }
    }

    void Paint(Hd2Locomotion player)
    {
        fighters.Clear();
        if (player != null)
            fighters.Add(MakeFighter(player.transform, Palette[0], true));

        var bots = FindObjectsByType<Hd2EnemyBot>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        System.Array.Sort(bots, (a, b) => string.CompareOrdinal(a.name, b.name));
        int colorIndex = 1;
        for (int i = 0; i < bots.Length && fighters.Count < Fighters; i++)
        {
            ColorName paint = Palette[colorIndex % Palette.Length];
            colorIndex++;
            bots[i].bodyColor = paint.Color;
            bots[i].useBodyColor = true;
            fighters.Add(MakeFighter(bots[i].transform, paint, false));
        }

        if (player != null)
        {
            player.teamColor = Palette[0].Color;
            var body = player.GetComponent<Hd2Body>();
            if (body != null)
                body.SetArmorColor(Palette[0].Color);
        }
    }

    static Fighter MakeFighter(Transform body, ColorName paint, bool isPlayer)
    {
        return new Fighter
        {
            Body = body,
            Name = paint.Name,
            Color = paint.Color,
            IsPlayer = isPlayer
        };
    }

    void HookHealth()
    {
        for (int i = 0; i < fighters.Count; i++)
        {
            Fighter fighter = fighters[i];
            if (fighter.Body == null)
                continue;
            fighter.Health = fighter.Body.GetComponent<Hd2Health>();
            if (fighter.Health == null)
                continue;
            fighter.Health.Changed += OnHealth;
        }
    }

    void OnHealth(Hd2Health current)
    {
        if (Over || current == null || current.Health > 0f)
            return;

        Fighter victim = FindFighter(current.transform);
        if (victim == null)
            return;

        Transform killer = current.LastAttacker;
        Fighter scorer = killer != null ? FindFighter(killer) : null;
        if (scorer != null && scorer != victim)
        {
            scorer.Kills++;
            if (scorer.Kills >= KillsToWin)
                Finish(null);
        }
    }

    Fighter FindFighter(Transform body)
    {
        if (body == null)
            return null;
        Transform root = body.root;
        for (int i = 0; i < fighters.Count; i++)
        {
            Fighter fighter = fighters[i];
            if (fighter.Body == null)
                continue;
            if (fighter.Body == body || fighter.Body == root || fighter.Body.root == root)
                return fighter;
        }

        return null;
    }

    void Finish(string reason)
    {
        if (Over)
            return;
        Over = true;

        for (int i = 0; i < fighters.Count; i++)
        {
            if (fighters[i].Health != null)
                fighters[i].Health.autoRespawn = false;
        }

        int best = -1;
        int leaders = 0;
        for (int i = 0; i < fighters.Count; i++)
        {
            if (fighters[i].Kills > best)
            {
                best = fighters[i].Kills;
                leaders = 1;
            }
            else if (fighters[i].Kills == best)
            {
                leaders++;
            }
        }

        string headline;
        Color tint = Color.white;
        if (best <= 0)
        {
            headline = "Draw\nNo kills";
        }
        else if (leaders > 1)
        {
            headline = "Tie\n" + LeaderNames(best) + "\n" + best + " kills";
        }
        else
        {
            Fighter winner = Leader(best);
            tint = winner.Color;
            headline = winner.IsPlayer
                ? "You win\n" + winner.Name + ", " + winner.Kills + " kills"
                : winner.Name + " wins\n" + winner.Kills + " kills";
        }

        if (!string.IsNullOrEmpty(reason))
            headline = reason + "\n" + headline;
        ShowBanner(headline, tint);
    }

    Fighter Leader(int kills)
    {
        for (int i = 0; i < fighters.Count; i++)
        {
            if (fighters[i].Kills == kills)
                return fighters[i];
        }

        return fighters.Count > 0 ? fighters[0] : null;
    }

    string LeaderNames(int kills)
    {
        string names = "";
        for (int i = 0; i < fighters.Count; i++)
        {
            if (fighters[i].Kills != kills)
                continue;
            if (names.Length > 0)
                names += " and ";
            names += fighters[i].IsPlayer ? "You" : fighters[i].Name;
        }

        return names;
    }

    void ShowBanner(string message, Color tint)
    {
        Camera view = Camera.main;
        if (view == null)
        {
            var cameras = FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (cameras != null && cameras.Length > 0)
                view = cameras[0];
        }

        if (view == null)
            return;

        bannerAnchor = view.transform;
        var bannerObject = new GameObject("Winner");
        banner = bannerObject.AddComponent<TextMesh>();
        banner.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        banner.fontSize = 64;
        banner.characterSize = 0.045f;
        banner.anchor = TextAnchor.MiddleCenter;
        banner.alignment = TextAlignment.Center;
        banner.color = tint;
        banner.text = message;
        LateUpdate();
    }
}
