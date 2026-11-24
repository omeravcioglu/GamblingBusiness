using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>One piece of money on the cash floor (a struct in a pooled array: no allocations while playing).</summary>
public struct CashBill
{
    public BillState state;
    public byte denom;            // CashLadder index (sprite and feel)
    public CashKind kind;
    public double value;          // exact money (a respawned bill can hold an odd amount)
    public Vector2 pos;           // world units, y down
    public Vector2 vel;
    public float z, vz;           // height above the felt and its speed (flying, held, carried)
    public float angle, spin;     // degrees, degrees per second
    public float age;             // seconds since it appeared
    public float phase;           // flutter / wobble phase
    public int owner;             // Carried: agent index
    public int slot;              // Held / Carried: place in the stack
    public int claim;             // agent index chasing it (-1 none)
    public Vector2 from, to;      // ToSafe / ToDept / Sucked flight
    public float t, dur, delay;   // flight progress (0..1), length and start delay
    public int dept;              // ToDept: whose crate
    public int batch;             // ToSafe: deposit batch (-1 none)
    public int order;             // draw order (newer on top)
    public bool airborne;         // left a tube / thrown and hasn't settled
    public bool thrown;           // in the air because the CEO threw it
    public bool landedOnce;
    public float pinned;          // seconds a paperweight still pins it
    public bool sleeping;
    public byte card;             // the playing card in its serial number (CashPoker: 0..51)
    public CashSpecial special;   // golden / marked (from a tube only)
}

/// <summary>
/// The cash floor's simulation: bills (pooled, lightweight 2D physics with a height axis), the delivery tubes, the CEO's
/// tools and gadgets, the department workers and their interference. Pure C# (UnityEngine math only): the view draws it,
/// CashGrabSystem feeds it input and money, and the tests run it headless.
/// World units are pixels of the 1920 x 1080 frame at 1x zoom, origin at the floor's top-left corner, y pointing down.
/// Money only leaves the floor through the sink (CashLedger): a bill landing in the safe or in a crate.
/// </summary>
public sealed partial class CashFloorSim
{
    public const int MaxBills = 1200;
    const float CellSize = 64f;

    // ---- Layout ----
    public Vector2 Size { get; private set; } = CashRules.BaseFloorSize;
    public float Scale { get; private set; } = 1f;
    public Vector2 SafeCenter { get; private set; }
    public Rect SafeRect { get; private set; }          // the safe's body: bills that slide in are the CEO's
    public float DepositRadius { get; private set; }    // releasing held bills this close to the safe deposits them
    public readonly Vector2[] DeptCenter = new Vector2[CashLedger.DeptCount];
    public readonly Rect[] DeptRect = new Rect[CashLedger.DeptCount];
    public Vector2[] TubeMouths { get; private set; } = new Vector2[0];
    public readonly float[] TubeKick = new float[6];   // the view animates a tube that just fired

    // ---- Bills ----
    public readonly CashBill[] Bills = new CashBill[MaxBills];
    readonly int[] freeSlots = new int[MaxBills];
    int freeCount;
    public int ActiveCount { get; private set; }
    double billSum;           // money in active bills
    double pendingPayday;     // already-booked money still waiting to pour out of the tubes
    int orderCounter;

    // ---- Spatial hash (free bills on/near the felt) ----
    int gridW, gridH;
    int[] cellHead = new int[1];
    readonly int[] cellNext = new int[MaxBills];

    // ---- Events ----
    public readonly List<CashEvent> Events = new List<CashEvent>(64);

    public ICashSink Sink;
    readonly System.Random rng;
    public float Time { get; private set; }

    /// <summary>The floor is on screen (the CEO is here): workers interfere only then, and move a bit slower when not.</summary>
    public bool Visible;
    /// <summary>Workers' speed while the floor isn't on screen (the CEO is hiring at the desk).</summary>
    public float AwaySpeedFactor = 0.7f;

    public CashFloorSim(int seed = 12345)
    {
        rng = new System.Random(seed);
        for (int i = MaxBills - 1; i >= 0; i--)
        {
            Bills[i].state = BillState.Inactive;
            freeSlots[freeCount++] = i;
        }
        InitAgents();
        SetLayout(1f, 1);
    }

    // ------------------------------------------------------------------
    // Layout
    // ------------------------------------------------------------------

