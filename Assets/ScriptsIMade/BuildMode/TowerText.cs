using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// The HQ tower's words in the current language, from the "Tower" String Table (Assets/Localization/Source/Tower.csv):
/// the built-in rooms' names, effects, descriptions and lock reasons, furniture and level names, and the small formats
/// the screens share. TowerRules, TowerPlan and TowerSave stay plain data (tested outside Unity) and keep their English;
/// a name typed in a TowerCatalog asset that isn't the built-in one shows as typed.
/// The campus (2026-09-29) adds building and link names, what a locked thing still needs (LockText), capacity, upkeep,
/// knowledge and savings texts.
/// </summary>
public static class TowerText
{
    /// <summary>The String Table collection of the tower's texts.</summary>
    public const string Table = "Tower";

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static Dictionary<string, TowerRoomDefinition> builtIn;   // the built-in rooms by id, as TowerRules.CreateDefault makes them
    static Dictionary<string, TowerBuildingType> builtInBuildings;
    static Dictionary<string, TowerConnectionType> builtInConnections;

    // Built-in furniture names and their String Table ids (tower.furniture.<id>); numbered ones ("Desk 2") use
    // tower.furniture.<id>.numbered with {number}
    static readonly Dictionary<string, string> FurnitureIds = new Dictionary<string, string>
    {
        { "Reception desk", "reception_desk" }, { "Plant", "plant" }, { "Sofa", "sofa" }, { "Bookshelf", "bookshelf" },
        { "Aquarium", "aquarium" }, { "Desk", "desk" }, { "Coffee machine", "coffee_machine" }, { "Arcade cabinet", "arcade_cabinet" },
        { "Big table", "big_table" }, { "Whiteboard", "whiteboard" }, { "Projector", "projector" }, { "Cooling unit", "cooling_unit" },
        { "Ticker board", "ticker_board" }, { "Slot machine", "slot_machine" }, { "Card table", "card_table" },
        { "Roulette wheel", "roulette_wheel" }, { "Bar", "bar" },
        // The campus (2026-09-29)
        { "Treadmill", "treadmill" }, { "Weights", "weights" }, { "Food counter", "food_counter" }, { "Filing cabinet", "filing_cabinet" },
        { "Parasol", "parasol" }, { "Helicopter", "helicopter" }, { "Server rack", "server_rack" }
    };
    static readonly Dictionary<string, string> NumberedFurnitureIds = new Dictionary<string, string>
    {
        { "Desk", "desk" }, { "Server rack", "server_rack" }, { "Terminal", "terminal" },
        { "Cubicle", "cubicle" }, { "Lab bench", "lab_bench" }, { "Trophy", "trophy" }
    };
    static readonly string[] LevelIds = { "basic", "standard", "good", "premium", "deluxe", "ultimate" };

    /// <summary>A number that changes with the language: fold it into a view's change key so it binds again.</summary>
    public static int LanguageStamp => L10n.CurrentCode.GetHashCode();

    public static string Get(string key)
    {
        return L10n.Get(Table, key);
    }

    public static string Get(string key, params (string name, object value)[] args)
    {
        return L10n.Get(Table, key, args);
    }

    static TowerRoomDefinition BuiltIn(string id)
    {
        if (builtIn == null)
        {
            builtIn = new Dictionary<string, TowerRoomDefinition>();
            foreach (TowerRoomDefinition room in TowerRules.CreateDefault().rooms)
                if (room != null && !string.IsNullOrEmpty(room.id)) builtIn[room.id] = room;
        }
        return !string.IsNullOrEmpty(id) && builtIn.TryGetValue(id, out TowerRoomDefinition found) ? found : null;
    }

    // ------------------------------------------------------------------
    // Rooms
    // ------------------------------------------------------------------

    /// <summary>"Big office" in the current language (a name typed in a catalog asset as typed).</summary>
    public static string RoomName(TowerRoomDefinition room)
    {
        if (room == null) return "";
        TowerRoomDefinition d = BuiltIn(room.id);
        return d != null && room.displayName == d.displayName ? Get("tower.room." + room.id + ".name") : room.displayName;
    }

