using System;
using System.Collections.Generic;

/// <summary>
/// The campus as saved under the save key "building" (format 4). "rooms" stays one flat list of every room of every
/// building (each names its building's index), so readers of format 3 (the cloud save server's income allowance reads
/// rooms[].id and rooms[].levels) still see every room.
/// </summary>
[Serializable]
public class TowerSaveData
{
    public int version = TowerSave.Version;
    public double budget;
    /// <summary>The HQ's floors (the format 3 field, kept).</summary>
    public int floors = 1;
    /// <summary>The first-visit note was dismissed.</summary>
    public bool introSeen;
    /// <summary>Every room of every building (the built-in ones too, for their furniture and levels).</summary>
    public List<TowerRoomSave> rooms = new List<TowerRoomSave>();
    /// <summary>The buildings left to right, the HQ first (index 0).</summary>
    public List<TowerBuildingSave> buildings = new List<TowerBuildingSave>();
    /// <summary>Skybridges and other links.</summary>
    public List<TowerConnectionSave> connections = new List<TowerConnectionSave>();
}

/// <summary>One room in a save.</summary>
[Serializable]
public class TowerRoomSave
{
    public string id;
    /// <summary>Index of its building in TowerSaveData.buildings (0 = the HQ; missing in format 3 = the HQ).</summary>
    public int building;
    public int floor;
    public int tile;
    public double paid;
    /// <summary>The room's level (1-based; 0 or missing = 1).</summary>
    public int level;
    /// <summary>What its room levels cost together.</summary>
    public double levelPaid;
    public int[] levels;
    public double[] furniturePaid;
}

/// <summary>One building in a save.</summary>
[Serializable]
public class TowerBuildingSave
{
    /// <summary>Its TowerBuildingType id ("hq", "annex", "tower", "skyscraper").</summary>
    public string type;
    public int floors = 1;
    /// <summary>What buying it cost.</summary>
    public double paid;
    /// <summary>What its floors cost together.</summary>
    public double floorsPaid;
    /// <summary>Floor type ids by floor (empty = every floor "standard"; the hook for later floor kinds).</summary>
    public List<string> floorTypes = new List<string>();
}

/// <summary>One link (skybridge) in a save: between building <c>left</c> and the next one, at <c>floor</c>.</summary>
[Serializable]
public class TowerConnectionSave
{
    public string type;
    public int left;
    public int floor;
    public double paid;
}

/// <summary>
/// Reads every format of the "building" save: the campus (4), the single tower (3: rooms), the blueprint build mode (2:
/// pieces with id, x, y, rotated, paid) and the old 3D build mode (1: items with itemName and a furniture count). Unknown
/// fields are ignored.
/// </summary>
[Serializable]
public class TowerLoadData
{
    public int version;
    public double budget;
    public int floors;
    public bool introSeen;
    public List<TowerRoomSave> rooms;
    public List<TowerBuildingSave> buildings;
    public List<TowerConnectionSave> connections;
    public List<TowerLegacyPiece> pieces;
    public List<TowerLegacyItem> items;
}

/// <summary>A piece of the blueprint build mode (format 2). Cells count from the old HQ's top-left corner.</summary>
[Serializable]
public class TowerLegacyPiece
{
    public string id;
    public int x;
    public int y;
    public bool rotated;
    public double paid;
}

/// <summary>An office of the old 3D build mode (format 1).</summary>
[Serializable]
public class TowerLegacyItem
{
    public string itemName;
    public int furniture;
}

/// <summary>What loading did (for the log and a message to the player).</summary>
public sealed class TowerRestoreReport
{
    /// <summary>Format that was read: 4 campus, 3 tower, 2 blueprint, 1 old 3D build mode, 0 nothing.</summary>
    public int format;
    public bool introSeen;
    public int roomsRestored;
    public int roomsMoved;
    public int roomsRefunded;
    public int officesConverted;
    public int floorsAdded;
    public int levelsGranted;
    /// <summary>The first office moved in (an older save whose spot next to the lobby was free).</summary>
    public bool starterAdded;
    public int buildingsRestored;
    public int buildingsRefunded;
    public int connectionsRestored;
    public int connectionsRefunded;
    /// <summary>Money that went back into the build budget (what couldn't be kept).</summary>
    public double refunded;
    public readonly List<string> log = new List<string>();
    /// <summary>What to tell the player (empty = nothing worth saying).</summary>
    public string message = "";
}

