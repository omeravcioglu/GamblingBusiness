using Newtonsoft.Json;

namespace GameSave.Tests;

/// <summary>SaveRules: every hard rejection and soft check, one at a time.</summary>
public class SaveRulesTests
{
    const long Now = T.T0 + 120;

    /// <summary>A small company: $1M on day 10, five average employees; a submit two days and 120 s later.</summary>
    static SaveBuilder Small(double value = 1_000_000, int day = 12) =>
        new SaveBuilder { Value = value, Day = day, Employees = T.Staff(5), MeetingsHeld = 0 };

    /// <summary>The save stored before (same staff, so no hires in between).</summary>
    static SaveSubmitContext Previous(SaveBuilder save, params SaveCityTransfer[] transfers) =>
        new SaveSubmitContext { PreviousSaveJson = save.Json(), CityTransfers = transfers.ToList() };

    static SubmitDecision Submit(SaveMeta meta, SubmitRequest request, CityTotals city = null, long now = Now, SaveSubmitContext context = null) =>
        SaveRules.EvaluateSubmit(meta, request, city ?? T.City(), now, context);

    static SubmitDecision SubmitSmall(SaveBuilder save, params LedgerEntry[] ledger) =>
        Submit(T.Meta(), T.Req(2, 1, save, ledger), T.City(), Now, Previous(Small(1_000_000, 10)));

    // ================================================================================================================
    // Runs
    // ================================================================================================================

    [Test]
    public void NewRunMeta_StartsFresh()
    {
        SaveMeta meta = SaveRules.NewRunMeta(T.Run, T.T0);
        Assert.That(meta.RunId, Is.EqualTo(T.Run));
        Assert.That(meta.Seq, Is.EqualTo(0));
        Assert.That(meta.Value, Is.EqualTo(50000));
        Assert.That(meta.StartedAt, Is.EqualTo(T.T0));
        Assert.That(meta.AcceptedAt, Is.EqualTo(0), "no previous meta: no time baseline");
        Assert.That(SaveRules.IsFirstSaveWithoutBaseline(meta), Is.True);
        Assert.That(meta.Flagged, Is.False);
        Assert.That(meta.Ended, Is.False);
    }

    [Test]
    public void NewRunMeta_TakesCityBaseline()
    {
        SaveMeta meta = SaveRules.NewRunMeta(T.Run, T.T0, T.City(700, 300));
        Assert.That(meta.CityDeposited, Is.EqualTo(700));
        Assert.That(meta.CityWithdrawn, Is.EqualTo(300));
        Assert.That(SaveRules.NewRunMeta(T.Run, T.T0, T.NoCity).CityDeposited, Is.EqualTo(0));
    }

    [TestCase("run-1", true)]
    [TestCase("a_B-9", true)]
    [TestCase("", false)]
    [TestCase(null, false)]
    [TestCase("has space", false)]
    [TestCase("slash/run", false)]
    [TestCase("ünicode", false)]
    public void RunIds_Validated(string runId, bool valid)
    {
        Assert.That(SaveRules.IsValidRunId(runId), Is.EqualTo(valid));
    }

    [Test]
    public void RunIds_LongerThan64_Rejected()
    {
        Assert.That(SaveRules.IsValidRunId(new string('a', 64)), Is.True);
        Assert.That(SaveRules.IsValidRunId(new string('a', 65)), Is.False);
    }

    [Test]
    public void EndRunMeta_EndsOnlyItsRun()
    {
        SaveMeta meta = T.Meta();
        SaveMeta ended = SaveRules.EndRunMeta(meta, T.Run, Now);
        Assert.That(ended.Ended, Is.True);
        Assert.That(meta.Ended, Is.False, "the input isn't changed");
        Assert.That(ended.Value, Is.EqualTo(meta.Value));

        SaveMeta other = SaveRules.EndRunMeta(meta, "other-run", Now);
        Assert.That(other, Is.SameAs(meta));
        Assert.That(SaveRules.EndRunMeta(null, T.Run, Now), Is.Null);
        Assert.That(SaveRules.IsLiveRun(meta, T.Run), Is.True);
        Assert.That(SaveRules.IsLiveRun(ended, T.Run), Is.False);
    }

    [Test]
    public void NewRun_ThenFirstSubmit_Accepted()
    {
        SaveMeta meta = SaveRules.NewRunMeta(T.Run, T.T0, T.City(1000, 0));
        var save = new SaveBuilder { Value = 50_632, Day = 1, Employees = T.Staff(1) };
        SubmitDecision d = Submit(meta, T.Req(1, 0, save, T.Flip(1, 100, true, 50_437)), T.City(1000, 0), T.T0 + 30);

        Assert.That(d.Accepted, Is.True, T.Why(d));
        Assert.That(d.NewFlags, Is.Empty, T.Why(d));
        Assert.That(d.Meta.Seq, Is.EqualTo(1));
        Assert.That(d.Meta.Day, Is.EqualTo(1));
        Assert.That(d.Meta.Value, Is.EqualTo(50_632));
        Assert.That(d.Meta.Peak, Is.EqualTo(50_632));
        Assert.That(d.Meta.AcceptedAt, Is.EqualTo(T.T0 + 30));
        Assert.That(d.Meta.StartedAt, Is.EqualTo(T.T0), "a run keeps its start");
        Assert.That(d.Meta.LedgerIndex, Is.EqualTo(1));
        Assert.That(d.Meta.Luck.Gambles.Count, Is.EqualTo(1));
        Assert.That(d.Meta.CityDeposited, Is.EqualTo(1000));
        Assert.That(meta.Seq, Is.EqualTo(0), "the stored meta isn't changed in place");
    }

    // ================================================================================================================
    // The chain
    // ================================================================================================================

    [Test]
    public void Stale_SeqNotNewer_Rejected()
    {
        SaveMeta meta = T.Meta(seq: 5);
        SubmitDecision d = Submit(meta, T.Req(5, 5, Small()));
        Assert.That(d.Code, Is.EqualTo(SaveCodes.Stale), T.Why(d));
        Assert.That(d.Meta, Is.SameAs(meta), "rejections hand back the stored meta unchanged");
        Assert.That(Submit(meta, T.Req(4, 5, Small())).Code, Is.EqualTo(SaveCodes.Stale));
    }

    [Test]
    public void Stale_BranchedBase_Rejected()
    {
        // Seq is newer, but this device's chain continued from save 3 while the server is at 5 (another device got ahead)
        SubmitDecision d = Submit(T.Meta(seq: 5), T.Req(9, 3, Small()));
        Assert.That(d.Code, Is.EqualTo(SaveCodes.Stale), T.Why(d));
    }

    [Test]
    public void Ended_Rejected()
    {
        SaveMeta meta = T.Meta();
        meta.Ended = true;
        SubmitDecision d = Submit(meta, T.Req(2, 1, Small()));
        Assert.That(d.Code, Is.EqualTo(SaveCodes.Ended), T.Why(d));
        Assert.That(d.Accepted, Is.False);
    }

    [Test]
    public void NoRun_DifferentRun_Rejected()
    {
        SubmitDecision d = Submit(T.Meta(), T.Req("other-run", 2, 1, Small()));
        Assert.That(d.Code, Is.EqualTo(SaveCodes.NoRun), T.Why(d));
    }

    // ================================================================================================================
    // Imports (no meta at all)
    // ================================================================================================================

