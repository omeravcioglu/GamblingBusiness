using UnityEngine;

/// <summary>
/// Wording for the skill board, built from a skill's data so balance changes show up by themselves:
/// the short effect line on a card ("+10% daily profit"), the folder's effect sentence split for the highlighter,
/// the quote, and an example for a few effects. The board's own texts are in the "SkillBoard" table
/// (Assets/Localization/Source/SkillBoard.csv); skill names, effects and quotes come from the Skills table (Skill.Display*).
/// </summary>
public static class SkillText
{
    /// <summary>The skill board's String Table.</summary>
    public const string Table = "SkillBoard";

    public static string BranchName(SkillBranch b)
    {
        switch (b)
        {
            case SkillBranch.Productivity: return L10n.Get(Table, "skillboard.branch.productivity");
            case SkillBranch.Chaos: return L10n.Get(Table, "skillboard.branch.chaos");
            default: return L10n.Get(Table, "skillboard.branch.growth");
        }
    }

    /// <summary>"10", "1.5" ("1,5" in Turkish): the number of an entry's "{percent}%" (the entry places the % sign).</summary>
    public static string PercentNumber(float v)
    {
        float p = v * 100f;
        return Mathf.Abs(p - Mathf.Round(p)) < 0.05f
            ? Mathf.RoundToInt(p).ToString(L10n.Culture)
            : p.ToString("0.#", L10n.Culture);
    }

    static string Num(float v) =>
        Mathf.Abs(v - Mathf.Round(v)) < 0.01f ? Mathf.RoundToInt(v).ToString(L10n.Culture) : v.ToString("0.#", L10n.Culture);

