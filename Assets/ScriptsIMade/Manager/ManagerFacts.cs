using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>One employee as the manager sees them (per-frame snapshot).</summary>
public sealed class ManagerStaffInfo
{
    public ApplicantData person;
    public string id;
    public double profit;       // per day (last day, or the projection before their first)
    public double salary;       // per year
    public float mood;          // the manager's notes, 50 = neutral
    public int days;            // days on staff
    public bool worked;         // at least one day booked
    public double Underpaid => salary > 0 ? profit / (salary / 365.0) : 0;
}

/// <summary>A decision's target, resolved to a person (an employee on staff, or a candidate).</summary>
public sealed class ManagerResolvedTarget
{
    public string name;
    public bool candidate;
    public ApplicantData person;
    public ManagerStaffInfo info;   // employees only
}

/// <summary>
/// The company as the manager sees it, for conditions (IManagerFacts) and params (IManagerParamSource): the observed facts
/// (ManagerSystem.Observe) plus, for a decision, its targets and params.
/// </summary>
public sealed class ManagerFacts : IManagerFacts, IManagerParamSource
{
    readonly ManagerSystem system;
    readonly Dictionary<string, double> facts;
    readonly List<ManagerResolvedTarget> targets;
    readonly IDictionary<string, double> prms;

    public ManagerFacts(ManagerSystem system, List<ManagerResolvedTarget> targets, IDictionary<string, double> prms)
    {
        this.system = system;
        facts = system.Observe();
        this.targets = targets;
        this.prms = prms;
    }

    public ManagerResolvedTarget Target(string name)
    {
        if (targets == null || string.IsNullOrEmpty(name)) return null;
        foreach (ManagerResolvedTarget t in targets)
            if (t.name == name) return t;
        return null;
    }

    public bool TryGetNumber(string name, out double value)
    {
        value = 0;
        if (string.IsNullOrEmpty(name)) return false;
        int dot = name.IndexOf('.');
        if (dot > 0)
        {
            ManagerResolvedTarget t = Target(name.Substring(0, dot));
            string field = name.Substring(dot + 1);
            if (field == "exists")
            {
                value = t != null && t.person != null ? 1 : 0;
                return true;
            }
            if (t == null || t.person == null) return false;
            value = TargetNumber(t, field);
            return true;
        }
        if (prms != null && prms.TryGetValue(name, out value)) return true;
        return facts.TryGetValue(name, out value);
    }

    static double TargetNumber(ManagerResolvedTarget t, string field)
    {
        ApplicantData p = t.person;
        switch (field)
        {
            case "profit": return t.info != null ? t.info.profit : (StaffLedger.Instance != null ? StaffLedger.Instance.ProjectDailyProfit(p) : 0);
            case "salary": return p.salary;
            case "mood": return t.info != null ? t.info.mood : 50;
            case "days": return t.info != null ? t.info.days : 0;
            case "iq": return p.IQ;
            case "age": return p.age;
            case "exp": return p.experienceYears;
            case "golden": return p.isGolden ? 1 : 0;
            case "underpaid": return t.info != null ? t.info.Underpaid : 0;
        }
        return 0;
    }

    public bool Test(string kind, string arg)
    {
        return system.TestPredicate(kind, arg, this);
    }

    // IManagerParamSource
    public double Value => Get("value");
    public double ProfitBase => Get("profit_base");
    public double Payroll => Get("payroll");
    public int CashTier => (int)Get("cash_tier");

    public bool TryGetTargetSalary(string target, out double yearly)
    {
        yearly = 0;
        ManagerResolvedTarget t = Target(target);
        if (t == null || t.person == null) return false;
        yearly = t.person.salary;
        return true;
    }

    public bool TryGetFact(string fact, out double value) => facts.TryGetValue(fact, out value);

    public double Random01() => UnityEngine.Random.value;

    double Get(string name) => facts.TryGetValue(name, out double v) ? v : 0;
}

public partial class ManagerSystem
{
    // ------------------------------------------------------------------
    // Observing the company (cached for a frame)
    // ------------------------------------------------------------------

    readonly Dictionary<string, double> observed = new Dictionary<string, double>(StringComparer.Ordinal);
    int observedFrame = -1;
    readonly List<ManagerStaffInfo> staff = new List<ManagerStaffInfo>();
    int staffFrame = -1;

    /// <summary>Forget the cached facts (after effects changed the company).</summary>
    internal void Dirty()
    {
        observedFrame = -1;
        staffFrame = -1;
        argsFrame = -1;
    }

