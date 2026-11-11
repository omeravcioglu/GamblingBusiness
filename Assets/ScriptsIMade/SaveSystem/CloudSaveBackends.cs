using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Services.Authentication;
using Unity.Services.CloudCode;
using Unity.Services.CloudSave;
using Unity.Services.CloudSave.Models.Data.Player;
using UnityEngine;

/// <summary>
/// What the server holds for this player: the meta (null = nothing yet) and save_data. The backend fills Meta and RawData;
/// CloudSaveSync.PrepareSnapshot (off the main thread) unpacks, parses and fingerprints the save.
/// </summary>
public sealed class CloudSnapshot
{
    public SaveMeta Meta;
    /// <summary>save_data as returned: a JSON string value holding packed text (from before packing: the JSON itself).</summary>
    public string RawData;
    /// <summary>The save's JSON text, unpacked, when it belongs to the meta's run (null otherwise).</summary>
    public string SaveJson;
    /// <summary>SaveJson parsed (null when SaveJson is).</summary>
    public SaveData Data;
    /// <summary>SavePacking.Hash of SaveJson (null when SaveJson is).</summary>
    public string SaveHash;
    /// <summary>save_data exists but couldn't be read (broken packing or JSON): never a reason to drop the local save.</summary>
    public bool DataBroken;
}

/// <summary>
/// The player's Online City money from one read of every city account (protected acct_* items), as the GameSave module
/// reads it (UgsSaveStore.LoadCity): the lifetime totals SaveMeta.CityDeposited / CityWithdrawn come from, every deps / wds
/// stamp and every applied op id - what SaveRules.ClassifyPending checks a save's pending transfers against.
/// </summary>
public sealed class CityMoney
{
    /// <summary>False when this server has no city data (the local test server): city money is left alone.</summary>
    public bool Known;
    public long Deposited;
    public long Withdrawn;
    public int Accounts;
    /// <summary>Whose accounts these are: the player the client stamps on its transfers (PlayerAccount.PlayerId).</summary>
    public string PlayerId;
    /// <summary>Every deps / wds stamp of the read (SaveCityTransfer: op id, the city's time, amount).</summary>
    public List<SaveCityTransfer> Transfers = new List<SaveCityTransfer>();
    /// <summary>Every op id the accounts applied (CityAccount ops: they go back further than the stamps).</summary>
    public List<string> Ops = new List<string>();

    /// <summary>A CityAccount deps / wds array, read exactly as the GameSave module reads it (UgsSaveStore.AddStamps).</summary>
    public void AddStamps(JArray stamps, bool deposit)
    {
        if (stamps == null) return;
        foreach (JToken token in stamps)
        {
            if (!(token is JObject stamp)) continue;
            long amount = Long(stamp["a"]);
            if (amount <= 0) continue;
            string opId = stamp["id"] != null && stamp["id"].Type == JTokenType.String ? stamp["id"].Value<string>() : null;
            Transfers.Add(new SaveCityTransfer { OpId = opId, Time = Long(stamp["t"]), Amount = amount, Deposit = deposit });
        }
    }

    /// <summary>A JSON number as the GameSave module reads it (UgsSaveStore.Long); anything else is 0.</summary>
    public static long Long(JToken token)
    {
        if (!SaveSnapshot.TryNumber(token, out double value) || double.IsNaN(value)) return 0L;
        if (value >= long.MaxValue) return long.MaxValue;
        if (value <= long.MinValue) return long.MinValue;
        return (long)value;
    }
}

