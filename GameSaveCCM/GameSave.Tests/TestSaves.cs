using Newtonsoft.Json;

namespace GameSave.Tests;

/// <summary>Builds save JSON shaped like the game's JsonUtility output (SaveSystem/SaveData.cs), with the extras the rules read.</summary>
public class SaveBuilder
{
    public double Value = 50000;
    public int Day = 1;
    public List<SaveEmployeeInfo> Employees = new();
    public int Fired;
    public double RunPeak;
    public List<(string Id, int[] Levels)> Rooms = new() { ("lobby", new[] { 0, 0 }), ("ceo", new[] { 0, 0, 0 }) };
    /// <summary>
    /// The campus as the game saves it now (the "building" extra's format 4, TowerSave.Capture): every building's rooms in
    /// one flat list with their room levels, then the buildings and skybridges. Null = format 3 from Rooms (one tower).
    /// </summary>
    public List<CampusRoom> Campus;
    /// <summary>Format 4 buildings left to right, the HQ first: (type id, floors). Null = the HQ alone, as tall as its rooms need.</summary>
    public List<(string Type, int Floors)> Buildings;
    /// <summary>Format 4 skybridges: (index of the left building, floor).</summary>
    public List<(int Left, int Floor)> Skybridges;
    /// <summary>The campus's build budget (format 4 "budget").</summary>
    public double BuildBudget;
    /// <summary>The manager's revenue shares running (the "manager" extra, ManagerSaveState.shares); null = no "manager" extra.</summary>
    public List<(string Id, int Start, int Until)> ManagerShares;
    public int SofaLevel;
    /// <summary>Board meetings held this run (null = no "board" extra).</summary>
    public int? MeetingsHeld;
    /// <summary>Shares held (the "stocks" extra); null = no "stocks" extra.</summary>
    public List<(string Symbol, long Shares, double AveragePrice)> Holdings = new();
    /// <summary>Online City transfers waiting for the city's answer (the "onlineCity" extra); null = no extra.</summary>
    public List<(string OpId, string Kind, long Amount)> Pending;
    /// <summary>The account each pending transfer names (op id -> player; null = no player field, as older saves). Default "player-1".</summary>
    public Dictionary<string, string> PendingPlayers;
    /// <summary>null = the save doesn't list skills.</summary>
    public List<string> Skills = new();
    /// <summary>Extra text (to make a save big).</summary>
    public string Padding;
    /// <summary>runStats "restarts" (bankruptcy restarts this run); null = a save from before the counter (no field).</summary>
    public int? Restarts;
    /// <summary>The "loan" extra (BankLoan.State); null = no extra.</summary>
    public (int Taken, double Debt, double Repaid)? Loan;

    public SaveBuilder Copy()
    {
        var copy = (SaveBuilder)MemberwiseClone();
        copy.Employees = Employees.Select(e => new SaveEmployeeInfo { Id = e.Id, IQ = e.IQ, GPA = e.GPA, Experience = e.Experience, Age = e.Age, PhD = e.PhD, Criminal = e.Criminal }).ToList();
        copy.Rooms = Rooms.ToList();
        copy.Campus = Campus?.ToList();
        copy.Buildings = Buildings?.ToList();
        copy.Skybridges = Skybridges?.ToList();
        copy.ManagerShares = ManagerShares?.ToList();
        copy.Skills = Skills?.ToList();
        copy.Holdings = Holdings?.ToList();
        copy.Pending = Pending?.ToList();
        copy.PendingPlayers = PendingPlayers == null ? null : new Dictionary<string, string>(PendingPlayers);
        return copy;
    }

    public SaveBuilder With(Action<SaveBuilder> change)
    {
        SaveBuilder copy = Copy();
        change(copy);
        return copy;
    }