    public static string RoomName(TowerRoom room)
    {
        return room != null ? RoomName(room.Definition) : "";
    }

    /// <summary>The room's catalog line when it has its own ("Applicants wait here"); "" when it is made from the numbers.</summary>
    public static string RoomEffect(TowerRoomDefinition room)
    {
        if (room == null || string.IsNullOrEmpty(room.effectText)) return "";
        TowerRoomDefinition d = BuiltIn(room.id);
        return d != null && room.effectText == d.effectText ? Get("tower.room." + room.id + ".effect") : room.effectText;
    }

    public static string RoomDescription(TowerRoomDefinition room)
    {
        if (room == null || string.IsNullOrEmpty(room.description)) return "";
        TowerRoomDefinition d = BuiltIn(room.id);
        return d != null && room.description == d.description ? Get("tower.room." + room.id + ".description") : room.description;
    }

    /// <summary>What unlocks a locked room ("Needs Casino License (Chaos & Luck, tier 5)"); "" when it gives none.</summary>
    public static string LockReason(TowerRoomDefinition room)
    {
        if (room == null || string.IsNullOrEmpty(room.lockReason)) return "";
        TowerRoomDefinition d = BuiltIn(room.id);
        return d != null && room.lockReason == d.lockReason ? Get("tower.room." + room.id + ".lock") : room.lockReason;
    }

    /// <summary>A locked room's reason, or "Big office is locked." when it has none.</summary>
    public static string LockedMessage(TowerRoomDefinition room)
    {
        string reason = LockReason(room);
        return reason.Length > 0 ? reason : Get("tower.message.locked", ("room", RoomName(room)));
    }

    // ------------------------------------------------------------------
    // Furniture and levels
    // ------------------------------------------------------------------

    /// <summary>"Coffee machine", "Desk 2" in the current language (a name typed in a catalog asset as typed).</summary>
    public static string SlotName(TowerFurnitureSlot slot)
    {
        if (slot == null || string.IsNullOrEmpty(slot.name)) return Get("tower.furniture.fallback");
        string name = slot.name;
        if (FurnitureIds.TryGetValue(name, out string id)) return Get("tower.furniture." + id);
        // "Desk 2", "Server rack 1", "Terminal 3"
        int space = name.LastIndexOf(' ');
        if (space > 0 && int.TryParse(name.Substring(space + 1), NumberStyles.None, Inv, out int number)
            && NumberedFurnitureIds.TryGetValue(name.Substring(0, space), out string numbered))
            return Get("tower.furniture." + numbered + ".numbered", ("number", number.ToString(Inv)));
        return name;
    }

    public static string SlotName(TowerRoom room, int slot)
    {
        return SlotName(room != null ? room.Definition.GetSlot(slot) : null);
    }

    /// <summary>"Basic" ... "Ultimate" in the current language (names typed in a catalog asset as typed).</summary>
    public static string LevelName(TowerRules rules, int level)
    {
        if (rules == null || rules.levelNames == null || rules.levelNames.Count == 0)
            return Get("tower.level.numbered", ("level", level.ToString(Inv)));
        level = Mathf.Clamp(level, 0, rules.levelNames.Count - 1);
        string name = rules.levelNames[level];
        if (defaultLevelNames == null) defaultLevelNames = TowerRules.DefaultLevelNames();
        return level < defaultLevelNames.Count && level < LevelIds.Length && name == defaultLevelNames[level] ? Get("tower.level." + LevelIds[level]) : name;
    }

    static List<string> defaultLevelNames;

    /// <summary>"Basic · Lvl 0", or with <paramref name="max"/> "Basic · Lvl 0 of 5".</summary>
    public static string LevelWithNumber(TowerRules rules, int level, int max = -1)
    {
        return max >= 0
            ? Get("tower.level.with_number.of", ("level", LevelName(rules, level)), ("number", level.ToString(Inv)), ("max", max.ToString(Inv)))
            : Get("tower.level.with_number", ("level", LevelName(rules, level)), ("number", level.ToString(Inv)));
    }

