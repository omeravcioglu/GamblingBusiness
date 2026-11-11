using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > Game > Demo: try the demo's end while MainGame plays in demo mode (Tools > Game > Demo Mode on).
/// - Show End / Final / Jackpot Card: opens that card now (it counts as shown, like a real one, and logs its end line).
/// - Raise Value to the Cap: lifts the company to the demo's value cap ($10M), so the real jackpot trigger fires.
/// - Add 60 s Play Time: moves the play clock towards the time cap and the hard stop.
/// - Log Demo State: one line with the clock, the day, the verdict and the card flags.
/// </summary>
public static class DemoDebugMenu
{
    const string Root = "Tools/Game/Demo/";

    [MenuItem(Root + "Show End Card")]
    static void ShowEnd() => Show(DemoEndMode.End);

    [MenuItem(Root + "Show Final Card")]
    static void ShowFinal() => Show(DemoEndMode.Final);

    [MenuItem(Root + "Show Jackpot Card")]
    static void ShowJackpot() => Show(DemoEndMode.Jackpot);

    [MenuItem(Root + "Raise Value to the Cap")]
    static void RaiseToCap()
    {
        if (Director() == null) return;
        CompanyManager company = CompanyManager.Instance;
        double cap = DemoMode.Config.valueCap;
        if (company != null && company.companyValue < cap) company.AddMoney(cap - company.companyValue, true);
    }

    [MenuItem(Root + "Add 60 s Play Time")]
    static void AddPlayTime()
    {
        if (Director() == null) return;
        DemoRun.PlaySeconds += 60f;
        Debug.Log("[Demo] Play time is now " + DemoRun.PlaySeconds.ToString("0") + " s.");
    }

    [MenuItem(Root + "Log Demo State")]
    static void LogState()
    {
        DemoDirector director = Director();
        if (director != null) Debug.Log(director.DescribeState());
    }

    [MenuItem(Root + "Show End Card", true)]
    [MenuItem(Root + "Show Final Card", true)]
    [MenuItem(Root + "Show Jackpot Card", true)]
    [MenuItem(Root + "Raise Value to the Cap", true)]
    [MenuItem(Root + "Add 60 s Play Time", true)]
    [MenuItem(Root + "Log Demo State", true)]
    static bool CanUse()
    {
        return EditorApplication.isPlaying;
    }

    static void Show(DemoEndMode mode)
    {
        DemoDirector director = Director();
        if (director != null) director.DebugShow(mode);
    }

    static DemoDirector Director()
    {
        if (!EditorApplication.isPlaying)
        {
            Debug.LogWarning("[Demo] Enter Play mode in MainGame first.");
            return null;
        }
        if (!DemoMode.Active)
        {
            Debug.LogWarning("[Demo] Demo mode is off: switch it on with Tools > Game > Demo Mode (Steam demo) before entering Play mode.");
            return null;
        }
        DemoDirector director = DemoDirector.Instance != null ? DemoDirector.Instance : Object.FindAnyObjectByType<DemoDirector>();
        if (director == null)
            Debug.LogWarning("[Demo] No DemoDirector in this scene (it is added to scenes with a CompanyManager, like MainGame).");
        return director;
    }
}
