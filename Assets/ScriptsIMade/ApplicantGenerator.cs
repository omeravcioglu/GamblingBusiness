using System.Collections.Generic;
using UnityEngine;

public class ApplicantGenerator : MonoBehaviour
{
    public NameDatabase nameDB;
    public TraitDatabase traitDB;
    public PhotoDatabase photoDB;
    public MajorDatabase majorDB;

    [Header("Portraits (used when no PhotoDatabase is assigned)")]
    [Tooltip("Female portraits, loaded one file at a time: Resources/<folder>/portrait_<number>")]
    public PortraitSet femalePortraits = PortraitSet.DefaultFemale();
    [Tooltip("Male portraits, loaded one file at a time: Resources/<folder>/portrait_<number>")]
    public PortraitSet malePortraits = PortraitSet.DefaultMale();

    const int TraitsPerApplicant = 3;

    // Hiring pool skills (Productivity tier 6). "Quality" shifts IQ, GPA and experience upward.
    const float BaseGoldenChance = 0.01f;           // 1 in 100
    const float TalentScoutingGoldenBonus = 0.01f;  // Talent Scouting doubles it to 2%
    const float TalentScoutingQuality = 0.10f;      // Talent Scouting (a flag skill): +10% quality
    const float MaxCandidateQuality = 0.45f;        // all three skills together
    const float IQSkewReductionPerQuality = 1.5f;   // IQ skew power 2 -> 1.33 at max quality (fewer low-IQ applicants)
    const float IQBonusPerQuality = 15f;            // +6.75 IQ at max quality
    const float GPABonusPerQuality = 0.5f;          // +0.225 GPA at max quality
    const float BaseCriminalChance = 0.1f;          // 10%, fewer with better candidates

    static readonly string[] genders = { "Male", "Female", "Other" };
    static readonly string[] races = { "White", "Black", "Asian", "Hispanic", "Mixed", "Middle Eastern" };
    static readonly string[] degrees = { "High School", "Associate Degree", "Bachelor", "Master", "PhD" };

    // Fallback majors if no database assigned
    static readonly string[] fallbackMajors = {
        "Business Administration", "Computer Science", "Psychology", "Communications",
        "Engineering", "Biology", "English", "History", "Nursing", "Accounting",
        "Underwater Basket Weaving", "Meme Economics", "Professional Napping"
    };

    // Prestigious majors for golden applicants (when no database is assigned)
    static readonly string[] smartMajors = {
        "Computer Science", "Artificial Intelligence", "Quantum Physics",
        "Neuroscience", "Molecular Biology", "Aerospace Engineering",
        "Data Science", "Mathematics", "Biomedical Engineering",
        "Economics", "Robotics", "Machine Learning"
    };

    // The values an applicant can get. They are ids: saved, matched by hiring tasks and other code, and shown through
    // ApplicantText (the "Applicants" String Table); the Editor's ApplicantTextSource lists them for translation.
    public static IReadOnlyList<string> Genders => genders;
    public static IReadOnlyList<string> Races => races;
    public static IReadOnlyList<string> Degrees => degrees;
    public static IReadOnlyList<string> FallbackMajors => fallbackMajors;
    public static IReadOnlyList<string> SmartMajors => smartMajors;

    void Awake()
    {
        // Databases left empty in the Inspector: use the ones in the scene
        if (nameDB == null) nameDB = FindAnyObjectByType<NameDatabase>();
        if (traitDB == null) traitDB = FindAnyObjectByType<TraitDatabase>();
        if (photoDB == null) photoDB = FindAnyObjectByType<PhotoDatabase>();
    }

    // Weighted random - skews toward lower values
    float WeightedRandomLow(float min, float max, float power = 2f)
    {
        // Random.value^power skews toward 0, so we get more lower values
        float t = Mathf.Pow(Random.value, power);
        return Mathf.Lerp(min, max, t);
    }

    int WeightedRandomLowInt(int min, int max, float power = 2f)
    {
        float t = Mathf.Pow(Random.value, power);
        return Mathf.RoundToInt(Mathf.Lerp(min, max, t));
    }