    /// <summary>Size the floor (office expansion makes it bigger) and place the safe, the crates and the tubes.</summary>
    public void SetLayout(float scale, int tubes)
    {
        scale = Mathf.Clamp(scale, 0.5f, 3f);
        tubes = Mathf.Clamp(tubes, 1, TubeKick.Length);
        bool changed = !Mathf.Approximately(scale, Scale) || TubeMouths.Length != tubes;
        Scale = scale;
        Size = CashRules.BaseFloorSize * scale;
        float w = Size.x, h = Size.y;

        SafeCenter = new Vector2(w * 0.5f, h - 92f);
        PlaceSafe();   // SafeRect and DepositRadius (the Wide safe perk widens them)

        DeptCenter[0] = new Vector2(98f, h * 0.46f);                // Backend: left wall
        DeptCenter[1] = new Vector2(w - 98f, h * 0.46f);            // Frontend: right wall
        DeptCenter[2] = new Vector2(176f, h - 78f);                 // Marketing: bottom-left corner
        for (int d = 0; d < CashLedger.DeptCount; d++)
            DeptRect[d] = new Rect(DeptCenter[d].x - 84f, DeptCenter[d].y - 60f, 168f, 120f);

        TubeMouths = new Vector2[tubes];
        float left = w * 0.24f, right = w * 0.76f;
        for (int i = 0; i < tubes; i++)
        {
            float t = tubes == 1 ? 0.5f : i / (float)(tubes - 1);
            TubeMouths[i] = new Vector2(Mathf.Lerp(left, right, t), 62f);   // the pipe hangs into the felt from the top edge
        }

        gridW = Mathf.Max(1, Mathf.CeilToInt(w / CellSize) + 1);
        gridH = Mathf.Max(1, Mathf.CeilToInt(h / CellSize) + 1);
        if (cellHead.Length != gridW * gridH) cellHead = new int[gridW * gridH];

        if (changed)
        {
            // Keep everything inside the (possibly smaller) floor
            for (int i = 0; i < MaxBills; i++)
            {
                if (Bills[i].state == BillState.Inactive) continue;
                Bills[i].pos = ClampToFloor(Bills[i].pos, 20f);
                Bills[i].sleeping = false;
            }
            foreach (Agent a in agents) a.pos = ClampToFloor(a.pos, 30f);
            foreach (Agent a in agents) a.home = HomeFor(a.dept, a.index);
        }
    }

    Vector2 ClampToFloor(Vector2 p, float margin)
    {
        return new Vector2(Mathf.Clamp(p.x, margin, Size.x - margin), Mathf.Clamp(p.y, margin, Size.y - margin));
    }

    // ------------------------------------------------------------------
    // Money on the floor
    // ------------------------------------------------------------------

    /// <summary>All the money on the floor: active bills plus a payday still in the tubes.</summary>
    public double FloorValue => Math.Max(0.0, billSum) + pendingPayday;
    public double BillValue => Math.Max(0.0, billSum);
    public double PendingPayday => pendingPayday;
    public int FreeSlots => freeCount;

    int Allocate()
    {
        if (freeCount == 0) return -1;
        int i = freeSlots[--freeCount];
        Bills[i] = default;
        Bills[i].owner = -1;
        Bills[i].claim = -1;
        Bills[i].batch = -1;
        Bills[i].dept = -1;
        ActiveCount++;
        return i;
    }

    void Release(int i)
    {
        if (Bills[i].state == BillState.Inactive) return;
        billSum -= Bills[i].value;
        if (Math.Abs(billSum) < 1e-6) billSum = 0.0;
        Bills[i].state = BillState.Inactive;
        Bills[i].value = 0.0;
        freeSlots[freeCount++] = i;
        ActiveCount--;
        if (ActiveCount == 0) billSum = 0.0;   // no float dust when the floor is empty
    }

    int Create(double value, Vector2 pos, CashKind? forceKind = null)
    {
        int i = Allocate();
        if (i < 0) return -1;
        int denom = CashLadder.IndexForValue(value);
        ref CashBill b = ref Bills[i];
        b.state = BillState.Free;
        b.denom = (byte)denom;
        b.kind = forceKind ?? CashLadder.All[denom].kind;
        b.value = value;
        b.pos = pos;
        b.phase = (float)rng.NextDouble() * 6.283f;
        b.angle = (float)(rng.NextDouble() * 360.0);
        b.card = (byte)rng.Next(52);
        b.order = ++orderCounter;
        billSum += value;
        return i;
    }