    /// <summary>Every employee with their profit, salary, mood and days (cached for the frame).</summary>
    public List<ManagerStaffInfo> Staff()
    {
        if (staffFrame == Time.frameCount) return staff;
        staffFrame = Time.frameCount;
        staff.Clear();
        CompanyManager company = CompanyManager.Instance;
        if (company == null || company.employees == null) return staff;
        StaffLedger ledger = StaffLedger.Instance;
        foreach (ApplicantData e in company.employees)
        {
            if (e == null) continue;
            var info = new ManagerStaffInfo { person = e, id = e.EnsureId(), salary = e.salary, mood = MoodOf(e) };
            if (ledger != null)
            {
                StaffStats stats = ledger.GetStats(e);
                info.profit = stats.profitPerDay;
                info.days = stats.daysEmployed;
                info.worked = stats.hasWorkedADay;
            }
            else
            {
                info.profit = company.ProjectEmployeeDay(e, false).Profit;
            }
            staff.Add(info);
        }
        return staff;
    }

    /// <summary>The observed company facts (ManagerSchema.Facts), cached for the frame.</summary>
    public Dictionary<string, double> Observe()
    {
        if (observedFrame == Time.frameCount) return observed;
        observedFrame = Time.frameCount;
        observed.Clear();
        CompanyManager company = CompanyManager.Instance;
        double value = company != null ? Math.Max(0, company.companyValue) : 0;
        int day = company != null ? company.currentDay : 0;
        void Set(string name, double v) => observed[name] = double.IsNaN(v) || double.IsInfinity(v) ? 0 : v;

        Set("value", value);
        Set("cash_tier", StatusTier.ForValue(value));
        Set("tier", CurrentTier());
        double profit = company != null ? company.LastDayBreakdown.operatingResult : 0;
        Set("profit", profit);
        double avg = Average(state.recentProfits);
        Set("profit_avg", avg);
        Set("profit_base", Math.Max(Math.Max(avg, value * 0.003), 300));
        Set("growth", Growth());
        Set("losing_days", state.losingDays);
        Set("day", day);
        Set("happiness", company != null ? company.employeeHappiness : 50);
        Set("trust", company != null ? company.investorTrust : 50);
        HeatMeter heat = HeatMeter.Instance;
        Set("heat", heat != null ? heat.Heat : 0);
        Set("heat_level", heat != null ? heat.Level : 0);
        Set("gamble_frozen", heat != null && heat.IsLockedOut ? 1 : 0);

        List<ManagerStaffInfo> people = Staff();
        double payroll = 0, salaries = 0, top = 0, low = 0;
        int unhappy = 0, golden = 0;
        bool first = true;
        float tax = SkillTreeManager.Instance != null ? Mathf.Clamp01(SkillTreeManager.Instance.GetTaxOptimization()) : 0f;
        foreach (ManagerStaffInfo s in people)
        {
            payroll += CompanyManager.DailySalary(s.person, tax, 1f);
            salaries += s.salary;
            if (first) { top = low = s.profit; first = false; }
            else { top = Math.Max(top, s.profit); low = Math.Min(low, s.profit); }
            if (s.mood < 35f) unhappy++;
            if (s.person.isGolden) golden++;
        }
        Set("employees", people.Count);
        Set("payroll", payroll);
        Set("avg_salary", people.Count > 0 ? salaries / people.Count : 0);
        Set("top_profit", top);
        Set("low_profit", low);
        Set("unhappy_staff", unhappy);
        Set("golden_staff", golden);

        bool limited = OfficeCapacity.IsLimited;
        int capacity = OfficeCapacity.Capacity;
        Set("capacity_limited", limited ? 1 : 0);
        Set("capacity", limited ? capacity : 9999);
        Set("free", limited ? OfficeCapacity.Free : 9999);
        Set("capacity_pct", limited && capacity > 0 ? 100.0 * people.Count / capacity : (limited ? 100 : 0));
        Set("full", limited && OfficeCapacity.IsFull ? 1 : 0);
        Set("rented_seats", RentedSeats);

        SkillTreeManager skills = SkillTreeManager.Instance;
        Set("skill_points", skills != null ? skills.skillPoints : 0);
        int unlocked = 0;
        if (skills != null && skills.allSkills != null)
            foreach (Skill s in skills.allSkills) if (s != null && s.unlocked) unlocked++;
        Set("skills", unlocked);

        Set("debt", BankLoan.CurrentDebt);
        StockMarketManager market = StockMarketManager.Instance;
        Set("stocks_open", market != null && FeatureGate.IsUnlocked(Feature.Stocks) && StockMarketManager.IsTradingUnlocked() ? 1 : 0);
        Set("stocks_value", market != null ? market.GetTotalPortfolioValue() : 0);
        Set("build_open", BuildModeCamera.Instance != null && FeatureGate.IsUnlocked(Feature.Build) ? 1 : 0);
        BoardMeetings board = BoardMeetings.Instance;
        Set("board_days", board != null && FeatureGate.IsUnlocked(Feature.BoardMeetings) ? board.DaysUntilMeeting : 99);
        Set("board_target", board != null && board.HasTarget ? 1 : 0);

        double lost = 0, staked = 0;
        int week = 0;
        foreach (ManagerGambleNote g in state.gambles)
        {
            if (g.day >= day - 3) { lost += g.lost; staked += g.stake; }
            if (g.day >= day - 7) week++;
        }
        Set("gamble_loss_recent", lost);
        Set("gamble_stake_recent", staked);
        Set("gambles_week", week);
        Set("fired_recent", CountSince(state.firedDays, day - 10));
        Set("hired_recent", CountSince(state.hiredDays, day - 5));
        Set("policies", ActivePolicies());
        Set("pending", state.pending.Count);

        ManagerPerson m = state.manager;
        bool active = HasManager;
        Set("mgr_satisfaction", active ? m.satisfaction : 50);
        Set("mgr_days", active ? Math.Max(0, day - m.hiredDay) : 0);
        Set("mgr_salary", active ? m.salary : 0);
        Set("mgr_pay_ratio", active ? 100.0 * m.salary / Math.Max(1, ExpectedSalary(m)) : 100);
        Set("mgr_expected", active ? ExpectedSalary(m) : 0);
        Set("mgr_count", state.managersHired);
        Set("mgr_competence", active ? 100.0 * m.competence : 100);
        return observed;
    }

