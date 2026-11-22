using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Elimination, played in the Shootout scene (chosen on the title screen).
/// Rounds: everyone starts at their base with their team's weapon in both hands and one
/// life. After a 3 s countdown the gates open and the 30 s round clock runs. The round ends
/// when a team has nobody left standing: the other team scores a point. If the clock runs
/// out first, nobody scores and the round is replayed. Every 3 points a team moves up to the
/// next weapon: pistols, shotguns, SMGs, burst, shocks, rockets, lasers. First to 21 wins.
/// The weapon pickups are hidden; health, thrust and drop shield pickups stay.
/// The dead stay down until the next round and get a free camera (Hd2PlayerDeath).
///
/// After each round the announcer calls the scoring team and the score, winner's points
/// first ("Red Team Scores, five, three"), from clips in Resources/Audio/Elimination.
/// </summary>
public class Hd2Elimination : MonoBehaviour
{
    public const int PointsToWin = 21;
    public const int PointsPerWeapon = 3;
    public const float RoundSeconds = 30f;
    public const float CountdownSeconds = 3f;
    public const float BetweenRoundsSeconds = 3f;

    /// <summary>An Elimination match is running in this scene.</summary>
    public static bool Active { get; private set; }
    /// <summary>Fighters who die stay down until the next round.</summary>
    public static bool HoldsDead => Active;
    public static int RedPoints { get; private set; }
    public static int BluePoints { get; private set; }
    /// <summary>Shown under the HUD clock until the next round (DRAW when time runs out).</summary>
    public static string HudNote { get; private set; }
    public static readonly string[] WeaponNames = { "Pistols", "Shotguns", "SMGs", "Burst", "Shocks", "Rockets", "Lasers" };

    enum Phase { Starting, Arming, Seating, Countdown, Live, Between, Over }

    Phase phase = Phase.Starting;
    float phaseEndsAt;
    AudioSource voice;
    AudioClip buzzer;
    readonly System.Func<Transform, bool>[] weapons = new System.Func<Transform, bool>[7];

    void Awake()
    {
        Active = true;
        RedPoints = 0;
        BluePoints = 0;
        voice = gameObject.AddComponent<AudioSource>();
        voice.playOnAwake = false;
        voice.spatialBlend = 0f;
        buzzer = Resources.Load<AudioClip>("Audio/Buzzer");
        HudNote = null;
        HideWeaponPickups();
    }

    void OnDestroy()
    {
        Active = false;
        Hd2GrayscaleFeature.Active = false;
        Hd2Shootout.EliminationMode = false;
    }

    // The scene's WeaponPickups folder is switched off; its pickups still hand out the
    // round's weapons directly.
    void HideWeaponPickups()
    {
        GameObject folder = FindFolder("WeaponPickups");
        if (folder == null)
            return;

        Transform root = folder.transform;
        weapons[1] = Source<Hd2ShotgunPickup>(root, p => p.GiveTo);
        weapons[2] = Source<Hd2SmgPickup>(root, p => p.GiveTo);
        weapons[3] = Source<Hd2BurstPickup>(root, p => p.GiveTo);
        weapons[4] = Source<Hd2ShocksPickup>(root, p => p.GiveTo);
        weapons[5] = Source<Hd2RocketPickup>(root, p => p.GiveTo);
        weapons[6] = Source<Hd2LaserPickup>(root, p => p.GiveTo);
        folder.SetActive(false);
    }

    static System.Func<Transform, bool> Source<T>(Transform root, System.Func<T, System.Func<Transform, bool>> give) where T : Component
    {
        T pickup = root.GetComponentInChildren<T>(true);
        return pickup != null ? give(pickup) : null;
    }

    static GameObject FindFolder(string name)
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            Transform found = Find(roots[i].transform, name);
            if (found != null)
                return found.gameObject;
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

    public static int WeaponOf(Hd2Team team)
    {
        int points = team == Hd2Team.Red ? RedPoints : team == Hd2Team.Blue ? BluePoints : 0;
        return Mathf.Clamp(points / PointsPerWeapon, 0, WeaponNames.Length - 1);
    }

