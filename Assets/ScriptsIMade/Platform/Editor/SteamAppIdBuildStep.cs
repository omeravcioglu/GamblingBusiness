using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Desktop builds and steam_appid.txt: a DEVELOPMENT build gets the project root's copy next to the executable, so it runs
/// with Steam when started by hand (Steam then treats it as app 4427670 and RestartAppIfNecessary doesn't relaunch it).
/// A release build must not have one (Steam starts release builds itself), so a copy left in its folder by an earlier
/// development build is removed. A demo development build (Resources/DemoMode.json) gets the demo's steamAppId instead.
/// </summary>
public class SteamAppIdBuildStep : IPostprocessBuildWithReport
{
    const string FileName = "steam_appid.txt";
    // Demo: the demo switch file (in the build while it exists)
    const string DemoFilePath = "Assets/Resources/" + DemoMode.ResourceName + ".json";

    static long ReadDemoAppId()
    {
        try { return JsonUtility.FromJson<DemoConfig>(File.ReadAllText(DemoFilePath))?.steamAppId ?? 0; }
        catch (Exception e)
        {
            Debug.LogWarning("[Steam] Couldn't read " + DemoFilePath + ": " + e.Message);
            return 0;
        }
    }

    public int callbackOrder => 100;

    public void OnPostprocessBuild(BuildReport report)
    {
        BuildTarget target = report.summary.platform;
        if (target != BuildTarget.StandaloneWindows && target != BuildTarget.StandaloneWindows64 &&
            target != BuildTarget.StandaloneLinux64 && target != BuildTarget.StandaloneOSX)
            return;

        string output = report.summary.outputPath;
        string folder = string.IsNullOrEmpty(output) ? null : Path.GetDirectoryName(output);
        if (string.IsNullOrEmpty(folder)) return;
        string destination = Path.Combine(folder, FileName);

        try
        {
            // Demo: a demo build runs as the demo's own app, never as the full game
            if ((report.summary.options & BuildOptions.Development) != 0 && File.Exists(DemoFilePath))
            {
                long demoId = ReadDemoAppId();
                if (demoId <= 0 || demoId == SteamPlatform.DefaultAppId)
                {
                    if (File.Exists(destination)) File.Delete(destination);
                    Debug.LogWarning("[Steam] Demo development build: " + DemoFilePath + " has no steamAppId yet, so there is no " +
                                     FileName + " next to the player (Steam won't start it by hand).");
                    return;
                }
                File.WriteAllText(destination, demoId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Debug.Log("[Steam] Demo development build: wrote the demo app id " + demoId + " to " + destination + ".");
                return;
            }
            if ((report.summary.options & BuildOptions.Development) != 0)
            {
                // The project root (next to Assets), also in batch / CI builds whose working folder may differ
                string source = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? Directory.GetCurrentDirectory(), FileName);
                if (!File.Exists(source))
                {
                    Debug.LogWarning("[Steam] Development build: there is no " + FileName + " in the project root to copy next to the player.");
                    return;
                }
                File.Copy(source, destination, true);
                Debug.Log("[Steam] Development build: copied " + FileName + " next to the player (" + destination + ").");
            }
            else if (File.Exists(destination))
            {
                File.Delete(destination);
                Debug.Log("[Steam] Release build: removed the " + FileName + " an earlier development build left in " + folder + ".");
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Steam] Couldn't update " + destination + ": " + e.Message);
        }
    }
}
