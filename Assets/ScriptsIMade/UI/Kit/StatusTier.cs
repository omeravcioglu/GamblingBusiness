/// <summary>
/// Status tiers of the design (page "Status upgrades"): the company's look grows with its value.
/// 0 Start-up · 1 Company $1M · 2 Corporation $10M · 3 Executive $50M · 4 Tycoon $100M · 5 Mogul $500M · 6 Billionaire $1B.
/// Only materials change per tier (paper stock, trims), never layout or text colours.
/// </summary>
public static class StatusTier
{
    public const int Max = 6;

    /// <summary>Company value that reaches tiers 1..6.</summary>
    public static readonly double[] Thresholds = { 1e6, 1e7, 5e7, 1e8, 5e8, 1e9 };

    /// <summary>The design's English tier names, for notes only: players see StatusLadder.GetTierName (the Status String Table).</summary>
    public static readonly string[] Names = { "Start-up", "Company", "Corporation", "Executive", "Tycoon", "Mogul", "Billionaire" };

    public static int ForValue(double companyValue)
    {
        int tier = 0;
        while (tier < Thresholds.Length && companyValue >= Thresholds[tier]) tier++;
        return tier;
    }

    /// <summary>Tier of the running company (0 without a CompanyManager).</summary>
    public static int Current => CompanyManager.Instance != null ? ForValue(CompanyManager.Instance.companyValue) : 0;
}
