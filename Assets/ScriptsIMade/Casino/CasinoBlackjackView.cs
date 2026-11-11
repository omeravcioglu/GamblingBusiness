using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The casino blackjack screen (design board CasinoBlackjack): the felt with its printed arcs and rules placard, the shoe,
/// the dealer's and your hands as real cards with their totals ("9 + ?" while the hole card is down), the betting circle
/// with the chips on it and the bet (- / +), the message slot (the "Press DEAL" slip, or the result banner) and the
/// actions: Deal, Hit, Stand, Double Down (ringed when it is on offer), then Deal again and Back to casino.
/// Drives CasinoBlackjack. The game settles a hand at once; the table deals the cards out of the shoe one by one and turns
/// the hole card over (real time), holding the result and the company value until it has caught up.
/// Built at runtime by Build unless a designed screen fills the references.
/// </summary>
public class CasinoBlackjackView : CasinoScreen
{
    const float CardX = 526f;
    const float DealerY = 180f;
    const float PlayerY = 502f;
    const float CardStep = 168f;
    const float DealerLastMaxX = 960f;   // the shoe starts at 1127
    const float PlayerLastMaxX = 950f;   // the betting circle starts at 1112
    const float DealSeconds = 0.22f;
    const float RevealSeconds = 0.24f;
    static readonly Vector2 ShoePosition = new Vector2(1127f, 196f);

    [Tooltip("Empty = the casino's blackjack table")]
    public CasinoBlackjack game;

    [Header("Table")]
    [Tooltip("Cards are made here (150 x 210, from its top-left, y down)")]
    public RectTransform cardLayer;
    [Tooltip("The dashed card places shown before the first deal")]
    public GameObject emptySlots;
    public TMP_Text dealerTotal;
    public TMP_Text playerTotal;
    public GameObject dealerTotalChip;
    public GameObject playerTotalChip;

    [Header("Bet")]
    public CasinoButton betMinusButton;
    public CasinoButton betPlusButton;
    public TMP_Text betText;
    public RectTransform betChip;
    [Tooltip("The chips on the betting circle, bottom to top (the third shows after a double down)")]
    public CasinoChip[] stackChips = new CasinoChip[3];
    public TMP_Text stackLabel;

    [Header("Message slot")]
    public GameObject betSlip;
    public TMP_Text betSlipText;
    public GameObject resultBanner;
    public TMP_Text resultTitle;
    public TMP_Text resultAmount;
    public TMP_Text resultNote;
    [Tooltip("Sparkles around the banner after a win")]
    public GameObject[] resultSparkles = new GameObject[2];

    [Header("Actions")]
    public GameObject playActions;
    public CasinoButton dealButton;
    public CasinoButton hitButton;
    public CasinoButton standButton;
    public CasinoButton doubleButton;
    public TMP_Text doubleNote;
    public GameObject resultActions;
    public CasinoButton dealAgainButton;
    public Button resultBackButton;

    readonly List<CasinoCard> dealerCards = new List<CasinoCard>();
    readonly List<CasinoCard> playerCards = new List<CasinoCard>();
    readonly List<CasinoCard> spareCards = new List<CasinoCard>();
    CasinoBlackjack subscribed;
    int shownHand = -1;
    bool tableClear = true;

    // The card being dealt or turned over
    CasinoCard moving;
    bool revealing;
    Vector2 moveFrom;
    Vector2 moveTo;
    float moveStart;

    string refusal;
    float refusalUntil;
    string shownState;
    long shownBet = -1;
    int soundedHand = -1;   // the hand whose result sound played

    CasinoBlackjack Game
    {
        get
        {
            if (game != null) return game;
            CasinoManager casino = Manager;
            if (casino != null && casino.blackjack != null) return casino.blackjack;
            return FindAnyObjectByType<CasinoBlackjack>(FindObjectsInactive.Include);
        }
    }

    /// <summary>A card is on its way, or the table hasn't shown all of the game's cards yet.</summary>
    bool CatchingUp
    {
        get
        {
            if (moving != null) return true;
            CasinoBlackjack g = Game;
            if (g == null || tableClear && !g.IsHandInProgress && g.HandNumber == shownHand) return false;
            if (g.HandNumber != shownHand) return true;
            if (playerCards.Count < g.PlayerCards.Count || dealerCards.Count < g.DealerCards.Count) return true;
            return dealerCards.Count >= 2 && !dealerCards[1].FaceUp && !g.DealerHoleHidden;
        }
    }

    public override bool IsBusy => CatchingUp;

    protected override void OnWire()
    {
        if (dealButton != null && dealButton.button != null) dealButton.button.onClick.AddListener(Deal);
        if (dealAgainButton != null && dealAgainButton.button != null) dealAgainButton.button.onClick.AddListener(Deal);
        if (hitButton != null && hitButton.button != null) hitButton.button.onClick.AddListener(() => Act(0));
        if (standButton != null && standButton.button != null) standButton.button.onClick.AddListener(() => Act(1));
        if (doubleButton != null && doubleButton.button != null) doubleButton.button.onClick.AddListener(() => Act(2));
        if (betMinusButton != null && betMinusButton.button != null) betMinusButton.button.onClick.AddListener(() => Step(-1));
        if (betPlusButton != null && betPlusButton.button != null) betPlusButton.button.onClick.AddListener(() => Step(1));
        if (resultBackButton != null) resultBackButton.onClick.AddListener(OnBack);
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        CasinoBlackjack g = Game;
        Subscribe(g);
        refusal = null;
        shownState = null;
        shownBet = -1;
        ClearTable();
        // A fresh table: the last hand stays in the past unless one is still being played
        shownHand = g != null && !g.IsHandInProgress ? g.HandNumber : -1;
        tableClear = shownHand >= 0;
        Refresh();
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        Subscribe(null);
        FinishMove();
    }

