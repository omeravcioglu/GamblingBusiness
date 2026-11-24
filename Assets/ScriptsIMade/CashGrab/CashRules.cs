using System;
using UnityEngine;

/// <summary>
/// The Cash Floor's numbers: tools, gadgets, departments, prices and unlocks. Change balance here and nowhere else.
/// Prices are paid from Player Cash (CashLedger.SpendPlayer). Tools unlock with office space (the HQ campus's seats,
/// TowerBuilder.TotalCapacity): office expansion opens bigger collection equipment, a bigger floor and more tubes.
/// </summary>
public static class CashRules
{
    public const int ToolCount = 6;
    public const int GadgetCount = 4;
    public const int MaxToolLevel = 3;
    public const int MaxGadgetLevel = 2;
    public const int MaxDeptLevel = 5;

    // ------------------------------------------------------------------
    // Tools
    // ------------------------------------------------------------------

    /// <summary>Price of each tool level (index 0 = buying it, 1 = level 2, 2 = level 3).</summary>
    static readonly double[][] ToolPrices =
    {
        new double[] { 0 },                                   // Hands: free, no upgrades
        new double[] { 1000, 4000, 15000 },                   // Scoop
        new double[] { 8000, 30000, 100000 },                 // Rake
        new double[] { 40000, 150000, 500000 },               // Tray
        new double[] { 200000, 750000, 2500000 },             // Vacuum
        new double[] { 1500000, 6000000, 25000000 }           // Industrial
    };

    /// <summary>Office seats (HQ capacity) a tool needs before it can be bought.</summary>
    static readonly int[] ToolSeats = { 0, 0, 12, 24, 45, 90 };

    public static int MaxLevel(CashTool tool) => tool == CashTool.Hands ? 1 : MaxToolLevel;

    /// <summary>Price of the next level of a tool owned at <paramref name="level"/> (0 = not owned); -1 at the top.</summary>
    public static double ToolPrice(CashTool tool, int level)
    {
        double[] prices = ToolPrices[(int)tool];
        return level >= 0 && level < prices.Length ? prices[level] : -1.0;
    }

    public static int ToolSeatsNeeded(CashTool tool) => ToolSeats[(int)tool];

    // Per-level numbers (level 1..3; index level-1)
    public static float ScoopRadius(int level) => Pick(level, 62f, 78f, 96f);
    public static int ScoopCapacity(int level) => (int)Pick(level, 6f, 10f, 16f);
    public static float RakeLength(int level) => Pick(level, 190f, 240f, 300f);
    public static float TrayRadius(int level) => Pick(level, 84f, 104f, 128f);
    public static int TrayCapacity(int level) => (int)Pick(level, 24f, 40f, 64f);
    public static float TrayPickRate(int level) => Pick(level, 28f, 42f, 60f);   // bills per second sliding onto the tray
    public static float VacuumRadius(int level) => Pick(level, 150f, 185f, 225f);
    public static float VacuumForce(int level) => Pick(level, 1300f, 1600f, 1950f);
    public static float VacuumHeatRate(int level) => Pick(level, 0.22f, 0.15f, 0.1f); // share of the heat bar per second of suction
    public static float IndustrialRadius(int level) => Pick(level, 260f, 320f, 390f);
    public static float IndustrialForce(int level) => Pick(level, 2200f, 2700f, 3300f);

    // ------------------------------------------------------------------
    // Gadgets
    // ------------------------------------------------------------------

    static readonly double[][] GadgetPrices =
    {
        new double[] { 2500, 12000 },        // Stapler
        new double[] { 8000, 40000 },        // Paperweight
        new double[] { 40000, 180000 },      // Fan
        new double[] { 20000, 90000 }        // Gloves
    };

    public static double GadgetPrice(CashGadget gadget, int level)
    {
        if (gadget == CashGadget.None) return -1.0;
        double[] prices = GadgetPrices[(int)gadget];
        return level >= 0 && level < prices.Length ? prices[level] : -1.0;
    }

