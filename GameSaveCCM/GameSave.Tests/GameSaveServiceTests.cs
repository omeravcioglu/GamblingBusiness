using Newtonsoft.Json;

namespace GameSave.Tests;

/// <summary>The endpoints' flow (GameSaveService over an in-memory Cloud Save with write locks).</summary>
public class GameSaveServiceTests
{
    long now = T.T0;
    readonly List<string> logs = new();

    GameSaveService Service(MemoryStore store) => new GameSaveService(store, () => now, logs.Add);

    static string Json(SubmitRequest request) => JsonConvert.SerializeObject(request);

    static SaveBuilder Save(double value, int day) => new SaveBuilder { Value = value, Day = day, Employees = T.Staff(3), MeetingsHeld = 0 };

    [Test]
    public async Task NewRun_WritesFreshMetaWithCityBaseline_AndDropsOldData()
    {
        var store = new MemoryStore { City = T.City(1234, 55) };
        store.Put(T.Meta(run: "old-run"), "{\"old\":true}");

        // This client saw the old run's last accept (T0): its New Game may replace it
        SaveResult r = await Service(store).NewRun("new-run", T.T0);
        Assert.That(r.Ok, Is.True, r.Message);
        Assert.That(r.Code, Is.EqualTo(SaveCodes.Accepted));
        Assert.That(r.ServerTime, Is.EqualTo(now));
        Assert.That(r.Meta.RunId, Is.EqualTo("new-run"));
        Assert.That(store.Meta.RunId, Is.EqualTo("new-run"));
        Assert.That(store.Meta.Seq, Is.EqualTo(0));
        Assert.That(store.Meta.CityDeposited, Is.EqualTo(1234));
        Assert.That(store.Meta.CityWithdrawn, Is.EqualTo(55));
        Assert.That(store.Meta.StartedAt, Is.EqualTo(now));
        Assert.That(store.Meta.AcceptedAt, Is.EqualTo(T.T0), "timed from the old run's last accept");
        Assert.That(store.HasData, Is.False, "the old run's save is deleted");
    }

    [Test]
    public async Task NewRun_SameLiveRun_IsANoOp()
    {
        var store = new MemoryStore();
        SaveMeta meta = T.Meta(seq: 7, value: 3_000_000);
        store.Put(meta, Save(3_000_000, 10).Json());
        int writes = store.Writes;

        SaveResult r = await Service(store).NewRun(T.Run);
        Assert.That(r.Ok, Is.True);
        Assert.That(r.Meta.Seq, Is.EqualTo(7));
        Assert.That(store.Writes, Is.EqualTo(writes));
        Assert.That(store.HasData, Is.True);
    }

    [Test]
    public async Task NewRun_SameIdAfterGameOver_Refused()
    {
        // An ended run never comes back under its id (a replayed NewRun can't reset a finished company to a fresh start)
        var store = new MemoryStore();
        SaveMeta meta = T.Meta(seq: 7, value: 3_000_000);
        meta.Ended = true;
        store.Put(meta);
        int writes = store.Writes;
        SaveResult r = await Service(store).NewRun(T.Run, T.T0);
        Assert.That(r.Code, Is.EqualTo(SaveCodes.Ended), r.Message);
        Assert.That(r.Ok, Is.False);
        Assert.That(store.Writes, Is.EqualTo(writes));
        Assert.That(store.Meta.Ended, Is.True);
        Assert.That(store.Meta.Seq, Is.EqualTo(7));
        Assert.That(SaveRules.IsEndedRun(store.Meta, T.Run), Is.True);

        SaveResult other = await Service(store).NewRun("next-run", T.T0);
        Assert.That(other.Ok, Is.True, "a new id after Game Over is fine: " + other.Message);
    }

