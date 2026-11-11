using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>Outcome of a blackjack hand.</summary>
public enum BlackjackResult
{
    Win,
    Lose,
    Push
}

/// <summary>
/// Single-hand blackjack (used by the office upgrade flow).
/// Pauses the game through UIManager while open. Escape does nothing during the hand;
/// once the result is shown, Close or Escape closes it and the callback fires exactly once.
/// Both are ignored for a moment after the result appears, so the second click of a double-click (a natural
/// blackjack shows its result at once) can't close it unseen.
/// A natural blackjack (21 with the first two cards) wins right away.
/// </summary>
public class BlackjackGame : MonoBehaviour
{
    public static BlackjackGame Instance;

    /// <summary>Real seconds after the result appears during which Close and Escape are ignored.</summary>
    const float CloseGuardSeconds = 0.35f;

    [Header("UI Elements")]
    public GameObject blackjackPanel;
    public TextMeshProUGUI playerHandText;
    public TextMeshProUGUI dealerHandText;
    public TextMeshProUGUI playerScoreText;
    public TextMeshProUGUI dealerScoreText;
    public TextMeshProUGUI resultText;
    public TextMeshProUGUI stakeText;

    [Header("Buttons")]
    public Button hitButton;
    public Button standButton;
    public Button closeButton;

    [Header("Result Colors")]
    public Color winColor = Color.green;
    public Color loseColor = Color.red;
    public Color pushColor = new Color(1f, 0.85f, 0.3f);

    // Card deck: codes 0-51, rank = code % 13 + 1 (1 = Ace, 11-13 = J Q K), suit = code / 13 (0 spades, 1 hearts, 2 diamonds, 3 clubs)
    private List<int> deck = new List<int>();
    private List<int> playerHand = new List<int>();
    private List<int> dealerHand = new List<int>();
    private string lastMessage = "";

    // Game state
    private bool gameInProgress = false;   // the hand is being played
    private bool resultPending = false;    // hand finished, waiting for Close
    private float resultShownAt = -10f;    // Time.unscaledTime when the result appeared
    private BlackjackResult result = BlackjackResult.Lose;
    private System.Action<BlackjackResult> onGameComplete;
    private string currentStakeDescription;

    /// <summary>True from StartGame until the result is closed.</summary>
    public bool IsOpen => gameInProgress || resultPending;

    /// <summary>True while the player can still Hit/Stand.</summary>
    public bool IsHandInProgress => gameInProgress;

    /// <summary>A new game can start (panel assigned and no game running).</summary>
    public bool CanStartGame => blackjackPanel != null && !IsOpen;

    /// <summary>Result of the last finished hand.</summary>
    public BlackjackResult LastResult => result;

    /// <summary>What's at stake in the current game (shown on the panel).</summary>
    public string CurrentStake => currentStakeDescription;

    /// <summary>Raised whenever the hands or the state change (deal, hit, stand, result, close).</summary>
    public event System.Action Changed;

    /// <summary>
    /// Raised once when a hand is decided, while its result is still on the table (before Close).
    /// Settle the stake here, so a save or a quit before Close can't undo the outcome.
    /// </summary>
    public event System.Action<BlackjackResult> HandEnded;

    /// <summary>The player's cards as codes (see Rank / Suit).</summary>
    public IReadOnlyList<int> PlayerCards => playerHand;

    /// <summary>The dealer's cards as codes; the second one is face down while the hand is played.</summary>
    public IReadOnlyList<int> DealerCards => dealerHand;

    /// <summary>True while the dealer's second card is still face down.</summary>
    public bool DealerHoleHidden => gameInProgress;

    public int PlayerTotal => GetHandValue(playerHand);

    /// <summary>The dealer's total (all cards once revealed).</summary>
    public int DealerTotal => GetHandValue(dealerHand);

    /// <summary>The value of the dealer's face-up card while the second one is hidden.</summary>
    public int DealerVisibleTotal => dealerHand.Count > 0 ? GetCardValue(dealerHand[0]) : 0;