/// <summary>
/// Saving and loading the campus, independent of Unity (TowerBuilder turns the data into JSON). Loading keeps the
/// player's value:
/// - Format 4: buildings (an unknown type is sold back), then rooms where they were (one that no longer fits moves to a
///   free spot anywhere on the campus, else it is refunded), then links (refunded if their pair or floor is gone).
/// - Format 3 (one tower): its rooms go into the HQ the same way. Then the first office moves in if its spot next to the
///   lobby (floor 1, tiles 7-8) is free.
/// - Format 2 (the blueprint): the first office first, then Small and Big offices become tower rooms (big ones first,
///   lowest free spot, floors added for free when needed); the money paid for the desks inside them buys furniture levels
///   at today's prices (lowest level first), and whatever is left over, every other piece and anything that doesn't fit
///   goes back into the build budget.
/// - Format 1 (the old 3D build mode): its offices are converted the same way (their furniture at the old prices:
///   $2,000 in big offices, $1,000 elsewhere).
/// A gamble that hadn't settled isn't in the save: its stake was already taken, so a save never gives it back.
/// </summary>
public static class TowerSave
{
    public const int Version = 4;
    /// <summary>The single-tower format, read and converted.</summary>
    public const int TowerVersion = 3;

    // The blueprint build mode's catalog (format 2), for sizes and which pieces were desks
    const int OldMainOfficeWidth = 22;
    const int OldMainOfficeHeight = 21;
    const double OldSmallDeskPrice = 1000;
    const double OldBigDeskPrice = 3000;
    // The old 3D build mode's furniture prices (format 1)
    const double OldSmallFurniturePrice = 1000;
    const double OldBigFurniturePrice = 2000;

    public const string SmallOfficeId = "small_office";
    public const string BigOfficeId = "big_office";

    // ------------------------------------------------------------------
    // Saving
    // ------------------------------------------------------------------

    public static TowerSaveData Capture(TowerPlan plan, bool introSeen)
    {
        var data = new TowerSaveData { budget = plan.Budget, floors = plan.Floors, introSeen = introSeen };
        foreach (TowerBuilding building in plan.Buildings)
        {
            var saved = new TowerBuildingSave { type = building.Type.id, floors = building.Floors, paid = building.Paid, floorsPaid = building.FloorsPaid };
            for (int floor = 1; floor <= building.Floors; floor++)
            {
                string id = building.FloorTypeAt(floor);
                if (id != TowerRules.StandardFloorId)
                {
                    // Only written when some floor isn't standard (all "standard" today)
                    while (saved.floorTypes.Count < floor - 1) saved.floorTypes.Add(TowerRules.StandardFloorId);
                    saved.floorTypes.Add(id);
                }
            }
            data.buildings.Add(saved);
        }
        foreach (TowerRoom room in plan.Rooms)
        {
            var saved = new TowerRoomSave
            {
                id = room.Definition.id,
                building = room.Building.Slot,
                floor = room.Floor,
                tile = room.Tile,
                paid = room.Paid,
                level = room.Level,
                levelPaid = room.LevelPaid,
                levels = new int[room.SlotCount],
                furniturePaid = new double[room.SlotCount]
            };
            for (int i = 0; i < room.SlotCount; i++)
            {
                saved.levels[i] = room.GetLevel(i);
                saved.furniturePaid[i] = room.GetFurniturePaid(i);
            }
            data.rooms.Add(saved);
        }
        foreach (TowerConnection link in plan.Connections)
            data.connections.Add(new TowerConnectionSave { type = link.Type.id, left = link.Left.Slot, floor = link.Floor, paid = link.Paid });
        return data;
    }

    // ------------------------------------------------------------------
    // Loading
    // ------------------------------------------------------------------

