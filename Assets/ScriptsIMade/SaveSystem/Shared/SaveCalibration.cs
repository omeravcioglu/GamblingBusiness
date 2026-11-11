using System;
using System.Collections.Generic;

/// <summary>
/// Every bound SaveRules checks a cloud save against, calibrated from the game code (file:line in the comments; paths are
/// under Assets/ScriptsIMade). Pure C# shared by the GameSave module and the Unity client (no UnityEngine).
/// Rule of thumb: each number is the most an HONEST player can reach (maxed skill tree, perfect luck, fastest clicking),
/// often with extra margin. A false flag on an honest player is worse than a missed cheat, so these are upper bounds,
/// not averages. When the game's numbers change, change them here too (the tests in GameSaveCCM check the maths).
/// </summary>
public static class SaveCalibration
{
    // ================================================================================================================
    // Time
    // ================================================================================================================

    /// <summary>Longest in-game day in seconds of running day clock (CompanyManager.cs:492, Random.Range(15f, 45f)).</summary>
    public const double MaxSecondsPerDay = 45.0;

    /// <summary>
    /// Seconds of running day clock between two saves that are <paramref name="days"/> in-game days apart: at most
    /// (days + 1) x 45 (the partial days at both ends). The desk, hiring tasks, crises and risk cards all wait while a
    /// panel is open, exactly like the day clock (CompanyManager.cs:403-411, GameFlowManager.cs:88-95,
    /// HiringTaskManager.Update :114-116, MiniGameManager.cs:405-408, EventManager.Update :197-203), so this bounds all of them.
    /// </summary>
    public static double RunSeconds(long days)
    {
        return (Math.Max(0L, days) + 1L) * MaxSecondsPerDay;
    }

    // ================================================================================================================
    // Employees (CompanyManager.EmployeeRevenue, CompanyManager.cs:810-847)
    // ================================================================================================================

    /// <summary>Daily output of an average employee (CompanyManager.cs:50).</summary>
    public const double BaseEmployeeRevenue = 250.0;
    /// <summary>CompanyManager.cs:39-41.</summary>
    public const double BaselineIQ = 100.0, BaselineGPA = 2.0, BaselineExperience = 5.0;

    /// <summary>
    /// Highest IQ an employee can have: golden applicants 145-169 (ApplicantGenerator.cs:115), normal ones at most 170
    /// after the hiring-pool bonus (ApplicantGenerator.cs:142-144), Lucky Hire at most 149 (CompanyManager.cs:1291).
    /// </summary>
    public const int MaxIQ = 170;
    /// <summary>GPA never goes above 4.0 (ApplicantGenerator.cs:116, :150; CompanyManager.cs:1292).</summary>
    public const double MaxGPA = 4.0;
    /// <summary>
    /// Experience: at most age - 16 = 48 through Skill Development (oldest applicant 64: ApplicantGenerator.cs:107;
    /// MIN_WORKING_AGE CompanyManager.cs:47, :1234), at hire at most 39 + 3 Onboarding years (ApplicantGenerator.cs:165,
    /// CompanyManager.cs:1006-1009, :1276-1279). 50 leaves margin.
    /// </summary>
    public const int MaxExperience = 50;

    /// <summary>
    /// Corporate Enlightenment multiplies every skill effect by 1.25 (SkillTreeManager.cs:630-633, :789-791; the bonus
    /// itself is capped at +100%, :43, so 2.0 is the hard ceiling if its value were ever raised).
    /// </summary>
    public const double SkillEffectMultiplier = 1.25;

    /// <summary>
    /// Largest employee output multiplier: productivity at its +150% cap (MAX_PRODUCTIVITY_BONUS, CompanyManager.cs:51,
    /// capped at :780; all output skills together are +166%) x the 100-happiness multiplier 1.15 (CompanyManager.cs:63)
    /// x the biggest Random Raise, 15% x 1.25 (CompanyManager.cs:85, :1079). Applied to positive output only (:831-832).
    /// </summary>
    public const double MaxOutputMultiplier = (1.0 + 1.5) * 1.15 * (1.0 + 0.15 * SkillEffectMultiplier);
    /// <summary>IQ Synergy: $50 per IQ point over 100 (SkillTreeManager.cs:322-325) x 1.25 (CompanyManager.cs:835-838).</summary>
    public const double IQSynergyPerPoint = 50.0 * SkillEffectMultiplier;
    /// <summary>PhD Appreciation: +15% x 1.25 on a PhD's positive output (SkillTreeManager.cs:328-331; CompanyManager.cs:841-844).</summary>
    public const double PhDBonus = 0.15 * SkillEffectMultiplier;
    /// <summary>Team Synergy: +1% x 1.25 of output per 5 employees (SkillTreeManager.cs:359-362; CompanyManager.cs:591-595).</summary>
    public const double TeamSynergyRate = 0.01 * SkillEffectMultiplier;
    public const int TeamSynergyGroup = 5;

    /// <summary>
    /// The most one employee can produce in a day, before Team Synergy: EmployeeRevenue (CompanyManager.cs:810-847) with
    /// the attributes clamped to what the game can generate and every multiplier at its maximum. Salaries, incidents and
    /// overhead are left out (an upper bound). Never negative.
    /// </summary>
    public static double EmployeeOutputMax(SaveEmployeeInfo e)
    {
        if (e == null) return 0.0;
        double iq = Clamp(e.IQ, 0, MaxIQ);
        double gpa = Clamp(e.GPA, 0.0, MaxGPA);
        double exp = Clamp(e.Experience, 0, MaxExperience);
        double productivity = 1.0 + (iq - BaselineIQ) / 100.0 + (gpa - BaselineGPA) / 4.0 + (exp - BaselineExperience) / 20.0;
        double revenue = BaseEmployeeRevenue * productivity;
        if (revenue > 0.0) revenue *= MaxOutputMultiplier;
        if (iq > BaselineIQ) revenue += (iq - BaselineIQ) * IQSynergyPerPoint;
        if (e.PhD && revenue > 0.0) revenue *= 1.0 + PhDBonus;
        return Math.Max(0.0, revenue);
    }

    /// <summary>The best possible employee (IQ 170, GPA 4.0, 50 years, PhD): for hires the server can't see (hired and fired between saves).</summary>
    public static double BestEmployeeOutputMax()
    {
        return EmployeeOutputMax(new SaveEmployeeInfo { IQ = MaxIQ, GPA = MaxGPA, Experience = MaxExperience, PhD = true });
    }

    /// <summary>Team Synergy's multiplier for a headcount: 1 + floor(n / 5) x 1.25% (CompanyManager.cs:591-595).</summary>
    public static double TeamSynergyMax(int headcount)
    {
        return 1.0 + Math.Floor(Math.Max(0, headcount) / (double)TeamSynergyGroup) * TeamSynergyRate;
    }

    // ================================================================================================================
    // The daily tick (CompanyManager.ProcessNewDay, CompanyManager.cs:502-654, and ApplyDailySkillEffects :1086-1117)
    // ================================================================================================================

    /// <summary>Market swing: Random.Range(-400, 400) + sqrt(n) x 50 (CompanyManager.cs:605).</summary>
    public static double MarketMax(int headcount)
    {
        return 400.0 + Math.Sqrt(Math.Max(0, headcount)) * 50.0;
    }

