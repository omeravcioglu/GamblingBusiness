using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Saves and loads the game (persists across scenes).
/// - The local file (persistentDataPath/savegame_backup.json) is the only save.
/// - The saved game is applied exactly once each time the main game scene loads, after every object's Start.
///   Nothing is saved for that scene before then, so a half-loaded scene can never overwrite the save.
/// - Company data is only written while a CompanyManager exists. In a scene without one a save only updates the
///   states of the save participants that are there (such as the persistent CityOnline) in the existing local
///   save; money is patched by ApplyMoneyDeltaToSave.
/// - Autosave: local writes at most every 10 s. SaveGame() writes right away
///   (SceneFlow.Load calls it before every scene change).
/// - Other systems store their own state by implementing ISaveParticipant.
/// - Cloud save: every local write (game saves, SaveWithoutCompany, ApplyMoneyDeltaToSave) keeps the save's run id,
///   adds one to its seq (CloudSaveSync.StampLocalWrite) and tells CloudSaveSync, which uploads it (explicit, pause and
///   quit saves right away). CloudSaveSync replaces the file itself when the server has a newer save (WriteLocalSaveText).
/// </summary>
public class SaveSystem : MonoBehaviour
{
    public static SaveSystem Instance { get; private set; }

    /// <summary>PlayerPrefs flag set by the main menu's New Game: the next main game load starts fresh.</summary>
    public const string NewGameFlagKey = "IsNewGame";
    /// <summary>Local autosaves are never written more often than this (seconds).</summary>
    public const float MinLocalAutoSaveInterval = 10f;

    const string SaveFileName = "savegame_backup.json";
    // One lock for every read, write and delete of the local save files: the cloud save reads them on worker threads, and a
    // read overlapping a replace (or a replace overlapping a read) fails on Windows
    static readonly object FileLock = new object();
    const string CompanyNameKey = "CompanyName";
    const string HasSetCompanyNameKey = "HasSetCompanyName";
    const string DefaultCompanyName = "New Company";

    [Header("Settings")]
    [Tooltip("No longer used: scene changes through SceneFlow.Load save before the old scene unloads.")]
    [HideInInspector] public bool autoSaveOnSceneChange = true;
    [Tooltip("Save when the game quits or the OS pauses it")]
    public bool autoSaveOnQuit = true;
    [Tooltip("Seconds between autosaves while a company is loaded. Local writes are at least 10 s apart.")]
    public float autoSaveInterval = 60f;

    [Tooltip("No longer used: the local save is always written (it is the source of truth)")]
    [HideInInspector] public bool keepLocalBackup = true;

    [Header("Debug")]
    public bool enableDebugLogs = true;

    /// <summary>
    /// Full path of the local save file (works without a SaveSystem instance, e.g. in the main menu). Playtest builds keep
    /// their own file, so their companies never replace the real save.
    /// </summary>
    public static string LocalSaveFilePath =>
        Path.Combine(Application.persistentDataPath,
                     DemoMode.Active ? DemoSaveFileName : PlaytestMode.Active ? PlaytestSaveFileName : SaveFileName);   // Demo: own file first

    const string PlaytestSaveFileName = "savegame_playtest.json";
    const string DemoSaveFileName = "savegame_demo.json";   // Demo

    /// <summary>True while a saved game is being loaded into the scene.</summary>
    public bool IsLoading => loadRoutine != null;

    /// <summary>True when saving has been switched off (DebugTools' complete reset).</summary>
    public bool SavingSuspended { get; private set; }

    /// <summary>True once the current main game scene has its saved state applied (so it may be saved).</summary>
    public bool IsGameLoaded => CompanyManager.Instance != null && CompanyManager.Instance == loadedCompany;

    enum SaveTrigger { Auto, Explicit, Pause, Quit }

    CompanyManager loadedCompany;   // The scene's company that has its saved state applied
    SaveData lastSnapshot;          // Last saved or loaded game (carries data of systems missing from a scene)
    Dictionary<string, string> knownExtras = new Dictionary<string, string>();
    Coroutine loadRoutine;
    int loadSceneHandle = int.MinValue;
    float lastLocalWriteTime = -1000f;