    public string Json()
    {
        var employees = Employees.Select((e, i) => new Dictionary<string, object>
        {
            ["name"] = "Employee " + i,
            ["age"] = e.Age > 0 ? e.Age : 40,
            ["gender"] = "Female",
            ["race"] = "Asian",
            ["degree"] = e.PhD ? "PhD" : "Bachelor",
            ["major"] = "Economics",
            ["GPA"] = e.GPA,
            ["IQ"] = e.IQ,
            ["salary"] = 80000,
            ["experienceYears"] = e.Experience,
            ["criminalRecord"] = e.Criminal,
            ["isGolden"] = false,
            ["portraitIndex"] = -1,
            ["traits"] = new object[0],
            ["id"] = e.Id
        }).ToList();
        var extras = new List<object>
        {
            new { key = "staff", json = JsonConvert.SerializeObject(new { version = 1, fired = Fired, severancePaid = 0.0, records = new object[0] }) },
            new
            {
                key = "runStats",
                json = Restarts.HasValue
                    ? JsonConvert.SerializeObject(new { peakValue = Math.Max(RunPeak, Value), hires = Employees.Count, restarts = Restarts.Value })
                    : JsonConvert.SerializeObject(new { peakValue = Math.Max(RunPeak, Value), hires = Employees.Count })
            },
            new
            {
                key = "building",
                json = Campus != null
                    ? CampusJson()
                    : JsonConvert.SerializeObject(new
                    {
                        version = 3, budget = 0.0, floors = 1, introSeen = true,
                        rooms = Rooms.Select((r, i) => new { id = r.Id, floor = 1 + i / 2, tile = (i % 2) * 4, paid = 0.0, levels = r.Levels, furniturePaid = r.Levels.Select(_ => 0.0) })
                    })
            }
        };
        if (MeetingsHeld.HasValue)
            extras.Add(new { key = "board", json = JsonConvert.SerializeObject(new { version = 1, nextMeetingDay = 10, meetingsHeld = MeetingsHeld.Value }) });
        if (Holdings != null)
        {
            // StockMarketManager.StockMarketSaveData: every listed stock, owned or not
            var stocks = new List<object> { new { symbol = "AAPL", currentPrice = 150.0, previousPrice = 150.0, basePrice = 150.0, ownedShares = 0L, averageBuyPrice = 0.0 } };
            stocks.AddRange(Holdings.Select(h => (object)new { symbol = h.Symbol, currentPrice = h.AveragePrice, previousPrice = h.AveragePrice, basePrice = h.AveragePrice, ownedShares = h.Shares, averageBuyPrice = h.AveragePrice }));
            extras.Insert(0, new { key = "stocks", json = JsonConvert.SerializeObject(new { version = 1, stocks }) });
        }
        if (Pending != null)
        {
            var pending = Pending.Select(p =>
            {
                var entry = new Dictionary<string, object> { ["opId"] = p.OpId, ["kind"] = p.Kind, ["city"] = "ist" };
                string player = "player-1";
                if (PendingPlayers != null && PendingPlayers.TryGetValue(p.OpId, out string named)) player = named;
                if (player != null) entry["player"] = player;
                entry["amount"] = p.Amount;
                entry["time"] = T.T0;
                return entry;
            });
            extras.Add(new { key = "onlineCity", json = JsonConvert.SerializeObject(new { unlocked = true, certificateShown = true, pending }) });
        }
        if (Loan.HasValue)
        {
            // BankLoan.State as JsonUtility writes it
            var loan = Loan.Value;
            extras.Add(new { key = "loan", json = JsonConvert.SerializeObject(new { debt = loan.Debt, borrowed = loan.Debt, loansTaken = loan.Taken, repaid = loan.Repaid, interest = 0.0 }) });
        }
        if (ManagerShares != null)
        {
            // ManagerSaveState as JsonUtility writes it (Manager/ManagerState.cs:192-225), field order kept: the shares come
            // after the desk, before the moods of every employee
            var person = new { name = "Dana Reyes", gender = "Female", portraitIndex = 3, age = 44, years = 18, personality = "numbers", salary = 90000.0, expectedBase = 85000.0,
                               satisfaction = 64.5, competence = 1.04, hiredDay = 5, bio = 2, raises = 1, bonuses = 0, promisesBroken = 0 };
            extras.Add(new
            {
                key = "manager",
                json = JsonConvert.SerializeObject(new
                {
                    version = 1, phase = "active", manager = person, candidates = new object[0], gapUntilDay = 0, pickShown = true, managersHired = 1, nextUid = 9, lastRollDay = Day,
                    pending = new[] { new { uid = 8, situation = "joint_marketing", createdDay = Math.Max(0, Day - 2), expiresDay = Day + 10, variant = 1, personality = "numbers", urgent = false,
                                            interrupt = false, interruptDone = false, revealed = false, again = false, withheld = false, seen = true, targets = new object[0],
                                            prms = new[] { new { name = "cost", value = 25000.0, format = "money" } } } },
                    followups = new object[0],
                    cooldowns = new[] { new { id = "joint_marketing", day = Math.Max(0, Day - 2) } },
                    categoryDays = new object[0], flags = new object[0], seen = new[] { "joint_marketing", "investor_share" }, chosen = new[] { "investor_share.accept" },
                    policies = new object[0], rentals = new object[0],
                    shares = ManagerShares.Select(s => new { id = s.Id, pct = 15.0, startDay = s.Start, untilDay = s.Until, paid = 12345.0 }),
                    perks = new object[0],
                    moods = Employees.Select(e => new { id = e.Id ?? "", mood = 55.0 }),
                    recentProfits = new[] { 1000.0, 2000.0 }, recentValues = new[] { Value }, gambles = new object[0], firedDays = new int[0], hiredDays = new int[0],
                    losingDays = 0, lowDays = 0,
                    stats = new { created = 4, decided = 3, expired = 0, followed = 1, overridden = 1, delegated = 0, interrupts = 0, resigned = 0, moneyIn = 0.0, moneyOut = 0.0, recurringPaid = 0.0 },
                    history = new[] { new { situation = "investor_share", choice = "accept", day = 12, how = "player" } }
                })
            });
        }
        var root = new Dictionary<string, object>
        {
            ["saveVersion"] = "2.0",
            ["savedAt"] = "2026-09-28 20:54:26",
            ["companyValue"] = Value,
            ["currentDay"] = Day,
            ["hasHiredFirst"] = Employees.Count > 0,
            ["employeeHappiness"] = 50.0,
            ["investorTrust"] = 50.0,
            ["employees"] = employees,
            ["skillPoints"] = 0,
            ["totalHighIQHires"] = 0,
            ["officeItems"] = new[] { new { itemName = "Sofa", currentLevel = SofaLevel } },
            ["hasUnlockedOnlineWorld"] = false,
            ["companyName"] = Padding ?? "Test Co",
            ["extras"] = extras
        };
        if (Skills != null) root["unlockedSkillIds"] = Skills;
        return JsonConvert.SerializeObject(root, Formatting.Indented);
    }