    /// <summary>Automation Initiative: RoundToInt(3 x 1.25) = 4 bots x $200 (SkillTreeManager.cs:384-387, :827; CompanyManager.cs:608-609).</summary>
    public const double RobotsMax = 4 * 200.0;

    /// <summary>
    /// Growth and interest skills add up to MAX_PROFIT_GROWTH = 5 x the day's positive operating result (CompanyManager.cs:44,
    /// :1094-1099; all of them together are 3.41 x 1.25 = 4.26).
    /// </summary>
    public const double MaxProfitGrowth = 5.0;
    /// <summary>Merger Mentality + Growth Through Scale: $ per employee per day, capped at 1,250 (CompanyManager.cs:45, :1000-1003, :1102-1106).</summary>
    public const double MaxMergerPerEmployee = 1250.0;

    /// <summary>Office items: $100 a level (the Sofa, dailyBonusPerLevel in MainGame.unity; OfficeItem.cs:12, :55-57), with margin.</summary>
    public const double OfficeIncomePerLevel = 250.0;
    public const int OfficeMaxLevel = 10;
    /// <summary>Used when the save lists no office items (the scene has one Sofa: 5 levels x $100).</summary>
    public const double OfficeIncomeFallback = 2500.0;

    // ---- The HQ campus (extra "building", BuildMode/TowerSave.cs formats 3 and 4; numbers: BuildMode/TowerRulesDefaults.cs) ----
    // A room earns base x (1 + 25% per furniture level) x its room level's multiplier + $100 per furniture level
    // (TowerPlan.RoomDailyIncome / RoomBonusIncome / FurnitureIncome, TowerPlan.cs:1011-1026), paid into the day's operating
    // result (CompanyManager.cs:617-620). Buildings, floors, skybridges and room levels add no money of their own; upkeep is
    // a cost; the accounting rooms' salary savings come back through DayBreakdown.recurring and stay below the salaries this
    // bound never subtracts; the build budget only comes from gambling and 50% sell refunds.

    /// <summary>
    /// Each furniture level earns $100 a day (TowerRules.incomePerLevel, TowerRules.cs:521; the room's level doesn't scale
    /// it, TowerPlan.cs:1011-1014), with margin.
    /// </summary>
    public const double TowerIncomePerLevel = 150.0;
    /// <summary>Each furniture level in a room adds 25% of the room's base income (TowerRules.bonusPerLevel, TowerRules.cs:523, :603-606).</summary>
    public const double TowerBonusPerLevel = 0.25;
    /// <summary>Top furniture level: Ultimate = 5 (TowerRules.MaxLevel, TowerRules.cs:563), with margin.</summary>
    public const int TowerMaxLevel = 6;
    /// <summary>Most furniture slots a room has: 7, the open-plan office (TowerRulesDefaults.cs:105-106), with margin.</summary>
    public const int TowerMaxSlotsPerRoom = 12;
    /// <summary>
    /// Most rooms a campus holds: the HQ's 36 floors x 8 tiles, 3 annexes x 6 floors x 12, 3 office towers x 30 x 10 and 2
    /// skyscrapers x 60 x 12 are 2,844 tiles (TowerRulesDefaults.DefaultBuildingTypes, :334-370); the smallest rooms take 2
    /// and the HQ's floor 1 holds the three built-in ones (8 tiles), so 1,421 rooms. With margin; rooms past it earn nothing.
    /// </summary>
    public const int TowerMaxRooms = 1500;
    /// <summary>The largest room-level multiplier of any room (x2.3 at the top level); unknown room ids get it at every level.</summary>
    public const double TowerLevelMultiplierMax = 2.3;

    /// <summary>
    /// The rooms with money of their own (TowerRoomDefinition.dailyIncome in TowerRulesDefaults.DefaultRooms): base income a
    /// day, the most one company may build (maxPerCompany, 0 = no limit) and the income multiplier at each room level from
    /// level 1 (levels[].effectMultiplier, x1 at level 1): trading floor (:275-285), casino room (:286-296), marketing
    /// (:186-195) and trophy hall (:320-329). Never lower a limit below what an older game allowed: loading keeps every
    /// saved room (TowerPlan.Restore skips the limits, TowerPlan.cs:789-806).
    /// </summary>
    static readonly Dictionary<string, TowerIncomeRoom> TowerIncomeRooms = new Dictionary<string, TowerIncomeRoom>(StringComparer.Ordinal)
    {
        ["trading_floor"] = new TowerIncomeRoom(1500.0, 0, 1.0, 1.4, 1.8, 2.3),
        ["casino_room"] = new TowerIncomeRoom(2500.0, 0, 1.0, 1.4, 1.8, 2.3),
        ["marketing"] = new TowerIncomeRoom(1000.0, 2, 1.0, 1.4, 1.8, 2.3),
        ["trophy_hall"] = new TowerIncomeRoom(60000.0, 1, 1.0, 1.4, 1.8)
    };

    /// <summary>Every other room of TowerRulesDefaults.DefaultRooms: no income of its own, only its furniture's.</summary>
    static readonly HashSet<string> TowerPlainRooms = new HashSet<string>(StringComparer.Ordinal)
    {
        "lobby", "ceo", "starter_office", "small_office", "big_office", "open_plan", "cubicle_farm", "break_room", "cafeteria", "gym",
        "meeting_room", "big_meeting_room", "executive_suite", "hr_department", "legal_department", "accounting", "research_lab",
        "training_center", "server_room", "data_center", "rooftop_lounge", "helipad"
    };

    /// <summary>A room id this version knows (TowerRulesDefaults.DefaultRooms).</summary>
    public static bool IsKnownTowerRoom(string roomId)
    {
        return roomId != null && (TowerIncomeRooms.ContainsKey(roomId) || TowerPlainRooms.Contains(roomId));
    }

    /// <summary>
    /// A room type's own daily income before furniture and room level: trading floor $1,500, casino room $2,500, marketing
    /// $1,000, trophy hall $60,000, every other known room $0. Unknown ids (a newer game may add rooms) get the casino room's
    /// $2,500, not the trophy hall's: any made-up id would earn that.
    /// </summary>
    public static double TowerRoomBaseIncome(string roomId)
    {
        if (roomId != null && TowerIncomeRooms.TryGetValue(roomId, out TowerIncomeRoom room)) return room.BaseIncome;
        return roomId != null && TowerPlainRooms.Contains(roomId) ? 0.0 : 2500.0;
    }

    /// <summary>
    /// The multiplier a room's level puts on its income (TowerRoomDefinition.MultiplierAt, TowerRules.cs:323-348): x1 at level
    /// 1 (and for 0 or a missing level: format 3 saves have none), a level past the room's top counts as the top (the game
    /// clamps it too, TowerPlan.cs:802). Trading floor, casino room, marketing: x1.4, x1.8, x2.3 at levels 2-4; trophy hall
    /// x1.4, x1.8 at levels 2-3. Rooms without income: x1 (nothing to multiply). Unknown ids: TowerLevelMultiplierMax.
    /// </summary>
    public static double TowerRoomLevelMultiplier(string roomId, int level)
    {
        if (roomId != null && TowerIncomeRooms.TryGetValue(roomId, out TowerIncomeRoom room))
        {
            double[] multipliers = room.LevelMultipliers;
            return multipliers[Math.Max(0, Math.Min(multipliers.Length, level) - 1)];
        }
        return roomId != null && TowerPlainRooms.Contains(roomId) ? 1.0 : TowerLevelMultiplierMax;
    }

