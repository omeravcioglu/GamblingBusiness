using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Newtonsoft.Json;

/// <summary>
/// The cloud save's contract, shared word for word by the Unity client (CloudSaveSync) and the GameSave Cloud Code module
/// (GameSaveCCM compiles this folder): keys, endpoints, request/response shapes and result codes.
/// - Pure C#, no UnityEngine: the module runs on .NET 9, the client on Unity's .NET Standard 2.1 (C# 9).
/// - Players can't write Cloud Save (Assets/CloudCode/OnlineCity.ac denies it), so the save only changes through GameSave's
///   endpoints, after SaveRules has checked it. The client reads its own save directly: protected player items DataKey and
///   MetaKey.
/// - Change it only together with the deployed module: an old module and a new client must still understand each other.
/// </summary>
public static class SaveCloud
{
    public const int ProtocolVersion = 1;

    /// <summary>Cloud Code module name (GameSaveCCM/GameSave.sln, Assets/CloudCode/GameSave.ccmr).</summary>
    public const string ModuleName = "GameSave";
    /// <summary>SubmitSave(string request): request = SubmitRequest JSON → SaveResult JSON.</summary>
    public const string FnSubmit = "SubmitSave";
    /// <summary>
    /// NewRun(string runId, long knownAcceptedAt): the player started a new company → SaveResult JSON (idempotent for the
    /// same run). knownAcceptedAt = the meta.AcceptedAt this client last saw from the server (0 if it never synced). When
    /// the server's live run was accepted AFTER that (another PC kept playing since this client last synced), the answer
    /// is Superseded and nothing changes: the client sets its offline company aside instead of wiping the other one.
    /// </summary>
    public const string FnNewRun = "NewRun";
    /// <summary>
    /// EndRun(string runId, long baseSeq, long inFlightSeq, string inFlightHash): the run is over (Game Over) → SaveResult
    /// JSON. Other runs are left alone. The run only ends when the ending copy is up to date with the server: meta.Seq ≤
    /// baseSeq, or meta.Seq == inFlightSeq and the stored save's SavePacking.Hash equals inFlightHash (an upload whose answer
    /// was lost). Otherwise the answer is Stale and nothing changes: another PC moved the company on since this copy.
    /// </summary>
    public const string FnEndRun = "EndRun";

    /// <summary>
    /// Protected player item: the save as a JSON string value, packed (SavePacking.Pack of the SaveData JSON text exactly
    /// as JsonUtility wrote it). Readers use SavePacking.Unpack, which also takes an unpacked JSON text.
    /// </summary>
    public const string DataKey = "save_data";
    /// <summary>Protected player item: SaveMeta, what the server accepted last.</summary>
    public const string MetaKey = "save_meta";
    /// <summary>Online City accounts are protected player items "acct_&lt;city&gt;" (CityAccount; lifetime totals in stats.dep / stats.wd).</summary>
    public const string CityAccountPrefix = "acct_";

    /// <summary>
    /// Largest save the server takes, unpacked (UTF-8 bytes). A new company's save is ~50 KB; each employee adds ~400-700
    /// bytes (6,000 employees ≈ 2.4 MB), and busy companies reach thousands of employees. 8 MB keeps the module's memory in
    /// check: every submit parses the new save and the previous one.
    /// </summary>
    public const int MaxSaveBytes = 8 * 1024 * 1024;
    /// <summary>Largest packed save (SavePacking.Pack text) the server takes. Cloud Save caps one item at 5 MB.</summary>
    public const int MaxPackedSaveBytes = 2 * 1024 * 1024;
    /// <summary>
    /// Largest SubmitSave request body the client sends (the serialized request JSON, UTF-8 bytes). Cloud Code documents a
    /// 1 MB request limit for scripts; modules are unconfirmed, so the client stays under it until a test deployment shows
    /// more is accepted. A bigger request is not sent: the save stays local and the status shows TooBig.
    /// </summary>
    public const int MaxRequestBytes = 950 * 1024;

