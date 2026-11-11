using System;
using System.Collections.Generic;

/// <summary>A problem found while reading or checking the situation files.</summary>
public sealed class ManagerProblem
{
    public string File;
    public int Line;
    /// <summary>"situation raise_request, choice big"</summary>
    public string Where;
    public string Message;
    /// <summary>Errors stop a situation from being used; warnings are advice.</summary>
    public bool IsError = true;

    public override string ToString()
    {
        string at = string.IsNullOrEmpty(File) ? "" : File + (Line > 0 ? " line " + Line : "") + ": ";
        string where = string.IsNullOrEmpty(Where) ? "" : Where + ": ";
        return (IsError ? "ERROR " : "warning ") + at + where + Message;
    }
}

/// <summary>Engine tuning from Resources/Manager/Engine.json ("engine"); these are the defaults.</summary>
public sealed class ManagerEngineSettings
{
    /// <summary>Chance that a day brings a new situation.</summary>
    public double dailyChance = 0.42;
    /// <summary>Added per status tier reached (up to 4 tiers): bigger companies have more going on.</summary>
    public double chancePerTier = 0.05;
    /// <summary>Added while the desk is empty.</summary>
    public double emptyDeskBonus = 0.2;
    /// <summary>Taken off while this many or more decisions wait.</summary>
    public int busyDeskAt = 4;
    public double busyDeskPenalty = 0.25;
    /// <summary>Decisions waiting at most; a new one pushes the oldest non-urgent one out (its default happens).</summary>
    public int maxPending = 6;
    /// <summary>Days a decision waits before its default happens (situations can set "expires").</summary>
    public int defaultExpiryDays = 12;
    /// <summary>Days an urgent decision waits when it doesn't say.</summary>
    public int urgentExpiryDays = 3;
    /// <summary>Real seconds between two interruptions.</summary>
    public double interruptGapSeconds = 180;
    /// <summary>Real seconds of calm before an interruption or the candidate pick opens.</summary>
    public double calmSeconds = 1.2;
    /// <summary>Days after a manager starts before the daily roll begins.</summary>
    public int firstRollDays = 1;
    /// <summary>Days without a manager after one leaves (random between the two).</summary>
    public int gapDaysMin = 2;
    public int gapDaysMax = 4;
    /// <summary>Default category cooldown in days.</summary>
    public int categoryCooldown = 2;
    /// <summary>Default situation cooldown in days.</summary>
    public int situationCooldown = 15;
    /// <summary>
    /// Chance that an event trigger brings a situation ("triggerChance" in Engine.json); triggers not listed always look.
    /// The daily roll uses dailyChance instead.
    /// </summary>
    public readonly Dictionary<string, double> triggerChance = new Dictionary<string, double>
    {
        { "hire", 0.3 }, { "fire", 0.4 }, { "big_gamble", 0.8 }, { "profit_spike", 0.7 }, { "loss_streak", 0.8 },
        { "tier_up", 1.0 }, { "capacity", 1.0 }, { "manager", 1.0 }
    };

    public double TriggerChance(string trigger) => trigger != null && triggerChance.TryGetValue(trigger, out double c) ? c : 1.0;
}

/// <summary>A category from Engine.json: its cooldown and how the company's state weighs it.</summary>
public sealed class ManagerCategoryDef
{
    public string Id;
    public int Cooldown = -1;
    public readonly List<ManagerWeightMod> Pressure = new List<ManagerWeightMod>();
}

/// <summary>"When this holds, multiply the weight by mul and add add."</summary>
public sealed class ManagerWeightMod
{
    public ManagerCondition If;
    public double Mul = 1.0;
    public double Add;
}

public sealed class ManagerTargetDef
{
    public string Name;
    public string Rule;
    public bool Optional;
}

public sealed class ManagerParamDef
{
    public string Name;
    public ManagerJson.Node Spec;
    public int Line;
}

