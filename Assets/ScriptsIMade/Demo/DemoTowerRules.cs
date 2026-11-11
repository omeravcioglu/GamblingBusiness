using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The demo's construction table and its FULL GAME locks. TowerBuilder passes its copy of the rules through ApplyTo while
/// demo mode is on (before TowerPlan sanitizes them); TowerPlan asks the Locks* methods first in its lock checks.
/// Base values are edited (not TowerPlan.PriceMultiplier, which clamps at 0.5 and stacks with the manager's discounts).
/// </summary>
public static class DemoTowerRules
{
    /// <summary>Rooms at or above this status tier stay in the full game.</summary>
    public const int LockedFromStatusTier = 2;

    // Room id -> demo price (null = unchanged) and level costs (L2, L3, ... in order; null = unchanged)
    static readonly (string id, double? cost, double[] levels)[] RoomPrices =
    {
        ("starter_office", null, new double[] { 5000, 12000, 36000 }),
        ("small_office", 3000, new double[] { 5000, 12000 }),
        ("big_office", 9000, new double[] { 15000, 36000, 90000 }),
        ("break_room", 5000, null),
        ("meeting_room", 7000, null),
        ("server_room", 12000, null),
        ("casino_room", 25000, null),
    };

    /// <summary>Edit the demo's prices and requirements into <paramref name="rules"/> (a copy: never the catalog asset).</summary>
    public static void ApplyTo(TowerRules rules)
    {
        if (rules == null) return;
        var matched = new List<string>();
        var missing = new List<string>();

        TowerBuildingType hq = rules.Headquarters ?? rules.FindBuildingType(TowerRules.HqTypeId);
        if (hq != null)
        {
            hq.secondFloorPrice = 6000;
            hq.floorPriceGrowth = 1.35;
            hq.maxFloors = 6;
            matched.Add(hq.id);
        }
        else missing.Add(TowerRules.HqTypeId);
        // Without an HQ type the legacy global curve applies
        rules.secondFloorPrice = 6000;
        rules.floorPriceGrowth = 1.35;
        rules.maxFloors = 6;

        foreach (var entry in RoomPrices)
        {
            TowerRoomDefinition room = rules.Find(entry.id);
            if (room == null)
            {
                missing.Add(entry.id);
                continue;
            }
            if (entry.cost.HasValue) room.cost = entry.cost.Value;
            if (entry.levels != null && room.levels != null)
            {
                int count = Mathf.Min(entry.levels.Length, room.levels.Count);
                for (int i = 0; i < count; i++)
                    if (room.levels[i] != null) room.levels[i].cost = entry.levels[i];
                if (count < entry.levels.Length) Debug.LogWarning("[Demo] " + entry.id + " has only " + room.levels.Count + " levels; the demo set " + count + ".");
            }
            matched.Add(entry.id);
        }

        rules.furnitureBasePrice = 500;
        rules.incomePerLevel = 200;

        // Every peak-value requirement halved (HR $75k, cafeteria and marketing $125k, training $150k, open plan $200k, accounting $250k)
        int halved = 0;
        if (rules.rooms != null)
        {
            foreach (TowerRoomDefinition room in rules.rooms)
            {
                if (room == null || room.requires == null || room.requires.minPeakValue <= 0.0) continue;
                room.requires.minPeakValue *= 0.5;
                halved++;
            }
        }

        Debug.Log("[Demo] Demo construction prices: " + string.Join(", ", matched) + "; " + halved + " peak-value requirements halved; HQ up to 6 floors.");
        if (missing.Count > 0) Debug.LogWarning("[Demo] Not in the tower rules (demo prices skipped): " + string.Join(", ", missing));
    }

    /// <summary>A room only the full game builds: status tier 2+ or the trading floor (no stock market in the demo).</summary>
    public static bool LocksRoom(TowerRoomDefinition definition)
    {
        if (definition == null || !DemoMode.Active) return false;
        if (definition.id == "trading_floor") return true;
        return definition.requires != null && definition.requires.minStatusTier >= LockedFromStatusTier;
    }

    /// <summary>Every building past the HQ is in the full game.</summary>
    public static bool LocksBuildingType(string id)
    {
        return DemoMode.Active && id != TowerRules.HqTypeId;
    }

    /// <summary>Skybridges (and any other link) are in the full game.</summary>
    public static bool LocksLinks => DemoMode.Active;

    /// <summary>The Campus tool and buying buildings are refused in the demo.</summary>
    public static bool LocksCampus => DemoMode.Active;

    /// <summary>The demo's toast when the campus is tried.</summary>
    public static string CampusLockedText => DemoMode.T("demo.lock.campus");

    /// <summary>The cheapest office the player can build now (unlocked, not built in), or null.</summary>
    public static TowerRoomDefinition CheapestOffice(TowerPlan plan)
    {
        if (plan == null || plan.Rules == null || plan.Rules.rooms == null) return null;
        TowerRoomDefinition best = null;
        double bestPrice = double.MaxValue;
        foreach (TowerRoomDefinition room in plan.Rules.rooms)
        {
            if (room == null || room.isFixed || !room.IsOffice || room.capacity <= 0 || !plan.IsUnlocked(room)) continue;
            double price = plan.RoomPrice(room);
            if (price < bestPrice)
            {
                best = room;
                bestPrice = price;
            }
        }
        return best;
    }

    /// <summary>The HQ has no spot left for the cheapest office (the next room needs a new floor first).</summary>
    public static bool NeedsFloor(TowerPlan plan)
    {
        if (plan == null) return false;
        TowerRoomDefinition office = CheapestOffice(plan);
        return office == null || !plan.TryFindSpot(office, plan.Headquarters, out _, out _);
    }

    /// <summary>
    /// What the next office costs in all: the cheapest office, plus the HQ's next floor while no spot is free (0 = nothing
    /// to aim at). <paramref name="goal"/> names it for the gamble pop-up.
    /// </summary>
    public static double NextOfficePrice(TowerPlan plan, out string goal)
    {
        goal = "";
        if (plan == null) return 0.0;
        TowerRoomDefinition office = CheapestOffice(plan);
        if (office == null) return 0.0;
        double price = plan.RoomPrice(office);
        goal = TowerText.RoomName(office);
        if (!plan.TryFindSpot(office, plan.Headquarters, out _, out _))
        {
            if (plan.IsTopFloorReached) return 0.0;
            price += plan.NextFloorPrice;
        }
        return price;
    }
}
