using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

/// <summary>
/// Checks the manager's situation files and their texts (plain C#: Tools > Game > Manager > Validate Situations runs it
/// in the Editor, and it also runs outside Unity). On top of what ManagerLibrary reports while loading: effect ops and
/// their fields, targets, params, conditions (fact and predicate names), 2-4 choices, recommendations, defaults,
/// follow-ups, and that every text key exists in Manager.csv in every language with only the placeholders the situation
/// provides.
/// </summary>
public static class ManagerValidator
{
    /// <summary>The String Table / CSV of the manager's texts.</summary>
    public const string Table = "Manager";

    /// <summary>Languages every key needs (the CSV's columns after Key and Context).</summary>
    public static readonly string[] Languages = { "en", "zh-Hans", "ja", "ko", "tr" };

    /// <summary>FeatureGate's features (the "feature:" predicate).</summary>
    public static readonly string[] Features =
        { "RiskCards", "Crises", "Skills", "Staff", "Build", "BoardMeetings", "Heat", "Stocks", "Casino", "Online", "Manager" };

    /// <summary>Placeholders every situation text may use.</summary>
    public static readonly string[] GlobalPlaceholders = { "manager", "company", "day", "days_left" };

    /// <summary>Placeholders only result texts get (what the decision did).</summary>
    public static readonly string[] ResultPlaceholders = { "money", "choice" };

    /// <summary>What {emp_...} placeholders a target gives (plus {emp} itself = the name).</summary>
    public static readonly string[] TargetPlaceholderSuffixes = { "salary", "salary_day", "profit", "iq", "degree", "major", "days", "age", "exp" };

    static readonly Regex PlaceholderRegex = new Regex(@"\{([A-Za-z_][A-Za-z0-9_]*)(?::[^}]*)?\}", RegexOptions.Compiled);
    static readonly Regex CodeKeyRegex = new Regex("\"(mgr\\.[a-z0-9_.]+)\"", RegexOptions.Compiled);

    /// <summary>A parsed Manager.csv: key → the five language cells.</summary>
    public sealed class Texts
    {
        public readonly Dictionary<string, string[]> Rows = new Dictionary<string, string[]>(StringComparer.Ordinal);
        public bool Loaded;

        public bool Has(string key) => key != null && Rows.ContainsKey(key);

        public string English(string key) => key != null && Rows.TryGetValue(key, out string[] cells) ? cells[0] : null;
    }

    /// <summary>The placeholders a text uses ("{emp} asked for {amount}" → emp, amount).</summary>
    public static List<string> Placeholders(string text)
    {
        var names = new List<string>();
        if (string.IsNullOrEmpty(text)) return names;
        foreach (Match m in PlaceholderRegex.Matches(text))
            if (!names.Contains(m.Groups[1].Value)) names.Add(m.Groups[1].Value);
        return names;
    }

    /// <summary>Complete "mgr.*" keys written as string literals in C# sources (keys built from pieces are skipped).</summary>
    public static List<string> KeysUsedInCode(IEnumerable<string> sources)
    {
        var keys = new List<string>();
        foreach (string source in sources)
        {
            if (string.IsNullOrEmpty(source)) continue;
            foreach (Match m in CodeKeyRegex.Matches(source))
            {
                string key = m.Groups[1].Value;
                if (key.EndsWith(".")) continue;
                if (!keys.Contains(key)) keys.Add(key);
            }
        }
        return keys;
    }

    // ------------------------------------------------------------------
    // CSV
    // ------------------------------------------------------------------

