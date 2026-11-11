using System;
using System.Collections.Generic;
using UnityEngine;

// The engine: what happens each day and on events, how a situation is picked, created, expired and followed up.
public partial class ManagerSystem
{
    readonly HashSet<string> triggers = new HashSet<string>();
    readonly Dictionary<string, int> triggerDays = new Dictionary<string, int>();
    bool capacityDirty;
    bool wasNearlyFull;
    bool demoRateLogged;   // Demo: the decision rate is logged once

    // ------------------------------------------------------------------
    // Events
    // ------------------------------------------------------------------

    void OnDayEnded(int newDay, double change)
    {
        if (!stateKnown) return;
        try
        {
            DayEnded(newDay);
        }
        catch (Exception e)
        {
            Debug.LogException(e, this);
        }
    }

    void OnEmployeeHired(ApplicantData hire)
    {
        if (!stateKnown) return;
        state.hiredDays.Add(Today);
        TrimList(state.hiredDays, 30);
        QueueTrigger("hire");
        capacityDirty = true;
    }

    void OnEmployeeFired(ApplicantData person, double paid)
    {
        if (!stateKnown) return;
        state.firedDays.Add(Today);
        TrimList(state.firedDays, 30);
        if (person != null) state.moods.RemoveAll(m => m.id == person.id);
        QueueTrigger("fire");
        capacityDirty = true;
        DropOrphans();
    }

    /// <summary>
    /// Decisions about someone who is no longer on staff (fired from the staff files, quit, poached) leave the desk quietly:
    /// nothing about them can be decided any more, and it isn't the player ignoring the manager.
    /// </summary>
    internal void DropOrphans()
    {
        if (!stateKnown || state.pending.Count == 0) return;
        Dirty();
        List<ManagerStaffInfo> people = Staff();
        bool dropped = false;
        foreach (ManagerPending p in state.pending.ToArray())
        {
            ManagerSituationDef def = library.Get(p.situation);
            if (def == null) continue;
            foreach (ManagerTargetState t in p.targets)
            {
                if (t.kind != "employee") continue;
                ManagerTargetDef target = def.Target(t.name);
                if (target != null && target.Optional) continue;
                if (people.Exists(s => s.id == t.employeeId)) continue;
                state.pending.Remove(p);
                dropped = true;
                Log("Decision " + def.Id + " left the desk: " + t.displayName + " is no longer on staff.");
                break;
            }
        }
        if (dropped) RaiseChanged();
    }

    void OnGambled(GambleRecord record)
    {
        if (!stateKnown || record.stake <= 0.0) return;
        state.gambles.Add(new ManagerGambleNote { day = Today, stake = record.stake, lost = record.AmountLost });
        state.gambles.RemoveAll(g => g.day < Today - 7);
        if (record.source == ManagerEffects.GambleSource) return;   // the manager's own bets don't start situations
        double profitBase = Observe().TryGetValue("profit_base", out double pb) ? pb : 300;
        if (record.StakeShare >= 0.2 || record.AmountLost >= profitBase * 3.0) QueueTrigger("big_gamble");
    }

    void OnCapacityChanged()
    {
        capacityDirty = true;
    }

    void OnTierChanged(int from, int to)
    {
        if (to > from) QueueTrigger("tier_up");
    }

    void QueueTrigger(string trigger)
    {
        if (stateKnown) triggers.Add(trigger);
    }

    /// <summary>Run queued event triggers (once per trigger per day), and notice the office filling up.</summary>
    void ProcessTriggers()
    {
        if (capacityDirty)
        {
            capacityDirty = false;
            Dirty();
            Dictionary<string, double> f = Observe();
            bool nearlyFull = f["capacity_limited"] > 0 && (f["full"] > 0 || f["capacity_pct"] >= 85);
            if (nearlyFull && !wasNearlyFull) triggers.Add("capacity");
            wasNearlyFull = nearlyFull;
        }
        if (triggers.Count == 0) return;
        if (!HasManager || (SaveSystem.Instance != null && SaveSystem.Instance.IsLoading))
        {
            triggers.Clear();
            return;
        }
        var list = new List<string>(triggers);
        triggers.Clear();
        int day = Today;
        foreach (string trigger in list)
        {
            if (triggerDays.TryGetValue(trigger, out int last) && last == day) continue;
            // Not every event brings something up (Engine.json "triggerChance"): one firing isn't always a lawsuit
            if (UnityEngine.Random.value >= Engine.TriggerChance(trigger)) continue;
            if (TryCreate(trigger)) triggerDays[trigger] = day;
        }
    }

