using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The office HUD from the design canvas ("Office HUD: reviewing CVs"), built by
/// Tools > Game > New UI > Build Office HUD into Assets/UI/Prefabs/OfficeHUD.prefab.
/// - Company ledger: name, value, today's change, day + day progress, staff, happiness, investor trust
/// - Sticky-note toolbar: Skills (skill-point badge), Staff, Stocks, Casino, Build (Q), Online ($1B)
/// - Pause button, applicant counter and golden-applicant stickies, Skip / Hire / Next batch
/// - Value pop and a toast when someone is hired
/// The hiring-task clipboard (HudTaskBoard) and toasts (HudToasts) are their own components.
/// GameFlowManager keeps driving the Skip / Hire / Next batch buttons (interactable, Next batch visibility).
/// Texts: the builder binds the fixed labels to the Hud String Table; this writes the changing ones (ledger lines, applicant
/// counter, skips left, toasts, the Casino / Online tab hints) in the current language, again when it changes.
/// Tutorial (small edit, owner-approved 2026-09-28): the Skills, Staff, Stocks and Build tabs stay locked until their
/// FeatureGate milestones and their Open methods refuse meanwhile; FeatureTabs adds the locked looks and NEW badges.
/// </summary>
[DisallowMultipleComponent]
public class OfficeHud : MonoBehaviour
{
    /// <summary>Raised by the Online tab. The online city UI subscribes and opens itself.</summary>
    public static event System.Action OnlineCityRequested;
    /// <summary>Raised by the Staff tab. The staff roster subscribes and opens itself.</summary>
    public static event System.Action StaffRequested;
    /// <summary>Raised by the Build tab. The new 2D building system subscribes and opens itself;
    /// without a subscriber the old 3D build mode (BuildModeCamera) toggles.</summary>
    public static event System.Action BuildRequested;

    public const double OnlineUnlockValue = 1000000000d;
    const string OnlineSeenKey = "HudOnlineTabSeen";
    const string HasSetNameKey = "HasSetCompanyName";
    const string T = "Hud";   // Assets/Localization/Source/Hud.csv

    [Header("Layers")]
    [Tooltip("Everything that belongs to the desk view; hidden in build mode")]
    public GameObject deskLayer;

    [Header("Ledger")]
    public TMP_Text companyNameText;
    public TMP_Text valueText;
    public TMP_Text changeText;
    public GameObject changeUpIcon;
    public GameObject changeDownIcon;
    public TMP_Text dayText;
    public RectTransform dayFill;
    public TMP_Text staffText;
    public RectTransform happinessFill;
    public TMP_Text happinessText;
    public RectTransform trustFill;
    public TMP_Text trustText;
    [Tooltip("Value font size by length: up to 10 characters, up to 14, longer")]
    public Vector3 valueSizes = new Vector3(54f, 42f, 38f);

    [Header("Toolbar")]
    public HudTab skillsTab;
    public HudTab staffTab;
    public HudTab stocksTab;
    public HudTab casinoTab;
    public HudTab buildTab;
    public HudTab onlineTab;
    public Button pauseButton;

    [Header("Desk")]
    public GameObject applicantSticky;
    public GameObject applicantReview;
    public GameObject applicantDone;
    public TMP_Text applicantCountText;
    public Image[] skipDots;
    public Image[] skipRings;
    public GameObject goldenSticky;
    public GameObject reviewBar;
    public TMP_Text skipsLeftText;
    public Button skipButton;
    public Button hireButton;
    public Button nextBatchButton;

    [Header("Value pop")]
    public RectTransform valuePop;
    public TMP_Text valuePopText;
    public CanvasGroup valuePopGroup;
    public float valuePopSeconds = 1.6f;
    public float valuePopRise = 44f;

    GameFlowManager flow;
    CompanyManager subscribedCompany;
    Vector2 valuePopHome;
    float valuePopAge = -1f;

