using System;

/// <summary>
/// Money helpers for payouts computed from Inspector multipliers and shares (floats). A float like 1.95f is really
/// 1.9500000477, so multiplying a big bet by it pays dollars too much; Exact reads the float as the decimal the designer
/// typed, and Cents rounds a payout to whole cents.
/// </summary>
public static class MoneyMath
{
    /// <summary>The float as the decimal it was typed as (1.95f -> 1.95, 0.02f -> 0.02).</summary>
    public static double Exact(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value)) return 0.0;
        return (double)(decimal)value;
    }

    /// <summary>Round money to whole cents (halves away from zero).</summary>
    public static double Cents(double amount)
    {
        if (double.IsNaN(amount) || double.IsInfinity(amount)) return 0.0;
        return Math.Round(amount, 2, MidpointRounding.AwayFromZero);
    }
}