    /// <summary>Read Manager.csv and check its shape: the 7 columns, no empty cell, no key twice, translations with only English's placeholders.</summary>
    public static Texts ReadCsv(string csvText, List<ManagerProblem> problems)
    {
        var texts = new Texts();
        const string file = "Manager.csv";
        if (string.IsNullOrEmpty(csvText))
        {
            problems.Add(new ManagerProblem { File = file, Message = "Manager.csv is missing or empty" });
            return texts;
        }
        L10nCsv.Sheet sheet;
        try { sheet = L10nCsv.ReadSheet(csvText); }
        catch (Exception e)
        {
            problems.Add(new ManagerProblem { File = file, Message = "couldn't read the CSV: " + e.Message });
            return texts;
        }
        string[] header = { "Key", "Context", "en", "zh-Hans", "ja", "ko", "tr" };
        if (sheet.Header == null || sheet.Header.Count != header.Length)
            problems.Add(new ManagerProblem { File = file, Line = 1, Message = "the header must be exactly: " + string.Join(",", header) });
        else
            for (int i = 0; i < header.Length; i++)
                if (sheet.Header[i].Trim().TrimStart('﻿') != header[i])
                    problems.Add(new ManagerProblem { File = file, Line = 1, Message = "column " + (i + 1) + " must be \"" + header[i] + "\" (is \"" + sheet.Header[i] + "\")" });

        int row = 1;
        foreach (List<string> cells in sheet.Rows)
        {
            row++;
            string key = L10nCsv.Sheet.Cell(cells, 0).Trim();
            if (key.Length == 0 || key.StartsWith("#")) continue;
            string where = "key " + key;
            if (cells.Count != header.Length)
                problems.Add(new ManagerProblem { File = file, Line = row, Where = where, Message = "has " + cells.Count + " cells instead of 7" });
            if (!key.StartsWith("mgr."))
                problems.Add(new ManagerProblem { File = file, Line = row, Where = where, Message = "manager keys start with \"mgr.\"" });
            if (texts.Rows.ContainsKey(key))
            {
                problems.Add(new ManagerProblem { File = file, Line = row, Where = where, Message = "the key is in the file twice" });
                continue;
            }
            var values = new string[Languages.Length];
            for (int i = 0; i < Languages.Length; i++)
            {
                values[i] = L10nCsv.Sheet.Cell(cells, 2 + i);
                if (string.IsNullOrWhiteSpace(values[i]))
                    problems.Add(new ManagerProblem { File = file, Line = row, Where = where, Message = "the " + Languages[i] + " cell is empty" });
            }
            if (string.IsNullOrWhiteSpace(L10nCsv.Sheet.Cell(cells, 1)))
                problems.Add(new ManagerProblem { File = file, Line = row, Where = where, Message = "the Context cell is empty", IsError = false });

            List<string> english = Placeholders(values[0]);
            for (int i = 1; i < Languages.Length; i++)
                foreach (string name in Placeholders(values[i]))
                    if (!english.Contains(name))
                        problems.Add(new ManagerProblem { File = file, Line = row, Where = where, Message = Languages[i] + " uses {" + name + "}, which the English text doesn't have" });
            CheckBraces(values, file, row, where, problems);
            texts.Rows[key] = values;
        }
        texts.Loaded = true;
        return texts;
    }

    static void CheckBraces(string[] values, string file, int row, string where, List<ManagerProblem> problems)
    {
        for (int i = 0; i < values.Length; i++)
        {
            int depth = 0;
            foreach (char c in values[i] ?? "")
            {
                if (c == '{') depth++;
                else if (c == '}') depth--;
                if (depth < 0 || depth > 1) break;
            }
            if (depth != 0)
                problems.Add(new ManagerProblem { File = file, Line = row, Where = where, Message = Languages[i] + " has unbalanced { } (Smart Strings need pairs)" });
        }
    }

    // ------------------------------------------------------------------
    // Situations
    // ------------------------------------------------------------------

