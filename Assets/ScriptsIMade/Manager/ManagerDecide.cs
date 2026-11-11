using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Everything one resolution of a decision needs and produces (the effect ops read and write it).</summary>
public sealed class ManagerApplyContext
{
    public ManagerSystem System;
    public ManagerPending Pending;
    public ManagerSituationDef Situation;
    public ManagerChoiceDef Choice;
    public Dictionary<string, double> Params = new Dictionary<string, double>();
    public List<ManagerResolvedTarget> Targets = new List<ManagerResolvedTarget>();
    public double ValueBefore;
    /// <summary>Net money of the decision's one-off money effects (not gambles): the cloud ledger's Change.</summary>
    public double Money;
    /// <summary>Money put at risk by the decision's gambles (the cloud ledger's Stake; each gamble reports to GambleLedger).</summary>
    public double Staked;
    /// <summary>Net result of the decision's gambles (payouts - stakes), for the result text.</summary>
    public double GambleNet;
    /// <summary>Result text keys to show, in order.</summary>
    public readonly List<string> Results = new List<string>();
    public readonly List<string> Toasts = new List<string>();
    public bool ManagerLeaves;
    public bool Delayed;

    public ManagerFacts Facts() => new ManagerFacts(System, Targets, Params);

    public ManagerResolvedTarget Target(string name)
    {
        foreach (ManagerResolvedTarget t in Targets) if (t.name == name) return t;
        return null;
    }

    /// <summary>An effect's number field (a number or a param name, "-name" for minus).</summary>
    public double Number(ManagerJson.Node field, double fallback = 0)
    {
        return ManagerParams.TryNumber(field, Params, out double v) ? v : fallback;
    }

    /// <summary>An effect's amount field: a number, a param name or an inline spec (worked out now).</summary>
    public double Amount(ManagerJson.Node field)
    {
        if (field == null) return 0;
        if (field.IsObject) return ManagerParams.Evaluate(field, Facts(), Params);
        return Number(field);
    }
}

/// <summary>What the desk shows for one choice.</summary>
public sealed class ManagerChoiceView
{
    public string Id;
    public string Text;
    public string Hint;
    public bool Available;
    public string Reason;
    public bool Recommended;
    public bool Delegate;
    public bool Ask;
}

/// <summary>What the desk shows for one decision.</summary>
public sealed class ManagerDecisionView
{
    public int Uid;
    public string SituationId;
    public string Category;
    public string CategoryLabel;
    public string Title;
    public string Message;
    public string Analysis;
    public bool Urgent;
    public int DaysLeft;
    public bool Withheld;
    public bool Personal;
    public bool NoManager;
    public readonly List<ManagerChoiceView> Choices = new List<ManagerChoiceView>();
}

/// <summary>What happened when the player chose.</summary>
public struct ManagerChooseResult
{
    public bool Done;
    public bool Asked;
    public string Message;
}

// Answering decisions: availability, the view model, resolving a choice, delegation, the manager's reactions
public partial class ManagerSystem
{
    internal string ManagerName => HasManager ? state.manager.name : ManagerText.Get("mgr.ui.no_manager_name");

    public ManagerPending FindPending(int uid) => state.pending.Find(p => p.uid == uid);

    // ------------------------------------------------------------------
    // Texts
    // ------------------------------------------------------------------

    // Args are asked for many times while the desk builds a page: cached for the frame (Dirty clears it)
    int argsUid = -1;
    int argsFrame = -1;
    Dictionary<string, object> argsCached;

    /// <summary>The placeholders of a decision: globals, params (formatted), targets (name and details). A fresh copy.</summary>
    public Dictionary<string, object> Args(ManagerPending p)
    {
        int uid = p != null ? p.uid : 0;
        if (argsCached != null && argsUid == uid && argsFrame == Time.frameCount)
            return new Dictionary<string, object>(argsCached, StringComparer.Ordinal);
        Dictionary<string, object> args = BuildArgs(p);
        argsCached = new Dictionary<string, object>(args, StringComparer.Ordinal);
        argsUid = uid;
        argsFrame = Time.frameCount;
        return args;
    }