    /// <summary>
    /// How many rooms of this type one company can have earning their income (maxPerCompany: the trophy hall 1, marketing 2;
    /// 0 = no limit). An honest save never has more (TowerPlan.CheckRoomRules, TowerPlan.cs:586-594); a save listing more
    /// counts the best ones at full income and the rest at their furniture's alone (TowerFurnitureIncomeMax).
    /// </summary>
    public static int TowerRoomMaxPerCompany(string roomId)
    {
        return roomId != null && TowerIncomeRooms.TryGetValue(roomId, out TowerIncomeRoom room) ? room.MaxPerCompany : 0;
    }

    /// <summary>
    /// The most one campus room earns a day with these furniture levels at room level <paramref name="roomLevel"/>
    /// (1-based; 0 or less = 1): base x (1 + 25% per furniture level) x the level's multiplier + $150 per furniture level.
    /// Top level with every piece at Ultimate: trading floor $23,700, casino room $37,500, marketing $16,800, trophy hall
    /// $651,000 (the game: $22,700, $36,500, $15,800, $650,000; the difference is the per-level margin), other rooms $150 a
    /// furniture level.
    /// </summary>
    public static double TowerRoomIncomeMax(string roomId, IList<int> levels, int roomLevel = 1)
    {
        int total = TowerFurnitureLevels(levels);
        return TowerRoomBaseIncome(roomId) * (1.0 + TowerBonusPerLevel * total) * TowerRoomLevelMultiplier(roomId, roomLevel)
               + total * TowerIncomePerLevel;
    }

    /// <summary>What a room's furniture earns a day on its own (the room's base income left out).</summary>
    public static double TowerFurnitureIncomeMax(IList<int> levels)
    {
        return TowerFurnitureLevels(levels) * TowerIncomePerLevel;
    }

    /// <summary>A room's furniture levels added up: at most TowerMaxSlotsPerRoom slots, each clamped to 0..TowerMaxLevel.</summary>
    static int TowerFurnitureLevels(IList<int> levels)
    {
        int total = 0;
        if (levels != null)
        {
            for (int i = 0; i < levels.Count && i < TowerMaxSlotsPerRoom; i++)
                total += (int)Clamp(levels[i], 0, TowerMaxLevel);
        }
        return total;
    }

    /// <summary>One room type with money of its own (TowerIncomeRooms).</summary>
    sealed class TowerIncomeRoom
    {
        public readonly double BaseIncome;
        public readonly int MaxPerCompany;
        /// <summary>The income multiplier at levels 1, 2, ... (x1 at level 1).</summary>
        public readonly double[] LevelMultipliers;

        public TowerIncomeRoom(double baseIncome, int maxPerCompany, params double[] levelMultipliers)
        {
            BaseIncome = baseIncome;
            MaxPerCompany = maxPerCompany;
            LevelMultipliers = levelMultipliers != null && levelMultipliers.Length > 0 ? levelMultipliers : new[] { 1.0 };
        }
    }

    /// <summary>
    /// Chaos-branch daily luck base: 1% of company value, between $500 and $250,000 (CompanyManager.cs:73-75, :1057-1060).
    /// </summary>
    public static double LuckBonusBase(double value)
    {
        if (double.IsNaN(value)) value = 0.0;
        return Math.Min(Math.Max(value * 0.01, 500.0), 250000.0);
    }

    /// <summary>
    /// The most the chaos branch's daily luck can pay (CompanyManager.ApplyDailyLuck, CompanyManager.cs:1120-1220), every
    /// roll at its best on the same day: Corporate Roulette 4 x base (:1128-1142) plus, on luckBase = base x 1.25 (:1123):
    /// Fortune Cookie 2x (:1145-1150), Lottery 10x (:1153-1158), Mystery Box 2x (:1161-1169), Dice Roll 3x (:1172-1180),
    /// Tarot 1x (:1183-1201), Surprise Bonus 2x (:1204-1209); Karma Bonus on every 5th day adds 1 + n/20 (:1212-1217).
    /// </summary>
    public static double DailyLuckMax(double value, int headcount, bool karmaDay)
    {
        double bonusBase = LuckBonusBase(value);
        double luckBase = bonusBase * SkillEffectMultiplier;
        double total = bonusBase * 4.0 + luckBase * (2.0 + 10.0 + 2.0 + 3.0 + 1.0 + 2.0);
        if (karmaDay) total += luckBase * (1.0 + Math.Max(0, headcount) / 20.0);
        return total;
    }

    /// <summary>Karma Bonus pays when currentDay % 5 == 0 after the day ticks over (CompanyManager.cs:88, :508, :1212).</summary>
    public static bool IsKarmaDay(long day)
    {
        return day > 0 && day % 5 == 0;
    }

    // ================================================================================================================
    // Money that isn't in the ledger: hires, hiring tasks, skipped CVs, crises, board meetings
    // ================================================================================================================

    /// <summary>
    /// Hiring pace: one hire takes a Hire click, the HIRED stamp's hold (PaperManager.stampHold 0.35 s, PaperManager.cs:20,
    /// :78-79), the paper's flight off the desk (fallbackMoveDuration 0.5 s, PaperManager.cs:24, :128-148), a Next-batch
    /// click (the button only shows once the batch is decided, GameFlowManager.cs:73-79, OfficeHud.cs:498-529) and the new
    /// batch's entrance (entranceStaggerDelay 0.15 s per paper, GameFlowManager.cs:31, :238): about 1.3 s at the fastest.
    /// The running seconds it is applied to are already an upper bound ((days + 1) x 45 s, RunSeconds), so 1 hire per
    /// running second leaves margin on top.
    /// </summary>
    public const double MaxHiresPerRunSecond = 1.0;

    /// <summary>
    /// Hire impact (CompanyManager.cs:1303-1326): (IQ-100) x 15 + (GPA-2) x 200 + (experience-5) x 30 - 300 (- 500 with a
    /// record), doubled by Critical Success (:1318-1324). Saved attributes are the ones after hire-time bonuses, and
    /// experience only grows afterwards, so this is an upper bound for that employee.
    /// </summary>
    public static double HireImpactMax(SaveEmployeeInfo e)
    {
        if (e == null) return 0.0;
        double iq = Clamp(e.IQ, 0, MaxIQ);
        double gpa = Clamp(e.GPA, 0.0, MaxGPA);
        double exp = Clamp(e.Experience, 0, MaxExperience);
        double impact = (iq - BaselineIQ) * 15.0 + (gpa - BaselineGPA) * 200.0 + (exp - BaselineExperience) * 30.0 - 300.0;
        if (e.Criminal) impact -= 500.0;
        return Math.Max(0.0, impact) * 2.0;
    }