    // change detection (so we only touch TMP, and the String Table, when something changed)
    string shownName, shownValue, shownChangeMoney, shownHappy, shownTrust;
    int shownChangeSign = 99, shownDay = -1, shownStaff = -1;
    int languageVersion, ledgerVersion = -1;   // a new language writes the ledger's texts again
    int lastEmployeeCount = -1;
    double lastCompanyValue;
    double taskMoneySinceLastCheck;
    HiringTaskManager subscribedTasks;
    float refreshTimer;

    void Awake()
    {
        L10nFont.AttachAll(this);   // every text of the HUD in the language's font (English keeps the design font)
        if (valuePop != null)
        {
            valuePopHome = valuePop.anchoredPosition;
            valuePop.gameObject.SetActive(false);
        }
    }

    void OnEnable()
    {
        L10n.LanguageChanged += OnLanguageChanged;
    }

    void OnDisable()
    {
        L10n.LanguageChanged -= OnLanguageChanged;
    }

    void OnLanguageChanged()
    {
        languageVersion++;
        WriteTabHints();
        RefreshAll();
    }

    void Start()
    {
        flow = FindAnyObjectByType<GameFlowManager>();
        if (flow != null)
        {
            flow.DeskStateChanged += RefreshDesk;

            // GameFlowManager drives these buttons (interactable, Next batch visibility); the builder assigns
            // them in the scene, and this keeps it working if a rebuilt prefab broke those references
            if (skipButton != null && flow.skipButton != skipButton) flow.skipButton = skipButton;
            if (hireButton != null && flow.hireButton != hireButton) flow.hireButton = hireButton;
            if (nextBatchButton != null && flow.nextBatchButton != nextBatchButton) flow.nextBatchButton = nextBatchButton;

            if (skipButton != null) skipButton.onClick.AddListener(flow.SkipApplicant);
            if (hireButton != null) hireButton.onClick.AddListener(flow.HireCurrent);
            if (nextBatchButton != null) nextBatchButton.onClick.AddListener(flow.NextBatch);
        }

        Hook(skillsTab, OpenSkills);
        Hook(staffTab, OpenStaff);
        Hook(stocksTab, OpenStocks);
        Hook(casinoTab, OpenCasino);
        Hook(buildTab, ToggleBuildMode);
        Hook(onlineTab, OpenOnline);
        if (pauseButton != null)
            pauseButton.onClick.AddListener(TogglePause);

        WriteTabHints();
        RefreshAll();
    }

    void OnDestroy()
    {
        if (flow != null)
            flow.DeskStateChanged -= RefreshDesk;
        if (subscribedCompany != null)
            subscribedCompany.StateLoaded -= OnStateLoaded;
        if (subscribedTasks != null)
            subscribedTasks.TaskCompleted -= OnTaskCompleted;
    }

    static void Hook(HudTab tab, UnityEngine.Events.UnityAction action)
    {
        if (tab != null && tab.button != null)
            tab.button.onClick.AddListener(action);
    }

    // ------------------------------------------------------------------

    void Update()
    {
        CompanyManager cm = CompanyManager.Instance;
        if (cm != subscribedCompany)
        {
            if (subscribedCompany != null)
                subscribedCompany.StateLoaded -= OnStateLoaded;
            subscribedCompany = cm;
            if (cm != null)
                cm.StateLoaded += OnStateLoaded;
            lastEmployeeCount = -1;
        }

        if (cm != null)
        {
            RefreshLedger(cm);
            DetectHire(cm);
        }

        refreshTimer -= Time.unscaledDeltaTime;
        if (refreshTimer <= 0f)
        {
            refreshTimer = 0.25f;
            RefreshName();
            RefreshToolbar(cm);
            RefreshDesk();
        }

        AnimateValuePop();
    }

    void RefreshAll()
    {
        CompanyManager cm = CompanyManager.Instance;
        RefreshName();
        if (cm != null)
            RefreshLedger(cm);
        RefreshToolbar(cm);
        RefreshDesk();
    }

    void OnStateLoaded()
    {
        lastEmployeeCount = -1;   // a loaded save is not a hire
    }

