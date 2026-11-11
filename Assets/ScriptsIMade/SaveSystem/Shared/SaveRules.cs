using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

/// <summary>
/// The server's checks on a cloud save, pure and shared: the GameSave Cloud Code module runs them on every submit and the
/// client's local test server runs the same code in the Editor. No I/O, no clock of its own (serverNow is passed in).
/// See SaveProtocol.cs for the shapes and codes, SaveCalibration.cs for every bound and where it comes from.
///
/// Hard rejections (the stored copy is unchanged; the client adopts the server copy): invalid, no_run, ended, stale, time;
/// error when the stored meta itself can't be read (the client keeps its save and tries later).
/// Soft checks (the save is stored; a flag is kept in meta.Flags, one per reason, updated when it happens again). A flag only
/// blocks Online City deposits, but honest play must essentially never raise one, so every bound is an honest maximum:
/// - ledger: a gamble / card / trade that can't happen: a payout above its multiple, a stake or value-before above what the
///   company can have had at that point (the running bound, see WalkLedger), more cards than the clock allows, more new
///   staff than hiring clicks allow; a manager decision that adds money other than an investor's offer (or above its
///   amount, or while the last offer's share runs: CheckManager), more manager gambles than their cooldowns allow.
/// - luck: wins far above honest odds (z &gt; 5 over 50+ results) for gambles with known odds and for risk cards, and rare
///   payouts (slots 20x or more, card jackpots) more often than honest odds give once in a billion: over the whole run
///   (meta.Luck.Rare, so one fake jackpot per upload adds up) and within one upload by payout size (clusters).
/// - growth: the company's effective value (value plus its money in transit to or from the Online City, classified by the
///   server against the city's own records: see ClassifyPending) grew more than the ledger, the city's lifetime totals and
///   a generous income allowance explain (and no bankruptcy restart the save shows can explain it either), or the save's
///   run peak is above anything reachable. Never raised while the city accounts can't be read.
/// Busy (nothing stored, the client sends its newer save): the city booked a transfer moments ago that the save doesn't
/// list as pending, so the save may be from just before it (CityTransferSettleSeconds).
/// - import: the first save of a run the server never saw is richer than MaxPlausibleValue(day), or implausibly far in.
/// </summary>
public static class SaveRules
{
    /// <summary>A new company's money (CompanyManager.STARTING_VALUE, CompanyManager.cs:30).</summary>
    public const double StartingValue = 50000.0;
    /// <summary>Shortest in-game day in real seconds (CompanyManager: dayDuration = Random.Range(15f, 45f)).</summary>
    public const double MinSecondsPerDay = 15.0;
    /// <summary>Time rule: day - meta.day may be at most floor((elapsed + 60) / 15 x 1.1) + 3.</summary>
    public const double TimeRuleFactor = 1.1;
    public const int TimeRuleGraceDays = 3;
    /// <summary>
    /// Real seconds added to the time since the last accept: a save's day is read when it is written, but its upload (and
    /// the accept of the one before) can wait in flight and in retries, so the two ends of the window aren't the same clock.
    /// </summary>
    public const long TimeRuleGraceSeconds = 60;
    /// <summary>Luck flag: at least this many results...</summary>
    public const int LuckMinResults = 50;
    /// <summary>...and more than this many standard deviations above what honest odds expect.</summary>
    public const double LuckMaxZ = 5.0;
    /// <summary>Rare payouts (SaveGambleRule.RareMultiples, card jackpots) are flagged when honest odds give their count a smaller chance than this.</summary>
    public const double RareLuckMaxChance = 1e-9;
    /// <summary>
    /// Gamble rounds a second of real time can hold at most: the fastest round is a coin flip of at least 0.2 s
    /// (CoinFlipGame.FlipSeconds, CoinFlipGame.cs:81). Summaries claiming more rounds than the time since the last accept
    /// allows count as that many in the gamble tally (their win rate kept), so a made-up mountain of rounds can't dilute it.
    /// </summary>
    public const double MaxRoundsPerSecond = 5.0;
    /// <summary>Most ledger entries one submit may carry (the client folds old gambles into summaries long before).</summary>
    public const int MaxLedgerEntries = 20000;
    /// <summary>
    /// Largest SubmitRequest.LedgerGz (packed text) and what it may unpack to: MaxLedgerEntries entries are ~160 bytes each
    /// as JSON, ~3.2 MB; the client's own cap (CloudLedger.MaxEntries, 3,000) is ~0.5 MB.
    /// </summary>
    public const int MaxPackedLedgerBytes = 1024 * 1024;
    public const int MaxLedgerJsonBytes = 4 * 1024 * 1024;
    /// <summary>Run ids: 1-64 characters of A-Z a-z 0-9 - _.</summary>
    public const int MaxRunIdLength = 64;
    /// <summary>
    /// The one honest timing gap left once pending transfers are classified against the same city read as the totals: an
    /// upload already on its way when the player wires money. The client saves with the transfer pending before it calls
    /// the city (CityOnline.cs:494-541) and holds uploads while one is pending, so a save that doesn't list a transfer the
    /// city booked this recently may simply be from just before it. Such a submit answers busy instead of being judged:
    /// always for a withdrawal (the next save would show money the baseline already counted), and for a deposit when the
    /// growth check would flag. The client retries with its current save (CloudSaveSync: up to 5 retries, 2-10 s apart),
    /// which lists or reflects the transfer; a made-up save gains nothing by waiting, it is judged once the stamp is older.
    /// </summary>
    public const long CityTransferSettleSeconds = 20;
    /// <summary>
    /// A gamble or card that leaves the company at this value or less by its own numbers shows a bankruptcy (the hearing
    /// opens at $0.50, RunEndDirector.cs:38).
    /// </summary>
    public const double BrokeValue = 1.0;
    /// <summary>Text from the client (sources, symbols, build) is cut to this many characters in messages, logs and flag details.</summary>
    public const int MaxClientText = 40;

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // ================================================================================================================
    // Runs
    // ================================================================================================================

    /// <summary>
    /// Meta for a run that starts now (NewRun) when the player's previous meta isn't known: no time baseline (AcceptedAt 0)
    /// and city totals 0. See the overload with the previous meta, which the GameSave module uses.
    /// </summary>
    public static SaveMeta NewRunMeta(string runId, long serverNow)
    {
        return NewRunMeta(runId, serverNow, null, null);
    }

    /// <summary>
    /// Meta for a run that starts now with the player's current lifetime Online City totals as the baseline the next submits
    /// measure transfers from (unknown totals leave 0), when the previous meta isn't known (no time baseline).
    /// </summary>
    public static SaveMeta NewRunMeta(string runId, long serverNow, CityTotals city)
    {
        return NewRunMeta(runId, serverNow, city, null);
    }

    /// <summary>
    /// Meta for a run that starts now (NewRun). StartedAt = now; value, day and seq start over; flags and luck start clean.
    /// The time baseline (AcceptedAt) is not now: a New Game can start offline and reach the server only later, so the run's
    /// first submit is timed from the last moment the server heard from this player:
    /// - <paramref name="previous"/> (the player's meta, any run, ended or not): AcceptedAt = its AcceptedAt (the earliest
    ///   the new run could have started). Known v1 allowance: when that was days ago, the first submit's window is that wide.
    /// - no previous meta (or an unreadable one): AcceptedAt = 0, "no baseline": the run's first submit skips the time rule
    ///   and gets the import check (MaxPlausibleValue) instead, exactly like a submit from a player without any meta
    ///   (IsFirstSaveWithoutBaseline).
    /// The Online City's freshness rule (deposits need a save accepted recently) reads AcceptedAt: right after NewRun it is 0
    /// or old, so deposits wait for the new run's first accepted save. Intended.
    /// </summary>
    public static SaveMeta NewRunMeta(string runId, long serverNow, CityTotals city, SaveMeta previous)
    {
        if (IsUnreadableMeta(previous)) previous = null;
        var meta = new SaveMeta
        {
            RunId = runId,
            Seq = 0,
            Day = 0,
            Value = StartingValue,
            Peak = StartingValue,
            StartedAt = serverNow,
            AcceptedAt = previous != null ? Math.Max(0L, Math.Min(previous.AcceptedAt, serverNow)) : 0L
        };
        if (city != null && city.Known)
        {
            meta.CityDeposited = Math.Max(0L, city.Deposited);
            meta.CityWithdrawn = Math.Max(0L, city.Withdrawn);
        }
        return meta;
    }

    /// <summary>
    /// A run from NewRun without a time baseline (the player had no meta) that has no accepted save yet: its first submit
    /// is checked like an import (no time rule, MaxPlausibleValue, no growth baseline).
    /// </summary>
    public static bool IsFirstSaveWithoutBaseline(SaveMeta current)
    {
        return current != null && current.Seq == 0 && current.AcceptedAt <= 0;
    }

    /// <summary>
    /// Meta after EndRun: a copy of <paramref name="current"/> with Ended = true when <paramref name="runId"/> is its run;
    /// otherwise <paramref name="current"/> itself, unchanged (other runs are left alone).
    /// </summary>
    public static SaveMeta EndRunMeta(SaveMeta current, string runId, long serverNow)
    {
        if (current == null || string.IsNullOrEmpty(runId) || current.RunId != runId) return current;
        SaveMeta meta = CloneMeta(current);
        meta.Ended = true;
        return meta;
    }

    /// <summary>1-64 characters of A-Z a-z 0-9 - _.</summary>
    public static bool IsValidRunId(string runId)
    {
        if (string.IsNullOrEmpty(runId) || runId.Length > MaxRunIdLength) return false;
        foreach (char c in runId)
        {
            bool ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_';
            if (!ok) return false;
        }
        return true;
    }

    /// <summary>The stored meta is this run and it isn't over (NewRun is then a no-op).</summary>
    public static bool IsLiveRun(SaveMeta current, string runId)
    {
        return current != null && !current.Ended && !string.IsNullOrEmpty(runId) && current.RunId == runId;
    }

