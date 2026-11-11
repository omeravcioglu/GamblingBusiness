using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > Game > Tutorial: try the first-day tour and the gradual unlocks.
/// - Replay Tour (Play mode, MainGame): shows the tour at the next calm moment.
/// - Reset Tutorial: forgets the tour and every Board memo on this player profile (PlayerPrefs "Tutorial.*"), so the
///   next new company gets the tour and each feature's memo again. Works in Edit mode too.
/// - Unlock Everything (Play mode): opens every feature of the current run, without memos or badges.
/// - Show Memo > (feature) (Play mode): shows that feature's memo now, whatever was seen.
/// - Log Unlocks (Play mode): writes the run's state to the Console.
/// - Show Demo Welcome (Play mode): replays the tour with the Steam demo's words (demo mode on or off).
/// - Log Demo Schedule: the unlock schedule FeatureGate plays by now and the DemoMode config behind it.
/// </summary>
public static class TutorialDebugMenu
{
    const string Root = "Tools/Game/Tutorial/";

    [MenuItem(Root + "Replay Tour", false, 10)]
    static void ReplayTour()
    {
        TutorialDirector director = Director();
        if (director != null) director.ReplayTour();
    }

    [MenuItem(Root + "Replay Tour", true)]
    static bool ReplayTourValidate() => Application.isPlaying;

    [MenuItem(Root + "Reset Tutorial", false, 11)]
    static void ResetTutorial()
    {
        FeatureGate.ResetProfile();
        Debug.Log("[Tutorial] Forgot the tour and the Board memos on this profile: the next new company gets them again" +
                  (Application.isPlaying ? " (Replay Tour shows the tour now)." : "."));
    }

    [MenuItem(Root + "Unlock Everything", false, 30)]
    static void UnlockEverything()
    {
        if (Runner() == null) return;
        FeatureGate.UnlockAll();
        Debug.Log("[Tutorial] Every feature of this run is open.");
    }

    [MenuItem(Root + "Unlock Everything", true)]
    static bool UnlockEverythingValidate() => Application.isPlaying;

    [MenuItem(Root + "Log Unlocks", false, 31)]
    static void LogUnlocks()
    {
        FeatureGateRunner runner = Runner();
        if (runner == null) return;
        var open = new System.Text.StringBuilder();
        foreach (Feature feature in FeatureGate.All)
        {
            open.Append(feature).Append(": ").Append(FeatureGate.IsUnlocked(feature) ? "open" : "locked");
            if (FeatureGate.IsNew(feature)) open.Append(" (NEW)");
            if (runner.IsMemoOwed(feature)) open.Append(" (memo owed)");
            if (FeatureGate.HasSeenMemo(feature)) open.Append(" (memo seen)");
            open.Append('\n');
        }
        Debug.Log("[Tutorial] " + (runner.IsNewRun ? "New company" : "Loaded company") + ", " + runner.Hires + " hires, tour " +
                  (FeatureGate.TourDone ? "done" : "not done") + ":\n" + open);
    }

    [MenuItem(Root + "Log Unlocks", true)]
    static bool LogUnlocksValidate() => Application.isPlaying;

    // ---- Demo ----

    [MenuItem(Root + "Show Demo Welcome", false, 70)]
    static void ShowDemoWelcome()
    {
        TutorialDirector director = Director();
        if (director == null) return;
        director.DebugShowDemoWelcome();
        Debug.Log("[Tutorial] The tour replays with the Steam demo's words at the next calm moment" +
                  (DemoMode.Active ? "." : " (demo mode is off: only the words are the demo's)."));
    }

    [MenuItem(Root + "Show Demo Welcome", true)]
    static bool ShowDemoWelcomeValidate() => Application.isPlaying;

