using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Builds the sound library (Assets/Resources/Audio/SfxLibrary.asset) from the two Kenney packs: every Sfx cue gets the
/// clips whose file names match its pattern (the clip map below), a volume set by ear from the recordings' loudness (the
/// casino pack is recorded 5-10 dB quieter than the interface pack), a pitch, pitch jitter (±4%), a priority and its
/// anti-spam limits, plus per-clip gains that bring a cue's variants to about the same loudness (measured from the
/// clips, three quarters of the way).
/// - Tools > Game > Build Sound Library creates or refreshes it. The editor also refreshes it after every script reload
///   (not in Play mode, not in batch mode) when the asset is missing or its clip lists don't match the files, and before a
///   player build. Only the library asset is touched. A refresh keeps the values tuned in the asset (volume, pitch,
///   priority, limits); delete the asset and build again to return to the defaults below.
/// - While the asset doesn't exist, Play mode builds the same library in memory (SfxLibrary.EditorFallback).
/// - Tools > Game > Debug > Play Every Sound plays each cue once, 0.4 s apart (Play mode), and logs which is which.
/// </summary>
[InitializeOnLoad]
public static class SfxLibraryBuilder
{
    const string InterfaceFolder = "Assets/kenney_interface-sounds/Audio";
    const string CasinoFolder = "Assets/kenney_casino-audio/Audio";

    // Loudness match between a cue's variants: this share of the difference to their average, within these gains
    const float MatchShare = 0.75f;
    const float MinGain = 0.5f;
    const float MaxGain = 1.6f;

    struct CueSpec
    {
        public Sfx cue;
        public string id;
        public string folder;
        public string pattern;   // regex on the file name without extension
        public float volume;
        public float pitch;
        public float jitter;
        public SfxPriority priority;
        public float minInterval;
        public int maxVoices;
        public float maxLength;
        public int burst;
        public float burstGap;
    }

    static CueSpec Spec(Sfx cue, string id, string folder, string pattern, float volume, SfxPriority priority = SfxPriority.Normal,
                        float pitch = 1f, float jitter = 0.04f, float minInterval = 0.04f, int maxVoices = 3, float maxLength = 0f,
                        int burst = 1, float burstGap = 0.05f)
    {
        return new CueSpec
        {
            cue = cue, id = id, folder = folder, pattern = pattern, volume = volume, priority = priority, pitch = pitch, jitter = jitter,
            minInterval = minInterval, maxVoices = maxVoices, maxLength = maxLength, burst = burst, burstGap = burstGap
        };
    }

