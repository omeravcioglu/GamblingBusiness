using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>
/// The online cities' economy numbers and formulas (shared by the game and the server: change them in one place).
/// The same rules run in all six cities; each city keeps its own money. Money is whole dollars (long). Times are unix seconds.
/// </summary>
public static class CityRules
{
    // ---- Access ------------------------------------------------------------------------------------------------
    /// <summary>Company value (single-player) that opens the online cities for the first time.</summary>
    public const long UnlockCompanyValue = 1_000_000_000L;

    // ---- Bank (every city has its own bank account) ------------------------------------------------------------
    /// <summary>Most money that can be wired from the company into one city in any 24 hours (keeps save-file cheaters in check).</summary>
    public const long DepositCapPerDay = 1_000_000_000L;
    public const long MinTransfer = 1_000_000L;
    /// <summary>Balances never go above this (keeps every amount exact in JSON).</summary>
    public const long MaxBalance = 1_000_000_000_000_000L;

    // ---- Rent --------------------------------------------------------------------------------------------------
    /// <summary>Rent per day as a share of the plot's base price.</summary>
    public const double RentPerDayRate = 0.05;
    /// <summary>Extra rent on every plot of a block you own completely.</summary>
    public const double BlockBonus = 0.5;
    /// <summary>Uncollected rent stops growing after this long.</summary>
    public const int RentMaxHours = 48;

    // ---- Plots -------------------------------------------------------------------------------------------------
    public const int MaxPlotsPerPlayer = 60;
    /// <summary>City tax taken from what a plot's owner is paid by a challenger.</summary>
    public const double MarketTax = 0.05;

    // ---- Challenges: the only way to get a plot somebody owns -----------------------------------------------------
    /// <summary>A challenger stakes what the owner paid x this, then plays a hand of blackjack for the plot.</summary>
    public const double ChallengeRate = 1.5;
    /// <summary>New owners can't be challenged for this long.</summary>
    public const int ShieldHours = 24;
    /// <summary>An unfinished hand stands on its own after this long.</summary>
    public const int GameMinutes = 10;
    /// <summary>After this many pushes in a row the house wins.</summary>
    public const int MaxPushes = 10;

    public const string GamePlaying = "playing";
    public const string GameWon = "won";         // won, the plot is being handed over
    public const string GameLost = "lost";       // lost, the owner is being paid
    public const string GameDone = "done";       // won and the plot changed hands
    public const string GameSettled = "settled"; // lost and the owner was paid
    public const string GameVoid = "void";       // the plot changed hands meanwhile: the stake came back

    // ---- Player drawings -----------------------------------------------------------------------------------------
    /// <summary>A drawing reported by this many different players is taken down everywhere.</summary>
    public const int ReportsToHide = 15;
    /// <summary>Company name and logo can change at most this often (every change repaints all the company's plots).</summary>
    public const int ProfileCooldownSeconds = 30;

    // ---- Rival companies each city starts with -------------------------------------------------------------------
    public const string NpcPrefix = "npc_";

    // ---- Bookkeeping -------------------------------------------------------------------------------------------
    public const int InboxSize = 40;
    public const int AccountOpsSize = 300;
    public const int DistrictOpsSize = 250;
    public const int HoldRecoverySeconds = 90;

    public static bool IsNpc(string ownerId) => ownerId != null && ownerId.StartsWith(NpcPrefix, StringComparison.Ordinal);

    // ------------------------------------------------------------------------------------------------------------
    // Formulas
    // ------------------------------------------------------------------------------------------------------------

    /// <summary>What a plot is worth: what its owner paid (never below the base price).</summary>
    public static long PlotValue(CityPlotDef def, CityPlot plot)
    {
        long paid = plot != null ? plot.PricePaid : 0L;
        return Math.Max(def.BasePrice, paid);
    }

    /// <summary>What a challenger stakes for an owned plot: 1.5x what its owner paid.</summary>
    public static long ChallengeStake(CityPlotDef def, CityPlot plot) => RoundNice(PlotValue(def, plot) * ChallengeRate);

