using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Tools > Game > Build Demo (Windows): a release Windows build of the Steam demo in Builds/Demo/&lt;version&gt;/, made only
/// when the owner clicks it. For the length of the build it parks PlaytestMode.json in DemoBuildParked (next to Assets), writes
/// DemoMode.json (an existing one is kept with its values), and names the player "Is Gambling Bad For Business Demo"
/// with version "&lt;x&gt;-demo" (its own persistentDataPath and PlayerPrefs). DemoBuildPrep also parks most portraits, caps
/// the kept ones and puts DirectX 11 first for the build. Everything is put back and saved afterwards, also when the build
/// fails; a marker file (and DemoBuildPrep's record) lets the next Editor start undo a build an Editor crash interrupted.
/// </summary>
[InitializeOnLoad]
public static class DemoBuild
{
    const string MenuPath = "Tools/Game/Build Demo (Windows)";
    const string DemoProductName = "Is Gambling Bad For Business Demo";
    const string VersionSuffix = "-demo";
    const string ExeName = "IsGamblingBadForBusinessDemo.exe";

    /// <summary>True while this script's build runs (DemoModeMenu's build guard then doesn't ask).</summary>
    public static bool IsBuilding { get; private set; }

    static string ProjectRoot => Path.GetDirectoryName(Application.dataPath) ?? Directory.GetCurrentDirectory();
    // Next to Assets (Unity doesn't import it): it survives an Editor restart (Temp is wiped on exit) and a deleted Library,
    // so a parked file, the marker and DemoBuildPrep's record are never lost. Deleted again once it's empty
    internal static string ParkFolder => Path.Combine(ProjectRoot, "DemoBuildParked");
    // Written for the length of a build: the original product name, the original version, and "1" when the build wrote DemoMode.json
    static string MarkerFile => Path.Combine(ParkFolder, "build-in-progress.txt");
    static string ParkedFile => Path.Combine(ParkFolder, Path.GetFileName(DemoModeMenu.PlaytestFilePath));

    static DemoBuild()
    {
        // A build runs synchronously, so a domain load never happens during one: anything left here is from an interrupted build
        if (File.Exists(MarkerFile) || File.Exists(ParkedFile) || DemoBuildPrep.NeedsRecovery)
            EditorApplication.delayCall += () => { if (!IsBuilding) RecoverInterruptedBuild(); };
    }

    [MenuItem(MenuPath, false, 42)]
    static void BuildFromMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
        {
            EditorUtility.DisplayDialog("Build Demo", "Stop Play mode and wait for scripts to compile first.", "OK");
            return;
        }

