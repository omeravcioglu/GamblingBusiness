using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The designed pause menu (legal pad list + Settings folder), built by Tools > Game > New UI > Build Pause Menu.
/// Holds references to its controls; SettingsPanel takes them over at start (ApplyTo), so the menu keeps
/// working even when the prefab is rebuilt. It writes the highlighter-marked labels (the to-do items, the Settings
/// headings) from the Menu table itself, and keeps every text on the language's font.
/// </summary>
[DisallowMultipleComponent]
public class PauseMenuView : MonoBehaviour
{
    /// <summary>A label shown as &lt;link="hl"&gt;entry&lt;/link&gt; (TextHighlighter marks it), from the Menu table.</summary>
    [System.Serializable]
    public struct HighlightLabel
    {
        public TMP_Text text;
        [Tooltip("Menu table key, e.g. \"pausemenu.item.resume\"")]
        public string key;
    }

    const string Table = "Menu";

    [Header("Pages")]
    public GameObject menuView;
    public GameObject settingsView;

    [Header("Pause list")]
    public Button closeButton;
    public Button resumeButton;
    public Button openSettingsButton;
    public Button saveButton;
    public Button renameButton;
    public Button mainMenuButton;
    public Button quitButton;
    public TextMeshProUGUI lastSavedText;

    [Header("Current save card")]
    public TextMeshProUGUI cardName;
    public TextMeshProUGUI cardDay;
    public TextMeshProUGUI cardValue;
    public TextMeshProUGUI cardStaff;
    public TextMeshProUGUI cardAutosave;

    [Header("Settings")]
    public Button settingsCloseButton;
    public Button settingsDoneButton;
    public Button resetDefaultsButton;
    public Slider masterSlider;
    public Slider musicSlider;
    public Slider sfxSlider;
    public TextMeshProUGUI masterValue;
    public TextMeshProUGUI musicValue;
    public TextMeshProUGUI sfxValue;
    public TMP_Dropdown resolutionDropdown;
    public TMP_Dropdown qualityDropdown;
    public Toggle fullscreenToggle;
    public TextMeshProUGUI fullscreenState;
    // Localization (2026-09-29): optional designed language dropdown; empty = LanguagePicker puts a copy of Quality in the footer
    public TMP_Dropdown languageDropdown;

    [Header("Localized labels")]
    [Tooltip("Labels under a highlighter (the to-do items, the Settings headings), written in the current language")]
    public HighlightLabel[] highlightLabels = new HighlightLabel[0];

    void Awake()
    {
        L10nFont.AttachAll(this);   // the builder's labels and the texts SettingsPanel writes follow the language's font
    }

    void OnEnable()
    {
        L10n.LanguageChanged += WriteHighlightLabels;
        WriteHighlightLabels();
    }

    void OnDisable()
    {
        L10n.LanguageChanged -= WriteHighlightLabels;
    }

    void WriteHighlightLabels()
    {
        if (highlightLabels == null) return;
        foreach (HighlightLabel label in highlightLabels)
        {
            if (label.text == null || string.IsNullOrEmpty(label.key)) continue;
            string text = "<link=\"" + TextHighlighter.LinkId + "\">" + L10n.Get(Table, label.key) + "</link>";
            if (label.text.text != text) label.text.text = text;
        }
    }

    public void ApplyTo(SettingsPanel p)
    {
        p.panel = gameObject;
        p.menuView = menuView;
        p.settingsView = settingsView;
        p.closeButton = closeButton;
        p.resumeButton = resumeButton;
        p.openSettingsButton = openSettingsButton;
        p.saveButton = saveButton;
        p.renameButton = renameButton;
        p.mainMenuButton = mainMenuButton;
        p.quitButton = quitButton;
        p.lastSavedText = lastSavedText;
        p.saveCardName = cardName;
        p.saveCardDay = cardDay;
        p.saveCardValue = cardValue;
        p.saveCardStaff = cardStaff;
        p.saveCardAutosave = cardAutosave;
        p.settingsCloseButton = settingsCloseButton;
        p.settingsDoneButton = settingsDoneButton;
        p.resetDefaultsButton = resetDefaultsButton;
        p.masterVolumeSlider = masterSlider;
        p.musicVolumeSlider = musicSlider;
        p.sfxVolumeSlider = sfxSlider;
        p.masterValueText = masterValue;
        p.musicValueText = musicValue;
        p.sfxValueText = sfxValue;
        p.resolutionDropdown = resolutionDropdown;
        p.qualityDropdown = qualityDropdown;
        p.fullscreenToggle = fullscreenToggle;
        p.fullscreenStateText = fullscreenState;
        p.languageDropdown = languageDropdown;
        p.statusText = null;
    }
}
