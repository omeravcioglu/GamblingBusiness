using System;
using System.Collections.Generic;

/// <summary>Why a campus action can't happen.</summary>
public enum TowerIssue
{
    None,
    NoSuchFloor,    // that floor isn't built
    OffTheFloor,    // the room hangs over the end of the floor
    Overlaps,       // another room (or skybridge) is in the way
    Locked,         // the room / building / link needs a skill or more progress first (TowerCheck.lockInfo)
    CantAfford,     // not enough build budget
    BuiltIn,        // the Lobby, the CEO office and the first office stay where they are
    TopFloor,       // the building is as tall as it gets
    MaxLevel,       // the furniture is already at the top level
    Missing,        // no such room, slot, building or link type
    // The campus (2026-09-29)
    LimitReached,   // the room's maximum per building / per company is reached (TowerCheck.limit)
    WrongBuilding,  // the room can't stand in this kind of building
    FloorRule,      // ground floor / top floor / a minimum floor (TowerCheck.floorRule, floorValue)
    CapacityInUse,  // selling it would leave fewer places than employees (TowerCheck.limit = places left)
    NotConnectable, // a link needs two neighbouring buildings
    TooManyLinks,   // the pair already has as many links as allowed (TowerCheck.limit)
    TopRoomLevel,   // the room is at its top level
    NoRoomLevels,   // the room has no upgrades
    BuildingLimit   // as many buildings of this type as allowed (TowerCheck.limit)
}

/// <summary>What a locked room, building or link still needs.</summary>
public enum TowerLockKind
{
    None,
    Skill,       // a skill (TowerUnlock) the skill tree hasn't unlocked
    PeakValue,   // the highest company value reached
    StatusTier,  // StatusLadder's tier
    Day,
    HqFloors,
    Buildings,   // buildings owned
    Room,        // a room of another type must be built first (text = its id)
    Demo         // Demo: only in the full game (DemoTowerRules); never saved
}

/// <summary>The first requirement that isn't met (kind None = unlocked).</summary>
public struct TowerLock
{
    public TowerLockKind kind;
    /// <summary>The number needed (value, tier, day, floors, buildings) or the skill (TowerUnlock as a number).</summary>
    public double value;
    /// <summary>Room: the id of the room needed. Skill: the definition's own lock reason (English, may be empty).</summary>
    public string text;

    public bool IsLocked => kind != TowerLockKind.None;
}

/// <summary>The run's progress that requirements are checked against (TowerBuilder fills it from the game).</summary>
public struct TowerProgress
{
    /// <summary>Highest company value reached.</summary>
    public double peakValue;
    /// <summary>StatusLadder tier (highest reached).</summary>
    public int statusTier;
    public int day;
}

/// <summary>Result of a placement, move, floor, upgrade, purchase or link check.</summary>
public struct TowerCheck
{
    public TowerIssue issue;
    /// <summary>The room in the way (Overlaps).</summary>
    public TowerRoom blocker;
    /// <summary>What it costs (0 for a move).</summary>
    public double price;
    /// <summary>OffTheFloor: how many tiles of the floor are left on that side.</summary>
    public int tilesLeft;
    /// <summary>LimitReached / BuildingLimit / TooManyLinks: the maximum. CapacityInUse: the places that would be left.</summary>
    public int limit;
    /// <summary>LimitReached: true when it is the per-building limit (else per company).</summary>
    public bool perBuilding;
    /// <summary>FloorRule: which rule, and its floor (MinFloor: the lowest; a link's lowest floor).</summary>
    public TowerFloorRule floorRule;
    public int floorValue;
    /// <summary>Locked: what is still needed.</summary>
    public TowerLock lockInfo;

    public bool Ok => issue == TowerIssue.None;

    public static TowerCheck Fail(TowerIssue issue, TowerRoom blocker = null, int tilesLeft = 0, double price = 0.0)
    {
        return new TowerCheck { issue = issue, blocker = blocker, tilesLeft = tilesLeft, price = price };
    }
}

/// <summary>What the campus adds each day (before the company's caps).</summary>
public struct TowerTotals
{
    /// <summary>Rooms (scaled by their furniture and level) plus the furniture levels' own income.</summary>
    public double dailyIncome;
    public double furnitureIncome;
    /// <summary>0.03 = +3% employee output.</summary>
    public float productivity;
    public float happinessPerDay;
    public float trustPerDay;
    /// <summary>Employees the rooms hold (old name: seats).</summary>
    public int seats;
    /// <summary>Rooms the player built (the built-in rooms aren't counted).</summary>
    public int builtRooms;
    /// <summary>Floors of every building together.</summary>
    public int floors;
    public int furnitureLevels;
    // The campus (2026-09-29)
    /// <summary>Employees the rooms hold (the hard limit on hiring).</summary>
    public int capacity;
    /// <summary>Upkeep a day before savings (rooms, buildings' floors, links).</summary>
    public double upkeepGross;
    /// <summary>Share of the upkeep the savings rooms take off (capped).</summary>
    public float upkeepSavings;
    /// <summary>Upkeep a day after savings (what the company pays).</summary>
    public double upkeep;
    /// <summary>Share of the salaries the savings rooms take off (capped).</summary>
    public float salarySavings;
    /// <summary>Skill points a day from knowledge rooms (capped).</summary>
    public float knowledgePerDay;
    public int buildings;
    public int connections;
}

/// <summary>A building of the campus: its type, place in the row (slot 0 = the HQ, leftmost) and floors.</summary>
public sealed class TowerBuilding
{
    internal TowerBuilding(int id, TowerBuildingType type, int floors, double paid)
    {
        Id = id;
        Type = type;
        Floors = Math.Max(1, floors);
        Paid = paid;
    }

    /// <summary>Unique within its plan.</summary>
    public int Id { get; }
    public TowerBuildingType Type { get; }
    /// <summary>Position in the row, left to right (0 = the HQ).</summary>
    public int Slot { get; internal set; }
    public int Floors { get; internal set; }
    /// <summary>What buying it cost (0 for the HQ).</summary>
    public double Paid { get; internal set; }
    /// <summary>What its floors cost together (for the record).</summary>
    public double FloorsPaid { get; internal set; }
    /// <summary>Its number among the buildings of its type (1 = the first annex bought), for its name.</summary>
    public int Number { get; internal set; } = 1;
    internal readonly List<string> floorTypes = new List<string>();