public sealed class ManagerEffectDef
{
    public string Op;
    public ManagerJson.Node Node;
    public int Line;
    /// <summary>The gamble op's outcomes (empty for other ops).</summary>
    public readonly List<ManagerGambleOutcome> GambleOutcomes = new List<ManagerGambleOutcome>();
}

public sealed class ManagerGambleOutcome
{
    public string Id;
    public double Chance;
    public double Mult;
    public string ResultKey;
    public readonly List<ManagerEffectDef> Effects = new List<ManagerEffectDef>();
    public int Line;
}

/// <summary>A weighted random branch of a choice (applied after the choice's own effects).</summary>
public sealed class ManagerOutcomeDef
{
    public string Id;
    public double Weight = 1.0;
    public readonly List<ManagerCondition> If = new List<ManagerCondition>();
    public readonly List<ManagerEffectDef> Effects = new List<ManagerEffectDef>();
    public string ResultKey;
    public int Line;
}

public sealed class ManagerChoiceDef
{
    public string Id;
    public string TextKey;
    public string HintKey;
    public string ResultKey;
    public readonly List<ManagerCondition> Requires = new List<ManagerCondition>();
    /// <summary>Hide the choice (instead of greying it out) while Requires doesn't hold.</summary>
    public bool HideIfUnavailable;
    public readonly List<ManagerEffectDef> Effects = new List<ManagerEffectDef>();
    public readonly List<ManagerOutcomeDef> Outcomes = new List<ManagerOutcomeDef>();
    /// <summary>"Let the manager handle it": resolved by the manager's personality and satisfaction.</summary>
    public bool Delegate;
    /// <summary>"Ask the manager": shows their analysis and recommendation; the decision stays open.</summary>
    public bool Ask;
    public readonly List<string> Tags = new List<string>();
    public int Line;
}

public sealed class ManagerSituationDef
{
    public string Id;
    public string Category;
    public string File;
    public int Line;
    public readonly List<string> Tags = new List<string>();
    public double Weight = 10;
    public readonly List<ManagerWeightMod> WeightMods = new List<ManagerWeightMod>();
    public readonly List<ManagerCondition> Conditions = new List<ManagerCondition>();
    public int MinDay;
    public int MaxDay;           // 0 = no limit
    public int MinTier;
    public int MaxTier = 99;
    public int MinManagerDays;
    public int Cooldown = -1;    // -1 = the engine default
    public int CategoryCooldown = -1;
    public bool Once;
    public bool FollowupOnly;
    public readonly List<string> Triggers = new List<string>();
    public readonly List<ManagerTargetDef> Targets = new List<ManagerTargetDef>();
    public readonly List<ManagerParamDef> Params = new List<ManagerParamDef>();
    public bool Urgent;
    public int Expires;          // 0 = the engine default
    public bool Interrupt;
    public string DefaultChoice;
    public readonly List<ManagerEffectDef> OnExpire = new List<ManagerEffectDef>();
    public string TitleKey;
    public string TextKey;
    public int TextVariants = 1;
    public string AnalysisKey;
    public bool HideRecommendation;
    public bool Personal;
    public string DelegateFail;
    public readonly List<ManagerChoiceDef> Choices = new List<ManagerChoiceDef>();
    public readonly Dictionary<string, string> Recommend = new Dictionary<string, string>();

    public ManagerChoiceDef Choice(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (ManagerChoiceDef c in Choices)
            if (c.Id == id) return c;
        return null;
    }

    public ManagerTargetDef Target(string name)
    {
        foreach (ManagerTargetDef t in Targets)
            if (t.Name == name) return t;
        return null;
    }

    public bool HasParam(string name)
    {
        foreach (ManagerParamDef p in Params)
            if (p.Name == name) return true;
        return false;
    }

    public bool HasTrigger(string trigger) => Triggers.Count == 0 ? trigger == "day" : Triggers.Contains(trigger);
}

