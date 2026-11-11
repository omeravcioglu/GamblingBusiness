using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Localization.Pseudo;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.SmartFormat;
using UnityEngine.ResourceManagement.AsyncOperations;
using Locale = UnityEngine.Localization.Locale;

/// <summary>
/// The game's way into Unity Localization, safe to call at any time (Edit mode, before the package is set up, during
/// scene loads):
/// - Get("Status", "status.memo.nice") and Get("Status", "status.memo.passed", ("amount", money)): the current
///   language's String Table entry, Smart String placeholders filled in. An empty (untranslated) entry shows English
///   (every language falls back to en). A key that isn't in the tables shows its English from the CSV in the Editor, or
///   "⟦table/key⟧" when the CSV doesn't have it either; a player shows the plain key.
/// - CurrentCode ("en", "zh-Hans", "ja", "ko", "tr", or "pseudo"), Languages (code and native name),
///   SetLanguage(code) (the package's PlayerPrefLocaleSelector remembers the choice) and LanguageChanged.
/// - MainFont: the current language's TMP font from the "Fonts" Asset Table (ComingSoon SDF when it can't be loaded).
///   ResolveFont(designFont) keeps a text's own font while the language uses the English font (English, Pseudo).
/// - ToUpper / ToLower / Culture: the current language's casing and number rules (Turkish i → İ, I → ı). The game's
///   global culture stays InvariantCulture (UIManager).
/// The texts live in Assets/Localization/Source/*.csv; see Assets/Localization/README.md.
/// </summary>
public static class L10n
{
    public const string English = "en";
    /// <summary>Code of the pseudo-locale (accented, ~30% longer, in brackets; reads the English tables).</summary>
    public const string PseudoCode = "pseudo";
    /// <summary>The Asset Table collection with one font per language, and its key.</summary>
    public const string FontsTable = "Fonts";
    public const string MainFontKey = "MainFont";
    /// <summary>The English design font (the UI session's asset, Assets/UI/Fonts/ComingSoon SDF.asset).</summary>
    public const string DefaultFontName = "ComingSoon SDF";
#if UNITY_EDITOR
    public const string DefaultFontPath = "Assets/UI/Fonts/ComingSoon SDF.asset";
    public const string SourceFolder = "Assets/Localization/Source";
#endif

    /// <summary>A language the game offers: its locale code and its name written in itself.</summary>
    public readonly struct Language
    {
        public readonly string Code;
        public readonly string NativeName;

        public Language(string code, string nativeName)
        {
            Code = code;
            NativeName = nativeName;
        }

        public override string ToString() => NativeName + " (" + Code + ")";
    }

    static readonly Language[] s_Languages =
    {
        new Language("en", "English"),
        new Language("zh-Hans", "简体中文"),
        new Language("ja", "日本語"),
        new Language("ko", "한국어"),
        new Language("tr", "Türkçe")
    };

    /// <summary>The languages for a language menu, in menu order (the pseudo-locale is a debug option, not listed).</summary>
    public static IReadOnlyList<Language> Languages => s_Languages;

    static Action s_LanguageChanged;
    static LocalizationSettings s_HookedSettings;
    static TMP_FontAsset s_MainFont;
    static string s_MainFontCode;
    static TMP_FontAsset s_DefaultFont;
    static AsyncOperationHandle<TMP_FontAsset> s_FontHandle;
    static bool s_HasFontHandle;
    static CultureInfo s_Culture;
    static string s_CultureCode;
    static readonly HashSet<string> s_Warned = new HashSet<string>();
    // Tables the current language doesn't have: not asked again (each failed load logs an error) until the language
    // changes or ClearCaches runs (the CSV importer calls it)
    static readonly HashSet<string> s_MissingTables = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// The language changed (LocalizationSettings.SelectedLocaleChanged): set your texts and fonts again. Raised after the
    /// new language's tables are loaded, so Get and MainFont already answer in it.
    /// </summary>
    public static event Action LanguageChanged
    {
        add
        {
            Hook();
            s_LanguageChanged += value;
        }
        remove => s_LanguageChanged -= value;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        // Entering Play mode without a domain reload keeps statics: start clean (a font handle belongs to the old session)
        s_LanguageChanged = null;
        if (s_HookedSettings != null)
        {
            try { s_HookedSettings.OnSelectedLocaleChanged -= OnSelectedLocaleChanged; }
            catch (Exception) { }
        }
        s_HookedSettings = null;
        s_FontHandle = default;
        s_HasFontHandle = false;
        s_MainFont = null;
        s_MainFontCode = null;
        s_DefaultFont = null;
        s_Culture = null;
        s_CultureCode = null;
        s_Warned.Clear();
        s_MissingTables.Clear();
    }

