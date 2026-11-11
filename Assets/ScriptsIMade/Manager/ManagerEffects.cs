using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The effect ops a situation's choices use, by name (ManagerSchema.Ops documents their fields; the game checks at start
/// that every op there has a handler here). Add an op: a schema entry plus Register("name", handler) here.
/// Money rules (cloud save): one-off money goes through "money" (counted into the decision's cloud-ledger entry) or
/// "gamble" (reported to GambleLedger with its real odds); recurring effects ("policy", "rent", "revenue_share", the
/// manager's salary) are daily COSTS through DailyEconomyHooks, never income.
/// </summary>
public static class ManagerEffects
{
    /// <summary>GambleLedger source of the manager's gambles and risky investments.</summary>
    public const string GambleSource = "manager";

    static readonly Dictionary<string, Action<ManagerApplyContext, ManagerEffectDef>> handlers = Build();

    public static bool Has(string op) => op != null && handlers.ContainsKey(op);

    public static ICollection<string> Names => handlers.Keys;

    /// <summary>Add or replace an op's handler (it must also be in ManagerSchema.Ops for the validator).</summary>
    public static void Register(string op, Action<ManagerApplyContext, ManagerEffectDef> handler)
    {
        if (!string.IsNullOrEmpty(op) && handler != null) handlers[op] = handler;
    }

    public static void Apply(ManagerApplyContext ctx, ManagerEffectDef effect)
    {
        if (effect == null) return;
        if (!handlers.TryGetValue(effect.Op, out Action<ManagerApplyContext, ManagerEffectDef> handler))
        {
            Debug.LogWarning("[Manager] Unknown effect op \"" + effect.Op + "\" (" + (ctx.Situation != null ? ctx.Situation.Id : "?") + ").");
            return;
        }
        handler(ctx, effect);
    }

    static CompanyManager Company => CompanyManager.Instance;
    static int Today => Company != null ? Company.currentDay : 0;

    static Dictionary<string, Action<ManagerApplyContext, ManagerEffectDef>> Build()
    {
        var h = new Dictionary<string, Action<ManagerApplyContext, ManagerEffectDef>>();
        h["money"] = Money;
        h["salary"] = Salary;
        h["mood"] = Mood;
        h["happiness"] = (ctx, e) => { if (Company != null) Company.AddHappiness((float)ctx.Number(e.Node.Get("add"))); };
        h["trust"] = (ctx, e) =>
        {
            if (Company == null) return;
            Company.investorTrust = Mathf.Clamp(Company.investorTrust + (float)ctx.Number(e.Node.Get("add")), 0f, 100f);
        };
        h["heat"] = (ctx, e) => { if (HeatMeter.Instance != null) HeatMeter.Instance.AddHeat((float)ctx.Number(e.Node.Get("add")), "manager decision"); };
        h["skill_points"] = SkillPoints;
        h["train"] = Train;
        h["fire"] = Fire;
        h["lose"] = Lose;
        h["hire"] = Hire;
        h["rent"] = Rent;
        h["policy"] = Policy;
        h["revenue_share"] = RevenueShare;
        h["gamble"] = Gamble;
        h["stocks"] = Stocks;
        h["followup"] = Followup;
        h["flag"] = Flag;
        h["satisfaction"] = (ctx, e) => ctx.System.AddSatisfaction((float)ctx.Number(e.Node.Get("add")), "decision");
        h["mgr_salary"] = ManagerSalary;
        h["mgr_leave"] = (ctx, e) => ctx.ManagerLeaves = true;
        h["build_mode"] = (ctx, e) => ctx.System.RequestBuildMode(e.Node.GetString("spot", ""));
        h["build_perk"] = BuildPerk;
        h["delay"] = Delay;
        h["toast"] = (ctx, e) => ctx.Toasts.Add(ManagerText.Get(e.Node.GetString("text"), ctx.System.Args(ctx.Pending)));
        h["end_policy"] = EndPolicy;
        return h;
    }

