using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class ApplicantData
{
    public string name;
    public int age;
    public string gender;
    public string race;
    public string degree;       // "High School", "Associate Degree", "Bachelor", "Master" or "PhD"
    public string major;        // Field of study (e.g., "Computer Science", "Underwater Basket Weaving")

    public float GPA;
    public int IQ;
    public int salary;
    public int experienceYears;

    public bool criminalRecord;

    public Sprite photo;
    // Portrait file number the photo came from (-1 = none), so a UI or save can reload it later (PortraitLibrary.Load)
    public int portraitIndex = -1;

    // Super rare golden applicant (1 in 100)
    public bool isGolden = false;

    public List<PersonalityTrait> traits = new List<PersonalityTrait>();

    // Stable employee id (a Guid, 32 hex digits) that the staff files (StaffLedger) and saves use. Given at hire
    // (CompanyManager.HireEmployee) or when a save from before ids loads; empty for applicants still on the CV pile.
    public string id = "";

    /// <summary>Give this person an id if they have none yet, and return it.</summary>
    public string EnsureId()
    {
        if (string.IsNullOrEmpty(id))
            id = NewId();
        return id;
    }

    /// <summary>A new unique employee id.</summary>
    public static string NewId()
    {
        return System.Guid.NewGuid().ToString("N");
    }

    /// <summary>
    /// Copy of this applicant with its own trait list (traits and photo are shared references).
    /// Used to remember the applicant exactly as the CV showed them.
    /// </summary>
    public ApplicantData Clone()
    {
        ApplicantData copy = (ApplicantData)MemberwiseClone();
        copy.traits = traits != null ? new List<PersonalityTrait>(traits) : new List<PersonalityTrait>();
        return copy;
    }

    /// <summary>Where the applicant studied, for the CV's Education line (e.g. "University" for a Bachelor).</summary>
    public string GetEducationText()
    {
        switch (degree)
        {
            case null:
            case "":
                return "None";
            case "High School":
                return "High School";
            case "Associate Degree":
                return "College";
            case "Bachelor":
                return "University";
            case "Master":
            case "PhD":
                return "Grad School";
            default:
                return "Other";
        }
    }

    /// <summary>Degree for the CV's Degree line. Uses the same names as hiring tasks (High School shows "Diploma").</summary>
    public string GetDegreeText()
    {
        if (string.IsNullOrEmpty(degree)) return "None";
        return degree == "High School" ? "Diploma" : degree;
    }

    /// <summary>Major for the CV ("Undeclared" when empty).</summary>
    public string GetMajorText()
    {
        return string.IsNullOrEmpty(major) ? "Undeclared" : major;
    }
}
