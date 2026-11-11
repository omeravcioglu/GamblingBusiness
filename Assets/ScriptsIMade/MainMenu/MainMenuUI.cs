using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Main Menu controller with New Game, Continue, Settings, and Quit
/// - New Game goes through the coin toss gate (when assigned): the "bank loan" flip. Each failed flip raises the odds
///   (the Inspector's chance, then at least 1 in 2, then a sure yes), so nobody is stuck at the menu. The menu
///   buttons are disabled while a toss or a scene change is running.
/// - Settings are stored through GameSettings (shared with the pause menu and the dice).
/// - Works when the player comes back from the game (persistent managers survive: SaveSystem, CityOnline...).
/// </summary>
public class MainMenuUI : MonoBehaviour
{
    [Header("Buttons")]
    [SerializeField] private Button newGameButton;
    [SerializeField] private Button continueButton;
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button quitButton;

    [Header("Panels")]
    [SerializeField] private GameObject settingsPanel;
    [SerializeField] private GameObject confirmNewGamePanel;

    [Header("Settings Panel")]
    [SerializeField] private Button settingsCloseButton;
    [SerializeField] private Slider masterVolumeSlider;
    [SerializeField] private Slider musicVolumeSlider;
    [SerializeField] private Slider sfxVolumeSlider;
    [SerializeField] private TMP_Dropdown qualityDropdown;
    [SerializeField] private TMP_Dropdown resolutionDropdown;
    [SerializeField] private Toggle fullscreenToggle;

    [Header("Confirm New Game Panel")]
    [SerializeField] private Button confirmYesButton;
    [SerializeField] private Button confirmNoButton;
    [SerializeField] private TextMeshProUGUI confirmText;

    [Header("Scene Settings")]
    [SerializeField] private string gameSceneName = SceneFlow.MainGameScene;
    [SerializeField] private int gameSceneIndex = 2; // 0=Splash, 1=Menu, 2=Game
    [SerializeField] private bool useSceneIndex = false;

    [Header("Visual Feedback")]
    [SerializeField] private TextMeshProUGUI continueInfoText;
    [SerializeField] private CanvasGroup fadeOverlay;
    [SerializeField] private float fadeDuration = 0.5f;

    [Header("Coin Toss Animation")]
    [SerializeField] private MenuCoinToss coinToss;
    [Tooltip("The designed menu's 2D coin (card table). Used instead of the 3D toss when it's there.")]
    [SerializeField] private MenuCoinFlip2D coinFlip2D;
    [SerializeField] private GameObject coinTossUI; // Panel/canvas that shows during toss
    [SerializeField] private GameObject mainMenuUI; // Main menu buttons to hide during toss
    [SerializeField] private float delayAfterSuccess = 1.5f; // Wait after success before loading
    [SerializeField] private float delayAfterFail = 1f; // Wait after fail before retry

    [Header("Coin Toss Result UI")]
    [SerializeField] private TextMeshProUGUI resultText; // Shows "SUCCESS" or "FAILURE"
    [SerializeField] private Button retryButton; // Button to retry after failure
    [SerializeField] private CanvasGroup resultCanvasGroup; // For fading result text
    [SerializeField] private Color successColor = new Color(0.2f, 0.9f, 0.3f); // Green
    [SerializeField] private Color failColor = new Color(0.9f, 0.2f, 0.2f); // Red
    [SerializeField] private float resultFadeInDuration = 0.5f;
    [SerializeField] private string successText = "SUCCESS";
    [SerializeField] private string failText = "FAILURE";

    // PlayerPrefs of the old company, cleared by New Game
    private static readonly string[] CompanyPrefsKeys =
    {
        "CompanyName", "HasSetCompanyName", "BillionNotificationDismissed",
        "ChosenMapId", "ChosenMapName", "HasUnlockedOnlineWorld"
    };

    private Resolution[] resolutions;
    private bool hasSaveFile = false;
    private bool isBusy = false;          // A coin toss or scene change is running
    private bool awaitingRetry = false;   // The last toss failed; Retry (or New Game) tosses again
    private int failedFlips = 0;          // Failed flips since the menu opened (each one raises the odds)

    public bool IsBusy => isBusy;

