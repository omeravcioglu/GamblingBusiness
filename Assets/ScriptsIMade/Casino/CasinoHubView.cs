using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The casino hub (design board CasinoHub): the felt table over the paused office, the company value, the shared bet
/// (the $100 / $500 / $1K / $5K / MAX chips, the picked one raised in a highlighter ring, or a custom amount typed in),
/// "The company keeps $1,000", the three game cards (Play opens the game's screen) and Leave casino / the X.
/// Drives CasinoManager (SetBet, SetMaxBet, CommitTypedBet, OpenSlots...). Built at runtime by Build unless a designed
/// hub fills the references.
/// </summary>
public class CasinoHubView : CasinoScreen
{
    static readonly long[] ChipValues = { 100, 500, 1000, 5000 };

    [Header("Bet")]
    [Tooltip("$100, $500, $1K, $5K and MAX")]
    public Button[] chipButtons = new Button[5];
    [Tooltip("The chips' pictures (raised and ringed while picked, dimmed while the company can't cover them)")]
    public CasinoChip[] chipGraphics = new CasinoChip[5];
    [Tooltip("The chips' labels (they rise with a picked chip)")]
    public RectTransform[] chipLabels = new RectTransform[5];
    public TMP_InputField customAmountInput;

    [Header("Games")]
    public Button playSlotsButton;
    public Button playBlackjackButton;
    public Button playCoinFlipButton;

    [Header("Value chip (optional: fits the value)")]
    public RectTransform valueChip;

    [Header("Demo (built only in the demo)")]
    [Tooltip("Demo: the paper chip under the bet row (it fits its text)")]
    public RectTransform demoHeatChip;
    [Tooltip("Demo: \"HEAT 12 · THIS BET +1.7\", or the freeze line")]
    public TMP_Text demoHeatText;

    // Set by Build: the runtime bet row, laid out again by the width of its words when the language changes
    internal RectTransform runtimeBetRow;

    long shownBet = -1;
    long shownMax = -1;
    bool chipsShown;

    // Demo: the heat line (refreshed on CasinoManager.Changed and every DemoHeatSeconds of real time)
    const float DemoHeatSeconds = 0.25f;
    CasinoManager demoSubscribed;
    float demoHeatNext;
    bool demoHeatDirty = true;
    bool demoShownLocked;
    int demoShownHeat = -1, demoShownAdd = -1, demoShownLevel = -1, demoShownRelease = -1;
    // Demo: the half-company note shown in place of the keep hint while it caps the bet
    string demoNote;
    long demoNoteMax = -1;
    bool demoNoteShown;

    /// <summary>The custom amount is being typed.</summary>
    public bool IsEditingBet => customAmountInput != null && customAmountInput.isFocused;

    protected override void OnWire()
    {
        for (int i = 0; i < chipButtons.Length; i++)
        {
            Button button = chipButtons[i];
            if (button == null) continue;
            int index = i;
            button.onClick.AddListener(() => PickChip(index));
        }
        if (playSlotsButton != null) playSlotsButton.onClick.AddListener(() => { if (Manager != null) Manager.OpenSlots(); });
        if (playBlackjackButton != null) playBlackjackButton.onClick.AddListener(() => { if (Manager != null) Manager.OpenBlackjack(); });
        if (playCoinFlipButton != null) playCoinFlipButton.onClick.AddListener(() => { if (Manager != null) Manager.OpenCoinFlip(); });
        if (customAmountInput != null)
        {
            // Editing shows the plain number; leaving the field (or Enter) sets the bet, Escape puts the old one back
            customAmountInput.onSelect.AddListener(_ =>
            {
                if (customAmountInput != null && Manager != null)
                    customAmountInput.SetTextWithoutNotify(CasinoManager.PlainNumber(Manager.GetCurrentBet()));
            });
            customAmountInput.onEndEdit.AddListener(text =>
            {
                CasinoManager casino = Manager;
                if (casino == null) return;
                bool cancelled = Input.GetKeyDown(KeyCode.Escape);
                if (cancelled) casino.NoteBetEditEnded();
                else casino.CommitTypedBet(text);
                shownBet = -1;
            });
            customAmountInput.onValidateInput = (text, index, c) => c >= '0' && c <= '9' ? c : '\0';
        }
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        shownBet = -1;
        shownMax = -1;
        chipsShown = false;
        demoHeatDirty = true;
        if (demoHeatText != null) SubscribeDemo(Manager);   // Demo: refresh the heat line when the bet changes
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        SubscribeDemo(null);   // Demo:
    }