    public static float StaplerStun(int level) => level >= 2 ? 4.5f : 3f;
    public static float StaplerCooldown(int level) => level >= 2 ? 5f : 7f;
    public static float PaperweightRadius(int level) => level >= 2 ? 125f : 100f;
    public static float PaperweightDuration(int level) => level >= 2 ? 15f : 10f;
    public const float PaperweightCooldown = 14f;
    public static float FanRadius(int level) => level >= 2 ? 380f : 300f;
    public static float FanCooldown(int level) => level >= 2 ? 9f : 12f;
    public static float GlovesResist(int level) => level >= 2 ? 1f : level >= 1 ? 0.6f : 0f;

    // ------------------------------------------------------------------
    // Departments
    // ------------------------------------------------------------------

    // The design canvas's owner colours (2026-11-24): gold is the CEO's alone, so Marketing is orange
    public static readonly Color[] DeptColors =
    {
        UIColors.Hex(0x2B5B8E),   // Backend: blue
        UIColors.Hex(0xF4A3C6),   // Frontend: pink
        UIColors.Hex(0xF0693A)    // Marketing: orange
    };

    public static readonly Color[] DeptSoftColors =
    {
        UIColors.Hex(0xBFE3F5),
        UIColors.Hex(0xFFD6E6),
        UIColors.Hex(0xFFC9A8)
    };

    /// <summary>Text on a department's colour: paper on Backend's dark blue, ink on the others.</summary>
    public static Color DeptInk(int dept) => dept == 0 ? UIColors.PaperHi : UIColors.Ink;

    /// <summary>The CEO's colour (gold) wherever the four sides are compared.</summary>
    public static readonly Color PlayerColor = UIColors.Hex(0xE2B13C);

    public static readonly string[] DeptIds = { "backend", "frontend", "marketing" };
    public static readonly string[] DeptSprites = { "dept_backend", "dept_frontend", "dept_marketing" };

    /// <summary>Workers on the floor at each level (1..5).</summary>
    public static int AgentsAt(int level) => level <= 0 ? 0 : level >= 5 ? 3 : level >= 3 ? 2 : 1;

    public static float AgentSpeed(CashDept dept, int level)
    {
        int l = Mathf.Clamp(level, 1, MaxDeptLevel) - 1;
        switch (dept)
        {
            case CashDept.Backend: return 140f + 25f * l;
            case CashDept.Frontend: return 215f + 30f * l;
            default: return 170f + 20f * l;
        }
    }

    public static int AgentCapacity(CashDept dept, int level)
    {
        int l = Mathf.Clamp(level, 1, MaxDeptLevel);
        switch (dept)
        {
            case CashDept.Backend: return 2 + l;          // 3..7: patient crawlers
            case CashDept.Frontend: return 1 + (l + 1) / 2; // 2..4: quick snipers
            default: return 2 + l;                        // 3..7
        }
    }

    public static float AgentGrabTime(CashDept dept)
    {
        switch (dept)
        {
            case CashDept.Backend: return 0.2f;
            case CashDept.Frontend: return 0.1f;
            default: return 0.15f;
        }
    }

    /// <summary>Level at which a department starts using an interference (int.MaxValue = never).</summary>
    public static int InterferenceLevel(CashDept dept, CashInterference kind)
    {
        switch (dept)
        {
            case CashDept.Backend:
                if (kind == CashInterference.Firewall) return 2;
                if (kind == CashInterference.RateLimit) return 4;
                break;
            case CashDept.Frontend:
                if (kind == CashInterference.Shove) return 2;
                if (kind == CashInterference.Popup) return 4;
                break;
            case CashDept.Marketing:
                if (kind == CashInterference.Yoink) return 2;
                if (kind == CashInterference.Magnet) return 3;
                if (kind == CashInterference.Confetti) return 5;
                break;
        }
        return int.MaxValue;
    }

    /// <summary>Seconds between two interferences of one department at a level (busier as it grows).</summary>
    public static float InterferenceCooldown(int level) => Mathf.Lerp(16f, 9f, (Mathf.Clamp(level, 2, MaxDeptLevel) - 2) / 3f);