    /// <summary>
    /// Cloud Save's rule for item keys: 1-255 characters, only letters, digits, '_' and '-' (NO dots: "save.data" was
    /// rejected with CloudSaveValidationException until 2026-09-29). Every key this contract defines passes it
    /// (GameSave.Tests/CloudKeyTests).
    /// </summary>
    public static bool IsValidKey(string key)
    {
        if (string.IsNullOrEmpty(key) || key.Length > 255) return false;
        foreach (char c in key)
        {
            bool ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '-';
            if (!ok) return false;
        }
        return true;
    }

    /// <summary>Seconds since 1970 (UTC). Only the server's clock decides anything; the client sends its own for logs.</summary>
    public static long UnixNow()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }
}

/// <summary>SaveResult.Code values and SaveFlag.Reason values.</summary>
public static class SaveCodes
{
    /// <summary>Stored. SaveResult.Meta is the new meta.</summary>
    public const string Accepted = "accepted";
    /// <summary>Another device got ahead (seq not newer, or the client's chain doesn't continue from the server's): adopt the server copy.</summary>
    public const string Stale = "stale";
    /// <summary>The run is over (Game Over): the company can't continue.</summary>
    public const string Ended = "ended";
    /// <summary>The request's run isn't the server's current run: NewRun if this client started it, else adopt the server copy.</summary>
    public const string NoRun = "no_run";
    /// <summary>More in-game days passed than real time allows: adopt the server copy.</summary>
    public const string Time = "time";
    /// <summary>Broken, oversized or inconsistent save: adopt the server copy.</summary>
    public const string Invalid = "invalid";
    /// <summary>Someone else wrote at the same moment (write lock): try again.</summary>
    public const string Busy = "busy";
    /// <summary>Server problem: keep the local save and try again later.</summary>
    public const string Error = "error";
    /// <summary>
    /// NewRun refused: the server's live run got new progress after this client last synced (knownAcceptedAt), so a New
    /// Game made offline on an out-of-date PC mustn't replace it. Keep the offline company aside, adopt the server's.
    /// </summary>
    public const string Superseded = "superseded";

    /// <summary>Flag: company value grew more than the recorded gambles, cards, trades, city transfers and income explain.</summary>
    public const string FlagGrowth = "growth";
    /// <summary>Flag: a recorded gamble, card or trade that can't happen (payout or change above its bound).</summary>
    public const string FlagLedger = "ledger";
    /// <summary>Flag: luck that honest odds almost never give (z &gt; 5 over 50+ results).</summary>
    public const string FlagLuck = "luck";
    /// <summary>Flag: the first save of a run the server never saw started was already implausibly rich.</summary>
    public const string FlagImport = "import";
}

/// <summary>LedgerEntry.Kind values.</summary>
public static class LedgerKinds
{
    /// <summary>A voluntary gamble (GambleLedger.Report): Source = its source, Detail = the casino game, Stake / Payout.</summary>
    public const string Gamble = "gamble";
    /// <summary>A risk card: Source = "high" / "balanced" / "low", Good, ValueBefore, Change (company value change).</summary>
    public const string Card = "card";
    /// <summary>A stock sale: Source = symbol, Stake = cost basis of the shares sold, Payout = proceeds.</summary>
    public const string Trade = "trade";
    /// <summary>Older gambles of one source folded together when the buffer grew too big: Count, Wins, Stake / Payout sums.</summary>
    public const string Summary = "summary";
    /// <summary>
    /// Money a manager decision moved (one-off outcomes only; recurring manager effects are costs through
    /// DailyEconomyHooks): Source = situation id, Detail = choice id, ValueBefore, Change = company value change applied,
    /// Stake = money put at risk by the choice (0 if none). Risky choices also report to GambleLedger for the luck tally.
    /// The server bounds these per situation/choice: only investor_share may add money (a tier-based cap, at most one
    /// payout per ~40 days, its revenue share listed in the "manager" extra); manager gambles follow per-choice rules
    /// (SaveRules / SaveCalibration).
    /// </summary>
    public const string Manager = "manager";
}

