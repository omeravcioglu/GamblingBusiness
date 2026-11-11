// The same test as SteamPlatform.cs (a #define only covers its own file): Steam is compiled in only when Steamworks.NET is
// installed (Game.Steam.asmdef's version defines) on a platform it supports.
#if STEAMWORKS_NET && GAME_STEAMWORKS_PACKAGE && (UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX || STEAMWORKS_WIN || STEAMWORKS_LIN_OSX)
#define GAME_STEAM
#endif

using System;
using System.Collections.Generic;
using UnityEngine;
#if GAME_STEAM
using Steamworks;
#endif

/// <summary>How a call to Steam's achievements and stats went (SteamAchievements).</summary>
public enum SteamAchievementResult
{
    /// <summary>Steam took it.</summary>
    Ok,
    /// <summary>Steam isn't running, or the user's stats haven't arrived yet: try again later.</summary>
    NotReady,
    /// <summary>Steam doesn't know this achievement or stat: not created in Steamworks yet (or not published), or a typo.</summary>
    Unknown,
    /// <summary>Anything else (logged).</summary>
    Failed
}

/// <summary>
/// Steam's achievements and stats (ISteamUserStats of Steamworks.NET / SDK 1.64), the only place that calls them. Safe in any
/// build: without Steamworks.NET (or on a platform it doesn't support) every call is a stub that answers NotReady.
/// - SDK 1.61+ has no RequestCurrentStats: Steam loads the user's stats when SteamAPI starts and sends UserStatsReceived_t.
///   IsReady is true once that arrived (or once a probe shows the achievement schema is loaded, or StatsWaitSeconds after
///   Steam started, whichever comes first: a failed call is simply tried again later). StatsReceived is raised each time.
/// - Unlock / SetStat / IndicateProgress change Steam's local copy; Store sends it (Steam rate-limits StoreStats, so the
///   caller batches). Steam ignores an unlock of an achievement that is already unlocked.
/// - An achievement or stat Steam doesn't know (not created in Steamworks yet, or the changes not published) answers
///   Unknown: SetAchievement returns false for it. The caller keeps it queued and logs it once.
/// - ResetAll wipes the user's stats (and achievements) on Steam: a testing tool.
/// The game's own logic (definitions, progress, queue) is AchievementSystem in Assets/ScriptsIMade/Achievements.
/// </summary>
public static class SteamAchievements
{
    /// <summary>Seconds after Steam started before the stats count as loaded even without UserStatsReceived_t.</summary>
    public const float StatsWaitSeconds = 15f;

    static bool statsReceived;
    static bool assumedReady;
    static bool wasInitialized;
    static float initSeenAt = -1f;
    static readonly HashSet<string> loggedErrors = new HashSet<string>();

#if GAME_STEAM
    static bool loggedNoSchema;
    static Callback<UserStatsReceived_t> receivedCallback;
    static Callback<UserStatsStored_t> storedCallback;
    static Callback<UserAchievementStored_t> achievementStoredCallback;
#endif

    /// <summary>The user's stats and achievements arrived from Steam (again): re-read them.</summary>
    public static event Action StatsReceived;

    /// <summary>
    /// Steam answered a Store: true = stored; false = Steam rejected a stat (out of date or breaking its rules: Steam sends
    /// its values back, so re-read the stats).
    /// </summary>
#pragma warning disable CS0414, CS0067 // never raised in builds without Steam
    public static event Action<bool> StatsStored;
#pragma warning restore CS0414, CS0067

    /// <summary>True when this build has Steamworks.NET for this platform.</summary>
    public static bool IsCompiledIn => SteamPlatform.IsCompiledIn;

    /// <summary>Steam runs and the user's stats are in (or assumed to be, see StatsWaitSeconds): the calls below can work.</summary>
    public static bool IsReady
    {
        get
        {
            Tick();
            return SteamPlatform.IsInitialized && (statsReceived || assumedReady);
        }
    }

