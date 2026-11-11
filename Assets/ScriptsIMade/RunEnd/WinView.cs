using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The $10B win (design: Win.dc.html): a HIGH ROLLER magazine cover with cover lines made from the run's stats,
/// "&lt;COMPANY&gt; HITS $10 BILLION", a CEO OF THE YEAR seal and the strip "IS GAMBLING BAD FOR BUSINESS? NOT FOR THEM.";
/// beside it the result sheet ("You made it.", "&lt;Company&gt; is worth $…", "The run keeps going if you want. It's your
/// company.") with Keep playing / Start a new company / Main menu, a WINNER stamp and falling confetti.
/// Pausing; Escape = Keep playing. RunEndDirector shows it once per run and handles the buttons.
/// With Root empty it is built at runtime (Build) on its own overlay canvas; a designed prefab can fill the references.
/// Texts come from the "RunEnd" String Table (runend.win.*); a runtime screen built in another language is built again
/// when it next shows, and the screen on show is filled again when the language changes.
/// </summary>
[DisallowMultipleComponent]
public class WinView : MonoBehaviour
{
    public const int CoverLineCount = 5;
    const string T = RunEndUI.Table;

    [Header("Look")]
    [Tooltip("Font (empty = the Coming Soon font the HUD uses, else TextMesh Pro's default)")]
    public TMP_FontAsset font;
    [Tooltip("Sorting order of the runtime canvas: above everything but the toasts (900)")]
    public int sortingOrder = 130;
    [Tooltip("Starting capital printed on the cover (\"$50,000 to start\")")]
    public double startingValue = 50000;
    // Demo: the demo company's start is printed instead
    double PrintedStart => DemoMode.Active ? DemoMode.StartingValue : startingValue;
    [Tooltip("Real seconds the confetti takes to fall into place")]
    public float confettiFallSeconds = 1.1f;

    [Header("Cover (leave Root empty to build it at runtime)")]
    public RectTransform root;
    [Tooltip("\"Day 164 issue · $50,000 to start\"")]
    public TMP_Text issueText;
    [Tooltip("Five cover lines built from the run's stats (a TextHighlighter on a line colours its <link=\"hl\"> part)")]
    public TMP_Text[] coverLines = new TMP_Text[CoverLineCount];
    public TMP_Text coverCompanyText;
    [Tooltip("\"$10 BILLION\"")]
    public TMP_Text coverAmountText;

    [Header("Result sheet")]
    [Tooltip("\"<Company> is worth\"")]
    public TMP_Text worthText;
    [Tooltip("\"$10,000,000,000.\"")]
    public TMP_Text worthValueText;
    public Button keepPlayingButton;
    public Button newCompanyButton;
    public Button mainMenuButton;

    [Header("Confetti")]
    [Tooltip("Pieces that fall into place when the screen opens")]
    public RectTransform[] confetti = new RectTransform[0];

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    bool hooked;
    Vector2[] confettiHome;
    float[] confettiAngle, confettiDrop, confettiSpin, confettiDelay;
    float confettiClock;
    bool confettiFalling;
    Canvas runtimeCanvas;
    string builtLanguage;
    bool fontsFollowLanguage;
    RunSummary shownSummary;
    double shownWinValue;

    public bool IsOpen { get; private set; }

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

    // The fixed texts are L10nText and the fonts L10nFont; the cover and the sheet are filled again
    void OnLanguageChanged()
    {
        if (IsOpen && shownSummary != null) Fill(shownSummary, shownWinValue);
    }

    void OnDestroy()
    {
        if (UIManager.Exists) UIManager.Instance.ClosePanel(this);
    }

    void Update()
    {
        if (!IsOpen) return;
        // UI state was reset (e.g. by a debug tool) while the screen is up: keep the game paused
        if (UIManager.Exists && !UIManager.Instance.IsPanelOpen(this))
            UIManager.Instance.OpenPanel(this, true, OnKeepPlaying);
        if (confettiFalling) AnimateConfetti(Time.unscaledDeltaTime);
    }