    /// <summary>
    /// A bill shoots out of a tube (the stream minted it; its money is already on the books). Returns its index, or -1
    /// when the floor is full (the caller then keeps the money owed and tries a bigger denomination).
    /// </summary>
    public int SpawnFromTube(double value, int tube) => SpawnFromTube(value, tube, true);

    /// <summary>The same; <paramref name="allowSpecial"/> false for a payday's big pieces (no golden payday bills).</summary>
    public int SpawnFromTube(double value, int tube, bool allowSpecial)
    {
        if (TubeMouths.Length == 0) return -1;
        tube = ((tube % TubeMouths.Length) + TubeMouths.Length) % TubeMouths.Length;
        Vector2 mouth = TubeMouths[tube];
        int i = Create(value, mouth + new Vector2(Rand(-6f, 6f), 0f));
        if (i < 0) return -1;
        ref CashBill b = ref Bills[i];
        float heavy = b.kind == CashKind.Bill || b.kind == CashKind.Check ? 1f : 0.75f;
        float angle = Rand(-32f, 32f) * Mathf.Deg2Rad;
        float speed = Rand(260f, 620f) * heavy * Mathf.Lerp(1f, 1.25f, (Scale - 1f) / 0.6f);
        b.vel = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * speed;
        b.z = 60f;
        b.vz = Rand(120f, 260f);
        b.spin = Rand(-540f, 540f);
        b.airborne = true;
        TubeKick[tube] = 1f;
        Events.Add(new CashEvent(CashEventType.TubePop, mouth, value));
        OnTubeBill(i, allowSpecial);
        return i;
    }

    /// <summary>A bill placed straight on the felt (a loaded save's floor money). -1 when full.</summary>
    public int SpawnScattered(double value)
    {
        Vector2 p = new Vector2(Rand(Size.x * 0.18f, Size.x * 0.82f), Rand(Size.y * 0.15f, Size.y * 0.7f));
        int i = Create(value, p);
        if (i < 0) return -1;
        Bills[i].sleeping = true;
        return i;
    }

    /// <summary>
    /// A saved floor's money comes back as a few dozen bills lying on the felt (no new money: it was counted when it was
    /// earned). What doesn't fit waits in the tubes as a payday (still floor money).
    /// </summary>
    public void RestoreFloor(double total)
    {
        if (double.IsNaN(total) || total <= 0.0) return;
        int index = CashLadder.IndexAtMost(total / 36.0);
        double piece = CashLadder.All[index].value;
        double left = total;
        int guard = 0;
        while (left > 1e-6 && guard++ < 60)
        {
            double value = left - piece < piece * 0.5 || guard >= 60 ? left : piece;
            if (SpawnScattered(value) < 0) break;
            left -= value;
        }
        if (left > 1e-6) RestorePayday(pendingPayday + left);
    }

    /// <summary>Money the day's books already counted, to pour out of the tubes over the next couple of seconds.</summary>
    public void QueuePayday(double amount)
    {
        if (double.IsNaN(amount) || amount <= 0.0) return;
        pendingPayday += amount;
        paydayPiece = Math.Max(1.0, pendingPayday / 20.0);   // about 20 pieces for the whole payday
        paydayAnnounced = false;
    }

    /// <summary>A loaded save's payday still in the tubes (saved as part of the floor's money).</summary>
    public void RestorePayday(double amount)
    {
        pendingPayday = double.IsNaN(amount) || amount < 0.0 ? 0.0 : amount;
        paydayPiece = Math.Max(1.0, pendingPayday / 20.0);
        paydayAnnounced = true;   // no "payday!" again after a load
    }

    bool paydayAnnounced;
    float paydayTimer;
    double paydayPiece = 1.0;