    // Whole number in [min, max) like Random.Range, skewed toward max as quality grows (quality 0 = uniform)
    static int SkewedRangeHigh(int min, int max, float quality)
    {
        if (max <= min + 1) return min;
        float t = Mathf.Pow(Random.value, 1f - Mathf.Clamp(quality, 0f, 0.9f));
        return Mathf.Min(max - 1, min + Mathf.FloorToInt(t * (max - min)));
    }

    /// <summary>
    /// Candidate quality from the hiring pool skills (0 = normal, at most 0.45): Talent Scouting +10%,
    /// Headhunter Bonus +20%, Recruiting Network +15%.
    /// </summary>
    public static float GetCandidateQuality()
    {
        SkillTreeManager skills = SkillTreeManager.Instance;
        if (skills == null) return 0f;

        float quality = 0f;
        if (skills.HasEffect(SkillEffect.TalentScouting))
            quality += TalentScoutingQuality * skills.EffectMultiplier;
        quality += Mathf.Max(0f, skills.GetEffect(SkillEffect.HeadhunterBonus));
        quality += Mathf.Max(0f, skills.GetEffect(SkillEffect.RecruitingNetwork));
        return Mathf.Clamp(quality, 0f, MaxCandidateQuality);
    }

    /// <summary>Chance that an applicant is golden: 1%, 2% with Talent Scouting.</summary>
    public static float GetGoldenChance()
    {
        float baseChance = DemoMode.Active ? DemoMode.Config.goldenChance : BaseGoldenChance;   // Demo: the demo's base chance
        SkillTreeManager skills = SkillTreeManager.Instance;
        if (skills != null && skills.HasEffect(SkillEffect.TalentScouting))
            return baseChance + TalentScoutingGoldenBonus * skills.EffectMultiplier;
        return baseChance;
    }

    // Demo: applicants made in this scene, for the first batch's guaranteed golden CV
    int generatedThisScene;

    // Demo: the 2nd CV of a brand-new demo run is golden, once per run
    bool DemoGuaranteedGolden()
    {
        if (!DemoMode.Active || !DemoMode.Config.guaranteedFirstGolden || !DemoRun.Exists || DemoRun.GoldenDealt) return false;
        if (generatedThisScene != 2) return false;
        // Only once the save is applied: before that a continued run still reads as day 1 with nothing dealt
        if (SaveSystem.Instance != null && !SaveSystem.Instance.IsGameLoaded) return false;
        CompanyManager company = CompanyManager.Instance;
        if (company == null || company.currentDay != 1) return false;
        return RunStats.Instance == null || RunStats.Instance.Hires == 0;
    }

