using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Globalization;
using System.Text;
using Random = UnityEngine.Random;

/// <summary>
/// The casino's slot machine: three reels of eight weighted symbols. Three of a kind pays the symbol's multiplier, any
/// two matching pay twoOfAKindPayout (payouts include the bet; return to player 93.8%).
/// - Spin takes the casino's bet (CasinoManager.TryPlaceBet: heat, the $1,000 the company keeps) and decides the three
///   symbols right away; the reels stop one by one (ReelStopTime, real time: the casino pauses the game) and the spin
///   is paid and reported (CasinoManager.ReportRound) the moment the last reel stops.
/// - The reel texts, spin button and result text below are for a designed prefab. The runtime screen (CasinoSlotsView)
///   draws the symbols itself from SpinStarted / SpinFinished / SpinRefused.
/// </summary>
public class SlotMachineGame : MonoBehaviour
{
    [Header("Reel Displays")]
    public TextMeshProUGUI reel1Text;
    public TextMeshProUGUI reel2Text;
    public TextMeshProUGUI reel3Text;

    [Header("UI")]
    public Button spinButton;
    public TextMeshProUGUI resultText;
    public TextMeshProUGUI multiplierInfoText;

    [Header("Settings")]
    public float spinDuration = 2f;
    public float reelStopDelay = 0.3f;

    /// <summary>A spin started: the bet taken and the symbols the reels will stop on (indexes into the symbols).</summary>
    public event Action<long, int[]> SpinStarted;
    /// <summary>The last reel stopped: (multiplier, winnings). The winnings are already paid.</summary>
    public event Action<float, double> SpinFinished;
    /// <summary>A spin couldn't start (the board froze gambling, not enough money...): why.</summary>
    public event Action<string> SpinRefused;

    // Slot symbols with weights (lower = rarer). Drawn as icons by the runtime screen (CasinoIcon.Symbol).
    private readonly string[] symbols = { "CHERRY", "LEMON", "ORANGE", "GRAPE", "BELL", "STAR", "7", "DIAMOND" };
    private readonly int[] weights = { 25, 22, 20, 15, 10, 5, 2, 1 }; // Total = 100

    // Payouts (multipliers of the bet, bet included). Return to player = 93.8%:
    //   3 of a kind: sum(p^3 * payout)                  = 0.2741
    //   any 2 match: sum(3 * p^2 * (1 - p)) = 0.4429 * 1.5 = 0.6643
    // 3 of a kind payouts
    private readonly float[] threeOfAKindPayout = { 5f, 6f, 8f, 12f, 20f, 50f, 100f, 500f };
    // 2 of a kind payout
    private readonly float twoOfAKindPayout = 1.5f;

    private bool isSpinning = false;
    private long spinBet;   // bet of the spin in progress (already taken from the balance)
    private double spinValueBefore;
    private int[] reelResults = new int[3];

    /// <summary>A spin is running (its bet is taken, the outcome not shown yet).</summary>
    public bool IsSpinning => isSpinning;
    /// <summary>A spin has finished (LastMultiplier / LastWinnings / the reel results are its outcome).</summary>
    public bool HasResult { get; private set; }
    public long LastBet { get; private set; }
    public float LastMultiplier { get; private set; }
    public double LastWinnings { get; private set; }

    public int SymbolCount => symbols.Length;
    public string SymbolName(int index) => symbols[Mathf.Clamp(index, 0, symbols.Length - 1)];
    public int SymbolWeight(int index) => weights[Mathf.Clamp(index, 0, weights.Length - 1)];
    public float ThreeOfAKindPayout(int index) => threeOfAKindPayout[Mathf.Clamp(index, 0, threeOfAKindPayout.Length - 1)];
    public float TwoOfAKindPayout => twoOfAKindPayout;

    /// <summary>The symbol reel 0-2 stops (or stopped) on in the current or last spin.</summary>
    public int GetReelResult(int reel) => reelResults[Mathf.Clamp(reel, 0, 2)];

    /// <summary>Real seconds from the start of a spin until reel 0-2 stops; the outcome shows (and is paid) when reel 2 stops.</summary>
    public float ReelStopTime(int reel)
    {
        return Mathf.Max(0.1f, spinDuration) + Mathf.Clamp(reel, 0, 2) * Mathf.Max(0f, reelStopDelay);
    }

    /// <summary>What three symbols pay: the three-of-a-kind multiplier, TwoOfAKindPayout for any two, or 0.</summary>
    public float MultiplierFor(int a, int b, int c)
    {
        if (a == b && b == c) return ThreeOfAKindPayout(a);
        if (a == b || b == c || a == c) return twoOfAKindPayout;
        return 0f;
    }

    void Start()
    {
        if (spinButton != null)
            spinButton.onClick.AddListener(Spin);

        UpdateMultiplierInfo();
        ResetReels();
    }

    void OnDisable()
    {
        // Coroutines stop when the panel is hidden: settle a spin in progress (its symbols were decided when it started)
        if (isSpinning)
            FinishSpin();
    }

    void UpdateMultiplierInfo()
    {
        if (multiplierInfoText == null) return;

        var text = new StringBuilder("PAYOUTS:\n");
        for (int i = symbols.Length - 1; i >= 0; i--)
            text.Append(symbols[i]).Append(" x3 = ").Append(FormatMultiplier(threeOfAKindPayout[i])).Append('\n');
        text.Append("Any 2 match = ").Append(FormatMultiplier(twoOfAKindPayout));
        multiplierInfoText.text = text.ToString();
    }

