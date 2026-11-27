using System;
using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.SceneManagement;

/// <summary>
/// One Fusion server room named hd2-test. The server loads Shootout and stays there.
/// Clients join that room. A capsule is spawned only after the client has loaded Shootout.
/// </summary>
public class Hd2Session : MonoBehaviour, INetworkRunnerCallbacks
{
    public const string SessionName = "hd2-test";
    public const string JoinMarker = "hd2-test";
    public const string JoinFailMessage = "Could not join hd2-test";
    public const string JoiningMessage = "Joining hd2-test";
    const string PlayerResource = "Hd2NetPlayer";
    const string PlayAsServerKey = "Hd2PlayAsServer";
    const string LostLabel = "hd2-test\nconnection lost";
    const float JoinTimeout = 30f;
    const int ReadyResends = 8;

    static readonly string[] CountLabels =
    {
        "hd2-test\n0 players",
        "hd2-test\n1 player",
        "hd2-test\n2 players",
        "hd2-test\n3 players",
        "hd2-test\n4 players",
        "hd2-test\n5 players",
        "hd2-test\n6 players",
        "hd2-test\n7 players",
        "hd2-test\n8 players",
        "hd2-test\n9 players",
        "hd2-test\n10 players"
    };

    static readonly ReliableKey ReadyKey = ReliableKey.FromInts(0x48443201, 0, 0, 0);

    static Hd2Session active;
    static bool hostPending;
    static int serverAttempts;
    static bool quitting;
    static string pendingTitleNote;

    NetworkRunner runner;
    NetworkObject playerPrefab;
    GameMode mode;
    bool sceneReady;
    bool live;
    bool closing;
    bool ending;
    bool returnToTitle;
    bool returned;
    bool anchored;
    bool keptOpen;
    int anchorTries;
    int spawnCount;
    int readySends;
    float giveUpAt;
    float nextReadyAt;
    Hd2Text roomLabel;
    readonly List<PlayerRef> members = new List<PlayerRef>(8);
    readonly List<PlayerRef> waiting = new List<PlayerRef>(8);
    readonly List<PlayerRef> ready = new List<PlayerRef>(8);
    readonly List<PlayerRef> spawnedPlayers = new List<PlayerRef>(8);
    readonly List<SpawnedBody> bodies = new List<SpawnedBody>(8);

    struct SpawnedBody
    {
        public int playerId;
        public NetworkObject body;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        active = null;
        hostPending = false;
        serverAttempts = 0;
        quitting = false;
        pendingTitleNote = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (quitting || active != null)
            return;

#if UNITY_EDITOR
        if (UnityEditor.SessionState.GetBool(PlayAsServerKey, false))
        {
            UnityEditor.SessionState.SetBool(PlayAsServerKey, false);
            hostPending = true;
            Begin(GameMode.Server);
            return;
        }
#endif
        if (hostPending)
        {
            Debug.Log("Hd2 server restarted after the title unloaded.");
            Begin(GameMode.Server);
            return;
        }

        if (IsServerProcess())
        {
            hostPending = true;
            Begin(GameMode.Server);
        }
    }

    public static void Join()
    {
        if (active != null && active.mode == GameMode.Server)
            return;
        if (active != null)
            active.Retire();
        Begin(GameMode.Client);
    }

    public static void NoteLocalBody()
    {
        if (active != null)
            active.live = true;
    }

    public static string TakeTitleNote()
    {
        string note = pendingTitleNote;
        pendingTitleNote = null;
        return note;
    }

    static bool IsServerProcess()
    {
#if UNITY_SERVER
        return true;
#else
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "-batchmode" || args[i] == "-hd2Server")
                return true;
        }

        return false;
