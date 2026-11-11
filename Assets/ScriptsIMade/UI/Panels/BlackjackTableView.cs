using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The designed blackjack table (felt, wood rail): draws BlackjackGame's hands as real cards, the totals
/// ("10 + ?" while the dealer's second card is down), what is at stake, and the outcome on the table placard (texts:
/// Panels table). BlackjackGame keeps the rules; its Hit / Stand / Close buttons are the table's buttons (Close shows as Continue).
/// </summary>
[DisallowMultipleComponent]
public class BlackjackTableView : MonoBehaviour
{
    public BlackjackGame game;
    public UpgradeFlowView flow;

    [Header("Cards")]
    public PlayingCardView cardTemplate;
    public RectTransform dealerRow;
    public RectTransform playerRow;
    [Tooltip("Left edge of the first card and the furthest left edge of the last card, in the row's coordinates")]
    public float firstCardX = 0f;
    public float dealerLastMaxX = 266f;
    public float playerLastMaxX = 218f;
    public float cardStep = 168f;

    [Header("Texts")]
    public TMP_Text dealerTotal;
    public TMP_Text playerTotal;
    public TMP_Text stakeText;
    public Image stakeIcon;
    [Tooltip("Caps the stake chip's text width (longer stakes wrap)")]
    public LayoutElement stakeWidth;
    public float maxStakeTextWidth = 680f;
    public TMP_Text placard;

    [Header("Buttons")]
    public GameObject playButtons;
    public GameObject continueButton;

    /// <summary>The placard's rules line in English (the builder's text and previews); the table shows upgrade.table.rules.</summary>
    public const string RulesLine = "Dealer stands on 17 · Ties refund the stake";

    const string L10nTable = "Panels";

    readonly List<PlayingCardView> dealerCards = new List<PlayingCardView>();
    readonly List<PlayingCardView> playerCards = new List<PlayingCardView>();
    BlackjackGame subscribed;
    string signature;

    BlackjackGame Game => game != null ? game : BlackjackGame.Instance;

    void Awake()
    {
        if (flow == null) flow = GetComponentInParent<UpgradeFlowView>(true);
        L10nFont.AttachAll(this);   // each language's font (English keeps the design font); cards copy it from the template
    }

    void OnEnable()
    {
        L10n.LanguageChanged += Refresh;
        signature = null;
        Refresh();
    }

    void OnDisable()
    {
        L10n.LanguageChanged -= Refresh;
        if (subscribed != null) subscribed.Changed -= Refresh;
        subscribed = null;
    }

    void LateUpdate()
    {
        var g = Game;
        if (g != subscribed)
        {
            if (subscribed != null) subscribed.Changed -= Refresh;
            subscribed = g;
            if (g != null) g.Changed += Refresh;
        }
        if (g == null) return;
        string sig = g.PlayerCards.Count + "/" + g.DealerCards.Count + "/" + g.DealerHoleHidden + "/" + g.IsHandInProgress;
        if (sig != signature) Refresh();
    }

    public void Refresh()
    {
        var g = Game;
        if (g == null) return;
        signature = g.PlayerCards.Count + "/" + g.DealerCards.Count + "/" + g.DealerHoleHidden + "/" + g.IsHandInProgress;

        bool finished = !g.IsHandInProgress && g.IsOpen;
        string dealerText = g.DealerHoleHidden
            ? g.DealerVisibleTotal.ToString(CultureInfo.InvariantCulture) + " + ?"
            : TotalText(g.DealerTotal);
        string message = finished ? Outcome(g) : L10n.Get(L10nTable, "upgrade.table.rules");
        Color messageColor = !finished ? UIColors.Ink
            : g.LastResult == BlackjackResult.Win ? UIColors.Money
            : g.LastResult == BlackjackResult.Push ? UIColors.Info : UIColors.Loss;

        Show(g.DealerCards, g.DealerHoleHidden, g.PlayerCards, dealerText, TotalText(g.PlayerTotal), finished, message, messageColor);

        var up = OfficeUpgradeManager.Instance;
        if (up != null && up.IsBusy && up.CurrentItem != null && up.CurrentItem.itemData != null && up.PendingCharge > 0)
        {
            OfficeItem item = up.CurrentItem.itemData;
            SetStake(L10n.Get(L10nTable, "upgrade.table.stake.upgrade", ("item", item.DisplayName), ("amount", "<b>" + UIFormat.Money(up.PendingCharge) + "</b>")),
                flow != null ? flow.IconFor(item.itemName) : null);
        }
        else
        {
            // the stake arrives written by whoever started the hand (already in the player's language)
            SetStake(string.IsNullOrEmpty(g.CurrentStake) ? L10n.Get(L10nTable, "upgrade.table.stake.none")
                                                          : L10n.Get(L10nTable, "upgrade.table.stake", ("stake", g.CurrentStake)),
                flow != null ? flow.stakeIcon : null);
        }
    }

