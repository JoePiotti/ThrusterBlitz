using System.Collections.Generic;
using Fusion;
using Fusion.Editor;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds the test capsule and a Windows dedicated server player.
/// </summary>
public static class Hd2NetSetup
{
    const string PlayerPath = "Assets/Resources/Hd2NetPlayer.prefab";
    const string PlayAsServerKey = "Hd2PlayAsServer";

    [MenuItem("HD2/Create Net Player")]
    public static void CreatePlayer()
    {
        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Hd2NetPlayer";
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader != null)
            body.GetComponent<Renderer>().sharedMaterial = new Material(shader);
        body.AddComponent<NetworkObject>();
        body.AddComponent<NetworkTransform>();
        body.AddComponent<Hd2NetPlayer>();

        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");

        PrefabUtility.SaveAsPrefabAsset(body, PlayerPath);
        Object.DestroyImmediate(body);
        NetworkProjectConfigUtilities.RebuildPrefabTable();
        Debug.Log("Hd2 net player saved.");
    }

    [MenuItem("HD2/Play As Server")]
    public static void PlayAsServer()
    {
        SessionState.SetBool(PlayAsServerKey, true);
        EditorApplication.EnterPlaymode();
    }

    [MenuItem("HD2/Build Test Server")]
    public static void BuildServer()
    {
        var scenes = new List<string>();
        foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
        {
            if (scene.enabled)
                scenes.Add(scene.path);
        }

        var options = new BuildPlayerOptions
        {
            scenes = scenes.ToArray(),
            locationPathName = "Builds/Hd2Server/Hd2Server.exe",
            target = BuildTarget.StandaloneWindows64,
            subtarget = (int)StandaloneBuildSubtarget.Server,
            options = BuildOptions.Development
        };
        var report = BuildPipeline.BuildPlayer(options);
        Debug.Log("Hd2 server build " + report.summary.result + " " + report.summary.totalErrors);
    }
}
