using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;
using Unity.Services.CloudCode.Shared;
using Unity.Services.CloudSave.Model;

/// <summary>
/// The calling player's cloud save in Cloud Save, protected player data (the player can read it; only this module writes
/// it, Assets/CloudCode/OnlineCity.ac denies player writes):
/// - save_meta: SaveMeta as a JSON object; save_data: the save packed with SavePacking (a JSON string value; saves written
///   before packing are plain JSON text, which SavePacking.Unpack passes through).
/// - The Online City accounts acct_&lt;city&gt; (written by the OnlineCity module) are only read: lifetime totals stats.dep /
///   stats.wd and the recent deposit / withdrawal stamps (deps / wds: t, a).
/// Writes pass the write lock they read, so a concurrent change fails with 409 (SaveConflictException) instead of being
/// overwritten; the save and its meta go in one batch.
/// </summary>
public class UgsSaveStore : ISaveStore
{
    /// <summary>The API takes at most 20 keys per read.</summary>
    const int KeysPerRead = 20;
    const int MaxKeyPages = 50;

    readonly IExecutionContext ctx;
    readonly IGameApiClient api;

    public UgsSaveStore(IExecutionContext ctx, IGameApiClient api)
    {
        this.ctx = ctx;
        this.api = api;
    }

    public async Task<StoredSave> LoadSave()
    {
        var result = new StoredSave();
        foreach (Item item in await ProtectedItems(new List<string> { SaveCloud.MetaKey, SaveCloud.DataKey }))
        {
            if (item.Key == SaveCloud.MetaKey)
            {
                // A save_meta that exists but can't be read is flagged, never taken as "no meta" (that would make the next
                // submit an import without a stale check)
                result.Meta = ParseMeta(item.Value);
                result.MetaUnreadable = result.Meta == null || SaveRules.IsUnreadableMeta(result.Meta);
                result.MetaLock = item.WriteLock;
            }
            else if (item.Key == SaveCloud.DataKey)
            {
                result.HasData = true;
                result.DataJson = AsText(item.Value);
                result.DataLock = item.WriteLock;
            }
        }
        return result;
    }

    public async Task<StoredCity> LoadCity()
    {
        var city = new StoredCity();

        // The player's keys, one page at a time: keep the city accounts
        var keys = new List<string>();
        string after = null;
        bool complete = false;
        for (int page = 0; page < MaxKeyPages; page++)
        {
            GetKeysResponse listed;
            try
            {
                var response = await api.CloudSaveData.GetProtectedKeysAsync(ctx, ctx.ServiceToken, ctx.ProjectId, ctx.PlayerId, after);
                listed = response.Data;
            }
            catch (ApiException e) when (e.Response?.StatusCode == HttpStatusCode.NotFound)
            {
                complete = true;   // A player with no saved data yet
                break;
            }
            if (listed?.Results == null || listed.Results.Count == 0)
            {
                complete = true;
                break;
            }
            foreach (KeyMetadata meta in listed.Results)
            {
                if (meta?.Key != null && meta.Key.StartsWith(SaveCloud.CityAccountPrefix, StringComparison.Ordinal)) keys.Add(meta.Key);
            }
            if (string.IsNullOrEmpty(listed.Links?.Next))
            {
                complete = true;
                break;
            }
            after = listed.Results[listed.Results.Count - 1].Key;
        }
        if (!complete) throw new InvalidOperationException("Too many protected keys to list.");

        foreach (Item item in await ProtectedItems(keys))
        {
            JObject account = AsObject(item.Value);
            if (account == null) continue;
            if (account["stats"] is JObject stats)
            {
                city.Totals.Deposited += Long(stats["dep"]);
                city.Totals.Withdrawn += Long(stats["wd"]);
            }
            AddStamps(city.Transfers, account["deps"] as JArray, true);
            AddStamps(city.Transfers, account["wds"] as JArray, false);
            // Applied op ids (CityAccount ops): a transfer whose stamp was tidied away is still booked
            if (account["ops"] is JArray ops)
            {
                foreach (JToken op in ops)
                    if (op != null && op.Type == JTokenType.String) city.Ops.Add(op.Value<string>());
            }
        }
        city.Totals.Known = true;
        city.PlayerId = ctx.PlayerId;
        return city;
    }