    // ------------------------------------------------------------------
    // Ledger

    // Before the player names the company the ledger says "Your company" in pencil (a stored default name shows translated)
    void RefreshName()
    {
        bool named = PlayerPrefs.GetInt(HasSetNameKey, 0) == 1;
        string name = named ? CompanyNameUI.DisplayName(CompanyNameUI.CurrentCompanyName) : L10n.Get(T, "hud.ledger.name.unnamed");
        if (name != shownName && companyNameText != null)
        {
            shownName = name;
            companyNameText.text = name;
            companyNameText.color = named ? UIColors.Ink : UIColors.Pencil;
        }
    }

    void RefreshLedger(CompanyManager cm)
    {
        bool relabel = ledgerVersion != languageVersion;
        ledgerVersion = languageVersion;

        string value = UIFormat.Money(cm.displayedValue);
        if (value != shownValue && valueText != null)
        {
            shownValue = value;
            valueText.text = value;
            valueText.fontSize = value.Length <= 10 ? valueSizes.x : (value.Length <= 14 ? valueSizes.y : valueSizes.z);
            valueText.color = cm.companyValue <= 0 ? UIColors.Loss : UIColors.Ink;
        }

        double change = cm.companyValue - cm.ValueAtDayStart;
        int sign = System.Math.Abs(change) < 0.5 ? 0 : (change > 0 ? 1 : -1);
        string changeMoney = sign == 0 ? UIFormat.Money(0) : UIFormat.SignedMoney(change);
        if (changeText != null && (relabel || sign != shownChangeSign || changeMoney != shownChangeMoney))
        {
            shownChangeMoney = changeMoney;
            changeText.text = sign == 0 ? L10n.Get(T, "hud.ledger.change.none", ("amount", changeMoney))
                                        : L10n.Get(T, "hud.ledger.change.today", ("amount", changeMoney));
        }
        if (sign != shownChangeSign)
        {
            shownChangeSign = sign;
            Color c = sign > 0 ? UIColors.Money : (sign < 0 ? UIColors.Loss : UIColors.Pencil);
            if (changeText != null) changeText.color = c;
            if (changeUpIcon != null)
            {
                changeUpIcon.SetActive(sign > 0);
                var g = changeUpIcon.GetComponent<Graphic>();
                if (g != null) g.color = c;
            }
            if (changeDownIcon != null)
            {
                changeDownIcon.SetActive(sign < 0);
                var g = changeDownIcon.GetComponent<Graphic>();
                if (g != null) g.color = c;
            }
        }

        int day = cm.currentDay;
        if (dayText != null && (relabel || day != shownDay))
        {
            shownDay = day;
            dayText.text = L10n.Get(T, "hud.ledger.day", ("day", day));
        }
        SetFill(dayFill, cm.DayProgress);

        int staff = cm.totalEmployees;
        if (staffText != null && (relabel || staff != shownStaff))
        {
            shownStaff = staff;
            staffText.text = L10n.Get(T, "hud.ledger.staff", ("count", UIFormat.Int(staff)));
        }

        string happy = WholePercent(cm.employeeHappiness);
        if (happinessText != null && (relabel || happy != shownHappy))
        {
            shownHappy = happy;
            happinessText.text = L10n.Get(T, "hud.ledger.percent", ("percent", happy));
        }
        SetFill(happinessFill, cm.employeeHappiness / 100f);

        string trust = WholePercent(cm.investorTrust);
        if (trustText != null && (relabel || trust != shownTrust))
        {
            shownTrust = trust;
            trustText.text = L10n.Get(T, "hud.ledger.percent", ("percent", trust));
        }
        SetFill(trustFill, cm.investorTrust / 100f);
    }

    /// <summary>"72" for a "{percent}%" entry: the entry puts the % sign where its language wants it (%72 in Turkish).</summary>
    static string WholePercent(float value)
    {
        return System.Math.Round(value).ToString("0", CultureInfo.InvariantCulture);
    }

