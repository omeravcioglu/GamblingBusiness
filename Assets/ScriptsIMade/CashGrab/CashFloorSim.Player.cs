using System;
using System.Collections.Generic;
using UnityEngine;

// The CEO's side of the floor: the mouse tools, deposits into the safe, throws, the gadgets and the deposit streak.
public sealed partial class CashFloorSim
{
    /// <summary>The mouse, in floor coordinates, as the view (or a test) reads it each step.</summary>
    public struct PlayerInput
    {
        public bool onFloor;     // the floor is open and the cursor is over the felt
        public Vector2 cursor;   // world units
        public bool down;        // left button went down this step
        public bool held;        // left button is down
        public bool up;          // left button went up this step
        public bool altDown;     // right button went down this step (gadget)
    }

    public PlayerInput Input;

    // ---- Equipment (CashGrabSystem sets these from what the CEO owns) ----
    public CashTool tool = CashTool.Hands;
    public int toolLevel = 1;
    public CashGadget gadget = CashGadget.None;
    public readonly int[] gadgetLevel = new int[CashRules.GadgetCount];
    readonly float[] gadgetCooldown = new float[CashRules.GadgetCount];

    // ---- Hand / tray ----
    readonly List<int> held = new List<int>();
    public IReadOnlyList<int> Held => held;
    public Vector2 Cursor { get; private set; }
    public Vector2 CursorVelocity { get; private set; }
    /// <summary>Where the hand / tray actually is (it lags behind the cursor when the tray is heavy).</summary>
    public Vector2 HandPos { get; private set; }
    bool cursorKnown;
    float pickTimer;
    float rakeSoundTimer;

    // ---- Vacuum ----
    public float VacuumHeat { get; private set; }
    public float JamTimer { get; private set; }
    public bool Sucking { get; private set; }
    float suckSoundTimer;

    // ---- Deposit streak (combo) ----
    public int Streak { get; private set; }
    public double StreakValue { get; private set; }
    public int StreakTier { get; private set; }
    public int BestStreak { get; private set; }
    public float StreakTimeLeft => streakTimer;
    public const float StreakWindow = 2.6f;
    static readonly int[] StreakTiers = { 8, 20, 45, 90, 180, 360 };
    float streakTimer;

    struct Batch
    {
        public int remaining;
        public int count;
        public double value;
        public double bonus;              // boost / golden / marked extras of the bills that landed (paid when it finishes)
        public CashBonusSource sources;
        public int poker;                 // CashPoker.Rank of a bare-hands deposit of five (0 = none)
        public double handValue;
    }

    readonly Dictionary<int, Batch> batches = new Dictionary<int, Batch>();
    int nextBatch;

    public float GadgetCooldownLeft(CashGadget g) => g == CashGadget.None ? 0f : gadgetCooldown[(int)g];

    public float GadgetCooldownTotal(CashGadget g)
    {
        int level = g == CashGadget.None ? 0 : gadgetLevel[(int)g];
        switch (g)
        {
            case CashGadget.Stapler: return CashRules.StaplerCooldown(level);
            case CashGadget.Paperweight: return CashRules.PaperweightCooldown;
            case CashGadget.Fan: return CashRules.FanCooldown(level);
            default: return 1f;
        }
    }

    /// <summary>Tools grab a little farther while the streak is hot.</summary>
    public float StreakReach => (1f + 0.06f * StreakTier) * ReachBonus;

    /// <summary>The tool's capacity (what one trip can carry; 0 for tools that don't carry).</summary>
    public int Capacity
    {
        get
        {
            switch (tool)
            {
                case CashTool.Hands: return CashRules.HandsCapacity;
                case CashTool.Scoop: return CashRules.ScoopCapacity(toolLevel);
                case CashTool.Tray: return CashRules.TrayCapacity(toolLevel);
                default: return 0;
            }
        }
    }

    /// <summary>Pick-up / suction / rake reach of the current tool (world units), streak included.</summary>
    public float ToolRadius
    {
        get
        {
            switch (tool)
            {
                case CashTool.Scoop: return CashRules.ScoopRadius(toolLevel) * StreakReach;
                case CashTool.Rake: return CashRules.RakeLength(toolLevel) * 0.5f * StreakReach;
                case CashTool.Tray: return CashRules.TrayRadius(toolLevel) * StreakReach;
                case CashTool.Vacuum: return CashRules.VacuumRadius(toolLevel) * StreakReach;
                case CashTool.Industrial: return CashRules.IndustrialRadius(toolLevel) * StreakReach;
                default: return 34f * StreakReach;
            }
        }
    }