    // The fonts switch by themselves; the texts this screen writes are written again on the next Refresh
    protected override void OnLanguageRefresh()
    {
        shownState = null;
    }

    protected override void FitLayout()
    {
        base.FitLayout();
        RefitBetChip(betChip, betText, betPlusButton != null ? betPlusButton.button : null, 300f);
        LayoutResultActions();
    }

    void Subscribe(CasinoBlackjack target)
    {
        if (subscribed == target) return;
        if (subscribed != null) subscribed.Refused -= OnRefused;
        subscribed = target;
        if (subscribed != null) subscribed.Refused += OnRefused;
    }

    void OnRefused(string reason)
    {
        refusal = reason;
        refusalUntil = Time.unscaledTime + 5f;
        shownState = null;
    }

    void Deal()
    {
        CasinoBlackjack g = Game;
        if (g == null || g.IsHandInProgress || CatchingUp) return;
        Subscribe(g);
        refusal = null;
        g.Deal();
        shownState = null;
    }

    void Act(int action)
    {
        CasinoBlackjack g = Game;
        if (g == null || !g.IsHandInProgress || CatchingUp) return;
        refusal = null;
        if (action == 0) g.Hit();
        else if (action == 1) g.Stand();
        else g.DoubleDown();
        shownState = null;
    }

    void Step(int direction)
    {
        CasinoManager casino = Manager;
        CasinoBlackjack g = Game;
        if (casino == null || (g != null && g.IsHandInProgress) || CatchingUp) return;
        casino.StepBet(direction);
    }

    // ------------------------------------------------------------------
    // Cards
    // ------------------------------------------------------------------

    void Update()
    {
        CasinoBlackjack g = Game;
        if (g == null) return;
        if (g.HandNumber != shownHand)
        {
            // A new hand: clear the table and deal it out
            FinishMove();
            ClearTable();
            shownHand = g.HandNumber;
            tableClear = false;
            shownState = null;
            GameSfx.Play(Sfx.CardShuffle);   // a fresh deck for every hand
        }
        if (moving != null)
        {
            AnimateMove();
            return;
        }
        if (!tableClear) NextStep(g);
    }

    /// <summary>Start the next thing the table owes the game: the deal P D P D(face down), your hits, the hole card, the dealer's draws.</summary>
    void NextStep(CasinoBlackjack g)
    {
        IReadOnlyList<int> player = g.PlayerCards, dealer = g.DealerCards;
        if (playerCards.Count == 0 && player.Count > 0) DealCard(playerCards, player[0], true, PlayerY, player.Count, PlayerLastMaxX);
        else if (dealerCards.Count == 0 && dealer.Count > 0) DealCard(dealerCards, dealer[0], true, DealerY, dealer.Count, DealerLastMaxX);
        else if (playerCards.Count == 1 && player.Count > 1) DealCard(playerCards, player[1], true, PlayerY, player.Count, PlayerLastMaxX);
        else if (dealerCards.Count == 1 && dealer.Count > 1) DealCard(dealerCards, dealer[1], false, DealerY, dealer.Count, DealerLastMaxX);
        else if (playerCards.Count < player.Count) DealCard(playerCards, player[playerCards.Count], true, PlayerY, player.Count, PlayerLastMaxX);
        else if (dealerCards.Count >= 2 && !dealerCards[1].FaceUp && !g.DealerHoleHidden) Reveal(dealerCards[1], dealer[1]);
        else if (dealerCards.Count < dealer.Count) DealCard(dealerCards, dealer[dealerCards.Count], true, DealerY, dealer.Count, DealerLastMaxX);
    }

    void DealCard(List<CasinoCard> row, int code, bool faceUp, float y, int total, float lastMaxX)
    {
        CasinoCard card = TakeCard();
        card.Set(code, faceUp);
        row.Add(card);
        Layout(row, y, Mathf.Max(total, row.Count), lastMaxX, card);
        moving = card;
        revealing = false;
        moveFrom = ShoePosition;
        moveTo = SlotPosition(row.Count - 1, y, Mathf.Max(total, row.Count), lastMaxX);
        moveStart = Time.unscaledTime;
        Place(card, moveFrom);
        card.transform.localScale = Vector3.one;
        card.transform.SetAsLastSibling();
        shownState = null;
        GameSfx.Play(Sfx.CardDeal);   // out of the shoe
    }

    void Reveal(CasinoCard card, int code)
    {
        moving = card;
        revealing = true;
        moveStart = Time.unscaledTime;
        card.Set(code, false);
        shownState = null;
        GameSfx.Play(Sfx.CardPlace);   // the hole card turns over
    }

    void AnimateMove()
    {
        if (moving == null) return;
        float t = Time.unscaledTime - moveStart;
        if (revealing)
        {
            // Turn over: squash the back, swap to the face, unsquash
            float u = Mathf.Clamp01(t / RevealSeconds);
            if (u >= 0.5f && !moving.FaceUp) moving.Set(moving.Code, true);
            float width = Mathf.Abs(Mathf.Cos(u * Mathf.PI));
            moving.transform.localScale = new Vector3(Mathf.Max(0.02f, width), 1f + 0.06f * Mathf.Sin(u * Mathf.PI), 1f);
            if (u >= 1f) FinishMove();
            return;
        }
        float k = CasinoUI.EaseOut(t / DealSeconds);
        Place(moving, Vector2.LerpUnclamped(moveFrom, moveTo, k));
        if (t >= DealSeconds) FinishMove();
    }

