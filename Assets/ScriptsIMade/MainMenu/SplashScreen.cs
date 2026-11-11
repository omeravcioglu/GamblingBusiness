using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// The start-up splash: SplashView's animation (the McHunter Studios logo assembles, then the game logo slaps down on the
/// felt table), about 6 s with its sounds, then a fade to black and the main menu.
/// Space skips it (Enter, Escape and a click too) after a short moment; the hint says so in the current language.
/// The menu loads in the background (LoadSceneAsync) under pulsing dots, so a slow PC doesn't look frozen.
/// Stages go to the Player.log through StartupTimeline.
/// The sprites and the hint font are set by Tools > Game > Splash > Install New Splash (SplashInstaller).
/// </summary>
public class SplashScreen : MonoBehaviour
{
    [Header("Picture")]
    [SerializeField] private Sprite[] sprites; // Assets/UI/Splash, made by Tools/Splash/make_splash_assets.py
    [SerializeField] private TMP_FontAsset hintFont;

    [Header("Timing")]
    [SerializeField] private float fadeOutDuration = 0.45f;
    [SerializeField] private float maxFrameStep = 0.1f; // a long frame (start-up, shader warm-up) slows the animation, never skips it

    [Header("Scene")]
    [SerializeField] private string menuSceneName = SceneFlow.MainMenuScene;
    [SerializeField] private int menuSceneIndex = 1;
    [SerializeField] private bool useSceneIndex = true;

    [Header("Skip")]
    [SerializeField] private bool skippable = true; // Space, Enter, Escape or a click skips the splash
    [SerializeField] private float skipDelay = 0.5f; // Prevent accidental skip right at start-up

    [Header("Loading Dots")]
    [SerializeField] private bool showLoadingDots = true; // pulsing dots over the black screen while the menu loads

    const float HintAlpha = 0.6f;
    const float HintFadeSeconds = 0.4f;
    static readonly Color LoadingDotColor = new Color(0.94f, 0.91f, 0.84f);
    static ThreadPriority loadingPriorityBeforeMenu;

    private SplashView view;
    private float clock;          // animation time
    private float endTime;
    private float hintAlpha;
    private bool started;
    private bool isTransitioning = false;
    private bool canAcceptInput = false;
    private GameObject loadingDots;
    private Image[] loadingDotImages;
    private Texture2D loadingDotTexture;
    private Sprite loadingDotSprite;

    void Start()
    {
        StartupTimeline.Mark("Splash started");

        // Ensure time is running
        Time.timeScale = 1f;

        view = SplashView.Build(transform, sprites, hintFont);
        endTime = SplashView.End;
        // Demo: the demo config may shorten the splash, but never cut the logo short
        if (DemoMode.Active)
            endTime = Mathf.Clamp(DemoMode.Config.splashSeconds, SplashView.LogoLanded, SplashView.End);

        if (view != null)
            L10nText.Attach(view.Hint, "Company", "menu.splash.skip"); // names the Space key in the current language (and its font)
        else
            endTime = 0.5f; // nothing to show: straight on to the menu

        CreateLoadingDots();

        // Delay input acceptance to avoid accidental skips from scene load
        StartCoroutine(EnableInputAfterDelay());
    }

    IEnumerator EnableInputAfterDelay()
    {
        // Only a fresh press skips (GetKeyDown), so a key held since start-up never does. No waiting for every key to be
        // released: a controller that reports a held button kept that wait from ever ending.
        yield return new WaitForSecondsRealtime(skipDelay);

        canAcceptInput = true;
        Debug.Log("[SplashScreen] Now accepting skip input");
    }

    static bool SkipPressed()
    {
        return Input.GetKeyDown(KeyCode.Space) ||
               Input.GetKeyDown(KeyCode.Return) ||
               Input.GetKeyDown(KeyCode.KeypadEnter) ||
               Input.GetKeyDown(KeyCode.Escape) ||
               Input.GetMouseButtonDown(0);
    }

    void Update()
    {
        AnimateLoadingDots();
        if (!started)
        {
            started = true;
            StartupTimeline.Mark("Splash animation started");
        }

        float step = Mathf.Min(Time.unscaledDeltaTime, maxFrameStep);
        float next = clock + step;
        if (view != null)
        {
            if (!isTransitioning)
                PlayCues(clock, next);
            view.Pose(next); // keeps moving under the fade out
            UpdateHint(step);
        }
        clock = next;

        if (isTransitioning) return;
        if (clock >= endTime || (skippable && canAcceptInput && SkipPressed()))
            Skip();
    }

    static void PlayCues(float from, float to)
    {
        foreach (var cue in SplashView.Cues)
            if (cue.time > from && cue.time <= to)
                GameSfx.Play(cue.sound, cue.volume);
    }

