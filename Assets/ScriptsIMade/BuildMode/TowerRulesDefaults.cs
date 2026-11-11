using System.Collections.Generic;

/// <summary>
/// The built-in content of the HQ campus (TowerRules.CreateDefault). Every number is a proposal to tune here (or in a
/// TowerCatalog asset). The owner's decisions (2026-09-29): construction is paid from the build budget that only gambling
/// fills; capacity is a hard limit on hiring; upkeep is a daily company-money cost.
/// Pacing it was balanced against: $50k start, ~$1M after 20-40 min, ~$1B after 3.5-10 h, $10B wins. Roughly:
/// - Minutes 0-10: the lobby's receptionist and the first office (5) = 6 places; the first OFFICE FULL comes after a
///   few hires. A $5k-10k coin flip buys HQ floor 2 ($10,000) and a small office ($5,000), or the first office's level 2.
/// - Up to $1M: HQ floors ($10k x 1.42 per floor), small / big offices and their levels, the break and meeting rooms.
/// - $300k-$10M: the annex (12 tiles, 6 floors), open-plan offices, departments, the cafeteria, the office tower ($1M).
/// - $10M-$1B: cubicle farms, data centers, research, towers up to 30 floors, skybridges, rooftop lounges.
/// - $100M+: skyscrapers (60 floors), the helipad; $500M+: the trophy hall.
/// Room levels list the level's totals (capacity 13 = the room holds 13 at that level) and multiply the room's effects.
/// </summary>
public partial class TowerRules
{
    static TowerRoomLevel Lv(double cost, int capacity, float multiplier, double upkeep, int look)
    {
        return new TowerRoomLevel(cost, capacity, multiplier, upkeep, look);
    }

    static List<TowerRoomLevel> Levels(params TowerRoomLevel[] levels)
    {
        return new List<TowerRoomLevel>(levels);
    }

    static List<TowerFurnitureSlot> Slots(params TowerFurnitureSlot[] slots)
    {
        return new List<TowerFurnitureSlot>(slots);
    }

    static TowerFurnitureSlot S(string name, TowerFurnitureArt art, int viewOrder)
    {
        return new TowerFurnitureSlot(name, art, viewOrder);
    }

    static List<string> Only(params string[] buildingTypeIds)
    {
        return new List<string>(buildingTypeIds);
    }