    [Test]
    public async Task NewRun_Superseded_WhenAnotherDeviceKeptPlaying()
    {
        // The server's live run was accepted at T0 + 500; this PC last synced at T0 and started a New Game offline
        var store = new MemoryStore();
        store.Put(T.Meta(run: "cloud-run", seq: 30, value: 8_000_000, acceptedAt: T.T0 + 500), Save(8_000_000, 10).Json());
        int writes = store.Writes;
        now = T.T0 + 900;

        SaveResult r = await Service(store).NewRun("offline-run", T.T0);
        Assert.That(r.Code, Is.EqualTo(SaveCodes.Superseded), r.Message);
        Assert.That(r.Ok, Is.False);
        Assert.That(r.Meta.RunId, Is.EqualTo("cloud-run"), "the client gets the server's company to adopt");
        Assert.That(store.Writes, Is.EqualTo(writes), "nothing changes");
        Assert.That(store.HasData, Is.True);
        Assert.That((await Service(store).NewRun("offline-run")).Code, Is.EqualTo(SaveCodes.Superseded), "left out = never synced: the strictest answer");

        // Up to date (it saw the latest accept), or the live run is over: the New Game goes ahead
        SaveResult upToDate = await Service(store).NewRun("offline-run", T.T0 + 500);
        Assert.That(upToDate.Ok, Is.True, upToDate.Message);
        Assert.That(store.Meta.RunId, Is.EqualTo("offline-run"));

        var endedStore = new MemoryStore();
        SaveMeta ended = T.Meta(run: "cloud-run", acceptedAt: T.T0 + 500);
        ended.Ended = true;
        endedStore.Put(ended);
        Assert.That((await Service(endedStore).NewRun("offline-run", 0)).Ok, Is.True, "an ended run is never superseding");
    }

    [Test]
    public async Task UnreadableMeta_NeverTreatedAsNoMeta()
    {
        foreach (string broken in new[] { "not json at all", "{\"seq\": 12, \"value\": 5000000}", "{\"run\": \"has space\", \"seq\": 3}" })
        {
            var store = new MemoryStore();
            store.MetaJson = broken;
            store.MetaVersion = 1;
            store.Data = Save(5_000_000, 10).Json();
            store.HasData = true;
            int writes = store.Writes;

            // A submit would otherwise be an import with no stale check
            SaveResult submit = await Service(store).Submit(Json(T.Req(1, 0, Save(50_000, 1))));
            Assert.That(submit.Code, Is.EqualTo(SaveCodes.Error), broken + ": " + submit.Message);
            Assert.That((await Service(store).EndRun(T.Run)).Code, Is.EqualTo(SaveCodes.Error), broken);
            Assert.That(store.Writes, Is.EqualTo(writes), broken + ": nothing written");
            Assert.That(SaveRules.EvaluateSubmit(new SaveMeta { Seq = 12 }, T.Req(1, 0, Save(50_000, 1)), T.City(), now).Code, Is.EqualTo(SaveCodes.Error));

            // NewRun replaces it, without a time baseline (the first save is checked like an import)
            SaveResult fresh = await Service(store).NewRun("new-run", 0);
            Assert.That(fresh.Ok, Is.True, broken + ": " + fresh.Message);
            Assert.That(store.Meta.RunId, Is.EqualTo("new-run"));
            Assert.That(store.Meta.AcceptedAt, Is.EqualTo(0));
            Assert.That(store.HasData, Is.False);
        }
    }

    [Test]
    public async Task ClientText_IsCutInLogsAndFlags()
    {
        var store = new MemoryStore();
        store.Put(T.Meta(seq: 5), Save(1_000_000, 10).Json());
        SubmitRequest request = T.Req(5, 5, Save(1_000_000, 11));
        request.Build = new string('b', 10_000) + "\n\u0007";
        await Service(store).Submit(Json(request));
        Assert.That(logs, Is.Not.Empty);
        Assert.That(logs.Max(l => l.Length), Is.LessThan(600), "a 10,000-character build string doesn't reach the log");

        Assert.That(SaveRules.ClientText(new string('x', 500)).Length, Is.EqualTo(SaveRules.MaxClientText + 3));
        Assert.That(SaveRules.ClientText("line\nbreak"), Is.EqualTo("line?break"));
        Assert.That(SaveRules.ClientText(null), Is.EqualTo("?"));

        // A ledger source is client text too: cut in the flag details
        store.Put(T.Meta(seq: 5), Save(1_000_000, 10).Json());
        now += 120;
        SubmitRequest noisy = T.Req(6, 5, Save(1_000_000, 11), T.Gamble(1, new string('s', 5000), null, 1000, 1e9, 1_000_000, 11));
        SaveResult r = await Service(store).Submit(Json(noisy));
        Assert.That(r.Ok, Is.True, r.Message);
        Assert.That(store.Meta.Flags.Max(f => f.Detail.Length), Is.LessThan(1000));
    }

