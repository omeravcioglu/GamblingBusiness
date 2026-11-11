using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Plays the game's sound effects: GameSfx.Play(Sfx.ChipsWin).
/// - The cues and their clips come from SfxLibrary (Resources/Audio/SfxLibrary). Every play picks a random variant (never
///   the same one twice in a row), jitters the pitch and multiplies the volume by GameSettings.SfxVolume (the master
///   volume is AudioListener.volume).
/// - Creates itself when the game starts: a DontDestroyOnLoad object with 12 pooled 2D AudioSources and UIClickSounds (the
///   click of every uGUI button). The sources ignore AudioListener.pause and time scale, so UI sounds play while the game
///   is paused.
/// - Anti-spam: a cue can't restart within its minInterval (about 40 ms) or play more than its maxVoices (3) at once, the
///   oldest one making room. Low-priority cues (toasts, back) wait for the end of the frame, UIClickSounds' clicks for the
///   end of the next one: they are dropped when a more important cue started in the last 120 ms (so a button that plays
///   its own sound doesn't also click), a click also when a toast answered it, and only one of them plays at a time.
/// - PlayDelayed / PlayPair schedule cues in real time (a scene change drops what hasn't played yet).
/// </summary>
[DefaultExecutionOrder(10000)]   // LateUpdate after everyone else's: every sound of the frame is known by then
[DisallowMultipleComponent]
public class GameSfx : MonoBehaviour
{
    const int VoiceCount = 12;
    const float LowGiveWaySeconds = 0.12f;  // a Low cue is dropped when a more important cue started this recently
    const float LowGapSeconds = 0.07f;      // Low cues of one level at least this far apart (a burst of toasts = one sound)
    const float FadeOutSeconds = 0.15f;     // how a cue with a maxLength ends
    const float LossScale = 0.6f;           // a lost hand's chips_clink is soft

    // Levels: a cue's priority, or Feedback for UIClickSounds (below everything, even toasts)
    const int Feedback = 0, Low = 1, Normal = 2, High = 3;
    const int FromCue = -1;

    struct SoundRequest
    {
        public Sfx cue;
        public float volume;   // volume scale of this play
        public int level;      // FromCue = the cue's priority
        public float time;     // when a delayed request is due / when a Low request was made (unscaled seconds)
        public int frame;      // the frame a Low request was made in
        public bool repeat;    // one of a burst's extra plays
    }

    static readonly int CueCount = CountCues();
    static readonly Comparison<SoundRequest> ByTime = (a, b) => a.time.CompareTo(b.time);

    static GameSfx instance;
    static bool quitting;

    SfxLibrary library;
    bool libraryLoaded;
    AudioSource[] voices;
    int[] voiceCue, voiceLevel;
    float[] voiceStart, voiceStopAt, voiceVolume;
    float[] lastStart;
    int[] lastVariant;
    readonly float[] lastLevelStart = { -10f, -10f, -10f, -10f };
    float lastImportantStart = -10f;
    readonly List<SoundRequest> delayed = new List<SoundRequest>();
    readonly List<SoundRequest> due = new List<SoundRequest>();
    readonly List<SoundRequest> pendingLow = new List<SoundRequest>();
    Sfx uiCheckCue = Sfx.None;
    int uiCheckCount, uiCheckFrames;

    // ------------------------------------------------------------------
    // Static API
    // ------------------------------------------------------------------

    /// <summary>Play a cue (a random variant) at its library volume times <paramref name="volumeScale"/>.</summary>
    public static void Play(Sfx cue, float volumeScale = 1f)
    {
        Request(cue, volumeScale, 0f, FromCue);
    }

    /// <summary>Play a cue at Low priority: it gives way to more important sounds (toasts use this).</summary>
    public static void PlayLow(Sfx cue, float volumeScale = 1f)
    {
        Request(cue, volumeScale, 0f, Low);
    }