    public int TilesPerFloor => Math.Max(2, Type.tiles);
    public int MaxFloors => Math.Max(Floors, Type.maxFloors);
    public bool IsHeadquarters => Type.isHeadquarters;
    public bool IsTopFloorReached => Floors >= Type.maxFloors;

    /// <summary>The floor type of floor <paramref name="floor"/> (the hook for later floor kinds; "standard" today).</summary>
    public string FloorTypeAt(int floor)
    {
        int i = floor - 1;
        return i >= 0 && i < floorTypes.Count && !string.IsNullOrEmpty(floorTypes[i]) ? floorTypes[i] : TowerRules.StandardFloorId;
    }
}

/// <summary>A link between two neighbouring buildings (a skybridge) at one floor.</summary>
public sealed class TowerConnection
{
    internal TowerConnection(int id, TowerConnectionType type, TowerBuilding left, TowerBuilding right, int floor, double paid)
    {
        Id = id;
        Type = type;
        Left = left;
        Right = right;
        Floor = floor;
        Paid = paid;
    }

    public int Id { get; }
    public TowerConnectionType Type { get; }
    /// <summary>The building on the left (its right neighbour is Right).</summary>
    public TowerBuilding Left { get; }
    public TowerBuilding Right { get; }
    public int Floor { get; }
    public double Paid { get; }
}

/// <summary>A room of the campus: its building, floor (1 = street level), first tile (0 = next to the lift), level and furniture levels.</summary>
public sealed class TowerRoom
{
    internal readonly int[] levels;
    internal readonly double[] furniturePaid;

    internal TowerRoom(int id, TowerRoomDefinition definition, TowerBuilding building, int floor, int tile, double paid)
    {
        Id = id;
        Definition = definition;
        Building = building;
        Floor = floor;
        Tile = tile;
        Paid = paid;
        Level = 1;
        levels = new int[Math.Max(0, definition.SlotCount)];
        furniturePaid = new double[levels.Length];
    }

    /// <summary>Unique within its plan.</summary>
    public int Id { get; }
    public TowerRoomDefinition Definition { get; }
    public TowerBuilding Building { get; internal set; }
    public int Floor { get; internal set; }
    public int Tile { get; internal set; }
    /// <summary>What was paid for the room itself (selling refunds a share).</summary>
    public double Paid { get; internal set; }
    /// <summary>The room's level (1 = as built; Definition.MaxLevel at the top).</summary>
    public int Level { get; internal set; }
    /// <summary>What its room levels cost together (selling refunds a share).</summary>
    public double LevelPaid { get; internal set; }

    public string Name => Definition.displayName;
    /// <summary>Built in: can't be moved or sold (the Lobby, the CEO office, the first office).</summary>
    public bool IsFixed => Definition.isFixed;
    public bool IsStarter => Definition.isStarter;
    public int Tiles => Definition.Tiles;
    /// <summary>First tile after the room.</summary>
    public int EndTile => Tile + Tiles;
    public int SlotCount => levels.Length;
    public int Capacity => Definition.CapacityAt(Level);
    public bool IsTopRoomLevel => Level >= Definition.MaxLevel;

    public int GetLevel(int slot)
    {
        return slot >= 0 && slot < levels.Length ? levels[slot] : 0;
    }

    /// <summary>
    /// What was spent on this slot's levels: prices paid and the stakes of gambles that won levels. A level lost on a
    /// roll keeps its price here, so selling refunds half of everything spent on the slot (never more than half).
    /// </summary>
    public double GetFurniturePaid(int slot)
    {
        return slot >= 0 && slot < furniturePaid.Length ? furniturePaid[slot] : 0.0;
    }

    public int TotalLevels
    {
        get
        {
            int total = 0;
            foreach (int level in levels) total += level;
            return total;
        }
    }

    public double TotalFurniturePaid
    {
        get
        {
            double total = 0.0;
            foreach (double paid in furniturePaid) total += paid;
            return total;
        }
    }

    /// <summary>Covers that tile of the floor in its own building (any building's floor numbers: legacy, prefer the building overload).</summary>
    public bool Covers(int floor, int tile)
    {
        return floor == Floor && tile >= Tile && tile < EndTile;
    }

    public bool Covers(TowerBuilding building, int floor, int tile)
    {
        return building == Building && Covers(floor, tile);
    }

    public bool Overlaps(int floor, int tile, int tiles)
    {
        return floor == Floor && tile < EndTile && Tile < tile + tiles;
    }

    public bool Overlaps(TowerBuilding building, int floor, int tile, int tiles)
    {
        return building == Building && Overlaps(floor, tile, tiles);
    }
}

/// <summary>
/// The rules of the HQ campus, independent of Unity (tested outside it):
/// - The campus is a row of buildings, the HQ first (leftmost, always there). A building has floors of its type's tiles
///   (the HQ: 8). The HQ's floor 1 has the built-in Lobby, CEO office and first office, which can't be moved or sold. A
///   new floor goes on top and costs the building type's floor price (the HQ: floor 2 $10,000, then about x1.42 each).
///   More buildings are bought to the right (TowerPlanCampus.cs) and neighbours can be linked by skybridges.
/// - A room takes whole tiles on one floor of one building: it must fit, not overlap, be allowed in that building type,
///   follow its floor rule (ground / top / lowest floor) and its limits (per building / per company). Building costs its
///   price from the build budget; locked rooms (a skill, company progress, another room first) can't be built. Moving is
///   free and keeps the furniture and level. Selling refunds sellRefundShare (50%) of what the room, its levels and its
///   furniture cost, unless it would leave fewer places than employees (the builder asks CheckSell).
/// - Rooms have levels (TowerRoomDefinition.levels): more capacity, x effects, more upkeep, a nicer look.
/// - Every furniture slot has a level 0-5 (Basic..Ultimate). The next level costs LevelPrice (paying: +1 level). Gambles
///   take the same price first (TakeStake) and settle with ChangeLevel. Each level earns incomePerLevel a day and adds
///   bonusPerLevel of the room's base bonus.
/// - Capacity (the employees the rooms hold) is a hard limit on hiring (OfficeCapacity); upkeep is a daily cost.
/// Everything without a building argument means the HQ (the one building of the original tower).
/// </summary>
public sealed partial class TowerPlan
{
    readonly List<TowerRoom> rooms = new List<TowerRoom>();
    readonly List<TowerRoom> seatOrder = new List<TowerRoom>();
    readonly List<TowerBuilding> buildings = new List<TowerBuilding>();
    readonly List<TowerConnection> connections = new List<TowerConnection>();
    int nextId = 1;
    int nextBuildingId = 1;
    int nextConnectionId = 1;
    double budget;

