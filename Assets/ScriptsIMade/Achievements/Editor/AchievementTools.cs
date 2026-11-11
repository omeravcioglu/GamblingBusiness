using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > Game > Achievements:
/// - List: every achievement with its state and progress for the current profile (Play mode: the running player; Edit
///   mode: the last profile's file), and Steam's state.
/// - Unlock...: unlock (or lock again) one achievement. In Play mode with Steam this changes the real Steam account.
/// - Simulate Event...: report any counter with an amount, like the game would.
/// - Reset All: forget all progress of the profile, and on Steam too (ResetAllStats) when Steam runs in Play mode.
/// - Validate: definitions, Achievements.csv, the icon files (256x256 JPGs) and STEAM_ACHIEVEMENTS.md agree.
/// - Fill Steamworks Localization...: see SteamLocVdf.
/// - Open Progress Folder: persistentDataPath/Achievements.
/// </summary>
public static class AchievementTools
{
    const string Menu = "Tools/Game/Achievements/";
    public const string CsvPath = "Assets/Localization/Source/Achievements.csv";
    static readonly string[] CsvColumns = { "Key", "Context", "en", "zh-Hans", "ja", "ko", "tr" };

    public static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;
    public static string IconFolder => Path.Combine(ProjectRoot, "SteamStore", "achievements");
    public static string DocPath => Path.Combine(ProjectRoot, "STEAM_ACHIEVEMENTS.md");

    static AchievementStore CurrentStore()
    {
        if (!Application.isPlaying) AchievementSystem.ReloadEditStore();
        return AchievementSystem.ActiveStore;
    }

    [MenuItem(Menu + "List", false, 1)]
    static void List()
    {
        if (Application.isPlaying && AchievementSystem.Instance == null)
        {
            Debug.Log("[Achievements] The system isn't running (batch mode?).");
            return;
        }
        Debug.Log(AchievementSystem.DescribeAll(CurrentStore()) + (Application.isPlaying ? "" :
                  "\n  (Edit mode: the last profile's file. Steam's own state shows in Play mode.)"));
    }

    [MenuItem(Menu + "Unlock...", false, 2)]
    static void OpenUnlock() => AchievementUnlockWindow.Open();

    [MenuItem(Menu + "Simulate Event...", false, 3)]
    static void OpenSimulate() => AchievementSimulateWindow.Open();

    [MenuItem(Menu + "Reset All", false, 20)]
    static void ResetAll()
    {
        AchievementStore store = CurrentStore();
        string profile = store != null ? store.ProfileId : AchievementStore.LocalProfile;
        bool steam = Application.isPlaying && SteamAchievements.IsReady && profile != AchievementStore.LocalProfile;
        string what = steam
            ? "This resets ALL achievements and stats of Steam user " + profile + " on Steam (ResetAllStats) and the local progress file.\n\nThis can't be undone."
            : "This deletes the local achievement progress of profile '" + profile + "'." +
              (Application.isPlaying ? "\n\nSteam isn't running, so Steam's copy is not touched." :
                                       "\n\nSteam's copy is only reset in Play mode with Steam running.");
        if (!EditorUtility.DisplayDialog("Reset all achievements?", what, "Reset", "Cancel")) return;

        if (Application.isPlaying && AchievementSystem.Instance != null)
        {
            Debug.Log("[Achievements] " + AchievementSystem.Instance.DebugResetAll());
            return;
        }
        store?.Clear();
        AchievementStore.DeleteFile(profile);
        AchievementSystem.ReloadEditStore();
        Debug.Log("[Achievements] Profile '" + profile + "' reset (local file deleted).");
    }

    [MenuItem(Menu + "Validate", false, 40)]
    static void ValidateMenu()
    {
        List<string> problems = Validate(out string summary);
        if (problems.Count == 0) Debug.Log("[Achievements] Validate: no problems. " + summary);
        else Debug.LogWarning("[Achievements] Validate: " + problems.Count + " problem(s). " + summary + "\n  " + string.Join("\n  ", problems));
    }