    /// <summary>Play a cue after <paramref name="delay"/> real seconds (a scene change drops it).</summary>
    public static void PlayDelayed(Sfx cue, float delay, float volumeScale = 1f)
    {
        Request(cue, volumeScale, delay, FromCue);
    }

    /// <summary>
    /// Two cues: <paramref name="first"/> after <paramref name="delay"/>, then <paramref name="second"/>
    /// <paramref name="gap"/> seconds later. Sfx.None leaves one out.
    /// </summary>
    public static void PlayPair(Sfx first, Sfx second, float gap, float delay = 0f)
    {
        Request(first, 1f, delay, FromCue);
        Request(second, 1f, delay + Mathf.Max(0f, gap), FromCue);
    }

    /// <summary>Play a cue unless it started less than <paramref name="minGap"/> real seconds ago (call it every frame for a steady tick).</summary>
    public static void PlayThrottled(Sfx cue, float minGap, float volumeScale = 1f)
    {
        if (cue == Sfx.None) return;
        GameSfx player = Player;
        if (player == null || Time.unscaledTime - player.lastStart[(int)cue] < minGap) return;
        Request(cue, volumeScale, 0f, FromCue);
    }

    /// <summary>A blackjack hand's end: a win (chips_win), a push (chips_collect) or a loss (a soft chips_clink).</summary>
    public static void PlayResult(BlackjackResult result, float delay = 0f)
    {
        switch (result)
        {
            case BlackjackResult.Win: PlayDelayed(Sfx.ChipsWin, delay); break;
            case BlackjackResult.Push: PlayDelayed(Sfx.ChipsCollect, delay); break;
            default: PlayDelayed(Sfx.ChipsClink, delay, LossScale); break;
        }
    }

    /// <summary>A casino blackjack hand's end: a win or blackjack (chips_win), a push (chips_collect) or a loss (a soft chips_clink).</summary>
    public static void PlayResult(CasinoHandResult result, float delay = 0f)
    {
        switch (result)
        {
            case CasinoHandResult.Win:
            case CasinoHandResult.Blackjack: PlayDelayed(Sfx.ChipsWin, delay); break;
            case CasinoHandResult.Push: PlayDelayed(Sfx.ChipsCollect, delay); break;
            case CasinoHandResult.Lose: PlayDelayed(Sfx.ChipsClink, delay, LossScale); break;
        }
    }

    /// <summary>Volume scale for a win that pays <paramref name="multiplier"/> x the bet: 1.5x plays at 55%, 20x and more at full volume.</summary>
    public static float WinScale(double multiplier)
    {
        return Mathf.Lerp(0.55f, 1f, Mathf.InverseLerp(1.5f, 20f, (float)multiplier));
    }

    /// <summary>The sound of a GameNotifications toast, at Low priority (it gives way to the sound of whatever caused it).</summary>
    public static void PlayNotification(NotificationKind kind)
    {
        switch (kind)
        {
            case NotificationKind.Good: PlayLow(Sfx.UiConfirm, 0.5f); break;
            case NotificationKind.Bad: PlayLow(Sfx.UiError, 0.5f); break;
            case NotificationKind.Special: PlayLow(Sfx.UiBong); break;
            default: PlayLow(Sfx.UiPluck); break;
        }
    }

    /// <summary>
    /// Play <paramref name="cue"/> at the end of the frame only if buttons were shown or hidden meanwhile (checked for two
    /// frames). UIManager calls this before an Escape handler runs: an Escape that closed or stepped back something is
    /// heard, one that was swallowed isn't.
    /// </summary>
    public static void PlayIfUiChanges(Sfx cue)
    {
        if (cue == Sfx.None) return;
        GameSfx player = Player;
        if (player == null) return;
        player.uiCheckCue = cue;
        player.uiCheckCount = Selectable.allSelectableCount;
        player.uiCheckFrames = 2;
    }

    /// <summary>UIClickSounds: a click, toggle or slider tick, below every other sound (even toasts).</summary>
    internal static void PlayFeedback(Sfx cue, float volumeScale = 1f)
    {
        Request(cue, volumeScale, 0f, Feedback);
    }

