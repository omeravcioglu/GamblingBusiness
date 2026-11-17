using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The demo's shell in the company scene (demo mode only; DemoDirectorBootstrap adds it):
/// - Play clock: DemoRun.PlaySeconds counts real seconds while the game has focus, the pause menu is closed and no demo
///   card is up (saved with the run).
/// - The end of the demo (DemoEndView):
///   (a) the Board meeting on DemoMode.FinaleDay (12) arms the finale; once its memo is answered and the screen has been
///       calm (RunEndDirector.IsScreenCalm) for calmSeconds, the DEMO COMPLETE card opens with the verdict;
///   (b) the time cap (endCapSeconds of play) opens it at the next safe moment; when the finale meeting is less than a day
///       away it waits up to endGraceSeconds for it, else the card says the verdict is pending;
///   (c) company value at valueCap shows the final jackpot card, at any time;
///   (d) after Keep playing, hardStopDay or hardStopSeconds shows the final card (no Keep playing).
///   A card never cuts a tutorial memo, crisis, risk card, Board meeting, run-end screen, casino round or build flip; it
///   closes the casino and leaves build mode before it opens. Continue shows the card that was up again.
/// - Each new card writes one "[Demo] end ..." line to the log and updates PlayerPrefs Demo.BestPeak.
/// </summary>
[DisallowMultipleComponent]
public class DemoDirector : MonoBehaviour
{
    public static DemoDirector Instance { get; private set; }

    /// <summary>PlayerPrefs flag: the main menu starts the next company at once, without the bank's coin toss.</summary>
    public const string SkipFundingTossKey = "Demo.SkipFundingToss";
    /// <summary>PlayerPrefs: the best peak company value of any demo run (a string, invariant culture).</summary>
    public const string BestPeakKey = "Demo.BestPeak";

    public const string ReasonVerdict = "verdict", ReasonTimeCap = "timecap", ReasonHardStop = "hardstop", ReasonJackpot = "jackpot";

    [Tooltip("Real seconds the screen must stay calm after the finale's Board memo before the card opens")]
    public float calmSeconds = 1.5f;
    [Tooltip("Real seconds nothing may be going on before a forced card (time cap, hard stop, jackpot) opens")]
    public float settleSeconds = 0.3f;

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    DemoEndView endView;
    DemoHudChip chip;
    bool finaleArmed;
    bool restoreChecked;
    bool hasRestoreCard;
    DemoEndMode restoreMode;
    float calmTimer;

    public DemoEndView EndView => endView;
    public DemoHudChip Chip => chip;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;

        GameObject viewObject = new GameObject("DemoEndView");
        viewObject.transform.SetParent(transform, false);
        endView = viewObject.AddComponent<DemoEndView>();
        endView.onWishlist = DemoMode.OpenStorePage;
        endView.onKeepPlaying = KeepPlaying;
        endView.onNewCompany = NewCompany;
        endView.onMainMenu = MainMenu;