    Dictionary<string, object> BuildArgs(ManagerPending p)
    {
        var args = new Dictionary<string, object>(StringComparer.Ordinal);
        CompanyManager company = CompanyManager.Instance;
        args["manager"] = ManagerName;
        args["company"] = CompanyNameUI.GetDisplayName();   // localization (2026-09-29): the default name in the player's language
        args["day"] = Today.ToString(System.Globalization.CultureInfo.InvariantCulture);
        args["days_left"] = p != null ? Math.Max(0, p.expiresDay - Today).ToString(System.Globalization.CultureInfo.InvariantCulture) : "0";
        if (p == null) return args;
        foreach (ManagerParamState prm in p.prms)
            args[prm.name] = ManagerText.FormatValue(prm.value, prm.format);
        List<ManagerResolvedTarget> resolved = Resolve(p.targets);
        for (int i = 0; i < p.targets.Count; i++)
        {
            ManagerTargetState t = p.targets[i];
            ManagerResolvedTarget r = i < resolved.Count ? resolved[i] : null;
            ApplicantData person = r != null ? r.person : null;
            string n = t.name;
            args[n] = !string.IsNullOrEmpty(t.displayName) ? t.displayName : StaffLedger.DisplayName(person);
            args[n + "_salary"] = person != null ? UIFormat.Money(person.salary) : "";
            args[n + "_salary_day"] = person != null ? UIFormat.Money(person.salary / 365.0) : "";
            args[n + "_profit"] = r != null && r.info != null ? UIFormat.Money(r.info.profit)
                                : person != null && StaffLedger.Instance != null ? UIFormat.Money(StaffLedger.Instance.ProjectDailyProfit(person)) : "";
            args[n + "_iq"] = person != null ? person.IQ.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
            args[n + "_degree"] = person != null ? ApplicantText.DegreeLine(person) : "";   // shown in the current language (ApplicantText)
            args[n + "_major"] = person != null ? ApplicantText.Major(person.major) : "";
            args[n + "_days"] = r != null && r.info != null ? r.info.days.ToString(System.Globalization.CultureInfo.InvariantCulture) : "0";
            args[n + "_age"] = person != null ? person.age.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
            args[n + "_exp"] = person != null ? person.experienceYears.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
        }
        return args;
    }

    public string Title(ManagerSituationDef def, ManagerPending p) => ManagerText.Get(def.TitleKey, Args(p));

    public string Message(ManagerSituationDef def, ManagerPending p)
    {
        string key = def.TextKey;
        if (p != null && p.variant > 1) key += "." + p.variant;
        if (p != null && p.again && ManagerText.Has(key + ".again")) key += ".again";
        string personality = p != null && !string.IsNullOrEmpty(p.personality) ? p.personality : (HasManager ? state.manager.personality : null);
        return ManagerText.Get(ManagerText.Variant(key, personality), Args(p));
    }

    public string ChoiceText(ManagerSituationDef def, ManagerChoiceDef c, ManagerPending p) => ManagerText.Get(c.TextKey, Args(p));

    // ------------------------------------------------------------------
    // Availability
    // ------------------------------------------------------------------

    /// <summary>Can this choice be taken now? <paramref name="reason"/> says why not (localized).</summary>
    public bool ChoiceAvailable(ManagerPending p, ManagerSituationDef def, ManagerChoiceDef c, out string reason)
    {
        reason = null;
        if (c == null) return false;
        if (c.Delegate && !HasManager) { reason = ManagerText.Get("mgr.why.no_manager"); return false; }
        if (c.Ask && (!HasManager || p.revealed)) { reason = ManagerText.Get("mgr.why.no_manager"); return false; }

        var prms = ParamValues(p);
        var targets = Resolve(p.targets);
        var facts = new ManagerFacts(this, targets, prms);
        foreach (ManagerCondition r in c.Requires)
            if (!r.Eval(facts)) { reason = ManagerText.Get("mgr.why.not_now"); return false; }

        // People the choice acts on must still be here
        foreach (string name in TargetsUsed(c))
        {
            ManagerResolvedTarget t = targets.Find(x => x.name == name);
            if (t != null && !t.candidate && t.person == null)
            {
                reason = ManagerText.Get("mgr.why.gone", ("name", DisplayOf(p, name)));
                return false;
            }
        }

        // Money, gambling, stocks, seats, build mode
        double value = CompanyManager.Instance != null ? CompanyManager.Instance.companyValue : 0;
        double cost = Cost(c, prms);
        if (cost > 0 && cost > value - Reserve)
        {
            reason = ManagerText.Get("mgr.why.money", ("amount", UIFormat.Money(cost)));
            return false;
        }
        if (Uses(c, "gamble") && !HeatMeter.CanGamble(out string frozen)) { reason = frozen; return false; }
        if (Uses(c, "stocks") && Observe()["stocks_open"] < 1) { reason = ManagerText.Get("mgr.why.stocks"); return false; }
        if (Uses(c, "build_mode") && !FeatureGate.IsUnlocked(Feature.Build))
        {
            reason = ManagerText.Get("mgr.why.build", ("day", FeatureGate.BuildFromDay));
            return false;
        }
        if (Uses(c, "hire") && OfficeCapacity.IsLimited)
        {
            int free = OfficeCapacity.Free + SeatsRented(c, prms) + Count(c, "fire") + Count(c, "lose");
            if (free < Count(c, "hire")) { reason = ManagerText.Get("mgr.why.seats"); return false; }
        }
        return true;
    }