    /// <summary>The library the player uses (null outside Play mode, or when there is none).</summary>
    public static SfxLibrary Library
    {
        get
        {
            GameSfx player = Player;
            return player != null ? player.Lib : null;
        }
    }

    static void Request(Sfx cue, float volumeScale, float delay, int level)
    {
        if (cue == Sfx.None || volumeScale <= 0f) return;
        GameSfx player = Player;
        if (player == null) return;
        var request = new SoundRequest { cue = cue, volume = volumeScale, level = level };
        if (delay > 0f)
        {
            request.time = Time.unscaledTime + delay;
            player.delayed.Add(request);
        }
        else
        {
            player.Route(request);
        }
    }

    /// <summary>The player, created on first use while the game runs (never while quitting).</summary>
    static GameSfx Player
    {
        get
        {
            if (instance != null) return instance;
            if (quitting || !Application.isPlaying) return null;
            GameObject go = new GameObject("GameSfx");
            DontDestroyOnLoad(go);
            go.AddComponent<GameSfx>();   // Awake registers it and adds UIClickSounds
            return instance;
        }
    }

    static int CountCues()
    {
        int max = -1;
        foreach (Sfx cue in (Sfx[])Enum.GetValues(typeof(Sfx)))
            max = Mathf.Max(max, (int)cue);
        return max + 1;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        // Entering Play mode without a domain reload keeps statics: start every session clean
        instance = null;
        quitting = false;
        Application.quitting -= OnQuitting;
        Application.quitting += OnQuitting;
    }

    static void OnQuitting()
    {
        quitting = true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void CreateAtStart()
    {
        // The clicks of the first scene's buttons need the player before any sound is asked for
        _ = Player;
    }

    // ------------------------------------------------------------------
    // Player
    // ------------------------------------------------------------------

    SfxLibrary Lib
    {
        get
        {
            if (!libraryLoaded)
            {
                libraryLoaded = true;
                library = SfxLibrary.Load();
                if (library != null) StartCoroutine(Preload(library));
            }
            return library;
        }
    }

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        if (transform.parent == null) DontDestroyOnLoad(gameObject);

        lastStart = new float[CueCount];
        lastVariant = new int[CueCount];
        for (int i = 0; i < CueCount; i++)
        {
            lastStart[i] = -10f;
            lastVariant[i] = -1;
        }

        voices = new AudioSource[VoiceCount];
        voiceCue = new int[VoiceCount];
        voiceLevel = new int[VoiceCount];
        voiceStart = new float[VoiceCount];
        voiceStopAt = new float[VoiceCount];
        voiceVolume = new float[VoiceCount];
        for (int i = 0; i < VoiceCount; i++)
        {
            AudioSource source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;           // 2D
            source.ignoreListenerPause = true;  // UI sounds play while the game is paused
            source.bypassReverbZones = true;
            source.dopplerLevel = 0f;
            voices[i] = source;
            voiceCue[i] = -1;
            voiceStopAt[i] = -1f;
        }

        if (GetComponent<UIClickSounds>() == null) gameObject.AddComponent<UIClickSounds>();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void Start()
    {
        _ = Lib;   // load (and start preloading) the library before the first sound
    }

    void OnDestroy()
    {
        if (instance != this) return;
        instance = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single) return;
        // Sounds already playing carry on; what the old scene scheduled doesn't
        delayed.Clear();
        pendingLow.Clear();
        uiCheckCue = Sfx.None;
    }

    /// <summary>The clips don't preload with their scene (import settings): decode them one a frame now, not on their first play.</summary>
    static IEnumerator Preload(SfxLibrary lib)
    {
        SfxLibrary.Entry[] entries = lib.entries;
        if (entries == null) yield break;
        foreach (SfxLibrary.Entry entry in entries)
        {
            if (entry == null || entry.clips == null) continue;
            foreach (AudioClip clip in entry.clips)
            {
                if (clip == null || clip.loadState != AudioDataLoadState.Unloaded) continue;
                clip.LoadAudioData();
                yield return null;
            }
        }
    }