    /// <summary>
    /// Batch mode (CI): import the changed CSVs (Achievements.csv becomes its String Table), run Validate and exit with 0
    /// (no problems) or 1. Unity.exe -batchmode -projectPath &lt;project&gt; -executeMethod AchievementTools.BatchValidate -logFile &lt;log&gt;
    /// </summary>
    public static void BatchValidate()
    {
        int code = 1;
        try
        {
            LocalizationCsvImporter.ImportChanged("AchievementTools.BatchValidate");
            List<string> problems = Validate(out string summary);
            Debug.Log("[Achievements] BatchValidate: " + summary + " " + problems.Count + " problem(s)." +
                      (problems.Count > 0 ? "\n  " + string.Join("\n  ", problems) : ""));
            string table = L10n.Get(AchievementDefinitions.Table, "ach.first_hire.name");
            Debug.Log("[Achievements] BatchValidate: the Achievements table answers ach.first_hire.name = \"" + table + "\"");
            code = problems.Count == 0 ? 0 : 1;
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
        if (Application.isBatchMode) EditorApplication.Exit(code);
    }

    [MenuItem(Menu + "Open Progress Folder", false, 60)]
    static void OpenFolder()
    {
        Directory.CreateDirectory(AchievementStore.Folder);
        EditorUtility.RevealInFinder(AchievementStore.Folder);
    }

    [MenuItem(Menu + "Fill Steamworks Localization...", false, 41)]
    static void FillLocalization()
    {
        string input = EditorUtility.OpenFilePanel("Localization file exported from Steamworks (achievements)", ProjectRoot, "vdf");
        if (string.IsNullOrEmpty(input)) return;
        string text;
        bool bom;
        try
        {
            byte[] bytes = File.ReadAllBytes(input);
            bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            text = Encoding.UTF8.GetString(bytes);
        }
        catch (Exception e)
        {
            EditorUtility.DisplayDialog("Fill Steamworks Localization", "Couldn't read the file:\n" + e.Message, "OK");
            return;
        }

        List<SteamLocVdf.Entry> entries = LoadEntries(out string csvError);
        if (csvError != null)
        {
            EditorUtility.DisplayDialog("Fill Steamworks Localization", csvError, "OK");
            return;
        }

        SteamLocVdf.Result result = SteamLocVdf.Fill(text, entries);
        if (result.Error != null)
        {
            EditorUtility.DisplayDialog("Fill Steamworks Localization", result.Error, "OK");
            return;
        }

        string folder = Path.GetDirectoryName(input);
        string name = Path.GetFileNameWithoutExtension(input) + "_filled.vdf";
        string output = EditorUtility.SaveFilePanel("Save the filled localization file", folder, name, "vdf");
        if (string.IsNullOrEmpty(output)) return;
        var encoding = new UTF8Encoding(bom);
        File.WriteAllText(output, result.Output, encoding);
        foreach (KeyValuePair<string, string> single in result.PerLanguage)
        {
            string path = Path.Combine(Path.GetDirectoryName(output), Path.GetFileNameWithoutExtension(output) + "_" + single.Key + ".vdf");
            File.WriteAllText(path, single.Value, encoding);
        }

        var log = new StringBuilder();
        log.Append("[Achievements] Steamworks localization: ").Append(result.Matched).Append(" of ").Append(result.TokenPairs)
           .Append(" achievements in the file matched, ").Append(entries.Count).Append(" defined in the game. Written: ").Append(output);
        if (result.PerLanguage.Count > 0) log.Append(" (plus one file per language next to it)");
        if (result.UnmatchedTokens.Count > 0)
            log.Append("\n  Not matched (the English text differs from Achievements.csv? fix it in Steamworks or the CSV):\n    ")
               .Append(string.Join("\n    ", result.UnmatchedTokens));
        if (result.AchievementsNotInFile.Count > 0)
            log.Append("\n  Defined in the game but not in the file (not created in Steamworks yet?):\n    ")
               .Append(string.Join("\n    ", result.AchievementsNotInFile));
        log.Append("\n  Matches:\n    ").Append(string.Join("\n    ", result.Report));
        if (result.UnmatchedTokens.Count > 0 || result.AchievementsNotInFile.Count > 0) Debug.LogWarning(log.ToString());
        else Debug.Log(log.ToString());
        EditorUtility.DisplayDialog("Fill Steamworks Localization",
            result.Matched + " of " + result.TokenPairs + " achievements matched.\n\nImport the saved file on Steamworks' achievements page." +
            (result.UnmatchedTokens.Count + result.AchievementsNotInFile.Count > 0 ? "\n\nSome didn't match: see the Console." : ""), "OK");
    }

    /// <summary>Every achievement's English and translated texts from Achievements.csv.</summary>
    public static List<SteamLocVdf.Entry> LoadEntries(out string error)
    {
        error = null;
        var entries = new List<SteamLocVdf.Entry>();
        string path = Path.Combine(ProjectRoot, CsvPath);
        if (!File.Exists(path))
        {
            error = CsvPath + " is missing.";
            return entries;
        }
        L10nCsv.Sheet sheet = L10nCsv.ReadSheet(File.ReadAllText(path, Encoding.UTF8));
        int key = sheet.Column("Key"), en = sheet.Column("en");
        if (key < 0 || en < 0)
        {
            error = CsvPath + " has no Key or en column.";
            return entries;
        }
        var rows = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (List<string> row in sheet.Rows)
        {
            string k = L10nCsv.Sheet.Cell(row, key).Trim();
            if (k.Length > 0 && !k.StartsWith("#", StringComparison.Ordinal)) rows[k] = row;
        }
        foreach (AchievementDef def in AchievementDefinitions.All)
        {
            rows.TryGetValue(def.NameKey, out List<string> nameRow);
            rows.TryGetValue(def.DescKey, out List<string> descRow);
            var entry = new SteamLocVdf.Entry
            {
                ApiName = def.ApiName,
                EnglishName = L10nCsv.Sheet.Cell(nameRow, en),
                EnglishDescription = L10nCsv.Sheet.Cell(descRow, en)
            };
            foreach (KeyValuePair<string, string> language in SteamLocVdf.Languages)
            {
                int column = sheet.Column(language.Value);
                entry.Names[language.Value] = L10nCsv.Sheet.Cell(nameRow, column);
                entry.Descriptions[language.Value] = L10nCsv.Sheet.Cell(descRow, column);
            }
            entries.Add(entry);
        }
        return entries;
    }

    /// <summary>Everything the achievements need, checked. Returns the problems (empty = fine).</summary>
    public static List<string> Validate(out string summary)
    {
        var problems = new List<string>(AchievementDefinitions.Problems());
        int hidden = AchievementDefinitions.All.Count(d => d.Hidden);
        int stats = AchievementDefinitions.Counters.Count(c => c.SteamStat != null);
        summary = AchievementDefinitions.All.Count + " achievements (" + hidden + " hidden), " + stats + " Steam stats.";

        // Achievements.csv: 7 columns, no empty cell, no duplicate key, placeholders only from the English text
        string csv = Path.Combine(ProjectRoot, CsvPath);
        if (!File.Exists(csv))
        {
            problems.Add(CsvPath + " is missing.");
        }
        else
        {
            string text = File.ReadAllText(csv, Encoding.UTF8);
            L10nCsv.Sheet sheet = L10nCsv.ReadSheet(text);
            if (!sheet.Header.Select(h => h.Trim()).SequenceEqual(CsvColumns))
                problems.Add(CsvPath + ": the header must be " + string.Join(",", CsvColumns) + ".");
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var placeholder = new Regex(@"\{\s*([^{}:,\s]+)");
            foreach (List<string> row in sheet.Rows)
            {
                string k = L10nCsv.Sheet.Cell(row, 0).Trim();
                if (k.Length == 0 || k.StartsWith("#", StringComparison.Ordinal)) continue;
                if (row.Count != CsvColumns.Length) problems.Add(CsvPath + ": " + k + " has " + row.Count + " cells.");
                if (!keys.Add(k)) problems.Add(CsvPath + ": " + k + " is listed twice.");
                for (int i = 1; i < CsvColumns.Length; i++)
                    if (string.IsNullOrWhiteSpace(L10nCsv.Sheet.Cell(row, i))) problems.Add(CsvPath + ": " + k + " has no " + CsvColumns[i] + ".");
                var english = new HashSet<string>(placeholder.Matches(L10nCsv.Sheet.Cell(row, 2)).Cast<Match>().Select(m => m.Groups[1].Value));
                for (int i = 3; i < CsvColumns.Length; i++)
                    foreach (Match m in placeholder.Matches(L10nCsv.Sheet.Cell(row, i)))
                        if (!english.Contains(m.Groups[1].Value)) problems.Add(CsvPath + ": " + k + " (" + CsvColumns[i] + ") uses {" + m.Groups[1].Value + "}, which the English text doesn't have.");
            }
            foreach (AchievementDef def in AchievementDefinitions.All)
            {
                if (!keys.Contains(def.NameKey)) problems.Add(CsvPath + ": no " + def.NameKey + ".");
                if (!keys.Contains(def.DescKey)) problems.Add(CsvPath + ": no " + def.DescKey + ".");
            }
        }

        // Icons: <API>.jpg and <API>_locked.jpg, 256 x 256
        foreach (AchievementDef def in AchievementDefinitions.All)
        {
            foreach (string file in new[] { def.IconFile, def.LockedIconFile })
            {
                string path = Path.Combine(IconFolder, file);
                if (!File.Exists(path))
                {
                    problems.Add("Icon missing: SteamStore/achievements/" + file);
                    continue;
                }
                var texture = new Texture2D(2, 2);
                try
                {
                    if (!texture.LoadImage(File.ReadAllBytes(path))) problems.Add("Icon unreadable: " + file);
                    else if (texture.width != 256 || texture.height != 256) problems.Add("Icon " + file + " is " + texture.width + "x" + texture.height + " (Steam wants 256x256).");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(texture);
                }
            }
        }

        // The table the owner types into Steamworks
        if (!File.Exists(DocPath)) problems.Add("STEAM_ACHIEVEMENTS.md is missing.");
        else
        {
            string doc = File.ReadAllText(DocPath, Encoding.UTF8);
            foreach (AchievementDef def in AchievementDefinitions.All)
                if (!doc.Contains(def.ApiName)) problems.Add("STEAM_ACHIEVEMENTS.md doesn't list " + def.ApiName + ".");
        }
        return problems;
    }
}

/// <summary>Tools > Game > Achievements > Unlock...: unlock or lock one achievement.</summary>
public sealed class AchievementUnlockWindow : EditorWindow
{
    string filter = "";
    Vector2 scroll;