    void FinishMove()
    {
        if (moving == null) return;
        if (revealing)
        {
            if (!moving.FaceUp) moving.Set(moving.Code, true);
            moving.transform.localScale = Vector3.one;
        }
        else
        {
            Place(moving, moveTo);
        }
        moving = null;
        revealing = false;
        shownState = null;
    }

    Vector2 SlotPosition(int index, float y, int total, float lastMaxX)
    {
        float step = total > 1 ? Mathf.Min(CardStep, (lastMaxX - CardX) / (total - 1)) : CardStep;
        return new Vector2(CardX + index * step, y);
    }

    /// <summary>Put a row's cards in their places (all but <paramref name="except"/>, which is on its way).</summary>
    void Layout(List<CasinoCard> row, float y, int total, float lastMaxX, CasinoCard except)
    {
        for (int i = 0; i < row.Count; i++)
            if (row[i] != except) Place(row[i], SlotPosition(i, y, total, lastMaxX));
    }

    static void Place(CasinoCard card, Vector2 topLeft)
    {
        RectTransform rect = (RectTransform)card.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(topLeft.x + 75f, -(topLeft.y + 105f));
    }

    CasinoCard TakeCard()
    {
        CasinoCard card;
        if (spareCards.Count > 0)
        {
            card = spareCards[spareCards.Count - 1];
            spareCards.RemoveAt(spareCards.Count - 1);
        }
        else
        {
            card = CasinoUI.Card(cardLayer != null ? cardLayer : (RectTransform)transform, "Card", font, 0f, 0f);
        }
        card.gameObject.SetActive(true);
        return card;
    }

    void ClearTable()
    {
        foreach (CasinoCard card in dealerCards) Spare(card);
        foreach (CasinoCard card in playerCards) Spare(card);
        dealerCards.Clear();
        playerCards.Clear();
        moving = null;
        revealing = false;
        tableClear = true;
    }

    void Spare(CasinoCard card)
    {
        if (card == null) return;
        card.transform.localScale = Vector3.one;
        card.gameObject.SetActive(false);
        spareCards.Add(card);
    }

    /// <summary>What the cards on the table count (aces 11 or 1).</summary>
    static int Total(List<CasinoCard> cards, int count)
    {
        int value = 0, aces = 0;
        for (int i = 0; i < count && i < cards.Count; i++)
        {
            int code = cards[i].Code;
            if (code < 0) continue;
            value += CasinoBlackjack.CardValue(code);
            if (code % 13 == 0) aces++;
        }
        while (value > 21 && aces > 0)
        {
            value -= 10;
            aces--;
        }
        return value;
    }

    static string TotalText(int total)
    {
        string text = total.ToString(CultureInfo.InvariantCulture);
        return total > 21 ? L10n.Get(T, "casino.blackjack.total.bust", ("total", text)) : text;
    }

    // ------------------------------------------------------------------
    // Texts and buttons
    // ------------------------------------------------------------------

    protected override bool HoldValue => CatchingUp;

    protected override void LateUpdate()
    {
        base.LateUpdate();
        if (refusal != null && Time.unscaledTime > refusalUntil)
        {
            refusal = null;
            shownState = null;
        }
        Refresh();
    }

    public void Refresh()
    {
        CasinoManager casino = Manager;
        CasinoBlackjack g = Game;
        long bet = casino != null ? casino.GetCurrentBet() : 0;
        bool catching = CatchingUp;
        bool inHand = g != null && g.IsHandInProgress;
        bool result = g != null && !inHand && !tableClear && !catching && g.LastResult != CasinoHandResult.None;
        string state = (catching ? "C" : "-") + (inHand ? "H" : "-") + (result ? "R" : "-") + (tableClear ? "E" : "-") + bet + "/"
                       + dealerCards.Count + "/" + playerCards.Count + "/" + (dealerCards.Count > 1 && dealerCards[1].FaceUp) + "/"
                       + (moving != null) + "/" + refusal + "/" + (g != null ? g.CurrentBet + "/" + g.CanDoubleDown : "") + "/"
                       + (casino != null && casino.CanBetAtAll);
        if (state == shownState) return;
        shownState = state;

        // Totals: what the table shows so far
        bool anyCards = dealerCards.Count > 0 || playerCards.Count > 0;
        if (dealerTotalChip != null && dealerTotalChip.activeSelf != (dealerCards.Count > 0)) dealerTotalChip.SetActive(dealerCards.Count > 0);
        if (playerTotalChip != null && playerTotalChip.activeSelf != (playerCards.Count > 0)) playerTotalChip.SetActive(playerCards.Count > 0);
        if (dealerTotal != null && dealerCards.Count > 0)
        {
            bool holeDown = dealerCards.Count >= 2 && !dealerCards[1].FaceUp;
            int shown = moving != null && dealerCards.Contains(moving) && !revealing ? dealerCards.Count - 1 : dealerCards.Count;
            dealerTotal.text = holeDown
                ? CasinoBlackjack.CardValue(dealerCards[0].Code).ToString(CultureInfo.InvariantCulture) + " + ?"
                : TotalText(Total(dealerCards, Mathf.Max(1, shown)));
        }
        if (playerTotal != null && playerCards.Count > 0)
        {
            int shown = moving != null && playerCards.Contains(moving) ? playerCards.Count - 1 : playerCards.Count;
            playerTotal.text = TotalText(Total(playerCards, Mathf.Max(1, shown)));
        }
        if (emptySlots != null && emptySlots.activeSelf == anyCards) emptySlots.SetActive(!anyCards);

        // The chips on the table: the hand's stake, or the bet the next deal takes
        double stake = !tableClear && g != null && g.CurrentBet > 0 ? g.CurrentBet : bet;
        bool doubled = !tableClear && g != null && g.Doubled;
        CasinoUI.ChipColors(doubled ? stake * 0.5 : stake, out Color chip, out Color spots, out Color label);
        for (int i = 0; i < stackChips.Length; i++)
        {
            CasinoChip c = stackChips[i];
            if (c == null) continue;
            bool show = i < 2 || doubled;
            if (c.gameObject.activeSelf != show) c.gameObject.SetActive(show);
            c.ChipColor = chip;
            c.SpotColor = spots;
        }
        if (stackLabel != null)
        {
            stackLabel.text = CasinoUI.ChipLabel(doubled ? stake * 0.5 : stake);
            stackLabel.color = label;
            RectTransform top = stackChips.Length > 0 && stackChips[doubled && stackChips.Length > 2 ? 2 : 1] != null
                ? stackChips[doubled && stackChips.Length > 2 ? 2 : 1].rectTransform : null;
            if (top != null && stackLabel.transform.parent != top) stackLabel.transform.SetParent(top, false);
        }

        // Bet
        if (bet != shownBet)
        {
            shownBet = bet;
            FitBetChip(betChip, betText, betPlusButton != null ? betPlusButton.button : null, UIFormat.Money(bet), 300f);
        }
        bool canChangeBet = !inHand && !catching;
        if (betMinusButton != null) betMinusButton.Interactable = canChangeBet && casino != null && casino.CanLowerBet;
        if (betPlusButton != null) betPlusButton.Interactable = canChangeBet && casino != null && casino.CanRaiseBet;

        FillMessage(g, casino, result, inHand, catching);
        FillActions(g, casino, result, inHand, catching);
    }