    /// <summary>What the owner is paid out of a challenger's stake (win or lose): the stake minus the city tax.</summary>
    public static long OwnerGets(long stake) => stake - Round(stake * MarketTax);

    public static long RentPerDay(CityPlotDef def, bool wholeBlock)
    {
        double rate = RentPerDayRate * (wholeBlock ? 1.0 + BlockBonus : 1.0);
        return Round(def.BasePrice * rate);
    }

    /// <summary>Rent one plot has earned since the owner last collected (capped at RentMaxHours).</summary>
    public static long RentAccrued(long perDay, long acquiredAt, long collectedAt, long now)
    {
        long from = Math.Max(Math.Max(acquiredAt, collectedAt), now - RentMaxHours * 3600L);
        if (now <= from) return 0L;
        return (long)Math.Floor(perDay * ((now - from) / 86400.0));
    }

    /// <summary>
    /// Rent a player can collect now and earns per day in one city, from its current map. Used by the server when
    /// collecting and by the game to show the number (same code, same result).
    /// </summary>
    public static void RentSummary(CityMap map, IDictionary<string, CityDistrict> districts, string playerId, long collectedAt, long now,
        out long ready, out long perDay, out int plots, out long plotsValue)
    {
        ready = 0; perDay = 0; plots = 0; plotsValue = 0;
        if (map == null || districts == null || string.IsNullOrEmpty(playerId)) return;

        var owned = new List<KeyValuePair<CityPlotDef, CityPlot>>();
        var ownedPerBlock = new Dictionary<string, int>();
        foreach (var district in districts.Values)
        {
            if (district?.Plots == null) continue;
            foreach (var pair in district.Plots)
            {
                if (pair.Value == null || pair.Value.OwnerId != playerId) continue;
                if (!map.PlotsById.TryGetValue(pair.Key, out var def)) continue;
                owned.Add(new KeyValuePair<CityPlotDef, CityPlot>(def, pair.Value));
                ownedPerBlock.TryGetValue(def.BlockId, out int count);
                ownedPerBlock[def.BlockId] = count + 1;
            }
        }

        foreach (var pair in owned)
        {
            var def = pair.Key;
            bool wholeBlock = ownedPerBlock[def.BlockId] >= map.BlocksById[def.BlockId].Plots.Count;
            long day = RentPerDay(def, wholeBlock);
            perDay += day;
            ready += RentAccrued(day, pair.Value.AcquiredAt, collectedAt, now);
            plots++;
            plotsValue += PlotValue(def, pair.Value);
        }
    }

    /// <summary>How many plots of a block the player owns.</summary>
    public static int OwnedInBlock(IDictionary<string, CityDistrict> districts, CityBlockDef block, string playerId)
    {
        if (districts == null || block == null || string.IsNullOrEmpty(playerId)) return 0;
        if (!districts.TryGetValue(block.DistrictId, out var district) || district?.Plots == null) return 0;
        int count = 0;
        foreach (var def in block.Plots)
        {
            if (district.Plots.TryGetValue(def.Id, out var plot) && plot != null && plot.OwnerId == playerId)
                count++;
        }
        return count;
    }

    public static long Round(double value)
    {
        if (double.IsNaN(value) || value <= 0) return 0L;
        if (value >= MaxBalance) return MaxBalance;
        return (long)Math.Round(value, MidpointRounding.AwayFromZero);
    }

    /// <summary>Round to a price that reads well: $5M steps above $100M, $1M steps below.</summary>
    public static long RoundNice(double value)
    {
        long step = value >= 100_000_000 ? 5_000_000L : 1_000_000L;
        long rounded = (long)Math.Round(value / step, MidpointRounding.AwayFromZero) * step;
        return Math.Max(step, Math.Min(MaxBalance, rounded));
    }

    public static long ClampBalance(long value) => Math.Max(0L, Math.Min(MaxBalance, value));

    // ------------------------------------------------------------------------------------------------------------
    // Money text
    // ------------------------------------------------------------------------------------------------------------