    // ------------------------------------------------------------------
    // People
    // ------------------------------------------------------------------

    /// <summary>The employees a target field means: a target of the decision, "all", or "top3" (the best three).</summary>
    static List<ApplicantData> People(ManagerApplyContext ctx, string target)
    {
        var list = new List<ApplicantData>();
        CompanyManager company = Company;
        if (company == null || company.employees == null) return list;
        if (target == ManagerSchema.AllEmployees)
        {
            foreach (ApplicantData e in company.employees) if (e != null) list.Add(e);
            return list;
        }
        if (target == ManagerSchema.TopThree)
        {
            List<ManagerStaffInfo> staff = new List<ManagerStaffInfo>(ctx.System.Staff());
            staff.Sort((a, b) => b.profit.CompareTo(a.profit));
            for (int i = 0; i < staff.Count && i < 3; i++) list.Add(staff[i].person);
            return list;
        }
        ManagerResolvedTarget t = ctx.Target(target);
        if (t != null && !t.candidate && t.person != null && company.employees.Contains(t.person)) list.Add(t.person);
        return list;
    }

    static string NameOf(ApplicantData person) => StaffLedger.DisplayName(person);

    // ------------------------------------------------------------------
    // Handlers
    // ------------------------------------------------------------------

    static void Money(ManagerApplyContext ctx, ManagerEffectDef e)
    {
        CompanyManager company = Company;
        if (company == null) return;
        double amount = ctx.Amount(e.Node.Get("amount"));
        if (amount == 0 || double.IsNaN(amount) || double.IsInfinity(amount)) return;
        double before = company.companyValue;
        company.AddMoney(amount);
        ctx.Money += company.companyValue - before;   // what really moved (a cost stops at $0)
    }

    static void Salary(ManagerApplyContext ctx, ManagerEffectDef e)
    {
        double pct = ctx.Number(e.Node.Get("pct"));
        if (pct == 0) return;
        foreach (ApplicantData person in People(ctx, e.Node.GetString("target")))
        {
            double salary = Math.Round(person.salary * (1.0 + pct / 100.0));
            person.salary = (int)Math.Max(1000, Math.Min(int.MaxValue / 2, salary));
        }
        if (Company != null) Company.UpdateCompanyStats();
    }

    static void Mood(ManagerApplyContext ctx, ManagerEffectDef e)
    {
        float add = (float)ctx.Number(e.Node.Get("add"));
        foreach (ApplicantData person in People(ctx, e.Node.GetString("target"))) ctx.System.AddMood(person, add);
    }

    static void SkillPoints(ManagerApplyContext ctx, ManagerEffectDef e)
    {
        SkillTreeManager skills = SkillTreeManager.Instance;
        if (skills == null) return;
        int add = (int)Math.Round(ctx.Number(e.Node.Get("add")));
        if (add == 0) return;
        skills.SetSkillPoints(Math.Max(0, skills.skillPoints + add));
        if (add > 0) ctx.Toasts.Add(ManagerText.Get("mgr.toast.skill_points", ("count", add)));
    }

    static void Train(ManagerApplyContext ctx, ManagerEffectDef e)
    {
        int years = (int)Math.Round(ctx.Number(e.Node.Get("years")));
        if (years <= 0) return;
        foreach (ApplicantData person in People(ctx, e.Node.GetString("target")))
            person.experienceYears = Math.Min(person.experienceYears + years, Math.Max(person.experienceYears, person.age - 16));
        if (Company != null) Company.UpdateCompanyStats();
    }

    static void Fire(ManagerApplyContext ctx, ManagerEffectDef e)
    {
        foreach (ApplicantData person in People(ctx, e.Node.GetString("target")))
        {
            if (StaffLedger.Instance != null) StaffLedger.Instance.Fire(person);
            else if (Company != null) Company.RemoveEmployee(person);
        }
        OfficeCapacity.NotifyChanged();
    }