    void FillMessage(CasinoBlackjack g, CasinoManager casino, bool result, bool inHand, bool catching)
    {
        bool slip = refusal != null || (!result && !inHand && !catching && tableClear);
        if (!slip && casino != null && !casino.CanBetAtAll && !inHand && !catching && !result) slip = true;
        bool banner = !slip && result;
        if (result && g != null && soundedHand != g.HandNumber)
        {
            // The table has caught up with the hand: win, push or loss, once per hand
            soundedHand = g.HandNumber;
            GameSfx.PlayResult(g.LastResult);
        }
        if (betSlip != null && betSlip.activeSelf != slip) betSlip.SetActive(slip);
        if (resultBanner != null && resultBanner.activeSelf != banner) resultBanner.SetActive(banner);

        if (slip && betSlipText != null)
        {
            string text;
            Color color = UIColors.Ink;
            // Refusals and "not enough money" are the game's and CasinoManager's own messages, shown as they come
            if (refusal != null)
            {
                text = refusal;
                color = UIColors.Loss;
            }
            else if (casino != null && !casino.CanBetAtAll)
            {
                text = casino.NotEnoughMessage;
                color = UIColors.Loss;
            }
            else
            {
                text = PressDealText();
            }
            betSlipText.text = text;
            betSlipText.color = color;
            FitSlip(text);
        }

        bool won = banner && g != null && (g.LastResult == CasinoHandResult.Win || g.LastResult == CasinoHandResult.Blackjack);
        foreach (GameObject sparkle in resultSparkles)
            if (sparkle != null && sparkle.activeSelf != won) sparkle.SetActive(won);
        if (banner && g != null) FillResult(g);
    }

    /// <summary>"Press DEAL to start!" with the button's name in bold.</summary>
    static string PressDealText()
    {
        return L10n.Get(T, "casino.blackjack.slip.press_deal", ("deal", "<b>" + L10n.Get(T, "casino.blackjack.slip.deal") + "</b>"));
    }

    /// <summary>The slip fits its message: 520 px (the board's), up to 840 px, centred on the table.</summary>
    void FitSlip(string text)
    {
        if (betSlip == null || betSlipText == null) return;
        RectTransform slip = (RectTransform)betSlip.transform;
        float width = Mathf.Ceil(RunEndUI.TextWidth(betSlipText.font, text, 32f)) + 4f + 32f + 14f + 48f;
        width = Mathf.Clamp(width, 520f, 840f);
        slip.sizeDelta = new Vector2(width, slip.sizeDelta.y);
        slip.anchoredPosition = new Vector2(960f - width * 0.5f, slip.anchoredPosition.y);
        RectTransform textRect = betSlipText.rectTransform;
        textRect.sizeDelta = new Vector2(width - 24f - 32f - 14f - 24f, textRect.sizeDelta.y);
    }

