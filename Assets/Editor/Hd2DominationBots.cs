using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Places domination bots on the red and blue spawns in Rome.
/// The player is already blue, so red gets five bots and blue gets four.
/// Runs only while .utmp/domination_bots.requested exists. Does not open or reload a scene.
/// </summary>
[InitializeOnLoad]
public static class Hd2DominationBots
{
    const string ScenePath = "Assets/Scenes/Rome.unity";
    const string RequestName = "domination_bots.requested";
    const string ReportName = "domination_bots_report.txt";
    const string RobotPath = "Assets/Models/Bot/HumanoidRobot.fbx";
    const string PistolPath = "Assets/Prefabs/GripPistol.prefab";

    static readonly Color Red = new Color(0.85f, 0.12f, 0.12f, 1f);
    static readonly Color Blue = new Color(0.16f, 0.38f, 0.95f, 1f);

    static bool running;
    static bool scheduled;

    static Hd2DominationBots()
    {
        EditorApplication.update += Poll;
    }

    static void Poll()
    {
        if (running || scheduled || EditorApplication.isCompiling || EditorApplication.isUpdating)
            return;
        if (!File.Exists(ProjectFile(RequestName)))
            return;
        scheduled = true;
        EditorApplication.delayCall += Run;
    }

    static string ProjectFile(string name)
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".utmp", name));
    }

    static void Run()
    {
        scheduled = false;
        if (running)
            return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            scheduled = true;
            EditorApplication.delayCall += Run;
            return;
        }

        if (!File.Exists(ProjectFile(RequestName)))
            return;

        if (OthersBusy())
        {
            scheduled = true;
            EditorApplication.delayCall += Run;
            return;
        }

        running = true;
        try
        {
            Place();
        }
        catch (Exception error)
        {
            Write("failed: " + error.Message);
        }
        finally
        {
            running = false;
            TryDelete(ProjectFile(RequestName));
        }
    }

    static void Place()
    {
        var scene = EditorSceneManager.GetSceneByPath(ScenePath);
        if (!scene.isLoaded)
        {
            Write("Rome is not loaded. Open it and request again.");
            return;
        }

        GameObject robot = AssetDatabase.LoadAssetAtPath<GameObject>(RobotPath);
        GameObject pistol = AssetDatabase.LoadAssetAtPath<GameObject>(PistolPath);
        if (robot == null || pistol == null)
        {
            Write("missing robot or pistol model");
            return;
        }

        Transform root = null;
        var domination = UnityEngine.Object.FindObjectsByType<Hd2Domination>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < domination.Length; i++)
        {
            if (domination[i] != null && domination[i].gameObject.scene == scene)
            {
                root = domination[i].transform;
                break;
            }
        }

        if (root == null)
        {
            Write("Domination object is missing from Rome");
            return;
        }

        var points = UnityEngine.Object.FindObjectsByType<Hd2SpawnPoint>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        var red = TeamPoints(points, scene, Hd2Team.Red);
        var blue = TeamPoints(points, scene, Hd2Team.Blue);
        if (red.Count < 5 || blue.Count < 4)
        {
            Write("need 5 red spawns and 4 blue spawns, found " + red.Count + " red and " + blue.Count + " blue");
            return;
        }

        int made = 0;
        made += Make(root, robot, pistol, "Red bot 1", "red one", Hd2Team.Red, Red, red[0], 0, "A", 0.55f);
        made += Make(root, robot, pistol, "Red bot 2", "red two", Hd2Team.Red, Red, red[1], 1, "B", 0.9f);
        made += Make(root, robot, pistol, "Red bot 3", "red three", Hd2Team.Red, Red, red[2], 2, "C", 1.15f);
        made += Make(root, robot, pistol, "Red bot 4", "red four", Hd2Team.Red, Red, red[3], 3, "A", 1.45f);
        made += Make(root, robot, pistol, "Red bot 5", "red five", Hd2Team.Red, Red, red[4], 4, "B", 1.7f);
        made += Make(root, robot, pistol, "Blue bot 1", "blue one", Hd2Team.Blue, Blue, blue[0], 0, "A", 0.7f);
        made += Make(root, robot, pistol, "Blue bot 2", "blue two", Hd2Team.Blue, Blue, blue[1], 1, "B", 1.05f);
        made += Make(root, robot, pistol, "Blue bot 3", "blue three", Hd2Team.Blue, Blue, blue[2], 2, "C", 1.35f);
        made += Make(root, robot, pistol, "Blue bot 4", "blue four", Hd2Team.Blue, Blue, blue[3], 3, "A", 1.6f);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Write("placed " + made + " bots under Domination. Red 5, blue 4. Player stays blue, so the sides are 5 against 5.");
    }

    static int Make(
        Transform root,
        GameObject robot,
        GameObject pistol,
        string objectName,
        string displayName,
        Hd2Team team,
        Color color,
        Hd2SpawnPoint spawn,
        int slot,
        string bias,
        float weight)
    {
        Transform existing = root.Find(objectName);
        GameObject go = existing != null ? existing.gameObject : new GameObject(objectName);
        if (existing == null)
            go.transform.SetParent(root, false);

        go.transform.SetPositionAndRotation(spawn.transform.position, spawn.transform.rotation);
        var bot = go.GetComponent<Hd2EnemyBot>();
        if (bot == null)
            bot = go.AddComponent<Hd2EnemyBot>();
        bot.robotPrefab = robot;
        bot.pistolModel = pistol;
        bot.displayName = displayName;
        bot.bodyColor = color;
        bot.useBodyColor = true;
        bot.team = team;
        bot.domination = true;
        bot.spawnSlot = slot;
        bot.pointBias = bias;
        bot.objectiveWeight = weight;
        bot.retreatFraction = 0.34f;
        bot.retreatHealPerSecond = 8f;
        return 1;
    }

    static List<Hd2SpawnPoint> TeamPoints(Hd2SpawnPoint[] points, Scene scene, Hd2Team team)
    {
        var list = new List<Hd2SpawnPoint>();
        for (int i = 0; i < points.Length; i++)
        {
            if (points[i] == null || points[i].gameObject.scene != scene || points[i].team != team)
                continue;
            list.Add(points[i]);
        }

        list.Sort((a, b) => a.index.CompareTo(b.index));
        return list;
    }

    static bool OthersBusy()
    {
        string dir = Path.GetDirectoryName(ProjectFile(RequestName));
        if (!Directory.Exists(dir))
            return false;
        string[] files = Directory.GetFiles(dir, "*.requested");
        for (int i = 0; i < files.Length; i++)
        {
            if (!files[i].EndsWith(RequestName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    static void Write(string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ProjectFile(ReportName)));
        File.WriteAllText(ProjectFile(ReportName), text);
        Debug.Log("Hd2DominationBots\n" + text);
    }

    static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception)
        {
        }
    }
}