    protected override void OnLanguageRefresh()
    {
        // Demo: the heat line and the cap note are written again in the new language
        demoHeatDirty = true;
        demoNote = null;
    }

    protected override void LateUpdate()
    {
        base.LateUpdate();
        RefreshBet();
        RefreshDemoHeat();   // Demo: both do nothing outside the demo
        ShowDemoMaxNote();
    }

    protected override void OnBack()
    {
        if (Manager != null) Manager.CloseCasino();
    }

    protected override void OnClose()
    {
        if (Manager != null) Manager.CloseCasino();
    }

    void PickChip(int index)
    {
        CasinoManager casino = Manager;
        if (casino == null) return;
        if (index < ChipValues.Length) casino.SetBet(ChipValues[index]);
        else casino.SetMaxBet();
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }

    void RefreshBet()
    {
        CasinoManager casino = Manager;
        if (casino == null) return;
        long bet = casino.GetCurrentBet();
        long max = casino.GetMaxBet();
        if (bet == shownBet && max == shownMax && chipsShown) return;
        shownBet = bet;
        shownMax = max;
        chipsShown = true;

        bool canBet = max >= 1;
        int picked = -1;
        for (int i = 0; i < ChipValues.Length; i++)
            if (canBet && bet == ChipValues[i] && ChipValues[i] <= max) { picked = i; break; }
        if (picked < 0 && canBet && bet >= max) picked = ChipValues.Length;

        for (int i = 0; i < chipButtons.Length; i++)
        {
            bool affordable = canBet && (i >= ChipValues.Length || ChipValues[i] <= max);
            if (chipButtons[i] != null) CasinoUI.SetEnabled(chipButtons[i], affordable);
            CasinoChip graphic = i < chipGraphics.Length ? chipGraphics[i] : null;
            if (graphic != null) graphic.Selected = i == picked;
            RectTransform label = i < chipLabels.Length ? chipLabels[i] : null;
            if (label != null) label.anchoredPosition = new Vector2(0f, graphic != null ? graphic.Lift : 0f);
        }

        if (customAmountInput != null && !customAmountInput.isFocused)
            customAmountInput.SetTextWithoutNotify(UIFormat.Money(bet));
    }

    // ------------------------------------------------------------------
    // Demo: the heat line and the half-company note
    // ------------------------------------------------------------------

    void SubscribeDemo(CasinoManager target)
    {
        if (demoSubscribed == target) return;
        if (demoSubscribed != null) demoSubscribed.Changed -= OnDemoCasinoChanged;
        demoSubscribed = target;
        if (demoSubscribed != null) demoSubscribed.Changed += OnDemoCasinoChanged;
    }

    void OnDemoCasinoChanged()
    {
        demoHeatNext = 0f;
    }

    /// <summary>
    /// Demo: "HEAT 12 · THIS BET +1.7" in the level's ink colour (as on the heat chip), or the freeze line in red while the
    /// board has frozen gambling. Rewritten only when a shown number changes.
    /// </summary>
    void RefreshDemoHeat()
    {
        if (demoHeatText == null) return;
        if (!demoHeatDirty && Time.unscaledTime < demoHeatNext) return;
        demoHeatNext = Time.unscaledTime + DemoHeatSeconds;
        SubscribeDemo(Manager);

        HeatMeter meter = HeatMeter.Instance;
        GameObject line = demoHeatChip != null ? demoHeatChip.gameObject : demoHeatText.gameObject;
        bool show = meter != null;
        if (line.activeSelf != show) line.SetActive(show);
        if (!show) return;

        bool locked = meter.IsLockedOut;
        int heat = meter.DisplayHeat;
        int level = meter.Level;
        int release = Mathf.RoundToInt(meter.lockoutReleaseBelow * 10f);
        int add = 0;
        if (!locked)
        {
            CasinoManager casino = Manager;
            long bet = casino != null ? casino.GetCurrentBet() : 0;
            add = Mathf.RoundToInt(meter.BetHeat(bet, CasinoManager.CompanyValueNow()) * 10f);
        }
        if (!demoHeatDirty && locked == demoShownLocked && heat == demoShownHeat && add == demoShownAdd && level == demoShownLevel
            && release == demoShownRelease)
            return;
        demoHeatDirty = false;
        demoShownLocked = locked;
        demoShownHeat = heat;
        demoShownAdd = add;
        demoShownLevel = level;
        demoShownRelease = release;

        CultureInfo inv = CultureInfo.InvariantCulture;
        demoHeatText.text = locked
            ? DemoMode.T("demo.casino.frozen", ("release", (release / 10f).ToString("0.#", inv)))
            : DemoMode.T("demo.casino.heat", ("heat", heat.ToString(inv)), ("add", (add / 10f).ToString("0.0", inv)));
        demoHeatText.color = locked ? UIColors.Loss : HeatMeter.LevelInkColor(level);
        FitDemoHeatChip();
    }