    /// <summary>Steam confirmed that the user's stats arrived (UserStatsReceived_t, or the loaded schema).</summary>
    public static bool HasStats => SteamPlatform.IsInitialized && statsReceived;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        // Entering Play mode without a domain reload keeps statics (Steam itself was shut down when Play mode ended)
        statsReceived = false;
        assumedReady = false;
        wasInitialized = false;
        initSeenAt = -1f;
        loggedErrors.Clear();
        StatsReceived = null;
        StatsStored = null;
#if GAME_STEAM
        loggedNoSchema = false;
        receivedCallback = null;
        storedCallback = null;
        achievementStoredCallback = null;
#endif
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        // SteamPlatform starts Steam before the first scene loads: the callbacks are registered before its first
        // RunCallbacks can deliver UserStatsReceived_t
        Tick();
    }

    /// <summary>
    /// Call often (every frame is fine; the properties call it too): registers Steam's callbacks once Steam runs and notices
    /// when the user's stats are in. Forgets everything when Steam stops.
    /// </summary>
    public static void Tick()
    {
        // Demo: idle in a demo build (the demo's app has no stats or achievements, and AchievementSystem is off there)
        bool initialized = SteamPlatform.IsInitialized && !SteamPlatform.IsDemoBuild;
        if (!initialized)
        {
            if (wasInitialized) Forget();
            return;
        }
        wasInitialized = true;
        if (initSeenAt < 0f) initSeenAt = Time.realtimeSinceStartup;
#if GAME_STEAM
        EnsureCallbacks();
        if (statsReceived) return;
        try
        {
            // The schema comes with the stats: achievements known = the stats are loaded
            if (SteamUserStats.GetNumAchievements() > 0)
            {
                MarkReceived("the achievement list is loaded");
                return;
            }
        }
        catch (Exception e)
        {
            LogOnce("probe", "[Steam] Achievements: reading the achievement list failed (" + e.Message + ").");
        }
        if (!assumedReady && Time.realtimeSinceStartup - initSeenAt > StatsWaitSeconds)
        {
            assumedReady = true;
            Debug.Log("[Steam] Achievements: no stats from Steam after " + StatsWaitSeconds.ToString("0") +
                      " s (none configured for app " + SteamPlatform.AppId + " yet?). Trying anyway; failed calls are repeated later.");
        }
#endif
    }

    static void Forget()
    {
        wasInitialized = false;
        statsReceived = false;
        assumedReady = false;
        initSeenAt = -1f;
#if GAME_STEAM
        try { receivedCallback?.Dispose(); } catch (Exception) { }
        try { storedCallback?.Dispose(); } catch (Exception) { }
        try { achievementStoredCallback?.Dispose(); } catch (Exception) { }
        receivedCallback = null;
        storedCallback = null;
        achievementStoredCallback = null;
#endif
    }

    // ------------------------------------------------------------------
    // Achievements
    // ------------------------------------------------------------------

    /// <summary>Unlock an achievement in Steam's local copy (Store sends it). Unlocking one that is already unlocked is fine.</summary>
    public static SteamAchievementResult Unlock(string apiName)
    {
        if (string.IsNullOrEmpty(apiName)) return SteamAchievementResult.Failed;
        if (!IsReady) return SteamAchievementResult.NotReady;
#if GAME_STEAM
        try
        {
            if (SteamUserStats.SetAchievement(apiName)) return SteamAchievementResult.Ok;
            return statsReceived ? SteamAchievementResult.Unknown : SteamAchievementResult.NotReady;
        }
        catch (Exception e)
        {
            LogOnce("set:" + apiName, "[Steam] SetAchievement(" + apiName + ") failed: " + e.Message);
            return SteamAchievementResult.Failed;
        }
#else
        return SteamAchievementResult.NotReady;
#endif
    }

    /// <summary>Is the achievement unlocked on Steam? False (with achieved = false) when Steam can't tell (not ready, unknown name).</summary>
    public static bool TryGetUnlocked(string apiName, out bool achieved)
    {
        achieved = false;
        if (string.IsNullOrEmpty(apiName) || !IsReady) return false;
#if GAME_STEAM
        try { return SteamUserStats.GetAchievement(apiName, out achieved); }
        catch (Exception e)
        {
            LogOnce("get:" + apiName, "[Steam] GetAchievement(" + apiName + ") failed: " + e.Message);
            achieved = false;
            return false;
        }
#else
        return false;
#endif
    }

    /// <summary>Lock an achievement again in Steam's local copy (testing; Store sends it).</summary>
    public static SteamAchievementResult Clear(string apiName)
    {
        if (string.IsNullOrEmpty(apiName)) return SteamAchievementResult.Failed;
        if (!IsReady) return SteamAchievementResult.NotReady;
#if GAME_STEAM
        try
        {
            if (SteamUserStats.ClearAchievement(apiName)) return SteamAchievementResult.Ok;
            return statsReceived ? SteamAchievementResult.Unknown : SteamAchievementResult.NotReady;
        }
        catch (Exception e)
        {
            LogOnce("clear:" + apiName, "[Steam] ClearAchievement(" + apiName + ") failed: " + e.Message);
            return SteamAchievementResult.Failed;
        }
#else
        return SteamAchievementResult.NotReady;
#endif
    }

    /// <summary>
    /// Steam's "x / y" progress pop-up for a locked achievement (progress must be below max; at max unlock it instead).
    /// Steam shows a notification every time: call it at milestones only.
    /// </summary>
    public static SteamAchievementResult IndicateProgress(string apiName, long progress, long max)
    {
        if (string.IsNullOrEmpty(apiName) || max <= 0) return SteamAchievementResult.Failed;
        if (!IsReady) return SteamAchievementResult.NotReady;
#if GAME_STEAM
        try
        {
            uint cap = (uint)Math.Min(max, uint.MaxValue);
            uint now = (uint)Math.Max(0L, Math.Min(progress, (long)cap - 1));
            if (SteamUserStats.IndicateAchievementProgress(apiName, now, cap)) return SteamAchievementResult.Ok;
            return statsReceived ? SteamAchievementResult.Unknown : SteamAchievementResult.NotReady;
        }
        catch (Exception e)
        {
            LogOnce("progress:" + apiName, "[Steam] IndicateAchievementProgress(" + apiName + ") failed: " + e.Message);
            return SteamAchievementResult.Failed;
        }
#else
        return SteamAchievementResult.NotReady;
#endif
    }

    /// <summary>Achievements Steam knows for this app (0 when not ready or none are configured).</summary>
    public static int AchievementCount
    {
        get
        {
            if (!IsReady) return 0;
#if GAME_STEAM
            try { return (int)Math.Min(int.MaxValue, SteamUserStats.GetNumAchievements()); }
            catch (Exception) { return 0; }
#else
            return 0;
#endif
        }
    }

    /// <summary>API name of Steam's achievement number <paramref name="index"/> ("" when there is none).</summary>
    public static string AchievementNameAt(int index)
    {
        if (index < 0 || !IsReady) return "";
#if GAME_STEAM
        try { return SteamUserStats.GetAchievementName((uint)index) ?? ""; }
        catch (Exception) { return ""; }
#else
        return "";
#endif
    }

    /// <summary>What Steamworks shows for an achievement: key "name", "desc" or "hidden" ("" when unknown).</summary>
    public static string DisplayAttribute(string apiName, string key)
    {
        if (string.IsNullOrEmpty(apiName) || string.IsNullOrEmpty(key) || !IsReady) return "";
#if GAME_STEAM
        try { return SteamUserStats.GetAchievementDisplayAttribute(apiName, key) ?? ""; }
        catch (Exception) { return ""; }
#else
        return "";
#endif
    }

    // ------------------------------------------------------------------
    // Stats (INT stats in Steamworks)
    // ------------------------------------------------------------------

    /// <summary>Set an INT stat in Steam's local copy (Store sends it). An "increment only" stat refuses a lower value at Store.</summary>
    public static SteamAchievementResult SetStat(string statName, int value)
    {
        if (string.IsNullOrEmpty(statName)) return SteamAchievementResult.Failed;
        if (!IsReady) return SteamAchievementResult.NotReady;
#if GAME_STEAM
        try
        {
            if (SteamUserStats.SetStat(statName, value)) return SteamAchievementResult.Ok;
            return statsReceived ? SteamAchievementResult.Unknown : SteamAchievementResult.NotReady;
        }
        catch (Exception e)
        {
            LogOnce("setstat:" + statName, "[Steam] SetStat(" + statName + ") failed: " + e.Message);
            return SteamAchievementResult.Failed;
        }
#else
        return SteamAchievementResult.NotReady;
#endif
    }

    /// <summary>Read an INT stat (false when Steam can't tell: not ready, or a stat it doesn't know).</summary>
    public static bool TryGetStat(string statName, out int value)
    {
        value = 0;
        if (string.IsNullOrEmpty(statName) || !IsReady) return false;
#if GAME_STEAM
        try { return SteamUserStats.GetStat(statName, out value); }
        catch (Exception e)
        {
            LogOnce("getstat:" + statName, "[Steam] GetStat(" + statName + ") failed: " + e.Message);
            value = 0;
            return false;
        }
#else
        return false;
#endif
    }

    /// <summary>Send Steam's local copy (unlocks and stats) to the server. Rate-limited by Steam: batch changes.</summary>
    public static bool Store()
    {
        if (!IsReady) return false;
#if GAME_STEAM
        try { return SteamUserStats.StoreStats(); }
        catch (Exception e)
        {
            LogOnce("store", "[Steam] StoreStats failed: " + e.Message);
            return false;
        }
#else
        return false;
#endif
    }

    /// <summary>Testing: wipe the user's stats (and achievements) on Steam. Steam stores it by itself.</summary>
    public static bool ResetAll(bool achievementsToo)
    {
        if (!IsReady) return false;
#if GAME_STEAM
        try { return SteamUserStats.ResetAllStats(achievementsToo); }
        catch (Exception e)
        {
            Debug.LogWarning("[Steam] ResetAllStats failed: " + e.Message);
            return false;
        }
#else
        return false;
#endif
    }

    /// <summary>One line for logs and tools.</summary>
    public static string Describe()
    {
        if (!IsCompiledIn) return "Steam achievements: not in this build (Steamworks.NET missing, or a platform it doesn't support).";
        if (!SteamPlatform.IsInitialized) return "Steam achievements: Steam isn't running (" + (SteamPlatform.InitError ?? "not started yet") + ").";
        if (statsReceived)
            return "Steam achievements: ready, " + AchievementCount + " achievement(s) configured for app " + SteamPlatform.AppId + ".";
        if (assumedReady)
            return "Steam achievements: Steam runs but sent no stats (none configured for app " + SteamPlatform.AppId + " yet?).";
        return "Steam achievements: waiting for the user's stats from Steam.";
    }

    // ------------------------------------------------------------------

    static void MarkReceived(string how)
    {
        bool first = !statsReceived;
        statsReceived = true;
        if (first) Debug.Log("[Steam] Achievements: the user's stats are in (" + how + ").");
        Action handlers = StatsReceived;
        if (handlers == null) return;
        foreach (Action handler in handlers.GetInvocationList())
        {
            try { handler(); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }

    static void LogOnce(string key, string message)
    {
        if (loggedErrors.Add(key)) Debug.LogWarning(message);
    }

#if GAME_STEAM
    static void EnsureCallbacks()
    {
        try
        {
            if (receivedCallback == null) receivedCallback = Callback<UserStatsReceived_t>.Create(OnStatsReceived);
            if (storedCallback == null) storedCallback = Callback<UserStatsStored_t>.Create(OnStatsStored);
            if (achievementStoredCallback == null) achievementStoredCallback = Callback<UserAchievementStored_t>.Create(OnAchievementStored);
        }
        catch (Exception e)
        {
            LogOnce("callbacks", "[Steam] Achievements: registering Steam's callbacks failed (" + e.Message + ").");
        }
    }

    static bool IsOurGame(ulong gameId)
    {
        return gameId == 0 || gameId == SteamPlatform.AppId;
    }

    static void OnStatsReceived(UserStatsReceived_t data)
    {
        if (!IsOurGame(data.m_nGameID)) return;
        ulong me = SteamPlatform.SteamId;
        if (me != 0 && data.m_steamIDUser.m_SteamID != 0 && data.m_steamIDUser.m_SteamID != me) return;   // someone else's stats
        // Fail means "no stats yet" for a user who never played: the schema is loaded all the same
        if (data.m_eResult == EResult.k_EResultOK || data.m_eResult == EResult.k_EResultFail)
        {
            MarkReceived(data.m_eResult == EResult.k_EResultOK ? "UserStatsReceived" : "UserStatsReceived, no stats stored yet");
            if (!loggedNoSchema && AchievementCountRaw() == 0)
            {
                loggedNoSchema = true;
                Debug.Log("[Steam] Achievements: Steam knows no achievements for app " + SteamPlatform.AppId +
                          " yet. Create them in Steamworks (see STEAM_ACHIEVEMENTS.md) and publish; progress is kept until then.");
            }
        }
        else
        {
            LogOnce("received:" + data.m_eResult, "[Steam] Achievements: loading the user's stats failed (" + data.m_eResult + ").");
        }
    }

    static int AchievementCountRaw()
    {
        try { return (int)Math.Min(int.MaxValue, SteamUserStats.GetNumAchievements()); }
        catch (Exception) { return 0; }
    }

    static void OnStatsStored(UserStatsStored_t data)
    {
        if (!IsOurGame(data.m_nGameID)) return;
        bool ok = data.m_eResult == EResult.k_EResultOK;
        if (!ok && data.m_eResult != EResult.k_EResultInvalidParam)
            LogOnce("stored:" + data.m_eResult, "[Steam] Achievements: StoreStats answered " + data.m_eResult + ".");
        Action<bool> handlers = StatsStored;
        if (handlers == null) return;
        foreach (Action<bool> handler in handlers.GetInvocationList())
        {
            try { handler(ok); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }

    static void OnAchievementStored(UserAchievementStored_t data)
    {
        if (!IsOurGame(data.m_nGameID) || !Debug.isDebugBuild) return;
        if (data.m_nMaxProgress == 0 && data.m_nCurProgress == 0)
            Debug.Log("[Steam] Achievement stored: " + data.m_rgchAchievementName);
    }
#endif
}
