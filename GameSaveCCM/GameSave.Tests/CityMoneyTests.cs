using Newtonsoft.Json;

namespace GameSave.Tests;

/// <summary>
/// The Online City money path end to end: GameSaveService over an in-memory store that keeps the city's books as CityLogic
/// does (lifetime totals, deposit and withdrawal stamps, applied ops, the 24 h / last-30 tidy-up), and a client that
/// behaves like CityOnline: a deposit leaves the company and is listed pending before the city is called, a withdrawal is
/// listed pending and paid when the answer comes, a refused deposit is refunded. Growth is judged on the effective value
/// (value + unbooked pending deposits + booked pending withdrawals) with pending transfers classified against the same city
/// read as the totals (SaveRules.ClassifyPending).
/// </summary>
public class CityMoneyTests
{
    const long M = 1_000_000;

    /// <summary>A $2B company at its last accept (seq 5, day 100) with the city, the module and a CityOnline-like client.</summary>
    sealed class Flow
    {
        public long Now = T.T0;
        public readonly MemoryStore Store = new MemoryStore();
        public readonly GameSaveService Service;
        public double Value = 2000 * M;
        public long Seq = 5;
        public readonly List<(string OpId, string Kind, long Amount)> Pending = new();
        public readonly Dictionary<string, string> Players = new();
        public readonly List<string> Logs = new();
        int nextOp;

        public Flow(long withdrawnBefore = 0)
        {
            Service = new GameSaveService(Store, () => Now, Logs.Add);
            Store.City = new CityTotals { Withdrawn = withdrawnBefore, Known = true };
            Store.Put(T.Meta(seq: 5, day: 100, value: Value, acceptedAt: Now, startedAt: Now - 36000, cityWd: withdrawnBefore), SavePacking.Pack(Save().Json()));
        }

        public SaveBuilder Save() => new SaveBuilder
        {
            Value = Value, Day = 100, Employees = T.Staff(10), MeetingsHeld = 0, Restarts = 0,
            Pending = Pending.ToList(), PendingPlayers = new Dictionary<string, string>(Players)
        };

        // ---- The city's books (CityLogic.Deposit / Withdraw / Tidy) ----
        public void Book(string op, bool deposit, long amount, long? at = null)
        {
            Store.Transfers.Add(new SaveCityTransfer { OpId = op, Time = at ?? Now, Amount = amount, Deposit = deposit });
            Store.Ops.Add(op);
            if (deposit) Store.City.Deposited += amount;
            else Store.City.Withdrawn += amount;
        }

        public void Tidy()
        {
            Store.Transfers.RemoveAll(t => t.Deposit && t.Time <= Now - 86400);
            List<SaveCityTransfer> withdrawals = Store.Transfers.Where(t => !t.Deposit).ToList();
            foreach (SaveCityTransfer old in withdrawals.Take(Math.Max(0, withdrawals.Count - 30))) Store.Transfers.Remove(old);
        }

        // ---- The client (CityOnline): the company pays first, the save lists the transfer until the city answers ----
        public string StartDeposit(long amount, string player = "player-1")
        {
            string op = "op-" + (++nextOp);
            Value -= amount;
            Pending.Add((op, "deposit", amount));
            Players[op] = player;
            return op;
        }

        public string StartWithdrawal(long amount, string player = "player-1")
        {
            string op = "op-" + (++nextOp);
            Pending.Add((op, "withdraw", amount));
            Players[op] = player;
            return op;
        }

        /// <summary>CityOnline.Settle: a refused deposit is refunded, a withdrawal is paid.</summary>
        public void Answer(string op, bool ok)
        {
            var transfer = Pending.Single(p => p.OpId == op);
            Pending.Remove(transfer);
            if (transfer.Kind == "deposit" && !ok) Value += transfer.Amount;
            if (transfer.Kind == "withdraw" && ok) Value += transfer.Amount;
        }

