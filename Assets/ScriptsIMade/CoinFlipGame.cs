using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

/// <summary>
/// The casino's coin flip: call heads or tails; a win pays payoutMultiplier x the bet (1.95x: return to player 97.5%).
/// Then double or nothing: the pot (the last win) goes up again on one more flip that pays twice the pot or nothing.
/// - Every flip is a round: its stake leaves company value when the coin is tossed (CasinoManager.TryPlaceBet: heat, the
///   $1,000 the company keeps) and it is settled, paid and reported (CasinoManager.ReportRound) the moment the coin
///   lands. So a win is in company value right away; the pot only says what "Double or nothing" would put up again, and
///   Collect just ends the streak.
/// - The texts, buttons and Double or Nothing panel below are for a designed prefab. The runtime screen
///   (CasinoCoinFlipView) animates its own coin from FlipStarted / FlipLanded / Collected / FlipRefused.
/// - Demo (DemoMode): at most DemoConfig.maxDoubleStreak doubles in a row (CanDoubleAgain); one more is refused.
/// </summary>
public class CoinFlipGame : MonoBehaviour
{
    [Header("UI Elements")]
    public TextMeshProUGUI coinText;
    public TextMeshProUGUI resultText;
    public TextMeshProUGUI streakText;
    public Button headsButton;
    public Button tailsButton;
    public Image coinImage;

    [Header("Sprites (Optional)")]
    public Sprite headsSprite;
    public Sprite tailsSprite;

    [Header("Settings")]
    public float flipDuration = 1.5f;
    public float payoutMultiplier = 1.95f; // Slightly less than 2x (house edge): return to player 97.5%
    [Tooltip("Double or nothing pays the pot times this")]
    public float doubleOrNothingMultiplier = 2f;

    [Header("Double or Nothing")]
    public GameObject doubleOrNothingPanel;
    public TextMeshProUGUI currentWinningsText;
    public Button doubleButton;
    public Button collectButton;

    /// <summary>The coin was tossed: (stake taken, whether it is a double-or-nothing flip).</summary>
    public event Action<double, bool> FlipStarted;
    /// <summary>The coin landed: (heads, won, paid). A win is already in company value.</summary>
    public event Action<bool, bool, double> FlipLanded;
    /// <summary>The streak ended with this pot kept (Collect, or a new bet).</summary>
    public event Action<double> Collected;
    /// <summary>A flip couldn't start (the board froze gambling, not enough money...): why.</summary>
    public event Action<string> FlipRefused;

    private bool isFlipping = false;
    private int currentStreak = 0;
    private double pendingWinnings = 0;   // the pot: the last win (already paid), what Double or Nothing puts up again
    private bool inDoubleOrNothing = false;

    // The flip in progress (decided when it starts, so leaving mid-flip can't change it)
    private bool flipChoseHeads;
    private bool flipIsHeads;
    private double flipPayout;
    private double flipStake;
    private double flipValueBefore;
    private bool flipIsDouble;

    /// <summary>The coin is in the air (its stake is taken, the outcome not shown yet).</summary>
    public bool IsFlipping => isFlipping;
    /// <summary>Wins in a row in this streak.</summary>
    public int Streak => currentStreak;
    /// <summary>
    /// Demo: Double or Nothing is still allowed (at most DemoConfig.maxDoubleStreak doubles in a row; the streak's first win
    /// is the plain flip). Always true outside the demo.
    /// </summary>
    public bool CanDoubleAgain => !DemoMode.Active || currentStreak - 1 < DemoMode.Config.maxDoubleStreak;
    /// <summary>Demo: why a double was refused ("Demo limit: three doubles in a row. Collect your pot.").</summary>
    public static string StreakCapMessage => DemoMode.T("demo.casino.streak_cap");
    /// <summary>The last win, already in company value: what Double or Nothing puts up again (0 = no open pot).</summary>
    public double Pot => pendingWinnings;
    public bool HasPot => pendingWinnings > 0.0;
    /// <summary>A flip has landed (the Last... values are its outcome).</summary>
    public bool HasResult { get; private set; }
    public bool LastCallHeads => flipChoseHeads;
    public bool LastFaceHeads => flipIsHeads;
    public bool LastFlipWon { get; private set; }
    public bool LastFlipWasDouble => flipIsDouble;
    public double LastStake => flipStake;
    public double LastPaid { get; private set; }
    /// <summary>Real seconds the coin is in the air.</summary>
    public float FlipSeconds => Mathf.Max(0.2f, flipDuration);
    /// <summary>What a new flip pays on a win for a <paramref name="bet"/> (bet included).</summary>
    public double WinPayoutFor(double bet) => MoneyMath.Cents(bet * MoneyMath.Exact(payoutMultiplier));
    /// <summary>What Double or Nothing pays on a win for the pot now.</summary>
    public double DoublePayout => MoneyMath.Cents(pendingWinnings * MoneyMath.Exact(Mathf.Max(0f, doubleOrNothingMultiplier)));

