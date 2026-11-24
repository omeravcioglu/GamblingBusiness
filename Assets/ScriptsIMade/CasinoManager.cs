using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// The company casino: the hub (game selection, the shared bet, company value) and the rules every casino bet follows.
/// - Opens from the HUD's Casino tab (OpenCasino) once the Casino License skill is unlocked (Chaos and Luck, tier 5);
///   while it is locked OpenCasino refuses with a toast. A pausing UIManager panel whose games are screens of it: Escape
///   backs out one step (game, then hub, then closed), never while a round is being played.
/// - Bets are whole dollars of company value (CompanyManager.companyValue), one bet shared by the games. The company
///   always keeps keepInCompany ($1,000): no bet is more than floor(company value - keepInCompany) (GetMaxBet), MAX and
///   blackjack doubles included.
/// - Every bet asks HeatMeter.CanGamble first (a frozen board takes nothing) and is taken when its round starts
///   (TryPlaceBet); the game settles the round the moment its outcome shows. MoneyMoved reports each money movement (a
///   bet taken, winnings paid) for RunStats' casino net; each settled round goes once to GambleLedger (ReportRound),
///   which turns it into heat. An unresolved bet is never given back: quitting mid-round forfeits it.
/// - UI: the references below can be wired to a designed prefab. With Casino Panel empty the screens are built at runtime
///   (CasinoView, from the design boards CasinoHub, Slots, CoinFlip and CasinoBlackjack) and fill in the panel references.
/// - CasinoBootstrap adds this (with the three games) to every scene that has a CompanyManager and no casino.
/// - Demo (DemoMode): the bet starts at DemoConfig.casinoDefaultBet, a chosen bet is also capped to casinoMaxBetShare of
///   company value (a coin-flip pot or a blackjack double may still use the room above the reserve), and a locked casino
///   shows FeatureGate's demo toast.
/// </summary>
public class CasinoManager : MonoBehaviour
{
    public static CasinoManager Instance;

    [Header("Main Panel")]
    public GameObject casinoPanel;
    public Button openCasinoButton;
    public Button closeCasinoButton;

    [Header("Game Panels")]
    public GameObject gameSelectionPanel;
    public GameObject slotMachinePanel;
    public GameObject blackjackPanel;
    public GameObject coinFlipPanel;

    [Header("Game Selection Buttons")]
    public Button playSlotButton;
    public Button playBlackjackButton;
    public Button playCoinFlipButton;
    public Button backToSelectionButton;

    [Header("Balance Display")]
    public TextMeshProUGUI balanceText;

    [Header("Bet Input")]
    public TMP_InputField betAmountInput;
    public Button bet100Button;
    public Button bet500Button;
    public Button bet1000Button;
    public Button bet5000Button;
    public Button betMaxButton;

    [Header("Games (empty = found in the scene)")]
    public SlotMachineGame slotMachine;
    public CasinoBlackjack blackjack;
    public CoinFlipGame coinFlip;

    [Header("Access")]
    [Tooltip("The casino only opens once the Casino License skill (Chaos and Luck, tier 5) is unlocked")]
    public bool requireCasinoLicense = true;

    [Header("Bets")]
    [Tooltip("Company value no bet can touch: the largest bet is floor(company value - this), MAX and blackjack doubles included")]
    public double keepInCompany = 1000;

    [Header("Runtime screens (built when Casino Panel is empty)")]
    [Tooltip("Font of the runtime screens (empty = the Coming Soon font the HUD uses)")]
    public TMP_FontAsset font;
    [Tooltip("Sorting order of the runtime canvas: above the HUD and its pop-ups, below the bankruptcy hearing (120), the tilt prompt (130) and the toasts (900)")]
    public int sortingOrder = 110;

    /// <summary>Shown when the casino is still locked (Casino table, casino.msg.locked, naming the skill as the skill board does).</summary>
    public static string LockedMessage
    {
        get
        {
            // Demo: the casino's demo schedule ("The casino opens after 4 hires or on day 4.")
            if (DemoMode.Active)
            {
                string demo = FeatureGate.LockedMessage(Feature.Casino);
                if (!string.IsNullOrEmpty(demo)) return demo;
            }
            return L10n.Get(CasinoUI.Table, "casino.msg.locked", ("skill", CompanyText.SkillName("chaos_5_casino", "Casino License")));
        }
    }