    // Inspector values, restored for every new game (older code switched autosave off and never back on)
    bool initialAutoSaveOnQuit;
    float initialAutoSaveInterval;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        initialAutoSaveOnQuit = autoSaveOnQuit;
        initialAutoSaveInterval = autoSaveInterval;

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void Start()
    {
        if (Instance != this) return;
        // OnSceneLoaded normally handled the first scene already; this covers the case where it didn't run
        BeginLoadForScene(SceneManager.GetActiveScene());
    }

    void OnDestroy()
    {
        if (Instance != this) return;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        Instance = null;
    }

    void Update()
    {
        if (SavingSuspended) return;

        // Autosave while a loaded company is running
        var company = CompanyManager.Instance;
        if (company != null && company == loadedCompany && autoSaveInterval > 0f)
        {
            float interval = Mathf.Max(autoSaveInterval, MinLocalAutoSaveInterval);
            if (Time.realtimeSinceStartup - lastLocalWriteTime >= interval)
                Save(SaveTrigger.Auto);
        }
    }

    void OnApplicationQuit()
    {
        if (Instance != this || !autoSaveOnQuit) return;
        // Local file (synchronous)
        Save(SaveTrigger.Quit);
    }

    void OnApplicationPause(bool paused)
    {
        if (Instance != this || !paused || !autoSaveOnQuit) return;
        Save(SaveTrigger.Pause);
    }

