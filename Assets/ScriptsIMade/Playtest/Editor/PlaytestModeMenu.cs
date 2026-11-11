using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Tools > Game > Playtest Mode: adds or removes Assets/Resources/PlaytestMode.json (what it does: see PlaytestMode). The
/// menu shows a check mark while it's on, and a build asks first, so a playtest build never reaches Steam by accident.
/// </summary>
public class PlaytestModeMenu : IPreprocessBuildWithReport
{
    const string MenuPath = "Tools/Game/Playtest Mode (rich start, casino open)";
    const string FilePath = "Assets/Resources/" + PlaytestMode.ResourceName + ".json";

    static bool IsOn => File.Exists(FilePath);
    // Demo: the demo switch file (demo mode wins over playtest mode)
    const string DemoFilePath = "Assets/Resources/" + DemoMode.ResourceName + ".json";

    [MenuItem(MenuPath, false, 40)]
    static void Toggle()
    {
        if (IsOn)
        {
            AssetDatabase.DeleteAsset(FilePath);
            Debug.Log("[Playtest] Playtest mode is off: new companies start with $50,000 and the cloud save is back.");
        }
        else
        {
            // Demo: never both switches at once
            if (File.Exists(DemoFilePath))
            {
                EditorUtility.DisplayDialog("Playtest mode", "Demo mode is on (" + DemoFilePath + "), and it always wins over " +
                                            "playtest mode. Turn it off first: Tools > Game > Demo Mode (Steam demo).", "OK");
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath) ?? "Assets/Resources");
            File.WriteAllText(FilePath, "{\n  \"startingValue\": " + PlaytestMode.DefaultStartingValue.ToString("0") +
                                        ",\n  \"skillPoints\": " + PlaytestMode.DefaultSkillPoints +
                                        ",\n  \"openCasino\": true\n}\n");
            AssetDatabase.ImportAsset(FilePath);
            Debug.Log("[Playtest] Playtest mode is on: new companies start with $" +
                      PlaytestMode.DefaultStartingValue.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " and " +
                      PlaytestMode.DefaultSkillPoints + " skill points, the casino is open (edit " + FilePath +
                      " to change these), no cloud save, no achievements. Start a NEW company to get them.");
        }
    }

    [MenuItem(MenuPath, true)]
    static bool ToggleValidate()
    {
        Menu.SetChecked(MenuPath, IsOn);
        return !EditorApplication.isPlayingOrWillChangePlaymode;
    }

    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        if (!IsOn) return;
        // Demo: a build with both switch files would be a demo carrying a playtest file
        if (File.Exists(DemoFilePath))
            throw new BuildFailedException("Build stopped: both " + FilePath + " and " + DemoFilePath + " are in the project. " +
                                           "Turn one off (Tools > Game > Playtest Mode / Demo Mode).");
        const string message = "Playtest mode is on. In this build new companies start with the money and skill points in " +
                               "Assets/Resources/PlaytestMode.json, the casino can be open, and there is no cloud save and no " +
                               "Steam achievements. Don't upload it to Steam. Tools > Game > Playtest Mode turns it off.";
        if (Application.isBatchMode)
        {
            Debug.LogWarning("[Playtest] " + message);
            return;
        }
        if (!EditorUtility.DisplayDialog("Playtest build", message, "Build playtest build", "Cancel"))
            throw new BuildFailedException("Build cancelled: playtest mode is on (Tools > Game > Playtest Mode turns it off).");
    }
}