#endif
    }

    static void Begin(GameMode mode)
    {
        if (active != null)
            return;
        if (mode != GameMode.Client && mode != GameMode.Server)
            return;
        if (mode == GameMode.Server)
        {
            serverAttempts++;
            if (serverAttempts > 2)
            {
                hostPending = false;
                Debug.LogError("Hd2 server stopped before Shootout loaded.");
                return;
            }
        }

        var host = new GameObject("Hd2Session");
        DontDestroyOnLoad(host);
        var session = host.AddComponent<Hd2Session>();
        session.mode = mode;
        session.StartSession();
    }

    void Awake()
    {
        if (active != null && active != this)
        {
            Destroy(gameObject);
            return;
        }

        active = this;
        DontDestroyOnLoad(gameObject);
    }

    void OnApplicationQuit()
    {
        quitting = true;
        hostPending = false;
    }

    void OnDestroy()
    {
        if (active == this)
            active = null;
    }

    void Retire()
    {
        closing = true;
        returnToTitle = false;
        if (runner != null && runner.IsRunning)
            runner.Shutdown(false, ShutdownReason.Ok, false);
        if (active == this)
            active = null;
        Destroy(gameObject);
    }

    async void StartSession()
    {
        DontDestroyOnLoad(gameObject);
        if (mode == GameMode.Client)
            giveUpAt = Time.unscaledTime + JoinTimeout;

        playerPrefab = Resources.Load<NetworkObject>(PlayerResource);
        if (playerPrefab == null)
            Debug.LogError("Hd2 is missing Resources/Hd2NetPlayer.");

        runner = gameObject.AddComponent<NetworkRunner>();
        runner.ProvideInput = false;
        var sceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>();
        runner.AddCallbacks(this);
        DontDestroyOnLoad(gameObject);

        var args = new StartGameArgs
        {
            GameMode = mode,
            SessionName = SessionName,
            SceneManager = sceneManager
        };

        if (mode == GameMode.Server)
        {
            int index = SceneUtility.GetBuildIndexByScenePath("Assets/Scenes/Shootout.unity");
            if (index < 0)
            {
                Debug.LogError("Hd2 Shootout is not in the build.");
                hostPending = false;
                return;
            }

            var scene = new NetworkSceneInfo();
            scene.AddSceneRef(SceneRef.FromIndex(index), LoadSceneMode.Single);
            args.Scene = scene;
        }

        try
        {
            StartGameResult result = await runner.StartGame(args);
            if (result.Ok)
            {
                Debug.Log("Hd2 session " + mode + " " + SessionName);
                return;
            }

            Debug.LogError("Hd2 session failed " + result.ShutdownReason + " " + result.ErrorMessage);
            if (mode == GameMode.Client)
                GiveUp();
        }
        catch (Exception error)
        {
            Debug.LogError("Hd2 session failed " + error.Message);
            if (mode == GameMode.Client)
                GiveUp();
        }
    }

    void Update()
    {
        if (returnToTitle)
        {
            ReturnToTitle();
            return;
        }

        if (mode != GameMode.Client || runner == null || !runner.IsRunning)
            return;

        if (!sceneReady)
        {
            if (giveUpAt > 0f && Time.unscaledTime >= giveUpAt)
                GiveUp();
            return;
        }

        if (readySends < ReadyResends && Time.unscaledTime >= nextReadyAt)
        {
            nextReadyAt = Time.unscaledTime + 1f;
            readySends++;
            runner.SendReliableDataToServer(ReadyKey, ReadOnlySpan<byte>.Empty);
        }
    }

    void LateUpdate()
    {
        if (anchored)
            return;

        anchorTries++;
        if (gameObject.scene.buildIndex < 0 || anchorTries > 5)
        {
            anchored = true;
            return;
        }

        DontDestroyOnLoad(gameObject);
    }

    void GiveUp()
    {
        if (ending || mode == GameMode.Server)
            return;
        ending = true;
        giveUpAt = 0f;
        Debug.LogError("Hd2 join timed out");
        if (runner != null && runner.IsRunning)
            runner.Shutdown(false, ShutdownReason.Ok, false);
        else
            returnToTitle = true;
    }

    void ReturnToTitle()
    {
        if (returned)
            return;
        returned = true;
        returnToTitle = false;
        bool onTitle = SceneManager.GetActiveScene().name == "Title";
        if (onTitle)
        {
            pendingTitleNote = null;
            Hd2MainMenu title = FindFirstObjectByType<Hd2MainMenu>();
            if (title != null)
                title.CancelJoin();
        }
        else
        {
            pendingTitleNote = JoinFailMessage;
            SceneManager.LoadScene("Title");
        }

        if (active == this)
            active = null;
        Destroy(gameObject);
    }

    void Remember(PlayerRef player)
    {
        if (Listed(members, player))
            return;
        members.Add(player);
        ApplyLabel();
    }

    void ForgetMember(PlayerRef player)
    {
        int id = player.PlayerId;
        for (int i = members.Count - 1; i >= 0; i--)
        {
            if (members[i].PlayerId != id)
                continue;
            members.RemoveAt(i);
            ApplyLabel();
            return;
        }
    }

    static bool Listed(List<PlayerRef> list, PlayerRef player)
    {
        int id = player.PlayerId;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].PlayerId == id)
                return true;
        }

        return false;
    }

    static void Forget(List<PlayerRef> list, PlayerRef player)
    {
        int id = player.PlayerId;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (list[i].PlayerId == id)
                list.RemoveAt(i);
        }
    }

    void SeatAndSpawn(PlayerRef player)
    {
        if (!sceneReady || !ShootoutLoaded() || playerPrefab == null || runner == null)
            return;
        if (!runner.IsPlayerValid(player) || Listed(spawnedPlayers, player))
            return;

        spawnedPlayers.Add(player);
        Forget(waiting, player);

        int nth = spawnCount;
        spawnCount++;
        Vector3 position = new Vector3(nth * 1.5f, 2.5f, 0f);
        Quaternion rotation = Quaternion.identity;
        if (TryTeamSpawn(nth, out Vector3 spawnPosition, out Quaternion spawnRotation))
        {
            position = spawnPosition;
            rotation = spawnRotation;
        }

        NetworkObject body = runner.Spawn(playerPrefab, position, rotation, player);
        if (body == null)
        {
            Forget(spawnedPlayers, player);
            Debug.LogError("Hd2 could not spawn a player body.");
            return;
        }

        var spawned = new SpawnedBody();
        spawned.playerId = player.PlayerId;
        spawned.body = body;
        bodies.Add(spawned);
        Hd2Shootout.ReplaceEasiestBot();
        Debug.Log("Hd2 player joined " + player.PlayerId + " at " + position.x);
    }

    static bool TryTeamSpawn(int nth, out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;
        Hd2Team side = (nth % 2) == 0 ? Hd2Team.Blue : Hd2Team.Red;
        int slot = nth / 2;
        var points = UnityEngine.Object.FindObjectsByType<Hd2SpawnPoint>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        int matches = 0;
        for (int i = 0; i < points.Length; i++)
        {
            if (points[i] != null && points[i].team == side)
                matches++;
        }

        if (matches == 0)
            return false;

        int want = slot % matches;
        int seen = 0;
        for (int i = 0; i < points.Length; i++)
        {
            if (points[i] == null || points[i].team != side)
                continue;
            if (seen == want)
            {
                position = points[i].transform.position;
                rotation = points[i].transform.rotation;
                return true;
            }

            seen++;
        }

        return false;
    }

    void DespawnBody(PlayerRef player)
    {
        if (runner == null)
            return;
        int id = player.PlayerId;
        for (int i = bodies.Count - 1; i >= 0; i--)
        {
            if (bodies[i].playerId != id)
                continue;
            NetworkObject body = bodies[i].body;
            bodies.RemoveAt(i);
            if (body != null)
                runner.Despawn(body);
        }
    }

    void EnsureView()
    {
        if (runner != null && runner.GameMode == GameMode.Server)
            return;
        if (Camera.main != null)
            return;

        var view = new GameObject("Hd2NetView");
        view.tag = "MainCamera";
        var camera = view.AddComponent<Camera>();
        camera.nearClipPlane = 0.05f;
        camera.allowHDR = false;
        view.AddComponent<AudioListener>();
        view.transform.position = new Vector3(0f, 1.6f, -4f);
        view.transform.rotation = Quaternion.LookRotation(new Vector3(0f, -0.15f, 1f));

#if !UNITY_SERVER
        if (UnityEngine.XR.XRSettings.isDeviceActive)
        {
            var driver = view.AddComponent<TrackedPoseDriver>();
            var position = new InputAction("Hd2EyePos", InputActionType.Value, "<XRHMD>/centerEyePosition", expectedControlType: "Vector3");
            var rotation = new InputAction("Hd2EyeRot", InputActionType.Value, "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion");
            position.Enable();
            rotation.Enable();
            driver.positionInput = new InputActionProperty(position);
            driver.rotationInput = new InputActionProperty(rotation);
            driver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            driver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
        }
#endif
    }

    void ShowRoomLabel()
    {
        if (roomLabel != null)
            return;

        var labelObject = new GameObject("Hd2RoomLabel");
        Camera view = Camera.main;
        if (view != null)
        {
            labelObject.transform.SetParent(view.transform, false);
            labelObject.transform.localPosition = new Vector3(0f, -0.22f, 1.25f);
            labelObject.transform.localRotation = Quaternion.identity;
        }
        else
        {
            labelObject.transform.position = new Vector3(0f, 2.2f, 3f);
            labelObject.transform.rotation = Quaternion.identity;
        }

        var text = Hd2Text.On(labelObject, true);
        text.fontSize = 64;
        text.characterSize = 0.018f;
        text.anchor = TextAnchor.MiddleCenter;
        text.alignment = TextAlignment.Center;
        text.color = new Color(0.55f, 1f, 0.7f, 1f);

        roomLabel = text;
        ApplyLabel();
    }

    void ApplyLabel()
    {
        if (roomLabel == null)
            return;
        int count = members.Count;
        if (count < 0)
            count = 0;
        if (count >= CountLabels.Length)
            count = CountLabels.Length - 1;
        string next = CountLabels[count];
        if (roomLabel.text == next)
            return;
        roomLabel.text = next;
    }

    void ShowLostLabel()
    {
        if (roomLabel == null)
            return;
        if (roomLabel.text == LostLabel)
            return;
        roomLabel.text = LostLabel;
    }

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        Remember(player);
        if (runner == null || !runner.IsServer)
            return;
        if (Listed(ready, player) || Listed(spawnedPlayers, player))
            SeatAndSpawn(player);
        else if (!Listed(waiting, player))
            waiting.Add(player);
    }

    static bool ShootoutLoaded()
    {
        Scene scene = SceneManager.GetActiveScene();
        return scene.IsValid() && scene.name == "Shootout";
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log("Hd2 player left " + player.PlayerId);
        ForgetMember(player);
        Forget(waiting, player);
        Forget(ready, player);
        Forget(spawnedPlayers, player);
        if (runner != null && runner.IsServer)
        {
            DespawnBody(player);
            if (!keptOpen)
            {
                keptOpen = true;
                Debug.Log("Hd2 room " + SessionName + " stays open");
            }
        }
    }

    public static void CloseRoom()
    {
        var session = FindFirstObjectByType<Hd2Session>();
        if (session != null)
            session.Close();
        else if (!IsServerProcess())
            SceneManager.LoadScene("Title");
    }

    public void Close()
    {
        if (mode == GameMode.Server)
            return;
        if (closing)
            return;
        closing = true;
        if (runner != null && runner.IsRunning)
            runner.Shutdown(false, ShutdownReason.Ok, false);
        if (IsServerProcess())
        {
#if !UNITY_EDITOR
            Application.Quit();
#endif
            return;
        }

        SceneManager.LoadScene("Title");
    }

    public void OnSceneLoadDone(NetworkRunner runner)
    {
        sceneReady = true;
        if (gameObject.scene.buildIndex >= 0)
            DontDestroyOnLoad(gameObject);

        if (runner != null && runner.IsServer)
        {
            hostPending = false;
            ShowRoomLabel();
            ApplyLabel();
            for (int i = 0; i < ready.Count; i++)
                SeatAndSpawn(ready[i]);
        }
        else
        {
            EnsureView();
            ShowRoomLabel();
            ApplyLabel();
            giveUpAt = 0f;
            nextReadyAt = Time.unscaledTime;
            readySends = 0;
        }

        Debug.Log("Hd2 scene ready");
    }

    public void OnConnectedToServer(NetworkRunner runner)
    {
        Debug.Log("Hd2 connected to " + SessionName);
    }

    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        Debug.Log("Hd2 shutdown " + shutdownReason);
        if (mode == GameMode.Server || closing)
            return;
        if (live)
        {
            ShowLostLabel();
            return;
        }

        returnToTitle = true;
        if (SceneManager.GetActiveScene().name != "Title")
            return;

        Hd2MainMenu title = FindFirstObjectByType<Hd2MainMenu>();
        if (title != null)
            title.CancelJoin();
    }

    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
        Debug.Log("Hd2 disconnected " + reason);
    }

    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
    {
        Debug.LogError("Hd2 connect failed " + reason);
    }

    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ReadOnlySpan<byte> data)
    {
        if (runner == null || !runner.IsServer || key != ReadyKey)
            return;
        if (!Listed(ready, player))
            ready.Add(player);
        SeatAndSpawn(player);
    }

    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
}