    /// <summary>Fires after any change to the rooms, floors, levels, buildings, links or the budget.</summary>
    public event Action Changed;

    /// <summary>Whether a skill is unlocked (null = everything is).</summary>
    public Func<TowerUnlock, bool> UnlockCheck;

    /// <summary>The run's progress for requirements (null = every requirement counts as met).</summary>
    public Func<TowerProgress> ProgressProvider;

    /// <summary>
    /// Build-budget price multiplier by category (PriceFloor, PriceRoom, PriceFurniture, PriceBuilding, PriceLink): the
    /// manager's discounts (ManagerBuildPerks.PriceMultiplier); null = full prices. Kept between 0.5 and 1. Every price the
    /// plan shows and charges goes through PriceOf, so what a card or slip shows is what the purchase takes.
    /// </summary>
    public Func<string, double> PriceMultiplier;

    /// <summary>A charge used a multiplier below 1 (its category): the manager counts a use (ManagerBuildPerks.NotifyPurchase).</summary>
    public Action<string> DiscountUsed;

    /// <summary>Price categories, as ManagerBuildPerks names them (a link is "link": only an "any" perk covers it).</summary>
    public const string PriceFloor = "floor", PriceRoom = "room", PriceFurniture = "furniture", PriceBuilding = "building", PriceLink = "link";

    public TowerPlan(TowerRules rules)
    {
        Rules = rules ?? TowerRules.CreateDefault();
        Rules.Sanitize();
        CreateHeadquarters();
        CreateFixedRooms(true);
    }

    public TowerRules Rules { get; }
    /// <summary>The buildings, left to right (the HQ first).</summary>
    public IReadOnlyList<TowerBuilding> Buildings => buildings;
    public TowerBuilding Headquarters => buildings[0];
    public IReadOnlyList<TowerConnection> Connections => connections;
    /// <summary>The HQ's floors.</summary>
    public int Floors => Headquarters.Floors;
    public double Budget => budget;
    /// <summary>Every room of every building, the built-in ones first.</summary>
    public IReadOnlyList<TowerRoom> Rooms => rooms;
    /// <summary>Goes up on every change.</summary>
    public int Version { get; private set; }
    /// <summary>The HQ's tiles per floor.</summary>
    public int TilesPerFloor => Headquarters.TilesPerFloor;
    public int MaxFloors => Headquarters.MaxFloors;
    public int MaxLevel => Rules.MaxLevel;
    public bool IsTopFloorReached => Headquarters.IsTopFloorReached;
    /// <summary>Price of the HQ's next floor (0 when it can't grow).</summary>
    public double NextFloorPrice => FloorPriceFor(Headquarters);

    /// <summary>Price of <paramref name="building"/>'s next floor (0 when it can't grow), after any discount.</summary>
    public double FloorPriceFor(TowerBuilding building)
    {
        if (building == null || building.IsTopFloorReached) return 0.0;
        return PriceOf(PriceFloor, Rules.FloorPrice(building.Type, building.Floors + 1));
    }

    /// <summary>The room type can be built as far as skills and progress go (limits and spots aside).</summary>
    public bool IsUnlocked(TowerRoomDefinition definition)
    {
        return !LockOf(definition).IsLocked;
    }

    // ------------------------------------------------------------------
    // Budget
    // ------------------------------------------------------------------

    public void SetBudget(double amount)
    {
        amount = Clean(amount);
        if (amount == budget) return;
        budget = amount;
        MarkChanged();
    }

    public void AddBudget(double amount)
    {
        if (amount == 0.0 || double.IsNaN(amount) || double.IsInfinity(amount)) return;
        SetBudget(budget + amount);
    }

    public bool CanAfford(double price)
    {
        return price <= budget + 0.001;
    }

    /// <summary>How much build budget is missing for <paramref name="price"/> (0 when it's affordable).</summary>
    public double Shortfall(double price)
    {
        return Math.Max(0.0, price - budget);
    }

    /// <summary>The price multiplier of a category now (1 = full price, never below 0.5).</summary>
    public double PriceFactor(string category)
    {
        if (PriceMultiplier == null) return 1.0;
        double factor;
        try { factor = PriceMultiplier(category); }
        catch (Exception) { return 1.0; }   // a broken perk source must never block building
        if (double.IsNaN(factor) || double.IsInfinity(factor)) return 1.0;
        return Math.Max(0.5, Math.Min(1.0, factor));
    }

    /// <summary>A category's prices are discounted now.</summary>
    public bool IsDiscounted(string category)
    {
        return PriceFactor(category) < 1.0;
    }

    /// <summary><paramref name="price"/> of <paramref name="category"/> after the multiplier, in whole dollars (0 for none).</summary>
    public double PriceOf(string category, double price)
    {
        if (!(price > 0.0)) return 0.0;
        double factor = PriceFactor(category);
        return factor >= 1.0 ? price : Math.Round(price * factor, MidpointRounding.AwayFromZero);
    }

    /// <summary>What a room of <paramref name="definition"/> costs now (after any discount).</summary>
    public double RoomPrice(TowerRoomDefinition definition)
    {
        return definition == null ? 0.0 : PriceOf(PriceRoom, definition.cost);
    }

    /// <summary>Prices changed from outside (a perk granted, used up or expired): what binds to Version refreshes.</summary>
    public void PricesChanged()
    {
        MarkChanged();
    }