/// <summary>
/// The loaded situation library (plain C#): Engine.json plus every situation file. Broken situations are left out and
/// reported in Problems; the rest load. ManagerValidator checks the meaning (effects, targets, text keys) on top.
/// </summary>
public sealed class ManagerLibrary
{
    public ManagerEngineSettings Engine = new ManagerEngineSettings();
    public readonly Dictionary<string, ManagerCategoryDef> Categories = new Dictionary<string, ManagerCategoryDef>();
    public readonly List<ManagerSituationDef> Situations = new List<ManagerSituationDef>();
    public readonly Dictionary<string, ManagerSituationDef> ById = new Dictionary<string, ManagerSituationDef>();
    public readonly List<ManagerProblem> Problems = new List<ManagerProblem>();

    static readonly HashSet<string> SituationFields = new HashSet<string>
    {
        "id", "category", "tags", "weight", "weightMods", "conditions", "minDay", "maxDay", "minTier", "maxTier",
        "minManagerDays", "cooldown", "categoryCooldown", "once", "followupOnly", "triggers", "targets", "params",
        "urgent", "expires", "interrupt", "defaultChoice", "onExpire", "title", "text", "textVariants", "analysis",
        "hideRecommendation", "personal", "delegateFail", "choices", "recommend"
    };

    static readonly HashSet<string> ChoiceFields = new HashSet<string>
    {
        "id", "text", "hint", "result", "requires", "hideIfUnavailable", "effects", "outcomes", "delegate", "ask", "tags"
    };

    static readonly HashSet<string> OutcomeFields = new HashSet<string> { "id", "weight", "if", "effects", "result" };
    static readonly HashSet<string> GambleOutcomeFields = new HashSet<string> { "id", "chance", "mult", "effects", "result" };

    public bool HasErrors
    {
        get
        {
            foreach (ManagerProblem p in Problems) if (p.IsError) return true;
            return false;
        }
    }

    public ManagerSituationDef Get(string id) => id != null && ById.TryGetValue(id, out ManagerSituationDef s) ? s : null;

    public int CategoryCooldown(string category)
    {
        if (category != null && Categories.TryGetValue(category, out ManagerCategoryDef c) && c.Cooldown >= 0) return c.Cooldown;
        return Engine.categoryCooldown;
    }

    // ------------------------------------------------------------------
    // Loading
    // ------------------------------------------------------------------

    /// <summary>
    /// Load Engine.json (<paramref name="engineText"/>, may be null) and the situation files (file name, text).
    /// </summary>
    public static ManagerLibrary Load(string engineText, IEnumerable<KeyValuePair<string, string>> files)
    {
        var lib = new ManagerLibrary();
        if (!string.IsNullOrEmpty(engineText)) lib.LoadEngine(engineText);
        if (files != null)
            foreach (KeyValuePair<string, string> file in files)
                lib.LoadFile(file.Key, file.Value);
        return lib;
    }

    void Problem(string file, int line, string where, string message, bool error = true)
    {
        Problems.Add(new ManagerProblem { File = file, Line = line, Where = where, Message = message, IsError = error });
    }