/// <summary>What the server accepted last for this player (protected item SaveCloud.MetaKey).</summary>
public class SaveMeta
{
    [JsonProperty("v")] public int Version = SaveCloud.ProtocolVersion;
    /// <summary>The current run (company) id, made by the client at New Game.</summary>
    [JsonProperty("run")] public string RunId;
    /// <summary>The client's save counter of the last accepted save (0 right after NewRun).</summary>
    [JsonProperty("seq")] public long Seq;
    [JsonProperty("day")] public int Day;
    /// <summary>Company value of the last accepted save (what the Online City may let the player wire out).</summary>
    [JsonProperty("value")] public double Value;
    /// <summary>Highest accepted value in this run.</summary>
    [JsonProperty("peak")] public double Peak;
    /// <summary>Server time the run started (NewRun, or the first submit of an imported run).</summary>
    [JsonProperty("startedAt")] public long StartedAt;
    /// <summary>
    /// Server time of the last accepted save. Right after NewRun it is the previous meta's AcceptedAt (the player's last
    /// accepted contact: the earliest a run started offline could have begun), or 0 when the player had no meta (no time
    /// baseline: the run's first save is then checked like an import).
    /// </summary>
    [JsonProperty("acceptedAt")] public long AcceptedAt;
    /// <summary>Game Over: the run can't continue (its save_data is deleted).</summary>
    [JsonProperty("ended")] public bool Ended;
    /// <summary>Highest ledger index already counted (entries at or below it are ignored when sent again).</summary>
    [JsonProperty("ledger")] public long LedgerIndex;
    /// <summary>The player's lifetime Online City deposits (sum of stats.dep over all cities) when the last save was accepted.</summary>
    [JsonProperty("cityDep")] public long CityDeposited;
    /// <summary>The player's lifetime Online City withdrawals (sum of stats.wd) when the last save was accepted.</summary>
    [JsonProperty("cityWd")] public long CityWithdrawn;
    /// <summary>
    /// Money in transit at the last accept, classified by the SERVER against the city's own records (never taken from the
    /// client's word): the accepted save's pending deposits the city had NOT booked (already taken from the company, not
    /// yet in a bank)...
    /// </summary>
    [JsonProperty("pendDep")] public long PendingDepositsUnbooked;
    /// <summary>...and its pending withdrawals the city HAD booked (already out of a bank, not yet paid to the company).
    /// Growth is checked on the effective value = value + these two, on both sides of a submit.</summary>
    [JsonProperty("pendWd")] public long PendingWithdrawalsBooked;
    /// <summary>Soft-check findings for this run (empty = clean). A flagged run can't wire money into the Online City.</summary>
    [JsonProperty("flags")] public List<SaveFlag> Flags = new List<SaveFlag>();
    /// <summary>Running luck tallies of this run.</summary>
    [JsonProperty("luck")] public LuckStats Luck = new LuckStats();

    [JsonIgnore] public bool Flagged => Flags != null && Flags.Count > 0;
}

/// <summary>One soft-check finding.</summary>
public class SaveFlag
{
    /// <summary>SaveCodes.Flag... value.</summary>
    [JsonProperty("reason")] public string Reason;
    /// <summary>English detail for the developer (amounts, which entry).</summary>
    [JsonProperty("detail")] public string Detail;
    /// <summary>The submit that raised it.</summary>
    [JsonProperty("seq")] public long Seq;
    [JsonProperty("at")] public long At;
}

/// <summary>Luck of one kind of outcome: n results with win chances p, "wins" of them won.</summary>
public class LuckTally
{
    [JsonProperty("n")] public int Count;
    [JsonProperty("w")] public double Wins;
    /// <summary>Σp: wins honest odds expect.</summary>
    [JsonProperty("e")] public double Expected;
    /// <summary>Σp(1-p).</summary>
    [JsonProperty("var")] public double Variance;

    public void Add(double winChance, bool won)
    {
        if (double.IsNaN(winChance) || winChance <= 0.0 || winChance >= 1.0) return;
        Count++;
        if (won) Wins += 1.0;
        Expected += winChance;
        Variance += winChance * (1.0 - winChance);
    }