    void PourPayday(float dt)
    {
        if (pendingPayday <= 0.0) return;
        if (!paydayAnnounced)
        {
            paydayAnnounced = true;
            Events.Add(new CashEvent(CashEventType.Payday, new Vector2(Size.x * 0.5f, 40f), pendingPayday));
            StartBoost(CashBoost.Payday);   // PAYDAY x2 while it pours
        }
        paydayTimer -= dt;
        if (paydayTimer > 0f) return;
        paydayTimer = 0.05f;
        int denom = CashLadder.IndexAtMost(paydayPiece);
        double value = Math.Min(pendingPayday, CashLadder.All[denom].value);
        if (pendingPayday - value < 1.0) value = pendingPayday;   // the last crumbs ride on the last bill
        int tube = rng.Next(TubeMouths.Length);
        int i = SpawnFromTube(value, tube, false);
        if (i < 0)
        {
            paydayTimer = 0.25f;   // full floor: wait for room
            return;
        }
        pendingPayday -= value;
        if (pendingPayday < 1e-6) pendingPayday = 0.0;
    }

    // What the company account takes back first when it runs short: loose money, then what workers carry or send to their
    // crates, and only then what is in the CEO's hand or on its way into the safe
    static readonly BillState[] SeizeOrder = { BillState.Free, BillState.Carried, BillState.ToDept, BillState.Held, BillState.ToSafe, BillState.Sucked };

    /// <summary>
    /// Take money off the floor for the company account (CashLedger.Enforce): the payday still in the tubes first, then
    /// bills in SeizeOrder (oldest first within each). Returns what was taken (at most <paramref name="amount"/>: a bill
    /// bigger than what's needed gives part of its value and stays).
    /// </summary>
    public double TakeFromFloor(double amount)
    {
        if (amount <= 0.0) return 0.0;
        double taken = 0.0;
        double fromPayday = Math.Min(pendingPayday, amount);
        pendingPayday -= fromPayday;
        taken += fromPayday;

        foreach (BillState wanted in SeizeOrder)
        {
            while (taken < amount - 1e-6)
            {
                int oldest = -1;
                for (int i = 0; i < MaxBills; i++)
                {
                    if (Bills[i].state != wanted) continue;
                    if (oldest < 0 || Bills[i].order < Bills[oldest].order) oldest = i;
                }
                if (oldest < 0) break;
                double need = amount - taken;
                if (Bills[oldest].value > need + 1e-6)
                {
                    // Take part of a big bill: it stays, worth less
                    Bills[oldest].value -= need;
                    billSum -= need;
                    Bills[oldest].denom = (byte)CashLadder.IndexForValue(Bills[oldest].value);
                    taken += need;
                    break;
                }
                taken += Bills[oldest].value;
                Vector2 at = Bills[oldest].pos;
                RemoveSeized(oldest);
                Events.Add(new CashEvent(CashEventType.Seized, at));
            }
            if (taken >= amount - 1e-6) break;
        }
        return taken;
    }

    /// <summary>A bill the company account took: out of whoever's hands or flight it was in, then gone.</summary>
    void RemoveSeized(int i)
    {
        switch (Bills[i].state)
        {
            case BillState.Carried:
                DetachFromAgent(i);
                break;
            case BillState.Held:
                held.Remove(i);
                break;
            case BillState.ToSafe:
                DropFromBatch(Bills[i].batch);
                break;
        }
        Release(i);
    }

    /// <summary>Remove everything (a new company, a loaded save that brings its own floor).</summary>
    public void ClearFloor()
    {
        for (int i = 0; i < MaxBills; i++)
            if (Bills[i].state != BillState.Inactive) Release(i);
        billSum = 0.0;
        pendingPayday = 0.0;
        held.Clear();
        foreach (Agent a in agents)
        {
            a.carry.Clear();
            a.target = -1;
            a.state = AgentState.Idle;
        }
        hazards.Clear();
        batches.Clear();
        looseBonus = 0.0;
        looseSources = CashBonusSource.None;
        ClearBoost();
    }

    // ------------------------------------------------------------------
    // Step
    // ------------------------------------------------------------------

    /// <summary>Advance the floor by <paramref name="dt"/> seconds of office time.</summary>
    public void Step(float dt)
    {
        if (dt <= 0f) return;
        dt = Mathf.Min(dt, 0.05f);   // a hitch never teleports anything through a wall
        Time += dt;

        for (int i = 0; i < TubeKick.Length; i++) TubeKick[i] = Mathf.Max(0f, TubeKick[i] - dt * 4f);

        RebuildGrid();
        StepHazards(dt);
        StepPlayer(dt);
        StepAgents(dt);
        StepBills(dt);
        PourPayday(dt);
        StepCombo(dt);
        StepBonus(dt);
    }