    [Test]
    public void Import_Clean_Accepted()
    {
        var save = new SaveBuilder { Value = 500_000, Day = 30, Employees = T.Staff(12) };
        SubmitDecision d = Submit(null, T.Req(7, 0, save), T.City(20, 10), Now);

        Assert.That(d.Accepted, Is.True, T.Why(d));
        Assert.That(d.NewFlags, Is.Empty, T.Why(d));
        Assert.That(d.Meta.RunId, Is.EqualTo(T.Run));
        Assert.That(d.Meta.Seq, Is.EqualTo(7));
        Assert.That(d.Meta.Day, Is.EqualTo(30));
        Assert.That(d.Meta.StartedAt, Is.EqualTo(Now), "an imported run starts when the server first sees it");
        Assert.That(d.Meta.Peak, Is.EqualTo(500_000));
        Assert.That(d.Meta.CityDeposited, Is.EqualTo(20));
        Assert.That(d.Meta.CityWithdrawn, Is.EqualTo(10));
        Assert.That(d.Growth, Is.Null, "no baseline to measure growth from");
    }

    [Test]
    public void Import_Absurd_Flagged()
    {
        var save = new SaveBuilder { Value = 1e12, Day = 5 };
        SubmitDecision d = Submit(null, T.Req(3, 0, save));
        Assert.That(d.Accepted, Is.True, T.Why(d));
        Assert.That(T.HasFlag(d.Meta, SaveCodes.FlagImport), Is.True, T.Why(d));
        Assert.That(d.NewFlags.Select(f => f.Reason), Is.EqualTo(new[] { SaveCodes.FlagImport }));
    }

    [Test]
    public void Import_LedgerStillChecked()
    {
        var save = new SaveBuilder { Value = 60_000, Day = 5 };
        SubmitDecision d = Submit(null, T.Req(3, 0, save, T.Gamble(1, "casino", "coinflip", 1000, 5000, 60_000)));
        Assert.That(d.Accepted, Is.True, T.Why(d));
        Assert.That(T.HasFlag(d.Meta, SaveCodes.FlagLedger), Is.True, T.Why(d));
    }

    // ================================================================================================================
    // Time
    // ================================================================================================================

    [Test]
    public void Time_ExactlyAtLimit_Passes()
    {
        // 120 s since the last accept, +60 s for uploads in flight: floor(180 / 15 x 1.1) + 3 = floor(13.2) + 3 = 16 days
        Assert.That(SaveRules.MaxDayAdvance(120), Is.EqualTo(11));
        Assert.That(SaveRules.MaxDayAdvance(120 + SaveRules.TimeRuleGraceSeconds), Is.EqualTo(16));
        SubmitDecision d = Submit(T.Meta(day: 10), T.Req(2, 1, Small(1_000_000, 26)));
        Assert.That(d.Accepted, Is.True, T.Why(d));
    }

    [Test]
    public void Time_OneDayOver_Rejected()
    {
        SaveMeta meta = T.Meta(day: 10);
        SubmitDecision d = Submit(meta, T.Req(2, 1, Small(1_000_000, 27)));
        Assert.That(d.Code, Is.EqualTo(SaveCodes.Time), T.Why(d));
        Assert.That(d.Meta, Is.SameAs(meta));
    }

    [Test]
    public void Time_DayBackwards_Rejected()
    {
        SubmitDecision d = Submit(T.Meta(day: 10), T.Req(2, 1, Small(1_000_000, 9)));
        Assert.That(d.Code, Is.EqualTo(SaveCodes.Time), T.Why(d));
    }

    [Test]
    public void Time_ElapsedNeverNegative()
    {
        // The server clock seems to go back (another instance): elapsed counts as 0, so the upload grace alone: 7 days at most
        SaveMeta meta = T.Meta(day: 10);
        Assert.That(Submit(meta, T.Req(2, 1, Small(1_000_000, 17)), now: T.T0 - 50).Accepted, Is.True);
        Assert.That(Submit(meta, T.Req(2, 1, Small(1_000_000, 18)), now: T.T0 - 50).Code, Is.EqualTo(SaveCodes.Time));
    }

    // ================================================================================================================
    // Invalid saves
    // ================================================================================================================

    [Test]
    public void Invalid_ValueMismatch()
    {
        SubmitRequest request = T.Req(2, 1, Small());
        request.Value += 1.0;
        Assert.That(Submit(T.Meta(), request).Code, Is.EqualTo(SaveCodes.Invalid));

        request.Value -= 0.7;   // within 50 cents: fine (JsonUtility rounding)
        Assert.That(Submit(T.Meta(), request).Code, Is.EqualTo(SaveCodes.Accepted));
    }

    [Test]
    public void Invalid_DayMismatch()
    {
        SubmitRequest request = T.Req(2, 1, Small());
        request.Day += 1;
        SubmitDecision d = Submit(T.Meta(), request);
        Assert.That(d.Code, Is.EqualTo(SaveCodes.Invalid), T.Why(d));
    }

    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    [TestCase(-5.0)]
    public void Invalid_BrokenValue(double value)
    {
        SubmitRequest request = T.Req(2, 1, Small());
        request.Value = value;
        Assert.That(Submit(T.Meta(), request).Code, Is.EqualTo(SaveCodes.Invalid));
    }

    [Test]
    public void Invalid_NegativeSaveValue()
    {
        SubmitDecision d = Submit(T.Meta(), T.Req(2, 1, Small(-5)));
        Assert.That(d.Code, Is.EqualTo(SaveCodes.Invalid), T.Why(d));
    }

    [Test]
    public void Invalid_Oversized()
    {
        SaveBuilder big = Small().With(s => s.Padding = new string('x', SaveCloud.MaxSaveBytes));
        SubmitDecision d = Submit(T.Meta(), T.Req(2, 1, big));
        Assert.That(d.Code, Is.EqualTo(SaveCodes.Invalid), T.Why(d));
        Assert.That(d.Message, Does.Contain("bytes"));
    }

    [TestCase("not json at all")]
    [TestCase("{\"companyValue\": 5")]
    [TestCase("[1, 2, 3]")]
    [TestCase("{\"currentDay\": 12}")]
    [TestCase("{\"companyValue\": \"1000000\", \"currentDay\": 12}")]
    [TestCase("{\"companyValue\": 1000000, \"currentDay\": 12.5}")]
    [TestCase("")]
    public void Invalid_Unparsable(string json)
    {
        SubmitRequest request = T.Req(2, 1, Small());
        request.SaveJson = json;
        SubmitDecision d = Submit(T.Meta(), request);
        Assert.That(d.Code, Is.EqualTo(SaveCodes.Invalid), T.Why(d));
    }

    [Test]
    public void Invalid_BadRunIdOrNoRequest()
    {
        Assert.That(Submit(T.Meta(), T.Req("bad run", 2, 1, Small())).Code, Is.EqualTo(SaveCodes.Invalid));
        Assert.That(Submit(T.Meta(), null).Code, Is.EqualTo(SaveCodes.Invalid));
        Assert.That(Submit(null, T.Req("", 2, 1, Small())).Code, Is.EqualTo(SaveCodes.Invalid));
    }

    // ================================================================================================================
    // Growth
    // ================================================================================================================

    [Test]
    public void Growth_NormalIncome_NotFlagged()
    {
        SubmitDecision d = SubmitSmall(Small(1_150_000));
        Assert.That(d.Accepted, Is.True, T.Why(d));
        Assert.That(d.Meta.Flagged, Is.False, T.Why(d));
        Assert.That(d.Growth.Residual, Is.LessThan(0));
    }