    /// <summary>Standard deviations above expectation (0 without results).</summary>
    [JsonIgnore] public double Z => Variance > 0.0 ? (Wins - Expected) / Math.Sqrt(Variance) : 0.0;
}

/// <summary>A run's luck: gambles with known odds and risk cards.</summary>
public class LuckStats
{
    [JsonProperty("g")] public LuckTally Gambles = new LuckTally();
    [JsonProperty("c")] public LuckTally Cards = new LuckTally();
    /// <summary>Rare payouts (jackpot-size gambles and cards) over the whole run, so one fake jackpot per upload adds up.</summary>
    [JsonProperty("r")] public LuckTally Rare = new LuckTally();
}

/// <summary>SubmitSave's request: the save and what happened since the last accepted one.</summary>
public class SubmitRequest
{
    [JsonProperty("v")] public int Version = SaveCloud.ProtocolVersion;
    [JsonProperty("run")] public string RunId;
    /// <summary>This save's counter (grows by one on every local write).</summary>
    [JsonProperty("seq")] public long Seq;
    /// <summary>The server seq this local chain continues from (last accepted or downloaded).</summary>
    [JsonProperty("base")] public long BaseSeq;
    /// <summary>Must equal the save's currentDay.</summary>
    [JsonProperty("day")] public int Day;
    /// <summary>Must equal the save's companyValue.</summary>
    [JsonProperty("value")] public double Value;
    /// <summary>
    /// The SaveData JSON text (JsonUtility), unpacked. Clients send SaveGz instead and leave this empty; the server fills it
    /// from SaveGz before any check, so the rules always see the JSON text here.
    /// </summary>
    [JsonProperty("save")] public string SaveJson;
    /// <summary>The same save packed with SavePacking.Pack (what clients send: ~3x smaller on real saves). Takes precedence over SaveJson.</summary>
    [JsonProperty("saveGz")] public string SaveGz;
    /// <summary>Ledger entries not yet accepted, oldest first (unpacked form; clients send LedgerGz instead).</summary>
    [JsonProperty("ledger")] public List<LedgerEntry> Ledger = new List<LedgerEntry>();
    /// <summary>
    /// The same ledger packed: SavePacking.Pack of the JSON array of LedgerEntry. Takes precedence over Ledger (the server
    /// unpacks it into Ledger before any check). Clients send it and leave Ledger empty: unpacked, a full ledger was up to
    /// ~0.45 MB of the request.
    /// </summary>
    [JsonProperty("ledgerGz")] public string LedgerGz;
    /// <summary>Application.version, for logs.</summary>
    [JsonProperty("build")] public string Build;
    /// <summary>Sent from the Editor or a development build (logs only; never loosens a check).</summary>
    [JsonProperty("dev")] public bool Dev;
    /// <summary>The client's clock (unix seconds), logs only.</summary>
    [JsonProperty("clientTime")] public long ClientTime;
}

/// <summary>One money event the server can check: a gamble, a risk card or a stock sale (see LedgerKinds).</summary>
public class LedgerEntry
{
    /// <summary>Running index within the run (1, 2, 3...).</summary>
    [JsonProperty("i")] public long Index;
    [JsonProperty("k")] public string Kind;
    [JsonProperty("src")] public string Source;
    [JsonProperty("det")] public string Detail;
    [JsonProperty("stake")] public double Stake;
    [JsonProperty("pay")] public double Payout;
    [JsonProperty("before")] public double ValueBefore;
    [JsonProperty("change")] public double Change;
    [JsonProperty("good")] public bool Good;
    [JsonProperty("day")] public int Day;
    /// <summary>Summary entries: gambles folded in (1 for every other entry).</summary>
    [JsonProperty("n")] public int Count = 1;
    /// <summary>Summary entries: how many of them were won.</summary>
    [JsonProperty("wins")] public int Wins;

    [JsonIgnore] public double Net => Payout - Stake;
}

