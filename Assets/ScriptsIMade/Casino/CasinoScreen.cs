using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// What every casino screen shares (hub, slots, coin flip, blackjack): the company value chip (live, but held while an
/// outcome is still being animated, so it doesn't give the outcome away), the "The company keeps $1,000" hint (in
/// highlighter yellow while that rule caps the bet), the back link and close button (off while a round is being played)
/// and the font. The runtime builders fill the references; a designed prefab can fill them instead.
/// Texts come from the "Casino" String Table. When the language changes, each screen writes its texts again
/// (OnLanguageRefresh) and the pieces laid out by a text's width are laid out again on the next LateUpdate (FitLayout),
/// once the language's font is on.
/// </summary>
public abstract class CasinoScreen : MonoBehaviour
{
    protected const string T = CasinoUI.Table;

    [Tooltip("Empty = CasinoManager.Instance")]
    public CasinoManager manager;
    [Tooltip("Font of the texts this screen writes (empty = the Coming Soon font the HUD uses)")]
    public TMP_FontAsset font;

    [Header("Header")]
    public TMP_Text companyValueText;
    [Tooltip("\"The company keeps $1,000\"")]
    public TMP_Text keepHintText;
    [Tooltip("Back to the hub (the hub: leave the casino)")]
    public Button backButton;
    [Tooltip("The round X button: back to the hub (the hub: close the casino)")]
    public Button closeButton;

    double shownValue = double.NaN;
    string shownHint;
    bool wired;
    bool layoutDirty = true;

    protected CasinoManager Manager => manager != null ? manager : CasinoManager.Instance;

    /// <summary>An outcome is still being animated: no leaving yet (CasinoManager.IsRoundInProgress asks this).</summary>
    public virtual bool IsBusy => false;

    /// <summary>Keep showing the company value from before (the outcome is still being animated).</summary>
    protected virtual bool HoldValue => IsBusy;

    protected virtual void Awake()
    {
        Wire();
    }

    void Wire()
    {
        if (wired) return;
        wired = true;
        if (backButton != null) backButton.onClick.AddListener(OnBack);
        if (closeButton != null) closeButton.onClick.AddListener(OnClose);
        OnWire();
    }

    /// <summary>Hook up this screen's own buttons (once, on Awake).</summary>
    protected virtual void OnWire()
    {
    }

    protected virtual void OnEnable()
    {
        shownValue = double.NaN;
        shownHint = null;
        layoutDirty = true;   // the language (or its font) may have changed while this screen was hidden
        L10n.LanguageChanged += OnLanguageChanged;
    }

    protected virtual void OnDisable()
    {
        L10n.LanguageChanged -= OnLanguageChanged;
    }

    void OnLanguageChanged()
    {
        shownHint = null;
        layoutDirty = true;
        OnLanguageRefresh();
    }

    /// <summary>The language changed while this screen is shown: write its texts again (the fonts switch by themselves).</summary>
    protected virtual void OnLanguageRefresh()
    {
    }

    protected virtual void LateUpdate()
    {
        if (layoutDirty)
        {
            layoutDirty = false;
            FitLayout();
        }
        RefreshHeader();
    }

    /// <summary>
    /// Lay out again what follows the width of a text (the value chip, the back link), for the language and font on now.
    /// Screens with more of it extend this.
    /// </summary>
    protected virtual void FitLayout()
    {
        CasinoUI.FitValueChip(companyValueText);
        if (companyValueText != null) OnValueShown();
        CasinoUI.FitLink(backButton);
    }

