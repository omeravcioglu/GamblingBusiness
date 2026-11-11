using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One employee's page in the staff files (StaffLedger.GetStats). Money is per day; costs are positive numbers.
/// While hasWorkedADay is false (no day of theirs booked yet: hired since the last day ended, or on file only since then)
/// the day values are CompanyManager's projection.
/// </summary>
public struct StaffStats
{
    /// <summary>False when the person isn't (or no longer is) on staff; everything else is empty then.</summary>
    public bool onStaff;
    public string id;
    /// <summary>Day they were hired. When hireDayKnown is false (hired before the staff files were kept): the day the
    /// files first saw them ("on file since day N").</summary>
    public int hireDay;
    public bool hireDayKnown;
    /// <summary>Finished days on staff (on file, when the hire day isn't known).</summary>
    public int daysEmployed;
    /// <summary>At least one of their days is booked: the day values are that last day, not an estimate.</summary>
    public bool hasWorkedADay;
    /// <summary>Expected salary cost of a day now: annual salary / 365, less Tax Optimization (no Salary Roulette).</summary>
    public double salaryPerDay;
    /// <summary>The last finished day (or the projection): output, salary, criminal-record trouble, overhead share, per-employee skill money.</summary>
    public double dayOutput;
    public double daySalary;
    public double dayIncidents;
    public double dayOverhead;
    public double dayBonus;
    /// <summary>daySalary + dayIncidents + dayOverhead.</summary>
    public double dayCost;
    /// <summary>dayOutput + dayBonus - dayCost.</summary>
    public double profitPerDay;
    /// <summary>Every finished day's profit added up.</summary>
    public double lifetimeProfit;
    /// <summary>Days they caused criminal-record trouble.</summary>
    public int incidentDays;
}

/// <summary>Staff-wide numbers for the header of the staff files (StaffLedger.Totals).</summary>
public struct StaffTotals
{
    public int headcount;
    /// <summary>Expected salaries of a day now (annual / 365, less Tax Optimization).</summary>
    public double payrollPerDay;
    /// <summary>Everyone's output: their last finished day, or the projection for people hired since.</summary>
    public double outputPerDay;
    /// <summary>Everyone's profit per day, the same way.</summary>
    public double profitPerDay;
    public double lifetimeProfit;
}

/// <summary>
/// The staff files: a record per hired employee (by ApplicantData.id) with their hire day, days employed, what their
/// last day brought in and cost, and their lifetime profit; plus firing.
/// - Fed by CompanyManager: EmployeeHired opens a record, and on DayEnded each employee's share of the day
///   (CompanyManager.LastDayEmployees: exactly what the day's books added for them) goes into theirs. Profit = output +
///   per-employee skill money (Merger Mentality) - salary - $20 overhead share - criminal-record trouble. Company-wide
///   amounts (market swing, headcount synergy, robots, office items, buildings, the fixed $100 overhead, growth/interest
///   and luck skills) belong to nobody in particular.
/// - ProjectDailyProfit: what someone would bring in a day if hired now (CompanyManager.ProjectEmployeeDay), for CVs.
/// - Fire: the person leaves the company first, then severanceDays of salary is paid as severance (at most what the
///   company has: never below $0) and the rest of the staff lose MoraleDropFor happiness: firingMoraleHit
///   (criminalMoraleHit when the person had a record) softened by Employee Retention, plus, within farmingWindowDays of
///   the hire, whatever their traits added to morale when they were hired. Someone fired within farmingWindowDays whose
///   IQ counted toward skill points takes their hire back out of the count (the next point needs one more hire; points
///   already awarded stay: SkillTreeManager.TakeBackHighIQHire). Then a toast and, last, EmployeeFired.
/// - Saved under "staff". Employees from a save without staff files get a record on load (hire day unknown: "on file
///   since day N").
/// - StaffBootstrap adds this (and the StaffFilesView) to every scene with a CompanyManager, so MainGame needs no edit.
/// - The toast and the staff files' texts come from the "Staff" String Table (Assets/Localization/Source/Staff.csv)
///   through L10n; the save holds numbers and ids only.
/// </summary>
[DisallowMultipleComponent]
public class StaffLedger : MonoBehaviour, ISaveParticipant
{
    public const string Key = "staff";

    /// <summary>The String Table collection of the staff files' texts (Assets/Localization/Source/Staff.csv).</summary>
    public const string Table = "Staff";
    const double DaysPerYear = 365.0;

