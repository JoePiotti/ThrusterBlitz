using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Play and builds open on the title scene. The editor otherwise starts whatever scene is open.
/// </summary>
[InitializeOnLoad]
static class Hd2TitleStartup
{
    const string TitlePath = "Assets/Scenes/Title.unity";

    static Hd2TitleStartup()
    {
        EditorApplication.delayCall += Apply;
    }

    static void Apply()
    {
        var title = AssetDatabase.LoadAssetAtPath<SceneAsset>(TitlePath);
        if (title != null && EditorSceneManager.playModeStartScene != title)
            EditorSceneManager.playModeStartScene = title;

        var scenes = EditorBuildSettings.scenes;
        int titleIndex = -1;
        for (int i = 0; i < scenes.Length; i++)
        {
            if (scenes[i].path == TitlePath)
            {
                titleIndex = i;
                break;
            }
        }

        if (titleIndex == 0 && scenes.Length > 0 && scenes[0].enabled)
            return;

        if (titleIndex < 0)
        {
            var next = new EditorBuildSettingsScene[scenes.Length + 1];
            next[0] = new EditorBuildSettingsScene(TitlePath, true);
            for (int i = 0; i < scenes.Length; i++)
                next[i + 1] = scenes[i];
            EditorBuildSettings.scenes = next;
            return;
        }

        var ordered = new EditorBuildSettingsScene[scenes.Length];
        ordered[0] = new EditorBuildSettingsScene(TitlePath, true);
        int write = 1;
        for (int i = 0; i < scenes.Length; i++)
        {
            if (i == titleIndex)
                continue;
            ordered[write++] = scenes[i];
        }

        EditorBuildSettings.scenes = ordered;
    }
}
