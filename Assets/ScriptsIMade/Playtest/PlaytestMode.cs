using System;
using System.Globalization;
using UnityEngine;

/// <summary>
/// Playtest mode, for testing builds and recording trailers. It is on while Assets/Resources/PlaytestMode.json is in the
/// project: Tools > Game > Playtest Mode adds and removes the file, and a build asks first while it's on.
/// - New companies start with StartingValue ($1.5B unless the file's "startingValue" says otherwise: past the $1B that
///   opens the Online City, below the $10B win), not $50,000,
///   and SkillPoints skill points ("skillPoints", 400 by default: the whole tree costs 321).
/// - The casino is open from the start ("openCasino", true by default), without the Casino License skill.
/// - No cloud save: the server knows every company starts at $50,000 and would flag the run. Playtest runs also keep
///   their own local save file, so the real save is never touched.
/// - No Steam achievements: a head start shouldn't unlock them on the developer's account.
/// - A release build started outside Steam runs as it is (SteamPlatform skips the relaunch through Steam).
/// - Demo mode (Resources/DemoMode.json) always wins: while that file exists, playtest mode is off.
/// </summary>
public static class PlaytestMode
{
    /// <summary>The Resources name of the switch file (SteamPlatform checks the same name: it can't see this class).</summary>
    public const string ResourceName = "PlaytestMode";
    public const double DefaultStartingValue = 1500000000;
    public const int DefaultSkillPoints = 400;

    [Serializable]
    class Config
    {
        // A field missing from the file keeps its default here
        public double startingValue = DefaultStartingValue;
        public int skillPoints = DefaultSkillPoints;
        public bool openCasino = true;
    }

    static bool loaded;
    static Config config;

    /// <summary>True while the switch file is in the build.</summary>
    public static bool Active
    {
        get
        {
            Load();
            return config != null;
        }
    }

    /// <summary>What a new company starts with in playtest mode.</summary>
    public static double StartingValue
    {
        get
        {
            Load();
            return config != null && config.startingValue > 0 ? config.startingValue : DefaultStartingValue;
        }
    }

    /// <summary>The skill points a new company starts with in playtest mode (0 outside it).</summary>
    public static int SkillPoints
    {
        get
        {
            Load();
            return config != null ? Math.Max(0, config.skillPoints) : 0;
        }
    }

    /// <summary>True when playtest mode opens the casino without the Casino License skill.</summary>
    public static bool OpenCasino
    {
        get
        {
            Load();
            return config != null && config.openCasino;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        // Entering Play mode without a domain reload keeps statics, and the file may have been added or removed since
        loaded = false;
        config = null;
    }

    static void Load()
    {
        if (loaded) return;
        loaded = true;
        var file = Resources.Load<TextAsset>(ResourceName);
        if (file == null) return;
        // Demo: the demo switch always wins (a demo build must never get playtest money or skip its isolation)
        var demoFile = Resources.Load<TextAsset>(DemoMode.ResourceName);
        if (demoFile != null)
        {
            Resources.UnloadAsset(demoFile);
            Resources.UnloadAsset(file);
            Debug.LogWarning("[Playtest] DemoMode.json is present: demo mode wins, playtest mode is ignored.");
            return;
        }
        try
        {
            config = JsonUtility.FromJson<Config>(file.text) ?? new Config();
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Playtest] Couldn't read Resources/" + ResourceName + ".json (" + e.Message + "): using the defaults.");
            config = new Config();
        }
        Resources.UnloadAsset(file);
        Debug.Log("[Playtest] Playtest mode is on: new companies start with $" +
                  StartingValue.ToString("N0", CultureInfo.InvariantCulture) + " and " + SkillPoints + " skill points" +
                  (OpenCasino ? ", the casino is open" : "") + "; no cloud save, no achievements.");
    }
}
