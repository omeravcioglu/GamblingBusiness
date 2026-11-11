using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > Game > Localization > Sync Content CSVs: merges the English of the game's content into the translation CSVs.
/// Every IContentTextSource (found by reflection: a class with a public parameterless constructor) is asked for its texts;
/// sources naming the same Table go into one Assets/Localization/Source/&lt;Table&gt;.csv, in source order (by type name),
/// each in its own order. Per table:
/// - a key the CSV doesn't have yet is added with its English and context and EMPTY translations (the English shows until
///   they are drafted), right after the nearest earlier key of the data (else before the nearest later one, else at the end);
/// - a key whose English changed gets the new English; its translations are kept (they may be stale) and the key is listed
///   in Assets/Localization/Reports/content_changed.txt (the list keeps growing until someone deletes lines);
/// - a key the data no longer has is kept, never deleted (the importer keeps table keys too), and listed in the report;
/// - an empty Context cell is filled from the source; a written one is kept (reviewers' notes stay);
/// - only keys whose first segment a source of that table uses ("event.", "skill.") count as content: other rows of the
///   same CSV (hand-written texts such as "risk.note.jinx_removed") are left exactly as they are and never reported.
/// The file keeps its column order, BOM and line ends (a new file: UTF-8 with a BOM, CRLF, Key, Context, en and the game's
/// languages); cells are written with standard CSV quoting (L10nCsv.Quote). Changed files are then imported (the usual CSV
/// import). Merge() does the work on text, without files, for tests.
/// </summary>
public static class ContentTextSync
{
    const string MenuPath = "Tools/Game/Localization/Sync Content CSVs";
    const string ChangedHeading = "## English changed since the translations were written";

    public static string ReportPath => LocalizationSetup.ReportsFolder + "/content_changed.txt";

    /// <summary>A key whose English changed: its translations were kept.</summary>
    public struct Change
    {
        public string Key;
        public string OldEnglish;
        public string NewEnglish;
        public int Translations;   // non-empty translation cells kept
    }

    /// <summary>What the sync did to one table.</summary>
    public sealed class TableResult
    {
        public string Table = "";
        public string Path = "";
        public string Sources = "";
        public bool Created;
        public bool Written;
        public int Keys;
        public int ContextsFilled;
        public string Error;
        public readonly List<string> Added = new List<string>();
        public readonly List<Change> Changed = new List<Change>();
        public readonly List<KeyValuePair<string, string>> Stale = new List<KeyValuePair<string, string>>();   // key, English
        public readonly List<string> Duplicates = new List<string>();
        public readonly List<string> Invalid = new List<string>();
    }

    [MenuItem(MenuPath, false, 4)]
    static void SyncMenu()
    {
        Debug.Log(Sync());
    }

    [MenuItem(MenuPath, true)]
    static bool SyncMenuValidate() => !EditorApplication.isPlayingOrWillChangePlaymode;