    Dictionary<string, double> ParamValues(ManagerPending p)
    {
        var d = new Dictionary<string, double>();
        if (p != null) foreach (ManagerParamState prm in p.prms) d[prm.name] = prm.value;
        return d;
    }

    string DisplayOf(ManagerPending p, string target)
    {
        ManagerTargetState t = p.targets.Find(x => x.name == target);
        return t != null && !string.IsNullOrEmpty(t.displayName) ? t.displayName : target;
    }

    static IEnumerable<ManagerEffectDef> AllEffects(ManagerChoiceDef c)
    {
        foreach (ManagerEffectDef e in c.Effects)
        {
            yield return e;
            foreach (ManagerGambleOutcome g in e.GambleOutcomes)
                foreach (ManagerEffectDef inner in g.Effects) yield return inner;
        }
        foreach (ManagerOutcomeDef o in c.Outcomes)
            foreach (ManagerEffectDef e in o.Effects) yield return e;
    }

    static bool Uses(ManagerChoiceDef c, string op)
    {
        foreach (ManagerEffectDef e in AllEffects(c)) if (e.Op == op) return true;
        return false;
    }

    static int Count(ManagerChoiceDef c, string op)
    {
        int n = 0;
        foreach (ManagerEffectDef e in c.Effects) if (e.Op == op) n++;
        return n;
    }

    static IEnumerable<string> TargetsUsed(ManagerChoiceDef c)
    {
        foreach (ManagerEffectDef e in AllEffects(c))
        {
            string t = e.Node.GetString("target");
            if (!string.IsNullOrEmpty(t) && t != ManagerSchema.AllEmployees && t != ManagerSchema.TopThree) yield return t;
        }
    }

    /// <summary>What the choice's own effects cost up front: money paid, stakes, stock buys (the worst case of its outcomes).</summary>
    static double Cost(ManagerChoiceDef c, Dictionary<string, double> prms)
    {
        double cost = 0;
        foreach (ManagerEffectDef e in c.Effects) cost += EffectCost(e, prms);
        double worstOutcome = 0;
        foreach (ManagerOutcomeDef o in c.Outcomes)
        {
            double oc = 0;
            foreach (ManagerEffectDef e in o.Effects) oc += EffectCost(e, prms);
            worstOutcome = Math.Max(worstOutcome, oc);
        }
        return cost + worstOutcome;
    }

    static double EffectCost(ManagerEffectDef e, Dictionary<string, double> prms)
    {
        switch (e.Op)
        {
            case "money":
            {
                ManagerJson.Node amount = e.Node.Get("amount");
                if (amount != null && !amount.IsObject && ManagerParams.TryNumber(amount, prms, out double v) && v < 0) return -v;
                return 0;
            }
            case "gamble":
            {
                ManagerJson.Node stake = e.Node.Get("stake");
                return stake != null && !stake.IsObject && ManagerParams.TryNumber(stake, prms, out double s) ? Math.Max(0, s) : 0;
            }
            case "stocks":
            {
                ManagerJson.Node amount = e.Node.Get("amount");
                return amount != null && !amount.IsObject && ManagerParams.TryNumber(amount, prms, out double s) ? Math.Max(0, s) : 0;
            }
        }
        return 0;
    }

