using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Board meetings: every 10 game days the Board (the one that signs the promotion memos) reviews the growth target the
/// CEO bet their reputation on, then proposes three new ones.
/// - Schedule: a new company's first meeting is on day 10 (a save from before board meetings gets its first one 10 days
///   after it loads), then one every 10 days. A meeting opens when its day starts, once the screen is calm
///   (RunEndDirector.IsScreenCalm for calmSeconds of real time, no click under way, no run-end screen, and not while a
///   broke company waits for its bankruptcy hearing). It waits while anything blocks it; it is never skipped.
/// - Result, measured when the meeting opens: hit when company value >= start value + target gain. ALL growth counts,
///   gambling and risk cards included. Hit: trust +4 / +10 / +20 and a bonus of 0 / 2% / 5% of company value (Safe /
///   Stretch / Moonshot). Miss: trust -6 / -12 / -25.
/// - Vote of no confidence: investor trust below 15 after the result forces a restructuring: 10% of company value is lost
///   (never leaving less than $1,000) and trust is reset to 30. Checked at every meeting.
/// - Proposals: E = max(average daily operating profit of the last 10 days x 10, 2% of company value, $10,000); Safe 0.8E,
///   Stretch 1.5E, Moonshot 3E, rounded to 2 significant figures, due at the next meeting. Or skip: trust -3, no target.
/// - The memo (BoardMeetingView) pauses the game until the CEO picks a card or skips; Escape asks "Skip the meeting?"
///   first. BoardTargetChip shows an active target's progress during play.
/// - Saved under "board": next meeting day, the active target, history counts, the last days' operating profit (for E)
///   and a meeting whose result was applied but whose choice wasn't made yet (it reopens after a load). Saves without it
///   start fresh.
/// - BoardBootstrap adds it to every scene with a CompanyManager, so MainGame needs no edit.
/// - Texts (tier names, toasts, the memo and the chip) come from the "Board" String Table (Assets/Localization/Source/
///   Board.csv) through L10n. The save and the Console keep the tiers' English names (TierEnglishName); the memo shows a
///   saved meeting's tiers by their index, in the current language.
/// For a HUD: Instance, NextMeetingDay, HasTarget, Target, Progress, IsMeetingOpen and the static events MeetingOpened,
/// TargetChosen, TargetResolved and Changed (each listener is called on its own). Every number is a field below.
/// </summary>
[DisallowMultipleComponent]
public class BoardMeetings : MonoBehaviour, ISaveParticipant
{
    public const string Key = "board";

    /// <summary>The String Table collection of the board texts (Assets/Localization/Source/Board.csv).</summary>
    public const string Table = "Board";

    public const int TierSafe = 0;
    public const int TierStretch = 1;
    public const int TierMoonshot = 2;
    public const int TierCount = 3;

    /// <summary>The choice that skips a meeting (BoardMeetingView reports it instead of a proposal index).</summary>
    public const int SkipChoice = -1;

    public static BoardMeetings Instance { get; private set; }

    /// <summary>The board memo opened (argument: the meeting's day). Also raised when an unanswered meeting reopens after a load.</summary>
    public static event Action<int> MeetingOpened;
    /// <summary>The CEO committed to a target (not raised for a skipped meeting).</summary>
    public static event Action<BoardTarget> TargetChosen;
    /// <summary>A meeting judged the active target: (hit, tier). The reward or penalty is already applied.</summary>
    public static event Action<bool, int> TargetResolved;
    /// <summary>Anything a HUD shows changed: a load, a meeting opening, a choice.</summary>
    public static event Action Changed;

    /// <summary>What one kind of target asks and pays.</summary>
    [Serializable]
    public class TierRule
    {
        [Tooltip("Shown on the proposal card and the memo. The built-in English names (Safe, Stretch, Moonshot) show translated (Board.csv); any other name shows as typed")]
        public string name;
        [Tooltip("The target gain is this many times the expected gain E")]
        public float gainMultiplier;
        [Tooltip("Investor trust change when the target is hit")]
        public float hitTrust;
        [Tooltip("Bonus when the target is hit, as a share of company value at the meeting (0.02 = 2%)")]
        [Range(0f, 1f)] public float hitBonusShare;
        [Tooltip("Investor trust change when the target is missed")]
        public float missTrust;

        public TierRule() { }

        public TierRule(string name, float gainMultiplier, float hitTrust, float hitBonusShare, float missTrust)
        {
            this.name = name;
            this.gainMultiplier = gainMultiplier;
            this.hitTrust = hitTrust;
            this.hitBonusShare = hitBonusShare;
            this.missTrust = missTrust;
        }
    }

    static readonly string[] DefaultTierNames = { "Safe", "Stretch", "Moonshot" };
    // The tiers' ids in the String Table: board.tier.<id> (name) and board.tier.<id>.caps (the proposal card's capitals)
    static readonly string[] TierIds = { "safe", "stretch", "moonshot" };
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    [Header("Schedule")]
    [Tooltip("Game days from one meeting to the next (also how long a target runs)")]
    [Min(1)] public int meetingInterval = 10;
    [Tooltip("Day of a new company's first meeting (a save from before board meetings gets its first one Meeting Interval days after it loads)")]
    [Min(1)] public int firstMeetingDay = 10;
    [Tooltip("Real seconds the screen must stay calm (RunEndDirector.IsScreenCalm) before a due meeting opens")]
    [Min(0f)] public float calmSeconds = 1f;