    void FillResult(CasinoBlackjack g)
    {
        string p = g.PlayerTotal.ToString(CultureInfo.InvariantCulture), d = g.DealerTotal.ToString(CultureInfo.InvariantCulture);
        double stake = g.CurrentBet;
        double net = g.LastPayout - stake;
        string title, amount, note;
        Color color;
        string bet = UIFormat.Money(stake);
        string doubled = g.Doubled ? ".doubled" : "";   // "... your $2,000 bet (doubled)"
        switch (g.LastResult)
        {
            case CasinoHandResult.Blackjack:
                title = L10n.Get(T, "casino.blackjack.result.blackjack");
                amount = UIFormat.SignedMoney(net);
                note = L10n.Get(T, "casino.blackjack.note.paid_3_2" + doubled, ("amount", bet));
                color = UIColors.Money;
                break;
            case CasinoHandResult.Win:
                title = g.DealerTotal > 21 ? L10n.Get(T, "casino.blackjack.result.dealer_bust", ("dealer", d))
                                           : L10n.Get(T, "casino.blackjack.result.win", ("player", p), ("dealer", d));
                amount = UIFormat.SignedMoney(net);
                note = L10n.Get(T, "casino.blackjack.note.paid_1_1" + doubled, ("amount", bet));
                color = UIColors.Money;
                break;
            case CasinoHandResult.Push:
                title = CasinoBlackjack.IsNatural(g.PlayerCards) && CasinoBlackjack.IsNatural(g.DealerCards)
                    ? L10n.Get(T, "casino.blackjack.result.push_blackjack")
                    : L10n.Get(T, "casino.blackjack.result.push", ("player", p), ("dealer", d));
                amount = UIFormat.Money(0.0);
                note = L10n.Get(T, "casino.blackjack.note.push" + doubled, ("amount", bet));
                color = UIColors.Info;
                break;
            default:
                if (g.PlayerTotal > 21) title = L10n.Get(T, "casino.blackjack.result.bust", ("player", p));
                else if (CasinoBlackjack.IsNatural(g.DealerCards)) title = L10n.Get(T, "casino.blackjack.result.dealer_blackjack");
                else title = L10n.Get(T, "casino.blackjack.result.dealer_wins", ("dealer", d), ("player", p));
                amount = UIFormat.SignedMoney(net);
                note = L10n.Get(T, "casino.blackjack.note.lost" + doubled, ("amount", bet));
                color = UIColors.Loss;
                break;
        }
        if (resultTitle != null)
        {
            resultTitle.text = title;
            resultTitle.color = color;
        }
        if (resultAmount != null)
        {
            resultAmount.text = amount;
            resultAmount.color = color;
        }
        if (resultNote != null) resultNote.text = note;
    }

    void FillActions(CasinoBlackjack g, CasinoManager casino, bool result, bool inHand, bool catching)
    {
        bool showResult = result;   // a refused "Deal again" keeps the result actions (the slip says why)
        if (playActions != null && playActions.activeSelf == showResult) playActions.SetActive(!showResult);
        if (resultActions != null && resultActions.activeSelf != showResult)
        {
            resultActions.SetActive(showResult);
            if (showResult) LayoutResultActions();
        }

        bool canBet = casino != null && casino.CanBetAtAll && g != null;
        if (showResult)
        {
            if (dealAgainButton != null) dealAgainButton.Interactable = canBet && !catching;
            return;
        }

        bool free = !catching;
        if (dealButton != null)
        {
            dealButton.Interactable = free && !inHand && canBet;
            dealButton.SetLook(CasinoButton.Look.Gold, false);
        }
        if (hitButton != null) hitButton.Interactable = free && inHand;
        if (standButton != null) standButton.Interactable = free && inHand;
        if (doubleButton != null)
        {
            bool offer = free && inHand && g.CanDoubleDown;
            bool affordable = casino != null && g != null && casino.CanAffordBet(g.DoubleDownCost);
            doubleButton.Interactable = offer && affordable;
            doubleButton.SetLook(CasinoButton.Look.Gold, offer && affordable);
            if (doubleNote != null)
            {
                long current = g != null && inHand ? g.CurrentBet : (casino != null ? casino.GetCurrentBet() : 0);
                doubleNote.text = inHand && g.CanDoubleDown && !affordable && casino != null
                    ? KeepHint(casino)
                    : L10n.Get(T, "casino.blackjack.button.double.note", ("amount", UIFormat.Money(current * 2)));
            }
        }
    }

    // ------------------------------------------------------------------
    // Runtime layout (board CasinoBlackjack, 1920 x 1080)
    // ------------------------------------------------------------------

    /// <summary>Build the blackjack screen under <paramref name="parent"/> (an inactive root).</summary>
    public static CasinoBlackjackView Build(RectTransform parent, TMP_FontAsset font)
    {
        RectTransform root = CasinoUI.FullRect(parent, "CasinoBlackjack");
        root.gameObject.SetActive(false);
        CasinoBlackjackView view = root.gameObject.AddComponent<CasinoBlackjackView>();
        view.font = font;
        CasinoUI.Felt(root, "Felt", true);
        RectTransform screen = CasinoUI.Stage(root);

        // The arcs printed on the felt
        TableArc(screen, 400f, 496f, 3f);
        TableArc(screen, 416f, 512f, 2f);

        L10nText.Attach(CasinoUI.Shrink(CasinoUI.Text(screen, "Title", "", 60f, UIColors.Gold, font, 660f, 84f, 600f, 69f, TextAlignmentOptions.Center, true), 36f),
                        T, "casino.blackjack.title");   // "Blackjack"

        // The shoe: three cards, the top one showing its back
        ShoeCard(screen, 1139f, 208f, true, false);
        ShoeCard(screen, 1133f, 202f, false, false);
        ShoeCard(screen, 1127f, 196f, false, true);

        // Rules placard, printed on the table
        RectTransform placard = CasinoUI.Rect(screen, "Rules", 540f, 426f, 840f, 44f);
        placard.pivot = new Vector2(0.5f, 0.5f);
        placard.anchoredPosition = new Vector2(540f + 420f, -(426f + 22f));
        placard.localEulerAngles = new Vector3(0f, 0f, 1f);
        BlueprintBox placardBox = BlueprintUI.Box(placard, "Box", UIColors.PaperHi, UIColors.Ink, 2f, CasinoUI.RadiusSmall);
        BlueprintUI.Stretch(placardBox.rectTransform);
        placardBox.SetShadow(UIColors.WithAlpha(CasinoUI.FeltShadow, 0.8f), new Vector2(2f, 3f));
        L10nText.Attach(CasinoUI.Shrink(CasinoUI.Text(placard, "Text", "", 22f, UIColors.Ink, font, 24f, 0f, 792f, 44f, TextAlignmentOptions.Center), 16f),
                        T, "casino.blackjack.rules");   // "Dealer draws to 17 · Blackjack pays 3:2 · Ties return your bet"

        // Dealer and you: label and total chip, right-aligned left of the cards
        view.dealerTotalChip = HandLabel(screen, "Dealer", "casino.blackjack.dealer", 240f, font, out view.dealerTotal);
        view.playerTotalChip = HandLabel(screen, "You", "casino.blackjack.you", 562f, font, out view.playerTotal);

        // The empty card places (before the first deal)
        RectTransform slots = CasinoUI.FullRect(screen, "EmptySlots");
        foreach (Vector2 at in new[] { new Vector2(526f, 180f), new Vector2(694f, 180f), new Vector2(526f, 502f), new Vector2(694f, 502f) })
        {
            BlueprintBox slot = CasinoUI.Box(slots, "Slot", UIColors.WithAlpha(UIColors.FeltDark, 0.35f), UIColors.WithAlpha(UIColors.Gold, 0.7f), 3f,
                                             BlueprintUI.Round(12f), at.x, at.y, 150f, 210f);
            slot.SetDash(true, 9f, 6f);
        }
        view.emptySlots = slots.gameObject;
        view.cardLayer = CasinoUI.FullRect(screen, "Cards");

        BuildBetArea(view, screen, font);
        BuildMessages(view, screen, font);
        BuildActions(view, screen, font);
        BuildGameHeader(view, screen, font, 48f, 36f);
        return view;
    }