    /// <summary>The best possible hire's impact (for hires the server can't see).</summary>
    public static double BestHireImpactMax()
    {
        return HireImpactMax(new SaveEmployeeInfo { IQ = MaxIQ, GPA = MaxGPA, Experience = MaxExperience });
    }

    /// <summary>A hiring task starts 10 s of desk time after the last one (delayBetweenTasks in MainGame.unity; HiringTaskManager.cs:30, CompleteTask :459).</summary>
    public const double MinSecondsPerTask = 10.0;
    /// <summary>
    /// Largest hiring-task payout: rewardMoney 15,000 (the top of HiringTaskDatabase.asset / HiringTaskDatabase.cs) x
    /// (1 + the reward-bonus cap 1.0) (HiringTaskManager.cs:53, GetTaskModifiers :236) x 1.5 for an R&amp;D project (:50, StartTask :267-270) x 2 for
    /// Coin Flip (CompleteTask :419-431). A task only completes through a hire (HiringTaskManager.OnEmployeeHired :409-417).
    /// </summary>
    public const double MaxTaskReward = 15000.0 * 2.0 * 1.5 * 2.0;

    /// <summary>Trash to Treasure: $500 x 1.25 per lucky skipped CV (SkillTreeManager.cs:453-456; GameFlowManager.cs:322-336).</summary>
    public const double MaxSkipBonus = 500.0 * SkillEffectMultiplier;
    /// <summary>Two skips per batch of three (the last CV can't be skipped, GameFlowManager.cs:25, :76).</summary>
    public const int SkipsPerBatch = 2;
    /// <summary>
    /// A MainGame load deals a new batch (GameFlowManager.cs:110-121). Coming back to MainGame means leaving it first: two
    /// scene loads of a large scene, well over 10 real seconds per new batch.
    /// </summary>
    public const double MinSecondsPerSceneLoad = 10.0;

    /// <summary>Crises come at least 30 s of running clock apart (minTimeBetweenGames in MainGame.unity; MiniGameManager.cs:405-418, :461-467).</summary>
    public const double MinSecondsPerCrisis = 30.0;
    /// <summary>
    /// Largest crisis reward: 2,000 (successRewardBase) x (1 + (difficultyLevel 1 - 1) x 0.25) x the 1.8 difficulty cap
    /// (MainGame.unity; MiniGameManager.cs:50-61, :645-649) x 2 for Coin Flip (:656-668).
    /// </summary>
    public const double MaxCrisisReward = 2000.0 * 1.8 * 2.0;

    /// <summary>Board meetings every 10 days (BoardMeetings.cs:94); a Moonshot hit pays 5% of company value (:113-115).</summary>
    public const int BoardMeetingInterval = 10;
    public const double BoardBonusShare = 0.05;

    /// <summary>Extra factor on the whole income allowance, for anything the model above missed.</summary>
    public const double IncomeSafetyFactor = 1.5;

    /// <summary>Unexplained growth never flags below max($10,000, 0.5% of the last accepted value) (design).</summary>
    public static double GrowthSlack(double valueBefore)
    {
        return Math.Max(10000.0, 0.005 * Math.Max(0.0, valueBefore));
    }

    // ================================================================================================================
    // Bankruptcy hearing (RunEnd/RunEndDirector.cs, RunEnd/BankLoan.cs)
    // ================================================================================================================

    /// <summary>
    /// What a bankruptcy hearing can put into a company that hit $0: a bank loan of max($50,000, 10% of the run's peak)
    /// (BankLoan.cs:27-32, :133-138, :152-164) or the coin-flip restart at the same amount (RunEndDirector.cs:44-49,
    /// :223-228, :283-293), rounded to $1,000.
    /// </summary>
    public static double HearingRestartMax(double runPeak)
    {
        return Math.Max(50000.0, 0.10 * Math.Max(0.0, runPeak)) + 1000.0;
    }

    // ================================================================================================================
    // Gambles (Heat/GambleLedger.cs sources)
    // ================================================================================================================

    /// <summary>Normalised casino game names the client sends as LedgerEntry.Detail for source "casino".</summary>
    public const string CasinoSlots = "slots", CasinoBlackjack = "blackjack", CasinoCoinFlip = "coinflip";

    /// <summary>
    /// Casino blackjack: the best possible chance to WIN a hand (payout above the stake; a push isn't a win) is 43.8%:
    /// exact single-deck calculation of CasinoBlackjack.cs:145-231, :348-396 (one deck shuffled every hand, dealer stands on
    /// every 17, naturals push each other, no split) over every hit/stand strategy (doubling is hit-once-then-stand, so it
    /// can't raise the chance). 0.46 leaves margin.
    /// </summary>
    public const double BlackjackWinChanceMax = 0.46;
    /// <summary>
    /// Slots: any two or three matching symbols pay more than the stake: 1 - P(all different) = 48.16% with the reel
    /// weights 25/22/20/15/10/5/2/1 (SlotMachineGame.cs:43-52, :83-88). 0.49 leaves margin.
    /// </summary>
    public const double SlotsWinChanceMax = 0.49;
    /// <summary>Coin flips land heads 50% (CoinFlipGame.Flip :163, TiltPromptView.cs:48, :416). 0.505 leaves margin.</summary>
    public const double CoinFlipWinChanceMax = 0.505;
    /// <summary>
    /// Slots' rare payouts by size, from the paytable and reel weights (SlotMachineGame.cs:43-52, no skill changes them):
    /// three bells or better pay 20x or more (0.10^3 + 0.05^3 + 0.02^3 + 0.01^3 = 0.1134% of spins), stars or better 50x
    /// (0.0134%), sevens or better 100x (0.0009%), diamonds 500x (0.0001%). SlotsRareChances are those odds +30% or so.
    /// SaveRules counts each tier's payouts against these odds (a faked jackpot shows even when the win count looks honest).
    /// </summary>
    public static readonly double[] SlotsRareMultiples = { 20.0, 50.0, 100.0, 500.0 };
    public static readonly double[] SlotsRareChances = { 0.0015, 0.00018, 0.000012, 0.0000013 };
    /// <summary>
    /// A slots spin lasts spinDuration 2 s + 2 x reelStopDelay 0.3 s = 2.6 s of real time (SlotMachineGame.cs:32-33, :76-79;
    /// CasinoBootstrap adds the machine with these defaults, CasinoBootstrap.cs:52-53) and a new spin can't start before the
    /// last one is paid (SlotMachineGame.cs:127). 2.5 s leaves margin.
    /// </summary>
    public const double MinSecondsPerSlotsSpin = 2.5;

