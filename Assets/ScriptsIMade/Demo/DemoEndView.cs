using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Which demo card shows: the DEMO COMPLETE card (with Keep playing), or one of the two final ones.</summary>
public enum DemoEndMode
{
    End,        // after the day-12 verdict or the time cap
    Final,      // after Keep playing ran out (day / time hard stop)
    Jackpot     // company value reached the demo's cap (always final)
}

/// <summary>The Board verdict line of a demo card.</summary>
public enum DemoVerdict
{
    None,
    Hit,
    Miss,
    Skipped,
    Pending
}

/// <summary>What a demo card shows (DemoDirector fills it in; the view writes the texts in the current language).</summary>
[Serializable]
public class DemoEndInfo
{
    public string companyName = "";
    public int days = 1;
    public double startValue, value, peak, best;
    public bool newBest;
    public int hires, goldenHires, rooms, desks, audits, crisesSolved, crisesTotal;
    public bool hasGambled;
    public double casinoNet, biggestWin;
    public DemoVerdict verdict;
    public int verdictTier;
    public int finaleDay;   // the verdict's meeting day, when later than DemoMode.FinaleDay (a pending verdict names it)
    public bool houseFlip, nextTarget, managerLocked;
}

/// <summary>
/// The demo's end card (WinView's runtime path): a paper sheet over a scrim with a rotated stamp (DEMO COMPLETE, DEMO
/// OVER or YOU BROKE THE DEMO), the run's title, the Board verdict, six stat rows, this run against the best demo run,
/// the house-flip reveal, a sticky "In the full game" list and the buttons Wishlist on Steam / Keep playing (End mode
/// only, also Escape) / New company / Main menu. Pausing (UIManager); DemoDirector decides when it shows and what the
/// buttons do. Built at runtime on its own canvas (sorting 460: above the tutorial, below the toasts); texts from the
/// Demo table, built again in the new language when the language changes while it shows.
/// </summary>
[DisallowMultipleComponent]
public class DemoEndView : MonoBehaviour
{
    const float SheetWidth = 1300f, SheetHeight = 900f;
    const float LeftX = 52f, LeftWidth = 660f, RightX = 752f, RightWidth = 500f, ColumnTop = 44f;

    [Tooltip("Font (empty = the Coming Soon font the HUD uses, else TextMesh Pro's default)")]
    public TMP_FontAsset font;
    [Tooltip("Sorting order of the runtime canvas: above the tutorial (455), below the toasts (900)")]
    public int sortingOrder = 460;

    // DemoDirector's handlers for the buttons
    public Action onWishlist, onKeepPlaying, onNewCompany, onMainMenu;

    RectTransform root;
    Canvas runtimeCanvas;
    string builtLanguage;
    TextMeshProUGUI stampLabel, titleText, subtitleText, verdictText, bestText, houseFlipText, nextTargetText;
    readonly TextMeshProUGUI[] rows = new TextMeshProUGUI[6];
    RectTransform stamp, stampArea, newBestStamp;
    GameObject managerItem;
    Button wishlistButton, keepButton, newCompanyButton, menuButton;
    TextHighlighter verdictMark;

    DemoEndInfo shownInfo;
    bool rebuildPending;

    public bool IsOpen { get; private set; }
    public DemoEndMode Mode { get; private set; }

    void OnEnable()
    {
        L10n.LanguageChanged += OnLanguageChanged;
    }

    void OnDisable()
    {
        L10n.LanguageChanged -= OnLanguageChanged;
        if (IsOpen)
        {
            IsOpen = false;
            if (UIManager.Exists) UIManager.Instance.ClosePanel(this);
        }
    }

    void OnDestroy()
    {
        if (UIManager.Exists) UIManager.Instance.ClosePanel(this);
    }

    // The layout follows its words (stamp and button widths): build it again in the new language next frame
    void OnLanguageChanged()
    {
        if (IsOpen) rebuildPending = true;
    }

    void Update()
    {
        if (!IsOpen) return;
        if (rebuildPending)
        {
            rebuildPending = false;
            EnsureBuilt();
            Fill();
            root.gameObject.SetActive(true);
        }
        // UI state was reset (e.g. by a debug tool) while the card is up: keep the game paused
        if (UIManager.Exists && !UIManager.Instance.IsPanelOpen(this))
            UIManager.Instance.OpenPanel(this, true, OnEscape);
    }