    /// <summary>The "building" extra in format 4, field for field as JsonUtility writes TowerSaveData (TowerSave.cs:9-67).</summary>
    string CampusJson()
    {
        List<(string Type, int Floors)> buildings = Buildings ?? new() { ("hq", Math.Max(1, Campus.Where(r => r.Building == 0).Select(r => r.Floor).DefaultIfEmpty(1).Max())) };
        return JsonConvert.SerializeObject(new
        {
            version = 4,
            budget = BuildBudget,
            floors = buildings[0].Floors,
            introSeen = true,
            rooms = Campus.Select(r => new
            {
                id = r.Id, building = r.Building, floor = r.Floor, tile = r.Tile, paid = 50000.0, level = r.Level, levelPaid = r.Level > 1 ? 150000.0 * (r.Level - 1) : 0.0,
                levels = r.Levels, furniturePaid = r.Levels.Select(l => l * 2660.0)
            }),
            buildings = buildings.Select((b, i) => new { type = b.Type, floors = b.Floors, paid = i == 0 ? 0.0 : 400000.0, floorsPaid = 150000.0 * (b.Floors - 1), floorTypes = new string[0] }),
            connections = (Skybridges ?? new()).Select(s => new { type = "skybridge", left = s.Left, floor = s.Floor, paid = 150000.0 + 20000.0 * (s.Floor - 1) })
        });
    }
}

/// <summary>One room of a format 4 campus: its id, building index (0 = the HQ), floor, first tile, room level (1-based; 0 = 1) and furniture levels.</summary>
public record CampusRoom(string Id, int Building, int Floor, int Tile, int Level, int[] Levels);

