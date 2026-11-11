using System;
using UnityEngine;

/// <summary>
/// Player settings (volume and display) in one place, saved in PlayerPrefs.
/// - Volumes are 0-1. Setting a value applies it right away and raises <see cref="Changed"/>.
/// - Display settings (fullscreen, quality, resolution) are only applied when the player has chosen them,
///   so the game never forces fullscreen or a resolution on its own. One exception: until a quality level is chosen,
///   a PC with a weak graphics card starts at a lower level (<see cref="ApplyHardwareQuality"/>; not saved as a choice).
/// - Saved settings are applied automatically when the game starts.
/// Call <see cref="Save"/> when a settings screen closes to write PlayerPrefs to disk.
/// </summary>
public static class GameSettings
{
    public const float DefaultMasterVolume = 1f;
    public const float DefaultMusicVolume = 0.7f;
    public const float DefaultSfxVolume = 1f;

    const string MasterVolumeKey = "GameSettings.MasterVolume";
    const string MusicVolumeKey = "GameSettings.MusicVolume";
    const string SfxVolumeKey = "GameSettings.SfxVolume";
    const string FullscreenKey = "GameSettings.Fullscreen";
    const string QualityKey = "GameSettings.QualityLevel";
    const string ResolutionIndexKey = "GameSettings.ResolutionIndex";
    const string ResolutionWidthKey = "GameSettings.ResolutionWidth";
    const string ResolutionHeightKey = "GameSettings.ResolutionHeight";
    const string VersionKey = "GameSettings.Version";
    const int CurrentVersion = 1;

    // Keys used by the three older settings screens (main menu, pause menu, dice), migrated once
    static readonly string[] LegacyKeys =
    {
        "MasterVolume", "MusicVolume", "SFXVolume", "QualityLevel", "ResolutionIndex", "Fullscreen",
        "Settings_MasterVolume", "Settings_MusicVolume", "Settings_SFXVolume", "Settings_Fullscreen",
        "Settings_Quality", "Settings_DiceVolume"
    };

    static bool migrated;

    /// <summary>Raised after any setting changes (from any screen), so open settings UIs can refresh.</summary>
    public static event Action Changed;

    // ------------------------------------------------------------------
    // Audio
    // ------------------------------------------------------------------

    /// <summary>Overall volume (AudioListener.volume), 0-1.</summary>
    public static float MasterVolume
    {
        get { EnsureMigrated(); return Mathf.Clamp01(PlayerPrefs.GetFloat(MasterVolumeKey, DefaultMasterVolume)); }
        set
        {
            if (!SetFloat(MasterVolumeKey, Mathf.Clamp01(value), DefaultMasterVolume)) return;
            AudioListener.volume = MasterVolume;
            Changed?.Invoke();
        }
    }