        DemoMode.Reload();   // a DemoMode.json kept in the project (edited since) wins over DemoConfig's defaults
        int choice = EditorUtility.DisplayDialogComplex("Build Demo (Windows)",
            "Makes a release Windows build of the Steam demo in Builds/Demo/.\n\n" +
            "For the length of the build it parks PlaytestMode.json, writes DemoMode.json, uses the demo's name and version, " +
            DemoBuildPrep.Describe(DemoMode.Config) + ". Everything is put back afterwards, also when the build fails (after a " +
            "crash, on the next Editor start). The portrait steps make the build slower, most of all the first time, when the " +
            "kept portraits are reimported smaller.\n\n" +
            "Run Tools > Game > New UI > Build All Screens and Tools > Game > Localization > Import CSVs first? " +
            "(Recommended: the build uses the generated screens and the imported texts.)",
            "Run both, then build", "Cancel", "Build without them");
        if (choice == 1) return;
        if (choice == 0)
        {
            if (!EditorApplication.ExecuteMenuItem("Tools/Game/New UI/Build All Screens"))
            {
                Debug.LogError("[Demo] Build stopped: Tools/Game/New UI/Build All Screens couldn't run.");
                return;
            }
            if (!EditorApplication.ExecuteMenuItem("Tools/Game/Localization/Import CSVs"))
            {
                Debug.LogError("[Demo] Build stopped: Tools/Game/Localization/Import CSVs couldn't run.");
                return;
            }
        }
        Build();
    }

    [MenuItem(MenuPath, true)]
    static bool BuildValidate() => !EditorApplication.isPlayingOrWillChangePlaymode && !IsBuilding;

    /// <summary>
    /// With Unity closed: Unity.exe -batchmode -projectPath &lt;project&gt; -executeMethod DemoBuild.BuildFromCommandLine -logFile &lt;file&gt;.
    /// Does what the menu's "Run both, then build" does (Build All Screens, Import CSVs, the demo build) and quits with 0 on success.
    /// </summary>
    public static void BuildFromCommandLine()
    {
        BuildResult result = BuildResult.Unknown;
        try
        {
            if (!EditorApplication.ExecuteMenuItem("Tools/Game/New UI/Build All Screens"))
                Debug.LogError("[Demo] Build stopped: Tools/Game/New UI/Build All Screens couldn't run.");
            else if (!EditorApplication.ExecuteMenuItem("Tools/Game/Localization/Import CSVs"))
                Debug.LogError("[Demo] Build stopped: Tools/Game/Localization/Import CSVs couldn't run.");
            else
            {
                DemoMode.Reload();
                result = Build();
            }
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
        Debug.Log("[Demo] Command-line demo build finished: " + result + ".");
        EditorApplication.Exit(result == BuildResult.Succeeded ? 0 : 1);
    }

    /// <summary>Make the demo build. Returns the build result (Unknown when it couldn't start).</summary>
    public static BuildResult Build()
    {
        var scenes = new List<string>();
        foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            if (scene.enabled && !string.IsNullOrEmpty(scene.path)) scenes.Add(scene.path);
        if (scenes.Count == 0)
        {
            Debug.LogError("[Demo] Build stopped: no scene is enabled in Build Settings.");
            return BuildResult.Unknown;
        }

        string playtestFile = DemoModeMenu.PlaytestFilePath;
        string demoFile = DemoModeMenu.FilePath;
        string parkedFile = ParkedFile;
        string parkedMeta = parkedFile + ".meta";

        // An earlier demo build was interrupted: put everything back before anything else
        RecoverInterruptedBuild();

        string oldProductName = PlayerSettings.productName;
        string oldVersion = PlayerSettings.bundleVersion;
        if (oldProductName == DemoProductName)
        {
            Debug.LogError("[Demo] Build stopped: the product name in Player Settings is the demo's (\"" + DemoProductName + "\"). " +
                           "Set the full game's name back first.");
            return BuildResult.Unknown;
        }
        if (!string.IsNullOrEmpty(oldVersion) && oldVersion.EndsWith(VersionSuffix, StringComparison.Ordinal))
        {
            string stripped = oldVersion.Substring(0, oldVersion.Length - VersionSuffix.Length);
            Debug.LogWarning("[Demo] The version in Player Settings was left at \"" + oldVersion + "\": it is restored as \"" + stripped + "\".");
            oldVersion = stripped;
        }
        bool demoFileExisted = File.Exists(demoFile);
        // Demo: a DemoMode.json kept from before wins over DemoConfig's defaults, also its Steam app id
        if (demoFileExisted && !ConfirmKeptAppId(demoFile)) return BuildResult.Unknown;
        bool parked = false;
        BuildResult result = BuildResult.Unknown;

        IsBuilding = true;
        try
        {
            Directory.CreateDirectory(ParkFolder);
            File.WriteAllLines(MarkerFile, new[] { oldProductName, oldVersion ?? "", demoFileExisted ? "0" : "1" });
            if (File.Exists(playtestFile))
            {
                if (File.Exists(parkedFile)) File.Delete(parkedFile);
                if (File.Exists(parkedMeta)) File.Delete(parkedMeta);
                File.Move(playtestFile, parkedFile);
                parked = true;
                if (File.Exists(playtestFile + ".meta")) File.Move(playtestFile + ".meta", parkedMeta);
                Debug.Log("[Demo] Parked " + playtestFile + " in " + ParkFolder + " for the demo build.");
            }
            DemoModeMenu.WriteDefaultFile();
            AssetDatabase.Refresh();
            // The switch file as the player will read it (a kept one may have been edited by hand)
            DemoMode.Reload();
            if (!DemoMode.Active) throw new InvalidOperationException("Couldn't read " + demoFile + " back through Resources.");

            string version = string.IsNullOrEmpty(oldVersion) ? "0.1.0" : oldVersion;
            if (!version.EndsWith(VersionSuffix, StringComparison.Ordinal)) version += VersionSuffix;
            PlayerSettings.productName = DemoProductName;
            PlayerSettings.bundleVersion = version;

            string folder = Path.Combine(ProjectRoot, "Builds", "Demo", SafeFolderName(version));
            Directory.CreateDirectory(folder);
            var options = new BuildPlayerOptions
            {
                scenes = scenes.ToArray(),
                locationPathName = Path.Combine(folder, ExeName),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None
            };
            // A smaller download (portraits parked and capped) and DirectX 11 first: recorded before the first change, undone
            // in finally, and by RecoverInterruptedBuild when a crash interrupts the build
            DemoBuildPrep.Apply(DemoMode.Config);

            Debug.Log("[Demo] Building the demo " + version + " (" + scenes.Count + " scenes) to " + options.locationPathName + " ...");
            BuildReport report = BuildPipeline.BuildPlayer(options);
            result = report.summary.result;
            if (result == BuildResult.Succeeded)
                Debug.Log("[Demo] Demo build succeeded in " + report.summary.totalTime.TotalMinutes.ToString("0.0", CultureInfo.InvariantCulture) +
                          " min: " + FormatSize(FolderSize(folder)) + " in " + folder + ".");
            else
                Debug.LogError("[Demo] Demo build " + result + " (" + report.summary.totalErrors + " errors): see the Console.");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            Debug.LogError("[Demo] Demo build stopped by an error: " + e.Message);
            result = BuildResult.Failed;
        }
        finally
        {
            PlayerSettings.productName = oldProductName;
            PlayerSettings.bundleVersion = oldVersion;
            // Portraits and graphics APIs back before SaveAssets (it logs; on a failure it keeps its record for the next try)
            DemoBuildPrep.Restore(null);
            if (!demoFileExisted && File.Exists(demoFile)) AssetDatabase.DeleteAsset(demoFile);
            if (parked) RestoreParked(parkedFile, parkedMeta, playtestFile);
            // Builds may write ProjectSettings.asset with the demo's name: save the restored values to disk at once
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            DeleteMarker();
            DeleteParkFolderIfEmpty();
            DemoMode.Reload();
            IsBuilding = false;
            Debug.Log("[Demo] Restored the product name (" + oldProductName + "), version (" + oldVersion + ")" +
                      (parked ? ", " + playtestFile : "") + (demoFileExisted ? "" : "; removed " + demoFile) + ".");
        }
        return result;
    }

    /// <summary>Undo what an interrupted demo build left behind: the demo product name and version, the DemoMode.json it
    /// wrote, the parked PlaytestMode.json, and DemoBuildPrep's parked portraits, import settings and graphics APIs.</summary>
    static void RecoverInterruptedBuild()
    {
        string playtestFile = DemoModeMenu.PlaytestFilePath;
        string demoFile = DemoModeMenu.FilePath;
        string parkedFile = ParkedFile;
        var restored = new List<string>();

        if (File.Exists(MarkerFile))
        {
            string[] lines;
            try { lines = File.ReadAllLines(MarkerFile); }
            catch (Exception) { lines = new string[0]; }
            string name = lines.Length > 0 ? lines[0].Trim() : "";
            string version = lines.Length > 1 ? lines[1].Trim() : "";
            bool buildWroteDemoFile = lines.Length > 2 && lines[2].Trim() == "1";

            if (name.Length > 0 && PlayerSettings.productName == DemoProductName)
            {
                PlayerSettings.productName = name;
                restored.Add("product name " + name);
            }
            if (version.Length > 0 && PlayerSettings.bundleVersion.EndsWith(VersionSuffix, StringComparison.Ordinal))
            {
                PlayerSettings.bundleVersion = version;
                restored.Add("version " + version);
            }
            if (buildWroteDemoFile && File.Exists(demoFile))
            {
                AssetDatabase.DeleteAsset(demoFile);
                restored.Add("removed " + demoFile);
            }
            DeleteMarker();
        }
        if (File.Exists(parkedFile) && !File.Exists(playtestFile))
        {
            RestoreParked(parkedFile, parkedFile + ".meta", playtestFile);
            restored.Add(playtestFile);
        }
        DemoBuildPrep.Restore(restored);
        DeleteParkFolderIfEmpty();
        if (restored.Count == 0) return;

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        DemoMode.Reload();
        Debug.LogWarning("[Demo] An earlier demo build was interrupted; put back: " + string.Join(", ", restored) + ".");
    }

    // Demo: an old DemoMode.json (written while the default was 0) would make a demo that never restarts through Steam
    /// <summary>True when the kept DemoMode.json has DemoConfig's Steam app id, or the owner builds anyway (batch mode: a
    /// warning).</summary>
    static bool ConfirmKeptAppId(string demoFile)
    {
        var defaults = new DemoConfig();
        long kept;
        try { kept = JsonUtility.FromJson<DemoConfig>(File.ReadAllText(demoFile))?.steamAppId ?? defaults.steamAppId; }
        catch (Exception e)
        {
            Debug.LogWarning("[Demo] Couldn't read " + demoFile + " (" + e.Message + "): the demo uses DemoConfig's defaults.");
            return true;
        }
        if (kept == defaults.steamAppId) return true;

        string message = demoFile + " is kept from before and has \"steamAppId\": " + kept + ", not the demo app's " +
                         defaults.steamAppId + ". " +
                         (kept <= 0 || kept == defaults.fullGameAppId
                             ? "A copy started outside Steam won't restart through Steam as the demo."
                             : "A copy started outside Steam restarts through Steam as app " + kept + ".") +
                         "\n\nSet \"steamAppId\" to " + defaults.steamAppId + " in the file (or delete it to get every default).";
        if (Application.isBatchMode)
        {
            Debug.LogWarning("[Demo] " + message);
            return true;
        }
        if (EditorUtility.DisplayDialog("Build Demo", message, "Build it anyway", "Cancel")) return true;
        Debug.Log("[Demo] Build cancelled: " + demoFile + " has steamAppId " + kept + ".");
        return false;
    }

    static void DeleteMarker()
    {
        try
        {
            if (File.Exists(MarkerFile)) File.Delete(MarkerFile);
        }
        catch (Exception e)
        {
            Debug.LogError("[Demo] Couldn't delete " + MarkerFile + " (" + e.Message + "): delete it by hand.");
        }
    }

    /// <summary>Delete ParkFolder when nothing is left in it (a kept record or parked file keeps it).</summary>
    static void DeleteParkFolderIfEmpty()
    {
        try
        {
            if (Directory.Exists(ParkFolder) && Directory.GetFileSystemEntries(ParkFolder).Length == 0) Directory.Delete(ParkFolder);
        }
        catch (Exception) { }   // an empty folder left behind changes nothing
    }

    static void RestoreParked(string parkedFile, string parkedMeta, string playtestFile)
    {
        try
        {
            if (File.Exists(parkedFile) && !File.Exists(playtestFile)) File.Move(parkedFile, playtestFile);
            if (File.Exists(parkedMeta) && !File.Exists(playtestFile + ".meta")) File.Move(parkedMeta, playtestFile + ".meta");
        }
        catch (Exception e)
        {
            Debug.LogError("[Demo] Couldn't put " + playtestFile + " back from " + parkedFile + " (" + e.Message + "): move it back by hand.");
        }
    }

    static string SafeFolderName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }

    static long FolderSize(string folder)
    {
        long total = 0;
        try
        {
            foreach (string file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
                total += new FileInfo(file).Length;
        }
        catch (Exception) { }
        return total;
    }

    static string FormatSize(long bytes)
    {
        return bytes >= 1L << 30
            ? (bytes / (double)(1L << 30)).ToString("0.00", CultureInfo.InvariantCulture) + " GB"
            : (bytes / (double)(1L << 20)).ToString("0", CultureInfo.InvariantCulture) + " MB";
    }
}
