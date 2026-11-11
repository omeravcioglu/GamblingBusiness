using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The Steam achievements' engine (created at boot, lives for the whole session):
/// - Progress is lifetime and per player: AchievementStore keeps one file per Steam ID in persistentDataPath/Achievements
///   (profile "local" while Steam isn't running), so it survives offline play and people sharing a PC keep their own.
///   Progress made without Steam goes to this PC's Steam user the first time Steam runs (never to another user's account:
///   the last Steam ID seen here must match, or be the first).
/// - Reports (Achievements.Report, fed by AchievementHooks from the game's events) move counters; AchievementTracker unlocks
///   what reached its target, exactly once.
/// - Steam is a queue, re-applied until it sticks: every unlock Steam doesn't confirm yet and every stat that differs from
///   what Steam was last given is sent (SetAchievement / SetStat) whenever Steam is ready, then stored (StoreStats right after
///   an unlock, stats alone at most once a minute and on quit/pause/scene change). Steam ignores duplicates. At the start of
///   each Steam session its own state is read first: unlocks made on another PC come in, and anything Steam lost is sent
///   again. An achievement or stat Steam doesn't know yet (not created in Steamworks) is logged once and kept queued.
/// - Long counters show Steam's "x / y" pop-up at 25/50/75% (IndicateAchievementProgress).
/// - Nothing here throws into gameplay; without Steam (the Editor, a build without Steamworks) it only logs unlocks.
/// - Off in batch mode, so automated runs don't earn anything.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("")]
public sealed class AchievementSystem : MonoBehaviour
{
    /// <summary>PlayerPrefs: the last Steam ID that played on this PC (progress made without Steam goes to it).</summary>
    public const string LastSteamIdKey = "Achievements.LastSteamId";
    /// <summary>PlayerPrefs: the last profile used (the editor tools open it).</summary>
    public const string LastProfileKey = "Achievements.LastProfile";

    const float PendingSyncSeconds = 1f;      // Steam work waiting: check this often
    const float IdleSyncSeconds = 15f;
    const float UnlockStoreGap = 1.5f;        // StoreStats after an unlock, at most this often
    const float StatStoreSeconds = 60f;       // stats alone are stored at most this often
    const float SaveDelay = 2f;
    const float UnknownRetrySeconds = 300f;   // names Steam didn't know are tried again after this

    public static AchievementSystem Instance { get; private set; }

    /// <summary>Also log every report (debugging).</summary>
    public static bool VerboseLog;

    static int mainThreadId = -1;
    static bool quitting;
    static bool loggedBatchMode;
    static AchievementStore editStore;
    static readonly ConcurrentQueue<KeyValuePair<string, long>> offThreadReports = new ConcurrentQueue<KeyValuePair<string, long>>();

    AchievementStore store;
    AchievementHooks hooks;
    bool steamPulled;
    float nextSync;
    float lastStoreAt = -999f;
    bool unlockStoreOwed;
    bool statStoreOwed;
    float saveAt = -1f;
    float unknownRetryAt;
    bool loggedNoSteam;
    bool loggedTickError;
    readonly HashSet<string> unknownOnSteam = new HashSet<string>(StringComparer.Ordinal);
    readonly HashSet<string> refusedStats = new HashSet<string>(StringComparer.Ordinal);
    bool storeRejected;
    readonly HashSet<string> loggedUnknownSteam = new HashSet<string>(StringComparer.Ordinal);
    readonly HashSet<string> loggedUnknownEvents = new HashSet<string>(StringComparer.Ordinal);
    readonly List<AchievementMilestone> owedPopups = new List<AchievementMilestone>();
    readonly List<AchievementDef> unlockedScratch = new List<AchievementDef>();
    readonly List<AchievementMilestone> milestoneScratch = new List<AchievementMilestone>();

    /// <summary>The active profile's progress (null before Awake).</summary>
    public AchievementStore Store => store;
    /// <summary>The active profile: a Steam ID, or "local".</summary>
    public string ProfileId => store != null ? store.ProfileId : "";