    /// <summary>
    /// What the next level (2..5) costs, from the department's budget. Scaled to the company's day income so a department
    /// that grabs a fair share of the money keeps climbing at a steady pace whatever the company earns.
    /// </summary>
    public static double LevelCost(int nextLevel, double dayIncome)
    {
        // A department that grabs ~15% of the money reaches level 2 in about 4 days, 3 in ~10 more, 4 in ~23 more, 5 in ~50 more
        double reference = Math.Max(2000.0, dayIncome);
        switch (nextLevel)
        {
            case 2: return Math.Max(1500.0, reference * 0.6);
            case 3: return Math.Max(6000.0, reference * 1.5);
            case 4: return Math.Max(25000.0, reference * 3.5);
            case 5: return Math.Max(100000.0, reference * 8.0);
            default: return double.PositiveInfinity;
        }
    }

    /// <summary>
    /// Share of each day's profit the company keeps in its own account (retained earnings: it pays the company's bills and
    /// takes the hits of risk cards, fines and bad days). The rest arrives on the cash floor as money to grab.
    /// </summary>
    public const double RetainedShare = 0.2;

    /// <summary>The department's company-wide perk at a level: Backend +3% productivity, Frontend +0.4 happiness a day,
    /// Marketing +0.4 investor trust a day, per level.</summary>
    public static float PerkPerLevel(CashDept dept)
    {
        return dept == CashDept.Backend ? 0.03f : 0.4f;
    }

    // ---- When each department shows up (gradual: one at a time) ----

    public static int JoinDay(CashDept dept)
    {
        bool demo = DemoMode.Active;
        switch (dept)
        {
            case CashDept.Backend: return demo ? 2 : 3;
            case CashDept.Frontend: return demo ? 4 : 6;
            default: return demo ? 6 : 9;
        }
    }

    /// <summary>Joining earlier: Backend after the CEO's first few deposits (on day 2 or later), the others at a headcount.</summary>
    public static int JoinHeadcount(CashDept dept)
    {
        switch (dept)
        {
            case CashDept.Backend: return int.MaxValue;
            case CashDept.Frontend: return 6;
            default: return 12;
        }
    }

    public const int BackendJoinDeposits = 4;

    // ------------------------------------------------------------------
    // Floor, tubes, stream
    // ------------------------------------------------------------------

    /// <summary>The floor's base size in world units (1 unit = 1 px of the 1920 x 1080 frame at 1x zoom).</summary>
    public static readonly Vector2 BaseFloorSize = new Vector2(1400f, 880f);

    /// <summary>Floor size step by office seats: more space for more money (the view zooms out to fit).</summary>
    public static float FloorScale(int seats)
    {
        if (seats >= 150) return 1.6f;
        if (seats >= 90) return 1.45f;
        if (seats >= 45) return 1.3f;
        if (seats >= 24) return 1.15f;
        return 1f;
    }

    /// <summary>Delivery tubes by office seats (1 to 6).</summary>
    public static int TubeCount(int seats)
    {
        if (seats >= 120) return 6;
        if (seats >= 70) return 5;
        if (seats >= 40) return 4;
        if (seats >= 20) return 3;
        if (seats >= 10) return 2;
        return 1;
    }

    /// <summary>Items a second the stream aims for (more tubes, a busier floor).</summary>
    public static float ItemsPerSecond(int tubes) => 3.2f + 0.6f * Mathf.Max(0, tubes - 1);

    /// <summary>Most items on the floor at once; past it the stream switches to bigger denominations.</summary>
    public const int SoftItemCap = 420;

    // ------------------------------------------------------------------
    // Bare hands: a hand of five bills, banked as a poker hand
    // ------------------------------------------------------------------

    /// <summary>Bills bare hands can hold (a hand of cards).</summary>
    public const int HandsCapacity = 5;
    /// <summary>Seconds between two pinches while the button is held and the hand passes over bills.</summary>
    public const float HandsPinchInterval = 0.09f;
    /// <summary>A poker hand's bonus is figured on at least this many seconds of the Drop, so hands stay worth playing late.</summary>
    public const double PokerBaseSeconds = 2.0;

    // ------------------------------------------------------------------
    // Special bills, boosts and the reels
    // ------------------------------------------------------------------

