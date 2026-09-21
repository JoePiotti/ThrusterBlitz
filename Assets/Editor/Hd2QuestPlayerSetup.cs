using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class Hd2QuestPlayerSetup
{
    const string SessionKey = "HD2_QUEST_PLAYER_SETUP_DONE";

    static Hd2QuestPlayerSetup()
    {
        EditorApplication.delayCall += AutoSetupIfNeeded;
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
        PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.hd2.game");
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.SetMobileMTRendering(BuildTargetGroup.Android, true);
        PlayerSettings.stereoRenderingPath = StereoRenderingPath.SinglePass;
        PlayerSettings.colorSpace = ColorSpace.Linear;
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
        PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.Android, ManagedStrippingLevel.Low);
        Debug.Log("HD2 Quest player settings complete.");
    }
}
