using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// What Build Demo (Windows) changes in the project for the length of the build besides the switch files (see DemoBuild):
/// - Portraits: every file past the first DemoConfig.demoPortraitsPerGender of Resources/Portraits/Female and /Male moves to
///   Assets/_DemoBuildParked with AssetDatabase.MoveAsset (same GUIDs and .meta files, no reimport), so it isn't in the
///   build, and the kept ones get a Standalone max size of demoPortraitMaxSize (reimported). PortraitLibrary keeps the
///   demo's picks and lookups inside the kept files. 0 in either field skips that step.
/// - Windows graphics APIs: Direct3D11 first, Direct3D12 second (the project has Direct3D12 first).
/// Before anything changes, the old state goes to DemoBuildParked/prep-in-progress.json next to Assets (it survives a
/// deleted Library), with the text of every .meta file Cap will touch. Restore puts everything back from it (after the
/// build, or on the next Editor start when a crash interrupted one), writes that text back to any touched .meta file the
/// importer API didn't bring back byte for byte, and keeps the record until every one reads as it did before.
/// A full-game build is refused while the record or the parked portraits are still there (DemoModeMenu).
/// </summary>
public static class DemoBuildPrep
{
    const string Title = "Build Demo";
    const string ResourcesRoot = "Assets/Resources";
    const string ParkRoot = "Assets/_DemoBuildParked";      // no Resources folder inside: nothing here goes into the build
    const string Platform = "Standalone";                    // texture overrides are per platform group (Windows, Mac, Linux)
    const BuildTarget Target = BuildTarget.StandaloneWindows64;
    static readonly GraphicsDeviceType[] DemoApis = { GraphicsDeviceType.Direct3D11, GraphicsDeviceType.Direct3D12 };
    static readonly Regex StandaloneEntry = new Regex(@"^[ \t]*buildTarget: Standalone[ \t]*\r?$", RegexOptions.Multiline);
    // A serialized PortraitSet in a scene (ApplicantGenerator in MainGame): which set, folder, fileNameFormat, firstIndex, lastIndex
    static readonly Regex ScenePortraitSet = new Regex(
        @"^[ \t]*(femalePortraits|malePortraits):[ \t]*\r?\n[ \t]*folder:[ \t]*(.*?)[ \t]*\r?\n[ \t]*fileNameFormat:[ \t]*(.*?)[ \t]*\r?\n" +
        @"[ \t]*firstIndex:[ \t]*(-?\d+)[ \t]*\r?\n[ \t]*lastIndex:[ \t]*(-?\d+)", RegexOptions.Multiline);

    static string RecordFile => Path.Combine(DemoBuild.ParkFolder, "prep-in-progress.json");

    /// <summary>True when a demo build's record or parked portraits are still in the project (an interrupted build).</summary>
    public static bool NeedsRecovery => File.Exists(RecordFile) || Directory.Exists(ParkRoot);

    /// <summary>Why no build may start while NeedsRecovery is true (Apply and DemoModeMenu's build guard).</summary>
    public static string LeftoversMessage =>
        "an earlier demo build's changes are still in the project (" + RecordFile + " or " + ParkRoot + "). Restart the Editor " +
        "to try putting them back again, or see the Console for what couldn't be put back and fix it by hand; then build again.";

    [Serializable]
    class Record
    {
        public string step = "planned";                    // the last step that finished (for whoever reads the file)
        public bool apisSaved;                             // useDefaultApis and apis hold the Windows settings from before
        public bool useDefaultApis;
        public int[] apis = new int[0];                    // GraphicsDeviceType values, in order
        public List<string> parked = new List<string>();   // original paths of the portraits moved to ParkRoot
        public List<ImporterState> importers = new List<ImporterState>();
    }

