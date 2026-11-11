using System;
using System.Collections.Generic;

/// <summary>Where an achievement belongs: its group in lists and the look of its icon.</summary>
public enum AchievementCategory
{
    Office,     // the tour, the language
    Business,   // hiring, staff, payroll
    Value,      // company value tiers
    Gambling,   // bets, the casino, "Win it back?"
    Heat,
    Risk,       // risk cards
    Crisis,
    Skills,
    Board,
    Building,
    Manager,
    Stocks,
    Ending,     // bankruptcy and the last coin flip
    City        // the online cities (reported by the Online City code)
}

/// <summary>How a counter takes a report: Sum adds the amount, Max keeps the highest amount reported.</summary>
public enum AchievementCounterMode
{
    Sum,
    Max
}

/// <summary>
/// A lifetime number the achievements read (per Steam user, across every company): hires, crises solved, the best
/// status tier... Achievements.Report(id, amount) feeds it. SteamStat names the INT stat in Steamworks that mirrors it
/// (for the progress bar on the achievement), or is null.
/// </summary>
public sealed class AchievementCounter
{
    public readonly string Id;
    public readonly AchievementCounterMode Mode;
    public readonly string SteamStat;
    /// <summary>What reports it (English, for tools).</summary>
    public readonly string Source;

    public AchievementCounter(string id, AchievementCounterMode mode, string source, string steamStat = null)
    {
        Id = id;
        Mode = mode;
        Source = source;
        SteamStat = steamStat;
    }
}

/// <summary>
/// One Steam achievement: its API name (permanent once shipped), group, hidden flag, icon, and the counter and target that
/// unlock it. Texts live in the "Achievements" String Table (Assets/Localization/Source/Achievements.csv) under NameKey and
/// DescKey; icons are SteamStore/achievements/&lt;API name&gt;.jpg and &lt;API name&gt;_locked.jpg.
/// </summary>
public sealed class AchievementDef
{
    public readonly string ApiName;
    public readonly AchievementCategory Category;
    public readonly bool Hidden;
    /// <summary>Icon of the generated badge: a file name from Assets/UI/Icons ("people", "Fill/star_fill") or a drawn mark ("draw:helipad").</summary>
    public readonly string Icon;
    /// <summary>Short language-neutral text on the badge ("100", "$1M"), or null.</summary>
    public readonly string Badge;
    public readonly string Counter;
    public readonly long Target;
    /// <summary>Steam's "x / y" pop-up at 25/50/75% of the target (long counters only).</summary>
    public readonly bool ShowProgress;
    /// <summary>How to unlock it, for testers (English; STEAM_ACHIEVEMENTS.md, the List tool).</summary>
    public readonly string Tip;

    public AchievementDef(string apiName, AchievementCategory category, string icon, string counter, long target,
                          bool hidden = false, string badge = null, bool progress = false, string tip = "")
    {
        ApiName = apiName;
        Category = category;
        Icon = icon;
        Counter = counter;
        Target = Math.Max(1L, target);
        Hidden = hidden;
        Badge = badge;
        ShowProgress = progress;
        Tip = tip ?? "";
    }

    /// <summary>The API name without "ACH_", in lower case: "first_hire".</summary>
    public string Id => (ApiName.StartsWith("ACH_", StringComparison.Ordinal) ? ApiName.Substring(4) : ApiName).ToLowerInvariant();
    public string NameKey => "ach." + Id + ".name";
    public string DescKey => "ach." + Id + ".desc";
    public string IconFile => ApiName + ".jpg";
    public string LockedIconFile => ApiName + "_locked.jpg";
}

/// <summary>
/// The ids Achievements.Report takes. Sum counters add the amount; Max counters keep the highest amount reported, so
/// "flags" report 1 and absolute numbers (the best tier, plots owned now) can be reported again and again safely.
/// </summary>
public static class AchievementEvents
{
    // Office
    public const string TutorialFinished = "tutorial_finished";      // Max: 1 = the first-day tour was played to its end
    public const string ForeignLanguageDay = "foreign_language_day"; // Max: 1 = a day ended with the game in another language

    // Business
    public const string Hires = "hires";                             // Sum: +1 per hire
    public const string GoldenHire = "golden_hire";                  // Max: 1
    public const string OfficeFull = "office_full";                  // Max: 1 = every place in the offices is taken
    public const string EmployeeFired = "employee_fired";            // Max: 1
    public const string Payroll = "payroll";                         // Max: yearly salaries of the whole staff, in dollars

