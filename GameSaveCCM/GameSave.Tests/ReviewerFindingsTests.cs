using System.Diagnostics;
using System.Text;
using Newtonsoft.Json;

namespace GameSave.Tests;

/// <summary>
/// Regression tests for the independent review of the server rules (harnesses in the review's attack, runpeak, tokens and
/// timerule scripts): every reproduction must be caught now, and the honest play next to it must still pass.
/// </summary>
public class ReviewerFindingsTests
{
    const long Now = T.T0 + 120;   // two minutes after the last accept

    /// <summary>The review's company: twenty average employees, no board meeting yet ($1M on day 10 at the last accept).</summary>
    static SaveBuilder Company(double value = 1_000_000, int day = 11) =>
        new SaveBuilder { Value = value, Day = day, Employees = T.Staff(20), MeetingsHeld = 0 };

    static SaveMeta Meta() => T.Meta(seq: 5, day: 10, value: 1_000_000, acceptedAt: T.T0);

    static SubmitDecision Submit(SubmitRequest request, SaveBuilder previous = null, SaveMeta meta = null, CityTotals city = null, long now = Now) =>
        SaveRules.EvaluateSubmit(meta ?? Meta(), request, city ?? T.City(), now,
                                 new SaveSubmitContext { PreviousSaveJson = (previous ?? Company(1_000_000, 10)).Json() });

    // ================================================================================================================
    // B2: the growth flag could never fire (the restart bound took the new value and a 10x self-reported peak)
    // ================================================================================================================

    [TestCase(1.001e9)]
    [TestCase(1e12)]
    [TestCase(1e15)]
    public void B2_RunPeakTenTimesTheValue_StillFlagged(double value)
    {
        SubmitDecision plain = Submit(T.Req(6, 5, Company(value)));
        SubmitDecision peak = Submit(T.Req(6, 5, Company(value).With(s => s.RunPeak = value * 10)));
        Assert.That(T.HasFlag(plain.Meta, SaveCodes.FlagGrowth), Is.True, T.Why(plain));
        Assert.That(T.HasFlag(peak.Meta, SaveCodes.FlagGrowth), Is.True, T.Why(peak));
        Assert.That(peak.Growth.HearingRestart, Is.LessThan(value / 100), "10% of a peak the company can have reached, not of the claim");
        Assert.That(peak.Growth.FreshStartBound, Is.LessThan(value / 10));
        Assert.That(peak.Findings.Single(f => f.StartsWith(SaveCodes.FlagGrowth)), Does.Contain("run peak"));
    }

    [Test]
    public void B2_TheRestartIsNeverTenPercentOfTheValueBeingChecked()
    {
        // A new loan shows a hearing; still, the restart is 10% of the peak before it (at most what the stretch can reach)
        SaveBuilder before = Company(1_000_000, 10).With(s => { s.Restarts = 0; s.Loan = (0, 0.0, 0.0); });
        SaveBuilder after = Company(500_000_000).With(s => { s.Restarts = 0; s.Loan = (1, 101_000.0, 0.0); });
        SubmitDecision d = Submit(T.Req(6, 5, after), before);
        Assert.That(d.Growth.Hearings, Is.EqualTo(1), T.Why(d));
        Assert.That(d.Growth.HearingRestart, Is.LessThan(1_000_000), T.Why(d));
        Assert.That(T.HasFlag(d.Meta, SaveCodes.FlagGrowth), Is.True, T.Why(d));
    }

