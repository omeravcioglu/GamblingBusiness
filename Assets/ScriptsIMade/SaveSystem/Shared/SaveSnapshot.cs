using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>One employee as SaveRules needs them (EmployeeSaveData fields, SaveSystem/SaveData.cs:78-94).</summary>
public class SaveEmployeeInfo
{
    /// <summary>Stable employee id (null for saves from before ids).</summary>
    public string Id;
    public int IQ;
    public double GPA;
    public int Experience;
    public int Age;
    public bool PhD;
    public bool Criminal;
}

/// <summary>A revenue share the manager's state lists (ManagerShare, Manager/ManagerState.cs:130-139).</summary>
public class SaveManagerShare
{
    public string Id;
    public int StartDay;
    /// <summary>Its last day (it ends the day after, ManagerEngine.ExpireContracts :203-210).</summary>
    public int UntilDay;

    public bool IsInvestor => Id == SaveCalibration.InvestorShareId;
}

/// <summary>An Online City transfer the save still waits on (CityOnline.PendingTransfer).</summary>
public class SavePendingTransfer
{
    public string OpId;
    public bool Deposit;
    public double Amount;
    /// <summary>The city bank it goes to or comes from.</summary>
    public string City;
    /// <summary>The account that started it (null for transfers from before accounts were recorded: the current one's).</summary>
    public string Player;
}

/// <summary>
/// What SaveRules reads from a save's JSON text (SaveData written by Unity's JsonUtility, SaveSystem/SaveData.cs): the
/// value and day it claims, the staff, and the few ISaveParticipant extras that bound income (fired count, HQ campus,
/// run peak, loans) plus the unlocked skills. Parsing never throws: a broken save comes back with Ok = false and Error set.
/// It streams (no JSON tree in memory) under a token budget, and rejects shapes JsonUtility never writes, so a tiny
/// packed save can't unpack into millions of tokens that cost the module seconds and hundreds of MB.
/// </summary>
public class SaveSnapshot
{
    public bool Ok;
    /// <summary>Why the save can't be used (null when Ok).</summary>
    public string Error;
    /// <summary>Size of the JSON text in UTF-8 bytes.</summary>
    public int Utf8Bytes;

    /// <summary>companyValue (SaveData.cs:21).</summary>
    public double CompanyValue;
    /// <summary>currentDay (SaveData.cs:22).</summary>
    public int CurrentDay;

    public List<SaveEmployeeInfo> Employees = new List<SaveEmployeeInfo>();
    /// <summary>People fired this run (extra "staff", Staff/StaffLedger.cs:122-128); 0 without it.</summary>
    public int Fired;
    /// <summary>
    /// The run's peak value as the game counted it, every frame (extra "runStats", RunEnd/RunStats.cs:25-39, :107-110,
    /// :177-183); 0 without it.
    /// </summary>
    public double RunPeak;
    /// <summary>Bankruptcy restarts this run (extra "runStats", "restarts", when the game counts them); 0 without it.</summary>
    public int Restarts;
    /// <summary>The save counts restarts (runStats has "restarts"): a coin-flip restart then always shows in it.</summary>
    public bool HasRestarts;
    /// <summary>Bank loans taken this run (extra "loan", RunEnd/BankLoan.cs:44-51); 0 without it.</summary>
    public int LoansTaken;
    /// <summary>What the company owes the bank, and has paid back this run (extra "loan").</summary>
    public double LoanDebt, LoanRepaid;
    /// <summary>
    /// Board meetings held this run (extra "board", meetingsHeld: counted in the same step that pays the hit bonus,
    /// Board/BoardMeetings.cs:530-537, :589); -1 without it.
    /// </summary>
    public int MeetingsHeld = -1;
    /// <summary>
    /// What the shares the company holds cost: sum of ownedShares x averageBuyPrice (extra "stocks",
    /// StockMarketManager.cs:319-357; a buy takes the money from company value, StockMarketManager.cs:242-243). -1 without it.
    /// </summary>
    public double HoldingsCost = -1;
    /// <summary>
    /// Online City transfers still waiting for the city's answer (extra "onlineCity", pending: a deposit left company value
    /// before the call, a withdrawal arrives after the answer; OnlineCity/Client/CityOnline.cs:456-471, :481-541).
    /// </summary>
    public List<SavePendingTransfer> PendingTransfers = new List<SavePendingTransfer>();
    /// <summary>
    /// The most the HQ campus can earn a day (extra "building", BuildMode/TowerSave.cs: format 3, one tower, and format 4, the
    /// campus, whose rooms stay one flat list; see ReadRooms).
    /// </summary>
    public double TowerIncomeMax;
    /// <summary>The most the office items can earn a day (officeItems, SaveData.cs:34).</summary>
    public double OfficeIncomeMax;
    /// <summary>Unlocked skill ids (unlockedSkillIds, SaveData.cs:31); null when the save doesn't list them.</summary>
    public HashSet<string> UnlockedSkills;
    /// <summary>The save has the manager's state (extra "manager", Manager/ManagerState.cs ManagerSaveState).</summary>
    public bool HasManagerState;
    /// <summary>Revenue shares running when the save was written (ManagerSaveState.shares; an investor's offer starts one).</summary>
    public List<SaveManagerShare> ManagerShares = new List<SaveManagerShare>();