    public double HeldValue
    {
        get
        {
            double sum = 0.0;
            foreach (int i in held) sum += Bills[i].value;
            return sum;
        }
    }

    // ------------------------------------------------------------------
    // Step
    // ------------------------------------------------------------------

    void StepPlayer(float dt)
    {
        for (int g = 0; g < gadgetCooldown.Length; g++) gadgetCooldown[g] = Mathf.Max(0f, gadgetCooldown[g] - dt);
        if (JamTimer > 0f)
        {
            JamTimer = Mathf.Max(0f, JamTimer - dt);
            VacuumHeat = Mathf.Max(0f, VacuumHeat - dt * 0.3f);
        }
        rakeSoundTimer -= dt;
        suckSoundTimer -= dt;

        PlayerInput input = Input;
        if (input.onFloor || held.Count > 0)
        {
            Vector2 p = ClampToFloor(input.cursor, 10f);
            if (!cursorKnown)
            {
                Cursor = p;
                HandPos = p;
                cursorKnown = true;
            }
            Vector2 instant = (p - Cursor) / dt;
            CursorVelocity = Vector2.Lerp(CursorVelocity, instant, 1f - Mathf.Exp(-dt * 22f));
            Cursor = p;
        }
        else
        {
            cursorKnown = false;
            CursorVelocity = Vector2.zero;
        }
        float follow = tool == CashTool.Tray ? TrayFollow() : 40f;
        HandPos = Vector2.Lerp(HandPos, Cursor, 1f - Mathf.Exp(-dt * follow));

        Sucking = false;

        // The floor closed or the mouse left it while carrying: let go where the hand is
        if (held.Count > 0 && (!input.held || !Visible))
        {
            bool overSafe = (HandPos - SafeCenter).sqrMagnitude <= DepositRadius * DepositRadius;
            if (Visible && input.onFloor && overSafe) DepositHeld();
            else ReleaseHeld(Visible && input.onFloor && input.up);
        }

        if (!Visible || !input.onFloor)
        {
            CoolVacuum(dt);
            return;
        }

        if (input.altDown && !TryStamp()) UseGadget();

        if (input.down)
        {
            // A pop-up window takes the click that closes it
            if (TryClosePopup(Cursor)) return;
            // Grab money back out of a worker's arms
            if (CarriesTool() && TryStealBack(Cursor)) return;
        }

        switch (tool)
        {
            case CashTool.Hands:
            case CashTool.Scoop:
                StepGrabber(dt, input);
                break;
            case CashTool.Tray:
                StepTray(dt, input);
                break;
            case CashTool.Rake:
                StepRake(dt, input);
                break;
            case CashTool.Vacuum:
            case CashTool.Industrial:
                StepVacuum(dt, input);
                break;
        }
        if (!Sucking) CoolVacuum(dt);
    }

    bool CarriesTool() => tool == CashTool.Hands || tool == CashTool.Scoop || tool == CashTool.Tray;

    void CoolVacuum(float dt)
    {
        if (JamTimer <= 0f) VacuumHeat = Mathf.Max(0f, VacuumHeat - dt * 0.35f);
    }

    // ---- Hands and scoop: pick up, carry, release over the safe (or throw) ----

    void StepGrabber(float dt, PlayerInput input)
    {
        int capacity = Capacity;
        if (input.down)
        {
            int count = held.Count;
            if (tool == CashTool.Hands) GrabTopmost(Cursor);
            else GrabAround(HandPos, ToolRadius, capacity);
            if (held.Count == count && FreeBillsNear(Cursor, ToolRadius).Count > 0 && PickupBlocked(Cursor))
                Events.Add(new CashEvent(CashEventType.Denied, Cursor));
        }
        else if (input.held && tool == CashTool.Hands && held.Count < capacity)
        {
            // Pinch: keep the button down and pass over bills to pick up a hand of five, one at a time
            pickTimer -= dt;
            if (pickTimer <= 0f)
            {
                int before = held.Count;
                GrabTopmost(Cursor);
                if (held.Count > before) pickTimer = CashRules.HandsPinchInterval * (RateLimited ? 2f : 1f);
            }
        }
        else if (input.held && tool == CashTool.Scoop && held.Count < capacity)
        {
            pickTimer -= dt;
            if (pickTimer <= 0f)
            {
                pickTimer = RateLimited ? 0.14f : 0f;
                GrabAround(HandPos, ToolRadius, RateLimited ? Math.Min(capacity, held.Count + 1) : capacity);
            }
        }
    }

