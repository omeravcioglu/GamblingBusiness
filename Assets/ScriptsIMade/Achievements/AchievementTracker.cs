using System;
using System.Collections.Generic;

/// <summary>A progress pop-up owed to Steam: an achievement's counter crossed 25%, 50% or 75% of its target.</summary>
public struct AchievementMilestone
{
    public AchievementDef Def;
    public long Progress;
    public int Percent;
}

/// <summary>
/// The rules, without Unity or Steam: a report moves a counter (Sum adds, Max keeps the highest), every achievement on that
/// counter whose target is reached unlocks (once: an unlocked one is never unlocked again), and long counters owe a
/// progress pop-up at 25/50/75% of a target. AchievementSystem applies the results (Steam, saving, logs); the editor tools
/// use the same rules on a profile file.
/// </summary>
public static class AchievementTracker
{
    static readonly int[] Percents = { 25, 50, 75 };

    /// <summary>Unix seconds now (UTC).</summary>
    public static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    /// <summary>
    /// Apply a report. False when the counter is unknown (nothing changes). Newly unlocked achievements and owed progress
    /// pop-ups are added to the lists (either may be null).
    /// </summary>
    public static bool Report(AchievementStore store, string counterId, long amount,
                              List<AchievementDef> unlocked, List<AchievementMilestone> milestones)
    {
        AchievementCounter counter = AchievementDefinitions.FindCounter(counterId);
        if (counter == null || store == null) return false;
        long before = store.GetCount(counter.Id);
        long after = counter.Mode == AchievementCounterMode.Sum ? SafeAdd(before, Math.Max(0L, amount)) : Math.Max(before, amount);
        Apply(store, counter, before, after, unlocked, milestones);
        return true;
    }

    /// <summary>
    /// Raise a counter to at least <paramref name="value"/>, whatever its mode: a lifetime total is never below the current
    /// company's own number (e.g. its hires from before achievements existed). Never lowers anything.
    /// </summary>
    public static bool RaiseTo(AchievementStore store, string counterId, long value,
                               List<AchievementDef> unlocked, List<AchievementMilestone> milestones)
    {
        AchievementCounter counter = AchievementDefinitions.FindCounter(counterId);
        if (counter == null || store == null) return false;
        long before = store.GetCount(counter.Id);
        Apply(store, counter, before, Math.Max(before, value), unlocked, milestones);
        return true;
    }

    /// <summary>
    /// Re-check every achievement against the stored counters (after a merge, or counters raised from Steam's stats). Only
    /// unlocks; never owes pop-ups.
    /// </summary>
    public static void Recheck(AchievementStore store, List<AchievementDef> unlocked)
    {
        if (store == null) return;
        foreach (AchievementDef def in AchievementDefinitions.All)
        {
            if (store.IsUnlocked(def.ApiName)) continue;
            if (store.GetCount(def.Counter) >= def.Target && store.MarkUnlocked(def.ApiName, Now()))
                unlocked?.Add(def);
        }
    }

    /// <summary>Unlock one achievement directly. False when it is unknown or already unlocked.</summary>
    public static bool Unlock(AchievementStore store, string apiName, List<AchievementDef> unlocked)
    {
        AchievementDef def = AchievementDefinitions.Find(apiName);
        if (def == null || store == null) return false;
        if (!store.MarkUnlocked(def.ApiName, Now())) return false;
        unlocked?.Add(def);
        return true;
    }

    /// <summary>0..1 toward an achievement's target (1 when unlocked).</summary>
    public static float Progress(AchievementStore store, AchievementDef def)
    {
        if (store == null || def == null) return 0f;
        if (store.IsUnlocked(def.ApiName)) return 1f;
        double share = (double)store.GetCount(def.Counter) / Math.Max(1L, def.Target);
        return (float)Math.Max(0.0, Math.Min(1.0, share));
    }

    /// <summary>The key a shown pop-up is remembered by: "ACH_X@50".</summary>
    public static string MilestoneKey(AchievementDef def, int percent) => def.ApiName + "@" + percent;

    static void Apply(AchievementStore store, AchievementCounter counter, long before, long after,
                      List<AchievementDef> unlocked, List<AchievementMilestone> milestones)
    {
        if (after < 0) after = 0;
        if (after != before) store.SetCount(counter.Id, after);
        foreach (AchievementDef def in AchievementDefinitions.ForCounter(counter.Id))
        {
            if (store.IsUnlocked(def.ApiName)) continue;
            long value = Math.Max(after, store.GetCount(counter.Id));
            if (value >= def.Target)
            {
                if (store.MarkUnlocked(def.ApiName, Now())) unlocked?.Add(def);
                continue;
            }
            if (!def.ShowProgress || def.Target < 10 || after <= before) continue;
            // Only the highest mark this report crossed gets a pop-up; lower ones crossed with it count as shown
            bool owed = false;
            for (int i = Percents.Length - 1; i >= 0; i--)
            {
                int percent = Percents[i];
                if (def.Target < 20 && percent != 50) continue;   // short counters: only the halfway pop-up
                long mark = (def.Target * percent + 99) / 100;
                if (value < mark) continue;
                string key = MilestoneKey(def, percent);
                if (store.HasMilestone(key)) continue;
                store.AddMilestone(key);
                if (owed || before >= mark) continue;
                owed = true;
                milestones?.Add(new AchievementMilestone { Def = def, Progress = value, Percent = percent });
            }
        }
    }

    static long SafeAdd(long a, long b)
    {
        long sum = a + b;
        return sum < a ? long.MaxValue : sum;
    }
}