    void Update()
    {
        switch (phase)
        {
            case Phase.Starting:
                // First frame: Hd2Shootout has filled and placed the teams.
                BeginRound();
                break;

            // The reset's old guns are only removed at the end of the frame, and a hand
            // that still holds, say, a shotgun refuses another one. So the round weapons
            // go in one frame after the reset, and bots line them up one frame after that.
            case Phase.Arming:
                ArmEveryone();
                phase = Phase.Seating;
                break;

            case Phase.Seating:
                SeatBots();
                phase = Phase.Countdown;
                phaseEndsAt = Time.time + CountdownSeconds;
                break;

            case Phase.Countdown:
                Show(RoundSeconds);
                if (Time.time >= phaseEndsAt)
                {
                    Hd2SpawnField.SetLocked(false);
                    phase = Phase.Live;
                    phaseEndsAt = Time.time + RoundSeconds;
                }
                break;

            case Phase.Live:
                Show(phaseEndsAt - Time.time);
                CountAlive(out int red, out int blue);
                if (red == 0 || blue == 0)
                    EndRound(red > 0 ? Hd2Team.Red : blue > 0 ? Hd2Team.Blue : Hd2Team.None);
                else if (Time.time >= phaseEndsAt)
                    EndRound(Hd2Team.None);
                break;

            case Phase.Between:
                if (Time.time >= phaseEndsAt)
                    BeginRound();
                break;
        }
    }

    void Show(float timeLeft)
    {
        Hd2Shootout.ShowRound(RedPoints, BluePoints, timeLeft);
    }

    void BeginRound()
    {
        Hd2SpawnField.SetLocked(true);
        HudNote = null;
        Show(RoundSeconds);
        ClearShields();
        ResetPickups();

        var players = FindObjectsByType<Hd2Locomotion>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < players.Length; i++)
        {
            Hd2Locomotion player = players[i];
            if (player == null)
                continue;
            // Back to the seat Hd2Shootout gave this player at the start, not the
            // nearest spawn point to wherever the last round ended.
            var death = player.GetComponent<Hd2PlayerDeath>();
            if (death != null)
                death.RecoverAtSpawn();
            else
                player.ReturnToBase();
        }