    /// <summary>Replace the plan with the saved campus (any format). The plan's budget is the saved one plus refunds.</summary>
    public static TowerRestoreReport Restore(TowerPlan plan, TowerLoadData data)
    {
        var report = new TowerRestoreReport();
        if (data == null)
        {
            plan.Reset(true);
            return report;
        }
        plan.Reset(false);   // the first office: each format decides below

        report.introSeen = data.introSeen;
        bool hasRooms = data.rooms != null && data.rooms.Count > 0;
        bool hasPieces = data.pieces != null && data.pieces.Count > 0;
        bool hasItems = data.items != null && data.items.Count > 0;
        bool hasBuildings = data.buildings != null && data.buildings.Count > 0;

        if (data.version >= Version || hasBuildings)
        {
            report.format = Version;
            RestoreCampus(plan, data, report);
        }
        else if (data.version == TowerVersion || (hasRooms && !hasPieces && !hasItems))
        {
            report.format = TowerVersion;
            plan.SetFloors(Math.Max(1, data.floors));
            RestoreRooms(plan, data.rooms, new[] { plan.Headquarters }, report);
            // An older save gets the first office only if its spot is free
            report.starterAdded = plan.PlaceStarter() != null;
        }
        else if (data.version == 2 || hasPieces)
        {
            report.format = 2;
            report.starterAdded = plan.PlaceStarter() != null;
            ConvertBlueprint(plan, data.pieces, report);
        }
        else if (hasItems)
        {
            report.format = 1;
            report.starterAdded = plan.PlaceStarter() != null;
            ConvertOld3D(plan, data.items, report);
        }
        else
        {
            report.format = Math.Max(0, data.version);
            plan.PlaceStarter();   // nothing was built: the company starts as a new one would
        }

        plan.SetBudget(Finite(data.budget) + report.refunded);
        report.message = Message(report);
        return report;
    }

    static void RestoreCampus(TowerPlan plan, TowerLoadData data, TowerRestoreReport report)
    {
        TowerBuilding[] map;
        if (data.buildings == null || data.buildings.Count == 0)
        {
            plan.SetFloors(Math.Max(1, data.floors));
            map = new[] { plan.Headquarters };
        }
        else
        {
            map = new TowerBuilding[data.buildings.Count];
            for (int i = 0; i < data.buildings.Count; i++)
            {
                TowerBuildingSave saved = data.buildings[i];
                if (saved == null) continue;
                if (i == 0)
                {
                    // The HQ is always the first building (its type can't change)
                    map[0] = plan.Headquarters;
                    plan.SetFloors(plan.Headquarters, Math.Max(1, Math.Max(saved.floors, data.floors)));
                    plan.RestoreBuildingRecord(plan.Headquarters, saved.floorsPaid, saved.floorTypes);
                    continue;
                }
                TowerBuildingType type = plan.Rules.FindBuildingType(saved.type);
                if (type == null || type.isHeadquarters)
                {
                    report.buildingsRefunded++;
                    Refund(report, Finite(saved.paid) + Finite(saved.floorsPaid), "the unknown building '" + saved.type + "'");
                    continue;
                }
                map[i] = plan.RestoreBuilding(type, saved.floors, saved.paid, saved.floorsPaid, saved.floorTypes);
                report.buildingsRestored++;
            }
            if (map[0] == null) map[0] = plan.Headquarters;
        }

        RestoreRooms(plan, data.rooms, map, report);

        if (data.connections == null) return;
        foreach (TowerConnectionSave saved in data.connections)
        {
            if (saved == null) continue;
            TowerConnectionType type = plan.Rules.FindConnectionType(saved.type);
            TowerBuilding left = saved.left >= 0 && saved.left < map.Length ? map[saved.left] : null;
            if (type != null && left != null && plan.RestoreConnection(type, left, saved.floor, saved.paid) != null)
            {
                report.connectionsRestored++;
                continue;
            }
            report.connectionsRefunded++;
            Refund(report, saved.paid, "the " + (type != null ? type.displayName : "link '" + saved.type + "'") + " (its buildings changed)");
        }
    }