    [Header("Expected gain E = max(avg daily operating profit x period days, value share x company value, minimum)")]
    [Tooltip("How many of the most recent days' operating profit are averaged")]
    [Min(1)] public int profitAverageDays = 10;
    [Tooltip("The average daily operating profit is multiplied by this many days")]
    [Min(1)] public int expectedPeriodDays = 10;
    [Tooltip("E is at least this share of company value (0.02 = 2%)")]
    public double expectedValueShare = 0.02;
    [Tooltip("E is at least this much")]
    public double expectedMinimum = 10000;
    [Tooltip("Target gains are rounded to this many significant figures")]
    [Range(1, 6)] public int significantFigures = 2;

    [Header("Targets")]
    public TierRule safe = new TierRule("Safe", 0.8f, 4f, 0f, -6f);
    public TierRule stretch = new TierRule("Stretch", 1.5f, 10f, 0.02f, -12f);
    public TierRule moonshot = new TierRule("Moonshot", 3f, 20f, 0.05f, -25f);

    [Header("Skipping a meeting")]
    [Tooltip("Investor trust change for skipping a meeting (no target until the next one)")]
    public float skipTrust = -3f;

    [Header("Vote of no confidence (checked after the result)")]
    [Tooltip("Investor trust below this forces a restructuring")]
    public float noConfidenceBelow = 15f;
    [Tooltip("The restructuring costs this share of company value...")]
    public double restructuringShare = 0.10;
    [Tooltip("...but never leaves the company with less than this")]
    public double restructuringReserve = 1000;
    [Tooltip("Investor trust after the restructuring")]
    public float restructuringTrust = 30f;

    [Header("Views (empty = found in the scene, else created and built at runtime)")]
    public BoardMeetingView meetingView;
    public BoardTargetChip targetChip;

    [Header("Debug")]
    [Tooltip("Write meetings, results and choices to the Console")]
    public bool logChanges = true;

    [Serializable]
    class SavedBoard
    {
        public int version = 1;
        public int nextMeetingDay;
        public bool hasTarget;
        public int targetTier;
        public double targetStartValue;
        public double targetGain;
        public int targetDueDay;
        public int targetSetDay;
        public string targetTierName;   // for people reading the save file; not read back
        public int[] hits;              // per tier: Safe, Stretch, Moonshot
        public int[] misses;
        public int skipped;
        public int meetingsHeld;
        public int restructurings;
        public bool lastMeetingSkipped;
        public double[] recentProfits;  // operating result of the last days, oldest first
        public bool meetingOpen;        // a meeting's result is applied, its choice not made yet
        public BoardMeetingReport openMeeting;
    }

    int nextMeetingDay;
    bool hasTarget;
    BoardTarget target;
    readonly int[] hits = new int[TierCount];
    readonly int[] misses = new int[TierCount];
    int skipped;
    int meetingsHeld;
    int restructurings;
    bool lastMeetingSkipped;
    readonly List<double> recentProfits = new List<double>();
    BoardMeetingReport openMeeting;   // the meeting on screen (or waiting to reopen); null = none

    bool stateKnown;
    bool restoredThisLoad;
    bool warnedInactiveView;
    float calmTimer;
    CompanyManager hookedCompany;

    // ------------------------------------------------------------------
    // Public state
    // ------------------------------------------------------------------

    /// <summary>Day the next meeting opens (when that day starts).</summary>
    public int NextMeetingDay => nextMeetingDay;
    /// <summary>Days left until the next meeting (0 = due now).</summary>
    public int DaysUntilMeeting => Mathf.Max(0, nextMeetingDay - CurrentDay);
    /// <summary>The CEO committed to a target that the next meeting will judge.</summary>
    public bool HasTarget => hasTarget;
    /// <summary>The active target (default when HasTarget is false).</summary>
    public BoardTarget Target => hasTarget ? target : default;
    /// <summary>Growth since the target was set (company value - start value; 0 without a target).</summary>
    public double CurrentGain => hasTarget ? CompanyValue - target.startValue : 0.0;
    /// <summary>Share of the target gain reached so far (0-1; 0 without a target).</summary>
    public float Progress
    {
        get
        {
            if (!hasTarget) return 0f;
            double gain = target.Gain;
            if (!(gain > 0.0)) return 1f;
            return Mathf.Clamp01((float)(CurrentGain / gain));
        }
    }
    /// <summary>A meeting is waiting for the CEO's choice (its memo is up, or reopens once the screen is calm).</summary>
    public bool IsMeetingOpen => openMeeting != null;
    /// <summary>The meeting waiting for a choice (null when none). Its result is already applied.</summary>
    public BoardMeetingReport OpenMeeting => openMeeting;
    /// <summary>The saved game (or a new one) is loaded, so the schedule and the target are known.</summary>
    public bool IsStateKnown => stateKnown;
    public int MeetingsHeld => meetingsHeld;
    public int MeetingsSkipped => skipped;
    public int Restructurings => restructurings;
    public int Hits(int tier) => tier >= 0 && tier < TierCount ? hits[tier] : 0;
    public int Misses(int tier) => tier >= 0 && tier < TierCount ? misses[tier] : 0;

    static double CompanyValue => CompanyManager.Instance != null ? CompanyManager.Instance.companyValue : 0.0;
    static int CurrentDay => CompanyManager.Instance != null ? Mathf.Max(1, CompanyManager.Instance.currentDay) : 1;

