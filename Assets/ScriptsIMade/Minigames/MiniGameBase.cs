using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Base class for all minigames. Inherit from this to create new minigames.
/// One play runs on real (unscaled) time, so it works while the game is paused:
///   1. "Incoming crisis" warning (the GameArea is hidden)
///   2. SetupGame() - the game resets its state and finds its UI
///   3. Get-ready phase - title, crisis line and controls are shown, input is ignored
///   4. Play - OnPlayStarted(), then GameUpdate() every frame until Win()/Lose()/time-out
///   5. Result (with the reward text supplied by the manager), then the panel hides
/// The completion callback fires exactly once on every path (win, loss, give-up, panel hidden, error).
/// Panels built by CrisisGamesBuilder carry a CrtScreenView (the office CRT): then the warning is the "Crisis incoming"
/// card with a countdown, the timer is the monitor's 30 segments and the result is shown on the glass; games report
/// their progress with ShowProgress / ShowStat / ShowLives.
/// Texts (l10n): the crisis line and its result line come from MiniGameManager by the problem's index (SetProblem), the
/// result's line and tag from the Crisis table (Win / Lose with CrisisText.Result("time_up")...), every other text from
/// CrisisText (Assets/Localization/Source/Crisis.csv).
/// </summary>
public abstract class MiniGameBase : MonoBehaviour
{
    [Header("Base UI References")]
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI problemText;      // Shows the business problem (the panel's direct child "ProblemText")
    public TextMeshProUGUI instructionText;
    public TextMeshProUGUI resultText;
    public TextMeshProUGUI timerText;
    public Image flashOverlay;

    [Header("Base Settings")]
    public string gameName = "Minigame";
    [Tooltip("Base time limit in seconds (0 = no limit). Difficulty shortens each play's limit; this value itself never changes.")]
    public float timeLimit = 30f;
    public bool showTimer = true;

    [Header("Flow (real-time seconds)")]
    public float warningDuration = 1.5f;
    public float instructionDuration = 2.5f;
    [Tooltip("Games without a time limit still end as a loss after this long")]
    public float maxPlayDuration = 60f;
    public float resultDuration = 2f;

    [Header("Visual Feedback")]
    public Color successFlashColor = new Color(0.2f, 1f, 0.2f, 0.5f);
    public Color failFlashColor = new Color(1f, 0.2f, 0.2f, 0.5f);
    public float flashDuration = 0.15f;

    [Header("Audio")]
    public AudioSource audioSource;
    public AudioClip successSound;
    public AudioClip failSound;
    public AudioClip winSound;
    public AudioClip loseSound;

    // Runtime state
    protected bool isPlaying;                   // true only during the play phase (input counts)
    protected float remainingTime;
    protected float difficultyMultiplier = 1f;  // Set by MiniGameManager
    protected float timeLimitMultiplier = 1f;   // Set by MiniGameManager (AI Assistant skill gives more time)
    protected string currentProblem = "";       // Business problem as given: MiniGameManager's English line, or a custom text
    protected int currentProblemIndex = -1;     // Index into MiniGameManager's problems (-1 = a custom line): picks the shown and result lines
    protected Action<bool> onGameComplete;

    // Flow state
    Func<bool, string> resultDetailProvider;
    bool gameRunning;        // StartGame called and the completion callback not fired yet
    bool resultDecided;
    bool resultSuccess;
    bool setupFailed;
    float currentTimeLimit;  // this play's limit (0 = none, the safety cap applies)
    float watchdogRemaining;
    int lastShownSeconds = -1;

    // UI defaults captured from the scene before the first play overwrites them
    bool defaultsCaptured;
    string defaultTitle = "";
    string defaultInstructions = "";
    bool timerSearched;

    const float WatchdogMargin = 15f;
    static Sprite runtimeWhiteSprite;

    // The office CRT (CrtScreenView on the panel), and what this crisis pays or costs
    protected CrtScreenView screen;
    float stakeReward;
    float stakePenalty;
    bool screenWired;
    bool startNowRequested;
    bool backRequested;

    /// <summary>True from StartGame until the completion callback has fired.</summary>
    public bool IsRunning => gameRunning;

