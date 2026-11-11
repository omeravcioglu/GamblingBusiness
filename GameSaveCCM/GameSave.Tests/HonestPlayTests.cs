using Newtonsoft.Json;
using System.Globalization;

namespace GameSave.Tests;

/// <summary>
/// Honest play, simulated with the game's own formulas and real random rolls (CompanyManager.ProcessNewDay with every skill
/// unlocked, hires with Lucky Hire and Critical Success, hiring tasks, crises, risk cards with every luck skill, casino
/// rounds, stock trades, Online City transfers (instant ones, and ones still on their way across uploads: booked before or
/// after an accept, refused and refunded, their stamps tidied), board bonuses, bankruptcy hearings (a bank loan or the
/// coin-flip restart), saves written mid-round and a withdrawal booked while an upload was on its way), submitted every
/// ~2 minutes like the client does, retrying a busy answer with the current save. None of it may ever be rejected or
/// flagged. Even seeds write runStats "restarts" (the client's counter, RunStats.State.restarts), odd seeds
/// don't (saves from before it).
/// </summary>
public class HonestPlayTests
{
    public enum Style
    {
        /// <summary>Builds the company: hires, tasks, crises, cards; a few bets.</summary>
        Builder,
        /// <summary>Bets big and often (casino, tilt, build budget), trades meme stocks, uses the city.</summary>
        Gambler,
        /// <summary>Every daily luck roll and every risk card at its best (still honest with the chaos skills).</summary>
        Lucky,
        /// <summary>Goes all-in at the casino again and again: bankrupt often, back through the hearing every time.</summary>
        Reckless,
        /// <summary>Hires at the game's fastest pace: one every ~1.3 s of running day clock (stamp, flight, next batch).</summary>
        Recruiter,
        /// <summary>Wires money to and from the Online City most days: transfers on their way across uploads, refusals, races.</summary>
        Banker,
        /// <summary>
        /// Late game with a big campus (format 4 saves): fills the build budget at the build gamble, buys furniture, room
        /// levels, floors and rooms, saves up for the trophy hall and upgrades it, sells a room now and then.
        /// </summary>
        Tycoon,
        /// <summary>
        /// Plays with a manager: situations come up by the engine's rules and wait on the desk up to 12 days; investor offers
        /// (accepted, negotiated) at the cash tier they came up at, the manager's gambles, and costs. No chaos branch (no daily
        /// luck, no risk cards), so it climbs through the cash tiers instead of leaping past them.
        /// </summary>
        Managed
    }

    [TestCase(Style.Builder, 1)]
    [TestCase(Style.Builder, 2)]
    [TestCase(Style.Builder, 3)]
    [TestCase(Style.Gambler, 4)]
    [TestCase(Style.Gambler, 5)]
    [TestCase(Style.Gambler, 6)]
    [TestCase(Style.Lucky, 7)]
    [TestCase(Style.Lucky, 8)]
    [TestCase(Style.Recruiter, 9)]
    [TestCase(Style.Recruiter, 10)]
    [TestCase(Style.Banker, 13)]
    [TestCase(Style.Banker, 14)]
    public void OneHourOfAStrongCompany_NeverFlagged(Style style, int seed)
    {
        var sim = new HonestCompany(style, seed);
        sim.Play(realSeconds: 3600);
        Assert.That(sim.Submits, Is.GreaterThanOrEqualTo(20));
        TestContext.Out.WriteLine(sim.Summary());
        if (style == Style.Recruiter)
            Assert.That(sim.MaxHireShare, Is.GreaterThan(0.3).And.LessThan(1.0), "hiring near, never over, the hire cap");
    }

    [TestCase(21)]
    [TestCase(22)]
    [TestCase(23)]
    [TestCase(24)]
    public void OneHourOfBankruptcies_NeverFlagged(int seed)
    {
        // Even seeds count restarts in the save (every hearing must show), odd seeds are today's client
        var sim = new HonestCompany(Style.Reckless, seed);
        sim.Play(realSeconds: 3600);
        TestContext.Out.WriteLine(sim.Summary());
        Assert.That(sim.Hearings, Is.GreaterThanOrEqualTo(5), "the style must actually go bankrupt");
    }

    [TestCase(31)]
    [TestCase(32)]
    [TestCase(33)]
    public void OneHourOfALateGameCampus_NeverFlagged(int seed)
    {
        // Starts from an accepted late-game save (day 400, $300M, a 115-income-room campus) and plays on: every day's campus
        // income is the game's own (room levels, furniture), with the most salary savings and no upkeep (stricter)
        var sim = new HonestCompany(Style.Tycoon, seed);
        sim.Play(realSeconds: 3600);
        TestContext.Out.WriteLine(sim.Summary());
        Assert.That(sim.Submits, Is.GreaterThanOrEqualTo(20));
        Assert.That(sim.HasTrophyHall, Is.True, "the style must build the trophy hall");
        Assert.That(sim.RoomUpgrades, Is.GreaterThan(20), "room levels bought during the hour");
        Assert.That(sim.MaxCampusIncome, Is.GreaterThan(2_000_000), "the campus earns millions a day");
    }

    [TestCase(41)]
    [TestCase(42)]
    [TestCase(43)]
    public void ThreeHoursWithAManager_NeverFlagged(int seed)
    {
        var sim = new HonestCompany(Style.Managed, seed);
        sim.Play(realSeconds: 3 * 3600);
        TestContext.Out.WriteLine(sim.Summary());
        Assert.That(sim.InvestorGains, Is.GreaterThanOrEqualTo(2), "investor offers taken");
        Assert.That(sim.GainTiers.Count, Is.GreaterThanOrEqualTo(2), "offers at more than one cash tier");
        Assert.That(sim.ManagerGambles, Is.GreaterThanOrEqualTo(8), "the manager's gambles");
    }

    [TestCase(Style.Builder, 11)]
    [TestCase(Style.Gambler, 12)]
    public void FourHours_NeverFlagged(Style style, int seed)
    {
        var sim = new HonestCompany(style, seed);
        sim.Play(realSeconds: 4 * 3600);
        TestContext.Out.WriteLine(sim.Summary());
    }

    [Test]
    public void WorstCaseLuck_EveryDayAtItsMaximum_IsCovered()
    {
        // The exact daily maximum the allowance models, day after day from $1M: the allowance must cover it
        double value = 1_000_000;
        for (int day = 11; day <= 18; day++)
            value += SaveCalibration.DailyLuckMax(value, 0, SaveCalibration.IsKarmaDay(day));
        var save = new SaveBuilder { Value = value, Day = 18, MeetingsHeld = 0 };
        var before = new SaveBuilder { Value = 1_000_000, Day = 10, MeetingsHeld = 0 };
        SubmitDecision d = SaveRules.EvaluateSubmit(T.Meta(day: 10), T.Req(2, 1, save), T.City(), T.T0 + 240,
                                                    new SaveSubmitContext { PreviousSaveJson = before.Json() });
        Assert.That(d.Meta.Flagged, Is.False, T.Why(d));
        Assert.That(value, Is.GreaterThan(5_000_000), "eight days of perfect chaos luck multiply a small company");
    }
}

/// <summary>A company played honestly by the game's rules, submitting its saves to SaveRules.</summary>
public class HonestCompany
{
    const double Inf = double.PositiveInfinity;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    readonly Random rng;
    readonly HonestPlayTests.Style style;

    // Company
    double value = SaveRules.StartingValue;
    double peak = SaveRules.StartingValue;
    int day = 1;
    double happiness = 80;
    int fired;
    int meetingsHeld;
    int nextMeetingDay = 10;
    readonly List<Employee> staff = new();
    int nextEmployeeId;
    readonly List<(string Id, int[] Levels)> rooms = new() { ("lobby", new[] { 0, 0 }), ("ceo", new[] { 0, 0, 0 }) };

    // Clocks (seconds)
    double realSeconds;
    double cardTimer, nextCard = 60, crisisTimer, nextCrisis = 45, taskTimer = -30;
    int crisesPlayed;
    bool lastCardBad;
    int goodStreak, wildCardDay = -1;

    // Stocks (one position per symbol with its average buy price, like Stock.RecordPurchase / RecordSale) and city
    long moonShares;
    double moonAverage, moonPrice = 5.0;
    // Online City: the city's books (CityLogic: lifetime totals, stamps, applied ops) and the transfers the client is
    // still waiting on (CityOnline: the company pays a deposit first, the save lists each transfer until the answer)
    long cityDeposited, cityWithdrawn;
    double cityBalance;
    readonly List<SaveCityTransfer> stamps = new();
    readonly List<string> cityOps = new();
    int nextOp, slowTransfers, refusedTransfers;

