using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Debug tools: money buttons, save helpers and Online City shortcuts.
/// Attach to a button or call from Inspector
/// - The debug buttons only work in the Editor and development builds. In release builds they are hidden by
///   disabling their Button, graphics and texts (never their GameObjects: other components share them).
/// - F9 (Editor / development builds, not while typing): emergency unstick, closes every panel and resumes time.
/// </summary>
public class DebugTools : MonoBehaviour
{
    [Header("Quick Value Buttons")]
    public Button billion1Button;
    public Button billion5Button;
    public Button billion10Button;
    public Button resetButton;

    [Header("Hotkeys (Editor / development builds only)")]
    [Tooltip("Key that closes every open panel and resumes time if the game gets stuck (not a letter: mini-games use letters)")]
    public KeyCode unstickKey = KeyCode.F9;

    static bool DebugFeaturesEnabled => Debug.isDebugBuild || Application.isEditor;

    void Start()
    {
        if (!DebugFeaturesEnabled)
        {
            // Release build: hide the debug buttons without deactivating anything
            HideButton(billion1Button);
            HideButton(billion5Button);
            HideButton(billion10Button);
            HideButton(resetButton);
            return;
        }

        // Auto-wire buttons if assigned
        if (billion1Button != null)
            billion1Button.onClick.AddListener(Set1Billion);
        if (billion5Button != null)
            billion5Button.onClick.AddListener(Set5Billion);
        if (billion10Button != null)
            billion10Button.onClick.AddListener(Set10Billion);
        if (resetButton != null)
            resetButton.onClick.AddListener(ResetValue);
    }

    /// <summary>Make a button invisible and unclickable, leaving its GameObject (and other components) active.</summary>
    static void HideButton(Button button)
    {
        if (button == null) return;

        button.onClick.RemoveAllListeners();
        button.interactable = false;
        button.enabled = false;

        foreach (var graphic in button.GetComponents<Graphic>())
            graphic.enabled = false;
        foreach (var graphic in button.GetComponentsInChildren<Graphic>(true))
            graphic.enabled = false;
    }

    void Update()
    {
        if (!DebugFeaturesEnabled) return;

        // Emergency unstick (debug hotkey)
        if (Input.GetKeyDown(unstickKey) && !UIManager.IsTypingInInputField()
            && (MiniGameManager.Instance == null || !MiniGameManager.Instance.IsMiniGameActive()))
        {
            Debug.LogWarning($"[DebugTools] {unstickKey} pressed: closing all panels and resuming time (Time.timeScale was {Time.timeScale}).");
            UIManager.Instance.ResetForSceneChange();
        }
    }

    void SetCompanyValue(double value)
    {
        if (CompanyManager.Instance == null)
        {
            Debug.LogError("CompanyManager not found!");
            return;
        }

        CompanyManager.Instance.AddMoney(value - CompanyManager.Instance.companyValue, true);

        // The Online City unlock check runs every second; run it right away
        if (CityOnline.Instance != null)
            CityOnline.Instance.CheckUnlockNow();

        Debug.Log($"DEBUG: Company value set to ${value:N0}");
    }

    /// <summary>
    /// Set company value to $1 Billion
    /// </summary>
    [ContextMenu("Set 1 Billion")]
    public void Set1Billion()
    {
        SetCompanyValue(1000000000f);
    }

    /// <summary>
    /// Set company value to $5 Billion
    /// </summary>
    [ContextMenu("Set 5 Billion")]
    public void Set5Billion()
    {
        SetCompanyValue(5000000000f);
    }

    /// <summary>
    /// Set company value to $10 Billion
    /// </summary>
    [ContextMenu("Set 10 Billion")]
    public void Set10Billion()
    {
        SetCompanyValue(10000000000f);
    }

    /// <summary>
    /// Reset to starting value
    /// </summary>
    [ContextMenu("Reset to 50K")]
    public void ResetValue()
    {
        SetCompanyValue(50000f);
    }

    /// <summary>
    /// Give 10 skill points (a new company starts with none)
    /// </summary>
    [ContextMenu("Add 10 Skill Points")]
    public void Add10SkillPoints()
    {
        if (SkillTreeManager.Instance == null)
        {
            Debug.LogError("SkillTreeManager not found!");
            return;
        }

        SkillTreeManager.Instance.SetSkillPoints(SkillTreeManager.Instance.skillPoints + 10);
        Debug.Log($"DEBUG: +10 skill points (now {SkillTreeManager.Instance.skillPoints})");
    }

    // ========== ONLINE CITY DEBUG ==========

    /// <summary>
    /// Reach $1B and unlock the Online City (the certificate shows)
    /// </summary>
    [ContextMenu("Force Unlock Online City")]
    public void ForceUnlockOnlineCity()
    {
        Set1Billion();
        CityOnline.Ensure().ForceUnlock();
        Debug.Log("DEBUG: Online City unlocked!");
    }

    /// <summary>
    /// Open the Online City screen (unlocking it first)
    /// </summary>
    [ContextMenu("Open Online City")]
    public void OpenOnlineCity()
    {
        CityOnline.Ensure().ForceUnlock();
        CityScreen.Open();
    }

