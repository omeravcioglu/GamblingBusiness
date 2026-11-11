using UnityEngine;
using System.Collections;

/// <summary>
/// Manages background music across all scenes
/// Persists with DontDestroyOnLoad
/// Volume comes from GameSettings.MusicVolume (set it there, or through SetVolume).
/// </summary>
public class MusicManager : MonoBehaviour
{
    public static MusicManager Instance { get; private set; }

    [Header("Music Tracks")]
    [SerializeField] private AudioClip mainTheme;
    [SerializeField] private AudioClip menuTheme; // Optional separate menu music
    [SerializeField] private AudioClip gameTheme; // Optional separate game music

    [Header("Settings")]
    [Tooltip("Current music volume (loaded from GameSettings at startup)")]
    [SerializeField] [Range(0f, 1f)] private float musicVolume = 0.7f;
    [SerializeField] private bool playOnStart = true;
    [SerializeField] private bool loopMusic = true;
    [SerializeField] private float fadeInDuration = 1f;
    [SerializeField] private float fadeOutDuration = 1f;
    [SerializeField] private float crossfadeDuration = 1.5f;

    [Header("Scene-Based Music (Optional)")]
    [SerializeField] private bool useSceneBasedMusic = false;
    [Tooltip("Main menu scene. SceneFlow.MainMenuScene is always recognised too.")]
    [SerializeField] private string menuSceneName = SceneFlow.MainMenuScene;
    [Tooltip("Main game scene. SceneFlow.MainGameScene is always recognised too.")]
    [SerializeField] private string gameSceneName = SceneFlow.MainGameScene;

    private AudioSource audioSource;
    private AudioSource secondarySource; // For crossfading
    private Coroutine fadeCoroutine;

    void Awake()
    {
        // Singleton pattern
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Setup audio sources
        SetupAudioSources();

        // Load saved volume
        musicVolume = GameSettings.MusicVolume;
    }

    void Start()
    {
        if (Instance != this) return;

        if (playOnStart && mainTheme != null)
        {
            PlayMusic(mainTheme);
        }
    }

    void OnEnable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDisable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void SetupAudioSources()
    {
        // Primary audio source
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.loop = loopMusic;
        audioSource.volume = 0f; // Start silent for fade in

        // Secondary audio source for crossfading
        secondarySource = gameObject.AddComponent<AudioSource>();
        secondarySource.playOnAwake = false;
        secondarySource.loop = loopMusic;
        secondarySource.volume = 0f;
    }

    void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        if (Instance != this || !useSceneBasedMusic) return;

        // Switch music based on scene (older scenes may still hold the old names "MainMenu"/"MainScene")
        bool isMenu = scene.name == menuSceneName || scene.name == SceneFlow.MainMenuScene;
        bool isGame = scene.name == gameSceneName || scene.name == SceneFlow.MainGameScene;