    /// <summary>
    /// Check everything. <paramref name="texts"/> may be null (text keys aren't checked then). <paramref name="runtimeOps"/>:
    /// the effect ops the game implements (null = not checked). <paramref name="codeKeys"/>: keys the C# code uses.
    /// </summary>
    public static List<ManagerProblem> Validate(ManagerLibrary lib, Texts texts, ICollection<string> runtimeOps, ICollection<string> codeKeys)
    {
        var problems = new List<ManagerProblem>(lib.Problems);

        if (runtimeOps != null)
        {
            foreach (string op in ManagerSchema.Ops.Keys)
                if (!runtimeOps.Contains(op)) problems.Add(new ManagerProblem { Where = "effects", Message = "op \"" + op + "\" has no handler in ManagerEffects" });
            foreach (string op in runtimeOps)
                if (!ManagerSchema.Ops.ContainsKey(op)) problems.Add(new ManagerProblem { Where = "effects", Message = "handler \"" + op + "\" isn't in ManagerSchema.Ops" });
        }

        if (lib.Categories.Count == 0)
            problems.Add(new ManagerProblem { File = "Engine.json", Message = "no categories (Resources/Manager/Engine.json)", IsError = false });
        foreach (ManagerCategoryDef cat in lib.Categories.Values)
            foreach (ManagerWeightMod mod in cat.Pressure)
                CheckCondition(mod.If, null, lib, problems, "Engine.json", 0, "category " + cat.Id);

        var referencedFollowups = new HashSet<string>();
        foreach (ManagerSituationDef s in lib.Situations)
            CheckSituation(s, lib, texts, problems, referencedFollowups);

        foreach (ManagerSituationDef s in lib.Situations)
            if (s.FollowupOnly && !referencedFollowups.Contains(s.Id))
                problems.Add(new ManagerProblem { File = s.File, Line = s.Line, Where = "situation " + s.Id, Message = "followupOnly, but no followup op schedules it", IsError = false });

        if (texts != null && texts.Loaded)
        {
            if (codeKeys != null)
                foreach (string key in codeKeys)
                    if (!texts.Has(key)) problems.Add(new ManagerProblem { File = "Manager.csv", Where = "code", Message = "the code uses \"" + key + "\", which Manager.csv doesn't have" });
            CheckVariantSuffixes(lib, texts, problems);
        }
        return problems;
    }

