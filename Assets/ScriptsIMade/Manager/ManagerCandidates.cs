using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Makes people: the three candidates for the manager's job (different personalities, salaries and bios) and the
/// applicants that situations bring (a talented one, a cheap one, a star), through the scene's ApplicantGenerator so names
/// and portraits match the rest of the game.
/// </summary>
public static class ManagerCandidates
{
    /// <summary>
    /// What a manager earns relative to status tier 0: a bigger company pays its manager more (tiers 1-6 at $1M, $10M,
    /// $50M, $100M, $500M, $1B).
    /// </summary>
    static readonly double[] TierPayTable = { 1.0, 1.8, 3.2, 5.5, 9.0, 15.0, 25.0 };

    public static double TierPay(int tier) => TierPayTable[Mathf.Clamp(tier, 0, TierPayTable.Length - 1)];

    /// <summary>Yearly salary at tier 0 by personality (numbers people cost the most, conservative ones the least).</summary>
    static double BaseSalary(string personality)
    {
        switch (personality)
        {
            case ManagerSchema.Aggressive: return 105000;
            case ManagerSchema.People: return 90000;
            case ManagerSchema.Numbers: return 120000;
            default: return 82000;
        }
    }

    /// <summary><paramref name="count"/> candidates with different personalities, paid for the company's tier.</summary>
    public static List<ManagerPerson> Generate(int count, int tier)
    {
        var personalities = new List<string>(ManagerSchema.Personalities);
        for (int i = personalities.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            (personalities[i], personalities[j]) = (personalities[j], personalities[i]);
        }
        var list = new List<ManagerPerson>();
        var names = new HashSet<string>();
        for (int i = 0; i < count && i < personalities.Count; i++)
        {
            ManagerPerson person = Person(personalities[i], tier);
            for (int tries = 0; tries < 5 && names.Contains(person.name); tries++) person = Person(personalities[i], tier);
            names.Add(person.name);
            list.Add(person);
        }
        return list;
    }

    static ManagerPerson Person(string personality, int tier)
    {
        ApplicantData look = Applicant(null);
        float competence = UnityEngine.Random.Range(0.86f, 1.12f);
        // Better managers ask for more; the pay scales with the company's tier
        double baseSalary = BaseSalary(personality) * UnityEngine.Random.Range(0.88f, 1.12f) * (0.85 + 0.6 * (competence - 0.86));
        baseSalary = ManagerParams.RoundMoney(baseSalary);
        int age = look != null ? Mathf.Clamp(look.age, 29, 63) : UnityEngine.Random.Range(30, 60);
        return new ManagerPerson
        {
            name = look != null && !string.IsNullOrWhiteSpace(look.name) ? look.name.Trim() : "Alex Morgan",
            gender = look != null ? look.gender : "Other",
            portraitIndex = look != null ? look.portraitIndex : -1,
            age = age,
            years = Mathf.Clamp(age - 24 - UnityEngine.Random.Range(0, 6), 4, 35),
            personality = personality,
            salary = ManagerParams.RoundMoney(baseSalary * TierPay(tier)),
            expectedBase = baseSalary,
            competence = competence,
            bio = UnityEngine.Random.Range(1, 4),
            satisfaction = 70f
        };
    }

    /// <summary>
    /// An applicant for a candidate target rule (candidate_talent, candidate_cheap, candidate_star), or any applicant for
    /// null. Null when the scene can't make applicants.
    /// </summary>
    public static ApplicantData Applicant(string rule)
    {
        ApplicantGenerator generator = UnityEngine.Object.FindAnyObjectByType<ApplicantGenerator>();
        ApplicantData a = null;
        if (generator != null)
        {
            try { a = generator.Generate(); }
            catch (Exception e) { Debug.LogException(e); }
        }
        if (a == null)
        {
            a = new ApplicantData { name = "Sam Taylor", age = 34, gender = "Other", race = "Mixed", degree = "Bachelor", major = "Business Administration", GPA = 3.2f, IQ = 112, salary = 60000, experienceYears = 8 };
            a.photo = PortraitLibrary.LoadRandom(a.gender, PortraitSet.DefaultFemale(), PortraitSet.DefaultMale(), out a.portraitIndex);
        }
        a.photo = null;   // the portrait is loaded by number when shown
        switch (rule)
        {
            case "candidate_talent":
                a.isGolden = false;
                a.IQ = UnityEngine.Random.Range(125, 151);
                a.GPA = UnityEngine.Random.Range(3.3f, 4.0f);
                a.experienceYears = Mathf.Clamp(UnityEngine.Random.Range(5, 16), 0, Mathf.Max(0, a.age - 18));
                a.salary = UnityEngine.Random.Range(60, 141) * 1000;
                a.criminalRecord = false;
                if (a.degree == "High School" || a.degree == "Associate Degree") a.degree = "Master";
                break;
            case "candidate_cheap":
                a.isGolden = false;
                a.IQ = UnityEngine.Random.Range(100, 121);
                a.GPA = UnityEngine.Random.Range(2.8f, 3.6f);
                a.experienceYears = Mathf.Clamp(UnityEngine.Random.Range(2, 9), 0, Mathf.Max(0, a.age - 18));
                a.salary = UnityEngine.Random.Range(28, 49) * 1000;
                a.criminalRecord = false;
                break;
            case "candidate_star":
                a.isGolden = true;
                a.degree = "PhD";
                a.IQ = UnityEngine.Random.Range(145, 171);
                a.GPA = UnityEngine.Random.Range(3.8f, 4.0f);
                a.experienceYears = Mathf.Clamp(UnityEngine.Random.Range(15, 31), 0, Mathf.Max(0, a.age - 18));
                a.salary = UnityEngine.Random.Range(50, 91) * 1000;
                a.criminalRecord = false;
                if (a.traits != null) a.traits.RemoveAll(t => t == null || t.type == TraitType.Negative);
                break;
        }
        a.GPA = Mathf.Round(a.GPA * 100f) / 100f;
        a.id = "";
        return a;
    }
}
