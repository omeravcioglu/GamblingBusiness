using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The coin flip screen (design board CoinFlip): call Heads or Tails (the picked one ringed with a check), the bet
/// (- / +), the big gold coin, the result card (the outcome, the pot and the streak of up to five pips) and the actions:
/// Flip for $X, then after a win Double or nothing (pot $X) or Collect $X.
/// Drives CoinFlipGame (Flip, DoubleOrNothing, CollectWinnings): on FlipStarted the coin is tossed (real time: the casino
/// pauses the game) and turns over to land on the face the game rolled, at the moment FlipLanded settles the flip.
/// Built at runtime by Build unless a designed screen fills the references.
/// </summary>
public class CasinoCoinFlipView : CasinoScreen
{
    const int Pips = 5;
    const int HalfTurns = 9;
    const float Jump = 170f;

    [Tooltip("Empty = the casino's coin flip")]
    public CoinFlipGame game;

    [Header("Call")]
    public CasinoButton headsButton;
    public CasinoButton tailsButton;
    [Tooltip("The check marks shown on the picked call")]
    public GameObject headsCheck;
    public GameObject tailsCheck;
    [Tooltip("\"Call it. A win pays 1.95x your bet.\"")]
    public TMP_Text subtitleText;

    [Header("Bet")]
    public CasinoButton betMinusButton;
    public CasinoButton betPlusButton;
    public TMP_Text betText;
    [Tooltip("The bet chip (it fits the amount)")]
    public RectTransform betChip;

    [Header("Coin")]
    [Tooltip("Squashed and lifted while the coin is in the air")]
    public RectTransform coinBody;
    [Tooltip("The coin's shadow on the table (shrinks while the coin is up)")]
    public RectTransform coinShadow;
    public TMP_Text coinLetter;
    public TMP_Text coinLetterLight;
    public TMP_Text coinLetterDark;

    [Header("Result card")]
    public TMP_Text resultTitle;
    public TMP_Text amountLabel;
    public TMP_Text amountValue;
    public TMP_Text streakText;
    public BlueprintBox[] streakPips = new BlueprintBox[Pips];

    [Header("Actions")]
    public CasinoButton flipButton;
    public CasinoButton doubleButton;
    public CasinoButton collectButton;
    [Tooltip("Row the action buttons are centred in")]
    public RectTransform actionRow;
    [Tooltip("Demo: the doubles-cap line under the actions (built only in the demo)")]
    public TMP_Text demoCapText;

    // Demo: the half-company note shown in place of the keep hint while it caps the bet
    string demoNote;
    long demoNoteMax = -1;
    bool demoNoteShown;

    CoinFlipGame subscribed;
    bool callHeads = true;
    bool tossing;
    float tossStart;
    float tossSeconds = 1.5f;
    bool tossHeads = true;
    string refusal;
    float refusalUntil;
    double collected;
    string shownState;
    long shownBet = -1;
    bool coinShowsHeads = true;
    string headsLetter;
    string tailsLetter;

    CoinFlipGame Game
    {
        get
        {
            if (game != null) return game;
            CasinoManager casino = Manager;
            if (casino != null && casino.coinFlip != null) return casino.coinFlip;
            return FindAnyObjectByType<CoinFlipGame>(FindObjectsInactive.Include);
        }
    }

    public override bool IsBusy => tossing;

    protected override void OnWire()
    {
        if (headsButton != null && headsButton.button != null) headsButton.button.onClick.AddListener(() => Call(true));
        if (tailsButton != null && tailsButton.button != null) tailsButton.button.onClick.AddListener(() => Call(false));
        if (betMinusButton != null && betMinusButton.button != null) betMinusButton.button.onClick.AddListener(() => Step(-1));
        if (betPlusButton != null && betPlusButton.button != null) betPlusButton.button.onClick.AddListener(() => Step(1));
        if (flipButton != null && flipButton.button != null) flipButton.button.onClick.AddListener(FlipNow);
        if (doubleButton != null && doubleButton.button != null) doubleButton.button.onClick.AddListener(DoubleNow);
        if (collectButton != null && collectButton.button != null) collectButton.button.onClick.AddListener(CollectNow);
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        Subscribe(Game);
        tossing = false;
        refusal = null;
        collected = 0.0;
        shownState = null;
        shownBet = -1;
        ResetCoin();
        CoinFlipGame g = Game;
        SetCoinFace(g != null && g.HasResult ? g.LastFaceHeads : true);
        FillSubtitle(g);
        Refresh();
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        Subscribe(null);
        tossing = false;
        ResetCoin();
    }

