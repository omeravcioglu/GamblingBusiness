using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Batch-only check of the staff files, in an empty scene (MainGame is never opened):
///   Unity.exe -batchmode -projectPath . -executeMethod StaffFilesSmoke.Run [-staffPreview &lt;dir&gt;] -logFile smoke.log
/// Hires made-up staff, runs days through CompanyManager's real day tick and checks that the staff's shares add up to
/// the day's books, that projections match the booked days, firing (severance, morale, headcount, events, toast, the
/// skill-point rules) and the save round trip. With -staffPreview it also renders the list, an open file and the
/// confirmation to PNGs (through the New UI tools' UIPreview, if present). Exit code 0 = everything passed.
/// </summary>
public static class StaffFilesSmoke
{
    const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    static readonly string[] FirstNames = { "Maya", "Haruto", "Emre", "Lucia", "Jonas", "Layla", "Daniel", "Ines", "Kofi", "Sofia", "Arjun", "Mei" };
    static readonly string[] LastNames = { "Collins", "Sato", "Yilmaz", "Fernandez", "Becker", "Haddad", "Brooks", "Moreau", "Mensah", "Rossi", "Patel", "Chen" };
    static readonly string[] Degrees = { "High School", "Associate Degree", "Bachelor", "Master", "PhD" };
    static readonly string[] Majors = { "Economics", "Physics", "Computer Science", "Underwater Basket Weaving", "History", "Marketing" };
    static readonly string[] SkillIds =
    {
        "prod_1_efficiency", "growth_1_salary", "growth_2_tax", "prod_1_training", "prod_2_quality",
        "prod_3_iq", "prod_3_phd", "prod_4_synergy", "growth_4_merger"
    };

    static int failures;
    static readonly List<string> log = new List<string>();

