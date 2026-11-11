using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

/// <summary>
/// Central UI state for the whole game.
/// - Tracks which panels are open (OpenPanel / ClosePanel).
/// - Owns pausing: Time.timeScale is 0 while any panel that pauses is open, 1 otherwise.
///   Scripts should never write Time.timeScale themselves.
/// - Routes the Escape key to the top-most panel (or placement mode) that can handle it.
/// - Disables office item interaction while any panel is open.
/// Auto-creates itself on first use and survives scene loads.
/// </summary>
public class UIManager : MonoBehaviour
{
    class Entry
    {
        public object owner;
        public bool isPanel;      // counts as an open panel (blocks gameplay input and interrupts)
        public bool pausesGame;   // holds Time.timeScale at 0 while open
        public Action onEscape;   // null = Escape is not handled by this entry
    }

    static UIManager instance;
    static bool quitting;

    /// <summary>The live UIManager. Created on demand while the game is running.</summary>
    public static UIManager Instance
    {
        get
        {
            if (instance == null && !quitting && Application.isPlaying)
            {
                instance = FindAnyObjectByType<UIManager>();
                if (instance == null)
                    instance = new GameObject("UIManager").AddComponent<UIManager>();
            }
            return instance;
        }
    }

    /// <summary>True if a UIManager exists, without creating one.</summary>
    public static bool Exists => instance != null;

    /// <summary>Fires whenever the set of open panels changes. Argument: any panel open.</summary>
    public event Action<bool> PanelStateChanged;

    /// <summary>Fires when Escape is pressed and no open panel handled it (e.g. open the pause menu).</summary>
    public event Action EscapeUnhandled;

