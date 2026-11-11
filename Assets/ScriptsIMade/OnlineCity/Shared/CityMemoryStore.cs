using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;

/// <summary>
/// One city kept in memory (the game's local test city, and the server tests). Items are stored as JSON so callers
/// never share objects with the store, like a real database. Stricter than Cloud Save on purpose: a save with a null
/// lock only creates, it never overwrites.
/// </summary>
public class CityMemoryStore : ICityStore
{
    class Entry
    {
        [JsonProperty("j")] public string Json;
        [JsonProperty("l")] public string Lock;
    }

    class Snapshot
    {
        [JsonProperty("v")] public int Version = 2;
        [JsonProperty("n")] public long LockCounter;
        [JsonProperty("d")] public Dictionary<string, Entry> Districts = new Dictionary<string, Entry>();
        [JsonProperty("a")] public Dictionary<string, Entry> Accounts = new Dictionary<string, Entry>();
        [JsonProperty("p")] public Dictionary<string, Dictionary<string, string>> Payouts = new Dictionary<string, Dictionary<string, string>>();
        [JsonProperty("g")] public Dictionary<string, Entry> Logos = new Dictionary<string, Entry>();
        [JsonProperty("r")] public Dictionary<string, Entry> Reports = new Dictionary<string, Entry>();
    }

    Snapshot data = new Snapshot();
    readonly object gate = new object();

    /// <summary>Raised after every write (the local test city saves its file then).</summary>
    public event Action Changed;

    string NewLock() => "L" + (++data.LockCounter);

    public Task<Dictionary<string, CityVersioned<CityDistrict>>> LoadDistricts(IList<string> districtIds)
    {
        var result = new Dictionary<string, CityVersioned<CityDistrict>>();
        lock (gate)
        {
            foreach (string id in districtIds ?? new List<string>())
            {
                if (id != null && data.Districts.TryGetValue(id, out var entry))
                    result[id] = new CityVersioned<CityDistrict>(JsonConvert.DeserializeObject<CityDistrict>(entry.Json), entry.Lock);
            }
        }
        return Task.FromResult(result);
    }

    public Task<string> SaveDistrict(CityDistrict district, string writeLock) =>
        Save(data.Districts, district.Id, district, writeLock);

    public Task<CityPlayerData> LoadPlayer(string playerId)
    {
        var result = new CityPlayerData();
        lock (gate)
        {
            if (data.Accounts.TryGetValue(playerId, out var entry))
            {
                result.Account = JsonConvert.DeserializeObject<CityAccount>(entry.Json);
                result.Lock = entry.Lock;
            }
            if (data.Payouts.TryGetValue(playerId, out var payouts))
            {
                foreach (var json in payouts.Values)
                    result.Payouts.Add(JsonConvert.DeserializeObject<CityPayout>(json));
            }
        }
        return Task.FromResult(result);
    }

    public Task<string> SaveAccount(string playerId, CityAccount account, string writeLock) =>
        Save(data.Accounts, playerId, account, writeLock);

    public Task SavePayout(CityPayout payout)
    {
        lock (gate)
        {
            if (!data.Payouts.TryGetValue(payout.PlayerId, out var payouts))
                data.Payouts[payout.PlayerId] = payouts = new Dictionary<string, string>();
            payouts[payout.Id] = JsonConvert.SerializeObject(payout);
        }
        Changed?.Invoke();
        return Task.CompletedTask;
    }

    public Task DeletePayout(string playerId, string payoutId)
    {
        bool removed = false;
        lock (gate)
        {
            if (data.Payouts.TryGetValue(playerId, out var payouts))
                removed = payouts.Remove(payoutId);
        }
        if (removed) Changed?.Invoke();
        return Task.CompletedTask;
    }

    public Task<CityVersioned<CityLogoRecord>> LoadLogo(string hash) => Task.FromResult(Load<CityLogoRecord>(data.Logos, hash));

    public Task<string> SaveLogo(CityLogoRecord logo, string writeLock) => Save(data.Logos, logo.Hash, logo, writeLock);

    public Task<CityVersioned<CityReportRecord>> LoadReport(string hash) => Task.FromResult(Load<CityReportRecord>(data.Reports, hash));

    public Task<string> SaveReport(CityReportRecord report, string writeLock) => Save(data.Reports, report.Hash, report, writeLock);

    CityVersioned<T> Load<T>(Dictionary<string, Entry> table, string key) where T : class
    {
        lock (gate)
        {
            if (key == null || !table.TryGetValue(key, out var entry)) return null;
            return new CityVersioned<T>(JsonConvert.DeserializeObject<T>(entry.Json), entry.Lock);
        }
    }

    Task<string> Save(Dictionary<string, Entry> table, string key, object value, string writeLock)
    {
        string newLock;
        lock (gate)
        {
            table.TryGetValue(key, out var existing);
            if (existing == null && writeLock != null) throw new CityConflictException();
            if (existing != null && existing.Lock != writeLock) throw new CityConflictException();
            newLock = NewLock();
            table[key] = new Entry { Json = JsonConvert.SerializeObject(value), Lock = newLock };
        }
        Changed?.Invoke();
        return Task.FromResult(newLock);
    }

    // ---- Inspection and persistence ---------------------------------------------------------------------------

    public List<string> PlayerIds()
    {
        lock (gate) return data.Accounts.Keys.ToList();
    }

    public int PendingPayouts(string playerId)
    {
        lock (gate) return data.Payouts.TryGetValue(playerId, out var p) ? p.Count : 0;
    }

    public string Export()
    {
        lock (gate) return JsonConvert.SerializeObject(data);
    }

    public bool Import(string json)
    {
        if (string.IsNullOrEmpty(json)) return false;
        try
        {
            var loaded = JsonConvert.DeserializeObject<Snapshot>(json);
            if (loaded == null || loaded.Version < 2) return false;
            if (loaded.Districts == null) loaded.Districts = new Dictionary<string, Entry>();
            if (loaded.Accounts == null) loaded.Accounts = new Dictionary<string, Entry>();
            if (loaded.Payouts == null) loaded.Payouts = new Dictionary<string, Dictionary<string, string>>();
            if (loaded.Logos == null) loaded.Logos = new Dictionary<string, Entry>();
            if (loaded.Reports == null) loaded.Reports = new Dictionary<string, Entry>();
            lock (gate) data = loaded;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public void Clear()
    {
        lock (gate) data = new Snapshot();
        Changed?.Invoke();
    }
}
