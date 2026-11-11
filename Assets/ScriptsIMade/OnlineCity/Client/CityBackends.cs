using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Unity.Services.Authentication;
using Unity.Services.CloudCode;
using Unity.Services.CloudCode.Subscriptions;
using Unity.Services.CloudSave;
using Unity.Services.CloudSave.Models.Data.Player;
using Unity.Services.Core;
using UnityEngine;

/// <summary>Where the game's city requests go: the real server (Unity Gaming Services) or the local test cities.</summary>
public interface ICityBackend
{
    bool IsLive { get; }
    string PlayerId { get; }
    Task Start();
    Task<CityResult> Call(string cityId, string function, Dictionary<string, object> args);
    /// <summary>A city's districts saved so far (districts nobody touched yet are left out).</summary>
    Task<Dictionary<string, CityDistrict>> LoadDistricts(string cityId);
    /// <summary>My account in every city I joined (the city picker shows them).</summary>
    Task<Dictionary<string, CityAccount>> LoadMyAccounts();
    /// <summary>Player drawings of a city by hash (unknown hashes are left out).</summary>
    Task<Dictionary<string, CityLogoRecord>> LoadLogos(string cityId, IList<string> hashes);
    /// <summary>Raised from any thread: CityOnline moves it to the main thread.</summary>
    event Action<CityPush> PlotsPushed;
    event Action<CityMessage> NewsPushed;
    void Stop();
}

/// <summary>
/// The real cities: Unity Authentication (anonymous for now, one account per PC; Steam later), the "OnlineCity" Cloud
/// Code module for every change, Cloud Save game data for reading maps and drawings, and push messages for live updates.
/// </summary>
public class CityUgsBackend : ICityBackend
{
    public const string ModuleName = "OnlineCity";

    ISubscriptionEvents projectMessages;
    ISubscriptionEvents playerMessages;

    public bool IsLive => true;
    public string PlayerId => AuthenticationService.Instance.PlayerId;

    public event Action<CityPush> PlotsPushed;
    public event Action<CityMessage> NewsPushed;

    public async Task Start()
    {
        if (UnityServices.State == ServicesInitializationState.Uninitialized)
            await UnityServices.InitializeAsync();
        else
            while (UnityServices.State == ServicesInitializationState.Initializing) await Task.Yield();

        if (!AuthenticationService.Instance.IsSignedIn)
            await AuthenticationService.Instance.SignInAnonymouslyAsync();

        await Subscribe();
    }

    async Task Subscribe()
    {
        // Live updates are a bonus: the map also refreshes on its own every minute while it's open
        try
        {
            var projectCallbacks = new SubscriptionEventCallbacks();
            projectCallbacks.MessageReceived += OnProjectMessage;
            projectMessages = await CloudCodeService.Instance.SubscribeToProjectMessagesAsync(projectCallbacks);

            var playerCallbacks = new SubscriptionEventCallbacks();
            playerCallbacks.MessageReceived += OnPlayerMessage;
            playerMessages = await CloudCodeService.Instance.SubscribeToPlayerMessagesAsync(playerCallbacks);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[City] Live updates unavailable ({e.Message}). The map refreshes every minute instead.");
        }
    }

    void OnProjectMessage(IMessageReceivedEvent message)
    {
        if (message.MessageType != "city.plots") return;
        try { PlotsPushed?.Invoke(JsonConvert.DeserializeObject<CityPush>(message.Message)); }
        catch (Exception e) { Debug.LogWarning($"[City] Bad live update: {e.Message}"); }
    }

    void OnPlayerMessage(IMessageReceivedEvent message)
    {
        if (message.MessageType != "city.news") return;
        try { NewsPushed?.Invoke(JsonConvert.DeserializeObject<CityMessage>(message.Message)); }
        catch (Exception e) { Debug.LogWarning($"[City] Bad news message: {e.Message}"); }
    }

    public async Task<CityResult> Call(string cityId, string function, Dictionary<string, object> args)
    {
        var all = args != null ? new Dictionary<string, object>(args) : new Dictionary<string, object>();
        all["city"] = cityId;
        string json = await CloudCodeService.Instance.CallModuleEndpointAsync<string>(ModuleName, function, all);
        return JsonConvert.DeserializeObject<CityResult>(json) ?? CityResult.Fail("The city sent an empty answer.");
    }