    [Test]
    public async Task NewRun_Refusals()
    {
        var store = new MemoryStore();
        Assert.That((await Service(store).NewRun("bad id!")).Code, Is.EqualTo(SaveCodes.Invalid));

        store.FailCity = true;
        SaveResult r = await Service(store).NewRun("fine-id");
        Assert.That(r.Code, Is.EqualTo(SaveCodes.Error), "no city baseline, no new run (the client retries)");
        Assert.That(store.MetaJson, Is.Null);

        store.FailCity = false;
        store.Put(T.Meta(run: "old-run"));
        store.BeforeWrite = () => store.Put(T.Meta(run: "sneaky"));   // another request writes first
        SaveResult busy = await Service(store).NewRun("fine-id", T.T0);
        Assert.That(busy.Code, Is.EqualTo(SaveCodes.Busy));
        Assert.That(busy.Meta.RunId, Is.EqualTo("sneaky"), "busy hands back the meta as it is now");
    }

    [Test]
    public async Task Submit_Accepted_WritesSaveAndMeta()
    {
        var store = new MemoryStore();
        store.Put(SaveRules.NewRunMeta(T.Run, now, T.City()));
        now += 60;
        SubmitRequest request = T.Req(1, 0, Save(52_000, 3));
        SaveResult r = await Service(store).Submit(Json(request));

        Assert.That(r.Ok, Is.True, r.Message);
        Assert.That(r.Meta.Seq, Is.EqualTo(1));
        Assert.That(store.Meta.Seq, Is.EqualTo(1));
        Assert.That(store.Meta.AcceptedAt, Is.EqualTo(now));
        Assert.That(SavePacking.IsPacked(store.Data), Is.True, "save_data is stored packed");
        Assert.That(SavePacking.Unpack(store.Data), Is.EqualTo(request.SaveJson), "...and unpacks to the save text exactly as sent");
        Assert.That(r.NewFlags, Is.Empty);
    }

    [Test]
    public async Task Submit_Rejected_WritesNothing()
    {
        var store = new MemoryStore();
        store.Put(T.Meta(seq: 5), Save(1_000_000, 10).Json());
        int writes = store.Writes;
        SaveResult r = await Service(store).Submit(Json(T.Req(5, 5, Save(1_000_000, 11))));
        Assert.That(r.Code, Is.EqualTo(SaveCodes.Stale));
        Assert.That(r.Ok, Is.False);
        Assert.That(r.Meta.Seq, Is.EqualTo(5));
        Assert.That(store.Writes, Is.EqualTo(writes));
        Assert.That(logs.Any(l => l.Contains("stale")), Is.True);
    }

    [Test]
    public async Task Submit_Garbage_IsInvalid()
    {
        var store = new MemoryStore();
        store.Put(T.Meta());
        Assert.That((await Service(store).Submit("{{{")).Code, Is.EqualTo(SaveCodes.Invalid));
        Assert.That((await Service(store).Submit(null)).Code, Is.EqualTo(SaveCodes.Invalid));
    }

    [Test]
    public async Task Submit_LockConflict_IsBusy()
    {
        var store = new MemoryStore();
        store.Put(T.Meta(), Save(1_000_000, 10).Json());
        store.BeforeWrite = () => store.Put(T.Meta(seq: 9));   // another device's save lands first
        now += 120;
        SaveResult r = await Service(store).Submit(Json(T.Req(2, 1, Save(1_050_000, 12))));
        Assert.That(r.Code, Is.EqualTo(SaveCodes.Busy));
        Assert.That(store.Meta.Seq, Is.EqualTo(9), "the other write wasn't overwritten");
    }

    [Test]
    public async Task Submit_Import_NeedsTheCityBaseline()
    {
        var store = new MemoryStore { FailCity = true };
        SaveResult r = await Service(store).Submit(Json(T.Req(4, 0, Save(80_000, 6))));
        Assert.That(r.Code, Is.EqualTo(SaveCodes.Error));
        Assert.That(store.MetaJson, Is.Null);

        store.FailCity = false;
        store.City = T.City(10, 20);
        SaveResult ok = await Service(store).Submit(Json(T.Req(4, 0, Save(80_000, 6))));
        Assert.That(ok.Ok, Is.True, ok.Message);
        Assert.That(store.Meta.CityDeposited, Is.EqualTo(10));
        Assert.That(store.Meta.StartedAt, Is.EqualTo(now));
    }