    /// <summary>
    /// A kept portrait's Standalone texture settings before the build: every public field of the .meta entry. The entry's
    /// one hidden field (forceMaximumCompressionQuality_BC6H_BC7) is never changed: Cap and Restore both edit the object
    /// GetPlatformTextureSettings returns, which carries it.
    /// </summary>
    [Serializable]
    class ImporterState
    {
        public string path;
        public bool hadEntry;           // the .meta had a Standalone entry (false: Restore removes the one the build added)
        public string metaHash;         // Hash128 of the .meta text before the build: tells Restore what is already back
        public string metaText;         // the .meta text before the build: written back when the settings alone don't match it
        public bool overridden, ignorePlatformSupport, crunchedCompression, allowsAlphaSplitting;
        public int maxTextureSize, resizeAlgorithm, format, textureCompression, compressionQuality, androidEtc2Fallback;

        public static ImporterState From(string path, TextureImporterPlatformSettings s, string metaText)
        {
            return new ImporterState
            {
                path = path,
                hadEntry = metaText == null || StandaloneEntry.IsMatch(metaText),
                metaHash = Hash(metaText),
                metaText = metaText,
                overridden = s.overridden,
                ignorePlatformSupport = s.ignorePlatformSupport,
                crunchedCompression = s.crunchedCompression,
                allowsAlphaSplitting = s.allowsAlphaSplitting,
                maxTextureSize = s.maxTextureSize,
                resizeAlgorithm = (int)s.resizeAlgorithm,
                format = (int)s.format,
                textureCompression = (int)s.textureCompression,
                compressionQuality = s.compressionQuality,
                androidEtc2Fallback = (int)s.androidETC2FallbackOverride
            };
        }

        public void ApplyTo(TextureImporterPlatformSettings s)
        {
            s.name = Platform;
            s.overridden = overridden;
            s.ignorePlatformSupport = ignorePlatformSupport;
            s.crunchedCompression = crunchedCompression;
            s.allowsAlphaSplitting = allowsAlphaSplitting;
            s.maxTextureSize = maxTextureSize;
            s.resizeAlgorithm = (TextureResizeAlgorithm)resizeAlgorithm;
            s.format = (TextureImporterFormat)format;
            s.textureCompression = (TextureImporterCompression)textureCompression;
            s.compressionQuality = compressionQuality;
            s.androidETC2FallbackOverride = (AndroidETC2FallbackOverride)androidEtc2Fallback;
        }
    }

    /// <summary>One line for the Build Demo dialog: what this build changes for its length.</summary>
    public static string Describe(DemoConfig config)
    {
        int all = PortraitSet.DefaultFemale().Count + PortraitSet.DefaultMale().Count;
        string portraits = config.demoPortraitsPerGender > 0
            ? "moves all but the first " + N(config.demoPortraitsPerGender) + " portraits of each gender out of Resources (to " + ParkRoot + ")" +
              (config.demoPortraitMaxSize > 0 ? ", caps the kept ones at " + config.demoPortraitMaxSize + " px" : "")
            : "keeps all " + N(all) + " portraits" + (config.demoPortraitMaxSize > 0 ? " but caps them at " + config.demoPortraitMaxSize + " px" : "");
        return portraits + " and puts DirectX 11 first (DirectX 12 second)";
    }

