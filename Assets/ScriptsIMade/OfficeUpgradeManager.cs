using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Upgrade flow for office items (e.g. the sofa):
/// choice panel (Blackjack or Random Chance) -> blackjack hand or dice roll -> result panel.
/// Every step pauses the game through UIManager. The cost is only charged once the chosen method
/// can actually run; a blackjack push refunds it. Escape cancels the choice and closes the result.
/// Both methods are gambles: each outcome is reported to GambleLedger (heat), and neither starts while the
/// heat meter has frozen gambling (HeatMeter.CanGamble).
/// </summary>
public class OfficeUpgradeManager : MonoBehaviour
{
    public static OfficeUpgradeManager Instance;

    [Header("UI Panels")]
    public GameObject upgradeChoicePanel;
    public GameObject randomResultPanel;

    [Header("Choice Panel UI")]
    public TextMeshProUGUI itemNameText;
    public TextMeshProUGUI currentLevelText;
    public TextMeshProUGUI upgradeCostText;
    public TextMeshProUGUI upgradeInfoText;
    public Button blackjackButton;
    public Button randomChanceButton;
    public Button cancelButton;

    [Header("Random Result UI")]
    public TextMeshProUGUI randomResultText;
    public TextMeshProUGUI randomOutcomeText;
    public Button randomCloseButton;

    [Header("Random Chance Settings")]
    [Tooltip("Normal success chance, on top of the critical success chance")]
    [Range(0f, 1f)]
    public float baseSuccessChance = 0.5f;      // 50% normal success
    [Tooltip("Free upgrade: +1 level and the cost is refunded")]
    [Range(0f, 1f)]
    public float criticalSuccessChance = 0.1f;  // 10% chance for free upgrade
    [Tooltip("Lose a level (only when the item is above level 0)")]
    [Range(0f, 1f)]
    public float criticalFailChance = 0.1f;     // 10% chance to lose a level

    [Header("Blackjack Reference")]
    public BlackjackGame blackjackGame;

    [Header("Result Colors")]
    public Color successColor = Color.green;
    public Color failColor = Color.red;
    public Color neutralColor = new Color(1f, 0.85f, 0.3f);

    [Header("Designed UI")]
    [Tooltip("Write the old panel texts and button labels. Off when the designed upgrade screens (UpgradeViews) draw them.")]
    public bool writeTexts = true;

    /// <summary>What the last upgrade attempt did, for designed result screens.</summary>
    public enum ResultKind { Upgraded, UpgradedFree, Nothing, Downgraded, BlackjackWin, BlackjackPush, BlackjackLoss }
    public ResultKind LastResult { get; private set; }
    /// <summary>What the last attempt cost (0 when it was refunded).</summary>
    public int LastCost { get; private set; }
    /// <summary>The item of the last attempt (still valid while its result is on screen).</summary>
    public OfficeItem LastItem { get; private set; }
    /// <summary>The upgrade price of the last attempt (also when it was refunded).</summary>
    public int LastPrice { get; private set; }

    enum Stage { Idle, Choosing, PlayingBlackjack, ShowingResult }
    enum Outcome { Success, Fail, Neutral }

    // Current upgrade in progress
    private OfficeItemInteractable currentItem;
    private Stage stage = Stage.Idle;
    private int chargedCost;
    private bool listenersAdded;
    private bool blackjackSettled;          // the hand's outcome is already applied (at the end of the hand, before Continue)
    private BlackjackResult blackjackResult;
    private double gambleValueBefore;       // company value before the price was taken (for the gamble ledger)

    /// <summary>True while an upgrade (choice, blackjack or result) is on screen.</summary>
    public bool IsBusy => stage != Stage.Idle;

    /// <summary>
    /// The price at stake in the blackjack upgrade on the table (shown by the table view). The outcome is applied
    /// as soon as the hand ends; a save never adds it back, so quitting mid-hand forfeits it.
    /// </summary>
    public int PendingCharge => stage == Stage.PlayingBlackjack ? chargedCost : 0;

    /// <summary>The item being upgraded, or null.</summary>
    public OfficeItemInteractable CurrentItem => currentItem;

    void Awake()
    {
        Instance = this;
        if (blackjackGame == null)
            blackjackGame = GetComponent<BlackjackGame>();
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
        if (blackjackGame != null)
            blackjackGame.HandEnded -= OnBlackjackHandEnded;
        if (UIManager.Exists)
            UIManager.Instance.ClosePanel(this);
    }