    // Company value
    public const string ValueTier = "value_tier";                    // Max: 1 $1M, 2 $10M, 3 $50M, 4 $100M, 5 $500M, 6 $1B
    public const string GameWon = "game_won";                        // Max: 1 = $10B reached

    // Gambling
    public const string Gambles = "gambles";                         // Sum: +1 per settled voluntary gamble
    public const string CasinoWin = "casino_win";                    // Max: 1
    public const string BlackjackNatural = "blackjack_natural";      // Max: 1
    public const string SlotsMultiplier = "slots_multiplier";        // Max: the best slots payout as a multiple of the bet
    public const string CoinflipStreak = "coinflip_streak";          // Max: casino coin flips won in a row
    public const string AllIn = "all_in";                            // Max: 1 = one bet of at least 90% of the company
    public const string BuildGambleWin = "build_gamble_win";         // Max: 1
    public const string GambleForItWin = "gamble_for_it_win";        // Max: 1
    public const string TiltWon = "tilt_won";                        // Max: 1
    public const string TiltWalkedAway = "tilt_walked_away";         // Max: 1
    public const string TiltLost = "tilt_lost";                      // Max: 1
    public const string RockBottom = "rock_bottom";                  // Max: 1 = one bet lost at least 90% of the company

    // Heat
    public const string HeatLevel = "heat_level";                    // Max: the highest heat level (2 = Hot, 3 = On fire)
    public const string HeatFrozen = "heat_frozen";                  // Max: 1
    public const string Audited = "audited";                         // Max: 1

    // Risk cards
    public const string RiskCards = "risk_cards";                    // Sum: +1 per risk card played
    public const string HighRiskWin = "high_risk_win";               // Max: 1
    public const string RiskSurvived = "risk_survived";              // Max: 1 = lost a quarter of the company on one card and stayed solvent
    public const string HighRiskStreak = "high_risk_streak";         // Max: good High Risk cards in a row

    // Crises
    public const string CrisesSolved = "crises_solved";              // Sum: +1 per crisis solved
    public const string CrisisStreak = "crisis_streak";              // Max: crises solved in a row
    public const string CrisisKinds = "crisis_kinds";                // Max: different mini-games solved

    // Skills
    public const string SkillsUnlocked = "skills_unlocked";          // Max: skills unlocked in one company
    public const string SkillBranchDone = "skill_branch_done";       // Max: 1
    public const string SecretSkill = "secret_skill";                // Max: 1

    // Board
    public const string MoonshotHit = "moonshot_hit";                // Max: 1
    public const string NoConfidence = "no_confidence";              // Max: 1

    // Building
    public const string RoomsBuilt = "rooms_built";                  // Max: rooms built on one campus (the fixed ones not counted)
    public const string TallestBuilding = "tallest_building";        // Max: floors of the tallest building
    public const string Buildings = "buildings";                     // Max: buildings on one campus
    public const string Skybridges = "skybridges";                   // Max: skybridges on one campus
    public const string RoomTopLevel = "room_top_level";             // Max: 1 = a room at its highest level
    public const string Helipad = "helipad";                         // Max: 1
    public const string TrophyHall = "trophy_hall";                  // Max: 1

    // Manager
    public const string ManagerHired = "manager_hired";              // Max: 1
    public const string ManagerDecisions = "manager_decisions";      // Sum: +1 per decision made on the manager's desk
    public const string ManagerAdvice = "manager_advice";            // Sum: +1 per decision that followed the manager's advice
    public const string ManagerResigned = "manager_resigned";        // Max: 1

    // Stocks
    public const string StockTrade = "stock_trade";                  // Max: 1
    public const string StockSaleProfit = "stock_sale_profit";       // Max: the best profit on one sale, in dollars

    // Endings
    public const string Bankrupt = "bankrupt";                       // Max: 1 = a bankruptcy hearing opened
    public const string CoinflipSurvived = "coinflip_survived";      // Max: 1 = heads at the hearing's last coin flip

    // Online cities (reported by the Online City code: absolute numbers, safe to report again and again)
    public const string CityPlotsBought = "city_plots_bought";       // Max: plots bought from the City (any city, the best)
    public const string CityPlotsOwned = "city_plots_owned";         // Max: plots owned at once in one city
    public const string CityLandmarksOwned = "city_landmarks_owned"; // Max: landmarks owned in one city
    public const string CityTakeoversWon = "city_takeovers_won";     // Max: plots won from owners at blackjack
    public const string CityPlotsDefended = "city_plots_defended";   // Max: challengers who lost at the player's plots
    public const string CityLogoDrawn = "city_logo_drawn";           // Max: 1 = a pixel logo was drawn and saved
}

