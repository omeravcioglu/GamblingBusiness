using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The run's side of FeatureGate: which features are open, which HUD tabs show NEW, which Board memos are still owed.
/// - Created automatically in every scene with a CompanyManager (with a TutorialDirector on the same object), and goes
///   away with that scene. Nothing to add to MainGame.
/// - A new company (SaveSystem finishes its load without handing over any data) starts with everything gated. A save
///   with "unlocks" carries on where it was. A save without it (a company from before the tutorial) gets every feature
///   its progress already qualifies for, silently: no memos, no badges and never the tour.
/// - Checks the milestones (FeatureGate's constants) on hires, new days, gambles and skill points, and four times a second
///   (company value, the Casino License, the City unlock).
/// - Saved under "unlocks" (ISaveParticipant): {version, unlocked, fresh, memosOwed, hires, gambled}, features by their
///   enum names. Unknown names and missing fields are skipped; a broken entry counts as an older save.
/// - Demo: full-game features never open, the Board grants the Casino License on a hire or day milestone, the casino memo
///   goes first and covers heat, and the staff files open with a toast instead of a memo.
/// </summary>
[DisallowMultipleComponent]
public class FeatureGateRunner : MonoBehaviour, ISaveParticipant
{
    public const string Key = "unlocks";

    /// <summary>Real seconds to wait for the save before treating the company as an older save (never gate a run blindly).</summary>
    const float UnknownStateTimeout = 8f;

    public static FeatureGateRunner Instance { get; private set; }

    [Tooltip("Real seconds between progress checks")]
    [SerializeField] float checkInterval = 0.25f;
    [Tooltip("Write unlocks to the Console")]
    [SerializeField] bool logChanges = true;

    readonly HashSet<Feature> unlocked = new HashSet<Feature>();
    readonly HashSet<Feature> fresh = new HashSet<Feature>();       // NEW badge until the screen is opened
    readonly HashSet<Feature> memosOwed = new HashSet<Feature>();   // opened during play, memo not shown yet
    readonly List<Feature> scratch = new List<Feature>();
    int hires;
    bool gambled;

    bool stateKnown;
    bool restoredThisLoad;
    bool newRun;
    float checkTimer;
    float unknownTime;
    CompanyManager hookedCompany;
    SkillTreeManager hookedSkills;
    SkillTreeUI oldSkillPanel;
    bool oldSkillPanelSearched;
    readonly HashSet<Feature> warnedChecks = new HashSet<Feature>();

    [Serializable]
    class SavedUnlocks
    {
        public int version = 1;
        public List<string> unlocked = new List<string>();
        public List<string> fresh = new List<string>();
        public List<string> memosOwed = new List<string>();
        public int hires;
        public bool gambled;
    }

    public string SaveKey => Key;

    /// <summary>The run is loaded (a new company or a save), so the unlocks are known.</summary>
    public bool IsStateKnown => stateKnown;

    /// <summary>A new company started in this scene (not a loaded save): the first-day tour may show.</summary>
    public bool IsNewRun => stateKnown && newRun;

    /// <summary>Hires counted this run (for the risk-card milestone).</summary>
    public int Hires => hires;

