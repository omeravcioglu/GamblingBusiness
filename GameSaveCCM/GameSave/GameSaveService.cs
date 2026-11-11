using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;

/// <summary>What Cloud Save holds for the player: save_meta and save_data with their write locks.</summary>
public class StoredSave
{
    /// <summary>null when the player has no meta (or it can't be read: MetaUnreadable).</summary>
    public SaveMeta Meta;
    /// <summary>
    /// save_meta exists but can't be read (not JSON, or no valid run id). Never "no meta": a submit would become an import
    /// without a stale check. Submits and EndRun answer error; NewRun replaces it without a time baseline.
    /// </summary>
    public bool MetaUnreadable;
    /// <summary>Write lock of save_meta (null when the item doesn't exist).</summary>
    public string MetaLock;
    public bool HasData;
    /// <summary>save_data as stored: packed (SavePacking), or plain JSON written before packing. Read it with SavePacking.Unpack.</summary>
    public string DataJson;
    public string DataLock;
}

/// <summary>
/// The player's Online City accounts as the server read them, all from one read: lifetime totals, the recent stamps and
/// the applied op ids (what SaveRules.ClassifyPending checks a save's pending transfers against).
/// </summary>
public class StoredCity
{
    public CityTotals Totals = new CityTotals { Known = true };
    public List<SaveCityTransfer> Transfers = new List<SaveCityTransfer>();
    /// <summary>Op ids the accounts applied (CityAccount ops).</summary>
    public List<string> Ops = new List<string>();
    /// <summary>Whose accounts these are (the transfers in a save name the player that started them).</summary>
    public string PlayerId;
}

/// <summary>Someone else wrote the item since it was read (Cloud Save write lock, HTTP 409).</summary>
public class SaveConflictException : Exception
{
    public SaveConflictException() : base("The save changed while it was being written.") { }
}

/// <summary>Where the save lives (Cloud Save in the module, memory in the tests).</summary>
public interface ISaveStore
{
    Task<StoredSave> LoadSave();
    /// <summary>Lifetime deposit / withdrawal totals over every city account, and their recent stamps. Throws when it can't read them.</summary>
    Task<StoredCity> LoadCity();
    /// <summary>
    /// save_data (the packed save text) and save_meta together (all or nothing), each only if its write lock still matches
    /// (null = create / overwrite).
    /// </summary>
    Task WriteSaveAndMeta(string storedSave, string dataLock, SaveMeta meta, string metaLock);
    Task WriteMeta(SaveMeta meta, string metaLock);
    /// <summary>Delete save_data (nothing to do when it's gone).</summary>
    Task DeleteData(string dataLock);
}

/// <summary>
/// The GameSave endpoints over a store: SaveRules decides, this reads and writes. Every call returns a SaveResult whose
/// Meta is the stored meta after the call (null when the player has none).
/// - SubmitSave: load meta + data (+ locks) and the city totals, SaveRules.EvaluateSubmit (which unpacks the request's
///   SaveGz), and on accept write the save (packed: SubmitDecision.StoredSave) and the meta in one batch with both write
///   locks. A lock conflict answers busy (the client retries).
/// - NewRun(runId, knownAcceptedAt): a no-op for the live run it names; ended for a run id that is over (it never comes
///   back); superseded when the server's live run (another one) was accepted after knownAcceptedAt, the AcceptedAt this
///   client last saw (another device kept playing: this New Game mustn't wipe it). Otherwise a fresh meta (city totals as
///   the money baseline; the previous meta's AcceptedAt as the time baseline, or none for a player without a readable meta:
///   SaveRules.NewRunMeta), then the old save_data is deleted (the new meta says seq 0, so a leftover save_data is never
///   used). Right after NewRun the meta's AcceptedAt is 0 or old, so the Online City's freshness rule makes deposits wait
///   for the run's first accepted save.
/// - EndRun(runId, baseSeq, inFlightSeq, inFlightHash): only for the current run, and only from a copy that is up to date
///   with the server (SaveRules.IsUpToDateForEnd): meta.Ended = true, then save_data is deleted. A copy another device got
///   ahead of: stale, nothing changes. Another run: no_run. An ended run: accepted again (nothing to protect).
/// - An unreadable save_meta (StoredSave.MetaUnreadable) answers error to SubmitSave and EndRun (the client keeps its save);
///   NewRun replaces it.
/// </summary>
public class GameSaveService
{
    readonly ISaveStore store;
    readonly Func<long> clock;
    readonly Action<string> log;