    [Test]
    public void B2_FreshStartNeedsAHearingTheSaveShows()
    {
        // Peak $500M, $5M at the last save, $50M now: only a bankruptcy restart (10% of the peak) explains it
        SaveMeta meta = T.Meta(seq: 5, day: 10, value: 5_000_000, acceptedAt: T.T0);
        meta.Peak = 500_000_000;
        SaveBuilder before = Company(5_000_000, 10).With(s => { s.RunPeak = 500_000_000; s.Restarts = 2; s.Loan = (1, 0.0, 60_000.0); });
        SaveBuilder after = Company(50_000_000).With(s => { s.RunPeak = 500_000_000; s.Restarts = 2; s.Loan = (1, 0.0, 60_000.0); });

        SubmitDecision none = Submit(T.Req(6, 5, after), before, meta);
        Assert.That(none.Growth.Hearings, Is.EqualTo(0));
        Assert.That(T.HasFlag(none.Meta, SaveCodes.FlagGrowth), Is.True, "no hearing shown, no restart: " + T.Why(none));

        // A coin-flip restart (runStats "restarts" up), a new loan, a forgiven loan or a bust in the ledger shows one
        var shown = new Dictionary<string, (SaveBuilder Before, SaveBuilder After, LedgerEntry[] Ledger)>
        {
            ["restart"] = (before, after.With(s => s.Restarts = 3), new LedgerEntry[0]),
            ["loan"] = (before, after.With(s => s.Loan = (2, 50_001_000.0, 60_000.0)), new LedgerEntry[0]),
            ["forgiven"] = (before.With(s => s.Loan = (1, 80_000.0, 60_000.0)), after, new LedgerEntry[0]),
            ["bust"] = (before, after, new[] { T.Gamble(1, "casino", "blackjack", 5_000_000, 0, 5_000_000, 10) })
        };
        foreach (var pair in shown)
        {
            SubmitDecision d = Submit(T.Req(6, 5, pair.Value.After, pair.Value.Ledger), pair.Value.Before, meta);
            Assert.That(d.Growth.Hearings, Is.EqualTo(1), pair.Key + ": " + T.Why(d));
            Assert.That(d.Meta.Flagged, Is.False, pair.Key + ": " + T.Why(d));
        }

        // Today's client doesn't write the restart counter, so a coin flip after a daily-cost bankruptcy can't show: a save
        // without the counter keeps one restart (bounded by the verified peak)
        SubmitDecision legacy = Submit(T.Req(6, 5, after.With(s => s.Restarts = null)), before.With(s => s.Restarts = null), meta);
        Assert.That(legacy.Meta.Flagged, Is.False, T.Why(legacy));
        Assert.That(legacy.Growth.HearingEvidence, Does.Contain("restart counter"));

        // Never more than 10% of the peak, whatever shows it
        SubmitDecision tooRich = Submit(T.Req(6, 5, Company(150_000_000).With(s => { s.RunPeak = 500_000_000; s.Restarts = 3; s.Loan = (1, 0.0, 60_000.0); })), before, meta);
        Assert.That(T.HasFlag(tooRich.Meta, SaveCodes.FlagGrowth), Is.True, T.Why(tooRich));
    }

    [Test]
    public void B2_ARunPeakFromAnEarlierStretch_IsNoFinding()
    {
        // The company peaked at $10M between two earlier saves (checked then) and is at $2M now: its run peak stays $10M
        SaveMeta meta = T.Meta(seq: 5, day: 10, value: 2_000_000, acceptedAt: T.T0);
        SaveBuilder before = Company(2_000_000, 10).With(s => s.RunPeak = 10_000_000);
        SubmitDecision d = Submit(T.Req(6, 5, Company(2_100_000).With(s => s.RunPeak = 10_000_000)), before, meta);
        Assert.That(d.Meta.Flagged, Is.False, T.Why(d));
        Assert.That(d.Growth.PeakBound, Is.GreaterThanOrEqualTo(10_000_000));
    }

    // ================================================================================================================
    // M1: the ledger walked against a running bound; summaries; rare payouts
    // ================================================================================================================

    [Test]
    public void M1_SelfReportedValueBefore_CantExplainAJackpot()
    {
        // attack 2: one slots entry claiming $2B before it (the server accepted $1M) and paying 500x: $1T explained
        LedgerEntry jackpot = T.Gamble(1, "casino", "slots", 2.004e9, 1.002e12, 2.004e9, 11);
        SubmitDecision d = Submit(T.Req(6, 5, Company(1e12), jackpot));
        Assert.That(T.HasFlag(d.Meta, SaveCodes.FlagLedger), Is.True, T.Why(d));
        Assert.That(T.HasFlag(d.Meta, SaveCodes.FlagGrowth), Is.True, T.Why(d));
        Assert.That(d.Findings.First(f => f.StartsWith(SaveCodes.FlagLedger)), Does.Contain("can have had at most"));

        // The same 500x jackpot on a stake the company had is fine
        LedgerEntry real = T.Gamble(1, "casino", "slots", 1_000_000, 500_000_000, 1_000_000, 11);
        SubmitDecision honest = Submit(T.Req(6, 5, Company(500_000_000), real));
        Assert.That(honest.Meta.Flagged, Is.False, T.Why(honest));
    }

    [Test]
    public void M1_ALaterEntry_CantStakeWhatALossTookAway()
    {
        // Everything ($1M) lost on a flip, then a $3M stake the company can't have had even after a bankruptcy restart
        SubmitDecision d = Submit(T.Req(6, 5, Company(1_500_000_000),
            T.Flip(1, 1_000_000, false, 1_000_000, 11),
            T.Gamble(2, "casino", "slots", 3_000_000, 1_500_000_000, 3_000_000, 11)));
        Assert.That(T.HasFlag(d.Meta, SaveCodes.FlagLedger), Is.True, T.Why(d));
        Assert.That(T.HasFlag(d.Meta, SaveCodes.FlagGrowth), Is.True, T.Why(d));
    }