/// <summary>Where CloudSaveSync's requests go: the GameSave module on Unity Gaming Services, or the local test server.</summary>
public interface ICloudSaveBackend
{
    /// <summary>For logs: "UGS" or "local test server".</summary>
    string Name { get; }
    bool IsLocalTest { get; }
    /// <summary>The player the requests act for (null while nobody is signed in).</summary>
    string PlayerId { get; }
    /// <summary>Requests can be made now without waiting for a sign-in.</summary>
    bool CanCall { get; }
    /// <summary>Sign in if needed; false when it can't (offline). Never throws.</summary>
    Task<bool> EnsureReady();
    /// <summary>The player's save_meta and save_data. Throws on network errors.</summary>
    Task<CloudSnapshot> Load();
    /// <summary>
    /// SubmitSave with the request and its JSON (already serialized and measured off the main thread). Throws on network
    /// errors; the server's answer otherwise (null for an empty answer).
    /// </summary>
    Task<SaveResult> Submit(SubmitRequest request, string requestJson);
    /// <summary>NewRun; knownAcceptedAt = the newest server AcceptedAt this client saw when the company started (superseded if older).</summary>
    Task<SaveResult> NewRun(string runId, long knownAcceptedAt);
    /// <summary>EndRun with what shows whether the ending copy is up to date (baseSeq -1 = unknown; inFlightSeq -1 = none).</summary>
    Task<SaveResult> EndRun(string runId, long baseSeq, long inFlightSeq, string inFlightHash);
    /// <summary>
    /// The player's Online City money (Known = false without city data). Throws when the accounts can't be loaded; an item
    /// that can't be read counts nothing, as on the server.
    /// </summary>
    Task<CityMoney> LoadCityMoney();
}

/// <summary>
/// The real server: the GameSave Cloud Code module writes (players can't write Cloud Save), and the player's protected
/// items save_meta / save_data are read directly.
/// </summary>
public sealed class UgsSaveBackend : ICloudSaveBackend
{
    public string Name => "UGS (GameSave module)";
    public bool IsLocalTest => false;

    // Release builds: only a Steam session carries the cloud save (PlayerAccount.CanUseCloudSave)
    public string PlayerId => PlayerAccount.CanUseCloudSave ? PlayerAccount.PlayerId : null;

    public bool CanCall => PlayerAccount.CanUseCloudSave;

    public async Task<bool> EnsureReady()
    {
        return await PlayerAccount.SignInAsync() && PlayerAccount.CanUseCloudSave;
    }

    public async Task<CloudSnapshot> Load()
    {
        var keys = new HashSet<string> { SaveCloud.MetaKey, SaveCloud.DataKey };
        Dictionary<string, Unity.Services.CloudSave.Models.Item> items =
            await CloudSaveService.Instance.Data.Player.LoadAsync(keys, new LoadOptions(new ProtectedReadAccessClassOptions()));

        var snapshot = new CloudSnapshot();
        if (items != null && items.TryGetValue(SaveCloud.MetaKey, out var metaItem) && metaItem?.Value != null)
        {
            // Read through our own Newtonsoft settings: fields a newer server adds are ignored
            string metaJson = metaItem.Value.GetAsString();
            if (!string.IsNullOrEmpty(metaJson))
                snapshot.Meta = JsonConvert.DeserializeObject<SaveMeta>(metaJson);
        }
        // A JSON string value holding packed text: unpacked and parsed off the main thread (CloudSaveSync.PrepareSnapshot)
        if (items != null && items.TryGetValue(SaveCloud.DataKey, out var dataItem) && dataItem?.Value != null)
            snapshot.RawData = dataItem.Value.GetAsString();
        return snapshot;
    }

    public Task<SaveResult> Submit(SubmitRequest request, string requestJson)
    {
        return Call(SaveCloud.FnSubmit, new Dictionary<string, object> { { "request", requestJson ?? JsonConvert.SerializeObject(request) } });
    }

    public Task<SaveResult> NewRun(string runId, long knownAcceptedAt)
    {
        return Call(SaveCloud.FnNewRun, new Dictionary<string, object> { { "runId", runId }, { "knownAcceptedAt", knownAcceptedAt } });
    }

    public Task<SaveResult> EndRun(string runId, long baseSeq, long inFlightSeq, string inFlightHash)
    {
        return Call(SaveCloud.FnEndRun, new Dictionary<string, object>
        {
            { "runId", runId }, { "baseSeq", baseSeq }, { "inFlightSeq", inFlightSeq }, { "inFlightHash", inFlightHash }
        });
    }