    /// <summary>The coin New Game tosses: the 2D coin when it's there, else the 3D toss (null when neither is).</summary>
    IMenuCoin Coin
    {
        get
        {
            if (coinFlip2D != null && coinFlip2D.isActiveAndEnabled) return coinFlip2D;
            if (coinToss != null) return coinToss;
            return null;
        }
    }

    /// <summary>Unity-aware null check through the interface (a destroyed coin counts as gone).</summary>
    static bool Alive(IMenuCoin coin) => coin is Object o && o != null;

    void Start()
    {
        // Check for existing save
        CheckForSaveFile();
        // The cloud save may replace or close the local save while the menu is open (a newer save from another PC)
        CloudSaveSync.LocalSaveChanged += OnLocalSaveChanged;

        // Setup button listeners
        SetupButtons();

        // Setup settings
        SetupSettings();
        GameSettings.Changed += RefreshSettingsUI;
        L10n.LanguageChanged += OnLanguageChanged;

        // Hide panels
        if (settingsPanel != null) settingsPanel.SetActive(false);
        if (confirmNewGamePanel != null) confirmNewGamePanel.SetActive(false);

        // Fade in
        if (fadeOverlay != null)
        {
            fadeOverlay.alpha = 1f;
            StartCoroutine(FadeIn());
        }

        // Setup retry button
        if (retryButton != null)
        {
            retryButton.onClick.AddListener(OnRetryClicked);
            retryButton.gameObject.SetActive(false);
        }

        // Hide result text initially
        HideResultText();

        // Saved volume (also applied at startup by GameSettings)
        GameSettings.ApplyAudio();

        // Start music when menu loads (not during splash video)
        if (MusicManager.Instance != null && !MusicManager.Instance.IsPlaying())
        {
            MusicManager.Instance.PlayMainTheme();
        }

        SetMenuInteractable(true);

        // Demo: New company on the demo's end card or Game Over starts the next company right away, without the toss
        if (DemoMode.Active && PlayerPrefs.GetInt(DemoDirector.SkipFundingTossKey, 0) == 1)
        {
            PlayerPrefs.DeleteKey(DemoDirector.SkipFundingTossKey);
            PlayerPrefs.Save();
            isBusy = true;
            SetMenuInteractable(false);
            StartCoroutine(DemoNewGameWithoutToss());
        }
    }

    // Demo: after the menu's fade-in, straight into a new company
    IEnumerator DemoNewGameWithoutToss()
    {
        // FadeIn ends at alpha 0; the fade-out must not start while it still runs
        float waited = 0f;
        while (fadeOverlay != null && fadeOverlay.alpha > 0f && waited < fadeDuration + 1f)
        {
            waited += Time.unscaledDeltaTime;
            yield return null;
        }
        yield return StartCoroutine(StartNewGameCoroutine());
    }

    // The quality presets in the new language (the dropdown's index stays the quality level)
    void OnLanguageChanged()
    {
        if (qualityDropdown == null) return;
        qualityDropdown.ClearOptions();
        qualityDropdown.AddOptions(CompanyText.QualityNames());
        qualityDropdown.SetValueWithoutNotify(GameSettings.QualityLevel);
        qualityDropdown.RefreshShownValue();
    }

    void OnDestroy()
    {
        GameSettings.Changed -= RefreshSettingsUI;
        L10n.LanguageChanged -= OnLanguageChanged;
        CloudSaveSync.LocalSaveChanged -= OnLocalSaveChanged;
        if (UIManager.Exists)
        {
            if (settingsPanel != null) UIManager.Instance.ClosePanel(settingsPanel);
            if (confirmNewGamePanel != null) UIManager.Instance.ClosePanel(confirmNewGamePanel);
        }
    }

    /// <summary>The cloud save replaced, closed or restored the local save: show what Continue loads now.</summary>
    void OnLocalSaveChanged()
    {
        CheckForSaveFile();
        if (!isBusy) SetMenuInteractable(true);
    }

    void CheckForSaveFile()
    {
        // Local save, or a named company
        hasSaveFile = SaveSystem.LocalSaveExists()
            || PlayerPrefs.HasKey("CompanyName");

        // Show save info
        if (continueInfoText != null)
        {
            if (SaveSystem.TryReadLocalSave(out SaveData data) && data.currentDay > 0)
            {
                string companyName = PlayerPrefs.GetString("CompanyName",
                    string.IsNullOrEmpty(data.companyName) ? "Unknown Company" : data.companyName);
                continueInfoText.text = $"{companyName} - Day {data.currentDay} - ${data.companyValue:N0}";
            }
            else
            {
                continueInfoText.text = hasSaveFile ? "Save file found" : "No save file";
            }
        }
    }