    static void CheckSituation(ManagerSituationDef s, ManagerLibrary lib, Texts texts, List<ManagerProblem> problems, HashSet<string> followups)
    {
        string where = "situation " + s.Id;
        void Err(int line, string message) => problems.Add(new ManagerProblem { File = s.File, Line = line > 0 ? line : s.Line, Where = where, Message = message });
        void Warn(int line, string message) => problems.Add(new ManagerProblem { File = s.File, Line = line > 0 ? line : s.Line, Where = where, Message = message, IsError = false });

        if (!string.IsNullOrEmpty(s.Category) && lib.Categories.Count > 0 && !lib.Categories.ContainsKey(s.Category))
            Warn(0, "category \"" + s.Category + "\" isn't in Engine.json (it gets the default cooldown and no pressure)");
        foreach (string t in s.Triggers)
            if (!ManagerSchema.Triggers.ContainsKey(t)) Err(0, "unknown trigger \"" + t + "\"");
        if (s.Interrupt && !s.Urgent) Err(0, "\"interrupt\" needs \"urgent\": true");
        if (s.MaxDay > 0 && s.MaxDay < s.MinDay) Err(0, "maxDay is below minDay");
        if (s.MaxTier < s.MinTier) Err(0, "maxTier is below minTier");

        // Targets
        var targetNames = new List<string>();
        var employeeTargets = new List<string>();
        var candidateTargets = new List<string>();
        foreach (ManagerTargetDef t in s.Targets)
        {
            if (!ManagerSchema.IsId(t.Name) || t.Name.Contains(".")) Err(0, "target name \"" + t.Name + "\" must be letters, digits and _");
            if (Array.IndexOf(ManagerSchema.ReservedTargetNames, t.Name) >= 0) Err(0, "\"" + t.Name + "\" can't be a target name");
            if (!ManagerSchema.TargetRules.TryGetValue(t.Rule, out ManagerSchema.TargetRule rule))
            {
                Err(0, "target \"" + t.Name + "\": unknown rule \"" + t.Rule + "\"");
                continue;
            }
            targetNames.Add(t.Name);
            (rule.Kind == ManagerSchema.TargetKind.Employee ? employeeTargets : candidateTargets).Add(t.Name);
        }

        // Params
        var paramNames = new List<string>();
        foreach (ManagerParamDef p in s.Params)
        {
            if (!ManagerSchema.IsId(p.Name) || p.Name.Contains(".")) Err(p.Line, "param name \"" + p.Name + "\" must be letters, digits and _");
            if (targetNames.Contains(p.Name) || paramNames.Contains(p.Name)) Err(p.Line, "the name \"" + p.Name + "\" is used twice");
            if (Array.IndexOf(GlobalPlaceholders, p.Name) >= 0 || Array.IndexOf(ResultPlaceholders, p.Name) >= 0)
                Err(p.Line, "\"" + p.Name + "\" is a reserved placeholder name");
            foreach (string message in ManagerParams.Check(p.Spec, paramNames, targetNames))
                Err(p.Line, "param \"" + p.Name + "\": " + message);
            paramNames.Add(p.Name);
        }

        var ctx = new Ctx { S = s, Lib = lib, Texts = texts, Problems = problems, Params = paramNames, Employees = employeeTargets, Candidates = candidateTargets, Targets = targetNames, Followups = followups };

        foreach (ManagerCondition c in s.Conditions) CheckCondition(c, ctx, lib, problems, s.File, s.Line, where);
        foreach (ManagerWeightMod m in s.WeightMods) CheckCondition(m.If, ctx, lib, problems, s.File, s.Line, where);
        CheckEffects(s.OnExpire, ctx, where + ", onExpire");

        // Choices
        int alwaysShown = 0;
        int delegates = 0, asks = 0;
        foreach (ManagerChoiceDef c in s.Choices)
        {
            string cw = where + ", choice " + c.Id;
            if (!c.HideIfUnavailable) alwaysShown++;
            if (c.Delegate) delegates++;
            if (c.Ask) asks++;
            if (c.Delegate && c.Ask) Err(c.Line, "choice " + c.Id + " can't both delegate and ask");
            if ((c.Delegate || c.Ask) && (c.Effects.Count > 0 || c.Outcomes.Count > 0))
                Err(c.Line, "choice " + c.Id + ": delegate and ask choices have no effects of their own");
            if (c.HideIfUnavailable && c.Requires.Count == 0) Warn(c.Line, "choice " + c.Id + " hides when unavailable but has no \"requires\"");
            if (string.IsNullOrEmpty(c.HintKey)) problems.Add(new ManagerProblem { File = s.File, Line = c.Line, Where = cw, Message = "no \"hint\" key (the consequence line under the choice)" });
            foreach (string tag in c.Tags)
                if (Array.IndexOf(ManagerSchema.ChoiceTags, tag) < 0) Err(c.Line, "choice " + c.Id + ": unknown tag \"" + tag + "\"");
            foreach (ManagerCondition r in c.Requires) CheckCondition(r, ctx, lib, problems, s.File, c.Line, cw);
            CheckEffects(c.Effects, ctx, cw);
            foreach (ManagerOutcomeDef o in c.Outcomes)
            {
                foreach (ManagerCondition cond in o.If) CheckCondition(cond, ctx, lib, problems, s.File, o.Line, cw + ", outcome");
                CheckEffects(o.Effects, ctx, cw + ", outcome");
                CheckKey(o.ResultKey, ctx, o.Line, cw + ", outcome result", true);
            }
            CheckKey(c.TextKey, ctx, c.Line, cw + " text", false);
            CheckKey(c.HintKey, ctx, c.Line, cw + " hint", false);
            CheckKey(c.ResultKey, ctx, c.Line, cw + " result", true);
        }
        if (s.Choices.Count < 2 || s.Choices.Count > 4) Err(0, "has " + s.Choices.Count + " choices (2-4)");
        if (alwaysShown < 2) Err(0, "fewer than 2 choices always show (hideIfUnavailable)");
        if (delegates > 1) Err(0, "more than one delegate choice");
        if (asks > 1) Err(0, "more than one ask choice");
        if (asks > 0 && string.IsNullOrEmpty(s.AnalysisKey)) Err(0, "an ask choice needs \"analysis\" (the manager's answer)");
        if (delegates > 0 && s.Choices.Count - delegates - asks < 1) Err(0, "a delegate choice needs other choices to pick from");

        // Recommendations and defaults
        foreach (KeyValuePair<string, string> r in s.Recommend)
        {
            if (!ManagerSchema.IsPersonality(r.Key)) { Err(0, "recommend: unknown personality \"" + r.Key + "\""); continue; }
            ManagerChoiceDef c = s.Choice(r.Value);
            if (c == null) Err(0, "recommend." + r.Key + ": no choice \"" + r.Value + "\"");
            else if (c.Delegate || c.Ask) Err(0, "recommend." + r.Key + ": the manager can't recommend a delegate or ask choice");
        }
        if (!string.IsNullOrEmpty(s.DefaultChoice))
        {
            ManagerChoiceDef c = s.Choice(s.DefaultChoice);
            if (c == null) Err(0, "defaultChoice: no choice \"" + s.DefaultChoice + "\"");
            else if (c.Delegate || c.Ask) Err(0, "defaultChoice can't be a delegate or ask choice");
            else if (c.Requires.Count > 0) Warn(0, "defaultChoice \"" + c.Id + "\" has requirements; if they fail on expiry nothing happens");
        }
        else if (s.Urgent) Warn(0, "urgent without a defaultChoice: ignoring it only costs the manager's goodwill");
        if (!string.IsNullOrEmpty(s.DelegateFail))
        {
            ManagerChoiceDef c = s.Choice(s.DelegateFail);
            if (c == null) Err(0, "delegateFail: no choice \"" + s.DelegateFail + "\"");
            else if (c.Delegate || c.Ask) Err(0, "delegateFail can't be a delegate or ask choice");
        }

        // Texts
        CheckKey(s.TitleKey, ctx, 0, where + " title", false);
        CheckKey(s.TextKey, ctx, 0, where + " text", false);
        for (int v = 2; v <= s.TextVariants; v++) CheckKey(s.TextKey + "." + v, ctx, 0, where + " text variant " + v, false);
        if (!string.IsNullOrEmpty(s.AnalysisKey)) CheckKey(s.AnalysisKey, ctx, 0, where + " analysis", false);
    }

