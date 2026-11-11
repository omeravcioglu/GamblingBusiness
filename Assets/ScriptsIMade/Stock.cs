using UnityEngine;

/// <summary>
/// One listed stock.
/// Prices follow a random walk in log space:
/// - basePrice (fair value) moves slowly: a tiny per-type drift plus this stock's trend, with a little noise.
/// - currentPrice jitters around it (volatility) and is pulled back toward it (mean reversion).
/// - Volatile and Meme stocks sometimes jump; part of a jump sticks, the rest fades back.
/// Prices never drop below MinPrice.
/// </summary>
[System.Serializable]
public class Stock
{
    /// <summary>Lowest possible share price.</summary>
    public const float MinPrice = 1f;
    /// <summary>Lowest possible base price, so cheap stocks don't sit on the price floor.</summary>
    public const float MinBasePrice = 2f;
    /// <summary>Tick length the volatility values are tuned for (StockMarketManager.priceUpdateInterval).</summary>
    public const float ReferenceTickSeconds = 5f;

    public string symbol;
    public string companyName;
    public float currentPrice;
    public float previousPrice;

    [Tooltip("Fair value the price is pulled back toward. It moves slowly on its own.")]
    public float basePrice;

    public long ownedShares = 0;

    [Tooltip("Average price paid per owned share (0 when none are owned).")]
    public float averageBuyPrice = 0f;

    // Price history for chart (last 30 data points, ring buffer)
    public float[] priceHistory = new float[30];
    private int historyIndex = 0;

    // Typical size of one price move
    [Tooltip("Typical price move per 5-second tick (0.01 = about 1%).")]
    [Range(0.001f, 0.08f)]
    public float volatility = 0.01f;

    // Extra long-run drift: positive = tends to go up, negative = tends to go down
    [Tooltip("Drift of the base price per hour of play, added to the type's drift (0.02 = +2% per hour).")]
    [Range(-0.1f, 0.1f)]
    public float trend = 0f;

    // Stock type affects behavior
    public StockType stockType = StockType.Normal;

    public Stock(string symbol, string name, float startPrice, float volatility, float trend, StockType type)
    {
        this.symbol = symbol;
        this.companyName = name;
        this.currentPrice = Mathf.Max(MinPrice, startPrice);
        this.previousPrice = this.currentPrice;
        this.basePrice = Mathf.Max(MinBasePrice, startPrice);
        this.volatility = volatility;
        this.trend = trend;
        this.stockType = type;

        ResetHistory(this.currentPrice);
    }

    /// <summary>Advance one tick of the default length.</summary>
    public void UpdatePrice()
    {
        UpdatePrice(ReferenceTickSeconds);
    }

    /// <summary>Advance the price by <paramref name="deltaSeconds"/> of game time.</summary>
    public void UpdatePrice(float deltaSeconds)
    {
        if (deltaSeconds <= 0f) return;

        // Repair bad values (Inspector edits, old saves)
        if (!IsValidPrice(currentPrice))
            currentPrice = IsValidPrice(basePrice) ? basePrice : MinBasePrice;
        if (!IsValidPrice(basePrice))
            basePrice = Mathf.Max(MinBasePrice, currentPrice);

        previousPrice = currentPrice;

        TypeProfile profile = TypeProfile.For(stockType);
        float steps = deltaSeconds / ReferenceTickSeconds; // 1 at the default interval
        float noiseScale = Mathf.Sqrt(steps);

        // Base price: slow random walk with a tiny drift
        float baseLog = Mathf.Log(basePrice)
                      + (profile.driftPerHour + trend) * (deltaSeconds / 3600f)
                      + Gaussian() * volatility * profile.baseNoise * noiseScale;

        // Current price: noise plus a pull back toward the base price
        float priceLog = Mathf.Log(currentPrice);
        float pull = 1f - Mathf.Pow(0.5f, deltaSeconds / (profile.reversionHalfLifeMinutes * 60f));
        priceLog += (baseLog - priceLog) * pull + Gaussian() * volatility * noiseScale;

        // Occasional jumps (hype, news, panic). Part sticks, the rest fades back through the pull.
        if (profile.jumpChance > 0f && Random.value < profile.jumpChance * steps)
        {
            float jump = Random.Range(profile.jumpMin, profile.jumpMax) * (Random.value < 0.5f ? 1f : -1f);
            priceLog += jump;
            baseLog += jump * profile.jumpCarry;
        }

        basePrice = Mathf.Max(MinBasePrice, Mathf.Exp(baseLog));
        currentPrice = Mathf.Max(MinPrice, Mathf.Exp(priceLog));

        // Update history
        if (priceHistory != null && priceHistory.Length > 0)
        {
            historyIndex %= priceHistory.Length;
            priceHistory[historyIndex] = currentPrice;
            historyIndex = (historyIndex + 1) % priceHistory.Length;
        }
    }

