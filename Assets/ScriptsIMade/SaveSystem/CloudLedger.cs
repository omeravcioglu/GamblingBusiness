using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The cloud save's ledger (ISaveParticipant "cloudLedger", on the CloudSaveSync object): the money events the server can
/// check (SaveRules), recorded since the last accepted upload, each with a running index within the run (1, 2, 3...).
/// - Gambles: every GambleLedger.Gambled (Source = its source, Detail = the casino game, Stake / Payout, ValueBefore).
/// - Risk cards: EventManager.RiskCardSettled (Source = "high" / "balanced" / "low", Good, ValueBefore, Change).
/// - Stock sales: StockMarketManager.StockSold (Kind "trade", Source = symbol, Stake = cost basis of the shares sold at
///   their average buy price, fees not included; Payout = proceeds after the fee). Buys aren't recorded: they only
///   lower the company value.
/// - It is saved inside the save itself, so what an upload sends always matches that save's money (CloudSaveSync reads
///   the entries from the save file it uploads, not from here). Accepted entries are pruned (index at or below the
///   server's meta.ledger); a new run or a downloaded save starts it over.
/// - Capped: past MaxEntries the oldest gambles are folded into one Summary entry per (source, game) - sums of Stake,
///   Payout and ValueBefore, Count and Wins - down to about CompactTo entries; if that isn't enough, the oldest trades
///   of each symbol are merged the same way (still Kind "trade", Count > 1). Risk cards are never folded.
///   An upload carries the ledger twice, both packed (~3x smaller): inside the packed save and as the request's LedgerGz.
///   At the 3,000 cap that is roughly 0.3 MB, and every request is measured against SaveCloud.MaxRequestBytes before it
///   goes. A normal upload window holds a few dozen entries; only long offline play reaches the cap.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("")]
public sealed class CloudLedger : MonoBehaviour, ISaveParticipant
{
    public const string Key = "cloudLedger";
    /// <summary>More entries than this are compacted (a normal upload window has a few dozen).</summary>
    public const int MaxEntries = 3000;
    /// <summary>What compaction brings the count down to (plus one summary per source and game).</summary>
    public const int CompactTo = 2000;

    /// <summary>One entry as saved (short names: the save holds up to MaxEntries of them). Mirrors LedgerEntry.</summary>
    [Serializable]
    public class Entry
    {
        public long i;          // running index within the run
        public string k;        // LedgerKinds
        public string src;
        public string det;
        public double stake;
        public double pay;
        public double before;
        public double change;
        public bool good;
        public int day;
        public int n = 1;       // gambles folded into a summary (1 otherwise)
        public int wins;

        public LedgerEntry ToContract()
        {
            return new LedgerEntry
            {
                Index = i,
                Kind = k,
                Source = src,
                Detail = string.IsNullOrEmpty(det) ? null : det,
                Stake = stake,
                Payout = pay,
                ValueBefore = before,
                Change = change,
                Good = good,
                Day = day,
                Count = Math.Max(1, n),
                Wins = wins
            };
        }
    }

    [Serializable]
    class State
    {
        public int version = 1;
        public string runId;
        public long next = 1;
        public List<Entry> entries = new List<Entry>();
    }

    public static CloudLedger Instance { get; private set; }

    State state = new State();

