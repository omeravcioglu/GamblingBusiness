using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Runs the first-day tour and the Board's one-time feature memos (TutorialView draws them). Lives next to the scene's
/// FeatureGateRunner (both are created automatically in every scene with a CompanyManager).
/// - The tour: once per player profile (FeatureGate.TourDone), on a NEW company, right after it is named (CompanyNameUI)
///   and the first CVs have landed, never for a loaded save. Welcome memo, then five spotlight steps (the company ledger,
///   the CV, Skip / Hire, the hiring-task board, the locked tabs). Skip tour, Skip the tour or Escape end it. ReplayTour
///   shows it again at the next calm moment.
/// - Memos: when FeatureGateRunner owes one (a feature opened in this run and this profile hasn't seen its memo), it opens
///   once the screen has been calm for a second. The risk-card and crisis memos wait for the first card / crisis
///   (EventManager and MiniGameManager ask HoldForMemo when one is due), and the first board meeting waits for its memo.
///   A memo about a feature with a HUD tab spotlights the tab, and the tab pulses when the memo closes.
/// - Both pause the game (a UIManager panel; Escape closes), wait for a calm screen (RunEndDirector.IsScreenCalm, no build
///   mode, no click under way, not broke) and never block anything if they can't be shown: a system is never kept waiting
///   more than maxMemoHoldSeconds.
/// </summary>
[DisallowMultipleComponent]
public class TutorialDirector : MonoBehaviour
{
    enum Mode { Idle, Tour, Memo }

    public static TutorialDirector Instance { get; private set; }

    const string HasSetNameKey = "HasSetCompanyName";   // CompanyNameUI's PlayerPrefs flag

    [Header("Timing (real seconds)")]
    [Tooltip("Calm after the company is named before the tour opens (the first CVs land meanwhile)")]
    [SerializeField] float tourCalmSeconds = 1.5f;
    [Tooltip("With no company-name popup coming up, the tour opens after this much calm anyway")]
    [SerializeField] float noNamePopupSeconds = 4f;
    [Tooltip("Calm before an owed memo opens")]
    [SerializeField] float memoCalmSeconds = 1f;
    [Tooltip("A risk card, crisis or board meeting never waits longer than this for its memo")]
    [SerializeField] float maxMemoHoldSeconds = 30f;

    [Header("Debug")]
    [SerializeField] bool logSteps = true;

    TutorialView view;
    Mode mode;
    int tourStep;              // 0 = the welcome memo, 1..TutorialContent.StepCount = spotlight steps
    bool tourRequested;        // ReplayTour
    bool tourShownThisScene;
    Feature memoFeature;
    float calmTime;
    float calmSinceMemo = 1e6f;   // Demo: calm seconds since the last memo closed (high: none yet, so the first one is on time)
    bool demoPreview;             // Demo: the debug menu's demo welcome (the tour shows the demo's words once)
    float noNameTime;
    readonly float[] holdTime = new float[FeatureGate.All.Count];
    readonly List<(Button button, bool interactable)> deskButtons = new List<(Button button, bool interactable)>();
    Coroutine pulse;
    RectTransform pulsed;

    /// <summary>The tour or a memo is on screen.</summary>
    public bool IsShowing => mode != Mode.Idle;

    public bool IsTourOpen => mode == Mode.Tour;

    // ------------------------------------------------------------------
    // Life cycle
    // ------------------------------------------------------------------

