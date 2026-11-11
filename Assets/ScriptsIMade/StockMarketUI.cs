using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Globalization;

/// <summary>
/// Stock market screen.
/// - Designed screen: <see cref="view"/> (StockMarketView, Assets/UI/Prefabs/StockMarket.prefab, installed by
///   StockMarketBuilder) draws everything from a StockScreenModel this class builds from the live market: the All /
///   Owned tabs, the chart, the order ticket with its Buy / Sell tabs, Max / All, recent trades.
/// - Designer mode: assign the serialized references (panel, list content, row prefab with StockItemUI, texts, buttons).
/// - Fallback mode: if stockMarketPanel is empty at Start, StockMarketFallbackUI builds a plain panel under the
///   HUD canvas and fills in these same fields, so everything else runs the same way in both modes.
/// The market is a live screen: it registers with UIManager (Escape closes it) but does not pause the game.
/// Keep this on an always-active object; StockMarketManager adds one to itself if the scene has none.
/// Texts come from the "Stocks" String Table.
/// </summary>
public class StockMarketUI : MonoBehaviour
{
    public static StockMarketUI Instance;

    /// <summary>HUD button used when openStockMarketButton is not assigned.</summary>
    public const string HudButtonName = "StockMarket";

    [Header("Designed screen")]
    [Tooltip("The designed market screen (StockMarketBuilder). When set, it is shown instead of the fallback panel.")]
    public StockMarketView view;

    [Header("Main Button")]
    [Tooltip("Opens the market. If empty, the Button named \"StockMarket\" on the HUD canvas is used.")]
    public Button openStockMarketButton;

    [Header("Panels")]
    [Tooltip("Root of the market screen. If empty, a fallback panel is built at runtime.")]
    public GameObject stockMarketPanel;
    public GameObject stockListContent;       // ScrollView content for stock list
    public GameObject stockDetailPanel;       // Panel showing selected stock details

    [Header("Stock Item Prefab")]
    public GameObject stockItemPrefab;        // Prefab for each stock row (needs StockItemUI)

    [Header("Portfolio Info")]
    public TextMeshProUGUI portfolioValueText;
    public TextMeshProUGUI cashAvailableText;
    public TextMeshProUGUI totalAssetsText;

    [Header("Stock Detail UI")]
    public TextMeshProUGUI detailSymbolText;
    public TextMeshProUGUI detailNameText;
    public TextMeshProUGUI detailPriceText;
    public TextMeshProUGUI detailChangeText;
    public TextMeshProUGUI detailOwnedText;
    public TextMeshProUGUI detailValueText;
    public TextMeshProUGUI detailTypeText;
    [Tooltip("Whole number of shares to trade.")]
    public TMP_InputField shareAmountInput;
    public Button buyButton;
    public Button sellButton;
    public Button buyMaxButton;
    public Button sellAllButton;
    [Tooltip("Preview of the trade for the entered amount.")]
    public TextMeshProUGUI tradeInfoText;
    [Tooltip("Result of the last trade. If empty, it is shown under the preview in Trade Info Text.")]
    public TextMeshProUGUI tradeMessageText;

    [Header("Close Button")]
    public Button closeButton;

    [Header("Access")]
    [Tooltip("Buying needs the Stock Market Access skill (Growth, tier 3). The market can always be viewed; shares already owned (older saves) can still be sold.")]
    public bool requireSkillToTrade = true;

    /// <summary>Shown while trading is locked: the "Stocks" table's stocks.msg.locked in the current language.</summary>
    public static string LockedMessage => LockedText;

    static string LockedText => L10n.Get(StockFormat.Table, "stocks.msg.locked");

    [Header("Colors")]
    public Color positiveColor = new Color(0.2f, 0.8f, 0.2f);
    public Color negativeColor = new Color(0.9f, 0.2f, 0.2f);
    public Color neutralColor = Color.white;

    const float LiveRefreshInterval = 0.5f; // cash changes between price ticks

