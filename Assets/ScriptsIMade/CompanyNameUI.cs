using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Popup for entering company name at the start of the game.
/// Pauses the game through UIManager while open. Not closable by Escape: the player confirms a name
/// (the field starts with the current name, so confirming without edits keeps it).
/// </summary>
public class CompanyNameUI : MonoBehaviour
{
    [Header("UI References")]
    public GameObject nameInputPanel;
    public TMP_InputField companyNameInput;
    public Button confirmButton;
    public Button randomButton;
    public TextMeshProUGUI errorText;
    public TextMeshProUGUI titleText;

    [Header("Settings")]
    public int minNameLength = 3;
    public int maxNameLength = 25;
    public bool showOnFirstLaunch = true;

    [Header("Random Name Parts")]
    public string[] prefixes = { "Global", "United", "Prime", "Elite", "Alpha", "Mega", "Super", "Ultra", "Dynamic", "Stellar", "Apex", "Phoenix", "Titan", "Nova", "Quantum" };
    public string[] suffixes = { "Industries", "Corp", "Solutions", "Enterprises", "Holdings", "Group", "Systems", "Technologies", "Dynamics", "Innovations", "Ventures", "Partners", "Labs", "Works", "Co" };

    private const string COMPANY_NAME_KEY = "CompanyName";
    private const string HAS_SET_NAME_KEY = "HasSetCompanyName";
    private const string DEFAULT_NAME = "New Company";

    public static string CurrentCompanyName { get; private set; } = DEFAULT_NAME;

    public static CompanyNameUI Instance { get; private set; }

    /// <summary>True while the name popup is showing.</summary>
    public bool IsOpen { get; private set; }

    // Never below the minimum, so a bad Inspector setup can't make every name invalid
    private int MaxLength => Mathf.Max(minNameLength, maxNameLength);