    // The fonts switch by themselves; the texts this screen writes are written again
    protected override void OnLanguageRefresh()
    {
        headsLetter = tailsLetter = null;
        demoNote = null;   // Demo: written again in the new language
        FillSubtitle(Game);
        SetCoinFace(coinShowsHeads);
        shownState = null;
        Refresh();
    }

    /// <summary>"Call it. A win pays 1.95× your bet."</summary>
    void FillSubtitle(CoinFlipGame g)
    {
        if (subtitleText != null && g != null)
            subtitleText.text = L10n.Get(T, "casino.coinflip.subtitle", ("multiplier", g.payoutMultiplier.ToString("0.##", CultureInfo.InvariantCulture)));
    }

    void Subscribe(CoinFlipGame target)
    {
        if (subscribed == target) return;
        if (subscribed != null)
        {
            subscribed.FlipStarted -= OnFlipStarted;
            subscribed.FlipLanded -= OnFlipLanded;
            subscribed.Collected -= OnCollected;
            subscribed.FlipRefused -= OnRefused;
        }
        subscribed = target;
        if (subscribed != null)
        {
            subscribed.FlipStarted += OnFlipStarted;
            subscribed.FlipLanded += OnFlipLanded;
            subscribed.Collected += OnCollected;
            subscribed.FlipRefused += OnRefused;
        }
    }

    // ------------------------------------------------------------------
    // Choices
    // ------------------------------------------------------------------

    void Call(bool heads)
    {
        if (tossing) return;
        callHeads = heads;
        shownState = null;
    }

    void Step(int direction)
    {
        CasinoManager casino = Manager;
        if (casino != null && !tossing) casino.StepBet(direction);
    }

    void FlipNow()
    {
        CoinFlipGame g = Game;
        if (g == null || tossing || g.IsFlipping) return;
        Subscribe(g);
        refusal = null;
        collected = 0.0;
        g.Flip(callHeads);
        shownState = null;
    }

    void DoubleNow()
    {
        CoinFlipGame g = Game;
        if (g == null || tossing || g.IsFlipping || !g.HasPot) return;
        Subscribe(g);
        refusal = null;
        g.DoubleOrNothing(callHeads);
        shownState = null;
    }

    void CollectNow()
    {
        CoinFlipGame g = Game;
        if (g == null || tossing || g.IsFlipping) return;
        g.CollectWinnings();
        shownState = null;
    }

    // ------------------------------------------------------------------
    // Game events
    // ------------------------------------------------------------------

    void OnFlipStarted(double stake, bool isDouble)
    {
        CoinFlipGame g = Game;
        tossing = true;
        tossStart = Time.unscaledTime;
        tossSeconds = g != null ? g.FlipSeconds : 1.5f;
        tossHeads = g == null || g.LastFaceHeads;
        refusal = null;
        collected = 0.0;
        shownState = null;
    }

    void OnFlipLanded(bool heads, bool won, double paid)
    {
        tossing = false;
        tossHeads = heads;
        ResetCoin();
        SetCoinFace(heads);
        shownState = null;
    }

    void OnCollected(double pot)
    {
        collected = pot;
        shownState = null;
    }

    void OnRefused(string reason)
    {
        refusal = reason;
        refusalUntil = Time.unscaledTime + 5f;
        shownState = null;
    }

    // ------------------------------------------------------------------
    // The coin
    // ------------------------------------------------------------------

