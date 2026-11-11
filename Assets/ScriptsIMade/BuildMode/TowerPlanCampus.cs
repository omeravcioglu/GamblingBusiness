using System;
using System.Collections.Generic;

/// <summary>
/// The campus part of TowerPlan (still independent of Unity): requirements, buying buildings, skybridges between
/// neighbours, room levels, upkeep and the daily totals.
/// - Buildings are bought to the right of the last one and never sold (the HQ is slot 0). A building type has a price, a
///   floor price curve, its own tiles and top floor, an upkeep per floor, a maximum count and requirements.
/// - A link (skybridge) joins two neighbouring buildings at a floor both have, never below its type's lowest floor (2).
/// - A room level raises capacity, multiplies the room's effects and upkeep, and draws the room nicer.
/// - Upkeep a day: every room's level upkeep + every building's floors x its type's upkeep per floor + every link's; the
///   savings rooms (legal, accounting) take a capped share off. The salary savings are a capped share of salaries.
/// </summary>
public sealed partial class TowerPlan
{
    // ------------------------------------------------------------------
    // Requirements
    // ------------------------------------------------------------------

    TowerProgress Progress => ProgressProvider != null ? ProgressProvider() : default;

    /// <summary>What a room type still needs (a skill, company progress, another room), or kind None.</summary>
    public TowerLock LockOf(TowerRoomDefinition definition)
    {
        if (definition == null) return default;
        if (DemoTowerRules.LocksRoom(definition)) return new TowerLock { kind = TowerLockKind.Demo };   // Demo: full game only
        return FirstUnmet(definition.unlock, definition.lockReason, definition.requires);
    }

    public TowerLock LockOf(TowerBuildingType type)
    {
        if (type == null) return default;
        if (!type.isHeadquarters && DemoTowerRules.LocksBuildingType(type.id)) return new TowerLock { kind = TowerLockKind.Demo };   // Demo: full game only
        return FirstUnmet(type.unlock, type.lockReason, type.requires);
    }

    public TowerLock LockOf(TowerConnectionType type)
    {
        if (type == null) return default;
        if (DemoTowerRules.LocksLinks) return new TowerLock { kind = TowerLockKind.Demo };   // Demo: full game only
        return FirstUnmet(type.unlock, type.lockReason, type.requires);
    }

    /// <summary>The first requirement that isn't met: the skill first, then the company's progress, then another room.</summary>
    public TowerLock FirstUnmet(TowerUnlock skill, string skillReason, TowerRequirements requires)
    {
        if (skill != TowerUnlock.None && UnlockCheck != null && !UnlockCheck(skill))
            return new TowerLock { kind = TowerLockKind.Skill, value = (int)skill, text = skillReason ?? "" };
        if (requires == null || requires.IsEmpty) return default;
        bool known = ProgressProvider != null;
        TowerProgress progress = Progress;
        if (known && requires.minPeakValue > 0.0 && progress.peakValue + 0.5 < requires.minPeakValue)
            return new TowerLock { kind = TowerLockKind.PeakValue, value = requires.minPeakValue };
        if (known && requires.minStatusTier > 0 && progress.statusTier < requires.minStatusTier)
            return new TowerLock { kind = TowerLockKind.StatusTier, value = requires.minStatusTier };
        if (known && requires.minDay > 0 && progress.day < requires.minDay)
            return new TowerLock { kind = TowerLockKind.Day, value = requires.minDay };
        if (requires.minHqFloors > 0 && Headquarters.Floors < requires.minHqFloors)
            return new TowerLock { kind = TowerLockKind.HqFloors, value = requires.minHqFloors };
        if (requires.minBuildings > 0 && buildings.Count < requires.minBuildings)
            return new TowerLock { kind = TowerLockKind.Buildings, value = requires.minBuildings };
        if (!string.IsNullOrEmpty(requires.requiresRoom) && Rules.Find(requires.requiresRoom) != null && CountRooms(requires.requiresRoom) == 0)
            return new TowerLock { kind = TowerLockKind.Room, text = requires.requiresRoom };
        return default;
    }

    // ------------------------------------------------------------------
    // Buildings
    // ------------------------------------------------------------------

    public int BuildingCount => buildings.Count;

    /// <summary>Buildings of this type on the campus.</summary>
    public int CountBuildings(TowerBuildingType type)
    {
        int count = 0;
        foreach (TowerBuilding building in buildings)
            if (building.Type == type) count++;
        return count;
    }

