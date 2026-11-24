using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Headless test of the cash floor's money (no scene, no Play mode: safe to run any time). It plays 40 office days with the
/// real CashStream, CashFloorSim, CashLedger and CashDepartments against a stand-in for the company's books, with a bot
/// CEO that uses every tool and gadget, departments arriving and upgrading, casino bets, risk-card style losses, a save
/// and reload in the middle, and the CEO stepping away to the desk. Every step it checks:
/// - company value = what the books should hold (start + every booked day + every outside change âˆ’ what was bet / bought
///   + what came back) + the advance delivered today (nothing created, nothing lost);
/// - floor money in = floor money out (minted + payday = to the CEO + to departments + still on the floor + seized);
/// - no pool below $0, the company account never negative after Enforce, the bill pool consistent.
/// Batch: Unity -batchmode -projectPath . -executeMethod CashGrabSmoke.Run -quit (exit code 0 = pass).
/// Menu: Tools > Game > Cash Floor > Run Money Smoke Test.
/// </summary>
public static class CashGrabSmoke
{
    sealed class Books : ICashBooks
    {
        public double value;
        public double Value => value;

        public void AddValue(double delta, bool instant)
        {
            value += delta;
            if (value < 0.0) value = 0.0;   // CompanyManager.AddMoney never goes below $0
        }
    }

    [MenuItem("Tools/Game/Cash Floor/Run Money Smoke Test")]
    static void RunMenu()
    {
        bool ok = RunAll(out string report);
        if (ok) Debug.Log(report);
        else Debug.LogError(report);
        EditorUtility.DisplayDialog("Cash floor smoke test", ok ? "PASS\n\n" + Short(report) : "FAIL\n\n" + Short(report), "OK");
    }

    static string Short(string s) => s.Length > 1500 ? s.Substring(0, 1500) + "..." : s;

    public static void Run()
    {
        bool ok;
        string report;
        try
        {
            ok = RunAll(out report);
        }
        catch (Exception e)
        {
            ok = false;
            report = "[CashGrabSmoke] crashed: " + e;
        }
        if (ok) Debug.Log(report);
        else Debug.LogError(report);
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
    }

    public static bool RunAll(out string report)
    {
        var sb = new StringBuilder();
        bool ok = true;
        foreach (int seed in new[] { 1, 7, 42 })
        {
            ok &= Scenario(seed, sb);
        }
        ok &= LedgerUnits(sb);
        report = (ok ? "[CashGrabSmoke] PASS\n" : "[CashGrabSmoke] FAIL\n") + sb;
        return ok;
    }

    // ------------------------------------------------------------------
    // The 40-day run
    // ------------------------------------------------------------------