    void Update()
    {
        if (!tossing) return;
        float t = Mathf.Clamp01((Time.unscaledTime - tossStart) / Mathf.Max(0.2f, tossSeconds));
        float eased = CasinoUI.EaseOut(t);
        float turns = eased * HalfTurns;
        if (coinBody != null)
        {
            coinBody.localScale = new Vector3(1f, Mathf.Max(0.04f, Mathf.Abs(Mathf.Cos(turns * Mathf.PI))), 1f);
            coinBody.anchoredPosition = new Vector2(0f, Mathf.Sin(t * Mathf.PI) * Jump);
        }
        if (coinShadow != null)
        {
            float shrink = 1f - 0.45f * Mathf.Sin(t * Mathf.PI);
            coinShadow.localScale = new Vector3(shrink, shrink, 1f);
        }
        // The face that lands is the rolled one: count faces back from the last half-turn
        int face = Mathf.FloorToInt(turns + 0.5f);
        SetCoinFace(((HalfTurns - face) % 2 == 0) == tossHeads);
        // The game settles the flip when its time is up (FlipLanded); until then the coin rests on its face
        if (t >= 1f) ResetCoin();
    }

    void ResetCoin()
    {
        if (coinBody != null)
        {
            coinBody.localScale = Vector3.one;
            coinBody.anchoredPosition = Vector2.zero;
        }
        if (coinShadow != null) coinShadow.localScale = Vector3.one;
    }

    /// <summary>"H" / "T" on the coin (Chinese 正 / 反, Japanese 表 / 裏, ...), looked up once per language.</summary>
    void SetCoinFace(bool heads)
    {
        coinShowsHeads = heads;
        if (headsLetter == null) headsLetter = L10n.Get(T, "casino.coinflip.coin.heads");
        if (tailsLetter == null) tailsLetter = L10n.Get(T, "casino.coinflip.coin.tails");
        string letter = heads ? headsLetter : tailsLetter;
        if (coinLetter != null && coinLetter.text != letter) coinLetter.text = letter;
        if (coinLetterLight != null && coinLetterLight.text != letter) coinLetterLight.text = letter;
        if (coinLetterDark != null && coinLetterDark.text != letter) coinLetterDark.text = letter;
    }

    // ------------------------------------------------------------------
    // Texts and buttons
    // ------------------------------------------------------------------

    protected override void LateUpdate()
    {
        base.LateUpdate();
        if (refusal != null && Time.unscaledTime > refusalUntil)
        {
            refusal = null;
            shownState = null;
        }
        Refresh();
        ShowDemoMaxNote();   // Demo: nothing outside the demo
    }

    /// <summary>
    /// Demo: while the half-company cap sets MAX and the bet is at it, the keep hint (written by RefreshHeader) says so
    /// instead; the keep hint comes back when it no longer applies.
    /// </summary>
    void ShowDemoMaxNote()
    {
        if (!DemoMode.Active || keepHintText == null) return;
        CasinoManager casino = Manager;
        bool capped = casino != null && casino.IsDemoShareCapped;
        if (capped)
        {
            long max = casino.GetMaxBet();
            if (demoNote == null || max != demoNoteMax)
            {
                demoNoteMax = max;
                demoNote = casino.DemoMaxNote;
            }
            if (keepHintText.text != demoNote) keepHintText.text = demoNote;
        }
        else if (demoNoteShown)
            keepHintText.text = KeepHint(casino, casino == null || casino.CanBetAtAll);
        demoNoteShown = capped;
    }

    /// <summary>Update the card, the calls, the bet and the actions (only when something changed).</summary>
    public void Refresh()
    {
        CasinoManager casino = Manager;
        CoinFlipGame g = Game;
        long bet = casino != null ? casino.GetCurrentBet() : 0;
        bool busy = tossing || (g != null && g.IsFlipping);
        bool pot = g != null && g.HasPot && !busy;
        string state = (busy ? "B" : "-") + (pot ? "P" + g.Pot.ToString("R", CultureInfo.InvariantCulture) : "-") + (callHeads ? "H" : "T")
                       + bet + "/" + (g != null ? g.Streak : 0) + "/" + (g != null && g.HasResult ? (g.LastFlipWon ? "W" : "L") : "-")
                       + "/" + refusal + "/" + collected.ToString("R", CultureInfo.InvariantCulture) + "/" + (casino != null && casino.CanBetAtAll);
        if (state == shownState) return;
        shownState = state;

        // Calls (they keep their look while the coin is up, showing what was called; Call ignores clicks then)
        StyleCall(headsButton, headsCheck, callHeads, true);
        StyleCall(tailsButton, tailsCheck, !callHeads, true);

        // Bet
        if (bet != shownBet)
        {
            shownBet = bet;
            FitBetChip(betChip, betText, betPlusButton != null ? betPlusButton.button : null, UIFormat.Money(bet), 320f);
        }
        if (betMinusButton != null) betMinusButton.Interactable = !busy && casino != null && casino.CanLowerBet;
        if (betPlusButton != null) betPlusButton.Interactable = !busy && casino != null && casino.CanRaiseBet;

        FillCard(g, casino, bet, busy);
        FillActions(g, casino, bet, busy, pot);
    }