    static void Lose(ManagerApplyContext ctx, ManagerEffectDef e)
    {
        CompanyManager company = Company;
        if (company == null) return;
        foreach (ApplicantData person in People(ctx, e.Node.GetString("target")))
        {
            string name = NameOf(person);
            if (!company.RemoveEmployee(person)) continue;
            company.AddHappiness(-1.5f);
            ctx.Toasts.Add(ManagerText.Get("mgr.toast.left", ("name", name)));
        }
        OfficeCapacity.NotifyChanged();
    }

    static void Hire(ManagerApplyContext ctx, ManagerEffectDef e)
    {
        CompanyManager company = Company;
        ManagerResolvedTarget t = ctx.Target(e.Node.GetString("target"));
        if (company == null || t == null || !t.candidate || t.person == null) return;
        if (OfficeCapacity.IsLimited && OfficeCapacity.IsFull)
        {
            ctx.Toasts.Add(ManagerText.Get("mgr.toast.no_seat", ("name", NameOf(t.person))));
            return;
        }
        company.HireEmployee(t.person);
        OfficeCapacity.NotifyChanged();
        ctx.Toasts.Add(ManagerText.Get("mgr.toast.joined", ("name", NameOf(t.person))));
    }

    static void Rent(ManagerApplyContext ctx, ManagerEffectDef e)
    {
        int seats = (int)Math.Round(ctx.Number(e.Node.Get("seats")));
        double rate = Math.Max(0, ctx.Amount(e.Node.Get("rate")));
        int days = (int)Math.Round(ctx.Number(e.Node.Get("days")));
        if (seats <= 0 || days <= 0) return;
        string id = e.Node.GetString("id", ctx.Situation != null ? ctx.Situation.Id : "rent");
        int day = Today;
        List<ManagerRental> rentals = ctx.System.State.rentals;
        ManagerRental existing = rentals.Find(r => r.id == id);
        if (existing != null)
        {
            existing.untilDay = Math.Max(existing.untilDay, day - 1) + days;
            existing.seats = Math.Max(existing.seats, seats);
            existing.rate = Math.Max(existing.rate, rate);
        }
        else
        {
            rentals.Add(new ManagerRental { id = id, seats = seats, rate = rate, startDay = day, untilDay = day + days - 1 });
        }
        ctx.System.RentalsChanged();
        ctx.Toasts.Add(ManagerText.Get("mgr.toast.rented", ("seats", seats), ("days", days), ("rent", UIFormat.Money(seats * rate))));
    }

    static void Policy(ManagerApplyContext ctx, ManagerEffectDef e)
    {
        string id = e.Node.GetString("id");
        if (string.IsNullOrEmpty(id)) return;
        List<ManagerPolicy> policies = ctx.System.State.policies;
        policies.RemoveAll(p => p.id == id);
        if (e.Node.GetString("action", "start") == "stop") return;
        int days = (int)Math.Round(ctx.Number(e.Node.Get("days")));
        int day = Today;
        policies.Add(new ManagerPolicy
        {
            id = id,
            situation = ctx.Situation != null ? ctx.Situation.Id : "",
            costDay = Math.Max(0, ctx.Amount(e.Node.Get("cost_day"))),
            costEmployee = Math.Max(0, ctx.Amount(e.Node.Get("cost_employee"))),
            happiness = Mathf.Clamp((float)ctx.Number(e.Node.Get("happiness")), -3f, 3f),
            trust = Mathf.Clamp((float)ctx.Number(e.Node.Get("trust")), -3f, 3f),
            heat = Mathf.Clamp((float)ctx.Number(e.Node.Get("heat")), -5f, 5f),
            startDay = day,
            untilDay = days > 0 ? day + days - 1 : 0
        });
    }

    static void RevenueShare(ManagerApplyContext ctx, ManagerEffectDef e)
    {
        double pct = Math.Min(50, Math.Max(0, ctx.Number(e.Node.Get("pct"))));
        int days = (int)Math.Round(ctx.Number(e.Node.Get("days")));
        if (pct <= 0 || days <= 0) return;
        string id = e.Node.GetString("id", ctx.Situation != null ? ctx.Situation.Id : "share");
        int day = Today;
        ctx.System.State.shares.Add(new ManagerShare { id = id, pct = pct, startDay = day, untilDay = day + days - 1 });
    }