    static void SetFill(RectTransform fill, float fraction)
    {
        if (fill == null) return;
        float f = Mathf.Clamp01(fraction);
        Vector2 max = fill.anchorMax;
        if (Mathf.Abs(max.x - f) < 0.0005f) return;
        max.x = f;
        fill.anchorMax = max;
    }

    // ------------------------------------------------------------------
    // Hires: value pop + toast

    void DetectHire(CompanyManager cm)
    {
        if (HiringTaskManager.Instance != subscribedTasks)
        {
            if (subscribedTasks != null)
                subscribedTasks.TaskCompleted -= OnTaskCompleted;
            subscribedTasks = HiringTaskManager.Instance;
            if (subscribedTasks != null)
                subscribedTasks.TaskCompleted += OnTaskCompleted;
        }

        bool loaded = SaveSystem.Instance == null || SaveSystem.Instance.IsGameLoaded;
        int count = cm.totalEmployees;
        if (loaded && lastEmployeeCount >= 0 && count == lastEmployeeCount + 1)
        {
            // A hire that completes a task also pays the task reward in the same frame: that's the task's toast
            double delta = cm.companyValue - lastCompanyValue - taskMoneySinceLastCheck;
            ApplicantData hired = cm.employees != null && cm.employees.Count > 0 ? cm.employees[cm.employees.Count - 1] : null;
            OnHired(hired, delta);
        }
        taskMoneySinceLastCheck = 0;
        lastEmployeeCount = loaded ? count : -1;
        lastCompanyValue = cm.companyValue;
    }

    void OnTaskCompleted(HiringTask task, bool success, int moneyChange)
    {
        taskMoneySinceLastCheck += moneyChange;
    }

    void OnHired(ApplicantData person, double delta)
    {
        string amount = UIFormat.SignedMoney(delta);
        string message = person != null && !string.IsNullOrEmpty(person.name)
            ? L10n.Get(T, "hud.toast.hired", ("name", person.name), ("amount", amount))
            : L10n.Get(T, "hud.toast.hired.anonymous", ("amount", amount));
        GameNotifications.Show(message, delta >= 0 ? NotificationKind.Good : NotificationKind.Bad);

        if (valuePop != null && valuePopText != null)
        {
            valuePopText.text = amount;
            valuePopText.color = delta >= 0 ? UIColors.Money : UIColors.Loss;
            valuePopAge = 0f;
            valuePop.anchoredPosition = valuePopHome;
            valuePop.gameObject.SetActive(true);
        }
    }

    void AnimateValuePop()
    {
        if (valuePopAge < 0f || valuePop == null) return;
        valuePopAge += Time.unscaledDeltaTime;
        float t = Mathf.Clamp01(valuePopAge / valuePopSeconds);
        float rise = 1f - (1f - t) * (1f - t);
        valuePop.anchoredPosition = valuePopHome + new Vector2(0f, rise * valuePopRise);
        if (valuePopGroup != null)
        {
            float appear = Mathf.Clamp01(valuePopAge / 0.12f);
            float fade = t > 0.7f ? 1f - (t - 0.7f) / 0.3f : 1f;
            valuePopGroup.alpha = appear * fade;
        }
        if (t >= 1f)
        {
            valuePopAge = -1f;
            valuePop.gameObject.SetActive(false);
        }
    }

    // ------------------------------------------------------------------
    // Toolbar

