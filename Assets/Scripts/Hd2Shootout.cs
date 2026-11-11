using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Team deathmatch. First team to 50 kills wins. The clock is 10 minutes.
/// At zero the leader wins, or the match is a draw.
/// </summary>
[DefaultExecutionOrder(100)]
public class Hd2Shootout : MonoBehaviour
{
    public const int KillsToWin = 50;
    public const int RosterSize = 5;
    public const float MatchSeconds = 600f;
    public const float GateSeconds = 5.5f;

    public static bool Present { get; private set; }
    public static bool Decided { get; private set; }
    public static int RedKills { get; private set; }
    public static int BlueKills { get; private set; }
    public static float TimeLeft { get; private set; } = MatchSeconds;

    TextMesh banner;
    Transform bannerAnchor;
    AudioSource voice;
    AudioClip buzzer;
    AudioClip matchBegins;
    bool gatesOpen;
    float gatesOpenAt;
    float matchEndsAt;
    bool bannerPending;
    Hd2Team bannerWinner;

    void Awake()
    {
        Present = true;
        voice = gameObject.AddComponent<AudioSource>();
        voice.playOnAwake = false;
        voice.loop = false;
        voice.spatialBlend = 0f;
        voice.dopplerLevel = 0f;
        buzzer = Resources.Load<AudioClip>("Audio/Buzzer");
        matchBegins = Resources.Load<AudioClip>("Audio/MatchBegins");
    }

    void OnDestroy()
    {
        if (Present)
            Present = false;
    }

