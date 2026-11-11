using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using Random = UnityEngine.Random;

/// <summary>How a casino blackjack hand ended.</summary>
public enum CasinoHandResult
{
    None,
    Win,
    Blackjack,
    Push,
    Lose
}

/// <summary>
/// The casino's blackjack table: one 52-card deck shuffled for every hand, the dealer draws to 17 (stands on every 17),
/// blackjack pays 3:2 (2.5x back), a win 1:1 (2x back), a tie returns the bet. Both naturals push; a dealer natural wins
/// at once. Double down: the first decision only, one more card, then the dealer plays.
/// - Deal and Double Down take their bets through CasinoManager.TryPlaceBet (heat, the $1,000 the company keeps, which
///   caps doubles too). The hand is paid and reported (CasinoManager.ReportRound, the whole stake) the moment its
///   outcome is known.
/// - Cards are codes 0-51: rank = code % 13 + 1 (1 = ace, 11-13 = J Q K), suit = code / 13 (0 spades, 1 hearts,
///   2 diamonds, 3 clubs), like BlackjackGame.
/// - The texts and buttons below are for a designed prefab. The runtime screen (CasinoBlackjackView) lays out real cards
///   from PlayerCards / DealerCards and Changed / HandEnded / Refused.
/// </summary>
public class CasinoBlackjack : MonoBehaviour
{
    [Header("UI Elements")]
    public TextMeshProUGUI playerHandText;
    public TextMeshProUGUI dealerHandText;
    public TextMeshProUGUI playerScoreText;
    public TextMeshProUGUI dealerScoreText;
    public TextMeshProUGUI resultText;
    public TextMeshProUGUI currentBetText;

    [Header("Buttons")]
    public Button dealButton;
    public Button hitButton;
    public Button standButton;
    public Button doubleDownButton;

    [Header("Payout Settings")]
    public float blackjackPayout = 2.5f;  // 3:2 payout for blackjack
    public float normalWinPayout = 2f;     // 1:1 payout for normal win

    /// <summary>Something changed on the table (a deal, a card, the end of the hand).</summary>
    public event Action Changed;
    /// <summary>The hand ended: (result, paid). The money is already settled.</summary>
    public event Action<CasinoHandResult, double> HandEnded;
    /// <summary>A deal or a double down couldn't be placed (the board froze gambling, not enough money...): why.</summary>
    public event Action<string> Refused;

    // Card deck (codes 0-51)
    private List<int> deck = new List<int>();
    private List<int> playerHand = new List<int>();
    private List<int> dealerHand = new List<int>();

    private bool gameInProgress = false;
    private long currentBet = 0;
    private bool canDoubleDown = true;
    private bool doubled;
    private double handValueBefore;

    /// <summary>A hand is being played (its bet is taken, the player still has to act).</summary>
    public bool IsHandInProgress => gameInProgress;
    /// <summary>Counts every deal (a new hand clears the table).</summary>
    public int HandNumber { get; private set; }
    public IReadOnlyList<int> PlayerCards => playerHand;
    public IReadOnlyList<int> DealerCards => dealerHand;
    /// <summary>The dealer's second card is face down (while the hand is being played).</summary>
    public bool DealerHoleHidden => gameInProgress && dealerHand.Count >= 2;
    public int PlayerTotal => GetHandValue(playerHand);
    public int DealerTotal => GetHandValue(dealerHand);
    /// <summary>What the dealer's face-up card counts.</summary>
    public int DealerVisibleTotal => dealerHand.Count > 0 ? GetCardValue(dealerHand[0]) : 0;
    /// <summary>The money on the table in this (or the last) hand, doubled after a double down.</summary>
    public long CurrentBet => currentBet;
    public bool Doubled => doubled;
    /// <summary>Double down is allowed now (the first decision of the hand).</summary>
    public bool CanDoubleDown => gameInProgress && canDoubleDown && playerHand.Count == 2;
    /// <summary>The extra money a double down puts up (the bet again).</summary>
    public long DoubleDownCost => currentBet;
    public CasinoHandResult LastResult { get; private set; }
    public double LastPayout { get; private set; }
    /// <summary>The last hand's outcome in words ("Dealer BUST! You WIN!").</summary>
    public string LastMessage { get; private set; } = "";

    /// <summary>What a card counts (an ace 11 here; GetHandValue makes aces 1 where needed).</summary>
    public static int CardValue(int code)
    {
        int rank = code % 13 + 1;
        if (rank == 1) return 11;
        if (rank >= 10) return 10;
        return rank;
    }

    /// <summary>A two-card 21.</summary>
    public static bool IsNatural(IReadOnlyList<int> hand)
    {
        return hand != null && hand.Count == 2 && CardValue(hand[0]) + CardValue(hand[1]) == 21;
    }