    /// <summary>Demo: the heat chip fits its text (14 px each side, at most 1100 px; a longer text shrinks).</summary>
    void FitDemoHeatChip()
    {
        if (demoHeatChip == null || demoHeatText == null) return;
        float width = Mathf.Ceil(CasinoUI.TextWidth(demoHeatText.font, demoHeatText.text, 18f, true)) + 4f;
        demoHeatChip.sizeDelta = new Vector2(Mathf.Clamp(width + 28f, 80f, 1100f), demoHeatChip.sizeDelta.y);
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

    protected override void OnValueShown()
    {
        // The chip fits its value, growing to the left (its right edge stays put)
        if (valueChip == null || companyValueText == null) return;
        RectTransform valueRect = companyValueText.rectTransform;
        float width = Mathf.Ceil(RunEndUI.TextWidth(companyValueText.font, companyValueText.text, 26f, true)) + 4f;
        valueRect.sizeDelta = new Vector2(width, valueRect.sizeDelta.y);
        valueChip.sizeDelta = new Vector2(valueRect.anchoredPosition.x + width + 18f, valueChip.sizeDelta.y);
    }

    protected override void FitLayout()
    {
        base.FitLayout();
        if (runtimeBetRow != null) LayoutBetRow(runtimeBetRow);
        FitDemoHeatChip();   // Demo: the language's font may be wider
    }

    // ------------------------------------------------------------------
    // Runtime layout (board CasinoHub, 1920 x 1080; the table's inside starts at (94, 74))
    // ------------------------------------------------------------------

    /// <summary>Build the hub under <paramref name="parent"/> (an inactive root, so buttons wake up with their faces in place).</summary>
    public static CasinoHubView Build(RectTransform parent, TMP_FontAsset font)
    {
        RectTransform screen = CasinoUI.FullRect(parent, "CasinoHub");
        CasinoHubView view = screen.gameObject.AddComponent<CasinoHubView>();
        view.font = font;

        // The paused office behind a scrim (it takes the HUD's clicks) and the "Game paused" chip
        RunEndUI.Scrim(screen, UIColors.Scrim);
        RectTransform stage = CasinoUI.Stage(screen);
        CasinoUI.PausedChip(stage, font, 848f, 4f);

        // The table: soft shadow, felt, 14 px wood rail, gold line 12 px inside it
        RectTransform table = CasinoUI.Rect(stage, "Table", 80f, 60f, 1760f, 960f);
        BlueprintBox shadow = BlueprintUI.Box(table, "Shadow", Color.clear, Color.clear, 0f, CasinoUI.RadiusCard);
        BlueprintUI.Stretch(shadow.rectTransform);
        shadow.SetShadow(CasinoUI.SheetShadow, new Vector2(0f, 22f), 48f);
        CasinoFelt felt = CasinoUI.Felt(table, "Felt", true);
        BlueprintUI.Stretch(felt.rectTransform, 14f, 14f, 14f, 14f);
        BlueprintBox rail = BlueprintUI.Box(table, "Rail", Color.clear, CasinoUI.Wood, 14f, CasinoUI.RadiusCard);
        BlueprintUI.Stretch(rail.rectTransform);
        BlueprintBox goldLine = BlueprintUI.Box(table, "GoldLine", Color.clear, UIColors.WithAlpha(UIColors.Gold, 0.8f), 3f, CasinoUI.RadiusCard);
        BlueprintUI.Stretch(goldLine.rectTransform, 26f, 26f, 26f, 26f);

        RectTransform inner = CasinoUI.Rect(table, "Inside", 14f, 14f, 1732f, 932f);

        // Header: title, tagline, company value chip
        Fixed(CasinoUI.Shrink(CasinoUI.Text(inner, "Title", "", 72f, UIColors.Gold, font, 56f, 46f, 1000f, 80f, TextAlignmentOptions.MidlineLeft, true), 40f),
              "casino.hub.title");   // "Company Casino"
        Fixed(CasinoUI.Shrink(CasinoUI.Text(inner, "Tagline", "", 19f, UIColors.PaperHi, font, 56f, 128f, 800f, 26f), 14f), "casino.hub.tagline");
        view.companyValueText = CasinoUI.ValueChip(inner, font, 1212f, 72f, 380f, 56f);
        view.valueChip = (RectTransform)view.companyValueText.transform.parent;
        view.valueChip.pivot = new Vector2(1f, 1f);
        view.valueChip.anchoredPosition = new Vector2(1592f, -72f);
        view.companyValueText.enableAutoSizing = false;

        BuildBetRow(view, inner, font);
        if (DemoMode.Active) BuildDemoHeatLine(view, inner, font);   // Demo: the heat line under the bet row
        BuildGameCards(view, inner, font);

        // Footer: Leave casino, the house rule. The button fits its label (up to 520 px), also after a language change
        CasinoButton leave = CasinoUI.Button(inner, "LeaveCasino", L10n.Get(T, "casino.hub.button.leave"), CasinoIconKind.Back, CasinoButton.Look.Paper,
                                             font, 28f, false, 56f, 818f, 0f, 68f, new Vector2(4f, 5f), UIColors.Ink, 36f, 28f, 200f, 520f);
        L10nText.Attach(leave.label, T, "casino.hub.button.leave");   // "Leave casino"
        view.backButton = leave.button;
        Fixed(CasinoUI.Shrink(CasinoUI.Text(inner, "HouseRule", "", 19f, UIColors.PaperHi, font, 600f, 818f, 1076f, 68f, TextAlignmentOptions.MidlineRight), 14f),
              "casino.hub.house_rule");   // "Every bet comes out of company value. Every win goes back in."
        view.closeButton = CasinoUI.CloseButton(inner, "Close", 1644f, 28f);
        return view;
    }

    /// <summary>A fixed text kept on its String Table entry (and the language's font).</summary>
    static TextMeshProUGUI Fixed(TextMeshProUGUI text, string key)
    {
        L10nText.Attach(text, T, key);
        return text;
    }

    static void BuildBetRow(CasinoHubView view, RectTransform inner, TMP_FontAsset font)
    {
        const float rowY = 190f, rowHeight = 96f, midY = rowY + rowHeight * 0.5f;
        float x = 56f;
        RectTransform row = CasinoUI.Rect(inner, "BetRow", 0f, 0f, 1732f, 932f);   // the row's pieces keep the table's coordinates
        view.runtimeBetRow = row;
        Fixed(CasinoUI.Text(row, "YourBet", "", 26f, UIColors.PaperHi, font, x, rowY, 10f, rowHeight), "casino.hub.your_bet");   // "Your bet"

        string[] labels = { "$100", "$500", "$1K", "$5K", L10n.Get(T, "casino.hub.chip.max") };
        Color[] colors = { CasinoUI.ChipRed, CasinoUI.ChipBlue, CasinoUI.ChipBlack, CasinoUI.ChipGold, UIColors.PaperHi };
        for (int i = 0; i < labels.Length; i++)
        {
            bool dark = i >= 3;   // the gold and paper chips have ink labels
            Color spots = i == 4 ? UIColors.Ink : UIColors.PaperHi;
            string chipName = i < 4 ? "Chip" + labels[i].Replace("$", "") : "ChipMAX";
            CasinoChip chip = CasinoUI.Chip(row, chipName, 76f, colors[i], spots, true, x + 38f + i * 96f, midY,
                                            labels[i], dark ? UIColors.Ink : UIColors.PaperHi, font, out TextMeshProUGUI label);
            if (i == 4) L10nText.Attach(label, T, "casino.hub.chip.max");   // "MAX"
            chip.SetShadow(CasinoUI.FeltShadow, 5f);
            chip.raycastTarget = true;
            Button button = chip.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = chip;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            view.chipButtons[i] = button;
            view.chipGraphics[i] = chip;
            view.chipLabels[i] = label.rectTransform;
        }

        Fixed(CasinoUI.Text(row, "Or", "", 19f, UIColors.PaperHi, font, x, rowY, 10f, rowHeight), "casino.hub.or");   // "or"
        Fixed(CasinoUI.Text(row, "CustomAmount", "", 19f, UIColors.PaperHi, font, x, rowY, 10f, rowHeight), "casino.hub.custom");   // "Custom amount"
        view.customAmountInput = AmountInput(row, font, x, midY - 32f);
        // The hub writes the hint (the reserve from CasinoManager)
        view.keepHintText = CasinoUI.Shrink(CasinoUI.Text(row, "KeepHint", "", 19f, UIColors.WithAlpha(UIColors.PaperHi, 0.85f), font, x, rowY,
                                                          120f, rowHeight), 14f);
        LayoutBetRow(row);
    }

    /// <summary>
    /// Demo: the heat line under the bet row, a small paper chip like the HUD's heat chip (2 px ink, felt shadow) whose text
    /// the hub writes in the level's colour.
    /// </summary>
    static void BuildDemoHeatLine(CasinoHubView view, RectTransform inner, TMP_FontAsset font)
    {
        BlueprintBox chip = CasinoUI.Box(inner, "DemoHeat", UIColors.PaperHi, UIColors.Ink, 2f, CasinoUI.RadiusSmall, 56f, 289f, 240f, 28f);
        chip.SetShadow(CasinoUI.FeltShadow, new Vector2(3f, 4f));
        TextMeshProUGUI text = BlueprintUI.Text(chip.transform, "Text", "", 18f, UIColors.Ink, font, TextAlignmentOptions.MidlineLeft, true);
        BlueprintUI.Stretch(text.rectTransform, 14f, 0f, 14f, 0f);
        CasinoUI.Shrink(text, 13f);
        L10nFont.Attach(text);
        view.demoHeatChip = chip.rectTransform;
        view.demoHeatText = text;
    }

    /// <summary>
    /// Place the runtime bet row left to right by the width of its words as they read now: Your bet, the five chips, or,
    /// Custom amount, the field, the keep hint in what is left.
    /// </summary>
    static void LayoutBetRow(RectTransform row)
    {
        float x = 56f;
        x = PlaceWord(row, "YourBet", x, 26f) + 28f;
        for (int i = 0; i < 5; i++)
        {
            Transform chip = row.Find(i < 4 ? "Chip" + new[] { "100", "500", "1K", "5K" }[i] : "ChipMAX");
            if (chip is RectTransform chipRect) chipRect.anchoredPosition = new Vector2(x + 38f + i * 96f, chipRect.anchoredPosition.y);
        }
        x += 5f * 76f + 4f * 20f + 28f;
        x = PlaceWord(row, "Or", x, 19f) + 28f;
        x = PlaceWord(row, "CustomAmount", x, 19f) + 12f;
        if (row.Find("CustomAmountField") is RectTransform field) field.anchoredPosition = new Vector2(x, field.anchoredPosition.y);
        x += 200f + 24f;
        if (row.Find("KeepHint") is RectTransform hint)
        {
            hint.anchoredPosition = new Vector2(x, hint.anchoredPosition.y);
            hint.sizeDelta = new Vector2(Mathf.Max(120f, 1676f - x), hint.sizeDelta.y);
        }
    }

    /// <summary>Put a one-line text of the row at <paramref name="x"/>, as wide as its words; returns its right edge.</summary>
    static float PlaceWord(RectTransform row, string name, float x, float size)
    {
        Transform child = row.Find(name);
        TMP_Text text = child != null ? child.GetComponent<TMP_Text>() : null;
        if (text == null) return x;
        float width = Mathf.Ceil(CasinoUI.TextWidth(text.font, text.text, size)) + 2f;
        RectTransform rect = text.rectTransform;
        rect.anchoredPosition = new Vector2(x, rect.anchoredPosition.y);
        rect.sizeDelta = new Vector2(width, rect.sizeDelta.y);
        return x + width;
    }

    /// <summary>The custom amount field: paper, 3 px ink border, 28 px text; only digits get in.</summary>
    static TMP_InputField AmountInput(RectTransform parent, TMP_FontAsset font, float x, float y)
    {
        BlueprintBox box = CasinoUI.Box(parent, "CustomAmountField", UIColors.PaperHi, UIColors.Ink, 3f, CasinoUI.RadiusSmall, x, y, 200f, 64f);
        box.raycastTarget = true;
        RectTransform area = BlueprintUI.Stretch(BlueprintUI.NewRect("Text Area", box.transform), 20f, 4f, 20f, 4f);
        area.gameObject.AddComponent<RectMask2D>();
        TextMeshProUGUI placeholder = BlueprintUI.Text(area, "Placeholder", "$1,000", 28f, UIColors.Pencil, font, TextAlignmentOptions.MidlineLeft);
        BlueprintUI.Stretch(placeholder.rectTransform);
        TextMeshProUGUI text = BlueprintUI.Text(area, "Text", "", 28f, UIColors.Ink, font, TextAlignmentOptions.MidlineLeft);
        BlueprintUI.Stretch(text.rectTransform);
        text.enableAutoSizing = true;
        text.fontSizeMin = 14f;
        text.fontSizeMax = 28f;

        TMP_InputField input = box.gameObject.AddComponent<TMP_InputField>();
        input.targetGraphic = box;
        input.colors = BlueprintUI.Tint();
        input.navigation = new Navigation { mode = Navigation.Mode.None };
        input.textViewport = area;
        input.textComponent = text;
        input.placeholder = placeholder;
        input.contentType = TMP_InputField.ContentType.Standard;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.characterLimit = 16;
        input.customCaretColor = true;
        input.caretColor = UIColors.Ink;
        input.caretWidth = 2;
        input.selectionColor = UIColors.WithAlpha(UIColors.Highlight, 0.6f);
        if (font != null) input.fontAsset = font;
        return input;
    }

    static void BuildGameCards(CasinoHubView view, RectTransform inner, TMP_FontAsset font)
    {
        view.playSlotsButton = GameCard(inner, "SlotsCard", 56f, "casino.hub.slots", font, SlotsArt);
        view.playBlackjackButton = GameCard(inner, "BlackjackCard", 616f, "casino.hub.blackjack", font, BlackjackArt);
        view.playCoinFlipButton = GameCard(inner, "CoinFlipCard", 1176f, "casino.hub.coinflip", font, CoinFlipArt);
    }

    delegate void Art(RectTransform card, TMP_FontAsset font);

    /// <summary>
    /// A game card (500 x 460): dark felt, ink border, gold line, the game's picture, name, tagline and Play. The name and
    /// tagline are the String Table entries <paramref name="key"/>.title and .tagline.
    /// </summary>
    static Button GameCard(RectTransform parent, string name, float x, string key, TMP_FontAsset font, Art art)
    {
        BlueprintBox card = CasinoUI.Box(parent, name, UIColors.FeltDark, UIColors.Ink, 3f, CasinoUI.RadiusLarge, x, 322f, 500f, 460f);
        card.SetShadow(new Color(0f, 0f, 0f, 0.35f), new Vector2(0f, 12f), 24f);
        BlueprintBox line = BlueprintUI.Box(card.transform, "GoldLine", Color.clear, UIColors.WithAlpha(UIColors.Gold, 0.8f), 2f, CasinoUI.RadiusLarge);
        BlueprintUI.Stretch(line.rectTransform, 11f, 11f, 11f, 11f);

        art(card.rectTransform, font);
        Fixed(CasinoUI.Shrink(CasinoUI.Text(card.transform, "Title", "", 44f, UIColors.Gold, font, 35f, 211f, 430f, 50f, TextAlignmentOptions.MidlineLeft, true), 28f),
              key + ".title");
        Fixed(CasinoUI.Shrink(CasinoUI.Text(card.transform, "Tagline", "", 22f, UIColors.PaperHi, font, 35f, 273f, 430f, 30f), 16f), key + ".tagline");
        CasinoButton play = CasinoUI.Button(card.transform, "Play", L10n.Get(T, "casino.hub.button.play"), CasinoIconKind.Play, CasinoButton.Look.Gold, font,
                                            28f, true, 35f, 361f, 430f, 68f, new Vector2(5f, 6f), CasinoUI.FeltShadow);
        L10nText.Attach(play.label, T, "casino.hub.button.play");   // "Play"
        return play.button;
    }

    // The pictures sit in a 430 x 168 area whose centre is (250, 115) on the card

    static void SlotsArt(RectTransform card, TMP_FontAsset font)
    {
        BlueprintBox machine = CasinoUI.Box(card, "SlotMachine", UIColors.Loss, UIColors.Gold, 4f, CasinoUI.RadiusLarge, 80f, 35f, 340f, 160f);
        machine.SetRings(UIColors.Ink, 3f, Color.clear, 0f);
        for (int i = 0; i < 3; i++)
        {
            BlueprintBox window = CasinoUI.Box(machine.transform, "Window" + i, UIColors.PaperHi, UIColors.Ink, 2f, BlueprintUI.Round(8f),
                                               22f + i * 102f, 20f, 92f, 120f);
            CasinoIcon.Create(window.transform, CasinoIconKind.Seven, 84f, Color.white, 46f, 60f, 1.4f);
        }
        BlueprintBox payline = CasinoUI.Box(machine.transform, "Payline", UIColors.Loss, Color.clear, 0f, Vector4.zero, 12f, 78f, 316f, 4f);
        payline.SetRings(UIColors.Ink, 1.5f, Color.clear, 0f);
    }

    static void BlackjackArt(RectTransform card, TMP_FontAsset font)
    {
        MiniCard(card, font, 140f, 37f, "A", 0, 8f);
        MiniCard(card, font, 246f, 37f, "K", 1, -7f);
    }

    /// <summary>A small face-up card (112 x 156) with its top-left at (x, y), turned <paramref name="degrees"/> (positive = counter-clockwise).</summary>
    static void MiniCard(RectTransform parent, TMP_FontAsset font, float x, float y, string rank, int suit, float degrees)
    {
        RectTransform rect = CasinoUI.Rect(parent, "Card" + rank, x, y, 112f, 156f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(x + 56f, -(y + 78f));
        rect.localEulerAngles = new Vector3(0f, 0f, degrees);
        BlueprintBox face = BlueprintUI.Box(rect, "Face", UIColors.PaperHi, UIColors.Ink, 2f, BlueprintUI.Round(12f));
        face.SetShadow(CasinoUI.CardShadow, new Vector2(3f, 4f));
        BlueprintUI.Stretch(face.rectTransform);
        Color ink = CasinoCard.IsRed(suit) ? UIColors.Loss : UIColors.Ink;
        CasinoIconKind kind = CasinoIcon.Suit(suit);

        for (int corner = 0; corner < 2; corner++)
        {
            RectTransform group = BlueprintUI.NewRect(corner == 0 ? "CornerTop" : "CornerBottom", face.transform);
            group.sizeDelta = new Vector2(24f, 52f);
            group.anchorMin = group.anchorMax = corner == 0 ? new Vector2(0f, 1f) : new Vector2(1f, 0f);
            group.anchoredPosition = corner == 0 ? new Vector2(10f + 12f, -(8f + 26f)) : new Vector2(-(10f + 12f), 8f + 26f);
            if (corner == 1) group.localEulerAngles = new Vector3(0f, 0f, 180f);
            TextMeshProUGUI label = BlueprintUI.Text(group, "Rank", rank, 26f, ink, font, TextAlignmentOptions.Center, true);
            RectTransform labelRect = label.rectTransform;
            labelRect.anchorMin = new Vector2(0f, 1f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.pivot = new Vector2(0.5f, 1f);
            labelRect.sizeDelta = new Vector2(12f, 30f);
            labelRect.anchoredPosition = new Vector2(0f, 2f);
            CasinoIcon small = CasinoIcon.Create(group, kind, 20f, ink, 0f, 0f);
            small.rectTransform.anchorMin = small.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            small.rectTransform.pivot = new Vector2(0.5f, 0f);
            small.rectTransform.anchoredPosition = Vector2.zero;
        }
        CasinoIcon.Create(face.transform, kind, 54f, ink, 56f, 78f);
    }

    static void CoinFlipArt(RectTransform card, TMP_FontAsset font)
    {
        // The coins' letters are the coin flip's own (heads H / tails T in English), kept on the language
        CasinoUI.CoinParts tails = CasinoUI.Coin(card, "CoinT", 108f, font, 306f, 121f, L10n.Get(T, "casino.coinflip.coin.tails"), 7f, 10f, 8f, 2f, 0f, false, 3f, 2f);
        CasinoUI.CoinParts heads = CasinoUI.Coin(card, "CoinH", 144f, font, 220f, 109f, L10n.Get(T, "casino.coinflip.coin.heads"), 8f, 11f, 10f, 2f, 0f, false, 3f, 2f);
        foreach (TextMeshProUGUI letter in new[] { tails.letter, tails.letterLight, tails.letterDark })
            L10nText.Attach(letter, T, "casino.coinflip.coin.tails");
        foreach (TextMeshProUGUI letter in new[] { heads.letter, heads.letterLight, heads.letterDark })
            L10nText.Attach(letter, T, "casino.coinflip.coin.heads");
    }
}
