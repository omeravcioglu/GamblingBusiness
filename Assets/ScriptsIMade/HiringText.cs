using System;
using System.Collections.Generic;

/// <summary>
/// Hiring-task texts in the current language, from the "Hiring" String Table (Assets/Localization/Source/Hiring.csv):
/// - A task's title and description stay English in HiringTaskDatabase.asset (the live list; tasks are matched by their
///   English target values) and show through HiringTask.DisplayTitle / DisplayDescription: task.&lt;slug&gt;.title and
///   .desc with slug = L10n.Slug(title) (titles are unique), the English when the table has no such entry.
/// - The objective sentences of HiringTaskManager.GetRequirementText (hiring.req.*, one per requirement type) and the
///   task board's words are the fixed texts below; their English lives here.
/// The Editor's HiringTextSource (Editor/HiringContentSources.cs) lists every key for ContentTextSync.
/// </summary>
public static class HiringText
{
    public const string Table = "Hiring";

    /// <summary>"task." + L10n.Slug(title): the key of a task's texts, before ".title" / ".desc".</summary>
    public static string TaskKey(string title) => "task." + L10n.Slug(title);

    /// <summary>A fixed text in the current language, its English from the list below when the table has none.</summary>
    public static string Get(string key, params (string name, object value)[] args)
    {
        return L10n.GetOr(Table, key, English(key), args);
    }

    static Dictionary<string, string> s_English;