        // ---- Uploads (CloudSaveSync: each one prepared from the current local save) ----
        public SaveResult Upload(SaveBuilder capture = null)
        {
            Seq++;
            SubmitRequest request = T.Req(Seq, Store.Meta.Seq, capture ?? Save());
            request.SaveGz = SavePacking.Pack(request.SaveJson);
            request.SaveJson = null;
            return Service.Submit(JsonConvert.SerializeObject(request)).GetAwaiter().GetResult();
        }

        /// <summary>Upload, and on busy retry like the client (2, 4, 6, 8, 10 s later) with the current save.</summary>
        public SaveResult UploadWithRetries(SaveBuilder firstCapture = null)
        {
            SaveResult r = Upload(firstCapture);
            for (int attempt = 1; r.Code == SaveCodes.Busy && attempt <= 5; attempt++)
            {
                Now += 2 * attempt;
                r = Upload();
            }
            return r;
        }
    }

    static bool Clean(SaveResult r) => r.Ok && (r.Meta.Flags == null || r.Meta.Flags.Count == 0);
    static string Why(SaveResult r) => r.Code + ": " + r.Message;

    // ================================================================================================================
    // Honest flows
    // ================================================================================================================

    [Test]
    public void PendingDeposit_BookedBeforeTheAccept()
    {
        var f = new Flow();
        string op = f.StartDeposit(300 * M);
        f.Now += 30;
        f.Book(op, true, 300 * M);
        f.Now += 30;
        SaveResult r = f.Upload();   // listed, and booked at the read: the totals have it
        Assert.That(Clean(r), Is.True, Why(r));
        Assert.That(r.Meta.PendingDepositsUnbooked, Is.EqualTo(0));

        f.Answer(op, true);
        f.Now += 120;
        Assert.That(Clean(f.Upload()), Is.True);
    }

    [Test]
    public void PendingDeposit_BookedAfterTheAccept()
    {
        var f = new Flow();
        string op = f.StartDeposit(300 * M);
        f.Now += 30;
        SaveResult r = f.Upload();   // listed, not booked yet: the company's money in transit
        Assert.That(Clean(r), Is.True, Why(r));
        Assert.That(r.Meta.PendingDepositsUnbooked, Is.EqualTo(300 * M));

        f.Now += 60;
        f.Book(op, true, 300 * M);
        f.Answer(op, true);
        f.Now += 120;
        SaveResult next = f.Upload();
        Assert.That(Clean(next), Is.True, Why(next));
        Assert.That(next.Meta.PendingDepositsUnbooked, Is.EqualTo(0));
    }

    [Test]
    public void PendingWithdrawal_BookedBeforeTheAccept()
    {
        var f = new Flow();
        string op = f.StartWithdrawal(200 * M);
        f.Now += 30;
        f.Book(op, false, 200 * M);
        f.Now += 30;
        SaveResult r = f.Upload();   // listed and booked: out of the bank, not in the company yet
        Assert.That(Clean(r), Is.True, Why(r));
        Assert.That(r.Meta.PendingWithdrawalsBooked, Is.EqualTo(200 * M));

        f.Answer(op, true);
        f.Now += 120;
        Assert.That(Clean(f.Upload()), Is.True);
    }

    [Test]
    public void PendingWithdrawal_BookedAfterTheAccept()
    {
        var f = new Flow();
        string op = f.StartWithdrawal(200 * M);
        f.Now += 30;
        SaveResult r = f.Upload();   // listed, not booked: still in the bank
        Assert.That(Clean(r), Is.True, Why(r));
        Assert.That(r.Meta.PendingWithdrawalsBooked, Is.EqualTo(0));

        f.Now += 60;
        f.Book(op, false, 200 * M);
        f.Answer(op, true);
        f.Now += 120;
        Assert.That(Clean(f.Upload()), Is.True);
    }

    [Test]
    public void RefusedDeposit_IsRefunded_NotGrowth()
    {
        // The deposit's answer was lost at the upload; the city later refuses it and the company gets its money back
        var f = new Flow();
        string op = f.StartDeposit(300 * M);
        f.Now += 30;
        Assert.That(Clean(f.Upload()), Is.True);
        f.Now += 300;
        f.Answer(op, false);
        f.Now += 120;
        SaveResult r = f.Upload();
        Assert.That(Clean(r), Is.True, "the refund was in transit, not growth: " + Why(r));
    }