    void LoadEngine(string text)
    {
        const string file = "Engine.json";
        ManagerJson.Node root = ManagerJson.Parse(text, out string error);
        if (root == null)
        {
            Problem(file, 0, null, "not valid JSON: " + error);
            return;
        }
        ManagerJson.Node engine = root.Get("engine");
        if (engine != null && engine.IsObject)
        {
            ManagerEngineSettings e = Engine;
            foreach (KeyValuePair<string, ManagerJson.Node> f in engine.Fields)
            {
                if (f.Key.StartsWith("_")) continue;
                if (!f.Value.IsNumber)
                {
                    Problem(file, f.Value.Line, "engine", "\"" + f.Key + "\" must be a number");
                    continue;
                }
                double v = f.Value.Number;
                int i = (int)Math.Round(v);
                switch (f.Key)
                {
                    case "dailyChance": e.dailyChance = v; break;
                    case "chancePerTier": e.chancePerTier = v; break;
                    case "emptyDeskBonus": e.emptyDeskBonus = v; break;
                    case "busyDeskAt": e.busyDeskAt = i; break;
                    case "busyDeskPenalty": e.busyDeskPenalty = v; break;
                    case "maxPending": e.maxPending = Math.Max(1, i); break;
                    case "defaultExpiryDays": e.defaultExpiryDays = Math.Max(1, i); break;
                    case "urgentExpiryDays": e.urgentExpiryDays = Math.Max(1, i); break;
                    case "interruptGapSeconds": e.interruptGapSeconds = Math.Max(0, v); break;
                    case "calmSeconds": e.calmSeconds = Math.Max(0, v); break;
                    case "firstRollDays": e.firstRollDays = Math.Max(0, i); break;
                    case "gapDaysMin": e.gapDaysMin = Math.Max(0, i); break;
                    case "gapDaysMax": e.gapDaysMax = Math.Max(0, i); break;
                    case "categoryCooldown": e.categoryCooldown = Math.Max(0, i); break;
                    case "situationCooldown": e.situationCooldown = Math.Max(0, i); break;
                    default: Problem(file, f.Value.Line, "engine", "unknown setting \"" + f.Key + "\""); break;
                }
            }
            if (e.gapDaysMax < e.gapDaysMin) e.gapDaysMax = e.gapDaysMin;
        }

        ManagerJson.Node chances = root.Get("triggerChance");
        if (chances != null && chances.IsObject)
        {
            foreach (KeyValuePair<string, ManagerJson.Node> f in chances.Fields)
            {
                if (f.Key.StartsWith("_")) continue;
                if (!ManagerSchema.Triggers.ContainsKey(f.Key)) Problem(file, f.Value.Line, "triggerChance", "unknown trigger \"" + f.Key + "\"");
                else if (!f.Value.IsNumber || f.Value.Number < 0 || f.Value.Number > 1) Problem(file, f.Value.Line, "triggerChance", "\"" + f.Key + "\" must be a number from 0 to 1");
                else Engine.triggerChance[f.Key] = f.Value.Number;
            }
        }

        foreach (ManagerJson.Node c in root.GetArray("categories"))
        {
            string id = c.GetString("id");
            if (!ManagerSchema.IsId(id))
            {
                Problem(file, c.Line, "categories", "a category needs an \"id\"");
                continue;
            }
            var cat = new ManagerCategoryDef { Id = id, Cooldown = c.GetInt("cooldown", -1) };
            foreach (ManagerJson.Node m in c.GetArray("pressure"))
            {
                ManagerWeightMod mod = ReadWeightMod(m, file, "category " + id);
                if (mod != null) cat.Pressure.Add(mod);
            }
            if (Categories.ContainsKey(id)) Problem(file, c.Line, "categories", "category \"" + id + "\" is listed twice");
            Categories[id] = cat;
        }
    }

    void LoadFile(string file, string text)
    {
        ManagerJson.Node root = ManagerJson.Parse(text, out string error);
        if (root == null)
        {
            Problem(file, 0, null, "not valid JSON: " + error);
            return;
        }
        if (!root.IsObject)
        {
            Problem(file, root.Line, null, "the file must be an object with a \"situations\" list");
            return;
        }
        string fileCategory = root.GetString("category");
        ManagerJson.Node list = root.Get("situations");
        if (list == null || !list.IsArray)
        {
            Problem(file, root.Line, null, "no \"situations\" list");
            return;
        }
        foreach (ManagerJson.Node node in list.Items)
        {
            ManagerSituationDef s = ReadSituation(node, file, fileCategory);
            if (s == null) continue;
            if (ById.ContainsKey(s.Id))
            {
                Problem(file, node.Line, "situation " + s.Id, "the id is used twice (also in " + ById[s.Id].File + ")");
                continue;
            }
            Situations.Add(s);
            ById[s.Id] = s;
        }
    }

