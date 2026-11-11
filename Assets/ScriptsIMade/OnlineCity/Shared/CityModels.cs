using System.Collections.Generic;
using Newtonsoft.Json;

// Online City data. This folder (OnlineCity/Shared) is compiled by Unity AND by the Cloud Code module
// (OnlineCityCCM links it), so the game and the server always agree. No UnityEngine code in here.
// Each of the six cities is its own world: its own districts, accounts (one per player per city) and logos.
// JSON names are short on purpose: one district item holds every owned plot of a district.

/// <summary>A plot somebody owns. Plots the city still owns have no entry (their defaults come from CityLayout).</summary>
public class CityPlot
{
    [JsonProperty("o")] public string OwnerId;     // player id, or "npc_<n>" for the rival companies the city starts with
    [JsonProperty("n")] public string OwnerName;   // company name shown on the plot
    [JsonProperty("g")] public string Logo;        // CityLogo code, or a pixel logo reference ("p|<hash>")
    [JsonProperty("pp")] public long PricePaid;    // a challenger stakes 1.5x this
    [JsonProperty("at")] public long AcquiredAt;   // unix seconds
    [JsonProperty("sh")] public long ShieldUntil;  // nobody can challenge for it before this time
    [JsonProperty("df")] public int Defended;      // challengers who lost at this plot since its owner got it

    public CityPlot Clone() { return (CityPlot)MemberwiseClone(); }
}

/// <summary>
/// Money (or news) for a player, written in the same atomic save as the district change that caused it and delivered
/// afterwards as a payout key on that player. Receivers apply each id once, so delivering twice is harmless.
/// </summary>
public class CityPayout
{
    [JsonProperty("id")] public string Id;
    [JsonProperty("to")] public string PlayerId;
    [JsonProperty("a")] public long Amount;        // >= 0
    [JsonProperty("k")] public string Kind;        // CityMessageKind
    [JsonProperty("m")] public string Text;        // inbox line
    [JsonProperty("p")] public string PlotId;
    [JsonProperty("g")] public string Logo;        // LogoHidden: the logo that was hidden
    [JsonProperty("t")] public long Time;
}

/// <summary>Everything that changes in one district: one Cloud Save item per district, saved with a write lock.</summary>
public class CityDistrict
{
    [JsonProperty("d")] public string Id;
    [JsonProperty("v")] public long Version;
    [JsonProperty("plots")] public Dictionary<string, CityPlot> Plots = new Dictionary<string, CityPlot>();
    [JsonProperty("outbox")] public List<CityPayout> Outbox = new List<CityPayout>();
    [JsonProperty("ops")] public List<string> RecentOps = new List<string>(); // committed "player:op" ids (retries, recovery)

    public void EnsureLists()
    {
        if (Plots == null) Plots = new Dictionary<string, CityPlot>();
        if (Outbox == null) Outbox = new List<CityPayout>();
        if (RecentOps == null) RecentOps = new List<string>();
    }
}

/// <summary>Money set aside for an operation that is still being committed to a district (recovered if it never was).</summary>
public class CityHold
{
    [JsonProperty("id")] public string OpId;
    [JsonProperty("d")] public string DistrictId;
    [JsonProperty("p")] public string PlotId;
    [JsonProperty("a")] public long Amount;
    [JsonProperty("k")] public string Kind;
    [JsonProperty("t")] public long Time;
}

public class CityStamp
{
    [JsonProperty("id")] public string Id;
    [JsonProperty("t")] public long Time;
    [JsonProperty("a")] public long Amount;
}

public static class CityMessageKind
{
    public const string TakenOver = "takeover";    // a challenger beat the dealer and took your plot (you were paid)
    public const string Defended = "defended";     // a challenger lost at your plot and their stake came to you
    public const string LogoHidden = "logo_hidden"; // players reported your drawing and it was taken down
    public const string Info = "info";
}

public class CityMessage
{
    [JsonProperty("id")] public string Id;
    [JsonProperty("c")] public string CityId;
    [JsonProperty("t")] public long Time;
    [JsonProperty("k")] public string Kind;
    [JsonProperty("m")] public string Text;
    [JsonProperty("p")] public string PlotId;
    [JsonProperty("a")] public long Amount;
}

/// <summary>
/// A challenge for someone's plot: the stake left the challenger's balance when it started, a hand of blackjack
/// decides. Win: the plot changes hands and the owner is paid the stake. Lose: the owner is paid the stake anyway.
/// </summary>
public class CityChallenge
{
    [JsonProperty("id")] public string Id;          // operation id of the whole challenge
    [JsonProperty("p")] public string PlotId;
    [JsonProperty("d")] public string DistrictId;
    [JsonProperty("s")] public string Status;       // CityRules.GamePlaying / GameWon / GameLost / GameDone / GameSettled / GameVoid
    [JsonProperty("o")] public string OwnerId;      // owner when it started (a win is void if the plot changes hands)
    [JsonProperty("on")] public string OwnerName;
    [JsonProperty("stake")] public long Stake;
    [JsonProperty("gets")] public long OwnerGets;   // the stake after the city tax
    [JsonProperty("t")] public long StartedAt;
    [JsonProperty("x")] public long ExpiresAt;      // an unfinished hand stands on its own after this
    [JsonProperty("pc")] public List<int> PlayerCards = new List<int>(); // ranks 1..13 (ace = 1)
    [JsonProperty("dc")] public List<int> DealerCards = new List<int>();
    [JsonProperty("r")] public int Round;            // a push deals a new hand
    [JsonProperty("m")] public string Message;