    /// <summary>
    /// Make the demo build's changes, writing the old state to the record before the first one. Throws when it can't start
    /// cleanly; the caller's finally then calls Restore, which undoes whatever happened.
    /// </summary>
    public static void Apply(DemoConfig config)
    {
        if (NeedsRecovery) throw new InvalidOperationException("Can't prepare the demo build: " + LeftoversMessage);
        double start = EditorApplication.timeSinceStartup;
        var record = new Record();
        var kept = new List<string>();
        try
        {
            EditorUtility.DisplayProgressBar(Title, "Listing the portraits ...", 0f);
            if (config.demoPortraitsPerGender > 0) CheckScenePortraitSets();
            PlanSet(PortraitSet.DefaultFemale(), config.demoPortraitsPerGender, kept, record.parked);
            PlanSet(PortraitSet.DefaultMale(), config.demoPortraitsPerGender, kept, record.parked);
            if (config.demoPortraitMaxSize > 0) PlanCaps(kept, config.demoPortraitMaxSize, record.importers);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
        GraphicsDeviceType[] oldApis = PlayerSettings.GetGraphicsAPIs(Target);
        record.useDefaultApis = PlayerSettings.GetUseDefaultGraphicsAPIs(Target);
        record.apis = Array.ConvertAll(oldApis, api => (int)api);
        record.apisSaved = true;
        Save(record, null);   // nothing in the project has changed before this line

        PlayerSettings.SetUseDefaultGraphicsAPIs(Target, false);
        PlayerSettings.SetGraphicsAPIs(Target, DemoApis);
        Save(record, "graphics APIs");
        int parked = Park(record.parked);
        Save(record, "portraits parked");
        if (parked < record.parked.Count)   // a portrait left in Resources ships: fail loudly rather than ship a big demo
            throw new InvalidOperationException("Only " + N(parked) + " of " + N(record.parked.Count) + " portraits could be parked (see the error above).");
        int capped = Cap(record.importers, config.demoPortraitMaxSize);
        Save(record, "portraits capped");

        Debug.Log("[Demo] Prepared the demo build in " + Seconds(start) + ": parked " + N(parked) + " portraits in " + ParkRoot +
                  " (" + N(kept.Count) + " stay in Resources), capped " + N(capped) +
                  (config.demoPortraitMaxSize > 0 ? " at " + config.demoPortraitMaxSize + " px" : "") +
                  ", Windows graphics APIs " + string.Join(", ", DemoApis) + " (were " + string.Join(", ", oldApis) +
                  (record.useDefaultApis ? ", automatic" : "") + ").");
    }

    /// <summary>
    /// Put back everything Apply changed and delete the record, or keep it with an error when something couldn't be put
    /// back (a touched .meta file that still doesn't read as before included), so the next Editor start or demo build
    /// tries again. Adds what it put back to <paramref name="restored"/> (null after a build; DemoBuild passes a list when
    /// it recovers an interrupted one). Without a record or parked portraits it does nothing.
    /// </summary>
    public static void Restore(List<string> restored)
    {
        if (!NeedsRecovery) return;
        double start = EditorApplication.timeSinceStartup;
        Record record = LoadRecord();
        bool complete = record != null || !File.Exists(RecordFile);
        if (record == null && !File.Exists(RecordFile))
            Debug.LogError("[Demo] " + ParkRoot + " is there without the demo build's record (" + RecordFile + "), so only the parked " +
                           "portraits go back. Check the Windows graphics APIs (Direct3D12, Direct3D11) and the portraits' Standalone max " +
                           "size by hand: python Tools/Dev/portrait_meta_check.py status (or compare, with a snapshot) lists what changed.");
        var done = new List<string>();
        int mismatched = 0, checkedMetas = 0;
        string firstMismatch = null;

        // Each step on its own, so one that throws doesn't keep the others from running
        void Run(Action step)
        {
            try
            {
                step();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                complete = false;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }
        Run(() =>
        {
            if (record == null || !record.apisSaved || !RestoreApis(record)) return;
            done.Add("Windows graphics APIs " + string.Join(", ", Array.ConvertAll(record.apis, api => (GraphicsDeviceType)api)));
            // After a crash the Editor may have started from the list the build saved (Direct3D11 first): it only picks its
            // graphics device when it starts
            BuildTarget active = EditorUserBuildSettings.activeBuildTarget;
            if (restored != null && record.apis.Length > 0 && SystemInfo.graphicsDeviceType != (GraphicsDeviceType)record.apis[0] &&
                (active == BuildTarget.StandaloneWindows64 || active == BuildTarget.StandaloneWindows))
                Debug.LogWarning("[Demo] The Editor started on " + SystemInfo.graphicsDeviceType + " from the list the interrupted demo build " +
                                 "left. The list is back (" + (GraphicsDeviceType)record.apis[0] + " first), but the Editor only switches when " +
                                 "it starts: restart Unity (until then the Scene and Game views run without ray tracing).");
        });
        Run(() =>
        {
            if (record == null) return;
            int settings = RestoreImporters(record.importers, out int failed);
            if (settings > 0) done.Add(N(settings) + " portrait import settings");
            if (failed > 0) complete = false;
        });
        Run(() =>
        {
            if (record == null) return;
            int written = RewriteMetas(record.importers, out int failed);
            if (written > 0) done.Add(N(written) + " portrait .meta files from their saved text");
            if (failed > 0) complete = false;
        });
        Run(() =>
        {
            int moved = Unpark(out int left);
            if (moved > 0) done.Add(N(moved) + " parked portraits");
            if (left > 0 || !RemoveParkRoot()) complete = false;
        });
        Run(() => AssetDatabase.Refresh());
        Run(() =>
        {
            if (record == null) return;
            checkedMetas = record.importers.Count;
            foreach (ImporterState s in record.importers)
                if (Hash(ReadMeta(s.path)) != s.metaHash && mismatched++ == 0) firstMismatch = s.path + ".meta";
            if (mismatched > 0) complete = false;   // the record keeps their old text for the next try
        });

        if (mismatched > 0)
            Debug.LogWarning("[Demo] " + N(mismatched) + " of " + N(checkedMetas) + " portrait .meta files don't read as before the build, " +
                             "even after writing their saved text back (first: " + firstMismatch + "). The record keeps that text, so the " +
                             "next Editor start or demo build writes it again.");
        if (complete)
        {
            try
            {
                if (File.Exists(RecordFile)) File.Delete(RecordFile);
                if (File.Exists(RecordFile + ".tmp")) File.Delete(RecordFile + ".tmp");
            }
            catch (Exception e)
            {
                Debug.LogError("[Demo] Couldn't delete " + RecordFile + " (" + e.Message + "): delete it by hand.");
            }
        }
        else
            Debug.LogError("[Demo] Not everything the demo build changed could be put back (see the errors above). " + RecordFile +
                           " is kept, so the next Editor start or demo build tries again; once it's fixed by hand, delete it.");
        if (done.Count > 0 || checkedMetas > 0)
            Debug.Log("[Demo] Put back in " + Seconds(start) + ": " + (done.Count > 0 ? string.Join(", ", done) : "nothing had changed") +
                      (checkedMetas > 0 ? "; " + N(checkedMetas - mismatched) + " of " + N(checkedMetas) + " touched .meta files read as before" : "") + ".");
        restored?.AddRange(done);
    }

    // ------------------------------------------------------------------
    // Plan (reads only)
    // ------------------------------------------------------------------

    /// <summary>
    /// Stop the build when a build scene's PortraitSets (ApplicantGenerator in MainGame) aren't the default ones the kept
    /// files are chosen from: PortraitLibrary picks from the scene's sets, so the demo would pick portraits that are parked.
    /// </summary>
    static void CheckScenePortraitSets()
    {
        foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
        {
            if (!scene.enabled || string.IsNullOrEmpty(scene.path) || !File.Exists(scene.path)) continue;
            foreach (Match m in ScenePortraitSet.Matches(File.ReadAllText(scene.path)))
            {
                PortraitSet expected = m.Groups[1].Value == "femalePortraits" ? PortraitSet.DefaultFemale() : PortraitSet.DefaultMale();
                string folder = m.Groups[2].Value.Trim('\'', '"').Trim('/');
                string format = m.Groups[3].Value.Trim('\'', '"');
                if (folder == expected.folder && (format.Length == 0 || format == expected.fileNameFormat) &&
                    m.Groups[4].Value == expected.firstIndex.ToString(CultureInfo.InvariantCulture) &&
                    m.Groups[5].Value == expected.lastIndex.ToString(CultureInfo.InvariantCulture))
                    continue;
                throw new InvalidOperationException(scene.path + " has " + m.Groups[1].Value + " " + folder + " " + format + " " + m.Groups[4].Value +
                                                    "-" + m.Groups[5].Value + ", but the demo build keeps the first portraits of " + expected.folder +
                                                    " " + expected.fileNameFormat + " " + expected.firstIndex + "-" + expected.lastIndex +
                                                    " (PortraitSet defaults). Set the scene back, or set \"demoPortraitsPerGender\": 0.");
            }
        }
    }

    /// <summary>Sort a set's files into kept (its first <paramref name="perGender"/> numbers, PortraitLibrary's rule; all for
    /// 0) and parked.</summary>
    static void PlanSet(PortraitSet set, int perGender, List<string> kept, List<string> parked)
    {
        string folder = ResourcesRoot + "/" + set.folder.Trim('/');
        if (!Directory.Exists(folder))
        {
            Debug.LogWarning("[Demo] " + folder + " isn't there: no portraits to park or cap in it.");
            return;
        }
        var keptPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int keep = perGender > 0 ? Math.Min(set.Count, perGender) : set.Count;
        for (int i = 0; i < keep; i++) keptPaths.Add(ResourcesRoot + "/" + set.GetPath(set.firstIndex + i));

        string[] files = Directory.GetFiles(folder);
        Array.Sort(files, StringComparer.Ordinal);
        foreach (string file in files)
        {
            if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) || !File.Exists(file + ".meta")) continue;   // assets only
            string path = file.Replace('\\', '/');
            string withoutExtension = path.Substring(0, path.Length - Path.GetExtension(path).Length);
            (perGender <= 0 || keptPaths.Contains(withoutExtension) ? kept : parked).Add(path);
        }
    }