    /// <summary>Largest employee list read (thousands fit well within SaveCloud.MaxSaveBytes).</summary>
    public const int MaxEmployees = 50000;
    /// <summary>
    /// JSON tokens a save may have, extras included. An employee is ~51 tokens in JsonUtility's output, so the largest honest
    /// save (8 MB, ~10,000 employees) has ~0.5 million; an 8 MB list of zeros would have 4 million.
    /// </summary>
    public const long MaxTokens = 1000000;

    /// <summary>A save as stored (save_data): packed text (SavePacking) or plain JSON from before packing. Never throws.</summary>
    public static SaveSnapshot ParseStored(string stored)
    {
        string json;
        try
        {
            json = SavePacking.Unpack(stored);
        }
        catch (Exception e)
        {
            return new SaveSnapshot().Fail("The stored save can't be unpacked (" + e.GetType().Name + ").");
        }
        return Parse(json);
    }

    public static SaveSnapshot Parse(string json)
    {
        return Parse(json, MaxTokens);
    }

    public static SaveSnapshot Parse(string json, long maxTokens)
    {
        var snapshot = new SaveSnapshot();
        if (string.IsNullOrEmpty(json)) return snapshot.Fail("The save is empty.");

        snapshot.Utf8Bytes = Encoding.UTF8.GetByteCount(json);
        if (snapshot.Utf8Bytes > SaveCloud.MaxSaveBytes)
            return snapshot.Fail("The save is " + snapshot.Utf8Bytes + " bytes (the limit is " + SaveCloud.MaxSaveBytes + ").");

        var budget = new TokenBudget(maxTokens);
        bool hasValue = false, hasDay = false;
        JArrayOfOffice office = null;
        try
        {
            var r = new SaveTokenReader(json, budget);
            if (!r.Read() || r.Token != JsonToken.StartObject) return snapshot.Fail("The save isn't a JSON object.");
            bool closed = false;
            while (r.Read())
            {
                if (r.Token == JsonToken.EndObject)
                {
                    closed = true;
                    break;
                }
                if (r.Token != JsonToken.PropertyName) return snapshot.Fail("The save's JSON is malformed.");
                string name = (string)r.Value;
                if (!r.Read()) break;
                switch (name)
                {
                    case "companyValue":
                        hasValue = r.TryNumber(out snapshot.CompanyValue);
                        if (!hasValue) r.Skip();
                        break;
                    case "currentDay":
                        hasDay = r.TryNumber(out double day) && day == Math.Floor(day) && day >= int.MinValue && day <= int.MaxValue;
                        if (hasDay) snapshot.CurrentDay = (int)day;
                        else r.Skip();
                        break;
                    case "employees":
                        ReadEmployees(r, snapshot);
                        break;
                    case "unlockedSkillIds":
                        snapshot.UnlockedSkills = ReadStrings(r, "unlockedSkillIds");
                        break;
                    case "officeItems":
                        office = ReadOfficeItems(r);
                        break;
                    case "extras":
                        ReadExtras(r, snapshot, budget);
                        break;
                    default:
                        r.Skip();
                        break;
                }
            }
            if (!closed) return snapshot.Fail("The save's JSON ends too early.");
            if (r.Read()) return snapshot.Fail("The save has more after its JSON object.");
        }
        catch (SaveShapeException e)
        {
            return snapshot.Fail(e.Message);
        }
        catch (JsonException)
        {
            return snapshot.Fail("The save isn't valid JSON.");
        }

        if (!hasValue) return snapshot.Fail("The save has no companyValue.");
        if (double.IsNaN(snapshot.CompanyValue) || double.IsInfinity(snapshot.CompanyValue)) return snapshot.Fail("The save's companyValue isn't a number.");
        if (!hasDay) return snapshot.Fail("The save has no whole currentDay.");

        snapshot.OfficeIncomeMax = office == null || office.Count == 0
            ? SaveCalibration.OfficeIncomeFallback
            : Math.Max(office.Income, SaveCalibration.OfficeIncomeFallback);
        snapshot.Ok = true;
        return snapshot;
    }