    void StyleCall(CasinoButton button, GameObject check, bool picked, bool enabled)
    {
        if (button == null) return;
        button.Interactable = enabled;
        button.SetLook(CasinoButton.Look.Paper, picked);
        if (check != null && check.activeSelf != picked) check.SetActive(picked);
        if (button.label != null) button.label.fontStyle = picked ? FontStyles.Bold : FontStyles.Normal;
    }

    void FillCard(CoinFlipGame g, CasinoManager casino, long bet, bool busy)
    {
        string title, label, value;
        Color titleColor = UIColors.Ink;
        int streak = g != null ? g.Streak : 0;
        // Refusals and "not enough money" are the game's and CasinoManager's own messages, shown as they come
        if (refusal != null)
        {
            title = refusal;
            titleColor = UIColors.Loss;
            label = L10n.Get(T, "casino.coinflip.label.bet");
            value = UIFormat.Money(bet);
        }
        else if (busy)
        {
            title = L10n.Get(T, "casino.coinflip.result.flipping");
            label = L10n.Get(T, "casino.coinflip.label.at_stake");
            value = UIFormat.Money(g != null ? g.LastStake : bet);
        }
        else if (g != null && g.HasResult && g.LastFlipWon && g.HasPot)
        {
            title = L10n.Get(T, g.LastFaceHeads ? "casino.coinflip.result.heads_win" : "casino.coinflip.result.tails_win");
            titleColor = UIColors.Money;
            label = L10n.Get(T, "casino.coinflip.label.pot");
            value = UIFormat.Money(g.Pot);
        }
        else if (collected > 0.0)
        {
            title = L10n.Get(T, "casino.coinflip.result.kept", ("amount", UIFormat.Money(collected)));
            titleColor = UIColors.Money;
            label = L10n.Get(T, "casino.coinflip.label.win_pays");
            value = UIFormat.Money(g != null ? g.WinPayoutFor(bet) : bet);
        }
        else if (g != null && g.HasResult && !g.LastFlipWon)
        {
            title = L10n.Get(T, g.LastFaceHeads ? "casino.coinflip.result.heads_lose" : "casino.coinflip.result.tails_lose");
            titleColor = UIColors.Loss;
            label = L10n.Get(T, "casino.coinflip.label.lost");
            value = UIFormat.Money(g.LastStake);
        }
        else if (casino != null && !casino.CanBetAtAll)
        {
            title = casino.NotEnoughMessage;
            titleColor = UIColors.Loss;
            label = L10n.Get(T, "casino.coinflip.label.bet");
            value = UIFormat.Money(bet);
        }
        else
        {
            title = L10n.Get(T, "casino.coinflip.result.idle");
            label = L10n.Get(T, "casino.coinflip.label.win_pays");
            value = UIFormat.Money(g != null ? g.WinPayoutFor(bet) : bet);
        }

        if (resultTitle != null)
        {
            resultTitle.text = title;
            resultTitle.color = titleColor;
        }
        if (amountLabel != null) amountLabel.text = label;
        if (amountValue != null) amountValue.text = value;
        if (streakText != null) streakText.text = L10n.Get(T, "casino.coinflip.streak", ("count", streak.ToString(CultureInfo.InvariantCulture)));
        for (int i = 0; i < streakPips.Length; i++)
        {
            BlueprintBox pip = streakPips[i];
            if (pip == null) continue;
            bool lit = i < streak;
            pip.SetStyle(lit ? UIColors.Gold : UIColors.PaperHi, lit ? UIColors.Ink : UIColors.Pencil, 2f);
            pip.SetDash(!lit, 3f, 2.5f);
        }
    }