    /// <summary>
    /// Move the fair value by a small log amount (0.01 = about +1%). The price follows it through mean reversion.
    /// Used by the Market Manipulation skill.
    /// </summary>
    public void NudgeBasePrice(float logChange)
    {
        if (logChange == 0f || !IsValidPrice(basePrice)) return;
        basePrice = Mathf.Max(MinBasePrice, basePrice * Mathf.Exp(logChange));
    }

    public float GetChangePercent()
    {
        if (previousPrice <= 0f) return 0f;
        return ((currentPrice - previousPrice) / previousPrice) * 100f;
    }

    /// <summary>The oldest price in the history window (30 ticks back: where the market screen's chart starts).</summary>
    public float GetWindowStartPrice()
    {
        if (priceHistory == null || priceHistory.Length == 0) return currentPrice;
        int n = priceHistory.Length;
        float p = priceHistory[((historyIndex % n) + n) % n];
        return p > 0f ? p : currentPrice;
    }

    /// <summary>Change against the oldest price in the history window, in percent (the market screen's "Change").</summary>
    public float GetWindowChangePercent()
    {
        float start = GetWindowStartPrice();
        return start > 0f ? (currentPrice - start) / start * 100f : 0f;
    }

    /// <summary>Copies the history window into <paramref name="into"/>, oldest first; it ends with the current price.</summary>
    public void CopyHistory(System.Collections.Generic.List<float> into)
    {
        into.Clear();
        if (priceHistory == null || priceHistory.Length == 0)
        {
            into.Add(currentPrice);
            return;
        }
        int n = priceHistory.Length;
        int start = ((historyIndex % n) + n) % n;
        for (int i = 0; i < n; i++)
        {
            float p = priceHistory[(start + i) % n];
            into.Add(p > 0f ? p : currentPrice);
        }
    }

    public double GetTotalValue()
    {
        return (double)currentPrice * ownedShares;
    }

    /// <summary>Unrealised profit or loss on the owned shares (0 when none are owned).</summary>
    public double GetProfitLoss()
    {
        if (ownedShares <= 0 || averageBuyPrice <= 0f) return 0.0;
        return ((double)currentPrice - averageBuyPrice) * ownedShares;
    }

    public float GetAverageHistoryPrice()
    {
        if (priceHistory == null || priceHistory.Length == 0) return currentPrice;
        float sum = 0;
        foreach (float p in priceHistory)
            sum += p;
        return sum / priceHistory.Length;
    }

    /// <summary>Add bought shares and update the average buy price.</summary>
    public void RecordPurchase(long shares, float pricePerShare)
    {
        if (shares <= 0) return;
        double totalCost = (double)averageBuyPrice * ownedShares + (double)pricePerShare * shares;
        ownedShares += shares;
        averageBuyPrice = (float)(totalCost / ownedShares);
    }

