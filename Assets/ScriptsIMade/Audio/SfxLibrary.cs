using System;
using UnityEngine;

/// <summary>
/// The game's sound effects: for every Sfx cue its clips (variants), volume, pitch, pitch jitter and priority, plus the
/// anti-spam limits GameSfx applies when it plays them.
/// - The asset is Assets/Resources/Audio/SfxLibrary.asset. Tools > Game > Build Sound Library (SfxLibraryBuilder) makes
///   it from the Kenney packs, matching clips to cues by file name, and refreshes it after a script reload whenever it is
///   missing or its clip lists don't match the files. Values tuned here in the Inspector survive those refreshes; delete
///   the asset and build again to go back to the builder's defaults.
/// - At runtime it is loaded with Resources.Load. In the Editor, while the asset doesn't exist yet, an in-memory library
///   is built from the project instead (EditorFallback), so the sounds work right away.
/// </summary>
public class SfxLibrary : ScriptableObject
{
    /// <summary>Resources.Load path of the library.</summary>
    public const string ResourcesPath = "Audio/SfxLibrary";
    /// <summary>Where the builder saves it.</summary>
    public const string AssetPath = "Assets/Resources/Audio/SfxLibrary.asset";

    [Serializable]
    public class Entry
    {
        [Tooltip("The cue's name (ui_click, chips_win ...)")]
        public string id;
        public Sfx cue;
        [Tooltip("Variants: a random one plays, never the same one twice in a row")]
        public AudioClip[] clips = new AudioClip[0];
        [Tooltip("Per-clip loudness match (set by the builder), so the variants play about equally loud")]
        public float[] gains = new float[0];
        [Range(0f, 1f)]
        public float volume = 0.6f;
        [Tooltip("Base pitch (1 = as recorded)")]
        [Range(0.5f, 2f)]
        public float pitch = 1f;
        [Tooltip("Random pitch change on every play, plus or minus this share (0.04 = ±4%)")]
        [Range(0f, 0.25f)]
        public float pitchJitter = 0.04f;
        public SfxPriority priority = SfxPriority.Normal;
        [Tooltip("Real seconds before this cue can start again")]
        [Min(0f)]
        public float minInterval = 0.04f;
        [Tooltip("At most this many of this cue at once (the oldest one makes room for a new one)")]
        [Range(1, 8)]
        public int maxVoices = 3;
        [Tooltip("Fade the clip out after this many real seconds (0 = play it to the end)")]
        [Min(0f)]
        public float maxLength;
        [Tooltip("Play this many variants in a quick row, like a stutter (1 = once)")]
        [Range(1, 6)]
        public int burst = 1;
        [Tooltip("Real seconds between the plays of a burst")]
        [Min(0.01f)]
        public float burstGap = 0.05f;

        public bool HasClips => clips != null && clips.Length > 0;

        /// <summary>The loudness match of variant <paramref name="index"/> (1 when unknown).</summary>
        public float GainOf(int index)
        {
            return gains != null && index >= 0 && index < gains.Length && gains[index] > 0f ? gains[index] : 1f;
        }
    }

    public Entry[] entries = new Entry[0];

    [NonSerialized] Entry[] byCue;

#if UNITY_EDITOR
    /// <summary>
    /// Set by SfxLibraryBuilder (Editor only): builds a library in memory from the project, used while the asset doesn't
    /// exist yet.
    /// </summary>
    public static Func<SfxLibrary> EditorFallback;
#endif

    /// <summary>The entry of a cue, or null when the library has none.</summary>
    public Entry Get(Sfx cue)
    {
        int index = (int)cue;
        if (index < 0) return null;
        if (byCue == null)
        {
            int size = 0;
            if (entries != null)
                foreach (Entry entry in entries)
                    if (entry != null) size = Mathf.Max(size, (int)entry.cue + 1);
            byCue = new Entry[size];
            if (entries != null)
                foreach (Entry entry in entries)
                    if (entry != null && (int)entry.cue >= 0) byCue[(int)entry.cue] = entry;
        }
        return index < byCue.Length ? byCue[index] : null;
    }

    /// <summary>Forget the cue lookup (after the entries were replaced).</summary>
    public void RebuildLookup()
    {
        byCue = null;
    }

    void OnEnable()
    {
        byCue = null;
    }

    void OnValidate()
    {
        byCue = null;
    }

    /// <summary>The library from Resources (in the Editor, an in-memory one while the asset is missing). Null when there is none.</summary>
    public static SfxLibrary Load()
    {
        SfxLibrary library = Resources.Load<SfxLibrary>(ResourcesPath);
#if UNITY_EDITOR
        if (library == null && EditorFallback != null)
        {
            try { library = EditorFallback(); }
            catch (Exception e) { Debug.LogException(e); }
        }
#endif
        if (library == null)
            Debug.LogWarning("[GameSfx] There is no sound library at Resources/" + ResourcesPath + ", so sound effects are off. Tools > Game > Build Sound Library makes it.");
        return library;
    }
}
