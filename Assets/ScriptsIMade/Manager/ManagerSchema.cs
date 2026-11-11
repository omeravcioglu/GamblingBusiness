using System;
using System.Collections.Generic;

/// <summary>
/// Everything a manager situation file may name (plain C#, shared by the game and the validator): personalities,
/// triggers, the observed company facts conditions can test, target rules, param fields and the effect ops with their
/// fields. A new effect op is one entry in <see cref="Ops"/> here plus its handler in ManagerEffects (the game checks at
/// start that both lists match).
/// </summary>
public static class ManagerSchema
{
    // ------------------------------------------------------------------
    // Personalities
    // ------------------------------------------------------------------

    public const string Conservative = "conservative";
    public const string Aggressive = "aggressive";
    public const string People = "people";
    public const string Numbers = "numbers";

    /// <summary>The manager personalities (saved by these ids; text keys use them as suffixes).</summary>
    public static readonly string[] Personalities = { Conservative, Aggressive, People, Numbers };

    public static bool IsPersonality(string id) => Array.IndexOf(Personalities, id) >= 0;

    // ------------------------------------------------------------------
    // Triggers: what makes the engine look for a situation
    // ------------------------------------------------------------------

    public static readonly Dictionary<string, string> Triggers = new Dictionary<string, string>
    {
        { "day", "the daily roll (default)" },
        { "capacity", "office capacity or headcount changed and the office is nearly full or full" },
        { "big_gamble", "a gamble staked a big share of the company or lost a lot" },
        { "profit_spike", "a day's profit was far above the recent average" },
        { "loss_streak", "several losing days in a row" },
        { "hire", "someone was hired" },
        { "fire", "someone was fired" },
        { "tier_up", "the company reached a new status tier" },
        { "manager", "a new manager just started (their first message)" },
    };

    // ------------------------------------------------------------------
    // Facts: the observed company state conditions test ("happiness < 40")
    // ------------------------------------------------------------------

    public static readonly Dictionary<string, string> Facts = new Dictionary<string, string>
    {
        { "value", "company value ($)" },
        { "cash_tier", "tier of the current value: 0 below $1M, then $1M/$10M/$50M/$100M/$500M/$1B = 1..6" },
        { "tier", "highest status tier reached (StatusLadder, never drops)" },
        { "profit", "last day's operating result ($)" },
        { "profit_avg", "average daily operating result over the last 7 days ($)" },
        { "profit_base", "money scale: max(profit_avg, 0.3% of value, $300)" },
        { "growth", "company value growth over the last 7 days (%)" },
        { "losing_days", "losing days in a row (operating result below 0)" },
        { "day", "current day" },
        { "happiness", "employee happiness 0-100" },
        { "trust", "investor trust 0-100" },
        { "heat", "heat 0-100" },
        { "heat_level", "heat level 0 cool, 1 warm, 2 hot, 3 on fire" },
        { "gamble_frozen", "1 while the board froze gambling" },
        { "employees", "employees on staff" },
        { "payroll", "daily payroll ($)" },
        { "avg_salary", "average yearly salary ($)" },
        { "capacity_limited", "1 when the buildings limit the headcount" },
        { "capacity", "seats (9999 without a limit)" },
        { "free", "free seats (9999 without a limit)" },
        { "capacity_pct", "employees / seats in % (0 without a limit)" },
        { "full", "1 when no seat is free (hiring blocked)" },
        { "rented_seats", "seats rented by the manager now" },
        { "skill_points", "unspent skill points" },
        { "skills", "skills unlocked" },
        { "top_profit", "daily profit of the best performer ($)" },
        { "low_profit", "daily profit of the weakest performer ($)" },
        { "unhappy_staff", "employees whose mood (manager's notes) is below 35" },
        { "golden_staff", "golden employees on staff" },
        { "debt", "bank loan debt ($)" },
        { "stocks_open", "1 when the stock market is open and trading unlocked" },
        { "stocks_value", "value of the stocks the company owns ($)" },
        { "build_open", "1 when build mode is unlocked" },
        { "board_days", "days until the next board meeting (99 without one)" },
        { "board_target", "1 while a board target runs" },
        { "gamble_loss_recent", "money lost gambling in the last 3 days ($)" },
        { "gamble_stake_recent", "money staked on gambles in the last 3 days ($)" },
        { "gambles_week", "gambles in the last 7 days" },
        { "fired_recent", "people fired in the last 10 days" },
        { "hired_recent", "people hired in the last 5 days" },
        { "policies", "company policies running" },
        { "pending", "decisions waiting on the desk" },
        { "mgr_satisfaction", "the manager's satisfaction 0-100" },
        { "mgr_days", "days the manager has worked here" },
        { "mgr_salary", "the manager's yearly salary ($)" },
        { "mgr_pay_ratio", "the manager's salary / what they expect, in % (100 = content)" },
        { "mgr_expected", "the yearly salary the manager expects now ($; grows with the company's tier and their time here)" },
        { "mgr_count", "managers hired this run" },
        { "mgr_competence", "the manager's competence in % (80-115)" },
    };

