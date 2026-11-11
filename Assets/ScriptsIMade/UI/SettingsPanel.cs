using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Pause menu with settings ("PAUSED"). Builds its UI at runtime unless the references are assigned.
/// - Escape opens it through UIManager.EscapeUnhandled (only when no open panel handles Escape) and closes it.
/// - The game is paused while it is open (UIManager owns Time.timeScale).
/// - Volume and display settings go through GameSettings (shared with the main menu and the dice).
/// - Buttons: Save (settings + game), Main Menu (saves first), Close, Quit.
/// </summary>
public class SettingsPanel : MonoBehaviour
{
    [Header("Settings")]
    public bool autoCreateUI = true;
    [Tooltip("Canvas for the generated UI. Empty = the scene's root Screen Space Overlay canvas.")]
    public Canvas targetCanvas;
    [Tooltip("Sorting order of the generated pause menu, so it draws above the HUD and other panels")]
    public int sortingOrder = 100;
    [Tooltip("Seconds the Save feedback stays visible (real time, the game is paused)")]
    public float statusDuration = 2f;

    [Header("References (auto-filled if autoCreateUI)")]
    public GameObject panel;
    public Slider masterVolumeSlider;
    public Slider musicVolumeSlider;
    public Slider sfxVolumeSlider;
    public Toggle fullscreenToggle;
    public TMP_Dropdown qualityDropdown;
    public Button saveButton;
    public Button mainMenuButton;
    public Button quitButton;
    public Button closeButton;
    [Tooltip("Optional text for short feedback such as \"Game saved\"")]
    public TextMeshProUGUI statusText;

    [Header("Designed pause menu (optional; Tools > Game > New UI > Build Pause Menu fills these)")]
    [Tooltip("The pause list (legal pad). With menuView and settingsView set, the menu opens on the list and Settings is a second page.")]
    public GameObject menuView;
    public GameObject settingsView;
    public Button resumeButton;
    public Button openSettingsButton;
    public Button settingsDoneButton;
    public Button settingsCloseButton;
    public Button renameButton;
    public Button resetDefaultsButton;
    public TMP_Dropdown resolutionDropdown;
    [Tooltip("Optional designed language dropdown; without one, a copy of the Quality dropdown goes in the Settings footer (LanguagePicker)")]
    public TMP_Dropdown languageDropdown;
    public TextMeshProUGUI masterValueText;
    public TextMeshProUGUI musicValueText;
    public TextMeshProUGUI sfxValueText;
    public TextMeshProUGUI fullscreenStateText;
    [Tooltip("\"Last saved 12 seconds ago\"")]
    public TextMeshProUGUI lastSavedText;
    public TextMeshProUGUI saveCardName;
    public TextMeshProUGUI saveCardDay;
    public TextMeshProUGUI saveCardValue;
    public TextMeshProUGUI saveCardStaff;
    public TextMeshProUGUI saveCardAutosave;

    private Canvas canvas;
    private bool isOpen = false;
    private bool subscribedToEscape = false;
    private Coroutine statusRoutine;
    private readonly Dictionary<Slider, TextMeshProUGUI> sliderValueLabels = new Dictionary<Slider, TextMeshProUGUI>();

    public bool IsOpen => isOpen;

    // Demo: the pause menu last opened (a destroyed one reads as null)
    static SettingsPanel lastOpened;

    /// <summary>Demo: the pause menu is open right now (DemoDirector's play clock stops while it is).</summary>
    public static bool IsPauseMenuOpen => lastOpened != null && lastOpened.isOpen;

    /// <summary>True while the designed menu shows its Settings page.</summary>
    public bool IsShowingSettings => settingsView != null && settingsView.activeSelf && menuView != null;

    readonly List<int> resolutionOptionToIndex = new List<int>();
    float lastSavedRefresh;

    void Start()
    {
        // The designed pause menu (PauseMenuView) brings its own controls
        var view = panel != null ? panel.GetComponent<PauseMenuView>() : FindAnyObjectByType<PauseMenuView>(FindObjectsInactive.Include);
        if (view != null && view.gameObject.scene == gameObject.scene)
            view.ApplyTo(this);

        if (autoCreateUI && panel == null)
        {
            CreateSettingsUI();
        }

        // The player's language (a designed dropdown, else a copy of Quality in the Settings footer)
        LanguagePicker.Setup(languageDropdown, qualityDropdown);

        if (masterValueText != null) sliderValueLabels[masterVolumeSlider] = masterValueText;
        if (musicValueText != null) sliderValueLabels[musicVolumeSlider] = musicValueText;
        if (sfxValueText != null) sliderValueLabels[sfxVolumeSlider] = sfxValueText;
        FillDropdowns();

        SetupListeners();
        RefreshValues();
        SetStatus("");

        if (panel != null)
            panel.SetActive(false);
        isOpen = false;

        // Escape that no open panel handled toggles this menu
        UIManager.Instance.EscapeUnhandled += OnEscapeUnhandled;
        subscribedToEscape = true;

        GameSettings.Changed += RefreshValues;
        L10n.LanguageChanged += OnLanguageChanged;
    }

