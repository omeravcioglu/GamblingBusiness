using Newtonsoft.Json;

namespace OnlineCity.Tests;

/// <summary>One city in memory with a clock the tests move and cards the tests can stack.</summary>
public class TestWorld
{
    public long Now = 1_800_000_000L;
    public readonly Queue<int> Rolls = new();
    public readonly string City;
    public readonly CityMap Map;
    public readonly ICityStore Store;
    public readonly CityMemoryStore Memory;
    public readonly CityLogic Logic;
    readonly Random fallback = new(1234);
    int opCounter;

    public TestWorld(string city = "ist", Func<CityMemoryStore, ICityStore> wrap = null)
    {
        City = city;
        Map = CityLayout.Get(city);
        Memory = new CityMemoryStore();
        Store = wrap != null ? wrap(Memory) : Memory;
        Logic = new CityLogic(Store, city, null, () => Now, (min, max) =>
        {
            lock (Rolls)
            {
                return Rolls.Count > 0 ? Rolls.Dequeue() : fallback.Next(min, max);
            }
        });
    }

    public string Op() => "op" + Interlocked.Increment(ref opCounter).ToString("D8");

    public void Advance(long seconds) => Now += seconds;

    /// <summary>Stack the deck: the next cards drawn, in order (player, player, dealer, then hits and the dealer's draws).</summary>
    public void Deal(params int[] ranks)
    {
        lock (Rolls)
        {
            foreach (int rank in ranks) Rolls.Enqueue(rank);
        }
    }

    public static string Logo => "1|circle|E2B13C|star|1D2433|AB|1";

    public async Task Join(string player, string name)
    {
        var r = await Logic.Join(player, name, Logo);
        Assert.That(r.Ok, Is.True, r.Error);
    }

    /// <summary>Join and wire money in (a day at a time, like a real player).</summary>
    public async Task JoinWith(string player, string name, long money)
    {
        await Join(player, name);
        await Fund(player, money);
    }

    public async Task Fund(string player, long money)
    {
        while (money > 0)
        {
            long chunk = Math.Min(money, CityRules.DepositCapPerDay);
            var r = await Logic.Deposit(player, Op(), chunk);
            Assert.That(r.Ok, Is.True, r.Error);
            money -= chunk;
            if (money > 0) Advance(86401);
        }
    }

    public async Task<CityAccountView> Account(string player)
    {
        var r = await Logic.GetAccount(player);
        Assert.That(r.Ok, Is.True, r.Error);
        return r.Account;
    }

    public async Task<long> Balance(string player) => (await Account(player)).Balance;

    public async Task<Dictionary<string, CityDistrict>> Districts()
    {
        var loaded = await Memory.LoadDistricts(Map.DistrictIds.ToList());
        return loaded.ToDictionary(p => p.Key, p => p.Value.Value);
    }

    public async Task<CityPlot> Plot(CityPlotDef def)
    {
        var d = await Memory.LoadDistricts(new[] { def.DistrictId });
        if (!d.TryGetValue(def.DistrictId, out var district))
        {
            CityNpcs.SeedDistrict(Map, Map.DistrictsById[def.DistrictId]).Plots.TryGetValue(def.Id, out var seeded);
            return seeded;
        }
        district.Value.Plots.TryGetValue(def.Id, out var plot);
        return plot;
    }

    /// <summary>Plots nobody owns when the city opens.</summary>
    public List<CityPlotDef> FreePlots(string districtId = null)
    {
        var def = Map.DistrictsById[districtId ?? Map.Districts[0].Id];
        var seeded = CityNpcs.SeedDistrict(Map, def);
        return def.Plots.Where(p => !seeded.Plots.ContainsKey(p.Id)).ToList();
    }

    public List<CityPlotDef> NpcPlots(string districtId = null)
    {
        var def = Map.DistrictsById[districtId ?? Map.Districts[0].Id];
        var seeded = CityNpcs.SeedDistrict(Map, def);
        return def.Plots.Where(p => seeded.Plots.ContainsKey(p.Id)).ToList();
    }

    public CityPlotDef FreeSmall(int index = 0) =>
        FreePlots().Where(p => p.Size == CityPlotSize.Small).Skip(index).First();

    /// <summary>A player buys a free plot and waits out its shield, so it can be challenged.</summary>
    public async Task<CityPlotDef> OwnChallengeable(string player, int index = 0)
    {
        var def = FreeSmall(index);
        var r = await Logic.BuyPlot(player, Op(), def.Id, def.BasePrice);
        Assert.That(r.Ok, Is.True, r.Error);
        Advance(CityRules.ShieldHours * 3600L + 1);
        return def;
    }

    public string Dump() => JsonConvert.SerializeObject(JsonConvert.DeserializeObject(Memory.Export()), Formatting.Indented);
}

/// <summary>A store that can fail or slow down on purpose (crashes and races).</summary>
public class FlakyStore : ICityStore
{
    readonly ICityStore inner;
    public int FailNextDistrictSaves;
    public int FailNextAccountSaves;
    public int SkipAccountSavesBeforeFailing;
    public int SkipDistrictSavesBeforeFailing;
    public bool Yield;

    public FlakyStore(ICityStore inner) { this.inner = inner; }

    async Task Pause()
    {
        if (Yield) await Task.Delay(1);
    }

    public async Task<Dictionary<string, CityVersioned<CityDistrict>>> LoadDistricts(IList<string> districtIds)
    {
        await Pause();
        return await inner.LoadDistricts(districtIds);
    }

    public async Task<string> SaveDistrict(CityDistrict district, string writeLock)
    {
        await Pause();
        if (FailNextDistrictSaves > 0)
        {
            if (SkipDistrictSavesBeforeFailing > 0) SkipDistrictSavesBeforeFailing--;
            else
            {
                FailNextDistrictSaves--;
                throw new IOException("Simulated network failure (district)");
            }
        }
        return await inner.SaveDistrict(district, writeLock);
    }

    public async Task<CityPlayerData> LoadPlayer(string playerId)
    {
        await Pause();
        return await inner.LoadPlayer(playerId);
    }

    public async Task<string> SaveAccount(string playerId, CityAccount account, string writeLock)
    {
        await Pause();
        if (FailNextAccountSaves > 0)
        {
            if (SkipAccountSavesBeforeFailing > 0) SkipAccountSavesBeforeFailing--;
            else
            {
                FailNextAccountSaves--;
                throw new IOException("Simulated network failure (account)");
            }
        }
        return await inner.SaveAccount(playerId, account, writeLock);
    }

    public async Task SavePayout(CityPayout payout)
    {
        await Pause();
        await inner.SavePayout(payout);
    }

    public async Task DeletePayout(string playerId, string payoutId)
    {
        await Pause();
        await inner.DeletePayout(playerId, payoutId);
    }

    public Task<CityVersioned<CityLogoRecord>> LoadLogo(string hash) => inner.LoadLogo(hash);
    public Task<string> SaveLogo(CityLogoRecord logo, string writeLock) => inner.SaveLogo(logo, writeLock);
    public Task<CityVersioned<CityReportRecord>> LoadReport(string hash) => inner.LoadReport(hash);
    public Task<string> SaveReport(CityReportRecord report, string writeLock) => inner.SaveReport(report, writeLock);
}