    /// <summary>Every room: the design's nine (ids unchanged) and the campus's seventeen.</summary>
    public static List<TowerRoomDefinition> DefaultRooms()
    {
        const TowerFurnitureArt Desk = TowerFurnitureArt.Desk, Plant = TowerFurnitureArt.Plant, Coffee = TowerFurnitureArt.CoffeeMachine;
        return new List<TowerRoomDefinition>
        {
            // ---------------- Built in ----------------
            new TowerRoomDefinition
            {
                id = "lobby", displayName = "Lobby", role = TowerRoomRole.Lobby, tiles = 3, cost = 0,
                isFixed = true, fixedFloor = 1, fixedTile = 0, capacity = 1, category = TowerRoomCategory.BuiltIn,
                effectText = "Applicants wait here",
                description = "Your front door. The first hire runs the reception desk.",
                paper = Hex(0xF7F2E6),
                slots = Slots(S("Reception desk", TowerFurnitureArt.ReceptionDesk, 1), S("Plant", Plant, 0))
            },
            new TowerRoomDefinition
            {
                id = "ceo", displayName = "CEO office", role = TowerRoomRole.CeoOffice, tiles = 3, cost = 0,
                isFixed = true, fixedFloor = 1, fixedTile = 3, category = TowerRoomCategory.BuiltIn,
                effectText = "Your desk: the office view",
                description = "Where you hire, gamble and decide. It can't be moved or sold.",
                paper = Hex(0xE7C98C),
                slots = Slots(S("Sofa", TowerFurnitureArt.Sofa, 1), S("Bookshelf", TowerFurnitureArt.Bookshelf, 0), S("Aquarium", TowerFurnitureArt.Aquarium, 2))
            },
            new TowerRoomDefinition
            {
                id = "starter_office", displayName = "First office", role = TowerRoomRole.StarterOffice, tiles = 2, cost = 0,
                isFixed = true, isStarter = true, fixedFloor = 1, fixedTile = 6, capacity = 5, upkeep = 20, category = TowerRoomCategory.Office,
                description = "Where it all started. It grows with you, but it never moves.",
                paper = Hex(0xFFE9A8),
                slots = Slots(S("Desk", Desk, 1), S("Plant", Plant, 0), S("Coffee machine", Coffee, 2)),
                levels = Levels(Lv(8000, 7, 1f, 30, 1), Lv(20000, 9, 1f, 40, 2), Lv(60000, 11, 1f, 55, 3))
            },

            // ---------------- Offices (capacity) ----------------
            new TowerRoomDefinition
            {
                id = "small_office", displayName = "Small office", role = TowerRoomRole.Office, tiles = 2, cost = 5000,
                capacity = 5, upkeep = 25, category = TowerRoomCategory.Office,
                description = "A desk, a plant and a coffee machine for a small team.",
                paper = Hex(0xBFE3F5),
                slots = Slots(S("Desk", Desk, 1), S("Plant", Plant, 0), S("Coffee machine", Coffee, 2)),
                levels = Levels(Lv(8000, 7, 1f, 35, 1), Lv(20000, 9, 1f, 45, 2))
            },
            new TowerRoomDefinition
            {
                id = "big_office", displayName = "Big office", role = TowerRoomRole.Office, tiles = 4, cost = 15000,
                capacity = 10, upkeep = 60, category = TowerRoomCategory.Office,
                description = "Four desks for two people each, and room for two more.",
                paper = Hex(0xBFE3F5),
                slots = Slots(S("Desk 1", Desk, 1), S("Desk 2", Desk, 2), S("Desk 3", Desk, 3), S("Desk 4", Desk, 4), S("Plant", Plant, 0),
                              S("Coffee machine", Coffee, 5)),
                levels = Levels(Lv(25000, 13, 1f, 80, 1), Lv(60000, 16, 1f, 100, 2), Lv(150000, 20, 1f, 130, 3))
            },
            new TowerRoomDefinition
            {
                id = "open_plan", displayName = "Open-plan office", role = TowerRoomRole.OpenPlan, tiles = 6, cost = 120000,
                capacity = 24, upkeep = 250, category = TowerRoomCategory.Office,
                requires = new TowerRequirements(peakValue: 400000),
                description = "Rows of desks, one long coffee queue. Two dozen people and a whiteboard nobody erases.",
                paper = Hex(0xC9E7F7),
                slots = Slots(S("Desk 1", Desk, 1), S("Desk 2", Desk, 2), S("Desk 3", Desk, 3), S("Desk 4", Desk, 4), S("Plant", Plant, 0),
                              S("Coffee machine", Coffee, 5), S("Whiteboard", TowerFurnitureArt.Whiteboard, 6)),
                levels = Levels(Lv(200000, 30, 1f, 320, 1), Lv(450000, 37, 1f, 400, 2), Lv(1100000, 45, 1f, 500, 2), Lv(2700000, 55, 1f, 620, 3))
            },
            new TowerRoomDefinition
            {
                id = "cubicle_farm", displayName = "Cubicle farm", role = TowerRoomRole.CubicleFarm, tiles = 8, cost = 3000000,
                capacity = 60, upkeep = 1500, category = TowerRoomCategory.Office,
                requires = new TowerRequirements(statusTier: 2),
                description = "A whole floor of grey partitions. Sixty people, sixty tiny plants.",
                paper = Hex(0xD5DDE8),
                slots = Slots(S("Cubicle 1", TowerFurnitureArt.Cubicle, 1), S("Cubicle 2", TowerFurnitureArt.Cubicle, 2), S("Cubicle 3", TowerFurnitureArt.Cubicle, 3),
                              S("Cubicle 4", TowerFurnitureArt.Cubicle, 4), S("Plant", Plant, 0), S("Coffee machine", Coffee, 5)),
                levels = Levels(Lv(5000000, 75, 1f, 1900, 1), Lv(12000000, 92, 1f, 2400, 2), Lv(30000000, 112, 1f, 3000, 2), Lv(75000000, 135, 1f, 3700, 3))
            },

            // ---------------- Employee facilities ----------------
            new TowerRoomDefinition
            {
                id = "break_room", displayName = "Break room", role = TowerRoomRole.BreakRoom, tiles = 2, cost = 8000,
                happinessPercent = 5f, upkeep = 40, category = TowerRoomCategory.Facility,
                description = "A sofa, coffee and an arcade cabinet. Staff come back happier.",
                paper = Hex(0xFFC7D1),
                slots = Slots(S("Sofa", TowerFurnitureArt.Sofa, 0), S("Coffee machine", Coffee, 1), S("Arcade cabinet", TowerFurnitureArt.ArcadeCabinet, 2)),
                levels = Levels(Lv(20000, 0, 1.4f, 60, 1), Lv(60000, 0, 1.8f, 85, 2))
            },
            new TowerRoomDefinition
            {
                id = "cafeteria", displayName = "Cafeteria", role = TowerRoomRole.Cafeteria, tiles = 4, cost = 150000,
                happinessPercent = 7f, productivityPercent = 1f, upkeep = 450, maxPerBuilding = 1, category = TowerRoomCategory.Facility,
                requires = new TowerRequirements(peakValue: 250000),
                description = "Hot lunch on the house. Nobody leaves the building to eat, or at all.",
                paper = Hex(0xFFD8A8),
                slots = Slots(S("Food counter", TowerFurnitureArt.FoodCounter, 0), S("Big table", TowerFurnitureArt.BigTable, 1), S("Coffee machine", Coffee, 2),
                              S("Plant", Plant, 3)),
                levels = Levels(Lv(350000, 0, 1.4f, 650, 1), Lv(900000, 0, 1.8f, 900, 2))
            },
            new TowerRoomDefinition
            {
                id = "gym", displayName = "Gym", role = TowerRoomRole.Gym, tiles = 3, cost = 400000,
                happinessPercent = 4f, productivityPercent = 3f, upkeep = 500, maxPerBuilding = 1, category = TowerRoomCategory.Facility,
                requires = new TowerRequirements(statusTier: 1),
                description = "Treadmills with a view. Fitter staff, fewer sick days.",
                paper = Hex(0xD9F2E6),
                slots = Slots(S("Treadmill", TowerFurnitureArt.Treadmill, 0), S("Weights", TowerFurnitureArt.Weights, 1), S("Plant", Plant, 2)),
                levels = Levels(Lv(1000000, 0, 1.4f, 700, 1), Lv(2500000, 0, 1.8f, 1000, 2))
            },

            // ---------------- Executive ----------------
            new TowerRoomDefinition
            {
                id = "meeting_room", displayName = "Meeting room", role = TowerRoomRole.MeetingRoom, tiles = 3, cost = 12000,
                trustPercent = 5f, upkeep = 40, category = TowerRoomCategory.Executive,
                description = "Charts, a projector and a big table. Investors like a good meeting.",
                paper = Hex(0xFFE680),
                slots = Slots(S("Big table", TowerFurnitureArt.BigTable, 1), S("Whiteboard", TowerFurnitureArt.Whiteboard, 0), S("Projector", TowerFurnitureArt.Projector, 2)),
                levels = Levels(Lv(30000, 0, 1.4f, 60, 1), Lv(90000, 0, 1.8f, 85, 2))
            },
            new TowerRoomDefinition
            {
                id = "big_meeting_room", displayName = "Big meeting room", role = TowerRoomRole.BigMeetingRoom, tiles = 5, cost = 500000,
                trustPercent = 9f, productivityPercent = 1f, upkeep = 400, maxPerCompany = 2, category = TowerRoomCategory.Executive,
                requires = new TowerRequirements(statusTier: 1, room: "meeting_room"),
                description = "Two tables, one projector and a view. Where the big decisions get postponed.",
                paper = Hex(0xFFF0A0),
                slots = Slots(S("Big table", TowerFurnitureArt.BigTable, 1), S("Whiteboard", TowerFurnitureArt.Whiteboard, 0), S("Projector", TowerFurnitureArt.Projector, 2),
                              S("Plant", Plant, 3)),
                levels = Levels(Lv(1200000, 0, 1.4f, 600, 1), Lv(3000000, 0, 1.8f, 850, 2))
            },
            new TowerRoomDefinition
            {
                id = "executive_suite", displayName = "Executive suite", role = TowerRoomRole.ExecutiveSuite, tiles = 3, cost = 300000,
                capacity = 3, trustPercent = 6f, upkeep = 350, maxPerCompany = 3, floorRule = TowerFloorRule.MinFloor, minFloor = 4,
                category = TowerRoomCategory.Executive, requires = new TowerRequirements(statusTier: 1),
                description = "Leather chairs high above the street. Investors trust a corner office.",
                paper = Hex(0xE9D3A6),
                slots = Slots(S("Sofa", TowerFurnitureArt.Sofa, 1), S("Bookshelf", TowerFurnitureArt.Bookshelf, 0), S("Bar", TowerFurnitureArt.Bar, 2)),
                levels = Levels(Lv(700000, 4, 1.4f, 500, 1), Lv(1800000, 5, 1.8f, 700, 2))
            },

            // ---------------- Departments ----------------
            new TowerRoomDefinition
            {
                id = "marketing", displayName = "Marketing", role = TowerRoomRole.Marketing, tiles = 4, cost = 250000,
                capacity = 5, productivityPercent = 4f, dailyIncome = 1000, upkeep = 400, maxPerCompany = 2, category = TowerRoomCategory.Department,
                requires = new TowerRequirements(peakValue: 250000),
                description = "Slogans, charts and one very confident whiteboard. Sales go up.",
                paper = Hex(0xFFB8A8),
                slots = Slots(S("Desk 1", Desk, 1), S("Desk 2", Desk, 2), S("Whiteboard", TowerFurnitureArt.Whiteboard, 0), S("Ticker board", TowerFurnitureArt.TickerBoard, 3)),
                levels = Levels(Lv(600000, 6, 1.4f, 550, 1), Lv(1500000, 7, 1.8f, 750, 2), Lv(4000000, 9, 2.3f, 1000, 3))
            },
            new TowerRoomDefinition
            {
                id = "hr_department", displayName = "HR department", role = TowerRoomRole.HrDepartment, tiles = 3, cost = 180000,
                capacity = 4, happinessPercent = 4f, trustPercent = 1f, upkeep = 300, maxPerCompany = 1, category = TowerRoomCategory.Department,
                requires = new TowerRequirements(peakValue: 150000),
                description = "Someone finally reads the complaints box. Happier staff.",
                paper = Hex(0xE8D5F0),
                slots = Slots(S("Desk", Desk, 1), S("Filing cabinet", TowerFurnitureArt.FilingCabinet, 2), S("Plant", Plant, 0)),
                levels = Levels(Lv(450000, 5, 1.4f, 420, 1), Lv(1100000, 6, 1.8f, 600, 2))
            },
            new TowerRoomDefinition
            {
                id = "legal_department", displayName = "Legal department", role = TowerRoomRole.LegalDepartment, tiles = 3, cost = 600000,
                capacity = 4, trustPercent = 4f, upkeepSavingsPercent = 8f, upkeep = 500, maxPerCompany = 1, category = TowerRoomCategory.Department,
                requires = new TowerRequirements(statusTier: 1),
                description = "Contracts, permits and cheaper maintenance deals. Investors sleep better.",
                paper = Hex(0xD8CFC0),
                slots = Slots(S("Desk", Desk, 1), S("Bookshelf", TowerFurnitureArt.Bookshelf, 0), S("Filing cabinet", TowerFurnitureArt.FilingCabinet, 2)),
                levels = Levels(Lv(1500000, 5, 1.4f, 700, 1), Lv(3600000, 6, 1.8f, 1000, 2))
            },
            new TowerRoomDefinition
            {
                id = "accounting", displayName = "Accounting", role = TowerRoomRole.Accounting, tiles = 3, cost = 350000,
                capacity = 4, salarySavingsPercent = 3f, upkeepSavingsPercent = 8f, upkeep = 350, maxPerCompany = 1, category = TowerRoomCategory.Department,
                requires = new TowerRequirements(peakValue: 500000),
                description = "Spreadsheets that find money. Salaries and upkeep cost a little less.",
                paper = Hex(0xDDEFD0),
                slots = Slots(S("Terminal 1", TowerFurnitureArt.Terminal, 0), S("Terminal 2", TowerFurnitureArt.Terminal, 1), S("Filing cabinet", TowerFurnitureArt.FilingCabinet, 2)),
                levels = Levels(Lv(900000, 5, 1.4f, 500, 1), Lv(2200000, 6, 1.8f, 700, 2))
            },

            // ---------------- Research and training (knowledge) ----------------
            new TowerRoomDefinition
            {
                id = "research_lab", displayName = "Research lab", role = TowerRoomRole.ResearchLab, tiles = 4, cost = 800000,
                capacity = 5, knowledgePerDay = 0.04f, productivityPercent = 1f, upkeep = 800, maxPerCompany = 2, category = TowerRoomCategory.Research,
                requires = new TowerRequirements(statusTier: 1),
                description = "Lab coats and big ideas. Earns skill points without a single hire.",
                paper = Hex(0xCFEFF5),
                slots = Slots(S("Lab bench 1", TowerFurnitureArt.LabBench, 0), S("Lab bench 2", TowerFurnitureArt.LabBench, 1), S("Server rack", TowerFurnitureArt.ServerRack, 2),
                              S("Whiteboard", TowerFurnitureArt.Whiteboard, 3)),
                levels = Levels(Lv(2000000, 6, 1.4f, 1100, 1), Lv(5000000, 7, 1.8f, 1500, 2), Lv(12000000, 8, 2.3f, 2000, 3))
            },
            new TowerRoomDefinition
            {
                id = "training_center", displayName = "Training center", role = TowerRoomRole.TrainingCenter, tiles = 4, cost = 300000,
                capacity = 4, knowledgePerDay = 0.015f, productivityPercent = 2f, upkeep = 400, maxPerCompany = 2, category = TowerRoomCategory.Research,
                requires = new TowerRequirements(peakValue: 300000),
                description = "Workshops with free snacks. Staff get better, slowly.",
                paper = Hex(0xF5E6C8),
                slots = Slots(S("Big table", TowerFurnitureArt.BigTable, 1), S("Projector", TowerFurnitureArt.Projector, 2), S("Whiteboard", TowerFurnitureArt.Whiteboard, 0),
                              S("Bookshelf", TowerFurnitureArt.Bookshelf, 3)),
                levels = Levels(Lv(750000, 5, 1.4f, 550, 1), Lv(1900000, 6, 1.8f, 750, 2))
            },

            // ---------------- Utility ----------------
            new TowerRoomDefinition
            {
                id = "server_room", displayName = "Server room", role = TowerRoomRole.ServerRoom, tiles = 2, cost = 20000,
                productivityPercent = 3f, upkeep = 100, category = TowerRoomCategory.Utility,
                description = "Blinking lights that make everyone a little faster.",
                paper = Hex(0xCDEBA6),
                slots = Slots(S("Server rack 1", TowerFurnitureArt.ServerRack, 0), S("Server rack 2", TowerFurnitureArt.ServerRack, 1), S("Cooling unit", TowerFurnitureArt.CoolingUnit, 2)),
                levels = Levels(Lv(50000, 0, 1.4f, 140, 1), Lv(150000, 0, 1.8f, 190, 2))
            },
            new TowerRoomDefinition
            {
                id = "data_center", displayName = "Data center", role = TowerRoomRole.DataCenter, tiles = 6, cost = 8000000,
                productivityPercent = 8f, upkeep = 4000, floorRule = TowerFloorRule.GroundOnly, maxPerBuilding = 1,
                allowedBuildings = Only("annex", "tower", "skyscraper"), category = TowerRoomCategory.Utility,
                requires = new TowerRequirements(statusTier: 2, room: "server_room"),
                description = "Racks, cold air and a humming floor. Everything runs faster. Ground floor only: it's heavy.",
                paper = Hex(0xB8D4B0),
                slots = Slots(S("Server rack 1", TowerFurnitureArt.ServerRack, 0), S("Server rack 2", TowerFurnitureArt.ServerRack, 1), S("Server rack 3", TowerFurnitureArt.ServerRack, 2),
                              S("Server rack 4", TowerFurnitureArt.ServerRack, 3), S("Cooling unit", TowerFurnitureArt.CoolingUnit, 4), S("Terminal 1", TowerFurnitureArt.Terminal, 5)),
                levels = Levels(Lv(20000000, 0, 1.4f, 5500, 1), Lv(50000000, 0, 1.8f, 7500, 2))
            },

            // ---------------- Money ----------------
            new TowerRoomDefinition
            {
                id = "trading_floor", displayName = "Trading floor", role = TowerRoomRole.TradingFloor, tiles = 3, cost = 40000,
                capacity = 3, dailyIncome = 1500, upkeep = 150, unlock = TowerUnlock.StockMarket, category = TowerRoomCategory.Money,
                lockReason = "Needs Stock Market Access (Growth, tier 3)",
                description = "Traders at their terminals, earning a little every day.",
                paper = Hex(0xF1ECDD),
                slots = Slots(S("Terminal 1", TowerFurnitureArt.Terminal, 0), S("Terminal 2", TowerFurnitureArt.Terminal, 2), S("Terminal 3", TowerFurnitureArt.Terminal, 3),
                              S("Ticker board", TowerFurnitureArt.TickerBoard, 1)),
                levels = Levels(Lv(120000, 3, 1.4f, 220, 1), Lv(400000, 4, 1.8f, 320, 2), Lv(1400000, 5, 2.3f, 450, 3))
            },
            new TowerRoomDefinition
            {
                id = "casino_room", displayName = "Casino room", role = TowerRoomRole.CasinoRoom, tiles = 3, cost = 50000,
                dailyIncome = 2500, upkeep = 250, unlock = TowerUnlock.CasinoLicense, category = TowerRoomCategory.Money,
                lockReason = "Needs Casino License (Chaos & Luck, tier 5)",
                description = "The house always wins. This time the house is you.",
                paper = Hex(0x1D5A3E), ink = Hex(0xFFFDF6),
                slots = Slots(S("Slot machine", TowerFurnitureArt.SlotMachine, 0), S("Card table", TowerFurnitureArt.CardTable, 2), S("Roulette wheel", TowerFurnitureArt.RouletteWheel, 1),
                              S("Bar", TowerFurnitureArt.Bar, 3)),
                levels = Levels(Lv(150000, 0, 1.4f, 360, 1), Lv(500000, 0, 1.8f, 520, 2), Lv(1800000, 0, 2.3f, 720, 3))
            },

            // ---------------- Prestige ----------------
            new TowerRoomDefinition
            {
                id = "rooftop_lounge", displayName = "Rooftop lounge", role = TowerRoomRole.RooftopLounge, tiles = 4, cost = 12000000,
                happinessPercent = 6f, trustPercent = 4f, upkeep = 2500, floorRule = TowerFloorRule.TopOnly, maxPerBuilding = 1,
                category = TowerRoomCategory.Prestige, requires = new TowerRequirements(statusTier: 3),
                description = "Parasols, cocktails and the skyline. Top floor only: it goes up with the roof.",
                paper = Hex(0xFFD1E1),
                slots = Slots(S("Parasol", TowerFurnitureArt.Parasol, 0), S("Sofa", TowerFurnitureArt.Sofa, 1), S("Bar", TowerFurnitureArt.Bar, 2), S("Plant", Plant, 3)),
                levels = Levels(Lv(30000000, 0, 1.4f, 3500, 1), Lv(75000000, 0, 1.8f, 5000, 2))
            },
            new TowerRoomDefinition
            {
                id = "helipad", displayName = "Helipad", role = TowerRoomRole.Helipad, tiles = 4, cost = 75000000,
                trustPercent = 12f, upkeep = 8000, floorRule = TowerFloorRule.TopOnly, maxPerCompany = 1,
                allowedBuildings = Only("tower", "skyscraper"), category = TowerRoomCategory.Prestige,
                requires = new TowerRequirements(statusTier: 4),
                description = "Arrive by helicopter. Investors never forget it. On the roof of a tower or skyscraper.",
                paper = Hex(0xC8CCD4),
                slots = Slots(S("Helicopter", TowerFurnitureArt.Helicopter, 0), S("Sofa", TowerFurnitureArt.Sofa, 1)),
                levels = Levels(Lv(180000000, 0, 1.4f, 12000, 2))
            },
            new TowerRoomDefinition
            {
                id = "trophy_hall", displayName = "Trophy hall", role = TowerRoomRole.TrophyHall, tiles = 4, cost = 300000000,
                trustPercent = 8f, happinessPercent = 3f, dailyIncome = 60000, upkeep = 15000, floorRule = TowerFloorRule.GroundOnly,
                maxPerCompany = 1, category = TowerRoomCategory.Prestige, requires = new TowerRequirements(statusTier: 5),
                description = "Every award you bought, lit from below. Tourists pay to see it. Ground floor only.",
                paper = Hex(0xF2D98A),
                slots = Slots(S("Trophy 1", TowerFurnitureArt.Trophy, 0), S("Trophy 2", TowerFurnitureArt.Trophy, 1), S("Trophy 3", TowerFurnitureArt.Trophy, 2), S("Plant", Plant, 3)),
                levels = Levels(Lv(700000000, 0, 1.4f, 22000, 2), Lv(1600000000, 0, 1.8f, 32000, 3))
            }
        };
    }