    public ApplicantData Generate()
    {
        ApplicantData a = new ApplicantData();
        float quality = GetCandidateQuality();
        generatedThisScene++;   // Demo

        // 1% chance for GOLDEN applicant (super rare!), 2% with Talent Scouting
        a.isGolden = Random.value < GetGoldenChance();
        if (!a.isGolden && DemoGuaranteedGolden()) a.isGolden = true;   // Demo
        if (a.isGolden && DemoMode.Active) DemoRun.GoldenDealt = true;   // Demo

        // Generate gender first, then get matching name
        a.gender = genders[Random.Range(0, genders.Length)];
        a.name = nameDB != null ? nameDB.GetRandomFullName(a.gender) : ApplicantText.FallbackName(Random.Range(100, 1000));
        a.age = Random.Range(18, 65);
        a.race = races[Random.Range(0, races.Length)];

        if (a.isGolden)
        {
            // GOLDEN APPLICANT - Amazing stats!
            a.degree = "PhD"; // Always PhD
            a.major = GetSmartMajor(); // Golden applicants get prestigious majors
            a.IQ = Random.Range(145, 170); // Genius level
            a.GPA = Random.Range(3.8f, 4.0f); // Near perfect
            a.salary = Random.Range(30000, 80000); // Surprisingly affordable!
            a.experienceYears = Random.Range(15, 30); // Very experienced
            a.criminalRecord = false; // Never
        }
        else
        {
            // Normal applicant generation
            a.degree = degrees[Random.Range(0, degrees.Length)];

            // Assign major based on education level
            if (a.degree == "High School")
            {
                // High schoolers get "Planned: Major" or just "N/A"
                if (Random.value < 0.7f)
                    a.major = "Planned: " + GetRandomMajor();
                else
                    a.major = "N/A";
            }
            else
            {
                a.major = GetRandomMajor();
            }

            // Weighted toward lower values - more dumb applicants (hiring pool skills soften the skew and add a bit)
            float iqPower = 2.0f - quality * IQSkewReductionPerQuality;
            a.IQ = WeightedRandomLowInt(70, 160, iqPower);    // Most will be 70-100 range
            if (quality > 0f)
                a.IQ = Mathf.Min(170, a.IQ + Mathf.RoundToInt(quality * IQBonusPerQuality));

            // GPA correlates with IQ - smarter people tend to have higher GPA
            float iqNormalized = Mathf.Clamp01((a.IQ - 70f) / 90f);  // 0 to 1 based on IQ (70-160)
            float gpaBase = Mathf.Lerp(1.5f, 3.2f, iqNormalized);  // Base GPA from IQ
            float gpaVariance = Random.Range(-0.5f, 0.8f);  // Some random variance
            a.GPA = Mathf.Clamp(gpaBase + gpaVariance + quality * GPABonusPerQuality, 1.5f, 4.0f);
            a.salary = DemoMode.Active   // Demo: the demo's narrower salary range
                ? Random.Range(DemoMode.Config.salaryMin, DemoMode.Config.salaryMax)
                : Random.Range(30000, 250000);

            // REALISTIC EXPERIENCE BASED ON AGE (better candidates lean toward the top of their bracket)
            if (a.age <= 21)
                a.experienceYears = SkewedRangeHigh(0, 2, quality);
            else if (a.age <= 25)
                a.experienceYears = SkewedRangeHigh(1, 5, quality);
            else if (a.age <= 30)
                a.experienceYears = SkewedRangeHigh(2, 8, quality);
            else if (a.age <= 40)
                a.experienceYears = SkewedRangeHigh(5, 15, quality);
            else if (a.age <= 50)
                a.experienceYears = SkewedRangeHigh(10, 25, quality);
            else
                a.experienceYears = SkewedRangeHigh(15, 40, quality);

            a.criminalRecord = Random.value < BaseCriminalChance * (1f - quality); // 10% chance, fewer with better candidates
        }

        // Assign photo based on gender (one portrait file is loaded, never the whole folder)
        if (photoDB != null)
            a.photo = photoDB.GetRandomPhoto(a.gender, out a.portraitIndex);
        else
            a.photo = PortraitLibrary.LoadRandom(a.gender, femalePortraits, malePortraits, out a.portraitIndex);

        AddTraits(a);

        return a;
    }

    // Always 3 unique traits (golden gets only positive traits).
    // Gives up after a while instead of looping forever if the trait list is too small.
    void AddTraits(ApplicantData a)
    {
        if (traitDB == null) return;

        List<PersonalityTrait> used = new List<PersonalityTrait>();
        for (int attempt = 0; attempt < 200 && a.traits.Count < TraitsPerApplicant; attempt++)
        {
            PersonalityTrait t = traitDB.GetRandomTrait();
            if (t == null) break;

            // Golden applicants only get positive traits
            if (a.isGolden && t.type != TraitType.Positive)
                continue;

            if (!used.Contains(t))
            {
                used.Add(t);
                a.traits.Add(t);
            }
        }
    }

    /// <summary>
    /// Get a random major (from database or fallback)
    /// </summary>
    string GetRandomMajor()
    {
        if (majorDB != null)
            return majorDB.GetRandomMajor();

        return fallbackMajors[Random.Range(0, fallbackMajors.Length)];
    }

    /// <summary>
    /// Get a prestigious/smart major for golden applicants
    /// </summary>
    string GetSmartMajor()
    {
        if (majorDB != null)
            return majorDB.GetRandomNormalMajor();

        return smartMajors[Random.Range(0, smartMajors.Length)];
    }
}