    /// <summary>
    /// Set the difficulty multiplier (1.0 = normal, higher = harder)
    /// </summary>
    public void SetDifficulty(float multiplier)
    {
        difficultyMultiplier = Mathf.Clamp(multiplier, 1f, 2f); // Cap at 2x difficulty
    }

    /// <summary>
    /// Scale this play's time limit (1 = normal, 1.25 = 25% more time). Games without a limit are unaffected.
    /// </summary>
    public void SetTimeLimitMultiplier(float multiplier)
    {
        timeLimitMultiplier = Mathf.Clamp(multiplier, 0.5f, 3f);
    }

    /// <summary>
    /// Set the business problem to display by its English line; a line of MiniGameManager's list is shown (and gets its
    /// result line) in the current language, any other text as it is.
    /// </summary>
    public void SetProblem(string problem)
    {
        currentProblem = problem ?? "";
        currentProblemIndex = MiniGameManager.ProblemIndex(currentProblem);
    }

    /// <summary>Set the business problem by its index in MiniGameManager's list (shown in the current language).</summary>
    public void SetProblem(int problemIndex)
    {
        currentProblem = MiniGameManager.EnglishProblem(problemIndex);
        currentProblemIndex = currentProblem.Length > 0 ? problemIndex : -1;
    }

    /// <summary>What solving this crisis pays and failing it costs (negative), shown on the warning card and the monitor.</summary>
    public void SetStakes(float reward, float penalty)
    {
        stakeReward = reward;
        stakePenalty = penalty;
    }

    /// <summary>
    /// Start the minigame with a completion callback
    /// </summary>
    public void StartGame(Action<bool> callback)
    {
        StartGame(callback, null);
    }

    /// <summary>
    /// Start the minigame. <paramref name="resultDetail"/> (optional) returns an extra line for the
    /// result screen, e.g. the money won or lost.
    /// </summary>
    public void StartGame(Action<bool> callback, Func<bool, string> resultDetail)
    {
        // A previous run that never reported must still report (exactly once)
        if (gameRunning)
            FinishNow(false);

        StopAllCoroutines();
        onGameComplete = callback;
        resultDetailProvider = resultDetail;
        gameRunning = true;
        resultDecided = false;
        resultSuccess = false;
        setupFailed = false;
        isPlaying = false;

        if (!isActiveAndEnabled)
        {
            Debug.LogWarning($"[{gameName}] Can't start while the panel is inactive - counting it as a loss.");
            FinishNow(false);
            return;
        }

        ResetWatchdog(warningDuration + WatchdogMargin);
        StartCoroutine(GameLoop());
    }