    // The clip map. Volumes aim at a quiet office / casino (level of a clip's loudest 50 ms after its volume): clicks and
    // ticks -26 to -29 dB, open / close / back / toggles about -24, confirmations, errors, drops and stamps about -21, the
    // casino table about -21.5 with a win the loudest (-19.5); the glitch bursts are short, so they sit higher (-17).
    static readonly CueSpec[] Cues =
    {
        // Interface
        Spec(Sfx.UiClick,      "ui_click",      InterfaceFolder, @"^click_\d+$",            0.35f, SfxPriority.Low),
        Spec(Sfx.UiOpen,       "ui_open",       InterfaceFolder, @"^open_\d+$",             0.25f),
        Spec(Sfx.UiClose,      "ui_close",      InterfaceFolder, @"^close_\d+$",            0.25f),
        Spec(Sfx.UiBack,       "ui_back",       InterfaceFolder, @"^back_\d+$",             0.45f, SfxPriority.Low),
        Spec(Sfx.UiConfirm,    "ui_confirm",    InterfaceFolder, @"^confirmation_\d+$",     0.2f),
        Spec(Sfx.UiError,      "ui_error",      InterfaceFolder, @"^error_\d+$",            0.33f),
        Spec(Sfx.UiQuestion,   "ui_question",   InterfaceFolder, @"^question_\d+$",         0.16f),
        Spec(Sfx.UiToggle,     "ui_toggle",     InterfaceFolder, @"^(toggle|switch)_\d+$",  0.27f),
        Spec(Sfx.UiTick,       "ui_tick",       InterfaceFolder, @"^tick_\d+$",             0.28f, minInterval: 0.05f, maxVoices: 2),
        Spec(Sfx.UiDrop,       "ui_drop",       InterfaceFolder, @"^drop_\d+$",             0.34f),
        Spec(Sfx.UiGlitch,     "ui_glitch",     InterfaceFolder, @"^glitch_\d+$",           0.45f, SfxPriority.High, burst: 3, burstGap: 0.05f),
        Spec(Sfx.UiBong,       "ui_bong",       InterfaceFolder, @"^bong_\d+$",             0.37f),
        Spec(Sfx.UiPluck,      "ui_pluck",      InterfaceFolder, @"^pluck_\d+$",            0.3f,  SfxPriority.Low),
        Spec(Sfx.UiMaximize,   "ui_maximize",   InterfaceFolder, @"^maximize_\d+$",         0.24f),
        Spec(Sfx.UiMinimize,   "ui_minimize",   InterfaceFolder, @"^minimize_\d+$",         0.24f),
        Spec(Sfx.UiScratch,    "ui_scratch",    InterfaceFolder, @"^scratch_\d+$",          0.42f),
        Spec(Sfx.UiGlass,      "ui_glass",      InterfaceFolder, @"^glass_\d+$",            0.42f, SfxPriority.High),

        // Casino table
        Spec(Sfx.CardDeal,     "card_deal",     CasinoFolder,    @"^card-slide-\d+$",       0.56f),
        Spec(Sfx.CardPlace,    "card_place",    CasinoFolder,    @"^card-place-\d+$",       0.6f),
        Spec(Sfx.CardToss,     "card_toss",     CasinoFolder,    @"^card-shove-\d+$",       0.62f),
        Spec(Sfx.CardShuffle,  "card_shuffle",  CasinoFolder,    @"^card-shuffle$",         0.76f, jitter: 0.02f, maxVoices: 1, maxLength: 1.1f),
        Spec(Sfx.CardFan,      "card_fan",      CasinoFolder,    @"^card-fan-\d+$",         0.5f),
        Spec(Sfx.ChipBet,      "chip_bet",      CasinoFolder,    @"^chip-lay-\d+$",         0.72f),
        Spec(Sfx.ChipsWin,     "chips_win",     CasinoFolder,    @"^chips-stack-\d+$",      0.95f, SfxPriority.High),
        Spec(Sfx.ChipsCollect, "chips_collect", CasinoFolder,    @"^chips-handle-\d+$",     0.75f),
        Spec(Sfx.ChipsClink,   "chips_clink",   CasinoFolder,    @"^chips-collide-\d+$",    0.94f),
        Spec(Sfx.DiceShake,    "dice_shake",    CasinoFolder,    @"^dice-shake-\d+$",       0.7f,  jitter: 0.03f, maxVoices: 1, maxLength: 1.05f),
        Spec(Sfx.DiceThrow,    "dice_throw",    CasinoFolder,    @"^(dice|die)-throw-\d+$", 0.85f),
        // No coin in the packs: a chip laid down, pitched up, is the flick; a die's bounces, pitched up and quiet, the landing
        Spec(Sfx.CoinFlick,    "coin_flick",    CasinoFolder,    @"^chip-lay-1$",           0.62f, pitch: 1.5f, jitter: 0.05f),
        Spec(Sfx.CoinLand,     "coin_land",     CasinoFolder,    @"^die-throw-\d+$",        0.55f, pitch: 1.3f),
    };

    static SfxLibraryBuilder()
    {
        SfxLibrary.EditorFallback = BuildInMemory;
        // Batch-mode tools of other sessions: leave the assets alone (the in-memory library still works there)
        if (Application.isBatchMode) return;
        EditorApplication.delayCall += RefreshIfNeeded;
        // A reload during Play mode skips the check: do it once the editor is back in Edit mode
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += RefreshIfNeeded;
    }