    static int SeatsRented(ManagerChoiceDef c, Dictionary<string, double> prms)
    {
        int seats = 0;
        foreach (ManagerEffectDef e in c.Effects)
            if (e.Op == "rent" && ManagerParams.TryNumber(e.Node.Get("seats"), prms, out double s)) seats += Math.Max(0, (int)Math.Round(s));
        return seats;
    }

    // ------------------------------------------------------------------
    // Recommendation
    // ------------------------------------------------------------------

    /// <summary>The choice the manager recommends for this decision (null without a manager, or when they keep quiet).</summary>
    public ManagerChoiceDef Recommendation(ManagerPending p, ManagerSituationDef def)
    {
        if (!HasManager || p == null || def == null || p.withheld) return null;
        return ManagerAdvisor.Recommend(def, state.manager.personality, c => ChoiceAvailable(p, def, c, out _));
    }

    // ------------------------------------------------------------------
    // The view model
    // ------------------------------------------------------------------

    public ManagerDecisionView Describe(ManagerPending p)
    {
        ManagerSituationDef def = p != null ? library.Get(p.situation) : null;
        if (def == null) return null;
        var view = new ManagerDecisionView
        {
            Uid = p.uid,
            SituationId = def.Id,
            Category = def.Category,
            CategoryLabel = ManagerText.Has("mgr.cat." + def.Category) ? ManagerText.Get("mgr.cat." + def.Category) : def.Category,
            Title = Title(def, p),
            Message = Message(def, p),
            Urgent = p.urgent,
            DaysLeft = Math.Max(0, p.expiresDay - Today),
            Withheld = p.withheld && HasManager,
            Personal = def.Personal,
            NoManager = !HasManager
        };
        if (p.revealed && !string.IsNullOrEmpty(def.AnalysisKey))
            view.Analysis = ManagerText.Get(ManagerText.Variant(def.AnalysisKey, p.personality), Args(p));
        bool showRecommendation = !def.HideRecommendation || p.revealed;
        ManagerChoiceDef recommended = showRecommendation ? Recommendation(p, def) : null;
        foreach (ManagerChoiceDef c in def.Choices)
        {
            bool available = ChoiceAvailable(p, def, c, out string reason);
            if (!available && c.HideIfUnavailable) continue;
            if (c.Ask && p.revealed) continue;
            view.Choices.Add(new ManagerChoiceView
            {
                Id = c.Id,
                Text = ChoiceText(def, c, p),
                Hint = !string.IsNullOrEmpty(c.HintKey) ? ManagerText.Get(c.HintKey, Args(p)) : "",
                Available = available,
                Reason = reason,
                Recommended = recommended != null && recommended.Id == c.Id,
                Delegate = c.Delegate,
                Ask = c.Ask
            });
        }
        return view;
    }

    /// <summary>The player looked at a decision (the chip's "new" count drops).</summary>
    public void MarkSeen(int uid)
    {
        ManagerPending p = FindPending(uid);
        if (p == null || p.seen) return;
        p.seen = true;
        p.interruptDone = true;   // seen on the desk: no need to interrupt for it
        RaiseChanged();
    }

    // ------------------------------------------------------------------
    // Choosing
    // ------------------------------------------------------------------