        var bots = FindObjectsByType<Hd2EnemyBot>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < bots.Length; i++)
        {
            Hd2EnemyBot bot = bots[i];
            if (bot == null)
                continue;
            bot.ReturnToBase();
            bot.ResetLoadout();
        }

        phase = Phase.Arming;
    }

    void ArmEveryone()
    {
        var players = FindObjectsByType<Hd2Locomotion>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] != null)
                Arm(players[i].leftHand, players[i].rightHand, WeaponOf(players[i].team));
        }

        var bots = FindObjectsByType<Hd2EnemyBot>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < bots.Length; i++)
        {
            if (bots[i] != null)
                Arm(bots[i].LeftHand, bots[i].RightHand, WeaponOf(bots[i].team));
        }
    }

    static void SeatBots()
    {
        var bots = FindObjectsByType<Hd2EnemyBot>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < bots.Length; i++)
        {
            if (bots[i] != null)
                bots[i].SeatHands();
        }
    }

    // Deployed and held drop shields are gone at the start of every round.
    static void ClearShields()
    {
        var shields = FindObjectsByType<Hd2DropShield>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < shields.Length; i++)
        {
            if (shields[i] != null)
                Destroy(shields[i].gameObject);
        }
    }

    // Health, thrust and drop shield pickups are all ready again each round.
    static void ResetPickups()
    {
        var health = FindObjectsByType<Hd2HealthPickup>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < health.Length; i++)
            health[i].ResetForRound();
        var thrust = FindObjectsByType<Hd2ThrustPickup>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < thrust.Length; i++)
            thrust[i].ResetForRound();
        var shields = FindObjectsByType<Hd2DropShieldPickup>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < shields.Length; i++)
            shields[i].ResetForRound();
    }

    // Pistols come back with the reset; any later weapon replaces them in both hands.
    void Arm(Transform left, Transform right, int weapon)
    {
        if (weapon <= 0 || weapon >= weapons.Length || weapons[weapon] == null)
            return;
        if (right != null)
            weapons[weapon](right);
        if (left != null)
            weapons[weapon](left);
    }

    static void CountAlive(out int red, out int blue)
    {
        red = 0;
        blue = 0;
        var players = FindObjectsByType<Hd2Locomotion>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < players.Length; i++)
        {
            Hd2Health life = players[i] != null ? players[i].GetComponent<Hd2Health>() : null;
            if (life == null || life.IsDead)
                continue;
            Tally(players[i].team, ref red, ref blue);
        }

        var bots = FindObjectsByType<Hd2EnemyBot>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < bots.Length; i++)
        {
            if (bots[i] != null && bots[i].Alive)
                Tally(bots[i].team, ref red, ref blue);
        }
    }

    static void Tally(Hd2Team team, ref int red, ref int blue)
    {
        if (team == Hd2Team.Red)
            red++;
        else if (team == Hd2Team.Blue)
            blue++;
    }

    void EndRound(Hd2Team winner)
    {
        if (winner == Hd2Team.Red)
            RedPoints++;
        else if (winner == Hd2Team.Blue)
            BluePoints++;
        Show(0f);

        if (RedPoints >= PointsToWin || BluePoints >= PointsToWin)
        {
            phase = Phase.Over;
            string champ = winner == Hd2Team.Red ? "Red" : "Blue";
            int won = winner == Hd2Team.Red ? RedPoints : BluePoints;
            int lost = winner == Hd2Team.Red ? BluePoints : RedPoints;
            Announce(champ + "Scored", "Number" + won, "Number" + lost, champ + "Wins");
            Hd2Shootout.DeclareWinner(winner);
            return;
        }

        if (winner == Hd2Team.None)
        {
            HudNote = "DRAW";
            if (buzzer != null)
                voice.PlayOneShot(buzzer);
            Queue("RoundDraw");
        }
        else
        {
            string team = winner == Hd2Team.Red ? "Red" : "Blue";
            int mine = winner == Hd2Team.Red ? RedPoints : BluePoints;
            int theirs = winner == Hd2Team.Red ? BluePoints : RedPoints;
            // "Red Team Scores ... five ... three": the round winner's score first.
            Announce(team + "Scored", "Number" + mine, "Number" + theirs);
            if (mine == PointsToWin - 1)
                Queue("MatchPoint" + team);
        }

        phase = Phase.Between;
        phaseEndsAt = Time.time + BetweenRoundsSeconds;
    }

    // ---- Announcer ----
    // Clips in Resources/Audio/Elimination: RedScored, BlueScored, Number0..Number21, and
    // (when recorded) MatchPointRed, MatchPointBlue, RoundDraw, RedWins, BlueWins. Missing
    // clips are skipped. Each clip starts as soon as the one before stops speaking, so the
    // silence recorded at the end of a clip doesn't leave gaps.

    const float LineGap = 0.08f;
    readonly Queue<AudioClip> lines = new Queue<AudioClip>();
    static readonly Dictionary<AudioClip, float> spoken = new Dictionary<AudioClip, float>();
    float nextLineAt;

    void Announce(params string[] names)
    {
        lines.Clear();
        nextLineAt = 0f;
        for (int i = 0; i < names.Length; i++)
            Queue(names[i]);
    }

    void Queue(string name)
    {
        AudioClip clip = Resources.Load<AudioClip>("Audio/Elimination/" + name);
        if (clip != null)
            lines.Enqueue(clip);
    }

    void LateUpdate()
    {
        if (lines.Count == 0 || voice == null || Time.time < nextLineAt)
            return;
        AudioClip clip = lines.Dequeue();
        voice.Stop();
        voice.clip = clip;
        voice.Play();
        nextLineAt = Time.time + SpokenLength(clip) + LineGap;
    }

    // Seconds until the clip's last audible sample, measured once per clip.
    static float SpokenLength(AudioClip clip)
    {
        if (spoken.TryGetValue(clip, out float known))
            return known;

        float length = clip.length;
        if (clip.loadState != AudioDataLoadState.Loaded)
            clip.LoadAudioData();
        int channels = Mathf.Max(1, clip.channels);
        var data = new float[clip.samples * channels];
        if (clip.loadState == AudioDataLoadState.Loaded && clip.GetData(data, 0))
        {
            for (int i = data.Length - 1; i >= 0; i--)
            {
                if (Mathf.Abs(data[i]) > 0.02f)
                {
                    length = Mathf.Min(clip.length, (i / channels) / (float)clip.frequency + 0.05f);
                    break;
                }
            }
        }

        spoken[clip] = length;
        return length;
    }
}
