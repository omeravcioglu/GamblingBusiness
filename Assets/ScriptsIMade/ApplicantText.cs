using System;
using System.Collections.Generic;

/// <summary>
/// Applicant and employee fields in the current language, for display only. The game saves and compares the English
/// values ApplicantGenerator writes (gender, ethnicity, degree, major and trait names; hiring tasks match their English
/// targets against them), so these helpers translate at display time from the "Applicants" String Table
/// (Assets/Localization/Source/Applicants.csv) and fall back to the English value for anything the table doesn't have
/// (new or edited content shows its English until it is translated). Keys are the English through L10n.Slug:
/// - Gender("Female"), Race("Middle Eastern"), Degree("Associate Degree"): applicant.gender|race|degree.&lt;slug&gt;
/// - DegreeLine(person): the CV's degree line (ApplicantData.GetDegreeText: "Diploma" for High School, "None")
/// - Education(person): where they studied (ApplicantData.GetEducationText: "University"...): applicant.education.&lt;slug&gt;
/// - Major("Computer Science"), Major("Planned: Law"), Major("N/A"), Major(""): major.&lt;slug&gt;, the template
///   applicant.major.planned ("Planned: {major}"), applicant.major.na and applicant.major.undeclared. A hiring task's
///   major keyword ("Art", "Nap") is a major.&lt;slug&gt; entry too.
/// - Trait("Hard Worker"): trait.&lt;slug&gt;
/// - DefaultCompanyName: "New Company", the company's name until the player names it (CompanyNameUI.DisplayName).
/// - FallbackName(427): "Applicant #427", ApplicantGenerator's name when there are no name lists (applicant.fallback_name).
/// The Editor's ApplicantTextSource (Editor/HiringContentSources.cs) lists every key for ContentTextSync.
/// </summary>
public static class ApplicantText
{
    public const string Table = "Applicants";

    /// <summary>ApplicantGenerator writes it before a High School applicant's planned major ("Planned: Law").</summary>
    public const string PlannedPrefix = "Planned: ";
    /// <summary>The major of a High School applicant without a plan.</summary>
    public const string NoMajor = "N/A";
    /// <summary>ApplicantData.GetMajorText's word for no major (MajorDatabase also returns it when its lists are empty).</summary>
    public const string Undeclared = "Undeclared";

    public const string GenderPrefix = "applicant.gender.";
    public const string RacePrefix = "applicant.race.";
    public const string DegreePrefix = "applicant.degree.";
    public const string EducationPrefix = "applicant.education.";
    public const string MajorPrefix = "major.";
    public const string TraitPrefix = "trait.";
    public const string PlannedKey = "applicant.major.planned";
    public const string NoMajorKey = "applicant.major.na";
    public const string UndeclaredKey = "applicant.major.undeclared";
    public const string DefaultCompanyKey = "company.default_name";
    public const string FallbackNameKey = "applicant.fallback_name";

    /// <summary>A gender as ApplicantData stores it ("Female"): applicant.gender.&lt;slug&gt;; unknown values come back as they are.</summary>
    public static string Gender(string english) => Lookup(GenderPrefix, english);

    /// <summary>An ethnicity as ApplicantData stores it ("Middle Eastern"): applicant.race.&lt;slug&gt;; unknown values come back as they are.</summary>
    public static string Race(string english) => Lookup(RacePrefix, english);

    /// <summary>A degree by its English name ("Bachelor", "High School", also "Diploma" and "None"): applicant.degree.&lt;slug&gt;.</summary>
    public static string Degree(string english) => Lookup(DegreePrefix, english);

    /// <summary>The CV's degree line: ApplicantData.GetDegreeText ("Diploma" for High School, "None" without a degree), translated.</summary>
    public static string DegreeLine(ApplicantData person) => person == null ? "" : Degree(person.GetDegreeText());

    /// <summary>Where the person studied: ApplicantData.GetEducationText ("University", "Grad School"...), translated.</summary>
    public static string Education(ApplicantData person) => person == null ? "" : Lookup(EducationPrefix, person.GetEducationText());

    /// <summary>A trait by its English name (PersonalityTrait.traitName): trait.&lt;slug&gt;.</summary>
    public static string Trait(string english) => Lookup(TraitPrefix, english);

