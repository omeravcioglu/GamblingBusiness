using System;
using System.Collections.Generic;
using UnityEngine;

// The departments' side of the floor: their workers (who walk out, grab bills, carry them back to their crate) and their
// interference (firewalls, rate limits, shoves, pop-ups, yoinks, magnets, confetti), plus the hazards those leave.
public sealed partial class CashFloorSim
{
    public enum AgentState
    {
        Idle,
        Seek,
        Grab,
        Return,
        Deposit,
        Stunned,
        Telegraph,
        Dash,
        Wander
    }

    /// <summary>A department worker on the floor.</summary>
    public sealed class Agent
    {
        public int id;              // index in the agents list
        public int dept;
        public int index;           // 0..2 within the department
        public bool active;
        public Vector2 pos, vel, home;
        public float facing = 1f;
        public AgentState state;
        public float timer;
        public int target = -1;
        public readonly List<int> carry = new List<int>();
        public float stun;
        public float walkPhase;
        public float interfereCooldown;
        public float telegraph;
        public CashInterference pending;
        public CashInterference dashMode;
        public Vector2 wanderTo;
        public float bob;            // squash when grabbing / depositing (the view reads it)
        public float writtenUp;      // seconds left sitting out after the CEO's Written up stamp (the view shows the slip)
    }

    public enum HazardKind
    {
        Firewall,
        Popup,
        Magnet,
        Paperweight,
        RateLimit
    }

    /// <summary>Something a department (or the CEO's paperweight) left on the floor for a while.</summary>
    public sealed class Hazard
    {
        public HazardKind kind;
        public Rect rect;            // Firewall, Popup
        public Vector2 center;       // Magnet, Paperweight
        public float radius;
        public float time;           // seconds left
        public float duration;
        public float started;
        public int dept;
    }

    public readonly List<Agent> agents = new List<Agent>();
    public readonly List<Hazard> hazards = new List<Hazard>();
    readonly int[] deptLevel = new int[CashLedger.DeptCount];
    float globalInterferenceCooldown = 4f;
    const int AgentsPerDept = 3;

    public int DeptLevel(int dept) => dept >= 0 && dept < deptLevel.Length ? deptLevel[dept] : 0;

    public bool RateLimited => FindHazard(HazardKind.RateLimit) != null;

    void InitAgents()
    {
        agents.Clear();
        for (int d = 0; d < CashLedger.DeptCount; d++)
        {
            for (int k = 0; k < AgentsPerDept; k++)
            {
                var a = new Agent { id = agents.Count, dept = d, index = k, active = false };
                a.home = HomeFor(d, k);
                a.pos = a.home;
                agents.Add(a);
            }
        }
    }

    Vector2 HomeFor(int dept, int index)
    {
        Vector2 c = DeptCenter[dept];
        Vector2 inward = dept == 0 ? new Vector2(1f, 0f) : dept == 1 ? new Vector2(-1f, 0f) : new Vector2(0.6f, -0.8f).normalized;
        Vector2 side = new Vector2(-inward.y, inward.x);
        return c + inward * 110f + side * ((index - 1) * 46f);
    }

    /// <summary>Set a department's level (0 = not on the floor yet): workers come and go, and their pace follows.</summary>
    public void SetDeptLevel(int dept, int level)
    {
        if (dept < 0 || dept >= deptLevel.Length) return;
        level = Mathf.Clamp(level, 0, CashRules.MaxDeptLevel);
        deptLevel[dept] = level;
        int wanted = CashRules.AgentsAt(level);
        foreach (Agent a in agents)
        {
            if (a.dept != dept) continue;
            bool on = a.index < wanted;
            if (on == a.active) continue;
            a.active = on;
            if (on)
            {
                a.pos = DeptCenter[dept];
                a.vel = Vector2.zero;
                a.state = AgentState.Idle;
                a.timer = 0.3f + a.index * 0.4f;
                a.interfereCooldown = 6f + a.index * 3f;
            }
            else
            {
                DropCarried(a, 60f);
                a.target = -1;
            }
        }
    }

    // ------------------------------------------------------------------
    // Workers
    // ------------------------------------------------------------------

    void StepAgents(float dt)
    {
        globalInterferenceCooldown = Mathf.Max(0f, globalInterferenceCooldown - dt);
        foreach (Agent a in agents)
        {
            if (!a.active) continue;
            a.bob = Mathf.Max(0f, a.bob - dt * 4f);
            a.interfereCooldown = Mathf.Max(0f, a.interfereCooldown - dt);
            a.writtenUp = Mathf.Max(0f, a.writtenUp - dt);
            StepAgent(a, dt);
        }
        SeparateAgents(dt);
    }

