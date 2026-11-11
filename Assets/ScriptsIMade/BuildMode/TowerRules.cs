using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>What a room is for (drives its drawing, its figures and its icon). New values are appended: catalog assets store the number.</summary>
public enum TowerRoomRole
{
    Office,
    Lobby,
    CeoOffice,
    BreakRoom,
    MeetingRoom,
    ServerRoom,
    TradingFloor,
    CasinoRoom,
    // The campus (2026-09-29)
    StarterOffice,
    OpenPlan,
    CubicleFarm,
    Cafeteria,
    Gym,
    ExecutiveSuite,
    BigMeetingRoom,
    Marketing,
    HrDepartment,
    LegalDepartment,
    Accounting,
    ResearchLab,
    TrainingCenter,
    DataCenter,
    RooftopLounge,
    Helipad,
    TrophyHall
}

/// <summary>A skill a room (or a building type) needs before it can be built.</summary>
public enum TowerUnlock
{
    None,
    StockMarket,     // SkillEffect.UnlockStockMarket (Stock Market Access)
    CasinoLicense    // SkillEffect.UnlockCasino (Casino License)
}

/// <summary>How a furniture slot is drawn (in the tower, in the room view and as its icon). New values are appended.</summary>
public enum TowerFurnitureArt
{
    Desk,
    Plant,
    CoffeeMachine,
    Sofa,
    Bookshelf,
    Aquarium,
    ReceptionDesk,
    ArcadeCabinet,
    BigTable,
    Whiteboard,
    Projector,
    ServerRack,
    CoolingUnit,
    Terminal,
    TickerBoard,
    SlotMachine,
    CardTable,
    RouletteWheel,
    Bar,
    // The campus (2026-09-29)
    Treadmill,
    Weights,
    FoodCounter,
    FilingCabinet,
    LabBench,
    Cubicle,
    Trophy,
    Helicopter,
    Parasol
}

/// <summary>The catalog's groups of rooms.</summary>
public enum TowerRoomCategory
{
    BuiltIn,
    Office,
    Facility,
    Executive,
    Department,
    Research,
    Utility,
    Prestige,
    Money
}

/// <summary>Which floors of a building a room may stand on.</summary>
public enum TowerFloorRule
{
    Any,
    GroundOnly,   // floor 1
    TopOnly,      // the building's top floor (it rides up when a floor is added)
    MinFloor      // minFloor and up
}

/// <summary>How a building type looks on the blueprint (its roof, walls and plate).</summary>
public enum TowerFacade
{
    Hq,          // the original tower: lift machine room, railing, the HQ flag
    Annex,       // low and wide: flat roof with air-conditioning units
    Tower,       // an antenna mast on the roof
    Skyscraper   // a spire and setbacks
}

/// <summary>What joins two buildings. Only skybridges are built today; the rest is the hook for later links.</summary>
public enum TowerConnectionKind
{
    Skybridge,   // between neighbouring buildings, at a floor both have (not the street)
    Tunnel       // (future) under the street
}

/// <summary>
/// Progress a room, building type or connection needs besides a skill (0 / empty = not needed). Checked against the run:
/// the highest company value reached, the status tier (StatusLadder: highest reached), the day, the HQ's floors, the
/// buildings owned and a room that must already stand somewhere on the campus.
/// </summary>
[Serializable]
public class TowerRequirements
{
    [Tooltip("Highest company value reached (a gambling loss doesn't lock it again)")]
    public double minPeakValue;
    [Tooltip("Status tier (0 Start-up, 1 Company $1M, 2 Corporation $10M, 3 Executive $50M, 4 Tycoon $100M, 5 Mogul $500M, 6 Billionaire $1B)")]
    [Min(0)]
    public int minStatusTier;
    [Min(0)]
    public int minDay;
    [Tooltip("Floors the HQ needs")]
    [Min(0)]
    public int minHqFloors;
    [Tooltip("Buildings owned (the HQ counts)")]
    [Min(0)]
    public int minBuildings;
    [Tooltip("Id of a room that must already be built somewhere on the campus")]
    public string requiresRoom = "";

    public bool IsEmpty => minPeakValue <= 0.0 && minStatusTier <= 0 && minDay <= 0 && minHqFloors <= 0 && minBuildings <= 0
                           && string.IsNullOrEmpty(requiresRoom);

    public TowerRequirements() { }