    public async Task<Dictionary<string, CityDistrict>> LoadDistricts(string cityId)
    {
        var result = new Dictionary<string, CityDistrict>();
        var map = CityLayout.Get(cityId);
        if (map == null) return result;
        foreach (var pair in await LoadCustom("city_" + cityId, map.DistrictIds.Select(id => "d_" + id).ToList()))
        {
            var district = pair.Value?.Value?.GetAs<CityDistrict>();
            if (district == null) continue;
            if (string.IsNullOrEmpty(district.Id)) district.Id = pair.Key.Substring(2);
            district.EnsureLists();
            result[district.Id] = district;
        }
        return result;
    }

    public async Task<Dictionary<string, CityAccount>> LoadMyAccounts()
    {
        var keys = new HashSet<string>(CityLayout.Worlds.Select(w => "acct_" + w.Id));
        var items = await CloudSaveService.Instance.Data.Player.LoadAsync(keys, new LoadOptions(new ProtectedReadAccessClassOptions()));
        var result = new Dictionary<string, CityAccount>();
        foreach (var pair in items)
        {
            var account = pair.Value?.Value?.GetAs<CityAccount>();
            if (account == null) continue;
            account.EnsureLists();
            result[pair.Key.Substring("acct_".Length)] = account;
        }
        return result;
    }

    public async Task<Dictionary<string, CityLogoRecord>> LoadLogos(string cityId, IList<string> hashes)
    {
        var result = new Dictionary<string, CityLogoRecord>();
        foreach (var pair in await LoadCustom("logos_" + cityId, hashes.Select(h => "l_" + h).ToList()))
        {
            var record = pair.Value?.Value?.GetAs<CityLogoRecord>();
            if (record == null) continue;
            if (string.IsNullOrEmpty(record.Hash)) record.Hash = pair.Key.Substring(2);
            result[record.Hash] = record;
        }
        return result;
    }

    /// <summary>Custom game data by key, 20 keys a request.</summary>
    static async Task<Dictionary<string, Unity.Services.CloudSave.Models.Item>> LoadCustom(string customId, List<string> keys)
    {
        var result = new Dictionary<string, Unity.Services.CloudSave.Models.Item>();
        for (int start = 0; start < keys.Count; start += 20)
        {
            var batch = new HashSet<string>(keys.Skip(start).Take(20));
            try
            {
                foreach (var pair in await CloudSaveService.Instance.Data.Custom.LoadAsync(customId, batch))
                    result[pair.Key] = pair.Value;
            }
            catch (CloudSaveException e) when (e.Reason == CloudSaveExceptionReason.NotFound)
            {
                // Nothing saved under this id yet
            }
        }
        return result;
    }

    public void Stop()
    {
        try
        {
            if (projectMessages != null) _ = projectMessages.UnsubscribeAsync();
            if (playerMessages != null) _ = playerMessages.UnsubscribeAsync();
        }
        catch (Exception) { }
        projectMessages = null;
        playerMessages = null;
    }

    /// <summary>A sentence the player can act on, for anything the services throw.</summary>
    public static string Describe(Exception e)
    {
        switch (e)
        {
            case CloudCodeException cc when cc.Reason == CloudCodeExceptionReason.NoInternetConnection:
                return "No internet connection. The cities need to be online.";
            case CloudCodeException cc when cc.Reason == CloudCodeExceptionReason.NotFound:
                return "The city server isn't set up yet. Deploy the OnlineCity module (Services > Deployment).";
            case CloudCodeException cc when cc.Reason == CloudCodeExceptionReason.TooManyRequests:
                return "Too many requests. Wait a moment and try again.";
            case CloudCodeException cc when cc.Reason == CloudCodeExceptionReason.ScriptError:
                return "The city server had a problem. Try again in a moment.";
            case AuthenticationException _:
                return "Couldn't sign in to the cities. Check your internet connection.";
            case CloudSaveException cs when cs.Reason == CloudSaveExceptionReason.NoInternetConnection:
                return "No internet connection. The cities need to be online.";
            case ServicesInitializationException _:
                return "Unity Gaming Services couldn't start. Is the project linked in Project Settings > Services?";
            case RequestFailedException rf:
                return $"Couldn't reach the city ({rf.Message}).";
            default:
                return $"Couldn't reach the city ({e.Message}).";
        }
    }
}