    public static void Run()
    {
        failures = 0;
        log.Clear();
        Application.logMessageReceived += OnLog;
        try
        {
            LogicChecks();
            string dir = Argument("-staffPreview");
            if (!string.IsNullOrEmpty(dir)) Previews(dir);
        }
        catch (Exception e)
        {
            Fail("Exception: " + e);
        }
        finally
        {
            Application.logMessageReceived -= OnLog;
        }
        Debug.Log("[StaffSmoke] " + (failures == 0 ? "PASSED" : failures + " FAILURE(S)") + "\n" + string.Join("\n", log));
        if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    static void OnLog(string message, string stack, LogType type)
    {
        if (type == LogType.Warning && message.StartsWith("[StaffLedger] Day")) Fail("Books check: " + message);
        if ((type == LogType.Exception || type == LogType.Error) && !message.StartsWith("[StaffSmoke]")) Fail("Logged error: " + message);
    }

    // ------------------------------------------------------------------
    // Logic
    // ------------------------------------------------------------------

    static void LogicChecks()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        World world = MakeWorld(40, true);
        CompanyManager company = world.company;
        StaffLedger ledger = world.ledger;
        SkillTreeManager skills = world.skills;

        Check(company.employees.Count == 40, "40 hires");
        var ids = new HashSet<string>();
        foreach (ApplicantData e in company.employees) ids.Add(e.id);
        Check(ids.Count == 40 && !ids.Contains("") && !ids.Contains(null), "every hire has its own id");
        Check(ledger.GetStats(company.employees[0]).onStaff && ledger.GetStats(company.employees[0]).hireDayKnown, "hire opens a record");

        // Days through the real tick; lifetime = the sum of the booked days
        var lifetime = new Dictionary<ApplicantData, double>();
        for (int d = 0; d < 6; d++)
        {
            company.employeeHappiness = 50f;   // neutral, so the projection below sees the same happiness as the day
            Call(company, "ProcessNewDay");
            double output = 0, salaries = 0, incidents = 0;
            foreach (CompanyManager.EmployeeDay share in company.LastDayEmployees)
            {
                output += share.output;
                salaries += share.salary;
                incidents += share.incidents;
                lifetime.TryGetValue(share.employee, out double sum);
                lifetime[share.employee] = sum + share.Profit;
            }
            CompanyManager.DayBreakdown books = company.LastDayBreakdown;
            Check(Near(output, books.employeeOutput) && Near(salaries, books.salaries) && Near(incidents, books.incidents),
                  "day " + books.day + ": shares add up to the books (output " + output.ToString("0.##") + " / " + books.employeeOutput.ToString("0.##") + ")");
            Check(Near(books.overhead, 100.0 + 20.0 * company.employees.Count), "day " + books.day + ": overhead still $100 + $20 a head");
        }
        ApplicantData first = company.employees[0];
        StaffStats s = ledger.GetStats(first);
        Check(s.daysEmployed == 6, "6 days employed (got " + s.daysEmployed + ")");
        Check(Near(s.lifetimeProfit, lifetime[first]), "lifetime profit = sum of the days");

        // Projection of someone on staff = their booked day (no dice: no Random Raise, Salary Roulette, and no record)
        ApplicantData clean = company.employees.Find(e => !e.criminalRecord);
        CompanyManager.EmployeeDay projected = company.ProjectEmployeeDay(clean, false);
        StaffStats cleanStats = ledger.GetStats(clean);
        Check(Near(projected.output, cleanStats.dayOutput) && Near(projected.salary, cleanStats.daySalary) && Near(projected.Profit, cleanStats.profitPerDay),
              "projection of a staff member matches their booked day (" + projected.Profit.ToString("0.##") + " / " + cleanStats.profitPerDay.ToString("0.##") + ")");

        // Projection of an applicant (Salary Negotiation, Onboarding, one more head) = their first booked day
        ApplicantData applicant = MakeApplicant(1001, iq: 118, criminal: false, golden: false, traits: false);
        double expected = ledger.ProjectDailyProfit(applicant);
        Check(!ledger.GetStats(applicant).onStaff, "an applicant isn't on staff");
        company.employeeHappiness = 50f;
        company.HireEmployee(applicant);
        company.employeeHappiness = 50f;
        StaffStats newcomer = ledger.GetStats(applicant);
        Check(!newcomer.hasWorkedADay && Near(newcomer.profitPerDay, expected), "a new hire shows the projection until their first day");
        Call(company, "ProcessNewDay");
        StaffStats booked = ledger.GetStats(applicant);
        Check(booked.hasWorkedADay && Near(booked.profitPerDay, expected),
              "ProjectDailyProfit = the first booked day (" + expected.ToString("0.##") + " / " + booked.profitPerDay.ToString("0.##") + ")");
        Check(booked.dayBonus > 0.0, "per-head skill bonus (Merger Mentality) is in the files");

        // A hire with mood traits changes the staff's happiness before their first day: the projection counts it
        company.employeeHappiness = 50f;
        ApplicantData cheerful = MakeApplicant(1003, iq: 112, criminal: false, golden: false, traits: true);   // two positive traits
        double cheerfulExpected = ledger.ProjectDailyProfit(cheerful);
        double plainExpected = ledger.ProjectDailyProfit(MakeApplicant(1003, iq: 112, criminal: false, golden: false, traits: false));
        Check(cheerfulExpected > plainExpected + 0.01, "the same CV with two positive traits projects more (" + cheerfulExpected.ToString("0.##") + " / " + plainExpected.ToString("0.##") + ")");
        company.HireEmployee(cheerful);
        Check(Mathf.Abs(company.employeeHappiness - 54f) < 0.001f, "two positive traits: happiness +4 on hire (got " + company.employeeHappiness + ")");
        Call(company, "ProcessNewDay");
        StaffStats cheerfulBooked = ledger.GetStats(cheerful);
        Check(cheerfulBooked.hasWorkedADay && Near(cheerfulBooked.profitPerDay, cheerfulExpected),
              "ProjectDailyProfit counts the hire's own happiness effect (" + cheerfulExpected.ToString("0.##") + " / " + cheerfulBooked.profitPerDay.ToString("0.##") + ")");

        // Someone the files never saw (on staff while they weren't there): on file since the day that booked them
        ApplicantData stranger = MakeApplicant(3001, iq: 105, criminal: false, golden: false, traits: false);
        stranger.EnsureId();
        company.employees.Add(stranger);
        int bookedDay = company.currentDay;
        Call(company, "ProcessNewDay");
        StaffStats strangerStats = ledger.GetStats(stranger);
        Check(!strangerStats.hireDayKnown && strangerStats.hireDay == bookedDay && strangerStats.hasWorkedADay && strangerStats.daysEmployed == 1,
              "a record opened while booking a day: on file since that day (" + strangerStats.hireDay + " / " + bookedDay + ")");
        Check(StaffText.Hired(strangerStats) == L10n.Get(StaffLedger.Table, "staff.hired.on_file", ("day", UIFormat.Int(bookedDay))),
              "wording: " + StaffText.Hired(strangerStats));

        // Totals
        StaffTotals totals = ledger.Totals;
        double payroll = 0;
        foreach (ApplicantData e in company.employees) payroll += ledger.GetStats(e).salaryPerDay;
        Check(totals.headcount == company.employees.Count && Near(totals.payrollPerDay, payroll), "totals: headcount and payroll");

        // Worst first
        ApplicantData worst = ledger.FindWorst();
        foreach (ApplicantData e in company.employees)
            if (ledger.GetStats(e).profitPerDay < ledger.GetStats(worst).profitPerDay - 1e-9) Fail("FindWorst missed " + e.name);

        FireChecks(company, ledger, skills);
        SaveChecks(company, ledger);
        SkillSaveChecks(skills);
    }