    /// <summary>A purchase of <paramref name="category"/> used a discount (also: a furniture gamble that kept its stake).</summary>
    public void ReportDiscountUsed(string category)
    {
        try { DiscountUsed?.Invoke(category); }
        catch (Exception) { }   // the perk bookkeeping must never undo a purchase
    }

    /// <summary>After a charge (and its MarkChanged): count the discount it used.</summary>
    void Charged(string category)
    {
        if (IsDiscounted(category)) ReportDiscountUsed(category);
    }

    static double Clean(double value)
    {
        return double.IsNaN(value) || double.IsInfinity(value) ? 0.0 : Math.Max(0.0, value);
    }

    // ------------------------------------------------------------------
    // Queries
    // ------------------------------------------------------------------

    public bool Contains(TowerRoom room)
    {
        return room != null && rooms.Contains(room);
    }

    public bool Contains(TowerBuilding building)
    {
        return building != null && buildings.Contains(building);
    }

    public TowerRoom FindRoom(int id)
    {
        foreach (TowerRoom room in rooms)
            if (room.Id == id) return room;
        return null;
    }

    public TowerBuilding FindBuilding(int id)
    {
        foreach (TowerBuilding building in buildings)
            if (building.Id == id) return building;
        return null;
    }

    /// <summary>The building in slot <paramref name="slot"/> (0 = the HQ), or null.</summary>
    public TowerBuilding BuildingAt(int slot)
    {
        return slot >= 0 && slot < buildings.Count ? buildings[slot] : null;
    }

    /// <summary>The room on that tile of the HQ.</summary>
    public TowerRoom RoomAt(int floor, int tile, TowerRoom ignore = null)
    {
        return RoomAt(Headquarters, floor, tile, ignore);
    }

    public TowerRoom RoomAt(TowerBuilding building, int floor, int tile, TowerRoom ignore = null)
    {
        foreach (TowerRoom room in rooms)
            if (room != ignore && room.Covers(building, floor, tile)) return room;
        return null;
    }

    /// <summary>The built-in room with this id (null if the rules have none).</summary>
    public TowerRoom FixedRoom(string id)
    {
        foreach (TowerRoom room in rooms)
            if (room.IsFixed && room.Definition.id == id) return room;
        return null;
    }

    /// <summary>The first office, or null (an older save whose spot was taken, or rules without one).</summary>
    public TowerRoom StarterRoom
    {
        get
        {
            foreach (TowerRoom room in rooms)
                if (room.IsStarter) return room;
            return null;
        }
    }

    /// <summary>Rooms on the HQ's floor <paramref name="floor"/>, left to right, added to <paramref name="into"/>.</summary>
    public void GetRoomsOn(int floor, List<TowerRoom> into)
    {
        GetRoomsOn(Headquarters, floor, into);
    }

    public void GetRoomsOn(TowerBuilding building, int floor, List<TowerRoom> into)
    {
        if (into == null) return;
        int start = into.Count;
        foreach (TowerRoom room in rooms)
            if (room.Building == building && room.Floor == floor) into.Add(room);
        into.Sort(start, into.Count - start, TileOrder.Instance);
    }

    sealed class TileOrder : IComparer<TowerRoom>
    {
        public static readonly TileOrder Instance = new TileOrder();
        public int Compare(TowerRoom a, TowerRoom b)
        {
            int c = a.Building.Slot.CompareTo(b.Building.Slot);
            if (c != 0) return c;
            c = a.Floor.CompareTo(b.Floor);
            return c != 0 ? c : a.Tile.CompareTo(b.Tile);
        }
    }

    public int BuiltRoomCount
    {
        get
        {
            int count = 0;
            foreach (TowerRoom room in rooms)
                if (!room.IsFixed) count++;
            return count;
        }
    }

    /// <summary>Rooms of this type on the campus (or in one building).</summary>
    public int CountRooms(TowerRoomDefinition definition, TowerBuilding building = null, TowerRoom ignore = null)
    {
        if (definition == null) return 0;
        int count = 0;
        foreach (TowerRoom room in rooms)
            if (room != ignore && room.Definition == definition && (building == null || room.Building == building)) count++;
        return count;
    }

    /// <summary>Rooms with this id on the campus.</summary>
    public int CountRooms(string id)
    {
        if (string.IsNullOrEmpty(id)) return 0;
        int count = 0;
        foreach (TowerRoom room in rooms)
            if (room.Definition.id == id) count++;
        return count;
    }

    /// <summary>Floors of every building together.</summary>
    public int TotalFloors
    {
        get
        {
            int total = 0;
            foreach (TowerBuilding building in buildings) total += building.Floors;
            return total;
        }
    }

    // ------------------------------------------------------------------
    // Checks
    // ------------------------------------------------------------------

    /// <summary>Can a room of <paramref name="definition"/> go on the HQ's <paramref name="floor"/> starting at <paramref name="tile"/>?</summary>
    public TowerCheck CheckPlace(TowerRoomDefinition definition, int floor, int tile, bool checkBudget = true, bool checkLock = true)
    {
        return CheckPlace(definition, Headquarters, floor, tile, checkBudget, checkLock);
    }

    /// <summary>
    /// Can a room of <paramref name="definition"/> go on <paramref name="floor"/> of <paramref name="building"/> starting at
    /// <paramref name="tile"/>? In this order: built in, locked, building type, floor rule, limits, the spot, the budget.
    /// <paramref name="checkLock"/> false (loading) also skips the limits.
    /// </summary>
    public TowerCheck CheckPlace(TowerRoomDefinition definition, TowerBuilding building, int floor, int tile, bool checkBudget = true, bool checkLock = true)
    {
        if (definition == null || !Contains(building)) return TowerCheck.Fail(TowerIssue.Missing);
        if (definition.isFixed) return TowerCheck.Fail(TowerIssue.BuiltIn);
        if (checkLock)
        {
            TowerLock lockInfo = LockOf(definition);
            if (lockInfo.IsLocked) return new TowerCheck { issue = TowerIssue.Locked, lockInfo = lockInfo };
        }
        TowerCheck rule = CheckRoomRules(definition, building, floor, null, checkLock);
        if (!rule.Ok) return rule;

        TowerCheck check = CheckSpot(building, definition.Tiles, floor, tile, null);
        if (!check.Ok) return check;

        check.price = RoomPrice(definition);
        if (checkBudget && !CanAfford(check.price))
            check.issue = TowerIssue.CantAfford;
        return check;
    }