    void Update()
    {
        float now = Time.unscaledTime;

        // Delayed cues that are due, in order
        if (delayed.Count > 0)
        {
            due.Clear();
            for (int i = delayed.Count - 1; i >= 0; i--)
            {
                if (delayed[i].time > now) continue;
                due.Add(delayed[i]);
                delayed.RemoveAt(i);
            }
            if (due.Count > 1) due.Sort(ByTime);
            foreach (SoundRequest request in due)
            {
                if (request.repeat) Repeat(request);
                else Route(request);
            }
            due.Clear();
        }

        // Cues with a maxLength fade out
        for (int i = 0; i < VoiceCount; i++)
        {
            if (voiceStopAt[i] < 0f) continue;
            AudioSource source = voices[i];
            if (!source.isPlaying)
            {
                voiceStopAt[i] = -1f;
                continue;
            }
            float t = (now - voiceStopAt[i]) / FadeOutSeconds;
            if (t < 0f) continue;
            if (t >= 1f)
            {
                source.Stop();
                voiceStopAt[i] = -1f;
            }
            else
            {
                source.volume = voiceVolume[i] * (1f - t);
            }
        }
    }

    void LateUpdate()
    {
        if (uiCheckCue != Sfx.None) CheckUiChange();
        if (pendingLow.Count == 0) return;

        // Low cues are decided in the frame they were asked for; clicks (Feedback) a frame later, so a screen that answers
        // the click on its next frame (a card dealt by a view's Update) still takes over. One of them a frame: the most
        // important one asked for first (a toast beats a click).
        int frame = Time.frameCount;
        int best = -1;
        for (int i = 0; i < pendingLow.Count; i++)
        {
            if (pendingLow[i].level == Feedback && pendingLow[i].frame >= frame) continue;
            if (best < 0 || pendingLow[i].level > pendingLow[best].level) best = i;
        }
        if (best < 0) return;
        SoundRequest chosen = pendingLow[best];
        for (int i = pendingLow.Count - 1; i >= 0; i--)
            if (pendingLow[i].level != Feedback || pendingLow[i].frame < frame) pendingLow.RemoveAt(i);   // this frame's clicks wait

        float now = Time.unscaledTime;
        if (now - lastImportantStart < LowGiveWaySeconds) return;   // a button's own sound, a win... already playing
        if (now - lastLevelStart[chosen.level] < LowGapSeconds) return;
        if (chosen.level == Feedback && lastLevelStart[Low] >= chosen.time) return;   // a toast or "back" answered the click
        SfxLibrary.Entry entry = Lib != null ? Lib.Get(chosen.cue) : null;
        if (entry != null && entry.HasClips) Begin(chosen, entry);
    }

    void CheckUiChange()
    {
        if (Selectable.allSelectableCount != uiCheckCount)
        {
            Sfx cue = uiCheckCue;
            uiCheckCue = Sfx.None;
            Route(new SoundRequest { cue = cue, volume = 1f, level = FromCue });
            return;
        }
        if (--uiCheckFrames <= 0) uiCheckCue = Sfx.None;
    }

    void Route(SoundRequest request)
    {
        SfxLibrary.Entry entry = Lib != null ? Lib.Get(request.cue) : null;
        if (entry == null || !entry.HasClips) return;
        if (request.level == FromCue) request.level = LevelOf(entry.priority);
        if (request.level <= Low)
        {
            // Decided in LateUpdate, once every sound of this frame (a click: of the next frame too) is known
            request.time = Time.unscaledTime;
            request.frame = Time.frameCount;
            pendingLow.Add(request);
        }
        else
        {
            Begin(request, entry);
        }
    }

    void Repeat(SoundRequest request)
    {
        SfxLibrary.Entry entry = Lib != null ? Lib.Get(request.cue) : null;
        if (entry != null && entry.HasClips) Begin(request, entry);
    }