    void StepAgent(Agent a, float dt)
    {
        CashDept dept = (CashDept)a.dept;
        int level = Mathf.Max(1, deptLevel[a.dept]);
        float speed = CashRules.AgentSpeed(dept, level) * DeptSpeed[a.dept] * (Visible ? 1f : AwaySpeedFactor);
        int capacity = CashRules.AgentCapacity(dept, level);

        if (a.stun > 0f)
        {
            a.stun -= dt;
            a.state = AgentState.Stunned;
            a.vel *= Mathf.Exp(-dt * 5f);
            Move(a, dt);
            if (a.stun <= 0f)
            {
                a.stun = 0f;
                a.state = AgentState.Idle;
            }
            return;
        }

        // Interfere when the CEO is on the floor (telegraphed with a "!" first)
        if (a.state != AgentState.Telegraph && a.state != AgentState.Dash && a.state != AgentState.Deposit && Visible)
            ConsiderInterference(a, level);

        switch (a.state)
        {
            case AgentState.Telegraph:
                a.vel *= Mathf.Exp(-dt * 8f);
                a.telegraph -= dt;
                if (a.telegraph <= 0f) Interfere(a, level);
                break;

            case AgentState.Dash:
                StepDash(a, dt, speed);
                break;

            case AgentState.Idle:
                a.timer -= dt;
                if (a.timer > 0f) { Steer(a, a.pos, speed, dt); break; }
                if (a.carry.Count >= capacity) { a.state = AgentState.Return; break; }
                int pick = PickTarget(a);
                if (pick >= 0)
                {
                    a.target = pick;
                    Bills[pick].claim = a.id;
                    a.state = AgentState.Seek;
                }
                else if (a.carry.Count > 0)
                {
                    a.state = AgentState.Return;
                }
                else
                {
                    a.state = AgentState.Wander;
                    a.wanderTo = ClampToFloor(a.home + new Vector2(Rand(-140f, 140f), Rand(-120f, 120f)), 60f);
                    a.timer = Rand(0.6f, 1.4f);
                }
                break;

            case AgentState.Wander:
                Steer(a, a.wanderTo, speed * 0.45f, dt);
                a.timer -= dt;
                if (a.timer <= 0f) a.state = AgentState.Idle;
                break;

            case AgentState.Seek:
            {
                if (!TargetStillGood(a))
                {
                    a.target = -1;
                    a.state = AgentState.Idle;
                    break;
                }
                Vector2 goal = Bills[a.target].pos;
                Steer(a, goal, speed, dt);
                if ((goal - a.pos).sqrMagnitude < 26f * 26f)
                {
                    a.state = AgentState.Grab;
                    a.timer = CashRules.AgentGrabTime(dept);
                }
                break;
            }

            case AgentState.Grab:
                a.vel *= Mathf.Exp(-dt * 12f);
                a.timer -= dt;
                if (a.timer > 0f) break;
                if (TargetStillGood(a) && (Bills[a.target].pos - a.pos).sqrMagnitude < 60f * 60f)
                {
                    TakeIntoCarry(a, a.target);
                    Events.Add(new CashEvent(CashEventType.DeptGrab, a.pos, Bills[a.target].value, 1, a.dept));
                }
                a.target = -1;
                a.state = a.carry.Count >= capacity ? AgentState.Return : AgentState.Idle;
                a.timer = 0f;
                break;

            case AgentState.Return:
            {
                Vector2 door = DeptCenter[a.dept] + (a.home - DeptCenter[a.dept]).normalized * 70f;
                Steer(a, door, speed * (a.carry.Count > capacity / 2 ? 0.9f : 1f), dt);
                if ((door - a.pos).sqrMagnitude < 34f * 34f)
                {
                    for (int k = 0; k < a.carry.Count; k++)
                    {
                        int i = a.carry[k];
                        if (Bills[i].state != BillState.Carried || Bills[i].owner != a.id) continue;
                        Bills[i].owner = -1;
                        SendToDept(i, a.dept, k * 0.06f);
                    }
                    a.carry.Clear();
                    a.state = AgentState.Deposit;
                    a.timer = 0.35f;
                    a.bob = 1f;
                }
                break;
            }

            case AgentState.Deposit:
                a.vel *= Mathf.Exp(-dt * 10f);
                a.timer -= dt;
                if (a.timer <= 0f) a.state = AgentState.Idle;
                break;
        }

        Move(a, dt);
    }

