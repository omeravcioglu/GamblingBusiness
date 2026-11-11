using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Metadata;
using UnityEngine.Localization.Tables;

/// <summary>
/// Syncs Assets/Localization/Source/&lt;Name&gt;.csv into the String Table collection &lt;Name&gt; (Assets/Localization/Tables/&lt;Name&gt;/).
/// The CSVs are the source of truth: edit them (Excel, Google Sheets, any text editor), not the tables.
/// - Columns: Key, Context, en, zh-Hans, ja, ko, tr (UTF-8, RFC 4180 quoting, cells may span lines).
/// - Creates missing collections, tables and keys, updates changed values, marks entries holding {placeholders} as Smart
///   Strings and stores Context as the key's Comment metadata. An empty cell removes that language's value: the entry
///   is untranslated and shows the English text. Keys that are no longer in the CSV are kept and listed, never deleted.
/// - Runs after a script reload and after a CSV is re-imported when its contents changed (the hashes are kept in
///   Library/LocalizationCsvHashes.txt), from Tools > Game > Localization > Import CSVs (always), and at the end of Set Up.
/// - Tools > Game > Localization > Export Missing List writes the untranslated keys per language to
///   Assets/Localization/Reports/untranslated.txt.
/// </summary>
public static class LocalizationCsvImporter
{
    const string HashFile = "Library/LocalizationCsvHashes.txt";
    const string MenuRoot = "Tools/Game/Localization/";
    const int ListLimit = 30;

    static readonly Regex PlaceholderPattern = new Regex(@"\{[^{}]+\}");
    static readonly Regex PlaceholderName = new Regex(@"\{\s*([^{}:,\s]+)");
    static bool s_Scheduled;
    static int s_Waits;

    public static string ReportPath => LocalizationSetup.ReportsFolder + "/untranslated.txt";

    [MenuItem(MenuRoot + "Import CSVs", false, 2)]
    static void ImportMenu()
    {
        string summary = Import(true, "Tools > Game > Localization > Import CSVs");
        if (!string.IsNullOrEmpty(summary)) Debug.Log(summary);
    }

    [MenuItem(MenuRoot + "Import CSVs", true)]
    static bool ImportMenuValidate() => !EditorApplication.isPlayingOrWillChangePlaymode;

    [MenuItem(MenuRoot + "Export Missing List", false, 3)]
    static void ExportMenu()
    {
        Debug.Log(ExportMissingList());
    }

    /// <summary>Import the CSVs whose contents changed since their last import (or whose collection is missing); logs what it did.</summary>
    public static void ImportChanged(string reason)
    {
        try
        {
            string summary = Import(false, reason);
            if (!string.IsNullOrEmpty(summary)) Debug.Log(summary);
        }
        catch (Exception e)
        {
            Debug.LogError("[L10n CSV] Import failed: " + e);
        }
    }

    /// <summary>Import soon (a CSV was re-imported by Unity); skipped in Play mode, when the setup's return to Edit mode catches it.</summary>
    public static void ScheduleImport()
    {
        if (s_Scheduled) return;
        s_Scheduled = true;
        s_Waits = 0;
        EditorApplication.delayCall += RunScheduled;
    }