    void StepTray(float dt, PlayerInput input)
    {
        if (!input.held) return;
        int capacity = CashRules.TrayCapacity(toolLevel);
        if (held.Count >= capacity) return;
        float rate = CashRules.TrayPickRate(toolLevel) * (RateLimited ? 0.5f : 1f);
        pickTimer -= dt;
        int guard = 4;
        while (pickTimer <= 0f && held.Count < capacity && guard-- > 0)
        {
            pickTimer += 1f / Mathf.Max(1f, rate);
            int i = NearestGrabbable(HandPos, ToolRadius);
            if (i < 0)
            {
                pickTimer = Mathf.Max(pickTimer, 0f);
                break;
            }
            Hold(i);
            Events.Add(new CashEvent(CashEventType.StackTick, Bills[i].pos, Bills[i].value, held.Count, -1, held.Count));
        }
        if (pickTimer < -0.2f) pickTimer = 0f;
    }

    /// <summary>A loaded tray is heavy: it follows the mouse more lazily the fuller it is.</summary>
    float TrayFollow()
    {
        int capacity = Mathf.Max(1, CashRules.TrayCapacity(toolLevel));
        return Mathf.Lerp(26f, 8f, Mathf.Clamp01(held.Count / (float)capacity));
    }

    void GrabTopmost(Vector2 p)
    {
        int best = -1;
        foreach (int i in FreeBillsNear(p, 70f))
        {
            ref CashBill b = ref Bills[i];
            if (b.z > 90f || !Grabbable(i)) continue;
            float r = CashLadder.GrabRadius(b.kind) * StreakReach;
            if ((b.pos - p).sqrMagnitude > r * r) continue;
            if (best < 0 || b.order > Bills[best].order) best = i;
        }
        if (best < 0) return;
        Hold(best);
        Events.Add(new CashEvent(CashEventType.Grab, p, Bills[best].value, 1));
    }

    void GrabAround(Vector2 p, float radius, int capacity)
    {
        int before = held.Count;
        double value = 0.0;
        List<int> near = FreeBillsNear(p, radius);
        // Nearest first, so a full scoop takes the closest money
        near.Sort((a, b) => (Bills[a].pos - p).sqrMagnitude.CompareTo((Bills[b].pos - p).sqrMagnitude));
        foreach (int i in near)
        {
            if (held.Count >= capacity) break;
            if (Bills[i].z > 90f || !Grabbable(i)) continue;
            value += Bills[i].value;
            Hold(i);
        }
        int got = held.Count - before;
        if (got == 1) Events.Add(new CashEvent(CashEventType.Grab, p, value, 1));
        else if (got > 1) Events.Add(new CashEvent(CashEventType.GrabMany, p, value, got));
    }

    int NearestGrabbable(Vector2 p, float radius)
    {
        int best = -1;
        float bestD = float.MaxValue;
        foreach (int i in FreeBillsNear(p, radius))
        {
            if (Bills[i].z > 90f || !Grabbable(i)) continue;
            float d = (Bills[i].pos - p).sqrMagnitude;
            if (d < bestD)
            {
                bestD = d;
                best = i;
            }
        }
        return best;
    }

    bool Grabbable(int i)
    {
        return Bills[i].state == BillState.Free && !PickupBlocked(Bills[i].pos);
    }

    void Hold(int i)
    {
        ref CashBill b = ref Bills[i];
        if (b.claim >= 0 && b.claim < agents.Count && agents[b.claim].target == i) agents[b.claim].target = -1;
        b.state = BillState.Held;
        b.claim = -1;
        b.slot = held.Count;
        b.sleeping = false;
        b.airborne = false;
        b.thrown = false;
        b.vel = Vector2.zero;
        b.spin = 0f;
        held.Add(i);
    }

