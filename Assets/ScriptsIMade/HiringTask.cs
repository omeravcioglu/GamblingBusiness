using System.Globalization;
using UnityEngine;

/// <summary>
/// Defines what type of employee attribute the task checks
/// </summary>
public enum TaskRequirementType
{
    // Demographics
    HireRace,
    HireGender,
    HireAgeMin,
    HireAgeMax,
    HireAgeRange,
    
    // Education
    HireDegree,
    HireMajorContains,
    HireGPAMin,
    HireGPAMax,
    
    // Intelligence
    HireIQMin,
    HireIQMax,
    HireIQRange,
    
    // Experience & Salary
    HireExperienceMin,
    HireExperienceMax,
    HireSalaryMin,
    HireSalaryMax,
    
    // Special
    HireWithCriminalRecord,
    HireWithoutCriminalRecord,
    HireGolden,
    HireWithTrait,
    
    // Combo requirements
    HireAny // Just hire anyone (easy task)
}

/// <summary>
/// A hiring task/challenge with requirements and rewards
/// </summary>
[System.Serializable]
public class HiringTask
{
    [Header("Display")]
    public string title;
    [TextArea(2, 4)]
    public string description;
    
    [Header("Requirement")]
    public TaskRequirementType requirementType;
    public string targetValue;      // The value to match (e.g., "Black", "PhD", "90")
    public string targetValueMax;   // For range checks (e.g., IQ between 80-100)
    
    [Header("Rewards & Penalties")]
    public int rewardMoney = 5000;
    public int penaltyMoney = 2000;
    public float timeLimitSeconds = 90f;
    
    [Header("Difficulty")]
    [Range(1, 5)]
    public int difficulty = 1;      // 1 = easy, 5 = very hard
    
    public HiringTask() { }
    
    public HiringTask(string title, string desc, TaskRequirementType type, string target, 
                      int reward = 5000, int penalty = 2000, float timeLimit = 90f, int diff = 1)
    {
        this.title = title;
        this.description = desc;
        this.requirementType = type;
        this.targetValue = target;
        this.rewardMoney = reward;
        this.penaltyMoney = penalty;
        this.timeLimitSeconds = timeLimit;
        this.difficulty = diff;
    }
    
    public HiringTask(string title, string desc, TaskRequirementType type, string targetMin, string targetMax,
                      int reward = 5000, int penalty = 2000, float timeLimit = 90f, int diff = 1)
    {
        this.title = title;
        this.description = desc;
        this.requirementType = type;
        this.targetValue = targetMin;
        this.targetValueMax = targetMax;
        this.rewardMoney = reward;
        this.penaltyMoney = penalty;
        this.timeLimitSeconds = timeLimit;
        this.difficulty = diff;
    }
    
    // Warn only once per task about a target value that can't be read
    [System.NonSerialized] private bool warnedBadValue;

    /// <summary>
    /// The title in the current language (Hiring table, task.&lt;slug&gt;.title, see HiringText); the English title when
    /// the table has none. The data, matching and logs keep the English title.
    /// </summary>
    public string DisplayTitle => L10n.GetOr(HiringText.Table, HiringText.TaskKey(title) + ".title", title);

    /// <summary>The description in the current language (task.&lt;slug&gt;.desc, keyed by the title); the English when untranslated.</summary>
    public string DisplayDescription => L10n.GetOr(HiringText.Table, HiringText.TaskKey(title) + ".desc", description);

