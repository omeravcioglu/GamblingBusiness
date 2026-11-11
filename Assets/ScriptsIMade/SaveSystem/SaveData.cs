using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Contains all game state that needs to be persisted
/// </summary>
[System.Serializable]
public class SaveData
{
    /// <summary>Version written by the current SaveSystem. "1.0" saves have no extras.</summary>
    public const string CurrentVersion = "2.0";

    // Meta info
    public string saveVersion = CurrentVersion;
    public string savedAt;

    // Cloud save (CloudSaveSync): the run (company) this save belongs to, its write counter (+1 on every local write) and
    // the player it belongs to: the Unity player id, and the Steam ID (known offline too). Saves from before the cloud save
    // get a new run id and seq 0.
    public string runId;
    public long seq;
    public string ownerId;
    public string ownerSteamId;
    // The server seq this chain continued from when the save was written (CloudSaveSync.StampLocalWrite): a save whose sync
    // state is gone (set aside on a shared PC, another backend) can still tell EndRun and the next sync where it stood
    public long baseSeq;

    // Company state
    public double companyValue; // double: floats lose whole dollars above ~$16.7M (older float saves still read)
    public int currentDay;
    public bool hasHiredFirst;
    public float employeeHappiness = 50f;
    public float investorTrust = 50f;

    // Employees
    public List<EmployeeSaveData> employees = new List<EmployeeSaveData>();

    // Skill tree
    public int skillPoints;
    public int totalHighIQHires;
    public List<string> unlockedSkillIds = new List<string>();

    // Office items (by name -> level)
    public List<OfficeItemSaveData> officeItems = new List<OfficeItemSaveData>();

    // Online world
    public bool hasUnlockedOnlineWorld;
    public string companyName;

    // State of other systems (ISaveParticipant): SaveKey -> JSON from CaptureState()
    public List<ExtraSaveEntry> extras = new List<ExtraSaveEntry>();

    public SaveData()
    {
        savedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    }

    /// <summary>Replace null lists (older or hand-edited files) with empty ones.</summary>
    public void EnsureLists()
    {
        if (employees == null) employees = new List<EmployeeSaveData>();
        if (unlockedSkillIds == null) unlockedSkillIds = new List<string>();
        if (officeItems == null) officeItems = new List<OfficeItemSaveData>();
        if (extras == null) extras = new List<ExtraSaveEntry>();
    }
}

/// <summary>
/// Saved state of one ISaveParticipant (key = its SaveKey, json = its CaptureState()).
/// </summary>
[System.Serializable]
public class ExtraSaveEntry
{
    public string key;
    public string json;

    public ExtraSaveEntry() { }

    public ExtraSaveEntry(string key, string json)
    {
        this.key = key;
        this.json = json;
    }
}

/// <summary>
/// Serializable employee data (without Sprite)
/// </summary>
[System.Serializable]
public class EmployeeSaveData
{
    public string name;
    public int age;
    public string gender;
    public string race;
    public string degree;
    public string major;
    public float GPA;
    public int IQ;
    public int salary;
    public int experienceYears;
    public bool criminalRecord;
    public bool isGolden;
    public int portraitIndex = -1; // Photo number for PortraitLibrary.Load (-1 = no photo)
    public List<TraitSaveData> traits = new List<TraitSaveData>();
    public string id;              // Stable employee id (ApplicantData.id); saves from before ids get new ones on load

    public EmployeeSaveData() { }

    public EmployeeSaveData(ApplicantData applicant)
    {
        name = applicant.name;
        age = applicant.age;
        gender = applicant.gender;
        race = applicant.race;
        degree = applicant.degree;
        major = applicant.major;
        GPA = applicant.GPA;
        IQ = applicant.IQ;
        salary = applicant.salary;
        experienceYears = applicant.experienceYears;
        criminalRecord = applicant.criminalRecord;
        isGolden = applicant.isGolden;
        portraitIndex = applicant.portraitIndex;
        id = applicant.id;

        // Save traits
        if (applicant.traits != null)
        {
            foreach (var trait in applicant.traits)
            {
                if (trait == null) continue;
                traits.Add(new TraitSaveData(trait.traitName, trait.type));
            }
        }
    }

    /// <param name="loadPhoto">Load the portrait sprite (skip it when only converting between save formats).</param>
    public ApplicantData ToApplicantData(bool loadPhoto = true)
    {
        var applicant = new ApplicantData
        {
            name = this.name,
            age = this.age,
            gender = this.gender,
            race = this.race,
            degree = this.degree,
            major = this.major,
            GPA = this.GPA,
            IQ = this.IQ,
            salary = this.salary,
            experienceYears = this.experienceYears,
            criminalRecord = this.criminalRecord,
            isGolden = this.isGolden,
            portraitIndex = this.portraitIndex,
            photo = loadPhoto && this.portraitIndex >= 0 ? PortraitLibrary.Load(this.portraitIndex) : null,
            id = string.IsNullOrEmpty(this.id) ? ApplicantData.NewId() : this.id
        };

        // Restore traits
        applicant.traits = new List<PersonalityTrait>();
        if (traits != null)
        {
            foreach (var traitData in traits)
            {
                if (traitData == null) continue;
                applicant.traits.Add(new PersonalityTrait(traitData.traitName, traitData.type));
            }
        }

        return applicant;
    }
}

/// <summary>
/// Serializable trait data
/// </summary>
[System.Serializable]
public class TraitSaveData
{
    public string traitName;
    public TraitType type;

    public TraitSaveData() { }

    public TraitSaveData(string name, TraitType traitType)
    {
        traitName = name;
        type = traitType;
    }
}

/// <summary>
/// Serializable office item state
/// </summary>
[System.Serializable]
public class OfficeItemSaveData
{
    public string itemName;
    public int currentLevel;

    public OfficeItemSaveData() { }

    public OfficeItemSaveData(string name, int level)
    {
        itemName = name;
        currentLevel = level;
    }
}