    ManagerWeightMod ReadWeightMod(ManagerJson.Node m, string file, string where)
    {
        if (m == null || !m.IsObject)
        {
            Problem(file, m != null ? m.Line : 0, where, "a weight modifier is {\"if\": \"...\", \"mul\": 2} or \"add\"");
            return null;
        }
        ManagerCondition cond = ReadCondition(m.Get("if"), file, where);
        if (cond == null) return null;
        var mod = new ManagerWeightMod { If = cond, Mul = m.GetNumber("mul", 1.0), Add = m.GetNumber("add", 0.0) };
        if (mod.Mul < 0) Problem(file, m.Line, where, "\"mul\" can't be negative");
        return mod;
    }

    ManagerCondition ReadCondition(ManagerJson.Node n, string file, string where)
    {
        if (n == null || !n.IsString)
        {
            Problem(file, n != null ? n.Line : 0, where, "a condition must be a string like \"happiness < 40\"");
            return null;
        }
        ManagerCondition c = ManagerCondition.Parse(n.Str, out string error);
        if (c == null) Problem(file, n.Line, where, "bad condition " + error);
        return c;
    }

    void ReadConditions(ManagerJson.Node n, List<ManagerCondition> into, string file, string where)
    {
        if (n == null) return;
        if (n.IsString)
        {
            ManagerCondition c = ReadCondition(n, file, where);
            if (c != null) into.Add(c);
            return;
        }
        if (!n.IsArray)
        {
            Problem(file, n.Line, where, "conditions are a string or a list of strings");
            return;
        }
        foreach (ManagerJson.Node item in n.Items)
        {
            ManagerCondition c = ReadCondition(item, file, where);
            if (c != null) into.Add(c);
        }
    }

    void CheckFields(ManagerJson.Node n, HashSet<string> known, string file, string where)
    {
        if (n == null || !n.IsObject) return;
        foreach (KeyValuePair<string, ManagerJson.Node> f in n.Fields)
            if (!f.Key.StartsWith("_") && !known.Contains(f.Key))
                Problem(file, f.Value.Line, where, "unknown field \"" + f.Key + "\"");
    }

