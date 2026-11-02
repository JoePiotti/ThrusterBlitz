using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class Hd2QuestPlayerSetup
{
    const string SessionKey = "HD2_QUEST_PLAYER_SETUP_DEATHMATCH";

    static Hd2QuestPlayerSetup()
    {
        EditorApplication.delayCall += EnsureTitleStarts;
        EditorApplication.delayCall += AutoSetupIfNeeded;
    }

    static void EnsureTitleStarts()
    {
        const string title = "Assets/Scenes/Title.unity";
        const string rome = "Assets/Scenes/Rome.unity";
        var titleAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(title);
        if (titleAsset != null && EditorSceneManager.playModeStartScene != titleAsset)
            EditorSceneManager.playModeStartScene = titleAsset;

        var scenes = EditorBuildSettings.scenes;
        bool titleFirst = scenes.Length > 1
            && scenes[0].enabled
            && scenes[0].path == title
            && scenes[1].enabled
            && scenes[1].path == rome;
        if (titleFirst)
            return;

        var next = new System.Collections.Generic.List<EditorBuildSettingsScene>
        {
            new EditorBuildSettingsScene(title, true),
            new EditorBuildSettingsScene(rome, true)
        };
        for (int i = 0; i < scenes.Length; i++)
        {
            if (scenes[i].path == title || scenes[i].path == rome)
                continue;
            next.Add(scenes[i]);
        }

        EditorBuildSettings.scenes = next.ToArray();
    }

    static void AutoSetupIfNeeded()
    {
        if (SessionState.GetBool(SessionKey, false))
            return;

        SetupQuestPlayer();
        SessionState.SetBool(SessionKey, true);
    }

    public static void SetupQuestPlayer()
    {
        PlayerSettings.productName = "TB";
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.tb.game");
        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene("Assets/Scenes/Title.unity", true),
            new EditorBuildSettingsScene("Assets/Scenes/Rome.unity", true),
            new EditorBuildSettingsScene("Assets/Scenes/Rome CP.unity", true),
            new EditorBuildSettingsScene("Assets/Scenes/Deathmatch.unity", false),
            new EditorBuildSettingsScene("Assets/Scenes/Greybox.unity", false)
        };
        var title = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/Title.unity");
        if (title != null)
            EditorSceneManager.playModeStartScene = title;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.SetMobileMTRendering(NamedBuildTarget.Android, true);
        PlayerSettings.stereoRenderingPath = StereoRenderingPath.SinglePass;
        PlayerSettings.colorSpace = ColorSpace.Linear;
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
        PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.Low);
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[]
        {
            UnityEngine.Rendering.GraphicsDeviceType.Vulkan
        });

        var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
        if (assets != null && assets.Length > 0)
        {
            var serialized = new SerializedObject(assets[0]);
            var inputHandler = serialized.FindProperty("activeInputHandler");
            if (inputHandler != null)
            {
                inputHandler.intValue = 1;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
        Debug.Log("HD2 Quest player settings complete.");
    }
}