/// <summary>
/// Every achievement and the counters behind them, in one place. Add new ones at the end of their group; never rename or
/// remove an API name once it shipped (players' unlocks are stored under it). Checked by Tools > Game > Achievements >
/// Validate and by the icon generator, which reads this file.
/// </summary>
public static class AchievementDefinitions
{
    /// <summary>The String Table with the names and descriptions (Assets/Localization/Source/Achievements.csv).</summary>
    public const string Table = "Achievements";

    const AchievementCounterMode Sum = AchievementCounterMode.Sum;
    const AchievementCounterMode Max = AchievementCounterMode.Max;

    static readonly AchievementCounter[] counters =
    {
        new AchievementCounter(AchievementEvents.TutorialFinished, Max, "TutorialDirector.TourEnded (played to the end)"),
        new AchievementCounter(AchievementEvents.ForeignLanguageDay, Max, "CompanyManager.DayEnded while L10n.CurrentCode isn't en"),
        new AchievementCounter(AchievementEvents.Hires, Sum, "CompanyManager.EmployeeHired (floor: RunStats.Hires)", "STAT_HIRES"),
        new AchievementCounter(AchievementEvents.GoldenHire, Max, "CompanyManager.EmployeeHired with isGolden"),
        new AchievementCounter(AchievementEvents.OfficeFull, Max, "OfficeCapacity.IsFull (limited, staff > 0)"),
        new AchievementCounter(AchievementEvents.EmployeeFired, Max, "StaffLedger.EmployeeFired"),
        new AchievementCounter(AchievementEvents.Payroll, Max, "CompanyManager.totalSalary"),
        new AchievementCounter(AchievementEvents.ValueTier, Max, "the run's peak company value ($1M ... $1B tiers)"),
        new AchievementCounter(AchievementEvents.GameWon, Max, "RunEndDirector.WinReached / peak >= $10B"),
        new AchievementCounter(AchievementEvents.Gambles, Sum, "GambleLedger.Gambled"),
        new AchievementCounter(AchievementEvents.CasinoWin, Max, "GambleLedger: casino round won"),
        new AchievementCounter(AchievementEvents.BlackjackNatural, Max, "CasinoBlackjack / BlackjackGame hand won with a natural 21"),
        new AchievementCounter(AchievementEvents.SlotsMultiplier, Max, "GambleLedger: slots payout / stake"),
        new AchievementCounter(AchievementEvents.CoinflipStreak, Max, "CoinFlipGame.Streak after a win"),
        new AchievementCounter(AchievementEvents.AllIn, Max, "GambleLedger: stake >= 90% of company value"),
        new AchievementCounter(AchievementEvents.BuildGambleWin, Max, "BuildGambleView.GambleSettled won"),
        new AchievementCounter(AchievementEvents.GambleForItWin, Max, "BuildGambleView.GambleSettled won with a Gamble-for-it goal"),
        new AchievementCounter(AchievementEvents.TiltWon, Max, "GambleLedger: source tilt, won"),
        new AchievementCounter(AchievementEvents.TiltWalkedAway, Max, "TiltPromptView.WalkedAway"),
        new AchievementCounter(AchievementEvents.TiltLost, Max, "GambleLedger: source tilt, lost"),
        new AchievementCounter(AchievementEvents.RockBottom, Max, "GambleLedger: loss >= 90% of company value"),
        new AchievementCounter(AchievementEvents.HeatLevel, Max, "HeatMeter.LevelChanged / Level"),
        new AchievementCounter(AchievementEvents.HeatFrozen, Max, "HeatMeter.LockoutChanged(true)"),
        new AchievementCounter(AchievementEvents.Audited, Max, "HeatMeter.Audited"),
        new AchievementCounter(AchievementEvents.RiskCards, Sum, "EventManager.RiskCardSettled (floor: RunStats.RiskCardsPlayed)", "STAT_RISK_CARDS"),
        new AchievementCounter(AchievementEvents.HighRiskWin, Max, "EventManager.RiskCardSettled: High Risk, good"),
        new AchievementCounter(AchievementEvents.RiskSurvived, Max, "EventManager.RiskCardSettled: lost >= 25%, still solvent"),
        new AchievementCounter(AchievementEvents.HighRiskStreak, Max, "EventManager.RiskCardSettled: good High Risk cards in a row"),
        new AchievementCounter(AchievementEvents.CrisesSolved, Sum, "MiniGameManager.MiniGameCompleted(success) (floor: RunStats.CrisesSolved)", "STAT_CRISES"),
        new AchievementCounter(AchievementEvents.CrisisStreak, Max, "MiniGameManager.MiniGameCompleted: successes in a row", "STAT_CRISIS_STREAK"),
        new AchievementCounter(AchievementEvents.CrisisKinds, Max, "MiniGameManager: different mini-games solved (MiniGameStarted name)", "STAT_CRISIS_KINDS"),
        new AchievementCounter(AchievementEvents.SkillsUnlocked, Max, "SkillTreeManager: skills unlocked"),
        new AchievementCounter(AchievementEvents.SkillBranchDone, Max, "SkillTreeManager: every skill of a branch unlocked"),
        new AchievementCounter(AchievementEvents.SecretSkill, Max, "SkillTreeManager: secret_ultimate unlocked"),
        new AchievementCounter(AchievementEvents.MoonshotHit, Max, "BoardMeetings.TargetResolved(hit, Moonshot) / Hits"),
        new AchievementCounter(AchievementEvents.NoConfidence, Max, "BoardMeetings: vote of no confidence / Restructurings"),
        new AchievementCounter(AchievementEvents.RoomsBuilt, Max, "TowerPlan.BuiltRoomCount"),
        new AchievementCounter(AchievementEvents.TallestBuilding, Max, "TowerPlan: the tallest building's floors", "STAT_TALLEST_BUILDING"),
        new AchievementCounter(AchievementEvents.Buildings, Max, "TowerPlan.Buildings.Count"),
        new AchievementCounter(AchievementEvents.Skybridges, Max, "TowerPlan.Connections.Count"),
        new AchievementCounter(AchievementEvents.RoomTopLevel, Max, "TowerPlan: a room at its top level"),
        new AchievementCounter(AchievementEvents.Helipad, Max, "TowerPlan: a helipad"),
        new AchievementCounter(AchievementEvents.TrophyHall, Max, "TowerPlan: a trophy hall"),
        new AchievementCounter(AchievementEvents.ManagerHired, Max, "ManagerSystem: a manager was hired"),
        new AchievementCounter(AchievementEvents.ManagerDecisions, Sum, "ManagerSystem.Stats.decided (new ones)", "STAT_MANAGER_DECISIONS"),
        new AchievementCounter(AchievementEvents.ManagerAdvice, Sum, "ManagerSystem.Stats.followed (new ones)", "STAT_MANAGER_ADVICE"),
        new AchievementCounter(AchievementEvents.ManagerResigned, Max, "ManagerSystem.Stats.resigned"),
        new AchievementCounter(AchievementEvents.StockTrade, Max, "StockMarketManager.TradeCompleted"),
        new AchievementCounter(AchievementEvents.StockSaleProfit, Max, "StockMarketManager.StockSold: proceeds - cost basis"),
        new AchievementCounter(AchievementEvents.Bankrupt, Max, "RunEndDirector.HearingOpened (or a loan / restart in the run)"),
        new AchievementCounter(AchievementEvents.CoinflipSurvived, Max, "RunStats.Restarts > 0 (heads at the hearing)"),
        new AchievementCounter(AchievementEvents.CityPlotsBought, Max, "Online City: CityStats.PlotsBought"),
        new AchievementCounter(AchievementEvents.CityPlotsOwned, Max, "Online City: plots owned in the city", "STAT_CITY_PLOTS"),
        new AchievementCounter(AchievementEvents.CityLandmarksOwned, Max, "Online City: landmarks owned in the city"),
        new AchievementCounter(AchievementEvents.CityTakeoversWon, Max, "Online City: CityStats.ChallengesWon"),
        new AchievementCounter(AchievementEvents.CityPlotsDefended, Max, "Online City: CityStats.Defended"),
        new AchievementCounter(AchievementEvents.CityLogoDrawn, Max, "Online City: a drawn logo was saved"),
    };