        GameObject chipObject = new GameObject("DemoHudChip");
        chipObject.transform.SetParent(transform, false);
        chip = chipObject.AddComponent<DemoHudChip>();
    }

    void OnEnable()
    {
        BoardMeetings.MeetingOpened += OnMeetingOpened;
        BoardMeetings.TargetResolved += OnTargetResolved;
    }

    void OnDisable()
    {
        BoardMeetings.MeetingOpened -= OnMeetingOpened;
        BoardMeetings.TargetResolved -= OnTargetResolved;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ------------------------------------------------------------------
    // Board events
    // ------------------------------------------------------------------

    void OnMeetingOpened(int day)
    {
        if (DemoRun.EndCardShown) return;
        if (day >= DemoMode.FinaleDay) finaleArmed = true;
        else DemoRun.VerdictKnown = false;   // only the finale meeting's verdict counts
    }

    void OnTargetResolved(bool hit, int tier)
    {
        if (DemoRun.EndCardShown)
        {
            // Later meetings judge targets the cards don't show. After a time-cap card the finale's verdict still counts:
            // Keep playing hears it, and the final card shows it (TargetResolved comes before MeetingOpened, so check the day)
            if (DemoRun.VerdictKnown || DemoRun.EndReason != ReasonTimeCap) return;
            CompanyManager company = CompanyManager.Instance;
            if (company == null || company.currentDay < DemoMode.FinaleDay) return;
        }
        DemoRun.VerdictKnown = true;
        DemoRun.VerdictHit = hit;
        DemoRun.VerdictTier = tier;
        // BoardMeetings saved just before this event: keep the verdict even if the game dies while its memo is up
        SaveSystem save = SaveSystem.Instance;
        if (save != null && save.IsGameLoaded && !save.SavingSuspended) save.SaveGame();
    }

    // ------------------------------------------------------------------
    // Clock and triggers
    // ------------------------------------------------------------------

    void Update()
    {
        CompanyManager company = CompanyManager.Instance;
        if (company == null || !DemoRun.Exists || !SaveLoaded()) return;
        RunEndDirector runEnd = RunEndDirector.Instance;
        if (runEnd != null && runEnd.IsRunOver) return;

        bool cardOpen = endView != null && endView.IsOpen;
        if (!cardOpen && Application.isFocused && !SettingsPanel.IsPauseMenuOpen)
            DemoRun.PlaySeconds += Mathf.Min(Time.unscaledDeltaTime, 0.25f);   // a load hitch or a long unfocused frame doesn't count

        if (!restoreChecked)
        {
            // Continue: the card that was up when the game was left comes back
            restoreChecked = true;
            if (DemoRun.FinalCardShown) { hasRestoreCard = true; restoreMode = DemoRun.EndReason == ReasonJackpot ? DemoEndMode.Jackpot : DemoEndMode.Final; }
            else if (DemoRun.EndCardShown && !DemoRun.KeepPlaying) { hasRestoreCard = true; restoreMode = DemoEndMode.End; }
        }
        if (cardOpen)
        {
            calmTimer = 0f;
            return;
        }

        if (!CardDue(company, out DemoEndMode mode, out string reason, out bool needsCalmScreen, out bool restore) ||
            !NothingWouldBeCut() || (needsCalmScreen && !RunEndDirector.IsScreenCalm()))
        {
            calmTimer = 0f;
            return;
        }
        calmTimer += Time.unscaledDeltaTime;
        if (calmTimer < (needsCalmScreen ? calmSeconds : settleSeconds)) return;
        calmTimer = 0f;
        if (restore) hasRestoreCard = false;
        ShowCard(mode, reason, restore);
    }

    /// <summary>The card due now, if any (and whether it waits for a fully calm screen, like the finale).</summary>
    bool CardDue(CompanyManager company, out DemoEndMode mode, out string reason, out bool needsCalmScreen, out bool restore)
    {
        DemoConfig cfg = DemoMode.Config;
        mode = DemoEndMode.End;
        reason = "";
        needsCalmScreen = false;
        restore = false;

        if (hasRestoreCard)
        {
            mode = restoreMode;
            reason = DemoRun.EndReason;
            restore = true;
            return true;
        }
        if (DemoRun.FinalCardShown) return false;

        // (c) the jackpot: any time
        if (company.companyValue >= cfg.valueCap)
        {
            mode = DemoEndMode.Jackpot;
            reason = ReasonJackpot;
            return true;
        }

        // (d) Keep playing ran out
        if (DemoRun.EndCardShown)
        {
            if (!DemoRun.KeepPlaying) return false;
            if (company.currentDay < cfg.hardStopDay && DemoRun.PlaySeconds < cfg.hardStopSeconds) return false;
            mode = DemoEndMode.Final;
            reason = ReasonHardStop;
            return true;
        }

        bool capReached = DemoRun.PlaySeconds >= cfg.endCapSeconds;
        // (a) the finale: once its memo is answered; past the time cap it no longer waits for a fully calm screen
        BoardMeetings board = BoardMeetings.Instance;
        if (FinaleHeld(company))
        {
            if (board != null && board.IsMeetingOpen) return false;
            reason = ReasonVerdict;
            needsCalmScreen = !capReached;
            return true;
        }

        // (b) the time cap, with grace when the finale meeting is less than a day away
        if (!capReached) return false;
        bool finaleClose = board != null && company.currentDay >= DemoMode.FinaleDay - 1;
        if (finaleClose && DemoRun.PlaySeconds < cfg.endCapSeconds + cfg.endGraceSeconds) return false;
        reason = ReasonTimeCap;
        return true;
    }

    /// <summary>The finale's Board meeting opened this session, or was held and answered before the game was saved.</summary>
    bool FinaleHeld(CompanyManager company)
    {
        if (finaleArmed) return true;
        BoardMeetings board = BoardMeetings.Instance;
        if (board == null || board.IsMeetingOpen || company.currentDay < DemoMode.FinaleDay) return false;
        // The last meeting (one interval before the next) was on the finale day or later; a first meeting that slipped
        // a day (next one on day 13) doesn't count
        return board.NextMeetingDay - Mathf.Max(1, board.meetingInterval) >= DemoMode.FinaleDay;
    }

    /// <summary>
    /// The day of the Board meeting whose verdict ends the demo: DemoMode.FinaleDay, or the meeting after it when an
    /// earlier meeting slipped a day; once the finale was held, the day it was held.
    /// </summary>
    public static int FinaleMeetingDay()
    {
        int finale = DemoMode.FinaleDay;
        BoardMeetings board = BoardMeetings.Instance;
        if (board == null) return finale;
        int next = board.NextMeetingDay;
        int last = next - Mathf.Max(1, board.meetingInterval);
        if (last >= finale) return last;
        return next > finale ? next : finale;
    }

    /// <summary>
    /// Nothing a card would cut is going on: no tutorial memo, crisis, risk card, Board meeting, run-end screen, casino
    /// round, build flip, sofa game or text input, and the save is loaded. Other pop-ups may stay under the card.
    /// </summary>
    static bool NothingWouldBeCut()
    {
        if (UIManager.IsTypingInInputField()) return false;
        TutorialDirector tutorial = TutorialDirector.Instance;
        if (tutorial != null && tutorial.IsShowing) return false;
        if (MiniGameManager.Instance != null && MiniGameManager.Instance.IsMiniGameActive()) return false;
        if (EventManager.Instance != null && EventManager.Instance.IsEventInProgress()) return false;
        BoardMeetings board = BoardMeetings.Instance;
        if (board != null && board.IsMeetingOpen) return false;
        RunEndDirector runEnd = RunEndDirector.Instance;
        if (runEnd != null && (runEnd.IsRunOver || runEnd.IsAnyScreenOpen)) return false;
        CasinoManager casino = CasinoManager.Instance;
        if (casino != null && casino.IsRoundInProgress()) return false;
        BuildGambleView gamble = BuildGambleView.Instance;
        if (gamble != null && (gamble.IsBusy || gamble.PendingBet > 0.0)) return false;
        if (OfficeUpgradeManager.Instance != null && OfficeUpgradeManager.Instance.IsBusy) return false;
        if (BlackjackGame.Instance != null && BlackjackGame.Instance.IsOpen) return false;
        return SaveLoaded();
    }

    static bool SaveLoaded()
    {
        SaveSystem save = SaveSystem.Instance;
        return save == null || (!save.IsLoading && save.IsGameLoaded);
    }

    // ------------------------------------------------------------------
    // Cards
    // ------------------------------------------------------------------

    void ShowCard(DemoEndMode mode, string reason, bool restore)
    {
        if (endView == null) return;

        // Close what the card would otherwise sit on top of
        CasinoManager casino = CasinoManager.Instance;
        if (casino != null && casino.IsOpen) casino.CloseCasino();
        BuildModeCamera buildCamera = BuildModeCamera.Instance;
        if (buildCamera != null && buildCamera.IsInBuildMode()) buildCamera.ExitBuildMode();

        double best = BestPeak();
        DemoEndInfo info = Collect(best);
        if (!restore)
        {
            DemoRun.EndReason = reason;
            if (mode == DemoEndMode.End) DemoRun.EndCardShown = true;
            else DemoRun.FinalCardShown = true;
            info.newBest = best > 0.5 && info.peak > best + 0.5;
            if (info.peak > best) SetBestPeak(info.peak);
            LogEnd(reason, info);
            // Remember right away that it was shown (Continue shows it again)
            if (SaveSystem.Instance != null) SaveSystem.Instance.SaveGame();
        }
        endView.Show(mode, info);
    }

    DemoEndInfo Collect(double best)
    {
        CompanyManager company = CompanyManager.Instance;
        RunStats stats = RunStats.Instance;
        double value = company != null ? Math.Max(0.0, company.companyValue) : 0.0;
        var info = new DemoEndInfo
        {
            companyName = CompanyNameUI.GetCompanyName(),
            days = company != null ? Mathf.Max(1, company.currentDay) : 1,
            startValue = DemoMode.StartingValue,
            value = value,
            peak = Math.Max(value, stats != null ? stats.PeakValue : 0.0),
            best = best,
            hires = stats != null ? stats.Hires : 0,
            goldenHires = stats != null ? stats.GoldenHires : 0,
            rooms = TowerBuilder.Instance != null ? TowerBuilder.Instance.Plan.BuiltRoomCount : 0,
            desks = OfficeCapacity.Capacity,
            audits = HeatMeter.Instance != null ? HeatMeter.Instance.AuditCount : 0,
            crisesSolved = stats != null ? stats.CrisesSolved : 0,
            crisesTotal = stats != null ? stats.CrisesTotal : 0,
            hasGambled = stats != null && stats.HasGambled,
            casinoNet = stats != null ? stats.CasinoNet : 0.0,
            biggestWin = stats != null ? stats.BiggestWin : 0.0,
            verdict = CurrentVerdict(company),
            verdictTier = DemoRun.VerdictTier,
            finaleDay = FinaleMeetingDay(),
            houseFlip = DemoRun.HouseFlipUsed,
            nextTarget = BoardMeetings.Instance != null && BoardMeetings.Instance.HasTarget,
            managerLocked = DemoMode.IsLocked(Feature.Manager)
        };
        return info;
    }

    DemoVerdict CurrentVerdict(CompanyManager company)
    {
        if (DemoRun.VerdictKnown) return DemoRun.VerdictHit ? DemoVerdict.Hit : DemoVerdict.Miss;
        if (company != null && FinaleHeld(company)) return DemoVerdict.Skipped;
        return DemoVerdict.Pending;
    }

    static double BestPeak()
    {
        string saved = PlayerPrefs.GetString(BestPeakKey, "");
        return double.TryParse(saved, NumberStyles.Float, Inv, out double best) && best > 0.0 ? best : 0.0;
    }

    static void SetBestPeak(double peak)
    {
        PlayerPrefs.SetString(BestPeakKey, Math.Round(peak).ToString("R", Inv));
        PlayerPrefs.Save();
    }

    // One line per card for tuning from playtesters' logs
    void LogEnd(string reason, DemoEndInfo info)
    {
        SkillTreeManager skills = SkillTreeManager.Instance;
        string verdict = info.verdict == DemoVerdict.Hit || info.verdict == DemoVerdict.Miss
            ? (info.verdict == DemoVerdict.Hit ? "hit-" : "miss-") + BoardMeetings.TierEnglishName(info.verdictTier)
            : info.verdict.ToString().ToLowerInvariant();
        Debug.Log("[Demo] end reason=" + reason +
                  " minutes=" + (DemoRun.PlaySeconds / 60f).ToString("0.0", Inv) +
                  " day=" + info.days +
                  " value=" + info.value.ToString("0", Inv) +
                  " peak=" + info.peak.ToString("0", Inv) +
                  " casinoNet=" + info.casinoNet.ToString("0", Inv) +
                  " audits=" + info.audits +
                  " sp=" + (skills != null ? skills.skillPoints : 0) +
                  " hires=" + info.hires +
                  " rooms=" + info.rooms +
                  " verdict=" + verdict);
    }

    // ------------------------------------------------------------------
    // Buttons
    // ------------------------------------------------------------------

    void KeepPlaying()
    {
        if (endView == null || endView.Mode != DemoEndMode.End) return;
        DemoRun.KeepPlaying = true;
        endView.Close();
        if (SaveSystem.Instance != null) SaveSystem.Instance.SaveGame();
    }

    /// <summary>End this company and go to the main menu, which starts the next one at once (no coin toss).</summary>
    void NewCompany()
    {
        if (!SceneFlow.CanLoad(SceneFlow.MainMenuScene))
        {
            Debug.LogError("[Demo] '" + SceneFlow.MainMenuScene + "' is not in Build Settings, so the game can't go back to the menu.");
            return;
        }
        PlayerPrefs.SetInt(SkipFundingTossKey, 1);
        PlayerPrefs.Save();
        RunEndDirector runEnd = RunEndDirector.Instance;
        if (runEnd != null) runEnd.StartNewCompany();
        else SceneFlow.Load(SceneFlow.MainMenuScene);
    }

    /// <summary>Back to the menu; the save is kept and Continue shows the card again.</summary>
    void MainMenu()
    {
        RunEndDirector runEnd = RunEndDirector.Instance;
        if (runEnd != null) runEnd.GoToMainMenu();
        else SceneFlow.Load(SceneFlow.MainMenuScene);
    }

    // ------------------------------------------------------------------
    // Debug (Tools > Game > Demo)
    // ------------------------------------------------------------------

    /// <summary>Debug: show a card now (it counts as shown, like a real one).</summary>
    public void DebugShow(DemoEndMode mode)
    {
        if (endView != null && endView.IsOpen) endView.Close();
        string reason = mode == DemoEndMode.Jackpot ? ReasonJackpot : mode == DemoEndMode.Final ? ReasonHardStop : ReasonVerdict;
        ShowCard(mode, reason, false);
    }

    /// <summary>Debug: what the demo knows about this run, in one log line.</summary>
    public string DescribeState()
    {
        CompanyManager company = CompanyManager.Instance;
        DemoConfig cfg = DemoMode.Config;
        return "[Demo] state: day=" + (company != null ? company.currentDay : 0) + "/" + DemoMode.FinaleDay +
               " playSeconds=" + DemoRun.PlaySeconds.ToString("0", Inv) + " (cap " + cfg.endCapSeconds.ToString("0", Inv) +
               " + grace " + cfg.endGraceSeconds.ToString("0", Inv) + ", hard stop " + cfg.hardStopSeconds.ToString("0", Inv) +
               " or day " + cfg.hardStopDay + ") value=" + (company != null ? company.companyValue.ToString("0", Inv) : "?") +
               " finaleArmed=" + finaleArmed + " finaleHeld=" + (company != null && FinaleHeld(company)) +
               " verdict=" + CurrentVerdict(company) + " tier=" + DemoRun.VerdictTier +
               " endCard=" + DemoRun.EndCardShown + " keepPlaying=" + DemoRun.KeepPlaying + " finalCard=" + DemoRun.FinalCardShown +
               " reason=\"" + DemoRun.EndReason + "\" houseFlip=" + DemoRun.HouseFlipUsed + " sofaTip=" + DemoRun.SofaHintShown +
               " bestPeak=" + BestPeak().ToString("0", Inv) + " cardOpen=" + (endView != null && endView.IsOpen) +
               " safeNow=" + NothingWouldBeCut() + " calm=" + RunEndDirector.IsScreenCalm();
    }
}

/// <summary>Adds the DemoDirector to every scene with a CompanyManager while demo mode is on.</summary>
static class DemoDirectorBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (!DemoMode.Active) return;
        SceneManager.sceneLoaded += OnSceneLoaded;
        EnsureDirector();   // the first scene loaded before this ran
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EnsureDirector();
    }

    static void EnsureDirector()
    {
        if (!Application.isPlaying || !DemoMode.Active) return;
        CompanyManager company = CompanyManager.Instance != null ? CompanyManager.Instance : UnityEngine.Object.FindAnyObjectByType<CompanyManager>();
        if (company == null) return;
        if (DemoDirector.Instance != null || UnityEngine.Object.FindAnyObjectByType<DemoDirector>(FindObjectsInactive.Include) != null) return;

        GameObject go = new GameObject("DemoDirector (Runtime)");
        Scene scene = company.gameObject.scene;
        if (scene.IsValid() && scene.isLoaded && go.scene != scene)
        {
            // Live and die with the company's scene
            try { SceneManager.MoveGameObjectToScene(go, scene); }
            catch (Exception e) { Debug.LogWarning("[Demo] Couldn't move the demo director next to the CompanyManager: " + e.Message); }
        }
        go.AddComponent<DemoDirector>();
    }
}