    public void Show(DemoEndMode mode, DemoEndInfo info)
    {
        Mode = mode;
        shownInfo = info ?? new DemoEndInfo();
        EnsureBuilt();
        Fill();
        root.gameObject.SetActive(true);
        UIManager.BringToFront(root.gameObject);
        IsOpen = true;
        RunEndUI.ClearSelection();
        UIManager ui = UIManager.Instance;
        if (ui != null) ui.OpenPanel(this, true, OnEscape);
        GameSfx.PlayPair(Sfx.ChipsWin, Sfx.UiGlass, 0.3f);
    }

    public void Close()
    {
        if (root != null) root.gameObject.SetActive(false);
        if (!IsOpen) return;
        IsOpen = false;
        if (UIManager.Exists) UIManager.Instance.ClosePanel(this);
    }

    // Escape = Keep playing on the End card; the final cards stay
    void OnEscape()
    {
        if (Mode == DemoEndMode.End) onKeepPlaying?.Invoke();
    }

    void EnsureBuilt()
    {
        // Built in the current language, again when it was built in another (never while the old one is filled: Update
        // calls this only to rebuild)
        bool built = RunEndUI.EnsureBuiltInLanguage(ref runtimeCanvas, ref builtLanguage, false, () =>
        {
            Canvas canvas = RunEndUI.CreateCanvas("DemoEndCanvas (Runtime)", sortingOrder, transform);
            Build((RectTransform)canvas.transform, RunEndUI.ResolveFont(font));
            return canvas;
        });
        if (!built) return;
        L10nFont.AttachAll(root);   // every text takes the language's font
        wishlistButton.onClick.AddListener(() => onWishlist?.Invoke());
        keepButton.onClick.AddListener(() => onKeepPlaying?.Invoke());
        newCompanyButton.onClick.AddListener(() => onNewCompany?.Invoke());
        menuButton.onClick.AddListener(() => onMainMenu?.Invoke());
    }

    // ------------------------------------------------------------------
    // Texts
    // ------------------------------------------------------------------

    void Fill()
    {
        DemoEndInfo run = shownInfo ?? new DemoEndInfo();
        bool end = Mode == DemoEndMode.End;
        string company = string.IsNullOrWhiteSpace(run.companyName)
            ? L10n.Get(RunEndUI.Table, "runend.company.fallback")
            : CompanyNameUI.DisplayName(run.companyName);   // the default name in the player's language
        string days = UIFormat.Int(Mathf.Max(1, run.days));

        string stampKey = Mode == DemoEndMode.Jackpot ? "demo.end.jackpot.stamp" : Mode == DemoEndMode.Final ? "demo.end.final.stamp" : "demo.end.stamp";
        SetStamp(DemoMode.T(stampKey));

        if (Mode == DemoEndMode.Jackpot)
            titleText.text = DemoMode.T("demo.end.jackpot.title", ("company", company), ("value", UIFormat.Money(run.value)), ("days", days));
        else if (Mode == DemoEndMode.Final)
            titleText.text = DemoMode.T("demo.end.final.title");
        else
            titleText.text = DemoMode.T("demo.end.title", ("company", company), ("days", days));
        subtitleText.text = end ? DemoMode.T("demo.end.subtitle") : DemoMode.T("demo.end.final.body", ("company", company));

        // A pending verdict asks to keep playing: only on the End card
        string verdict = VerdictLine(run, end);
        verdictText.gameObject.SetActive(!string.IsNullOrEmpty(verdict));
        verdictText.text = string.IsNullOrEmpty(verdict) ? "" : RunEndUI.Mark(verdict);
        if (verdictMark != null) verdictMark.color = run.verdict == DemoVerdict.Hit ? RunEndUI.MarkGreen : RunEndUI.MarkYellow;

        rows[0].text = DemoMode.T("demo.end.row.value", ("start", UIFormat.Money(run.startValue)), ("value", UIFormat.Money(run.value)),
                                  ("peak", UIFormat.Money(run.peak)));
        rows[1].text = DemoMode.T("demo.end.row.hires", ("hires", UIFormat.Int(run.hires)), ("golden", UIFormat.Int(run.goldenHires)));
        rows[2].text = DemoMode.T("demo.end.row.build", ("rooms", UIFormat.Int(run.rooms)), ("desks", UIFormat.Int(run.desks)));
        string net = run.hasGambled ? UIFormat.SignedMoney(run.casinoNet) : UIFormat.Money(0.0);
        rows[3].text = DemoMode.T("demo.end.row.casino", ("net", net), ("audits", UIFormat.Int(run.audits)));
        rows[4].text = DemoMode.T("demo.end.row.risk", ("amount", run.biggestWin > 0.5 ? UIFormat.SignedMoney(run.biggestWin) : UIFormat.Money(0.0)));
        rows[5].text = DemoMode.T("demo.end.row.crises", ("solved", UIFormat.Int(run.crisesSolved)), ("total", UIFormat.Int(run.crisesTotal)));

        bestText.text = DemoMode.T("demo.end.best", ("peak", UIFormat.Money(run.peak)), ("best", UIFormat.Money(Math.Max(run.best, run.peak))));
        newBestStamp.gameObject.SetActive(run.newBest);
        BlueprintUI.Stretch(bestText.rectTransform, 0f, 0f, run.newBest ? newBestStamp.sizeDelta.x + 20f : 0f, 0f);

        houseFlipText.gameObject.SetActive(run.houseFlip);
        nextTargetText.gameObject.SetActive(end && run.nextTarget);
        managerItem.SetActive(run.managerLocked);
        keepButton.gameObject.SetActive(end);
    }

