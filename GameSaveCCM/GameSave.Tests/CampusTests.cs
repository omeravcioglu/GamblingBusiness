using System.Globalization;
using System.Text.RegularExpressions;

namespace GameSave.Tests;

/// <summary>
/// The HQ campus in the income allowance (the "building" extra, format 4): the server's room table against the game's own
/// numbers (BuildMode/TowerRulesDefaults.cs), room levels, per-company limits, the room cap, the token budget, and format 3
/// saves read as before.
/// </summary>
public class CampusTests
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>The game's income of a room (TowerPlan.RoomDailyIncome): base x (1 + 0.25f x levels) x the float level multiplier + $100 a level.</summary>
    static double GameIncome(double baseIncome, int furnitureLevels, float levelMultiplier)
        => baseIncome * (1f + 0.25f * furnitureLevels) * levelMultiplier + furnitureLevels * 100.0;

    static SaveBuilder CampusSave(params CampusRoom[] rooms) => new SaveBuilder { Campus = rooms.ToList() };

    static double TowerIncome(SaveBuilder save)
    {
        SaveSnapshot parsed = SaveSnapshot.Parse(save.Json());
        Assert.That(parsed.Ok, Is.True, parsed.Error);
        return parsed.TowerIncomeMax;
    }

    // ================================================================================================================
    // The server's numbers against the game's
    // ================================================================================================================

    [Test]
    public void IncomeRooms_AtTheirTop_MatchTheGame()
    {
        // Top level, four pieces at Ultimate (20 levels): the game pays $22,700 / $36,500 / $15,800 / $650,000
        Assert.That(GameIncome(1500, 20, 2.3f), Is.EqualTo(22_700).Within(0.01));
        Assert.That(GameIncome(2500, 20, 2.3f), Is.EqualTo(36_500).Within(0.01));
        Assert.That(GameIncome(1000, 20, 2.3f), Is.EqualTo(15_800).Within(0.01));
        Assert.That(GameIncome(60000, 20, 1.8f), Is.EqualTo(650_000).Within(0.1));
        int[] maxed = { 5, 5, 5, 5 };
        // The server: the same, with $150 a furniture level (+$1,000 of margin on 20 levels)
        Assert.That(SaveCalibration.TowerRoomIncomeMax("trading_floor", maxed, 4), Is.EqualTo(23_700).Within(1e-6));
        Assert.That(SaveCalibration.TowerRoomIncomeMax("casino_room", maxed, 4), Is.EqualTo(37_500).Within(1e-6));
        Assert.That(SaveCalibration.TowerRoomIncomeMax("marketing", maxed, 4), Is.EqualTo(16_800).Within(1e-6));
        Assert.That(SaveCalibration.TowerRoomIncomeMax("trophy_hall", maxed, 3), Is.EqualTo(651_000).Within(1e-6));
        // Other rooms: their furniture only, whatever their level (5 x $100 a piece in the game)
        Assert.That(SaveCalibration.TowerRoomIncomeMax("open_plan", new[] { 5, 5, 5, 5, 5, 5, 5 }, 5), Is.EqualTo(35 * 150));
        Assert.That(SaveCalibration.TowerRoomIncomeMax("helipad", new[] { 5, 5 }, 2), Is.EqualTo(10 * 150));

        Assert.That(SaveCalibration.TowerRoomMaxPerCompany("trophy_hall"), Is.EqualTo(1));
        Assert.That(SaveCalibration.TowerRoomMaxPerCompany("marketing"), Is.EqualTo(2));
        Assert.That(SaveCalibration.TowerRoomMaxPerCompany("casino_room"), Is.EqualTo(0), "no limit");
        Assert.That(SaveCalibration.TowerRoomMaxPerCompany("mystery_room"), Is.EqualTo(0));
    }

    [Test]
    public void RoomLevels_ClampLikeTheGame_UnknownRoomsGetTheTopMultiplier()
    {
        Assert.That(SaveCalibration.TowerRoomLevelMultiplier("casino_room", 0), Is.EqualTo(1.0), "0 / missing = level 1");
        Assert.That(SaveCalibration.TowerRoomLevelMultiplier("casino_room", -3), Is.EqualTo(1.0));
        Assert.That(SaveCalibration.TowerRoomLevelMultiplier("casino_room", 1), Is.EqualTo(1.0));
        Assert.That(SaveCalibration.TowerRoomLevelMultiplier("casino_room", 2), Is.EqualTo(1.4));
        Assert.That(SaveCalibration.TowerRoomLevelMultiplier("casino_room", 4), Is.EqualTo(2.3));
        Assert.That(SaveCalibration.TowerRoomLevelMultiplier("casino_room", 99), Is.EqualTo(2.3), "past the top = the top (TowerPlan.Restore clamps it)");
        Assert.That(SaveCalibration.TowerRoomLevelMultiplier("trophy_hall", 3), Is.EqualTo(1.8));
        Assert.That(SaveCalibration.TowerRoomLevelMultiplier("trophy_hall", 4), Is.EqualTo(1.8));
        Assert.That(SaveCalibration.TowerRoomLevelMultiplier("break_room", 3), Is.EqualTo(1.0), "no income to multiply");

        // An id this version doesn't know: the casino room's $2,500 at x2.3 (a newer game's room), never the trophy hall's size
        Assert.That(SaveCalibration.IsKnownTowerRoom("future_room"), Is.False);
        Assert.That(SaveCalibration.TowerRoomBaseIncome("future_room"), Is.EqualTo(2500));
        Assert.That(SaveCalibration.TowerRoomIncomeMax("future_room", new[] { 5, 5, 5, 5 }, 1), Is.EqualTo(2500 * 6 * 2.3 + 20 * 150).Within(1e-6));
        Assert.That(SaveCalibration.TowerRoomIncomeMax(null, new int[0]), Is.EqualTo(2500 * 2.3).Within(1e-6), "a room without an id counts as unknown");
    }

    [Test]
    public void ServerTable_MatchesTowerRulesDefaults()
    {
        // The code is the truth: read the game's own room and building numbers and hold the server's table against them, so
        // tuning TowerRulesDefaults.cs without updating SaveCalibration fails here
        string defaultsPath = FindGameFile("Assets", "ScriptsIMade", "BuildMode", "TowerRulesDefaults.cs");
        string rulesPath = FindGameFile("Assets", "ScriptsIMade", "BuildMode", "TowerRules.cs");
        if (defaultsPath == null || rulesPath == null) Assert.Ignore("The game's BuildMode sources aren't next to the module.");
        string defaults = File.ReadAllText(defaultsPath), rules = File.ReadAllText(rulesPath);

        // Furniture: $100 a level, +25% a level, levels 0..5 (TowerRules.cs)
        Assert.That(Number(rules, @"public double incomePerLevel = ([\d.]+)"), Is.LessThanOrEqualTo(SaveCalibration.TowerIncomePerLevel));
        Assert.That(Number(rules, @"public float bonusPerLevel = ([\d.]+)f"), Is.EqualTo(SaveCalibration.TowerBonusPerLevel));
        Match names = Regex.Match(rules, @"DefaultLevelNames\(\)\s*\{\s*return new List<string> \{([^}]*)\}");
        Assert.That(names.Success, Is.True, "DefaultLevelNames not found");
        Assert.That(Regex.Matches(names.Groups[1].Value, "\"").Count / 2 - 1, Is.LessThanOrEqualTo(SaveCalibration.TowerMaxLevel));

        string roomsPart = Between(defaults, "DefaultRooms()", "DefaultBuildingTypes()");
        string[] roomBlocks = roomsPart.Split("new TowerRoomDefinition").Skip(1).ToArray();
        Assert.That(roomBlocks.Length, Is.GreaterThanOrEqualTo(26), "every room of the campus");
        int fixedRooms = 0, fixedTiles = 0, smallest = int.MaxValue;
        var ids = new HashSet<string>();
        foreach (string raw in roomBlocks)
        {
            string id = Regex.Match(raw, "\\bid = \"([^\"]+)\"").Groups[1].Value;
            string block = Regex.Replace(raw, "\"(?:[^\"\\\\]|\\\\.)*\"", "\"\"");   // the numbers only, never text in the strings
            Assert.That(ids.Add(id), Is.True, "room id " + id + " twice");
            int tiles = (int)Number(block, @"\btiles = (\d+)");
            double income = Number(block, @"\bdailyIncome = ([\d.]+)", 0);
            int maxPerCompany = (int)Number(block, @"\bmaxPerCompany = (\d+)", 0);
            int slots = Regex.Matches(block, @"\bS\(").Count;
            List<float> multipliers = Regex.Matches(block, @"\bLv\(\s*[\d.]+\s*,\s*\d+\s*,\s*([\d.]+)f")
                                           .Select(m => float.Parse(m.Groups[1].Value, Inv)).ToList();

            Assert.That(SaveCalibration.IsKnownTowerRoom(id), Is.True, id + ": a room the server doesn't know gets the unknown room's income");
            Assert.That(SaveCalibration.TowerRoomBaseIncome(id), Is.EqualTo(income), id + ": base income");
            Assert.That(slots, Is.InRange(1, SaveCalibration.TowerMaxSlotsPerRoom), id + ": furniture slots");
            if (income > 0)
            {
                // Level 1 is x1; a level past the top is the top (TowerRoomDefinition.MultiplierAt)
                for (int level = 1; level <= multipliers.Count + 2; level++)
                {
                    double game = level <= 1 ? 1.0 : multipliers[Math.Min(multipliers.Count, level - 1) - 1];
                    double server = SaveCalibration.TowerRoomLevelMultiplier(id, level);
                    Assert.That(server, Is.GreaterThanOrEqualTo(game).And.EqualTo(game).Within(1e-6), id + " at level " + level);
                }
                int serverLimit = SaveCalibration.TowerRoomMaxPerCompany(id);
                if (maxPerCompany == 0) Assert.That(serverLimit, Is.EqualTo(0), id + ": no limit in the game, none on the server");
                else Assert.That(serverLimit == 0 || serverLimit >= maxPerCompany, Is.True, id + ": the server's limit is below the game's " + maxPerCompany);
            }
            if (Regex.IsMatch(block, @"\bisFixed = true"))
            {
                fixedRooms++;
                fixedTiles += tiles;
            }
            else
            {
                smallest = Math.Min(smallest, tiles);
            }
        }

        // Buildings: tiles x floors x how many a campus may own; the rooms that fit, at the smallest size
        string buildingsPart = Between(defaults, "DefaultBuildingTypes()", "DefaultConnectionTypes()");
        long campusTiles = 0;
        foreach (string raw in buildingsPart.Split("new TowerBuildingType").Skip(1))
        {
            string block = Regex.Replace(raw, "\"(?:[^\"\\\\]|\\\\.)*\"", "\"\"");
            int count = Regex.IsMatch(block, @"\bisHeadquarters = true") ? 1 : (int)Number(block, @"\bmaxCount = (\d+)");
            Assert.That(count, Is.GreaterThan(0), "a building type without a limit makes the campus endless: TowerMaxRooms needs another bound");
            campusTiles += (long)Number(block, @"\btiles = (\d+)") * (long)Number(block, @"\bmaxFloors = (\d+)") * count;
        }
        long mostRooms = fixedRooms + (campusTiles - fixedTiles) / smallest;
        TestContext.Out.WriteLine($"Campus: {campusTiles:N0} tiles, at most {mostRooms:N0} rooms (the server counts {SaveCalibration.TowerMaxRooms:N0}).");
        Assert.That(campusTiles, Is.EqualTo(2844), "the campus the calibration comments describe");
        Assert.That(mostRooms, Is.LessThanOrEqualTo(SaveCalibration.TowerMaxRooms));
    }

    // ================================================================================================================
    // Reading the save
    // ================================================================================================================

    [Test]
    public void Format4_MultiBuildingCampus_WithRoomLevels()
    {
        var save = new SaveBuilder
        {
            Campus = new()
            {
                new("lobby", 0, 1, 0, 1, new[] { 1, 0 }),
                new("ceo", 0, 1, 3, 1, new[] { 0, 0, 2 }),
                new("starter_office", 0, 1, 6, 3, new[] { 5, 5, 5 }),
                new("trading_floor", 0, 2, 0, 4, new[] { 5, 5, 5, 5 }),
                new("casino_room", 1, 1, 0, 3, new[] { 5, 5, 3, 0 }),
                new("marketing", 1, 1, 3, 2, new[] { 2, 2, 2, 2 }),
                new("open_plan", 1, 2, 0, 5, new[] { 1, 1, 1, 1, 1, 1, 1 }),
                new("trophy_hall", 2, 1, 0, 2, new[] { 5, 5, 5, 5 }),
                new("helipad", 2, 30, 4, 1, new[] { 3, 3 }),
                new("casino_room", 3, 60, 0, 0, new[] { 1, 0, 0, 0 })   // level 0 = level 1
            },
            Buildings = new() { ("hq", 12), ("annex", 6), ("tower", 30), ("skyscraper", 60) },
            Skybridges = new() { (0, 3), (1, 4), (2, 20) },
            BuildBudget = 12_345_678.9
        };
        double expected = 150 + 300 + 15 * 150                       // lobby, CEO office, the first office (furniture only, any level)
                        + 1500 * 6 * 2.3 + 20 * 150                  // trading floor, level 4, maxed
                        + 2500 * (1 + 0.25 * 13) * 1.8 + 13 * 150    // casino room, level 3
                        + 1000 * (1 + 0.25 * 8) * 1.4 + 8 * 150      // marketing, level 2
                        + 7 * 150                                    // open-plan office
                        + 60000 * 6 * 1.4 + 20 * 150                 // trophy hall, level 2, maxed
                        + 6 * 150                                    // helipad
                        + 2500 * 1.25 + 150;                         // the skyscraper's casino room, level 1
        Assert.That(TowerIncome(save), Is.EqualTo(expected).Within(1e-6));

        // The budget, the buildings and the skybridges add nothing: the same rooms in one building give the same income
        var hqOnly = save.With(s =>
        {
            s.Buildings = null;
            s.Skybridges = null;
            s.BuildBudget = 1e300;
        });
        Assert.That(TowerIncome(hqOnly), Is.EqualTo(expected).Within(1e-6));
    }

    [Test]
    public void Format4_RoomLevels_ScaleEveryIncomeRoom()
    {
        (string Id, double Base, double[] Multipliers)[] incomeRooms =
        {
            ("trading_floor", 1500, new[] { 1.0, 1.4, 1.8, 2.3 }),
            ("casino_room", 2500, new[] { 1.0, 1.4, 1.8, 2.3 }),
            ("marketing", 1000, new[] { 1.0, 1.4, 1.8, 2.3 }),
            ("trophy_hall", 60000, new[] { 1.0, 1.4, 1.8 })
        };
        foreach (var room in incomeRooms)
        {
            for (int level = 1; level <= room.Multipliers.Length + 1; level++)
            {
                double multiplier = room.Multipliers[Math.Min(level, room.Multipliers.Length) - 1];
                foreach (int[] furniture in new[] { new[] { 0, 0, 0, 0 }, new[] { 5, 5, 5, 5 }, new[] { 3, 1, 4, 1 } })
                {
                    int sum = furniture.Sum();
                    double income = TowerIncome(CampusSave(new CampusRoom(room.Id, 1, 1, 0, level, furniture)));
                    Assert.That(income, Is.EqualTo(room.Base * (1 + 0.25 * sum) * multiplier + sum * 150).Within(1e-6), room.Id + " at level " + level);
                    // Never below what the game pays (float multipliers, $100 a level)
                    Assert.That(income, Is.GreaterThanOrEqualTo(GameIncome(room.Base, sum, (float)multiplier)));
                }
            }
        }
    }

    [Test]
    public void PerCompanyLimits_ASecondTrophyHallAddsOnlyItsFurniture()
    {
        int[] maxed = { 5, 5, 5, 5 };
        var one = CampusSave(new CampusRoom("trophy_hall", 1, 1, 0, 3, maxed));
        var two = CampusSave(new CampusRoom("trophy_hall", 1, 1, 0, 3, maxed), new CampusRoom("trophy_hall", 2, 1, 0, 3, maxed));
        Assert.That(TowerIncome(one), Is.EqualTo(651_000).Within(1e-6));
        Assert.That(TowerIncome(two), Is.EqualTo(651_000 + 20 * 150).Within(1e-6), "the second one earns its furniture's $3,000, not $650,000");

        // The best one counts, wherever it is in the list
        var bareFirst = CampusSave(new CampusRoom("trophy_hall", 1, 1, 0, 1, new[] { 0, 0, 0, 0 }), new CampusRoom("trophy_hall", 2, 1, 0, 3, maxed));
        Assert.That(TowerIncome(bareFirst), Is.EqualTo(651_000).Within(1e-6));

        // Marketing: two at full income (the best two), a third at its furniture's
        var marketing = CampusSave(new CampusRoom("marketing", 1, 1, 0, 1, new[] { 1, 1, 1, 1 }), new CampusRoom("marketing", 1, 1, 4, 4, maxed),
                                   new CampusRoom("marketing", 1, 2, 0, 4, maxed));
        Assert.That(TowerIncome(marketing), Is.EqualTo(2 * 16_800 + 4 * 150).Within(1e-6));

        // Rooms without a limit all count
        var casinos = CampusSave(Enumerable.Range(0, 5).Select(i => new CampusRoom("casino_room", 1, 1 + i, 0, 4, maxed)).ToArray());
        Assert.That(TowerIncome(casinos), Is.EqualTo(5 * 37_500).Within(1e-6));
    }

    [Test]
    public void PerCompanyLimits_InTheAllowance()
    {
        int[] maxed = { 5, 5, 5, 5 };
        var campus = CampusSave(new CampusRoom("lobby", 0, 1, 0, 1, new[] { 0, 0 }), new CampusRoom("casino_room", 0, 2, 0, 4, maxed));
        var withTrophy = campus.With(s => s.Campus.Add(new CampusRoom("trophy_hall", 1, 1, 0, 3, maxed)));
        var withTwo = withTrophy.With(s => s.Campus.Add(new CampusRoom("trophy_hall", 2, 1, 0, 3, maxed)));
        double Operating(SaveBuilder b)
        {
            SaveSnapshot s = SaveSnapshot.Parse(b.Json());
            return SaveRules.IncomeAllowance(s, s, 100, 1, 45, 0).OperatingMax;
        }
        Assert.That(Operating(withTrophy) - Operating(campus), Is.EqualTo(651_000).Within(1e-6));
        Assert.That(Operating(withTwo) - Operating(withTrophy), Is.EqualTo(3_000).Within(1e-6));
    }

    [Test]
    public void TheRoomCap_CountsAFullCampus_AndNoMore()
    {
        // The most rooms an honest campus holds (1,421 two-tile rooms, see SaveCalibration.TowerMaxRooms) all count
        Assert.That(SaveCalibration.TowerMaxRooms, Is.GreaterThanOrEqualTo(1421));
        int[] furnished = { 5, 5, 5 };
        CampusRoom Small(int i) => new("small_office", i % 9, 1 + i / 54, (i % 6) * 2, 3, furnished);
        Assert.That(TowerIncome(CampusSave(Enumerable.Range(0, 1421).Select(Small).ToArray())), Is.EqualTo(1421 * 15 * 150.0).Within(1e-6));
        // Rooms past the cap earn nothing
        Assert.That(TowerIncome(CampusSave(Enumerable.Range(0, 1600).Select(Small).ToArray())),
                    Is.EqualTo(SaveCalibration.TowerMaxRooms * 15 * 150.0).Within(1e-6));
    }

    [Test]
    public void AFullCampus_FitsTheTokenBudget_AndTheSizeLimits()
    {
        // Every tile built with the smallest rooms, every one with seven pieces (levels varied, as a real campus is), all nine
        // buildings at their top floor, the most skybridges (two per neighbouring pair): the biggest "building" extra
        var buildings = new List<(string Type, int Floors)> { ("hq", 36), ("annex", 6), ("annex", 6), ("annex", 6), ("tower", 30), ("tower", 30), ("tower", 30), ("skyscraper", 60), ("skyscraper", 60) };
        var bridges = Enumerable.Range(0, buildings.Count - 1).SelectMany(i => new[] { (i, 2), (i, 3) }).ToList();
        var rng = new Random(5);
        var save = new SaveBuilder
        {
            Campus = Enumerable.Range(0, 1421).Select(i => new CampusRoom(i < 3 ? new[] { "lobby", "ceo", "starter_office" }[i] : "open_plan", i % 9, 1 + i / 54, (i % 6) * 2,
                                                                          1 + rng.Next(5), Enumerable.Range(0, 7).Select(_ => rng.Next(6)).ToArray())).ToList(),
            Buildings = buildings,
            Skybridges = bridges,
            BuildBudget = 987_654_321.123
        };
        string json = save.Json();
        SaveSnapshot parsed = SaveSnapshot.Parse(json, 60_000);
        Assert.That(parsed.Ok, Is.True, "a full campus takes under 60,000 of the save's " + SaveSnapshot.MaxTokens.ToString("N0", Inv) + " tokens: " + parsed.Error);

        int bytes = System.Text.Encoding.UTF8.GetByteCount(json);
        int packed = SavePacking.Pack(json).Length;
        TestContext.Out.WriteLine($"A full campus: {bytes:N0} bytes of save JSON ({packed:N0} packed) with {save.Campus.Count:N0} rooms.");
        Assert.That(bytes, Is.LessThan(SaveCloud.MaxSaveBytes / 8), "well within the save limit");
        Assert.That(packed, Is.LessThan(SaveCloud.MaxRequestBytes / 4), "leaves most of the request for the staff");

        // With thousands of employees it still parses under the normal budget
        var staffed = save.With(s => s.Employees = T.Staff(6000));
        Assert.That(SaveSnapshot.Parse(staffed.Json()).Ok, Is.True);
    }

    [Test]
    public void Format3_ReadsAsBefore_AndAsFormat4AtLevelOne()
    {
        var rooms = new List<(string Id, int[] Levels)>
        {
            ("lobby", new[] { 0, 0 }), ("ceo", new[] { 1, 0, 0 }), ("trading_floor", new[] { 5, 5, 5, 5 }), ("casino_room", new[] { 2, 2, 2, 2 }),
            ("small_office", new[] { 1, 1, 1 }), ("meeting_room", new[] { 4, 4, 4 })
        };
        var tower = new SaveBuilder { Rooms = rooms };   // format 3: no buildings, no room levels
        double expected = 150 + 1500 * 6 + 20 * 150 + 2500 * 3 + 8 * 150 + 3 * 150 + 12 * 150;
        Assert.That(TowerIncome(tower), Is.EqualTo(expected).Within(1e-6), "the income format 3 always had (room level 1)");

        var campus = new SaveBuilder { Campus = rooms.Select((r, i) => new CampusRoom(r.Id, 0, 1 + i / 2, (i % 2) * 4, i % 2, r.Levels)).ToList() };
        Assert.That(TowerIncome(campus), Is.EqualTo(expected).Within(1e-6), "format 4 at levels 0 and 1");
    }

    // ================================================================================================================
    // The growth check
    // ================================================================================================================

    [Test]
    public void CampusIncomeAtTheGamesMaximum_EveryDay_IsCovered()
    {
        // A late-game campus earning the most the game pays, every day: 20 casino rooms, 10 trading floors, both marketing
        // rooms (all at level 4 with every piece at Ultimate) and the trophy hall at level 3, maxed. The company's value is low
        // (the build gamble put it into the build budget), so daily luck, the other big part of the allowance, is small, and
        // there are no staff: the campus has to explain nearly all of it. Every day: the campus and the sofa (level 5) x every
        // growth skill (x5.2625: CompanyManager.ApplyDailySkillEffects), plus perfect chaos luck.
        int[] maxed = { 5, 5, 5, 5 };
        var rooms = new List<CampusRoom> { new("lobby", 0, 1, 0, 1, new[] { 0, 0 }), new("ceo", 0, 1, 3, 1, new[] { 0, 0, 0 }), new("starter_office", 0, 1, 6, 1, new[] { 0, 0, 0 }) };
        for (int i = 0; i < 20; i++) rooms.Add(new CampusRoom("casino_room", 4, 2 + i / 3, (i % 3) * 3, 4, maxed));
        for (int i = 0; i < 10; i++) rooms.Add(new CampusRoom("trading_floor", 1, 1 + i / 4, (i % 4) * 3, 4, maxed));
        rooms.Add(new CampusRoom("marketing", 2, 1, 0, 4, maxed));
        rooms.Add(new CampusRoom("marketing", 2, 1, 4, 4, maxed));
        rooms.Add(new CampusRoom("trophy_hall", 3, 1, 0, 3, maxed));
        double campusPerDay = 20 * GameIncome(2500, 20, 2.3f) + 10 * GameIncome(1500, 20, 2.3f) + 2 * GameIncome(1000, 20, 2.3f) + GameIncome(60000, 20, 1.8f);
        Assert.That(campusPerDay, Is.EqualTo(1_638_600).Within(1));

        double value = 100_000;
        for (int day = 301; day <= 305; day++)
            value += (campusPerDay + 5 * 100) * 5.2625 + SaveCalibration.DailyLuckMax(value, 0, SaveCalibration.IsKarmaDay(day));
        var before = new SaveBuilder
        {
            Value = 100_000, Day = 300, SofaLevel = 5, MeetingsHeld = 30, Campus = rooms,
            Buildings = new() { ("hq", 1), ("annex", 3), ("annex", 2), ("annex", 1), ("tower", 8) }, Skybridges = new() { (1, 2) }
        };
        var after = before.With(s =>
        {
            s.Value = value;
            s.Day = 305;
        });
        SubmitDecision d = SaveRules.EvaluateSubmit(T.Meta(day: 300, value: 100_000), T.Req(2, 1, after), T.City(), T.T0 + 5 * 45,
                                                    new SaveSubmitContext { PreviousSaveJson = before.Json() });
        Assert.That(d.Accepted, Is.True, T.Why(d));
        Assert.That(d.Meta.Flagged, Is.False, T.Why(d));
        Assert.That(d.Growth.Allowance.OperatingMax, Is.GreaterThan(campusPerDay), "the campus is in the daily bound, room levels included");
        TestContext.Out.WriteLine($"$100,000 -> {value:N0} in 5 days; allowance {d.Growth.Allowance.Total:N0} (daily bound {d.Growth.Allowance.OperatingMax:N0}).");
    }

    // ================================================================================================================
    // Helpers
    // ================================================================================================================

    /// <summary>A file of the Unity project the module sits in (walking up from the test's folder), or null.</summary>
    static string FindGameFile(params string[] parts)
    {
        string relative = Path.Combine(parts);
        for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir != null; dir = dir.Parent)
        {
            string path = Path.Combine(dir.FullName, relative);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    static string Between(string text, string start, string end)
    {
        int from = text.IndexOf(start, StringComparison.Ordinal);
        int to = from >= 0 ? text.IndexOf(end, from, StringComparison.Ordinal) : -1;
        Assert.That(from >= 0 && to > from, Is.True, "'" + start + "' ... '" + end + "' not found");
        return text.Substring(from, to - from);
    }

    static double Number(string text, string pattern, double? fallback = null)
    {
        Match m = Regex.Match(text, pattern);
        if (!m.Success)
        {
            Assert.That(fallback.HasValue, Is.True, "'" + pattern + "' not found");
            return fallback.Value;
        }
        return double.Parse(m.Groups[1].Value, Inv);
    }
}