    void Move(Agent a, float dt)
    {
        a.pos += a.vel * dt;
        a.pos = ClampToFloor(a.pos, 26f);
        float speed = a.vel.magnitude;
        if (Mathf.Abs(a.vel.x) > 12f) a.facing = Mathf.Sign(a.vel.x);
        a.walkPhase += speed * dt * 0.045f;
    }

    void Steer(Agent a, Vector2 goal, float speed, float dt)
    {
        Vector2 to = goal - a.pos;
        float d = to.magnitude;
        Vector2 desired = d > 4f ? to / d * Mathf.Min(speed, d * 5f) : Vector2.zero;
        a.vel = Vector2.MoveTowards(a.vel, desired, speed * 7f * dt);
    }

    void SeparateAgents(float dt)
    {
        for (int i = 0; i < agents.Count; i++)
        {
            Agent a = agents[i];
            if (!a.active) continue;
            for (int j = i + 1; j < agents.Count; j++)
            {
                Agent b = agents[j];
                if (!b.active) continue;
                Vector2 d = a.pos - b.pos;
                float dist = d.magnitude;
                if (dist >= 38f || dist < 0.01f) continue;
                Vector2 push = d / dist * (38f - dist) * 0.5f;
                a.pos += push;
                b.pos -= push;
            }
        }
    }

    bool TargetStillGood(Agent a)
    {
        int i = a.target;
        if (i < 0) return false;
        ref CashBill b = ref Bills[i];
        return b.state == BillState.Free && (b.claim == a.id || b.claim < 0) && b.pinned <= 0f && b.z < 30f;
    }

    /// <summary>Each department hunts differently: Backend the nearest, Frontend the biggest, Marketing near the CEO.</summary>
    int PickTarget(Agent a)
    {
        CashDept dept = (CashDept)a.dept;
        float range = dept == CashDept.Frontend ? 900f : 1100f;
        Vector2 focus = a.pos;
        bool chaseCeo = dept == CashDept.Marketing && Visible && Input.onFloor;
        if (chaseCeo) focus = Vector2.Lerp(a.pos, Cursor, 0.6f);

        int best = -1;
        double bestScore = double.MinValue;
        // A coarse sweep over the free bills (the grid makes nearby lookups cheap; workers re-pick rarely)
        for (int i = 0; i < MaxBills; i++)
        {
            ref CashBill b = ref Bills[i];
            if (b.state != BillState.Free || b.pinned > 0f || b.z > 20f) continue;
            if (b.claim >= 0 && b.claim != a.id) continue;
            if (SafeRect.Contains(b.pos)) continue;
            float d = (b.pos - a.pos).magnitude;
            if (d > range) continue;
            double score;
            switch (dept)
            {
                case CashDept.Frontend:
                    score = Math.Log10(b.value + 1.0) * 220.0 - d;
                    break;
                case CashDept.Marketing:
                    score = -(b.pos - focus).magnitude - d * 0.4;
                    break;
                default:
                    score = -d + (b.age > 6f ? 60.0 : 0.0);   // patient crawlers clean up the old stuff too
                    break;
            }
            // Everyone races for a golden bill (Frontend, the sniper, most of all)
            if (b.special == CashSpecial.Golden) score += dept == CashDept.Frontend ? 420.0 : 180.0;
            if (score > bestScore)
            {
                bestScore = score;
                best = i;
            }
        }
        return best;
    }

    void TakeIntoCarry(Agent a, int i)
    {
        ref CashBill b = ref Bills[i];
        b.state = BillState.Carried;
        b.owner = a.id;
        b.claim = -1;
        b.slot = a.carry.Count;
        b.sleeping = false;
        b.airborne = false;
        a.carry.Add(i);
        a.bob = 1f;
    }

    void DetachFromAgent(int i)
    {
        int owner = Bills[i].owner;
        if (owner >= 0 && owner < agents.Count) agents[owner].carry.Remove(i);
        Bills[i].owner = -1;
    }