    /// <summary>
    /// Main game loop - handles warning, setup, get-ready, gameplay and time-out
    /// </summary>
    IEnumerator GameLoop()
    {
        // 1) Incoming-crisis warning
        AutoFindBaseElements();
        CaptureDefaultTexts();
        ClearUISelection(); // so Space/Enter can't "click" a HUD button that was selected before
        ShowWarning();
        if (screen != null)
        {
            // The card counts down; Start now, Enter or Space starts at once
            startNowRequested = false;
            float total = Mathf.Max(0.5f, screen.countdownSeconds);
            float left = total;
            ResetWatchdog(total + WatchdogMargin);
            while (left > 0f && !startNowRequested)
            {
                screen.SetCountdown(left, total);
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space))
                    startNowRequested = true;
                yield return null;
                left -= Mathf.Min(Time.unscaledDeltaTime, 0.25f);
            }
        }
        else if (warningDuration > 0f)
            yield return new WaitForSecondsRealtime(warningDuration);

        // 2) Setup (an exception or FailSetup ends the game as a loss instead of leaving it stuck)
        if (screen == null) SetGameAreaVisible(true);   // the monitor shows it once the game is set up
        ResetWatchdog(WatchdogMargin);
        yield return StartCoroutine(RunGuarded(SetupGame()));
        PrepareFilledImages();

        currentTimeLimit = GetPlayTimeLimit() * timeLimitMultiplier;
        remainingTime = currentTimeLimit > 0f ? currentTimeLimit : Mathf.Max(5f, maxPlayDuration);
        if (screen != null)
            screen.ShowPlay(CrisisLine(), stakeReward, stakePenalty);
        else
            ApplyPlayTexts();
        lastShownSeconds = -1;
        UpdateTimerDisplay();

        if (setupFailed)
        {
            DecideResult(false, CrisisText.Result("failed"));
            yield break;
        }

        // 3) Get ready: instructions stay visible, input is ignored (the CRT's card already showed them)
        if (screen != null)
        {
            ResetWatchdog(screen.readySeconds + WatchdogMargin);
            if (screen.readySeconds > 0f)
                yield return new WaitForSecondsRealtime(screen.readySeconds);
        }
        else
        {
            ShowReadyBanner(true);
            ResetWatchdog(instructionDuration + WatchdogMargin);
            if (instructionDuration > 0f)
                yield return new WaitForSecondsRealtime(instructionDuration);
            ShowReadyBanner(false);
        }

        // 4) Play (use unscaled time so it works while the game is paused)
        ClearUISelection();
        isPlaying = true;
        ResetWatchdog(remainingTime + WatchdogMargin);

        bool failed = false;
        try { OnPlayStarted(); }
        catch (Exception e) { Debug.LogException(e, this); failed = true; }
        if (failed)
        {
            if (isPlaying) Lose();
            yield break;
        }

        while (isPlaying)
        {
            remainingTime -= Mathf.Min(Time.unscaledDeltaTime, 0.25f);
            UpdateTimerDisplay();

            if (remainingTime <= 0f)
            {
                Debug.Log($"[{gameName}] Time's up!");
                OnTimeUp();
                if (isPlaying) Lose(CrisisText.Result("time_up")); // an override that did nothing still ends the game
                yield break;
            }

            // Let the specific game handle its update
            try { GameUpdate(); }
            catch (Exception e) { Debug.LogException(e, this); failed = true; }
            if (failed)
            {
                if (isPlaying) Lose();
                yield break;
            }

            yield return null;
        }
    }

    /// <summary>Steps a coroutine and turns an exception into a failed setup.</summary>
    IEnumerator RunGuarded(IEnumerator routine)
    {
        if (routine == null) yield break;
        while (true)
        {
            bool hasNext;
            object current = null;
            try
            {
                hasNext = routine.MoveNext();
                if (hasNext) current = routine.Current;
            }
            catch (Exception e)
            {
                Debug.LogException(e, this);
                setupFailed = true;
                hasNext = false;
            }
            if (!hasNext) yield break;
            yield return current;
        }
    }

    /// <summary>
    /// Override to set up your game (reset state, find UI, etc.). Call base first.
    /// Call FailSetup() if something required is missing. Start sequences/timers in OnPlayStarted().
    /// </summary>
    protected virtual IEnumerator SetupGame()
    {
        // Auto-find common UI elements if not assigned
        AutoFindBaseElements();

        // Reset UI
        if (resultText != null)
            resultText.gameObject.SetActive(false);

        if (flashOverlay != null)
        {
            flashOverlay.color = new Color(0, 0, 0, 0);
            flashOverlay.raycastTarget = false;
            flashOverlay.gameObject.SetActive(true);
        }

        yield return null;
    }

    /// <summary>Called once when the play phase begins (after the get-ready phase). Start sequences here.</summary>
    protected virtual void OnPlayStarted() { }

    /// <summary>Called once when the game ends, just before the panel hides. Clean up here.</summary>
    protected virtual void OnGameEnded() { }

    /// <summary>
    /// Call from SetupGame when required elements are missing: the game ends as a loss instead of hanging.
    /// </summary>
    protected void FailSetup(string reason)
    {
        setupFailed = true;
        Debug.LogWarning($"[{gameName}] {reason} - ending this minigame as a loss.");
    }

    /// <summary>Controls line shown during the game. Override to mention extra controls.</summary>
    protected virtual string GetInstructionsText(string sceneInstructions)
    {
        return sceneInstructions;
    }

    /// <summary>This play's time limit (0 = none). Harder games get up to 30% less time.</summary>
    protected virtual float GetPlayTimeLimit()
    {
        if (timeLimit <= 0f) return 0f;
        float reduction = Mathf.Clamp01((difficultyMultiplier - 1f) * 0.3f);
        return Mathf.Max(timeLimit * (1f - reduction), Mathf.Min(timeLimit, 5f)); // Never less than 5 seconds
    }

    /// <summary>
    /// Auto-find common UI elements by name. They are direct children of the panel, so game
    /// elements with the same name inside GameArea (MathBlitz's equation "ProblemText",
    /// CoinFlip's feedback "ResultText") are never picked up.
    /// </summary>
    protected virtual void AutoFindBaseElements()
    {
        if (titleText == null) titleText = FindBaseElement<TextMeshProUGUI>("Title", true);
        if (problemText == null) problemText = FindBaseElement<TextMeshProUGUI>("ProblemText", false);
        if (instructionText == null) instructionText = FindBaseElement<TextMeshProUGUI>("Instructions", true);
        if (resultText == null) resultText = FindBaseElement<TextMeshProUGUI>("ResultText", false);
        if (timerText == null && !timerSearched)
        {
            timerSearched = true; // remember a miss instead of searching every frame
            timerText = FindBaseElement<TextMeshProUGUI>("Timer", true);
            if (timerText == null) timerText = FindBaseElement<TextMeshProUGUI>("TimerText", true);
        }
        if (flashOverlay == null) flashOverlay = FindBaseElement<Image>("FlashOverlay", true);
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
        if (screen == null) screen = GetComponent<CrtScreenView>();
        if (screen != null && !screenWired)
        {
            screenWired = true;
            screen.StartNowClicked += () => startNowRequested = true;
            screen.GiveUpClicked += GiveUp;
            screen.BackClicked += () => backRequested = true;
        }
    }

    /// <summary>The crisis line in the current language (a default when the manager gave none).</summary>
    string CrisisLine()
    {
        return string.IsNullOrEmpty(currentProblem) ? CrisisText.Get("crisis.problem.fallback") : ProblemLine();
    }

    /// <summary>The problem as shown: MiniGameManager's line in the current language, or the custom text as it is.</summary>
    string ProblemLine()
    {
        return currentProblemIndex >= 0 ? MiniGameManager.ProblemText(currentProblemIndex) : currentProblem;
    }

    /// <summary>Progress row on the CRT: done / needed (pips for 15 or fewer); current marks the pip in play.</summary>
    protected void ShowProgress(int done, int needed, int current = -1, bool blue = false)
    {
        if (screen != null) screen.SetProgress(done, needed, current, blue);
    }

    /// <summary>The value at the right of the CRT's progress row (its label is part of the panel).</summary>
    protected void ShowStat(string value, string label = null, bool yellow = false)
    {
        if (screen != null) screen.SetStat(value, label, yellow);
    }

    /// <summary>"1.0 s" / "×1.5" style values for the CRT's stat row (the unit and decimal mark of the current language).</summary>
    protected static string Seconds(float s) => CrisisText.Seconds(s);
    protected static string Times(float f) => "×" + f.ToString("0.0", L10n.Culture);

    /// <summary>Hearts at the right of the CRT's progress row.</summary>
    protected void ShowLives(int left, int max)
    {
        if (screen != null) screen.SetLives(left, max);
    }

    /// <summary>Direct child of the panel; optionally a nested search that skips GameArea.</summary>
    T FindBaseElement<T>(string elementName, bool searchNested) where T : Component
    {
        Transform direct = transform.Find(elementName);
        if (direct != null)
        {
            var comp = direct.GetComponent<T>();
            if (comp != null) return comp;
        }
        if (!searchNested) return null;

        foreach (Transform child in transform)
        {
            if (child.name == "GameArea") continue;
            var found = FindElementRecursive<T>(child, elementName);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>
    /// Find a UI element by name - searches GameArea first, then panel, then recursively
    /// </summary>
    protected T FindElement<T>(string elementName) where T : Component
    {
        // First try GameArea
        Transform gameArea = transform.Find("GameArea");
        if (gameArea != null)
        {
            var t = gameArea.Find(elementName);
            if (t != null)
            {
                var comp = t.GetComponent<T>();
                if (comp != null) return comp;
            }
        }

        // Then try direct child of panel
        var direct = transform.Find(elementName);
        if (direct != null)
        {
            var comp = direct.GetComponent<T>();
            if (comp != null) return comp;
        }

        // Finally search recursively
        return FindElementRecursive<T>(transform, elementName);
    }

    /// <summary>
    /// Recursively search for an element by name
    /// </summary>
    protected T FindElementRecursive<T>(Transform parent, string elementName) where T : Component
    {
        foreach (Transform child in parent)
        {
            if (child.name == elementName)
            {
                var comp = child.GetComponent<T>();
                if (comp != null) return comp;
            }

            var found = FindElementRecursive<T>(child, elementName);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>
    /// Get the game area transform (or panel root if no GameArea exists)
    /// </summary>
    protected Transform GetGameArea()
    {
        Transform gameArea = transform.Find("GameArea");
        return gameArea != null ? gameArea : transform;
    }

    /// <summary>
    /// Get difficulty-adjusted value (for child classes to use)
    /// Higher difficulty = higher returned value
    /// </summary>
    protected float GetDifficultyValue(float baseValue, float maxIncrease = 0.5f)
    {
        // At difficulty 1.0: returns baseValue
        // At difficulty 2.0: returns baseValue * (1 + maxIncrease)
        float increase = (difficultyMultiplier - 1f) * maxIncrease;
        return baseValue * (1f + increase);
    }

    /// <summary>
    /// Get difficulty-adjusted int value
    /// </summary>
    protected int GetDifficultyInt(int baseValue, float maxIncrease = 0.5f)
    {
        return Mathf.RoundToInt(GetDifficultyValue(baseValue, maxIncrease));
    }

    /// <summary>
    /// Override to handle per-frame game logic (only called during the play phase)
    /// </summary>
    protected abstract void GameUpdate();

    /// <summary>
    /// Call this when the player wins
    /// </summary>
    protected void Win()
    {
        Win(CrisisText.Result("success"));
    }

    /// <summary>Win with a line of its own for the old panels (the CRT's tag stays SOLVED).</summary>
    protected void Win(string message)
    {
        Win(new CrisisResult(message, CrisisText.Get("crisis.result.success.tag")));
    }

    protected void Win(CrisisResult result)
    {
        if (!isPlaying) return;
        DecideResult(true, result);
    }

    /// <summary>
    /// Call this when the player loses
    /// </summary>
    protected void Lose()
    {
        Lose(CrisisText.Result("failed"));
    }

    /// <summary>
    /// Lose with why, in the current language: CrisisText.Result("missed") gives the old panels' line and the CRT's tag
    /// (Crisis table "crisis.result.missed" / ".tag"). The string overload shows the text as given for both.
    /// </summary>
    protected void Lose(CrisisResult result)
    {
        if (!isPlaying) return;
        DecideResult(false, result);
    }

    protected void Lose(string message)
    {
        Lose(new CrisisResult(message, message));
    }

    /// <summary>
    /// Called when time runs out (default: lose)
    /// </summary>
    protected virtual void OnTimeUp()
    {
        Lose(CrisisText.Result("time_up"));
    }

    /// <summary>
    /// Give up (Escape). Counts as a loss. During the result screen it just closes the panel.
    /// </summary>
    public void GiveUp()
    {
        if (!gameRunning) return;
        if (resultDecided)
        {
            FinishNow(true);
            return;
        }
        StopAllCoroutines(); // stops the game loop and any running sequence
        isPlaying = false;
        DecideResult(false, CrisisText.Result("gave_up"));
    }

    void DecideResult(bool success, CrisisResult result)
    {
        if (!gameRunning || resultDecided) return;
        resultDecided = true;
        resultSuccess = success;
        isPlaying = false;

        if (!isActiveAndEnabled)
        {
            FinishNow(false);
            return;
        }

        ResetWatchdog(resultDuration + flashDuration + WatchdogMargin);
        StartCoroutine(ResultSequence(success, result));
    }

    IEnumerator ResultSequence(bool success, CrisisResult result)
    {
        Debug.Log($"[{gameName}] {(success ? "WIN" : "LOSE")}: {result.Message}");
        PlaySound(success ? winSound : loseSound);

        if (screen != null)
        {
            if (flashOverlay != null)
                yield return StartCoroutine(FlashEffect(success ? successFlashColor : failFlashColor));
            float amount = success ? stakeReward : stakePenalty;
            string maths = "";
            if (MiniGameManager.Instance != null)
                MiniGameManager.Instance.DescribeRunReward(success, out amount, out maths);
            // The result line follows the problem's index, not its shown (translated) text; the tag is its own entry in final case
            screen.ShowResult(success, MiniGameManager.OutcomeLine(currentProblemIndex, success), amount, maths, result.Tag);
            backRequested = false;
            float left = Mathf.Max(0.5f, screen.resultSeconds);
            ResetWatchdog(left + WatchdogMargin);
            while (left > 0f && !backRequested)
            {
                screen.SetClosesIn(left);
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                    backRequested = true;
                yield return null;
                left -= Mathf.Min(Time.unscaledDeltaTime, 0.25f);
            }
            FinishNow(true);
            yield break;
        }

        string detail = null;
        if (resultDetailProvider != null)
        {
            try { detail = resultDetailProvider(success); }
            catch (Exception e) { Debug.LogException(e, this); }
        }

        // Skip flash if overlay is null
        if (flashOverlay != null)
            yield return StartCoroutine(FlashEffect(success ? successFlashColor : failFlashColor));

        if (resultText != null)
        {
            resultText.text = string.IsNullOrEmpty(detail) ? result.Message : result.Message + "\n<size=60%>" + detail + "</size>";
            resultText.color = success ? Color.green : Color.red;
            resultText.gameObject.SetActive(true);
        }

        yield return new WaitForSecondsRealtime(resultDuration);
        FinishNow(true);
    }

    /// <summary>Ends the game and fires the completion callback once.</summary>
    void FinishNow(bool hidePanel)
    {
        if (!gameRunning) return;
        gameRunning = false;
        isPlaying = false;
        bool success = resultDecided && resultSuccess;
        resultDecided = true;

        Action<bool> callback = onGameComplete;
        onGameComplete = null;
        resultDetailProvider = null;

        try { OnGameEnded(); }
        catch (Exception e) { Debug.LogException(e, this); }

        // Don't leave a (soon hidden) button or input field selected
        var es = EventSystem.current;
        if (es != null && !es.alreadySelecting && es.currentSelectedGameObject != null &&
            es.currentSelectedGameObject.transform.IsChildOf(transform))
            es.SetSelectedGameObject(null);

        if (hidePanel)
        {
            StopAllCoroutines();
            if (gameObject.activeSelf)
                gameObject.SetActive(false); // OnDisable sees gameRunning == false and does nothing
        }

        if (callback != null)
        {
            try { callback(success); }
            catch (Exception e) { Debug.LogException(e, this); }
        }
    }

    // ------------------------------------------------------------------
    // Phase visuals
    // ------------------------------------------------------------------

    void CaptureDefaultTexts()
    {
        if (defaultsCaptured) return;
        defaultsCaptured = true;
        defaultTitle = titleText != null && !string.IsNullOrEmpty(titleText.text) ? titleText.text : "";   // "": PlayTitle uses the game's name
        defaultInstructions = instructionText != null ? instructionText.text : "";
        if (screen != null) return;   // the CRT's texts are laid out by its builder

        // Room for a long crisis line on one row (the panels are full screen)
        if (instructionText != null) EnsureMinWidth(instructionText.rectTransform, 900f);
        if (problemText != null) EnsureMinWidth(problemText.rectTransform, 1000f);
    }

    void ShowWarning()
    {
        if (screen != null)
        {
            screen.ShowWarning(CrisisLine(), stakeReward, stakePenalty);
            return;
        }
        SetGameAreaVisible(false);
        if (resultText != null) resultText.gameObject.SetActive(false);
        if (timerText != null) timerText.gameObject.SetActive(false);
        if (flashOverlay != null)
        {
            flashOverlay.raycastTarget = false;
            flashOverlay.gameObject.SetActive(true);
            StartCoroutine(FlashEffect(failFlashColor));
        }

        string crisis = CrisisLine();
        if (titleText != null)
        {
            titleText.text = "<color=#FF5A4A>" + CrisisText.Get("crisis.legacy.incoming") + "</color>";
            titleText.gameObject.SetActive(true);
        }
        if (problemText != null)
        {
            problemText.text = crisis;
            problemText.gameObject.SetActive(true);
            if (instructionText != null) instructionText.gameObject.SetActive(false);
        }
        else if (instructionText != null)
        {
            instructionText.text = crisis;
            instructionText.gameObject.SetActive(true);
        }
    }

    void ApplyPlayTexts()
    {
        if (titleText != null)
        {
            titleText.text = PlayTitle();
            titleText.gameObject.SetActive(true);
        }

        bool hasProblem = !string.IsNullOrEmpty(currentProblem);
        string problemLine = hasProblem ? ProblemLine() : "";
        if (problemText != null)
        {
            problemText.text = problemLine;
            problemText.gameObject.SetActive(hasProblem);
        }

        // The controls stay visible for the whole game. Panels without a ProblemText show the crisis line here.
        if (instructionText != null)
        {
            string text = GetInstructionsText(defaultInstructions);
            if (problemText == null && hasProblem)
            {
                string crisisLine = "<size=85%><i><color=#FFD84D>" + problemLine + "</color></i></size>";
                text = string.IsNullOrEmpty(text) ? crisisLine : crisisLine + "\n" + text;
            }
            instructionText.text = text;
            instructionText.gameObject.SetActive(!string.IsNullOrEmpty(text));
        }

        if (timerText != null)
            timerText.gameObject.SetActive(showTimer && currentTimeLimit > 0f);
    }

    /// <summary>Old panels' title during play: the scene's own title, else the game's name in capitals (current language).</summary>
    string PlayTitle()
    {
        return !string.IsNullOrEmpty(defaultTitle) ? defaultTitle : L10n.ToUpper(CrisisText.GameName(gameName));
    }

    void ShowReadyBanner(bool show)
    {
        if (resultText == null) return;
        if (show)
        {
            resultText.text = "<size=70%>" + CrisisText.Get("crisis.legacy.get_ready") + "</size>";
            resultText.color = new Color(1f, 0.85f, 0.3f);
            resultText.gameObject.SetActive(true);
        }
        else if (!resultDecided)
        {
            resultText.gameObject.SetActive(false);
        }
    }

    void SetGameAreaVisible(bool visible)
    {
        Transform area = transform.Find("GameArea");
        if (area != null && area.gameObject.activeSelf != visible)
            area.gameObject.SetActive(visible);
    }

    void UpdateTimerDisplay()
    {
        if (screen != null)
        {
            // 30 segments of the limit (games without one show their safety cap)
            if (!resultDecided)
                screen.SetTime(remainingTime, currentTimeLimit > 0f ? currentTimeLimit : Mathf.Max(5f, maxPlayDuration));
            return;
        }
        if (!showTimer) return;

        // Games without a limit only show the safety countdown in its last 10 seconds
        bool visible = currentTimeLimit > 0f || remainingTime <= 10f;
        int displayTime = Mathf.CeilToInt(Mathf.Max(0f, remainingTime));

        if (timerText != null)
        {
            if (timerText.gameObject.activeSelf != visible)
                timerText.gameObject.SetActive(visible);
            if (!visible || displayTime == lastShownSeconds) return;
            lastShownSeconds = displayTime;
            timerText.text = displayTime.ToString();

            // Red when low on time
            timerText.color = remainingTime <= 5f ? Color.red : Color.white;
        }
        else if (titleText != null && visible && !resultDecided)
        {
            // No Timer element on this panel: show the countdown next to the title
            if (displayTime == lastShownSeconds) return;
            lastShownSeconds = displayTime;
            string color = remainingTime <= 5f ? "#FF5555" : "#FFFFFF";
            titleText.text = $"{PlayTitle()}  <color={color}>{displayTime}</color>";
        }
    }

    /// <summary>
    /// Filled Images without a sprite draw as full rectangles and ignore fillAmount.
    /// Give them a plain white sprite so radial/horizontal fills work.
    /// </summary>
    void PrepareFilledImages()
    {
        foreach (var img in GetComponentsInChildren<Image>(true))
        {
            if (img != null && img.type == Image.Type.Filled && img.sprite == null)
                img.sprite = GetRuntimeWhiteSprite();
        }
    }

    protected static Sprite GetRuntimeWhiteSprite()
    {
        if (runtimeWhiteSprite == null)
        {
            Texture2D tex = Texture2D.whiteTexture;
            runtimeWhiteSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            runtimeWhiteSprite.name = "MiniGameWhite (runtime)";
        }
        return runtimeWhiteSprite;
    }

    static void EnsureMinWidth(RectTransform rt, float minWidth)
    {
        if (rt == null) return;
        if (!Mathf.Approximately(rt.anchorMin.x, rt.anchorMax.x)) return; // stretched: width follows the parent
        if (rt.sizeDelta.x < minWidth)
            rt.sizeDelta = new Vector2(minWidth, rt.sizeDelta.y);
    }

    static void ClearUISelection()
    {
        var es = EventSystem.current;
        if (es != null && !es.alreadySelecting && es.currentSelectedGameObject != null)
            es.SetSelectedGameObject(null);
    }

    /// <summary>
    /// Focus a TMP input field on the next frame (it drops focus right after onSubmit).
    /// </summary>
    protected void FocusInputField(TMP_InputField field)
    {
        if (field == null || !isActiveAndEnabled) return;
        StartCoroutine(FocusNextFrame(field));
    }

    IEnumerator FocusNextFrame(TMP_InputField field)
    {
        yield return null;
        if (field == null || !isPlaying || !field.gameObject.activeInHierarchy) yield break;
        field.Select();
        field.ActivateInputField();
    }

    /// <summary>
    /// Play a flash effect on the overlay
    /// </summary>
    protected IEnumerator FlashEffect(Color color)
    {
        if (flashOverlay == null) yield break;

        flashOverlay.color = color;

        float elapsed = 0f;
        while (elapsed < flashDuration)
        {
            elapsed += Time.unscaledDeltaTime; // Use real time so it works when paused
            float alpha = Mathf.Lerp(color.a, 0f, elapsed / flashDuration);
            flashOverlay.color = new Color(color.r, color.g, color.b, alpha);
            yield return null;
        }

        flashOverlay.color = new Color(0, 0, 0, 0);
    }

    /// <summary>
    /// Play a sound effect (at the player's sound effect volume)
    /// </summary>
    protected void PlaySound(AudioClip clip)
    {
        if (audioSource != null && clip != null)
        {
            audioSource.PlayOneShot(clip, GameSettings.SfxVolume);
        }
    }

    /// <summary>
    /// Force stop the game immediately (counts as a loss unless the result was already decided)
    /// </summary>
    public void ForceStop()
    {
        if (gameRunning)
        {
            FinishNow(true);
            return;
        }
        StopAllCoroutines();
        isPlaying = false;
        gameObject.SetActive(false);
    }

    void ResetWatchdog(float seconds)
    {
        watchdogRemaining = seconds;
    }

    /// <summary>
    /// Safety net: if a phase takes far longer than it can (e.g. a coroutine died from an
    /// exception), end the game so the manager never stays stuck.
    /// </summary>
    protected virtual void Update()
    {
        if (!gameRunning) return;
        watchdogRemaining -= Mathf.Min(Time.unscaledDeltaTime, 0.25f);
        if (watchdogRemaining <= 0f)
        {
            Debug.LogWarning($"[{gameName}] Minigame stopped responding - ending it.");
            FinishNow(true);
        }
    }

    protected virtual void OnDisable()
    {
        isPlaying = false;
        if (gameRunning)
        {
            // Hidden from outside (or the scene is unloading) before the game finished: still report once
            Debug.LogWarning($"[{gameName}] Panel was hidden before the game finished.");
            FinishNow(false);
        }
    }
}
