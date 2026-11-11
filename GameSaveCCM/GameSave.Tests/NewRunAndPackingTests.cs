using System.IO.Compression;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GameSave.Tests;

/// <summary>
/// NewRun's time baseline (a New Game can start offline and reach the server later) and packed saves (SubmitRequest.SaveGz,
/// save_data stored packed).
/// </summary>
public class NewRunAndPackingTests
{
    static SaveBuilder Company(double value, int day, int staff = 20) =>
        new SaveBuilder { Value = value, Day = day, Employees = T.Staff(staff, iq: 115, gpa: 3.0, exp: 12), MeetingsHeld = day / 10 };

    static SubmitDecision Submit(SaveMeta meta, SubmitRequest request, long now, SaveSubmitContext context = null) =>
        SaveRules.EvaluateSubmit(meta, request, T.City(), now, context);

    static SubmitRequest Packed(SubmitRequest request)
    {
        SubmitRequest copy = JsonConvert.DeserializeObject<SubmitRequest>(JsonConvert.SerializeObject(request));
        copy.SaveGz = SavePacking.Pack(request.SaveJson);
        copy.SaveJson = null;
        return copy;
    }

    // ================================================================================================================
    // NewRun: the time baseline
    // ================================================================================================================

    [Test]
    public void NewRunMeta_TimedFromThePreviousMetasLastAccept()
    {
        SaveMeta previous = T.Meta(run: "old-run", acceptedAt: T.T0 - 500);
        previous.Ended = true;
        SaveMeta meta = SaveRules.NewRunMeta("new-run", T.T0, T.City(4, 5), previous);
        Assert.That(meta.AcceptedAt, Is.EqualTo(T.T0 - 500), "any earlier run counts, ended or not");
        Assert.That(meta.StartedAt, Is.EqualTo(T.T0));
        Assert.That(meta.Seq, Is.EqualTo(0));
        Assert.That(meta.Value, Is.EqualTo(SaveRules.StartingValue));
        Assert.That(meta.Flags, Is.Empty);
        Assert.That(meta.CityDeposited, Is.EqualTo(4));
        Assert.That(SaveRules.IsFirstSaveWithoutBaseline(meta), Is.False);

        Assert.That(SaveRules.NewRunMeta("new-run", T.T0, T.City(), null).AcceptedAt, Is.EqualTo(0), "no meta at all: no baseline");
        Assert.That(SaveRules.NewRunMeta("new-run", T.T0, T.City(), T.Meta(acceptedAt: T.T0 + 999)).AcceptedAt, Is.EqualTo(T.T0),
                    "a clock that ran ahead elsewhere never gives a baseline in the future");
    }

    [Test]
    public void OfflineNewGame_AfterAPreviousRun_Accepted()
    {
        // The old run's last save was accepted at T0. The player started a New Game offline right away, played 30 minutes
        // (day 100), and NewRun only reached the server on reconnecting.
        SaveMeta previous = T.Meta(run: "old-run", seq: 40, day: 300, value: 9e8, acceptedAt: T.T0);
        long reconnect = T.T0 + 1800;
        SaveMeta meta = SaveRules.NewRunMeta("new-run", reconnect, T.City(), previous);
        SubmitDecision d = Submit(meta, T.Req("new-run", 1, 0, Company(3_000_000, 100)), reconnect + 10);
        Assert.That(d.Accepted, Is.True, T.Why(d));
        Assert.That(d.Meta.Flagged, Is.False, T.Why(d));
        Assert.That(d.Growth, Is.Not.Null, "a run with a baseline gets the growth check");
        Assert.That(d.Meta.StartedAt, Is.EqualTo(reconnect));

        // Timed from NewRun itself, the same save would be rejected
        SaveMeta fromNow = SaveRules.NewRunMeta("new-run", reconnect, T.City(), T.Meta(acceptedAt: reconnect));
        Assert.That(Submit(fromNow, T.Req("new-run", 1, 0, Company(3_000_000, 100)), reconnect + 10).Code, Is.EqualTo(SaveCodes.Time));
    }