    void RefreshToolbar(CompanyManager cm)
    {
        // Tutorial: tabs of systems the company hasn't reached yet stay locked (FeatureGate). FeatureTabs first gives these
        // tabs the locked looks and NEW badges the prefab lacks, their "opens ..." hints and the NEW badges' state.
        FeatureTabs.Refresh(this);
        SkillTreeManager skills = SkillTreeManager.Instance;
        if (skillsTab != null)
        {
            skillsTab.SetLocked(!FeatureGate.IsUnlocked(Feature.Skills));   // Tutorial (was: never locked)
            skillsTab.SetCount(skills != null ? skills.skillPoints : 0);
        }
        if (staffTab != null)
            staffTab.SetLocked(!FeatureGate.IsUnlocked(Feature.Staff));   // Tutorial (was: never locked)
        if (stocksTab != null)
            stocksTab.SetLocked(!FeatureGate.IsUnlocked(Feature.Stocks));   // Tutorial (was: the Stock Market Access skill, which still gates buying in StockMarketUI)
        if (casinoTab != null)
            casinoTab.SetLocked(skills != null && !skills.IsCasinoUnlocked());
        if (buildTab != null)
            buildTab.SetLocked(!FeatureGate.IsUnlocked(Feature.Build));   // Tutorial (was: never locked)
        if (onlineTab != null)
        {
            // Stays open once the City is unlocked: a broke company may need the money in its City bank
            bool unlocked = (cm != null && cm.companyValue >= OnlineUnlockValue)
                            || (CityOnline.Instance != null && CityOnline.Instance.IsUnlocked);
            bool demoLocked = DemoMode.IsLocked(Feature.Online);   // Demo: the City is in the full game only
            onlineTab.SetLocked(!unlocked || demoLocked);
            onlineTab.SetNew(unlocked && !demoLocked && PlayerPrefs.GetInt(OnlineSeenKey, 0) == 0);
        }
        SetInteractable(skillsTab);
        SetInteractable(staffTab);
        SetInteractable(stocksTab);
        SetInteractable(casinoTab);
        SetInteractable(buildTab);
        SetInteractable(onlineTab);
    }

    /// <summary>The unlock hints of the tabs FeatureGate doesn't explain (FeatureTabs writes the others), in the current language.</summary>
    void WriteTabHints()
    {
        if (stocksTab != null && !FeatureGate.GatesTab(Feature.Stocks))
            stocksTab.lockedHint = L10n.Get(T, "hud.tab.stocks.locked", ("skill", CompanyText.SkillName("growth_3_stocks", "Stock Market Access")));
        if (casinoTab != null && !FeatureGate.GatesTab(Feature.Casino))
            casinoTab.lockedHint = L10n.Get(T, "hud.tab.casino.locked", ("skill", CompanyText.SkillName("chaos_5_casino", "Casino License")));
        if (onlineTab != null && !FeatureGate.GatesTab(Feature.Online))   // Demo: FeatureTabs writes the full-game hint
            onlineTab.lockedHint = L10n.Get(T, "hud.tab.online.locked", ("amount", UIFormat.Money(OnlineUnlockValue)));
    }

    static void SetInteractable(HudTab tab)
    {
        if (tab != null && tab.button != null && tab.button.interactable == tab.IsLocked)
            tab.button.interactable = !tab.IsLocked;
    }

    static bool CanOpenPanels()
    {
        return UIManager.GameplayInputAllowed();
    }

    public void OpenSkills()
    {
        if (!CanOpenPanels()) return;
        if (!FeatureGate.AllowOpen(Feature.Skills)) return;   // Tutorial: locked until the first skill point
        // the designed cork board first, the old panel if it isn't in the scene
        if (SkillBoardView.Instance != null)
        {
            SkillBoardView.Instance.Open();
            return;
        }
        var ui = FindAnyObjectByType<SkillTreeUI>(FindObjectsInactive.Include);
        if (ui != null)
            ui.OpenPanel();
    }

    public void OpenStaff()
    {
        if (!CanOpenPanels()) return;
        if (!FeatureGate.AllowOpen(Feature.Staff)) return;   // Tutorial: locked until the company has a few employees
        if (StaffRequested != null)
            StaffRequested.Invoke();
        else
            GameNotifications.Show(L10n.Get(T, "hud.toast.staff_soon"), NotificationKind.Info);
    }