    Vector2 HeldSlotPosition(int slot)
    {
        switch (tool)
        {
            case CashTool.Tray:
                return HandPos + new Vector2(Mathf.Sin(slot * 2.39996f) * 7f, -slot * 1.6f + 4f);
            case CashTool.Scoop:
            {
                float a = slot * 2.39996f;   // golden angle: a neat heap
                float r = 6f + Mathf.Sqrt(slot) * 7f;
                return HandPos + new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r * 0.6f);
            }
            case CashTool.Hands:
                // Fanned like a hand of cards
                return HandPos + new Vector2((slot - 2f) * 9f, 6f - Mathf.Abs(slot - 2f) * 2f);
            default:
                return HandPos + new Vector2(0f, 6f);
        }
    }

    float HeldSlotAngle(int slot, float phase)
    {
        if (tool == CashTool.Tray) return Mathf.Sin(phase) * 9f;
        if (tool == CashTool.Hands) return -90f + (slot - 2f) * 12f;
        return Mathf.Sin(phase + slot) * 24f - 8f;
    }

    /// <summary>Everything in the hand flies into the safe (a staggered cascade, one batch for the "+$" pop and the combo).</summary>
    void DepositHeld()
    {
        int count = held.Count;
        if (count == 0) return;
        int batch = ++nextBatch;
        var fresh = new Batch { remaining = count };
        ScoreHand(ref fresh);
        batches[batch] = fresh;
        float stagger = tool == CashTool.Tray ? Mathf.Min(0.022f, 0.9f / count) : 0.045f;
        for (int k = 0; k < count; k++)
            SendToSafe(held[k], k * stagger, batch, 0.24f);
        held.Clear();
    }

    /// <summary>Let go away from the safe: a throw (with the hand's speed) or a gentle drop.</summary>
    void ReleaseHeld(bool throwIt)
    {
        if (held.Count == 0) return;
        Vector2 v = CursorVelocity;
        if (v.magnitude > 1900f) v = v.normalized * 1900f;
        bool isThrow = throwIt && v.magnitude > 320f;
        float spill = tool == CashTool.Tray ? 160f : 70f;
        foreach (int i in held)
        {
            ref CashBill b = ref Bills[i];
            b.state = BillState.Free;
            b.vel = (isThrow ? v * 0.92f : v * 0.3f) + new Vector2(Rand(-spill, spill), Rand(-spill, spill));
            b.z = Mathf.Max(b.z, 14f);
            b.vz = isThrow ? 170f + v.magnitude * 0.08f : 60f;
            b.spin = Rand(-720f, 720f);
            b.airborne = true;
            b.thrown = isThrow;
            b.landedOnce = false;
            b.sleeping = false;
            b.claim = -1;
        }
        Events.Add(new CashEvent(isThrow ? CashEventType.Throw : CashEventType.Drop, HandPos, 0.0, held.Count));
        held.Clear();
    }

    /// <summary>Drop whatever the hand holds right where it is (the floor closes).</summary>
    public void DropHeld()
    {
        ReleaseHeld(false);
    }

    // ---- Rake: sweep bills along (push them into the safe's slot) ----

    void StepRake(float dt, PlayerInput input)
    {
        if (!input.held) return;
        Vector2 v = CursorVelocity;
        float speed = v.magnitude;
        if (speed < 30f) return;
        float half = CashRules.RakeLength(toolLevel) * 0.5f * StreakReach;
        float push = RateLimited ? 0.55f : 1f;
        int moved = 0;
        // The paddle is a horizontal bar at the cursor; anything it overlaps (or swept past this frame) rides along
        foreach (int i in FreeBillsNear(Cursor, half + 30f))
        {
            ref CashBill b = ref Bills[i];
            if (b.z > 40f) continue;
            Vector2 d = b.pos - Cursor;
            if (Mathf.Abs(d.x) > half || Mathf.Abs(d.y) > 34f) continue;
            b.vel = Vector2.Lerp(b.vel, v * 1.08f * push, 0.75f);
            b.pos += v.normalized * Mathf.Min(10f, speed * dt);   // stay ahead of the paddle
            b.spin += Rand(-90f, 90f);
            b.sleeping = false;
            b.airborne = false;
            moved++;
        }
        if (moved > 0 && rakeSoundTimer <= 0f)
        {
            rakeSoundTimer = 0.22f;
            Events.Add(new CashEvent(CashEventType.RakeSweep, Cursor, 0.0, moved));
        }
    }

    // ---- Vacuum and industrial collector: suction through a hose into the safe ----

    void StepVacuum(float dt, PlayerInput input)
    {
        bool industrial = tool == CashTool.Industrial;
        if (!input.held) return;
        if (JamTimer > 0f)
        {
            if (input.down) Events.Add(new CashEvent(CashEventType.Jam, Cursor));
            return;
        }
        Sucking = true;
        if (!industrial)
        {
            VacuumHeat += CashRules.VacuumHeatRate(toolLevel) * dt;
            if (VacuumHeat >= 1f)
            {
                VacuumHeat = 1f;
                JamTimer = 2.5f;
                Sucking = false;
                Events.Add(new CashEvent(CashEventType.Jam, Cursor));
                return;
            }
        }

        float radius = ToolRadius;
        float force = (industrial ? CashRules.IndustrialForce(toolLevel) : CashRules.VacuumForce(toolLevel)) * (RateLimited ? 0.5f : 1f);
        int sucked = 0;
        double suckedValue = 0.0;
        foreach (int i in FreeBillsNear(Cursor, radius))
        {
            ref CashBill b = ref Bills[i];
            if (PickupBlocked(b.pos)) continue;
            Vector2 to = Cursor - b.pos;
            float d = to.magnitude;
            if (d < 26f)
            {
                b.state = BillState.Sucked;
                b.from = Cursor;
                b.t = 0f;
                b.dur = 0.5f + (Cursor - SafeCenter).magnitude / 2600f;
                b.claim = -1;
                b.batch = -1;
                sucked++;
                suckedValue += b.value;
                continue;
            }
            float pull = force * Mathf.Sqrt(Mathf.Clamp01(1f - d / radius));
            b.vel = Vector2.Lerp(b.vel, to / d * Mathf.Min(pull, 900f), 1f - Mathf.Exp(-dt * 6f));
            b.vel += to / d * pull * dt;
            b.spin += Rand(-200f, 200f) * dt * 10f;
            b.sleeping = false;
            if (b.z < 6f) b.z = 6f;
            b.airborne = true;
            b.vz = Mathf.Max(b.vz, 30f);
        }
        if (sucked > 0 && suckSoundTimer <= 0f)
        {
            suckSoundTimer = 0.06f;
            Events.Add(new CashEvent(CashEventType.VacuumSuck, Cursor, suckedValue, sucked));
        }

        // Sucked bills already in the hose follow the nozzle
        for (int i = 0; i < MaxBills; i++)
            if (Bills[i].state == BillState.Sucked) Bills[i].from = Cursor;

        if (industrial) BlowAgentsAway(Cursor, radius * 0.55f, dt);
    }

    // ------------------------------------------------------------------
    // Gadgets (right mouse button)
    // ------------------------------------------------------------------

    void UseGadget()
    {
        CashGadget g = gadget;
        if (g == CashGadget.None || g == CashGadget.Gloves || gadgetLevel[(int)g] <= 0) return;
        if (gadgetCooldown[(int)g] > 0f)
        {
            Events.Add(new CashEvent(CashEventType.Miss, Cursor, 0.0, 0, -1, 1));
            return;
        }
        int level = gadgetLevel[(int)g];
        switch (g)
        {
            case CashGadget.Stapler:
            {
                Agent a = AgentAt(Cursor, 52f);
                if (a == null || a.stun > 0f)
                {
                    Events.Add(new CashEvent(CashEventType.Miss, Cursor));
                    return;
                }
                Stun(a, CashRules.StaplerStun(level));
                Events.Add(new CashEvent(CashEventType.Stun, a.pos, 0.0, 0, a.dept));
                gadgetCooldown[(int)g] = CashRules.StaplerCooldown(level);
                break;
            }
            case CashGadget.Paperweight:
            {
                int max = level >= 2 ? 2 : 1;
                int active = 0;
                Hazard oldest = null;
                foreach (Hazard h in hazards)
                {
                    if (h.kind != HazardKind.Paperweight) continue;
                    active++;
                    if (oldest == null || h.started < oldest.started) oldest = h;
                }
                if (active >= max && oldest != null) hazards.Remove(oldest);
                hazards.Add(new Hazard
                {
                    kind = HazardKind.Paperweight,
                    center = Cursor,
                    radius = CashRules.PaperweightRadius(level),
                    time = CashRules.PaperweightDuration(level),
                    duration = CashRules.PaperweightDuration(level),
                    started = Time,
                    dept = -1
                });
                Events.Add(new CashEvent(CashEventType.PaperweightPlaced, Cursor));
                gadgetCooldown[(int)g] = CashRules.PaperweightCooldown;
                break;
            }
            case CashGadget.Fan:
            {
                float radius = CashRules.FanRadius(level);
                foreach (Agent a in agents)
                {
                    if (!a.active) continue;
                    Vector2 away = a.pos - Cursor;
                    float d = away.magnitude;
                    if (d > radius) continue;
                    a.vel = (d > 1f ? away / d : Vector2.right) * 700f * (1.2f - d / radius);
                    DropCarried(a, 220f);
                    a.stun = Mathf.Max(a.stun, 0.8f);
                    a.state = AgentState.Stunned;
                    a.pending = CashInterference.None;
                }
                for (int k = hazards.Count - 1; k >= 0; k--)
                {
                    Hazard h = hazards[k];
                    if (h.kind != HazardKind.Firewall && h.kind != HazardKind.Popup) continue;
                    if (!h.rect.Overlaps(new Rect(Cursor.x - radius, Cursor.y - radius, radius * 2f, radius * 2f))) continue;
                    hazards.RemoveAt(k);
                    Events.Add(new CashEvent(CashEventType.PopupClosed, h.rect.center, 0.0, 0, h.dept, (int)(h.kind == HazardKind.Firewall ? CashInterference.Firewall : CashInterference.Popup)));
                }
                foreach (int i in FreeBillsNear(Cursor, radius * 0.6f))
                {
                    Vector2 away = Bills[i].pos - Cursor;
                    float d = Mathf.Max(1f, away.magnitude);
                    Bills[i].vel += away / d * 160f;
                    Bills[i].sleeping = false;
                }
                Events.Add(new CashEvent(CashEventType.FanBlast, Cursor, 0.0, 0, -1, (int)radius));
                gadgetCooldown[(int)g] = CashRules.FanCooldown(level);
                break;
            }
        }
    }

    // ------------------------------------------------------------------
    // Deposits and the streak
    // ------------------------------------------------------------------

    void FinishBatchPart(int batch, double value)
    {
        if (batch < 0 || !batches.TryGetValue(batch, out Batch b))
        {
            Events.Add(new CashEvent(CashEventType.DepositBatch, SafeCenter, value, 1));
            return;
        }
        b.remaining--;
        b.count++;
        b.value += value;
        if (b.remaining <= 0)
        {
            batches.Remove(batch);
            Events.Add(new CashEvent(CashEventType.DepositBatch, SafeCenter, b.value, b.count));
            SettleBatchBonus(b);
        }
        else
        {
            batches[batch] = b;
        }
    }

    /// <summary>A bill of a deposit batch vanished before landing (the company account took it): the batch finishes without it.</summary>
    void DropFromBatch(int batch)
    {
        if (batch < 0 || !batches.TryGetValue(batch, out Batch b)) return;
        b.remaining--;
        if (b.remaining > 0)
        {
            batches[batch] = b;
            return;
        }
        batches.Remove(batch);
        if (b.count > 0) Events.Add(new CashEvent(CashEventType.DepositBatch, SafeCenter, b.value, b.count));
        b.poker = 0;   // the hand lost a card on the way in: no poker hand
        SettleBatchBonus(b);
    }

    void CountDeposit(double value)
    {
        Streak++;
        StreakValue += value;
        streakTimer = StreakWindow;
        if (Streak > BestStreak) BestStreak = Streak;
        int tier = 0;
        while (tier < StreakTiers.Length && Streak >= StreakTiers[tier]) tier++;
        if (tier > StreakTier)
        {
            StreakTier = tier;
            Events.Add(new CashEvent(CashEventType.ComboTier, SafeCenter, StreakValue, Streak, -1, tier));
        }
    }

    void StepCombo(float dt)
    {
        if (Streak <= 0) return;
        streakTimer -= dt;
        if (streakTimer <= 0f) BreakStreak();
    }

    void BreakStreak()
    {
        Streak = 0;
        StreakValue = 0.0;
        StreakTier = 0;
        streakTimer = 0f;
    }

    /// <summary>Restore the best streak from a save.</summary>
    public void RestoreBestStreak(int best)
    {
        BestStreak = Math.Max(BestStreak, best);
    }
}
