using System.Globalization;

/// <summary>
/// Number formats of the design: money always "$1,284,500", losses "−$20,000" with a true minus sign (U+2212),
/// gains "+$15,000", durations "0:47". Culture-independent (the game runs on Turkish PCs too).
/// </summary>
public static class UIFormat
{
    /// <summary>True minus sign. Coming Soon doesn't have it; the TMP fallback font draws it.</summary>
    public const string Minus = "−";

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>"$1,284,500" (negative values get the minus sign).</summary>
    public static string Money(double value)
    {
        double rounded = System.Math.Round(value);
        if (rounded < 0)
            return Minus + "$" + (-rounded).ToString("N0", Inv);
        return "$" + rounded.ToString("N0", Inv);
    }

    /// <summary>"+$15,000", "−$20,000" or "$0".</summary>
    public static string SignedMoney(double value)
    {
        double rounded = System.Math.Round(value);
        if (rounded > 0)
            return "+$" + rounded.ToString("N0", Inv);
        if (rounded < 0)
            return Minus + "$" + (-rounded).ToString("N0", Inv);
        return "$0";
    }

    /// <summary>"0:47", "2:05" (rounded up, never negative).</summary>
    public static string Duration(float seconds)
    {
        int s = seconds <= 0f ? 0 : (int)System.Math.Ceiling(seconds);
        return (s / 60).ToString(Inv) + ":" + (s % 60).ToString("00", Inv);
    }

    /// <summary>"72%"</summary>
    public static string Percent(float value)
    {
        return System.Math.Round(value).ToString("0", Inv) + "%";
    }

    public static string Int(int value)
    {
        return value.ToString("N0", Inv);
    }
}