    static void FireChecks(CompanyManager company, StaffLedger ledger, SkillTreeManager skills)
    {
        string toast = null;
        ApplicantData firedWho = null;
        double firedPaid = -1;
        int statsEvents = 0;
        Action<string, NotificationKind> onToast = (m, k) => toast = m;
        Action<ApplicantData, double> onFired = (e, paid) => { firedWho = e; firedPaid = paid; };
        CompanyManager.OnCompanyStatsChanged onStats = () => statsEvents++;
        GameNotifications.Posted += onToast;
        StaffLedger.EmployeeFired += onFired;
        company.StatsChanged += onStats;
        try
        {
            // A plain firing: severance, morale, headcount, toast, event
            ApplicantData target = company.employees.Find(e => !e.criminalRecord && e.IQ < skills.iqThreshold);
            company.companyValue = 1000000;
            company.employeeHappiness = 60f;
            int before = company.employees.Count;
            double severance = Math.Round(target.salary / 365.0 * 30.0);
            Check(Near(ledger.SeveranceFor(target), severance), "severance = 30 days of salary");
            Check(Mathf.Abs(ledger.MoraleDropFor(target) - 2f) < 0.001f, "the confirmation's morale drop: 2 (got " + ledger.MoraleDropFor(target) + ")");
            Check(ledger.Fire(target), "Fire returns true");
            Check(company.employees.Count == before - 1 && company.totalEmployees == before - 1 && !company.employees.Contains(target), "off the staff, totals recounted");
            Check(Near(company.companyValue, 1000000 - severance), "severance paid from company value");
            Check(Mathf.Abs(company.employeeHappiness - 58f) < 0.001f, "staff morale -2 (got " + company.employeeHappiness + ")");
            Check(statsEvents > 0, "StatsChanged raised");
            Check(firedWho == target && Near(firedPaid, severance), "EmployeeFired(employee, severance)");
            Check(toast != null && toast.StartsWith(L10n.Get(StaffLedger.Table, "staff.toast.fired", ("name", target.name), ("amount", UIFormat.Money(severance)))),
                  "toast: " + toast);
            Check(!ledger.GetStats(target).onStaff, "their file is closed");
            Check(!ledger.Fire(target), "can't fire them twice");

            // Criminal record: nobody misses them
            ApplicantData crook = company.employees.Find(e => e.criminalRecord);
            if (crook != null)
            {
                company.employeeHappiness = 60f;
                ledger.Fire(crook);
                Check(Mathf.Abs(company.employeeHappiness - 60f) < 0.001f, "no morale hit for a criminal record");
            }

            // Not enough cash: pays what there is, never below $0
            ApplicantData poor = company.employees[company.employees.Count - 1];
            company.companyValue = 100;
            ledger.Fire(poor);
            Check(company.companyValue == 0.0 && Near(firedPaid, 100), "severance capped at the company's value, value stays at $0");
            company.companyValue = 1000000;

            // A quick fire takes back the morale the hire's traits brought, on top of the firing hit (no morale pump), and
            // the confirmation's number (MoraleDropFor) is exactly what Fire takes
            ApplicantData charmer = MakeApplicant(2201, iq: 90, criminal: false, golden: false, traits: true);   // two positive traits
            company.employeeHappiness = 60f;
            company.HireEmployee(charmer);
            Check(Mathf.Abs(company.employeeHappiness - 64f) < 0.001f, "two positive traits: +4 on hire (got " + company.employeeHappiness + ")");
            float promised = ledger.MoraleDropFor(charmer);
            Check(Mathf.Abs(promised - 6f) < 0.001f, "quick fire: the firing hit plus the +4 back (" + promised + ")");
            ledger.Fire(charmer);
            Check(Mathf.Abs(company.employeeHappiness - 58f) < 0.001f, "hire-and-fire nets only the firing hit (got " + company.employeeHappiness + ")");

            // A criminal record spares the firing hit only: the hire's morale still goes back
            ApplicantData charmingCrook = MakeApplicant(2203, iq: 90, criminal: true, golden: false, traits: true);
            company.employeeHappiness = 60f;
            company.HireEmployee(charmingCrook);
            Check(Mathf.Abs(ledger.MoraleDropFor(charmingCrook) - 4f) < 0.001f, "a crook's quick fire: only the +4 goes back (" + ledger.MoraleDropFor(charmingCrook) + ")");
            ledger.Fire(charmingCrook);
            Check(Mathf.Abs(company.employeeHappiness - 60f) < 0.001f, "...back where it was (got " + company.employeeHappiness + ")");

            // Employee Retention softens the firing hit, and the confirmation shows the softened number
            Skill retention = skills.GetSkill("prod_5_retention");
            if (retention != null)
            {
                retention.unlocked = true;
                skills.UpdateActiveEffects();
                ApplicantData longServing = company.employees.Find(e => !e.criminalRecord && ledger.GetStats(e).daysEmployed > ledger.farmingWindowDays);
                company.employeeHappiness = 60f;
                float softened = ledger.MoraleDropFor(longServing);
                ledger.Fire(longServing);
                Check(Mathf.Abs(softened - 1.4f) < 0.001f && Mathf.Abs(company.employeeHappiness - (60f - softened)) < 0.001f,
                      "Employee Retention: -1.4, as the confirmation said (" + softened + ", now " + company.employeeHappiness + ")");
                retention.unlocked = false;
                skills.UpdateActiveEffects();
            }
            else Fail("skill prod_5_retention is missing");

            // Hire-and-fire: the hire goes back out of the high-IQ count (the next point needs one more), points awarded stay
            int perPoint = Mathf.Max(1, skills.hiresPerSkillPoint);
            skills.SetHighIQHires(perPoint * 3 + 2);
            int points = skills.skillPoints;
            ApplicantData quick = MakeApplicant(2001, iq: 130, criminal: false, golden: false, traits: false);
            company.HireEmployee(quick);
            Check(skills.totalHighIQHires == perPoint * 3 + 3, "a high-IQ hire counts");
            Check(ledger.WouldTakeBackSkillProgress(quick), "fired at once: the hire would go back");
            ledger.Fire(quick);
            Check(skills.totalHighIQHires == perPoint * 3 + 2 && skills.skillPoints == points, "hire taken back, points untouched");

            // The hire that completes a point awards it once: fired, the count drops below the point, which stays, and
            // reaching it again pays nothing
            skills.SetHighIQHires(perPoint * 4 - 1);
            ApplicantData lucky = MakeApplicant(2002, iq: 130, criminal: false, golden: false, traits: false);
            company.HireEmployee(lucky);
            int awarded = skills.skillPoints;
            Check(awarded == points + 1 && skills.totalHighIQHires == perPoint * 4 && skills.HighIQPointsAwarded == 4, "the hire that completes a point awards it");
            Check(ledger.WouldTakeBackSkillProgress(lucky), "right after a point the hire still goes back");
            ledger.Fire(lucky);
            Check(skills.totalHighIQHires == perPoint * 4 - 1 && skills.skillPoints == awarded, "the awarded point stays, the count drops below it");
            Check(skills.GetHiresUntilNextPoint() == perPoint + 1 && skills.HighIQHiresTowardNextPoint == 0,
                  "the next point needs one more hire (" + skills.GetHiresUntilNextPoint() + " to go)");
            ApplicantData again = MakeApplicant(2003, iq: 130, criminal: false, golden: false, traits: false);
            company.HireEmployee(again);
            Check(skills.totalHighIQHires == perPoint * 4 && skills.skillPoints == awarded, "back at the point: it doesn't pay twice");
            ledger.Fire(again);
            Check(skills.totalHighIQHires == perPoint * 4 - 1 && skills.skillPoints == awarded, "hire-and-fire at the boundary farms nothing");

            // The batch version: a point's worth hired at once earns it; fired within the window, all of them go back; hired
            // again, they earn nothing
            skills.SetHighIQHires(perPoint * 5);
            int batchPoints = skills.skillPoints;
            for (int round = 0; round < 2; round++)
            {
                var batch = new List<ApplicantData>();
                for (int i = 0; i < perPoint; i++)
                {
                    ApplicantData hire = MakeApplicant(2100 + round * 50 + i, iq: 130, criminal: false, golden: false, traits: false);
                    company.HireEmployee(hire);
                    batch.Add(hire);
                }
                Check(skills.skillPoints == batchPoints + 1, "batch " + (round + 1) + " of " + perPoint + " high-IQ hires: one point in all (" + (skills.skillPoints - batchPoints) + ")");
                foreach (ApplicantData hire in batch) ledger.Fire(hire);
                Check(skills.totalHighIQHires == perPoint * 5 && skills.skillPoints == batchPoints + 1, "batch " + (round + 1) + " fired: every hire goes back, the point stays");
            }

            // After the window nothing goes back
            ApplicantData veteran = MakeApplicant(2004, iq: 130, criminal: false, golden: false, traits: false);
            company.HireEmployee(veteran);
            for (int d = 0; d < ledger.farmingWindowDays + 1; d++) Call(company, "ProcessNewDay");
            int counter = skills.totalHighIQHires;
            ledger.Fire(veteran);
            Check(skills.totalHighIQHires == counter, "fired after " + (ledger.farmingWindowDays + 1) + " days: progress stays");
        }
        finally
        {
            GameNotifications.Posted -= onToast;
            StaffLedger.EmployeeFired -= onFired;
            company.StatsChanged -= onStats;
        }
    }