    sealed class Ctx
    {
        public ManagerSituationDef S;
        public ManagerLibrary Lib;
        public Texts Texts;
        public List<ManagerProblem> Problems;
        public List<string> Params, Employees, Candidates, Targets;
        public HashSet<string> Followups;
    }

    static void CheckCondition(ManagerCondition c, Ctx ctx, ManagerLibrary lib, List<ManagerProblem> problems, string file, int line, string where)
    {
        if (c == null) return;
        var numbers = new List<string>();
        var predicates = new List<KeyValuePair<string, string>>();
        c.CollectNames(numbers, predicates);
        void Err(string message) => problems.Add(new ManagerProblem { File = file, Line = line, Where = where, Message = "condition \"" + c.Source + "\": " + message });

        foreach (string name in numbers)
        {
            if (ManagerSchema.Facts.ContainsKey(name)) continue;
            int dot = name.IndexOf('.');
            if (dot > 0)
            {
                string target = name.Substring(0, dot), field = name.Substring(dot + 1);
                if (ctx == null || !ctx.Targets.Contains(target)) Err("\"" + target + "\" isn't a target of the situation");
                else if (!ManagerSchema.TargetFacts.ContainsKey(field)) Err("targets have no \"" + field + "\" (" + string.Join(", ", ManagerSchema.TargetFacts.Keys) + ")");
                continue;
            }
            if (ctx != null && ctx.Params.Contains(name)) continue;   // params can be compared too ("cost > 1000")
            Err("unknown fact \"" + name + "\"");
        }
        foreach (KeyValuePair<string, string> p in predicates)
        {
            if (!ManagerSchema.Predicates.ContainsKey(p.Key)) { Err("unknown predicate \"" + p.Key + ":\""); continue; }
            string arg = p.Value;
            switch (p.Key)
            {
                case "flag": case "policy": case "perk":
                    if (!ManagerSchema.IsId(arg)) Err(p.Key + ": needs an id");
                    break;
                case "rental": case "share":
                    if (arg.Length > 0 && !ManagerSchema.IsId(arg)) Err(p.Key + ": bad id");
                    break;
                case "personality":
                    if (!ManagerSchema.IsPersonality(arg)) Err("unknown personality \"" + arg + "\"");
                    break;
                case "feature":
                    if (Array.IndexOf(Features, arg) < 0) Err("unknown feature \"" + arg + "\" (" + string.Join(", ", Features) + ")");
                    break;
                case "seen":
                    if (lib.Get(arg) == null) Err("no situation \"" + arg + "\"");
                    break;
                case "chose":
                {
                    int dot = arg.LastIndexOf('.');
                    ManagerSituationDef other = dot > 0 ? lib.Get(arg.Substring(0, dot)) : null;
                    if (other == null || other.Choice(arg.Substring(dot + 1)) == null) Err("\"" + arg + "\" isn't situation_id.choice_id");
                    break;
                }
                case "target":
                    if (ctx == null || !ctx.Targets.Contains(arg)) Err("\"" + arg + "\" isn't a target of the situation");
                    break;
            }
        }
    }