    /// <summary>Short money text: $750K, $250M, $1.5B, $2.25T.</summary>
    public static string Money(long amount)
    {
        string sign = amount < 0 ? "-" : "";
        double v = Math.Abs((double)amount);
        if (v >= 1e12) return sign + "$" + Short(v / 1e12) + "T";
        if (v >= 1e9) return sign + "$" + Short(v / 1e9) + "B";
        if (v >= 1e6) return sign + "$" + Short(v / 1e6) + "M";
        if (v >= 1e3) return sign + "$" + Short(v / 1e3) + "K";
        return sign + "$" + v.ToString("0", CultureInfo.InvariantCulture);
    }

    /// <summary>Full money text: $1,250,000,000.</summary>
    public static string MoneyFull(long amount)
    {
        return (amount < 0 ? "-$" : "$") + Math.Abs(amount).ToString("N0", CultureInfo.InvariantCulture);
    }

    static string Short(double value)
    {
        if (value >= 100) return value.ToString("0", CultureInfo.InvariantCulture);
        if (value >= 10) return TrimZeros(value.ToString("0.#", CultureInfo.InvariantCulture));
        return TrimZeros(value.ToString("0.##", CultureInfo.InvariantCulture));
    }

    static string TrimZeros(string s) => s.Contains(".") ? s.TrimEnd('0').TrimEnd('.') : s;

