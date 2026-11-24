using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// "Fill the build budget" (design boards TowerBudget / TowerBudgetWon): bet company value on a coin flip or a blackjack
/// hand; a win pays bet x 1.5 into the build budget, a loss is gone (a blackjack push gives the bet back to the company).
/// - Pick a game, set the bet (-/+, the field, the slider or the $1,000 / $5,000 / $10,000 / MAX presets) and play.
/// - The company always keeps keepInCompany ($1,000): the largest bet is floor(company value - keepInCompany), so an
///   all-in bet (which pays into the build budget, not into cash) can't leave the company at $0. Demo: one bet is also at
///   most DemoConfig.buildGambleMaxBetShare (half) of the company value, and the note under the bet says so.
/// - Pausing pop-up (UIManager.OpenPanel(this, true, ...)) on the tower screen. Escape closes it, except while a flip or
///   a hand is running.
/// - The bet is taken from the company when the game starts (PendingBet while unresolved) and settled as soon as the
///   outcome shows: the coin's final face, or the end of the hand (BlackjackGame.HandEnded), before the result is
///   closed. Saves don't add an unresolved bet back, so quitting mid-flip or mid-hand forfeits it. GambleSettled reports
///   every settled bet (the run's casino net; GambleLedger turns it into heat). No bet is taken while HeatMeter.CanGamble
///   says the board froze gambling.
/// - Blackjack uses the scene's BlackjackGame; the tower screen is hidden while its table (on the HUD canvas) is open.
/// - Feedback ("You need at least $1,100 ...") shows on the pop-up's status chip while it is up (the HUD's toasts are
///   under the tower screen), on the tower screen's message chip otherwise, and as a game notification only when neither
///   is on screen (a hand that ends after build mode closed).
/// - With Root empty the pop-up is built at runtime (BuildGambleView.Build); a designed one can fill the references.
/// - Its words come from the "Tower" String Table (tower.gamble.*): fixed ones keep to it by themselves (L10nText), the
///   rest is written again when the language changes while it is open, in the language's font (L10nFont).
/// - "Gamble for it" (2026-09-29, the owner's decision that build money stays gambling-only): OpenFor(shortfall, goal)
///   opens it with the bet whose win covers a purchase's shortfall (within $100 and the company's reserve), and the note
///   under the presets follows the goal until the pop-up closes.
/// </summary>
public class BuildGambleView : MonoBehaviour
{
    public static BuildGambleView Instance { get; private set; }

    /// <summary>The games of the pop-up.</summary>
    public enum Game
    {
        CoinFlip,
        Blackjack
    }

    static readonly double[] QuickBets = { 1000, 5000, 10000 };

    const float BetFontSize = 30f;
    const float MinBetFontSize = 16f;    // big bets shrink down to this instead of being cut off
    const int MaxFullBetChars = 20;      // "$999,999,999,999,999" still fits at 16 pt; longer ones show as "$1.2Q"

    [Header("Rules")]
    [Tooltip("Smallest bet")]
    public double minimumBet = 100;
    [Tooltip("A win pays the bet times this into the build budget")]
    public double winMultiplier = 1.5;
    [Tooltip("Company value that no bet can touch (never below 0): the largest bet is company value minus this")]
    public double keepInCompany = 1000;
    [Tooltip("Shown with the blackjack odds (the table's rules decide the real odds)")]
    [Range(0f, 1f)]
    public float blackjackWinChance = 0.42f;
    [Range(0f, 1f)]
    public float blackjackPushChance = 0.08f;
    [Tooltip("Length of the coin flip animation (real time)")]
    public float coinFlipSeconds = 1.3f;

    [Header("Look")]
    [Tooltip("Font of the pop-up (empty = the tower screen's font, else TextMesh Pro's default)")]
    public TMP_FontAsset font;

    [Header("Pop-up (leave Root empty to build it at runtime)")]
    public RectTransform root;
    public TextMeshProUGUI budgetText;
    public TextMeshProUGUI companyValueText;
    [Tooltip("Game cards: pick the coin flip or blackjack")]
    public Button coinFlipButton;
    public Button blackjackButton;
    public TMP_InputField betInput;
    public Button minusButton;
    public Button plusButton;
    [Tooltip("0..1; the bet grows with the square of the slider for finer small bets")]
    public Slider betSlider;
    public TextMeshProUGUI sliderMinText;
    public TextMeshProUGUI sliderMaxText;
    [Tooltip("$1,000, $5,000, $10,000 and MAX")]
    public Button[] quickBetButtons = new Button[4];
    [Tooltip("\"MAX bets $X. The company keeps $1,000.\" next to the presets")]
    public TextMeshProUGUI keepHintText;
    [Tooltip("The big gold button: flips or deals the picked game")]
    public Button playButton;
    public TextMeshProUGUI playButtonText;
    public Button cancelButton;
    public Button closeButton;

    [Header("The odds")]
    public TextMeshProUGUI oddsTitleText;
    [Tooltip("The odds bar (drawn in code)")]
    public BlueprintGraphic oddsBar;
    public TextMeshProUGUI winLabelText;
    [Tooltip("What a win pays (\"+$7,500 budget\")")]
    public TextMeshProUGUI winPreviewText;
    public RectTransform pushRow;
    public TextMeshProUGUI pushLabelText;
    public TextMeshProUGUI pushPreviewText;
    public TextMeshProUGUI loseLabelText;
    [Tooltip("What a loss pays (\"+$0 budget\")")]
    public TextMeshProUGUI losePreviewText;
    public TextMeshProUGUI oddsNoteText;
    public TextMeshProUGUI oddsAverageText;

    [Header("Coin flip")]
    public RectTransform coinFlipLayer;
    public RectTransform coin;
    public TextMeshProUGUI coinLetter;

    [Header("Result")]
    public RectTransform resultLayer;
    public TextMeshProUGUI resultTitleText;
    public TextMeshProUGUI resultSubtitleText;
    public TextMeshProUGUI resultCoinLetter;
    public TextMeshProUGUI resultCoinLabelText;
    public TextMeshProUGUI resultGameText;
    public BlueprintBox resultStampBox;
    public TextMeshProUGUI resultStampText;
    public TextMeshProUGUI resultAmountText;
    public TextMeshProUGUI resultBudgetDeltaText;
    public TextMeshProUGUI resultBudgetText;
    public TextMeshProUGUI resultValueDeltaText;
    public TextMeshProUGUI resultValueText;
    [Tooltip("\"Again\": back to the bet")]
    public Button resultButton;
    public TextMeshProUGUI resultButtonText;
    [Tooltip("\"Back to building\": closes the pop-up")]
    public Button resultBackButton;

    [Header("Status")]
    [Tooltip("Chip on the table for short feedback (empty = the text alone is shown and hidden)")]
    public RectTransform statusChip;
    [Tooltip("Short feedback on the table (empty = the tower screen's message chip)")]
    public TextMeshProUGUI statusText;

    Game game = Game.CoinFlip;
    double bet = 5000;
    double pendingBet;
    double valueBeforeBet;
    bool isOpen;
    bool flipping;
    bool houseFlip;   // Demo: the running flip is the run's free one
    bool handRunning;
    bool handSettled;            // the running hand's outcome is already paid (at the end of the hand, before Continue)
    BlackjackResult handResult;
    double handStake;
    double handBudgetBefore;
    double handValueBefore;
    BlackjackGame handTable;
    bool hooked;
    bool updatingControls;
    double shownMax = -1;
    double shownValue = -1;
    double shownBudget = -1;
    string shownBetText;         // what the bet field shows while it isn't being edited
    int betEditEndFrame = -1;    // frame the last bet edit ended (Escape may reach the field before UIManager)
    float statusUntil;
    CanvasGroup statusGroup;
    Coroutine flipRoutine;
    Canvas ownCanvas;
    int drawnOddsKey;
    bool fontsFollowLanguage;

    // "Gamble for it": what the win is for (null = no goal) and the build budget that pays for it
    string goalName;
    double goalPrice;
    bool openingForGoal;

    // The result on the felt, kept to write it again in another language
    string shownCoinFace;        // "H", "T" or "21" (blackjack)
    BlackjackResult shownOutcome;
    double shownStake, shownBudgetBefore, shownBudgetAfter, shownValueBefore, shownValueAfter;

    /// <summary>Money taken from the company for a flip or hand that hasn't resolved yet.</summary>
    public double PendingBet => pendingBet;
    public bool IsOpen => isOpen;
    /// <summary>A coin flip or blackjack hand is running.</summary>
    public bool IsBusy => flipping || handRunning;
    public Game SelectedGame => game;

    /// <summary>
    /// A flip or hand settled: (bet, money that came back: a win's payout into the build budget, a push's refund, 0 after
    /// a loss). Run stats count it as casino net.
    /// </summary>
    public static event System.Action<double, double> GambleSettled;