    // ------------------------------------------------------------------
    // The day
    // ------------------------------------------------------------------

    void DayEnded(int newDay)
    {
        CompanyManager company = CompanyManager.Instance;
        if (company == null) return;
        Dirty();

        // What the manager keeps track of
        double profit = company.LastDayBreakdown.operatingResult;
        double avgBefore = Average(state.recentProfits);
        state.recentProfits.Add(profit);
        TrimList(state.recentProfits, 7);
        state.recentValues.Add(company.companyValue);
        TrimList(state.recentValues, 8);
        state.losingDays = profit < 0 ? state.losingDays + 1 : 0;
        state.gambles.RemoveAll(g => g.day < newDay - 7);
        state.firedDays.RemoveAll(d => d < newDay - 30);
        state.hiredDays.RemoveAll(d => d < newDay - 30);
        ExpireContracts(newDay);
        DriftMoods();

        switch (state.phase)
        {
            case ManagerPhase.Gap:
                if (newDay >= state.gapUntilDay) StartPicking("the job has been open since " + (state.gapUntilDay > 0 ? "a few days" : "a while"));
                break;
            case ManagerPhase.Active:
                DailySatisfaction(newDay);
                break;
        }

        // Decisions age (also while the job is open: the company's matters don't wait)
        DropOrphans();
        ExpireDue(newDay);

        if (!HasManager)
        {
            RaiseChanged();
            return;
        }

        // Scheduled follow-ups, then events of the day, then the daily roll
        RunFollowups(newDay);
        double profitBase = Math.Max(Math.Max(avgBefore, company.companyValue * 0.003), 300);
        if (state.recentProfits.Count >= 4 && profit > 0 && profit > avgBefore * 2.5 && profit > profitBase * 2.0) QueueTrigger("profit_spike");
        if (state.losingDays == 3) QueueTrigger("loss_streak");
        DailyRoll(newDay);
        RaiseChanged();
    }

    /// <summary>Policies, rentals, shares, perks and flags whose last day has passed end.</summary>
    void ExpireContracts(int newDay)
    {
        bool rentalsChanged = false;
        foreach (ManagerRental r in state.rentals.ToArray())
        {
            if (r.untilDay > 0 && r.untilDay < newDay)
            {
                state.rentals.Remove(r);
                rentalsChanged = true;
                GameNotifications.Show(ManagerText.Get("mgr.toast.rent_ended", ("seats", r.seats)), NotificationKind.Info);
            }
        }
        if (rentalsChanged) RentalsChanged();
        foreach (ManagerPolicy p in state.policies.ToArray())
            if (p.untilDay > 0 && p.untilDay < newDay) state.policies.Remove(p);
        foreach (ManagerShare s in state.shares.ToArray())
        {
            if (s.untilDay > 0 && s.untilDay < newDay)
            {
                state.shares.Remove(s);
                GameNotifications.Show(ManagerText.Get("mgr.toast.share_ended", ("paid", UIFormat.Money(s.paid))), NotificationKind.Info);
            }
        }
        int perksBefore = state.perks.Count;
        state.perks.RemoveAll(p => (p.untilDay > 0 && p.untilDay < newDay) || p.usesLeft == 0);
        if (state.perks.Count != perksBefore) ManagerBuildPerks.RaiseChanged();
        state.flags.RemoveAll(f => f.day > 0 && f.day < newDay);

        // Policies' daily heat (heat isn't a DailyEconomyHooks meter)
        HeatMeter heat = HeatMeter.Instance;
        if (heat != null)
        {
            float total = 0f;
            foreach (ManagerPolicy p in state.policies) if (RunsOn(p.startDay, p.untilDay, newDay - 1)) total += p.heat;
            if (Mathf.Abs(total) > 0.001f) heat.AddHeat(Mathf.Clamp(total, -10f, 10f), "company policies");
        }
    }