    public TowerRequirements(double peakValue = 0.0, int statusTier = 0, int day = 0, int hqFloors = 0, int buildings = 0, string room = "")
    {
        minPeakValue = peakValue;
        minStatusTier = statusTier;
        minDay = day;
        minHqFloors = hqFloors;
        minBuildings = buildings;
        requiresRoom = room ?? "";
    }
}

/// <summary>
/// One upgrade level of a room beyond level 1 (level 1 is the room's own price, capacity and upkeep). The numbers are the
/// level's totals, not additions: capacity 13 = the room holds 13 employees at this level.
/// </summary>
[Serializable]
public class TowerRoomLevel
{
    [Tooltip("Price of reaching this level (build budget)")]
    public double cost;
    [Tooltip("Employees the room holds at this level")]
    [Min(0)]
    public int capacity;
    [Tooltip("x the room's effects at this level (income, output, happiness, trust, knowledge, savings); level 1 is x1")]
    public float effectMultiplier = 1f;
    [Tooltip("Company money a day at this level")]
    public double upkeep;
    [Tooltip("How much better it is drawn: 0 plain, 1 trimmed, 2 premium, 3 luxury")]
    [Range(0, 3)]
    public int look;

    public TowerRoomLevel() { }

    public TowerRoomLevel(double cost, int capacity, float effectMultiplier, double upkeep, int look)
    {
        this.cost = cost;
        this.capacity = capacity;
        this.effectMultiplier = effectMultiplier;
        this.upkeep = upkeep;
        this.look = look;
    }
}

/// <summary>One piece of furniture a room has. Every slot has a level from Basic (0) to Ultimate (5).</summary>
[Serializable]
public class TowerFurnitureSlot
{
    public string name = "Desk";
    public TowerFurnitureArt art = TowerFurnitureArt.Desk;
    [Tooltip("Left-to-right position in the room view (lower = further left); ties keep the list order")]
    public int viewOrder;

    public TowerFurnitureSlot() { }

    public TowerFurnitureSlot(string name, TowerFurnitureArt art, int viewOrder = 0)
    {
        this.name = name;
        this.art = art;
        this.viewOrder = viewOrder;
    }
}

/// <summary>A room of the campus: its size in tiles, price, capacity, upkeep, rules, effects, levels and furniture slots.</summary>
[Serializable]
public class TowerRoomDefinition
{
    [Tooltip("Stable id stored in save files. Never change it once shipped.")]
    public string id = "room";
    public string displayName = "Room";
    public TowerRoomRole role = TowerRoomRole.Office;
    [Tooltip("Width in tiles (an HQ floor has 8)")]
    [Min(1)]
    public int tiles = 2;
    [Tooltip("Price from the build budget")]
    public double cost = 5000;

    [Header("Built in (the Lobby, the CEO office and the first office)")]
    [Tooltip("Always there: can't be built, moved or sold")]
    public bool isFixed;
    [Min(1)]
    public int fixedFloor = 1;
    [Tooltip("First tile, counting from 0 at the lift")]
    [Min(0)]
    public int fixedTile;
    [Tooltip("The first office: a new company gets it (an older save only when its spot is free). It can be upgraded, never moved or sold. Needs isFixed.")]
    public bool isStarter;

    [Header("Effects (room bonuses grow with the furniture levels in the room and with the room's level)")]
    [Tooltip("Old name of capacity (catalogs from before 2026-09-29, when seats were a soft cap): read into capacity while capacity is 0")]
    [HideInInspector]
    [Min(0)]
    public int seats;
    [Tooltip("Added to the company value every day")]
    public double dailyIncome;
    [Tooltip("+3 = +3% employee output (counts toward the company's +150% cap)")]
    public float productivityPercent;
    [Tooltip("+5 = \"+5% happiness\": happiness settles about 5 points higher (see TowerRules.meterPointsPerPercent)")]
    public float happinessPercent;
    [Tooltip("+5 = \"+5% investor trust\", like happiness")]
    public float trustPercent;

    [Header("Unlock")]
    public TowerUnlock unlock = TowerUnlock.None;
    [Tooltip("Shown on the locked catalog card for the skill (empty = made from the requirements)")]
    public string lockReason = "";

    [Header("Text")]
    [Tooltip("What the catalog card says it does (empty = made from the numbers)")]
    public string effectText = "";
    [TextArea(2, 4)]
    public string description = "";

    [Header("Look")]
    [Tooltip("Paper colour of the room")]
    public Color paper = Color.white;
    [Tooltip("Colour of its label and figures")]
    public Color ink = TowerRules.Hex(0x1D2433);

    [Header("Furniture")]
    public List<TowerFurnitureSlot> slots = new List<TowerFurnitureSlot>();