    void Start()
    {
        if (headsButton != null)
            headsButton.onClick.AddListener(() => Flip(true));
        if (tailsButton != null)
            tailsButton.onClick.AddListener(() => Flip(false));
        if (doubleButton != null)
            doubleButton.onClick.AddListener(DoubleOrNothing);
        if (collectButton != null)
            collectButton.onClick.AddListener(CollectWinnings);

        ResetGame();
    }

    void ResetGame()
    {
        if (coinText != null) coinText.text = "?";
        SetResult("Choose HEADS or TAILS!", Color.white);
        if (doubleOrNothingPanel != null) doubleOrNothingPanel.SetActive(false);

        currentStreak = 0;
        pendingWinnings = 0;
        inDoubleOrNothing = false;
        isFlipping = false;
        UpdateStreakText();
        SetFlipButtons(true);
    }

    /// <summary>
    /// Toss the coin, calling heads or tails: a new flip for the casino's bet (an open pot is kept first), or, once
    /// DoubleOrNothing armed it, a flip for the pot.
    /// </summary>
    public void Flip(bool choseHeads)
    {
        if (isFlipping || !isActiveAndEnabled) return;

        CasinoManager casino = CasinoManager.Instance;
        if (casino == null)
        {
            Refuse(CasinoManager.ClosedMessage);
            return;
        }

        double valueBefore = CasinoManager.CompanyValueNow();
        bool isDouble = inDoubleOrNothing && pendingWinnings > 0.0;
        string reason;
        if (isDouble)
        {
            // Demo: the doubles cap (the pot stays open; the next plain flip is a new bet)
            if (!CanDoubleAgain)
            {
                inDoubleOrNothing = false;
                Refuse(StreakCapMessage);
                return;
            }
            // Double or nothing: the pot (already in company value) goes up again
            double stake = pendingWinnings;
            if (!casino.TryPlaceBet(stake, out reason))
            {
                Refuse(reason);
                return;
            }
            flipStake = stake;
            flipPayout = DoublePayout;
        }
        else
        {
            // A new bet: an open pot is kept (it is already paid)
            if (pendingWinnings > 0.0)
                EndStreak();

            long bet = casino.GetCurrentBet();
            if (!casino.TryPlaceBet(bet, out reason))
            {
                Refuse(reason);
                return;
            }
            flipStake = bet;
            flipPayout = WinPayoutFor(bet);
        }

        // Determine result (50/50)
        flipChoseHeads = choseHeads;
        flipIsHeads = UnityEngine.Random.value < 0.5f;
        flipValueBefore = valueBefore;
        flipIsDouble = isDouble;
        inDoubleOrNothing = false;

        if (doubleOrNothingPanel != null)
            doubleOrNothingPanel.SetActive(false);

        isFlipping = true;
        if (isDouble) GameSfx.Play(Sfx.ChipBet);   // double or nothing: the pot goes up again
        GameSfx.Play(Sfx.CoinFlick);
        StartCoroutine(FlipCoin());

        Action<double, bool> started = FlipStarted;
        if (started != null)
        {
            try { started(flipStake, isDouble); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }

    /// <summary>Double or nothing with this call: one flip for the pot (nothing happens without a pot).</summary>
    public void DoubleOrNothing(bool choseHeads)
    {
        if (isFlipping || pendingWinnings <= 0.0) return;
        if (!CanDoubleAgain)
        {
            Refuse(StreakCapMessage);   // Demo: at most maxDoubleStreak doubles in a row
            return;
        }
        inDoubleOrNothing = true;
        Flip(choseHeads);
        if (!isFlipping) inDoubleOrNothing = false;   // refused: the pot stays open, the next plain flip is a new bet
    }

    void Refuse(string reason)
    {
        SetResult(reason, Color.red);
        Action<string> refused = FlipRefused;
        if (refused == null) return;
        try { refused(reason); }
        catch (Exception e) { Debug.LogException(e); }
    }

    IEnumerator FlipCoin()
    {
        SetFlipButtons(false);
        SetResult("Flipping...", Color.white);

        // Animate flip (realtime: the casino pauses the game)
        float elapsed = 0f;
        float duration = FlipSeconds;
        while (elapsed < duration)
        {
            if (coinText != null)
                coinText.text = Mathf.FloorToInt(elapsed / 0.1f) % 2 == 0 ? "HEADS" : "TAILS";
            yield return null;
            elapsed += Time.unscaledDeltaTime;
        }

        // The coin shows its face: settled now, so a quit from here on keeps the outcome
        ResolveFlip();
    }

    void ShowCoinFace()
    {
        if (coinText != null)
            coinText.text = flipIsHeads ? "HEADS" : "TAILS";

        if (coinImage != null)
        {
            if (flipIsHeads && headsSprite != null)
                coinImage.sprite = headsSprite;
            else if (!flipIsHeads && tailsSprite != null)
                coinImage.sprite = tailsSprite;
        }
    }

    /// <summary>Apply the result of the flip in progress: a win is paid now and becomes the pot; every flip is reported.</summary>
    void ResolveFlip()
    {
        if (!isFlipping) return;
        isFlipping = false;
        ShowCoinFace();
        GameSfx.Play(Sfx.CoinLand);

        string face = flipIsHeads ? "HEADS" : "TAILS";
        bool won = flipChoseHeads == flipIsHeads;
        double paid = won ? flipPayout : 0.0;
        CasinoManager casino = CasinoManager.Instance;
        if (won && casino != null)
            casino.AddWinnings(paid);
        CasinoManager.ReportRound(flipStake, paid, flipValueBefore);

        LastFlipWon = won;
        LastPaid = paid;
        HasResult = true;

        if (won)
        {
            GameSfx.PlayDelayed(Sfx.ChipsWin, 0.3f);   // after the landing
            currentStreak++;
            pendingWinnings = paid;
            SetResult(face + "!\nYou WIN $" + pendingWinnings.ToString("N0") + "!", Color.green);
            UpdateStreakText();

            // Show double or nothing option
            ShowDoubleOrNothing();
        }
        else
        {
            SetResult(face + "!\nYou LOSE!", Color.red);

            pendingWinnings = 0;
            currentStreak = 0;
            inDoubleOrNothing = false;
            UpdateStreakText();

            if (doubleOrNothingPanel != null)
                doubleOrNothingPanel.SetActive(false);
            SetFlipButtons(true);
        }

        Action<bool, bool, double> landed = FlipLanded;
        if (landed != null)
        {
            try { landed(flipIsHeads, won, paid); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }

    void ShowDoubleOrNothing()
    {
        // Without the Double or Nothing panel the flip buttons stay on: the next flip is a new bet (the pot is kept)
        if (doubleOrNothingPanel == null || doubleButton == null || collectButton == null)
        {
            SetFlipButtons(true);
            return;
        }

        doubleOrNothingPanel.SetActive(true);
        if (DemoMode.Active) doubleButton.interactable = CanDoubleAgain;   // Demo: off once the doubles cap is reached

        if (currentWinningsText != null)
            currentWinningsText.text = "Current Winnings: $" + pendingWinnings.ToString("N0") +
                                       "\n\nDouble to: $" + DoublePayout.ToString("N0") +
                                       "\nor Collect now!";

        SetFlipButtons(false); // choose Double or Collect first
    }

    /// <summary>The Double button of a designed panel: the next Heads/Tails flip puts the pot up.</summary>
    public void DoubleOrNothing()
    {
        if (isFlipping || pendingWinnings <= 0.0) return;
        if (!CanDoubleAgain)
        {
            Refuse(StreakCapMessage);   // Demo: at most maxDoubleStreak doubles in a row
            return;
        }

        inDoubleOrNothing = true;
        if (doubleOrNothingPanel != null)
            doubleOrNothingPanel.SetActive(false);

        // Player will choose heads or tails again
        SetFlipButtons(true);
        SetResult("DOUBLE OR NOTHING!\nChoose HEADS or TAILS!", Color.white);
    }

    /// <summary>Walk away with the pot: it is already in company value, so this just ends the streak.</summary>
    public void CollectWinnings()
    {
        if (isFlipping) return;

        if (pendingWinnings > 0)
            SetResult("Collected $" + pendingWinnings.ToString("N0") + "!", Color.green);
        if (pendingWinnings > 0) GameSfx.Play(Sfx.ChipsCollect);   // walking away with the pot
        EndStreak();

        if (doubleOrNothingPanel != null)
            doubleOrNothingPanel.SetActive(false);

        SetFlipButtons(true);
    }

    void EndStreak()
    {
        double kept = pendingWinnings;
        pendingWinnings = 0;
        inDoubleOrNothing = false;
        currentStreak = 0;
        UpdateStreakText();
        if (kept <= 0.0) return;

        Action<double> collected = Collected;
        if (collected == null) return;
        try { collected(kept); }
        catch (Exception e) { Debug.LogException(e); }
    }

    void OnDisable()
    {
        // Leaving mid-flip: settle that flip (the coroutine stopped; its outcome was decided when it was tossed). Any
        // pot is already in company value, so nothing unresolved is left.
        if (isFlipping)
            ResolveFlip();

        ResetGame();
    }

    void SetFlipButtons(bool interactable)
    {
        if (headsButton != null) headsButton.interactable = interactable;
        if (tailsButton != null) tailsButton.interactable = interactable;
    }

    void UpdateStreakText()
    {
        if (streakText != null)
            streakText.text = "Win Streak: " + currentStreak;
    }

    void SetResult(string message, Color color)
    {
        if (resultText == null) return;
        resultText.text = message;
        resultText.color = color;
    }
}
