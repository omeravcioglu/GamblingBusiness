using System;
using System.Collections.Generic;

// The manager system's saved state (ISaveParticipant "manager", JsonUtility). Every class tolerates missing fields: an
// older or partial save reads with the defaults below, and ManagerSystem.Sanitize repairs what doesn't fit.

/// <summary>Where the run stands with its manager.</summary>
public static class ManagerPhase
{
    /// <summary>The Manager feature hasn't opened yet.</summary>
    public const string None = "none";
    /// <summary>Three candidates wait for the player's pick.</summary>
    public const string Picking = "picking";
    /// <summary>A manager works here.</summary>
    public const string Active = "active";
    /// <summary>The manager left; the next candidates come on gapUntilDay.</summary>
    public const string Gap = "gap";
}

/// <summary>A manager (or a candidate for the job).</summary>
[Serializable]
public class ManagerPerson
{
    public string name = "";
    public string gender = "";
    public int portraitIndex = -1;
    public int age = 40;
    public int years = 10;              // years of experience (the CV line)
    public string personality = ManagerSchema.Conservative;
    public double salary;               // yearly
    public double expectedBase;         // what they expect per year at status tier 0 (their expectation grows with the company)
    public float satisfaction = 70f;
    public float competence = 1f;       // 0.85-1.15: how well "let me handle it" goes
    public int hiredDay;
    public int bio = 1;                 // which bio line (mgr.pers.<personality>.bio.<n>)
    public int raises;
    public int bonuses;
    public int promisesBroken;

    public bool IsEmpty => string.IsNullOrEmpty(name);
}

/// <summary>A resolved target of a pending decision: an employee (by id) or a candidate (the whole applicant).</summary>
[Serializable]
public class ManagerTargetState
{
    public string name = "";
    public string kind = "employee";    // "employee" or "candidate"
    public string employeeId = "";
    public string displayName = "";     // the name when it was resolved (texts keep working if they leave)
    public EmployeeSaveData candidate = new EmployeeSaveData();
}

/// <summary>A param worked out when the decision was created.</summary>
[Serializable]
public class ManagerParamState
{
    public string name = "";
    public double value;
    public string format = ManagerParams.FormatNumber;
}

/// <summary>A decision waiting on the desk.</summary>
[Serializable]
public class ManagerPending
{
    public int uid;
    public string situation = "";
    public int createdDay;
    public int expiresDay;
    public int variant = 1;             // which numbered message variant
    public string personality = "";     // the manager's personality when it was written (its text variant)
    public bool urgent;
    public bool interrupt;              // may open the desk by itself (once)
    public bool interruptDone;
    public bool revealed;               // "Ask the manager" answered
    public bool again;                  // asked again after "later"
    public bool withheld;               // an unhappy manager kept their recommendation to themselves
    public bool seen;                   // the player has looked at it
    public List<ManagerTargetState> targets = new List<ManagerTargetState>();
    public List<ManagerParamState> prms = new List<ManagerParamState>();
}

/// <summary>A situation scheduled for a later day (a follow-up, or a decision asked again).</summary>
[Serializable]
public class ManagerFollowup
{
    public string situation = "";
    public int dueDay;
    public string from = "";
    public bool again;
    public int variant = 1;
    public List<ManagerTargetState> targets = new List<ManagerTargetState>();
    public List<ManagerParamState> prms = new List<ManagerParamState>();
}

[Serializable]
public class ManagerNamedDay
{
    public string id = "";
    public int day;
}

/// <summary>A company policy: a daily cost and daily meter nudges while it runs.</summary>
[Serializable]
public class ManagerPolicy
{
    public string id = "";
    public string situation = "";
    public double costDay;
    public double costEmployee;
    public float happiness;
    public float trust;
    public float heat;
    public int startDay;
    public int untilDay;                // last day it runs (0 = until stopped)
}

/// <summary>Rented office seats (OfficeCapacity extra) for a daily rent.</summary>
[Serializable]
public class ManagerRental
{
    public string id = "";
    public int seats;
    public double rate;                 // per seat per day
    public int startDay;
    public int untilDay;                // last day it's rented
}

/// <summary>An investor's share of each positive day's operating result.</summary>
[Serializable]
public class ManagerShare
{
    public string id = "";
    public double pct;
    public int startDay;
    public int untilDay;
    public double paid;
}

/// <summary>A build-budget discount (ManagerBuildPerks).</summary>
[Serializable]
public class ManagerPerk
{
    public string id = "";
    public string category = "any";
    public float discount;              // percent
    public int untilDay;
    public int usesLeft = -1;           // -1 = unlimited
}

[Serializable]
public class ManagerMood
{
    public string id = "";
    public float mood = 50f;
}

[Serializable]
public class ManagerGambleNote
{
    public int day;
    public double stake;
    public double lost;
}

[Serializable]
public class ManagerHistory
{
    public string situation = "";
    public string choice = "";          // "" when it went unanswered without a default
    public int day;
    public string how = "";             // player, delegated, expired, unanswered
    // As the desk showed them (the desk's "Decided lately"; texts in the language of that day)
    public string title = "";
    public string choiceText = "";
    public string manager = "";
}

[Serializable]
public class ManagerStats
{
    public int created;
    public int decided;
    public int expired;
    public int followed;
    public int overridden;
    public int delegated;
    public int interrupts;
    public int resigned;
    public double moneyIn;
    public double moneyOut;
    public double recurringPaid;
}

[Serializable]
public class ManagerSaveState
{
    public int version = 1;
    public string phase = ManagerPhase.None;
    public ManagerPerson manager = new ManagerPerson();
    public List<ManagerPerson> candidates = new List<ManagerPerson>();
    public int gapUntilDay;
    public bool pickShown;              // the candidate pick opened by itself once
    public int managersHired;
    public int nextUid = 1;
    public int lastRollDay;
    public List<ManagerPending> pending = new List<ManagerPending>();
    public List<ManagerFollowup> followups = new List<ManagerFollowup>();
    public List<ManagerNamedDay> cooldowns = new List<ManagerNamedDay>();     // situation → day it last came up
    public List<ManagerNamedDay> categoryDays = new List<ManagerNamedDay>();  // category → day it last came up
    public List<ManagerNamedDay> flags = new List<ManagerNamedDay>();         // flag → last day (0 = for good)
    public List<string> seen = new List<string>();
    public List<string> chosen = new List<string>();                          // "situation.choice"
    public List<ManagerPolicy> policies = new List<ManagerPolicy>();
    public List<ManagerRental> rentals = new List<ManagerRental>();
    public List<ManagerShare> shares = new List<ManagerShare>();
    public List<ManagerPerk> perks = new List<ManagerPerk>();
    public List<ManagerMood> moods = new List<ManagerMood>();
    public List<double> recentProfits = new List<double>();                   // operating results, oldest first (7)
    public List<double> recentValues = new List<double>();                    // day-end values, oldest first (8)
    public List<ManagerGambleNote> gambles = new List<ManagerGambleNote>();   // last 7 days
    public List<int> firedDays = new List<int>();
    public List<int> hiredDays = new List<int>();
    public int losingDays;
    public int lowDays;                 // days in a row the manager's satisfaction was at rock bottom
    public ManagerStats stats = new ManagerStats();
    public List<ManagerHistory> history = new List<ManagerHistory>();
}