    [Test]
    public void M1_WinlessSummary_PaysAtMostAPush()
    {
        // attack 3: a summary of 1 round with 0 wins "paid" $1T: a loss to the luck tally, a fortune to the growth check
        SubmitDecision d = Submit(T.Req(6, 5, Company(1e12), T.Summary(1, "casino", "slots", 1, 0, 2.004e9, 1e12)));
        Assert.That(T.HasFlag(d.Meta, SaveCodes.FlagLedger), Is.True, T.Why(d));
        Assert.That(T.HasFlag(d.Meta, SaveCodes.FlagGrowth), Is.True, T.Why(d));

        // Without a win, slots, flips and tilt pay nothing back; blackjack at most pushes (the stake back)
        bool Flagged(LedgerEntry summary) => T.HasFlag(Submit(T.Req(6, 5, Company(1_000_000), summary)).Meta, SaveCodes.FlagLedger);
        Assert.That(Flagged(T.Summary(1, "casino", "blackjack", 10, 0, 50_000, 50_000)), Is.False, "ten pushes");
        Assert.That(Flagged(T.Summary(1, "casino", "blackjack", 10, 0, 50_000, 50_001)), Is.True);
        Assert.That(Flagged(T.Summary(1, "casino", "coinflip", 10, 0, 50_000, 1)), Is.True);
        Assert.That(Flagged(T.Summary(1, "tilt", null, 10, 0, 50_000, 0)), Is.False);
        Assert.That(Flagged(T.Summary(1, "casino", "slots", 10, 3, 50_000, 60_000)), Is.False, "wins pay up to their multiple");
    }

    [Test]
    public void M1_SummaryStakes_AtMostCountTimesTheBound()
    {
        // Ten folded rounds staking $500M in total from a $1M company
        SubmitDecision d = Submit(T.Req(6, 5, Company(1_200_000_000), T.Summary(1, "casino", "blackjack", 10, 6, 500_000_000, 1_250_000_000)));
        Assert.That(T.HasFlag(d.Meta, SaveCodes.FlagLedger), Is.True, T.Why(d));
        Assert.That(T.HasFlag(d.Meta, SaveCodes.FlagGrowth), Is.True, T.Why(d));
    }

    [Test]
    public void M1_CardsListedAheadOfFoldedRounds_SeeTheirWinnings()
    {
        // The client folds the oldest gambles into a summary at the index of the newest one; a card played after some of those
        // rounds is listed ahead of the summary and may have seen their winnings
        LedgerEntry card = T.Card(2, "high", true, 15_000_000, 100_000, 11);
        LedgerEntry rounds = T.Summary(5, "casino", "slots", 500, 245, 500_000, 20_000_000);
        SubmitDecision d = Submit(T.Req(6, 5, Company(20_600_000), card, rounds));
        Assert.That(d.Meta.Flagged, Is.False, T.Why(d));

        // A single gamble is never older than the folded rounds (the oldest are folded): it gets no such lead
        LedgerEntry bet = T.Gamble(2, "casino", "coinflip", 10_000_000, 0, 15_000_000, 11);
        SubmitDecision early = Submit(T.Req(6, 5, Company(10_500_000), bet, rounds));
        Assert.That(T.HasFlag(early.Meta, SaveCodes.FlagLedger), Is.True, T.Why(early));
    }

    [Test]
    public void M1_FakeJackpots_WithAnHonestWinRate_FlaggedAsLuck()
    {
        // attack 5: 100 spins, 49 wins (the honest rate), but every win a 500x jackpot
        var entries = new List<LedgerEntry>();
        double v = 1_000_000;
        for (int i = 1; i <= 100; i++)
        {
            bool win = i % 2 == 0 && i <= 98;
            entries.Add(T.Gamble(i, "casino", "slots", 10_000, win ? 5_000_000 : 0, v, 11));
            v += win ? 4_990_000 : -10_000;
        }
        SubmitDecision d = Submit(T.Req(6, 5, Company(v), entries.ToArray()));
        Assert.That(T.HasFlag(d.Meta, SaveCodes.FlagLuck), Is.True, T.Why(d));
        Assert.That(d.Meta.Luck.Gambles.Z, Is.LessThan(1), "the win count alone looks honest");
    }