    /// <summary>What a tier asks and pays (TierSafe, TierStretch, TierMoonshot).</summary>
    public TierRule GetRule(int tier)
    {
        switch (tier)
        {
            case TierSafe: return safe ?? (safe = new TierRule("Safe", 0.8f, 4f, 0f, -6f));
            case TierMoonshot: return moonshot ?? (moonshot = new TierRule("Moonshot", 3f, 20f, 0.05f, -25f));
            default: return stretch ?? (stretch = new TierRule("Stretch", 1.5f, 10f, 0.02f, -12f));
        }
    }

    /// <summary>
    /// "Safe", "Stretch" or "Moonshot" in the current language (board.tier.*). A name changed on the live board in the
    /// Inspector shows as typed.
    /// </summary>
    public static string TierName(int tier)
    {
        tier = Mathf.Clamp(tier, 0, TierCount - 1);
        string name = TierEnglishName(tier);
        return name == DefaultTierNames[tier] ? L10n.Get(Table, "board.tier." + TierIds[tier]) : name;
    }

    /// <summary>
    /// The tier's name in capitals ("STRETCH", the proposal card's band): written in capitals in the String Table
    /// (board.tier.*.caps); a name typed in the Inspector goes through the language's casing rules (L10n.ToUpper).
    /// </summary>
    public static string TierNameCaps(int tier)
    {
        tier = Mathf.Clamp(tier, 0, TierCount - 1);
        string name = TierEnglishName(tier);
        return name == DefaultTierNames[tier] ? L10n.Get(Table, "board.tier." + TierIds[tier] + ".caps") : L10n.ToUpper(name);
    }

    /// <summary>The tier's name as set on the board (English by default), for the save file and the Console log.</summary>
    public static string TierEnglishName(int tier)
    {
        tier = Mathf.Clamp(tier, 0, TierCount - 1);
        if (Instance != null)
        {
            string custom = Instance.GetRule(tier).name;
            if (!string.IsNullOrWhiteSpace(custom)) return custom.Trim();
        }
        return DefaultTierNames[tier];
    }

    /// <summary>
    /// The name to show for a tier a saved meeting report names: the tier's name in the current language (TierName), or
    /// the saved name as it was written when it is neither that tier's English name nor its built-in one (a name typed in
    /// the Inspector back then). Reports keep English names only (older saves too), so they never need translating.
    /// </summary>
    public static string TierDisplayName(int tier, string savedName, bool capitals = false)
    {
        tier = Mathf.Clamp(tier, 0, TierCount - 1);
        string saved = savedName != null ? savedName.Trim() : "";
        if (saved.Length == 0 || saved == TierEnglishName(tier) || saved == DefaultTierNames[tier])
            return capitals ? TierNameCaps(tier) : TierName(tier);
        return capitals ? L10n.ToUpper(saved) : saved;
    }