    public void Show(RunSummary summary, double winValue)
    {
        EnsureBuilt();
        Fill(summary ?? new RunSummary(), winValue);
        root.gameObject.SetActive(true);
        UIManager.BringToFront(root.gameObject);
        IsOpen = true;
        StartConfetti();
        RunEndUI.ClearSelection();
        UIManager ui = UIManager.Instance;
        if (ui != null) ui.OpenPanel(this, true, OnKeepPlaying);
    }

    public void Close()
    {
        confettiFalling = false;
        if (root != null) root.gameObject.SetActive(false);
        if (!IsOpen) return;
        IsOpen = false;
        if (UIManager.Exists) UIManager.Instance.ClosePanel(this);
    }

    void EnsureBuilt()
    {
        if (root == null || runtimeCanvas != null)
        {
            // Built at runtime in the current language (again, when it was built in another: its layout follows its words)
            bool built = RunEndUI.EnsureBuiltInLanguage(ref runtimeCanvas, ref builtLanguage, IsOpen, () =>
            {
                Canvas canvas = RunEndUI.CreateCanvas("WinCanvas (Runtime)", sortingOrder, transform);
                Build(this, (RectTransform)canvas.transform, RunEndUI.ResolveFont(font));
                return canvas;
            });
            if (built)
            {
                hooked = fontsFollowLanguage = false;
                confettiHome = null;   // new confetti pieces
            }
        }
        if (root != null && !fontsFollowLanguage)
        {
            L10nFont.AttachAll(root);   // every text of the cover and the sheet takes the language's font
            fontsFollowLanguage = true;
        }
        if (hooked) return;
        hooked = true;
        if (keepPlayingButton != null) keepPlayingButton.onClick.AddListener(OnKeepPlaying);
        if (newCompanyButton != null) newCompanyButton.onClick.AddListener(OnNewCompany);
        if (mainMenuButton != null) mainMenuButton.onClick.AddListener(OnMainMenu);
    }

    void OnKeepPlaying()
    {
        RunEndDirector director = RunEndDirector.Instance;
        if (director != null) director.KeepPlaying();
        else Close();
    }

    void OnNewCompany()
    {
        RunEndDirector director = RunEndDirector.Instance;
        if (director != null) director.StartNewCompany();
    }

    void OnMainMenu()
    {
        RunEndDirector director = RunEndDirector.Instance;
        if (director != null) director.GoToMainMenu();
        else SceneFlow.Load(SceneFlow.MainMenuScene);
    }

    // ------------------------------------------------------------------
    // Texts
    // ------------------------------------------------------------------

    void Fill(RunSummary run, double winValue)
    {
        shownSummary = run;
        shownWinValue = winValue;
        string company = string.IsNullOrWhiteSpace(run.companyName) ? L10n.Get(T, "runend.company.fallback") : CompanyNameUI.DisplayName(run.companyName);   // the default name in the player's language
        if (issueText != null)
            issueText.text = L10n.Get(T, "runend.win.issue", ("day", UIFormat.Int(run.day)), ("amount", UIFormat.Money(PrintedStart)));
        // The player's company name in capitals, by the language's rules (Turkish i -> İ)
        if (coverCompanyText != null) coverCompanyText.text = L10n.ToUpper(company);
        if (coverAmountText != null) coverAmountText.text = RunEndUI.Mark(BillionsText(winValue));
        if (worthText != null) worthText.text = L10n.Get(T, "runend.win.worth", ("company", company));
        if (worthValueText != null) worthValueText.text = L10n.Get(T, "runend.win.worth.value", ("amount", RunEndUI.Mark(UIFormat.Money(run.companyValue))));

        string[] lines = CoverLines(run);
        if (coverLines == null) return;
        for (int i = 0; i < coverLines.Length && i < lines.Length; i++)
            if (coverLines[i] != null) coverLines[i].text = lines[i];
    }

