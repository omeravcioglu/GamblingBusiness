using System;
using UnityEngine;

/// <summary>
/// Turns the game's events into achievement reports (AchievementSystem owns one). Read-only towards the game:
/// - Static events, hooked once: StatusLadder.TierChanged, RunEndDirector.HearingOpened / WinReached, GambleLedger.Gambled,
///   HeatMeter.LevelChanged / LockoutChanged / Audited, EventManager.RiskCardSettled, StaffLedger.EmployeeFired,
///   BoardMeetings.TargetResolved / MeetingOpened, OfficeCapacity.Changed, ManagerSystem.Changed,
///   BuildGambleView.GambleSettled, StockMarketManager.StockSold, TiltPromptView.WalkedAway, TutorialDirector.TourEnded.
/// - Scene objects, re-hooked when they change (like RunStats does): CompanyManager (EmployeeHired, DayEnded,
///   StateLoaded), MiniGameManager (MiniGameStarted / Completed), SkillTreeManager.SkillUnlocked,
///   StockMarketManager.TradeCompleted, the casino's CasinoBlackjack.HandEnded and CoinFlipGame.FlipLanded,
///   BlackjackGame.HandEnded (office upgrades, build gambles) and TowerBuilder.PlanChanged.
/// - Every 2 s (and right after a load or a profile change), once the saved game is in place, the company's state is
///   checked: peak value tier, payroll, full offices, heat, skills, the campus, board history, stock trades, the manager,
///   and the run's own records (RunStats) as floors for the lifetime counters, so saves from before achievements (or
///   played on another profile) still count what they did.
/// Every handler is guarded: a problem here is logged, never thrown into the game.
/// </summary>
sealed class AchievementHooks
{
    // Company value tiers the achievements name: $1M, $10M, $50M, $100M, $500M, $1B (fixed, whatever the status ladder says)
    static readonly double[] ValueTiers = { 1e6, 1e7, 5e7, 1e8, 5e8, 1e9 };
    const double DefaultWinValue = 1e10;
    const float StateCheckSeconds = 2f;
    const int MaxManagerStep = 5;             // more new decisions than this between two looks = a loaded state, not play
    const double BigBetShare = 0.9;           // "all in" / "rock bottom": 90% of the company value
    const double RiskSurvivorShare = 0.25;    // a quarter of the company lost on one card
    const double SolventAbove = 0.5;          // RunEndDirector's bankruptcy line

    // Helper values kept in the profile (lifetime streaks in progress)
    const string VarCrisisStreak = "crisis_streak_now";
    const string VarHighRiskStreak = "high_risk_streak_now";
    const string SetCrisisKinds = "crisis_kinds";

    readonly AchievementSystem owner;

    CompanyManager company;
    MiniGameManager crises;
    SkillTreeManager skills;
    StockMarketManager market;
    CasinoBlackjack casinoTable;
    CoinFlipGame coinFlip;
    BlackjackGame deskTable;
    TowerBuilder tower;

    ManagerSystem manager;
    bool managerBaselined;
    int mgrDecided, mgrFollowed;

    string lastCrisisGame;
    float nextStateCheck;
    bool checkSoon = true;
    bool loggedError;

    public AchievementHooks(AchievementSystem owner)
    {
        this.owner = owner;
    }

    public void Attach()
    {
        HookStatics(true);
    }

    public void Detach()
    {
        HookStatics(false);
        SetCompany(null);
        SetCrises(null);
        SetSkills(null);
        SetMarket(null);
        SetCasinoTable(null);
        SetCoinFlip(null);
        SetDeskTable(null);
        SetTower(null);
    }

    /// <summary>Another player's profile is active: check the company's state for them at once.</summary>
    public void OnProfileChanged()
    {
        checkSoon = true;
        managerBaselined = false;
    }

    public void Tick()
    {
        HookInstances();
        if (!checkSoon && Time.unscaledTime < nextStateCheck) return;
        checkSoon = false;
        nextStateCheck = Time.unscaledTime + StateCheckSeconds;
        Guard(CheckState);
    }

    // ------------------------------------------------------------------
    // Reporting helpers
    // ------------------------------------------------------------------

    void Report(string id, long amount = 1)
    {
        owner.Report(id, amount);
    }

    void Raise(string id, long value)
    {
        if (value > 0) owner.RaiseTo(id, value);
    }