    // ------------------------------------------------------------------
    // The campus (2026-09-29): capacity, upkeep, rules, more effects, levels
    // ------------------------------------------------------------------

    [Header("Capacity and upkeep")]
    [Tooltip("Employees the room holds at level 1. Capacity is a HARD limit on hiring (OfficeCapacity).")]
    [Min(0)]
    public int capacity;
    [Tooltip("Company money a day at level 1 (the daily upkeep, DailyEconomyHooks \"upkeep\")")]
    public double upkeep;
    public TowerRoomCategory category = TowerRoomCategory.Office;

    [Header("Where and how many")]
    [Tooltip("At most this many in one building (0 = no limit)")]
    [Min(0)]
    public int maxPerBuilding;
    [Tooltip("At most this many on the campus (0 = no limit)")]
    [Min(0)]
    public int maxPerCompany;
    [Tooltip("Building type ids it may stand in (empty = every building)")]
    public List<string> allowedBuildings = new List<string>();
    public TowerFloorRule floorRule = TowerFloorRule.Any;
    [Tooltip("Lowest floor for TowerFloorRule.MinFloor")]
    [Min(1)]
    public int minFloor = 1;

    [Header("More unlock rules (with the skill above)")]
    public TowerRequirements requires = new TowerRequirements();

    [Header("More effects")]
    [Tooltip("Skill-point progress a day (0.05 = a skill point every 20 days), through SkillTreeManager.AddKnowledge; capped company-wide")]
    public float knowledgePerDay;
    [Tooltip("-3 = salaries cost 3% less (capped company-wide)")]
    public float salarySavingsPercent;
    [Tooltip("-8 = the campus's upkeep costs 8% less (capped company-wide)")]
    public float upkeepSavingsPercent;

    [Header("Room levels (upgrades beyond level 1; empty = no upgrades)")]
    public List<TowerRoomLevel> levels = new List<TowerRoomLevel>();

    public int SlotCount => slots != null ? slots.Count : 0;
    public int Tiles => Math.Max(1, tiles);
    public bool IsOffice => role == TowerRoomRole.Office || role == TowerRoomRole.StarterOffice || role == TowerRoomRole.OpenPlan
                            || role == TowerRoomRole.CubicleFarm;
    /// <summary>Money, output, happiness, trust, knowledge or savings that furniture levels and room levels scale.</summary>
    public bool HasBonus => dailyIncome != 0.0 || productivityPercent != 0f || happinessPercent != 0f || trustPercent != 0f
                            || knowledgePerDay != 0f || salarySavingsPercent != 0f || upkeepSavingsPercent != 0f;
    /// <summary>Highest room level (1 without upgrades).</summary>
    public int MaxLevel => 1 + (levels != null ? levels.Count : 0);
    public bool HasLevels => levels != null && levels.Count > 0;

    public TowerFurnitureSlot GetSlot(int slot)
    {
        return slots != null && slot >= 0 && slot < slots.Count ? slots[slot] : null;
    }

    TowerRoomLevel LevelData(int level)
    {
        if (level <= 1 || levels == null || levels.Count == 0) return null;
        return levels[Math.Min(levels.Count, level - 1) - 1];
    }

    /// <summary>Employees it holds at <paramref name="level"/> (1-based).</summary>
    public int CapacityAt(int level)
    {
        TowerRoomLevel data = LevelData(level);
        return Math.Max(0, data != null ? data.capacity : capacity);
    }

    /// <summary>Company money a day at <paramref name="level"/>.</summary>
    public double UpkeepAt(int level)
    {
        TowerRoomLevel data = LevelData(level);
        return Math.Max(0.0, data != null ? data.upkeep : upkeep);
    }

    /// <summary>x the room's effects at <paramref name="level"/> (1 at level 1).</summary>
    public float MultiplierAt(int level)
    {
        TowerRoomLevel data = LevelData(level);
        return data != null ? Math.Max(0f, data.effectMultiplier) : 1f;
    }

    /// <summary>How nice it is drawn at <paramref name="level"/> (0-3).</summary>
    public int LookAt(int level)
    {
        TowerRoomLevel data = LevelData(level);
        return data != null ? Math.Max(0, Math.Min(3, data.look)) : 0;
    }

    /// <summary>Price of upgrading to <paramref name="level"/> (0 for level 1 or past the top).</summary>
    public double UpgradeCost(int level)
    {
        if (level <= 1 || level > MaxLevel) return 0.0;
        TowerRoomLevel data = LevelData(level);
        return data != null ? Math.Max(0.0, data.cost) : 0.0;
    }