    // ------------------------------------------------------------------
    // Build / refresh
    // ------------------------------------------------------------------

    [MenuItem("Tools/Game/Build Sound Library")]
    static void BuildFromMenu()
    {
        SfxLibrary library = Build();
        if (library != null) EditorGUIUtility.PingObject(library);
    }

    /// <summary>Create or refresh the library asset: clip lists and gains are rebuilt, values tuned in the asset are kept.</summary>
    public static SfxLibrary Build()
    {
        EnsureFolder(Path.GetDirectoryName(SfxLibrary.AssetPath).Replace('\\', '/'));
        SfxLibrary library = AssetDatabase.LoadAssetAtPath<SfxLibrary>(SfxLibrary.AssetPath);
        bool created = library == null;
        if (created) library = ScriptableObject.CreateInstance<SfxLibrary>();

        int clips = Fill(library, !created, true, out int empty);
        if (created)
            AssetDatabase.CreateAsset(library, SfxLibrary.AssetPath);
        else
            EditorUtility.SetDirty(library);
        AssetDatabase.SaveAssetIfDirty(library);

        string note = empty > 0 ? " " + empty + " cue(s) found no clips: are the Kenney folders still in place?" : "";
        Debug.Log("[SfxLibrary] " + (created ? "Created " : "Refreshed ") + SfxLibrary.AssetPath + ": " + Cues.Length + " cues, " + clips + " clips." + note,
                  library);
        return library;
    }

    /// <summary>True when the asset has every cue with exactly the clips its pattern finds (and a gain for each).</summary>
    public static bool IsUpToDate(SfxLibrary library)
    {
        if (library == null || library.entries == null || library.entries.Length != Cues.Length) return false;
        library.RebuildLookup();
        var cache = new Dictionary<string, List<string>>();
        foreach (CueSpec spec in Cues)
        {
            SfxLibrary.Entry entry = library.Get(spec.cue);
            if (entry == null || entry.clips == null || entry.gains == null) return false;
            List<string> expected = MatchingPaths(spec, cache);
            if (entry.clips.Length != expected.Count || entry.gains.Length != expected.Count) return false;
            for (int i = 0; i < expected.Count; i++)
                if (entry.clips[i] == null || AssetDatabase.GetAssetPath(entry.clips[i]) != expected[i]) return false;
        }
        return true;
    }

