using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Edit-mode pictures of the manager's screens (the chip on the office HUD, the desk, the candidate pick), to check them
/// against the design canvas's "Manager" page without Play mode or the save:
/// Unity.exe -batchmode -projectPath . -executeMethod ManagerPreview.Run -logFile manager_preview.log [-previewDir folder]
/// Each picture: a new empty scene with the office HUD prefab, a CompanyManager stand-in (value and day only, never
/// awakened) and a ManagerSystem whose Awake runs by reflection (it loads the situation files), restored from a sample
/// state; the view is built through its own private build methods and drawn by UIPreview.
/// </summary>
public static class ManagerPreview
{
    const string HudPrefab = "Assets/UI/Prefabs/OfficeHUD.prefab";
    const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    [MenuItem("Tools/Game/Manager/Render Previews (batch only)")]
    static void MenuRun() => Debug.Log("[ManagerPreview] Batch mode only: -executeMethod ManagerPreview.Run");

    public static void Run()
    {
        var log = new List<string>();
        int exit = 0;
        try
        {
            string dir = Arg("-previewDir") ?? Path.Combine(Application.dataPath, "..", "Temp", "ManagerPreview");
            UIPreview.ReadScreenSizeArg();   // -previewSize 3440x1440: as on that screen
            LocalizationCsvImporter.ImportChanged("manager preview");
            // The situation files and Manager.csv, as Tools > Game > Manager > Validate Situations checks them
            List<ManagerProblem> problems = ManagerMenu.Run(out ManagerLibrary library, out ManagerValidator.Texts texts);
            int errors = 0;
            foreach (ManagerProblem p in problems) if (p.IsError) errors++;
            log.Add("Validation: " + problems.Count + " notes, " + errors + " errors.");
            if (errors > 0)
            {
                Debug.LogError("[ManagerPreview] " + ManagerValidator.Report(library, problems, texts));
                exit = 1;
            }
            Render(Path.Combine(dir, "manager_chip.png"), "chip", log);
            Render(Path.Combine(dir, "manager_desk.png"), "desk", log);
            Render(Path.Combine(dir, "manager_take.png"), "take", log);
            Render(Path.Combine(dir, "manager_urgent.png"), "urgent", log);
            Render(Path.Combine(dir, "manager_pick.png"), "pick", log);
            string langs = Arg("-langs");   // e.g. "tr,ja": the pick and the urgent desk again in those languages
            if (!string.IsNullOrEmpty(langs)) RenderLanguages(dir, langs.Split(','), log);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            exit = 1;
        }
        finally
        {
            CompanyManager.Instance = null;
        }
        foreach (string line in log) Debug.Log("[ManagerPreview] " + line);
        if (Application.isBatchMode) EditorApplication.Exit(exit);
    }

    /// <summary>
    /// The long-text check: the pick and the urgent desk in other languages. Switching the language goes through the
    /// Localization package's selected locale, which the PlayerPrefLocaleSelector may remember: the player's saved choice is
    /// put back exactly as it was (or removed again) afterwards.
    /// </summary>
    static void RenderLanguages(string dir, string[] codes, List<string> log)
    {
        string prefKey = "selected-locale";
        foreach (var selector in UnityEngine.Localization.Settings.LocalizationSettings.StartupLocaleSelectors)
            if (selector is UnityEngine.Localization.Settings.PlayerPrefLocaleSelector prefs) prefKey = prefs.PlayerPreferenceKey;
        bool hadPref = PlayerPrefs.HasKey(prefKey);
        string savedPref = hadPref ? PlayerPrefs.GetString(prefKey) : null;
        string before = L10n.CurrentCode;
        try
        {
            foreach (string raw in codes)
            {
                string code = raw.Trim();
                if (code.Length == 0 || !L10n.SetLanguage(code)) continue;
                Render(Path.Combine(dir, "manager_pick_" + code + ".png"), "pick", log);
                Render(Path.Combine(dir, "manager_urgent_" + code + ".png"), "urgent", log);
            }
        }
        finally
        {
            L10n.SetLanguage(before);
            if (hadPref) PlayerPrefs.SetString(prefKey, savedPref);
            else PlayerPrefs.DeleteKey(prefKey);
            PlayerPrefs.Save();
            log.Add("Language back to " + before + "; saved language preference " + (hadPref ? "restored" : "left unset") + ".");
        }
    }

