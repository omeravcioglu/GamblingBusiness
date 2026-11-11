using System.Globalization;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The designed main menu (canvas boards "Main menu", "New game: coin in the air / tails / heads", "Volume dice").
/// MainMenuUI keeps the menu's logic (new game, continue, settings, the coin's odds); this view does the design-only
/// parts: the legal pad fades while the coin decides, the "Flipping..." note, the result slip (MainMenuUI writes its
/// result into a hidden text, retry is the slip's Flip again), the save lines, and the settings folder's extras.
/// The texts it writes come from the Menu table (volumes, On/Off and the save line's day from the Company table's
/// pause.*) and are written again when the language changes; every text of the menu but the logo follows the language's font.
/// </summary>
[DisallowMultipleComponent]
public class MainMenuView : MonoBehaviour
{
    const string Table = "Menu";
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public MainMenuUI menu;
    public MenuCoinFlip2D coin;

    [Header("Menu")]
    [Tooltip("Logo + legal pad: faded while the coin decides")]
    public CanvasGroup menuGroup;
    public float fadedAlpha = 0.3f;
    public Button continueButton;
    public Graphic[] continueGraphics;
    public TMP_Text saveLine;
    public TMP_Text versionText;
    [Tooltip("The logo: the game's name, which keeps its design font in every language")]
    public RectTransform logo;

    [Header("Volume dice")]
    [Tooltip("The place row's percentages, box by box (written the language's way: Turkish %10)")]
    public TMP_Text[] placePercents = new TMP_Text[0];
    [Tooltip("The percentage each of those boxes shows")]
    public int[] placePercentValues = new int[0];

    [Header("Coin")]
    public GameObject airNote;
    [Tooltip("MainMenuUI.resultText: it writes SUCCESS / FAILURE (+ the next flip's odds) here")]
    public TMP_Text sourceResult;
    public string successWord = "SUCCESS";
    public GameObject slip;
    public GameObject headsContent;
    public GameObject tailsContent;
    public TMP_Text tailsNext;
    public Button backToMenuButton;
    [Tooltip("\"Starting value $50,000\" on the heads slip")]
    public TMP_Text startValueText;
    [Tooltip("The company's starting value that chip shows")]
    public double startingValue = 50000;

    [Header("New game confirm")]
    [Tooltip("\"Starting a new game will DELETE your current save:\" (the word in bold red)")]
    public TMP_Text confirmDeleteText;
    public TMP_Text confirmSaveLine;
    [Tooltip("\"After that, the bank flips a coin: 1 in 3 chance they fund you.\" (odds from the coin)")]
    public TMP_Text confirmOdds;
    public Button confirmCloseButton;
    public Button confirmNoButton;

    [Header("Settings folder")]
    public PauseMenuView settings;

    [Header("Credits")]
    public GameObject creditsPage;
    public Button creditsButton;
    public Button creditsBackButton;

    static readonly Regex Tags = new Regex("<[^>]+>");

    bool slipDismissed;
    string shownResult;
    bool shownActive;
    // what the settings folder's value texts show (-1: write them again)
    int shownMaster = -1, shownMusic = -1, shownSfx = -1, shownFullscreen = -1;

    void Awake()
    {
        // Every text of the menu (the settings and the credits included) on the language's font, but the logo
        foreach (TMP_Text text in GetComponentsInChildren<TMP_Text>(true))
            if (logo == null || !text.transform.IsChildOf(logo)) L10nFont.Attach(text);
        if (backToMenuButton != null) backToMenuButton.onClick.AddListener(() => { slipDismissed = true; ShowSlip(false, false); });
        if (confirmCloseButton != null && confirmNoButton != null) confirmCloseButton.onClick.AddListener(() => confirmNoButton.onClick.Invoke());
        if (settings != null)
        {
            if (settings.settingsDoneButton != null && settings.settingsCloseButton != null)
                settings.settingsDoneButton.onClick.AddListener(() => settings.settingsCloseButton.onClick.Invoke());
            if (settings.resetDefaultsButton != null) settings.resetDefaultsButton.onClick.AddListener(ResetSound);
            if (settings.fullscreenToggle != null) settings.fullscreenToggle.onValueChanged.AddListener(_ => RefreshSettingsTexts());
        }
        if (creditsButton != null) creditsButton.onClick.AddListener(OpenCredits);
        if (creditsBackButton != null) creditsBackButton.onClick.AddListener(CloseCredits);
        if (creditsPage != null) creditsPage.SetActive(false);
        ShowSlip(false, false);
        if (airNote != null) airNote.SetActive(false);
    }

