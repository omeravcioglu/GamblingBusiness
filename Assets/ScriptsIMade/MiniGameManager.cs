using UnityEngine;
using System;
using System.Collections.Generic;

public class MiniGameManager : MonoBehaviour
{
    public static MiniGameManager Instance;

    [Header("=== TIMING GAMES ===")]
    public GameObject arrowTimingPanel;
    public GameObject stopTheBarPanel;

    [Header("=== REACTION GAMES ===")]
    public GameObject colorMatchPanel;
    public GameObject whackAMolePanel;
    public GameObject rapidKeysPanel;

    [Header("=== MEMORY GAMES ===")]
    public GameObject simonSaysPanel;
    public GameObject numberSequencePanel;
    public GameObject sequenceMemoryPanel;
    public GameObject patternMatchPanel;

    [Header("=== PUZZLE GAMES ===")]
    public GameObject keypadCodePanel;
    public GameObject safeCrackerPanel;
    public GameObject oddOneOutPanel;

    [Header("=== SKILL GAMES ===")]
    public GameObject buttonMashPanel;
    public GameObject catchFallingPanel;

    [Header("=== QUICK GAMES ===")]
    public GameObject mathBlitzPanel;
    public GameObject quickTapPanel;
    public GameObject higherLowerPanel;
    public GameObject wordTypePanel;
    public GameObject coinFlipPanel;
    public GameObject speedCountPanel;

    [Header("Timing Settings")]
    public float minTimeBetweenGames = 30f;   // 30 seconds minimum
    public float maxTimeBetweenGames = 180f;  // 3 minutes maximum
    public bool miniGamesEnabled = true;
    public bool autoFindPanels = true;
    [Tooltip("Real-time seconds of calm after a menu, event or build mode closes before a crisis may start")]
    public float calmPeriodAfterInterruption = 2f;

    [Header("Rewards & Penalties")]
    public float successRewardBase = 2000f;
    public float failurePenaltyBase = 1000f;   // also used for time-outs and giving up (Escape)

    [Header("Difficulty")]
    [Range(1, 5)]
    public int difficultyLevel = 1;

    [Header("Progressive Difficulty")]
    [Tooltip("How much harder games get per game played (0.01 = 1% harder each game)")]
    public float difficultyIncreasePerGame = 0.015f;  // 1.5% harder each game
    [Tooltip("Maximum difficulty multiplier (2.0 = twice as hard)")]
    public float maxDifficultyMultiplier = 1.8f;      // Cap at 80% harder (reachable after ~53 games / ~1 hour)

    [Header("UI")]
    public Canvas mainCanvas;

    // AI Assistant skill (value 0.5): removes that share of the extra difficulty and adds half of it as time (+25%).
    // Rewards still follow the unassisted difficulty.
    const float MaxAIDifficultyReduction = 0.75f;
    const float AITimeBonusPerEffect = 0.5f;
    const float MaxAITimeBonus = 0.5f;
    // Coin Flip skill: a won crisis pays double this often
    const float CoinFlipDoubleChance = 0.5f;
    // ...and its id in SkillTreeManager: the reward texts name it by its name in the current language
    const string CoinFlipSkillId = "chaos_2_coin";

    private float gameTimer;
    private float nextGameTime;
    private float calmTimer;
    private bool miniGameActive = false;
    private readonly List<GameObject> availableGames = new List<GameObject>();
    private int gamesPlayedThisSession = 0;
    private float currentDifficultyMultiplier = 1f;
    private MiniGameBase activeGame;
    private GameObject lastPanel;
    private int runId;          // identifies the current run; late reports from older runs are ignored
    private bool quitting;

    // The current run's reward, decided once: the result screen shows it before the manager pays it
    private int rewardRunId = -1;
    private bool rewardSuccess;
    private float rewardAmount;
    private bool rewardDoubled;

    /// <summary>
    /// Check if a minigame is currently active (from the crisis warning until the panel closes)
    /// </summary>
    public bool IsMiniGameActive() => miniGameActive;

    // Events
    public delegate void OnMiniGameStarted(string gameName);
    public event OnMiniGameStarted MiniGameStarted;

    public delegate void OnMiniGameCompleted(bool success, float reward);
    public event OnMiniGameCompleted MiniGameCompleted;