    /// <summary>Forget cached lookups (missing tables, the Editor's CSV copies, logged warnings); the CSV importer calls it.</summary>
    public static void ClearCaches()
    {
        s_MissingTables.Clear();
        s_Warned.Clear();
#if UNITY_EDITOR
        s_CsvEnglish.Clear();
#endif
    }

    // ------------------------------------------------------------------
    // Strings
    // ------------------------------------------------------------------

    /// <summary>The entry <paramref name="key"/> of String Table <paramref name="table"/> in the current language.</summary>
    public static string Get(string table, string key)
    {
        return Format(table, key, null);
    }

    /// <summary>
    /// The entry with Smart String arguments: positional ({0}, {1}) or one dictionary / object for named placeholders
    /// (see Args). Prefer the named overload below.
    /// </summary>
    public static string Get(string table, string key, params object[] args)
    {
        return Format(table, key, args != null && args.Length > 0 ? args : null);
    }

    /// <summary>The entry with named Smart String arguments: Get("Heat", "heat.chip.label", ("level", name), ("heat", "72")).</summary>
    public static string Get(string table, string key, params (string name, object value)[] args)
    {
        return Format(table, key, args != null && args.Length > 0 ? new object[] { Args(args) } : null);
    }

    /// <summary>
    /// For content whose English lives in game data (risk cards, hiring tasks, majors, skills, traits): the entry in the
    /// current language, or <paramref name="english"/> when the tables have no such key (never a loud "missing" marker:
    /// new or edited content simply shows its English until it is translated). Named placeholders fill either text.
    /// </summary>
    public static string GetOr(string table, string key, string english, params (string name, object value)[] args)
    {
        IList<object> arguments = args != null && args.Length > 0 ? new object[] { Args(args) } : null;
        if (!string.IsNullOrEmpty(table) && !string.IsNullOrEmpty(key))
        {
            Locale locale = CurrentLocale();
            if (locale != null && !s_MissingTables.Contains(table))
            {
                string text = FromTables(table, key, arguments, locale);
                if (text != null) return text;
            }
        }
        return english == null ? "" : FormatLoose(english, arguments);
    }