    public GameSaveService(ISaveStore store, Func<long> clock = null, Action<string> log = null)
    {
        this.store = store;
        this.clock = clock ?? SaveCloud.UnixNow;
        this.log = log ?? (_ => { });
    }

    public async Task<SaveResult> Submit(string requestJson)
    {
        long now = clock();
        StoredSave stored = await store.LoadSave();
        if (Unreadable(stored))
        {
            log("SubmitSave: the stored save_meta can't be read; nothing changed.");
            return Result(SaveCodes.Error, "The server's save meta for this player can't be read; try again later.", null, now);
        }

        SubmitRequest request = null;
        try
        {
            request = string.IsNullOrEmpty(requestJson) ? null : JsonConvert.DeserializeObject<SubmitRequest>(requestJson);
        }
        catch (Exception)
        {
            request = null;
        }
        if (request == null) return Result(SaveCodes.Invalid, "The request isn't readable.", stored.Meta, now);

        // One read of the city accounts: the totals, the stamps and the applied ops the save's pending transfers are
        // classified against all come from it (and become the next baseline together)
        CityTotals city;
        StoredCity loaded = null;
        try
        {
            loaded = await store.LoadCity();
            city = loaded.Totals ?? new CityTotals();
        }
        catch (Exception e)
        {
            log("Couldn't read the Online City accounts, city money is left out of this save's checks: " + e.Message);
            city = new CityTotals { Known = false };
        }

        // An imported run's first save sets the city baseline: without it, every older transfer would count as new later
        if (stored.Meta == null && !city.Known)
            return Result(SaveCodes.Error, "The Online City accounts couldn't be read; try again.", null, now);

        var context = new SaveSubmitContext
        {
            PreviousSaveJson = stored.Meta != null && stored.Meta.RunId == request.RunId ? stored.DataJson : null,
            CityTransfers = loaded?.Transfers ?? new List<SaveCityTransfer>(),
            CityOps = loaded?.Ops ?? new List<string>(),
            PlayerId = loaded?.PlayerId
        };
        SubmitDecision decision = SaveRules.EvaluateSubmit(stored.Meta, request, city, now, context);
        string who = "run " + SaveRules.ClientText(request.RunId) + " seq " + request.Seq + " day " + request.Day + (request.Dev ? " (dev)" : "") +
                     (string.IsNullOrEmpty(request.Build) ? "" : " build " + SaveRules.ClientText(request.Build));
        if (!decision.Accepted)
        {
            log("SubmitSave " + decision.Code + " for " + who + ": " + decision.Message);
            return Result(decision.Code, decision.Message, decision.Meta, now);
        }

        try
        {
            await store.WriteSaveAndMeta(decision.StoredSave, stored.DataLock, decision.Meta, stored.MetaLock);
        }
        catch (SaveConflictException)
        {
            return await Busy(stored, now);
        }

        if (decision.Findings.Count > 0)
            log("SubmitSave accepted with findings for " + who + ": " + string.Join(" | ", decision.Findings));
        SaveResult result = Result(SaveCodes.Accepted, decision.Message, decision.Meta, now);
        result.NewFlags = decision.NewFlags;
        return result;
    }

    /// <param name="knownAcceptedAt">The meta.AcceptedAt this client last saw from the server (0 = it never synced).</param>
    public async Task<SaveResult> NewRun(string runId, long knownAcceptedAt = 0)
    {
        long now = clock();
        StoredSave stored = await store.LoadSave();
        if (!SaveRules.IsValidRunId(runId))
            return Result(SaveCodes.Invalid, "The run id is missing or malformed.", stored.Meta, now);

        // An unreadable meta is replaced (without a time baseline): otherwise the player could never save again
        bool unreadable = Unreadable(stored);
        SaveMeta current = unreadable ? null : stored.Meta;
        if (unreadable) log("NewRun " + runId + ": the stored save_meta can't be read; it is replaced.");

        if (SaveRules.IsLiveRun(current, runId))
            return Result(SaveCodes.Accepted, "Run " + runId + " is already the current run.", current, now);
        if (SaveRules.IsEndedRun(current, runId))
            return Result(SaveCodes.Ended, "Run " + runId + " is over (Game Over); a new company needs a new run id.", current, now);
        if (SaveRules.IsSuperseded(current, runId, knownAcceptedAt))
        {
            log("NewRun " + runId + " superseded: run " + current.RunId + " was accepted at " + current.AcceptedAt + ", this device last saw " + knownAcceptedAt + ".");
            return Result(SaveCodes.Superseded, "Run " + current.RunId + " got new progress after this device last synced; keep the server's company.", current, now);
        }

        StoredCity city;
        try
        {
            city = await store.LoadCity();
        }
        catch (Exception e)
        {
            // The new run's city baseline must be right, or its first saves would count old transfers as new
            log("NewRun " + runId + ": couldn't read the Online City accounts: " + e.Message);
            return Result(SaveCodes.Error, "The Online City accounts couldn't be read; try again.", current, now);
        }

        // Timed from the last accept of any earlier run (a New Game may have started offline since), or no baseline at all
        SaveMeta meta = SaveRules.NewRunMeta(runId, now, city.Totals, current);
        try
        {
            await store.WriteMeta(meta, stored.MetaLock);
        }
        catch (SaveConflictException)
        {
            return await Busy(stored, now);
        }
        await DeleteOldData(stored, "NewRun " + runId);
        return Result(SaveCodes.Accepted, "Run " + runId + " started.", meta, now);
    }