    void StepBills(float dt)
    {
        float w = Size.x, h = Size.y;
        Hazard magnet = FindHazard(CashInterference.Magnet);

        for (int i = 0; i < MaxBills; i++)
        {
            ref CashBill b = ref Bills[i];
            if (b.state == BillState.Inactive) continue;
            b.age += dt;
            if (b.pinned > 0f) b.pinned = Mathf.Max(0f, b.pinned - dt);

            switch (b.state)
            {
                case BillState.Free:
                    StepFree(i, ref b, dt, w, h, magnet);
                    break;
                case BillState.Held:
                    StepHeld(ref b, dt);
                    break;
                case BillState.Carried:
                    StepCarried(ref b, dt);
                    break;
                case BillState.ToSafe:
                case BillState.ToDept:
                case BillState.Sucked:
                    StepFlight(i, ref b, dt);
                    break;
            }
        }
    }

    void StepFree(int i, ref CashBill b, float dt, float w, float h, Hazard magnet)
    {
        // A sleeping bill wakes up when something pushes it (vel set) or a magnet pulls
        bool pulled = magnet != null && b.pinned <= 0f && b.z <= 2f && (b.pos - magnet.center).sqrMagnitude < magnet.radius * magnet.radius;
        if (b.sleeping && !pulled && b.vel.sqrMagnitude < 1f) return;
        b.sleeping = false;

        if (pulled)
        {
            Vector2 to = magnet.center - b.pos;
            float d = to.magnitude;
            if (d > 1f) b.vel += to / d * (160f * Mathf.Clamp01(1.2f - d / magnet.radius)) * dt * 3f;
        }

        if (b.airborne || b.z > 0f || b.vz > 0f)
        {
            b.vz -= CashLadder.Gravity(b.kind) * dt;
            b.z += b.vz * dt;
            b.vel *= Mathf.Exp(-CashLadder.AirDrag(b.kind) * dt);
            float flutter = CashLadder.Flutter(b.kind);
            if (flutter > 0f) b.vel.x += Mathf.Sin(b.age * 7.3f + b.phase) * flutter * dt * 6f;
            if (b.z <= 0f)
            {
                b.z = 0f;
                float bounce = CashLadder.Bounce(b.kind);
                if (b.vz < -140f && bounce > 0.05f)
                {
                    b.vz = -b.vz * bounce;
                    b.vel *= 0.7f;
                }
                else
                {
                    b.vz = 0f;
                    b.airborne = false;
                    b.thrown = false;
                }
                if (!b.landedOnce)
                {
                    b.landedOnce = true;
                    Events.Add(new CashEvent(CashEventType.BillLand, b.pos, b.value, 0, -1, (int)b.kind));
                }
                b.spin *= 0.4f;
            }
        }
        else
        {
            float friction = CashLadder.Friction(b.kind);
            b.vel *= Mathf.Exp(-friction * dt);
            b.spin *= Mathf.Exp(-friction * 1.4f * dt);
        }

        b.pos += b.vel * dt;
        b.angle += b.spin * dt;

        // Walls
        const float margin = 18f;
        if (b.pos.x < margin) { b.pos.x = margin; b.vel.x = Mathf.Abs(b.vel.x) * 0.35f; }
        else if (b.pos.x > w - margin) { b.pos.x = w - margin; b.vel.x = -Mathf.Abs(b.vel.x) * 0.35f; }
        if (b.pos.y < margin) { b.pos.y = margin; b.vel.y = Mathf.Abs(b.vel.y) * 0.35f; }
        else if (b.pos.y > h - margin) { b.pos.y = h - margin; b.vel.y = -Mathf.Abs(b.vel.y) * 0.35f; }

        // Slid (or was thrown, raked, blown) into the safe or a crate: it lands there (a throw counts from higher up)
        if (b.z < (b.thrown ? 150f : 36f))
        {
            if (SafeRect.Contains(b.pos))
            {
                bool shot = b.thrown;
                SendToSafe(i, 0f, -1, 0.18f);
                if (shot) Events.Add(new CashEvent(CashEventType.Swish, b.pos, b.value));
                return;
            }
            for (int d = 0; d < CashLedger.DeptCount; d++)
            {
                if (deptLevel[d] <= 0 || !DeptRect[d].Contains(b.pos)) continue;
                SendToDept(i, d, 0f);
                return;
            }
        }

        // Settle: a slow bill on the felt goes to sleep (no physics until something touches it)
        if (!b.airborne && b.z <= 0f && b.vel.sqrMagnitude < 9f && Mathf.Abs(b.spin) < 8f)
        {
            b.vel = Vector2.zero;
            b.spin = 0f;
            b.sleeping = true;
        }
    }