    readonly List<Entry> entries = new List<Entry>();
    int legacyOpenCount;
    bool lastAnyOpen;

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        if (transform.parent == null)
            DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy()
    {
        if (instance == this)
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            instance = null;
        }
    }

    void OnApplicationQuit()
    {
        quitting = true;
    }

    /// <summary>
    /// The game's text is English, so format and parse numbers the same way on every machine.
    /// Without this a Turkish system shows "$2.000", parses "3.8" as 38 and upper-cases "i" to "İ".
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void UseInvariantCulture()
    {
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = invariant;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = invariant;
        System.Globalization.CultureInfo.CurrentCulture = invariant;
        System.Globalization.CultureInfo.CurrentUICulture = invariant;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Single)
            ResetForSceneChange();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (!HandleEscape())
                EscapeUnhandled?.Invoke();
        }
    }

    // ------------------------------------------------------------------
    // Panels
    // ------------------------------------------------------------------

    /// <summary>
    /// Register an open panel. Call again with the same owner to update its settings.
    /// </summary>
    /// <param name="owner">Usually the panel's controller (this).</param>
    /// <param name="pauseGame">Freeze game time while this panel is open.</param>
    /// <param name="onEscape">Called when Escape is pressed and this is the top-most handler. Null = not closable by Escape.</param>
    public void OpenPanel(object owner, bool pauseGame = true, Action onEscape = null)
    {
        if (owner == null) return;
        Entry e = Find(owner);
        if (e == null)
        {
            e = new Entry { owner = owner };
            entries.Add(e);
        }
        else
        {
            // Re-opening moves it to the top of the Escape order
            entries.Remove(e);
            entries.Add(e);
        }
        e.isPanel = true;
        e.pausesGame = pauseGame;
        e.onEscape = onEscape;
        ApplyState();
    }

    /// <summary>Unregister a panel (safe to call when it isn't open).</summary>
    public void ClosePanel(object owner)
    {
        Entry e = Find(owner);
        if (e != null && e.isPanel)
        {
            entries.Remove(e);
            ApplyState();
        }
    }

    public bool IsPanelOpen(object owner)
    {
        Entry e = Find(owner);
        return e != null && e.isPanel;
    }

    /// <summary>True while any panel is open (including legacy OnPanelOpened callers).</summary>
    public bool IsAnyPanelOpen()
    {
        Prune();
        if (legacyOpenCount > 0) return true;
        foreach (var e in entries)
            if (e.isPanel) return true;
        return false;
    }

    /// <summary>True while any panel other than <paramref name="except"/> is open.</summary>
    public bool IsAnyPanelOpen(object except)
    {
        Prune();
        if (legacyOpenCount > 0) return true;
        foreach (var e in entries)
            if (e.isPanel && !ReferenceEquals(e.owner, except)) return true;
        return false;
    }

    /// <summary>True while any panel other than <paramref name="exceptA"/> and <paramref name="exceptB"/> is open.</summary>
    public bool IsAnyPanelOpen(object exceptA, object exceptB)
    {
        Prune();
        if (legacyOpenCount > 0) return true;
        foreach (var e in entries)
            if (e.isPanel && !ReferenceEquals(e.owner, exceptA) && !ReferenceEquals(e.owner, exceptB)) return true;
        return false;
    }

    /// <summary>True while a panel is holding the game paused.</summary>
    public bool IsGamePaused
    {
        get
        {
            Prune();
            foreach (var e in entries)
                if (e.isPanel && e.pausesGame) return true;
            return false;
        }
    }

    // ------------------------------------------------------------------
    // Escape handling for non-panel modes (e.g. build placement)
    // ------------------------------------------------------------------

    /// <summary>Give Escape to <paramref name="onEscape"/> until removed, without counting as a panel.</summary>
    public void PushEscapeHandler(object owner, Action onEscape)
    {
        if (owner == null || onEscape == null) return;
        Entry e = Find(owner);
        if (e != null && e.isPanel)
        {
            e.onEscape = onEscape;
            return;
        }
        if (e != null) entries.Remove(e);
        entries.Add(new Entry { owner = owner, isPanel = false, pausesGame = false, onEscape = onEscape });
    }

    public void RemoveEscapeHandler(object owner)
    {
        Entry e = Find(owner);
        if (e != null && !e.isPanel)
            entries.Remove(e);
    }

    /// <summary>Send Escape to the top-most handler. Returns true if something handled it.</summary>
    public bool HandleEscape()
    {
        Prune();
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            if (entries[i].onEscape != null)
            {
                GameSfx.PlayIfUiChanges(Sfx.UiBack);   // heard only if the handler closed or stepped back something
                entries[i].onEscape.Invoke();
                return true;
            }
        }
        return false;
    }

    // ------------------------------------------------------------------
    // Legacy API (kept for older callers; does not pause)
    // ------------------------------------------------------------------

    public void OnPanelOpened()
    {
        legacyOpenCount++;
        ApplyState();
    }

    public void OnPanelClosed()
    {
        legacyOpenCount = Mathf.Max(0, legacyOpenCount - 1);
        ApplyState();
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    /// <summary>Close everything and resume time. Used on scene changes and by debug tools.</summary>
    public void ResetForSceneChange()
    {
        entries.Clear();
        legacyOpenCount = 0;
        if (lastAnyOpen)
            SetInteractablesEnabled(true); // used mid-scene by debug tools: give office items their clicks back
        lastAnyOpen = false;
        Time.timeScale = 1f;
        PanelStateChanged?.Invoke(false);
    }

    /// <summary>Draw a panel above its siblings (call when opening a modal).</summary>
    public static void BringToFront(GameObject panel)
    {
        if (panel != null)
            panel.transform.SetAsLastSibling();
    }

    /// <summary>True while the player is typing into a text field (hotkeys should be ignored).</summary>
    public static bool IsTypingInInputField()
    {
        var es = EventSystem.current;
        if (es == null || es.currentSelectedGameObject == null) return false;
        var go = es.currentSelectedGameObject;
        var tmp = go.GetComponent<TMP_InputField>();
        if (tmp != null && tmp.isFocused) return true;
        var legacy = go.GetComponent<UnityEngine.UI.InputField>();
        return legacy != null && legacy.isFocused;
    }

    /// <summary>
    /// True when world interaction and gameplay hotkeys are allowed: nothing is being typed,
    /// no panel is open, and no mini-game or risk-card event is running.
    /// </summary>
    public static bool GameplayInputAllowed()
    {
        if (IsTypingInInputField()) return false;
        if (Exists && instance.IsAnyPanelOpen()) return false;
        if (MiniGameManager.Instance != null && MiniGameManager.Instance.IsMiniGameActive()) return false;
        if (EventManager.Instance != null && EventManager.Instance.IsEventInProgress()) return false;
        return true;
    }

    Entry Find(object owner)
    {
        for (int i = 0; i < entries.Count; i++)
            if (ReferenceEquals(entries[i].owner, owner)) return entries[i];
        return null;
    }

    /// <summary>Drop entries whose owner was destroyed (e.g. its scene unloaded).</summary>
    void Prune()
    {
        bool removed = false;
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            if (entries[i].owner is UnityEngine.Object uo && uo == null)
            {
                entries.RemoveAt(i);
                removed = true;
            }
        }
        if (removed) ApplyState();
    }

    void ApplyState()
    {
        // Prune without recursion
        for (int i = entries.Count - 1; i >= 0; i--)
            if (entries[i].owner is UnityEngine.Object uo && uo == null)
                entries.RemoveAt(i);

        bool pause = false, anyOpen = legacyOpenCount > 0;
        foreach (var e in entries)
        {
            if (!e.isPanel) continue;
            anyOpen = true;
            if (e.pausesGame) pause = true;
        }

        Time.timeScale = pause ? 0f : 1f;

        if (anyOpen != lastAnyOpen)
        {
            lastAnyOpen = anyOpen;
            SetInteractablesEnabled(!anyOpen);
            PanelStateChanged?.Invoke(anyOpen);
        }
    }

    void SetInteractablesEnabled(bool enabled)
    {
        foreach (var item in FindObjectsByType<OfficeItemInteractable>(FindObjectsSortMode.None))
        {
            if (item == null) continue;
            item.enabled = enabled;
            if (!enabled && item.hoverUI != null)
                item.hoverUI.SetActive(false);
        }
    }
}