    /// <summary>
    /// A major as ApplicantData.major stores it: "Computer Science" (major.computer_science), "Planned: Law" (the
    /// applicant.major.planned template around the translated major), "N/A", or empty ("Undeclared", like
    /// ApplicantData.GetMajorText). Majors the table doesn't have come back as they are.
    /// </summary>
    public static string Major(string english)
    {
        if (string.IsNullOrWhiteSpace(english)) return Fixed(UndeclaredKey);
        string major = english.Trim();
        if (major.StartsWith(PlannedPrefix, StringComparison.Ordinal))
            return L10n.GetOr(Table, PlannedKey, English(PlannedKey), ("major", Major(major.Substring(PlannedPrefix.Length))));
        if (major == NoMajor) return Fixed(NoMajorKey);
        if (major == Undeclared) return Fixed(UndeclaredKey);
        return Lookup(MajorPrefix, major);
    }

    /// <summary>The company's default name ("New Company", CompanyNameUI's stored default) in the current language.</summary>
    public static string DefaultCompanyName => Fixed(DefaultCompanyKey);

    /// <summary>
    /// The name ApplicantGenerator gives an applicant when the scene has no name lists ("Applicant #427"), in the language
    /// of the moment it is made: it is saved as the person's name, like any other name.
    /// </summary>
    public static string FallbackName(int number) =>
        L10n.GetOr(Table, FallbackNameKey, English(FallbackNameKey), ("number", number.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    // ------------------------------------------------------------------

    static string Lookup(string prefix, string english)
    {
        if (string.IsNullOrEmpty(english)) return english ?? "";
        string slug = L10n.Slug(english);
        return slug.Length == 0 ? english : L10n.GetOr(Table, prefix + slug, english);
    }

    static string Fixed(string key) => L10n.GetOr(Table, key, English(key));

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

    /// <summary>
    /// The table's texts that aren't values of a data list (those are ApplicantGenerator's genders, races and degrees,
    /// TraitDatabase's traits and MajorDatabase's majors): key, English, note for translators. The Editor's content
    /// source writes them to Applicants.csv.
    /// </summary>
    public static readonly (string Key, string English, string Context)[] FixedTexts =
    {
        (DefaultCompanyKey, "New Company",
            "The company's name until the player names it (memo and CV letterheads, end screens, the save card, the name field's starting text). ~20 characters (at most 25: the name field's limit), written like a company name."),
        (PlannedKey, "Planned: {major}",
            "CV Major line of a high-school applicant: the major they plan to study. {major} = that major (a major.* entry, already translated). 22 px, shrinks, wraps to 2 lines in 216 px."),
        (NoMajorKey, "N/A",
            "CV Major line of a high-school applicant with no planned major. Short (~10 characters)."),
        (UndeclaredKey, "Undeclared",
            "Major line on a CV and in the staff files when there is no major. Short (~12 characters)."),
        (FallbackNameKey, "Applicant #{number}",
            "Name given to an applicant when the scene's name lists are missing (a rare fallback); it is saved as the person's name. {number} = 100 to 999."),
        (RacePrefix + "none", "None",
            "Old company stats panel: the most / least common ethnicity before anyone is hired. Short."),
        (RacePrefix + "unknown", "Unknown",
            "Old company stats panel: the ethnicity of an employee who has none on file. Short."),
        (DegreePrefix + "diploma", "Diploma",
            "Degree line on a CV and in the staff files for a high-school graduate (a high-school diploma; objectives say applicant.degree.high_school). Short (~14 characters)."),
        (DegreePrefix + "none", "None",
            "Degree line on a CV and in the staff files when there is no degree. Short."),
        (EducationPrefix + "none", "None",
            "Education line of the old 3D CV (where the applicant studied): nothing on file. Short."),
        (EducationPrefix + "high_school", "High School",
            "Education line of the old 3D CV: a high-school graduate. Short."),
        (EducationPrefix + "college", "College",
            "Education line of the old 3D CV: an associate degree (a two-year college). Short."),
        (EducationPrefix + "university", "University",
            "Education line of the old 3D CV: a bachelor's degree. Short."),
        (EducationPrefix + "grad_school", "Grad School",
            "Education line of the old 3D CV: a master's degree or a PhD. Short."),
        (EducationPrefix + "other", "Other",
            "Education line of the old 3D CV: any other degree. Short."),
    };
}