/// <summary>
/// Cities that live on this PC only: the same rules (CityLogic) against files, so everything can be tried without the
/// server. Editor and development builds only. Includes a test rival company.
/// </summary>
public class CityLocalBackend : ICityBackend
{
    public const string LocalPlayerId = "local_player";
    public const string RivalId = "local_rival";
    const string FilePrefix = "city_local_test_";

    class LocalCity
    {
        public CityMemoryStore Store;
        public CityLogic Logic;
        public bool Dirty;
    }

    readonly Dictionary<string, LocalCity> cities = new Dictionary<string, LocalCity>();
    float lastFlush;

    public bool IsLive => false;
    public string PlayerId => LocalPlayerId;

    /// <summary>Editor snapshots and tests: keep their cities in other files.</summary>
    public static string FilePrefixOverride;
    public static string FilePath(string cityId) => Path.Combine(Application.persistentDataPath, (FilePrefixOverride ?? FilePrefix) + cityId + ".json");

    public event Action<CityPush> PlotsPushed;
    public event Action<CityMessage> NewsPushed;

    public Task Start()
    {
        foreach (var world in CityLayout.Worlds)
        {
            var city = new LocalCity { Store = new CityMemoryStore() };
            string path = FilePath(world.Id);
            try
            {
                if (File.Exists(path) && !city.Store.Import(File.ReadAllText(path)))
                    Debug.LogWarning($"[City] The local test city file {path} couldn't be read - starting a fresh one.");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[City] Couldn't read the local test city: {e.Message}");
            }
            city.Store.Changed += () => city.Dirty = true;
            city.Logic = new CityLogic(city.Store, world.Id, new Notifier(this, world.Id), null, null, m => Debug.LogWarning(m));
            cities[world.Id] = city;
        }
        return Task.CompletedTask;
    }

    LocalCity Get(string cityId)
    {
        if (cityId == null || !cities.TryGetValue(cityId, out var city)) throw new ArgumentException("Unknown city " + cityId);
        return city;
    }

    public Task<CityResult> Call(string cityId, string function, Dictionary<string, object> args) =>
        Get(cityId).Logic.Invoke(LocalPlayerId, function, args);

    public async Task<Dictionary<string, CityDistrict>> LoadDistricts(string cityId)
    {
        var loaded = await Get(cityId).Store.LoadDistricts(CityLayout.Get(cityId).DistrictIds.ToList());
        return loaded.ToDictionary(p => p.Key, p => p.Value.Value);
    }

    public async Task<Dictionary<string, CityAccount>> LoadMyAccounts()
    {
        var result = new Dictionary<string, CityAccount>();
        foreach (var pair in cities)
        {
            var data = await pair.Value.Store.LoadPlayer(LocalPlayerId);
            if (data.Account != null) result[pair.Key] = data.Account;
        }
        return result;
    }

    public async Task<Dictionary<string, CityLogoRecord>> LoadLogos(string cityId, IList<string> hashes)
    {
        var result = new Dictionary<string, CityLogoRecord>();
        foreach (string hash in hashes)
        {
            var record = await Get(cityId).Store.LoadLogo(hash);
            if (record?.Value != null) result[hash] = record.Value;
        }
        return result;
    }

    /// <summary>Write the files now and then (CityOnline calls this every frame).</summary>
    public void Tick()
    {
        if (Time.unscaledTime - lastFlush < 1f) return;
        Flush();
    }

    public void Flush()
    {
        lastFlush = Time.unscaledTime;
        foreach (var pair in cities)
        {
            if (!pair.Value.Dirty) continue;
            pair.Value.Dirty = false;
            try { File.WriteAllText(FilePath(pair.Key), pair.Value.Store.Export()); }
            catch (Exception e) { Debug.LogWarning($"[City] Couldn't save the local test city: {e.Message}"); }
        }
    }

    public void Stop() => Flush();

    public void Wipe(string cityId)
    {
        Get(cityId).Store.Clear();
        Flush();
    }

    // ---- Test rival: lets you see challenges and reports happen to you --------------------------------------------