    [Test]
    public void AlreadyWired_Replay()
    {
        // The city booked the deposit but the answer never came; the client's retry gets "Already wired" later
        var f = new Flow();
        string op = f.StartDeposit(300 * M);
        f.Now += 10;
        f.Book(op, true, 300 * M);
        f.Now += 50;
        Assert.That(Clean(f.Upload()), Is.True, "listed and booked");
        f.Now += 600;
        f.Answer(op, true);   // "Already wired."
        f.Now += 120;
        Assert.That(Clean(f.Upload()), Is.True);
    }

    [Test]
    public void BookedStampTidiedAway_StillBookedByItsOp()
    {
        // A deposit booked after the accept whose answer never came; a day later its stamp is tidied, the op stays
        var f = new Flow();
        string op = f.StartDeposit(300 * M);
        f.Now += 30;
        Assert.That(Clean(f.Upload()), Is.True);
        f.Now += 60;
        f.Book(op, true, 300 * M);
        f.Now += 90_000;
        f.Tidy();
        Assert.That(f.Store.Transfers.Any(t => t.OpId == op), Is.False, "the stamp is gone");
        SaveResult r = f.Upload();
        Assert.That(Clean(r), Is.True, Why(r));
        Assert.That(r.Meta.PendingDepositsUnbooked, Is.EqualTo(0), "booked by its op");
    }

    [Test]
    public void TransfersFinishedBeforeTheSave_OutsideTheSettleWindow()
    {
        var f = new Flow();
        string deposit = f.StartDeposit(300 * M);
        f.Book(deposit, true, 300 * M);
        f.Answer(deposit, true);
        string withdrawal = f.StartWithdrawal(100 * M);
        f.Book(withdrawal, false, 100 * M);
        f.Answer(withdrawal, true);
        f.Now += 100;   // both finished well before this upload
        SaveResult r = f.Upload();
        Assert.That(Clean(r), Is.True, Why(r));
    }

    [Test]
    public void UploadInFlightWhileTheCityBooks_BusyThenTheNewerSave()
    {
        // The upload's save was read before the player wired money; the city booked it before the server read the city
        var f = new Flow();
        SaveBuilder inFlight = f.Save();
        string deposit = f.StartDeposit(300 * M);
        f.Book(deposit, true, 300 * M);
        f.Answer(deposit, true);
        f.Now += 3;
        SaveResult first = f.Upload(inFlight);
        Assert.That(first.Code, Is.EqualTo(SaveCodes.Busy), Why(first));
        Assert.That(f.Store.Writes, Is.EqualTo(0), "nothing stored");
        f.Now += 2;
        Assert.That(Clean(f.Upload()), Is.True, "the newer save shows the deposit");

        // A withdrawal: busy until the stamp is older than the settle window, whatever the save shows
        f.Now += 120;
        inFlight = f.Save();
        string withdrawal = f.StartWithdrawal(200 * M);
        f.Book(withdrawal, false, 200 * M);
        f.Answer(withdrawal, true);
        f.Now += 3;
        SaveResult r = f.UploadWithRetries(inFlight);
        Assert.That(Clean(r), Is.True, Why(r));
        Assert.That(f.Now - f.Store.Transfers.Last().Time, Is.GreaterThan(SaveRules.CityTransferSettleSeconds));
    }

    [Test]
    public void LegacyTransferWithoutAPlayer_CountsAsTheCurrentAccounts()
    {
        var f = new Flow();
        string op = f.StartWithdrawal(200 * M, player: null);
        f.Book(op, false, 200 * M);
        f.Now += 60;
        SaveResult r = f.Upload();
        Assert.That(Clean(r), Is.True, Why(r));
        Assert.That(r.Meta.PendingWithdrawalsBooked, Is.EqualTo(200 * M));
        f.Answer(op, true);
        f.Now += 120;
        Assert.That(Clean(f.Upload()), Is.True);
    }