    static double Average(List<double> values)
    {
        if (values == null || values.Count == 0) return 0;
        double sum = 0;
        foreach (double v in values) sum += v;
        return sum / values.Count;
    }

    double Growth()
    {
        List<double> values = state.recentValues;
        if (values.Count < 2) return 0;
        double first = values[0];
        double now = CompanyManager.Instance != null ? CompanyManager.Instance.companyValue : values[values.Count - 1];
        return first > 1 ? (now - first) / first * 100.0 : 0;
    }

    static int CountSince(List<int> days, int since)
    {
        int n = 0;
        foreach (int d in days) if (d >= since) n++;
        return n;
    }

    int ActivePolicies()
    {
        int day = Today, n = 0;
        foreach (ManagerPolicy p in state.policies) if (p.untilDay <= 0 || day <= p.untilDay) n++;
        return n;
    }

    /// <summary>The highest status tier reached (StatusLadder), else the tier of the current value.</summary>
    public static int CurrentTier()
    {
        StatusLadder ladder = StatusLadder.Instance;
        if (ladder != null && ladder.IsTierKnown) return ladder.CurrentTier;
        return StatusTier.Current;
    }

    /// <summary>What a manager expects to earn per year now: their base, grown with the company's tier and their time here.</summary>
    public static double ExpectedSalary(ManagerPerson m)
    {
        if (m == null) return 0;
        int days = Math.Max(0, Today - m.hiredDay);
        double tenure = 1.0 + Math.Min(0.5, 0.004 * days);
        return Math.Max(1, m.expectedBase) * ManagerCandidates.TierPay(CurrentTier()) * tenure;
    }

    // ------------------------------------------------------------------
    // Predicates (flag:, policy:, ...)
    // ------------------------------------------------------------------

