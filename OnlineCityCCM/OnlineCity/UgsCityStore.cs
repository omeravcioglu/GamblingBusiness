using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;
using Unity.Services.CloudCode.Shared;
using Unity.Services.CloudSave.Model;

/// <summary>
/// One city in Cloud Save (every city uses its own keys, so the six worlds never mix):
/// - Districts: game data, custom id "city_&lt;city&gt;", keys "d_&lt;district&gt;". Every player can read them (the map).
/// - Drawings: game data, custom id "logos_&lt;city&gt;", keys "l_&lt;hash&gt;". Every player can read them.
/// - Reports: PRIVATE game data, custom id "reports_&lt;city&gt;": only this module sees who reported whom.
/// - Accounts: protected player data (key "acct_&lt;city&gt;"): the player can read it, only this module writes it.
/// - Payouts: protected player data (keys "pay_&lt;city&gt;_&lt;id&gt;") written for the receiving player, applied on their next call.
/// Saves pass the write lock they read, so a concurrent change fails with 409 instead of being overwritten.
/// </summary>
public class UgsCityStore : ICityStore
{
    readonly IExecutionContext ctx;
    readonly IGameApiClient api;
    readonly string cityId;

    public UgsCityStore(IExecutionContext ctx, IGameApiClient api, string cityId)
    {
        this.ctx = ctx;
        this.api = api;
        this.cityId = cityId;
    }

    public static string DistrictsId(string city) => "city_" + city;
    public static string LogosId(string city) => "logos_" + city;
    public static string ReportsId(string city) => "reports_" + city;
    public static string AccountKey(string city) => "acct_" + city;
    public static string PayoutPrefix(string city) => "pay_" + city + "_";
    public static string DistrictKey(string districtId) => "d_" + districtId;
    public static string LogoKey(string hash) => "l_" + hash;
    public static string ReportKey(string hash) => "r_" + hash;

    // ---- Districts ---------------------------------------------------------------------------------------------

    public async Task<Dictionary<string, CityVersioned<CityDistrict>>> LoadDistricts(IList<string> districtIds)
    {
        var result = new Dictionary<string, CityVersioned<CityDistrict>>();
        if (districtIds == null || districtIds.Count == 0) return result;

        foreach (var item in await CustomItems(DistrictsId(cityId), districtIds.Select(DistrictKey).ToList(), false))
        {
            var district = Parse<CityDistrict>(item.Value);
            if (district == null) continue;
            string id = !string.IsNullOrEmpty(district.Id) ? district.Id : item.Key.Substring(2);
            district.Id = id;
            result[id] = new CityVersioned<CityDistrict>(district, item.WriteLock);
        }
        return result;
    }

    public Task<string> SaveDistrict(CityDistrict district, string writeLock) =>
        SetCustom(DistrictsId(cityId), DistrictKey(district.Id), district, writeLock, false);

    // ---- Drawings and reports ----------------------------------------------------------------------------------

    public async Task<CityVersioned<CityLogoRecord>> LoadLogo(string hash)
    {
        var item = (await CustomItems(LogosId(cityId), new List<string> { LogoKey(hash) }, false)).FirstOrDefault();
        var record = item != null ? Parse<CityLogoRecord>(item.Value) : null;
        return record != null ? new CityVersioned<CityLogoRecord>(record, item.WriteLock) : null;
    }

    public Task<string> SaveLogo(CityLogoRecord logo, string writeLock) =>
        SetCustom(LogosId(cityId), LogoKey(logo.Hash), logo, writeLock, false);

    public async Task<CityVersioned<CityReportRecord>> LoadReport(string hash)
    {
        var item = (await CustomItems(ReportsId(cityId), new List<string> { ReportKey(hash) }, true)).FirstOrDefault();
        var record = item != null ? Parse<CityReportRecord>(item.Value) : null;
        return record != null ? new CityVersioned<CityReportRecord>(record, item.WriteLock) : null;
    }

    public Task<string> SaveReport(CityReportRecord report, string writeLock) =>
        SetCustom(ReportsId(cityId), ReportKey(report.Hash), report, writeLock, true);

    async Task<List<Item>> CustomItems(string customId, List<string> keys, bool isPrivate)
    {
        var all = new List<Item>();
        // The API takes at most 20 keys per request
        for (int start = 0; start < keys.Count; start += 20)
        {
            var batch = keys.Skip(start).Take(20).ToList();
            try
            {
                var response = isPrivate
                    ? await api.CloudSaveData.GetPrivateCustomItemsAsync(ctx, ctx.ServiceToken, ctx.ProjectId, customId, batch)
                    : await api.CloudSaveData.GetCustomItemsAsync(ctx, ctx.ServiceToken, ctx.ProjectId, customId, batch);
                all.AddRange(response.Data?.Results ?? new List<Item>());
            }
            catch (ApiException e) when (e.Response?.StatusCode == HttpStatusCode.NotFound)
            {
                // Nothing saved under this id yet
            }
        }
        return all;
    }

    async Task<string> SetCustom(string customId, string key, object value, string writeLock, bool isPrivate)
    {
        try
        {
            var body = new SetItemBody(key, JObject.FromObject(value), writeLock);
            var response = isPrivate
                ? await api.CloudSaveData.SetPrivateCustomItemAsync(ctx, ctx.ServiceToken, ctx.ProjectId, customId, body)
                : await api.CloudSaveData.SetCustomItemAsync(ctx, ctx.ServiceToken, ctx.ProjectId, customId, body);
            return response.Data?.WriteLock;
        }
        catch (ApiException e) when (e.Response?.StatusCode == HttpStatusCode.Conflict)
        {
            throw new CityConflictException();
        }
    }