    sealed class Transfer
    {
        public string Op;
        public bool Deposit, Refused, Booked;
        public long Amount;
        public double BookAt, AnswerAt;
    }

    readonly List<Transfer> inTransit = new();

    // Bankruptcy hearings (BankLoan.State, RunStats restarts)
    readonly bool countsRestarts;
    int loansTaken, restarts;
    double loanDebt, loanRepaid;

    // Cloud save
    SaveMeta meta;
    string previousSave;
    long seq;
    long nextIndex = 1;
    readonly List<LedgerEntry> ledger = new();
    double nextSubmit = 120;
    public int Submits { get; private set; }
    /// <summary>Largest share of the income allowance honest play needed in one submit (growth nothing else explained / allowance).</summary>
    public double MaxAllowanceUse { get; private set; }
    /// <summary>Largest share of the server's hire cap one submit's new staff used (new faces / hire cap).</summary>
    public double MaxHireShare { get; private set; }
    int gambles, cards, trades, hearings, straddles, races, busyRetries, hearingsAtLastSubmit;
    public int Hearings => hearings;

    public HonestCompany(HonestPlayTests.Style style, int seed)
    {
        this.style = style;
        rng = new Random(seed);
        countsRestarts = seed % 2 == 0;
        meta = SaveRules.NewRunMeta(T.Run, T.T0, T.City());
        if (style == HonestPlayTests.Style.Tycoon) StartLateGame();
    }

    long ServerNow => T.T0 + (long)realSeconds;

    public void Play(double realSeconds)
    {
        while (this.realSeconds < realSeconds)
        {
            PlayDay();
            if (this.realSeconds >= nextSubmit) Submit(openRound: rng.NextDouble() < 0.15);
        }
    }

    public string Summary() =>
        $"{style}: day {day}, value {value.ToString("N0", Inv)} (peak {peak.ToString("N0", Inv)}), staff {staff.Count}, fired {fired}, " +
        $"submits {Submits}, allowance used at most {MaxAllowanceUse:P0}, hire cap used at most {MaxHireShare:P0}, gambles {gambles}, cards {cards}, trades {trades}, hearings {hearings} (loans {loansTaken}, restarts {restarts}{(countsRestarts ? "" : ", not counted in the save")}), mid-round saves {straddles}, city transfers on their way {slowTransfers} (refused {refusedTransfers}), withdrawals racing an upload {races}, busy retries {busyRetries}, " +
        $"luck z gambles {meta.Luck.Gambles.Z:0.00} (n {meta.Luck.Gambles.Count}), cards {meta.Luck.Cards.Z:0.00} (n {meta.Luck.Cards.Count})" +
        (style == HonestPlayTests.Style.Tycoon
            ? $"; campus: {campus.Count} rooms in {campusBuildings.Count} buildings ({campusBuildings.Sum(b => b.Floors)} floors), {roomsBuilt} built, {roomsSold} sold, " +
              $"{RoomUpgrades} room levels and {furnitureBought} furniture levels bought, trophy hall {(HasTrophyHall ? "level " + campus.First(r => r.Id == "trophy_hall").Level : "no")}, " +
              $"{buildGambles} build gambles, budget left {buildBudget.ToString("N0", Inv)}, campus income up to {MaxCampusIncome.ToString("N0", Inv)} a day"
            : "") +
        (style == HonestPlayTests.Style.Managed
            ? $"; manager: {InvestorGains} investor payments at tiers {string.Join("/", GainTiers.OrderBy(t => t))}, {ManagerGambles} gambles, {managerCosts} costs"
            : "");

    // ================================================================================================================
    // One in-game day
    // ================================================================================================================

    void PlayDay()
    {
        double duration = 15 + rng.NextDouble() * 30;   // CompanyManager.cs:492
        double hireEvery = style == HonestPlayTests.Style.Builder ? 7 : style == HonestPlayTests.Style.Reckless ? 20 : style == HonestPlayTests.Style.Recruiter ? 1.3 : 12;
        double dayStart = realSeconds;
        for (double t = 0; t < duration; t += 1)
        {
            realSeconds += 1;
            taskTimer += 1;
            if (rng.NextDouble() < 1.0 / hireEvery) HireFromBatch();
            crisisTimer += 1;
            if (crisisTimer >= nextCrisis) PlayCrisis();
            cardTimer += 1;
            if (cardTimer >= nextCard && style != HonestPlayTests.Style.Managed) PlayCard();   // Managed: no chaos branch (it climbs the tiers slowly)
        }

        if (rng.NextDouble() < (style == HonestPlayTests.Style.Gambler ? 0.8 : style == HonestPlayTests.Style.Reckless ? 0.9 : 0.25)) PlayCasino();
        MovePrice(realSeconds - dayStart);
        if (style != HonestPlayTests.Style.Builder && rng.NextDouble() < 0.25) Trade();
        if (style == HonestPlayTests.Style.Gambler && day > 20 && rng.NextDouble() < 0.02) GoBrokeHoldingShares();
        MoveTransfers();
        bool banker = style == HonestPlayTests.Style.Banker;
        if (day > (banker ? 5 : 30) && rng.NextDouble() < (banker ? 0.5 : 0.08)) UseCity();
        if (rng.NextDouble() < 0.03 && staff.Count > 5) FireWorst();
        if (day == 40) rooms.Add(("trading_floor", new[] { 5, 5, 5, 5 }));
        if (day == 60) rooms.Add(("casino_room", new[] { 5, 5, 5, 5 }));
        if (style == HonestPlayTests.Style.Tycoon) BuildCampus();
        if (style == HonestPlayTests.Style.Managed) RunManager();

        EndDay();
        CheckBankruptcy();
    }

    // ---- Staff ---------------------------------------------------------------------------------------------------

    class Employee
    {
        public string Id;
        public int IQ, Experience, Age, Salary;
        public double GPA;
        public bool PhD, Criminal;
    }

    /// <summary>ApplicantGenerator.Generate with every hiring-pool skill (quality 0.45, golden 2.25%).</summary>
    Employee Applicant()
    {
        var a = new Employee { Id = "emp" + (++nextEmployeeId), Age = rng.Next(18, 65) };
        if (rng.NextDouble() < 0.0225)
        {
            a.PhD = true;
            a.IQ = rng.Next(145, 170);
            a.GPA = 3.8 + rng.NextDouble() * 0.2;
            a.Salary = rng.Next(30000, 80000);
            a.Experience = rng.Next(15, 30);
            return a;
        }
        a.PhD = rng.Next(5) == 4;
        a.IQ = Math.Min(170, (int)Math.Round(70 + 90 * Math.Pow(rng.NextDouble(), 2.0 - 0.45 * 1.5)) + 7);
        double iqNorm = Math.Clamp((a.IQ - 70) / 90.0, 0, 1);
        a.GPA = Math.Clamp(1.5 + 1.7 * iqNorm + (-0.5 + rng.NextDouble() * 1.3) + 0.225, 1.5, 4.0);
        a.Salary = rng.Next(30000, 250000);
        (int min, int max) bracket = a.Age <= 21 ? (0, 2) : a.Age <= 25 ? (1, 5) : a.Age <= 30 ? (2, 8) : a.Age <= 40 ? (5, 15) : a.Age <= 50 ? (10, 25) : (15, 40);
        a.Experience = Math.Min(bracket.max - 1, bracket.min + (int)Math.Floor(Math.Pow(rng.NextDouble(), 0.55) * (bracket.max - bracket.min)));
        a.Criminal = rng.NextDouble() < 0.1 * 0.55;
        return a;
    }

    static double Expected(Employee e) => (e.IQ - 100) / 100.0 + (e.GPA - 2) / 4.0 + (e.Experience - 5) / 20.0 - (e.Criminal ? 1 : 0);

