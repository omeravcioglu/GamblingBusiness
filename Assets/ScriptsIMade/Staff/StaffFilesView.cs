using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The staff files (design board "Staff files"): every employee as a hanging file in a filing drawer, under a green-bar
/// strip with the staff's totals. Opened by the HUD's Staff tab (OfficeHud.StaffRequested); a pausing UIManager panel.
/// - Header: headcount, seats used / total in the HQ tower (TowerBuilder.TotalSeats), payroll, output and profit per
///   day (StaffLedger.Totals), average IQ and experience, happiness and investor trust.
/// - Sorting (default: worst profit per day first, so the losers stand out), search by name, degree or major.
/// - The list is virtualized: a few pooled rows move while it scrolls, so 300+ employees cost what 10 do. Portraits load
///   in the background (Resources.LoadAsync, one or two at a time) into a small cache of recently shown ones, and the
///   list never frees memory (Resources.UnloadUnusedAssets) while it moves. The pager buttons scroll a page.
/// - Clicking a file opens it (a CV-like sheet with the performance ledger); "Fire" there or "Let go" on a row asks
///   "Fire &lt;name&gt;? Severance $X · staff morale −N" (the drop StaffLedger.MoraleDropFor works out; Fire / Keep), then
///   StaffLedger.Fire.
/// - Escape closes the sort list, leaves the search box, closes the confirmation, the file, then the panel.
/// With Root empty the screen is built at runtime (StaffFilesBuilder) on its own overlay canvas. A designed prefab can
/// fill the references instead; rows are copies of Row Template (anchored top-left in the scroll content).
/// Texts come from the "Staff" String Table (staff.*) through L10n. When the language changes while the files are open,
/// the header, the rows, the open file and the confirmation are written again; every text follows the language's font.
/// </summary>
[DisallowMultipleComponent]
public class StaffFilesView : MonoBehaviour
{
    public enum SortMode { WorstProfitFirst, BestProfitFirst, HighestSalary, HighestIQ, NewestFirst, OldestFirst, NameAToZ }

    const string T = StaffLedger.Table;

    /// <summary>The String Table keys of the sort names, in SortMode order (the select and a designed drop-down show them).</summary>
    public static readonly string[] SortKeys =
    {
        "staff.sort.worst_profit", "staff.sort.best_profit", "staff.sort.highest_salary", "staff.sort.highest_iq",
        "staff.sort.newest", "staff.sort.oldest", "staff.sort.name"
    };

    /// <summary>The name of a sort order in the current language ("Worst profit first").</summary>
    public static string SortLabel(int index)
    {
        return L10n.Get(T, SortKeys[Mathf.Clamp(index, 0, SortKeys.Length - 1)]);
    }

    public static StaffFilesView Instance { get; private set; }

    [Header("Look")]
    [Tooltip("Font (empty = the Coming Soon font the HUD uses, else TextMesh Pro's default)")]
    public TMP_FontAsset font;
    [Tooltip("Sorting order of the runtime canvas: above the HUD and the skill board (90), below the toasts (900)")]
    public int sortingOrder = 95;

    [Header("List")]
    [Tooltip("Order of the files when the panel first opens")]
    public SortMode defaultSort = SortMode.WorstProfitFirst;
    [Tooltip("Distance from one file to the next (reference pixels)")]
    public float rowPitch = 84f;
    [Tooltip("Room above the first file and below the last")]
    public float listPaddingTop = 10f;
    public float listPaddingBottom = 10f;
    [Tooltip("Files' distance from the left edge of the scroll content")]
    public float rowLeft = 14f;
    [Tooltip("Scrolling per mouse-wheel step (reference pixels)")]
    public float wheelStep = 84f;
    [Tooltip("Portraits loading at once in the background (Resources.LoadAsync); 1-2 keeps scrolling smooth")]
    public int portraitLoadsAtOnce = 2;
    [Tooltip("Recently shown portraits kept loaded, so scrolling back shows them at once")]
    public int portraitCacheSize = 30;
    [Tooltip("Pooled rows beyond the ones that fit the view")]
    public int extraRows = 2;

    [Header("Screen (leave Root empty to build it at runtime)")]
    [Tooltip("Everything of the panel (scrim included); shown while it is open")]
    public RectTransform root;
    [Tooltip("Scaled down to fit screens that aren't 16:9 (the runtime screen's 1920 x 1080 frame; optional)")]
    public RectTransform fitFrame;
    [Tooltip("\"18 employees\"")]
    public TMP_Text countText;
    public TMP_Text seatsText;
    public TMP_Text payrollText;
    public TMP_Text outputText;
    public TMP_Text profitText;
    public TMP_Text avgIqText;
    public TMP_Text avgExperienceText;
    public TMP_Text happinessText;
    public TMP_Text trustText;
    public Button closeButton;
    [Tooltip("\"Back to the office\"")]
    public Button backButton;
    public TMP_InputField searchField;
    [Tooltip("A designed sort drop-down (its options are replaced by the sort names); or use the select below")]
    public TMP_Dropdown sortDropdown;
    [Tooltip("The select that opens Sort Menu")]
    public Button sortButton;
    public TMP_Text sortLabel;
    [Tooltip("The list of sort options (shown by Sort Button)")]
    public RectTransform sortMenu;
    [Tooltip("One per SortMode, in order")]
    public Button[] sortOptions;
    [Tooltip("Highlight behind the current option, one per SortMode")]
    public Graphic[] sortOptionMarks;
    [Tooltip("Covers the rest of the screen while the sort list is open: a click there closes it")]
    public Button sortMenuBlocker;
    public ScrollRect scrollRect;
    [Tooltip("A file row to copy (hidden itself)")]
    public StaffFileRow rowTemplate;
    [Tooltip("Shown instead of the list when it is empty")]
    public TMP_Text emptyText;
    [Tooltip("\"1–7 of 18\"")]
    public TMP_Text pageText;
    public Button prevPageButton;
    public Button nextPageButton;

    [Header("Open file")]
    public StaffFileSheet file = new StaffFileSheet();

    [Header("Fire confirmation")]
    public StaffFireConfirm confirm = new StaffFireConfirm();

    // Set by StaffFilesBuilder: the runtime header's title, which the count follows (its width changes with the language)
    internal TMP_Text runtimeTitle;
    internal float runtimeTitleLeft;
    internal float runtimeCountRight;

    struct Entry
    {
        public ApplicantData employee;
        public StaffStats stats;
        public int order;   // place in CompanyManager.employees (hiring order)
    }

