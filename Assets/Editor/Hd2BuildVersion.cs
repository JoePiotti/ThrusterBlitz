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
        string path = folder + "/BuildVersion.txt";
        if (WriteWithRetry(path, version))
            AssetDatabase.ImportAsset(path);
        else
            UnityEngine.Debug.LogWarning("Hd2BuildVersion: " + path + " is locked by another program; "
                + "the title screen will show the previous version for this build.");
        AssetDatabase.SaveAssets();
    }

    // Another program (git, a file sync, a virus scan) can hold the file for a moment.
    // Windows then refuses the write (IO error 1224) and the whole build used to fail.
    static bool WriteWithRetry(string path, string text)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                System.IO.File.WriteAllText(path, text);
                return true;
            }
            catch (System.IO.IOException)
            {
                System.Threading.Thread.Sleep(200);
            }
            catch (System.UnauthorizedAccessException)
            {
                System.Threading.Thread.Sleep(200);
            }
        }

        return false;
    }
}