    /// <summary>
    /// "$10 BILLION" (the win value in billions); Chinese, Japanese and Korean count it in hundreds of millions
    /// (100亿美元, 100億ドル, 100억 달러: runend.win.cover.amount.cjk).
    /// </summary>
    static string BillionsText(double value)
    {
        if (value < 1e9) return UIFormat.Money(value);
        return L10n.IsCjk
            ? L10n.Get(T, "runend.win.cover.amount.cjk", ("amount", (value / 1e8).ToString("0.#", Inv)))
            : L10n.Get(T, "runend.win.cover.amount", ("amount", (value / 1e9).ToString("0.##", Inv)));
    }

    /// <summary>
    /// The five cover lines (rich text; the part in &lt;link="hl"&gt; gets the line's highlighter colour):
    /// days, hires and golden hires, biggest risk-card win, casino net, crises solved.
    /// </summary>
    public string[] CoverLines(RunSummary run)
    {
        string gold = RunEndUI.Hex(UIColors.Gold);
        string ink = RunEndUI.Hex(UIColors.Ink);
        var lines = new string[CoverLineCount];

        // Each line is one String Table sentence; its highlighted or underlined part is styled here and passed in
        lines[0] = L10n.Get(T, "runend.win.line.days", ("amount", UIFormat.Money(PrintedStart)),
                            ("days", RunEndUI.Mark(L10n.Get(T, "runend.win.line.days.count", ("count", run.day)))));

        if (run.hires <= 0)
            lines[1] = L10n.Get(T, "runend.win.line.hires.none", ("mark", Underline(L10n.Get(T, "runend.win.line.hires.none.mark"), gold)));
        else if (run.goldenHires <= 0)
            lines[1] = L10n.Get(T, "runend.win.line.hires.no_golden", ("count", run.hires),
                                ("mark", Underline(L10n.Get(T, "runend.win.line.hires.no_golden.mark"), gold)));
        else
            lines[1] = L10n.Get(T, "runend.win.line.hires.golden", ("count", run.hires),
                                ("mark", Underline(L10n.Get(T, "runend.win.line.hires.golden.mark", ("golden", UIFormat.Int(run.goldenHires))), gold)));

        if (run.biggestWin > 0.5)
            // The card's title in the current language (by its saved key; an older save finds it by the English title)
            lines[2] = L10n.Get(T, "runend.win.line.biggest_win", ("title", RunStats.CardTitle(run.biggestWinTitle, run.biggestWinCard)),
                                ("amount", RunEndUI.Mark(RunEndUI.CompactMoney(run.biggestWin, true))));
        else if (run.riskCardsPlayed > 0)
            lines[2] = L10n.Get(T, "runend.win.line.cards_unpaid", ("count", RunEndUI.Mark(UIFormat.Int(run.riskCardsPlayed))));
        else
            lines[2] = RunEndUI.Mark(L10n.Get(T, "runend.win.line.no_cards"));

        lines[3] = run.hasGambled
            ? L10n.Get(T, "runend.win.line.casino", ("amount", RunEndUI.CompactMoney(run.casinoNet, true)),
                       ("aside", RunEndUI.Mark(L10n.Get(T, "runend.win.line.casino.aside"))))
            : L10n.Get(T, "runend.win.line.casino.never", ("never", RunEndUI.Mark(L10n.Get(T, "runend.win.line.casino.never.mark"))));

        lines[4] = run.crisesTotal > 0
            ? L10n.Get(T, "runend.win.line.crises", ("mark", Underline(L10n.Get(T, "runend.win.line.crises.mark", ("solved", UIFormat.Int(run.crisesSolved)),
                                                                                  ("total", UIFormat.Int(run.crisesTotal))), ink)))
            : L10n.Get(T, "runend.win.line.crises.none", ("mark", Underline(L10n.Get(T, "runend.win.line.crises.none.mark"), ink)));
        return lines;
    }

    /// <summary>Words underlined in a colour (the cover's pen strokes).</summary>
    static string Underline(string words, string hexColor)
    {
        return "<u color=" + hexColor + ">" + words + "</u>";
    }

    // ------------------------------------------------------------------
    // Confetti
    // ------------------------------------------------------------------