    /// <summary>Building type, floor rule and limits of a room at <paramref name="building"/>'s <paramref name="floor"/>.</summary>
    TowerCheck CheckRoomRules(TowerRoomDefinition definition, TowerBuilding building, int floor, TowerRoom moving, bool checkLimits)
    {
        if (!definition.IsAllowedIn(building.Type.id)) return TowerCheck.Fail(TowerIssue.WrongBuilding);
        if (floor >= 1 && floor <= building.Floors)
        {
            bool fits;
            switch (definition.floorRule)
            {
                case TowerFloorRule.GroundOnly: fits = floor == 1; break;
                case TowerFloorRule.TopOnly: fits = floor == building.Floors; break;
                case TowerFloorRule.MinFloor: fits = floor >= definition.minFloor; break;
                default: fits = true; break;
            }
            if (!fits)
                return new TowerCheck { issue = TowerIssue.FloorRule, floorRule = definition.floorRule, floorValue = definition.minFloor };
        }
        if (checkLimits)
        {
            // Moving within its own building never changes the per-building count
            if (definition.maxPerBuilding > 0 && (moving == null || moving.Building != building)
                && CountRooms(definition, building, moving) >= definition.maxPerBuilding)
                return new TowerCheck { issue = TowerIssue.LimitReached, limit = definition.maxPerBuilding, perBuilding = true };
            if (definition.maxPerCompany > 0 && moving == null && CountRooms(definition) >= definition.maxPerCompany)
                return new TowerCheck { issue = TowerIssue.LimitReached, limit = definition.maxPerCompany };
        }
        return new TowerCheck();
    }

    /// <summary>Can <paramref name="room"/> move to the HQ's <paramref name="floor"/> / <paramref name="tile"/>? Moving is free.</summary>
    public TowerCheck CheckMove(TowerRoom room, int floor, int tile)
    {
        return CheckMove(room, room != null ? room.Building : Headquarters, floor, tile);
    }

    public TowerCheck CheckMove(TowerRoom room, TowerBuilding building, int floor, int tile)
    {
        if (!Contains(room) || !Contains(building)) return TowerCheck.Fail(TowerIssue.Missing);
        if (room.IsFixed) return TowerCheck.Fail(TowerIssue.BuiltIn, room);
        TowerCheck rule = CheckRoomRules(room.Definition, building, floor, room, true);
        if (!rule.Ok) return rule;
        return CheckSpot(building, room.Tiles, floor, tile, room);
    }

    TowerCheck CheckSpot(TowerBuilding building, int tiles, int floor, int tile, TowerRoom ignore)
    {
        int width = building.TilesPerFloor;
        if (floor < 1 || floor > building.Floors) return TowerCheck.Fail(TowerIssue.NoSuchFloor);
        if (tile < 0) return TowerCheck.Fail(TowerIssue.OffTheFloor, null, Math.Max(0, tile + tiles));
        if (tile + tiles > width) return TowerCheck.Fail(TowerIssue.OffTheFloor, null, Math.Max(0, width - tile));
        foreach (TowerRoom other in rooms)
        {
            if (other != ignore && other.Overlaps(building, floor, tile, tiles))
                return TowerCheck.Fail(TowerIssue.Overlaps, other);
        }
        return new TowerCheck();
    }

    /// <summary>Can the HQ get another floor?</summary>
    public TowerCheck CheckAddFloor()
    {
        return CheckAddFloor(Headquarters);
    }

    public TowerCheck CheckAddFloor(TowerBuilding building)
    {
        if (!Contains(building)) return TowerCheck.Fail(TowerIssue.Missing);
        if (building.IsTopFloorReached) return new TowerCheck { issue = TowerIssue.TopFloor, limit = building.Type.maxFloors };
        var check = new TowerCheck { price = FloorPriceFor(building) };
        if (!CanAfford(check.price)) check.issue = TowerIssue.CantAfford;
        return check;
    }

    // ------------------------------------------------------------------
    // Changes
    // ------------------------------------------------------------------

    /// <summary>Buy and place a room on the HQ (charges the build budget). Null if it can't go there.</summary>
    public TowerRoom Place(TowerRoomDefinition definition, int floor, int tile, out TowerCheck check)
    {
        return Place(definition, Headquarters, floor, tile, out check);
    }

    /// <summary>Buy and place a room (charges the build budget). Null if it can't go there.</summary>
    public TowerRoom Place(TowerRoomDefinition definition, TowerBuilding building, int floor, int tile, out TowerCheck check)
    {
        check = CheckPlace(definition, building, floor, tile);
        if (!check.Ok) return null;
        budget = Clean(budget - check.price);
        var room = new TowerRoom(nextId++, definition, building, floor, tile, check.price);
        rooms.Add(room);
        MarkChanged();
        Charged(PriceRoom);
        return room;
    }

    /// <summary>Move a room within the HQ or its own building (free; its furniture comes along).</summary>
    public bool Move(TowerRoom room, int floor, int tile, out TowerCheck check)
    {
        return Move(room, room != null ? room.Building : Headquarters, floor, tile, out check);
    }

    /// <summary>Move a room, to another building too (free; its furniture and level come along).</summary>
    public bool Move(TowerRoom room, TowerBuilding building, int floor, int tile, out TowerCheck check)
    {
        check = CheckMove(room, building, floor, tile);
        if (!check.Ok) return false;
        if (room.Building == building && room.Floor == floor && room.Tile == tile) return true;
        room.Building = building;
        room.Floor = floor;
        room.Tile = tile;
        MarkChanged();
        return true;
    }

    /// <summary>What selling gives back into the build budget (0 for the built-in rooms).</summary>
    public double SellValue(TowerRoom room)
    {
        if (room == null || room.IsFixed) return 0.0;
        double paid = room.Paid + (Rules.refundFurnitureOnSell ? room.TotalFurniturePaid + room.LevelPaid : 0.0);
        return Math.Round(Math.Max(0.0, paid) * Rules.sellRefundShare);
    }

