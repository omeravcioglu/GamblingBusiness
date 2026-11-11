using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A system that adds recurring money and meter changes to every office day: the HQ buildings' upkeep, the manager's
/// company policies (food, benefits), an investor's share of revenue... Register it with DailyEconomyHooks while it is
/// active and unregister it when it goes away.
/// CompanyManager asks every hook once per day, after staff, office items and buildings are counted and before skill
/// growth: the money becomes DayBreakdown.recurring (part of the operating result, so investor trust and growth skills
/// react to it); the meters are applied with their own daily cap.
/// </summary>
public interface IDailyEconomyHook
{
    /// <summary>Short stable name for logs ("upkeep", "policies").</summary>
    string HookName { get; }

    /// <summary>
    /// Today's money from this system (negative = a cost). <paramref name="resultBeforeHooks"/> is the day's operating
    /// result before any hook, for shares of revenue.
    /// </summary>
    double DailyMoney(CompanyManager company, double resultBeforeHooks);

    /// <summary>Today's happiness and trust nudges from this system, in meter points (add to the running totals).</summary>
    void DailyMeters(CompanyManager company, ref float happiness, ref float trust);
}

/// <summary>The registry of IDailyEconomyHook systems CompanyManager asks at the end of every day.</summary>
public static class DailyEconomyHooks
{
    /// <summary>All hooks together never move happiness more than this a day (like buildings' cap).</summary>
    public const float MaxHappinessPerDay = 5f;
    /// <summary>All hooks together never move investor trust more than this a day.</summary>
    public const float MaxTrustPerDay = 5f;

    static readonly List<IDailyEconomyHook> hooks = new List<IDailyEconomyHook>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        // Entering Play mode without a domain reload keeps statics
        hooks.Clear();
    }

    public static void Register(IDailyEconomyHook hook)
    {
        if (hook != null && !hooks.Contains(hook)) hooks.Add(hook);
    }

    public static void Unregister(IDailyEconomyHook hook)
    {
        hooks.Remove(hook);
    }

    /// <summary>Hooks registered now.</summary>
    public static int Count => hooks.Count;

    /// <summary>The day's money from every hook. A hook that throws or returns NaN counts as 0 (and is logged).</summary>
    public static double CollectMoney(CompanyManager company, double resultBeforeHooks)
    {
        double total = 0.0;
        foreach (IDailyEconomyHook hook in hooks.ToArray())
        {
            try
            {
                double money = hook.DailyMoney(company, resultBeforeHooks);
                if (!double.IsNaN(money) && !double.IsInfinity(money)) total += money;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
        return total;
    }

    /// <summary>The day's happiness and trust from every hook, each capped at its daily maximum.</summary>
    public static void CollectMeters(CompanyManager company, out float happiness, out float trust)
    {
        happiness = 0f;
        trust = 0f;
        foreach (IDailyEconomyHook hook in hooks.ToArray())
        {
            try
            {
                hook.DailyMeters(company, ref happiness, ref trust);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
        happiness = float.IsNaN(happiness) ? 0f : Mathf.Clamp(happiness, -MaxHappinessPerDay, MaxHappinessPerDay);
        trust = float.IsNaN(trust) ? 0f : Mathf.Clamp(trust, -MaxTrustPerDay, MaxTrustPerDay);
    }
}