    void StartConfetti()
    {
        if (confetti == null || confetti.Length == 0) return;
        int n = confetti.Length;
        if (confettiHome == null || confettiHome.Length != n)
        {
            confettiHome = new Vector2[n];
            confettiAngle = new float[n];
            confettiDrop = new float[n];
            confettiSpin = new float[n];
            confettiDelay = new float[n];
            for (int i = 0; i < n; i++)
            {
                if (confetti[i] == null) continue;
                confettiHome[i] = confetti[i].anchoredPosition;
                confettiAngle[i] = confetti[i].localEulerAngles.z;
            }
        }
        for (int i = 0; i < n; i++)
        {
            confettiDrop[i] = Random.Range(260f, 720f);
            confettiSpin[i] = Random.Range(-540f, 540f);
            confettiDelay[i] = Random.Range(0f, 0.35f);
        }
        confettiClock = 0f;
        confettiFalling = true;
        AnimateConfetti(0f);
    }

    void AnimateConfetti(float dt)
    {
        confettiClock += dt;
        float duration = Mathf.Max(0.1f, confettiFallSeconds);
        bool done = true;
        for (int i = 0; i < confetti.Length; i++)
        {
            RectTransform piece = confetti[i];
            if (piece == null) continue;
            float p = Mathf.Clamp01((confettiClock - confettiDelay[i]) / duration);
            if (p < 1f) done = false;
            float eased = 1f - (1f - p) * (1f - p) * (1f - p);
            piece.anchoredPosition = confettiHome[i] + new Vector2(0f, (1f - eased) * confettiDrop[i]);
            piece.localRotation = Quaternion.Euler(0f, 0f, confettiAngle[i] + (1f - eased) * confettiSpin[i]);
        }
        if (done) confettiFalling = false;
    }

    // ------------------------------------------------------------------
    // Runtime layout (the design at 1920 x 1080)
    // ------------------------------------------------------------------

    static readonly Vector4 CoverRadii = BlueprintUI.RadiusSmall;   // 10 14 9 15
    static readonly Color PhotoWash = new Color(29f / 255f, 36f / 255f, 51f / 255f, 0.04f);

    // [left, top, width, height, css rotation, colour index] from the design; colours: yellow, gold, blue, pink, green
    static readonly float[,] ConfettiPieces =
    {
        { 58, 64, 18, 32, -24, 0 }, { 156, 150, 16, 28, 38, 1 }, { 40, 262, 22, 14, 12, 2 },
        { 128, 356, 14, 30, -52, 3 }, { 70, 470, 20, 20, 30, 4 }, { 168, 566, 16, 32, 64, 1 },
        { 34, 660, 22, 16, -18, 0 }, { 120, 770, 14, 28, 22, 2 }, { 182, 872, 18, 30, -40, 1 },
        { 60, 968, 20, 14, 48, 3 },
        { 1000, 40, 16, 30, 48, 3 }, { 1096, 150, 20, 14, -10, 4 }, { 1188, 70, 18, 32, 26, 1 },
        { 1290, 176, 14, 26, -70, 0 }, { 1382, 44, 22, 16, -34, 2 }, { 1478, 132, 16, 30, 16, 1 },
        { 1576, 60, 20, 20, 44, 3 }, { 1668, 168, 14, 28, -60, 4 }, { 1760, 40, 18, 34, 20, 1 },
        { 1858, 124, 22, 14, -8, 0 },
        { 1712, 284, 16, 30, 70, 1 }, { 1846, 372, 18, 26, -28, 2 }, { 1734, 470, 20, 16, 34, 3 },
        { 1862, 574, 14, 30, -14, 1 }, { 1700, 680, 22, 14, 52, 0 }, { 1830, 790, 16, 28, -36, 4 },
        { 1070, 932, 14, 26, -70, 1 }, { 1188, 1020, 18, 30, 18, 0 }, { 1316, 900, 14, 24, -20, 1 },
        { 1444, 996, 20, 14, 10, 2 }, { 1570, 920, 16, 30, 40, 3 }, { 1706, 1010, 18, 18, -30, 4 },
        { 1834, 930, 16, 32, 58, 1 },
        { 996, 600, 16, 26, 28, 0 }, { 1004, 780, 14, 22, -44, 1 },
        { 420, 14, 18, 28, 32, 1 }, { 760, 10, 20, 14, -22, 2 },
        { 470, 1040, 16, 28, -16, 0 }, { 846, 1036, 20, 16, 40, 1 }
    };