    /// <summary>
    /// Can <paramref name="room"/> be sold with <paramref name="employees"/> on the payroll? Built-in rooms can't; a room
    /// with places can't when the rest would hold fewer than the employees (CapacityInUse, limit = the places left).
    /// </summary>
    public TowerCheck CheckSell(TowerRoom room, int employees)
    {
        if (!Contains(room)) return TowerCheck.Fail(TowerIssue.Missing);
        if (room.IsFixed) return TowerCheck.Fail(TowerIssue.BuiltIn, room);
        int places = room.Capacity;
        if (places > 0)
        {
            int left = Capacity - places;
            if (left < Math.Max(0, employees)) return new TowerCheck { issue = TowerIssue.CapacityInUse, limit = Math.Max(0, left), blocker = room };
        }
        return new TowerCheck { price = SellValue(room) };
    }

    /// <summary>Sell a room: SellValue goes into the build budget. Returns the refund. (Ask CheckSell first: this doesn't know the payroll.)</summary>
    public double Sell(TowerRoom room)
    {
        if (!Contains(room) || room.IsFixed) return 0.0;
        double refund = SellValue(room);
        rooms.Remove(room);
        budget = Clean(budget + refund);
        MarkChanged();
        return refund;
    }

    /// <summary>Build the HQ's next floor (charges the build budget).</summary>
    public bool AddFloor(out TowerCheck check)
    {
        return AddFloor(Headquarters, out check);
    }

    /// <summary>
    /// Build <paramref name="building"/>'s next floor on top (charges the build budget). Rooms that must stay on the top floor
    /// (TowerFloorRule.TopOnly: the rooftop lounge, the helipad) ride up to the new top floor.
    /// </summary>
    public bool AddFloor(TowerBuilding building, out TowerCheck check)
    {
        check = CheckAddFloor(building);
        if (!check.Ok) return false;
        budget = Clean(budget - check.price);
        building.Floors++;
        building.FloorsPaid += check.price;
        RaiseRoofRooms(building);
        MarkChanged();
        Charged(PriceFloor);
        return true;
    }

    void RaiseRoofRooms(TowerBuilding building)
    {
        int oldTop = building.Floors - 1;
        foreach (TowerRoom room in rooms)
            if (room.Building == building && room.Floor == oldTop && room.Definition.floorRule == TowerFloorRule.TopOnly)
                room.Floor = building.Floors;
    }

    /// <summary>Add an HQ floor without charging (converting old saves). False at the top floor.</summary>
    public bool AddFreeFloor()
    {
        return AddFreeFloor(Headquarters);
    }

    public bool AddFreeFloor(TowerBuilding building)
    {
        if (!Contains(building) || building.IsTopFloorReached) return false;
        building.Floors++;
        MarkChanged();
        return true;
    }

    /// <summary>Set the HQ's floor count (loading). Never below the floors the rooms stand on.</summary>
    public void SetFloors(int count)
    {
        SetFloors(Headquarters, count);
    }

    public void SetFloors(TowerBuilding building, int count)
    {
        if (!Contains(building)) return;
        int lowest = 1;
        foreach (TowerRoom room in rooms)
            if (room.Building == building) lowest = Math.Max(lowest, room.Floor);
        foreach (TowerConnection link in connections)
            if (link.Left == building || link.Right == building) lowest = Math.Max(lowest, link.Floor);
        count = Math.Max(lowest, Math.Min(Math.Max(1, building.Type.maxFloors), count));
        if (count == building.Floors) return;
        building.Floors = count;
        MarkChanged();
    }

    /// <summary>Put a room back on the HQ without charging (loading). Null if it doesn't fit there. Paid of 0 or less = its price.</summary>
    public TowerRoom Restore(TowerRoomDefinition definition, int floor, int tile, double paid, int[] levels, double[] levelsPaid)
    {
        return Restore(definition, Headquarters, floor, tile, paid, levels, levelsPaid, 1, 0.0);
    }

    /// <summary>
    /// Put a room back without charging (loading): no lock or limit checks, but it must be allowed there, follow its floor
    /// rule and fit. Null if it can't. Paid of 0 or less = its price. <paramref name="level"/> is clamped to its levels.
    /// </summary>
    public TowerRoom Restore(TowerRoomDefinition definition, TowerBuilding building, int floor, int tile, double paid, int[] levels, double[] levelsPaid,
                             int level, double levelPaid)
    {
        TowerCheck check = CheckPlace(definition, building, floor, tile, false, false);
        if (!check.Ok) return null;
        if (double.IsNaN(paid) || double.IsInfinity(paid) || paid <= 0.0) paid = Math.Max(0.0, definition.cost);
        var room = new TowerRoom(nextId++, definition, building, floor, tile, paid);
        rooms.Add(room);
        CopyLevels(room, levels, levelsPaid);
        room.Level = Math.Max(1, Math.Min(definition.MaxLevel, level));
        room.LevelPaid = Clean(levelPaid);
        MarkChanged();
        return room;
    }

    /// <summary>Set a room's furniture levels and what was paid for them (loading).</summary>
    public void RestoreLevels(TowerRoom room, int[] levels, double[] levelsPaid)
    {
        if (!Contains(room)) return;
        CopyLevels(room, levels, levelsPaid);
        MarkChanged();
    }

    /// <summary>Set a room's level and what its levels cost (loading; the built-in rooms too).</summary>
    public void RestoreRoomLevel(TowerRoom room, int level, double levelPaid)
    {
        if (!Contains(room)) return;
        room.Level = Math.Max(1, Math.Min(room.Definition.MaxLevel, level));
        room.LevelPaid = Clean(levelPaid);
        MarkChanged();
    }

    void CopyLevels(TowerRoom room, int[] levels, double[] levelsPaid)
    {
        for (int i = 0; i < room.SlotCount; i++)
        {
            room.levels[i] = levels != null && i < levels.Length ? Math.Max(0, Math.Min(MaxLevel, levels[i])) : 0;
            double paid = levelsPaid != null && i < levelsPaid.Length ? levelsPaid[i] : 0.0;
            room.furniturePaid[i] = double.IsNaN(paid) || double.IsInfinity(paid) ? 0.0 : Math.Max(0.0, paid);
        }
    }

