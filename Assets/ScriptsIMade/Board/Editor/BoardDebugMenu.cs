using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > Game > Debug: try the board meetings while MainGame is playing.
/// - Open Board Meeting Now: opens a meeting right away, whatever the day and the calm (an active target is judged by the
///   real rule; an unanswered meeting just comes back).
/// - Resolve Target (Hit) / (Miss): opens a meeting right away that judges the active target as hit or missed (its start
///   value is moved so the memo's numbers add up). Without a target a Stretch one is made first.
/// After either, the next meeting is 10 days after the day it opened.
/// </summary>
public static class BoardDebugMenu
{
    const string Root = "Tools/Game/Debug/";
    const string OpenItem = Root + "Open Board Meeting Now";
    const string HitItem = Root + "Resolve Target (Hit)";
    const string MissItem = Root + "Resolve Target (Miss)";

    [MenuItem(OpenItem)]
    static void OpenNow()
    {
        BoardMeetings board = Board();
        if (board != null && !board.DebugOpenMeetingNow())
            Debug.LogWarning("[BoardMeetings] The game is still loading - try again in a moment.");
    }

    [MenuItem(HitItem)]
    static void ResolveHit()
    {
        BoardMeetings board = Board();
        if (board != null) board.DebugResolveTarget(true);
    }

    [MenuItem(MissItem)]
    static void ResolveMiss()
    {
        BoardMeetings board = Board();
        if (board != null) board.DebugResolveTarget(false);
    }

    [MenuItem(OpenItem, true)]
    [MenuItem(HitItem, true)]
    [MenuItem(MissItem, true)]
    static bool CanUse()
    {
        return EditorApplication.isPlaying;
    }

    static BoardMeetings Board()
    {
        if (!EditorApplication.isPlaying)
        {
            Debug.LogWarning("[BoardMeetings] Enter Play mode in MainGame first.");
            return null;
        }
        BoardMeetings board = BoardMeetings.Instance != null ? BoardMeetings.Instance : Object.FindAnyObjectByType<BoardMeetings>();
        if (board == null)
            Debug.LogWarning("[BoardMeetings] No board in this scene (it is added to scenes with a CompanyManager, like MainGame).");
        return board;
    }
}
