using System;
using UnityEngine;

/// <summary>One settled voluntary gamble, as reported to GambleLedger.</summary>
[Serializable]
public struct GambleRecord
{
    /// <summary>Where the bet was made: one of the GambleLedger.Source... names (e.g. "upgrade-blackjack", "tilt").</summary>
    public string source;
    /// <summary>Money the player put at risk.</summary>
    public double stake;
    /// <summary>
    /// Everything that came back: 0 = lost, the stake = even (a push, a refund), more = won. Something bought with the
    /// bet counts at its price (an upgrade won at blackjack came back as its cost).
    /// </summary>
    public double payout;
    /// <summary>Company value just before the stake was taken.</summary>
    public double companyValueBefore;
    /// <summary>Which game, for sources with several (the casino: GambleLedger.DetailSlots, DetailBlackjack, DetailCoinFlip); null otherwise.</summary>
    public string detail;
    /// <summary>In-game day it was settled on (0 without a company).</summary>
    public int day;
    /// <summary>Time.realtimeSinceStartup when it was reported.</summary>
    public float realTime;

    public double Net => payout - stake;
    public bool IsLoss => payout < stake - 0.005;
    public bool IsWin => payout > stake + 0.005;
    /// <summary>What the player is down on this bet (0 for an even or won bet).</summary>
    public double AmountLost => Math.Max(0.0, stake - payout);
    /// <summary>The stake as a share of the company value before it (1 = everything; 1 when the value is unknown).</summary>
    public double StakeShare => companyValueBefore > 0.0 ? stake / companyValueBefore : (stake > 0.0 ? 1.0 : 0.0);
}

/// <summary>
/// The single entry point every voluntary gamble reports to once its outcome is known:
/// Report(source, stake, payout, companyValueBefore) raises Gambled with a GambleRecord. The heat meter (HeatMeter)
/// turns every bet into heat and the "Win it back?" prompt (TiltPromptView) watches for losses; anything else (run stats,
/// a HUD) can listen too.
/// - Hooked here: BuildGambleView.GambleSettled (the build budget's coin flip and blackjack hand).
///   The casino's games (slots, blackjack, coin flip) report each settled round themselves as SourceCasino
///   (CasinoManager.ReportRound), OfficeUpgradeManager reports its blackjack and random-chance upgrades itself, the tilt
///   prompt's flip reports as SourceTilt, and new gambles (build mode furniture...) call Report directly.
/// - Only voluntary gambles belong here. Risk cards are forced events: EventManager tells HeatMeter.OnRiskCardChosen.
/// </summary>
public static class GambleLedger
{
    public const string SourceBuildGamble = "build-gamble";
    public const string SourceUpgradeBlackjack = "upgrade-blackjack";
    public const string SourceUpgradeChance = "upgrade-chance";
    public const string SourceCasino = "casino";
    public const string SourceFurniture = "furniture";
    /// <summary>The tilt prompt's own "Win it back" flip (never starts another prompt by itself).</summary>
    public const string SourceTilt = "tilt";

    /// <summary>GambleRecord.detail of the casino's games (the cloud save's server checks each game's payouts and odds).</summary>
    public const string DetailSlots = "slots";
    public const string DetailBlackjack = "blackjack";
    public const string DetailCoinFlip = "coinflip";

    /// <summary>A gamble was settled. Raised once per Report, each listener on its own (one that throws can't stop the rest).</summary>
    public static event Action<GambleRecord> Gambled;

    /// <summary>Write every reported gamble to the Console (handy for balancing).</summary>
    public static bool LogReports = true;