    // Business problem descriptions - randomly selected when triggering minigames
    private static readonly string[] businessProblems = new string[]
    {
        // Employee Issues
        "An employee is threatening to quit! Resolve this quickly!",
        "Two employees are having a heated argument! Calm them down!",
        "Someone filed a complaint against management! Handle it!",
        "An employee wants a raise or they're leaving! Decide now!",
        "New hire is struggling with training! Help them out!",
        "Employee caught sleeping on the job! What do you do?",
        "Top performer got a competing offer! Convince them to stay!",

        // Client/Customer Issues
        "A major client is furious about a delay! Fix it!",
        "Customer demanding a full refund! Handle the situation!",
        "Important client meeting in 5 minutes! Prepare now!",
        "Bad review going viral online! Do damage control!",
        "Client threatening to sue! Resolve this immediately!",
        "VIP customer waiting on hold! Don't lose them!",

        // Technical/Operations
        "Server is crashing! Get systems back online!",
        "Critical bug found in production! Fix it fast!",
        "Inventory system showing errors! Correct the data!",
        "Security breach detected! Lock it down!",
        "Backup system failing! Save the data!",
        "Website traffic spike! Keep it running!",

        // Financial
        "Audit happening tomorrow! Get the books in order!",
        "Invoice discrepancy found! Reconcile the accounts!",
        "Budget overrun detected! Cut costs now!",
        "Payment processor is down! Process transactions manually!",
        "Tax deadline approaching! File the paperwork!",
        "Expense report fraud suspected! Investigate!",

        // General Crisis
        "Fire alarm went off! Manage the evacuation!",
        "Power outage in the building! Keep operations running!",
        "Delivery truck broke down! Reroute the shipment!",
        "Press asking for a statement! Craft a response!",
        "Board meeting in 10 minutes! Get your presentation ready!",
        "Competitor launched a surprise product! React quickly!",
        "Supply chain disruption! Find alternatives!",
        "Office flooded! Save the equipment!"
    };

    // What happened, per business problem above (same order): shown on the CRT's result
    private static readonly string[] solvedLines = new string[]
    {
        "They're staying. For now.",
        "Handshakes all round.",
        "Complaint closed, lessons noted.",
        "A fair deal. Everyone's happy.",
        "They've got the hang of it.",
        "Wide awake and back at it.",
        "Your star is staying put.",

        "The client calmed down.",
        "Sorted without a refund.",
        "The meeting went great.",
        "The story died down.",
        "Settled over coffee.",
        "The VIP got through.",

        "Servers are back online.",
        "Bug squashed.",
        "The numbers add up again.",
        "Breach sealed.",
        "The backups are safe.",
        "The site held up.",

        "The books are spotless.",
        "The accounts balance.",
        "Back under budget.",
        "Every payment went through.",
        "Filed with minutes to spare.",
        "Case closed.",

        "Everyone got out calmly.",
        "The lights are back on.",
        "The shipment arrived on time.",
        "A statement worth quoting.",
        "The board loved it.",
        "You stole their thunder.",
        "A new supplier stepped in.",
        "The equipment stayed dry."
    };

    private static readonly string[] failedLines = new string[]
    {
        "They walked out with a stapler.",
        "Now nobody's talking to anybody.",
        "HR wants a word. Several words.",
        "They took the other offer.",
        "Still lost in the onboarding folder.",
        "Still snoring by the printer.",
        "Your best person just left.",

        "The client is still shouting.",
        "Refund issued. In full.",
        "You winged it. It showed.",
        "It's trending. For the wrong reasons.",
        "The lawyers are circling.",
        "The VIP hung up.",

        "The server is still on fire.",
        "The bug is a feature now.",
        "Nobody knows what's in stock.",
        "The data is out there now.",
        "Some files are gone for good.",
        "The site went down under the load.",

        "The auditors found things.",
        "The numbers still don't match.",
        "Still over budget.",
        "Payments are piling up.",
        "Late fees incoming.",
        "The receipts got away.",

        "Chaos in the stairwell.",
        "Work stopped in the dark.",
        "The shipment is stuck on the highway.",
        "\u201CNo comment\u201D made the headlines.",
        "The slides didn't load.",
        "They stole your customers.",
        "The shelves are empty.",
        "The computers went for a swim."
    };

    // ------------------------------------------------------------------
    // Crisis lines in the current language. The English arrays above are the data and give the keys of the Crisis table
    // (Assets/Localization/Source/Crisis.csv): "problem." + L10n.Slug(problem) + ".text" / ".solved" / ".failed" (a repeated
    // slug gets _2, _3...). A crisis is tracked by its index into the arrays, never by the text it shows, so its result line
    // is found in every language. English shows the arrays themselves; the Editor's CrisisTextSource keeps Crisis.csv in
    // step with them (Tools > Game > Localization > Sync Content CSVs).
    // ------------------------------------------------------------------