    /// <summary>A worker lets go of everything (stapled, blown away, sent home): the bills scatter around them.</summary>
    void DropCarried(Agent a, float scatter)
    {
        if (a.carry.Count == 0) return;
        int count = 0;
        foreach (int i in a.carry)
        {
            ref CashBill b = ref Bills[i];
            if (b.state != BillState.Carried || b.owner != a.id) continue;
            b.state = BillState.Free;
            b.owner = -1;
            b.vel = new Vector2(Rand(-1f, 1f), Rand(-1f, 1f)).normalized * Rand(scatter * 0.4f, scatter);
            b.z = 24f;
            b.vz = 140f;
            b.airborne = true;
            b.landedOnce = false;
            b.sleeping = false;
            b.spin = Rand(-600f, 600f);
            count++;
        }
        a.carry.Clear();
        if (count > 0) Events.Add(new CashEvent(CashEventType.Drop, a.pos, 0.0, count, a.dept));
    }

    void Stun(Agent a, float seconds)
    {
        DropCarried(a, 160f);
        a.stun = Mathf.Max(a.stun, seconds);
        a.state = AgentState.Stunned;
        a.target = -1;
        a.pending = CashInterference.None;
        a.vel *= 0.2f;
    }

    /// <summary>The worker whose body is under <paramref name="p"/> (their sprite stands on their feet: body ~34 px up).</summary>
    Agent AgentAt(Vector2 p, float radius)
    {
        Agent best = null;
        float bestD = radius * radius;
        foreach (Agent a in agents)
        {
            if (!a.active) continue;
            float d = (a.pos + new Vector2(0f, -34f) - p).sqrMagnitude;
            if (d < bestD)
            {
                bestD = d;
                best = a;
            }
        }
        return best;
    }

    /// <summary>The CEO clicks a worker who carries money: take it back (as much as the tool holds).</summary>
    bool TryStealBack(Vector2 p)
    {
        Agent a = AgentAt(p, 46f);
        if (a == null || a.carry.Count == 0) return false;
        int capacity = Capacity;
        int taken = 0;
        double value = 0.0;
        for (int k = a.carry.Count - 1; k >= 0 && held.Count < capacity; k--)
        {
            int i = a.carry[k];
            if (Bills[i].state != BillState.Carried) continue;
            a.carry.RemoveAt(k);
            Bills[i].owner = -1;
            Bills[i].state = BillState.Free;
            value += Bills[i].value;
            Hold(i);
            taken++;
        }
        if (taken == 0) return false;
        a.bob = 1f;
        a.state = AgentState.Idle;
        a.timer = 0.4f;
        Events.Add(new CashEvent(CashEventType.Grab, p, value, taken, a.dept, 1));
        return true;
    }

    /// <summary>The industrial collector's blast pushes workers back and shakes bills out of their arms.</summary>
    void BlowAgentsAway(Vector2 p, float radius, float dt)
    {
        foreach (Agent a in agents)
        {
            if (!a.active) continue;
            Vector2 away = a.pos - p;
            float d = away.magnitude;
            if (d > radius || d < 0.01f) continue;
            a.vel += away / d * 900f * dt * (1.2f - d / radius);
            if (a.carry.Count > 0 && Chance(dt * 1.6f))
            {
                int i = a.carry[a.carry.Count - 1];
                a.carry.RemoveAt(a.carry.Count - 1);
                ref CashBill b = ref Bills[i];
                b.state = BillState.Free;
                b.owner = -1;
                b.vel = (p - a.pos).normalized * 200f;
                b.z = 20f;
                b.vz = 60f;
                b.airborne = true;
            }
        }
    }

    // ------------------------------------------------------------------
    // Interference
    // ------------------------------------------------------------------

