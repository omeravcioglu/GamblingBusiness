using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// TEMPORARY runtime-built stock market panel, used when StockMarketUI has no designed panel assigned.
/// Build() creates a plain dark panel under the HUD canvas and fills in StockMarketUI's serialized references
/// (including an inactive row template used as stockItemPrefab), so StockMarketUI runs exactly as it will
/// with the designed panel. Delete this file once the designed panel is wired in.
/// </summary>
public static class StockMarketFallbackUI
{
    // Layout at the 1920x1080 reference resolution
    const float PanelWidth = 1100f;
    const float PanelHeight = 700f;
    const float RowHeight = 44f;
    const float CellSpacing = 6f;
    const float ScrollbarWidth = 10f;
    const float DetailWidth = 380f;
    // List columns, shared by the header and the rows (the name column takes the remaining width)
    const float SymbolWidth = 76f, PriceWidth = 104f, ChangeWidth = 88f, OwnedWidth = 92f, TypeWidth = 88f;

    // Theme
    static readonly Color PanelColor = new Color(0.07f, 0.08f, 0.10f, 0.97f);
    static readonly Color SectionColor = new Color(0.11f, 0.12f, 0.15f, 1f);
    static readonly Color RowColor = new Color(0.17f, 0.19f, 0.23f, 1f);
    static readonly Color RowOwnedColor = new Color(0.16f, 0.25f, 0.36f, 1f);
    static readonly Color RowSelectedColor = new Color(0.24f, 0.40f, 0.66f, 1f);
    static readonly Color TextColor = new Color(0.93f, 0.94f, 0.96f, 1f);
    static readonly Color MutedColor = new Color(0.62f, 0.66f, 0.72f, 1f);
    static readonly Color PositiveColor = new Color(0.30f, 0.85f, 0.45f, 1f);
    static readonly Color NegativeColor = new Color(0.95f, 0.38f, 0.38f, 1f);
    static readonly Color BuyColor = new Color(0.18f, 0.58f, 0.34f, 1f);
    static readonly Color BuyMaxColor = new Color(0.14f, 0.45f, 0.27f, 1f);
    static readonly Color SellColor = new Color(0.72f, 0.26f, 0.26f, 1f);
    static readonly Color SellAllColor = new Color(0.56f, 0.20f, 0.20f, 1f);
    static readonly Color ButtonColor = new Color(0.32f, 0.35f, 0.42f, 1f);
    static readonly Color InputColor = new Color(0.04f, 0.05f, 0.07f, 1f);

    /// <summary>Build the panel under <paramref name="canvas"/> (a new overlay canvas if null) and assign <paramref name="ui"/>'s references.</summary>
    public static void Build(StockMarketUI ui, Canvas canvas)
    {
        if (ui == null) return;
        if (canvas == null) canvas = CreateOverlayCanvas();
        EnsureEventSystem();

        // Built inactive, so every component initialises with its references already set
        RectTransform panel = NewRect("StockMarketPanel (Runtime)", canvas.transform);
        panel.gameObject.SetActive(false);
        ui.stockMarketPanel = panel.gameObject;

        panel.anchorMin = new Vector2(0.5f, 0.5f);
        panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.pivot = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(PanelWidth, PanelHeight);
        panel.anchoredPosition = Vector2.zero;
        AddImage(panel, PanelColor);
        AddVertical(panel, 10f, new RectOffset(18, 18, 18, 18));

        BuildHeader(ui, panel);
        BuildSummary(ui, panel);

        RectTransform body = NewRect("Body", panel);
        SetLayout(body, flexHeight: 1f);
        AddHorizontal(body, 16f, new RectOffset(), true);
        BuildList(ui, body);
        BuildDetail(ui, body);

        ui.stockItemPrefab = BuildRowTemplate(panel).gameObject;

        ui.positiveColor = PositiveColor;
        ui.negativeColor = NegativeColor;
        ui.neutralColor = TextColor;
    }

    // ------------------------------------------------------------------
    // Sections
    // ------------------------------------------------------------------

    static void BuildHeader(StockMarketUI ui, Transform panel)
    {
        RectTransform header = NewRect("Header", panel);
        SetLayout(header, prefHeight: 52f);
        AddHorizontal(header, 12f, new RectOffset(4, 0, 0, 0));

        TextMeshProUGUI title = NewText(header, "Title", "STOCK MARKET", 32f, TextColor, TextAlignmentOptions.Left, FontStyles.Bold);
        SetLayout(title, flexWidth: 1f);

        ui.closeButton = NewButton(header, "CloseButton", "Close", ButtonColor);
        SetLayout(ui.closeButton, prefWidth: 120f, prefHeight: 44f);
    }