    void Start()
    {
        Decided = false;
        RedKills = 0;
        BlueKills = 0;
        BalanceTeams();
        matchEndsAt = Time.time + MatchSeconds;
        TimeLeft = MatchSeconds;
        Hd2SpawnField.SetLocked(true);
        gatesOpen = false;
        gatesOpenAt = Time.time + GateSeconds;
        if (voice != null && matchBegins != null)
        {
            voice.clip = matchBegins;
            voice.Play();
        }

        var players = FindObjectsByType<Hd2Locomotion>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] != null)
                players[i].ReturnToBase();
        }

        var bots = FindObjectsByType<Hd2EnemyBot>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < bots.Length; i++)
        {
            if (bots[i] != null)
                bots[i].ReturnToBase();
        }
    }

    void LateUpdate()
    {
        if (!Decided)
        {
            TimeLeft = matchEndsAt - Time.time;
            if (TimeLeft <= 0f)
            {
                TimeLeft = 0f;
                FinishByClock();
            }
        }

        if (!gatesOpen && Time.time >= gatesOpenAt)
        {
            gatesOpen = true;
            Hd2SpawnField.SetLocked(false);
        }

        if (bannerPending && banner == null)
            ShowBanner(bannerWinner);
        PlaceBanner();
    }

    void BalanceTeams()
    {
        var players = new List<Hd2Locomotion>();
        var foundPlayers = FindObjectsByType<Hd2Locomotion>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < foundPlayers.Length; i++)
        {
            if (foundPlayers[i] != null)
                players.Add(foundPlayers[i]);
        }

        for (int i = players.Count - 1; i > 0; i--)
        {
            int swap = Random.Range(0, i + 1);
            Hd2Locomotion hold = players[i];
            players[i] = players[swap];
            players[swap] = hold;
        }

        int redHumans = 0;
        int blueHumans = 0;
        for (int i = 0; i < players.Count; i++)
        {
            Hd2Team side;
            if (redHumans < blueHumans)
                side = Hd2Team.Red;
            else if (blueHumans < redHumans)
                side = Hd2Team.Blue;
            else
                side = Random.value < 0.5f ? Hd2Team.Red : Hd2Team.Blue;

            players[i].team = side;
            players[i].teamColor = Hd2Locomotion.ArmorOf(side);
            if (side == Hd2Team.Red)
                redHumans++;
            else
                blueHumans++;
        }

        Hd2Locomotion.PaintTeamRoster();
        var spawns = FindObjectsByType<Hd2SpawnPoint>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        var bots = new List<Hd2EnemyBot>();
        var foundBots = FindObjectsByType<Hd2EnemyBot>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < foundBots.Length; i++)
        {
            if (foundBots[i] != null)
                bots.Add(foundBots[i]);
        }

        PlaceSide(Hd2Team.Red, players, bots, spawns);
        PlaceSide(Hd2Team.Blue, players, bots, spawns);
    }

    static void PlaceSide(Hd2Team side, List<Hd2Locomotion> players, List<Hd2EnemyBot> bots, Hd2SpawnPoint[] spawns)
    {
        var points = new List<Hd2SpawnPoint>();
        for (int i = 0; i < spawns.Length; i++)
        {
            if (spawns[i] != null && spawns[i].team == side)
                points.Add(spawns[i]);
        }

        points.Sort((a, b) => a.index.CompareTo(b.index));
        int seats = points.Count < RosterSize ? points.Count : RosterSize;
        var humans = new List<Hd2Locomotion>();
        for (int i = 0; i < players.Count; i++)
        {
            if (players[i] != null && players[i].team == side)
                humans.Add(players[i]);
        }

        int humanSeats = humans.Count < seats ? humans.Count : seats;
        for (int i = 0; i < humanSeats; i++)
            StandHuman(humans[i], points[i].transform);

        var mine = new List<Hd2EnemyBot>();
        for (int i = 0; i < bots.Count; i++)
        {
            if (bots[i] != null && bots[i].team == side)
                mine.Add(bots[i]);
        }

        int need = seats - humanSeats;
        Hd2EnemyBot template = bots.Count > 0 ? bots[0] : null;
        Transform parent = template != null ? template.transform.parent : null;
        for (int i = 0; i < need; i++)
        {
            int rank = humanSeats + i;
            Transform spot = points[rank].transform;
            Hd2EnemyBot bot = i < mine.Count ? mine[i] : CreateBot(template, parent, side, spot);
            if (bot == null)
                continue;
            Color color = side == Hd2Team.Red ? Hd2Locomotion.RedShade(rank) : Hd2Locomotion.BlueShade(rank);
            bot.TakePlace(side, color, rank, spot);
            bot.displayName = (side == Hd2Team.Red ? "red " : "blue ") + (i + 1);
        }

        for (int i = need; i < mine.Count; i++)
        {
            if (mine[i] != null)
                Destroy(mine[i].gameObject);
        }
    }

    static void StandHuman(Hd2Locomotion player, Transform spot)
    {
        if (player == null || spot == null)
            return;

        player.transform.SetPositionAndRotation(spot.position, spot.rotation);
        Hd2Health life = player.GetComponent<Hd2Health>();
        if (life != null)
            life.PinSpawn(spot.position, spot.rotation);
    }

    static Hd2EnemyBot CreateBot(Hd2EnemyBot template, Transform parent, Hd2Team side, Transform spot)
    {
        var go = new GameObject(side == Hd2Team.Red ? "Red bot" : "Blue bot");
        if (parent != null)
            go.transform.SetParent(parent, false);
        if (spot != null)
            go.transform.SetPositionAndRotation(spot.position, spot.rotation);

        var bot = go.AddComponent<Hd2EnemyBot>();
        if (template != null)
        {
            bot.robotPrefab = template.robotPrefab;
            bot.pistolModel = template.pistolModel;
        }

        bot.team = side;
        bot.useBodyColor = true;
        return bot;
    }

    public static void NoteKill(Hd2Team team)
    {
        if (!Present || Decided || team == Hd2Team.None)
            return;

        if (team == Hd2Team.Red)
            RedKills++;
        else
            BlueKills++;

        if (RedKills >= KillsToWin)
            Finish(Hd2Team.Red);
        else if (BlueKills >= KillsToWin)
            Finish(Hd2Team.Blue);
    }

    static void FinishByClock()
    {
        Hd2Team winner = Hd2Team.None;
        if (RedKills > BlueKills)
            winner = Hd2Team.Red;
        else if (BlueKills > RedKills)
            winner = Hd2Team.Blue;
        Finish(winner);
    }

    static void Finish(Hd2Team winner)
    {
        Hd2Shootout match = FindFirstObjectByType<Hd2Shootout>();
        if (match == null)
            return;
        match.End(winner);
    }

    public void StopMatch()
    {
        if (Decided)
            return;

        Decided = true;
        TimeLeft = Mathf.Max(0f, TimeLeft);
        if (voice != null)
            voice.Stop();
    }

    void End(Hd2Team winner)
    {
        if (Decided)
            return;

        Decided = true;
        TimeLeft = Mathf.Max(0f, TimeLeft);
        if (voice != null)
        {
            voice.Stop();
            if (buzzer != null)
            {
                voice.clip = buzzer;
                voice.Play();
            }
        }

        ShowBanner(winner);
    }

    Camera PlayerView()
    {
        var players = FindObjectsByType<Hd2Locomotion>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        Camera view = null;
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] == null)
                continue;
            Camera cam = players[i].GetComponentInChildren<Camera>();
            if (cam == null || !cam.isActiveAndEnabled)
                continue;
            if (cam.CompareTag("MainCamera"))
                return cam;
            if (view == null)
                view = cam;
        }

        return view;
    }

    void ShowBanner(Hd2Team winner)
    {
        bannerWinner = winner;
        bannerPending = true;
        Camera view = PlayerView();
        if (view == null)
            return;

        bannerPending = false;

        bannerAnchor = view.transform;
        var bannerObject = new GameObject("ShootoutResult");
        bannerObject.transform.SetParent(bannerAnchor, false);
        bannerObject.transform.localPosition = new Vector3(0f, -0.08f, 2f);
        bannerObject.transform.localRotation = Quaternion.identity;
        banner = bannerObject.AddComponent<TextMesh>();
        banner.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        banner.fontSize = 64;
        banner.characterSize = 0.045f;
        banner.anchor = TextAnchor.MiddleCenter;
        banner.alignment = TextAlignment.Center;

        Hd2Locomotion player = view.GetComponentInParent<Hd2Locomotion>();
        if (winner == Hd2Team.None)
        {
            banner.color = Color.white;
            banner.text = "Draw";
        }
        else if (player != null && player.team == winner)
        {
            banner.color = winner == Hd2Team.Red ? Hd2Locomotion.RedArmor : Hd2Locomotion.BlueArmor;
            banner.text = "You win";
        }
        else if (player != null)
        {
            banner.color = winner == Hd2Team.Red ? Hd2Locomotion.RedArmor : Hd2Locomotion.BlueArmor;
            banner.text = "You lose";
        }
        else if (winner == Hd2Team.Red)
        {
            banner.color = Hd2Locomotion.RedArmor;
            banner.text = "Red wins";
        }
        else
        {
            banner.color = Hd2Locomotion.BlueArmor;
            banner.text = "Blue wins";
        }

        var renderer = banner.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            if (banner.font != null)
                renderer.sharedMaterial = banner.font.material;
        }
    }

    void PlaceBanner()
    {
        if (banner == null || bannerAnchor == null)
            return;
        banner.transform.localPosition = new Vector3(0f, -0.08f, 2f);
        banner.transform.localRotation = Quaternion.identity;
    }
}