    void UpdateHint(float step)
    {
        float target = skippable && canAcceptInput && !isTransitioning ? HintAlpha : 0f;
        hintAlpha = Mathf.MoveTowards(hintAlpha, target, step / HintFadeSeconds);
        view.Hint.alpha = hintAlpha;
    }

    /// <summary>End the splash now: fade to black and load the menu.</summary>
    public void Skip()
    {
        if (!isTransitioning)
            StartCoroutine(TransitionToMenu());
    }

    void OnDestroy()
    {
        if (loadingDotSprite != null) Destroy(loadingDotSprite);
        if (loadingDotTexture != null) Destroy(loadingDotTexture);
    }

    // Three pulsing dots in the bottom-right corner, on their own canvas above the splash. The round sprite is made here
    // and there is no text, so they need no asset, font or language.
    void CreateLoadingDots()
    {
        if (!showLoadingDots) return;

        const int size = 32;
        loadingDotTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "SplashLoadingDot",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        var pixels = new Color32[size * size];
        float radius = size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - radius;
                float dy = y + 0.5f - radius;
                float alpha = Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy)); // soft 1 px edge
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
            }
        }
        loadingDotTexture.SetPixels32(pixels);
        loadingDotTexture.Apply(false, true);
        loadingDotSprite = Sprite.Create(loadingDotTexture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);

        loadingDots = new GameObject("LoadingDots", typeof(RectTransform));
        loadingDots.transform.SetParent(transform, false);
        var canvas = loadingDots.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000; // above the splash and its fade
        var scaler = loadingDots.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

        loadingDotImages = new Image[3];
        for (int i = 0; i < loadingDotImages.Length; i++)
        {
            var dot = new GameObject("Dot" + i, typeof(RectTransform));
            dot.transform.SetParent(loadingDots.transform, false);
            var rect = (RectTransform)dot.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 0f);
            rect.sizeDelta = new Vector2(14f, 14f);
            rect.anchoredPosition = new Vector2(-72f - (loadingDotImages.Length - 1 - i) * 26f, 64f);
            var image = dot.AddComponent<Image>();
            image.sprite = loadingDotSprite;
            image.color = LoadingDotColor;
            image.raycastTarget = false;
            loadingDotImages[i] = image;
        }

        loadingDots.SetActive(false);
    }

    void SetLoadingDots(bool visible)
    {
        if (loadingDots != null && loadingDots.activeSelf != visible)
            loadingDots.SetActive(visible);
    }

    void AnimateLoadingDots()
    {
        if (loadingDots == null || !loadingDots.activeSelf) return;
        float t = Time.unscaledTime * 4f;
        for (int i = 0; i < loadingDotImages.Length; i++)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(t - i * 0.9f);
            Color color = LoadingDotColor;
            color.a = 0.25f + 0.75f * pulse;
            loadingDotImages[i].color = color;
        }
    }

    IEnumerator TransitionToMenu()
    {
        if (isTransitioning) yield break;
        isTransitioning = true;

        Debug.Log($"[SplashScreen] Transitioning to menu after {clock:F1}s of the splash");

        // Fade to black
        if (view != null)
        {
            float from = view.Fade.alpha;
            for (float elapsed = 0f; elapsed < fadeOutDuration; elapsed += Time.unscaledDeltaTime)
            {
                view.Fade.alpha = Mathf.Lerp(from, 1f, elapsed / fadeOutDuration);
                yield return null;
            }
            view.Fade.alpha = 1f;
        }

        // Load menu scene in the background: the loading dots keep moving instead of the screen freezing on black.
        // One frame first, so the dots are on screen even if the load or the menu's first frame stalls.
        SetLoadingDots(true);
        yield return null;
        StartupTimeline.Mark("Splash done: loading the main menu");

        // Loading screen speed until the menu is in (the priority also paces later async loads, so it goes back then)
        loadingPriorityBeforeMenu = Application.backgroundLoadingPriority;
        Application.backgroundLoadingPriority = ThreadPriority.High;
        SceneManager.sceneLoaded -= RestoreLoadingPriority;
        SceneManager.sceneLoaded += RestoreLoadingPriority;

        AsyncOperation load = useSceneIndex
            ? SceneManager.LoadSceneAsync(menuSceneIndex)
            : SceneManager.LoadSceneAsync(menuSceneName);
        if (load == null)
        {
            // Not in Build Settings (Unity logged why)
            SceneManager.sceneLoaded -= RestoreLoadingPriority;
            Application.backgroundLoadingPriority = loadingPriorityBeforeMenu;
            yield break;
        }

        // This object goes away when the menu scene takes over
        yield return load;
    }

    static void RestoreLoadingPriority(Scene scene, LoadSceneMode mode)
    {
        SceneManager.sceneLoaded -= RestoreLoadingPriority;
        Application.backgroundLoadingPriority = loadingPriorityBeforeMenu;
    }
}
