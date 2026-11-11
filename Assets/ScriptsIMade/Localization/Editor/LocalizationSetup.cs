using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Metadata;
using UnityEngine.Localization.Pseudo;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.TextCore.LowLevel;
using Object = UnityEngine.Object;

/// <summary>
/// Sets up Unity Localization for the game. Idempotent: it creates what is missing, repairs what is wrong and leaves
/// the rest alone, then writes one summary block to the Console (and Editor.log).
/// - Tools > Game > Localization > Set Up runs everything. It also runs by itself after a script reload when anything
///   is missing (never in Play mode or batch mode; checked again on returning to Edit mode), and otherwise imports
///   the CSVs whose contents changed.
/// - Creates: Assets/Localization/LocalizationSettings.asset (active; initializes synchronously; preloads the selected
///   language and its fallback; starts in the player's saved language, else the system language, else English), the
///   locales en, zh-Hans, ja, ko, tr (each falling back to en) and a pseudo-locale, the Addressables settings (content
///   built with the player), dynamic multi-atlas TMP fonts in Assets/Localization/Fonts (PatrickHand, LXGWWenKai,
///   KleeOne, Gaegu; LXGWWenKai is also TMP's global last-resort fallback), the "Fonts" Asset Table (MainFont per
///   language) and one String Table collection per CSV in Assets/Localization/Source (LocalizationCsvImporter).
/// </summary>
[InitializeOnLoad]
public static class LocalizationSetup
{
    public const string Root = "Assets/Localization";
    public const string SettingsPath = Root + "/LocalizationSettings.asset";
    public const string LocalesFolder = Root + "/Locales";
    public const string TablesFolder = Root + "/Tables";
    public const string FontsFolder = Root + "/Fonts";
    public const string SourceFolder = Root + "/Source";
    public const string ReportsFolder = Root + "/Reports";
    public const string PlayerPrefKey = "selected-locale";

    /// <summary>
    /// Start in the system language when the player never picked one. Off while the translations are unfinished (a
    /// Turkish or CJK system would otherwise open a half-translated game); turn it on once every table is translated.
    /// Changing it re-applies the startup selectors on the next setup run.
    /// </summary>
    public static readonly bool DetectSystemLanguage = false;

    const string MenuRoot = "Tools/Game/Localization/";
    const string AutoRunKey = "L10nSetup.AutoRunSignature";
    const string LiberationFallbackPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF - Fallback.asset";
    const string LiberationFallbackName = "LiberationSans SDF - Fallback";
    const string PseudoFileName = "Pseudo-Locale (pseudo)";

    struct LocaleSpec
    {
        public readonly string Code;
        public readonly string Name;
        public readonly ushort Order;

        public LocaleSpec(string code, string name, ushort order)
        {
            Code = code;
            Name = name;
            Order = order;
        }
    }

    /// <summary>The game's languages; English is the source and every other language's fallback.</summary>
    static readonly LocaleSpec[] LocaleSpecs =
    {
        new LocaleSpec("en", "English (en)", 0),
        new LocaleSpec("zh-Hans", "Chinese (Simplified) (zh-Hans)", 1),
        new LocaleSpec("ja", "Japanese (ja)", 2),
        new LocaleSpec("ko", "Korean (ko)", 3),
        new LocaleSpec("tr", "Turkish (tr)", 4)
    };

    struct FontSpec
    {
        public readonly string Locale;
        public readonly string SourceFile;
        public readonly string Name;
        public readonly int SamplingSize;
        public readonly int Padding;

        public FontSpec(string locale, string sourceFile, string name, int samplingSize, int padding)
        {
            Locale = locale;
            SourceFile = sourceFile;
            Name = name;
            SamplingSize = samplingSize;
            Padding = padding;
        }

        public string AssetPath => FontsFolder + "/" + Name + ".asset";
        public string SourcePath => FontsFolder + "/" + SourceFile;
    }

    // Handwritten per language. Padding / sampling size is 10% everywhere (like Coming Soon's 9 / 90), so strokes weigh
    // the same across a fallback chain; CJK glyphs are dense and sample cleanly at 50. LXGW WenKai comes first: the
    // other fonts fall back to it.
    static readonly FontSpec[] FontSpecs =
    {
        new FontSpec("zh-Hans", "LXGWWenKai-Regular.ttf", "LXGWWenKai SDF", 50, 5),
        new FontSpec("tr", "PatrickHand-Regular.ttf", "PatrickHand SDF", 90, 9),
        new FontSpec("ja", "KleeOne-Regular.ttf", "KleeOne SDF", 50, 5),
        new FontSpec("ko", "Gaegu-Regular.ttf", "Gaegu SDF", 50, 5)
    };

    const string LastResortFont = "LXGWWenKai SDF";
    const int AtlasSize = 1024;

    static int s_Waits;

    static LocalizationSetup()
    {
        if (Application.isBatchMode) return;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        ScheduleAutoRun();
    }