    readonly List<Entry> entries = new List<Entry>();
    Comparison<Entry> comparer;
    StaffFileRow[] rows = new StaffFileRow[0];
    int[] rowIndex = new int[0];
    int[] rowVersion = new int[0];
    int dataVersion;
    bool dirty;
    bool built;
    bool hooked;
    bool isOpen;
    Canvas runtimeCanvas;
    SortMode sort;
    bool sortChosen;
    string search = "";
    CompanyManager hookedCompany;
    ApplicantData fileEmployee;
    ApplicantData confirmEmployee;
    int searchFocusFrame = -10;

    // Portraits: the recently shown ones (least recently used first), background loads, numbers without a sprite
    struct PortraitLoad
    {
        public int index;
        public ResourceRequest request;
    }

    const int LoadsBeforeUnload = 60;       // portraits loaded before the ones nothing shows are freed...
    const float UnloadIdleSeconds = 1.5f;   // ...once the list has stood still this long
    readonly Dictionary<int, Sprite> portraitCache = new Dictionary<int, Sprite>();
    readonly List<int> portraitUse = new List<int>();
    readonly List<PortraitLoad> portraitLoads = new List<PortraitLoad>();
    readonly HashSet<int> missingPortraits = new HashSet<int>();
    int portraitLoadsSinceUnload;
    float listMovedAt;
    int filePhotoIndex = -1;     // portrait number the open file shows
    int filePhotoPending = -1;   // ...or is waiting for

    /// <summary>The panel is on screen.</summary>
    public bool IsOpen => isOpen;
    /// <summary>Someone's file is open.</summary>
    public bool IsFileOpen => fileEmployee != null;
    /// <summary>The fire confirmation is up.</summary>
    public bool IsConfirmOpen => confirmEmployee != null;
    public SortMode Sort => sort;

