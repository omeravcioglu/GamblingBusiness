using UnityEngine;

/// <summary>
/// SteamPlatform's hidden helper (SteamPlatform.Initialize creates it on a DontDestroyOnLoad object): runs Steam's callbacks
/// every frame and shuts Steam down when the game quits (or Play mode ends in the Editor).
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("")]
public sealed class SteamCallbackRunner : MonoBehaviour
{
    void Update()
    {
        SteamPlatform.RunCallbacks();
    }

    void OnApplicationQuit()
    {
        SteamPlatform.Shutdown();
    }

    void OnDestroy()
    {
        SteamPlatform.Shutdown();
    }
}