    /// <summary>
    /// The check for one gamble source: the largest payout multiple of the stake, the win chance for the luck tally
    /// (0 = not tallied: the outcome isn't a plain win/lose with known odds, or a win creates no money) and how the payout
    /// turns into company money.
    /// </summary>
    public static SaveGambleRule GambleRule(string source, string detail)
    {
        string key = Normalise(source);
        switch (key)
        {
            case "casino":
            {
                string game = NormaliseCasinoGame(detail);
                if (game == CasinoSlots)
                    // Three diamonds pay 500x the bet (SlotMachineGame.cs:50, :225-232); a spin that doesn't win pays nothing
                    // (any two matching symbols already pay 1.5x); 20x or more (three bells or better) comes 0.1134% of spins
                    return new SaveGambleRule(key + "/" + game, 500.0, SlotsWinChanceMax, SaveMoneyModel.Cash, true,
                                              pushMultiple: 0.0, rareMultiples: SlotsRareMultiples, rareChances: SlotsRareChances);
                if (game == CasinoBlackjack)
                    // A natural pays 2.5x, a win 2x, of the whole (doubled) stake; a push returns it (CasinoBlackjack.cs:47-48, :437-469)
                    return new SaveGambleRule(key + "/" + game, 2.5, BlackjackWinChanceMax, SaveMoneyModel.Cash, true);
                if (game == CasinoCoinFlip)
                    // A new flip pays 1.95x, double or nothing 2x the pot, both rounded to cents; a loss pays nothing
                    // (CoinFlipGame.cs:32-35, :83-86)
                    return new SaveGambleRule(key + "/" + game, 2.0, CoinFlipWinChanceMax, SaveMoneyModel.Cash, true, pushMultiple: 0.0);
                // Game not named: the largest casino payout, no luck tally
                return new SaveGambleRule(key, 500.0, 0.0, SaveMoneyModel.Cash, true);
            }
            case "tilt":
                // "Win it back": heads (50%) pays 2x the stake, tails nothing (TiltPromptView.cs:48-50, :483-490)
                return new SaveGambleRule(key, 2.0, CoinFlipWinChanceMax, SaveMoneyModel.Cash, true, pushMultiple: 0.0);
            case "upgrade-blackjack":
                // Win: the upgrade, reported at its price; push: the price back; loss: 0 (OfficeUpgradeManager.cs:343-368)
                return new SaveGambleRule(key, 1.0, 0.0, SaveMoneyModel.UpgradeBlackjack, true);
            case "upgrade-chance":
                // Critical success: upgrade + refund, reported as 2x the price; success: the upgrade at its price
                // (OfficeUpgradeManager.cs:437-470)
                return new SaveGambleRule(key, 2.0, 0.0, SaveMoneyModel.UpgradeChance, true);
            case "build-gamble":
                // A win pays 1.5x into the BUILD BUDGET (not company value); a push refunds the company
                // (BuildMode/BuildGambleView.cs:47, :729-752, :819-840; GambleLedger.cs:176-181)
                return new SaveGambleRule(key, 1.5, 0.0, SaveMoneyModel.BuildBudgetWin, true);
            case "furniture":
                // Stake and winnings are build budget and furniture levels (+2 for blackjack, +3 at most for the roll),
                // company value never moves (BuildMode/TowerBuilder.cs:987-990, :1093, :1113, :1199; TowerRules.cs:193, :315)
                return new SaveGambleRule(key, 3.0, 0.0, SaveMoneyModel.NoMoney, false);
            case ManagerGambleSource:
                return ManagerGambleRule(detail);
            default:
                // A source this version doesn't know: generous bound, no tally, the payout counts as money
                return new SaveGambleRule(key.Length > 0 ? key : "unknown", 500.0, 0.0, SaveMoneyModel.Cash, true);
        }
    }

    /// <summary>
    /// The most company value a gamble entry can have added (negative = it can only have cost money), with the payout
    /// already capped at the rule's multiple. Single entries use the exact outcome; summary entries (folded gambles)
    /// an upper bound from the sums.
    /// </summary>
    public static double GambleMoneyNet(SaveGambleRule rule, double stake, double payout, bool summary)
    {
        switch (rule.Money)
        {
            case SaveMoneyModel.NoMoney:
                return 0.0;
            case SaveMoneyModel.UpgradeBlackjack:
                // payout = stake (win: the upgrade, or push: the refund) or 0: at best the refund
                return Math.Min(0.0, payout - stake);
            case SaveMoneyModel.UpgradeChance:
                // Only a critical success (reported as 2x) gives the money back; with sums, crits' stakes are at most payout/2
                if (!summary) return payout >= 2.0 * stake - 0.01 ? 0.0 : -stake;
                return Math.Min(0.0, payout / 2.0 - stake);
            case SaveMoneyModel.BuildBudgetWin:
                // Only a push (payout = stake) gives company value back; a win went to the build budget
                if (!summary) return Math.Abs(payout - stake) < 0.01 ? 0.0 : -stake;
                return Math.Min(0.0, payout - stake);
            default:
                return payout - stake;
        }
    }

    // ================================================================================================================
    // The manager (Manager/ManagerEffects.cs, ManagerDecide.cs, ManagerEngine.cs; Resources/Manager/Situations/*.json)
    // ================================================================================================================
    // A decision's one-off money is a LedgerKinds.Manager entry (ManagerDecide.FinishContext :556-564: Change = what its
    // "money" effects moved, Stake = what its gambles staked); each of its gambles is also a gamble entry of source "manager"
    // (ManagerEffects.Gamble :256-290). Its recurring money (salary, policies, rent, the investor's revenue share) is a daily
    // cost through DailyEconomyHooks (ManagerSystem.DailyMoney :464-486): nothing to allow. Situations come up at most
    // once per cooldown (counted from when one came up, ManagerEngine.Eligible :329-337, Create :435), never while one is
    // on the desk (:323), and a decision waits at most defaultExpiryDays (Engine.json; ManagerEngine.cs:406-407, :523-527).

    /// <summary>GambleLedger source of the manager's gambles (ManagerEffects.GambleSource); their detail is "situation/choice".</summary>
    public const string ManagerGambleSource = "manager";
    /// <summary>A manager gamble never stakes the company's last $1,000 (ManagerSystem.Reserve; ManagerEffects.Gamble :260-261).</summary>
    public const double ManagerReserve = 1000.0;
    /// <summary>
    /// Days a decision can wait on the desk after its situation came up: defaultExpiryDays 12 (Engine.json); none of the
    /// money situations sets "expires" or "urgent". Its params (the offer's amount, a gamble's stake) are fixed when it
    /// comes up (ManagerEngine.Create :410-419).
    /// </summary>
    public const int ManagerDecisionWindowDays = 12;
    /// <summary>The engine's cooldown for a situation that doesn't set one (Engine.json situationCooldown): used for gambles of situations this version doesn't know.</summary>
    public const int ManagerDefaultCooldownDays = 15;

