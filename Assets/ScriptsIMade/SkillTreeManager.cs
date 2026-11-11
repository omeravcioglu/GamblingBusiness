using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// The skill tree: skill points, the skills and their effects. Every hiresPerSkillPoint hires with IQ iqThreshold+ earn a
/// point. totalHighIQHires is the net count (StaffLedger takes back the hire of someone fired within a few days), and a
/// point is paid when the count first reaches the next multiple (the high-water mark, saved under "skillHires"), so
/// hire-and-fire can't earn the same point twice. The count, points and skills themselves are in SaveData.
/// </summary>
public class SkillTreeManager : MonoBehaviour, ISaveParticipant
{
    public static SkillTreeManager Instance;

    /// <summary>Save key of the high-water mark of the high-IQ hire count (ISaveParticipant).</summary>
    public const string HiresSaveKey = "skillHires";

    [Header("Skill Points")]
    [Tooltip("Skill points a new company starts with (raise it for testing)")]
    public int startingSkillPoints = 0;
    [Tooltip("Live value: reset to Starting Skill Points when the game starts, then loaded from the save")]
    public int skillPoints = 0;
    [Tooltip("Live value: reset to 0 when the game starts, then loaded from the save")]
    public int totalHighIQHires = 0;
    [Tooltip("High-IQ hires per skill point (was 7: the whole tree took ~20 hours of hiring)")]
    public int hiresPerSkillPoint = 3;
    public int iqThreshold = 100;

    // Points the high-IQ hires have paid so far: the highest totalHighIQHires / hiresPerSkillPoint reached. A hire only
    // earns a point when the count climbs past it, so after take-backs the count has to pass it again before the next.
    private int highIQPointsAwarded;

    [Header("All Skills")]
    public List<Skill> allSkills = new List<Skill>();

    // Cached active effects
    private Dictionary<SkillEffect, float> activeEffects = new Dictionary<SkillEffect, float>();

    /// <summary>The hidden ultimate skill. It shows in the tree once every other skill is unlocked.</summary>
    public const string SecretSkillId = "secret_ultimate";

    // Corporate Enlightenment: every other effect x (1 + its value, at most +100%) while unlocked
    private const float MaxSecretBonus = 1f;
    private float effectMultiplier = 1f;
    private bool secretWasRevealed;

    // Events
    public delegate void OnSkillUnlocked(Skill skill);
    public event OnSkillUnlocked SkillUnlocked;

    public delegate void OnSkillPointsChanged(int points);
    public event OnSkillPointsChanged SkillPointsChanged;