    internal bool TestPredicate(string kind, string arg, ManagerFacts facts)
    {
        int day = Today;
        switch (kind)
        {
            case "flag": return HasFlag(arg);
            case "policy":
                foreach (ManagerPolicy p in state.policies) if (p.id == arg && (p.untilDay <= 0 || day <= p.untilDay)) return true;
                return false;
            case "rental":
                foreach (ManagerRental r in state.rentals) if ((string.IsNullOrEmpty(arg) || r.id == arg) && (r.untilDay <= 0 || day <= r.untilDay)) return true;
                return false;
            case "perk":
                foreach (ManagerPerk p in state.perks) if (p.id == arg && (p.untilDay <= 0 || day <= p.untilDay) && p.usesLeft != 0) return true;
                return false;
            case "share":
                foreach (ManagerShare s in state.shares) if ((string.IsNullOrEmpty(arg) || s.id == arg) && (s.untilDay <= 0 || day <= s.untilDay)) return true;
                return false;
            case "personality": return HasManager && state.manager.personality == arg;
            case "feature":
                return Enum.TryParse(arg, out Feature feature) && Enum.IsDefined(typeof(Feature), feature) && FeatureGate.IsUnlocked(feature);
            case "seen": return state.seen.Contains(arg);
            case "chose": return state.chosen.Contains(arg);
            case "target":
            {
                ManagerResolvedTarget t = facts != null ? facts.Target(arg) : null;
                return t != null && t.person != null;
            }
        }
        return false;
    }

    public bool HasFlag(string flag)
    {
        int day = Today;
        foreach (ManagerNamedDay f in state.flags)
            if (f.id == flag && (f.day <= 0 || day <= f.day)) return true;
        return false;
    }

    // ------------------------------------------------------------------
    // Moods (the manager's notes on each employee, by ApplicantData.id)
    // ------------------------------------------------------------------

    public float MoodOf(ApplicantData person)
    {
        if (person == null || string.IsNullOrEmpty(person.id)) return 50f;
        foreach (ManagerMood m in state.moods) if (m.id == person.id) return m.mood;
        return 50f;
    }

    internal void AddMood(ApplicantData person, float amount)
    {
        if (person == null) return;
        string id = person.EnsureId();
        ManagerMood mood = state.moods.Find(m => m.id == id);
        if (mood == null)
        {
            mood = new ManagerMood { id = id, mood = 50f };
            state.moods.Add(mood);
        }
        mood.mood = Mathf.Clamp(mood.mood + amount, 0f, 100f);
        Dirty();
    }

    /// <summary>Moods drift back toward 50 and people who left are forgotten.</summary>
    void DriftMoods()
    {
        CompanyManager company = CompanyManager.Instance;
        var onStaff = new HashSet<string>();
        if (company != null && company.employees != null)
            foreach (ApplicantData e in company.employees) if (e != null && !string.IsNullOrEmpty(e.id)) onStaff.Add(e.id);
        state.moods.RemoveAll(m => !onStaff.Contains(m.id));
        foreach (ManagerMood m in state.moods) m.mood += (50f - m.mood) * 0.04f;
        state.moods.RemoveAll(m => Mathf.Abs(m.mood - 50f) < 0.5f);
    }

    // ------------------------------------------------------------------
    // Targets
    // ------------------------------------------------------------------

    /// <summary>Resolve saved targets to people (employees who left resolve to null).</summary>
    internal List<ManagerResolvedTarget> Resolve(List<ManagerTargetState> saved)
    {
        var list = new List<ManagerResolvedTarget>();
        if (saved == null) return list;
        List<ManagerStaffInfo> people = Staff();
        foreach (ManagerTargetState t in saved)
        {
            if (t == null) continue;
            var r = new ManagerResolvedTarget { name = t.name, candidate = t.kind == "candidate" };
            if (r.candidate)
            {
                r.person = t.candidate != null && !string.IsNullOrEmpty(t.candidate.name) ? t.candidate.ToApplicantData(false) : null;
            }
            else
            {
                ManagerStaffInfo info = people.Find(s => s.id == t.employeeId);
                r.info = info;
                r.person = info != null ? info.person : null;
            }
            list.Add(r);
        }
        return list;
    }