    /// <param name="baseSeq">The server seq the ending copy's chain continues from (-1 = unknown: the strictest).</param>
    /// <param name="inFlightSeq">The seq of that copy's upload whose answer never came (-1 = none).</param>
    /// <param name="inFlightHash">SavePacking.Hash of that upload's unpacked save text (null = none).</param>
    public async Task<SaveResult> EndRun(string runId, long baseSeq = -1, long inFlightSeq = -1, string inFlightHash = null)
    {
        long now = clock();
        StoredSave stored = await store.LoadSave();
        if (!SaveRules.IsValidRunId(runId))
            return Result(SaveCodes.Invalid, "The run id is missing or malformed.", stored.Meta, now);
        if (Unreadable(stored))
        {
            log("EndRun " + runId + ": the stored save_meta can't be read; nothing changed.");
            return Result(SaveCodes.Error, "The server's save meta for this player can't be read; try again later.", null, now);
        }
        if (stored.Meta == null || stored.Meta.RunId != runId)
            return Result(SaveCodes.NoRun, "Run " + runId + " isn't the server's current run; nothing changed.", stored.Meta, now);
        if (stored.Meta.Ended)
        {
            await DeleteOldData(stored, "EndRun " + runId);
            return Result(SaveCodes.Accepted, "Run " + runId + " was already over.", stored.Meta, now);
        }

        // Only a copy that is up to date with the server ends the run: another device may have moved the company on since
        if (!SaveRules.IsUpToDateForEnd(stored.Meta, baseSeq, inFlightSeq, inFlightHash, stored.HasData ? stored.DataJson : null))
        {
            log("EndRun " + runId + " stale: the server has save " + stored.Meta.Seq + "; this copy continued from " + baseSeq +
                (inFlightSeq >= 0 ? " with upload " + inFlightSeq + " unanswered" : "") + ". The run goes on.");
            return Result(SaveCodes.Stale, "Another device moved run " + runId + " on: the server has save " + stored.Meta.Seq +
                                           ", this copy continued from " + baseSeq + ". The run goes on.", stored.Meta, now);
        }

        SaveMeta meta = SaveRules.EndRunMeta(stored.Meta, runId, now);
        try
        {
            await store.WriteMeta(meta, stored.MetaLock);
        }
        catch (SaveConflictException)
        {
            return await Busy(stored, now);
        }
        await DeleteOldData(stored, "EndRun " + runId);
        return Result(SaveCodes.Accepted, "Run " + runId + " is over.", meta, now);
    }

    /// <summary>save_meta exists but isn't usable: not JSON, or without a valid run id.</summary>
    static bool Unreadable(StoredSave stored)
    {
        return stored.MetaUnreadable || SaveRules.IsUnreadableMeta(stored.Meta);
    }

    /// <summary>The meta is written already: a save_data that won't go away is only noise (seq 0 / ended say not to use it).</summary>
    async Task DeleteOldData(StoredSave stored, string operation)
    {
        if (!stored.HasData) return;
        try
        {
            await store.DeleteData(stored.DataLock);
        }
        catch (Exception e)
        {
            log(operation + ": couldn't delete save_data: " + e.Message);
        }
    }

    async Task<SaveResult> Busy(StoredSave stored, long now)
    {
        SaveMeta meta = stored.Meta;
        try
        {
            meta = (await store.LoadSave()).Meta;
        }
        catch (Exception)
        {
            // Keep the meta read before
        }
        return Result(SaveCodes.Busy, "Someone else saved at the same moment; try again.", meta, now);
    }

    static SaveResult Result(string code, string message, SaveMeta meta, long now)
    {
        return SaveResult.From(code, message, meta, now);
    }
}
