using System;
using UnityEngine;

// The Cash Floor ("Cash Grab", 2026-11-24): the company's day income arrives as physical money on a felt floor, the CEO
// grabs it with the mouse and deposits it in their safe (Player Cash, the only money for gambling, stocks, the build
// gamble and collection tools), and the Backend, Frontend and Marketing departments grab the same money for their own
// budgets. Shared small types live here; see CashGrabSystem for how the pieces fit together.

/// <summary>The three departments that compete with the CEO for the cash on the floor. Saved by number: never reorder.</summary>
public enum CashDept
{
    Backend = 0,
    Frontend = 1,
    Marketing = 2
}

/// <summary>The CEO's collection tools, in unlock order. Saved by number: never reorder.</summary>
public enum CashTool
{
    Hands = 0,       // one bill at a time: click, drag, drop in the safe (or throw it)
    Scoop = 1,       // several bills per movement
    Rake = 2,        // sweeps and herds bills (pushes them into the safe's slot)
    Tray = 3,        // stacks lots of bills, deposits them all at once
    Vacuum = 4,      // continuous suction through a hose into the safe (overheats)
    Industrial = 5   // large-scale suction, pushes department workers back
}

/// <summary>Counter-equipment against the departments (right mouse button). Saved by number: never reorder.</summary>
public enum CashGadget
{
    None = -1,
    Stapler = 0,     // staple a worker's tie to the floor: stunned, drops what they carry
    Paperweight = 1, // pins the bills around it: departments can't take them
    Fan = 2,         // blows workers back (they drop their bills) and clears pop-ups and firewalls
    Gloves = 3       // passive: held bills resist shoves and yoinks
}

/// <summary>What a bill is doing.</summary>
public enum BillState : byte
{
    Inactive = 0,
    Free,          // on the felt (or in the air after a tube spat it out or a throw)
    Held,          // in the CEO's hand / scoop / on the tray
    Carried,       // a department worker carries it
    ToSafe,        // flying into the safe (committed: counts as the CEO's once it lands)
    ToDept,        // flying into a department's crate
    Sucked         // travelling up the vacuum hose to the safe
}

/// <summary>Department interference mechanics (readable, telegraphed, counterable).</summary>
public enum CashInterference
{
    None = 0,
    Firewall,      // Backend: a zone where the CEO can't pick anything up (Fan clears it)
    RateLimit,     // Backend: the CEO's tool works at half speed for a moment
    Shove,         // Frontend: dashes into the CEO's hand and knocks bills loose (Gloves resist)
    Popup,         // Frontend: a pop-up window covers part of the floor (click it to close, Fan clears it)
    Yoink,         // Marketing: snatches bills out of the CEO's hand (Gloves resist)
    Magnet,        // Marketing: pulls loose bills toward the Marketing crate (Paperweight pins bills)
    Confetti       // Marketing: a confetti cannon blasts bills away from the CEO's hand
}

/// <summary>Things that happened in the simulation this step (the view turns them into sounds and effects).</summary>
public enum CashEventType
{
    TubePop,
    BillLand,
    Grab,
    GrabMany,
    StackTick,
    Throw,
    DepositBill,      // one bill landed in the safe (value)
    DepositBatch,     // a batch finished landing (value, count)
    Swish,            // a thrown bill landed in the safe
    RakeSweep,
    VacuumSuck,
    Jam,
    DeptGrab,
    DeptDeposit,      // a department banked bills (value, dept)
    DeptLevelUp,
    DeptJoined,
    Telegraph,        // a worker is about to interfere ("!")
    Interference,     // an interference started (tier = CashInterference)
    InterferenceEnd,
    Steal,            // bills taken out of the CEO's hand (count, value)
    Drop,             // bills knocked loose (count)
    Blocked,          // Gloves blocked a shove / yoink
    Stun,             // a worker was stapled
    Miss,             // a gadget found nothing to work on
    PaperweightPlaced,
    FanBlast,
    PopupClosed,
    ComboTier,        // the deposit streak reached a new tier (tier)
    Denied,           // can't pick up here (firewall / pop-up / rate limit)
    Payday,           // the day's settlement poured extra money out of the tubes (value)
    Seized,           // the company account ran short and took money from the floor / the CEO (value)