    [Test]
    public void AnotherAccountsPendingTransfer_CountsNowhere()
    {
        // A deposit another sign-in started on this company: its money left the company, this account's city never sees it
        var f = new Flow();
        f.StartDeposit(300 * M, player: "someone-else");
        f.Now += 60;
        SaveResult r = f.Upload();
        Assert.That(Clean(r), Is.True, "a drop in value is never flagged: " + Why(r));
        Assert.That(r.Meta.PendingDepositsUnbooked, Is.EqualTo(0));
        f.Now += 120;
        Assert.That(Clean(f.Upload()), Is.True);
    }

    [Test]
    public void CityUnreadable_KeepsTheBaselineAndThePendingSums()
    {
        var f = new Flow();
        string op = f.StartDeposit(300 * M);
        f.Now += 30;
        Assert.That(f.Upload().Meta.PendingDepositsUnbooked, Is.EqualTo(300 * M));

        f.Store.FailCity = true;
        f.Now += 60;
        f.Book(op, true, 300 * M);
        f.Answer(op, true);
        f.Now += 120;
        SaveResult blind = f.Upload();
        Assert.That(Clean(blind), Is.True, Why(blind));
        Assert.That(blind.Meta.PendingDepositsUnbooked, Is.EqualTo(300 * M), "kept");
        Assert.That(blind.Meta.CityDeposited, Is.EqualTo(0), "kept");

        f.Store.FailCity = false;
        f.Now += 120;
        SaveResult back = f.Upload();
        Assert.That(Clean(back), Is.True, "measured from the kept sums: " + Why(back));
        Assert.That(back.Meta.PendingDepositsUnbooked, Is.EqualTo(0));
    }

    // ================================================================================================================
    // The review's cheats
    // ================================================================================================================

    [Test]
    public void C1_ReListingAnOldBookedWithdrawal_Flagged()
    {
        // A real $400M withdrawal booked long ago (in the baseline, still among the last 30 stamps) listed as pending again:
        // the company's money in transit jumps by $400M that the city didn't move in this stretch
        var f = new Flow(withdrawnBefore: 400 * M);
        f.Book("old-op", false, 400 * M, at: f.Now - 7200);
        f.Store.City.Withdrawn -= 400 * M;   // Book added it again: it was already in the baseline
        f.Pending.Add(("old-op", "withdraw", 400 * M));
        f.Now += 120;
        SaveResult listed = f.Upload();
        Assert.That(listed.Ok, Is.True);
        Assert.That(T.HasFlag(listed.Meta, SaveCodes.FlagGrowth), Is.True, "flagged in the save that lists it: " + Why(listed));

        // ...and dropping it with the money added doesn't unflag anything
        f.Pending.Clear();
        f.Value += 400 * M;
        f.Now += 120;
        Assert.That(T.HasFlag(f.Upload().Meta, SaveCodes.FlagGrowth), Is.True);
    }

    [Test]
    public void C2_PendingDepositListedWithoutPaying_Flagged()
    {
        // A deposit listed as pending while the company kept the money, then wired straight to the city
        var f = new Flow();
        f.Pending.Add(("fake-dep", "deposit", 900 * M));
        f.Now += 60;
        SaveResult listed = f.Upload();
        Assert.That(T.HasFlag(listed.Meta, SaveCodes.FlagGrowth), Is.True, "flagged in the save that lists it: " + Why(listed));

        f.Book("fake-dep", true, 900 * M);
        f.Pending.Clear();
        f.Now += 120;
        Assert.That(T.HasFlag(f.Upload().Meta, SaveCodes.FlagGrowth), Is.True);
    }

    [Test]
    public void C5_UnpaidDirectDepositRightBeforeAnUpload_BusyThenFlagged()
    {
        // $900M wired to the city without the company paying, seconds before an upload: the upload waits (busy) while the
        // save could be from just before, then it is judged
        var f = new Flow();
        f.Now += 60;
        f.Book("direct", true, 900 * M);
        f.Now += 2;
        SaveResult r = f.UploadWithRetries();
        Assert.That(r.Ok, Is.True, Why(r));
        Assert.That(T.HasFlag(r.Meta, SaveCodes.FlagGrowth), Is.True, "no timing window excuses it: " + Why(r));
    }
}
