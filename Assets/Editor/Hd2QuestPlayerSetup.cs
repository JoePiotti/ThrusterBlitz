using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

[InitializeOnLoad]
public static class Hd2QuestPlayerSetup
{
    const string SessionKey = "HD2_QUEST_PLAYER_SETUP_DEATHMATCH";

    static Hd2QuestPlayerSetup()
    {
        EditorApplication.delayCall += EnsureRomeStarts;
        EditorApplication.delayCall += AutoSetupIfNeeded;
    }

    static void EnsureRomeStarts()
    {
        const string rome = "Assets/Scenes/Rome.unity";
        var scenes = EditorBuildSettings.scenes;
        if (scenes.Length > 0 && scenes[0].enabled && scenes[0].path == rome)
            return;

        var next = new System.Collections.Generic.List<EditorBuildSettingsScene>
        {
            new EditorBuildSettingsScene(rome, true)
        };
        for (int i = 0; i < scenes.Length; i++)
        {
            if (scenes[i].path == rome)
                continue;
            next.Add(new EditorBuildSettingsScene(scenes[i].path, false));
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
            new EditorBuildSettingsScene("Assets/Scenes/Rome.unity", true),
            new EditorBuildSettingsScene("Assets/Scenes/Deathmatch.unity", false),
            new EditorBuildSettingsScene("Assets/Scenes/Greybox.unity", false)
        };
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