    /// <summary>Remove sold shares (the average buy price of the rest is unchanged).</summary>
    public void RecordSale(long shares)
    {
        if (shares <= 0) return;
        ownedShares = shares >= ownedShares ? 0 : ownedShares - shares;
        if (ownedShares == 0)
            averageBuyPrice = 0f;
    }

    /// <summary>Apply saved values. Missing or invalid values keep the current ones.</summary>
    public void RestoreState(float price, float previous, float savedBasePrice, long shares, float averagePrice)
    {
        if (IsValidPrice(price))
            currentPrice = Mathf.Max(MinPrice, price);
        previousPrice = IsValidPrice(previous) ? Mathf.Max(MinPrice, previous) : currentPrice;
        basePrice = Mathf.Max(MinBasePrice, IsValidPrice(savedBasePrice) ? savedBasePrice : currentPrice);
        ownedShares = shares > 0 ? shares : 0;
        averageBuyPrice = ownedShares > 0 ? (IsValidPrice(averagePrice) ? averagePrice : currentPrice) : 0f;
        ResetHistory(currentPrice);
    }

    void ResetHistory(float price)
    {
        if (priceHistory == null || priceHistory.Length == 0)
            priceHistory = new float[30];
        for (int i = 0; i < priceHistory.Length; i++)
            priceHistory[i] = price;
        historyIndex = 0;
    }

    static bool IsValidPrice(float price)
    {
        return price > 0f && !float.IsNaN(price) && !float.IsInfinity(price);
    }

    /// <summary>Standard normal random number, clipped to +-3.5 so single ticks stay sane.</summary>
    static float Gaussian()
    {
        float u1 = Random.Range(1e-6f, 1f);
        float u2 = Random.value;
        float z = Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Cos(2f * Mathf.PI * u2);
        return Mathf.Clamp(z, -3.5f, 3.5f);
    }

    /// <summary>How each stock type moves. Volatility itself is per stock (see StockMarketManager.InitializeStocks).</summary>
    struct TypeProfile
    {
        public float driftPerHour;             // base price drift per hour of play
        public float reversionHalfLifeMinutes; // time for a gap between price and base price to halve
        public float baseNoise;                // base price noise, as a fraction of the stock's volatility
        public float jumpChance;               // chance of a jump per 5 s tick
        public float jumpMin, jumpMax;         // jump size in log terms (0.2 = about 20%), up or down
        public float jumpCarry;                // part of a jump that moves the base price too (the rest fades)

        public static TypeProfile For(StockType type)
        {
            switch (type)
            {
                case StockType.Stable:
                    return new TypeProfile { driftPerHour = 0.015f, reversionHalfLifeMinutes = 4f, baseNoise = 0.25f };
                case StockType.Dividend:
                    return new TypeProfile { driftPerHour = 0.02f, reversionHalfLifeMinutes = 3f, baseNoise = 0.25f };
                case StockType.Volatile:
                    return new TypeProfile
                    {
                        driftPerHour = 0.005f, reversionHalfLifeMinutes = 8f, baseNoise = 0.3f,
                        jumpChance = 0.002f, jumpMin = 0.05f, jumpMax = 0.12f, jumpCarry = 0.3f
                    };
                case StockType.Meme:
                    // Can moon or crash at any moment, but 60% of each spike fades back
                    return new TypeProfile
                    {
                        driftPerHour = -0.01f, reversionHalfLifeMinutes = 5f, baseNoise = 0.2f,
                        jumpChance = 0.01f, jumpMin = 0.1f, jumpMax = 0.3f, jumpCarry = 0.4f
                    };
                default: // Normal
                    return new TypeProfile { driftPerHour = 0.01f, reversionHalfLifeMinutes = 6f, baseNoise = 0.3f };
            }
        }
    }
}

public enum StockType
{
    Normal,     // Standard stock
    Volatile,   // High risk, high reward
    Stable,     // Low risk, low reward
    Meme,       // Can moon or crash randomly
    Dividend    // Slow steady growth
}
