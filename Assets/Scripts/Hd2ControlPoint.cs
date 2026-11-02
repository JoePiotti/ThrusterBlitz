using UnityEngine;

/// <summary>
/// Control point. One button. Each second it stays a team's color, that team gains one point.
/// First to 250 wins. At the end of the match clock the leader wins, or the match is a draw.
/// The opening still shuts the bases, counts in, and respawns everyone. When the button
/// turns red or blue, that team's line plays in the ear. A new color cuts off the line
/// still playing. When a score passes the other team, and that other score is already
/// above zero, the lead line waits until the current announcer clip finishes.
/// The countered clip does not play.
/// </summary>
[DefaultExecutionOrder(100)]
public class Hd2ControlPoint : MonoBehaviour
{
    public const int PointsToWin = 250;
    public const float MatchSeconds = 900f;
    public const float GateSeconds = 5.5f;

    public static bool Present { get; private set; }
    public static bool Decided { get; private set; }
    public static int RedPoints { get; private set; }
    public static int BluePoints { get; private set; }
    public static float TimeLeft { get; private set; } = MatchSeconds;
    public static Hd2TeamButton Point { get; private set; }

    Hd2TeamButton point;
    Hd2Team scoring = Hd2Team.None;
    Hd2Team announced = Hd2Team.None;
    float second;
    TextMesh banner;
    Transform bannerAnchor;
    AudioSource voice;
    AudioSource matchVoice;
    AudioClip buzzer;
    AudioClip matchBegins;
    AudioClip redTaken;
    AudioClip blueTaken;
    AudioClip redLead;
    AudioClip blueLead;
    AudioClip queuedLead;
    bool gatesOpen;
    float gatesOpenAt;
    float matchEndsAt;

    void OnEnable()
    {
        Present = true;
    }

    void OnDisable()
    {
        if (Present && Point == point)
            Point = null;
        Present = false;
    }

    void Awake()
    {
        voice = gameObject.AddComponent<AudioSource>();
        voice.playOnAwake = false;
        voice.loop = false;
        voice.spatialBlend = 0f;
        voice.dopplerLevel = 0f;
        buzzer = Resources.Load<AudioClip>("Audio/Buzzer");
        redTaken = Resources.Load<AudioClip>("Audio/RedTakenControl");
        blueTaken = Resources.Load<AudioClip>("Audio/BlueTakenControl");
        redLead = Resources.Load<AudioClip>("Audio/RedHasTakenTheLead");
        blueLead = Resources.Load<AudioClip>("Audio/BlueHasTakenTheLead");

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
        scoring = Hd2Team.None;
        announced = Hd2Team.None;
        second = 0f;
        matchEndsAt = Time.time + MatchSeconds;
        TimeLeft = MatchSeconds;
        BeginMatch();
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

        PumpLead();

        EnsurePoint();
        if (point != null)
            Announce(point.Owner);

        if (!gatesOpen || point == null)
            return;

        Hd2Team owner = point.Owner;
        if (owner != Hd2Team.Red && owner != Hd2Team.Blue)
        {
            scoring = Hd2Team.None;
            second = 0f;
            return;
        }

        if (scoring != owner)
        {
            scoring = owner;
            second = 0f;
        }

        second += Time.deltaTime;
        int steps = 0;
        while (second >= 1f && steps < 5)
        {
            second -= 1f;
            steps++;
            if (!Award(owner))
                return;
        }
    }

    void EnsurePoint()
    {
        if (point != null)
            return;

        var all = Hd2TeamButton.All;
        for (int i = 0; i < all.Count; i++)
        {
            Hd2TeamButton button = all[i];
            if (button == null)
                continue;
            point = button;
            Point = button;
            return;
        }
    }

    void Announce(Hd2Team owner)
    {
        if (owner != Hd2Team.Red && owner != Hd2Team.Blue)
        {
            announced = Hd2Team.None;
            return;
        }

        if (announced == owner)
            return;

        announced = owner;
        Play(owner == Hd2Team.Red ? redTaken : blueTaken);
    }

    void Play(AudioClip clip)
    {
        if (voice == null || clip == null)
            return;

        voice.Stop();
        voice.clip = clip;
        voice.Play();
    }

    void QueueLead(Hd2Team team)
    {
        AudioClip clip = team == Hd2Team.Red ? redLead : blueLead;
        if (clip == null || Decided)
            return;

        if (VoiceBusy())
        {
            queuedLead = clip;
            return;
        }

        queuedLead = null;
        voice.clip = clip;
        voice.Play();
    }

    void PumpLead()
    {
        if (queuedLead == null || VoiceBusy())
            return;

        AudioClip clip = queuedLead;
        queuedLead = null;
        voice.clip = clip;
        voice.Play();
    }

    bool VoiceBusy()
    {
        if (voice != null && voice.isPlaying)
            return true;
        return matchVoice != null && matchVoice.isPlaying;
    }

    void BeginMatch()
    {
        scoring = Hd2Team.None;
        announced = Hd2Team.None;
        queuedLead = null;
        second = 0f;
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

    bool Award(Hd2Team team)
    {
        int redBefore = RedPoints;
        int blueBefore = BluePoints;
        if (team == Hd2Team.Red)
            RedPoints++;
        else if (team == Hd2Team.Blue)
            BluePoints++;

        bool tookLead = team == Hd2Team.Red
            ? RedPoints > BluePoints && redBefore <= blueBefore && blueBefore > 0
            : BluePoints > RedPoints && blueBefore <= redBefore && redBefore > 0;
        if (tookLead)
            QueueLead(team);

        if (RedPoints >= PointsToWin || BluePoints >= PointsToWin)
        {
            Finish(team);
            return false;
        }

        return true;
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
        queuedLead = null;
        if (matchVoice != null)
            matchVoice.Stop();
        if (voice != null && buzzer != null)
        {
            voice.Stop();
            voice.clip = buzzer;
            voice.Play();
        }

        ShowBanner(winner);
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
        var bannerObject = new GameObject("ControlPointWinner");
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