    static void CheckEffects(List<ManagerEffectDef> effects, Ctx ctx, string where)
    {
        foreach (ManagerEffectDef e in effects) CheckEffect(e, ctx, where);
    }

    static void CheckEffect(ManagerEffectDef e, Ctx ctx, string where)
    {
        string ew = where + ", effect " + e.Op;
        void Err(string message) => ctx.Problems.Add(new ManagerProblem { File = ctx.S.File, Line = e.Line, Where = ew, Message = message });

        if (!ManagerSchema.Ops.TryGetValue(e.Op, out ManagerSchema.OpInfo info))
        {
            Err("unknown op (known: " + string.Join(", ", ManagerSchema.Ops.Keys) + ")");
            return;
        }
        foreach (KeyValuePair<string, ManagerJson.Node> f in e.Node.Fields)
            if (f.Key != "op" && !f.Key.StartsWith("_") && info.Field(f.Key) == null) Err("unknown field \"" + f.Key + "\"");
        foreach (ManagerSchema.OpField field in info.Fields)
        {
            ManagerJson.Node v = e.Node.Get(field.Name);
            if (v == null)
            {
                if (field.Required) Err("needs \"" + field.Name + "\" (" + field.Doc + ")");
                continue;
            }
            switch (field.Type)
            {
                case ManagerSchema.FieldType.Number:
                    if (!v.IsNumber)
                    {
                        string p = ManagerParams.ReferencedParam(v);
                        if (p == null || !ctx.Params.Contains(p)) Err("\"" + field.Name + "\" must be a number or a param name");
                    }
                    break;
                case ManagerSchema.FieldType.Amount:
                    if (v.IsObject)
                        foreach (string message in ManagerParams.Check(v, ctx.Params, ctx.Targets)) Err("\"" + field.Name + "\": " + message);
                    else if (!v.IsNumber)
                    {
                        string p = ManagerParams.ReferencedParam(v);
                        if (p == null || !ctx.Params.Contains(p)) Err("\"" + field.Name + "\" must be a number, a param name or a param spec");
                    }
                    break;
                case ManagerSchema.FieldType.EmployeeTarget:
                {
                    bool group = v.IsString && (v.Str == ManagerSchema.AllEmployees || v.Str == ManagerSchema.TopThree);
                    bool single = e.Op == "fire" || e.Op == "lose";
                    if (!v.IsString || (!ctx.Employees.Contains(v.Str) && !(group && !single)))
                        Err("\"target\" must be an employee target of the situation" + (single ? "" : ", \"all\" or \"top3\""));
                    break;
                }
                case ManagerSchema.FieldType.CandidateTarget:
                    if (!v.IsString || !ctx.Candidates.Contains(v.Str)) Err("\"target\" must be a candidate target of the situation");
                    break;
                case ManagerSchema.FieldType.Situation:
                    if (!v.IsString || ctx.Lib.Get(v.Str) == null) Err("no situation \"" + (v.IsString ? v.Str : v.ToString()) + "\"");
                    else ctx.Followups.Add(v.Str);
                    break;
                case ManagerSchema.FieldType.Id:
                    if (!v.IsString || !ManagerSchema.IsId(v.Str)) Err("\"" + field.Name + "\" must be an id");
                    break;
                case ManagerSchema.FieldType.TextKey:
                    if (!v.IsString) Err("\"" + field.Name + "\" must be a text key");
                    else CheckKey(v.Str, ctx, e.Line, ew, true);
                    break;
                case ManagerSchema.FieldType.Bool:
                    if (!v.IsBool) Err("\"" + field.Name + "\" must be true or false");
                    break;
                case ManagerSchema.FieldType.Text:
                    if (!v.IsString) Err("\"" + field.Name + "\" must be a string");
                    break;
                case ManagerSchema.FieldType.TargetList:
                    if (!v.IsArray) Err("\"" + field.Name + "\" must be a list of target names");
                    else foreach (ManagerJson.Node item in v.Items)
                            if (!item.IsString || !ctx.Targets.Contains(item.Str)) Err("\"" + field.Name + "\": \"" + item + "\" isn't a target");
                    break;
                case ManagerSchema.FieldType.Outcomes:
                    break;   // below
            }
        }

        // Op-specific checks
        switch (e.Op)
        {
            case "gamble":
            {
                if (e.GambleOutcomes.Count < 2) Err("a gamble needs at least 2 outcomes");
                double total = 0;
                foreach (ManagerGambleOutcome o in e.GambleOutcomes)
                {
                    if (o.Chance <= 0 || o.Chance > 1) Err("each outcome needs a \"chance\" between 0 and 1");
                    if (o.Mult < 0) Err("each outcome needs a \"mult\" (0 = the stake is lost, 2 = double)");
                    total += Math.Max(0, o.Chance);
                    CheckEffects(o.Effects, ctx, ew + " outcome");
                    CheckKey(o.ResultKey, ctx, o.Line, ew + " outcome result", true);
                    foreach (ManagerEffectDef inner in o.Effects)
                        if (inner.Op == "gamble") Err("a gamble can't contain another gamble");
                }
                if (Math.Abs(total - 1.0) > 0.001) Err("outcome chances add up to " + total.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ", not 1");
                break;
            }
            case "policy":
            {
                string action = e.Node.GetString("action", "start");
                if (action != "start" && action != "stop") Err("\"action\" is start or stop");
                break;
            }
            case "build_perk":
            {
                string category = e.Node.GetString("category", "any");
                if (Array.IndexOf(ManagerSchema.BuildCategories, category) < 0) Err("\"category\" is one of " + string.Join(", ", ManagerSchema.BuildCategories));
                ManagerParams.TryNumber(e.Node.Get("discount"), null, out double discount);
                if (e.Node.Get("discount") != null && e.Node.Get("discount").IsNumber && (discount <= 0 || discount > 50)) Err("\"discount\" is 1-50 (%)");
                break;
            }
            case "stocks":
            {
                string pick = e.Node.GetString("pick", "steady");
                if (Array.IndexOf(ManagerSchema.StockPicks, pick) < 0) Err("\"pick\" is steady, hot or random");
                break;
            }
            case "flag":
                if (!e.Node.Has("set") && !e.Node.Has("clear")) Err("needs \"set\" or \"clear\"");
                break;
            case "mgr_salary":
                if (!e.Node.Has("pct") && !e.Node.Has("to_expected")) Err("needs \"pct\" or \"to_expected\"");
                break;
            case "followup":
            {
                ManagerJson.Node chance = e.Node.Get("chance");
                if (chance != null && chance.IsNumber && (chance.Number <= 0 || chance.Number > 1)) Err("\"chance\" is above 0 and at most 1");
                break;
            }
        }
    }