    static void BuildSummary(StockMarketUI ui, Transform panel)
    {
        RectTransform summary = NewRect("Summary", panel);
        SetLayout(summary, prefHeight: 40f);
        AddImage(summary, SectionColor);
        AddHorizontal(summary, 16f, new RectOffset(14, 14, 0, 0));

        ui.portfolioValueText = NewText(summary, "PortfolioValue", "Portfolio: $0.00", 20f, TextColor, TextAlignmentOptions.Left);
        ui.cashAvailableText = NewText(summary, "CashAvailable", "Cash: $0.00", 20f, TextColor, TextAlignmentOptions.Center);
        ui.totalAssetsText = NewText(summary, "TotalAssets", "Total: $0.00", 20f, TextColor, TextAlignmentOptions.Right, FontStyles.Bold);
        foreach (TextMeshProUGUI text in new[] { ui.portfolioValueText, ui.cashAvailableText, ui.totalAssetsText })
        {
            SetLayout(text, prefWidth: 0f, flexWidth: 1f, minWidth: 0f);
            AutoSize(text, 20f);
        }
    }

    static void BuildList(StockMarketUI ui, Transform body)
    {
        RectTransform column = NewRect("StockList", body);
        SetLayout(column, flexWidth: 1f);
        AddVertical(column, 6f, new RectOffset());

        // Column headers, aligned with the row cells (content padding 4 + row padding 10; scrollbar gutter on the right)
        RectTransform headers = NewRect("ColumnHeaders", column);
        SetLayout(headers, prefHeight: 26f);
        AddHorizontal(headers, CellSpacing, new RectOffset(14, 14 + (int)ScrollbarWidth + 4, 0, 0), true);
        Cell(headers, "Symbol", "SYMBOL", SymbolWidth, TextAlignmentOptions.Left, MutedColor, 18f);
        Cell(headers, "Company", "COMPANY", 0f, TextAlignmentOptions.Left, MutedColor, 18f);
        Cell(headers, "Price", "PRICE", PriceWidth, TextAlignmentOptions.Right, MutedColor, 18f);
        Cell(headers, "Change", "CHANGE", ChangeWidth, TextAlignmentOptions.Right, MutedColor, 18f);
        Cell(headers, "Owned", "OWNED", OwnedWidth, TextAlignmentOptions.Right, MutedColor, 18f);
        Cell(headers, "Type", "TYPE", TypeWidth, TextAlignmentOptions.Right, MutedColor, 18f);

        // Scroll view (its image also catches scroll and drag input between rows)
        RectTransform scrollRoot = NewRect("ScrollView", column);
        SetLayout(scrollRoot, flexHeight: 1f);
        AddImage(scrollRoot, SectionColor);
        ScrollRect scroll = scrollRoot.gameObject.AddComponent<ScrollRect>();

        RectTransform viewport = NewRect("Viewport", scrollRoot);
        Stretch(viewport, 0f, ScrollbarWidth + 4f, 0f, 0f);
        viewport.gameObject.AddComponent<RectMask2D>();

        RectTransform content = NewRect("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.sizeDelta = Vector2.zero;
        content.anchoredPosition = Vector2.zero;
        AddVertical(content, 4f, new RectOffset(4, 4, 4, 4));
        ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.content = content;
        scroll.viewport = viewport;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 40f;
        scroll.verticalScrollbar = NewScrollbar(scrollRoot);
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

        ui.stockListContent = content.gameObject;
    }

    static void BuildDetail(StockMarketUI ui, Transform body)
    {
        RectTransform column = NewRect("DetailColumn", body);
        SetLayout(column, prefWidth: DetailWidth, flexWidth: 0f, minWidth: DetailWidth);

        // Visible behind the detail panel while no stock is selected
        TextMeshProUGUI hint = NewText(column, "SelectHint", "Select a stock from the list.", 20f, MutedColor, TextAlignmentOptions.Center);
        Stretch(hint.rectTransform);

        RectTransform detail = NewRect("StockDetailPanel", column);
        Stretch(detail);
        AddImage(detail, SectionColor);
        AddVertical(detail, 6f, new RectOffset(16, 16, 14, 14));
        ui.stockDetailPanel = detail.gameObject;

        RectTransform symbolRow = Row(detail, "SymbolRow", 40f, 8f);
        ui.detailSymbolText = NewText(symbolRow, "Symbol", "", 32f, TextColor, TextAlignmentOptions.Left, FontStyles.Bold);
        SetLayout(ui.detailSymbolText, flexWidth: 1f);
        ui.detailTypeText = NewText(symbolRow, "Type", "", 18f, MutedColor, TextAlignmentOptions.Right);
        SetLayout(ui.detailTypeText, prefWidth: 170f);

        ui.detailNameText = NewText(detail, "Name", "", 20f, MutedColor, TextAlignmentOptions.Left);
        SetLayout(ui.detailNameText, prefHeight: 26f);

        RectTransform priceRow = Row(detail, "PriceRow", 38f, 8f);
        ui.detailPriceText = NewText(priceRow, "Price", "", 28f, TextColor, TextAlignmentOptions.Left, FontStyles.Bold);
        SetLayout(ui.detailPriceText, flexWidth: 1f);
        AutoSize(ui.detailPriceText, 28f);
        ui.detailChangeText = NewText(priceRow, "Change", "", 22f, TextColor, TextAlignmentOptions.Right, FontStyles.Bold);
        SetLayout(ui.detailChangeText, prefWidth: 120f);

        ui.detailOwnedText = NewText(detail, "Owned", "", 20f, TextColor, TextAlignmentOptions.Left);
        SetLayout(ui.detailOwnedText, prefHeight: 26f);
        AutoSize(ui.detailOwnedText, 20f);

        ui.detailValueText = NewText(detail, "Value", "", 20f, TextColor, TextAlignmentOptions.TopLeft);
        ui.detailValueText.textWrappingMode = TextWrappingModes.Normal;
        SetLayout(ui.detailValueText, prefHeight: 52f);

        RectTransform separator = NewRect("Separator", detail);
        AddImage(separator, new Color(1f, 1f, 1f, 0.08f));
        SetLayout(separator, prefHeight: 2f);

        RectTransform amountRow = Row(detail, "AmountRow", 44f, 10f);
        TextMeshProUGUI amountLabel = NewText(amountRow, "Label", "Shares", 20f, MutedColor, TextAlignmentOptions.Left);
        SetLayout(amountLabel, prefWidth: 80f);
        ui.shareAmountInput = NewIntegerInput(amountRow, "ShareAmountInput", "Amount", 20f);
        SetLayout(ui.shareAmountInput, prefWidth: 0f, flexWidth: 1f);

        RectTransform tradeRow = Row(detail, "BuySellRow", 44f, 10f);
        ui.buyButton = NewButton(tradeRow, "BuyButton", "Buy", BuyColor);
        ui.sellButton = NewButton(tradeRow, "SellButton", "Sell", SellColor);

        RectTransform maxRow = Row(detail, "MaxRow", 44f, 10f);
        ui.buyMaxButton = NewButton(maxRow, "BuyMaxButton", "Buy Max", BuyMaxColor);
        ui.sellAllButton = NewButton(maxRow, "SellAllButton", "Sell All", SellAllColor);
        foreach (Button button in new[] { ui.buyButton, ui.sellButton, ui.buyMaxButton, ui.sellAllButton })
            SetLayout(button, prefWidth: 0f, flexWidth: 1f);

        ui.tradeInfoText = NewText(detail, "TradeInfo", "", 18f, MutedColor, TextAlignmentOptions.TopLeft);
        ui.tradeInfoText.textWrappingMode = TextWrappingModes.Normal;
        SetLayout(ui.tradeInfoText, prefHeight: 70f);

        ui.tradeMessageText = NewText(detail, "TradeMessage", "", 18f, TextColor, TextAlignmentOptions.TopLeft);
        ui.tradeMessageText.textWrappingMode = TextWrappingModes.Normal;
        SetLayout(ui.tradeMessageText, prefHeight: 48f);
    }

    /// <summary>Inactive row used as stockItemPrefab; StockMarketUI instantiates and activates one per stock.</summary>
    static StockItemUI BuildRowTemplate(Transform parent)
    {
        RectTransform row = NewRect("StockRowTemplate", parent);
        row.gameObject.SetActive(false);
        SetLayout(row, prefHeight: RowHeight);
        Image background = AddImage(row, RowColor);
        Button button = row.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        button.colors = Tint();
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        AddHorizontal(row, CellSpacing, new RectOffset(10, 10, 0, 0), true);

        StockItemUI item = row.gameObject.AddComponent<StockItemUI>();
        item.symbolText = Cell(row, "Symbol", "", SymbolWidth, TextAlignmentOptions.Left, TextColor, 20f, FontStyles.Bold);
        item.nameText = Cell(row, "Name", "", 0f, TextAlignmentOptions.Left, MutedColor, 20f);
        item.priceText = Cell(row, "Price", "", PriceWidth, TextAlignmentOptions.Right, TextColor, 20f);
        item.changeText = Cell(row, "Change", "", ChangeWidth, TextAlignmentOptions.Right, TextColor, 20f);
        item.ownedText = Cell(row, "Owned", "", OwnedWidth, TextAlignmentOptions.Right, TextColor, 20f);
        item.typeText = Cell(row, "Type", "", TypeWidth, TextAlignmentOptions.Right, TextColor, 18f);
        item.selectButton = button;
        item.backgroundImage = background;
        item.normalColor = RowColor;
        item.ownedHighlightColor = RowOwnedColor;
        item.selectedColor = RowSelectedColor;
        item.positiveColor = PositiveColor;
        item.negativeColor = NegativeColor;
        item.unchangedColor = MutedColor;
        return item;
    }

    // ------------------------------------------------------------------
    // Canvas / EventSystem
    // ------------------------------------------------------------------

    static Canvas CreateOverlayCanvas()
    {
        var go = new GameObject("StockMarketCanvas (Runtime)", typeof(RectTransform));
        int uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer >= 0) go.layer = uiLayer;

        Canvas canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        CanvasScaler scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        go.AddComponent<GraphicRaycaster>();
        return canvas;
    }