    /// <summary>Entries not yet accepted by the server.</summary>
    public int Count => state.entries.Count;
    /// <summary>The index the next entry gets.</summary>
    public long NextIndex => state.next;
    /// <summary>The run these entries belong to (may be empty for a company that hasn't saved yet).</summary>
    public string RunId => state.runId;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        // Entering Play mode without a domain reload keeps statics
        Instance = null;
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
        // Static events on the other side, so the hooks live as long as this object (-= first: never twice)
        GambleLedger.Gambled -= OnGambled;
        GambleLedger.Gambled += OnGambled;
        EventManager.RiskCardSettled -= OnRiskCardSettled;
        EventManager.RiskCardSettled += OnRiskCardSettled;
        StockMarketManager.StockSold -= OnStockSold;
        StockMarketManager.StockSold += OnStockSold;
    }

    void OnDestroy()
    {
        if (Instance != this) return;
        GambleLedger.Gambled -= OnGambled;
        EventManager.RiskCardSettled -= OnRiskCardSettled;
        StockMarketManager.StockSold -= OnStockSold;
        Instance = null;
    }

    // ------------------------------------------------------------------
    // Recording
    // ------------------------------------------------------------------

    void OnGambled(GambleRecord record)
    {
        Add(new Entry
        {
            k = LedgerKinds.Gamble,
            src = record.source,
            det = record.detail,
            stake = record.stake,
            pay = record.payout,
            before = record.companyValueBefore,
            day = record.day,
            wins = record.IsWin ? 1 : 0
        });
    }

    void OnRiskCardSettled(CardRisk risk, bool good, double valueBefore, double change)
    {
        Add(new Entry
        {
            k = LedgerKinds.Card,
            src = RiskName(risk),
            good = good,
            before = Usable(valueBefore),
            change = double.IsNaN(change) || double.IsInfinity(change) ? 0.0 : change,
            day = CurrentDay()
        });
    }

    void OnStockSold(string symbol, long shares, double proceeds, double costBasis)
    {
        CompanyManager company = CompanyManager.Instance;
        double after = company != null ? company.companyValue : 0.0;
        Add(new Entry
        {
            k = LedgerKinds.Trade,
            src = symbol,
            stake = Usable(costBasis),
            pay = Usable(proceeds),
            before = Math.Max(0.0, after - Usable(proceeds)), // the event fires after the proceeds came in
            day = CurrentDay()
        });
    }

    /// <summary>
    /// A manager decision moved money (LedgerKinds.Manager, recorded by ManagerSystem): Source = situation id, Detail =
    /// choice id, Stake = money put at risk (0 if none), Change = the company value change its one-off money effects applied
    /// (the decision's gambles report to GambleLedger themselves and aren't in Change), ValueBefore. No-op without a ledger.
    /// </summary>
    public static void RecordManager(string situation, string choice, double stake, double change, double valueBefore)
    {
        CloudLedger ledger = Instance;
        if (ledger == null) return;
        ledger.Add(new Entry
        {
            k = LedgerKinds.Manager,
            src = situation,
            det = choice,
            stake = Usable(stake),
            before = Usable(valueBefore),
            change = double.IsNaN(change) || double.IsInfinity(change) ? 0.0 : change,
            good = change > 0.0,
            day = CurrentDay()
        });
    }

    /// <summary>The risk level names the server expects.</summary>
    public static string RiskName(CardRisk risk)
    {
        switch (risk)
        {
            case CardRisk.HighRisk: return "high";
            case CardRisk.LowRisk: return "low";
            default: return "balanced";
        }
    }

    void Add(Entry entry)
    {
        if (entry == null) return;
        if (string.IsNullOrEmpty(state.runId))
            state.runId = CloudSaveSync.CurrentRunId;
        entry.i = state.next++;
        state.entries.Add(entry);
        if (state.entries.Count > MaxEntries)
            Compact();
    }

    // ------------------------------------------------------------------
    // Server bookkeeping (CloudSaveSync)
    // ------------------------------------------------------------------

    /// <summary>Drop the entries the server has counted (index at or below <paramref name="acceptedIndex"/>).</summary>
    public void Prune(long acceptedIndex)
    {
        if (acceptedIndex <= 0) return;
        state.entries.RemoveAll(e => e == null || e.i <= acceptedIndex);
        if (state.next <= acceptedIndex)
            state.next = acceptedIndex + 1;
    }

    /// <summary>Start over for a run (a new company, a downloaded save): no entries, the next index after the server's.</summary>
    public void ResetForRun(string runId, long acceptedIndex)
    {
        state = new State { runId = runId, next = Math.Max(1L, acceptedIndex + 1) };
    }

    /// <summary>
    /// Take the entries from the local save file (at boot, and after CloudSaveSync replaced the file), so a save written
    /// before the game scene loads (the menu's quit save) keeps them.
    /// </summary>
    public void LoadFromLocalSave()
    {
        State loaded = null;
        string runId = null;
        if (SaveSystem.TryReadLocalSave(out SaveData data) && data != null && data.currentDay > 0)
        {
            loaded = Parse(FindExtra(data, Key));
            runId = data.runId;
        }
        if (loaded == null)
            loaded = new State();
        if (string.IsNullOrEmpty(loaded.runId))
            loaded.runId = !string.IsNullOrEmpty(runId) ? runId : CloudSaveSync.CurrentRunId;
        state = loaded;
        if (state.entries == null) state.entries = new List<Entry>();
        Prune(CloudSaveSync.KnownLedgerIndex(state.runId));
    }

    /// <summary>The not yet counted entries of a save (what an upload of that save sends), oldest first.</summary>
    public static List<LedgerEntry> EntriesInSave(SaveData data, long afterIndex)
    {
        var result = new List<LedgerEntry>();
        State saved = Parse(FindExtra(data, Key));
        if (saved == null || saved.entries == null) return result;
        // Entries of another run explain nothing about this save's money
        if (!string.IsNullOrEmpty(saved.runId) && data != null && !string.IsNullOrEmpty(data.runId) && saved.runId != data.runId)
            return result;
        foreach (Entry entry in saved.entries)
        {
            if (entry != null && entry.i > afterIndex && !string.IsNullOrEmpty(entry.k))
                result.Add(entry.ToContract());
        }
        result.Sort((a, b) => a.Index.CompareTo(b.Index));
        return result;
    }

    // ------------------------------------------------------------------
    // ISaveParticipant
    // ------------------------------------------------------------------

    public string SaveKey => Key;

    public string CaptureState()
    {
        if (string.IsNullOrEmpty(state.runId))
            state.runId = CloudSaveSync.CurrentRunId;
        return JsonUtility.ToJson(state);
    }

    public void RestoreState(string json)
    {
        State loaded = Parse(json);
        if (loaded == null) return;
        if (loaded.entries == null) loaded.entries = new List<Entry>();
        state = loaded;
        Prune(CloudSaveSync.KnownLedgerIndex(state.runId));
    }

    // ------------------------------------------------------------------
    // Compaction
    // ------------------------------------------------------------------

    void Compact()
    {
        int before = state.entries.Count;
        Fold(e => e.k == LedgerKinds.Gamble || e.k == LedgerKinds.Summary, LedgerKinds.Summary);
        if (state.entries.Count > MaxEntries)
            Fold(e => e.k == LedgerKinds.Trade, LedgerKinds.Trade);
        Debug.Log("[CloudSave] Ledger compacted: " + before + " -> " + state.entries.Count + " entries (older gambles folded into summaries).");
        if (state.entries.Count > MaxEntries)
            Debug.LogWarning("[CloudSave] The ledger still holds " + state.entries.Count + " entries (risk cards are never folded).");
    }

    /// <summary>Fold the oldest entries <paramref name="which"/> picks, one sum per (source, detail), until about CompactTo remain.</summary>
    void Fold(Predicate<Entry> which, string foldedKind)
    {
        int toFold = state.entries.Count - CompactTo;
        if (toFold <= 0) return;

        var sums = new Dictionary<string, Entry>();
        var created = new List<Entry>();
        var kept = new List<Entry>(state.entries.Count);
        foreach (Entry entry in state.entries) // oldest first
        {
            if (entry == null) continue;
            if (toFold <= 0 || !which(entry))
            {
                kept.Add(entry);
                continue;
            }
            string key = (entry.src ?? "") + "|" + (entry.det ?? "");
            if (!sums.TryGetValue(key, out Entry sum))
            {
                sum = new Entry { k = foldedKind, src = entry.src, det = entry.det, n = 0 };
                sums[key] = sum;
                created.Add(sum);
            }
            // The newest folded index: unique (its entry is gone) and above anything the server counted
            sum.i = Math.Max(sum.i, entry.i);
            sum.stake += entry.stake;
            sum.pay += entry.pay;
            sum.before += entry.before;
            sum.change += entry.change;
            sum.n += Math.Max(1, entry.n);
            sum.wins += entry.wins;
            sum.day = Math.Max(sum.day, entry.day);
            toFold--;
        }
        kept.AddRange(created);
        kept.Sort((a, b) => a.i.CompareTo(b.i));
        state.entries = kept;
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    static State Parse(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            State parsed = JsonUtility.FromJson<State>(json);
            if (parsed != null && parsed.entries == null) parsed.entries = new List<Entry>();
            return parsed;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[CloudSave] Couldn't read the saved ledger: " + e.Message);
            return null;
        }
    }

    static string FindExtra(SaveData data, string key)
    {
        if (data == null || data.extras == null) return null;
        foreach (ExtraSaveEntry extra in data.extras)
        {
            if (extra != null && extra.key == key) return extra.json;
        }
        return null;
    }

    static int CurrentDay()
    {
        CompanyManager company = CompanyManager.Instance;
        return company != null ? company.currentDay : 0;
    }

    static double Usable(double amount)
    {
        return double.IsNaN(amount) || double.IsInfinity(amount) || amount < 0.0 ? 0.0 : amount;
    }
}