    /// <summary>Merge every source into its CSV, write the report, import the changed CSVs. Returns the summary.</summary>
    public static string Sync()
    {
        var log = new StringBuilder();
        List<IContentTextSource> sources = FindSources(log);
        if (sources.Count == 0)
            return "[L10n Sync] No content sources: nothing implements IContentTextSource." + (log.Length > 0 ? "\n" + log.ToString().TrimEnd() : "");

        var tables = new SortedDictionary<string, List<IContentTextSource>>(StringComparer.Ordinal);
        foreach (IContentTextSource source in sources)
        {
            string table = SafeTable(source);
            if (!tables.TryGetValue(table, out List<IContentTextSource> list)) tables[table] = list = new List<IContentTextSource>();
            list.Add(source);
        }

        var results = new List<TableResult>();
        foreach (KeyValuePair<string, List<IContentTextSource>> table in tables)
            results.Add(SyncTable(table.Key, table.Value));

        string report = "";
        try
        {
            WriteReport(results);
            AssetDatabase.ImportAsset(ReportPath);
            report = ReportPath;
        }
        catch (Exception e)
        {
            report = "FAILED (" + e.Message + ")";
        }

        var written = results.Where(r => r.Written).Select(r => r.Path).ToList();
        foreach (string path in written)
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        var summary = new StringBuilder("[L10n Sync] Sync Content CSVs: " + sources.Count + " source" + (sources.Count == 1 ? "" : "s") + ", " +
                                        results.Count + " table" + (results.Count == 1 ? "" : "s") + "\n");
        foreach (TableResult r in results)
        {
            summary.Append("  " + r.Table + ".csv (" + r.Sources + "): ");
            if (r.Error != null)
            {
                summary.Append("NOT SYNCED, " + r.Error + "\n");
                continue;
            }
            summary.Append(r.Keys + " keys; " + r.Added.Count + " added, " + r.Changed.Count + " English changed, " + r.Stale.Count +
                           " no longer in the data (kept), " + r.ContextsFilled + " contexts filled; " +
                           (r.Written ? (r.Created ? "created" : "written") : "unchanged") + "\n");
            if (r.Added.Count > 0) summary.Append("    added (untranslated): " + Short(r.Added) + "\n");
            if (r.Changed.Count > 0) summary.Append("    English changed (translations kept, see the report): " + Short(r.Changed.Select(c => c.Key).ToList()) + "\n");
            if (r.Stale.Count > 0) summary.Append("    no longer in the data: " + Short(r.Stale.Select(s => s.Key).ToList()) + "\n");
            if (r.Duplicates.Count > 0) summary.Append("    WARNING keys given twice (first kept): " + Short(r.Duplicates) + "\n");
            if (r.Invalid.Count > 0) summary.Append("    WARNING unusable keys (skipped): " + Short(r.Invalid) + "\n");
        }
        if (log.Length > 0) summary.Append(log);
        summary.Append("  Report: " + report);

        // The usual import (it logs its own summary)
        if (written.Count > 0) LocalizationCsvImporter.ImportChanged("Sync Content CSVs");
        return summary.ToString();
    }

    // ------------------------------------------------------------------
    // Sources and files
    // ------------------------------------------------------------------

    static List<IContentTextSource> FindSources(StringBuilder log)
    {
        var sources = new List<IContentTextSource>();
        foreach (Type type in TypeCache.GetTypesDerivedFrom<IContentTextSource>())
        {
            if (type.IsAbstract || type.IsInterface || type.ContainsGenericParameters) continue;
            if (type.GetConstructor(Type.EmptyTypes) == null)
            {
                log.Append("  skipped " + type.FullName + ": no public parameterless constructor\n");
                continue;
            }
            try
            {
                sources.Add((IContentTextSource)Activator.CreateInstance(type));
            }
            catch (Exception e)
            {
                log.Append("  skipped " + type.FullName + ": " + (e.InnerException ?? e).Message + "\n");
            }
        }
        sources.Sort((a, b) => string.CompareOrdinal(a.GetType().FullName, b.GetType().FullName));
        return sources;
    }

    static string SafeTable(IContentTextSource source)
    {
        try
        {
            return (source.Table ?? "").Trim();
        }
        catch (Exception)
        {
            return "";
        }
    }