    static void RunScheduled()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            if (++s_Waits < 2000) EditorApplication.delayCall += RunScheduled;
            else s_Scheduled = false;   // the next reload's automatic run imports it
            return;
        }
        s_Scheduled = false;
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (LocalizationEditorSettings.ActiveLocalizationSettings == null) return;   // Set Up imports them
        ImportChanged("a CSV changed");
    }

    // ------------------------------------------------------------------
    // Import
    // ------------------------------------------------------------------

    /// <summary>
    /// Sync the CSVs into their String Table collections: all of them when <paramref name="force"/>, else only those that
    /// changed or have no collection yet. Returns the summary ("" when there was nothing to do).
    /// </summary>
    public static string Import(bool force, string reason)
    {
        string folder = LocalizationSetup.SourceFolder;
        if (!Directory.Exists(folder)) return force ? "[L10n CSV] There is no " + folder + " folder." : "";
        if (LocalizationEditorSettings.ActiveLocalizationSettings == null)
            return "[L10n CSV] Skipped: no active Localization Settings yet (Tools > Game > Localization > Set Up).";
        ReadOnlyCollection<Locale> locales = LocalizationEditorSettings.GetLocales();
        if (locales.Count == 0) return "[L10n CSV] Skipped: the project has no locales yet (Tools > Game > Localization > Set Up).";

        Dictionary<string, string> hashes = LoadHashes();
        var summary = new StringBuilder();
        bool imported = false;
        string[] files = Directory.GetFiles(folder, "*.csv", SearchOption.TopDirectoryOnly);
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        foreach (string file in files)
        {
            string path = file.Replace('\\', '/');
            string name = Path.GetFileNameWithoutExtension(path);
            byte[] bytes = File.ReadAllBytes(path);
            string hash = Hash(bytes);
            StringTableCollection collection = LocalizationSetup.FindStringCollection(name);
            if (!force && collection != null && hashes.TryGetValue(path, out string old) && old == hash) continue;

            imported = true;
            try
            {
                ImportFile(path, name, bytes, locales, collection, summary);
                hashes[path] = hash;
            }
            catch (Exception e)
            {
                summary.AppendLine("  FAIL " + name + ".csv: " + e.Message);
                Debug.LogException(e);
            }
        }

        if (!imported) return force ? "[L10n CSV] No CSV files in " + folder + "." : "";
        SaveHashes(hashes);
        AssetDatabase.SaveAssets();
        L10n.ClearCaches();   // a table that was missing may exist now
        return "[L10n CSV] Import (" + reason + ")\n" + summary.ToString().TrimEnd();
    }

    static void ImportFile(string path, string name, byte[] bytes, ReadOnlyCollection<Locale> locales, StringTableCollection collection, StringBuilder summary)
    {
        string text = new UTF8Encoding(false, false).GetString(bytes);
        if (text.IndexOf('\uFFFD') >= 0)
            summary.AppendLine("  WARNING " + name + ".csv has bytes that aren't UTF-8 (save it as \"CSV UTF-8\"): some characters are broken.");

        L10nCsv.Sheet sheet = L10nCsv.ReadSheet(text);
        int keyColumn = sheet.Column("Key");
        int contextColumn = sheet.Column("Context");
        if (keyColumn < 0) throw new Exception("the header has no Key column");

        // One column per project locale, matched by code
        var columns = new List<KeyValuePair<Locale, int>>();
        var missingColumns = new List<string>();
        foreach (Locale locale in locales)
        {
            int column = sheet.Column(locale.Identifier.Code);
            if (column >= 0) columns.Add(new KeyValuePair<Locale, int>(locale, column));
            else missingColumns.Add(locale.Identifier.Code);
        }
        int englishColumn = sheet.Column(L10n.English);
        if (englishColumn < 0) throw new Exception("the header has no en column (English is the source)");
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Key", "Context" };
        foreach (Locale locale in locales) known.Add(locale.Identifier.Code);
        List<string> ignored = sheet.Header.Where(h => !string.IsNullOrWhiteSpace(h) && !known.Contains(h.Trim())).ToList();

        // The collection and a table per locale
        string how = "updated";
        if (collection == null)
        {
            string error = ValidateCollectionName(name);
            if (error != null) throw new Exception(error);
            if (LocalizationSetup.FindAssetCollection(name) != null) throw new Exception("an Asset Table collection is already called " + name);
            string tableFolder = LocalizationSetup.TablesFolder + "/" + name;
            LocalizationSetup.EnsureFolder(tableFolder);
            collection = LocalizationEditorSettings.CreateStringTableCollection(name, tableFolder);
            if (collection == null) throw new Exception("the collection couldn't be created");
            how = "created";
        }
        var tables = new Dictionary<string, StringTable>();
        foreach (Locale locale in locales)
        {
            var table = collection.GetTable(locale.Identifier) as StringTable;
            if (table == null) table = collection.AddNewTable(locale.Identifier) as StringTable;
            if (table != null) tables[locale.Identifier.Code] = table;
        }

        SharedTableData shared = collection.SharedData;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var duplicates = new List<string>();
        var untranslated = new Dictionary<string, List<string>>();
        var brokenPlaceholders = new List<string>();
        var droppedPlaceholders = new List<string>();
        int rows = 0, keysAdded = 0, added = 0, updated = 0, cleared = 0, smart = 0, contexts = 0;

        foreach (List<string> row in sheet.Rows)
        {
            string key = L10nCsv.Sheet.Cell(row, keyColumn).Trim();
            if (key.Length == 0 || key.StartsWith("#", StringComparison.Ordinal)) continue;   // blank line or comment
            if (!seen.Add(key))
            {
                duplicates.Add(key);
                continue;
            }
            rows++;

            SharedTableData.SharedTableEntry entry = shared.GetEntry(key);
            if (entry == null)
            {
                entry = shared.AddKey(key);
                keysAdded++;
            }
            string context = contextColumn >= 0 ? L10nCsv.NormalizeNewlines(L10nCsv.Sheet.Cell(row, contextColumn)).Trim() : "";
            if (SetComment(entry, context)) contexts++;

            string english = L10nCsv.NormalizeNewlines(L10nCsv.Sheet.Cell(row, englishColumn));
            HashSet<string> englishNames = PlaceholderNames(english);

            foreach (KeyValuePair<Locale, int> column in columns)
            {
                string code = column.Key.Identifier.Code;
                if (!tables.TryGetValue(code, out StringTable table)) continue;
                string value = L10nCsv.NormalizeNewlines(L10nCsv.Sheet.Cell(row, column.Value));
                StringTableEntry tableEntry = table.GetEntry(entry.Id);

                if (value.Length == 0)
                {
                    // Untranslated: no value, so the en fallback shows
                    if (!untranslated.TryGetValue(code, out List<string> list)) untranslated[code] = list = new List<string>();
                    list.Add(key);
                    if (tableEntry != null)
                    {
                        table.RemoveEntry(entry.Id);
                        cleared++;
                    }
                    continue;
                }

                if (tableEntry == null)
                {
                    tableEntry = table.AddEntry(entry.Id, value);
                    added++;
                }
                else if (tableEntry.Value != value)
                {
                    tableEntry.Value = value;
                    updated++;
                }
                bool isSmart = PlaceholderPattern.IsMatch(value);
                if (tableEntry.IsSmart != isSmart) tableEntry.IsSmart = isSmart;
                if (isSmart) smart++;

                if (code != L10n.English && english.Length > 0)
                {
                    HashSet<string> names = PlaceholderNames(value);
                    foreach (string unknown in names.Where(n => !englishNames.Contains(n)))
                        brokenPlaceholders.Add(code + " " + key + ": {" + unknown + "}");
                    foreach (string unused in englishNames.Where(n => !names.Contains(n)))
                        droppedPlaceholders.Add(code + " " + key + ": {" + unused + "}");
                }
            }
        }

        // Keys the CSV no longer has stay in the tables
        List<string> kept = shared.Entries.Where(e => !seen.Contains(e.Key)).Select(e => e.Key).ToList();

        string preload = "on";
        try
        {
            if (!collection.IsPreloadTableFlagSet()) collection.SetPreloadTableFlag(true);
        }
        catch (Exception e)
        {
            preload = "FAILED (" + e.Message + ")";
        }

        EditorUtility.SetDirty(collection);
        EditorUtility.SetDirty(shared);
        foreach (StringTable table in tables.Values) EditorUtility.SetDirty(table);
        LocalizationEditorSettings.EditorEvents.RaiseCollectionModified(collection, collection);

        summary.AppendLine("  " + name + ".csv -> String Table collection \"" + name + "\" (" + how + ", " + AssetDatabase.GetAssetPath(collection) + ")");
        summary.AppendLine("    " + rows + " keys (" + keysAdded + " new); values: " + added + " added, " + updated + " changed, " + cleared +
                           " cleared; " + smart + " Smart; " + contexts + " contexts written; preload " + preload);
        if (missingColumns.Count > 0) summary.AppendLine("    WARNING no column for: " + string.Join(", ", missingColumns) + " (those languages show English)");
        if (ignored.Count > 0) summary.AppendLine("    ignored columns (no such locale in the project): " + string.Join(", ", ignored));
        if (duplicates.Count > 0) summary.AppendLine("    WARNING duplicate keys (first row kept): " + List(duplicates));

        var untranslatedLine = new List<string>();
        foreach (KeyValuePair<Locale, int> column in columns)
        {
            string code = column.Key.Identifier.Code;
            int count = untranslated.TryGetValue(code, out List<string> list) ? list.Count : 0;
            untranslatedLine.Add(code + " " + count);
        }
        summary.AppendLine("    empty cells: " + string.Join(", ", untranslatedLine));
        foreach (KeyValuePair<string, List<string>> pair in untranslated)
        {
            string note = pair.Key == L10n.English ? "WARNING English is empty (shows ⟦table/key⟧ in the Editor)" : "untranslated, the English text shows";
            summary.AppendLine("    " + pair.Key + " " + note + ": " + List(pair.Value));
        }
        if (brokenPlaceholders.Count > 0)
            summary.AppendLine("    WARNING placeholders the English text doesn't have (they won't format): " + List(brokenPlaceholders));
        if (droppedPlaceholders.Count > 0)
            summary.AppendLine("    placeholders a translation leaves out (fine if on purpose): " + List(droppedPlaceholders));
        if (kept.Count > 0)
            summary.AppendLine("    kept, no longer in the CSV (delete them in the Tables window if unused): " + List(kept));
    }

    /// <summary>Context as the key's Comment metadata (removed when the cell is empty); true when it changed.</summary>
    static bool SetComment(SharedTableData.SharedTableEntry entry, string context)
    {
        if (entry.Metadata == null)
        {
            if (string.IsNullOrEmpty(context)) return false;
            entry.Metadata = new MetadataCollection();
        }
        Comment comment = entry.Metadata.GetMetadata<Comment>();
        if (string.IsNullOrEmpty(context))
        {
            if (comment == null) return false;
            entry.Metadata.RemoveMetadata(comment);
            return true;
        }
        if (comment == null)
        {
            entry.Metadata.AddMetadata(new Comment { CommentText = context });
            return true;
        }
        if (comment.CommentText == context) return false;
        comment.CommentText = context;
        return true;
    }

    static HashSet<string> PlaceholderNames(string text)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(text)) return names;
        foreach (Match match in PlaceholderName.Matches(text))
            names.Add(match.Groups[1].Value);
        return names;
    }

    /// <summary>The package's rules for a collection name (it becomes file and Addressables names); null when fine.</summary>
    static string ValidateCollectionName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name != name.Trim()) return "the file name makes an invalid collection name";
        if (name.Contains("[") && name.Contains("]")) return "a collection name can't contain both [ and ]";
        char[] invalid = Path.GetInvalidFileNameChars().Concat(new[] { '/', '?', '<', '>', '\\', ':', '|', '"' }).ToArray();
        if (name.IndexOfAny(invalid) >= 0) return "the collection name has characters a file name can't have";
        return null;
    }

    static string List(List<string> items)
    {
        if (items.Count <= ListLimit) return string.Join(", ", items);
        return string.Join(", ", items.Take(ListLimit)) + " … and " + (items.Count - ListLimit) + " more (Export Missing List has them all)";
    }

    // ------------------------------------------------------------------
    // Hashes (per machine, in Library)
    // ------------------------------------------------------------------

    static string Hash(byte[] bytes)
    {
        using (SHA1 sha = SHA1.Create())
        {
            byte[] digest = sha.ComputeHash(bytes);
            var text = new StringBuilder(digest.Length * 2);
            foreach (byte b in digest) text.Append(b.ToString("x2"));
            return text.ToString();
        }
    }

    static Dictionary<string, string> LoadHashes()
    {
        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(HashFile)) return hashes;
            foreach (string line in File.ReadAllLines(HashFile))
            {
                int tab = line.LastIndexOf('\t');
                if (tab > 0) hashes[line.Substring(0, tab)] = line.Substring(tab + 1).Trim();
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[L10n CSV] Couldn't read " + HashFile + " (every CSV will be imported): " + e.Message);
        }
        return hashes;
    }

    static void SaveHashes(Dictionary<string, string> hashes)
    {
        try
        {
            File.WriteAllLines(HashFile, hashes.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).Select(p => p.Key + "\t" + p.Value));
        }
        catch (Exception e)
        {
            Debug.LogWarning("[L10n CSV] Couldn't write " + HashFile + ": " + e.Message);
        }
    }

    // ------------------------------------------------------------------
    // Missing list
    // ------------------------------------------------------------------

    /// <summary>Write every untranslated key per language (missing or empty in the tables) to Assets/Localization/Reports/untranslated.txt.</summary>
    public static string ExportMissingList()
    {
        ReadOnlyCollection<StringTableCollection> collections = LocalizationEditorSettings.GetStringTableCollections();
        ReadOnlyCollection<Locale> locales = LocalizationEditorSettings.GetLocales();
        if (collections.Count == 0 || locales.Count == 0)
            return "[L10n] Nothing to export: no String Table collections or locales yet (Tools > Game > Localization > Set Up).";

        var perLocale = new Dictionary<string, List<string>>();
        foreach (Locale locale in locales) perLocale[locale.Identifier.Code] = new List<string>();
        int keys = 0;
        foreach (StringTableCollection collection in collections)
        {
            if (collection == null || collection.SharedData == null) continue;
            var english = collection.GetTable(new LocaleIdentifier(L10n.English)) as StringTable;
            foreach (SharedTableData.SharedTableEntry shared in collection.SharedData.Entries)
            {
                keys++;
                string englishText = english != null ? english.GetEntry(shared.Id)?.Value ?? "" : "";
                foreach (Locale locale in locales)
                {
                    var table = collection.GetTable(locale.Identifier) as StringTable;
                    StringTableEntry entry = table != null ? table.GetEntry(shared.Id) : null;
                    if (entry == null || string.IsNullOrWhiteSpace(entry.Value))
                        perLocale[locale.Identifier.Code].Add(collection.TableCollectionName + "\t" + shared.Key + "\t" + OneLine(englishText));
                }
            }
        }

        var text = new StringBuilder();
        text.Append("# Untranslated keys per language: missing or empty in the String Tables, so the game shows the English text.\n");
        text.Append("# Written by Tools > Game > Localization > Export Missing List, " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + ".\n");
        text.Append("# Fill the cells in Assets/Localization/Source/<table>.csv. Columns: table, key, English.\n");
        var counts = new List<string>();
        foreach (KeyValuePair<string, List<string>> pair in perLocale)
        {
            counts.Add(pair.Key + " " + pair.Value.Count);
            text.Append("\n## " + pair.Key + ": " + pair.Value.Count + " of " + keys + " untranslated\n");
            foreach (string line in pair.Value) text.Append(line).Append('\n');
        }

        LocalizationSetup.EnsureFolder(LocalizationSetup.ReportsFolder);
        File.WriteAllText(ReportPath, text.ToString(), new UTF8Encoding(false));
        AssetDatabase.ImportAsset(ReportPath);
        return "[L10n] Untranslated keys (" + keys + " keys): " + string.Join(", ", counts) + " -> " + ReportPath;
    }

    static string OneLine(string text)
    {
        return string.IsNullOrEmpty(text) ? "" : text.Replace("\r", "").Replace("\n", "\\n").Replace("\t", " ");
    }
}

/// <summary>Imports a localization CSV soon after Unity re-imports it (edited outside Unity, then back in the Editor).</summary>
sealed class LocalizationCsvPostprocessor : AssetPostprocessor
{
    static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
    {
        if (Application.isBatchMode) return;
        string prefix = LocalizationSetup.SourceFolder + "/";
        foreach (string path in importedAssets)
        {
            if (path.StartsWith(prefix, StringComparison.Ordinal) && path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                LocalizationCsvImporter.ScheduleImport();
                return;
            }
        }
    }
}