    [Test]
    public void M1_RarePayouts_FlaggedOnlyBelowOneInABillion()
    {
        SubmitDecision Spins(int jackpots)
        {
            // 60 spins of $10k, the first ones hitting three diamonds (500x)
            var entries = new List<LedgerEntry>();
            double v = 1_000_000;
            for (int i = 1; i <= 60; i++)
            {
                double payout = i <= jackpots ? 5_000_000 : 0;
                entries.Add(T.Gamble(i, "casino", "slots", 10_000, payout, v, 11));
                v += payout - 10_000;
            }
            return Submit(T.Req(6, 5, Company(v), entries.ToArray()));
        }
        // Two jackpots in 60 spins: honest odds about 2 in a billion, not flagged; three: flagged
        Assert.That(Spins(1).Meta.Flagged, Is.False, T.Why(Spins(1)));
        Assert.That(Spins(2).Meta.Flagged, Is.False, T.Why(Spins(2)));
        SubmitDecision three = Spins(3);
        Assert.That(T.HasFlag(three.Meta, SaveCodes.FlagLuck), Is.True, T.Why(three));
        Assert.That(three.Findings.Single(f => f.StartsWith(SaveCodes.FlagLuck)), Does.Contain("500x or more"));
    }

    [Test]
    public void M1_CardJackpots_Tallied()
    {
        // Gains above what every card skill but the Jackpot allows needed a Jackpot (at most 3.1% of good cards): ten in a row
        var entries = new List<LedgerEntry>();
        double v = 1_000_000;
        for (int i = 1; i <= 10; i++)
        {
            double gain = 5_000_000 + v;
            entries.Add(T.Card(i, "high", true, v, gain, 10 + i / 2));
            v += gain;
        }
        SubmitDecision d = Submit(T.Req(6, 5, Company(v, 17), entries.ToArray()));
        Assert.That(T.HasFlag(d.Meta, SaveCodes.FlagLedger), Is.False, "each card is within its bound: " + T.Why(d));
        Assert.That(T.HasFlag(d.Meta, SaveCodes.FlagLuck), Is.True, T.Why(d));
        Assert.That(d.Findings.Single(f => f.StartsWith(SaveCodes.FlagLuck)), Does.Contain("jackpots"));

        // One jackpot among them is luck, not a finding
        SubmitDecision one = Submit(T.Req(6, 5, Company(1_000_000 + 6_000_000 + 100_000), T.Card(1, "high", true, 1_000_000, 6_000_000, 11),
                                         T.Card(2, "high", true, 7_000_000, 100_000, 11)));
        Assert.That(one.Meta.Flagged, Is.False, T.Why(one));
    }

    [Test]
    public void M1_SaleOfSharesTheCompanyCantHaveHeld_Flagged()
    {
        // No shares at the last save and about $1M to buy some: shares that cost $50M can't have been sold
        SubmitDecision d = Submit(T.Req(6, 5, Company(60_000_000), T.Trade(1, "MOON", 50_000_000, 60_000_000, 11)));
        Assert.That(T.HasFlag(d.Meta, SaveCodes.FlagLedger), Is.True, T.Why(d));

        // Shares held at the last save can be
        SubmitDecision held = Submit(T.Req(6, 5, Company(61_000_000), T.Trade(1, "MOON", 50_000_000, 60_000_000, 11)),
                                     Company(1_000_000, 10).With(s => s.Holdings = new() { ("MOON", 10_000_000, 5.0) }));
        Assert.That(held.Meta.Flagged, Is.False, T.Why(held));
    }

    // ================================================================================================================
    // M2: new staff
    // ================================================================================================================

    [Test]
    public void M2_TwentyThousandNewStaffInOneUpload_Flagged()
    {
        // attack 1: 20,000 perfect employees appear in one upload (days 10 to 14); the allowance counted them all
        SaveBuilder fake = Company(1_500_000_000, 14).With(s => s.Employees = s.Employees.Concat(T.Staff(20_000, iq: 170, gpa: 4.0, exp: 50, phd: true, prefix: "x")).ToList());
        SubmitDecision d = Submit(T.Req(6, 5, fake));
        Assert.That(T.HasFlag(d.Meta, SaveCodes.FlagLedger), Is.True, T.Why(d));
        Assert.That(d.Growth.Allowance.NewStaff, Is.EqualTo(20_000));
        Assert.That(d.Growth.Allowance.HireCap, Is.EqualTo(227), "1 hire a second of five days' clock (225 s), +2");
        Assert.That(d.Growth.Allowance.Headcount, Is.EqualTo(20 + 227), "only the best hire-cap of them count");

        // Honest hiring (100 new people in those four days) is fine
        SaveBuilder hired = Company(1_000_000, 14).With(s => s.Employees = s.Employees.Concat(T.Staff(100, prefix: "h")).ToList());
        SubmitDecision honest = Submit(T.Req(6, 5, hired));
        Assert.That(honest.Meta.Flagged, Is.False, T.Why(honest));
        Assert.That(honest.Growth.Allowance.NewStaff, Is.EqualTo(100));
    }