    const AchievementCategory Office = AchievementCategory.Office;
    const AchievementCategory Business = AchievementCategory.Business;
    const AchievementCategory Value = AchievementCategory.Value;
    const AchievementCategory Gambling = AchievementCategory.Gambling;
    const AchievementCategory Heat = AchievementCategory.Heat;
    const AchievementCategory Risk = AchievementCategory.Risk;
    const AchievementCategory Crisis = AchievementCategory.Crisis;
    const AchievementCategory Skills = AchievementCategory.Skills;
    const AchievementCategory Board = AchievementCategory.Board;
    const AchievementCategory Building = AchievementCategory.Building;
    const AchievementCategory Manager = AchievementCategory.Manager;
    const AchievementCategory Stocks = AchievementCategory.Stocks;
    const AchievementCategory Ending = AchievementCategory.Ending;
    const AchievementCategory City = AchievementCategory.City;

    // One per line: D(API name, category, icon, counter, target, named options). The icon generator parses these lines.
    static readonly AchievementDef[] all =
    {
        D("ACH_TUTORIAL", Office, "clipboard", AchievementEvents.TutorialFinished, 1, tip: "New company, first-day tour: press Next through every step (Skip doesn't count). Pause menu can replay it."),
        D("ACH_LANGUAGE", Office, "draw:language", AchievementEvents.ForeignLanguageDay, 1, tip: "Switch the language (Settings) to anything but English and let one in-game day end."),
        D("ACH_FIRST_HIRE", Business, "person", AchievementEvents.Hires, 1, tip: "Hire anyone."),
        D("ACH_HIRE_100", Business, "people", AchievementEvents.Hires, 100, badge: "100", progress: true, tip: "100 hires in total, over all companies (lifetime counter)."),
        D("ACH_HIRE_1000", Business, "people", AchievementEvents.Hires, 1000, badge: "1,000", progress: true, tip: "1,000 hires in total, over all companies (lifetime counter)."),
        D("ACH_GOLDEN_HIRE", Business, "Fill/star_fill", AchievementEvents.GoldenHire, 1, tip: "Hire an applicant with the golden CV (or get one through Lucky Hire)."),
        D("ACH_FULL_OFFICE", Business, "home", AchievementEvents.OfficeFull, 1, badge: "10/10", tip: "Hire until the offices are full (the HUD shows OFFICE FULL)."),
        D("ACH_FIRE", Business, "person", AchievementEvents.EmployeeFired, 1, tip: "Staff files: fire anyone."),
        D("ACH_BIG_PAYROLL", Business, "money", AchievementEvents.Payroll, 5000000, badge: "$5M", tip: "Staff salaries add up to $5,000,000 a year (about 50 average hires)."),
        D("ACH_VALUE_1M", Value, "trend-up", AchievementEvents.ValueTier, 1, badge: "$1M", tip: "Company value reaches $1,000,000 in any company (Company status)."),
        D("ACH_VALUE_10M", Value, "building", AchievementEvents.ValueTier, 2, badge: "$10M", tip: "Company value reaches $10,000,000 (Corporation status)."),
        D("ACH_VALUE_50M", Value, "briefcase", AchievementEvents.ValueTier, 3, badge: "$50M", tip: "Company value reaches $50,000,000 (Executive status)."),
        D("ACH_VALUE_100M", Value, "seal", AchievementEvents.ValueTier, 4, badge: "$100M", tip: "Company value reaches $100,000,000 (Tycoon status)."),
        D("ACH_VALUE_500M", Value, "trophy", AchievementEvents.ValueTier, 5, badge: "$500M", tip: "Company value reaches $500,000,000 (Mogul status)."),
        D("ACH_VALUE_1B", Value, "Fill/star_fill", AchievementEvents.ValueTier, 6, badge: "$1B", tip: "Company value reaches $1,000,000,000 (Billionaire status; the online cities open)."),
        D("ACH_WIN_10B", Value, "trophy", AchievementEvents.GameWon, 1, hidden: true, badge: "$10B", tip: "Company value reaches $10,000,000,000: the win screen."),
        D("ACH_FIRST_GAMBLE", Gambling, "dice", AchievementEvents.Gambles, 1, tip: "Any voluntary bet: the build-budget gamble, the casino, a blackjack or chance office upgrade."),
        D("ACH_CASINO_WIN", Gambling, "cards", AchievementEvents.CasinoWin, 1, tip: "Company casino (Casino License skill): win any round of slots, blackjack or coin flip."),
        D("ACH_BLACKJACK_NATURAL", Gambling, "spade", AchievementEvents.BlackjackNatural, 1, badge: "21", tip: "Get 21 with the first two cards at any blackjack table (casino, office upgrade, build gamble)."),
        D("ACH_SLOTS_BIG_WIN", Gambling, "draw:777", AchievementEvents.SlotsMultiplier, 10, tip: "Casino slots: three grapes or better (10x+). About 1 spin in 220."),
        D("ACH_COINFLIP_STREAK", Gambling, "coin", AchievementEvents.CoinflipStreak, 5, badge: "x5", tip: "Casino coin flip: win, then win Double or nothing 4 times in a row (1 in 32)."),
        D("ACH_ALL_IN", Gambling, "draw:chips", AchievementEvents.AllIn, 1, tip: "One bet of at least 90% of the company value (casino MAX with a company over ~$10,000)."),
        D("ACH_BUILD_GAMBLE", Gambling, "building", AchievementEvents.BuildGambleWin, 1, tip: "Build mode: Fill budget, win the coin flip or the blackjack hand."),
        D("ACH_GAMBLE_FOR_IT", Gambling, "tag", AchievementEvents.GambleForItWin, 1, tip: "Build mode: pick something you can't afford, press Gamble for it, win."),
        D("ACH_TILT_WON", Gambling, "refresh", AchievementEvents.TiltWon, 1, tip: "At heat Hot (60+), lose a bet; Win it back? appears: take it and get heads."),
        D("ACH_TILT_WALK_AWAY", Gambling, "power", AchievementEvents.TiltWalkedAway, 1, tip: "When Win it back? appears, press Walk away (or Escape)."),
        D("ACH_TILT_LOST", Gambling, "trend-down", AchievementEvents.TiltLost, 1, hidden: true, tip: "Take the Win it back? flip and get tails."),
        D("ACH_ROCK_BOTTOM", Gambling, "skull", AchievementEvents.RockBottom, 1, hidden: true, tip: "Lose one bet of at least 90% of the company value."),
        D("ACH_HEAT_HOT", Heat, "fire", AchievementEvents.HeatLevel, 2, tip: "Gamble until the heat chip reads Hot (60+)."),
        D("ACH_HEAT_FROZEN", Heat, "draw:snowflake", AchievementEvents.HeatFrozen, 1, tip: "Push heat to 100: the board freezes gambling."),
        D("ACH_AUDITED", Heat, "search", AchievementEvents.Audited, 1, hidden: true, tip: "End days at Hot (6%) or On fire (15%) until the regulators audit you."),
        D("ACH_RISK_100", Risk, "cards", AchievementEvents.RiskCards, 100, badge: "100", progress: true, tip: "Play 100 risk cards in total, over all companies (lifetime counter)."),
        D("ACH_RISK_JACKPOT", Risk, "sparkle", AchievementEvents.HighRiskWin, 1, tip: "Pick the High Risk card and get a good event (about 25%)."),
        D("ACH_RISK_SURVIVOR", Risk, "shield", AchievementEvents.RiskSurvived, 1, tip: "One bad risk card takes at least 25% of the company value, and the company still has money."),
        D("ACH_CHARMED_LIFE", Risk, "heart", AchievementEvents.HighRiskStreak, 3, hidden: true, badge: "x3", tip: "Three risk cards in a row, all High Risk, all good (about 1 in 64)."),
        D("ACH_CRISIS_25", Crisis, "warning", AchievementEvents.CrisesSolved, 25, badge: "25", progress: true, tip: "Solve 25 crisis mini-games in total, over all companies."),
        D("ACH_CRISIS_STREAK", Crisis, "check", AchievementEvents.CrisisStreak, 10, badge: "x10", tip: "Solve 10 crises in a row without failing or giving up one (lifetime streak)."),
        D("ACH_CRISIS_ALL", Crisis, "monitor", AchievementEvents.CrisisKinds, 20, badge: "20", progress: true, tip: "Solve each of the 20 different crisis mini-games at least once."),
        D("ACH_SKILL_FIRST", Skills, "bulb", AchievementEvents.SkillsUnlocked, 1, tip: "Unlock any skill on the skill board."),
        D("ACH_SKILL_BRANCH", Skills, "target", AchievementEvents.SkillBranchDone, 1, tip: "Unlock all 30 skills of the Growth, Productivity or Chaos branch in one company."),
        D("ACH_SKILL_SECRET", Skills, "eye", AchievementEvents.SecretSkill, 1, hidden: true, tip: "Unlock every skill, then the secret one that appears (Corporate Enlightenment)."),
        D("ACH_BOARD_MOONSHOT", Board, "draw:moon", AchievementEvents.MoonshotHit, 1, tip: "Board meeting: pick the Moonshot target and reach it by the next meeting."),
        D("ACH_NO_CONFIDENCE", Board, "stamp", AchievementEvents.NoConfidence, 1, hidden: true, tip: "Investor trust below 15 at a board meeting: the board forces a restructuring."),
        D("ACH_FIRST_ROOM", Building, "home", AchievementEvents.RoomsBuilt, 1, tip: "Build mode (Q): build any room."),
        D("ACH_FLOORS_10", Building, "building", AchievementEvents.TallestBuilding, 10, badge: "10", progress: true, tip: "Add floors until one building has 10."),
        D("ACH_SECOND_BUILDING", Building, "draw:campus", AchievementEvents.Buildings, 2, tip: "Build mode, Campus: buy a second building (the Annex needs a $300k peak and 4 HQ floors)."),
        D("ACH_SKYBRIDGE", Building, "draw:bridge", AchievementEvents.Skybridges, 1, tip: "Build mode, Campus: build a skybridge between two buildings."),
        D("ACH_ROOM_MAX_LEVEL", Building, "arrow-up", AchievementEvents.RoomTopLevel, 1, tip: "Upgrade any room with levels to its highest level."),
        D("ACH_HELIPAD", Building, "draw:helipad", AchievementEvents.Helipad, 1, tip: "Build the Helipad (Tycoon status, top floor of a tower or skyscraper)."),
        D("ACH_TROPHY_HALL", Building, "trophy", AchievementEvents.TrophyHall, 1, tip: "Build the Trophy hall (Mogul status, ground floor)."),
        D("ACH_MANAGER_HIRED", Manager, "briefcase", AchievementEvents.ManagerHired, 1, tip: "Day 5: pick one of the three manager candidates."),
        D("ACH_MANAGER_DECISIONS_50", Manager, "folder", AchievementEvents.ManagerDecisions, 50, badge: "50", progress: true, tip: "Answer 50 decisions on the manager's desk (lifetime)."),
        D("ACH_MANAGER_ADVICE_25", Manager, "info", AchievementEvents.ManagerAdvice, 25, badge: "25", progress: true, tip: "Pick the choice marked I'd do this 25 times (lifetime)."),
        D("ACH_MANAGER_RESIGNED", Manager, "envelope", AchievementEvents.ManagerResigned, 1, hidden: true, tip: "Keep the manager's mood at 5 or below for 2 days (underpay, overrule): they resign."),
        D("ACH_STOCK_FIRST", Stocks, "chart", AchievementEvents.StockTrade, 1, tip: "Stock market (Stock Market Access skill): buy any shares."),
        D("ACH_STOCK_PROFIT", Stocks, "trend-up", AchievementEvents.StockSaleProfit, 1000000, badge: "$1M", tip: "Sell shares for at least $1,000,000 more than they cost."),
        D("ACH_BANKRUPT", Ending, "scales", AchievementEvents.Bankrupt, 1, hidden: true, tip: "Let the company value hit $0 with nothing left to sell: the bankruptcy hearing opens."),
        D("ACH_COINFLIP_SURVIVOR", Ending, "coin", AchievementEvents.CoinflipSurvived, 1, hidden: true, tip: "At the bankruptcy hearing pick One last coin flip and get heads."),
        D("ACH_CITY_PLOT", City, "map-pin", AchievementEvents.CityPlotsBought, 1, tip: "Online city ($1B): buy a plot from the City."),
        D("ACH_CITY_PLOTS_10", City, "Fill/map-pin_fill", AchievementEvents.CityPlotsOwned, 10, badge: "10", progress: true, tip: "Own 10 plots at the same time in one online city."),
        D("ACH_CITY_LANDMARK", City, "flag", AchievementEvents.CityLandmarksOwned, 1, tip: "Own a landmark plot in an online city."),
        D("ACH_CITY_TAKEOVER", City, "cards", AchievementEvents.CityTakeoversWon, 1, tip: "Challenge another owner's plot at blackjack and win it."),
        D("ACH_CITY_DEFEND", City, "shield", AchievementEvents.CityPlotsDefended, 1, tip: "Someone challenges one of your plots and loses at blackjack."),
        D("ACH_CITY_LOGO", City, "draw:pixel", AchievementEvents.CityLogoDrawn, 1, tip: "Online city company dialog: draw a pixel logo and save it."),
    };