    public static void Open()
    {
        var window = GetWindow<AchievementUnlockWindow>(true, "Unlock achievement");
        window.minSize = new Vector2(560f, 420f);
    }

    void OnGUI()
    {
        AchievementStore store = AchievementSystem.ActiveStore;
        EditorGUILayout.HelpBox(Application.isPlaying
            ? (SteamAchievements.IsReady ? "Play mode with Steam: this changes the real Steam account " + (store != null ? store.ProfileId : "") + "."
                                         : "Play mode without Steam: the local profile '" + (store != null ? store.ProfileId : "") + "' only; Steam gets it later.")
            : "Edit mode: the last profile's file ('" + (store != null ? store.ProfileId : "-") + "'); Steam gets unlocks the next time the game runs with Steam.",
            MessageType.Info);
        filter = EditorGUILayout.TextField("Filter", filter);
        scroll = EditorGUILayout.BeginScrollView(scroll);
        foreach (AchievementDef def in AchievementDefinitions.All)
        {
            string name = AchievementText.Name(def);
            if (filter.Length > 0 && def.ApiName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
            bool unlocked = store != null && store.IsUnlocked(def.ApiName);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField((unlocked ? "✓ " : "   ") + def.ApiName, GUILayout.Width(250f));
            EditorGUILayout.LabelField(name + (def.Hidden ? " (hidden)" : ""), GUILayout.MinWidth(120f));
            using (new EditorGUI.DisabledScope(unlocked))
            {
                if (GUILayout.Button("Unlock", GUILayout.Width(70f))) Achievements.Unlock(def.ApiName);
            }
            using (new EditorGUI.DisabledScope(!unlocked))
            {
                if (GUILayout.Button("Lock", GUILayout.Width(55f))) Lock(def, store);
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();
    }

    static void Lock(AchievementDef def, AchievementStore store)
    {
        if (Application.isPlaying && AchievementSystem.Instance != null)
        {
            AchievementSystem.Instance.DebugLock(def.ApiName);
            Debug.Log("[Achievements] " + def.ApiName + " locked again" + (SteamAchievements.IsReady ? " (and on Steam)." : " (locally)."));
            return;
        }
        if (store != null && store.RemoveUnlock(def.ApiName))
        {
            store.Save();
            Debug.Log("[Achievements] (Edit mode) " + def.ApiName + " locked again in profile '" + store.ProfileId + "' (Steam's copy only changes in Play mode).");
        }
    }

    void OnInspectorUpdate() => Repaint();
}

/// <summary>Tools > Game > Achievements > Simulate Event...: report a counter like the game would.</summary>
public sealed class AchievementSimulateWindow : EditorWindow
{
    int selected;
    long amount = 1;

    public static void Open()
    {
        var window = GetWindow<AchievementSimulateWindow>(true, "Simulate achievement event");
        window.minSize = new Vector2(520f, 320f);
    }

    void OnGUI()
    {
        IReadOnlyList<AchievementCounter> counters = AchievementDefinitions.Counters;
        string[] labels = counters.Select(c => c.Id + "  (" + c.Mode + ")").ToArray();
        selected = Mathf.Clamp(EditorGUILayout.Popup("Event", selected, labels), 0, counters.Count - 1);
        AchievementCounter counter = counters[selected];
        EditorGUILayout.LabelField("Fed by", counter.Source, EditorStyles.wordWrappedLabel);
        if (counter.SteamStat != null) EditorGUILayout.LabelField("Steam stat", counter.SteamStat);
        amount = EditorGUILayout.LongField(counter.Mode == AchievementCounterMode.Sum ? "Add" : "Report value", amount);

        AchievementStore store = AchievementSystem.ActiveStore;
        long now = store != null ? store.GetCount(counter.Id) : 0;
        EditorGUILayout.LabelField("Current value", now.ToString("N0") + "  (profile '" + (store != null ? store.ProfileId : "-") + "')");
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Unlocks", EditorStyles.boldLabel);
        foreach (AchievementDef def in AchievementDefinitions.ForCounter(counter.Id))
        {
            bool unlocked = store != null && store.IsUnlocked(def.ApiName);
            EditorGUILayout.LabelField((unlocked ? "✓ " : "   ") + def.ApiName, "at " + def.Target.ToString("N0") + "  " + AchievementText.Name(def));
        }
        EditorGUILayout.Space();
        if (GUILayout.Button("Report", GUILayout.Height(28f)))
        {
            Achievements.Report(counter.Id, amount);
            if (!Application.isPlaying) AchievementSystem.ReloadEditStore();
        }
        EditorGUILayout.HelpBox(Application.isPlaying
            ? "Play mode: goes through the running system (and to Steam when it runs)."
            : "Edit mode: changes the last profile's file only.", MessageType.None);
    }

    void OnInspectorUpdate() => Repaint();
}