    void Begin(SoundRequest request, SfxLibrary.Entry entry)
    {
        float now = Time.unscaledTime;
        int cue = (int)request.cue;
        if (cue < 0 || cue >= CueCount) return;
        if (!request.repeat && now - lastStart[cue] < entry.minInterval) return;

        float volume = entry.volume * request.volume * GameSettings.SfxVolume;
        if (volume <= 0.001f) return;

        // A random variant, never the one that played last
        int count = entry.clips.Length;
        int variant;
        if (count <= 1 || lastVariant[cue] < 0 || lastVariant[cue] >= count)
        {
            variant = UnityEngine.Random.Range(0, count);
        }
        else
        {
            variant = UnityEngine.Random.Range(0, count - 1);
            if (variant >= lastVariant[cue]) variant++;
        }
        AudioClip clip = entry.clips[variant];
        if (clip == null) return;

        int voice = PickVoice(cue, request.level, entry.maxVoices);
        if (voice < 0) return;

        AudioSource source = voices[voice];
        float jitter = Mathf.Max(0f, entry.pitchJitter);
        source.Stop();
        source.clip = clip;
        source.volume = Mathf.Clamp01(volume * entry.GainOf(variant));
        source.pitch = Mathf.Clamp(entry.pitch * (1f + UnityEngine.Random.Range(-jitter, jitter)), 0.1f, 3f);
        source.priority = AudioPriority(request.level);
        source.Play();

        voiceCue[voice] = cue;
        voiceLevel[voice] = request.level;
        voiceStart[voice] = now;
        voiceVolume[voice] = source.volume;
        voiceStopAt[voice] = entry.maxLength > 0f ? now + entry.maxLength : -1f;

        lastStart[cue] = now;
        lastVariant[cue] = variant;
        lastLevelStart[request.level] = now;
        if (request.level >= Normal) lastImportantStart = now;

        // A burst's other plays (their own variants)
        if (!request.repeat && entry.burst > 1)
        {
            for (int i = 1; i < entry.burst; i++)
            {
                delayed.Add(new SoundRequest
                {
                    cue = request.cue, volume = request.volume, level = request.level,
                    time = now + i * Mathf.Max(0.01f, entry.burstGap), repeat = true
                });
            }
        }
    }

    /// <summary>
    /// A voice for a new sound: the oldest voice of the same cue when it already plays maxVoices times, else a free one,
    /// else the least important (then oldest) voice that isn't more important than the new sound. -1 = none.
    /// </summary>
    int PickVoice(int cue, int level, int maxVoices)
    {
        int same = 0, oldestSame = -1, free = -1, victim = -1;
        for (int i = 0; i < VoiceCount; i++)
        {
            if (!voices[i].isPlaying)
            {
                if (free < 0) free = i;
                continue;
            }
            if (voiceCue[i] == cue)
            {
                same++;
                if (oldestSame < 0 || voiceStart[i] < voiceStart[oldestSame]) oldestSame = i;
            }
            if (voiceLevel[i] <= level && (victim < 0 || voiceLevel[i] < voiceLevel[victim]
                                           || voiceLevel[i] == voiceLevel[victim] && voiceStart[i] < voiceStart[victim]))
                victim = i;
        }
        if (same >= Mathf.Max(1, maxVoices)) return oldestSame;
        return free >= 0 ? free : victim;
    }

    static int LevelOf(SfxPriority priority)
    {
        switch (priority)
        {
            case SfxPriority.Low: return Low;
            case SfxPriority.High: return High;
            default: return Normal;
        }
    }

    /// <summary>AudioSource.priority (0 = most important) for a level, so Unity's own voice limit drops the right sounds too.</summary>
    static int AudioPriority(int level)
    {
        switch (level)
        {
            case High: return 32;
            case Normal: return 128;
            case Low: return 192;
            default: return 224;
        }
    }
}
