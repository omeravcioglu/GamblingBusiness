using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using TMPro;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Batch smoke test of the manager (never touches the save: the test scene has no SaveSystem):
/// Unity.exe -batchmode -projectPath . -executeMethod ManagerSmoke.Run -logFile manager_smoke.log
/// 1. validates the situation files and Manager.csv (like Tools > Game > Manager > Validate Situations);
/// 2. enters Play mode in an empty scene with a CompanyManager, staff files, heat meter and eight employees, waits for the
///    candidate pick to open by itself, hires the first candidate and opens the desk;
/// 3. lays out every situation's desk page and the candidate pick in all five languages (a page that doesn't fit even with
///    its texts shrunk fails; texts spilling out of their boxes are listed);
/// 4. puts every situation on the desk and answers it with every choice in turn (one per frame, the desk rebuilding each
///    time), re-hiring a manager whenever one leaves;
/// 5. lets 25 game days pass fast (daily rolls, expiry, satisfaction, policies, rentals, follow-ups), answering whatever
///    lands on the desk;
/// 6. round-trips the save state through JSON.
/// Exit code 0 = no exception or error from the manager's code and no L10n formatting problem in the Manager table.
/// </summary>
[InitializeOnLoad]
public static class ManagerSmoke
{
    const string RunningKey = "ManagerSmoke.Running";
    const string StepKey = "ManagerSmoke.Step";
    const float TimeLimit = 300f;

    static readonly List<string> failures = new List<string>();
    static readonly List<(string situation, string choice)> queue = new List<(string, string)>();
    static int step;
    static float stepTime;
    static float startTime = -1f;
    static int decided, skipped, days;
    static int lastDay;
    static bool hooked;
    static readonly string[] Languages = { "en", "zh-Hans", "ja", "ko", "tr" };
    static int langIndex = -1, sitIndex, langFrames;
    static readonly List<string> shrunk = new List<string>();
    static readonly List<string> spilled = new List<string>();

    static ManagerSmoke()
    {
        // Entering Play mode reloads the domain: carry on from here
        if (SessionState.GetBool(RunningKey, false)) EditorApplication.update += Tick;
    }

    public static void Run()
    {
        List<ManagerProblem> problems = ManagerMenu.Run(out ManagerLibrary lib, out ManagerValidator.Texts texts);
        Debug.Log(ManagerValidator.Report(lib, problems, texts));
        foreach (ManagerProblem p in problems)
            if (p.IsError)
            {
                Debug.LogError("[ManagerSmoke] The files don't validate; not starting Play mode.");
                EditorApplication.Exit(1);
                return;
            }
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        // Other sessions edit files while this runs: an import mid-run would reload the domain and restart the test
        AssetDatabase.DisallowAutoRefresh();
        SessionState.SetBool(RunningKey, true);
        SessionState.SetInt(StepKey, 0);
        EditorApplication.update += Tick;
        EditorApplication.EnterPlaymode();
    }

    static void Log(string message) => Debug.Log("[ManagerSmoke] " + message);

    static void OnLog(string condition, string stack, LogType type)
    {
        bool mine = (condition != null && (condition.Contains("Manager") || condition.Contains("manager"))) ||
                    (stack != null && stack.Contains("Manager"));
        if ((type == LogType.Exception || type == LogType.Error || type == LogType.Assert) && mine && !condition.StartsWith("[ManagerSmoke]"))
            failures.Add(type + ": " + condition + "\n" + stack);
        if (type == LogType.Warning && condition != null && condition.StartsWith("[L10n]") && condition.Contains("Manager/"))
            failures.Add("L10n: " + condition);
        if (type == LogType.Warning && condition != null && condition.StartsWith("[Manager] Effect"))
            failures.Add("Effect: " + condition);
    }

    static void Tick()
    {
        if (!Application.isPlaying) return;   // still entering Play mode
        if (!hooked)
        {
            hooked = true;
            Application.logMessageReceived += OnLog;
            startTime = Time.realtimeSinceStartup;
            if (SessionState.GetInt(StepKey, 0) > 0)
            {
                failures.Add("The domain reloaded in the middle of the test (at step " + SessionState.GetInt(StepKey, 0) + "): files changed on disk. Run it again.");
                Finish();
                return;
            }
        }
        if (Time.realtimeSinceStartup - startTime > TimeLimit)
        {
            failures.Add("Timed out at step " + step);
            Finish();
            return;
        }
        try
        {
            Advance();
        }
        catch (Exception e)
        {
            failures.Add("Smoke step " + step + " threw: " + e);
            Finish();
        }
    }

