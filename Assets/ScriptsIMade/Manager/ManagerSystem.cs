using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The manager: a hired character who watches the company and brings decisions up to the CEO (the player).
/// - Arrives on day 5 (FeatureGate Feature.Manager): the player picks one of three candidates with different personalities
///   (conservative, aggressive, people-first, numbers-first), salaries and bios. A manager who resigns is replaced the same
///   way after a short gap.
/// - Situations are data (Resources/Manager/Situations/*.json, loaded into a ManagerLibrary; Engine.json tunes the engine).
///   Every day (and on events: capacity, big gambles, profit spikes, hires, fires, a new tier) the engine looks at the
///   company's real state (ManagerFacts), picks a relevant situation by weight (state pressure per category, cooldowns),
///   resolves its targets and params and queues it as a pending decision on the desk (at most maxPending; the oldest
///   non-urgent one then goes with its default). Only urgent situations flagged "interrupt" open the desk by themselves,
///   at most once every few minutes and never over another panel.
/// - The player answers from the desk (ManagerDeskView, a pausing panel opened from the side chip, ManagerChipView): 2-4
///   choices with consequence hints and the manager's recommendation ("I'd do this"), which depends on their personality.
///   Effects are ops from ManagerEffects (money with a cloud-ledger entry, salaries, moods, meters, hires and fires, rented
///   seats, policies and revenue shares as daily costs, gambles through GambleLedger, stocks, follow-ups, flags...).
/// - The manager is an employee: a salary (a daily cost through DailyEconomyHooks), satisfaction 0-100 that drifts with pay
///   versus expectation and reacts to followed or ignored advice, expired decisions and choices against their
///   personality. An unhappy manager asks for a raise, handles delegated work worse, gets outside offers, threatens to
///   leave and finally resigns.
/// - Recurring money only ever COSTS (the cloud save flags unexplained growth); one-off money goes to CloudLedger as
///   LedgerKinds.Manager entries.
/// - Saved under "manager" (ISaveParticipant). Created automatically in every scene with a CompanyManager.
/// </summary>
[DisallowMultipleComponent]
public partial class ManagerSystem : MonoBehaviour, ISaveParticipant, IDailyEconomyHook
{
    public const string Key = "manager";
    /// <summary>OfficeCapacity extra id of the rented seats.</summary>
    public const string RentalCapacityId = "manager.rentals";
    /// <summary>Money a choice never touches (it can't leave the company with less).</summary>
    public const double Reserve = 1000.0;

    public static ManagerSystem Instance { get; private set; }

    /// <summary>Something the chip or the desk shows changed.</summary>
    public static event Action Changed;
    /// <summary>A new decision landed on the desk.</summary>
    public static event Action<ManagerPending> DecisionAdded;

    [Tooltip("Write the manager's decisions and changes to the Console")]
    [SerializeField] bool logChanges = true;
    [Tooltip("Real seconds to wait for the save before treating the company as an older save")]
    [SerializeField] float unknownStateTimeout = 8f;

    ManagerSaveState state = new ManagerSaveState();
    ManagerLibrary library;
    bool stateKnown;
    bool restoredThisLoad;
    float unknownTime;
    CompanyManager hookedCompany;
    bool hookRegistered;
    bool rentalsRegistered;
    ManagerChipView chipView;
    ManagerDeskView deskView;
    ManagerPickView pickView;

    public ManagerLibrary Library => library;
    public bool IsStateKnown => stateKnown;
    public string Phase => state.phase;
    public bool HasManager => state.phase == ManagerPhase.Active && !state.manager.IsEmpty;
    public ManagerPerson Manager => state.manager;
    public IReadOnlyList<ManagerPerson> Candidates => state.candidates;
    public IReadOnlyList<ManagerPending> Pending => state.pending;
    /// <summary>Decisions made, delegated or left to expire, oldest first (at most 40).</summary>
    public IReadOnlyList<ManagerHistory> History => state.history;
    public int PendingCount => state.pending.Count;
    public int GapUntilDay => state.gapUntilDay;
    public ManagerStats Stats => state.stats;
    public ManagerEngineSettings Engine => library != null ? library.Engine : new ManagerEngineSettings();
    internal ManagerSaveState State => state;
    /// <summary>The saved state, for debug tools (read it; change it through the methods here).</summary>
    public ManagerSaveState DebugState => state;