    // ------------------------------------------------------------------
    // Satisfaction
    // ------------------------------------------------------------------

    void DailySatisfaction(int newDay)
    {
        ManagerPerson m = state.manager;
        double ratio = m.salary / Math.Max(1.0, ExpectedSalary(m));
        float target = Mathf.Clamp((float)(50.0 + 125.0 * (ratio - 0.8)), 8f, 92f);
        float before = m.satisfaction;
        m.satisfaction += (target - m.satisfaction) * 0.05f;
        // A desk piling up with unanswered memos wears them down
        if (state.pending.Count >= 5) m.satisfaction -= 0.6f;
        m.satisfaction = Mathf.Clamp(m.satisfaction, 0f, 100f);

        if (m.satisfaction <= 5f) state.lowDays++;
        else state.lowDays = 0;
        if (state.lowDays >= 2)
        {
            Resign("fed up");
            return;
        }
        if (Mathf.Floor(before / 10f) != Mathf.Floor(m.satisfaction / 10f))
            Log(m.name + "'s satisfaction " + before.ToString("0") + " -> " + m.satisfaction.ToString("0") + " (pay " + (ratio * 100.0).ToString("0") + "% of what they expect).");
    }

    /// <summary>Change the manager's satisfaction (clamped to 0-100).</summary>
    internal void AddSatisfaction(float amount, string why)
    {
        if (!HasManager || amount == 0f) return;
        ManagerPerson m = state.manager;
        m.satisfaction = Mathf.Clamp(m.satisfaction + amount, 0f, 100f);
        Dirty();
        if (Mathf.Abs(amount) >= 5f) Log(m.name + " satisfaction " + (amount > 0 ? "+" : "") + amount.ToString("0.#") + " (" + why + ") = " + m.satisfaction.ToString("0") + ".");
    }

    // ------------------------------------------------------------------
    // Picking a situation
    // ------------------------------------------------------------------

    void DailyRoll(int day)
    {
        if (state.lastRollDay == day) return;
        state.lastRollDay = day;
        ManagerEngineSettings e = Engine;
        if (day - state.manager.hiredDay < e.firstRollDays) return;
        if (HasFlag("mgr_away")) return;
        double chance = e.dailyChance + e.chancePerTier * Math.Min(4, CurrentTier());
        if (state.pending.Count == 0) chance += e.emptyDeskBonus;
        if (state.pending.Count >= e.busyDeskAt) chance -= e.busyDeskPenalty;
        chance *= DemoMode.ManagerDecisionRate;   // Demo: fewer decisions (x1 outside the demo)
        if (DemoMode.Active && !demoRateLogged)
        {
            demoRateLogged = true;
            Log("Demo: daily decision chance x" + DemoMode.ManagerDecisionRate.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " (" + (chance * 100.0).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "% today).");
        }
        if (UnityEngine.Random.value >= chance) return;
        TryCreate("day");
    }

    /// <summary>A situation the trigger allows, eligible and weighted by the company's state; false when none fits.</summary>
    public bool TryCreate(string trigger)
    {
        if (!HasManager || library == null) return false;
        var eligible = new List<(ManagerSituationDef def, double weight, List<ManagerTargetState> targets)>();
        double total = 0;
        foreach (ManagerSituationDef def in library.Situations)
        {
            if (def.FollowupOnly || !def.HasTrigger(trigger)) continue;
            if (!Eligible(def, null, out List<ManagerTargetState> targets, out _)) continue;
            double weight = Weight(def, targets);
            if (weight <= 0) continue;
            eligible.Add((def, weight, targets));
            total += weight;
        }
        if (eligible.Count == 0) return false;
        double roll = UnityEngine.Random.value * total;
        var pick = eligible[eligible.Count - 1];
        foreach (var candidate in eligible)
        {
            roll -= candidate.weight;
            if (roll <= 0)
            {
                pick = candidate;
                break;
            }
        }
        // Applicants are only made for the situation that comes up (making one loads a portrait)
        if (!FillCandidates(pick.def, pick.targets)) return false;
        Create(pick.def, pick.targets, null, false, 0, trigger);
        return true;
    }