    /// <summary>
    /// A stable key part made from English text, the same everywhere: lower case, letters and digits, words joined with
    /// "_", accents dropped ("Viral Product!" → "viral_product", "Exposé" → "expose"); "" for text without letters/digits.
    /// Content keys use it ("event.small_good." + Slug(title)), so an edited English text gets a new key (and shows its
    /// English until translated).
    /// </summary>
    public static string Slug(string english)
    {
        if (string.IsNullOrEmpty(english)) return "";
        string decomposed = english.Normalize(NormalizationForm.FormD);
        var slug = new StringBuilder(decomposed.Length);
        bool pendingSeparator = false;
        foreach (char c in decomposed)
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark) continue;   // accents after FormD
            char lower = char.ToLowerInvariant(c);
            bool keep = (lower >= 'a' && lower <= 'z') || (lower >= '0' && lower <= '9');
            if (!keep)
            {
                pendingSeparator = slug.Length > 0;
                continue;
            }
            if (pendingSeparator) slug.Append('_');
            pendingSeparator = false;
            slug.Append(lower);
        }
        return slug.ToString();
    }

    /// <summary>Named arguments as one Smart String argument: Args(("company", name), ("tier", tier)).</summary>
    public static Dictionary<string, object> Args(params (string name, object value)[] pairs)
    {
        var dictionary = new Dictionary<string, object>(StringComparer.Ordinal);
        if (pairs == null) return dictionary;
        foreach ((string name, object value) in pairs)
        {
            if (!string.IsNullOrEmpty(name))
                dictionary[name] = value ?? "";
        }
        return dictionary;
    }

    static string Format(string table, string key, IList<object> args)
    {
        if (string.IsNullOrEmpty(table) || string.IsNullOrEmpty(key)) return "";
        Locale locale = CurrentLocale();
        if (locale != null && !s_MissingTables.Contains(table))
        {
            string text = FromTables(table, key, args, locale);
            if (text != null) return text;
        }
        return Missing(table, key, args, locale != null);
    }

    /// <summary>The formatted entry, English when the language's own text can't be formatted; null when the key has no text at all.</summary>
    static string FromTables(string table, string key, IList<object> args, Locale locale)
    {
        IList<object> arguments = args ?? Array.Empty<object>();
        string raw = null;
        try
        {
            LocalizedStringDatabase database = LocalizationSettings.StringDatabase;
            var found = database.GetTableEntry(table, key, locale, FallbackBehavior.UseFallback);
            if (found.Table == null)
            {
                s_MissingTables.Add(table);
                LogOnce("table:" + table, "[L10n] There is no String Table \"" + table + "\" for " + locale + " (Tools > Game > Localization > Import CSVs).");
                return null;
            }
            if (found.Entry == null || string.IsNullOrEmpty(found.Entry.Value)) return null;
            raw = found.Entry.Value;

            string text = database.GetLocalizedString(table, key, arguments, locale, FallbackBehavior.UseFallback);
            if (!string.IsNullOrEmpty(text)) return text;
            LogOnce("format:" + table + "/" + key + "/" + locale.Identifier.Code,
                    "[L10n] " + table + "/" + key + " couldn't be formatted in " + locale + " (check its placeholders).");
        }
        catch (Exception e)
        {
            LogOnce("error:" + table + "/" + key + "/" + locale.Identifier.Code, "[L10n] " + table + "/" + key + " in " + locale + ": " + e.Message);
        }

        // A translation whose placeholders don't format: English before giving up
        try
        {
            Locale english = LocalizationSettings.AvailableLocales?.GetLocale(English);
            if (english != null && english != locale)
            {
                string text = LocalizationSettings.StringDatabase.GetLocalizedString(table, key, arguments, english, FallbackBehavior.UseFallback);
                if (!string.IsNullOrEmpty(text)) return text;
            }
        }
        catch (Exception)
        {
            // Logged above
        }
        return raw;
    }

    /// <summary>A key the tables don't have (or no Localization Settings yet).</summary>
    static string Missing(string table, string key, IList<object> args, bool tablesReady)
    {
#if UNITY_EDITOR
        string english = EditorCsvEnglish(table, key);
        if (english != null)
        {
            if (tablesReady)
                LogOnce("csv:" + table + "/" + key, "[L10n] " + table + "/" + key + " is in the CSV but not in the tables yet: showing the CSV's English " +
                        "(Tools > Game > Localization > Import CSVs).");
            return FormatLoose(english, args);
        }
        LogOnce("missing:" + table + "/" + key, "[L10n] Missing key " + table + "/" + key + " (not in the String Tables or " + SourceFolder + "/" + table + ".csv).");
        return "⟦" + table + "/" + key + "⟧";
#else
        LogOnce("missing:" + table + "/" + key, "[L10n] Missing key " + table + "/" + key);
        return key;
#endif
    }

    /// <summary>Smart-format text that isn't in a table (the Editor's CSV fallback); the raw text when it doesn't format.</summary>
    static string FormatLoose(string text, IList<object> args)
    {
        if (args == null || args.Count == 0 || text.IndexOf('{') < 0) return text;
        try
        {
            var array = new object[args.Count];
            args.CopyTo(array, 0);
            return Smart.Format(Culture, text, array);
        }
        catch (Exception)
        {
            return text;
        }
    }

#if UNITY_EDITOR
    static readonly Dictionary<string, KeyValuePair<DateTime, Dictionary<string, string>>> s_CsvEnglish =
        new Dictionary<string, KeyValuePair<DateTime, Dictionary<string, string>>>();

    /// <summary>The en cell of <paramref name="key"/> in Assets/Localization/Source/&lt;table&gt;.csv (Editor only), or null.</summary>
    static string EditorCsvEnglish(string table, string key)
    {
        try
        {
            string path = SourceFolder + "/" + table + ".csv";
            if (!System.IO.File.Exists(path)) return null;
            DateTime stamp = System.IO.File.GetLastWriteTimeUtc(path);
            if (!s_CsvEnglish.TryGetValue(table, out var cached) || cached.Key != stamp)
            {
                var english = new Dictionary<string, string>(StringComparer.Ordinal);
                L10nCsv.Sheet sheet = L10nCsv.ReadSheet(System.IO.File.ReadAllText(path, Encoding.UTF8));
                int keyColumn = sheet.Column("Key");
                int enColumn = sheet.Column(English);
                if (keyColumn >= 0 && enColumn >= 0)
                {
                    foreach (List<string> row in sheet.Rows)
                    {
                        string rowKey = L10nCsv.Sheet.Cell(row, keyColumn).Trim();
                        if (rowKey.Length > 0 && !english.ContainsKey(rowKey))
                            english[rowKey] = L10nCsv.NormalizeNewlines(L10nCsv.Sheet.Cell(row, enColumn));
                    }
                }
                cached = new KeyValuePair<DateTime, Dictionary<string, string>>(stamp, english);
                s_CsvEnglish[table] = cached;
            }
            return cached.Value.TryGetValue(key, out string value) && !string.IsNullOrEmpty(value) ? value : null;
        }
        catch (Exception e)
        {
            LogOnce("csvread:" + table, "[L10n] Couldn't read " + table + ".csv: " + e.Message);
            return null;
        }
    }
