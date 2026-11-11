using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Heat (0-100): how much attention the company's gambling draws. The title asks whether gambling is bad for business;
/// this is where the business feels it.
/// - Every voluntary gamble reported to GambleLedger adds clamp(2 + 24 x stake / company value before, 2, 26) heat
///   (the tilt prompt's flip too). Risk cards add heat by the card chosen: High +6, Balanced +2, Low +0
///   (EventManager calls OnRiskCardChosen after the card's outcome is rolled).
/// - Levels: Cool 0-29, Warm 30-59, Hot 60-84, On fire 85-100.
/// - At the end of every day (CompanyManager.DayEnded, so never while a panel is open), by the level the day ended at:
///   investor trust and employee happiness drop (Warm: trust -0.5; Hot: trust -1.5, happiness -1; On fire: trust -3,
///   happiness -2), then a regulator audit may come (Hot 6%, On fire 15%): a fine of (heat / 10)% of company value, at
///   least $1,000 but never leaving the company less than $1,000, trust -5 and heat -30. Then the heat cools:
///   heat x 0.85 - 8, never below 0.
/// - Risk cards: Hot -5 points, On fire -10 points on the good-outcome chance (EventManager.GetGoodChance adds
///   RiskCardChanceModifier).
/// - At heat 100 the board freezes voluntary gambling until the heat is below 70: gambles ask CanGamble first.
/// - Saved with the game (ISaveParticipant "heat"). HeatBootstrap adds it to every scene with a CompanyManager, and it
///   brings the heat chip (HeatChipView) and the "Win it back?" prompt (TiltPromptView).
/// - For a HUD: Heat, DisplayHeat, Level, LevelName, IsLockedOut, EffectsSummary, LevelColor, and the static events
///   HeatChanged, LevelChanged, LockoutChanged and Audited (also raised once after every load).
/// - Level names, effect summaries and the level / freeze / audit toasts come from the "Heat" String Table
///   (Assets/Localization/Source/Heat.csv) in the current language; the save and the Console log keep English.
/// - Demo (DemoMode): a gamble adds at least DemoConfig.heatBetMin from a base of heatBetBase (1.5), audits come at
///   auditChanceHot / auditChanceOnFire (3% / 10%), and a build-budget flip adds buildGambleHeatScale (half) of its heat.
/// Every number is a field below.
/// </summary>
[DisallowMultipleComponent]
public class HeatMeter : MonoBehaviour, ISaveParticipant
{
    public const string Key = "heat";
    /// <summary>The String Table collection of the heat texts (Assets/Localization/Source/Heat.csv).</summary>
    public const string Table = "Heat";

    public const int LevelCool = 0;
    public const int LevelWarm = 1;
    public const int LevelHot = 2;
    public const int LevelOnFire = 3;

    public static HeatMeter Instance { get; private set; }

    /// <summary>The heat changed (argument: the heat now, 0-100). Static, so a HUD can subscribe before the meter exists.</summary>
    public static event Action<float> HeatChanged;
    /// <summary>The level changed: (old level, new level), LevelCool..LevelOnFire. Also raised once after every load (old may equal new).</summary>
    public static event Action<int, int> LevelChanged;
    /// <summary>The board froze (true) or unfroze (false) voluntary gambling. Also raised once after every load.</summary>
    public static event Action<bool> LockoutChanged;
    /// <summary>A regulator audit happened (argument: the fine taken, 0 when there was nothing to take).</summary>
    public static event Action<double> Audited;

    /// <summary>What one heat level does.</summary>
    [Serializable]
    public class LevelRule
    {
        [Tooltip("Shown on the heat chip and in messages")]
        public string name;
        [Tooltip("Heat at which this level starts")]
        public float from;
        [Tooltip("Investor trust change at the end of every day that ends at this level")]
        public float dailyTrust;
        [Tooltip("Employee happiness change at the end of every day that ends at this level (Employee Retention softens drops)")]
        public float dailyHappiness;
        [Tooltip("Change to every risk card's good-outcome chance, in percentage points (-5 = 50% becomes 45%)")]
        public float riskCardPoints;
        [Tooltip("Chance of a regulator audit at the end of every day that ends at this level")]
        [Range(0f, 1f)] public float auditChance;