    // ------------------------------------------------------------------
    // Life cycle
    // ------------------------------------------------------------------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Instance = null;
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[StaffFiles] Removed a second StaffFilesView on '" + name + "'.");
            Destroy(this);
            return;
        }
        Instance = this;
        comparer = CompareEntries;
        if (root != null && root.gameObject.activeSelf) root.gameObject.SetActive(false);
    }

    void OnEnable()
    {
        OfficeHud.StaffRequested -= Open;
        OfficeHud.StaffRequested += Open;
        StaffLedger.Changed -= OnFilesChanged;
        StaffLedger.Changed += OnFilesChanged;
        L10n.LanguageChanged += OnLanguageChanged;
    }

    void OnDisable()
    {
        OfficeHud.StaffRequested -= Open;
        StaffLedger.Changed -= OnFilesChanged;
        L10n.LanguageChanged -= OnLanguageChanged;
        UnhookCompany();
        Close();   // never leave the game paused
    }

    // The fonts switch by themselves (L10nFont) and the fixed labels are L10nText; the files are written again
    void OnLanguageChanged()
    {
        FillSortDropdown();
        if (!isOpen) return;
        Refresh(false);   // header, sort labels, every row, the open file
        if (confirmEmployee != null) FillConfirm(confirmEmployee);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (UIManager.Exists) UIManager.Instance.ClosePanel(this);
        if (runtimeCanvas != null) Destroy(runtimeCanvas.gameObject);
    }

    void Update()
    {
        if (!isOpen) return;
        // UI state was reset (e.g. by a debug tool) while the files are open: keep the game paused
        if (UIManager.Exists && !UIManager.Instance.IsPanelOpen(this))
            UIManager.Instance.OpenPanel(this, true, OnEscape);
        HookCompany();
        if (dirty) Refresh(false);
        LoadPortraits();
        FitFrame();
    }

    void LateUpdate()
    {
        // Escape in the search box first leaves the box (TMP may already have let go of it by the time UIManager asks)
        if (isOpen && searchField != null && searchField.isFocused) searchFocusFrame = Time.frameCount;
    }

    void HookCompany()
    {
        CompanyManager company = CompanyManager.Instance;
        if (company == hookedCompany) return;
        UnhookCompany();
        hookedCompany = company;
        if (company != null) company.StatsChanged += OnStatsChanged;
    }

    void UnhookCompany()
    {
        if (hookedCompany != null) hookedCompany.StatsChanged -= OnStatsChanged;
        hookedCompany = null;
    }

    void OnStatsChanged()
    {
        if (isOpen) dirty = true;
    }

    void OnFilesChanged()
    {
        if (isOpen) dirty = true;
    }

    // ------------------------------------------------------------------
    // Open / close
    // ------------------------------------------------------------------

    /// <summary>Open the staff files (the HUD's Staff tab does this through OfficeHud.StaffRequested).</summary>
    public void Open()
    {
        if (CompanyManager.Instance == null)
        {
            Debug.LogWarning("[StaffFiles] No company in this scene.");
            return;
        }
        EnsureBuilt();
        if (root == null) return;
        if (!sortChosen)
        {
            sort = defaultSort;
            sortChosen = true;
        }
        CloseSortMenu();
        HideConfirm();
        HideFile();

        isOpen = true;
        root.gameObject.SetActive(true);
        UIManager.BringToFront(root.gameObject);
        StaffUI.ClearSelection();
        UIManager ui = UIManager.Instance;
        if (ui != null) ui.OpenPanel(this, true, OnEscape);
        HookCompany();
        FitFrame();
        Refresh(true);
    }

    /// <summary>Close the staff files and let the game run again.</summary>
    public void Close()
    {
        bool wasOpen = isOpen;
        isOpen = false;
        CloseSortMenu();
        HideConfirm();
        HideFile();
        if (searchField != null && searchField.isFocused) searchField.DeactivateInputField();
        if (root != null && root.gameObject.activeSelf) root.gameObject.SetActive(false);
        if (wasOpen)
        {
            StaffUI.ClearSelection();
            ReleasePortraits();
        }
        if (UIManager.Exists) UIManager.Instance.ClosePanel(this);
    }

    void OnEscape()
    {
        if (IsSortMenuOpen)
        {
            CloseSortMenu();
            return;
        }
        if (searchField != null && Time.frameCount - searchFocusFrame <= 1)
        {
            searchFocusFrame = -10;
            if (searchField.isFocused) searchField.DeactivateInputField();
            StaffUI.ClearSelection();
            return;
        }
        if (IsConfirmOpen)
        {
            HideConfirm();
            return;
        }
        if (IsFileOpen)
        {
            HideFile();
            return;
        }
        Close();
    }

    void EnsureBuilt()
    {
        if (!built)
        {
            built = true;
            if (root == null)
            {
                runtimeCanvas = StaffUI.CreateCanvas("StaffFilesCanvas (Runtime)", sortingOrder, transform);
                StaffFilesBuilder.Build(this, (RectTransform)runtimeCanvas.transform, StaffUI.ResolveFont(font));
            }
            // Every text of the screen (built here or designed, the row template included) takes the language's font
            if (root != null) L10nFont.AttachAll(root);
        }
        if (!hooked) HookUI();
        EnsureRows();
    }

    void HookUI()
    {
        hooked = true;
        if (closeButton != null) closeButton.onClick.AddListener(Close);
        if (backButton != null) backButton.onClick.AddListener(Close);
        if (searchField != null)
        {
            searchField.restoreOriginalTextOnEscape = false;
            searchField.onValueChanged.AddListener(OnSearchChanged);
        }
        if (sortDropdown != null)
        {
            FillSortDropdown();
            sortDropdown.onValueChanged.AddListener(index => SetSort((SortMode)Mathf.Clamp(index, 0, SortKeys.Length - 1)));
        }
        if (sortButton != null) sortButton.onClick.AddListener(ToggleSortMenu);
        if (sortMenuBlocker != null) sortMenuBlocker.onClick.AddListener(CloseSortMenu);
        if (sortOptions != null)
        {
            for (int i = 0; i < sortOptions.Length && i < SortKeys.Length; i++)
            {
                SortMode mode = (SortMode)i;
                if (sortOptions[i] != null) sortOptions[i].onClick.AddListener(() => SetSort(mode));
            }
        }
        if (scrollRect != null) scrollRect.onValueChanged.AddListener(_ => UpdateRows());
        if (prevPageButton != null) prevPageButton.onClick.AddListener(() => Page(-1));
        if (nextPageButton != null) nextPageButton.onClick.AddListener(() => Page(1));
        if (file.fireButton != null) file.fireButton.onClick.AddListener(() => AskToFire(fileEmployee));
        if (file.closeButton != null) file.closeButton.onClick.AddListener(HideFile);
        if (confirm.fireButton != null) confirm.fireButton.onClick.AddListener(ConfirmFire);
        if (confirm.keepButton != null) confirm.keepButton.onClick.AddListener(HideConfirm);
    }

    /// <summary>Copies of the row template: enough to cover the view plus a few, reused while the list scrolls.</summary>
    void EnsureRows()
    {
        if (rows.Length > 0 || rowTemplate == null || scrollRect == null || scrollRect.content == null) return;
        if (rowTemplate.gameObject.activeSelf) rowTemplate.gameObject.SetActive(false);
        int count = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(ViewportHeight(), 300f) / Mathf.Max(8f, rowPitch)) + Mathf.Max(1, extraRows), 3, 40);
        rows = new StaffFileRow[count];
        rowIndex = new int[count];
        rowVersion = new int[count];
        for (int i = 0; i < count; i++)
        {
            StaffFileRow row = Instantiate(rowTemplate, scrollRect.content, false);
            row.name = "File " + i;
            RectTransform rect = (RectTransform)row.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            row.gameObject.SetActive(false);
            if (row.openButton != null) row.openButton.onClick.AddListener(() => OpenFile(row.Employee));
            if (row.letGoButton != null) row.letGoButton.onClick.AddListener(() => AskToFire(row.Employee));
            rows[i] = row;
            rowIndex[i] = -1;
        }
    }

    // ------------------------------------------------------------------
    // Data
    // ------------------------------------------------------------------

    /// <summary>Re-read the staff (sorted and filtered) and redraw what shows.</summary>
    public void Refresh(bool resetScroll)
    {
        dirty = false;
        CompanyManager company = CompanyManager.Instance;
        StaffLedger ledger = StaffLedger.Instance;
        entries.Clear();
        int staff = 0;
        if (company != null && company.employees != null && ledger != null)
        {
            List<ApplicantData> list = company.employees;
            for (int i = 0; i < list.Count; i++)
            {
                ApplicantData employee = list[i];
                if (employee == null) continue;
                staff++;
                if (!Matches(employee)) continue;
                entries.Add(new Entry { employee = employee, stats = ledger.GetStats(employee), order = i });
            }
        }
        entries.Sort(comparer ?? (comparer = CompareEntries));
        dataVersion++;

        RefreshHeader(company, ledger, staff);
        RefreshSortLabels();
        LayoutList(resetScroll);
        UpdateRows();
        if (emptyText != null)
        {
            bool empty = entries.Count == 0;
            if (emptyText.gameObject.activeSelf != empty) emptyText.gameObject.SetActive(empty);
            if (empty)
                emptyText.text = staff == 0 ? L10n.Get(T, "staff.empty.none") : L10n.Get(T, "staff.empty.no_match", ("search", search));
        }

        if (fileEmployee != null)
        {
            if (IsOnStaff(fileEmployee)) FillFile(fileEmployee);
            else HideFile();
        }
        if (confirmEmployee != null && !IsOnStaff(confirmEmployee)) HideConfirm();
    }

    bool Matches(ApplicantData employee)
    {
        if (string.IsNullOrEmpty(search)) return true;
        // The English (as saved) and the words shown in the current language
        return Contains(employee.name) || Contains(employee.major) || Contains(employee.GetDegreeText())
            || Contains(ApplicantText.Major(employee.major)) || Contains(ApplicantText.DegreeLine(employee));
    }

    bool Contains(string text)
    {
        return !string.IsNullOrEmpty(text) && text.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    int CompareEntries(Entry a, Entry b)
    {
        int result = 0;
        switch (sort)
        {
            case SortMode.WorstProfitFirst: result = a.stats.profitPerDay.CompareTo(b.stats.profitPerDay); break;
            case SortMode.BestProfitFirst: result = b.stats.profitPerDay.CompareTo(a.stats.profitPerDay); break;
            case SortMode.HighestSalary: result = b.employee.salary.CompareTo(a.employee.salary); break;
            case SortMode.HighestIQ: result = b.employee.IQ.CompareTo(a.employee.IQ); break;
            case SortMode.NewestFirst: return b.order.CompareTo(a.order);
            case SortMode.OldestFirst: return a.order.CompareTo(b.order);
            case SortMode.NameAToZ: result = string.Compare(a.employee.name, b.employee.name, StringComparison.OrdinalIgnoreCase); break;
        }
        return result != 0 ? result : b.order.CompareTo(a.order);   // equals: newest first
    }

    static bool IsOnStaff(ApplicantData employee)
    {
        CompanyManager company = CompanyManager.Instance;
        return employee != null && company != null && company.employees != null && company.employees.Contains(employee);
    }

    void RefreshHeader(CompanyManager company, StaffLedger ledger, int staff)
    {
        SetText(countText, staff == 0 ? L10n.Get(T, "staff.header.count.none") : L10n.Get(T, "staff.header.count", ("count", staff)));
        PlaceCount();
        StaffTotals totals = ledger != null ? ledger.Totals : new StaffTotals();

        TowerBuilder tower = TowerBuilder.Instance;
        if (seatsText != null)
        {
            if (tower != null)
            {
                int seats = tower.TotalSeats;
                SetText(seatsText, UIFormat.Int(staff) + " / " + UIFormat.Int(seats));
                seatsText.color = staff > seats ? UIColors.Loss : UIColors.Ink;
            }
            else
            {
                SetText(seatsText, "-");
                seatsText.color = UIColors.Ink;
            }
        }
        SetText(payrollText, UIFormat.Money(totals.payrollPerDay));
        SetText(outputText, UIFormat.Money(totals.outputPerDay));
        SetText(profitText, UIFormat.SignedMoney(totals.profitPerDay));
        if (profitText != null) profitText.color = totals.profitPerDay >= 0.0 ? UIColors.Money : UIColors.Loss;
        bool anyone = company != null && staff > 0;
        SetText(avgIqText, anyone ? Mathf.RoundToInt(company.averageIQ).ToString(System.Globalization.CultureInfo.InvariantCulture) : "-");
        SetText(avgExperienceText, anyone
            ? L10n.Get(T, "staff.totals.experience.value", ("years", company.averageExperience.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)))
            : "-");
        SetText(happinessText, company != null ? Percent(company.employeeHappiness) : "-");
        SetText(trustText, company != null ? Percent(company.investorTrust) : "-");
    }

    /// <summary>"72%" ("%72" in Turkish): the sign's place is in the String Table.</summary>
    static string Percent(float value)
    {
        return L10n.Get(T, "staff.totals.percent", ("percent", Mathf.Round(value).ToString("0", System.Globalization.CultureInfo.InvariantCulture)));
    }

    /// <summary>The runtime header: the count right after the title, whose width depends on the language (and its font).</summary>
    void PlaceCount()
    {
        if (runtimeTitle == null || countText == null) return;
        RectTransform title = runtimeTitle.rectTransform;
        float titleWidth = Mathf.Ceil(StaffUI.Measure(runtimeTitle, runtimeTitle.text));
        title.sizeDelta = new Vector2(titleWidth + 4f, title.sizeDelta.y);
        RectTransform count = countText.rectTransform;
        float left = runtimeTitleLeft + titleWidth + 16f;
        count.anchoredPosition = new Vector2(left, count.anchoredPosition.y);
        count.sizeDelta = new Vector2(Mathf.Max(120f, runtimeCountRight - left), count.sizeDelta.y);
    }

    // ------------------------------------------------------------------
    // Sorting and search
    // ------------------------------------------------------------------

    bool IsSortMenuOpen => sortMenu != null && sortMenu.gameObject.activeSelf;

    void ToggleSortMenu()
    {
        if (sortMenu == null) return;
        bool open = !IsSortMenuOpen;
        if (open) RefreshSortLabels();
        sortMenu.gameObject.SetActive(open);
    }

    void CloseSortMenu()
    {
        if (IsSortMenuOpen) sortMenu.gameObject.SetActive(false);
    }

    /// <summary>Change the order of the files (back to the top of the list).</summary>
    public void SetSort(SortMode mode)
    {
        sort = mode;
        sortChosen = true;
        CloseSortMenu();
        if (isOpen) Refresh(true);
    }

    /// <summary>A designed drop-down's options: the sort names in the current language.</summary>
    void FillSortDropdown()
    {
        if (sortDropdown == null) return;
        var options = new List<string>(SortKeys.Length);
        for (int i = 0; i < SortKeys.Length; i++) options.Add(SortLabel(i));
        int current = sortDropdown.value;
        sortDropdown.ClearOptions();
        sortDropdown.AddOptions(options);
        sortDropdown.SetValueWithoutNotify(Mathf.Clamp(current, 0, SortKeys.Length - 1));
        sortDropdown.RefreshShownValue();
    }

    void RefreshSortLabels()
    {
        int current = (int)sort;
        SetText(sortLabel, SortLabel(current));
        if (sortOptionMarks != null)
            for (int i = 0; i < sortOptionMarks.Length; i++)
                if (sortOptionMarks[i] != null && sortOptionMarks[i].enabled != (i == current)) sortOptionMarks[i].enabled = i == current;
        if (sortDropdown != null && sortDropdown.value != current) sortDropdown.SetValueWithoutNotify(current);
    }

    void OnSearchChanged(string value)
    {
        string trimmed = value != null ? value.Trim() : "";
        if (trimmed == search) return;
        search = trimmed;
        if (isOpen) Refresh(true);
    }

    // ------------------------------------------------------------------
    // The list (virtualized)
    // ------------------------------------------------------------------

    float ViewportHeight()
    {
        if (scrollRect == null) return 0f;
        RectTransform viewport = scrollRect.viewport != null ? scrollRect.viewport : (RectTransform)scrollRect.transform;
        return viewport.rect.height;
    }

    float RowHeight()
    {
        return rowTemplate != null ? ((RectTransform)rowTemplate.transform).rect.height : rowPitch;
    }

    float MaxScroll()
    {
        return scrollRect != null && scrollRect.content != null ? Mathf.Max(0f, scrollRect.content.rect.height - ViewportHeight()) : 0f;
    }

    void LayoutList(bool resetScroll)
    {
        if (scrollRect == null || scrollRect.content == null) return;
        RectTransform content = scrollRect.content;
        float height = listPaddingTop + listPaddingBottom + (entries.Count > 0 ? (entries.Count - 1) * rowPitch + RowHeight() : 0f);
        content.sizeDelta = new Vector2(content.sizeDelta.x, height);
        scrollRect.scrollSensitivity = wheelStep;

        Vector2 position = content.anchoredPosition;
        float max = Mathf.Max(0f, height - ViewportHeight());
        if (resetScroll)
        {
            scrollRect.StopMovement();
            position.y = 0f;
        }
        position.y = Mathf.Clamp(position.y, 0f, max);
        content.anchoredPosition = position;
        SyncScrollbar();
    }

    /// <summary>
    /// Put the scrollbar where the content is. ScrollRect does this every LateUpdate, but a bar that still holds an older
    /// value can push it back first (the Editor's layout rebuild does), which scrolled a fresh list to its end.
    /// </summary>
    void SyncScrollbar()
    {
        Scrollbar bar = scrollRect != null ? scrollRect.verticalScrollbar : null;
        if (bar == null || scrollRect.content == null) return;
        float height = scrollRect.content.rect.height;
        float viewport = ViewportHeight();
        float max = Mathf.Max(0f, height - viewport);
        bar.size = height > 0f ? Mathf.Clamp01(viewport / height) : 1f;
        bar.SetValueWithoutNotify(max > 0f ? 1f - Mathf.Clamp01(scrollRect.content.anchoredPosition.y / max) : 1f);
    }

    /// <summary>Put the pooled rows on the files in view. Each file index keeps its own slot (index % pool size), so a
    /// scroll step rebinds one row, not all of them.</summary>
    void UpdateRows()
    {
        if (rows.Length == 0 || scrollRect == null || scrollRect.content == null) return;
        listMovedAt = Time.unscaledTime;   // unused portraits are only freed while the list stands still
        float scrolled = Mathf.Max(0f, scrollRect.content.anchoredPosition.y);
        int count = entries.Count;
        int first = Mathf.Clamp(Mathf.FloorToInt((scrolled - listPaddingTop) / Mathf.Max(1f, rowPitch)), 0, Mathf.Max(0, count - 1));
        int pool = rows.Length;
        for (int index = first; index < first + pool; index++)
        {
            int slot = index % pool;
            StaffFileRow row = rows[slot];
            if (row == null) continue;
            if (index >= count)
            {
                if (row.gameObject.activeSelf)
                {
                    row.gameObject.SetActive(false);
                    row.Clear();
                }
                rowIndex[slot] = -1;
                continue;
            }
            BlueprintUI.SetAnchoredPosition((RectTransform)row.transform, new Vector2(rowLeft, -(listPaddingTop + index * rowPitch)));
            if (!row.gameObject.activeSelf) row.gameObject.SetActive(true);
            if (rowIndex[slot] != index || rowVersion[slot] != dataVersion)
            {
                Entry entry = entries[index];
                row.Bind(entry.employee, entry.stats);
                ShowCachedPortrait(row);   // a cached portrait shows right away, no placeholder flash while scrolling
                rowIndex[slot] = index;
                rowVersion[slot] = dataVersion;
            }
        }
        UpdatePager(scrolled);
    }

    void UpdatePager(float scrolled)
    {
        int count = entries.Count;
        float viewport = ViewportHeight();
        float max = MaxScroll();
        if (pageText != null)
        {
            if (count == 0)
            {
                SetText(pageText, L10n.Get(T, "staff.pager.none"));
            }
            else
            {
                float half = RowHeight() * 0.5f;
                int firstShown = Mathf.Clamp(Mathf.FloorToInt((scrolled - listPaddingTop + half) / rowPitch), 0, count - 1);
                int lastShown = Mathf.Clamp(Mathf.FloorToInt((scrolled + viewport - listPaddingTop - half) / rowPitch), firstShown, count - 1);
                SetText(pageText, L10n.Get(T, "staff.pager.range", ("first", UIFormat.Int(firstShown + 1)), ("last", UIFormat.Int(lastShown + 1)),
                                           ("count", UIFormat.Int(count))));
            }
        }
        SetButton(prevPageButton, scrolled > 0.5f);
        SetButton(nextPageButton, scrolled < max - 0.5f);
    }

    /// <summary>Scroll a page up (-1) or down (+1).</summary>
    public void Page(int direction)
    {
        if (scrollRect == null || scrollRect.content == null) return;
        int perPage = Mathf.Max(1, Mathf.FloorToInt(ViewportHeight() / Mathf.Max(1f, rowPitch)));
        RectTransform content = scrollRect.content;
        Vector2 position = content.anchoredPosition;
        float target = position.y + direction * perPage * rowPitch;
        target = Mathf.Round(target / rowPitch) * rowPitch;   // land on a file's edge
        position.y = Mathf.Clamp(target, 0f, MaxScroll());
        scrollRect.StopMovement();
        content.anchoredPosition = position;
        SyncScrollbar();
        UpdateRows();
    }

    // ------------------------------------------------------------------
    // Portraits: loaded in the background (a 1024² texture each), a couple at a time, into a small cache
    // ------------------------------------------------------------------

    /// <summary>
    /// Once a frame: finished loads go to whoever waits for them, then the open file's photo and the rows in view (top
    /// first) start new ones, at most portraitLoadsAtOnce at a time. Cached portraits show at once. Portraits nothing
    /// shows any more are freed only while the list stands still.
    /// </summary>
    void LoadPortraits()
    {
        for (int i = portraitLoads.Count - 1; i >= 0; i--)
        {
            PortraitLoad load = portraitLoads[i];
            if (load.request != null && !load.request.isDone) continue;
            portraitLoads.RemoveAt(i);
            FinishPortraitLoad(load);
        }

        if (filePhotoPending >= 0) RequestPortrait(filePhotoPending);
        int pool = rows.Length;
        if (pool > 0)
        {
            int first = FirstRowIndex();
            for (int index = first; index < first + pool && index < entries.Count; index++)
            {
                int slot = index % pool;
                StaffFileRow row = rows[slot];
                if (row == null || rowIndex[slot] != index || !row.gameObject.activeSelf || row.PendingPortrait < 0) continue;
                if (!ShowCachedPortrait(row)) RequestPortrait(row.PendingPortrait);
            }
        }
        FreeUnusedPortraits();
    }

    /// <summary>Wait for the portraits loading now and put them in place (previews and tests: the game never waits).</summary>
    public void FinishPortraitLoads()
    {
        while (portraitLoads.Count > 0)
        {
            PortraitLoad load = portraitLoads[0];
            portraitLoads.RemoveAt(0);
            FinishPortraitLoad(load);   // reading the request's asset waits for it
        }
    }

    int FirstRowIndex()
    {
        float scrolled = scrollRect != null && scrollRect.content != null ? Mathf.Max(0f, scrollRect.content.anchoredPosition.y) : 0f;
        return Mathf.Clamp(Mathf.FloorToInt((scrolled - listPaddingTop) / Mathf.Max(1f, rowPitch)), 0, Mathf.Max(0, entries.Count - 1));
    }

    /// <summary>Give a row the portrait it waits for when that needs no loading (cached, or known to have none). False otherwise.</summary>
    bool ShowCachedPortrait(StaffFileRow row)
    {
        int index = row.PendingPortrait;
        if (index < 0) return true;
        if (missingPortraits.Contains(index))
        {
            row.ShowPortrait(null, index);   // the placeholder, for good
            return true;
        }
        if (!TryCachedPortrait(index, out Sprite sprite)) return false;
        row.ShowPortrait(sprite, index);
        return true;
    }

    // Start loading a portrait, unless it's cached, loading, known to be missing, or enough loads are running
    void RequestPortrait(int index)
    {
        if (index < 0 || missingPortraits.Contains(index) || portraitCache.ContainsKey(index)) return;
        for (int i = 0; i < portraitLoads.Count; i++)
            if (portraitLoads[i].index == index) return;
        if (portraitLoads.Count >= Mathf.Max(1, portraitLoadsAtOnce)) return;

        // Straight from Resources: PortraitLibrary.Load would load it on this frame and now and then unload unused assets
        string path = PortraitLibrary.GetPath(index);
        ResourceRequest request = path != null ? Resources.LoadAsync<Sprite>(path) : null;
        if (request == null)
        {
            missingPortraits.Add(index);
            PortraitArrived(index, null);
            return;
        }
        portraitLoads.Add(new PortraitLoad { index = index, request = request });
    }

    void FinishPortraitLoad(PortraitLoad load)
    {
        Sprite sprite = load.request != null ? load.request.asset as Sprite : null;
        portraitLoadsSinceUnload++;
        if (sprite != null) CachePortrait(load.index, sprite);
        else missingPortraits.Add(load.index);   // no such file, or no sprite in it: never tried again
        PortraitArrived(load.index, sprite);
    }

    // A finished portrait goes to the rows and the open file waiting for it (null = the placeholder)
    void PortraitArrived(int index, Sprite sprite)
    {
        foreach (StaffFileRow row in rows)
            if (row != null && row.gameObject.activeSelf && row.PendingPortrait == index) row.ShowPortrait(sprite, index);
        if (filePhotoPending == index) ShowFilePhoto(sprite, index);
    }

    bool TryCachedPortrait(int index, out Sprite sprite)
    {
        if (!portraitCache.TryGetValue(index, out sprite)) return false;
        portraitUse.Remove(index);
        if (sprite == null)   // destroyed behind the cache's back
        {
            portraitCache.Remove(index);
            return false;
        }
        portraitUse.Add(index);   // most recently used
        return true;
    }

    void CachePortrait(int index, Sprite sprite)
    {
        portraitCache[index] = sprite;
        portraitUse.Remove(index);
        portraitUse.Add(index);
        int limit = Mathf.Max(1, portraitCacheSize);
        while (portraitUse.Count > limit)
        {
            portraitCache.Remove(portraitUse[0]);
            portraitUse.RemoveAt(0);
        }
    }

    // Portraits that left the cache stay in memory until Resources.UnloadUnusedAssets, which takes a moment: it only runs
    // after a good number of loads, once the list has stood still for a bit (never while it scrolls) and nothing is loading
    void FreeUnusedPortraits()
    {
        if (portraitLoadsSinceUnload < LoadsBeforeUnload || portraitLoads.Count > 0) return;
        if (Time.unscaledTime - listMovedAt < UnloadIdleSeconds) return;
        portraitLoadsSinceUnload = 0;
        Resources.UnloadUnusedAssets();
    }

    void ReleasePortraits()
    {
        // The rows, the open file and the cache let go of their portraits; loads still running are dropped (they finish
        // unused). Closing is a calm moment to free them if many piled up (not when the scene goes: its load frees them)
        foreach (StaffFileRow row in rows)
        {
            if (row == null) continue;
            row.Clear();
            if (row.gameObject.activeSelf) row.gameObject.SetActive(false);
        }
        for (int i = 0; i < rowIndex.Length; i++) rowIndex[i] = -1;
        ShowFilePhoto(null, -1);
        portraitLoadsSinceUnload += portraitLoads.Count;
        portraitLoads.Clear();
        portraitCache.Clear();
        portraitUse.Clear();
        if (portraitLoadsSinceUnload >= LoadsBeforeUnload && isActiveAndEnabled)
        {
            portraitLoadsSinceUnload = 0;
            Resources.UnloadUnusedAssets();
        }
    }

    void FitFrame()
    {
        if (fitFrame == null) return;
        Canvas canvas = fitFrame.GetComponentInParent<Canvas>();
        if (canvas == null) return;
        RectTransform canvasRect = (RectTransform)canvas.rootCanvas.transform;
        Vector2 size = canvasRect.rect.size;
        Vector2 design = fitFrame.rect.size;
        if (size.x <= 0f || size.y <= 0f || design.x <= 0f || design.y <= 0f) return;
        float scale = Mathf.Min(1f, Mathf.Min(size.x / design.x, size.y / design.y));
        BlueprintUI.SetLocalScale(fitFrame, new Vector3(scale, scale, 1f));
    }

    // ------------------------------------------------------------------
    // The open file
    // ------------------------------------------------------------------

    /// <summary>Open <paramref name="employee"/>'s file (the CV-like sheet with the performance ledger and Fire).</summary>
    public void OpenFile(ApplicantData employee)
    {
        if (employee == null || file.root == null || !IsOnStaff(employee)) return;
        CloseSortMenu();
        fileEmployee = employee;
        FillFile(employee);
        if (!file.root.gameObject.activeSelf) file.root.gameObject.SetActive(true);
        StaffUI.ClearSelection();
    }

    void HideFile()
    {
        fileEmployee = null;
        if (file.root != null && file.root.gameObject.activeSelf) file.root.gameObject.SetActive(false);
        ShowFilePhoto(null, -1);
    }

    // The open file's photo: from the cache when it's there, else loaded in the background ahead of the rows (the
    // placeholder until then)
    void FillFilePhoto(int index)
    {
        if (file.photo == null)
        {
            filePhotoPending = -1;
            return;
        }
        if (index >= 0 && index == filePhotoIndex && file.photo.sprite != null) return;   // already showing
        if (index >= 0 && TryCachedPortrait(index, out Sprite cached))
        {
            ShowFilePhoto(cached, index);
            return;
        }
        ShowFilePhoto(null, -1);
        if (index >= 0 && !missingPortraits.Contains(index)) filePhotoPending = index;
    }

    void ShowFilePhoto(Sprite sprite, int index)
    {
        filePhotoPending = -1;
        filePhotoIndex = sprite != null ? index : -1;
        if (file.photo != null)
        {
            file.photo.sprite = sprite;
            file.photo.enabled = sprite != null;
        }
        if (file.photoPlaceholder != null && file.photoPlaceholder.activeSelf == (sprite != null)) file.photoPlaceholder.SetActive(sprite == null);
    }

    void FillFile(ApplicantData e)
    {
        StaffLedger ledger = StaffLedger.Instance;
        StaffStats stats = ledger != null ? ledger.GetStats(e) : new StaffStats();
        string name = StaffLedger.DisplayName(e);
        int space = name.IndexOf(' ');
        string first = space > 0 ? name.Substring(0, space) : name;
        string last = space > 0 ? name.Substring(space + 1) : "";
        string soft = "<color=" + StaffUI.Hex(UIColors.InkSoft) + ">";

        SetText(file.firstNameText, first);
        SetText(file.lastNameText, last);
        if (file.goldenStars != null)
        {
            if (file.goldenStars.gameObject.activeSelf != e.isGolden) file.goldenStars.gameObject.SetActive(e.isGolden);
            if (e.isGolden && file.firstNameText != null)
            {
                RectTransform nameRect = file.firstNameText.rectTransform;
                float width = Mathf.Min(StaffUI.Measure(file.firstNameText, first), nameRect.rect.width);
                Vector2 position = file.goldenStars.anchoredPosition;
                position.x = nameRect.anchoredPosition.x + width + 10f;
                file.goldenStars.anchoredPosition = position;
            }
        }
        SetText(file.metaText, StaffText.Meta(e));
        if (file.paper != null) file.paper.Border = e.isGolden ? UIColors.GoldDark : UIColors.Ink;
        if (file.rule != null)
        {
            file.rule.color = e.isGolden ? UIColors.Gold : UIColors.Ink;
            RectTransform rule = file.rule.rectTransform;
            rule.sizeDelta = new Vector2(rule.sizeDelta.x, e.isGolden ? 8f : 4f);
        }
        FillFilePhoto(e.portraitIndex);

        // Degree and major in the current language (ApplicantText; the data keeps the applicant generator's English)
        SetText(file.degreeText, ApplicantText.DegreeLine(e));
        SetText(file.majorText, ApplicantText.Major(e.major));
        SetText(file.gpaText, StaffText.Gpa(e.GPA));
        int years = Mathf.Max(0, e.experienceYears);
        SetText(file.experienceText, L10n.Get(T, "staff.file.experience", ("years", "<b>" + years.ToString(System.Globalization.CultureInfo.InvariantCulture) + "</b>"),
                                              ("unit", "<size=73%>" + soft + L10n.Get(T, "staff.file.experience.unit", ("years", years)) + "</color></size>")));
        SetText(file.salaryText, L10n.Get(T, "staff.file.salary", ("amount", "<b>" + UIFormat.Money(e.salary) + "</b>"),
                                          ("unit", "<size=73%>" + soft + L10n.Get(T, "staff.file.salary.unit") + "</color></size>")));
        FillTraits(e);
        SetText(file.iqText, e.IQ.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Color recordColor = e.criminalRecord ? UIColors.Loss : UIColors.Money;
        SetText(file.recordText, L10n.Get(T, e.criminalRecord ? "staff.file.record.has" : "staff.file.record.clean"));
        if (file.recordText != null) file.recordText.color = recordColor;
        if (file.recordInk != null)
            foreach (Graphic ink in file.recordInk)
                if (ink is BlueprintBox box) box.Border = recordColor;
                else if (ink != null) ink.color = recordColor;
        if (file.goldenSeal != null && file.goldenSeal.activeSelf != e.isGolden) file.goldenSeal.SetActive(e.isGolden);
        SetText(file.fileNumberText, L10n.Get(T, "staff.file.number", ("number", StaffText.FileNumber(e.id))));

        // Performance ledger (someone hired before the staff files were kept is "on file since day N", with days on file)
        SetText(file.basisText, L10n.Get(T, stats.hasWorkedADay ? "staff.file.basis.last_day" : "staff.file.basis.estimate"));
        string hireDay = UIFormat.Int(stats.hireDay);
        SetText(file.hiredText, L10n.Get(T, stats.hireDayKnown ? "staff.file.hired" : "staff.file.hired.on_file", ("day", hireDay)));
        SetText(file.daysText, L10n.Get(T, stats.hireDayKnown ? "staff.file.days" : "staff.file.days.on_file", ("days", stats.daysEmployed)));
        SetMoney(file.outputText, stats.dayOutput);
        SetMoney(file.salaryDayText, -stats.daySalary);
        SetMoney(file.overheadText, -stats.dayOverhead);
        SetMoney(file.troubleText, -stats.dayIncidents);
        SetMoney(file.bonusText, stats.dayBonus);
        SetMoney(file.profitText, stats.profitPerDay);
        SetMoney(file.lifetimeText, stats.lifetimeProfit);

        // One note per line, each a whole sentence of its own
        var notes = new List<string>();
        if (!stats.hireDayKnown)
            notes.Add(L10n.Get(T, stats.hasWorkedADay ? "staff.file.note.on_file" : "staff.file.note.on_file.estimate", ("day", hireDay)));
        else if (!stats.hasWorkedADay)
            notes.Add(L10n.Get(T, "staff.file.note.estimate"));
        if (stats.incidentDays > 0)
            notes.Add(L10n.Get(T, "staff.file.note.trouble", ("days", stats.incidentDays)));
        if (ledger != null)
        {
            float drop = ledger.MoraleDropFor(e);
            string severance = UIFormat.Money(ledger.SeveranceFor(e));
            int severanceDays = Mathf.RoundToInt(ledger.severanceDays);
            notes.Add(HasDrop(drop)
                ? L10n.Get(T, "staff.file.note.severance.morale", ("amount", severance), ("days", severanceDays), ("morale", MoraleText(drop)))
                : L10n.Get(T, "staff.file.note.severance", ("amount", severance), ("days", severanceDays)));
            // "Hired today: ..." / "Hired 3 days ago: ..." starts the first of the two notes below that applies
            bool hiredToday = stats.daysEmployed == 0;
            bool hiredSaid = false;
            float takenBack = ledger.HireMoraleTakenBack(e);
            if (takenBack > 0f && HasDrop(drop))
            {
                notes.Add(hiredToday ? L10n.Get(T, "staff.file.note.traits.today", ("gain", GainText(takenBack)))
                                     : L10n.Get(T, "staff.file.note.traits.ago", ("days", stats.daysEmployed), ("gain", GainText(takenBack))));
                hiredSaid = true;
            }
            if (ledger.WouldTakeBackSkillProgress(e))
            {
                if (hiredSaid) notes.Add(L10n.Get(T, "staff.file.note.skill"));
                else if (hiredToday) notes.Add(L10n.Get(T, "staff.file.note.skill.today"));
                else notes.Add(L10n.Get(T, "staff.file.note.skill.ago", ("days", stats.daysEmployed)));
            }
        }
        SetText(file.noteText, string.Join("\n", notes));
    }

    void FillTraits(ApplicantData e)
    {
        int count = file.traitTexts != null ? file.traitTexts.Length : 0;
        int shown = 0;
        if (e.traits != null)
        {
            foreach (PersonalityTrait trait in e.traits)
            {
                if (shown >= count) break;
                if (trait == null || string.IsNullOrEmpty(trait.traitName)) continue;
                SetTrait(shown, ApplicantText.Trait(trait.traitName), trait.type);
                shown++;
            }
        }
        if (shown == 0 && count > 0)
        {
            SetTrait(0, L10n.Get(T, "staff.file.traits.none"), TraitType.Neutral);
            if (file.traitIcons != null && file.traitIcons.Length > 0 && file.traitIcons[0] != null) file.traitIcons[0].gameObject.SetActive(false);
            shown = 1;
        }
        for (int i = shown; i < count; i++)
        {
            if (file.traitTexts[i] != null && file.traitTexts[i].gameObject.activeSelf) file.traitTexts[i].gameObject.SetActive(false);
            if (file.traitIcons != null && i < file.traitIcons.Length && file.traitIcons[i] != null) file.traitIcons[i].gameObject.SetActive(false);
        }
    }

    void SetTrait(int i, string label, TraitType type)
    {
        TMP_Text text = file.traitTexts[i];
        if (text != null)
        {
            if (!text.gameObject.activeSelf) text.gameObject.SetActive(true);
            SetText(text, label);
        }
        StaffIcon icon = file.traitIcons != null && i < file.traitIcons.Length ? file.traitIcons[i] : null;
        if (icon == null) return;
        if (!icon.gameObject.activeSelf) icon.gameObject.SetActive(true);
        icon.Kind = type == TraitType.Positive ? StaffIconKind.Check : type == TraitType.Negative ? StaffIconKind.Cross : StaffIconKind.Wave;
        icon.color = type == TraitType.Positive ? UIColors.Money : type == TraitType.Negative ? UIColors.Loss : UIColors.Info;
    }

    // ------------------------------------------------------------------
    // Firing
    // ------------------------------------------------------------------

    /// <summary>Ask "Fire &lt;name&gt;? Severance $X · staff morale −N" (Fire / Keep); N is the drop Fire will cause.</summary>
    public void AskToFire(ApplicantData employee)
    {
        StaffLedger ledger = StaffLedger.Instance;
        if (employee == null || ledger == null || confirm.root == null || !IsOnStaff(employee)) return;
        CloseSortMenu();
        confirmEmployee = employee;
        FillConfirm(employee);

        if (!confirm.root.gameObject.activeSelf) confirm.root.gameObject.SetActive(true);
        StaffUI.ClearSelection();
        GameSfx.Play(Sfx.UiQuestion);   // "Fire them?"
    }

    /// <summary>The confirmation's texts for <paramref name="employee"/> (written again when the language changes).</summary>
    void FillConfirm(ApplicantData employee)
    {
        StaffLedger ledger = StaffLedger.Instance;
        if (employee == null || ledger == null) return;
        CompanyManager company = CompanyManager.Instance;
        double value = company != null ? Math.Max(0.0, company.companyValue) : 0.0;
        double severance = ledger.SeveranceFor(employee);
        double payable = ledger.SeverancePayable(employee);
        float morale = ledger.MoraleDropFor(employee);   // what Fire takes: Employee Retention and a quick fire included

        SetText(confirm.titleText, L10n.Get(T, "staff.confirm.title", ("name", StaffLedger.DisplayName(employee))));
        string amount = "<color=" + StaffUI.Hex(UIColors.Loss) + ">" + UIFormat.Money(payable) + "</color>";
        if (HasDrop(morale)) SetText(confirm.detailText, L10n.Get(T, "staff.confirm.detail", ("amount", amount), ("morale", MoraleText(morale))));
        else if (employee.criminalRecord) SetText(confirm.detailText, L10n.Get(T, "staff.confirm.detail.nobody", ("amount", amount)));
        else SetText(confirm.detailText, L10n.Get(T, "staff.confirm.detail", ("amount", amount), ("morale", "0")));

        var notes = new List<string>();
        if (payable < severance - 0.5)
            notes.Add(L10n.Get(T, "staff.confirm.note.short", ("value", UIFormat.Money(value)), ("severance", UIFormat.Money(severance))));
        else if (payable > 0.0 && value - payable < 0.5)
            notes.Add(L10n.Get(T, "staff.confirm.note.empties"));
        int window = ledger.farmingWindowDays;
        float takenBack = HasDrop(morale) ? ledger.HireMoraleTakenBack(employee) : 0f;
        bool skillBack = ledger.WouldTakeBackSkillProgress(employee);
        if (takenBack > 0f && skillBack)
            notes.Add(L10n.Get(T, "staff.confirm.note.traits_skill", ("days", window), ("gain", GainText(takenBack))));
        else if (takenBack > 0f)
            notes.Add(L10n.Get(T, "staff.confirm.note.traits", ("days", window), ("gain", GainText(takenBack))));
        else if (skillBack)
            notes.Add(L10n.Get(T, "staff.confirm.note.skill", ("days", window)));
        SetText(confirm.noteText, string.Join("\n", notes));
    }

    void HideConfirm()
    {
        confirmEmployee = null;
        if (confirm.root != null && confirm.root.gameObject.activeSelf) confirm.root.gameObject.SetActive(false);
    }

    void ConfirmFire()
    {
        ApplicantData target = confirmEmployee;
        HideConfirm();
        StaffLedger ledger = StaffLedger.Instance;
        if (target == null || ledger == null) return;
        if (!ledger.Fire(target)) return;
        GameSfx.Play(Sfx.CardToss);   // their file goes in the bin
        if (fileEmployee == target) HideFile();
        if (isOpen) Refresh(false);
    }

    /// <summary>"−2" (a true minus sign; one decimal when it has one).</summary>
    static string MoraleText(float amount)
    {
        return UIFormat.Minus + amount.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>"+4", "+3.9".</summary>
    static string GainText(float amount)
    {
        return "+" + amount.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>A morale drop worth showing (at least "−0.1").</summary>
    static bool HasDrop(float amount)
    {
        return amount >= 0.05f;
    }

    // ------------------------------------------------------------------

    static void SetText(TMP_Text text, string value)
    {
        if (text != null && text.text != value) text.text = value;
    }

    static void SetMoney(TMP_Text text, double amount)
    {
        if (text == null) return;
        SetText(text, UIFormat.SignedMoney(amount));
        text.color = amount > 0.5 ? UIColors.Money : amount < -0.5 ? UIColors.Loss : UIColors.Ink;
    }

    static void SetButton(Button button, bool on)
    {
        if (button != null && button.interactable != on) StaffUI.SetEnabled(button, on);
    }
}