    static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredEditMode) ScheduleAutoRun();
    }

    static void ScheduleAutoRun()
    {
        s_Waits = 0;
        EditorApplication.delayCall -= AutoRun;
        EditorApplication.delayCall += AutoRun;
    }

    static void AutoRun()
    {
        if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            if (++s_Waits < 2000) EditorApplication.delayCall += AutoRun;
            else Debug.LogWarning("[L10n Setup] The Editor stayed busy, so the automatic check was skipped: run Tools > Game > Localization > Set Up.");
            return;
        }

        try
        {
            List<string> missing = FindMissing();
            if (missing.Count == 0)
            {
                LocalizationCsvImporter.ImportChanged("after script reload");
                return;
            }

            // Once per editor session for the same gap: a step that can't succeed shouldn't repeat on every reload
            string signature = string.Join("|", missing);
            if (SessionState.GetString(AutoRunKey, "") == signature)
            {
                LocalizationCsvImporter.ImportChanged("after script reload");
                return;
            }
            SessionState.SetString(AutoRunKey, signature);
            Run(true, missing);
        }
        catch (Exception e)
        {
            Debug.LogError("[L10n Setup] The automatic setup failed: " + e);
        }
    }

    [MenuItem(MenuRoot + "Set Up", false, 1)]
    static void SetUpMenu()
    {
        SessionState.EraseString(AutoRunKey);
        Run(false, null);
    }

    [MenuItem(MenuRoot + "Set Up", true)]
    static bool SetUpMenuValidate() => !EditorApplication.isPlayingOrWillChangePlaymode;

    // ------------------------------------------------------------------
    // What is missing (cheap checks, nothing is created)
    // ------------------------------------------------------------------

    /// <summary>The parts of the setup that are missing or wrong, as short names (empty = all there).</summary>
    public static List<string> FindMissing()
    {
        var missing = new List<string>();
        LocalizationSettings settings = LocalizationEditorSettings.ActiveLocalizationSettings;
        if (settings == null)
        {
            missing.Add("Localization Settings");
            return missing;   // everything else hangs off it
        }
        if (!InitializesSynchronously(settings)) missing.Add("InitializeSynchronously");
        if (!SelectorsAreSet(settings)) missing.Add("startup selectors");

        AddressableAssetSettings addressables = AddressableAssetSettingsDefaultObject.Settings;
        if (addressables == null) missing.Add("Addressables settings");
        else if (addressables.BuildAddressablesWithPlayerBuild != AddressableAssetSettings.PlayerBuildOption.BuildWithPlayer)
            missing.Add("Addressables built with the player");

        Locale english = LocalizationEditorSettings.GetLocale(new LocaleIdentifier(L10n.English));
        foreach (LocaleSpec spec in LocaleSpecs)
        {
            Locale locale = LocalizationEditorSettings.GetLocale(new LocaleIdentifier(spec.Code));
            if (locale == null) missing.Add("locale " + spec.Code);
            else if (spec.Code != L10n.English && !FallsBackTo(locale, english)) missing.Add("fallback " + spec.Code);
        }
        if (LocalizationEditorSettings.GetPseudoLocales().Count == 0) missing.Add("pseudo-locale");

        TMP_FontAsset lastResort = null;
        foreach (FontSpec spec in FontSpecs)
        {
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(spec.AssetPath);
            if (font == null || !HasMaterialOnFile(spec.AssetPath)) missing.Add("font " + spec.Name);
            if (spec.Name == LastResortFont) lastResort = font;
        }
        if (lastResort != null && !GlobalFallbackHas(lastResort)) missing.Add("TMP global fallback");

        AssetTableCollection fonts = FindAssetCollection(L10n.FontsTable);
        if (fonts == null) missing.Add("Fonts table");
        else
        {
            foreach (LocaleSpec spec in LocaleSpecs)
            {
                if (FontsTableGuid(fonts, spec.Code) == null) missing.Add("Fonts/MainFont " + spec.Code);
            }
        }
        return missing;
    }

    // ------------------------------------------------------------------
    // The setup
    // ------------------------------------------------------------------

    /// <summary>Run every step (idempotent) and log one summary block. <paramref name="auto"/>: the automatic run after a reload.</summary>
    public static void Run(bool auto, IList<string> reasons)
    {
        var report = new Report(auto
            ? "Automatic setup after a script reload (missing: " + string.Join(", ", reasons ?? new List<string>()) + ")"
            : "Tools > Game > Localization > Set Up");
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            report.Fail("Play mode", "leave Play mode first");
            report.Log();
            return;
        }

        try
        {
            Step(report, "folders", () => EnsureFolders(report));
            Step(report, "Addressables", () => EnsureAddressables(report));

            LocalizationSettings settings = null;
            Step(report, "Localization Settings", () => settings = EnsureSettings(report));
            if (settings == null)
            {
                report.Fail("setup", "stopped: there are no Localization Settings");
                return;
            }

            Step(report, "locales", () => EnsureLocales(report));
            Step(report, "pseudo-locale", () => EnsurePseudoLocale(report));
            Step(report, "startup selectors", () => EnsureSelectors(settings, report));

            var fonts = new Dictionary<string, TMP_FontAsset>();
            Step(report, "fonts", () => EnsureFonts(fonts, report));
            Step(report, "TMP Settings", () => EnsureTmpSettings(fonts, report));
            Step(report, "Fonts table", () => EnsureFontsTable(fonts, report));
            AssetDatabase.SaveAssets();

            Step(report, "CSV import", () =>
            {
                string summary = LocalizationCsvImporter.Import(!auto, "setup");
                report.Block(string.IsNullOrEmpty(summary) ? "CSVs unchanged since the last import" : summary);
            });

            AssetDatabase.SaveAssets();
            Step(report, "verification", () => Verify(report));
        }
        finally
        {
            report.Log();
        }
    }

    static void Step(Report report, string name, Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            report.Fail(name, e.GetType().Name + ": " + e.Message);
            Debug.LogException(e);
        }
    }

    static void EnsureFolders(Report report)
    {
        foreach (string folder in new[] { Root, LocalesFolder, TablesFolder, FontsFolder, SourceFolder, ReportsFolder })
        {
            bool existed = AssetDatabase.IsValidFolder(folder);
            EnsureFolder(folder);
            if (!existed) report.Line("created", folder);
        }
    }

    static void EnsureAddressables(Report report)
    {
        bool existed = AddressableAssetSettingsDefaultObject.SettingsExists;
        AddressableAssetSettings addressables = AddressableAssetSettingsDefaultObject.GetSettings(true);
        if (addressables == null)
        {
            report.Fail("Addressables settings", "couldn't be created");
            return;
        }
        bool changed = false;
        if (addressables.BuildAddressablesWithPlayerBuild != AddressableAssetSettings.PlayerBuildOption.BuildWithPlayer)
        {
            // Player builds must carry the tables and fonts
            addressables.BuildAddressablesWithPlayerBuild = AddressableAssetSettings.PlayerBuildOption.BuildWithPlayer;
            EditorUtility.SetDirty(addressables);
            changed = true;
        }
        report.Line(existed ? (changed ? "fixed" : "found") : "created", "Addressables settings",
                    AssetDatabase.GetAssetPath(addressables) + "; content is built with the player");
    }

    static LocalizationSettings EnsureSettings(Report report)
    {
        LocalizationSettings settings = LocalizationEditorSettings.ActiveLocalizationSettings;
        string how = "found";
        if (settings == null)
        {
            settings = AssetDatabase.LoadAssetAtPath<LocalizationSettings>(SettingsPath);
            if (settings == null)
            {
                // Another settings asset somewhere in Assets (never one inside a package)
                foreach (string guid in AssetDatabase.FindAssets("t:LocalizationSettings", new[] { "Assets" }))
                {
                    settings = AssetDatabase.LoadAssetAtPath<LocalizationSettings>(AssetDatabase.GUIDToAssetPath(guid));
                    if (settings != null) break;
                }
            }
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<LocalizationSettings>();
                settings.name = Path.GetFileNameWithoutExtension(SettingsPath);
                AssetDatabase.CreateAsset(settings, SettingsPath);
                how = "created";
            }
            LocalizationEditorSettings.ActiveLocalizationSettings = settings;
        }
        // The runtime singleton may still hold an in-memory default made before the asset existed
        LocalizationSettings.Instance = settings;

        var serialized = new SerializedObject(settings);
        bool changed = false;
        SerializedProperty sync = serialized.FindProperty("m_InitializeSynchronously");
        if (sync != null && !sync.boolValue)
        {
            sync.boolValue = true;   // L10n.Get answers from the first frame
            changed = true;
        }
        SerializedProperty preload = serialized.FindProperty("m_PreloadBehavior");
        int wanted = (int)PreloadBehavior.PreloadSelectedLocaleAndFallbacks;
        if (preload != null && preload.enumValueIndex != wanted)
        {
            preload.enumValueIndex = wanted;   // untranslated entries read the en tables: have them loaded too
            changed = true;
        }
        if (changed)
        {
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);
        }
        report.Line(how, "Localization Settings", AssetDatabase.GetAssetPath(settings) + " (active)" +
                    (sync == null ? "; this package version has no InitializeSynchronously" : ""));
        return settings;
    }

    static void EnsureLocales(Report report)
    {
        var locales = new Dictionary<string, Locale>();
        foreach (LocaleSpec spec in LocaleSpecs)
        {
            Locale locale = LocalizationEditorSettings.GetLocale(new LocaleIdentifier(spec.Code)) ?? FindLocaleAsset(spec.Code);
            string how = locale != null ? "found" : "created";
            if (locale == null)
            {
                locale = Locale.CreateLocale(new LocaleIdentifier(spec.Code));
                locale.LocaleName = spec.Name;
                locale.SortOrder = spec.Order;
                locale.name = spec.Name;
                AssetDatabase.CreateAsset(locale, LocalesFolder + "/" + spec.Name + ".asset");
            }
            LocalizationEditorSettings.AddLocale(locale);   // no-op when it is in already
            locales[spec.Code] = locale;
            report.Line(how, "Locale " + spec.Code, AssetDatabase.GetAssetPath(locale));
        }

        // English is every other language's fallback: an empty (untranslated) entry shows the English text
        Locale english = locales[L10n.English];
        foreach (LocaleSpec spec in LocaleSpecs)
        {
            if (spec.Code == L10n.English) continue;
            Locale locale = locales[spec.Code];
            if (FallsBackTo(locale, english)) continue;
            if (locale.Metadata == null) locale.Metadata = new MetadataCollection();
            FallbackLocale fallback = locale.Metadata.GetMetadata<FallbackLocale>();
            if (fallback != null) fallback.Locale = english;
            else locale.Metadata.AddMetadata(new FallbackLocale(english));
            EditorUtility.SetDirty(locale);
            report.Line("set", "Fallback " + spec.Code + " -> en");
        }
    }

    static Locale FindLocaleAsset(string code)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Locale", new[] { "Assets" }))
        {
            var locale = AssetDatabase.LoadAssetAtPath<Locale>(AssetDatabase.GUIDToAssetPath(guid));
            if (locale != null && !(locale is PseudoLocale) && locale.Identifier.Code == code) return locale;
        }
        return null;
    }

    static void EnsurePseudoLocale(Report report)
    {
        PseudoLocale pseudo = LocalizationEditorSettings.GetPseudoLocales().FirstOrDefault();
        if (pseudo == null)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:PseudoLocale", new[] { "Assets" }))
            {
                pseudo = AssetDatabase.LoadAssetAtPath<PseudoLocale>(AssetDatabase.GUIDToAssetPath(guid));
                if (pseudo != null) break;
            }
        }
        string how = pseudo != null ? "found" : "created";
        if (pseudo == null)
        {
            // Reads the English tables (its identifier is "en") and makes the text accented, ~30% longer and bracketed
            pseudo = PseudoLocale.CreatePseudoLocale();
            pseudo.name = PseudoFileName;
            pseudo.LocaleName = "Pseudo-Locale";
            pseudo.SortOrder = 5;
            pseudo.Methods.Clear();
            pseudo.Methods.Add(new PreserveTags());
            var expander = new Expander();
            expander.ExpansionRules.Clear();
            expander.ExpansionRules.Add(new Expander.ExpansionRule(0, int.MaxValue, 0.3f));
            pseudo.Methods.Add(expander);
            pseudo.Methods.Add(new Accenter());
            pseudo.Methods.Add(new Encapsulator());
            AssetDatabase.CreateAsset(pseudo, LocalesFolder + "/" + PseudoFileName + ".asset");
        }
        LocalizationEditorSettings.AddLocale(pseudo);
        report.Line(how, "Pseudo-locale", AssetDatabase.GetAssetPath(pseudo) + " (" + DescribeMethods(pseudo) + "; reads the en tables)");
    }

    static void EnsureSelectors(LocalizationSettings settings, Report report)
    {
        List<IStartupLocaleSelector> selectors = settings.GetStartupLocaleSelectors();
        if (SelectorsAreSet(settings))
        {
            report.Line("found", "Startup selectors", DescribeSelectors(selectors));
            return;
        }
        // The player's saved choice, else (once DetectSystemLanguage is on) the system language, else English
        string key = selectors.OfType<PlayerPrefLocaleSelector>().Select(s => s.PlayerPreferenceKey).FirstOrDefault(k => !string.IsNullOrEmpty(k)) ?? PlayerPrefKey;
        selectors.Clear();
        selectors.Add(new PlayerPrefLocaleSelector { PlayerPreferenceKey = key });
        if (DetectSystemLanguage)
            selectors.Add(new SystemLocaleSelector());
        selectors.Add(new SpecificLocaleSelector { LocaleId = new LocaleIdentifier(L10n.English) });
        EditorUtility.SetDirty(settings);
        report.Line("set", "Startup selectors", DescribeSelectors(selectors));
    }

    // ------------------------------------------------------------------
    // Fonts
    // ------------------------------------------------------------------

    static void EnsureFonts(Dictionary<string, TMP_FontAsset> fonts, Report report)
    {
        TMP_FontAsset english = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(L10n.DefaultFontPath);
        if (english != null)
        {
            fonts[L10n.English] = english;
            report.Line("found", "Font en", L10n.DefaultFontPath + " (the UI's Coming Soon, used as is)");
        }
        else report.Fail("Font en", L10n.DefaultFontPath + " is missing");

        if (TMP_Settings.instance == null)
        {
            report.Fail("Fonts", "TextMesh Pro's Essential Resources are missing (Window > TextMeshPro > Import TMP Essential Resources)");
            return;
        }

        foreach (FontSpec spec in FontSpecs)
        {
            TMP_FontAsset font = EnsureFontAsset(spec, report);
            if (font != null) fonts[spec.Locale] = font;
        }

        // Fallbacks: the last-resort font (rare glyphs, symbols, names typed in another script), then Liberation Sans
        fonts.TryGetValue("zh-Hans", out TMP_FontAsset lastResort);
        TMP_FontAsset liberation = FindLiberationFallback();
        foreach (FontSpec spec in FontSpecs)
        {
            if (!fonts.TryGetValue(spec.Locale, out TMP_FontAsset font)) continue;
            var wanted = new List<TMP_FontAsset>();
            if (lastResort != null && lastResort != font) wanted.Add(lastResort);
            if (liberation != null) wanted.Add(liberation);
            if (SetFallbacks(font, wanted)) report.Line("set", spec.Name + " fallbacks", Names(font.fallbackFontAssetTable));
        }
    }

    static TMP_FontAsset EnsureFontAsset(FontSpec spec, Report report)
    {
        string path = spec.AssetPath;
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        string how = "found";
        if (font == null)
        {
            var source = AssetDatabase.LoadAssetAtPath<Font>(spec.SourcePath);
            if (source == null)
            {
                report.Fail("Font " + spec.Name, spec.SourcePath + " is missing");
                return null;
            }
            font = TMP_FontAsset.CreateFontAsset(source, spec.SamplingSize, spec.Padding, GlyphRenderMode.SDFAA, AtlasSize, AtlasSize,
                                                 AtlasPopulationMode.Dynamic, true);
            if (font == null)
            {
                report.Fail("Font " + spec.Name, "TextMesh Pro couldn't make a font asset from " + spec.SourcePath + " (is Include Font Data on?)");
                return null;
            }
            font.name = spec.Name;
            AssetDatabase.CreateAsset(font, path);
            how = "created";
        }

        bool changed = RepairFontAsset(font, spec.Name, path);
        report.Line(how == "found" && changed ? "fixed" : how, "Font " + spec.Locale, path + " (" + spec.SamplingSize + " pt / " + spec.Padding + " padding)");
        return font;
    }

    /// <summary>
    /// Dynamic, multi-atlas, cleared before builds, and every atlas texture plus the material saved inside the asset
    /// (a material that is only in memory never reaches the file). True when something had to change.
    /// </summary>
    static bool RepairFontAsset(TMP_FontAsset font, string displayName, string path)
    {
        bool changed = false;
        if (font.atlasPopulationMode != AtlasPopulationMode.Dynamic)
        {
            font.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            changed = true;
        }
        if (!font.isMultiAtlasTexturesEnabled)
        {
            font.isMultiAtlasTexturesEnabled = true;   // CJK needs more than one atlas
            changed = true;
        }
        var serialized = new SerializedObject(font);
        SerializedProperty clear = serialized.FindProperty("m_ClearDynamicDataOnBuild");
        if (clear != null && !clear.boolValue)
        {
            clear.boolValue = true;   // glyphs added while testing in the Editor don't ship
            serialized.ApplyModifiedPropertiesWithoutUndo();
            changed = true;
        }

        Texture2D[] textures = font.atlasTextures;
        if (textures != null)
        {
            for (int i = 0; i < textures.Length; i++)
            {
                Texture2D texture = textures[i];
                if (texture == null || AssetDatabase.Contains(texture)) continue;
                texture.name = displayName + " Atlas" + (i > 0 ? " " + i : "");
                AssetDatabase.AddObjectToAsset(texture, font);
                EditorUtility.SetDirty(texture);
                changed = true;
            }
        }
        Material material = font.material;
        if (material != null)
        {
            if (!AssetDatabase.Contains(material))
            {
                material.name = displayName + " Material";
                AssetDatabase.AddObjectToAsset(material, font);
                changed = true;
            }
            if (font.atlasTexture != null && material.mainTexture != font.atlasTexture)
            {
                material.mainTexture = font.atlasTexture;
                changed = true;
            }
            if (changed) EditorUtility.SetDirty(material);
        }

        if (changed)
        {
            EditorUtility.SetDirty(font);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(path);
        }
        return changed;
    }

    /// <summary>Add the wanted fallbacks (in order, after any the font has already); true when the table changed.</summary>
    static bool SetFallbacks(TMP_FontAsset font, List<TMP_FontAsset> wanted)
    {
        if (font.fallbackFontAssetTable == null) font.fallbackFontAssetTable = new List<TMP_FontAsset>();
        List<TMP_FontAsset> table = font.fallbackFontAssetTable;
        bool changed = table.RemoveAll(f => f == null) > 0;
        foreach (TMP_FontAsset fallback in wanted)
        {
            if (fallback == null || fallback == font || table.Contains(fallback)) continue;
            table.Add(fallback);
            changed = true;
        }
        if (changed) EditorUtility.SetDirty(font);
        return changed;
    }

    static TMP_FontAsset FindLiberationFallback()
    {
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(LiberationFallbackPath);
        if (font != null) return font;
        foreach (string guid in AssetDatabase.FindAssets("t:TMP_FontAsset", new[] { "Assets/TextMesh Pro" }))
        {
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(guid));
            if (font != null && font.name == LiberationFallbackName) return font;
        }
        return null;
    }

    /// <summary>The last-resort font in TMP Settings' global fallback list; modern (word-based) line breaks for Korean.</summary>
    static void EnsureTmpSettings(Dictionary<string, TMP_FontAsset> fonts, Report report)
    {
        if (!fonts.TryGetValue("zh-Hans", out TMP_FontAsset lastResort) || lastResort == null)
        {
            report.Fail("TMP Settings", LastResortFont + " isn't there, so it can't be the global fallback");
            return;
        }
        TMP_Settings settings = FindTmpSettings();
        if (settings == null)
        {
            report.Fail("TMP Settings", "no TMP Settings asset in Assets");
            return;
        }

        var serialized = new SerializedObject(settings);
        bool changed = false;
        SerializedProperty list = serialized.FindProperty("m_fallbackFontAssets");
        if (list == null)
        {
            report.Fail("TMP Settings", "no global fallback list in this TextMesh Pro version");
            return;
        }
        bool has = false;
        for (int i = 0; i < list.arraySize; i++)
        {
            if (list.GetArrayElementAtIndex(i).objectReferenceValue == lastResort) has = true;
        }
        if (!has)
        {
            int index = list.arraySize;
            list.InsertArrayElementAtIndex(index);
            list.GetArrayElementAtIndex(index).objectReferenceValue = lastResort;
            changed = true;
        }
        // Korean breaks lines between words (spaces) like Latin text, not between any two syllables
        SerializedProperty hangul = serialized.FindProperty("m_UseModernHangulLineBreakingRules");
        bool hangulChanged = false;
        if (hangul != null && !hangul.boolValue)
        {
            hangul.boolValue = true;
            changed = hangulChanged = true;
        }
        if (changed)
        {
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);
        }
        report.Line(has ? "found" : "set", "TMP global fallback", LastResortFont + " in " + AssetDatabase.GetAssetPath(settings));
        if (hangulChanged) report.Line("set", "TMP Korean line breaks", "modern (between words)");
    }

    static TMP_Settings FindTmpSettings()
    {
        TMP_Settings found = null;
        foreach (string guid in AssetDatabase.FindAssets("t:TMP_Settings", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var settings = AssetDatabase.LoadAssetAtPath<TMP_Settings>(path);
            if (settings == null) continue;
            if (path.EndsWith("/Resources/TMP Settings.asset", StringComparison.Ordinal)) return settings;   // the one TMP loads
            if (found == null) found = settings;
        }
        return found;
    }

    // ------------------------------------------------------------------
    // Fonts Asset Table
    // ------------------------------------------------------------------

    static void EnsureFontsTable(Dictionary<string, TMP_FontAsset> fonts, Report report)
    {
        AssetTableCollection collection = FindAssetCollection(L10n.FontsTable);
        string how = "found";
        if (collection == null)
        {
            if (FindStringCollection(L10n.FontsTable) != null)
            {
                report.Fail("Fonts table", "a String Table collection is already called " + L10n.FontsTable);
                return;
            }
            string folder = TablesFolder + "/" + L10n.FontsTable;
            EnsureFolder(folder);
            collection = LocalizationEditorSettings.CreateAssetTableCollection(L10n.FontsTable, folder);
            how = "created";
            if (collection == null)
            {
                report.Fail("Fonts table", "couldn't be created");
                return;
            }
        }
        report.Line(how, "Asset Table collection " + L10n.FontsTable, AssetDatabase.GetAssetPath(collection));

        AddressableAssetSettings addressables = AddressableAssetSettingsDefaultObject.Settings;
        bool changed = false;
        foreach (Locale locale in LocalizationEditorSettings.GetLocales())
        {
            string code = locale.Identifier.Code;
            var table = collection.GetTable(locale.Identifier) as AssetTable;
            if (table == null)
            {
                table = collection.AddNewTable(locale.Identifier) as AssetTable;
                changed = true;
            }
            if (table == null)
            {
                report.Fail("Fonts/" + L10n.MainFontKey + " " + code, "couldn't add the table");
                continue;
            }
            if (!fonts.TryGetValue(code, out TMP_FontAsset font) || font == null)
            {
                report.Fail("Fonts/" + L10n.MainFontKey + " " + code, "no font for this language");
                continue;
            }
            string path = AssetDatabase.GetAssetPath(font);
            if (path.Contains("/Resources/"))
            {
                report.Fail("Fonts/" + L10n.MainFontKey + " " + code, path + " is in a Resources folder, which Asset Tables can't load");
                continue;
            }
            string guid = AssetDatabase.AssetPathToGUID(path);
            bool addressable = addressables != null && addressables.FindAssetEntry(guid) != null;
            if (FontsTableGuid(collection, code) != guid || !addressable)
            {
                // Also makes the font Addressable, with the locale's label
                collection.AddAssetToTable(table, L10n.MainFontKey, font);
                changed = true;
            }
        }

        if (changed)
        {
            EditorUtility.SetDirty(collection);
            EditorUtility.SetDirty(collection.SharedData);
            foreach (AssetTable table in collection.AssetTables)
            {
                if (table != null) EditorUtility.SetDirty(table);
            }
            LocalizationEditorSettings.EditorEvents.RaiseCollectionModified(collection, collection);
            AssetDatabase.SaveAssets();
        }
    }

    /// <summary>The asset GUID in the Fonts table's MainFont entry for a locale, or null.</summary>
    static string FontsTableGuid(AssetTableCollection collection, string code)
    {
        if (collection == null || collection.SharedData == null) return null;
        var table = collection.GetTable(new LocaleIdentifier(code)) as AssetTable;
        SharedTableData.SharedTableEntry shared = collection.SharedData.GetEntry(L10n.MainFontKey);
        if (table == null || shared == null) return null;
        AssetTableEntry entry = table.GetEntry(shared.Id);
        return entry != null && !string.IsNullOrEmpty(entry.Guid) ? entry.Guid : null;
    }

    // ------------------------------------------------------------------
    // Verification (against the files, not the objects in memory)
    // ------------------------------------------------------------------

    static void Verify(Report report)
    {
        report.Line("--", "Verification");
        LocalizationSettings settings = LocalizationEditorSettings.ActiveLocalizationSettings;
        report.Check(settings != null, "active Localization Settings", settings != null ? AssetDatabase.GetAssetPath(settings) : "none");
        if (settings != null)
        {
            report.Check(InitializesSynchronously(settings), "initializes synchronously");
            var serialized = new SerializedObject(settings);
            SerializedProperty preload = serialized.FindProperty("m_PreloadBehavior");
            report.Check(preload == null || preload.enumValueIndex == (int)PreloadBehavior.PreloadSelectedLocaleAndFallbacks,
                         "preloads the selected language and its fallback");
            report.Check(SelectorsAreSet(settings), "startup selectors", DescribeSelectors(settings.GetStartupLocaleSelectors()));
        }

        AddressableAssetSettings addressables = AddressableAssetSettingsDefaultObject.Settings;
        report.Check(addressables != null && addressables.BuildAddressablesWithPlayerBuild == AddressableAssetSettings.PlayerBuildOption.BuildWithPlayer,
                     "Addressables content built with the player");

        Locale english = LocalizationEditorSettings.GetLocale(new LocaleIdentifier(L10n.English));
        foreach (LocaleSpec spec in LocaleSpecs)
        {
            Locale locale = LocalizationEditorSettings.GetLocale(new LocaleIdentifier(spec.Code));
            bool fallbackOk = spec.Code == L10n.English || FallsBackTo(locale, english);
            report.Check(locale != null && fallbackOk, "locale " + spec.Code, spec.Code == L10n.English ? "source language" : "falls back to en");
        }
        PseudoLocale pseudo = LocalizationEditorSettings.GetPseudoLocales().FirstOrDefault();
        report.Check(pseudo != null, "pseudo-locale", pseudo != null ? DescribeMethods(pseudo) : "none");

        TMP_FontAsset lastResort = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontsFolder + "/" + LastResortFont + ".asset");
        foreach (FontSpec spec in FontSpecs)
        {
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(spec.AssetPath);
            if (font == null)
            {
                report.Fail("font " + spec.Name, "missing");
                continue;
            }
            Object[] onFile = AssetDatabase.LoadAllAssetsAtPath(spec.AssetPath);
            int materials = onFile.OfType<Material>().Count();
            int textures = onFile.OfType<Texture2D>().Count();
            bool clears = new SerializedObject(font).FindProperty("m_ClearDynamicDataOnBuild")?.boolValue ?? true;
            bool linked = font.material != null && font.atlasTexture != null && font.material.mainTexture == font.atlasTexture;
            bool fallbacksOk = font == lastResort || (font.fallbackFontAssetTable != null && font.fallbackFontAssetTable.Contains(lastResort));
            report.Check(materials >= 1 && textures >= 1 && font.isMultiAtlasTexturesEnabled && font.atlasPopulationMode == AtlasPopulationMode.Dynamic
                         && clears && linked && fallbacksOk,
                         "font " + spec.Name,
                         "material on file " + materials + ", atlas textures on file " + textures + ", multi-atlas " + font.isMultiAtlasTexturesEnabled +
                         ", dynamic " + (font.atlasPopulationMode == AtlasPopulationMode.Dynamic) + ", cleared on build " + clears +
                         ", fallbacks [" + Names(font.fallbackFontAssetTable) + "]");
        }
        report.Check(lastResort != null && GlobalFallbackHas(lastResort), "TMP global fallback", LastResortFont);

        AssetTableCollection fontsTable = FindAssetCollection(L10n.FontsTable);
        report.Check(fontsTable != null, "Fonts table", fontsTable != null ? AssetDatabase.GetAssetPath(fontsTable) : "missing");
        if (fontsTable != null)
        {
            foreach (LocaleSpec spec in LocaleSpecs)
            {
                string guid = FontsTableGuid(fontsTable, spec.Code);
                string path = guid != null ? AssetDatabase.GUIDToAssetPath(guid) : null;
                AddressableAssetEntry entry = guid != null && addressables != null ? addressables.FindAssetEntry(guid) : null;
                report.Check(guid != null && entry != null, "Fonts/" + L10n.MainFontKey + " " + spec.Code,
                             (path ?? "no entry") + (entry != null ? " (Addressable, group " + entry.parentGroup?.Name + ")" : " (NOT Addressable)"));
            }
            report.Line("ok", "Fonts/" + L10n.MainFontKey + " pseudo", "reads the en table: " + L10n.DefaultFontName);
        }

        report.Block(LocalizationChecks.CheckTables());
    }

    // ------------------------------------------------------------------
    // Helpers (also used by the importer and the checks)
    // ------------------------------------------------------------------

    /// <summary>Create a folder (and its parents) through the AssetDatabase.</summary>
    public static void EnsureFolder(string path)
    {
        path = path.Replace('\\', '/').TrimEnd('/');
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        string name = Path.GetFileName(path);
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }

    /// <summary>The String Table collection with this name (also found by searching Assets if the package's cache is stale).</summary>
    public static StringTableCollection FindStringCollection(string name)
    {
        StringTableCollection found = LocalizationEditorSettings.GetStringTableCollection(name);
        if (found != null) return found;
        foreach (string guid in AssetDatabase.FindAssets("t:StringTableCollection", new[] { "Assets" }))
        {
            var collection = AssetDatabase.LoadAssetAtPath<StringTableCollection>(AssetDatabase.GUIDToAssetPath(guid));
            if (collection != null && collection.SharedData != null && collection.TableCollectionName == name) return collection;
        }
        return null;
    }

    /// <summary>The Asset Table collection with this name (also found by searching Assets if the package's cache is stale).</summary>
    public static AssetTableCollection FindAssetCollection(string name)
    {
        AssetTableCollection found = LocalizationEditorSettings.GetAssetTableCollection(name);
        if (found != null) return found;
        foreach (string guid in AssetDatabase.FindAssets("t:AssetTableCollection", new[] { "Assets" }))
        {
            var collection = AssetDatabase.LoadAssetAtPath<AssetTableCollection>(AssetDatabase.GUIDToAssetPath(guid));
            if (collection != null && collection.SharedData != null && collection.TableCollectionName == name) return collection;
        }
        return null;
    }

    static bool InitializesSynchronously(LocalizationSettings settings)
    {
        SerializedProperty property = new SerializedObject(settings).FindProperty("m_InitializeSynchronously");
        return property == null || property.boolValue;   // missing in this package version = nothing to do
    }

    static bool SelectorsAreSet(LocalizationSettings settings)
    {
        List<IStartupLocaleSelector> selectors = settings.GetStartupLocaleSelectors();
        int expected = DetectSystemLanguage ? 3 : 2;
        return selectors != null && selectors.Count == expected
               && selectors[0] is PlayerPrefLocaleSelector
               && (!DetectSystemLanguage || selectors[1] is SystemLocaleSelector)
               && selectors[expected - 1] is SpecificLocaleSelector specific && specific.LocaleId.Code == L10n.English;
    }

    static string DescribeSelectors(List<IStartupLocaleSelector> selectors)
    {
        if (selectors == null) return "none";
        return string.Join(" -> ", selectors.Select(s =>
        {
            switch (s)
            {
                case PlayerPrefLocaleSelector prefs: return "saved choice (PlayerPrefs \"" + prefs.PlayerPreferenceKey + "\")";
                case SystemLocaleSelector _: return "system language";
                case SpecificLocaleSelector specific: return "specific (" + specific.LocaleId.Code + ")";
                default: return s != null ? s.GetType().Name : "(empty)";
            }
        }));
    }

    static string DescribeMethods(PseudoLocale pseudo)
    {
        var parts = new List<string>();
        foreach (IPseudoLocalizationMethod method in pseudo.Methods)
        {
            if (method is Expander expander && expander.ExpansionRules.Count == 1)
                parts.Add("Expander +" + Mathf.RoundToInt(expander.ExpansionRules[0].ExpansionAmount * 100f) + "%");
            else if (method != null)
                parts.Add(method.GetType().Name);
        }
        return string.Join(", ", parts);
    }

    static bool FallsBackTo(Locale locale, Locale fallback)
    {
        if (locale == null || fallback == null || locale.Metadata == null) return false;
        FallbackLocale metadata = locale.Metadata.GetMetadata<FallbackLocale>();
        return metadata != null && metadata.Locale == fallback;
    }

    static bool HasMaterialOnFile(string path)
    {
        return AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>().Any();
    }

    static bool GlobalFallbackHas(TMP_FontAsset font)
    {
        TMP_Settings settings = FindTmpSettings();
        if (settings == null) return false;
        SerializedProperty list = new SerializedObject(settings).FindProperty("m_fallbackFontAssets");
        if (list == null) return false;
        for (int i = 0; i < list.arraySize; i++)
        {
            if (list.GetArrayElementAtIndex(i).objectReferenceValue == font) return true;
        }
        return false;
    }

    static string Names(List<TMP_FontAsset> fonts)
    {
        return fonts == null ? "" : string.Join(", ", fonts.Select(f => f != null ? f.name : "(missing)"));
    }

    /// <summary>The summary block: one line per step and check, logged once.</summary>
    sealed class Report
    {
        readonly StringBuilder text = new StringBuilder();
        int failures;

        public Report(string title)
        {
            text.AppendLine("[L10n Setup] " + title);
        }

        public void Line(string status, string what, string detail = null)
        {
            text.Append("  ").Append(status.PadRight(8)).Append(what);
            if (!string.IsNullOrEmpty(detail)) text.Append(": ").Append(detail);
            text.AppendLine();
        }

        public void Check(bool ok, string what, string detail = null)
        {
            if (ok) Line("ok", what, detail);
            else Fail(what, detail);
        }

        public void Fail(string what, string detail)
        {
            failures++;
            Line("FAIL", what, detail);
        }

        public void Block(string block)
        {
            if (string.IsNullOrEmpty(block)) return;
            foreach (string line in block.Replace("\r\n", "\n").Split('\n'))
            {
                if (line.Length > 0) text.Append("  | ").AppendLine(line);
            }
        }

        public void Log()
        {
            text.Append(failures == 0 ? "  => done, no problems" : "  => " + failures + " problem(s): see the FAIL lines");
            if (failures == 0) Debug.Log(text.ToString());
            else Debug.LogWarning(text.ToString());
        }
    }
}