    /// <summary>The stake chip: its text (wrapping past maxStakeTextWidth) and icon.</summary>
    public void SetStake(string text, Sprite icon)
    {
        if (stakeText != null)
        {
            stakeText.text = text;
            if (stakeWidth != null)
                stakeWidth.preferredWidth = Mathf.Min(maxStakeTextWidth, stakeText.GetPreferredValues(text, 10000f, 1000f).x + 1f);
        }
        if (stakeIcon != null && icon != null) stakeIcon.sprite = icon;
    }

    static string TotalText(int total)
    {
        string t = total.ToString(CultureInfo.InvariantCulture);
        return total > 21 ? L10n.Get(L10nTable, "upgrade.table.total.bust", ("total", t)) : t;
    }

    static string Outcome(BlackjackGame g)
    {
        int p = g.PlayerTotal, d = g.DealerTotal;
        var player = ("player", (object)UpgradeFlowView.Int(p));
        var dealer = ("dealer", (object)UpgradeFlowView.Int(d));
        switch (g.LastResult)
        {
            case BlackjackResult.Win:
                if (p == 21 && g.PlayerCards.Count == 2) return L10n.Get(L10nTable, "upgrade.table.result.blackjack");
                if (d > 21) return L10n.Get(L10nTable, "upgrade.table.result.dealer_bust", dealer);
                return L10n.Get(L10nTable, "upgrade.table.result.win", player, dealer);
            case BlackjackResult.Push:
                return L10n.Get(L10nTable, "upgrade.table.result.push", player, dealer);
            default:
                if (p > 21) return L10n.Get(L10nTable, "upgrade.table.result.bust", player);
                return L10n.Get(L10nTable, "upgrade.table.result.dealer_wins", dealer, player);
        }
    }

    /// <summary>Lay out both hands (also used for previews).</summary>
    public void Show(IReadOnlyList<int> dealer, bool hideHole, IReadOnlyList<int> player, string dealerTotalText,
        string playerTotalText, bool finished, string message, Color messageColor)
    {
        Lay(dealerCards, dealerRow, dealer, hideHole, dealerLastMaxX);
        Lay(playerCards, playerRow, player, false, playerLastMaxX);
        if (dealerTotal != null) dealerTotal.text = dealerTotalText;
        if (playerTotal != null) playerTotal.text = playerTotalText;
        if (placard != null)
        {
            placard.text = message;
            placard.color = messageColor;
            placard.fontStyle = finished ? TMPro.FontStyles.Bold : TMPro.FontStyles.Normal;
        }
        if (playButtons != null && playButtons.activeSelf == finished) playButtons.SetActive(!finished);
        if (continueButton != null && continueButton.activeSelf != finished) continueButton.SetActive(finished);
    }

    void Lay(List<PlayingCardView> pool, RectTransform row, IReadOnlyList<int> cards, bool hideSecond, float lastMaxX)
    {
        if (row == null || cardTemplate == null) return;
        int n = cards != null ? cards.Count : 0;
        while (pool.Count < n)
        {
            var c = Instantiate(cardTemplate, row);
            c.name = "Card" + pool.Count;
            pool.Add(c);
        }
        float step = n > 1 ? Mathf.Min(cardStep, (lastMaxX - firstCardX) / (n - 1)) : cardStep;
        for (int i = 0; i < pool.Count; i++)
        {
            var c = pool[i];
            bool used = i < n;
            if (c.gameObject.activeSelf != used) c.gameObject.SetActive(used);
            if (!used) continue;
            var rt = (RectTransform)c.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(firstCardX + i * step, 0f);
            rt.SetSiblingIndex(i);
            c.Set(cards[i], !(hideSecond && i == 1));
        }
    }
}