/// <summary>Requests, ledger entries and metas for the tests.</summary>
public static class T
{
    public const long T0 = 1_800_000_000L;
    public const string Run = "run-1";

    public static SaveEmployeeInfo Emp(string id, int iq = 100, double gpa = 2.5, int exp = 10, bool phd = false, int age = 40)
        => new SaveEmployeeInfo { Id = id, IQ = iq, GPA = gpa, Experience = exp, PhD = phd, Age = age };

    public static List<SaveEmployeeInfo> Staff(int count, int iq = 100, double gpa = 2.5, int exp = 10, bool phd = false, string prefix = "e")
        => Enumerable.Range(0, count).Select(i => Emp(prefix + i, iq, gpa, exp, phd)).ToList();

    public static SaveMeta Meta(long seq = 1, int day = 10, double value = 1_000_000, long acceptedAt = T0, long startedAt = T0 - 3600,
                                long ledger = 0, string run = Run, long cityDep = 0, long cityWd = 0)
        => new SaveMeta
        {
            RunId = run, Seq = seq, Day = day, Value = value, Peak = value, AcceptedAt = acceptedAt, StartedAt = startedAt, LedgerIndex = ledger,
            CityDeposited = cityDep, CityWithdrawn = cityWd
        };

    public static SubmitRequest Req(long seq, long baseSeq, SaveBuilder save, params LedgerEntry[] ledger)
        => Req(Run, seq, baseSeq, save, ledger);

    public static SubmitRequest Req(string run, long seq, long baseSeq, SaveBuilder save, params LedgerEntry[] ledger)
        => new SubmitRequest { RunId = run, Seq = seq, BaseSeq = baseSeq, Day = save.Day, Value = save.Value, SaveJson = save.Json(), Ledger = ledger.ToList(), Build = "test" };

    public static CityTotals City(long deposited = 0, long withdrawn = 0) => new CityTotals { Deposited = deposited, Withdrawn = withdrawn, Known = true };
    public static CityTotals NoCity => new CityTotals { Known = false };

    public static LedgerEntry Gamble(long i, string source, string detail, double stake, double payout, double before, int day = 0)
        => new LedgerEntry { Index = i, Kind = LedgerKinds.Gamble, Source = source, Detail = detail, Stake = stake, Payout = payout, ValueBefore = before, Day = day };

    public static LedgerEntry Flip(long i, double stake, bool won, double before, int day = 0)
        => Gamble(i, "casino", "coinflip", stake, won ? Math.Round(stake * 1.95, 2) : 0.0, before, day);

    public static LedgerEntry Card(long i, string risk, bool good, double before, double change, int day = 0)
        => new LedgerEntry { Index = i, Kind = LedgerKinds.Card, Source = risk, Good = good, ValueBefore = before, Change = change, Day = day };

    public static LedgerEntry Trade(long i, string symbol, double cost, double proceeds, int day = 0)
        => new LedgerEntry { Index = i, Kind = LedgerKinds.Trade, Source = symbol, Stake = cost, Payout = proceeds, Day = day };

    public static LedgerEntry Summary(long i, string source, string detail, int count, int wins, double stake, double payout)
        => new LedgerEntry { Index = i, Kind = LedgerKinds.Summary, Source = source, Detail = detail, Count = count, Wins = wins, Stake = stake, Payout = payout };

    /// <summary>A manager decision's one-off money (CloudLedger.RecordManager): situation, choice, what its gambles staked, the money its effects moved.</summary>
    public static LedgerEntry Manager(long i, string situation, string choice, double change, double before, int day, double staked = 0)
        => new LedgerEntry { Index = i, Kind = LedgerKinds.Manager, Source = situation, Detail = choice, Stake = staked, Change = change, ValueBefore = before, Good = change > 0, Day = day };

    /// <summary>A manager gamble as ManagerEffects.Gamble reports it (source "manager", detail "situation/choice").</summary>
    public static LedgerEntry ManagerGamble(long i, string detail, double stake, double payout, double before, int day)
        => Gamble(i, "manager", detail, stake, payout, before, day);