    static TableResult SyncTable(string table, List<IContentTextSource> sources)
    {
        var result = new TableResult
        {
            Table = table,
            Path = LocalizationSetup.SourceFolder + "/" + table + ".csv",
            Sources = string.Join(", ", sources.Select(s => s.GetType().Name))
        };
        if (table.Length == 0 || table.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || table.IndexOfAny(new[] { '/', '\\', '[', ']' }) >= 0)
        {
            result.Error = "\"" + table + "\" can't be a CSV / String Table name";
            return result;
        }

        // Every text, in order (a source that fails leaves the CSV untouched: half the data would look like removed keys)
        var texts = new List<ContentText>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (IContentTextSource source in sources)
        {
            List<ContentText> collected;
            try
            {
                collected = (source.Collect() ?? Enumerable.Empty<ContentText>()).ToList();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                result.Error = source.GetType().Name + " failed (" + e.Message + ")";
                return result;
            }
            foreach (ContentText text in collected)
            {
                string key = text.Key != null ? text.Key.Trim() : "";
                if (key.Length == 0 || key.StartsWith("#", StringComparison.Ordinal) || key.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0)
                {
                    result.Invalid.Add(key.Length == 0 ? "(empty key from " + source.GetType().Name + ")" : key);
                    continue;
                }
                if (!seen.Add(key))
                {
                    result.Duplicates.Add(key);
                    continue;
                }
                texts.Add(new ContentText(key, text.Context ?? "", L10nCsv.NormalizeNewlines(text.English ?? "")));
            }
        }

        string existing = "";
        bool bom = true;
        if (!File.Exists(result.Path) && texts.Count == 0) return result;   // no file for a source without texts
        if (File.Exists(result.Path))
        {
            byte[] bytes = File.ReadAllBytes(result.Path);
            bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            existing = new UTF8Encoding(false, false).GetString(bytes);
            if (existing.IndexOf('\uFFFD') >= 0)
            {
                result.Error = "it has bytes that aren't UTF-8 (save it as \"CSV UTF-8\" first)";
                return result;
            }
            if (existing.Length > 0 && existing[0] == '\uFEFF') existing = existing.Substring(1);
        }

        string merged = Merge(existing, texts, L10n.Languages.Select(l => l.Code).ToList(), result);
        if (merged == null || merged == existing) return result;

        LocalizationSetup.EnsureFolder(LocalizationSetup.SourceFolder);
        File.WriteAllText(result.Path, merged, new UTF8Encoding(bom));
        result.Written = true;
        return result;
    }

    // ------------------------------------------------------------------
    // The merge (text in, text out)
    // ------------------------------------------------------------------