    /// <summary>The result line of the finished hand ("Dealer bust! You win!"), empty while playing.</summary>
    public string LastMessage => lastMessage;

    public static int Rank(int code) => code % 13 + 1;
    public static int Suit(int code) => code / 13;

    void Awake()
    {
        Instance = this;
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
        if (hitButton != null) hitButton.onClick.AddListener(Hit);
        if (standButton != null) standButton.onClick.AddListener(Stand);
        if (closeButton != null) closeButton.onClick.AddListener(CloseGame);

        if (blackjackPanel != null && !IsOpen) blackjackPanel.SetActive(false);
    }

    void Update()
    {
        // Safety net: if another script hid the panel mid-game, finish the game so the caller never waits forever
        // (right away: the close guard is only for the player's clicks)
        if (IsOpen && (blackjackPanel == null || !blackjackPanel.activeSelf))
        {
            if (gameInProgress) Stand();
            if (resultPending) FinishGame();
        }
    }

    /// <summary>
    /// Start a hand. The callback gets true only for a win (a push counts as false here).
    /// Returns false if the game couldn't start (then the callback is never called).
    /// </summary>
    public bool StartGame(string stakeDescription, System.Action<bool> callback)
    {
        System.Action<BlackjackResult> wrapped = null;
        if (callback != null)
            wrapped = r => callback(r == BlackjackResult.Win);
        return StartGame(stakeDescription, wrapped);
    }

    /// <summary>
    /// Start a hand. The callback fires exactly once, when the player closes the result.
    /// Returns false if the game couldn't start (then the callback is never called).
    /// </summary>
    public bool StartGame(string stakeDescription, System.Action<BlackjackResult> callback)
    {
        if (IsOpen)
        {
            Debug.LogWarning("[BlackjackGame] A game is already running.");
            return false;
        }
        if (blackjackPanel == null)
        {
            Debug.LogWarning("[BlackjackGame] blackjackPanel is not assigned, so a game can't start.");
            return false;
        }

        currentStakeDescription = stakeDescription;
        onGameComplete = callback;
        lastMessage = "";

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

        // Show UI
        blackjackPanel.SetActive(true);
        UIManager.BringToFront(blackjackPanel);
        if (stakeText != null) stakeText.text = "Stake: " + stakeDescription;
        if (resultText != null) resultText.text = "";

        // Enable buttons
        SetPlayButtons(true);
        if (closeButton != null) closeButton.gameObject.SetActive(false);

        gameInProgress = true;
        resultPending = false;

        // Pause; Escape is swallowed during the hand so nothing underneath closes
        UIManager ui = UIManager.Instance;
        if (ui != null)
            ui.OpenPanel(this, true, IgnoreEscape);

        UpdateUI(false); // Hide dealer's second card
        Changed?.Invoke();
        GameSfx.PlayPair(Sfx.CardShuffle, Sfx.CardDeal, 0.3f);   // before a natural blackjack's result (0.35 s)

        // Natural blackjack wins right away
        if (GetHandValue(playerHand) == 21)
        {
            UpdateUI(true);
            EndGame(BlackjackResult.Win, "BLACKJACK! You WIN!");
        }

        return true;
    }

    void IgnoreEscape()
    {
    }