#endif

    // ------------------------------------------------------------------
    // Language
    // ------------------------------------------------------------------

    /// <summary>True once Localization Settings exist and a language can be picked.</summary>
    public static bool IsReady => CurrentLocale() != null;

    /// <summary>The current language: "en", "zh-Hans", "ja", "ko", "tr", or "pseudo" ("en" before the package is ready).</summary>
    public static string CurrentCode
    {
        get
        {
            Locale locale = CurrentLocale();
            if (locale == null) return English;
            if (locale is PseudoLocale) return PseudoCode;
            string code = locale.Identifier.Code;
            return string.IsNullOrEmpty(code) ? English : code;
        }
    }

    /// <summary>The native name of a language code ("日本語"), "Pseudo" for the pseudo-locale, else the code itself.</summary>
    public static string NativeName(string code)
    {
        foreach (Language language in s_Languages)
        {
            if (language.Code == code) return language.NativeName;
        }
        return code == PseudoCode ? "Pseudo" : code;
    }

    /// <summary>
    /// Switch the game to a language ("en", "zh-Hans", "ja", "ko", "tr" or "pseudo") through LocalizationSettings.SelectedLocale;
    /// the PlayerPrefLocaleSelector remembers it for the next start. False when there is no such language.
    /// </summary>
    public static bool SetLanguage(string code)
    {
        if (string.IsNullOrEmpty(code)) return false;
        if (!Hook())
        {
            Debug.LogWarning("[L10n] There are no Localization Settings yet: run Tools > Game > Localization > Set Up.");
            return false;
        }
        try
        {
            Locale target = FindLocale(code);
            if (target == null)
            {
                Debug.LogWarning("[L10n] The Localization Settings have no language '" + code + "'.");
                return false;
            }
            LocalizationSettings.SelectedLocale = target;
            return true;
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            return false;
        }
    }

    static Locale FindLocale(string code)
    {
        ILocalesProvider provider = LocalizationSettings.AvailableLocales;
        if (provider == null) return null;
        if (code == PseudoCode)
        {
            foreach (Locale locale in provider.Locales)
            {
                if (locale is PseudoLocale) return locale;
            }
            return null;
        }
        return provider.GetLocale(code);
    }

    /// <summary>The selected locale, else English (Edit mode before one is picked); null without Localization Settings.</summary>
    static Locale CurrentLocale()
    {
        if (!Hook()) return null;
        try
        {
            Locale selected = LocalizationSettings.SelectedLocale;
            if (selected != null) return selected;
            Locale project = LocalizationSettings.ProjectLocale;
            return project != null ? project : LocalizationSettings.AvailableLocales?.GetLocale(English);
        }
        catch (Exception e)
        {
            LogOnce("locale", "[L10n] Couldn't read the selected language: " + e.Message);
            return null;
        }
    }

    /// <summary>Listen to the package once Localization Settings exist; true when they do.</summary>
    static bool Hook()
    {
        LocalizationSettings settings;
        try
        {
            if (!LocalizationSettings.HasSettings) return false;
            settings = LocalizationSettings.Instance;
        }
        catch (Exception e)
        {
            LogOnce("hook", "[L10n] Localization isn't available: " + e.Message);
            return false;
        }
        if (settings == null) return false;
        if (!ReferenceEquals(settings, s_HookedSettings))
        {
            if (s_HookedSettings != null)
            {
                try { s_HookedSettings.OnSelectedLocaleChanged -= OnSelectedLocaleChanged; }
                catch (Exception) { }
            }
            settings.OnSelectedLocaleChanged += OnSelectedLocaleChanged;
            s_HookedSettings = settings;
        }
        return true;
    }

    static void OnSelectedLocaleChanged(Locale locale)
    {
        // The old font stays loaded until every text on it has switched: a text on a released font would show nothing
        AsyncOperationHandle<TMP_FontAsset> oldFont = s_FontHandle;
        bool hadFont = s_HasFontHandle;
        s_FontHandle = default;
        s_HasFontHandle = false;
        s_MainFont = null;
        s_MainFontCode = null;
        s_Culture = null;
        s_CultureCode = null;
        s_Warned.Clear();
        s_MissingTables.Clear();

        // The PlayerPrefLocaleSelector has stored the new choice; write it now rather than at quit
        if (Application.isPlaying)
        {
            try { PlayerPrefs.Save(); }
            catch (Exception) { }
        }

        Action handlers = s_LanguageChanged;
        if (handlers != null)
        {
            foreach (Action handler in handlers.GetInvocationList())
            {
                // One broken listener mustn't keep the others on the old language
                try { ((Action)handler)(); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        if (hadFont) ReleaseFont(oldFont);
    }

    // ------------------------------------------------------------------
    // Fonts
    // ------------------------------------------------------------------

    /// <summary>
    /// The current language's font: the "Fonts" Asset Table's MainFont entry (Coming Soon, Patrick Hand, LXGW WenKai,
    /// Klee One or Gaegu), loaded synchronously and cached; ComingSoon SDF when the table can't be read.
    /// </summary>
    public static TMP_FontAsset MainFont
    {
        get
        {
            string code = CurrentCode;
            if (s_MainFont != null && s_MainFontCode == code) return s_MainFont;
            TMP_FontAsset font = LoadTableFont(CurrentLocale());
            s_MainFont = font != null ? font : DefaultFont;
            s_MainFontCode = code;
            return s_MainFont;
        }
    }

    /// <summary>
    /// The font a text should use now: its <paramref name="designFont"/> while the current language uses the English
    /// font (English and Pseudo: every text stays exactly as designed, material included) or that same face; otherwise
    /// MainFont (Patrick Hand, LXGW WenKai, Klee One or Gaegu).
    /// </summary>
    public static TMP_FontAsset ResolveFont(TMP_FontAsset designFont)
    {
        TMP_FontAsset main = MainFont;
        if (designFont == null) return main;
        if (main == null || SameFace(main, designFont) || SameFace(main, DefaultFont)) return designFont;
        return main;
    }

    // In a player the Fonts table's copy of a font is a different object from the one scenes use: compare by name too
    static bool SameFace(TMP_FontAsset a, TMP_FontAsset b)
    {
        return a != null && b != null && (a == b || a.name == b.name);
    }

    /// <summary>ComingSoon SDF (the English design font); TextMesh Pro's default font if it isn't loaded.</summary>
    public static TMP_FontAsset DefaultFont
    {
        get
        {
            if (s_DefaultFont != null) return s_DefaultFont;
#if UNITY_EDITOR
            s_DefaultFont = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(DefaultFontPath);
            if (s_DefaultFont != null) return s_DefaultFont;
#endif
            foreach (TMP_FontAsset font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            {
                if (font != null && font.name == DefaultFontName)
                {
                    s_DefaultFont = font;
                    return font;
                }
            }
            // Not cached: Coming Soon may still be loaded by a scene later
            return TMP_Settings.instance != null ? TMP_Settings.defaultFontAsset : null;
        }
    }

    static TMP_FontAsset LoadTableFont(Locale locale)
    {
        if (locale == null) return null;
        try
        {
            AsyncOperationHandle<TMP_FontAsset> handle =
                LocalizationSettings.AssetDatabase.GetLocalizedAssetAsync<TMP_FontAsset>(FontsTable, MainFontKey, locale, FallbackBehavior.UseFallback);
            if (!handle.IsValid()) return null;
            TMP_FontAsset font = handle.IsDone ? handle.Result : handle.WaitForCompletion();
            if (handle.Status != AsyncOperationStatus.Succeeded || font == null)
            {
                LogOnce("font:" + locale.Identifier.Code, "[L10n] The Fonts table has no MainFont for " + locale + ": using " + DefaultFontName + ".");
                if (!Application.isPlaying) ReleaseFont(handle);   // Edit mode doesn't auto-release it
                return null;
            }

            // In Play mode the package releases this handle next frame: keep a reference of our own until the language
            // changes and every text has moved to the next font
            if (Application.isPlaying) handle = Addressables.ResourceManager.Acquire(handle);
            if (s_HasFontHandle) ReleaseFont(s_FontHandle);
            s_FontHandle = handle;
            s_HasFontHandle = true;
            return font;
        }
        catch (Exception e)
        {
            LogOnce("fonterror:" + locale.Identifier.Code, "[L10n] Couldn't load the MainFont of " + locale + ": " + e.Message);
            return null;
        }
    }

    static void ReleaseFont(AsyncOperationHandle<TMP_FontAsset> handle)
    {
        try
        {
            if (handle.IsValid()) Addressables.Release(handle);
        }
        catch (Exception)
        {
            // Already released (e.g. Addressables was reset)
        }
    }

    // ------------------------------------------------------------------
    // Casing and culture
    // ------------------------------------------------------------------

    /// <summary>
    /// Formatting rules of the current language (tr-TR, zh-CN, ja-JP, ko-KR, en-US) for anything culture-specific, e.g.
    /// decimals: 1.5 in English, 1,5 in Turkish. Money stays UIFormat's "$1,234,567" in every language. Never assign it
    /// to CultureInfo.CurrentCulture (the game parses with InvariantCulture).
    /// </summary>
    public static CultureInfo Culture
    {
        get
        {
            string code = CurrentCode;
            if (s_Culture == null || s_CultureCode != code)
            {
                s_Culture = CultureFor(code);
                s_CultureCode = code;
            }
            return s_Culture;
        }
    }

    /// <summary>True while the language is Chinese, Japanese or Korean (no letter case; lines break between characters).</summary>
    public static bool IsCjk => IsCjkCode(CurrentCode);

    /// <summary>
    /// Upper case by the current language's rules, rich-text tags left as they are: Turkish "istanbul" → "İSTANBUL",
    /// "ılık" → "ILIK". CJK characters have no case and come back unchanged. Prefer authoring text in its final case;
    /// use this only when that can't be done (player-typed names, a style applied to changing text).
    /// </summary>
    public static string ToUpper(string text) => ChangeCase(text, true);

    /// <summary>Lower case by the current language's rules (Turkish "I" → "ı", "İ" → "i"), rich-text tags left as they are.</summary>
    public static string ToLower(string text) => ChangeCase(text, false);

    static string ChangeCase(string text, bool upper)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        string code = CurrentCode;
        TextInfo info = IsCjkCode(code) ? CultureInfo.InvariantCulture.TextInfo : Culture.TextInfo;
        if (text.IndexOf('<') < 0) return upper ? info.ToUpper(text) : info.ToLower(text);

        // Keep tags such as <link="hl"> or <color=#1d2433> exactly as written
        var result = new StringBuilder(text.Length);
        int i = 0;
        while (i < text.Length)
        {
            int open = text.IndexOf('<', i);
            int close = open >= 0 ? text.IndexOf('>', open + 1) : -1;
            if (open < 0 || close < 0)
            {
                string rest = text.Substring(i);
                result.Append(upper ? info.ToUpper(rest) : info.ToLower(rest));
                break;
            }
            if (open > i)
            {
                string part = text.Substring(i, open - i);
                result.Append(upper ? info.ToUpper(part) : info.ToLower(part));
            }
            result.Append(text, open, close - open + 1);
            i = close + 1;
        }
        return result.ToString();
    }

    static bool IsCjkCode(string code)
    {
        return code != null && (code.StartsWith("zh", StringComparison.Ordinal) || code == "ja" || code == "ko");
    }

    static CultureInfo CultureFor(string code)
    {
        string name;
        switch (code)
        {
            case "tr": name = "tr-TR"; break;
            case "zh-Hans": name = "zh-CN"; break;
            case "ja": name = "ja-JP"; break;
            case "ko": name = "ko-KR"; break;
            case English:
            case PseudoCode: name = "en-US"; break;
            default: name = code; break;
        }
        try
        {
            return CultureInfo.GetCultureInfo(name);
        }
        catch (Exception)
        {
            return CultureInfo.InvariantCulture;
        }
    }

    // ------------------------------------------------------------------

    static void LogOnce(string id, string message)
    {
        if (!s_Warned.Add(id)) return;
        if (Debug.isDebugBuild) Debug.LogWarning(message);
    }
}