    void FillActions(CoinFlipGame g, CasinoManager casino, long bet, bool busy, bool pot)
    {
        bool canBet = casino != null && casino.CanBetAtAll && g != null;
        bool showDouble = pot || (busy && g != null && g.LastFlipWasDouble);
        if (doubleButton != null && doubleButton.gameObject.activeSelf != showDouble) doubleButton.gameObject.SetActive(showDouble);
        if (collectButton != null && collectButton.gameObject.activeSelf != showDouble) collectButton.gameObject.SetActive(showDouble);
        if (flipButton != null && flipButton.gameObject.activeSelf == showDouble) flipButton.gameObject.SetActive(!showDouble);

        if (showDouble)
        {
            double stake = pot ? g.Pot : g.LastStake;
            double wins = stake * Mathf.Max(0f, g.doubleOrNothingMultiplier);
            if (doubleButton != null)
            {
                doubleButton.SetLabel(L10n.Get(T, "casino.coinflip.button.double", ("amount", UIFormat.Money(wins))));
                // Only when the pot itself may be staked (heat, the $1,000 the company keeps); Demo: and under the doubles cap
                doubleButton.Interactable = pot && canBet && g.CanDoubleAgain && casino.CanPlaceBet(g.Pot, out _);
            }
            if (collectButton != null)
            {
                collectButton.SetLabel(L10n.Get(T, "casino.coinflip.button.collect", ("amount", UIFormat.Money(stake))));
                collectButton.Interactable = pot;
            }
            LayoutActions(doubleButton, collectButton);
        }
        else
        {
            if (flipButton != null)
            {
                bool again = g != null && g.HasResult;
                flipButton.SetLabel(L10n.Get(T, again ? "casino.coinflip.button.flip_again" : "casino.coinflip.button.flip", ("amount", UIFormat.Money(bet))));
                flipButton.Interactable = !busy && canBet;
            }
            LayoutActions(flipButton, null);
        }

        // Demo: the doubles cap is reached, so the pot can only be collected
        if (demoCapText != null)
        {
            bool capped = pot && !g.CanDoubleAgain;
            if (capped) demoCapText.text = CoinFlipGame.StreakCapMessage;
            if (demoCapText.gameObject.activeSelf != capped) demoCapText.gameObject.SetActive(capped);
        }
    }

    protected override void FitLayout()
    {
        base.FitLayout();
        RefitBetChip(betChip, betText, betPlusButton != null ? betPlusButton.button : null, 320f);
    }

    /// <summary>Centre one or two action buttons in the action row (24 px apart).</summary>
    void LayoutActions(CasinoButton first, CasinoButton second)
    {
        if (actionRow == null || first == null) return;
        float rowWidth = actionRow.rect.width > 0f ? actionRow.rect.width : 1000f;
        float a = ((RectTransform)first.transform).sizeDelta.x;
        float b = second != null ? ((RectTransform)second.transform).sizeDelta.x : 0f;
        float total = a + (second != null ? 24f + b : 0f);
        float x = (rowWidth - total) * 0.5f;
        RectTransform ra = (RectTransform)first.transform;
        ra.anchoredPosition = new Vector2(x, ra.anchoredPosition.y);
        if (second != null)
        {
            RectTransform rb = (RectTransform)second.transform;
            rb.anchoredPosition = new Vector2(x + a + 24f, rb.anchoredPosition.y);
        }
    }

    // ------------------------------------------------------------------
    // Runtime layout (board CoinFlip, 1920 x 1080)
    // ------------------------------------------------------------------

