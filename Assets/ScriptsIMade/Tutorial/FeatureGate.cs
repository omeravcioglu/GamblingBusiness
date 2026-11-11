using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A system that a new company opens one at a time (FeatureGate). The names are written into saves ("unlocks"): never
/// rename them.
/// </summary>
public enum Feature
{
    RiskCards,
    Crises,
    Skills,
    Staff,
    Build,
    BoardMeetings,
    Heat,
    Stocks,
    Casino,
    Online,
    Manager   // the manager arrives (ManagerSystem: the arrival is the candidate pick, no Board memo)
}

/// <summary>
/// Gradual feature unlocks for new players (a playtester found the game "very crowded and hard to understand"): a new
/// company starts with the hiring desk only, and the other systems open at progress milestones. The first time a player
/// profile meets a system, the Board sends a one-time memo about it (TutorialDirector).
/// - The schedule is below (owner-approved, 2026-09-28). Casino keeps its Casino License rule (CasinoManager) and Online
///   its $1B rule (OfficeHud / CityOnline): FeatureGate only notices when they open.
/// - Unlocks belong to the run: FeatureGateRunner keeps them in the save ("unlocks"), a New Game starts gated again, and a
///   company from before this system (a save without "unlocks") gets everything its progress already qualifies for,
///   silently. Memos (and the first-day tour) are once per player profile (PlayerPrefs "Tutorial.*"): later runs just
///   unlock, with a NEW badge on the HUD tab.
/// - Hooks: EventManager (risk-card countdown), MiniGameManager (crisis countdown), BoardMeetings (the first meeting),
///   HeatChipView (the chip), BuildModeCamera (Q) and OfficeHud (tabs and their Open methods) ask IsUnlocked / AllowOpen /
///   HoldForMemo. With no runner in the scene (no company, or a scene without the tutorial) everything counts as unlocked,
///   so nothing is ever blocked by a missing piece.
/// - Player texts come from the "Tutorial" String Table (Assets/Localization/Source/Tutorial.csv).
/// </summary>
public static class FeatureGate
{
    // ------------------------------------------------------------------
    // The unlock schedule (owner-approved, 2026-09-28): change the numbers here and nowhere else
    // (Demo: the Steam demo reads its own schedule from DemoMode.Config; each value below is the full game's otherwise)
    // ------------------------------------------------------------------

    /// <summary>Risk cards start after this many hires.</summary>
    public static int RiskCardsAfterHires => DemoMode.Active ? DemoMode.Config.riskCardsAfterHires : 3;
    /// <summary>Crises (the mini-games) start on this day.</summary>
    public static int CrisesFromDay => DemoMode.Active ? DemoMode.Config.crisesFromDay : 5;
    /// <summary>The staff files open at this many employees.</summary>
    public static int StaffAtEmployees => DemoMode.Active ? DemoMode.Config.staffAtEmployees : 5;
    /// <summary>Build mode (the HQ tower) opens on this day (the owner moved it from day 8 to day 3 on 2026-09-29).</summary>
    public static int BuildFromDay => DemoMode.Active ? DemoMode.Config.buildFromDay : 3;
    /// <summary>Board meetings start on this day (the first meeting, BoardMeetings.firstMeetingDay, is on day 10 too).</summary>
    public static int BoardMeetingsFromDay => DemoMode.Active ? DemoMode.Config.boardFirstDay : 10;
    /// <summary>The stock market opens when company value first reaches this.</summary>
    public static double StocksAtValue => 250000d;
    /// <summary>The manager arrives on this day: the player picks one of three candidates (owner's decision, 2026-09-29).</summary>
    public static int ManagerFromDay => DemoMode.Active ? DemoMode.Config.managerFromDay : 5;
    // Skills: the first skill point (or any skill point / unlocked skill).
    // Heat: the first gamble (GambleLedger.Gambled), or heat reaching Warm some other way (risk cards), so its effects
    //       never hide behind a hidden chip.
    // Casino: CasinoManager.IsCasinoAvailable (the Casino License skill), unchanged.
    // Online: the $1B City unlock (OfficeHud.OnlineUnlockValue / CityOnline.IsUnlocked), unchanged; no memo (the $1B
    //         certificate announces it).

    /// <summary>The String Table collection of the tutorial texts (Assets/Localization/Source/Tutorial.csv).</summary>
    public const string Table = "Tutorial";

    /// <summary>PlayerPrefs: the first-day tour was finished or skipped on this profile.</summary>
    public static string TourDoneKey => DemoPrefix + "Tutorial.TourDone";
    /// <summary>PlayerPrefs prefix: the memo of a feature was shown on this profile ("Tutorial.MemoSeen.Skills").</summary>
    public static string MemoSeenPrefix => DemoPrefix + "Tutorial.MemoSeen.";

    // Demo: the demo keeps its own tour and memo memory, so the full game's tutorial stays untouched on the same PC
    static string DemoPrefix => DemoMode.Active ? "Demo." : "";

    const float ToastGapSeconds = 1.5f;

    static readonly Feature[] all = (Feature[])Enum.GetValues(typeof(Feature));
    static readonly float[] lastToast = new float[all.Length];