    /// <summary>The first-day tour ended: true when the player went through to its end, false when it was skipped or cut short.</summary>
    public static event Action<bool> TourEnded;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Instance = null;
        TourEnded = null;
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[Tutorial] Removed a second TutorialDirector on '" + name + "'.");
            Destroy(this);
            return;
        }
        Instance = this;
    }

    void Start()
    {
        // Built early, so its canvas has its size by the time anything shows
        EnsureView();
    }

    void OnEnable()
    {
        L10n.LanguageChanged += OnLanguageChanged;
    }

    void OnDisable()
    {
        L10n.LanguageChanged -= OnLanguageChanged;
        // Leaving the scene while something shows: never leave the game paused
        if (mode != Mode.Idle)
        {
            mode = Mode.Idle;
            RestoreDeskButtons();
            if (view != null) view.Hide();
            if (UIManager.Exists) UIManager.Instance.ClosePanel(this);
        }
    }

    void OnDestroy()
    {
        if (UIManager.Exists) UIManager.Instance.ClosePanel(this);
        if (Instance == this) Instance = null;
    }

    // Words and fonts follow the language (the view lays out again over the next frames)
    void OnLanguageChanged()
    {
        if (mode != Mode.Idle) ShowCurrent();
    }

    TutorialView EnsureView()
    {
        if (view == null)
        {
            view = GetComponent<TutorialView>();
            if (view == null) view = gameObject.AddComponent<TutorialView>();
        }
        return view.EnsureBuilt() ? view : null;
    }

    // ------------------------------------------------------------------
    // Frame
    // ------------------------------------------------------------------

    void Update()
    {
        if (mode != Mode.Idle)
        {
            // UI state was reset (e.g. by a debug tool) while showing: keep the game paused
            if (UIManager.Exists && !UIManager.Instance.IsPanelOpen(this))
                UIManager.Instance.OpenPanel(this, true, mode == Mode.Tour ? (Action)SkipTour : CloseMemo);
            if (view == null || !view.IsOpen)
            {
                // Something hid the view: don't leave the game paused behind nothing
                if (mode == Mode.Tour) FinishTour("the view was closed");
                else CloseMemo();
                return;
            }
            HandleKeys();
            return;
        }

        FeatureGateRunner runner = FeatureGateRunner.Instance;
        if (runner == null || !runner.IsStateKnown) return;

        bool calm = IsCalm();
        calmTime = calm ? calmTime + Time.unscaledDeltaTime : 0f;
        if (calm) calmSinceMemo += Time.unscaledDeltaTime;

        if (WantsTour(runner))
        {
            if (calm && TourReady()) StartTour();
            return;   // memos wait for the tour
        }

        if (!calm || calmTime < memoCalmSeconds) return;
        // Demo: the compressed schedule must not stack memos (held first-use memos come through HoldForMemo regardless)
        if (DemoMode.Active && calmSinceMemo < DemoMode.Config.minMemoGapSeconds) return;
        Feature? next = runner.NextOwedMemo(false);
        if (next.HasValue) ShowMemo(next.Value, false);
    }

    void HandleKeys()
    {
        if (UIManager.IsTypingInInputField()) return;
        if (!Input.GetKeyDown(KeyCode.Return) && !Input.GetKeyDown(KeyCode.KeypadEnter)) return;
        if (mode == Mode.Tour) Next();
        else if (mode == Mode.Memo) CloseMemo();
    }

    /// <summary>
    /// Nothing else is going on: no panel, crisis, risk card, typing or bet in play, the game is loaded and running
    /// (RunEndDirector.IsScreenCalm), no build mode or run-end screen, no click under way, and not broke (the bankruptcy
    /// hearing goes first).
    /// </summary>
    static bool IsCalm()
    {
        if (!RunEndDirector.IsScreenCalm()) return false;
        RunEndDirector director = RunEndDirector.Instance;
        if (director != null)
        {
            if (director.IsRunOver || director.IsAnyScreenOpen) return false;
            CompanyManager company = CompanyManager.Instance;
            if (company != null && company.companyValue <= director.bankruptcyThreshold) return false;
        }
        if (BuildModeCamera.Instance != null && BuildModeCamera.Instance.IsInBuildMode()) return false;
        if (Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButtonUp(0) || Input.GetMouseButtonUp(1)) return false;
        return true;
    }

    // ------------------------------------------------------------------
    // The tour
    // ------------------------------------------------------------------

    /// <summary>Show the first-day tour again at the next calm moment (e.g. from a "Replay tutorial" button or the debug menu).</summary>
    public void ReplayTour()
    {
        tourRequested = true;
        if (logSteps) Debug.Log("[Tutorial] The tour will replay at the next calm moment.");
    }

    /// <summary>Debug (Tools > Game > Tutorial > Show Demo Welcome): replay the tour with the Steam demo's words, demo or not.</summary>
    public void DebugShowDemoWelcome()
    {
        demoPreview = true;
        ReplayTour();
    }

    bool WantsTour(FeatureGateRunner runner)
    {
        if (tourRequested) return true;
        return runner.IsNewRun && !tourShownThisScene && !FeatureGate.TourDone;
    }

    /// <summary>A new game's tour opens right after the company is named and the first CVs have landed.</summary>
    bool TourReady()
    {
        if (tourRequested) return calmTime >= 0.3f;

        CompanyNameUI names = CompanyNameUI.Instance;
        if (names != null && names.IsOpen) return false;
        if (PlayerPrefs.GetInt(HasSetNameKey, 0) != 1)
        {
            // Not named: the name popup may not exist in this scene; start anyway after a while
            noNameTime += Time.unscaledDeltaTime;
            if (noNameTime < noNamePopupSeconds) return false;
        }
        GameFlowManager flow = TutorialTargets.Flow;
        if (flow != null && flow.IsSpawning) return false;
        return calmTime >= tourCalmSeconds;
    }

    void StartTour()
    {
        tourRequested = false;
        tourShownThisScene = true;
        mode = Mode.Tour;
        tourStep = 0;
        OpenPanel(SkipTour);
        if (!ShowCurrent())
        {
            Abort("the tour couldn't be shown");
            return;
        }
        GameSfx.Play(Sfx.UiBong);
        if (logSteps) Debug.Log("[Tutorial] First-day tour started.");
    }

    /// <summary>Next (and Show me around): the next step, or the end after the last one.</summary>
    void Next()
    {
        if (mode != Mode.Tour) return;
        RestoreDeskButtons();
        tourStep++;
        if (tourStep > TutorialContent.StepCount)
        {
            FinishTour("finished");
            return;
        }
        TourStep step = TutorialContent.Step(tourStep);
        if (step.target == TourTarget.SkipHire) TutorialTargets.ShowDeskButtonsLive(deskButtons);
        if (!ShowCurrent()) FinishTour("a step couldn't be shown");
    }

    void SkipTour()
    {
        if (mode == Mode.Tour) FinishTour(tourStep == 0 ? "skipped at the welcome memo" : "skipped at step " + tourStep);
    }

    void FinishTourFromButton()
    {
        if (mode == Mode.Tour) FinishTour("finished");
    }

    void FinishTour(string how)
    {
        FeatureGate.MarkTourDone();
        demoPreview = false;   // Demo
        CloseCurrent();
        if (logSteps) Debug.Log("[Tutorial] First-day tour " + how + ".");
        try { TourEnded?.Invoke(how == "finished"); }
        catch (Exception e) { Debug.LogException(e); }
    }

    // ------------------------------------------------------------------
    // Memos
    // ------------------------------------------------------------------

    /// <summary>FeatureGate.HoldForMemo: true while the feature's first-appearance memo is owed or on screen.</summary>
    public bool HoldForMemo(Feature feature)
    {
        int i = (int)feature;
        if (mode == Mode.Memo && memoFeature == feature) return true;
        FeatureGateRunner runner = FeatureGateRunner.Instance;
        if (runner == null || !runner.IsMemoOwed(feature))
        {
            if (i >= 0 && i < holdTime.Length) holdTime[i] = 0f;
            return false;
        }
        if (mode == Mode.Idle && IsCalm())
        {
            ShowMemo(feature, false);
            if (mode == Mode.Memo) return true;
        }
        // Can't show it yet (the tour, another memo, a busy screen): wait, but never for long
        if (i >= 0 && i < holdTime.Length)
        {
            holdTime[i] += Time.unscaledDeltaTime;
            if (holdTime[i] >= maxMemoHoldSeconds)
            {
                holdTime[i] = 0f;
                runner.DropMemo(feature);
                Debug.LogWarning("[Tutorial] The " + feature + " memo couldn't come up for " + maxMemoHoldSeconds + " s; going ahead without it.");
                return false;
            }
        }
        return true;
    }

    void ShowMemo(Feature feature, bool debug)
    {
        mode = Mode.Memo;
        memoFeature = feature;
        FeatureGateRunner runner = FeatureGateRunner.Instance;
        if (!debug && runner != null)
        {
            runner.MemoShown(feature);
            // Demo: the casino memo explains heat too, so the heat memo never comes on its own
            if (DemoMode.Active && feature == Feature.Casino && !FeatureGate.HasSeenMemo(Feature.Heat)) runner.MemoShown(Feature.Heat);
        }
        OpenPanel(CloseMemo);
        if (!ShowCurrent())
        {
            Abort("the " + feature + " memo couldn't be shown");
            return;
        }
        GameSfx.Play(Sfx.UiOpen);
        if (logSteps) Debug.Log("[Tutorial] Board memo: " + feature + (debug ? " (debug)" : "") + ".");
    }

    void CloseMemo()
    {
        if (mode != Mode.Memo) return;
        Feature feature = memoFeature;
        CloseCurrent();
        if (FeatureGate.HasTab(feature)) PulseTab(feature);
    }

    /// <summary>Debug (Tools > Game > Tutorial > Show Memo): show a feature's memo now, whatever was seen.</summary>
    public void DebugShowMemo(Feature feature)
    {
        if (mode != Mode.Idle)
        {
            Debug.LogWarning("[Tutorial] Something is already showing.");
            return;
        }
        if (!FeatureGate.HasMemo(feature))
        {
            Debug.Log("[Tutorial] " + feature + " has no memo (the $1B certificate announces the City).");
            return;
        }
        ShowMemo(feature, true);
    }

    // ------------------------------------------------------------------
    // Showing
    // ------------------------------------------------------------------

    void OpenPanel(Action onEscape)
    {
        calmTime = 0f;
        UIManager ui = UIManager.Instance;
        if (ui != null) ui.OpenPanel(this, true, onEscape);
    }

    /// <summary>Fill the view with what is showing now (also after a language change). False if it couldn't be shown.</summary>
    bool ShowCurrent()
    {
        try
        {
            TutorialView v = EnsureView();
            if (v == null) return false;
            if (mode == Mode.Tour)
            {
                bool demo = DemoMode.Active || demoPreview;   // Demo: the demo's words (or the debug preview of them)
                if (tourStep <= 0)
                {
                    v.ShowWelcome(TutorialContent.Welcome(demo), Next, SkipTour);
                }
                else
                {
                    TourStep step = TutorialContent.Step(tourStep, demo);
                    v.ShowStep(step, tourStep, TutorialContent.StepCount,
                               space => TutorialTargets.TryFind(step, space, out Rect spot) ? spot : (Rect?)null,
                               Next, SkipTour, FinishTourFromButton);
                }
            }
            else if (mode == Mode.Memo)
            {
                Feature feature = memoFeature;
                Func<RectTransform, Rect?> tab = null;
                if (FeatureGate.HasTab(feature))
                    tab = space => TutorialTargets.TryFindTab(feature, space, out Rect spot, out _) ? spot : (Rect?)null;
                v.ShowMemo(TutorialContent.Memo(feature), tab, CloseMemo);
            }
            return v.IsOpen;
        }
        catch (Exception e)
        {
            Debug.LogException(e, this);
            return false;
        }
    }

    void CloseCurrent()
    {
        // Demo: the memo gap counts from a closed memo; the tour keeps the first memo after it on time
        if (mode == Mode.Memo) calmSinceMemo = 0f;
        mode = Mode.Idle;
        RestoreDeskButtons();
        if (view != null) view.Hide();
        if (UIManager.Exists) UIManager.Instance.ClosePanel(this);
        calmTime = 0f;
    }

    /// <summary>Showing failed: close everything so the game goes on (a broken tour isn't retried).</summary>
    void Abort(string why)
    {
        Debug.LogWarning("[Tutorial] " + why + "; the game goes on without it.");
        if (mode == Mode.Tour)
        {
            FeatureGate.MarkTourDone();
            demoPreview = false;   // Demo
        }
        CloseCurrent();
    }

    void RestoreDeskButtons()
    {
        TutorialTargets.RestoreDeskButtons(deskButtons);
    }

    // ------------------------------------------------------------------
    // Tab pulse
    // ------------------------------------------------------------------

    /// <summary>After a memo about a feature with a tab: the tab swells twice so the eye finds it.</summary>
    void PulseTab(Feature feature)
    {
        HudTab tab = FeatureTabs.TabFor(TutorialTargets.Hud, feature);
        if (tab == null || !tab.isActiveAndEnabled) return;
        RectTransform target = tab.swing != null ? tab.swing : tab.transform as RectTransform;
        if (target == null) return;
        if (pulse != null)
        {
            StopCoroutine(pulse);
            if (pulsed != null) pulsed.localScale = Vector3.one;
        }
        pulsed = target;
        pulse = StartCoroutine(Pulse(target));
    }

    IEnumerator Pulse(RectTransform target)
    {
        const float Seconds = 0.45f;
        for (int n = 0; n < 2; n++)
        {
            for (float t = 0f; t < Seconds; t += Time.unscaledDeltaTime)
            {
                if (target == null) yield break;
                float s = 1f + 0.12f * Mathf.Sin(t / Seconds * Mathf.PI);
                target.localScale = new Vector3(s, s, 1f);
                yield return null;
            }
        }
        if (target != null) target.localScale = Vector3.one;
        pulse = null;
        pulsed = null;
    }
}