    /// <summary>Back to day one: the HQ's floor 1 with the built-in rooms (and the first office), no furniture, an empty budget.</summary>
    public void Reset()
    {
        Reset(true);
    }

    /// <summary>Back to day one. <paramref name="withStarter"/> false leaves the first office out (loading decides about it).</summary>
    public void Reset(bool withStarter)
    {
        rooms.Clear();
        connections.Clear();
        buildings.Clear();
        budget = 0.0;
        CreateHeadquarters();
        CreateFixedRooms(withStarter);
        MarkChanged();
    }

    void CreateHeadquarters()
    {
        TowerBuildingType hq = Rules.Headquarters;
        var building = new TowerBuilding(nextBuildingId++, hq, 1, 0.0) { Slot = 0, Number = 1 };
        buildings.Add(building);
    }

    void CreateFixedRooms(bool withStarter)
    {
        TowerBuilding hq = Headquarters;
        foreach (TowerRoomDefinition definition in Rules.rooms)
        {
            if (definition == null || !definition.isFixed) continue;
            if (definition.isStarter && !withStarter) continue;
            hq.Floors = Math.Min(Math.Max(hq.Floors, definition.fixedFloor), Math.Max(1, hq.Type.maxFloors));
            if (definition.fixedFloor > hq.Floors) continue;
            if (!CheckSpot(hq, definition.Tiles, definition.fixedFloor, definition.fixedTile, null).Ok) continue;
            rooms.Add(new TowerRoom(nextId++, definition, hq, definition.fixedFloor, definition.fixedTile, 0.0));
        }
    }

    /// <summary>
    /// Put the first office in its spot on the HQ if the rules have one, the campus doesn't, and the spot is free (an older
    /// save gets it only then). Returns the room, or null.
    /// </summary>
    public TowerRoom PlaceStarter()
    {
        TowerRoomDefinition definition = Rules.Starter;
        if (definition == null || StarterRoom != null) return null;
        TowerBuilding hq = Headquarters;
        if (definition.fixedFloor > hq.Floors) return null;
        if (!CheckSpot(hq, definition.Tiles, definition.fixedFloor, definition.fixedTile, null).Ok) return null;
        var room = new TowerRoom(nextId++, definition, hq, definition.fixedFloor, definition.fixedTile, 0.0);
        rooms.Add(room);
        MarkChanged();
        return room;
    }

    /// <summary>The lowest, leftmost free spot for a room on the HQ (ignores the budget, locks and limits). False if it's full.</summary>
    public bool TryFindSpot(TowerRoomDefinition definition, out int floor, out int tile)
    {
        return TryFindSpot(definition, Headquarters, out floor, out tile);
    }

    /// <summary>The lowest, leftmost free spot in one building where the room is allowed (ignores the budget, locks and limits).</summary>
    public bool TryFindSpot(TowerRoomDefinition definition, TowerBuilding building, out int floor, out int tile)
    {
        floor = 0;
        tile = 0;
        if (definition == null || !Contains(building)) return false;
        int tiles = definition.Tiles;
        for (int f = 1; f <= building.Floors; f++)
        {
            if (!CheckRoomRules(definition, building, f, null, false).Ok) continue;
            for (int t = 0; t + tiles <= building.TilesPerFloor; t++)
            {
                if (!CheckSpot(building, tiles, f, t, null).Ok) continue;
                floor = f;
                tile = t;
                return true;
            }
        }
        return false;
    }

    /// <summary>The first free spot anywhere on the campus, the HQ first (loading: a room that no longer fits where it was).</summary>
    public bool TryFindSpotAnywhere(TowerRoomDefinition definition, out TowerBuilding building, out int floor, out int tile)
    {
        foreach (TowerBuilding candidate in buildings)
        {
            if (TryFindSpot(definition, candidate, out floor, out tile))
            {
                building = candidate;
                return true;
            }
        }
        building = null;
        floor = tile = 0;
        return false;
    }

    // ------------------------------------------------------------------
    // Furniture
    // ------------------------------------------------------------------

    /// <summary>Price of the slot's next level (0 at the top level), after any discount.</summary>
    public double UpgradePrice(TowerRoom room, int slot)
    {
        if (room == null || slot < 0 || slot >= room.SlotCount) return 0.0;
        int level = room.GetLevel(slot);
        return level >= MaxLevel ? 0.0 : LevelPrice(level + 1);
    }

    /// <summary>What furniture level <paramref name="level"/> costs now (after any discount).</summary>
    public double LevelPrice(int level)
    {
        return PriceOf(PriceFurniture, Rules.LevelPrice(level));
    }

    public TowerCheck CheckUpgrade(TowerRoom room, int slot, bool checkBudget = true)
    {
        if (!Contains(room) || slot < 0 || slot >= room.SlotCount) return TowerCheck.Fail(TowerIssue.Missing);
        if (room.GetLevel(slot) >= MaxLevel) return TowerCheck.Fail(TowerIssue.MaxLevel);
        var check = new TowerCheck { price = UpgradePrice(room, slot) };
        if (checkBudget && !CanAfford(check.price)) check.issue = TowerIssue.CantAfford;
        return check;
    }

    /// <summary>Pay for the next level (+1).</summary>
    public bool BuyLevel(TowerRoom room, int slot, out TowerCheck check)
    {
        check = CheckUpgrade(room, slot);
        if (!check.Ok) return false;
        budget = Clean(budget - check.price);
        room.levels[slot]++;
        room.furniturePaid[slot] += check.price;
        MarkChanged();
        Charged(PriceFurniture);
        return true;
    }

    /// <summary>
    /// Take the next level's price for a gamble (settle it with ChangeLevel or a refund). A discount isn't reported here:
    /// TowerBuilder reports it once the gamble keeps the stake (a push or a failed start gives it back).
    /// </summary>
    public bool TakeStake(TowerRoom room, int slot, out double stake)
    {
        stake = 0.0;
        TowerCheck check = CheckUpgrade(room, slot);
        if (!check.Ok) return false;
        stake = check.price;
        budget = Clean(budget - stake);
        MarkChanged();
        return true;
    }