    [Test]
    public void BrandNewPlayer_NewRunThenDay100_CheckedLikeAnImport()
    {
        // No meta at all before NewRun: no baseline, so the first save skips the time rule and gets the import check
        SaveMeta meta = SaveRules.NewRunMeta("run-1", T.T0, T.City(), null);
        SubmitDecision d = Submit(meta, T.Req("run-1", 3, 0, Company(3_000_000, 100)), T.T0 + 60);
        Assert.That(d.Accepted, Is.True, T.Why(d));
        Assert.That(d.Meta.Flagged, Is.False, T.Why(d));
        Assert.That(d.Growth, Is.Null, "no growth baseline, like an import");
        Assert.That(d.Meta.StartedAt, Is.EqualTo(T.T0), "the run keeps the start NewRun gave it");
        Assert.That(d.Meta.AcceptedAt, Is.EqualTo(T.T0 + 60));

        SubmitDecision absurd = Submit(meta, T.Req("run-1", 3, 0, Company(1e15, 100)), T.T0 + 60);
        Assert.That(absurd.Accepted, Is.True);
        Assert.That(T.HasFlag(absurd.Meta, SaveCodes.FlagImport), Is.True, T.Why(absurd));

        // From the first accepted save on, the time rule applies as usual
        SubmitDecision next = Submit(d.Meta, T.Req("run-1", 4, 3, Company(3_100_000, 150)), T.T0 + 120);
        Assert.That(next.Code, Is.EqualTo(SaveCodes.Time), "60 s after the first accept allow 7 days, not 50");
        Assert.That(Submit(d.Meta, T.Req("run-1", 4, 3, Company(3_100_000, 104)), T.T0 + 120).Accepted, Is.True);
    }

    [Test]
    public void OnlineNewGame_RightAway_StillStrict()
    {
        // The old run was accepted 10 s before NewRun: the new run's first save may be at most 8 days in after 70 s
        SaveMeta meta = SaveRules.NewRunMeta("new-run", T.T0, T.City(), T.Meta(run: "old-run", acceptedAt: T.T0 - 10));
        SubmitDecision d = Submit(meta, T.Req("new-run", 1, 0, Company(80_000, 3, 2)), T.T0 + 60);
        Assert.That(d.Accepted, Is.True, T.Why(d));
        Assert.That(d.Meta.Flagged, Is.False, T.Why(d));
        Assert.That(Submit(meta, T.Req("new-run", 1, 0, Company(80_000, 20, 2)), T.T0 + 60).Code, Is.EqualTo(SaveCodes.Time));
    }

    [Test]
    public void OldRunAcceptedDaysAgo_WidensTheWindow()
    {
        // Known v1 allowance: the previous run's last accept five days ago lets the new run's first save be up to
        // floor(5 days / 15 s x 1.1) + 3 = 31,683 days in. The growth check still applies (from $50k at day 0).
        SaveMeta meta = SaveRules.NewRunMeta("new-run", T.T0, T.City(), T.Meta(run: "old-run", acceptedAt: T.T0 - 5 * 86400));
        SubmitDecision d = Submit(meta, T.Req("new-run", 1, 0, Company(40_000_000, 2000)), T.T0 + 60);
        Assert.That(d.Accepted, Is.True, T.Why(d));
        Assert.That(d.Growth, Is.Not.Null);
    }

