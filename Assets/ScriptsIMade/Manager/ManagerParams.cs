using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>What param specs are worked out from (the running company in the game; fixed numbers in tests and the server spec).</summary>
public interface IManagerParamSource
{
    /// <summary>Company value ($).</summary>
    double Value { get; }
    /// <summary>max(average daily operating result over 7 days, 0.3% of value, $300).</summary>
    double ProfitBase { get; }
    /// <summary>The whole staff's salary cost per day ($).</summary>
    double Payroll { get; }
    /// <summary>Tier of the current value, 0..6 (min/max tables are indexed by it).</summary>
    int CashTier { get; }
    /// <summary>Yearly salary of a resolved target.</summary>
    bool TryGetTargetSalary(string target, out double yearly);
    /// <summary>A fact for "count" (employees, capacity, free...).</summary>
    bool TryGetFact(string fact, out double value);
    /// <summary>A random number in [0, 1).</summary>
    double Random01();
}

/// <summary>
/// Param specs (plain C#): the named numbers a situation works out when it is created, so its text and its effects use
/// the same amounts ("Pay {cost}" and money -cost). Money scales with the company:
/// <code>
/// sum = flat + value + value_of (an earlier param) + pct% x company value + profit_days x profit_base + payroll_days x daily payroll
///       + salary_of's salary (salary_pct % of the yearly salary, per year or day, or salary_days days of it)
///       + count_pct % x count (a fact) + random [min, max]
/// sum x= times (an earlier param), += plus (an earlier param); then min / max (a number or 7 numbers by cash tier); then round
/// </code>
/// Default rounding: money terms round to 2 significant figures ("money"), counts to whole numbers ("int"), plain values
/// stay as written. Text shows money as $1,234 ("money"), counts as 12 ("int"), values as written ("number").
/// </summary>
public static class ManagerParams
{
    public const string FormatMoney = "money";
    public const string FormatInt = "int";
    public const string FormatNumber = "number";

    static readonly string[] MoneyTerms = { "flat", "pct", "profit_days", "payroll_days", "salary_of" };

    /// <summary>True when the spec produces dollars (it uses a money term).</summary>
    public static bool IsMoney(ManagerJson.Node spec)
    {
        if (spec == null || !spec.IsObject) return false;
        foreach (string t in MoneyTerms) if (spec.Has(t)) return true;
        string format = spec.GetString("format");
        return format == FormatMoney;
    }

    /// <summary>How {name} shows in text.</summary>
    public static string FormatOf(ManagerJson.Node spec)
    {
        string format = spec != null ? spec.GetString("format") : null;
        if (!string.IsNullOrEmpty(format)) return format;
        if (IsMoney(spec)) return FormatMoney;
        if (spec != null && spec.Has("count")) return FormatInt;
        return FormatNumber;
    }