    static string VerdictLine(DemoEndInfo run, bool endCard)
    {
        switch (run.verdict)
        {
            case DemoVerdict.Hit:
                return DemoMode.T("demo.end.verdict.hit", ("tier", BoardMeetings.TierName(run.verdictTier)));
            case DemoVerdict.Miss:
                return DemoMode.T("demo.end.verdict.miss", ("tier", BoardMeetings.TierName(run.verdictTier)));
            case DemoVerdict.Skipped:
                return DemoMode.T("demo.end.verdict.skipped");
            case DemoVerdict.Pending:
                return endCard ? DemoMode.T("demo.end.verdict.pending", ("day", UIFormat.Int(Mathf.Max(DemoMode.FinaleDay, run.finaleDay)))) : "";
            default:
                return "";
        }
    }

    // The stamp keeps its rect: a longer word (another mode) shrinks into it
    void SetStamp(string word)
    {
        if (stampLabel == null) return;
        stampLabel.text = word;
        TMP_FontAsset useFont = stampLabel.font;
        float natural = RunEndUI.TextWidth(useFont, word, StampSize, true, 0.1f) + 48f;
        float width = Mathf.Clamp(natural, 220f, RightWidth - 30f);
        stamp.sizeDelta = new Vector2(width, stamp.sizeDelta.y);
    }

    // ------------------------------------------------------------------
    // Runtime layout (1920 x 1080 reference; a 1300 x 900 sheet in the middle)
    // ------------------------------------------------------------------

    const float StampSize = 52f;

    void Build(RectTransform parent, TMP_FontAsset useFont)
    {
        // Built inactive, so MarkerButton and TextHighlighter start with their references set
        root = BlueprintUI.Stretch(BlueprintUI.NewRect("DemoEnd", parent));
        root.gameObject.SetActive(false);
        RunEndUI.Scrim(root, UIColors.Scrim);

        BlueprintBox sheet = RunEndUI.Sheet(root, "Sheet", UIColors.Paper, BlueprintUI.RadiusCard, 0f, 0f, SheetWidth, SheetHeight);
        BlueprintUI.Centered(sheet.rectTransform, SheetWidth, SheetHeight);
        RunEndUI.Tape(sheet.transform, 190f, -2f);
        RectTransform s = RunEndUI.Inner(sheet.transform, 3f);

        BuildLeft(s, useFont);
        BuildRight(s, useFont);
    }