    /// <summary>
    /// Every protected acct_* item of the player (CityAccount), read item by item as the GameSave module reads them
    /// (UgsSaveStore.LoadCity: stats.dep / stats.wd summed, the deps / wds stamps and the ops listed, an item that isn't an
    /// account object skipped). Throws when the keys or the items can't be loaded.
    /// </summary>
    public async Task<CityMoney> LoadCityMoney()
    {
        var money = new CityMoney { Known = true, PlayerId = PlayerAccount.PlayerId };
        List<Unity.Services.CloudSave.Models.ItemKey> keys =
            await CloudSaveService.Instance.Data.Player.ListAllKeysAsync(new ListAllKeysOptions(new ProtectedReadAccessClassOptions()));
        var accountKeys = new List<string>();
        if (keys != null)
        {
            foreach (Unity.Services.CloudSave.Models.ItemKey key in keys)
            {
                if (key != null && !string.IsNullOrEmpty(key.Key) && key.Key.StartsWith(SaveCloud.CityAccountPrefix, StringComparison.Ordinal))
                    accountKeys.Add(key.Key);
            }
        }
        for (int start = 0; start < accountKeys.Count; start += 20)
        {
            var batch = new HashSet<string>(accountKeys.GetRange(start, Math.Min(20, accountKeys.Count - start)));
            Dictionary<string, Unity.Services.CloudSave.Models.Item> items =
                await CloudSaveService.Instance.Data.Player.LoadAsync(batch, new LoadOptions(new ProtectedReadAccessClassOptions()));
            if (items == null) continue;
            foreach (KeyValuePair<string, Unity.Services.CloudSave.Models.Item> pair in items)
            {
                // As the module reads it: an object (or a JSON string holding one); anything else counts nothing
                JObject account;
                try
                {
                    account = SaveSnapshot.ParseObject(pair.Value?.Value?.GetAsString());
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[CloudSave] The city account item " + pair.Key + " couldn't be read (" + e.Message + "): it counts nothing, as on the server.");
                    continue;
                }
                if (account == null) continue;
                money.Accounts++;
                if (account["stats"] is JObject stats)
                {
                    money.Deposited += CityMoney.Long(stats["dep"]);
                    money.Withdrawn += CityMoney.Long(stats["wd"]);
                }
                money.AddStamps(account["deps"] as JArray, true);
                money.AddStamps(account["wds"] as JArray, false);
                if (account["ops"] is JArray ops)
                {
                    foreach (JToken op in ops)
                    {
                        if (op != null && op.Type == JTokenType.String) money.Ops.Add(op.Value<string>());
                    }
                }
            }
        }
        return money;
    }

    static async Task<SaveResult> Call(string function, Dictionary<string, object> args)
    {
        string json = await CloudCodeService.Instance.CallModuleEndpointAsync<string>(SaveCloud.ModuleName, function, args);
        return string.IsNullOrEmpty(json) ? null : JsonConvert.DeserializeObject<SaveResult>(json);
    }
}

/// <summary>
/// The Editor's local test server (Tools > Game > Cloud Save > Use Local Test Server): the same SaveRules the GameSave
/// module runs, in-process, against one JSON file per player id in persistentDataPath/cloudtest/. City totals are
/// unknown here (CityTotals.Known = false), so city money is left out of the checks. Requests and answers go through
/// JSON like the real ones; like the module it unpacks SubmitRequest.SaveGz before the rules, stores the save packed, gives
/// a new run the previous meta's AcceptedAt (SaveRules.NewRunMeta; none without one: the run's first save then counts like
/// an import), and answers NewRun superseded when a live other run was accepted after the client's knownAcceptedAt.
/// </summary>
public sealed class LocalTestSaveBackend : ICloudSaveBackend
{
    /// <summary>The player id used while nobody is signed in.</summary>
    public const string OfflinePlayerId = "local_player";

    class Record
    {
        [JsonProperty("meta")] public SaveMeta Meta;
        [JsonProperty("data")] public string Data;
    }

    public string Name => "local test server";
    public bool IsLocalTest => true;

    public string PlayerId => PlayerAccount.IsSignedIn && !string.IsNullOrEmpty(PlayerAccount.PlayerId) ? PlayerAccount.PlayerId : OfflinePlayerId;

    // Once the first sign-in attempt is over (either way): the player id is settled then
    public bool CanCall => !PlayerAccount.IsSigningIn && PlayerAccount.State != PlayerAccountState.NotSignedIn;

    public static string Folder => Path.Combine(Application.persistentDataPath, "cloudtest");

    public static string FileFor(string playerId)
    {
        return Path.Combine(Folder, CloudSyncState.SafeFilePart(playerId) + ".json");
    }