    /// <summary>The HQ and the buildings the company can buy (placed to the right of the HQ, in the order bought).</summary>
    public static List<TowerBuildingType> DefaultBuildingTypes()
    {
        return new List<TowerBuildingType>
        {
            new TowerBuildingType
            {
                id = HqTypeId, displayName = "Headquarters", facade = TowerFacade.Hq, isHeadquarters = true,
                tiles = 8, maxFloors = 36, price = 0, secondFloorPrice = 10000, floorPriceGrowth = 1.42, upkeepPerFloor = 15, maxCount = 1,
                tint = new UnityEngine.Color(1f, 1f, 1f, 0f),
                description = "The tower you started in. Eight tiles a floor, up to 36 floors."
            },
            new TowerBuildingType
            {
                id = "annex", displayName = "Annex", facade = TowerFacade.Annex,
                tiles = 12, maxFloors = 6, price = 400000, secondFloorPrice = 150000, floorPriceGrowth = 1.6, upkeepPerFloor = 60, maxCount = 3,
                tint = new UnityEngine.Color(1f, 0.9f, 0.75f, 0.06f),
                requires = new TowerRequirements(peakValue: 300000, hqFloors: 4),
                description = "Low and wide: twelve tiles a floor, up to six floors. Room for big open-plan offices."
            },
            new TowerBuildingType
            {
                id = "tower", displayName = "Office tower", facade = TowerFacade.Tower,
                tiles = 10, maxFloors = 30, price = 2500000, secondFloorPrice = 200000, floorPriceGrowth = 1.3, upkeepPerFloor = 200, maxCount = 3,
                tint = new UnityEngine.Color(0.8f, 0.95f, 1f, 0.06f),
                requires = new TowerRequirements(statusTier: 1),
                description = "Ten tiles a floor and room to climb: up to 30 floors."
            },
            new TowerBuildingType
            {
                id = "skyscraper", displayName = "Skyscraper", facade = TowerFacade.Skyscraper,
                tiles = 12, maxFloors = 60, price = 150000000, secondFloorPrice = 5000000, floorPriceGrowth = 1.085, upkeepPerFloor = 2000, maxCount = 2,
                tint = new UnityEngine.Color(0.85f, 0.85f, 1f, 0.07f),
                requires = new TowerRequirements(statusTier: 4),
                description = "The skyline's new tallest thing: up to 60 floors of twelve tiles."
            }
        };
    }

    /// <summary>The links between buildings: the skybridge (tunnels and the like can be added as more types).</summary>
    public static List<TowerConnectionType> DefaultConnectionTypes()
    {
        return new List<TowerConnectionType>
        {
            new TowerConnectionType
            {
                id = "skybridge", displayName = "Skybridge", kind = TowerConnectionKind.Skybridge,
                price = 150000, pricePerFloor = 20000, minFloor = 2, maxPerPair = 2, upkeep = 200,
                productivityPercent = 2f, happinessPercent = 1f,
                requires = new TowerRequirements(buildings: 2),
                description = "A glass walkway between neighbouring buildings. Nobody takes the lift down and up again."
            }
        };
    }
}