    void StepHeld(ref CashBill b, float dt)
    {
        Vector2 target = HeldSlotPosition(b.slot);
        float k = 1f - Mathf.Exp(-dt * (tool == CashTool.Tray ? TrayFollow() : 28f));
        b.pos = Vector2.Lerp(b.pos, target, k);
        float targetZ = 14f + (tool == CashTool.Tray ? b.slot * 1.2f : 0f);
        b.z = Mathf.Lerp(b.z, targetZ, 1f - Mathf.Exp(-dt * 20f));
        float targetAngle = HeldSlotAngle(b.slot, b.phase);
        b.angle = Mathf.LerpAngle(b.angle, targetAngle, 1f - Mathf.Exp(-dt * 16f));
    }

    void StepCarried(ref CashBill b, float dt)
    {
        if (b.owner < 0 || b.owner >= agents.Count)
        {
            b.state = BillState.Free;
            b.owner = -1;
            return;
        }
        Agent a = agents[b.owner];
        Vector2 target = a.pos + new Vector2(Mathf.Sin(b.phase) * 4f, -58f - b.slot * 5f);
        b.pos = Vector2.Lerp(b.pos, target, 1f - Mathf.Exp(-dt * 22f));
        b.z = 24f + b.slot * 2f;
        b.angle = Mathf.LerpAngle(b.angle, Mathf.Sin(b.phase + b.slot) * 10f, 1f - Mathf.Exp(-dt * 10f));
    }

    void StepFlight(int i, ref CashBill b, float dt)
    {
        if (b.delay > 0f)
        {
            b.delay -= dt;
            if (b.state != BillState.Sucked) return;
        }
        b.t += dt / Mathf.Max(0.05f, b.dur);
        float t = Mathf.Clamp01(b.t);
        if (b.state == BillState.Sucked)
        {
            b.pos = HosePoint(b.from, t);
            b.z = 30f;
            b.angle += 900f * dt;
        }
        else
        {
            float e = t * t * (3f - 2f * t);
            Vector2 mid = (b.from + b.to) * 0.5f + new Vector2(0f, -Mathf.Min(140f, (b.to - b.from).magnitude * 0.35f));
            b.pos = Bezier(b.from, mid, b.to, e);
            b.z = Mathf.Sin(t * Mathf.PI) * 40f;
            b.angle += (b.state == BillState.ToSafe ? 540f : -420f) * dt;
        }
        if (t < 1f) return;

        double value = b.value;
        Vector2 at = b.to;
        if (b.state == BillState.ToDept)
        {
            int dept = b.dept;
            Release(i);
            Sink?.DepositDept(dept, value);
            Events.Add(new CashEvent(CashEventType.DeptDeposit, at, value, 1, dept));
        }
        else
        {
            int batch = b.batch;
            CashSpecial special = b.special;
            Release(i);
            Sink?.DepositPlayer(value);
            Events.Add(new CashEvent(CashEventType.DepositBill, SafeCenter, value, 0, -1, (int)special));
            CountDeposit(value);
            AccrueBonus(value, special, batch);
            FinishBatchPart(batch, value);
        }
    }

    /// <summary>Send a bill into the CEO's safe (it is the CEO's once it lands).</summary>
    void SendToSafe(int i, float delay, int batch, float duration = 0.32f)
    {
        ref CashBill b = ref Bills[i];
        if (b.state == BillState.Carried) DetachFromAgent(i);
        b.state = BillState.ToSafe;
        b.from = b.pos;
        b.to = SafeCenter + new Vector2(Rand(-22f, 22f), -36f);
        b.t = 0f;
        b.dur = duration;
        b.delay = delay;
        b.batch = batch;
        b.claim = -1;
        b.sleeping = false;
    }

    void SendToDept(int i, int dept, float delay)
    {
        ref CashBill b = ref Bills[i];
        b.state = BillState.ToDept;
        b.from = b.pos;
        b.to = DeptCenter[dept] + new Vector2(Rand(-20f, 20f), -24f);
        b.t = 0f;
        b.dur = 0.3f;
        b.delay = delay;
        b.dept = dept;
        b.claim = -1;
        b.owner = -1;
        b.sleeping = false;
    }