    static void Advance()
    {
        ManagerSystem system = ManagerSystem.Instance;
        switch (step)
        {
            case 0:
                SetUp();
                Next(1);
                break;

            case 1:   // the pick opens by itself once the screen is calm
                if (system == null || !system.IsStateKnown) return;
                if (!system.IsPickOpen)
                {
                    if (Time.realtimeSinceStartup - stepTime > 15f)
                    {
                        failures.Add("The candidate pick didn't open by itself (phase " + system.Phase + "). " + CalmReport());
                        Finish();
                    }
                    return;
                }
                if (system.Candidates.Count != 3) failures.Add("Expected 3 candidates, got " + system.Candidates.Count + ".");
                var styles = new HashSet<string>();
                foreach (ManagerPerson c in system.Candidates) styles.Add(c.personality);
                if (styles.Count != system.Candidates.Count) failures.Add("Candidates share a personality.");
                Log("Candidates: " + Describe(system.Candidates));
                Hire(system);
                Next(2);
                break;

            case 2:   // the desk shows their first message
                if (Frames() < 3) return;
                if (!system.IsDeskOpen) failures.Add("The desk didn't open after the hire.");
                bool welcome = false;
                foreach (ManagerPending p in system.Pending) if (p.situation == "mgr_welcome") welcome = true;
                if (!welcome) failures.Add("No welcome decision after the hire.");
                foreach (ManagerSituationDef def in system.Library.Situations)
                    foreach (ManagerChoiceDef c in def.Choices)
                        queue.Add((def.Id, c.Id));
                Log(queue.Count + " choices to play.");
                Next(10);
                break;

            case 10:   // layout: every page in every language, and the candidate pick
                Layout(system);
                break;

            case 3:   // every choice of every situation, one per frame, with the desk open
                if (queue.Count == 0)
                {
                    Log("Played " + decided + " choices, skipped " + skipped + ".");
                    if (system.IsDeskOpen) CloseDesk();
                    lastDay = CompanyManager.Instance.currentDay;
                    Time.timeScale = 30f;
                    Next(4);
                    return;
                }
                PlayOne(system, queue[0].situation, queue[0].choice);
                queue.RemoveAt(0);
                break;

            case 4:   // days go by: rolls, expiry, drift, costs, follow-ups
                KeepGoing(system);
                int day = CompanyManager.Instance.currentDay;
                if (day != lastDay)
                {
                    days += day - lastDay;
                    lastDay = day;
                    if (days % 5 == 0) Log("Day " + day + ": " + system.PendingCount + " waiting, phase " + system.Phase +
                                           (system.HasManager ? ", satisfaction " + system.Manager.satisfaction.ToString("0") : "") +
                                           ", value " + UIFormat.Money(CompanyManager.Instance.companyValue) + ".");
                }
                if (days >= 25)
                {
                    Time.timeScale = 1f;
                    Next(5);
                }
                break;

            case 5:   // the save round-trips
            {
                // JsonUtility writes a double with 17 digits the first time and its shortest form after a re-read
                // (13044.510345697401 -> 13044.5103456974, the same number): compare once it has settled
                string json = JsonUtility.ToJson(JsonUtility.FromJson<ManagerSaveState>(system.CaptureState()));
                ManagerSaveState copy = JsonUtility.FromJson<ManagerSaveState>(json);
                string again = JsonUtility.ToJson(copy);
                ManagerSaveState live = system.DebugState;
                if (copy.pending.Count != live.pending.Count || copy.followups.Count != live.followups.Count || copy.phase != live.phase ||
                    copy.manager.name != live.manager.name || copy.moods.Count != live.moods.Count)
                    failures.Add("The save state lost something on the way through JSON.");
                if (again != json)
                {
                    int i = 0;
                    while (i < json.Length && i < again.Length && json[i] == again[i]) i++;
                    int from = Math.Max(0, i - 120);
                    failures.Add("The save state doesn't round-trip through JSON (first difference at " + i + "):\n  saved: " +
                                 json.Substring(from, Math.Min(240, json.Length - from)) + "\n  again: " + again.Substring(from, Math.Min(240, again.Length - from)));
                }
                Log("Save state: " + json.Length + " characters; stats " + JsonUtility.ToJson(system.Stats));
                if (DailyEconomyHooks.Count < 1) failures.Add("The manager isn't registered with DailyEconomyHooks.");
                Finish();
                break;
            }
        }
    }