    /// <summary>Work out one spec. <paramref name="earlier"/> holds the params before it (for times / plus).</summary>
    public static double Evaluate(ManagerJson.Node spec, IManagerParamSource src, IDictionary<string, double> earlier)
    {
        if (spec == null) return 0;
        if (spec.IsNumber) return spec.Number;
        if (!spec.IsObject) return 0;

        double sum = 0;
        sum += spec.GetNumber("flat", 0);
        sum += spec.GetNumber("value", 0);
        if (spec.Has("pct")) sum += spec.GetNumber("pct", 0) / 100.0 * Math.Max(0, src.Value);
        if (spec.Has("profit_days")) sum += spec.GetNumber("profit_days", 0) * Math.Max(0, src.ProfitBase);
        if (spec.Has("payroll_days")) sum += spec.GetNumber("payroll_days", 0) * Math.Max(0, src.Payroll);

        string salaryOf = spec.GetString("salary_of");
        if (!string.IsNullOrEmpty(salaryOf) && src.TryGetTargetSalary(salaryOf, out double yearly))
        {
            if (spec.Has("salary_pct"))
            {
                double part = spec.GetNumber("salary_pct", 0) / 100.0 * yearly;
                sum += spec.GetString("per", "year") == "day" ? part / 365.0 : part;
            }
            if (spec.Has("salary_days")) sum += spec.GetNumber("salary_days", 0) * yearly / 365.0;
        }

        string count = spec.GetString("count");
        if (!string.IsNullOrEmpty(count) && src.TryGetFact(count, out double counted))
            sum += spec.GetNumber("count_pct", 100) / 100.0 * counted;

        ManagerJson.Node random = spec.Get("random");
        if (random != null && random.IsArray && random.Items.Count == 2 && random.Items[0].IsNumber && random.Items[1].IsNumber)
        {
            double a = random.Items[0].Number, b = random.Items[1].Number;
            sum += a + (b - a) * src.Random01();
        }

        string valueOf = spec.GetString("value_of");
        if (!string.IsNullOrEmpty(valueOf) && earlier != null && earlier.TryGetValue(valueOf, out double of)) sum += of;

        string times = spec.GetString("times");
        if (!string.IsNullOrEmpty(times) && earlier != null && earlier.TryGetValue(times, out double t)) sum *= t;
        string plus = spec.GetString("plus");
        if (!string.IsNullOrEmpty(plus) && earlier != null && earlier.TryGetValue(plus, out double p)) sum += p;

        if (spec.Has("min")) sum = Math.Max(sum, Bound(spec.Get("min"), src.CashTier, double.MinValue));
        if (spec.Has("max")) sum = Math.Min(sum, Bound(spec.Get("max"), src.CashTier, double.MaxValue));

        string round = spec.GetString("round");
        if (string.IsNullOrEmpty(round)) round = IsMoney(spec) ? FormatMoney : spec.Has("count") ? FormatInt : "none";
        switch (round)
        {
            case FormatMoney: sum = RoundMoney(sum); break;
            case FormatInt: sum = Math.Round(sum, MidpointRounding.AwayFromZero); break;
        }
        return double.IsNaN(sum) || double.IsInfinity(sum) ? 0 : sum;
    }

    /// <summary>A bound: a number, or 7 numbers by cash tier (the last one for higher tiers).</summary>
    public static double Bound(ManagerJson.Node node, int tier, double fallback)
    {
        if (node == null) return fallback;
        if (node.IsNumber) return node.Number;
        if (node.IsArray && node.Items.Count > 0)
        {
            int i = Math.Max(0, Math.Min(node.Items.Count - 1, tier));
            ManagerJson.Node item = node.Items[i];
            return item != null && item.IsNumber ? item.Number : fallback;
        }
        return fallback;
    }

