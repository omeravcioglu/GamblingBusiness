using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Simulated stock market.
/// Prices update every priceUpdateInterval seconds of game (scaled) time, so they freeze while a
/// modal panel pauses the game. Trades move money in and out of CompanyManager.companyValue.
/// Prices and holdings are saved through ISaveParticipant (key "stocks").
/// </summary>
public class StockMarketManager : MonoBehaviour, ISaveParticipant
{
    public static StockMarketManager Instance;

    public enum TradeResult
    {
        Success,
        InvalidAmount,   // not a positive whole number of shares
        UnknownStock,
        NoCompany,       // no CompanyManager in this scene
        NotEnoughMoney,
        NotEnoughShares
    }

    /// <summary>Largest number of shares in one trade (keeps share math exact in doubles).</summary>
    public const long MaxSharesPerTrade = 1000000000000L;

    [Header("Stock List")]
    [Tooltip("Filled from code in Awake (InitializeStocks). Entries added in the Inspector are replaced.")]
    public List<Stock> stocks = new List<Stock>();

    [Header("Market Settings")]
    [Tooltip("Seconds of game time between price updates. Uses scaled time, so prices freeze while the game is paused.")]
    public float priceUpdateInterval = 5f; // Update prices every 5 seconds
    private float updateTimer;

    [Header("Trading")]
    public float transactionFeePercent = 0.01f; // 1% fee on trades

    // Market Manipulation skill (value 0.15): the fair value of stocks you own drifts up (+6% per hour of play)
    // and trading fees are halved
    const float ManipulationDriftPerHourPerEffect = 0.4f;
    const float MaxManipulationDriftPerHour = 0.1f;
    const float ManipulationFeeFactor = 0.5f;

    // Events
    public delegate void OnStockPricesUpdated();
    public event OnStockPricesUpdated StockPricesUpdated;

    public delegate void OnTradeCompleted(string symbol, long shares, bool isBuy);
    public event OnTradeCompleted TradeCompleted;