    void SetupButtons()
    {
        if (newGameButton != null)
            newGameButton.onClick.AddListener(OnNewGameClicked);

        if (continueButton != null)
            continueButton.onClick.AddListener(OnContinueClicked);

        if (settingsButton != null)
            settingsButton.onClick.AddListener(OnSettingsClicked);

        if (quitButton != null)
            quitButton.onClick.AddListener(OnQuitClicked);

        if (settingsCloseButton != null)
            settingsCloseButton.onClick.AddListener(CloseSettings);

        if (confirmYesButton != null)
            confirmYesButton.onClick.AddListener(ConfirmNewGame);

        if (confirmNoButton != null)
            confirmNoButton.onClick.AddListener(CancelNewGame);
    }

    /// <summary>Enable or disable the menu buttons (off while a toss or scene change runs).</summary>
    void SetMenuInteractable(bool interactable)
    {
        if (newGameButton != null) newGameButton.interactable = interactable;
        if (settingsButton != null) settingsButton.interactable = interactable;
        if (quitButton != null) quitButton.interactable = interactable;
        if (confirmYesButton != null) confirmYesButton.interactable = interactable;
        if (confirmNoButton != null) confirmNoButton.interactable = interactable;

        if (continueButton != null)
        {
            bool canContinue = interactable && hasSaveFile;
            continueButton.interactable = canContinue;

            // Gray out text if no save
            var buttonText = continueButton.GetComponentInChildren<TextMeshProUGUI>();
            if (buttonText != null)
            {
                buttonText.color = hasSaveFile ? Color.white : new Color(0.5f, 0.5f, 0.5f, 0.5f);
            }
        }
    }

    void SetupSettings()
    {
        // The player's language: a copy of the Quality dropdown in the Settings footer (LanguagePicker)
        LanguagePicker.Setup(null, qualityDropdown);

        // Quality
        if (qualityDropdown != null)
        {
            qualityDropdown.ClearOptions();
            qualityDropdown.AddOptions(CompanyText.QualityNames());   // the presets in the current language
            qualityDropdown.onValueChanged.AddListener(SetQuality);
        }

        // Resolutions
        if (resolutionDropdown != null)
        {
            resolutions = Screen.resolutions;
            resolutionDropdown.ClearOptions();

            var options = new List<string>();
            for (int i = 0; i < resolutions.Length; i++)
            {
                options.Add($"{resolutions[i].width} x {resolutions[i].height} @ {resolutions[i].refreshRateRatio.value:F0}Hz");
            }

            resolutionDropdown.AddOptions(options);
            resolutionDropdown.onValueChanged.AddListener(SetResolution);
        }

        // Fullscreen
        if (fullscreenToggle != null)
        {
            fullscreenToggle.onValueChanged.AddListener(SetFullscreen);
        }

        // Volume sliders (values are set before listening, so nothing is re-saved here)
        if (masterVolumeSlider != null)
        {
            masterVolumeSlider.onValueChanged.AddListener(SetMasterVolume);
        }

        if (musicVolumeSlider != null)
        {
            musicVolumeSlider.onValueChanged.AddListener(SetMusicVolume);
        }

        if (sfxVolumeSlider != null)
        {
            sfxVolumeSlider.onValueChanged.AddListener(SetSFXVolume);
        }

        RefreshSettingsUI();
    }

    /// <summary>Show the current settings without triggering the change handlers.</summary>
    void RefreshSettingsUI()
    {
        if (qualityDropdown != null)
            qualityDropdown.SetValueWithoutNotify(GameSettings.QualityLevel);

        if (resolutionDropdown != null && resolutions != null && resolutions.Length > 0)
        {
            int index = GameSettings.ResolutionIndex;
            if (index < 0)
            {
                // No choice yet: show the current screen size
                index = 0;
                for (int i = 0; i < resolutions.Length; i++)
                {
                    if (resolutions[i].width == Screen.width && resolutions[i].height == Screen.height)
                        index = i;
                }
            }
            resolutionDropdown.SetValueWithoutNotify(Mathf.Clamp(index, 0, resolutions.Length - 1));
        }

        if (fullscreenToggle != null)
            fullscreenToggle.SetIsOnWithoutNotify(Screen.fullScreen);

        if (masterVolumeSlider != null)
            masterVolumeSlider.SetValueWithoutNotify(GameSettings.MasterVolume);
        if (musicVolumeSlider != null)
            musicVolumeSlider.SetValueWithoutNotify(GameSettings.MusicVolume);
        if (sfxVolumeSlider != null)
            sfxVolumeSlider.SetValueWithoutNotify(GameSettings.SfxVolume);
    }