    static void RefreshIfNeeded()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += RefreshIfNeeded;   // again once the import is done
            return;
        }
        try
        {
            if (!IsUpToDate(AssetDatabase.LoadAssetAtPath<SfxLibrary>(SfxLibrary.AssetPath))) Build();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    /// <summary>SfxLibrary.EditorFallback: the same library in memory (no loudness match), while the asset doesn't exist.</summary>
    static SfxLibrary BuildInMemory()
    {
        SfxLibrary library = ScriptableObject.CreateInstance<SfxLibrary>();
        library.name = "SfxLibrary (in memory)";
        library.hideFlags = HideFlags.DontSave;
        Fill(library, false, false, out _);
        Debug.Log("[SfxLibrary] " + SfxLibrary.AssetPath + " doesn't exist yet, so the sounds use a library built in memory. " +
                  "Tools > Game > Build Sound Library saves it.");
        return library;
    }

    /// <summary>Fill the library from the clip map; returns the number of clips (and how many cues found none).</summary>
    static int Fill(SfxLibrary library, bool keepTuning, bool measure, out int emptyCues)
    {
        var tuned = new Dictionary<Sfx, SfxLibrary.Entry>();
        if (keepTuning && library.entries != null)
            foreach (SfxLibrary.Entry old in library.entries)
                if (old != null && !tuned.ContainsKey(old.cue)) tuned[old.cue] = old;

        var cache = new Dictionary<string, List<string>>();
        var entries = new List<SfxLibrary.Entry>();
        int total = 0;
        emptyCues = 0;
        foreach (CueSpec spec in Cues)
        {
            var entry = new SfxLibrary.Entry
            {
                id = spec.id, cue = spec.cue, volume = spec.volume, pitch = spec.pitch, pitchJitter = spec.jitter,
                priority = spec.priority, minInterval = spec.minInterval, maxVoices = spec.maxVoices, maxLength = spec.maxLength,
                burst = spec.burst, burstGap = spec.burstGap
            };
            if (tuned.TryGetValue(spec.cue, out SfxLibrary.Entry old)) KeepTuning(old, entry);

            var clips = new List<AudioClip>();
            foreach (string path in MatchingPaths(spec, cache))
            {
                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip != null) clips.Add(clip);
            }
            entry.clips = clips.ToArray();
            entry.gains = measure ? LoudnessGains(entry.clips) : Ones(entry.clips.Length);
            total += entry.clips.Length;
            if (entry.clips.Length == 0) emptyCues++;
            entries.Add(entry);
        }

        library.entries = entries.ToArray();
        library.RebuildLookup();
        return total;
    }

    static void KeepTuning(SfxLibrary.Entry from, SfxLibrary.Entry to)
    {
        to.volume = from.volume;
        to.pitch = from.pitch;
        to.pitchJitter = from.pitchJitter;
        to.priority = from.priority;
        to.minInterval = from.minInterval;
        to.maxVoices = from.maxVoices;
        to.maxLength = from.maxLength;
        to.burst = from.burst;
        to.burstGap = from.burstGap;
    }

    /// <summary>The asset paths of the clips in the cue's folder (not its subfolders) whose names match its pattern, sorted.</summary>
    static List<string> MatchingPaths(CueSpec spec, Dictionary<string, List<string>> cache)
    {
        if (!cache.TryGetValue(spec.folder, out List<string> all))
        {
            all = new List<string>();
            if (AssetDatabase.IsValidFolder(spec.folder))
            {
                foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { spec.folder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (Path.GetDirectoryName(path).Replace('\\', '/') == spec.folder && !all.Contains(path)) all.Add(path);
                }
            }
            all.Sort(string.CompareOrdinal);
            cache[spec.folder] = all;
        }

        var regex = new Regex(spec.pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var matches = new List<string>();
        foreach (string path in all)
            if (regex.IsMatch(Path.GetFileNameWithoutExtension(path))) matches.Add(path);
        return matches;
    }

    static void EnsureFolder(string folder)
    {
        if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder)) return;
        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }

    // ------------------------------------------------------------------
    // Loudness match
    // ------------------------------------------------------------------

    static float[] Ones(int count)
    {
        var gains = new float[count];
        for (int i = 0; i < count; i++) gains[i] = 1f;
        return gains;
    }

    /// <summary>Per-clip gains that move each variant most of the way to the variants' average loudness.</summary>
    static float[] LoudnessGains(AudioClip[] clips)
    {
        float[] gains = Ones(clips.Length);
        if (clips.Length < 2) return gains;
        var loudness = new float[clips.Length];
        float sum = 0f;
        int known = 0;
        for (int i = 0; i < clips.Length; i++)
        {
            loudness[i] = LoudestDb(clips[i]);
            if (float.IsNaN(loudness[i])) continue;
            sum += loudness[i];
            known++;
        }
        if (known < 2) return gains;
        float mean = sum / known;
        for (int i = 0; i < clips.Length; i++)
        {
            if (float.IsNaN(loudness[i])) continue;
            float gain = Mathf.Clamp(Mathf.Pow(10f, MatchShare * (mean - loudness[i]) / 20f), MinGain, MaxGain);
            gains[i] = Mathf.Round(gain * 1000f) / 1000f;
        }
        return gains;
    }

    /// <summary>Level of the clip's loudest 50 ms in dBFS (mono mix), or NaN when its samples can't be read.</summary>
    static float LoudestDb(AudioClip clip)
    {
        if (clip == null || clip.loadType != AudioClipLoadType.DecompressOnLoad || clip.samples <= 0) return float.NaN;
        bool wasLoaded = clip.loadState == AudioDataLoadState.Loaded;
        if (!wasLoaded) clip.LoadAudioData();
        if (clip.loadState != AudioDataLoadState.Loaded) return float.NaN;
        try
        {
            int channels = Mathf.Max(1, clip.channels);
            int frames = clip.samples;
            var data = new float[frames * channels];
            if (!clip.GetData(data, 0)) return float.NaN;

            int window = Mathf.Clamp(clip.frequency / 20, 1, frames);
            var power = new float[frames];
            for (int f = 0; f < frames; f++)
            {
                float mono = 0f;
                for (int c = 0; c < channels; c++) mono += data[f * channels + c];
                mono /= channels;
                power[f] = mono * mono;
            }
            double run = 0.0, best = 0.0;
            for (int f = 0; f < frames; f++)
            {
                run += power[f];
                if (f >= window) run -= power[f - window];
                if (f >= window - 1 && run > best) best = run;
            }
            double meanSquare = best / window;
            return meanSquare > 1e-12 ? (float)(10.0 * Math.Log10(meanSquare)) : float.NaN;
        }
        finally
        {
            if (!wasLoaded) clip.UnloadAudioData();
        }
    }

    // ------------------------------------------------------------------
    // Debug: Tools > Game > Debug > Play Every Sound
    // ------------------------------------------------------------------

    const double DemoGap = 0.4;
    static readonly Queue<Sfx> demoQueue = new Queue<Sfx>();
    static double demoNext;
    static int demoIndex, demoTotal;

    [MenuItem("Tools/Game/Debug/Play Every Sound")]
    static void PlayEverySound()
    {
        if (!EditorApplication.isPlaying)
        {
            Debug.LogWarning("[GameSfx] Enter Play mode first.");
            return;
        }
        demoQueue.Clear();
        foreach (Sfx cue in (Sfx[])Enum.GetValues(typeof(Sfx)))
            if (cue != Sfx.None) demoQueue.Enqueue(cue);
        demoIndex = 0;
        demoTotal = demoQueue.Count;
        demoNext = EditorApplication.timeSinceStartup;
        EditorApplication.update -= PlayNextDemo;
        EditorApplication.update += PlayNextDemo;
    }

    [MenuItem("Tools/Game/Debug/Play Every Sound", true)]
    static bool CanPlayEverySound()
    {
        return EditorApplication.isPlaying;
    }

    static void PlayNextDemo()
    {
        if (!EditorApplication.isPlaying || demoQueue.Count == 0)
        {
            EditorApplication.update -= PlayNextDemo;
            return;
        }
        if (EditorApplication.timeSinceStartup < demoNext) return;
        demoNext += DemoGap;

        Sfx cue = demoQueue.Dequeue();
        demoIndex++;
        GameSfx.Play(cue);
        SfxLibrary library = GameSfx.Library;
        SfxLibrary.Entry entry = library != null ? library.Get(cue) : null;
        Debug.Log("[GameSfx] " + demoIndex + "/" + demoTotal + "  " + (entry != null
            ? entry.id + " · " + entry.clips.Length + " clip(s) · volume " + entry.volume.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " · " + entry.priority
            : cue + ": not in the sound library"));
    }
}

/// <summary>Before a player build: make sure the sound library asset exists and matches the clips (builds have no in-memory fallback).</summary>
class SfxLibraryBuildCheck : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        if (!SfxLibraryBuilder.IsUpToDate(AssetDatabase.LoadAssetAtPath<SfxLibrary>(SfxLibrary.AssetPath)))
            SfxLibraryBuilder.Build();
    }
}