    void ResetReels()
    {
        if (reel1Text != null) reel1Text.text = "?";
        if (reel2Text != null) reel2Text.text = "?";
        if (reel3Text != null) reel3Text.text = "?";
        SetResult("Place your bet and SPIN!", Color.white);
    }

    public void Spin()
    {
        if (isSpinning || !isActiveAndEnabled) return;

        CasinoManager casino = CasinoManager.Instance;
        if (casino == null)
        {
            Refuse(CasinoManager.ClosedMessage);
            return;
        }

        // The bet leaves company value now (unless the board froze gambling or the company can't cover it)
        long bet = casino.GetCurrentBet();
        double valueBefore = CasinoManager.CompanyValueNow();
        if (!casino.TryPlaceBet(bet, out string reason))
        {
            Refuse(reason);
            return;
        }
        spinBet = bet;
        spinValueBefore = valueBefore;
        LastBet = bet;
        HasResult = false;

        // Decide the result up front; the animation only shows it
        for (int i = 0; i < 3; i++)
        {
            reelResults[i] = GetWeightedRandomSymbol();
        }

        // Start spinning
        isSpinning = true;
        StartCoroutine(SpinReels());

        Action<long, int[]> started = SpinStarted;
        if (started != null)
        {
            try { started(bet, (int[])reelResults.Clone()); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }

    void Refuse(string reason)
    {
        SetResult(reason, Color.red);
        Action<string> refused = SpinRefused;
        if (refused == null) return;
        try { refused(reason); }
        catch (Exception e) { Debug.LogException(e); }
    }

    IEnumerator SpinReels()
    {
        isSpinning = true;
        if (spinButton != null) spinButton.interactable = false;
        SetResult("Spinning...", Color.white);

        // Animate spinning (unscaled: the casino pauses the game); each reel shows random symbols until it stops
        float elapsed = 0f;
        float end = ReelStopTime(2);
        int reelsLanded = 0;   // for their sounds (the last one lands in FinishSpin)
        while (elapsed < end)
        {
            for (int reel = 0; reel < 3; reel++)
            {
                TextMeshProUGUI text = ReelText(reel);
                if (text == null) continue;
                text.text = elapsed < ReelStopTime(reel) ? symbols[Random.Range(0, symbols.Length)] : symbols[reelResults[reel]];
            }
            if (elapsed >= ReelStopTime(reelsLanded)) { reelsLanded++; GameSfx.Play(Sfx.UiDrop, 0.6f); }   // a reel stops
            else GameSfx.PlayThrottled(Sfx.UiTick, 0.08f);                                                     // the reels click round
            yield return null;
            elapsed += Time.unscaledDeltaTime;
        }

        // The last reel stopped: the outcome shows, so it is settled now
        FinishSpin();
    }

    TextMeshProUGUI ReelText(int reel)
    {
        switch (reel)
        {
            case 0: return reel1Text;
            case 1: return reel2Text;
            default: return reel3Text;
        }
    }

    /// <summary>Show the final reels and pay out the spin in progress.</summary>
    void FinishSpin()
    {
        if (!isSpinning) return;
        isSpinning = false;

        if (reel1Text != null) reel1Text.text = symbols[reelResults[0]];
        if (reel2Text != null) reel2Text.text = symbols[reelResults[1]];
        if (reel3Text != null) reel3Text.text = symbols[reelResults[2]];

        // Calculate winnings
        float multiplier = CalculateWinnings();
        double winnings = spinBet * (double)multiplier;
        long stake = spinBet;
        spinBet = 0;

        if (multiplier > 0 && CasinoManager.Instance != null)
            CasinoManager.Instance.AddWinnings(winnings);
        CasinoManager.ReportRound(stake, multiplier > 0 ? winnings : 0.0, spinValueBefore);

        LastMultiplier = multiplier;
        LastWinnings = multiplier > 0 ? winnings : 0.0;
        HasResult = true;
        GameSfx.Play(Sfx.UiDrop, 0.6f);   // the last reel stops
        if (multiplier > 0) GameSfx.PlayDelayed(Sfx.ChipsWin, 0.15f, GameSfx.WinScale(multiplier));   // bigger wins are louder

        if (multiplier > 0)
        {
            string won = "You won $" + winnings.ToString("N0") + "! (" + FormatMultiplier(multiplier) + ")";
            if (multiplier >= 50)
                SetResult("JACKPOT!\n" + won, Color.green);
            else if (multiplier >= 10)
                SetResult("BIG WIN!\n" + won, Color.green);
            else
                SetResult("WIN!\n" + won, Color.green);
        }
        else
        {
            SetResult("No match. Try again!", Color.red);
        }

        if (spinButton != null) spinButton.interactable = true;

        Action<float, double> finished = SpinFinished;
        if (finished != null)
        {
            try { finished(multiplier, LastWinnings); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }

    void SetResult(string message, Color color)
    {
        if (resultText == null) return;
        resultText.text = message;
        resultText.color = color;
    }

    /// <summary>"500x", "1.5x".</summary>
    public static string FormatMultiplier(float multiplier)
    {
        return multiplier.ToString("0.##", CultureInfo.InvariantCulture) + "x";
    }

    int GetWeightedRandomSymbol()
    {
        int totalWeight = 0;
        foreach (int w in weights) totalWeight += w;

        int random = Random.Range(0, totalWeight);
        int cumulative = 0;

        for (int i = 0; i < weights.Length; i++)
        {
            cumulative += weights[i];
            if (random < cumulative)
                return i;
        }

        return 0;
    }

    float CalculateWinnings()
    {
        return MultiplierFor(reelResults[0], reelResults[1], reelResults[2]);
    }
}