    /// <summary>Deal a batch of three, skip the two weaker ones (Trash to Treasure), hire the best (CompanyManager.HireEmployee).</summary>
    void HireFromBatch()
    {
        var batch = new[] { Applicant(), Applicant(), Applicant() }.OrderBy(Expected).ToList();
        for (int skip = 0; skip < 2; skip++)
            if (rng.NextDouble() < 0.3125) value += 625;   // 20% x 1.25 x Serendipity 1.25, $500 x 1.25
        Employee a = batch[2];

        a.Experience += 3;                                  // Onboarding Program x 1.25
        a.Salary = (int)(a.Salary * (1 - 0.0625));          // Salary Negotiation x 1.25
        if (rng.NextDouble() < 0.078)                       // Lucky Hire 5% x 1.25 x Serendipity
        {
            a.IQ = Math.Max(a.IQ, rng.Next(130, 150));
            a.GPA = Math.Max(a.GPA, 3.5 + rng.NextDouble() * 0.5);
        }
        double impact = (a.IQ - 100) * 15.0 + (a.GPA - 2) * 200.0 + (a.Experience - 5) * 30.0 - 300.0 - (a.Criminal ? 500 : 0);
        if (impact > 0 && rng.NextDouble() < 0.125) impact *= 2;   // Critical Success
        value = Math.Max(0, value + impact);
        staff.Add(a);

        // The active hiring task completes with this hire (an honest player aims for it)
        if (taskTimer >= 10)
        {
            double reward = Math.Round((3000 + rng.NextDouble() * 12000) * 1.4375 * (rng.NextDouble() < 0.3 ? 1.5 : 1.0));
            if (rng.NextDouble() < 0.5) reward *= 2;   // Coin Flip
            value += reward;
            taskTimer = 0;
            happiness = Math.Min(100, happiness + 5);
        }
    }

    void FireWorst()
    {
        Employee worst = staff.OrderBy(Expected).First();
        staff.Remove(worst);
        fired++;
        value = Math.Max(0, value - Math.Min(value, Math.Round(worst.Salary / 365.0 * 30)));
    }

    // ---- Crises, cards -------------------------------------------------------------------------------------------

    void PlayCrisis()
    {
        crisesPlayed++;
        double modifier = Math.Min(1.8, 1 + crisesPlayed * 0.015);
        bool won = rng.NextDouble() < 0.9;
        double reward = won ? 2000 * modifier * (rng.NextDouble() < 0.5 ? 2 : 1) : -1000 * modifier;
        value = Math.Max(0, value + reward);
        crisisTimer = 0;
        nextCrisis = 30 + rng.NextDouble() * 90;
        realSeconds += 8 + rng.NextDouble() * 12;   // the crisis pauses the day clock
    }

    /// <summary>EventManager.ChooseCard with every luck skill: high risk; good chances and multipliers as in the game.</summary>
    void PlayCard()
    {
        cardTimer = 0;
        nextCard = (60 + rng.NextDouble() * 120) * 0.7;   // Chaotic Energy
        realSeconds += 4 + rng.NextDouble() * 6;
        cards++;
        bool lucky = style == HonestPlayTests.Style.Lucky;

        double g = Math.Min(0.95, 0.25 + 0.1875 + 0.125 + 0.1875 + 0.03 + (lastCardBad ? 0.5 : 0));
        bool good = lucky || rng.NextDouble() < g;
        if (!good && rng.NextDouble() < g) good = true;              // Quantum Luck
        if (!good && rng.NextDouble() < 0.125) good = true;          // Reality Warp
        if (!good && rng.NextDouble() < 0.3125) good = true;         // Reverse Karma
        if (!good && wildCardDay != day)                             // Wild Card
        {
            wildCardDay = day;
            good = rng.NextDouble() < g;
        }

        double before = value, change;
        if (good)
        {
            double money = 15000 + rng.NextDouble() * 40000, pct = 0.10 + rng.NextDouble() * 0.25;
            double multiplier = 1.3125 * (1 + 0.1875 * Math.Min(goodStreak, 3));
            if (lucky || rng.NextDouble() < 0.125) multiplier *= 2;                    // Critical Success
            multiplier *= lucky || rng.NextDouble() < 0.5 ? 2 : 0;                      // Double or Nothing
            if (multiplier > 0 && rng.NextDouble() < 0.025) multiplier *= 10;             // Jackpot (2% x 1.25), even when lucky: its odds are tallied
            money *= multiplier;
            pct = Math.Min(pct * multiplier, 1.0);
            change = money + (before + money) * pct;
            goodStreak++;
            lastCardBad = false;
        }
        else if (rng.NextDouble() < 0.375)
        {
            change = 0;   // Jinx Removal
        }
        else
        {
            double reduction = (1 - 0.25) * (1 - 0.875) * (1 - 0.3125);   // Public Relations, both parachutes, Unlucky Protection
            double money = -(15000 + rng.NextDouble() * 45000) * reduction, pct = -(0.15 + rng.NextDouble() * 0.2) * reduction;
            change = money + (before + money) * pct;
            if (before + change < 0) change = -before;
            goodStreak = 0;
            lastCardBad = true;
        }
        value = Math.Max(0, before + change);
        Add(new LedgerEntry { Kind = LedgerKinds.Card, Source = "high", Good = good, ValueBefore = before, Change = change, Day = day });
    }

    // ---- Casino, stocks, city ------------------------------------------------------------------------------------

    void PlayCasino()
    {
        bool reckless = style == HonestPlayTests.Style.Reckless;
        int rounds = style == HonestPlayTests.Style.Gambler || reckless ? rng.Next(1, 6) : 1;
        for (int r = 0; r < rounds; r++)
        {
            if (value < (reckless ? 1 : 2000)) return;
            double share = style == HonestPlayTests.Style.Gambler ? rng.NextDouble() * 0.6 : reckless ? rng.NextDouble() * 0.8 : rng.NextDouble() * 0.1;
            double stake = Math.Floor(Math.Max(1, (value - 1000) * share));
            if (reckless && rng.NextDouble() < 0.35) stake = Math.Floor(value);   // all in
            double before = value;
            double payout = Round(rng, out string game) * stake;
            value = value - stake + payout;
            Add(new LedgerEntry { Kind = LedgerKinds.Gamble, Source = "casino", Detail = game, Stake = stake, Payout = payout, ValueBefore = before, Day = day });
            gambles++;
            realSeconds += 2 + rng.NextDouble() * 6;
            if (value <= 0.5)
            {
                CheckBankruptcy();   // the hearing opens 1.5 s later (RunEndDirector.cs:36-38), long before the day ends
                continue;
            }

            // "Win it back?" after a loss: a flip for what was lost
            if (payout < stake && rng.NextDouble() < 0.3 && value > 2000)
            {
                double flip = Math.Min(stake - payout, value - 1000);
                double flipBefore = value;
                double flipPay = rng.NextDouble() < 0.5 ? flip * 2 : 0;
                value = value - flip + flipPay;
                Add(new LedgerEntry { Kind = LedgerKinds.Gamble, Source = "tilt", Stake = flip, Payout = flipPay, ValueBefore = flipBefore, Day = day });
                gambles++;
            }
        }

        // The build budget gamble: a win pays the budget, not the company
        if (style == HonestPlayTests.Style.Gambler && rng.NextDouble() < 0.3 && value > 3000)
        {
            double stake = Math.Floor((value - 1000) * rng.NextDouble() * 0.3), before = value;
            double roll = rng.NextDouble();
            double paidBack = roll < 0.45 ? stake * 1.5 : roll < 0.5 ? stake : 0;
            value -= stake;
            if (paidBack == stake) value += stake;
            Add(new LedgerEntry { Kind = LedgerKinds.Gamble, Source = "build-gamble", Stake = stake, Payout = paidBack, ValueBefore = before, Day = day });
        }
    }

    /// <summary>One casino round with the real odds (payout multiple of the stake).</summary>
    static double Round(Random rng, out string game)
    {
        switch (rng.Next(3))
        {
            case 0:
                game = "coinflip";
                return rng.NextDouble() < 0.5 ? 1.95 : 0.0;
            case 1:
            {
                game = "blackjack";
                double r = rng.NextDouble();
                return r < 0.045 ? 2.5 : r < 0.42 ? 2.0 : r < 0.505 ? 1.0 : 0.0;
            }
            default:
            {
                game = "slots";
                int[] weights = { 25, 22, 20, 15, 10, 5, 2, 1 };
                double[] three = { 5, 6, 8, 12, 20, 50, 100, 500 };
                int Reel()
                {
                    int roll = rng.Next(100), sum = 0;
                    for (int i = 0; i < weights.Length; i++)
                    {
                        sum += weights[i];
                        if (roll < sum) return i;
                    }
                    return 7;
                }
                int a = Reel(), b = Reel(), c = Reel();
                if (a == b && b == c) return three[a];
                return a == b || b == c || a == c ? 1.5 : 0.0;
            }
        }
    }

    /// <summary>MOON's price wanders while time passes (fair-value sigma 0.3 per sqrt(hour), Market Manipulation drift, $1 floor).</summary>
    void MovePrice(double seconds)
    {
        double hours = seconds / 3600.0;
        moonPrice = Math.Max(1.0, moonPrice * Math.Exp(0.1 * hours + 0.3 * Math.Sqrt(hours) * Gaussian()));
    }