    void BuildLeft(RectTransform s, TMP_FontAsset useFont)
    {
        RectTransform left = BlueprintUI.NewRect("Left", s);
        BlueprintUI.TopLeft(left, LeftX, ColumnTop, LeftWidth, SheetHeight - ColumnTop * 2f);
        BlueprintUI.VColumn(left, 12f, new RectOffset(0, 0, 0, 0), TextAnchor.UpperLeft);

        titleText = Line(left, "Title", 46f, UIColors.Ink, useFont, true, 64f, 28f);
        subtitleText = Para(left, "Subtitle", 22f, UIColors.InkSoft, useFont, false);

        verdictText = Para(left, "Verdict", 24f, UIColors.Ink, useFont, true);
        verdictMark = RunEndUI.Highlight(verdictText, RunEndUI.MarkYellow);

        // Stat rows on a paper card
        BlueprintBox card = BlueprintUI.Box(left, "Stats", UIColors.PaperHi, UIColors.Pencil, 2f, BlueprintUI.RadiusSmall);
        BlueprintUI.VColumn(card, 2f, new RectOffset(20, 20, 12, 12), TextAnchor.UpperLeft);
        string[] names = { "Value", "Hires", "Build", "Casino", "Risk", "Crises" };
        for (int i = 0; i < rows.Length; i++)
            rows[i] = Line(card.transform, names[i], 21f, UIColors.Ink, useFont, false, 34f, 14f);

        // This run against the best, with the NEW BEST stamp at the row's right end
        RectTransform bestRow = BlueprintUI.NewRect("BestRow", left);
        BlueprintUI.Layout(bestRow, -1f, 46f);
        bestText = RunEndUI.Shrink(BlueprintUI.Text(bestRow, "Best", "", 22f, UIColors.Ink, useFont, TextAlignmentOptions.MidlineLeft, true), 15f);
        BlueprintUI.Stretch(bestText.rectTransform);
        string newBest = DemoMode.T("demo.end.new_best");
        newBestStamp = RunEndUI.Stamp(bestRow, "NewBest", newBest, 22f, UIColors.Money, useFont, 3f, 0f, 0f, 6f, 0.08f, 10f, 2f, -6f);
        newBestStamp.anchorMin = newBestStamp.anchorMax = new Vector2(1f, 0.5f);
        newBestStamp.anchoredPosition = new Vector2(-newBestStamp.sizeDelta.x * 0.5f - 4f, 0f);
        ShrinkStampLabel(newBestStamp);

        houseFlipText = Para(left, "HouseFlip", 19f, UIColors.Pencil, useFont, false);
        L10nText.Attach(houseFlipText, DemoMode.Table, "demo.end.house_flip");
        nextTargetText = Para(left, "NextTarget", 19f, UIColors.Ink, useFont, false);
        L10nText.Attach(nextTargetText, DemoMode.Table, "demo.end.next_target");
    }

    void BuildRight(RectTransform s, TMP_FontAsset useFont)
    {
        RectTransform right = BlueprintUI.NewRect("Right", s);
        BlueprintUI.TopLeft(right, RightX, ColumnTop - 14f, RightWidth, SheetHeight - ColumnTop * 2f + 14f);
        BlueprintUI.VColumn(right, 12f, new RectOffset(0, 0, 0, 0), TextAnchor.UpperLeft);

        // The stamp sits in a fixed area at the top (its width follows the mode's word)
        stampArea = BlueprintUI.NewRect("StampArea", right);
        BlueprintUI.Layout(stampArea, -1f, 108f);
        stamp = RunEndUI.Stamp(stampArea, "Stamp", DemoMode.T("demo.end.stamp"), StampSize, UIColors.Loss, useFont, 6f, 3f, 5f, 12f, 0.1f, 22f, 0f, -6f);
        stamp.anchorMin = stamp.anchorMax = new Vector2(0.5f, 0.5f);
        stamp.anchoredPosition = Vector2.zero;
        stampLabel = stamp.GetComponentInChildren<TextMeshProUGUI>(true);
        ShrinkStampLabel(stamp);

        // "In the full game" on a sticky note
        BlueprintBox sticky = BlueprintUI.Box(right, "FullGame", UIColors.StickyYellow, Color.clear, 0f, BlueprintUI.Round(2f));
        sticky.SetShadow(new Color(0f, 0f, 0f, 0.22f), new Vector2(3f, 6f), 8f);
        sticky.rectTransform.localRotation = RunEndUI.Rotation(1f);
        BlueprintUI.VColumn(sticky, 6f, new RectOffset(24, 24, 18, 20), TextAnchor.UpperLeft);
        TextMeshProUGUI heading = Line(sticky.transform, "Title", 25f, UIColors.Ink, useFont, true, 40f, 17f);
        L10nText.Attach(heading, DemoMode.Table, "demo.end.full.title");
        float itemWidth = RightWidth - 48f - 22f - 10f;
        Item(sticky.transform, "Online", "demo.end.full.online", itemWidth, useFont);
        Item(sticky.transform, "Stocks", "demo.end.full.stocks", itemWidth, useFont);
        Item(sticky.transform, "Skills", "demo.end.full.skills", itemWidth, useFont);
        Item(sticky.transform, "Tower", "demo.end.full.tower", itemWidth, useFont);
        Item(sticky.transform, "Ending", "demo.end.full.ending", itemWidth, useFont);
        managerItem = Item(sticky.transform, "Manager", "demo.end.full.manager", itemWidth, useFont);

        TextMeshProUGUI tryNext = Para(right, "TryNext", 19f, UIColors.Pencil, useFont, false);
        L10nText.Attach(tryNext, DemoMode.Table, "demo.end.try_next");

        BlueprintUI.Spacer(right, 0f, 1f);

        // Buttons: Wishlist (primary), Keep playing + New company, Main menu
        wishlistButton = RunEndUI.PaperButton(right, "Wishlist", DemoMode.T("demo.wishlist"), RunEndIconKind.None, useFont, UIColors.Highlight,
                                              RightWidth, 70f, 30f, true, new Vector2(5f, 6f), out TextMeshProUGUI wishlist);
        L10nText.Attach(wishlist, DemoMode.Table, "demo.wishlist");
        BlueprintUI.Layout(wishlistButton, -1f, 70f);

        RectTransform pair = BlueprintUI.NewRect("Buttons", right);
        BlueprintUI.Layout(pair, -1f, 62f);
        HorizontalLayoutGroup pairRow = BlueprintUI.HRow(pair, 16f, new RectOffset(0, 6, 0, 0), TextAnchor.MiddleLeft);
        pairRow.childForceExpandWidth = true;
        keepButton = RunEndUI.PaperButton(pair, "KeepPlaying", DemoMode.T("demo.end.keep"), RunEndIconKind.Play, useFont, UIColors.PaperHi,
                                          240f, 58f, 24f, false, new Vector2(4f, 5f), out TextMeshProUGUI keep, 18f, 24f);
        L10nText.Attach(keep, DemoMode.Table, "demo.end.keep");
        BlueprintUI.Layout(keepButton, -1f, 58f, 1f);
        newCompanyButton = RunEndUI.PaperButton(pair, "NewCompany", DemoMode.T("demo.end.new_company"), RunEndIconKind.Restart, useFont, UIColors.PaperHi,
                                                240f, 58f, 24f, false, new Vector2(4f, 5f), out TextMeshProUGUI newCompany, 18f, 24f);
        L10nText.Attach(newCompany, DemoMode.Table, "demo.end.new_company");
        BlueprintUI.Layout(newCompanyButton, -1f, 58f, 1f);

        menuButton = RunEndUI.LinkButton(right, "MainMenu", DemoMode.T("demo.end.menu"), RunEndIconKind.Home, useFont, 24f, 44f, out TextMeshProUGUI menu);
        L10nText.Attach(menu, DemoMode.Table, "demo.end.menu");
        BlueprintUI.Layout(menuButton, -1f, 44f);
    }

