using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Tools > Game > Demo Mode (Steam demo): adds or removes Assets/Resources/DemoMode.json (what it does: see DemoMode). The
/// menu shows a check mark while it's on. Demo and playtest mode never go on together, and a build with both switch files
/// is refused. A build with the demo file that Build Demo (Windows) didn't start asks first. Any other build is refused
/// while a demo build's parked portraits or record are still in the project (DemoBuildPrep.NeedsRecovery).
/// </summary>
public class DemoModeMenu : IPreprocessBuildWithReport
{
    const string MenuPath = "Tools/Game/Demo Mode (Steam demo)";
    public const string FilePath = "Assets/Resources/" + DemoMode.ResourceName + ".json";
    public const string PlaytestFilePath = "Assets/Resources/" + PlaytestMode.ResourceName + ".json";

    static bool IsOn => File.Exists(FilePath);

    [MenuItem(MenuPath, false, 41)]
    static void Toggle()
    {
        if (IsOn)
        {
            AssetDatabase.DeleteAsset(FilePath);
            DemoMode.Reload();
            Debug.Log("[Demo] Demo mode is off: the full game is back (its save file, cloud save and achievements).");
            return;
        }

        if (File.Exists(PlaytestFilePath))
        {
            if (!EditorUtility.DisplayDialog("Demo mode",
                    "Playtest mode is on (" + PlaytestFilePath + "). Demo and playtest mode can't be on together.\n\n" +
                    "Remove the playtest file and turn demo mode on?", "Remove it, turn demo on", "Cancel"))
                return;
            AssetDatabase.DeleteAsset(PlaytestFilePath);
            Debug.Log("[Playtest] Playtest mode is off (Demo Mode removed " + PlaytestFilePath + ").");
        }

        WriteDefaultFile();
        Debug.Log("[Demo] Demo mode is on: a new company starts with the demo's numbers (edit " + FilePath + " to change them), " +
                  "its own save file, no cloud save, no UGS sign-in, no achievements. Start a NEW company to get them.");
    }

    [MenuItem(MenuPath, true)]
    static bool ToggleValidate()
    {
        Menu.SetChecked(MenuPath, IsOn);
        return !EditorApplication.isPlayingOrWillChangePlaymode;
    }

    /// <summary>Write the switch file with every default from DemoConfig (it keeps an existing file).</summary>
    public static void WriteDefaultFile()
    {
        if (File.Exists(FilePath)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath) ?? "Assets/Resources");
        File.WriteAllText(FilePath, JsonUtility.ToJson(new DemoConfig(), true) + "\n");
        AssetDatabase.ImportAsset(FilePath);
        DemoMode.Reload();
    }

    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        // A full-game build would ship without the parked portraits (or with the demo's caps and Direct3D11 first)
        if (DemoBuildPrep.NeedsRecovery && !DemoBuild.IsBuilding)
            throw new BuildFailedException("Build stopped: " + DemoBuildPrep.LeftoversMessage);
        bool demo = IsOn;
        if (demo && File.Exists(PlaytestFilePath))
            throw new BuildFailedException("Build stopped: both " + FilePath + " and " + PlaytestFilePath + " are in the project. " +
                                           "Turn one off (Tools > Game > Demo Mode / Playtest Mode).");
        if (!demo || DemoBuild.IsBuilding) return;

        const string message = "Demo mode is on (" + FilePath + "), so this build is the Steam demo: the demo's numbers and " +
                               "locks, its own save file, no cloud save and no achievements, but the full game's product name " +
                               "and version, so it shares the full game's PlayerPrefs and data folder and must never reach " +
                               "players. Tools > Game > Build Demo (Windows) makes the proper demo build; Tools > Game > " +
                               "Demo Mode turns demo mode off.";
        if (Application.isBatchMode)
        {
            Debug.LogWarning("[Demo] " + message);
            return;
        }
        if (!EditorUtility.DisplayDialog("Demo build", message, "Build it anyway", "Cancel"))
            throw new BuildFailedException("Build cancelled: demo mode is on (Tools > Game > Demo Mode turns it off).");
    }
}