    static Color ConfettiColor(int index)
    {
        switch (index)
        {
            case 0: return UIColors.StickyYellow;
            case 1: return UIColors.Gold;
            case 2: return UIColors.StickyBlue;
            case 3: return UIColors.StickyPink;
            default: return UIColors.StickyGreen;
        }
    }

    static void Build(WinView view, RectTransform parent, TMP_FontAsset font)
    {
        // Built inactive, so MarkerButton and TextHighlighter start with their references set
        RectTransform root = BlueprintUI.Stretch(BlueprintUI.NewRect("Win", parent));
        root.gameObject.SetActive(false);
        view.root = root;
        RunEndUI.Scrim(root, UIColors.Scrim);

        BuildCover(view, root, font);
        BuildSheet(view, root, font);
        BuildConfetti(view, root);
    }

    // HIGH ROLLER cover: 700 x 940 at 250, 70, rotated -2 deg; children placed inside its 3 px border (694 x 934)
    static void BuildCover(WinView view, RectTransform root, TMP_FontAsset font)
    {
        BlueprintBox cover = BlueprintUI.Box(root, "Cover", UIColors.PaperHi, UIColors.Ink, 3f, CoverRadii, true);
        cover.SetShadow(RunEndUI.SheetShadow, new Vector2(0f, 22f), 40f);
        RectTransform coverRect = cover.rectTransform;
        RunEndUI.PlaceCenter(coverRect, 250f + 350f, 70f + 470f);
        coverRect.sizeDelta = new Vector2(700f, 940f);
        coverRect.localRotation = RunEndUI.Rotation(-2f);
        RectTransform c = RunEndUI.Inner(coverRect, 3f);

        // Gold frame with thin dark-gold lines on both sides
        BlueprintBox frame = BlueprintUI.Box(c, "Frame", Color.clear, UIColors.Gold, 3f, BlueprintUI.Round(4f));
        frame.SetOutline(UIColors.GoldDark, 1f, 0f);
        BlueprintUI.TopLeft(frame.rectTransform, 14f, 14f, 666f, 906f);
        BlueprintBox frameInner = BlueprintUI.Box(c, "FrameInner", Color.clear, UIColors.GoldDark, 1f, BlueprintUI.Round(2f));
        BlueprintUI.TopLeft(frameInner.rectTransform, 17f, 17f, 660f, 900f);

        TextMeshProUGUI issue = RunEndUI.Shrink(RunEndUI.Text(c, "Issue", "", 19f, UIColors.Pencil, font, 36f, 30f, 622f, 24f, TextAlignmentOptions.Midline), 14f);
        issue.characterSpacing = 6f;
        view.issueText = issue;

        // Masthead, spread to the cover's width like the design's textLength (for its words in the current language)
        string title = L10n.Get(T, "runend.win.masthead");   // "HIGH ROLLER"
        TextMeshProUGUI masthead = RunEndUI.Text(c, "Masthead", title, 100f, UIColors.Ink, font, 36f, 54f, 622f, 104f,
                                                 TextAlignmentOptions.Midline, true);
        float natural = RunEndUI.TextWidth(font, title, 100f, true);
        int gaps = Mathf.Max(1, new System.Globalization.StringInfo(title).LengthInTextElements - 1);
        if (natural > 614f) RunEndUI.Shrink(masthead, 50f);
        else if (natural > 1f) masthead.characterSpacing = Mathf.Min(40f, (614f - natural) / gaps);   // 1 unit = 1% of 100 px

        BlueprintBox rule = BlueprintUI.Box(c, "GoldRule", UIColors.Ink, Color.clear, 0f, BlueprintUI.Round(0f));
        BlueprintUI.TopLeft(rule.rectTransform, 36f, 158f, 622f, 10f);
        BlueprintBox ruleGold = BlueprintUI.Box(rule.transform, "Gold", UIColors.Gold, Color.clear, 0f, BlueprintUI.Round(0f));
        BlueprintUI.Stretch(ruleGold.rectTransform, 0f, 2f, 0f, 2f);

        L10nText.Attach(RunEndUI.Shrink(RunEndUI.Text(c, "Tagline", "", 19f, UIColors.InkSoft, font, 36f, 176f, 622f, 26f, TextAlignmentOptions.Midline), 14f),
                        T, "runend.win.tagline");   // "The magazine for people who bet the company."

        // Cover photo placeholder with the CEO silhouette
        BlueprintBox photo = BlueprintUI.Box(c, "Photo", PhotoWash, UIColors.Pencil, 3f, CoverRadii);
        photo.SetDash(true, 9f, 6f);
        BlueprintUI.TopLeft(photo.rectTransform, 36f, 218f, 622f, 616f);
        RunEndIcon person = RunEndIcon.Create(c, RunEndIconKind.Person, 180f, UIColors.Pencil, 1.3f);
        BlueprintUI.TopLeft(person.rectTransform, 400f, 330f, 180f, 180f);
        L10nText.Attach(RunEndUI.Shrink(RunEndUI.Text(c, "PhotoCaption", "", 19f, UIColors.Pencil, font, 380f, 520f, 220f, 26f, TextAlignmentOptions.Midline), 13f),
                        T, "runend.win.photo_caption");   // "Cover photo: the CEO"

        // Cover lines (22 px, 30 px lines, 18 px apart)
        RectTransform list = BlueprintUI.NewRect("CoverLines", c);
        BlueprintUI.TopLeft(list, 54f, 240f, 310f, 360f);
        VerticalLayoutGroup column = BlueprintUI.VColumn(list, 18f, new RectOffset(0, 0, 0, 0), TextAnchor.UpperLeft);
        column.childForceExpandHeight = false;
        Color[] marks = { RunEndUI.MarkYellow, Color.clear, RunEndUI.MarkGreen, RunEndUI.MarkPink, Color.clear };
        view.coverLines = new TMP_Text[CoverLineCount];
        for (int i = 0; i < CoverLineCount; i++)
        {
            TextMeshProUGUI line = BlueprintUI.Text(list, "Line " + (i + 1), "", 22f, UIColors.Ink, font, TextAlignmentOptions.TopLeft);
            RunEndUI.Wrapped(line, 30f / 22f);
            if (marks[i].a > 0f) RunEndUI.Highlight(line, marks[i]);
            view.coverLines[i] = line;
        }

        // Headline: COMPANY / HITS / $10 BILLION (highlighted, tilted -1.5 deg around its left edge)
        view.coverCompanyText = RunEndUI.Shrink(RunEndUI.Text(c, "Company", "", 60f, UIColors.Ink, font, 54f, 610f, 604f, 66f,
                                                              TextAlignmentOptions.MidlineLeft, true), 30f);
        L10nText.Attach(RunEndUI.Shrink(RunEndUI.Text(c, "Hits", "", 40f, UIColors.Ink, font, 54f, 676f, 604f, 44f, TextAlignmentOptions.MidlineLeft, true), 24f),
                        T, "runend.win.cover.hits");   // "HITS"
        // The view writes the amount (the win value)
        TextMeshProUGUI amount = RunEndUI.Text(c, "Amount", "", 84f, UIColors.Ink, font, 68f, 720f, 576f, 92f,
                                               TextAlignmentOptions.MidlineLeft, true);
        RectTransform amountRect = amount.rectTransform;
        amountRect.pivot = new Vector2(0f, 0.5f);
        amountRect.anchoredPosition = new Vector2(68f, -(720f + 46f));
        amountRect.localRotation = RunEndUI.Rotation(-1.5f);
        RunEndUI.Shrink(amount, 48f);
        RunEndUI.Highlight(amount, RunEndUI.MarkYellow, 0.14f, 0.88f, 14f);
        view.coverAmountText = amount;

        // Bottom strip on felt between gold lines
        BlueprintBox strip = BlueprintUI.Box(c, "Strip", UIColors.Felt, Color.clear, 0f, BlueprintUI.Round(0f));
        BlueprintUI.TopLeft(strip.rectTransform, 18f, 858f, 658f, 58f);
        BlueprintBox stripTop = BlueprintUI.Box(strip.transform, "GoldTop", UIColors.Gold, Color.clear, 0f, BlueprintUI.Round(0f));
        BlueprintUI.TopLeft(stripTop.rectTransform, 0f, 0f, 658f, 3f);
        BlueprintBox stripBottom = BlueprintUI.Box(strip.transform, "GoldBottom", UIColors.Gold, Color.clear, 0f, BlueprintUI.Round(0f));
        BlueprintUI.TopLeft(stripBottom.rectTransform, 0f, 55f, 658f, 3f);
        TextMeshProUGUI strapline = RunEndUI.Text(strip.transform, "Text", "", 19f, UIColors.PaperHi, font, 0f, 0f, 658f, 58f, TextAlignmentOptions.Midline, true);
        strapline.characterSpacing = 8f;
        RunEndUI.Shrink(strapline, 14f);
        // "IS GAMBLING BAD FOR BUSINESS? NOT FOR THEM." with the answer in highlighter yellow
        L10nText.Attach(strapline, T, "runend.win.strap", () => new object[]
        {
            L10n.Args(("answer", "<color=" + RunEndUI.Hex(UIColors.Highlight) + ">" + L10n.Get(T, "runend.win.strap.answer") + "</color>"))
        });

        // CEO OF THE YEAR seal over the photo's top-right corner, rotated -6 deg: three stacked lines
        RectTransform seal = BlueprintUI.NewRect("Seal", c);
        RunEndUI.PlaceCenter(seal, 548f + 95f, 206f + 115f);
        seal.sizeDelta = new Vector2(190f, 230f);
        seal.localRotation = RunEndUI.Rotation(-6f);
        RunEndDecor.Create(seal, "Rosette", RunEndDecorKind.Seal, Color.clear, Color.clear, 0f, Vector4.zero);
        L10nText.Attach(RunEndUI.Shrink(RunEndUI.Text(seal, "CEO", "", 32f, UIColors.Ink, font, 20f, 47f, 150f, 34f, TextAlignmentOptions.Midline, true), 18f),
                        T, "runend.win.seal.line1");   // "CEO"
        TextMeshProUGUI ofThe = RunEndUI.Shrink(RunEndUI.Text(seal, "OfThe", "", 17f, UIColors.Ink, font, 20f, 81f, 150f, 20f, TextAlignmentOptions.Midline, true), 11f);
        ofThe.characterSpacing = 10f;
        L10nText.Attach(ofThe, T, "runend.win.seal.line2");   // "OF THE"
        L10nText.Attach(RunEndUI.Shrink(RunEndUI.Text(seal, "Year", "", 24f, UIColors.Ink, font, 20f, 101f, 150f, 28f, TextAlignmentOptions.Midline, true), 14f),
                        T, "runend.win.seal.line3");   // "YEAR"
    }