    ManagerSituationDef ReadSituation(ManagerJson.Node n, string file, string fileCategory)
    {
        if (n == null || !n.IsObject)
        {
            Problem(file, n != null ? n.Line : 0, null, "a situation must be an object");
            return null;
        }
        string id = n.GetString("id");
        if (!ManagerSchema.IsId(id))
        {
            Problem(file, n.Line, null, "a situation needs an \"id\" (letters, digits, _ . -)");
            return null;
        }
        string where = "situation " + id;
        int errorsBefore = CountErrors();
        CheckFields(n, SituationFields, file, where);

        var s = new ManagerSituationDef
        {
            Id = id,
            File = file,
            Line = n.Line,
            Category = n.GetString("category", fileCategory),
            Weight = n.GetNumber("weight", 10),
            MinDay = n.GetInt("minDay", 0),
            MaxDay = n.GetInt("maxDay", 0),
            MinTier = n.GetInt("minTier", 0),
            MaxTier = n.GetInt("maxTier", 99),
            MinManagerDays = n.GetInt("minManagerDays", 0),
            Cooldown = n.GetInt("cooldown", -1),
            CategoryCooldown = n.GetInt("categoryCooldown", -1),
            Once = n.GetBool("once", false),
            FollowupOnly = n.GetBool("followupOnly", false),
            Urgent = n.GetBool("urgent", false),
            Expires = n.GetInt("expires", 0),
            Interrupt = n.GetBool("interrupt", false),
            DefaultChoice = n.GetString("defaultChoice"),
            TitleKey = n.GetString("title"),
            TextKey = n.GetString("text"),
            TextVariants = Math.Max(1, n.GetInt("textVariants", 1)),
            AnalysisKey = n.GetString("analysis"),
            HideRecommendation = n.GetBool("hideRecommendation", false),
            Personal = n.GetBool("personal", false),
            DelegateFail = n.GetString("delegateFail"),
        };
        if (string.IsNullOrEmpty(s.Category)) Problem(file, n.Line, where, "no \"category\" (on the situation or the file)");
        if (s.Weight < 0) Problem(file, n.Line, where, "\"weight\" can't be negative");
        if (string.IsNullOrEmpty(s.TitleKey)) Problem(file, n.Line, where, "no \"title\" key");
        if (string.IsNullOrEmpty(s.TextKey)) Problem(file, n.Line, where, "no \"text\" key");
        s.Tags.AddRange(n.GetStrings("tags"));
        s.Triggers.AddRange(n.GetStrings("triggers"));

        foreach (ManagerJson.Node m in n.GetArray("weightMods"))
        {
            ManagerWeightMod mod = ReadWeightMod(m, file, where);
            if (mod != null) s.WeightMods.Add(mod);
        }
        ReadConditions(n.Get("conditions"), s.Conditions, file, where);

        ManagerJson.Node targets = n.Get("targets");
        if (targets != null)
        {
            if (!targets.IsObject) Problem(file, targets.Line, where, "\"targets\" is an object: {\"emp\": \"top_performer\"}");
            else
                foreach (KeyValuePair<string, ManagerJson.Node> t in targets.Fields)
                {
                    var target = new ManagerTargetDef { Name = t.Key };
                    if (t.Value.IsString) target.Rule = t.Value.Str;
                    else if (t.Value.IsObject)
                    {
                        target.Rule = t.Value.GetString("rule");
                        target.Optional = t.Value.GetBool("optional", false);
                    }
                    if (string.IsNullOrEmpty(target.Rule))
                    {
                        Problem(file, t.Value.Line, where, "target \"" + t.Key + "\" needs a rule");
                        continue;
                    }
                    s.Targets.Add(target);
                }
        }

        ManagerJson.Node prms = n.Get("params");
        if (prms != null)
        {
            if (!prms.IsObject) Problem(file, prms.Line, where, "\"params\" is an object: {\"cost\": {\"pct\": 3}}");
            else
                foreach (KeyValuePair<string, ManagerJson.Node> p in prms.Fields)
                {
                    if (p.Key.StartsWith("_")) continue;
                    ManagerJson.Node spec = p.Value;
                    if (spec.IsNumber)
                        spec = new ManagerJson.Node
                        {
                            Type = ManagerJson.Kind.Object,
                            Line = spec.Line,
                            Fields = new List<KeyValuePair<string, ManagerJson.Node>> { new KeyValuePair<string, ManagerJson.Node>("value", spec) }
                        };
                    if (!spec.IsObject)
                    {
                        Problem(file, p.Value.Line, where, "param \"" + p.Key + "\" must be a number or an object");
                        continue;
                    }
                    s.Params.Add(new ManagerParamDef { Name = p.Key, Spec = spec, Line = spec.Line });
                }
        }

        ReadEffects(n.Get("onExpire"), s.OnExpire, file, where + ", onExpire");

        ManagerJson.Node choices = n.Get("choices");
        if (choices == null || !choices.IsArray)
            Problem(file, n.Line, where, "no \"choices\" list");
        else
            foreach (ManagerJson.Node c in choices.Items)
            {
                ManagerChoiceDef choice = ReadChoice(c, file, where);
                if (choice == null) continue;
                if (s.Choice(choice.Id) != null)
                {
                    Problem(file, c.Line, where, "choice id \"" + choice.Id + "\" is used twice");
                    continue;
                }
                s.Choices.Add(choice);
            }

        ManagerJson.Node rec = n.Get("recommend");
        if (rec != null)
        {
            if (!rec.IsObject) Problem(file, rec.Line, where, "\"recommend\" is an object: {\"conservative\": \"choice_id\"}");
            else
                foreach (KeyValuePair<string, ManagerJson.Node> r in rec.Fields)
                {
                    if (!r.Value.IsString) Problem(file, r.Value.Line, where, "recommend." + r.Key + " must be a choice id");
                    else s.Recommend[r.Key] = r.Value.Str;
                }
        }

        // A situation with errors in it isn't used (the rest of the file still loads)
        return CountErrors() == errorsBefore ? s : null;
    }

