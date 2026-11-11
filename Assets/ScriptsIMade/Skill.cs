using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class Skill
{
    /// <summary>The String Table with the skills' texts (Assets/Localization/Source/Skills.csv), keyed by skill id.</summary>
    public const string Table = "Skills";

    public string id;
    public string skillName;
    [TextArea(2, 4)]
    public string description;
    public SkillBranch branch;
    public int tier; // 1-5, determines position in tree
    public int cost = 1; // Skill points required
    public bool unlocked = false;
    
    // Prerequisites (skill IDs that must be unlocked first)
    public string[] prerequisites;
    
    // Effect values
    public SkillEffect effectType;
    public float effectValue;
    public float effectValue2; // Some skills need two values
    
    public Skill(string id, string name, string desc, SkillBranch branch, int tier, 
                 SkillEffect effect, float value, float value2 = 0f, params string[] prereqs)
    {
        this.id = id;
        this.skillName = name;
        this.description = desc;
        this.branch = branch;
        this.tier = tier;
        this.effectType = effect;
        this.effectValue = value;
        this.effectValue2 = value2;
        this.prerequisites = prereqs;
        this.cost = tier; // Higher tier = more expensive
    }

    // ------------------------------------------------------------------
    // Texts for display. skillName and description stay the English source (the skill board finds icons by skillName);
    // other languages come from the Skills table: "skill." + id + ".name" / ".effect" / ".quote". English always shows
    // the data itself, so an edit here shows at once (Tools > Game > Localization > Sync Content CSVs updates the table).
    // ------------------------------------------------------------------

    /// <summary>Key prefix of this skill's texts in the Skills table: "skill." + id.</summary>
    public string TextKey => "skill." + id;

    /// <summary>The English effect sentence: the description's first line.</summary>
    public string EnglishEffect
    {
        get
        {
            string d = description != null ? description.Trim() : "";
            int br = d.IndexOf('\n');
            return br >= 0 ? d.Substring(0, br).Trim() : d;
        }
    }

    /// <summary>The English quote without its quotation marks: the description's second line ("" when there is none).</summary>
    public string EnglishQuote
    {
        get
        {
            string d = description != null ? description.Trim() : "";
            int br = d.IndexOf('\n');
            return br < 0 ? "" : d.Substring(br + 1).Trim().Trim('"', '“', '”').Trim();
        }
    }

    /// <summary>The name in the current language, else the English skillName.</summary>
    public string DisplayName => Localized(".name", skillName);

    /// <summary>
    /// The effect sentence in the current language, else the English one. A translation whose numbers differ from the
    /// English sentence's (a balance change the table hasn't caught up with) shows the English instead.
    /// </summary>
    public string DisplayEffect
    {
        get
        {
            string english = EnglishEffect;
            string text = Localized(".effect", english);
            return ReferenceEquals(text, english) || SameNumbers(english, text) ? text : english;
        }
    }

    /// <summary>The quote in the current language, without quotation marks ("" when the skill has none).</summary>
    public string DisplayQuote
    {
        get
        {
            string english = EnglishQuote;
            return english.Length == 0 ? "" : Localized(".quote", english);
        }
    }

    /// <summary>
    /// The description in the current language, in the English shape: the effect, a line break and the quote in curly
    /// quotes (SkillText splits it there); only the effect when there is no quote.
    /// </summary>
    public string DisplayDescription
    {
        get
        {
            string quote = DisplayQuote;
            return quote.Length == 0 ? DisplayEffect : DisplayEffect + "\n“" + quote + "”";
        }
    }

    string Localized(string part, string english)
    {
        if (english == null) english = "";
        if (string.IsNullOrEmpty(id) || english.Length == 0 || L10n.CurrentCode == L10n.English) return english;
        return L10n.GetOr(Table, TextKey + part, english);
    }

    // Same numbers in both texts, in any order: "+10% daily profit." and "每日利润+10%。" both hold 10
    static bool SameNumbers(string a, string b)
    {
        List<string> x = Numbers(a), y = Numbers(b);
        if (x.Count != y.Count) return false;
        x.Sort(System.StringComparer.Ordinal);
        y.Sort(System.StringComparer.Ordinal);
        for (int i = 0; i < x.Count; i++)
        {
            if (x[i] != y[i]) return false;
        }
        return true;
    }

    static List<string> Numbers(string text)
    {
        var numbers = new List<string>();
        int i = 0;
        while (i < text.Length)
        {
            if (text[i] < '0' || text[i] > '9')
            {
                i++;
                continue;
            }
            int start = i;
            while (i < text.Length && text[i] >= '0' && text[i] <= '9') i++;
            numbers.Add(text.Substring(start, i - start));
        }
        return numbers;
    }
}

public enum SkillBranch
{
    Growth,      // Money and growth focused
    Productivity, // Efficiency and quality
    Chaos        // Random events and luck
}

