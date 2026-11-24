using System;

/// <summary>
/// The money rules of the Cash Floor, in one place (pure C#, no Unity objects, so the tests can run it headless).
///
/// Company value (CompanyManager.companyValue, the score: status, Board, $1B, $10B...) stays EVERYTHING the company owns.
/// The Cash Floor splits it into four pools that always add up to it:
///   company value = company account + money on the floor + Player Cash + department budgets
/// - Money on the floor: bills the company earned today that nobody has picked up yet (CashFloorSim's bills, plus a payday
///   burst still waiting in the tubes). Day income only ever arrives here.
/// - Player Cash: what the CEO physically collected and deposited in their safe. The ONLY money for gambling (casino,
///   build gamble, "win it back"), stocks and the collection tools. Company income never goes here by itself.
/// - Department budgets (Backend, Frontend, Marketing): what each department grabbed. Spent on the department's own
///   upgrades; never gambled.
/// - Company account: the rest. It pays salaries, upkeep, fines and severance and takes in hires' value, risk cards,
///   hiring tasks, crises, Board bonuses... (every existing system that changes company value keeps doing exactly that).
/// Every movement has one source and one destination:
///   Mint            day income → floor          (company value +, through the books)
///   MintPrecounted  a payday burst → floor      (already booked by the day's settlement: company value unchanged)
///   DepositPlayer   floor → Player Cash         (a bill landed in the safe)
///   DepositDept     floor → a budget            (a bill landed in a crate)
///   SpendPlayer     Player Cash → gone          (a bet, a stock, a tool: company value −)
///   ReceivePlayer   gone → Player Cash          (winnings, refunds, stock sales: company value +)
///   SpendDept       a budget → gone             (a department upgrade: company value −)
/// When something outside the Cash Floor takes more from company value than the company account holds (a −20% risk
/// card, a fine, bankruptcy's $0), Enforce takes the rest from the departments first, then the floor, then the CEO: the
/// pools never add up to more than the company is worth, and none goes below $0.
/// </summary>
public sealed class CashLedger : ICashSink
{
    public const int DeptCount = 3;
    const double Epsilon = 0.005;

    readonly ICashBooks books;
    readonly Func<double> floorValue;              // the floor's money (bills + payday still in the tubes)
    readonly Func<double, double> takeFromFloor;   // removes up to that much from the floor; returns what it took

    double playerCash;
    readonly double[] budgets = new double[DeptCount];

    // ---- Lifetime totals (stats, the tests' conservation checks) ----
    public double TotalMinted { get; private set; }
    public double TotalPrecounted { get; private set; }
    public double TotalToPlayer { get; private set; }
    public double TotalToDepts { get; private set; }
    public double TotalPlayerSpent { get; private set; }
    public double TotalPlayerReceived { get; private set; }
    public double TotalDeptSpent { get; private set; }
    public double TotalSeized { get; private set; }
    public double TotalSeizedFromFloor { get; private set; }
    public double TotalSeizedFromPlayer { get; private set; }
    public double TotalSeizedFromDepts { get; private set; }
    readonly double[] deptLifetime = new double[DeptCount];

    /// <summary>The last Enforce took money from the pools: (from the floor, from the CEO, from departments).</summary>
    public event Action<double, double, double> Seized;

    public CashLedger(ICashBooks books, Func<double> floorValue, Func<double, double> takeFromFloor)
    {
        this.books = books ?? throw new ArgumentNullException(nameof(books));
        this.floorValue = floorValue ?? (() => 0.0);
        this.takeFromFloor = takeFromFloor ?? (_ => 0.0);
    }

    // ------------------------------------------------------------------
    // Reading
    // ------------------------------------------------------------------

    public double PlayerCash => playerCash;
    public double Budget(int dept) => dept >= 0 && dept < DeptCount ? budgets[dept] : 0.0;
    public double DeptLifetime(int dept) => dept >= 0 && dept < DeptCount ? deptLifetime[dept] : 0.0;
    public double BudgetsTotal => budgets[0] + budgets[1] + budgets[2];
    public double FloorValue => Math.Max(0.0, floorValue());
    public double CompanyValue => books.Value;