    /// <summary>The entry after <paramref name="change"/> (for one-off tweaks of a helper-made entry).</summary>
    public static LedgerEntry With(this LedgerEntry entry, Action<LedgerEntry> change)
    {
        change(entry);
        return entry;
    }

    public static bool HasFlag(SaveMeta meta, string reason) => meta?.Flags != null && meta.Flags.Any(f => f.Reason == reason);

    public static string Why(SubmitDecision d) => d.Code + ": " + d.Message + " | " + string.Join(" | ", d.Findings);
}

/// <summary>Cloud Save in memory, with write locks like the real one (a lock must match; null writes unconditionally).</summary>
public class MemoryStore : ISaveStore
{
    public string MetaJson;
    public int MetaVersion;
    public bool HasData;
    public string Data;
    public int DataVersion;
    public CityTotals City = new() { Known = true };
    public List<SaveCityTransfer> Transfers = new();
    /// <summary>The city accounts' applied op ids (CityAccount ops).</summary>
    public List<string> Ops = new();
    /// <summary>Whose accounts these are (SaveBuilder's pending transfers name "player-1").</summary>
    public string PlayerId = "player-1";
    public bool FailCity;
    /// <summary>Runs right before a write is checked (a concurrent writer).</summary>
    public Action BeforeWrite;
    public int Writes;

    public SaveMeta Meta => MetaJson == null ? null : JsonConvert.DeserializeObject<SaveMeta>(MetaJson);

    public void Put(SaveMeta meta, string data = null)
    {
        MetaJson = meta == null ? null : JsonConvert.SerializeObject(meta);
        MetaVersion++;
        if (data != null)
        {
            Data = data;
            HasData = true;
            DataVersion++;
        }
    }

    /// <summary>Like UgsSaveStore: a save_meta that exists but isn't a readable meta is flagged, not taken as none.</summary>
    public Task<StoredSave> LoadSave()
    {
        SaveMeta meta = null;
        bool unreadable = false;
        if (MetaJson != null)
        {
            try { meta = JsonConvert.DeserializeObject<SaveMeta>(MetaJson); }
            catch (JsonException) { meta = null; }
            unreadable = meta == null || SaveRules.IsUnreadableMeta(meta);
        }
        return Task.FromResult(new StoredSave
        {
            Meta = meta,
            MetaUnreadable = unreadable,
            MetaLock = MetaJson == null ? null : "m" + MetaVersion,
            HasData = HasData,
            DataJson = HasData ? Data : null,
            DataLock = HasData ? "d" + DataVersion : null
        });
    }

    public Task<StoredCity> LoadCity()
    {
        if (FailCity) throw new IOException("Cloud Save is down");
        return Task.FromResult(new StoredCity
        {
            Totals = new CityTotals { Deposited = City.Deposited, Withdrawn = City.Withdrawn, Known = true },
            Transfers = Transfers.ToList(),
            Ops = Ops.ToList(),
            PlayerId = PlayerId
        });
    }

    public Task WriteSaveAndMeta(string storedSave, string dataLock, SaveMeta meta, string metaLock)
    {
        BeforeWrite?.Invoke();
        Check(metaLock, MetaJson != null, "m" + MetaVersion);
        Check(dataLock, HasData, "d" + DataVersion);
        Data = storedSave;
        HasData = true;
        DataVersion++;
        MetaJson = JsonConvert.SerializeObject(meta);
        MetaVersion++;
        Writes++;
        return Task.CompletedTask;
    }

    public Task WriteMeta(SaveMeta meta, string metaLock)
    {
        BeforeWrite?.Invoke();
        Check(metaLock, MetaJson != null, "m" + MetaVersion);
        MetaJson = JsonConvert.SerializeObject(meta);
        MetaVersion++;
        Writes++;
        return Task.CompletedTask;
    }

    public Task DeleteData(string dataLock)
    {
        if (!HasData) return Task.CompletedTask;
        Check(dataLock, true, "d" + DataVersion);
        HasData = false;
        Data = null;
        DataVersion++;
        return Task.CompletedTask;
    }

    static void Check(string given, bool exists, string current)
    {
        if (given == null) return;
        if (!exists || given != current) throw new SaveConflictException();
    }
}