    AchievementStore Store => owner.Store;

    void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            if (!loggedError) Debug.LogException(e);
            loggedError = true;
        }
    }

    /// <summary>The saved game is in place (values from before would be the fresh scene's).</summary>
    static bool GameReady()
    {
        return SaveSystem.Instance == null || SaveSystem.Instance.IsGameLoaded;
    }

    static int TierFor(double value)
    {
        int tier = 0;
        for (int i = 0; i < ValueTiers.Length; i++)
            if (value >= ValueTiers[i]) tier = i + 1;
        return tier;
    }

    static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    // ------------------------------------------------------------------
    // Static events
    // ------------------------------------------------------------------

    void HookStatics(bool on)
    {
        StatusLadder.TierChanged -= OnTierChanged;
        RunEndDirector.HearingOpened -= OnHearingOpened;
        RunEndDirector.WinReached -= OnWinReached;
        GambleLedger.Gambled -= OnGambled;
        HeatMeter.LevelChanged -= OnHeatLevelChanged;
        HeatMeter.LockoutChanged -= OnLockoutChanged;
        HeatMeter.Audited -= OnAudited;
        EventManager.RiskCardSettled -= OnRiskCardSettled;
        StaffLedger.EmployeeFired -= OnEmployeeFired;
        BoardMeetings.TargetResolved -= OnTargetResolved;
        BoardMeetings.MeetingOpened -= OnMeetingOpened;
        OfficeCapacity.Changed -= OnCapacityChanged;
        ManagerSystem.Changed -= OnManagerChanged;
        BuildGambleView.GambleSettled -= OnBuildGambleSettled;
        StockMarketManager.StockSold -= OnStockSold;
        TiltPromptView.WalkedAway -= OnWalkedAway;
        TutorialDirector.TourEnded -= OnTourEnded;
        if (!on) return;
        StatusLadder.TierChanged += OnTierChanged;
        RunEndDirector.HearingOpened += OnHearingOpened;
        RunEndDirector.WinReached += OnWinReached;
        GambleLedger.Gambled += OnGambled;
        HeatMeter.LevelChanged += OnHeatLevelChanged;
        HeatMeter.LockoutChanged += OnLockoutChanged;
        HeatMeter.Audited += OnAudited;
        EventManager.RiskCardSettled += OnRiskCardSettled;
        StaffLedger.EmployeeFired += OnEmployeeFired;
        BoardMeetings.TargetResolved += OnTargetResolved;
        BoardMeetings.MeetingOpened += OnMeetingOpened;
        OfficeCapacity.Changed += OnCapacityChanged;
        ManagerSystem.Changed += OnManagerChanged;
        BuildGambleView.GambleSettled += OnBuildGambleSettled;
        StockMarketManager.StockSold += OnStockSold;
        TiltPromptView.WalkedAway += OnWalkedAway;
        TutorialDirector.TourEnded += OnTourEnded;
    }

    // Any tier change (a promotion, or the tier a save loads with): check the value tiers now
    void OnTierChanged(int oldTier, int newTier)
    {
        checkSoon = true;
    }

    void OnHearingOpened()
    {
        Guard(() => Report(AchievementEvents.Bankrupt));
    }

    void OnWinReached()
    {
        Guard(() => Report(AchievementEvents.GameWon));
    }

    void OnGambled(GambleRecord record)
    {
        Guard(() =>
        {
            Report(AchievementEvents.Gambles);
            bool casino = record.source == GambleLedger.SourceCasino;
            if (casino && record.IsWin) Report(AchievementEvents.CasinoWin);
            if (casino && record.detail == GambleLedger.DetailSlots && record.stake > 0.0 && Finite(record.payout))
                Report(AchievementEvents.SlotsMultiplier, (long)Math.Floor(record.payout / record.stake + 1e-9));
            if (record.source == GambleLedger.SourceTilt)
            {
                if (record.IsWin) Report(AchievementEvents.TiltWon);
                else if (record.IsLoss) Report(AchievementEvents.TiltLost);
            }
            // Company money only: furniture gambles bet build budget against the company's value
            if (record.source != GambleLedger.SourceFurniture && record.companyValueBefore > 0.0)
            {
                double line = record.companyValueBefore * BigBetShare - 0.005;
                if (record.stake >= line) Report(AchievementEvents.AllIn);
                if (record.AmountLost >= line) Report(AchievementEvents.RockBottom);
            }
        });
    }

    void OnHeatLevelChanged(int oldLevel, int newLevel)
    {
        Guard(() => Report(AchievementEvents.HeatLevel, newLevel));
    }

    void OnLockoutChanged(bool locked)
    {
        if (locked) Guard(() => Report(AchievementEvents.HeatFrozen));
    }

    void OnAudited(double fine)
    {
        Guard(() => Report(AchievementEvents.Audited));
    }

    void OnRiskCardSettled(CardRisk risk, bool good, double valueBefore, double change)
    {
        Guard(() =>
        {
            Report(AchievementEvents.RiskCards);
            bool highWin = risk == CardRisk.HighRisk && good;
            if (highWin) Report(AchievementEvents.HighRiskWin);

            AchievementStore store = Store;
            if (store != null)
            {
                long streak = highWin ? store.GetVar(VarHighRiskStreak) + 1 : 0;
                store.SetVar(VarHighRiskStreak, streak);
                if (streak > 0) Report(AchievementEvents.HighRiskStreak, streak);
            }

            if (!good && Finite(change) && valueBefore > 0.0 && -change >= valueBefore * RiskSurvivorShare)
            {
                CompanyManager c = CompanyManager.Instance;
                if (c != null && c.companyValue > SolventAbove) Report(AchievementEvents.RiskSurvived);
            }
        });
    }

    void OnEmployeeFired(ApplicantData employee, double severance)
    {
        Guard(() => Report(AchievementEvents.EmployeeFired));
    }

    void OnTargetResolved(bool hit, int tier)
    {
        if (hit && tier == BoardMeetings.TierMoonshot) Guard(() => Report(AchievementEvents.MoonshotHit));
    }

    void OnMeetingOpened(int day)
    {
        Guard(() =>
        {
            BoardMeetings board = BoardMeetings.Instance;
            BoardMeetingReport meeting = board != null ? board.OpenMeeting : null;
            if (meeting != null && meeting.noConfidence) Report(AchievementEvents.NoConfidence);
        });
    }

    void OnCapacityChanged()
    {
        Guard(CheckOfficeFull);
    }

    void OnManagerChanged()
    {
        Guard(CheckManager);
    }

    void OnBuildGambleSettled(double stake, double paidBack)
    {
        if (!(paidBack > stake + 0.005)) return;
        Guard(() =>
        {
            Report(AchievementEvents.BuildGambleWin);
            BuildGambleView view = BuildGambleView.Instance;
            if (view != null && view.GoalName != null) Report(AchievementEvents.GambleForItWin);
        });
    }

    void OnStockSold(string symbol, long shares, double proceeds, double costBasis)
    {
        Guard(() =>
        {
            double profit = proceeds - costBasis;
            if (Finite(profit) && profit >= 1.0) Report(AchievementEvents.StockSaleProfit, (long)Math.Min(Math.Floor(profit), long.MaxValue / 2));
        });
    }

    void OnWalkedAway()
    {
        Guard(() => Report(AchievementEvents.TiltWalkedAway));
    }

    void OnTourEnded(bool finished)
    {
        if (finished) Guard(() => Report(AchievementEvents.TutorialFinished));
    }

    // ------------------------------------------------------------------
    // Scene objects
    // ------------------------------------------------------------------

    void HookInstances()
    {
        if (!ReferenceEquals(company, CompanyManager.Instance)) SetCompany(CompanyManager.Instance);
        if (!ReferenceEquals(crises, MiniGameManager.Instance)) SetCrises(MiniGameManager.Instance);
        if (!ReferenceEquals(skills, SkillTreeManager.Instance)) SetSkills(SkillTreeManager.Instance);
        if (!ReferenceEquals(market, StockMarketManager.Instance)) SetMarket(StockMarketManager.Instance);
        if (!ReferenceEquals(deskTable, BlackjackGame.Instance)) SetDeskTable(BlackjackGame.Instance);
        if (!ReferenceEquals(tower, TowerBuilder.Instance)) SetTower(TowerBuilder.Instance);

        CasinoManager casino = CasinoManager.Instance;
        CasinoBlackjack table = casino != null ? casino.blackjack : null;
        CoinFlipGame flip = casino != null ? casino.coinFlip : null;
        if (!ReferenceEquals(casinoTable, table)) SetCasinoTable(table);
        if (!ReferenceEquals(coinFlip, flip)) SetCoinFlip(flip);
    }

    void SetCompany(CompanyManager next)
    {
        if (company is object)
        {
            company.EmployeeHired -= OnEmployeeHired;
            company.DayEnded -= OnDayEnded;
            company.StateLoaded -= OnStateLoaded;
        }
        company = next;
        if (next is object)
        {
            next.EmployeeHired += OnEmployeeHired;
            next.DayEnded += OnDayEnded;
            next.StateLoaded += OnStateLoaded;
            checkSoon = true;
        }
    }

    void SetCrises(MiniGameManager next)
    {
        if (crises is object)
        {
            crises.MiniGameStarted -= OnCrisisStarted;
            crises.MiniGameCompleted -= OnCrisisCompleted;
        }
        crises = next;
        if (next is object)
        {
            next.MiniGameStarted += OnCrisisStarted;
            next.MiniGameCompleted += OnCrisisCompleted;
        }
    }

    void SetSkills(SkillTreeManager next)
    {
        if (skills is object) skills.SkillUnlocked -= OnSkillUnlocked;
        skills = next;
        if (next is object) next.SkillUnlocked += OnSkillUnlocked;
    }

    void SetMarket(StockMarketManager next)
    {
        if (market is object) market.TradeCompleted -= OnTradeCompleted;
        market = next;
        if (next is object) next.TradeCompleted += OnTradeCompleted;
    }

    void SetCasinoTable(CasinoBlackjack next)
    {
        if (casinoTable is object) casinoTable.HandEnded -= OnCasinoHandEnded;
        casinoTable = next;
        if (next is object) next.HandEnded += OnCasinoHandEnded;
    }

    void SetCoinFlip(CoinFlipGame next)
    {
        if (coinFlip is object) coinFlip.FlipLanded -= OnFlipLanded;
        coinFlip = next;
        if (next is object) next.FlipLanded += OnFlipLanded;
    }

    void SetDeskTable(BlackjackGame next)
    {
        if (deskTable is object) deskTable.HandEnded -= OnDeskHandEnded;
        deskTable = next;
        if (next is object) next.HandEnded += OnDeskHandEnded;
    }

    void SetTower(TowerBuilder next)
    {
        if (tower is object) tower.PlanChanged -= OnPlanChanged;
        tower = next;
        if (next is object)
        {
            next.PlanChanged += OnPlanChanged;
            checkSoon = true;
        }
    }

    void OnEmployeeHired(ApplicantData applicant)
    {
        Guard(() =>
        {
            Report(AchievementEvents.Hires);
            if (applicant != null && applicant.isGolden) Report(AchievementEvents.GoldenHire);
            CheckOfficeFull();
            CompanyManager c = CompanyManager.Instance;
            if (c != null) Report(AchievementEvents.Payroll, c.totalSalary);
        });
    }

    void OnDayEnded(int newDay, double change)
    {
        Guard(() =>
        {
            string code = L10n.CurrentCode;
            if (!string.IsNullOrEmpty(code) && code != L10n.English && code != L10n.PseudoCode) Report(AchievementEvents.ForeignLanguageDay);
        });
        checkSoon = true;
    }

    void OnStateLoaded()
    {
        // The save is in place: re-read the company once every participant restored itself
        checkSoon = true;
        managerBaselined = false;
    }

    void OnCrisisStarted(string gameName)
    {
        lastCrisisGame = gameName;
    }

    void OnCrisisCompleted(bool success, float reward)
    {
        Guard(() =>
        {
            AchievementStore store = Store;
            if (success)
            {
                Report(AchievementEvents.CrisesSolved);
                if (store != null)
                {
                    long streak = store.GetVar(VarCrisisStreak) + 1;
                    store.SetVar(VarCrisisStreak, streak);
                    Report(AchievementEvents.CrisisStreak, streak);
                    if (!string.IsNullOrEmpty(lastCrisisGame))
                        Report(AchievementEvents.CrisisKinds, store.AddToSet(SetCrisisKinds, lastCrisisGame));
                }
            }
            else if (store != null)
            {
                store.SetVar(VarCrisisStreak, 0);
            }
        });
        lastCrisisGame = null;
    }

    void OnSkillUnlocked(Skill skill)
    {
        Guard(CheckSkills);
    }

    void OnTradeCompleted(string symbol, long shares, bool isBuy)
    {
        Guard(() => Report(AchievementEvents.StockTrade));
    }

    void OnCasinoHandEnded(CasinoHandResult result, double paid)
    {
        if (result == CasinoHandResult.Blackjack) Guard(() => Report(AchievementEvents.BlackjackNatural));
    }

    void OnFlipLanded(bool heads, bool won, double paid)
    {
        if (!won) return;
        Guard(() =>
        {
            CoinFlipGame game = coinFlip;
            if (game != null) Report(AchievementEvents.CoinflipStreak, game.Streak);
        });
    }

    void OnDeskHandEnded(BlackjackResult result)
    {
        if (result != BlackjackResult.Win) return;
        Guard(() =>
        {
            BlackjackGame table = deskTable;
            if (table != null && table.PlayerCards != null && table.PlayerCards.Count == 2 && table.PlayerTotal == 21)
                Report(AchievementEvents.BlackjackNatural);
        });
    }

    void OnPlanChanged()
    {
        Guard(CheckTower);
    }

    // ------------------------------------------------------------------
    // State checks
    // ------------------------------------------------------------------

    void CheckState()
    {
        CompanyManager c = CompanyManager.Instance;
        if (c == null || !GameReady()) return;
        RunStats stats = RunStats.Instance;

        // Company value: a tier counts once reached (the run's peak)
        double peak = Finite(c.companyValue) ? Math.Max(0.0, c.companyValue) : 0.0;
        if (stats != null && Finite(stats.PeakValue)) peak = Math.Max(peak, stats.PeakValue);
        int tier = TierFor(peak);
        if (tier > 0) Report(AchievementEvents.ValueTier, tier);
        RunEndDirector director = RunEndDirector.Instance;
        double win = director != null && director.winValue > 0.0 ? director.winValue : DefaultWinValue;
        if (peak >= win || (stats != null && stats.WinShown)) Report(AchievementEvents.GameWon);

        // The run's own records are floors for the lifetime counters
        if (stats != null)
        {
            Raise(AchievementEvents.Hires, stats.Hires);
            if (stats.GoldenHires > 0) Report(AchievementEvents.GoldenHire);
            Raise(AchievementEvents.RiskCards, stats.RiskCardsPlayed);
            Raise(AchievementEvents.CrisesSolved, stats.CrisesSolved);
            if (stats.Restarts > 0)
            {
                Report(AchievementEvents.Bankrupt);
                Report(AchievementEvents.CoinflipSurvived);
            }
        }
        BankLoan loan = BankLoan.Instance;
        if (loan != null && loan.LoansTaken > 0) Report(AchievementEvents.Bankrupt);

        if (c.totalSalary > 0) Report(AchievementEvents.Payroll, c.totalSalary);
        CheckOfficeFull();
        CheckHeat();
        CheckSkills();
        CheckTower();
        CheckBoard();
        CheckStocks();
        CheckManager();
    }

    void CheckOfficeFull()
    {
        if (!GameReady()) return;
        if (OfficeCapacity.IsLimited && OfficeCapacity.Employees > 0 && OfficeCapacity.IsFull) Report(AchievementEvents.OfficeFull);
    }

    void CheckHeat()
    {
        HeatMeter heat = HeatMeter.Instance;
        if (heat == null) return;
        if (heat.Level > 0) Report(AchievementEvents.HeatLevel, heat.Level);
        if (heat.IsLockedOut) Report(AchievementEvents.HeatFrozen);
    }

    void CheckSkills()
    {
        SkillTreeManager tree = SkillTreeManager.Instance;
        if (tree == null || tree.allSkills == null || !GameReady()) return;
        var total = new int[3];
        var done = new int[3];
        int unlocked = 0;
        bool secret = false;
        foreach (Skill skill in tree.allSkills)
        {
            if (skill == null) continue;
            if (skill.id == SkillTreeManager.SecretSkillId)
            {
                secret |= skill.unlocked;
                continue;
            }
            int branch = (int)skill.branch;
            if (branch < 0 || branch >= total.Length) continue;
            total[branch]++;
            if (!skill.unlocked) continue;
            done[branch]++;
            unlocked++;
        }
        if (secret) unlocked++;
        if (unlocked > 0) Report(AchievementEvents.SkillsUnlocked, unlocked);
        for (int i = 0; i < total.Length; i++)
        {
            if (total[i] > 0 && done[i] == total[i])
            {
                Report(AchievementEvents.SkillBranchDone);
                break;
            }
        }
        if (secret) Report(AchievementEvents.SecretSkill);
    }

    void CheckTower()
    {
        TowerBuilder builder = TowerBuilder.Instance;
        if (builder == null || !GameReady()) return;
        TowerPlan plan = builder.Plan;
        if (plan == null) return;
        Report(AchievementEvents.RoomsBuilt, plan.BuiltRoomCount);
        int tallest = 0;
        foreach (TowerBuilding building in plan.Buildings)
            if (building != null) tallest = Math.Max(tallest, building.Floors);
        Report(AchievementEvents.TallestBuilding, tallest);
        Report(AchievementEvents.Buildings, plan.Buildings.Count);
        Report(AchievementEvents.Skybridges, plan.Connections.Count);
        foreach (TowerRoom room in plan.Rooms)
        {
            if (room == null || room.Definition == null) continue;
            if (room.Definition.MaxLevel >= 2 && room.IsTopRoomLevel)
            {
                Report(AchievementEvents.RoomTopLevel);
                break;
            }
        }
        if (plan.CountRooms("helipad") > 0) Report(AchievementEvents.Helipad);
        if (plan.CountRooms("trophy_hall") > 0) Report(AchievementEvents.TrophyHall);
    }

    void CheckBoard()
    {
        BoardMeetings board = BoardMeetings.Instance;
        if (board == null || !board.IsStateKnown) return;
        if (board.Hits(BoardMeetings.TierMoonshot) > 0) Report(AchievementEvents.MoonshotHit);
        if (board.Restructurings > 0) Report(AchievementEvents.NoConfidence);
    }

    void CheckStocks()
    {
        StockMarketManager m = StockMarketManager.Instance;
        if (m == null || m.RecentTrades == null || !GameReady()) return;
        if (m.RecentTrades.Count > 0) Report(AchievementEvents.StockTrade);
        foreach (StockMarketManager.TradeRecord trade in m.RecentTrades)
        {
            if (trade != null && !trade.buy && Finite(trade.profit) && trade.profit >= 1.0)
                Report(AchievementEvents.StockSaleProfit, (long)Math.Min(Math.Floor(trade.profit), long.MaxValue / 2));
        }
    }

    /// <summary>
    /// The manager has no events for single decisions: new decisions and followed advice are the growth of its saved
    /// statistics since the last look (a jump of more than a few at once is a loaded state and only resets the baseline).
    /// </summary>
    void CheckManager()
    {
        ManagerSystem m = ManagerSystem.Instance;
        if (m == null || !m.IsStateKnown || !GameReady())
        {
            if (!ReferenceEquals(manager, m)) managerBaselined = false;
            return;
        }
        ManagerStats stats = m.Stats;
        ManagerSaveState state = m.DebugState;
        if (stats == null || state == null) return;

        if (m.HasManager || state.managersHired > 0) Report(AchievementEvents.ManagerHired);
        if (stats.resigned > 0) Report(AchievementEvents.ManagerResigned);

        if (!ReferenceEquals(manager, m) || !managerBaselined)
        {
            manager = m;
            managerBaselined = true;
            mgrDecided = stats.decided;
            mgrFollowed = stats.followed;
            Raise(AchievementEvents.ManagerDecisions, stats.decided);
            Raise(AchievementEvents.ManagerAdvice, stats.followed);
            return;
        }

        int decided = stats.decided - mgrDecided;
        int followed = stats.followed - mgrFollowed;
        mgrDecided = stats.decided;
        mgrFollowed = stats.followed;
        if (decided > 0 && decided <= MaxManagerStep) Report(AchievementEvents.ManagerDecisions, decided);
        if (followed > 0 && followed <= MaxManagerStep) Report(AchievementEvents.ManagerAdvice, followed);
        Raise(AchievementEvents.ManagerDecisions, stats.decided);
        Raise(AchievementEvents.ManagerAdvice, stats.followed);
    }
}