    /// <summary>The company value, the keep hint and the back / close buttons.</summary>
    protected void RefreshHeader()
    {
        CasinoManager casino = Manager;
        if (companyValueText != null && !HoldValue)
        {
            double value = Math.Floor(CasinoManager.BalanceNow());   // Cash Grab: the chip shows Player Cash (the bankroll)
            if (value != shownValue)
            {
                shownValue = value;
                companyValueText.text = UIFormat.Money(value);
                OnValueShown();
            }
        }

        if (keepHintText != null && casino != null)
        {
            bool canBet = casino.CanBetAtAll;
            string hint = KeepHint(casino, canBet);
            if (DemoMode.Active && casino.IsDemoShareCapped) hint = casino.DemoMaxNote;   // Demo: says why MAX is half the company
            bool capped = !canBet || casino.IsMaxBet;
            string key = hint + (capped ? "!" : "");
            if (key != shownHint)
            {
                shownHint = key;
                keepHintText.text = hint;
                keepHintText.color = capped ? UIColors.Highlight : UIColors.WithAlpha(UIColors.PaperHi, 0.85f);
            }
        }

        bool free = casino == null || !casino.IsRoundInProgress();
        if (backButton != null) CasinoUI.SetEnabled(backButton, free);
        if (closeButton != null) CasinoUI.SetEnabled(closeButton, free);
    }

    /// <summary>
    /// "The company keeps $1,000" (": nothing to bet" when the company has nothing above that), in the current language.
    /// </summary>
    protected static string KeepHint(CasinoManager casino, bool canBet = true)
    {
        // Cash Grab: bets come out of Player Cash (no company reserve)
        if (PlayerWallet.Active) return CashText.Get(canBet ? "cash.casino.hint" : "cash.casino.hint.nothing");
        string amount = UIFormat.Money(casino != null ? casino.KeepInCompany : 0.0);
        return L10n.Get(T, canBet ? "casino.keep_hint" : "casino.keep_hint.nothing", ("amount", amount));
    }

    /// <summary>The value chip shows a new value (e.g. to resize it).</summary>
    protected virtual void OnValueShown()
    {
    }

    protected virtual void OnBack()
    {
        CasinoManager casino = Manager;
        if (casino != null) casino.BackToHub();
    }

    protected virtual void OnClose()
    {
        OnBack();
    }

    /// <summary>
    /// The game screens' header (boards Slots, CoinFlip, CasinoBlackjack): the "Back to casino" link at (backX, backY)
    /// (none when backX is below 0), the company value chip with the keep hint under it, and the round X (back to the hub).
    /// </summary>
    protected static void BuildGameHeader(CasinoScreen screen, RectTransform root, TMP_FontAsset font, float backX, float backY)
    {
        if (backX >= 0f)
        {
            screen.backButton = CasinoUI.LinkButton(root, "BackToCasino", L10n.Get(T, "casino.button.back"), font, backX, backY, 52f, UIColors.PaperHi,
                                                    out TextMeshProUGUI back);
            L10nText.Attach(back, T, "casino.button.back");   // "Back to casino" (FitLayout re-fits the link when it changes)
        }
        screen.companyValueText = CasinoUI.ValueChip(root, font, 1432f, 38f, 380f, 52f);
        // The screen writes the hint (the reserve from CasinoManager)
        screen.keepHintText = CasinoUI.Shrink(CasinoUI.Text(root, "KeepHint", "", 19f,
                                                            UIColors.WithAlpha(UIColors.PaperHi, 0.85f), font, 1300f, 98f, 512f, 26f,
                                                            TextAlignmentOptions.MidlineRight), 14f);
        screen.closeButton = CasinoUI.CloseButton(root, "Close", 1828f, 34f);
    }

