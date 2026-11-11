using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > Game > Debug > Open Casino: open the company casino while MainGame is playing, without the Casino License
/// (the casino is created first if the scene has none).
/// </summary>
public static class CasinoDebugMenu
{
    const string Item = "Tools/Game/Debug/Open Casino";

    [MenuItem(Item)]
    static void OpenCasino()
    {
        if (!EditorApplication.isPlaying)
        {
            Debug.LogWarning("[Casino] Enter Play mode in MainGame first.");
            return;
        }
        CasinoManager casino = CasinoBootstrap.EnsureCasino(true);
        if (casino == null)
        {
            Debug.LogWarning("[Casino] Couldn't find or create the casino.");
            return;
        }
        casino.DebugOpenCasino();
    }

    [MenuItem(Item, true)]
    static bool CanOpen()
    {
        return EditorApplication.isPlaying;
    }
}