    public static StaffLedger Instance { get; private set; }

    /// <summary>Someone was fired: (who, severance paid). They are already off the staff when this fires.</summary>
    public static event Action<ApplicantData, double> EmployeeFired;
    /// <summary>The files changed: a day was booked, someone was hired or fired, or a save loaded.</summary>
    public static event Action Changed;

    [Header("Firing")]
    [Tooltip("Severance: this many days of salary (annual salary / 365 a day)")]
    public float severanceDays = 30f;
    [Tooltip("Happiness the rest of the staff lose when someone is fired (Employee Retention softens it)")]
    public float firingMoraleHit = 2f;
    [Tooltip("Happiness they lose instead when the fired person had a criminal record (nobody misses them)")]
    public float criminalMoraleHit = 0f;
    [Tooltip("Fired within this many days of being hired: their high-IQ hire is taken back out of the skill-point count, " +
             "and the morale their traits brought goes with them")]
    public int farmingWindowDays = 5;

    [Header("Debug")]
    [Tooltip("Log each firing to the Console")]
    public bool logFires = true;
    [Tooltip("Warn when the staff's share of a day doesn't add up to the day's books (Editor and development builds)")]
    public bool checkAgainstBooks = true;

    [Serializable]
    class Record
    {
        public string id;
        public int hireDay;
        public bool hireDayKnown;
        public int daysEmployed;
        public int lastDay;
        public double lastOutput;
        public double lastSalary;
        public double lastIncidents;
        public double lastOverhead;
        public double lastBonus;
        public double lastProfit;
        public double lifetimeProfit;
        public int incidentDays;
    }

    [Serializable]
    class SavedStaff
    {
        public int version = 1;
        public int fired;
        public double severancePaid;
        public List<Record> records = new List<Record>();
    }

    readonly Dictionary<string, Record> records = new Dictionary<string, Record>();
    CompanyManager hookedCompany;
    bool restoredThisLoad;
    int firedCount;
    double severancePaid;

    // Per-frame caches: the day factors search the scene for office items, the totals walk every employee
    CompanyManager.EmployeeDayFactors cachedFactors;
    CompanyManager factorsCompany;
    int factorsFrame = -1;
    StaffTotals cachedTotals;
    int totalsFrame = -1;
    int totalsVersion = -1;
    int version;

    /// <summary>Goes up whenever the files change (a cheap way for a view to notice).</summary>
    public int Version => version;
    /// <summary>People fired this run.</summary>
    public int FiredCount => firedCount;
    /// <summary>Severance paid this run.</summary>
    public double SeverancePaid => severancePaid;