    /// <summary>A text key must be in the CSV, in every language, using only placeholders the situation gives.</summary>
    static void CheckKey(string key, Ctx ctx, int line, string where, bool isResult)
    {
        if (string.IsNullOrEmpty(key) || ctx.Texts == null || !ctx.Texts.Loaded) return;
        if (!key.StartsWith("mgr."))
        {
            ctx.Problems.Add(new ManagerProblem { File = ctx.S.File, Line = line > 0 ? line : ctx.S.Line, Where = where, Message = "text key \"" + key + "\" must start with \"mgr.\"" });
            return;
        }
        var keys = new List<string> { key };
        // Personality variants of this key and of its numbered variants use the same placeholders
        foreach (string personality in ManagerSchema.Personalities)
            if (ctx.Texts.Has(key + "." + personality)) keys.Add(key + "." + personality);
        if (ctx.Texts.Has(key + ".again")) keys.Add(key + ".again");

        if (!ctx.Texts.Has(key))
        {
            ctx.Problems.Add(new ManagerProblem { File = ctx.S.File, Line = line > 0 ? line : ctx.S.Line, Where = where, Message = "text key \"" + key + "\" isn't in Manager.csv" });
            return;
        }
        var allowed = AllowedPlaceholders(ctx, isResult);
        foreach (string k in keys)
            foreach (string name in Placeholders(ctx.Texts.English(k)))
                if (!allowed.Contains(name))
                    ctx.Problems.Add(new ManagerProblem
                    {
                        File = ctx.S.File, Line = line > 0 ? line : ctx.S.Line, Where = where,
                        Message = "\"" + k + "\" uses {" + name + "}, which the situation doesn't provide (params, targets, " + string.Join(", ", GlobalPlaceholders) + (isResult ? ", money, choice" : "") + ")"
                    });
    }