    /// <summary>Buy MOON (the cost leaves company value, the average buy price updates), or sell some of the position.</summary>
    void Trade()
    {
        if (moonShares > 0 && rng.NextDouble() < 0.6)
        {
            long shares = rng.NextDouble() < 0.5 ? moonShares : Math.Max(1, moonShares / 2);
            double proceeds = shares * moonPrice * (1 - 0.005);   // fee halved by Market Manipulation
            double costBasis = shares * moonAverage;
            moonShares -= shares;
            if (moonShares == 0) moonAverage = 0;
            value += proceeds;
            Add(new LedgerEntry { Kind = LedgerKinds.Trade, Source = "MOON", Stake = costBasis, Payout = proceeds, Day = day });
            trades++;
            return;
        }
        if (value < 10_000) return;
        long buy = (long)Math.Floor(value * 0.3 / (moonPrice * 1.005));
        if (buy < 1) return;
        value -= buy * moonPrice * 1.005;
        moonAverage = (moonAverage * moonShares + moonPrice * buy) / (moonShares + buy);
        moonShares += buy;
    }

    /// <summary>
    /// Everything but the last $1,000 goes into MOON, then an upgrade played at blackjack for the rest is lost: the company
    /// is at $0 while it still owns the shares (the hearing restarts it; the shares are sold later).
    /// </summary>
    void GoBrokeHoldingShares()
    {
        if (value < 20_000) return;
        long buy = (long)Math.Floor((value - 1000) / (moonPrice * 1.005));
        if (buy < 1) return;
        value -= buy * moonPrice * 1.005;
        moonAverage = (moonAverage * moonShares + moonPrice * buy) / (moonShares + buy);
        moonShares += buy;
        double before = value;
        Add(new LedgerEntry { Kind = LedgerKinds.Gamble, Source = "upgrade-blackjack", Stake = before, Payout = 0, ValueBefore = before, Day = day });
        value = 0;
        CheckBankruptcy();   // the hearing opens 1.5 s later (RunEndDirector.cs:36-38), long before the day ends
    }

    double Gaussian() => Math.Clamp(Math.Sqrt(-2 * Math.Log(1 - rng.NextDouble())) * Math.Cos(2 * Math.PI * rng.NextDouble()), -3.5, 3.5);

    void UseCity()
    {
        bool slow = rng.NextDouble() < 0.4;   // the city's answer takes a while (or is lost until a retry)
        if (rng.NextDouble() < 0.5 && value > 20_000)
        {
            long amount = (long)(value * rng.NextDouble() * 0.2);
            if (amount < 1) return;
            value -= amount;   // the company pays first
            if (slow) Slow(amount, deposit: true);
            else Book(NewOp(), true, amount, ServerNow);
        }
        else if (cityBalance >= 1)
        {
            long amount = (long)cityBalance;
            if (slow)
            {
                Slow(amount, deposit: false);
            }
            else
            {
                Book(NewOp(), false, amount, ServerNow);
                value += amount;
            }
        }
    }

    string NewOp() => "op-" + (++nextOp);

    /// <summary>A transfer listed pending in the saves until its answer: booked by the city after a while, or refused.</summary>
    void Slow(long amount, bool deposit)
    {
        double bookAt = realSeconds + 1 + rng.NextDouble() * 300;
        inTransit.Add(new Transfer
        {
            Op = NewOp(), Deposit = deposit, Amount = amount, BookAt = bookAt, AnswerAt = bookAt + rng.NextDouble() * 200,
            Refused = rng.NextDouble() < (deposit ? 0.15 : 0.1)
        });
        if (!deposit) cityBalance -= amount;   // held while it's on its way
        slowTransfers++;
    }

    /// <summary>CityLogic.Deposit / Withdraw: a stamp, the op applied, the lifetime totals.</summary>
    void Book(string op, bool deposit, long amount, long time)
    {
        stamps.Add(new SaveCityTransfer { OpId = op, Time = time, Amount = amount, Deposit = deposit });
        cityOps.Add(op);
        if (deposit)
        {
            cityDeposited += amount;
            cityBalance += amount;
        }
        else
        {
            cityWithdrawn += amount;
            if (cityBalance < 0) cityBalance = 0;
        }
    }

    /// <summary>The transfers on their way move on: the city books (or refuses) them, then the answer settles them (CityOnline.Settle).</summary>
    void MoveTransfers()
    {
        foreach (Transfer t in inTransit.ToArray())
        {
            if (!t.Booked && !t.Refused && realSeconds >= t.BookAt)
            {
                Book(t.Op, t.Deposit, t.Amount, T.T0 + (long)t.BookAt);
                t.Booked = true;
            }
            if (realSeconds < t.AnswerAt) continue;
            inTransit.Remove(t);
            if (t.Refused)
            {
                refusedTransfers++;
                if (t.Deposit) value += t.Amount;   // a refused deposit comes back
                else cityBalance += t.Amount;
            }
            else if (!t.Deposit)
            {
                value += t.Amount;                  // the withdrawal is paid
            }
        }
        // CityLogic.Tidy: deposit stamps for 24 h, the last 30 withdrawal stamps, the last 300 ops
        long now = ServerNow;
        stamps.RemoveAll(t => t.Deposit && t.Time <= now - 86400);
        List<SaveCityTransfer> withdrawals = stamps.Where(t => !t.Deposit).ToList();
        foreach (SaveCityTransfer old in withdrawals.Take(Math.Max(0, withdrawals.Count - 30))) stamps.Remove(old);
        if (cityOps.Count > 300) cityOps.RemoveRange(0, cityOps.Count - 300);
    }

    // ---- The daily tick (CompanyManager.ProcessNewDay with every skill) ------------------------------------------

    void EndDay()
    {
        day++;
        int n = staff.Count;
        bool lucky = style == HonestPlayTests.Style.Lucky;
        double raise = 0;
        if (rng.NextDouble() < 0.3125)   // Random Raise 25% x Serendipity 1.25
        {
            raise = (0.05 + rng.NextDouble() * 0.10) * 1.25;
            happiness = Math.Min(100, happiness + 3);
        }
        double happinessMultiplier = happiness < 50 ? 0.85 + 0.15 * happiness / 50 : 1 + 0.15 * (happiness - 50) / 50;
        double outputMultiplier = 2.5 * happinessMultiplier * (1 + raise);
        double salaryMultiplier = 0.75 + rng.NextDouble() * 0.5;   // Salary Roulette 20% x 1.25

        double output = 0, salaries = 0, incidents = 0;
        foreach (Employee e in staff)
        {
            double productivity = 1 + (e.IQ - 100) / 100.0 + (e.GPA - 2) / 4.0 + (e.Experience - 5) / 20.0;
            if (productivity < 1) productivity = 1 - (1 - productivity) * (1 - 0.3125);   // Quality Control
            double revenue = 250 * productivity;
            if (revenue > 0) revenue *= outputMultiplier;
            if (e.IQ > 100) revenue += (e.IQ - 100) * 62.5;
            if (e.PhD && revenue > 0) revenue *= 1.1875;
            output += revenue;
            salaries += e.Salary / 365.0 * (1 - 0.2125) * salaryMultiplier;
            if (e.Criminal && rng.NextDouble() < 0.05) incidents += 500 + rng.NextDouble() * 1500;
        }
        if (output > 0) output *= 1 + Math.Floor(n / 5.0) * 0.0125;
        double tower;
        if (style == HonestPlayTests.Style.Tycoon)
        {
            // The campus (TowerPlan.RoomDailyIncome, room levels included); the accounting rooms' salary savings at their
            // 15% cap come back through DayBreakdown.recurring; upkeep (a cost) is left out: both make it harder for the server
            tower = campus.Sum(RoomIncome);
            MaxCampusIncome = Math.Max(MaxCampusIncome, tower);
            salaries *= 1 - 0.15;
        }
        else
        {
            tower = rooms.Sum(r => (r.Id == "trading_floor" ? 1500 : r.Id == "casino_room" ? 2500 : 0) * (1 + 0.25 * r.Levels.Sum()) + r.Levels.Sum() * 100);
        }
        double operating = output - salaries - incidents + (rng.NextDouble() * 800 - 400) + Math.Sqrt(n) * 50 + 800 + 500 + tower - (100 + 20 * n);
        if (style == HonestPlayTests.Style.Managed) operating = ManagerDay(operating);

        double valueBefore = value;
        double growth = Math.Max(0, operating) * 4.2625 + n * 1250.0;
        double luck = style == HonestPlayTests.Style.Managed ? 0 : DailyLuck(valueBefore, n, lucky);
        value = Math.Max(0, value + growth + luck);
        value = Math.Max(0, value + operating);

        // BankLoan.OnDayEnded: 3% interest, then half the day's operating profit pays the debt (the company keeps $1,000)
        if (loanDebt > 0)
        {
            loanDebt *= 1.03;
            double pay = Math.Min(Math.Min(loanDebt, Math.Max(0, operating) * 0.5), Math.Max(0, value - 1000));
            if (pay > 0)
            {
                value -= pay;
                loanDebt -= pay;
                loanRepaid += pay;
                if (loanDebt < 0.005) loanDebt = 0;
            }
        }

        happiness = Math.Clamp(happiness + (50 - happiness) * 0.03 + (rng.NextDouble() - 0.4) * 4, 0, 100);
        foreach (Employee e in staff)
        {
            int cap = Math.Max(0, e.Age - 16);
            if (e.Experience >= cap) continue;
            double rate = 0.0625;
            if (e.Experience < 5) rate = Math.Max(rate, 0.05) * 1.5;
            double gain = Math.Max(1, e.Experience) * rate;
            int years = (int)Math.Floor(gain) + (rng.NextDouble() < gain - Math.Floor(gain) ? 1 : 0);
            e.Experience = Math.Min(e.Experience + years, cap);
        }

        if (day >= nextMeetingDay)
        {
            meetingsHeld++;
            if (rng.NextDouble() < 0.6) value += Math.Round(value * 0.05);   // a Moonshot hit
            nextMeetingDay = day + 10;
            realSeconds += 5;
        }
        peak = Math.Max(peak, value);
    }