    /// <summary>"Basic (Lvl 0)".</summary>
    public static string LevelInBrackets(TowerRules rules, int level)
    {
        return Get("tower.level.in_brackets", ("level", LevelName(rules, level)), ("number", level.ToString(Inv)));
    }

    // ------------------------------------------------------------------
    // Effects and small formats
    // ------------------------------------------------------------------

    /// <summary>What a room does, as its catalog card says it ("Seats 4 staff", "+5% happiness", "+$1,500/day").</summary>
    public static string EffectText(TowerRoomDefinition d)
    {
        if (d == null) return "";
        string own = RoomEffect(d);
        if (own.Length > 0) return own;
        var parts = new List<string>(4);
        if (d.capacity > 0) parts.Add(Get("tower.effect.capacity", ("count", d.capacity)));
        if (d.happinessPercent != 0f) parts.Add(PercentPart("tower.effect.happiness", d.happinessPercent));
        if (d.trustPercent != 0f) parts.Add(PercentPart("tower.effect.trust", d.trustPercent));
        if (d.productivityPercent != 0f) parts.Add(PercentPart("tower.effect.productivity", d.productivityPercent));
        if (d.dailyIncome != 0.0) parts.Add(PerDay(UIFormat.SignedMoney(d.dailyIncome)));
        if (d.knowledgePerDay > 0f) parts.Add(Knowledge(d.knowledgePerDay));
        if (d.salarySavingsPercent > 0f) parts.Add(SavingsPart("tower.effect.salaries", d.salarySavingsPercent));
        if (d.upkeepSavingsPercent > 0f) parts.Add(SavingsPart("tower.effect.upkeep_savings", d.upkeepSavingsPercent));
        return parts.Count > 0 ? string.Join(Get("tower.list.separator"), parts) : RoomDescription(d);
    }

    /// <summary>A room's bonus with its furniture ("+6.3% happiness"), empty for rooms without one.</summary>
    public static string BonusText(TowerPlan plan, TowerRoom room)
    {
        TowerRoomDefinition d = room.Definition;
        var parts = new List<string>(2);
        if (d.happinessPercent != 0f) parts.Add(PercentPart("tower.bonus.happiness", plan.HappinessPercent(room)));
        if (d.trustPercent != 0f) parts.Add(PercentPart("tower.bonus.trust", plan.TrustPercent(room)));
        if (d.productivityPercent != 0f) parts.Add(PercentPart("tower.bonus.output", plan.ProductivityPercent(room)));
        if (d.dailyIncome != 0.0) parts.Add(PerDay(UIFormat.SignedMoney(plan.RoomBonusIncome(room))));
        if (d.knowledgePerDay > 0f) parts.Add(Knowledge(plan.KnowledgePerDay(room)));
        if (d.salarySavingsPercent > 0f) parts.Add(SavingsPart("tower.effect.salaries", plan.SalarySavingsPercent(room)));
        if (d.upkeepSavingsPercent > 0f) parts.Add(SavingsPart("tower.effect.upkeep_savings", plan.UpkeepSavingsPercent(room)));
        return string.Join(Get("tower.list.separator"), parts);
    }

    /// <summary>"+5% happiness": the sign and the number go in separately (Turkish writes +%5).</summary>
    static string PercentPart(string key, float value)
    {
        return Get(key, ("sign", value >= 0f ? "+" : UIFormat.Minus), ("percent", Mathf.Abs(value).ToString("0.#", Inv)));
    }

    /// <summary>"-3% salaries": a saving (the minus sign is written by the entry's {sign}).</summary>
    static string SavingsPart(string key, float value)
    {
        return Get(key, ("sign", UIFormat.Minus), ("percent", Mathf.Abs(value).ToString("0.#", Inv)));
    }

