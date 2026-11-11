using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Localization.Settings;

/// <summary>
/// The manager's texts ("Manager" String Table, Assets/Localization/Source/Manager.csv) through L10n, plus variant
/// lookups: a key with a personality suffix ("mgr.s.raise_request.msg.aggressive") is used when the table has it, else
/// the base key. <see cref="Has"/> asks the String Table (and, in the Editor before the CSV is imported, the CSV itself).
/// </summary>
public static class ManagerText
{
    public const string Table = ManagerValidator.Table;

    static readonly Dictionary<string, bool> known = new Dictionary<string, bool>(StringComparer.Ordinal);
    static string knownLanguage;
    static bool hooked;
#if UNITY_EDITOR
    static HashSet<string> csvKeys;
    static DateTime csvStamp;
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        known.Clear();
        knownLanguage = null;
        hooked = false;
    }

    public static string Get(string key) => L10n.Get(Table, key);

    public static string Get(string key, params (string name, object value)[] args) => L10n.Get(Table, key, args);

    /// <summary>The entry with a ready dictionary of named arguments (a decision's placeholders).</summary>
    public static string Get(string key, Dictionary<string, object> args)
    {
        if (args == null || args.Count == 0) return L10n.Get(Table, key);
        return L10n.Get(Table, key, new object[] { args });
    }

    /// <summary>True when the Manager table (or, in the Editor, Manager.csv) has this key with a text.</summary>
    public static bool Has(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        if (!hooked)
        {
            hooked = true;
            L10n.LanguageChanged += known.Clear;
        }
        string language = L10n.CurrentCode;
        if (knownLanguage != language)
        {
            known.Clear();
            knownLanguage = language;
        }
        if (known.TryGetValue(key, out bool has)) return has;
        has = TableHas(key);
#if UNITY_EDITOR
        if (!has) has = CsvHas(key);
#endif
        known[key] = has;
        return has;
    }

    static bool TableHas(string key)
    {
        try
        {
            if (!L10n.IsReady) return false;
            var locale = LocalizationSettings.SelectedLocale;
            if (locale == null) return false;
            var found = LocalizationSettings.StringDatabase.GetTableEntry(Table, key, locale, FallbackBehavior.UseFallback);
            return found.Entry != null && !string.IsNullOrEmpty(found.Entry.Value);
        }
        catch (Exception)
        {
            return false;
        }
    }

#if UNITY_EDITOR
    static bool CsvHas(string key)
    {
        try
        {
            string path = L10n.SourceFolder + "/" + Table + ".csv";
            if (!System.IO.File.Exists(path)) return false;
            DateTime stamp = System.IO.File.GetLastWriteTimeUtc(path);
            if (csvKeys == null || stamp != csvStamp)
            {
                csvStamp = stamp;
                csvKeys = new HashSet<string>(StringComparer.Ordinal);
                L10nCsv.Sheet sheet = L10nCsv.ReadSheet(System.IO.File.ReadAllText(path, System.Text.Encoding.UTF8));
                foreach (List<string> row in sheet.Rows)
                {
                    string k = L10nCsv.Sheet.Cell(row, 0).Trim();
                    if (k.Length > 0 && L10nCsv.Sheet.Cell(row, 2).Trim().Length > 0) csvKeys.Add(k);
                }
            }
            return csvKeys.Contains(key);
        }
        catch (Exception)
        {
            return false;
        }
    }
#endif

    /// <summary>The personality variant of a key when the table has one, else the key.</summary>
    public static string Variant(string key, string personality)
    {
        if (!string.IsNullOrEmpty(personality) && Has(key + "." + personality)) return key + "." + personality;
        return key;
    }

    // ------------------------------------------------------------------
    // Formatting
    // ------------------------------------------------------------------

    /// <summary>A param value as text: $1,234 (money), 12 (int), or the number as written (up to 2 decimals).</summary>
    public static string FormatValue(double value, string format)
    {
        switch (format)
        {
            case ManagerParams.FormatMoney: return UIFormat.Money(value);
            case ManagerParams.FormatInt: return Math.Round(value).ToString("N0", CultureInfo.InvariantCulture);
            default:
                double rounded = Math.Round(value, 2);
                return rounded == Math.Floor(rounded)
                    ? rounded.ToString("0", CultureInfo.InvariantCulture)
                    : rounded.ToString("0.##", L10n.Culture);
        }
    }

    /// <summary>"$1,200/yr"-style money without the unit (the texts write the unit).</summary>
    public static string Money(double value) => UIFormat.Money(value);

    /// <summary>A personality's display name ("Conservative").</summary>
    public static string PersonalityName(string personality) => Get("mgr.pers." + SafeId(personality) + ".name");

    /// <summary>A personality's one-line style description.</summary>
    public static string PersonalityStyle(string personality) => Get("mgr.pers." + SafeId(personality) + ".style");

    /// <summary>One of a personality's three bio lines.</summary>
    public static string PersonalityBio(string personality, int bio, Dictionary<string, object> args)
    {
        int n = Mathf.Clamp(bio, 1, 3);
        return Get("mgr.pers." + SafeId(personality) + ".bio." + n, args);
    }

    static string SafeId(string personality) => ManagerSchema.IsPersonality(personality) ? personality : ManagerSchema.Conservative;

    /// <summary>The satisfaction band's word ("content", "restless"...).</summary>
    public static string SatisfactionWord(float satisfaction) => Get("mgr.sat." + SatisfactionBand(satisfaction));

    /// <summary>happy (70+), fine (50+), restless (35+), unhappy (20+), leaving (below 20).</summary>
    public static string SatisfactionBand(float satisfaction)
    {
        if (satisfaction >= 70f) return "happy";
        if (satisfaction >= 50f) return "fine";
        if (satisfaction >= 35f) return "restless";
        if (satisfaction >= 20f) return "unhappy";
        return "leaving";
    }

    public static Color SatisfactionColor(float satisfaction)
    {
        if (satisfaction >= 50f) return UIColors.Money;
        if (satisfaction >= 35f) return UIColors.Warn;
        return UIColors.Loss;
    }
}
