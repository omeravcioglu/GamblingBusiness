using System;
using UnityEngine;

/// <summary>
/// Turns the company's revenue into money arriving on the cash floor. During the day it mints bills (out of the delivery
/// tubes) at the pace of CompanyManager.ProjectDayIncome: by the time the day is X% done, about X% of the expected day
/// income has arrived, so a hire shows up as more money right away and a bad hire as less. Each minted bill is booked as
/// it appears (CashLedger.Mint: company value grows bill by bill instead of in one lump).
/// When the day ends, CompanyManager books the day as always and asks SettleDay: the stream hands back what it delivered
/// in advance (so the day counts exactly once) and pours any remainder (daily luck, a better day than expected) out of
/// the tubes as a "payday" burst whose money the books already hold. A worse day than expected comes out of the company
/// account. Nothing else ever creates floor money, so the floor never holds more than the company earned.
/// </summary>
public sealed class CashStream : CompanyManager.IDayIncomeStream
{
    readonly CashLedger ledger;
    readonly CashFloorSim sim;
    readonly System.Random rng;

    /// <summary>Money delivered in advance for the current day (already on the books; SettleDay takes it back).</summary>
    public double MintedToday { get; private set; }

    /// <summary>CompanyManager.IDayIncomeStream: the advance (saves store company value without it).</summary>
    public double Provisional => MintedToday;
    /// <summary>The day the advance belongs to (for the log; a save written mid-day carries it).</summary>
    public int MintedDay { get; private set; }
    /// <summary>What today's books are expected to add (refreshed twice a second).</summary>
    public double ProjectedDay { get; private set; }
    /// <summary>The part of it that arrives on the floor (the rest stays in the company account: CashRules.RetainedShare).</summary>
    public double ProjectedPhysical => Math.Max(0.0, ProjectedDay) * (1.0 - CashRules.RetainedShare);
    /// <summary>Money a second arriving now (0 when the company loses money).</summary>
    public double RatePerSecond { get; private set; }
    /// <summary>The last day's settlement: what came on top of the advance (positive = payday, negative = shortfall).</summary>
    public double LastSettlementRemainder { get; private set; }
    /// <summary>What the last day kept in the company account (retained earnings).</summary>
    public double LastRetained { get; private set; }

    float refreshTimer;
    float spawnTimer;
    int nextTube;

    public CashStream(CashLedger ledger, CashFloorSim sim, int seed = 777)
    {
        this.ledger = ledger;
        this.sim = sim;
        rng = new System.Random(seed);
    }

    /// <summary>Re-read the projection on the next tick (a hire, a fire, a skill...).</summary>
    public void Invalidate()
    {
        refreshTimer = 0f;
    }

    /// <summary>
    /// Advance the stream. <paramref name="projection"/> asks the company what today will bring (called at most twice a
    /// second); <paramref name="dayProgress"/> is how far the day is (0..1); nothing is minted while the clock is stopped.
    /// </summary>
    public void Tick(float dt, bool clockRunning, int day, float dayProgress, float dayDuration, Func<double> projection)
    {
        refreshTimer -= dt;
        if (refreshTimer <= 0f || double.IsNaN(ProjectedDay))
        {
            refreshTimer = 0.5f;
            double p = projection != null ? projection() : 0.0;
            ProjectedDay = double.IsNaN(p) || double.IsInfinity(p) ? 0.0 : p;
        }
        double projected = ProjectedPhysical;   // the company keeps its share in its account; the rest comes as money
        float duration = Mathf.Max(5f, dayDuration);
        RatePerSecond = projected / duration;
        if (!clockRunning || dt <= 0f) return;
        if (MintedDay != day)
        {
            if (MintedToday > 0.0 && MintedDay > 0)
                Debug.LogWarning("[CashGrab] The day changed from " + MintedDay + " to " + day + " without a settlement; $" + MintedToday.ToString("N0") +
                                 " delivered in advance stays on the books until the next one.");
            MintedDay = day;
        }

        int tubes = Mathf.Max(1, sim.TubeMouths.Length);
        float itemsPerSecond = CashRules.ItemsPerSecond(tubes);
        double target = projected * Mathf.Clamp01(dayProgress);
        double owed = target - MintedToday;
        if (owed <= 0.0) return;

        // A few items a second whatever the company earns; a backlog (a hire raised the projection mid-day) catches up
        // within about two seconds in bigger bills; a crowded floor gets bigger bills too
        double perItem = RatePerSecond / itemsPerSecond;
        perItem = Math.Max(perItem, owed / (itemsPerSecond * 2.0));
        int crowd = sim.ActiveCount - CashRules.SoftItemCap;
        if (crowd > 0) perItem *= 1.0 + crowd / 40.0;
        int baseIndex = CashLadder.IndexAtMost(perItem);
        double baseValue = CashLadder.All[baseIndex].value;

        spawnTimer -= dt;
        int guard = 4;
        while (owed >= baseValue && spawnTimer <= 0f && guard-- > 0)
        {
            int index = RollDenomination(baseIndex);
            double room = projected - MintedToday;   // never more than today's projection in advance
            while (index > baseIndex && CashLadder.All[index].value > room) index--;
            double value = CashLadder.All[index].value;
            if (value > room + 1e-6) break;
            int bill = sim.SpawnFromTube(value, nextTube);
            if (bill < 0) break;   // the floor is full: the money stays owed (bigger bills next time)
            nextTube = (nextTube + 1 + rng.Next(Math.Max(1, tubes - 1))) % tubes;
            ledger.Mint(value);
            MintedToday += value;
            owed -= value;
            spawnTimer += 1f / (itemsPerSecond * 1.6f);
        }
        if (spawnTimer < -0.5f) spawnTimer = 0f;
    }

    /// <summary>Mostly the base denomination; sometimes a bigger one (a little jolt of excitement).</summary>
    int RollDenomination(int baseIndex)
    {
        double roll = rng.NextDouble();
        int step = roll < 0.72 ? 0 : roll < 0.9 ? 1 : roll < 0.975 ? 2 : roll < 0.996 ? 3 : 4;
        return CashLadder.Clamp(baseIndex + step);
    }

    // ------------------------------------------------------------------
    // Day settlement (CompanyManager.IDayIncomeStream)
    // ------------------------------------------------------------------

    public double SettleDay(double booked, int endedDay)
    {
        double delivered = MintedToday;
        MintedToday = 0.0;
        MintedDay = endedDay + 1;
        // A profitable day: the company keeps its share in its account, the rest is floor money (what already came, plus a
        // payday for anything still owed). A loss (or a worse day than what already came) is the company account's.
        double physical = booked > 0.0 ? booked * (1.0 - CashRules.RetainedShare) : 0.0;
        LastRetained = booked > 0.0 ? booked - physical : 0.0;
        double remainder = physical - delivered;
        LastSettlementRemainder = remainder;
        if (remainder > 0.5)
        {
            // The books already hold it: pour it out as a payday (floor money that company value already counts)
            sim.QueuePayday(remainder);
            ledger.MintPrecounted(remainder);
        }
        return delivered;
    }

    // ------------------------------------------------------------------
    // Saving
    // ------------------------------------------------------------------

    public void Restore(double mintedToday, int mintedDay)
    {
        MintedToday = double.IsNaN(mintedToday) || mintedToday < 0.0 ? 0.0 : mintedToday;
        MintedDay = mintedDay;
        refreshTimer = 0f;
    }

    public void Clear()
    {
        MintedToday = 0.0;
        MintedDay = 0;
        ProjectedDay = 0.0;
        RatePerSecond = 0.0;
        refreshTimer = 0f;
    }
}
