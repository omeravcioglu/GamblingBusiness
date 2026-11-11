using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Pseudo;
using UnityEngine.Localization.Tables;
using Object = UnityEngine.Object;

/// <summary>
/// Tools > Game > Localization > Check Tables: every key has a value in every language (a missing or empty value shows
/// the English text in game); lists the gaps per language.
/// Tools > Game > Localization > Check Fonts: per language, the Fonts table's font (material and atlases saved in the
/// file, multi-atlas, dynamic, cleared on build, Addressable, fallback chain) and whether every character the CSVs use
/// for that language has a glyph in the font or its fallbacks (the "tofu" check, read from the .ttf files).
/// </summary>
public static class LocalizationChecks
{
    const string MenuRoot = "Tools/Game/Localization/";
    // Characters code adds around the texts: money, signs, percentages
    const string RuntimeCharacters = "0123456789$,.+−%";

    static readonly Regex Placeholder = new Regex(@"\{[^{}]*\}");
    static readonly Dictionary<string, HashSet<int>> s_Cmaps = new Dictionary<string, HashSet<int>>();

    [MenuItem(MenuRoot + "Check Tables", false, 20)]
    static void CheckTablesMenu()
    {
        string result = CheckTables();
        if (result.StartsWith("COMPLETE", StringComparison.Ordinal)) Debug.Log("[L10n] Table check: " + result);
        else Debug.LogWarning("[L10n] Table check: " + result);
    }

    [MenuItem(MenuRoot + "Check Fonts", false, 21)]
    static void CheckFontsMenu()
    {
        string result = CheckFonts(out int problems);
        if (problems == 0) Debug.Log(result);
        else Debug.LogWarning(result);
    }

    // ------------------------------------------------------------------
    // Tables
    // ------------------------------------------------------------------

    /// <summary>COMPLETE, GAPS (per language, then every gap) or INCONCLUSIVE (nothing to check).</summary>
    public static string CheckTables()
    {
        var gaps = new List<string>();
        var perLocale = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int checkedCount = 0;

        foreach (StringTableCollection collection in LocalizationEditorSettings.GetStringTableCollections())
        {
            if (collection == null || collection.SharedData == null) continue;
            foreach (SharedTableData.SharedTableEntry key in collection.SharedData.Entries)
            {
                foreach (StringTable table in collection.StringTables)
                {
                    if (table == null) continue;
                    string code = table.LocaleIdentifier.Code;
                    if (!perLocale.ContainsKey(code)) perLocale[code] = 0;
                    checkedCount++;
                    StringTableEntry entry = table.GetEntry(key.Id);
                    // A missing entry and a present-but-empty one both show as untranslated in game
                    if (entry == null || string.IsNullOrWhiteSpace(entry.Value))
                    {
                        gaps.Add(collection.TableCollectionName + " / " + code + " / " + key.Key);
                        perLocale[code]++;
                    }
                }
            }
        }

        // Zero entries is not a pass: nothing was examined
        if (checkedCount == 0)
            return "INCONCLUSIVE: no table entries found. Either no String Table Collection exists yet, or the collection has no locale tables.";
        if (gaps.Count == 0)
            return "COMPLETE: " + checkedCount + " entries checked, no gaps (" + string.Join(", ", perLocale.Keys) + ")";
        return "GAPS (" + gaps.Count + " of " + checkedCount + " checked): " + string.Join(", ", perLocale.Select(p => p.Key + " " + p.Value)) +
               "\n  " + string.Join("\n  ", gaps);
    }

    // ------------------------------------------------------------------
    // Fonts
    // ------------------------------------------------------------------