    [Test]
    public async Task Service_NewRun_UsesTheStoredMetaAsBaseline()
    {
        long now = T.T0;
        var store = new MemoryStore();
        var service = new GameSaveService(store, () => now);

        // A brand-new player: no baseline; a day-100 first save is accepted with the import check
        Assert.That((await service.NewRun("run-a")).Ok, Is.True);
        Assert.That(store.Meta.AcceptedAt, Is.EqualTo(0));
        now += 60;
        SaveResult first = await service.Submit(JsonConvert.SerializeObject(Packed(T.Req("run-a", 1, 0, Company(3_000_000, 100)))));
        Assert.That(first.Ok, Is.True, first.Message);
        Assert.That(first.Meta.Flagged, Is.False, first.Message);

        // Game Over, then a New Game played offline for 30 minutes: timed from run-a's last accept
        Assert.That((await service.EndRun("run-a", first.Meta.Seq, -1, null)).Ok, Is.True);
        long lastAccept = store.Meta.AcceptedAt;
        now += 1800;
        Assert.That((await service.NewRun("run-b")).Ok, Is.True);
        Assert.That(store.Meta.AcceptedAt, Is.EqualTo(lastAccept));
        Assert.That(store.Meta.StartedAt, Is.EqualTo(now));
        Assert.That((await service.NewRun("run-b")).Meta.AcceptedAt, Is.EqualTo(lastAccept), "NewRun stays a no-op for the live run");
        now += 10;
        SaveResult offline = await service.Submit(JsonConvert.SerializeObject(Packed(T.Req("run-b", 1, 0, Company(3_000_000, 100)))));
        Assert.That(offline.Ok, Is.True, offline.Message);
        Assert.That(offline.Meta.Flagged, Is.False, offline.Message);
    }

    // ================================================================================================================
    // Packed saves
    // ================================================================================================================