    [Test]
    public void Growth_UnexplainedJump_Flagged()
    {
        SubmitDecision d = SubmitSmall(Small(21_000_000));
        Assert.That(d.Accepted, Is.True, "growth is a soft check: the save is kept");
        Assert.That(T.HasFlag(d.Meta, SaveCodes.FlagGrowth), Is.True, T.Why(d));
        Assert.That(d.NewFlags.Single().Reason, Is.EqualTo(SaveCodes.FlagGrowth));
        Assert.That(d.Growth.Flagged, Is.True);
        Assert.That(d.Growth.Allowance.Total, Is.LessThan(5_000_000), "the income allowance of five employees over two days");
    }

    [Test]
    public void Growth_ExplainedByGambleWin()
    {
        // A slots win of 41x the stake explains the same jump
        SubmitDecision d = SubmitSmall(Small(21_000_000), T.Gamble(1, "casino", "slots", 500_000, 20_500_000, 1_000_000, 11));
        Assert.That(d.Meta.Flagged, Is.False, T.Why(d));
    }

    [Test]
    public void Growth_ExplainedByCard()
    {
        // Without the card, +$8M is too much for two days...
        Assert.That(T.HasFlag(SubmitSmall(Small(9_000_000)).Meta, SaveCodes.FlagGrowth), Is.True);
        // ...a good high-risk card of +$8M on $1M explains it (the bound is $10.0M with every card skill)
        SubmitDecision d = SubmitSmall(Small(9_000_000), T.Card(1, "high", true, 1_000_000, 8_000_000, 11));
        Assert.That(d.Meta.Flagged, Is.False, T.Why(d));
    }

    [Test]
    public void Growth_ExplainedByStockSale()
    {
        // $4M of MOON held at the last save sells for $24M
        SaveBuilder before = Small(1_000_000, 10).With(s => s.Holdings = new() { ("MOON", 800_000, 5.0) });
        SubmitDecision d = Submit(T.Meta(), T.Req(2, 1, Small(21_000_000), T.Trade(1, "MOON", 4_000_000, 24_000_000, 11)), T.City(), Now, Previous(before));
        Assert.That(d.Meta.Flagged, Is.False, T.Why(d));
    }

    [Test]
    public void Growth_ExplainedByCityWithdrawal()
    {
        SubmitDecision d = Submit(T.Meta(cityWd: 5_000), T.Req(2, 1, Small(21_000_000)), T.City(0, 20_005_000), Now, Previous(Small(1_000_000, 10)));
        Assert.That(d.Meta.Flagged, Is.False, T.Why(d));
        Assert.That(d.Growth.CityNet, Is.EqualTo(20_000_000));
        Assert.That(d.Meta.CityWithdrawn, Is.EqualTo(20_005_000), "the new baseline");
    }

    [Test]
    public void Growth_DepositLowersValue_Fine()
    {
        SubmitDecision d = Submit(T.Meta(), T.Req(2, 1, Small(500_000)), T.City(500_000, 0), Now, Previous(Small(1_000_000, 10)));
        Assert.That(d.Meta.Flagged, Is.False, T.Why(d));
        Assert.That(d.Growth.CityNet, Is.EqualTo(-500_000));
    }

    [Test]
    public void Growth_UnknownCity_NoFlag_BaselineKept()
    {
        // The city accounts couldn't be read: a withdrawal may explain the jump, so no growth flag (and the old baseline stays,
        // so the next save with known totals counts every transfer since)
        SaveMeta meta = T.Meta(cityDep: 7, cityWd: 9);
        SubmitDecision d = Submit(meta, T.Req(2, 1, Small(21_000_000)), T.NoCity, Now, Previous(Small(1_000_000, 10)));
        Assert.That(d.Accepted, Is.True);
        Assert.That(d.Meta.Flagged, Is.False, T.Why(d));
        Assert.That(d.Growth.CityUnknown, Is.True);
        Assert.That(d.Growth.Residual, Is.GreaterThan(0), "unexplained, but not flagged while city money is unknown");
        Assert.That(d.Growth.CityNet, Is.EqualTo(0));
        Assert.That(d.Meta.CityDeposited, Is.EqualTo(7));
        Assert.That(d.Meta.CityWithdrawn, Is.EqualTo(9));

        // The ledger's own rules still apply
        SubmitDecision cheat = Submit(meta, T.Req(2, 1, Small(21_000_000), T.Flip(1, 1000, true, 1_000_000, 11).With(e => e.Payout = 9000)),
                                      T.NoCity, Now, Previous(Small(1_000_000, 10)));
        Assert.That(T.HasFlag(cheat.Meta, SaveCodes.FlagLedger), Is.True, T.Why(cheat));
    }

    [Test]
    public void Growth_BuildGambleWin_PaysTheBudgetNotTheCompany()
    {
        // $9M bet into the build budget and won: the budget got $13.5M, company value dropped by $9M
        SaveMeta meta = T.Meta(value: 10_000_000, day: 12);
        var previous = Small(10_000_000, 12);
        LedgerEntry win = T.Gamble(1, "build-gamble", null, 9_000_000, 13_500_000, 10_000_000, 12);

        SubmitDecision honest = Submit(meta, T.Req(2, 1, Small(1_000_000, 12), win), T.City(), Now, Previous(previous));
        Assert.That(honest.Meta.Flagged, Is.False, T.Why(honest));

        // A save that shows the winnings in company value isn't explained by that win
        SubmitDecision cash = Submit(meta, T.Req(2, 1, Small(14_500_000, 12), win), T.City(), Now, Previous(previous));
        Assert.That(T.HasFlag(cash.Meta, SaveCodes.FlagGrowth), Is.True, T.Why(cash));
    }

    [Test]
    public void Growth_StraddlingRound_NotFlagged()
    {
        // The last save was written mid-hand: $900k was on the table (value $1M). The hand then won 2x ($1.8M back).
        // RunStats tracks the peak every frame, so that save's own peak shows the $1.9M the company had before the stake.
        SaveMeta meta = T.Meta(value: 1_000_000, day: 12);
        LedgerEntry hand = T.Gamble(1, "casino", "blackjack", 900_000, 1_800_000, 1_900_000, 12);
        SaveBuilder midHand = Small(1_000_000, 12).With(s => s.RunPeak = 1_900_000);
        SubmitDecision d = Submit(meta, T.Req(2, 1, Small(2_800_000, 12), hand), T.City(), Now, Previous(midHand));
        Assert.That(d.Meta.Flagged, Is.False, T.Why(d));
        Assert.That(d.Growth.Straddle, Is.EqualTo(900_000));

        // A stake that big needs the company to have had it: a previous save that never saw $1.9M caps the straddle
        SubmitDecision noPeak = Submit(meta, T.Req(2, 1, Small(2_800_000, 12), hand), T.City(), Now, Previous(Small(1_000_000, 12)));
        Assert.That(noPeak.Growth.Straddle, Is.LessThan(100_000), T.Why(noPeak));
        Assert.That(noPeak.Meta.Flagged, Is.True, T.Why(noPeak));

        // A card is never open across a save
        SubmitDecision card = Submit(meta, T.Req(2, 1, Small(1_100_000, 12), T.Card(1, "low", true, 1_000_000, 50_000, 12)),
                                     T.City(), Now, Previous(midHand));
        Assert.That(card.Growth.Straddle, Is.EqualTo(0));
    }