    /// <summary>Rooms into their buildings (built-in rooms first, then the rest where they were, then misfits anywhere free).</summary>
    static void RestoreRooms(TowerPlan plan, List<TowerRoomSave> saves, TowerBuilding[] map, TowerRestoreReport report)
    {
        if (saves == null) return;

        // Built-in rooms first: their spots are theirs, and a saved first office comes back before anything could take its spot
        foreach (TowerRoomSave saved in saves)
        {
            if (saved == null) continue;
            TowerRoomDefinition definition = plan.Rules.Find(saved.id);
            if (definition == null || !definition.isFixed) continue;
            TowerRoom builtIn = plan.FixedRoom(definition.id);
            if (builtIn == null && definition.isStarter) builtIn = plan.PlaceStarter();
            if (builtIn != null)
            {
                plan.RestoreLevels(builtIn, saved.levels, saved.furniturePaid);
                plan.RestoreRoomLevel(builtIn, Math.Max(1, saved.level), saved.levelPaid);
            }
            else
            {
                Refund(report, FurnitureValue(saved) + Finite(saved.levelPaid), "the furniture of '" + saved.id + "'");
            }
        }

        // Rooms that fit where they were first, so a moved room never takes another one's spot
        var later = new List<TowerRoomSave>();
        foreach (TowerRoomSave saved in saves)
        {
            if (saved == null) continue;
            TowerRoomDefinition definition = plan.Rules.Find(saved.id);
            if (definition == null)
            {
                report.roomsRefunded++;
                Refund(report, SavedValue(saved, null), "the unknown room '" + saved.id + "'");
                continue;
            }
            if (definition.isFixed) continue;
            TowerBuilding building = saved.building >= 0 && saved.building < map.Length ? map[saved.building] : null;
            if (building != null && plan.Restore(definition, building, saved.floor, saved.tile, saved.paid, saved.levels, saved.furniturePaid,
                                                 Math.Max(1, saved.level), saved.levelPaid) != null)
            {
                report.roomsRestored++;
                continue;
            }
            later.Add(saved);
        }

        foreach (TowerRoomSave saved in later)
        {
            TowerRoomDefinition definition = plan.Rules.Find(saved.id);
            if (plan.TryFindSpotAnywhere(definition, out TowerBuilding building, out int floor, out int tile)
                && plan.Restore(definition, building, floor, tile, saved.paid, saved.levels, saved.furniturePaid, Math.Max(1, saved.level), saved.levelPaid) != null)
            {
                report.roomsMoved++;
                report.log.Add("The " + definition.displayName + " from building " + (saved.building + 1) + ", floor " + saved.floor + ", tile " + (saved.tile + 1)
                               + " no longer fit there; it moved to building " + (building.Slot + 1) + ", floor " + floor + ", tile " + (tile + 1) + ".");
                continue;
            }
            report.roomsRefunded++;
            Refund(report, SavedValue(saved, definition), "the " + definition.displayName + " (no free spot)");
        }
    }

    /// <summary>An office being converted from an older save.</summary>
    sealed class OldOffice
    {
        public TowerRoomDefinition definition;
        public string name;
        public double paid;
        public double furniture;
        public int order;
        public int x, y, width, height;

        public bool Contains(int px, int py, int w, int h)
        {
            return px >= x && py >= y && px + w <= x + width && py + h <= y + height;
        }
    }

    static void ConvertBlueprint(TowerPlan plan, List<TowerLegacyPiece> pieces, TowerRestoreReport report)
    {
        // A partial save can lack the list (JsonUtility leaves it null): keep the budget, convert nothing
        if (pieces == null) return;
        var offices = new List<OldOffice>();
        var fixtures = new List<TowerLegacyPiece>();
        foreach (TowerLegacyPiece piece in pieces)
        {
            if (piece == null) continue;
            if (OldSize(piece.id, out int w, out int h, out bool isRoom, out _) && isRoom)
            {
                if (piece.rotated) Swap(ref w, ref h);
                offices.Add(new OldOffice
                {
                    definition = plan.Rules.Find(piece.id),
                    name = piece.id,
                    paid = Finite(piece.paid),
                    order = offices.Count,
                    x = piece.x, y = piece.y, width = w, height = h
                });
            }
            else
            {
                fixtures.Add(piece);
            }
        }

        foreach (TowerLegacyPiece piece in fixtures)
        {
            bool known = OldSize(piece.id, out int w, out int h, out _, out bool isDesk);
            if (!known) w = h = 1;
            if (piece.rotated) Swap(ref w, ref h);
            OldOffice office = null;
            bool inMainOffice = piece.x >= 0 && piece.y >= 0 && piece.x + w <= OldMainOfficeWidth && piece.y + h <= OldMainOfficeHeight;
            if (!inMainOffice)
            {
                foreach (OldOffice candidate in offices)
                {
                    if (!candidate.Contains(piece.x, piece.y, w, h)) continue;
                    office = candidate;
                    break;
                }
            }

            double paid = Finite(piece.paid);
            if (isDesk && office != null && office.definition != null)
            {
                bool big = office.name == BigOfficeId;
                office.furniture += paid > 0.0 ? paid : (big ? OldBigDeskPrice : OldSmallDeskPrice);
                continue;
            }
            Refund(report, paid, "the " + (string.IsNullOrEmpty(piece.id) ? "unknown piece" : piece.id));
        }

        PlaceOffices(plan, offices, report);
    }