    void Awake()
    {
        Instance = this;

        // The scene still stores old test values for these; a new company always starts fresh.
        // SaveSystem loads the real values after this. Playtest builds start with plenty (PlaytestMode).
        skillPoints = Mathf.Max(0, PlaytestMode.Active ? Mathf.Max(startingSkillPoints, PlaytestMode.SkillPoints) : startingSkillPoints);
        if (DemoMode.Active)
        {
            // Demo: points come faster (over the scene's value), and the first ones are the Board's gift on the first hire
            hiresPerSkillPoint = DemoMode.Config.hiresPerSkillPoint;
            skillPoints = Mathf.Max(0, startingSkillPoints);
        }
        totalHighIQHires = 0;
        highIQPointsAwarded = 0;
        knowledge = 0f;   // HQ campus knowledge (AddKnowledge)

        InitializeSkillTree();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    int HiresPerPoint => Mathf.Max(1, hiresPerSkillPoint);

    void InitializeSkillTree()
    {
        allSkills.Clear();
        AddDefaultSkills(allSkills);
        UpdateActiveEffects();
    }

    /// <summary>
    /// Every skill as defined in code, in tree order (a fresh copy). The Editor's SkillsTextSource writes the Skills
    /// translation table (Assets/Localization/Source/Skills.csv) from it.
    /// </summary>
    public static List<Skill> CreateDefaultSkills()
    {
        var skills = new List<Skill>();
        AddDefaultSkills(skills);
        return skills;
    }

    // The skills. Their texts in other languages are in the Skills String Table, keyed by id (Skill.DisplayName etc.)
    static void AddDefaultSkills(List<Skill> allSkills)
    {
        // ========== GROWTH BRANCH (Money & Investors) ==========
        
        // Tier 1 (5 skills)
        allSkills.Add(new Skill(
            "growth_1_investor", "Investor Confidence", 
            "+10% daily profit.\n\"They believe in us... for now.\"",
            SkillBranch.Growth, 1, SkillEffect.DailyValueGrowth, 0.10f));

        allSkills.Add(new Skill(
            "growth_1_salary", "Salary Negotiation", 
            "All new hires accept 5% lower salary.\n\"We have great culture... and free coffee.\"",
            SkillBranch.Growth, 1, SkillEffect.SalaryDiscount, 0.05f));

        allSkills.Add(new Skill(
            "growth_1_cashback", "Budget Optimization", 
            "+6% daily profit.\n\"Every dollar counts.\"",
            SkillBranch.Growth, 1, SkillEffect.DailyValueGrowth, 0.06f));

        allSkills.Add(new Skill(
            "growth_1_loyalty", "Employee Retention Bonus", 
            "+8% daily profit.\n\"Happy employees, happy company.\"",
            SkillBranch.Growth, 1, SkillEffect.DailyValueGrowth, 0.08f));

        allSkills.Add(new Skill(
            "growth_1_volume", "Operating Efficiency", 
            "+5% daily profit.\n\"Streamline operations.\"",
            SkillBranch.Growth, 1, SkillEffect.DailyValueGrowth, 0.05f));

        // Tier 2 (5 skills)
        allSkills.Add(new Skill(
            "growth_2_pr", "Public Relations", 
            "Negative events hurt 20% less.\n\"No comment.\"",
            SkillBranch.Growth, 2, SkillEffect.NegativeEventReduction, 0.20f,
            0f, "growth_1_investor"));

        allSkills.Add(new Skill(
            "growth_2_tax", "Tax Optimization", 
            "Daily salary costs reduced by 5%.\n\"It's not evasion, it's optimization!\"",
            SkillBranch.Growth, 2, SkillEffect.TaxOptimization, 0.05f,
            0f, "growth_1_salary"));

        allSkills.Add(new Skill(
            "growth_2_profit", "Increased Revenue", 
            "+12% daily profit.\n\"More money in our pockets.\"",
            SkillBranch.Growth, 2, SkillEffect.DailyValueGrowth, 0.12f,
            0f, "growth_1_cashback"));

        allSkills.Add(new Skill(
            "growth_2_cost", "Overhead Reduction", 
            "+10% daily profit.\n\"Trim the fat.\"",
            SkillBranch.Growth, 2, SkillEffect.DailyValueGrowth, 0.10f,
            0f, "growth_1_loyalty"));

        allSkills.Add(new Skill(
            "growth_2_revenue", "Salary Optimization", 
            "Reduce salary costs by 4% through profit sharing.\n\"We're all in this together.\"",
            SkillBranch.Growth, 2, SkillEffect.TaxOptimization, 0.04f,
            0f, "growth_1_volume"));

        // Tier 3 (5 skills)
        allSkills.Add(new Skill(
            "growth_3_stocks", "Stock Market Access", 
            "Unlocks stock trading.\n\"Time to become a real corporation.\"",
            SkillBranch.Growth, 3, SkillEffect.UnlockStockMarket, 1f,
            0f, "growth_2_pr", "growth_2_tax"));

        allSkills.Add(new Skill(
            "growth_3_interest", "Corporate Savings Account", 
            "Your reserves earn interest: +10% daily profit.\n\"Money makes money.\"",
            SkillBranch.Growth, 3, SkillEffect.BankInterest, 0.10f,
            0f, "growth_2_tax", "growth_2_profit"));

        allSkills.Add(new Skill(
            "growth_3_venture", "Investor Injection", 
            "+15% daily profit.\n\"Show me the money!\"",
            SkillBranch.Growth, 3, SkillEffect.DailyValueGrowth, 0.15f,
            0f, "growth_2_cost"));

        allSkills.Add(new Skill(
            "growth_3_loan", "Financial Leverage", 
            "+12% daily profit.\n\"Money makes money.\"",
            SkillBranch.Growth, 3, SkillEffect.DailyValueGrowth, 0.12f,
            0f, "growth_2_revenue"));

        allSkills.Add(new Skill(
            "growth_3_dividend", "Investment Returns", 
            "Your investments pay out: +15% daily profit.\n\"Passive income is best income.\"",
            SkillBranch.Growth, 3, SkillEffect.BankInterest, 0.15f,
            0f, "growth_2_pr"));

        // Tier 4 (5 skills)
        allSkills.Add(new Skill(
            "growth_4_parachute", "Golden Parachute", 
            "Bad risk cards cost 30% less.\n\"Always have an exit strategy.\"",
            SkillBranch.Growth, 4, SkillEffect.GoldenParachute, 0.30f,
            0f, "growth_3_stocks"));

        allSkills.Add(new Skill(
            "growth_4_merger", "Merger Mentality", 
            "+$500 per employee, every day.\n\"Bigger is better.\"",
            SkillBranch.Growth, 4, SkillEffect.MergerBonus, 500f,
            0f, "growth_3_interest"));

        allSkills.Add(new Skill(
            "growth_4_insider", "Stock Market Expertise", 
            "+18% daily profit.\n\"We know the market.\"",
            SkillBranch.Growth, 4, SkillEffect.DailyValueGrowth, 0.18f,
            0f, "growth_3_venture"));

        allSkills.Add(new Skill(
            "growth_4_realestate", "Asset Appreciation", 
            "+20% daily profit.\n\"Assets grow over time.\"",
            SkillBranch.Growth, 4, SkillEffect.DailyValueGrowth, 0.20f,
            0f, "growth_3_loan"));

        allSkills.Add(new Skill(
            "growth_4_taxhaven", "Advanced Tax Strategy", 
            "Reduce salary costs by 8%.\n\"Offshore is onshore for us.\"",
            SkillBranch.Growth, 4, SkillEffect.TaxOptimization, 0.08f,
            0f, "growth_3_dividend"));

        // Tier 5 (5 skills)
        allSkills.Add(new Skill(
            "growth_5_manipulation", "Market Manipulation", 
            "Stock prices tend to favor your trades.\n\"Totally legal, probably.\"",
            SkillBranch.Growth, 5, SkillEffect.MarketManipulation, 0.15f,
            0f, "growth_4_parachute", "growth_4_merger"));

        allSkills.Add(new Skill(
            "growth_5_monopoly", "Market Dominance", 
            "+25% daily profit.\n\"We own the market.\"",
            SkillBranch.Growth, 5, SkillEffect.DailyValueGrowth, 0.25f,
            0f, "growth_4_insider"));

        allSkills.Add(new Skill(
            "growth_5_ipo", "Public Valuation Boost", 
            "+22% daily profit.\n\"Going public, going rich.\"",
            SkillBranch.Growth, 5, SkillEffect.DailyValueGrowth, 0.22f,
            0f, "growth_4_realestate"));

        allSkills.Add(new Skill(
            "growth_5_export", "Expansion Bonus", 
            "+28% daily profit.\n\"Global expansion pays off.\"",
            SkillBranch.Growth, 5, SkillEffect.DailyValueGrowth, 0.28f,
            0f, "growth_4_taxhaven"));

        allSkills.Add(new Skill(
            "growth_5_acquisition", "Growth Through Scale", 
            "+$750 per employee, every day.\n\"Bigger is better.\"",
            SkillBranch.Growth, 5, SkillEffect.MergerBonus, 750f,
            0f, "growth_4_parachute"));

        // Tier 6 (5 skills)
        allSkills.Add(new Skill(
            "growth_6_pricegouge", "Premium Positioning", 
            "+30% daily profit.\n\"Premium value, premium returns.\"",
            SkillBranch.Growth, 6, SkillEffect.DailyValueGrowth, 0.30f,
            0f, "growth_5_manipulation"));

        allSkills.Add(new Skill(
            "growth_6_liquidation", "Efficient Capital", 
            "+28% daily profit.\n\"Optimize every dollar.\"",
            SkillBranch.Growth, 6, SkillEffect.DailyValueGrowth, 0.28f,
            0f, "growth_5_monopoly"));

        allSkills.Add(new Skill(
            "growth_6_bailout", "Financial Safety Net", 
            "Bad risk cards cost 40% less.\n\"Too big to fail.\"",
            SkillBranch.Growth, 6, SkillEffect.GoldenParachute, 0.40f,
            0f, "growth_5_ipo"));

        allSkills.Add(new Skill(
            "growth_6_franchise", "Scalable Operations", 
            "+32% daily profit.\n\"Scale everywhere.\"",
            SkillBranch.Growth, 6, SkillEffect.DailyValueGrowth, 0.32f,
            0f, "growth_5_export"));

        allSkills.Add(new Skill(
            "growth_6_investorbonus", "Premium Talent Attraction", 
            "+35% daily profit.\n\"Premium talent, premium returns.\"",
            SkillBranch.Growth, 6, SkillEffect.DailyValueGrowth, 0.35f,
            0f, "growth_5_acquisition"));

        // ========== PRODUCTIVITY BRANCH (Efficiency & Quality) ==========

        // Tier 1 (5 skills)
        allSkills.Add(new Skill(
            "prod_1_efficiency", "Office Efficiency", 
            "All employees produce 10% more value.\n\"Standing desks for everyone!\"",
            SkillBranch.Productivity, 1, SkillEffect.EmployeeProductivity, 0.10f));

        allSkills.Add(new Skill(
            "prod_1_training", "Onboarding Program", 
            "New hires start with +2 years experience.\n\"Fake it till you make it.\"",
            SkillBranch.Productivity, 1, SkillEffect.TrainingProgram, 2f));

        allSkills.Add(new Skill(
            "prod_1_workspace", "Workspace Optimization", 
            "Office improvements boost output by 8%.\n\"Feng shui for productivity.\"",
            SkillBranch.Productivity, 1, SkillEffect.WorkspaceOptimization, 0.08f));

        allSkills.Add(new Skill(
            "prod_1_flexible", "Flexible Hours", 
            "Flexible schedules increase productivity by 6%.\n\"Work when you work best.\"",
            SkillBranch.Productivity, 1, SkillEffect.FlexibleHours, 0.06f));

        allSkills.Add(new Skill(
            "prod_1_remote", "Work From Home", 
            "Remote work productivity boost of 7%.\n\"Pajamas are professional.\"",
            SkillBranch.Productivity, 1, SkillEffect.WorkFromHome, 0.07f));

        // Tier 2 (5 skills)
        allSkills.Add(new Skill(
            "prod_2_experience", "Skill Development", 
            "Employee experience grows 5% daily.\n\"Learning never stops (mandatory).\"",
            SkillBranch.Productivity, 2, SkillEffect.ExperienceGrowth, 0.05f,
            0f, "prod_1_efficiency"));

        allSkills.Add(new Skill(
            "prod_2_quality", "Quality Control", 
            "Low performers hurt company 25% less.\n\"We'll fix them... eventually.\"",
            SkillBranch.Productivity, 2, SkillEffect.QualityControl, 0.25f,
            0f, "prod_1_training"));

        allSkills.Add(new Skill(
            "prod_2_mentor", "Mentorship Program", 
            "New employees learn 40% faster.\n\"Learn from the best.\"",
            SkillBranch.Productivity, 2, SkillEffect.MentorshipProgram, 0.40f,
            0f, "prod_1_workspace"));

        allSkills.Add(new Skill(
            "prod_2_performance", "Performance Reviews", 
            "Regular reviews boost performance by 12%.\n\"Feedback is a gift.\"",
            SkillBranch.Productivity, 2, SkillEffect.PerformanceReviews, 0.12f,
            0f, "prod_1_flexible"));

        allSkills.Add(new Skill(
            "prod_2_certification", "Skill Certification", 
            "Certified employees get +10% bonus.\n\"Papers prove competence.\"",
            SkillBranch.Productivity, 2, SkillEffect.SkillCertification, 0.10f,
            0f, "prod_1_remote"));

        // Tier 3 (5 skills)
        allSkills.Add(new Skill(
            "prod_3_iq", "IQ Synergy", 
            "+$50 daily per IQ point over 100.\n\"Big brain energy.\"",
            SkillBranch.Productivity, 3, SkillEffect.IQSynergy, 50f,
            0f, "prod_2_experience"));

        allSkills.Add(new Skill(
            "prod_3_phd", "PhD Appreciation", 
            "PhD employees give +15% bonus.\n\"Finally, those degrees pay off.\"",
            SkillBranch.Productivity, 3, SkillEffect.InnovationBonus, 0.15f,
            0f, "prod_2_quality"));

        allSkills.Add(new Skill(
            "prod_3_lean", "Lean Management", 
            "Efficiency from lean principles increases output by 10%.\n\"Eliminate waste.\"",
            SkillBranch.Productivity, 3, SkillEffect.LeanManagement, 0.10f,
            0f, "prod_2_mentor"));

        allSkills.Add(new Skill(
            "prod_3_sixsigma", "Six Sigma", 
            "Quality improvement gives +8% productivity.\n\"Perfection is possible.\"",
            SkillBranch.Productivity, 3, SkillEffect.SixSigma, 0.08f,
            0f, "prod_2_performance"));

        allSkills.Add(new Skill(
            "prod_3_agile", "Agile Methodology", 
            "Projects complete 15% faster (hiring tasks get 15% more time).\n\"Iterate and improve.\"",
            SkillBranch.Productivity, 3, SkillEffect.AgileMethodology, 0.15f,
            0f, "prod_2_certification"));

        // Tier 4 (5 skills)
        allSkills.Add(new Skill(
            "prod_4_rnd", "R&D Laboratory", 
            "Unlocks special research projects and +25% task rewards.\n\"Science! For profit!\"",
            SkillBranch.Productivity, 4, SkillEffect.UnlockRnD, 1f,
            0f, "prod_3_iq", "prod_3_phd"));

        allSkills.Add(new Skill(
            "prod_4_synergy", "Team Synergy", 
            "+1% value per 5 employees.\n\"Teamwork makes the dream work.\"",
            SkillBranch.Productivity, 4, SkillEffect.TeamSynergy, 0.01f,
            5f, "prod_3_iq"));

        allSkills.Add(new Skill(
            "prod_4_time", "Time Tracking", 
            "Better time management increases efficiency by 12%.\n\"Time is money.\"",
            SkillBranch.Productivity, 4, SkillEffect.TimeTracking, 0.12f,
            0f, "prod_3_lean"));

        allSkills.Add(new Skill(
            "prod_4_project", "Project Management", 
            "Better project outcomes: +10% task rewards, -10% task penalties.\n\"Plan the work, work the plan.\"",
            SkillBranch.Productivity, 4, SkillEffect.ProjectManagement, 0.10f,
            0f, "prod_3_sixsigma"));

        allSkills.Add(new Skill(
            "prod_4_collaboration", "Collaboration Tools", 
            "Team communication boost increases output by 9%.\n\"Together we achieve more.\"",
            SkillBranch.Productivity, 4, SkillEffect.CollaborationTools, 0.09f,
            0f, "prod_3_agile"));

        // Tier 5 (5 skills)
        allSkills.Add(new Skill(
            "prod_5_automation", "Automation Initiative", 
            "3 robot workers join (free workforce).\n\"They don't need coffee breaks.\"",
            SkillBranch.Productivity, 5, SkillEffect.AutomationBots, 3f,
            0f, "prod_4_rnd"));

        allSkills.Add(new Skill(
            "prod_5_ai", "AI Assistant", 
            "Mini-games become easier and give 25% more time.\n\"Let the AI handle it.\"",
            SkillBranch.Productivity, 5, SkillEffect.AIAssistant, 0.5f,
            0f, "prod_4_synergy"));

        allSkills.Add(new Skill(
            "prod_5_communication", "Communication Boost", 
            "Better internal communication increases efficiency by 11%.\n\"Clear talk, clear results.\"",
            SkillBranch.Productivity, 5, SkillEffect.CommunicationBoost, 0.11f,
            0f, "prod_4_time"));

        allSkills.Add(new Skill(
            "prod_5_stress", "Stress Reduction", 
            "Lower stress equals 8% higher productivity.\n\"Zen and the art of business.\"",
            SkillBranch.Productivity, 5, SkillEffect.StressReduction, 0.08f,
            0f, "prod_4_project"));

        allSkills.Add(new Skill(
            "prod_5_retention", "Employee Retention", 
            "Loyal staff: happiness drops 30% less.\n\"Loyalty pays.\"",
            SkillBranch.Productivity, 5, SkillEffect.EmployeeRetention, 0.30f,
            0f, "prod_4_collaboration"));

        // Tier 6 (5 skills)
        allSkills.Add(new Skill(
            "prod_6_talent", "Talent Scouting", 
            "Find better candidates (+10% quality, golden applicants twice as common).\n\"We only hire the best.\"",
            SkillBranch.Productivity, 6, SkillEffect.TalentScouting, 1f,
            0f, "prod_5_automation"));

        allSkills.Add(new Skill(
            "prod_6_headhunter", "Headhunter Bonus", 
            "Premium hires available, +20% quality.\n\"Elite talent only.\"",
            SkillBranch.Productivity, 6, SkillEffect.HeadhunterBonus, 0.20f,
            0f, "prod_5_ai"));

        allSkills.Add(new Skill(
            "prod_6_recruiting", "Recruiting Network", 
            "Access to better hiring pool, +15% candidate quality.\n\"Connections matter.\"",
            SkillBranch.Productivity, 6, SkillEffect.RecruitingNetwork, 0.15f,
            0f, "prod_5_communication"));

        allSkills.Add(new Skill(
            "prod_6_referral", "Employee Referral", 
            "Referrals give +12% productivity bonus.\n\"Friends work well together.\"",
            SkillBranch.Productivity, 6, SkillEffect.EmployeeReferral, 0.12f,
            0f, "prod_5_stress"));

        allSkills.Add(new Skill(
            "prod_6_culture", "Culture Fit", 
            "Better team cohesion increases output by 10%.\n\"We're family here.\"",
            SkillBranch.Productivity, 6, SkillEffect.CultureFit, 0.10f,
            0f, "prod_5_retention"));

        // ========== CHAOS BRANCH (Luck & Random) ==========

        // Tier 1 (5 skills)
        allSkills.Add(new Skill(
            "chaos_1_luck", "Beginner's Luck", 
            "Good events happen 15% more often.\n\"Fortune favors the bold.\"",
            SkillBranch.Chaos, 1, SkillEffect.GoodEventChance, 0.15f));

        allSkills.Add(new Skill(
            "chaos_1_skip", "Trash to Treasure", 
            "20% chance skipped applicants give $500.\n\"One man's trash...\"",
            SkillBranch.Chaos, 1, SkillEffect.SkipBonus, 0.20f,
            500f));

        allSkills.Add(new Skill(
            "chaos_1_charm", "Lucky Charm", 
            "Passive luck increase of 10%.\n\"This rabbit's foot works!\"",
            SkillBranch.Chaos, 1, SkillEffect.LuckyCharm, 0.10f));

        allSkills.Add(new Skill(
            "chaos_1_fortune", "Fortune Cookie", 
            "Daily fortune gives random bonus.\n\"Your lucky numbers are...\"",
            SkillBranch.Chaos, 1, SkillEffect.FortuneCookie, 1f));

        allSkills.Add(new Skill(
            "chaos_1_lottery", "Lottery Ticket", 
            "Daily lottery chance for cash prize.\n\"Feeling lucky?\"",
            SkillBranch.Chaos, 1, SkillEffect.LotteryTicket, 1f));

        // Tier 2 (5 skills)
        allSkills.Add(new Skill(
            "chaos_2_lucky", "Lucky Hire", 
            "5% chance any hire becomes golden.\n\"Sometimes you just get lucky.\"",
            SkillBranch.Chaos, 2, SkillEffect.LuckyHireChance, 0.05f,
            0f, "chaos_1_luck"));

        allSkills.Add(new Skill(
            "chaos_2_salary", "Salary Roulette", 
            "Salary costs fluctuate ±20% daily.\n\"Today's special: discount employees!\"",
            SkillBranch.Chaos, 2, SkillEffect.SalaryFluctuation, 0.20f,
            0f, "chaos_1_skip"));

        allSkills.Add(new Skill(
            "chaos_2_unlucky", "Unlucky Protection", 
            "Reduce bad luck impact by 25%.\n\"Not today, bad luck!\"",
            SkillBranch.Chaos, 2, SkillEffect.UnluckyProtection, 0.25f,
            0f, "chaos_1_charm"));

        allSkills.Add(new Skill(
            "chaos_2_coin", "Coin Flip", 
            "50/50 chance for 2x rewards from completed tasks and won crises.\n\"Heads or tails?\"",
            SkillBranch.Chaos, 2, SkillEffect.CoinFlip, 1f,
            0f, "chaos_1_fortune"));

        allSkills.Add(new Skill(
            "chaos_2_mystery", "Mystery Box", 
            "Random rewards from mystery boxes.\n\"What's inside?\"",
            SkillBranch.Chaos, 2, SkillEffect.MysteryBox, 1f,
            0f, "chaos_1_lottery"));

        // Tier 3 (5 skills)
        allSkills.Add(new Skill(
            "chaos_3_roulette", "Corporate Roulette", 
            "Daily random buff OR nerf to company.\n\"Spin the wheel of fortune!\"",
            SkillBranch.Chaos, 3, SkillEffect.DailyRandomBuff, 1f,
            0f, "chaos_2_lucky"));

        allSkills.Add(new Skill(
            "chaos_3_crit", "Critical Success", 
            "10% chance for double rewards.\n\"CRITICAL HIT!\"",
            SkillBranch.Chaos, 3, SkillEffect.CriticalSuccess, 0.10f,
            0f, "chaos_2_salary"));

        allSkills.Add(new Skill(
            "chaos_3_raise", "Random Raise", 
            "Random salary bonuses appear (+5-15%).\n\"Surprise money!\"",
            SkillBranch.Chaos, 3, SkillEffect.RandomRaise, 1f,
            0f, "chaos_2_unlucky"));

        allSkills.Add(new Skill(
            "chaos_3_dice", "Dice Roll", 
            "Roll for random effect each day.\n\"Roll the dice!\"",
            SkillBranch.Chaos, 3, SkillEffect.DiceRoll, 1f,
            0f, "chaos_2_coin"));

        allSkills.Add(new Skill(
            "chaos_3_tarot", "Tarot Card", 
            "Mystical prediction gives daily bonus.\n\"The cards reveal...\"",
            SkillBranch.Chaos, 3, SkillEffect.TarotCard, 1f,
            0f, "chaos_2_mystery"));

        // Tier 4 (5 skills)
        allSkills.Add(new Skill(
            "chaos_4_energy", "Chaotic Energy", 
            "Events more frequent, but 25% better rewards.\n\"Embrace the chaos.\"",
            SkillBranch.Chaos, 4, SkillEffect.ChaoticEnergy, 0.25f,
            0f, "chaos_3_roulette"));

        allSkills.Add(new Skill(
            "chaos_4_fallacy", "Gambler's Fallacy", 
            "After bad event, next is 40% more likely good.\n\"We're due for a win!\"",
            SkillBranch.Chaos, 4, SkillEffect.GamblersFallacy, 0.40f,
            0f, "chaos_3_crit"));

        allSkills.Add(new Skill(
            "chaos_4_surprise", "Surprise Bonus", 
            "Unexpected bonuses appear randomly.\n\"Just when you need it!\"",
            SkillBranch.Chaos, 4, SkillEffect.SurpriseBonus, 1f,
            0f, "chaos_3_raise"));

        allSkills.Add(new Skill(
            "chaos_4_streak", "Lucky Streak", 
            "Consecutive good risk cards give +15% each (up to +45%).\n\"On a roll!\"",
            SkillBranch.Chaos, 4, SkillEffect.LuckyStreak, 0.15f,
            0f, "chaos_3_dice"));

        allSkills.Add(new Skill(
            "chaos_4_jinx", "Jinx Removal", 
            "Remove negative effects 30% of the time.\n\"Bad vibes begone!\"",
            SkillBranch.Chaos, 4, SkillEffect.JinxRemoval, 0.30f,
            0f, "chaos_3_tarot"));

        // Tier 5 (5 skills)
        allSkills.Add(new Skill(
            "chaos_5_casino", "Casino License", 
            "Unlocks the company casino.\n\"What could go wrong?\"",
            SkillBranch.Chaos, 5, SkillEffect.UnlockCasino, 1f,
            0f, "chaos_4_energy"));

        allSkills.Add(new Skill(
            "chaos_5_wildcard", "Wild Card", 
            "Once per day, reroll any bad outcome.\n\"Mulligan!\"",
            SkillBranch.Chaos, 5, SkillEffect.WildCard, 1f,
            0f, "chaos_4_fallacy"));

        allSkills.Add(new Skill(
            "chaos_5_double", "Double or Nothing", 
            "Risk/reward mechanic: 50% chance for 2x or 0x.\n\"All in!\"",
            SkillBranch.Chaos, 5, SkillEffect.DoubleOrNothing, 1f,
            0f, "chaos_4_surprise"));

        allSkills.Add(new Skill(
            "chaos_5_jackpot", "Jackpot Chance", 
            "Small chance (2%) for huge bonus (10x).\n\"Life-changing money!\"",
            SkillBranch.Chaos, 5, SkillEffect.JackpotChance, 0.02f,
            0f, "chaos_4_streak"));

        allSkills.Add(new Skill(
            "chaos_5_serendipity", "Serendipity", 
            "Lucky coincidences happen 20% more often.\n\"Right place, right time.\"",
            SkillBranch.Chaos, 5, SkillEffect.Serendipity, 0.20f,
            0f, "chaos_4_jinx"));

        // Tier 6 (5 skills)
        allSkills.Add(new Skill(
            "chaos_6_karma", "Karma Bonus", 
            "Good karma rewards appear regularly.\n\"What goes around comes around.\"",
            SkillBranch.Chaos, 6, SkillEffect.KarmaBonus, 1f,
            0f, "chaos_5_casino"));

        allSkills.Add(new Skill(
            "chaos_6_reverse", "Reverse Karma", 
            "Bad things become good 25% of the time.\n\"Turn that frown upside down!\"",
            SkillBranch.Chaos, 6, SkillEffect.ReverseKarma, 0.25f,
            0f, "chaos_5_wildcard"));

        allSkills.Add(new Skill(
            "chaos_6_probability", "Probability Manipulation", 
            "Bend luck to your favor by 15%.\n\"I make my own luck.\"",
            SkillBranch.Chaos, 6, SkillEffect.ProbabilityManipulation, 0.15f,
            0f, "chaos_5_double"));

        allSkills.Add(new Skill(
            "chaos_6_reality", "Reality Warp", 
            "Break normal rules occasionally.\n\"The matrix has you.\"",
            SkillBranch.Chaos, 6, SkillEffect.RealityWarp, 1f,
            0f, "chaos_5_jackpot"));

        allSkills.Add(new Skill(
            "chaos_6_quantum", "Quantum Luck", 
            "Multiple outcomes exist simultaneously.\n\"All possibilities are real.\"",
            SkillBranch.Chaos, 6, SkillEffect.QuantumLuck, 1f,
            0f, "chaos_5_serendipity"));

        // ========== SECRET SKILL ==========
        allSkills.Add(new Skill(
            "secret_ultimate", "Corporate Enlightenment", 
            "All effects +25%. You've seen it all.\n\"Is this what success feels like?\"",
            SkillBranch.Growth, 6, SkillEffect.UnlockSecretBranch, 0.25f,
            0f, "growth_6_pricegouge", "prod_6_talent", "chaos_6_karma"));
    }

    public void OnEmployeeHired(ApplicantData employee)
    {
        if (employee == null) return;

        // Demo: the Board's signing bonus on the run's first hire (saved, so Continue doesn't give it again)
        if (DemoMode.Active && DemoRun.Exists && !DemoRun.SkillGiftGiven)
        {
            DemoRun.SkillGiftGiven = true;
            int gift = DemoMode.Config.skillGiftPoints;
            GrantPoints(gift, DemoMode.T("demo.toast.skill_gift", ("count", gift)));
        }

        if (employee.IQ >= iqThreshold)
        {
            totalHighIQHires++;

            // A point when the count reaches a multiple it never reached before (one that already paid, reached again
            // after hires were taken back, pays nothing)
            int reached = totalHighIQHires / HiresPerPoint;
            if (reached > highIQPointsAwarded)
            {
                highIQPointsAwarded = reached;
                skillPoints++;
                Debug.Log("Skill point earned! Total: " + skillPoints);
                GameNotifications.Show(L10n.Get(Skill.Table, "skilltree.toast.point_earned", ("count", skillPoints)), NotificationKind.Special);
            }

            // Progress changed either way (the skill tree shows hires until the next point)
            SkillPointsChanged?.Invoke(skillPoints);
        }
    }

    // ========== DEMO ==========

    /// <summary>Demo: hand out skill points outside the hiring rule (the Board's signing bonus), with an optional toast.</summary>
    public void GrantPoints(int points, string toast)
    {
        if (points <= 0) return;
        skillPoints += points;
        SkillPointsChanged?.Invoke(skillPoints);
        if (!string.IsNullOrEmpty(toast)) GameNotifications.Show(toast, NotificationKind.Special);
    }

    /// <summary>
    /// Demo: the Board grants the casino skills (DemoMode.CasinoGrantIds, in tree order) at no cost. Saves keep them like
    /// any unlocked skill. Does nothing outside the demo or when they are all owned already.
    /// </summary>
    public void DemoGrantCasino()
    {
        if (!DemoMode.Active) return;
        var granted = new List<Skill>();
        foreach (string id in DemoMode.CasinoGrantIds)
        {
            Skill skill = GetSkill(id);
            if (skill == null || skill.unlocked) continue;
            skill.unlocked = true;
            granted.Add(skill);
        }
        if (granted.Count == 0) return;

        UpdateActiveEffects();
        foreach (Skill skill in granted) SkillUnlocked?.Invoke(skill);
        SkillPointsChanged?.Invoke(skillPoints);
        secretWasRevealed = IsSecretSkillRevealed();
        Debug.Log("[Demo] The Board granted: " + string.Join(", ", granted.Select(s => s.id)));
        GameNotifications.Show(DemoMode.T("demo.toast.casino_granted"), NotificationKind.Special);
    }

    /// <summary>
    /// Demo: a skill kept for the full game (above DemoConfig.maxSkillTier, listed in lockedSkills, or the secret one).
    /// Owned skills are never locked. Always false outside the demo.
    /// </summary>
    public bool IsDemoLocked(Skill skill)
    {
        if (!DemoMode.Active || skill == null || skill.unlocked) return false;
        DemoConfig config = DemoMode.Config;
        return skill.tier > config.maxSkillTier || skill.id == SecretSkillId ||
               (config.lockedSkills != null && System.Array.IndexOf(config.lockedSkills, skill.id) >= 0);
    }

    /// <summary>Demo: an owned skill above the demo's tiers that the Board granted (the casino chain). False outside the demo.</summary>
    public bool IsDemoGift(Skill skill)
    {
        if (!DemoMode.Active || skill == null || !skill.unlocked) return false;
        return skill.tier > DemoMode.Config.maxSkillTier && DemoMode.CasinoGrantIds.Contains(skill.id);
    }

    public bool CanUnlockSkill(Skill skill)
    {
        if (skill == null) return false;
        if (skill.unlocked) return false;
        if (IsDemoLocked(skill)) return false;   // Demo: kept for the full game
        if (skillPoints < skill.cost) return false;
        return ArePrerequisitesMet(skill);
    }

    /// <summary>True when every prerequisite of the skill is unlocked.</summary>
    public bool ArePrerequisitesMet(Skill skill)
    {
        if (skill == null) return false;
        if (skill.prerequisites == null) return true;

        foreach (string prereqId in skill.prerequisites)
        {
            Skill prereq = GetSkill(prereqId);
            if (prereq == null || !prereq.unlocked)
                return false;
        }

        return true;
    }

    public bool UnlockSkill(string skillId)
    {
        Skill skill = GetSkill(skillId);
        if (skill == null || !CanUnlockSkill(skill))
            return false;

        skill.unlocked = true;
        skillPoints = Mathf.Max(0, skillPoints - skill.cost);

        UpdateActiveEffects();

        SkillUnlocked?.Invoke(skill);
        SkillPointsChanged?.Invoke(skillPoints);

        Debug.Log("Unlocked skill: " + skill.skillName);
        GameNotifications.Show(L10n.Get(Skill.Table, "skilltree.toast.unlocked", ("skill", skill.DisplayName)), NotificationKind.Good);

        // Every other skill owned: the secret one appears in the tree
        if (!secretWasRevealed && IsSecretSkillRevealed())
        {
            secretWasRevealed = true;
            Skill secret = GetSkill(SecretSkillId);
            if (secret != null && !secret.unlocked)
                GameNotifications.Show(L10n.Get(Skill.Table, "skilltree.toast.secret", ("skill", secret.DisplayName)), NotificationKind.Special);
        }
        return true;
    }

    /// <summary>
    /// Restore a saved skill tree in one go: points, high-IQ hire count and exactly the listed skills
    /// unlocked (everything else locked). Refreshes effects and notifies listeners.
    /// Every multiple of hiresPerSkillPoint the saved count has passed counts as paid, so a save without the "skillHires"
    /// entry never hands out a surprise point; that entry (RestoreState, which SaveSystem calls after this) can only raise it.
    /// </summary>
    public void ApplySavedState(int points, int highIQHires, IEnumerable<string> unlockedSkillIds)
    {
        skillPoints = Mathf.Max(0, points);
        totalHighIQHires = Mathf.Max(0, highIQHires);
        highIQPointsAwarded = totalHighIQHires / HiresPerPoint;

        HashSet<string> unlockedIds = unlockedSkillIds != null ? new HashSet<string>(unlockedSkillIds) : new HashSet<string>();
        foreach (Skill skill in allSkills)
        {
            if (skill != null)
                skill.unlocked = skill.id != null && unlockedIds.Contains(skill.id);
        }

        UpdateActiveEffects();
        secretWasRevealed = IsSecretSkillRevealed();
        SkillPointsChanged?.Invoke(skillPoints);
    }

    /// <summary>
    /// True when the secret skill should be shown in the tree: it is unlocked, or every other skill is.
    /// </summary>
    public bool IsSecretSkillRevealed()
    {
        bool found = false;
        foreach (Skill skill in allSkills)
        {
            if (skill == null) continue;
            if (skill.id == SecretSkillId)
            {
                if (skill.unlocked) return true;
                found = true;
                continue;
            }
            if (!skill.unlocked) return false;
        }
        return found;
    }

    public Skill GetSkill(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        return allSkills.Find(s => s != null && s.id == id);
    }

    public List<Skill> GetSkillsByBranch(SkillBranch branch)
    {
        return allSkills.Where(s => s != null && s.branch == branch).OrderBy(s => s.tier).ToList();
    }

    public List<Skill> GetUnlockedSkills()
    {
        return allSkills.Where(s => s != null && s.unlocked).ToList();
    }

    public void UpdateActiveEffects()
    {
        activeEffects.Clear();

        foreach (var skill in allSkills.Where(s => s != null && s.unlocked))
        {
            if (activeEffects.ContainsKey(skill.effectType))
                activeEffects[skill.effectType] += skill.effectValue;
            else
                activeEffects[skill.effectType] = skill.effectValue;
        }

        // Corporate Enlightenment: "All effects +25%" (capped so an edited value can't run away)
        float secret;
        effectMultiplier = activeEffects.TryGetValue(SkillEffect.UnlockSecretBranch, out secret)
            ? 1f + Mathf.Clamp(secret, 0f, MaxSecretBonus)
            : 1f;
    }

    // ========== EFFECT GETTERS ==========

    /// <summary>
    /// Summed value of an effect over all unlocked skills (x1.25 with Corporate Enlightenment). Systems apply their
    /// own caps on top. Flag effects (unlocks) just need a value above 0: use HasEffect for those.
    /// </summary>
    public float GetEffect(SkillEffect effect)
    {
        float value;
        if (!activeEffects.TryGetValue(effect, out value)) return 0f;
        return effect == SkillEffect.UnlockSecretBranch ? value : value * effectMultiplier;
    }

    /// <summary>
    /// 1 normally, 1.25 with Corporate Enlightenment. For amounts that come from a skill's effectValue2
    /// or from a code constant instead of GetEffect (e.g. Trash to Treasure's $500).
    /// </summary>
    public float EffectMultiplier => effectMultiplier;

    public bool HasEffect(SkillEffect effect)
    {
        return activeEffects.ContainsKey(effect) && activeEffects[effect] > 0;
    }

    // Convenience methods for common checks
    public float GetDailyValueGrowth() => GetEffect(SkillEffect.DailyValueGrowth);
    public float GetSalaryDiscount() => GetEffect(SkillEffect.SalaryDiscount);
    public float GetNegativeEventReduction() => GetEffect(SkillEffect.NegativeEventReduction);
    public float GetTaxOptimization() => GetEffect(SkillEffect.TaxOptimization);
    public bool IsStockMarketUnlocked() => HasEffect(SkillEffect.UnlockStockMarket);
    public float GetEmployeeProductivity() => GetEffect(SkillEffect.EmployeeProductivity);
    public float GetIQSynergyBonus() => GetEffect(SkillEffect.IQSynergy);
    public bool IsRnDUnlocked() => HasEffect(SkillEffect.UnlockRnD);
    public int GetAutomationBots() => Mathf.RoundToInt(GetEffect(SkillEffect.AutomationBots));
    public float GetGoodEventChance() => GetEffect(SkillEffect.GoodEventChance);
    public float GetLuckyHireChance() => GetEffect(SkillEffect.LuckyHireChance);
    public bool IsCasinoUnlocked() => PlaytestMode.OpenCasino || HasEffect(SkillEffect.UnlockCasino);
    public float GetCriticalSuccessChance() => GetEffect(SkillEffect.CriticalSuccess);
    public bool HasDailyRandomBuff() => HasEffect(SkillEffect.DailyRandomBuff);
    public float GetSkipBonus() => GetEffect(SkillEffect.SkipBonus);

    /// <summary>Progress to the next skill point (0-1). 0 while the count is still below a multiple that already paid.</summary>
    public float GetSkillPointProgress()
    {
        return (float)HighIQHiresTowardNextPoint / HiresPerPoint;
    }

    /// <summary>Set skill points (e.g. from a save) and notify listeners.</summary>
    public void SetSkillPoints(int points)
    {
        skillPoints = Mathf.Max(0, points);
        SkillPointsChanged?.Invoke(skillPoints);
    }

    /// <summary>
    /// Set the high-IQ hire counter (e.g. a debug tool) and notify listeners. The multiples it has passed count as paid
    /// (nothing is awarded for them): the next point comes at the next multiple.
    /// </summary>
    public void SetHighIQHires(int count)
    {
        totalHighIQHires = Mathf.Max(0, count);
        highIQPointsAwarded = totalHighIQHires / HiresPerPoint;
        SkillPointsChanged?.Invoke(skillPoints);
    }

    /// <summary>
    /// Take one high-IQ hire back out of the count (StaffLedger: someone fired within a few days of being hired, so
    /// hire-and-fire can't farm points). Points already awarded stay; the next one needs one more hire, because a point
    /// is only paid when the count climbs past the highest multiple it has reached. Never below 0.
    /// Returns true if a hire was taken back.
    /// </summary>
    public bool TakeBackHighIQHire()
    {
        if (totalHighIQHires <= 0) return false;
        totalHighIQHires--;
        SkillPointsChanged?.Invoke(skillPoints);
        return true;
    }

    /// <summary>High-IQ hires still needed for the next skill point (hiresPerSkillPoint or more after take-backs).</summary>
    public int GetHiresUntilNextPoint()
    {
        return NextPointAt - totalHighIQHires;
    }

    /// <summary>
    /// High-IQ hires done toward the next point (0 to hiresPerSkillPoint - 1): what a progress bar shows. Stays 0 while
    /// the count is below a multiple that already paid (use this, not totalHighIQHires % hiresPerSkillPoint).
    /// </summary>
    public int HighIQHiresTowardNextPoint => Mathf.Max(0, HiresPerPoint - GetHiresUntilNextPoint());

    /// <summary>Points the high-IQ hires have paid so far (the high-water mark of totalHighIQHires / hiresPerSkillPoint).</summary>
    public int HighIQPointsAwarded => highIQPointsAwarded;

    // The count at which the next point is paid: the multiple after the highest one reached
    int NextPointAt => (Mathf.Max(highIQPointsAwarded, totalHighIQHires / HiresPerPoint) + 1) * HiresPerPoint;

    // ------------------------------------------------------------------
    // Knowledge (added 2026-09-29 for the HQ campus: the research lab and the training center earn skill-point progress
    // without hires; TowerBuilder adds it once a day and announces the points). Saved with the high-water mark below.
    // ------------------------------------------------------------------

    private float knowledge;

    /// <summary>Progress toward the next knowledge skill point (0-1).</summary>
    public float Knowledge => knowledge;

    /// <summary>
    /// Add knowledge: 0.05 = a twentieth of a skill point. Every whole point is paid into skillPoints at once and listeners
    /// hear SkillPointsChanged. Returns the points paid (0 most days). Shows nothing itself (the caller announces it).
    /// </summary>
    public int AddKnowledge(float amount)
    {
        if (float.IsNaN(amount) || float.IsInfinity(amount) || amount <= 0f) return 0;
        knowledge += amount;
        int points = Mathf.FloorToInt(knowledge + 0.0001f);
        if (points <= 0) return 0;
        knowledge = Mathf.Max(0f, knowledge - points);
        skillPoints += points;
        SkillPointsChanged?.Invoke(skillPoints);
        return points;
    }

    // ------------------------------------------------------------------
    // Save: the high-water mark ("skillHires"). SaveSystem restores it after ApplySavedState.
    // ------------------------------------------------------------------

    [System.Serializable]
    class SavedHires
    {
        public int version = 1;
        public int pointsAwarded;
        public int hiresPerPoint;
        public float knowledge;   // AddKnowledge's progress toward the next point (2026-09-29; missing in older saves = 0)
    }

    public string SaveKey => HiresSaveKey;

    public string CaptureState()
    {
        return JsonUtility.ToJson(new SavedHires { pointsAwarded = highIQPointsAwarded, hiresPerPoint = HiresPerPoint, knowledge = knowledge });
    }

    public void RestoreState(string json)
    {
        SavedHires data = null;
        if (!string.IsNullOrEmpty(json))
        {
            try { data = JsonUtility.FromJson<SavedHires>(json); }
            catch (System.Exception e) { Debug.LogWarning("[SkillTreeManager] Couldn't read the saved high-IQ hire progress (the saved count decides): " + e.Message); }
        }
        if (data == null) return;
        knowledge = float.IsNaN(data.knowledge) ? 0f : Mathf.Clamp(data.knowledge, 0f, 0.9999f);

        // Saved in points of its own hiresPerPoint: the same count of hires in today's points
        int savedPer = data.hiresPerPoint > 0 ? data.hiresPerPoint : HiresPerPoint;
        long hires = (long)Mathf.Max(0, data.pointsAwarded) * savedPer;
        int saved = (int)System.Math.Min(int.MaxValue, hires / HiresPerPoint);

        // Only ever raised: ApplySavedState already counted the multiples the saved count has passed
        int markBefore = highIQPointsAwarded, pointsBefore = skillPoints;
        highIQPointsAwarded = Mathf.Max(highIQPointsAwarded, saved);

        // Points got easier since this save (e.g. 7 → 3 high-IQ hires per point): pay, once, what the hires already made
        // earn at today's rate (the next save stores today's rate, so this never repeats)
        if (savedPer > HiresPerPoint)
        {
            int bonus = highIQPointsAwarded - Mathf.Max(0, data.pointsAwarded);
            if (bonus > 0)
            {
                skillPoints += bonus;
                GameNotifications.Show(L10n.Get(Skill.Table, "skilltree.toast.points_easier", ("count", bonus)), NotificationKind.Special);
            }
        }

        if (highIQPointsAwarded != markBefore || skillPoints != pointsBefore)
            SkillPointsChanged?.Invoke(skillPoints);
    }
}