    [Test]
    public async Task Submit_CityUnreadable_KeepsTheChainGoing()
    {
        var store = new MemoryStore { FailCity = true };
        store.Put(T.Meta(cityDep: 5, cityWd: 6), Save(1_000_000, 10).Json());
        now += 120;
        SaveResult r = await Service(store).Submit(Json(T.Req(2, 1, Save(1_050_000, 12))));
        Assert.That(r.Ok, Is.True, r.Message);
        Assert.That(store.Meta.CityDeposited, Is.EqualTo(5), "the old baseline stays");
        Assert.That(store.Meta.CityWithdrawn, Is.EqualTo(6));
        Assert.That(logs.Any(l => l.Contains("Online City")), Is.True);
    }

    [Test]
    public async Task Submit_UsesThePreviousSave()
    {
        // The stored save_data counts: 200 strong employees earned before 190 were let go
        List<SaveEmployeeInfo> staff = T.Staff(200, iq: 140, gpa: 3.6, exp: 20, phd: true);
        var store = new MemoryStore();
        store.Put(T.Meta(value: 10_000_000), new SaveBuilder { Value = 10_000_000, Day = 10, Employees = staff, MeetingsHeld = 0 }.Json());
        now += 120;
        var after = new SaveBuilder { Value = 50_000_000, Day = 14, Employees = staff.Take(10).ToList(), MeetingsHeld = 0 };
        SaveResult r = await Service(store).Submit(Json(T.Req(2, 1, after)));
        Assert.That(r.Ok, Is.True, r.Message);
        Assert.That(r.Meta.Flagged, Is.False, string.Join(" | ", logs));
    }

    [Test]
    public async Task Submit_FlagsComeBackInTheResult()
    {
        var store = new MemoryStore();
        store.Put(T.Meta(), Save(1_000_000, 10).Json());
        now += 120;
        SaveResult r = await Service(store).Submit(Json(T.Req(2, 1, Save(90_000_000, 12))));
        Assert.That(r.Ok, Is.True);
        Assert.That(r.NewFlags.Single().Reason, Is.EqualTo(SaveCodes.FlagGrowth));
        Assert.That(store.Meta.Flagged, Is.True);
        Assert.That(logs.Any(l => l.Contains("findings")), Is.True);

        // The result travels as JSON with the contract's names
        string json = JsonConvert.SerializeObject(r);
        Assert.That(json, Does.Contain("\"code\":\"accepted\"").And.Contain("\"flags\":[{\"reason\":\"growth\""));
    }

    [Test]
    public async Task EndRun_OnlyTheCurrentRun()
    {
        var store = new MemoryStore();
        store.Put(T.Meta(seq: 4), Save(1_000_000, 10).Json());

        SaveResult other = await Service(store).EndRun("another-run");
        Assert.That(other.Code, Is.EqualTo(SaveCodes.NoRun));
        Assert.That(store.Meta.Ended, Is.False);
        Assert.That(store.HasData, Is.True);

        SaveResult ended = await Service(store).EndRun(T.Run, baseSeq: 4);
        Assert.That(ended.Ok, Is.True, ended.Message);
        Assert.That(ended.Meta.Ended, Is.True);
        Assert.That(store.Meta.Ended, Is.True);
        Assert.That(store.HasData, Is.False, "a finished company's save is gone");

        SaveResult again = await Service(store).EndRun(T.Run);
        Assert.That(again.Ok, Is.True, "ending an ended run is fine, whatever the copy knows");

        SaveResult submit = await Service(store).Submit(Json(T.Req(5, 4, Save(1_000_000, 11))));
        Assert.That(submit.Code, Is.EqualTo(SaveCodes.Ended));

        Assert.That((await Service(new MemoryStore()).EndRun(T.Run)).Code, Is.EqualTo(SaveCodes.NoRun), "no meta at all");
    }

    [Test]
    public async Task FullRun_NewRunSubmitsEndRun()
    {
        var store = new MemoryStore { City = T.City(0, 0) };
        GameSaveService service = Service(store);
        Assert.That((await service.NewRun("run-a")).Ok, Is.True);

        double value = 50_000;
        long seq = 0;
        int day = 1;
        for (int i = 0; i < 20; i++)
        {
            now += 120;
            day += 4;
            value *= 1.08;
            SaveResult r = await service.Submit(Json(T.Req("run-a", seq + 1, seq, Save(value, day))));
            Assert.That(r.Ok, Is.True, r.Message);
            Assert.That(r.Meta.Flagged, Is.False, r.Message);
            seq = r.Meta.Seq;
        }
        Assert.That(store.Meta.Seq, Is.EqualTo(20));
        Assert.That((await service.EndRun("run-a", baseSeq: seq)).Ok, Is.True);
        Assert.That((await service.NewRun("run-b")).Ok, Is.True);
        Assert.That(store.Meta.RunId, Is.EqualTo("run-b"));
        Assert.That(store.Meta.Flags, Is.Empty, "a new run starts with clean flags");
    }