    /// <summary>
    /// Change a slot's level by <paramref name="delta"/>, kept between 0 and the top level. <paramref name="paid"/> is added to
    /// what the slot's levels cost when it went up. Returns the change that happened.
    /// </summary>
    public int ChangeLevel(TowerRoom room, int slot, int delta, double paid)
    {
        if (!Contains(room) || slot < 0 || slot >= room.SlotCount) return 0;
        int before = room.levels[slot];
        int after = Math.Max(0, Math.Min(MaxLevel, before + delta));
        room.levels[slot] = after;
        if (after > before && paid > 0.0 && !double.IsNaN(paid) && !double.IsInfinity(paid))
            room.furniturePaid[slot] += paid;
        if (after != before) MarkChanged();
        return after - before;
    }

    // ------------------------------------------------------------------
    // Effects of one room (furniture scales income, output, happiness and trust; the room's level scales everything)
    // ------------------------------------------------------------------

    /// <summary>How much the room's bonus is scaled by its furniture (1 + 25% per level).</summary>
    public float BonusMultiplier(TowerRoom room)
    {
        return room == null ? 1f : Rules.BonusMultiplier(room.TotalLevels);
    }

    /// <summary>How much the room's effects are scaled by its level (x1 at level 1).</summary>
    public float LevelMultiplier(TowerRoom room)
    {
        return room == null ? 1f : room.Definition.MultiplierAt(room.Level);
    }

    public double FurnitureIncome(TowerRoom room)
    {
        return room == null ? 0.0 : room.TotalLevels * Math.Max(0.0, Rules.incomePerLevel);
    }

    /// <summary>The room's own daily income (scaled by its furniture and level), without the furniture's income.</summary>
    public double RoomBonusIncome(TowerRoom room)
    {
        return room == null ? 0.0 : room.Definition.dailyIncome * BonusMultiplier(room) * LevelMultiplier(room);
    }

    /// <summary>Everything the room earns a day: its bonus income plus its furniture.</summary>
    public double RoomDailyIncome(TowerRoom room)
    {
        return RoomBonusIncome(room) + FurnitureIncome(room);
    }

    /// <summary>The room's happiness bonus in "percent" (5 = +5%), scaled by its furniture and level.</summary>
    public float HappinessPercent(TowerRoom room)
    {
        return room == null ? 0f : room.Definition.happinessPercent * BonusMultiplier(room) * LevelMultiplier(room);
    }

    public float TrustPercent(TowerRoom room)
    {
        return room == null ? 0f : room.Definition.trustPercent * BonusMultiplier(room) * LevelMultiplier(room);
    }

    public float ProductivityPercent(TowerRoom room)
    {
        return room == null ? 0f : room.Definition.productivityPercent * BonusMultiplier(room) * LevelMultiplier(room);
    }

    /// <summary>Skill points a day the room earns (scaled by its level only; the campus total is capped).</summary>
    public float KnowledgePerDay(TowerRoom room)
    {
        return room == null ? 0f : Math.Max(0f, room.Definition.knowledgePerDay) * LevelMultiplier(room);
    }

    /// <summary>Percent of salaries the room saves (3 = 3%; scaled by its level only; the campus total is capped).</summary>
    public float SalarySavingsPercent(TowerRoom room)
    {
        return room == null ? 0f : Math.Max(0f, room.Definition.salarySavingsPercent) * LevelMultiplier(room);
    }

    /// <summary>Percent of the upkeep the room saves (scaled by its level only; the campus total is capped).</summary>
    public float UpkeepSavingsPercent(TowerRoom room)
    {
        return room == null ? 0f : Math.Max(0f, room.Definition.upkeepSavingsPercent) * LevelMultiplier(room);
    }

    // ------------------------------------------------------------------
    // Capacity (old name: seats)
    // ------------------------------------------------------------------

    /// <summary>Employees the campus holds: the hard limit on hiring.</summary>
    public int Capacity
    {
        get
        {
            int total = 0;
            foreach (TowerRoom room in rooms) total += room.Capacity;
            return total;
        }
    }

    /// <summary>Old name of Capacity (seats were a soft cap before 2026-09-29).</summary>
    public int TotalSeats => Capacity;

    /// <summary>Employees over capacity (only an older save can be: hiring stops at capacity).</summary>
    public int Unseated(int employees)
    {
        return Math.Max(0, employees - Capacity);
    }

    /// <summary>Happiness a day that employees over capacity cost (positive number, capped; 0 by default: they're grandfathered).</summary>
    public float SeatPenalty(int employees)
    {
        int unseated = Unseated(employees);
        if (unseated <= 0) return 0f;
        return Math.Min(Math.Max(0f, Rules.maxSeatPenaltyPerDay), unseated * Math.Max(0f, Rules.unseatedHappinessPerDay));
    }

    /// <summary>
    /// Who sits where: the Lobby's receptionist first (the first hire runs reception), then the rooms with places from the
    /// HQ's bottom floor up, left to right, then the next building. Adds (room, people seated) for every room with places.
    /// </summary>
    public void CountSeated(int employees, Dictionary<TowerRoom, int> into)
    {
        if (into == null) return;
        into.Clear();
        seatOrder.Clear();
        foreach (TowerRoom room in rooms)
            if (room.Capacity > 0) seatOrder.Add(room);
        seatOrder.Sort(SeatOrder);
        int left = Math.Max(0, employees);
        foreach (TowerRoom room in seatOrder)
        {
            int seated = Math.Min(left, room.Capacity);
            into[room] = seated;
            left -= seated;
        }
        seatOrder.Clear();
    }

    static readonly Comparison<TowerRoom> SeatOrder = SeatComparison;

    static int SeatComparison(TowerRoom a, TowerRoom b)
    {
        bool lobbyA = a.Definition.role == TowerRoomRole.Lobby, lobbyB = b.Definition.role == TowerRoomRole.Lobby;
        if (lobbyA != lobbyB) return lobbyA ? -1 : 1;
        int c = a.Building.Slot.CompareTo(b.Building.Slot);
        if (c != 0) return c;
        c = a.Floor.CompareTo(b.Floor);
        return c != 0 ? c : a.Tile.CompareTo(b.Tile);
    }

    void MarkChanged()
    {
        Version++;
        Changed?.Invoke();
    }
}