    /// <summary>
    /// Can this situation come up now? Checks days, tiers, cooldowns, once, not already waiting, the targets and the
    /// conditions. <paramref name="why"/> says what stopped it (for the debug list).
    /// </summary>
    public bool Eligible(ManagerSituationDef def, List<ManagerTargetState> carried, out List<ManagerTargetState> targets, out string why)
    {
        targets = null;
        why = null;
        int day = Today;
        int tier = CurrentTier();
        if (state.pending.Exists(p => p.situation == def.Id)) { why = "already on the desk"; return false; }
        if (day < def.MinDay) { why = "before day " + def.MinDay; return false; }
        if (def.MaxDay > 0 && day > def.MaxDay) { why = "after day " + def.MaxDay; return false; }
        if (tier < def.MinTier || tier > def.MaxTier) { why = "tier " + tier; return false; }
        if (HasManager && day - state.manager.hiredDay < def.MinManagerDays) { why = "manager too new"; return false; }
        if (def.Once && state.seen.Contains(def.Id)) { why = "once, already seen"; return false; }
        if (carried == null)
        {
            int cooldown = def.Cooldown >= 0 ? def.Cooldown : Engine.situationCooldown;
            int last = LastDay(state.cooldowns, def.Id);
            if (last > 0 && day - last < cooldown) { why = "cooldown (" + (cooldown - (day - last)) + " days)"; return false; }
            int catCooldown = def.CategoryCooldown >= 0 ? def.CategoryCooldown : library.CategoryCooldown(def.Category);
            int lastCat = LastDay(state.categoryDays, def.Category);
            if (lastCat > 0 && day - lastCat < catCooldown) { why = "category cooldown"; return false; }
        }
        if (!PickTargets(def, carried, false, out targets)) { why = "no target"; return false; }
        var facts = new ManagerFacts(this, Resolve(targets), null);
        foreach (ManagerCondition c in def.Conditions)
        {
            if (!c.Eval(facts))
            {
                why = "condition \"" + c.Source + "\"";
                return false;
            }
        }
        return true;
    }

    /// <summary>A situation's weight now: its own weight with its modifiers, times its category's pressure.</summary>
    public double Weight(ManagerSituationDef def, List<ManagerTargetState> targets)
    {
        var facts = new ManagerFacts(this, Resolve(targets), null);
        double weight = def.Weight;
        foreach (ManagerWeightMod mod in def.WeightMods)
            if (mod.If.Eval(facts)) weight = weight * mod.Mul + mod.Add;
        if (def.Category != null && library.Categories.TryGetValue(def.Category, out ManagerCategoryDef cat))
            foreach (ManagerWeightMod mod in cat.Pressure)
                if (mod.If.Eval(facts)) weight = weight * mod.Mul + mod.Add;
        return Math.Max(0, weight);
    }

    static int LastDay(List<ManagerNamedDay> list, string id)
    {
        if (string.IsNullOrEmpty(id)) return 0;
        foreach (ManagerNamedDay d in list) if (d.id == id) return d.day;
        return 0;
    }

    static void SetDay(List<ManagerNamedDay> list, string id, int day)
    {
        if (string.IsNullOrEmpty(id)) return;
        foreach (ManagerNamedDay d in list)
            if (d.id == id)
            {
                d.day = day;
                return;
            }
        list.Add(new ManagerNamedDay { id = id, day = day });
    }