    #region Button Handlers

    void OnNewGameClicked()
    {
        if (isBusy) return;

        // Already confirmed, the last toss just failed: toss again
        if (awaitingRetry)
        {
            StartNewGame();
            return;
        }

        if (hasSaveFile && confirmNewGamePanel != null)
        {
            // Show confirmation if there's existing save
            confirmNewGamePanel.SetActive(true);
            UIManager.BringToFront(confirmNewGamePanel);
            UIManager.Instance.OpenPanel(confirmNewGamePanel, false, CancelNewGame);
            if (confirmText != null)
            {
                confirmText.text = "Starting a new game will DELETE your current save.\n\nAre you sure?";
            }
        }
        else
        {
            // No save (or no confirmation panel), just start
            StartNewGame();
        }
    }

    void ConfirmNewGame()
    {
        if (isBusy) return;
        HideConfirmPanel();
        StartNewGame();
    }

    void CancelNewGame()
    {
        HideConfirmPanel();
    }

    void HideConfirmPanel()
    {
        if (confirmNewGamePanel != null)
        {
            confirmNewGamePanel.SetActive(false);
            if (UIManager.Exists) UIManager.Instance.ClosePanel(confirmNewGamePanel);
        }
    }

    void StartNewGame()
    {
        if (isBusy) return;

        HideRetryButton();
        HideResultText();

        // If coin toss is set up, use it!
        if (Coin != null)
        {
            StartCoroutine(CoinTossNewGameSequence());
        }
        else
        {
            StartCoroutine(StartNewGameCoroutine());
        }
    }

    IEnumerator CoinTossNewGameSequence()
    {
        isBusy = true;
        awaitingRetry = false;
        SetMenuInteractable(false);

        Debug.Log("[MainMenu] Starting coin toss for New Game...");

        // Clean up the coin of an earlier toss
        IMenuCoin coin = Coin;
        coin.CleanupCoin();

        // Start the toss (the odds improve with every failed flip)
        coin.StartCoinToss(FlipChance());

        // Wait for toss to complete
        while (Alive(coin) && coin.IsTossing)
        {
            yield return null;
        }

        bool success = Alive(coin) && coin.LastResultWasSuccess;

        if (success)
        {
            Debug.Log("[MainMenu] Coin toss SUCCESS! Starting game...");

            // Show SUCCESS text with fade in
            yield return StartCoroutine(ShowResultText(successText, successColor));

            yield return new WaitForSecondsRealtime(delayAfterSuccess);

            // Clean up coin
            if (Alive(coin)) coin.CleanupCoin();

            // Now proceed with the actual new game setup (keeps the menu disabled)
            yield return StartCoroutine(StartNewGameCoroutine());
        }
        else
        {
            failedFlips++;
            Debug.Log("[MainMenu] Coin toss FAIL! Waiting for retry... (next flip: " + OddsText(FlipChance()) + ")");

            // Show FAILURE text with fade in, and the better odds of the next flip
            yield return StartCoroutine(ShowResultText(failText + "\n<size=55%>" + NextFlipLine() + "</size>", failColor));

            yield return new WaitForSecondsRealtime(delayAfterFail);

            // Retry (or New Game) tosses again; the rest of the menu works meanwhile
            awaitingRetry = true;
            if (retryButton != null)
            {
                retryButton.gameObject.SetActive(true);
                retryButton.interactable = true;
            }
            isBusy = false;
            SetMenuInteractable(true);
        }
    }

    void OnRetryClicked()
    {
        if (isBusy) return;
        StartNewGame();
    }

    void HideRetryButton()
    {
        awaitingRetry = false;
        if (retryButton != null)
            retryButton.gameObject.SetActive(false);
    }