    [Test]
    public void Growth_BankruptcyRestart_NotFlagged()
    {
        // Peak $500M, down to $5M at the last save, then bankrupt: the hearing restarts the company at 10% of the peak
        SaveMeta meta = T.Meta(value: 5_000_000);
        meta.Peak = 500_000_000;
        SubmitDecision d = Submit(meta, T.Req(2, 1, Small(50_000_000)), T.City(), Now, Previous(Small(5_000_000, 10)));
        Assert.That(d.Meta.Flagged, Is.False, T.Why(d));
        Assert.That(d.Growth.Residual, Is.GreaterThan(0), "the restart isn't growth from $5M...");
        Assert.That(d.Growth.FreshStartBound, Is.GreaterThanOrEqualTo(50_000_000), "...but a restart explains it");

        SubmitDecision tooRich = Submit(meta, T.Req(2, 1, Small(150_000_000)), T.City(), Now, Previous(Small(5_000_000, 10)));
        Assert.That(T.HasFlag(tooRich.Meta, SaveCodes.FlagGrowth), Is.True, T.Why(tooRich));
    }

    /// <summary>A context with the city's stamps and applied ops, as GameSaveService builds it from one read.</summary>
    static SaveSubmitContext City(SaveBuilder previous, IEnumerable<SaveCityTransfer> stamps, params string[] ops) =>
        new SaveSubmitContext { PreviousSaveJson = previous.Json(), CityTransfers = stamps.ToList(), CityOps = ops.ToList(), PlayerId = "player-1" };

    [Test]
    public void City_WithdrawalBookedMomentsAgo_TheSaveDoesntList_IsBusy()
    {
        // The city booked a $30M withdrawal 5 s before this submit and the save doesn't list it: it may have been written
        // just before (an upload on its way while the player wired money). Busy: the client sends its newer save.
        SaveMeta meta = T.Meta();
        var stamp = new SaveCityTransfer { OpId = "op-w", Time = Now - 5, Amount = 30_000_000, Deposit = false };
        SubmitDecision early = Submit(meta, T.Req(2, 1, Small(1_000_000)), T.City(0, 30_000_000), Now, City(Small(1_000_000, 10), new[] { stamp }, "op-w"));
        Assert.That(early.Code, Is.EqualTo(SaveCodes.Busy), T.Why(early));
        Assert.That(early.Meta, Is.SameAs(meta), "nothing changes");

        // The newer save lists it (pending, booked: the company's money in transit) or shows it paid: both fine
        SaveBuilder listed = Small(1_000_000).With(s => s.Pending = new() { ("op-w", "withdraw", 30_000_000) });
        SubmitDecision pending = Submit(meta, T.Req(2, 1, listed), T.City(0, 30_000_000), Now, City(Small(1_000_000, 10), new[] { stamp }, "op-w"));
        Assert.That(pending.Accepted && !pending.Meta.Flagged, Is.True, T.Why(pending));
        Assert.That(pending.Meta.PendingWithdrawalsBooked, Is.EqualTo(30_000_000));
        SubmitDecision paid = Submit(meta, T.Req(2, 1, Small(31_000_000)), T.City(0, 30_000_000), Now + 30, City(Small(1_000_000, 10), new[] { stamp }, "op-w"));
        Assert.That(paid.Accepted && !paid.Meta.Flagged, Is.True, "older than the settle window: judged, and it adds up: " + T.Why(paid));
    }

    [Test]
    public void City_DepositPendingAtTheLastSave_BookedHoursLater_NotFlagged()
    {
        // $20M left the company just before the last save, which listed it pending; the city hadn't booked it at that accept
        // (meta: unbooked pending deposits $20M). It booked it three hours later: the totals take it, the value doesn't move.
        long booked = T.T0 + 3 * 3600, now = booked + 3600;
        SaveMeta meta = T.Meta(day: 12, value: 50_000_000);
        meta.PendingDepositsUnbooked = 20_000_000;
        var stamp = new SaveCityTransfer { OpId = "op-1", Time = booked, Amount = 20_000_000, Deposit = true };
        SubmitDecision d = Submit(meta, T.Req(2, 1, Small(50_000_000, 12)), T.City(20_000_000, 0), now, City(Small(50_000_000, 12), new[] { stamp }, "op-1"));
        Assert.That(d.Meta.Flagged, Is.False, T.Why(d));
        Assert.That(d.Growth.CityNet, Is.EqualTo(-20_000_000));
        Assert.That(d.Growth.PendingBefore, Is.EqualTo(20_000_000));
        Assert.That(d.Meta.PendingDepositsUnbooked, Is.EqualTo(0));

        // Without it in transit at the last accept, the city took $20M the company never paid
        meta.PendingDepositsUnbooked = 0;
        SubmitDecision unpaid = Submit(meta, T.Req(2, 1, Small(50_000_000, 12)), T.City(20_000_000, 0), now, City(Small(50_000_000, 12), new[] { stamp }, "op-1"));
        Assert.That(T.HasFlag(unpaid.Meta, SaveCodes.FlagGrowth), Is.True, T.Why(unpaid));
    }

    [Test]
    public void City_WithdrawalBookedBeforeTheLastAccept_ArrivingNow_NotFlagged()
    {
        // The last save listed a $30M withdrawal the city had already booked (meta: booked pending withdrawals $30M, and the
        // totals have it); the company gets the money now
        SaveMeta meta = T.Meta(day: 12, value: 10_000_000, cityWd: 30_000_000);
        meta.PendingWithdrawalsBooked = 30_000_000;
        var stamp = new SaveCityTransfer { OpId = "op-2", Time = T.T0 - 3000, Amount = 30_000_000, Deposit = false };
        SubmitDecision d = Submit(meta, T.Req(2, 1, Small(40_000_000, 12)), T.City(0, 30_000_000), T.T0 + 7200, City(Small(10_000_000, 12), new[] { stamp }, "op-2"));
        Assert.That(d.Meta.Flagged, Is.False, T.Why(d));

        // Still waiting for the answer (listed again, still booked): the money is still in transit, nothing moved
        SaveBuilder waiting = Small(10_000_000, 12).With(s => s.Pending = new() { ("op-2", "withdraw", 30_000_000) });
        SubmitDecision still = Submit(meta, T.Req(2, 1, waiting), T.City(0, 30_000_000), T.T0 + 7200, City(Small(10_000_000, 12), new[] { stamp }, "op-2"));
        Assert.That(still.Meta.Flagged, Is.False, T.Why(still));
        Assert.That(still.Growth.PendingAfter, Is.EqualTo(30_000_000));
    }

    [Test]
    public void City_PendingTransfersCountOnlyWhatTheCityRecorded()
    {
        SaveMeta meta = T.Meta(day: 12, value: 10_000_000);
        var small = new SaveCityTransfer { OpId = "op-3", Time = T.T0 - 3000, Amount = 5_000_000, Deposit = false };

        // A pending withdrawal the city never booked is nobody's money yet; one it booked for less counts at the stamp's amount
        SaveBuilder listing = Small(10_000_000, 12).With(s => s.Pending = new() { ("fake", "withdraw", 1_000_000_000), ("op-3", "withdraw", 900_000_000) });
        SubmitDecision d = Submit(meta, T.Req(2, 1, listing), T.City(), T.T0 + 7200, City(Small(10_000_000, 12), new[] { small }, "op-3"));
        Assert.That(d.Growth.PendingAfter, Is.EqualTo(5_000_000), "only what the city stamped");

        // Money in transit the city recorded long ago but the last save never listed appears out of nowhere: flagged
        var big = new SaveCityTransfer { OpId = "op-4", Time = T.T0 - 3000, Amount = 400_000_000, Deposit = false };
        SaveBuilder relisted = Small(10_000_000, 12).With(s => s.Pending = new() { ("op-4", "withdraw", 400_000_000) });
        SubmitDecision again = Submit(meta, T.Req(2, 1, relisted), T.City(), T.T0 + 7200, City(Small(10_000_000, 12), new[] { big }, "op-4"));
        Assert.That(T.HasFlag(again.Meta, SaveCodes.FlagGrowth), Is.True, T.Why(again));

        // A pending deposit the company never paid (its value is untouched) raises the effective value: flagged right there
        SaveBuilder unpaid = Small(10_000_000, 12).With(s => s.Pending = new() { ("dep-x", "deposit", 900_000_000) });
        SubmitDecision fake = Submit(meta, T.Req(2, 1, unpaid), T.City(), T.T0 + 7200, City(Small(10_000_000, 12), new SaveCityTransfer[0]));
        Assert.That(T.HasFlag(fake.Meta, SaveCodes.FlagGrowth), Is.True, T.Why(fake));
    }