    static string[] problemKeys;

    /// <summary>How many business problems (crisis lines) there are.</summary>
    public static int ProblemCount => businessProblems.Length;

    /// <summary>The English problem line <paramref name="index"/> (the data); "" when out of range.</summary>
    public static string EnglishProblem(int index) => index >= 0 && index < businessProblems.Length ? businessProblems[index] : "";

    /// <summary>The English result line of problem <paramref name="index"/> (the data); "" when it has none.</summary>
    public static string EnglishOutcome(int index, bool solved)
    {
        string[] lines = solved ? solvedLines : failedLines;
        return index >= 0 && index < lines.Length && index < businessProblems.Length ? lines[index] : "";
    }

    /// <summary>Index of an English problem line (as the arrays hold it); -1 when the text isn't one of them.</summary>
    public static int ProblemIndex(string englishProblem)
    {
        return string.IsNullOrEmpty(englishProblem) ? -1 : Array.IndexOf(businessProblems, englishProblem);
    }

    /// <summary>Key prefix of problem <paramref name="index"/> in the Crisis table ("problem.server_is_crashing_get_systems_back_online"); null when out of range.</summary>
    public static string ProblemKey(int index)
    {
        if (index < 0 || index >= businessProblems.Length) return null;
        if (problemKeys == null || problemKeys.Length != businessProblems.Length)
        {
            var keys = new string[businessProblems.Length];
            var used = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < keys.Length; i++)
            {
                string slug = L10n.Slug(businessProblems[i]);
                string key = "problem." + (slug.Length > 0 ? slug : "line");
                string unique = key;
                for (int n = 2; !used.Add(unique); n++) unique = key + "_" + n;
                keys[i] = unique;
            }
            problemKeys = keys;
        }
        return problemKeys[index];
    }

    /// <summary>The problem line in the current language (English: the data itself); "" when out of range.</summary>
    public static string ProblemText(int index) => LocalizedLine(index, ".text", EnglishProblem(index));

    /// <summary>One line on how a crisis ended ("Servers are back online."), in the current language, for the CRT's result.</summary>
    public static string OutcomeLine(int problemIndex, bool solved)
    {
        string english = EnglishOutcome(problemIndex, solved);
        if (english.Length == 0)
            return CrisisText.Get(solved ? "crisis.outcome.fallback.solved" : "crisis.outcome.fallback.failed");
        return LocalizedLine(problemIndex, solved ? ".solved" : ".failed", english);
    }

    /// <summary>The same, for callers holding the English problem line (an unknown line gets the general result line).</summary>
    public static string OutcomeLine(string problem, bool solved) => OutcomeLine(ProblemIndex(problem), solved);

    static string LocalizedLine(int index, string part, string english)
    {
        if (english.Length == 0 || L10n.CurrentCode == L10n.English) return english;
        string key = ProblemKey(index);
        return key == null ? english : L10n.GetOr(CrisisText.Table, key + part, english);
    }

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        // Auto-find panels if enabled
        if (autoFindPanels)
        {
            AutoFindPanels();
        }

        BuildGameList();
        // Demo: the demo's crisis timers win over the scene's
        if (DemoMode.Active)
        {
            minTimeBetweenGames = DemoMode.Config.crisisMin;
            maxTimeBetweenGames = DemoMode.Config.crisisMax;
            demoLoadCompany = CompanyManager.Instance;
            if (demoLoadCompany != null) demoLoadCompany.StateLoaded += OnDemoStateLoaded;
        }
        SetNextGameTime();
        calmTimer = calmPeriodAfterInterruption;

        Debug.Log($"[MiniGameManager] Initialized with {availableGames.Count} minigames. Next game in {nextGameTime:F0} seconds.");
    }

    void OnApplicationQuit()
    {
        quitting = true;
    }

    void OnDisable()
    {
        // Scene unloading or the manager was switched off: stop tracking the run and release the pause.
        // The game still hides itself when it ends; its late report is ignored.
        if (miniGameActive)
        {
            runId++;
            miniGameActive = false;
            activeGame = null;
        }
        if (UIManager.Exists)
            UIManager.Instance.ClosePanel(this);
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
        if (demoLoadCompany != null) demoLoadCompany.StateLoaded -= OnDemoStateLoaded;   // Demo
    }

    // Demo: a save is applied a few frames after Start; re-roll the timer once the saved first-crisis flag is known
    CompanyManager demoLoadCompany;

    void OnDemoStateLoaded()
    {
        if (!miniGameActive) SetNextGameTime();
    }

    void AutoFindPanels()
    {
        // Try to find each panel by name if not assigned
        // Timing
        if (arrowTimingPanel == null) arrowTimingPanel = FindPanel("ArrowTimingPanel");
        if (stopTheBarPanel == null) stopTheBarPanel = FindPanel("StopTheBarPanel");
        // Reaction
        if (colorMatchPanel == null) colorMatchPanel = FindPanel("ColorMatchPanel");
        if (whackAMolePanel == null) whackAMolePanel = FindPanel("WhackAMolePanel");
        if (rapidKeysPanel == null) rapidKeysPanel = FindPanel("RapidKeysPanel");
        // Memory
        if (simonSaysPanel == null) simonSaysPanel = FindPanel("SimonSaysPanel");
        if (numberSequencePanel == null) numberSequencePanel = FindPanel("NumberSequencePanel");
        if (sequenceMemoryPanel == null) sequenceMemoryPanel = FindPanel("SequenceMemoryPanel");
        if (patternMatchPanel == null) patternMatchPanel = FindPanel("PatternMatchPanel");
        // Puzzle
        if (keypadCodePanel == null) keypadCodePanel = FindPanel("KeypadCodePanel");
        if (safeCrackerPanel == null) safeCrackerPanel = FindPanel("SafeCrackerPanel");
        if (oddOneOutPanel == null) oddOneOutPanel = FindPanel("OddOneOutPanel");
        // Skill
        if (buttonMashPanel == null) buttonMashPanel = FindPanel("ButtonMashPanel");
        if (catchFallingPanel == null) catchFallingPanel = FindPanel("CatchFallingPanel");
        // Quick
        if (mathBlitzPanel == null) mathBlitzPanel = FindPanel("MathBlitzPanel");
        if (quickTapPanel == null) quickTapPanel = FindPanel("QuickTapPanel");
        if (higherLowerPanel == null) higherLowerPanel = FindPanel("HigherLowerPanel");
        if (wordTypePanel == null) wordTypePanel = FindPanel("WordTypePanel");
        if (coinFlipPanel == null) coinFlipPanel = FindPanel("CoinFlipPanel");
        if (speedCountPanel == null) speedCountPanel = FindPanel("SpeedCountPanel");
    }

    /// <summary>Panels are direct children of the main canvas (or of any canvas as a fallback).</summary>
    GameObject FindPanel(string panelName)
    {
        if (mainCanvas != null)
        {
            var found = mainCanvas.transform.Find(panelName);
            if (found != null) return found.gameObject;
        }

        foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (canvas == null) continue;
            var found = canvas.transform.Find(panelName);
            if (found != null)
            {
                if (mainCanvas == null) mainCanvas = canvas;
                return found.gameObject;
            }
        }
        return null;
    }

    void BuildGameList()
    {
        availableGames.Clear();

        // Timing Games
        AddGame(arrowTimingPanel);
        AddGame(stopTheBarPanel);

        // Reaction Games
        AddGame(colorMatchPanel);
        AddGame(whackAMolePanel);
        AddGame(rapidKeysPanel);

        // Memory Games
        AddGame(simonSaysPanel);
        AddGame(numberSequencePanel);
        AddGame(sequenceMemoryPanel);
        AddGame(patternMatchPanel);

        // Puzzle Games
        AddGame(keypadCodePanel);
        AddGame(safeCrackerPanel);
        AddGame(oddOneOutPanel);

        // Skill Games
        AddGame(buttonMashPanel);
        AddGame(catchFallingPanel);

        // Quick Games
        AddGame(mathBlitzPanel);
        AddGame(quickTapPanel);
        AddGame(higherLowerPanel);
        AddGame(wordTypePanel);
        AddGame(coinFlipPanel);
        AddGame(speedCountPanel);

        // Hide all at start
        foreach (var game in availableGames)
        {
            if (game != null) game.SetActive(false);
        }
    }

    void AddGame(GameObject panel)
    {
        if (panel == null || availableGames.Contains(panel)) return;

        if (panel.GetComponent<MiniGameBase>() == null && GetScriptType(panel) == null)
        {
            Debug.LogWarning($"[MiniGameManager] {panel.name} has no minigame script - left out of the rotation.");
            return;
        }
        availableGames.Add(panel);
    }

    void Update()
    {
        if (miniGameActive)
        {
            WatchActiveGame();
            return;
        }
        if (!miniGamesEnabled) return;

        // Game time only, and it waits while any panel is open (like the day timer), so closing the skill tree or
        // stock market doesn't set off a crisis that came due while browsing. The timer is never lost while blocked.
        // Tutorial: no crises before FeatureGate.CrisesFromDay
        if (FeatureGate.IsUnlocked(Feature.Crises) && (!UIManager.Exists || !UIManager.Instance.IsAnyPanelOpen()))
            gameTimer += Time.deltaTime;

        if (IsTriggerBlocked())
        {
            calmTimer = 0f;
            return;
        }
        calmTimer += Time.unscaledDeltaTime;

        if (gameTimer >= nextGameTime && calmTimer >= calmPeriodAfterInterruption)
        {
            // Tutorial: a player's first crisis comes after the Board's one-time memo about crises (it pauses; the crisis follows)
            if (FeatureGate.HoldForMemo(Feature.Crises))
            {
                calmTimer = 0f;
                return;
            }
            TriggerRandomMiniGame();
        }
    }

    /// <summary>Safety net while a minigame is up, so the game can never stay blocked or unpaused.</summary>
    void WatchActiveGame()
    {
        // The game object was destroyed or stopped without reporting: settle it as a loss
        if (activeGame == null || !activeGame.IsRunning)
        {
            Debug.LogWarning("[MiniGameManager] The active minigame ended without reporting - counting it as a loss.");
            if (activeGame != null && activeGame.gameObject.activeSelf)
                activeGame.gameObject.SetActive(false);
            OnMiniGameComplete(runId, false);
            return;
        }

        // UI state was reset, or a script un-paused the game directly (e.g. DebugTools' T key), while the
        // minigame is still on screen: register again so UIManager re-applies the pause
        if (UIManager.Exists && (!UIManager.Instance.IsPanelOpen(this) || Time.timeScale > 0f))
            UIManager.Instance.OpenPanel(this, true, GiveUpActiveGame);
    }

    /// <summary>
    /// A crisis waits while a menu/panel, risk-card event, build mode or text input is active.
    /// </summary>
    bool IsTriggerBlocked()
    {
        if (EventManager.Instance != null && EventManager.Instance.IsEventInProgress()) return true;

        var ui = UIManager.Instance;
        if (ui != null && ui.IsAnyPanelOpen()) return true;
        if (UIManager.IsTypingInInputField()) return true;

        if (BuildModeCamera.Instance != null && BuildModeCamera.Instance.IsInBuildMode()) return true;

        // Something paused the game without registering a panel
        if (Time.timeScale <= 0f) return true;

        return false;
    }

    void SetNextGameTime()
    {
        float min = Mathf.Max(1f, Mathf.Min(minTimeBetweenGames, maxTimeBetweenGames));
        float max = Mathf.Max(min, maxTimeBetweenGames);
        nextGameTime = UnityEngine.Random.Range(min, max);
        // Demo: the run's first crisis comes sooner (the timer only runs once crises are open)
        if (DemoMode.Active && !DemoRun.FirstCrisisDone)
            nextGameTime = UnityEngine.Random.Range(DemoMode.Config.firstCrisisMin, DemoMode.Config.firstCrisisMax);
        gameTimer = 0f;
    }

    // Demo: the run's first crisis is an easy one (QuickTap or ButtonMash) when one is in the rotation; -1 = none
    int DemoFirstCrisisIndex()
    {
        if (!DemoMode.Active || DemoRun.FirstCrisisDone) return -1;
        var easy = new List<int>();
        for (int i = 0; i < availableGames.Count; i++)
        {
            GameObject g = availableGames[i];
            MiniGameBase existing = g.GetComponent<MiniGameBase>();
            Type type = existing != null ? existing.GetType() : GetScriptType(g);
            if (type == typeof(QuickTapMiniGame) || type == typeof(ButtonMashMiniGame)) easy.Add(i);
        }
        return easy.Count > 0 ? easy[UnityEngine.Random.Range(0, easy.Count)] : -1;
    }

    public void TriggerRandomMiniGame()
    {
        if (miniGameActive) return;

        availableGames.RemoveAll(g => g == null);
        if (availableGames.Count == 0)
        {
            Debug.LogWarning("[MiniGameManager] No minigames available!");
            SetNextGameTime();
            return;
        }

        // Avoid the same game twice in a row when there is a choice
        int index = UnityEngine.Random.Range(0, availableGames.Count);
        if (availableGames.Count > 1 && availableGames[index] == lastPanel)
            index = (index + UnityEngine.Random.Range(1, availableGames.Count)) % availableGames.Count;
        int easyIndex = DemoFirstCrisisIndex();   // Demo
        if (easyIndex >= 0) index = easyIndex;

        TriggerMiniGame(availableGames[index]);
    }

    public void TriggerMiniGame(GameObject gamePanel)
    {
        if (gamePanel == null || miniGameActive) return;

        // Try to get or add the correct script based on panel name
        MiniGameBase baseGame = gamePanel.GetComponent<MiniGameBase>();
        if (baseGame == null)
        {
            baseGame = TryAddMiniGameScript(gamePanel);
        }

        if (baseGame == null)
        {
            // No script found or added: drop it from the rotation and wait for the next crisis
            Debug.LogWarning($"[MiniGameManager] No minigame script on {gamePanel.name} - removed from the rotation.");
            availableGames.Remove(gamePanel);
            gamePanel.SetActive(false);
            SetNextGameTime();
            return;
        }

        int run = ++runId;
        miniGameActive = true;
        activeGame = baseGame;
        lastPanel = gamePanel;

        try
        {
            gamePanel.SetActive(true);
            if (!gamePanel.activeInHierarchy)
                throw new InvalidOperationException("its canvas is hidden");

            // Draw above the HUD and hold the game paused; Escape = give up (a loss)
            UIManager.BringToFront(gamePanel);
            var ui = UIManager.Instance;
            if (ui != null)
                ui.OpenPanel(this, true, GiveUpActiveGame);

            // Calculate progressive difficulty
            CalculateDifficulty();

            // Random business problem, tracked by its index (the line is shown translated; the result line follows the index)
            int problem = GetRandomProblemIndex();

            // Apply difficulty and problem to the game (AI Assistant: easier and more time; the reward is unchanged)
            GetAIAssist(out float difficultyReduction, out float timeBonus);
            baseGame.SetDifficulty(1f + (currentDifficultyMultiplier - 1f) * (1f - difficultyReduction));
            baseGame.SetTimeLimitMultiplier(1f + timeBonus);
            baseGame.SetProblem(problem);
            baseGame.SetStakes(CalculateReward(true), CalculateReward(false));

            try { MiniGameStarted?.Invoke(baseGame.gameName); }
            catch (Exception e) { Debug.LogException(e, this); }
            Debug.Log($"[MiniGameManager] Started: {baseGame.gameName} (Difficulty: {currentDifficultyMultiplier:F2}x) - {EnglishProblem(problem)}");
            GameSfx.Play(Sfx.UiGlitch);   // "Crisis incoming"

            // Start the game (the callback fires exactly once, when the panel closes)
            baseGame.StartGame(success => OnMiniGameComplete(run, success), GetResultDetail);
            if (DemoMode.Active) DemoRun.FirstCrisisDone = true;   // Demo
        }
        catch (Exception e)
        {
            Debug.LogError($"[MiniGameManager] Couldn't start {gamePanel.name}: {e.Message}");
            AbortRun(run, gamePanel);
        }
    }

    /// <summary>Undo a run that failed to start (no reward or penalty).</summary>
    void AbortRun(int run, GameObject panel)
    {
        if (run != runId || !miniGameActive) return; // it already reported
        runId++;
        miniGameActive = false;
        activeGame = null;
        if (panel != null && panel.activeSelf) panel.SetActive(false);
        if (UIManager.Exists) UIManager.Instance.ClosePanel(this);
        SetNextGameTime();
    }

    /// <summary>Escape while a minigame is up: give up (counts as a loss).</summary>
    void GiveUpActiveGame()
    {
        if (!miniGameActive)
        {
            if (UIManager.Exists) UIManager.Instance.ClosePanel(this);
            return;
        }

        if (activeGame != null && activeGame.IsRunning)
            activeGame.GiveUp();
        else
            OnMiniGameComplete(runId, false); // the game vanished without reporting: settle it as a loss
    }

    /// <summary>AI Assistant skill: share of the extra difficulty removed, and extra time (0.25 = +25%).</summary>
    public static void GetAIAssist(out float difficultyReduction, out float timeBonus)
    {
        float assist = SkillTreeManager.Instance != null ? Mathf.Max(0f, SkillTreeManager.Instance.GetEffect(SkillEffect.AIAssistant)) : 0f;
        difficultyReduction = Mathf.Min(assist, MaxAIDifficultyReduction);
        timeBonus = Mathf.Min(assist * AITimeBonusPerEffect, MaxAITimeBonus);
    }

    void CalculateDifficulty()
    {
        // Progressive difficulty: starts at 1.0, increases slightly each game
        // After ~53 games (about 1 hour at avg 1 min between games), reaches max
        currentDifficultyMultiplier = 1f + (gamesPlayedThisSession * difficultyIncreasePerGame);
        currentDifficultyMultiplier = Mathf.Min(currentDifficultyMultiplier, maxDifficultyMultiplier);
    }

    int GetRandomProblemIndex()
    {
        return UnityEngine.Random.Range(0, businessProblems.Length);
    }

    MiniGameBase TryAddMiniGameScript(GameObject panel)
    {
        Type type = GetScriptType(panel);
        return type != null ? panel.AddComponent(type) as MiniGameBase : null;
    }

    /// <summary>Which minigame script drives a panel, by panel name.</summary>
    static Type GetScriptType(GameObject panel)
    {
        string name = panel.name.ToLowerInvariant();

        // Timing
        if (name.Contains("arrow")) return typeof(ArrowTimingMiniGame);
        if (name.Contains("stopthebar")) return typeof(StopTheBarMiniGame);
        // Reaction
        if (name.Contains("colormatch")) return typeof(ColorMatchMiniGame);
        if (name.Contains("whack") || name.Contains("mole")) return typeof(WhackAMoleMiniGame);
        if (name.Contains("rapidkey")) return typeof(RapidKeysMiniGame);
        // Memory
        if (name.Contains("simonsays")) return typeof(SimonSaysMiniGame);
        if (name.Contains("numbersequence")) return typeof(NumberSequenceMiniGame);
        if (name.Contains("sequencememory")) return typeof(SequenceMemoryMiniGame);
        if (name.Contains("patternmatch")) return typeof(PatternMatchMiniGame);
        // Puzzle
        if (name.Contains("keypad")) return typeof(KeypadCodeMiniGame);
        if (name.Contains("safecracker")) return typeof(SafeCrackerMiniGame);
        if (name.Contains("oddoneout")) return typeof(OddOneOutMiniGame);
        // Skill
        if (name.Contains("buttonmash")) return typeof(ButtonMashMiniGame);
        if (name.Contains("catchfalling")) return typeof(CatchFallingMiniGame);
        // Quick
        if (name.Contains("mathblitz")) return typeof(MathBlitzMiniGame);
        if (name.Contains("quicktap")) return typeof(QuickTapMiniGame);
        if (name.Contains("higherlower")) return typeof(HigherLowerMiniGame);
        if (name.Contains("wordtype")) return typeof(WordTypeMiniGame);
        if (name.Contains("coinflip")) return typeof(CoinFlipMiniGame);
        if (name.Contains("speedcount")) return typeof(SpeedCountMiniGame);

        return null;
    }

    /// <summary>Reward (positive) or penalty (negative) for the current run.</summary>
    float CalculateReward(bool success)
    {
        // Reward scales with difficulty (harder games = better rewards)
        float modifier = (1f + (difficultyLevel - 1) * 0.25f) * currentDifficultyMultiplier;
        return success ? RewardBase(true) * modifier : -RewardBase(false) * modifier;
    }

    /// <summary>
    /// The flat reward or penalty before the multipliers. Demo: at least a share of the company value, so crises still
    /// matter at a few hundred thousand.
    /// </summary>
    float RewardBase(bool success)
    {
        float flat = success ? successRewardBase : failurePenaltyBase;
        if (!DemoMode.Active) return flat;
        CompanyManager company = CompanyManager.Instance;
        if (company == null) return flat;
        double share = success ? DemoMode.Config.crisisRewardShare : DemoMode.Config.crisisPenaltyShare;
        return Mathf.Max(flat, (float)(share * System.Math.Max(0.0, company.companyValue)));
    }

    /// <summary>
    /// The current run's reward (positive) or penalty (negative). The Coin Flip skill's 50% double is decided once per
    /// run, so the result screen and the payout always agree.
    /// </summary>
    float GetRunReward(bool success, out bool coinFlipDoubled)
    {
        if (rewardRunId != runId || rewardSuccess != success)
        {
            rewardRunId = runId;
            rewardSuccess = success;
            rewardAmount = CalculateReward(success);
            rewardDoubled = success && rewardAmount > 0f && SkillTreeManager.Instance != null &&
                            SkillTreeManager.Instance.HasEffect(SkillEffect.CoinFlip) &&
                            UnityEngine.Random.value < CoinFlipDoubleChance;
            if (rewardDoubled)
                rewardAmount *= 2f;
        }
        coinFlipDoubled = rewardDoubled;
        return rewardAmount;
    }

    /// <summary>
    /// The current run's payout (the same amount the manager pays) and its sum for the CRT's result,
    /// e.g. "$2,000 × 1.2 · the bonus grows with every crisis".
    /// </summary>
    public void DescribeRunReward(bool success, out float amount, out string maths)
    {
        GameSfx.Play(success ? Sfx.UiConfirm : Sfx.UiError);   // the CRT asks for this once, as it shows the result
        amount = GetRunReward(success, out bool doubled);
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        float modifier = (1f + (difficultyLevel - 1) * 0.25f) * currentDifficultyMultiplier;
        // Money stays "$2,000" in every language; the multiplier takes the language's decimal mark (Crisis table texts)
        string sum = "$" + RewardBase(success).ToString("N0", inv);   // Demo: the same base as the payout
        if (Mathf.Abs(modifier - 1f) >= 0.005f) sum += " \u00D7 " + modifier.ToString("0.##", L10n.Culture);
        if (doubled) sum = CrisisText.Get("crisis.reward.doubled", ("sum", sum), ("skill", CoinFlipSkillName()));
        maths = CrisisText.Get(success ? "crisis.reward.note.solved" : "crisis.reward.note.failed", ("sum", sum));
    }

    /// <summary>The Coin Flip skill's name in the current language (Skill.DisplayName).</summary>
    static string CoinFlipSkillName()
    {
        Skill skill = SkillTreeManager.Instance != null ? SkillTreeManager.Instance.GetSkill(CoinFlipSkillId) : null;
        return skill != null ? skill.DisplayName : L10n.GetOr(Skill.Table, "skill." + CoinFlipSkillId + ".name", "Coin Flip");
    }

    /// <summary>Line shown under SUCCESS!/FAILED! on the result screen, e.g. "+$2,000".</summary>
    string GetResultDetail(bool success)
    {
        GameSfx.Play(success ? Sfx.UiConfirm : Sfx.UiError);   // old panels ask for this once, as they show the result
        float amount = GetRunReward(success, out bool doubled);
        string text = (amount >= 0f ? "+$" : "-$") + Mathf.Abs(amount).ToString("N0");
        return doubled ? CrisisText.Get("crisis.legacy.detail.doubled", ("amount", text), ("skill", L10n.ToUpper(CoinFlipSkillName()))) : text;
    }

    /// <summary>Report the active minigame's result (no effect when none is running).</summary>
    public void OnMiniGameComplete(bool success)
    {
        OnMiniGameComplete(runId, success);
    }

    void OnMiniGameComplete(int run, bool success)
    {
        if (this == null) return;                     // manager destroyed (scene unloading)
        if (run != runId || !miniGameActive) return;  // stale or duplicate report

        miniGameActive = false;
        activeGame = null;
        calmTimer = 0f;
        if (UIManager.Exists)
            UIManager.Instance.ClosePanel(this);

        if (quitting) return;

        gamesPlayedThisSession++;
        float reward = GetRunReward(success, out bool coinFlipDoubled);

        try { MiniGameCompleted?.Invoke(success, reward); }
        catch (Exception e) { Debug.LogException(e, this); }

        // Apply reward/penalty to company
        if (CompanyManager.Instance != null)
        {
            CompanyManager.Instance.AddMoney(reward);
        }
        if (coinFlipDoubled)
            GameNotifications.Show(CrisisText.Get("crisis.toast.coin_flip_doubled", ("skill", CoinFlipSkillName()),
                                                  ("amount", EventManager.FormatSignedMoney(reward))), NotificationKind.Good);

        SetNextGameTime();

        Debug.Log($"[MiniGame] Complete: {(success ? "WIN" : "LOSE")}, Reward: ${reward:N0}, Games Played: {gamesPlayedThisSession}, Next game in {nextGameTime:F0}s");
    }

    /// <summary>
    /// Get the number of available minigames
    /// </summary>
    public int GetAvailableGameCount()
    {
        return availableGames.Count;
    }

    /// <summary>
    /// Trigger a specific minigame by name (debug hook, e.g. "coinflip")
    /// </summary>
    public void TriggerGameByName(string gameName)
    {
        if (string.IsNullOrEmpty(gameName)) return;
        string wanted = gameName.ToLowerInvariant();
        foreach (var game in availableGames)
        {
            if (game != null && game.name.ToLowerInvariant().Contains(wanted))
            {
                TriggerMiniGame(game);
                return;
            }
        }
        Debug.LogWarning($"[MiniGameManager] Game not found: {gameName}");
    }

    /// <summary>
    /// Force trigger next minigame immediately (for testing)
    /// </summary>
    [ContextMenu("Trigger Random Minigame Now")]
    public void DebugTriggerNow()
    {
        TriggerRandomMiniGame();
    }
}