    static void ConvertOld3D(TowerPlan plan, List<TowerLegacyItem> items, TowerRestoreReport report)
    {
        var offices = new List<OldOffice>();
        foreach (TowerLegacyItem item in items)
        {
            if (item == null) continue;
            string key = NameKey(item.itemName);
            bool big = key.Contains("big") || key.Contains("large");
            string id = big ? BigOfficeId : key.Contains("office") ? SmallOfficeId : null;
            if (id == null)
            {
                report.log.Add("The old build item '" + item.itemName + "' has no place in the tower; skipped.");
                continue;
            }
            TowerRoomDefinition definition = plan.Rules.Find(id);
            offices.Add(new OldOffice
            {
                definition = definition,
                name = id,
                paid = definition != null ? Math.Max(0.0, definition.cost) : 0.0,
                furniture = Math.Max(0, item.furniture) * (big ? OldBigFurniturePrice : OldSmallFurniturePrice),
                order = offices.Count
            });
        }
        PlaceOffices(plan, offices, report);
    }

    /// <summary>Big offices first, each at the HQ's lowest free spot (adding floors for free); desks become furniture levels.</summary>
    static void PlaceOffices(TowerPlan plan, List<OldOffice> offices, TowerRestoreReport report)
    {
        offices.Sort((a, b) =>
        {
            int tilesA = a.definition != null ? a.definition.Tiles : 0, tilesB = b.definition != null ? b.definition.Tiles : 0;
            int c = tilesB.CompareTo(tilesA);
            return c != 0 ? c : a.order.CompareTo(b.order);
        });

        foreach (OldOffice office in offices)
        {
            if (office.definition == null)
            {
                report.roomsRefunded++;
                Refund(report, office.paid + office.furniture, "the " + office.name + " (not in the tower's catalog)");
                continue;
            }

            bool found = plan.TryFindSpot(office.definition, out int floor, out int tile);
            while (!found && plan.AddFreeFloor())
            {
                report.floorsAdded++;
                found = plan.TryFindSpot(office.definition, out floor, out tile);
            }
            TowerRoom room = found ? plan.Restore(office.definition, floor, tile, office.paid, null, null) : null;
            if (room == null)
            {
                report.roomsRefunded++;
                Refund(report, (office.paid > 0.0 ? office.paid : office.definition.cost) + office.furniture,
                       "the " + office.definition.displayName + " (the tower is full)");
                continue;
            }

            report.officesConverted++;
            double left = SpendOnLevels(plan, room, office.furniture, report);
            if (left >= 0.5)
                Refund(report, left, "the rest of the " + office.definition.displayName + "'s furniture money");
        }
    }

