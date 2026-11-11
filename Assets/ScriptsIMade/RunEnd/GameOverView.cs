using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Game over (design: GameOver.dc.html): a green-bar ledger sheet with a BANKRUPT stamp, "&lt;Company&gt; is out of
/// business.", "Was gambling bad for business? The ledger says yes." and the final statement (days in business, peak
/// value, people hired, risk cards played, biggest win, biggest loss, crises solved, casino net), with Start a new company
/// and Main menu. Pausing and full-screen; Escape does nothing. Both buttons go to the main menu (RunEndDirector), whose
/// New Game starts the next company; the save is already gone by the time this shows.
/// With Root empty it is built at runtime (Build) on its own overlay canvas; a designed prefab can fill the references.
/// Texts come from the "RunEnd" String Table (runend.gameover.*); a runtime sheet built in another language is built
/// again when it next shows, and the sheet on show is filled again when the language changes.
/// </summary>
[DisallowMultipleComponent]
public class GameOverView : MonoBehaviour
{
    /// <summary>Rows of the final statement, top to bottom (the order of rowLabels / rowNotes / rowValues).</summary>
    public const int RowCount = 8;
    /// <summary>The rows' labels in the String Table (runend.gameover.row.*).</summary>
    static readonly string[] RowKeys =
    {
        "runend.gameover.row.days", "runend.gameover.row.peak", "runend.gameover.row.hires", "runend.gameover.row.cards",
        "runend.gameover.row.biggest_win", "runend.gameover.row.biggest_loss", "runend.gameover.row.crises", "runend.gameover.row.casino"
    };
    const string T = RunEndUI.Table;

    [Header("Look")]
    [Tooltip("Font (empty = the Coming Soon font the HUD uses, else TextMesh Pro's default)")]
    public TMP_FontAsset font;
    [Tooltip("Sorting order of the runtime canvas: above everything but the toasts (900)")]
    public int sortingOrder = 130;

    [Header("Sheet (leave Root empty to build it at runtime)")]
    public RectTransform root;
    [Tooltip("\"<Company> is out of business.\"")]
    public TMP_Text headlineText;
    [Tooltip("Label, note and value texts of the final statement rows (8, top to bottom)")]
    public TMP_Text[] rowLabels = new TMP_Text[RowCount];
    public TMP_Text[] rowNotes = new TMP_Text[RowCount];
    public TMP_Text[] rowValues = new TMP_Text[RowCount];
    public Button newCompanyButton;
    public Button mainMenuButton;

    bool hooked;
    Canvas runtimeCanvas;
    string builtLanguage;
    bool fontsFollowLanguage;
    RunSummary shownSummary;

    public bool IsOpen { get; private set; }

    /// <summary>
    /// Demo: the sheet is on screen and filled (root exists now; RunEndDirector.RunEnded comes before it is built).
    /// DemoGameOverDecor adds its buttons under root from here.
    /// </summary>
    public static event System.Action<GameOverView> Shown;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Shown = null;   // Demo: no listeners left over from the last play session

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

    // The fixed texts are L10nText and the fonts L10nFont; the statement is filled again
    void OnLanguageChanged()
    {
        if (IsOpen && shownSummary != null) Fill(shownSummary);
    }

    void OnDestroy()
    {
        if (UIManager.Exists) UIManager.Instance.ClosePanel(this);
    }

    void Update()
    {
        // UI state was reset (e.g. by a debug tool) while the screen is up: keep the game paused
        if (IsOpen && UIManager.Exists && !UIManager.Instance.IsPanelOpen(this))
            UIManager.Instance.OpenPanel(this, true, SwallowEscape);
    }

    void SwallowEscape() { }

    public void Show(RunSummary summary)
    {
        EnsureBuilt();
        Fill(summary ?? new RunSummary());
        root.gameObject.SetActive(true);
        UIManager.BringToFront(root.gameObject);
        IsOpen = true;
        RunEndUI.ClearSelection();
        UIManager ui = UIManager.Instance;
        if (ui != null) ui.OpenPanel(this, true, SwallowEscape);
        RaiseShown();   // Demo
    }

