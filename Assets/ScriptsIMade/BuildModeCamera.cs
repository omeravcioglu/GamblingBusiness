using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Build mode switch. Press Q at the desk (or the HUD's Build tab) to open the 2D HQ tower; Q, Escape or its Back to
/// office button leaves it. The 3D office and its camera are not moved: the tower screen (TowerView) covers the screen
/// while build mode is open and TowerBuilder does the building. The HUD objects in uiToHideInBuildMode are hidden
/// meanwhile (only the ones that were visible are shown again).
/// Build mode is a pausing UIManager panel (TowerBuilder registers it), so the day, desk interaction and interrupts wait
/// while it's open. Missing TowerBuilder / TowerView / BuildGambleView / OfficeCapacityChip components are added to this object.
/// </summary>
public class BuildModeCamera : MonoBehaviour
{
    public static BuildModeCamera Instance;

    public enum CameraMode
    {
        Desk,   // Normal desk view
        Build   // HQ tower build mode
    }

    [Header("Mode Settings")]
    public CameraMode currentMode = CameraMode.Desk;
    public KeyCode toggleKey = KeyCode.Q;

    [Header("UI")]
    [Tooltip("Optional extra UI to show in build mode")]
    public GameObject buildModeUI;
    [Tooltip("UI elements to hide in build mode (only the ones that were visible are shown again)")]
    public GameObject[] uiToHideInBuildMode;

    /// <summary>Fires when build mode starts (true) or ends (false).</summary>
    public event System.Action<bool> ModeChanged;

    // Internal state
    private readonly List<GameObject> hiddenByBuildMode = new List<GameObject>();
    private TowerBuilder builder;

    /// <summary>The camera that shows the world (build mode no longer moves a camera).</summary>
    public Camera ActiveCamera => Camera.main;

    TowerBuilder Builder
    {
        get
        {
            if (builder == null)
                builder = TowerBuilder.Instance != null ? TowerBuilder.Instance : FindAnyObjectByType<TowerBuilder>(FindObjectsInactive.Include);
            return builder;
        }
    }

    void Awake()
    {
        Instance = this;
        EnsureTowerComponents();
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
        if (UIManager.Exists)
            UIManager.Instance.ClosePanel(this);
    }

    void Start()
    {
        // Start at the desk (without touching the HUD); honour Build if it was set in the Inspector
        bool startInBuildMode = currentMode == CameraMode.Build;
        currentMode = CameraMode.Desk;
        if (buildModeUI != null)
            buildModeUI.SetActive(false);
        if (startInBuildMode)
            EnterBuildMode();
    }

    void Update()
    {
        // Toggle mode with Q
        if (Input.GetKeyDown(toggleKey) && InputAllowed())
        {
            ToggleMode();
        }

        // Build mode was closed directly on the builder: bring the HUD back too
        if (currentMode == CameraMode.Build && Builder != null && !Builder.IsOpen)
        {
            EnterDeskMode();
        }
    }

    /// <summary>
    /// Build mode needs a TowerBuilder (rules, input, save), a TowerView (screen) and a BuildGambleView
    /// (filling the build budget). Any the scene doesn't have is added to this object.
    /// </summary>
    void EnsureTowerComponents()
    {
        builder = FindAnyObjectByType<TowerBuilder>(FindObjectsInactive.Include);
        if (builder == null)
            builder = gameObject.AddComponent<TowerBuilder>();
        if (FindAnyObjectByType<TowerView>(FindObjectsInactive.Include) == null)
            gameObject.AddComponent<TowerView>();
        if (FindAnyObjectByType<BuildGambleView>(FindObjectsInactive.Include) == null)
            gameObject.AddComponent<BuildGambleView>();
        // The HUD's "EMPLOYEES 12 / 20" chip and its OFFICE FULL banner (capacity is a hard limit on hiring, 2026-09-29)
        if (FindAnyObjectByType<OfficeCapacityChip>(FindObjectsInactive.Include) == null)
            gameObject.AddComponent<OfficeCapacityChip>();
    }

    /// <summary>
    /// Q only works when nothing is typed, no other panel is open (build mode's own panel doesn't count)
    /// and no mini-game or risk event is running.
    /// </summary>
    bool InputAllowed()
    {
        if (UIManager.IsTypingInInputField()) return false;
        // Build mode's own panel: the builder's, or this object's fallback when there is no builder
        object ownPanel = Builder != null ? (object)Builder : this;
        if (UIManager.Exists && UIManager.Instance.IsAnyPanelOpen(ownPanel)) return false;
        if (MiniGameManager.Instance != null && MiniGameManager.Instance.IsMiniGameActive()) return false;
        if (EventManager.Instance != null && EventManager.Instance.IsEventInProgress()) return false;
        return true;
    }

    /// <summary>Switch between desk and build mode (entering uses the same checks as the Q key).</summary>
    public void ToggleMode()
    {
        if (currentMode == CameraMode.Desk)
            TryEnterBuildMode();
        else
            ExitBuildMode();
    }

    /// <summary>Enter build mode if nothing is in the way. Returns true if build mode is active afterwards.</summary>
    public bool TryEnterBuildMode()
    {
        if (currentMode == CameraMode.Build) return true;
        if (!InputAllowed()) return false;
        // Tutorial: build mode opens on FeatureGate.BuildFromDay (until then Q and the Build tab say when, in a toast)
        if (!FeatureGate.AllowOpen(Feature.Build)) return false;
        EnterBuildMode();
        return true;
    }

    /// <summary>Leave build mode (also called by Escape and the Exit button).</summary>
    public void ExitBuildMode()
    {
        if (currentMode != CameraMode.Build) return;
        EnterDeskMode();
    }

    /// <summary>Set the mode directly (no checks). Does nothing if already in that mode.</summary>
    public void SetMode(CameraMode mode)
    {
        if (mode == currentMode) return;

        if (mode == CameraMode.Desk)
        {
            EnterDeskMode();
        }
        else
        {
            EnterBuildMode();
        }
    }

    void EnterDeskMode()
    {
        bool wasBuilding = currentMode == CameraMode.Build;
        currentMode = CameraMode.Desk;

        // Close the tower (guarded so the HUD is always restored)
        try
        {
            TowerBuilder b = Builder;
            if (b != null)
                b.Close();
            if (buildModeUI != null)
                buildModeUI.SetActive(false);
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
        }

        // Show again only what build mode hid
        foreach (var ui in hiddenByBuildMode)
        {
            if (ui != null) ui.SetActive(true);
        }
        hiddenByBuildMode.Clear();

        if (UIManager.Exists)
            UIManager.Instance.ClosePanel(this);

        // Show cursor
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        if (wasBuilding)
        {
            Debug.Log("[BuildModeCamera] Switched to DESK mode");
            GameSfx.Play(Sfx.UiMinimize);
            ModeChanged?.Invoke(false);
        }
    }

    void EnterBuildMode()
    {
        currentMode = CameraMode.Build;

        // Open the tower first: it registers build mode with UIManager (pausing; Escape steps back and then
        // leaves), so build mode can always be left again even if the UI below fails
        try
        {
            TowerBuilder b = Builder;
            if (b != null)
            {
                b.Open(ExitBuildMode);
            }
            else
            {
                Debug.LogWarning("[BuildModeCamera] No TowerBuilder in the scene, so build mode has nothing to show.");
                UIManager uiManager = UIManager.Instance;
                if (uiManager != null)
                    uiManager.OpenPanel(this, true, ExitBuildMode);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
        }

        // Hide HUD pieces that were visible
        hiddenByBuildMode.Clear();
        if (uiToHideInBuildMode != null)
        {
            foreach (var ui in uiToHideInBuildMode)
            {
                if (ui != null && ui.activeSelf)
                {
                    ui.SetActive(false);
                    hiddenByBuildMode.Add(ui);
                }
            }
        }

        if (buildModeUI != null)
            buildModeUI.SetActive(true);

        // Show cursor for building
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        Debug.Log("[BuildModeCamera] Switched to BUILD mode");
        GameSfx.Play(Sfx.UiMaximize);
        ModeChanged?.Invoke(true);
    }

    /// <summary>
    /// Check if in build mode
    /// </summary>
    public bool IsInBuildMode()
    {
        return currentMode == CameraMode.Build;
    }
}