    /// <summary>
    /// Odds of the next flip: the Inspector's chance first (1 in 3 in the menu scene), then at least 1 in 2,
    /// then the bank gives in.
    /// </summary>
    float FlipChance()
    {
        IMenuCoin coin = Coin;
        float baseChance = coin != null ? Mathf.Clamp01(coin.BaseSuccessChance) : 1f;
        if (DemoMode.Active) return failedFlips <= 0 ? DemoMode.Config.fundingFirstFlipChance : 1f;   // Demo: 1 in 2, then sure
        if (failedFlips <= 0) return baseChance;
        if (failedFlips == 1) return Mathf.Max(baseChance, 0.5f);
        return 1f;
    }

    /// <summary>
    /// The tails slip's line (MainMenuView shows what follows the first line break of the result text) in the current
    /// language: "Next flip: 1 in 2 gets you funded." (Company table, menu.flip.next.*).
    /// </summary>
    string NextFlipLine()
    {
        float next = FlipChance();
        if (next >= 1f) return CompanyText.Get("menu.flip.next.sure");
        if (next > 0f)
        {
            float oneIn = 1f / next;
            int rounded = Mathf.RoundToInt(oneIn);
            if (Mathf.Abs(oneIn - rounded) < 0.1f)
                return CompanyText.Get("menu.flip.next.one_in", ("count", rounded.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
        return CompanyText.Get("menu.flip.next.percent", ("percent", CompanyText.Percent(Mathf.Clamp01(next))));
    }

    /// <summary>0.33 -> "1 in 3", 0.5 -> "1 in 2", anything else as a percentage (English, for the Console).</summary>
    static string OddsText(float chance)
    {
        if (chance >= 1f) return "a sure thing";
        if (chance <= 0f) return "no chance";
        float oneIn = 1f / chance;
        int rounded = Mathf.RoundToInt(oneIn);
        if (Mathf.Abs(oneIn - rounded) < 0.1f) return "1 in " + rounded;
        return Mathf.RoundToInt(chance * 100f) + "%";
    }

    IEnumerator ShowResultText(string text, Color color)
    {
        if (resultText == null) yield break;

        resultText.text = text;
        resultText.color = color;
        resultText.gameObject.SetActive(true);

        // Fade in
        if (resultCanvasGroup != null)
        {
            resultCanvasGroup.alpha = 0f;
            float elapsed = 0f;

            while (elapsed < resultFadeInDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                resultCanvasGroup.alpha = Mathf.Lerp(0f, 1f, elapsed / resultFadeInDuration);
                yield return null;
            }
            resultCanvasGroup.alpha = 1f;
        }
    }

    void HideResultText()
    {
        if (resultText != null)
        {
            resultText.gameObject.SetActive(false);
        }
        if (resultCanvasGroup != null)
        {
            resultCanvasGroup.alpha = 0f;
        }
    }

    IEnumerator StartNewGameCoroutine()
    {
        isBusy = true;
        SetMenuInteractable(false);

        Debug.Log("[MainMenu] Starting New Game - deleting the old save...");

        // Local save (static: works even before the game scene created a SaveSystem). Another Steam user's save on a
        // shared PC is set aside instead of deleted; when it can't be, nothing is deleted and New Game stops (with a toast).
        if (!CloudSaveSync.ClearLocalSaveForNewGame())
        {
            HideResultText();
            isBusy = false;
            SetMenuInteractable(true);
            yield break;
        }

        // Forget the old game in memory (only exists after visiting the game). Keeps autosave working.
        if (SaveSystem.Instance != null)
            SaveSystem.Instance.PrepareForNewGame();

        // PlayerPrefs of the old company
        foreach (string key in CompanyPrefsKeys)
            PlayerPrefs.DeleteKey(key);

        // Cloud save: a new run (company) starts; the server registers it at the next sync
        CloudSaveSync.BeginNewRun();

        // Online City: the new company has to reach $1B itself (the City account itself is online and stays)
        CityOnline.BeginNewGame();

        // Demo: a kiosk build shows the tour to every new company
        if (DemoMode.Active && DemoMode.Config.alwaysShowTour)
            FeatureGate.ResetProfile();

        // Mark as new game so SaveSystem doesn't try to load
        PlayerPrefs.SetInt(SaveSystem.NewGameFlagKey, 1);
        PlayerPrefs.Save();

        Debug.Log("[MainMenu] All data cleared - starting fresh game!");

        // Small delay to ensure everything is processed
        yield return new WaitForSecondsRealtime(0.2f);

        // Load game scene
        yield return StartCoroutine(LoadSceneWithFade());
    }

    void OnContinueClicked()
    {
        if (isBusy) return;

        if (!hasSaveFile)
        {
            Debug.LogWarning("[MainMenu] No save file to continue from!");
            return;
        }

        Debug.Log("[MainMenu] Continuing game...");

        // A failed toss may still be on screen
        HideRetryButton();
        HideResultText();
        IMenuCoin coin = Coin;
        if (coin != null) coin.CleanupCoin();

        // Make sure new game flag is cleared
        PlayerPrefs.DeleteKey(SaveSystem.NewGameFlagKey);
        PlayerPrefs.Save();

        StartCoroutine(LoadSceneWithFade());
    }

    void OnSettingsClicked()
    {
        if (isBusy || settingsPanel == null) return;

        RefreshSettingsUI();
        settingsPanel.SetActive(true);
        UIManager.BringToFront(settingsPanel);
        // The menu never pauses (the coin and dice use physics)
        UIManager.Instance.OpenPanel(settingsPanel, false, CloseSettings);
    }

    void CloseSettings()
    {
        if (settingsPanel != null)
        {
            settingsPanel.SetActive(false);
            if (UIManager.Exists) UIManager.Instance.ClosePanel(settingsPanel);
        }

        // Save settings
        GameSettings.Save();
    }

    void OnQuitClicked()
    {
        if (isBusy) return;

        Debug.Log("[MainMenu] Quitting game...");
        GameSettings.Save();

        #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
        #else
            Application.Quit();
        #endif
    }

    #endregion

    #region Settings Handlers

    void SetQuality(int qualityIndex)
    {
        GameSettings.QualityLevel = qualityIndex;
    }

    void SetResolution(int resolutionIndex)
    {
        if (resolutions == null || resolutionIndex < 0 || resolutionIndex >= resolutions.Length) return;
        GameSettings.ResolutionIndex = resolutionIndex;
    }

    void SetFullscreen(bool isFullscreen)
    {
        GameSettings.Fullscreen = isFullscreen;
    }

    void SetMasterVolume(float volume)
    {
        GameSettings.MasterVolume = volume;
    }

    void SetMusicVolume(float volume)
    {
        GameSettings.MusicVolume = volume;
    }

    void SetSFXVolume(float volume)
    {
        GameSettings.SfxVolume = volume;
    }

    #endregion

    #region Scene Loading

    string GameSceneName()
    {
        if (useSceneIndex)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(gameSceneIndex);
            if (!string.IsNullOrEmpty(path))
                return System.IO.Path.GetFileNameWithoutExtension(path);
        }
        return string.IsNullOrEmpty(gameSceneName) ? SceneFlow.MainGameScene : gameSceneName;
    }

    IEnumerator LoadSceneWithFade()
    {
        isBusy = true;
        SetMenuInteractable(false);
        GameSettings.Save();

        // Fade out
        if (fadeOverlay != null)
        {
            float elapsed = 0f;
            while (elapsed < fadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                fadeOverlay.alpha = Mathf.Lerp(0f, 1f, elapsed / fadeDuration);
                yield return null;
            }
            fadeOverlay.alpha = 1f;
        }

        // Load scene (SceneFlow resets UI state; it only saves when a company is loaded)
        string sceneName = GameSceneName();
        if (!SceneFlow.Load(sceneName))
        {
            Debug.LogError($"[MainMenu] Can't load '{sceneName}'. Add it to File > Build Profiles.");
            if (continueInfoText != null)
                continueInfoText.text = $"Can't load {sceneName}";
            if (fadeOverlay != null)
                fadeOverlay.alpha = 0f;
            isBusy = false;
            CheckForSaveFile();
            SetMenuInteractable(true);
        }
    }

    IEnumerator FadeIn()
    {
        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            fadeOverlay.alpha = Mathf.Lerp(1f, 0f, elapsed / fadeDuration);
            yield return null;
        }
        fadeOverlay.alpha = 0f;
    }

    #endregion
}