    /// <summary>The short line on a card (fits 180 px at 17 px; the card shrinks longer translations).</summary>
    public static string Short(Skill s)
    {
        if (s == null) return "";
        string p = PercentNumber(s.effectValue);
        switch (s.effectType)
        {
            case SkillEffect.DailyValueGrowth:
            case SkillEffect.BankInterest: return L10n.Get(Table, "skillboard.short.daily_profit", ("percent", p));
            case SkillEffect.SalaryDiscount: return L10n.Get(Table, "skillboard.short.hire_salaries", ("percent", p));
            case SkillEffect.NegativeEventReduction: return L10n.Get(Table, "skillboard.short.bad_events", ("percent", p));
            case SkillEffect.TaxOptimization: return L10n.Get(Table, "skillboard.short.salary_costs", ("percent", p));
            case SkillEffect.UnlockStockMarket: return L10n.Get(Table, "skillboard.short.unlock_stocks");
            case SkillEffect.GoldenParachute: return L10n.Get(Table, "skillboard.short.bad_cards", ("percent", p));
            case SkillEffect.MergerBonus: return L10n.Get(Table, "skillboard.short.per_staff", ("amount", UIFormat.Money(s.effectValue)));
            case SkillEffect.MarketManipulation: return L10n.Get(Table, "skillboard.short.stocks_favor");

            case SkillEffect.EmployeeProductivity:
            case SkillEffect.WorkspaceOptimization:
            case SkillEffect.FlexibleHours:
            case SkillEffect.WorkFromHome:
            case SkillEffect.PerformanceReviews:
            case SkillEffect.LeanManagement:
            case SkillEffect.SixSigma:
            case SkillEffect.TimeTracking:
            case SkillEffect.CollaborationTools:
            case SkillEffect.CommunicationBoost:
            case SkillEffect.StressReduction:
            case SkillEffect.EmployeeReferral:
            case SkillEffect.CultureFit: return L10n.Get(Table, "skillboard.short.output", ("percent", p));
            case SkillEffect.TrainingProgram: return L10n.Get(Table, "skillboard.short.experience_years", ("years", Num(s.effectValue)));
            case SkillEffect.ExperienceGrowth: return L10n.Get(Table, "skillboard.short.exp_daily", ("percent", p));
            case SkillEffect.QualityControl: return L10n.Get(Table, "skillboard.short.duds", ("percent", p));
            case SkillEffect.MentorshipProgram: return L10n.Get(Table, "skillboard.short.learn_faster", ("percent", p));
            case SkillEffect.SkillCertification: return L10n.Get(Table, "skillboard.short.cert_bonus", ("percent", p));
            case SkillEffect.IQSynergy: return L10n.Get(Table, "skillboard.short.iq_synergy", ("amount", UIFormat.Money(s.effectValue)));
            case SkillEffect.InnovationBonus: return L10n.Get(Table, "skillboard.short.phd", ("percent", p));
            case SkillEffect.AgileMethodology: return L10n.Get(Table, "skillboard.short.task_time", ("percent", p));
            case SkillEffect.UnlockRnD: return L10n.Get(Table, "skillboard.short.research");
            case SkillEffect.TeamSynergy:
                return L10n.Get(Table, "skillboard.short.team_synergy", ("percent", p), ("staff", Num(s.effectValue2)));
            case SkillEffect.ProjectManagement: return L10n.Get(Table, "skillboard.short.tasks_better", ("percent", p));
            case SkillEffect.AutomationBots: return L10n.Get(Table, "skillboard.short.robots", ("count", Num(s.effectValue)));
            case SkillEffect.AIAssistant: return L10n.Get(Table, "skillboard.short.easier_games");
            case SkillEffect.EmployeeRetention: return L10n.Get(Table, "skillboard.short.mood_drops", ("percent", p));
            case SkillEffect.TalentScouting: return L10n.Get(Table, "skillboard.short.better_applicants");
            case SkillEffect.HeadhunterBonus:
            case SkillEffect.RecruitingNetwork: return L10n.Get(Table, "skillboard.short.hire_quality", ("percent", p));

            case SkillEffect.GoodEventChance: return L10n.Get(Table, "skillboard.short.good_events", ("percent", p));
            case SkillEffect.SkipBonus: return L10n.Get(Table, "skillboard.short.skip_bonus", ("amount", UIFormat.Money(s.effectValue2)));
            case SkillEffect.LuckyCharm:
            case SkillEffect.ProbabilityManipulation: return L10n.Get(Table, "skillboard.short.luck", ("percent", p));
            case SkillEffect.FortuneCookie: return L10n.Get(Table, "skillboard.short.fortune");
            case SkillEffect.LotteryTicket: return L10n.Get(Table, "skillboard.short.lottery");
            case SkillEffect.LuckyHireChance: return L10n.Get(Table, "skillboard.short.golden_hires", ("percent", p));
            case SkillEffect.SalaryFluctuation: return L10n.Get(Table, "skillboard.short.salary_roulette", ("percent", p));
            case SkillEffect.UnluckyProtection: return L10n.Get(Table, "skillboard.short.bad_luck", ("percent", p));
            case SkillEffect.CoinFlip: return L10n.Get(Table, "skillboard.short.coin_flip");
            case SkillEffect.MysteryBox: return L10n.Get(Table, "skillboard.short.mystery");
            case SkillEffect.DailyRandomBuff: return L10n.Get(Table, "skillboard.short.buff_or_nerf");
            case SkillEffect.CriticalSuccess: return L10n.Get(Table, "skillboard.short.critical", ("percent", p));
            case SkillEffect.RandomRaise: return L10n.Get(Table, "skillboard.short.random_raises");
            case SkillEffect.DiceRoll: return L10n.Get(Table, "skillboard.short.dice");
            case SkillEffect.TarotCard: return L10n.Get(Table, "skillboard.short.tarot");
            case SkillEffect.ChaoticEnergy: return L10n.Get(Table, "skillboard.short.chaotic", ("percent", p));
            case SkillEffect.GamblersFallacy: return L10n.Get(Table, "skillboard.short.fallacy", ("percent", p));
            case SkillEffect.UnlockCasino: return L10n.Get(Table, "skillboard.short.unlock_casino");
            case SkillEffect.WildCard: return L10n.Get(Table, "skillboard.short.reroll");
            case SkillEffect.DoubleOrNothing: return L10n.Get(Table, "skillboard.short.double");
            case SkillEffect.JackpotChance: return L10n.Get(Table, "skillboard.short.jackpot", ("percent", p));
            case SkillEffect.LuckyStreak: return L10n.Get(Table, "skillboard.short.streak", ("percent", p));
            case SkillEffect.SurpriseBonus: return L10n.Get(Table, "skillboard.short.surprise");
            case SkillEffect.JinxRemoval: return L10n.Get(Table, "skillboard.short.jinx", ("percent", p));
            case SkillEffect.Serendipity: return L10n.Get(Table, "skillboard.short.serendipity", ("percent", p));
            case SkillEffect.KarmaBonus: return L10n.Get(Table, "skillboard.short.karma");
            case SkillEffect.ReverseKarma: return L10n.Get(Table, "skillboard.short.reverse_karma", ("percent", p));
            case SkillEffect.RealityWarp: return L10n.Get(Table, "skillboard.short.reality");
            case SkillEffect.QuantumLuck: return L10n.Get(Table, "skillboard.short.quantum");
            case SkillEffect.UnlockSecretBranch: return L10n.Get(Table, "skillboard.short.all_effects", ("percent", p));
        }
        return EffectSentence(s);
    }