    /// <summary>
    /// The one situation that pays money in: an investor's offer (Finance.json:80-145). "accept" pays the offer's amount,
    /// "negotiate" 1.25 x it with 55% (45%: nothing); both start the investor's 40-day revenue share. It only comes up
    /// while no share runs ("!share", ManagerFacts.cs:339-341), so two gains are at least 40 days apart.
    /// </summary>
    public const string InvestorSituation = "investor_share";
    public const string InvestorAccept = "accept", InvestorNegotiate = "negotiate";
    /// <summary>The revenue share's id and length ("revenue_share" effect with id "investor", share_days 40).</summary>
    public const string InvestorShareId = "investor";
    public const int InvestorShareDays = 40;
    /// <summary>
    /// The offer's amount: 5.5 days of profit clamped to the cash tier's [min, max] (the tier of the value when the offer came
    /// up), rounded to 2 significant figures (ManagerParams.Evaluate :107-116): never above max, by tier 0-6.
    /// </summary>
    public static readonly double[] InvestorAcceptMax = { 80000, 500000, 4000000, 20000000, 40000000, 200000000, 500000000 };
    /// <summary>Negotiated: 1.25 x the amount, rounded to 2 significant figures ("better"): never above RoundMoney(1.25 x max).</summary>
    public static readonly double[] InvestorNegotiateMax = { 100000, 630000, 5000000, 25000000, 50000000, 250000000, 630000000 };
    /// <summary>Company value that reaches cash tiers 1-6 (StatusTier.Thresholds, UI/Kit/StatusTier.cs:11; the manager's "cash_tier" fact).</summary>
    public static readonly double[] CashTierThresholds = { 1e6, 1e7, 5e7, 1e8, 5e8, 1e9 };

    /// <summary>The cash tier of a company value (StatusTier.ForValue): 0-6.</summary>
    public static int CashTier(double value)
    {
        if (double.IsNaN(value)) return CashTierThresholds.Length;
        int tier = 0;
        while (tier < CashTierThresholds.Length && value >= CashTierThresholds[tier]) tier++;
        return tier;
    }

    /// <summary>
    /// The most a manager decision's money can add (a positive Change) for this situation and choice when its offer came up at
    /// cash tier <paramref name="tier"/>: the investor's offer, accepted or negotiated. 0 for everything else: every other
    /// "money" effect of the situations is a cost.
    /// </summary>
    public static double ManagerGainMax(string situation, string choice, int tier)
    {
        if (Normalise(situation) != InvestorSituation) return 0.0;
        int t = Math.Max(0, Math.Min(InvestorAcceptMax.Length - 1, tier));
        string c = Normalise(choice);
        if (c == InvestorAccept) return InvestorAcceptMax[t];
        if (c == InvestorNegotiate) return InvestorNegotiateMax[t];
        return 0.0;
    }

    /// <summary>
    /// The manager's gambles by "situation/choice": largest payout multiple, win chance (payout above the stake), the most a
    /// round that isn't won pays back, and the situation's cooldown. side_project (Employees.json), joint_marketing,
    /// profitable_month, idle_cash (Finance.json), casino_night (Heat.json).
    /// </summary>
    static readonly Dictionary<string, ManagerGamble> ManagerGambles = new Dictionary<string, ManagerGamble>(StringComparer.Ordinal)
    {
        ["side_project/fund"] = new ManagerGamble(3.0, 0.35, 0.2, 25),
        ["joint_marketing/accept"] = new ManagerGamble(1.5, 0.75, 0.6, 18),
        ["joint_marketing/negotiate"] = new ManagerGamble(1.5, 0.75, 0.6, 18),
        ["joint_marketing/aggressive"] = new ManagerGamble(3.0, 0.35, 0.3, 18),
        ["profitable_month/gamble"] = new ManagerGamble(2.0, 0.48, 0.0, 20),
        ["idle_cash/gamble"] = new ManagerGamble(2.0, 0.48, 0.0, 25),
        ["casino_night/real"] = new ManagerGamble(2.2, 0.45, 0.0, 30)
    };

    /// <summary>
    /// A manager gamble: payout = stake x its outcome's multiple, rounded to cents (ManagerEffects.Gamble :265-286), from company
    /// value, never the last $1,000. Known details are tallied at their win chance +1 point; a detail this version doesn't know
    /// gets the largest manager multiple (3x) and no tally.
    /// </summary>
    static SaveGambleRule ManagerGambleRule(string detail)
    {
        string d = Normalise(detail);
        if (ManagerGambles.TryGetValue(d, out ManagerGamble g))
            return new SaveGambleRule(ManagerGambleSource + "/" + d, g.MaxMultiple, g.WinChance + 0.01, SaveMoneyModel.Cash, true,
                                      pushMultiple: g.LossMultiple, stakeReserve: ManagerReserve);
        return new SaveGambleRule(ManagerGambleSource, 3.0, 0.0, SaveMoneyModel.Cash, true, stakeReserve: ManagerReserve);
    }

    /// <summary>The situation part of a manager gamble's detail ("joint_marketing/accept" → "joint_marketing").</summary>
    public static string ManagerSituationOf(string detail)
    {
        string d = Normalise(detail);
        int slash = d.IndexOf('/');
        return slash >= 0 ? d.Substring(0, slash) : d;
    }

    /// <summary>
    /// The most gambles of one manager situation <paramref name="days"/> in-game days can hold: a decision comes at most
    /// ManagerDecisionWindowDays after its situation came up, and it comes up at most once per cooldown, so the days plus that
    /// window hold floor((days + 12) / cooldown) + 1; +1 margin. Unknown situations (all of them together) use the engine's
    /// default cooldown. -1 when <paramref name="situation"/> is unknown.
    /// </summary>
    public static long ManagerGambleCap(string situation, long days)
    {
        int cooldown = -1;
        foreach (KeyValuePair<string, ManagerGamble> g in ManagerGambles)
            if (ManagerSituationOf(g.Key) == situation) cooldown = cooldown < 0 ? g.Value.CooldownDays : Math.Min(cooldown, g.Value.CooldownDays);
        return cooldown <= 0 ? -1 : ManagerGambleCapFor(cooldown, days);
    }

    /// <summary>ManagerGambleCap for a cooldown.</summary>
    public static long ManagerGambleCapFor(int cooldownDays, long days)
    {
        return (Math.Max(0L, days) + ManagerDecisionWindowDays) / Math.Max(1, cooldownDays) + 2;
    }

    /// <summary>One manager gamble (ManagerGambles).</summary>
    sealed class ManagerGamble
    {
        public readonly double MaxMultiple, WinChance, LossMultiple;
        public readonly int CooldownDays;

        public ManagerGamble(double maxMultiple, double winChance, double lossMultiple, int cooldownDays)
        {
            MaxMultiple = maxMultiple;
            WinChance = winChance;
            LossMultiple = lossMultiple;
            CooldownDays = cooldownDays;
        }
    }

    // ================================================================================================================
    // Risk cards (EventManager.ChooseCard / GetGoodChance / RollOutcome / ApplyEvent, EventDatabase.cs)
    // ================================================================================================================

    /// <summary>
    /// Largest multiplier skills put on a good card: Chaotic Energy 1 + 0.25 x 1.25 (SkillTreeManager.cs:537-540;
    /// EventManager.ApplyEvent :545-546) x Lucky Streak 1 + 0.15 x 1.25 x 3 (SkillTreeManager.cs:555-558; EventManager.cs:64,
    /// :549-555) x Critical Success 2 (:558-564) x Double or Nothing 2 (:567-578) x Jackpot 10 (:582-588) = 82.03.
    /// </summary>
    public const double CardSkillMultiplierMax = (1.0 + 0.25 * SkillEffectMultiplier) * (1.0 + 0.15 * SkillEffectMultiplier * 3.0) * 2.0 * 2.0 * 10.0;
    /// <summary>The percentage part of one card adds at most +100% of company value (EventManager.cs:72, ApplyEvent :592).</summary>
    public const double CardMaxPercentGain = 1.0;