    public void OpenStocks()
    {
        if (!CanOpenPanels()) return;
        if (!FeatureGate.AllowOpen(Feature.Stocks)) return;   // Tutorial: locked until the company is worth enough
        var ui = StockMarketUI.Instance != null ? StockMarketUI.Instance : FindAnyObjectByType<StockMarketUI>(FindObjectsInactive.Include);
        if (ui != null)
            ui.OpenStockMarket();
    }

    public void OpenCasino()
    {
        if (!CanOpenPanels()) return;
        if (CasinoManager.Instance != null)
            CasinoManager.Instance.OpenCasino();
        else
            GameNotifications.Show(L10n.Get(T, "hud.toast.casino_soon"), NotificationKind.Info);
    }

    public void ToggleBuildMode()
    {
        if (BuildRequested != null)
        {
            if (CanOpenPanels() && FeatureGate.AllowOpen(Feature.Build)) BuildRequested.Invoke();   // Tutorial: locked until its day
            return;
        }
        if (BuildModeCamera.Instance != null)
            BuildModeCamera.Instance.ToggleMode();
    }

    public void OpenOnline()
    {
        if (DemoMode.IsLocked(Feature.Online))
        {
            FeatureGate.ShowLockedToast(Feature.Online);   // Demo: every way in says "full game"
            return;
        }
        if (!CanOpenPanels()) return;
        PlayerPrefs.SetInt(OnlineSeenKey, 1);
        if (onlineTab != null)
            onlineTab.SetNew(false);
        if (OnlineCityRequested != null)
        {
            OnlineCityRequested.Invoke();
            return;
        }
        // Until the new online city hooks in: the old online-world button still knows the way
        System.Type legacy = System.Type.GetType("OnlineWorldButton");
        Object button = legacy != null ? FindAnyObjectByType(legacy, FindObjectsInactive.Include) : null;
        if (button is Component c)
            c.SendMessage("OnButtonClicked", SendMessageOptions.DontRequireReceiver);
        else
            GameNotifications.Show(L10n.Get(T, "hud.toast.online_soon"), NotificationKind.Info);
    }

    public void TogglePause()
    {
        var settings = FindAnyObjectByType<SettingsPanel>(FindObjectsInactive.Include);
        if (settings == null) return;
        if (settings.IsOpen)
            settings.ClosePanel();
        else if (!UIManager.Exists || !UIManager.Instance.IsAnyPanelOpen())
            settings.OpenPanel();
    }

    // ------------------------------------------------------------------
    // Desk: applicant counter, golden sticky, Skip / Hire / Next batch

    void RefreshDesk()
    {
        if (flow == null) return;

        bool batchDone = flow.CanStartNextBatch;
        int count = Mathf.Max(1, flow.ApplicantCount);
        int index = Mathf.Clamp(flow.CurrentIndex, 0, count - 1);
        int skipsLeft = batchDone ? 0 : Mathf.Max(0, count - 1 - index);

        if (applicantReview != null && applicantReview.activeSelf == batchDone) applicantReview.SetActive(!batchDone);
        if (applicantDone != null && applicantDone.activeSelf != batchDone) applicantDone.SetActive(batchDone);
        if (applicantCountText != null)
            applicantCountText.text = L10n.Get(T, "hud.sticky.applicant.count", ("index", index + 1), ("count", count));

        if (skipDots != null)
        {
            for (int i = 0; i < skipDots.Length; i++)
            {
                bool left = i < skipsLeft;
                if (skipDots[i] != null) skipDots[i].enabled = left;
                if (skipRings != null && i < skipRings.Length && skipRings[i] != null) skipRings[i].enabled = !left;
            }
        }
        if (skipsLeftText != null)
            skipsLeftText.text = L10n.Get(T, "hud.bar.skip.left", ("count", skipsLeft));

        ApplicantData current = batchDone ? null : flow.CurrentApplicant;
        bool golden = current != null && current.isGolden && !flow.IsSpawning;
        if (goldenSticky != null && goldenSticky.activeSelf != golden) goldenSticky.SetActive(golden);

        if (reviewBar != null && reviewBar.activeSelf == batchDone) reviewBar.SetActive(!batchDone);
    }
}