    private Stock selectedStock;
    private readonly List<StockItemUI> stockItems = new List<StockItemUI>();
    private StockMarketManager subscribedMarket;
    private bool isOpen;
    private string tradeMessage = "";
    private bool tradeMessageIsError;
    private float liveRefreshTimer;
    private bool warnedRowSetup;

    // Designed screen state
    private bool sellMode;          // order ticket on Sell (only while the selected stock is owned)
    private bool showOwned;         // list on the Owned tab
    private bool viewDirty;
    private bool viewHooked;
    private readonly StockScreenModel model = new StockScreenModel();
    private readonly List<StockRowModel> rowModels = new List<StockRowModel>();
    private readonly List<StockTradeModel> tradeModels = new List<StockTradeModel>();

    /// <summary>True while the market screen is showing.</summary>
    public bool IsOpen => isOpen;

    /// <summary>True while buying is locked behind the Stock Market Access skill.</summary>
    public bool IsTradingLocked => requireSkillToTrade && !StockMarketManager.IsTradingUnlocked();

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        EnsurePanel();
        HookButtons();
        SubscribeToMarket();
        L10n.LanguageChanged += OnLanguageChanged;

        // Hide panel at start
        if (stockMarketPanel != null)
            stockMarketPanel.SetActive(false);
        if (stockDetailPanel != null)
            stockDetailPanel.SetActive(false);
    }

    void Update()
    {
        if (!isOpen) return;

        // Stay in step with UIManager if something else hid the panel or cleared the open panels
        bool visible = stockMarketPanel != null && stockMarketPanel.activeInHierarchy;
        bool registered = UIManager.Exists && UIManager.Instance.IsPanelOpen(this);
        if (!visible || !registered)
        {
            CloseStockMarket();
            return;
        }

        // Company value keeps changing between price ticks, so keep the money texts current
        liveRefreshTimer += Time.unscaledDeltaTime;
        if (liveRefreshTimer >= LiveRefreshInterval)
        {
            liveRefreshTimer = 0f;
            UpdatePortfolioInfo();
            UpdateTradeInfo();
        }

        if (viewDirty)
            RenderView();
    }

    void OnDestroy()
    {
        UnsubscribeFromMarket();
        L10n.LanguageChanged -= OnLanguageChanged;
        if (view != null)
            view.StockClicked -= OnViewStockClicked;
        if (isOpen && UIManager.Exists)
            UIManager.Instance.ClosePanel(this);
        if (Instance == this)
            Instance = null;
    }

    void OnLanguageChanged()
    {
        if (!isOpen) return;
        RefreshAll();
        RenderView();
    }

    // ------------------------------------------------------------------
    // Setup
    // ------------------------------------------------------------------

    /// <summary>Use the designed screen, else build the fallback panel. False if there is still no panel.</summary>
    bool EnsurePanel()
    {
        if (stockMarketPanel == null && view != null)
            stockMarketPanel = view.root != null ? view.root : view.gameObject;
        if (stockMarketPanel == null)
            StockMarketFallbackUI.Build(this, FindHudCanvas());
        return stockMarketPanel != null;
    }

    void HookButtons()
    {
        if (openStockMarketButton == null)
            openStockMarketButton = FindHudButton();

        Hook(openStockMarketButton, OpenStockMarket);
        Hook(closeButton, CloseStockMarket);
        Hook(buyButton, BuyShares);
        Hook(sellButton, SellShares);
        Hook(buyMaxButton, BuyMaxShares);
        Hook(sellAllButton, SellAllShares);
        HookView();

        if (shareAmountInput != null)
        {
            shareAmountInput.onValueChanged.RemoveListener(OnShareAmountChanged);
            shareAmountInput.onValueChanged.AddListener(OnShareAmountChanged);
        }
    }

    /// <summary>The designed screen's buttons, rows and amount field.</summary>
    void HookView()
    {
        if (view == null || viewHooked) return;
        viewHooked = true;
        if (shareAmountInput == null)
            shareAmountInput = view.amountInput;
        Hook(view.closeButton, CloseStockMarket);
        Hook(view.backButton, CloseStockMarket);
        Hook(view.allTab != null ? view.allTab.button : null, ShowAllStocks);
        Hook(view.ownedTab != null ? view.ownedTab.button : null, ShowOwnedStocks);
        Hook(view.buyTab != null ? view.buyTab.button : null, SetBuyMode);
        Hook(view.sellTab != null ? view.sellTab.button : null, SetSellMode);
        Hook(view.quickButton, QuickFill);
        Hook(view.lessButton, OneShareLess);
        Hook(view.moreButton, OneShareMore);
        Hook(view.actionButton, TradeFromTicket);
        Hook(view.openSkillsButton, OpenSkillBoard);
        view.StockClicked -= OnViewStockClicked;
        view.StockClicked += OnViewStockClicked;
    }

    static void Hook(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null) return;
        button.onClick.RemoveListener(action); // never add the same listener twice
        button.onClick.AddListener(action);
    }

    /// <summary>
    /// The root Screen Space Overlay canvas holding the HUD. World-space canvases are ignored; a canvas
    /// containing the "StockMarket" button, in this scene, with a CanvasScaler is preferred.
    /// </summary>
    Canvas FindHudCanvas()
    {
        if (openStockMarketButton != null)
        {
            Canvas parent = openStockMarketButton.GetComponentInParent<Canvas>(true);
            if (parent != null && parent.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
                return parent.rootCanvas;
        }

        Canvas best = null;
        int bestScore = -1;
        foreach (Canvas canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!canvas.isRootCanvas || canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;

            int score = 0;
            if (FindButtonNamed(canvas.transform, HudButtonName) != null) score += 4;
            if (canvas.gameObject.scene == gameObject.scene) score += 2;
            if (canvas.GetComponent<CanvasScaler>() != null) score += 1;
            if (score > bestScore)
            {
                best = canvas;
                bestScore = score;
            }
        }
        return best;
    }

    Button FindHudButton()
    {
        Canvas canvas = FindHudCanvas();
        return canvas != null ? FindButtonNamed(canvas.transform, HudButtonName) : null;
    }

    static Button FindButtonNamed(Transform root, string buttonName)
    {
        foreach (Button button in root.GetComponentsInChildren<Button>(true))
        {
            if (button.name == buttonName)
                return button;
        }
        return null;
    }

    void SubscribeToMarket()
    {
        StockMarketManager market = StockMarketManager.Instance;
        if (market == subscribedMarket) return;

        UnsubscribeFromMarket();
        if (market != null)
        {
            market.StockPricesUpdated += RefreshUI;
            subscribedMarket = market;
        }
    }

    void UnsubscribeFromMarket()
    {
        if (subscribedMarket != null)
            subscribedMarket.StockPricesUpdated -= RefreshUI;
        subscribedMarket = null;
    }

    // ------------------------------------------------------------------
    // Open / close
    // ------------------------------------------------------------------

    public void OpenStockMarket()
    {
        // Tutorial: the market opens at FeatureGate.StocksAtValue (every way in, not just the HUD's tab)
        if (!FeatureGate.AllowOpen(Feature.Stocks)) return;
        if (!EnsurePanel())
        {
            Debug.LogWarning("[StockMarketUI] There is no stock market panel to open.");
            return;
        }
        SubscribeToMarket();

        isOpen = true;
        liveRefreshTimer = 0f;
        stockMarketPanel.SetActive(true);
        UIManager.BringToFront(stockMarketPanel);

        // Live screen: counts as an open panel (Escape closes it) but prices keep moving
        UIManager ui = UIManager.Instance;
        if (ui != null)
            ui.OpenPanel(this, false, CloseStockMarket);

        tradeMessage = "";
        PopulateStockList();

        // Keep the previous selection if it is still listed, otherwise select the first stock
        StockMarketManager market = StockMarketManager.Instance;
        Stock toSelect = null;
        if (market != null)
        {
            if (selectedStock != null && market.GetStock(selectedStock.symbol) == selectedStock)
                toSelect = selectedStock;
            else
                toSelect = market.stocks.Find(s => s != null);
        }

        if (toSelect != null)
        {
            SelectStock(toSelect);
        }
        else
        {
            selectedStock = null;
            if (stockDetailPanel != null)
                stockDetailPanel.SetActive(false);
        }

        RefreshAll();
        RenderView();
    }

    public void CloseStockMarket()
    {
        isOpen = false;
        if (stockMarketPanel != null)
            stockMarketPanel.SetActive(false);
        if (UIManager.Exists)
            UIManager.Instance.ClosePanel(this);
    }

    // ------------------------------------------------------------------
    // List
    // ------------------------------------------------------------------

    void PopulateStockList()
    {
        if (view != null) return;   // the designed screen draws its own rows
        StockMarketManager market = StockMarketManager.Instance;
        if (market == null || stockItemPrefab == null || stockListContent == null)
        {
            if (!warnedRowSetup)
            {
                warnedRowSetup = true;
                Debug.LogWarning("[StockMarketUI] Stock list needs StockMarketManager, stockItemPrefab and stockListContent.");
            }
            return;
        }

        // Rows are only rebuilt when the listed stocks changed
        if (RowsMatch(market.stocks))
            return;

        ClearRows();
        foreach (var stock in market.stocks)
        {
            if (stock == null) continue;

            GameObject item = Instantiate(stockItemPrefab, stockListContent.transform, false);
            item.name = "Stock_" + stock.symbol;
            item.SetActive(true); // the runtime row template is kept inactive

            StockItemUI itemUI = item.GetComponent<StockItemUI>();
            if (itemUI == null)
            {
                Debug.LogWarning("[StockMarketUI] stockItemPrefab has no StockItemUI component.");
                Destroy(item);
                return;
            }

            itemUI.Setup(stock, this);
            stockItems.Add(itemUI);
        }
    }

    bool RowsMatch(List<Stock> stocks)
    {
        int index = 0;
        foreach (var stock in stocks)
        {
            if (stock == null) continue;
            if (index >= stockItems.Count || stockItems[index] == null || stockItems[index].Stock != stock)
                return false;
            index++;
        }
        return index == stockItems.Count;
    }

    void ClearRows()
    {
        foreach (var item in stockItems)
        {
            if (item != null)
                Destroy(item.gameObject);
        }
        stockItems.Clear();
    }

    /// <summary>Called on every price update. Only does work while the market is open.</summary>
    public void RefreshUI()
    {
        if (isOpen)
            RefreshAll();
    }

    void RefreshAll()
    {
        foreach (var item in stockItems)
        {
            if (item != null)
                item.Refresh();
        }

        UpdatePortfolioInfo();

        if (selectedStock != null)
            UpdateDetailPanel();
        else
            UpdateTradeInfo();
        MarkView();
    }

    void UpdatePortfolioInfo()
    {
        StockMarketManager market = StockMarketManager.Instance;
        double portfolioValue = market != null ? market.GetTotalPortfolioValue() : 0.0;
        double cash = StockMarketManager.GetCash();
        double totalAssets = portfolioValue + cash;

        if (portfolioValueText != null)
            portfolioValueText.text = "Portfolio: " + FormatMoney(portfolioValue);
        if (cashAvailableText != null)
            cashAvailableText.text = "Cash: " + FormatMoney(cash);
        if (totalAssetsText != null)
            totalAssetsText.text = "Total: " + FormatMoney(totalAssets);
    }

    // ------------------------------------------------------------------
    // Detail panel
    // ------------------------------------------------------------------

    public void SelectStock(Stock stock)
    {
        if (stock == null) return;
        selectedStock = stock;

        if (stockDetailPanel != null)
            stockDetailPanel.SetActive(true);

        // Keep a typed amount when switching stocks; start at 1 otherwise
        if (shareAmountInput != null && GetShareAmount() <= 0)
            shareAmountInput.SetTextWithoutNotify("1");

        foreach (var item in stockItems)
        {
            if (item != null)
                item.SetSelected(item.Stock == stock);
        }

        // The ticket stays on Sell only for a stock you own
        if (stock.ownedShares <= 0)
            sellMode = false;

        tradeMessage = "";
        UpdateDetailPanel();
        MarkView();
    }

    void UpdateDetailPanel()
    {
        if (selectedStock == null) return;
        Stock stock = selectedStock;

        if (detailSymbolText != null)
            detailSymbolText.text = stock.symbol;

        if (detailNameText != null)
            detailNameText.text = stock.companyName;

        if (detailPriceText != null)
            detailPriceText.text = FormatMoney(stock.currentPrice);

        if (detailChangeText != null)
        {
            float change = stock.GetWindowChangePercent();
            detailChangeText.text = StockFormat.Percent(change);
            detailChangeText.color = change > 0 ? positiveColor : (change < 0 ? negativeColor : neutralColor);
        }

        if (detailOwnedText != null)
        {
            detailOwnedText.text = stock.ownedShares > 0
                ? "Owned: " + stock.ownedShares.ToString("N0") + " @ avg " + FormatMoney(stock.averageBuyPrice)
                : "Owned: none";
        }

        if (detailValueText != null)
        {
            string value = "Value: " + FormatMoney(stock.GetTotalValue());
            if (stock.ownedShares > 0 && stock.averageBuyPrice > 0f)
            {
                double profit = stock.GetProfitLoss();
                double percent = (stock.currentPrice / (double)stock.averageBuyPrice - 1.0) * 100.0;
                string profitText = FormatSignedMoney(profit) + " (" + FormatPercent(percent, true) + ")";
                value += "\nP/L " + Colorize(profitText, profit >= 0 ? positiveColor : negativeColor);
            }
            detailValueText.text = value;
        }

        if (detailTypeText != null)
            detailTypeText.text = StockFormat.TypeName(stock.stockType);

        UpdateTradeInfo();
    }

    void UpdateTradeInfo()
    {
        UpdateTradeButtons();

        string preview = BuildTradePreview();
        string message = tradeMessage;
        bool messageIsError = tradeMessageIsError;
        if (string.IsNullOrEmpty(message) && IsTradingLocked)
        {
            message = LockedText;
            messageIsError = true;
        }
        MarkView();
        Color messageColor = messageIsError ? negativeColor : positiveColor;

        if (tradeMessageText != null)
        {
            if (tradeInfoText != null)
                tradeInfoText.text = preview;
            tradeMessageText.text = message;
            tradeMessageText.color = messageColor;
        }
        else if (tradeInfoText != null)
        {
            tradeInfoText.text = string.IsNullOrEmpty(message)
                ? preview
                : preview + "\n" + Colorize(message, messageColor);
        }
    }

    /// <summary>Buy buttons are off while trading is locked; selling stays possible for shares already owned.</summary>
    void UpdateTradeButtons()
    {
        bool locked = IsTradingLocked;
        bool ownsShares = selectedStock != null && selectedStock.ownedShares > 0;
        SetInteractable(buyButton, !locked);
        SetInteractable(buyMaxButton, !locked);
        SetInteractable(sellButton, !locked || ownsShares);
        SetInteractable(sellAllButton, !locked || ownsShares);
    }

    static void SetInteractable(Button button, bool interactable)
    {
        if (button != null && button.interactable != interactable)
            button.interactable = interactable;
    }

    string BuildTradePreview()
    {
        StockMarketManager market = StockMarketManager.Instance;
        if (selectedStock == null || market == null) return "";

        long shares = GetShareAmount();
        if (shares <= 0)
            return Msg("stocks.msg.amount");   // "Enter a whole number of shares (1 or more)."

        string buyLine = "Buy " + shares.ToString("N0") + ": " + FormatMoney(market.GetBuyCost(selectedStock, shares));
        string sellLine = selectedStock.ownedShares >= shares
            ? "Sell " + shares.ToString("N0") + ": " + FormatMoney(market.GetSellProceeds(selectedStock, shares))
            : "Sell: you own " + selectedStock.ownedShares.ToString("N0");
        string feeLine = (market.FeeRate * 100f).ToString("0.##") + "% fee included. Max buy: "
                       + market.GetMaxAffordableShares(selectedStock).ToString("N0");
        return buyLine + "\n" + sellLine + "\n" + feeLine;
    }

    /// <summary>Shares typed in the amount field, or 0 when it isn't a positive whole number.</summary>
    long GetShareAmount()
    {
        if (shareAmountInput == null) return 1;

        string raw = shareAmountInput.text;
        if (string.IsNullOrWhiteSpace(raw)) return 0;

        long amount;
        if (long.TryParse(raw.Trim(), NumberStyles.Integer | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out amount) && amount > 0)
            return amount;
        return 0;
    }

    void SetShareAmount(long shares)
    {
        if (shareAmountInput != null)
            shareAmountInput.SetTextWithoutNotify(shares.ToString("N0", CultureInfo.InvariantCulture));   // "2,000"; typed digits still parse
    }

    void OnShareAmountChanged(string _)
    {
        tradeMessage = ""; // the last result no longer applies
        UpdateTradeInfo();
    }

    // ------------------------------------------------------------------
    // Designed screen
    // ------------------------------------------------------------------

    void MarkView()
    {
        if (view != null) viewDirty = true;
    }

    /// <summary>Draw the designed screen from the live market now.</summary>
    void RenderView()
    {
        viewDirty = false;
        if (view == null || !isOpen) return;
        view.Render(BuildModel());
    }

    /// <summary>The designed screen's model, from the market, the company and this screen's state.</summary>
    public StockScreenModel BuildModel()
    {
        StockMarketManager market = StockMarketManager.Instance;
        model.day = CompanyManager.Instance != null ? CompanyManager.Instance.currentDay : 1;
        model.feePercent = market != null ? market.FeeRate * 100f : 1f;
        model.cash = StockMarketManager.GetCash();
        model.showOwned = showOwned;

        model.rows.Clear();
        int i = 0;
        if (market != null)
        {
            foreach (var stock in market.stocks)
            {
                if (stock == null) continue;
                if (i >= rowModels.Count) rowModels.Add(new StockRowModel());
                var r = rowModels[i++];
                r.symbol = stock.symbol;
                r.company = stock.companyName;
                r.type = stock.stockType;
                r.price = stock.currentPrice;
                r.change = stock.GetWindowChangePercent();
                r.owned = stock.ownedShares;
                r.averagePrice = stock.averageBuyPrice;
                model.rows.Add(r);
            }
        }

        model.trades.Clear();
        if (market != null)
        {
            int t = 0;
            foreach (var trade in market.RecentTrades)
            {
                if (trade == null) continue;
                if (t >= tradeModels.Count) tradeModels.Add(new StockTradeModel());
                var m = tradeModels[t++];
                m.day = trade.day;
                m.buy = trade.buy;
                m.symbol = trade.symbol;
                m.shares = trade.shares;
                m.price = trade.price;
                m.cash = trade.cash;
                m.profit = trade.profit;
                model.trades.Add(m);
            }
        }

        model.selected = selectedStock != null ? selectedStock.symbol : null;
        model.history.Clear();
        if (selectedStock != null)
            selectedStock.CopyHistory(model.history);
        model.locked = IsTradingLocked;
        model.sellMode = sellMode && selectedStock != null && selectedStock.ownedShares > 0;
        model.amount = GetShareAmount();
        model.maxBuy = market != null && selectedStock != null ? market.GetMaxAffordableShares(selectedStock) : 0;
        model.message = tradeMessage ?? "";
        model.messageIsError = tradeMessageIsError;
        return model;
    }

    void OnViewStockClicked(string symbol)
    {
        StockMarketManager market = StockMarketManager.Instance;
        Stock stock = market != null ? market.GetStock(symbol) : null;
        if (stock != null)
            SelectStock(stock);
    }

    /// <summary>List tab: every stock.</summary>
    public void ShowAllStocks()
    {
        showOwned = false;
        MarkView();
    }

    /// <summary>List tab: the stocks you own, with profits and recent trades.</summary>
    public void ShowOwnedStocks()
    {
        showOwned = true;
        MarkView();
    }

    /// <summary>Order ticket tab: Buy (shows why when buying is locked).</summary>
    public void SetBuyMode()
    {
        sellMode = false;
        tradeMessage = "";
        UpdateTradeInfo();
    }

    /// <summary>Order ticket tab: Sell (only for a stock you own).</summary>
    public void SetSellMode()
    {
        if (selectedStock == null || selectedStock.ownedShares <= 0) return;
        sellMode = true;
        tradeMessage = "";
        UpdateTradeInfo();
    }

    /// <summary>Max (buy: the most the cash buys) or All (sell: every share you own) fills in the amount.</summary>
    void QuickFill()
    {
        StockMarketManager market = StockMarketManager.Instance;
        if (selectedStock == null || market == null) return;
        tradeMessage = "";
        if (sellMode && selectedStock.ownedShares > 0)
        {
            SetShareAmount(selectedStock.ownedShares);
        }
        else
        {
            long max = market.GetMaxAffordableShares(selectedStock);
            if (max > 0)
                SetShareAmount(max);
            else
            {
                ShowTradeMessage(L10n.Get(StockFormat.Table, "stocks.msg.no_money_one", ("symbol", selectedStock.symbol),
                    ("cost", FormatMoney(market.GetBuyCost(selectedStock, 1)))), true);
                return;
            }
        }
        UpdateTradeInfo();
    }

    void OneShareLess() => StepAmount(-1);

    void OneShareMore() => StepAmount(1);

    void StepAmount(long delta)
    {
        long amount = GetShareAmount();
        SetShareAmount(System.Math.Max(1L, System.Math.Min(StockMarketManager.MaxSharesPerTrade, (amount <= 0 ? 0 : amount) + delta)));
        tradeMessage = "";
        UpdateTradeInfo();
    }

    /// <summary>The ticket's big button: buy or sell the typed amount.</summary>
    void TradeFromTicket()
    {
        bool sell = sellMode && selectedStock != null && selectedStock.ownedShares > 0;
        Trade(!sell, GetShareAmount());
        if (selectedStock != null && selectedStock.ownedShares <= 0)
            sellMode = false;
    }

    /// <summary>Close the market and open the skill board (Stock Market Access is on it).</summary>
    void OpenSkillBoard()
    {
        CloseStockMarket();
        var hud = FindAnyObjectByType<OfficeHud>();
        if (hud != null)
            hud.OpenSkills();
    }

    // ------------------------------------------------------------------
    // Trading
    // ------------------------------------------------------------------

    void BuyShares()
    {
        Trade(true, GetShareAmount());
    }

    void SellShares()
    {
        Trade(false, GetShareAmount());
    }

    void BuyMaxShares()
    {
        if (IsTradingLocked)
        {
            ShowTradeMessage(LockedText, true);
            return;
        }

        StockMarketManager market = StockMarketManager.Instance;
        if (selectedStock == null || market == null)
        {
            Trade(true, 0); // reports what is missing
            return;
        }

        long maxShares = market.GetMaxAffordableShares(selectedStock);
        if (maxShares <= 0)
        {
            ShowTradeMessage(Msg("stocks.msg.no_money_one", ("symbol", selectedStock.symbol),
                ("cost", FormatMoney(market.GetBuyCost(selectedStock, 1)))), true);
            return;
        }

        SetShareAmount(maxShares);
        Trade(true, maxShares);
    }

    void SellAllShares()
    {
        if (selectedStock == null)
        {
            ShowTradeMessage(Msg("stocks.msg.select"), true);
            return;
        }
        if (selectedStock.ownedShares <= 0)
        {
            ShowTradeMessage(Msg("stocks.detail.none", ("symbol", selectedStock.symbol)), true);
            return;
        }

        long owned = selectedStock.ownedShares;
        SetShareAmount(owned);
        Trade(false, owned);
    }

    void Trade(bool buy, long shares)
    {
        StockMarketManager market = StockMarketManager.Instance;
        if (selectedStock == null)
        {
            ShowTradeMessage(Msg("stocks.msg.select"), true);
            return;
        }
        if (market == null)
        {
            ShowTradeMessage(Msg("stocks.msg.unavailable"), true);
            return;
        }
        if (IsTradingLocked && (buy || selectedStock.ownedShares <= 0))
        {
            ShowTradeMessage(LockedText, true);
            return;
        }

        double amount;
        StockMarketManager.TradeResult result = buy
            ? market.TryBuyStock(selectedStock.symbol, shares, out amount)
            : market.TrySellStock(selectedStock.symbol, shares, out amount);

        ShowTradeMessage(DescribeTrade(result, buy, shares, amount), result != StockMarketManager.TradeResult.Success);
        RefreshAll();
    }

    string DescribeTrade(StockMarketManager.TradeResult result, bool buy, long shares, double amount)
    {
        string symbol = selectedStock != null ? selectedStock.symbol : "";
        string count = shares.ToString("N0", CultureInfo.InvariantCulture);
        switch (result)
        {
            case StockMarketManager.TradeResult.Success:
                return Msg(buy ? "stocks.msg.bought" : "stocks.msg.sold", ("count", count), ("symbol", symbol), ("amount", FormatMoney(amount)));
            case StockMarketManager.TradeResult.InvalidAmount:
                return Msg("stocks.msg.amount");
            case StockMarketManager.TradeResult.NotEnoughMoney:
                return Msg("stocks.msg.no_money", ("need", FormatMoney(amount)), ("have", FormatMoney(StockMarketManager.GetCash())));
            case StockMarketManager.TradeResult.NotEnoughShares:
                return Msg("stocks.msg.no_shares", ("count", (selectedStock != null ? selectedStock.ownedShares : 0).ToString("N0", CultureInfo.InvariantCulture)),
                    ("symbol", symbol));
            case StockMarketManager.TradeResult.NoCompany:
                return Msg("stocks.msg.no_company");
            case StockMarketManager.TradeResult.UnknownStock:
                return Msg("stocks.msg.delisted", ("symbol", symbol));
            default:
                return Msg("stocks.msg.failed");
        }
    }

    static string Msg(string key) => L10n.Get(StockFormat.Table, key);

    static string Msg(string key, params (string name, object value)[] args) => L10n.Get(StockFormat.Table, key, args);

    void ShowTradeMessage(string message, bool isError)
    {
        tradeMessage = message ?? "";
        tradeMessageIsError = isError;
        UpdateTradeInfo();
    }

    // ------------------------------------------------------------------
    // Formatting
    // ------------------------------------------------------------------

    static string FormatMoney(double value) => StockFormat.Money(value);

    static string FormatSignedMoney(double value)
    {
        return (value >= 0 ? "+" : "-") + FormatMoney(System.Math.Abs(value));
    }

    static string FormatPercent(double value, bool showPlus)
    {
        return (showPlus && value > 0 ? "+" : "") + value.ToString("N2") + "%";
    }

    static string Colorize(string text, Color color)
    {
        return "<color=#" + ColorUtility.ToHtmlStringRGB(color) + ">" + text + "</color>";
    }
}