    public static string CheckFonts(out int problems)
    {
        problems = 0;
        var report = new StringBuilder("[L10n] Font check\n");
        AssetTableCollection fonts = LocalizationSetup.FindAssetCollection(L10n.FontsTable);
        if (fonts == null)
        {
            problems++;
            report.AppendLine("  FAIL there is no Fonts Asset Table (Tools > Game > Localization > Set Up)");
            return report.ToString();
        }

        AddressableAssetSettings addressables = AddressableAssetSettingsDefaultObject.Settings;
        List<TMP_FontAsset> global = GlobalFallbacks();
        TMP_FontAsset lastResort = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(LocalizationSetup.FontsFolder + "/LXGWWenKai SDF.asset");
        bool globalOk = lastResort != null && global.Contains(lastResort);
        if (!globalOk) problems++;
        report.AppendLine("  " + (globalOk ? "ok  " : "FAIL") + " TMP global fallbacks: [" + string.Join(", ", global.Select(f => f != null ? f.name : "(missing)")) + "]");

        Dictionary<string, string> texts = CsvTextPerLocale();
        foreach (Locale locale in LocalizationEditorSettings.GetLocales())
        {
            string code = locale.Identifier.Code;
            TMP_FontAsset font = TableFont(fonts, code);
            texts.TryGetValue(code, out string text);
            problems += CheckFont(code, font, text ?? "", global, addressables, report);
        }

        // The pseudo-locale reads the en table (its font is en's): check the accented letters it makes
        PseudoLocale pseudo = LocalizationEditorSettings.GetPseudoLocales().FirstOrDefault();
        if (pseudo != null && texts.TryGetValue(L10n.English, out string english))
        {
            string accented;
            try { accented = pseudo.GetPseudoString(english); }
            catch (Exception) { accented = english; }
            problems += CheckFont(L10n.PseudoCode, TableFont(fonts, L10n.English), accented, global, addressables, report);
        }

        report.Append(problems == 0 ? "  => no problems" : "  => " + problems + " problem(s)");
        return report.ToString();
    }

    static int CheckFont(string code, TMP_FontAsset font, string text, List<TMP_FontAsset> global, AddressableAssetSettings addressables, StringBuilder report)
    {
        int problems = 0;
        if (font == null)
        {
            report.AppendLine("  == " + code + ": FAIL no MainFont in the Fonts table");
            return 1;
        }
        string path = AssetDatabase.GetAssetPath(font);
        report.AppendLine("  == " + code + ": " + font.name + " (" + path + ")");

        // The file, not the object in memory
        Object[] onFile = AssetDatabase.LoadAllAssetsAtPath(path);
        int materials = onFile.OfType<Material>().Count();
        int textures = onFile.OfType<Texture2D>().Count();
        bool dynamic = font.atlasPopulationMode == AtlasPopulationMode.Dynamic;
        bool clears = new SerializedObject(font).FindProperty("m_ClearDynamicDataOnBuild")?.boolValue ?? false;
        string source = font.sourceFontFile != null ? Path.GetFileName(AssetDatabase.GetAssetPath(font.sourceFontFile)) : "none";
        bool fileOk = materials >= 1 && textures >= 1 && font.isMultiAtlasTexturesEnabled && dynamic;
        bool ownedByUi = path.StartsWith("Assets/UI/", StringComparison.Ordinal);   // Coming Soon: the UI session's asset, checked as is
        if (!fileOk && !ownedByUi) problems++;
        report.AppendLine("     " + (fileOk || ownedByUi ? "ok  " : "FAIL") + " file: material " + (materials >= 1 ? "saved" : "NOT saved") + ", " + textures +
                          " atlas texture(s), multi-atlas " + OnOff(font.isMultiAtlasTexturesEnabled) + ", " + (dynamic ? "dynamic" : "static") +
                          ", cleared on build " + OnOff(clears) + ", source " + source);

        string guid = AssetDatabase.AssetPathToGUID(path);
        AddressableAssetEntry entry = addressables != null ? addressables.FindAssetEntry(guid) : null;
        bool inResources = path.Contains("/Resources/");
        if (entry == null || inResources) problems++;
        report.AppendLine("     " + (entry != null && !inResources ? "ok  " : "FAIL") + " Addressable: " +
                          (entry != null ? "group " + entry.parentGroup?.Name + ", labels " + string.Join(" ", entry.labels) : "no") +
                          (inResources ? " (in a Resources folder: Asset Tables can't load it)" : ""));

        List<TMP_FontAsset> chain = Chain(font, global);
        report.AppendLine("     fallback chain: " + string.Join(" -> ", chain.Select(f => f.name)));

        // Glyph coverage of every character the language's texts use
        var characters = new SortedSet<int>();
        foreach (int c in Codepoints(Placeholder.Replace(text, "") + RuntimeCharacters))
        {
            if (!char.IsWhiteSpace((char)Math.Min(c, 0xFFFF)) && !char.IsControl((char)Math.Min(c, 0xFFFF))) characters.Add(c);
        }
        var fromFallback = new Dictionary<string, List<int>>();
        var missing = new List<int>();
        int own = 0;
        foreach (int c in characters)
        {
            TMP_FontAsset provider = chain.FirstOrDefault(f => Covers(f, c));
            if (provider == null) missing.Add(c);
            else if (provider == font) own++;
            else
            {
                if (!fromFallback.TryGetValue(provider.name, out List<int> list)) fromFallback[provider.name] = list = new List<int>();
                list.Add(c);
            }
        }
        if (missing.Count > 0) problems++;
        var line = new StringBuilder("     " + (missing.Count == 0 ? "ok  " : "FAIL") + " glyphs: " + characters.Count + " characters, " + own + " in " + font.name);
        foreach (KeyValuePair<string, List<int>> pair in fromFallback)
            line.Append(", " + pair.Value.Count + " from " + pair.Key + " (" + Sample(pair.Value) + ")");
        if (missing.Count > 0) line.Append(", MISSING (tofu): " + Sample(missing));
        report.AppendLine(line.ToString());
        return problems;
    }