    /// <summary>Build the coin flip screen under <paramref name="parent"/> (an inactive root).</summary>
    public static CasinoCoinFlipView Build(RectTransform parent, TMP_FontAsset font)
    {
        RectTransform root = CasinoUI.FullRect(parent, "CasinoCoinFlip");
        root.gameObject.SetActive(false);
        CasinoCoinFlipView view = root.gameObject.AddComponent<CasinoCoinFlipView>();
        view.font = font;
        CasinoUI.Felt(root, "Felt", true);
        RectTransform screen = CasinoUI.Stage(root);

        TextMeshProUGUI title = CasinoUI.Shrink(CasinoUI.Text(screen, "Title", "", 60f, UIColors.Gold, font, 660f, 84f, 600f, 69f, TextAlignmentOptions.Center, true), 36f);
        L10nText.Attach(title, T, "casino.coinflip.title");   // "Coin flip"
        // The screen writes the subtitle (the game's payout)
        view.subtitleText = CasinoUI.Shrink(CasinoUI.Text(screen, "Subtitle", "", 22f, UIColors.PaperHi, font, 560f, 158f, 800f, 30f,
                                                          TextAlignmentOptions.Center), 15f);

        // Heads | Tails | bet
        view.headsButton = CallButton(screen, "Heads", "casino.coinflip.heads", 575f, font, out view.headsCheck);
        view.tailsButton = CallButton(screen, "Tails", "casino.coinflip.tails", 795f, font, out view.tailsCheck);
        CasinoUI.Box(screen, "Divider", UIColors.WithAlpha(UIColors.PaperHi, 0.4f), Color.clear, 0f, Vector4.zero, 1015f, 232f, 2f, 44f);
        view.betChip = BuildBetStepper(screen, font, 1037f, 228f, true, out view.betMinusButton, out view.betPlusButton, out TextMeshProUGUI bet);
        view.betText = bet;

        CasinoUI.Sparkle(screen, 712f, 344f, 48f);
        CasinoUI.Sparkle(screen, 1160f, 540f, 40f);

        // The coin (SetCoinFace writes its letter in the current language)
        CasinoUI.CoinParts coin = CasinoUI.Coin(screen, "Coin", 320f, font, 960f, 480f, L10n.Get(T, "casino.coinflip.coin.heads"), 12f, 16f, 20f, 3f, 32f,
                                                true, 4f, 3f);
        view.coinBody = coin.body;
        view.coinShadow = coin.shadow != null ? coin.shadow.rectTransform : null;
        view.coinLetter = coin.letter;
        view.coinLetterLight = coin.letterLight;
        view.coinLetterDark = coin.letterDark;

        BuildResultCard(view, screen, font);

        // Actions: Flip, or Double or nothing and Collect (the screen writes their labels with the amounts)
        view.actionRow = CasinoUI.Rect(screen, "Actions", 460f, 880f, 1000f, 68f);
        view.flipButton = CasinoUI.Button(view.actionRow, "Flip", L10n.Get(T, "casino.coinflip.button.flip", ("amount", "$1,000")), CasinoIconKind.Coin,
                                          CasinoButton.Look.Gold, font, 28f, true, 0f, 0f, 0f, 68f, new Vector2(5f, 6f), CasinoUI.FeltShadow, 36f, 28f, 260f, 700f);
        view.doubleButton = CasinoUI.Button(view.actionRow, "DoubleOrNothing", L10n.Get(T, "casino.coinflip.button.double", ("amount", "$4,000")),
                                            CasinoIconKind.Coin, CasinoButton.Look.Gold, font, 28f, true, 0f, 0f, 0f, 68f, new Vector2(5f, 6f),
                                            CasinoUI.FeltShadow, 36f, 28f, 300f, 620f);
        view.collectButton = CasinoUI.Button(view.actionRow, "Collect", L10n.Get(T, "casino.coinflip.button.collect", ("amount", "$2,000")),
                                             CasinoIconKind.Banknote, CasinoButton.Look.Money, font, 28f, true, 0f, 0f, 0f, 68f, new Vector2(5f, 6f),
                                             UIColors.Ink, 36f, 28f, 220f, 360f);
        view.doubleButton.gameObject.SetActive(false);
        view.collectButton.gameObject.SetActive(false);
        if (DemoMode.Active)
        {
            // Demo: the doubles-cap line under the actions (the screen writes it)
            view.demoCapText = CasinoUI.Shrink(CasinoUI.Text(screen, "DemoDoublesCap", "", 22f, UIColors.Highlight, font, 460f, 962f, 1000f, 34f,
                                                             TextAlignmentOptions.Center), 15f);
            L10nFont.Attach(view.demoCapText);
            view.demoCapText.gameObject.SetActive(false);
        }

        BuildGameHeader(view, screen, font, 48f, 36f);
        return view;
    }