    SaveSnapshot Fail(string error)
    {
        Ok = false;
        Error = error;
        return this;
    }

    // ---- The save's lists --------------------------------------------------------------------------------------

    static void ReadEmployees(SaveTokenReader r, SaveSnapshot snapshot)
    {
        if (r.Token == JsonToken.Null) return;
        if (r.Token != JsonToken.StartArray) throw new SaveShapeException("The save's employees aren't a list.");
        while (r.Read())
        {
            if (r.Token == JsonToken.EndArray) return;
            if (r.Token != JsonToken.StartObject) throw new SaveShapeException("The save's employees aren't all objects.");
            if (snapshot.Employees.Count >= MaxEmployees) throw new SaveShapeException("The save lists more than " + MaxEmployees + " employees.");
            var e = new SaveEmployeeInfo();
            ReadObject(r, (name, reader) =>
            {
                switch (name)
                {
                    case "id": e.Id = reader.Text(); break;
                    case "IQ": e.IQ = (int)Math.Round(SaveCalibration.Clamp(reader.Number(0.0), -1e6, 1e6)); break;
                    case "GPA": e.GPA = reader.Number(0.0); break;
                    case "experienceYears": e.Experience = (int)Math.Round(SaveCalibration.Clamp(reader.Number(0.0), -1e6, 1e6)); break;
                    case "age": e.Age = (int)Math.Round(SaveCalibration.Clamp(reader.Number(0.0), -1e6, 1e6)); break;
                    case "degree": e.PhD = reader.Text() == "PhD"; break;
                    case "criminalRecord": e.Criminal = reader.Bool(); break;
                    default: reader.Skip(); break;
                }
                return true;
            });
            snapshot.Employees.Add(e);
        }
        throw new SaveShapeException("The save's JSON ends too early.");
    }

    static HashSet<string> ReadStrings(SaveTokenReader r, string what)
    {
        if (r.Token == JsonToken.Null) return null;
        if (r.Token != JsonToken.StartArray) throw new SaveShapeException("The save's " + what + " isn't a list.");
        var result = new HashSet<string>(StringComparer.Ordinal);
        while (r.Read())
        {
            if (r.Token == JsonToken.EndArray) return result;
            string text = r.Text();
            if (!string.IsNullOrEmpty(text)) result.Add(text);
        }
        throw new SaveShapeException("The save's JSON ends too early.");
    }

    /// <summary>Office items (name, level): the saved levels x the calibrated income per level.</summary>
    sealed class JArrayOfOffice
    {
        public int Count;
        public double Income;
    }