    /// <summary>A one-line text in a column: shrinks to fit its width (down to <paramref name="min"/>).</summary>
    static TextMeshProUGUI Line(Transform parent, string name, float size, Color color, TMP_FontAsset useFont, bool bold, float height, float min)
    {
        TextMeshProUGUI text = BlueprintUI.Text(parent, name, "", size, color, useFont, TextAlignmentOptions.MidlineLeft, bold);
        RunEndUI.Shrink(text, min);
        BlueprintUI.Layout(text, -1f, height);
        return text;
    }

    /// <summary>A wrapped paragraph in a column (its height follows its lines).</summary>
    static TextMeshProUGUI Para(Transform parent, string name, float size, Color color, TMP_FontAsset useFont, bool bold)
    {
        TextMeshProUGUI text = BlueprintUI.Text(parent, name, "", size, color, useFont, TextAlignmentOptions.TopLeft, bold);
        RunEndUI.Wrapped(text, 1.3f);
        return text;
    }

    /// <summary>A ticked line of the "In the full game" list.</summary>
    static GameObject Item(Transform parent, string name, string key, float textWidth, TMP_FontAsset useFont)
    {
        RectTransform row = BlueprintUI.NewRect(name, parent);
        BlueprintUI.HRow(row, 10f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft);
        RunEndIcon tick = RunEndIcon.Create(row, RunEndIconKind.Check, 22f, UIColors.Ink);
        BlueprintUI.Layout(tick, 22f, 22f);
        TextMeshProUGUI text = BlueprintUI.Text(row, "Text", "", 20f, UIColors.Ink, useFont, TextAlignmentOptions.MidlineLeft);
        RunEndUI.Wrapped(text, 1.25f);
        BlueprintUI.Layout(text, textWidth);
        L10nText.Attach(text, DemoMode.Table, key);
        return row.gameObject;
    }

    // A stamp's word shrinks inside the stamp's rect (another language's word may be wider than the one it was sized for)
    static void ShrinkStampLabel(RectTransform stampRect)
    {
        TextMeshProUGUI label = stampRect.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label == null) return;
        label.enableAutoSizing = true;
        label.fontSizeMax = label.fontSize;
        label.fontSizeMin = Mathf.Round(label.fontSize * 0.5f);
    }
}