    // The texts this menu writes itself (quality presets, On/Off, volumes, the save card) in the new language
    void OnLanguageChanged()
    {
        if (qualityDropdown != null && qualityDropdown.options.Count == QualitySettings.names.Length)
        {
            qualityDropdown.ClearOptions();   // (resets the shown choice without an event: set it back below)
            qualityDropdown.AddOptions(CompanyText.QualityNames());
            qualityDropdown.SetValueWithoutNotify(GameSettings.QualityLevel);
            qualityDropdown.RefreshShownValue();
        }
        if (!isOpen) return;
        RefreshValues();
        RefreshSaveInfo();
    }

    void OnDestroy()
    {
        GameSettings.Changed -= RefreshValues;
        L10n.LanguageChanged -= OnLanguageChanged;

        if (UIManager.Exists)
        {
            if (subscribedToEscape)
                UIManager.Instance.EscapeUnhandled -= OnEscapeUnhandled;
            UIManager.Instance.ClosePanel(this);
        }
    }

    void OnEscapeUnhandled()
    {
        // Escape in a text field only leaves the field
        if (UIManager.IsTypingInInputField()) return;
        TogglePanel();
    }

    public void TogglePanel()
    {
        if (isOpen)
            ClosePanel();
        else
            OpenPanel();
    }

    public void OpenPanel()
    {
        if (panel == null) return;

        RefreshValues();
        SetStatus("");
        ShowMenu();
        RefreshSaveInfo();
        panel.SetActive(true);
        isOpen = true;
        lastOpened = this;   // Demo: IsPauseMenuOpen
        UIManager.BringToFront(panel);
        UIManager.Instance.OpenPanel(this, true, OnEscapeWhileOpen);
    }

    // Escape on the Settings page goes back to the list; on the list it resumes the game
    void OnEscapeWhileOpen()
    {
        if (IsShowingSettings)
            ShowMenu();
        else
            ClosePanel();
    }

    /// <summary>Designed menu: show the pause list.</summary>
    public void ShowMenu()
    {
        if (menuView == null || settingsView == null) return;
        menuView.SetActive(true);
        settingsView.SetActive(false);
        RefreshSaveInfo();
    }

    /// <summary>Designed menu: show the Settings page.</summary>
    public void ShowSettings()
    {
        if (menuView == null || settingsView == null) return;
        RefreshValues();
        menuView.SetActive(false);
        settingsView.SetActive(true);
    }

    void RenameCompany()
    {
        ClosePanel();
        if (CompanyNameUI.Instance != null)
            CompanyNameUI.Instance.OpenNameChange();
    }

    void ResetDefaults()
    {
        GameSettings.MasterVolume = GameSettings.DefaultMasterVolume;
        GameSettings.MusicVolume = GameSettings.DefaultMusicVolume;
        GameSettings.SfxVolume = GameSettings.DefaultSfxVolume;
        RefreshValues();
    }

    void Update()
    {
        // "Last saved 12 seconds ago" keeps counting while the menu is open (real time: the game is paused)
        if (!isOpen || lastSavedText == null) return;
        lastSavedRefresh -= Time.unscaledDeltaTime;
        if (lastSavedRefresh <= 0f)
            RefreshSaveInfo();
    }