    void OnEnable()
    {
        L10n.LanguageChanged += OnLanguageChanged;
    }

    void OnDisable()
    {
        L10n.LanguageChanged -= OnLanguageChanged;
    }

    void Start()
    {
        RefreshTexts();
        if (versionText != null) versionText.text = "v" + Application.version;
    }

    void OnLanguageChanged()
    {
        RefreshTexts();
        shownMaster = shownMusic = shownSfx = shownFullscreen = -1;
        RefreshSettingsTexts();
    }

    /// <summary>The save lines, the confirm sheet's lines, the starting value and the place row, in the current language.</summary>
    void RefreshTexts()
    {
        string line = SaveLine();
        if (saveLine != null) saveLine.text = line ?? L10n.Get(Table, "mainmenu.pad.no_save");
        if (confirmSaveLine != null) confirmSaveLine.text = line ?? L10n.Get(Table, "mainmenu.confirm.no_save");
        if (confirmDeleteText != null)
            confirmDeleteText.text = L10n.Get(Table, "mainmenu.confirm.delete", ("delete", "<b><color=#" + ColorUtility.ToHtmlStringRGB(UIColors.Loss) + ">" +
                                                                                          L10n.Get(Table, "mainmenu.confirm.delete.word") + "</color></b>"));
        if (confirmOdds != null && coin != null)
            confirmOdds.text = L10n.Get(Table, "mainmenu.confirm.odds",
                                        ("odds", "<link=\"" + TextHighlighter.LinkId + "\">" + OneIn(coin.BaseSuccessChance) + "</link>"));
        if (startValueText != null) startValueText.text = L10n.Get(Table, "mainmenu.slip.start_value", ("amount", UIFormat.Money(startingValue)));
        if (placePercents != null && placePercentValues != null)
        {
            for (int i = 0; i < placePercents.Length && i < placePercentValues.Length; i++)
                if (placePercents[i] != null)
                    placePercents[i].text = CompanyText.Get("pause.volume", ("value", placePercentValues[i].ToString(Inv)));
        }
    }

    /// <summary>0.33 -> "1 in 3", 0.5 -> "1 in 2", else a percentage (in the current language).</summary>
    public static string OneIn(float chance)
    {
        chance = Mathf.Clamp01(chance);
        if (chance > 0f && chance < 1f)
        {
            float n = 1f / chance;
            int r = Mathf.RoundToInt(n);
            if (r >= 2 && Mathf.Abs(n - r) < 0.1f) return L10n.Get(Table, "mainmenu.confirm.odds.one_in", ("count", r.ToString(Inv)));
        }
        return L10n.Get(Table, "mainmenu.confirm.odds.percent", ("percent", Mathf.RoundToInt(chance * 100f).ToString(Inv)));
    }

    /// <summary>"Nova Holdings · Day 42 · $3,284,100" from the local save (in the current language), or null.</summary>
    public static string SaveLine()
    {
        if (!SaveSystem.TryReadLocalSave(out SaveData data) || data == null || data.currentDay <= 0) return null;
        string stored = PlayerPrefs.GetString("CompanyName", data.companyName ?? "");
        string name = string.IsNullOrWhiteSpace(stored) ? L10n.Get(Table, "mainmenu.save.no_name") : CompanyNameUI.DisplayName(stored);
        return name + " · " + CompanyText.Get("pause.save_card.day", ("day", data.currentDay.ToString(Inv))) + " · " + UIFormat.Money(data.companyValue);
    }