    static bool Scenario(int seed, StringBuilder sb)
    {
        var rng = new System.Random(seed);
        var books = new Books { value = 50000.0 };
        var sim = new CashFloorSim(seed);
        var ledger = new CashLedger(books, () => sim.FloorValue, a => sim.TakeFromFloor(a));
        sim.Sink = ledger;
        var stream = new CashStream(ledger, sim, seed * 31);
        var levels = new int[CashLedger.DeptCount];
        double expected = books.value;       // company value without today's advance
        double grant = ledger.GrantFromAccount(1000.0);

        const float dt = 1f / 60f;
        int day = 1, deposits = 0, staff = 3;
        float dayTimer = 0f, dayDuration = 25f;
        double projection = 2500.0;
        int failures = 0;
        var stats = new Dictionary<string, int>();
        long steps = 0;
        double maxStepMs = 0.0, totalStepMs = 0.0;
        int maxBills = 0;
        var watch = new Stopwatch();
        Bot bot = new Bot(rng);
        bool reloaded = false;
        double floorBase = 0.0;   // after the reload the new ledger's totals start at 0, with the restored floor on top

        void Fail(string what)
        {
            failures++;
            if (failures <= 12) sb.AppendLine("  seed " + seed + " day " + day + ": " + what);
        }

        while (day <= 40)
        {
            steps++;
            // The company grows; a hire mid-day raises the pace right away
            if (rng.NextDouble() < dt / 18.0)
            {
                projection *= 1.03 + rng.NextDouble() * 0.05;
                staff++;
                stream.Invalidate();
            }

            stream.Tick(dt, true, day, dayTimer / dayDuration, dayDuration, () => projection);

            // The CEO is on the floor most of the time, at the desk now and then
            sim.Visible = (day % 4) != 3;
            bot.Equip(sim, day);
            sim.Input = sim.Visible ? bot.Next(sim, dt) : default;

            watch.Restart();
            sim.Step(dt);
            watch.Stop();
            double ms = watch.Elapsed.TotalMilliseconds;
            totalStepMs += ms;
            if (steps > 60 && ms > maxStepMs) maxStepMs = ms;
            maxBills = Math.Max(maxBills, sim.ActiveCount);

            foreach (CashEvent e in sim.Events)
            {
                string k = e.type.ToString();
                stats[k] = stats.TryGetValue(k, out int n) ? n + 1 : 1;
                if (e.type == CashEventType.DepositBatch) deposits++;
            }
            sim.Events.Clear();

            if (steps % 30 == 0)
            {
                // Departments buy upgrades with their budgets: that money leaves the company
                double spentBefore = ledger.TotalDeptSpent;
                CashDepartments.Tick(ledger, levels, day, staff, deposits, stream.ProjectedDay, true,
                                     (d, before, after) => sim.SetDeptLevel(d, after));
                expected -= ledger.TotalDeptSpent - spentBefore;
            }

            // The CEO gambles some of their cash (the casino: half the bets win double)
            if (rng.NextDouble() < dt * 0.5 && ledger.PlayerCash >= 100.0)
            {
                double bet = Math.Floor(ledger.PlayerCash * (0.1 + rng.NextDouble() * 0.5));
                if (ledger.SpendPlayer(bet))
                {
                    expected -= bet;
                    if (rng.NextDouble() < 0.48)
                    {
                        ledger.ReceivePlayer(bet * 2.0);
                        expected += bet * 2.0;
                    }
                }
            }

            // Outside the cash floor: a risk card, a fine, a hiring task reward (the books change, the floor doesn't)
            if (rng.NextDouble() < dt * 0.03)
            {
                double before = books.value;
                double change = rng.NextDouble() < 0.6 ? -books.value * (0.02 + rng.NextDouble() * 0.18) : books.value * 0.02;
                if (rng.NextDouble() < 0.05) change = -books.value * 0.7;   // now and then a disaster
                books.AddValue(change, true);
                expected += books.value - before;
            }

            ledger.Enforce();

            // A save and a reload mid-day, once (the floor comes back as bills lying around, the advance is kept)
            if (!reloaded && day == 21 && dayTimer > dayDuration * 0.5f)
            {
                reloaded = true;
                double savedCash = ledger.PlayerCash;
                double[] savedBudgets = ledger.BudgetsCopy(), savedLife = ledger.LifetimeCopy();
                double savedFloor = sim.FloorValue, savedMinted = stream.MintedToday;
                int savedMintedDay = stream.MintedDay;
                var floorIn = ledger.TotalMinted + ledger.TotalPrecounted - ledger.TotalToPlayer - ledger.TotalToDepts - ledger.TotalSeizedFromFloor;
                sim = new CashFloorSim(seed + 1000);
                ledger = new CashLedger(books, () => sim.FloorValue, a => sim.TakeFromFloor(a));
                sim.Sink = ledger;
                stream = new CashStream(ledger, sim, seed * 77);
                ledger.Restore(savedCash, savedBudgets, savedLife);
                stream.Restore(savedMinted, savedMintedDay);
                for (int d = 0; d < levels.Length; d++) sim.SetDeptLevel(d, levels[d]);
                sim.RestoreFloor(savedFloor);
                if (Math.Abs(sim.FloorValue - savedFloor) > 0.01) Fail("reload: floor " + sim.FloorValue + " != saved " + savedFloor);
                if (Math.Abs(floorIn - savedFloor) > Math.Max(0.05, savedFloor * 1e-9)) Fail("before reload the floor didn't add up: " + floorIn + " vs " + savedFloor);
                bot = new Bot(rng);
                floorBase = savedFloor;
            }

            // Day end: the books book the day, the stream settles (CompanyManager.ProcessNewDay + SettleIncomeStream)
            dayTimer += dt;
            if (dayTimer >= dayDuration)
            {
                double actual = projection * (0.85 + rng.NextDouble() * 0.3);
                if (rng.NextDouble() < 0.12) actual += projection * (0.5 + rng.NextDouble() * 2.0);   // daily luck
                if (rng.NextDouble() < 0.06) actual -= projection * 1.5;                              // a terrible day
                double valueBeforeBooks = books.value;
                books.AddValue(actual, true);
                double booked = books.value - valueBeforeBooks;
                double delivered = stream.SettleDay(booked, day);
                books.AddValue(-delivered, true);
                expected += booked;
                ledger.Enforce();
                day++;
                dayTimer = 0f;
                dayDuration = 15f + (float)rng.NextDouble() * 30f;
            }

            // ---- Checks ----
            double shouldBe = expected + stream.MintedToday;
            double tolerance = Math.Max(0.05, Math.Abs(shouldBe) * 1e-9);
            if (books.value > 0.0 && Math.Abs(books.value - shouldBe) > tolerance)
            {
                Fail("company value " + books.value.ToString("F2") + " but the flows say " + shouldBe.ToString("F2"));
                expected = books.value - stream.MintedToday;   // report each drift once
            }
            else if (books.value <= 0.0)
            {
                expected = -stream.MintedToday;   // bankrupt at $0 (the books clamp): start counting again from here
            }
            if (ledger.PlayerCash < -1e-6) Fail("player cash below $0: " + ledger.PlayerCash);
            for (int d = 0; d < CashLedger.DeptCount; d++)
                if (ledger.Budget(d) < -1e-6) Fail("budget " + d + " below $0");
            if (ledger.CompanyAccount < -0.05) Fail("company account negative after Enforce: " + ledger.CompanyAccount.ToString("F2"));
            double floorFlow = ledger.TotalMinted + ledger.TotalPrecounted - ledger.TotalToPlayer - ledger.TotalToDepts - ledger.TotalSeizedFromFloor;
            if (Math.Abs(floorFlow + floorBase - sim.FloorValue) > Math.Max(0.05, sim.FloorValue * 1e-9))
                Fail("floor money doesn't add up: flows " + (floorFlow + floorBase).ToString("F2") + ", floor " + sim.FloorValue.ToString("F2"));
            if (steps % 300 == 0 && !sim.CheckIntegrity(out string problem)) Fail("bill pool: " + problem);
        }

        sb.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "seed {0}: {1} failures | value ${2:N0}, player cash ${3:N0} (start grant ${4:N0}), budgets ${5:N0}/${6:N0}/${7:N0} at levels {8}/{9}/{10}, floor ${11:N0} in {12} bills (max {13})",
            seed, failures, books.value, ledger.PlayerCash, grant, ledger.Budget(0), ledger.Budget(1), ledger.Budget(2), levels[0], levels[1], levels[2],
            sim.FloorValue, sim.ActiveCount, maxBills));
        sb.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "  minted ${0:N0}, payday ${1:N0}, to CEO ${2:N0}, to departments ${3:N0}, seized ${4:N0} (floor ${5:N0}, CEO ${6:N0}); step avg {7:F3} ms, max {8:F2} ms",
            ledger.TotalMinted, ledger.TotalPrecounted, ledger.TotalToPlayer, ledger.TotalToDepts, ledger.TotalSeized, ledger.TotalSeizedFromFloor,
            ledger.TotalSeizedFromPlayer, totalStepMs / Math.Max(1, steps), maxStepMs));
        sb.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "  company account ${0:N0} ({1:P0} of the value), departments spent ${2:N0} on upgrades, CEO bet ${3:N0} and got back ${4:N0}",
            ledger.CompanyAccount, books.value > 0 ? ledger.CompanyAccount / books.value : 0.0, ledger.TotalDeptSpent,
            ledger.TotalPlayerSpent, ledger.TotalPlayerReceived));
        var events = new List<string>();
        foreach (var pair in stats) events.Add(pair.Key + " " + pair.Value);
        events.Sort(StringComparer.Ordinal);
        sb.AppendLine("  events: " + string.Join(", ", events));
        sb.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  bonuses paid ${0:N0} (poker, boosts, golden and marked bills), fines ${1:N0}",
                                    ledger.TotalBonus, ledger.TotalFines));

        // The bot must have exercised the important paths
        foreach (string needed in new[] { "DepositBatch", "DeptDeposit", "Grab", "TubePop", "Telegraph", "Interference", "PokerHand", "SpecialSpawned", "ReelSpin" })
            if (!stats.ContainsKey(needed)) { failures++; sb.AppendLine("  seed " + seed + ": never saw " + needed); }
        if (ledger.TotalToPlayer <= 0.0) { failures++; sb.AppendLine("  seed " + seed + ": the CEO never deposited anything"); }
        if (levels[2] == 0) { failures++; sb.AppendLine("  seed " + seed + ": Marketing never arrived"); }
        if (maxStepMs > 8.0) sb.AppendLine("  seed " + seed + ": warning: a step took " + maxStepMs.ToString("F2") + " ms");
        return failures == 0;
    }

    // ------------------------------------------------------------------
    // A bot CEO: picks money, carries it to the safe, rakes, vacuums, staples workers
    // ------------------------------------------------------------------

    sealed class Bot
    {
        readonly System.Random rng;
        Vector2 cursor = new Vector2(700f, 400f);
        bool holding;
        int target = -1;
        float hold;
        bool prevHeld;

        public Bot(System.Random rng)
        {
            this.rng = rng;
        }

        public void Equip(CashFloorSim sim, int day)
        {
            CashTool tool = day < 6 ? CashTool.Hands : day < 12 ? CashTool.Scoop : day < 17 ? CashTool.Rake : day < 24 ? CashTool.Tray
                          : day < 32 ? CashTool.Vacuum : CashTool.Industrial;
            if (sim.tool != tool)
            {
                sim.DropHeld();
                sim.tool = tool;
                sim.toolLevel = 1 + (day % 3);
                holding = false;
            }
            if (day >= 8)
                for (int g = 0; g < CashRules.GadgetCount; g++) sim.gadgetLevel[g] = day >= 20 ? 2 : 1;
            sim.gadget = (CashGadget)((day / 3) % 3);
        }

        public CashFloorSim.PlayerInput Next(CashFloorSim sim, float dt)
        {
            var input = new CashFloorSim.PlayerInput { onFloor = true };
            Vector2 goal;
            bool wantHeld;

            switch (sim.tool)
            {
                case CashTool.Rake:
                {
                    // Get behind a bill (on the far side from the safe), then sweep toward the safe
                    if (target < 0 || sim.Bills[target].state != BillState.Free) target = Nearest(sim, cursor);
                    if (target < 0) { goal = sim.SafeCenter + new Vector2(0f, -300f); wantHeld = false; break; }
                    Vector2 bill = sim.Bills[target].pos;
                    Vector2 toSafe = (sim.SafeCenter - bill).normalized;
                    Vector2 behind = bill - toSafe * 60f;
                    if (!holding && (cursor - behind).sqrMagnitude > 30f * 30f) { goal = behind; wantHeld = false; }
                    else { holding = true; goal = sim.SafeCenter; wantHeld = true; if ((cursor - sim.SafeCenter).sqrMagnitude < 90f * 90f) { holding = false; target = -1; } }
                    break;
                }
                case CashTool.Vacuum:
                case CashTool.Industrial:
                {
                    if (target < 0 || sim.Bills[target].state != BillState.Free) target = Nearest(sim, cursor);
                    goal = target >= 0 ? sim.Bills[target].pos : cursor;
                    wantHeld = sim.JamTimer <= 0f && sim.VacuumHeat < 0.9f;
                    break;
                }
                default:
                {
                    int capacity = Math.Max(1, sim.Capacity);
                    bool full = sim.Held.Count >= capacity || (sim.Held.Count > 0 && sim.tool != CashTool.Hands && rng.NextDouble() < 0.004);
                    // Bare hands fill a hand of five before banking (a poker hand)
                    bool wantMore = sim.tool == CashTool.Hands && sim.Held.Count < capacity && Nearest(sim, cursor) >= 0;
                    if (full || (sim.Held.Count > 0 && target < 0 && !wantMore))
                    {
                        goal = sim.SafeCenter + new Vector2(0f, -40f);
                        wantHeld = true;
                        if ((cursor - goal).sqrMagnitude < 50f * 50f) wantHeld = false;   // release over the safe
                    }
                    else
                    {
                        if (target < 0 || sim.Bills[target].state != BillState.Free) target = Nearest(sim, cursor);
                        goal = target >= 0 ? sim.Bills[target].pos : cursor;
                        wantHeld = sim.Held.Count > 0 || (target >= 0 && (cursor - goal).sqrMagnitude < 18f * 18f);
                        if (target >= 0 && sim.Held.Count > 0 && (cursor - goal).sqrMagnitude < 12f * 12f) target = -1;
                    }
                    // Now and then a throw at the safe from afar
                    if (sim.Held.Count > 0 && rng.NextDouble() < 0.002) wantHeld = false;
                    break;
                }
            }

            Vector2 to = goal - cursor;
            float speed = 1500f;
            cursor += to.magnitude > speed * dt ? to.normalized * speed * dt : to;
            input.cursor = cursor;
            input.held = wantHeld;
            input.down = wantHeld && !prevHeld;
            input.up = !wantHeld && prevHeld;
            prevHeld = wantHeld;

            // Gadgets: staple a worker who carries money, sometimes a paperweight or the fan
            hold -= dt;
            if (hold <= 0f && rng.NextDouble() < 0.02)
            {
                hold = 0.5f;
                input.altDown = true;
            }
            return input;
        }

        static int Nearest(CashFloorSim sim, Vector2 p)
        {
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < sim.Bills.Length; i++)
            {
                if (sim.Bills[i].state != BillState.Free || sim.Bills[i].z > 40f) continue;
                if (sim.SafeRect.Contains(sim.Bills[i].pos)) continue;
                float d = (sim.Bills[i].pos - p).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }
    }

    // ------------------------------------------------------------------
    // Small ledger cases
    // ------------------------------------------------------------------

    static bool LedgerUnits(StringBuilder sb)
    {
        int failures = 0;
        void Check(bool condition, string what)
        {
            if (condition) return;
            failures++;
            sb.AppendLine("  ledger: " + what);
        }

        var books = new Books { value = 10000.0 };
        double floor = 2000.0;
        var ledger = new CashLedger(books, () => floor, amount => { double t = Math.Min(floor, amount); floor -= t; return t; });

        ledger.DepositPlayer(1500.0);
        ledger.DepositDept(0, 500.0);
        Check(Math.Abs(ledger.CompanyAccount - (10000.0 - 2000.0 - 1500.0 - 500.0)) < 1e-6, "account after deposits " + ledger.CompanyAccount);
        Check(!ledger.SpendPlayer(5000.0), "spent more than the cash");
        Check(ledger.SpendPlayer(500.0) && Math.Abs(ledger.PlayerCash - 1000.0) < 1e-6 && Math.Abs(books.value - 9500.0) < 1e-6, "a $500 bet");
        ledger.ReceivePlayer(1000.0);
        Check(Math.Abs(ledger.PlayerCash - 2000.0) < 1e-6 && Math.Abs(books.value - 10500.0) < 1e-6, "a $1,000 win");

        // A big outside loss: departments pay first, then the floor, then the CEO
        books.value = 3000.0;   // pools: floor 2000 + cash 2000 + budget 500 = 4500 â†’ short 1500
        double taken = ledger.Enforce();
        Check(Math.Abs(taken - 1500.0) < 1e-6, "seized " + taken + " instead of 1500");
        Check(ledger.Budget(0) < 1e-6, "the budget wasn't taken first");
        Check(Math.Abs(floor - 1000.0) < 1e-6, "the floor should have given 1000, it has " + floor);
        Check(Math.Abs(ledger.PlayerCash - 2000.0) < 1e-6, "the CEO lost cash before the floor was empty");
        Check(ledger.CompanyAccount >= -1e-6, "account still negative");

        // Bankruptcy: company value at $0 takes everything
        books.value = 0.0;
        ledger.Enforce();
        Check(ledger.PlayerCash < 1e-6 && floor < 1e-6 && ledger.BudgetsTotal < 1e-6, "pools left at $0 company value");

        // The petty-cash grant never creates money
        books.value = 5000.0;
        double grant = ledger.GrantFromAccount(10000.0);
        Check(Math.Abs(grant - 5000.0) < 1e-6 && Math.Abs(books.value - 5000.0) < 1e-6, "grant " + grant);

        // Denominations: a few items a second, the floor never mints beyond the day
        Check(CashLadder.IndexAtMost(0.4) == 0 && CashLadder.All[CashLadder.IndexAtMost(250.0)].value == 100.0 &&
              CashLadder.All[CashLadder.IndexAtMost(7.3e9)].value == 1e9, "denomination ladder");

        // Bonuses and fines move money between pools: company value stays, the account keeps its reserve
        {
            var b2 = new Books { value = 10000.0 };
            double f2 = 1000.0;
            var l2 = new CashLedger(b2, () => f2, a => { double t = Math.Min(f2, a); f2 -= t; return t; });
            l2.DepositDept(1, 2000.0);                      // account = 10000 - 1000 - 2000 = 7000
            l2.BonusReserve = 5000.0;
            double paid = l2.PayBonus(4000.0);
            Check(Math.Abs(paid - 2000.0) < 1e-6 && Math.Abs(b2.value - 10000.0) < 1e-6, "bonus " + paid + " (reserve 5000 of 7000)");
            Check(l2.PayBonus(100.0) == 0.0, "a bonus out of the reserve");
            double fine = l2.FineDept(1, 0.05);
            Check(Math.Abs(fine - 100.0) < 1e-6 && Math.Abs(l2.Budget(1) - 1900.0) < 1e-6 && Math.Abs(l2.PlayerCash - 2100.0) < 1e-6 && Math.Abs(b2.value - 10000.0) < 1e-6,
                  "fine " + fine);
        }

        // Poker hands (rank = card / 4: 0 is the 2, 12 the ace; suit = card % 4)
        byte C(int rank, int suit) => (byte)(rank * 4 + suit);
        Check(CashPoker.Evaluate(new[] { C(12, 0), C(11, 0), C(10, 0), C(9, 0), C(8, 0) }) == CashPoker.Rank.RoyalFlush, "royal flush");
        Check(CashPoker.Evaluate(new[] { C(12, 1), C(0, 1), C(1, 1), C(2, 1), C(3, 1) }) == CashPoker.Rank.StraightFlush, "wheel straight flush");
        Check(CashPoker.Evaluate(new[] { C(12, 1), C(0, 2), C(1, 1), C(2, 3), C(3, 1) }) == CashPoker.Rank.Straight, "wheel straight");
        Check(CashPoker.Evaluate(new[] { C(5, 0), C(5, 1), C(5, 2), C(7, 0), C(7, 3) }) == CashPoker.Rank.FullHouse, "full house");
        Check(CashPoker.Evaluate(new[] { C(5, 0), C(5, 1), C(5, 2), C(5, 3), C(7, 3) }) == CashPoker.Rank.FourOfAKind, "four of a kind");
        Check(CashPoker.Evaluate(new[] { C(1, 2), C(4, 2), C(6, 2), C(9, 2), C(11, 2) }) == CashPoker.Rank.Flush, "flush");
        Check(CashPoker.Evaluate(new[] { C(1, 2), C(1, 0), C(6, 2), C(6, 1), C(11, 2) }) == CashPoker.Rank.TwoPair, "two pair");
        Check(CashPoker.Evaluate(new[] { C(1, 2), C(1, 0), C(6, 2), C(8, 1), C(11, 2) }) == CashPoker.Rank.Pair, "pair");
        Check(CashPoker.Evaluate(new[] { C(1, 2), C(3, 0), C(6, 2), C(8, 1), C(11, 2) }) == CashPoker.Rank.HighCard, "high card");
        Check(CashPoker.Partial(new[] { C(9, 2), C(9, 0) }) == CashPoker.Rank.Pair, "partial pair");
        Check(CashRules.DeptForMajor("Computer Science") == 0 && CashRules.DeptForMajor("Biomedical Engineering") == 0 &&
              CashRules.DeptForMajor("Psychology") == 1 && CashRules.DeptForMajor("Meme Economics") == 2 && CashRules.DeptForMajor("Professional Napping") == -1,
              "majors to departments");

        sb.AppendLine("ledger cases: " + (failures == 0 ? "pass" : failures + " failures"));
        return failures == 0;
    }
}
