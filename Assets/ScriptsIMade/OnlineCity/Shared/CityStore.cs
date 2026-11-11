using System;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>A stored value with the write lock it was read with (optimistic concurrency).</summary>
public class CityVersioned<T> where T : class
{
    public T Value;
    public string Lock;

    public CityVersioned(T value, string writeLock)
    {
        Value = value;
        Lock = writeLock;
    }
}

/// <summary>Someone else saved the item since it was read: read it again and retry.</summary>
public class CityConflictException : Exception
{
    public CityConflictException() : base("The data changed since it was read.") { }
}

/// <summary>A rule was broken: the message is shown to the player as is.</summary>
public class CityRuleException : Exception
{
    public CityRuleException(string message) : base(message) { }
}

/// <summary>A player's account in a city (null for players who never joined it) and the payouts waiting for them there.</summary>
public class CityPlayerData
{
    public CityAccount Account;
    public string Lock;
    public List<CityPayout> Payouts = new List<CityPayout>();
}

/// <summary>
/// Where ONE city lives (every city has its own store, so the six worlds never mix). On the server: Cloud Save
/// (districts and drawings = game data, reports = private game data, accounts and payouts = protected player data).
/// In the game's local test city: memory + a JSON file. Saves with a lock throw CityConflictException when the item
/// changed since it was read; a null lock means "create it".
/// </summary>
public interface ICityStore
{
    /// <summary>Districts that exist (missing ids are simply left out).</summary>
    Task<Dictionary<string, CityVersioned<CityDistrict>>> LoadDistricts(IList<string> districtIds);
    Task<string> SaveDistrict(CityDistrict district, string writeLock);

    Task<CityPlayerData> LoadPlayer(string playerId);
    Task<string> SaveAccount(string playerId, CityAccount account, string writeLock);

    /// <summary>Deliver a payout (a new key on the receiving player; writing the same id twice is harmless).</summary>
    Task SavePayout(CityPayout payout);
    Task DeletePayout(string playerId, string payoutId);

    /// <summary>A drawing by its hash (null when nobody saved it yet).</summary>
    Task<CityVersioned<CityLogoRecord>> LoadLogo(string hash);
    Task<string> SaveLogo(CityLogoRecord logo, string writeLock);

    /// <summary>The reports against a drawing (null when there are none).</summary>
    Task<CityVersioned<CityReportRecord>> LoadReport(string hash);
    Task<string> SaveReport(CityReportRecord report, string writeLock);
}

/// <summary>Tells players what changed in a city (server: Cloud Code push messages). Failures are logged, never thrown.</summary>
public interface ICityNotifier
{
    Task PlotsChanged(List<CityPlotChange> changes);
    Task PlayerNews(string playerId, CityMessage message);
}

public class CityNullNotifier : ICityNotifier
{
    public Task PlotsChanged(List<CityPlotChange> changes) => Task.CompletedTask;
    public Task PlayerNews(string playerId, CityMessage message) => Task.CompletedTask;
}
