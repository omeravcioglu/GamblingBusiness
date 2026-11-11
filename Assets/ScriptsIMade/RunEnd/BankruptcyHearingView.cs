using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The bankruptcy hearing: a pausing paper sheet (UIManager.OpenPanel(this, true); Escape does nothing) with three
/// choices on sticky cards: take a bank loan, one last coin flip, close the company. The coin flip spins a gold coin on
/// real time, then a result slip says HEADS! (APPROVED) or TAILS. (DENIED).
/// RunEndDirector opens it and applies every choice (TakeLoan, RollCoinFlip / ApplyCoinFlip, CloseCompany).
/// With Root empty the sheet is built at runtime (Build) on its own overlay canvas; a designed prefab can fill the
/// references instead.
/// Texts come from the "RunEnd" String Table (runend.hearing.*). A runtime sheet built in another language is built again
/// at the next hearing; the one on show is filled again when the language changes.
/// </summary>
[DisallowMultipleComponent]
public class BankruptcyHearingView : MonoBehaviour
{
    const string T = RunEndUI.Table;

    [Header("Look")]
    [Tooltip("Font (empty = the Coming Soon font the HUD uses, else TextMesh Pro's default)")]
    public TMP_FontAsset font;
    [Tooltip("Sorting order of the runtime canvas: above the HUD's pop-ups, below the toasts (900)")]
    public int sortingOrder = 120;
    [Tooltip("Length of the coin flip (real seconds)")]
    public float coinFlipSeconds = 1.6f;
    [Tooltip("Half-turns the coin makes while it flips")]
    public int coinHalfTurns = 9;
    [Tooltip("How high the coin jumps (reference pixels)")]
    public float coinJump = 160f;
    [Tooltip("Pause after the coin lands before the result slip shows (real seconds)")]
    public float resultDelay = 0.4f;

    [Header("Sheet (leave Root empty to build it at runtime)")]
    public RectTransform root;
    public TMP_Text dayText;
    public TMP_Text titleText;
    public TMP_Text subtitleText;

    [Header("Take a bank loan")]
    public Button loanButton;
    public TMP_Text loanFigureText;
    public TMP_Text loanBodyText;
    public TMP_Text loanButtonText;

    [Header("One last coin flip")]
    public Button flipButton;
    public TMP_Text flipFigureText;
    public TMP_Text flipBodyText;

    [Header("Close the company")]
    public Button closeButton;
    public TMP_Text closeFigureText;
    public TMP_Text closeBodyText;

    [Header("Coin flip")]
    public RectTransform flipLayer;
    public RectTransform coin;
    public TMP_Text coinLetter;

    [Header("Result slip")]
    public RectTransform resultSlip;
    public TMP_Text resultTitleText;
    public TMP_Text resultBodyText;
    public GameObject approvedStamp;
    public GameObject deniedStamp;
    public Button resultButton;
    public TMP_Text resultButtonText;

    bool hooked;
    bool flipping;
    bool resultHeads;
    bool resultShown;
    bool coinShowsHeads = true;
    string headsLetter;
    string tailsLetter;
    Coroutine flipRoutine;
    Vector2 coinHome;
    bool coinHomeKnown;
    HearingInfo info = new HearingInfo();
    Canvas runtimeCanvas;
    string builtLanguage;
    bool fontsFollowLanguage;

    public bool IsOpen { get; private set; }
    /// <summary>The coin is in the air (or its result hasn't shown yet).</summary>
    public bool IsFlipping => flipping;

    void OnEnable()
    {
        L10n.LanguageChanged += OnLanguageChanged;
    }

    // The fixed texts are L10nText and the fonts L10nFont; the sheet (and a result on show) is filled again
    void OnLanguageChanged()
    {
        headsLetter = tailsLetter = null;
        if (!IsOpen) return;
        Fill();
        SetCoinFace(coinShowsHeads);
        if (resultShown) FillResult(resultHeads);
    }