/// <summary>What every endpoint returns (as JSON text).</summary>
public class SaveResult
{
    [JsonProperty("ok")] public bool Ok;
    /// <summary>SaveCodes value.</summary>
    [JsonProperty("code")] public string Code;
    /// <summary>English, for logs (the client shows its own translated text).</summary>
    [JsonProperty("msg")] public string Message;
    /// <summary>The server's meta after the call (null when the player has none).</summary>
    [JsonProperty("meta")] public SaveMeta Meta;
    /// <summary>Flags this call added (empty for a clean save).</summary>
    [JsonProperty("flags")] public List<SaveFlag> NewFlags = new List<SaveFlag>();
    [JsonProperty("serverTime")] public long ServerTime;

    public static SaveResult From(string code, string message, SaveMeta meta, long serverTime)
    {
        return new SaveResult { Ok = code == SaveCodes.Accepted, Code = code, Message = message, Meta = meta, ServerTime = serverTime };
    }
}

/// <summary>
/// The save's packed form, for the wire (SubmitRequest.SaveGz) and for storage (save_data): "gz1:" + base64 of the gzip of
/// the UTF-8 JSON text. Measured on realistic saves (GUID ids, float digits, staff records) it shrinks about 3x, ~240 bytes
/// per employee packed, so MaxPackedSaveBytes holds ~8,000 employees and MaxRequestBytes ~3,500.
/// Hash is the one fingerprint both sides use to recognise a save (a lost answer, EndRun's up-to-date check).
/// </summary>
public static class SavePacking
{
    /// <summary>SHA-256 of the UTF-8 bytes of the UNPACKED save text, as 64 lowercase hex characters ("" for null).</summary>
    public static string Hash(string json)
    {
        if (json == null) return "";
        using (var sha = System.Security.Cryptography.SHA256.Create())
        {
            byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(json));
            var hex = new StringBuilder(digest.Length * 2);
            foreach (byte b in digest) hex.Append(b.ToString("x2"));
            return hex.ToString();
        }
    }

    /// <summary>Marks packed text (an unpacked save is JSON and starts with '{').</summary>
    public const string Prefix = "gz1:";

    /// <summary>Pack a save's JSON text (null for null).</summary>
    public static string Pack(string json)
    {
        if (json == null) return null;
        byte[] raw = Encoding.UTF8.GetBytes(json);
        using (var buffer = new MemoryStream())
        {
            using (var gzip = new GZipStream(buffer, CompressionLevel.Optimal, true))
                gzip.Write(raw, 0, raw.Length);
            return Prefix + Convert.ToBase64String(buffer.GetBuffer(), 0, (int)buffer.Length);
        }
    }

    /// <summary>True when <paramref name="stored"/> is packed text (starts with Prefix).</summary>
    public static bool IsPacked(string stored)
    {
        return stored != null && stored.StartsWith(Prefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// The save's JSON text from packed text; unpacked text (JSON) comes back unchanged, null stays null. Throws
    /// FormatException / InvalidDataException for broken packed text, and InvalidDataException when the unpacked save would
    /// be bigger than <paramref name="maxBytes"/> (a tiny packed text can't blow up into gigabytes).
    /// </summary>
    public static string Unpack(string stored, int maxBytes = SaveCloud.MaxSaveBytes)
    {
        if (stored == null || !IsPacked(stored)) return stored;
        byte[] packed = Convert.FromBase64String(stored.Substring(Prefix.Length));
        using (var input = new MemoryStream(packed))
        using (var gzip = new GZipStream(input, CompressionMode.Decompress))
        using (var output = new MemoryStream())
        {
            var chunk = new byte[81920];
            int read;
            while ((read = gzip.Read(chunk, 0, chunk.Length)) > 0)
            {
                if (output.Length + read > maxBytes)
                    throw new InvalidDataException("The packed save unpacks to more than " + maxBytes + " bytes.");
                output.Write(chunk, 0, read);
            }
            return Encoding.UTF8.GetString(output.GetBuffer(), 0, (int)output.Length);
        }
    }
}

/// <summary>The player's lifetime Online City transfers, summed over every city account the server could read.</summary>
public class CityTotals
{
    public long Deposited;
    public long Withdrawn;
    /// <summary>False when the accounts couldn't be read (the local test server, or a read error): city money is then left out of the checks.</summary>
    public bool Known;
}