    [Test]
    public void ClassifyPending_TheRule()
    {
        var stamps = new List<SaveCityTransfer>
        {
            new() { OpId = "d-stamped", Deposit = true, Amount = 100, Time = T.T0 },
            new() { OpId = "w-stamped", Deposit = false, Amount = 70, Time = T.T0 },
            new() { OpId = "w-as-deposit", Deposit = true, Amount = 50, Time = T.T0 }
        };
        string[] ops = { "d-stamped", "w-stamped", "d-tidied", "w-tidied", "w-as-deposit" };
        SavePendingTransfer P(string op, bool deposit, double amount, string player = "me") =>
            new() { OpId = op, Deposit = deposit, Amount = amount, Player = player, City = "ist" };
        var pending = new List<SavePendingTransfer>
        {
            P("d-stamped", true, 100),            // booked (stamp): the city totals have it
            P("d-tidied", true, 200),             // booked (op only, the stamp tidied away)
            P("d-waiting", true, 300),            // unbooked: the company's money in transit
            P("d-waiting", true, 300),            // the same op again counts once
            P("w-stamped", false, 90),            // booked for 70 (the stamp's amount, not the save's 90)
            P("w-tidied", false, 40),             // booked (op only): the save's amount
            P("w-waiting", false, 500),           // unbooked: still in the bank, nobody's money in transit
            P("w-as-deposit", false, 60),         // the city stamped that op as a deposit: not a booked withdrawal
            P("d-legacy", true, 11, null),        // from before transfers named their account: the current account's
            P("d-other", true, 1000, "someone")   // another account's: not classified here at all
        };
        SavePendingCheck c = SaveRules.ClassifyPending(pending, "me", stamps, ops);
        Assert.That(c.UnbookedDeposits, Is.EqualTo(300 + 11));
        Assert.That(c.BookedWithdrawals, Is.EqualTo(70 + 40));
        Assert.That(c.Booked, Is.EquivalentTo(new[] { "d-stamped", "d-tidied", "w-stamped", "w-tidied" }));

        // Unknown player: every transfer counts
        Assert.That(SaveRules.ClassifyPending(pending, null, stamps, ops).UnbookedDeposits, Is.EqualTo(300 + 11 + 1000));
    }

    [Test]
    public void Growth_StaffWhoLeft_CountFromThePreviousSave()
    {
        // 200 strong employees earned for four days, then 190 were let go just before the save
        List<SaveEmployeeInfo> staff = T.Staff(200, iq: 140, gpa: 3.6, exp: 20, phd: true);
        var before = new SaveBuilder { Value = 10_000_000, Day = 10, Employees = staff, MeetingsHeld = 0 };
        var after = new SaveBuilder { Value = 50_000_000, Day = 14, Employees = staff.Take(10).ToList(), MeetingsHeld = 0 };
        SaveMeta meta = T.Meta(value: 10_000_000, day: 10);

        SubmitDecision withPrevious = Submit(meta, T.Req(2, 1, after), T.City(), Now, Previous(before));
        Assert.That(withPrevious.Meta.Flagged, Is.False, T.Why(withPrevious));
        Assert.That(withPrevious.Growth.Allowance.Headcount, Is.EqualTo(200));

        SubmitDecision without = Submit(meta, T.Req(2, 1, after), T.City(), Now, null);
        Assert.That(T.HasFlag(without.Meta, SaveCodes.FlagGrowth), Is.True, "only ten employees in sight");
    }

    [Test]
    public void Growth_BoardMeetings_CountedFromTheSaves()
    {
        var before = Small(1_000_000, 10).With(s => s.MeetingsHeld = 3);
        SubmitDecision noMeeting = SaveRules.EvaluateSubmit(T.Meta(), T.Req(2, 1, Small(1_100_000).With(s => s.MeetingsHeld = 3)), T.City(), Now, Previous(before));
        Assert.That(noMeeting.Growth.Allowance.Meetings, Is.EqualTo(0));
        SubmitDecision oneMeeting = SaveRules.EvaluateSubmit(T.Meta(), T.Req(2, 1, Small(1_100_000).With(s => s.MeetingsHeld = 4)), T.City(), Now, Previous(before));
        Assert.That(oneMeeting.Growth.Allowance.Meetings, Is.EqualTo(1));
        Assert.That(oneMeeting.Growth.Allowance.Board, Is.GreaterThan(0));
        SubmitDecision inflated = SaveRules.EvaluateSubmit(T.Meta(), T.Req(2, 1, Small(1_100_000).With(s => s.MeetingsHeld = 90)), T.City(), Now, Previous(before));
        Assert.That(inflated.Growth.Allowance.Meetings, Is.EqualTo(1), "never more than one per 10 days");
    }

    // ================================================================================================================
    // Stocks: company value is cash, shares are in the "stocks" extra
    // ================================================================================================================

    static List<(string, long, double)> Shares(long count, double averagePrice) => new() { ("MOON", count, averagePrice) };

    [Test]
    public void Stocks_SaleOfSharesBoughtBeforeThePreviousSave_NotFlagged()
    {
        // $10M of MOON was bought before the last accepted save (cash then $1M); now all of it sells for $12M
        SaveBuilder before = Small(1_000_000, 12).With(s => s.Holdings = Shares(2_000_000, 5.0));
        SaveBuilder after = Small(13_000_000, 12).With(s => s.Holdings = new());
        SubmitDecision d = Submit(T.Meta(day: 12), T.Req(2, 1, after, T.Trade(1, "MOON", 10_000_000, 12_000_000, 12)), T.City(), Now, Previous(before));
        Assert.That(d.Meta.Flagged, Is.False, T.Why(d));
        Assert.That(d.Growth.HoldingsBefore, Is.EqualTo(10_000_000));
        Assert.That(d.Growth.HoldingsAfter, Is.EqualTo(0));
        Assert.That(d.Growth.StockCash, Is.EqualTo(12_000_000), "the gain and the cost coming back as cash");
    }

    [Test]
    public void Stocks_BuyAndSellInOneStretch_NetsCorrectly()
    {
        SaveMeta meta = T.Meta(day: 12, value: 20_000_000);
        SaveBuilder before = Small(20_000_000, 12).With(s => s.Holdings = new());

        // Bought $10M (+0.5% fee) and sold them for $12M: +$1.95M cash
        SubmitDecision sold = Submit(meta, T.Req(2, 1, Small(21_950_000, 12).With(s => s.Holdings = new()), T.Trade(1, "MOON", 10_000_000, 12_000_000, 12)),
                                     T.City(), Now, Previous(before));
        Assert.That(sold.Meta.Flagged, Is.False, T.Why(sold));
        Assert.That(sold.Growth.StockCash, Is.EqualTo(2_000_000));

        // Bought $10M and still holding them: the cash became shares
        SubmitDecision holding = Submit(meta, T.Req(2, 1, Small(9_950_000, 12).With(s => s.Holdings = Shares(1_000_000, 10.0))), T.City(), Now, Previous(before));
        Assert.That(holding.Meta.Flagged, Is.False, T.Why(holding));
        Assert.That(holding.Growth.StockCash, Is.EqualTo(-10_000_000));

        // The same shares with the cash still there too: nothing explains it
        SubmitDecision both = Submit(meta, T.Req(2, 1, Small(20_000_000, 12).With(s => s.Holdings = Shares(1_000_000, 10.0))), T.City(), Now, Previous(before));
        Assert.That(T.HasFlag(both.Meta, SaveCodes.FlagGrowth), Is.True, T.Why(both));
    }