    /// <summary>What ManagerSystem.IsCalm is made of, for a failure message.</summary>
    static string CalmReport()
    {
        var sb = new StringBuilder("Calm check: ");
        sb.Append("ManagerSystem.IsCalm=").Append(ManagerSystem.IsCalm());
        sb.Append(", RunEndDirector.IsScreenCalm=").Append(RunEndDirector.IsScreenCalm());
        sb.Append(", panel open=").Append(UIManager.Exists && UIManager.Instance.IsAnyPanelOpen());
        sb.Append(", typing=").Append(UIManager.IsTypingInInputField());
        sb.Append(", timeScale=").Append(Time.timeScale);
        sb.Append(", unscaledDeltaTime=").Append(Time.unscaledDeltaTime);
        sb.Append(", SaveSystem=").Append(SaveSystem.Instance != null);
        sb.Append(", CityOnline pending=").Append(CityOnline.Instance != null && CityOnline.Instance.HasPendingTransfers);
        sb.Append(", RunEndDirector=").Append(RunEndDirector.Instance != null);
        sb.Append(", BuildModeCamera=").Append(BuildModeCamera.Instance != null);
        sb.Append(", Tutorial=").Append(TutorialDirector.Instance != null && TutorialDirector.Instance.IsShowing);
        sb.Append(", mouse=").Append(Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButtonUp(0) || Input.GetMouseButtonUp(1));
        return sb.ToString();
    }

    static void Layout(ManagerSystem system)
    {
        if (langIndex < 0 || sitIndex >= system.Library.Situations.Count)
        {
            if (langIndex >= 0) CheckPick(system, Languages[langIndex]);
            langIndex++;
            if (langIndex >= Languages.Length)
            {
                L10n.SetLanguage("en");
                Log("Layout: " + shrunk.Count + " pages needed smaller text; " + spilled.Count + " texts spill out of their boxes.");
                foreach (string s in shrunk) Log("  smaller text: " + s);
                foreach (string s in spilled) Log("  spills: " + s);
                Next(3);
                return;
            }
            L10n.SetLanguage(Languages[langIndex]);
            sitIndex = 0;
            langFrames = 0;
            return;
        }
        string lang = Languages[langIndex];
        if (L10n.CurrentCode != lang)
        {
            if (++langFrames > 600) { failures.Add("The language didn't switch to " + lang + "."); Finish(); }
            return;
        }
        ManagerSituationDef def = system.Library.Situations[sitIndex++];
        Prepare(system);
        if (!system.Force(def.Id, true, out string why))
        {
            Log("Layout skip " + lang + " " + def.Id + ": " + why);
            return;
        }
        ManagerPending pending = null;
        foreach (ManagerPending p in system.Pending) if (p.situation == def.Id) pending = p;
        ManagerDeskView desk = UnityEngine.Object.FindAnyObjectByType<ManagerDeskView>();
        if (pending == null || desk == null) { failures.Add("Layout: no desk or decision for " + def.Id); return; }
        desk.Open(pending.uid);
        if (!desk.LastFit) failures.Add("Layout: " + lang + " " + def.Id + " doesn't fit the page even with smaller text.");
        else if (desk.LastTextScale < 1f) shrunk.Add(lang + " " + def.Id);
        CheckTexts(desk, lang + " " + def.Id);
        system.DebugDiscard(pending.uid);
    }

    /// <summary>The candidate pick in this language: a resignation, three new candidates, the pick, then a hire again.</summary>
    static void CheckPick(ManagerSystem system, string lang)
    {
        if (system.HasManager) system.Resign("smoke layout");
        system.StartPicking("smoke layout");
        system.OpenPick();
        ManagerPickView pick = UnityEngine.Object.FindAnyObjectByType<ManagerPickView>();
        if (pick == null || !pick.IsOpen) failures.Add("Layout: the candidate pick didn't open in " + lang + ".");
        else CheckTexts(pick, lang + " pick");
        if (!system.PickCandidate(0)) failures.Add("Layout: PickCandidate failed in " + lang + ".");
        system.OpenDesk();
    }