    /// <summary>What the company account holds: company value minus every earmarked pool (≥ 0 after Enforce).</summary>
    public double CompanyAccount => books.Value - playerCash - BudgetsTotal - FloorValue;

    // ------------------------------------------------------------------
    // Money in
    // ------------------------------------------------------------------

    /// <summary>Day income put on the floor as a bill: the company earned it now (company value goes up by it).</summary>
    public void Mint(double value)
    {
        if (!Usable(value)) return;
        books.AddValue(value, false);
        TotalMinted += value;
    }

    /// <summary>A payday bill whose money the day's books already counted (company value stays).</summary>
    public void MintPrecounted(double value)
    {
        if (!Usable(value)) return;
        TotalPrecounted += value;
    }

    // ------------------------------------------------------------------
    // Floor → pools (ICashSink: CashFloorSim calls these when a bill lands)
    // ------------------------------------------------------------------

    public void DepositPlayer(double value)
    {
        if (!Usable(value)) return;
        playerCash += value;
        TotalToPlayer += value;
    }

    public void DepositDept(int dept, double value)
    {
        if (!Usable(value) || dept < 0 || dept >= DeptCount) return;
        budgets[dept] += value;
        deptLifetime[dept] += value;
        TotalToDepts += value;
    }

    // ------------------------------------------------------------------
    // Player Cash
    // ------------------------------------------------------------------

    public bool CanSpendPlayer(double amount)
    {
        return Usable(amount) && amount <= playerCash + Epsilon;
    }

    /// <summary>Take <paramref name="amount"/> of Player Cash out of the company (a bet, a stock, a tool). False = not enough, nothing taken.</summary>
    public bool SpendPlayer(double amount, bool instant = true)
    {
        if (!CanSpendPlayer(amount)) return false;
        amount = Math.Min(amount, playerCash);
        playerCash -= amount;
        if (playerCash < Epsilon) playerCash = 0.0;
        books.AddValue(-amount, instant);
        TotalPlayerSpent += amount;
        return true;
    }

    /// <summary>Money that comes back to the CEO (winnings, a refunded bet, stock sales): Player Cash and company value go up.</summary>
    public void ReceivePlayer(double amount, bool instant = true)
    {
        if (!Usable(amount)) return;
        playerCash += amount;
        books.AddValue(amount, instant);
        TotalPlayerReceived += amount;
    }

    // ------------------------------------------------------------------
    // Department budgets
    // ------------------------------------------------------------------

    public bool SpendDept(int dept, double amount)
    {
        if (dept < 0 || dept >= DeptCount || !Usable(amount) || amount > budgets[dept] + Epsilon) return false;
        amount = Math.Min(amount, budgets[dept]);
        budgets[dept] -= amount;
        if (budgets[dept] < Epsilon) budgets[dept] = 0.0;
        books.AddValue(-amount, false);
        TotalDeptSpent += amount;
        return true;
    }

    // ------------------------------------------------------------------
    // Bonuses and fines (moves between pools: company value stays)
    // ------------------------------------------------------------------

    /// <summary>What the company account keeps no matter what bonuses it pays (CashGrabSystem sets it from the projection).</summary>
    public double BonusReserve { get; set; }
    public double TotalBonus { get; private set; }
    public double TotalFines { get; private set; }

    /// <summary>
    /// A bonus for the CEO (a poker hand, a golden bill, a boost): company account → Player Cash, at most what the
    /// account holds above BonusReserve. Company value doesn't change. Returns what was paid.
    /// </summary>
    public double PayBonus(double amount)
    {
        if (!Usable(amount)) return 0.0;
        double room = Math.Max(0.0, CompanyAccount - Math.Max(0.0, BonusReserve));
        double paid = Math.Min(amount, room);
        if (paid < 0.01) return 0.0;
        playerCash += paid;
        TotalBonus += paid;
        return paid;
    }