    void RefreshSaveInfo()
    {
        lastSavedRefresh = 1f;
        if (lastSavedText != null)
            lastSavedText.text = LastSavedLine();

        var cm = CompanyManager.Instance;
        if (saveCardName != null) saveCardName.text = CompanyNameUI.DisplayName(CompanyNameUI.CurrentCompanyName);   // localization (2026-09-29): the default name in the player's language
        // Texts: the Company table (CompanyText, pause.*); refreshed every second while the menu is open
        if (cm != null)
        {
            if (saveCardDay != null) saveCardDay.text = CompanyText.Get("pause.save_card.day", ("day", cm.currentDay.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            if (saveCardValue != null) saveCardValue.text = UIFormat.Money(cm.companyValue);
            if (saveCardStaff != null) saveCardStaff.text = CompanyText.Get("pause.save_card.staff", ("count", UIFormat.Int(cm.totalEmployees)));
        }
        if (saveCardAutosave != null)
        {
            float every = SaveSystem.Instance != null ? SaveSystem.Instance.autoSaveInterval : 0f;
            saveCardAutosave.text = every > 0f ? CompanyText.Get("pause.autosave.every", ("seconds", Mathf.RoundToInt(every).ToString(System.Globalization.CultureInfo.InvariantCulture)))
                                               : CompanyText.Get("pause.autosave.off");
        }
    }

    static string LastSavedLine()
    {
        string path = SaveSystem.LocalSaveFilePath;
        if (!System.IO.File.Exists(path)) return CompanyText.Get("pause.saved.never");
        double seconds = (System.DateTime.Now - System.IO.File.GetLastWriteTime(path)).TotalSeconds;
        if (seconds < 5) return CompanyText.Get("pause.saved.just_now");
        if (seconds < 60) return CompanyText.Get("pause.saved.seconds", ("count", (int)seconds));
        int minutes = (int)(seconds / 60);
        if (minutes < 60) return CompanyText.Get("pause.saved.minutes", ("count", minutes));
        int hours = minutes / 60;
        return CompanyText.Get("pause.saved.hours", ("count", hours));
    }

    /// <summary>Designed dropdowns start empty: fill quality levels and screen sizes here.</summary>
    void FillDropdowns()
    {
        if (qualityDropdown != null && qualityDropdown.options.Count == 0)
            qualityDropdown.AddOptions(CompanyText.QualityNames());   // the presets in the current language

        if (resolutionDropdown != null)
        {
            resolutionDropdown.ClearOptions();
            resolutionOptionToIndex.Clear();
            var labels = new List<string>();
            Resolution[] list = Screen.resolutions;
            // one entry per size (the list repeats sizes per refresh rate; the last one has the highest rate)
            for (int i = list.Length - 1; i >= 0; i--)
            {
                string label = list[i].width + " \u00D7 " + list[i].height;
                if (labels.Contains(label)) continue;
                labels.Add(label);
                resolutionOptionToIndex.Add(i);
            }
            resolutionDropdown.AddOptions(labels);
        }
    }

    void OnResolutionChanged(int option)
    {
        if (option >= 0 && option < resolutionOptionToIndex.Count)
            GameSettings.ResolutionIndex = resolutionOptionToIndex[option];
    }

    public void ClosePanel()
    {
        if (panel != null)
            panel.SetActive(false);
        isOpen = false;

        if (UIManager.Exists)
            UIManager.Instance.ClosePanel(this);

        GameSettings.Save();
    }

    void SetupListeners()
    {
        if (masterVolumeSlider != null)
            masterVolumeSlider.onValueChanged.AddListener(OnMasterVolumeChanged);
        if (musicVolumeSlider != null)
            musicVolumeSlider.onValueChanged.AddListener(OnMusicVolumeChanged);
        if (sfxVolumeSlider != null)
            sfxVolumeSlider.onValueChanged.AddListener(OnSFXVolumeChanged);
        if (fullscreenToggle != null)
            fullscreenToggle.onValueChanged.AddListener(OnFullscreenChanged);
        if (qualityDropdown != null)
            qualityDropdown.onValueChanged.AddListener(OnQualityChanged);
        if (saveButton != null)
            saveButton.onClick.AddListener(SaveSettings);
        if (mainMenuButton != null)
            mainMenuButton.onClick.AddListener(GoToMainMenu);
        if (quitButton != null)
            quitButton.onClick.AddListener(QuitGame);
        if (closeButton != null)
            closeButton.onClick.AddListener(ClosePanel);

        if (resumeButton != null)
            resumeButton.onClick.AddListener(ClosePanel);
        if (openSettingsButton != null)
            openSettingsButton.onClick.AddListener(ShowSettings);
        if (settingsDoneButton != null)
            settingsDoneButton.onClick.AddListener(ShowMenu);
        if (settingsCloseButton != null)
            settingsCloseButton.onClick.AddListener(ShowMenu);
        if (renameButton != null)
            renameButton.onClick.AddListener(RenameCompany);
        if (resetDefaultsButton != null)
            resetDefaultsButton.onClick.AddListener(ResetDefaults);
        if (resolutionDropdown != null)
            resolutionDropdown.onValueChanged.AddListener(OnResolutionChanged);
    }

    /// <summary>Show the current settings without triggering the change handlers.</summary>
    void RefreshValues()
    {
        SetSliderValue(masterVolumeSlider, GameSettings.MasterVolume);
        SetSliderValue(musicVolumeSlider, GameSettings.MusicVolume);
        SetSliderValue(sfxVolumeSlider, GameSettings.SfxVolume);

        if (fullscreenToggle != null)
            fullscreenToggle.SetIsOnWithoutNotify(GameSettings.Fullscreen);

        if (qualityDropdown != null)
        {
            qualityDropdown.SetValueWithoutNotify(GameSettings.QualityLevel);
            qualityDropdown.RefreshShownValue();
        }

        if (fullscreenStateText != null)
            fullscreenStateText.text = CompanyText.Get(GameSettings.Fullscreen ? "pause.on" : "pause.off");

        if (resolutionDropdown != null && resolutionOptionToIndex.Count > 0)
        {
            int index = GameSettings.ResolutionIndex;
            int option = index >= 0 ? resolutionOptionToIndex.IndexOf(index) : -1;
            if (option < 0)
            {
                // never chosen: show the current window size
                Resolution[] list = Screen.resolutions;
                for (int o = 0; o < resolutionOptionToIndex.Count && option < 0; o++)
                {
                    Resolution r = list[resolutionOptionToIndex[o]];
                    if (r.width == Screen.width && r.height == Screen.height) option = o;
                }
            }
            if (option >= 0)
            {
                resolutionDropdown.SetValueWithoutNotify(option);
                resolutionDropdown.RefreshShownValue();
            }
        }
    }

    void SetSliderValue(Slider slider, float value)
    {
        if (slider == null) return;
        slider.SetValueWithoutNotify(value);
        UpdateValueLabel(slider, value);
    }

    void UpdateValueLabel(Slider slider, float value)
    {
        if (slider != null && sliderValueLabels.TryGetValue(slider, out var label) && label != null)
            label.text = CompanyText.Get("pause.volume", ("value", Mathf.RoundToInt(value * 100f).ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    /// <summary>Save settings and the game, with brief feedback.</summary>
    void SaveSettings()
    {
        GameSettings.Save();

        if (SaveSystem.Instance == null)
        {
            SetStatus(CompanyText.Get("pause.status.settings_saved"));
            return;
        }

        bool saved = SaveSystem.Instance.SaveGame();
        SetStatus(CompanyText.Get(saved ? "pause.status.game_saved" : "pause.status.game_not_saved"));
        if (lastSavedText != null)
            lastSavedText.text = CompanyText.Get(saved ? "pause.saved.just_now" : "pause.saved.cant");
        lastSavedRefresh = 3f;
        Debug.Log(saved ? "[Settings] Settings and game saved" : "[Settings] Settings saved; the game wasn't saved");
    }

    void GoToMainMenu()
    {
        GameSettings.Save();

        // SceneFlow saves the game first and resets the UI state
        if (!SceneFlow.Load(SceneFlow.MainMenuScene))
            SetStatus(CompanyText.Get("pause.status.no_menu_scene"));
    }

    void SetStatus(string message)
    {
        if (statusText == null) return;

        if (statusRoutine != null)
        {
            StopCoroutine(statusRoutine);
            statusRoutine = null;
        }

        // Only the text changes, so the layout doesn't jump
        statusText.text = message ?? "";
        if (!string.IsNullOrEmpty(message) && isActiveAndEnabled)
            statusRoutine = StartCoroutine(HideStatusAfterDelay());
    }

    IEnumerator HideStatusAfterDelay()
    {
        // Real time: the game is paused while this menu is open
        yield return new WaitForSecondsRealtime(statusDuration);
        if (statusText != null)
            statusText.text = "";
        statusRoutine = null;
    }

    void OnMasterVolumeChanged(float value)
    {
        GameSettings.MasterVolume = value;
        UpdateValueLabel(masterVolumeSlider, value);
    }

    void OnMusicVolumeChanged(float value)
    {
        GameSettings.MusicVolume = value;
        UpdateValueLabel(musicVolumeSlider, value);
    }

    void OnSFXVolumeChanged(float value)
    {
        GameSettings.SfxVolume = value;
        UpdateValueLabel(sfxVolumeSlider, value);
    }

    void OnFullscreenChanged(bool value)
    {
        GameSettings.Fullscreen = value;
        if (fullscreenStateText != null)
            fullscreenStateText.text = CompanyText.Get(value ? "pause.on" : "pause.off");
    }

    void OnQualityChanged(int value)
    {
        GameSettings.QualityLevel = value;
    }

    void QuitGame()
    {
        GameSettings.Save();

        // SaveSystem saves on quit; save here only if that is switched off
        if (SaveSystem.Instance != null && !SaveSystem.Instance.autoSaveOnQuit)
            SaveSystem.Instance.SaveGame();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ------------------------------------------------------------------
    // Runtime UI
    // ------------------------------------------------------------------

    /// <summary>The root Screen Space Overlay canvas of this scene (or a new one).</summary>
    Canvas FindTargetCanvas()
    {
        if (targetCanvas != null) return targetCanvas;

        Canvas best = null;
        foreach (var candidate in FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (candidate == null || !candidate.isRootCanvas || candidate.renderMode != RenderMode.ScreenSpaceOverlay) continue;
            if (candidate.gameObject.scene != gameObject.scene) continue;

            bool interactive = candidate.GetComponent<GraphicRaycaster>() != null;
            bool bestInteractive = best != null && best.GetComponent<GraphicRaycaster>() != null;
            if (best == null || (interactive && !bestInteractive) ||
                (interactive == bestInteractive && candidate.sortingOrder > best.sortingOrder))
                best = candidate;
        }
        if (best != null) return best;

        // No overlay canvas in the scene: make one
        GameObject canvasObj = new GameObject("PauseMenuCanvas");
        Canvas created = canvasObj.AddComponent<Canvas>();
        created.renderMode = RenderMode.ScreenSpaceOverlay;
        created.sortingOrder = sortingOrder;
        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        canvasObj.AddComponent<GraphicRaycaster>();
        return created;
    }

    void CreateSettingsUI()
    {
        canvas = FindTargetCanvas();
        if (canvas == null)
        {
            Debug.LogWarning("[SettingsPanel] No Canvas found!");
            return;
        }

        // Full-screen root: dims the game and blocks clicks on the HUD while paused.
        // Its own sorting keeps it above the HUD and other panels.
        panel = new GameObject("PauseMenu");
        panel.transform.SetParent(canvas.transform, false);
        RectTransform rootRect = panel.AddComponent<RectTransform>();
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;
        Canvas overlay = panel.AddComponent<Canvas>();
        overlay.overrideSorting = true;
        overlay.sortingOrder = sortingOrder;
        panel.AddComponent<GraphicRaycaster>();
        Image dim = panel.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.55f);

        // Main window
        GameObject window = CreatePanel("SettingsPanel", new Vector2(500, 440));
        window.transform.SetParent(panel.transform, false);

        // Title
        CreateLabel(window.transform, "PAUSED", 28, new Color(0.9f, 0.75f, 0.3f), 40);

        // Feedback line (e.g. "Game saved")
        statusText = CreateLabel(window.transform, "", 15, new Color(0.6f, 0.9f, 0.6f), 22);
        statusText.fontStyle = FontStyles.Normal;

        // Separator
        CreateSeparator(window.transform);

        // Master Volume
        masterVolumeSlider = CreateSliderRow(window.transform, "Master Volume", 0f, 1f, GameSettings.MasterVolume);

        // Music Volume
        musicVolumeSlider = CreateSliderRow(window.transform, "Music Volume", 0f, 1f, GameSettings.MusicVolume);

        // SFX Volume
        sfxVolumeSlider = CreateSliderRow(window.transform, "SFX Volume", 0f, 1f, GameSettings.SfxVolume);

        // Separator
        CreateSeparator(window.transform);

        // Fullscreen Toggle
        fullscreenToggle = CreateToggleRow(window.transform, "Fullscreen");
        fullscreenToggle.SetIsOnWithoutNotify(GameSettings.Fullscreen);

        // Quality Dropdown
        qualityDropdown = CreateDropdownRow(window.transform, "Quality", QualitySettings.names);
        qualityDropdown.SetValueWithoutNotify(GameSettings.QualityLevel);

        // Language (LanguagePicker fills it with the languages' own names and switches the game)
        if (languageDropdown == null)
            languageDropdown = CreateDropdownRow(window.transform, "Language", new string[0]);

        // Separator
        CreateSeparator(window.transform);

        // Buttons row
        GameObject btnRow = new GameObject("Buttons");
        btnRow.transform.SetParent(window.transform, false);
        RectTransform btnRowRect = btnRow.AddComponent<RectTransform>();
        btnRowRect.sizeDelta = new Vector2(0, 45);

        HorizontalLayoutGroup hlg = btnRow.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 10;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = true;
        hlg.padding = new RectOffset(0, 0, 5, 5);

        LayoutElement btnRowLE = btnRow.AddComponent<LayoutElement>();
        btnRowLE.preferredHeight = 45;

        saveButton = CreateButton(btnRow.transform, "Save", new Color(0.2f, 0.55f, 0.3f));
        mainMenuButton = CreateButton(btnRow.transform, "Main Menu", new Color(0.25f, 0.35f, 0.6f));
        closeButton = CreateButton(btnRow.transform, "Close", new Color(0.4f, 0.4f, 0.45f));
        quitButton = CreateButton(btnRow.transform, "Quit", new Color(0.6f, 0.2f, 0.2f));

        panel.SetActive(false);
    }

    GameObject CreatePanel(string name, Vector2 size)
    {
        GameObject panelObj = new GameObject(name);
        panelObj.transform.SetParent(canvas.transform, false);

        RectTransform rect = panelObj.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;

        Image bg = panelObj.AddComponent<Image>();
        bg.color = new Color(0.08f, 0.08f, 0.1f, 0.97f);

        // Add outline
        Outline outline = panelObj.AddComponent<Outline>();
        outline.effectColor = new Color(0.9f, 0.75f, 0.3f, 0.6f);
        outline.effectDistance = new Vector2(2, 2);

        VerticalLayoutGroup vlg = panelObj.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(20, 20, 15, 15);
        vlg.spacing = 8;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true; // Rows use their LayoutElement heights
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        return panelObj;
    }

    TextMeshProUGUI CreateLabel(Transform parent, string text, int fontSize, Color color, float height)
    {
        GameObject obj = new GameObject("Label");
        obj.transform.SetParent(parent, false);

        LayoutElement le = obj.AddComponent<LayoutElement>();
        le.preferredHeight = height;

        TextMeshProUGUI tmp = obj.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontStyle = FontStyles.Bold;
        tmp.raycastTarget = false;

        return tmp;
    }

    void CreateSeparator(Transform parent)
    {
        GameObject obj = new GameObject("Separator");
        obj.transform.SetParent(parent, false);

        LayoutElement le = obj.AddComponent<LayoutElement>();
        le.preferredHeight = 2;
        le.flexibleWidth = 1;

        Image img = obj.AddComponent<Image>();
        img.color = new Color(0.4f, 0.35f, 0.2f, 0.5f);
        img.raycastTarget = false;
    }

    Slider CreateSliderRow(Transform parent, string label, float min, float max, float defaultVal)
    {
        GameObject row = new GameObject(label.Replace(" ", "") + "Row");
        row.transform.SetParent(parent, false);

        LayoutElement rowLE = row.AddComponent<LayoutElement>();
        rowLE.preferredHeight = 35;

        HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;
        hlg.spacing = 10;

        // Label
        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(row.transform, false);
        TextMeshProUGUI labelTmp = labelObj.AddComponent<TextMeshProUGUI>();
        labelTmp.text = label;
        labelTmp.fontSize = 16;
        labelTmp.color = Color.white;
        labelTmp.alignment = TextAlignmentOptions.Left;
        labelTmp.raycastTarget = false;
        LayoutElement labelLE = labelObj.AddComponent<LayoutElement>();
        labelLE.preferredWidth = 130;

        // Slider
        GameObject sliderObj = new GameObject("Slider");
        sliderObj.transform.SetParent(row.transform, false);

        LayoutElement sliderLE = sliderObj.AddComponent<LayoutElement>();
        sliderLE.flexibleWidth = 1;
        sliderLE.preferredHeight = 25;

        Image sliderBg = sliderObj.AddComponent<Image>();
        sliderBg.color = new Color(0.15f, 0.15f, 0.18f);

        Slider slider = sliderObj.AddComponent<Slider>();
        slider.minValue = min;
        slider.maxValue = max;
        slider.SetValueWithoutNotify(defaultVal);

        // Fill area
        GameObject fillArea = new GameObject("FillArea");
        fillArea.transform.SetParent(sliderObj.transform, false);
        RectTransform fillAreaRect = fillArea.AddComponent<RectTransform>();
        fillAreaRect.anchorMin = new Vector2(0, 0.3f);
        fillAreaRect.anchorMax = new Vector2(1, 0.7f);
        fillAreaRect.offsetMin = new Vector2(5, 0);
        fillAreaRect.offsetMax = new Vector2(-5, 0);

        GameObject fill = new GameObject("Fill");
        fill.transform.SetParent(fillArea.transform, false);
        RectTransform fillRect = fill.AddComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.sizeDelta = Vector2.zero;
        Image fillImg = fill.AddComponent<Image>();
        fillImg.color = new Color(0.9f, 0.75f, 0.3f);
        fillImg.raycastTarget = false;

        slider.fillRect = fillRect;

        // Handle
        GameObject handleArea = new GameObject("HandleArea");
        handleArea.transform.SetParent(sliderObj.transform, false);
        RectTransform handleAreaRect = handleArea.AddComponent<RectTransform>();
        handleAreaRect.anchorMin = Vector2.zero;
        handleAreaRect.anchorMax = Vector2.one;
        handleAreaRect.sizeDelta = Vector2.zero;

        GameObject handle = new GameObject("Handle");
        handle.transform.SetParent(handleArea.transform, false);
        RectTransform handleRect = handle.AddComponent<RectTransform>();
        handleRect.sizeDelta = new Vector2(18, 0);
        Image handleImg = handle.AddComponent<Image>();
        handleImg.color = Color.white;

        slider.handleRect = handleRect;
        slider.targetGraphic = handleImg;

        // Value label (kept in sync by UpdateValueLabel)
        GameObject valueObj = new GameObject("Value");
        valueObj.transform.SetParent(row.transform, false);
        TextMeshProUGUI valueTmp = valueObj.AddComponent<TextMeshProUGUI>();
        valueTmp.text = CompanyText.Get("pause.volume", ("value", Mathf.RoundToInt(defaultVal * 100f).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        valueTmp.fontSize = 14;
        valueTmp.color = new Color(0.7f, 0.7f, 0.7f);
        valueTmp.alignment = TextAlignmentOptions.Right;
        valueTmp.raycastTarget = false;
        LayoutElement valueLE = valueObj.AddComponent<LayoutElement>();
        valueLE.preferredWidth = 45;

        sliderValueLabels[slider] = valueTmp;

        return slider;
    }

    Toggle CreateToggleRow(Transform parent, string label)
    {
        GameObject row = new GameObject(label.Replace(" ", "") + "Row");
        row.transform.SetParent(parent, false);

        LayoutElement rowLE = row.AddComponent<LayoutElement>();
        rowLE.preferredHeight = 35;

        HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = true;
        hlg.spacing = 10;

        // Label
        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(row.transform, false);
        TextMeshProUGUI labelTmp = labelObj.AddComponent<TextMeshProUGUI>();
        labelTmp.text = label;
        labelTmp.fontSize = 16;
        labelTmp.color = Color.white;
        labelTmp.alignment = TextAlignmentOptions.Left;
        labelTmp.raycastTarget = false;
        LayoutElement labelLE = labelObj.AddComponent<LayoutElement>();
        labelLE.flexibleWidth = 1;

        // Toggle
        GameObject toggleObj = new GameObject("Toggle");
        toggleObj.transform.SetParent(row.transform, false);

        LayoutElement toggleLE = toggleObj.AddComponent<LayoutElement>();
        toggleLE.preferredWidth = 50;
        toggleLE.preferredHeight = 28;

        Image toggleBg = toggleObj.AddComponent<Image>();
        toggleBg.color = new Color(0.2f, 0.2f, 0.25f);

        Toggle toggle = toggleObj.AddComponent<Toggle>();

        // Checkmark
        GameObject checkObj = new GameObject("Checkmark");
        checkObj.transform.SetParent(toggleObj.transform, false);
        RectTransform checkRect = checkObj.AddComponent<RectTransform>();
        checkRect.anchorMin = new Vector2(0.15f, 0.15f);
        checkRect.anchorMax = new Vector2(0.85f, 0.85f);
        checkRect.sizeDelta = Vector2.zero;

        Image checkImg = checkObj.AddComponent<Image>();
        checkImg.color = new Color(0.9f, 0.75f, 0.3f);
        checkImg.raycastTarget = false;

        toggle.targetGraphic = toggleBg;
        toggle.graphic = checkImg;

        return toggle;
    }

    TMP_Dropdown CreateDropdownRow(Transform parent, string label, string[] options)
    {
        GameObject row = new GameObject(label.Replace(" ", "") + "Row");
        row.transform.SetParent(parent, false);

        LayoutElement rowLE = row.AddComponent<LayoutElement>();
        rowLE.preferredHeight = 35;

        HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;
        hlg.spacing = 10;

        // Label
        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(row.transform, false);
        TextMeshProUGUI labelTmp = labelObj.AddComponent<TextMeshProUGUI>();
        labelTmp.text = label;
        labelTmp.fontSize = 16;
        labelTmp.color = Color.white;
        labelTmp.alignment = TextAlignmentOptions.Left;
        labelTmp.raycastTarget = false;
        LayoutElement labelLE = labelObj.AddComponent<LayoutElement>();
        labelLE.preferredWidth = 130;

        // Dropdown
        GameObject dropdownObj = new GameObject("Dropdown");
        dropdownObj.transform.SetParent(row.transform, false);

        LayoutElement dropdownLE = dropdownObj.AddComponent<LayoutElement>();
        dropdownLE.flexibleWidth = 1;
        dropdownLE.preferredHeight = 30;

        Image dropdownBg = dropdownObj.AddComponent<Image>();
        dropdownBg.color = new Color(0.15f, 0.15f, 0.18f);

        TMP_Dropdown dropdown = dropdownObj.AddComponent<TMP_Dropdown>();
        dropdown.targetGraphic = dropdownBg;

        // Caption text
        GameObject captionObj = new GameObject("Label");
        captionObj.transform.SetParent(dropdownObj.transform, false);
        RectTransform captionRect = captionObj.AddComponent<RectTransform>();
        captionRect.anchorMin = Vector2.zero;
        captionRect.anchorMax = Vector2.one;
        captionRect.offsetMin = new Vector2(10, 0);
        captionRect.offsetMax = new Vector2(-25, 0);
        TextMeshProUGUI captionTmp = captionObj.AddComponent<TextMeshProUGUI>();
        captionTmp.fontSize = 14;
        captionTmp.color = Color.white;
        captionTmp.alignment = TextAlignmentOptions.Left;
        captionTmp.raycastTarget = false;
        dropdown.captionText = captionTmp;

        // Arrow (plain "v": the default TMP font has no arrow glyphs)
        GameObject arrowObj = new GameObject("Arrow");
        arrowObj.transform.SetParent(dropdownObj.transform, false);
        RectTransform arrowRect = arrowObj.AddComponent<RectTransform>();
        arrowRect.anchorMin = new Vector2(1, 0.5f);
        arrowRect.anchorMax = new Vector2(1, 0.5f);
        arrowRect.pivot = new Vector2(1, 0.5f);
        arrowRect.anchoredPosition = new Vector2(-10, 0);
        arrowRect.sizeDelta = new Vector2(15, 15);
        TextMeshProUGUI arrowTmp = arrowObj.AddComponent<TextMeshProUGUI>();
        arrowTmp.text = "v";
        arrowTmp.fontSize = 12;
        arrowTmp.color = new Color(0.7f, 0.7f, 0.7f);
        arrowTmp.alignment = TextAlignmentOptions.Center;
        arrowTmp.raycastTarget = false;

        // Template (simplified - will use Unity's default behavior)
        GameObject templateObj = new GameObject("Template");
        templateObj.transform.SetParent(dropdownObj.transform, false);
        RectTransform templateRect = templateObj.AddComponent<RectTransform>();
        templateRect.anchorMin = new Vector2(0, 0);
        templateRect.anchorMax = new Vector2(1, 0);
        templateRect.pivot = new Vector2(0.5f, 1);
        templateRect.anchoredPosition = Vector2.zero;
        templateRect.sizeDelta = new Vector2(0, 150);

        Image templateBg = templateObj.AddComponent<Image>();
        templateBg.color = new Color(0.12f, 0.12f, 0.15f);

        ScrollRect scrollRect = templateObj.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;

        // Viewport
        GameObject viewportObj = new GameObject("Viewport");
        viewportObj.transform.SetParent(templateObj.transform, false);
        RectTransform viewportRect = viewportObj.AddComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.sizeDelta = Vector2.zero;
        viewportObj.AddComponent<Mask>().showMaskGraphic = false;
        viewportObj.AddComponent<Image>().color = Color.white;

        // Content
        GameObject contentObj = new GameObject("Content");
        contentObj.transform.SetParent(viewportObj.transform, false);
        RectTransform contentRect = contentObj.AddComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0, 1);
        contentRect.anchorMax = new Vector2(1, 1);
        contentRect.pivot = new Vector2(0.5f, 1);
        contentRect.sizeDelta = new Vector2(0, 30);

        // Item
        GameObject itemObj = new GameObject("Item");
        itemObj.transform.SetParent(contentObj.transform, false);
        RectTransform itemRect = itemObj.AddComponent<RectTransform>();
        itemRect.anchorMin = new Vector2(0, 0.5f);
        itemRect.anchorMax = new Vector2(1, 0.5f);
        itemRect.sizeDelta = new Vector2(0, 30);

        Toggle itemToggle = itemObj.AddComponent<Toggle>();

        // Item background
        GameObject itemBgObj = new GameObject("Background");
        itemBgObj.transform.SetParent(itemObj.transform, false);
        RectTransform itemBgRect = itemBgObj.AddComponent<RectTransform>();
        itemBgRect.anchorMin = Vector2.zero;
        itemBgRect.anchorMax = Vector2.one;
        itemBgRect.sizeDelta = Vector2.zero;
        Image itemBgImg = itemBgObj.AddComponent<Image>();
        itemBgImg.color = new Color(0.9f, 0.75f, 0.3f, 0.3f);

        // Item label
        GameObject itemLabelObj = new GameObject("Label");
        itemLabelObj.transform.SetParent(itemObj.transform, false);
        RectTransform itemLabelRect = itemLabelObj.AddComponent<RectTransform>();
        itemLabelRect.anchorMin = Vector2.zero;
        itemLabelRect.anchorMax = Vector2.one;
        itemLabelRect.offsetMin = new Vector2(10, 0);
        itemLabelRect.offsetMax = new Vector2(-10, 0);
        TextMeshProUGUI itemLabelTmp = itemLabelObj.AddComponent<TextMeshProUGUI>();
        itemLabelTmp.fontSize = 14;
        itemLabelTmp.color = Color.white;
        itemLabelTmp.alignment = TextAlignmentOptions.Left;

        itemToggle.targetGraphic = itemBgImg;
        itemToggle.graphic = itemBgImg;

        dropdown.template = templateRect;
        dropdown.itemText = itemLabelTmp;

        scrollRect.content = contentRect;
        scrollRect.viewport = viewportRect;

        templateObj.SetActive(false);

        // Add options
        dropdown.ClearOptions();
        dropdown.AddOptions(new List<string>(options));

        return dropdown;
    }

    Button CreateButton(Transform parent, string text, Color bgColor)
    {
        GameObject btnObj = new GameObject(text.Replace(" ", "") + "Button");
        btnObj.transform.SetParent(parent, false);

        Image bg = btnObj.AddComponent<Image>();
        bg.color = bgColor;

        Button btn = btnObj.AddComponent<Button>();
        btn.targetGraphic = bg;

        // Hover color
        ColorBlock colors = btn.colors;
        colors.highlightedColor = new Color(bgColor.r + 0.1f, bgColor.g + 0.1f, bgColor.b + 0.1f);
        colors.pressedColor = new Color(bgColor.r - 0.1f, bgColor.g - 0.1f, bgColor.b - 0.1f);
        btn.colors = colors;

        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(btnObj.transform, false);
        RectTransform textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = Vector2.zero;

        TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = 15;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontStyle = FontStyles.Bold;
        tmp.raycastTarget = false;

        return btn;
    }
}