    static string Fixture() => File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "jsonutility_save.json"));

    [Test]
    public void Packing_RoundTrips()
    {
        string json = Fixture();
        string packed = SavePacking.Pack(json);
        Assert.That(SavePacking.IsPacked(packed), Is.True);
        Assert.That(packed, Does.StartWith(SavePacking.Prefix));
        Assert.That(packed.Length, Is.LessThan(json.Length / 2));
        Assert.That(SavePacking.Unpack(packed), Is.EqualTo(json));
        Assert.That(SavePacking.Unpack(json), Is.EqualTo(json), "plain JSON (saves from before packing) passes through");
        Assert.That(SavePacking.Unpack(null), Is.Null);
        Assert.That(SaveSnapshot.ParseStored(packed).CompanyValue, Is.EqualTo(SaveSnapshot.Parse(json).CompanyValue));
    }

    [Test]
    public void PackedRequest_DecidesLikeTheUnpackedOne()
    {
        // A submit with a ledger and a growth flag, sent both ways
        SaveMeta meta = T.Meta(day: 10);
        var save = Company(21_000_000, 12, 5);
        var context = new SaveSubmitContext { PreviousSaveJson = Company(1_000_000, 10, 5).Json() };
        SubmitRequest plain = T.Req(2, 1, save, T.Flip(1, 1000, true, 1_000_000, 11), T.Card(2, "low", true, 1_000_950, 20_000, 11));
        SubmitRequest packed = Packed(plain);

        SubmitDecision a = Submit(meta, plain, T.T0 + 120, context);
        SubmitDecision b = Submit(meta, packed, T.T0 + 120, context);
        Assert.That(a.Code, Is.EqualTo(SaveCodes.Accepted));
        Assert.That(T.HasFlag(a.Meta, SaveCodes.FlagGrowth), Is.True, "the scenario has a flag to compare");
        Assert.That(b.Code, Is.EqualTo(a.Code));
        Assert.That(JsonConvert.SerializeObject(b.Meta), Is.EqualTo(JsonConvert.SerializeObject(a.Meta)));
        Assert.That(b.Findings, Is.EqualTo(a.Findings));
        Assert.That(b.Growth.Residual, Is.EqualTo(a.Growth.Residual));

        Assert.That(packed.SaveJson, Is.EqualTo(plain.SaveJson), "the server fills SaveJson from SaveGz");
        Assert.That(b.StoredSave, Is.SameAs(packed.SaveGz), "a packed save is stored as sent");
        Assert.That(SavePacking.IsPacked(a.StoredSave), Is.True, "a plain save is packed for storage");
        Assert.That(SavePacking.Unpack(a.StoredSave), Is.EqualTo(plain.SaveJson));

        // The previous save read from packed storage decides the same
        var packedContext = new SaveSubmitContext { PreviousSaveJson = SavePacking.Pack(context.PreviousSaveJson) };
        SubmitDecision c = Submit(meta, T.Req(2, 1, save, T.Flip(1, 1000, true, 1_000_000, 11), T.Card(2, "low", true, 1_000_950, 20_000, 11)),
                                  T.T0 + 120, packedContext);
        Assert.That(JsonConvert.SerializeObject(c.Meta), Is.EqualTo(JsonConvert.SerializeObject(a.Meta)));
    }

    [Test]
    public void PackedRequest_SaveGzTakesPrecedence()
    {
        SaveBuilder sent = Company(1_050_000, 12, 5), other = Company(9_999_999, 12, 5);
        SubmitRequest request = T.Req(2, 1, sent);
        request.SaveGz = SavePacking.Pack(sent.Json());
        request.SaveJson = other.Json();   // ignored
        SubmitDecision d = Submit(T.Meta(day: 10), request, T.T0 + 120);
        Assert.That(d.Accepted, Is.True, T.Why(d));
        Assert.That(d.Meta.Value, Is.EqualTo(1_050_000));
    }

    static string PackBytes(byte[] raw)
    {
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.Optimal, true)) gzip.Write(raw, 0, raw.Length);
        return SavePacking.Prefix + Convert.ToBase64String(buffer.ToArray());
    }

    [Test]
    public void PackedRequest_Broken_IsInvalid()
    {
        SaveMeta meta = T.Meta(day: 10);
        SubmitRequest Broken(string saveGz)
        {
            SubmitRequest r = T.Req(2, 1, Company(1_050_000, 12, 5));
            r.SaveJson = null;
            r.SaveGz = saveGz;
            return r;
        }

        var random = new byte[4096];
        new Random(7).NextBytes(random);
        var cases = new Dictionary<string, string>
        {
            ["broken base64"] = SavePacking.Prefix + "!!!not base64 at all!!!",
            ["broken gzip"] = SavePacking.Prefix + Convert.ToBase64String(random),
            ["truncated gzip"] = SavePacking.Pack(Company(1_050_000, 12, 5).Json()).Substring(0, 200),
            ["not json inside"] = PackBytes(Encoding.UTF8.GetBytes("this is not a save")),
            ["empty inside"] = PackBytes(new byte[0]),
            ["oversized packed"] = SavePacking.Prefix + new string('A', SaveCloud.MaxPackedSaveBytes),
            // A zip bomb: a few KB that unpack to 17 MB (more than SaveCloud.MaxSaveBytes)
            ["zip bomb"] = PackBytes(new byte[17 * 1024 * 1024])
        };
        Assert.That(cases["zip bomb"].Length, Is.LessThan(100_000), "the bomb is small packed");
        foreach (var pair in cases)
        {
            SubmitDecision d = Submit(meta, Broken(pair.Value), T.T0 + 120);
            Assert.That(d.Code, Is.EqualTo(SaveCodes.Invalid), pair.Key + ": " + T.Why(d));
            Assert.That(d.Meta, Is.SameAs(meta));
            Assert.That(d.StoredSave, Is.Null);
        }
        Assert.That(SaveSnapshot.ParseStored(cases["zip bomb"]).Ok, Is.False, "stored saves are unpacked with the same limit");
    }

    [Test]
    public void BigCompany_FitsPacked()
    {
        // 6,000 employees: far over the old 1 MB limit unpacked, under 2 MB packed. The builder's staff are near copies (ids
        // e0..e5999, same names), so they pack far better than real saves; Packing_RealisticSaves_AboutThreeTimesSmaller
        // measures real-looking records (6,000 of those still fit, at ~240 bytes each)
        var save = new SaveBuilder { Value = 5e9, Day = 900, Employees = T.Staff(6000, iq: 120, gpa: 3.2, exp: 15), MeetingsHeld = 90 };
        string json = save.Json();
        string packed = SavePacking.Pack(json);
        TestContext.Out.WriteLine($"6,000 employees: {json.Length:N0} characters, {packed.Length:N0} packed");
        Assert.That(json.Length, Is.GreaterThan(1024 * 1024));
        Assert.That(packed.Length, Is.LessThan(SaveCloud.MaxPackedSaveBytes));

        SubmitRequest request = Packed(T.Req(2, 1, save));
        SubmitDecision d = Submit(T.Meta(day: 880, value: 4.9e9), request, T.T0 + 600, new SaveSubmitContext { PreviousSaveJson = SavePacking.Pack(save.With(s => s.Day = 880).Json()) });
        Assert.That(d.Accepted, Is.True, T.Why(d));
        Assert.That(d.Meta.Flagged, Is.False, T.Why(d));
    }

    [Test]
    public void Packing_RealisticSaves_AboutThreeTimesSmaller()
    {
        // Real saves pack about 3x (the review measured ~3.3x, ~240 bytes per employee packed): GUID ids, full float digits,
        // varied names and traits don't repeat. 2,000 varied copies of the real JsonUtility fixture's employee record, each
        // with its StaffLedger record in the "staff" extra (seven full-precision doubles, Staff/StaffLedger.cs:109-125),
        // written compact like the game does (SaveSystem.cs:663)
        JObject save = JObject.Parse(Fixture());
        var template = (JObject)save["employees"][0];
        var staffRecords = new JArray();
        var rng = new Random(7);
        string[] first = { "Andrea", "Mateo", "Yuki", "Priya", "Liam", "Fatima", "Chen", "Olga", "Kwame", "Sofia", "Omar", "Hana", "Diego", "Aisha", "Noah", "Ines" };
        string[] last = { "Villarreal", "Okafor", "Tanaka", "Sharma", "Murphy", "Haddad", "Wang", "Petrova", "Mensah", "Rossi", "Yilmaz", "Kim", "Silva", "Novak" };
        string[] traits = { "Drama Starter", "Organized", "Addicted to Coffee", "Night Owl", "Team Player", "Perfectionist", "Lazy", "Genius", "Networker", "Clumsy" };
        var employees = new JArray();
        for (int i = 0; i < 2000; i++)
        {
            var e = (JObject)template.DeepClone();
            e["name"] = first[rng.Next(first.Length)] + " " + last[rng.Next(last.Length)];
            e["age"] = rng.Next(18, 65);
            e["GPA"] = 1.5 + rng.NextDouble() * 2.5;
            e["IQ"] = rng.Next(70, 171);
            e["salary"] = rng.Next(30_000, 250_000);
            e["experienceYears"] = rng.Next(0, 40);
            e["portraitIndex"] = rng.Next(0, 8000);
            var picked = new JArray();
            for (int t = rng.Next(1, 4); t > 0; t--) picked.Add(new JObject { ["traitName"] = traits[rng.Next(traits.Length)], ["type"] = rng.Next(0, 3) });
            e["traits"] = picked;
            var id = new byte[16];
            rng.NextBytes(id);
            e["id"] = Convert.ToHexString(id).ToLowerInvariant();
            employees.Add(e);
            int hired = rng.Next(1, 400);
            staffRecords.Add(new JObject
            {
                ["id"] = e["id"], ["hireDay"] = hired, ["hireDayKnown"] = true, ["daysEmployed"] = 400 - hired, ["lastDay"] = 400,
                ["lastOutput"] = rng.NextDouble() * 5000, ["lastSalary"] = rng.NextDouble() * 700, ["lastIncidents"] = rng.Next(4) == 0 ? rng.NextDouble() * 900 : 0.0,
                ["lastOverhead"] = rng.NextDouble() * 60, ["lastBonus"] = rng.NextDouble() * 200, ["lastProfit"] = rng.NextDouble() * 4000 - 500,
                ["lifetimeProfit"] = rng.NextDouble() * 900_000, ["incidentDays"] = rng.Next(0, 5)
            });
        }
        save["employees"] = employees;
        JObject staffExtra = save["extras"].Children<JObject>().First(x => (string)x["key"] == "staff");
        staffExtra["json"] = new JObject { ["version"] = 1, ["fired"] = 0, ["severancePaid"] = 0.0, ["records"] = staffRecords }.ToString(Formatting.None);
        string json = save.ToString(Formatting.None);
        string packed = SavePacking.Pack(json);
        double ratio = (double)json.Length / packed.Length, perEmployee = packed.Length / 2000.0;
        TestContext.Out.WriteLine($"2,000 real-looking employees: {json.Length:N0} characters, {packed.Length:N0} packed ({ratio:0.0}x, {perEmployee:0} per employee)");
        Assert.That(ratio, Is.GreaterThan(2.0).And.LessThan(5.0), "about 3x, not 10x");
        Assert.That(perEmployee, Is.GreaterThan(150).And.LessThan(400));
        Assert.That(6000 * perEmployee, Is.LessThan(SaveCloud.MaxPackedSaveBytes), "6,000 real employees still fit packed");
        Assert.That(SaveSnapshot.ParseStored(packed).Employees.Count, Is.EqualTo(2000));
    }

    // ================================================================================================================
    // Packed ledger (SubmitRequest.LedgerGz)
    // ================================================================================================================

    /// <summary>The request with its ledger sent packed (LedgerGz) and Ledger left empty, as the client sends it.</summary>
    static SubmitRequest WithPackedLedger(SubmitRequest request)
    {
        SubmitRequest copy = JsonConvert.DeserializeObject<SubmitRequest>(JsonConvert.SerializeObject(request));
        copy.LedgerGz = SavePacking.Pack(JsonConvert.SerializeObject(request.Ledger));
        copy.Ledger = new List<LedgerEntry>();
        return copy;
    }

    [Test]
    public async Task PackedLedger_DecidesLikeTheUnpackedOne()
    {
        SaveMeta meta = T.Meta(day: 10);
        var context = new SaveSubmitContext { PreviousSaveJson = Company(1_000_000, 10, 5).Json() };
        SubmitRequest plain = T.Req(2, 1, Company(21_000_000, 12, 5), T.Flip(1, 1000, true, 1_000_000, 11), T.Card(2, "low", true, 1_000_950, 20_000, 11),
                                    T.Gamble(3, "casino", "slots", 1000, 9000, 1_020_950, 11), T.Summary(4, "casino", "blackjack", 30, 12, 30_000, 31_000));
        SubmitRequest packed = WithPackedLedger(plain);
        Assert.That(packed.Ledger, Is.Empty);

        SubmitDecision a = Submit(meta, plain, T.T0 + 120, context);
        SubmitDecision b = Submit(meta, packed, T.T0 + 120, context);
        Assert.That(T.HasFlag(a.Meta, SaveCodes.FlagGrowth), Is.True, "the scenario has a flag to compare");
        Assert.That(JsonConvert.SerializeObject(b.Meta), Is.EqualTo(JsonConvert.SerializeObject(a.Meta)));
        Assert.That(b.Findings, Is.EqualTo(a.Findings));
        Assert.That(b.Growth.LedgerNet, Is.EqualTo(a.Growth.LedgerNet));
        Assert.That(packed.Ledger.Count, Is.EqualTo(4), "the server fills Ledger from LedgerGz");

        // LedgerGz takes precedence: an entry left in Ledger is ignored
        SubmitRequest both = WithPackedLedger(plain);
        both.Ledger = new List<LedgerEntry> { T.Gamble(1, "casino", "coinflip", 1000, 1e9, 1_000_000, 11) };
        Assert.That(JsonConvert.SerializeObject(Submit(meta, both, T.T0 + 120, context).Meta), Is.EqualTo(JsonConvert.SerializeObject(a.Meta)));

        // Through the service, as the client sends it: packed save, packed ledger
        var store = new MemoryStore();
        store.Put(T.Meta(day: 10), Company(1_000_000, 10, 5).Json());
        var service = new GameSaveService(store, () => T.T0 + 120);
        SubmitRequest wire = WithPackedLedger(Packed(T.Req(2, 1, Company(1_000_950, 12, 5), T.Flip(1, 1000, true, 1_000_000, 11))));
        SaveResult r = await service.Submit(JsonConvert.SerializeObject(wire));
        Assert.That(r.Ok, Is.True, r.Message);
        Assert.That(r.Meta.LedgerIndex, Is.EqualTo(1));
        Assert.That(r.Meta.Luck.Gambles.Count, Is.EqualTo(1));
    }

    [Test]
    public void PackedLedger_Broken_IsInvalid()
    {
        SaveMeta meta = T.Meta(day: 10);
        SubmitRequest With(string ledgerGz)
        {
            SubmitRequest r = T.Req(2, 1, Company(1_050_000, 12, 5));
            r.LedgerGz = ledgerGz;
            return r;
        }

        var random = new byte[4096];
        new Random(11).NextBytes(random);
        List<LedgerEntry> tooMany = Enumerable.Range(1, SaveRules.MaxLedgerEntries + 1).Select(i => T.Flip(i, 10, false, 1_000_000, 11)).ToList();
        // Valid ledgers that are too big: 4,000 entries with random 450-character sources (packs past 1 MB), and 20,000 with
        // 250-character ones (packs small, unpacks past 4 MB)
        var noise = new Random(12);
        string Hex(int length) => string.Concat(Enumerable.Range(0, length).Select(_ => "0123456789abcdef"[noise.Next(16)]));
        List<LedgerEntry> noisy = Enumerable.Range(1, 4000).Select(i => T.Gamble(i, Hex(450), null, 10, 0, 1_000_000, 11)).ToList();
        List<LedgerEntry> wordy = Enumerable.Range(1, SaveRules.MaxLedgerEntries).Select(i => T.Gamble(i, new string('x', 250), null, 10, 0, 1_000_000, 11)).ToList();
        var cases = new Dictionary<string, string>
        {
            ["broken base64"] = SavePacking.Prefix + "!!!not base64!!!",
            ["broken gzip"] = SavePacking.Prefix + Convert.ToBase64String(random),
            ["not JSON inside"] = SavePacking.Pack("this is not a ledger"),
            ["an object, not a list"] = SavePacking.Pack("{\"i\": 1, \"k\": \"gamble\"}"),
            ["wrong types"] = SavePacking.Pack("[{\"i\": \"one\", \"k\": \"gamble\"}]"),
            // A few KB that unpack to 5 MB (more than MaxLedgerJsonBytes)
            ["bomb"] = PackBytes(new byte[5 * 1024 * 1024]),
            ["oversized packed"] = SavePacking.Prefix + new string('A', SaveRules.MaxPackedLedgerBytes + 1),
            ["valid but packed past 1 MB"] = SavePacking.Pack(JsonConvert.SerializeObject(noisy)),
            ["valid but unpacking past 4 MB"] = SavePacking.Pack(JsonConvert.SerializeObject(wordy)),
            ["more entries than a submit may carry"] = SavePacking.Pack(JsonConvert.SerializeObject(tooMany))
        };
        Assert.That(cases["bomb"].Length, Is.LessThan(100_000), "the bomb is small packed");
        Assert.That(cases["valid but packed past 1 MB"].Length, Is.GreaterThan(SaveRules.MaxPackedLedgerBytes));
        Assert.That(JsonConvert.SerializeObject(noisy).Length, Is.LessThan(SaveRules.MaxLedgerJsonBytes), "it would unpack fine");
        Assert.That(cases["valid but unpacking past 4 MB"].Length, Is.LessThan(SaveRules.MaxPackedLedgerBytes));
        Assert.That(JsonConvert.SerializeObject(wordy).Length, Is.GreaterThan(SaveRules.MaxLedgerJsonBytes));
        Assert.That(cases["more entries than a submit may carry"].Length, Is.LessThan(SaveRules.MaxPackedLedgerBytes), "packed it fits; unpacked it's too many");
        foreach (var pair in cases)
        {
            SubmitDecision d = Submit(meta, With(pair.Value), T.T0 + 120);
            Assert.That(d.Code, Is.EqualTo(SaveCodes.Invalid), pair.Key + ": " + T.Why(d));
            Assert.That(d.Meta, Is.SameAs(meta));
            Assert.That(d.StoredSave, Is.Null);
        }

        // An empty packed list, or "null", is simply no ledger
        Assert.That(Submit(meta, With(SavePacking.Pack("[]")), T.T0 + 120).Accepted, Is.True);
        Assert.That(Submit(meta, With(SavePacking.Pack("null")), T.T0 + 120).Accepted, Is.True);
    }

    [Test]
    public async Task Service_StoresThePackedSaveAsSent_AndReadsPackedStorage()
    {
        long now = T.T0;
        var store = new MemoryStore();
        List<SaveEmployeeInfo> staff = T.Staff(200, iq: 140, gpa: 3.6, exp: 20, phd: true);
        // The previous save is stored packed: 200 strong employees before 190 were let go (it must be read to explain the growth)
        store.Put(T.Meta(value: 10_000_000), SavePacking.Pack(new SaveBuilder { Value = 10_000_000, Day = 10, Employees = staff, MeetingsHeld = 0 }.Json()));
        var service = new GameSaveService(store, () => now);
        now += 120;

        SubmitRequest request = Packed(T.Req(2, 1, new SaveBuilder { Value = 50_000_000, Day = 14, Employees = staff.Take(10).ToList(), MeetingsHeld = 0 }));
        SaveResult r = await service.Submit(JsonConvert.SerializeObject(request));
        Assert.That(r.Ok, Is.True, r.Message);
        Assert.That(r.Meta.Flagged, Is.False, r.Message);
        Assert.That(store.Data, Is.EqualTo(request.SaveGz), "save_data is the packed text as sent");

        // A plain (old-style) request still works and is stored packed
        now += 120;
        SubmitRequest plain = T.Req(3, 2, new SaveBuilder { Value = 50_500_000, Day = 16, Employees = staff.Take(10).ToList(), MeetingsHeld = 0 });
        SaveResult r2 = await service.Submit(JsonConvert.SerializeObject(plain));
        Assert.That(r2.Ok, Is.True, r2.Message);
        Assert.That(SavePacking.IsPacked(store.Data), Is.True);
        Assert.That(SavePacking.Unpack(store.Data), Is.EqualTo(plain.SaveJson));

        // A broken packed save changes nothing
        int writes = store.Writes;
        SubmitRequest broken = Packed(T.Req(4, 3, new SaveBuilder { Value = 50_600_000, Day = 17, Employees = staff.Take(10).ToList() }));
        broken.SaveGz = broken.SaveGz.Substring(0, broken.SaveGz.Length / 2);
        SaveResult r3 = await service.Submit(JsonConvert.SerializeObject(broken));
        Assert.That(r3.Code, Is.EqualTo(SaveCodes.Invalid));
        Assert.That(store.Writes, Is.EqualTo(writes));
    }
}