    /// <summary>
    /// A sale went through, for the cloud save's ledger (CloudLedger): (symbol, shares sold, proceeds after the fee, cost
    /// basis of those shares = average buy price x shares, buy fees not included). Static, so listeners outlive the scene.
    /// </summary>
    public static event Action<string, long, double, double> StockSold;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        // Entering Play mode without a domain reload keeps statics
        StockSold = null;
    }

    /// <summary>One trade, for the market screen's "Recent trades" (saved with the market).</summary>
    [Serializable]
    public class TradeRecord
    {
        public int day;
        public bool buy;
        public string symbol;
        public long shares;
        public float price;     // per share, fee not included
        public double cash;     // buy: total paid, fee included; sell: money received after the fee
        public double profit;   // sell: cash minus the cost basis of the shares sold; buy: 0
    }

    /// <summary>How many trades the market remembers.</summary>
    public const int MaxTradeRecords = 20;
    readonly List<TradeRecord> tradeRecords = new List<TradeRecord>();

    /// <summary>The last trades, newest first.</summary>
    public IReadOnlyList<TradeRecord> RecentTrades => tradeRecords;

    void RecordTrade(bool buy, Stock stock, long shares, float price, double cash, double profit)
    {
        tradeRecords.Insert(0, new TradeRecord
        {
            day = CompanyManager.Instance != null ? CompanyManager.Instance.currentDay : 0,
            buy = buy, symbol = stock.symbol, shares = shares, price = price, cash = cash, profit = profit
        });
        if (tradeRecords.Count > MaxTradeRecords)
            tradeRecords.RemoveRange(MaxTradeRecords, tradeRecords.Count - MaxTradeRecords);
    }

    private bool initialized;

    void Awake()
    {
        Instance = this;
        EnsureInitialized();
    }

    void Start()
    {
        // The market screen: use the scene's StockMarketUI if there is one, otherwise add one here.
        // (It builds a fallback panel at runtime when no designed panel is assigned.)
        if (FindAnyObjectByType<StockMarketUI>(FindObjectsInactive.Include) == null)
            gameObject.AddComponent<StockMarketUI>();
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void EnsureInitialized()
    {
        if (initialized) return;
        initialized = true;
        InitializeStocks();
    }

    // Code is the source of truth for the listed stocks.
    // volatility = typical move per 5 s tick (0.004 = 0.4%)
    // trend      = extra drift of the base price per hour of play (on top of the type's drift)
    // Per-type behaviour (drift, mean reversion, jumps) is in Stock.cs.
    void InitializeStocks()
    {
        stocks.Clear();

        // Tech stocks
        stocks.Add(new Stock("AAPL", "Apple Corp", 150f, 0.0035f, 0f, StockType.Stable));
        stocks.Add(new Stock("GOOG", "Goggle Inc", 2800f, 0.0055f, 0f, StockType.Normal));
        stocks.Add(new Stock("MSFT", "Macrosoft", 300f, 0.0035f, 0f, StockType.Stable));
        stocks.Add(new Stock("AMZN", "Amazon River", 3300f, 0.006f, 0f, StockType.Normal));
        stocks.Add(new Stock("TSLA", "Teslo Motors", 900f, 0.014f, 0f, StockType.Volatile));
        stocks.Add(new Stock("NVDA", "Nvidio Graphics", 250f, 0.012f, 0.005f, StockType.Volatile));

        // Meme stocks
        stocks.Add(new Stock("GME", "GameStomp", 180f, 0.025f, 0f, StockType.Meme));
        stocks.Add(new Stock("AMC", "AMC Cinemas", 40f, 0.022f, -0.005f, StockType.Meme));
        stocks.Add(new Stock("DOGE", "DogeCorp", 4.20f, 0.03f, 0f, StockType.Meme));
        stocks.Add(new Stock("MOON", "ToTheMoon Inc", 5f, 0.035f, 0.01f, StockType.Meme));

        // Dividend/Safe stocks
        stocks.Add(new Stock("KO", "Coca-Coola", 55f, 0.0025f, 0f, StockType.Dividend));
        stocks.Add(new Stock("JNJ", "Johnson Bros", 165f, 0.0025f, 0f, StockType.Dividend));
        stocks.Add(new Stock("PG", "Proctor Gambler", 140f, 0.0025f, 0f, StockType.Dividend));
        stocks.Add(new Stock("WMT", "WallMart", 145f, 0.0025f, 0f, StockType.Dividend));

        // Risky stocks
        stocks.Add(new Stock("SPCE", "Space Virgin", 25f, 0.018f, -0.005f, StockType.Volatile));
        stocks.Add(new Stock("PLTR", "Palintear", 22f, 0.014f, 0f, StockType.Volatile));
        stocks.Add(new Stock("COIN", "CoinBased", 250f, 0.02f, 0f, StockType.Volatile));
        stocks.Add(new Stock("ARKK", "ARK Invest", 120f, 0.012f, 0f, StockType.Volatile));

        // Cheap penny stocks
        stocks.Add(new Stock("WISH", "WishDotCom", 3f, 0.025f, -0.01f, StockType.Meme));
        stocks.Add(new Stock("CLOV", "CloverLeaf", 8f, 0.016f, -0.005f, StockType.Volatile));
    }

    void Update()
    {
        // Scaled time: the market freezes while a modal panel pauses the game
        float interval = Mathf.Max(0.5f, priceUpdateInterval);
        updateTimer += Time.deltaTime;
        if (updateTimer >= interval)
        {
            updateTimer -= interval;
            if (updateTimer >= interval) updateTimer = 0f; // no burst of ticks after a long frame
            UpdateAllPrices(interval);
        }
    }

    void UpdateAllPrices(float seconds)
    {
        // Market Manipulation: stocks the company owns tend to rise
        float ownerDrift = GetManipulationDriftPerHour() * (seconds / 3600f);

        foreach (var stock in stocks)
        {
            if (stock == null) continue;
            stock.UpdatePrice(seconds);
            if (ownerDrift > 0f && stock.ownedShares > 0)
                stock.NudgeBasePrice(ownerDrift);
        }

        StockPricesUpdated?.Invoke();
    }

    /// <summary>
    /// True when the company may buy stocks: the Stock Market Access skill (Growth, tier 3) is unlocked.
    /// Also true in scenes without a skill tree.
    /// </summary>
    public static bool IsTradingUnlocked()
    {
        return SkillTreeManager.Instance == null || SkillTreeManager.Instance.IsStockMarketUnlocked();
    }

    static bool HasMarketManipulation()
    {
        return SkillTreeManager.Instance != null && SkillTreeManager.Instance.HasEffect(SkillEffect.MarketManipulation);
    }

    /// <summary>Market Manipulation: upward drift of owned stocks' fair value per hour of play (0.06 = +6%).</summary>
    public static float GetManipulationDriftPerHour()
    {
        if (!HasMarketManipulation()) return 0f;
        float effect = SkillTreeManager.Instance.GetEffect(SkillEffect.MarketManipulation);
        return Mathf.Clamp(effect * ManipulationDriftPerHourPerEffect, 0f, MaxManipulationDriftPerHour);
    }

    // ------------------------------------------------------------------
    // Money helpers
    // ------------------------------------------------------------------

    /// <summary>Money the company can spend (its value). 0 when no company is loaded.</summary>
    public static double GetCash()
    {
        if (CompanyManager.Instance == null) return 0.0;
        return Math.Max(0.0, CompanyManager.Instance.companyValue);
    }

    /// <summary>Fee rate actually used (Inspector value clamped to 0-50%, halved by Market Manipulation).</summary>
    public float FeeRate
    {
        get
        {
            float fee = Mathf.Clamp(transactionFeePercent, 0f, 0.5f);
            return HasMarketManipulation() ? fee * ManipulationFeeFactor : fee;
        }
    }

    /// <summary>Total price of buying <paramref name="shares"/>, fee included.</summary>
    public double GetBuyCost(Stock stock, long shares)
    {
        if (stock == null || shares <= 0) return 0.0;
        double cost = (double)stock.currentPrice * shares;
        return cost + cost * FeeRate;
    }

    /// <summary>Money received for selling <paramref name="shares"/>, after the fee.</summary>
    public double GetSellProceeds(Stock stock, long shares)
    {
        if (stock == null || shares <= 0) return 0.0;
        double value = (double)stock.currentPrice * shares;
        return value - value * FeeRate;
    }

    /// <summary>Most shares of <paramref name="stock"/> the company can afford right now (fee included).</summary>
    public long GetMaxAffordableShares(Stock stock)
    {
        if (stock == null || stock.currentPrice <= 0f) return 0;
        double cash = GetCash();
        double max = Math.Floor(cash / ((double)stock.currentPrice * (1.0 + FeeRate)));
        if (double.IsNaN(max) || max < 1.0) return 0;

        long shares = max >= MaxSharesPerTrade ? MaxSharesPerTrade : (long)max;
        while (shares > 0 && GetBuyCost(stock, shares) > cash)
            shares--; // floating point rounding guard
        return shares;
    }

    // ------------------------------------------------------------------
    // Trading
    // ------------------------------------------------------------------

    /// <summary>Buy shares. <paramref name="totalCost"/> is the price including the fee (also set when there isn't enough money).</summary>
    public TradeResult TryBuyStock(string symbol, long shares, out double totalCost)
    {
        totalCost = 0.0;
        if (shares <= 0 || shares > MaxSharesPerTrade) return TradeResult.InvalidAmount;

        Stock stock = GetStock(symbol);
        if (stock == null) return TradeResult.UnknownStock;

        CompanyManager company = CompanyManager.Instance;
        if (company == null) return TradeResult.NoCompany;

        totalCost = GetBuyCost(stock, shares);
        if (GetCash() < totalCost) return TradeResult.NotEnoughMoney;

        company.AddMoney(-totalCost, true);
        stock.RecordPurchase(shares, stock.currentPrice);
        RecordTrade(true, stock, shares, stock.currentPrice, totalCost, 0.0);

        TradeCompleted?.Invoke(stock.symbol, shares, true);
        return TradeResult.Success;
    }

    /// <summary>Sell owned shares. <paramref name="proceeds"/> is the money received after the fee.</summary>
    public TradeResult TrySellStock(string symbol, long shares, out double proceeds)
    {
        proceeds = 0.0;
        if (shares <= 0 || shares > MaxSharesPerTrade) return TradeResult.InvalidAmount;

        Stock stock = GetStock(symbol);
        if (stock == null) return TradeResult.UnknownStock;

        CompanyManager company = CompanyManager.Instance;
        if (company == null) return TradeResult.NoCompany;

        if (stock.ownedShares < shares) return TradeResult.NotEnoughShares;

        proceeds = GetSellProceeds(stock, shares);
        double costBasis = (double)stock.averageBuyPrice * shares; // before RecordSale, which clears the average when all are sold
        company.AddMoney(proceeds, true);
        stock.RecordSale(shares);
        RecordTrade(false, stock, shares, stock.currentPrice, proceeds, proceeds - costBasis);

        TradeCompleted?.Invoke(stock.symbol, shares, false);
        ReportSale(stock.symbol, shares, proceeds, costBasis);
        return TradeResult.Success;
    }

    static void ReportSale(string symbol, long shares, double proceeds, double costBasis)
    {
        Action<string, long, double, double> handlers = StockSold;
        if (handlers == null) return;
        try { handlers(symbol, shares, proceeds, costBasis); }
        catch (Exception e) { Debug.LogException(e); }
    }

    public bool BuyStock(string symbol, long shares)
    {
        return TryBuyStock(symbol, shares, out _) == TradeResult.Success;
    }

    public bool SellStock(string symbol, long shares)
    {
        return TrySellStock(symbol, shares, out _) == TradeResult.Success;
    }

    public Stock GetStock(string symbol)
    {
        if (string.IsNullOrEmpty(symbol)) return null;
        return stocks.Find(s => s != null && s.symbol == symbol);
    }

    public double GetTotalPortfolioValue()
    {
        double total = 0.0;
        foreach (var stock in stocks)
        {
            if (stock != null)
                total += stock.GetTotalValue();
        }
        return total;
    }

    public int GetTotalOwnedStocks()
    {
        int count = 0;
        foreach (var stock in stocks)
        {
            if (stock != null && stock.ownedShares > 0)
                count++;
        }
        return count;
    }

    // Force price update (for testing)
    public void ForceUpdate()
    {
        UpdateAllPrices(Mathf.Max(0.5f, priceUpdateInterval));
    }

    // ------------------------------------------------------------------
    // Save / load (ISaveParticipant)
    // ------------------------------------------------------------------

    [Serializable]
    public class StockSaveEntry
    {
        public string symbol;
        public float currentPrice;
        public float previousPrice;
        public float basePrice;
        public long ownedShares;
        public float averageBuyPrice;
    }

    [Serializable]
    public class StockMarketSaveData
    {
        public int version = 2;   // 2: + trades
        public List<StockSaveEntry> stocks = new List<StockSaveEntry>();
        public List<TradeRecord> trades = new List<TradeRecord>();
    }

    public string SaveKey => "stocks";

    public string CaptureState()
    {
        EnsureInitialized();
        var data = new StockMarketSaveData();
        foreach (var stock in stocks)
        {
            if (stock == null || string.IsNullOrEmpty(stock.symbol)) continue;
            data.stocks.Add(new StockSaveEntry
            {
                symbol = stock.symbol,
                currentPrice = stock.currentPrice,
                previousPrice = stock.previousPrice,
                basePrice = stock.basePrice,
                ownedShares = stock.ownedShares,
                averageBuyPrice = stock.averageBuyPrice
            });
        }
        data.trades.AddRange(tradeRecords);
        return JsonUtility.ToJson(data);
    }

    public void RestoreState(string json)
    {
        EnsureInitialized();
        if (string.IsNullOrEmpty(json)) return;

        StockMarketSaveData data;
        try
        {
            data = JsonUtility.FromJson<StockMarketSaveData>(json);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[StockMarketManager] Could not read saved stock data: " + e.Message);
            return;
        }
        if (data == null || data.stocks == null) return;

        foreach (var entry in data.stocks)
        {
            if (entry == null || string.IsNullOrEmpty(entry.symbol)) continue;

            // Stocks that are no longer listed are skipped; listed stocks missing from the save keep their defaults
            Stock stock = GetStock(entry.symbol);
            if (stock == null) continue;

            stock.RestoreState(entry.currentPrice, entry.previousPrice, entry.basePrice, entry.ownedShares, entry.averageBuyPrice);
        }

        // Saves before version 2 have no trades
        tradeRecords.Clear();
        if (data.trades != null)
        {
            foreach (var trade in data.trades)
            {
                if (trade == null || string.IsNullOrEmpty(trade.symbol) || trade.shares <= 0) continue;
                tradeRecords.Add(trade);
                if (tradeRecords.Count >= MaxTradeRecords) break;
            }
        }

        StockPricesUpdated?.Invoke();
    }
}