    [Test]
    public void Stocks_HoldingsCanOnlyDropByWhatWasHeld()
    {
        // $1M of shares before; the save shows them gone (even "negative") and $6M more cash than their sale brought
        SaveBuilder before = Small(1_000_000, 12).With(s => s.Holdings = Shares(200_000, 5.0));
        SaveBuilder after = Small(7_200_000, 12).With(s => s.Holdings = Shares(-5_000_000, 5.0));
        SubmitDecision d = Submit(T.Meta(day: 12), T.Req(2, 1, after, T.Trade(1, "MOON", 1_000_000, 1_200_000, 12)), T.City(), Now, Previous(before));
        Assert.That(d.Growth.HoldingsAfter, Is.EqualTo(0), "shares can't be negative");
        Assert.That(d.Growth.StockCash, Is.EqualTo(1_200_000), "the sale explains its proceeds, nothing more");
        Assert.That(T.HasFlag(d.Meta, SaveCodes.FlagGrowth), Is.True, T.Why(d));
    }

    [Test]
    public void Stocks_WithoutHoldingsOnBothSides_FullProceedsCount()
    {
        SaveBuilder after = Small(13_000_000, 12).With(s => s.Holdings = new());
        LedgerEntry sale = T.Trade(1, "MOON", 10_000_000, 12_000_000, 12);

        SubmitDecision noPrevious = Submit(T.Meta(day: 12), T.Req(2, 1, after, sale), T.City(), Now, null);
        Assert.That(noPrevious.Meta.Flagged, Is.False, T.Why(noPrevious));
        Assert.That(noPrevious.Growth.StockCash, Is.EqualTo(12_000_000));

        SubmitDecision oldSave = Submit(T.Meta(day: 12), T.Req(2, 1, after, sale), T.City(), Now, Previous(Small(1_000_000, 12).With(s => s.Holdings = null)));
        Assert.That(oldSave.Growth.HoldingsBefore, Is.EqualTo(-1));
        Assert.That(oldSave.Growth.StockCash, Is.EqualTo(12_000_000));
    }

    [Test]
    public void Stocks_ANewRunStartsWithNoShares()
    {
        // The first save of a run: $45k of shares bought since NewRun came out of the $50k
        SaveMeta meta = SaveRules.NewRunMeta(T.Run, T.T0, T.City(), T.Meta(run: "old-run", acceptedAt: T.T0 - 30));
        var save = new SaveBuilder { Value = 5_000, Day = 3, Holdings = Shares(9_000, 5.0) };
        SubmitDecision d = Submit(meta, T.Req(1, 0, save), T.City(), T.T0 + 60);
        Assert.That(d.Meta.Flagged, Is.False, T.Why(d));
        Assert.That(d.Growth.HoldingsBefore, Is.EqualTo(0));
        Assert.That(d.Growth.StockCash, Is.EqualTo(-45_000));
    }

    [Test]
    public void FoldedEntries_SumsAreChecked()
    {
        // The client folds old rounds per (source, game) into summaries whose stake, payout and value-before are sums,
        // and may merge sales of one symbol into one trade entry (Count > 1). $30M of MOON was held at the last save.
        LedgerEntry rounds = T.Summary(1, "casino", "coinflip", 50, 25, 50_000_000, 48_750_000);
        rounds.ValueBefore = 55_000_000;
        LedgerEntry sales = T.Trade(2, "MOON", 30_000_000, 36_000_000, 12);
        sales.Count = 3;
        SaveBuilder before = Small(1_000_000, 10).With(s => s.Holdings = new() { ("MOON", 6_000_000, 5.0) });
        SubmitDecision d = Submit(T.Meta(), T.Req(2, 1, Small(5_000_000), rounds, sales), T.City(), Now, Previous(before));
        Assert.That(T.HasFlag(d.Meta, SaveCodes.FlagLedger), Is.False, "a summary's stake is a sum, not one bet: " + T.Why(d));
        Assert.That(d.Meta.Luck.Gambles.Count, Is.EqualTo(50));

        LedgerEntry impossible = T.Summary(1, "casino", "coinflip", 50, 25, 50_000_000, 120_000_000);   // more than 2x the sum
        Assert.That(T.HasFlag(SubmitSmall(Small(5_000_000), impossible).Meta, SaveCodes.FlagLedger), Is.True);
    }

    // ================================================================================================================
    // Ledger
    // ================================================================================================================

    [Test]
    public void Ledger_PayoutAboveMultiple_Flagged()
    {
        SubmitDecision d = SubmitSmall(Small(1_002_000), T.Gamble(1, "casino", "coinflip", 1000, 3000, 1_000_000, 11));
        Assert.That(T.HasFlag(d.Meta, SaveCodes.FlagLedger), Is.True, T.Why(d));
        Assert.That(T.HasFlag(d.Meta, SaveCodes.FlagGrowth), Is.False);
    }

    [Test]
    public void Ledger_HonestCasinoPayouts_NotFlagged()
    {
        SubmitDecision d = SubmitSmall(Small(1_000_000 + 950 + 1500 + 49_900 + 1_000 + 950),
            T.Flip(1, 1000, true, 1_000_000, 11),
            T.Gamble(2, "casino", "blackjack", 1000, 2500, 1_000_950, 11),                 // a natural pays 2.5x
            T.Gamble(3, "casino", "slots", 100, 50_000, 1_002_450, 11),                     // three diamonds pay 500x
            T.Gamble(4, "tilt", null, 1000, 2000, 1_052_350, 11),
            T.Gamble(5, "casino", "Coin Flip", 950, 1900, 1_053_350, 11));                   // double or nothing, detail spelled differently
        Assert.That(d.Meta.Flagged, Is.False, T.Why(d));
        Assert.That(d.Meta.Luck.Gambles.Count, Is.EqualTo(5));
    }

    [Test]
    public void Ledger_StakeAboveValue_Flagged()
    {
        SubmitDecision d = SubmitSmall(Small(1_000_000), T.Flip(1, 2_000_000, false, 1_000_000, 11));
        Assert.That(T.HasFlag(d.Meta, SaveCodes.FlagLedger), Is.True, T.Why(d));
    }

    [Test]
    public void Ledger_FurnitureStakeIsBuildBudget()
    {
        // Furniture gambles stake build budget (can be more than company value) and pay levels (3x at most)
        SubmitDecision ok = SubmitSmall(Small(1_000_000), T.Gamble(1, "furniture", null, 5_000_000, 10_000_000, 1_000_000, 11));
        Assert.That(ok.Meta.Flagged, Is.False, T.Why(ok));
        Assert.That(ok.Growth.LedgerNet, Is.EqualTo(0), "company value never moves");
        SubmitDecision tooMuch = SubmitSmall(Small(1_000_000), T.Gamble(1, "furniture", null, 5_000_000, 20_000_000, 1_000_000, 11));
        Assert.That(T.HasFlag(tooMuch.Meta, SaveCodes.FlagLedger), Is.True, T.Why(tooMuch));
    }