    /// <summary>Chance that a bill from a tube is golden / marked (marked bills from day 4).</summary>
    public const float GoldenChance = 1f / 70f;
    public const float MarkedChance = 1f / 45f;
    public const int MarkedFromDay = 4;
    /// <summary>A golden bill pays 10x its value, figured on at least 4 seconds of the Drop.</summary>
    public const double GoldenMultiplier = 10.0;
    public const double GoldenBaseSeconds = 4.0;
    /// <summary>A marked bill pays 3x, and every one banked adds this much Heat (0-100).</summary>
    public const double MarkedMultiplier = 3.0;
    public const float MarkedHeat = 1.5f;

    public static double BoostMultiplier(CashBoost boost)
    {
        switch (boost)
        {
            case CashBoost.Payday: return 2.0;
            case CashBoost.Jackpot: return 3.0;
            case CashBoost.HappyHour: return 1.5;
            default: return 1.0;
        }
    }

    public static float BoostSeconds(CashBoost boost)
    {
        switch (boost)
        {
            case CashBoost.Payday: return 10f;
            case CashBoost.Jackpot: return 8f;
            case CashBoost.HappyHour: return 8f;
            default: return 0f;
        }
    }

    /// <summary>The reels turn once every this many bills out of the tubes.</summary>
    public const int ReelEvery = 6;
    /// <summary>Symbols on a reel (0 is the 7) and the chance a reel stops on the 7.</summary>
    public const int ReelSymbols = 6;
    public const float SevenChance = 0.18f;
    /// <summary>Happy hour starts once a day, somewhere in this part of the day.</summary>
    public const float HappyHourFrom = 0.3f, HappyHourTo = 0.75f;

    /// <summary>Bonuses come out of the company account, but never below this many days of projected income (it pays the bills).</summary>
    public const double BonusReserveDays = 0.5;

    // ------------------------------------------------------------------
    // The Written up stamp (free from day 1: right-click a worker caught mid-foul)
    // ------------------------------------------------------------------

    public const float StampSitOut = 10f;
    public const double StampFineShare = 0.05;
    public const float StampCooldown = 6f;
    /// <summary>Stamping a worker who wasn't fouling wastes a little time (the stamp is for cheaters).</summary>
    public const float StampMissCooldown = 2.5f;

    // ------------------------------------------------------------------
    // The Count (every few days) and its perks
    // ------------------------------------------------------------------

    public const int CountEveryDays = 5;
    /// <summary>WideSafe: deposit reach and the safe's mouth this much wider.</summary>
    public const float WideSafeFactor = 1.3f;
    /// <summary>LongLunch: every department's workers at this speed.</summary>
    public const float LongLunchSpeed = 0.8f;
    /// <summary>A department that wins the Count works this much faster until the next one.</summary>
    public const float WinnerSpeed = 1.15f;
    /// <summary>Seconds the CEO has to pick a perk before the Count closes without one.</summary>
    public const float PerkPickSeconds = 25f;

    // ------------------------------------------------------------------
    // Hiring feeds the floor: each hire joins a department by major (or the CEO's side as an intern)
    // ------------------------------------------------------------------

    /// <summary>A department's workers move this much faster per hire it has, up to HireSpeedCap.</summary>
    public const float HireSpeedPerHire = 0.02f;
    public const float HireSpeedCap = 0.3f;
    /// <summary>Each intern makes the CEO's tools reach this much farther, up to InternReachCap.</summary>
    public const float InternReachPerHire = 0.02f;
    public const float InternReachCap = 0.2f;

    /// <summary>The department a major joins: -1 = none (an intern on the CEO's side). Matched on the English data.</summary>
    public static int DeptForMajor(string major)
    {
        if (string.IsNullOrWhiteSpace(major)) return -1;
        string m = major.ToLowerInvariant();
        string[] backend = { "comput", "engineer", "data", "math", "robot", "machine", "artificial", "quantum", "physics" };
        string[] marketing = { "business", "commun", "account", "econom", "market" };
        string[] frontend = { "psych", "english", "history", "neuro", "bio", "nurs", "design", "art" };
        foreach (string k in backend) if (m.Contains(k)) return 0;
        foreach (string k in marketing) if (m.Contains(k)) return 2;
        foreach (string k in frontend) if (m.Contains(k)) return 1;
        return -1;
    }

    static float Pick(int level, float l1, float l2, float l3)
    {
        return level >= 3 ? l3 : level == 2 ? l2 : l1;
    }
}