    /// <summary>The English of one of the fixed texts below (the key itself when it isn't one).</summary>
    public static string English(string key)
    {
        if (s_English == null)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var text in FixedTexts) map[text.Key] = text.English;
            s_English = map;
        }
        if (key == null) return "";
        return s_English.TryGetValue(key, out string english) ? english : key;
    }

    const string Objective = "Hiring objective on the HUD task board (22 px, wraps onto 2 lines).";

    /// <summary>
    /// The table's texts that don't come from HiringTaskDatabase: key, English, note for translators. The Editor's content
    /// source writes them to Hiring.csv. Objectives: HiringTaskManager.GetRequirementText passes every value through the
    /// UI's highlight (the board's highlighter mark); objectives without a value are highlighted whole.
    /// </summary>
    public static readonly (string Key, string English, string Context)[] FixedTexts =
    {
        ("hiring.board.header", "HIRING TASK",
            "Spaced capitals at the top of the HUD task clipboard, next to the difficulty stars (17 px, 220 px wide, ~16 characters). Final case."),
        ("hiring.board.header.research", "R&D PROJECT",
            "The same header while the task is an R&D research project (a harder task with a bigger reward, from the R&D Laboratory skill). ~16 characters. Final case."),
        ("hiring.toast.coin_flip", "Coin Flip: task reward doubled! {amount}",
            "Toast when the Coin Flip skill doubles a finished task's reward. {amount} = the doubled reward with its sign (+$10,000). Coin Flip is the skill's name: use its name from the skill tree."),
        ("hiring.legacy.research_title", "R&D PROJECT: {title}",
            "Old task panel (hidden in the current HUD): the title of an R&D research project. {title} = the task's title."),
        ("hiring.legacy.stakes", "Reward: {reward}  |  Penalty: -{penalty}",
            "Old task panel (hidden in the current HUD): what the task pays and what missing it costs. {reward}, {penalty} = money ($5,000)."),

        ("hiring.req.race", "Hire a {race} employee",
            Objective + " {race} = an ethnicity as the CV shows it (applicant.race.*), highlighted."),
        ("hiring.req.race.an", "Hire an {race} employee",
            "Same objective as hiring.req.race: English needs 'an' before a vowel (Asian). Translate it exactly like hiring.req.race."),
        ("hiring.req.gender", "Hire a {gender} employee",
            Objective + " {gender} = a gender as the CV shows it (applicant.gender.*: Male, Female, Other), highlighted."),
        ("hiring.req.gender.an", "Hire an {gender} employee",
            "Same objective as hiring.req.gender: English needs 'an' before a vowel (Other). Translate it exactly like hiring.req.gender."),
        ("hiring.req.age_min", "Hire someone {age}+ years old",
            Objective + " {age} = the minimum age (55); that age itself passes. Highlighted."),
        ("hiring.req.age_max", "Hire someone {age} or younger",
            Objective + " {age} = the maximum age (25); that age itself passes. Highlighted."),
        ("hiring.req.age_range", "Hire someone aged {range}",
            Objective + " {range} = an age range made by hiring.req.range (24-28); both ends pass. Highlighted."),
        ("hiring.req.age_exact", "Hire someone exactly {age} years old",
            Objective + " {age} = the one age that passes (42). Highlighted."),
        ("hiring.req.range", "{min}-{max}",
            "A range of two whole numbers inside objectives: ages (24-28) and IQ (90-110); both ends pass. {min}, {max} = the numbers. Use your language's usual range style."),
        ("hiring.req.degree", "Hire someone with a {degree}",
            Objective + " {degree} = a degree as named in applicant.degree.* (Bachelor, Master, PhD), highlighted; the CV's degree line shows the same word."),
        ("hiring.req.degree.an", "Hire someone with an {degree}",
            "Same objective as hiring.req.degree: English needs 'an' before a vowel (Associate Degree). Translate it exactly like hiring.req.degree."),
        ("hiring.req.degree.high_school", "Hire someone with a {degree} education",
            Objective + " {degree} = applicant.degree.high_school (High School), highlighted; the CV's degree line of such an applicant shows applicant.degree.diploma."),
        ("hiring.req.major", "Hire someone who studied {major}",
            Objective + " {major} = a major or a part of one (Computer, Art, Nap: a major.* entry), highlighted; any CV major that contains it passes."),
        ("hiring.req.gpa_min", "Hire someone with GPA {gpa}+",
            Objective + " {gpa} = the minimum grade point average (3.8, out of 4.0); that value passes. Highlighted."),
        ("hiring.req.gpa_max", "Hire someone with GPA {gpa} or lower",
            Objective + " {gpa} = the maximum grade point average (2.5); that value passes. Highlighted."),
        ("hiring.req.iq_min", "Hire someone with IQ {iq}+",
            Objective + " {iq} = the minimum IQ (120); that value passes. Highlighted."),
        ("hiring.req.iq_max", "Hire someone with IQ {iq} or lower",
            Objective + " {iq} = the maximum IQ (85); that value passes. Highlighted."),
        ("hiring.req.iq_range", "Hire someone with IQ {range}",
            Objective + " {range} = an IQ range made by hiring.req.range (90-110); both ends pass. Highlighted."),
        ("hiring.req.experience_min", "Hire someone with {years}+ years experience",
            Objective + " {years} = the minimum years of work experience (10); that value passes. Highlighted."),
        ("hiring.req.experience_max", "Hire someone with {years} {count:plural:year|years} of experience or less",
            Objective + " {years} = the maximum years of work experience (2), highlighted; {count} = the same number without the mark, for choosing year/years ({count:plural:year|years}); count your own way."),
        ("hiring.req.experience_max.none", "Hire someone with no experience",
            Objective + " Zero years of work experience. The whole line is highlighted."),
        ("hiring.req.salary_min", "Hire someone asking {amount}+",
            Objective + " {amount} = the minimum yearly salary the applicant asks for ($150,000); that amount passes. Highlighted."),
        ("hiring.req.salary_max", "Hire someone asking {amount} or less",
            Objective + " {amount} = the maximum yearly salary the applicant asks for ($45,000); that amount passes. Highlighted."),
        ("hiring.req.criminal_record", "Hire someone with a criminal record",
            Objective + " The whole line is highlighted."),
        ("hiring.req.clean_record", "Hire someone with a clean record",
            Objective + " No criminal record. The whole line is highlighted."),
        ("hiring.req.golden", "Hire a GOLDEN applicant",
            Objective + " Golden = the rare 1-in-100 super applicant (a gold CV; the staff files' Golden tag). The whole line is highlighted."),
        ("hiring.req.trait", "Hire someone with the '{trait}' trait",
            Objective + " {trait} = a personality trait's name or a part of one (a trait.* entry), highlighted; any trait containing it passes. Use your language's quotation marks."),
        ("hiring.req.any", "Hire anyone",
            Objective + " Any hire passes. The whole line is highlighted."),
        ("hiring.req.default", "Complete the objective",
            Objective + " Fallback for an objective type the game doesn't know."),
    };
}