    public async Task WriteSaveAndMeta(string storedSave, string dataLock, SaveMeta meta, string metaLock)
    {
        var body = new SetItemBatchBody(new List<SetItemBody>
        {
            new SetItemBody(SaveCloud.DataKey, storedSave ?? "", dataLock),
            new SetItemBody(SaveCloud.MetaKey, JObject.FromObject(meta), metaLock)
        });
        try
        {
            await api.CloudSaveData.SetProtectedItemBatchAsync(ctx, ctx.ServiceToken, ctx.ProjectId, ctx.PlayerId, body);
        }
        catch (ApiException e) when (e.Response?.StatusCode == HttpStatusCode.Conflict)
        {
            throw new SaveConflictException();
        }
    }

    public async Task WriteMeta(SaveMeta meta, string metaLock)
    {
        try
        {
            var body = new SetItemBody(SaveCloud.MetaKey, JObject.FromObject(meta), metaLock);
            await api.CloudSaveData.SetProtectedItemAsync(ctx, ctx.ServiceToken, ctx.ProjectId, ctx.PlayerId, body);
        }
        catch (ApiException e) when (e.Response?.StatusCode == HttpStatusCode.Conflict)
        {
            throw new SaveConflictException();
        }
    }

    public async Task DeleteData(string dataLock)
    {
        try
        {
            await api.CloudSaveData.DeleteProtectedItemAsync(ctx, ctx.ServiceToken, SaveCloud.DataKey, ctx.ProjectId, ctx.PlayerId, dataLock);
        }
        catch (ApiException e) when (e.Response?.StatusCode == HttpStatusCode.NotFound)
        {
            // Already gone
        }
        catch (ApiException e) when (e.Response?.StatusCode == HttpStatusCode.Conflict)
        {
            throw new SaveConflictException();
        }
    }

    async Task<List<Item>> ProtectedItems(List<string> keys)
    {
        var all = new List<Item>();
        for (int start = 0; start < keys.Count; start += KeysPerRead)
        {
            List<string> batch = keys.Skip(start).Take(KeysPerRead).ToList();
            try
            {
                var response = await api.CloudSaveData.GetProtectedItemsAsync(ctx, ctx.ServiceToken, ctx.ProjectId, ctx.PlayerId, batch);
                all.AddRange(response.Data?.Results ?? new List<Item>());
            }
            catch (ApiException e) when (e.Response?.StatusCode == HttpStatusCode.NotFound)
            {
                // Nothing saved under these keys yet
            }
        }
        return all;
    }

    static void AddStamps(List<SaveCityTransfer> into, JArray stamps, bool deposit)
    {
        if (stamps == null) return;
        foreach (JToken token in stamps)
        {
            if (!(token is JObject stamp)) continue;
            long amount = Long(stamp["a"]);
            if (amount <= 0) continue;
            string opId = stamp["id"] != null && stamp["id"].Type == JTokenType.String ? stamp["id"].Value<string>() : null;
            into.Add(new SaveCityTransfer { OpId = opId, Time = Long(stamp["t"]), Amount = amount, Deposit = deposit });
        }
    }

    /// <summary>Cloud Save hands values back as JSON tokens; accept a JSON string too.</summary>
    static SaveMeta ParseMeta(object value)
    {
        try
        {
            if (value == null) return null;
            if (value is JToken token)
            {
                if (token.Type == JTokenType.String) return JsonConvert.DeserializeObject<SaveMeta>(token.Value<string>());
                if (token.Type == JTokenType.Object) return token.ToObject<SaveMeta>();
                return null;
            }
            if (value is string text) return JsonConvert.DeserializeObject<SaveMeta>(text);
            return JToken.FromObject(value).ToObject<SaveMeta>();
        }
        catch (JsonException)
        {
            return null;   // Unreadable: LoadSave flags it (StoredSave.MetaUnreadable)
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    static JObject AsObject(object value)
    {
        try
        {
            if (value is JObject obj) return obj;
            if (value is JToken token && token.Type == JTokenType.String) return SaveSnapshot.ParseObject(token.Value<string>());
            if (value is string text) return SaveSnapshot.ParseObject(text);
            return value != null ? JToken.FromObject(value) as JObject : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>save_data is stored as a JSON string; an object (written by hand) is turned back into text.</summary>
    static string AsText(object value)
    {
        if (value == null) return null;
        if (value is JToken token) return token.Type == JTokenType.String ? token.Value<string>() : token.ToString(Formatting.None);
        if (value is string text) return text;
        return JsonConvert.SerializeObject(value);
    }

    static long Long(JToken token)
    {
        if (!SaveSnapshot.TryNumber(token, out double value) || double.IsNaN(value)) return 0L;
        if (value >= long.MaxValue) return long.MaxValue;
        if (value <= long.MinValue) return long.MinValue;
        return (long)value;
    }
}