    [Test]
    public void Ledger_CardBounds()
    {
        SubmitDecision tooBig = SubmitSmall(Small(1_000_000), T.Card(1, "high", true, 1_000_000, 20_000_000, 11));
        Assert.That(T.HasFlag(tooBig.Meta, SaveCodes.FlagLedger), Is.True, "above $10.0M: " + T.Why(tooBig));

        SubmitDecision badGain = SubmitSmall(Small(1_005_000), T.Card(1, "balanced", false, 1_000_000, 5_000, 11));
        Assert.That(T.HasFlag(badGain.Meta, SaveCodes.FlagLedger), Is.True, T.Why(badGain));

        SubmitDecision goodLoss = SubmitSmall(Small(990_000), T.Card(1, "low", true, 1_000_000, -10_000, 11));
        Assert.That(T.HasFlag(goodLoss.Meta, SaveCodes.FlagLedger), Is.True, T.Why(goodLoss));

        SubmitDecision fine = SubmitSmall(Small(700_000),
            T.Card(1, "high", false, 1_000_000, -400_000, 11),
            T.Card(2, "Balanced", true, 600_000, 0, 11),          // Double or Nothing: nothing
            T.Card(3, "LowRisk", true, 600_000, 100_000, 11));
        Assert.That(fine.Meta.Flagged, Is.False, T.Why(fine));
        Assert.That(fine.Meta.Luck.Cards.Count, Is.EqualTo(3));
    }

    [Test]
    public void Ledger_TradeAboveBound_Flagged()
    {
        SubmitDecision d = SubmitSmall(Small(1_000_000), T.Trade(1, "DOGE", 1_000, 1e9, 11));
        Assert.That(T.HasFlag(d.Meta, SaveCodes.FlagLedger), Is.True, T.Why(d));
    }

    [Test]
    public void Ledger_MoreCardsThanTheClockAllows_Flagged()
    {
        // Two days: at most floor(3 x 45 / 42) + 2 = 5 risk cards
        LedgerEntry[] cards = Enumerable.Range(1, 6).Select(i => T.Card(i, "low", false, 1_000_000, 0, 11)).ToArray();
        Assert.That(T.HasFlag(SubmitSmall(Small(1_000_000), cards.Take(5).ToArray()).Meta, SaveCodes.FlagLedger), Is.False);
        SubmitDecision d = SubmitSmall(Small(1_000_000), cards);
        Assert.That(T.HasFlag(d.Meta, SaveCodes.FlagLedger), Is.True, T.Why(d));
    }

    [Test]
    public void Ledger_BrokenEntries_Flagged()
    {
        Assert.That(T.HasFlag(SubmitSmall(Small(), T.Gamble(1, "casino", "slots", double.NaN, 0, 1_000_000)).Meta, SaveCodes.FlagLedger), Is.True);
        Assert.That(T.HasFlag(SubmitSmall(Small(), T.Gamble(1, "casino", "slots", -5, 0, 1_000_000)).Meta, SaveCodes.FlagLedger), Is.True);
        Assert.That(T.HasFlag(SubmitSmall(Small(), T.Summary(1, "casino", "coinflip", 3, 5, 3000, 5850)).Meta, SaveCodes.FlagLedger), Is.True, "5 wins of 3");
        Assert.That(T.HasFlag(SubmitSmall(Small(), T.Card(1, "high", true, double.PositiveInfinity, 5)).Meta, SaveCodes.FlagLedger), Is.True);
    }

    [Test]
    public void Ledger_UnknownKindsAndSources_Tolerated()
    {
        var unknownKind = new LedgerEntry { Index = 1, Kind = "lottery", Stake = 5, Payout = 1e12 };
        SubmitDecision d = SubmitSmall(Small(1_000_000), unknownKind, T.Gamble(2, "space-race", null, 1000, 400_000, 1_000_000, 11));
        Assert.That(T.HasFlag(d.Meta, SaveCodes.FlagLedger), Is.False, T.Why(d));
        Assert.That(d.Growth.LedgerNet, Is.EqualTo(399_000), "an unknown source counts as money, an unknown kind explains nothing");
        Assert.That(d.Meta.Luck.Gambles.Count, Is.EqualTo(0), "no known odds, no luck tally");
        Assert.That(d.Meta.LedgerIndex, Is.EqualTo(2));
    }

    // ================================================================================================================
    // Luck
    // ================================================================================================================

    [Test]
    public void Luck_FiftyFakeWins_Flagged()
    {
        // 49 coin-flip wins in a row: suspicious, but the flag needs 50 results
        double value = 1_000_000;
        var entries = new List<LedgerEntry>();
        for (int i = 1; i <= 49; i++)
        {
            entries.Add(T.Flip(i, 1000, true, value, 11));
            value += 950;
        }
        SubmitDecision first = SubmitSmall(Small(value), entries.ToArray());
        Assert.That(first.Meta.Flagged, Is.False, T.Why(first));
        Assert.That(first.Meta.Luck.Gambles.Count, Is.EqualTo(49));
        Assert.That(first.Meta.Luck.Gambles.Z, Is.GreaterThan(5));

        // The 50th win: z = (50 - 25.25) / sqrt(12.5) = 7
        SubmitDecision second = Submit(first.Meta, T.Req(3, 2, Small(value + 950, 12), T.Flip(50, 1000, true, value, 12)), T.City(), Now + 30,
                                       Previous(Small(value, 12)));
        Assert.That(T.HasFlag(second.Meta, SaveCodes.FlagLuck), Is.True, T.Why(second));
        Assert.That(second.Meta.Luck.Gambles.Count, Is.EqualTo(50));
    }