    /// <summary>
    /// Check if the given applicant meets this task's requirements.
    /// Min/Max limits are inclusive (the task text says "or lower" / "or less" / "+").
    /// </summary>
    public bool CheckRequirement(ApplicantData applicant)
    {
        if (applicant == null) return false;

        switch (requirementType)
        {
            case TaskRequirementType.HireRace:
                return SameText(applicant.race, targetValue);

            case TaskRequirementType.HireGender:
                return SameText(applicant.gender, targetValue);

            case TaskRequirementType.HireAgeMin:
                return ReadInt(targetValue, out int minAge) && applicant.age >= minAge;

            case TaskRequirementType.HireAgeMax:
                return ReadInt(targetValue, out int maxAge) && applicant.age <= maxAge;

            case TaskRequirementType.HireAgeRange:
                return ReadInt(targetValue, out int rangeAgeMin) && ReadInt(targetValueMax, out int rangeAgeMax)
                    && applicant.age >= rangeAgeMin && applicant.age <= rangeAgeMax;

            case TaskRequirementType.HireDegree:
                return SameText(applicant.degree, targetValue);

            case TaskRequirementType.HireMajorContains:
                return ContainsText(applicant.major, targetValue);

            // GPA is compared as shown on the CV (two decimals), so a CV showing 3.80 passes "GPA 3.8+"
            case TaskRequirementType.HireGPAMin:
                return ReadFloat(targetValue, out float minGpa) && GpaHundredths(applicant.GPA) >= GpaHundredths(minGpa);

            case TaskRequirementType.HireGPAMax:
                return ReadFloat(targetValue, out float maxGpa) && GpaHundredths(applicant.GPA) <= GpaHundredths(maxGpa);

            case TaskRequirementType.HireIQMin:
                return ReadInt(targetValue, out int minIq) && applicant.IQ >= minIq;

            case TaskRequirementType.HireIQMax:
                return ReadInt(targetValue, out int maxIq) && applicant.IQ <= maxIq;

            case TaskRequirementType.HireIQRange:
                return ReadInt(targetValue, out int rangeIqMin) && ReadInt(targetValueMax, out int rangeIqMax)
                    && applicant.IQ >= rangeIqMin && applicant.IQ <= rangeIqMax;

            case TaskRequirementType.HireExperienceMin:
                return ReadInt(targetValue, out int minExp) && applicant.experienceYears >= minExp;

            case TaskRequirementType.HireExperienceMax:
                return ReadInt(targetValue, out int maxExp) && applicant.experienceYears <= maxExp;

            case TaskRequirementType.HireSalaryMin:
                return ReadInt(targetValue, out int minSalary) && applicant.salary >= minSalary;

            case TaskRequirementType.HireSalaryMax:
                return ReadInt(targetValue, out int maxSalary) && applicant.salary <= maxSalary;

            case TaskRequirementType.HireWithCriminalRecord:
                return applicant.criminalRecord;

            case TaskRequirementType.HireWithoutCriminalRecord:
                return !applicant.criminalRecord;

            case TaskRequirementType.HireGolden:
                return applicant.isGolden;

            case TaskRequirementType.HireWithTrait:
                if (applicant.traits == null) return false;
                foreach (var trait in applicant.traits)
                {
                    if (trait != null && ContainsText(trait.traitName, targetValue))
                        return true;
                }
                return false;

            case TaskRequirementType.HireAny:
                return true;

            default:
                return false;
        }
    }

    bool ReadInt(string text, out int value)
    {
        if (TryParseInt(text, out value)) return true;
        WarnBadValue(text);
        return false;
    }

    bool ReadFloat(string text, out float value)
    {
        if (TryParseFloat(text, out value)) return true;
        WarnBadValue(text);
        return false;
    }

    void WarnBadValue(string text)
    {
        if (warnedBadValue) return;
        warnedBadValue = true;
        Debug.LogWarning($"[HiringTask] '{title}': target value '{text}' is not a valid number for {requirementType}.");
    }

    // ------------------------------------------------------------------
    // Helpers (culture-independent: "3.8" must read the same on a Turkish or German PC)
    // ------------------------------------------------------------------

    public static bool TryParseInt(string text, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        return int.TryParse(text.Trim(), NumberStyles.Integer | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Parses "3.8" (a comma decimal separator is accepted too).</summary>
    public static bool TryParseFloat(string text, out float value)
    {
        value = 0f;
        if (string.IsNullOrWhiteSpace(text)) return false;
        return float.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>GPA rounded the way the CV shows it (3.7996 -> 380).</summary>
    public static int GpaHundredths(float gpa)
    {
        return Mathf.RoundToInt(gpa * 100f);
    }

    static bool SameText(string a, string b)
    {
        if (a == null || b == null) return false;
        return string.Equals(a.Trim(), b.Trim(), System.StringComparison.OrdinalIgnoreCase);
    }

    static bool ContainsText(string text, string part)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(part)) return false;
        return text.IndexOf(part.Trim(), System.StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