    [Test]
    public void M2_TheFastestHonestHiring_StaysUnderTheCap()
    {
        // The game's fastest hire is ~1.3 s (click, 0.35 s stamp, 0.5 s flight, Next batch, 0.15 s entrance). Four days are at
        // most 225 s of running clock (the cap counts every day as 45 s): 225 / 1.3 = 173 hires, well under 227
        Assert.That(SaveCalibration.MaxHiresPerRunSecond, Is.EqualTo(1.0));
        int fastest = (int)Math.Floor(SaveCalibration.RunSeconds(4) / 1.3);
        SaveBuilder spree = Company(1_000_000, 14).With(s => s.Employees = s.Employees.Concat(T.Staff(fastest, iq: 150, gpa: 3.8, exp: 30, prefix: "f")).ToList());
        SubmitDecision d = Submit(T.Req(6, 5, spree));
        Assert.That(d.Meta.Flagged, Is.False, T.Why(d));
        Assert.That(d.Growth.Allowance.NewStaff, Is.EqualTo(173));
        Assert.That(d.Growth.Allowance.Headcount, Is.EqualTo(20 + 173), "every one of them counts");

        // More than the cap is a finding
        SaveBuilder tooMany = Company(1_000_000, 14).With(s => s.Employees = s.Employees.Concat(T.Staff(228, prefix: "f")).ToList());
        Assert.That(T.HasFlag(Submit(T.Req(6, 5, tooMany)).Meta, SaveCodes.FlagLedger), Is.True);
    }

    [Test]
    public void M2_StaffWithoutIds_AreTheSamePeopleAsBefore()
    {
        // Saves from before employee ids: 50 people without ids stay 50 people, not 50 new hires on every save
        List<SaveEmployeeInfo> NoIds(int n) => T.Staff(n).Select(e => { e.Id = null; return e; }).ToList();
        SaveBuilder before = Company(1_000_000, 10).With(s => s.Employees = NoIds(50));
        SubmitDecision same = Submit(T.Req(6, 5, Company().With(s => s.Employees = NoIds(50))), before);
        Assert.That(same.Growth.Allowance.NewStaff, Is.EqualTo(0));
        Assert.That(same.Growth.Allowance.HireImpact, Is.EqualTo(0));
        Assert.That(same.Growth.Allowance.Headcount, Is.EqualTo(50));

        SubmitDecision more = Submit(T.Req(6, 5, Company().With(s => s.Employees = NoIds(60))), before);
        Assert.That(more.Growth.Allowance.NewStaff, Is.EqualTo(10));
        Assert.That(more.Growth.Allowance.Headcount, Is.EqualTo(60));
    }

    // ================================================================================================================
    // M3: imports
    // ================================================================================================================

    [Test]
    public void M3_ImportsCapped()
    {
        SubmitDecision Import(SaveBuilder save) => SaveRules.EvaluateSubmit(null, T.Req(1, 0, save), T.City(), Now);

        // attack 4: an import at day 2000 with $1e18, and at day 500 with $1e15 (the uncapped curve allowed both)
        Assert.That(T.HasFlag(Import(new SaveBuilder { Value = 1e18, Day = 2000, Employees = T.Staff(5) }).Meta, SaveCodes.FlagImport), Is.True);
        Assert.That(T.HasFlag(Import(new SaveBuilder { Value = 1e15, Day = 500, Employees = T.Staff(5) }).Meta, SaveCodes.FlagImport), Is.True);

        // Implausibly far in, however modest; or a run peak above anything plausible that day
        SubmitDecision far = Import(new SaveBuilder { Value = 2_000_000, Day = 150_000 });
        Assert.That(T.HasFlag(far.Meta, SaveCodes.FlagImport), Is.True, T.Why(far));
        SubmitDecision peak = Import(new SaveBuilder { Value = 500_000, Day = 30, RunPeak = 5e11 });
        Assert.That(T.HasFlag(peak.Meta, SaveCodes.FlagImport), Is.True, T.Why(peak));

        // A rich honest import far enough in is fine; negative days don't exist
        SubmitDecision rich = Import(new SaveBuilder { Value = 5e9, Day = 300, Employees = T.Staff(50) });
        Assert.That(rich.Meta.Flagged, Is.False, T.Why(rich));
        Assert.That(Import(new SaveBuilder { Value = 60_000, Day = -3 }).Code, Is.EqualTo(SaveCodes.Invalid));
    }

    // ================================================================================================================
    // M4: token budget
    // ================================================================================================================