    /// <summary>Background music volume, 0-1 (applied to MusicManager).</summary>
    public static float MusicVolume
    {
        get { EnsureMigrated(); return Mathf.Clamp01(PlayerPrefs.GetFloat(MusicVolumeKey, DefaultMusicVolume)); }
        set
        {
            if (!SetFloat(MusicVolumeKey, Mathf.Clamp01(value), DefaultMusicVolume)) return;
            if (MusicManager.Instance != null)
                MusicManager.Instance.ApplyVolume(MusicVolume);
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Sound effect volume, 0-1. There is no mixer yet: scripts that play effects multiply their volume by this
    /// (e.g. <c>source.PlayOneShot(clip, GameSettings.SfxVolume)</c>).
    /// </summary>
    public static float SfxVolume
    {
        get { EnsureMigrated(); return Mathf.Clamp01(PlayerPrefs.GetFloat(SfxVolumeKey, DefaultSfxVolume)); }
        set
        {
            if (!SetFloat(SfxVolumeKey, Mathf.Clamp01(value), DefaultSfxVolume)) return;
            Changed?.Invoke();
        }
    }

    // ------------------------------------------------------------------
    // Display
    // ------------------------------------------------------------------

    /// <summary>True once the player has chosen fullscreen or windowed in a settings screen.</summary>
    public static bool HasFullscreenChoice { get { EnsureMigrated(); return PlayerPrefs.HasKey(FullscreenKey); } }

    /// <summary>True once the player has picked a quality level in a settings screen.</summary>
    public static bool HasQualityChoice { get { EnsureMigrated(); return PlayerPrefs.HasKey(QualityKey); } }

    /// <summary>True once the player has picked a resolution in a settings screen.</summary>
    public static bool HasResolutionChoice
    {
        get { EnsureMigrated(); return PlayerPrefs.HasKey(ResolutionWidthKey) && PlayerPrefs.HasKey(ResolutionHeightKey); }
    }

    /// <summary>Fullscreen on/off. Reads the player's saved choice, or the current window state if they never chose.</summary>
    public static bool Fullscreen
    {
        get { EnsureMigrated(); return PlayerPrefs.HasKey(FullscreenKey) ? PlayerPrefs.GetInt(FullscreenKey) == 1 : Screen.fullScreen; }
        set
        {
            EnsureMigrated();
            PlayerPrefs.SetInt(FullscreenKey, value ? 1 : 0);
            ApplyFullscreen(value);
            Changed?.Invoke();
        }
    }

    /// <summary>Quality level index (QualitySettings.names). Reads the saved choice, or the current level.</summary>
    public static int QualityLevel
    {
        get
        {
            EnsureMigrated();
            return ClampQuality(PlayerPrefs.GetInt(QualityKey, QualitySettings.GetQualityLevel()));
        }
        set
        {
            EnsureMigrated();
            int level = ClampQuality(value);
            PlayerPrefs.SetInt(QualityKey, level);
            ApplyQuality(level);
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Index into Screen.resolutions of the player's chosen resolution, or -1 when they never chose one
    /// (or it isn't available on this display). Setting it applies the resolution and keeps the fullscreen state.
    /// </summary>
    public static int ResolutionIndex
    {
        get
        {
            if (!HasResolutionChoice) return -1;
            Resolution[] list = Screen.resolutions;
            int width = PlayerPrefs.GetInt(ResolutionWidthKey, 0);
            int height = PlayerPrefs.GetInt(ResolutionHeightKey, 0);
            int saved = PlayerPrefs.GetInt(ResolutionIndexKey, -1);
            if (saved >= 0 && saved < list.Length && list[saved].width == width && list[saved].height == height)
                return saved;
            // The list can change between displays: find the same size (highest refresh rate is last)
            for (int i = list.Length - 1; i >= 0; i--)
                if (list[i].width == width && list[i].height == height) return i;
            return -1;
        }
        set
        {
            EnsureMigrated();
            Resolution[] list = Screen.resolutions;
            if (value < 0 || value >= list.Length) return;
            Resolution res = list[value];
            PlayerPrefs.SetInt(ResolutionIndexKey, value);
            PlayerPrefs.SetInt(ResolutionWidthKey, res.width);
            PlayerPrefs.SetInt(ResolutionHeightKey, res.height);
            ApplyResolution(res.width, res.height, Screen.fullScreenMode);
            Changed?.Invoke();
        }
    }

    // ------------------------------------------------------------------
    // Apply / save
    // ------------------------------------------------------------------

    /// <summary>Apply every saved setting: volumes always, display settings only where the player made a choice.</summary>
    public static void ApplyAll()
    {
        ApplyAudio();
        ApplyDisplay();
    }

    /// <summary>Apply master volume (AudioListener) and music volume (MusicManager).</summary>
    public static void ApplyAudio()
    {
        AudioListener.volume = MasterVolume;
        if (MusicManager.Instance != null)
            MusicManager.Instance.ApplyVolume(MusicVolume);
    }

    /// <summary>Apply only the display settings the player explicitly chose (never forces fullscreen).</summary>
    public static void ApplyDisplay()
    {
        if (HasQualityChoice)
            ApplyQuality(QualityLevel);

        FullScreenMode mode = Screen.fullScreenMode;
        if (HasFullscreenChoice)
            mode = ModeFor(Fullscreen);

        if (HasResolutionChoice)
            ApplyResolution(PlayerPrefs.GetInt(ResolutionWidthKey, 0), PlayerPrefs.GetInt(ResolutionHeightKey, 0), mode);
        else if (HasFullscreenChoice)
            ApplyFullscreen(Fullscreen);
    }

    /// <summary>Write settings to disk (PlayerPrefs.Save). Call when a settings screen closes.</summary>
    public static void Save()
    {
        EnsureMigrated();
        PlayerPrefs.Save();
    }

    // ------------------------------------------------------------------
    // Startup
    // ------------------------------------------------------------------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        // Needed when domain reload is disabled in the Editor's play mode options
        Changed = null;
        migrated = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void ApplyOnStartup()
    {
        EnsureMigrated();
        ApplyAudio();
        // In the Editor, these would change the Editor's own quality level and Game view, so builds only
        if (!Application.isEditor)
        {
            if (!HasQualityChoice)
                ApplyHardwareQuality();
            ApplyDisplay();
        }
    }

    // Quality levels by name, lowest first (the HDRP template's levels)
    static readonly string[] QualityNamesLowestFirst = { "Performant", "Balanced", "High Fidelity" };

    /// <summary>
    /// While no quality level is chosen: a PC with Intel's built-in graphics, under 3 GB of video memory or under 6 GB of
    /// memory starts at Performant, one under 6 GB of video memory at Balanced, so its first minutes (and the first
    /// launch's shader compiling) don't crawl. Never raises the level and isn't saved: the settings screen shows it and
    /// the player can pick another. Does nothing when the levels aren't named as in <see cref="QualityNamesLowestFirst"/>.
    /// </summary>
    static void ApplyHardwareQuality()
    {
        int current = QualitySettings.GetQualityLevel();
        int currentRank = QualityRank(current);
        int targetRank = HardwareQualityRank();
        if (currentRank < 0 || targetRank >= currentRank) return;

        string[] names = QualitySettings.names;
        int target = Array.FindIndex(names, n => string.Equals(n, QualityNamesLowestFirst[targetRank], StringComparison.OrdinalIgnoreCase));
        if (target < 0) return;

        QualitySettings.SetQualityLevel(target, true);
        Debug.Log($"[GameSettings] No quality level chosen yet: {names[target]} instead of {names[current]} on this PC " +
                  $"({SystemInfo.graphicsDeviceName}, {SystemInfo.graphicsMemorySize} MB video memory, {SystemInfo.systemMemorySize} MB memory).");
    }

    /// <summary>Position of a quality level in <see cref="QualityNamesLowestFirst"/>, or -1 when it has another name.</summary>
    static int QualityRank(int level)
    {
        string[] names = QualitySettings.names;
        if (level < 0 || level >= names.Length) return -1;
        return Array.FindIndex(QualityNamesLowestFirst, n => string.Equals(n, names[level], StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The quality this PC's hardware suits: 0 Performant, 1 Balanced, 2 High Fidelity.</summary>
    static int HardwareQualityRank()
    {
        string gpu = SystemInfo.graphicsDeviceName ?? "";
        // Intel's built-in graphics (UHD, Iris); its Arc cards are real graphics cards
        bool intelBuiltIn = gpu.IndexOf("Intel", StringComparison.OrdinalIgnoreCase) >= 0 &&
                            gpu.IndexOf("Arc", StringComparison.OrdinalIgnoreCase) < 0;
        if (intelBuiltIn || SystemInfo.graphicsMemorySize < 3000 || SystemInfo.systemMemorySize < 6000) return 0;
        if (SystemInfo.graphicsMemorySize < 6000) return 1;
        return 2;
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    static void ApplyFullscreen(bool fullscreen)
    {
        FullScreenMode mode = ModeFor(fullscreen);
        if (Screen.fullScreenMode != mode)
            Screen.fullScreenMode = mode;
    }

    static FullScreenMode ModeFor(bool fullscreen)
    {
        if (!fullscreen) return FullScreenMode.Windowed;
        // Keep exclusive fullscreen if the player already uses it
        return Screen.fullScreenMode == FullScreenMode.Windowed ? FullScreenMode.FullScreenWindow : Screen.fullScreenMode;
    }

    static void ApplyQuality(int level)
    {
        if (QualitySettings.names.Length == 0) return;
        if (QualitySettings.GetQualityLevel() != level)
            QualitySettings.SetQualityLevel(level, true);
    }

    static void ApplyResolution(int width, int height, FullScreenMode mode)
    {
        if (width <= 0 || height <= 0) return;
        if (Screen.width == width && Screen.height == height && Screen.fullScreenMode == mode) return;
        Screen.SetResolution(width, height, mode);
    }

    static int ClampQuality(int level)
    {
        return Mathf.Clamp(level, 0, Mathf.Max(0, QualitySettings.names.Length - 1));
    }

    /// <summary>Store a float; returns true if the stored value changed.</summary>
    static bool SetFloat(string key, float value, float fallback)
    {
        EnsureMigrated();
        bool existed = PlayerPrefs.HasKey(key);
        float old = PlayerPrefs.GetFloat(key, fallback);
        PlayerPrefs.SetFloat(key, value);
        return !existed || !Mathf.Approximately(old, value);
    }

    /// <summary>One-time move from the three older settings systems to these keys.</summary>
    static void EnsureMigrated()
    {
        if (migrated) return;
        migrated = true;
        if (PlayerPrefs.GetInt(VersionKey, 0) >= CurrentVersion) return;

        // Volumes: the first old key that exists wins. "Settings_MasterVolume" was written last (pause menu
        // and dice), so it's what the player heard most recently. A dice roll of 2 used to store 0 (muted).
        MigrateFloat(MasterVolumeKey, 0.1f, "Settings_MasterVolume", "MasterVolume");
        MigrateFloat(MusicVolumeKey, 0f, "MusicVolume", "Settings_MusicVolume");
        MigrateFloat(SfxVolumeKey, 0f, "SFXVolume", "Settings_SFXVolume");

        // Display: the main menu only stored these when the player changed them. The old pause menu saved
        // fullscreen = on by default, so "Settings_Fullscreen" isn't a real choice and is dropped.
        MigrateInt(FullscreenKey, "Fullscreen");
        MigrateInt(QualityKey, "QualityLevel", "Settings_Quality");
        if (!PlayerPrefs.HasKey(ResolutionWidthKey) && PlayerPrefs.HasKey("ResolutionIndex"))
        {
            int index = PlayerPrefs.GetInt("ResolutionIndex");
            Resolution[] list = Screen.resolutions;
            if (index >= 0 && index < list.Length)
            {
                PlayerPrefs.SetInt(ResolutionIndexKey, index);
                PlayerPrefs.SetInt(ResolutionWidthKey, list[index].width);
                PlayerPrefs.SetInt(ResolutionHeightKey, list[index].height);
            }
        }

        foreach (string key in LegacyKeys)
            PlayerPrefs.DeleteKey(key);

        PlayerPrefs.SetInt(VersionKey, CurrentVersion);
        PlayerPrefs.Save();
    }

    static void MigrateFloat(string newKey, float min, params string[] oldKeys)
    {
        if (PlayerPrefs.HasKey(newKey)) return;
        foreach (string oldKey in oldKeys)
        {
            if (!PlayerPrefs.HasKey(oldKey)) continue;
            PlayerPrefs.SetFloat(newKey, Mathf.Clamp(PlayerPrefs.GetFloat(oldKey), min, 1f));
            return;
        }
    }

    static void MigrateInt(string newKey, params string[] oldKeys)
    {
        if (PlayerPrefs.HasKey(newKey)) return;
        foreach (string oldKey in oldKeys)
        {
            if (!PlayerPrefs.HasKey(oldKey)) continue;
            PlayerPrefs.SetInt(newKey, PlayerPrefs.GetInt(oldKey));
            return;
        }
    }
}