    /// <summary>Round dollars to 2 significant figures (43,712 → 44,000; 1,234 → 1,200); small amounts to whole dollars.</summary>
    public static double RoundMoney(double amount)
    {
        double a = Math.Abs(amount);
        if (a < 100) return Math.Round(amount, MidpointRounding.AwayFromZero);
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(a)) - 1);
        return Math.Round(amount / magnitude, MidpointRounding.AwayFromZero) * magnitude;
    }

    /// <summary>
    /// A number field of an effect: a number, or a param name with an optional leading '-' ("-cost"). False when it names a
    /// param that isn't there.
    /// </summary>
    public static bool TryNumber(ManagerJson.Node field, IDictionary<string, double> prms, out double value)
    {
        value = 0;
        if (field == null) return false;
        if (field.IsNumber)
        {
            value = field.Number;
            return true;
        }
        if (field.IsString)
        {
            string name = field.Str.Trim();
            double sign = 1;
            if (name.StartsWith("-"))
            {
                sign = -1;
                name = name.Substring(1);
            }
            if (prms != null && prms.TryGetValue(name, out double v))
            {
                value = sign * v;
                return true;
            }
            if (double.TryParse(name, NumberStyles.Float, CultureInfo.InvariantCulture, out double literal))
            {
                value = sign * literal;
                return true;
            }
        }
        return false;
    }

    /// <summary>The param name a field refers to ("-cost" → "cost"), or null for numbers and inline specs.</summary>
    public static string ReferencedParam(ManagerJson.Node field)
    {
        if (field == null || !field.IsString) return null;
        string name = field.Str.Trim();
        if (name.StartsWith("-")) name = name.Substring(1);
        if (double.TryParse(name, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) return null;
        return name;
    }

    // ------------------------------------------------------------------
    // Checking (the validator)
    // ------------------------------------------------------------------

    /// <summary>Problems with one spec (as messages). <paramref name="earlier"/> = names of the params before it.</summary>
    public static List<string> Check(ManagerJson.Node spec, ICollection<string> earlier, ICollection<string> targetNames)
    {
        var problems = new List<string>();
        if (spec == null || !spec.IsObject)
        {
            problems.Add("must be an object");
            return problems;
        }
        bool hasTerm = false;
        foreach (KeyValuePair<string, ManagerJson.Node> f in spec.Fields)
        {
            if (f.Key.StartsWith("_")) continue;
            if (!ManagerSchema.ParamFields.ContainsKey(f.Key))
            {
                problems.Add("unknown field \"" + f.Key + "\"");
                continue;
            }
            ManagerJson.Node v = f.Value;
            switch (f.Key)
            {
                case "flat": case "value": case "pct": case "profit_days": case "payroll_days": case "salary_pct": case "salary_days": case "count_pct":
                    if (!v.IsNumber) problems.Add("\"" + f.Key + "\" must be a number");
                    if (f.Key != "salary_pct" && f.Key != "salary_days" && f.Key != "count_pct") hasTerm = true;
                    break;
                case "salary_of":
                    hasTerm = true;
                    if (!v.IsString || targetNames == null || !targetNames.Contains(v.Str)) problems.Add("\"salary_of\" must name a target of the situation");
                    if (!spec.Has("salary_pct") && !spec.Has("salary_days")) problems.Add("\"salary_of\" needs salary_pct or salary_days");
                    break;
                case "per":
                    if (!v.IsString || (v.Str != "day" && v.Str != "year")) problems.Add("\"per\" is \"day\" or \"year\"");
                    break;
                case "count":
                    hasTerm = true;
                    if (!v.IsString || !ManagerSchema.Facts.ContainsKey(v.Str)) problems.Add("\"count\" must be a fact name (employees, capacity, free...)");
                    break;
                case "random":
                    hasTerm = true;
                    if (!v.IsArray || v.Items.Count != 2 || !v.Items[0].IsNumber || !v.Items[1].IsNumber) problems.Add("\"random\" is [min, max]");
                    break;
                case "value_of":
                    hasTerm = true;
                    if (!v.IsString || earlier == null || !earlier.Contains(v.Str)) problems.Add("\"value_of\" must name a param written before this one");
                    break;
                case "times": case "plus":
                    if (!v.IsString || earlier == null || !earlier.Contains(v.Str)) problems.Add("\"" + f.Key + "\" must name a param written before this one");
                    break;
                case "min": case "max":
                    if (v.IsArray)
                    {
                        if (v.Items.Count != 7) problems.Add("\"" + f.Key + "\" by tier needs 7 numbers (tiers 0-6)");
                        foreach (ManagerJson.Node item in v.Items) if (!item.IsNumber) { problems.Add("\"" + f.Key + "\" holds numbers only"); break; }
                    }
                    else if (!v.IsNumber) problems.Add("\"" + f.Key + "\" is a number or 7 numbers by tier");
                    break;
                case "round":
                    if (!v.IsString || (v.Str != FormatMoney && v.Str != FormatInt && v.Str != "none")) problems.Add("\"round\" is money, int or none");
                    break;
                case "format":
                    if (!v.IsString || (v.Str != FormatMoney && v.Str != FormatInt && v.Str != FormatNumber)) problems.Add("\"format\" is money, int or number");
                    break;
            }
        }
        if (!hasTerm) problems.Add("has no amount (flat, value, pct, profit_days, payroll_days, salary_of, count or random)");
        return problems;
    }
}