    static void SaveChecks(CompanyManager company, StaffLedger ledger)
    {
        var before = new Dictionary<string, StaffStats>();
        foreach (ApplicantData e in company.employees) before[e.id] = ledger.GetStats(e);
        string json = ledger.CaptureState();
        ledger.RestoreState("");
        Call(ledger, "OnStateLoaded");   // a save without the key: fresh records, hire day unknown
        Check(!ledger.GetStats(company.employees[0]).hireDayKnown, "a save without staff files gives everyone a fresh record");
        ledger.RestoreState(json);
        Call(ledger, "OnStateLoaded");
        bool same = true;
        foreach (ApplicantData e in company.employees)
        {
            StaffStats a = before[e.id], b = ledger.GetStats(e);
            if (a.daysEmployed != b.daysEmployed || a.hireDay != b.hireDay || !Near(a.lifetimeProfit, b.lifetimeProfit) || !Near(a.profitPerDay, b.profitPerDay))
                same = false;
        }
        Check(same, "save round trip keeps every file (" + json.Length + " characters for " + company.employees.Count + " people)");

        // Employee ids in the save; saves from before ids get new ones
        var saved = new EmployeeSaveData(company.employees[0]);
        Check(saved.ToApplicantData(false).id == company.employees[0].id, "EmployeeSaveData keeps the id");
        saved.id = null;
        Check(!string.IsNullOrEmpty(saved.ToApplicantData(false).id), "an old save's employee gets an id on load");
    }