    static JArrayOfOffice ReadOfficeItems(SaveTokenReader r)
    {
        if (r.Token == JsonToken.Null) return null;
        if (r.Token != JsonToken.StartArray) throw new SaveShapeException("The save's officeItems isn't a list.");
        var office = new JArrayOfOffice();
        while (r.Read())
        {
            if (r.Token == JsonToken.EndArray) return office;
            if (r.Token != JsonToken.StartObject) throw new SaveShapeException("The save's officeItems aren't all objects.");
            double level = 0.0;
            ReadObject(r, (name, reader) =>
            {
                if (name == "currentLevel") level = reader.Number(0.0);
                else reader.Skip();
                return true;
            });
            if (++office.Count <= 100)
                office.Income += SaveCalibration.Clamp(level, 0.0, SaveCalibration.OfficeMaxLevel) * SaveCalibration.OfficeIncomePerLevel;
        }
        throw new SaveShapeException("The save's JSON ends too early.");
    }

    /// <summary>The ISaveParticipant extras: {key, json} objects; the json of the few the rules use is read in turn.</summary>
    static void ReadExtras(SaveTokenReader r, SaveSnapshot snapshot, TokenBudget budget)
    {
        if (r.Token == JsonToken.Null) return;
        if (r.Token != JsonToken.StartArray) throw new SaveShapeException("The save's extras aren't a list.");
        while (r.Read())
        {
            if (r.Token == JsonToken.EndArray) return;
            if (r.Token != JsonToken.StartObject) throw new SaveShapeException("The save's extras aren't all objects.");
            string key = null, extraJson = null;
            ReadObject(r, (name, reader) =>
            {
                if (name == "key") key = reader.Text();
                else if (name == "json") extraJson = reader.Text();
                else reader.Skip();
                return true;
            });
            if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(extraJson))
                ReadExtra(key, extraJson, snapshot, budget);
        }
        throw new SaveShapeException("The save's JSON ends too early.");
    }

    static void ReadExtra(string key, string json, SaveSnapshot s, TokenBudget budget)
    {
        switch (key)
        {
            case "staff":
                // "fired" comes before the per-employee records: stop there
                VisitExtra(json, budget, (name, r) =>
                {
                    if (name != "fired") { r.Skip(); return true; }
                    s.Fired = (int)SaveCalibration.Clamp(r.Number(0.0), 0.0, int.MaxValue);
                    return false;
                });
                break;
            case "runStats":
                VisitExtra(json, budget, (name, r) =>
                {
                    if (name == "peakValue") s.RunPeak = Math.Max(0.0, Finite(r.Number(0.0)));
                    else if (name == "restarts")
                    {
                        s.Restarts = (int)SaveCalibration.Clamp(r.Number(0.0), 0.0, int.MaxValue);
                        s.HasRestarts = true;
                    }
                    else r.Skip();
                    return true;
                });
                break;
            case "loan":
                VisitExtra(json, budget, (name, r) =>
                {
                    if (name == "loansTaken") s.LoansTaken = (int)SaveCalibration.Clamp(r.Number(0.0), 0.0, int.MaxValue);
                    else if (name == "debt") s.LoanDebt = Math.Max(0.0, Finite(r.Number(0.0)));
                    else if (name == "repaid") s.LoanRepaid = Math.Max(0.0, Finite(r.Number(0.0)));
                    else r.Skip();
                    return true;
                });
                break;
            case "board":
                VisitExtra(json, budget, (name, r) =>
                {
                    if (name != "meetingsHeld") { r.Skip(); return true; }
                    double held = r.Number(-1.0);
                    if (held >= 0 && held <= int.MaxValue) s.MeetingsHeld = (int)held;
                    return true;
                });
                break;
            case "building":
                VisitExtra(json, budget, (name, r) =>
                {
                    if (name == "rooms") s.TowerIncomeMax = ReadRooms(r);
                    else r.Skip();
                    return true;
                });
                break;
            case "stocks":
                s.HoldingsCost = 0.0;
                VisitExtra(json, budget, (name, r) =>
                {
                    if (name == "stocks") s.HoldingsCost = ReadHoldings(r);
                    else r.Skip();
                    return true;
                });
                break;
            case "onlineCity":
                VisitExtra(json, budget, (name, r) =>
                {
                    if (name == "pending") ReadPending(r, s.PendingTransfers);
                    else r.Skip();
                    return true;
                });
                break;
            case "manager":
                VisitExtra(json, budget, (name, r) =>
                {
                    s.HasManagerState = true;
                    if (name != "shares")
                    {
                        r.Skip();
                        return true;
                    }
                    ReadShares(r, s.ManagerShares);
                    return false;   // what follows (perks, a mood for every employee...) isn't needed
                });
                break;
        }
    }

    /// <summary>The manager's revenue shares (ManagerShare: id, pct, startDay, untilDay, paid); at most 100.</summary>
    static void ReadShares(SaveTokenReader r, List<SaveManagerShare> into)
    {
        if (r.Token != JsonToken.StartArray)
        {
            r.Skip();
            return;
        }
        while (r.Read() && r.Token != JsonToken.EndArray)
        {
            if (r.Token != JsonToken.StartObject)
            {
                r.Skip();
                continue;
            }
            var share = new SaveManagerShare();
            ReadObject(r, (name, reader) =>
            {
                if (name == "id") share.Id = reader.Text();
                else if (name == "startDay") share.StartDay = (int)SaveCalibration.Clamp(reader.Number(0.0), int.MinValue, int.MaxValue);
                else if (name == "untilDay") share.UntilDay = (int)SaveCalibration.Clamp(reader.Number(0.0), int.MinValue, int.MaxValue);
                else reader.Skip();
                return true;
            });
            if (into.Count < 100) into.Add(share);
        }
    }

    /// <summary>
    /// The campus's rooms through SaveCalibration.TowerRoomIncomeMax, each at its saved room level ("level", 1-based; format 3
    /// rooms have none: level 1); 0 without rooms. Format 4 keeps every building's rooms in this one flat list (its
    /// "buildings" and "connections" add no money and are skipped by the caller). A room type with a per-company limit
    /// (SaveCalibration.TowerRoomMaxPerCompany) earns its own income in at most that many rooms, the best ones; the others
    /// count their furniture alone. Only the first SaveCalibration.TowerMaxRooms rooms count.
    /// </summary>
    static double ReadRooms(SaveTokenReader r)
    {
        if (r.Token != JsonToken.StartArray)
        {
            r.Skip();
            return 0.0;
        }
        double total = 0.0;
        int count = 0;
        // Limited room types: what their best rooms earn beyond their furniture (a list of at most the limit's length)
        Dictionary<string, List<double>> best = null;
        while (r.Read() && r.Token != JsonToken.EndArray)
        {
            if (r.Token != JsonToken.StartObject)
            {
                r.Skip();
                continue;
            }
            string id = null;
            int roomLevel = 1;
            var levels = new List<int>();
            ReadObject(r, (name, reader) =>
            {
                if (name == "id") id = reader.Text();
                else if (name == "level") roomLevel = (int)SaveCalibration.Clamp(reader.Number(1.0), 1.0, 1000.0);
                else if (name == "levels" && reader.Token == JsonToken.StartArray)
                {
                    while (reader.Read() && reader.Token != JsonToken.EndArray)
                    {
                        double level = reader.Number(0.0);
                        if (levels.Count < SaveCalibration.TowerMaxSlotsPerRoom)
                            levels.Add((int)SaveCalibration.Clamp(level, 0, SaveCalibration.TowerMaxLevel));
                    }
                }
                else reader.Skip();
                return true;
            });
            if (++count > SaveCalibration.TowerMaxRooms) continue;

            double income = SaveCalibration.TowerRoomIncomeMax(id, levels, roomLevel);
            int limit = SaveCalibration.TowerRoomMaxPerCompany(id);
            if (limit <= 0)
            {
                total += income;
                continue;
            }
            double furniture = SaveCalibration.TowerFurnitureIncomeMax(levels);
            total += furniture;
            if (best == null) best = new Dictionary<string, List<double>>(StringComparer.Ordinal);
            if (!best.TryGetValue(id, out List<double> kept))
            {
                kept = new List<double>(limit);
                best[id] = kept;
            }
            double extra = income - furniture;
            if (kept.Count < limit)
            {
                kept.Add(extra);
                continue;
            }
            int lowest = 0;
            for (int i = 1; i < kept.Count; i++)
                if (kept[i] < kept[lowest]) lowest = i;
            if (extra > kept[lowest]) kept[lowest] = extra;
        }
        if (best != null)
        {
            foreach (List<double> kept in best.Values)
                foreach (double extra in kept) total += extra;
        }
        return total;
    }

    /// <summary>Sum of ownedShares x averageBuyPrice over the saved stocks (StockMarketSaveData).</summary>
    static double ReadHoldings(SaveTokenReader r)
    {
        if (r.Token != JsonToken.StartArray)
        {
            r.Skip();
            return 0.0;
        }
        double total = 0.0;
        while (r.Read() && r.Token != JsonToken.EndArray)
        {
            if (r.Token != JsonToken.StartObject)
            {
                r.Skip();
                continue;
            }
            double shares = 0.0, price = 0.0;
            ReadObject(r, (name, reader) =>
            {
                if (name == "ownedShares") shares = Finite(reader.Number(0.0));
                else if (name == "averageBuyPrice") price = Finite(reader.Number(0.0));
                else reader.Skip();
                return true;
            });
            if (shares > 0.0 && price > 0.0) total += shares * price;
        }
        return double.IsInfinity(total) ? double.MaxValue : total;
    }

    static void ReadPending(SaveTokenReader r, List<SavePendingTransfer> into)
    {
        if (r.Token != JsonToken.StartArray)
        {
            r.Skip();
            return;
        }
        while (r.Read() && r.Token != JsonToken.EndArray)
        {
            if (r.Token != JsonToken.StartObject)
            {
                r.Skip();
                continue;
            }
            string opId = null, kind = null, city = null, player = null;
            double amount = 0.0;
            ReadObject(r, (name, reader) =>
            {
                if (name == "opId") opId = reader.Text();
                else if (name == "kind") kind = reader.Text();
                else if (name == "amount") amount = Finite(reader.Number(0.0));
                else if (name == "city") city = reader.Text();
                else if (name == "player") player = reader.Text();
                else reader.Skip();
                return true;
            });
            if (!string.IsNullOrEmpty(opId) && amount > 0.0 && into.Count < 1000)
                into.Add(new SavePendingTransfer { OpId = opId, Deposit = kind == "deposit", Amount = amount, City = city, Player = string.IsNullOrEmpty(player) ? null : player });
        }
    }

    // ---- Streaming helpers ---------------------------------------------------------------------------------------

    /// <summary>
    /// Read the object the reader is on (at its StartObject) property by property; the visitor reads or skips each value
    /// and returns false to stop reading this object early (the rest is left unread).
    /// </summary>
    static bool ReadObject(SaveTokenReader r, Func<string, SaveTokenReader, bool> visit)
    {
        while (r.Read())
        {
            if (r.Token == JsonToken.EndObject) return true;
            if (r.Token != JsonToken.PropertyName) throw new SaveShapeException("The save's JSON is malformed.");
            string name = (string)r.Value;
            if (!r.Read()) break;
            if (!visit(name, r)) return false;
        }
        throw new SaveShapeException("The save's JSON ends too early.");
    }

    /// <summary>
    /// Visit the properties of an extra's JSON object. A broken extra counts as missing; only running out of the token budget
    /// fails the whole save.
    /// </summary>
    static void VisitExtra(string json, TokenBudget budget, Func<string, SaveTokenReader, bool> visit)
    {
        try
        {
            var r = new SaveTokenReader(json, budget);
            if (!r.Read() || r.Token != JsonToken.StartObject) return;
            ReadObject(r, visit);
        }
        catch (SaveBudgetException)
        {
            throw;
        }
        catch (SaveShapeException)
        {
            // A malformed extra: its fields keep their defaults
        }
        catch (JsonException)
        {
            // Not JSON: likewise
        }
    }

    static double Finite(double value)
    {
        return double.IsNaN(value) || double.IsInfinity(value) ? 0.0 : value;
    }

    // ---- JSON tree helpers (small documents only: the module's city accounts) ---------------------------------------

    /// <summary>A JSON object, or null when the text isn't one. Dates stay strings, numbers are doubles.</summary>
    public static JObject ParseObject(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            using (var reader = new JsonTextReader(new StringReader(json)))
            {
                reader.DateParseHandling = DateParseHandling.None;
                reader.FloatParseHandling = FloatParseHandling.Double;
                reader.MaxDepth = 64;
                JToken token = JToken.ReadFrom(reader);
                while (reader.Read())
                {
                    if (reader.TokenType != JsonToken.Comment) return null;
                }
                return token as JObject;
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>A JSON number (integer or float, any size) as a double.</summary>
    public static bool TryNumber(JToken token, out double value)
    {
        value = 0.0;
        if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)) return false;
        return double.TryParse(token.ToString(Formatting.None), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}

/// <summary>Tokens left for one save, shared by the save and its extras.</summary>
sealed class TokenBudget
{
    public long Left;
    public readonly long Max;

    public TokenBudget(long max)
    {
        Max = max;
        Left = max;
    }
}

/// <summary>The save has a shape JsonUtility never writes (or too many tokens): it can't be used.</summary>
class SaveShapeException : Exception
{
    public SaveShapeException(string message) : base(message) { }
}

/// <summary>The save has more JSON tokens than the budget allows.</summary>
sealed class SaveBudgetException : SaveShapeException
{
    public SaveBudgetException(long max) : base("The save has more than " + max + " JSON tokens.") { }
}

/// <summary>A streaming JSON reader that counts every token against a TokenBudget and can skip whole values.</summary>
sealed class SaveTokenReader
{
    readonly JsonTextReader reader;
    readonly TokenBudget budget;

    public SaveTokenReader(string json, TokenBudget budget)
    {
        this.budget = budget;
        reader = new JsonTextReader(new StringReader(json))
        {
            DateParseHandling = DateParseHandling.None,
            FloatParseHandling = FloatParseHandling.Double,
            MaxDepth = 64
        };
    }

    public JsonToken Token => reader.TokenType;
    public object Value => reader.Value;

    /// <summary>The next token that isn't a comment (false at the end of the text).</summary>
    public bool Read()
    {
        while (reader.Read())
        {
            if (--budget.Left < 0) throw new SaveBudgetException(budget.Max);
            if (reader.TokenType != JsonToken.Comment) return true;
        }
        return false;
    }

    /// <summary>Skip the value the reader is on: all of an object or array (a plain value needs nothing).</summary>
    public void Skip()
    {
        JsonToken start = reader.TokenType;
        if (start != JsonToken.StartObject && start != JsonToken.StartArray && start != JsonToken.StartConstructor) return;
        int depth = reader.Depth;
        while (Read())
        {
            JsonToken t = reader.TokenType;
            if ((t == JsonToken.EndObject || t == JsonToken.EndArray || t == JsonToken.EndConstructor) && reader.Depth == depth) return;
        }
        throw new SaveShapeException("The save's JSON ends too early.");
    }

    /// <summary>The current value as a number (integer of any size, or float).</summary>
    public bool TryNumber(out double value)
    {
        value = 0.0;
        object raw = reader.Value;
        if (reader.TokenType == JsonToken.Integer)
        {
            if (raw is long l) { value = l; return true; }
            if (raw is int i) { value = i; return true; }
            if (raw is System.Numerics.BigInteger big) { value = (double)big; return true; }
        }
        else if (reader.TokenType != JsonToken.Float)
        {
            return false;
        }
        if (raw is double d) { value = d; return true; }
        return raw != null && double.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>The current value as a number, or <paramref name="fallback"/> (anything else is skipped).</summary>
    public double Number(double fallback)
    {
        if (TryNumber(out double value) && !double.IsNaN(value)) return value;
        Skip();
        return fallback;
    }

    /// <summary>The current value as a string, or null (anything else is skipped).</summary>
    public string Text()
    {
        if (reader.TokenType == JsonToken.String) return (string)reader.Value;
        Skip();
        return null;
    }

    public bool Bool()
    {
        if (reader.TokenType == JsonToken.Boolean) return (bool)reader.Value;
        Skip();
        return false;
    }
}