    /// <summary>
    /// A risk level's check: base good chance (EventManager.cs:58-60) and the largest good-event money / percentage of its
    /// lists (EventDatabase.cs, GetEventList :680-690): High = excellent good (max $55,000 / 35%), Balanced = average good
    /// (max $8,000 / 12%), Low = small good (max $2,500 / 4%). Unknown risk names get the High bounds and no luck tally.
    /// </summary>
    public static SaveCardRule CardRule(string risk)
    {
        string key = Normalise(risk).Replace("-", "").Replace("_", "").Replace(" ", "");
        switch (key)
        {
            case "high":
            case "highrisk":
                return new SaveCardRule("high", 0.25, 55000.0, 0.35, true);
            case "balanced":
            case "medium":
                return new SaveCardRule("balanced", 0.50, 8000.0, 0.12, true);
            case "low":
            case "lowrisk":
                return new SaveCardRule("low", 0.75, 2500.0, 0.04, true);
            default:
                return new SaveCardRule(key.Length > 0 ? key : "unknown", 0.25, 55000.0, 0.35, false);
        }
    }

    /// <summary>
    /// The most one good card can add: money x M, then the percentage (x M, at most +100%) of the value after the money
    /// (EventManager.ApplyEvent :590-606), with M the largest skill multiplier. +$1 for rounding.
    /// </summary>
    public static double CardGainMax(SaveCardRule rule, double valueBefore)
    {
        return CardGainMax(rule, valueBefore, null, true);
    }

    /// <summary>
    /// CardGainMax for a player with these skills unlocked (null = unknown: all of them), with or without the Jackpot's x10.
    /// A gain above the no-Jackpot bound needed the Jackpot (SaveRules tallies those as rare outcomes; its caps use null).
    /// </summary>
    public static double CardGainMax(SaveCardRule rule, double valueBefore, ICollection<string> unlockedSkills, bool withJackpot)
    {
        double m = CardSkillMultiplier(unlockedSkills, withJackpot);
        double money = rule.MaxMoney * m;
        double percent = Math.Min(rule.MaxPercent * m, CardMaxPercentGain);
        return money + (Math.Max(0.0, valueBefore) + money) * percent + 1.0;
    }

    /// <summary>
    /// The multiplier a player's skills can put on a good card (EventManager.ApplyEvent :541-592): Chaotic Energy
    /// (chaos_4_energy, SkillTreeManager.cs:537-540), Lucky Streak x3 steps (chaos_4_streak, :555-558), Critical Success x2
    /// (chaos_3_crit, :512-515), Double or Nothing x2 (chaos_5_double, :580-583), Jackpot x10 (chaos_5_jackpot, :586-589),
    /// Corporate Enlightenment x1.25 on the first two. Skills only get unlocked during a run, so the save's list covers every
    /// card since the last save. +5% margin; null skills = all of them.
    /// </summary>
    public static double CardSkillMultiplier(ICollection<string> unlockedSkills, bool withJackpot)
    {
        bool all = unlockedSkills == null;
        Func<string, bool> has = id => all || unlockedSkills.Contains(id);
        double em = has("secret_ultimate") ? SkillEffectMultiplier : 1.0;
        double m = 1.0;
        if (has("chaos_4_energy")) m *= 1.0 + 0.25 * em;
        if (has("chaos_4_streak")) m *= 1.0 + 0.15 * em * 3.0;
        if (has("chaos_3_crit")) m *= 2.0;
        if (has("chaos_5_double")) m *= 2.0;
        if (withJackpot && has("chaos_5_jackpot")) m *= 10.0;
        return m * 1.05;
    }

    /// <summary>
    /// Upper bound of a good card's chance to hit the Jackpot (x10): 2% x 1.25 (SkillTreeManager.cs:586-589, capped at 25%
    /// by EventManager.cs:71, :582), +25% margin; 0 without the skill.
    /// </summary>
    public static double CardJackpotChanceMax(ICollection<string> unlockedSkills)
    {
        bool all = unlockedSkills == null;
        if (!all && !unlockedSkills.Contains("chaos_5_jackpot")) return 0.0;
        double em = all || unlockedSkills.Contains("secret_ultimate") ? SkillEffectMultiplier : 1.0;
        return Math.Min(0.02 * em, 0.25) * 1.25;
    }

    /// <summary>
    /// Upper bound of a card's chance to come out good for a player with these skills unlocked (null = unknown: every luck
    /// skill counts). GetGoodChance (EventManager.cs:336-375): base + Beginner's Luck 0.15 + Lucky Charm 0.10 + Probability
    /// Manipulation 0.15 + Gambler's Fallacy 0.40 (assumed after a bad card), each x 1.25 with Corporate Enlightenment,
    /// + 0.05 for full investor trust (CompanyManager.cs:64, :388), heat only lowers it; capped at 0.95. Then RollOutcome
    /// (EventManager.cs:461-493) saves a bad roll: Quantum Luck rerolls, Reality Warp 10% x 1.25, Reverse Karma 25% x 1.25,
    /// Wild Card rerolls (once a day; assumed always). +0.03 margin.
    /// </summary>
    public static double CardGoodChanceMax(SaveCardRule rule, ICollection<string> unlockedSkills)
    {
        bool all = unlockedSkills == null;
        Func<string, bool> has = id => all || unlockedSkills.Contains(id);
        double em = has("secret_ultimate") ? SkillEffectMultiplier : 1.0;

        double good = rule.BaseGoodChance + 0.05;
        if (has("chaos_1_luck")) good += 0.15 * em;          // SkillTreeManager.cs:447-450
        if (has("chaos_1_charm")) good += 0.10 * em;         // SkillTreeManager.cs:458-461
        if (has("chaos_6_probability")) good += 0.15 * em;   // SkillTreeManager.cs:610-614
        if (has("chaos_4_fallacy")) good += 0.40 * em;       // SkillTreeManager.cs:542-546
        good = Clamp(good, 0.0, 0.95);                       // EventManager.cs:63, :374

        double bad = 1.0 - good;
        if (has("chaos_6_quantum")) bad *= 1.0 - good;                       // SkillTreeManager.cs:622-626
        if (has("chaos_6_reality")) bad *= 1.0 - 0.10 * em;                  // SkillTreeManager.cs:616-620
        if (has("chaos_6_reverse")) bad *= 1.0 - Math.Min(0.25 * em, 0.75);  // SkillTreeManager.cs:604-608; EventManager.cs:66
        if (has("chaos_5_wildcard")) bad *= 1.0 - good;                      // SkillTreeManager.cs:573-577
        return Math.Min(0.999, 1.0 - bad + 0.03);
    }

    /// <summary>Risk cards come at least 60 s x 0.7 (Chaotic Energy) = 42 s of running clock apart (MainGame.unity; EventManager.cs:43-44, SetNextEventTime :220-233).</summary>
    public const double MinSecondsPerCard = 42.0;

    // ================================================================================================================
    // Stock sales (Stock.cs, StockMarketManager.cs)
    // ================================================================================================================