    // ------------------------------------------------------------------
    // Creating a decision
    // ------------------------------------------------------------------

    /// <summary>
    /// Put a situation on the desk with these targets. <paramref name="carriedParams"/> (a follow-up's or a re-ask's) keep
    /// their values; the rest are worked out now. <paramref name="variant"/> 0 = pick a message variant.
    /// </summary>
    internal ManagerPending Create(ManagerSituationDef def, List<ManagerTargetState> targets, List<ManagerParamState> carriedParams, bool again, int variant, string why)
    {
        int day = Today;
        ManagerEngineSettings e = Engine;
        var pending = new ManagerPending
        {
            uid = state.nextUid++,
            situation = def.Id,
            createdDay = day,
            urgent = def.Urgent,
            interrupt = def.Urgent && def.Interrupt,
            personality = HasManager ? state.manager.personality : "",
            again = again,
            targets = targets ?? new List<ManagerTargetState>()
        };
        int expires = def.Expires > 0 ? def.Expires : (def.Urgent ? e.urgentExpiryDays : e.defaultExpiryDays);
        pending.expiresDay = day + Math.Max(1, expires);
        pending.variant = variant > 0 ? Mathf.Clamp(variant, 1, def.TextVariants) : UnityEngine.Random.Range(1, def.TextVariants + 1);

        // Params in the order they are written (a later one may use an earlier one)
        var values = new Dictionary<string, double>();
        var facts = new ManagerFacts(this, Resolve(pending.targets), values);
        foreach (ManagerParamDef p in def.Params)
        {
            ManagerParamState carried = carriedParams != null ? carriedParams.Find(x => x != null && x.name == p.Name) : null;
            double v = carried != null ? carried.value : ManagerParams.Evaluate(p.Spec, facts, values);
            values[p.Name] = v;
            pending.prms.Add(new ManagerParamState { name = p.Name, value = v, format = ManagerParams.FormatOf(p.Spec) });
        }

        // An unhappy manager sometimes keeps their advice to themselves
        if (HasManager && state.manager.satisfaction < 25f && !def.Personal) pending.withheld = UnityEngine.Random.value < 0.5f;

        // Room on the desk: the oldest non-urgent decision goes with its default
        while (state.pending.Count >= Math.Max(1, e.maxPending))
        {
            ManagerPending oldest = null;
            foreach (ManagerPending p in state.pending)
                if (!p.urgent && (oldest == null || p.createdDay < oldest.createdDay)) oldest = p;
            if (oldest == null) oldest = state.pending[0];
            Expire(oldest, "the desk was full");
        }

        state.pending.Add(pending);
        SetDay(state.cooldowns, def.Id, day);
        SetDay(state.categoryDays, def.Category, day);
        if (!state.seen.Contains(def.Id)) state.seen.Add(def.Id);
        state.stats.created++;
        Dirty();
        Log("New decision: " + def.Id + " (" + why + (pending.urgent ? ", urgent" : "") + ", " + DescribeTargets(pending) + ").");
        if (HasManager && !pending.interrupt)
            GameNotifications.Show(ManagerText.Get("mgr.toast.new", ("manager", state.manager.name), ("title", Title(def, pending))), NotificationKind.Info);
        RaiseDecisionAdded(pending);
        RaiseChanged();
        return pending;
    }

    static string DescribeTargets(ManagerPending p)
    {
        if (p.targets.Count == 0) return "no targets";
        var parts = new List<string>();
        foreach (ManagerTargetState t in p.targets) parts.Add(t.name + "=" + t.displayName);
        return string.Join(", ", parts);
    }