    private bool listenersAdded;

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (IsOpen && UIManager.Exists)
            UIManager.Instance.ClosePanel(this);
        if (Instance == this)
            Instance = null;
    }

    void Start()
    {
        // Load saved company name (always: the static value would otherwise carry the previous
        // company's name into a New Game, which deletes the key)
        CurrentCompanyName = PlayerPrefs.GetString(COMPANY_NAME_KEY, DEFAULT_NAME);

        // Setup UI
        EnsureListeners();

        if (nameInputPanel != null && !IsOpen)
            nameInputPanel.SetActive(false);

        if (companyNameInput != null)
            companyNameInput.characterLimit = MaxLength;

        if (errorText != null)
            errorText.gameObject.SetActive(false);

        // Show popup on first launch
        if (showOnFirstLaunch && !PlayerPrefs.HasKey(HAS_SET_NAME_KEY))
        {
            ShowNameInput();
        }
    }

    void EnsureListeners()
    {
        if (listenersAdded) return;
        listenersAdded = true;

        if (confirmButton != null)
            confirmButton.onClick.AddListener(OnConfirmClicked);

        if (randomButton != null)
            randomButton.onClick.AddListener(OnRandomClicked);

        if (companyNameInput != null)
        {
            companyNameInput.onValueChanged.AddListener(OnNameChanged);
            // Enter confirms too
            companyNameInput.onSubmit.AddListener(OnNameSubmitted);
        }
    }

    public void ShowNameInput()
    {
        if (nameInputPanel == null)
        {
            Debug.LogWarning("CompanyNameUI: nameInputPanel is not assigned - keeping the company name '" + CurrentCompanyName + "'.");
            return;
        }
        if (confirmButton == null && companyNameInput == null)
        {
            // Nothing could ever close the paused popup
            Debug.LogWarning("CompanyNameUI: neither confirmButton nor companyNameInput is assigned - not showing the name popup.");
            return;
        }

        EnsureListeners();

        nameInputPanel.SetActive(true);
        UIManager.BringToFront(nameInputPanel);
        IsOpen = true;

        // Pause game (UIManager owns Time.timeScale). No Escape handler: a name has to be confirmed.
        UIManager.Instance.OpenPanel(this, true, null);

        // Set current name in input field (the default name in the player's language: OnConfirmClicked stores it as the default)
        if (companyNameInput != null)
        {
            companyNameInput.characterLimit = MaxLength;
            companyNameInput.text = DisplayName(CurrentCompanyName);
            companyNameInput.Select();
            companyNameInput.ActivateInputField();
        }

        if (titleText != null)
            titleText.text = CompanyText.Get(PlayerPrefs.HasKey(HAS_SET_NAME_KEY) ? "company.name.title.change" : "company.name.title.enter");

        if (errorText != null)
            errorText.gameObject.SetActive(false);
    }

    void OnNameChanged(string newName)
    {
        // Clear error when typing
        if (errorText != null)
            errorText.gameObject.SetActive(false);
    }

    void OnNameSubmitted(string submitted)
    {
        OnConfirmClicked();
    }

    /// <summary>Confirm the typed name. Closes and unpauses when the name is valid.</summary>
    public void OnConfirmClicked()
    {
        if (!IsOpen) return;

        string newName = companyNameInput != null ? companyNameInput.text.Trim() : CurrentCompanyName;
        // The default name as shown in the player's language is stored as the default id ("New Company"), which the
        // save system compares against
        if (newName == ApplicantText.DefaultCompanyName) newName = DEFAULT_NAME;

        // Validate (only when there is a field to fix it in)
        if (companyNameInput != null)
        {
            if (string.IsNullOrEmpty(newName))
            {
                ShowError(CompanyText.Get("company.name.error.empty"));
                return;
            }

            if (newName.Length < minNameLength)
            {
                ShowError(CompanyText.Get("company.name.error.short", ("count", minNameLength)));
                return;
            }

            if (newName.Length > MaxLength)
            {
                ShowError(CompanyText.Get("company.name.error.long", ("count", MaxLength)));
                return;
            }
        }

        try
        {
            // Save the name
            SetCompanyName(string.IsNullOrEmpty(newName) ? DEFAULT_NAME : newName);
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
        }
        finally
        {
            // Always close and unpause, even if syncing the name failed
            ClosePanel();
        }
    }

    void OnRandomClicked()
    {
        string randomName = GenerateRandomName();
        if (companyNameInput != null)
            companyNameInput.text = randomName;
    }

    string GenerateRandomName()
    {
        string prefix = prefixes != null && prefixes.Length > 0 ? prefixes[Random.Range(0, prefixes.Length)] : "Global";
        string suffix = suffixes != null && suffixes.Length > 0 ? suffixes[Random.Range(0, suffixes.Length)] : "Corp";
        return $"{prefix} {suffix}";
    }

    void ShowError(string message)
    {
        if (errorText != null)
        {
            errorText.text = message;
            errorText.gameObject.SetActive(true);
        }
        else
        {
            Debug.LogWarning("CompanyNameUI: " + message);
        }
    }

    void SetCompanyName(string name)
    {
        CurrentCompanyName = name;

        // Save to PlayerPrefs
        PlayerPrefs.SetString(COMPANY_NAME_KEY, name);
        PlayerPrefs.SetInt(HAS_SET_NAME_KEY, 1);
        PlayerPrefs.Save();

        Debug.Log($"Company name set to: {name}");
        // (The Online City has its own company name and logo, set in the City's company sheet.)
    }

    void ClosePanel()
    {
        IsOpen = false;

        if (nameInputPanel != null)
            nameInputPanel.SetActive(false);

        // Resume game
        if (UIManager.Exists)
            UIManager.Instance.ClosePanel(this);
    }

    /// <summary>
    /// Close the popup without changing the name (for a future Cancel button).
    /// If no name was ever set, the popup appears again next launch.
    /// </summary>
    public void CancelNameChange()
    {
        if (IsOpen)
            ClosePanel();
    }

    /// <summary>
    /// Call this from a button to let players change their name later
    /// </summary>
    public void OpenNameChange()
    {
        ShowNameInput();
    }

    /// <summary>
    /// Adopt a company name restored from a save (called by SaveSystem after it writes PlayerPrefs).
    /// </summary>
    public static void ApplyRestoredName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        CurrentCompanyName = name.Trim();
    }

    /// <summary>
    /// Get the saved company name (call from anywhere)
    /// </summary>
    public static string GetCompanyName()
    {
        if (PlayerPrefs.HasKey(COMPANY_NAME_KEY))
            return PlayerPrefs.GetString(COMPANY_NAME_KEY);
        return DEFAULT_NAME;
    }

    /// <summary>
    /// A stored company name as players see it: the default "New Company" (or no name) in the current language
    /// (Applicants table, company.default_name), any other name as typed. Saves, the cloud and comparisons keep the
    /// stored value (the save system checks for "New Company").
    /// </summary>
    public static string DisplayName(string storedName)
    {
        if (string.IsNullOrWhiteSpace(storedName) || storedName.Trim() == DEFAULT_NAME) return ApplicantText.DefaultCompanyName;
        return storedName.Trim();
    }

    /// <summary>The saved company name as players see it (GetCompanyName through DisplayName).</summary>
    public static string GetDisplayName()
    {
        return DisplayName(GetCompanyName());
    }
}