    void LateUpdate()
    {
        bool tossing = coin != null && coin.IsTossing;
        if (airNote != null && airNote.activeSelf != tossing) airNote.SetActive(tossing);

        // result slip: follows MainMenuUI's (hidden) result text
        bool active = sourceResult != null && sourceResult.gameObject.activeInHierarchy;
        string text = active ? sourceResult.text : null;
        if (active != shownActive || text != shownResult)
        {
            if (active && (!shownActive || text != shownResult)) slipDismissed = false;
            shownActive = active;
            shownResult = text;
            bool heads = active && text != null && text.StartsWith(successWord);
            if (active && !heads && tailsNext != null)
            {
                int br = text.IndexOf('\n');
                tailsNext.text = br >= 0 ? Tags.Replace(text.Substring(br + 1), "").Trim() : "";
            }
            ShowSlip(active && !slipDismissed, heads);
        }

        bool deciding = tossing || (slip != null && slip.activeSelf);
        bool credits = creditsPage != null && creditsPage.activeSelf;
        if (menuGroup != null)
        {
            // the credits show the room alone (the board has no logo or pad behind the receipt)
            float target = credits ? 0f : deciding ? fadedAlpha : 1f;
            menuGroup.alpha = Mathf.MoveTowards(menuGroup.alpha, target, Time.unscaledDeltaTime * 3f);
        }

        // MainMenuUI tints the Continue label white/grey for its old dark menu: keep the pad's ink
        if (continueButton != null && continueGraphics != null)
        {
            Color c = continueButton.interactable ? UIColors.Ink : UIColors.Pencil;
            foreach (var g in continueGraphics)
                if (g != null && g.color != c) g.color = c;
        }

        if (settings != null && settings.settingsView != null && settings.settingsView.activeInHierarchy)
            RefreshSettingsTexts();
    }

    void ShowSlip(bool on, bool heads)
    {
        if (slip != null && slip.activeSelf != on) slip.SetActive(on);
        if (!on) return;
        if (headsContent != null) headsContent.SetActive(heads);
        if (tailsContent != null) tailsContent.SetActive(!heads);
    }

    // The settings folder's volumes and fullscreen state (MainMenuUI moves the controls; the pause menu's SettingsPanel writes these in game)
    void RefreshSettingsTexts()
    {
        if (settings == null) return;
        SetPct(settings.masterValue, settings.masterSlider, ref shownMaster);
        SetPct(settings.musicValue, settings.musicSlider, ref shownMusic);
        SetPct(settings.sfxValue, settings.sfxSlider, ref shownSfx);
        if (settings.fullscreenState != null && settings.fullscreenToggle != null)
        {
            int on = settings.fullscreenToggle.isOn ? 1 : 0;
            if (on != shownFullscreen)
            {
                shownFullscreen = on;
                settings.fullscreenState.text = CompanyText.Get(on == 1 ? "pause.on" : "pause.off");
            }
        }
    }

    static void SetPct(TMP_Text t, Slider s, ref int shown)
    {
        if (t == null || s == null) return;
        int pct = Mathf.RoundToInt(s.value * 100f);
        if (pct == shown) return;
        shown = pct;
        t.text = CompanyText.Get("pause.volume", ("value", pct.ToString(Inv)));
    }

    void ResetSound()
    {
        GameSettings.MasterVolume = GameSettings.DefaultMasterVolume;
        GameSettings.MusicVolume = GameSettings.DefaultMusicVolume;
        GameSettings.SfxVolume = GameSettings.DefaultSfxVolume;
        GameSettings.Save();
    }

    void OpenCredits()
    {
        if (menu != null && menu.IsBusy) return;
        if (creditsPage == null) return;
        creditsPage.SetActive(true);
        if (menuGroup != null) menuGroup.alpha = 0f;
        var ui = UIManager.Instance;
        if (ui != null) ui.OpenPanel(creditsPage, false, CloseCredits);
    }

    void CloseCredits()
    {
        if (creditsPage != null) creditsPage.SetActive(false);
        if (UIManager.Exists) UIManager.Instance.ClosePanel(creditsPage);
    }

    /// <summary>Show a result slip without MainMenuUI (previews).</summary>
    public void PreviewResult(bool heads, string nextLine)
    {
        if (tailsNext != null && nextLine != null) tailsNext.text = nextLine;
        ShowSlip(true, heads);
        if (menuGroup != null) menuGroup.alpha = fadedAlpha;
    }
}