    // ---- Players -------------------------------------------------------------------------------------------------

    public async Task<CityPlayerData> LoadPlayer(string playerId)
    {
        var data = new CityPlayerData();
        string accountKey = AccountKey(cityId), payoutPrefix = PayoutPrefix(cityId);

        // Only this city's keys: list them first (the player also has accounts in other cities)
        var keys = new List<string>();
        string after = null;
        for (int page = 0; page < 50; page++)
        {
            GetKeysResponse listed;
            try
            {
                var response = await api.CloudSaveData.GetProtectedKeysAsync(ctx, ctx.ServiceToken, ctx.ProjectId, playerId, after);
                listed = response.Data;
            }
            catch (ApiException e) when (e.Response?.StatusCode == HttpStatusCode.NotFound)
            {
                break; // A player with no saved data yet
            }
            if (listed?.Results == null || listed.Results.Count == 0) break;
            foreach (var meta in listed.Results)
            {
                if (meta.Key == accountKey || meta.Key.StartsWith(payoutPrefix, StringComparison.Ordinal)) keys.Add(meta.Key);
            }
            if (string.IsNullOrEmpty(listed.Links?.Next)) break;
            after = listed.Results[listed.Results.Count - 1].Key;
        }

        for (int start = 0; start < keys.Count; start += 20)
        {
            var batch = keys.Skip(start).Take(20).ToList();
            GetItemsResponse items;
            try
            {
                var response = await api.CloudSaveData.GetProtectedItemsAsync(ctx, ctx.ServiceToken, ctx.ProjectId, playerId, batch);
                items = response.Data;
            }
            catch (ApiException e) when (e.Response?.StatusCode == HttpStatusCode.NotFound)
            {
                continue;
            }
            foreach (var item in items?.Results ?? new List<Item>())
            {
                if (item.Key == accountKey)
                {
                    data.Account = Parse<CityAccount>(item.Value);
                    data.Lock = item.WriteLock;
                }
                else if (item.Key.StartsWith(payoutPrefix, StringComparison.Ordinal))
                {
                    var payout = Parse<CityPayout>(item.Value);
                    if (payout != null && payout.PlayerId == playerId) data.Payouts.Add(payout);
                }
            }
        }
        return data;
    }

    public async Task<string> SaveAccount(string playerId, CityAccount account, string writeLock)
    {
        try
        {
            var body = new SetItemBody(AccountKey(cityId), JObject.FromObject(account), writeLock);
            var response = await api.CloudSaveData.SetProtectedItemAsync(ctx, ctx.ServiceToken, ctx.ProjectId, playerId, body);
            return response.Data?.WriteLock;
        }
        catch (ApiException e) when (e.Response?.StatusCode == HttpStatusCode.Conflict)
        {
            throw new CityConflictException();
        }
    }

    public async Task SavePayout(CityPayout payout)
    {
        var body = new SetItemBody(PayoutPrefix(cityId) + payout.Id, JObject.FromObject(payout));
        await api.CloudSaveData.SetProtectedItemAsync(ctx, ctx.ServiceToken, ctx.ProjectId, payout.PlayerId, body);
    }

    public async Task DeletePayout(string playerId, string payoutId)
    {
        try
        {
            await api.CloudSaveData.DeleteProtectedItemAsync(ctx, ctx.ServiceToken, PayoutPrefix(cityId) + payoutId, ctx.ProjectId, playerId);
        }
        catch (ApiException e) when (e.Response?.StatusCode == HttpStatusCode.NotFound)
        {
            // Already gone
        }
    }

    /// <summary>Cloud Save hands values back as JSON tokens; accept a JSON string too.</summary>
    static T Parse<T>(object value) where T : class
    {
        if (value == null) return null;
        if (value is JToken token)
        {
            if (token.Type == JTokenType.String) return JsonConvert.DeserializeObject<T>(token.Value<string>());
            return token.ToObject<T>();
        }
        if (value is string text) return JsonConvert.DeserializeObject<T>(text);
        return JToken.FromObject(value).ToObject<T>();
    }
}

/// <summary>Live updates through Cloud Code push messages (the game subscribes with Wire and ignores other cities).</summary>
public class UgsCityNotifier : ICityNotifier
{
    /// <summary>Bigger pushes only say "reload" (push messages are meant to be small).</summary>
    const int MaxPushChars = 7000;

    readonly IExecutionContext ctx;
    readonly IPushClient push;
    readonly ILogger logger;
    readonly string cityId;

    public UgsCityNotifier(IExecutionContext ctx, IPushClient push, ILogger logger, string cityId)
    {
        this.ctx = ctx;
        this.push = push;
        this.logger = logger;
        this.cityId = cityId;
    }

    public async Task PlotsChanged(List<CityPlotChange> changes)
    {
        try
        {
            string json = JsonConvert.SerializeObject(new CityPush { CityId = cityId, Changes = changes });
            if (json.Length > MaxPushChars)
                json = JsonConvert.SerializeObject(new CityPush { CityId = cityId, Changes = new List<CityPlotChange>(), Reload = true });
            await push.SendProjectMessageAsync(ctx, json, "city.plots");
        }
        catch (Exception e)
        {
            logger.LogWarning("Couldn't push plot changes: {Error}", e.Message);
        }
    }

    public async Task PlayerNews(string playerId, CityMessage message)
    {
        try
        {
            message.CityId = cityId;
            await push.SendPlayerMessageAsync(ctx, JsonConvert.SerializeObject(message), "city.news", playerId);
        }
        catch (Exception e)
        {
            logger.LogWarning("Couldn't push news to {Player}: {Error}", playerId, e.Message);
        }
    }
}