    /// <summary>ApplyDailyLuck with every chaos skill (Serendipity x1.25 on chances, Corporate Enlightenment x1.25 on money).</summary>
    double DailyLuck(double valueBefore, int n, bool best)
    {
        double bonusBase = Math.Min(Math.Max(valueBefore * 0.01, 500), 250000), luckBase = bonusBase * 1.25, total = 0;
        bool Roll(double chance) => best || rng.NextDouble() < Math.Min(chance * 1.25, 0.9);
        double Between(double min, double max) => best ? max : min + rng.NextDouble() * (max - min);
        total += Roll(0.5) ? bonusBase * Between(1, 4) : -bonusBase * (0.6 + rng.NextDouble() * 2.4);
        if (Roll(0.30)) total += luckBase * Between(0.5, 2);
        if (Roll(0.05)) total += luckBase * 10;
        if (Roll(0.25)) total += luckBase * Between(-0.5, 2);
        int dice = best ? 6 : rng.Next(1, 7);
        total += luckBase * (dice == 1 ? -0.5 : dice <= 3 ? 0.5 : dice <= 5 ? 1.0 : 3.0);
        if (best || rng.Next(3) == 2) total += luckBase;
        if (Roll(0.15)) total += luckBase * 2;
        if (day % 5 == 0) total += luckBase * (1 + n / 20.0);
        return total;
    }

    void CheckBankruptcy()
    {
        if (value > 0.5) return;
        // The hearing (RunEndDirector): half the time a bank loan when none is out (BankLoan.TakeLoan), otherwise the last
        // coin flip comes up heads (ApplyCoinFlip: a loan still out is forgiven). Either way the company gets
        // max($50k, 10% of the run's peak), rounded to $1,000.
        double amount = Math.Round(Math.Max(50_000, 0.10 * peak) / 1000) * 1000;
        if (loanDebt <= 0 && rng.NextDouble() < 0.5)
        {
            loansTaken++;
            loanDebt = amount;
            value += amount;
        }
        else
        {
            restarts++;
            loanDebt = 0;
            value = amount;
        }
        hearings++;
        realSeconds += 20;
    }

    // ================================================================================================================
    // The campus (Tycoon; BuildMode/TowerRulesDefaults.cs, TowerPlan.cs)
    // ================================================================================================================

    sealed class Room
    {
        public string Id;
        public int Building, Floor, Tile, Level = 1;
        public int[] Furniture;
        /// <summary>Its price, room levels and furniture levels: selling refunds half (TowerPlan.SellValue).</summary>
        public double Spent;
    }

    /// <summary>Room types the Tycoon has: tiles, furniture slots, price, income a day, the price of each level from 2, the multiplier of each level from 1 (floats, as the game has them).</summary>
    static readonly Dictionary<string, (int Tiles, int Slots, double Price, double Income, double[] LevelCosts, float[] Multipliers)> RoomTypes = new()
    {
        ["lobby"] = (3, 2, 0, 0, new double[0], new[] { 1f }),
        ["ceo"] = (3, 3, 0, 0, new double[0], new[] { 1f }),
        ["starter_office"] = (2, 3, 0, 0, new[] { 8_000.0, 20_000, 60_000 }, new[] { 1f, 1f, 1f, 1f }),
        ["small_office"] = (2, 3, 5_000, 0, new[] { 8_000.0, 20_000 }, new[] { 1f, 1f, 1f }),
        ["server_room"] = (2, 3, 20_000, 0, new[] { 50_000.0, 150_000 }, new[] { 1f, 1.4f, 1.8f }),
        ["trading_floor"] = (3, 4, 40_000, 1_500, new[] { 120_000.0, 400_000, 1_400_000 }, new[] { 1f, 1.4f, 1.8f, 2.3f }),
        ["casino_room"] = (3, 4, 50_000, 2_500, new[] { 150_000.0, 500_000, 1_800_000 }, new[] { 1f, 1.4f, 1.8f, 2.3f }),
        ["marketing"] = (4, 4, 250_000, 1_000, new[] { 600_000.0, 1_500_000, 4_000_000 }, new[] { 1f, 1.4f, 1.8f, 2.3f }),
        ["trophy_hall"] = (4, 4, 300_000_000, 60_000, new[] { 700_000_000.0, 1_600_000_000 }, new[] { 1f, 1.4f, 1.8f })
    };

    /// <summary>Building types: tiles a floor, top floor, price, floor 2's price, the price growth per floor.</summary>
    static readonly Dictionary<string, (int Tiles, int MaxFloors, double Price, double SecondFloor, double Growth)> BuildingTypes = new()
    {
        ["hq"] = (8, 36, 0, 10_000, 1.42),
        ["annex"] = (12, 6, 400_000, 150_000, 1.6),
        ["tower"] = (10, 30, 2_500_000, 200_000, 1.3)
    };

    readonly List<Room> campus = new();
    readonly List<(string Type, int Floors)> campusBuildings = new();
    readonly List<(int Left, int Floor)> skybridges = new();
    double buildBudget;
    int buildGambles, roomsBuilt, roomsSold, furnitureBought;
    public int RoomUpgrades { get; private set; }
    public bool HasTrophyHall => campus.Any(r => r.Id == "trophy_hall");
    /// <summary>The most the campus earned in one day.</summary>
    public double MaxCampusIncome { get; private set; }

    /// <summary>What a room earns a day in the game (TowerPlan.RoomDailyIncome): base x (1 + 25% a furniture level) x its level's multiplier + $100 a level.</summary>
    static double RoomIncome(Room r)
    {
        var type = RoomTypes[r.Id];
        int levels = r.Furniture.Sum();
        float multiplier = type.Multipliers[Math.Min(type.Multipliers.Length, r.Level) - 1];
        return type.Income * (1f + 0.25f * levels) * multiplier + levels * 100.0;
    }

    /// <summary>TowerRules.LevelPrice: $1,000 x 2.66^level, rounded.</summary>
    static double FurniturePrice(int level) => Math.Round(1000 * Math.Pow(2.66, level));

    /// <summary>
    /// A long run the server already accepted (seq 1): day 400, $300M (peak $600M: Mogul), 200 staff, the HQ at 30 floors
    /// full of casino rooms, an annex of trading floors, an office tower with both marketing rooms and more casino rooms, two
    /// skybridges; rooms at random levels and furniture.
    /// </summary>
    void StartLateGame()
    {
        value = 300_000_000;
        peak = 600_000_000;
        day = 400;
        meetingsHeld = 40;
        nextMeetingDay = 410;
        for (int i = 0; i < 200; i++)
        {
            Employee hire = new[] { Applicant(), Applicant(), Applicant() }.OrderBy(Expected).Last();
            hire.Experience = Math.Min(Math.Max(0, hire.Age - 16), hire.Experience + 3);
            staff.Add(hire);
        }
        campusBuildings.AddRange(new[] { ("hq", 30), ("annex", 6), ("tower", 12) });
        skybridges.AddRange(new[] { (0, 3), (1, 5) });
        Built("lobby", 0, 1, 0, 1);
        Built("ceo", 0, 1, 3, 1);
        Built("starter_office", 0, 1, 6, 4);
        for (int floor = 2; floor <= 30; floor++)
        {
            Built("casino_room", 0, floor, 0);
            Built("casino_room", 0, floor, 3);
            Built("small_office", 0, floor, 6);
        }
        for (int floor = 1; floor <= 6; floor++)
            for (int k = 0; k < 4; k++) Built("trading_floor", 1, floor, k * 3);
        Built("marketing", 2, 1, 0);
        Built("marketing", 2, 1, 4);
        Built("server_room", 2, 1, 8);
        for (int floor = 2; floor <= 12; floor++)
            for (int k = 0; k < 3; k++) Built("casino_room", 2, floor, k * 3);
        buildBudget = 5_000_000;

        meta = new SaveMeta { RunId = T.Run, Seq = 1, Day = day, Value = value, Peak = peak, AcceptedAt = T.T0, StartedAt = T.T0 - 6 * 3600 };
        seq = 1;
        previousSave = SavePacking.Pack(Save().Json());
    }