    public async Task<bool> EnsureReady()
    {
        await PlayerAccount.SignInAsync(); // settles the player id; the local server works either way
        return true;
    }

    public async Task<CloudSnapshot> Load()
    {
        await Task.Yield();
        Record record = Read(PlayerId);
        return new CloudSnapshot { Meta = Copy(record.Meta), RawData = record.Data };
    }

    public async Task<SaveResult> Submit(SubmitRequest request, string requestJson)
    {
        await Task.Yield();
        string playerId = PlayerId;
        long now = SaveCloud.UnixNow();
        Record record = Read(playerId);
        // From the JSON, like the real request (SaveRules unpacks LedgerGz itself)
        SubmitRequest sent = JsonConvert.DeserializeObject<SubmitRequest>(requestJson ?? JsonConvert.SerializeObject(request));

        // Like the module: the packed save (preferred) becomes the JSON text the rules read
        if (!string.IsNullOrEmpty(sent.SaveGz))
        {
            if (sent.SaveGz.Length > SaveCloud.MaxPackedSaveBytes)
                return Answer(SaveResult.From(SaveCodes.Invalid, "The packed save is " + sent.SaveGz.Length + " bytes (the limit is " + SaveCloud.MaxPackedSaveBytes + ").", record.Meta, now));
            try
            {
                sent.SaveJson = SavePacking.Unpack(sent.SaveGz);
            }
            catch (Exception e)
            {
                return Answer(SaveResult.From(SaveCodes.Invalid, "The packed save couldn't be read: " + e.Message, record.Meta, now));
            }
            sent.SaveGz = null;
        }

        // Like the module: the stored save of the same run is the previous save (staff who left still count)
        var context = new SaveSubmitContext
        {
            PreviousSaveJson = record.Meta != null && record.Meta.RunId == sent.RunId ? UnpackOrNull(record.Data) : null
        };
        SubmitDecision decision;
        try
        {
            decision = SaveRules.EvaluateSubmit(record.Meta, sent, new CityTotals { Known = false }, now, context);
        }
        catch (NotImplementedException e)
        {
            return Answer(SaveResult.From(SaveCodes.Error, "SaveRules isn't written yet: " + e.Message, record.Meta, now));
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            return Answer(SaveResult.From(SaveCodes.Error, "SaveRules threw: " + e.Message, record.Meta, now));
        }
        if (decision == null)
            return Answer(SaveResult.From(SaveCodes.Error, "SaveRules gave no decision.", record.Meta, now));

        if (decision.Accepted)
        {
            record.Meta = decision.Meta;
            record.Data = decision.StoredSave ?? SavePacking.Pack(sent.SaveJson); // stored packed, exactly like the module's save_data
            Write(playerId, record);
        }
        SaveResult result = SaveResult.From(decision.Code, decision.Message, decision.Accepted ? decision.Meta : record.Meta, now);
        result.NewFlags = decision.NewFlags ?? new List<SaveFlag>();
        return Answer(result);
    }

    public async Task<SaveResult> NewRun(string runId, long knownAcceptedAt)
    {
        await Task.Yield();
        string playerId = PlayerId;
        long now = SaveCloud.UnixNow();
        Record record = Read(playerId);
        if (!SaveRules.IsValidRunId(runId))
            return Answer(SaveResult.From(SaveCodes.Invalid, "The run id is missing or malformed.", record.Meta, now));
        if (SaveRules.IsLiveRun(record.Meta, runId))
            return Answer(SaveResult.From(SaveCodes.Accepted, "Run " + runId + " is already the current run.", record.Meta, now));
        if (SaveRules.IsEndedRun(record.Meta, runId))
            return Answer(SaveResult.From(SaveCodes.Ended, "Run " + runId + " is over (Game Over); a new company needs a new run id.", record.Meta, now));
        // Another PC played the live company after this client last synced: a New Game made on an out-of-date PC mustn't
        // replace it (nothing changes; the client keeps its company aside and loads this one)
        if (SaveRules.IsSuperseded(record.Meta, runId, knownAcceptedAt))
            return Answer(SaveResult.From(SaveCodes.Superseded, "Run " + record.Meta.RunId + " was accepted at " + record.Meta.AcceptedAt +
                                                                ", after the client's last sync (" + knownAcceptedAt + ").", record.Meta, now));
        // A new company replaces the old one (its save goes); the time baseline is the previous meta's (SaveRules)
        record.Meta = SaveRules.NewRunMeta(runId, now, new CityTotals { Known = false }, record.Meta);
        record.Data = null;
        Write(playerId, record);
        return Answer(SaveResult.From(SaveCodes.Accepted, "Run " + runId + " started.", record.Meta, now));
    }