    /// <summary>"+0.04 SP/day": skill points a day from knowledge.</summary>
    public static string Knowledge(float perDay)
    {
        return Get("tower.effect.knowledge", ("amount", perDay.ToString("0.0##", Inv)));
    }

    /// <summary>"+$1,500/day" (<paramref name="amount"/> is formatted money, sign included).</summary>
    public static string PerDay(string amount)
    {
        return Get("tower.per_day", ("amount", amount));
    }

    /// <summary>"+$100/day" for an income (a plus sign even at $0, as the furniture lists show it).</summary>
    public static string PlusPerDay(double amount)
    {
        return PerDay("+" + UIFormat.Money(amount));
    }

    /// <summary>"50%" ("%50" in Turkish) for a number of percent.</summary>
    public static string Percent(double value)
    {
        return Get("tower.percent", ("percent", value.ToString("0.#", Inv)));
    }

    /// <summary>"+2", "−1", "±0": a change of furniture levels (the same in every language).</summary>
    public static string LevelChange(int levels)
    {
        return levels > 0 ? "+" + levels.ToString(Inv) : levels < 0 ? UIFormat.Minus + (-levels).ToString(Inv) : "±0";
    }

    /// <summary>Roughly how wide a name runs, counting wide (CJK, Hangul) characters twice.</summary>
    public static int VisualLength(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        int length = 0;
        foreach (char c in text) length += c >= 0x1100 ? 2 : 1;
        return length;
    }

    /// <summary>Two sentences one after the other ("A. B."; "A。B。" without the space).</summary>
    public static string Join(string first, string second)
    {
        if (string.IsNullOrEmpty(first)) return second ?? "";
        if (string.IsNullOrEmpty(second)) return first;
        return Get("tower.sentence.join", ("first", first), ("second", second));
    }

    // ------------------------------------------------------------------
    // The campus: buildings, links, what a locked thing needs, rules, capacity and upkeep
    // ------------------------------------------------------------------

    static TowerBuildingType BuiltInBuilding(string id)
    {
        if (builtInBuildings == null)
        {
            builtInBuildings = new Dictionary<string, TowerBuildingType>();
            foreach (TowerBuildingType type in TowerRules.DefaultBuildingTypes())
                if (type != null && !string.IsNullOrEmpty(type.id)) builtInBuildings[type.id] = type;
        }
        return !string.IsNullOrEmpty(id) && builtInBuildings.TryGetValue(id, out TowerBuildingType found) ? found : null;
    }

    static TowerConnectionType BuiltInConnection(string id)
    {
        if (builtInConnections == null)
        {
            builtInConnections = new Dictionary<string, TowerConnectionType>();
            foreach (TowerConnectionType type in TowerRules.DefaultConnectionTypes())
                if (type != null && !string.IsNullOrEmpty(type.id)) builtInConnections[type.id] = type;
        }
        return !string.IsNullOrEmpty(id) && builtInConnections.TryGetValue(id, out TowerConnectionType found) ? found : null;
    }

    /// <summary>"Office tower" in the current language (a name typed in a catalog asset as typed).</summary>
    public static string BuildingTypeName(TowerBuildingType type)
    {
        if (type == null) return "";
        TowerBuildingType d = BuiltInBuilding(type.id);
        return d != null && type.displayName == d.displayName ? Get("tower.building." + type.id + ".name") : type.displayName;
    }

    public static string BuildingTypeDescription(TowerBuildingType type)
    {
        if (type == null || string.IsNullOrEmpty(type.description)) return "";
        TowerBuildingType d = BuiltInBuilding(type.id);
        return d != null && type.description == d.description ? Get("tower.building." + type.id + ".description") : type.description;
    }

    /// <summary>"HQ", "Annex 2", "Office tower 1": a building's name on the campus.</summary>
    public static string BuildingName(TowerBuilding building)
    {
        if (building == null) return "";
        if (building.IsHeadquarters) return Get("tower.building.hq.short");
        return Get("tower.building.numbered", ("name", BuildingTypeName(building.Type)), ("number", building.Number.ToString(Inv)));
    }

