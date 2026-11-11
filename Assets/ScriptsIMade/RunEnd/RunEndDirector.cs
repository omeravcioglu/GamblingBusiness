using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The end of a run: bankruptcy with a lifeline, game over and the $10B win.
/// - Bankruptcy: company value at or below bankruptcyThreshold ($0.50) while the screen is calm (IsScreenCalm: no UIManager
///   panel, crisis, risk card, text input or bet waiting on a flip/hand, and the save is loaded) for calmSeconds (1.5 s,
///   real time) opens the hearing (BankruptcyHearingView): take a bank loan (BankLoan; only without a loan), one last coin
///   flip (heads: debt forgiven and the company restarts at max($50,000, 10% of peak); tails: game over) or close the
///   company (game over).
/// - Game over deletes the save, forgets the company name (so the menu has nothing to Continue) and stops all saving
///   (autosave, save on quit, SceneFlow's save) until the next scene loads, then shows GameOverView. Both of its buttons
///   go to the main menu, whose New Game starts the next company.
/// - Win: the first time in a run the value reaches winValue ($10B) on a calm screen, WinView shows (once per run,
///   remembered in RunStats). Keep playing closes it; Start a new company deletes the save and goes to the main menu;
///   Main menu keeps the save.
/// - RunEndBootstrap adds this (with RunStats and BankLoan) to every scene that has a CompanyManager, so MainGame needs no
///   edit. The views are found in the scene or created here; each builds its own screen at runtime unless its Root is set.
/// </summary>
[DisallowMultipleComponent]
public class RunEndDirector : MonoBehaviour
{
    public static RunEndDirector Instance { get; private set; }

    /// <summary>The bankruptcy hearing opened.</summary>
    public static event Action HearingOpened;
    /// <summary>The run is over: the save is deleted and the Game Over screen is coming up.</summary>
    public static event Action RunEnded;
    /// <summary>The $10B win screen opened.</summary>
    public static event Action WinReached;

    const string CompanyNameKey = "CompanyName";
    const string HasSetCompanyNameKey = "HasSetCompanyName";

    [Header("Bankruptcy")]
    [Tooltip("The company is bankrupt at or below this value")]
    public double bankruptcyThreshold = 0.5;
    [Tooltip("Real seconds the screen must stay calm (no panel, crisis, risk card, text input or unresolved bet) before the hearing or the win screen opens")]
    public float calmSeconds = 1.5f;

    [Header("One last coin flip")]
    [Tooltip("Chance that the coin shows heads")]
    [Range(0f, 1f)] public float headsChance = 0.5f;
    [Tooltip("Heads restarts the company at the larger of this and restartPeakShare x the run's peak value")]
    public double restartMinimum = 50000;
    [Tooltip("Share of the run's peak value the company restarts at on heads (when that is more than restartMinimum)")]
    public double restartPeakShare = 0.10;
    [Tooltip("The restart value is rounded to a multiple of this")]
    public double restartRoundTo = 1000;

    [Header("Win")]
    [Tooltip("Company value that wins the run (the win screen shows once per run)")]
    public double winValue = 10000000000d;

    [Header("Views (empty = found in the scene, else created and built at runtime)")]
    public BankruptcyHearingView hearingView;
    public GameOverView gameOverView;
    public WinView winView;
    public DebtChipView debtChipView;

    float calmTimer;
    bool runOver;
    bool winShownThisRun;
    bool outOfCashHintShown;
    RunSummary endSummary;
    static bool savingSuspendedByRunEnd;