    /// <summary>"prefix:arg" predicates conditions can test ("flag:food_fight", "!policy:food").</summary>
    public static readonly Dictionary<string, string> Predicates = new Dictionary<string, string>
    {
        { "flag", "a memory flag is set (flag:name)" },
        { "policy", "a company policy runs (policy:id)" },
        { "rental", "rented office space (rental, or rental:id)" },
        { "perk", "a build perk is active (perk:id)" },
        { "share", "an investor takes a revenue share (share, or share:id)" },
        { "personality", "the manager's personality (personality:conservative)" },
        { "feature", "a feature is unlocked (feature:Stocks, feature:Build, feature:Heat, feature:Casino...)" },
        { "seen", "the situation came up before in this run (seen:situation_id)" },
        { "chose", "that choice was made before in this run (chose:situation_id.choice_id)" },
        { "target", "the situation's target resolved (target:emp)" },
    };

    /// <summary>Numbers of a resolved target ("emp.profit > 200").</summary>
    public static readonly Dictionary<string, string> TargetFacts = new Dictionary<string, string>
    {
        { "exists", "1 when the target resolved" },
        { "profit", "daily profit ($)" },
        { "salary", "yearly salary ($)" },
        { "mood", "mood in the manager's notes 0-100 (50 = neutral)" },
        { "days", "days on staff" },
        { "iq", "IQ" },
        { "age", "age" },
        { "exp", "years of experience" },
        { "golden", "1 for a golden employee" },
        { "underpaid", "daily profit / daily salary" },
    };

    // ------------------------------------------------------------------
    // Targets
    // ------------------------------------------------------------------

    public enum TargetKind { Employee, Candidate }

    public sealed class TargetRule
    {
        public string Name;
        public TargetKind Kind;
        public string Doc;
    }

    public static readonly Dictionary<string, TargetRule> TargetRules = BuildTargetRules();

    static Dictionary<string, TargetRule> BuildTargetRules()
    {
        var rules = new Dictionary<string, TargetRule>();
        void Add(string name, TargetKind kind, string doc) => rules[name] = new TargetRule { Name = name, Kind = kind, Doc = doc };
        Add("top_performer", TargetKind.Employee, "highest daily profit");
        Add("low_performer", TargetKind.Employee, "lowest daily profit");
        Add("most_underpaid", TargetKind.Employee, "highest profit per salary dollar (profit > 0)");
        Add("highest_paid", TargetKind.Employee, "highest salary");
        Add("random_employee", TargetKind.Employee, "anyone");
        Add("newest", TargetKind.Employee, "hired most recently");
        Add("longest_serving", TargetKind.Employee, "on staff the longest");
        Add("unhappy", TargetKind.Employee, "lowest mood in the manager's notes, below 40");
        Add("golden", TargetKind.Employee, "a golden employee");
        Add("high_iq", TargetKind.Employee, "IQ 120 or more (the highest)");
        Add("candidate_talent", TargetKind.Candidate, "a strong applicant: IQ 125-150, good GPA, 5-15 years");
        Add("candidate_cheap", TargetKind.Candidate, "a decent applicant who asks for a low salary");
        Add("candidate_star", TargetKind.Candidate, "an exceptional applicant (a golden one)");
        return rules;
    }