    static void SkillSaveChecks(SkillTreeManager skills)
    {
        // The high-water mark of the high-IQ count is saved under "skillHires" (SaveSystem restores it after ApplySavedState)
        int per = Mathf.Max(1, skills.hiresPerSkillPoint);
        List<string> owned = skills.GetUnlockedSkills().ConvertAll(k => k.id);
        skills.SetHighIQHires(per * 6);   // six points paid...
        skills.TakeBackHighIQHire();      // ...and a hire taken back: the count is below the sixth multiple
        string json = skills.CaptureState();
        int points = skills.skillPoints, count = skills.totalHighIQHires;
        Check(skills.SaveKey == "skillHires" && !string.IsNullOrEmpty(json), "the skill tree saves under 'skillHires': " + json);

        skills.ApplySavedState(points, count, owned);
        Check(skills.HighIQPointsAwarded == count / per, "a save without the entry: the count's multiples count as paid (" + skills.HighIQPointsAwarded + ")");
        skills.RestoreState(json);
        Check(skills.HighIQPointsAwarded == 6 && skills.GetHiresUntilNextPoint() == per + 1,
              "the saved mark comes back: next point in " + skills.GetHiresUntilNextPoint() + " hires, not 1");
        skills.RestoreState("{\"version\":1,\"pointsAwarded\":0,\"hiresPerPoint\":" + per + "}");
        Check(skills.HighIQPointsAwarded == 6, "a saved mark can only raise it");
        skills.RestoreState("{\"version\":1,\"pointsAwarded\":3,\"hiresPerPoint\":" + (per * 4) + "}");
        Check(skills.HighIQPointsAwarded == 12, "a mark saved with another hires-per-point is converted (" + skills.HighIQPointsAwarded + ")");
        skills.SetHighIQHires(count);
    }