    /// <summary>
    /// Largest proceeds / cost basis of one sale after the shares were held up to <paramref name="marketHours"/> of play.
    /// Prices are a log random walk around a moving fair value (Stock.cs:72-116): the wildest stock (MOON: volatility
    /// 0.035, meme jumps of 10-30% with 1% a tick, StockMarketManager.cs:102; Stock.cs:232-238) moves the fair value with
    /// sigma 0.29 per sqrt(hour); drift is at most +0.13 per hour (Market Manipulation 0.1, StockMarketManager.cs:40-44,
    /// :164-170, + dividend 0.02 + trend 0.01). So exp(2.3 + 0.13 T + 1.6 sqrt(T)): 2.3 covers buying at the $1 floor while
    /// the fair value is at its $2 floor (Stock.cs:15-17), the price-vs-fair-value swing (sd 0.25) and a transient jump;
    /// 1.6 sqrt(T) is 5.5 sigma. A simulation of the best hindsight trade (buy at the lowest price, sell at the highest
    /// later) stays far below: p99 3.2x in 15 min, 4.9x in 1 h, 53x in 10 h, against 23x, 56x and 5,800x here. Loose on
    /// purpose: the model allows big moves over long holds.
    /// </summary>
    public static double TradeRatioMax(double marketHours)
    {
        double t = Math.Max(0.0, marketHours);
        double exponent = 2.3 + 0.13 * t + 1.6 * Math.Sqrt(t);
        return exponent > 700.0 ? double.MaxValue : Math.Exp(exponent);
    }

    // ================================================================================================================
    // Imports
    // ================================================================================================================

    /// <summary>
    /// The richest an honest company can plausibly be on a given day, for runs the server first sees at a later day
    /// (imports, and the first save of a run started without a time baseline). A fast honest pace ($1M by day 40, i.e. ~20
    /// min of 30-s days; $1B by day 280, i.e. 3.5 h of 45-s days) times 2,000 (a 500x slots jackpot with room to spare),
    /// but never more than MaxPlausibleValueCap: $100M on day 0, $2B on day 40, the $10B cap from day ~96 on. Anything
    /// richer gets the import flag, whatever the day.
    /// </summary>
    public static double MaxPlausibleValue(int day)
    {
        double d = Math.Max(0, day);
        double pace = d <= 40.0
            ? 50000.0 * Math.Exp(Math.Log(20.0) / 40.0 * d)
            : 1000000.0 * Math.Exp(Math.Log(1000.0) / 240.0 * (d - 40.0));
        double value = 2000.0 * pace;
        return double.IsNaN(value) || value > MaxPlausibleValueCap ? MaxPlausibleValueCap : value;
    }

    /// <summary>The import check never lets more than $10B through unflagged (the run's win value, RunEndDirector.cs:52).</summary>
    public const double MaxPlausibleValueCap = 1e10;

    /// <summary>
    /// A first save beyond this day gets the import flag too: 100,000 days are 17-52 days of non-stop play (15-45 s a day).
    /// </summary>
    public const int MaxPlausibleDay = 100000;

    // ================================================================================================================
    // Helpers
    // ================================================================================================================

    public static double Clamp(double value, double min, double max)
    {
        if (double.IsNaN(value)) return min;
        return value < min ? min : value > max ? max : value;
    }

    /// <summary>Lower-case, trimmed ("" for null).</summary>
    public static string Normalise(string text)
    {
        return string.IsNullOrWhiteSpace(text) ? "" : text.Trim().ToLowerInvariant();
    }

    /// <summary>"Slots", "slot machine", "coin-flip", "BlackJack"... to CasinoSlots / CasinoBlackjack / CasinoCoinFlip ("" = unknown).</summary>
    public static string NormaliseCasinoGame(string detail)
    {
        string d = Normalise(detail);
        var letters = new System.Text.StringBuilder(d.Length);
        foreach (char c in d)
            if (c >= 'a' && c <= 'z') letters.Append(c);
        string s = letters.ToString();
        if (s == "slots" || s == "slot" || s == "slotmachine") return CasinoSlots;
        if (s == "blackjack") return CasinoBlackjack;
        if (s == "coinflip" || s == "coin") return CasinoCoinFlip;
        return "";
    }
}

/// <summary>How a gamble's payout turns into company value.</summary>
public enum SaveMoneyModel
{
    /// <summary>Stake and payout are company value.</summary>
    Cash,
    /// <summary>Payout = the upgrade's price (win) or a refund (push): at best the stake back.</summary>
    UpgradeBlackjack,
    /// <summary>Only a critical success (payout 2x) returns the stake; a success is the upgrade.</summary>
    UpgradeChance,
    /// <summary>A win pays into the build budget; only a push returns company value.</summary>
    BuildBudgetWin,
    /// <summary>Build budget in, levels out: company value never moves.</summary>
    NoMoney
}

/// <summary>One gamble source's bounds (SaveCalibration.GambleRule).</summary>
public class SaveGambleRule
{
    public readonly string Key;
    /// <summary>Largest payout / stake.</summary>
    public readonly double MaxMultiplier;
    /// <summary>Chance a round is won (payout above the stake) for the luck tally; 0 = not tallied.</summary>
    public readonly double WinChance;
    public readonly SaveMoneyModel Money;
    /// <summary>The stake comes out of company value, so it can't be more than the value before it.</summary>
    public readonly bool StakeFromValue;
    /// <summary>The most a round that isn't won (payout not above the stake) pays back, per stake: a push or refund 1, else 0.</summary>
    public readonly double PushMultiple;
    /// <summary>Rare payout sizes (multiples of the stake, ascending; empty = none) and an upper bound of each one's chance per round.</summary>
    public readonly double[] RareMultiples;
    public readonly double[] RareChances;
    /// <summary>What a stake from company value always leaves behind (the manager's gambles: $1,000; 0 for the rest).</summary>
    public readonly double StakeReserve;

    public SaveGambleRule(string key, double maxMultiplier, double winChance, SaveMoneyModel money, bool stakeFromValue,
                          double pushMultiple = 1.0, double[] rareMultiples = null, double[] rareChances = null, double stakeReserve = 0.0)
    {
        Key = key;
        MaxMultiplier = maxMultiplier;
        WinChance = winChance;
        Money = money;
        StakeFromValue = stakeFromValue;
        PushMultiple = pushMultiple;
        StakeReserve = Math.Max(0.0, stakeReserve);
        bool rare = rareMultiples != null && rareChances != null && rareMultiples.Length == rareChances.Length;
        RareMultiples = rare ? rareMultiples : new double[0];
        RareChances = rare ? rareChances : new double[0];
    }
}

/// <summary>One risk level's bounds (SaveCalibration.CardRule).</summary>
public class SaveCardRule
{
    public readonly string Key;
    public readonly double BaseGoodChance;
    public readonly double MaxMoney;
    public readonly double MaxPercent;
    /// <summary>Known risk level: its results go into the luck tally.</summary>
    public readonly bool Tallied;

    public SaveCardRule(string key, double baseGoodChance, double maxMoney, double maxPercent, bool tallied)
    {
        Key = key;
        BaseGoodChance = baseGoodChance;
        MaxMoney = maxMoney;
        MaxPercent = maxPercent;
        Tallied = tallied;
    }
}