    [Test]
    public void Luck_FakeGoodCards_Flagged_UnlessLuckSkills()
    {
        // 60 good high-risk cards in 54 days: without luck skills a high-risk card is good at most 33% of the time
        var entries = Enumerable.Range(1, 60).Select(i => T.Card(i, "high", true, 1_000_000 + (i - 1) * 1000.0, 1000, 11 + i / 2)).ToArray();
        SaveMeta meta = T.Meta(day: 10);
        var save = new SaveBuilder { Value = 1_060_000, Day = 64, Employees = T.Staff(5), MeetingsHeld = 0 };
        SubmitDecision d = Submit(meta, T.Req(2, 1, save, entries), T.City(), T.T0 + 800, Previous(Small(1_000_000, 10)));
        Assert.That(d.Accepted, Is.True, T.Why(d));
        Assert.That(T.HasFlag(d.Meta, SaveCodes.FlagLuck), Is.True, T.Why(d));
        Assert.That(T.HasFlag(d.Meta, SaveCodes.FlagLedger), Is.False, "54 days allow 60 cards: " + T.Why(d));

        // With every luck skill cards are nearly always good: the same run is honest
        var lucky = save.With(s => s.Skills = new List<string> { "chaos_1_luck", "chaos_1_charm", "chaos_4_fallacy", "chaos_5_wildcard",
                                                                  "chaos_6_reverse", "chaos_6_probability", "chaos_6_reality", "chaos_6_quantum", "secret_ultimate" });
        SubmitDecision withSkills = Submit(meta, T.Req(2, 1, lucky, entries), T.City(), T.T0 + 800, Previous(Small(1_000_000, 10)));
        Assert.That(T.HasFlag(withSkills.Meta, SaveCodes.FlagLuck), Is.False, T.Why(withSkills));
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(5)]
    [TestCase(6)]
    public void Luck_HonestRandomPlay_NeverFlagged(int seed)
    {
        // Thousands of casino rounds and risk cards with the game's real odds, submitted 60 at a time
        var rng = new Random(seed);
        SaveMeta meta = T.Meta(day: 10, value: 5_000_000);
        double value = meta.Value;
        int day = meta.Day;
        long index = 0, now = T.T0, seq = meta.Seq;
        SaveBuilder previous = Small(value, day);
        for (int batch = 0; batch < 60; batch++)
        {
            var entries = new List<LedgerEntry>();
            for (int i = 0; i < 60; i++)
            {
                double stake = Math.Floor(Math.Min(value - 1000, 1000 + rng.NextDouble() * value * 0.02));
                double payout = HonestRound(rng, out string game) * stake;
                entries.Add(T.Gamble(++index, "casino", game, stake, payout, value, day));
                value += payout - stake;
            }
            for (int i = 0; i < 3; i++)
            {
                // No luck skills: high risk is good 25% of the time, balanced 50%, low 75% (trust and heat keep it there or lower)
                string risk = new[] { "high", "balanced", "low" }[rng.Next(3)];
                double p = risk == "high" ? 0.25 : risk == "balanced" ? 0.5 : 0.75;
                bool good = rng.NextDouble() < p;
                double change = good ? value * 0.02 : -value * 0.02;
                entries.Add(T.Card(++index, risk, good, value, change, day));
                value += change;
            }
            day += 3;
            now += 120;
            var save = new SaveBuilder { Value = value, Day = day, Employees = T.Staff(5), MeetingsHeld = 0 };
            SubmitDecision d = Submit(meta, T.Req(seq + 1, seq, save, entries.ToArray()), T.City(), now, Previous(previous));
            Assert.That(d.Accepted, Is.True, T.Why(d));
            Assert.That(d.Meta.Flagged, Is.False, "batch " + batch + ": " + T.Why(d));
            meta = d.Meta;
            seq = meta.Seq;
            previous = save;
        }
        Assert.That(meta.Luck.Gambles.Count, Is.EqualTo(3600));
        Assert.That(meta.Luck.Cards.Count, Is.EqualTo(180));
        Assert.That(meta.Luck.Gambles.Z, Is.LessThan(3), "honest odds sit below the tallied upper bounds");
    }

    /// <summary>One casino round with the real odds: coin flip (1.95x at 50%), blackjack (win 42%, natural 4.5%, push 8.5%), slots (the reel weights).</summary>
    static double HonestRound(Random rng, out string game)
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
                    return weights.Length - 1;
                }
                int a = Reel(), b = Reel(), c = Reel();
                if (a == b && b == c) return three[a];
                return a == b || b == c || a == c ? 1.5 : 0.0;
            }
        }
    }

    [Test]
    public void Luck_SummaryEntriesCountEveryRound()
    {
        SubmitDecision d = SubmitSmall(Small(1_000_000 + 10 * 950 - 10 * 1000), T.Summary(1, "casino", "coinflip", 20, 10, 20_000, 19_500));
        Assert.That(d.Meta.Flagged, Is.False, T.Why(d));
        Assert.That(d.Meta.Luck.Gambles.Count, Is.EqualTo(20));
        Assert.That(d.Meta.Luck.Gambles.Wins, Is.EqualTo(10));
    }

    // ================================================================================================================
    // Resends
    // ================================================================================================================

    [Test]
    public void Resend_AlreadyCountedEntries_Ignored()
    {
        LedgerEntry[] firstThree = { T.Flip(1, 1000, true, 1_000_000, 11), T.Flip(2, 1000, true, 1_000_950, 11), T.Flip(3, 1000, true, 1_001_900, 11) };
        SubmitDecision first = SubmitSmall(Small(1_002_850), firstThree);
        Assert.That(first.Meta.LedgerIndex, Is.EqualTo(3));

        // The client didn't hear back and sends the same entries again, plus a new one (out of order, one twice)
        LedgerEntry fourth = T.Flip(4, 1000, false, 1_002_850, 12);
        SubmitDecision second = Submit(first.Meta, T.Req(3, 2, Small(1_001_850, 12), fourth, firstThree[2], firstThree[0], firstThree[1], fourth),
                                       T.City(), Now + 30, Previous(Small(1_002_850, 12)));
        Assert.That(second.Accepted, Is.True, T.Why(second));
        Assert.That(second.Meta.LedgerIndex, Is.EqualTo(4));
        Assert.That(second.Meta.Luck.Gambles.Count, Is.EqualTo(4), "3 + 1, not 3 + 4");
        Assert.That(second.Growth.LedgerNet, Is.EqualTo(-1000));
    }

    [Test]
    public void Resend_DoesNotExplainGrowthTwice()
    {
        LedgerEntry jackpot = T.Gamble(1, "casino", "slots", 400_000, 20_000_000, 1_000_000, 12);
        SaveMeta meta = T.Meta(day: 12);
        SubmitDecision first = Submit(meta, T.Req(2, 1, Small(20_600_000, 12), jackpot), T.City(), Now, Previous(Small(1_000_000, 12)));
        Assert.That(first.Meta.Flagged, Is.False, T.Why(first));

        // The same jackpot sent again can't explain another $19.6M
        SubmitDecision second = Submit(first.Meta, T.Req(3, 2, Small(40_200_000, 12), jackpot), T.City(), Now + 20, Previous(Small(20_600_000, 12)));
        Assert.That(T.HasFlag(second.Meta, SaveCodes.FlagGrowth), Is.True, T.Why(second));
    }

    // ================================================================================================================
    // Flags
    // ================================================================================================================

    [Test]
    public void Flags_OnePerReason_UpdatedInPlace()
    {
        SubmitDecision first = SubmitSmall(Small(21_000_000));
        Assert.That(first.NewFlags.Count, Is.EqualTo(1));
        SaveFlag flag = first.Meta.Flags.Single();
        Assert.That(flag.Seq, Is.EqualTo(2));

        SubmitDecision second = Submit(first.Meta, T.Req(3, 2, Small(60_000_000, 13)), T.City(), Now + 30, Previous(Small(21_000_000, 12)));
        Assert.That(second.Accepted, Is.True);
        Assert.That(second.NewFlags, Is.Empty, "the run already had a growth flag");
        Assert.That(second.Meta.Flags.Count, Is.EqualTo(1));
        Assert.That(second.Meta.Flags[0].Seq, Is.EqualTo(3));
        Assert.That(second.Meta.Flags[0].Detail, Does.Contain("first at seq 2"));
        Assert.That(second.Findings.Single(), Does.StartWith(SaveCodes.FlagGrowth));

        SubmitDecision third = Submit(second.Meta, T.Req(4, 3, Small(90_000_000, 14)), T.City(), Now + 60, Previous(Small(60_000_000, 13)));
        Assert.That(third.Meta.Flags.Count, Is.EqualTo(1));
        Assert.That(third.Meta.Flags[0].Detail, Does.Contain("first at seq 2"), "the first seq survives repeats");
        Assert.That(third.Meta.Flags[0].Detail.Split("[again").Length, Is.EqualTo(2), "one marker, not a growing chain");
    }

    [Test]
    public void Meta_RoundTripsThroughJson()
    {
        SubmitDecision d = SubmitSmall(Small(21_000_000), T.Flip(1, 1000, true, 1_000_000, 11));
        string json = JsonConvert.SerializeObject(d.Meta);
        SaveMeta back = JsonConvert.DeserializeObject<SaveMeta>(json);
        Assert.That(back.Flags.Single().Reason, Is.EqualTo(SaveCodes.FlagGrowth));
        Assert.That(back.Luck.Gambles.Count, Is.EqualTo(1));
        Assert.That(json, Does.Contain("\"ledger\":1"));
    }
}