    // Result sheet: 620 x 616 at 1040, 236 (padding 44 48)
    static void BuildSheet(WinView view, RectTransform root, TMP_FontAsset font)
    {
        BlueprintBox sheet = RunEndUI.Sheet(root, "Sheet", UIColors.Paper, BlueprintUI.RadiusCard, 1040f, 236f, 620f, 616f);
        RectTransform s = RunEndUI.Inner(sheet.transform, 3f);
        const float x = 48f, w = 524f;

        L10nText.Attach(RunEndUI.Shrink(RunEndUI.Text(s, "Title", "", 60f, UIColors.Ink, font, x, 44f, w, 69f, TextAlignmentOptions.MidlineLeft, true), 36f),
                        T, "runend.win.title");   // "You made it."
        // The view writes the company's worth (two lines of one sentence)
        view.worthText = RunEndUI.Shrink(RunEndUI.Text(s, "Worth", "", 26f, UIColors.Ink, font, x, 125f, w, 36f), 18f);
        TextMeshProUGUI value = RunEndUI.Text(s, "WorthValue", "", 26f, UIColors.Ink, font, x + 6f, 161f, w - 6f, 36f);
        RunEndUI.Shrink(value, 18f);
        RunEndUI.Highlight(value, RunEndUI.MarkYellow);
        view.worthValueText = value;
        L10nText.Attach(RunEndUI.Shrink(RunEndUI.Text(s, "KeepGoing", "", 19f, UIColors.Pencil, font, x, 209f, w, 27f), 14f),
                        T, "runend.win.keep_going");   // "The run keeps going if you want."
        L10nText.Attach(RunEndUI.Shrink(RunEndUI.Text(s, "YourCompany", "", 19f, UIColors.Pencil, font, x, 236f, w, 27f), 14f),
                        T, "runend.win.your_company");   // "It's your company."

        view.keepPlayingButton = RunEndUI.PaperButton(s, "KeepPlaying", "", RunEndIconKind.Play, font, UIColors.Highlight,
                                                      420f, 80f, 32f, true, new Vector2(5f, 6f), out TextMeshProUGUI keepPlaying);
        L10nText.Attach(keepPlaying, T, "runend.win.button.keep_playing");   // "Keep playing"
        BlueprintUI.TopLeft((RectTransform)view.keepPlayingButton.transform, x, 295f, 420f, 80f);
        view.newCompanyButton = RunEndUI.PaperButton(s, "NewCompany", "", RunEndIconKind.Restart, font, UIColors.PaperHi,
                                                     420f, 68f, 28f, false, new Vector2(4f, 5f), out TextMeshProUGUI newCompany);
        L10nText.Attach(newCompany, T, "runend.button.new_company");   // "Start a new company"
        BlueprintUI.TopLeft((RectTransform)view.newCompanyButton.transform, x, 399f, 420f, 68f);
        view.mainMenuButton = RunEndUI.LinkButton(s, "MainMenu", L10n.Get(T, "runend.button.main_menu"), RunEndIconKind.Home, font, 26f, 52f,
                                                  out TextMeshProUGUI mainMenu);
        L10nText.Attach(mainMenu, T, "runend.button.main_menu");   // "Main menu"
        RectTransform menu = (RectTransform)view.mainMenuButton.transform;
        BlueprintUI.TopLeft(menu, x, 491f, menu.sizeDelta.x, 52f);

        RectTransform winner = RunEndUI.Stamp(s, "Winner", L10n.Get(T, "runend.win.stamp"), 44f, UIColors.Money, font, 5f, 2f, 4f, 10f, 0.12f, 12f, 0f, -10f);
        winner.sizeDelta = new Vector2(Mathf.Max(276f, winner.sizeDelta.x), 72f);
        RunEndUI.PlaceCenter(winner, 314f + winner.sizeDelta.x * 0.5f, 488f + 36f);
    }

    static void BuildConfetti(WinView view, RectTransform root)
    {
        int n = ConfettiPieces.GetLength(0);
        view.confetti = new RectTransform[n];
        for (int i = 0; i < n; i++)
        {
            float w = ConfettiPieces[i, 2], h = ConfettiPieces[i, 3];
            BlueprintBox piece = BlueprintUI.Box(root, "Confetti " + (i + 1), ConfettiColor((int)ConfettiPieces[i, 5]), UIColors.Ink, 2f,
                                                 BlueprintUI.Round(0f));
            piece.SetShadow(new Color(0f, 0f, 0f, 0.25f), new Vector2(2f, 3f));
            RectTransform rect = piece.rectTransform;
            RunEndUI.PlaceCenter(rect, ConfettiPieces[i, 0] + w * 0.5f, ConfettiPieces[i, 1] + h * 0.5f);
            rect.sizeDelta = new Vector2(w, h);
            rect.localRotation = RunEndUI.Rotation(ConfettiPieces[i, 4]);
            view.confetti[i] = rect;
        }
    }
}