    /// <summary>It may stand in a building of type <paramref name="buildingTypeId"/>.</summary>
    public bool IsAllowedIn(string buildingTypeId)
    {
        if (allowedBuildings == null || allowedBuildings.Count == 0) return true;
        foreach (string allowed in allowedBuildings)
            if (allowed == buildingTypeId) return true;
        return false;
    }
}

/// <summary>A kind of building the company can own on its campus (the HQ is the first and only one of its kind).</summary>
[Serializable]
public class TowerBuildingType
{
    [Tooltip("Stable id stored in save files. Never change it once shipped.")]
    public string id = "tower";
    public string displayName = "Office tower";
    [TextArea(2, 4)]
    public string description = "";
    public TowerFacade facade = TowerFacade.Tower;
    [Tooltip("A faint wash behind the building on the blueprint (alpha is used)")]
    public Color tint = new Color(1f, 1f, 1f, 0.05f);
    [Tooltip("Width of every floor in tiles (a tile is 104 px)")]
    [Min(2)]
    public int tiles = 10;
    [Min(1)]
    public int maxFloors = 30;
    [Tooltip("Buying it (floor 1 included), from the build budget")]
    public double price = 2500000;
    [Tooltip("Price of its floor 2; each floor costs floorPriceGrowth times the one below")]
    public double secondFloorPrice = 200000;
    public double floorPriceGrowth = 1.3;
    [Tooltip("Company money a day for each of its floors")]
    public double upkeepPerFloor = 200;
    [Tooltip("At most this many on the campus (0 = no limit)")]
    [Min(0)]
    public int maxCount = 3;
    [Tooltip("The company's first building: it comes with the company and can't be bought or sold")]
    public bool isHeadquarters;
    public TowerUnlock unlock = TowerUnlock.None;
    public TowerRequirements requires = new TowerRequirements();
    [Tooltip("Shown on the locked card for the skill (empty = made from the requirements)")]
    public string lockReason = "";
}

/// <summary>A kind of link between two buildings (skybridges today; tunnels and the like later).</summary>
[Serializable]
public class TowerConnectionType
{
    [Tooltip("Stable id stored in save files. Never change it once shipped.")]
    public string id = "skybridge";
    public string displayName = "Skybridge";
    [TextArea(2, 4)]
    public string description = "";
    public TowerConnectionKind kind = TowerConnectionKind.Skybridge;
    [Tooltip("Price from the build budget")]
    public double price = 150000;
    [Tooltip("Plus this much for every floor above the street")]
    public double pricePerFloor = 20000;
    [Tooltip("Lowest floor it may be built at (skybridges: 2, never the street)")]
    [Min(1)]
    public int minFloor = 2;
    [Tooltip("At most this many between the same two buildings")]
    [Min(1)]
    public int maxPerPair = 2;
    [Tooltip("Company money a day")]
    public double upkeep = 200;
    public float productivityPercent = 2f;
    public float happinessPercent = 1f;
    public float trustPercent;
    public TowerUnlock unlock = TowerUnlock.None;
    public TowerRequirements requires = new TowerRequirements();
    public string lockReason = "";

    /// <summary>Price of one at <paramref name="floor"/>.</summary>
    public double PriceAt(int floor)
    {
        return Math.Max(0.0, price) + Math.Max(0.0, pricePerFloor) * Math.Max(0, floor - 1);
    }
}

/// <summary>
/// A kind of floor (the hook for later floor types such as a sky lobby or a mechanical floor). Every floor is "standard"
/// today: floor prices are the building's curve times priceMultiplier.
/// </summary>
[Serializable]
public class TowerFloorType
{
    public string id = "standard";
    public string displayName = "Floor";
    public double priceMultiplier = 1.0;
}

/// <summary>One outcome of the furniture "random chance" roll: level change and its chance (chances are normalised).</summary>
[Serializable]
public class TowerRollOutcome
{
    public int levels;
    [Range(0f, 1f)]
    public float chance;

    public TowerRollOutcome() { }

    public TowerRollOutcome(int levels, float chance)
    {
        this.levels = levels;
        this.chance = chance;
    }
}