    void OnDisable()
    {
        L10n.LanguageChanged -= OnLanguageChanged;
        StopFlip();
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

    void Update()
    {
        // UI state was reset (e.g. by a debug tool) while the hearing is up: keep the game paused
        if (IsOpen && UIManager.Exists && !UIManager.Instance.IsPanelOpen(this))
            UIManager.Instance.OpenPanel(this, true, SwallowEscape);
    }

    /// <summary>Escape does nothing: there is no walking away from a hearing (and no pause menu on top of it).</summary>
    void SwallowEscape() { }

    // ------------------------------------------------------------------
    // Open / close
    // ------------------------------------------------------------------

    public void Open(HearingInfo hearing)
    {
        info = hearing ?? new HearingInfo();
        StopFlip();
        EnsureBuilt();
        Fill();
        resultShown = false;
        if (flipLayer != null) flipLayer.gameObject.SetActive(false);
        if (resultSlip != null) resultSlip.gameObject.SetActive(false);
        SetChoicesEnabled(true);

        root.gameObject.SetActive(true);
        UIManager.BringToFront(root.gameObject);
        IsOpen = true;
        RunEndUI.ClearSelection();
        UIManager ui = UIManager.Instance;
        if (ui != null) ui.OpenPanel(this, true, SwallowEscape);
    }

    public void Close()
    {
        StopFlip();
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
                Canvas canvas = RunEndUI.CreateCanvas("BankruptcyHearingCanvas (Runtime)", sortingOrder, transform);
                Build(this, (RectTransform)canvas.transform, RunEndUI.ResolveFont(font));
                return canvas;
            });
            if (built) hooked = coinHomeKnown = fontsFollowLanguage = false;
        }
        if (root != null && !fontsFollowLanguage)
        {
            L10nFont.AttachAll(root);   // every text of the hearing takes the language's font
            fontsFollowLanguage = true;
        }
        if (coin != null && !coinHomeKnown)
        {
            coinHome = coin.anchoredPosition;
            coinHomeKnown = true;
        }
        Hook();
    }

    void Hook()
    {
        if (hooked) return;
        hooked = true;
        if (loanButton != null) loanButton.onClick.AddListener(OnLoan);
        if (flipButton != null) flipButton.onClick.AddListener(OnFlip);
        if (closeButton != null) closeButton.onClick.AddListener(OnCloseCompany);
        if (resultButton != null) resultButton.onClick.AddListener(OnResult);
    }

    void Fill()
    {
        string company = CompanyName();
        // The kicker is written in capitals in the String Table (no UpperCase style: TMP's ignores Turkish i / İ)
        if (dayText != null) dayText.text = L10n.Get(T, "runend.hearing.kicker", ("day", UIFormat.Int(info.day)));
        if (titleText != null) titleText.text = L10n.Get(T, "runend.hearing.title", ("company", company));
        if (subtitleText != null)
            subtitleText.text = L10n.Get(T, "runend.hearing.subtitle",
                                         ("amount", RunEndUI.Mark("<color=" + RunEndUI.Hex(UIColors.Ink) + ">" + UIFormat.Money(info.companyValue) + "</color>")));

        if (info.loanAvailable)
        {
            SetText(loanFigureText, "+" + UIFormat.Money(info.loanAmount), UIColors.Money);
            SetText(loanBodyText, LoanBody(info.dailyInterest, info.autoRepayShare), UIColors.InkSoft);
            SetText(loanButtonText, L10n.Get(T, "runend.hearing.loan.button"), UIColors.Ink);
        }
        else
        {
            SetText(loanFigureText, L10n.Get(T, "runend.hearing.loan.owed", ("amount", UIFormat.Money(info.debt))), UIColors.Loss);
            SetText(loanBodyText, L10n.Get(T, "runend.hearing.loan.none"), UIColors.InkSoft);
            SetText(loanButtonText, L10n.Get(T, "runend.hearing.loan.button.none"), UIColors.Ink);
        }

        int headsPercent = Mathf.RoundToInt(Mathf.Clamp01(info.headsChance) * 100f);
        SetText(flipFigureText, headsPercent == 50 ? L10n.Get(T, "runend.hearing.flip.figure.even")
                                                   : L10n.Get(T, "runend.hearing.flip.figure", ("percent", headsPercent.ToString(System.Globalization.CultureInfo.InvariantCulture))),
                UIColors.Ink);
        string restart = UIFormat.Money(info.restartValue);
        SetText(flipBodyText, info.debt > 0.0
            ? L10n.Get(T, "runend.hearing.flip.body.debt", ("debt", UIFormat.Money(info.debt)), ("restart", restart))
            : L10n.Get(T, "runend.hearing.flip.body", ("restart", restart)), UIColors.InkSoft);

        SetText(closeFigureText, UIFormat.Money(info.companyValue), UIColors.Loss);
        SetText(closeBodyText, L10n.Get(T, "runend.hearing.close.body"), UIColors.InkSoft);
    }

    string CompanyName()
    {
        return string.IsNullOrWhiteSpace(info.companyName) ? L10n.Get(T, "runend.company.fallback") : CompanyNameUI.DisplayName(info.companyName);   // the default name in the player's language
    }

    /// <summary>
    /// "The debt grows 3% a day. Half of each day's profit pays it back on its own." (a quarter, all, or a percentage: one
    /// sentence each, so every language words its share its own way).
    /// </summary>
    static string LoanBody(double dailyInterest, double repayShare)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string interest = (dailyInterest * 100.0).ToString("0.#", inv);
        if (System.Math.Abs(repayShare - 0.5) < 0.0001) return L10n.Get(T, "runend.hearing.loan.body.half", ("interest", interest));
        if (System.Math.Abs(repayShare - 0.25) < 0.0001) return L10n.Get(T, "runend.hearing.loan.body.quarter", ("interest", interest));
        if (repayShare >= 0.9999) return L10n.Get(T, "runend.hearing.loan.body.all", ("interest", interest));
        return L10n.Get(T, "runend.hearing.loan.body", ("interest", interest), ("share", (repayShare * 100.0).ToString("0.#", inv)));
    }

    static void SetText(TMP_Text text, string value, Color color)
    {
        if (text == null) return;
        text.text = value;
        text.color = color;
    }

    void SetChoicesEnabled(bool on)
    {
        RunEndUI.SetEnabled(loanButton, on && info.loanAvailable);
        RunEndUI.SetEnabled(flipButton, on);
        RunEndUI.SetEnabled(closeButton, on);
    }

    // ------------------------------------------------------------------
    // Choices
    // ------------------------------------------------------------------

    void OnLoan()
    {
        if (flipping || !info.loanAvailable) return;
        RunEndDirector director = RunEndDirector.Instance;
        if (director != null) director.TakeLoan();
    }

    void OnCloseCompany()
    {
        if (flipping) return;
        RunEndDirector director = RunEndDirector.Instance;
        if (director != null) director.CloseCompany();
    }

    void OnFlip()
    {
        if (flipping) return;
        RunEndDirector director = RunEndDirector.Instance;
        if (director == null) return;
        bool heads = director.RollCoinFlip();
        // Tails ends the run right away (the save is deleted before the coin lands), so quitting while the coin spins
        // can't undo it. Heads is applied when its face shows, so the counter behind doesn't give the result away.
        if (!heads) director.ApplyCoinFlip(false);
        SetChoicesEnabled(false);
        flipping = true;
        GameSfx.Play(Sfx.CoinFlick);
        flipRoutine = StartCoroutine(Flip(heads));
    }

    void OnResult()
    {
        if (flipping) return;
        RunEndDirector director = RunEndDirector.Instance;
        if (director == null) return;
        if (resultHeads) director.CloseHearing();
        else director.ShowGameOverView();
    }

    IEnumerator Flip(bool heads)
    {
        if (resultSlip != null) resultSlip.gameObject.SetActive(false);
        if (flipLayer != null)
        {
            flipLayer.gameObject.SetActive(true);
            flipLayer.SetAsLastSibling();
        }

        // Spin: squash the coin sideways, swapping faces at the thin moments (real time: the game is paused)
        float duration = Mathf.Max(0.3f, coinFlipSeconds);
        int halfTurns = Mathf.Max(1, coinHalfTurns);
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(t / duration);
            float eased = 1f - Mathf.Pow(1f - progress, 3f);
            float turns = eased * halfTurns;
            if (coin != null)
            {
                coin.localScale = new Vector3(Mathf.Max(0.04f, Mathf.Abs(Mathf.Cos(turns * Mathf.PI))), 1f, 1f);
                coin.anchoredPosition = coinHome + new Vector2(0f, Mathf.Sin(progress * Mathf.PI) * coinJump);
            }
            // The face that lands is the chosen one: count faces back from the last half-turn
            int face = Mathf.FloorToInt(turns + 0.5f);
            bool showHeads = ((halfTurns - face) % 2 == 0) == heads;
            SetCoinFace(showHeads);
            yield return null;
        }
        if (coin != null)
        {
            coin.localScale = Vector3.one;
            coin.anchoredPosition = coinHome;
        }
        SetCoinFace(heads);
        GameSfx.Play(Sfx.CoinLand);

        // Heads settles the moment its face shows (tails was already settled when the coin was tossed)
        RunEndDirector director = RunEndDirector.Instance;
        if (director != null) director.ApplyCoinFlip(heads);

        yield return new WaitForSecondsRealtime(Mathf.Max(0f, resultDelay));
        flipRoutine = null;
        ShowResult(heads);
        flipping = false;
    }

    /// <summary>"H" / "T" on the coin (Chinese 正 / 反, Japanese 表 / 裏, ...), looked up once per language.</summary>
    void SetCoinFace(bool heads)
    {
        coinShowsHeads = heads;
        if (headsLetter == null) headsLetter = L10n.Get(T, "runend.hearing.coin.heads");
        if (tailsLetter == null) tailsLetter = L10n.Get(T, "runend.hearing.coin.tails");
        string face = heads ? headsLetter : tailsLetter;
        TextMeshProUGUI letter = coinLetter as TextMeshProUGUI;
        if (letter != null) RunEndUI.SetCoinLetter(letter, face);
        else if (coinLetter != null) coinLetter.text = face;
    }

    void ShowResult(bool heads)
    {
        resultHeads = heads;
        resultShown = true;
        FillResult(heads);
        if (approvedStamp != null) approvedStamp.SetActive(heads);
        if (deniedStamp != null) deniedStamp.SetActive(!heads);
        GameSfx.PlayPair(Sfx.UiScratch, heads ? Sfx.ChipsWin : Sfx.None, 0.15f);   // APPROVED / DENIED stamp; heads saves the company
        if (resultButton != null)
        {
            Transform face = resultButton.transform.Find("Face");
            BlueprintBox box = face != null ? face.GetComponent<BlueprintBox>() : null;
            if (box != null) box.Fill = heads ? UIColors.Highlight : UIColors.PaperHi;
        }
        if (resultSlip != null)
        {
            resultSlip.gameObject.SetActive(true);
            resultSlip.SetAsLastSibling();
        }
        RunEndUI.ClearSelection();
    }

    /// <summary>The result slip's texts (written again when the language changes).</summary>
    void FillResult(bool heads)
    {
        if (resultTitleText != null) resultTitleText.text = L10n.Get(T, heads ? "runend.hearing.result.heads" : "runend.hearing.result.tails");
        if (resultBodyText != null)
        {
            string restart = UIFormat.Money(info.restartValue);
            resultBodyText.text = heads
                ? L10n.Get(T, info.debt > 0.0 ? "runend.hearing.result.heads.debt" : "runend.hearing.result.heads.plain", ("restart", restart))
                : L10n.Get(T, "runend.hearing.result.tails.body", ("company", CompanyName()));
        }
        if (resultButtonText != null) resultButtonText.text = L10n.Get(T, heads ? "runend.hearing.result.button.heads" : "runend.hearing.result.button.tails");
    }

    void StopFlip()
    {
        if (flipRoutine != null) StopCoroutine(flipRoutine);
        flipRoutine = null;
        flipping = false;
        if (coin != null && coinHomeKnown)
        {
            coin.localScale = Vector3.one;
            coin.anchoredPosition = coinHome;
        }
    }

    // ------------------------------------------------------------------
    // Runtime layout (1920 x 1080: a 1180 x 700 paper sheet in the middle, three sticky cards on it)
    // ------------------------------------------------------------------

    const float SheetWidth = 1180f, SheetHeight = 700f;
    const float CardWidth = 336f, CardHeight = 420f, CardTop = 200f, CardGap = 28f, Pad = 56f;

    static void Build(BankruptcyHearingView view, RectTransform parent, TMP_FontAsset font)
    {
        // Built inactive, so MarkerButton and TextHighlighter start with their references set
        RectTransform root = BlueprintUI.Stretch(BlueprintUI.NewRect("BankruptcyHearing", parent));
        root.gameObject.SetActive(false);
        view.root = root;

        RunEndUI.Scrim(root, UIColors.Scrim);
        RunEndUI.PausedChip(root, font);

        BlueprintBox sheet = RunEndUI.Sheet(root, "Sheet", UIColors.Paper, BlueprintUI.RadiusCard,
                                            (1920f - SheetWidth) * 0.5f, (1080f - SheetHeight) * 0.5f + 10f, SheetWidth, SheetHeight);
        RunEndUI.Tape(sheet.transform, 170f, -2f);
        RectTransform s = sheet.rectTransform;

        // Capitals written so in the String Table (the view writes the day)
        view.dayText = BlueprintUI.Caps(RunEndUI.Shrink(RunEndUI.Text(s, "Day", "", 19f, UIColors.Pencil, font, Pad, 40f, 700f, 26f), 14f));
        view.titleText = RunEndUI.Shrink(RunEndUI.Text(s, "Title", "", 48f, UIColors.Ink, font, Pad, 68f, 720f, 60f,
                                                       TextAlignmentOptions.MidlineLeft, true), 30f);
        TextMeshProUGUI subtitle = RunEndUI.Shrink(RunEndUI.Text(s, "Subtitle", "", 26f, UIColors.InkSoft, font, Pad, 132f, 720f, 36f), 18f);
        RunEndUI.Highlight(subtitle, RunEndUI.MarkYellow);
        view.subtitleText = subtitle;

        RectTransform notice = RunEndUI.Stamp(s, "FinalNotice", L10n.Get(T, "runend.hearing.stamp"), 34f, UIColors.Loss, font, 5f, 2f, 4f, 10f, 0.1f, 18f, 4f, -8f);
        RunEndUI.PlaceCenter(notice, SheetWidth - Pad - notice.sizeDelta.x * 0.5f + 6f, 84f);

        // Three choices (the view writes the loan card's button: it changes while a loan is out)
        Card(s, 0, "Loan", "runend.hearing.loan.title", RunEndIconKind.Bank, UIColors.StickyGreen, -1f, null, UIColors.Highlight, font,
             out view.loanFigureText, out view.loanBodyText, out view.loanButton, out view.loanButtonText);
        Card(s, 1, "Flip", "runend.hearing.flip.title", RunEndIconKind.Coin, UIColors.StickyYellow, 0.8f, "runend.hearing.flip.button", UIColors.Gold, font,
             out view.flipFigureText, out view.flipBodyText, out view.flipButton, out _);
        Card(s, 2, "Close", "runend.hearing.close.title", RunEndIconKind.Close, UIColors.StickyPink, -0.6f, "runend.hearing.close.button", UIColors.PaperHi, font,
             out view.closeFigureText, out view.closeBodyText, out view.closeButton, out _);

        L10nText.Attach(RunEndUI.Shrink(RunEndUI.Text(s, "Footer", "", 19f, UIColors.Pencil, font, 0f, 644f, SheetWidth, 28f, TextAlignmentOptions.Midline), 14f),
                        T, "runend.hearing.footer");   // "Pick one. The court doesn't do Escape."

        BuildFlipLayer(view, root, font);
    }

    /// <summary>
    /// A sticky choice card: its title (String Table <paramref name="titleKey"/>), figure, text and button (its label on
    /// <paramref name="buttonKey"/>, or written by the view when that is null).
    /// </summary>
    static void Card(RectTransform sheet, int index, string name, string titleKey, RunEndIconKind icon, Color paper, float cssRotation,
                     string buttonKey, Color buttonFill, TMP_FontAsset font, out TMP_Text figure, out TMP_Text body,
                     out Button button, out TMP_Text buttonText)
    {
        float x = Pad + index * (CardWidth + CardGap);
        BlueprintBox card = BlueprintUI.Box(sheet, name + "Card", paper, UIColors.Ink, 2f, BlueprintUI.RadiusLarge);
        card.SetShadow(new Color(0f, 0f, 0f, 0.25f), new Vector2(0f, 10f), 18f);
        RectTransform rect = card.rectTransform;
        RunEndUI.PlaceCenter(rect, x + CardWidth * 0.5f, CardTop + CardHeight * 0.5f);
        rect.sizeDelta = new Vector2(CardWidth, CardHeight);
        rect.localRotation = RunEndUI.Rotation(cssRotation);

        BlueprintBox badge = BlueprintUI.Box(rect, "IconBadge", UIColors.PaperHi, UIColors.Ink, 2f, BlueprintUI.Round(32f));
        BlueprintUI.TopLeft(badge.rectTransform, 24f, 24f, 64f, 64f);
        RunEndIcon glyph = RunEndIcon.Create(badge.transform, icon, 34f, UIColors.Ink);
        BlueprintUI.Centered(glyph.rectTransform, 34f, 34f);

        L10nText.Attach(RunEndUI.Shrink(RunEndUI.Text(rect, "Title", "", 30f, UIColors.Ink, font, 24f, 102f, CardWidth - 48f, 40f,
                                                      TextAlignmentOptions.MidlineLeft, true), 20f), T, titleKey);
        figure = RunEndUI.Shrink(RunEndUI.Text(rect, "Figure", "", 40f, UIColors.Ink, font, 24f, 146f, CardWidth - 48f, 50f,
                                               TextAlignmentOptions.MidlineLeft, true), 24f);
        TextMeshProUGUI bodyText = RunEndUI.Text(rect, "Body", "", 20f, UIColors.InkSoft, font, 24f, 204f, CardWidth - 48f, 116f,
                                                 TextAlignmentOptions.TopLeft);
        RunEndUI.Shrink(RunEndUI.Wrapped(bodyText, 1.35f), 15f);
        body = bodyText;

        button = RunEndUI.PaperButton(rect, "Button", "", RunEndIconKind.None, font, buttonFill, CardWidth - 48f, 64f, 26f, true,
                                      new Vector2(4f, 5f), out TextMeshProUGUI label);
        BlueprintUI.TopLeft((RectTransform)button.transform, 24f, CardHeight - 24f - 64f - 5f, CardWidth - 48f, 64f);
        if (buttonKey != null) L10nText.Attach(label, T, buttonKey);
        buttonText = label;
    }

    static void BuildFlipLayer(BankruptcyHearingView view, RectTransform root, TMP_FontAsset font)
    {
        RectTransform layer = BlueprintUI.Stretch(BlueprintUI.NewRect("CoinFlip", root));
        Image dim = layer.gameObject.AddComponent<Image>();
        dim.color = UIColors.Dark(0.45f);
        dim.raycastTarget = true;
        view.flipLayer = layer;

        RectTransform coin = RunEndUI.Coin(layer, "Coin", 240f, font, out TextMeshProUGUI letter);
        coin.anchoredPosition = new Vector2(0f, 190f);
        view.coin = coin;
        view.coinLetter = letter;

        // Result slip under the coin
        BlueprintBox slip = BlueprintUI.Box(layer, "ResultSlip", UIColors.PaperHi, UIColors.Ink, 3f, BlueprintUI.RadiusCard, true);
        slip.SetShadow(RunEndUI.SheetShadow, new Vector2(0f, 22f), 40f);
        RectTransform slipRect = BlueprintUI.Centered(slip.rectTransform, 720f, 360f, 0f, -170f);
        view.resultSlip = slipRect;

        L10nText.Attach(BlueprintUI.Caps(RunEndUI.Shrink(RunEndUI.Text(slipRect, "Label", "", 19f, UIColors.Pencil, font, 56f, 40f, 400f, 26f), 14f)),
                        T, "runend.hearing.flip.kicker");   // "THE LAST FLIP" (capitals written so in the String Table)
        view.resultTitleText = RunEndUI.Shrink(RunEndUI.Text(slipRect, "Title", "", 60f, UIColors.Ink, font, 56f, 68f, 400f, 70f,
                                                             TextAlignmentOptions.MidlineLeft, true), 36f);
        TextMeshProUGUI body = RunEndUI.Text(slipRect, "Body", "", 28f, UIColors.Ink, font, 56f, 142f, 608f, 96f, TextAlignmentOptions.TopLeft);
        view.resultBodyText = RunEndUI.Shrink(RunEndUI.Wrapped(body, 1.2f), 20f);

        // The stamps and the button are sized to their words in the current language (the sheet is built again for another)
        RectTransform approved = RunEndUI.Stamp(slipRect, "Approved", L10n.Get(T, "runend.hearing.stamp.approved"), 40f, UIColors.Money, font,
                                                5f, 2f, 4f, 10f, 0.12f, 22f, 4f, -10f);
        RunEndUI.PlaceCenter(approved, 720f - 44f - approved.sizeDelta.x * 0.5f, 84f);
        view.approvedStamp = approved.gameObject;
        RectTransform denied = RunEndUI.Stamp(slipRect, "Denied", L10n.Get(T, "runend.hearing.stamp.denied"), 40f, UIColors.Loss, font,
                                              5f, 2f, 4f, 10f, 0.12f, 22f, 4f, -10f);
        RunEndUI.PlaceCenter(denied, 720f - 56f - denied.sizeDelta.x * 0.5f, 84f);
        view.deniedStamp = denied.gameObject;

        string backToWork = L10n.Get(T, "runend.hearing.result.button.heads"), closeBooks = L10n.Get(T, "runend.hearing.result.button.tails");
        float width = Mathf.Min(608f, Mathf.Max(RunEndUI.TextWidth(font, backToWork, 28f, true), RunEndUI.TextWidth(font, closeBooks, 28f, true)) + 76f);
        view.resultButton = RunEndUI.PaperButton(slipRect, "Continue", backToWork, RunEndIconKind.None, font, UIColors.Highlight,
                                                 Mathf.Ceil(width), 68f, 28f, true, new Vector2(5f, 6f), out TextMeshProUGUI label);
        BlueprintUI.TopLeft((RectTransform)view.resultButton.transform, 56f, 260f, Mathf.Ceil(width), 68f);
        view.resultButtonText = label;

        slipRect.gameObject.SetActive(false);
        layer.gameObject.SetActive(false);
    }
}