    /// <summary>Can a building of <paramref name="type"/> be bought (it goes to the right of the last one)?</summary>
    public TowerCheck CheckBuyBuilding(TowerBuildingType type, bool checkBudget = true)
    {
        if (type == null) return TowerCheck.Fail(TowerIssue.Missing);
        if (type.isHeadquarters) return TowerCheck.Fail(TowerIssue.BuiltIn);
        TowerLock lockInfo = LockOf(type);
        double price = BuildingPrice(type);
        if (lockInfo.IsLocked) return new TowerCheck { issue = TowerIssue.Locked, lockInfo = lockInfo, price = price };
        if (type.maxCount > 0 && CountBuildings(type) >= type.maxCount)
            return new TowerCheck { issue = TowerIssue.BuildingLimit, limit = type.maxCount, price = price };
        var check = new TowerCheck { price = price };
        if (checkBudget && !CanAfford(check.price)) check.issue = TowerIssue.CantAfford;
        return check;
    }

    /// <summary>Buy a building (charges the build budget): one floor, placed right of the last building. Null if it can't be bought.</summary>
    public TowerBuilding BuyBuilding(TowerBuildingType type, out TowerCheck check)
    {
        check = CheckBuyBuilding(type);
        if (!check.Ok) return null;
        budget = Clean(budget - check.price);
        TowerBuilding building = AppendBuilding(type, 1, check.price);
        MarkChanged();
        Charged(PriceBuilding);
        return building;
    }

    /// <summary>What a building of <paramref name="type"/> costs now (after any discount).</summary>
    public double BuildingPrice(TowerBuildingType type)
    {
        return type == null ? 0.0 : PriceOf(PriceBuilding, type.price);
    }

    /// <summary>What a <paramref name="type"/> link at <paramref name="floor"/> costs now (after any discount).</summary>
    public double LinkPrice(TowerConnectionType type, int floor)
    {
        return type == null ? 0.0 : PriceOf(PriceLink, type.PriceAt(floor));
    }

    /// <summary>Put a building back without charging (loading): no lock or count checks. Null for the HQ's type or none.</summary>
    public TowerBuilding RestoreBuilding(TowerBuildingType type, int floors, double paid, double floorsPaid, IList<string> floorTypes = null)
    {
        if (type == null || type.isHeadquarters) return null;
        TowerBuilding building = AppendBuilding(type, floors, Clean(paid));
        building.FloorsPaid = Clean(floorsPaid);
        CopyFloorTypes(building, floorTypes);
        MarkChanged();
        return building;
    }

    /// <summary>Set what a building's floors cost and its floor types (loading; the HQ is never bought, so it comes back this way).</summary>
    public void RestoreBuildingRecord(TowerBuilding building, double floorsPaid, IList<string> floorTypes)
    {
        if (!Contains(building)) return;
        building.FloorsPaid = Clean(floorsPaid);
        CopyFloorTypes(building, floorTypes);
    }

    static void CopyFloorTypes(TowerBuilding building, IList<string> floorTypes)
    {
        building.floorTypes.Clear();
        if (floorTypes == null) return;
        foreach (string id in floorTypes) building.floorTypes.Add(id ?? "");
    }

    TowerBuilding AppendBuilding(TowerBuildingType type, int floors, double paid)
    {
        var building = new TowerBuilding(nextBuildingId++, type, Math.Max(1, Math.Min(type.maxFloors, floors)), paid)
        {
            Slot = buildings.Count,
            Number = CountBuildings(type) + 1
        };
        buildings.Add(building);
        return building;
    }

    // ------------------------------------------------------------------
    // Links (skybridges)
    // ------------------------------------------------------------------

    /// <summary>The building right of <paramref name="left"/>, or null.</summary>
    public TowerBuilding RightNeighbour(TowerBuilding left)
    {
        return left != null && Contains(left) ? BuildingAt(left.Slot + 1) : null;
    }

    /// <summary>Links between <paramref name="left"/> and its right neighbour.</summary>
    public int CountConnections(TowerBuilding left)
    {
        int count = 0;
        foreach (TowerConnection link in connections)
            if (link.Left == left) count++;
        return count;
    }

    /// <summary>The link between <paramref name="left"/> and its right neighbour at <paramref name="floor"/>, or null.</summary>
    public TowerConnection ConnectionAt(TowerBuilding left, int floor)
    {
        foreach (TowerConnection link in connections)
            if (link.Left == left && link.Floor == floor) return link;
        return null;
    }