    // ------------------------------------------------------------------
    // Life cycle
    // ------------------------------------------------------------------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        // Entering Play mode without a domain reload keeps statics: start every session clean
        Instance = null;
        MeetingOpened = null;
        TargetChosen = null;
        TargetResolved = null;
        Changed = null;
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[BoardMeetings] Removed a second BoardMeetings on '" + name + "'.");
            Destroy(this);
            return;
        }
        Instance = this;
        // Demo: a shorter schedule (first meeting day 8, verdict 4 days later) and targets sized for a $100k company
        if (DemoMode.Active)
        {
            DemoConfig demo = DemoMode.Config;
            firstMeetingDay = demo.boardFirstDay;
            meetingInterval = demo.boardInterval;
            expectedPeriodDays = profitAverageDays = demo.boardInterval;
            expectedMinimum = demo.boardExpectedMinimum;
        }
        nextMeetingDay = Mathf.Max(1, firstMeetingDay);
    }

    void OnEnable()
    {
        Hook();
    }

    void OnDisable()
    {
        Unhook();
    }

    void Start()
    {
        // The chip is always there (it shows a target from the save); the memo is made when first needed
        targetChip = FindOrCreate(targetChip, "BoardTargetChip");
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Hook()
    {
        CompanyManager company = CompanyManager.Instance;
        if (company == hookedCompany) return;
        Unhook();
        hookedCompany = company;
        if (company != null)
        {
            company.DayEnded += OnDayEnded;
            company.StateLoaded += OnStateLoaded;
        }
    }

    void Unhook()
    {
        if (hookedCompany != null)
        {
            hookedCompany.DayEnded -= OnDayEnded;
            hookedCompany.StateLoaded -= OnStateLoaded;
        }
        hookedCompany = null;
    }

    T FindOrCreate<T>(T current, string objectName) where T : MonoBehaviour
    {
        if (current != null) return current;
        T found = FindAnyObjectByType<T>(FindObjectsInactive.Include);
        if (found != null) return found;
        GameObject go = new GameObject(objectName);
        go.transform.SetParent(transform, false);
        return go.AddComponent<T>();
    }

    BoardMeetingView MeetingView => meetingView = FindOrCreate(meetingView, "BoardMeetingView");

    void Update()
    {
        Hook();
        CompanyManager company = hookedCompany;
        if (company == null) return;

        if (!stateKnown)
        {
            // A new game (or no save): SaveSystem finishes without handing over any data
            SaveSystem save = SaveSystem.Instance;
            if (save == null || (save.IsGameLoaded && !save.IsLoading))
                ApplyFresh(false);
            return;
        }

        // Due when its day has started; an unanswered meeting (e.g. from a save) comes back the same way
        bool due = openMeeting != null || company.currentDay >= nextMeetingDay;
        bool showing = meetingView != null && meetingView.IsOpen;
        if (!due || showing || !CanOpenNow(company))
        {
            calmTimer = 0f;
            return;
        }
        // Tutorial: no meeting before FeatureGate.BoardMeetingsFromDay (day 10, like the first meeting), and the first one a
        // player sees waits for the Board's one-time memo about meetings
        if (!FeatureGate.IsUnlocked(Feature.BoardMeetings) || FeatureGate.HoldForMemo(Feature.BoardMeetings))
        {
            calmTimer = 0f;
            return;
        }

        calmTimer += Time.unscaledDeltaTime;
        if (calmTimer < calmSeconds) return;
        calmTimer = 0f;

        if (openMeeting != null) ShowMeeting();
        else BeginMeeting(null);
    }

    /// <summary>
    /// Nothing else is going on (RunEndDirector.IsScreenCalm), no run-end screen, no click under way (the memo opens from
    /// this Update and its scrim isn't raycast on that frame), and a broke company gets its bankruptcy hearing first.
    /// </summary>
    static bool CanOpenNow(CompanyManager company)
    {
        if (Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButtonUp(0) || Input.GetMouseButtonUp(1)) return false;
        RunEndDirector director = RunEndDirector.Instance;
        if (director != null)
        {
            if (director.IsRunOver || director.IsAnyScreenOpen) return false;
            if (company.companyValue <= director.bankruptcyThreshold) return false;
        }
        return RunEndDirector.IsScreenCalm();
    }

    void OnDayEnded(int day, double change)
    {
        CompanyManager company = hookedCompany;
        if (company == null) return;
        double profit = company.LastDayBreakdown.operatingResult;
        if (double.IsNaN(profit) || double.IsInfinity(profit)) return;
        recentProfits.Add(profit);
        TrimProfits();
    }

    void TrimProfits()
    {
        int keep = Mathf.Max(1, profitAverageDays);
        if (recentProfits.Count > keep)
            recentProfits.RemoveRange(0, recentProfits.Count - keep);
    }

    // ------------------------------------------------------------------
    // Expected gain and proposals
    // ------------------------------------------------------------------

    /// <summary>Average operating profit of the most recent days (up to profitAverageDays; 0 before the first day ends).</summary>
    public double AverageDailyProfit
    {
        get
        {
            int count = Mathf.Min(recentProfits.Count, Mathf.Max(1, profitAverageDays));
            if (count <= 0) return 0.0;
            double sum = 0.0;
            for (int i = recentProfits.Count - count; i < recentProfits.Count; i++)
                sum += recentProfits[i];
            return sum / count;
        }
    }

    /// <summary>E for a company worth <paramref name="value"/>: max(average daily profit x period days, value share x value, minimum).</summary>
    public double ExpectedGain(double value)
    {
        double fromProfit = AverageDailyProfit * Mathf.Max(1, expectedPeriodDays);
        double fromValue = Math.Max(0.0, value) * Math.Max(0.0, expectedValueShare);
        double e = Math.Max(fromProfit, Math.Max(fromValue, Math.Max(0.0, expectedMinimum)));
        return double.IsNaN(e) || double.IsInfinity(e) ? Math.Max(0.0, expectedMinimum) : e;
    }

    /// <summary>Round to <paramref name="figures"/> significant figures (9,876 -> 9,900 at 2).</summary>
    public static double RoundSignificant(double value, int figures)
    {
        if (!(value > 0.0) || double.IsInfinity(value)) return 0.0;
        figures = Mathf.Clamp(figures, 1, 15);
        int digits = (int)Math.Floor(Math.Log10(value)) + 1 - figures;
        double scale = Math.Pow(10.0, digits);
        double rounded = Math.Round(value / scale, MidpointRounding.AwayFromZero) * scale;
        return digits >= 0 ? Math.Round(rounded) : rounded;
    }

    BoardProposal MakeProposal(int tier, double startValue, double expected, int dueDay)
    {
        TierRule rule = GetRule(tier);
        double gain = RoundSignificant(expected * Math.Max(0f, rule.gainMultiplier), significantFigures);
        if (gain < 1.0) gain = 1.0;   // a target always asks for something
        double targetValue = startValue + gain;
        return new BoardProposal
        {
            tier = tier,
            name = TierEnglishName(tier),   // English, for the save file and the log: the memo shows the tier in the current language
            multiplier = rule.gainMultiplier,
            gain = gain,
            targetValue = targetValue,
            dueDay = dueDay,
            hitTrust = rule.hitTrust,
            hitBonusShare = Mathf.Max(0f, rule.hitBonusShare),
            bonusAtTarget = Math.Round(targetValue * MoneyMath.Exact(Mathf.Max(0f, rule.hitBonusShare))),
            missTrust = rule.missTrust
        };
    }

    // ------------------------------------------------------------------
    // The meeting
    // ------------------------------------------------------------------

    /// <summary>
    /// Open a meeting now: judge the active target (<paramref name="forcedOutcome"/> overrides the rule, for debugging),
    /// hold the vote of no confidence, make the proposals and show the memo.
    /// </summary>
    void BeginMeeting(bool? forcedOutcome)
    {
        if (CompanyManager.Instance == null || openMeeting != null) return;
        // Hold a panel while money moves (hit bonus, restructuring) until the memo has registered its own: a bonus that
        // crosses $1B would otherwise let the City certificate (which only waits for a free screen) open first and stack
        // two pausing modals. The certificate retries once the memo closes.
        if (UIManager.Exists) UIManager.Instance.OpenPanel(this, true, null);
        try
        {
            BeginMeetingCore(forcedOutcome);
        }
        finally
        {
            if (UIManager.Exists) UIManager.Instance.ClosePanel(this);
        }
    }

    void BeginMeetingCore(bool? forcedOutcome)
    {
        CompanyManager company = CompanyManager.Instance;
        if (company == null || openMeeting != null) return;
        int day = Mathf.Max(1, company.currentDay);
        var report = new BoardMeetingReport
        {
            day = day,
            firstMeeting = meetingsHeld == 0 && !hasTarget,
            lastSkipped = !hasTarget && lastMeetingSkipped,
            periodStartDay = Mathf.Clamp(hasTarget ? target.setDay : nextMeetingDay - meetingInterval, 1, day)
        };
        bool resolved = false, resolvedHit = false;
        int resolvedTier = 0;

        // 1. The last period's result
        if (hasTarget)
        {
            TierRule rule = GetRule(target.tier);
            double value = Math.Max(0.0, company.companyValue);
            bool hit = forcedOutcome ?? Math.Round(value) >= Math.Round(target.targetValue);
            float trustChange = hit ? rule.hitTrust : rule.missTrust;
            double bonus = hit ? Math.Round(value * MoneyMath.Exact(Mathf.Max(0f, rule.hitBonusShare))) : 0.0;

            ChangeTrust(company, trustChange);
            if (bonus > 0.0) company.AddMoney(bonus, true);
            if (hit) hits[target.tier]++;
            else misses[target.tier]++;

            report.hadTarget = true;
            report.resultTier = target.tier;
            report.resultTierName = TierEnglishName(target.tier);   // English (save and log); the memo shows resultTier's name
            report.hit = hit;
            report.resultStartValue = target.startValue;
            report.resultTargetValue = target.targetValue;
            report.resultActualValue = value;
            report.resultTrustChange = trustChange;
            report.resultBonus = bonus;

            resolved = true;
            resolvedHit = hit;
            resolvedTier = target.tier;
            hasTarget = false;
            target = default;
            Log("Day " + day + ": the " + report.resultTierName + " target (" + UIFormat.Money(report.resultTargetValue) + ") was " +
                (hit ? "hit" : "missed") + " at " + UIFormat.Money(value) + ": trust " + Signed(trustChange) +
                (bonus > 0.0 ? ", bonus " + UIFormat.Money(bonus) : "") + (forcedOutcome.HasValue ? " (forced)" : ""));
        }

        // 2. Vote of no confidence
        float trust = company.investorTrust;
        if (trust < noConfidenceBelow)
        {
            double value = Math.Max(0.0, company.companyValue);
            double cost = Math.Floor(Math.Max(0.0, Math.Min(value * Math.Max(0.0, restructuringShare), value - Math.Max(0.0, restructuringReserve))));
            if (cost > 0.0) company.AddMoney(-cost, true);
            company.investorTrust = Mathf.Clamp(restructuringTrust, 0f, 100f);
            restructurings++;

            report.noConfidence = true;
            report.noConfidenceTrust = trust;
            report.noConfidenceBelow = noConfidenceBelow;
            report.restructuringCost = cost;
            report.restructuringShare = restructuringShare;
            report.trustAfterRestructuring = company.investorTrust;
            Log("Day " + day + ": vote of no confidence at trust " + trust.ToString("0.#", Inv) + ": restructuring cost " +
                UIFormat.Money(cost) + ", trust reset to " + company.investorTrust.ToString("0.#", Inv));
        }

        // 3. New proposals, from the company as it stands after all that
        report.startValue = Math.Round(Math.Max(0.0, company.companyValue));
        report.expectedGain = ExpectedGain(report.startValue);
        report.dueDay = day + Mathf.Max(1, meetingInterval);
        report.skipTrust = skipTrust;
        report.proposals = new BoardProposal[TierCount];
        for (int tier = 0; tier < TierCount; tier++)
            report.proposals[tier] = MakeProposal(tier, report.startValue, report.expectedGain, report.dueDay);

        meetingsHeld++;
        openMeeting = report;
        calmTimer = 0f;
        company.UpdateCompanyStats();   // the trust change reaches the HUD
        Log("Day " + day + ": board meeting. E = " + UIFormat.Money(report.expectedGain) + " (average daily profit " +
            UIFormat.Money(AverageDailyProfit) + "), proposals " + UIFormat.SignedMoney(report.proposals[0].gain) + " / " +
            UIFormat.SignedMoney(report.proposals[1].gain) + " / " + UIFormat.SignedMoney(report.proposals[2].gain) + " by day " + report.dueDay);

        SaveNow();   // the result is applied: a quit now reopens this same meeting instead of judging the target again
        if (resolved) RaiseTargetResolved(resolvedHit, resolvedTier);
        ShowMeeting();
        RaiseChanged();
    }

    void ShowMeeting()
    {
        if (openMeeting == null) return;
        BoardMeetingView view = MeetingView;
        if (view == null) return;
        if (!view.isActiveAndEnabled)
        {
            // A switched-off view can't animate or take clicks: leave the game running and the meeting waiting
            if (!warnedInactiveView) Debug.LogWarning("[BoardMeetings] The BoardMeetingView is switched off, so the meeting can't open.", view);
            warnedInactiveView = true;
            return;
        }
        warnedInactiveView = false;
        view.Show(openMeeting, CompanyNameUI.GetCompanyName(), OnMeetingChoice);
        if (view.IsOpen) RaiseMeetingOpened(openMeeting.day);
    }

    /// <summary>The CEO's answer: a proposal index (0 Safe, 1 Stretch, 2 Moonshot) or SkipChoice.</summary>
    void OnMeetingChoice(int choice)
    {
        BoardMeetingReport report = openMeeting;
        if (report == null) return;
        CompanyManager company = CompanyManager.Instance;
        BoardProposal chosen = choice >= 0 && report.proposals != null && choice < report.proposals.Length ? report.proposals[choice] : null;

        if (chosen != null)
        {
            target = new BoardTarget
            {
                tier = Mathf.Clamp(chosen.tier, 0, TierCount - 1),
                startValue = report.startValue,
                targetValue = report.startValue + chosen.gain,
                dueDay = report.dueDay,
                setDay = report.day
            };
            hasTarget = true;
            lastMeetingSkipped = false;
            GameNotifications.Show(L10n.Get(Table, "board.toast.committed", ("gain", UIFormat.SignedMoney(chosen.gain)), ("day", report.dueDay),
                                            ("tier", TierDisplayName(chosen.tier, chosen.name)), ("value", UIFormat.Money(target.targetValue))),
                                   NotificationKind.Info);
            Log("Day " + report.day + ": committed to the " + chosen.name + " target: reach " + UIFormat.Money(target.targetValue) + " by day " + report.dueDay);
        }
        else
        {
            hasTarget = false;
            target = default;
            lastMeetingSkipped = true;
            skipped++;
            if (company != null)
            {
                ChangeTrust(company, report.skipTrust);
                company.UpdateCompanyStats();
            }
            GameNotifications.Show(L10n.Get(Table, "board.toast.skipped", ("trust", Signed(report.skipTrust))), NotificationKind.Bad);
            Log("Day " + report.day + ": meeting skipped, trust " + Signed(report.skipTrust));
        }

        nextMeetingDay = Mathf.Max(report.dueDay, report.day + 1);
        openMeeting = null;
        calmTimer = 0f;
        SaveNow();
        if (chosen != null) RaiseTargetChosen(target);
        RaiseChanged();
    }

    static void ChangeTrust(CompanyManager company, float amount)
    {
        if (amount == 0f || float.IsNaN(amount) || float.IsInfinity(amount)) return;
        company.investorTrust = Mathf.Clamp(company.investorTrust + amount, 0f, 100f);
    }

    static void SaveNow()
    {
        SaveSystem save = SaveSystem.Instance;
        if (save != null && save.IsGameLoaded && !save.SavingSuspended)
            save.SaveGame();
    }

    // ------------------------------------------------------------------
    // Save
    // ------------------------------------------------------------------

    public string SaveKey => Key;

    public string CaptureState()
    {
        var data = new SavedBoard
        {
            nextMeetingDay = nextMeetingDay,
            hasTarget = hasTarget,
            targetTier = hasTarget ? target.tier : 0,
            targetStartValue = hasTarget ? target.startValue : 0.0,
            targetGain = hasTarget ? target.Gain : 0.0,
            targetDueDay = hasTarget ? target.dueDay : 0,
            targetSetDay = hasTarget ? target.setDay : 0,
            targetTierName = hasTarget ? TierEnglishName(target.tier) : "",
            hits = (int[])hits.Clone(),
            misses = (int[])misses.Clone(),
            skipped = skipped,
            meetingsHeld = meetingsHeld,
            restructurings = restructurings,
            lastMeetingSkipped = lastMeetingSkipped,
            recentProfits = recentProfits.ToArray(),
            meetingOpen = openMeeting != null,
            openMeeting = openMeeting
        };
        return JsonUtility.ToJson(data);
    }

    public void RestoreState(string json)
    {
        SavedBoard data = null;
        if (!string.IsNullOrEmpty(json))
        {
            try { data = JsonUtility.FromJson<SavedBoard>(json); }
            catch (Exception e) { Debug.LogWarning("[BoardMeetings] Couldn't read the saved board (starting fresh): " + e.Message); }
        }
        if (data == null) return;   // OnStateLoaded starts fresh

        int day = CurrentDay;   // SaveSystem applies the company (and its day) before the extras
        CloseViewWithoutChoice();

        hasTarget = data.hasTarget && IsFinite(data.targetStartValue) && IsFinite(data.targetGain) && data.targetGain > 0.0;
        target = hasTarget
            ? new BoardTarget
            {
                tier = Mathf.Clamp(data.targetTier, 0, TierCount - 1),
                startValue = data.targetStartValue,
                targetValue = data.targetStartValue + data.targetGain,
                dueDay = Mathf.Max(1, data.targetDueDay),
                setDay = Mathf.Max(1, data.targetSetDay)
            }
            : default;
        CopyCounts(data.hits, hits);
        CopyCounts(data.misses, misses);
        skipped = Mathf.Max(0, data.skipped);
        meetingsHeld = Mathf.Max(0, data.meetingsHeld);
        restructurings = Mathf.Max(0, data.restructurings);
        lastMeetingSkipped = data.lastMeetingSkipped;

        recentProfits.Clear();
        if (data.recentProfits != null)
        {
            foreach (double profit in data.recentProfits)
                if (IsFinite(profit)) recentProfits.Add(profit);
        }
        TrimProfits();

        openMeeting = data.meetingOpen && IsUsable(data.openMeeting) ? data.openMeeting : null;
        nextMeetingDay = data.nextMeetingDay >= 1 ? data.nextMeetingDay : day + Mathf.Max(1, meetingInterval);

        restoredThisLoad = true;
        stateKnown = true;
        calmTimer = 0f;
        Log("Loaded: next meeting on day " + nextMeetingDay + (hasTarget ? ", " + TierEnglishName(target.tier) + " target: reach " +
            UIFormat.Money(target.targetValue) + " by day " + target.dueDay : ", no target") + (openMeeting != null ? ", a meeting is waiting for its answer" : ""));
        RaiseChanged();
    }

    // SaveSystem raises this after it applied a save (after RestoreState when the save had board data)
    void OnStateLoaded()
    {
        if (restoredThisLoad)
        {
            restoredThisLoad = false;
            return;
        }
        ApplyFresh(true);   // a save from before board meetings
    }

    /// <summary>
    /// Start the board fresh: no target, no history. A new company's first meeting is on firstMeetingDay; a save from
    /// before board meetings gets its first one meetingInterval days after the day it loads on.
    /// </summary>
    void ApplyFresh(bool fromOlderSave)
    {
        int day = CurrentDay;
        CloseViewWithoutChoice();
        hasTarget = false;
        target = default;
        Array.Clear(hits, 0, hits.Length);
        Array.Clear(misses, 0, misses.Length);
        skipped = meetingsHeld = restructurings = 0;
        lastMeetingSkipped = false;
        recentProfits.Clear();
        openMeeting = null;
        nextMeetingDay = fromOlderSave ? day + Mathf.Max(1, meetingInterval) : Mathf.Max(Mathf.Max(1, firstMeetingDay), day + 1);
        restoredThisLoad = false;
        stateKnown = true;
        calmTimer = 0f;
        Log((fromOlderSave ? "Save without board data" : "New company") + ": the first board meeting is on day " + nextMeetingDay);
        RaiseChanged();
    }

    void CloseViewWithoutChoice()
    {
        // A load replaces the board's state: a memo still on screen belongs to the old one
        if (meetingView != null && meetingView.IsOpen) meetingView.Close();
    }

    static void CopyCounts(int[] from, int[] into)
    {
        Array.Clear(into, 0, into.Length);
        if (from == null) return;
        for (int i = 0; i < into.Length && i < from.Length; i++)
            into[i] = Mathf.Max(0, from[i]);
    }

    static bool IsUsable(BoardMeetingReport report)
    {
        if (report == null || report.day < 1 || report.dueDay <= report.day || !IsFinite(report.startValue)) return false;
        if (report.proposals == null || report.proposals.Length != TierCount) return false;
        foreach (BoardProposal proposal in report.proposals)
        {
            if (proposal == null || !IsFinite(proposal.gain) || proposal.gain <= 0.0) return false;
        }
        return true;
    }

    static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    // ------------------------------------------------------------------
    // Debug (Tools > Game > Debug)
    // ------------------------------------------------------------------

    /// <summary>Debug: open a meeting now, whatever the day or the calm (an unanswered one just comes back). False while loading.</summary>
    public bool DebugOpenMeetingNow()
    {
        if (!stateKnown || CompanyManager.Instance == null) return false;
        if (openMeeting != null)
        {
            if (meetingView == null || !meetingView.IsOpen) ShowMeeting();
            return true;
        }
        BeginMeeting(null);
        return true;
    }

    /// <summary>
    /// Debug: open a meeting now that judges the target as hit or missed. The target's start value is moved so the real rule
    /// agrees and the memo's numbers add up (hit: the value lands exactly on the target; miss: no growth at all). Without a
    /// target a Stretch one is made first. False while loading or while a meeting waits for its answer.
    /// </summary>
    public bool DebugResolveTarget(bool hit)
    {
        CompanyManager company = CompanyManager.Instance;
        if (!stateKnown || company == null)
        {
            Debug.LogWarning("[BoardMeetings] The game is still loading - try again in a moment.");
            return false;
        }
        if (openMeeting != null)
        {
            Debug.LogWarning("[BoardMeetings] A meeting is already waiting for its answer: pick a card or skip first.");
            if (meetingView == null || !meetingView.IsOpen) ShowMeeting();
            return false;
        }

        double value = Math.Round(Math.Max(0.0, company.companyValue));
        if (!hasTarget)
        {
            BoardProposal made = MakeProposal(TierStretch, value, ExpectedGain(value), CurrentDay + Mathf.Max(1, meetingInterval));
            target = new BoardTarget
            {
                tier = TierStretch,
                startValue = value,
                targetValue = value + made.gain,
                dueDay = made.dueDay,
                setDay = Mathf.Max(1, CurrentDay - Mathf.Max(1, meetingInterval))
            };
            hasTarget = true;
            Log("Debug: no active target, made a " + made.name + " target of " + UIFormat.SignedMoney(made.gain) + " to judge");
        }

        double gain = target.Gain;
        target.startValue = hit ? value - gain : value;
        target.targetValue = target.startValue + gain;
        BeginMeeting(hit);
        return true;
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    void Log(string message)
    {
        if (logChanges) Debug.Log("[BoardMeetings] " + message);
    }

    /// <summary>"+4", "−6" (a true minus sign), "+2.5".</summary>
    public static string Signed(float value)
    {
        string body = Math.Abs(value).ToString("0.#", Inv);
        return value < 0f ? UIFormat.Minus + body : "+" + body;
    }

    static void RaiseMeetingOpened(int day)
    {
        Action<int> handlers = MeetingOpened;
        if (handlers == null) return;
        foreach (Action<int> handler in handlers.GetInvocationList())
        {
            try { handler(day); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }

    static void RaiseTargetChosen(BoardTarget chosen)
    {
        Action<BoardTarget> handlers = TargetChosen;
        if (handlers == null) return;
        foreach (Action<BoardTarget> handler in handlers.GetInvocationList())
        {
            try { handler(chosen); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }

    static void RaiseTargetResolved(bool hit, int tier)
    {
        Action<bool, int> handlers = TargetResolved;
        if (handlers == null) return;
        foreach (Action<bool, int> handler in handlers.GetInvocationList())
        {
            try { handler(hit, tier); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }

    static void RaiseChanged()
    {
        Action handlers = Changed;
        if (handlers == null) return;
        foreach (Action handler in handlers.GetInvocationList())
        {
            try { handler(); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}

/// <summary>A growth target the CEO committed to at a board meeting.</summary>
[Serializable]
public struct BoardTarget
{
    /// <summary>BoardMeetings.TierSafe, TierStretch or TierMoonshot.</summary>
    public int tier;
    /// <summary>Company value when the target was set.</summary>
    public double startValue;
    /// <summary>Company value to reach: start value + the target gain.</summary>
    public double targetValue;
    /// <summary>The meeting that judges it (when this day starts).</summary>
    public int dueDay;
    /// <summary>The meeting that set it.</summary>
    public int setDay;

    /// <summary>The growth asked for (target value - start value).</summary>
    public double Gain => targetValue - startValue;
    /// <summary>The tier's name in the current language.</summary>
    public string TierName => BoardMeetings.TierName(tier);
}

/// <summary>One of the three targets a meeting proposes.</summary>
[Serializable]
public class BoardProposal
{
    public int tier;
    /// <summary>
    /// The tier's English name, for people reading the save file and for the log (older saves hold the same). Shown through
    /// BoardMeetings.TierDisplayName(tier, name), which puts it in the current language.
    /// </summary>
    public string name = "";
    /// <summary>The gain as a multiple of the expected gain E (0.8, 1.5, 3).</summary>
    public float multiplier;
    public double gain;
    public double targetValue;
    public int dueDay;
    public float hitTrust;
    /// <summary>Bonus when hit, as a share of company value at the next meeting.</summary>
    public float hitBonusShare;
    /// <summary>The bonus if the value lands exactly on the target (hitBonusShare x targetValue), for the card's estimate.</summary>
    public double bonusAtTarget;
    public float missTrust;
}

/// <summary>What a board meeting's memo shows: the last period's result, the vote of no confidence and the proposals.</summary>
[Serializable]
public class BoardMeetingReport
{
    public int day;
    /// <summary>First day of the period the result covers (the meeting that set the target).</summary>
    public int periodStartDay;
    /// <summary>Day of the next meeting, when the new target is due.</summary>
    public int dueDay;
    public bool firstMeeting;

    // The last period
    public bool hadTarget;
    public bool lastSkipped;
    public int resultTier;
    /// <summary>resultTier's English name (save file and log; shown through BoardMeetings.TierDisplayName).</summary>
    public string resultTierName = "";
    public bool hit;
    public double resultStartValue;
    public double resultTargetValue;
    public double resultActualValue;
    public float resultTrustChange;
    public double resultBonus;

    // Vote of no confidence
    public bool noConfidence;
    public float noConfidenceTrust;
    public float noConfidenceBelow;
    public double restructuringCost;
    public double restructuringShare;
    public float trustAfterRestructuring;

    // The new proposals
    public double startValue;
    public double expectedGain;
    public BoardProposal[] proposals = new BoardProposal[0];
    public float skipTrust;
}

/// <summary>
/// Adds the board (BoardMeetings, with its chip and memo) to every scene that has a CompanyManager, when it loads, unless
/// one is already there. So MainGame needs no scene edit.
/// </summary>
static class BoardBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        EnsureBoard();   // the first scene loaded before this ran
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EnsureBoard();
    }

    static void EnsureBoard()
    {
        if (!Application.isPlaying) return;
        CompanyManager company = CompanyManager.Instance != null ? CompanyManager.Instance : UnityEngine.Object.FindAnyObjectByType<CompanyManager>();
        if (company == null) return;
        if (BoardMeetings.Instance != null || UnityEngine.Object.FindAnyObjectByType<BoardMeetings>(FindObjectsInactive.Include) != null) return;

        GameObject go = new GameObject("Board (Runtime)");
        Scene scene = company.gameObject.scene;
        if (scene.IsValid() && scene.isLoaded && go.scene != scene)
        {
            // Live and die with the company's scene
            try { SceneManager.MoveGameObjectToScene(go, scene); }
            catch (Exception e) { Debug.LogWarning("[Board] Couldn't move the board next to the CompanyManager: " + e.Message); }
        }
        go.AddComponent<BoardMeetings>();
    }
}