        if (isMenu && menuTheme != null)
        {
            CrossfadeTo(menuTheme);
        }
        else if (isGame && gameTheme != null)
        {
            CrossfadeTo(gameTheme);
        }
        else if (mainTheme != null && audioSource.clip != mainTheme)
        {
            CrossfadeTo(mainTheme);
        }
    }

    #region Public Methods

    /// <summary>
    /// Play a music track (with fade in)
    /// </summary>
    public void PlayMusic(AudioClip clip, bool fadeIn = true)
    {
        if (clip == null || audioSource == null) return;

        StopFade();

        // A crossfade may have been interrupted: silence the other source
        if (secondarySource != null && secondarySource.isPlaying)
        {
            secondarySource.Stop();
            secondarySource.volume = 0f;
        }

        audioSource.clip = clip;
        audioSource.loop = loopMusic;
        audioSource.Play();

        if (fadeIn)
        {
            fadeCoroutine = StartCoroutine(FadeIn(audioSource, fadeInDuration));
        }
        else
        {
            audioSource.volume = musicVolume;
        }

        Debug.Log($"[MusicManager] Playing: {clip.name}");
    }

    /// <summary>
    /// Stop music (with fade out)
    /// </summary>
    public void StopMusic(bool fadeOut = true)
    {
        if (audioSource == null) return;

        StopFade();

        if (fadeOut)
        {
            fadeCoroutine = StartCoroutine(FadeOutAndStop(audioSource, fadeOutDuration));
        }
        else
        {
            audioSource.Stop();
            audioSource.volume = 0f;
        }
    }

    /// <summary>
    /// Pause music
    /// </summary>
    public void PauseMusic()
    {
        if (audioSource == null) return;
        audioSource.Pause();
        secondarySource.Pause();
    }

    /// <summary>
    /// Resume music
    /// </summary>
    public void ResumeMusic()
    {
        if (audioSource == null) return;
        audioSource.UnPause();
        secondarySource.UnPause();
    }

    /// <summary>
    /// Crossfade to a new track
    /// </summary>
    public void CrossfadeTo(AudioClip newClip)
    {
        if (newClip == null || audioSource == null || newClip == audioSource.clip) return;

        StopFade();

        fadeCoroutine = StartCoroutine(CrossfadeCoroutine(newClip));
    }

    /// <summary>
    /// Set music volume (0-1) and save it as the player's setting.
    /// </summary>
    public void SetVolume(float volume)
    {
        // GameSettings stores the value and calls ApplyVolume
        GameSettings.MusicVolume = volume;
        ApplyVolume(GameSettings.MusicVolume);
    }

    /// <summary>
    /// Use this volume right away without saving it (GameSettings calls this).
    /// </summary>
    public void ApplyVolume(float volume)
    {
        musicVolume = Mathf.Clamp01(volume);

        // Running fades read musicVolume every frame, so only adjust a source that is playing normally
        if (fadeCoroutine == null && audioSource != null && audioSource.isPlaying)
            audioSource.volume = musicVolume;
    }

    /// <summary>
    /// Get current music volume
    /// </summary>
    public float GetVolume()
    {
        return musicVolume;
    }

    /// <summary>
    /// Check if music is currently playing
    /// </summary>
    public bool IsPlaying()
    {
        return audioSource != null && audioSource.isPlaying;
    }

    /// <summary>
    /// Play the main theme
    /// </summary>
    public void PlayMainTheme()
    {
        if (mainTheme != null)
            PlayMusic(mainTheme);
    }

    /// <summary>
    /// Play menu theme (or main theme if not set)
    /// </summary>
    public void PlayMenuTheme()
    {
        PlayMusic(menuTheme != null ? menuTheme : mainTheme);
    }

    /// <summary>
    /// Play game theme (or main theme if not set)
    /// </summary>
    public void PlayGameTheme()
    {
        PlayMusic(gameTheme != null ? gameTheme : mainTheme);
    }

    #endregion

    #region Coroutines

    void StopFade()
    {
        if (fadeCoroutine != null)
        {
            StopCoroutine(fadeCoroutine);
            fadeCoroutine = null;
        }
    }

    IEnumerator FadeIn(AudioSource source, float duration)
    {
        float elapsed = 0f;
        source.volume = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            source.volume = Mathf.Lerp(0f, musicVolume, elapsed / duration);
            yield return null;
        }

        source.volume = musicVolume;
        fadeCoroutine = null;
    }

    IEnumerator FadeOutAndStop(AudioSource source, float duration)
    {
        float elapsed = 0f;
        float startVolume = source.volume;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            source.volume = Mathf.Lerp(startVolume, 0f, elapsed / duration);
            yield return null;
        }

        source.volume = 0f;
        source.Stop();
        fadeCoroutine = null;
    }

    IEnumerator CrossfadeCoroutine(AudioClip newClip)
    {
        // Start new track on secondary source
        secondarySource.clip = newClip;
        secondarySource.loop = loopMusic;
        secondarySource.volume = 0f;
        secondarySource.Play();

        float elapsed = 0f;
        float startVolume = audioSource.volume;

        // Crossfade
        while (elapsed < crossfadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / crossfadeDuration;

            audioSource.volume = Mathf.Lerp(startVolume, 0f, t);
            secondarySource.volume = Mathf.Lerp(0f, musicVolume, t);

            yield return null;
        }

        // Stop old track
        audioSource.Stop();
        audioSource.volume = 0f;

        // Swap sources (secondary becomes primary)
        var temp = audioSource;
        audioSource = secondarySource;
        secondarySource = temp;
        audioSource.volume = musicVolume;
        fadeCoroutine = null;

        Debug.Log($"[MusicManager] Crossfaded to: {newClip.name}");
    }

    #endregion
}