    /// <summary>Read "250M", "1.5b", "$2,000,000", "750k" (true when it's a positive amount).</summary>
    public static bool TryParseMoney(string text, out long amount)
    {
        amount = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        string s = text.Trim().Replace("$", "").Replace(",", "").Replace("_", "").Replace(" ", "").ToLowerInvariant();
        double factor = 1;
        if (s.EndsWith("t")) { factor = 1e12; s = s.Substring(0, s.Length - 1); }
        else if (s.EndsWith("b")) { factor = 1e9; s = s.Substring(0, s.Length - 1); }
        else if (s.EndsWith("m")) { factor = 1e6; s = s.Substring(0, s.Length - 1); }
        else if (s.EndsWith("k")) { factor = 1e3; s = s.Substring(0, s.Length - 1); }
        if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)) return false;
        value *= factor;
        if (double.IsNaN(value) || value <= 0 || value > MaxBalance) return false;
        amount = (long)Math.Round(value);
        return amount > 0;
    }

    public static string Duration(long seconds)
    {
        if (seconds <= 0) return "now";
        if (seconds < 90) return seconds + "s";
        long minutes = (seconds + 59) / 60;
        if (minutes < 90) return minutes + " min";
        long hours = (seconds + 1799) / 3600;
        if (hours < 48) return hours + " h";
        return ((hours + 12) / 24) + " days";
    }

    // ------------------------------------------------------------------------------------------------------------
    // Ids and names
    // ------------------------------------------------------------------------------------------------------------

    /// <summary>Operation ids come from the client (they make retries safe): 8-40 letters, digits, '-' or '_'.</summary>
    public static bool IsValidOpId(string opId)
    {
        if (string.IsNullOrEmpty(opId) || opId.Length < 8 || opId.Length > 40) return false;
        foreach (char c in opId)
        {
            bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_';
            if (!ok) return false;
        }
        return true;
    }

    public const int NameMinLength = 2;
    public const int NameMaxLength = 24;
    const string NameExtraChars = " &.,'-!+";

    /// <summary>Tidy a company name for the City, or return null with the reason it can't be used.</summary>
    public static string CleanCompanyName(string raw, out string error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "Give your company a name.";
            return null;
        }

        var sb = new StringBuilder();
        bool lastSpace = false;
        int letters = 0;
        foreach (char c in raw.Trim())
        {
            if (char.IsWhiteSpace(c))
            {
                if (!lastSpace) sb.Append(' ');
                lastSpace = true;
                continue;
            }
            lastSpace = false;
            if (char.IsLetterOrDigit(c) || NameExtraChars.IndexOf(c) >= 0)
            {
                sb.Append(c);
                if (char.IsLetter(c)) letters++;
            }
            else
            {
                error = $"'{c}' can't be used in a company name.";
                return null;
            }
        }

        string name = sb.ToString().Trim();
        if (name.Length < NameMinLength || letters < 2)
        {
            error = "The name needs at least 2 letters.";
            return null;
        }
        if (name.Length > NameMaxLength)
        {
            error = $"Keep the name to {NameMaxLength} characters.";
            return null;
        }
        if (ContainsBlockedWord(name))
        {
            error = "That name isn't allowed in the City. Pick another one.";
            return null;
        }
        return name;
    }

    // Kept short on purpose: blocks the obvious slurs and swear words (English and Turkish), including l33t spellings.
    // Words that hide inside ordinary names are left out (rape: grape, nazi: nazik, wank: swank, amk: Amkor).
    static readonly string[] BlockedWords =
    {
        "fuck", "shit", "cunt", "bitch", "nigger", "nigga", "faggot", "whore", "slut", "hitler",
        "porn", "penis", "vagina", "asshole", "bastard", "retard", "kike", "chink", "twat", "pussy", "dildo",
        "jizz", "cocksuck", "blowjob", "handjob",
        "aminakoy", "amcik", "orospu", "sikis", "sikik", "siktir", "sikerim", "yarrak",
        "gavat", "kahpe", "pezevenk", "ibne", "yavsak", "serefsiz", "kaltak", "pust", "dalyarak", "gotveren",
    };

    static HashSet<string> normalizedBlocked;

    public static bool ContainsBlockedWord(string text)
    {
        if (normalizedBlocked == null)
        {
            var set = new HashSet<string>();
            foreach (var w in BlockedWords) set.Add(Normalize(w));
            normalizedBlocked = set;
        }
        string n = Normalize(text);
        foreach (var w in normalizedBlocked)
        {
            if (w.Length > 0 && n.Contains(w)) return true;
        }
        return false;
    }

    /// <summary>Lower-case letters only: Turkish letters folded, l33t digits read as letters, repeats collapsed.</summary>
    static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        char last = '\0';
        foreach (char raw in text)
        {
            char c;
            switch (raw)
            {
                case 'İ': case 'I': case 'ı': case 'i': case '1': case '!': case '|': c = 'i'; break;
                case 'Ş': case 'ş': case '$': case '5': c = 's'; break;
                case 'Ğ': case 'ğ': c = 'g'; break;
                case 'Ç': case 'ç': c = 'c'; break;
                case 'Ö': case 'ö': case '0': c = 'o'; break;
                case 'Ü': case 'ü': c = 'u'; break;
                case '3': c = 'e'; break;
                case '4': case '@': c = 'a'; break;
                case '7': c = 't'; break;
                default: c = char.ToLowerInvariant(raw); break;
            }
            if (c < 'a' || c > 'z') continue;
            if (c == last) continue;
            sb.Append(c);
            last = c;
        }
        return sb.ToString();
    }
}

/// <summary>Small deterministic random numbers (the same sequence in the game and on the server).</summary>
public class CityRandom
{
    ulong state;

    public CityRandom(ulong seed) { state = seed; }
    public CityRandom(string seed) : this(CityHash.Fnv64(seed)) { }

    public ulong NextULong()
    {
        // SplitMix64
        state += 0x9E3779B97F4A7C15UL;
        ulong z = state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>0 <= result < 1</summary>
    public double NextDouble() => (NextULong() >> 11) * (1.0 / 9007199254740992.0);

    /// <summary>min <= result < max</summary>
    public int Range(int min, int max) => max <= min ? min : min + (int)(NextULong() % (ulong)(max - min));
}

public static class CityHash
{
    /// <summary>FNV-1a over the UTF-16 code units (string.GetHashCode differs between runtimes and runs).</summary>
    public static ulong Fnv64(string text)
    {
        ulong hash = 14695981039346656037UL;
        if (text == null) return hash;
        foreach (char c in text)
        {
            hash ^= c;
            hash *= 1099511628211UL;
        }
        return hash;
    }

    public static string Short(string text, int length = 8)
    {
        string hex = Fnv64(text).ToString("x16");
        return hex.Substring(0, Math.Min(length, hex.Length));
    }
}