    /// <summary>The last gamble reported (default until RecordCount > 0).</summary>
    public static GambleRecord LastRecord { get; private set; }
    /// <summary>Gambles reported since the game started.</summary>
    public static int RecordCount { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        // Entering Play mode without a domain reload keeps statics: start every session clean
        Gambled = null;
        LogReports = true;
        LastRecord = default;
        RecordCount = 0;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void HookSources()
    {
        // Static events on both sides, so the hooks live for the whole session (-= first: never twice)
        BuildGambleView.GambleSettled -= OnBuildGambleSettled;
        BuildGambleView.GambleSettled += OnBuildGambleSettled;
    }

    // ------------------------------------------------------------------
    // Reporting
    // ------------------------------------------------------------------

    /// <summary>
    /// Report a settled voluntary gamble: the <paramref name="stake"/> put at risk, the <paramref name="payout"/> that came
    /// back (0 = lost, the stake = even, more = won; something bought with it counts at its price) and the company value
    /// just before the stake was taken. Call it once, when the outcome shows. Negative or broken amounts count as 0.
    /// <paramref name="detail"/> names the game when the source has several (the casino: DetailSlots...).
    /// </summary>
    public static void Report(string source, double stake, double payout, double companyValueBefore, string detail = null)
    {
        stake = Usable(stake);
        payout = Usable(payout);
        companyValueBefore = Usable(companyValueBefore);
        if (stake <= 0.0 && payout <= 0.0) return;

        CompanyManager company = CompanyManager.Instance;
        var record = new GambleRecord
        {
            source = string.IsNullOrWhiteSpace(source) ? "unknown" : source.Trim(),
            stake = stake,
            payout = payout,
            companyValueBefore = companyValueBefore,
            detail = string.IsNullOrWhiteSpace(detail) ? null : detail.Trim(),
            day = company != null ? company.currentDay : 0,
            realTime = Time.realtimeSinceStartup
        };
        LastRecord = record;
        RecordCount++;

        if (LogReports)
            Debug.Log("[GambleLedger] " + record.source + (record.detail != null ? " (" + record.detail + ")" : "") + ": staked " + UIFormat.Money(stake) + ", " + UIFormat.Money(payout) +
                      " came back (" + UIFormat.SignedMoney(record.Net) + "), company value before " + UIFormat.Money(companyValueBefore));

        Action<GambleRecord> handlers = Gambled;
        if (handlers == null) return;
        foreach (Action<GambleRecord> handler in handlers.GetInvocationList())
        {
            try { handler(record); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }

    /// <summary>How a source reads in an English sentence: "a blackjack upgrade", "the casino", "a gamble" (for the Console log).</summary>
    public static string Describe(string source)
    {
        switch (source)
        {
            case SourceBuildGamble: return "the build budget gamble";
            case SourceUpgradeBlackjack: return "a blackjack upgrade";
            case SourceUpgradeChance: return "a random-chance upgrade";
            case SourceCasino: return "the casino";
            case SourceFurniture: return "a furniture gamble";
            case SourceTilt: return "the last flip";
            default: return "a gamble";
        }
    }

    /// <summary>
    /// The source for the player, in the current language (heat.source.* in the "Heat" String Table), worded to fit the
    /// "Win it back?" sentences ("You just lost $12,000 on {source}"; Turkish: "kumarhanede").
    /// </summary>
    public static string DescribeLocalized(string source)
    {
        string id;
        switch (source)
        {
            case SourceBuildGamble: id = "build_gamble"; break;
            case SourceUpgradeBlackjack: id = "upgrade_blackjack"; break;
            case SourceUpgradeChance: id = "upgrade_chance"; break;
            case SourceCasino: id = "casino"; break;
            case SourceFurniture: id = "furniture"; break;
            case SourceTilt: id = "tilt"; break;
            default: id = "other"; break;
        }
        return L10n.Get(HeatMeter.Table, "heat.source." + id);
    }

    static double Usable(double amount)
    {
        return double.IsNaN(amount) || double.IsInfinity(amount) || amount < 0.0 ? 0.0 : amount;
    }

    static double CompanyValueNow()
    {
        CompanyManager company = CompanyManager.Instance;
        return company != null ? Math.Max(0.0, company.companyValue) : 0.0;
    }

    // ------------------------------------------------------------------
    // Hooked sources
    // ------------------------------------------------------------------

    // The build budget's flip or hand: the bet left the company when the game started; a push has already put it back
    static void OnBuildGambleSettled(double stake, double paidBack)
    {
        double now = CompanyValueNow();
        bool refunded = Math.Abs(paidBack - stake) < 0.01;
        Report(SourceBuildGamble, stake, paidBack, refunded ? now : now + stake);
    }

    /// <summary>
    /// Nothing to do any more: the casino's games report their rounds themselves, so its bets and winnings
    /// (CasinoManager.MoneyMoved) are no longer paired into rounds here. Kept because HeatMeter still calls it every frame.
    /// </summary>
    public static void Poll(float roundSeconds)
    {
    }
}