    public async Task<string> RivalAct(string cityId, string what)
    {
        var city = Get(cityId);
        var map = CityLayout.Get(cityId);
        await EnsureRival(city);
        var districts = await LoadDistricts(cityId);
        var mine = districts.Values.SelectMany(d => d.Plots).Where(p => p.Value.OwnerId == LocalPlayerId).ToList();
        string Op() => "rv" + Guid.NewGuid().ToString("N").Substring(0, 16);

        switch (what)
        {
            case "challenge":
            {
                if (mine.Count == 0) return "Buy a plot first - the rival challenges one of your plots.";
                // Test tool: skip the shield so it can happen right away
                var pick = mine[UnityEngine.Random.Range(0, mine.Count)];
                await ExpireShield(city, map, pick.Key);
                var result = await city.Logic.StartChallenge(RivalId, Op(), pick.Key);
                if (!result.Ok) return result.Error;
                for (int i = 0; i < 30 && result.Game != null && result.Game.Status == CityRules.GamePlaying; i++)
                {
                    string move = CityCards.Score(result.Game.PlayerCards) < 15 ? "hit" : "stand";
                    result = await city.Logic.ChallengeMove(RivalId, Op(), move);
                }
                int number = map.PlotsById[pick.Key].Number;
                return result.Game?.Status == CityRules.GameDone
                    ? $"The rival beat the dealer and took plot #{number}. You were paid."
                    : $"The rival lost a hand at plot #{number}. Their stake is yours.";
            }
            case "report":
            {
                var account = (await city.Store.LoadPlayer(LocalPlayerId)).Account;
                if (account == null || !CityPixelLogo.IsRef(account.Logo)) return "Draw a logo first - this reports your drawing.";
                if (mine.Count == 0) return "Buy a plot first - reports are made from a plot showing your logo.";
                for (int i = 1; i <= CityRules.ReportsToHide; i++)
                {
                    string reporter = "local_reporter_" + i;
                    if ((await city.Store.LoadPlayer(reporter)).Account == null)
                        await city.Logic.Join(reporter, "Reporter " + i, "1|circle|676B74|eye|FFFDF6||1");
                    await city.Logic.ReportLogo(reporter, mine[0].Key);
                }
                return $"{CityRules.ReportsToHide} test players reported your drawing.";
            }
        }
        return "Unknown rival action.";
    }

    /// <summary>Test money straight into the city bank (the daily limit doesn't apply to this tool).</summary>
    public async Task<string> AddTestMoney(string cityId, string playerId, long amount)
    {
        var city = Get(cityId);
        for (int attempt = 0; attempt < 5; attempt++)
        {
            var data = await city.Store.LoadPlayer(playerId);
            if (data.Account == null) return "Set up your company first.";
            data.Account.Balance = CityRules.ClampBalance(data.Account.Balance + amount);
            try
            {
                await city.Store.SaveAccount(playerId, data.Account, data.Lock);
                return $"Added {CityRules.Money(amount)} of test money.";
            }
            catch (CityConflictException) { }
        }
        return "Couldn't add test money.";
    }

    async Task EnsureRival(LocalCity city)
    {
        var data = await city.Store.LoadPlayer(RivalId);
        if (data.Account == null)
            await city.Logic.Join(RivalId, "Rival Test Co", "1|shield|7A4FB5|bolt|FFE14D|RT|1");
        data = await city.Store.LoadPlayer(RivalId);
        if (data.Account != null && data.Account.Balance < 20L * CityRules.DepositCapPerDay)
            await AddTestMoney(city.Logic.CityId, RivalId, 50L * CityRules.DepositCapPerDay);
    }

    static async Task ExpireShield(LocalCity city, CityMap map, string plotId)
    {
        var def = map.PlotsById[plotId];
        for (int attempt = 0; attempt < 5; attempt++)
        {
            var loaded = await city.Store.LoadDistricts(new[] { def.DistrictId });
            if (!loaded.TryGetValue(def.DistrictId, out var district)) return;
            if (!district.Value.Plots.TryGetValue(plotId, out var plot) || plot.ShieldUntil == 0) return;
            plot.ShieldUntil = 0;
            try
            {
                await city.Store.SaveDistrict(district.Value, district.Lock);
                return;
            }
            catch (CityConflictException) { }
        }
    }

    class Notifier : ICityNotifier
    {
        readonly CityLocalBackend owner;
        readonly string cityId;
        public Notifier(CityLocalBackend owner, string cityId) { this.owner = owner; this.cityId = cityId; }

        public Task PlotsChanged(List<CityPlotChange> changes)
        {
            owner.PlotsPushed?.Invoke(new CityPush { CityId = cityId, Changes = changes });
            return Task.CompletedTask;
        }

        public Task PlayerNews(string playerId, CityMessage message)
        {
            message.CityId = cityId;
            if (playerId == LocalPlayerId) owner.NewsPushed?.Invoke(message);
            return Task.CompletedTask;
        }
    }
}