    static void ReportSettled(double stake, double paidOut)
    {
        try { GambleSettled?.Invoke(stake, paidOut); }
        catch (System.Exception e) { Debug.LogException(e); }
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[BuildGambleView] Removed a second BuildGambleView on '" + name + "'.");
            Destroy(this);
            return;
        }
        Instance = this;
        if (DemoMode.Active) winMultiplier = DemoMode.Config.buildWinMultiplier;   // Demo: 2x over the scene's 1.5x
    }

    void OnEnable()
    {
        L10n.LanguageChanged += OnLanguageChanged;
    }

    // Write the open pop-up again (the fixed words and the fonts follow by themselves: L10nText, L10nFont)
    void OnLanguageChanged()
    {
        if (!isOpen) return;
        UpdateBetControls();
        if (resultLayer != null && resultLayer.gameObject.activeSelf && shownCoinFace != null) FillResult();
    }

    void OnDisable()
    {
        L10n.LanguageChanged -= OnLanguageChanged;
        // Leaving mid-flip (object switched off, scene unloading): the stake goes back
        if (flipping) CancelFlip();
        if (isOpen)
        {
            isOpen = false;
            if (UIManager.Exists) UIManager.Instance.ClosePanel(this);
        }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (handTable != null) handTable.HandEnded -= OnHandEnded;
        handTable = null;
        if (handRunning && pendingBet > 0.0 && CompanyManager.Instance != null)
            PlayerWallet.Receive(pendingBet, "build gamble (refund)");   // Cash Grab: the bet came from Player Cash
        pendingBet = 0.0;
        if (UIManager.Exists) UIManager.Instance.ClosePanel(this);
        if (ownCanvas != null) Destroy(ownCanvas.gameObject);
    }

    void Update()
    {
        UpdateStatus();
        if (isOpen) DrawOddsBar(game == Game.Blackjack);   // its size is known once the layout ran
        if (!isOpen || IsBusy) return;
        // The company value and the build budget can change (they rarely do while paused): keep the limits current
        if (MaxBet() != shownMax || CompanyValue() != shownValue || BuildBudget != shownBudget)
        {
            SetBet(bet);
            RefreshControls();
        }
    }

    // ------------------------------------------------------------------
    // Opening
    // ------------------------------------------------------------------

    public void Open()
    {
        if (isOpen) return;
        if (!openingForGoal) goalName = null;   // the plain "Fill budget" button: no goal
        EnsureBuilt();
        root.gameObject.SetActive(true);
        root.SetAsLastSibling();
        if (coinFlipLayer != null) coinFlipLayer.gameObject.SetActive(false);
        if (resultLayer != null) resultLayer.gameObject.SetActive(false);
        HideStatus();
        isOpen = true;
        SetBet(bet);
        RefreshControls();

        UIManager ui = UIManager.Instance;
        if (ui != null) ui.OpenPanel(this, true, OnEscape);

        // Say right away why nothing can be bet
        if (!HeatMeter.CanGamble(out string frozen))
            Say(frozen, NotificationKind.Bad);
        else if (CompanyManager.Instance != null && MaxBet() < minimumBet)
            Say(NotEnoughText(), NotificationKind.Bad);
    }

    /// <summary>
    /// "Gamble for it": open the pop-up (or retarget it) with the bet whose win covers <paramref name="shortfall"/> of build
    /// budget: shortfall / winMultiplier, rounded up to $10, then kept within the table's limits (minimumBet, and the most the
    /// company can bet while it keeps keepInCompany). When even MAX can't cover it, MAX is set and the note says so.
    /// <paramref name="goal"/> names the purchase in the note ("Big office").
    /// </summary>
    public void OpenFor(double shortfall, string goal)
    {
        if (IsBusy) return;
        shortfall = double.IsNaN(shortfall) || double.IsInfinity(shortfall) ? 0.0 : System.Math.Max(0.0, shortfall);
        goalName = string.IsNullOrEmpty(goal) ? TowerText.Get("tower.gamble.goal.fallback") : goal;
        goalPrice = BuildBudget + shortfall;
        bet = System.Math.Ceiling(shortfall / System.Math.Max(0.01, winMultiplier) / 10.0) * 10.0;   // SetBet keeps it within the limits
        if (!isOpen)
        {
            openingForGoal = true;
            try { Open(); }
            finally { openingForGoal = false; }
        }
        else
        {
            if (resultLayer != null) resultLayer.gameObject.SetActive(false);
            SetBet(bet);
            RefreshControls();
        }
    }

    /// <summary>The purchase a "Gamble for it" opened this for (null = none), and the build budget still missing for it.</summary>
    public string GoalName => goalName;
    public double GoalShortfall => goalName != null ? System.Math.Max(0.0, goalPrice - BuildBudget) : 0.0;

    /// <summary>Close (ignored while a flip or hand is running).</summary>
    public void Close()
    {
        if (IsBusy) return;
        CloseNow();
    }

    /// <summary>Close right away (build mode is closing). A running flip is refunded; a blackjack hand still finishes.</summary>
    public void ForceClose()
    {
        if (flipping) CancelFlip();
        CloseNow();
    }

    void CloseNow()
    {
        if (root != null) root.gameObject.SetActive(false);
        if (!isOpen) return;
        isOpen = false;
        goalName = null;
        if (UIManager.Exists) UIManager.Instance.ClosePanel(this);
    }

    void OnEscape()
    {
        // Escape while typing a bet only ends the edit (the field goes back to the bet); the pop-up stays open
        if (IsEditingBet())
        {
            CancelBetEdit();
            return;
        }
        if (IsBusy) return;
        if (resultLayer != null && resultLayer.gameObject.activeSelf)
        {
            resultLayer.gameObject.SetActive(false);
            RefreshControls();
            return;
        }
        Close();
    }

    void EnsureBuilt()
    {
        if (root == null)
        {
            TowerView view = TowerView.Instance;
            TMP_FontAsset useFont = font != null ? font : view != null ? view.ResolveFont() : null;
            RectTransform parent;
            if (view != null)
            {
                // Under the tower screen's root: hiding the screen for the blackjack table hides this too
                parent = view.Root;
            }
            else
            {
                ownCanvas = BlueprintUI.CreateOverlayCanvas("BuildGambleCanvas (Runtime)", 61);
                parent = (RectTransform)ownCanvas.transform;
            }
            Build(this, parent, useFont);
        }
        if (root != null && !fontsFollowLanguage)
        {
            L10nFont.AttachAll(root);   // built here or designed: every text takes the language's font
            fontsFollowLanguage = true;
        }
        Hook();
    }

    void Hook()
    {
        if (hooked) return;
        hooked = true;
        if (minusButton != null) minusButton.onClick.AddListener(() => Step(-1));
        if (plusButton != null) plusButton.onClick.AddListener(() => Step(1));
        if (betSlider != null) betSlider.onValueChanged.AddListener(OnSlider);
        if (betInput != null)
        {
            betInput.onSelect.AddListener(_ => { if (betInput != null) betInput.SetTextWithoutNotify(Plain(bet)); });
            betInput.onEndEdit.AddListener(OnInputEnd);
            // ASCII digits only: char.IsDigit also lets Arabic, Persian and full-width digits in, which don't parse
            betInput.onValidateInput = (text, index, c) => IsAsciiDigit(c) ? c : '\0';
        }
        for (int i = 0; i < quickBetButtons.Length; i++)
        {
            if (quickBetButtons[i] == null) continue;
            int index = i;
            quickBetButtons[i].onClick.AddListener(() => SetBet(index < QuickBets.Length ? QuickBets[index] : MaxBet()));
            quickBetButtons[i].onClick.AddListener(() => GameSfx.Play(Sfx.ChipBet));
        }
        if (coinFlipButton != null) coinFlipButton.onClick.AddListener(() => PickGame(Game.CoinFlip));
        if (blackjackButton != null) blackjackButton.onClick.AddListener(() => PickGame(Game.Blackjack));
        if (playButton != null) playButton.onClick.AddListener(Play);
        if (cancelButton != null) cancelButton.onClick.AddListener(Close);
        if (closeButton != null) closeButton.onClick.AddListener(Close);
        if (resultButton != null)
            resultButton.onClick.AddListener(() =>
            {
                if (resultLayer != null) resultLayer.gameObject.SetActive(false);
                RefreshControls();
            });
        if (resultBackButton != null) resultBackButton.onClick.AddListener(Close);
    }

    // ------------------------------------------------------------------
    // Game and bet
    // ------------------------------------------------------------------

    public void PickGame(Game picked)
    {
        if (IsBusy) return;
        game = picked;
        RefreshControls();
    }

    /// <summary>Flip or deal the picked game.</summary>
    public void Play()
    {
        if (game == Game.Blackjack) StartBlackjack();
        else StartCoinFlip();
    }

    /// <summary>
    /// The bankroll in whole dollars: Player Cash (Cash Grab, 2026-11-24: the build budget is filled with cash the CEO
    /// collected on the cash floor; PlayerWallet falls back to company value without a cash floor).
    /// </summary>
    static double CompanyValue()
    {
        return CompanyManager.Instance != null ? System.Math.Max(0.0, System.Math.Floor(PlayerWallet.Balance)) : 0.0;
    }

    /// <summary>What no bet may touch: nothing with the cash floor (only Player Cash is bet), keepInCompany otherwise.</summary>
    double Reserve => PlayerWallet.Active ? 0.0 : System.Math.Max(0.0, keepInCompany);

    /// <summary>
    /// The largest bet: all of the company value except what the company keeps. In the demo also at most
    /// DemoConfig.buildGambleMaxBetShare of the company value (half, like a casino bet), so one lost hand can't take
    /// everything (2026-11-16: the owner lost all the company on one MAX hand).
    /// </summary>
    double MaxBet()
    {
        double max = System.Math.Max(0.0, System.Math.Floor(CompanyValue() - Reserve));
        if (DemoMode.Active) max = System.Math.Min(max, DemoShareCap());
        return max;
    }

    /// <summary>Demo: the most one bet may be, as a share of the company value (whole dollars).</summary>
    static double DemoShareCap()
    {
        return System.Math.Max(0.0, System.Math.Floor(CompanyValue() * DemoMode.Config.buildGambleMaxBetShare));
    }

    /// <summary>Demo: the share cap, not the reserve, is what sets MAX right now. Always false outside the demo.</summary>
    bool DemoShareCapped => DemoMode.Active && DemoShareCap() < System.Math.Floor(CompanyValue() - Reserve);

    string NotEnoughText()
    {
        if (PlayerWallet.Active) return CashText.Get("cash.gamble.not_enough", ("amount", UIFormat.Money(minimumBet)));   // Cash Grab
        return TowerText.Get("tower.gamble.not_enough", ("amount", UIFormat.Money(minimumBet + Reserve)), ("reserve", UIFormat.Money(Reserve)));
    }

    double ClampBet(double amount)
    {
        double max = MaxBet();
        double min = System.Math.Min(minimumBet, max);
        if (double.IsNaN(amount)) amount = min;
        return System.Math.Min(System.Math.Max(System.Math.Round(amount), min), max);
    }

    void SetBet(double amount)
    {
        bet = ClampBet(amount);
        UpdateBetControls();
    }

    void UpdateBetControls()
    {
        double max = MaxBet();
        double value = CompanyValue();
        double budget = BuildBudget;
        shownMax = max;
        shownValue = value;
        shownBudget = budget;
        double min = System.Math.Min(minimumBet, max);
        bool canBet = max >= minimumBet;
        updatingControls = true;
        if (betInput != null && !betInput.isFocused) ShowBetText();
        if (betSlider != null)
        {
            double range = max - min;
            float t = range > 0.0 ? Mathf.Sqrt((float)((bet - min) / range)) : 0f;
            betSlider.SetValueWithoutNotify(Mathf.Clamp01(t));
        }
        updatingControls = false;

        if (sliderMinText != null) sliderMinText.text = UIFormat.Money(minimumBet);
        if (sliderMaxText != null) sliderMaxText.text = UIFormat.Money(max);
        if (budgetText != null) budgetText.text = UIFormat.Money(budget);
        if (companyValueText != null) companyValueText.text = UIFormat.Money(value);
        if (keepHintText != null)
            keepHintText.text = !canBet ? NotEnoughText()
                : goalName != null ? GoalText(max)
                : DemoShareCapped ? DemoMode.T("demo.casino.max_note", ("max", "<b>" + UIFormat.Money(max) + "</b>"))   // Demo: "one bet is at most half the company"
                : PlayerWallet.Active ? CashText.Get("cash.gamble.keep_hint", ("max", "<b>" + TowerText.Get("tower.gamble.preset.max") + "</b>"),
                                                     ("amount", "<b>" + UIFormat.Money(max) + "</b>"))   // Cash Grab: MAX bets all your cash
                : TowerText.Get("tower.gamble.keep_hint", ("max", "<b>" + TowerText.Get("tower.gamble.preset.max") + "</b>"),
                                ("amount", "<b>" + UIFormat.Money(max) + "</b>"), ("reserve", UIFormat.Money(Reserve)));

        // The odds for the picked game and bet
        double payout = bet * winMultiplier;
        bool blackjack = game == Game.Blackjack;
        if (oddsTitleText != null) oddsTitleText.text = TowerText.Get(blackjack ? "tower.gamble.odds.title.blackjack" : "tower.gamble.odds.title.coin_flip");
        if (winLabelText != null)
            winLabelText.text = blackjack ? TowerText.Get("tower.gamble.odds.win", ("percent", Percent(blackjackWinChance)))
                                          : TowerText.Get("tower.gamble.odds.heads", ("percent", Percent(0.5f)));
        if (winPreviewText != null) winPreviewText.text = Preview(winPreviewText, "tower.gamble.odds.budget_preview", "+" + UIFormat.Money(payout));
        if (pushRow != null) pushRow.gameObject.SetActive(blackjack);
        if (pushLabelText != null) pushLabelText.text = TowerText.Get("tower.gamble.odds.tie", ("percent", Percent(blackjackPushChance)));
        if (pushPreviewText != null) pushPreviewText.text = Preview(pushPreviewText, "tower.gamble.odds.push_preview", UIFormat.Money(bet));
        if (loseLabelText != null)
            loseLabelText.text = blackjack ? TowerText.Get("tower.gamble.odds.lose", ("percent", Percent(Mathf.Max(0f, 1f - blackjackWinChance - blackjackPushChance))))
                                           : TowerText.Get("tower.gamble.odds.tails", ("percent", Percent(0.5f)));
        if (losePreviewText != null) losePreviewText.text = Preview(losePreviewText, "tower.gamble.odds.budget_preview", "+" + UIFormat.Money(0.0));
        if (oddsNoteText != null)
            oddsNoteText.text = TowerText.Get(blackjack ? "tower.gamble.odds.note.blackjack" : "tower.gamble.odds.note.coin_flip",
                                              ("bet", "<b>" + UIFormat.Money(bet) + "</b>"), ("from", UIFormat.Money(value)),
                                              ("to", UIFormat.Money(System.Math.Max(0.0, value - bet))));
        if (oddsAverageText != null)
        {
            double average = blackjack ? payout * blackjackWinChance : payout * 0.5;
            oddsAverageText.text = blackjack
                ? TowerText.Get("tower.gamble.odds.average.blackjack", ("average", UIFormat.Money(average)), ("bet", UIFormat.Money(bet)),
                                ("refund", UIFormat.Money(bet * blackjackPushChance)))
                : TowerText.Get("tower.gamble.odds.average.coin_flip", ("average", UIFormat.Money(average)), ("bet", UIFormat.Money(bet)));
        }
        DrawOddsBar(blackjack);
        if (playButtonText != null) playButtonText.text = TowerText.Get(blackjack ? "tower.gamble.play.deal" : "tower.gamble.play.flip", ("amount", UIFormat.Money(bet)));

        for (int i = 0; i < quickBetButtons.Length; i++)
        {
            Button button = quickBetButtons[i];
            if (button == null) continue;
            double quick = i < QuickBets.Length ? QuickBets[i] : max;
            bool selected = System.Math.Abs(bet - quick) < 0.5 && canBet;
            BlueprintUI.SetInteractable(button, !IsBusy && quick <= max && canBet);
            BlueprintBox box = button.GetComponent<BlueprintBox>();
            if (box != null)
            {
                box.SetStyle(selected ? UIColors.Highlight : UIColors.PaperHi, UIColors.Ink, selected ? 3f : 2f);
                box.SetShadow(BlueprintUI.FeltShadow, selected ? new Vector2(4f, 5f) : new Vector2(3f, 4f));
            }
            TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null) label.fontStyle = selected || i >= QuickBets.Length ? FontStyles.Bold : FontStyles.Normal;
        }
    }

    /// <summary>
    /// The goal note of a "Gamble for it": how much is still missing and what a win on the bet pays ("Big office: $12,000 short.
    /// A win on $8,000 pays $12,000."), that even MAX falls short, or that the budget covers it now.
    /// </summary>
    string GoalText(double max)
    {
        double shortfall = GoalShortfall;
        string goal = "<b>" + goalName + "</b>";
        if (shortfall < 0.5) return TowerText.Get("tower.gamble.goal.done", ("goal", goal));
        double payout = bet * winMultiplier;
        string key = payout + 0.5 >= shortfall ? "tower.gamble.goal" : "tower.gamble.goal.max";
        return TowerText.Get(key, ("goal", goal), ("short", "<b>" + UIFormat.Money(shortfall) + "</b>"), ("bet", UIFormat.Money(bet)),
                             ("payout", UIFormat.Money(payout)));
    }

    /// <summary>A share as a whole percentage: "42%" ("%42" in Turkish).</summary>
    static string Percent(float share)
    {
        return TowerText.Percent(System.Math.Round(share * 100.0));
    }

    /// <summary>"+$7,500 budget": the amount big and bold in the row's colour, the words around it grey.</summary>
    static string Preview(TextMeshProUGUI target, string key, string amount)
    {
        string colour = ColorUtility.ToHtmlStringRGB(target.color);
        return "<color=#474F60>" + TowerText.Get(key, ("amount", "<color=#" + colour + "><b><size=22>" + amount + "</size></b></color>")) + "</color>";
    }

    /// <summary>The coin's letter for heads or tails ("H" / "T"; each language has its own).</summary>
    static string CoinLetter(bool heads)
    {
        return TowerText.Get(heads ? "tower.gamble.coin.heads" : "tower.gamble.coin.tails");
    }

    /// <summary>Coin flip: green half / red half. Blackjack: win / tie / lose.</summary>
    void DrawOddsBar(bool blackjack)
    {
        if (oddsBar == null) return;
        Rect r = oddsBar.rectTransform.rect;
        float w = r.width, h = r.height;
        if (w <= 0f || h <= 0f) return;
        int key = (blackjack ? 1 : 2) + Mathf.RoundToInt(w) * 7 + Mathf.RoundToInt(h) * 7919;
        if (key == drawnOddsKey) return;
        drawnOddsKey = key;
        BlueprintMeshBuilder m = oddsBar.Mesh;
        m.Clear();
        m.Feather = BlueprintBox.SoftEdge(oddsBar.canvas);
        m.SetYDownFrame(new Vector2(r.xMin, r.yMax));
        float win = blackjack ? Mathf.Clamp01(blackjackWinChance) : 0.5f;
        float push = blackjack ? Mathf.Clamp01(blackjackPushChance) : 0f;
        Vector4 radii = BlueprintUI.RadiusSmall;
        m.FillRoundRect(new Rect(0f, 0f, w, h), radii, UIColors.PaperHi);
        m.FillRect(new Rect(2f, 2f, (w - 4f) * win, h - 4f), UIColors.Money, 0f);
        if (push > 0f) m.FillRect(new Rect(2f + (w - 4f) * win, 2f, (w - 4f) * push, h - 4f), UIColors.PaperShade, 0f);
        m.FillRect(new Rect(2f + (w - 4f) * (win + push), 2f, (w - 4f) * (1f - win - push), h - 4f), UIColors.Loss, 0f);
        m.Line(new Vector2(2f + (w - 4f) * win, 0f), new Vector2(2f + (w - 4f) * win, h), 2f, UIColors.Ink);
        if (push > 0f) m.Line(new Vector2(2f + (w - 4f) * (win + push), 0f), new Vector2(2f + (w - 4f) * (win + push), h), 2f, UIColors.Ink);
        m.StrokeRoundRect(new Rect(1f, 1f, w - 2f, h - 2f), radii, 2f, UIColors.Ink);
        oddsBar.Commit();
    }

    void OnSlider(float t)
    {
        if (updatingControls) return;
        double max = MaxBet();
        double min = System.Math.Min(minimumBet, max);
        double amount = min + (max - min) * t * t;
        if (t < 0.999f) amount = System.Math.Round(amount / 100.0) * 100.0;   // round to $100 (the top end stays exact)
        else amount = max;
        bet = ClampBet(amount);
        updatingControls = true;
        UpdateBetControls();
        updatingControls = false;
    }

    void OnInputEnd(string text)
    {
        betEditEndFrame = Time.frameCount;
        // What the player typed (only '0'-'9' get in); the field's own display text ("$1,000", "$1.2Q") is no new bet
        string digits = "";
        if (text != shownBetText)
            foreach (char c in text ?? "")
                if (IsAsciiDigit(c)) digits += c;
        if (digits.Length > 0 && double.TryParse(digits, System.Globalization.NumberStyles.None,
                                                 System.Globalization.CultureInfo.InvariantCulture, out double value))
            SetBet(value);
        else
            SetBet(bet);
        ShowBetText();
    }

    static bool IsAsciiDigit(char c)
    {
        return c >= '0' && c <= '9';
    }

    /// <summary>Show the bet in the field (while it isn't being edited).</summary>
    void ShowBetText()
    {
        if (betInput == null) return;
        shownBetText = BetText(bet);
        betInput.SetTextWithoutNotify(shownBetText);
    }

    /// <summary>
    /// The bet as the field shows it: the exact amount (the text shrinks from 30 to 16 pt to fit), or a short "$1.2Q"
    /// for amounts too long even at 16 pt. Editing always shows the plain digits.
    /// </summary>
    static string BetText(double amount)
    {
        string full = UIFormat.Money(amount);
        if (full.Length <= MaxFullBetChars) return full;
        return "$" + (System.Math.Abs(amount) / 1e15).ToString("#,0.##", System.Globalization.CultureInfo.InvariantCulture) + "Q";
    }

    /// <summary>The player is typing a bet, or the field just ended an edit this frame (Escape reached it first).</summary>
    bool IsEditingBet()
    {
        if (betInput == null) return false;
        return betInput.isFocused || betEditEndFrame == Time.frameCount || UIManager.IsTypingInInputField();
    }

    /// <summary>End the bet edit without applying it (like the field's own Escape) and deselect the field.</summary>
    void CancelBetEdit()
    {
        if (betInput == null) return;
        if (betInput.isFocused)
        {
            // Escape reached UIManager before the field: put the bet back, then end the edit
            betInput.SetTextWithoutNotify(Plain(bet));
            betInput.DeactivateInputField();
        }
        EventSystem system = EventSystem.current;
        if (system != null && system.currentSelectedGameObject == betInput.gameObject)
            system.SetSelectedGameObject(null);
        SetBet(bet);
    }

    /// <summary>- and + walk a 1-2-5 ladder ($100, $200, $500, $1,000 ...).</summary>
    void Step(int direction)
    {
        double next = direction > 0 ? LadderAbove(bet) : LadderBelow(bet);
        SetBet(next);
        GameSfx.Play(Sfx.ChipBet);
    }

    static double LadderAbove(double value)
    {
        for (double scale = 100; scale < 1e15; scale *= 10)
            foreach (double m in new[] { 1.0, 2.0, 5.0 })
                if (scale * m > value + 0.5) return scale * m;
        return value;
    }

    static double LadderBelow(double value)
    {
        double best = 100;
        for (double scale = 100; scale < 1e15; scale *= 10)
        {
            foreach (double m in new[] { 1.0, 2.0, 5.0 })
            {
                double step = scale * m;
                if (step < value - 0.5) best = step;
                else return best;
            }
        }
        return best;
    }

    static string Plain(double amount)
    {
        return System.Math.Round(amount).ToString("0", System.Globalization.CultureInfo.InvariantCulture);
    }

    void RefreshControls()
    {
        bool idle = !IsBusy;
        bool canBet = idle && MaxBet() >= minimumBet && CompanyManager.Instance != null;
        BlueprintUI.SetInteractable(playButton, canBet);
        BlueprintUI.SetInteractable(coinFlipButton, idle);
        BlueprintUI.SetInteractable(blackjackButton, idle);
        StyleGameCard(coinFlipButton, game == Game.CoinFlip);
        StyleGameCard(blackjackButton, game == Game.Blackjack);
        BlueprintUI.SetInteractable(cancelButton, idle);
        BlueprintUI.SetInteractable(closeButton, idle);
        BlueprintUI.SetInteractable(minusButton, idle);
        BlueprintUI.SetInteractable(plusButton, idle);
        if (betSlider != null) betSlider.interactable = idle;
        if (betInput != null) betInput.interactable = idle;
        UpdateBetControls();
    }

    /// <summary>The picked game card has a highlighter ring (5 px yellow, 3 px ink) and a bold name.</summary>
    static void StyleGameCard(Button button, bool picked)
    {
        if (button == null) return;
        BlueprintBox box = button.GetComponent<BlueprintBox>();
        if (box != null)
        {
            box.SetRings(picked ? UIColors.Highlight : Color.clear, picked ? 5f : 0f, picked ? UIColors.Ink : Color.clear, picked ? 3f : 0f);
            box.SetShadow(picked ? Color.clear : BlueprintUI.FeltShadow, new Vector2(4f, 5f));
        }
        foreach (TextMeshProUGUI text in button.GetComponentsInChildren<TextMeshProUGUI>(true))
            if (text.name == "Name") text.fontStyle = picked ? FontStyles.Bold : FontStyles.Normal;
    }

    /// <summary>The bet to play now, or false (with a message) when nobody can bet or the board froze gambling.</summary>
    bool TryGetBet(out double stake)
    {
        stake = 0.0;
        if (IsBusy || CompanyManager.Instance == null) return false;
        if (!HeatMeter.CanGamble(out string frozen))
        {
            Say(frozen, NotificationKind.Bad);
            return false;
        }
        if (MaxBet() < minimumBet)
        {
            Say(NotEnoughText(), NotificationKind.Bad);
            return false;
        }
        stake = ClampBet(bet);
        if (stake != bet) SetBet(stake);
        return stake >= minimumBet;
    }

    /// <summary>Take the stake out of Player Cash (PlayerWallet). False when it isn't there: nothing is taken.</summary>
    bool TakeBet(double stake)
    {
        double before = CompanyValue();
        if (!PlayerWallet.TrySpend(stake, "build gamble")) return false;
        pendingBet = stake;
        valueBeforeBet = before;
        return true;
    }

    static TowerBuilder Builder => TowerBuilder.Instance;

    static double BuildBudget => Builder != null ? Builder.Budget : 0.0;

    // ------------------------------------------------------------------
    // Coin flip
    // ------------------------------------------------------------------

    void StartCoinFlip()
    {
        if (!TryGetBet(out double stake)) return;
        HideStatus();
        if (!TakeBet(stake))
        {
            Say(NotEnoughText(), NotificationKind.Bad);
            return;
        }
        flipping = true;
        GameSfx.Play(Sfx.CoinFlick);
        RefreshControls();
        houseFlip = IsHouseFlip(stake);
        flipRoutine = StartCoroutine(CoinFlip(stake, houseFlip || Random.value < 0.5f));
    }

    /// <summary>Demo: the run's first coin flip up to houseFlipMaxBet is on the house (the toast and the end card say so).</summary>
    static bool IsHouseFlip(double stake)
    {
        if (!DemoMode.Active || !DemoRun.Exists) return false;
        DemoConfig config = DemoMode.Config;
        return config.houseFlip && !DemoRun.HouseFlipUsed && stake <= config.houseFlipMaxBet + 0.001;
    }

    IEnumerator CoinFlip(double stake, bool heads)
    {
        if (resultLayer != null) resultLayer.gameObject.SetActive(false);
        if (coinFlipLayer != null) coinFlipLayer.gameObject.SetActive(true);

        // Spin: squash the coin horizontally, swapping faces each half turn (real time: the game is paused)
        string headsLetter = CoinLetter(true), tailsLetter = CoinLetter(false);
        float duration = Mathf.Max(0.3f, coinFlipSeconds);
        float spins = 6.5f;
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(t / duration);
            float eased = 1f - (1f - progress) * (1f - progress);
            float angle = eased * spins * Mathf.PI;
            if (coin != null)
            {
                coin.localScale = new Vector3(Mathf.Max(0.04f, Mathf.Abs(Mathf.Cos(angle))), 1f, 1f);
                coin.anchoredPosition = new Vector2(0f, Mathf.Sin(progress * Mathf.PI) * 120f);
            }
            if (coinLetter != null)
            {
                bool showHeads = Mathf.FloorToInt(eased * spins + 0.5f) % 2 == 0;
                if (progress >= 1f) showHeads = heads;
                coinLetter.text = showHeads ? headsLetter : tailsLetter;
            }
            yield return null;
        }
        if (coin != null)
        {
            coin.localScale = Vector3.one;
            coin.anchoredPosition = Vector2.zero;
        }
        if (coinLetter != null) coinLetter.text = heads ? headsLetter : tailsLetter;
        GameSfx.Play(Sfx.CoinLand);

        // Settle the moment the face shows: a save or a quit from here on keeps the outcome
        pendingBet = 0.0;
        double before = BuildBudget;
        double valueBefore = valueBeforeBet;
        double payout = stake * winMultiplier;
        if (heads && Builder != null) Builder.AddBudget(payout);
        bool wasHouseFlip = houseFlip;
        houseFlip = false;
        if (wasHouseFlip) DemoRun.HouseFlipUsed = true;   // Demo: used once the win is paid (a cancelled flip keeps it)
        ReportSettled(stake, heads && Builder != null ? payout : 0.0);

        yield return new WaitForSecondsRealtime(0.35f);

        flipRoutine = null;
        flipping = false;
        ShowResult(heads ? "H" : "T", heads ? BlackjackResult.Win : BlackjackResult.Lose, stake, before, valueBefore);
        if (coinFlipLayer != null) coinFlipLayer.gameObject.SetActive(false);
        RefreshControls();
        if (wasHouseFlip) GameNotifications.Show(DemoMode.T("demo.build.house_flip"), NotificationKind.Special);   // Demo: honest about it
    }

    void CancelFlip()
    {
        if (flipRoutine != null) StopCoroutine(flipRoutine);
        flipRoutine = null;
        flipping = false;
        houseFlip = false;   // Demo: an unsettled house flip stays unused
        if (pendingBet > 0.0 && CompanyManager.Instance != null)
            PlayerWallet.Receive(pendingBet, "build gamble (refund)");
        pendingBet = 0.0;
        if (coinFlipLayer != null) coinFlipLayer.gameObject.SetActive(false);
        if (coin != null) coin.localScale = Vector3.one;
    }

    // ------------------------------------------------------------------
    // Blackjack
    // ------------------------------------------------------------------

    void StartBlackjack()
    {
        if (!TryGetBet(out double stake)) return;

        BlackjackGame table = BlackjackGame.Instance != null ? BlackjackGame.Instance : FindAnyObjectByType<BlackjackGame>(FindObjectsInactive.Include);
        bool tableVisible = table != null && table.blackjackPanel != null
                            && (table.blackjackPanel.transform.parent == null || table.blackjackPanel.transform.parent.gameObject.activeInHierarchy);
        if (table == null || !table.CanStartGame || !tableVisible)
        {
            Say(TowerText.Get("tower.gamble.no_table"), NotificationKind.Bad);
            return;
        }

        HideStatus();
        if (!TakeBet(stake))
        {
            Say(NotEnoughText(), NotificationKind.Bad);
            return;
        }
        handValueBefore = valueBeforeBet;
        handRunning = true;
        RefreshControls();

        // The table is on the HUD canvas, under the tower screen
        TowerView view = TowerView.Instance;
        if (view != null) view.SetCanvasVisible(false);
        if (ownCanvas != null) ownCanvas.enabled = false;

        string stakeText = TowerText.Get("tower.gamble.blackjack.stake", ("stake", UIFormat.Money(stake)), ("payout", UIFormat.Money(stake * winMultiplier)));
        // Listen first: a natural blackjack ends the hand inside StartGame
        handSettled = false;
        handTable = table;
        table.HandEnded -= OnHandEnded;
        table.HandEnded += OnHandEnded;
        bool started;
        try
        {
            started = table.StartGame(stakeText, OnBlackjackFinished);
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            started = false;
        }

        if (!started)
        {
            table.HandEnded -= OnHandEnded;
            handTable = null;
            handRunning = false;
            if (CompanyManager.Instance != null) PlayerWallet.Receive(pendingBet, "build gamble (refund)");
            pendingBet = 0.0;
            ShowScreenAgain();
            RefreshControls();
            Say(TowerText.Get("tower.gamble.blackjack_failed"), NotificationKind.Info);
        }
    }

    /// <summary>
    /// The hand is decided (its result is still on the table): settle now, so a save or a quit before
    /// Continue keeps the outcome. The result is shown when the table closes.
    /// </summary>
    void OnHandEnded(BlackjackResult result)
    {
        if (handTable != null) handTable.HandEnded -= OnHandEnded;
        handTable = null;
        if (!handRunning || handSettled) return;
        handSettled = true;
        handResult = result;
        handStake = pendingBet;
        pendingBet = 0.0;
        handBudgetBefore = BuildBudget;

        if (result == BlackjackResult.Win)
        {
            if (Builder != null) Builder.AddBudget(handStake * winMultiplier);
        }
        else if (result == BlackjackResult.Push)
        {
            if (CompanyManager.Instance != null) PlayerWallet.Receive(handStake, "build gamble (push)");
        }
        ReportSettled(handStake, result == BlackjackResult.Win ? (Builder != null ? handStake * winMultiplier : 0.0)
                               : result == BlackjackResult.Push ? handStake : 0.0);
    }

    void OnBlackjackFinished(BlackjackResult result)
    {
        // The table closed without reporting the end of the hand: settle now
        if (!handSettled) OnHandEnded(result);
        ShowScreenAgain();
        handRunning = false;

        if (isOpen)
        {
            ShowResult("21", handResult, handStake, handBudgetBefore, handValueBefore);
            RefreshControls();
        }
        else
        {
            // Build mode closed during the hand: just tell the player (a game notification, unless the tower screen is up)
            string text = handResult == BlackjackResult.Win
                ? TowerText.Get("tower.gamble.toast.won", ("amount", UIFormat.SignedMoney(handStake * winMultiplier)))
                : handResult == BlackjackResult.Push ? TowerText.Get("tower.gamble.toast.push", ("amount", UIFormat.Money(handStake)))
                : TowerText.Get("tower.gamble.toast.lost", ("amount", UIFormat.Money(handStake)));
            Say(text, handResult == BlackjackResult.Win ? NotificationKind.Good : NotificationKind.Info);
        }
    }

    void ShowScreenAgain()
    {
        TowerView view = TowerView.Instance;
        if (view != null) view.SetCanvasVisible(true);
        if (ownCanvas != null) ownCanvas.enabled = true;
    }

    static string StripTags(string text)
    {
        return System.Text.RegularExpressions.Regex.Replace(text ?? "", "<[^>]+>", "");
    }

    /// <summary>The result on the felt (TowerBudgetWon): the coin, the stamped slip, where the money went.</summary>
    void ShowResult(string coinFace, BlackjackResult outcome, double stake, double budgetBefore, double valueBefore)
    {
        bool coinFlip = coinFace != "21";
        bool won = outcome == BlackjackResult.Win;
        if (won && coinFlip) GameSfx.Play(Sfx.ChipsWin);   // a hand's win already sounded at the table
        shownCoinFace = coinFace;
        shownOutcome = outcome;
        shownStake = stake;
        shownBudgetBefore = budgetBefore;
        shownBudgetAfter = BuildBudget;
        shownValueBefore = valueBefore;
        shownValueAfter = CompanyValue();
        if (resultLayer == null)
        {
            ResultTexts(out string title, out string subtitle);
            Say(TowerText.Join(title, StripTags(subtitle)), won ? NotificationKind.Good : NotificationKind.Info);
            return;
        }

        FillResult();
        resultLayer.gameObject.SetActive(true);
        resultLayer.SetAsLastSibling();
        if (closeButton != null) closeButton.transform.SetAsLastSibling();
    }

    /// <summary>The shown result's title and subtitle in the current language.</summary>
    void ResultTexts(out string title, out string subtitle)
    {
        bool coinFlip = shownCoinFace != "21";
        bool won = shownOutcome == BlackjackResult.Win, push = shownOutcome == BlackjackResult.Push;
        string stake = UIFormat.Money(shownStake);
        title = TowerText.Get(coinFlip ? (won ? "tower.gamble.result.title.heads" : "tower.gamble.result.title.tails")
                              : won ? "tower.gamble.result.title.won" : push ? "tower.gamble.result.title.push" : "tower.gamble.result.title.lost");
        subtitle = won ? TowerText.Get("tower.gamble.result.subtitle.won", ("bet", stake), ("multiplier", "<b>" + Multiplier() + "</b>"),
                                       ("payout", UIFormat.Money(shownStake * winMultiplier)))
                 : push ? TowerText.Get("tower.gamble.result.subtitle.push", ("bet", stake))
                 : TowerText.Get("tower.gamble.result.subtitle.lost", ("bet", stake), ("budget", UIFormat.Money(shownBudgetBefore)));
    }

    /// <summary>Write the shown result on the felt (again after a language change).</summary>
    void FillResult()
    {
        bool coinFlip = shownCoinFace != "21";
        bool won = shownOutcome == BlackjackResult.Win, push = shownOutcome == BlackjackResult.Push;
        double payout = won ? shownStake * winMultiplier : 0.0;
        ResultTexts(out string title, out string subtitle);
        SetText(resultTitleText, title);
        SetText(resultSubtitleText, subtitle);
        SetText(resultCoinLetter, coinFlip ? CoinLetter(shownCoinFace == "H") : "21");
        SetText(resultCoinLabelText, TowerText.Get(coinFlip ? (shownCoinFace == "H" ? "tower.gamble.result.coin.heads" : "tower.gamble.result.coin.tails")
                                                            : "tower.gamble.result.coin.blackjack"));
        SetText(resultGameText, coinFlip
            ? TowerText.Get("tower.gamble.result.game.coin_flip", ("percent", Percent(0.5f)), ("bet", UIFormat.Money(shownStake)))
            : TowerText.Get("tower.gamble.result.game.blackjack", ("percent", Percent(blackjackWinChance)), ("bet", UIFormat.Money(shownStake))));
        Color stampColor = won ? UIColors.Money : push ? UIColors.Pencil : UIColors.Loss;
        // Capitals in the String Table (the stamp's Caps style only spaces them)
        SetText(resultStampText, TowerText.Get(won ? "tower.result.stamp.won" : push ? "tower.result.stamp.push" : "tower.result.stamp.lost"));
        if (resultStampText != null) resultStampText.color = stampColor;
        if (resultStampBox != null)
        {
            resultStampBox.Border = stampColor;
            resultStampBox.SetOutline(stampColor, 2f, 4f);
        }
        SetText(resultAmountText, "+" + UIFormat.Money(payout));
        if (resultAmountText != null) resultAmountText.color = won ? UIColors.Money : UIColors.Ink;
        SetText(resultBudgetDeltaText, won ? "<color=#1E7B45>" + TowerText.Get("tower.gamble.result.budget.win", ("amount", "+" + UIFormat.Money(payout))) + "</color>"
                                           : "<color=#676B74>" + TowerText.Get("tower.gamble.result.budget.none") + "</color>");
        SetText(resultBudgetText, "<color=#474F60>" + UIFormat.Money(shownBudgetBefore) + "  →</color>  <b>" + UIFormat.Money(shownBudgetAfter) + "</b>");
        SetText(resultValueDeltaText, push ? "<color=#676B74>" + TowerText.Get("tower.gamble.result.value.tie", ("amount", "±$0")) + "</color>"
                                           : "<color=#C0352B>" + TowerText.Get("tower.gamble.result.value.bet", ("amount", UIFormat.Minus + UIFormat.Money(shownStake))) + "</color>");
        SetText(resultValueText, "<color=#474F60>" + UIFormat.Money(shownValueBefore) + "  →</color>  <b>" + UIFormat.Money(shownValueAfter) + "</b>");
        SetText(resultButtonText, TowerText.Get("tower.gamble.button.again"));
    }

    string Multiplier()
    {
        return winMultiplier.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "×";
    }

    static void SetText(TextMeshProUGUI text, string value)
    {
        if (text != null) text.text = value;
    }

    // ------------------------------------------------------------------
    // Feedback
    // ------------------------------------------------------------------

    /// <summary>
    /// Tell the player something where it can be seen: on the felt while the pop-up is on screen, else on the tower
    /// screen's message chip while that is on screen, else as a game notification (the HUD's toasts are under the tower
    /// screen, so they only work while it's closed or hidden).
    /// </summary>
    void Say(string text, NotificationKind kind)
    {
        if (string.IsNullOrEmpty(text)) return;
        bool error = kind == NotificationKind.Bad;
        if (statusText != null && PopupShowing)
        {
            ShowStatus(text, error);
            return;
        }
        TowerView view = TowerView.Instance;
        if (view != null && view.IsShowing)
        {
            view.ShowMessage(text, error);   // above the pop-up's scrim
            return;
        }
        GameNotifications.Show(text, kind);
    }

    /// <summary>The pop-up is open and on screen (not hidden with the tower screen for the blackjack table).</summary>
    bool PopupShowing
    {
        get
        {
            if (!isOpen || root == null || !root.gameObject.activeInHierarchy) return false;
            if (ownCanvas != null) return ownCanvas.enabled;
            TowerView view = TowerView.Instance;
            return view == null || view.IsShowing;
        }
    }

    GameObject StatusObject => statusChip != null ? statusChip.gameObject : statusText != null ? statusText.gameObject : null;

    void ShowStatus(string text, bool error)
    {
        GameObject target = StatusObject;
        if (target == null) return;
        statusText.text = text;
        BlueprintBox box = statusChip != null ? statusChip.GetComponent<BlueprintBox>() : null;
        if (box != null) box.Fill = error ? UIColors.LossSoft : UIColors.PaperHi;
        if (statusGroup == null)
        {
            statusGroup = target.GetComponent<CanvasGroup>();
            if (statusGroup == null) statusGroup = target.AddComponent<CanvasGroup>();
            statusGroup.blocksRaycasts = false;
        }
        statusGroup.alpha = 1f;
        target.SetActive(true);
        target.transform.SetAsLastSibling();   // above the coin flip and result layers
        statusUntil = Time.unscaledTime + (error ? 3.5f : 2.4f);
    }

    void HideStatus()
    {
        GameObject target = StatusObject;
        if (target != null && target.activeSelf) target.SetActive(false);
    }

    /// <summary>Fade the status out after a few seconds (real time: the game is paused).</summary>
    void UpdateStatus()
    {
        GameObject target = StatusObject;
        if (target == null || !target.activeSelf) return;
        float left = statusUntil - Time.unscaledTime;
        if (left <= 0f)
        {
            target.SetActive(false);
        }
        else if (statusGroup != null)
        {
            float alpha = Mathf.Clamp01(left / 0.3f);
            if (!Mathf.Approximately(statusGroup.alpha, alpha)) statusGroup.alpha = alpha;
        }
    }

    // ------------------------------------------------------------------
    // Runtime layout (TowerBudget at 1920 x 1080: a felt panel 1120 x 724 in the middle, 14 px wooden rim)
    // ------------------------------------------------------------------

    static void Build(BuildGambleView view, RectTransform parent, TMP_FontAsset font)
    {
        RectTransform root = BlueprintUI.Stretch(BlueprintUI.NewRect("FillTheBudget", parent));
        root.gameObject.SetActive(false);
        view.root = root;

        // Dim everything behind (and take its clicks)
        Image scrim = root.gameObject.AddComponent<Image>();
        scrim.color = UIColors.ScrimLight;
        scrim.raycastTarget = true;

        // Felt panel with a wooden rim and a gold line
        BlueprintBox table = BlueprintUI.Box(root, "Table", UIColors.Felt, UIColors.ClipboardDark, 14f, BlueprintUI.RadiusCard, true);
        table.SetGradient(UIColors.FeltHi, new Vector2(0.5f, 0.35f));
        table.SetShadow(new Color(0f, 0f, 0f, 0.45f), new Vector2(0f, 22f), 48f);
        RectTransform tableRect = BlueprintUI.Centered(table.rectTransform, 1120f, 724f);
        BlueprintBox goldLine = BlueprintUI.Box(table.transform, "GoldLine", Color.clear, UIColors.WithAlpha(UIColors.Gold, 0.8f), 3f, BlueprintUI.RadiusCard);
        BlueprintUI.Stretch(goldLine.rectTransform, 26f, 26f, 26f, 26f);

        // Everything is placed from the felt's inner top-left corner (inside the 14 px rim), as on the board
        RectTransform inner = BlueprintUI.Stretch(BlueprintUI.NewRect("Inner", tableRect), 14f, 14f, 14f, 14f);
        RectTransform bet = BlueprintUI.Stretch(BlueprintUI.NewRect("Bet", inner));
        BuildBetLayer(view, bet, font);

        // Round close button (top right)
        BlueprintBox close = BlueprintUI.Box(inner, "Close", UIColors.PaperHi, UIColors.Ink, 3f, new Vector4(30f, 27.6f, 31.2f, 28.8f), true);
        close.SetShadow(UIColors.Ink, new Vector2(3f, 4f));
        BlueprintUI.TopLeft(close.rectTransform, 1004f, 24f, 60f, 60f);
        TowerIcon closeIcon = TowerViewFactory.Icon(close.transform, TowerIconKind.Close, 28f, UIColors.Ink);
        BlueprintUI.Layout(closeIcon).ignoreLayout = true;
        BlueprintUI.Centered(closeIcon.rectTransform, 28f, 28f);
        view.closeButton = BlueprintUI.MakeButton(close);

        // Coin flip animation over the felt
        RectTransform flipLayer = BlueprintUI.Stretch(BlueprintUI.NewRect("CoinFlip", tableRect));
        BlueprintUI.AddBox(flipLayer, new Color(0.063f, 0.051f, 0.031f, 0.5f), Color.clear, 0f, BlueprintUI.RadiusCard, true);
        view.coinFlipLayer = flipLayer;
        view.coin = Coin(flipLayer, font, 148f, out view.coinLetter);
        flipLayer.gameObject.SetActive(false);

        BuildResultLayer(view, inner, font);
        close.transform.SetAsLastSibling();

        // Status chip (bottom right of the felt, beside the buttons)
        BlueprintBox status = BlueprintUI.Box(inner, "Status", UIColors.PaperHi, UIColors.Ink, 2f, BlueprintUI.RadiusSmall);
        status.SetShadow(BlueprintUI.FeltShadow, new Vector2(3f, 4f));
        BlueprintUI.TopLeft(status.rectTransform, 604f, 604f, 444f, 60f);
        BlueprintUI.HRow(status, 0f, new RectOffset(16, 16, 6, 6), TextAnchor.MiddleCenter);
        view.statusText = BlueprintUI.Wrap(BlueprintUI.Text(status.transform, "Text", "", 19f, UIColors.Ink, font, TextAlignmentOptions.Center));
        BlueprintUI.Layout(view.statusText, 0f, -1f, 1f, -1f, 0f);
        view.statusChip = status.rectTransform;
        status.gameObject.SetActive(false);
    }

    static void BuildBetLayer(BuildGambleView view, RectTransform inner, TMP_FontAsset font)
    {
        // Title: coin, "Fill the build budget", a sparkle; the rule under it
        RectTransform titleRow = BlueprintUI.NewRect("Title", inner);
        BlueprintUI.TopLeft(titleRow, 44f, 30f, 900f, 52f);
        BlueprintUI.HRow(titleRow, 16f, new RectOffset(), TextAnchor.MiddleLeft);
        Badge(titleRow, 52f, 32f);
        Fixed(BlueprintUI.AutoSize(BlueprintUI.Text(titleRow, "Text", "", 44f, UIColors.Gold, font, TextAlignmentOptions.Left, true), 28f), "tower.gamble.title");
        TowerViewFactory.Icon(titleRow, TowerIconKind.Sparkle, 40f, UIColors.Gold, 2.2f);
        TextMeshProUGUI rule = BlueprintUI.AutoSize(BlueprintUI.Text(inner, "Rule", "", 22f, UIColors.PaperHi, font), 15f);
        L10nText.Attach(rule, TowerText.Table, "tower.gamble.rule", () => new object[] { L10n.Args(("multiplier", "<b>" + view.Multiplier() + "</b>")) });
        BlueprintUI.TopLeft(rule.rectTransform, 44f, 88f, 920f, 30f);

        // Where the money is now
        RectTransform money = BlueprintUI.NewRect("Money", inner);
        BlueprintUI.TopLeft(money, 44f, 138f, 900f, 52f);
        BlueprintUI.HRow(money, 16f, new RectOffset(), TextAnchor.MiddleLeft);
        view.budgetText = MoneyChip(money, "BuildBudget", "tower.label.build_budget", true, font);
        view.companyValueText = MoneyChip(money, "CompanyValue", "tower.label.company_value", false, font);
        // Cash Grab: the bets come from Player Cash, so the chip says "Your cash"
        if (view.companyValueText != null && view.companyValueText.transform.parent.Find("Label") is Transform cashLabel &&
            cashLabel.TryGetComponent(out TextMeshProUGUI cashCaption))
            L10nText.Attach(cashCaption, CashText.Table, "cash.wallet.label");

        // Pick a game (the odds follow the pop-up's rules)
        Caps(inner, "PickLabel", "tower.gamble.pick_game", 44f, 214f, font);
        view.coinFlipButton = GameCard(inner, "CoinFlipCard", "tower.gamble.card.coin_flip", "tower.gamble.card.coin_flip.odds", () => Percent(0.5f), null,
                                       true, 44f, 250f, font);
        view.blackjackButton = GameCard(inner, "BlackjackCard", "tower.gamble.card.blackjack", "tower.gamble.card.blackjack.odds",
                                        () => Percent(view.blackjackWinChance), "tower.gamble.card.blackjack.note", false, 342f, 250f, font);

        // The bet, from company value: - [field] + and the slider
        Caps(inner, "BetLabel", "tower.gamble.bet_label", 44f, 362f, font);
        view.minusButton = SquareButton(inner, "Lower", TowerIconKind.Minus, 44f, 392f);
        view.betInput = BetInput(inner, font, 108f, 392f);
        view.plusButton = SquareButton(inner, "Raise", TowerIconKind.Plus, 316f, 392f);
        view.betSlider = BetSlider(inner, 388f, 392f, 236f);
        view.sliderMinText = BlueprintUI.Text(inner, "Min", "$100", 15f, UIColors.PaperHi, font);
        BlueprintUI.TopLeft(view.sliderMinText.rectTransform, 388f, 430f, 118f, 22f);
        view.sliderMaxText = BlueprintUI.AutoSize(BlueprintUI.Text(inner, "Max", "$0", 15f, UIColors.PaperHi, font, TextAlignmentOptions.Right), 11f);
        BlueprintUI.TopLeft(view.sliderMaxText.rectTransform, 506f, 430f, 118f, 22f);

        // Presets: $1,000 · $5,000 · $10,000 · MAX
        string[] labels = { "$1,000", "$5,000", "$10,000", "MAX" };
        view.quickBetButtons = new Button[labels.Length];
        const float presetWidth = (580f - 12f * 3f) / 4f;
        for (int i = 0; i < labels.Length; i++)
        {
            bool max = i == labels.Length - 1;
            BlueprintBox box = BlueprintUI.Box(inner, "Quick " + labels[i], UIColors.PaperHi, UIColors.Ink, 2f, BlueprintUI.RadiusSmall, true);
            box.SetShadow(BlueprintUI.FeltShadow, new Vector2(3f, 4f));
            BlueprintUI.TopLeft(box.rectTransform, 44f + i * (presetWidth + 12f), 460f, presetWidth, 52f);
            BlueprintUI.HRow(box, 8f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleCenter);
            if (max) TowerViewFactory.Icon(box.transform, TowerIconKind.Warning, 22f, UIColors.Loss, 2.6f);
            TextMeshProUGUI label = BlueprintUI.Text(box.transform, "Label", labels[i], 22f, UIColors.Ink, font, TextAlignmentOptions.Center, max);
            if (max)
            {
                label.characterSpacing = 8f;
                Fixed(label, "tower.gamble.preset.max");   // "MAX"
            }
            view.quickBetButtons[i] = BlueprintUI.MakeButton(box);
        }

        // What MAX bets and what the company keeps (written when the pop-up opens)
        BlueprintBox note = BlueprintUI.Box(inner, "KeepNote", UIColors.PaperHi, UIColors.Ink, 2f, BlueprintUI.RadiusSmall);
        note.SetShadow(BlueprintUI.FeltShadow, new Vector2(3f, 4f));
        BlueprintUI.TopLeft(note.rectTransform, 44f, 524f, 580f, 50f);
        BlueprintUI.HRow(note, 10f, new RectOffset(14, 14, 0, 0), TextAnchor.MiddleLeft);
        TowerViewFactory.Icon(note.transform, TowerIconKind.Warning, 24f, UIColors.Loss);
        view.keepHintText = BlueprintUI.AutoSize(BlueprintUI.Text(note.transform, "Text", "", 19f, UIColors.Ink, font), 13f);
        BlueprintUI.Layout(view.keepHintText, 0f, -1f, 1f, -1f, 0f);

        // The honest odds for the picked game and bet
        BuildOdds(view, inner, font);

        // Actions: the big gold button, and a way out
        RectTransform actions = BlueprintUI.NewRect("Actions", inner);
        BlueprintUI.TopLeft(actions, 44f, 590f, 560f, 80f);
        BlueprintUI.HRow(actions, 28f, new RectOffset(), TextAnchor.MiddleLeft);
        BlueprintBox play = BlueprintUI.Box(actions, "Play", UIColors.Gold, UIColors.Ink, 3f, BlueprintUI.RadiusLarge, true);
        play.SetShadow(BlueprintUI.FeltShadow, new Vector2(5f, 6f));
        BlueprintUI.HRow(play, 14f, new RectOffset(34, 34, 0, 0), TextAnchor.MiddleCenter);
        BlueprintUI.Layout(play, -1f, 80f, -1f, -1f, -1f, 80f);
        TowerViewFactory.Icon(play.transform, TowerIconKind.Coin, 32f, UIColors.Ink);
        view.playButtonText = BlueprintUI.AutoSize(BlueprintUI.Text(play.transform, "Label", "", 32f, UIColors.Ink, font, TextAlignmentOptions.Center, true), 20f);
        view.playButton = BlueprintUI.MakeButton(play);
        RectTransform notNow = BlueprintUI.NewRect("NotNow", actions);
        BlueprintUI.HRow(notNow, 12f, new RectOffset(), TextAnchor.MiddleLeft);
        Image hit = notNow.gameObject.AddComponent<Image>();
        hit.color = Color.clear;
        TextMeshProUGUI notNowText = Fixed(BlueprintUI.Text(notNow, "Label", "", 26f, UIColors.PaperHi, font), "tower.gamble.not_now");
        notNowText.fontStyle = FontStyles.Underline;
        BlueprintUI.Keycap(notNow, "Esc", font);
        view.cancelButton = notNow.gameObject.AddComponent<Button>();
        view.cancelButton.targetGraphic = hit;
        view.cancelButton.transition = Selectable.Transition.None;
        view.cancelButton.navigation = new Navigation { mode = Navigation.Mode.None };
    }

    static void BuildOdds(BuildGambleView view, RectTransform inner, TMP_FontAsset font)
    {
        BlueprintBox box = BlueprintUI.Box(inner, "Odds", UIColors.PaperHi, UIColors.Ink, 2f, BlueprintUI.RadiusLarge);
        box.SetShadow(BlueprintUI.FeltShadow, new Vector2(3f, 4f));
        BlueprintUI.TopLeft(box.rectTransform, 644f, 214f, 404f, 360f);
        BlueprintUI.VColumn(box, 10f, new RectOffset(20, 20, 16, 16));
        // Written by UpdateBetControls (the title in capitals from the String Table)
        view.oddsTitleText = BlueprintUI.AutoSize(BlueprintUI.Caps(BlueprintUI.Text(box.transform, "Title", "", 19f, UIColors.Pencil, font)), 14f);
        RectTransform bar = BlueprintUI.NewRect("Bar", box.transform);
        BlueprintUI.Layout(bar, -1f, 18f, -1f, -1f, -1f, 18f);
        view.oddsBar = bar.gameObject.AddComponent<BlueprintGraphic>();
        view.oddsBar.raycastTarget = false;
        OddsRow(box.transform, "Win", TowerIconKind.TrendUp, UIColors.Money, font, out view.winLabelText, out view.winPreviewText);
        view.pushRow = OddsRow(box.transform, "Push", TowerIconKind.Minus, UIColors.Pencil, font, out view.pushLabelText, out view.pushPreviewText);
        OddsRow(box.transform, "Lose", TowerIconKind.TrendDown, UIColors.Loss, font, out view.loseLabelText, out view.losePreviewText);
        BlueprintBox divider = BlueprintUI.Box(box.transform, "Divider", UIColors.WithAlpha(UIColors.Ink, 0.2f), Color.clear, 0f, BlueprintUI.Round(0f));
        BlueprintUI.Layout(divider, -1f, 2f, -1f, -1f, -1f, 2f);
        view.oddsNoteText = BlueprintUI.Wrap(BlueprintUI.Text(box.transform, "Note", "", 19f, UIColors.Ink, font));
        view.oddsAverageText = BlueprintUI.Wrap(BlueprintUI.Text(box.transform, "Average", "", 17f, UIColors.InkSoft, font));
    }

    static RectTransform OddsRow(Transform parent, string name, TowerIconKind icon, Color color, TMP_FontAsset font, out TextMeshProUGUI label,
                                 out TextMeshProUGUI value)
    {
        RectTransform row = BlueprintUI.NewRect(name, parent);
        BlueprintUI.HRow(row, 8f, new RectOffset(), TextAnchor.MiddleLeft);
        BlueprintUI.Layout(row, -1f, 30f, -1f, -1f, -1f, 30f);
        TowerViewFactory.Icon(row, icon, 24f, color);
        label = BlueprintUI.AutoSize(BlueprintUI.Text(row, "Label", "", 19f, color, font, TextAlignmentOptions.Left, true), 14f);
        BlueprintUI.Layout(label, 0f, -1f, 1f, -1f, 0f);
        value = BlueprintUI.Text(row, "Value", "", 17f, color == UIColors.Pencil ? UIColors.Ink : color, font, TextAlignmentOptions.Right);
        return row;
    }

    /// <summary>Small spaced capitals: the String Table entry <paramref name="key"/> (capitals written so there).</summary>
    static void Caps(RectTransform parent, string name, string key, float x, float y, TMP_FontAsset font)
    {
        TextMeshProUGUI label = Fixed(BlueprintUI.AutoSize(BlueprintUI.Caps(BlueprintUI.Text(parent, name, "", 19f, UIColors.PaperHi, font)), 14f), key);
        BlueprintUI.TopLeft(label.rectTransform, x, y, 580f, 26f);
    }

    /// <summary>A fixed text: the Tower String Table entry <paramref name="key"/>, written again on a language change.</summary>
    static TextMeshProUGUI Fixed(TextMeshProUGUI text, string key)
    {
        L10nText.Attach(text, TowerText.Table, key);
        return text;
    }

    /// <summary>The gold coin badge with the coin icon.</summary>
    static void Badge(Transform parent, float size, float iconSize)
    {
        BlueprintBox badge = BlueprintUI.Box(parent, "Badge", UIColors.Gold, UIColors.Ink, 3f, BlueprintUI.Round(size * 0.5f));
        badge.SetGradient(UIColors.Hex(0xF7DC87), new Vector2(0.38f, 0.32f));
        badge.SetShadow(BlueprintUI.FeltShadow, new Vector2(0f, 3f));
        BlueprintUI.Layout(badge, size, size, 0f, 0f, size, size);
        TowerIcon icon = TowerViewFactory.Icon(badge.transform, TowerIconKind.Coin, iconSize, UIColors.Ink);
        BlueprintUI.Layout(icon).ignoreLayout = true;
        BlueprintUI.Centered(icon.rectTransform, iconSize, iconSize);
    }

    static TextMeshProUGUI MoneyChip(Transform parent, string name, string labelKey, bool coin, TMP_FontAsset font)
    {
        BlueprintBox chip = BlueprintUI.Box(parent, name, UIColors.PaperHi, UIColors.Ink, 2f, BlueprintUI.RadiusSmall);
        chip.SetShadow(BlueprintUI.FeltShadow, new Vector2(3f, 4f));
        BlueprintUI.HRow(chip, 10f, new RectOffset(coin ? 8 : 12, 16, 0, 0), TextAnchor.MiddleLeft);
        BlueprintUI.Layout(chip, -1f, 52f, -1f, -1f, -1f, 52f);
        if (coin) Badge(chip.transform, 34f, 22f);
        else TowerViewFactory.Icon(chip.transform, TowerIconKind.Cash, 28f, UIColors.Ink);
        Fixed(BlueprintUI.Text(chip.transform, "Label", "", 19f, UIColors.InkSoft, font), labelKey);
        return BlueprintUI.Text(chip.transform, "Amount", "$0", 26f, UIColors.Ink, font, TextAlignmentOptions.Left, true);
    }

    /// <summary>
    /// A game card: its name, the chance to win (<paramref name="oddsKey"/> with <paramref name="percent"/>, asked again on
    /// every language change) and an optional note, all from the String Table.
    /// </summary>
    static Button GameCard(RectTransform parent, string name, string titleKey, string oddsKey, System.Func<string> percent, string noteKey, bool coin,
                           float x, float y, TMP_FontAsset font)
    {
        BlueprintBox box = BlueprintUI.Box(parent, name, UIColors.PaperHi, UIColors.Ink, 3f, BlueprintUI.RadiusLarge, true);
        box.SetShadow(BlueprintUI.FeltShadow, new Vector2(4f, 5f));
        BlueprintUI.TopLeft(box.rectTransform, x, y, 282f, 96f);
        BlueprintUI.HRow(box, 10f, new RectOffset(14, 14, 0, 0), TextAnchor.MiddleLeft);
        if (coin)
        {
            Badge(box.transform, 40f, 28f);
        }
        else
        {
            BlueprintBox cards = BlueprintUI.Box(box.transform, "IconBox", UIColors.PaperHi, UIColors.Ink, 2f, BlueprintUI.RadiusSmall);
            BlueprintUI.Layout(cards, 40f, 40f, 0f, 0f, 40f, 40f);
            TowerIcon icon = TowerViewFactory.Icon(cards.transform, TowerIconKind.Cards, 28f, UIColors.Ink);
            BlueprintUI.Layout(icon).ignoreLayout = true;
            BlueprintUI.Centered(icon.rectTransform, 28f, 28f);
        }
        RectTransform texts = BlueprintUI.NewRect("Texts", box.transform);
        BlueprintUI.VColumn(texts, 1f, new RectOffset(), TextAnchor.MiddleLeft);
        BlueprintUI.Layout(texts, 0f, -1f, 1f, -1f, 0f);
        Fixed(BlueprintUI.AutoSize(BlueprintUI.Text(texts, "Name", "", 26f, UIColors.Ink, font), 18f), titleKey);   // StyleGameCard bolds "Name"
        TextMeshProUGUI odds = BlueprintUI.AutoSize(BlueprintUI.Text(texts, "Odds", "", 19f, UIColors.Ink, font), 13f);
        L10nText.Attach(odds, TowerText.Table, oddsKey, () => new object[] { L10n.Args(("percent", percent())) });
        if (noteKey != null) Fixed(BlueprintUI.AutoSize(BlueprintUI.Text(texts, "Note", "", 17f, UIColors.InkSoft, font), 12f), noteKey);
        return BlueprintUI.MakeButton(box);
    }

    static Button SquareButton(RectTransform parent, string label, TowerIconKind icon, float x, float y)
    {
        BlueprintBox box = BlueprintUI.Box(parent, label, UIColors.PaperHi, UIColors.Ink, 3f, BlueprintUI.RadiusSmall, true);
        box.SetShadow(BlueprintUI.FeltShadow, new Vector2(3f, 4f));
        BlueprintUI.TopLeft(box.rectTransform, x, y, 56f, 56f);
        TowerIcon glyph = TowerViewFactory.Icon(box.transform, icon, 26f, UIColors.Ink);
        BlueprintUI.Layout(glyph).ignoreLayout = true;
        BlueprintUI.Centered(glyph.rectTransform, 26f, 26f);
        return BlueprintUI.MakeButton(box);
    }

    static TMP_InputField BetInput(RectTransform parent, TMP_FontAsset font, float x, float y)
    {
        BlueprintBox box = BlueprintUI.Box(parent, "Bet", UIColors.PaperHi, UIColors.Ink, 3f, BlueprintUI.RadiusSmall, true);
        BlueprintUI.TopLeft(box.rectTransform, x, y, 200f, 56f);
        RectTransform area = BlueprintUI.Stretch(BlueprintUI.NewRect("Text Area", box.transform), 12f, 4f, 12f, 4f);
        area.gameObject.AddComponent<RectMask2D>();
        TextMeshProUGUI placeholder = BlueprintUI.Text(area, "Placeholder", "$100", BetFontSize, UIColors.Pencil, font, TextAlignmentOptions.Center, true);
        BlueprintUI.Stretch(placeholder.rectTransform);
        TextMeshProUGUI text = BlueprintUI.Text(area, "Text", "", BetFontSize, UIColors.Ink, font, TextAlignmentOptions.Center, true);
        BlueprintUI.Stretch(text.rectTransform);
        // $1,000,000,000 and up would be cut off by the mask at full size: shrink to fit instead (overflow stays as it is)
        text.enableAutoSizing = true;
        text.fontSizeMin = MinBetFontSize;
        text.fontSizeMax = BetFontSize;

        TMP_InputField input = box.gameObject.AddComponent<TMP_InputField>();
        input.targetGraphic = box;
        input.colors = BlueprintUI.Tint();
        input.navigation = new Navigation { mode = Navigation.Mode.None };
        input.textViewport = area;
        input.textComponent = text;
        input.placeholder = placeholder;
        input.contentType = TMP_InputField.ContentType.Standard;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.characterLimit = 15;
        input.customCaretColor = true;
        input.caretColor = UIColors.Ink;
        input.caretWidth = 2;
        input.selectionColor = UIColors.WithAlpha(UIColors.Highlight, 0.6f);
        if (font != null) input.fontAsset = font;
        return input;
    }

    static Slider BetSlider(RectTransform parent, float x, float y, float width)
    {
        RectTransform rect = BlueprintUI.NewRect("BetSlider", parent);
        BlueprintUI.TopLeft(rect, x, y, width, 36f);

        BlueprintBox track = BlueprintUI.Box(rect, "Track", UIColors.PaperHi, UIColors.Ink, 2f, BlueprintUI.RadiusSmall * 0.5f, true);
        RectTransform trackRect = track.rectTransform;
        trackRect.anchorMin = new Vector2(0f, 0.5f);
        trackRect.anchorMax = new Vector2(1f, 0.5f);
        trackRect.sizeDelta = new Vector2(0f, 12f);
        trackRect.anchoredPosition = Vector2.zero;

        RectTransform fillArea = BlueprintUI.NewRect("Fill Area", rect);
        fillArea.anchorMin = new Vector2(0f, 0.5f);
        fillArea.anchorMax = new Vector2(1f, 0.5f);
        fillArea.offsetMin = new Vector2(2f, -4f);
        fillArea.offsetMax = new Vector2(-2f, 4f);
        BlueprintBox fill = BlueprintUI.Box(fillArea, "Fill", UIColors.Highlight, Color.clear, 0f, BlueprintUI.Round(3f));
        fill.rectTransform.anchorMin = Vector2.zero;
        fill.rectTransform.anchorMax = new Vector2(0f, 1f);   // the slider sets the horizontal anchors
        fill.rectTransform.sizeDelta = Vector2.zero;

        RectTransform handleArea = BlueprintUI.NewRect("Handle Slide Area", rect);
        BlueprintUI.Stretch(handleArea, 15f, 0f, 15f, 0f);
        BlueprintBox handle = BlueprintUI.Box(handleArea, "Handle", UIColors.Gold, UIColors.Ink, 3f, BlueprintUI.Round(15f), true);
        handle.SetGradient(UIColors.Hex(0xF5D77A), new Vector2(0.35f, 0.35f));
        handle.rectTransform.sizeDelta = new Vector2(30f, 30f);

        Slider slider = rect.gameObject.AddComponent<Slider>();
        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;
        slider.colors = BlueprintUI.Tint();
        slider.navigation = new Navigation { mode = Navigation.Mode.None };
        return slider;
    }

    /// <summary>Gold coin with an inner ring, a dashed ring and a letter (H / T / 21).</summary>
    static RectTransform Coin(RectTransform parent, TMP_FontAsset font, float size, out TextMeshProUGUI letter)
    {
        BlueprintBox coin = BlueprintUI.Box(parent, "Coin", UIColors.Gold, UIColors.Ink, 4f, BlueprintUI.Round(size * 0.5f));
        coin.SetGradient(UIColors.Hex(0xF7DC87), new Vector2(0.38f, 0.32f));
        coin.SetShadow(UIColors.GoldDark, new Vector2(0f, size * 0.05f));
        RectTransform rect = BlueprintUI.Centered(coin.rectTransform, size, size);
        BlueprintUI.Layout(coin).ignoreLayout = true;
        BlueprintBox ring = BlueprintUI.Box(coin.transform, "Ring", Color.clear, UIColors.GoldDark, 3f, BlueprintUI.Round(size * 0.5f - size * 0.06f));
        BlueprintUI.Stretch(ring.rectTransform, size * 0.06f, size * 0.06f, size * 0.06f, size * 0.06f);
        BlueprintBox dashes = BlueprintUI.Box(coin.transform, "Dashes", Color.clear, UIColors.WithAlpha(UIColors.GoldDark, 0.7f), 2f, BlueprintUI.Round(size * 0.5f - size * 0.1f));
        dashes.SetDash(true, 6f, 5f);
        BlueprintUI.Stretch(dashes.rectTransform, size * 0.1f, size * 0.1f, size * 0.1f, size * 0.1f);
        letter = BlueprintUI.Text(coin.transform, "Letter", "", size * 0.56f, UIColors.GoldDark, font, TextAlignmentOptions.Center, true);   // CoinLetter / "21"
        BlueprintUI.Stretch(letter.rectTransform);
        letter.enableAutoSizing = true;
        letter.fontSizeMin = size * 0.25f;
        letter.fontSizeMax = size * 0.56f;
        return rect;
    }

    static void BuildResultLayer(BuildGambleView view, RectTransform inner, TMP_FontAsset font)
    {
        // The same felt after the game: title, the coin on the left, the stamped slip on the right, two buttons
        RectTransform layer = BlueprintUI.Stretch(BlueprintUI.NewRect("Result", inner), -2f, -2f, -2f, -2f);
        BlueprintBox felt = BlueprintUI.AddBox(layer, UIColors.Felt, Color.clear, 0f, BlueprintUI.RadiusCard, true);
        felt.SetGradient(UIColors.FeltHi, new Vector2(0.5f, 0.35f));
        BlueprintBox goldLine = BlueprintUI.Box(layer, "GoldLine", Color.clear, UIColors.WithAlpha(UIColors.Gold, 0.8f), 3f, BlueprintUI.RadiusCard);
        BlueprintUI.Stretch(goldLine.rectTransform, 14f, 14f, 14f, 14f);

        RectTransform titleRow = BlueprintUI.NewRect("Title", layer);
        BlueprintUI.TopLeft(titleRow, 46f, 32f, 940f, 52f);
        BlueprintUI.HRow(titleRow, 16f, new RectOffset(), TextAnchor.MiddleLeft);
        Badge(titleRow, 52f, 32f);
        // FillResult writes the result (the fixed words keep to the String Table themselves)
        view.resultTitleText = BlueprintUI.AutoSize(BlueprintUI.Text(titleRow, "Text", "", 44f, UIColors.Gold, font, TextAlignmentOptions.Left, true), 28f);
        view.resultSubtitleText = BlueprintUI.AutoSize(BlueprintUI.Text(layer, "Subtitle", "", 22f, UIColors.PaperHi, font), 16f);
        BlueprintUI.TopLeft(view.resultSubtitleText.rectTransform, 46f, 90f, 940f, 30f);

        // The coin, landed
        TowerIcon sparkle = TowerViewFactory.Icon(layer, TowerIconKind.Sparkle, 44f, UIColors.Gold, 2.2f);
        BlueprintUI.Layout(sparkle).ignoreLayout = true;
        BlueprintUI.TopLeft(sparkle.rectTransform, 62f, 162f, 44f, 44f);
        TowerIcon sparkle2 = TowerViewFactory.Icon(layer, TowerIconKind.Sparkle, 36f, UIColors.Gold, 2.2f);
        BlueprintUI.Layout(sparkle2).ignoreLayout = true;
        BlueprintUI.TopLeft(sparkle2.rectTransform, 294f, 362f, 36f, 36f);
        RectTransform coinHolder = BlueprintUI.NewRect("CoinHolder", layer);
        BlueprintUI.TopLeft(coinHolder, 86f, 172f, 240f, 240f);
        Coin(coinHolder, font, 240f, out view.resultCoinLetter);
        view.resultCoinLabelText = BlueprintUI.AutoSize(BlueprintUI.Text(layer, "CoinLabel", "", 26f, UIColors.Gold, font, TextAlignmentOptions.Center, true), 18f);
        BlueprintUI.TopLeft(view.resultCoinLabelText.rectTransform, 46f, 466f, 320f, 32f);
        view.resultGameText = BlueprintUI.AutoSize(BlueprintUI.Text(layer, "Game", "", 19f, UIColors.PaperHi, font, TextAlignmentOptions.Center), 14f);
        BlueprintUI.TopLeft(view.resultGameText.rectTransform, 46f, 500f, 320f, 26f);

        // The slip with the stamp
        BlueprintBox slip = BlueprintUI.Box(layer, "Slip", UIColors.PaperHi, UIColors.Ink, 3f, BlueprintUI.RadiusLarge);
        slip.SetShadow(new Color(0f, 0f, 0f, 0.45f), new Vector2(0f, 22f), 48f);
        BlueprintUI.TopLeft(slip.rectTransform, 386f, 152f, 664f, 402f);
        BlueprintBox tape = BlueprintUI.Box(slip.transform, "Tape", UIColors.Tape, Color.clear, 0f, BlueprintUI.Round(0f));
        tape.SetShadow(new Color(0f, 0f, 0f, 0.18f), new Vector2(0f, 2f), 4f);
        RectTransform tapeRect = tape.rectTransform;
        tapeRect.anchorMin = tapeRect.anchorMax = new Vector2(0f, 1f);
        tapeRect.pivot = new Vector2(0.5f, 0.5f);
        tapeRect.sizeDelta = new Vector2(150f, 38f);
        tapeRect.anchoredPosition = new Vector2(332f, 0f);
        tapeRect.localEulerAngles = new Vector3(0f, 0f, 3f);

        BlueprintBox stamp = BlueprintUI.Box(slip.transform, "Stamp", Color.clear, UIColors.Money, 5f, BlueprintUI.Round(10f));
        stamp.SetOutline(UIColors.Money, 2f, 4f);
        BlueprintUI.TopRight(stamp.rectTransform, 40f, 34f, 190f, 66f);
        stamp.rectTransform.localEulerAngles = new Vector3(0f, 0f, 10f);
        view.resultStampText = BlueprintUI.AutoSize(BlueprintUI.Caps(BlueprintUI.Text(stamp.transform, "Text", "", 44f, UIColors.Money, font, TextAlignmentOptions.Center, true), 0.12f), 24f);
        BlueprintUI.Stretch(view.resultStampText.rectTransform);
        view.resultStampBox = stamp;

        view.resultAmountText = BlueprintUI.Text(slip.transform, "Amount", "+$0", 54f, UIColors.Money, font, TextAlignmentOptions.Left, true);
        BlueprintUI.TopLeft(view.resultAmountText.rectTransform, 32f, 28f, 380f, 62f);
        TextMeshProUGUI toBudget = Fixed(BlueprintUI.AutoSize(BlueprintUI.Text(slip.transform, "AmountNote", "", 26f, UIColors.Ink, font), 18f),
                                         "tower.gamble.result.to_budget");   // "to the build budget"
        BlueprintUI.TopLeft(toBudget.rectTransform, 32f, 92f, 400f, 34f);
        BlueprintBox rule = BlueprintUI.Box(slip.transform, "Rule", UIColors.WithAlpha(UIColors.Ink, 0.2f), Color.clear, 0f, BlueprintUI.Round(0f));
        BlueprintUI.TopLeft(rule.rectTransform, 32f, 140f, 600f, 2f);
        SlipBlock(slip.transform, "Build budget", "tower.gamble.result.budget_caps", 156f, font, out view.resultBudgetDeltaText, out view.resultBudgetText);
        SlipBlock(slip.transform, "Company value", "tower.gamble.result.value_caps", 226f, font, out view.resultValueDeltaText, out view.resultValueText);
        TextMeshProUGUI rules = Fixed(BlueprintUI.AutoSize(BlueprintUI.Wrap(BlueprintUI.Text(slip.transform, "Rules", "", 17f, UIColors.InkSoft, font)), 13f),
                                      "tower.gamble.result.rules");
        BlueprintUI.TopLeft(rules.rectTransform, 32f, 300f, 600f, 44f);

        // Again · Back to building
        RectTransform actions = BlueprintUI.NewRect("Actions", layer);
        BlueprintUI.TopLeft(actions, 46f, 586f, 900f, 80f);
        BlueprintUI.HRow(actions, 24f, new RectOffset(), TextAnchor.MiddleLeft);
        BlueprintBox again = BlueprintUI.Box(actions, "Again", UIColors.Gold, UIColors.Ink, 3f, BlueprintUI.RadiusLarge, true);
        again.SetShadow(BlueprintUI.FeltShadow, new Vector2(5f, 6f));
        BlueprintUI.HRow(again, 14f, new RectOffset(40, 40, 0, 0), TextAnchor.MiddleCenter);
        BlueprintUI.Layout(again, -1f, 80f, -1f, -1f, -1f, 80f);
        TowerViewFactory.Icon(again.transform, TowerIconKind.Coin, 32f, UIColors.Ink);
        view.resultButtonText = BlueprintUI.Text(again.transform, "Label", "", 32f, UIColors.Ink, font, TextAlignmentOptions.Center, true);
        view.resultButton = BlueprintUI.MakeButton(again);
        BlueprintBox back = BlueprintUI.Box(actions, "BackToBuilding", UIColors.PaperHi, UIColors.Ink, 3f, BlueprintUI.RadiusLarge, true);
        back.SetShadow(UIColors.Ink, new Vector2(4f, 5f));
        BlueprintUI.HRow(back, 14f, new RectOffset(36, 36, 0, 0), TextAnchor.MiddleCenter);
        BlueprintUI.Layout(back, -1f, 80f, -1f, -1f, -1f, 80f);
        TowerViewFactory.Icon(back.transform, TowerIconKind.Building, 32f, UIColors.Ink);
        Fixed(BlueprintUI.Text(back.transform, "Label", "", 32f, UIColors.Ink, font, TextAlignmentOptions.Center), "tower.gamble.button.back");
        view.resultBackButton = BlueprintUI.MakeButton(back);

        view.resultLayer = layer;
        layer.gameObject.SetActive(false);
    }

    /// <summary>A block of the result slip: a caption in spaced capitals (String Table entry <paramref name="captionKey"/>), the change and the line.</summary>
    static void SlipBlock(Transform slip, string label, string captionKey, float y, TMP_FontAsset font, out TextMeshProUGUI delta, out TextMeshProUGUI line)
    {
        TextMeshProUGUI caption = Fixed(BlueprintUI.AutoSize(BlueprintUI.Caps(BlueprintUI.Text(slip, label, "", 19f, UIColors.Pencil, font)), 14f), captionKey);
        BlueprintUI.TopLeft(caption.rectTransform, 32f, y, 300f, 24f);
        delta = BlueprintUI.Text(slip, label + " Delta", "", 19f, UIColors.Money, font, TextAlignmentOptions.Right);
        BlueprintUI.TopLeft(delta.rectTransform, 332f, y, 300f, 24f);
        line = BlueprintUI.AutoSize(BlueprintUI.Text(slip, label + " Line", "", 26f, UIColors.Ink, font), 16f);
        BlueprintUI.TopLeft(line.rectTransform, 32f, y + 26f, 600f, 34f);
    }
}