    /// <summary>A printed arc across the felt: M360 y0 Q960 yMid 1560 y0.</summary>
    static void TableArc(RectTransform screen, float y0, float yMid, float width)
    {
        RectTransform rect = CasinoUI.FullRect(screen, "Arc");
        rect.pivot = new Vector2(0f, 1f);
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(1920f, 1080f);
        UIPolyline line = rect.gameObject.AddComponent<UIPolyline>();
        line.color = UIColors.WithAlpha(UIColors.Gold, 0.7f);
        line.width = width;
        var points = new List<Vector2>();
        for (int i = 0; i <= 48; i++)
        {
            float t = i / 48f, u = 1f - t;
            float x = u * u * 360f + 2f * u * t * 960f + t * t * 1560f;
            float y = u * u * y0 + 2f * u * t * yMid + t * t * y0;
            points.Add(new Vector2(x, -y));
        }
        line.AddPath(points.ToArray());
    }

    static void ShoeCard(RectTransform screen, float x, float y, bool shadow, bool back)
    {
        BlueprintBox card = CasinoUI.Box(screen, "ShoeCard", UIColors.PaperHi, UIColors.Ink, 2f, BlueprintUI.Round(10f), x, y, 120f, 168f);
        if (shadow) card.SetShadow(UIColors.WithAlpha(CasinoUI.FeltShadow, 0.8f), new Vector2(3f, 4f));
        if (back) CasinoUI.CardBack(card.transform, 9f, 5f);
    }

    /// <summary>"Dealer" / "You" and the total chip under it, right-aligned at x 502; returns the chip.</summary>
    static GameObject HandLabel(RectTransform screen, string label, string key, float top, TMP_FontAsset font, out TMP_Text total)
    {
        L10nText.Attach(CasinoUI.Shrink(CasinoUI.Text(screen, label + "Label", "", 26f, UIColors.PaperHi, font, 262f, top, 240f, 31f,
                                                      TextAlignmentOptions.MidlineRight), 16f), T, key);   // "Dealer" / "You"
        RectTransform chip = BlueprintUI.NewRect(label + "Total", screen);
        chip.anchorMin = chip.anchorMax = new Vector2(0f, 1f);
        chip.pivot = new Vector2(1f, 1f);
        chip.anchoredPosition = new Vector2(502f, -(top + 43f));
        BlueprintBox box = BlueprintUI.AddBox(chip, UIColors.PaperHi, UIColors.Ink, 2f, CasinoUI.RadiusSmall);
        BlueprintUI.HRow(box, 0f, new RectOffset(16, 16, 4, 4), TextAnchor.MiddleCenter);
        BlueprintUI.Fit(box, true, true);
        TextMeshProUGUI text = BlueprintUI.Text(chip, "Text", "0", 26f, UIColors.Ink, font, TextAlignmentOptions.Center, true);
        BlueprintUI.Layout(text, -1f, 36f, -1f, -1f, -1f, 36f);
        total = text;
        chip.gameObject.SetActive(false);
        return chip.gameObject;
    }