    /// <summary>A room built earlier in the run: at <paramref name="level"/> (0 = random) with random furniture.</summary>
    void Built(string id, int building, int floor, int tile, int level = 0)
    {
        var type = RoomTypes[id];
        var room = new Room { Id = id, Building = building, Floor = floor, Tile = tile, Furniture = new int[type.Slots], Spent = type.Price };
        room.Level = level > 0 ? level : 1 + rng.Next(type.LevelCosts.Length + 1);
        for (int l = 2; l <= room.Level; l++) room.Spent += type.LevelCosts[l - 2];
        for (int s = 0; s < type.Slots; s++)
        {
            room.Furniture[s] = rng.Next(6);
            for (int l = 1; l <= room.Furniture[s]; l++) room.Spent += FurniturePrice(l);
        }
        campus.Add(room);
    }

    /// <summary>A day's building (build mode pauses the day clock): gambles for the budget, a sale now and then, then purchases.</summary>
    void BuildCampus()
    {
        // The build gamble: a win pays 1.5x the stake into the build budget, a push gives the stake back (BuildGambleView)
        if (rng.NextDouble() < 0.4 && value > 100_000)
        {
            double stake = Math.Floor(value * (0.05 + rng.NextDouble() * 0.10)), before = value;
            double roll = rng.NextDouble();
            double payout = roll < 0.45 ? stake * 1.5 : roll < 0.5 ? stake : 0;
            value -= stake;
            if (payout == stake) value += stake;
            else buildBudget += payout;
            Add(new LedgerEntry { Kind = LedgerKinds.Gamble, Source = "build-gamble", Stake = stake, Payout = payout, ValueBefore = before, Day = day });
            buildGambles++;
            realSeconds += 3 + rng.NextDouble() * 5;
        }

        // An income room sold (half of what it cost back into the budget): its income is in the last save, not the next
        if (rng.NextDouble() < 0.03)
        {
            List<Room> sellable = campus.Where(r => r.Id == "casino_room" || r.Id == "trading_floor").ToList();
            if (sellable.Count > 0)
            {
                Room sold = sellable[rng.Next(sellable.Count)];
                campus.Remove(sold);
                buildBudget += Math.Round(sold.Spent * 0.5);
                roomsSold++;
            }
        }

        // Mogul ($500M peak) unlocks the trophy hall: save up for it (a new annex's ground floor when no ground floor is free)
        if (!HasTrophyHall && peak >= 500_000_000)
        {
            if (buildBudget < RoomTypes["trophy_hall"].Price + BuildingTypes["annex"].Price) return;
            if (!TryPlace("trophy_hall", groundOnly: true))
            {
                BuyBuilding("annex");
                TryPlace("trophy_hall", groundOnly: true);
            }
        }

        // Once it stands, the trophy hall's next level comes first (the biggest income of any room)
        Room trophy = campus.FirstOrDefault(r => r.Id == "trophy_hall");
        double[] trophyLevels = RoomTypes["trophy_hall"].LevelCosts;
        if (trophy != null && trophy.Level <= trophyLevels.Length && trophyLevels[trophy.Level - 1] <= buildBudget)
        {
            buildBudget -= trophyLevels[trophy.Level - 1];
            trophy.Spent += trophyLevels[trophy.Level - 1];
            trophy.Level++;
            RoomUpgrades++;
        }

        // The cheapest improvement first: a furniture level, a room level, a new casino room (a new floor when none fits)
        for (int purchase = 0; purchase < 40; purchase++)
        {
            Room furnitureRoom = null, levelRoom = null;
            int furnitureSlot = -1;
            double furniturePrice = double.MaxValue, levelPrice = double.MaxValue;
            foreach (Room r in campus)
            {
                var type = RoomTypes[r.Id];
                for (int s = 0; s < r.Furniture.Length; s++)
                {
                    if (r.Furniture[s] >= 5) continue;
                    double price = FurniturePrice(r.Furniture[s] + 1);
                    if (price < furniturePrice)
                    {
                        furniturePrice = price;
                        furnitureRoom = r;
                        furnitureSlot = s;
                    }
                }
                if (r.Level <= type.LevelCosts.Length && type.LevelCosts[r.Level - 1] < levelPrice)
                {
                    levelPrice = type.LevelCosts[r.Level - 1];
                    levelRoom = r;
                }
            }
            bool roomFits = FindSpot(RoomTypes["casino_room"].Tiles, false, out _, out _, out _);
            double roomPrice = roomFits ? RoomTypes["casino_room"].Price : NextFloorPrice(out _);

            double cheapest = Math.Min(furniturePrice, Math.Min(levelPrice, roomPrice));
            if (cheapest > buildBudget) break;
            if (cheapest == furniturePrice)
            {
                buildBudget -= furniturePrice;
                furnitureRoom.Furniture[furnitureSlot]++;
                furnitureRoom.Spent += furniturePrice;
                furnitureBought++;
            }
            else if (cheapest == levelPrice)
            {
                buildBudget -= levelPrice;
                levelRoom.Level++;
                levelRoom.Spent += levelPrice;
                RoomUpgrades++;
            }
            else if (roomFits)
            {
                TryPlace("casino_room", groundOnly: false);
            }
            else if (!AddFloor())
            {
                break;
            }
        }
    }

    bool FindSpot(int tiles, bool groundOnly, out int building, out int floor, out int tile)
    {
        for (building = 0; building < campusBuildings.Count; building++)
        {
            var type = BuildingTypes[campusBuildings[building].Type];
            int top = groundOnly ? 1 : campusBuildings[building].Floors;
            for (floor = 1; floor <= top; floor++)
            {
                for (tile = 0; tile + tiles <= type.Tiles; tile++)
                {
                    int b = building, f = floor, t = tile;
                    if (!campus.Any(r => r.Building == b && r.Floor == f && t < r.Tile + RoomTypes[r.Id].Tiles && r.Tile < t + tiles)) return true;
                }
            }
        }
        building = floor = tile = -1;
        return false;
    }

    bool TryPlace(string id, bool groundOnly)
    {
        var type = RoomTypes[id];
        if (type.Price > buildBudget || !FindSpot(type.Tiles, groundOnly, out int building, out int floor, out int tile)) return false;
        buildBudget -= type.Price;
        campus.Add(new Room { Id = id, Building = building, Floor = floor, Tile = tile, Furniture = new int[type.Slots], Spent = type.Price });
        roomsBuilt++;
        return true;
    }