    [Test]
    public void M4_TinyPackedSaveOfMillionsOfTokens_InvalidAndFast()
    {
        // tokens/Program.cs: an ~11 KB packed save that unpacks to an 8 MB list of four million zeros
        var sb = new StringBuilder("{\"companyValue\":1000000.0,\"currentDay\":11,\"employees\":[");
        int n = (8 * 1024 * 1024 - 200) / 2;
        for (int i = 0; i < n; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append('0');
        }
        sb.Append("]}");
        string packed = SavePacking.Pack(sb.ToString());
        Assert.That(packed.Length, Is.LessThan(20_000));

        var request = new SubmitRequest { RunId = T.Run, Seq = 6, BaseSeq = 5, Day = 11, Value = 1_000_000, SaveGz = packed };
        var watch = Stopwatch.StartNew();
        SubmitDecision d = SaveRules.EvaluateSubmit(Meta(), request, T.City(), Now, new SaveSubmitContext { PreviousSaveJson = packed });
        watch.Stop();
        Assert.That(d.Code, Is.EqualTo(SaveCodes.Invalid), T.Why(d));
        Assert.That(watch.ElapsedMilliseconds, Is.LessThan(3000));
        Assert.That(SaveSnapshot.ParseStored(packed).Ok, Is.False, "the same as a stored previous save is refused too");

        // Millions of tokens where the rules skip (an unknown field): the budget stops it
        var junk = new StringBuilder("{\"companyValue\":1,\"currentDay\":1,\"junk\":[");
        for (int i = 0; i < 1_500_000; i++) junk.Append(i == 0 ? "0" : ",0");
        junk.Append("]}");
        SaveSnapshot skipped = SaveSnapshot.Parse(junk.ToString());
        Assert.That(skipped.Ok, Is.False);
        Assert.That(skipped.Error, Does.Contain("tokens"));

        // Shapes JsonUtility never writes
        foreach (string json in new[]
                 {
                     "{\"companyValue\":1,\"currentDay\":1,\"employees\":{}}",
                     "{\"companyValue\":1,\"currentDay\":1,\"extras\":[1,2]}",
                     "{\"companyValue\":1,\"currentDay\":1} trailing"
                 })
            Assert.That(SaveSnapshot.Parse(json).Ok, Is.False, json);
    }

    [Test]
    public void M4_BigHonestSave_WithAFullLedger_IsQuick()
    {
        // attack 7: 12,000 employees and a 20,000-entry ledger, packed like the client sends it
        SaveBuilder big = Company(1_000_000, 14).With(s => s.Employees = s.Employees.Concat(T.Staff(12_000, prefix: "b")).ToList());
        SubmitRequest request = T.Req(6, 5, big, Enumerable.Range(1, 20_000).Select(i => T.Flip(i, 10, i % 2 == 0, 1_000_000, 11)).ToArray());
        request.SaveGz = SavePacking.Pack(request.SaveJson);
        request.SaveJson = null;
        var watch = Stopwatch.StartNew();
        SubmitDecision d = SaveRules.EvaluateSubmit(Meta(), request, T.City(), Now, new SaveSubmitContext { PreviousSaveJson = request.SaveGz });
        watch.Stop();
        Assert.That(d.Accepted, Is.True, T.Why(d));
        Assert.That(watch.ElapsedMilliseconds, Is.LessThan(5000));
    }

    // ================================================================================================================
    // Follow-up: the client's restart counter (RunStats.State.restarts, +1 in RunEndDirector.ApplyCoinFlip on heads)
    // ================================================================================================================