    // ------------------------------------------------------------------
    // Life cycle
    // ------------------------------------------------------------------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Instance = null;
        EmployeeFired = null;
        Changed = null;
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[StaffLedger] Removed a second StaffLedger on '" + name + "' (only one may save under '" + Key + "').");
            Destroy(this);
            return;
        }
        Instance = this;
    }

    void OnEnable()
    {
        Hook();
    }

    void OnDisable()
    {
        Unhook();
    }

    void Start()
    {
        // The roster opens itself from the HUD's Staff tab (OfficeHud.StaffRequested)
        if (StaffFilesView.Instance == null && FindAnyObjectByType<StaffFilesView>(FindObjectsInactive.Include) == null)
        {
            GameObject go = new GameObject("StaffFilesView");
            go.transform.SetParent(transform, false);
            go.AddComponent<StaffFilesView>();
        }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        Hook();
    }

    void Hook()
    {
        CompanyManager company = CompanyManager.Instance;
        if (company == hookedCompany) return;
        Unhook();
        hookedCompany = company;
        if (company != null)
        {
            company.DayEnded += OnDayEnded;
            company.StateLoaded += OnStateLoaded;
            company.EmployeeHired += OnEmployeeHired;
        }
    }

    void Unhook()
    {
        if (hookedCompany != null)
        {
            hookedCompany.DayEnded -= OnDayEnded;
            hookedCompany.StateLoaded -= OnStateLoaded;
            hookedCompany.EmployeeHired -= OnEmployeeHired;
        }
        hookedCompany = null;
    }

    // ------------------------------------------------------------------
    // Feeding the files
    // ------------------------------------------------------------------

    void OnEmployeeHired(ApplicantData hire)
    {
        CompanyManager company = CompanyManager.Instance;
        if (hire == null || company == null) return;
        CreateRecord(hire, company.currentDay, true);
        MarkChanged();
    }

    void OnDayEnded(int newDay, double change)
    {
        CompanyManager company = hookedCompany != null ? hookedCompany : CompanyManager.Instance;
        if (company == null) return;
        BookDay(company, newDay - 1);
    }

    /// <summary>Add each employee's share of the day that just ended (CompanyManager.LastDayEmployees) to their record.</summary>
    void BookDay(CompanyManager company, int day)
    {
        IReadOnlyList<CompanyManager.EmployeeDay> shares = company.LastDayEmployees;
        double output = 0.0, salaries = 0.0, incidents = 0.0;
        for (int i = 0; i < shares.Count; i++)
        {
            CompanyManager.EmployeeDay share = shares[i];
            if (share.employee == null) continue;
            // Unknown to the files (hired while they weren't there): on file since the day being booked (currentDay
            // already counts the next one)
            Record record = FindRecord(share.employee) ?? CreateRecord(share.employee, day, false);
            double profit = share.Profit;
            record.daysEmployed++;
            record.lastDay = day;
            record.lastOutput = share.output;
            record.lastSalary = share.salary;
            record.lastIncidents = share.incidents;
            record.lastOverhead = share.overhead;
            record.lastBonus = share.perHeadBonus;
            record.lastProfit = profit;
            record.lifetimeProfit += profit;
            if (share.incidents > 0.0) record.incidentDays++;
            output += share.output;
            salaries += share.salary;
            incidents += share.incidents;
        }

        if (checkAgainstBooks && (Application.isEditor || Debug.isDebugBuild))
        {
            CompanyManager.DayBreakdown books = company.LastDayBreakdown;
            if (!Near(output, books.employeeOutput) || !Near(salaries, books.salaries) || !Near(incidents, books.incidents))
                Debug.LogWarning("[StaffLedger] Day " + day + ": the staff's shares (output " + output.ToString("0.##") + ", salaries " +
                                 salaries.ToString("0.##") + ", incidents " + incidents.ToString("0.##") + ") don't match the books (" +
                                 books.employeeOutput.ToString("0.##") + ", " + books.salaries.ToString("0.##") + ", " +
                                 books.incidents.ToString("0.##") + ").");
        }
        MarkChanged();
    }

    static bool Near(double a, double b)
    {
        return Math.Abs(a - b) <= Math.Max(0.01, Math.Abs(b) * 1e-6);
    }

    Record FindRecord(ApplicantData employee)
    {
        if (employee == null || string.IsNullOrEmpty(employee.id)) return null;
        return records.TryGetValue(employee.id, out Record record) ? record : null;
    }

    /// <summary>A new record: hired on <paramref name="day"/> (hiredNow), or on file since that day (hire day unknown).</summary>
    Record CreateRecord(ApplicantData employee, int day, bool hiredNow)
    {
        var record = new Record
        {
            id = employee.EnsureId(),
            hireDay = Mathf.Max(1, day),
            hireDayKnown = hiredNow
        };
        records[record.id] = record;
        return record;
    }

    /// <summary>Every employee has a unique id and a record; records of people who left are dropped.</summary>
    void Reconcile()
    {
        CompanyManager company = CompanyManager.Instance;
        if (company == null || company.employees == null) return;
        var onStaff = new HashSet<string>();
        foreach (ApplicantData employee in company.employees)
        {
            if (employee == null) continue;
            if (string.IsNullOrEmpty(employee.id) || onStaff.Contains(employee.id))
                employee.id = ApplicantData.NewId();   // no id yet, or a copy of someone else's
            onStaff.Add(employee.id);
            if (!records.ContainsKey(employee.id))
                CreateRecord(employee, company.currentDay, false);
        }
        if (records.Count <= onStaff.Count) return;
        var gone = new List<string>();
        foreach (string id in records.Keys)
            if (!onStaff.Contains(id)) gone.Add(id);
        foreach (string id in gone)
            records.Remove(id);
    }

    void MarkChanged()
    {
        version++;
        factorsFrame = -1;
        RaiseChanged();
    }

    // ------------------------------------------------------------------
    // Reading the files
    // ------------------------------------------------------------------

    /// <summary>
    /// The staff file of <paramref name="employee"/>: hire day, days employed, the last finished day's output, costs and
    /// profit (a projection until their first day ends), lifetime profit. onStaff is false for anyone not employed.
    /// </summary>
    public StaffStats GetStats(ApplicantData employee)
    {
        StaffStats stats = new StaffStats();
        CompanyManager company = CompanyManager.Instance;
        if (employee == null || company == null) return stats;

        Record record = FindRecord(employee);
        if (record == null)
        {
            if (company.employees == null || !company.employees.Contains(employee)) return stats;
            record = CreateRecord(employee, company.currentDay, false);   // on staff but unknown to the files (hired while they weren't there)
        }

        CompanyManager.EmployeeDayFactors factors = Factors(company);
        stats.onStaff = true;
        stats.id = record.id;
        stats.hireDay = record.hireDay;
        stats.hireDayKnown = record.hireDayKnown;
        stats.daysEmployed = record.daysEmployed;
        stats.hasWorkedADay = record.daysEmployed > 0;
        stats.salaryPerDay = CompanyManager.DailySalary(employee, factors.taxOptimization, 1f);
        stats.lifetimeProfit = record.lifetimeProfit;
        stats.incidentDays = record.incidentDays;

        if (stats.hasWorkedADay)
        {
            stats.dayOutput = record.lastOutput;
            stats.daySalary = record.lastSalary;
            stats.dayIncidents = record.lastIncidents;
            stats.dayOverhead = record.lastOverhead;
            stats.dayBonus = record.lastBonus;
        }
        else
        {
            CompanyManager.EmployeeDay projected = company.ProjectEmployeeDay(employee, false, factors);
            stats.dayOutput = projected.output;
            stats.daySalary = projected.salary;
            stats.dayIncidents = projected.incidents;
            stats.dayOverhead = projected.overhead;
            stats.dayBonus = projected.perHeadBonus;
        }
        stats.dayCost = stats.daySalary + stats.dayIncidents + stats.dayOverhead;
        stats.profitPerDay = stats.dayOutput + stats.dayBonus - stats.dayCost;
        return stats;
    }

    /// <summary>Headcount, payroll, output and profit per day of the whole staff (recounted at most once a frame).</summary>
    public StaffTotals Totals
    {
        get
        {
            if (totalsFrame != Time.frameCount || totalsVersion != version)
            {
                cachedTotals = ComputeTotals();
                totalsFrame = Time.frameCount;
                totalsVersion = version;
            }
            return cachedTotals;
        }
    }

    public int Headcount => Totals.headcount;
    public double PayrollPerDay => Totals.payrollPerDay;
    public double OutputPerDay => Totals.outputPerDay;
    public double ProfitPerDay => Totals.profitPerDay;

    StaffTotals ComputeTotals()
    {
        StaffTotals totals = new StaffTotals();
        CompanyManager company = CompanyManager.Instance;
        if (company == null || company.employees == null) return totals;
        foreach (ApplicantData employee in company.employees)
        {
            if (employee == null) continue;
            StaffStats stats = GetStats(employee);
            totals.headcount++;
            totals.payrollPerDay += stats.salaryPerDay;
            totals.outputPerDay += stats.dayOutput;
            totals.profitPerDay += stats.profitPerDay;
            totals.lifetimeProfit += stats.lifetimeProfit;
        }
        return totals;
    }

    /// <summary>
    /// Expected profit per day if <paramref name="applicant"/> were hired now: CompanyManager's formula with today's skills,
    /// productivity and happiness, Salary Negotiation and Onboarding Program applied and one more head for Team Synergy;
    /// output + per-employee skill money - salary - $20 overhead share - average criminal-record trouble. Random Raise,
    /// Salary Roulette and Lucky Hire (luck) are left out. For someone already on staff: their expected day as things stand.
    /// </summary>
    public double ProjectDailyProfit(ApplicantData applicant)
    {
        return ProjectDay(applicant).Profit;
    }

    /// <summary>The whole projected day behind ProjectDailyProfit (output, salary, trouble, overhead, skill money).</summary>
    public CompanyManager.EmployeeDay ProjectDay(ApplicantData applicant)
    {
        CompanyManager company = CompanyManager.Instance;
        if (company == null || applicant == null) return new CompanyManager.EmployeeDay { employee = applicant };
        bool onStaff = company.employees != null && company.employees.Contains(applicant);
        return company.ProjectEmployeeDay(applicant, !onStaff, Factors(company));
    }

    CompanyManager.EmployeeDayFactors Factors(CompanyManager company)
    {
        if (factorsFrame != Time.frameCount || factorsCompany != company)
        {
            cachedFactors = company.GetEmployeeDayFactors();
            factorsCompany = company;
            factorsFrame = Time.frameCount;
        }
        return cachedFactors;
    }

    /// <summary>The employee with the lowest profit per day (the newest of equals), or null without staff.</summary>
    public ApplicantData FindWorst()
    {
        CompanyManager company = CompanyManager.Instance;
        if (company == null || company.employees == null) return null;
        ApplicantData worst = null;
        double worstProfit = double.MaxValue;
        foreach (ApplicantData employee in company.employees)
        {
            if (employee == null) continue;
            double profit = GetStats(employee).profitPerDay;
            if (profit <= worstProfit)
            {
                worst = employee;
                worstProfit = profit;
            }
        }
        return worst;
    }

    // ------------------------------------------------------------------
    // Firing
    // ------------------------------------------------------------------

    /// <summary>Severance for <paramref name="employee"/>: severanceDays of salary (annual / 365), whole dollars.</summary>
    public double SeveranceFor(ApplicantData employee)
    {
        if (employee == null) return 0.0;
        return Math.Round(Math.Max(0, employee.salary) / DaysPerYear * Math.Max(0f, severanceDays));
    }

    /// <summary>What firing <paramref name="employee"/> costs right now: the severance, capped at the company's value.</summary>
    public double SeverancePayable(ApplicantData employee)
    {
        CompanyManager company = CompanyManager.Instance;
        double value = company != null ? Math.Max(0.0, company.companyValue) : 0.0;
        return Math.Min(SeveranceFor(employee), value);
    }

    /// <summary>
    /// Happiness the staff lose if <paramref name="employee"/> is fired now; Fire takes exactly this much. The firing hit
    /// (firingMoraleHit, or criminalMoraleHit for someone with a record; none when nobody else is left) softened by
    /// Employee Retention, plus HireMoraleTakenBack (someone hired within farmingWindowDays), within the 0-100 range.
    /// </summary>
    public float MoraleDropFor(ApplicantData employee)
    {
        CompanyManager company = CompanyManager.Instance;
        if (company == null || employee == null) return 0f;
        float before = company.employeeHappiness;
        float after = before;
        float hit = OthersOnStaff(employee) > 0 ? Mathf.Max(0f, employee.criminalRecord ? criminalMoraleHit : firingMoraleHit) : 0f;
        if (hit > 0f) after = CompanyManager.HappinessAfterChange(after, -hit);   // AddHappiness's math: Retention softens it
        float takenBack = HireMoraleTakenBack(employee);
        if (takenBack > 0f) after = Mathf.Clamp(after - takenBack, 0f, 100f);
        return Mathf.Max(0f, before - after);
    }

    /// <summary>
    /// The morale that firing <paramref name="employee"/> now takes back from their hire, on top of the firing hit: what
    /// their traits added when they were hired (CompanyManager.HireTraitHappiness), when they were hired within
    /// farmingWindowDays and it was a gain, so hire-and-fire can't pump morale. Taken in full, with or without anyone left
    /// (Employee Retention didn't soften the gain either, and happiness stays with the company). A hire that lowered
    /// morale gets nothing back: that loss may have stopped at 0, and refunding it in full would pump the other way.
    /// </summary>
    public float HireMoraleTakenBack(ApplicantData employee)
    {
        Record record = FindRecord(employee);
        if (record == null || !record.hireDayKnown || record.daysEmployed > farmingWindowDays) return 0f;
        return Mathf.Max(0f, CompanyManager.HireTraitHappiness(employee));
    }

    /// <summary>
    /// True when firing <paramref name="employee"/> now takes their hire back out of the skill tree's high-IQ hire count:
    /// hired within farmingWindowDays with IQ at the threshold. The next skill point then needs one more hire (a point is
    /// only paid when the count passes the highest multiple it has reached); points already awarded are never taken back.
    /// </summary>
    public bool WouldTakeBackSkillProgress(ApplicantData employee)
    {
        SkillTreeManager skills = SkillTreeManager.Instance;
        if (skills == null || employee == null || employee.IQ < skills.iqThreshold) return false;
        Record record = FindRecord(employee);
        if (record == null || !record.hireDayKnown || record.daysEmployed > farmingWindowDays) return false;
        return skills.totalHighIQHires > 0;
    }

    /// <summary>
    /// Fire <paramref name="employee"/>: take them off the staff (the tower's seats follow the headcount), then pay the
    /// severance (never taking the company below $0), lower the staff's happiness by MoraleDropFor, take their hire back
    /// out of the skill-point count if they were hired within farmingWindowDays, toast and, last, raise EmployeeFired.
    /// False if they aren't on staff.
    /// </summary>
    public bool Fire(ApplicantData employee)
    {
        CompanyManager company = CompanyManager.Instance;
        if (company == null || employee == null || company.employees == null || !company.employees.Contains(employee))
        {
            Debug.LogWarning("[StaffLedger] Can't fire " + LogName(employee) + ": not on staff.");
            return false;
        }

        // Everything is worked out while they are still on staff
        string who = DisplayName(employee);
        string logWho = LogName(employee);
        bool takeBack = WouldTakeBackSkillProgress(employee);
        float moraleDrop = MoraleDropFor(employee);
        double paid = SeverancePayable(employee);

        // Off the staff first (totals, averages and the office figure update and StatsChanged fires). A listener that
        // throws can't stop the firing halfway any more: paying first left them on staff with the severance paid, to be
        // fired and paid again
        Guarded(() => company.RemoveEmployee(employee));
        if (company.employees.Contains(employee)) return false;
        if (!string.IsNullOrEmpty(employee.id)) records.Remove(employee.id);
        firedCount++;

        // The staff who stay take it badly: exactly what MoraleDropFor promised (the confirmation shows the same)
        if (moraleDrop > 0f) company.employeeHappiness = Mathf.Clamp(company.employeeHappiness - moraleDrop, 0f, 100f);

        // Then the severance: firing is allowed even when it leaves the company with little (AddMoney stops at $0). Its
        // StatsChanged (raised even for $0) also shows the happiness change
        Guarded(() => company.AddMoney(-paid));
        severancePaid += paid;

        // Hired and fired in a hurry: their high-IQ hire no longer counts toward skill points
        bool tookBack = false;
        SkillTreeManager skills = SkillTreeManager.Instance;
        if (takeBack && skills != null)
        {
            int count = skills.totalHighIQHires;
            Guarded(() => skills.TakeBackHighIQHire());
            tookBack = skills.totalHighIQHires < count;
        }

        GameNotifications.Show(L10n.Get(Table, tookBack ? "staff.toast.fired.took_back" : "staff.toast.fired", ("name", who), ("amount", UIFormat.Money(paid))),
                               NotificationKind.Info);
        if (logFires)
            Debug.Log("[StaffLedger] Fired " + logWho + " (salary " + UIFormat.Money(employee.salary) + "/yr): severance " + UIFormat.Money(paid) +
                      ", staff happiness -" + moraleDrop.ToString("0.#") + (tookBack ? ", high-IQ hire taken back" : "") + ".");

        MarkChanged();
        RaiseFired(employee, paid);
        return true;
    }

    // A listener of the company's or skill tree's events that throws mustn't stop a firing halfway
    static void Guarded(Action step)
    {
        try { step(); }
        catch (Exception e) { Debug.LogException(e); }
    }

    int OthersOnStaff(ApplicantData employee)
    {
        CompanyManager company = CompanyManager.Instance;
        if (company == null || company.employees == null) return 0;
        int count = 0;
        foreach (ApplicantData other in company.employees)
            if (other != null && other != employee) count++;
        return count;
    }

    /// <summary>The employee's name for the player ("Someone" in the current language when they have none).</summary>
    public static string DisplayName(ApplicantData employee)
    {
        return employee != null && !string.IsNullOrWhiteSpace(employee.name) ? employee.name.Trim() : L10n.Get(Table, "staff.name.someone");
    }

    /// <summary>The employee's name for the Console (English "Someone" when they have none).</summary>
    public static string LogName(ApplicantData employee)
    {
        return employee != null && !string.IsNullOrWhiteSpace(employee.name) ? employee.name.Trim() : "Someone";
    }

    // ------------------------------------------------------------------
    // Save
    // ------------------------------------------------------------------

    public string SaveKey => Key;

    public string CaptureState()
    {
        var data = new SavedStaff { fired = firedCount, severancePaid = severancePaid };
        CompanyManager company = CompanyManager.Instance;
        if (company != null && company.employees != null)
        {
            // Only the current staff (in hiring order); ids are never changed here, the employees are already saved
            foreach (ApplicantData employee in company.employees)
            {
                Record record = FindRecord(employee);
                if (record != null) data.records.Add(record);
            }
        }
        else
        {
            data.records.AddRange(records.Values);
        }
        return JsonUtility.ToJson(data);
    }

    public void RestoreState(string json)
    {
        records.Clear();
        firedCount = 0;
        severancePaid = 0.0;

        SavedStaff data = null;
        if (!string.IsNullOrEmpty(json))
        {
            try { data = JsonUtility.FromJson<SavedStaff>(json); }
            catch (Exception e) { Debug.LogWarning("[StaffLedger] Couldn't read the saved staff files (starting them over): " + e.Message); }
        }
        if (data != null)
        {
            firedCount = Mathf.Max(0, data.fired);
            severancePaid = Finite(data.severancePaid);
            if (data.records != null)
            {
                foreach (Record record in data.records)
                {
                    if (record == null || string.IsNullOrEmpty(record.id)) continue;
                    record.daysEmployed = Mathf.Max(0, record.daysEmployed);
                    record.incidentDays = Mathf.Max(0, record.incidentDays);
                    record.hireDay = Mathf.Max(1, record.hireDay);
                    record.lastOutput = Finite(record.lastOutput);
                    record.lastSalary = Finite(record.lastSalary);
                    record.lastIncidents = Finite(record.lastIncidents);
                    record.lastOverhead = Finite(record.lastOverhead);
                    record.lastBonus = Finite(record.lastBonus);
                    record.lastProfit = Finite(record.lastProfit);
                    record.lifetimeProfit = Finite(record.lifetimeProfit);
                    records[record.id] = record;
                }
            }
        }
        restoredThisLoad = true;
    }

    // SaveSystem raises this after it applied a save (after RestoreState, when the save had staff files)
    void OnStateLoaded()
    {
        if (!restoredThisLoad)
        {
            // A save from before the staff files: everyone gets a fresh record
            records.Clear();
            firedCount = 0;
            severancePaid = 0.0;
        }
        restoredThisLoad = false;
        Reconcile();
        MarkChanged();
    }

    static double Finite(double value)
    {
        return double.IsNaN(value) || double.IsInfinity(value) ? 0.0 : value;
    }

    // ------------------------------------------------------------------

    static void RaiseChanged()
    {
        Action handlers = Changed;
        if (handlers == null) return;
        foreach (Action handler in handlers.GetInvocationList())
        {
            try { handler(); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }

    static void RaiseFired(ApplicantData employee, double paid)
    {
        Action<ApplicantData, double> handlers = EmployeeFired;
        if (handlers == null) return;
        foreach (Action<ApplicantData, double> handler in handlers.GetInvocationList())
        {
            try { handler(employee, paid); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}

/// <summary>
/// Adds the staff files (StaffLedger, which makes its StaffFilesView) to every scene that has a CompanyManager, when it
/// loads, unless they are already there. So MainGame needs no scene edit.
/// </summary>
static class StaffBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        EnsureLedger();   // the first scene loaded before this ran
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EnsureLedger();
    }

    static void EnsureLedger()
    {
        if (!Application.isPlaying) return;
        CompanyManager company = CompanyManager.Instance != null ? CompanyManager.Instance : UnityEngine.Object.FindAnyObjectByType<CompanyManager>();
        if (company == null) return;
        if (StaffLedger.Instance != null || UnityEngine.Object.FindAnyObjectByType<StaffLedger>(FindObjectsInactive.Include) != null) return;

        GameObject go = new GameObject("Staff (Runtime)");
        Scene scene = company.gameObject.scene;
        if (scene.IsValid() && scene.isLoaded && go.scene != scene)
        {
            // Live and die with the company's scene (and be found by SaveSystem before it restores the scene)
            try { SceneManager.MoveGameObjectToScene(go, scene); }
            catch (Exception e) { Debug.LogWarning("[Staff] Couldn't move the staff files next to the CompanyManager: " + e.Message); }
        }
        go.AddComponent<StaffLedger>();
    }
}