    /// <summary>Target names a situation can't declare (they mean something else in effects).</summary>
    public static readonly string[] ReservedTargetNames = { "all", "top3", "manager", "company", "day" };

    /// <summary>Words effects accept instead of a target name: every employee, the three best performers.</summary>
    public const string AllEmployees = "all";
    public const string TopThree = "top3";

    // ------------------------------------------------------------------
    // Params: named numbers worked out when a situation is created
    // ------------------------------------------------------------------

    /// <summary>Fields of a param spec object. See ManagerParams for what each does.</summary>
    public static readonly Dictionary<string, string> ParamFields = new Dictionary<string, string>
    {
        { "flat", "dollars added" },
        { "value", "a plain number (odds, percentages, counts shown in text)" },
        { "value_of", "an earlier param's value (seats x rate: value_of seats, times rate)" },
        { "pct", "% of company value" },
        { "profit_days", "days of profit_base" },
        { "payroll_days", "days of the whole daily payroll" },
        { "salary_of", "a target's salary: with salary_pct (% of the yearly salary) or salary_days (days of salary)" },
        { "salary_pct", "% of salary_of's yearly salary (per: year or day)" },
        { "salary_days", "days of salary_of's salary" },
        { "per", "\"day\" or \"year\" for salary_pct (default year)" },
        { "count", "a fact name (employees, capacity, free...) with count_pct" },
        { "count_pct", "% of the count (default 100)" },
        { "random", "[min, max] uniform" },
        { "times", "multiply by an earlier param" },
        { "plus", "add an earlier param" },
        { "min", "lower bound: a number or 7 numbers by cash tier" },
        { "max", "upper bound: a number or 7 numbers by cash tier" },
        { "round", "\"money\" (2 significant figures), \"int\", \"none\"" },
        { "format", "how {name} shows in text: \"money\", \"int\", \"number\"" },
    };

    // ------------------------------------------------------------------
    // Effect ops
    // ------------------------------------------------------------------

    /// <summary>How a field of an effect is checked.</summary>
    public enum FieldType
    {
        /// <summary>A number or a param name (optionally with a leading '-').</summary>
        Number,
        /// <summary>Money: a number, a param name (optionally '-'), or an inline param spec.</summary>
        Amount,
        /// <summary>An employee target of the situation, or "all" / "top3".</summary>
        EmployeeTarget,
        /// <summary>A candidate target of the situation.</summary>
        CandidateTarget,
        /// <summary>A situation id.</summary>
        Situation,
        /// <summary>An id (letters, digits, _ . -).</summary>
        Id,
        /// <summary>A text key in Manager.csv.</summary>
        TextKey,
        Bool,
        /// <summary>A free string (build spots, categories).</summary>
        Text,
        /// <summary>The gamble's outcomes array.</summary>
        Outcomes,
        /// <summary>A list of target names.</summary>
        TargetList,
    }

    public sealed class OpField
    {
        public string Name;
        public FieldType Type;
        public bool Required;
        public string Doc;
    }

    public sealed class OpInfo
    {
        public string Name;
        public string Doc;
        public readonly List<OpField> Fields = new List<OpField>();

        public OpField Field(string name)
        {
            foreach (OpField f in Fields)
                if (f.Name == name) return f;
            return null;
        }
    }

    public static readonly Dictionary<string, OpInfo> Ops = BuildOps();

