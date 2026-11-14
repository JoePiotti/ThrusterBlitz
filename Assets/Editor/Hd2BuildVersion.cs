using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

/// <summary>
/// Bumps the player version before each build so the title screen can show
/// which package is on a headset.
/// </summary>
public class Hd2BuildVersion : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        int code = PlayerSettings.Android.bundleVersionCode + 1;
        if (code < 1)
            code = 1;
        string version = "1.0." + code;
        PlayerSettings.Android.bundleVersionCode = code;
        PlayerSettings.bundleVersion = version;
        string folder = "Assets/Resources";
        if (!AssetDatabase.IsValidFolder(folder))
            AssetDatabase.CreateFolder("Assets", "Resources");
        System.IO.File.WriteAllText(folder + "/BuildVersion.txt", version);
        AssetDatabase.ImportAsset(folder + "/BuildVersion.txt");
        AssetDatabase.SaveAssets();
    }
}