    /// <summary>Record the Standalone settings of every kept portrait that imports larger than <paramref name="maxSize"/>.</summary>
    static void PlanCaps(List<string> kept, int maxSize, List<ImporterState> importers)
    {
        for (int i = 0; i < kept.Count; i++)
        {
            if (i % 200 == 0)
                EditorUtility.DisplayProgressBar(Title, "Reading the kept portraits' import settings (" + N(i) + "/" + N(kept.Count) + ")", (float)i / kept.Count);
            var importer = AssetImporter.GetAtPath(kept[i]) as TextureImporter;
            if (importer == null) continue;
            TextureImporterPlatformSettings now = importer.GetPlatformTextureSettings(Platform);
            int size = now.overridden ? now.maxTextureSize : importer.GetDefaultPlatformTextureSettings().maxTextureSize;
            if (size > maxSize) importers.Add(ImporterState.From(kept[i], now, ReadMeta(kept[i])));
        }
    }

    // ------------------------------------------------------------------
    // Apply
    // ------------------------------------------------------------------

    static int Park(List<string> paths)
    {
        if (paths.Count == 0) return 0;
        var folders = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string path in paths) folders.Add(Parent(ParkedPath(path)));
        foreach (string folder in folders) CreateFolder(folder);   // before StartAssetEditing, so the moves find them
        return MoveAll(paths, ParkedPath, "Parking portraits outside Resources");
    }

    /// <summary>A Standalone override of <paramref name="maxSize"/> px on each recorded portrait, then one batch reimport.</summary>
    static int Cap(List<ImporterState> importers, int maxSize)
    {
        if (importers.Count == 0) return 0;
        int capped = 0;
        AssetDatabase.StartAssetEditing();
        try
        {
            for (int i = 0; i < importers.Count; i++)
            {
                if (i % 50 == 0)
                    EditorUtility.DisplayProgressBar(Title, "Capping the kept portraits at " + maxSize + " px (" + N(i) + "/" + N(importers.Count) + ")",
                                                     (float)i / importers.Count);
                var importer = AssetImporter.GetAtPath(importers[i].path) as TextureImporter;
                if (importer == null) continue;
                // Today's Standalone result (its override, else the default settings) with only the max size lowered
                TextureImporterPlatformSettings demo = importer.GetPlatformTextureSettings(Platform);
                if (!demo.overridden)
                    ImporterState.From(null, importer.GetDefaultPlatformTextureSettings(), null).ApplyTo(demo);
                demo.overridden = true;
                demo.maxTextureSize = maxSize;
                importer.SetPlatformTextureSettings(demo);
                importer.SaveAndReimport();   // queued until StopAssetEditing
                capped++;
            }
            EditorUtility.DisplayProgressBar(Title, "Reimporting " + N(capped) + " portraits at " + maxSize + " px (slower the first time) ...", 1f);
        }
        finally
        {
            AssetDatabase.StopAssetEditing();   // the queued reimports run here
            EditorUtility.ClearProgressBar();
        }
        return capped;
    }

    // ------------------------------------------------------------------
    // Restore
    // ------------------------------------------------------------------

    /// <summary>The Windows graphics API settings from the record. False when they already were the same.</summary>
    static bool RestoreApis(Record record)
    {
        GraphicsDeviceType[] apis = Array.ConvertAll(record.apis ?? new int[0], api => (GraphicsDeviceType)api);
        GraphicsDeviceType[] now = PlayerSettings.GetGraphicsAPIs(Target);
        bool same = PlayerSettings.GetUseDefaultGraphicsAPIs(Target) == record.useDefaultApis && now.Length == apis.Length;
        for (int i = 0; same && i < apis.Length; i++) same = now[i] == apis[i];
        if (same) return false;
        if (apis.Length > 0) PlayerSettings.SetGraphicsAPIs(Target, apis);
        PlayerSettings.SetUseDefaultGraphicsAPIs(Target, record.useDefaultApis);
        return true;
    }

    /// <summary>The recorded Standalone settings on every portrait whose .meta changed, then one batch reimport.</summary>
    static int RestoreImporters(List<ImporterState> importers, out int failed)
    {
        failed = 0;
        var changed = new List<ImporterState>();
        foreach (ImporterState s in importers)
            if (Hash(ReadMeta(s.path)) != s.metaHash) changed.Add(s);   // the others never changed (or are back): no reimport
        if (changed.Count == 0) return 0;

        int count = 0;
        AssetDatabase.StartAssetEditing();
        try
        {
            for (int i = 0; i < changed.Count; i++)
            {
                if (i % 50 == 0)
                    EditorUtility.DisplayProgressBar(Title, "Putting back the portraits' import settings (" + N(i) + "/" + N(changed.Count) + ")",
                                                     (float)i / changed.Count);
                ImporterState s = changed[i];
                var importer = AssetImporter.GetAtPath(s.path) as TextureImporter;
                if (importer == null)
                {
                    Debug.LogError("[Demo] Couldn't put back the import settings of " + s.path + ": there is no texture there.");
                    failed++;
                    continue;
                }
                if (s.hadEntry)
                {
                    TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings(Platform);
                    s.ApplyTo(settings);
                    importer.SetPlatformTextureSettings(settings);
                }
                else importer.ClearPlatformTextureSettings(Platform);
                importer.SaveAndReimport();   // queued until StopAssetEditing
                count++;
            }
            EditorUtility.DisplayProgressBar(Title, "Reimporting " + N(count) + " portraits with their own settings ...", 1f);
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            EditorUtility.ClearProgressBar();
        }
        return count;
    }

    /// <summary>The saved text on every recorded .meta file that still doesn't read as before (its settings are back, but
    /// Unity wrote the file differently or couldn't write it), then one batch reimport. Returns how many it wrote.</summary>
    static int RewriteMetas(List<ImporterState> importers, out int failed)
    {
        failed = 0;
        string firstError = null;
        var written = new List<string>();
        foreach (ImporterState s in importers)
        {
            if (s.metaText == null || !File.Exists(s.path) || Hash(ReadMeta(s.path)) == s.metaHash) continue;
            try
            {
                File.WriteAllText(AssetDatabase.GetTextMetaFilePathFromAssetPath(s.path), s.metaText);
                written.Add(s.path);
            }
            catch (Exception e)
            {
                if (failed++ == 0) firstError = s.path + ".meta: " + e.Message;
            }
        }
        if (failed > 0) Debug.LogError("[Demo] Couldn't write back " + N(failed) + " portrait .meta files (first: " + firstError + ").");
        if (written.Count == 0) return 0;

        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (string path in written) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);   // reads the .meta again
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }
        return written.Count;
    }

    /// <summary>Move every file under ParkRoot back to the same place under Assets/Resources (it finds them on disk, so it
    /// also works when the record couldn't be read). A file whose original path is taken stays parked.</summary>
    static int Unpark(out int left)
    {
        left = 0;
        if (!Directory.Exists(ParkRoot)) return 0;
        var paths = new List<string>();
        foreach (string file in Directory.GetFiles(ParkRoot, "*", SearchOption.AllDirectories))
        {
            string path = file.Replace('\\', '/');
            if (path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
            if (File.Exists(OriginalPath(path)))
            {
                if (left++ == 0) Debug.LogError("[Demo] " + path + " can't go back: " + OriginalPath(path) + " is there too. Keep one of them by hand.");
                continue;
            }
            paths.Add(path);
        }
        paths.Sort(StringComparer.Ordinal);
        int moved = paths.Count > 0 ? MoveAll(paths, OriginalPath, "Moving the parked portraits back") : 0;
        left += paths.Count - moved;
        return moved;
    }

    /// <summary>Delete ParkRoot once only folder .meta files are left in it. False when files are still there.</summary>
    static bool RemoveParkRoot()
    {
        if (!Directory.Exists(ParkRoot)) return true;
        foreach (string file in Directory.GetFiles(ParkRoot, "*", SearchOption.AllDirectories))
            if (!file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) return false;
        if (AssetDatabase.DeleteAsset(ParkRoot)) return true;
        Debug.LogError("[Demo] Couldn't delete the empty " + ParkRoot + " folder: delete it in the Project window.");
        return false;
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    /// <summary>AssetDatabase.MoveAsset for each path in one asset-editing batch (GUIDs and .meta files move along). Returns
    /// how many moved.</summary>
    static int MoveAll(List<string> from, Func<string, string> to, string label)
    {
        int moved = 0, failed = 0;
        string firstError = null;
        AssetDatabase.StartAssetEditing();
        try
        {
            for (int i = 0; i < from.Count; i++)
            {
                if (i % 100 == 0) EditorUtility.DisplayProgressBar(Title, label + " (" + N(i) + "/" + N(from.Count) + ")", (float)i / from.Count);
                string error = AssetDatabase.MoveAsset(from[i], to(from[i]));
                if (string.IsNullOrEmpty(error)) moved++;
                else if (failed++ == 0) firstError = from[i] + ": " + error;
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            EditorUtility.ClearProgressBar();
        }
        if (failed > 0) Debug.LogError("[Demo] " + label + ": " + N(failed) + " of " + N(from.Count) + " files couldn't move (first: " + firstError + ").");
        return moved;
    }

    static void CreateFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = Parent(folder);
        CreateFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }

    static string ParkedPath(string original) => ParkRoot + original.Substring(ResourcesRoot.Length);
    static string OriginalPath(string parked) => ResourcesRoot + parked.Substring(ParkRoot.Length);
    static string Parent(string path) => (Path.GetDirectoryName(path) ?? "").Replace('\\', '/');

    static string ReadMeta(string assetPath)
    {
        string meta = AssetDatabase.GetTextMetaFilePathFromAssetPath(assetPath);
        try
        {
            return File.Exists(meta) ? File.ReadAllText(meta) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    static string Hash(string text) => text != null ? Hash128.Compute(text).ToString() : "";

    static void Save(Record record, string step)
    {
        if (step != null) record.step = step;
        Directory.CreateDirectory(DemoBuild.ParkFolder);
        // Written next to the record, then swapped in: a crash while writing never leaves an empty or cut-off record
        string temp = RecordFile + ".tmp";
        File.WriteAllText(temp, JsonUtility.ToJson(record, true));
        if (File.Exists(RecordFile)) File.Replace(temp, RecordFile, null);
        else File.Move(temp, RecordFile);
    }

    static Record LoadRecord()
    {
        if (!File.Exists(RecordFile)) return null;
        try
        {
            // Every saved record has apisSaved (set before the first save): an empty or broken file isn't a record of nothing
            Record record = JsonUtility.FromJson<Record>(File.ReadAllText(RecordFile));
            if (record == null || !record.apisSaved) throw new FormatException("the file is empty or incomplete");
            return record;
        }
        catch (Exception e)
        {
            Debug.LogError("[Demo] Couldn't read " + RecordFile + " (" + e.Message + "). The parked portraits go back anyway; check the Windows " +
                           "graphics APIs (Direct3D12, Direct3D11) and the portraits' Standalone max size by hand (python Tools/Dev/" +
                           "portrait_meta_check.py lists the .meta files that changed), then delete the file.");
            return null;
        }
    }

    static string N(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

    static string Seconds(double start) =>
        (EditorApplication.timeSinceStartup - start).ToString("0.0", CultureInfo.InvariantCulture) + " s";
}