    static Dictionary<string, OpInfo> BuildOps()
    {
        var ops = new Dictionary<string, OpInfo>();
        OpInfo Op(string name, string doc)
        {
            var info = new OpInfo { Name = name, Doc = doc };
            ops[name] = info;
            return info;
        }
        void F(OpInfo op, string name, FieldType type, bool required, string doc) =>
            op.Fields.Add(new OpField { Name = name, Type = type, Required = required, Doc = doc });

        OpInfo o;
        o = Op("money", "one-off money (negative = a cost); recorded in the cloud ledger as a manager entry");
        F(o, "amount", FieldType.Amount, true, "dollars");

        o = Op("salary", "change salaries by a percentage (ApplicantData.salary)");
        F(o, "target", FieldType.EmployeeTarget, true, "who (a target, all, top3)");
        F(o, "pct", FieldType.Number, true, "percent, e.g. 20 or -5");

        o = Op("mood", "change the mood the manager notes for someone (0-100, 50 neutral)");
        F(o, "target", FieldType.EmployeeTarget, true, "who");
        F(o, "add", FieldType.Number, true, "points");

        o = Op("happiness", "employee happiness now (losses softened by Employee Retention)");
        F(o, "add", FieldType.Number, true, "points");

        o = Op("trust", "investor trust now");
        F(o, "add", FieldType.Number, true, "points");

        o = Op("heat", "heat now");
        F(o, "add", FieldType.Number, true, "points");

        o = Op("skill_points", "skill points (the company's knowledge)");
        F(o, "add", FieldType.Number, true, "points");

        o = Op("train", "years of experience for employees (capped by age)");
        F(o, "target", FieldType.EmployeeTarget, true, "who");
        F(o, "years", FieldType.Number, true, "years");

        o = Op("fire", "fire someone through the staff files (severance, morale)");
        F(o, "target", FieldType.EmployeeTarget, true, "who (a target)");

        o = Op("lose", "someone quits (no severance; the staff are a little sad)");
        F(o, "target", FieldType.EmployeeTarget, true, "who (a target)");

        o = Op("hire", "hire a candidate target (needs a free seat)");
        F(o, "target", FieldType.CandidateTarget, true, "the candidate");

        o = Op("rent", "rent office seats for some days (daily rent in company money)");
        F(o, "seats", FieldType.Number, true, "seats");
        F(o, "rate", FieldType.Amount, true, "rent per seat per day");
        F(o, "days", FieldType.Number, true, "days");
        F(o, "id", FieldType.Id, false, "rental id (default: the situation id); renting the same id again extends it");

        o = Op("policy", "start or stop a company policy: a daily cost and daily meter nudges");
        F(o, "id", FieldType.Id, true, "policy id");
        F(o, "action", FieldType.Text, false, "start (default) or stop");
        F(o, "cost_day", FieldType.Amount, false, "flat cost per day");
        F(o, "cost_employee", FieldType.Amount, false, "cost per employee per day");
        F(o, "happiness", FieldType.Number, false, "happiness per day");
        F(o, "trust", FieldType.Number, false, "trust per day");
        F(o, "heat", FieldType.Number, false, "heat per day");
        F(o, "days", FieldType.Number, false, "days (0 = until stopped)");

        o = Op("revenue_share", "an investor takes a share of each positive day's operating result for some days (a cost)");
        F(o, "pct", FieldType.Number, true, "percent of the day's operating result");
        F(o, "days", FieldType.Number, true, "days");
        F(o, "id", FieldType.Id, false, "share id (default: the situation id)");

        o = Op("gamble", "stake company money on weighted outcomes (reported to GambleLedger as source \"manager\")");
        F(o, "stake", FieldType.Amount, true, "dollars at risk");
        F(o, "outcomes", FieldType.Outcomes, true, "[{chance, mult, effects?, result?}] (chances add up to 1)");

        o = Op("stocks", "buy stocks for an amount (needs the stock market)");
        F(o, "amount", FieldType.Amount, true, "dollars to invest");
        F(o, "pick", FieldType.Text, false, "steady (default), hot or random");

        o = Op("followup", "schedule another situation in some days, with this one's targets and params");
        F(o, "situation", FieldType.Situation, true, "situation id");
        F(o, "days", FieldType.Number, true, "days from now");
        F(o, "chance", FieldType.Number, false, "0-1 (default 1)");
        F(o, "keep", FieldType.TargetList, false, "targets to carry over (default: all)");

        o = Op("flag", "set or clear a memory flag");
        F(o, "set", FieldType.Id, false, "flag to set");
        F(o, "clear", FieldType.Id, false, "flag to clear");
        F(o, "days", FieldType.Number, false, "days it lasts (0 = for good)");

        o = Op("satisfaction", "the manager's satisfaction");
        F(o, "add", FieldType.Number, true, "points");

        o = Op("mgr_salary", "change the manager's salary: by a percentage, or up to a share of what they expect");
        F(o, "pct", FieldType.Number, false, "percent");
        F(o, "to_expected", FieldType.Number, false, "raise to at least this share of their expected salary (1 = what they expect)");

        o = Op("mgr_leave", "the manager leaves after this decision (a new one is picked after a short gap)");

        o = Op("build_mode", "open build mode once the desk closes (build money only comes from gambling)");
        F(o, "spot", FieldType.Text, false, "hint for the building system: expand, new_building, office...");

        o = Op("build_perk", "a build-budget discount the building system can read (ManagerBuildPerks)");
        F(o, "id", FieldType.Id, true, "perk id");
        F(o, "discount", FieldType.Number, true, "percent off build-budget prices");
        F(o, "days", FieldType.Number, true, "days it lasts");
        F(o, "category", FieldType.Text, false, "any (default), floor, room, furniture, building");
        F(o, "uses", FieldType.Number, false, "purchases it covers (0 = unlimited)");

        o = Op("delay", "ask again later: the same decision comes back in some days");
        F(o, "days", FieldType.Number, true, "days");

        o = Op("toast", "an extra message");
        F(o, "text", FieldType.TextKey, true, "text key");

        o = Op("end_policy", "stop every policy, rental or share with this id (cleanup for chains)");
        F(o, "id", FieldType.Id, true, "id");

        return ops;
    }

