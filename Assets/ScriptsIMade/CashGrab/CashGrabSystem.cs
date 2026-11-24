using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The Cash Floor (Cash Grab), the game's core active loop since 2026-11-24:
///   hire / skip → the company earns → its income arrives as physical money on the cash floor → the CEO fights the
///   Backend, Frontend and Marketing departments for it → deposits it in the safe (Player Cash) → gambles, trades stocks,
///   fills the build budget and buys collection tools with it → expands the office → hires more.
/// Pieces:
/// - CashLedger: the money rules (company value = company account + floor + Player Cash + department budgets).
/// - CashStream: the day's income arrives as bills during the day (CompanyManager.IncomeStream settles it at day end).
/// - CashFloorSim: bills, physics, the CEO's tools and gadgets, the department workers and their interference.
/// - CashFloorView (+ CashShopView, CashDeskPeek, CashAudio, CashCoach): the full-screen floor behind the HUD (Tab), the
///   desk's little cash chip, sounds and one-time tips.
/// - PlayerWallet: Player Cash for the casino, the build gamble, the "win it back" flip and stocks.
/// This component runs the simulation even while the CEO is at the desk (the departments keep grabbing), but only while
/// the office clock runs (no panel open). Saved under "cashgrab". CashGrabBootstrap adds it to every scene with a
/// CompanyManager, so MainGame needs no edit. Texts: the "CashGrab" String Table.
/// </summary>
[DefaultExecutionOrder(50)]
[DisallowMultipleComponent]
public class CashGrabSystem : MonoBehaviour, ISaveParticipant, IDailyEconomyHook
{
    public const string Key = "cashgrab";

    public static CashGrabSystem Instance { get; private set; }

    /// <summary>The floor opened (true) or closed (false).</summary>
    public static event Action<bool> FloorToggled;

    public CashLedger Ledger { get; private set; }
    public CashFloorSim Sim { get; private set; }
    public CashStream Stream { get; private set; }
    public CashFloorView View { get; private set; }
    public CashAudio Audio { get; private set; }

    /// <summary>The company's cash state is known (a loaded save or a fresh company): Player Cash is in charge from now on.</summary>
    public bool Ready { get; private set; }

    [Header("Debug")]
    [Tooltip("Log purchases, department changes and settlements to the Console")]
    public bool logEvents = true;

    // ---- Equipment and progress ----
    readonly int[] toolLevels = new int[CashRules.ToolCount];
    readonly int[] gadgetLevels = new int[CashRules.GadgetCount];
    readonly int[] deptLevels = new int[CashLedger.DeptCount];
    CashTool selectedTool = CashTool.Hands;
    CashGadget selectedGadget = CashGadget.None;

    // ---- Stats ----
    public int Deposits { get; private set; }
    public double LifetimeCollected { get; private set; }
    public double LifetimeStolen { get; private set; }
    public double LifetimeSpent { get; private set; }
    public double TodayCollected { get; private set; }
    int todayDay;

    // ---- Design canvas extras (2026-11-24): the Count, perks, hands, hires ----
    public double LifetimeBonus { get; private set; }
    public double LifetimeFines { get; private set; }
    public int WrittenUpCount { get; private set; }
    public int BestHand { get; private set; }
    /// <summary>What each side banked today: 0..2 the departments, 3 the CEO (bonuses and fines included).</summary>
    readonly double[] todayTakes = new double[4];
    /// <summary>The same since the last Count.</summary>
    readonly double[] weekTakes = new double[4];
    int weekDays;
    int todayHands, todayBestHand, todayStamps;
    double todayBonus;
    public CashPerk ActivePerk { get; private set; }
    /// <summary>The department that won the last Count (works faster until the next one), -1 none.</summary>
    public int WinnerDept { get; private set; } = -1;
    /// <summary>The CEO won the last Count and hasn't picked a perk yet.</summary>
    public bool PerkPending { get; private set; }
    public float PerkPendingSince { get; private set; }
    readonly int[] deptHires = new int[CashLedger.DeptCount];
    public int Interns { get; private set; }
    public int DeptHires(int dept) => dept >= 0 && dept < deptHires.Length ? deptHires[dept] : 0;
    public int DaysUntilCount => Math.Max(1, CashRules.CountEveryDays - weekDays);
    public double WeekTake(int side) => side >= 0 && side < weekTakes.Length ? weekTakes[side] : 0.0;
    public double TodayTake(int side) => side >= 0 && side < todayTakes.Length ? todayTakes[side] : 0.0;
    int happyDay = -1;
    float happyAt;
    bool happyDone;

