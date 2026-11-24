using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Tools > Game > Build Test (Windows, Development): a Development build of the full game for the owner's own testing, in
/// Builds/Test/&lt;date-time&gt;/. Development, so it runs from its folder without Steam relaunching the Steam copy (see
/// igbfb-startup-performance). Playtest mode stays as the project has it. Not for Steam uploads.
/// With Unity closed: Unity.exe -batchmode -projectPath &lt;project&gt; -executeMethod TestBuild.BuildFromCommandLine -logFile &lt;file&gt;
/// (runs Build All Screens and Import CSVs first, like the demo build, and quits with 0 on success).
/// </summary>
public static class TestBuild
{
    const string MenuPath = "Tools/Game/Build Test (Windows, Development)";
    const string ExeName = "IsGamblingBadForBusiness.exe";

    static string ProjectRoot => Path.GetDirectoryName(Application.dataPath) ?? Directory.GetCurrentDirectory();

    [MenuItem(MenuPath, false, 43)]
    static void BuildFromMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
        {
            EditorUtility.DisplayDialog("Build Test", "Stop Play mode and wait for scripts to compile first.", "OK");
            return;
        }
        if (!PrepareScreensAndTexts()) return;
        BuildReport report = Build(out string folder);
        if (report != null && report.summary.result == BuildResult.Succeeded) EditorUtility.RevealInFinder(Path.Combine(folder, ExeName));
    }

    public static void BuildFromCommandLine()
    {
        BuildResult result = BuildResult.Unknown;
        try
        {
            if (PrepareScreensAndTexts())
            {
                BuildReport report = Build(out _);
                if (report != null) result = report.summary.result;
            }
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
        Debug.Log("[TestBuild] Finished: " + result + ".");
        EditorApplication.Exit(result == BuildResult.Succeeded ? 0 : 1);
    }

    static bool PrepareScreensAndTexts()
    {
        if (!EditorApplication.ExecuteMenuItem("Tools/Game/New UI/Build All Screens"))
        {
            Debug.LogError("[TestBuild] Stopped: Tools/Game/New UI/Build All Screens couldn't run.");
            return false;
        }
        if (!EditorApplication.ExecuteMenuItem("Tools/Game/Localization/Import CSVs"))
        {
            Debug.LogError("[TestBuild] Stopped: Tools/Game/Localization/Import CSVs couldn't run.");
            return false;
        }
        return true;
    }

    static BuildReport Build(out string folder)
    {
        var scenes = new List<string>();
        foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            if (scene.enabled && !string.IsNullOrEmpty(scene.path)) scenes.Add(scene.path);
        folder = Path.Combine(ProjectRoot, "Builds", "Test", DateTime.Now.ToString("yyyy-MM-dd_HHmm", CultureInfo.InvariantCulture));
        if (scenes.Count == 0)
        {
            Debug.LogError("[TestBuild] Stopped: no scene is enabled in Build Settings.");
            return null;
        }
        Directory.CreateDirectory(folder);
        var options = new BuildPlayerOptions
        {
            scenes = scenes.ToArray(),
            locationPathName = Path.Combine(folder, ExeName),
            target = BuildTarget.StandaloneWindows64,
            targetGroup = BuildTargetGroup.Standalone,
            options = BuildOptions.Development
        };
        Debug.Log("[TestBuild] Building " + PlayerSettings.productName + " " + PlayerSettings.bundleVersion + " (Development, " + scenes.Count + " scenes) to " + options.locationPathName);
        BuildReport report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result == BuildResult.Succeeded)
            Debug.Log("[TestBuild] Succeeded in " + report.summary.totalTime.TotalMinutes.ToString("0.0", CultureInfo.InvariantCulture) + " min: " + folder);
        else
            Debug.LogError("[TestBuild] " + report.summary.result + " (" + report.summary.totalErrors + " errors).");
        return report;
    }
}