    [Test]
    public void RestartCounter_ReadFromTheRealRunStatsShape()
    {
        // RunStats.State exactly as JsonUtility writes it (every field, in declaration order), inside the "runStats" extra
        const string runStats = "{\"peakValue\":1234567.0,\"hires\":12,\"goldenHires\":1,\"riskCardsPlayed\":4,\"biggestWin\":50000.0," +
                                "\"biggestWinTitle\":\"Viral Ad\",\"biggestLoss\":-20000.0,\"biggestLossTitle\":\"\",\"crisesSolved\":3," +
                                "\"crisesTotal\":4,\"casinoWon\":1000.0,\"casinoBet\":2000.0,\"winShown\":false,\"restarts\":2}";
        string json = "{\"companyValue\":1000000.0,\"currentDay\":12,\"employees\":[],\"extras\":[{\"key\":\"runStats\",\"json\":" + JsonConvert.ToString(runStats) + "}]}";
        SaveSnapshot save = SaveSnapshot.Parse(json);
        Assert.That(save.Ok, Is.True, save.Error);
        Assert.That(save.HasRestarts, Is.True);
        Assert.That(save.Restarts, Is.EqualTo(2));
        Assert.That(save.RunPeak, Is.EqualTo(1234567.0));

        // A save written before the counter existed (the real JsonUtility fixture) doesn't have it
        SaveSnapshot old = SaveSnapshot.Parse(File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "jsonutility_save.json")));
        Assert.That(old.HasRestarts, Is.False);
        Assert.That(old.Restarts, Is.EqualTo(0));
    }

    [Test]
    public void RestartCounter_TheFirstSaveAfterTheClientUpdate()
    {
        // The previous save came from an older build (no counter), this one counts: only a rise shows a restart
        SaveMeta meta = T.Meta(seq: 5, day: 10, value: 5_000_000, acceptedAt: T.T0);
        meta.Peak = 500_000_000;
        SaveBuilder before = Company(5_000_000, 10).With(s => { s.RunPeak = 500_000_000; s.Restarts = null; });
        SaveBuilder noRestart = Company(50_000_000).With(s => { s.RunPeak = 500_000_000; s.Restarts = 0; });

        SubmitDecision none = Submit(T.Req(6, 5, noRestart), before, meta);
        Assert.That(none.Growth.Hearings, Is.EqualTo(0), T.Why(none));
        Assert.That(T.HasFlag(none.Meta, SaveCodes.FlagGrowth), Is.True, "the counter is there and didn't rise: " + T.Why(none));

        SubmitDecision restarted = Submit(T.Req(6, 5, noRestart.With(s => s.Restarts = 1)), before, meta);
        Assert.That(restarted.Growth.HearingEvidence, Does.Contain("1 restart"));
        Assert.That(restarted.Meta.Flagged, Is.False, T.Why(restarted));

        // Both saves from before the counter: the fallback (one restart allowed) still applies
        SubmitDecision legacy = Submit(T.Req(6, 5, noRestart.With(s => s.Restarts = null)), before, meta);
        Assert.That(legacy.Meta.Flagged, Is.False, T.Why(legacy));
    }

    // ================================================================================================================
    // Follow-up: rare payouts counted over the whole run (meta.Luck.Rare)
    // ================================================================================================================

    /// <summary>
    /// Uploads two minutes apart, each of 20 slots spins at $1,000 (all lost unless <paramref name="jackpotIn"/> says the
    /// upload's first spin hits three diamonds, 500x), plus <paramref name="padding"/> made-up $1 losing spins.
    /// </summary>
    static List<SubmitDecision> SlotsUploads(int uploads, Func<int, bool> jackpotIn, int padding = 0)
    {
        var decisions = new List<SubmitDecision>();
        SaveMeta meta = Meta();
        double value = 1_000_000;
        SaveBuilder previous = Company(value, 10).With(s => s.Restarts = 0);
        long index = 0, now = T.T0;
        int day = 10;
        for (int u = 1; u <= uploads; u++)
        {
            var entries = new List<LedgerEntry>();
            for (int i = 0; i < 20 + padding; i++)
            {
                double stake = i < 20 ? 1000 : 1;
                double payout = i == 0 && jackpotIn(u) ? stake * 500 : 0;
                entries.Add(T.Gamble(++index, "casino", "slots", stake, payout, value, day));
                value += payout - stake;
            }
            now += 120;
            day += 2;
            SaveBuilder save = Company(value, day).With(s => s.Restarts = 0);
            SubmitDecision d = SaveRules.EvaluateSubmit(meta, T.Req(meta.Seq + 1, meta.Seq, save, entries.ToArray()), T.City(), now,
                                                        new SaveSubmitContext { PreviousSaveJson = previous.Json() });
            decisions.Add(d);
            meta = d.Meta;
            previous = save;
        }
        return decisions;
    }

    [Test]
    public void RunRareTally_OneFakeJackpotPerUpload_AddsUp()
    {
        // One fake 500x jackpot in every upload: never enough within one upload, but the run's tally adds them up
        List<SubmitDecision> run = SlotsUploads(10, u => true);
        for (int u = 0; u < 3; u++) Assert.That(run[u].Meta.Flagged, Is.False, "upload " + (u + 1) + ": " + T.Why(run[u]));
        SubmitDecision caught = run.FirstOrDefault(d => T.HasFlag(d.Meta, SaveCodes.FlagLuck));
        Assert.That(caught, Is.Not.Null, "ten fake jackpots in 200 spins: " + T.Why(run.Last()));
        Assert.That(caught.Findings.Single(f => f.StartsWith(SaveCodes.FlagLuck)), Does.Contain("this run"));
        TestContext.Out.WriteLine("caught at upload " + (run.IndexOf(caught) + 1) + ": " + T.Why(caught));
        Assert.That(run.Last().Meta.Luck.Rare.Wins, Is.EqualTo(10));
        Assert.That(run.Last().Meta.Luck.Rare.Count, Is.EqualTo(200));
    }

    [Test]
    public void RunRareTally_AnHonestRunWithOneRealJackpot_IsFine()
    {
        List<SubmitDecision> run = SlotsUploads(10, u => u == 5);
        SubmitDecision flagged = run.FirstOrDefault(d => d.Meta.Flagged);
        Assert.That(flagged, Is.Null, flagged == null ? "" : T.Why(flagged));
        Assert.That(run.Last().Meta.Luck.Rare.Wins, Is.EqualTo(1));
        Assert.That(run.Last().Meta.Luck.Rare.Expected, Is.EqualTo(200 * 0.0015).Within(1e-9));
    }

    [Test]
    public void RunRareTally_PaddingWithMadeUpSpins_CantDiluteIt()
    {
        // Each upload also claims 5,000 losing $1 spins; two minutes (+ the upload grace) hold at most 82 at 2.5 s a spin, so
        // only that many count as trials, and the fake jackpots still add up
        List<SubmitDecision> run = SlotsUploads(20, u => true, padding: 5000);
        SubmitDecision caught = run.FirstOrDefault(d => T.HasFlag(d.Meta, SaveCodes.FlagLuck));
        Assert.That(caught, Is.Not.Null, T.Why(run.Last()));
        TestContext.Out.WriteLine("caught at upload " + (run.IndexOf(caught) + 1));
        Assert.That(run.Last().Meta.Luck.Rare.Count, Is.LessThanOrEqualTo(20 * 82));
    }

    [Test]
    public void RunRareTally_AMountainOfFoldedRounds_CantDiluteTheGambleTally()
    {
        // A summary claiming a hundred million rounds in two minutes counts as at most what the time holds (win rate kept)
        LedgerEntry mountain = T.Summary(1, "casino", "coinflip", 100_000_000, 50_000_000, 100_000_000, 97_500_000);
        mountain.ValueBefore = 100_000_000;
        SubmitDecision d = Submit(T.Req(6, 5, Company(1_000_000), mountain));
        Assert.That(d.Meta.Luck.Gambles.Count, Is.LessThanOrEqualTo(SaveRules.MaxLedgerEntries + 5 * 180));
        Assert.That(d.Meta.Luck.Gambles.Wins / d.Meta.Luck.Gambles.Count, Is.EqualTo(0.5).Within(0.001), "the same win rate");
    }

    [Test]
    public void RunRareTally_OldMetasWithoutIt_StartClean()
    {
        SaveMeta old = JsonConvert.DeserializeObject<SaveMeta>("{\"v\":1,\"run\":\"run-1\",\"seq\":5,\"day\":10,\"value\":1000000,\"peak\":1000000,\"acceptedAt\":" +
                                                               T.T0 + ",\"luck\":{\"g\":{\"n\":3,\"w\":1,\"e\":1.5,\"var\":0.75},\"c\":null,\"r\":null}}");
        Assert.That(old.Luck.Rare, Is.Null);
        Assert.That(SaveRules.CloneMeta(old).Luck.Rare, Is.Not.Null);
        SubmitDecision d = Submit(T.Req(6, 5, Company(999_000), T.Gamble(1, "casino", "slots", 1000, 0, 1_000_000, 11)), meta: old);
        Assert.That(d.Accepted, Is.True, T.Why(d));
        Assert.That(d.Meta.Luck.Rare.Count, Is.EqualTo(1));
        Assert.That(d.Meta.Luck.Gambles.Count, Is.EqualTo(4));
        Assert.That(JsonConvert.SerializeObject(d.Meta), Does.Contain("\"r\":{"));
    }

    // ================================================================================================================
    // Minor: the time rule's upload grace
    // ================================================================================================================

    [Test]
    public void TimeRule_UploadGrace()
    {
        // The last save was written ~50 s before the server accepted it (in flight); the next arrives 30 s after that accept,
        // 8 days later by the game's clock. floor(30 / 15 x 1.1) + 3 = 5 would refuse it; with +60 s: floor(6.6) + 3 = 9.
        SubmitDecision d = Submit(T.Req(6, 5, Company(1_000_000, 18)), now: T.T0 + 30);
        Assert.That(d.Accepted, Is.True, T.Why(d));
        Assert.That(Submit(T.Req(6, 5, Company(1_000_000, 20)), now: T.T0 + 30).Code, Is.EqualTo(SaveCodes.Time));
    }
}
