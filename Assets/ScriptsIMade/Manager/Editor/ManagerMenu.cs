using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > Game > Manager: check the situation files (JSON, effect and condition names, 2-4 choices, recommendations,
/// text keys in Manager.csv in every language) and debug the manager in Play mode (the Manager Debug window lists every
/// situation with its weight or why it can't come up, forces one, sets satisfaction, opens the desk...).
/// </summary>
public static class ManagerMenu
{
    const string Root = "Tools/Game/Manager/";
    public const string DataFolder = "Assets/Resources/Manager";
    public const string CsvPath = "Assets/Localization/Source/Manager.csv";
    public const string CodeFolder = "Assets/ScriptsIMade/Manager";

    [MenuItem(Root + "Validate Situations", false, 1)]
    public static void Validate()
    {
        List<ManagerProblem> problems = Run(out ManagerLibrary lib, out ManagerValidator.Texts texts);
        string report = ManagerValidator.Report(lib, problems, texts);
        int errors = problems.Count(p => p.IsError);
        if (errors > 0) Debug.LogError(report);
        else if (problems.Count > 0) Debug.LogWarning(report);
        else Debug.Log(report);
    }

    /// <summary>Batch mode: -executeMethod ManagerMenu.ValidateBatch (exit code 1 on errors).</summary>
    public static void ValidateBatch()
    {
        List<ManagerProblem> problems = Run(out ManagerLibrary lib, out ManagerValidator.Texts texts);
        Debug.Log(ManagerValidator.Report(lib, problems, texts));
        EditorApplication.Exit(problems.Any(p => p.IsError) ? 1 : 0);
    }

    public static List<ManagerProblem> Run(out ManagerLibrary lib, out ManagerValidator.Texts texts)
    {
        string engine = File.Exists(DataFolder + "/Engine.json") ? File.ReadAllText(DataFolder + "/Engine.json", Encoding.UTF8) : null;
        var files = new List<KeyValuePair<string, string>>();
        string situations = DataFolder + "/Situations";
        if (Directory.Exists(situations))
            foreach (string path in Directory.GetFiles(situations, "*.json").OrderBy(p => p, System.StringComparer.Ordinal))
                files.Add(new KeyValuePair<string, string>(Path.GetFileName(path), File.ReadAllText(path, Encoding.UTF8)));
        lib = ManagerLibrary.Load(engine, files);

        var problems = new List<ManagerProblem>();
        texts = ManagerValidator.ReadCsv(File.Exists(CsvPath) ? File.ReadAllText(CsvPath, Encoding.UTF8) : null, problems);
        var code = new List<string>();
        if (Directory.Exists(CodeFolder))
            foreach (string path in Directory.GetFiles(CodeFolder, "*.cs", SearchOption.AllDirectories))
                code.Add(File.ReadAllText(path, Encoding.UTF8));
        List<string> codeKeys = ManagerValidator.KeysUsedInCode(code);
        problems.AddRange(ManagerValidator.Validate(lib, texts, new List<string>(ManagerEffects.Names), codeKeys));
        return problems;
    }

    // ------------------------------------------------------------------
    // Play mode shortcuts
    // ------------------------------------------------------------------

    [MenuItem(Root + "Debug Window", false, 20)]
    static void OpenWindow() => ManagerDebugWindow.Open();

    [MenuItem(Root + "Open Desk", false, 40)]
    static void OpenDesk()
    {
        if (Live() is ManagerSystem s) s.OpenDesk();
    }

    [MenuItem(Root + "Hire A Manager Now (candidate pick)", false, 41)]
    static void PickNow()
    {
        if (!(Live() is ManagerSystem s)) return;
        if (s.Phase != ManagerPhase.Picking) s.StartPicking("debug");
        s.OpenPick();
    }

    [MenuItem(Root + "List Eligible Situations", false, 42)]
    static void ListEligible()
    {
        if (!(Live() is ManagerSystem s)) return;
        var sb = new StringBuilder("[Manager] Situations now:");
        foreach (ManagerSituationDef def in s.Library.Situations)
        {
            bool ok = s.Eligible(def, null, out List<ManagerTargetState> targets, out string why);
            sb.Append('\n').Append(ok ? "  OK   " : "  --   ").Append(def.Id.PadRight(24))
              .Append(ok ? "weight " + s.Weight(def, targets).ToString("0.##") : why)
              .Append(def.FollowupOnly ? " (follow-up only)" : "");
        }
        Debug.Log(sb.ToString());
    }

    [MenuItem(Root + "Satisfaction/10 (fed up)", false, 60)] static void Sat10() => SetSatisfaction(10f);
    [MenuItem(Root + "Satisfaction/30 (unhappy)", false, 61)] static void Sat30() => SetSatisfaction(30f);
    [MenuItem(Root + "Satisfaction/50", false, 62)] static void Sat50() => SetSatisfaction(50f);
    [MenuItem(Root + "Satisfaction/90 (content)", false, 63)] static void Sat90() => SetSatisfaction(90f);

    public static void SetSatisfaction(float value)
    {
        if (Live() is ManagerSystem s) s.SetSatisfaction(value);
    }

    [MenuItem(Root + "Force Resignation", false, 80)]
    static void Resign()
    {
        if (Live() is ManagerSystem s) s.Resign("debug");
    }

    [MenuItem(Root + "Reload Situation Files", false, 81)]
    static void Reload()
    {
        if (Live() is ManagerSystem s) s.ReloadLibrary();
    }

    static ManagerSystem Live()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[Manager] Enter Play mode first.");
            return null;
        }
        ManagerSystem s = ManagerSystem.Instance;
        if (s == null || !s.IsStateKnown) Debug.LogWarning("[Manager] No manager system in this scene yet (or the game is still loading).");
        return s != null && s.IsStateKnown ? s : null;
    }
}