    /// <summary>Can a <paramref name="type"/> link join <paramref name="left"/> and its right neighbour at <paramref name="floor"/>?</summary>
    public TowerCheck CheckConnection(TowerConnectionType type, TowerBuilding left, int floor, bool checkBudget = true)
    {
        if (type == null) return TowerCheck.Fail(TowerIssue.Missing);
        TowerBuilding right = RightNeighbour(left);
        if (right == null) return TowerCheck.Fail(TowerIssue.NotConnectable);
        double price = LinkPrice(type, floor);
        TowerLock lockInfo = LockOf(type);
        if (lockInfo.IsLocked) return new TowerCheck { issue = TowerIssue.Locked, lockInfo = lockInfo, price = price };
        if (floor < type.minFloor)
            return new TowerCheck { issue = TowerIssue.FloorRule, floorRule = TowerFloorRule.MinFloor, floorValue = type.minFloor, price = price };
        if (floor > left.Floors || floor > right.Floors) return new TowerCheck { issue = TowerIssue.NoSuchFloor, price = price };
        if (ConnectionAt(left, floor) != null) return new TowerCheck { issue = TowerIssue.Overlaps, price = price };
        if (CountConnections(left) >= type.maxPerPair) return new TowerCheck { issue = TowerIssue.TooManyLinks, limit = type.maxPerPair, price = price };
        var check = new TowerCheck { price = price };
        if (checkBudget && !CanAfford(price)) check.issue = TowerIssue.CantAfford;
        return check;
    }

    /// <summary>Build a link (charges the build budget). Null if it can't be built.</summary>
    public TowerConnection BuildConnection(TowerConnectionType type, TowerBuilding left, int floor, out TowerCheck check)
    {
        check = CheckConnection(type, left, floor);
        if (!check.Ok) return null;
        budget = Clean(budget - check.price);
        var link = new TowerConnection(nextConnectionId++, type, left, RightNeighbour(left), floor, check.price);
        connections.Add(link);
        MarkChanged();
        Charged(PriceLink);
        return link;
    }

    /// <summary>Put a link back without charging (loading): the pair and floor must still work. Null if it can't.</summary>
    public TowerConnection RestoreConnection(TowerConnectionType type, TowerBuilding left, int floor, double paid)
    {
        TowerBuilding right = RightNeighbour(left);
        if (type == null || right == null || floor < 1 || floor > left.Floors || floor > right.Floors || ConnectionAt(left, floor) != null)
            return null;
        var link = new TowerConnection(nextConnectionId++, type, left, right, floor, Clean(paid));
        connections.Add(link);
        MarkChanged();
        return link;
    }

    // ------------------------------------------------------------------
    // Room levels
    // ------------------------------------------------------------------

    /// <summary>Price of the room's next level (0 at the top or without levels), after any discount (a room's).</summary>
    public double RoomUpgradePrice(TowerRoom room)
    {
        if (room == null || room.IsTopRoomLevel) return 0.0;
        return PriceOf(PriceRoom, room.Definition.UpgradeCost(room.Level + 1));
    }

    public TowerCheck CheckRoomUpgrade(TowerRoom room, bool checkBudget = true)
    {
        if (!Contains(room)) return TowerCheck.Fail(TowerIssue.Missing);
        if (!room.Definition.HasLevels) return TowerCheck.Fail(TowerIssue.NoRoomLevels);
        if (room.IsTopRoomLevel) return TowerCheck.Fail(TowerIssue.TopRoomLevel);
        var check = new TowerCheck { price = RoomUpgradePrice(room) };
        if (checkBudget && !CanAfford(check.price)) check.issue = TowerIssue.CantAfford;
        return check;
    }

    /// <summary>Pay for the room's next level (more places, stronger effects, more upkeep, a nicer look).</summary>
    public bool UpgradeRoom(TowerRoom room, out TowerCheck check)
    {
        check = CheckRoomUpgrade(room);
        if (!check.Ok) return false;
        budget = Clean(budget - check.price);
        room.Level++;
        room.LevelPaid += check.price;
        MarkChanged();
        Charged(PriceRoom);
        return true;
    }

    // ------------------------------------------------------------------
    // Upkeep and savings
    // ------------------------------------------------------------------

    /// <summary>The room's upkeep a day at its level (before savings).</summary>
    public double RoomUpkeep(TowerRoom room)
    {
        return room == null ? 0.0 : room.Definition.UpkeepAt(room.Level);
    }

    /// <summary>The building's upkeep a day: its floors x its type's upkeep per floor (before savings).</summary>
    public double BuildingUpkeep(TowerBuilding building)
    {
        return building == null ? 0.0 : Math.Max(0.0, building.Type.upkeepPerFloor) * building.Floors;
    }