    /// <summary>The effect sentence: the description's first line.</summary>
    public static string EffectSentence(Skill s)
    {
        // l10n (batch B): the description's first line in the current language (Skill.DisplayEffect, Skills table)
        return s != null ? s.DisplayEffect : "";
    }

    /// <summary>The quote: the description's second line, in the language's quotation marks (empty when there is none).</summary>
    public static string Quote(Skill s)
    {
        // Skill.DisplayQuote comes without quotation marks; the entry adds them
        string q = s != null ? s.DisplayQuote.Trim().Trim('"', '“', '”').Trim() : "";
        return q.Length == 0 ? "" : L10n.Get(Table, "skillboard.folder.quote", ("quote", q.Replace("'", "’")));
    }

    /// <summary>
    /// The effect sentence with its key part wrapped in the highlighter link: from the first word with a number
    /// ("hurt <mark>20% less.</mark>"), else what an "Unlocks ..." sentence unlocks, else the last two words.
    /// </summary>
    public static string MarkedEffect(Skill s)
    {
        string e = EffectSentence(s).Replace("'", "’");
        if (e.Length == 0) return "";
        int start = -1;
        string[] words = e.Split(' ');
        int pos = 0;
        foreach (string w in words)
        {
            foreach (char c in w)
            {
                if (char.IsDigit(c)) { start = pos; break; }
            }
            if (start >= 0) break;
            pos += w.Length + 1;
        }
        // l10n (batch B): the word rules below read English; a translation is marked from its first number (text without
        // spaces, Chinese or Japanese, is one "word": from the digit, with a sign / $ / % just before it), else all of it
        bool english = L10n.CurrentCode == L10n.English;
        if (start == 0 && !english && e.IndexOf(' ') < 0) start = NumberStart(e);
        if (start < 0 && english && e.StartsWith("Unlocks ")) start = "Unlocks ".Length;
        if (start < 0 && english && words.Length > 2) start = e.Length - (words[words.Length - 2].Length + 1 + words[words.Length - 1].Length);
        if (start <= 0) return "<link=\"" + TextHighlighter.LinkId + "\">" + e + "</link>";
        return e.Substring(0, start) + "<link=\"" + TextHighlighter.LinkId + "\">" + e.Substring(start) + "</link>";
    }

    // l10n (batch B): where the number starts in text without spaces ("每日利润+10%。" → at "+")
    static int NumberStart(string e)
    {
        int i = 0;
        while (i < e.Length && !char.IsDigit(e[i])) i++;
        if (i >= e.Length) return 0;
        while (i > 0 && "+-−±$%＋－".IndexOf(e[i - 1]) >= 0) i--;
        return i;
    }

    /// <summary>A worked example for effects where one helps (empty otherwise).</summary>
    public static string Example(Skill s)
    {
        if (s == null) return "";
        float v = s.effectValue;
        switch (s.effectType)
        {
            case SkillEffect.NegativeEventReduction:
                return L10n.Get(Table, "skillboard.example.event_hit",
                    ("before", UIFormat.Money(-25000f)), ("after", UIFormat.Money(-25000f * (1f - v))));
            case SkillEffect.GoldenParachute:
                return L10n.Get(Table, "skillboard.example.bad_card",
                    ("before", UIFormat.Money(-20000f)), ("after", UIFormat.Money(-20000f * (1f - v))));
            case SkillEffect.DailyValueGrowth:
            case SkillEffect.BankInterest:
                return L10n.Get(Table, "skillboard.example.day",
                    ("before", UIFormat.Money(10000f)), ("after", UIFormat.Money(10000f * (1f + v))));
            case SkillEffect.SalaryDiscount:
                return L10n.Get(Table, "skillboard.example.salary",
                    ("before", UIFormat.Money(5000f)), ("after", UIFormat.Money(5000f * (1f - v))));
            case SkillEffect.MergerBonus:
                return L10n.Get(Table, "skillboard.example.staff", ("staff", 20), ("amount", UIFormat.Money(20f * v)));
            case SkillEffect.UnlockStockMarket:
                return L10n.Get(Table, "skillboard.example.stocks_tab");
            case SkillEffect.UnlockCasino:
                return L10n.Get(Table, "skillboard.example.casino_tab");
        }
        return "";
    }
}
