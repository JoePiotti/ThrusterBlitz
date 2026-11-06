using System;
using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.SceneManagement;

/// <summary>
/// One Fusion server room named hd2-test. The server loads Greybox and spawns a capsule
/// for each joined player. Clients only render that room.
/// </summary>
public class Hd2Session : MonoBehaviour, INetworkRunnerCallbacks
{
    public const string SessionName = "hd2-test";
    public const string JoinMarker = "hd2-test";
    const string PlayerResource = "Hd2NetPlayer";
    const string PlayAsServerKey = "Hd2PlayAsServer";

    static bool starting;

    NetworkRunner runner;
    NetworkObject playerPrefab;
    bool sceneReady;
    int spawnCount;
    readonly List<PlayerRef> waiting = new List<PlayerRef>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
#if UNITY_EDITOR
        if (UnityEditor.SessionState.GetBool(PlayAsServerKey, false))
        {
            UnityEditor.SessionState.SetBool(PlayAsServerKey, false);
            Begin(GameMode.Server);
            return;
        }
#endif
        if (IsServerProcess())
            Begin(GameMode.Server);
    }

    public static void Join()
    {
        Begin(GameMode.Client);
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
        if (starting)
            return;
        starting = true;

        var host = new GameObject("Hd2Session");
        DontDestroyOnLoad(host);
        var session = host.AddComponent<Hd2Session>();
        session.StartSession(mode);
    }

    async void StartSession(GameMode mode)
    {
        playerPrefab = Resources.Load<NetworkObject>(PlayerResource);
        if (playerPrefab == null)
            Debug.LogError("Hd2 is missing Resources/Hd2NetPlayer.");

        runner = gameObject.AddComponent<NetworkRunner>();
        runner.ProvideInput = false;
        var sceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>();
        runner.AddCallbacks(this);

        var args = new StartGameArgs
        {
            GameMode = mode,
            SessionName = SessionName,
            SceneManager = sceneManager
        };

        if (mode == GameMode.Server)
        {
            int index = SceneUtility.GetBuildIndexByScenePath("Assets/Scenes/Greybox.unity");
            if (index < 0)
            {
                Debug.LogError("Hd2 Greybox is not in the build.");
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
                Debug.Log("Hd2 session " + mode + " " + SessionName);
            else
                Debug.LogError("Hd2 session failed " + result.ShutdownReason + " " + result.ErrorMessage + ". Paste a Fusion 2 App Id into PhotonAppSettings.");
        }
        catch (Exception error)
        {
            Debug.LogError("Hd2 session failed " + error.Message);
        }
    }

    void SpawnPlayer(PlayerRef player)
    {
        if (playerPrefab == null)
            return;

        Vector3 position = new Vector3(spawnCount * 1.5f, 1.5f, 0f);
        spawnCount++;
        runner.Spawn(playerPrefab, position, Quaternion.identity, player);
        Debug.Log("Hd2 player joined " + player.PlayerId + " at " + position.x);
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

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        if (!runner.IsServer)
            return;
        if (!sceneReady)
        {
            waiting.Add(player);
            return;
        }

        SpawnPlayer(player);
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log("Hd2 player left " + player.PlayerId);
    }

    public void OnSceneLoadDone(NetworkRunner runner)
    {
        sceneReady = true;
        if (runner.IsServer)
        {
            for (int i = 0; i < waiting.Count; i++)
                SpawnPlayer(waiting[i]);
            waiting.Clear();
        }
        else
        {
            EnsureView();
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
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ReadOnlySpan<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
}
