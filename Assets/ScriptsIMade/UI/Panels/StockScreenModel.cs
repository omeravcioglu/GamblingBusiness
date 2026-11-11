using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// Everything the designed stock market screen (StockMarketView) shows, as plain data: StockMarketUI fills it from the
/// live market, the editor previews fill it with the design canvas' sample numbers.
/// </summary>
public class StockScreenModel
{
    public int day = 1;
    /// <summary>Trading fee in percent (1, or 0.5 with Market Manipulation).</summary>
    public float feePercent = 1f;
    /// <summary>Company value: the money trades use.</summary>
    public double cash;

    /// <summary>True: the list shows the Owned tab.</summary>
    public bool showOwned;
    /// <summary>Every listed stock, in list order.</summary>
    public readonly List<StockRowModel> rows = new List<StockRowModel>();
    /// <summary>Last trades, newest first (the Owned tab shows 5).</summary>
    public readonly List<StockTradeModel> trades = new List<StockTradeModel>();

    /// <summary>Symbol of the selected stock (null: none).</summary>
    public string selected;
    /// <summary>The selected stock's last 30 prices, oldest first (ends with its price).</summary>
    public readonly List<float> history = new List<float>();

    /// <summary>Buying needs the Stock Market Access skill and it's missing.</summary>
    public bool locked;
    /// <summary>The order ticket's Sell tab is on (only honoured while the selected stock is owned).</summary>
    public bool sellMode;
    /// <summary>Shares typed in the ticket (0 = not a whole number of 1 or more).</summary>
    public long amount;
    /// <summary>Most shares of the selected stock the cash buys, fee included.</summary>
    public long maxBuy;
    /// <summary>Result of the last trade ("" = none) and whether it failed.</summary>
    public string message = "";
    public bool messageIsError;

    public StockRowModel Selected
    {
        get
        {
            if (string.IsNullOrEmpty(selected)) return null;
            foreach (var r in rows)
                if (r.symbol == selected) return r;
            return null;
        }
    }
}

public class StockRowModel
{
    public string symbol;
    public string company;
    public StockType type;
    public float price;
    /// <summary>Change against 30 ticks ago, in percent.</summary>
    public float change;
    public long owned;
    public float averagePrice;

    public double Value => (double)price * owned;
    public double Paid => (double)averagePrice * owned;
    public double Profit => owned > 0 && averagePrice > 0f ? Value - Paid : 0.0;
    public double ProfitPercent => owned > 0 && averagePrice > 0f ? ((double)price / averagePrice - 1.0) * 100.0 : 0.0;
}

public class StockTradeModel
{
    public int day;
    public bool buy;
    public string symbol;
    public long shares;
    public float price;
    public double cash;
    public double profit;
}

/// <summary>Texts and colours of the stock market screen.</summary>
public static class StockFormat
{
    public const string Table = "Stocks";
    const string Minus = "−";
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static readonly Color Ink = new Color32(0x1D, 0x24, 0x33, 0xFF);
    public static readonly Color InkSoft = new Color32(0x47, 0x4F, 0x60, 0xFF);
    public static readonly Color Pencil = new Color32(0x67, 0x6B, 0x74, 0xFF);
    public static readonly Color Gain = new Color32(0x1E, 0x7B, 0x45, 0xFF);
    public static readonly Color Loss = new Color32(0xC0, 0x35, 0x2B, 0xFF);

    /// <summary>"$912.40" below $1M, "$5,583,280" from $1M (money looks the same in every language).</summary>
    public static string Money(double value)
    {
        string sign = value < 0 ? Minus : "";
        double v = System.Math.Abs(value);
        return sign + "$" + v.ToString(v >= 1000000.0 ? "N0" : "N2", Inv);
    }

    /// <summary>"+$1,172.00", "−$54.00", "$0.00".</summary>
    public static string SignedMoney(double value)
    {
        if (value > 0) return "+" + Money(value);
        return Money(value);
    }

    /// <summary>"+3.2%", "−4.1%" ("+%3,2" in Turkish), one decimal.</summary>
    public static string Percent(double value)
    {
        double rounded = System.Math.Round(value, 1);
        string sign = rounded > 0 ? "+" : rounded < 0 ? Minus : "";
        string number = System.Math.Abs(rounded).ToString("0.0", L10n.Culture);
        return L10n.Get(Table, "stocks.percent", ("sign", sign), ("value", number));
    }

    /// <summary>A share count: "1,234" (the game's culture is invariant).</summary>
    public static string Count(long value) => value.ToString("N0", Inv);

    /// <summary>The trading fee as a number for "{fee}%": "1", "0.5".</summary>
    public static string FeeNumber(float percent) => percent.ToString("0.##", L10n.Culture);

    public static Color ProfitColor(double value) => value > 0 ? Gain : value < 0 ? Loss : Pencil;

    public static string TypeName(StockType type) => L10n.Get(Table, "stocks.type." + TypeKey(type));

    public static string TypeHint(StockType type) => L10n.Get(Table, "stocks.hint." + TypeKey(type));

    static string TypeKey(StockType type)
    {
        switch (type)
        {
            case StockType.Stable: return "stable";
            case StockType.Volatile: return "volatile";
            case StockType.Meme: return "meme";
            case StockType.Dividend: return "dividend";
            default: return "normal";
        }
    }

    /// <summary>The type chips' pastel colours (sticky-note colours of the design).</summary>
    public static Color ChipColor(StockType type)
    {
        switch (type)
        {
            case StockType.Stable: return new Color32(0xBF, 0xE3, 0xF5, 0xFF);
            case StockType.Volatile: return new Color32(0xFF, 0xC7, 0xD1, 0xFF);
            case StockType.Meme: return new Color32(0xFF, 0xE6, 0x80, 0xFF);
            case StockType.Dividend: return new Color32(0xCD, 0xEB, 0xA6, 0xFF);
            default: return new Color32(0xEA, 0xE2, 0xCF, 0xFF);
        }
    }

    public static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

    public static string Bold(string text) => "<b>" + text + "</b>";

    public static string Colored(string text, Color c) => "<color=" + Hex(c) + ">" + text + "</color>";
}