    /// <summary>
    /// Merge <paramref name="texts"/> (keys, contexts and English in data order) into the CSV text <paramref name="csv"/>
    /// ("" when there is no file yet: a new one gets Key, Context, en and <paramref name="languageCodes"/>). Returns the new
    /// CSV text (no BOM; the line ends of the old text, else CRLF), or null when the old header has no Key or en column.
    /// Fills <paramref name="result"/> (added, changed, no longer in the data, contexts filled).
    /// </summary>
    public static string Merge(string csv, IList<ContentText> texts, IList<string> languageCodes, TableResult result)
    {
        csv = csv ?? "";
        if (csv.Length > 0 && csv[0] == '\uFEFF') csv = csv.Substring(1);
        bool exists = csv.Trim('\r', '\n', ' ', '\t').Length > 0;

        string newline = "\r\n";
        int firstBreak = csv.IndexOf('\n');
        if (exists && firstBreak >= 0 && (firstBreak == 0 || csv[firstBreak - 1] != '\r')) newline = "\n";

        List<string> header;
        List<List<string>> rows;
        if (exists)
        {
            L10nCsv.Sheet sheet = L10nCsv.ReadSheet(csv);
            header = new List<string>(sheet.Header);
            rows = sheet.Rows;
        }
        else
        {
            header = new List<string> { "Key", "Context", L10n.English };
            foreach (string code in languageCodes)
            {
                if (!string.IsNullOrEmpty(code) && IndexOf(header, code) < 0) header.Add(code);
            }
            rows = new List<List<string>>();
            result.Created = true;
        }

        int keyColumn = IndexOf(header, "Key");
        int englishColumn = IndexOf(header, L10n.English);
        if (keyColumn < 0 || englishColumn < 0)
        {
            result.Error = "its header has no Key or en column";
            return null;
        }
        int contextColumn = IndexOf(header, "Context");
        if (contextColumn < 0)
        {
            header.Add("Context");
            contextColumn = header.Count - 1;
        }

        // The rows the CSV has (the first one of a repeated key; the importer warns about the others)
        var rowOf = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (List<string> row in rows)
        {
            string key = Cell(row, keyColumn).Trim();
            if (IsKey(key) && !rowOf.ContainsKey(key)) rowOf[key] = row;
        }
        var inData = new HashSet<string>(StringComparer.Ordinal);
        var owned = new HashSet<string>(StringComparer.Ordinal);
        foreach (ContentText text in texts)
        {
            inData.Add(text.Key);
            owned.Add(FirstSegment(text.Key));
        }

        for (int i = 0; i < texts.Count; i++)
        {
            ContentText text = texts[i];
            string english = text.English ?? "";
            if (rowOf.TryGetValue(text.Key, out List<string> row))
            {
                string old = L10nCsv.NormalizeNewlines(Cell(row, englishColumn));
                if (old != english)
                {
                    result.Changed.Add(new Change
                    {
                        Key = text.Key,
                        OldEnglish = old,
                        NewEnglish = english,
                        Translations = CountTranslations(row, header, keyColumn, contextColumn, englishColumn)
                    });
                    SetCell(row, englishColumn, english);
                }
                if (Cell(row, contextColumn).Trim().Length == 0 && !string.IsNullOrEmpty(text.Context))
                {
                    SetCell(row, contextColumn, text.Context);
                    result.ContextsFilled++;
                }
                continue;
            }

            var added = new List<string>(header.Count);
            for (int c = 0; c < header.Count; c++) added.Add("");
            added[keyColumn] = text.Key;
            added[contextColumn] = text.Context ?? "";
            added[englishColumn] = english;
            rows.Insert(InsertAt(rows, rowOf, texts, i), added);
            rowOf[text.Key] = added;
            result.Added.Add(text.Key);
        }

        // Content keys the data no longer has: kept, reported
        foreach (List<string> row in rows)
        {
            string key = Cell(row, keyColumn).Trim();
            if (!IsKey(key) || inData.Contains(key) || !owned.Contains(FirstSegment(key))) continue;
            result.Stale.Add(new KeyValuePair<string, string>(key, L10nCsv.NormalizeNewlines(Cell(row, englishColumn))));
        }
        result.Keys = texts.Count;

        var output = new StringBuilder();
        AppendRow(output, header, header.Count, newline);
        foreach (List<string> row in rows) AppendRow(output, row, header.Count, newline);
        return output.ToString();
    }

    // Right after the nearest earlier key of the data that the CSV has, else before the nearest later one, else at the end
    static int InsertAt(List<List<string>> rows, Dictionary<string, List<string>> rowOf, IList<ContentText> texts, int index)
    {
        for (int j = index - 1; j >= 0; j--)
        {
            if (rowOf.TryGetValue(texts[j].Key, out List<string> before)) return rows.IndexOf(before) + 1;
        }
        for (int j = index + 1; j < texts.Count; j++)
        {
            if (rowOf.TryGetValue(texts[j].Key, out List<string> after)) return rows.IndexOf(after);
        }
        return rows.Count;
    }

    static void AppendRow(StringBuilder output, List<string> row, int columns, string newline)
    {
        // A blank line stays blank; a short row is padded to the header; empty cells past the header are dropped
        if (row.Count <= 1 && Cell(row, 0).Length == 0)
        {
            output.Append(newline);
            return;
        }
        int count = Math.Max(columns, row.Count);
        while (count > columns && string.IsNullOrEmpty(row[count - 1])) count--;
        for (int c = 0; c < count; c++)
        {
            if (c > 0) output.Append(',');
            output.Append(L10nCsv.Quote(Cell(row, c)));
        }
        output.Append(newline);
    }

    static int CountTranslations(List<string> row, List<string> header, int keyColumn, int contextColumn, int englishColumn)
    {
        int count = 0;
        for (int c = 0; c < header.Count; c++)
        {
            if (c == keyColumn || c == contextColumn || c == englishColumn || string.IsNullOrWhiteSpace(header[c])) continue;
            if (Cell(row, c).Trim().Length > 0) count++;
        }
        return count;
    }