    public async Task<SaveResult> EndRun(string runId, long baseSeq, long inFlightSeq, string inFlightHash)
    {
        await Task.Yield();
        string playerId = PlayerId;
        long now = SaveCloud.UnixNow();
        Record record = Read(playerId);
        if (!SaveRules.IsValidRunId(runId))
            return Answer(SaveResult.From(SaveCodes.Invalid, "The run id is missing or malformed.", record.Meta, now));
        // Other runs are left alone (like the module: no_run, nothing changes)
        if (record.Meta == null || record.Meta.RunId != runId)
            return Answer(SaveResult.From(SaveCodes.NoRun, "Run " + runId + " isn't the current run; nothing changed.", record.Meta, now));
        if (record.Meta.Ended)
        {
            record.Data = null;
            Write(playerId, record);
            return Answer(SaveResult.From(SaveCodes.Accepted, "Run " + runId + " was already over.", record.Meta, now));
        }
        // Like the module: only a copy that is up to date with the server ends the run
        if (!SaveRules.IsUpToDateForEnd(record.Meta, baseSeq, inFlightSeq, inFlightHash, record.Data))
            return Answer(SaveResult.From(SaveCodes.Stale, "Another device moved run " + runId + " on: the server has save " + record.Meta.Seq +
                                                           ", this copy continued from " + baseSeq + ". The run goes on.", record.Meta, now));
        record.Meta = SaveRules.EndRunMeta(record.Meta, runId, now);
        record.Data = null;
        Write(playerId, record);
        return Answer(SaveResult.From(SaveCodes.Accepted, "Run " + runId + " is over.", record.Meta, now));
    }

    /// <summary>The local test server keeps no city accounts: the client leaves city money alone (Known = false).</summary>
    public Task<CityMoney> LoadCityMoney()
    {
        return Task.FromResult(new CityMoney { Known = false });
    }

    static Record Read(string playerId)
    {
        string path = FileFor(playerId);
        try
        {
            if (File.Exists(path))
                return JsonConvert.DeserializeObject<Record>(File.ReadAllText(path, Encoding.UTF8)) ?? new Record();
        }
        catch (Exception e)
        {
            Debug.LogWarning("[CloudSave] The local test server file " + path + " couldn't be read (" + e.Message + "): treating it as empty.");
        }
        return new Record();
    }

    static void Write(string playerId, Record record)
    {
        CloudSyncState.WriteAtomically(FileFor(playerId), JsonConvert.SerializeObject(record, Formatting.Indented));
    }

    /// <summary>The answer as the client would read it from the wire (also a deep copy).</summary>
    static SaveResult Answer(SaveResult result)
    {
        return JsonConvert.DeserializeObject<SaveResult>(JsonConvert.SerializeObject(result));
    }

    static string UnpackOrNull(string stored)
    {
        try
        {
            return SavePacking.Unpack(stored);
        }
        catch (Exception)
        {
            return null;
        }
    }

    static SaveMeta Copy(SaveMeta meta)
    {
        return meta == null ? null : JsonConvert.DeserializeObject<SaveMeta>(JsonConvert.SerializeObject(meta));
    }

    /// <summary>For the status tool: the local test server's record of a player (null when there is none).</summary>
    public static string DescribeRecord(string playerId)
    {
        string path = FileFor(playerId);
        if (!File.Exists(path)) return "local test server: no record for " + playerId;
        Record record = Read(playerId);
        string save = ", no save";
        if (record.Data != null)
        {
            string json = UnpackOrNull(record.Data);
            save = ", save " + record.Data.Length + " chars stored" + (SavePacking.IsPacked(record.Data) ? " (packed)" : "") +
                   (json != null ? ", " + json.Length + " unpacked" : ", UNREADABLE");
        }
        return "local test server (" + path + "): " + CloudSaveSync.DescribeMeta(record.Meta) + save;
    }
}