    static void Gamble(ManagerApplyContext ctx, ManagerEffectDef e)
    {
        CompanyManager company = Company;
        if (company == null || e.GambleOutcomes.Count == 0) return;
        double stake = ctx.Amount(e.Node.Get("stake"));
        stake = Math.Min(stake, Math.Max(0, company.companyValue - ManagerSystem.Reserve));
        if (!(stake > 0)) return;

        // The outcome, by its real odds
        double roll = UnityEngine.Random.value, total = 0;
        foreach (ManagerGambleOutcome o in e.GambleOutcomes) total += Math.Max(0, o.Chance);
        roll *= total;
        ManagerGambleOutcome outcome = e.GambleOutcomes[e.GambleOutcomes.Count - 1];
        foreach (ManagerGambleOutcome o in e.GambleOutcomes)
        {
            roll -= Math.Max(0, o.Chance);
            if (roll <= 0)
            {
                outcome = o;
                break;
            }
        }

        double valueBefore = company.companyValue;
        double payout = Math.Round(stake * Math.Max(0, outcome.Mult), 2);
        company.AddMoney(-stake);
        if (payout > 0) company.AddMoney(payout);
        ctx.Staked += stake;
        ctx.GambleNet += payout - stake;
        string detail = (ctx.Situation != null ? ctx.Situation.Id : "?") + "/" + (ctx.Choice != null ? ctx.Choice.Id : "?");
        GambleLedger.Report(GambleSource, stake, payout, valueBefore, detail);

        ctx.System.ApplyEffects(ctx, outcome.Effects);
        if (!string.IsNullOrEmpty(outcome.ResultKey)) ctx.Results.Add(outcome.ResultKey);
    }