    static bool IsKey(string key) => key.Length > 0 && !key.StartsWith("#", StringComparison.Ordinal);

    static string FirstSegment(string key)
    {
        int dot = key.IndexOf('.');
        return dot < 0 ? key : key.Substring(0, dot);
    }

    static int IndexOf(List<string> header, string name)
    {
        for (int i = 0; i < header.Count; i++)
        {
            if (string.Equals((header[i] ?? "").Trim(), name, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return -1;
    }

    static string Cell(List<string> row, int column) => L10nCsv.Sheet.Cell(row, column);

    static void SetCell(List<string> row, int column, string value)
    {
        while (row.Count <= column) row.Add("");
        row[column] = value ?? "";
    }

    // ------------------------------------------------------------------
    // Report
    // ------------------------------------------------------------------

    static void WriteReport(List<TableResult> results)
    {
        // The "English changed" lines of earlier runs stay until someone deletes them: they mark translations to check
        var history = new List<string>();
        if (File.Exists(ReportPath))
        {
            bool inChanged = false;
            foreach (string line in File.ReadAllLines(ReportPath, Encoding.UTF8))
            {
                if (line.StartsWith("## ", StringComparison.Ordinal))
                {
                    inChanged = line.StartsWith(ChangedHeading, StringComparison.Ordinal);
                    continue;
                }
                if (inChanged && line.Trim().Length > 0 && !line.StartsWith("#", StringComparison.Ordinal)) history.Add(line);
            }
        }

        string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        foreach (TableResult r in results)
        {
            foreach (Change c in r.Changed)
            {
                history.Add(stamp + "\t" + r.Table + "\t" + c.Key + "\t" + (c.Translations > 0 ? c.Translations + " translations kept" : "untranslated") +
                            "\twas: " + OneLine(c.OldEnglish) + "\tnow: " + OneLine(c.NewEnglish));
            }
        }

        var text = new StringBuilder();
        text.Append("# Written by Tools > Game > Localization > Sync Content CSVs, last run " + stamp + ".\n");
        text.Append("# The English of the game's content (risk cards, skills, hiring tasks, majors...) lives in its data; the sync merges it into\n");
        text.Append("# Assets/Localization/Source/<Table>.csv. Columns are separated by tabs.\n");

        text.Append("\n## Last run\n");
        foreach (TableResult r in results)
        {
            text.Append(r.Table + "\t" + (r.Error != null ? "NOT SYNCED: " + r.Error
                : r.Keys + " keys, " + r.Added.Count + " added, " + r.Changed.Count + " English changed, " + r.Stale.Count + " no longer in the data" +
                  (r.Written ? "" : ", file unchanged")) + "\n");
            foreach (string key in r.Added) text.Append(r.Table + "\tadded (untranslated)\t" + key + "\n");
        }

        text.Append("\n## Keys no longer in the data (kept in the CSV and the String Table: delete them there once nothing uses them)\n");
        foreach (TableResult r in results)
        {
            foreach (KeyValuePair<string, string> stale in r.Stale) text.Append(r.Table + "\t" + stale.Key + "\t" + OneLine(stale.Value) + "\n");
        }

        text.Append("\n" + ChangedHeading + " (they were kept: re-translate, then delete the line)\n");
        text.Append("# when, table, key, translations, old English, new English\n");
        foreach (string line in history) text.Append(line + "\n");

        LocalizationSetup.EnsureFolder(LocalizationSetup.ReportsFolder);
        File.WriteAllText(ReportPath, text.ToString(), new UTF8Encoding(false));
    }

    static string OneLine(string text)
    {
        return string.IsNullOrEmpty(text) ? "" : text.Replace("\r", "").Replace("\n", "\\n").Replace("\t", " ");
    }

    static string Short(List<string> items)
    {
        const int limit = 20;
        return items.Count <= limit ? string.Join(", ", items) : string.Join(", ", items.Take(limit)) + " … and " + (items.Count - limit) + " more";
    }
}