    static void CheckTexts(Component root, string where)
    {
        foreach (TMP_Text t in root.GetComponentsInChildren<TMP_Text>(false))
        {
            // Coming Soon's glyphs (about 1.5 em) are taller than the kit's CSS line boxes, so TMP calls most lines
            // "overflowing": only a spill of half a line or more, or a single line wider than its box, counts
            t.ForceMeshUpdate();
            if (string.IsNullOrEmpty(t.text) || t.enableAutoSizing) continue;
            Rect box = t.rectTransform.rect;
            bool tooTall = t.preferredHeight > box.height + t.fontSize * 0.5f;
            bool tooWide = t.textWrappingMode == TextWrappingModes.NoWrap && t.preferredWidth > box.width + 2f;
            if (!tooTall && !tooWide) continue;
            string text = t.text ?? "";
            if (text.Length > 60) text = text.Substring(0, 60) + "...";
            spilled.Add(where + ": " + t.name + " \"" + text.Replace("\n", " ") + "\"");
        }
    }

    /// <summary>A manager, money and staff for the next forced decision.</summary>
    static void Prepare(ManagerSystem system)
    {
        CompanyManager company = CompanyManager.Instance;
        if (!system.HasManager) Hire(system);
        if (company.companyValue < 500000) company.AddMoney(500000 - company.companyValue, true);
        while (company.employees.Count < 8) company.HireEmployee(Person(company.employees.Count + 20));
    }

    static void Next(int s)
    {
        step = s;
        SessionState.SetInt(StepKey, s);
        stepTime = Time.realtimeSinceStartup;
        frames = 0;
    }

    static int frames;
    static int Frames() => ++frames;

    static void SetUp()
    {
        // ProjectSettings/TimeManager.asset holds timeScale 0; the game's menu and scene changes set 1, this scene has neither
        Time.timeScale = 1f;
        var company = new GameObject("Company").AddComponent<CompanyManager>();
        company.logDailyBreakdown = false;
        company.companyValue = company.displayedValue = 400000;
        company.currentDay = 6;
        new GameObject("Staff").AddComponent<StaffLedger>();
        new GameObject("Heat").AddComponent<HeatMeter>();
        new GameObject("Applicants").AddComponent<ApplicantGenerator>();
        for (int i = 0; i < 8; i++) company.HireEmployee(Person(i));
        ManagerSystem.Ensure();
        GambleLedger.LogReports = false;
        Log("Set up: " + company.employees.Count + " employees, " + UIFormat.Money(company.companyValue) + ", day " + company.currentDay + ".");
    }

    static ApplicantData Person(int i)
    {
        return new ApplicantData
        {
            name = "Test Person " + (i + 1),
            age = 25 + i * 3,
            gender = i % 2 == 0 ? "Female" : "Male",
            race = "Mixed",
            degree = i % 3 == 0 ? "Master" : "Bachelor",
            major = "Accounting",
            GPA = 2.5f + 0.15f * i,
            IQ = 90 + i * 7,
            salary = 40000 + 15000 * i,
            experienceYears = 2 + i,
            portraitIndex = -1
        };
    }

    static void Hire(ManagerSystem system)
    {
        if (system.Phase != ManagerPhase.Picking) system.StartPicking("smoke");
        if (!system.PickCandidate(0)) failures.Add("PickCandidate failed.");
        system.OpenDesk();
    }