    static void BuildBetArea(CasinoBlackjackView view, RectTransform screen, TMP_FontAsset font)
    {
        // Betting circle with the chip stack
        RectTransform circle = CasinoUI.Rect(screen, "BettingCircle", 1112f, 522f, 150f, 150f);
        BlueprintBox ring = BlueprintUI.Box(circle, "Ring", Color.clear, UIColors.WithAlpha(UIColors.Gold, 0.85f), 3f, BlueprintUI.Round(75f));
        BlueprintUI.Stretch(ring.rectTransform);
        BlueprintBox dashes = BlueprintUI.Box(circle, "Dashes", Color.clear, UIColors.WithAlpha(UIColors.Gold, 0.55f), 2f, BlueprintUI.Round(63f));
        dashes.SetDash(true, 8f, 6f);
        BlueprintUI.Stretch(dashes.rectTransform, 12f, 12f, 12f, 12f);
        for (int i = 0; i < 3; i++)
        {
            CasinoChip chip = CasinoUI.Chip(circle, "Chip" + i, 76f, CasinoUI.ChipBlue, UIColors.PaperHi, true, 75f, 81f - i * 12f, null,
                                            Color.clear, font, out _);
            if (i == 0) chip.SetShadow(CasinoUI.FeltShadow, 4f);
            view.stackChips[i] = chip;
        }
        view.stackChips[2].gameObject.SetActive(false);
        TextMeshProUGUI label = BlueprintUI.Text(view.stackChips[1].rectTransform, "Label", "$500", 17f, UIColors.PaperHi, font,
                                                 TextAlignmentOptions.Center, true);
        BlueprintUI.Stretch(label.rectTransform);
        label.rectTransform.sizeDelta = new Vector2(-32f, 0f);
        label.enableAutoSizing = true;
        label.fontSizeMax = 17f;
        label.fontSizeMin = 10f;
        view.stackLabel = label;

        // Bet chip with - and +, and where the money comes from
        view.betChip = BuildBetStepper(screen, font, 1288f, 554f, false, out view.betMinusButton, out view.betPlusButton, out TextMeshProUGUI bet);
        view.betText = bet;
        L10nText.Attach(CasinoUI.Shrink(CasinoUI.Text(screen, "FromCompanyValue", "", 19f, UIColors.PaperHi, font, 1342f, 614f, 300f, 26f), 13f),
                        T, "casino.blackjack.from_value");   // "from company value"
    }

    static void BuildMessages(CasinoBlackjackView view, RectTransform screen, TMP_FontAsset font)
    {
        // The bet slip: "Press DEAL to start!" (or why a deal was refused)
        BlueprintBox slip = CasinoUI.Box(screen, "BetSlip", UIColors.PaperHi, UIColors.Ink, 2f, CasinoUI.RadiusLarge, 700f, 770f, 520f, 84f);
        slip.SetShadow(CasinoUI.FeltShadow, new Vector2(3f, 4f));
        CasinoIcon.Create(slip.transform, CasinoIconKind.Cards, 32f, UIColors.Ink, 24f + 16f, 42f);
        view.betSlipText = CasinoUI.Shrink(CasinoUI.Text(slip.transform, "Text", PressDealText(), 32f, UIColors.Ink, font,
                                                         24f + 32f + 14f, 0f, 520f - 94f, 84f, TextAlignmentOptions.MidlineLeft), 16f);
        view.betSlip = slip.gameObject;

        // The result banner, with its two sparkles
        BlueprintBox banner = CasinoUI.Box(screen, "ResultBanner", UIColors.PaperHi, UIColors.Ink, 3f, CasinoUI.RadiusLarge, 560f, 744f, 800f, 136f);
        banner.SetShadow(CasinoUI.SheetShadow, new Vector2(0f, 22f), 48f);
        view.resultTitle = CasinoUI.Shrink(CasinoUI.Text(banner.transform, "Title", "", 44f, UIColors.Money, font, 36f, 19f, 728f, 52f,
                                                         TextAlignmentOptions.Center, true), 20f);
        RectTransform row = CasinoUI.Rect(banner.transform, "Row", 36f, 79f, 728f, 38f);
        BlueprintUI.HRow(row, 20f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleCenter);
        view.resultAmount = BlueprintUI.Text(row, "Amount", "+$0", 32f, UIColors.Money, font, TextAlignmentOptions.Center, true);
        BlueprintBox divider = BlueprintUI.Box(row, "Divider", UIColors.WithAlpha(UIColors.Ink, 0.3f), Color.clear, 0f, Vector4.zero);
        BlueprintUI.Layout(divider, 2f, 30f, 0f, 0f, 2f, 30f);
        view.resultNote = BlueprintUI.Text(row, "Note", "", 22f, UIColors.InkSoft, font, TextAlignmentOptions.Center);
        view.resultBanner = banner.gameObject;
        CasinoIcon left = CasinoUI.Sparkle(screen, 488f, 752f, 48f);
        CasinoIcon right = CasinoUI.Sparkle(screen, 1388f, 820f, 40f);
        view.resultSparkles = new[] { left.gameObject, right.gameObject };
        left.gameObject.SetActive(false);
        right.gameObject.SetActive(false);
        banner.gameObject.SetActive(false);
    }

    static void BuildActions(CasinoBlackjackView view, RectTransform screen, TMP_FontAsset font)
    {
        // Deal, Hit, Stand, Double Down
        RectTransform play = CasinoUI.Rect(screen, "PlayActions", 428f, 912f, 1064f, 80f);
        view.playActions = play.gameObject;
        view.dealButton = ActionButton(play, "Deal", "casino.blackjack.button.deal", CasinoIconKind.Cards, CasinoButton.Look.Gold, true, 0f,
                                       new Vector2(5f, 6f), font);
        view.hitButton = ActionButton(play, "Hit", "casino.blackjack.button.hit", CasinoIconKind.Plus, CasinoButton.Look.Gold, true, 224f,
                                      new Vector2(5f, 6f), font);
        view.standButton = ActionButton(play, "Stand", "casino.blackjack.button.stand", CasinoIconKind.None, CasinoButton.Look.Paper, false, 448f,
                                        new Vector2(4f, 5f), font);
        view.doubleButton = DoubleDownButton(play, font, 672f, 0f, out view.doubleNote);

        // Deal again, Back to casino (both fit their words: LayoutResultActions centres the pair again when they change)
        RectTransform after = CasinoUI.Rect(screen, "ResultActions", 0f, 912f, 1920f, 80f);
        view.resultActions = after.gameObject;
        view.dealAgainButton = CasinoUI.Button(after, "DealAgain", L10n.Get(T, "casino.blackjack.button.deal_again"), CasinoIconKind.Cards,
                                               CasinoButton.Look.Highlight, font, 32f, true, 0f, 0f, 0f, 80f, new Vector2(5f, 6f), UIColors.Ink,
                                               36f, 28f, 220f, 560f);
        L10nText.Attach(view.dealAgainButton.label, T, "casino.blackjack.button.deal_again");   // "Deal again"
        view.resultBackButton = CasinoUI.LinkButton(after, "BackToCasino", L10n.Get(T, "casino.button.back"), font, 0f, 14f, 52f, UIColors.PaperHi,
                                                    out TextMeshProUGUI backLabel);
        L10nText.Attach(backLabel, T, "casino.button.back");   // "Back to casino"
        view.LayoutResultActions();
        after.gameObject.SetActive(false);
    }

