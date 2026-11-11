using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > Game > Debug: try the status ladder in Play mode (MainGame).
/// - Show Promotion Memo (next tier): promotes to the next tier now (TierChanged, the toast and the memo, without
///   waiting for a calm moment). At the top tier it shows the top tier's memo again.
/// - Reset Status Tier: forgets the highest tier reached and goes back to the tier the current company value has
///   reached, without a memo.
/// </summary>
public static class StatusDebugMenu
{
    const string ShowMemoMenu = "Tools/Game/Debug/Show Promotion Memo (next tier)";
    const string ResetMenu = "Tools/Game/Debug/Reset Status Tier";

    [MenuItem(ShowMemoMenu, false, 100)]
    static void ShowMemo()
    {
        StatusLadder ladder = FindLadder();
        if (ladder != null && !ladder.DebugShowNextTierMemo())
            Debug.LogWarning("[StatusLadder] The game is still loading - try again in a moment.");
    }

    [MenuItem(ShowMemoMenu, true)]
    static bool ShowMemoValidate() => Application.isPlaying;

    [MenuItem(ResetMenu, false, 101)]
    static void ResetTier()
    {
        StatusLadder ladder = FindLadder();
        if (ladder != null)
            ladder.ResetToValue();
    }

    [MenuItem(ResetMenu, true)]
    static bool ResetTierValidate() => Application.isPlaying;

    static StatusLadder FindLadder()
    {
        StatusLadder ladder = StatusLadder.Ensure();
        if (ladder == null)
            Debug.LogWarning("[StatusLadder] There is no company in this scene: enter Play mode in MainGame first.");
        return ladder;
    }
}