    static void EnsureEventSystem()
    {
        if (EventSystem.current != null || Object.FindAnyObjectByType<EventSystem>() != null) return;
        new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
    }

    // ------------------------------------------------------------------
    // Building blocks
    // ------------------------------------------------------------------

    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        return rect;
    }

    static void Stretch(RectTransform rect, float left = 0f, float right = 0f, float top = 0f, float bottom = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    static Image AddImage(Component target, Color color)
    {
        Image image = target.gameObject.AddComponent<Image>();
        image.color = color;
        return image;
    }

    /// <summary>Set layout sizes (-1 = not set). A preferred height is also used as the minimum height.</summary>
    static LayoutElement SetLayout(Component target, float prefWidth = -1f, float prefHeight = -1f,
                                   float flexWidth = -1f, float flexHeight = -1f, float minWidth = -1f)
    {
        LayoutElement element = target.GetComponent<LayoutElement>();
        if (element == null) element = target.gameObject.AddComponent<LayoutElement>();
        element.preferredWidth = prefWidth;
        element.preferredHeight = prefHeight;
        element.flexibleWidth = flexWidth;
        element.flexibleHeight = flexHeight;
        element.minWidth = minWidth;
        element.minHeight = prefHeight >= 0f ? prefHeight : -1f;
        return element;
    }

    static HorizontalLayoutGroup AddHorizontal(Component target, float spacing, RectOffset padding, bool expandHeight = false)
    {
        var group = target.gameObject.AddComponent<HorizontalLayoutGroup>();
        group.spacing = spacing;
        group.padding = padding;
        group.childAlignment = TextAnchor.MiddleLeft;
        group.childControlWidth = true;
        group.childControlHeight = true;
        group.childForceExpandWidth = false;
        group.childForceExpandHeight = expandHeight;
        return group;
    }

    static VerticalLayoutGroup AddVertical(Component target, float spacing, RectOffset padding)
    {
        var group = target.gameObject.AddComponent<VerticalLayoutGroup>();
        group.spacing = spacing;
        group.padding = padding;
        group.childAlignment = TextAnchor.UpperLeft;
        group.childControlWidth = true;
        group.childControlHeight = true;
        group.childForceExpandWidth = true;
        group.childForceExpandHeight = false;
        return group;
    }

    static RectTransform Row(Transform parent, string name, float height, float spacing)
    {
        RectTransform row = NewRect(name, parent);
        SetLayout(row, prefHeight: height);
        AddHorizontal(row, spacing, new RectOffset(), true);
        return row;
    }

    static TextMeshProUGUI NewText(Transform parent, string name, string text, float size, Color color,
                                   TextAlignmentOptions alignment, FontStyles style = FontStyles.Normal)
    {
        RectTransform rect = NewRect(name, parent);
        TextMeshProUGUI label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.fontSize = size; // an explicit size also stops TMP from applying its defaults on Awake
        label.text = text;
        label.color = color;
        label.alignment = alignment;
        label.fontStyle = style;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.raycastTarget = false;
        return label;
    }

    /// <summary>Shrink long text down to 18 px (still readable) before it gets cut off.</summary>
    static void AutoSize(TextMeshProUGUI text, float maxSize)
    {
        text.enableAutoSizing = true;
        text.fontSizeMin = Mathf.Min(18f, maxSize);
        text.fontSizeMax = maxSize;
    }

    /// <summary>List cell: fixed width, or the remaining width when <paramref name="width"/> is 0.</summary>
    static TextMeshProUGUI Cell(Transform row, string name, string text, float width, TextAlignmentOptions alignment,
                                Color color, float size, FontStyles style = FontStyles.Normal)
    {
        TextMeshProUGUI label = NewText(row, name, text, size, color, alignment, style);
        AutoSize(label, size);
        if (width > 0f)
            SetLayout(label, prefWidth: width, flexWidth: 0f, minWidth: width);
        else
            SetLayout(label, prefWidth: 0f, flexWidth: 1f, minWidth: 0f);
        return label;
    }

    static Button NewButton(Transform parent, string name, string label, Color color, float fontSize = 20f)
    {
        RectTransform rect = NewRect(name, parent);
        Image image = AddImage(rect, color);
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.colors = Tint();
        button.navigation = new Navigation { mode = Navigation.Mode.None };

        TextMeshProUGUI text = NewText(rect, "Label", label, fontSize, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        Stretch(text.rectTransform, 6f, 6f, 0f, 0f);
        return button;
    }

    static TMP_InputField NewIntegerInput(Transform parent, string name, string placeholder, float size)
    {
        RectTransform rect = NewRect(name, parent);
        Image background = AddImage(rect, InputColor);

        RectTransform textArea = NewRect("Text Area", rect);
        Stretch(textArea, 12f, 12f, 4f, 4f);
        textArea.gameObject.AddComponent<RectMask2D>();

        TextMeshProUGUI hint = NewText(textArea, "Placeholder", placeholder, size, MutedColor, TextAlignmentOptions.Left, FontStyles.Italic);
        Stretch(hint.rectTransform);
        TextMeshProUGUI text = NewText(textArea, "Text", "", size, TextColor, TextAlignmentOptions.Left);
        text.overflowMode = TextOverflowModes.Overflow; // the input field scrolls long text itself
        Stretch(text.rectTransform);

        TMP_InputField input = rect.gameObject.AddComponent<TMP_InputField>();
        input.targetGraphic = background;
        input.colors = Tint();
        input.navigation = new Navigation { mode = Navigation.Mode.None };
        input.textViewport = textArea;
        input.textComponent = text;
        input.placeholder = hint;
        input.contentType = TMP_InputField.ContentType.IntegerNumber;
        input.characterLimit = 12;
        input.customCaretColor = true;
        input.caretColor = TextColor;
        input.caretWidth = 2;
        input.selectionColor = new Color(0.35f, 0.55f, 0.95f, 0.45f);
        return input;
    }

    static Scrollbar NewScrollbar(Transform scrollRoot)
    {
        RectTransform rect = NewRect("Scrollbar", scrollRoot);
        rect.anchorMin = new Vector2(1f, 0f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.sizeDelta = new Vector2(ScrollbarWidth, -8f);
        rect.anchoredPosition = new Vector2(-2f, 0f);
        AddImage(rect, new Color(0f, 0f, 0f, 0.3f));

        RectTransform slidingArea = NewRect("Sliding Area", rect);
        Stretch(slidingArea);
        RectTransform handle = NewRect("Handle", slidingArea);
        Stretch(handle);
        Image handleImage = AddImage(handle, new Color(0.45f, 0.49f, 0.56f, 1f));

        Scrollbar scrollbar = rect.gameObject.AddComponent<Scrollbar>();
        scrollbar.handleRect = handle;
        scrollbar.targetGraphic = handleImage;
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scrollbar.colors = Tint();
        scrollbar.navigation = new Navigation { mode = Navigation.Mode.None };
        return scrollbar;
    }

    /// <summary>Button tint: the base colour is the hover colour, normal is slightly dimmed (a tint can only darken).</summary>
    static ColorBlock Tint()
    {
        ColorBlock colors = ColorBlock.defaultColorBlock;
        colors.normalColor = new Color(0.82f, 0.82f, 0.82f, 1f);
        colors.highlightedColor = Color.white;
        colors.pressedColor = new Color(0.62f, 0.62f, 0.62f, 1f);
        colors.selectedColor = colors.normalColor; // don't stay lit after a click
        colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.6f);
        colors.fadeDuration = 0.08f;
        return colors;
    }
}