    /// <summary>The player picked <paramref name="choiceId"/> for decision <paramref name="uid"/>.</summary>
    public ManagerChooseResult Choose(int uid, string choiceId)
    {
        ManagerPending p = FindPending(uid);
        ManagerSituationDef def = p != null ? library.Get(p.situation) : null;
        ManagerChoiceDef choice = def != null ? def.Choice(choiceId) : null;
        if (choice == null) return new ManagerChooseResult { Message = "no such choice" };
        if (!ChoiceAvailable(p, def, choice, out string reason)) return new ManagerChooseResult { Message = reason };

        if (choice.Ask)
        {
            p.revealed = true;
            p.seen = true;
            AddSatisfaction(0.5f, "asked for advice");
            Log("Asked " + ManagerName + " about " + def.Id + ".");
            RaiseChanged();
            return new ManagerChooseResult { Asked = true };
        }

        // What the manager advised, as the desk showed it (before the effects change the company)
        ManagerChoiceDef advised = !def.HideRecommendation || p.revealed ? Recommendation(p, def) : null;
        ManagerChoiceDef applied = choice;
        string how = "player";
        bool delegateFailed = false;
        if (choice.Delegate)
        {
            how = "delegated";
            state.stats.delegated++;
            applied = ManagerAdvisor.Recommend(def, state.manager.personality, c => ChoiceAvailable(p, def, c, out _));
            if (applied == null)
            {
                return new ManagerChooseResult { Message = ManagerText.Get("mgr.why.not_now") };
            }
            if (UnityEngine.Random.value >= DelegateChance())
            {
                // Handled badly: the situation's worse option (delegateFail, else its default), when it has a different one
                ManagerChoiceDef fallback = def.Choice(def.DelegateFail) ?? def.Choice(def.DefaultChoice);
                if (fallback != null && fallback != applied && !fallback.Delegate && !fallback.Ask && ChoiceAvailable(p, def, fallback, out _))
                {
                    applied = fallback;
                    delegateFailed = true;
                }
            }
        }

        state.pending.Remove(p);
        var ctx = NewContext(p, def, applied);
        ApplyChoice(ctx, applied);
        if (choice.Delegate)
        {
            AddSatisfaction(1f, "trusted with a decision");
            ctx.Toasts.Insert(0, ManagerText.Get(delegateFailed ? "mgr.toast.delegate.bad" : "mgr.toast.delegate.good",
                ("manager", ManagerName), ("choice", ChoiceText(def, applied, p))));
        }
        else
        {
            React(def, choice, advised);
        }
        state.stats.decided++;
        Remember(def, applied, how, p);
        FinishContext(ctx);
        Log("Decided " + def.Id + ": " + applied.Id + " (" + how + (delegateFailed ? ", handled badly" : "") + ").");
        DropOrphans();
        RaiseChanged();
        return new ManagerChooseResult { Done = true };
    }

    /// <summary>How likely "let me handle it" goes the way the manager meant: satisfaction and competence.</summary>
    public float DelegateChance()
    {
        if (!HasManager) return 0f;
        ManagerPerson m = state.manager;
        return Mathf.Clamp(0.3f + 0.0065f * m.satisfaction * m.competence, 0.25f, 0.95f);
    }

    /// <summary>How the manager takes the player's own choice: followed their advice, overrode it, went against their grain.</summary>
    void React(ManagerSituationDef def, ManagerChoiceDef choice, ManagerChoiceDef recommended)
    {
        if (!HasManager || def.Personal) return;
        if (recommended != null)
        {
            if (recommended.Id == choice.Id)
            {
                state.stats.followed++;
                AddSatisfaction(2f, "advice followed");
            }
            else
            {
                state.stats.overridden++;
                AddSatisfaction(-1f, "advice overridden");
            }
        }
        if (ManagerAdvisor.Clashes(state.manager.personality, choice)) AddSatisfaction(-3f, "a choice against their grain");
    }

    /// <summary>
    /// Note a decision for the history (and "situation.choice" for conditions). <paramref name="choice"/> null = it went
    /// unanswered without a default. With <paramref name="p"/>, the texts the desk showed are kept for "Decided lately".
    /// </summary>
    void Remember(ManagerSituationDef def, ManagerChoiceDef choice, string how, ManagerPending p = null)
    {
        if (choice != null)
        {
            string key = def.Id + "." + choice.Id;
            if (!state.chosen.Contains(key)) state.chosen.Add(key);
        }
        var entry = new ManagerHistory { situation = def.Id, choice = choice != null ? choice.Id : "", day = Today, how = how };
        if (p != null)
        {
            try
            {
                entry.title = Title(def, p);
                entry.choiceText = choice != null ? ChoiceText(def, choice, p) : "";
                entry.manager = HasManager ? state.manager.name : "";
            }
            catch (Exception e) { Debug.LogWarning("[Manager] Couldn't word the history of " + def.Id + ": " + e.Message); }
        }
        state.history.Add(entry);
        if (state.history.Count > 40) state.history.RemoveRange(0, state.history.Count - 40);
    }

    // ------------------------------------------------------------------
    // Applying
    // ------------------------------------------------------------------