    void InitializeDeck()
    {
        deck.Clear();
        // 52 cards: code = suit * 13 + (rank - 1)
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

    int GetCardValue(int code)
    {
        int card = Rank(code);
        if (card == 1) return 11; // Ace (will be adjusted if bust)
        if (card >= 10) return 10; // 10, J, Q, K
        return card;
    }

    int GetHandValue(List<int> hand)
    {
        int value = 0;
        int aces = 0;

        foreach (int code in hand)
        {
            value += GetCardValue(code);
            if (Rank(code) == 1) aces++;
        }

        // Adjust for aces if bust
        while (value > 21 && aces > 0)
        {
            value -= 10;
            aces--;
        }

        return value;
    }

    string GetCardName(int code)
    {
        int card = Rank(code);
        switch (card)
        {
            case 1: return "A";
            case 11: return "J";
            case 12: return "Q";
            case 13: return "K";
            default: return card.ToString();
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

    void SetPlayButtons(bool interactable)
    {
        if (hitButton != null) hitButton.interactable = interactable;
        if (standButton != null) standButton.interactable = interactable;
    }

    public void Hit()
    {
        if (!gameInProgress) return;

        playerHand.Add(DrawCard());
        GameSfx.Play(Sfx.CardDeal);
        UpdateUI(false);
        Changed?.Invoke();

        int playerValue = GetHandValue(playerHand);
        if (playerValue > 21)
        {
            UpdateUI(true);
            EndGame(BlackjackResult.Lose, "BUST! You went over 21!");
        }
        else if (playerValue == 21)
        {
            Stand(); // Auto-stand on 21
        }
    }

    public void Stand()
    {
        if (!gameInProgress) return;

        // Disable player buttons
        SetPlayButtons(false);

        // Dealer draws until 17 or higher
        while (GetHandValue(dealerHand) < 17)
        {
            dealerHand.Add(DrawCard());
        }

        UpdateUI(true); // Show full dealer hand
        GameSfx.Play(Sfx.CardPlace);   // the hole card turns over

        // Determine winner
        int playerValue = GetHandValue(playerHand);
        int dealerValue = GetHandValue(dealerHand);

        if (dealerValue > 21)
        {
            EndGame(BlackjackResult.Win, "Dealer BUST! You WIN!");
        }
        else if (playerValue > dealerValue)
        {
            EndGame(BlackjackResult.Win, "You WIN! " + playerValue + " vs " + dealerValue);
        }
        else if (dealerValue > playerValue)
        {
            EndGame(BlackjackResult.Lose, "Dealer wins. " + dealerValue + " vs " + playerValue);
        }
        else
        {
            EndGame(BlackjackResult.Push, "PUSH! " + playerValue + " vs " + dealerValue + " - it's a tie.");
        }
    }

    void EndGame(BlackjackResult outcome, string message)
    {
        gameInProgress = false;
        resultPending = true;
        resultShownAt = Time.unscaledTime;
        result = outcome;
        lastMessage = message;
        GameSfx.PlayResult(outcome, 0.35f);   // win: chips, push: chips back, loss: a soft clink (after the last card)

        SetPlayButtons(false);

        if (resultText != null)
        {
            resultText.text = message;
            resultText.color = outcome == BlackjackResult.Win ? winColor
                             : outcome == BlackjackResult.Push ? pushColor
                             : loseColor;
        }

        if (closeButton != null) closeButton.gameObject.SetActive(true);

        // Now Escape closes it too (the game stays paused until the caller's result is up)
        UIManager ui = UIManager.Instance;
        if (ui != null)
            ui.OpenPanel(this, true, CloseGame);

        try
        {
            HandEnded?.Invoke(outcome);
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// Close the finished game and report the result (the Close / Continue button and Escape call this). Does nothing
    /// while the hand is still being played, or for CloseGuardSeconds after the result appeared.
    /// </summary>
    public void CloseGame()
    {
        if (!resultPending) return;
        if (Time.unscaledTime - resultShownAt < CloseGuardSeconds) return;
        FinishGame();
    }

    /// <summary>Close the finished game now and report the result exactly once.</summary>
    void FinishGame()
    {
        if (!resultPending) return;
        resultPending = false;

        if (blackjackPanel != null) blackjackPanel.SetActive(false);

        System.Action<BlackjackResult> callback = onGameComplete;
        onGameComplete = null;

        try
        {
            // The caller opens its own result panel first, so the game never unpauses in between
            callback?.Invoke(result);
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
        }
        finally
        {
            // Unless the callback already started a new game
            if (!IsOpen && UIManager.Exists)
                UIManager.Instance.ClosePanel(this);
        }
    }
}