    public int UrgentCount
    {
        get
        {
            int n = 0;
            foreach (ManagerPending p in state.pending) if (p.urgent) n++;
            return n;
        }
    }

    public int UnseenCount
    {
        get
        {
            int n = 0;
            foreach (ManagerPending p in state.pending) if (!p.seen) n++;
            return n;
        }
    }

    static int Today => CompanyManager.Instance != null ? CompanyManager.Instance.currentDay : 0;

    // ------------------------------------------------------------------
    // Creation
    // ------------------------------------------------------------------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Instance = null;
        Changed = null;
        DecisionAdded = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Ensure();   // the first scene is loaded already
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Ensure();
    }

    /// <summary>The scene's manager system, created if missing. Null in a scene without a CompanyManager.</summary>
    public static ManagerSystem Ensure()
    {
        if (Instance != null) return Instance;
        if (!Application.isPlaying) return null;
        CompanyManager company = CompanyManager.Instance != null ? CompanyManager.Instance : FindAnyObjectByType<CompanyManager>();
        if (company == null) return null;
        ManagerSystem existing = FindAnyObjectByType<ManagerSystem>(FindObjectsInactive.Include);
        if (existing != null) return existing;

        var go = new GameObject("Manager (Runtime)");
        Scene scene = company.gameObject.scene;
        if (scene.IsValid() && scene.isLoaded && go.scene != scene)
        {
            // Live and die with the company's scene (and be found by SaveSystem before it restores the scene)
            try { SceneManager.MoveGameObjectToScene(go, scene); }
            catch (Exception e) { Debug.LogWarning("[Manager] Couldn't move the manager next to the CompanyManager: " + e.Message); }
        }
        return go.AddComponent<ManagerSystem>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[Manager] Removed a second ManagerSystem on '" + name + "' (only one may save under '" + Key + "').");
            Destroy(this);
            return;
        }
        Instance = this;
        LoadLibrary();
    }

    void OnEnable()
    {
        if (Instance != this) return;
        if (!hookRegistered)
        {
            DailyEconomyHooks.Register(this);
            hookRegistered = true;
        }
        GambleLedger.Gambled -= OnGambled;
        GambleLedger.Gambled += OnGambled;
        StaffLedger.EmployeeFired -= OnEmployeeFired;
        StaffLedger.EmployeeFired += OnEmployeeFired;
        OfficeCapacity.Changed -= OnCapacityChanged;
        OfficeCapacity.Changed += OnCapacityChanged;
        StatusLadder.TierChanged -= OnTierChanged;
        StatusLadder.TierChanged += OnTierChanged;
        FeatureGate.Unlocked -= OnFeatureUnlocked;
        FeatureGate.Unlocked += OnFeatureUnlocked;
        L10n.LanguageChanged += OnLanguageChanged;
        if (stateKnown) RegisterRentals();
    }

    void OnDisable()
    {
        DailyEconomyHooks.Unregister(this);
        hookRegistered = false;
        GambleLedger.Gambled -= OnGambled;
        StaffLedger.EmployeeFired -= OnEmployeeFired;
        OfficeCapacity.Changed -= OnCapacityChanged;
        StatusLadder.TierChanged -= OnTierChanged;
        FeatureGate.Unlocked -= OnFeatureUnlocked;
        L10n.LanguageChanged -= OnLanguageChanged;
        UnregisterRentals();
    }

    void Start()
    {
        if (Instance != this) return;
        chipView = GetOrAddView<ManagerChipView>("ManagerChip");
        deskView = GetOrAddView<ManagerDeskView>("ManagerDesk");
        pickView = GetOrAddView<ManagerPickView>("ManagerPick");
    }

    T GetOrAddView<T>(string childName) where T : Component
    {
        T view = GetComponentInChildren<T>(true);
        if (view != null) return view;
        var go = new GameObject(childName);
        go.transform.SetParent(transform, false);
        return go.AddComponent<T>();
    }

    void OnDestroy()
    {
        Unhook();
        if (Instance == this)
        {
            Instance = null;
            ManagerBuildPerks.SetSource(null);
        }
    }

    void LoadLibrary()
    {
        var files = new List<KeyValuePair<string, string>>();
        foreach (TextAsset asset in Resources.LoadAll<TextAsset>("Manager/Situations"))
            if (asset != null) files.Add(new KeyValuePair<string, string>(asset.name + ".json", asset.text));
        files.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
        TextAsset engine = Resources.Load<TextAsset>("Manager/Engine");
        library = ManagerLibrary.Load(engine != null ? engine.text : null, files);

        int errors = 0;
        foreach (ManagerProblem p in library.Problems)
        {
            if (!p.IsError) continue;
            errors++;
            if (errors <= 20) Debug.LogWarning("[Manager] " + p);
        }
        var missing = new List<string>();
        foreach (string op in ManagerSchema.Ops.Keys)
            if (!ManagerEffects.Has(op)) missing.Add(op);
        if (missing.Count > 0) Debug.LogError("[Manager] Effect ops without a handler: " + string.Join(", ", missing));
        Log("Loaded " + library.Situations.Count + " situations from " + files.Count + " files" + (errors > 0 ? " (" + errors + " broken ones left out: Tools > Game > Manager > Validate Situations)" : "") + ".");
    }

    /// <summary>Reload the situation files (debug: after editing them in Play mode).</summary>
    public void ReloadLibrary()
    {
        LoadLibrary();
        RaiseChanged();
    }

    // ------------------------------------------------------------------
    // Company hooks
    // ------------------------------------------------------------------

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
            company.EmployeeHired += OnEmployeeHired;
        }
    }

    void Unhook()
    {
        if (hookedCompany != null)
        {
            hookedCompany.DayEnded -= OnDayEnded;
            hookedCompany.StateLoaded -= OnStateLoaded;
            hookedCompany.EmployeeHired -= OnEmployeeHired;
        }
        hookedCompany = null;
    }

    void Update()
    {
        if (Instance != this) return;
        Hook();
        if (hookedCompany == null) return;

        if (!stateKnown)
        {
            // A new company (or no save): SaveSystem finishes without raising StateLoaded
            SaveSystem save = SaveSystem.Instance;
            if (save == null || (save.IsGameLoaded && !save.IsLoading))
            {
                BeginFresh("a new company");
                return;
            }
            unknownTime += Time.unscaledDeltaTime;
            if (unknownTime >= unknownStateTimeout)
            {
                BeginFresh("the save didn't finish loading");
                Debug.LogWarning("[Manager] The saved game didn't finish loading; starting the manager's notes fresh.");
            }
            return;
        }

        CheckFeature();
        ProcessTriggers();
        UpdateCalm();
        TryInterrupt();
        TryAutoOpenPick();
        TryOpenRequestedBuildMode();
    }

    // ------------------------------------------------------------------
    // Loading and saving
    // ------------------------------------------------------------------

    void BeginFresh(string why)
    {
        state = new ManagerSaveState();
        stateKnown = true;
        restoredThisLoad = false;
        unknownTime = 0f;
        AfterStateChanged();
        Log("Fresh notes (" + why + ").");
    }

    // SaveSystem raises this after it applied a save (after RestoreState, when the save had "manager")
    void OnStateLoaded()
    {
        bool fromSave = restoredThisLoad;
        restoredThisLoad = false;
        stateKnown = true;
        unknownTime = 0f;
        if (!fromSave) state = new ManagerSaveState();   // a save from before the manager
        Sanitize();
        AfterStateChanged();
        Log(fromSave ? "Carrying on (" + state.phase + ", " + state.pending.Count + " decisions waiting)." : "A company from before the manager: fresh notes.");
    }

    void AfterStateChanged()
    {
        UnregisterRentals();
        RegisterRentals();
        ManagerBuildPerks.SetSource(state.perks);
        triggers.Clear();
        calmTime = 0f;
        RaiseChanged();
    }

    public string SaveKey => Key;

    public string CaptureState()
    {
        return JsonUtility.ToJson(state);
    }

    public void RestoreState(string json)
    {
        ManagerSaveState loaded = null;
        if (!string.IsNullOrEmpty(json))
        {
            try { loaded = JsonUtility.FromJson<ManagerSaveState>(json); }
            catch (Exception e) { Debug.LogWarning("[Manager] Couldn't read the saved manager (starting fresh): " + e.Message); }
        }
        if (loaded == null) return;   // OnStateLoaded treats it like an older save
        state = loaded;
        restoredThisLoad = true;
    }

    /// <summary>Repair whatever an old, partial or edited save got wrong.</summary>
    void Sanitize()
    {
        ManagerSaveState s = state;
        if (s.manager == null) s.manager = new ManagerPerson();
        if (s.candidates == null) s.candidates = new List<ManagerPerson>();
        if (s.pending == null) s.pending = new List<ManagerPending>();
        if (s.followups == null) s.followups = new List<ManagerFollowup>();
        if (s.cooldowns == null) s.cooldowns = new List<ManagerNamedDay>();
        if (s.categoryDays == null) s.categoryDays = new List<ManagerNamedDay>();
        if (s.flags == null) s.flags = new List<ManagerNamedDay>();
        if (s.seen == null) s.seen = new List<string>();
        if (s.chosen == null) s.chosen = new List<string>();
        if (s.policies == null) s.policies = new List<ManagerPolicy>();
        if (s.rentals == null) s.rentals = new List<ManagerRental>();
        if (s.shares == null) s.shares = new List<ManagerShare>();
        if (s.perks == null) s.perks = new List<ManagerPerk>();
        if (s.moods == null) s.moods = new List<ManagerMood>();
        if (s.recentProfits == null) s.recentProfits = new List<double>();
        if (s.recentValues == null) s.recentValues = new List<double>();
        if (s.gambles == null) s.gambles = new List<ManagerGambleNote>();
        if (s.firedDays == null) s.firedDays = new List<int>();
        if (s.hiredDays == null) s.hiredDays = new List<int>();
        if (s.stats == null) s.stats = new ManagerStats();
        if (s.history == null) s.history = new List<ManagerHistory>();

        if (s.phase != ManagerPhase.None && s.phase != ManagerPhase.Picking && s.phase != ManagerPhase.Active && s.phase != ManagerPhase.Gap)
            s.phase = ManagerPhase.None;
        if (s.phase == ManagerPhase.Active && s.manager.IsEmpty) s.phase = ManagerPhase.Gap;
        if (s.phase == ManagerPhase.Picking && s.candidates.Count == 0) s.phase = ManagerPhase.Gap;

        ManagerPerson m = s.manager;
        if (!m.IsEmpty)
        {
            if (!ManagerSchema.IsPersonality(m.personality)) m.personality = ManagerSchema.Conservative;
            m.satisfaction = Mathf.Clamp(Finite(m.satisfaction, 60f), 0f, 100f);
            m.competence = Mathf.Clamp(Finite(m.competence, 1f), 0.7f, 1.3f);
            m.salary = Math.Max(0, FiniteD(m.salary));
            if (m.expectedBase <= 0) m.expectedBase = Math.Max(50000, m.salary);
        }
        foreach (ManagerPerson c in s.candidates)
            if (c != null && !ManagerSchema.IsPersonality(c.personality)) c.personality = ManagerSchema.Conservative;
        s.candidates.RemoveAll(c => c == null || c.IsEmpty);

        var uids = new HashSet<int>();
        s.pending.RemoveAll(p => p == null || library.Get(p.situation) == null);
        foreach (ManagerPending p in s.pending)
        {
            if (p.targets == null) p.targets = new List<ManagerTargetState>();
            if (p.prms == null) p.prms = new List<ManagerParamState>();
            if (p.uid <= 0 || !uids.Add(p.uid))
            {
                p.uid = Math.Max(1, s.nextUid++);
                uids.Add(p.uid);
            }
        }
        foreach (int uid in uids) if (uid >= s.nextUid) s.nextUid = uid + 1;
        s.followups.RemoveAll(f => f == null || library.Get(f.situation) == null);
        foreach (ManagerFollowup f in s.followups)
        {
            if (f.targets == null) f.targets = new List<ManagerTargetState>();
            if (f.prms == null) f.prms = new List<ManagerParamState>();
        }
        s.rentals.RemoveAll(r => r == null || r.seats <= 0);
        s.policies.RemoveAll(p => p == null || string.IsNullOrEmpty(p.id));
        s.shares.RemoveAll(x => x == null || x.pct <= 0);
        s.perks.RemoveAll(x => x == null || x.discount <= 0f);
        foreach (ManagerRental r in s.rentals) r.rate = Math.Max(0, FiniteD(r.rate));
        foreach (ManagerPolicy p in s.policies)
        {
            p.costDay = Math.Max(0, FiniteD(p.costDay));
            p.costEmployee = Math.Max(0, FiniteD(p.costEmployee));
        }
        foreach (ManagerShare x in s.shares) x.pct = Math.Min(50, Math.Max(0, FiniteD(x.pct)));
        foreach (ManagerPerk x in s.perks) x.discount = Mathf.Clamp(x.discount, 0f, 50f);
        foreach (ManagerMood mood in s.moods) if (mood != null) mood.mood = Mathf.Clamp(Finite(mood.mood, 50f), 0f, 100f);
        s.moods.RemoveAll(x => x == null || string.IsNullOrEmpty(x.id));
        TrimList(s.recentProfits, 7);
        TrimList(s.recentValues, 8);
        if (s.history.Count > 40) s.history.RemoveRange(0, s.history.Count - 40);
    }

    static float Finite(float v, float fallback) => float.IsNaN(v) || float.IsInfinity(v) ? fallback : v;
    static double FiniteD(double v) => double.IsNaN(v) || double.IsInfinity(v) ? 0.0 : v;

    static void TrimList<T>(List<T> list, int max)
    {
        if (list.Count > max) list.RemoveRange(0, list.Count - max);
    }

    // ------------------------------------------------------------------
    // Daily money and meters (DailyEconomyHooks): costs only
    // ------------------------------------------------------------------

    public string HookName => "manager";

    public double DailyMoney(CompanyManager company, double resultBeforeHooks)
    {
        if (!stateKnown || company == null) return 0.0;
        // ProcessNewDay has already counted the new day: the day being booked is the one before it
        int day = company.currentDay - 1;
        int staff = company.employees != null ? company.employees.Count : 0;
        double cost = 0.0;
        if (HasManager) cost += Math.Max(0.0, state.manager.salary) / 365.0;
        foreach (ManagerPolicy p in state.policies)
            if (RunsOn(p.startDay, p.untilDay, day)) cost += Math.Max(0.0, p.costDay) + Math.Max(0.0, p.costEmployee) * staff;
        foreach (ManagerRental r in state.rentals)
            if (RunsOn(r.startDay, r.untilDay, day)) cost += Math.Max(0, r.seats) * Math.Max(0.0, r.rate);
        foreach (ManagerShare sh in state.shares)
        {
            if (!RunsOn(sh.startDay, sh.untilDay, day)) continue;
            double share = Math.Max(0.0, resultBeforeHooks) * Math.Max(0.0, sh.pct) / 100.0;
            sh.paid += share;
            cost += share;
        }
        if (double.IsNaN(cost) || double.IsInfinity(cost)) cost = 0.0;
        state.stats.recurringPaid += cost;
        return -cost;   // never an income
    }

    public void DailyMeters(CompanyManager company, ref float happiness, ref float trust)
    {
        if (!stateKnown || company == null) return;
        int day = company.currentDay - 1;
        foreach (ManagerPolicy p in state.policies)
        {
            if (!RunsOn(p.startDay, p.untilDay, day)) continue;
            happiness += p.happiness;
            trust += p.trust;
        }
        // Employees the manager knows are miserable drag the office down a little
        int miserable = 0;
        if (company.employees != null)
            foreach (ApplicantData e in company.employees)
                if (e != null && MoodOf(e) < 25f) miserable++;
        if (miserable > 0) happiness -= Mathf.Min(1.5f, 0.3f * miserable);
    }

    static bool RunsOn(int start, int until, int day) => day >= start && (until <= 0 || day <= until);

    // ------------------------------------------------------------------
    // Rented seats (OfficeCapacity extra)
    // ------------------------------------------------------------------

    /// <summary>Seats rented now.</summary>
    public int RentedSeats
    {
        get
        {
            int day = Today, seats = 0;
            foreach (ManagerRental r in state.rentals)
                if (r.untilDay <= 0 || day <= r.untilDay) seats += Math.Max(0, r.seats);
            return seats;
        }
    }

    void RegisterRentals()
    {
        if (!stateKnown || rentalsRegistered || state.rentals.Count == 0) return;
        OfficeCapacity.RegisterExtra(RentalCapacityId, () => RentedSeats);
        rentalsRegistered = true;
    }

    void UnregisterRentals()
    {
        if (!rentalsRegistered) return;
        rentalsRegistered = false;
        OfficeCapacity.UnregisterExtra(RentalCapacityId);
    }

    /// <summary>The rentals changed: (un)register the extra seats and tell OfficeCapacity's readers.</summary>
    internal void RentalsChanged()
    {
        if (state.rentals.Count == 0) UnregisterRentals();
        else if (!rentalsRegistered) RegisterRentals();
        else OfficeCapacity.NotifyChanged();
    }

    // ------------------------------------------------------------------
    // Phases: the candidate pick, the manager, the gap
    // ------------------------------------------------------------------

    void CheckFeature()
    {
        if (state.phase == ManagerPhase.None && ManagerFeatureOpen()) StartPicking("the Manager feature opened");
    }

    static bool ManagerFeatureOpen()
    {
        if (FeatureGateRunner.Instance != null) return FeatureGate.IsKnown && FeatureGate.IsUnlocked(Feature.Manager);
        return Today >= FeatureGate.ManagerFromDay && !DemoMode.IsLocked(Feature.Manager);   // no tutorial in this scene: by the day alone (Demo: unless the demo locks it)
    }

    void OnFeatureUnlocked(Feature feature)
    {
        if (feature == Feature.Manager && stateKnown && state.phase == ManagerPhase.None) StartPicking("day " + Today);
    }

    /// <summary>Three candidates wait for the player's pick (the pick opens itself at the next calm moment).</summary>
    public void StartPicking(string why)
    {
        state.candidates = ManagerCandidates.Generate(3, CurrentTier());
        state.phase = ManagerPhase.Picking;
        state.pickShown = false;
        Log("Candidates for the manager's job (" + why + "): " + DescribeCandidates() + ".");
        RaiseChanged();
    }

    string DescribeCandidates()
    {
        var parts = new List<string>();
        foreach (ManagerPerson c in state.candidates) parts.Add(c.name + " (" + c.personality + ", " + UIFormat.Money(c.salary) + "/yr)");
        return string.Join(", ", parts);
    }

    /// <summary>The player hired candidate <paramref name="index"/>. False if there's no such candidate.</summary>
    public bool PickCandidate(int index)
    {
        if (state.phase != ManagerPhase.Picking || index < 0 || index >= state.candidates.Count) return false;
        ManagerPerson m = state.candidates[index];
        m.hiredDay = Today;
        m.satisfaction = Mathf.Clamp(68f + UnityEngine.Random.Range(-4f, 6f), 0f, 100f);
        state.manager = m;
        state.candidates.Clear();
        state.phase = ManagerPhase.Active;
        state.managersHired++;
        state.lowDays = 0;
        state.lastRollDay = Today;
        GameNotifications.Show(ManagerText.Get("mgr.toast.hired", ("manager", m.name), ("style", ManagerText.PersonalityName(m.personality))), NotificationKind.Good);
        Log("Hired " + m.name + " (" + m.personality + ", " + UIFormat.Money(m.salary) + "/yr).");
        RaiseChanged();
        TryCreate("manager");   // their first message
        return true;
    }

    /// <summary>Set the manager's satisfaction (debug).</summary>
    public void SetSatisfaction(float value)
    {
        if (!HasManager) return;
        state.manager.satisfaction = Mathf.Clamp(value, 0f, 100f);
        Dirty();
        RaiseChanged();
        Log(state.manager.name + "'s satisfaction set to " + value.ToString("0") + ".");
    }

    /// <summary>Take a pending decision off the desk without any effect (debug).</summary>
    public void DebugDiscard(int uid)
    {
        if (state.pending.RemoveAll(p => p.uid == uid) > 0) RaiseChanged();
    }

    /// <summary>Let a pending decision expire now, with its default (debug).</summary>
    public void ExpireNow(int uid)
    {
        ManagerPending p = FindPending(uid);
        if (p != null) Expire(p, "debug");
    }

    /// <summary>The manager leaves: the job stays open for a few days, then new candidates come.</summary>
    public void Resign(string why)
    {
        if (!HasManager) return;
        ManagerPerson m = state.manager;
        ManagerEngineSettings e = Engine;
        int gap = UnityEngine.Random.Range(e.gapDaysMin, e.gapDaysMax + 1);
        state.phase = ManagerPhase.Gap;
        state.gapUntilDay = Today + Math.Max(0, gap);
        state.manager = new ManagerPerson();
        state.stats.resigned++;
        // Their personal matters leave with them; company decisions stay on the desk
        state.pending.RemoveAll(p => library.Get(p.situation) != null && library.Get(p.situation).Personal);
        state.followups.RemoveAll(f => library.Get(f.situation) != null && library.Get(f.situation).Personal);
        GameNotifications.Show(ManagerText.Get("mgr.toast.resigned", ("manager", m.name), ("days", Math.Max(1, gap))), NotificationKind.Bad);
        Log(m.name + " resigned (" + why + "); new candidates on day " + state.gapUntilDay + ".");
        RaiseChanged();
    }

    // ------------------------------------------------------------------
    // Views
    // ------------------------------------------------------------------

    /// <summary>
    /// Open the desk (the candidate pick while candidates wait). <paramref name="focusUid"/> selects a decision;
    /// <paramref name="interrupted"/>: an urgent decision opened it by itself (the desk says the manager knocked).
    /// </summary>
    public void OpenDesk(int focusUid = 0, bool interrupted = false)
    {
        if (!stateKnown) return;
        if (state.phase == ManagerPhase.Picking)
        {
            OpenPick();
            return;
        }
        DropOrphans();
        if (deskView == null) deskView = GetOrAddView<ManagerDeskView>("ManagerDesk");
        if (deskView != null) deskView.Open(focusUid, interrupted);
    }

    public void OpenPick()
    {
        if (state.phase != ManagerPhase.Picking) return;
        if (pickView == null) pickView = GetOrAddView<ManagerPickView>("ManagerPick");
        if (pickView != null)
        {
            state.pickShown = true;
            pickView.Open();
        }
    }

    public bool IsDeskOpen => deskView != null && deskView.IsOpen;
    /// <summary>A decision asked for build mode; it opens once the desk has closed.</summary>
    public bool BuildModePending => buildModeRequested;
    public bool IsPickOpen => pickView != null && pickView.IsOpen;

    void OnLanguageChanged()
    {
        RaiseChanged();
    }

    // ------------------------------------------------------------------
    // Calm moments: interruptions, the candidate pick, build mode
    // ------------------------------------------------------------------

    float calmTime;
    float lastInterruptTime = -10000f;
    string buildModeSpot;
    bool buildModeRequested;

    /// <summary>Nothing else is going on (like the tutorial's memos): no panel, crisis, card, build mode, bet, run end, click.</summary>
    public static bool IsCalm()
    {
        if (!RunEndDirector.IsScreenCalm()) return false;
        RunEndDirector director = RunEndDirector.Instance;
        if (director != null)
        {
            if (director.IsRunOver || director.IsAnyScreenOpen) return false;
            CompanyManager company = CompanyManager.Instance;
            if (company != null && company.companyValue <= director.bankruptcyThreshold) return false;
        }
        if (BuildModeCamera.Instance != null && BuildModeCamera.Instance.IsInBuildMode()) return false;
        if (TutorialDirector.Instance != null && TutorialDirector.Instance.IsShowing) return false;
        if (Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButtonUp(0) || Input.GetMouseButtonUp(1)) return false;
        return true;
    }

    void UpdateCalm()
    {
        calmTime = IsCalm() ? calmTime + Time.unscaledDeltaTime : 0f;
    }

    bool CalmEnough => calmTime >= Engine.calmSeconds;

    void TryInterrupt()
    {
        if (!HasManager || !CalmEnough) return;
        if (Time.realtimeSinceStartup - lastInterruptTime < Engine.interruptGapSeconds) return;
        foreach (ManagerPending p in state.pending)
        {
            if (!p.interrupt || p.interruptDone) continue;
            p.interruptDone = true;
            lastInterruptTime = Time.realtimeSinceStartup;
            state.stats.interrupts++;
            calmTime = 0f;
            Log("Interrupting for " + p.situation + ".");
            OpenDesk(p.uid, true);
            return;
        }
    }

    void TryAutoOpenPick()
    {
        if (state.phase != ManagerPhase.Picking || state.pickShown || !CalmEnough) return;
        calmTime = 0f;
        OpenPick();
    }

    /// <summary>A decision asked for build mode: it opens once the desk has closed and nothing else is in the way.</summary>
    internal void RequestBuildMode(string spot)
    {
        buildModeRequested = true;
        buildModeSpot = spot ?? "";
    }

    void TryOpenRequestedBuildMode()
    {
        if (!buildModeRequested) return;
        if (IsDeskOpen || IsPickOpen) return;
        if (UIManager.Exists && UIManager.Instance.IsAnyPanelOpen()) return;
        buildModeRequested = false;
        BuildModeCamera camera = BuildModeCamera.Instance;
        if (camera == null)
        {
            GameNotifications.Show(ManagerText.Get("mgr.toast.no_build_mode"), NotificationKind.Info);
            return;
        }
        if (camera.TryEnterBuildMode()) ManagerBuildPerks.RaiseBuildModeRequested(buildModeSpot);
    }

    // ------------------------------------------------------------------

    internal static void RaiseChanged()
    {
        Action handlers = Changed;
        if (handlers == null) return;
        foreach (Action handler in handlers.GetInvocationList())
        {
            try { handler(); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }

    static void RaiseDecisionAdded(ManagerPending pending)
    {
        Action<ManagerPending> handlers = DecisionAdded;
        if (handlers == null) return;
        foreach (Action<ManagerPending> handler in handlers.GetInvocationList())
        {
            try { handler(pending); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }

    internal void Log(string message)
    {
        if (logChanges) Debug.Log("[Manager] " + message);
    }
}