    // ---- Added 2026-11-24 with the design canvas (hands, drops, boosts, the stamp) ----
    PokerHand,        // bare hands banked five bills as a poker hand (tier = CashPoker.Rank, value = bonus paid, count = multiplier x10)
    BonusPaid,        // a boost or a special bill paid a bonus from the company account (value, tier = CashBonusSource flags)
    BoostStarted,     // PAYDAY / JACKPOT / HAPPY HOUR began (tier = CashBoost)
    BoostEnded,
    ReelSpin,         // the hopper's reels turned (count = the three symbols packed: a*100 + b*10 + c)
    SpecialSpawned,   // a golden or marked bill came out of a tube (tier = CashSpecial)
    MarkedBanked,     // marked bills reached the safe (count): the system adds Heat
    WrittenUp,        // the CEO stamped a worker caught mid-foul (dept, value = fine moved to Player Cash)
    StampMiss         // the stamp found no foul (tier 1 = the worker wasn't fouling, 0 = still inked)
}

/// <summary>A special bill (rolled when it leaves a tube). Not saved: a loaded floor comes back plain.</summary>
public enum CashSpecial : byte
{
    None = 0,
    Golden,          // shiny: banked, it pays a bonus (CashRules.GoldenBonus)
    Marked           // red-dyed: pays triple, but every one banked adds Heat
}

/// <summary>A short multiplier on everything the CEO banks.</summary>
public enum CashBoost
{
    None = 0,
    Payday,          // the day's payday is pouring
    Jackpot,         // the hopper's reels hit 7-7-7
    HappyHour        // once a day, for a few seconds
}

/// <summary>Why a bonus was paid (flags, the view words the floating number with them).</summary>
[Flags]
public enum CashBonusSource
{
    None = 0,
    Boost = 1,
    Golden = 2,
    Marked = 4
}

/// <summary>The weekly Count's perks (the winner picks one; it lasts until the next Count). Saved by number.</summary>
public enum CashPerk
{
    None = 0,
    WideSafe = 1,     // the safe takes deposits from farther away
    LuckyReels = 2,   // the first reel stays on 7
    LongLunch = 3     // every department works slower
}

[Serializable]
public struct CashEvent
{
    public CashEventType type;
    public Vector2 pos;
    public double value;
    public int count;
    public int dept;
    public int tier;

    public CashEvent(CashEventType type, Vector2 pos, double value = 0.0, int count = 0, int dept = -1, int tier = 0)
    {
        this.type = type;
        this.pos = pos;
        this.value = value;
        this.count = count;
        this.dept = dept;
        this.tier = tier;
    }
}

/// <summary>
/// Where the simulation's money goes when a bill lands somewhere. CashLedger implements it: a bill in the safe becomes
/// Player Cash, a bill in a crate becomes that department's budget. Nothing else moves money out of the floor.
/// </summary>
public interface ICashSink
{
    void DepositPlayer(double value);
    void DepositDept(int dept, double value);
    /// <summary>A bonus for the CEO, moved from the company account into Player Cash (company value stays). Returns what was paid.</summary>
    double PayBonus(double amount);
    /// <summary>A written-up department pays a share of its budget into Player Cash (company value stays). Returns the fine.</summary>
    double FineDept(int dept, double share);
}

/// <summary>
/// The company's books as the Cash Floor sees them: the company value (everything the company owns) and a way to change
/// it. CompanyManager in the game; a plain number in the tests.
/// </summary>
public interface ICashBooks
{
    double Value { get; }
    void AddValue(double delta, bool instant);
}