    void ConsiderInterference(Agent a, int level)
    {
        if (a.interfereCooldown > 0f || globalInterferenceCooldown > 0f || !Input.onFloor) return;
        if (a.state == AgentState.Return && a.carry.Count > 0) return;   // busy banking money
        CashDept dept = (CashDept)a.dept;
        CashInterference pick = CashInterference.None;
        float distToCeo = (a.pos - Cursor).magnitude;

        switch (dept)
        {
            case CashDept.Backend:
                if (level >= CashRules.InterferenceLevel(dept, CashInterference.RateLimit) && FindHazard(HazardKind.RateLimit) == null && Chance(0.35f))
                    pick = CashInterference.RateLimit;
                else if (level >= CashRules.InterferenceLevel(dept, CashInterference.Firewall) && FindHazard(HazardKind.Firewall) == null)
                    pick = CashInterference.Firewall;
                break;
            case CashDept.Frontend:
                if (level >= CashRules.InterferenceLevel(dept, CashInterference.Popup) && FindHazard(HazardKind.Popup) == null && Chance(0.4f))
                    pick = CashInterference.Popup;
                else if (level >= CashRules.InterferenceLevel(dept, CashInterference.Shove) && held.Count >= 2 && distToCeo < 520f)
                    pick = CashInterference.Shove;
                break;
            case CashDept.Marketing:
                if (level >= CashRules.InterferenceLevel(dept, CashInterference.Confetti) && Chance(0.3f))
                    pick = CashInterference.Confetti;
                else if (level >= CashRules.InterferenceLevel(dept, CashInterference.Magnet) && FindHazard(HazardKind.Magnet) == null && Chance(0.45f))
                    pick = CashInterference.Magnet;
                else if (level >= CashRules.InterferenceLevel(dept, CashInterference.Yoink) && held.Count >= 1 && distToCeo < 480f)
                    pick = CashInterference.Yoink;
                break;
        }
        if (pick == CashInterference.None) return;

        // Telegraph: stop, show "!" (the view) and play the alarm, then act
        a.pending = pick;
        a.state = AgentState.Telegraph;
        a.telegraph = pick == CashInterference.Shove || pick == CashInterference.Yoink ? 0.45f : 0.7f;
        a.interfereCooldown = CashRules.InterferenceCooldown(level) * Rand(0.85f, 1.2f);
        globalInterferenceCooldown = 4.5f;
        if (a.target >= 0)
        {
            if (Bills[a.target].claim == a.id) Bills[a.target].claim = -1;
            a.target = -1;
        }
        Events.Add(new CashEvent(CashEventType.Telegraph, a.pos, 0.0, 0, a.dept, (int)pick));
    }

    void Interfere(Agent a, int level)
    {
        CashInterference kind = a.pending;
        a.pending = CashInterference.None;
        a.state = AgentState.Idle;
        a.timer = 0.2f;
        int extra = Mathf.Max(0, level - 2);
        Vector2 at = Input.onFloor ? Cursor : new Vector2(Size.x * 0.5f, Size.y * 0.5f);

        switch (kind)
        {
            case CashInterference.Firewall:
            {
                Vector2 size = new Vector2(230f + 20f * extra, 170f + 15f * extra);
                Vector2 c = ClampToFloor(at + new Vector2(Rand(-40f, 40f), Rand(-30f, 30f)), 90f);
                float time = 3.5f + 0.4f * extra;
                hazards.Add(new Hazard { kind = HazardKind.Firewall, rect = new Rect(c - size * 0.5f, size), time = time, duration = time, started = Time, dept = a.dept });
                break;
            }
            case CashInterference.RateLimit:
                hazards.Add(new Hazard { kind = HazardKind.RateLimit, center = at, time = 3f, duration = 3f, started = Time, dept = a.dept });
                if (level >= CashRules.MaxDeptLevel && (tool == CashTool.Vacuum || tool == CashTool.Industrial)) JamTimer = Mathf.Max(JamTimer, 1.8f);
                break;
            case CashInterference.Popup:
            {
                Vector2 size = new Vector2(270f, 176f);
                Vector2 c = ClampToFloor(at + new Vector2(Rand(-60f, 60f), Rand(-40f, 40f)), 100f);
                hazards.Add(new Hazard { kind = HazardKind.Popup, rect = new Rect(c - size * 0.5f, size), time = 5f, duration = 5f, started = Time, dept = a.dept });
                break;
            }
            case CashInterference.Magnet:
            {
                float time = 4.5f + 0.5f * extra;
                hazards.Add(new Hazard { kind = HazardKind.Magnet, center = DeptCenter[a.dept], radius = 380f + 30f * extra, time = time, duration = time, started = Time, dept = a.dept });
                break;
            }
            case CashInterference.Confetti:
            {
                int blown = 0;
                foreach (int i in FreeBillsNear(at, 210f))
                {
                    if (Bills[i].pinned > 0f) continue;
                    Vector2 away = Bills[i].pos - at;
                    float d = Mathf.Max(8f, away.magnitude);
                    Bills[i].vel += away / d * Rand(380f, 560f);
                    Bills[i].z = Mathf.Max(Bills[i].z, 10f);
                    Bills[i].vz = 140f;
                    Bills[i].airborne = true;
                    Bills[i].sleeping = false;
                    blown++;
                }
                break;
            }
            case CashInterference.Shove:
            case CashInterference.Yoink:
                a.state = AgentState.Dash;
                a.dashMode = kind;
                a.timer = 1.6f;
                break;
        }
        Events.Add(new CashEvent(CashEventType.Interference, kind == CashInterference.Magnet ? DeptCenter[a.dept] : at, 0.0, 0, a.dept, (int)kind));
    }