    /// <summary>A day ended: the floor's tally (and every CountEveryDays days the Count with its winner).</summary>
    public sealed class DayCount
    {
        public int day;
        public bool weekly;
        public readonly double[] takes = new double[4];   // 0..2 departments, 3 the CEO
        public int winner = -1;                           // weekly: 3 the CEO, 0..2 a department; -1 none
        public int hands, bestHand, stamps;
        public double bonus;
    }

    /// <summary>The floor's day tally is ready (the view shows it; weekly ones offer the winner's perk).</summary>
    public static event Action<DayCount> CountReady;
    public DayCount LastCount { get; private set; }

    CompanyManager hookedCompany;
    bool restoredThisLoad;
    SavedCash pendingRestore;
    float departmentTimer;
    float layoutTimer;
    float resumTimer;
    float lastSeizeToast = -100f;
    int lastSeats = -1;

    readonly List<CashEvent> eventBuffer = new List<CashEvent>(64);

    public int ToolLevel(CashTool tool) => toolLevels[(int)tool];
    public int GadgetLevel(CashGadget gadget) => gadget == CashGadget.None ? 0 : gadgetLevels[(int)gadget];
    public int DeptLevel(int dept) => deptLevels[dept];
    public CashTool SelectedTool => selectedTool;
    public CashGadget SelectedGadget => selectedGadget;
    public bool IsFloorOpen => View != null && View.IsOpen;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        // Entering Play mode without a domain reload keeps statics
        Instance = null;
        FloorToggled = null;
        CountReady = null;
        CompanyManager.IncomeStream = null;
        CompanyManager.ExtraProductivity = null;
    }

    // ------------------------------------------------------------------
    // Lifecycle
    // ------------------------------------------------------------------

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[CashGrab] Removed a second CashGrabSystem (only one may save under '" + Key + "').");
            Destroy(this);
            return;
        }
        Instance = this;

        Sim = new CashFloorSim(Environment.TickCount);
        Ledger = new CashLedger(new CompanyBooks(), () => Sim.FloorValue, amount => Sim.TakeFromFloor(amount));
        Sim.Sink = Ledger;
        Stream = new CashStream(Ledger, Sim, Environment.TickCount ^ 0x5bd1e995);
        Ledger.Seized += OnSeized;

        toolLevels[(int)CashTool.Hands] = 1;   // everyone has hands
        ApplyEquipment();
    }

    void OnEnable()
    {
        if (Instance != this) return;
        CompanyManager.IncomeStream = Stream;
        CompanyManager.ExtraProductivity = BackendProductivity;
        DailyEconomyHooks.Register(this);
    }

    void OnDisable()
    {
        if (Instance != this) return;
        if (ReferenceEquals(CompanyManager.IncomeStream, Stream)) CompanyManager.IncomeStream = null;
        CompanyManager.ExtraProductivity = null;
        DailyEconomyHooks.Unregister(this);
    }

    void Start()
    {
        if (Instance != this) return;
        Audio = gameObject.AddComponent<CashAudio>();
        View = CashFloorView.Create(this);
        if (!Application.isBatchMode) CashDeskPeek.Create(this);
    }

    void OnDestroy()
    {
        if (Instance != this) return;
        UnhookCompany();
        if (View != null) Destroy(View.gameObject);
        Instance = null;
    }

    void HookCompany(CompanyManager company)
    {
        if (company == hookedCompany) return;
        UnhookCompany();
        hookedCompany = company;
        if (company == null) return;
        company.EmployeeHired += OnHired;
        company.StatsChanged += OnStatsChanged;
        company.DayEnded += OnDayEnded;
    }

    void UnhookCompany()
    {
        if (hookedCompany == null) return;
        hookedCompany.EmployeeHired -= OnHired;
        hookedCompany.StatsChanged -= OnStatsChanged;
        hookedCompany.DayEnded -= OnDayEnded;
        hookedCompany = null;
    }

    void OnHired(ApplicantData applicant) => Stream.Invalidate();
    void OnStatsChanged() => Stream.Invalidate();

    // ------------------------------------------------------------------
    // Update
    // ------------------------------------------------------------------

    void Update()
    {
        CompanyManager company = CompanyManager.Instance;
        HookCompany(company);
        if (company == null) return;

        if (!Ready) TryBecomeReady(company);
        if (!Ready) return;

        float dt = Time.deltaTime;
        bool clockRunning = company.ClockRunning;

        Stream.Tick(dt, clockRunning, company.currentDay, company.DayProgress, company.DayDuration, company.ProjectDayIncome);
        Sim.DropRate = Stream.RatePerSecond;
        Ledger.BonusReserve = Math.Max(0.0, Stream.ProjectedDay) * CashRules.BonusReserveDays;
        Sim.MarkedEnabled = company.currentDay >= CashRules.MarkedFromDay && !DemoMode.Active;
        UpdateHappyHour(company, clockRunning);

        layoutTimer -= Time.unscaledDeltaTime;
        if (layoutTimer <= 0f)
        {
            layoutTimer = 2f;
            UpdateLayout();
        }

        Sim.Visible = IsFloorOpen && clockRunning;
        if (View != null) View.FeedInput(Sim);
        else Sim.Input = default;
        if (clockRunning) Sim.Step(dt);

        HandleEvents();

        departmentTimer -= dt;
        if (departmentTimer <= 0f)
        {
            departmentTimer = 1f;
            UpdateDepartments(company);
        }

        Ledger.Enforce();

        resumTimer -= Time.unscaledDeltaTime;
        if (resumTimer <= 0f)
        {
            resumTimer = 30f;
            Sim.Resum();
        }

        if (todayDay != company.currentDay)
        {
            todayDay = company.currentDay;
            ResetToday();
        }
    }

    void ResetToday()
    {
        TodayCollected = 0.0;
        Array.Clear(todayTakes, 0, todayTakes.Length);
        todayHands = todayBestHand = todayStamps = 0;
        todayBonus = 0.0;
    }

    /// <summary>Happy hour: once a day, somewhere mid-day, while the CEO is on the floor.</summary>
    void UpdateHappyHour(CompanyManager company, bool clockRunning)
    {
        if (happyDay != company.currentDay)
        {
            happyDay = company.currentDay;
            happyAt = UnityEngine.Random.Range(CashRules.HappyHourFrom, CashRules.HappyHourTo);
            happyDone = false;
        }
        if (happyDone || !clockRunning || !IsFloorOpen || company.DayProgress < happyAt) return;
        happyDone = true;
        Sim.StartBoost(CashBoost.HappyHour);
    }

    // ------------------------------------------------------------------
    // The Count (design canvas: a tally at each day's end, the Count with a winner every CountEveryDays days)
    // ------------------------------------------------------------------

    void OnDayEnded(int newDay, double change)
    {
        if (!Ready) return;
        var count = new DayCount
        {
            day = Math.Max(1, newDay - 1),
            hands = todayHands,
            bestHand = todayBestHand,
            stamps = todayStamps,
            bonus = todayBonus
        };
        for (int s = 0; s < 4; s++)
        {
            count.takes[s] = todayTakes[s];
            weekTakes[s] += todayTakes[s];
        }
        weekDays++;
        if (weekDays >= CashRules.CountEveryDays)
        {
            count.weekly = true;
            int winner = -1;
            double best = 0.0;
            for (int s = 0; s < 4; s++)
            {
                if (weekTakes[s] <= best) continue;
                best = weekTakes[s];
                winner = s;
            }
            for (int s = 0; s < 4; s++) count.takes[s] = weekTakes[s];
            count.winner = winner;
            Array.Clear(weekTakes, 0, weekTakes.Length);
            weekDays = 0;

            // Last Count's perk ends; the winner gets the next one
            ActivePerk = CashPerk.None;
            WinnerDept = winner >= 0 && winner < 3 ? winner : -1;
            PerkPending = winner == 3;
            PerkPendingSince = Time.realtimeSinceStartup;
            ApplyPerks();
            Log("The Count: " + (winner == 3 ? "the CEO" : winner >= 0 ? CashRules.DeptIds[winner] : "nobody") + " won the week");
        }
        if (CompanyManager.Instance != null) todayDay = CompanyManager.Instance.currentDay;
        ResetToday();
        LastCount = count;
        try { CountReady?.Invoke(count); }
        catch (Exception e) { Debug.LogException(e); }
    }

    /// <summary>The CEO picks the perk they won at the Count (until the next Count). False when there is none to pick.</summary>
    public bool PickPerk(CashPerk perk)
    {
        if (!PerkPending || perk == CashPerk.None) return false;
        PerkPending = false;
        ActivePerk = perk;
        ApplyPerks();
        Log("Picked the perk " + perk);
        return true;
    }

    /// <summary>The perk window closed without a pick.</summary>
    public void DeclinePerk()
    {
        PerkPending = false;
    }

    void ApplyPerks()
    {
        Sim.SetSafeWiden(ActivePerk == CashPerk.WideSafe ? CashRules.WideSafeFactor : 1f);
        Sim.LuckyReels = ActivePerk == CashPerk.LuckyReels;
        for (int d = 0; d < CashLedger.DeptCount; d++)
        {
            float hire = 1f + Mathf.Min(CashRules.HireSpeedCap, deptHires[d] * CashRules.HireSpeedPerHire);
            float lunch = ActivePerk == CashPerk.LongLunch ? CashRules.LongLunchSpeed : 1f;
            float won = WinnerDept == d ? CashRules.WinnerSpeed : 1f;
            Sim.DeptSpeed[d] = hire * lunch * won;
        }
        Sim.ReachBonus = 1f + Mathf.Min(CashRules.InternReachCap, Interns * CashRules.InternReachPerHire);
    }

    /// <summary>Each hire works at one department's rail (by major) or on the CEO's side as an intern.</summary>
    void CountHires(CompanyManager company)
    {
        int b = 0, f = 0, m = 0, interns = 0;
        if (company.employees != null)
        {
            foreach (ApplicantData e in company.employees)
            {
                if (e == null) continue;
                switch (CashRules.DeptForMajor(e.major))
                {
                    case 0: b++; break;
                    case 1: f++; break;
                    case 2: m++; break;
                    default: interns++; break;
                }
            }
        }
        if (b == deptHires[0] && f == deptHires[1] && m == deptHires[2] && interns == Interns) return;
        deptHires[0] = b;
        deptHires[1] = f;
        deptHires[2] = m;
        Interns = interns;
        ApplyPerks();
    }

    /// <summary>
    /// The company's state is known once SaveSystem has applied the save (or started a new company). A save from before the
    /// cash floor gets a one-time petty-cash start; a new company a small one.
    /// </summary>
    void TryBecomeReady(CompanyManager company)
    {
        SaveSystem saves = SaveSystem.Instance;
        bool loaded = saves == null || saves.IsGameLoaded;
        if (!loaded) return;

        Ready = true;
        if (restoredThisLoad && pendingRestore != null)
        {
            ApplyRestore(pendingRestore);
            pendingRestore = null;
            Log("Restored: cash " + UIFormat.Money(Ledger.PlayerCash) + ", floor " + UIFormat.Money(Sim.FloorValue) + ", account " + UIFormat.Money(Ledger.CompanyAccount));
            return;
        }

        // No cash floor in the save: a new company, or a company from before the cash floor
        bool olderSave = company.currentDay > 1 || company.employees.Count > 0;
        double grant;
        string toastKey;
        if (olderSave)
        {
            grant = company.companyValue * 0.05;
            toastKey = "cash.toast.migrated";
        }
        else if (DemoMode.Active)
        {
            grant = 10000.0;
            toastKey = null;
        }
        else
        {
            grant = PlaytestMode.Active ? company.companyValue * 0.05 : Math.Min(company.companyValue, 1000.0);
            toastKey = null;
        }
        double given = Ledger.GrantFromAccount(Math.Floor(grant));
        Log((olderSave ? "A save from before the cash floor" : "A new company") + ": petty cash " + UIFormat.Money(given));
        if (olderSave && given > 0.0 && toastKey != null)
            GameNotifications.Show(CashText.Get(toastKey, ("amount", UIFormat.Money(given))), NotificationKind.Special);
        ApplyEquipment();
        UpdateLayout();
    }

    void UpdateLayout()
    {
        int seats = OfficeSeats();
        if (seats == lastSeats) return;
        lastSeats = seats;
        Sim.SetLayout(CashRules.FloorScale(seats), CashRules.TubeCount(seats));
    }

    /// <summary>Office seats of the HQ campus (the tower's capacity); 6 (the starter office) without a tower.</summary>
    public static int OfficeSeats()
    {
        TowerBuilder tower = TowerBuilder.Instance;
        return tower != null ? Math.Max(1, tower.TotalCapacity) : 6;
    }

    void HandleEvents()
    {
        if (Sim.Events.Count == 0) return;
        eventBuffer.Clear();
        eventBuffer.AddRange(Sim.Events);
        Sim.Events.Clear();
        bool visible = IsFloorOpen;
        foreach (CashEvent e in eventBuffer)
        {
            switch (e.type)
            {
                case CashEventType.DepositBatch:
                    Deposits++;
                    LifetimeCollected += e.value;
                    TodayCollected += e.value;
                    todayTakes[3] += e.value;
                    break;
                case CashEventType.Steal:
                    if (e.value > 0.0) LifetimeStolen += e.value;
                    break;
                case CashEventType.DeptDeposit:
                    if (e.dept >= 0 && e.dept < 3) todayTakes[e.dept] += e.value;
                    break;
                case CashEventType.BonusPaid:
                case CashEventType.PokerHand:
                    if (e.value > 0.0)
                    {
                        LifetimeBonus += e.value;
                        TodayCollected += e.value;
                        todayTakes[3] += e.value;
                        todayBonus += e.value;
                    }
                    if (e.type == CashEventType.PokerHand)
                    {
                        todayHands++;
                        todayBestHand = Math.Max(todayBestHand, e.tier);
                        BestHand = Math.Max(BestHand, e.tier);
                    }
                    break;
                case CashEventType.WrittenUp:
                    WrittenUpCount++;
                    todayStamps++;
                    if (e.value > 0.0)
                    {
                        LifetimeFines += e.value;
                        TodayCollected += e.value;
                        todayTakes[3] += e.value;
                    }
                    break;
                case CashEventType.MarkedBanked:
                    if (HeatMeter.Instance != null) HeatMeter.Instance.AddHeat(CashRules.MarkedHeat * Math.Max(1, e.count), "cash floor: marked bills");
                    break;
            }
            if (visible)
            {
                if (Audio != null) Audio.Handle(e, Sim);
                if (View != null) View.Handle(e);
            }
            else if (View != null)
            {
                View.HandleAway(e);
            }
        }
    }

    // ------------------------------------------------------------------
    // Departments
    // ------------------------------------------------------------------

    void UpdateDepartments(CompanyManager company)
    {
        int day = company.currentDay;
        int staff = company.employees != null ? company.employees.Count : 0;
        bool earning = staff > 0 || Stream.ProjectedDay > 0.0;
        CashDepartments.Tick(Ledger, deptLevels, day, staff, Deposits, Stream.ProjectedDay, earning, OnDeptChanged);
        CountHires(company);
        if (!Application.isBatchMode) CashCvSlip.AttachAll();   // the CVs' floor-impact slips (papers spawn as batches arrive)
    }

    void OnDeptChanged(int dept, int before, int after)
    {
        Sim.SetDeptLevel(dept, after);
        Sim.Events.Add(new CashEvent(before == 0 ? CashEventType.DeptJoined : CashEventType.DeptLevelUp, Sim.DeptCenter[dept], 0.0, 0, dept, after));
        Log(before == 0 ? CashRules.DeptIds[dept] + " joined the cash floor" : CashRules.DeptIds[dept] + " bought level " + after + " with its budget");
    }

    void SetDeptLevel(int dept, int level)
    {
        deptLevels[dept] = Mathf.Clamp(level, 0, CashRules.MaxDeptLevel);
        Sim.SetDeptLevel(dept, deptLevels[dept]);
    }

    /// <summary>The next level's price for a department (∞ at the top).</summary>
    public double NextDeptCost(int dept)
    {
        int level = deptLevels[dept];
        if (level <= 0 || level >= CashRules.MaxDeptLevel) return double.PositiveInfinity;
        return CashRules.LevelCost(level + 1, Math.Max(0.0, Stream.ProjectedDay));
    }

    /// <summary>The Backend department's productivity perk (CompanyManager.ExtraProductivity).</summary>
    float BackendProductivity()
    {
        return deptLevels[(int)CashDept.Backend] * CashRules.PerkPerLevel(CashDept.Backend);
    }

    // IDailyEconomyHook: Frontend makes the office nicer to work in, Marketing keeps investors warm
    public string HookName => "cash-departments";

    public double DailyMoney(CompanyManager company, double resultBeforeHooks) => 0.0;

    public void DailyMeters(CompanyManager company, ref float happiness, ref float trust)
    {
        happiness += deptLevels[(int)CashDept.Frontend] * CashRules.PerkPerLevel(CashDept.Frontend);
        trust += deptLevels[(int)CashDept.Marketing] * CashRules.PerkPerLevel(CashDept.Marketing);
    }

    // ------------------------------------------------------------------
    // Player Cash (PlayerWallet)
    // ------------------------------------------------------------------

    public bool SpendPlayerCash(double amount, string source)
    {
        if (!Ledger.SpendPlayer(amount)) return false;
        LifetimeSpent += amount;
        if (logEvents) Debug.Log("[CashGrab] " + (source ?? "?") + " spent " + UIFormat.Money(amount) + " of Player Cash (left " + UIFormat.Money(Ledger.PlayerCash) + ")");
        return true;
    }

    public void ReceivePlayerCash(double amount, string source)
    {
        Ledger.ReceivePlayer(amount);
        if (logEvents) Debug.Log("[CashGrab] " + (source ?? "?") + " paid " + UIFormat.Money(amount) + " into Player Cash (now " + UIFormat.Money(Ledger.PlayerCash) + ")");
    }

    void OnSeized(double fromFloor, double fromPlayer, double fromDepts)
    {
        if (logEvents)
            Debug.Log("[CashGrab] The company account ran short: took " + UIFormat.Money(fromDepts) + " from budgets, " + UIFormat.Money(fromFloor) +
                      " from the floor, " + UIFormat.Money(fromPlayer) + " from Player Cash.");
        if (fromPlayer < 1.0 || Time.realtimeSinceStartup - lastSeizeToast < 6f) return;
        lastSeizeToast = Time.realtimeSinceStartup;
        GameNotifications.Show(CashText.Get("cash.toast.seized", ("amount", UIFormat.Money(fromPlayer))), NotificationKind.Bad);
        Sim.Events.Add(new CashEvent(CashEventType.Seized, Sim.SafeCenter, fromPlayer));
    }

    // ------------------------------------------------------------------
    // Equipment
    // ------------------------------------------------------------------

    public enum BuyResult
    {
        Bought,
        MaxLevel,
        Locked,
        NotEnoughCash,
        NotReady
    }

    /// <summary>Tool unlocked for buying (enough office seats).</summary>
    public bool ToolUnlocked(CashTool tool) => OfficeSeats() >= CashRules.ToolSeatsNeeded(tool);

    /// <summary>Gadget unlocked for buying (the department it counters has shown up; gloves after the first shove or yoink).</summary>
    public bool GadgetUnlocked(CashGadget gadget)
    {
        switch (gadget)
        {
            case CashGadget.Stapler: return deptLevels[0] > 0;
            case CashGadget.Paperweight: return deptLevels[1] > 0 || deptLevels[2] > 0;
            case CashGadget.Fan: return deptLevels[2] > 0 || deptLevels[0] >= 2;
            case CashGadget.Gloves: return deptLevels[1] >= 2 || deptLevels[2] >= 2 || LifetimeStolen > 0.0;
            default: return false;
        }
    }

    public BuyResult BuyTool(CashTool tool)
    {
        if (!Ready) return BuyResult.NotReady;
        int level = toolLevels[(int)tool];
        if (level >= CashRules.MaxLevel(tool)) return BuyResult.MaxLevel;
        if (level == 0 && !ToolUnlocked(tool)) return BuyResult.Locked;
        double price = CashRules.ToolPrice(tool, level);
        if (price < 0.0) return BuyResult.MaxLevel;
        if (price > 0.0 && !SpendPlayerCash(price, "the cash floor shop (" + tool + ")")) return BuyResult.NotEnoughCash;
        toolLevels[(int)tool] = level + 1;
        selectedTool = tool;
        ApplyEquipment();
        Log("Bought " + tool + " level " + (level + 1) + " for " + UIFormat.Money(price));
        return BuyResult.Bought;
    }

    public BuyResult BuyGadget(CashGadget gadget)
    {
        if (!Ready || gadget == CashGadget.None) return BuyResult.NotReady;
        int level = gadgetLevels[(int)gadget];
        if (level >= CashRules.MaxGadgetLevel) return BuyResult.MaxLevel;
        if (level == 0 && !GadgetUnlocked(gadget)) return BuyResult.Locked;
        double price = CashRules.GadgetPrice(gadget, level);
        if (price < 0.0) return BuyResult.MaxLevel;
        if (!SpendPlayerCash(price, "the cash floor shop (" + gadget + ")")) return BuyResult.NotEnoughCash;
        gadgetLevels[(int)gadget] = level + 1;
        if (gadget != CashGadget.Gloves && (selectedGadget == CashGadget.None || level == 0)) selectedGadget = gadget;
        ApplyEquipment();
        Log("Bought " + gadget + " level " + (level + 1) + " for " + UIFormat.Money(price));
        return BuyResult.Bought;
    }

    public bool SelectTool(CashTool tool)
    {
        if (toolLevels[(int)tool] <= 0 || tool == selectedTool) return false;
        Sim.DropHeld();
        selectedTool = tool;
        ApplyEquipment();
        return true;
    }

    /// <summary>The next owned tool up or down the row (mouse wheel).</summary>
    public bool CycleTool(int direction)
    {
        if (direction == 0) return false;
        int start = (int)selectedTool;
        for (int step = 1; step <= CashRules.ToolCount; step++)
        {
            int t = ((start + step * Math.Sign(direction)) % CashRules.ToolCount + CashRules.ToolCount) % CashRules.ToolCount;
            if (toolLevels[t] > 0) return SelectTool((CashTool)t);
        }
        return false;
    }

    public bool SelectGadget(CashGadget gadget)
    {
        if (gadget == CashGadget.None || gadget == CashGadget.Gloves || gadgetLevels[(int)gadget] <= 0 || gadget == selectedGadget) return false;
        selectedGadget = gadget;
        ApplyEquipment();
        return true;
    }

    void ApplyEquipment()
    {
        if (Sim == null) return;
        if (toolLevels[(int)selectedTool] <= 0) selectedTool = CashTool.Hands;
        Sim.tool = selectedTool;
        Sim.toolLevel = Math.Max(1, toolLevels[(int)selectedTool]);
        for (int g = 0; g < CashRules.GadgetCount; g++) Sim.gadgetLevel[g] = gadgetLevels[g];
        if (selectedGadget != CashGadget.None && gadgetLevels[(int)selectedGadget] <= 0) selectedGadget = CashGadget.None;
        Sim.gadget = selectedGadget;
    }

    // ------------------------------------------------------------------
    // Opening the floor (the view and the desk chip call these)
    // ------------------------------------------------------------------

    public static void NotifyFloorToggled(bool open)
    {
        try { FloorToggled?.Invoke(open); }
        catch (Exception e) { Debug.LogException(e); }
    }

    // ------------------------------------------------------------------
    // Saving ("cashgrab")
    // ------------------------------------------------------------------

    [Serializable]
    class SavedCash
    {
        public int version = 2;
        /// <summary>The save's company value leaves out mintedToday (SaveSystem saves CompanyManager.BookedValue): add it back.</summary>
        public bool valueExcludesAdvance;
        public double playerCash;
        public double[] budgets = new double[CashLedger.DeptCount];
        public double[] deptLifetime = new double[CashLedger.DeptCount];
        public int[] deptLevels = new int[CashLedger.DeptCount];
        public double floorCash;          // every bill on the floor (and a payday still in the tubes)
        public double mintedToday;        // income already on the books for the current day (SettleDay takes it back)
        public int mintedDay;
        public int[] toolLevels = new int[CashRules.ToolCount];
        public int[] gadgetLevels = new int[CashRules.GadgetCount];
        public int tool;
        public int gadget = -1;
        public int deposits;
        public double lifetimeCollected;
        public double lifetimeStolen;
        public double lifetimeSpent;
        public int bestStreak;
        // Version 3 (2026-11-24, the design canvas): the Count, perks, hands, the stamp
        public double[] weekTakes = new double[4];
        public int weekDays;
        public int perk;
        public int winnerDept = -1;
        public bool perkPending;
        public double lifetimeBonus;
        public double lifetimeFines;
        public int writtenUp;
        public int bestHand;
    }

    public string SaveKey => Key;

    public string CaptureState()
    {
        var s = new SavedCash
        {
            valueExcludesAdvance = true,
            playerCash = Ledger.PlayerCash,
            budgets = Ledger.BudgetsCopy(),
            deptLifetime = Ledger.LifetimeCopy(),
            deptLevels = (int[])deptLevels.Clone(),
            floorCash = Sim.FloorValue,
            mintedToday = Stream.MintedToday,
            mintedDay = Stream.MintedDay,
            toolLevels = (int[])toolLevels.Clone(),
            gadgetLevels = (int[])gadgetLevels.Clone(),
            tool = (int)selectedTool,
            gadget = (int)selectedGadget,
            deposits = Deposits,
            lifetimeCollected = LifetimeCollected,
            lifetimeStolen = LifetimeStolen,
            lifetimeSpent = LifetimeSpent,
            bestStreak = Sim.BestStreak,
            weekTakes = (double[])weekTakes.Clone(),
            weekDays = weekDays,
            perk = (int)ActivePerk,
            winnerDept = WinnerDept,
            perkPending = PerkPending,
            lifetimeBonus = LifetimeBonus,
            lifetimeFines = LifetimeFines,
            writtenUp = WrittenUpCount,
            bestHand = BestHand
        };
        s.version = 3;
        return JsonUtility.ToJson(s);
    }

    public void RestoreState(string json)
    {
        SavedCash s = null;
        try { s = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<SavedCash>(json); }
        catch (Exception e) { Debug.LogWarning("[CashGrab] Couldn't read the saved cash floor: " + e.Message); }
        if (s == null) return;
        restoredThisLoad = true;
        pendingRestore = s;
        // SaveSystem restores participants after the company value is in place: apply now when we're already running
        if (Ready)
        {
            ApplyRestore(s);
            pendingRestore = null;
        }
    }

    void ApplyRestore(SavedCash s)
    {
        Ledger.Restore(s.playerCash, s.budgets, s.deptLifetime);
        Stream.Restore(s.mintedToday, s.mintedDay);
        // The save stored company value without today's advance (CompanyManager.BookedValue): the advance's bills are part
        // of the saved floor, so their money goes back on the books
        CompanyManager company = CompanyManager.Instance;
        if (s.valueExcludesAdvance && s.mintedToday > 0.0 && company != null) company.AddMoney(s.mintedToday, true);
        Array.Clear(toolLevels, 0, toolLevels.Length);
        Array.Clear(gadgetLevels, 0, gadgetLevels.Length);
        if (s.toolLevels != null)
            for (int t = 0; t < toolLevels.Length && t < s.toolLevels.Length; t++) toolLevels[t] = Mathf.Clamp(s.toolLevels[t], 0, CashRules.MaxLevel((CashTool)t));
        toolLevels[(int)CashTool.Hands] = 1;
        if (s.gadgetLevels != null)
            for (int g = 0; g < gadgetLevels.Length && g < s.gadgetLevels.Length; g++) gadgetLevels[g] = Mathf.Clamp(s.gadgetLevels[g], 0, CashRules.MaxGadgetLevel);
        selectedTool = (CashTool)Mathf.Clamp(s.tool, 0, CashRules.ToolCount - 1);
        selectedGadget = s.gadget >= 0 && s.gadget < CashRules.GadgetCount ? (CashGadget)s.gadget : CashGadget.None;
        Deposits = Math.Max(0, s.deposits);
        LifetimeCollected = Math.Max(0.0, s.lifetimeCollected);
        LifetimeStolen = Math.Max(0.0, s.lifetimeStolen);
        LifetimeSpent = Math.Max(0.0, s.lifetimeSpent);
        Array.Clear(weekTakes, 0, weekTakes.Length);
        if (s.weekTakes != null)
            for (int k = 0; k < weekTakes.Length && k < s.weekTakes.Length; k++) weekTakes[k] = double.IsNaN(s.weekTakes[k]) ? 0.0 : Math.Max(0.0, s.weekTakes[k]);
        weekDays = Mathf.Clamp(s.weekDays, 0, CashRules.CountEveryDays - 1);
        ActivePerk = s.perk >= 0 && s.perk <= (int)CashPerk.LongLunch ? (CashPerk)s.perk : CashPerk.None;
        // Saves before version 3 have no winner: JsonUtility leaves the field at 0 (Backend), so read it only from v3 on
        WinnerDept = s.version >= 3 && s.winnerDept >= 0 && s.winnerDept < 3 ? s.winnerDept : -1;
        PerkPending = s.perkPending;
        LifetimeBonus = Math.Max(0.0, s.lifetimeBonus);
        LifetimeFines = Math.Max(0.0, s.lifetimeFines);
        WrittenUpCount = Math.Max(0, s.writtenUp);
        BestHand = Mathf.Clamp(s.bestHand, 0, (int)CashPoker.Rank.RoyalFlush);

        Sim.ClearFloor();
        UpdateLayout();
        for (int d = 0; d < CashLedger.DeptCount; d++)
            SetDeptLevel(d, s.deptLevels != null && d < s.deptLevels.Length ? s.deptLevels[d] : 0);
        Sim.RestoreFloor(Math.Max(0.0, s.floorCash));
        Sim.RestoreBestStreak(s.bestStreak);
        ApplyEquipment();
        ApplyPerks();
        Ledger.Enforce();   // a save that doesn't add up (edited, very old) is made consistent right away
    }

    void Log(string message)
    {
        if (logEvents) Debug.Log("[CashGrab] " + message);
    }

    // ------------------------------------------------------------------

    /// <summary>The company's books for the ledger: CompanyManager's company value.</summary>
    sealed class CompanyBooks : ICashBooks
    {
        public double Value => CompanyManager.Instance != null ? CompanyManager.Instance.companyValue : 0.0;

        public void AddValue(double delta, bool instant)
        {
            CompanyManager company = CompanyManager.Instance;
            if (company != null) company.AddMoney(delta, instant);
        }
    }
}

/// <summary>
/// Adds the cash floor (CashGrabSystem) to every scene that has a CompanyManager, when it loads, unless it's already there.
/// So MainGame needs no scene edit.
/// </summary>
static class CashGrabBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Ensure();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Ensure();
    }

    static void Ensure()
    {
        if (!Application.isPlaying) return;
        CompanyManager company = CompanyManager.Instance != null ? CompanyManager.Instance : UnityEngine.Object.FindAnyObjectByType<CompanyManager>();
        if (company == null) return;
        if (CashGrabSystem.Instance != null || UnityEngine.Object.FindAnyObjectByType<CashGrabSystem>(FindObjectsInactive.Include) != null) return;

        var go = new GameObject("Cash Floor (Runtime)");
        Scene scene = company.gameObject.scene;
        if (scene.IsValid() && scene.isLoaded && go.scene != scene)
        {
            // Live and die with the company's scene (and be found by SaveSystem before it restores the scene)
            try { SceneManager.MoveGameObjectToScene(go, scene); }
            catch (Exception e) { Debug.LogWarning("[CashGrab] Couldn't move the cash floor next to the CompanyManager: " + e.Message); }
        }
        go.AddComponent<CashGrabSystem>();
    }
}