    static AchievementDef D(string apiName, AchievementCategory category, string icon, string counter, long target,
                            bool hidden = false, string badge = null, bool progress = false, string tip = "")
    {
        return new AchievementDef(apiName, category, icon, counter, target, hidden, badge, progress, tip);
    }

    static Dictionary<string, AchievementDef> byApi;
    static Dictionary<string, AchievementCounter> countersById;
    static Dictionary<string, List<AchievementDef>> byCounter;

    /// <summary>Every achievement, in list order.</summary>
    public static IReadOnlyList<AchievementDef> All => all;

    /// <summary>Every counter.</summary>
    public static IReadOnlyList<AchievementCounter> Counters => counters;

    public static AchievementDef Find(string apiName)
    {
        if (string.IsNullOrEmpty(apiName)) return null;
        Index();
        byApi.TryGetValue(apiName, out AchievementDef def);
        return def;
    }

    public static AchievementCounter FindCounter(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        Index();
        countersById.TryGetValue(id, out AchievementCounter counter);
        return counter;
    }

    /// <summary>The achievements a counter unlocks (empty when none).</summary>
    public static IReadOnlyList<AchievementDef> ForCounter(string id)
    {
        Index();
        if (id != null && byCounter.TryGetValue(id, out List<AchievementDef> list)) return list;
        return Array.Empty<AchievementDef>();
    }