    // ------------------------------------------------------------------
    // Creation
    // ------------------------------------------------------------------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        // Entering Play mode without a domain reload keeps statics: start every session clean
        Instance = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Ensure(); // the first scene is loaded already
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Ensure();
    }

    /// <summary>The scene's runner (with its TutorialDirector), created if missing. Null in a scene without a CompanyManager.</summary>
    public static FeatureGateRunner Ensure()
    {
        if (Instance != null) return Instance;
        if (!Application.isPlaying || CompanyManager.Instance == null) return null;

        FeatureGateRunner existing = FindAnyObjectByType<FeatureGateRunner>();
        if (existing != null) return existing;
        var go = new GameObject("Tutorial");
        FeatureGateRunner runner = go.AddComponent<FeatureGateRunner>();
        go.AddComponent<TutorialDirector>();
        return runner;
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[Tutorial] Removed a second FeatureGateRunner on '" + name + "'.");
            Destroy(this);
            return;
        }
        Instance = this;
        Hook();
    }

    void OnEnable()
    {
        GambleLedger.Gambled -= OnGambled;
        GambleLedger.Gambled += OnGambled;
    }

    void OnDisable()
    {
        GambleLedger.Gambled -= OnGambled;
    }

    void OnDestroy()
    {
        Unhook();
        if (Instance == this) Instance = null;
    }

    void Hook()
    {
        CompanyManager company = CompanyManager.Instance;
        if (company != hookedCompany)
        {
            if (hookedCompany != null)
            {
                hookedCompany.EmployeeHired -= OnHired;
                hookedCompany.DayEnded -= OnDayEnded;
                hookedCompany.StateLoaded -= OnStateLoaded;
            }
            hookedCompany = company;
            if (company != null)
            {
                company.EmployeeHired += OnHired;
                company.DayEnded += OnDayEnded;
                company.StateLoaded += OnStateLoaded;
            }
        }

        SkillTreeManager skills = SkillTreeManager.Instance;
        if (skills != hookedSkills)
        {
            if (hookedSkills != null) hookedSkills.SkillPointsChanged -= OnSkillPoints;
            hookedSkills = skills;
            if (skills != null) skills.SkillPointsChanged += OnSkillPoints;
        }
    }

    void Unhook()
    {
        if (hookedCompany != null)
        {
            hookedCompany.EmployeeHired -= OnHired;
            hookedCompany.DayEnded -= OnDayEnded;
            hookedCompany.StateLoaded -= OnStateLoaded;
        }
        hookedCompany = null;
        if (hookedSkills != null) hookedSkills.SkillPointsChanged -= OnSkillPoints;
        hookedSkills = null;
    }

    // ------------------------------------------------------------------
    // Loading and saving
    // ------------------------------------------------------------------

    void Update()
    {
        Hook();
        if (hookedCompany == null) return;

        if (!stateKnown)
        {
            // A new company (or no save): SaveSystem finishes without raising StateLoaded
            SaveSystem save = SaveSystem.Instance;
            if (save == null || (save.IsGameLoaded && !save.IsLoading))
            {
                BeginRun(true);
                Log("A new company: the systems past the hiring desk open as it grows.");
                Evaluate(false);
                return;
            }
            unknownTime += Time.unscaledDeltaTime;
            if (unknownTime >= UnknownStateTimeout)
            {
                // The save never finished loading: open what the company has reached rather than gate it blindly
                BeginRun(false);
                Evaluate(true);
                Debug.LogWarning("[Tutorial] The saved game didn't finish loading; treating the company like an older save (opened: " + Describe(unlocked) + ").");
            }
            return;
        }

        checkTimer -= Time.unscaledDeltaTime;
        if (checkTimer > 0f) return;
        checkTimer = Mathf.Max(0.05f, checkInterval);
        Evaluate(false);
        NoticeOpenedScreens();
        // Demo: build mode was opened before its memo could show (the memo waits for a calm desk): it's moot now
        if (DemoMode.Active && unlocked.Contains(Feature.Build) && !fresh.Contains(Feature.Build) && memosOwed.Contains(Feature.Build))
            MemoShown(Feature.Build);
    }

    void BeginRun(bool isNew)
    {
        ClearState();
        stateKnown = true;
        newRun = isNew;
        restoredThisLoad = false;
        unknownTime = 0f;
        checkTimer = 0f;
    }

    void ClearState()
    {
        unlocked.Clear();
        fresh.Clear();
        memosOwed.Clear();
        hires = 0;
        gambled = false;
    }

    // SaveSystem raises this after it applied a save (after RestoreState, if the save had "unlocks")
    void OnStateLoaded()
    {
        bool fromSave = restoredThisLoad;
        restoredThisLoad = false;
        stateKnown = true;
        newRun = false;
        unknownTime = 0f;
        checkTimer = 0f;

        if (fromSave)
        {
            Log("Carrying on a saved run (open: " + Describe(unlocked) + ").");
            Evaluate(false);
            return;
        }

        // A company from before the tutorial: what its progress reached opens silently (no memos, badges or tour)
        ClearState();
        Evaluate(true);
        Log("A company from before the tutorial: opened what it has reached (" + Describe(unlocked) + "), without memos.");
    }

    public string CaptureState()
    {
        var data = new SavedUnlocks { hires = hires, gambled = gambled };
        foreach (Feature feature in FeatureGate.All)
        {
            if (unlocked.Contains(feature)) data.unlocked.Add(feature.ToString());
            if (fresh.Contains(feature)) data.fresh.Add(feature.ToString());
            if (memosOwed.Contains(feature)) data.memosOwed.Add(feature.ToString());
        }
        return JsonUtility.ToJson(data);
    }

    public void RestoreState(string json)
    {
        SavedUnlocks data = null;
        if (!string.IsNullOrEmpty(json))
        {
            try
            {
                data = JsonUtility.FromJson<SavedUnlocks>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Tutorial] Couldn't read the saved unlocks (" + e.Message + "): the company's progress decides instead.");
            }
        }
        if (data == null) return; // OnStateLoaded treats it like an older save

        ClearState();
        ReadFeatures(data.unlocked, unlocked);
        ReadFeatures(data.fresh, fresh);
        ReadFeatures(data.memosOwed, memosOwed);
        fresh.IntersectWith(unlocked);
        memosOwed.IntersectWith(unlocked);
        hires = Mathf.Max(0, data.hires);
        gambled = data.gambled;
        restoredThisLoad = true;
    }

    static void ReadFeatures(List<string> names, HashSet<Feature> into)
    {
        if (names == null) return;
        foreach (string name in names)
        {
            if (!string.IsNullOrEmpty(name) && Enum.TryParse(name.Trim(), false, out Feature feature) && Enum.IsDefined(typeof(Feature), feature))
                into.Add(feature);
        }
    }

    // ------------------------------------------------------------------
    // Milestones
    // ------------------------------------------------------------------

    void OnHired(ApplicantData applicant)
    {
        hires++;
        if (stateKnown) Evaluate(false);
    }

    void OnDayEnded(int day, double change)
    {
        if (stateKnown) Evaluate(false);
    }

    void OnSkillPoints(int points)
    {
        if (stateKnown) Evaluate(false);
    }

    void OnGambled(GambleRecord record)
    {
        if (record.stake <= 0.0) return;
        gambled = true;
        if (stateKnown) Evaluate(false);
    }

    /// <summary>Open every feature whose milestone is reached. <paramref name="olderSave"/>: silently, and past progress counts too.</summary>
    void Evaluate(bool olderSave)
    {
        CompanyManager company = hookedCompany != null ? hookedCompany : CompanyManager.Instance;
        if (company == null || !stateKnown) return;

        if (DemoMode.Active && !olderSave) DemoGrantCasino(company);   // Demo: the Board's free Casino License

        foreach (Feature feature in FeatureGate.All)
        {
            if (unlocked.Contains(feature)) continue;
            bool reached;
            try
            {
                reached = Reached(feature, company, olderSave);
            }
            catch (Exception e)
            {
                reached = false;
                if (warnedChecks.Add(feature))
                    Debug.LogWarning("[Tutorial] Checking " + feature + " failed: " + e.Message);
            }
            if (reached) Unlock(feature, olderSave);
        }
    }

    // Demo: the license arrives on the casinoAfterHires-th hire or on casinoFromDay, whichever comes first. The next pass
    // then opens Casino loudly through IsCasinoAvailable (memo and NEW badge), like a bought license.
    void DemoGrantCasino(CompanyManager company)
    {
        SkillTreeManager skills = hookedSkills != null ? hookedSkills : SkillTreeManager.Instance;
        if (skills == null || skills.HasEffect(SkillEffect.UnlockCasino)) return;
        DemoConfig config = DemoMode.Config;
        if (HireCount(company, RunStats.Instance) < config.casinoAfterHires && company.currentDay < config.casinoFromDay) return;
        try { skills.DemoGrantCasino(); }
        catch (Exception e)
        {
            if (warnedChecks.Add(Feature.Casino)) Debug.LogWarning("[Tutorial] The demo's Casino License grant failed: " + e.Message);
        }
    }

    bool Reached(Feature feature, CompanyManager company, bool olderSave)
    {
        if (DemoMode.IsLocked(feature)) return false;   // Demo: kept for the full game
        RunStats stats = RunStats.Instance;
        int day = company.currentDay;
        switch (feature)
        {
            case Feature.RiskCards:
                return HireCount(company, stats) >= FeatureGate.RiskCardsAfterHires ||
                       (olderSave && stats != null && stats.RiskCardsPlayed > 0);
            case Feature.Crises:
                return day >= FeatureGate.CrisesFromDay || (olderSave && stats != null && stats.CrisesTotal > 0);
            case Feature.Skills:
                return HasSkillProgress();
            case Feature.Staff:
                return EmployeeCount(company) >= FeatureGate.StaffAtEmployees || HireCount(company, stats) >= FeatureGate.StaffAtEmployees;
            case Feature.Build:
                // ...or the first time every office place is taken (2026-09-29): nobody is stuck at a full desk before its day
                return day >= FeatureGate.BuildFromDay || (olderSave && HasBuildBudget()) || OfficeFull(company);
            case Feature.BoardMeetings:
                return day >= FeatureGate.BoardMeetingsFromDay ||
                       (olderSave && BoardMeetings.Instance != null && BoardMeetings.Instance.MeetingsHeld > 0);
            case Feature.Heat:
                return gambled || HeatLevel() >= HeatMeter.LevelWarm || (olderSave && GambledBefore(stats));
            case Feature.Stocks:
                return company.companyValue >= FeatureGate.StocksAtValue || (stats != null && stats.PeakValue >= FeatureGate.StocksAtValue) ||
                       (olderSave && StockMarketManager.Instance != null && StockMarketManager.Instance.GetTotalOwnedStocks() > 0);
            case Feature.Casino:
                return CasinoManager.Instance != null
                    ? CasinoManager.Instance.IsCasinoAvailable()
                    : SkillTreeManager.Instance != null && SkillTreeManager.Instance.IsCasinoUnlocked();
            case Feature.Online:
                return company.companyValue >= OfficeHud.OnlineUnlockValue || (CityOnline.Instance != null && CityOnline.Instance.IsUnlocked);
            case Feature.Manager:
                return day >= FeatureGate.ManagerFromDay;   // ManagerSystem offers the candidates when it opens
        }
        return false;
    }

    int HireCount(CompanyManager company, RunStats stats)
    {
        int count = Mathf.Max(hires, EmployeeCount(company));
        if (stats != null) count = Mathf.Max(count, stats.Hires);
        return count;
    }

    static int EmployeeCount(CompanyManager company)
    {
        return Mathf.Max(company.totalEmployees, company.employees != null ? company.employees.Count : 0);
    }

    static bool HasSkillProgress()
    {
        SkillTreeManager skills = SkillTreeManager.Instance;
        if (skills == null) return false;
        if (skills.skillPoints > 0 || skills.HighIQPointsAwarded > 0) return true;
        if (skills.allSkills == null) return false;
        foreach (Skill skill in skills.allSkills)
        {
            if (skill != null && skill.unlocked) return true;
        }
        return false;
    }

    /// <summary>Every office place is taken (OfficeCapacity) and someone works there.</summary>
    static bool OfficeFull(CompanyManager company)
    {
        return OfficeCapacity.IsLimited && EmployeeCount(company) > 0 && OfficeCapacity.IsFull;
    }

    static bool HasBuildBudget()
    {
        // Demo: every demo plan starts with the Board's construction grant, which isn't gambling winnings
        double floor = DemoMode.Active ? DemoMode.Config.startingBuildBudget : 0.0;
        return TowerBuilder.Instance != null && TowerBuilder.Instance.Budget > floor + 0.5;
    }

    static int HeatLevel()
    {
        return HeatMeter.Instance != null ? HeatMeter.Instance.Level : HeatMeter.LevelCool;
    }

    static bool GambledBefore(RunStats stats)
    {
        if (stats != null && stats.HasGambled) return true;
        HeatMeter meter = HeatMeter.Instance;
        if (meter != null && (meter.Heat > 0f || meter.AuditCount > 0)) return true;
        return HasBuildBudget();
    }

    // ------------------------------------------------------------------
    // State for FeatureGate and the director
    // ------------------------------------------------------------------

    /// <summary>Open in this run (also true until the run is loaded, so nothing is blocked while a scene starts).</summary>
    public bool IsUnlocked(Feature feature)
    {
        return !stateKnown || unlocked.Contains(feature);
    }

    /// <summary>
    /// Open a feature. Loudly (<paramref name="silent"/> false): its tab gets a NEW badge and, if this profile hasn't seen
    /// it, the Board's memo is owed; FeatureGate.Unlocked is raised.
    /// </summary>
    public void Unlock(Feature feature, bool silent)
    {
        if (DemoMode.IsLocked(feature)) return;   // Demo: not even Unlock Everything opens a full-game feature
        if (!unlocked.Add(feature)) return;
        if (!silent)
        {
            if (FeatureGate.HasTab(feature)) fresh.Add(feature);
            if (FeatureGate.HasMemo(feature) && !FeatureGate.HasSeenMemo(feature)) memosOwed.Add(feature);
        }
        CompanyManager company = CompanyManager.Instance;
        Log(feature + " unlocked" + (silent ? " (silently)" : "") + (company != null ? " on day " + company.currentDay : "") + ".");
        if (!silent && DemoMode.Active && feature == Feature.Staff)
            GameNotifications.Show(DemoMode.T("demo.toast.staff"), NotificationKind.Info);   // Demo: a toast instead of the memo
        if (!silent) FeatureGate.RaiseUnlocked(feature);
    }

    /// <summary>Open every feature without memos or badges (debug).</summary>
    public void UnlockAll()
    {
        if (!stateKnown)
        {
            Debug.LogWarning("[Tutorial] The game is still loading - try again in a moment.");
            return;
        }
        foreach (Feature feature in FeatureGate.All)
            Unlock(feature, true);
    }

    public bool IsNew(Feature feature)
    {
        return stateKnown && fresh.Contains(feature) && unlocked.Contains(feature);
    }

    public void MarkOpened(Feature feature)
    {
        fresh.Remove(feature);
    }

    /// <summary>The feature's memo is owed in this run and this profile hasn't seen it yet.</summary>
    public bool IsMemoOwed(Feature feature)
    {
        if (!memosOwed.Contains(feature)) return false;
        if (!FeatureGate.HasSeenMemo(feature)) return true;
        memosOwed.Remove(feature);   // shown in another run meanwhile
        return false;
    }

    /// <summary>
    /// The first owed memo in schedule order, or null. Memos that wait for their system's first appearance (risk cards,
    /// crises) are left out unless <paramref name="includeFirstUse"/>.
    /// </summary>
    public Feature? NextOwedMemo(bool includeFirstUse)
    {
        if (memosOwed.Count == 0) return null;
        if (DemoMode.Active)
        {
            // Demo: the casino memo goes first, and it also covers heat (TutorialDirector marks Heat seen with it)
            if (IsMemoOwed(Feature.Casino)) return Feature.Casino;
        }
        foreach (Feature feature in FeatureGate.All)
        {
            if (DemoMode.Active && feature == Feature.Heat) continue;   // Demo: never on its own
            if (!IsMemoOwed(feature)) continue;
            if (!includeFirstUse && FeatureGate.MemoWaitsForFirstUse(feature)) continue;
            return feature;
        }
        return null;
    }

    /// <summary>The memo came up: it isn't owed any more, and this profile has seen it.</summary>
    public void MemoShown(Feature feature)
    {
        memosOwed.Remove(feature);
        FeatureGate.MarkMemoSeen(feature);
    }

    /// <summary>Give up on a memo (it couldn't come up): the system goes ahead without it.</summary>
    public void DropMemo(Feature feature)
    {
        memosOwed.Remove(feature);
    }

    /// <summary>A feature's screen is open: its NEW badge goes (however it was opened).</summary>
    void NoticeOpenedScreens()
    {
        if (fresh.Count == 0) return;
        scratch.Clear();
        scratch.AddRange(fresh);
        foreach (Feature feature in scratch)
        {
            bool open;
            try { open = IsScreenOpen(feature); }
            catch (Exception) { open = false; }
            if (open) fresh.Remove(feature);
        }
    }

    bool IsScreenOpen(Feature feature)
    {
        switch (feature)
        {
            case Feature.Skills:
                if (SkillBoardView.Instance != null && SkillBoardView.Instance.IsOpen) return true;
                if (!oldSkillPanelSearched)
                {
                    oldSkillPanelSearched = true;
                    oldSkillPanel = FindAnyObjectByType<SkillTreeUI>(FindObjectsInactive.Include);
                }
                return oldSkillPanel != null && oldSkillPanel.IsOpen;
            case Feature.Staff:
                return StaffFilesView.Instance != null && StaffFilesView.Instance.IsOpen;
            case Feature.Stocks:
                return StockMarketUI.Instance != null && StockMarketUI.Instance.IsOpen;
            case Feature.Build:
                return (BuildModeCamera.Instance != null && BuildModeCamera.Instance.IsInBuildMode()) || TowerBuilder.IsBuildModeOpen;
            case Feature.Casino:
                return CasinoManager.Instance != null && CasinoManager.Instance.IsOpen;
        }
        return false;
    }

    // ------------------------------------------------------------------

    void Log(string message)
    {
        if (logChanges) Debug.Log("[Tutorial] " + message);
    }

    static string Describe(HashSet<Feature> set)
    {
        if (set.Count == 0) return "nothing yet";
        var names = new List<string>();
        foreach (Feature feature in FeatureGate.All)
            if (set.Contains(feature)) names.Add(feature.ToString());
        return string.Join(", ", names);
    }
}