    void Start()
    {
        if (dealButton != null) dealButton.onClick.AddListener(Deal);
        if (hitButton != null) hitButton.onClick.AddListener(Hit);
        if (standButton != null) standButton.onClick.AddListener(Stand);
        if (doubleDownButton != null) doubleDownButton.onClick.AddListener(DoubleDown);

        ResetTable();
    }

    void OnDisable()
    {
        // Leaving the table mid-hand (casino closed / back to selection) stands, so the bet is settled, not lost
        if (gameInProgress)
            Stand();
    }

    void ResetTable()
    {
        if (playerHandText != null) playerHandText.text = "Your Hand: -";
        if (dealerHandText != null) dealerHandText.text = "Dealer: -";
        if (playerScoreText != null) playerScoreText.text = "Score: -";
        if (dealerScoreText != null) dealerScoreText.text = "Score: ?";
        if (resultText != null) resultText.text = "Press DEAL to start!";
        if (currentBetText != null) currentBetText.text = "Bet: $0";

        SetButtonStates(true, false, false, false);
        gameInProgress = false;
    }

    void SetButtonStates(bool deal, bool hit, bool stand, bool doubleDown)
    {
        if (dealButton != null) dealButton.interactable = deal;
        if (hitButton != null) hitButton.interactable = hit;
        if (standButton != null) standButton.interactable = stand;
        if (doubleDownButton != null) doubleDownButton.interactable = doubleDown;
    }

    public void Deal()
    {
        if (gameInProgress) return;

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
        currentBet = bet;
        handValueBefore = valueBefore;
        doubled = false;
        LastResult = CasinoHandResult.None;
        LastPayout = 0.0;
        LastMessage = "";
        HandNumber++;
        if (currentBetText != null)
            currentBetText.text = "Bet: $" + currentBet.ToString("N0");

        // Setup deck
        InitializeDeck();
        ShuffleDeck();

        // Clear hands
        playerHand.Clear();
        dealerHand.Clear();

        // Deal initial cards
        playerHand.Add(DrawCard());
        dealerHand.Add(DrawCard());
        playerHand.Add(DrawCard());
        dealerHand.Add(DrawCard());

        gameInProgress = true;
        canDoubleDown = true;

        // Check if can afford double down
        bool canAffordDouble = casino.CanAffordBet(currentBet);

        SetButtonStates(false, true, true, canAffordDouble && canDoubleDown);

        SetResult("", Color.white);

        UpdateUI(false);

        // Check for blackjack
        int playerValue = GetHandValue(playerHand);
        int dealerValue = GetHandValue(dealerHand);

        if (playerValue == 21 && dealerValue == 21)
        {
            // Both blackjack - push
            EndGame(CasinoHandResult.Push, 0, "Both Blackjack! Push - bet returned.");
        }
        else if (playerValue == 21)
        {
            // Player blackjack!
            EndGame(CasinoHandResult.Blackjack, blackjackPayout, "BLACKJACK! You win!");
        }
        else if (dealerValue == 21)
        {
            // Dealer blackjack
            UpdateUI(true);
            EndGame(CasinoHandResult.Lose, -1, "Dealer Blackjack! You lose.");
        }
        else
        {
            RaiseChanged();
        }
    }

    void Refuse(string reason)
    {
        SetResult(reason, Color.red);
        Action<string> refused = Refused;
        if (refused == null) return;
        try { refused(reason); }
        catch (Exception e) { Debug.LogException(e); }
    }

    void InitializeDeck()
    {
        deck.Clear();
        for (int code = 0; code < 52; code++)
            deck.Add(code);
    }

    void ShuffleDeck()
    {
        for (int i = deck.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            int temp = deck[i];
            deck[i] = deck[j];
            deck[j] = temp;
        }
    }

    int DrawCard()
    {
        if (deck.Count == 0)
        {
            InitializeDeck();
            ShuffleDeck();
        }

        int card = deck[0];
        deck.RemoveAt(0);
        return card;
    }

    int GetCardValue(int card)
    {
        return CardValue(card);
    }

    int GetHandValue(List<int> hand)
    {
        int value = 0;
        int aces = 0;

        foreach (int card in hand)
        {
            value += GetCardValue(card);
            if (card % 13 == 0) aces++;
        }

        while (value > 21 && aces > 0)
        {
            value -= 10;
            aces--;
        }

        return value;
    }

    string GetCardName(int card)
    {
        switch (card % 13 + 1)
        {
            case 1: return "A";
            case 11: return "J";
            case 12: return "Q";
            case 13: return "K";
            default: return (card % 13 + 1).ToString();
        }
    }

    string GetHandString(List<int> hand, bool hideSecond = false)
    {
        string result = "";
        for (int i = 0; i < hand.Count; i++)
        {
            if (i == 1 && hideSecond)
                result += "[?] ";
            else
                result += "[" + GetCardName(hand[i]) + "] ";
        }
        return result.Trim();
    }