/// <summary>
/// Scene changes that save the game first, while the gameplay managers still exist.
/// </summary>
public static class SceneFlow
{
    public const string MainMenuScene = "Main_Menu";
    public const string MainGameScene = "MainGame";

    /// <summary>True if the scene is in Build Settings and can be loaded.</summary>
    public static bool CanLoad(string sceneName)
    {
        return !string.IsNullOrEmpty(sceneName) && Application.CanStreamedLevelBeLoaded(sceneName);
    }

    /// <summary>
    /// Save (when a company is loaded), reset UI state, and load the scene. Returns false if it can't be loaded.
    /// Going into the game while the cloud save is syncing waits for it first (at most ~10 s, CloudSaveSync.DeferGameLoad),
    /// so Continue loads the newest save; the load then still happens (true is returned right away).
    /// </summary>
    public static bool Load(string sceneName)
    {
        if (!CanLoad(sceneName))
        {
            Debug.LogError($"[SceneFlow] Scene '{sceneName}' is not in Build Settings (File > Build Profiles), so it can't be loaded.");
            return false;
        }

        if (sceneName == MainGameScene && CloudSaveSync.DeferGameLoad(sceneName))
            return true;

        return LoadNow(sceneName);
    }

    /// <summary>Load without waiting for the cloud save: save (when a company is loaded), reset UI state, load the scene.</summary>
    public static bool LoadNow(string sceneName)
    {
        if (!CanLoad(sceneName))
        {
            Debug.LogError($"[SceneFlow] Scene '{sceneName}' is not in Build Settings (File > Build Profiles), so it can't be loaded.");
            return false;
        }

        if (SaveSystem.Instance != null && CompanyManager.Instance != null)
            SaveSystem.Instance.SaveGame();

        if (UIManager.Exists)
            UIManager.Instance.ResetForSceneChange();
        else
            Time.timeScale = 1f;

        SceneManager.LoadScene(sceneName);
        return true;
    }
}
