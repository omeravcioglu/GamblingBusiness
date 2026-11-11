using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// ContentTextSync sources for the hiring tasks (table "Hiring") and the applicant fields (table "Applicants"). Both read
/// the live data: HiringTaskDatabase.asset and MajorDatabase.asset through the AssetDatabase (an asset whose lists are
/// empty stands for its code defaults, which the game then fills in), ApplicantGenerator's genders, races, degrees and
/// fallback majors, TraitDatabase's built-in traits, and the fixed texts in HiringText / ApplicantText. Keys:
/// task.&lt;slug&gt;.title|.desc and hiring.* (HiringText); applicant.*, company.*, trait.&lt;slug&gt; and major.&lt;slug&gt;
/// (ApplicantText), slug = L10n.Slug(English). A hiring task's major keyword ("Art", "Nap") becomes a major.&lt;slug&gt;
/// entry, a trait keyword a trait.&lt;slug&gt; entry.
/// </summary>
public sealed class HiringTextSource : IContentTextSource
{
    public string Table => HiringText.Table;

    public IEnumerable<ContentText> Collect()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var text in HiringText.FixedTexts)
        {
            if (keys.Add(text.Key)) yield return new ContentText(text.Key, text.Context, text.English);
        }

        foreach (HiringTask task in HiringContentData.Tasks())
        {
            if (task == null || string.IsNullOrWhiteSpace(task.title)) continue;
            string key = HiringText.TaskKey(task.title);
            if (!keys.Add(key + ".title"))
            {
                Debug.LogWarning("[HiringTextSource] Two hiring tasks are titled \"" + task.title + "\": the second one shares the first one's texts.");
                continue;
            }
            string objective = HiringContentData.Describe(task);
            yield return new ContentText(key + ".title",
                "Hiring task title on the HUD task board (30 px bold, wraps). Deadpan corporate satire: transcreate the joke. Objective: " + objective + ".",
                task.title);
            if (!string.IsNullOrEmpty(task.description) && keys.Add(key + ".desc"))
                yield return new ContentText(key + ".desc",
                    "Hiring task description under its title \"" + task.title + "\" (19 px, wraps, 1-3 lines). Deadpan corporate satire: transcreate the joke. Objective: " + objective + ".",
                    task.description);
        }
    }
}

public sealed class ApplicantTextSource : IContentTextSource
{
    public string Table => ApplicantText.Table;

    const string MajorBase = "Major (field of study) on a CV and in the staff files: 22 px, shrinking to 18 px, then 2 lines in 216 px.";
    const string KeywordNote = " Also a keyword of the objective hiring.req.major: any major containing it passes.";

    public IEnumerable<ContentText> Collect()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<ContentText>();
        void Add(string key, string context, string english)
        {
            // (a value without letters or digits has no slug: no key)
            if (string.IsNullOrEmpty(english) || key.EndsWith(".", StringComparison.Ordinal)) return;
            if (keys.Add(key)) result.Add(new ContentText(key, context, english));
        }
        void AddFixed(Func<string, bool> which)
        {
            foreach (var text in ApplicantText.FixedTexts)
                if (which(text.Key)) Add(text.Key, text.Context, text.English);
        }
        bool IsField(string key) => key.StartsWith(ApplicantText.RacePrefix, StringComparison.Ordinal)
                                    || key.StartsWith(ApplicantText.DegreePrefix, StringComparison.Ordinal)
                                    || key.StartsWith(ApplicantText.EducationPrefix, StringComparison.Ordinal);

        // Company name and the major words
        AddFixed(key => !IsField(key));

        foreach (string gender in ApplicantGenerator.Genders)
            Add(ApplicantText.GenderPrefix + L10n.Slug(gender),
                "Gender on a CV (after the age: 29 · Female · Mixed), in the staff files and in objectives (hiring.req.gender). Short (~10 characters)." +
                (gender == "Other" ? " The option besides Male and Female (non-binary or another gender)." : ""),
                gender);

        foreach (string race in ApplicantGenerator.Races)
            Add(ApplicantText.RacePrefix + L10n.Slug(race),
                "Ethnicity on a CV (29 · Female · Mixed), in the staff files and in objectives (hiring.req.race). Use the neutral, respectful standard term. Short (~14 characters)." +
                (race == "Mixed" ? " Of mixed ethnicity (multiracial)." : race == "Hispanic" ? " Hispanic / Latino." : ""),
                race);
        AddFixed(key => key.StartsWith(ApplicantText.RacePrefix, StringComparison.Ordinal));