    // ------------------------------------------------------------------
    // Spatial queries
    // ------------------------------------------------------------------

    void RebuildGrid()
    {
        for (int c = 0; c < cellHead.Length; c++) cellHead[c] = -1;
        for (int i = 0; i < MaxBills; i++)
        {
            if (Bills[i].state != BillState.Free) continue;
            int c = Cell(Bills[i].pos);
            cellNext[i] = cellHead[c];
            cellHead[c] = i;
        }
    }

    int Cell(Vector2 p)
    {
        int cx = Mathf.Clamp((int)(p.x / CellSize), 0, gridW - 1);
        int cy = Mathf.Clamp((int)(p.y / CellSize), 0, gridH - 1);
        return cy * gridW + cx;
    }

    readonly List<int> queryResult = new List<int>(64);

    /// <summary>Free bills within <paramref name="radius"/> of <paramref name="p"/> (from the grid built this step).</summary>
    List<int> FreeBillsNear(Vector2 p, float radius)
    {
        queryResult.Clear();
        int x0 = Mathf.Clamp((int)((p.x - radius) / CellSize), 0, gridW - 1);
        int x1 = Mathf.Clamp((int)((p.x + radius) / CellSize), 0, gridW - 1);
        int y0 = Mathf.Clamp((int)((p.y - radius) / CellSize), 0, gridH - 1);
        int y1 = Mathf.Clamp((int)((p.y + radius) / CellSize), 0, gridH - 1);
        float r2 = radius * radius;
        for (int cy = y0; cy <= y1; cy++)
        {
            for (int cx = x0; cx <= x1; cx++)
            {
                for (int i = cellHead[cy * gridW + cx]; i >= 0; i = cellNext[i])
                {
                    if (Bills[i].state != BillState.Free) continue;
                    if ((Bills[i].pos - p).sqrMagnitude <= r2) queryResult.Add(i);
                }
            }
        }
        return queryResult;
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    float Rand(float min, float max) => min + (float)rng.NextDouble() * (max - min);
    bool Chance(float p) => rng.NextDouble() < p;

    static Vector2 Bezier(Vector2 a, Vector2 b, Vector2 c, float t)
    {
        float u = 1f - t;
        return u * u * a + 2f * u * t * b + t * t * c;
    }

    /// <summary>A point on the vacuum hose from the nozzle at <paramref name="nozzle"/> to the safe (t: 0 nozzle → 1 safe).</summary>
    public Vector2 HosePoint(Vector2 nozzle, float t)
    {
        Vector2 safeTop = SafeCenter + new Vector2(0f, -70f);
        Vector2 c1 = nozzle + new Vector2(0f, 140f);
        Vector2 c2 = safeTop + new Vector2(0f, -160f);
        float u = 1f - t;
        return u * u * u * nozzle + 3f * u * u * t * c1 + 3f * u * t * t * c2 + t * t * t * safeTop;
    }

    /// <summary>True when every active bill is inside the floor and the bill sum matches (the tests' sanity check).</summary>
    public bool CheckIntegrity(out string problem)
    {
        double sum = 0.0;
        int count = 0;
        for (int i = 0; i < MaxBills; i++)
        {
            if (Bills[i].state == BillState.Inactive) continue;
            count++;
            sum += Bills[i].value;
            if (double.IsNaN(Bills[i].value) || Bills[i].value <= 0.0)
            {
                problem = "bill " + i + " has value " + Bills[i].value;
                return false;
            }
        }
        if (count != ActiveCount)
        {
            problem = "active count " + ActiveCount + " but " + count + " bills are active";
            return false;
        }
        if (Math.Abs(sum - billSum) > Math.Max(0.01, sum * 1e-9))
        {
            problem = "bill sum drifted: tracked " + billSum + ", real " + sum;
            return false;
        }
        problem = null;
        return true;
    }

    /// <summary>Recompute the running bill sum from the bills (removes float drift after very long sessions).</summary>
    public void Resum()
    {
        double sum = 0.0;
        for (int i = 0; i < MaxBills; i++)
            if (Bills[i].state != BillState.Inactive) sum += Bills[i].value;
        billSum = sum;
    }
}