    // Demo: each listener on its own, so a failing one never stops the game-over screen
    void RaiseShown()
    {
        System.Action<GameOverView> handlers = Shown;
        if (handlers == null) return;
        foreach (System.Action<GameOverView> handler in handlers.GetInvocationList())
        {
            try { handler(this); }
            catch (System.Exception e) { Debug.LogException(e); }
        }
    }

    public void Hide()
    {
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
                Canvas canvas = RunEndUI.CreateCanvas("GameOverCanvas (Runtime)", sortingOrder, transform);
                Build(this, (RectTransform)canvas.transform, RunEndUI.ResolveFont(font));
                return canvas;
            });
            if (built) hooked = fontsFollowLanguage = false;
        }
        if (root != null && !fontsFollowLanguage)
        {
            L10nFont.AttachAll(root);   // every text of the sheet takes the language's font
            fontsFollowLanguage = true;
        }
        if (hooked) return;
        hooked = true;
        if (newCompanyButton != null) newCompanyButton.onClick.AddListener(OnNewCompany);
        if (mainMenuButton != null) mainMenuButton.onClick.AddListener(OnMainMenu);
    }

    void OnNewCompany()
    {
        RunEndDirector director = RunEndDirector.Instance;
        if (director != null) director.StartNewCompany();
        else SceneFlow.Load(SceneFlow.MainMenuScene);
    }

    void OnMainMenu()
    {
        RunEndDirector director = RunEndDirector.Instance;
        if (director != null) director.GoToMainMenu();
        else SceneFlow.Load(SceneFlow.MainMenuScene);
    }

    void Fill(RunSummary run)
    {
        shownSummary = run;
        string company = string.IsNullOrWhiteSpace(run.companyName) ? L10n.Get(T, "runend.company.fallback") : CompanyNameUI.DisplayName(run.companyName);   // the default name in the player's language
        if (headlineText != null) headlineText.text = L10n.Get(T, "runend.gameover.headline", ("company", company));

        string none = L10n.Get(T, "runend.gameover.value.none");
        SetRow(0, "", UIFormat.Int(run.day), UIColors.Ink);
        SetRow(1, "", UIFormat.Money(run.peakValue), UIColors.Ink);
        SetRow(2, run.goldenHires > 0 ? L10n.Get(T, "runend.gameover.note.golden", ("count", UIFormat.Int(run.goldenHires))) : "", UIFormat.Int(run.hires), UIColors.Ink);
        SetRow(3, "", UIFormat.Int(run.riskCardsPlayed), UIColors.Ink);
        if (run.biggestWin > 0.5) SetRow(4, CardNote(run.biggestWinTitle, run.biggestWinCard), UIFormat.SignedMoney(run.biggestWin), UIColors.Money);
        else SetRow(4, "", none, UIColors.Pencil);
        if (run.biggestLoss < -0.5) SetRow(5, CardNote(run.biggestLossTitle, run.biggestLossCard), UIFormat.SignedMoney(run.biggestLoss), UIColors.Loss);
        else SetRow(5, "", none, UIColors.Pencil);
        SetRow(6, "", L10n.Get(T, "runend.gameover.value.crises", ("solved", UIFormat.Int(run.crisesSolved)), ("total", UIFormat.Int(run.crisesTotal))), UIColors.Ink);
        if (!run.hasGambled) SetRow(7, "", UIFormat.Money(0.0), UIColors.Ink);
        else SetRow(7, "", UIFormat.SignedMoney(run.casinoNet),
                    run.casinoNet > 0.5 ? UIColors.Money : run.casinoNet < -0.5 ? UIColors.Loss : UIColors.Ink);
    }

    /// <summary>"(Office Poker Night)": the risk card's title in the current language (RunStats.CardTitle) in brackets.</summary>
    static string CardNote(string savedTitle, string savedKey)
    {
        return L10n.Get(T, "runend.gameover.note.card", ("title", RunStats.CardTitle(savedTitle, savedKey)));
    }

    void SetRow(int index, string note, string value, Color valueColor)
    {
        // A designed sheet may bring its own labels; the runtime one keeps them on the String Table (L10nText)
        if (rowLabels != null && index < rowLabels.Length && rowLabels[index] != null && string.IsNullOrEmpty(rowLabels[index].text))
            rowLabels[index].text = L10n.Get(T, RowKeys[index]);
        if (rowNotes != null && index < rowNotes.Length && rowNotes[index] != null)
            rowNotes[index].text = note;
        if (rowValues != null && index < rowValues.Length && rowValues[index] != null)
        {
            rowValues[index].text = value;
            rowValues[index].color = valueColor;
        }
    }

    // ------------------------------------------------------------------
    // Runtime layout (the design at 1920 x 1080: a 900 x 840 ledger sheet at 510, 120)
    // ------------------------------------------------------------------

    static readonly Vector4 SheetRadii = BlueprintUI.RadiusCard;   // 18 26 20 30
    const float SheetX = 510f, SheetY = 120f, SheetWidth = 900f, SheetHeight = 840f, Border = 3f;
    const float Row = 34f, PadX = 66f, HeaderTop = 212f, HeaderHeight = 168f, Gap = 28f, ValueWidth = 200f;

    static void Build(GameOverView view, RectTransform parent, TMP_FontAsset font)
    {
        // Built inactive, so MarkerButton and TextHighlighter start with their references set
        RectTransform root = BlueprintUI.Stretch(BlueprintUI.NewRect("GameOver", parent));
        root.gameObject.SetActive(false);
        view.root = root;
        RunEndUI.Scrim(root, UIColors.Scrim);

        // Ledger paper: fill + shadow, bands and tractor feed inside the border, the border on top
        BlueprintBox sheet = BlueprintUI.Box(root, "Sheet", UIColors.Ledger, Color.clear, 0f, SheetRadii, true);
        sheet.SetShadow(RunEndUI.SheetShadow, new Vector2(0f, 22f), 40f);
        BlueprintUI.TopLeft(sheet.rectTransform, SheetX, SheetY, SheetWidth, SheetHeight);
        RectTransform inner = RunEndUI.Inner(sheet.transform, Border);
        Vector4 innerRadii = SheetRadii - new Vector4(Border, Border, Border, Border);
        RunEndDecor.Create(inner, "Bands", RunEndDecorKind.LedgerBands, UIColors.LedgerBand, Color.clear, Row, innerRadii);
        RunEndDecor.Create(inner, "TractorFeed", RunEndDecorKind.TractorFeed, RunEndUI.Desk, UIColors.LedgerLine, Row, innerRadii);

        float innerWidth = SheetWidth - Border * 2f;
        float contentWidth = innerWidth - PadX * 2f;

        // BANKRUPT stamp (top: 53, 92 px, 0.1 em spacing, 7 px border + 3 px outline 6 px out, rotated -8 deg), sized to its word
        RectTransform stamp = RunEndUI.Stamp(inner, "Bankrupt", L10n.Get(T, "runend.gameover.stamp"), 92f, UIColors.Loss, font, 7f, 3f, 6f, 12f, 0.1f, 27f, 0f, -8f);
        RunEndUI.PlaceCenter(stamp, innerWidth * 0.5f, 53f + stamp.sizeDelta.y * 0.5f);

        // Header: two lines centred in a 168 px block (the view writes the headline)
        float titleHeight = 51f, taglineHeight = 36f;
        float headerY = HeaderTop + (HeaderHeight - titleHeight - 12f - taglineHeight) * 0.5f;
        view.headlineText = RunEndUI.Shrink(RunEndUI.Text(inner, "Headline", "", 44f, UIColors.Ink, font,
                                                          PadX, headerY, contentWidth, titleHeight, TextAlignmentOptions.Midline, true), 28f);
        TextMeshProUGUI tagline = RunEndUI.Shrink(RunEndUI.Text(inner, "Tagline", "", 26f, UIColors.InkSoft, font, PadX, headerY + titleHeight + 12f,
                                                                contentWidth, taglineHeight, TextAlignmentOptions.Midline), 18f);
        // "Was gambling bad for business? The ledger says yes." with the answer in ink under the highlighter
        L10nText.Attach(tagline, T, "runend.gameover.tagline", () => new object[]
        {
            L10n.Args(("answer", RunEndUI.Mark("<color=" + RunEndUI.Hex(UIColors.Ink) + ">" + L10n.Get(T, "runend.gameover.tagline.answer") + "</color>")))
        });
        RunEndUI.Highlight(tagline, RunEndUI.MarkYellow);

        // Final statement: 8 rows of 34 px on the ledger lines (label | note | value with a rule on its left)
        float tableY = HeaderTop + HeaderHeight + Gap;
        float valueX = PadX + contentWidth - ValueWidth;
        view.rowLabels = new TMP_Text[RowCount];
        view.rowNotes = new TMP_Text[RowCount];
        view.rowValues = new TMP_Text[RowCount];
        for (int i = 0; i < RowCount; i++)
        {
            float y = tableY + i * Row;
            TextMeshProUGUI label = RunEndUI.Shrink(RunEndUI.Text(inner, "Label " + (i + 1), "", 22f, UIColors.Ink, font, PadX, y, 250f, Row), 15f);
            L10nText.Attach(label, T, RowKeys[i]);   // "Days in business", ...
            view.rowLabels[i] = label;
            view.rowNotes[i] = RunEndUI.Shrink(RunEndUI.Text(inner, "Note " + (i + 1), "", 19f, UIColors.Pencil, font, PadX + 262f, y,
                                                             valueX - 16f - (PadX + 262f), Row, TextAlignmentOptions.MidlineRight), 14f);
            view.rowNotes[i].overflowMode = TextOverflowModes.Ellipsis;
            BlueprintBox rule = BlueprintUI.Box(inner, "Rule " + (i + 1), UIColors.LedgerLine, Color.clear, 0f, BlueprintUI.Round(0f));
            BlueprintUI.TopLeft(rule.rectTransform, valueX, y, 2f, Row);
            view.rowValues[i] = RunEndUI.Shrink(RunEndUI.Text(inner, "Value " + (i + 1), "", 24f, UIColors.Ink, font, valueX + 18f, y,
                                                              ValueWidth - 18f, Row, TextAlignmentOptions.MidlineRight, true), 16f);
        }

        // Buttons, centred
        float buttonsY = tableY + RowCount * Row + Gap;
        // Sized to their words in the current language (the sheet is built again for another one)
        view.newCompanyButton = RunEndUI.PaperButton(inner, "NewCompany", L10n.Get(T, "runend.button.new_company"), RunEndIconKind.Restart, font,
                                                     UIColors.Highlight, 0f, 68f, 28f, true, new Vector2(5f, 6f), out _);
        view.mainMenuButton = RunEndUI.PaperButton(inner, "MainMenu", L10n.Get(T, "runend.button.main_menu"), RunEndIconKind.Home, font, UIColors.PaperHi,
                                                   0f, 68f, 28f, false, new Vector2(4f, 5f), out _);
        RectTransform first = (RectTransform)view.newCompanyButton.transform;
        RectTransform second = (RectTransform)view.mainMenuButton.transform;
        float total = first.sizeDelta.x + 24f + second.sizeDelta.x;
        float x = (innerWidth - total) * 0.5f;
        BlueprintUI.TopLeft(first, x, buttonsY, first.sizeDelta.x, 68f);
        BlueprintUI.TopLeft(second, x + first.sizeDelta.x + 24f, buttonsY, second.sizeDelta.x, 68f);

        // The ink border goes over the bands and the tractor feed
        BlueprintBox border = BlueprintUI.Box(sheet.transform, "Border", Color.clear, UIColors.Ink, Border, SheetRadii);
        BlueprintUI.Stretch(border.rectTransform);
    }
}