        foreach (string degree in ApplicantGenerator.Degrees)
            Add(ApplicantText.DegreePrefix + L10n.Slug(degree),
                "Degree on a CV's degree line and in the staff files, and in objectives (hiring.req.degree). Short (~16 characters)." +
                (degree == "High School" ? " A high-school education: the CV's degree line shows applicant.degree.diploma instead; the objective is hiring.req.degree.high_school." : ""),
                degree);
        AddFixed(key => key.StartsWith(ApplicantText.DegreePrefix, StringComparison.Ordinal));
        AddFixed(key => key.StartsWith(ApplicantText.EducationPrefix, StringComparison.Ordinal));

        // Traits
        List<PersonalityTrait> traits = TraitDatabase.CreateDefaults();
        foreach (PersonalityTrait trait in traits)
        {
            if (trait == null) continue;
            string kind = trait.type == TraitType.Positive ? "A good trait."
                        : trait.type == TraitType.Negative ? "A bad trait."
                        : "A quirky neutral trait: a joke, transcreate it.";
            Add(ApplicantText.TraitPrefix + L10n.Slug(trait.traitName),
                "Personality trait on a CV and in the staff files (20 px, ~22 characters). " + kind, trait.traitName);
        }

        // Majors: the database's lists, the generator's own lists, then the hiring tasks' keywords
        HiringContentData.Majors(out List<string> normal, out List<string> funny);
        List<HiringTask> tasks = HiringContentData.Tasks();
        var majorKeywords = new List<string>();
        var traitKeywords = new List<string>();
        foreach (HiringTask task in tasks)
        {
            string target = task != null && task.targetValue != null ? task.targetValue.Trim() : "";
            if (target.Length == 0) continue;
            if (task.requirementType == TaskRequirementType.HireMajorContains) majorKeywords.Add(target);
            if (task.requirementType == TaskRequirementType.HireWithTrait) traitKeywords.Add(target);
        }
        var keywordSlugs = new HashSet<string>(majorKeywords.Select(L10n.Slug), StringComparer.Ordinal);
        string Keyword(string major) => keywordSlugs.Contains(L10n.Slug(major)) ? KeywordNote : "";

        foreach (string major in normal)
            Add(ApplicantText.MajorPrefix + L10n.Slug(major), MajorBase + " A real field of study: use its usual name." + Keyword(major), major);
        foreach (string major in funny)
            Add(ApplicantText.MajorPrefix + L10n.Slug(major), MajorBase + " A joke major: transcreate it as a funny fake field of study." + Keyword(major), major);
        foreach (string major in ApplicantGenerator.FallbackMajors.Concat(ApplicantGenerator.SmartMajors))
            Add(ApplicantText.MajorPrefix + L10n.Slug(major),
                MajorBase + " A real field of study: use its usual name. Used when the scene has no major database." + Keyword(major), major);

        List<string> allMajors = normal.Concat(funny).ToList();
        foreach (string keyword in majorKeywords)
            Add(ApplicantText.MajorPrefix + L10n.Slug(keyword), KeywordContext("hiring.req.major (Hire someone who studied {major})", "major", keyword, allMajors), keyword);
        List<string> traitNames = traits.Where(t => t != null).Select(t => t.traitName).ToList();
        foreach (string keyword in traitKeywords)
            Add(ApplicantText.TraitPrefix + L10n.Slug(keyword), KeywordContext("hiring.req.trait (Hire someone with the '{trait}' trait)", "trait", keyword, traitNames), keyword);

        return result;
    }

    static string KeywordContext(string objective, string what, string keyword, List<string> values)
    {
        List<string> examples = values.Where(v => !string.IsNullOrEmpty(v) && v.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                                      .Distinct().Take(5).ToList();
        string text = "Keyword of the objective " + objective + ": any CV " + what + " containing it passes";
        if (examples.Count == 0) return text + ".";
        return text + ", e.g. " + string.Join(", ", examples) + ". Where you can, use the word your translations of those " + what + "s share.";
    }
}