    public double ConnectionUpkeep(TowerConnection link)
    {
        return link == null ? 0.0 : Math.Max(0.0, link.Type.upkeep);
    }

    /// <summary>Upkeep a day of everything before savings.</summary>
    public double GrossUpkeep
    {
        get
        {
            double total = 0.0;
            foreach (TowerRoom room in rooms) total += RoomUpkeep(room);
            foreach (TowerBuilding building in buildings) total += BuildingUpkeep(building);
            foreach (TowerConnection link in connections) total += ConnectionUpkeep(link);
            return total;
        }
    }

    /// <summary>Share of the upkeep the savings rooms take off (0..maxUpkeepSavings).</summary>
    public float UpkeepSavingsShare
    {
        get
        {
            float percent = 0f;
            foreach (TowerRoom room in rooms) percent += UpkeepSavingsPercent(room);
            return Math.Min(Math.Max(0f, Rules.maxUpkeepSavings), percent / 100f);
        }
    }

    /// <summary>Share of the salaries the savings rooms take off (0..maxSalarySavings).</summary>
    public float SalarySavingsShare
    {
        get
        {
            float percent = 0f;
            foreach (TowerRoom room in rooms) percent += SalarySavingsPercent(room);
            return Math.Min(Math.Max(0f, Rules.maxSalarySavings), percent / 100f);
        }
    }

    /// <summary>What the company pays a day for the campus (upkeep after savings).</summary>
    public double NetUpkeep => GrossUpkeep * (1.0 - UpkeepSavingsShare);

    /// <summary>Upkeep a day that a room of <paramref name="definition"/> would add at level 1 (after today's savings).</summary>
    public double UpkeepIfBuilt(TowerRoomDefinition definition)
    {
        return definition == null ? 0.0 : definition.UpkeepAt(1) * (1.0 - UpkeepSavingsShare);
    }

    /// <summary>Skill points a day from the knowledge rooms, capped (TowerRules.maxKnowledgePerDay).</summary>
    public float KnowledgeTotal
    {
        get
        {
            float total = 0f;
            foreach (TowerRoom room in rooms) total += KnowledgePerDay(room);
            return Math.Min(Math.Max(0f, Rules.maxKnowledgePerDay), total);
        }
    }

    // ------------------------------------------------------------------
    // Totals
    // ------------------------------------------------------------------

    /// <summary>Daily income, output, happiness, trust, capacity, upkeep and knowledge of the whole campus.</summary>
    public TowerTotals ComputeTotals()
    {
        var totals = new TowerTotals { floors = TotalFloors, buildings = buildings.Count, connections = connections.Count };
        float meter = Math.Max(0f, Rules.meterPointsPerPercent);
        float knowledge = 0f, upkeepSaving = 0f, salarySaving = 0f;
        foreach (TowerRoom room in rooms)
        {
            double furniture = FurnitureIncome(room);
            totals.furnitureIncome += furniture;
            totals.dailyIncome += RoomBonusIncome(room) + furniture;
            totals.productivity += ProductivityPercent(room) / 100f;
            totals.happinessPerDay += HappinessPercent(room) * meter;
            totals.trustPerDay += TrustPercent(room) * meter;
            totals.capacity += room.Capacity;
            totals.furnitureLevels += room.TotalLevels;
            totals.upkeepGross += RoomUpkeep(room);
            knowledge += KnowledgePerDay(room);
            upkeepSaving += UpkeepSavingsPercent(room);
            salarySaving += SalarySavingsPercent(room);
            if (!room.IsFixed) totals.builtRooms++;
        }
        foreach (TowerBuilding building in buildings) totals.upkeepGross += BuildingUpkeep(building);
        foreach (TowerConnection link in connections)
        {
            totals.upkeepGross += ConnectionUpkeep(link);
            totals.productivity += link.Type.productivityPercent / 100f;
            totals.happinessPerDay += link.Type.happinessPercent * meter;
            totals.trustPerDay += link.Type.trustPercent * meter;
        }
        totals.seats = totals.capacity;
        totals.upkeepSavings = Math.Min(Math.Max(0f, Rules.maxUpkeepSavings), upkeepSaving / 100f);
        totals.upkeep = totals.upkeepGross * (1.0 - totals.upkeepSavings);
        totals.salarySavings = Math.Min(Math.Max(0f, Rules.maxSalarySavings), salarySaving / 100f);
        totals.knowledgePerDay = Math.Min(Math.Max(0f, Rules.maxKnowledgePerDay), knowledge);
        return totals;
    }
}
