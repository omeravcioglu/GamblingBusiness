using System;

/// <summary>How a piece of money behaves physically (light bills flutter, gold is heavy).</summary>
public enum CashKind : byte
{
    Bill,
    Bundle,
    Brick,
    Bag,
    Gold,
    Check
}

/// <summary>
/// The money items, from a $1 bill to a $1T novelty cheque. Each has its atlas sprite (Tools/CashGrab/make_cash_art.py)
/// and a physical feel. The stream picks denominations so a few items a second arrive whatever the company earns.
/// </summary>
public static class CashLadder
{
    public struct Denom
    {
        public readonly double value;
        public readonly string sprite;
        public readonly CashKind kind;

        public Denom(double value, string sprite, CashKind kind)
        {
            this.value = value;
            this.sprite = sprite;
            this.kind = kind;
        }
    }

    public static readonly Denom[] All =
    {
        new Denom(1, "bill_1", CashKind.Bill),
        new Denom(5, "bill_5", CashKind.Bill),
        new Denom(10, "bill_10", CashKind.Bill),
        new Denom(20, "bill_20", CashKind.Bill),
        new Denom(50, "bill_50", CashKind.Bill),
        new Denom(100, "bill_100", CashKind.Bill),
        new Denom(1e3, "bundle_1k", CashKind.Bundle),
        new Denom(5e3, "bundle_5k", CashKind.Bundle),
        new Denom(1e4, "bundle_10k", CashKind.Bundle),
        new Denom(5e4, "bundle_50k", CashKind.Bundle),
        new Denom(1e5, "brick_100k", CashKind.Brick),
        new Denom(5e5, "brick_500k", CashKind.Brick),
        new Denom(1e6, "bag_1m", CashKind.Bag),
        new Denom(5e6, "bag_5m", CashKind.Bag),
        new Denom(1e7, "gold_10m", CashKind.Gold),
        new Denom(5e7, "gold_50m", CashKind.Gold),
        new Denom(1e8, "gold_100m", CashKind.Gold),
        new Denom(5e8, "gold_500m", CashKind.Gold),
        new Denom(1e9, "check_1b", CashKind.Check),
        new Denom(1e10, "check_10b", CashKind.Check),
        new Denom(1e11, "check_100b", CashKind.Check),
        new Denom(1e12, "check_1t", CashKind.Check)
    };

    public static int Count => All.Length;

    /// <summary>The biggest denomination worth at most <paramref name="amount"/> (the $1 bill for anything smaller).</summary>
    public static int IndexAtMost(double amount)
    {
        if (double.IsNaN(amount) || amount < All[0].value) return 0;
        for (int i = All.Length - 1; i >= 0; i--)
            if (All[i].value <= amount * (1.0 + 1e-9)) return i;
        return 0;
    }

    /// <summary>The denomination whose sprite shows a bill of this (possibly odd) value.</summary>
    public static int IndexForValue(double value)
    {
        return IndexAtMost(value);
    }

    public static int Clamp(int index)
    {
        return index < 0 ? 0 : index >= All.Length ? All.Length - 1 : index;
    }

    // ------------------------------------------------------------------
    // Physical feel per kind
    // ------------------------------------------------------------------

    /// <summary>Air drag (per second) while flying: bills float, gold drops.</summary>
    public static float AirDrag(CashKind kind)
    {
        switch (kind)
        {
            case CashKind.Bill: return 2.2f;
            case CashKind.Check: return 2.6f;
            case CashKind.Bundle: return 0.9f;
            default: return 0.45f;
        }
    }

    /// <summary>Sliding friction on the felt (per second).</summary>
    public static float Friction(CashKind kind)
    {
        switch (kind)
        {
            case CashKind.Bill: return 5.5f;
            case CashKind.Check: return 6f;
            case CashKind.Bundle: return 4.2f;
            case CashKind.Brick: return 3.6f;
            case CashKind.Bag: return 4.8f;
            default: return 3.2f;   // gold slides
        }
    }

    /// <summary>How much of the fall speed a landing keeps as a bounce.</summary>
    public static float Bounce(CashKind kind)
    {
        switch (kind)
        {
            case CashKind.Bag: return 0.35f;
            case CashKind.Gold: return 0.3f;
            case CashKind.Brick: return 0.22f;
            default: return 0.12f;
        }
    }

    /// <summary>Gravity on the height axis (px/s²): bills fall slower (air).</summary>
    public static float Gravity(CashKind kind)
    {
        switch (kind)
        {
            case CashKind.Bill: return 900f;
            case CashKind.Check: return 800f;
            case CashKind.Bundle: return 1500f;
            default: return 1900f;
        }
    }

    /// <summary>Paper flutters in the air (sideways wobble amplitude, px/s).</summary>
    public static float Flutter(CashKind kind)
    {
        switch (kind)
        {
            case CashKind.Bill: return 70f;
            case CashKind.Check: return 55f;
            case CashKind.Bundle: return 12f;
            default: return 0f;
        }
    }

    /// <summary>Pick-up radius (px at 1x): bigger items are easier to grab.</summary>
    public static float GrabRadius(CashKind kind)
    {
        switch (kind)
        {
            case CashKind.Check: return 62f;
            case CashKind.Bag: return 44f;
            case CashKind.Brick: return 44f;
            case CashKind.Gold: return 42f;
            case CashKind.Bundle: return 40f;
            default: return 36f;
        }
    }

    /// <summary>Weight for a full tray / a worker's load (a bag counts as several bills).</summary>
    public static int Bulk(CashKind kind)
    {
        switch (kind)
        {
            case CashKind.Check: return 2;
            case CashKind.Bag: return 2;
            default: return 1;
        }
    }

    /// <summary>"$1.2K"-style short amount for floating numbers.</summary>
    public static string Short(double value)
    {
        return UIFormat.Money(Math.Round(value));
    }
}