/// <summary>The live hiring and major data for the content sources.</summary>
static class HiringContentData
{
    /// <summary>Every HiringTaskDatabase asset's tasks (path order); an empty asset stands for the code defaults.</summary>
    public static List<HiringTask> Tasks()
    {
        var tasks = new List<HiringTask>();
        foreach (HiringTaskDatabase database in Load<HiringTaskDatabase>())
        {
            if (database.tasks != null && database.tasks.Count > 0)
            {
                tasks.AddRange(database.tasks);
                continue;
            }
            var defaults = ScriptableObject.CreateInstance<HiringTaskDatabase>();
            try
            {
                defaults.InitializeDefaultTasks();
                tasks.AddRange(defaults.tasks);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(defaults);
            }
        }
        return tasks;
    }

    /// <summary>Every MajorDatabase asset's normal and funny majors (path order); empty lists stand for the code defaults.</summary>
    public static void Majors(out List<string> normal, out List<string> funny)
    {
        normal = new List<string>();
        funny = new List<string>();
        foreach (MajorDatabase database in Load<MajorDatabase>())
        {
            MajorDatabase source = database;
            MajorDatabase defaults = null;
            if (database.normalMajors == null || database.normalMajors.Length == 0 || database.funnyMajors == null || database.funnyMajors.Length == 0)
            {
                defaults = ScriptableObject.CreateInstance<MajorDatabase>();
                defaults.normalMajors = database.normalMajors;
                defaults.funnyMajors = database.funnyMajors;
                defaults.InitializeDefaults();
                source = defaults;
            }
            try
            {
                if (source.normalMajors != null) normal.AddRange(source.normalMajors.Where(m => !string.IsNullOrWhiteSpace(m)));
                if (source.funnyMajors != null) funny.AddRange(source.funnyMajors.Where(m => !string.IsNullOrWhiteSpace(m)));
            }
            finally
            {
                if (defaults != null) UnityEngine.Object.DestroyImmediate(defaults);
            }
        }
    }

    static IEnumerable<T> Load<T>() where T : ScriptableObject
    {
        return AssetDatabase.FindAssets("t:" + typeof(T).Name)
                            .Select(AssetDatabase.GUIDToAssetPath)
                            .Distinct()
                            .OrderBy(path => path, StringComparer.Ordinal)
                            .Select(AssetDatabase.LoadAssetAtPath<T>)
                            .Where(asset => asset != null)
                            .ToList();
    }

    /// <summary>The objective in plain English for translators ("ethnicity Black", "age 24-28", "a major containing 'Art'").</summary>
    public static string Describe(HiringTask task)
    {
        string value = task.targetValue != null ? task.targetValue.Trim() : "";
        string max = task.targetValueMax != null ? task.targetValueMax.Trim() : "";
        switch (task.requirementType)
        {
            case TaskRequirementType.HireRace: return "ethnicity " + value;
            case TaskRequirementType.HireGender: return "gender " + value;
            case TaskRequirementType.HireAgeMin: return "age " + value + "+";
            case TaskRequirementType.HireAgeMax: return "age " + value + " or younger";
            case TaskRequirementType.HireAgeRange: return value == max ? "age exactly " + value : "age " + value + "-" + max;
            case TaskRequirementType.HireDegree: return "degree " + value;
            case TaskRequirementType.HireMajorContains: return "a major containing '" + value + "'";
            case TaskRequirementType.HireGPAMin: return "GPA " + value + "+";
            case TaskRequirementType.HireGPAMax: return "GPA " + value + " or lower";
            case TaskRequirementType.HireIQMin: return "IQ " + value + "+";
            case TaskRequirementType.HireIQMax: return "IQ " + value + " or lower";
            case TaskRequirementType.HireIQRange: return "IQ " + value + "-" + max;
            case TaskRequirementType.HireExperienceMin: return value + "+ years of experience";
            case TaskRequirementType.HireExperienceMax: return "at most " + value + " years of experience";
            case TaskRequirementType.HireSalaryMin: return "asks $" + Money(value) + "+ a year";
            case TaskRequirementType.HireSalaryMax: return "asks at most $" + Money(value) + " a year";
            case TaskRequirementType.HireWithCriminalRecord: return "a criminal record";
            case TaskRequirementType.HireWithoutCriminalRecord: return "a clean record";
            case TaskRequirementType.HireGolden: return "a golden (1-in-100) applicant";
            case TaskRequirementType.HireWithTrait: return "the trait '" + value + "'";
            case TaskRequirementType.HireAny: return "anyone";
            default: return task.requirementType.ToString();
        }
    }

    static string Money(string value)
    {
        return HiringTask.TryParseInt(value, out int amount) ? amount.ToString("N0", CultureInfo.InvariantCulture) : value;
    }
}
