using UnityEngine;

/// <summary>
/// Rome domination. A team scores when A, B, and C are all that team's color
/// for six seconds. First to three scores wins. The match clock runs for 15 minutes.
/// At zero the leader wins, or the match is a draw. Each round starts like the
/// opening: bases shut, match countdown, and everyone respawns at their base.
/// That team's countdown plays while a hold runs. The other team taking a point
/// cuts it off with the countered clip. The buzzer plays when the match ends.
/// </summary>
[DefaultExecutionOrder(100)]
public class Hd2Domination : MonoBehaviour
{
    public const float HoldSeconds = 6f;
    public const float GateSeconds = 5.5f;
    public const int PointsToWin = 3;
    public const float MatchSeconds = 900f;

    public static bool Decided { get; private set; }
    public static int RedPoints { get; private set; }
    public static int BluePoints { get; private set; }
    public static float TimeLeft { get; private set; } = MatchSeconds;

    Hd2TeamButton pointA;
    Hd2TeamButton pointB;
    Hd2TeamButton pointC;
    Hd2Team pending = Hd2Team.None;
    float heldFor;
    TextMesh banner;
    Transform bannerAnchor;
    AudioSource voice;
    AudioSource matchVoice;
    AudioClip redCountdown;
    AudioClip blueCountdown;
    AudioClip countered;
    AudioClip buzzer;
    AudioClip matchBegins;
    bool gatesOpen;
    float gatesOpenAt;
    float matchEndsAt;

    void Awake()
    {
        voice = gameObject.AddComponent<AudioSource>();
        voice.playOnAwake = false;
        voice.loop = false;
        voice.spatialBlend = 0f;
        voice.dopplerLevel = 0f;
        redCountdown = Resources.Load<AudioClip>("Audio/RedCountdown");
        blueCountdown = Resources.Load<AudioClip>("Audio/BlueCountdown");
        countered = Resources.Load<AudioClip>("Audio/Countered");
        buzzer = Resources.Load<AudioClip>("Audio/Buzzer");

        matchVoice = gameObject.AddComponent<AudioSource>();
        matchVoice.playOnAwake = false;
        matchVoice.loop = false;
        matchVoice.spatialBlend = 0f;
        matchVoice.dopplerLevel = 0f;
        matchBegins = Resources.Load<AudioClip>("Audio/MatchBegins");
    }

    void Start()
    {
        Decided = false;
        RedPoints = 0;
        BluePoints = 0;
        matchEndsAt = Time.time + MatchSeconds;
        TimeLeft = MatchSeconds;
        BeginRound();
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

        if (!Application.isPlaying || Decided)
        {
            PlaceBanner();
            return;
        }

        if (!gatesOpen || !Ready())
            return;

        Hd2Team team = SharedTeam();
        if (team == Hd2Team.None)
        {
            if (pending != Hd2Team.None)
            {
                if (OwnedBy(Other(pending)))
                    Play(countered);
                else
                    StopVoice();
            }

            pending = Hd2Team.None;
            heldFor = 0f;
            return;
        }

        if (pending != team)
        {
            pending = team;
            heldFor = 0f;
            Play(team == Hd2Team.Red ? redCountdown : blueCountdown);
        }

        heldFor += Time.deltaTime;
        if (heldFor >= HoldSeconds)
            ScoreRound(team);
    }

    void BeginRound()
    {
        pending = Hd2Team.None;
        heldFor = 0f;
        StopVoice();
        var points = Hd2TeamButton.All;
        for (int i = 0; i < points.Count; i++)
        {
            if (points[i] != null)
                points[i].ResetClaim();
        }

        Hd2SpawnField.SetLocked(true);
        gatesOpen = false;
        gatesOpenAt = Time.time + GateSeconds;
        if (matchVoice != null && matchBegins != null)
        {
            matchVoice.Stop();
            matchVoice.clip = matchBegins;
            matchVoice.Play();
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

    void ScoreRound(Hd2Team team)
    {
        if (Decided)
            return;

        if (team == Hd2Team.Red)
            RedPoints++;
        else if (team == Hd2Team.Blue)
            BluePoints++;

        pending = Hd2Team.None;
        heldFor = 0f;
        StopVoice();
        if (RedPoints >= PointsToWin || BluePoints >= PointsToWin)
            Finish(team);
        else
            BeginRound();
    }

    void FinishByClock()
    {
        Hd2Team winner = Hd2Team.None;
        if (RedPoints > BluePoints)
            winner = Hd2Team.Red;
        else if (BluePoints > RedPoints)
            winner = Hd2Team.Blue;
        Finish(winner);
    }

    void Finish(Hd2Team winner)
    {
        if (Decided)
            return;

        Decided = true;
        StopVoice();
        if (matchVoice != null)
            matchVoice.Stop();
        Play(buzzer);
        ShowBanner(winner);
    }

    static Hd2Team Other(Hd2Team team)
    {
        return team == Hd2Team.Red ? Hd2Team.Blue : Hd2Team.Red;
    }

    bool OwnedBy(Hd2Team team)
    {
        return pointA.Owner == team || pointB.Owner == team || pointC.Owner == team;
    }

    void Play(AudioClip clip)
    {
        if (voice == null || clip == null)
            return;
        voice.Stop();
        voice.clip = clip;
        voice.Play();
    }

    void StopVoice()
    {
        if (voice != null && voice.isPlaying)
            voice.Stop();
    }

    bool Ready()
    {
        if (pointA != null && pointB != null && pointC != null)
            return true;

        var all = Hd2TeamButton.All;
        for (int i = 0; i < all.Count; i++)
        {
            Hd2TeamButton point = all[i];
            if (point == null)
                continue;
            if (point.Label == "A")
                pointA = point;
            else if (point.Label == "B")
                pointB = point;
            else if (point.Label == "C")
                pointC = point;
        }

        return pointA != null && pointB != null && pointC != null;
    }

    Hd2Team SharedTeam()
    {
        Hd2Team team = pointA.Owner;
        if (team == Hd2Team.None || pointB.Owner != team || pointC.Owner != team)
            return Hd2Team.None;
        return team;
    }

    void ShowBanner(Hd2Team team)
    {
        Camera view = Camera.main;
        if (view == null)
        {
            var players = FindObjectsByType<Hd2Locomotion>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (players != null && players.Length > 0)
                view = players[0].GetComponentInChildren<Camera>();
        }

        if (view == null)
            return;

        bannerAnchor = view.transform;
        var bannerObject = new GameObject("DominationWinner");
        bannerObject.transform.SetParent(bannerAnchor, false);
        bannerObject.transform.localPosition = new Vector3(0f, -0.08f, 2f);
        bannerObject.transform.localRotation = Quaternion.identity;
        banner = bannerObject.AddComponent<TextMesh>();
        banner.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        banner.fontSize = 64;
        banner.characterSize = 0.045f;
        banner.anchor = TextAnchor.MiddleCenter;
        banner.alignment = TextAlignment.Center;
        if (team == Hd2Team.Red)
        {
            banner.color = Hd2Locomotion.RedArmor;
            banner.text = "Red wins";
        }
        else if (team == Hd2Team.Blue)
        {
            banner.color = Hd2Locomotion.BlueArmor;
            banner.text = "Blue wins";
        }
        else
        {
            banner.color = Color.white;
            banner.text = "Draw";
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