    static void Index()
    {
        if (byApi != null) return;
        var api = new Dictionary<string, AchievementDef>(StringComparer.Ordinal);
        var ids = new Dictionary<string, AchievementCounter>(StringComparer.Ordinal);
        var lists = new Dictionary<string, List<AchievementDef>>(StringComparer.Ordinal);
        foreach (AchievementCounter counter in counters)
            ids[counter.Id] = counter;
        foreach (AchievementDef def in all)
        {
            api[def.ApiName] = def;
            if (!lists.TryGetValue(def.Counter, out List<AchievementDef> list))
                lists[def.Counter] = list = new List<AchievementDef>();
            list.Add(def);
        }
        countersById = ids;
        byCounter = lists;
        byApi = api;
    }

    /// <summary>Problems in the lists above (duplicates, unknown counters, bad names), for the validator. Empty = fine.</summary>
    public static List<string> Problems()
    {
        var problems = new List<string>();
        var seenApi = new HashSet<string>(StringComparer.Ordinal);
        var seenCounter = new HashSet<string>(StringComparer.Ordinal);
        var seenStat = new HashSet<string>(StringComparer.Ordinal);
        var usedCounters = new HashSet<string>(StringComparer.Ordinal);
        foreach (AchievementCounter counter in counters)
        {
            if (!seenCounter.Add(counter.Id)) problems.Add("Counter '" + counter.Id + "' is listed twice.");
            if (counter.SteamStat != null && !seenStat.Add(counter.SteamStat)) problems.Add("Steam stat " + counter.SteamStat + " is used by two counters.");
            if (counter.SteamStat != null && !IsSteamName(counter.SteamStat, "STAT_")) problems.Add("Steam stat '" + counter.SteamStat + "' should be STAT_ + upper case letters, digits and _.");
        }
        foreach (AchievementDef def in all)
        {
            if (!seenApi.Add(def.ApiName)) problems.Add(def.ApiName + " is listed twice.");
            if (!IsSteamName(def.ApiName, "ACH_")) problems.Add(def.ApiName + ": API names are ACH_ + upper case letters, digits and _.");
            if (!seenCounter.Contains(def.Counter)) problems.Add(def.ApiName + ": unknown counter '" + def.Counter + "'.");
            usedCounters.Add(def.Counter);
            if (string.IsNullOrEmpty(def.Icon)) problems.Add(def.ApiName + " has no icon.");
        }
        foreach (AchievementCounter counter in counters)
            if (!usedCounters.Contains(counter.Id)) problems.Add("Counter '" + counter.Id + "' unlocks nothing.");
        return problems;
    }

    static bool IsSteamName(string name, string prefix)
    {
        if (string.IsNullOrEmpty(name) || !name.StartsWith(prefix, StringComparison.Ordinal) || name.Length <= prefix.Length || name.Length > 64) return false;
        foreach (char c in name)
            if (!((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_')) return false;
        return true;
    }
}