        public LevelRule() { }

        public LevelRule(string name, float from, float dailyTrust, float dailyHappiness, float riskCardPoints, float auditChance)
        {
            this.name = name;
            this.from = from;
            this.dailyTrust = dailyTrust;
            this.dailyHappiness = dailyHappiness;
            this.riskCardPoints = riskCardPoints;
            this.auditChance = auditChance;
        }
    }

    [Header("Heat")]
    [Tooltip("The most heat there can be")]
    public float maxHeat = 100f;

    // Playtesters found heat rising too fast (2026-09-30): gains are about 40% lower (a bet of a fifth of the company
    // adds ~7, was ~11) and the daily cooling is stronger than before (was keep 0.9, drop 6)
    [Header("Heat from a gamble: clamp(base + stake heat x stake / company value before, min, max)")]
    public float betBaseHeat = 2f;
    [Tooltip("Heat for betting the whole company value (scaled by the stake's share of it)")]
    public float betStakeHeat = 24f;
    public float betMinHeat = 2f;
    public float betMaxHeat = 26f;

    [Header("Heat from a risk card (the card the player picks)")]
    public float highRiskHeat = 6f;
    public float balancedHeat = 2f;
    public float lowRiskHeat = 0f;

    [Header("Cooling at the end of every day: heat x keep - drop, never below 0")]
    [Range(0f, 1f)] public float dailyCoolKeep = 0.85f;
    public float dailyCoolDrop = 8f;

    [Header("Levels (each starts at its From heat; effects apply at the end of a day that ends at that level)")]
    public LevelRule cool = new LevelRule("Cool", 0f, 0f, 0f, 0f, 0f);
    public LevelRule warm = new LevelRule("Warm", 30f, -0.5f, 0f, 0f, 0f);
    public LevelRule hot = new LevelRule("Hot", 60f, -1.5f, -1f, -5f, 0.06f);
    public LevelRule onFire = new LevelRule("On fire", 85f, -3f, -2f, -10f, 0.15f);

    [Header("Audits")]
    [Tooltip("The fine is this many percent of company value per point of heat (0.1: heat 70 = 7%)")]
    public float auditFinePercentPerHeat = 0.1f;
    [Tooltip("The smallest fine...")]
    public double auditMinimumFine = 1000;
    [Tooltip("...but an audit never leaves the company with less than this")]
    public double auditReserve = 1000;
    public float auditTrust = -5f;
    public float auditHeat = -30f;

    [Header("Gambling freeze")]
    [Tooltip("At this heat the board freezes voluntary gambling...")]
    public float lockoutAt = 100f;
    [Tooltip("...until the heat is below this")]
    public float lockoutReleaseBelow = 70f;

    [Header("Casino (until its games report their rounds to GambleLedger themselves)")]
    [Tooltip("Real seconds without winnings after which a casino bet counts as lost (closing the casino settles it too)")]
    public float casinoRoundSeconds = 20f;

    [Header("Messages")]
    [Tooltip("Toast when the level changes or the board freezes / unfreezes gambling")]
    public bool announceLevelChanges = true;
    [Tooltip("Write every heat change to the Console")]
    public bool logChanges = true;

    [Header("Views (empty = found in the scene, else created and built at runtime)")]
    public HeatChipView chipView;
    public TiltPromptView tiltPromptView;

    [Serializable]
    class SavedHeat
    {
        public float heat;
        public bool lockedOut;
        public int audits;
        public double finesPaid;
        public string level;   // for people reading the save file; not read back
    }

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static readonly string[] DefaultLevelNames = { "Cool", "Warm", "Hot", "On fire" };
    // The levels' ids in the String Table: heat.level.<id>
    static readonly string[] LevelIds = { "cool", "warm", "hot", "onfire" };

    float heat;
    int level;
    bool lockedOut;
    int audits;
    double finesPaid;
    bool restoredThisLoad;
    CompanyManager hookedCompany;