    /// <summary>A game's refusal when the casino closed meanwhile (the games raise it through their Refused events).</summary>
    public static string ClosedMessage => L10n.Get(CasinoUI.Table, "casino.msg.closed");

    /// <summary>
    /// Casino money for run stats: (bet taken, winnings paid), once per money movement. DeductBet reports bets,
    /// AddWinnings payouts (a blackjack push pays the bet back).
    /// </summary>
    public static event Action<double, double> MoneyMoved;

    /// <summary>The casino opened or closed, its screen changed or the bet changed.</summary>
    public event Action Changed;

    /// <summary>What the casino shows.</summary>
    public enum CasinoScreen
    {
        Closed,
        Hub,
        Slots,
        Blackjack,
        CoinFlip
    }

    private long currentBet = 100;
    CasinoScreen lastGame = CasinoScreen.Closed;   // the game opened last: its round may settle after the casino closed
    bool uiWired;
    bool isOpen;
    int betEditEndFrame = -1;
    CasinoView runtimeView;

    /// <summary>The screens built at runtime (null while a designed casino UI is wired in, or before the casino first opens).</summary>
    public CasinoView RuntimeView => runtimeView;

    /// <summary>The casino is on screen.</summary>
    public bool IsOpen => isOpen;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        // Entering Play mode without a domain reload keeps statics: start every session clean
        Instance = null;
        MoneyMoved = null;
    }

    void Awake()
    {
        Instance = this;
        if (DemoMode.Active) currentBet = Math.Max(1L, DemoMode.Config.casinoDefaultBet);   // Demo: the demo's starting bet
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
        if (UIManager.Exists)
            UIManager.Instance.ClosePanel(this);
    }

    void Start()
    {
        ResolveGames();
        WireUI();

        // Hide panels at start (unless the casino was opened before its first frame, e.g. by the debug menu)
        if (isOpen) return;
        if (casinoPanel != null) casinoPanel.SetActive(false);
        HideAllGamePanels();
    }

    void Update()
    {
        if (!isOpen) return;
        // UI state was reset (e.g. by a debug tool) while the casino is up: keep the game paused
        if (UIManager.Exists && !UIManager.Instance.IsPanelOpen(this))
            UIManager.Instance.OpenPanel(this, true, OnEscape);
    }

    void WireUI()
    {
        if (uiWired) return;
        uiWired = true;

        // Main panel buttons
        if (openCasinoButton != null)
            openCasinoButton.onClick.AddListener(OpenCasino);
        if (closeCasinoButton != null)
            closeCasinoButton.onClick.AddListener(CloseCasino);

        // Game selection buttons
        if (playSlotButton != null)
            playSlotButton.onClick.AddListener(OpenSlots);
        if (playBlackjackButton != null)
            playBlackjackButton.onClick.AddListener(OpenBlackjack);
        if (playCoinFlipButton != null)
            playCoinFlipButton.onClick.AddListener(OpenCoinFlip);
        if (backToSelectionButton != null)
            backToSelectionButton.onClick.AddListener(() => BackToHub());

        // Bet buttons
        if (bet100Button != null)
            bet100Button.onClick.AddListener(() => SetBet(100));
        if (bet500Button != null)
            bet500Button.onClick.AddListener(() => SetBet(500));
        if (bet1000Button != null)
            bet1000Button.onClick.AddListener(() => SetBet(1000));
        if (bet5000Button != null)
            bet5000Button.onClick.AddListener(() => SetBet(5000));
        if (betMaxButton != null)
            betMaxButton.onClick.AddListener(SetMaxBet);
        if (betAmountInput != null)
        {
            // Editing shows the plain number; leaving the field sets the bet
            betAmountInput.onSelect.AddListener(_ => { if (betAmountInput != null) betAmountInput.SetTextWithoutNotify(PlainNumber(GetCurrentBet())); });
            betAmountInput.onEndEdit.AddListener(CommitTypedBet);
            betAmountInput.onValidateInput = (text, index, c) => c >= '0' && c <= '9' ? c : '\0';
        }
    }

    /// <summary>Find the games in the scene when they aren't wired in.</summary>
    void ResolveGames()
    {
        if (slotMachine == null) slotMachine = FindAnyObjectByType<SlotMachineGame>(FindObjectsInactive.Include);
        if (blackjack == null) blackjack = FindAnyObjectByType<CasinoBlackjack>(FindObjectsInactive.Include);
        if (coinFlip == null) coinFlip = FindAnyObjectByType<CoinFlipGame>(FindObjectsInactive.Include);
    }

    // ------------------------------------------------------------------
    // Open / close
    // ------------------------------------------------------------------

    /// <summary>True when the casino may open (Casino License unlocked, or the license isn't required).</summary>
    public bool IsCasinoAvailable()
    {
        return !requireCasinoLicense || SkillTreeManager.Instance == null || SkillTreeManager.Instance.IsCasinoUnlocked();
    }

    /// <summary>Open the casino (the HUD's Casino tab). Refuses with a toast while the Casino License is locked.</summary>
    public void OpenCasino()
    {
        if (!IsCasinoAvailable())
        {
            // Demo: FeatureGate's one rate-limited toast (the tab may have shown it already)
            if (DemoMode.Active && !string.IsNullOrEmpty(FeatureGate.LockedMessage(Feature.Casino)))
            {
                FeatureGate.ShowLockedToast(Feature.Casino);
                return;
            }
            GameNotifications.Show(LockedMessage, NotificationKind.Bad);
            return;
        }
        Open();
    }

    /// <summary>Debug (Tools > Game > Debug > Open Casino): open the casino without the Casino License.</summary>
    public void DebugOpenCasino()
    {
        Open();
    }

    void Open()
    {
        ResolveGames();
        EnsureScreens();

        if (casinoPanel != null)
        {
            casinoPanel.SetActive(true);
            UIManager.BringToFront(casinoPanel);
        }
        if (gameSelectionPanel != null) gameSelectionPanel.SetActive(true);
        HideAllGamePanels();
        isOpen = true;
        UpdateBalanceDisplay();
        ShowBet();

        // Modal: pauses the game. Escape backs out of a game first, then closes the casino.
        UIManager ui = UIManager.Instance;
        if (ui != null)
            ui.OpenPanel(this, true, OnEscape);
        ClearSelection();
        RaiseChanged();
    }

    /// <summary>With no designed casino UI wired in (Casino Panel empty), build the runtime screens and use their panels.</summary>
    void EnsureScreens()
    {
        if (casinoPanel != null) return;
        if (runtimeView == null)
        {
            try
            {
                runtimeView = CasinoView.Build(this, font, sortingOrder);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                runtimeView = null;
            }
        }
        if (runtimeView != null)
            runtimeView.AssignPanels(this);
    }

    public void CloseCasino()
    {
        // A hand can't wait at a closed table: it stands (a spin or a flip in the air settles by itself), and an
        // open coin-flip pot (already paid) is kept
        if (blackjack != null && blackjack.IsHandInProgress)
            blackjack.Stand();
        if (coinFlip != null && coinFlip.HasPot && !coinFlip.IsFlipping)
            coinFlip.CollectWinnings();

        // Hiding the panel also hides the games; a designed game panel settles a round in progress in OnDisable
        if (casinoPanel != null) casinoPanel.SetActive(false);
        isOpen = false;
        if (UIManager.Exists)
            UIManager.Instance.ClosePanel(this);
        RaiseChanged();
    }

    void OnEscape()
    {
        if (IsEditingBet()) return;          // the bet field takes this Escape (it puts the old bet back)
        if (IsRoundInProgress()) return;     // never mid-round: the spin, flip or hand finishes first
        if (IsAnyGameOpen())
            BackToSelection();
        else
            CloseCasino();
    }

    /// <summary>A round is being played (a spin, a flip in the air, a hand, or an outcome still being shown): no leaving now.</summary>
    public bool IsRoundInProgress()
    {
        if (slotMachine != null && slotMachine.IsSpinning) return true;
        if (coinFlip != null && coinFlip.IsFlipping) return true;
        if (blackjack != null && blackjack.IsHandInProgress) return true;
        if (runtimeView != null && runtimeView.IsBusy) return true;
        return false;
    }

    /// <summary>What the casino shows now.</summary>
    public CasinoScreen CurrentScreen
    {
        get
        {
            if (!isOpen) return CasinoScreen.Closed;
            if (slotMachinePanel != null && slotMachinePanel.activeSelf) return CasinoScreen.Slots;
            if (blackjackPanel != null && blackjackPanel.activeSelf) return CasinoScreen.Blackjack;
            if (coinFlipPanel != null && coinFlipPanel.activeSelf) return CasinoScreen.CoinFlip;
            return CasinoScreen.Hub;
        }
    }

    public void OpenSlots()
    {
        lastGame = CasinoScreen.Slots;
        OpenGame(slotMachinePanel);
    }

    public void OpenBlackjack()
    {
        lastGame = CasinoScreen.Blackjack;
        OpenGame(blackjackPanel);
    }

    public void OpenCoinFlip()
    {
        lastGame = CasinoScreen.CoinFlip;
        OpenGame(coinFlipPanel);
    }

    /// <summary>Back to the hub from a game; refused (false) while its round is being played.</summary>
    public bool BackToHub()
    {
        if (IsRoundInProgress()) return false;
        BackToSelection();
        return true;
    }

    void OpenGame(GameObject gamePanel)
    {
        if (gameSelectionPanel != null) gameSelectionPanel.SetActive(false);
        HideAllGamePanels();
        if (gamePanel != null) gamePanel.SetActive(true);
        ClearSelection();
        RaiseChanged();
    }

    void BackToSelection()
    {
        // Leaving the coin flip keeps an open pot (it is already in company value)
        if (coinFlip != null && coinFlip.HasPot && !coinFlip.IsFlipping)
            coinFlip.CollectWinnings();
        HideAllGamePanels();
        if (gameSelectionPanel != null) gameSelectionPanel.SetActive(true);
        UpdateBalanceDisplay();
        ShowBet();
        ClearSelection();
        RaiseChanged();
    }

    void HideAllGamePanels()
    {
        if (slotMachinePanel != null) slotMachinePanel.SetActive(false);
        if (blackjackPanel != null) blackjackPanel.SetActive(false);
        if (coinFlipPanel != null) coinFlipPanel.SetActive(false);
    }

    bool IsAnyGameOpen()
    {
        return (slotMachinePanel != null && slotMachinePanel.activeSelf)
            || (blackjackPanel != null && blackjackPanel.activeSelf)
            || (coinFlipPanel != null && coinFlipPanel.activeSelf);
    }

    /// <summary>Stop Enter/Space from clicking the button that opened a screen again.</summary>
    static void ClearSelection()
    {
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    void RaiseChanged()
    {
        Action handlers = Changed;
        if (handlers == null) return;
        foreach (Action handler in handlers.GetInvocationList())
        {
            try { handler(); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }

    // ------------------------------------------------------------------
    // Bets (whole dollars)
    // ------------------------------------------------------------------

    /// <summary>
    /// The money bets come from, in whole dollars: Player Cash (Cash Grab, 2026-11-24: the casino only takes cash the CEO
    /// collected on the cash floor; PlayerWallet falls back to company value without a cash floor); 0 without a company.
    /// </summary>
    public static long GetBalance()
    {
        if (CompanyManager.Instance == null) return 0;
        return ToWholeDollars(PlayerWallet.Balance);
    }

    /// <summary>The bankroll now (Player Cash; company value without a cash floor): what the casino's money chip shows.</summary>
    public static double BalanceNow()
    {
        return CompanyManager.Instance != null ? Math.Max(0.0, PlayerWallet.Balance) : 0.0;
    }

    /// <summary>Company value now (0 without a company): a round's value before its stake is taken.</summary>
    public static double CompanyValueNow()
    {
        CompanyManager company = CompanyManager.Instance;
        return company != null ? Math.Max(0.0, company.companyValue) : 0.0;
    }

    static long ToWholeDollars(double value)
    {
        if (double.IsNaN(value) || value < 1.0) return 0;
        if (value >= long.MaxValue) return long.MaxValue;
        return (long)Math.Floor(value);
    }

    /// <summary>
    /// What the company always keeps: no bet can take it. With the cash floor there is no reserve: bets only ever come out
    /// of Player Cash, which can't bankrupt the company.
    /// </summary>
    public double KeepInCompany => PlayerWallet.Active ? 0.0 : double.IsNaN(keepInCompany) ? 0.0 : Math.Max(0.0, keepInCompany);

    /// <summary>
    /// The largest bet right now: floor(bankroll - KeepInCompany), in the demo also at most casinoMaxBetShare of the
    /// bankroll; 0 when nothing can be bet. The bankroll is Player Cash (PlayerWallet).
    /// </summary>
    public long GetMaxBet()
    {
        CompanyManager company = CompanyManager.Instance;
        if (company == null) return 0;
        double balance = PlayerWallet.Balance;
        long max = ToWholeDollars(balance - KeepInCompany);
        if (DemoMode.Active) max = Math.Min(max, DemoShareCap(balance));   // Demo: half the bankroll at most
        return max;
    }

    /// <summary>floor(bankroll - KeepInCompany): the room above the reserve, whatever caps a chosen bet.</summary>
    long RoomAboveReserve()
    {
        CompanyManager company = CompanyManager.Instance;
        return company != null ? ToWholeDollars(PlayerWallet.Balance - KeepInCompany) : 0;
    }

    /// <summary>Demo: the largest share of the bankroll one chosen bet may be (whole dollars).</summary>
    static long DemoShareCap(double balance)
    {
        return ToWholeDollars(balance * DemoMode.Config.casinoMaxBetShare);
    }

    /// <summary>
    /// Demo: the bet is at MAX and the half-company cap is what sets MAX (not the reserve). Always false outside the demo.
    /// </summary>
    public bool IsDemoShareCapped
    {
        get
        {
            if (!DemoMode.Active || CompanyManager.Instance == null) return false;
            long share = DemoShareCap(PlayerWallet.Balance);
            return share >= 1 && share < RoomAboveReserve() && currentBet >= share;
        }
    }

    /// <summary>Demo: "Demo limit: one bet is at most half the company ($50,000)." (Demo table, demo.casino.max_note).</summary>
    public string DemoMaxNote => DemoMode.T("demo.casino.max_note", ("max", UIFormat.Money(GetMaxBet())));

    /// <summary>The company has more than it keeps, so there is something to bet.</summary>
    public bool CanBetAtAll => GetMaxBet() >= 1;

    /// <summary>"Bets come out of your cash" (Cash Grab), else "The company keeps $1,000" (Casino table, casino.keep_hint).</summary>
    public string KeepHint => PlayerWallet.Active ? CashText.Get("cash.casino.hint")
                                                  : L10n.Get(CasinoUI.Table, "casino.keep_hint", ("amount", UIFormat.Money(KeepInCompany)));

    /// <summary>Why nothing can be bet: no Player Cash yet (Cash Grab), else the company's reserve (casino.msg.not_enough).</summary>
    public string NotEnoughMessage => PlayerWallet.Active ? CashText.Get("cash.casino.no_cash")
                                                          : L10n.Get(CasinoUI.Table, "casino.msg.not_enough", ("amount", UIFormat.Money(KeepInCompany)));

    /// <summary>Inspector-friendly overload (UnityEvents can't pass a long).</summary>
    public void SetBet(int amount)
    {
        SetBet((long)amount);
    }

    /// <summary>Set the bet, clamped to at least $1 and at most the largest bet (MAX).</summary>
    public void SetBet(long amount)
    {
        long max = GetMaxBet();
        long bet = Math.Max(1L, amount);
        if (max >= 1) bet = Math.Min(bet, max);
        currentBet = bet;
        GameSfx.Play(Sfx.ChipBet);   // presets and - / +
        ShowBet();
        RaiseChanged();
    }

    /// <summary>MAX: bet all the company can put up (its value minus what it keeps).</summary>
    public void SetMaxBet()
    {
        currentBet = Math.Max(1L, GetMaxBet());
        GameSfx.Play(Sfx.ChipBet);
        ShowBet();
        RaiseChanged();
    }

    /// <summary>- and +: the next step of the 1-2-5 ladder ($1, $2, $5, $10 ... $100, $200, $500, $1,000 ...), at most MAX.</summary>
    public void StepBet(int direction)
    {
        if (direction == 0) return;
        long bet = GetCurrentBet();
        SetBet(direction > 0 ? LadderAbove(bet) : LadderBelow(bet));
    }

    /// <summary>+ can raise the bet (it is below MAX).</summary>
    public bool CanRaiseBet => GetMaxBet() >= 1 && GetCurrentBet() < GetMaxBet();

    /// <summary>- can lower the bet (it is above $1).</summary>
    public bool CanLowerBet => GetCurrentBet() > 1;

    static long LadderAbove(long value)
    {
        for (long scale = 1; scale <= long.MaxValue / 50; scale *= 10)
        {
            if (scale > value) return scale;
            if (scale * 2 > value) return scale * 2;
            if (scale * 5 > value) return scale * 5;
        }
        return value;
    }

    static long LadderBelow(long value)
    {
        long best = 1;
        for (long scale = 1; scale <= long.MaxValue / 50; scale *= 10)
        {
            if (scale >= value) return best;
            best = scale;
            if (scale * 2 >= value) return best;
            best = scale * 2;
            if (scale * 5 >= value) return best;
            best = scale * 5;
        }
        return best;
    }

    void ShowBet()
    {
        if (betAmountInput != null && !betAmountInput.isFocused)
            betAmountInput.SetTextWithoutNotify(UIFormat.Money(GetCurrentBet()));
    }

    /// <summary>
    /// The bet to place: the chosen bet, capped to the largest bet the company can put up now (it may have shrunk since
    /// the bet was chosen); at least $1. A bet still being typed into the bet field counts too.
    /// </summary>
    public long GetCurrentBet()
    {
        if (betAmountInput != null && betAmountInput.isFocused)
            ApplyTypedBet(betAmountInput.text);
        long bet = Math.Max(1L, currentBet);
        long max = GetMaxBet();
        return max >= 1 ? Math.Min(bet, max) : bet;
    }

    /// <summary>The chosen bet is more than the company can put up now, so the bet placed is capped to MAX.</summary>
    public bool IsBetCapped
    {
        get
        {
            long max = GetMaxBet();
            return max >= 1 && currentBet > max;
        }
    }

    /// <summary>The bet is all the company can put up (MAX).</summary>
    public bool IsMaxBet
    {
        get
        {
            long max = GetMaxBet();
            return max >= 1 && GetCurrentBet() >= max;
        }
    }

    /// <summary>
    /// A bet typed into a bet field, applied when the field is left: its digits ("$1,000" and "1000" both mean $1,000),
    /// clamped like SetBet. Text without digits keeps the bet.
    /// </summary>
    public void CommitTypedBet(string text)
    {
        betEditEndFrame = Time.frameCount;
        ApplyTypedBet(text);
        ShowBet();
        RaiseChanged();
    }

    /// <summary>Remember that a bet field ended its edit this frame (its Escape mustn't close anything too).</summary>
    public void NoteBetEditEnded()
    {
        betEditEndFrame = Time.frameCount;
    }

    void ApplyTypedBet(string text)
    {
        long typed = ParseDigits(text);
        if (typed <= 0) return;
        long max = GetMaxBet();
        currentBet = max >= 1 ? Math.Min(typed, max) : typed;
    }

    /// <summary>The ASCII digits of a text as a number (0 when there are none; huge numbers stop growing).</summary>
    public static long ParseDigits(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        long value = 0;
        bool any = false;
        foreach (char c in text)
        {
            if (c < '0' || c > '9') continue;
            any = true;
            if (value > (long.MaxValue - 9) / 10) return value;
            value = value * 10 + (c - '0');
        }
        return any ? value : 0;
    }

    /// <summary>"1000" (for editing a bet).</summary>
    public static string PlainNumber(long value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The player is typing a bet, or a bet field ended its edit this frame (Escape reached it first).</summary>
    bool IsEditingBet()
    {
        if (betEditEndFrame == Time.frameCount) return true;
        if (betAmountInput != null && betAmountInput.isFocused) return true;
        return runtimeView != null && runtimeView.IsEditingBet;
    }

    /// <summary>The company can put up this bet and still keep KeepInCompany (a blackjack double asks this).</summary>
    public bool CanAffordBet(long amount)
    {
        // Demo: the half-company cap is for choosing a bet; a double may use the room above the reserve, as in the full game
        long limit = DemoMode.Active ? RoomAboveReserve() : GetMaxBet();
        return amount >= 1 && CompanyManager.Instance != null && amount <= limit;
    }

    /// <summary>
    /// Whether a bet of <paramref name="amount"/> can be taken now; <paramref name="reason"/> says why not: the board
    /// froze gambling (HeatMeter.CanGamble), there is no company, or it would leave the company less than it keeps.
    /// </summary>
    public bool CanPlaceBet(double amount, out string reason)
    {
        if (CompanyManager.Instance == null)
        {
            reason = L10n.Get(CasinoUI.Table, "casino.msg.no_company");
            return false;
        }
        if (!HeatMeter.CanGamble(out reason))
            return false;
        long max = GetMaxBet();
        if (max < 1)
        {
            reason = NotEnoughMessage;
            return false;
        }
        if (double.IsNaN(amount) || double.IsInfinity(amount) || amount <= 0.0)
        {
            reason = L10n.Get(CasinoUI.Table, "casino.msg.no_bet");
            return false;
        }
        // The cap is floor(bankroll - reserve) for picking a bet; a fractional stake (a coin-flip pot) may use the exact
        // room above the reserve, so a pot that leaves exactly the reserve can still be doubled
        double room = Math.Max(max, PlayerWallet.Balance - KeepInCompany);
        if (amount > room + 1e-6)
        {
            reason = PlayerWallet.Active ? CashText.Get("cash.casino.over_max", ("max", UIFormat.Money(max)))
                                         : L10n.Get(CasinoUI.Table, "casino.msg.over_max", ("max", UIFormat.Money(max)), ("amount", UIFormat.Money(KeepInCompany)));
            return false;
        }
        reason = "";
        return true;
    }

    /// <summary>
    /// Take a bet from company value when its round starts. False (with <paramref name="reason"/>, see CanPlaceBet) when it
    /// can't be taken, and then nothing is taken.
    /// </summary>
    public bool TryPlaceBet(double amount, out string reason)
    {
        if (!CanPlaceBet(amount, out reason)) return false;
        if (!DeductBet(amount))
        {
            reason = NotEnoughMessage;
            return false;
        }
        return true;
    }

    /// <summary>
    /// A settled round, once, when its outcome shows: GambleLedger turns it into heat (and the tilt prompt watches losses).
    /// The game is the one on screen, else the one opened last (a spin or flip can settle after the casino closed).
    /// </summary>
    public static void ReportRound(double stake, double payout, double companyValueBefore)
    {
        ReportRound(stake, payout, companyValueBefore, CurrentGameDetail());
    }

    /// <summary>ReportRound naming the game: GambleLedger.DetailSlots, DetailBlackjack or DetailCoinFlip.</summary>
    public static void ReportRound(double stake, double payout, double companyValueBefore, string game)
    {
        GambleLedger.Report(GambleLedger.SourceCasino, stake, payout, companyValueBefore, game);
    }

    /// <summary>The GambleLedger detail of the game being played (null when it can't be told).</summary>
    static string CurrentGameDetail()
    {
        CasinoManager casino = Instance;
        if (casino == null) return null;
        CasinoScreen screen = casino.CurrentScreen;
        if (screen == CasinoScreen.Closed || screen == CasinoScreen.Hub)
            screen = casino.lastGame;
        switch (screen)
        {
            case CasinoScreen.Slots: return GambleLedger.DetailSlots;
            case CasinoScreen.Blackjack: return GambleLedger.DetailBlackjack;
            case CasinoScreen.CoinFlip: return GambleLedger.DetailCoinFlip;
            default: return null;
        }
    }

    public void UpdateBalanceDisplay()
    {
        if (balanceText != null)
            balanceText.text = UIFormat.Money(Math.Floor(BalanceNow()));
    }

    /// <summary>Winnings (and a push's returned bet) go to Player Cash (PlayerWallet).</summary>
    public void AddWinnings(double amount)
    {
        // Winnings are never negative (a negative amount would take money)
        if (amount <= 0.0 || double.IsNaN(amount) || CompanyManager.Instance == null) return;
        PlayerWallet.Receive(amount, "casino");
        UpdateBalanceDisplay();
        ReportMoney(0.0, amount);
    }

    /// <summary>Take a bet out of Player Cash (PlayerWallet). False when it isn't there (nothing is taken).</summary>
    public bool DeductBet(double amount)
    {
        // A negative bet would add money, so it is ignored
        if (amount <= 0.0 || double.IsNaN(amount) || CompanyManager.Instance == null) return false;
        if (!PlayerWallet.TrySpend(amount, "casino")) return false;
        UpdateBalanceDisplay();
        ReportMoney(amount, 0.0);
        return true;
    }

    static void ReportMoney(double bet, double won)
    {
        try { MoneyMoved?.Invoke(bet, won); }
        catch (Exception e) { Debug.LogException(e); }
    }
}