    /// <summary>Every feature, in schedule order.</summary>
    public static IReadOnlyList<Feature> All => all;

    /// <summary>
    /// A feature opened in the current run (not raised for the silent unlocks of an older save or of Unlock Everything).
    /// Each listener is called on its own.
    /// </summary>
    public static event Action<Feature> Unlocked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        // Entering Play mode without a domain reload keeps statics
        Unlocked = null;
        for (int i = 0; i < lastToast.Length; i++) lastToast[i] = -100f;
    }

    // ------------------------------------------------------------------
    // State (the scene's FeatureGateRunner)
    // ------------------------------------------------------------------

    /// <summary>
    /// True when the feature is open in this run. Also true while the run isn't loaded yet (the first frames of a scene)
    /// and whenever there's no FeatureGateRunner, so a missing piece never blocks the game.
    /// </summary>
    public static bool IsUnlocked(Feature feature)
    {
        if (DemoMode.IsLocked(feature)) return false;   // Demo: kept for the full game, never opens
        FeatureGateRunner runner = FeatureGateRunner.Instance;
        return runner == null || runner.IsUnlocked(feature);
    }

    /// <summary>True once the run's unlocks are known (a new company, or a loaded save).</summary>
    public static bool IsKnown => FeatureGateRunner.Instance != null && FeatureGateRunner.Instance.IsStateKnown;

    /// <summary>Open a feature now, as its milestone would (NEW badge, and its memo if this profile hasn't seen it).</summary>
    public static void Unlock(Feature feature)
    {
        FeatureGateRunner runner = FeatureGateRunner.Instance;
        if (runner != null) runner.Unlock(feature, false);
    }

    /// <summary>Open every feature of the current run without memos or badges (debug: Tools > Game > Tutorial).</summary>
    public static void UnlockAll()
    {
        FeatureGateRunner runner = FeatureGateRunner.Instance;
        if (runner != null) runner.UnlockAll();
    }

    /// <summary>The feature's HUD tab shows a NEW badge: it opened in this run and hasn't been opened since.</summary>
    public static bool IsNew(Feature feature)
    {
        FeatureGateRunner runner = FeatureGateRunner.Instance;
        return runner != null && runner.IsNew(feature);
    }

    /// <summary>The player opened the feature's screen (clears its NEW badge). The runner also notices by itself.</summary>
    public static void MarkOpened(Feature feature)
    {
        FeatureGateRunner runner = FeatureGateRunner.Instance;
        if (runner != null) runner.MarkOpened(feature);
    }

    /// <summary>
    /// For a screen's open method: true when the feature is open; otherwise a toast says when it opens (at most every
    /// 1.5 s) and false comes back.
    /// </summary>
    public static bool AllowOpen(Feature feature)
    {
        if (IsUnlocked(feature)) return true;
        ShowLockedToast(feature);
        return false;
    }

    /// <summary>The "opens ..." toast of a locked feature (at most every 1.5 s per feature).</summary>
    public static void ShowLockedToast(Feature feature)
    {
        string message = LockedMessage(feature);
        if (string.IsNullOrEmpty(message)) return;
        int i = (int)feature;
        float now = Time.realtimeSinceStartup;
        if (i >= 0 && i < lastToast.Length)
        {
            if (now - lastToast[i] < ToastGapSeconds) return;
            lastToast[i] = now;
        }
        GameNotifications.Show(message, NotificationKind.Info);
    }

    /// <summary>
    /// For a system whose first appearance comes with a memo (risk cards, crises, the first board meeting): true while that
    /// memo is owed or on screen, so the system waits; asking also brings the memo up as soon as the screen allows. False
    /// when there's no memo to wait for (already seen on this profile, an older save, or no TutorialDirector).
    /// </summary>
    public static bool HoldForMemo(Feature feature)
    {
        TutorialDirector director = TutorialDirector.Instance;
        return director != null && director.HoldForMemo(feature);
    }

    // ------------------------------------------------------------------
    // What each feature has
    // ------------------------------------------------------------------

    /// <summary>Features with a HUD tab that gets a NEW badge when it opens (Online keeps its own badge).</summary>
    public static bool HasTab(Feature feature)
    {
        return feature == Feature.Skills || feature == Feature.Staff || feature == Feature.Stocks ||
               feature == Feature.Build || feature == Feature.Casino;
    }

    /// <summary>Features whose tab FeatureGate locks (Casino and Online keep their own rules in OfficeHud).</summary>
    public static bool GatesTab(Feature feature)
    {
        // Demo: FeatureTabs also explains the full-game tabs and the casino's demo schedule
        if (DemoMode.Active && (DemoMode.IsLocked(feature) || feature == Feature.Casino)) return true;
        return feature == Feature.Skills || feature == Feature.Staff || feature == Feature.Stocks || feature == Feature.Build;
    }

    /// <summary>Features with a Board memo (Online has the $1B certificate instead).</summary>
    public static bool HasMemo(Feature feature)
    {
        if (DemoMode.Active && feature == Feature.Staff) return false;   // Demo: a toast instead (FeatureGateRunner)
        return feature != Feature.Online && feature != Feature.Manager;   // the manager's arrival is its own candidate pick
    }

    /// <summary>Memos that wait for the system's first appearance instead of showing at the unlock (see HoldForMemo).</summary>
    public static bool MemoWaitsForFirstUse(Feature feature)
    {
        return feature == Feature.RiskCards || feature == Feature.Crises;
    }

    // ------------------------------------------------------------------
    // Per player profile (PlayerPrefs)
    // ------------------------------------------------------------------

    /// <summary>This profile has seen the feature's memo.</summary>
    public static bool HasSeenMemo(Feature feature)
    {
        return PlayerPrefs.GetInt(MemoSeenPrefix + feature, 0) == 1;
    }

    public static void MarkMemoSeen(Feature feature)
    {
        PlayerPrefs.SetInt(MemoSeenPrefix + feature, 1);
        PlayerPrefs.Save();
    }

    /// <summary>This profile has finished or skipped the first-day tour.</summary>
    public static bool TourDone => PlayerPrefs.GetInt(TourDoneKey, 0) == 1;

    public static void MarkTourDone()
    {
        PlayerPrefs.SetInt(TourDoneKey, 1);
        PlayerPrefs.Save();
    }

    /// <summary>Forget the tour and every memo on this profile (debug: Tools > Game > Tutorial > Reset Tutorial).</summary>
    public static void ResetProfile()
    {
        PlayerPrefs.DeleteKey(TourDoneKey);
        foreach (Feature feature in all)
            PlayerPrefs.DeleteKey(MemoSeenPrefix + feature);
        PlayerPrefs.Save();
    }

    // ------------------------------------------------------------------
    // Texts
    // ------------------------------------------------------------------

    /// <summary>
    /// A locked tab's tooltip (after "Skills — "): "opens with your first skill point". Empty for features without a gated
    /// tab.
    /// </summary>
    public static string LockedHint(Feature feature)
    {
        string demo = DemoLockedHint(feature);   // Demo: full-game features and the casino's demo schedule
        if (demo != null) return demo;
        switch (feature)
        {
            case Feature.Skills: return L10n.Get(Table, "tutorial.hint.skills");
            case Feature.Staff: return L10n.Get(Table, "tutorial.hint.staff", ("count", StaffAtEmployees));
            case Feature.Stocks: return L10n.Get(Table, "tutorial.hint.stocks", ("amount", UIFormat.Money(StocksAtValue)));
            case Feature.Build: return L10n.Get(Table, "tutorial.hint.build", ("day", BuildFromDay));
            default: return "";
        }
    }

    /// <summary>The toast for a locked feature: "Build mode opens on day 3." Empty for features without one.</summary>
    public static string LockedMessage(Feature feature)
    {
        string demo = DemoLockedMessage(feature);   // Demo: full-game features and the casino's demo schedule
        if (demo != null) return demo;
        switch (feature)
        {
            case Feature.Skills: return L10n.Get(Table, "tutorial.toast.skills");
            case Feature.Staff: return L10n.Get(Table, "tutorial.toast.staff", ("count", StaffAtEmployees));
            case Feature.Stocks: return L10n.Get(Table, "tutorial.toast.stocks", ("amount", UIFormat.Money(StocksAtValue)));
            case Feature.Build: return L10n.Get(Table, "tutorial.toast.build", ("day", BuildFromDay));
            default: return "";
        }
    }

    // Demo: the tooltip of a feature the demo keeps for the full game, or of the casino before the Board grants it.
    // Null when there's no demo text (and always outside the demo).
    static string DemoLockedHint(Feature feature)
    {
        if (!DemoMode.Active) return null;
        if (DemoMode.IsLocked(feature)) return DemoMode.T("demo.hint.full_game");
        if (feature == Feature.Casino)
            return DemoMode.T("demo.hint.casino", ("count", DemoMode.Config.casinoAfterHires), ("day", DemoMode.Config.casinoFromDay));
        return null;
    }

    // Demo: the toast of the same features (null = no demo text; a locked feature without its own toast keeps its usual one)
    static string DemoLockedMessage(Feature feature)
    {
        if (!DemoMode.Active) return null;
        if (DemoMode.IsLocked(feature))
        {
            switch (feature)
            {
                case Feature.Stocks: return DemoMode.T("demo.toast.full_game.stocks");
                case Feature.Online: return DemoMode.T("demo.toast.full_game.online");
                case Feature.Manager: return DemoMode.T("demo.toast.full_game.manager");
            }
        }
        if (feature == Feature.Casino)
            return DemoMode.T("demo.toast.casino", ("count", DemoMode.Config.casinoAfterHires), ("day", DemoMode.Config.casinoFromDay));
        return null;
    }

    // ------------------------------------------------------------------

    /// <summary>FeatureGateRunner: a feature opened during play. Each listener on its own.</summary>
    internal static void RaiseUnlocked(Feature feature)
    {
        Action<Feature> handlers = Unlocked;
        if (handlers == null) return;
        foreach (Action<Feature> handler in handlers.GetInvocationList())
        {
            try { handler(feature); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}