    /// <summary>Buy furniture levels with <paramref name="money"/> at list price, lowest level first. Returns what's left.</summary>
    public static double SpendOnLevels(TowerPlan plan, TowerRoom room, double money, TowerRestoreReport report = null)
    {
        money = Finite(money);
        while (room != null && room.SlotCount > 0)
        {
            int best = -1, bestLevel = int.MaxValue;
            for (int slot = 0; slot < room.SlotCount; slot++)
            {
                int level = room.GetLevel(slot);
                if (level < plan.MaxLevel && level < bestLevel)
                {
                    best = slot;
                    bestLevel = level;
                }
            }
            if (best < 0) break;
            double price = plan.Rules.LevelPrice(bestLevel + 1);
            if (price > money + 0.001) break;
            money -= price;
            plan.ChangeLevel(room, best, 1, price);
            if (report != null) report.levelsGranted++;
        }
        return Math.Max(0.0, money);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    /// <summary>Size in cells, room or not, desk or not, of a piece of the blueprint build mode's default catalog.</summary>
    static bool OldSize(string id, out int width, out int height, out bool isRoom, out bool isDesk)
    {
        isRoom = false;
        isDesk = false;
        switch (id)
        {
            case SmallOfficeId: width = 12; height = 10; isRoom = true; return true;
            case BigOfficeId: width = 20; height = 12; isRoom = true; return true;
            case "desk": width = 3; height = 2; isDesk = true; return true;
            case "bookshelf": width = 4; height = 1; return true;
            case "plant": width = 2; height = 2; return true;
            case "painting": width = 3; height = 1; return true;
            case "server_rack": width = 2; height = 3; return true;
            case "big_screen": width = 4; height = 1; return true;
            case "sofa": width = 5; height = 2; return true;
            case "coffee_machine": width = 1; height = 1; return true;
            default: width = 1; height = 1; return false;
        }
    }

    static void Swap(ref int a, ref int b)
    {
        int t = a;
        a = b;
        b = t;
    }

    /// <summary>What a saved room was worth: its price (or the catalog price), its furniture and its room levels.</summary>
    static double SavedValue(TowerRoomSave saved, TowerRoomDefinition definition)
    {
        double paid = Finite(saved.paid);
        if (paid <= 0.0 && definition != null) paid = Math.Max(0.0, definition.cost);
        return paid + FurnitureValue(saved) + Finite(saved.levelPaid);
    }

    static double FurnitureValue(TowerRoomSave saved)
    {
        double total = 0.0;
        if (saved.furniturePaid != null)
            foreach (double paid in saved.furniturePaid) total += Finite(paid);
        return total;
    }

    static void Refund(TowerRestoreReport report, double amount, string what)
    {
        amount = Finite(amount);
        if (amount <= 0.0) return;
        report.refunded += amount;
        report.log.Add("Refunded " + Money(amount) + " for " + what + ".");
    }

    static double Finite(double value)
    {
        return double.IsNaN(value) || double.IsInfinity(value) ? 0.0 : Math.Max(0.0, value);
    }

    static string NameKey(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var chars = new System.Text.StringBuilder(text.Length);
        foreach (char c in text)
            if (char.IsLetterOrDigit(c)) chars.Append(char.ToLowerInvariant(c));
        return chars.ToString();
    }

    static string Money(double value)
    {
        return "$" + Math.Round(value).ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
    }

    static string Message(TowerRestoreReport report)
    {
        string refund = report.refunded >= 0.5 ? Money(report.refunded) + " went back into your build budget" : "";
        string starter = report.starterAdded ? "Your first office moved in next to the lobby." : "";
        if (report.format == 2 || report.format == 1)
        {
            if (report.officesConverted == 0 && refund.Length == 0) return starter;
            string text = (report.format == 2 ? "Build mode is now the HQ tower: " : "Your old offices moved into the HQ tower: ")
                          + report.officesConverted + (report.officesConverted == 1 ? " office moved in" : " offices moved in");
            if (report.floorsAdded > 0) text += " (" + report.floorsAdded + (report.floorsAdded == 1 ? " floor" : " floors") + " added for free)";
            if (report.levelsGranted > 0) text += ", their desks became " + report.levelsGranted + " furniture level" + (report.levelsGranted == 1 ? "" : "s");
            if (refund.Length > 0) text += " and " + refund;
            return (text + ". " + starter).Trim();
        }
        if ((report.format == Version || report.format == TowerVersion)
            && (report.roomsMoved > 0 || report.roomsRefunded > 0 || report.buildingsRefunded > 0 || report.connectionsRefunded > 0))
        {
            int count = report.roomsMoved + report.roomsRefunded;
            string text = count > 0 ? count + (count == 1 ? " room" : " rooms") + " no longer fit where " + (count == 1 ? "it was" : "they were") : "Part of the campus changed";
            if (report.roomsMoved > 0) text += ": " + report.roomsMoved + " moved to a free spot";
            if (report.roomsRefunded > 0) text += (report.roomsMoved > 0 ? ", " : ": ") + report.roomsRefunded + " sold back";
            if (refund.Length > 0) text += " (" + refund + ")";
            return (text + ". " + starter).Trim();
        }
        return starter;
    }
}