    /// <summary>Still holding the stake: being played, or won/lost and not settled with the district yet.</summary>
    [JsonIgnore] public bool IsOpen => Status == CityRules.GamePlaying || Status == CityRules.GameWon || Status == CityRules.GameLost;
}

public class CityStats
{
    [JsonProperty("rent")] public long RentEarned;
    [JsonProperty("dep")] public long Deposited;
    [JsonProperty("wd")] public long Withdrawn;
    [JsonProperty("buy")] public int PlotsBought;     // from the City
    [JsonProperty("won")] public int ChallengesWon;
    [JsonProperty("lost")] public int ChallengesLost;
    [JsonProperty("taken")] public int PlotsTaken;    // plots challengers won from me
    [JsonProperty("def")] public int Defended;        // challengers who lost at my plots
    [JsonProperty("defIn")] public long DefenseIncome;
    [JsonProperty("saleIn")] public long TakeoverIncome;
}

/// <summary>A player's account in one city (Cloud Save, Protected: only the server writes it, only the player reads it).</summary>
public class CityAccount
{
    [JsonProperty("name")] public string CompanyName;
    [JsonProperty("logo")] public string Logo;
    [JsonProperty("bal")] public long Balance;
    [JsonProperty("joined")] public long JoinedAt;
    [JsonProperty("rentAt")] public long RentCollectedAt;
    [JsonProperty("profAt")] public long ProfileChangedAt;
    [JsonProperty("deps")] public List<CityStamp> RecentDeposits = new List<CityStamp>();
    [JsonProperty("wds")] public List<CityStamp> RecentWithdrawals = new List<CityStamp>();
    [JsonProperty("ops")] public List<string> RecentOps = new List<string>();   // applied op and payout ids
    [JsonProperty("holds")] public List<CityHold> Holds = new List<CityHold>();
    [JsonProperty("inbox")] public List<CityMessage> Inbox = new List<CityMessage>();
    [JsonProperty("game")] public CityChallenge Game;
    [JsonProperty("stats")] public CityStats Stats = new CityStats();
    [JsonProperty("seen")] public long InboxSeenAt;

    public void EnsureLists()
    {
        if (RecentDeposits == null) RecentDeposits = new List<CityStamp>();
        if (RecentWithdrawals == null) RecentWithdrawals = new List<CityStamp>();
        if (RecentOps == null) RecentOps = new List<string>();
        if (Holds == null) Holds = new List<CityHold>();
        if (Inbox == null) Inbox = new List<CityMessage>();
        if (Stats == null) Stats = new CityStats();
    }
}

/// <summary>What the server tells a player about their own account in a city. Plot-derived numbers (rent ready,
/// plots owned) are computed by the client from the map with the same CityRules code.</summary>
public class CityAccountView
{
    [JsonProperty("id")] public string PlayerId;
    [JsonProperty("city")] public string CityId;
    [JsonProperty("joined")] public bool Joined;
    [JsonProperty("name")] public string CompanyName;
    [JsonProperty("logo")] public string Logo;
    [JsonProperty("bal")] public long Balance;
    [JsonProperty("held")] public long InPlay;            // a stake on the table (or money being committed)
    [JsonProperty("rentAt")] public long RentCollectedAt;
    [JsonProperty("depLeft")] public long DepositLeft;    // can still be wired in (rolling 24 h)
    [JsonProperty("depReset")] public long DepositResetAt;
    [JsonProperty("profAt")] public long ProfileChangedAt;
    [JsonProperty("inbox")] public List<CityMessage> Inbox = new List<CityMessage>();
    [JsonProperty("unread")] public int Unread;
    [JsonProperty("game")] public CityChallenge Game;
    [JsonProperty("stats")] public CityStats Stats = new CityStats();
    [JsonProperty("now")] public long ServerTime;
}

/// <summary>A plot as it is after an operation (Plot null = back to the city), so maps update without reloading.</summary>
public class CityPlotChange
{
    [JsonProperty("d")] public string DistrictId;
    [JsonProperty("p")] public string PlotId;
    [JsonProperty("plot")] public CityPlot Plot;
}

/// <summary>Reply of every server call.</summary>
public class CityResult
{
    [JsonProperty("ok")] public bool Ok;
    [JsonProperty("err")] public string Error;
    [JsonProperty("msg")] public string Message;
    [JsonProperty("acct")] public CityAccountView Account;
    [JsonProperty("amt")] public long Amount;
    [JsonProperty("game")] public CityChallenge Game;
    [JsonProperty("changes")] public List<CityPlotChange> Changes = new List<CityPlotChange>();

    public static CityResult Fail(string error) { return new CityResult { Ok = false, Error = error }; }
}

/// <summary>Live update pushed to every connected player when plots of a city change.</summary>
public class CityPush
{
    [JsonProperty("city")] public string CityId;
    [JsonProperty("changes")] public List<CityPlotChange> Changes = new List<CityPlotChange>();
    [JsonProperty("reload")] public bool Reload;   // too many changes for one push: reload the city
}

/// <summary>A player drawing (pixel logo), stored once per city under its hash; plots point at it with "p|hash".</summary>
public class CityLogoRecord
{
    [JsonProperty("h")] public string Hash;
    [JsonProperty("px")] public string Pixels;     // CityPixelLogo code; null once hidden
    [JsonProperty("o")] public string OwnerId;     // who drew it first
    [JsonProperty("t")] public long Time;
    [JsonProperty("hid")] public bool Hidden;      // taken down after reports: can't be used again
}

/// <summary>Who reported a drawing (server only: players never see who reported them).</summary>
public class CityReportRecord
{
    [JsonProperty("h")] public string Hash;
    [JsonProperty("by")] public List<string> Reporters = new List<string>();
    [JsonProperty("done")] public bool Handled;
}