    /// <summary>The heat now (0 to MaxHeat).</summary>
    public float Heat => heat;
    /// <summary>The heat as the chip shows it: rounded down, so it agrees with the level thresholds.</summary>
    public int DisplayHeat => Mathf.FloorToInt(heat + 0.001f);
    public float MaxHeat => Mathf.Max(1f, maxHeat);
    /// <summary>Heat as a share of MaxHeat (0-1), e.g. for a thermometer.</summary>
    public float Fraction => Mathf.Clamp01(heat / MaxHeat);
    /// <summary>LevelCool, LevelWarm, LevelHot or LevelOnFire.</summary>
    public int Level => level;
    public string LevelName => GetLevelName(level);
    /// <summary>The board has frozen voluntary gambling (from lockoutAt until the heat is below lockoutReleaseBelow).</summary>
    public bool IsLockedOut => lockedOut;
    public int AuditCount => audits;
    public double TotalFines => finesPaid;
    /// <summary>The "Win it back?" prompt (found or created in Start).</summary>
    public TiltPromptView TiltPrompt => tiltPromptView;

    /// <summary>Heat of the live meter (0 when there is none).</summary>
    public static float CurrentHeat => Instance != null ? Instance.heat : 0f;
    /// <summary>Level of the live meter (Cool when there is none).</summary>
    public static int CurrentLevel => Instance != null ? Instance.level : LevelCool;