/// <summary>
/// Every number of the HQ campus (TowerCatalog holds one; without a catalog TowerRules.CreateDefault() is used): rooms,
/// building types, connections, floor prices, room levels, furniture levels and prices, the furniture gambles, upkeep
/// and selling. Plain data without Unity calls, so the rules can be tested outside Unity. The defaults are in
/// TowerRulesDefaults.cs.
/// </summary>
[Serializable]
public partial class TowerRules
{
    /// <summary>Id of the HQ's building type (the first building, always there).</summary>
    public const string HqTypeId = "hq";
    /// <summary>Id of the floor type every floor has today.</summary>
    public const string StandardFloorId = "standard";

    [Tooltip("Every room. Ids are stored in save files: never change one once shipped.")]
    public List<TowerRoomDefinition> rooms = new List<TowerRoomDefinition>();

    [Header("Floors of the HQ (the HQ's building type uses these when it has none of its own)")]
    [Min(1)]
    public int tilesPerFloor = 8;
    [Tooltip("The HQ can't grow beyond this")]
    [Min(1)]
    public int maxFloors = 36;
    [Tooltip("Price of floor 2 (floor 1 comes with the company)")]
    public double secondFloorPrice = 10000;
    [Tooltip("Each floor costs this many times the one below it")]
    public double floorPriceGrowth = 1.42;
    [Tooltip("Floor prices are rounded to this many significant digits (2: $40,960 -> $41,000)")]
    [Range(1, 6)]
    public int floorPriceDigits = 2;

    [Header("Campus")]
    [Tooltip("Building types. The first with isHeadquarters is the HQ. Ids are stored in save files.")]
    public List<TowerBuildingType> buildingTypes = new List<TowerBuildingType>();
    [Tooltip("Connection types (skybridges). Ids are stored in save files.")]
    public List<TowerConnectionType> connectionTypes = new List<TowerConnectionType>();
    [Tooltip("Floor types (the hook for later kinds of floor; every floor is \"standard\" today)")]
    public List<TowerFloorType> floorTypes = new List<TowerFloorType>();

    [Header("Furniture")]
    [Tooltip("Level names from level 0 (the length sets the top level)")]
    public List<string> levelNames = new List<string>();
    [Tooltip("Reaching level L costs base x growth^L")]
    public double furnitureBasePrice = 1000;
    public double furniturePriceGrowth = 2.66;
    [Tooltip("Daily income of each furniture level")]
    public double incomePerLevel = 100;
    [Tooltip("Each furniture level in a room adds this share of the room's base bonus (0.25 = +25%)")]
    public float bonusPerLevel = 0.25f;

    [Header("Furniture gambles (they cost the same as paying)")]
    [Tooltip("Levels a won blackjack hand gives (capped at the top level). A push refunds, a loss gives nothing.")]
    [Min(1)]
    public int blackjackWinLevels = 2;
    [Tooltip("Shown on the blackjack option (the table's rules decide the real odds)")]
    [Range(0f, 1f)]
    public float blackjackWinChance = 0.42f;
    [Range(0f, 1f)]
    public float blackjackPushChance = 0.08f;
    [Tooltip("The random chance roll (never below level 0)")]
    public List<TowerRollOutcome> rollOutcomes = new List<TowerRollOutcome>();

    [Header("Employees over capacity (older saves only: capacity is a hard limit on hiring)")]
    [Tooltip("Happiness lost each day per employee over capacity (0: an older save that is over is grandfathered, owner 2026-09-29)")]
    public float unseatedHappinessPerDay = 0f;
    [Tooltip("That penalty never takes more than this a day")]
    public float maxSeatPenaltyPerDay = 5f;

    [Header("Meters")]
    [Tooltip("Happiness/trust per day for each \"+1%\" of a room: happiness and trust drift 3% of the way back to 50 each day, " +
             "so +0.03/day settles one point higher (+5% = +0.15/day)")]
    public float meterPointsPerPercent = 0.03f;

    [Header("Company-wide caps of the campus's effects")]
    [Tooltip("Skill points a day from knowledge rooms, all together")]
    public float maxKnowledgePerDay = 0.25f;
    [Tooltip("Share of the upkeep that savings rooms can take off, all together (0.4 = 40%)")]
    public float maxUpkeepSavings = 0.4f;
    [Tooltip("Share of the salaries that savings rooms can take off, all together (0.15 = 15%)")]
    public float maxSalarySavings = 0.15f;

    [Header("Selling")]
    [Tooltip("Share of the room's price that selling puts back into the build budget (0-1)")]
    public double sellRefundShare = 0.5;
    [Tooltip("Also refund that share of what its furniture levels and room levels cost")]
    public bool refundFurnitureOnSell = true;