    /// <summary>A 200 x 80 action button whose label stays on its String Table entry.</summary>
    static CasinoButton ActionButton(RectTransform parent, string name, string key, CasinoIconKind icon, CasinoButton.Look look, bool bold, float x,
                                     Vector2 shadowOffset, TMP_FontAsset font)
    {
        CasinoButton button = CasinoUI.Button(parent, name, L10n.Get(T, key), icon, look, font, 32f, bold, x, 0f, 200f, 80f, shadowOffset,
                                              CasinoUI.FeltShadow, 24f);
        L10nText.Attach(button.label, T, key);
        return button;
    }

    /// <summary>
    /// Deal again and the Back to casino link, fitted to their words as they read now and centred together (after a
    /// language change they may be longer or shorter).
    /// </summary>
    void LayoutResultActions()
    {
        if (dealAgainButton == null || resultBackButton == null) return;
        dealAgainButton.FitLabel();
        CasinoUI.FitLink(resultBackButton);
        RectTransform dealAgain = (RectTransform)dealAgainButton.transform;
        RectTransform back = (RectTransform)resultBackButton.transform;
        float total = dealAgain.sizeDelta.x + 48f + back.sizeDelta.x;
        dealAgain.anchoredPosition = new Vector2(960f - total * 0.5f, 0f);
        back.anchoredPosition = new Vector2(960f - total * 0.5f + dealAgain.sizeDelta.x + 48f, -14f);
    }

    /// <summary>Double Down (392 x 80): a coin, "Double Down" and the note under it ("bet $2,000 · one more card").</summary>
    static CasinoButton DoubleDownButton(RectTransform parent, TMP_FontAsset font, float x, float y, out TMP_Text note)
    {
        RectTransform root = CasinoUI.Rect(parent, "DoubleDown", x, y, 392f, 80f);
        Image hit = root.gameObject.AddComponent<Image>();
        hit.color = Color.clear;
        hit.raycastTarget = true;
        Button button = root.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = hit;
        button.navigation = new Navigation { mode = Navigation.Mode.None };

        BlueprintBox shadow = BlueprintUI.Box(root, "Shadow", CasinoUI.FeltShadow, Color.clear, 0f, CasinoUI.RadiusLarge);
        BlueprintUI.Stretch(shadow.rectTransform);
        BlueprintBox face = BlueprintUI.Box(root, "Face", UIColors.Gold, UIColors.Ink, 3f, CasinoUI.RadiusLarge);
        BlueprintUI.Stretch(face.rectTransform);
        BlueprintUI.HRow(face, 12f, new RectOffset(20, 20, 0, 0), TextAnchor.MiddleCenter);
        CasinoIcon coin = CasinoUI.RowIcon(face.transform, CasinoIconKind.Coin, 28f, UIColors.Ink);
        RectTransform column = BlueprintUI.NewRect("Text", face.transform);
        VerticalLayoutGroup stack = BlueprintUI.VColumn(column, 2f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft);
        stack.childForceExpandWidth = false;
        TextMeshProUGUI title = BlueprintUI.Text(column, "Title", "", 32f, UIColors.Ink, font, TextAlignmentOptions.Left, true);
        BlueprintUI.Layout(title, -1f, 36f, -1f, -1f, -1f, 36f);
        L10nText.Attach(title, T, "casino.blackjack.button.double");   // "Double Down"
        // The screen writes the note (the doubled bet)
        TextMeshProUGUI small = BlueprintUI.Text(column, "Note", L10n.Get(T, "casino.blackjack.button.double.note", ("amount", "$2,000")), 19f,
                                                 UIColors.Ink, font, TextAlignmentOptions.Left);
        BlueprintUI.Layout(small, 300f, 24f, -1f, -1f, -1f, 24f);
        small.enableAutoSizing = true;
        small.fontSizeMax = 19f;
        small.fontSizeMin = 12f;
        note = small;

        MarkerButton marker = root.gameObject.AddComponent<MarkerButton>();
        marker.face = face.rectTransform;
        marker.pressOffset = new Vector2(5f, -6f);

        CasinoButton casino = root.gameObject.AddComponent<CasinoButton>();
        casino.button = button;
        casino.face = face;
        casino.shadow = shadow;
        casino.content = new Graphic[] { coin, title, small };
        casino.shadowColor = CasinoUI.FeltShadow;
        casino.shadowOffset = new Vector2(5f, 6f);
        casino.SetLook(CasinoButton.Look.Gold, true);
        return casino;
    }
}