    /// <summary>The font, its fallbacks (depth first, as TMP searches them), then TMP Settings' global fallbacks.</summary>
    static List<TMP_FontAsset> Chain(TMP_FontAsset font, List<TMP_FontAsset> global)
    {
        var chain = new List<TMP_FontAsset>();
        void Add(TMP_FontAsset f, int depth)
        {
            if (f == null || chain.Contains(f) || depth > 4) return;
            chain.Add(f);
            if (f.fallbackFontAssetTable == null) return;
            foreach (TMP_FontAsset fallback in f.fallbackFontAssetTable) Add(fallback, depth + 1);
        }
        Add(font, 0);
        foreach (TMP_FontAsset f in global) Add(f, 1);
        return chain;
    }

    static bool Covers(TMP_FontAsset font, int codepoint)
    {
        if (font.sourceFontFile != null)
        {
            string path = AssetDatabase.GetAssetPath(font.sourceFontFile);
            if (!string.IsNullOrEmpty(path) && File.Exists(path)) return ReadCmap(path).Contains(codepoint);
        }
        // A static font asset: only what is baked in
        return font.characterLookupTable != null && font.characterLookupTable.ContainsKey((uint)codepoint);
    }

    static TMP_FontAsset TableFont(AssetTableCollection fonts, string code)
    {
        var table = fonts.GetTable(new LocaleIdentifier(code)) as AssetTable;
        SharedTableData.SharedTableEntry shared = fonts.SharedData != null ? fonts.SharedData.GetEntry(L10n.MainFontKey) : null;
        AssetTableEntry entry = table != null && shared != null ? table.GetEntry(shared.Id) : null;
        if (entry == null || string.IsNullOrEmpty(entry.Guid)) return null;
        return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(entry.Guid));
    }

    static List<TMP_FontAsset> GlobalFallbacks()
    {
        var list = new List<TMP_FontAsset>();
        foreach (string guid in AssetDatabase.FindAssets("t:TMP_Settings", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.EndsWith("/Resources/TMP Settings.asset", StringComparison.Ordinal)) continue;
            var settings = AssetDatabase.LoadAssetAtPath<TMP_Settings>(path);
            SerializedProperty fallbacks = settings != null ? new SerializedObject(settings).FindProperty("m_fallbackFontAssets") : null;
            if (fallbacks == null) continue;
            for (int i = 0; i < fallbacks.arraySize; i++)
                list.Add(fallbacks.GetArrayElementAtIndex(i).objectReferenceValue as TMP_FontAsset);
            break;
        }
        list.RemoveAll(f => f == null);
        return list;
    }

    /// <summary>Every cell of each language's column in every CSV, joined (the characters that language shows).</summary>
    static Dictionary<string, string> CsvTextPerLocale()
    {
        var texts = new Dictionary<string, StringBuilder>();
        if (!Directory.Exists(LocalizationSetup.SourceFolder)) return new Dictionary<string, string>();
        foreach (string file in Directory.GetFiles(LocalizationSetup.SourceFolder, "*.csv"))
        {
            L10nCsv.Sheet sheet;
            try { sheet = L10nCsv.ReadSheet(File.ReadAllText(file, Encoding.UTF8)); }
            catch (Exception) { continue; }
            for (int column = 0; column < sheet.Header.Count; column++)
            {
                string code = sheet.Header[column].Trim();
                if (code.Length == 0 || code.Equals("Key", StringComparison.OrdinalIgnoreCase) || code.Equals("Context", StringComparison.OrdinalIgnoreCase)) continue;
                if (!texts.TryGetValue(code, out StringBuilder text)) texts[code] = text = new StringBuilder();
                foreach (List<string> row in sheet.Rows) text.Append(L10nCsv.Sheet.Cell(row, column)).Append('\n');
            }
        }
        return texts.ToDictionary(p => p.Key, p => p.Value.ToString());
    }

    static IEnumerable<int> Codepoints(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                yield return char.ConvertToUtf32(text[i], text[i + 1]);
                i++;
            }
            else yield return text[i];
        }
    }

    static string Sample(List<int> codepoints)
    {
        IEnumerable<string> shown = codepoints.Take(40).Select(c => char.ConvertFromUtf32(c) + " U+" + c.ToString("X4"));
        return string.Join(" ", shown) + (codepoints.Count > 40 ? " …" : "");
    }

    static string OnOff(bool value) => value ? "on" : "off";

    /// <summary>Characters mapped by a TrueType/OpenType font's 'cmap' table (formats 4 and 12), read from the file.</summary>
    static HashSet<int> ReadCmap(string path)
    {
        if (s_Cmaps.TryGetValue(path, out HashSet<int> cached)) return cached;
        var set = new HashSet<int>();
        try
        {
            byte[] d = File.ReadAllBytes(path);
            int U16(int o) => o + 1 < d.Length ? (d[o] << 8) | d[o + 1] : 0;
            int S16(int o) => (short)U16(o);
            long U32(int o) => o + 3 < d.Length ? ((long)d[o] << 24) | ((long)d[o + 1] << 16) | ((long)d[o + 2] << 8) | d[o + 3] : 0;

            int cmap = -1;
            int tableCount = U16(4);
            for (int i = 0; i < tableCount; i++)
            {
                int record = 12 + 16 * i;
                if (record + 16 > d.Length) break;
                if (Encoding.ASCII.GetString(d, record, 4) == "cmap") cmap = (int)U32(record + 8);
            }
            if (cmap >= 0)
            {
                int subtables = U16(cmap + 2);
                for (int i = 0; i < subtables; i++)
                {
                    int offset = cmap + (int)U32(cmap + 4 + 8 * i + 4);
                    int format = U16(offset);
                    if (format == 4)
                    {
                        int segX2 = U16(offset + 6);
                        int segments = segX2 / 2;
                        int ends = offset + 14, starts = ends + segX2 + 2, deltas = starts + segX2, ranges = deltas + segX2;
                        for (int s = 0; s < segments; s++)
                        {
                            int end = U16(ends + 2 * s), start = U16(starts + 2 * s), delta = S16(deltas + 2 * s), rangeOffset = U16(ranges + 2 * s);
                            for (int c = start; c <= end; c++)
                            {
                                if (c == 0xFFFF) continue;
                                int glyph;
                                if (rangeOffset == 0) glyph = (c + delta) & 0xFFFF;
                                else
                                {
                                    glyph = U16(ranges + 2 * s + rangeOffset + 2 * (c - start));
                                    if (glyph != 0) glyph = (glyph + delta) & 0xFFFF;
                                }
                                if (glyph != 0) set.Add(c);
                            }
                        }
                    }
                    else if (format == 12)
                    {
                        long groups = U32(offset + 12);
                        for (long g = 0; g < groups; g++)
                        {
                            int group = offset + 16 + (int)(12 * g);
                            long first = U32(group), last = U32(group + 4);
                            for (long c = first; c <= last && c < 0x110000; c++) set.Add((int)c);
                        }
                    }
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[L10n] Couldn't read the character map of " + path + ": " + e.Message);
        }
        s_Cmaps[path] = set;
        return set;
    }
}
