using System;
using System.Diagnostics;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

/// <summary>
/// Startup timing in the Player.log, to find out what makes a launch slow on someone else's PC. Builds only (nothing
/// happens in the Editor).
/// - The PC first: graphics card, graphics API, video memory, processor, memory, screen and quality level.
/// - Stages with the seconds since launch: "[Startup] +4.1s Scene loaded: SplashScreen", "+22.9s Splash done: loading
///   the main menu", "+24.0s Scene loaded: Main_Menu". Other scripts add their own with <see cref="Mark"/>.
/// - Every frame that took longer than <see cref="SlowFrameSeconds"/> in the first <see cref="WatchSeconds"/> after
///   launch and the first <see cref="AfterLoadWatchSeconds"/> after each scene load, with the scene it was in, then a
///   summary. Long frames mean the graphics driver is compiling shaders (the first launch on a PC) or a script blocks.
///   A long stretch between two stages with no slow frames means loading or waiting.
/// The log is %USERPROFILE%\AppData\LocalLow\Mchunterr\IsGamblingBadForBusiness\Player.log (Player-prev.log is the
/// launch before).
/// </summary>
public class StartupTimeline : MonoBehaviour
{
    public const float SlowFrameSeconds = 0.5f;
    public const float WatchSeconds = 300f;
    public const float AfterLoadWatchSeconds = 20f;

    static StartupTimeline s_Instance;

    double lastFrame;
    double watchUntil;
    double windowStart;
    int slowFrames;
    double slowTotal;
    double longest;
    string longestScene;
    bool watching;
    bool firstFrameLogged;

    /// <summary>Log a startup stage with the seconds since launch. Builds only.</summary>
    public static void Mark(string stage)
    {
        if (Application.isEditor) return;
        Debug.Log($"[Startup] +{Time.realtimeSinceStartupAsDouble:F1}s {stage}");
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
    static void BeforeSplashScreen()
    {
        if (Application.isEditor) return;
        string opened = "";
        try
        {
            // Time Windows needed before the engine ran any script (antivirus scans and a slow disk show up here)
            double sinceOpened = (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalSeconds;
            opened = $" ({sinceOpened:F1} s after the program was opened)";
        }
        catch (Exception) { }
        Mark("Engine ready" + opened);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void BeforeSceneLoad()
    {
        if (Application.isEditor) return;
        Mark("Loading the first scene. PC: " + DescribePc());
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AfterSceneLoad()
    {
        if (Application.isEditor || s_Instance != null) return;
        var go = new GameObject("StartupTimeline");
        DontDestroyOnLoad(go);
        s_Instance = go.AddComponent<StartupTimeline>();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Mark($"Scene loaded: {scene.name} (quality {QualityName()})");
        if (s_Instance != null) s_Instance.Watch(AfterLoadWatchSeconds);
    }

    void Awake()
    {
        lastFrame = Time.realtimeSinceStartupAsDouble;
        Watch(Math.Max(0.0, WatchSeconds - lastFrame));
    }

    void Watch(double seconds)
    {
        double now = Time.realtimeSinceStartupAsDouble;
        if (!watching)
        {
            watching = true;
            windowStart = now;
            slowFrames = 0;
            slowTotal = 0;
            longest = 0;
            longestScene = null;
        }
        watchUntil = Math.Max(watchUntil, now + seconds);
    }

    void Update()
    {
        double now = Time.realtimeSinceStartupAsDouble;
        double gap = now - lastFrame;
        lastFrame = now;

        if (!firstFrameLogged)
        {
            firstFrameLogged = true;
            Mark("First frame");
        }

        if (!watching) return;

        if (gap >= SlowFrameSeconds)
        {
            string scene = SceneManager.GetActiveScene().name;
            slowFrames++;
            slowTotal += gap;
            if (gap > longest)
            {
                longest = gap;
                longestScene = scene;
            }
            Mark($"Slow frame: {gap:F2} s ({scene}, frame {Time.frameCount})");
        }

        if (now >= watchUntil)
        {
            watching = false;
            Mark(slowFrames == 0
                ? $"No slow frames since +{windowStart:F1}s"
                : $"Since +{windowStart:F1}s: {slowFrames} slow frame(s), {slowTotal:F1} s in total, the longest {longest:F1} s ({longestScene})");
        }
    }

    static string QualityName()
    {
        string[] names = QualitySettings.names;
        int level = QualitySettings.GetQualityLevel();
        return level >= 0 && level < names.Length ? names[level] : level.ToString();
    }

    static string DescribePc()
    {
        var text = new StringBuilder();
        text.Append(SystemInfo.graphicsDeviceName)
            .Append(" (").Append(SystemInfo.graphicsDeviceVersion)
            .Append(", ").Append(SystemInfo.graphicsMemorySize).Append(" MB video memory); ")
            .Append(SystemInfo.processorType).Append(", ").Append(SystemInfo.processorCount).Append(" threads; ")
            .Append(SystemInfo.systemMemorySize).Append(" MB memory; ")
            .Append(SystemInfo.operatingSystem).Append("; screen ")
            .Append(Screen.width).Append('x').Append(Screen.height).Append(' ').Append(Screen.fullScreenMode)
            .Append("; quality ").Append(QualityName());
        return text.ToString();
    }
}