    /// <summary>
    /// EndRun from a copy that is up to date with the server (SaveCloud.FnEndRun): its chain continues from the server's last
    /// accepted save (meta.Seq ≤ <paramref name="baseSeq"/>), or its upload whose answer was lost is that save (meta.Seq ==
    /// <paramref name="inFlightSeq"/> and the stored save's SavePacking.Hash equals <paramref name="inFlightHash"/>).
    /// Otherwise another device moved the company on since this copy, and the run mustn't end (Stale).
    /// <paramref name="storedSave"/> is save_data as stored (packed, or JSON from before packing); null when there is none.
    /// </summary>
    public static bool IsUpToDateForEnd(SaveMeta current, long baseSeq, long inFlightSeq, string inFlightHash, string storedSave)
    {
        if (current == null) return false;
        if (current.Seq <= baseSeq) return true;
        if (current.Seq != inFlightSeq || string.IsNullOrEmpty(inFlightHash) || string.IsNullOrEmpty(storedSave)) return false;
        string json;
        try
        {
            json = SavePacking.Unpack(storedSave);
        }
        catch (Exception)
        {
            return false;   // An unreadable stored save proves nothing
        }
        return string.Equals(SavePacking.Hash(json), inFlightHash.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The stored meta is this run and it is over: NewRun with that id is refused (Ended), an ended run never comes back.</summary>
    public static bool IsEndedRun(SaveMeta current, string runId)
    {
        return current != null && current.Ended && !string.IsNullOrEmpty(runId) && current.RunId == runId;
    }

    /// <summary>
    /// NewRun from a client that last saw the server's meta.AcceptedAt = <paramref name="knownAcceptedAt"/> (0 = never synced)
    /// while the server's live run (another run, not over) was accepted after that: another device kept playing it since, so
    /// this New Game mustn't replace it (SaveCodes.Superseded, nothing changes).
    /// </summary>
    public static bool IsSuperseded(SaveMeta current, string runId, long knownAcceptedAt)
    {
        return current != null && IsValidRunId(current.RunId) && !current.Ended && current.RunId != runId && current.AcceptedAt > knownAcceptedAt;
    }

    /// <summary>
    /// A stored meta without a valid run id: never written like that (a damaged item). It isn't "no meta": a submit answers
    /// error instead of being taken as an import without a stale check; NewRun replaces it without a time baseline.
    /// </summary>
    public static bool IsUnreadableMeta(SaveMeta current)
    {
        return current != null && !IsValidRunId(current.RunId);
    }

    /// <summary>Most in-game days a save may be ahead of the last accepted one after <paramref name="elapsedSeconds"/>.</summary>
    public static long MaxDayAdvance(long elapsedSeconds)
    {
        return (long)Math.Floor(Math.Max(0L, elapsedSeconds) / MinSecondsPerDay * TimeRuleFactor) + TimeRuleGraceDays;
    }

    /// <summary>The richest an honest company can plausibly be on <paramref name="day"/> (import check; SaveCalibration.MaxPlausibleValue).</summary>
    public static double MaxPlausibleValue(int day)
    {
        return SaveCalibration.MaxPlausibleValue(day);
    }

    /// <summary>Deep copy (never null lists).</summary>
    public static SaveMeta CloneMeta(SaveMeta meta)
    {
        if (meta == null) return null;
        SaveMeta copy = JsonConvert.DeserializeObject<SaveMeta>(JsonConvert.SerializeObject(meta)) ?? new SaveMeta();
        if (copy.Flags == null) copy.Flags = new List<SaveFlag>();
        if (copy.Luck == null) copy.Luck = new LuckStats();
        if (copy.Luck.Gambles == null) copy.Luck.Gambles = new LuckTally();
        if (copy.Luck.Cards == null) copy.Luck.Cards = new LuckTally();
        if (copy.Luck.Rare == null) copy.Luck.Rare = new LuckTally();
        return copy;
    }

    // ================================================================================================================
    // Submit
    // ================================================================================================================

    /// <summary>
    /// Decide a submit against the stored meta (null = the player has none yet). Accepted: Decision.Meta is the meta to
    /// store with Decision.StoredSave (the packed save). Rejected: Decision.Meta is <paramref name="current"/> unchanged.
    /// A packed request (SaveGz) is unpacked into request.SaveJson first; a broken, oversized or bomb-like one is invalid.
    /// Same as the overload with no context (no previous save, no city transfer times).
    /// </summary>
    public static SubmitDecision EvaluateSubmit(SaveMeta current, SubmitRequest request, CityTotals city, long serverNow)
    {
        return EvaluateSubmit(current, request, city, serverNow, null);
    }

    /// <summary>
    /// Decide a submit with what else the server knows (<paramref name="context"/>): the save stored before this one (staff
    /// who left since, shares held, loans and the run's peak then) and the times of recent Online City transfers.
    /// </summary>
    public static SubmitDecision EvaluateSubmit(SaveMeta current, SubmitRequest request, CityTotals city, long serverNow, SaveSubmitContext context)
    {
        if (city == null) city = new CityTotals();
        if (IsUnreadableMeta(current))
            return Reject(SaveCodes.Error, "The server's save meta for this player has no valid run id; it has to be repaired before saves continue.", current);

        // ---- Request ----------------------------------------------------------------------------------------------
        if (request == null) return Reject(SaveCodes.Invalid, "No request.", current);
        if (!IsValidRunId(request.RunId)) return Reject(SaveCodes.Invalid, "The run id is missing or malformed.", current);
        if (double.IsNaN(request.Value) || double.IsInfinity(request.Value) || request.Value < 0.0)
            return Reject(SaveCodes.Invalid, "The company value isn't a number or is negative.", current);

        // The ledger: LedgerGz (packed, what clients send) takes precedence over Ledger and replaces it
        if (!string.IsNullOrEmpty(request.LedgerGz))
        {
            if (request.LedgerGz.Length > MaxPackedLedgerBytes)
                return Reject(SaveCodes.Invalid, "The packed ledger is " + request.LedgerGz.Length + " characters (the limit is " + MaxPackedLedgerBytes + ").", current);
            try
            {
                string ledgerJson = SavePacking.Unpack(request.LedgerGz, MaxLedgerJsonBytes);   // throws past the limit (bombs)
                request.Ledger = JsonConvert.DeserializeObject<List<LedgerEntry>>(ledgerJson) ?? new List<LedgerEntry>();
            }
            catch (Exception e)
            {
                return Reject(SaveCodes.Invalid, "The packed ledger can't be read (" + e.GetType().Name + ").", current);
            }
        }
        if (request.Ledger != null && request.Ledger.Count > MaxLedgerEntries)
            return Reject(SaveCodes.Invalid, "The ledger has " + request.Ledger.Count + " entries (the limit is " + MaxLedgerEntries + ").", current);

        // ---- The run and the chain --------------------------------------------------------------------------------
        bool import = current == null;
        if (!import)
        {
            if (request.RunId != current.RunId)
                return Reject(SaveCodes.NoRun, "This save is run " + request.RunId + "; the server's current run is " + current.RunId + ".", current);
            if (current.Ended)
                return Reject(SaveCodes.Ended, "Run " + current.RunId + " is over (Game Over).", current);
            if (request.Seq <= current.Seq || request.BaseSeq != current.Seq)
                return Reject(SaveCodes.Stale, "Another device got ahead: the server has save " + current.Seq + "; this is save " + request.Seq +
                                               " continuing from " + request.BaseSeq + ".", current);
        }
        // Checked like an import: no meta at all, or the first save of a run NewRun started without a time baseline
        bool importChecks = import || IsFirstSaveWithoutBaseline(current);

        // ---- The save itself: SaveGz (packed, what clients send) takes precedence over SaveJson -------------------
        if (!string.IsNullOrEmpty(request.SaveGz))
        {
            if (request.SaveGz.Length > SaveCloud.MaxPackedSaveBytes)
                return Reject(SaveCodes.Invalid, "The packed save is " + request.SaveGz.Length + " characters (the limit is " + SaveCloud.MaxPackedSaveBytes + ").", current);
            try
            {
                request.SaveJson = SavePacking.Unpack(request.SaveGz);   // throws past SaveCloud.MaxSaveBytes (zip bombs)
            }
            catch (Exception e)
            {
                return Reject(SaveCodes.Invalid, "The packed save can't be unpacked (" + e.GetType().Name + ").", current);
            }
        }
        SaveSnapshot save = SaveSnapshot.Parse(request.SaveJson);
        if (!save.Ok) return Reject(SaveCodes.Invalid, save.Error, current);
        if (save.CompanyValue < 0.0) return Reject(SaveCodes.Invalid, "The save's company value is negative.", current);
        if (save.CurrentDay < 0) return Reject(SaveCodes.Invalid, "The save's day is negative.", current);
        if (request.Day != save.CurrentDay)
            return Reject(SaveCodes.Invalid, "The request says day " + request.Day + ", the save day " + save.CurrentDay + ".", current);
        if (!ValuesMatch(request.Value, save.CompanyValue))
            return Reject(SaveCodes.Invalid, "The request says " + Money(request.Value) + ", the save " + Money(save.CompanyValue) + ".", current);

        // ---- Time --------------------------------------------------------------------------------------------------
        long elapsed = importChecks ? 0L : Math.Max(0L, serverNow - current.AcceptedAt);
        if (!importChecks)
        {
            if (request.Day < current.Day)
                return Reject(SaveCodes.Time, "Day " + request.Day + " is before the server's day " + current.Day + ".", current);
            long allowed = MaxDayAdvance(elapsed + TimeRuleGraceSeconds);
            if ((long)request.Day - current.Day > allowed)
                return Reject(SaveCodes.Time, "Day " + request.Day + " is " + ((long)request.Day - current.Day) + " days after the server's day " + current.Day +
                                              "; " + elapsed + " s (+" + TimeRuleGraceSeconds + " s for uploads) allow at most " + allowed + ".", current);
        }

        // ---- What gets stored: the packed text as sent, or the JSON packed here ---------------------------------------
        string stored = SavePacking.IsPacked(request.SaveGz) ? request.SaveGz : SavePacking.Pack(request.SaveJson);
        if (stored.Length > SaveCloud.MaxPackedSaveBytes)
            return Reject(SaveCodes.Invalid, "The save is " + stored.Length + " characters even packed (the limit is " + SaveCloud.MaxPackedSaveBytes + ").", current);

        // ---- Accepted: the soft checks ------------------------------------------------------------------------------
        var decision = new SubmitDecision { Code = SaveCodes.Accepted, StoredSave = stored };
        SaveMeta meta = import ? new SaveMeta { StartedAt = serverNow, Peak = 0.0 } : CloneMeta(current);
        var findings = new Findings();
        var rare = new RareLuck(meta.Luck.Rare);
        double value = save.CompanyValue;
        LedgerResult ledger;

        // The save's money in transit, classified against this submit's city read (the same read as the totals)
        SavePendingCheck pendingNow = city.Known
            ? ClassifyPending(save.PendingTransfers, context != null ? context.PlayerId : null, context != null ? context.CityTransfers : null,
                              context != null ? context.CityOps : null)
            : null;
        long recentDeposits = 0, recentWithdrawals = 0;
        if (city.Known) RecentUnlisted(save, context, serverNow, out recentDeposits, out recentWithdrawals);

        if (importChecks)
        {
            // No baseline: the ledger's own rules and the import bounds (no running bound, no growth check)
            if (recentWithdrawals > 0)
                return Reject(SaveCodes.Busy, SettleMessage(recentWithdrawals, false), current);
            long days = import ? Math.Max(0, request.Day) : (long)request.Day - current.Day;
            long countedUpTo = import ? 0L : current.LedgerIndex;
            ledger = WalkLedger(FreshEntries(request, countedUpTo), countedUpTo, null, meta.Luck, rare, save, import ? 0 : current.Day, days, -1L,
                                MarketHours(import ? null : current, request.Day, serverNow), findings, null, null, null,
                                Math.Max(save.RunPeak, save.CompanyValue));
            CheckImport(import, request.Day, save, findings);
        }
        else
        {
            decision.Growth = CheckRun(current, request, save, city, context, pendingNow, serverNow, elapsed, meta.Luck, rare, findings, out ledger);
            // A transfer the city booked moments ago that this save doesn't list: the save may be from just before it
            if (recentWithdrawals > 0)
                return Reject(SaveCodes.Busy, SettleMessage(recentWithdrawals, false), current);
            if (recentDeposits > 0 && decision.Growth.Unexplained)
                return Reject(SaveCodes.Busy, SettleMessage(recentDeposits, true), current);
        }

        if (meta.Luck.Gambles.Count >= LuckMinResults && meta.Luck.Gambles.Z > LuckMaxZ)
            findings.Add(SaveCodes.FlagLuck, "Gambles: " + Luck(meta.Luck.Gambles) + ".");
        if (meta.Luck.Cards.Count >= LuckMinResults && meta.Luck.Cards.Z > LuckMaxZ)
            findings.Add(SaveCodes.FlagLuck, "Risk cards: " + Luck(meta.Luck.Cards) + ".");
        rare.Check(findings);

        // ---- The new meta -------------------------------------------------------------------------------------------
        meta.Version = SaveCloud.ProtocolVersion;
        meta.RunId = request.RunId;
        meta.Seq = request.Seq;
        meta.Day = request.Day;
        meta.Value = value;
        meta.Peak = Math.Max(meta.Peak, value);
        meta.AcceptedAt = serverNow;
        meta.Ended = false;
        meta.LedgerIndex = Math.Max(meta.LedgerIndex, ledger.HighestIndex);
        if (city.Known)
        {
            // The new baseline, all from the same city read: lifetime totals and this save's money in transit
            meta.CityDeposited = Math.Max(0L, city.Deposited);
            meta.CityWithdrawn = Math.Max(0L, city.Withdrawn);
            meta.PendingDepositsUnbooked = pendingNow.UnbookedDeposits;
            meta.PendingWithdrawalsBooked = pendingNow.BookedWithdrawals;
        }
        // Unknown city: the old totals and the old pending sums stay (the next read measures from them)
        findings.ApplyTo(meta, decision, request.Seq, serverNow);

        decision.Meta = meta;
        decision.Message = findings.Count > 0 ? "Saved; flagged: " + string.Join(", ", findings.Reasons) + "." : "Saved.";
        return decision;
    }

    /// <summary>The first save of a run without a baseline: implausibly far in, or richer (value or run peak) than any honest company that day.</summary>
    static void CheckImport(bool import, int day, SaveSnapshot save, Findings findings)
    {
        string what = import ? "Imported run" : "First save of a run started without a time baseline";
        double plausible = MaxPlausibleValue(day);
        if (day > SaveCalibration.MaxPlausibleDay)
            findings.Add(SaveCodes.FlagImport, what + " at day " + day + ": no run gets that far (at most day " + SaveCalibration.MaxPlausibleDay + ").");
        if (save.CompanyValue > plausible)
            findings.Add(SaveCodes.FlagImport, what + " at day " + day + " with " + Money(save.CompanyValue) + " (plausible at most " + Money(plausible) + ").");
        else if (save.RunPeak > plausible)
            findings.Add(SaveCodes.FlagImport, what + " at day " + day + " claims a run peak of " + Money(save.RunPeak) + " (plausible at most " + Money(plausible) + ").");
    }

    // ================================================================================================================
    // A run with a baseline: the ledger against the running bound, then growth
    // ================================================================================================================

    static SaveGrowthReport CheckRun(SaveMeta current, SubmitRequest request, SaveSnapshot save, CityTotals city, SaveSubmitContext context,
                                     SavePendingCheck pendingNow, long serverNow, long elapsed, LuckStats luck, RareLuck rare, Findings findings,
                                     out LedgerResult ledger)
    {
        // The run's first save (seq 0): it started at $50,000 with no staff, shares, loans or peak, and there is no previous save
        bool freshRun = current.Seq == 0;
        SaveSnapshot previous = !freshRun && context != null ? ParsePrevious(context.PreviousSaveJson) : null;
        bool previousKnown = freshRun || previous != null;
        long days = (long)request.Day - current.Day;
        double valueBefore = current.Value, value = save.CompanyValue;
        List<LedgerEntry> fresh = FreshEntries(request, current.LedgerIndex);
        double marketHours = MarketHours(current, request.Day, serverNow);

        var report = new SaveGrowthReport { ValueBefore = valueBefore, ValueAfter = value, Days = days, CityUnknown = !city.Known };

        // ---- Peaks: the highest value the server accepted (never the value being checked), and the run's own peak in the
        // previous save (RunStats tracks it every frame; it was checked against its own stretch when that save came in) ----
        double acceptedPeak = Math.Max(Math.Max(current.Peak, current.Value), StartingValue);
        double peakBefore = Math.Max(acceptedPeak, previous != null ? previous.RunPeak : 0.0);

        // ---- Online City money: server lifetime totals since the last accept (lifetime totals never shrink), and the money
        // in transit on both sides: the last accepted save's pending transfers as the server classified them then (meta),
        // this save's as classified against this read. Growth is measured on the effective value = value + both ----
        double withdrawn = 0.0, deposited = 0.0;
        if (city.Known)
        {
            withdrawn = Math.Max(0L, city.Withdrawn - current.CityWithdrawn);
            deposited = Math.Max(0L, city.Deposited - current.CityDeposited);
        }
        report.CityNet = withdrawn - deposited;
        report.PendingBefore = SaturatedSum(current.PendingDepositsUnbooked, current.PendingWithdrawalsBooked);
        report.PendingAfter = pendingNow != null ? SaturatedSum(pendingNow.UnbookedDeposits, pendingNow.BookedWithdrawals) : report.PendingBefore;
        double effectiveBefore = valueBefore + report.PendingBefore, effectiveAfter = value + report.PendingAfter;

        // ---- A round open when the last accepted save was written: its stake left the value before that save, its payout
        // came after. That stake left a value that was at most the run's peak by then ----
        report.Straddle = freshRun ? 0.0 : Math.Min(StraddleClaim(fresh, current.Day), StraddleCap(valueBefore, peakBefore));

        // ---- Bankruptcy hearings the save shows (a loan or a restart puts max($50k, 10% of the peak) into a broke company) ----
        string evidence;
        report.Hearings = HearingCount(save, previous, freshRun, CountBusts(fresh), out evidence);
        report.HearingEvidence = evidence;

        // ---- Stocks: company value is cash; the shares' cost basis sits in the "stocks" extra (a new run has none) ----
        report.HoldingsBefore = freshRun ? 0.0 : previous != null ? previous.HoldingsCost : -1.0;
        report.HoldingsAfter = save.HoldingsCost;

        // ---- The income allowance's parts that don't depend on the value; more new staff than hiring clicks allow ----
        AllowanceModel model = AllowanceModel.Build(save, previous, freshRun, current.Day, days, elapsed);
        if (model.CheckHires && model.Base.NewStaff > model.Base.HireCap)
            findings.Add(SaveCodes.FlagLedger, model.Base.NewStaff + " new employees in " + days + " days, where hiring allows at most " + model.Base.HireCap +
                                               " (only the best " + model.Base.HireCap + " count).");

        // ---- An investor's share only ends on its last day (ManagerEngine.ExpireContracts): one the last accepted save lists
        // that still runs must still be listed, or the next offer's timing couldn't be checked (ManagerBook) ----
        if (previous != null)
        {
            foreach (SaveManagerShare s in previous.ManagerShares)
            {
                if (!s.IsInvestor || s.UntilDay < (long)save.CurrentDay + 2 || HasInvestorShare(save, s.StartDay)) continue;
                findings.Add(SaveCodes.FlagLedger, "The investor's share from day " + s.StartDay + " runs until day " + s.UntilDay + ", but this save (day " +
                                                   save.CurrentDay + ") no longer lists it.");
                break;
            }
        }

        report.Slack = SaveCalibration.GrowthSlack(effectiveBefore);
        double start = effectiveBefore + report.Straddle + withdrawn;

        // ---- The ledger, walked against the running bound on company value (none while city money is unknown) ----
        if (city.Known)
        {
            double[] restartBefore = null;
            if (report.Hearings > 0)
            {
                // A restart is 10% of the peak before it. A silent first walk bounds that peak before each entry, so an entry
                // can only lean on a restart sized by what came before it (never by its own payout). The first walk has
                // restarts of the size the peaks checked already give (a company can climb past its old peak after one)
                double knownRestart = SaveCalibration.HearingRestartMax(Math.Max(acceptedPeak, Math.Min(save.RunPeak, peakBefore)));
                var probe = new Reach(start + report.Hearings * knownRestart, report.Slack, model, report.HoldingsBefore);
                var reachedBefore = new double[fresh.Count + 1];
                WalkLedger(fresh, current.LedgerIndex, probe, null, null, save, current.Day, days, elapsed, marketHours, null, reachedBefore, null, previous, peakBefore);
                restartBefore = new double[fresh.Count + 1];
                for (int i = 0; i <= fresh.Count; i++)
                {
                    double peak = Math.Max(acceptedPeak, Math.Min(save.RunPeak, Math.Max(peakBefore, reachedBefore[i])));
                    restartBefore[i] = report.Hearings * SaveCalibration.HearingRestartMax(peak);
                }
                report.HearingRestart = restartBefore[fresh.Count] / report.Hearings;   // after everything: the growth check's restart
            }
            var reach = new Reach(start, report.Slack, model, report.HoldingsBefore);
            ledger = WalkLedger(fresh, current.LedgerIndex, reach, luck, rare, save, current.Day, days, elapsed, marketHours, findings, null, restartBefore, previous, peakBefore);
            report.PeakBound = Math.Max(peakBefore, ledger.ReachMax);
            if (previousKnown && save.RunPeak > report.PeakBound * (1.0 + 1e-9) + report.Slack)
                findings.Add(SaveCodes.FlagGrowth, "The save's run peak " + Money(save.RunPeak) + " is more than the company can have reached (" + Money(report.PeakBound) + ").");
        }
        else
        {
            ledger = WalkLedger(fresh, current.LedgerIndex, null, luck, rare, save, current.Day, days, elapsed, marketHours, findings, null, null, previous, peakBefore);
            if (report.Hearings > 0) report.HearingRestart = SaveCalibration.HearingRestartMax(Math.Max(acceptedPeak, save.RunPeak));
            report.PeakBound = double.PositiveInfinity;
        }

        // ---- Growth ----
        // Cash from stocks = sum(proceeds - cost basis sold) + (holdings cost before - holdings cost now): selling shares bought
        // before the last accepted save brings back their cost too, net buying lowers it (a buy takes its cost,
        // StockMarketManager.cs:242; a sale adds its proceeds, :264). Without both holdings, every sale's full proceeds count.
        report.StockCash = report.HoldingsBefore >= 0.0 && report.HoldingsAfter >= 0.0
            ? ledger.TradeNet + (report.HoldingsBefore - report.HoldingsAfter)
            : ledger.TradeProceeds;
        report.LedgerNet = ledger.MoneyNet + report.StockCash;

        double restartValue = report.Hearings > 0 ? report.HearingRestart : 0.0;
        double luckStart = Math.Max(effectiveBefore, restartValue) + ledger.FreshStartGain + report.Straddle + withdrawn;
        report.Allowance = model.For(luckStart);

        report.Residual = (effectiveAfter - effectiveBefore) - report.LedgerNet - report.Straddle - report.CityNet - report.Allowance.Total - report.Slack;
        // After a restart: the restart, what came after it, city withdrawals and the money that was in transit at the last accept
        report.FreshStartBound = report.Hearings > 0
            ? restartValue + report.Allowance.Total + ledger.FreshStartGain + withdrawn + report.PendingBefore + report.Slack
            : 0.0;

        // Unknown city money: no flag (a withdrawal may explain it); the meta keeps the old city baseline and pending sums
        report.Unexplained = report.Residual > 0.0 && (report.Hearings == 0 || effectiveAfter > report.FreshStartBound);
        if (report.Unexplained && city.Known)
        {
            report.Flagged = true;
            string transit = report.PendingBefore > 0 || report.PendingAfter > 0
                ? " (in transit to or from the city " + Money(report.PendingBefore) + " -> " + Money(report.PendingAfter) + ")"
                : "";
            findings.Add(SaveCodes.FlagGrowth,
                "Value " + Money(valueBefore) + " -> " + Money(value) + transit + " over " + days + " days: " + Money(report.Residual) + " unexplained (ledger " +
                Money(report.LedgerNet) + ", city " + Money(report.CityNet) + ", allowance " + Money(report.Allowance.Total) + ", slack " + Money(report.Slack) +
                (report.Hearings > 0 ? "; a restart (" + report.HearingEvidence + ") explains at most " + Money(report.FreshStartBound) : "; no bankruptcy restart shown") + ").");
        }
        return report;
    }

    /// <summary>
    /// Bankruptcy hearings since the last accept that the save shows (0 = none; <paramref name="evidence"/> says what):
    /// a loan taken (the "loan" extra's loansTaken went up), a coin-flip restart (runStats "restarts" went up), a loan
    /// forgiven (its debt vanished without being repaid: heads, RunEndDirector.ApplyCoinFlip :290) or a ledger entry that
    /// left the company at $0. A save without the restart counter (runStats "restarts") can't show a coin-flip restart
    /// after a daily-cost bankruptcy, so for it one restart is always allowed. Never more than one per bust in the ledger
    /// plus one: every restart after the first needs the company broke again, and a ledger bust shows as one.
    /// </summary>
    static int HearingCount(SaveSnapshot save, SaveSnapshot previous, bool freshRun, int busts, out string evidence)
    {
        bool known = freshRun || previous != null;
        int loans = known ? Math.Max(0, save.LoansTaken - (previous != null ? previous.LoansTaken : 0)) : save.LoansTaken;
        int restarts = known ? Math.Max(0, save.Restarts - (previous != null ? previous.Restarts : 0)) : save.Restarts;
        bool forgiven = previous != null && previous.LoanDebt > 0.005 && save.LoanDebt <= 0.005 &&
                        save.LoanRepaid - previous.LoanRepaid < previous.LoanDebt - 1.0;

        var shown = new List<string>();
        if (loans > 0) shown.Add(loans + (loans == 1 ? " loan" : " loans"));
        if (restarts > 0) shown.Add(restarts + (restarts == 1 ? " restart" : " restarts"));
        if (forgiven) shown.Add("a forgiven loan");
        if (busts > 0) shown.Add(busts + (busts == 1 ? " bust" : " busts") + " in the ledger");
        if (shown.Count == 0 && !save.HasRestarts) shown.Add("a save without the restart counter");
        evidence = shown.Count > 0 ? string.Join(", ", shown) : null;
        if (shown.Count == 0) return 0;
        long claimed = Math.Max(1L, (long)loans + restarts);
        return (int)Math.Min(claimed, 1L + busts);
    }

    /// <summary>Gambles and cards that left the company at BrokeValue or less by their own numbers (it went bankrupt there).</summary>
    static int CountBusts(List<LedgerEntry> fresh)
    {
        int busts = 0;
        foreach (LedgerEntry e in fresh)
        {
            double before = e.ValueBefore, after;
            if (!Finite(before) || before <= BrokeValue) continue;
            string kind = SaveCalibration.Normalise(e.Kind);
            if (kind == LedgerKinds.Gamble)
            {
                SaveGambleRule rule = SaveCalibration.GambleRule(e.Source, e.Detail);
                if (!rule.StakeFromValue || !Finite(e.Stake) || !Finite(e.Payout) || e.Stake < 0.0 || e.Payout < 0.0) continue;
                double payout = Math.Min(e.Payout, e.Stake * rule.MaxMultiplier);
                after = before + SaveCalibration.GambleMoneyNet(rule, e.Stake, payout, false);
            }
            else if (kind == LedgerKinds.Card)
            {
                if (!Finite(e.Change)) continue;
                after = before + e.Change;
            }
            else if (kind == LedgerKinds.Manager)
            {
                // A decision's cost stops at $0 (ManagerEffects.Money :112-121): it can leave the company broke
                if (!Finite(e.Change) || e.Change >= 0.0) continue;
                after = before + e.Change;
            }
            else
            {
                continue;
            }
            if (after <= BrokeValue) busts++;
        }
        return busts;
    }

    /// <summary>
    /// Largest stake of a round that may have been open when the last accepted save was written: the oldest new entry, or
    /// one settled on that save's day (rounds pause the day clock).
    /// </summary>
    static double StraddleClaim(List<LedgerEntry> fresh, int previousDay)
    {
        double claim = 0.0;
        bool first = true;
        foreach (LedgerEntry e in fresh)
        {
            bool firstEntry = first;
            first = false;
            string kind = SaveCalibration.Normalise(e.Kind);
            if (kind != LedgerKinds.Gamble && kind != LedgerKinds.Summary) continue;
            SaveGambleRule rule = SaveCalibration.GambleRule(e.Source, e.Detail);
            if (!rule.StakeFromValue || !Finite(e.Stake) || e.Stake < 0.0) continue;
            if (firstEntry || e.Day == previousDay) claim = Math.Max(claim, e.Stake);
        }
        return claim;
    }

    /// <summary>
    /// The most a straddling round can have staked: the value its stake left was at most the run's peak by then (the previous
    /// save's own peak, or the accepted one), and what was left is the last accepted value.
    /// </summary>
    static double StraddleCap(double valueBefore, double peakBefore)
    {
        return Math.Max(0.0, peakBefore - valueBefore) + SaveCalibration.GrowthSlack(peakBefore);
    }

    // ================================================================================================================
    // Ledger
    // ================================================================================================================

    /// <summary>What the new ledger entries add up to.</summary>
    sealed class LedgerResult
    {
        public long HighestIndex;
        /// <summary>Company value the gambles and cards explain (upper bounds per entry, impossible parts capped).</summary>
        public double MoneyNet;
        /// <summary>Stock sales: proceeds minus the cost basis of the shares sold (both capped).</summary>
        public double TradeNet;
        /// <summary>Stock sales: the proceeds alone.</summary>
        public double TradeProceeds;
        /// <summary>The same when the company restarted after a bankruptcy on the way: gains only, trades at full proceeds.</summary>
        public double FreshStartGain;
        public int NewEntries;
        public int Cards;
        /// <summary>Highest value the running bound reached (PositiveInfinity without one).</summary>
        public double ReachMax = double.PositiveInfinity;
    }

    /// <summary>The request's entries not yet counted (index above <paramref name="countedUpTo"/>), oldest first, each index once.</summary>
    static List<LedgerEntry> FreshEntries(SubmitRequest request, long countedUpTo)
    {
        var fresh = new List<LedgerEntry>();
        if (request.Ledger == null || request.Ledger.Count == 0) return fresh;
        long last = countedUpTo;
        foreach (LedgerEntry e in request.Ledger.Where(x => x != null && x.Index > countedUpTo).OrderBy(x => x.Index))
        {
            if (e.Index <= last) continue;   // sent twice
            last = e.Index;
            fresh.Add(e);
        }
        return fresh;
    }

    /// <summary>
    /// Walk the new entries in index order. With a running bound (<paramref name="reach"/>, see Reach) every stake and
    /// value-before must fit what the company can have had at that point: a bigger one is a ledger finding and counts as
    /// the bound (so its payout cap shrinks with it). Without one (imports, unknown city money) only each entry's own rules
    /// apply. <paramref name="findings"/> null = a silent walk (no findings, no tallies) that only measures the reach; then
    /// <paramref name="reachedBefore"/> (Count + 1 long) gets the highest bound before each entry and at the end.
    /// <paramref name="restartBefore"/>: what restarts can have added by each entry (and at the end), or null.
    /// <paramref name="elapsed"/>: real seconds since the last accept (-1 = unknown: imports), for how many rounds the time can hold.
    /// <paramref name="previous"/> (the last accepted save, or null) and <paramref name="peakBefore"/> (the run's peak by then)
    /// are what the manager's investor gains are checked against (ManagerBook).
    /// </summary>
    static LedgerResult WalkLedger(List<LedgerEntry> fresh, long countedUpTo, Reach reach, LuckStats luck, RareLuck rare, SaveSnapshot save,
                                   int previousDay, long days, long elapsed, double marketHours, Findings findings, double[] reachedBefore,
                                   double[] restartBefore, SaveSnapshot previous, double peakBefore)
    {
        var result = new LedgerResult { HighestIndex = countedUpTo };
        double tallyScale = SummaryTallyScale(fresh, elapsed);
        if (rare != null) rare.TrialScale = RareTrialScale(fresh, elapsed);
        if (reach != null && restartBefore != null) reach.Restart = restartBefore[0];
        var manager = new ManagerBook(save, previous, previousDay, peakBefore);
        var managerGambles = new Dictionary<string, long>(StringComparer.Ordinal);   // situation → rounds

        // Folded rounds (summaries) sit at the index of their newest round, but the older rounds in them may have come before
        // the cards and trades listed ahead of them: what they can have won counts as a lead until the walk passes them
        double[] credits = null;
        if (reach != null)
        {
            double startBound = reach.Bound(false);
            credits = new double[fresh.Count];
            for (int i = 0; i < fresh.Count; i++)
            {
                if (SaveCalibration.Normalise(fresh[i].Kind) != LedgerKinds.Summary) continue;
                credits[i] = SummaryCredit(fresh[i], startBound);
                reach.Lead += credits[i];
            }
        }

        double tradeRatio = SaveCalibration.TradeRatioMax(marketHours);
        double holdingsLeft = reach != null ? reach.HoldingsBefore : -1.0;   // cost of shares held at the last save not sold yet

        for (int i = 0; i < fresh.Count; i++)
        {
            LedgerEntry e = fresh[i];
            result.HighestIndex = Math.Max(result.HighestIndex, e.Index);
            result.NewEntries++;
            string kind = SaveCalibration.Normalise(e.Kind);
            if (credits != null && credits[i] > 0.0) reach.Lead -= credits[i];   // its own winnings are no lead for itself
            if (reach != null && restartBefore != null) reach.Restart = restartBefore[i];
            if (reach != null && reachedBefore != null)
            {
                reach.Bound(true);
                reachedBefore[i] = reach.Max;
            }

            double added;
            if (kind == LedgerKinds.Gamble)
                added = CheckGamble(e, reach, luck, rare, result, findings);
            else if (kind == LedgerKinds.Summary)
                added = CheckSummary(e, reach, luck, rare, tallyScale, result, findings);
            else if (kind == LedgerKinds.Card)
                added = CheckCard(e, reach, luck, rare, save, result, findings);
            else if (kind == LedgerKinds.Trade)
                added = CheckTrade(e, reach, ref holdingsLeft, tradeRatio, marketHours, result, findings);
            else if (kind == LedgerKinds.Manager)
                added = CheckManager(e, reach, manager, result, findings);
            else
                added = 0.0;   // Unknown kinds (a newer client) explain nothing and aren't flagged

            if ((kind == LedgerKinds.Gamble || kind == LedgerKinds.Summary) && SaveCalibration.Normalise(e.Source) == SaveCalibration.ManagerGambleSource)
            {
                string situation = SaveCalibration.ManagerSituationOf(e.Detail);
                managerGambles.TryGetValue(situation, out long rounds);
                managerGambles[situation] = rounds + (kind == LedgerKinds.Summary ? Math.Max(1, e.Count) : 1);
            }

            if (reach != null)
            {
                reach.Net += added;
                if (added > 0.0) reach.Gains += added;
            }
        }

        if (reach != null)
        {
            reach.Lead = 0.0;
            if (restartBefore != null) reach.Restart = restartBefore[fresh.Count];
            reach.Bound(true);   // the end of the stretch counts toward the reach too
            result.ReachMax = reach.Max;
            if (reachedBefore != null) reachedBefore[fresh.Count] = reach.Max;
        }

        // Risk cards wait on the same clock as the days (EventManager.Update :197-203): at most one per 42 s of it
        long maxCards = (long)Math.Floor(SaveCalibration.RunSeconds(days) / SaveCalibration.MinSecondsPerCard) + 2;
        if (findings != null && result.Cards > maxCards)
            findings.Add(SaveCodes.FlagLedger, result.Cards + " risk cards in " + days + " days (at most " + maxCards + ").");

        // A manager situation comes up at most once per cooldown (its gambles win more than they cost on average, so a
        // made-up stream of them at honest odds must not add up); situations this version doesn't know share one cap
        if (findings != null && managerGambles.Count > 0)
        {
            long unknown = 0;
            foreach (KeyValuePair<string, long> g in managerGambles)
            {
                long cap = SaveCalibration.ManagerGambleCap(g.Key, days);
                if (cap < 0) unknown += g.Value;
                else if (g.Value > cap)
                    findings.Add(SaveCodes.FlagLedger, g.Value + " manager gambles of " + ClientText(g.Key) + " in " + days + " days (at most " + cap + ": it comes up once per cooldown).");
            }
            long unknownCap = SaveCalibration.ManagerGambleCapFor(SaveCalibration.ManagerDefaultCooldownDays, days);
            if (unknown > unknownCap)
                findings.Add(SaveCodes.FlagLedger, unknown + " manager gambles of situations this server doesn't know in " + days + " days (at most " + unknownCap + ").");
        }
        return result;
    }

    /// <summary>
    /// The share of their rounds summaries count with in the luck tallies: 1, unless together with the single gambles they
    /// claim more rounds than the time allows (MaxRoundsPerSecond over the time since the last accept and the upload grace,
    /// plus MaxLedgerEntries). Scaling keeps each summary's win rate, so it never raises a flag; it only stops a made-up
    /// mountain of rounds from diluting the run's tallies.
    /// </summary>
    static double SummaryTallyScale(List<LedgerEntry> fresh, long elapsed)
    {
        long singles = 0, folded = 0;
        foreach (LedgerEntry e in fresh)
        {
            string kind = SaveCalibration.Normalise(e.Kind);
            if (kind == LedgerKinds.Gamble) singles++;
            else if (kind == LedgerKinds.Summary) folded += Math.Max(1, e.Count);
        }
        if (folded == 0) return 1.0;
        double allowed = MaxLedgerEntries + MaxRoundsPerSecond * (Math.Max(0L, elapsed) + TimeRuleGraceSeconds) - singles;
        return Math.Max(0.0, Math.Min(1.0, allowed / folded));
    }

    /// <summary>
    /// The share of this upload's rounds of games with rare tiers (slots) the rare tallies count as trials: 1, unless more
    /// spins are claimed than the time since the last accept (plus the upload grace) can hold, at one per
    /// SaveCalibration.MinSecondsPerSlotsSpin. Their hits always count in full, so padding an upload with made-up losing
    /// spins can't dilute the run's rare tally; honest spins never reach the limit. Imports (time unknown): 1.
    /// </summary>
    static double RareTrialScale(List<LedgerEntry> fresh, long elapsed)
    {
        if (elapsed < 0) return 1.0;
        double spins = 0.0;
        foreach (LedgerEntry e in fresh)
        {
            string kind = SaveCalibration.Normalise(e.Kind);
            if (kind != LedgerKinds.Gamble && kind != LedgerKinds.Summary) continue;
            if (SaveCalibration.GambleRule(e.Source, e.Detail).RareMultiples.Length == 0) continue;
            spins += kind == LedgerKinds.Summary ? Math.Max(1, e.Count) : 1;
        }
        double possible = 10.0 + (elapsed + TimeRuleGraceSeconds) / SaveCalibration.MinSecondsPerSlotsSpin;
        return spins <= possible ? 1.0 : possible / spins;
    }

    /// <summary>One gamble. Returns what it added to company value (at most).</summary>
    static double CheckGamble(LedgerEntry e, Reach reach, LuckStats luck, RareLuck rare, LedgerResult result, Findings findings)
    {
        SaveGambleRule rule = SaveCalibration.GambleRule(e.Source, e.Detail);
        string what = "Gamble " + e.Index + " (" + ClientText(rule.Key) + ")";
        if (!Finite(e.Stake) || !Finite(e.Payout) || e.Stake < 0.0 || e.Payout < 0.0 || !Finite(e.ValueBefore))
        {
            Note(findings, SaveCodes.FlagLedger, what + " has broken amounts.");
            return 0.0;
        }

        // Luck counts what was reported: a faked outcome shows as luck even when its money is capped below
        if (luck != null && rule.WinChance > 0.0) luck.Gambles.Add(rule.WinChance, e.Payout > e.Stake + 0.005);
        if (rare != null) rare.AddRounds(rule, 1, e.Stake, e.Payout);

        double stake = e.Stake;
        if (rule.StakeFromValue)
        {
            double before = e.ValueBefore;
            if (reach != null)
            {
                double bound = reach.Bound(false);
                if (before > bound + Tolerance(bound))
                {
                    Note(findings, SaveCodes.FlagLedger, what + " says the company had " + Money(before) + " before it; it can have had at most " + Money(bound) + ".");
                    before = bound;
                }
            }
            if (stake > before - rule.StakeReserve + 1.0)
            {
                Note(findings, SaveCodes.FlagLedger, what + " staked " + Money(stake) + " with only " + Money(before) +
                                                     (rule.StakeReserve > 0.0 ? " (it always leaves " + Money(rule.StakeReserve) + ")." : "."));
                stake = Math.Max(0.0, before - rule.StakeReserve);
            }
        }

        if (e.Payout > PayoutCap(e.Stake, rule.MaxMultiplier))
            Note(findings, SaveCodes.FlagLedger, what + " paid " + Money(e.Payout) + " on " + Money(e.Stake) + " (at most " + rule.MaxMultiplier.ToString("0.##", Inv) + "x).");
        double payout = Math.Min(e.Payout, PayoutCap(stake, rule.MaxMultiplier));

        double money = SaveCalibration.GambleMoneyNet(rule, stake, payout, false);
        result.MoneyNet += money;
        result.FreshStartGain += Math.Max(0.0, money);
        return money;
    }

    /// <summary>Folded gambles of one source: Count rounds, Wins won, Stake / Payout / ValueBefore summed. Returns what they added (at most).</summary>
    static double CheckSummary(LedgerEntry e, Reach reach, LuckStats luck, RareLuck rare, double tallyScale, LedgerResult result, Findings findings)
    {
        SaveGambleRule rule = SaveCalibration.GambleRule(e.Source, e.Detail);
        string what = "Summary " + e.Index + " (" + ClientText(rule.Key) + ")";
        if (!Finite(e.Stake) || !Finite(e.Payout) || e.Stake < 0.0 || e.Payout < 0.0 || !Finite(e.ValueBefore))
        {
            Note(findings, SaveCodes.FlagLedger, what + " has broken amounts.");
            return 0.0;
        }

        int count = Math.Max(1, e.Count);
        int wins = Math.Max(0, Math.Min(count, e.Wins));
        if (e.Count < 1 || e.Wins < 0 || e.Wins > e.Count)
            Note(findings, SaveCodes.FlagLedger, what + " has " + e.Wins + " wins of " + e.Count + ".");
        // The tallies count at most as many rounds as the time allows (SummaryTallyScale), at the same win rate
        int tallied = tallyScale >= 1.0 ? count : Math.Max(1, (int)Math.Round(count * tallyScale));
        int talliedWins = tallyScale >= 1.0 ? wins : Math.Min(tallied, (int)Math.Round(wins * tallyScale));
        if (luck != null && rule.WinChance > 0.0) AddResults(luck.Gambles, rule.WinChance, tallied, talliedWins);
        if (rare != null) rare.AddRounds(rule, count, e.Stake, e.Payout);

        // The sums: every round staked at most what the company can have had
        double stake = e.Stake;
        if (rule.StakeFromValue && reach != null)
        {
            double bound = reach.Bound(true) * count;
            if (stake > bound + Tolerance(bound))
            {
                Note(findings, SaveCodes.FlagLedger, what + " staked " + Money(stake) + " in " + count + " rounds, more than " + count + " times what the company can have had (" + Money(bound) + ").");
                stake = bound;
            }
            if (e.ValueBefore > bound + Tolerance(bound))
                Note(findings, SaveCodes.FlagLedger, what + " says the company had " + Money(e.ValueBefore) + " before its " + count + " rounds, more than " + count + " times what it can have had (" + Money(bound) + ").");
        }

        // A round that isn't won pays at most a push (blackjack: the stake back; slots, flips: nothing)
        double multiple = wins > 0 ? rule.MaxMultiplier : rule.PushMultiple;
        if (e.Payout > PayoutCap(e.Stake, multiple))
            Note(findings, SaveCodes.FlagLedger, what + " paid " + Money(e.Payout) + " on " + Money(e.Stake) + (wins > 0
                ? " (at most " + multiple.ToString("0.##", Inv) + "x)."
                : " without a win (at most " + multiple.ToString("0.##", Inv) + "x)."));
        double payout = Math.Min(e.Payout, PayoutCap(stake, multiple));

        double money = SaveCalibration.GambleMoneyNet(rule, stake, payout, true);
        result.MoneyNet += money;
        result.FreshStartGain += Math.Max(0.0, money);
        return money;
    }

    /// <summary>What a summary can have added at most, with every round's stake at most <paramref name="startBound"/> (its lead).</summary>
    static double SummaryCredit(LedgerEntry e, double startBound)
    {
        SaveGambleRule rule = SaveCalibration.GambleRule(e.Source, e.Detail);
        if (!rule.StakeFromValue || !Finite(e.Stake) || !Finite(e.Payout) || e.Stake < 0.0 || e.Payout < 0.0) return 0.0;
        int count = Math.Max(1, e.Count);
        int wins = Math.Max(0, Math.Min(count, e.Wins));
        double stake = Math.Min(e.Stake, startBound * count);
        double payout = Math.Min(e.Payout, PayoutCap(stake, wins > 0 ? rule.MaxMultiplier : rule.PushMultiple));
        return Math.Max(0.0, SaveCalibration.GambleMoneyNet(rule, stake, payout, true));
    }

    /// <summary>One risk card. Returns what it added (at most; negative for a loss).</summary>
    static double CheckCard(LedgerEntry e, Reach reach, LuckStats luck, RareLuck rare, SaveSnapshot save, LedgerResult result, Findings findings)
    {
        result.Cards++;
        SaveCardRule rule = SaveCalibration.CardRule(e.Source);
        string what = "Card " + e.Index + " (" + ClientText(rule.Key) + ", " + (e.Good ? "good" : "bad") + ")";
        if (!Finite(e.Change) || !Finite(e.ValueBefore) || e.ValueBefore < 0.0)
        {
            Note(findings, SaveCodes.FlagLedger, what + " has broken amounts.");
            return 0.0;
        }
        if (luck != null && rule.Tallied)
            luck.Cards.Add(SaveCalibration.CardGoodChanceMax(rule, save.UnlockedSkills), e.Good);

        double before = e.ValueBefore;
        if (reach != null)
        {
            double bound = reach.Bound(true);
            if (before > bound + Tolerance(bound))
            {
                Note(findings, SaveCodes.FlagLedger, what + " says the company had " + Money(before) + " before it; it can have had at most " + Money(bound) + ".");
                before = bound;
            }
        }

        // Card bounds assume every card skill: the save's own skill list comes from the client, so trusting it would only
        // catch careless cheats and could hurt honest players if a skill were ever missing from it
        double money = e.Change;
        if (e.Good)
        {
            double max = SaveCalibration.CardGainMax(rule, before);
            // More than the most without the Jackpot (x10) means a Jackpot: a rare outcome (at most 3.1% of good cards)
            if (rare != null) rare.AddCard(e.Change > SaveCalibration.CardGainMax(rule, before, null, false));
            if (money < -0.01)
            {
                Note(findings, SaveCodes.FlagLedger, what + " lost " + Money(-money) + ".");
                money = 0.0;
            }
            else if (money > max)
            {
                Note(findings, SaveCodes.FlagLedger, what + " added " + Money(money) + " to " + Money(before) + " (at most " + Money(max) + ").");
                money = max;
            }
        }
        else
        {
            if (money > 0.01)
            {
                Note(findings, SaveCodes.FlagLedger, what + " added " + Money(money) + ".");
                money = 0.0;
            }
            else if (money < -(before + 1.0))
            {
                Note(findings, SaveCodes.FlagLedger, what + " lost " + Money(-money) + " of " + Money(before) + ".");
                money = -before;
            }
        }
        result.MoneyNet += money;
        result.FreshStartGain += Math.Max(0.0, money);
        return money;
    }

    /// <summary>
    /// One stock sale (or several of one symbol merged: sums). Returns what it added to the running bound: the proceeds, less
    /// the part of the cost basis that wasn't held at the last save (bought since, with cash the bound still counted).
    /// </summary>
    static double CheckTrade(LedgerEntry e, Reach reach, ref double holdingsLeft, double tradeRatio, double marketHours, LedgerResult result, Findings findings)
    {
        string what = "Sale " + e.Index + " (" + ClientText(e.Source) + ")";
        if (!Finite(e.Stake) || !Finite(e.Payout) || e.Stake < 0.0 || e.Payout < 0.0)
        {
            Note(findings, SaveCodes.FlagLedger, what + " has broken amounts.");
            return 0.0;
        }

        double stake = e.Stake;
        bool holdingsKnown = holdingsLeft >= 0.0;
        if (reach != null)
        {
            double bound = reach.Bound(true);
            // The shares sold cost at most what was held at the last save plus what the company's cash can have bought since
            if (holdingsKnown)
            {
                double canHold = holdingsLeft + bound;
                if (stake > canHold + Tolerance(canHold))
                {
                    Note(findings, SaveCodes.FlagLedger, what + " sold shares that cost " + Money(stake) + "; the company can have held at most " + Money(canHold) + ".");
                    stake = canHold;
                }
            }
            double beforeBound = bound * Math.Max(1, e.Count);
            if (Finite(e.ValueBefore) && e.ValueBefore > beforeBound + Tolerance(beforeBound))
                Note(findings, SaveCodes.FlagLedger, what + " says the company had " + Money(e.ValueBefore) + " before it; it can have had at most " + Money(beforeBound) + ".");
        }

        // Merged sales of one symbol (Count > 1) carry sums: the bound holds for the sums too
        if (e.Payout > TradeCap(e.Stake, tradeRatio))
            Note(findings, SaveCodes.FlagLedger, what + " sold for " + Money(e.Payout) + " shares that cost " + Money(e.Stake) + " (at most " +
                                                 tradeRatio.ToString("0.#", Inv) + "x after " + marketHours.ToString("0.#", Inv) + " h).");
        double payout = Math.Min(e.Payout, TradeCap(stake, tradeRatio));

        result.TradeNet += payout - stake;
        result.TradeProceeds += payout;
        // After a bankruptcy restart the shares (bought before it) come back as new money: count the full proceeds
        result.FreshStartGain += payout;

        if (!holdingsKnown) return payout;
        double held = Math.Min(stake, holdingsLeft);
        holdingsLeft -= held;
        return payout - (stake - held);
    }

    /// <summary>
    /// A manager decision's one-off money (LedgerKinds.Manager: Source = situation, Detail = choice, Change = what its "money"
    /// effects moved; its gambles are gamble entries of source "manager", so Stake counts nothing here). A cost counts as it
    /// is. A gain can only be an investor's offer: at most the offer's amount at the cash tier it came up at
    /// (SaveCalibration.ManagerGainMax) and on the investor's timing (ManagerBook.CheckGain). Anything else is a ledger
    /// finding and explains nothing (an offer above its amount: the amount). Returns what it added (at most).
    /// </summary>
    static double CheckManager(LedgerEntry e, Reach reach, ManagerBook book, LedgerResult result, Findings findings)
    {
        string situation = SaveCalibration.Normalise(e.Source), choice = SaveCalibration.Normalise(e.Detail);
        string what = "Manager decision " + e.Index + " (" + ClientText(situation) + " / " + ClientText(choice) + ")";
        if (!Finite(e.Change) || !Finite(e.ValueBefore) || !Finite(e.Stake) || e.Stake < 0.0)
        {
            Note(findings, SaveCodes.FlagLedger, what + " has broken amounts.");
            return 0.0;
        }
        if (reach != null)
        {
            double bound = reach.Bound(true);
            if (e.ValueBefore > bound + Tolerance(bound))
                Note(findings, SaveCodes.FlagLedger, what + " says the company had " + Money(e.ValueBefore) + " before it; it can have had at most " + Money(bound) + ".");
        }

        double money = e.Change;
        if (money > 0.5)
        {
            int day = book.DayOf(e);
            int tier = book.OfferTier(day, e.ValueBefore, reach);
            double max = SaveCalibration.ManagerGainMax(situation, choice, tier);
            string timing;
            if (max <= 0.0)
            {
                Note(findings, SaveCodes.FlagLedger, what + " added " + Money(money) + "; only an investor's offer (accept or negotiate) pays money in.");
                money = 0.0;
            }
            else if ((timing = book.CheckGain(day)) != null)
            {
                Note(findings, SaveCodes.FlagLedger, what + " added " + Money(money) + timing);
                money = 0.0;
            }
            else if (money > max + Tolerance(max))
            {
                Note(findings, SaveCodes.FlagLedger, what + " added " + Money(money) + "; the offer pays at most " + Money(max) + " at cash tier " + tier + ".");
                money = max;
            }
        }
        result.MoneyNet += money;
        result.FreshStartGain += Math.Max(0.0, money);
        return money;
    }

    /// <summary>
    /// The investor's timing for one walk of the ledger. An offer only comes up while no revenue share runs, and taking it
    /// starts the investor's 40-day share (SaveCalibration.InvestorSituation), so gains are at least 40 days apart. The save
    /// shows the share: the last accepted save's investor share blocks gains until it ends, and a gain recent enough for its
    /// share to still run must have it in this save (so the next upload sees it in the previous save). 2 days of slack for
    /// where the day ticks over.
    /// </summary>
    sealed class ManagerBook
    {
        /// <summary>Gains come at least this many days apart (the share's 40 days, less the slack).</summary>
        const int GainSpacingDays = SaveCalibration.InvestorShareDays - 2;

        readonly SaveSnapshot save;
        readonly int previousDay;
        readonly double peakBefore;
        readonly int blockingStart = int.MinValue;
        int lastGain = int.MinValue;

        public ManagerBook(SaveSnapshot save, SaveSnapshot previous, int previousDay, double peakBefore)
        {
            this.save = save;
            this.previousDay = previousDay;
            this.peakBefore = peakBefore;
            if (previous == null) return;
            foreach (SaveManagerShare s in previous.ManagerShares)
                if (s.IsInvestor) blockingStart = Math.Max(blockingStart, s.StartDay);
        }

        /// <summary>
        /// The entry's day, within the stretch it belongs to: recorded after the last accepted save and before this one (the
        /// client's number can't reach outside it, nor overflow the timing sums).
        /// </summary>
        public int DayOf(LedgerEntry e)
        {
            int last = Math.Max(previousDay, save.CurrentDay);
            return Math.Max(previousDay, Math.Min(last, e.Day));
        }

        /// <summary>
        /// The highest cash tier the offer can have come up at: at most ManagerDecisionWindowDays before the decision, at the
        /// value it had then. After the last accepted save that is at most the highest the running bound reached; before it,
        /// the run's peak. Without a running bound: the larger of the value it claims and the peak.
        /// </summary>
        public int OfferTier(int day, double valueBefore, Reach reach)
        {
            double then;
            if (reach == null) then = Math.Max(valueBefore, peakBefore);
            else if (day - SaveCalibration.ManagerDecisionWindowDays > previousDay) then = reach.Max;
            else then = Math.Max(peakBefore, reach.Max);
            return SaveCalibration.CashTier(then);
        }

        /// <summary>Null when a gain on <paramref name="day"/> fits the investor's timing, else why it doesn't (for the finding).</summary>
        public string CheckGain(int day)
        {
            string why = null;
            if (lastGain != int.MinValue && day < lastGain + GainSpacingDays)
                why = " on day " + day + ", " + (day - lastGain) + " days after the investor's last payment: an offer only comes after its " +
                      SaveCalibration.InvestorShareDays + "-day share has ended.";
            else if (blockingStart != int.MinValue && day < blockingStart + GainSpacingDays)
                why = " on day " + day + " while the investor's share from day " + blockingStart + " still ran (the last accepted save lists it).";
            else if (save.CurrentDay <= day + GainSpacingDays - 1 && !HasInvestorShare(save, day))
                why = " on day " + day + ", but this save lists no investor share from that day (every accepted offer starts one).";
            if (why == null) lastGain = day;
            return why;
        }
    }

    /// <summary>The save lists an investor share that started on <paramref name="day"/> (a day either way).</summary>
    static bool HasInvestorShare(SaveSnapshot save, int day)
    {
        foreach (SaveManagerShare s in save.ManagerShares)
            if (s.IsInvestor && Math.Abs((long)s.StartDay - day) <= 1) return true;
        return false;
    }

    static double PayoutCap(double stake, double multiple)
    {
        return stake * multiple + 0.01 + stake * 1e-9;
    }

    static double TradeCap(double stake, double tradeRatio)
    {
        return tradeRatio >= double.MaxValue / Math.Max(1.0, stake) ? double.MaxValue : stake * tradeRatio + 1.0;
    }

    /// <summary>Hours stock prices may have moved while shares were held: the run's real time plus 45 s a day (imported runs started earlier), +1.</summary>
    static double MarketHours(SaveMeta current, int day, long serverNow)
    {
        double real = current != null ? Math.Max(0L, serverNow - current.StartedAt) / 3600.0 : 0.0;
        return real + Math.Max(0, day) * SaveCalibration.MaxSecondsPerDay / 3600.0 + 1.0;
    }

    static void AddResults(LuckTally tally, double winChance, int count, int wins)
    {
        if (tally == null || count <= 0 || double.IsNaN(winChance) || winChance <= 0.0 || winChance >= 1.0) return;
        tally.Count = (int)Math.Min(int.MaxValue, (long)tally.Count + count);
        tally.Wins += wins;
        tally.Expected += count * winChance;
        tally.Variance += count * winChance * (1.0 - winChance);
    }

    /// <summary>
    /// The running upper bound on company value while the ledger is walked in index order:
    /// U = Start + Restart + Net + Lead + IncomeAllowance(from Start + Restart + Gains + Lead) + Slack.
    /// - Start: the last accepted value, plus a straddling stake and the city's withdrawals and timing slack.
    /// - Restart: what the bankruptcy restarts the save shows can have put in by this point (a restart comes after a bust, so
    ///   it adds to what the ledger left; its size is 10% of the peak reachable before this entry).
    /// - Net: what the entries walked so far added (losses included); Gains: their positive part (daily luck and board
    ///   bonuses grow with the value, so the allowance is taken from the highest start the gains allow).
    /// - Lead: what folded rounds further on can have won, for the cards, trades and summaries listed ahead of them.
    /// The whole stretch's income counts from the start: an upper bound at every point.
    /// </summary>
    sealed class Reach
    {
        public readonly double Start, Slack, HoldingsBefore;
        readonly AllowanceModel allowance;
        public double Restart, Net, Gains, Lead, Max;
        double withLeadAt = double.NegativeInfinity, withLeadTotal, plainAt = double.NegativeInfinity, plainTotal;

        public Reach(double start, double slack, AllowanceModel allowance, double holdingsBefore)
        {
            Start = Finite(start) ? Math.Max(0.0, start) : double.MaxValue;
            Slack = slack;
            this.allowance = allowance;
            HoldingsBefore = holdingsBefore;
        }

        /// <summary>
        /// The most the company can have right now. Gambles take it without the lead: the client folds the oldest gambles
        /// first, so no single gamble comes before a summary's rounds.
        /// </summary>
        public double Bound(bool withLead)
        {
            double lead = withLead ? Math.Max(0.0, Lead) : 0.0;
            double start = Start + Math.Max(0.0, Restart);
            double at = start + Math.Max(0.0, Gains) + lead;
            double income;
            // The allowance only grows with its start, so one worked out for a higher start is still an upper bound
            if (withLead)
            {
                if (at > withLeadAt)
                {
                    withLeadTotal = allowance.Total(at);
                    withLeadAt = at;
                }
                income = withLeadTotal;
            }
            else
            {
                if (at > plainAt)
                {
                    plainTotal = allowance.Total(at);
                    plainAt = at;
                }
                income = plainTotal;
            }
            double u = start + Net + lead + income + Slack;
            if (double.IsNaN(u) || u > double.MaxValue) u = double.MaxValue;
            if (u < 0.0) u = 0.0;
            if (u > Max) Max = u;
            return u;
        }
    }

    /// <summary>
    /// Rare payouts against honest odds, two ways, each flagged when honest odds give the count a chance below
    /// RareLuckMaxChance (a Chernoff bound, so the true chance is smaller still):
    /// - over the whole run (meta.Luck.Rare): every round of a game with rare tiers is one trial of its first tier (slots:
    ///   20x or more, at most 0.15%) and every good card one trial of a jackpot (at most 3.1%), so one fake jackpot per upload
    ///   adds up across uploads;
    /// - within this upload, by payout size (SaveGambleRule.RareMultiples, card jackpots), so a cluster of big ones shows at
    ///   once. The chance is split over the tests.
    /// A summary counts all its rounds and, when its payout sum reaches a tier's multiple of its stake sum, one hit. Claimed
    /// spins beyond what the time allows don't add trials (TrialScale), but their hits count.
    /// </summary>
    sealed class RareLuck
    {
        sealed class Tier
        {
            public string What;
            public double Expected;
            public long Rounds;
            public int Hits;
        }

        readonly Dictionary<string, Tier> tiers = new Dictionary<string, Tier>(StringComparer.Ordinal);
        readonly LuckTally run;
        double runTrials;   // this upload's trials for the run's tally (added to its count once, whole)
        /// <summary>This upload's share of claimed spins counted as trials (RareTrialScale); hits always count in full.</summary>
        public double TrialScale = 1.0;

        /// <param name="run">The run's tally (meta.Luck.Rare, updated in place); null = this upload only.</param>
        public RareLuck(LuckTally run)
        {
            this.run = run;
        }

        public void AddRounds(SaveGambleRule rule, int count, double stake, double payout)
        {
            if (count <= 0) return;
            double trials = count * Math.Max(0.0, Math.Min(1.0, TrialScale));
            for (int t = 0; t < rule.RareMultiples.Length; t++)
            {
                double multiple = rule.RareMultiples[t], chance = rule.RareChances[t];
                bool hit = stake > 0.0 && payout >= multiple * stake * (1.0 - 1e-9) - 0.01;
                Tier tier = Get(rule.Key + "@" + multiple.ToString("0.##", Inv), ClientText(rule.Key) + " paying " + multiple.ToString("0.##", Inv) + "x or more");
                tier.Rounds += count;
                tier.Expected += trials * chance;
                if (hit) tier.Hits++;
                if (t == 0) AddToRun(chance, trials, hit);
            }
        }

        public void AddCard(bool jackpot)
        {
            double chance = SaveCalibration.CardJackpotChanceMax(null);
            Tier tier = Get("card-jackpot", "Risk-card jackpots");
            tier.Rounds++;
            tier.Expected += chance;
            if (jackpot) tier.Hits++;
            AddToRun(chance, 1, jackpot);
        }

        void AddToRun(double chance, double trials, bool hit)
        {
            if (run == null) return;
            runTrials += trials;
            run.Expected += trials * chance;
            run.Variance += trials * chance * (1.0 - chance);
            if (hit) run.Wins += 1.0;
        }

        Tier Get(string key, string what)
        {
            if (!tiers.TryGetValue(key, out Tier tier))
            {
                tier = new Tier { What = what };
                tiers[key] = tier;
            }
            return tier;
        }

        public void Check(Findings findings)
        {
            if (run != null && runTrials > 0.0)
            {
                run.Count = (int)Math.Min(int.MaxValue, run.Count + Math.Round(runTrials));
                runTrials = 0.0;
            }
            if (tiers.Count == 0) return;
            double limit = Math.Log(RareLuckMaxChance / (tiers.Count + 1));
            string odds = " (a chance below 1 in " + (1.0 / RareLuckMaxChance).ToString("N0", Inv) + ")";
            if (run != null && run.Wins > 0.0 && LogTailBound(run.Expected, run.Wins) < limit)
                findings.Add(SaveCodes.FlagLuck, "Rare payouts this run: " + run.Wins.ToString("0", Inv) + " of " + run.Count + " rounds paid 20x or more or hit a card jackpot, where honest odds expect " +
                                                 run.Expected.ToString("0.###", Inv) + odds + ".");
            foreach (Tier tier in tiers.Values)
            {
                if (tier.Hits > 0 && LogTailBound(tier.Expected, tier.Hits) < limit)
                    findings.Add(SaveCodes.FlagLuck, "This upload: " + tier.What + " " + tier.Hits + " times in " + tier.Rounds + " rounds, where honest odds expect " +
                                                     tier.Expected.ToString("0.####", Inv) + odds + ".");
            }
        }
    }

    /// <summary>ln of the Chernoff bound e^-λ (eλ/k)^k on the chance of k or more hits among independent rare events expecting λ (0 when k ≤ λ).</summary>
    static double LogTailBound(double expected, double hits)
    {
        if (hits <= expected) return 0.0;
        if (expected <= 0.0) return double.NegativeInfinity;
        return -expected + hits * (1.0 + Math.Log(expected) - Math.Log(hits));
    }

    // ================================================================================================================
    // Income allowance
    // ================================================================================================================

    /// <summary>
    /// The most the company can have earned without a ledger entry over <paramref name="days"/> days after day
    /// <paramref name="startDay"/>: daily ticks from the staff of both saves (plus anyone hired and fired in between), hires,
    /// hiring tasks, skipped CVs, crises and board bonuses, all at their calibrated maximum, times the safety factor.
    /// <paramref name="previous"/> null = the run's first save (everyone in it was hired since; at most as many count as
    /// hiring clicks allow). <paramref name="luckStart"/> is the highest value the company can have started from (daily luck
    /// and board bonuses grow with it).
    /// </summary>
    public static SaveAllowance IncomeAllowance(SaveSnapshot save, SaveSnapshot previous, int startDay, long days, long elapsedSeconds, double luckStart)
    {
        return AllowanceModel.Build(save, previous, previous == null, startDay, days, elapsedSeconds).For(luckStart);
    }

    /// <summary>The income allowance's parts that don't depend on the company's value, worked out once per submit.</summary>
    sealed class AllowanceModel
    {
        const double LuckCapValue = 25000000.0;   // LuckBonusBase stops growing at $25M (250,000 = 1%)

        /// <summary>Everything but Daily, Board and Total.</summary>
        public SaveAllowance Base;
        /// <summary>The new staff can be told apart (previous save known, or the run's first save): the hire cap applies.</summary>
        public bool CheckHires;
        int startDay;
        double events;

        public static AllowanceModel Build(SaveSnapshot save, SaveSnapshot previous, bool freshRun, int startDay, long days, long elapsedSeconds)
        {
            var model = new AllowanceModel { startDay = startDay };
            var a = new SaveAllowance { Days = Math.Max(0L, days) };
            model.Base = a;
            double runSeconds = SaveCalibration.RunSeconds(a.Days);
            long hireCap = (long)Math.Floor(runSeconds * SaveCalibration.MaxHiresPerRunSecond) + 2;
            a.HireCap = hireCap;

            // ---- Staff: who was there before (by id, the best attributes of both saves) and who is new ----
            var existing = new List<SaveEmployeeInfo>();
            var hired = new List<SaveEmployeeInfo>();
            int removed = 0;
            if (previous != null)
            {
                var union = new Dictionary<string, SaveEmployeeInfo>(StringComparer.Ordinal);
                var oldWithoutId = new List<SaveEmployeeInfo>();
                foreach (SaveEmployeeInfo e in previous.Employees)
                {
                    if (string.IsNullOrEmpty(e.Id)) oldWithoutId.Add(e);
                    else Merge(union, e);
                }
                var before = new HashSet<string>(union.Keys, StringComparer.Ordinal);
                var newById = new Dictionary<string, SaveEmployeeInfo>(StringComparer.Ordinal);
                var nowWithoutId = new List<SaveEmployeeInfo>();
                var now = new HashSet<string>(StringComparer.Ordinal);
                foreach (SaveEmployeeInfo e in save.Employees)
                {
                    if (string.IsNullOrEmpty(e.Id))
                    {
                        nowWithoutId.Add(e);
                        continue;
                    }
                    now.Add(e.Id);
                    Merge(before.Contains(e.Id) ? union : newById, e);
                }
                foreach (string id in before)
                {
                    if (!now.Contains(id)) removed++;
                }
                existing.AddRange(union.Values);
                hired.AddRange(newById.Values);

                // Staff without ids (saves from before ids) can't be told apart: as many as the previous save had are the same
                // people (the best of them), the rest are new; those who left worked part of the stretch
                nowWithoutId = ByOutput(nowWithoutId);
                int same = Math.Min(oldWithoutId.Count, nowWithoutId.Count);
                for (int i = 0; i < nowWithoutId.Count; i++) (i < same ? existing : hired).Add(nowWithoutId[i]);
                if (oldWithoutId.Count > nowWithoutId.Count)
                {
                    existing.AddRange(ByOutput(oldWithoutId).Take(oldWithoutId.Count - nowWithoutId.Count));
                    removed += oldWithoutId.Count - nowWithoutId.Count;
                }
                model.CheckHires = true;
            }
            else
            {
                // The run's first save: everyone was hired since the run began. Previous save unknown: everyone counts as maybe new
                var byId = new Dictionary<string, SaveEmployeeInfo>(StringComparer.Ordinal);
                foreach (SaveEmployeeInfo e in save.Employees)
                {
                    if (string.IsNullOrEmpty(e.Id)) hired.Add(e);
                    else Merge(byId, e);
                }
                hired.AddRange(byId.Values);
                model.CheckHires = freshRun;
            }
            a.NewStaff = hired.Count;

            // At most hireCap new faces count, the best ones (a save with more gets a ledger finding); plus fires the save
            // counts that weren't among the old staff (hired and fired in between)
            long counted = model.CheckHires ? Math.Min(hired.Count, hireCap) : hired.Count;
            int firedSince = previous != null ? Math.Max(0, save.Fired - previous.Fired) : Math.Max(0, save.Fired);
            long hiresSeen = hired.Count + Math.Max(0, firedSince - removed);
            a.Hires = Math.Min(hiresSeen, Math.Max(hireCap, counted));
            long unseen = Math.Max(0L, Math.Min(hiresSeen, hireCap) - counted);
            a.Headcount = (int)Math.Min(int.MaxValue / 2, existing.Count + counted + unseen);

            List<SaveEmployeeInfo> bestHires = ByOutput(hired);
            double output = unseen * SaveCalibration.BestEmployeeOutputMax();
            foreach (SaveEmployeeInfo e in existing) output += SaveCalibration.EmployeeOutputMax(e);
            for (int i = 0; i < counted; i++) output += SaveCalibration.EmployeeOutputMax(bestHires[i]);
            a.EmployeeOutput = output;

            double office = Math.Max(save.OfficeIncomeMax, previous != null ? previous.OfficeIncomeMax : 0.0);
            double tower = Math.Max(save.TowerIncomeMax, previous != null ? previous.TowerIncomeMax : 0.0);
            a.OperatingMax = output * SaveCalibration.TeamSynergyMax(a.Headcount) + SaveCalibration.MarketMax(a.Headcount) + SaveCalibration.RobotsMax + office + tower;
            a.DailyFixed = a.OperatingMax * (1.0 + SaveCalibration.MaxProfitGrowth) + a.Headcount * SaveCalibration.MaxMergerPerEmployee;

            // ---- Money that comes with actions, not days ----
            List<double> impacts = hired.Select(SaveCalibration.HireImpactMax).OrderByDescending(x => x).ToList();
            for (int i = 0; i < counted; i++) a.HireImpact += impacts[i];
            a.HireImpact += unseen * SaveCalibration.BestHireImpactMax();
            long tasks = Math.Min(a.Hires, (long)Math.Floor(runSeconds / SaveCalibration.MinSecondsPerTask) + 1);
            a.Tasks = tasks * SaveCalibration.MaxTaskReward;
            long loads = (long)Math.Floor(Math.Max(0L, elapsedSeconds) / SaveCalibration.MinSecondsPerSceneLoad) + 2;
            a.Skips = SaveCalibration.SkipsPerBatch * (double)(a.Hires + loads) * SaveCalibration.MaxSkipBonus;
            a.Crises = (Math.Floor(runSeconds / SaveCalibration.MinSecondsPerCrisis) + 1) * SaveCalibration.MaxCrisisReward;
            model.events = a.HireImpact + a.Tasks + a.Skips + a.Crises;

            // Board bonus: meetings are at least 10 days apart (BoardMeetings.cs:583, :660); the saves count them exactly
            long meetings = a.Days / SaveCalibration.BoardMeetingInterval + 1;
            if (save.MeetingsHeld >= 0)
            {
                long held = previous != null && previous.MeetingsHeld >= 0 ? Math.Max(0, save.MeetingsHeld - previous.MeetingsHeld) : save.MeetingsHeld;
                meetings = Math.Min(meetings, held);
            }
            a.Meetings = meetings;
            return model;
        }

        /// <summary>The allowance from a company that started the stretch with at most <paramref name="luckStart"/>.</summary>
        public SaveAllowance For(double luckStart)
        {
            SaveAllowance a = Base.Clone();
            double daily, board;
            Walk(luckStart, out daily, out board);
            a.Daily = daily;
            a.Board = board;
            a.Total = Safe((events + daily + board) * SaveCalibration.IncomeSafetyFactor);
            return a;
        }

        /// <summary>For(luckStart).Total without the breakdown.</summary>
        public double Total(double luckStart)
        {
            double daily, board;
            Walk(luckStart, out daily, out board);
            return Safe((events + daily + board) * SaveCalibration.IncomeSafetyFactor);
        }

        /// <summary>Daily ticks: luck grows with the value, so walk the days from the highest possible start; then the board bonuses.</summary>
        void Walk(double luckStart, out double daily, out double board)
        {
            SaveAllowance a = Base;
            if (double.IsNaN(luckStart)) luckStart = double.MaxValue;
            double v = Math.Max(0.0, luckStart) + events;
            daily = 0.0;
            long d = 0;
            for (; d < a.Days && v < LuckCapValue; d++)
            {
                double income = a.DailyFixed + SaveCalibration.DailyLuckMax(v, a.Headcount, SaveCalibration.IsKarmaDay(startDay + d + 1));
                daily += income;
                v += income;
            }
            if (d < a.Days)
            {
                long remaining = a.Days - d;
                long firstDay = startDay + d + 1, lastDay = startDay + a.Days;
                long karmaDays = lastDay / 5 - (firstDay - 1) / 5;
                double plain = a.DailyFixed + SaveCalibration.DailyLuckMax(LuckCapValue, a.Headcount, false);
                double karmaExtra = SaveCalibration.DailyLuckMax(LuckCapValue, a.Headcount, true) - SaveCalibration.DailyLuckMax(LuckCapValue, a.Headcount, false);
                double rest = remaining * plain + karmaDays * karmaExtra;
                daily += rest;
                v += rest;
            }
            board = a.Meetings * SaveCalibration.BoardBonusShare * v;
        }

        static double Safe(double total)
        {
            return double.IsNaN(total) || double.IsInfinity(total) ? double.MaxValue : total;
        }

        static List<SaveEmployeeInfo> ByOutput(List<SaveEmployeeInfo> staff)
        {
            return staff.OrderByDescending(SaveCalibration.EmployeeOutputMax).ToList();
        }
    }

    static void Merge(Dictionary<string, SaveEmployeeInfo> union, SaveEmployeeInfo e)
    {
        if (!union.TryGetValue(e.Id, out SaveEmployeeInfo known))
        {
            union[e.Id] = new SaveEmployeeInfo { Id = e.Id, IQ = e.IQ, GPA = e.GPA, Experience = e.Experience, Age = e.Age, PhD = e.PhD, Criminal = e.Criminal };
            return;
        }
        known.IQ = Math.Max(known.IQ, e.IQ);
        known.GPA = Math.Max(known.GPA, e.GPA);
        known.Experience = Math.Max(known.Experience, e.Experience);
        known.PhD = known.PhD || e.PhD;
        known.Criminal = known.Criminal && e.Criminal;
    }

    // ================================================================================================================
    // Online City timing, the previous save
    // ================================================================================================================

    /// <summary>
    /// A save's pending Online City transfers classified against one read of the player's city accounts: the stamps and
    /// applied op ids of every acct_&lt;city&gt; the read found (SaveCityTransfer: CityAccount deps / wds; the account's ops).
    /// The one rule the server's growth check and the client's adopt settlement share:
    /// - only this player's transfers count (an empty player is the current one's: saves from before accounts were
    ///   recorded; <paramref name="playerId"/> null = every transfer counts), each op id once;
    /// - a transfer is booked when the city has a stamp of its kind with its op id, or when no stamp of the other kind
    ///   has that id and the op id is among the applied ops (a stamp can be tidied away, the op stays longer);
    /// - an unbooked deposit already left the company but is in no bank yet: its amount (the save's) is the company's;
    /// - a booked withdrawal already left a bank but isn't in the company yet: its amount (the save's, at most the stamp's
    ///   when the stamp is there) is the company's.
    /// Everything else in transit counts nowhere yet: the city's lifetime totals take it once it is booked (deposits) and
    /// the company's value once it is paid (withdrawals).
    /// </summary>
    public static SavePendingCheck ClassifyPending(IEnumerable<SavePendingTransfer> pending, string playerId, IEnumerable<SaveCityTransfer> stamps,
                                                   IEnumerable<string> appliedOps)
    {
        var result = new SavePendingCheck();
        if (pending == null) return result;
        var deposits = new Dictionary<string, SaveCityTransfer>(StringComparer.Ordinal);
        var withdrawals = new Dictionary<string, SaveCityTransfer>(StringComparer.Ordinal);
        if (stamps != null)
        {
            foreach (SaveCityTransfer t in stamps)
            {
                if (t == null || string.IsNullOrEmpty(t.OpId)) continue;
                Dictionary<string, SaveCityTransfer> side = t.Deposit ? deposits : withdrawals;
                if (!side.ContainsKey(t.OpId)) side[t.OpId] = t;
            }
        }
        var ops = new HashSet<string>(StringComparer.Ordinal);
        if (appliedOps != null)
        {
            foreach (string op in appliedOps)
                if (!string.IsNullOrEmpty(op)) ops.Add(op);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (SavePendingTransfer p in pending)
        {
            if (p == null || string.IsNullOrEmpty(p.OpId) || !seen.Add(p.OpId)) continue;
            if (!string.IsNullOrEmpty(playerId) && !string.IsNullOrEmpty(p.Player) && p.Player != playerId) continue;
            long amount = WholeAmount(p.Amount);
            if (amount <= 0) continue;
            SaveCityTransfer same;
            bool hasSame = (p.Deposit ? deposits : withdrawals).TryGetValue(p.OpId, out same);
            bool hasOther = (p.Deposit ? withdrawals : deposits).ContainsKey(p.OpId);
            bool booked = hasSame || (!hasOther && ops.Contains(p.OpId));
            if (booked) result.Booked.Add(p.OpId);
            if (p.Deposit && !booked)
                result.UnbookedDeposits = SaturatedSum(result.UnbookedDeposits, amount);
            else if (!p.Deposit && booked)
                result.BookedWithdrawals = SaturatedSum(result.BookedWithdrawals, hasSame ? Math.Min(amount, Math.Max(0L, same.Amount)) : amount);
        }
        return result;
    }

    /// <summary>
    /// Transfers the city booked within CityTransferSettleSeconds of <paramref name="serverNow"/> (or later) that the save
    /// doesn't list as pending: the save may have been written just before they started.
    /// </summary>
    static void RecentUnlisted(SaveSnapshot save, SaveSubmitContext context, long serverNow, out long deposits, out long withdrawals)
    {
        deposits = 0;
        withdrawals = 0;
        if (context == null || context.CityTransfers == null) return;
        var listed = new HashSet<string>(StringComparer.Ordinal);
        foreach (SavePendingTransfer p in save.PendingTransfers)
            if (p != null && !string.IsNullOrEmpty(p.OpId)) listed.Add(p.OpId);
        foreach (SaveCityTransfer t in context.CityTransfers)
        {
            if (t == null || t.Amount <= 0 || t.Time < serverNow - CityTransferSettleSeconds) continue;
            if (!string.IsNullOrEmpty(t.OpId) && listed.Contains(t.OpId)) continue;
            if (t.Deposit) deposits = SaturatedSum(deposits, t.Amount);
            else withdrawals = SaturatedSum(withdrawals, t.Amount);
        }
    }

    static string SettleMessage(long amount, bool deposit)
    {
        return "The Online City booked a " + (deposit ? "deposit" : "withdrawal") + " of " + Money(amount) + " moments ago that this save doesn't show yet; " +
               "send the newer save.";
    }

    static long WholeAmount(double amount)
    {
        if (double.IsNaN(amount) || amount <= 0.0) return 0L;
        return amount >= long.MaxValue ? long.MaxValue : (long)Math.Floor(amount);
    }

    static long SaturatedSum(long a, long b)
    {
        long sum = a + b;
        if (a > 0 && b > 0 && sum < 0) return long.MaxValue;
        return sum;
    }

    /// <summary>
    /// The stored save before this one (save_data: packed, or JSON from before packing), or null when unusable. One bigger
    /// than any accepted save could be isn't parsed at all.
    /// </summary>
    static SaveSnapshot ParsePrevious(string stored)
    {
        if (string.IsNullOrEmpty(stored)) return null;
        if (stored.Length > (SavePacking.IsPacked(stored) ? SaveCloud.MaxPackedSaveBytes : SaveCloud.MaxSaveBytes)) return null;
        SaveSnapshot previous = SaveSnapshot.ParseStored(stored);
        return previous.Ok ? previous : null;
    }

    // ================================================================================================================
    // Flags
    // ================================================================================================================

    /// <summary>This submit's findings, one line list per flag reason.</summary>
    sealed class Findings
    {
        readonly Dictionary<string, List<string>> byReason = new Dictionary<string, List<string>>();
        readonly List<string> order = new List<string>();

        public int Count => order.Count;
        /// <summary>Reasons found, ledger first, then luck, growth, import (the order they are listed and flagged in).</summary>
        public IEnumerable<string> Reasons => order.OrderBy(Rank);

        public void Add(string reason, string detail)
        {
            if (!byReason.TryGetValue(reason, out List<string> lines))
            {
                lines = new List<string>();
                byReason[reason] = lines;
                order.Add(reason);
            }
            lines.Add(detail);
        }

        static int Rank(string reason)
        {
            switch (reason)
            {
                case SaveCodes.FlagLedger: return 0;
                case SaveCodes.FlagLuck: return 1;
                case SaveCodes.FlagGrowth: return 2;
                case SaveCodes.FlagImport: return 3;
                default: return 4;
            }
        }

        /// <summary>
        /// Put the findings into the meta: a reason the run has no flag for yet gets a new one (also listed in NewFlags);
        /// one it already has is updated in place (latest detail, seq and time; the first seq is kept in the text).
        /// </summary>
        public void ApplyTo(SaveMeta meta, SubmitDecision decision, long seq, long now)
        {
            if (meta.Flags == null) meta.Flags = new List<SaveFlag>();
            foreach (string reason in Reasons)
            {
                List<string> lines = byReason[reason];
                string detail = string.Join(" ", lines.Take(3)) + (lines.Count > 3 ? " (+" + (lines.Count - 3) + " more)" : "");
                decision.Findings.Add(reason + ": " + detail);
                SaveFlag open = meta.Flags.FirstOrDefault(f => f != null && f.Reason == reason);
                if (open == null)
                {
                    var flag = new SaveFlag { Reason = reason, Detail = detail, Seq = seq, At = now };
                    meta.Flags.Add(flag);
                    decision.NewFlags.Add(flag);
                }
                else
                {
                    long firstSeq = FirstSeq(open);
                    open.Detail = detail + " [again; first at seq " + firstSeq.ToString(Inv) + "]";
                    open.Seq = seq;
                    open.At = now;
                }
            }
        }

        static long FirstSeq(SaveFlag flag)
        {
            const string marker = "[again; first at seq ";
            string detail = flag.Detail ?? "";
            int at = detail.LastIndexOf(marker, StringComparison.Ordinal);
            if (at >= 0)
            {
                int start = at + marker.Length;
                int end = detail.IndexOf(']', start);
                if (end > start && long.TryParse(detail.Substring(start, end - start), NumberStyles.Integer, Inv, out long first)) return first;
            }
            return flag.Seq;
        }
    }

    /// <summary>Findings.Add, unless the walk is silent (<paramref name="findings"/> null).</summary>
    static void Note(Findings findings, string reason, string detail)
    {
        if (findings != null) findings.Add(reason, detail);
    }

    // ================================================================================================================
    // Helpers
    // ================================================================================================================

    static SubmitDecision Reject(string code, string message, SaveMeta current)
    {
        return new SubmitDecision { Code = code, Message = message, Meta = current };
    }

    /// <summary>Request and save agree on the value: within 50 cents, or a billionth of it for huge values.</summary>
    public static bool ValuesMatch(double a, double b)
    {
        return Math.Abs(a - b) <= Math.Max(0.5, 1e-9 * Math.Max(Math.Abs(a), Math.Abs(b)));
    }

    /// <summary>Text from the client for messages, logs and flag details: at most MaxClientText characters, no control characters ("?" when empty).</summary>
    public static string ClientText(string text)
    {
        if (string.IsNullOrEmpty(text)) return "?";
        int length = Math.Min(text.Length, MaxClientText);
        var sb = new StringBuilder(length + 3);
        for (int i = 0; i < length; i++)
        {
            char c = text[i];
            sb.Append(char.IsControl(c) ? '?' : c);
        }
        if (text.Length > MaxClientText) sb.Append("...");
        return sb.ToString();
    }

    static bool Finite(double x)
    {
        return !double.IsNaN(x) && !double.IsInfinity(x);
    }

    /// <summary>Rounding room when comparing an amount with a running bound.</summary>
    static double Tolerance(double bound)
    {
        return 1.0 + 1e-9 * Math.Abs(bound);
    }

    static string Money(double amount)
    {
        if (double.IsNaN(amount)) return "NaN";
        if (double.IsInfinity(amount) || Math.Abs(amount) >= 1e18) return (amount < 0 ? "-$" : "$") + Math.Abs(amount).ToString("0.###e0", Inv);
        return (amount < 0 ? "-$" : "$") + Math.Abs(amount).ToString("N0", Inv);
    }

    static string Luck(LuckTally t)
    {
        return t.Wins.ToString("0", Inv) + " wins of " + t.Count + " where honest odds give " + t.Expected.ToString("0.#", Inv) + " (z = " + t.Z.ToString("0.0", Inv) + ")";
    }
}

/// <summary>EvaluateSubmit's answer.</summary>
public class SubmitDecision
{
    /// <summary>SaveCodes value.</summary>
    public string Code;
    /// <summary>English, for logs.</summary>
    public string Message;
    /// <summary>Accepted: the meta to store. Rejected: the current meta, unchanged (may be null).</summary>
    public SaveMeta Meta;
    /// <summary>
    /// Accepted: the text to store as save_data, packed (the request's SaveGz as sent, or SavePacking.Pack of its SaveJson).
    /// Null for rejections.
    /// </summary>
    public string StoredSave;
    /// <summary>Flags this submit added (also already appended to Meta.Flags when accepted).</summary>
    public List<SaveFlag> NewFlags = new List<SaveFlag>();
    /// <summary>Every soft-check finding of this submit ("reason: detail"), new or repeated, for logs.</summary>
    public List<string> Findings = new List<string>();
    /// <summary>The growth check's numbers (null for rejections and imports).</summary>
    public SaveGrowthReport Growth;

    public bool Accepted => Code == SaveCodes.Accepted;
}

/// <summary>What the server knows besides the request (all optional).</summary>
public class SaveSubmitContext
{
    /// <summary>
    /// The save stored before this submit (save_data as stored: packed, or plain JSON from before packing): staff who left
    /// since still count toward the income allowance, shares held then count toward stock cash, and its loans, restarts and
    /// run peak are what this save's are measured from.
    /// </summary>
    public string PreviousSaveJson;
    /// <summary>
    /// The player's recent Online City transfers from the same read as the CityTotals (CityAccount deps / wds stamps): with
    /// CityOps, what ClassifyPending checks the save's pending transfers against.
    /// </summary>
    public List<SaveCityTransfer> CityTransfers = new List<SaveCityTransfer>();
    /// <summary>The op ids the player's city accounts applied (CityAccount ops, the last few hundred per city).</summary>
    public List<string> CityOps = new List<string>();
    /// <summary>The submitting player (Cloud Code's player id, the one the client stamps on its transfers); null = unknown.</summary>
    public string PlayerId;
}

/// <summary>One Online City transfer as the city server stamped it (CityAccount deps / wds: id, t, a).</summary>
public class SaveCityTransfer
{
    /// <summary>The client's operation id (matches CityOnline.PendingTransfer.opId in the save).</summary>
    public string OpId;
    /// <summary>Server time (unix seconds).</summary>
    public long Time;
    public long Amount;
    /// <summary>True for a deposit (company to city), false for a withdrawal.</summary>
    public bool Deposit;
}

/// <summary>The growth check of one submit, for logs and tests.</summary>
public class SaveGrowthReport
{
    public double ValueBefore;
    public double ValueAfter;
    public long Days;
    /// <summary>Company value the new ledger entries explain (gambles, cards and StockCash).</summary>
    public double LedgerNet;
    /// <summary>Cash stock trading brought in (sales net of cost basis, plus the drop in holdings cost).</summary>
    public double StockCash;
    /// <summary>Holdings cost in the previous save / this save (-1 = unknown).</summary>
    public double HoldingsBefore, HoldingsAfter;
    /// <summary>A stake that left the value before the last accepted save and came back after it (at most what the run's peak allows).</summary>
    public double Straddle;
    /// <summary>Online City withdrawals minus deposits since the last accept (server totals).</summary>
    public double CityNet;
    /// <summary>
    /// Money in transit to or from the city that is still the company's (unbooked pending deposits + booked pending
    /// withdrawals): at the last accept (meta) and in this save (against this read). Growth is measured on value + these.
    /// </summary>
    public double PendingBefore, PendingAfter;
    /// <summary>Bankruptcy hearings the save shows since the last accept (0 = none: no fresh start explains anything).</summary>
    public int Hearings;
    /// <summary>What shows them (loan, restart, forgiven loan, bust in the ledger, a save without the restart counter), or null.</summary>
    public string HearingEvidence;
    /// <summary>The most one hearing puts into the company: max($50k, 10% of the run's peak before it), 0 without a hearing.</summary>
    public double HearingRestart;
    /// <summary>The highest the run's peak can be now (accepted peak, the previous save's peak, the running bound's highest).</summary>
    public double PeakBound;
    public SaveAllowance Allowance;
    public double Slack;
    /// <summary>Growth nothing explains (positive = too much).</summary>
    public double Residual;
    /// <summary>The most the value could be if the company went bankrupt and restarted on the way (0 without a hearing).</summary>
    public double FreshStartBound;
    /// <summary>The Online City accounts couldn't be read: city money is left out and growth is never flagged.</summary>
    public bool CityUnknown;
    /// <summary>Nothing explains the growth (flagged unless the city couldn't be read).</summary>
    public bool Unexplained;
    public bool Flagged;
}

/// <summary>SaveRules.ClassifyPending's answer: a save's pending city transfers against one read of the player's accounts.</summary>
public class SavePendingCheck
{
    /// <summary>Pending deposits the city hasn't booked: taken from the company, in no bank yet.</summary>
    public long UnbookedDeposits;
    /// <summary>Pending withdrawals the city has booked: out of a bank, not in the company yet.</summary>
    public long BookedWithdrawals;
    /// <summary>The op ids of the counted transfers the city has booked (settled once the company reflects them).</summary>
    public List<string> Booked = new List<string>();
}

/// <summary>SaveRules.IncomeAllowance's breakdown.</summary>
public class SaveAllowance
{
    public long Days;
    public int Headcount;
    public long Hires;
    /// <summary>New faces since the previous save (by id), and the most hiring clicks allow in the stretch (only that many count).</summary>
    public int NewStaff;
    public long HireCap;
    /// <summary>Every employee's maximum daily output, before Team Synergy.</summary>
    public double EmployeeOutput;
    /// <summary>Largest operating result of one day.</summary>
    public double OperatingMax;
    /// <summary>Operating result with growth skills and merger bonus: the day's income before luck.</summary>
    public double DailyFixed;
    public double HireImpact;
    public double Tasks;
    public double Skips;
    public double Crises;
    /// <summary>All daily ticks (income and luck).</summary>
    public double Daily;
    /// <summary>Board meetings that can have paid a bonus in this stretch.</summary>
    public long Meetings;
    public double Board;
    /// <summary>Everything, times SaveCalibration.IncomeSafetyFactor.</summary>
    public double Total;

    public SaveAllowance Clone()
    {
        return (SaveAllowance)MemberwiseClone();
    }
}