    public static string ConnectionName(TowerConnectionType type)
    {
        if (type == null) return "";
        TowerConnectionType d = BuiltInConnection(type.id);
        return d != null && type.displayName == d.displayName ? Get("tower.link." + type.id + ".name") : type.displayName;
    }

    public static string ConnectionDescription(TowerConnectionType type)
    {
        if (type == null || string.IsNullOrEmpty(type.description)) return "";
        TowerConnectionType d = BuiltInConnection(type.id);
        return d != null && type.description == d.description ? Get("tower.link." + type.id + ".description") : type.description;
    }

    /// <summary>What a link does ("+2% productivity · +1% happiness").</summary>
    public static string ConnectionEffect(TowerConnectionType type)
    {
        if (type == null) return "";
        var parts = new List<string>(3);
        if (type.productivityPercent != 0f) parts.Add(PercentPart("tower.effect.productivity", type.productivityPercent));
        if (type.happinessPercent != 0f) parts.Add(PercentPart("tower.effect.happiness", type.happinessPercent));
        if (type.trustPercent != 0f) parts.Add(PercentPart("tower.effect.trust", type.trustPercent));
        return string.Join(Get("tower.list.separator"), parts);
    }

    /// <summary>
    /// What a locked room, building or link still needs, in the current language: the skill (a built-in room's own reason),
    /// a company value, a status tier, a day, HQ floors, buildings or another room first.
    /// </summary>
    public static string LockText(TowerLock lockInfo, TowerRoomDefinition room = null, TowerRules rules = null)
    {
        switch (lockInfo.kind)
        {
            case TowerLockKind.Skill:
                string reason = room != null ? LockReason(room) : lockInfo.text;
                return !string.IsNullOrEmpty(reason) ? reason : Get("tower.lock.skill");
            case TowerLockKind.PeakValue:
                return Get("tower.lock.value", ("amount", UIFormat.Money(lockInfo.value)));
            case TowerLockKind.StatusTier:
                int tier = (int)lockInfo.value;
                return Get("tower.lock.status", ("tier", StatusLadder.GetTierName(tier)), ("amount", UIFormat.Money(StatusLadder.GetTierThreshold(tier))));
            case TowerLockKind.Day:
                return Get("tower.lock.day", ("day", ((int)lockInfo.value).ToString(Inv)));
            case TowerLockKind.HqFloors:
                return Get("tower.lock.hq_floors", ("count", (int)lockInfo.value));
            case TowerLockKind.Buildings:
                return Get("tower.lock.buildings", ("count", (int)lockInfo.value));
            case TowerLockKind.Room:
                TowerRoomDefinition needed = rules != null ? rules.Find(lockInfo.text) : BuiltIn(lockInfo.text);
                return Get("tower.lock.room", ("room", needed != null ? RoomName(needed) : lockInfo.text));
            case TowerLockKind.Demo:
                return DemoMode.T("demo.lock.room");   // Demo: "In the full game"
            default:
                return "";
        }
    }

    /// <summary>A room's lock text as the plan sees it now ("" when it can be built).</summary>
    public static string RoomLockText(TowerPlan plan, TowerRoomDefinition room)
    {
        if (plan == null || room == null) return LockReason(room);
        return LockText(plan.LockOf(room), room, plan.Rules);
    }

    /// <summary>The building types a room may stand in ("Office tower, Skyscraper"), "" when it may stand anywhere.</summary>
    public static string AllowedBuildings(TowerRules rules, TowerRoomDefinition room)
    {
        if (room == null || room.allowedBuildings == null || room.allowedBuildings.Count == 0) return "";
        var names = new List<string>(room.allowedBuildings.Count);
        foreach (string id in room.allowedBuildings)
        {
            TowerBuildingType type = rules != null ? rules.FindBuildingType(id) : BuiltInBuilding(id);
            if (type != null) names.Add(BuildingTypeName(type));
        }
        return string.Join(Get("tower.list.comma"), names);
    }