    /// <summary>The cheapest next floor of any building that can grow (TowerRules.FloorPrice, 2 significant digits); MaxValue when none can.</summary>
    double NextFloorPrice(out int building)
    {
        building = -1;
        double best = double.MaxValue;
        for (int i = 0; i < campusBuildings.Count; i++)
        {
            var type = BuildingTypes[campusBuildings[i].Type];
            int next = campusBuildings[i].Floors + 1;
            if (next > type.MaxFloors) continue;
            double raw = type.SecondFloor * Math.Pow(type.Growth, next - 2);
            double magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)) - 1);
            double price = Math.Round(raw / magnitude) * magnitude;
            if (price < best)
            {
                best = price;
                building = i;
            }
        }
        return best;
    }

    bool AddFloor()
    {
        double price = NextFloorPrice(out int building);
        if (building < 0 || price > buildBudget) return false;
        buildBudget -= price;
        campusBuildings[building] = (campusBuildings[building].Type, campusBuildings[building].Floors + 1);
        return true;
    }

    void BuyBuilding(string typeId)
    {
        buildBudget -= BuildingTypes[typeId].Price;
        campusBuildings.Add((typeId, 1));
    }

    // ================================================================================================================
    // The manager (Managed; Manager/ManagerEngine.cs, ManagerEffects.cs, Resources/Manager/Situations/*.json)
    // ================================================================================================================

    /// <summary>A situation on the desk: when it came up, when the player answers, and its params' inputs then (frozen).</summary>
    sealed class Decision
    {
        public string Situation;
        public int Created, DecideOn, Tier;
        public double Profit, Value;
    }

    /// <summary>The situations that move money, with their cooldowns (counted from when one came up).</summary>
    static readonly Dictionary<string, int> SituationCooldowns = new()
    {
        ["investor_share"] = 30, ["joint_marketing"] = 18, ["side_project"] = 25, ["profitable_month"] = 20, ["idle_cash"] = 25, ["casino_night"] = 30,
        ["lawsuit"] = 25, ["tax_consultants"] = 40
    };

    // Cash-tier tables of the situations' params (min / max by tier 0-6)
    static readonly double[] CashTiers = { 1e6, 1e7, 5e7, 1e8, 5e8, 1e9 };
    static readonly double[] InvestorMin = { 10_000, 60_000, 400_000, 2_000_000, 4_000_000, 20_000_000, 40_000_000 };
    static readonly double[] InvestorMax = { 80_000, 500_000, 4_000_000, 20_000_000, 40_000_000, 200_000_000, 500_000_000 };
    static readonly double[] JointMin = { 3000, 25000, 250000, 1500000, 3000000, 15000000, 30000000 };
    static readonly double[] PotMin = { 1000, 4000, 20000, 80000, 160000, 700000, 1400000 };
    static readonly double[] HostMin = { 800, 3000, 15000, 60000, 120000, 500000, 1000000 };
    static readonly double[] SettleMin = { 3000, 12000, 60000, 250000, 500000, 2000000, 4000000 };
    static readonly double[] FeeMin = { 2000, 8000, 40000, 150000, 300000, 1200000, 2500000 };

    readonly List<Decision> desk = new();
    readonly Dictionary<string, int> cameUp = new();
    readonly List<(string Id, int Start, int Until)> managerShares = new();
    readonly List<double> recentProfits = new();
    int managerCosts;
    public int InvestorGains { get; private set; }
    public int ManagerGambles { get; private set; }
    /// <summary>The cash tiers the investor's payments came at.</summary>
    public HashSet<int> GainTiers { get; } = new();

    static int CashTier(double v)
    {
        int tier = 0;
        while (tier < CashTiers.Length && v >= CashTiers[tier]) tier++;
        return tier;
    }

    /// <summary>ManagerParams.RoundMoney: 2 significant figures.</summary>
    static double RoundMoney(double amount)
    {
        double a = Math.Abs(amount);
        if (a < 100) return Math.Round(amount, MidpointRounding.AwayFromZero);
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(a)) - 1);
        return Math.Round(amount / magnitude, MidpointRounding.AwayFromZero) * magnitude;
    }

    /// <summary>ManagerEngine: max(average daily operating result of the last 7 days, 0.3% of value, $300).</summary>
    double ProfitBase() => Math.Max(Math.Max(recentProfits.Count > 0 ? recentProfits.Average() : 0, value * 0.003), 300);

    /// <summary>The desk's day: answers whose day has come, then the daily roll (ManagerEngine.DailyRoll).</summary>
    void RunManager()
    {
        foreach (Decision d in desk.Where(x => x.DecideOn <= day).ToList())
        {
            desk.Remove(d);
            Decide(d);
            if (value <= 0.5) CheckBankruptcy();
        }
        int tier = CashTier(value);
        double chance = 0.42 + 0.05 * Math.Min(tier, 4) + (desk.Count == 0 ? 0.2 : 0);
        if (rng.NextDouble() >= chance) return;
        List<string> eligible = SituationCooldowns.Keys.Where(CanComeUp).ToList();
        if (eligible.Count == 0) return;
        string pick = eligible[rng.Next(eligible.Count)];
        cameUp[pick] = day;
        // The player answers within the decision's 12 days (Engine.json defaultExpiryDays)
        desk.Add(new Decision { Situation = pick, Created = day, DecideOn = day + rng.Next(0, 13), Tier = tier, Profit = ProfitBase(), Value = value });
    }

    /// <summary>ManagerEngine.Eligible: not on the desk, past its cooldown, its conditions.</summary>
    bool CanComeUp(string situation)
    {
        if (desk.Any(d => d.Situation == situation)) return false;
        if (cameUp.TryGetValue(situation, out int last) && day - last < SituationCooldowns[situation]) return false;
        switch (situation)
        {
            case "investor_share": return day >= 10 && managerShares.Count == 0;   // "!share" (trust assumed high enough)
            case "joint_marketing": return day >= 8;
            case "idle_cash": return value >= 200_000;
            case "tax_consultants": return day >= 15 && value >= 150_000;
            default: return true;
        }
    }

    /// <summary>The player's answer, by the situation's params as they were when it came up (Finance, Employees, Heat .json).</summary>
    void Decide(Decision d)
    {
        double roll = rng.NextDouble();
        switch (d.Situation)
        {
            case "investor_share":
            {
                double amount = RoundMoney(Math.Min(Math.Max(5.5 * d.Profit, InvestorMin[d.Tier]), InvestorMax[d.Tier]));
                string choice = roll < 0.4 ? "accept" : roll < 0.8 ? "negotiate" : "reject";
                double money = choice == "accept" ? amount : choice == "negotiate" && rng.NextDouble() < 0.55 ? RoundMoney(1.25 * amount) : 0;
                if (money <= 0) return;
                double before = value;
                value += money;
                managerShares.Add(("investor", day, day + 39));   // the revenue share, 15% for 40 days
                Add(new LedgerEntry { Kind = LedgerKinds.Manager, Source = d.Situation, Detail = choice, Change = money, ValueBefore = before, Good = true, Day = day });
                InvestorGains++;
                GainTiers.Add(d.Tier);
                return;
            }
            case "joint_marketing":
            {
                double cost = RoundMoney(Math.Max(0.04 * d.Value + d.Profit, JointMin[d.Tier]));
                if (roll < 0.4) ManagerGamble(d, "accept", cost, (0.75, 1.5), (0.25, 0.6));
                else if (roll < 0.7) { if (rng.NextDouble() < 0.6) ManagerGamble(d, "negotiate", RoundMoney(0.7 * cost), (0.75, 1.5), (0.25, 0.6)); }
                else if (roll < 0.85) ManagerGamble(d, "aggressive", RoundMoney(2 * cost), (0.35, 3.0), (0.65, 0.3));
                return;
            }
            case "side_project":
                if (roll < 0.5) ManagerGamble(d, "fund", RoundMoney(2.5 * d.Profit + 0.005 * d.Value), (0.35, 3.0), (0.65, 0.2));
                return;
            case "profitable_month":
            {
                double extra = RoundMoney(4 * d.Profit);
                if (roll < 0.35) ManagerCost(d, "bonus", RoundMoney(0.5 * extra));
                else if (roll < 0.7) ManagerGamble(d, "gamble", RoundMoney(0.4 * extra), (0.48, 2.0), (0.52, 0.0));
                return;
            }
            case "idle_cash":
                if (roll < 0.5) ManagerGamble(d, "gamble", RoundMoney(0.03 * d.Value), (0.48, 2.0), (0.52, 0.0));
                return;
            case "casino_night":
                if (roll < 0.4) ManagerGamble(d, "real", RoundMoney(Math.Max(1.5 * d.Profit, PotMin[d.Tier])), (0.45, 2.2), (0.55, 0.0));
                else if (roll < 0.7) ManagerCost(d, "host", RoundMoney(Math.Max(d.Profit, HostMin[d.Tier])));
                return;
            case "lawsuit":
                ManagerCost(d, "settle", RoundMoney(Math.Max(2 * d.Profit, SettleMin[d.Tier])));
                return;
            case "tax_consultants":
                if (roll < 0.5) ManagerCost(d, "hire", RoundMoney(Math.Max(1.5 * d.Profit, FeeMin[d.Tier])));
                return;
        }
    }

    /// <summary>ManagerEffects.Gamble: the stake never takes the last $1,000; the gamble entry, then the decision's entry.</summary>
    void ManagerGamble(Decision d, string choice, double param, params (double Chance, double Mult)[] outcomes)
    {
        double stake = Math.Min(param, Math.Max(0, value - 1000));
        if (!(stake > 0)) return;
        double roll = rng.NextDouble(), mult = outcomes[^1].Mult;
        foreach ((double chance, double m) in outcomes)
        {
            roll -= chance;
            if (roll <= 0)
            {
                mult = m;
                break;
            }
        }
        double before = value, payout = Math.Round(stake * mult, 2);
        value = value - stake + payout;
        Add(new LedgerEntry { Kind = LedgerKinds.Gamble, Source = "manager", Detail = d.Situation + "/" + choice, Stake = stake, Payout = payout, ValueBefore = before, Day = day });
        Add(new LedgerEntry { Kind = LedgerKinds.Manager, Source = d.Situation, Detail = choice, Stake = stake, Change = 0, ValueBefore = before, Day = day });
        ManagerGambles++;
    }

    /// <summary>ManagerEffects.Money with a cost: it stops at $0.</summary>
    void ManagerCost(Decision d, string choice, double amount)
    {
        double before = value, moved = Math.Min(amount, value);
        if (moved < 0.5) return;
        value -= moved;
        Add(new LedgerEntry { Kind = LedgerKinds.Manager, Source = d.Situation, Detail = choice, Change = -moved, ValueBefore = before, Day = day });
        managerCosts++;
    }

    /// <summary>
    /// The day's manager money (DailyEconomyHooks, ManagerSystem.DailyMoney): the investor's 15% of a positive operating
    /// result while the share runs; the day's result for the profit base; shares whose last day passed end.
    /// </summary>
    double ManagerDay(double operating)
    {
        int booked = day - 1;   // ProcessNewDay has counted the new day already
        if (managerShares.Any(s => s.Start <= booked && booked <= s.Until)) operating -= 0.15 * Math.Max(0, operating);
        recentProfits.Add(operating);
        if (recentProfits.Count > 7) recentProfits.RemoveAt(0);
        managerShares.RemoveAll(s => s.Until < day);   // ManagerEngine.ExpireContracts
        return operating;
    }

    // ================================================================================================================
    // Cloud save
    // ================================================================================================================

    void Add(LedgerEntry entry)
    {
        entry.Index = nextIndex++;
        ledger.Add(entry);
        peak = Math.Max(peak, value);   // RunStats tracks the peak every frame
    }

    SaveBuilder Save()
    {
        var save = new SaveBuilder
        {
            Value = value,
            Day = day,
            Employees = staff.Select(e => new SaveEmployeeInfo { Id = e.Id, IQ = e.IQ, GPA = e.GPA, Experience = e.Experience, Age = e.Age, PhD = e.PhD, Criminal = e.Criminal }).ToList(),
            Fired = fired,
            RunPeak = peak,
            Rooms = rooms.ToList(),
            SofaLevel = 5,
            MeetingsHeld = meetingsHeld,
            Skills = AllSkills,
            Holdings = moonShares > 0 ? new() { ("MOON", moonShares, moonAverage) } : new(),
            Pending = inTransit.Select(t => (t.Op, t.Deposit ? "deposit" : "withdraw", t.Amount)).ToList(),
            Restarts = countsRestarts ? restarts : null,
            Loan = (loansTaken, loanDebt, loanRepaid)
        };
        if (style == HonestPlayTests.Style.Managed) save.ManagerShares = managerShares.ToList();
        if (style == HonestPlayTests.Style.Tycoon)
        {
            // TowerSave.Capture: format 4
            save.Campus = campus.Select(r => new CampusRoom(r.Id, r.Building, r.Floor, r.Tile, r.Level, r.Furniture.ToArray())).ToList();
            save.Buildings = campusBuildings.ToList();
            save.Skybridges = skybridges.ToList();
            save.BuildBudget = buildBudget;
        }
        return save;
    }

    static readonly List<string> AllSkills = new()
    {
        "chaos_1_luck", "chaos_1_charm", "chaos_1_skip", "chaos_3_crit", "chaos_4_energy", "chaos_4_fallacy", "chaos_4_streak", "chaos_5_wildcard",
        "chaos_5_double", "chaos_5_jackpot", "chaos_5_serendipity", "chaos_6_reverse", "chaos_6_probability", "chaos_6_reality", "chaos_6_quantum",
        "chaos_6_karma", "secret_ultimate"
    };

    /// <param name="openRound">The save is written while a blackjack hand is on the table (its stake already taken).</param>
    void Submit(bool openRound)
    {
        peak = Math.Max(peak, value);   // RunStats has seen this value (before a hand's stake leaves it)
        double openStake = 0, openBefore = 0;
        if (openRound && value > 5000)
        {
            openStake = Math.Floor((value - 1000) * rng.NextDouble() * 0.9);
            openBefore = value;
            value -= openStake;
            straddles++;
        }

        MoveTransfers();
        SaveBuilder save = Save();

        // A withdrawal the player wired while this upload was on its way: the city booked (and paid) it after the save
        // was read, before the server read the city. The player is at the city's bank then, so no casino hand is open
        if (day > (style == HonestPlayTests.Style.Banker ? 5 : 30) && openStake == 0 && cityBalance >= 1 && rng.NextDouble() < 0.2)
        {
            long amount = (long)cityBalance;
            Book(NewOp(), false, amount, ServerNow);
            value += amount;
            races++;
        }

        SubmitRequest request;
        SubmitDecision d;
        for (int attempt = 0; ; attempt++)
        {
            seq += 1 + rng.Next(3);
            // Sent packed, like the client does (SubmitRequest.SaveGz, LedgerGz)
            request = new SubmitRequest
            {
                RunId = meta.RunId, Seq = seq, BaseSeq = meta.Seq, Day = save.Day, Value = save.Value, SaveGz = SavePacking.Pack(save.Json()),
                LedgerGz = SavePacking.Pack(JsonConvert.SerializeObject(ledger)), Build = "sim"
            };
            var city = new CityTotals { Deposited = cityDeposited, Withdrawn = cityWithdrawn, Known = true };
            d = SaveRules.EvaluateSubmit(meta, request, city, ServerNow, new SaveSubmitContext
            {
                PreviousSaveJson = previousSave, CityTransfers = stamps.ToList(), CityOps = cityOps.ToList(), PlayerId = "player-1"
            });
            if (d.Code != SaveCodes.Busy) break;
            // CloudSaveSync: busy is retried 2, 4, 6, 8, 10 s later with the current local save
            Assert.That(attempt, Is.LessThan(5), "the client stops retrying after five busy answers: " + T.Why(d));
            busyRetries++;
            realSeconds += 2 * (attempt + 1);
            MoveTransfers();
            save = Save();
        }
        string why = T.Why(d) + (d.Growth != null
            ? $" | growth: {d.Growth.ValueBefore:N0} -> {d.Growth.ValueAfter:N0}, in transit {d.Growth.PendingBefore:N0} -> {d.Growth.PendingAfter:N0}, " +
              $"ledger {d.Growth.LedgerNet:N0}, straddle {d.Growth.Straddle:N0}, city {d.Growth.CityNet:N0}, allowance {d.Growth.Allowance.Total:N0}, residual {d.Growth.Residual:N0}"
            : "") + " | " + Summary();
        Assert.That(d.Accepted, Is.True, "submit " + Submits + ": " + why);
        Assert.That(d.Meta.Flagged, Is.False, "submit " + Submits + ": " + why);
        if (d.Growth != null && d.Growth.Allowance.Total > 0 && hearings == hearingsAtLastSubmit)
        {
            // Growth left for the allowance to explain (stretches with a bankruptcy restart are left out: the restart isn't income)
            double needed = d.Growth.Residual + d.Growth.Allowance.Total + d.Growth.Slack;
            MaxAllowanceUse = Math.Max(MaxAllowanceUse, needed / d.Growth.Allowance.Total);
        }
        hearingsAtLastSubmit = hearings;
        if (d.Growth != null && d.Growth.Allowance.HireCap > 0)
            MaxHireShare = Math.Max(MaxHireShare, (double)d.Growth.Allowance.NewStaff / d.Growth.Allowance.HireCap);

        meta = d.Meta;
        previousSave = d.StoredSave;   // save_data as the module stores it (packed)
        Assert.That(previousSave, Is.SameAs(request.SaveGz));
        ledger.RemoveAll(e => e.Index <= meta.LedgerIndex);
        Submits++;
        nextSubmit = realSeconds + 120 + rng.NextDouble() * 30;

        // After the save: the open hand settles (a win pays 2x)
        if (openStake > 0)
        {
            double r = rng.NextDouble();
            double payout = r < 0.045 ? openStake * 2.5 : r < 0.42 ? openStake * 2 : r < 0.505 ? openStake : 0;
            value += payout;
            Add(new LedgerEntry { Kind = LedgerKinds.Gamble, Source = "casino", Detail = "blackjack", Stake = openStake, Payout = payout, ValueBefore = openBefore, Day = day });
            gambles++;
        }
        realSeconds += 3;
    }
}