    static HashSet<string> AllowedPlaceholders(Ctx ctx, bool isResult)
    {
        var allowed = new HashSet<string>(GlobalPlaceholders);
        foreach (string p in ctx.Params) allowed.Add(p);
        foreach (string t in ctx.Targets)
        {
            allowed.Add(t);
            foreach (string suffix in TargetPlaceholderSuffixes) allowed.Add(t + "_" + suffix);
        }
        if (isResult) foreach (string r in ResultPlaceholders) allowed.Add(r);
        return allowed;
    }

    /// <summary>A key like mgr.s.x.msg.aggresive (a typo) would silently never show: warn about unknown suffixes of situation text keys.</summary>
    static void CheckVariantSuffixes(ManagerLibrary lib, Texts texts, List<ManagerProblem> problems)
    {
        var bases = new HashSet<string>();
        foreach (ManagerSituationDef s in lib.Situations)
        {
            if (!string.IsNullOrEmpty(s.TextKey))
            {
                bases.Add(s.TextKey);
                for (int v = 2; v <= s.TextVariants; v++) bases.Add(s.TextKey + "." + v);
            }
            if (!string.IsNullOrEmpty(s.AnalysisKey)) bases.Add(s.AnalysisKey);
        }
        foreach (string key in texts.Rows.Keys)
        {
            int dot = key.LastIndexOf('.');
            if (dot <= 0) continue;
            string head = key.Substring(0, dot), tail = key.Substring(dot + 1);
            if (!bases.Contains(head)) continue;
            bool known = ManagerSchema.IsPersonality(tail) || tail == "again" || int.TryParse(tail, out _);
            if (!known)
                problems.Add(new ManagerProblem { File = "Manager.csv", Where = "key " + key, Message = "unknown variant suffix \"." + tail + "\" (a personality, .again or a number)", IsError = false });
        }
    }

    /// <summary>A short report: counts, then every problem.</summary>
    public static string Report(ManagerLibrary lib, List<ManagerProblem> problems, Texts texts)
    {
        int errors = 0, warnings = 0;
        foreach (ManagerProblem p in problems) { if (p.IsError) errors++; else warnings++; }
        var sb = new System.Text.StringBuilder();
        sb.Append("[Manager] ").Append(lib.Situations.Count).Append(" situations in ")
          .Append(lib.Categories.Count).Append(" categories");
        if (texts != null && texts.Loaded) sb.Append(", ").Append(texts.Rows.Count).Append(" text keys");
        sb.Append(": ").Append(errors).Append(errors == 1 ? " error, " : " errors, ").Append(warnings).Append(warnings == 1 ? " warning." : " warnings.");
        foreach (ManagerProblem p in problems) sb.Append('\n').Append(p);
        return sb.ToString();
    }
}