    static string Arg(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == name) return args[i + 1];
        return null;
    }

    static void Render(string png, string variant, List<string> log)
    {
        UIPreview.RenderWith(png, log, cam =>
        {
            var made = new List<GameObject>();
            var hudPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPrefab);
            if (hudPrefab != null)
            {
                var hud = (GameObject)PrefabUtility.InstantiatePrefab(hudPrefab);
                hud.SetActive(true);
                Canvas hudCanvas = hud.GetComponent<Canvas>();
                if (hudCanvas != null) ToCamera(hudCanvas, cam, 20f);
                made.Add(hud);
            }
            else log.Add("No office HUD prefab at " + HudPrefab + " (drawn on the plain backdrop).");

            var companyGo = new GameObject("Company (Preview)");
            var company = companyGo.AddComponent<CompanyManager>();
            company.companyValue = 3284100;
            company.currentDay = 42;
            CompanyManager.Instance = company;
            made.Add(companyGo);

            var go = new GameObject("Manager (Preview)");
            var system = go.AddComponent<ManagerSystem>();
            Call(system, "Awake");
            system.RestoreState(JsonUtility.ToJson(Sample(variant)));
            Call(system, "OnStateLoaded");
            made.Add(go);

            Canvas canvas;
            switch (variant)
            {
                case "chip":
                    var chip = new GameObject("ManagerChip").AddComponent<ManagerChipView>();
                    chip.transform.SetParent(go.transform, false);
                    Call(chip, "EnsureBuilt");
                    Get<RectTransform>(chip, "root").gameObject.SetActive(true);
                    Call(chip, "Refresh", system);
                    canvas = Get<Canvas>(chip, "canvas");
                    break;
                case "pick":
                    var pick = new GameObject("ManagerPick").AddComponent<ManagerPickView>();
                    pick.transform.SetParent(go.transform, false);
                    Call(pick, "EnsureBuilt");
                    Get<RectTransform>(pick, "screen").gameObject.SetActive(true);
                    Call(pick, "Rebuild");
                    canvas = Get<Canvas>(pick, "canvas");
                    break;
                default:
                    var desk = new GameObject("ManagerDesk").AddComponent<ManagerDeskView>();
                    desk.transform.SetParent(go.transform, false);
                    Call(desk, "EnsureBuilt");
                    int uid = variant == "urgent" ? 4 : variant == "take" ? 2 : 1;
                    Set(desk, "selectedUid", uid);
                    Set(desk, "interruptUid", variant == "urgent" ? uid : 0);
                    Get<RectTransform>(desk, "screen").gameObject.SetActive(true);
                    Call(desk, "Rebuild");
                    canvas = Get<Canvas>(desk, "canvas");
                    log.Add(variant + ": text scale " + desk.LastTextScale + (desk.LastFit ? ", fits" : ", DOESN'T FIT"));
                    break;
            }
            if (canvas != null)
            {
                ToCamera(canvas, cam, 10f);
                if (canvas.transform.parent == null || canvas.transform.root != go.transform) made.Add(canvas.transform.root.gameObject);
            }
            return made;
        });
    }

    static void ToCamera(Canvas canvas, Camera cam, float distance)
    {
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = cam;
        canvas.planeDistance = distance;
    }

    // ------------------------------------------------------------------
    // The sample company: Priya Shah (people-first) with three or four matters on her desk, or three candidates
    // ------------------------------------------------------------------

    static ManagerSaveState Sample(string variant)
    {
        var s = new ManagerSaveState();
        if (variant == "pick")
        {
            s.phase = ManagerPhase.Picking;
            s.candidates.Add(Person("Helen Park", "Female", 2310, 52, 24, ManagerSchema.Conservative, 84000, 3));
            s.candidates.Add(Person("Priya Shah", "Female", 1204, 41, 14, ManagerSchema.People, 91000, 1));
            s.candidates.Add(Person("Marco Rossi", "Male", 4120, 36, 11, ManagerSchema.Aggressive, 109000, 1));
            return s;
        }
        s.phase = ManagerPhase.Active;
        s.manager = Person("Priya Shah", "Female", 1204, 41, 14, ManagerSchema.People, 91000, 1);
        s.manager.satisfaction = variant == "urgent" ? 41f : 74f;
        s.manager.hiredDay = 5;
        int today = 42;
        if (variant == "urgent")
            s.pending.Add(Pending(4, "mgr_offer", today + 2, true, false, P("offer", 112000, ManagerParams.FormatMoney), P("perk", 15000, ManagerParams.FormatMoney)));
        s.pending.Add(Pending(1, "nearly_full", today + 9, false, true, P("left", 2), P("seats", 5), P("rate", 70, ManagerParams.FormatMoney),
                              P("rent_day", 350, ManagerParams.FormatMoney)));
        ManagerPending investor = Pending(2, "investor_share", today + 12, false, variant == "take", P("amount", 68000, ManagerParams.FormatMoney),
                                          P("better", 85000, ManagerParams.FormatMoney), P("share_pct", 15), P("share_days", 40), P("cost_est", 74000, ManagerParams.FormatMoney));
        investor.revealed = variant == "take";
        s.pending.Add(investor);
        ManagerPending raise = Pending(3, "raise_request", today + 6, false, true, P("emp_profit", 1800, ManagerParams.FormatMoney), P("big_day", 110, ManagerParams.FormatMoney),
                                       P("small_day", 40, ManagerParams.FormatMoney));
        raise.targets.Add(new ManagerTargetState { name = "emp", kind = "employee", employeeId = "preview-dana", displayName = "Dana Brooks" });
        s.pending.Add(raise);
        s.nextUid = 5;
        s.history.Add(new ManagerHistory { situation = "stock_tip", choice = "", day = 33, how = "unanswered", title = "A hot stock tip" });
        s.history.Add(new ManagerHistory { situation = "tax_consultants", choice = "hire", day = 37, how = "delegated", title = "Tax season",
                                           choiceText = "Hire the consultants ($3,000)", manager = "Priya Shah" });
        s.history.Add(new ManagerHistory { situation = "morale_slump", choice = "party", day = 40, how = "player", title = "Morale is sinking",
                                           choiceText = "Throw a team party ($4,000)" });
        return s;
    }

    static ManagerPerson Person(string name, string gender, int portrait, int age, int years, string personality, double salary, int bio)
    {
        return new ManagerPerson
        {
            name = name, gender = gender, portraitIndex = portrait, age = age, years = years, personality = personality,
            salary = salary, expectedBase = salary, satisfaction = 70f, competence = 1f, bio = bio
        };
    }

    static ManagerPending Pending(int uid, string situation, int expires, bool urgent, bool seen, params ManagerParamState[] prms)
    {
        var p = new ManagerPending { uid = uid, situation = situation, createdDay = 40, expiresDay = expires, urgent = urgent, interrupt = urgent, seen = seen, personality = ManagerSchema.People };
        p.prms.AddRange(prms);
        return p;
    }

    static ManagerParamState P(string name, double value, string format = ManagerParams.FormatInt)
        => new ManagerParamState { name = name, value = value, format = format };

    // ------------------------------------------------------------------
    // Reflection: the views build at runtime through private methods
    // ------------------------------------------------------------------

    static void Call(object target, string method, params object[] args)
    {
        MethodInfo m = target.GetType().GetMethod(method, Any);
        if (m == null) throw new MissingMethodException(target.GetType().Name, method);
        m.Invoke(target, args);
    }

    static T Get<T>(object target, string field) where T : class
    {
        FieldInfo f = target.GetType().GetField(field, Any);
        if (f == null) throw new MissingFieldException(target.GetType().Name, field);
        return f.GetValue(target) as T;
    }

    static void Set(object target, string field, object value)
    {
        FieldInfo f = target.GetType().GetField(field, Any);
        if (f == null) throw new MissingFieldException(target.GetType().Name, field);
        f.SetValue(target, value);
    }
}