    // ------------------------------------------------------------------
    // Loading
    // ------------------------------------------------------------------

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (Instance != this || mode != LoadSceneMode.Single) return;
        Log($"Scene loaded: {scene.name}");
        BeginLoadForScene(scene);
    }

    /// <summary>Load the saved game into this scene once (if it has a CompanyManager).</summary>
    void BeginLoadForScene(Scene scene)
    {
        if (scene.handle == loadSceneHandle) return;
        loadSceneHandle = scene.handle;

        // The new scene's company must get its save before it can be saved
        loadedCompany = null;
        if (loadRoutine != null)
            StopCoroutine(loadRoutine);
        loadRoutine = StartCoroutine(LoadWhenSceneReady(scene.handle));
    }

    IEnumerator LoadWhenSceneReady(int sceneHandle)
    {
        // Let every object in the scene run Awake and Start first (CompanyManager.Start resets its values).
        // Frames, not seconds: this also works while a panel pauses the game.
        yield return null;
        yield return null;

        float waited = 0f;
        while (CompanyManager.Instance == null && waited < 2f)
        {
            waited += Time.unscaledDeltaTime;
            yield return null;
        }
        if (waited > 0f)
            yield return null; // A late CompanyManager needs its Start too

        var company = CompanyManager.Instance;
        if (company == null || SceneManager.GetActiveScene().handle != sceneHandle)
        {
            Log("No company in this scene - nothing to load");
            loadRoutine = null;
            yield break;
        }

        yield return LoadIntoCompany(company);
        loadRoutine = null;
    }

    /// <summary>
    /// Apply the saved game once: a new game starts fresh; otherwise the local save is loaded (when it is usable).
    /// </summary>
    IEnumerator LoadIntoCompany(CompanyManager company)
    {
        // New game from the main menu: start fresh
        if (PlayerPrefs.GetInt(NewGameFlagKey, 0) == 1)
        {
            PlayerPrefs.DeleteKey(NewGameFlagKey);
            PlayerPrefs.Save();
            lastSnapshot = null;
            knownExtras.Clear();
            FinishLoad(company, "New game - starting fresh");
            yield break;
        }

        SaveData local = ReadLocalSave();
        if (local != null && !LooksEmpty(local))
        {
            ApplySaveData(local);
            FinishLoad(company, $"Loaded local save (Day {local.currentDay}, ${local.companyValue:N0})");
            yield break;
        }

        if (local != null)
            Debug.LogWarning("[SaveSystem] The local save has no company data (an older version wrote it after the scene closed). Starting a new company.");

        // Nothing to load: a fresh company (forget any game held in memory from earlier).
        // (CityOnline reads the file's hasUnlockedOnlineWorld itself, so the City unlock survives an emptied save.)
        lastSnapshot = null;
        knownExtras.Clear();

        FinishLoad(company, "No save found - starting a new company");
    }

    void FinishLoad(CompanyManager company, string message)
    {
        loadedCompany = company;
        // First autosave one interval from now
        lastLocalWriteTime = Time.realtimeSinceStartup;
        Log(message);
    }

    /// <summary>Saves written after the scene was destroyed (older versions) have day 0 and no company data.</summary>
    static bool LooksEmpty(SaveData data)
    {
        return data.currentDay <= 0;
    }

    /// <summary>
    /// Reload the saved game into the current scene (debug). Normal loading happens automatically.
    /// </summary>
    public void LoadGame()
    {
        var company = CompanyManager.Instance;
        if (company == null)
        {
            Log("No company in this scene - nothing to load");
            return;
        }

        if (loadRoutine != null)
            StopCoroutine(loadRoutine);
        loadedCompany = null;
        loadRoutine = StartCoroutine(ReloadRoutine(company));
    }

    IEnumerator ReloadRoutine(CompanyManager company)
    {
        yield return LoadIntoCompany(company);
        loadRoutine = null;
    }

    void ApplySaveData(SaveData data)
    {
        data.EnsureLists();

        // Version 1.0 saves had no photo numbers (don't trust a default 0)
        if (data.saveVersion == "1.0")
        {
            foreach (var emp in data.employees)
            {
                if (emp != null) emp.portraitIndex = -1;
            }
        }

        var company = CompanyManager.Instance;
        if (company != null)
        {
            company.companyValue = Math.Max(0.0, data.companyValue);
            company.displayedValue = company.companyValue;
            company.currentDay = Mathf.Max(1, data.currentDay);
            company.employeeHappiness = Mathf.Clamp(data.employeeHappiness, 0f, 100f);
            company.investorTrust = Mathf.Clamp(data.investorTrust, 0f, 100f);

            company.employees.Clear();
            foreach (var empData in data.employees)
            {
                if (empData != null)
                    company.employees.Add(empData.ToApplicantData(false)); // portraits load on demand (portraitIndex)
            }
            company.hasHiredFirst = data.hasHiredFirst || company.employees.Count > 0;

            company.UpdateCompanyStats();
        }

        // Re-locks skills missing from the save, recalculates effects and notifies the UI
        var skills = SkillTreeManager.Instance;
        if (skills != null)
            skills.ApplySavedState(data.skillPoints, data.totalHighIQHires, data.unlockedSkillIds);

        ApplyOfficeItemLevels(data.officeItems);

        RestoreCompanyName(data.companyName);

        lastSnapshot = data;

        // Other systems last, once the core state is in place
        RestoreExtras(data.extras);

        if (company != null)
            company.NotifyStateLoaded();
    }

    static void ApplyOfficeItemLevels(List<OfficeItemSaveData> savedItems)
    {
        if (savedItems == null || savedItems.Count == 0) return;

        var levels = new Dictionary<string, int>();
        foreach (var saved in savedItems)
        {
            if (saved != null && !string.IsNullOrEmpty(saved.itemName))
                levels[saved.itemName] = saved.currentLevel;
        }

        foreach (var item in FindObjectsByType<OfficeItemInteractable>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (item == null || item.itemData == null) continue;
            if (!levels.TryGetValue(OfficeItemKey(item), out int level)) continue;

            level = Mathf.Clamp(level, 0, Mathf.Max(0, item.itemData.maxLevel));
            if (item.itemData.currentLevel == level) continue;

            item.itemData.currentLevel = level;
            item.OnUpgraded();
        }
    }

    /// <summary>
    /// Put the saved company name back when PlayerPrefs lost it (otherwise PlayerPrefs is newer: renames are
    /// stored there right away). Closes the "enter your company name" popup that appeared only because it was missing.
    /// </summary>
    static void RestoreCompanyName(string companyName)
    {
        if (string.IsNullOrWhiteSpace(companyName) || companyName == DefaultCompanyName) return;

        bool changed = false;
        if (!PlayerPrefs.HasKey(CompanyNameKey))
        {
            PlayerPrefs.SetString(CompanyNameKey, companyName);
            changed = true;
        }
        if (!PlayerPrefs.HasKey(HasSetCompanyNameKey))
        {
            PlayerPrefs.SetInt(HasSetCompanyNameKey, 1);
            changed = true;
        }
        if (!changed) return;

        PlayerPrefs.Save();
        CompanyNameUI.ApplyRestoredName(PlayerPrefs.GetString(CompanyNameKey, companyName));
        if (CompanyNameUI.Instance != null && CompanyNameUI.Instance.IsOpen)
            CompanyNameUI.Instance.CancelNameChange();
    }

    void RestoreExtras(List<ExtraSaveEntry> entries)
    {
        knownExtras = new Dictionary<string, string>();
        if (entries != null)
        {
            foreach (var entry in entries)
            {
                if (entry != null && !string.IsNullOrEmpty(entry.key))
                    knownExtras[entry.key] = entry.json ?? "";
            }
        }
        if (knownExtras.Count == 0) return;

        foreach (var participant in FindParticipants())
        {
            string key = SafeKey(participant);
            if (key == null || !knownExtras.TryGetValue(key, out string json)) continue;

            try
            {
                participant.RestoreState(json);
                Log($"Restored '{key}'");
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveSystem] Restoring '{key}' failed: {e}");
            }
        }
    }

    // ------------------------------------------------------------------
    // Saving
    // ------------------------------------------------------------------

    /// <summary>
    /// Save now: writes the local file immediately.
    /// Returns true if the local save was written (false when there is nothing to save yet).
    /// </summary>
    public bool SaveGame()
    {
        return Save(SaveTrigger.Explicit);
    }

    /// <summary>
    /// Force save immediately (for testing or manual save button)
    /// </summary>
    [ContextMenu("Force Save Now")]
    public void ForceSave()
    {
        Log("Force saving...");
        SaveGame();
    }

    /// <summary>
    /// Force load immediately (for testing)
    /// </summary>
    [ContextMenu("Force Load Now")]
    public void ForceLoad()
    {
        Log("Force loading...");
        LoadGame();
    }

    bool Save(SaveTrigger trigger)
    {
        if (SavingSuspended)
        {
            Log("Saving is suspended - skipped");
            return false;
        }

        var company = CompanyManager.Instance;
        if (company != null)
        {
            if (company != loadedCompany)
            {
                // Writing now would replace the save with a half-loaded company
                Log("Skipped save: the saved game hasn't been loaded into this scene yet");
                return false;
            }

            SaveData data = CaptureSaveData(company);
            bool written = WriteLocalSave(data, trigger);
            lastLocalWriteTime = Time.realtimeSinceStartup;
            lastSnapshot = data;
            return written;
        }

        return SaveWithoutCompany(trigger);
    }

    SaveData CaptureSaveData(CompanyManager company)
    {
        var data = new SaveData();

        // The cloud save's chain continues from the save this company was loaded from (a new company gets its run id
        // when it is first written: CloudSaveSync.StampLocalWrite)
        if (lastSnapshot != null)
        {
            data.runId = lastSnapshot.runId;
            data.seq = lastSnapshot.seq;
            data.ownerId = lastSnapshot.ownerId;
            data.ownerSteamId = lastSnapshot.ownerSteamId;
            data.baseSeq = lastSnapshot.baseSeq;
        }

        // A bet on a flip/hand that hasn't resolved is saved as spent: gambles settle the moment their outcome
        // shows, so adding it back would let a quit on a losing hand undo the loss.
        // Cash Grab: the value as booked, without the income the cash floor delivered in advance today (the cash floor's own
        // save keeps that advance and puts it back on load), so the cloud save's server sees day income at day end as before
        data.companyValue = Math.Max(0.0, company.BookedValue);
        data.currentDay = Mathf.Max(1, company.currentDay);
        data.employeeHappiness = company.employeeHappiness;
        data.investorTrust = company.investorTrust;
        if (company.employees != null)
        {
            foreach (var emp in company.employees)
            {
                if (emp != null)
                    data.employees.Add(new EmployeeSaveData(emp));
            }
        }
        data.hasHiredFirst = company.hasHiredFirst || data.employees.Count > 0;

        var skills = SkillTreeManager.Instance;
        if (skills != null)
        {
            data.skillPoints = skills.skillPoints;
            data.totalHighIQHires = skills.totalHighIQHires;
            if (skills.allSkills != null)
            {
                foreach (var skill in skills.allSkills)
                {
                    if (skill != null && skill.unlocked && !string.IsNullOrEmpty(skill.id))
                        data.unlockedSkillIds.Add(skill.id);
                }
            }
        }
        else if (lastSnapshot != null)
        {
            // No skill tree in this scene: keep what was saved
            data.skillPoints = lastSnapshot.skillPoints;
            data.totalHighIQHires = lastSnapshot.totalHighIQHires;
            data.unlockedSkillIds.AddRange(lastSnapshot.unlockedSkillIds);
        }

        var seenItems = new HashSet<string>();
        foreach (var item in FindObjectsByType<OfficeItemInteractable>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (item == null || item.itemData == null) continue;
            string key = OfficeItemKey(item);
            if (!seenItems.Add(key)) continue;
            data.officeItems.Add(new OfficeItemSaveData(key, item.itemData.currentLevel));
        }

        // The Online City unlock is permanent (CityOnline keeps it; this copy keeps older readers of the save right)
        bool unlocked = CityOnline.Instance != null && CityOnline.Instance.ReachedUnlock;
        data.hasUnlockedOnlineWorld = unlocked || (lastSnapshot != null && lastSnapshot.hasUnlockedOnlineWorld);

        data.companyName = CompanyNameUI.GetCompanyName();
        data.extras = CaptureExtras();

        return data;
    }

    List<ExtraSaveEntry> CaptureExtras()
    {
        // Start from the known entries so systems that aren't in this scene keep their data
        var captured = new Dictionary<string, string>(knownExtras);
        var keysThisSave = new HashSet<string>();

        foreach (var participant in FindParticipants())
        {
            string key = SafeKey(participant);
            if (key == null) continue;
            if (!keysThisSave.Add(key))
            {
                Debug.LogWarning($"[SaveSystem] Two save participants use the key '{key}' - only the first is saved.");
                continue;
            }

            try
            {
                captured[key] = participant.CaptureState() ?? "";
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveSystem] Saving '{key}' failed (its previous data is kept): {e}");
            }
        }

        knownExtras = captured;
        var list = new List<ExtraSaveEntry>();
        foreach (var pair in captured)
            list.Add(new ExtraSaveEntry(pair.Key, pair.Value));
        return list;
    }

    static List<ISaveParticipant> FindParticipants()
    {
        var list = new List<ISaveParticipant>();
        foreach (var behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (behaviour is ISaveParticipant participant)
                list.Add(participant);
        }
        return list;
    }

    static string SafeKey(ISaveParticipant participant)
    {
        try
        {
            string key = participant.SaveKey;
            return string.IsNullOrEmpty(key) ? null : key;
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveSystem] A save participant threw from SaveKey: {e.Message}");
            return null;
        }
    }

    static string OfficeItemKey(OfficeItemInteractable item)
    {
        // OfficeItemInteractable.Start uses the object name when itemName is empty
        return !string.IsNullOrEmpty(item.itemData.itemName) ? item.itemData.itemName : item.gameObject.name;
    }

    /// <summary>
    /// No CompanyManager in this scene: write the states of the save participants that are here (such as the
    /// persistent CityOnline) into the existing local save and keep everything else in it.
    /// Money isn't touched here: ApplyMoneyDeltaToSave patches it when it changes.
    /// </summary>
    bool SaveWithoutCompany(SaveTrigger trigger)
    {
        if (FindParticipants().Count == 0)
        {
            Log("Nothing to save in this scene");
            return false;
        }

        SaveData data = ReadLocalSave();
        if (data == null || LooksEmpty(data))
        {
            Log("No company in this scene and no local save to update - skipped");
            return false;
        }

        // The file's entries first, so systems that aren't in this scene keep their data
        knownExtras = new Dictionary<string, string>();
        foreach (var entry in data.extras)
        {
            if (entry != null && !string.IsNullOrEmpty(entry.key))
                knownExtras[entry.key] = entry.json ?? "";
        }
        data.extras = CaptureExtras();

        if (CityOnline.Instance != null && CityOnline.Instance.ReachedUnlock)
            data.hasUnlockedOnlineWorld = true;
        data.savedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        bool written = WriteLocalSave(data, trigger);
        lastLocalWriteTime = Time.realtimeSinceStartup;
        return written;
    }

    /// <summary>
    /// Add (or subtract) money in the saved game when no CompanyManager is loaded (e.g. buying a spot in a city).
    /// Patches the local save file and keeps everything else in it. Returns false if there is no local save.
    /// </summary>
    public bool ApplyMoneyDeltaToSave(double delta)
    {
        var company = CompanyManager.Instance;
        if (company != null && company == loadedCompany)
        {
            // A loaded company is the live copy: the next save writes it
            company.AddMoney(delta, true);
            return true;
        }

        if (SavingSuspended) return false;

        if (lastSnapshot != null)
            lastSnapshot.companyValue = Math.Max(0.0, lastSnapshot.companyValue + delta);

        SaveData data = ReadLocalSave();
        if (data == null || LooksEmpty(data)) return false;

        data.companyValue = Math.Max(0.0, data.companyValue + delta);
        data.savedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        bool written = WriteLocalSave(data, SaveTrigger.Auto);
        Log($"Money in the save changed by {delta:N0} (now ${data.companyValue:N0})");
        return written;
    }

    // ------------------------------------------------------------------
    // Local file
    // ------------------------------------------------------------------

    bool WriteLocalSave(SaveData data, SaveTrigger trigger)
    {
        try
        {
            data.saveVersion = SaveData.CurrentVersion;
            CloudSaveSync.StampLocalWrite(data); // run id, seq + 1, owner
            string json = JsonUtility.ToJson(data, false); // compact: about half the size of the indented form
            WriteFileSafely(LocalSaveFilePath, json);
            Log($"Saved locally (Day {data.currentDay}, ${data.companyValue:N0}, seq {data.seq})");
            CloudSaveSync.NotifyLocalWrite(trigger != SaveTrigger.Auto);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveSystem] Local save error: {e.Message}\n{e.StackTrace}");
            return false;
        }
    }

    /// <summary>Write to a temp file and swap it in, keeping the previous save as .prev.</summary>
    static void WriteFileSafely(string path, string contents)
    {
        lock (FileLock)
            WriteFileSafelyLocked(path, contents);
    }

    static void WriteFileSafelyLocked(string path, string contents)
    {
        string directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        string temp = path + ".tmp";
        string previous = path + ".prev";
        File.WriteAllText(temp, contents);

        if (File.Exists(path))
        {
            try
            {
                File.Replace(temp, path, previous, true);
                return;
            }
            catch (Exception)
            {
                // Some file systems don't support Replace: copy + move instead
            }

            if (File.Exists(path))
            {
                File.Copy(path, previous, true);
                File.Delete(path);
            }
        }

        if (File.Exists(temp))
            File.Move(temp, path);
    }

    /// <summary>Read the local save (falls back to the temp / previous copy if the main file is damaged).</summary>
    static SaveData ReadLocalSave()
    {
        return ReadLocalSave(out _);
    }

    /// <summary>The local save and the exact text it was read from (the main file, else its temp / previous copy).</summary>
    static SaveData ReadLocalSave(out string text)
    {
        return ReadSaveFile(LocalSaveFilePath, out text);
    }

    /// <summary>
    /// The save at <paramref name="path"/> (else its temp / previous copy) and the exact text it was read from. Uses no Unity
    /// API but JsonUtility, so it may run on a worker thread (the cloud save reads big saves off the main thread).
    /// </summary>
    public static bool TryReadSaveFileText(string path, out string text, out SaveData data)
    {
        data = ReadSaveFile(path, out text);
        return data != null;
    }

    static SaveData ReadSaveFile(string path, out string text)
    {
        text = null;
        foreach (string candidate in new[] { path, path + ".tmp", path + ".prev" })
        {
            try
            {
                // Only the file access holds the lock (not the parsing of a big save)
                string contents;
                lock (FileLock)
                {
                    if (!File.Exists(candidate)) continue;
                    contents = File.ReadAllText(candidate);
                }
                SaveData data = JsonUtility.FromJson<SaveData>(contents);
                if (data == null) continue;
                data.EnsureLists();
                if (candidate != path)
                    Debug.LogWarning($"[SaveSystem] Main save file missing or unreadable - using {Path.GetFileName(candidate)}");
                text = contents;
                return data;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SaveSystem] Couldn't read {Path.GetFileName(candidate)}: {e.Message}");
            }
        }
        return null;
    }

    /// <summary>Read the local save without a SaveSystem instance (e.g. to show save info in the main menu).</summary>
    public static bool TryReadLocalSave(out SaveData data)
    {
        data = ReadLocalSave();
        return data != null;
    }

    /// <summary>The local save and the exact JSON text it was read from (the cloud save uploads that text as is).</summary>
    public static bool TryReadLocalSaveText(out string text, out SaveData data)
    {
        data = ReadLocalSave(out text);
        return data != null;
    }

    /// <summary>
    /// Replace the local save with this exact text (a save downloaded by CloudSaveSync), keeping the old file as .prev.
    /// Not a game save: no seq is added and nothing is uploaded. False if it couldn't be written. (Under the files' lock.)
    /// </summary>
    public static bool WriteLocalSaveText(string json)
    {
        if (string.IsNullOrEmpty(json)) return false;
        try
        {
            WriteFileSafely(LocalSaveFilePath, json);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveSystem] Couldn't replace the local save: {e.Message}");
            return false;
        }
    }

    /// <summary>True if a local save file exists (works without a SaveSystem instance).</summary>
    public static bool LocalSaveExists()
    {
        string path = LocalSaveFilePath;
        return File.Exists(path) || File.Exists(path + ".tmp") || File.Exists(path + ".prev");
    }

    /// <summary>Delete the local save and its backups (works without a SaveSystem instance).</summary>
    public static void DeleteLocalSaveFiles()
    {
        string path = LocalSaveFilePath;
        foreach (string file in new[] { path, path + ".tmp", path + ".prev" })
        {
            try
            {
                lock (FileLock)
                {
                    if (File.Exists(file))
                        File.Delete(file);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveSystem] Couldn't delete {Path.GetFileName(file)}: {e.Message}");
            }
        }
    }

    // ------------------------------------------------------------------
    // New game / reset
    // ------------------------------------------------------------------

    /// <summary>
    /// Forget everything about the current game in memory (the main menu calls this for New Game, after deleting
    /// the save files). Saving works again for the next game.
    /// </summary>
    public void PrepareForNewGame()
    {
        if (loadRoutine != null)
        {
            StopCoroutine(loadRoutine);
            loadRoutine = null;
        }
        loadedCompany = null;
        lastSnapshot = null;
        knownExtras.Clear();
        SavingSuspended = false;

        // Autosave back to the Inspector settings
        autoSaveOnSceneChange = true;
        autoSaveOnQuit = initialAutoSaveOnQuit;
        autoSaveInterval = initialAutoSaveInterval;
        Log("Ready for a new game");
    }

    /// <summary>Stop all saving until ResumeSaving (used by DebugTools' complete reset).</summary>
    public void SuspendSaving()
    {
        SavingSuspended = true;
        Log("Saving suspended");
    }

    public void ResumeSaving()
    {
        SavingSuspended = false;
        Log("Saving resumed");
    }

    /// <summary>
    /// Delete the local save files
    /// </summary>
    public void DeleteSave()
    {
        DeleteLocalSaveFiles();
        Log("Local save file deleted");
        lastSnapshot = null;
        knownExtras.Clear();
    }

    /// <summary>
    /// True if there is a game to continue (a local save file).
    /// </summary>
    public bool HasSaveFile()
    {
        return LocalSaveExists();
    }

    /// <summary>
    /// Returns true if local save file exists
    /// </summary>
    public bool HasLocalSave()
    {
        return LocalSaveExists();
    }

    /// <summary>
    /// Get the local save path
    /// </summary>
    public string GetLocalSavePath()
    {
        return LocalSaveFilePath;
    }

    /// <summary>
    /// Debug: Print save file location
    /// </summary>
    [ContextMenu("Print Save Location")]
    public void PrintSaveLocation()
    {
        Debug.Log($"[SaveSystem] Local save path: {LocalSaveFilePath}");
        Debug.Log($"[SaveSystem] File exists: {LocalSaveExists()}");
        Debug.Log($"[SaveSystem] Game loaded in this scene: {IsGameLoaded}");
    }

    void Log(string message)
    {
        if (enableDebugLogs)
        {
            Debug.Log($"[SaveSystem] {message}");
        }
    }
}