    static void PlayOne(ManagerSystem system, string situation, string choice)
    {
        CompanyManager company = CompanyManager.Instance;
        if (!system.HasManager) Hire(system);
        if (company.companyValue < 500000) company.AddMoney(500000 - company.companyValue, true);
        while (company.employees.Count < 8) company.HireEmployee(Person(company.employees.Count + 20));
        if (!system.IsDeskOpen) system.OpenDesk();

        if (!system.Force(situation, true, out string why))
        {
            skipped++;
            Log("Skip " + situation + "." + choice + ": " + why);
            return;
        }
        ManagerPending pending = null;
        foreach (ManagerPending p in system.Pending) if (p.situation == situation) pending = p;
        if (pending == null) { failures.Add(situation + " didn't reach the desk."); return; }
        ManagerDecisionView view = system.Describe(pending);
        if (view == null || string.IsNullOrEmpty(view.Title) || string.IsNullOrEmpty(view.Message)) failures.Add(situation + " has no title or message.");
        if (view != null && (view.Title.Contains("⟦") || view.Message.Contains("⟦"))) failures.Add(situation + ": missing text " + view.Title + " / " + view.Message);
        ManagerChoiceView shown = view != null ? view.Choices.Find(c => c.Id == choice) : null;
        if (shown == null || !shown.Available)
        {
            skipped++;
            Log("Skip " + situation + "." + choice + ": " + (shown == null ? "hidden" : shown.Reason));
            system.ExpireNow(pending.uid);
            return;
        }
        if (shown.Text.Contains("⟦") || shown.Hint.Contains("⟦")) failures.Add(situation + "." + choice + ": missing text");
        if (shown.Ask)
        {
            ManagerChooseResult asked = system.Choose(pending.uid, choice);
            if (!asked.Asked) failures.Add(situation + ": ask didn't reveal the analysis.");
            ManagerDecisionView after = system.Describe(pending);
            if (after == null || string.IsNullOrEmpty(after.Analysis)) failures.Add(situation + ": no analysis after asking.");
            system.ExpireNow(pending.uid);
            decided++;
            return;
        }
        ManagerChooseResult result = system.Choose(pending.uid, choice);
        if (!result.Done) failures.Add(situation + "." + choice + " didn't resolve: " + result.Message);
        else decided++;
        if (UIManager.Exists && !system.IsDeskOpen && UIManager.Instance.IsAnyPanelOpen()) UIManager.Instance.HandleEscape();
    }

    /// <summary>While days pass: answer what lands on the desk, hire when candidates wait, dismiss other pop-ups.</summary>
    static void KeepGoing(ManagerSystem system)
    {
        if (system.IsPickOpen || system.Phase == ManagerPhase.Picking)
        {
            system.PickCandidate(0);
            return;
        }
        if (system.IsDeskOpen)
        {
            ManagerPending p = system.Pending.Count > 0 ? system.Pending[0] : null;
            if (p == null) { CloseDesk(); return; }
            ManagerDecisionView view = system.Describe(p);
            ManagerChoiceView choice = view != null ? view.Choices.Find(c => c.Available && !c.Ask) : null;
            if (choice == null) system.ExpireNow(p.uid);
            else if (!system.Choose(p.uid, choice.Id).Done) failures.Add("Couldn't answer " + p.situation + " with " + choice.Id);
            else decided++;
            if (system.PendingCount == 0) CloseDesk();
            return;
        }
        if (UIManager.Exists && UIManager.Instance.IsAnyPanelOpen())
        {
            UIManager.Instance.HandleEscape();   // a tilt prompt or another pop-up
            return;
        }
        if (system.PendingCount >= 4) system.OpenDesk();
        Time.timeScale = 30f;
    }

    static void CloseDesk()
    {
        ManagerDeskView desk = UnityEngine.Object.FindAnyObjectByType<ManagerDeskView>();
        if (desk != null) desk.Close();
    }

    static string Describe(IReadOnlyList<ManagerPerson> people)
    {
        var sb = new StringBuilder();
        foreach (ManagerPerson p in people)
            sb.Append(p.name).Append(" (").Append(p.personality).Append(", ").Append(UIFormat.Money(p.salary)).Append(") ");
        return sb.ToString();
    }

    static void Finish()
    {
        EditorApplication.update -= Tick;
        SessionState.SetBool(RunningKey, false);
        SessionState.SetInt(StepKey, 0);
        AssetDatabase.AllowAutoRefresh();
        Application.logMessageReceived -= OnLog;
        Log("Decided " + decided + ", skipped " + skipped + ", days " + days + ", failures " + failures.Count + ".");
        foreach (string f in failures) Debug.LogWarning("[ManagerSmoke] FAIL " + f);
        Debug.Log("[ManagerSmoke] RESULT " + (failures.Count == 0 ? "PASS" : "FAIL"));
        EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
    }
}