    /// <summary>
    /// The bet chip of the coin flip and blackjack boards, with - and + around it (the casino's shared bet; MAX is on the
    /// hub): a paper chip (2 px ink, felt shadow) with an optional small poker chip, "Bet" and the amount. Top-left at
    /// (x, y), 52 px tall; returns the chip rect (its width fits the amount, see FitBetChip).
    /// </summary>
    protected static RectTransform BuildBetStepper(Transform parent, TMP_FontAsset font, float x, float y, bool pokerChip,
                                                   out CasinoButton minus, out CasinoButton plus, out TextMeshProUGUI amount)
    {
        minus = CasinoUI.IconButton(parent, "BetMinus", CasinoIconKind.Minus, x, y + 4f, 44f, new Vector2(3f, 4f), UIColors.Ink, 24f);
        BlueprintBox chip = CasinoUI.Box(parent, "Bet", UIColors.PaperHi, UIColors.Ink, 2f, CasinoUI.RadiusSmall, x + 54f, y, 200f, 52f);
        chip.SetShadow(CasinoUI.FeltShadow, new Vector2(3f, 4f));
        float cursor = 16f;
        if (pokerChip)
        {
            CasinoChip small = CasinoUI.Chip(chip.transform, "Chip", 30f, CasinoUI.ChipBlack, UIColors.PaperHi, false, cursor + 15f, 26f, null,
                                             Color.clear, font, out _);
            small.name = "Chip";
            cursor += 40f;
        }
        string word = L10n.Get(T, "casino.bet.label");   // "Bet"
        float betWidth = Mathf.Ceil(CasinoUI.TextWidth(font, word, 19f)) + 2f;
        TextMeshProUGUI label = CasinoUI.Text(chip.transform, "Label", word, 19f, UIColors.InkSoft, font, cursor, 0f, betWidth, 52f);
        L10nText.Attach(label, T, "casino.bet.label");
        amount = CasinoUI.Text(chip.transform, "Amount", "$0", 26f, UIColors.Ink, font, cursor + betWidth + 10f, 0f, 120f, 52f,
                               TextAlignmentOptions.MidlineLeft, true);
        plus = CasinoUI.IconButton(parent, "BetPlus", CasinoIconKind.Plus, x + 264f, y + 4f, 44f, new Vector2(3f, 4f), UIColors.Ink, 24f);
        return chip.rectTransform;
    }

    /// <summary>
    /// Lay a bet chip (BuildBetStepper) out again for its "Bet" word as it reads now (after a language change): the
    /// amount follows the word; then the chip fits the amount again (FitBetChip).
    /// </summary>
    protected static void RefitBetChip(RectTransform chip, TMP_Text amount, Button plus, float maxWidth)
    {
        if (chip == null || amount == null) return;
        Transform labelTransform = chip.Find("Label");
        TMP_Text label = labelTransform != null ? labelTransform.GetComponent<TMP_Text>() : null;
        if (label != null)
        {
            RectTransform labelRect = label.rectTransform;
            float betWidth = Mathf.Ceil(CasinoUI.TextWidth(label.font, label.text, 19f)) + 2f;
            labelRect.sizeDelta = new Vector2(betWidth, labelRect.sizeDelta.y);
            RectTransform amountRect = amount.rectTransform;
            amountRect.anchoredPosition = new Vector2(labelRect.anchoredPosition.x + betWidth + 10f, amountRect.anchoredPosition.y);
        }
        FitBetChip(chip, amount, plus, amount.text, maxWidth);
    }

    /// <summary>Show <paramref name="text"/> in a bet chip built by BuildBetStepper, resizing it (and moving its +) to fit.</summary>
    protected static void FitBetChip(RectTransform chip, TMP_Text amount, Button plus, string text, float maxWidth)
    {
        if (amount == null) return;
        amount.text = text;
        if (chip == null) return;
        RectTransform amountRect = amount.rectTransform;
        float left = amountRect.anchoredPosition.x;
        float width = Mathf.Ceil(RunEndUI.TextWidth(amount.font, text, 26f, true)) + 4f;
        float chipWidth = Mathf.Min(maxWidth, left + width + 16f);
        amountRect.sizeDelta = new Vector2(Mathf.Max(20f, chipWidth - left - 12f), amountRect.sizeDelta.y);
        chip.sizeDelta = new Vector2(chipWidth, chip.sizeDelta.y);
        if (plus != null)
        {
            RectTransform plusRect = (RectTransform)plus.transform;
            plusRect.anchoredPosition = new Vector2(chip.anchoredPosition.x + chipWidth + 10f, plusRect.anchoredPosition.y);
        }
    }
}
