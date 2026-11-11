using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// HAND-OFF to the building system (BuildMode/**, owned by another session): what manager decisions offer the building side.
/// Build money only comes from gambling (the build budget), so the manager never pays construction with company money; a
/// decision can instead
/// - open build mode (ManagerSystem calls BuildModeCamera.TryEnterBuildMode once its desk closes) and raise
///   <see cref="BuildModeRequested"/> with a spot hint ("expand", "new_building", "office") so the tower can scroll or
///   highlight the right place;
/// - grant a build-budget discount for some days: the building system multiplies its build-budget prices by
///   <see cref="PriceMultiplier"/>(category) and calls <see cref="NotifyPurchase"/>(category) after a purchase that used it
///   (perks with a limited number of uses count down).
/// Categories: "floor", "room", "furniture", "building" (a new building); a perk of category "any" covers them all. Perks
/// don't stack: the best matching one counts. Nothing here changes a price by itself: without the building side's two calls
/// the perks are only announced.
/// </summary>
public static class ManagerBuildPerks
{
    /// <summary>The perks changed (granted, used up, expired, or a save loaded).</summary>
    public static event Action Changed;

    /// <summary>A decision opened build mode; the argument is a spot hint ("expand", "new_building", "office" or "").</summary>
    public static event Action<string> BuildModeRequested;

    // The manager's live perk list (ManagerSystem's saved state)
    static List<ManagerPerk> source;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Changed = null;
        BuildModeRequested = null;
        source = null;
    }

    static int Today => CompanyManager.Instance != null ? CompanyManager.Instance.currentDay : 0;

    static bool Covers(ManagerPerk perk, string category, int day)
    {
        if (perk == null || perk.discount <= 0f) return false;
        if (perk.untilDay > 0 && day > perk.untilDay) return false;
        if (perk.usesLeft == 0) return false;
        return perk.category == "any" || string.IsNullOrEmpty(category) || perk.category == category;
    }

    /// <summary>
    /// Multiply a build-budget price of this category ("floor", "room", "furniture", "building") by this: 1 without a perk,
    /// 0.85 with a 15% discount. Never below 0.5.
    /// </summary>
    public static double PriceMultiplier(string category)
    {
        ManagerPerk best = Best(category);
        return best == null ? 1.0 : Math.Max(0.5, 1.0 - best.discount / 100.0);
    }

    /// <summary>The discount in percent that applies to this category now (0 = none).</summary>
    public static float DiscountPercent(string category)
    {
        ManagerPerk best = Best(category);
        return best != null ? best.discount : 0f;
    }

    /// <summary>A purchase of this category used the discount: a perk with limited uses counts down.</summary>
    public static void NotifyPurchase(string category)
    {
        ManagerPerk best = Best(category);
        if (best == null || best.usesLeft < 0) return;
        best.usesLeft = Math.Max(0, best.usesLeft - 1);
        RaiseChanged();
    }

    /// <summary>The perks in force now (copies).</summary>
    public static List<ManagerPerk> Active()
    {
        var list = new List<ManagerPerk>();
        if (source == null) return list;
        int day = Today;
        foreach (ManagerPerk perk in source)
            if (Covers(perk, null, day))
                list.Add(new ManagerPerk { id = perk.id, category = perk.category, discount = perk.discount, untilDay = perk.untilDay, usesLeft = perk.usesLeft });
        return list;
    }

    static ManagerPerk Best(string category)
    {
        if (source == null) return null;
        int day = Today;
        ManagerPerk best = null;
        foreach (ManagerPerk perk in source)
            if (Covers(perk, category, day) && (best == null || perk.discount > best.discount)) best = perk;
        return best;
    }

    // ------------------------------------------------------------------
    // ManagerSystem's side
    // ------------------------------------------------------------------

    internal static void SetSource(List<ManagerPerk> perks)
    {
        source = perks;
        RaiseChanged();
    }

    internal static void RaiseChanged()
    {
        Action handlers = Changed;
        if (handlers == null) return;
        foreach (Action handler in handlers.GetInvocationList())
        {
            try { handler(); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }

    internal static void RaiseBuildModeRequested(string spot)
    {
        Action<string> handlers = BuildModeRequested;
        if (handlers == null) return;
        foreach (Action<string> handler in handlers.GetInvocationList())
        {
            try { handler(spot ?? ""); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}