    void Start()
    {
        // Setup button listeners
        if (!listenersAdded)
        {
            listenersAdded = true;
            if (blackjackButton != null)
                blackjackButton.onClick.AddListener(ChooseBlackjack);
            if (randomChanceButton != null)
                randomChanceButton.onClick.AddListener(ChooseRandomChance);
            if (cancelButton != null)
                cancelButton.onClick.AddListener(CancelUpgrade);
            if (randomCloseButton != null)
                randomCloseButton.onClick.AddListener(CloseRandomResult);
        }

        // Hide panels at start (unless an upgrade already started)
        if (stage == Stage.Idle)
        {
            if (upgradeChoicePanel != null) upgradeChoicePanel.SetActive(false);
            if (randomResultPanel != null) randomResultPanel.SetActive(false);
        }
    }

    void Update()
    {
        // Safety net: never stay paused waiting for a blackjack game that no longer exists
        if (stage == Stage.PlayingBlackjack && (blackjackGame == null || !blackjackGame.IsOpen))
        {
            Refund(chargedCost);
            FinishUpgrade();
        }
    }

    public void ShowUpgradeChoice(OfficeItemInteractable item)
    {
        if (item == null || item.itemData == null || !item.itemData.CanUpgrade()) return;
        if (stage != Stage.Idle) return; // an upgrade is already in progress
        if (upgradeChoicePanel == null)
        {
            Debug.LogWarning("[OfficeUpgradeManager] upgradeChoicePanel is not assigned.");
            return;
        }

        currentItem = item;
        stage = Stage.Choosing;
        RefreshChoicePanel();

        // Show panel
        upgradeChoicePanel.SetActive(true);
        UIManager.BringToFront(upgradeChoicePanel);
        OpenPanel(CancelUpgrade);
        GameSfx.Play(Sfx.UiOpen);   // opened by clicking an office item (no button click to hear)
    }