    /// <summary>Build perk categories the building system can ask about.</summary>
    public static readonly string[] BuildCategories = { "any", "floor", "room", "furniture", "building" };

    /// <summary>Stock picks for the stocks op.</summary>
    public static readonly string[] StockPicks = { "steady", "hot", "random" };

    // ------------------------------------------------------------------
    // Recommendation tags
    // ------------------------------------------------------------------

    /// <summary>Tags a choice can carry (from its effects, or written in "tags") for the recommendation fallback.</summary>
    public static readonly string[] ChoiceTags = { "safe", "cost", "income", "risk", "gamble", "people_up", "people_down", "cut", "growth", "reputation", "stall" };

    /// <summary>How much each personality likes each tag (the fallback recommendation adds these up).</summary>
    public static double TagWeight(string personality, string tag)
    {
        switch (personality)
        {
            case Conservative:
                switch (tag)
                {
                    case "safe": return 3; case "cut": return 2; case "reputation": return 1; case "stall": return 0.5;
                    case "cost": return -1; case "risk": return -3; case "gamble": return -4; case "growth": return -0.5;
                }
                break;
            case Aggressive:
                switch (tag)
                {
                    case "growth": return 3; case "risk": return 2; case "gamble": return 2; case "income": return 1;
                    case "safe": return -2; case "stall": return -2; case "cut": return -0.5;
                }
                break;
            case People:
                switch (tag)
                {
                    case "people_up": return 3; case "reputation": return 0.5;
                    case "people_down": return -4; case "cut": return -1; case "cost": return -0.5;
                }
                break;
            case Numbers:
                switch (tag)
                {
                    case "income": return 3; case "cut": return 2; case "growth": return 1;
                    case "cost": return -2; case "gamble": return -1; case "risk": return -0.5; case "people_up": return -0.5;
                }
                break;
        }
        return 0;
    }

    /// <summary>True when an id is letters, digits, '_' , '.' or '-'.</summary>
    public static bool IsId(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        foreach (char c in id)
        {
            bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '.' || c == '-';
            if (!ok) return false;
        }
        return true;
    }
}