    /// <summary>The top furniture level (Ultimate = 5).</summary>
    public int MaxLevel => levelNames != null && levelNames.Count > 1 ? levelNames.Count - 1 : 5;

    public string LevelName(int level)
    {
        if (levelNames == null || levelNames.Count == 0) return "Level " + level;
        level = Math.Max(0, Math.Min(levelNames.Count - 1, level));
        return levelNames[level];
    }

    /// <summary>What reaching <paramref name="level"/> costs ($1,000 x 2.66^level; level 0 is free).</summary>
    public double LevelPrice(int level)
    {
        if (level <= 0) return 0.0;
        return Math.Round(Math.Max(0.0, furnitureBasePrice) * Math.Pow(Math.Max(1.0, furniturePriceGrowth), level));
    }

    /// <summary>Price of HQ floor <paramref name="floor"/> (floor 1 is free): floor 2's price x growth per floor, rounded.</summary>
    public double FloorPrice(int floor)
    {
        TowerBuildingType hq = Headquarters;
        return hq != null ? FloorPrice(hq, floor) : RoundSignificant(RawFloorPrice(secondFloorPrice, floorPriceGrowth, floor), floorPriceDigits);
    }

    /// <summary>Price of floor <paramref name="floor"/> of a building of <paramref name="type"/> (floor 1 comes with it), rounded.</summary>
    public double FloorPrice(TowerBuildingType type, int floor, string floorTypeId = StandardFloorId)
    {
        if (type == null) return 0.0;
        double raw = RawFloorPrice(type.secondFloorPrice, type.floorPriceGrowth, floor);
        TowerFloorType floorType = FindFloorType(floorTypeId);
        if (floorType != null) raw *= Math.Max(0.0, floorType.priceMultiplier);
        return RoundSignificant(raw, floorPriceDigits);
    }

    static double RawFloorPrice(double second, double growth, int floor)
    {
        if (floor <= 1) return 0.0;
        return Math.Max(0.0, second) * Math.Pow(Math.Max(1.0, growth), floor - 2);
    }

    /// <summary>Daily income, output, happiness or trust multiplier of a room with this many furniture levels.</summary>
    public float BonusMultiplier(int furnitureLevels)
    {
        return 1f + Math.Max(0f, bonusPerLevel) * Math.Max(0, furnitureLevels);
    }

    public static double RoundSignificant(double value, int digits)
    {
        if (value <= 0.0 || double.IsNaN(value) || double.IsInfinity(value)) return 0.0;
        digits = Math.Max(1, digits);
        double magnitude = Math.Pow(10.0, Math.Floor(Math.Log10(value)) - digits + 1);
        return Math.Round(value / magnitude) * magnitude;
    }

    public TowerRoomDefinition Find(string id)
    {
        if (string.IsNullOrEmpty(id) || rooms == null) return null;
        foreach (TowerRoomDefinition room in rooms)
            if (room != null && room.id == id) return room;
        return null;
    }

    public TowerBuildingType FindBuildingType(string id)
    {
        if (string.IsNullOrEmpty(id) || buildingTypes == null) return null;
        foreach (TowerBuildingType type in buildingTypes)
            if (type != null && type.id == id) return type;
        return null;
    }

    public TowerConnectionType FindConnectionType(string id)
    {
        if (string.IsNullOrEmpty(id) || connectionTypes == null) return null;
        foreach (TowerConnectionType type in connectionTypes)
            if (type != null && type.id == id) return type;
        return null;
    }

    public TowerFloorType FindFloorType(string id)
    {
        if (floorTypes == null) return null;
        string wanted = string.IsNullOrEmpty(id) ? StandardFloorId : id;
        foreach (TowerFloorType type in floorTypes)
            if (type != null && type.id == wanted) return type;
        return null;
    }

    /// <summary>The HQ's building type (the first with isHeadquarters; Sanitize makes one if a catalog has none).</summary>
    public TowerBuildingType Headquarters
    {
        get
        {
            if (buildingTypes == null) return null;
            foreach (TowerBuildingType type in buildingTypes)
                if (type != null && type.isHeadquarters) return type;
            return null;
        }
    }

    /// <summary>The first office (the room with isStarter), or null.</summary>
    public TowerRoomDefinition Starter
    {
        get
        {
            if (rooms == null) return null;
            foreach (TowerRoomDefinition room in rooms)
                if (room != null && room.isStarter && room.isFixed) return room;
            return null;
        }
    }