    void UpdateUI(bool showDealerFull)
    {
        if (playerHandText != null)
            playerHandText.text = "Your Hand: " + GetHandString(playerHand);

        if (dealerHandText != null)
            dealerHandText.text = "Dealer: " + GetHandString(dealerHand, !showDealerFull);

        if (playerScoreText != null)
            playerScoreText.text = "Score: " + GetHandValue(playerHand);

        if (dealerScoreText != null)
        {
            if (showDealerFull)
                dealerScoreText.text = "Score: " + GetHandValue(dealerHand);
            else
                dealerScoreText.text = "Score: ?";
        }
    }

    public void Hit()
    {
        if (!gameInProgress) return;

        playerHand.Add(DrawCard());
        canDoubleDown = false;

        if (doubleDownButton != null)
            doubleDownButton.interactable = false;

        UpdateUI(false);

        int playerValue = GetHandValue(playerHand);
        if (playerValue > 21)
        {
            EndGame(CasinoHandResult.Lose, -1, "BUST! You went over 21.");
        }
        else if (playerValue == 21)
        {
            Stand();
        }
        else
        {
            RaiseChanged();
        }
    }

    public void Stand()
    {
        if (!gameInProgress) return;

        SetButtonStates(false, false, false, false);

        // Dealer draws until 17
        while (GetHandValue(dealerHand) < 17)
        {
            dealerHand.Add(DrawCard());
        }

        UpdateUI(true);

        int playerValue = GetHandValue(playerHand);
        int dealerValue = GetHandValue(dealerHand);

        if (dealerValue > 21)
        {
            EndGame(CasinoHandResult.Win, normalWinPayout, "Dealer BUST! You WIN!");
        }
        else if (playerValue > dealerValue)
        {
            EndGame(CasinoHandResult.Win, normalWinPayout, "You WIN! " + playerValue + " vs " + dealerValue);
        }
        else if (dealerValue > playerValue)
        {
            EndGame(CasinoHandResult.Lose, -1, "Dealer wins. " + dealerValue + " vs " + playerValue);
        }
        else
        {
            EndGame(CasinoHandResult.Push, 0, "PUSH! It's a tie - bet returned.");
        }
    }

    public void DoubleDown()
    {
        if (!gameInProgress || !canDoubleDown) return;

        CasinoManager casino = CasinoManager.Instance;
        if (casino == null)
        {
            Refuse(L10n.Get(CasinoUI.Table, "casino.msg.no_double"));
            return;
        }

        // Double the bet: the extra money is a bet like any other (heat, what the company keeps)
        if (!casino.TryPlaceBet(currentBet, out string reason))
        {
            Refuse(reason);
            return;
        }
        currentBet *= 2;
        doubled = true;
        canDoubleDown = false;
        GameSfx.Play(Sfx.ChipBet);   // the second stack goes on the table (the view deals and turns the cards with their sounds)
        if (currentBetText != null)
            currentBetText.text = "Bet: $" + currentBet.ToString("N0");

        // Draw one card and stand
        playerHand.Add(DrawCard());
        UpdateUI(false);

        int playerValue = GetHandValue(playerHand);
        if (playerValue > 21)
        {
            EndGame(CasinoHandResult.Lose, -1, "BUST! You went over 21.");
        }
        else
        {
            Stand();
        }
    }

    void EndGame(CasinoHandResult result, float payoutMultiplier, string message)
    {
        gameInProgress = false;
        UpdateUI(true);

        CasinoManager casino = CasinoManager.Instance;
        double paid = 0.0;
        if (payoutMultiplier > 0)
        {
            // Payout includes the bet (2x = even money, 2.5x = 3:2 blackjack)
            double winnings = currentBet * (double)payoutMultiplier;
            if (casino != null)
                casino.AddWinnings(winnings);
            paid = winnings;

            SetResult(message + "\n+$" + winnings.ToString("N0"), Color.green);
        }
        else if (payoutMultiplier == 0)
        {
            // Push - return bet
            if (casino != null)
                casino.AddWinnings(currentBet);
            paid = currentBet;

            SetResult(message, Color.white);
        }
        else
        {
            SetResult(message + "\n-$" + currentBet.ToString("N0"), Color.red);
        }

        // The outcome is known: the round (the whole stake, doubled or not) is settled and reported now
        CasinoManager.ReportRound(currentBet, paid, handValueBefore);
        LastResult = result;
        LastPayout = paid;
        LastMessage = message;

        SetButtonStates(true, false, false, false);

        RaiseChanged();
        Action<CasinoHandResult, double> ended = HandEnded;
        if (ended != null)
        {
            try { ended(result, paid); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }

    void SetResult(string message, Color color)
    {
        if (resultText == null) return;
        resultText.text = message;
        resultText.color = color;
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
}