    /// <summary>
    /// Switch between the real City and the local test city (Editor / development builds)
    /// </summary>
    [ContextMenu("Toggle Local Test City")]
    public void ToggleLocalTestCity()
    {
        CityOnline.UseLocalTestCity = !CityOnline.UseLocalTestCity;
        _ = CityOnline.Ensure().Reconnect();
        Debug.Log(CityOnline.UseLocalTestCity ? "DEBUG: Using the local test city" : "DEBUG: Using the real City");
    }

    // ========== SAVE SYSTEM DEBUG ==========

    /// <summary>
    /// Force save the game now
    /// </summary>
    [ContextMenu("Force Save Game")]
    public void ForceSaveGame()
    {
        if (SaveSystem.Instance != null)
        {
            bool saved = SaveSystem.Instance.SaveGame();
            Debug.Log(saved ? "DEBUG: Game saved!" : "DEBUG: Nothing was saved (see SaveSystem log)");
        }
        else
        {
            Debug.LogError("SaveSystem not found!");
        }
    }

    /// <summary>
    /// Force load the game now
    /// </summary>
    [ContextMenu("Force Load Game")]
    public void ForceLoadGame()
    {
        if (SaveSystem.Instance != null)
        {
            SaveSystem.Instance.LoadGame();
            Debug.Log("DEBUG: Reloading the saved game...");
        }
        else
        {
            Debug.LogError("SaveSystem not found!");
        }
    }

    /// <summary>
    /// Delete save file and start fresh
    /// </summary>
    [ContextMenu("Delete Save File")]
    public void DeleteSaveFile()
    {
        if (SaveSystem.Instance != null)
        {
            SaveSystem.Instance.DeleteSave();
            // The cloud save starts a new run too (like New Game), so the server's copy doesn't come back
            CloudSaveSync.BeginNewRun();
            Debug.Log("DEBUG: Save file deleted! (The next autosave writes a new one unless you quit first.)");
        }
        else
        {
            Debug.LogError("SaveSystem not found!");
        }
    }

    /// <summary>
    /// Show save file location
    /// </summary>
    [ContextMenu("Show Save Location")]
    public void ShowSaveLocation()
    {
        Debug.Log($"Save file location: {SaveSystem.LocalSaveFilePath}");
    }

    // ========== BILLION CERTIFICATE DEBUG ==========

    /// <summary>
    /// Show the $1B certificate again (on the next unlock check)
    /// </summary>
    [ContextMenu("Reset Billion Certificate")]
    public void ResetBillionNotification()
    {
        if (CityOnline.Instance != null)
            CityOnline.Instance.CertificateShown = false;
        Debug.Log("DEBUG: The $1B certificate will show again once the City is unlocked.");
    }

    // ========== COMPLETE RESET ==========

    /// <summary>
    /// COMPLETE RESET - Deletes ALL data everywhere and restarts fresh
    /// </summary>
    [ContextMenu("COMPLETE RESET - DELETE EVERYTHING")]
    public void CompleteReset()
    {
        StartCoroutine(CompleteResetCoroutine());
    }

    System.Collections.IEnumerator CompleteResetCoroutine()
    {
        Debug.LogWarning("COMPLETE RESET STARTING... (saving is switched off first so nothing is written back)");

        // STEP 1: Stop all saving immediately
        if (SaveSystem.Instance != null)
        {
            SaveSystem.Instance.SuspendSaving();
            SaveSystem.Instance.autoSaveOnQuit = false;
        }

        // STEP 2: Delete local save files (the Online City's unlock is saved in them too)
        SaveSystem.DeleteLocalSaveFiles();
        Debug.Log("Deleted local save files");

        // The cloud save starts a new run (like New Game): the server's copy of the old company doesn't come back
        CloudSaveSync.BeginNewRun();

        // STEP 3: Clear ALL PlayerPrefs (settings too)
        PlayerPrefs.DeleteAll();
        PlayerPrefs.Save();
        Debug.Log("Cleared all PlayerPrefs");

        // STEP 4: Clear in-memory state (the Online City locks again; the City account itself is online and stays)
        CityOnline.BeginNewGame();

        Debug.Log("COMPLETE RESET DONE! Quitting - restart manually for a fresh start.");

        yield return new WaitForSecondsRealtime(1f);

        // Quit the application completely (scene reload might still have cached data)
        #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
        #else
            Application.Quit();
        #endif
    }

    /// <summary>
    /// Show all save locations
    /// </summary>
    [ContextMenu("Show All Save Locations")]
    public void ShowAllSaveLocations()
    {
        Debug.Log("=== SAVE DATA LOCATIONS ===");
        Debug.Log($"Local Save: {SaveSystem.LocalSaveFilePath}");
        Debug.Log($"PlayerPrefs: Registry/plist (OS dependent)");
        Debug.Log("Online cities: one account per city on the server (Unity Gaming Services); unlock and unfinished transfers in the local save (entry \"onlineCity\")");
        Debug.Log($"Local test cities: {CityLocalBackend.FilePath("ist")} (and one file per city)");
        Debug.Log("===========================");
    }
}