    /// <summary>A room's placement rules for its card ("Top floor only · 1 per building"), "" when it has none.</summary>
    public static string RoomRules(TowerRules rules, TowerRoomDefinition room)
    {
        if (room == null) return "";
        var parts = new List<string>(3);
        switch (room.floorRule)
        {
            case TowerFloorRule.GroundOnly: parts.Add(Get("tower.rule.ground")); break;
            case TowerFloorRule.TopOnly: parts.Add(Get("tower.rule.top")); break;
            case TowerFloorRule.MinFloor: parts.Add(Get("tower.rule.min_floor", ("floor", room.minFloor.ToString(Inv)))); break;
        }
        if (room.maxPerBuilding > 0) parts.Add(Get("tower.rule.per_building", ("count", room.maxPerBuilding)));
        if (room.maxPerCompany > 0) parts.Add(Get("tower.rule.per_company", ("count", room.maxPerCompany)));
        string only = AllowedBuildings(rules, room);
        if (only.Length > 0) parts.Add(Get("tower.rule.only_in", ("buildings", only)));
        return string.Join(Get("tower.list.separator"), parts);
    }

    /// <summary>"−$1,234/day": an upkeep (a cost).</summary>
    public static string Upkeep(double perDay)
    {
        return PerDay(UIFormat.Minus + UIFormat.Money(perDay));
    }

    /// <summary>"12 / 20": employees and capacity (numbers stay invariant).</summary>
    public static string EmployeesOf(int employees, int capacity)
    {
        return UIFormat.Int(employees) + " / " + UIFormat.Int(capacity);
    }


    // ------------------------------------------------------------------
    // Upgrade results, the restore message
    // ------------------------------------------------------------------

    /// <summary>The furniture of an upgrade result in the current language (its English name when the room is unknown).</summary>
    public static string ResultSlotName(TowerUpgradeResult r)
    {
        return r.room != null ? SlotName(r.room, r.slot) : string.IsNullOrEmpty(r.slotName) ? Get("tower.furniture.fallback") : r.slotName;
    }

    /// <summary>
    /// What loading a build save tells the player, in the current language: an older build save converted into the tower,
    /// or rooms that no longer fit where they were. "" when nothing is worth saying. (TowerSave's report.message is the
    /// same in English, for the log.)
    /// </summary>
    public static string RestoreMessage(TowerRestoreReport report)
    {
        if (report == null) return "";
        string refund = report.refunded >= 0.5 ? Get("tower.restore.refund", ("amount", UIFormat.Money(report.refunded))) : "";
        string text;
        if (report.format == 1 || report.format == 2)
        {
            if (report.officesConverted == 0 && refund.Length == 0) return "";
            text = Get(report.format == 2 ? "tower.restore.converted.blueprint" : "tower.restore.converted.old", ("count", report.officesConverted));
            if (report.floorsAdded > 0) text = Join(text, Get("tower.restore.floors", ("count", report.floorsAdded)));
            if (report.levelsGranted > 0) text = Join(text, Get("tower.restore.levels", ("count", report.levelsGranted)));
        }
        else if ((report.format == TowerSave.Version || report.format == TowerSave.TowerVersion)
                 && (report.roomsMoved > 0 || report.roomsRefunded > 0 || report.buildingsRefunded > 0 || report.connectionsRefunded > 0))
        {
            int misfits = report.roomsMoved + report.roomsRefunded;
            text = misfits > 0 ? Get("tower.restore.misfit", ("count", misfits)) : Get("tower.restore.campus_changed");
            if (report.roomsMoved > 0) text = Join(text, Get("tower.restore.moved", ("count", report.roomsMoved)));
            if (report.roomsRefunded > 0) text = Join(text, Get("tower.restore.sold", ("count", report.roomsRefunded)));
        }
        else
        {
            return report.starterAdded ? Get("tower.restore.starter") : "";
        }
        text = Join(text, refund);
        return report.starterAdded ? Join(text, Get("tower.restore.starter")) : text;
    }
}