    // ------------------------------------------------------------------
    // Previews
    // ------------------------------------------------------------------

    static void Previews(string dir)
    {
        Type preview = Type.GetType("UIPreview, Assembly-CSharp-Editor");
        MethodInfo render = preview != null ? preview.GetMethod("RenderWith", BindingFlags.Public | BindingFlags.Static) : null;
        if (render == null)
        {
            log.Add("Previews skipped: UIPreview.RenderWith isn't there.");
            return;
        }
        Directory.CreateDirectory(dir);
        string[] states = { "list", "file", "confirm", "scrolled" };
        foreach (string state in states)
        {
            var messages = new List<string>();
            string png = Path.Combine(dir, "staff_" + state + ".png");
            Func<Camera, List<GameObject>> setup = cam => PreviewSetup(cam, state);
            render.Invoke(null, new object[] { png, messages, setup });
            log.AddRange(messages);
        }
    }

    static List<GameObject> PreviewSetup(Camera cam, string state)
    {
        World world = MakeWorld(state == "scrolled" ? 320 : 18, false);
        for (int d = 0; d < 3; d++) Call(world.company, "ProcessNewDay");
        world.company.HireEmployee(MakeApplicant(9001, iq: 104, criminal: false, golden: false, traits: true));   // a new hire (estimate)

        var go = new GameObject("StaffFilesView");
        StaffFilesView view = go.AddComponent<StaffFilesView>();
        view.Open();
        Canvas canvas = go.GetComponentInChildren<Canvas>(true);
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = cam;
        canvas.planeDistance = 10f;
        Canvas.ForceUpdateCanvases();
        LoadPortraitsNow(view);
        Call(view, "FitFrame");

        ApplicantData golden = world.company.employees.Find(e => e.isGolden) ?? world.company.employees[0];
        if (state == "file") view.OpenFile(golden);
        if (state == "confirm")
        {
            view.OpenFile(golden);
            view.AskToFire(golden);
        }
        if (state == "scrolled")
        {
            view.Page(1);
            view.Page(1);
            view.Page(1);
            LoadPortraitsNow(view);
            int active = 0;
            foreach (StaffFileRow row in go.GetComponentsInChildren<StaffFileRow>(true))
                if (row.gameObject.activeSelf) active++;
            int total = go.GetComponentsInChildren<StaffFileRow>(true).Length;
            Check(total <= 12, "320 staff use a small pool of rows (" + total + " including the template, " + active + " showing)");
        }
        return new List<GameObject> { go, world.company.gameObject, world.skills.gameObject, world.ledger.gameObject };
    }