    static void Stocks(ManagerApplyContext ctx, ManagerEffectDef e)
    {
        StockMarketManager market = StockMarketManager.Instance;
        if (market == null || market.stocks == null || market.stocks.Count == 0) return;
        double amount = Math.Max(0, ctx.Amount(e.Node.Get("amount")));
        Stock pick = null;
        switch (e.Node.GetString("pick", "steady"))
        {
            case "hot":
                foreach (Stock s in market.stocks) if (s != null && s.currentPrice > 0 && (pick == null || s.volatility > pick.volatility)) pick = s;
                break;
            case "random":
            {
                var usable = market.stocks.FindAll(s => s != null && s.currentPrice > 0);
                if (usable.Count > 0) pick = usable[UnityEngine.Random.Range(0, usable.Count)];
                break;
            }
            default:
                foreach (Stock s in market.stocks) if (s != null && s.currentPrice > 0 && (pick == null || s.volatility < pick.volatility)) pick = s;
                break;
        }
        if (pick == null) return;
        long shares = (long)Math.Floor(amount / (pick.currentPrice * (1.0 + market.FeeRate)));
        shares = Math.Min(shares, market.GetMaxAffordableShares(pick));
        if (shares <= 0) return;
        if (market.TryBuyStock(pick.symbol, shares, out double cost) == StockMarketManager.TradeResult.Success)
            ctx.Toasts.Add(ManagerText.Get("mgr.toast.stocks", ("shares", shares.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)),
                                           ("symbol", pick.symbol), ("amount", UIFormat.Money(cost))));
    }

    static void Followup(ManagerApplyContext ctx, ManagerEffectDef e)
    {
        double chance = e.Node.Has("chance") ? ctx.Number(e.Node.Get("chance"), 1) : 1;
        if (UnityEngine.Random.value >= chance) return;
        string situation = e.Node.GetString("situation");
        int days = (int)Math.Round(ctx.Number(e.Node.Get("days"), 1));
        List<ManagerTargetState> targets = ctx.Pending != null ? ctx.Pending.targets : new List<ManagerTargetState>();
        List<string> keep = e.Node.GetStrings("keep");
        if (keep.Count > 0) targets = targets.FindAll(t => keep.Contains(t.name));
        ctx.System.Schedule(situation, days, targets, ctx.Pending != null ? ctx.Pending.prms : null, ctx.Situation != null ? ctx.Situation.Id : "", false, 0);
    }

    static void Flag(ManagerApplyContext ctx, ManagerEffectDef e)
    {
        List<ManagerNamedDay> flags = ctx.System.State.flags;
        string clear = e.Node.GetString("clear");
        if (!string.IsNullOrEmpty(clear)) flags.RemoveAll(f => f.id == clear);
        string set = e.Node.GetString("set");
        if (string.IsNullOrEmpty(set)) return;
        int days = (int)Math.Round(ctx.Number(e.Node.Get("days")));
        flags.RemoveAll(f => f.id == set);
        flags.Add(new ManagerNamedDay { id = set, day = days > 0 ? Today + days - 1 : 0 });
    }

    static void ManagerSalary(ManagerApplyContext ctx, ManagerEffectDef e)
    {
        if (!ctx.System.HasManager) return;
        ManagerPerson m = ctx.System.Manager;
        double salary = m.salary;
        if (e.Node.Has("pct")) salary *= 1.0 + ctx.Number(e.Node.Get("pct")) / 100.0;
        if (e.Node.Has("to_expected")) salary = Math.Max(salary, ManagerSystem.ExpectedSalary(m) * ctx.Number(e.Node.Get("to_expected"), 1));
        salary = ManagerParams.RoundMoney(Math.Max(10000, salary));
        if (salary > m.salary) m.raises++;
        ctx.System.Log(m.name + "'s salary " + UIFormat.Money(m.salary) + " -> " + UIFormat.Money(salary) + " a year.");
        m.salary = salary;
    }

    static void BuildPerk(ManagerApplyContext ctx, ManagerEffectDef e)
    {
        string id = e.Node.GetString("id");
        float discount = Mathf.Clamp((float)ctx.Number(e.Node.Get("discount")), 0f, 50f);
        int days = (int)Math.Round(ctx.Number(e.Node.Get("days")));
        if (string.IsNullOrEmpty(id) || discount <= 0f || days <= 0) return;
        int uses = e.Node.Has("uses") ? (int)Math.Round(ctx.Number(e.Node.Get("uses"))) : 0;
        List<ManagerPerk> perks = ctx.System.State.perks;
        perks.RemoveAll(p => p.id == id);
        perks.Add(new ManagerPerk
        {
            id = id,
            category = e.Node.GetString("category", "any"),
            discount = discount,
            untilDay = Today + days - 1,
            usesLeft = uses > 0 ? uses : -1
        });
        ManagerBuildPerks.RaiseChanged();
        ctx.Toasts.Add(ManagerText.Get("mgr.toast.perk", ("discount", Mathf.RoundToInt(discount)), ("days", days)));
    }

    static void Delay(ManagerApplyContext ctx, ManagerEffectDef e)
    {
        if (ctx.Situation == null || ctx.Pending == null) return;
        int days = (int)Math.Round(ctx.Number(e.Node.Get("days"), 3));
        ctx.System.Schedule(ctx.Situation.Id, days, ctx.Pending.targets, ctx.Pending.prms, ctx.Situation.Id, true, ctx.Pending.variant);
        ctx.Delayed = true;
    }

    static void EndPolicy(ManagerApplyContext ctx, ManagerEffectDef e)
    {
        string id = e.Node.GetString("id");
        if (string.IsNullOrEmpty(id)) return;
        ManagerSaveState s = ctx.System.State;
        s.policies.RemoveAll(p => p.id == id);
        s.shares.RemoveAll(p => p.id == id);
        if (s.perks.RemoveAll(p => p.id == id) > 0) ManagerBuildPerks.RaiseChanged();
        if (s.rentals.RemoveAll(p => p.id == id) > 0) ctx.System.RentalsChanged();
    }
}