    // ------------------------------------------------------------------
    // Life cycle
    // ------------------------------------------------------------------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Instance = null;
        HeatChanged = null;
        LevelChanged = null;
        LockoutChanged = null;
        Audited = null;
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[HeatMeter] Removed a second HeatMeter on '" + name + "'.");
            Destroy(this);
            return;
        }
        Instance = this;
        if (DemoMode.Active) ApplyDemoValues();   // Demo: also over values a scene may have saved
        level = LevelFor(heat);
    }

    /// <summary>Demo: the demo's bet heat and audit chances (DemoConfig).</summary>
    void ApplyDemoValues()
    {
        DemoConfig config = DemoMode.Config;
        betBaseHeat = config.heatBetBase;
        betMinHeat = config.heatBetMin;
        if (hot != null) hot.auditChance = config.auditChanceHot;
        if (onFire != null) onFire.auditChance = config.auditChanceOnFire;
    }

    void OnEnable()
    {
        Hook();
        GambleLedger.Gambled -= OnGambled;
        GambleLedger.Gambled += OnGambled;
    }

    void OnDisable()
    {
        Unhook();
        GambleLedger.Gambled -= OnGambled;
    }

    void Start()
    {
        chipView = FindOrCreate(chipView, "HeatChipView");
        tiltPromptView = FindOrCreate(tiltPromptView, "TiltPromptView");
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        Hook();
        GambleLedger.Poll(casinoRoundSeconds);
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

    // ------------------------------------------------------------------
    // Levels
    // ------------------------------------------------------------------

    /// <summary>The level a heat value falls in.</summary>
    public int LevelFor(float value)
    {
        if (value >= onFire.from) return LevelOnFire;
        if (value >= hot.from) return LevelHot;
        if (value >= warm.from) return LevelWarm;
        return LevelCool;
    }

    /// <summary>What a level does (LevelCool..LevelOnFire).</summary>
    public LevelRule GetRule(int levelIndex)
    {
        switch (levelIndex)
        {
            case LevelWarm: return warm;
            case LevelHot: return hot;
            case LevelOnFire: return onFire;
            default: return cool;
        }
    }

    /// <summary>
    /// "Cool", "Warm", "Hot" or "On fire" in the current language (heat.level.*). A name changed on the live meter in the
    /// Inspector shows as typed.
    /// </summary>
    public static string GetLevelName(int levelIndex)
    {
        levelIndex = Mathf.Clamp(levelIndex, LevelCool, LevelOnFire);
        string name = GetLevelEnglishName(levelIndex);
        return name == DefaultLevelNames[levelIndex] ? L10n.Get(Table, "heat.level." + LevelIds[levelIndex]) : name;
    }

    /// <summary>The level's name as set on the meter (English by default), for the save and the Console log.</summary>
    public static string GetLevelEnglishName(int levelIndex)
    {
        levelIndex = Mathf.Clamp(levelIndex, LevelCool, LevelOnFire);
        if (Instance != null)
        {
            string custom = Instance.GetRule(levelIndex).name;
            if (!string.IsNullOrWhiteSpace(custom)) return custom.Trim();
        }
        return DefaultLevelNames[levelIndex];
    }

    /// <summary>A level's colour for bars and fills: Cool blue, Warm gold, Hot orange, On fire red.</summary>
    public static Color LevelColor(int levelIndex)
    {
        switch (levelIndex)
        {
            case LevelWarm: return UIColors.Gold;
            case LevelHot: return UIColors.Warn;
            case LevelOnFire: return UIColors.Loss;
            default: return UIColors.Info;
        }
    }

    /// <summary>A level's colour for text on paper (gold is too light to read, so Warm uses the dark gold).</summary>
    public static Color LevelInkColor(int levelIndex)
    {
        return levelIndex == LevelWarm ? UIColors.GoldDark : LevelColor(levelIndex);
    }

    /// <summary>
    /// What a level costs, for messages and tooltips, in the current language: "trust −1.5 and happiness −1 a day,
    /// risk-card odds −5%, 6% audit risk a day" (heat.effects.*).
    /// </summary>
    public string EffectsSummary(int levelIndex)
    {
        LevelRule rule = GetRule(levelIndex);
        var parts = new List<string>();
        bool trust = rule.dailyTrust != 0f, happiness = rule.dailyHappiness != 0f;
        if (trust && happiness)
            parts.Add(L10n.Get(Table, "heat.effects.daily.both", ("trust", Signed(rule.dailyTrust)), ("happiness", Signed(rule.dailyHappiness))));
        else if (trust)
            parts.Add(L10n.Get(Table, "heat.effects.daily.trust", ("trust", Signed(rule.dailyTrust))));
        else if (happiness)
            parts.Add(L10n.Get(Table, "heat.effects.daily.happiness", ("happiness", Signed(rule.dailyHappiness))));
        if (rule.riskCardPoints != 0f) parts.Add(L10n.Get(Table, "heat.effects.odds", ("points", Signed(rule.riskCardPoints))));
        if (rule.auditChance > 0f) parts.Add(L10n.Get(Table, "heat.effects.audit", ("chance", DisplayNumber(rule.auditChance * 100f))));
        return string.Join(L10n.Get(Table, "heat.effects.separator"), parts);
    }

    // ------------------------------------------------------------------
    // Changing the heat
    // ------------------------------------------------------------------

    /// <summary>Add heat (negative = remove). <paramref name="why"/> goes to the Console log.</summary>
    public void AddHeat(float amount, string why)
    {
        if (float.IsNaN(amount) || float.IsInfinity(amount) || amount == 0f) return;
        ApplyHeat(heat + amount, why, true);
    }

    /// <summary>Set the heat (clamped to 0-MaxHeat), with the usual messages and events.</summary>
    public void SetHeat(float value, string why = "set")
    {
        ApplyHeat(value, why, true);
    }

    /// <summary>Heat a bet adds: clamp(betBaseHeat + betStakeHeat x stake / company value before, betMinHeat, betMaxHeat).</summary>
    public float BetHeat(double stake, double companyValueBefore)
    {
        if (!(stake > 0.0)) return 0f;
        double share = companyValueBefore > 0.0 ? stake / companyValueBefore : 1.0;
        if (double.IsNaN(share) || double.IsInfinity(share)) share = 1.0;
        float raw = betBaseHeat + betStakeHeat * (float)Math.Min(share, 100.0);
        return Mathf.Clamp(raw, betMinHeat, Mathf.Max(betMinHeat, betMaxHeat));
    }

    /// <summary>Heat a risk card adds when it is picked.</summary>
    public float RiskCardHeat(CardRisk risk)
    {
        switch (risk)
        {
            case CardRisk.HighRisk: return highRiskHeat;
            case CardRisk.LowRisk: return lowRiskHeat;
            default: return balancedHeat;
        }
    }

    void OnGambled(GambleRecord record)
    {
        if (record.stake <= 0.0) return;   // a payout on its own (late casino winnings) adds nothing
        float add = BetHeat(record.stake, record.companyValueBefore);
        if (DemoMode.Active && record.source == GambleLedger.SourceBuildGamble)
            add *= DemoMode.Config.buildGambleHeatScale;   // Demo: build-budget flips draw less attention
        AddHeat(add, GambleLedger.Describe(record.source) + ", " + UIFormat.Money(record.stake) + " staked");
    }

    void ApplyHeat(float value, string why, bool announce)
    {
        if (float.IsNaN(value) || float.IsInfinity(value)) value = 0f;
        float oldHeat = heat;
        int oldLevel = level;
        bool oldLock = lockedOut;

        // Two decimals: the thresholds compare cleanly and the chip's number agrees with the level
        heat = Mathf.Clamp(Mathf.Round(value * 100f) / 100f, 0f, MaxHeat);
        level = LevelFor(heat);
        if (!lockedOut && heat >= LockoutHeat) lockedOut = true;
        else if (lockedOut && heat < lockoutReleaseBelow) lockedOut = false;

        bool heatMoved = !Mathf.Approximately(oldHeat, heat);
        if (heatMoved && logChanges)
            Debug.Log("[HeatMeter] Heat " + Number(oldHeat) + " -> " + Number(heat) + " (" + why + "): " + GetLevelEnglishName(level) + (lockedOut ? ", gambling frozen" : ""));

        if (heatMoved) RaiseHeatChanged(heat);
        if (level != oldLevel) RaiseLevelChanged(oldLevel, level);
        if (lockedOut != oldLock) RaiseLockoutChanged(lockedOut);
        if (announce && (level != oldLevel || lockedOut != oldLock))
            Announce(oldLevel, level, lockedOut != oldLock);
        if (announce && ((level > oldLevel && level >= LevelHot) || (lockedOut && !oldLock)))
            GameSfx.Play(Sfx.UiGlitch);   // up to Hot or On fire, or the board froze gambling
    }

    float LockoutHeat => Mathf.Min(lockoutAt, MaxHeat);

    /// <summary>One toast for a level change and/or a freeze change.</summary>
    void Announce(int oldLevel, int newLevel, bool lockChanged)
    {
        if (!announceLevelChanges) return;
        if (lockChanged && lockedOut)
        {
            GameNotifications.Show(FreezeMessage, NotificationKind.Bad);
            return;
        }

        string message = "";
        NotificationKind kind = NotificationKind.Info;
        if (newLevel != oldLevel)
        {
            string effects = EffectsSummary(newLevel);
            bool up = newLevel > oldLevel;
            string key = (up ? "heat.toast.up" : "heat.toast.down") + (effects.Length > 0 ? "" : ".plain");
            message = L10n.Get(Table, key, ("level", GetLevelName(newLevel)), ("effects", effects));
            kind = up ? NotificationKind.Bad : NotificationKind.Good;
        }
        if (lockChanged)
        {
            string lifted = L10n.Get(Table, "heat.toast.unfrozen");
            message = message.Length > 0 ? L10n.Get(Table, "heat.toast.join", ("first", message), ("second", lifted)) : lifted;
            kind = NotificationKind.Good;
        }
        if (message.Length > 0) GameNotifications.Show(message, kind);
    }

    // ------------------------------------------------------------------
    // Hooks for other systems
    // ------------------------------------------------------------------

    /// <summary>
    /// False while the board has frozen voluntary gambling; <paramref name="reason"/> then says why (show it and don't
    /// take the bet). True when there's no heat meter.
    /// </summary>
    public static bool CanGamble(out string reason)
    {
        HeatMeter meter = Instance;
        if (meter == null || !meter.lockedOut)
        {
            reason = "";
            return true;
        }
        reason = meter.FreezeMessage;
        return false;
    }

    /// <summary>"The board froze your gambling until things cool down (heat 100 → below 70)." in the current language.</summary>
    public string FreezeMessage =>
        L10n.Get(Table, "heat.freeze.message", ("from", DisplayNumber(LockoutHeat)), ("to", DisplayNumber(lockoutReleaseBelow)));

    /// <summary>Change to every risk card's good-outcome chance from the heat, in 0-1 units (-0.05 Hot, -0.10 On fire).</summary>
    public static float RiskCardChanceModifier
    {
        get
        {
            HeatMeter meter = Instance;
            return meter != null ? meter.GetRule(meter.level).riskCardPoints / 100f : 0f;
        }
    }

    /// <summary>EventManager: the player picked a risk card (called after its outcome is rolled, so it can't change its own odds).</summary>
    public static void OnRiskCardChosen(CardRisk risk)
    {
        HeatMeter meter = Instance;
        if (meter == null) return;
        try
        {
            meter.AddHeat(meter.RiskCardHeat(risk), risk + " card");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    // ------------------------------------------------------------------
    // End of the day: effects, audit, cooling
    // ------------------------------------------------------------------

    void OnDayEnded(int day, double change)
    {
        CompanyManager company = CompanyManager.Instance;
        if (company == null) return;
        LevelRule rule = GetRule(level);

        // What a day at this heat cost (CompanyManager raises StatsChanged right after DayEnded)
        if (rule.dailyTrust != 0f)
            company.investorTrust = Mathf.Clamp(company.investorTrust + rule.dailyTrust, 0f, 100f);
        if (rule.dailyHappiness != 0f)
            company.AddHappiness(rule.dailyHappiness);

        float next = heat;
        string why = "day " + Mathf.Max(1, day - 1) + " ended";
        if (rule.auditChance > 0f && UnityEngine.Random.value < rule.auditChance)
        {
            next += RunAudit(company);
            why += ", audit";
        }

        // Then it cools for the next day (one heat change, so at most one level toast)
        next = Mathf.Max(0f, next * dailyCoolKeep - dailyCoolDrop);
        ApplyHeat(next, why + ", cooled", true);
    }

    /// <summary>The audit's fine, trust drop, toast and event; returns the heat change for the caller to apply.</summary>
    float RunAudit(CompanyManager company)
    {
        double value = Math.Max(0.0, company.companyValue);
        double fine = value * (heat * auditFinePercentPerHeat / 100.0);
        fine = Math.Max(fine, auditMinimumFine);
        fine = Math.Floor(Math.Max(0.0, Math.Min(fine, value - Math.Max(0.0, auditReserve))));
        if (fine > 0.0) company.AddMoney(-fine, true);
        company.investorTrust = Mathf.Clamp(company.investorTrust + auditTrust, 0f, 100f);
        audits++;
        GameSfx.PlayPair(Sfx.UiError, fine > 0.0 ? Sfx.ChipsCollect : Sfx.None, 0.25f);   // the regulators take their fine
        finesPaid += fine;

        string toast = (fine > 0.0 ? "heat.audit.fined" : "heat.audit.nothing") + (auditHeat != 0f ? "" : ".noheat");
        GameNotifications.Show(L10n.Get(Table, toast, ("amount", UIFormat.Money(fine)), ("heat", Signed(auditHeat))), NotificationKind.Bad);
        if (logChanges)
            Debug.Log("[HeatMeter] Audit at heat " + Number(heat) + ": fined " + UIFormat.Money(fine) + ", trust " + Signed(auditTrust));

        Action<double> handlers = Audited;
        if (handlers != null)
        {
            foreach (Action<double> handler in handlers.GetInvocationList())
            {
                try { handler(fine); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }
        return auditHeat;
    }

    /// <summary>Debug: a regulator audit right now, whatever the level (the fine follows the current heat). Returns the fine.</summary>
    public double ForceAudit()
    {
        CompanyManager company = CompanyManager.Instance;
        if (company == null) return 0.0;
        double before = finesPaid;
        float change = RunAudit(company);
        ApplyHeat(heat + change, "audit (forced)", true);
        company.UpdateCompanyStats();   // the trust drop reaches the HUD
        return finesPaid - before;
    }

    // ------------------------------------------------------------------
    // Save
    // ------------------------------------------------------------------

    public string SaveKey => Key;

    public string CaptureState()
    {
        return JsonUtility.ToJson(new SavedHeat
        {
            heat = heat,
            lockedOut = lockedOut,
            audits = audits,
            finesPaid = finesPaid,
            level = GetLevelEnglishName(level)
        });
    }

    public void RestoreState(string json)
    {
        SavedHeat data = null;
        if (!string.IsNullOrEmpty(json))
        {
            try { data = JsonUtility.FromJson<SavedHeat>(json); }
            catch (Exception e) { Debug.LogWarning("[HeatMeter] Couldn't read the saved heat (starting cool): " + e.Message); }
        }
        data = data ?? new SavedHeat();
        restoredThisLoad = true;
        audits = Mathf.Max(0, data.audits);
        finesPaid = double.IsNaN(data.finesPaid) || double.IsInfinity(data.finesPaid) || data.finesPaid < 0.0 ? 0.0 : data.finesPaid;
        LoadHeat(data.heat, data.lockedOut, "loaded");
    }

    // SaveSystem raises this after it applied a save (after RestoreState when the save had heat)
    void OnStateLoaded()
    {
        if (!restoredThisLoad)
        {
            // A save from before the heat meter: no heat
            audits = 0;
            finesPaid = 0.0;
            LoadHeat(0f, false, "save without heat");
        }
        restoredThisLoad = false;
    }

    /// <summary>A loaded heat, without toasts; the HUD hears HeatChanged, LevelChanged and LockoutChanged once.</summary>
    void LoadHeat(float value, bool wasLocked, string how)
    {
        if (float.IsNaN(value) || float.IsInfinity(value)) value = 0f;
        int oldLevel = level;
        heat = Mathf.Clamp(Mathf.Round(value * 100f) / 100f, 0f, MaxHeat);
        level = LevelFor(heat);
        lockedOut = heat >= LockoutHeat || (wasLocked && heat >= lockoutReleaseBelow);
        if (logChanges)
            Debug.Log("[HeatMeter] Heat " + Number(heat) + " (" + how + "): " + GetLevelEnglishName(level) + (lockedOut ? ", gambling frozen" : ""));
        RaiseHeatChanged(heat);
        RaiseLevelChanged(oldLevel, level);
        RaiseLockoutChanged(lockedOut);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    static void RaiseHeatChanged(float value)
    {
        Action<float> handlers = HeatChanged;
        if (handlers == null) return;
        foreach (Action<float> handler in handlers.GetInvocationList())
        {
            try { handler(value); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }

    static void RaiseLevelChanged(int oldLevel, int newLevel)
    {
        Action<int, int> handlers = LevelChanged;
        if (handlers == null) return;
        foreach (Action<int, int> handler in handlers.GetInvocationList())
        {
            try { handler(oldLevel, newLevel); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }

    static void RaiseLockoutChanged(bool locked)
    {
        Action<bool> handlers = LockoutChanged;
        if (handlers == null) return;
        foreach (Action<bool> handler in handlers.GetInvocationList())
        {
            try { handler(locked); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }

    /// <summary>"+4", "−1.5" (a true minus sign; the decimal mark of the current language: "−1,5" in Turkish).</summary>
    public static string Signed(float value)
    {
        // Numbers look the same in every language (the user's rule for money applies to all numbers)
        string body = Math.Abs(value).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
        return value < 0f ? UIFormat.Minus + body : "+" + body;
    }

    /// <summary>A number for the Console log (always "72.5").</summary>
    static string Number(float value)
    {
        return value.ToString("0.#", Inv);
    }

    /// <summary>A number for the player, with the current language's decimal mark ("72.5", "72,5").</summary>
    static string DisplayNumber(float value)
    {
        return value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// Adds the heat meter (with its chip and tilt prompt) to every scene that has a CompanyManager, when it loads, unless
/// one is already there. So MainGame needs no scene edit.
/// </summary>
static class HeatBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        EnsureMeter();   // the first scene loaded before this ran
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EnsureMeter();
    }

    static void EnsureMeter()
    {
        if (!Application.isPlaying) return;
        CompanyManager company = CompanyManager.Instance != null ? CompanyManager.Instance : UnityEngine.Object.FindAnyObjectByType<CompanyManager>();
        if (company == null) return;
        if (HeatMeter.Instance != null || UnityEngine.Object.FindAnyObjectByType<HeatMeter>(FindObjectsInactive.Include) != null) return;

        GameObject go = new GameObject("Heat (Runtime)");
        Scene scene = company.gameObject.scene;
        if (scene.IsValid() && scene.isLoaded && go.scene != scene)
        {
            // Live and die with the company's scene
            try { SceneManager.MoveGameObjectToScene(go, scene); }
            catch (Exception e) { Debug.LogWarning("[Heat] Couldn't move the heat meter next to the CompanyManager: " + e.Message); }
        }
        go.AddComponent<HeatMeter>();
    }
}