    /// <summary>A Heads / Tails call (200 x 68, paper; the picked one gets the ring, a green check and a bold label).</summary>
    static CasinoButton CallButton(RectTransform parent, string name, string key, float x, TMP_FontAsset font, out GameObject check)
    {
        CasinoButton button = CasinoUI.Button(parent, name, L10n.Get(T, key), CasinoIconKind.Check, CasinoButton.Look.Paper, font, 28f, false,
                                              x, 220f, 200f, 68f, new Vector2(4f, 5f), UIColors.Ink, 24f, 28f);
        L10nText.Attach(button.label, T, key);   // "Heads" / "Tails"
        button.ringShadow = false;
        // The check is green and only on the picked call; the label follows the look
        check = null;
        foreach (Graphic graphic in button.content)
        {
            if (graphic is CasinoIcon icon)
            {
                icon.color = UIColors.Money;
                icon.StrokeWidth = 3f;
                check = icon.gameObject;
            }
        }
        button.content = new Graphic[] { button.label };
        return button;
    }

    static void BuildResultCard(CasinoCoinFlipView view, RectTransform screen, TMP_FontAsset font)
    {
        BlueprintBox card = CasinoUI.Box(screen, "ResultCard", UIColors.PaperHi, UIColors.Ink, 3f, CasinoUI.RadiusLarge, 560f, 688f, 800f, 156f);
        card.SetShadow(CasinoUI.SheetShadow, new Vector2(0f, 22f), 48f);
        view.resultTitle = CasinoUI.Shrink(CasinoUI.Text(card.transform, "Title", L10n.Get(T, "casino.coinflip.result.idle"), 44f, UIColors.Ink, font,
                                                         36f, 28f, 728f, 52f, TextAlignmentOptions.Center, true), 18f);

        // Pot: $2,000 | Streak 1 (pips)
        RectTransform row = CasinoUI.Rect(card.transform, "Row", 36f, 92f, 728f, 36f);
        BlueprintUI.HRow(row, 28f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleCenter);
        RectTransform amount = BlueprintUI.NewRect("Amount", row);
        BlueprintUI.HRow(amount, 10f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleCenter);
        view.amountLabel = BlueprintUI.Text(amount, "Label", L10n.Get(T, "casino.coinflip.label.win_pays"), 26f, UIColors.InkSoft, font);
        view.amountValue = BlueprintUI.Text(amount, "Value", "$0", 26f, UIColors.Ink, font, TextAlignmentOptions.Left, true);
        BlueprintBox divider = BlueprintUI.Box(row, "Divider", UIColors.WithAlpha(UIColors.Ink, 0.3f), Color.clear, 0f, Vector4.zero);
        BlueprintUI.Layout(divider, 2f, 32f, 0f, 0f, 2f, 32f);
        RectTransform streak = BlueprintUI.NewRect("Streak", row);
        BlueprintUI.HRow(streak, 12f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleCenter);
        view.streakText = BlueprintUI.Text(streak, "Text", L10n.Get(T, "casino.coinflip.streak", ("count", "0")), 22f, UIColors.Ink, font);
        RectTransform pips = BlueprintUI.NewRect("Pips", streak);
        BlueprintUI.HRow(pips, 6f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleCenter);
        for (int i = 0; i < Pips; i++)
        {
            BlueprintBox pip = BlueprintUI.Box(pips, "Pip" + i, UIColors.PaperHi, UIColors.Pencil, 2f, BlueprintUI.Round(10f));
            BlueprintUI.Layout(pip, 20f, 20f, 0f, 0f, 20f, 20f);
            view.streakPips[i] = pip;
        }
    }
}