    ManagerApplyContext NewContext(ManagerPending p, ManagerSituationDef def, ManagerChoiceDef choice)
    {
        var ctx = new ManagerApplyContext
        {
            System = this,
            Pending = p,
            Situation = def,
            Choice = choice,
            Params = ParamValues(p),
            Targets = Resolve(p.targets),
            ValueBefore = CompanyManager.Instance != null ? CompanyManager.Instance.companyValue : 0
        };
        return ctx;
    }

    /// <summary>A choice's effects, then one of its weighted outcomes (the first whose conditions hold, by weight).</summary>
    void ApplyChoice(ManagerApplyContext ctx, ManagerChoiceDef choice)
    {
        ApplyEffects(ctx, choice.Effects);
        ManagerOutcomeDef outcome = PickOutcome(ctx, choice.Outcomes);
        if (outcome != null)
        {
            ApplyEffects(ctx, outcome.Effects);
            if (!string.IsNullOrEmpty(outcome.ResultKey)) ctx.Results.Add(outcome.ResultKey);
            else if (!string.IsNullOrEmpty(choice.ResultKey)) ctx.Results.Add(choice.ResultKey);
        }
        else if (!string.IsNullOrEmpty(choice.ResultKey))
        {
            ctx.Results.Insert(0, choice.ResultKey);
        }
    }

    static ManagerOutcomeDef PickOutcome(ManagerApplyContext ctx, List<ManagerOutcomeDef> outcomes)
    {
        if (outcomes == null || outcomes.Count == 0) return null;
        ManagerFacts facts = ctx.Facts();
        var open = outcomes.FindAll(o => ManagerCondition.All(o.If, facts));
        if (open.Count == 0) return null;
        double total = 0;
        foreach (ManagerOutcomeDef o in open) total += Math.Max(0, o.Weight);
        double roll = UnityEngine.Random.value * total;
        foreach (ManagerOutcomeDef o in open)
        {
            roll -= Math.Max(0, o.Weight);
            if (roll <= 0) return o;
        }
        return open[open.Count - 1];
    }

    internal void ApplyEffects(ManagerApplyContext ctx, List<ManagerEffectDef> effects)
    {
        if (effects == null) return;
        foreach (ManagerEffectDef e in effects)
        {
            try
            {
                ManagerEffects.Apply(ctx, e);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Manager] Effect " + e.Op + " of " + (ctx.Situation != null ? ctx.Situation.Id : "?") + " failed: " + ex.Message);
                Debug.LogException(ex, this);
            }
            Dirty();
        }
    }

    /// <summary>After the effects: the cloud ledger entry, the toasts, the manager leaving.</summary>
    void FinishContext(ManagerApplyContext ctx)
    {
        CompanyManager company = CompanyManager.Instance;
        if (Math.Abs(ctx.Money) >= 0.5 || ctx.Staked > 0)
        {
            CloudLedger.RecordManager(ctx.Situation.Id, ctx.Choice != null ? ctx.Choice.Id : "expired", ctx.Staked, ctx.Money, ctx.ValueBefore);
            if (ctx.Money > 0) state.stats.moneyIn += ctx.Money;
            else state.stats.moneyOut -= ctx.Money;
        }

        Dictionary<string, object> args = Args(ctx.Pending);
        double net = ctx.Money + ctx.GambleNet;
        args["money"] = UIFormat.SignedMoney(net);
        args["choice"] = ctx.Choice != null ? ChoiceText(ctx.Situation, ctx.Choice, ctx.Pending) : "";
        foreach (string toast in ctx.Toasts)
            GameNotifications.Show(toast, NotificationKind.Info);
        if (ctx.Results.Count > 0)
        {
            var lines = new List<string>();
            foreach (string key in ctx.Results) lines.Add(ManagerText.Get(key, args));
            NotificationKind kind = net > 0.5 ? NotificationKind.Good : net < -0.5 ? NotificationKind.Bad : NotificationKind.Info;
            GameNotifications.Show(string.Join("\n", lines), kind);
        }
        else if (ctx.Choice != null && Math.Abs(net) >= 0.5)
        {
            GameNotifications.Show(ManagerText.Get("mgr.toast.money", ("choice", (string)args["choice"]), ("money", (string)args["money"])),
                                   net > 0 ? NotificationKind.Good : NotificationKind.Bad);
        }
        if (company != null) company.UpdateCompanyStats();
        if (ctx.ManagerLeaves) Resign("left after a decision");
    }
}