    void StepDash(Agent a, float dt, float speed)
    {
        a.timer -= dt;
        Steer(a, HandPos, speed * 2.3f, dt);
        bool contact = (a.pos + new Vector2(0f, -30f) - HandPos).sqrMagnitude < 48f * 48f;
        if (!contact && a.timer > 0f && held.Count > 0) return;
        a.state = AgentState.Idle;
        a.timer = 0.3f;
        if (!contact || held.Count == 0) return;

        // Gloves can hold on
        float resist = CashRules.GlovesResist(gadgetLevel[(int)CashGadget.Gloves]);
        if (resist > 0f && Chance(resist))
        {
            Events.Add(new CashEvent(CashEventType.Blocked, HandPos, 0.0, 0, a.dept));
            a.vel = (a.pos - HandPos).normalized * 420f;
            a.stun = 0.6f;
            return;
        }

        BreakStreak();
        if (a.dashMode == CashInterference.Yoink)
        {
            int level = Mathf.Max(1, deptLevel[a.dept]);
            int take = Mathf.Min(held.Count, 1 + level / 2);
            double value = 0.0;
            for (int k = 0; k < take; k++)
            {
                int i = held[held.Count - 1];
                held.RemoveAt(held.Count - 1);
                value += Bills[i].value;
                Bills[i].state = BillState.Free;
                TakeIntoCarry(a, i);
            }
            Events.Add(new CashEvent(CashEventType.Steal, HandPos, value, take, a.dept));
            a.state = AgentState.Return;
        }
        else
        {
            int loose = Mathf.Max(1, Mathf.RoundToInt(held.Count * 0.4f));
            for (int k = 0; k < loose && held.Count > 0; k++)
            {
                int i = held[held.Count - 1];
                held.RemoveAt(held.Count - 1);
                ref CashBill b = ref Bills[i];
                b.state = BillState.Free;
                b.vel = new Vector2(Rand(-1f, 1f), Rand(-1f, 1f)).normalized * Rand(160f, 360f) + a.vel * 0.3f;
                b.z = 20f;
                b.vz = 160f;
                b.airborne = true;
                b.landedOnce = false;
                b.sleeping = false;
                b.spin = Rand(-700f, 700f);
            }
            Events.Add(new CashEvent(CashEventType.Steal, HandPos, 0.0, loose, a.dept, 1));
            Events.Add(new CashEvent(CashEventType.Drop, HandPos, 0.0, loose, a.dept));
        }
    }

    // ------------------------------------------------------------------
    // Hazards
    // ------------------------------------------------------------------

    void StepHazards(float dt)
    {
        for (int k = hazards.Count - 1; k >= 0; k--)
        {
            Hazard h = hazards[k];
            h.time -= dt;
            if (h.kind == HazardKind.Paperweight)
            {
                // Pin what lies under it (a pinned bill is invisible to the departments and their magnet)
                foreach (int i in FreeBillsNear(h.center, h.radius))
                    Bills[i].pinned = Mathf.Max(Bills[i].pinned, 0.25f);
            }
            if (h.time > 0f) continue;
            hazards.RemoveAt(k);
            Events.Add(new CashEvent(CashEventType.InterferenceEnd, h.kind == HazardKind.Firewall || h.kind == HazardKind.Popup ? h.rect.center : h.center,
                                     0.0, 0, h.dept, (int)h.kind));
        }
    }

    Hazard FindHazard(HazardKind kind)
    {
        foreach (Hazard h in hazards)
            if (h.kind == kind) return h;
        return null;
    }

    Hazard FindHazard(CashInterference kind)
    {
        return kind == CashInterference.Magnet ? FindHazard(HazardKind.Magnet) : null;
    }

    /// <summary>The CEO can't pick money up here (a firewall or a pop-up covers it).</summary>
    public bool PickupBlocked(Vector2 p)
    {
        foreach (Hazard h in hazards)
        {
            if ((h.kind == HazardKind.Firewall || h.kind == HazardKind.Popup) && h.rect.Contains(p)) return true;
        }
        return false;
    }

    bool TryClosePopup(Vector2 p)
    {
        for (int k = hazards.Count - 1; k >= 0; k--)
        {
            Hazard h = hazards[k];
            if (h.kind != HazardKind.Popup || !h.rect.Contains(p)) continue;
            hazards.RemoveAt(k);
            Events.Add(new CashEvent(CashEventType.PopupClosed, h.rect.center, 0.0, 0, h.dept, (int)CashInterference.Popup));
            return true;
        }
        return false;
    }
}