    /// <summary>Every schedule value FeatureGate plays by right now, and the demo's numbers behind them. Works in Edit mode too.</summary>
    [MenuItem(Root + "Log Demo Schedule", false, 71)]
    static void LogDemoSchedule()
    {
        DemoConfig c = DemoMode.Config;
        var log = new System.Text.StringBuilder();
        log.Append("[Tutorial] Demo mode ").Append(DemoMode.Active ? "ON" : "off (the values below are the full game's)").Append(":\n");
        log.Append("Risk cards after ").Append(FeatureGate.RiskCardsAfterHires).Append(" hires, crises from day ").Append(FeatureGate.CrisesFromDay)
           .Append(", staff at ").Append(FeatureGate.StaffAtEmployees).Append(" employees, build from day ").Append(FeatureGate.BuildFromDay)
           .Append(", board from day ").Append(FeatureGate.BoardMeetingsFromDay).Append(", manager from day ").Append(FeatureGate.ManagerFromDay)
           .Append(", stocks at ").Append(FeatureGate.StocksAtValue.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
        log.Append("PlayerPrefs: ").Append(FeatureGate.TourDoneKey).Append(", ").Append(FeatureGate.MemoSeenPrefix).Append("*\n");
        foreach (Feature feature in FeatureGate.All)
            if (DemoMode.IsLocked(feature)) log.Append("Locked for the full game: ").Append(feature).Append('\n');
        log.Append("Demo config: ").Append(JsonUtility.ToJson(c, true));
        Debug.Log(log.ToString());
    }

    // ---- Show Memo > ... ----

    [MenuItem(Root + "Show Memo/Risk Cards", false, 50)] static void MemoRiskCards() => ShowMemo(Feature.RiskCards);
    [MenuItem(Root + "Show Memo/Crises", false, 51)] static void MemoCrises() => ShowMemo(Feature.Crises);
    [MenuItem(Root + "Show Memo/Skills", false, 52)] static void MemoSkills() => ShowMemo(Feature.Skills);
    [MenuItem(Root + "Show Memo/Staff", false, 53)] static void MemoStaff() => ShowMemo(Feature.Staff);
    [MenuItem(Root + "Show Memo/Build", false, 54)] static void MemoBuild() => ShowMemo(Feature.Build);
    [MenuItem(Root + "Show Memo/Board Meetings", false, 55)] static void MemoBoard() => ShowMemo(Feature.BoardMeetings);
    [MenuItem(Root + "Show Memo/Heat", false, 56)] static void MemoHeat() => ShowMemo(Feature.Heat);
    [MenuItem(Root + "Show Memo/Stocks", false, 57)] static void MemoStocks() => ShowMemo(Feature.Stocks);
    [MenuItem(Root + "Show Memo/Casino", false, 58)] static void MemoCasino() => ShowMemo(Feature.Casino);

    [MenuItem(Root + "Show Memo/Risk Cards", true)]
    [MenuItem(Root + "Show Memo/Crises", true)]
    [MenuItem(Root + "Show Memo/Skills", true)]
    [MenuItem(Root + "Show Memo/Staff", true)]
    [MenuItem(Root + "Show Memo/Build", true)]
    [MenuItem(Root + "Show Memo/Board Meetings", true)]
    [MenuItem(Root + "Show Memo/Heat", true)]
    [MenuItem(Root + "Show Memo/Stocks", true)]
    [MenuItem(Root + "Show Memo/Casino", true)]
    static bool ShowMemoValidate() => Application.isPlaying;

    static void ShowMemo(Feature feature)
    {
        TutorialDirector director = Director();
        if (director != null) director.DebugShowMemo(feature);
    }

    static FeatureGateRunner Runner()
    {
        FeatureGateRunner runner = FeatureGateRunner.Ensure();
        if (runner == null)
            Debug.LogWarning("[Tutorial] There is no company in this scene: enter Play mode in MainGame first.");
        return runner;
    }

    static TutorialDirector Director()
    {
        if (Runner() == null) return null;
        TutorialDirector director = TutorialDirector.Instance;
        if (director == null)
            Debug.LogWarning("[Tutorial] The scene has no TutorialDirector yet - try again in a moment.");
        return director;
    }
}