    /// <summary>Fix values that can't work (duplicates, sizes, chances, missing HQ). Safe to call more than once.</summary>
    public void Sanitize()
    {
        tilesPerFloor = Math.Max(2, tilesPerFloor);
        maxFloors = Math.Max(1, maxFloors);
        floorPriceDigits = Math.Max(1, Math.Min(6, floorPriceDigits));
        if (levelNames == null || levelNames.Count < 2) levelNames = DefaultLevelNames();
        if (rollOutcomes == null || rollOutcomes.Count == 0) rollOutcomes = DefaultRollOutcomes();
        blackjackWinLevels = Math.Max(1, blackjackWinLevels);
        unseatedHappinessPerDay = Math.Max(0f, unseatedHappinessPerDay);
        maxSeatPenaltyPerDay = Math.Max(0f, maxSeatPenaltyPerDay);
        maxKnowledgePerDay = Math.Max(0f, maxKnowledgePerDay);
        maxUpkeepSavings = Math.Max(0f, Math.Min(0.95f, maxUpkeepSavings));
        maxSalarySavings = Math.Max(0f, Math.Min(0.95f, maxSalarySavings));
        sellRefundShare = Math.Max(0.0, Math.Min(1.0, sellRefundShare));
        if (rooms == null) rooms = new List<TowerRoomDefinition>();
        if (buildingTypes == null) buildingTypes = new List<TowerBuildingType>();
        if (connectionTypes == null) connectionTypes = new List<TowerConnectionType>();
        if (floorTypes == null) floorTypes = new List<TowerFloorType>();
        if (FindFloorType(StandardFloorId) == null) floorTypes.Insert(0, new TowerFloorType());

        SanitizeBuildingTypes();

        for (int i = rooms.Count - 1; i >= 0; i--)
        {
            TowerRoomDefinition room = rooms[i];
            if (room == null || string.IsNullOrWhiteSpace(room.id))
            {
                rooms.RemoveAt(i);
                continue;
            }
            room.tiles = Math.Max(1, Math.Min(MaxTilesOfAnyBuilding(), room.tiles));
            room.seats = Math.Max(0, room.seats);
            if (room.capacity <= 0 && room.seats > 0) room.capacity = room.seats;   // a catalog from the soft-cap days
            room.capacity = Math.Max(0, room.capacity);
            room.upkeep = Finite(room.upkeep);
            room.fixedFloor = Math.Max(1, room.fixedFloor);
            room.fixedTile = Math.Max(0, Math.Min(tilesPerFloor - Math.Min(room.tiles, tilesPerFloor), room.fixedTile));
            if (room.isFixed) room.tiles = Math.Min(room.tiles, tilesPerFloor);
            if (!room.isFixed) room.isStarter = false;
            room.maxPerBuilding = Math.Max(0, room.maxPerBuilding);
            room.maxPerCompany = Math.Max(0, room.maxPerCompany);
            room.minFloor = Math.Max(1, room.minFloor);
            if (room.allowedBuildings == null) room.allowedBuildings = new List<string>();
            room.allowedBuildings.RemoveAll(string.IsNullOrEmpty);
            if (room.requires == null) room.requires = new TowerRequirements();
            if (room.requires.requiresRoom == null) room.requires.requiresRoom = "";
            if (room.levels == null) room.levels = new List<TowerRoomLevel>();
            room.levels.RemoveAll(l => l == null);
            foreach (TowerRoomLevel level in room.levels)
            {
                level.cost = Finite(level.cost);
                level.capacity = Math.Max(0, level.capacity);
                level.upkeep = Finite(level.upkeep);
                if (float.IsNaN(level.effectMultiplier) || level.effectMultiplier < 0f) level.effectMultiplier = 1f;
                level.look = Math.Max(0, Math.Min(3, level.look));
            }
            if (float.IsNaN(room.knowledgePerDay)) room.knowledgePerDay = 0f;
            if (room.slots == null) room.slots = new List<TowerFurnitureSlot>();
            room.slots.RemoveAll(s => s == null);
            if (string.IsNullOrEmpty(room.displayName)) room.displayName = room.id;
        }
        // Later duplicates lose (walk forward so the first one wins); only one first office
        var seen = new HashSet<string>();
        bool starterSeen = false;
        for (int i = 0; i < rooms.Count; i++)
        {
            if (!seen.Add(rooms[i].id))
            {
                rooms.RemoveAt(i);
                i--;
                continue;
            }
            if (rooms[i].isStarter)
            {
                if (starterSeen) rooms[i].isStarter = false;
                starterSeen = true;
            }
        }

        var seenConnections = new HashSet<string>();
        for (int i = 0; i < connectionTypes.Count; i++)
        {
            TowerConnectionType type = connectionTypes[i];
            if (type == null || string.IsNullOrWhiteSpace(type.id) || !seenConnections.Add(type.id))
            {
                connectionTypes.RemoveAt(i);
                i--;
                continue;
            }
            type.price = Finite(type.price);
            type.pricePerFloor = Finite(type.pricePerFloor);
            type.upkeep = Finite(type.upkeep);
            type.minFloor = Math.Max(1, type.minFloor);
            type.maxPerPair = Math.Max(1, type.maxPerPair);
            if (type.requires == null) type.requires = new TowerRequirements();
            if (string.IsNullOrEmpty(type.displayName)) type.displayName = type.id;
        }
    }