public enum SkillEffect
{
    // Growth Branch
    DailyValueGrowth,          // +X% of the day's profit (not of the company value: that compounded out of control)
    SalaryDiscount,            // -X% on all salaries
    NegativeEventReduction,    // Negative events hurt X% less
    TaxOptimization,           // -X% daily salary costs
    UnlockStockMarket,         // Unlocks stock trading
    InvestorBonus,             // +X% when hiring good employees
    BankInterest,              // Interest skills: +X% of the day's profit, like DailyValueGrowth
    GoldenParachute,           // Bad risk cards cost X% less
    MergerBonus,               // +$X per employee per day
    MarketManipulation,        // Stock prices favor you
    DividendPayments,          // Earn X% dividends from investments
    RealEstateInvestment,      // +X% value from property holdings
    VentureCapital,            // Bonus cash from investors
    LoanAccess,                // Unlocks loan system
    AssetLiquidation,          // Get X% value when selling assets
    PriceGouging,              // Charge customers X% more
    MonopolyBonus,             // Bonus when market dominant
    InsiderTrading,            // Stock trades give X% bonus
    BailoutProtection,         // Government bailouts available
    CashbackRewards,           // Get X% back on purchases
    LoyaltyProgram,            // Repeat customers give bonus
    VolumeDiscount,            // Bulk purchases cost X% less
    TaxHaven,                  // Reduce taxes by X%
    RevenueSharing,            // Employees share in profits (cost reduction)
    ProfitMargin,              // Increase profit margins by X%
    CostCutting,               // Reduce all costs by X%
    AcquisitionBonus,          // Buying companies gives bonus
    IPOAccess,                 // Unlock IPO for big payout
    ExportBonus,               // International sales bonus
    FranchiseOpportunity,      // Unlock franchise system
    
    // Productivity Branch
    EmployeeProductivity,      // Employees produce +X% more
    ExperienceGrowth,          // Experience grows X% faster over days
    IQSynergy,                 // High IQ employees give X% bonus per IQ over 100
    UnlockRnD,                 // Unlocks R&D projects
    AutomationBots,            // Free workforce (X bots)
    TrainingProgram,           // New hires start with +X experience
    QualityControl,            // Bad employees hurt X% less
    InnovationBonus,           // PhD employees give +X% bonus
    TeamSynergy,               // +X% per Y employees (diminishing)
    AIAssistant,               // Reduces mini-game difficulty
    WorkFromHome,              // Remote work productivity boost
    FlexibleHours,             // Flexible schedule productivity bonus
    MentorshipProgram,         // New employees learn faster
    PerformanceReviews,        // Regular reviews boost performance
    SkillCertification,        // Certified employees get bonus
    LeanManagement,            // Efficiency from lean principles
    SixSigma,                  // Quality improvement bonus
    AgileMethodology,          // Faster project completion
    TimeTracking,              // Better time management
    ProjectManagement,         // Better project outcomes
    CollaborationTools,        // Team communication boost
    CommunicationBoost,        // Better internal communication
    StressReduction,           // Lower stress = higher productivity
    EmployeeRetention,         // Keep employees longer
    TalentScouting,            // Find better candidates
    HeadhunterBonus,           // Premium hires available
    RecruitingNetwork,         // Better hiring pool
    EmployeeReferral,          // Referrals give bonus
    CultureFit,                // Better team cohesion
    WorkspaceOptimization,     // Office improvements boost output
    
    // Chaos Branch
    GoodEventChance,           // Good events X% more often
    LuckyHireChance,           // X% chance hire becomes super employee
    DailyRandomBuff,           // Daily random buff OR nerf
    SkipBonus,                 // Skipped employees X% chance give bonus
    SalaryFluctuation,         // Salary costs fluctuate (can be lower)
    CriticalSuccess,           // X% chance for 2x rewards
    ChaoticEnergy,             // Random events more frequent but better rewards
    GamblersFallacy,           // After bad event, next is more likely good
    UnlockCasino,              // Unlocks casino
    WildCard,                  // Once per day, reroll any outcome
    DoubleOrNothing,           // Risk/reward mechanic
    JackpotChance,             // Small chance for huge bonus
    LuckyStreak,               // Consecutive wins give bonus
    UnluckyProtection,         // Reduce bad luck impact
    RandomRaise,               // Random salary bonuses
    SurpriseBonus,             // Unexpected bonuses appear
    MysteryBox,                // Random rewards from boxes
    LotteryTicket,             // Daily lottery chance
    CoinFlip,                  // 50/50 chance for bonus
    DiceRoll,                  // Roll for random effect
    TarotCard,                 // Mystical prediction bonus
    FortuneCookie,             // Daily fortune bonus
    LuckyCharm,                // Passive luck increase
    JinxRemoval,               // Remove negative effects
    Serendipity,               // Lucky coincidences happen
    KarmaBonus,                // Good karma rewards
    ReverseKarma,              // Bad things become good
    ProbabilityManipulation,   // Bend luck to your favor
    RealityWarp,               // Break normal rules
    QuantumLuck,               // Multiple outcomes exist
    
    // Special
    UnlockSecretBranch         // Secret ultimate skill
}

