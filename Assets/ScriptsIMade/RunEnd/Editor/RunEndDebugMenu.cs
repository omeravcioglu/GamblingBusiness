using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > Game > Debug: force the run-end screens while MainGame is playing.
/// - Force Bankruptcy Hearing: drops the company to $0 and opens the hearing right away.
/// - Force Game Over: ends the run (deletes the save, like a real game over).
/// - Force Win ($10B): raises the company to $10B if it is below and shows the win screen.
/// </summary>
public static class RunEndDebugMenu
{
    const string Root = "Tools/Game/Debug/";

    [MenuItem(Root + "Force Bankruptcy Hearing")]
    static void ForceHearing()
    {
        RunEndDirector director = Director();
        if (director != null) director.DebugForceBankruptcy();
    }

    [MenuItem(Root + "Force Game Over (deletes the save)")]
    static void ForceGameOver()
    {
        RunEndDirector director = Director();
        if (director != null) director.GameOver();
    }

    [MenuItem(Root + "Force Win ($10B)")]
    static void ForceWin()
    {
        RunEndDirector director = Director();
        if (director != null) director.DebugForceWin();
    }

    [MenuItem(Root + "Force Bankruptcy Hearing", true)]
    [MenuItem(Root + "Force Game Over (deletes the save)", true)]
    [MenuItem(Root + "Force Win ($10B)", true)]
    static bool CanForce()
    {
        return EditorApplication.isPlaying;
    }

    static RunEndDirector Director()
    {
        if (!EditorApplication.isPlaying)
        {
            Debug.LogWarning("[RunEnd] Enter Play mode in MainGame first.");
            return null;
        }
        RunEndDirector director = RunEndDirector.Instance != null ? RunEndDirector.Instance : Object.FindAnyObjectByType<RunEndDirector>();
        if (director == null)
            Debug.LogWarning("[RunEnd] No RunEndDirector in this scene (it is added to scenes with a CompanyManager, like MainGame).");
        return director;
    }
}
