using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class CompanyManager : MonoBehaviour
{
    public static CompanyManager Instance;

    public List<ApplicantData> employees = new List<ApplicantData>();

    public float averageGPA;
    public float averageIQ;
    public float averageAge;
    public float averageExperience;

    public long totalSalary;
    public int totalEmployees;

    public int maleCount;
    public int femaleCount;
    public float maleToFemaleRatio;

    public Dictionary<string, int> raceCount = new Dictionary<string, int>();
    public string mostCommonRace = "None";
    public string leastCommonRace = "None";

    // Company value system. Doubles: a float can't count single dollars above ~$16.7M (and $1B is a goal).
    public double companyValue = 50000;
    public double displayedValue = 50000; // The value shown (animates toward companyValue)
    private const double STARTING_VALUE = 50000;
    public int currentDay = 1;

    // Employee Happiness (0-100): Affected by completed tasks and hired employee traits
    public float employeeHappiness = 50f;
    // Investor Trust (0-100): Affected by earnings, risk cards, minigames, and successful upgrades
    public float investorTrust = 50f;

    // Baseline averages for comparison
    private const float BASELINE_IQ = 100f;
    private const float BASELINE_GPA = 2.0f;  // Below 2.0 GPA = loses money
    private const float BASELINE_EXPERIENCE = 5f;

    // Caps that stop skill effects from compounding out of control (a day is only 15-45 seconds)
    private const float MAX_PROFIT_GROWTH = 5f;                // growth + interest skills together: at most +500% of the day's profit (all of them add up to +341%)
    private const float MAX_MERGER_BONUS_PER_EMPLOYEE = 1250f; // MergerBonus per employee per day (both merger skills)
    private const float DEFAULT_TEAM_SYNERGY_GROUP = 5f;       // employees per Team Synergy step if the skill doesn't say
    private const int MIN_WORKING_AGE = 16;                    // Skill Development can't push experience past age - 16

    // Employee output
    private const float BASE_EMPLOYEE_REVENUE = 250f;          // daily output of an average employee
    private const float MAX_PRODUCTIVITY_BONUS = 1.5f;         // productivity skills + office items + buildings together (+150%)
    private const float MENTORSHIP_BASE_GROWTH = 0.05f;        // Mentorship's learning pace for juniors without Skill Development
    private const int MENTORSHIP_MAX_EXPERIENCE = 5;           // "new employees": under 5 years of experience
    private const float MAX_RETENTION = 0.6f;                  // Employee Retention can soften happiness losses by at most 60%
    private const double BASE_OVERHEAD = 100.0;                // daily overhead (office, utilities...) of any company with staff
    private const double OVERHEAD_PER_EMPLOYEE = 20.0;         // ...plus this much per employee
    private const float CRIMINAL_INCIDENT_CHANCE = 0.05f;      // daily chance that an employee with a record causes trouble
    private const float CRIMINAL_INCIDENT_MIN = 500f;          // what the trouble costs: theft, damage, lawsuits
    private const float CRIMINAL_INCIDENT_MAX = 2000f;

    // Happiness and investor trust (0-100, neutral at 50)
    private const float HAPPINESS_MIN_MULTIPLIER = 0.85f;      // employee output at 0 happiness (1.0 at 50)
    private const float HAPPINESS_MAX_MULTIPLIER = 1.15f;      // employee output at 100 happiness
    private const float TRUST_MAX_GOOD_CHANCE = 0.05f;         // risk-card good chance: -5 points at 0 trust, +5 at 100
    private const float METER_DRIFT_PER_DAY = 0.03f;           // both meters settle this share of the way back to 50 each day
    private const float MAX_BUILDING_HAPPINESS_PER_DAY = 5f;   // happiness per day from buildings' happinessBonus, all together
    private const float MAX_BUILDING_TRUST_PER_DAY = 5f;       // investor trust per day from the HQ tower's meeting rooms, all together
    private const float LOW_MORALE = 25f;                      // below this, a notification explains the output penalty
    private const float HIGH_MORALE = 75f;                     // at or above this, one explains the bonus
    private const float MORALE_HYSTERESIS = 5f;                // no repeat notification while hovering around a threshold

    // Chaos branch daily luck. Money scales with company size: 1% of its value, between $500 and $250,000.
    private const double LUCK_BONUS_SHARE = 0.01;
    private const double LUCK_BONUS_MIN = 500;
    private const double LUCK_BONUS_MAX = 250000;
    private const float MAX_LUCK_CHANCE = 0.9f;                // no daily chance goes above 90%, even with Serendipity
    private const float FORTUNE_COOKIE_CHANCE = 0.30f;         // +0.5-2x base
    private const float LOTTERY_CHANCE = 0.05f;                // +10x base
    private const float LOTTERY_PRIZE = 10f;
    private const float MYSTERY_BOX_CHANCE = 0.25f;            // -0.5x to +2x base
    private const float SURPRISE_BONUS_CHANCE = 0.15f;         // +2x base
    private const float SURPRISE_BONUS_PRIZE = 2f;
    private const float RANDOM_RAISE_CHANCE = 0.25f;           // +5-15% output that day and +3 happiness
    private const float RANDOM_RAISE_MIN = 0.05f;
    private const float RANDOM_RAISE_MAX = 0.15f;
    private const float RANDOM_RAISE_HAPPINESS = 3f;
    private const float TAROT_METER_BONUS = 5f;                // Tarot's happiness or trust card
    private const int KARMA_INTERVAL_DAYS = 5;                 // Karma Bonus: base x (1 + employees / 20) every 5th day
    private const float KARMA_EMPLOYEES_PER_STEP = 20f;

    // Skills that add to employee output ("+X% productivity"). Summed, then capped together with office items and buildings.
    static readonly SkillEffect[] OutputSkillEffects =
    {
        SkillEffect.EmployeeProductivity,   // Office Efficiency
        SkillEffect.WorkspaceOptimization,
        SkillEffect.FlexibleHours,
        SkillEffect.WorkFromHome,
        SkillEffect.PerformanceReviews,
        SkillEffect.SkillCertification,
        SkillEffect.LeanManagement,
        SkillEffect.SixSigma,
        SkillEffect.TimeTracking,
        SkillEffect.CollaborationTools,
        SkillEffect.CommunicationBoost,
        SkillEffect.StressReduction,
        SkillEffect.EmployeeReferral,
        SkillEffect.CultureFit
    };

    // Fortune Cookie's fortunes: their Company table keys and English (CompanyText; the English shows when a key is missing)
    static readonly (string key, string english)[] Fortunes =
    {
        ("company.fortune.surprise", "A pleasant surprise is waiting for you."),
        ("company.fortune.hard_work", "Your hard work will soon pay off."),
        ("company.fortune.opportunity", "Opportunity knocks. Answer the door."),
        ("company.fortune.hire_wisely", "Money flows to those who hire wisely."),
        ("company.fortune.lucky_numbers", "Your lucky numbers are 4, 8, 15, 16, 23, 42."),
        ("company.fortune.printer", "Avoid the printer today.")
    };

    // Tarot Card's money cards (Wheel of Fortune, The Star, Ace of Pentacles): one luck line each in the Company table
    static readonly string[] TarotMoneyKeys = { "company.luck.tarot.wheel", "company.luck.tarot.star", "company.luck.tarot.pentacles" };

    static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

    [Tooltip("Write each day's income breakdown to the Console (handy for balancing)")]
    public bool logDailyBreakdown = true;

    /// <summary>Where one day's money came from. Amounts that cost money are positive numbers.</summary>
    [System.Serializable]
    public struct DayBreakdown
    {
        public int day;                     // the day that ended
        public double employeeOutput;       // what employees produced (productivity, happiness and Team Synergy included)
        public double salaries;
        public double incidents;            // criminal-record trouble
        public double marketAndSynergy;     // random market swing + headcount synergy
        public double robots;               // Automation Initiative
        public double officeItems;          // upgraded desk items
        public double buildings;            // placed offices and their furniture
        public double recurring;            // registered daily systems (DailyEconomyHooks): upkeep, policies, revenue shares
        public double overhead;
        public double skillGrowth;          // daily value growth, interest and merger bonus
        public double skillLuck;            // chaos branch daily luck (can be negative)
        public double operatingResult;      // output - costs + flat bonuses (what investor trust reacts to)
        public double total;                // everything the day tick added (hires, events and trades are extra)
        public float productivityBonus;     // capped: 0.5 = +50%
        public float happinessMultiplier;
        public float randomRaise;           // Random Raise bonus on that day's output (0 = none)
    }

    /// <summary>Breakdown of the last finished day (for a stats screen).</summary>
    public DayBreakdown LastDayBreakdown { get; private set; }

    /// <summary>
    /// One employee's share of a day: exactly what ProcessNewDay added for them (the staff files, StaffLedger, keep
    /// these). Costs are positive numbers. The market swing, headcount synergy, robots, office items, buildings, the
    /// fixed $100 overhead, growth/interest skills and luck belong to the company, not to anyone in particular.
    /// </summary>
    public struct EmployeeDay
    {
        public ApplicantData employee;
        public double output;        // what they produced (productivity, happiness, Random Raise and Team Synergy included)
        public double salary;        // salary cost (Tax Optimization and Salary Roulette included)
        public double incidents;     // criminal-record trouble they caused that day
        public double overhead;      // their share of the overhead ($20 per employee)
        public double perHeadBonus;  // skill money paid per employee (Merger Mentality, Growth Through Scale)
        public double Cost => salary + incidents + overhead;
        public double Profit => output + perHeadBonus - Cost;
    }

    /// <summary>What ProcessNewDay applies to every employee's day (GetEmployeeDayFactors: as things stand now).</summary>
    public struct EmployeeDayFactors
    {
        public float productivityBonus;    // skills, office items and buildings, capped: 0.5 = +50%
        public float happinessMultiplier;  // 0.85 at 0 happiness, 1.15 at 100
        public float happiness;            // the staff's happiness behind it (a new hire's projection moves it: ProjectEmployeeDay)
        public float qualityControl;       // share of a below-average employee's shortfall forgiven
        public float iqSynergyBonus;       // IQ Synergy: $ per IQ point over 100
        public float phdBonus;             // PhD Appreciation: share added to a PhD's output
        public float taxOptimization;      // share of salaries saved
        public float teamSynergyRate;      // Team Synergy: output share per group of employees
        public float teamSynergyGroup;     // employees per Team Synergy group
        public float perHeadBonus;         // Merger Mentality etc.: $ per employee per day (capped)
        /// <summary>Productivity x happiness (the day's Random Raise multiplies on top).</summary>
        public float OutputMultiplier => (1f + productivityBonus) * happinessMultiplier;
    }

    private readonly List<EmployeeDay> lastDayEmployees = new List<EmployeeDay>();
    /// <summary>Each employee's share of the last finished day (read it on DayEnded). Empty on a day without staff.</summary>
    public IReadOnlyList<EmployeeDay> LastDayEmployees => lastDayEmployees;

    // The day's luck messages (chaos skills), posted together as one notification when the day completes
    private readonly List<string> luckLines = new List<string>();
    private bool luckHasGood, luckHasBad, luckHasSpecial;
    private int moraleBand;   // -1 low, 0 normal, 1 high (for the morale notifications)

    // Day timer
    private float dayTimer;
    private float dayDuration;

    // Value animation (unscaled time, so the HUD settles even while a panel pauses the game)
    [Tooltip("Share of the gap between shown and real value closed per second (0.15 = 15%/s)")]
    [SerializeField] private float catchUpSpeed = 0.15f;
    [Tooltip("Minimum animation speed in $/s, for small changes")]
    [SerializeField] private float minChangePerSecond = 10f;
    private float minChangePerSecondShare = 0.0005f; // ...or 0.05% of the company value per second, whichever is bigger

    // Cosmetic real-time movement (employees constantly working). It is a random offset that keeps
    // being pulled back toward zero, so the value visibly moves but nothing is added over time:
    // income is only counted once, in ProcessNewDay.
    private float realtimeTimer = 0f;
    private float realtimeInterval = 0.5f;    // Update every 0.5 seconds
    private float ambientOffset = 0f;
    private const float AMBIENT_NOISE_PER_EMPLOYEE = 20f; // wiggle size, scaled by sqrt(employees)
    private const float AMBIENT_REVERSION = 0.25f;        // share of the offset pulled back each tick
    public bool hasHiredFirst = false;        // Track if first hire happened (public for save system)

    // Daily change tracking (for the HUD)
    private int baselineDay;

    /// <summary>Company value when the current day started.</summary>
    public double ValueAtDayStart { get; private set; }
    /// <summary>Net value change over the last finished day (income, hires, events... everything).</summary>
    public double LastDayChange { get; private set; }
    /// <summary>How far the current day is (0..1).</summary>
    public float DayProgress => dayDuration > 0f ? Mathf.Clamp01(dayTimer / dayDuration) : 0f;
    /// <summary>Length of the current day in seconds of office time (15-45 s, the demo's own range in the demo).</summary>
    public float DayDuration => dayDuration;
    /// <summary>True while the office clock runs (no UIManager panel open): the day, its income and the cash floor move.</summary>
    public bool ClockRunning => (!UIManager.Exists || !UIManager.Instance.IsAnyPanelOpen()) && Time.timeScale > 0f;

    public delegate void OnCompanyStatsChanged();
    public event OnCompanyStatsChanged StatsChanged;

    public delegate void OnValueChanging(double change);
    public event OnValueChanging ValueChanging; // Fires every frame when value is animating

    /// <summary>Fires after SaveSystem has written loaded save data into this manager.</summary>
    public event System.Action StateLoaded;

    /// <summary>Fires when a new day starts: (new day number, net value change over the day that just ended).</summary>
    public event System.Action<int, double> DayEnded;

    /// <summary>Fires after an applicant is hired and added to employees (isGolden already includes Lucky Hire).</summary>
    public event System.Action<ApplicantData> EmployeeHired;

    /// <summary>
    /// Cash Grab: a stream that delivers the day's income during the day (as bills on the cash floor, CashStream) instead of
    /// as one lump when the day ends. It already put that money on the books bill by bill; at the end of the day
    /// ProcessNewDay books the day as always and then asks the stream to settle: the stream takes back what it delivered
    /// in advance (so nothing is counted twice) and delivers any remainder. Null = the day's income is booked at day end.
    /// </summary>
    public interface IDayIncomeStream
    {
        /// <summary>
        /// The day's books just added <paramref name="booked"/> (day.total after the $0 floor). Returns how much of that
        /// the stream had already added to company value during the day, which ProcessNewDay takes back out.
        /// </summary>
        double SettleDay(double booked, int endedDay);

        /// <summary>What the stream has added to company value in advance for the current day (SettleDay takes it back).</summary>
        double Provisional { get; }
    }

    /// <summary>The cash floor's income stream (CashGrabSystem sets it while it runs). See IDayIncomeStream.</summary>
    public static IDayIncomeStream IncomeStream;

    /// <summary>
    /// Company value as the books stand at the start of the current day's settlement: companyValue without the income the
    /// cash floor delivered in advance today. Saves store this (so the cloud save's server keeps seeing day income arrive
    /// when the day ends, as it always did); the cash floor puts the advance back when the save loads.
    /// </summary>
    public double BookedValue
    {
        get
        {
            IDayIncomeStream stream = IncomeStream;
            double advance = stream != null ? stream.Provisional : 0.0;
            if (double.IsNaN(advance) || advance < 0.0) advance = 0.0;
            return System.Math.Max(0.0, companyValue - advance);
        }
    }

    /// <summary>
    /// Cash Grab: extra employee productivity from outside the skill tree and the buildings (the Backend department's
    /// upgrades), as a share (0.06 = +6%). It counts inside the +150% productivity cap like everything else.
    /// </summary>
    public static System.Func<float> ExtraProductivity;

    private HiringTaskManager subscribedTaskManager;
    private MiniGameManager subscribedMiniGameManager;


    private void Awake()
    {
        Instance = this;

        // Initialize starting values here rather than in Start, so a save applied from another
        // script's Start can't be overwritten afterwards (playtest builds start richer: PlaytestMode)
        double start = DemoMode.StartingValueFor(STARTING_VALUE);   // Demo: the demo's start, else playtest's, else $50k
        companyValue = start;
        displayedValue = start;
        currentDay = 1;
        ResetDayBaseline();

        if (DemoMode.Active) logDailyBreakdown = false;   // Demo: keep the demo's Player.log short

        SetNewDayDuration();
    }

    private void OnDestroy()
    {
        if (subscribedTaskManager != null)
            subscribedTaskManager.TaskCompleted -= OnTaskCompleted;
        if (subscribedMiniGameManager != null)
            subscribedMiniGameManager.MiniGameCompleted -= OnMiniGameCompleted;

        if (Instance == this)
            Instance = null;
    }

    /// <summary>Called by SaveSystem after it applies loaded data, so UI can resync.</summary>
    public void NotifyStateLoaded()
    {
        ambientOffset = 0f;
        realtimeTimer = 0f;
        RecalculateStats();
        ResetDayBaseline();
        moraleBand = GetMoraleBand(0); // a loaded game doesn't announce its morale

        StateLoaded?.Invoke();
        StatsChanged?.Invoke();
    }

    private void Start()
    {
        // Subscribe to game events for happiness/trust tracking
        SubscribeToGameEvents();
    }

    void SubscribeToGameEvents()
    {
        // Subscribe to hiring task events
        if (HiringTaskManager.Instance != null)
        {
            subscribedTaskManager = HiringTaskManager.Instance;
            subscribedTaskManager.TaskCompleted += OnTaskCompleted;
        }

        // Subscribe to minigame events
        if (MiniGameManager.Instance != null)
        {
            subscribedMiniGameManager = MiniGameManager.Instance;
            subscribedMiniGameManager.MiniGameCompleted += OnMiniGameCompleted;
        }
    }

    void OnTaskCompleted(HiringTask task, bool success, int moneyChange)
    {
        if (success)
        {
            AddHappiness(5f);
            investorTrust += 3f;
        }
        else
        {
            AddHappiness(-3f);
            investorTrust -= 5f;
        }
        ClampHappinessAndTrust();
        StatsChanged?.Invoke();
    }

    void OnMiniGameCompleted(bool success, float reward)
    {
        if (success)
        {
            AddHappiness(3f);
            investorTrust += Mathf.Clamp(reward / 500f, 1f, 5f);
        }
        else
        {
            AddHappiness(-4f);
            investorTrust -= Mathf.Clamp(Mathf.Abs(reward) / 500f, 2f, 8f);
        }
        ClampHappinessAndTrust();
        StatsChanged?.Invoke();
    }

    /// <summary>
    /// Called by EventManager when a risk card resolves
    /// </summary>
    public void OnRiskCardResolved(bool isGood, double moneyChange)
    {
        if (isGood)
        {
            investorTrust += Mathf.Clamp((float)(moneyChange / 1000.0), 1f, 10f);
            AddHappiness(2f);
        }
        else
        {
            investorTrust -= Mathf.Clamp((float)(System.Math.Abs(moneyChange) / 1000.0), 2f, 15f);
            AddHappiness(-5f);
        }
        ClampHappinessAndTrust();
        StatsChanged?.Invoke();
    }

    void ClampHappinessAndTrust()
    {
        employeeHappiness = Mathf.Clamp(employeeHappiness, 0f, 100f);
        investorTrust = Mathf.Clamp(investorTrust, 0f, 100f);
    }

    /// <summary>
    /// Change employee happiness (clamped to 0-100). Losses are smaller with Employee Retention.
    /// Doesn't raise StatsChanged: callers do that when they're done.
    /// </summary>
    public void AddHappiness(float amount)
    {
        if (amount < 0f && SkillTreeManager.Instance != null)
        {
            float retention = Mathf.Clamp(SkillTreeManager.Instance.GetEffect(SkillEffect.EmployeeRetention), 0f, MAX_RETENTION);
            amount *= 1f - retention;
        }
        employeeHappiness = Mathf.Clamp(employeeHappiness + amount, 0f, 100f);
    }

    /// <summary>Employee output multiplier from happiness: 0.85 at 0, 1.0 at 50, 1.15 at 100.</summary>
    public float HappinessMultiplier
    {
        get
        {
            float happiness = Mathf.Clamp(employeeHappiness, 0f, 100f);
            return happiness < 50f
                ? Mathf.Lerp(HAPPINESS_MIN_MULTIPLIER, 1f, happiness / 50f)
                : Mathf.Lerp(1f, HAPPINESS_MAX_MULTIPLIER, (happiness - 50f) / 50f);
        }
    }

    /// <summary>Change to the risk-card good chance from investor trust: -0.05 at 0 trust, 0 at 50, +0.05 at 100.</summary>
    public float TrustGoodChanceBonus => (Mathf.Clamp(investorTrust, 0f, 100f) - 50f) / 50f * TRUST_MAX_GOOD_CHANCE;

    // Both meters settle a little toward 50 every day, so they show how things have gone lately
    void DriftMeters()
    {
        AddHappiness((50f - employeeHappiness) * METER_DRIFT_PER_DAY); // a drop is softened by Employee Retention
        investorTrust += (50f - investorTrust) * METER_DRIFT_PER_DAY;
        ClampHappinessAndTrust();
    }

    private void Update()
    {
        // The office clock runs on game time and also waits while any panel is open: the skill tree and the stock
        // market don't pause the game, but crises, risk cards and hiring tasks wait for them, so the days wait too
        // (otherwise a panel would be a safe place to idle while earning)
        if (!UIManager.Exists || !UIManager.Instance.IsAnyPanelOpen())
        {
            dayTimer += Time.deltaTime;
            if (dayTimer >= dayDuration)
            {
                dayTimer = 0f;
                ProcessNewDay();
                SetNewDayDuration();
            }

            // Cosmetic real-time movement (employees constantly working)
            if (hasHiredFirst && totalEmployees > 0)
            {
                realtimeTimer += Time.deltaTime;
                if (realtimeTimer >= realtimeInterval)
                {
                    realtimeTimer = 0f;
                    ApplyRealtimeChanges();
                }
            }
        }

        // Animate displayed value toward actual value
        AnimateDisplayedValue(Time.unscaledDeltaTime);
    }

    void AnimateDisplayedValue(float deltaTime)
    {
        double gap = companyValue - displayedValue;
        if (gap == 0.0 || deltaTime <= 0f) return;

        // Snap when close (half a dollar, or a millionth of the value for very large companies)
        double snapDistance = System.Math.Max(0.5, System.Math.Abs(companyValue) * 0.000001);
        double next;

        if (System.Math.Abs(gap) <= snapDistance)
        {
            next = companyValue;
        }
        else
        {
            // Proportional catch-up: close 15% of the remaining gap per second, never slower than a small minimum
            double minSpeed = System.Math.Max(minChangePerSecond, System.Math.Abs(companyValue) * minChangePerSecondShare);
            double speed = System.Math.Max(System.Math.Abs(gap) * catchUpSpeed, minSpeed);
            double step = System.Math.Min(speed * deltaTime, System.Math.Abs(gap)); // Don't overshoot the target
            next = displayedValue + System.Math.Sign(gap) * step;

            if (next == displayedValue || System.Math.Abs(companyValue - next) <= snapDistance)
                next = companyValue;
        }

        double change = next - displayedValue;
        displayedValue = next;

        // Only fire event for meaningful changes
        if (System.Math.Abs(change) > 0.1)
            ValueChanging?.Invoke(change);
    }

    // Small real-time wiggle while employees work. Purely cosmetic: the offset stays around zero.
    void ApplyRealtimeChanges()
    {
        float noise = AMBIENT_NOISE_PER_EMPLOYEE * Mathf.Sqrt(totalEmployees);
        float newOffset = ambientOffset * (1f - AMBIENT_REVERSION) + Random.Range(-noise, noise);
        float delta = newOffset - ambientOffset;

        // Never below $0, and the offset only tracks what was really applied: clamping the value alone
        // made money out of nothing at $0 (the offset later reverted upward)
        if (companyValue + delta < 0.0)
            delta = (float)(-companyValue);
        ambientOffset += delta;

        companyValue += delta;
        if (companyValue < 0) companyValue = 0;

        // Check for billion threshold (Online World unlock)
        CheckBillionThreshold();
    }

    // Check if player reached $1 Billion (unlocks the Online City; CityOnline also checks once a second)
    void CheckBillionThreshold()
    {
        if (DemoMode.Active) return;   // Demo: the Online City isn't in the demo
        if (CityOnline.Instance != null)
            CityOnline.Instance.CheckUnlockNow();
    }

    void SetNewDayDuration()
    {
        // Random duration between 15 seconds and 45 seconds (Demo: the demo's shorter range)
        if (DemoMode.Active) dayDuration = Random.Range(DemoMode.Config.dayMin, DemoMode.Config.dayMax);
        else dayDuration = Random.Range(15f, 45f);
    }

    void ResetDayBaseline()
    {
        ValueAtDayStart = companyValue;
        baselineDay = currentDay;
        LastDayChange = 0f;
    }

    void ProcessNewDay()
    {
        // The day number was changed from outside (save load, debug tools): start a fresh baseline
        if (baselineDay != currentDay)
            ResetDayBaseline();

        currentDay++;
        lastDayEmployees.Clear();

        // Cash Grab: what company value was before today's books (the income stream settles against it before the day ends)
        double valueBeforeBooks = companyValue;

        SkillTreeManager skills = SkillTreeManager.Instance;
        DayBreakdown day = new DayBreakdown { day = currentDay - 1, happinessMultiplier = 1f };

        int employeeCount = employees.Count;
        if (employeeCount == 0)
        {
            // Nobody works yet: what the company owns still earns (upgraded office items, placed buildings, as their
            // screens promise), plus skill effects and the meters settling
            day.officeItems = GetTotalOfficeBonus();
            GetBuildingBonuses(out double ownedBuildingMoney, out _, out float ownedBuildingHappiness, out float ownedBuildingTrust);
            day.buildings = ownedBuildingMoney;
            // Upkeep, policies and other recurring systems run even with nobody on staff
            day.recurring = DailyEconomyHooks.CollectMoney(this, day.officeItems + day.buildings);
            day.operatingResult = day.officeItems + day.buildings + day.recurring;

            ApplyDailySkillEffects(skills, ref day);

            companyValue += day.operatingResult;
            if (companyValue < 0) companyValue = 0;
            if (ownedBuildingHappiness != 0f)
                AddHappiness(Mathf.Clamp(ownedBuildingHappiness, -MAX_BUILDING_HAPPINESS_PER_DAY, MAX_BUILDING_HAPPINESS_PER_DAY));
            if (ownedBuildingTrust != 0f)
                investorTrust += Mathf.Clamp(ownedBuildingTrust, -MAX_BUILDING_TRUST_PER_DAY, MAX_BUILDING_TRUST_PER_DAY);
            DailyEconomyHooks.CollectMeters(this, out float idleHookHappiness, out float idleHookTrust);
            if (idleHookHappiness != 0f) AddHappiness(idleHookHappiness);
            if (idleHookTrust != 0f) investorTrust += idleHookTrust;
            DriftMeters();
            day.total = day.operatingResult + day.skillGrowth + day.skillLuck;
            SettleIncomeStream(valueBeforeBooks, day.day);
            CompleteDay(day);
            return;
        }

        // Salary Roulette: salaries fluctuate today (if the skill is unlocked)
        bool hasSalaryFluctuation = skills != null && skills.HasEffect(SkillEffect.SalaryFluctuation);
        float salaryFluctuationRate = skills != null ? Mathf.Clamp01(skills.GetEffect(SkillEffect.SalaryFluctuation)) : 0f;
        float salaryMultiplier = 1f;
        if (hasSalaryFluctuation)
        {
            salaryMultiplier = Random.Range(1f - salaryFluctuationRate, 1f + salaryFluctuationRate);
        }

        // What boosts output today: productivity (skills, office items, buildings - capped), happiness, Random Raise.
        // The raise is rolled first: it also cheers the staff up, and today's happiness multiplier counts that.
        day.randomRaise = RollRandomRaise(skills);
        EmployeeDayFactors factors = GetEmployeeDayFactors();   // and the other skill bonuses (StaffLedger projects with these)
        day.productivityBonus = factors.productivityBonus;
        day.happinessMultiplier = factors.happinessMultiplier;
        float outputMultiplier = factors.OutputMultiplier * (1f + day.randomRaise);

        // Calculate each employee's individual contribution
        foreach (var emp in employees)
        {
            if (emp == null) continue;

            // Daily salary cost for this employee (annual salary / 365 days, with tax optimization and salary fluctuation)
            float dailySalary = DailySalary(emp, factors.taxOptimization, salaryMultiplier);

            // What they produce, or lose for a bad hire (EmployeeRevenue)
            float employeeRevenue = EmployeeRevenue(emp, outputMultiplier, factors.qualityControl, factors.iqSynergyBonus, factors.phdBonus);

            // Criminal record? They might cause problems (random chance of incident)
            float incident = 0f;
            if (emp.criminalRecord && Random.value < CRIMINAL_INCIDENT_CHANCE) // 5% chance per day
            {
                incident = Random.Range(CRIMINAL_INCIDENT_MIN, CRIMINAL_INCIDENT_MAX); // Theft, damage, lawsuits
                day.incidents += incident;
            }

            // High salary but low productivity = big loss; low salary but high productivity = big gain
            day.employeeOutput += employeeRevenue;
            day.salaries += dailySalary;

            // Their own share of the day, for the staff files (Team Synergy is added to it below)
            lastDayEmployees.Add(new EmployeeDay
            {
                employee = emp,
                output = employeeRevenue,
                salary = dailySalary,
                incidents = incident,
                overhead = OVERHEAD_PER_EMPLOYEE,
                perHeadBonus = factors.perHeadBonus
            });
        }

        // Team Synergy skill: +X% output per Y employees (Y comes from the skill, 5 for "Team Synergy")
        if (factors.teamSynergyRate > 0f && day.employeeOutput > 0.0)
        {
            int synergyTiers = Mathf.FloorToInt(employeeCount / factors.teamSynergyGroup);
            float synergy = 1f + (synergyTiers * factors.teamSynergyRate);
            day.employeeOutput *= synergy;
            for (int i = 0; i < lastDayEmployees.Count; i++)
            {
                EmployeeDay share = lastDayEmployees[i];
                share.output *= synergy;
                lastDayEmployees[i] = share;
            }
        }

        // Market fluctuation (random daily variance), plus a small headcount synergy (diminishing)
        day.marketAndSynergy = Random.Range(-400f, 400f) + Mathf.Sqrt(employeeCount) * 50f;

        // Automation bots (free workforce)
        int bots = skills != null ? skills.GetAutomationBots() : 0;
        day.robots = bots * 200.0; // Each bot generates $200/day

        // Upgraded office items and the HQ tower (rooms + their furniture)
        day.officeItems = GetTotalOfficeBonus();
        GetBuildingBonuses(out double buildingMoney, out _, out float buildingHappiness, out float buildingTrust);
        day.buildings = buildingMoney;

        // Overhead costs (office, utilities, etc.) - scales with company size
        day.overhead = BASE_OVERHEAD + (employeeCount * OVERHEAD_PER_EMPLOYEE);

        double resultBeforeHooks = day.employeeOutput - day.salaries - day.incidents + day.marketAndSynergy
                                 + day.robots + day.officeItems + day.buildings - day.overhead;
        // Upkeep, company policies, revenue shares and other recurring systems (DailyEconomyHooks)
        day.recurring = DailyEconomyHooks.CollectMoney(this, resultBeforeHooks);
        day.operatingResult = resultBeforeHooks + day.recurring;

        // Skill effects on the whole company (growth, interest, merger bonus, chaos luck, experience)
        ApplyDailySkillEffects(skills, ref day);

        companyValue += day.operatingResult;
        if (companyValue < 0) companyValue = 0;
        day.total = day.operatingResult + day.skillGrowth + day.skillLuck;

        // Update investor trust based on daily performance
        if (day.operatingResult > 0)
        {
            // Positive day = slight trust boost (scaled by earnings)
            investorTrust += Mathf.Clamp((float)(day.operatingResult / 2000.0), 0.5f, 3f);
        }
        else if (day.operatingResult < -500.0)
        {
            // Significant loss = trust penalty
            investorTrust -= Mathf.Clamp((float)(System.Math.Abs(day.operatingResult) / 3000.0), 0.5f, 2f);
        }

        // Comfortable buildings keep staff happier (happinessBonus per day, capped), meeting rooms keep investors warm
        // (trust per day, capped the same way), staff without a seat in the HQ tower are unhappy (seats are a soft cap),
        // then both meters settle a bit
        if (buildingHappiness != 0f)
            AddHappiness(Mathf.Clamp(buildingHappiness, -MAX_BUILDING_HAPPINESS_PER_DAY, MAX_BUILDING_HAPPINESS_PER_DAY));
        if (buildingTrust != 0f)
            investorTrust += Mathf.Clamp(buildingTrust, -MAX_BUILDING_TRUST_PER_DAY, MAX_BUILDING_TRUST_PER_DAY);
        float seatPenalty = GetSeatPenalty();
        if (seatPenalty > 0f)
            AddHappiness(-seatPenalty);
        DailyEconomyHooks.CollectMeters(this, out float hookHappiness, out float hookTrust);
        if (hookHappiness != 0f) AddHappiness(hookHappiness);
        if (hookTrust != 0f) investorTrust += hookTrust;
        DriftMeters();

        SettleIncomeStream(valueBeforeBooks, day.day);
        CompleteDay(day);
    }

    /// <summary>
    /// Cash Grab: the day's books are done. The income stream (IncomeStream) delivered part of today's income during the
    /// day and already added it to company value; take that part back out, so the day counts exactly once, before the
    /// day's change, the $1B check and DayEnded see the value.
    /// </summary>
    void SettleIncomeStream(double valueBeforeBooks, int endedDay)
    {
        IDayIncomeStream stream = IncomeStream;
        if (stream == null) return;
        double booked = companyValue - valueBeforeBooks;
        double delivered;
        try
        {
            delivered = stream.SettleDay(booked, endedDay);
        }
        catch (System.Exception e)
        {
            Debug.LogException(e, this);
            return;
        }
        if (double.IsNaN(delivered) || double.IsInfinity(delivered) || delivered == 0.0) return;
        companyValue -= delivered;
        if (companyValue < 0) companyValue = 0;
    }

    /// <summary>
    /// Cash Grab: what today's books are expected to add when the day ends, as things stand now: ProcessNewDay's formula
    /// without the day's dice (no Random Raise or Salary Roulette, criminal-record trouble at its average cost, the market
    /// swing at its average, recurring systems as they cost on the last day), then the growth/interest skills on that
    /// profit and the merger bonus. Daily luck is left out (it arrives at day end). Changes nothing; cheap enough to call a
    /// few times a second. The cash floor's income stream paces the day's bills with it.
    /// </summary>
    public double ProjectDayIncome()
    {
        SkillTreeManager skills = SkillTreeManager.Instance;
        double officeItems = GetTotalOfficeBonus();
        GetBuildingBonuses(out double buildings, out _, out _, out _);
        double recurring = LastDayBreakdown.day > 0 ? LastDayBreakdown.recurring : 0.0;   // hooks have side effects: last day's amount

        int count = 0;
        double output = 0.0, salaries = 0.0, incidents = 0.0;
        EmployeeDayFactors factors = default;
        bool haveFactors = false;
        foreach (var emp in employees)
        {
            if (emp == null) continue;
            if (!haveFactors)
            {
                factors = GetEmployeeDayFactors();
                haveFactors = true;
            }
            count++;
            output += EmployeeRevenue(emp, factors.OutputMultiplier, factors.qualityControl, factors.iqSynergyBonus, factors.phdBonus);
            salaries += DailySalary(emp, factors.taxOptimization, 1f);
            if (emp.criminalRecord)
                incidents += CRIMINAL_INCIDENT_CHANCE * (CRIMINAL_INCIDENT_MIN + CRIMINAL_INCIDENT_MAX) * 0.5;
        }

        double operating;
        if (count == 0)
        {
            operating = officeItems + buildings + recurring;
        }
        else
        {
            if (factors.teamSynergyRate > 0f && output > 0.0)
            {
                int synergyTiers = Mathf.FloorToInt(count / Mathf.Max(1f, factors.teamSynergyGroup));
                output *= 1f + synergyTiers * factors.teamSynergyRate;
            }
            double market = Mathf.Sqrt(count) * 50.0;                  // the random swing averages out
            double robots = (skills != null ? skills.GetAutomationBots() : 0) * 200.0;
            double overhead = BASE_OVERHEAD + count * OVERHEAD_PER_EMPLOYEE;
            operating = output - salaries - incidents + market + robots + officeItems + buildings - overhead + recurring;
        }

        double growth = 0.0;
        if (skills != null)
        {
            float profitGrowth = Mathf.Clamp(skills.GetDailyValueGrowth() + skills.GetEffect(SkillEffect.BankInterest), 0f, MAX_PROFIT_GROWTH);
            if (operating > 0.0 && profitGrowth > 0f) growth += operating * profitGrowth;
            growth += count * (double)MergerBonusPerEmployee(skills);
        }
        double total = operating + growth;
        return double.IsNaN(total) || double.IsInfinity(total) ? 0.0 : total;
    }

    void CompleteDay(DayBreakdown day)
    {
        ClampHappinessAndTrust();
        LastDayBreakdown = day;
        PostLuckLines();
        CheckMorale();

        if (logDailyBreakdown)
        {
            string raise = day.randomRaise > 0f ? ", raise +" + (day.randomRaise * 100f).ToString("0") + "%" : "";
            Debug.Log($"[CompanyManager] Day {day.day}: {Signed(day.total)} = operating {Signed(day.operatingResult)} " +
                      $"(output {Signed(day.employeeOutput)}, salaries {Signed(-day.salaries)}, incidents {Signed(-day.incidents)}, " +
                      $"market {Signed(day.marketAndSynergy)}, robots {Signed(day.robots)}, office items {Signed(day.officeItems)}, " +
                      $"buildings {Signed(day.buildings)}, recurring {Signed(day.recurring)}, overhead {Signed(-day.overhead)}) + skill growth {Signed(day.skillGrowth)} " +
                      $"+ luck {Signed(day.skillLuck)}. Productivity +{day.productivityBonus * 100f:0}%, " +
                      $"happiness x{day.happinessMultiplier:0.00}{raise}. Value ${companyValue:N0}");
        }

        FinishDay();
    }

    static string Signed(double amount)
    {
        return EventManager.FormatSignedMoney(amount);
    }

    int GetMoraleBand(int previousBand)
    {
        if (employeeHappiness < LOW_MORALE) return -1;
        if (employeeHappiness >= HIGH_MORALE) return 1;
        if (previousBand == -1 && employeeHappiness < LOW_MORALE + MORALE_HYSTERESIS) return -1;
        if (previousBand == 1 && employeeHappiness >= HIGH_MORALE - MORALE_HYSTERESIS) return 1;
        return 0;
    }

    // Explain the happiness effect once whenever morale becomes low or high
    void CheckMorale()
    {
        int band = GetMoraleBand(moraleBand);
        if (band == moraleBand) return;
        moraleBand = band;
        if (band == 0 || employees.Count == 0) return;

        string percent = CompanyText.Percent(Mathf.Abs(HappinessMultiplier - 1f));
        if (band < 0)
            GameNotifications.Show(CompanyText.Get("company.morale.low", ("percent", percent)), NotificationKind.Bad);
        else
            GameNotifications.Show(CompanyText.Get("company.morale.high", ("percent", percent)), NotificationKind.Good);
    }

    void AddLuckLine(string line, NotificationKind kind)
    {
        luckLines.Add(line);
        if (kind == NotificationKind.Special) luckHasSpecial = true;
        else if (kind == NotificationKind.Bad) luckHasBad = true;
        else luckHasGood = true;
    }

    // One notification per day for all luck effects that triggered (one line each)
    void PostLuckLines()
    {
        if (luckLines.Count == 0) return;
        NotificationKind kind = luckHasSpecial ? NotificationKind.Special
                              : luckHasBad && !luckHasGood ? NotificationKind.Bad
                              : NotificationKind.Good;
        GameNotifications.Show(string.Join("\n", luckLines), kind);
        luckLines.Clear();
        luckHasGood = luckHasBad = luckHasSpecial = false;
    }

    // End-of-day bookkeeping shared by all ProcessNewDay paths
    void FinishDay()
    {
        if (companyValue < 0) companyValue = 0;

        // Experience growth may have changed the averages
        RecalculateStats();

        // Check for billion threshold (Online World unlock)
        CheckBillionThreshold();

        LastDayChange = companyValue - ValueAtDayStart;
        ValueAtDayStart = companyValue;
        baselineDay = currentDay;

        DayEnded?.Invoke(currentDay, LastDayChange);
        StatsChanged?.Invoke();
    }

    // Employees per Team Synergy step, read from the unlocked skill (effectValue2)
    float GetTeamSynergyGroupSize()
    {
        float groupSize = 0f;
        if (SkillTreeManager.Instance != null && SkillTreeManager.Instance.allSkills != null)
        {
            foreach (Skill skill in SkillTreeManager.Instance.allSkills)
            {
                if (skill == null || !skill.unlocked || skill.effectType != SkillEffect.TeamSynergy || skill.effectValue2 < 1f)
                    continue;
                groupSize = groupSize <= 0f ? skill.effectValue2 : Mathf.Min(groupSize, skill.effectValue2);
            }
        }
        return groupSize >= 1f ? groupSize : DEFAULT_TEAM_SYNERGY_GROUP;
    }

    /// <summary>
    /// Employee output bonus from productivity skills, upgraded office items and placed buildings, capped at +150%
    /// (0.5 = +50%). Happiness and Random Raise multiply on top of it.
    /// </summary>
    public float GetProductivityBonus()
    {
        float bonus = 0f;
        SkillTreeManager skills = SkillTreeManager.Instance;
        if (skills != null)
        {
            foreach (SkillEffect effect in OutputSkillEffects)
                bonus += Mathf.Max(0f, skills.GetEffect(effect));
        }

        bonus += Mathf.Max(0f, GetTotalOfficeProductivity());
        GetBuildingBonuses(out _, out float buildingProductivity, out _);
        bonus += Mathf.Max(0f, buildingProductivity);

        // Cash Grab: the Backend department's upgrades (inside the same cap)
        System.Func<float> extra = ExtraProductivity;
        if (extra != null)
        {
            try
            {
                float value = extra();
                if (!float.IsNaN(value)) bonus += Mathf.Max(0f, value);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e, this);
            }
        }

        return Mathf.Clamp(bonus, 0f, MAX_PRODUCTIVITY_BONUS);
    }

    /// <summary>
    /// What ProcessNewDay applies to every employee's day, as things stand now (the day's dice, Random Raise and Salary
    /// Roulette, are rolled when the day ends).
    /// </summary>
    public EmployeeDayFactors GetEmployeeDayFactors()
    {
        SkillTreeManager skills = SkillTreeManager.Instance;
        return new EmployeeDayFactors
        {
            productivityBonus = GetProductivityBonus(),
            happinessMultiplier = HappinessMultiplier,
            happiness = employeeHappiness,
            qualityControl = skills != null ? Mathf.Clamp01(skills.GetEffect(SkillEffect.QualityControl)) : 0f,
            iqSynergyBonus = skills != null ? skills.GetIQSynergyBonus() : 0f,
            phdBonus = skills != null ? skills.GetEffect(SkillEffect.InnovationBonus) : 0f,
            taxOptimization = skills != null ? Mathf.Clamp01(skills.GetTaxOptimization()) : 0f,
            teamSynergyRate = skills != null ? skills.GetEffect(SkillEffect.TeamSynergy) : 0f,
            teamSynergyGroup = GetTeamSynergyGroupSize(),
            perHeadBonus = MergerBonusPerEmployee(skills)
        };
    }

    /// <summary>
    /// One employee's output for a day, before Team Synergy: $250 x (1 + how their IQ, GPA and experience compare with the
    /// baseline; negative for a bad hire), a shortfall softened by Quality Control, then outputMultiplier (productivity x
    /// happiness x Random Raise), IQ Synergy and PhD Appreciation. The multipliers only boost positive output.
    /// </summary>
    public static float EmployeeRevenue(ApplicantData emp, float outputMultiplier, float qualityControl, float iqSynergyBonus, float phdBonus)
    {
        // Employee productivity based on their stats vs baseline
        float iqFactor = (emp.IQ - BASELINE_IQ) / 100f;           // -0.3 to +0.6 typically
        float gpaFactor = (emp.GPA - BASELINE_GPA) / 4f;          // -0.3 to +0.3 typically
        float expFactor = (emp.experienceYears - BASELINE_EXPERIENCE) / 20f; // -0.25 to +1.75 typically

        // Combined productivity multiplier (can be negative for bad employees!)
        float productivityMultiplier = 1f + iqFactor + gpaFactor + expFactor;

        // Quality control reduces impact of bad employees
        if (productivityMultiplier < 1f && qualityControl > 0f)
        {
            float badAmount = 1f - productivityMultiplier;
            badAmount *= (1f - qualityControl);
            productivityMultiplier = 1f - badAmount;
        }

        // Base revenue this employee generates (or loses if multiplier < 0). Productivity bonuses,
        // happiness and Random Raise only boost positive output: they never make a bad hire lose more.
        float employeeRevenue = BASE_EMPLOYEE_REVENUE * productivityMultiplier;
        if (employeeRevenue > 0f)
            employeeRevenue *= outputMultiplier;

        // IQ Synergy skill: bonus per IQ point over 100
        if (iqSynergyBonus > 0f && emp.IQ > 100)
        {
            employeeRevenue += (emp.IQ - 100) * iqSynergyBonus;
        }

        // PhD Appreciation skill (a bonus on what they produce, never on a loss)
        if (phdBonus > 0f && emp.degree == "PhD" && employeeRevenue > 0f)
        {
            employeeRevenue *= (1f + phdBonus);
        }

        return employeeRevenue;
    }

    /// <summary>One employee's salary cost for a day: annual salary / 365, less Tax Optimization, times Salary Roulette's multiplier.</summary>
    public static float DailySalary(ApplicantData emp, float taxOptimization, float salaryMultiplier)
    {
        float dailySalary = emp.salary / 365f;
        dailySalary *= (1f - taxOptimization);
        dailySalary *= salaryMultiplier;
        return dailySalary;
    }

    /// <summary>
    /// One employee's expected day with today's skills, productivity and happiness: ProcessNewDay's formula without the
    /// day's dice (no Random Raise, Salary Roulette at x1, criminal-record trouble at its average cost).
    /// <paramref name="asNewHire"/> first applies what hiring changes (Salary Negotiation, Onboarding Program, the staff's
    /// happiness: HappinessAfterHiring; Lucky Hire is luck and left out) and counts them in the headcount for Team Synergy.
    /// StaffLedger uses this for CVs and new hires. Changes nothing.
    /// </summary>
    public EmployeeDay ProjectEmployeeDay(ApplicantData emp, bool asNewHire)
    {
        return ProjectEmployeeDay(emp, asNewHire, GetEmployeeDayFactors());
    }

    /// <summary>The same with factors from GetEmployeeDayFactors (cheaper when projecting many people at once).</summary>
    public EmployeeDay ProjectEmployeeDay(ApplicantData emp, bool asNewHire, EmployeeDayFactors factors)
    {
        EmployeeDay result = new EmployeeDay { employee = emp };
        if (emp == null) return result;

        ApplicantData worker = emp;
        SkillTreeManager skills = SkillTreeManager.Instance;
        if (asNewHire && skills != null)
        {
            worker = emp.Clone();
            float trainingBonus = skills.GetEffect(SkillEffect.TrainingProgram);
            float salaryDiscount = Mathf.Clamp01(skills.GetSalaryDiscount());
            if (trainingBonus > 0f)
                worker.experienceYears += TrainingYears(trainingBonus);
            if (salaryDiscount > 0f)
                worker.salary = DiscountedSalary(worker.salary, salaryDiscount);
        }

        // Hiring moves the staff's happiness before the first day ends (their traits, a hiring task they complete), and
        // that day's happiness multiplier counts it
        float outputMultiplier = factors.OutputMultiplier;
        if (asNewHire)
        {
            float happiness = HappinessAfterHiring(emp, factors.happiness);
            if (happiness != factors.happiness)
                outputMultiplier = (1f + factors.productivityBonus) * HappinessMultiplierAt(happiness);
        }

        double output = EmployeeRevenue(worker, outputMultiplier, factors.qualityControl, factors.iqSynergyBonus, factors.phdBonus);

        // Team Synergy grows everyone's output while the staff's total is positive
        if (factors.teamSynergyRate > 0f && (LastDayBreakdown.employeeOutput > 0.0 || output > 0.0))
        {
            int headcount = employees.Count + (asNewHire ? 1 : 0);
            int synergyTiers = Mathf.FloorToInt(headcount / factors.teamSynergyGroup);
            output *= 1f + (synergyTiers * factors.teamSynergyRate);
        }

        result.output = output;
        result.salary = DailySalary(worker, factors.taxOptimization, 1f);
        result.incidents = worker.criminalRecord ? CRIMINAL_INCIDENT_CHANCE * (CRIMINAL_INCIDENT_MIN + CRIMINAL_INCIDENT_MAX) * 0.5 : 0.0;
        result.overhead = OVERHEAD_PER_EMPLOYEE;
        result.perHeadBonus = factors.perHeadBonus;
        return result;
    }

    // ------------------------------------------------------------------
    // Happiness projections (ProjectEmployeeDay, StaffLedger's firing): what hiring or firing would do to the staff's
    // happiness, without changing anything. Keep them in step with AddHappiness, HappinessMultiplier, HireEmployee's
    // trait effect and OnTaskCompleted.
    // ------------------------------------------------------------------

    private const float HIRE_POSITIVE_TRAIT_HAPPINESS = 2f;    // HireEmployee: each positive trait of the hire
    private const float HIRE_NEGATIVE_TRAIT_HAPPINESS = -3f;   // ...each negative one
    private const float TASK_SUCCESS_HAPPINESS = 5f;           // OnTaskCompleted: a finished hiring task

    // Employee Retention: the share of a happiness loss it takes off (AddHappiness)
    static float HappinessRetention()
    {
        SkillTreeManager skills = SkillTreeManager.Instance;
        return skills != null ? Mathf.Clamp(skills.GetEffect(SkillEffect.EmployeeRetention), 0f, MAX_RETENTION) : 0f;
    }

    /// <summary>
    /// AddHappiness's math without changing anything: <paramref name="happiness"/> after a change of
    /// <paramref name="amount"/> (a loss is softened by Employee Retention), clamped to 0-100.
    /// </summary>
    public static float HappinessAfterChange(float happiness, float amount)
    {
        if (amount < 0f) amount *= 1f - HappinessRetention();
        return Mathf.Clamp(happiness + amount, 0f, 100f);
    }

    /// <summary>HappinessMultiplier at another happiness: 0.85 at 0, 1.0 at 50, 1.15 at 100.</summary>
    public static float HappinessMultiplierAt(float happiness)
    {
        happiness = Mathf.Clamp(happiness, 0f, 100f);
        return happiness < 50f
            ? Mathf.Lerp(HAPPINESS_MIN_MULTIPLIER, 1f, happiness / 50f)
            : Mathf.Lerp(1f, HAPPINESS_MAX_MULTIPLIER, (happiness - 50f) / 50f);
    }

    /// <summary>
    /// The staff's happiness right after hiring <paramref name="applicant"/> (as the CV shows them) from
    /// <paramref name="happiness"/>: HireEmployee's trait effect (+2 a positive trait, -3 a negative one, one after the
    /// other through AddHappiness), then +5 if they meet the active hiring task (hiring them completes it). Changes nothing.
    /// </summary>
    public float HappinessAfterHiring(ApplicantData applicant, float happiness)
    {
        if (applicant == null) return happiness;
        if (applicant.traits != null)
        {
            foreach (var trait in applicant.traits)
            {
                if (trait == null) continue;
                if (trait.type == TraitType.Positive)
                    happiness = HappinessAfterChange(happiness, HIRE_POSITIVE_TRAIT_HAPPINESS);
                else if (trait.type == TraitType.Negative)
                    happiness = HappinessAfterChange(happiness, HIRE_NEGATIVE_TRAIT_HAPPINESS);
            }
        }

        // HireEmployee hands the CV to the hiring task: a match completes it, and OnTaskCompleted cheers the staff up
        HiringTaskManager tasks = HiringTaskManager.Instance;
        if (tasks != null && tasks == subscribedTaskManager && tasks.IsTaskActive && tasks.CurrentTask != null &&
            tasks.CurrentTask.CheckRequirement(applicant))
            happiness = HappinessAfterChange(happiness, TASK_SUCCESS_HAPPINESS);
        return happiness;
    }

    /// <summary>
    /// What <paramref name="employee"/>'s traits did to the staff's happiness when they were hired (HireEmployee: +2 a
    /// positive trait, -3 a negative one softened by Employee Retention), leaving out the clamping at 0 and 100.
    /// </summary>
    public static float HireTraitHappiness(ApplicantData employee)
    {
        if (employee == null || employee.traits == null) return 0f;
        float retention = HappinessRetention();
        float change = 0f;
        foreach (var trait in employee.traits)
        {
            if (trait == null) continue;
            if (trait.type == TraitType.Positive) change += HIRE_POSITIVE_TRAIT_HAPPINESS;
            else if (trait.type == TraitType.Negative) change += HIRE_NEGATIVE_TRAIT_HAPPINESS * (1f - retention);
        }
        return change;
    }

    // Merger Mentality / Growth Through Scale: $ per employee per day, capped as a guard
    static float MergerBonusPerEmployee(SkillTreeManager skills)
    {
        return skills != null ? Mathf.Clamp(skills.GetEffect(SkillEffect.MergerBonus), 0f, MAX_MERGER_BONUS_PER_EMPLOYEE) : 0f;
    }

    // Onboarding Program: extra starting years for a new hire (2 years, 3 with Corporate Enlightenment's 2.5)
    static int TrainingYears(float trainingBonus)
    {
        return Mathf.FloorToInt(trainingBonus + 0.5f);
    }

    // Salary Negotiation: a new hire accepts a lower salary (whole dollars)
    static int DiscountedSalary(int salary, float salaryDiscount)
    {
        return (int)(salary * (1f - salaryDiscount));
    }

    /// <summary>
    /// Daily money, productivity and happiness from the HQ tower (TowerBuilder: its rooms, scaled by their furniture,
    /// and the furniture's own income). The callers apply the caps.
    /// </summary>
    public void GetBuildingBonuses(out double dailyMoney, out float productivity, out float happiness)
    {
        GetBuildingBonuses(out dailyMoney, out productivity, out happiness, out _);
    }

    /// <summary>The same, plus investor trust per day (meeting rooms).</summary>
    public void GetBuildingBonuses(out double dailyMoney, out float productivity, out float happiness, out float trust)
    {
        dailyMoney = 0.0;
        productivity = 0f;
        happiness = 0f;
        trust = 0f;

        TowerBuilder tower = TowerBuilder.Instance;
        if (tower == null) return;

        TowerTotals totals = tower.GetDailyTotals();
        dailyMoney = totals.dailyIncome;
        productivity = totals.productivity;
        happiness = totals.happinessPerDay;
        trust = totals.trustPerDay;
    }

    /// <summary>Happiness a day that employees without a seat in the HQ tower cost (0 while everyone has one).</summary>
    public float GetSeatPenalty()
    {
        TowerBuilder tower = TowerBuilder.Instance;
        return tower != null ? tower.GetSeatPenalty(employees.Count) : 0f;
    }

    /// <summary>Base amount of the chaos branch's daily luck: 1% of company value, between $500 and $250,000.</summary>
    public double GetLuckBonusBase()
    {
        return GetLuckBonusBase(companyValue);
    }

    static double GetLuckBonusBase(double value)
    {
        return System.Math.Min(System.Math.Max(value * LUCK_BONUS_SHARE, LUCK_BONUS_MIN), LUCK_BONUS_MAX);
    }

    // Serendipity: good random effects trigger 20% more often (a multiplier on their chances)
    static float LuckChanceMultiplier(SkillTreeManager skills)
    {
        return skills != null ? 1f + Mathf.Max(0f, skills.GetEffect(SkillEffect.Serendipity)) : 1f;
    }

    static bool RollLuck(float chance, float multiplier)
    {
        return Random.value < Mathf.Min(chance * multiplier, MAX_LUCK_CHANCE);
    }

    // Random Raise (chaos): some days staff get surprise bonuses - more output that day and a little happier
    float RollRandomRaise(SkillTreeManager skills)
    {
        if (skills == null || !skills.HasEffect(SkillEffect.RandomRaise)) return 0f;
        if (!RollLuck(RANDOM_RAISE_CHANCE, LuckChanceMultiplier(skills))) return 0f;

        float raise = Random.Range(RANDOM_RAISE_MIN, RANDOM_RAISE_MAX) * skills.EffectMultiplier;
        AddHappiness(RANDOM_RAISE_HAPPINESS);
        AddLuckLine(CompanyText.Get("company.luck.random_raise", ("skill", CompanyText.SkillName("chaos_3_raise", "Random Raise")),
                                    ("percent", CompanyText.Percent(raise)), ("happiness", CompanyText.Number(RANDOM_RAISE_HAPPINESS))),
                    NotificationKind.Good);
        return raise;
    }

    void ApplyDailySkillEffects(SkillTreeManager skills, ref DayBreakdown day)
    {
        if (skills == null) return;
        double valueBefore = companyValue;

        // Growth skills (Investor Confidence etc.) and the interest skills (Corporate Savings Account, Investment
        // Returns) add a share of the day's profit on top. Based on the profit, not the whole company value: a day is
        // only 15-45 s, and growth on the value compounded so fast that hiring, tasks and crises stopped mattering.
        double profit = System.Math.Max(0.0, day.operatingResult);
        float profitGrowth = Mathf.Clamp(skills.GetDailyValueGrowth() + skills.GetEffect(SkillEffect.BankInterest), 0f, MAX_PROFIT_GROWTH);
        if (profit > 0.0 && profitGrowth > 0f)
        {
            companyValue += profit * profitGrowth;
        }

        // Merger Bonus (+$X per employee) - grows with headcount only; per-employee total capped as a guard
        float mergerBonus = MergerBonusPerEmployee(skills);
        if (mergerBonus > 0f)
        {
            companyValue += employees.Count * (double)mergerBonus;
        }

        day.skillGrowth = companyValue - valueBefore;

        // Chaos branch: daily luck, sized by the value before today's growth
        day.skillLuck = ApplyDailyLuck(skills, GetLuckBonusBase(valueBefore));
        companyValue += day.skillLuck;
        if (companyValue < 0) companyValue = 0;

        // Experience Growth / Mentorship Program
        GrowExperience(skills);
    }

    /// <summary>The chaos branch's daily random bonuses. Returns the total money change (the caller applies it).</summary>
    double ApplyDailyLuck(SkillTreeManager skills, double bonusBase)
    {
        // Luck skills are flags (value 1); Corporate Enlightenment still makes their money 25% bigger
        double luckBase = bonusBase * skills.EffectMultiplier;
        float chanceMultiplier = LuckChanceMultiplier(skills);
        double total = 0.0;

        // Corporate Roulette (daily random buff OR nerf): $500-2,000 / -$300-1,500 for a small company, more later
        if (skills.HasDailyRandomBuff())
        {
            // Texts: one Company table line per skill (CompanyText), naming the skill as the skill board does
            string roulette = CompanyText.SkillName("chaos_3_roulette", "Corporate Roulette");
            if (RollLuck(0.5f, chanceMultiplier))
            {
                double buff = bonusBase * Random.Range(1f, 4f);
                total += buff;
                AddLuckLine(CompanyText.Get("company.luck.roulette.buff", ("skill", roulette), ("amount", Signed(buff))), NotificationKind.Good);
            }
            else
            {
                double nerf = -bonusBase * Random.Range(0.6f, 3f);
                total += nerf;
                AddLuckLine(CompanyText.Get("company.luck.roulette.nerf", ("skill", roulette), ("amount", Signed(nerf))), NotificationKind.Bad);
            }
        }

        // Fortune Cookie: 30% a day, +0.5-2x
        if (skills.HasEffect(SkillEffect.FortuneCookie) && RollLuck(FORTUNE_COOKIE_CHANCE, chanceMultiplier))
        {
            double amount = luckBase * Random.Range(0.5f, 2f);
            total += amount;
            var fortune = Fortunes[Random.Range(0, Fortunes.Length)];
            AddLuckLine(CompanyText.Get("company.luck.fortune_cookie", ("skill", CompanyText.SkillName("chaos_1_fortune", "Fortune Cookie")),
                                        ("fortune", L10n.GetOr(CompanyText.Table, fortune.key, fortune.english)), ("amount", Signed(amount))),
                        NotificationKind.Good);
        }

        // Lottery Ticket: 5% a day, +10x
        if (skills.HasEffect(SkillEffect.LotteryTicket) && RollLuck(LOTTERY_CHANCE, chanceMultiplier))
        {
            double prize = luckBase * LOTTERY_PRIZE;
            total += prize;
            GameNotifications.Show(CompanyText.Get("company.luck.lottery", ("amount", Signed(prize))), NotificationKind.Special);
        }

        // Mystery Box: 25% a day, anything from -0.5x (a bill) to +2x
        if (skills.HasEffect(SkillEffect.MysteryBox) && RollLuck(MYSTERY_BOX_CHANCE, chanceMultiplier))
        {
            double amount = luckBase * Random.Range(-0.5f, 2f);
            total += amount;
            string box = CompanyText.SkillName("chaos_2_mystery", "Mystery Box");
            if (amount >= 0.0)
                AddLuckLine(CompanyText.Get("company.luck.mystery_box.good", ("skill", box), ("amount", Signed(amount))), NotificationKind.Good);
            else
                AddLuckLine(CompanyText.Get("company.luck.mystery_box.bad", ("skill", box), ("amount", Signed(amount))), NotificationKind.Bad);
        }

        // Dice Roll: every day. 1: -0.5x, 2-3: +0.5x, 4-5: +1x, 6: +3x
        if (skills.HasEffect(SkillEffect.DiceRoll))
        {
            int roll = Random.Range(1, 7);
            double factor = roll == 1 ? -0.5 : roll <= 3 ? 0.5 : roll <= 5 ? 1.0 : 3.0;
            double amount = luckBase * factor;
            total += amount;
            AddLuckLine(CompanyText.Get(roll == 6 ? "company.luck.dice.six" : "company.luck.dice",
                                        ("skill", CompanyText.SkillName("chaos_3_dice", "Dice Roll")), ("roll", roll.ToString(Inv)), ("amount", Signed(amount))),
                        roll == 1 ? NotificationKind.Bad : roll == 6 ? NotificationKind.Special : NotificationKind.Good);
        }

        // Tarot Card: every day, one of: happier staff, more investor trust, or money
        if (skills.HasEffect(SkillEffect.TarotCard))
        {
            float meterBonus = TAROT_METER_BONUS * skills.EffectMultiplier;
            string tarot = CompanyText.SkillName("chaos_3_tarot", "Tarot Card");
            switch (Random.Range(0, 3))
            {
                case 0:
                    AddHappiness(meterBonus);
                    AddLuckLine(CompanyText.Get("company.luck.tarot.sun", ("skill", tarot), ("points", CompanyText.Number(meterBonus))), NotificationKind.Good);
                    break;
                case 1:
                    investorTrust = Mathf.Clamp(investorTrust + meterBonus, 0f, 100f);
                    AddLuckLine(CompanyText.Get("company.luck.tarot.emperor", ("skill", tarot), ("points", CompanyText.Number(meterBonus))), NotificationKind.Good);
                    break;
                default:
                    total += luckBase;
                    AddLuckLine(CompanyText.Get(TarotMoneyKeys[Random.Range(0, TarotMoneyKeys.Length)], ("skill", tarot), ("amount", Signed(luckBase))),
                                NotificationKind.Good);
                    break;
            }
        }

        // Surprise Bonus: 15% a day, +2x
        if (skills.HasEffect(SkillEffect.SurpriseBonus) && RollLuck(SURPRISE_BONUS_CHANCE, chanceMultiplier))
        {
            double amount = luckBase * SURPRISE_BONUS_PRIZE;
            total += amount;
            AddLuckLine(CompanyText.Get("company.luck.surprise_bonus", ("skill", CompanyText.SkillName("chaos_4_surprise", "Surprise Bonus")),
                                        ("amount", Signed(amount))), NotificationKind.Good);
        }

        // Karma Bonus: every 5th day, base x (1 + employees / 20)
        if (skills.HasEffect(SkillEffect.KarmaBonus) && currentDay % KARMA_INTERVAL_DAYS == 0)
        {
            double amount = luckBase * (1.0 + employees.Count / KARMA_EMPLOYEES_PER_STEP);
            total += amount;
            AddLuckLine(CompanyText.Get("company.luck.karma", ("skill", CompanyText.SkillName("chaos_6_karma", "Karma Bonus")), ("amount", Signed(amount))),
                        NotificationKind.Good);
        }

        return total;
    }

    // Experience Growth (Skill Development) and Mentorship Program (juniors learn 40% faster)
    void GrowExperience(SkillTreeManager skills)
    {
        float expGrowth = Mathf.Max(0f, skills.GetEffect(SkillEffect.ExperienceGrowth));
        float mentorship = Mathf.Max(0f, skills.GetEffect(SkillEffect.MentorshipProgram));
        if (expGrowth <= 0f && mentorship <= 0f) return;

        foreach (var emp in employees)
        {
            if (emp == null) continue;

            // Nobody gets more experience than their working life allows (stops runaway growth)
            int maxExperience = Mathf.Max(0, emp.age - MIN_WORKING_AGE);
            if (emp.experienceYears >= maxExperience) continue;

            // Mentorship: employees with under 5 years learn 40% faster (at a basic pace of their own without Skill Development)
            float rate = expGrowth;
            if (mentorship > 0f && emp.experienceYears < MENTORSHIP_MAX_EXPERIENCE)
                rate = Mathf.Max(rate, MENTORSHIP_BASE_GROWTH) * (1f + mentorship);
            if (rate <= 0f) continue;

            // Probabilistic rounding: 5% of 4 years = 0.2, so a 20% chance to gain a year today
            float gain = Mathf.Max(1, emp.experienceYears) * rate;
            int years = Mathf.FloorToInt(gain);
            if (Random.value < gain - years) years++;

            if (years > 0)
                emp.experienceYears = Mathf.Min(emp.experienceYears + years, maxExperience);
        }
    }

    public void HireEmployee(ApplicantData a)
    {
        if (a == null) return;
        if (employees.Contains(a))
        {
            Debug.LogWarning("CompanyManager: " + a.name + " is already hired.");
            return;
        }

        // Hiring tasks judge the applicant exactly as the CV showed them (before the skill bonuses below)
        ApplicantData shownOnCv = a.Clone();

        // Mark that first hire happened (enables ambient fluctuation)
        hasHiredFirst = true;

        // Get skill tree bonuses
        SkillTreeManager skills = SkillTreeManager.Instance;
        float salaryDiscount = skills != null ? Mathf.Clamp01(skills.GetSalaryDiscount()) : 0f;
        float trainingBonus = skills != null ? skills.GetEffect(SkillEffect.TrainingProgram) : 0f;
        float luckyHireChance = skills != null ? Mathf.Min(skills.GetLuckyHireChance() * LuckChanceMultiplier(skills), MAX_LUCK_CHANCE) : 0f;
        float criticalSuccessChance = skills != null ? skills.GetCriticalSuccessChance() : 0f;

        // Apply training bonus (extra starting experience)
        if (trainingBonus > 0f)
        {
            a.experienceYears += TrainingYears(trainingBonus); // 2 years, 3 with Corporate Enlightenment (2.5)
        }

        // Apply salary discount
        if (salaryDiscount > 0f)
        {
            a.salary = DiscountedSalary(a.salary, salaryDiscount);
        }

        // Lucky Hire: chance to upgrade to golden applicant
        if (!a.isGolden && luckyHireChance > 0f && Random.value < luckyHireChance)
        {
            a.isGolden = true;
            a.IQ = Mathf.Max(a.IQ, Random.Range(130, 150));
            a.GPA = Mathf.Max(a.GPA, Random.Range(3.5f, 4.0f));
            Debug.Log("LUCKY HIRE! " + a.name + " became a golden employee!");
            GameNotifications.Show(CompanyText.Get("company.hire.lucky", ("skill", CompanyText.SkillName("chaos_2_lucky", "Lucky Hire")),
                                                   ("name", StaffLedger.DisplayName(a)), ("iq", a.IQ.ToString(Inv))), NotificationKind.Special);
        }

        // Notify skill tree of hire (skill points count high-IQ hires) - after Lucky Hire so a boosted IQ counts
        if (skills != null)
        {
            skills.OnEmployeeHired(a);
        }

        // New hire value depends on their quality - bad hires can cost you!
        float iqValue = (a.IQ - BASELINE_IQ) * 15f;           // Can be negative!
        float gpaValue = (a.GPA - BASELINE_GPA) * 200f;       // Can be negative!
        float expValue = (a.experienceYears - BASELINE_EXPERIENCE) * 30f; // Can be negative!

        // Base hiring cost (paperwork, onboarding, training)
        float hiringCost = 300f;

        // Total hire impact
        float hireImpact = iqValue + gpaValue + expValue - hiringCost;

        // Criminal record? Extra risk on hire
        if (a.criminalRecord)
            hireImpact -= 500f;

        // Critical Success: chance for double hire bonus
        if (criticalSuccessChance > 0f && Random.value < criticalSuccessChance && hireImpact > 0)
        {
            hireImpact *= 2f;
            Debug.Log("CRITICAL SUCCESS! Double hire bonus!");
            GameNotifications.Show(CompanyText.Get("company.hire.critical", ("skill", CompanyText.SkillName("chaos_3_crit", "Critical Success")),
                                                   ("amount", Signed(hireImpact))), NotificationKind.Good);
        }

        companyValue += hireImpact;
        if (companyValue < 0) companyValue = 0;

        // Update employee happiness based on new hire's traits
        if (a.traits != null)
        {
            foreach (var trait in a.traits)
            {
                if (trait == null) continue;
                if (trait.type == TraitType.Positive)
                    AddHappiness(2f);
                else if (trait.type == TraitType.Negative)
                    AddHappiness(-3f);
            }
            ClampHappinessAndTrust();
        }

        // Keep only the portrait number: holding every hire's 1024² portrait would grow memory without limit.
        // Load it on demand with PortraitLibrary.Load(portraitIndex) if a UI ever shows hired employees.
        a.photo = null;
        a.EnsureId();   // a stable id for the staff files (StaffLedger) and the save
        employees.Add(a);
        UpdateCompanyStats();

        // Run stats and other listeners (a listener's error must not stop the hire)
        try { EmployeeHired?.Invoke(a); }
        catch (System.Exception e) { Debug.LogException(e, this); }

        // Notify hiring task manager (with the CV snapshot)
        if (HiringTaskManager.Instance != null)
        {
            HiringTaskManager.Instance.OnEmployeeHired(shownOnCv);
        }

        // Spawn employee visual in the office
        if (EmployeeSpawnManager.Instance != null)
        {
            EmployeeSpawnManager.Instance.OnEmployeeHired(a);
        }

        CheckBillionThreshold();
    }

    /// <summary>
    /// Take someone off the staff: recounts totals and averages, removes their figure from the office and raises
    /// StatsChanged. Firing goes through StaffLedger.Fire (severance, morale, skill-point progress) instead.
    /// Returns false if they weren't on staff.
    /// </summary>
    public bool RemoveEmployee(ApplicantData a)
    {
        if (a == null || !employees.Remove(a)) return false;

        if (EmployeeSpawnManager.Instance != null)
        {
            EmployeeSpawnManager.Instance.OnEmployeeFired(a);
        }

        UpdateCompanyStats();
        return true;
    }

    public void UpdateCompanyStats()
    {
        RecalculateStats();
        StatsChanged?.Invoke();
    }

    // Recompute counts and averages from the employee list (no event)
    void RecalculateStats()
    {
        float gpaSum = 0;
        float iqSum = 0;
        float ageSum = 0;
        float expSum = 0;
        long salarySum = 0;
        int count = 0;

        maleCount = 0;
        femaleCount = 0;

        // Reset race count
        raceCount.Clear();

        foreach (var e in employees)
        {
            if (e == null) continue;
            count++;

            gpaSum += e.GPA;
            iqSum += e.IQ;
            ageSum += e.age;
            expSum += e.experienceYears;
            salarySum += e.salary;

            // Gender
            if (e.gender == "Male") maleCount++;
            if (e.gender == "Female") femaleCount++;

            // Race
            string race = string.IsNullOrEmpty(e.race) ? "Unknown" : e.race;
            if (!raceCount.ContainsKey(race))
                raceCount[race] = 0;

            raceCount[race]++;
        }

        totalEmployees = count;

        if (count == 0)
        {
            // No employees: clear everything instead of keeping the last company's numbers
            averageGPA = 0f;
            averageIQ = 0f;
            averageAge = 0f;
            averageExperience = 0f;
            totalSalary = 0;
            maleToFemaleRatio = 0f;
            mostCommonRace = "None";
            leastCommonRace = "None";
            return;
        }

        // Averages
        averageGPA = gpaSum / count;
        averageIQ = iqSum / count;
        averageAge = ageSum / count;
        averageExperience = expSum / count;
        totalSalary = salarySum;

        // Gender ratio
        if (femaleCount == 0)
            maleToFemaleRatio = maleCount;
        else
            maleToFemaleRatio = (float)maleCount / (float)femaleCount;

        // Most Race / Least Race
        mostCommonRace = raceCount.OrderByDescending(r => r.Value).First().Key;
        leastCommonRace = raceCount.OrderBy(r => r.Value).First().Key;
    }

    // Call this to add/remove money and update the display
    public void AddMoney(double amount, bool instant = false)
    {
        companyValue += amount;
        if (companyValue < 0) companyValue = 0;

        if (instant)
            displayedValue = companyValue;

        if (amount > 0f)
            CheckBillionThreshold();

        StatsChanged?.Invoke();
    }

    // Call this to add/remove a percentage of company value
    public void AddPercentage(double percentage, bool instant = false)
    {
        companyValue += companyValue * percentage;
        if (companyValue < 0) companyValue = 0;

        if (instant)
            displayedValue = companyValue;

        if (percentage > 0f)
            CheckBillionThreshold();

        StatsChanged?.Invoke();
    }

    // Force displayed value to match actual value instantly
    public void SyncDisplayedValue()
    {
        displayedValue = companyValue;
        StatsChanged?.Invoke();
    }

    // Get total daily bonus from all office items
    public float GetTotalOfficeBonus()
    {
        float total = 0f;
        OfficeItemInteractable[] items = FindObjectsByType<OfficeItemInteractable>(FindObjectsSortMode.None);
        foreach (var item in items)
        {
            if (item != null && item.itemData != null)
                total += item.itemData.GetTotalDailyBonus();
        }
        return total;
    }

    // Get total productivity bonus from all office items
    public float GetTotalOfficeProductivity()
    {
        float total = 0f;
        OfficeItemInteractable[] items = FindObjectsByType<OfficeItemInteractable>(FindObjectsSortMode.None);
        foreach (var item in items)
        {
            if (item != null && item.itemData != null)
                total += item.itemData.GetTotalProductivityBonus();
        }
        return total;
    }
}