    void RefreshChoicePanel()
    {
        if (currentItem == null || currentItem.itemData == null) return;
        OfficeItem data = currentItem.itemData;
        if (!writeTexts)
        {
            bool afford = CanAfford(data.GetUpgradeCost());
            if (blackjackButton != null) blackjackButton.interactable = afford && IsBlackjackAvailable();
            if (randomChanceButton != null) randomChanceButton.interactable = afford;
            return;
        }

        // Update UI (texts: the Company table, CompanyText)
        if (itemNameText != null)
            itemNameText.text = data.DisplayName;

        if (currentLevelText != null)
            currentLevelText.text = CompanyText.Get("upgrade.choice.current", ("level", data.GetCurrentLevelName()),
                                                    ("number", data.currentLevel.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        if (upgradeCostText != null)
            upgradeCostText.text = CompanyText.Get("upgrade.choice.cost", ("amount", UIFormat.Money(data.GetUpgradeCost())));

        if (upgradeInfoText != null)
        {
            upgradeInfoText.text = CompanyText.Get("upgrade.choice.next", ("level", data.GetNextLevelName())) + "\n" +
                                   CompanyText.Get("upgrade.choice.daily", ("amount", UIFormat.Money(data.dailyBonusPerLevel))) + "\n" +
                                   CompanyText.Get("upgrade.choice.productivity", ("percent", CompanyText.Percent(data.productivityPerLevel)));
        }

        // Check if player can afford
        bool canAfford = CanAfford(data.GetUpgradeCost());
        bool blackjackAvailable = IsBlackjackAvailable();

        if (blackjackButton != null)
        {
            blackjackButton.interactable = canAfford && blackjackAvailable;
            string label;
            if (!blackjackAvailable) label = CompanyText.Get("upgrade.choice.blackjack.unavailable");
            else if (!canAfford) label = CompanyText.Get("upgrade.choice.cant_afford");
            else label = CompanyText.Get("upgrade.choice.blackjack");
            SetButtonLabel(blackjackButton, label);
        }

        if (randomChanceButton != null)
        {
            randomChanceButton.interactable = canAfford;
            SetButtonLabel(randomChanceButton, canAfford ? GetRandomChanceLabel(data) : CompanyText.Get("upgrade.choice.cant_afford"));
        }
    }

    /// <summary>Button label with the real odds, e.g. "Random Chance (60% success, 10% lose a level)", in the current language.</summary>
    public string GetRandomChanceLabel(OfficeItem data)
    {
        GetRandomChanceOdds(data, out float criticalSuccess, out float success, out float criticalFail, out _);
        string successText = CompanyText.Percent(criticalSuccess + success);
        if (criticalFail > 0f)
            return CompanyText.Get("upgrade.choice.random.lose", ("success", successText), ("lose", CompanyText.Percent(criticalFail)));
        return CompanyText.Get("upgrade.choice.random", ("success", successText));
    }

    /// <summary>
    /// The real odds of a random upgrade for an item at its current level (they add up to 1):
    /// critical success = +1 level and refund, success = +1 level, critical fail = -1 level, fail = nothing.
    /// </summary>
    public void GetRandomChanceOdds(OfficeItem data, out float criticalSuccess, out float success, out float criticalFail, out float fail)
    {
        criticalSuccess = Mathf.Clamp01(criticalSuccessChance);
        float totalSuccess = Mathf.Clamp01(criticalSuccessChance + baseSuccessChance);
        success = totalSuccess - criticalSuccess;
        bool canLoseLevel = data != null && data.currentLevel > 0;
        criticalFail = canLoseLevel ? Mathf.Clamp(criticalFailChance, 0f, 1f - totalSuccess) : 0f;
        fail = Mathf.Max(0f, 1f - totalSuccess - criticalFail);
    }

    static void SetButtonLabel(Button button, string text)
    {
        if (button == null) return;
        TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null)
            label.text = text;
    }

    bool IsBlackjackAvailable()
    {
        if (blackjackGame == null)
            blackjackGame = GetComponent<BlackjackGame>() != null ? GetComponent<BlackjackGame>() : BlackjackGame.Instance;
        return blackjackGame != null && blackjackGame.CanStartGame;
    }

    static bool CanAfford(int cost)
    {
        return CompanyManager.Instance != null && CompanyManager.Instance.companyValue >= cost;
    }

    bool TryCharge(int cost)
    {
        if (!CanAfford(cost)) return false;
        gambleValueBefore = CompanyManager.Instance.companyValue;
        CompanyManager.Instance.AddMoney(-cost, true);
        chargedCost = cost;
        return true;
    }

    void Refund(int amount)
    {
        if (amount > 0 && CompanyManager.Instance != null)
            CompanyManager.Instance.AddMoney(amount, true);
        chargedCost = 0;
    }

    void OpenPanel(System.Action onEscape)
    {
        UIManager ui = UIManager.Instance;
        if (ui != null)
            ui.OpenPanel(this, true, onEscape);
    }

    void IgnoreEscape()
    {
    }

    /// <summary>Choice panel: upgrade by playing a blackjack hand.</summary>
    public void ChooseBlackjack()
    {
        if (stage != Stage.Choosing || currentItem == null || currentItem.itemData == null) return;
        if (!GamblingAllowed()) return;

        // Check before charging, so the money is never taken for a game that can't run
        if (!IsBlackjackAvailable())
        {
            Debug.LogWarning("[OfficeUpgradeManager] No BlackjackGame available - blackjack upgrades are disabled.");
            RefreshChoicePanel();
            return;
        }

        // Take the money
        int cost = currentItem.itemData.GetUpgradeCost();
        if (!TryCharge(cost))
        {
            RefreshChoicePanel();
            return;
        }

        // Hide choice panel; stay registered (paused, Escape ignored) while the hand is played
        stage = Stage.PlayingBlackjack;
        if (upgradeChoicePanel != null) upgradeChoicePanel.SetActive(false);
        OpenPanel(IgnoreEscape);

        // Start blackjack game (listen first: a natural blackjack ends the hand inside StartGame)
        string stake = CompanyText.Get("upgrade.stake", ("item", currentItem.itemData.DisplayName), ("level", currentItem.itemData.GetNextLevelName()));
        blackjackSettled = false;
        blackjackGame.HandEnded -= OnBlackjackHandEnded;
        blackjackGame.HandEnded += OnBlackjackHandEnded;
        if (!blackjackGame.StartGame(stake, OnBlackjackComplete))
        {
            // Couldn't start after all: give the money back and return to the choice
            blackjackGame.HandEnded -= OnBlackjackHandEnded;
            Refund(cost);
            stage = Stage.Choosing;
            if (upgradeChoicePanel != null) upgradeChoicePanel.SetActive(true);
            OpenPanel(CancelUpgrade);
            RefreshChoicePanel();
        }
    }

    /// <summary>
    /// The hand is decided (its result is still on the table): apply it now, so a save or a quit before
    /// Continue keeps the outcome. chargedCost stays set until the result closes (the table shows it).
    /// </summary>
    void OnBlackjackHandEnded(BlackjackResult result)
    {
        if (blackjackGame != null)
            blackjackGame.HandEnded -= OnBlackjackHandEnded;
        if (stage != Stage.PlayingBlackjack || blackjackSettled) return;
        blackjackSettled = true;
        blackjackResult = result;

        if (currentItem == null || currentItem.itemData == null)
        {
            // The item vanished mid-game: money back
            if (chargedCost > 0 && CompanyManager.Instance != null)
                CompanyManager.Instance.AddMoney(chargedCost, true);
            return;
        }

        OfficeItem data = currentItem.itemData;
        switch (result)
        {
            case BlackjackResult.Win:
                Record(ResultKind.BlackjackWin, chargedCost, data, chargedCost);
                SetItemLevel(data.currentLevel + 1);
                ReportGamble(GambleLedger.SourceUpgradeBlackjack, chargedCost, chargedCost);   // the upgrade is worth its price
                break;

            case BlackjackResult.Push:
                // Tie: money back, nothing changes
                Record(ResultKind.BlackjackPush, 0, data, chargedCost);
                if (chargedCost > 0 && CompanyManager.Instance != null)
                    CompanyManager.Instance.AddMoney(chargedCost, true);
                ReportGamble(GambleLedger.SourceUpgradeBlackjack, chargedCost, chargedCost);
                break;

            default:
                // Lost - money is gone
                Record(ResultKind.BlackjackLoss, chargedCost, data, chargedCost);
                ReportGamble(GambleLedger.SourceUpgradeBlackjack, chargedCost, 0.0);
                break;
        }
    }

    void OnBlackjackComplete(BlackjackResult result)
    {
        if (stage != Stage.PlayingBlackjack) return;

        // The table closed without reporting the end of the hand: settle now
        if (!blackjackSettled)
            OnBlackjackHandEnded(result);

        if (currentItem == null || currentItem.itemData == null)
        {
            // The item vanished mid-game (refunded when the hand ended)
            FinishUpgrade();
            return;
        }

        OfficeItem data = currentItem.itemData;
        switch (blackjackResult)
        {
            case BlackjackResult.Win:
                ShowRandomResult(Outcome.Success, CompanyText.Get("upgrade.result.blackjack_win",
                    ("item", data.DisplayName), ("level", data.GetCurrentLevelName())));
                break;

            case BlackjackResult.Push:
                ShowRandomResult(Outcome.Neutral, CompanyText.Get("upgrade.result.blackjack_push",
                    ("amount", UIFormat.Money(chargedCost)), ("item", data.DisplayName), ("level", data.GetCurrentLevelName())));
                break;

            default:
                ShowRandomResult(Outcome.Fail, CompanyText.Get("upgrade.result.blackjack_loss",
                    ("amount", UIFormat.Money(chargedCost)), ("item", data.DisplayName), ("level", data.GetCurrentLevelName())));
                break;
        }
    }

    /// <summary>Choice panel: upgrade by a random roll (odds shown on the button).</summary>
    public void ChooseRandomChance()
    {
        if (stage != Stage.Choosing || currentItem == null || currentItem.itemData == null) return;
        if (!GamblingAllowed()) return;

        // Take the money
        OfficeItem data = currentItem.itemData;
        int cost = data.GetUpgradeCost();
        if (!TryCharge(cost))
        {
            RefreshChoicePanel();
            return;
        }

        // Hide choice panel
        if (upgradeChoicePanel != null) upgradeChoicePanel.SetActive(false);

        // Roll the dice!
        GameSfx.Play(Sfx.DiceThrow);
        GetRandomChanceOdds(data, out float criticalSuccess, out float success, out float criticalFail, out _);
        float roll = Random.value;

        if (roll < criticalSuccess)
        {
            // CRITICAL SUCCESS - Free upgrade + refund!
            Record(ResultKind.UpgradedFree, 0, data, cost);
            SetItemLevel(data.currentLevel + 1);
            Refund(cost);
            ReportGamble(GambleLedger.SourceUpgradeChance, cost, cost * 2.0);   // the upgrade and the money back

            ShowRandomResult(Outcome.Success, CompanyText.Get("upgrade.result.critical_success",
                ("item", data.DisplayName), ("level", data.GetCurrentLevelName())));
        }
        else if (roll < criticalSuccess + success)
        {
            // Normal success
            Record(ResultKind.Upgraded, cost, data, cost);
            SetItemLevel(data.currentLevel + 1);
            ReportGamble(GambleLedger.SourceUpgradeChance, cost, cost);   // the upgrade is worth its price

            ShowRandomResult(Outcome.Success, CompanyText.Get("upgrade.result.success",
                ("item", data.DisplayName), ("level", data.GetCurrentLevelName())));
        }
        else if (criticalFail > 0f && roll >= 1f - criticalFail)
        {
            // CRITICAL FAIL - Lose a level!
            Record(ResultKind.Downgraded, cost, data, cost);
            SetItemLevel(data.currentLevel - 1);
            ReportGamble(GambleLedger.SourceUpgradeChance, cost, 0.0);

            ShowRandomResult(Outcome.Fail, CompanyText.Get("upgrade.result.critical_failure",
                ("item", data.DisplayName), ("level", data.GetCurrentLevelName())));
        }
        else
        {
            // Normal fail - just lose money
            Record(ResultKind.Nothing, cost, data, cost);
            ReportGamble(GambleLedger.SourceUpgradeChance, cost, 0.0);
            ShowRandomResult(Outcome.Fail, CompanyText.Get("upgrade.result.failed",
                ("amount", UIFormat.Money(cost)), ("item", data.DisplayName), ("level", data.GetCurrentLevelName())));
        }
    }

    /// <summary>Gambling upgrades stop while the board has frozen gambling (heat): say so before any money moves.</summary>
    static bool GamblingAllowed()
    {
        if (HeatMeter.CanGamble(out string reason)) return true;
        GameNotifications.Show(reason, NotificationKind.Bad);
        return false;
    }

    /// <summary>Tell the gamble ledger (heat, the tilt prompt) how an upgrade gamble ended. An upgrade counts as worth its price.</summary>
    void ReportGamble(string source, int stake, double payout)
    {
        GambleLedger.Report(source, stake, payout, gambleValueBefore);
    }

    void Record(ResultKind kind, int cost, OfficeItem item, int price)
    {
        LastResult = kind;
        LastCost = cost;
        LastItem = item;
        LastPrice = price;
    }

    void SetItemLevel(int level)
    {
        if (currentItem == null || currentItem.itemData == null) return;
        currentItem.itemData.currentLevel = Mathf.Clamp(level, 0, Mathf.Max(0, currentItem.itemData.maxLevel));
        currentItem.OnUpgraded();
    }

    void ShowRandomResult(Outcome outcome, string message)
    {
        // Success or failure (a push has none): after the dice of a random roll have landed, at once after the table
        GameSfx.PlayDelayed(outcome == Outcome.Success ? Sfx.UiConfirm : outcome == Outcome.Fail ? Sfx.UiError : Sfx.None, stage == Stage.Choosing ? 0.5f : 0f);
        stage = Stage.ShowingResult;
        if (upgradeChoicePanel != null) upgradeChoicePanel.SetActive(false);

        if (randomResultPanel == null)
        {
            // Nothing to show it on: log it and finish instead of leaving the game paused
            Debug.Log("[OfficeUpgradeManager] " + message.Replace("\n", " "));
            FinishUpgrade();
            return;
        }

        randomResultPanel.SetActive(true);
        UIManager.BringToFront(randomResultPanel);

        if (randomResultText != null && writeTexts)
        {
            randomResultText.text = message;
        }

        if (randomOutcomeText != null && writeTexts)
        {
            switch (outcome)
            {
                case Outcome.Success:
                    randomOutcomeText.text = CompanyText.Get("upgrade.outcome.success");
                    randomOutcomeText.color = successColor;
                    break;
                case Outcome.Neutral:
                    randomOutcomeText.text = CompanyText.Get("upgrade.outcome.no_change");
                    randomOutcomeText.color = neutralColor;
                    break;
                default:
                    randomOutcomeText.text = CompanyText.Get("upgrade.outcome.failed");
                    randomOutcomeText.color = failColor;
                    break;
            }
        }

        // Still paused; Escape (or Close) closes the result
        OpenPanel(CloseRandomResult);
    }

    /// <summary>Result panel: close it and resume the game.</summary>
    public void CloseRandomResult()
    {
        if (stage != Stage.ShowingResult) return;
        if (randomResultPanel != null) randomResultPanel.SetActive(false);
        FinishUpgrade();
    }

    /// <summary>Choice panel: back out without paying.</summary>
    public void CancelUpgrade()
    {
        if (stage != Stage.Choosing) return;
        if (upgradeChoicePanel != null) upgradeChoicePanel.SetActive(false);
        FinishUpgrade();
    }

    void FinishUpgrade()
    {
        currentItem = null;
        chargedCost = 0;
        stage = Stage.Idle;

        if (UIManager.Exists)
            UIManager.Instance.ClosePanel(this);
    }
}