    // ================================================================================================================
    // EndRun: only an up-to-date copy ends the run (SaveCloud.FnEndRun)
    // ================================================================================================================

    /// <summary>The server at save 20 of the run, save_data stored packed as the client sent it.</summary>
    static (MemoryStore Store, string SaveJson) RunAtSeq20()
    {
        var store = new MemoryStore();
        string json = Save(4_000_000, 60).Json();
        store.Put(T.Meta(seq: 20, value: 4_000_000, day: 60), SavePacking.Pack(json));
        return (store, json);
    }

    [Test]
    public async Task EndRun_UpToDateCopy_Ends()
    {
        var (store, _) = RunAtSeq20();
        SaveResult r = await Service(store).EndRun(T.Run, 20, -1, null);
        Assert.That(r.Code, Is.EqualTo(SaveCodes.Accepted), r.Message);
        Assert.That(store.Meta.Ended, Is.True);
        Assert.That(store.HasData, Is.False);
    }

    [Test]
    public async Task EndRun_TheUploadWhoseAnswerWasLost_Ends()
    {
        // The copy continued from 19 and uploaded 20, but the answer never came: the stored save is that upload
        var (store, json) = RunAtSeq20();
        SaveResult r = await Service(store).EndRun(T.Run, 19, 20, SavePacking.Hash(json));
        Assert.That(r.Code, Is.EqualTo(SaveCodes.Accepted), r.Message);
        Assert.That(store.Meta.Ended, Is.True);

        // The hash is of the unpacked text, as SavePacking.Hash gives it (upper case is fine too)
        var (other, otherJson) = RunAtSeq20();
        Assert.That((await Service(other).EndRun(T.Run, 19, 20, SavePacking.Hash(otherJson).ToUpperInvariant())).Ok, Is.True);
    }

    [Test]
    public async Task EndRun_FromAnOutOfDateCopy_Refused()
    {
        // The reviewer's case: the server is at save 20, an offline copy that continued from 10 reaches Game Over
        var (store, _) = RunAtSeq20();
        int writes = store.Writes;
        SaveResult r = await Service(store).EndRun(T.Run, 10, 11, SavePacking.Hash("{\"its own save\":1}"));
        Assert.That(r.Code, Is.EqualTo(SaveCodes.Stale), r.Message);
        Assert.That(r.Ok, Is.False);
        Assert.That(r.Meta.Seq, Is.EqualTo(20), "the answer carries the server's meta");
        Assert.That(r.Meta.Ended, Is.False);
        Assert.That(store.Meta.Ended, Is.False, "the run goes on");
        Assert.That(store.HasData, Is.True, "save_data is kept");
        Assert.That(store.Writes, Is.EqualTo(writes));
        Assert.That(logs.Any(l => l.Contains("stale")), Is.True);

        // Left-out parameters are the strictest: a live run doesn't end
        Assert.That((await Service(store).EndRun(T.Run)).Code, Is.EqualTo(SaveCodes.Stale));
        Assert.That(store.Meta.Ended, Is.False);
    }

    [Test]
    public async Task EndRun_InFlightHashMismatch_Refused()
    {
        // Same seq, different save: another device's upload 20, not this copy's
        var (store, json) = RunAtSeq20();
        Assert.That((await Service(store).EndRun(T.Run, 19, 20, SavePacking.Hash(json + " "))).Code, Is.EqualTo(SaveCodes.Stale));
        Assert.That((await Service(store).EndRun(T.Run, 19, 21, SavePacking.Hash(json))).Code, Is.EqualTo(SaveCodes.Stale), "another seq");
        Assert.That((await Service(store).EndRun(T.Run, 19, 20, "")).Code, Is.EqualTo(SaveCodes.Stale), "no hash");
        Assert.That(store.Meta.Ended, Is.False);

        // No stored save to compare with: nothing proves the copy is up to date
        store.HasData = false;
        store.Data = null;
        Assert.That((await Service(store).EndRun(T.Run, 19, 20, SavePacking.Hash(json))).Code, Is.EqualTo(SaveCodes.Stale));
    }
}