    /// <summary>The run ended (game over): nothing triggers any more and nothing is saved until the next scene.</summary>
    public bool IsRunOver => runOver;
    public bool IsHearingOpen => hearingView != null && hearingView.IsOpen;
    /// <summary>The hearing, the Game Over or the Win screen is up.</summary>
    public bool IsAnyScreenOpen => IsHearingOpen || (gameOverView != null && gameOverView.IsOpen) || (winView != null && winView.IsOpen);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Instance = null;
        HearingOpened = null;
        RunEnded = null;
        WinReached = null;
        savingSuspendedByRunEnd = false;
        SceneManager.sceneLoaded -= ResumeSavingOnSceneLoad;
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[RunEndDirector] Removed a second RunEndDirector on '" + name + "'.");
            Destroy(this);
            return;
        }
        Instance = this;
        if (DemoMode.Active) restartMinimum = DemoMode.StartingValue;   // Demo: heads restarts at the demo's start

        // The save participants must exist before SaveSystem restores the scene (a couple of frames after it loads)
        if (RunStats.Instance == null && FindAnyObjectByType<RunStats>(FindObjectsInactive.Include) == null)
            gameObject.AddComponent<RunStats>();
        if (BankLoan.Instance == null && FindAnyObjectByType<BankLoan>(FindObjectsInactive.Include) == null)
            gameObject.AddComponent<BankLoan>();
    }

    void Start()
    {
        // The debt chip is always there (it shows a loan from the save); the other screens are made when first needed
        debtChipView = FindOrCreate(debtChipView, "DebtChipView");
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        if (runOver) return;
        CompanyManager company = CompanyManager.Instance;
        if (company == null || IsAnyScreenOpen)
        {
            calmTimer = 0f;
            return;
        }

        double value = company.companyValue;
        // Out of cash isn't broke while stocks or the City bank still hold money the player can raise
        bool outOfCash = value <= bankruptcyThreshold;
        bool broke = outOfCash && value + SellableAssets() <= bankruptcyThreshold;
        if (outOfCash && !broke && !outOfCashHintShown && IsScreenCalm())
        {
            outOfCashHintShown = true;
            GameNotifications.Show(L10n.Get(RunEndUI.Table, "runend.toast.out_of_cash"), NotificationKind.Bad);
        }
        else if (!outOfCash)
        {
            outOfCashHintShown = false;
        }
        bool won = !broke && value >= winValue && !winShownThisRun && (RunStats.Instance == null || !RunStats.Instance.WinShown);
        if ((!broke && !won) || !IsScreenCalm())
        {
            calmTimer = 0f;
            return;
        }

        calmTimer += Time.unscaledDeltaTime;
        if (calmTimer < calmSeconds) return;
        calmTimer = 0f;
        if (broke) OpenHearing();
        else ShowWin();
    }

    /// <summary>
    /// Nothing else is going on: no UIManager panel, crisis, risk card or text input, no bet waiting on a coin flip or a
    /// blackjack hand, the game isn't paused some other way and the saved game has been loaded.
    /// </summary>
    public static bool IsScreenCalm()
    {
        if (UIManager.IsTypingInInputField()) return false;
        if (UIManager.Exists && UIManager.Instance.IsAnyPanelOpen()) return false;
        if (MiniGameManager.Instance != null && MiniGameManager.Instance.IsMiniGameActive()) return false;
        if (EventManager.Instance != null && EventManager.Instance.IsEventInProgress()) return false;

        BuildGambleView gamble = BuildGambleView.Instance;
        if (gamble != null && (gamble.IsBusy || gamble.PendingBet > 0.0)) return false;
        if (OfficeUpgradeManager.Instance != null && OfficeUpgradeManager.Instance.IsBusy) return false;
        if (BlackjackGame.Instance != null && BlackjackGame.Instance.IsOpen) return false;

        // Money on its way to or from the City bank (it may still come back)
        if (CityOnline.Instance != null && CityOnline.Instance.HasPendingTransfers) return false;

        if (Time.timeScale <= 0f) return false;
        SaveSystem save = SaveSystem.Instance;
        if (save != null && (save.IsLoading || !save.IsGameLoaded)) return false;
        return true;
    }

    /// <summary>Money the company could still raise without a loan: its stocks (after the sell fee) and its city bank balances.</summary>
    static double SellableAssets()
    {
        double total = 0.0;
        try
        {
            StockMarketManager market = StockMarketManager.Instance;
            if (market != null && market.stocks != null)
            {
                foreach (Stock stock in market.stocks)
                {
                    if (stock != null && stock.ownedShares > 0)
                        total += market.GetSellProceeds(stock, stock.ownedShares);
                }
            }

            CityOnline city = CityOnline.Instance;
            if (city != null)
                total += city.TotalBankBalance;
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
        return double.IsNaN(total) || total < 0.0 ? 0.0 : total;
    }

    T FindOrCreate<T>(T current, string objectName) where T : MonoBehaviour
    {
        if (current != null) return current;
        T found = FindAnyObjectByType<T>(FindObjectsInactive.Include);
        if (found != null) return found;
        GameObject go = new GameObject(objectName);
        go.transform.SetParent(transform, false);
        return go.AddComponent<T>();
    }

    BankruptcyHearingView Hearing => hearingView = FindOrCreate(hearingView, "BankruptcyHearingView");
    GameOverView GameOverScreen => gameOverView = FindOrCreate(gameOverView, "GameOverView");
    WinView WinScreen => winView = FindOrCreate(winView, "WinView");

    // ------------------------------------------------------------------
    // Bankruptcy hearing
    // ------------------------------------------------------------------

    /// <summary>Company value the last coin flip restarts at: max(restartMinimum, restartPeakShare x peak), rounded.</summary>
    public double RestartValue()
    {
        double peak = RunStats.Instance != null ? RunStats.Instance.PeakValue : 0.0;
        return BankLoan.RoundAmount(Math.Max(restartMinimum, peak * restartPeakShare), restartRoundTo);
    }

    /// <summary>Open the hearing now (normally Update does it once the company is broke and the screen is calm).</summary>
    public void OpenHearing()
    {
        if (runOver || IsHearingOpen) return;
        CompanyManager company = CompanyManager.Instance;
        BankLoan loan = BankLoan.Instance;
        var info = new HearingInfo
        {
            companyName = CompanyNameUI.GetCompanyName(),
            day = company != null ? Mathf.Max(1, company.currentDay) : 1,
            companyValue = company != null ? Math.Max(0.0, company.companyValue) : 0.0,
            loanAvailable = loan != null && !loan.HasLoan,
            loanAmount = loan != null ? loan.QuoteLoan() : 0.0,
            debt = loan != null ? loan.Debt : 0.0,
            dailyInterest = loan != null ? loan.dailyInterest : 0.0,
            autoRepayShare = loan != null ? loan.autoRepayShare : 0.0,
            restartValue = RestartValue(),
            headsChance = headsChance
        };
        Hearing.Open(info);
        GameSfx.Play(Sfx.UiGlitch);   // out of money: the court wants an answer
        Raise(HearingOpened);
    }

    /// <summary>Hearing option a: borrow from the bank (only without a loan) and carry on.</summary>
    public void TakeLoan()
    {
        BankLoan loan = BankLoan.Instance;
        if (runOver || loan == null) return;
        if (!loan.TakeLoan())
        {
            GameNotifications.Show(L10n.Get(RunEndUI.Table, "runend.hearing.loan.none"), NotificationKind.Bad);   // "The bank won't lend again until..."
            return;
        }
        if (hearingView != null) hearingView.Close();
        GameSfx.Play(Sfx.ChipsCollect);   // the loan comes in
        GameNotifications.Show(LoanToast(loan.Borrowed, loan.dailyInterest, loan.autoRepayShare), NotificationKind.Info);
    }

    /// <summary>
    /// "The bank lent you $60,000. The debt grows 3% a day; half of each day's profit pays it back." in the current language:
    /// one entry per share the hearing's loan card also names (half, a quarter, all), a percentage otherwise.
    /// </summary>
    static string LoanToast(double borrowed, double dailyInterest, double repayShare)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string amount = UIFormat.Money(borrowed);
        string interest = (dailyInterest * 100.0).ToString("0.#", inv);
        if (Math.Abs(repayShare - 0.5) < 0.0001)
            return L10n.Get(RunEndUI.Table, "runend.loan.toast.lent.half", ("amount", amount), ("interest", interest));
        if (Math.Abs(repayShare - 0.25) < 0.0001)
            return L10n.Get(RunEndUI.Table, "runend.loan.toast.lent.quarter", ("amount", amount), ("interest", interest));
        if (repayShare >= 0.9999)
            return L10n.Get(RunEndUI.Table, "runend.loan.toast.lent.all", ("amount", amount), ("interest", interest));
        return L10n.Get(RunEndUI.Table, "runend.loan.toast.lent", ("amount", amount), ("interest", interest),
                        ("share", (repayShare * 100.0).ToString("0.#", inv)));
    }

    /// <summary>Hearing option b, step 1: toss the coin (true = heads). Nothing changes until ApplyCoinFlip.</summary>
    public bool RollCoinFlip()
    {
        return UnityEngine.Random.value < headsChance;
    }

    /// <summary>
    /// Hearing option b, step 2: heads (applied when the coin shows its face) forgives the debt and restarts the company
    /// at RestartValue(); tails (applied as soon as the coin is tossed) ends the run: the save is deleted right away, so a
    /// quit while the coin spins can't undo it.
    /// </summary>
    public void ApplyCoinFlip(bool heads)
    {
        if (runOver) return;
        if (!heads)
        {
            CommitGameOver();
            return;
        }

        double restart = RestartValue();
        if (RunStats.Instance != null) RunStats.Instance.CountRestart();   // the cloud save server explains the restart money by it
        if (BankLoan.Instance != null) BankLoan.Instance.Forgive();
        CompanyManager company = CompanyManager.Instance;
        if (company != null) company.AddMoney(restart - company.companyValue, true);
    }

    /// <summary>Close the hearing after a heads result.</summary>
    public void CloseHearing()
    {
        if (hearingView != null) hearingView.Close();
    }

    /// <summary>Hearing option c: close the company (game over).</summary>
    public void CloseCompany()
    {
        GameOver();
    }

    // ------------------------------------------------------------------
    // Game over
    // ------------------------------------------------------------------

    /// <summary>End the run now: delete the save, stop saving until the next scene, show the Game Over screen.</summary>
    public void GameOver()
    {
        CommitGameOver();
        ShowGameOverView();
    }

    /// <summary>Show the Game Over screen for a run that already ended (after the tails result).</summary>
    public void ShowGameOverView()
    {
        if (!runOver) CommitGameOver();
        if (endSummary == null) endSummary = Summarize();
        GameNotifications.ClearAll();
        GameOverScreen.Show(endSummary);   // opens (and pauses) before the others close, so the game never runs in between
        GameSfx.PlayPair(Sfx.UiError, Sfx.UiScratch, 0.4f);   // then the BANKRUPT stamp
        if (hearingView != null && hearingView.IsOpen) hearingView.Close();
        if (winView != null && winView.IsOpen) winView.Close();
    }

    void CommitGameOver()
    {
        if (runOver) return;
        runOver = true;
        calmTimer = 0f;
        endSummary = Summarize();   // before the company name is forgotten
        EndSavedGame();
        Raise(RunEnded);
    }

    RunSummary Summarize()
    {
        if (RunStats.Instance != null) return RunStats.Instance.Summarize();
        CompanyManager company = CompanyManager.Instance;
        return new RunSummary
        {
            companyName = CompanyNameUI.GetCompanyName(),
            day = company != null ? Mathf.Max(1, company.currentDay) : 1,
            companyValue = company != null ? Math.Max(0.0, company.companyValue) : 0.0,
            peakValue = company != null ? Math.Max(0.0, company.companyValue) : 0.0
        };
    }

    /// <summary>
    /// Delete the save, forget the company name (the main menu then offers no Continue) and stop every save (autosave,
    /// save on quit, SceneFlow.Load's save) until the next scene has loaded.
    /// </summary>
    static void EndSavedGame()
    {
        // Cloud save: this run is over (the server is told now, or at the next sync); before the save is deleted
        CloudSaveSync.EndRun();

        SaveSystem save = SaveSystem.Instance;
        if (save != null)
        {
            save.DeleteSave();
            if (!save.SavingSuspended)
            {
                save.SuspendSaving();
                savingSuspendedByRunEnd = true;
            }
        }
        else
        {
            SaveSystem.DeleteLocalSaveFiles();
        }

        PlayerPrefs.DeleteKey(CompanyNameKey);
        PlayerPrefs.DeleteKey(HasSetCompanyNameKey);
        PlayerPrefs.Save();

        SceneManager.sceneLoaded -= ResumeSavingOnSceneLoad;
        SceneManager.sceneLoaded += ResumeSavingOnSceneLoad;
    }

    static void ResumeSavingOnSceneLoad(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single) return;
        SceneManager.sceneLoaded -= ResumeSavingOnSceneLoad;
        if (savingSuspendedByRunEnd && SaveSystem.Instance != null && SaveSystem.Instance.SavingSuspended)
            SaveSystem.Instance.ResumeSaving();
        savingSuspendedByRunEnd = false;
    }

    // ------------------------------------------------------------------
    // Win
    // ------------------------------------------------------------------

    /// <summary>Show the $10B win screen (normally Update does it, once per run).</summary>
    public void ShowWin()
    {
        if (runOver) return;
        winShownThisRun = true;
        if (RunStats.Instance != null) RunStats.Instance.MarkWinShown();
        if (SaveSystem.Instance != null) SaveSystem.Instance.SaveGame();   // remember right away that it was shown
        WinScreen.Show(Summarize(), winValue);
        GameSfx.PlayPair(Sfx.ChipsWin, Sfx.UiGlass, 0.3f);   // $10B: chips and a sparkle as the confetti falls
        Raise(WinReached);
    }

    /// <summary>Win screen: close it and carry on (it never shows again this run).</summary>
    public void KeepPlaying()
    {
        if (winView != null) winView.Close();
    }

    /// <summary>
    /// Start over: end this company (delete the save, forget its name, stop saving until the next scene) and go to the
    /// main menu, whose New Game starts the next one. Does nothing (with an error) when the menu scene can't be loaded.
    /// </summary>
    public void StartNewCompany()
    {
        if (!SceneFlow.CanLoad(SceneFlow.MainMenuScene))
        {
            Debug.LogError("[RunEndDirector] '" + SceneFlow.MainMenuScene + "' is not in Build Settings, so the game can't go back to the menu.");
            return;
        }
        if (!runOver)
        {
            runOver = true;
            EndSavedGame();
        }
        SceneFlow.Load(SceneFlow.MainMenuScene);
    }

    /// <summary>Go to the main menu. SceneFlow saves first, unless the run is over (saving is suspended then).</summary>
    public void GoToMainMenu()
    {
        SceneFlow.Load(SceneFlow.MainMenuScene);
    }

    // ------------------------------------------------------------------
    // Debug (Tools > Game > Debug)
    // ------------------------------------------------------------------

    /// <summary>Debug: drop the company to $0 and open the hearing right away.</summary>
    public void DebugForceBankruptcy()
    {
        if (runOver) { Debug.LogWarning("[RunEndDirector] The run is already over."); return; }
        CompanyManager company = CompanyManager.Instance;
        if (company != null && company.companyValue > 0.0) company.AddMoney(-company.companyValue, true);
        OpenHearing();
    }

    /// <summary>Debug: raise the company to the win value (if below) and show the win screen right away.</summary>
    public void DebugForceWin()
    {
        if (runOver) { Debug.LogWarning("[RunEndDirector] The run is already over."); return; }
        CompanyManager company = CompanyManager.Instance;
        if (company != null && company.companyValue < winValue) company.AddMoney(winValue - company.companyValue, true);
        ShowWin();
    }

    // ------------------------------------------------------------------

    static void Raise(Action handler)
    {
        // Each listener on its own: one that throws (or outlived its scene) mustn't silence the others
        if (handler == null) return;
        foreach (Action single in handler.GetInvocationList())
        {
            try { single(); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}

/// <summary>What the bankruptcy hearing shows (RunEndDirector fills it in).</summary>
[Serializable]
public class HearingInfo
{
    public string companyName = "";
    public int day = 1;
    public double companyValue;
    public bool loanAvailable;
    public double loanAmount;
    public double debt;
    public double dailyInterest;
    public double autoRepayShare;
    public double restartValue;
    public float headsChance = 0.5f;
}

/// <summary>
/// Adds the run-end managers (RunEndDirector with RunStats and BankLoan) to every scene that has a CompanyManager, when
/// it loads, unless one is already there. So MainGame needs no scene edit.
/// </summary>
static class RunEndBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        EnsureDirector();   // the first scene loaded before this ran
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EnsureDirector();
    }

    static void EnsureDirector()
    {
        if (!Application.isPlaying) return;
        CompanyManager company = CompanyManager.Instance != null ? CompanyManager.Instance : UnityEngine.Object.FindAnyObjectByType<CompanyManager>();
        if (company == null) return;
        if (RunEndDirector.Instance != null || UnityEngine.Object.FindAnyObjectByType<RunEndDirector>(FindObjectsInactive.Include) != null) return;

        GameObject go = new GameObject("RunEnd (Runtime)");
        Scene scene = company.gameObject.scene;
        if (scene.IsValid() && scene.isLoaded && go.scene != scene)
        {
            // Live and die with the company's scene
            try { SceneManager.MoveGameObjectToScene(go, scene); }
            catch (Exception e) { Debug.LogWarning("[RunEnd] Couldn't move the run-end managers next to the CompanyManager: " + e.Message); }
        }
        go.AddComponent<RunEndDirector>();
    }
}