    int CountErrors()
    {
        int n = 0;
        foreach (ManagerProblem p in Problems) if (p.IsError) n++;
        return n;
    }

    ManagerChoiceDef ReadChoice(ManagerJson.Node n, string file, string where)
    {
        if (n == null || !n.IsObject)
        {
            Problem(file, n != null ? n.Line : 0, where, "a choice must be an object");
            return null;
        }
        string id = n.GetString("id");
        if (!ManagerSchema.IsId(id))
        {
            Problem(file, n.Line, where, "a choice needs an \"id\"");
            return null;
        }
        string cw = where + ", choice " + id;
        CheckFields(n, ChoiceFields, file, cw);
        var c = new ManagerChoiceDef
        {
            Id = id,
            Line = n.Line,
            TextKey = n.GetString("text"),
            HintKey = n.GetString("hint"),
            ResultKey = n.GetString("result"),
            HideIfUnavailable = n.GetBool("hideIfUnavailable", false),
            Delegate = n.GetBool("delegate", false),
            Ask = n.GetBool("ask", false),
        };
        if (string.IsNullOrEmpty(c.TextKey)) Problem(file, n.Line, cw, "no \"text\" key");
        c.Tags.AddRange(n.GetStrings("tags"));
        ReadConditions(n.Get("requires"), c.Requires, file, cw);
        ReadEffects(n.Get("effects"), c.Effects, file, cw);
        foreach (ManagerJson.Node o in n.GetArray("outcomes"))
        {
            if (o == null || !o.IsObject)
            {
                Problem(file, o != null ? o.Line : n.Line, cw, "an outcome must be an object");
                continue;
            }
            CheckFields(o, OutcomeFields, file, cw + ", outcome");
            var outcome = new ManagerOutcomeDef
            {
                Id = o.GetString("id"),
                Weight = o.GetNumber("weight", 1.0),
                ResultKey = o.GetString("result"),
                Line = o.Line
            };
            if (outcome.Weight <= 0) Problem(file, o.Line, cw, "an outcome's weight must be above 0");
            ReadConditions(o.Get("if"), outcome.If, file, cw + ", outcome");
            ReadEffects(o.Get("effects"), outcome.Effects, file, cw + ", outcome");
            c.Outcomes.Add(outcome);
        }
        return c;
    }

    void ReadEffects(ManagerJson.Node n, List<ManagerEffectDef> into, string file, string where)
    {
        if (n == null) return;
        if (!n.IsArray)
        {
            Problem(file, n.Line, where, "\"effects\" is a list");
            return;
        }
        foreach (ManagerJson.Node e in n.Items)
        {
            if (e == null || !e.IsObject)
            {
                Problem(file, e != null ? e.Line : n.Line, where, "an effect must be an object with an \"op\"");
                continue;
            }
            string op = e.GetString("op");
            if (string.IsNullOrEmpty(op))
            {
                Problem(file, e.Line, where, "an effect needs an \"op\"");
                continue;
            }
            var effect = new ManagerEffectDef { Op = op, Node = e, Line = e.Line };
            if (op == "gamble")
            {
                foreach (ManagerJson.Node g in e.GetArray("outcomes"))
                {
                    if (g == null || !g.IsObject)
                    {
                        Problem(file, g != null ? g.Line : e.Line, where, "a gamble outcome must be an object");
                        continue;
                    }
                    CheckFields(g, GambleOutcomeFields, file, where + ", gamble outcome");
                    var outcome = new ManagerGambleOutcome
                    {
                        Id = g.GetString("id"),
                        Chance = g.GetNumber("chance", -1),
                        Mult = g.GetNumber("mult", -1),
                        ResultKey = g.GetString("result"),
                        Line = g.Line
                    };
                    ReadEffects(g.Get("effects"), outcome.Effects, file, where + ", gamble outcome");
                    effect.GambleOutcomes.Add(outcome);
                }
            }
            into.Add(effect);
        }
    }
}
