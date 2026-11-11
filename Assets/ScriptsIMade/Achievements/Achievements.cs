using System;
using UnityEngine;

/// <summary>
/// The door to the Steam achievements for any system (the Online City included). Safe to call at any time and from any
/// thread (off the main thread a report waits for the next frame); it never throws.
/// - Report(eventId, amount): something happened. eventId is one of the AchievementEvents constants. A Sum counter adds
///   the amount (Report(AchievementEvents.Hires) = one more hire); a Max counter keeps the highest amount ever reported, so
///   absolute numbers can be reported again and again (Report(AchievementEvents.CityPlotsOwned, 12)).
/// - Unlock(apiName): unlock one achievement directly ("ACH_..."): prefer Report, which keeps progress.
/// - IsUnlocked / Count / Progress: the current player's state.
/// Progress is lifetime, per Steam user; AchievementSystem sends it to Steam when Steam runs (and only logs without it).
/// In Edit mode (tools) the calls work on the last profile's file.
/// </summary>
public static class Achievements
{
    /// <summary>Report an event: see AchievementEvents for the ids and whether they add up (Sum) or keep the best (Max).</summary>
    public static void Report(string eventId, long amount = 1)
    {
        try
        {
            if (string.IsNullOrEmpty(eventId)) return;
            if (AchievementSystem.IsOffMainThread)
            {
                AchievementSystem.EnqueueReport(eventId, amount);
                return;
            }
            AchievementSystem system = AchievementSystem.Ensure();
            if (system != null)
            {
                system.Report(eventId, amount);
                return;
            }
            if (!Application.isPlaying) EditReport(eventId, amount);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    /// <summary>Unlock one achievement by its API name ("ACH_FIRST_HIRE"). False when unknown or already unlocked.</summary>
    public static bool Unlock(string apiName)
    {
        try
        {
            if (string.IsNullOrEmpty(apiName) || AchievementSystem.IsOffMainThread) return false;
            AchievementSystem system = AchievementSystem.Ensure();
            if (system != null) return system.Unlock(apiName);
            if (Application.isPlaying) return false;
            AchievementStore store = AchievementSystem.ActiveStore;
            if (store == null || !AchievementTracker.Unlock(store, apiName, null)) return false;
            store.Save();
            Debug.Log("[Achievements] (Edit mode) " + apiName + " unlocked in profile '" + store.ProfileId + "'; Steam gets it the next time the game runs with Steam.");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            return false;
        }
    }

    /// <summary>The current player has this achievement.</summary>
    public static bool IsUnlocked(string apiName)
    {
        try
        {
            AchievementStore store = AchievementSystem.ActiveStore;
            return store != null && store.IsUnlocked(apiName);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>A counter's lifetime value (0 when unknown).</summary>
    public static long Count(string eventId)
    {
        try
        {
            AchievementStore store = AchievementSystem.ActiveStore;
            return store != null ? store.GetCount(eventId) : 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    /// <summary>0..1 toward an achievement (1 when unlocked, 0 when unknown).</summary>
    public static float Progress(string apiName)
    {
        try
        {
            return AchievementTracker.Progress(AchievementSystem.ActiveStore, AchievementDefinitions.Find(apiName));
        }
        catch (Exception)
        {
            return 0f;
        }
    }

    /// <summary>The player whose progress counts now: a Steam ID, or "local" without Steam.</summary>
    public static string ProfileId
    {
        get
        {
            AchievementStore store = AchievementSystem.ActiveStore;
            return store != null ? store.ProfileId : "";
        }
    }

    static void EditReport(string eventId, long amount)
    {
        AchievementStore store = AchievementSystem.ActiveStore;
        if (store == null) return;
        var unlocked = new System.Collections.Generic.List<AchievementDef>();
        if (!AchievementTracker.Report(store, eventId, amount, unlocked, null))
        {
            Debug.LogWarning("[Achievements] Unknown event id '" + eventId + "'.");
            return;
        }
        if (store.Dirty) store.Save();
        Debug.Log("[Achievements] (Edit mode) " + eventId + " " + amount + " -> " + store.GetCount(eventId) + " in profile '" + store.ProfileId + "'" +
                  (unlocked.Count > 0 ? "; unlocked " + string.Join(", ", unlocked.ConvertAll(d => d.ApiName)) : "") + ".");
    }
}

/// <summary>Achievement names and descriptions in the current language (the "Achievements" String Table).</summary>
public static class AchievementText
{
    public const string Table = AchievementDefinitions.Table;

    public static string Name(AchievementDef def)
    {
        return def == null ? "" : L10n.GetOr(Table, def.NameKey, def.ApiName);
    }

    public static string Description(AchievementDef def)
    {
        return def == null ? "" : L10n.GetOr(Table, def.DescKey, "");
    }
}