    // The view loads portraits in the background a couple a frame; a preview has no frames, so it waits for each batch
    static void LoadPortraitsNow(StaffFilesView view)
    {
        for (int i = 0; i < 20; i++)
        {
            Call(view, "LoadPortraits");
            view.FinishPortraitLoads();
        }
    }

    // ------------------------------------------------------------------
    // A made-up company
    // ------------------------------------------------------------------

    struct World
    {
        public CompanyManager company;
        public SkillTreeManager skills;
        public StaffLedger ledger;
    }

    static World MakeWorld(int staff, bool neutralHires)
    {
        var world = new World();
        world.company = new GameObject("Company").AddComponent<CompanyManager>();
        CompanyManager.Instance = world.company;
        world.company.logDailyBreakdown = false;

        world.skills = new GameObject("Skills").AddComponent<SkillTreeManager>();
        Call(world.skills, "Awake");
        foreach (string id in SkillIds)
        {
            Skill skill = world.skills.GetSkill(id);
            if (skill != null) skill.unlocked = true;
            else Fail("skill " + id + " is missing");
        }
        world.skills.UpdateActiveEffects();

        world.ledger = new GameObject("Staff").AddComponent<StaffLedger>();
        Call(world.ledger, "Awake");
        Call(world.ledger, "OnEnable");

        UnityEngine.Random.InitState(12345);
        for (int i = 0; i < staff; i++)
        {
            bool criminal = i % 7 == 3;
            bool golden = i == 1;
            int iq = 80 + (i * 37) % 70;
            ApplicantData hire = MakeApplicant(i, iq, criminal, golden, !neutralHires);
            if (!neutralHires)   // real portraits for the previews (PortraitSet.DefaultFemale / DefaultMale numbers)
                hire.portraitIndex = hire.gender == "Female" ? (i * 97) % 3699 : 3699 + (i * 89) % 3013;
            world.company.HireEmployee(hire);
        }
        world.company.employeeHappiness = 50f;
        return world;
    }

    static ApplicantData MakeApplicant(int n, int iq, bool criminal, bool golden, bool traits)
    {
        var a = new ApplicantData
        {
            name = FirstNames[n % FirstNames.Length] + " " + LastNames[(n / FirstNames.Length + n) % LastNames.Length],
            age = 22 + (n * 7) % 30,
            gender = n % 2 == 0 ? "Female" : "Male",
            race = "Mixed",
            degree = Degrees[(n * 3) % Degrees.Length],
            major = Majors[n % Majors.Length],
            GPA = 1.6f + ((n * 13) % 25) / 10f,
            IQ = iq,
            salary = 30000 + (n * 7919) % 110000,
            experienceYears = (n * 5) % 16,
            criminalRecord = criminal,
            isGolden = golden,
            portraitIndex = -1
        };
        if (traits)
        {
            a.traits.Add(new PersonalityTrait("Fast Learner", TraitType.Positive));
            a.traits.Add(new PersonalityTrait(n % 2 == 0 ? "Always Late" : "Addicted to Coffee", n % 2 == 0 ? TraitType.Negative : TraitType.Neutral));
            a.traits.Add(new PersonalityTrait("Team Player", TraitType.Positive));
        }
        return a;
    }

    // ------------------------------------------------------------------

    static void Call(object target, string method)
    {
        MethodInfo info = target.GetType().GetMethod(method, Any, null, Type.EmptyTypes, null);
        if (info == null) throw new MissingMethodException(target.GetType().Name, method);
        info.Invoke(target, null);
    }

    static bool Near(double a, double b)
    {
        return Math.Abs(a - b) <= Math.Max(0.001, Math.Abs(b) * 1e-6);
    }

    static void Check(bool ok, string what)
    {
        if (ok) log.Add("ok   " + what);
        else Fail(what);
    }

    static void Fail(string what)
    {
        failures++;
        log.Add("FAIL " + what);
    }

    static string Argument(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == name) return args[i + 1];
        return null;
    }
}