    // ------------------------------------------------------------------
    // Creation
    // ------------------------------------------------------------------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        // Entering Play mode without a domain reload keeps statics
        Instance = null;
        mainThreadId = -1;
        quitting = false;
        loggedBatchMode = false;
        editStore = null;
        while (offThreadReports.TryDequeue(out _)) { }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        mainThreadId = Thread.CurrentThread.ManagedThreadId;
        Ensure();
    }

    /// <summary>The running system, created if missing. Null outside Play mode, in batch mode and while the game quits.</summary>
    public static AchievementSystem Ensure()
    {
        if (Instance != null) return Instance;
        if (!Application.isPlaying || quitting) return null;
        if (Application.isBatchMode)
        {
            if (!loggedBatchMode) Debug.Log("[Achievements] Off in batch mode: automated runs don't earn achievements.");
            loggedBatchMode = true;
            return null;
        }
        // Demo: no achievements in the demo (they belong to the full game's app)
        if (DemoMode.Active)
        {
            if (!loggedBatchMode) Debug.Log("[Achievements] Off in the demo.");
            loggedBatchMode = true;
            return null;
        }
        if (PlaytestMode.Active)
        {
            if (!loggedBatchMode) Debug.Log("[Achievements] Off in playtest mode: a head start doesn't earn achievements.");
            loggedBatchMode = true;
            return null;
        }
        var go = new GameObject("Achievements");
        DontDestroyOnLoad(go);
        return go.AddComponent<AchievementSystem>();
    }

    /// <summary>True when called from another thread than Unity's main one (reports are then queued for the next frame).</summary>
    internal static bool IsOffMainThread => mainThreadId >= 0 && Thread.CurrentThread.ManagedThreadId != mainThreadId;

    internal static void EnqueueReport(string eventId, long amount)
    {
        offThreadReports.Enqueue(new KeyValuePair<string, long>(eventId, amount));
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
        if (mainThreadId < 0) mainThreadId = Thread.CurrentThread.ManagedThreadId;
        Application.quitting -= OnQuitting;
        Application.quitting += OnQuitting;
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
        SteamAchievements.StatsReceived -= OnSteamStatsReceived;
        SteamAchievements.StatsReceived += OnSteamStatsReceived;
        SteamAchievements.StatsStored -= OnSteamStatsStored;
        SteamAchievements.StatsStored += OnSteamStatsStored;

        // The profile is picked at the first Update (or report): SteamPlatform starts Steam at the same boot stage, in no
        // particular order with this
        hooks = new AchievementHooks(this);
        hooks.Attach();
    }

    void OnDestroy()
    {
        if (Instance != this) return;
        Flush();
        hooks?.Detach();
        Application.quitting -= OnQuitting;
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        SteamAchievements.StatsReceived -= OnSteamStatsReceived;
        SteamAchievements.StatsStored -= OnSteamStatsStored;
        Instance = null;
    }

    static void OnQuitting()
    {
        quitting = true;
        if (Instance != null) Instance.Flush();
    }

    void OnApplicationPause(bool paused)
    {
        if (paused) Flush();
    }

    void OnApplicationQuit()
    {
        Flush();
    }

    void OnActiveSceneChanged(Scene from, Scene to)
    {
        Flush();
    }

    void Update()
    {
        try
        {
            SteamAchievements.Tick();
            CheckProfile();
            while (offThreadReports.TryDequeue(out KeyValuePair<string, long> report))
                Report(report.Key, report.Value);
            hooks.Tick();
            SyncSteam();
            if (store != null && store.Dirty && saveAt < 0f) ScheduleSave(SaveDelay);
            if (saveAt >= 0f && Time.unscaledTime >= saveAt)
            {
                saveAt = -1f;
                if (store != null && store.Dirty) store.Save();
            }
        }
        catch (Exception e)
        {
            if (!loggedTickError) Debug.LogException(e, this);
            loggedTickError = true;
        }
    }

    // ------------------------------------------------------------------
    // Reports
    // ------------------------------------------------------------------

    /// <summary>A counter moves by a report (see Achievements.Report). Unknown ids are logged once.</summary>
    public void Report(string eventId, long amount)
    {
        Apply(eventId, amount, false, true);
    }

    /// <summary>A counter goes up to at least <paramref name="value"/> (never down), without progress pop-ups.</summary>
    public void RaiseTo(string eventId, long value)
    {
        Apply(eventId, value, true, false);
    }

    void Apply(string eventId, long amount, bool raise, bool popups)
    {
        if (store == null) CheckProfile();
        if (store == null) return;
        unlockedScratch.Clear();
        milestoneScratch.Clear();
        bool known = raise
            ? AchievementTracker.RaiseTo(store, eventId, amount, unlockedScratch, popups ? milestoneScratch : null)
            : AchievementTracker.Report(store, eventId, amount, unlockedScratch, popups ? milestoneScratch : null);
        if (!known)
        {
            if (loggedUnknownEvents.Add(eventId ?? ""))
                Debug.LogWarning("[Achievements] Unknown event id '" + eventId + "' (the ids are in AchievementEvents).");
            return;
        }
        if (VerboseLog) Debug.Log("[Achievements] " + (raise ? "raise " : "report ") + eventId + " " + amount + " -> " + store.GetCount(eventId));
        AfterChange();
    }

    /// <summary>Unlock one achievement directly (debug tools, special cases). False when unknown or already unlocked.</summary>
    public bool Unlock(string apiName)
    {
        if (store == null) CheckProfile();
        if (store == null) return false;
        unlockedScratch.Clear();
        milestoneScratch.Clear();
        if (!AchievementTracker.Unlock(store, apiName, unlockedScratch))
        {
            if (AchievementDefinitions.Find(apiName) == null) Debug.LogWarning("[Achievements] Unknown achievement '" + apiName + "'.");
            return false;
        }
        AfterChange();
        return true;
    }

    void AfterChange()
    {
        foreach (AchievementDef def in unlockedScratch) Announce(def);
        owedPopups.AddRange(milestoneScratch);
        if (unlockedScratch.Count > 0 || milestoneScratch.Count > 0 || StatsWaiting()) nextSync = 0f;
        if (store.Dirty) ScheduleSave(unlockedScratch.Count > 0 ? 0f : SaveDelay);
        unlockedScratch.Clear();
        milestoneScratch.Clear();
    }

    void Announce(AchievementDef def)
    {
        Debug.Log("[Achievements] Unlocked " + def.ApiName + ": " + AchievementText.Name(def) +
                  (SteamAchievements.IsReady ? "" : " (Steam isn't running: kept for later)"));
    }

    void ScheduleSave(float delay)
    {
        float at = Time.unscaledTime + Mathf.Max(0f, delay);
        if (saveAt < 0f || at < saveAt) saveAt = at;
    }

    /// <summary>Write the profile now and send owed Steam changes (quit, pause, scene change).</summary>
    public void Flush()
    {
        try
        {
            saveAt = -1f;
            if (store != null && store.Dirty) store.Save();
            if ((unlockStoreOwed || statStoreOwed) && SteamAchievements.IsReady && SteamAchievements.Store())
            {
                unlockStoreOwed = false;
                statStoreOwed = false;
                lastStoreAt = Time.unscaledTime;
            }
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    // ------------------------------------------------------------------
    // Profiles
    // ------------------------------------------------------------------

    static string DesiredProfile()
    {
        ulong id = SteamPlatform.IsInitialized ? SteamPlatform.SteamId : 0UL;
        return id != 0UL ? id.ToString(CultureInfo.InvariantCulture) : AchievementStore.LocalProfile;
    }

    void CheckProfile()
    {
        if (quitting && store != null) return;
        string wanted = DesiredProfile();
        if (store == null || wanted != store.ProfileId) SwitchProfile(wanted);
    }

    void SwitchProfile(string profileId)
    {
        AchievementStore previous = store;
        if (previous != null && previous.Dirty) previous.Save();

        AchievementStore next = AchievementStore.Load(profileId);
        bool isSteam = profileId != AchievementStore.LocalProfile;
        if (isSteam) MergeLocalInto(next, previous);

        store = next;
        steamPulled = false;
        unlockStoreOwed = false;
        statStoreOwed = false;
        owedPopups.Clear();
        unknownOnSteam.Clear();
        refusedStats.Clear();
        storeRejected = false;
        nextSync = 0f;

        unlockedScratch.Clear();
        AchievementTracker.Recheck(store, unlockedScratch);   // counters merged in may have reached targets
        foreach (AchievementDef def in unlockedScratch) Announce(def);
        unlockedScratch.Clear();
        if (store.Dirty) store.Save();

        try
        {
            PlayerPrefs.SetString(LastProfileKey, profileId);
            if (isSteam) PlayerPrefs.SetString(LastSteamIdKey, profileId);
            PlayerPrefs.Save();
        }
        catch (Exception) { }

        Debug.Log("[Achievements] Profile " + (isSteam ? "Steam user " + profileId : "'local' (Steam isn't running)") + ": " +
                  store.Unlocks.Count + " of " + AchievementDefinitions.All.Count + " unlocked" +
                  (store.PendingSteamUnlocks > 0 ? ", " + store.PendingSteamUnlocks + " waiting for Steam" : "") + ".");
        hooks?.OnProfileChanged();
    }

    /// <summary>Progress made without Steam goes to this PC's Steam user, the first time Steam runs (never to someone else).</summary>
    static void MergeLocalInto(AchievementStore steamStore, AchievementStore current)
    {
        string last = "";
        try { last = PlayerPrefs.GetString(LastSteamIdKey, ""); }
        catch (Exception) { }
        if (last.Length > 0 && last != steamStore.ProfileId) return;

        AchievementStore local = current != null && current.ProfileId == AchievementStore.LocalProfile
            ? current
            : AchievementStore.Load(AchievementStore.LocalProfile);
        if (local.IsEmpty) return;
        int changes = steamStore.MergeFrom(local);
        local.Clear();
        AchievementStore.DeleteFile(AchievementStore.LocalProfile);
        steamStore.Save();
        Debug.Log("[Achievements] Progress made without Steam (" + changes + " change(s)) was added to Steam user " + steamStore.ProfileId + ".");
    }

    // ------------------------------------------------------------------
    // Steam
    // ------------------------------------------------------------------

    void OnSteamStatsReceived()
    {
        steamPulled = false;   // read Steam's state again at the next sync
        nextSync = 0f;
    }

    void OnSteamStatsStored(bool ok)
    {
        if (ok) return;
        // Steam rejected a stat (out of date: another PC stored a higher value) and sent its values back: read them again
        storeRejected = true;
        steamPulled = false;
        nextSync = 0f;
    }

    bool StatsWaiting()
    {
        if (store == null) return false;
        foreach (AchievementCounter counter in AchievementDefinitions.Counters)
        {
            if (counter.SteamStat == null || unknownOnSteam.Contains(counter.SteamStat) || refusedStats.Contains(counter.SteamStat)) continue;
            if (store.GetSent(counter.Id) != store.GetCount(counter.Id)) return true;
        }
        return false;
    }

    bool UnlocksWaiting()
    {
        if (store == null) return false;
        foreach (AchievementUnlockRecord u in store.Unlocks)
            if (!u.steam && !unknownOnSteam.Contains(u.api)) return true;
        return false;
    }

    void SyncSteam()
    {
        if (store == null || Time.unscaledTime < nextSync) return;
        bool waiting = unlockStoreOwed || statStoreOwed || owedPopups.Count > 0 || UnlocksWaiting() || StatsWaiting();
        nextSync = Time.unscaledTime + (waiting ? PendingSyncSeconds : IdleSyncSeconds);

        if (!SteamAchievements.IsReady || store.ProfileId == AchievementStore.LocalProfile)
        {
            owedPopups.Clear();   // pop-ups are for now or never
            if (waiting && !loggedNoSteam && !SteamPlatform.IsInitialized && Time.realtimeSinceStartup > 20f)
            {
                loggedNoSteam = true;
                Debug.Log("[Achievements] " + SteamAchievements.Describe() + " Unlocks and progress are kept for profile '" + ProfileId +
                          "' and sent once Steam runs.");
            }
            return;
        }

        if (!steamPulled)
        {
            steamPulled = true;
            PullFromSteam();
        }

        if (Time.unscaledTime >= unknownRetryAt)
        {
            unknownOnSteam.Clear();
            unknownRetryAt = Time.unscaledTime + UnknownRetrySeconds;
        }

        // Unlocks Steam doesn't have yet
        foreach (AchievementUnlockRecord u in store.Unlocks)
        {
            if (u.steam || unknownOnSteam.Contains(u.api)) continue;
            SteamAchievementResult result = SteamAchievements.Unlock(u.api);
            if (result == SteamAchievementResult.Ok)
            {
                store.SetSteamHas(u.api, true);
                unlockStoreOwed = true;
            }
            else if (result == SteamAchievementResult.Unknown)
            {
                MarkUnknown(u.api, "achievement");
            }
            else if (result == SteamAchievementResult.NotReady)
            {
                break;
            }
        }

        // Stats that differ from what Steam was last given
        foreach (AchievementCounter counter in AchievementDefinitions.Counters)
        {
            if (counter.SteamStat == null || unknownOnSteam.Contains(counter.SteamStat) || refusedStats.Contains(counter.SteamStat)) continue;
            long value = store.GetCount(counter.Id);
            if (store.GetSent(counter.Id) == value) continue;
            SteamAchievementResult result = SteamAchievements.SetStat(counter.SteamStat, (int)Math.Min(value, int.MaxValue));
            if (result == SteamAchievementResult.Ok)
            {
                store.SetSent(counter.Id, value);
                statStoreOwed = true;
            }
            else if (result == SteamAchievementResult.Unknown)
            {
                MarkUnknown(counter.SteamStat, "stat");
            }
            else if (result == SteamAchievementResult.NotReady)
            {
                break;
            }
        }

        // Progress pop-ups for achievements still locked
        foreach (AchievementMilestone popup in owedPopups)
        {
            if (popup.Def == null || store.IsUnlocked(popup.Def.ApiName) || unknownOnSteam.Contains(popup.Def.ApiName)) continue;
            SteamAchievements.IndicateProgress(popup.Def.ApiName, popup.Progress, popup.Def.Target);
        }
        owedPopups.Clear();

        float now = Time.unscaledTime;
        if ((unlockStoreOwed && now - lastStoreAt >= UnlockStoreGap) || (statStoreOwed && now - lastStoreAt >= StatStoreSeconds))
        {
            lastStoreAt = now;
            if (SteamAchievements.Store())
            {
                unlockStoreOwed = false;
                statStoreOwed = false;
            }
        }
        if (store.Dirty) ScheduleSave(SaveDelay);
    }

    void MarkUnknown(string name, string what)
    {
        unknownOnSteam.Add(name);
        if (loggedUnknownSteam.Add(name))
            Debug.LogWarning("[Achievements] Steam doesn't know the " + what + " " + name + " yet (create it in Steamworks and publish; " +
                             "see STEAM_ACHIEVEMENTS.md). It stays queued and is tried again later.");
    }

    /// <summary>
    /// Read Steam's own state (once per Steam session and after Steam sent new values): unlocks made elsewhere come in, an
    /// unlock or stat value Steam lost is sent again, and a higher stat from another PC raises the counter.
    /// </summary>
    void PullFromSteam()
    {
        int fromSteam = 0, resend = 0;
        foreach (AchievementDef def in AchievementDefinitions.All)
        {
            if (!SteamAchievements.TryGetUnlocked(def.ApiName, out bool achieved)) continue;   // Steam doesn't know it
            AchievementUnlockRecord local = store.GetUnlock(def.ApiName);
            if (achieved)
            {
                if (local == null)
                {
                    store.MarkUnlocked(def.ApiName, AchievementTracker.Now(), true);
                    fromSteam++;
                }
                else
                {
                    store.SetSteamHas(def.ApiName, true);
                }
            }
            else if (local != null && local.steam)
            {
                store.SetSteamHas(def.ApiName, false);
                resend++;
            }
        }

        bool afterRejection = storeRejected;
        storeRejected = false;
        foreach (AchievementCounter counter in AchievementDefinitions.Counters)
        {
            if (counter.SteamStat == null || !SteamAchievements.TryGetStat(counter.SteamStat, out int steamValue)) continue;
            long local = store.GetCount(counter.Id);
            if (steamValue > local) store.SetCount(counter.Id, steamValue);
            else if (afterRejection && steamValue < local && refusedStats.Add(counter.SteamStat))
                // Steam refused a higher value and kept a lower one: not a race with another PC, so it won't take it (the
                // stat's max value in Steamworks is too low?). Not sent again this session.
                Debug.LogWarning("[Achievements] Steam refused " + counter.SteamStat + " = " + local + " and keeps " + steamValue +
                                 ". Check the stat in Steamworks (INT, max value high enough, see STEAM_ACHIEVEMENTS.md).");
            store.SetSent(counter.Id, Math.Max(0, steamValue));   // what Steam holds; the sync sends more when there is more
        }

        unlockedScratch.Clear();
        AchievementTracker.Recheck(store, unlockedScratch);
        foreach (AchievementDef def in unlockedScratch) Announce(def);
        unlockedScratch.Clear();

        if (fromSteam > 0 || resend > 0)
            Debug.Log("[Achievements] Steam sync: " + fromSteam + " unlock(s) came from Steam" + (resend > 0 ? ", " + resend + " to send again" : "") + ".");
        if (store.Dirty) ScheduleSave(0f);
    }

    // ------------------------------------------------------------------
    // Debug (Tools > Game > Achievements)
    // ------------------------------------------------------------------

    /// <summary>Forget all progress of the active profile, and on Steam too when it runs (ResetAllStats with achievements).</summary>
    public string DebugResetAll()
    {
        bool steam = SteamAchievements.IsReady && store != null && store.ProfileId != AchievementStore.LocalProfile;
        bool steamOk = steam && SteamAchievements.ResetAll(true);
        store?.Clear();
        store?.Save();
        owedPopups.Clear();
        unknownOnSteam.Clear();
        refusedStats.Clear();
        unlockStoreOwed = statStoreOwed = false;
        steamPulled = false;
        nextSync = 0f;
        hooks?.OnProfileChanged();
        return "Profile '" + ProfileId + "' reset" + (steam ? (steamOk ? ", Steam stats and achievements reset." : ", but Steam's ResetAllStats failed.") :
                                                              " (Steam isn't running: only the local file).");
    }

    /// <summary>Lock one achievement again (local, and on Steam when it runs).</summary>
    public bool DebugLock(string apiName)
    {
        // Its counter stays (the next report unlocks it again), and so do shown pop-ups (they only stop repeats)
        if (store == null || !store.RemoveUnlock(apiName)) return false;
        if (SteamAchievements.IsReady && SteamAchievements.Clear(apiName) == SteamAchievementResult.Ok)
            unlockStoreOwed = true;
        ScheduleSave(0f);
        nextSync = 0f;
        return true;
    }

    /// <summary>One block for the List tool: the profile, Steam's state and every achievement with its progress.</summary>
    public static string DescribeAll(AchievementStore s)
    {
        var text = new System.Text.StringBuilder();
        text.Append("[Achievements] Profile '").Append(s != null ? s.ProfileId : "-").Append("'");
        if (s != null)
            text.Append(": ").Append(s.Unlocks.Count).Append(" of ").Append(AchievementDefinitions.All.Count).Append(" unlocked, ")
                .Append(s.PendingSteamUnlocks).Append(" waiting for Steam");
        text.Append('\n').Append("  ").Append(SteamAchievements.Describe()).Append('\n');
        foreach (AchievementDef def in AchievementDefinitions.All)
        {
            AchievementUnlockRecord unlock = s?.GetUnlock(def.ApiName);
            long count = s != null ? s.GetCount(def.Counter) : 0;
            text.Append("  ").Append(unlock != null ? (unlock.steam ? "[x] " : "[~] ") : "[ ] ")
                .Append(def.ApiName.PadRight(26)).Append(' ')
                .Append(unlock != null ? "unlocked " + DateTimeOffset.FromUnixTimeSeconds(unlock.time).ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                                       : Math.Min(count, def.Target).ToString("N0", CultureInfo.InvariantCulture) + " / " + def.Target.ToString("N0", CultureInfo.InvariantCulture))
                .Append(def.Hidden ? "  (hidden)" : "")
                .Append("  ").Append(AchievementText.Name(def)).Append('\n');
        }
        text.Append("  [x] on Steam  [~] unlocked here, waiting for Steam  [ ] locked");
        return text.ToString();
    }

    /// <summary>The store the facade uses: the running system's, or in Edit mode the last profile's file (for the tools).</summary>
    public static AchievementStore ActiveStore
    {
        get
        {
            if (Instance != null) return Instance.store;
            if (Application.isPlaying) return null;
            string profile = AchievementStore.LocalProfile;
            try { profile = PlayerPrefs.GetString(LastProfileKey, AchievementStore.LocalProfile); }
            catch (Exception) { }
            if (editStore == null || editStore.ProfileId != profile) editStore = AchievementStore.Load(profile);
            return editStore;
        }
    }

    /// <summary>Forget the Edit mode copy (after a tool changed the file).</summary>
    public static void ReloadEditStore()
    {
        editStore = null;
    }
}