    void SanitizeBuildingTypes()
    {
        var seen = new HashSet<string>();
        for (int i = 0; i < buildingTypes.Count; i++)
        {
            TowerBuildingType type = buildingTypes[i];
            if (type == null || string.IsNullOrWhiteSpace(type.id) || !seen.Add(type.id))
            {
                buildingTypes.RemoveAt(i);
                i--;
                continue;
            }
            type.tiles = Math.Max(2, type.tiles);
            type.maxFloors = Math.Max(1, type.maxFloors);
            type.price = Finite(type.price);
            type.secondFloorPrice = Finite(type.secondFloorPrice);
            type.floorPriceGrowth = double.IsNaN(type.floorPriceGrowth) ? 1.0 : Math.Max(1.0, type.floorPriceGrowth);
            type.upkeepPerFloor = Finite(type.upkeepPerFloor);
            type.maxCount = Math.Max(0, type.maxCount);
            if (type.requires == null) type.requires = new TowerRequirements();
            if (string.IsNullOrEmpty(type.displayName)) type.displayName = type.id;
        }

        // Exactly one HQ, first in the list, with the rules' HQ numbers (the legacy fields stay the HQ's)
        TowerBuildingType hq = Headquarters;
        if (hq == null)
        {
            hq = FindBuildingType(HqTypeId) ?? new TowerBuildingType { id = HqTypeId, displayName = "Headquarters", facade = TowerFacade.Hq };
            if (!buildingTypes.Contains(hq)) buildingTypes.Insert(0, hq);
            hq.isHeadquarters = true;
            hq.tiles = tilesPerFloor;
            hq.maxFloors = maxFloors;
            hq.secondFloorPrice = secondFloorPrice;
            hq.floorPriceGrowth = Math.Max(1.0, floorPriceGrowth);
        }
        foreach (TowerBuildingType type in buildingTypes)
            if (type != hq) type.isHeadquarters = false;
        buildingTypes.Remove(hq);
        buildingTypes.Insert(0, hq);
        hq.price = 0.0;
        hq.maxCount = 1;
        tilesPerFloor = hq.tiles;
        maxFloors = hq.maxFloors;
    }

    int MaxTilesOfAnyBuilding()
    {
        int most = Math.Max(2, tilesPerFloor);
        if (buildingTypes != null)
            foreach (TowerBuildingType type in buildingTypes)
                if (type != null) most = Math.Max(most, type.tiles);
        return most;
    }

    static double Finite(double value)
    {
        return double.IsNaN(value) || double.IsInfinity(value) ? 0.0 : Math.Max(0.0, value);
    }

    public static List<string> DefaultLevelNames()
    {
        return new List<string> { "Basic", "Standard", "Good", "Premium", "Deluxe", "Ultimate" };
    }

    public static List<TowerRollOutcome> DefaultRollOutcomes()
    {
        return new List<TowerRollOutcome>
        {
            new TowerRollOutcome(3, 0.10f),
            new TowerRollOutcome(2, 0.40f),
            new TowerRollOutcome(0, 0.35f),
            new TowerRollOutcome(-1, 0.15f)
        };
    }

    public static Color Hex(uint rgb)
    {
        return new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
    }

    /// <summary>The rooms, buildings and numbers of the design ("HQ tower (2D build)") and the campus expansion (TowerRulesDefaults.cs).</summary>
    public static TowerRules CreateDefault()
    {
        var rules = new TowerRules
        {
            levelNames = DefaultLevelNames(),
            rollOutcomes = DefaultRollOutcomes(),
            rooms = DefaultRooms(),
            buildingTypes = DefaultBuildingTypes(),
            connectionTypes = DefaultConnectionTypes(),
            floorTypes = new List<TowerFloorType> { new TowerFloorType() }
        };
        return rules;
    }
}