    /// <summary>A written-up department pays <paramref name="share"/> of its budget into Player Cash. Returns the fine.</summary>
    public double FineDept(int dept, double share)
    {
        if (dept < 0 || dept >= DeptCount || double.IsNaN(share) || share <= 0.0) return 0.0;
        double fine = Math.Floor(budgets[dept] * Math.Min(1.0, share));
        if (fine <= 0.0) return 0.0;
        budgets[dept] -= fine;
        if (budgets[dept] < Epsilon) budgets[dept] = 0.0;
        playerCash += fine;
        TotalFines += fine;
        return fine;
    }

    // ------------------------------------------------------------------
    // Keeping the pools inside company value
    // ------------------------------------------------------------------

    /// <summary>
    /// Company value fell below the earmarked pools (something outside the Cash Floor spent more than the company account
    /// held): take the difference from the departments (in proportion), then the floor, then the CEO. Returns what was taken.
    /// </summary>
    public double Enforce()
    {
        double shortfall = -CompanyAccount;
        if (shortfall <= Epsilon) return 0.0;

        double fromDepts = 0.0, fromFloor = 0.0, fromPlayer = 0.0;

        double deptTotal = BudgetsTotal;
        if (deptTotal > 0.0)
        {
            double take = Math.Min(shortfall, deptTotal);
            for (int i = 0; i < DeptCount; i++)
            {
                double part = budgets[i] / deptTotal * take;
                budgets[i] = Math.Max(0.0, budgets[i] - part);
                if (budgets[i] < Epsilon) budgets[i] = 0.0;
            }
            fromDepts = take;
            shortfall -= take;
        }

        if (shortfall > Epsilon)
        {
            double took = Math.Max(0.0, takeFromFloor(shortfall));
            fromFloor = took;
            shortfall -= took;
        }

        if (shortfall > Epsilon && playerCash > 0.0)
        {
            double take = Math.Min(shortfall, playerCash);
            playerCash -= take;
            if (playerCash < Epsilon) playerCash = 0.0;
            fromPlayer = take;
            shortfall -= take;
        }

        double total = fromDepts + fromFloor + fromPlayer;
        TotalSeized += total;
        TotalSeizedFromFloor += fromFloor;
        TotalSeizedFromPlayer += fromPlayer;
        TotalSeizedFromDepts += fromDepts;
        if (total > Epsilon)
        {
            try { Seized?.Invoke(fromFloor, fromPlayer, fromDepts); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }
        }
        return total;
    }

    // ------------------------------------------------------------------
    // Saving
    // ------------------------------------------------------------------

    /// <summary>Put saved balances back (a loaded game): the books already hold the saved company value.</summary>
    public void Restore(double savedPlayerCash, double[] savedBudgets, double[] savedLifetime)
    {
        playerCash = Clean(savedPlayerCash);
        for (int i = 0; i < DeptCount; i++)
        {
            budgets[i] = savedBudgets != null && i < savedBudgets.Length ? Clean(savedBudgets[i]) : 0.0;
            deptLifetime[i] = savedLifetime != null && i < savedLifetime.Length ? Clean(savedLifetime[i]) : 0.0;
        }
    }

    /// <summary>Move money from the company account into Player Cash without changing company value (an older save's
    /// one-time start, the demo's petty cash). At most what the account holds.</summary>
    public double GrantFromAccount(double amount)
    {
        if (!Usable(amount)) return 0.0;
        double room = Math.Max(0.0, CompanyAccount);
        double grant = Math.Min(amount, room);
        playerCash += grant;
        return grant;
    }

    /// <summary>Forget everything (a new company).</summary>
    public void Clear()
    {
        playerCash = 0.0;
        for (int i = 0; i < DeptCount; i++)
        {
            budgets[i] = 0.0;
            deptLifetime[i] = 0.0;
        }
    }

    public double[] BudgetsCopy() => (double[])budgets.Clone();
    public double[] LifetimeCopy() => (double[])deptLifetime.Clone();

    static bool Usable(double amount)
    {
        return !double.IsNaN(amount) && !double.IsInfinity(amount) && amount > 0.0;
    }

    static double Clean(double amount)
    {
        return double.IsNaN(amount) || double.IsInfinity(amount) || amount < 0.0 ? 0.0 : amount;
    }
}