    /// <summary>
    /// Pick targets for a new situation by its rules (carried ones from a follow-up are kept when still valid). False when a
    /// required target can't be found. Without <paramref name="makeCandidates"/> candidate targets stay empty placeholders
    /// (FillCandidates makes them for the situation that comes up: making an applicant loads a portrait), so conditions
    /// can't test candidate numbers.
    /// </summary>
    bool PickTargets(ManagerSituationDef def, List<ManagerTargetState> carried, bool makeCandidates, out List<ManagerTargetState> picked)
    {
        picked = new List<ManagerTargetState>();
        var used = new HashSet<string>();
        // Nobody is the subject of two decisions at once
        foreach (ManagerPending p in state.pending)
            foreach (ManagerTargetState t in p.targets)
                if (t.kind == "employee" && !string.IsNullOrEmpty(t.employeeId)) used.Add(t.employeeId);
        List<ManagerStaffInfo> people = Staff();

        foreach (ManagerTargetDef target in def.Targets)
        {
            ManagerTargetState keep = carried != null ? carried.Find(t => t != null && t.name == target.Name) : null;
            if (keep != null)
            {
                bool valid = keep.kind == "candidate"
                    ? keep.candidate != null && !string.IsNullOrEmpty(keep.candidate.name)
                    : people.Exists(s => s.id == keep.employeeId);
                if (valid)
                {
                    picked.Add(keep);
                    if (keep.kind == "employee") used.Add(keep.employeeId);
                    continue;
                }
                if (!target.Optional) return false;
                continue;
            }

            if (!ManagerSchema.TargetRules.TryGetValue(target.Rule, out ManagerSchema.TargetRule rule)) return false;
            if (rule.Kind == ManagerSchema.TargetKind.Candidate)
            {
                if (!makeCandidates)
                {
                    picked.Add(new ManagerTargetState { name = target.Name, kind = "candidate" });
                    continue;
                }
                ApplicantData candidate = ManagerCandidates.Applicant(target.Rule);
                if (candidate == null)
                {
                    if (!target.Optional) return false;
                    continue;
                }
                picked.Add(new ManagerTargetState { name = target.Name, kind = "candidate", displayName = candidate.name, candidate = new EmployeeSaveData(candidate) });
                continue;
            }

            ManagerStaffInfo who = PickEmployee(target.Rule, people, used);
            if (who == null)
            {
                if (!target.Optional) return false;
                continue;
            }
            used.Add(who.id);
            picked.Add(new ManagerTargetState { name = target.Name, kind = "employee", employeeId = who.id, displayName = StaffLedger.DisplayName(who.person) });
        }
        return true;
    }

    /// <summary>Make the applicants of candidate placeholders (PickTargets without makeCandidates). False if one can't be made.</summary>
    bool FillCandidates(ManagerSituationDef def, List<ManagerTargetState> targets)
    {
        if (targets == null) return true;
        foreach (ManagerTargetState t in targets)
        {
            if (t.kind != "candidate" || (t.candidate != null && !string.IsNullOrEmpty(t.candidate.name))) continue;
            ManagerTargetDef target = def.Target(t.name);
            ApplicantData candidate = target != null ? ManagerCandidates.Applicant(target.Rule) : null;
            if (candidate == null) return false;
            t.candidate = new EmployeeSaveData(candidate);
            t.displayName = candidate.name;
        }
        return true;
    }

    static ManagerStaffInfo PickEmployee(string rule, List<ManagerStaffInfo> people, HashSet<string> used)
    {
        var pool = people.FindAll(s => !used.Contains(s.id));
        if (pool.Count == 0) return null;
        ManagerStaffInfo best = null;
        switch (rule)
        {
            case "top_performer":
                foreach (ManagerStaffInfo s in pool) if (best == null || s.profit > best.profit) best = s;
                return best;
            case "low_performer":
                foreach (ManagerStaffInfo s in pool) if (best == null || s.profit < best.profit) best = s;
                return best;
            case "most_underpaid":
                foreach (ManagerStaffInfo s in pool) if (s.profit > 0 && (best == null || s.Underpaid > best.Underpaid)) best = s;
                return best;
            case "highest_paid":
                foreach (ManagerStaffInfo s in pool) if (best == null || s.salary > best.salary) best = s;
                return best;
            case "random_employee":
                return pool[UnityEngine.Random.Range(0, pool.Count)];
            case "newest":
                foreach (ManagerStaffInfo s in pool) if (best == null || s.days < best.days) best = s;
                return best;
            case "longest_serving":
                foreach (ManagerStaffInfo s in pool) if (best == null || s.days > best.days) best = s;
                return best;
            case "unhappy":
                foreach (ManagerStaffInfo s in pool) if (s.mood < 40f && (best == null || s.mood < best.mood)) best = s;
                return best;
            case "golden":
            {
                var golden = pool.FindAll(s => s.person.isGolden);
                return golden.Count > 0 ? golden[UnityEngine.Random.Range(0, golden.Count)] : null;
            }
            case "high_iq":
                foreach (ManagerStaffInfo s in pool) if (s.person.IQ >= 120 && (best == null || s.person.IQ > best.person.IQ)) best = s;
                return best;
        }
        return null;
    }
}
