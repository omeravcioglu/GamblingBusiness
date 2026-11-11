using System;
using UnityEngine;

/// <summary>
/// How many employees the company's offices can hold, and how many it has. Hiring needs a free place: the owner decided
/// (2026-09-29) that office capacity is a HARD limit on hiring ("Employees 10 / 10" = no more hires until offices are
/// built or upgraded).
/// - The building system provides the number (SetProvider); until one is registered there is no limit (Unlimited).
/// - Readers: the hiring desk (GameFlowManager), the HUD's "Employees 17 / 25" counter, the tutorial, the manager.
/// - Whoever changes capacity or headcount calls NotifyChanged so the counter and the desk update at once.
/// </summary>
public static class OfficeCapacity
{
    /// <summary>What Capacity reports while no building system provides one.</summary>
    public const int Unlimited = int.MaxValue;

    static Func<int> provider;
    // Extra places on top of the buildings (e.g. office space the manager rented), by id
    static readonly System.Collections.Generic.Dictionary<string, Func<int>> extras =
        new System.Collections.Generic.Dictionary<string, Func<int>>();

    /// <summary>Raised after capacity or headcount changed (NotifyChanged).</summary>
    public static event Action Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        // Entering Play mode without a domain reload keeps statics
        provider = null;
        extras.Clear();
        Changed = null;
    }

    /// <summary>
    /// Add places that don't come from the buildings (e.g. rented office space), under a stable id; registering the same
    /// id again replaces it. They only count while a building provider is registered (no provider = no limit anyway).
    /// </summary>
    public static void RegisterExtra(string id, Func<int> places)
    {
        if (string.IsNullOrEmpty(id)) return;
        if (places == null) extras.Remove(id);
        else extras[id] = places;
        NotifyChanged();
    }

    public static void UnregisterExtra(string id)
    {
        if (!string.IsNullOrEmpty(id) && extras.Remove(id)) NotifyChanged();
    }

    /// <summary>Places the buildings provide, without extras (Unlimited when no provider).</summary>
    public static int BuildingCapacity
    {
        get
        {
            if (provider == null) return Unlimited;
            try
            {
                return Math.Max(0, provider());
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return Unlimited;   // a broken provider must never lock hiring
            }
        }
    }

    /// <summary>Places from extras (rented space...), 0 when none.</summary>
    public static int ExtraCapacity
    {
        get
        {
            long total = 0;
            foreach (Func<int> extra in extras.Values)
            {
                try { total += Math.Max(0, extra()); }
                catch (Exception e) { Debug.LogException(e); }
            }
            return (int)Math.Min(int.MaxValue / 2, total);
        }
    }

    /// <summary>Employees on the payroll now.</summary>
    public static int Employees
    {
        get
        {
            CompanyManager company = CompanyManager.Instance;
            return company != null && company.employees != null ? company.employees.Count : 0;
        }
    }

    /// <summary>Employees the company can hold: buildings + extras (Unlimited when no building system provides a number).</summary>
    public static int Capacity
    {
        get
        {
            int buildings = BuildingCapacity;
            if (buildings == Unlimited) return Unlimited;
            return (int)Math.Min(int.MaxValue - 1, (long)buildings + ExtraCapacity);
        }
    }

    /// <summary>True when a building system provides the capacity (false = no limit yet).</summary>
    public static bool IsLimited => provider != null;

    /// <summary>Free places (0 when full or over).</summary>
    public static int Free
    {
        get
        {
            int capacity = Capacity;
            return capacity == Unlimited ? Unlimited : Math.Max(0, capacity - Employees);
        }
    }

    /// <summary>No free place: hiring is blocked until capacity grows or someone leaves.</summary>
    public static bool IsFull => Free <= 0;

    /// <summary>The building system registers where the number comes from (null = no limit).</summary>
    public static void SetProvider(Func<int> capacityProvider)
    {
        provider = capacityProvider;
        NotifyChanged();
    }

    /// <summary>Tell readers that capacity or headcount changed.</summary>
    public static void NotifyChanged()
    {
        Action handlers = Changed;
        if (handlers == null) return;
        foreach (Action handler in handlers.GetInvocationList())
        {
            try { ((Action)handler)(); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}