    /// <summary>Put a situation on the desk now, if it can come up (debug; ignores cooldowns when <paramref name="force"/>).</summary>
    public bool Force(string situationId, bool force, out string why)
    {
        why = null;
        ManagerSituationDef def = library != null ? library.Get(situationId) : null;
        if (def == null) { why = "no such situation"; return false; }
        if (!HasManager) { why = "no manager"; return false; }
        if (force)
        {
            if (!PickTargets(def, null, true, out List<ManagerTargetState> targets)) { why = "no target"; return false; }
            state.pending.RemoveAll(p => p.situation == def.Id);
            Create(def, targets, null, false, 0, "forced");
            return true;
        }
        if (!Eligible(def, null, out List<ManagerTargetState> t, out why)) return false;
        if (!FillCandidates(def, t)) { why = "couldn't make an applicant"; return false; }
        Create(def, t, null, false, 0, "debug");
        return true;
    }

    // ------------------------------------------------------------------
    // Follow-ups and expiry
    // ------------------------------------------------------------------

    /// <summary>Schedule a situation for a later day with these targets and params.</summary>
    internal void Schedule(string situation, int days, List<ManagerTargetState> targets, List<ManagerParamState> prms, string from, bool again, int variant)
    {
        state.followups.Add(new ManagerFollowup
        {
            situation = situation,
            dueDay = Today + Math.Max(1, days),
            from = from ?? "",
            again = again,
            variant = variant,
            targets = targets != null ? new List<ManagerTargetState>(targets) : new List<ManagerTargetState>(),
            prms = prms != null ? new List<ManagerParamState>(prms) : new List<ManagerParamState>()
        });
    }

    void RunFollowups(int day)
    {
        foreach (ManagerFollowup f in state.followups.ToArray())
        {
            if (f.dueDay > day) continue;
            state.followups.Remove(f);
            ManagerSituationDef def = library.Get(f.situation);
            if (def == null) continue;
            if (state.pending.Exists(p => p.situation == def.Id)) continue;
            // A re-ask comes back unless its people are gone; a follow-up needs its conditions too
            if (!PickTargets(def, f.targets, true, out List<ManagerTargetState> targets))
            {
                Log("Follow-up " + def.Id + " dropped: its people are gone.");
                continue;
            }
            if (!f.again)
            {
                var facts = new ManagerFacts(this, Resolve(targets), null);
                if (!ManagerCondition.All(def.Conditions, facts))
                {
                    Log("Follow-up " + def.Id + " dropped: its conditions no longer hold.");
                    continue;
                }
            }
            Create(def, targets, f.prms, f.again, f.again ? f.variant : 0, f.again ? "asked again" : "follow-up of " + f.from);
        }
    }

    void ExpireDue(int day)
    {
        foreach (ManagerPending p in state.pending.ToArray())
            if (day > p.expiresDay) Expire(p, "no answer in time");
    }

    /// <summary>A decision nobody answered: the manager's goodwill drops and the default happens (if the situation has one).</summary>
    internal void Expire(ManagerPending p, string why)
    {
        if (!state.pending.Remove(p)) return;
        ManagerSituationDef def = library.Get(p.situation);
        state.stats.expired++;
        AddSatisfaction(p.urgent ? -4f : -2f, "decision expired");
        if (def == null) return;
        ManagerChoiceDef fallback = def.Choice(def.DefaultChoice);
        string title = Title(def, p);
        if (fallback != null && ChoiceAvailable(p, def, fallback, out _))
        {
            var ctx = NewContext(p, def, fallback);
            ApplyChoice(ctx, fallback);
            ApplyEffects(ctx, def.OnExpire);
            FinishContext(ctx);
            Remember(def, fallback, "expired", p);
            GameNotifications.Show(ManagerText.Get("mgr.toast.expired.default", ("manager", ManagerName), ("title", title), ("choice", ChoiceText(def, fallback, p))), NotificationKind.Info);
        }
        else
        {
            var ctx = NewContext(p, def, null);
            ApplyEffects(ctx, def.OnExpire);
            FinishContext(ctx);
            Remember(def, null, "unanswered", p);
            GameNotifications.Show(ManagerText.Get("mgr.toast.expired", ("title", title)), NotificationKind.Info);
        }
        Log("Decision " + def.Id + " expired (" + why + ")" + (fallback != null ? ": default " + fallback.Id : "") + ".");
        Dirty();
        RaiseChanged();
    }
}
