// Steam is compiled in only when Steamworks.NET is installed (Game.Steam.asmdef's version defines set STEAMWORKS_NET and
// GAME_STEAMWORKS_PACKAGE) on a platform it supports (Steamworks.NET's own list, Android left out). GAME_STEAMWORKS_PACKAGE
// comes only from the package: Steamworks.NET also writes STEAMWORKS_NET into Player Settings, and that global define
// stays behind if the package is ever removed.
#if STEAMWORKS_NET && GAME_STEAMWORKS_PACKAGE && (UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX || STEAMWORKS_WIN || STEAMWORKS_LIN_OSX)
#define GAME_STEAM
#endif

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
#if GAME_STEAM
using Steamworks;
#endif

/// <summary>An auth ticket for a web API (SteamPlatform.GetWebApiTicketAsync).</summary>
public sealed class SteamWebTicket
{
    /// <summary>The ticket as hex text: what Unity Authentication's SignInWithSteamAsync takes. Null when there is none.</summary>
    public string Hex;
    /// <summary>The ticket's handle, for SteamPlatform.CancelTicket once it was used (0 = none).</summary>
    public uint Handle;
    /// <summary>Why there is no ticket (English, for logs).</summary>
    public string Error;

    public bool Ok => !string.IsNullOrEmpty(Hex);
}

/// <summary>
/// The game's only door to Steam (Steamworks.NET). Safe to call in any build:
/// - Initialize() runs at boot (BeforeSceneLoad) and is idempotent. In a release player that Steam didn't start, Steam
///   starts the game again through its client (RestartAppIfNecessary) and this copy quits. Then SteamAPI.InitEx; a hidden
///   DontDestroyOnLoad object (SteamCallbackRunner) runs SteamAPI.RunCallbacks every frame and SteamAPI.Shutdown on quit.
///   A failed start is retried at most every InitRetrySeconds, on the next Initialize call.
/// - IsInitialized, IsAvailable (initialized and logged on to Steam), InitError, SteamId, PersonaName, AppId.
/// - GetWebApiTicketAsync(identity, timeout): an auth ticket for a web API (PlayerAccount signs in to Unity Authentication
///   with identity "unityauthenticationservice"); CancelTicket once the ticket was used.
/// Without Steamworks.NET (or on a platform it doesn't support) everything here is a stub: never initialized, no tickets.
/// Development builds started outside Steam need steam_appid.txt next to the executable (the build step copies it).
/// </summary>
public static class SteamPlatform
{
    /// <summary>The game's Steam App ID (steam_appid.txt in the project root holds the same number).</summary>
    public const uint DefaultAppId = 4427670;
    // Demo: the demo's own app (DemoConfig.steamAppId in Assembly-CSharp defaults to the same number)
    /// <summary>The Steam demo's App ID: what a demo build runs as when DemoMode.json doesn't say otherwise.</summary>
    public const uint DemoAppId = 5532550;
    /// <summary>A failed start (Steam not running...) is tried again at most this often.</summary>
    public const float InitRetrySeconds = 30f;

    static float lastInitAttempt = float.NegativeInfinity;
    static SteamCallbackRunner runner;
    static int demoBuild = -1;   // Demo: 1 = Resources/DemoMode.json is in the build, 0 = it isn't, -1 = not read yet

#if GAME_STEAM
    static bool restartChecked;
    static Callback<GetTicketForWebApiResponse_t> ticketCallback;
    static readonly Dictionary<uint, TaskCompletionSource<SteamWebTicket>> pendingTickets =
        new Dictionary<uint, TaskCompletionSource<SteamWebTicket>>();
#endif

    /// <summary>True when this build has Steamworks.NET for this platform (otherwise every call here is a stub).</summary>
    public static bool IsCompiledIn
    {
        get
        {
#if GAME_STEAM
            return true;
#else
            return false;
#endif
        }
    }

    /// <summary>
    /// Demo: Resources/DemoMode.json is in this build, so Steam runs it as the demo's app (no stats or achievements there:
    /// SteamAchievements stays idle).
    /// </summary>
    internal static bool IsDemoBuild
    {
        get
        {
            if (demoBuild < 0) ReadDemoAppId(out _);
            return demoBuild == 1;
        }
    }

    /// <summary>SteamAPI started (the Steam client runs and knows this game).</summary>
    public static bool IsInitialized { get; private set; }

    /// <summary>Steam was asked to start the game again through its client: this copy is quitting.</summary>
    public static bool IsRestarting { get; private set; }

    /// <summary>Why Steam isn't initialized (English, for logs and tools); null once it is.</summary>
    public static string InitError { get; private set; }

    /// <summary>The user is logged on to Steam (false in Steam's offline mode or when Steam isn't initialized).</summary>
    public static bool IsLoggedOn
    {
        get
        {
            if (!IsInitialized) return false;
#if GAME_STEAM
            try { return SteamUser.BLoggedOn(); }
            catch (Exception) { return false; }
#else
            return false;
#endif
        }
    }

    /// <summary>Steam can be used now: initialized and logged on (a web API ticket can be had).</summary>
    public static bool IsAvailable => IsInitialized && IsLoggedOn;

    /// <summary>The user's 64-bit Steam ID (0 when Steam isn't initialized).</summary>
    public static ulong SteamId
    {
        get
        {
            if (!IsInitialized) return 0;
#if GAME_STEAM
            try { return SteamUser.GetSteamID().m_SteamID; }
            catch (Exception) { return 0; }
#else
            return 0;
#endif
        }
    }

    /// <summary>The user's Steam name ("" when Steam isn't initialized).</summary>
    public static string PersonaName
    {
        get
        {
            if (!IsInitialized) return "";
#if GAME_STEAM
            try { return SteamFriends.GetPersonaName() ?? ""; }
            catch (Exception) { return ""; }
#else
            return "";
#endif
        }
    }

    /// <summary>The App ID Steam runs this game as (a playtest or demo has its own), else DefaultAppId.</summary>
    public static uint AppId
    {
        get
        {
            if (!IsInitialized) return DefaultAppId;
#if GAME_STEAM
            try
            {
                uint id = SteamUtils.GetAppID().m_AppId;
                return id != 0 ? id : DefaultAppId;
            }
            catch (Exception) { return DefaultAppId; }
#else
            return DefaultAppId;
#endif
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        // Entering Play mode without a domain reload keeps statics (Steam itself was shut down when Play mode ended)
        IsInitialized = false;
        IsRestarting = false;
        InitError = null;
        lastInitAttempt = float.NegativeInfinity;
        runner = null;
        demoBuild = -1;   // Demo: the switch file may have been added or removed since
#if GAME_STEAM
        restartChecked = false;
        ticketCallback = null;
        pendingTickets.Clear();
#endif
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        Initialize();
    }

    /// <summary>
    /// Start Steam (idempotent; true once it runs). Playing without Steam is allowed: a failure is logged, not thrown, and
    /// InitError says why. In a player that Steam didn't start this asks Steam to start the game again and quits.
    /// </summary>
    public static bool Initialize()
    {
        if (IsInitialized) return true;
        if (IsRestarting) return false;
        if (!Application.isPlaying)
        {
            InitError = "Steam starts in Play mode.";
            return false;
        }
        if (Time.realtimeSinceStartup - lastInitAttempt < InitRetrySeconds) return false;
        lastInitAttempt = Time.realtimeSinceStartup;

#if GAME_STEAM
        if (!restartChecked)
        {
            restartChecked = true;
            try
            {
                // Release players must run through Steam. Development builds never restart (one without steam_appid.txt
                // next to it would quit at once), nor do playtest builds (PlaytestMode's switch file, read here by name:
                // this assembly can't see Assembly-CSharp); the Editor always runs as the app in the project's steam_appid.txt.
                bool playtest = Resources.Load<TextAsset>("PlaytestMode") != null;
                // Demo: a demo build (DemoMode's switch file, read by name) restarts as the demo's own app, never as the
                // full game; it wins over playtest mode. Id 0 (the demo app isn't created yet) skips the restart.
                long demoAppId = ReadDemoAppId(out bool demo);
                uint restartAppId = DefaultAppId;
                if (demo)
                {
                    playtest = false;
                    if (demoAppId > 0 && demoAppId <= uint.MaxValue && demoAppId != DefaultAppId) restartAppId = (uint)demoAppId;
                    else
                    {
                        restartAppId = 0;
                        if (!Application.isEditor && !Debug.isDebugBuild)
                            Debug.LogWarning("[Steam] Demo build without a demo app id (DemoMode.json steamAppId): no restart through Steam.");
                    }
                }
                if (!Application.isEditor && !Debug.isDebugBuild && !playtest && restartAppId != 0 &&
                    SteamAPI.RestartAppIfNecessary(new AppId_t(restartAppId)))
                {
                    IsRestarting = true;
                    InitError = "The game wasn't started by Steam: Steam starts it again.";
                    Debug.Log("[Steam] " + InitError + " This copy quits.");
                    Application.Quit();
                    return false;
                }
            }
            catch (DllNotFoundException e)
            {
                InitError = "The steam_api library is missing (" + e.Message + ").";
                Debug.LogError("[Steam] " + InitError + " Playing without Steam.");
                return false;
            }
            catch (Exception e)
            {
                InitError = "RestartAppIfNecessary failed (" + e.Message + ").";
                Debug.LogWarning("[Steam] " + InitError);
            }
        }

        try
        {
            if (!Packsize.Test())
            {
                InitError = "Steamworks.NET was built with the wrong packing for this platform (Packsize).";
                Debug.LogError("[Steam] " + InitError);
                return false;
            }
            if (!DllCheck.Test())
            {
                InitError = "The steam_api library doesn't match this Steamworks.NET (DllCheck).";
                Debug.LogError("[Steam] " + InitError);
                return false;
            }

            ESteamAPIInitResult result = SteamAPI.InitEx(out string message);
            if (result != ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
            {
                InitError = DescribeInitResult(result) + (string.IsNullOrEmpty(message) ? "" : " (" + message + ")");
                // Not a warning: the game runs without Steam (offline, local saves only)
                Debug.Log("[Steam] Not available: " + InitError);
                return false;
            }
        }
        catch (DllNotFoundException e)
        {
            InitError = "The steam_api library is missing (" + e.Message + ").";
            Debug.LogError("[Steam] " + InitError + " Playing without Steam.");
            return false;
        }
        catch (Exception e)
        {
            InitError = "SteamAPI.InitEx failed (" + e.Message + ").";
            Debug.LogWarning("[Steam] " + InitError);
            return false;
        }

        IsInitialized = true;
        InitError = null;
        CreateRunner();
        Debug.Log("[Steam] Ready: " + PersonaName + " (" + SteamId + "), app " + AppId + (IsLoggedOn ? "" : ", offline mode"));
        return true;
#else
        InitError = "Steamworks.NET isn't in this build.";
        return false;
#endif
    }

    /// <summary>Stop Steam (the runner calls this on quit). Pending ticket requests fail.</summary>
    public static void Shutdown()
    {
        if (!IsInitialized) return;
        IsInitialized = false;
#if GAME_STEAM
        FailPendingTickets("Steam shut down.");
        try { ticketCallback?.Dispose(); }
        catch (Exception) { }
        ticketCallback = null;
        try { SteamAPI.Shutdown(); }
        catch (Exception e) { Debug.LogWarning("[Steam] Shutdown failed: " + e.Message); }
#endif
        InitError = "Steam was shut down.";
    }

    /// <summary>Steam's callbacks (the runner calls this every frame).</summary>
    internal static void RunCallbacks()
    {
        if (!IsInitialized) return;
#if GAME_STEAM
        try { SteamAPI.RunCallbacks(); }
        catch (Exception e) { Debug.LogException(e); }
#endif
    }

    // ------------------------------------------------------------------
    // Web API tickets
    // ------------------------------------------------------------------

    /// <summary>
    /// An auth ticket for the web API <paramref name="identity"/> (Unity Authentication: "unityauthenticationservice").
    /// Never throws: a ticket without Hex has Error set (Steam unavailable, refused, or no answer within the timeout).
    /// Call CancelTicket once the ticket was used.
    /// </summary>
    public static Task<SteamWebTicket> GetWebApiTicketAsync(string identity, float timeoutSeconds)
    {
        if (!IsAvailable)
            return Task.FromResult(new SteamWebTicket { Error = IsInitialized ? "Steam is in offline mode." : (InitError ?? "Steam isn't running.") });
#if GAME_STEAM
        return RequestTicket(identity, timeoutSeconds);
#else
        return Task.FromResult(new SteamWebTicket { Error = "Steamworks.NET isn't in this build." });
#endif
    }

    /// <summary>Tell Steam a ticket is no longer needed (safe with null or an already cancelled ticket).</summary>
    public static void CancelTicket(SteamWebTicket ticket)
    {
        if (ticket == null || ticket.Handle == 0) return;
        CancelHandle(ticket.Handle);
        ticket.Handle = 0;
    }

    static void CancelHandle(uint handle)
    {
        if (handle == 0 || !IsInitialized) return;
#if GAME_STEAM
        try { SteamUser.CancelAuthTicket(new HAuthTicket(handle)); }
        catch (Exception e) { Debug.LogWarning("[Steam] CancelAuthTicket failed: " + e.Message); }
#endif
    }

#if GAME_STEAM
    static async Task<SteamWebTicket> RequestTicket(string identity, float timeoutSeconds)
    {
        // One callback for every request: answers are matched by handle
        if (ticketCallback == null)
            ticketCallback = Callback<GetTicketForWebApiResponse_t>.Create(OnTicketResponse);

        HAuthTicket handle;
        try { handle = SteamUser.GetAuthTicketForWebApi(identity); }
        catch (Exception e) { return new SteamWebTicket { Error = "GetAuthTicketForWebApi failed (" + e.Message + ")." }; }
        if (handle == HAuthTicket.Invalid)
            return new SteamWebTicket { Error = "Steam gave no ticket." };

        // The answer arrives in a later RunCallbacks (next frame at the earliest), after this is registered
        var pending = new TaskCompletionSource<SteamWebTicket>(TaskCreationOptions.RunContinuationsAsynchronously);
        pendingTickets[handle.m_HAuthTicket] = pending;

        float seconds = Mathf.Max(1f, timeoutSeconds);
        Task first = await Task.WhenAny(pending.Task, Task.Delay(TimeSpan.FromSeconds(seconds)));
        if (first == pending.Task)
            return await pending.Task;

        pendingTickets.Remove(handle.m_HAuthTicket);
        CancelHandle(handle.m_HAuthTicket);
        return new SteamWebTicket { Error = "Steam didn't send the ticket within " + seconds.ToString("0") + " s." };
    }

    static void OnTicketResponse(GetTicketForWebApiResponse_t response)
    {
        uint handle = response.m_hAuthTicket.m_HAuthTicket;
        if (!pendingTickets.TryGetValue(handle, out TaskCompletionSource<SteamWebTicket> pending))
            return; // not ours, or it already timed out
        pendingTickets.Remove(handle);

        if (response.m_eResult != EResult.k_EResultOK)
        {
            CancelHandle(handle);
            pending.TrySetResult(new SteamWebTicket { Error = "Steam refused the ticket (" + response.m_eResult + ")." });
            return;
        }

        byte[] bytes = response.m_rgubTicket;
        int length = bytes != null ? Math.Min(Math.Max(0, response.m_cubTicket), bytes.Length) : 0;
        if (length <= 0)
        {
            CancelHandle(handle);
            pending.TrySetResult(new SteamWebTicket { Error = "Steam sent an empty ticket." });
            return;
        }
        pending.TrySetResult(new SteamWebTicket { Hex = ToHex(bytes, length), Handle = handle });
    }

    static void FailPendingTickets(string reason)
    {
        if (pendingTickets.Count == 0) return;
        var waiting = new List<TaskCompletionSource<SteamWebTicket>>(pendingTickets.Values);
        pendingTickets.Clear();
        foreach (TaskCompletionSource<SteamWebTicket> pending in waiting)
            pending.TrySetResult(new SteamWebTicket { Error = reason });
    }

    static string DescribeInitResult(ESteamAPIInitResult result)
    {
        switch (result)
        {
            case ESteamAPIInitResult.k_ESteamAPIInitResult_NoSteamClient: return "Steam isn't running.";
            case ESteamAPIInitResult.k_ESteamAPIInitResult_VersionMismatch: return "The Steam client is out of date.";
            default: return "Steam couldn't start (" + result + ").";
        }
    }
#endif

    /// <summary>The first <paramref name="length"/> bytes as upper-case hex.</summary>
    static string ToHex(byte[] bytes, int length)
    {
        const string digits = "0123456789ABCDEF";
        var chars = new char[length * 2];
        for (int i = 0; i < length; i++)
        {
            chars[2 * i] = digits[bytes[i] >> 4];
            chars[2 * i + 1] = digits[bytes[i] & 0xF];
        }
        return new string(chars);
    }

    static void CreateRunner()
    {
        if (runner != null) return;
        var go = new GameObject("SteamPlatform");
        go.hideFlags = HideFlags.HideInHierarchy;
        UnityEngine.Object.DontDestroyOnLoad(go);
        runner = go.AddComponent<SteamCallbackRunner>();
    }

    // Demo: the Steam store page (the demo's Wishlist buttons)

    /// <summary>
    /// Open <paramref name="appId"/>'s store page in the Steam overlay. False when Steam or its overlay isn't available
    /// (the Editor, Steam not running, overlay off): the caller opens the store in the browser instead.
    /// </summary>
    public static bool OpenStorePage(uint appId)
    {
        if (appId == 0 || !IsInitialized) return false;
#if GAME_STEAM
        try
        {
            if (!SteamUtils.IsOverlayEnabled()) return false;
            SteamFriends.ActivateGameOverlayToStore(new AppId_t(appId), EOverlayToStoreFlag.k_EOverlayToStoreFlag_None);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Steam] ActivateGameOverlayToStore failed: " + e.Message);
            return false;
        }
#else
        return false;
#endif
    }

    // Demo: DemoMode's switch file, read by name (this assembly can't see DemoMode); only its Steam app id matters here
    [Serializable]
    class DemoSwitch
    {
        // Demo: a file without the field keeps DemoConfig's default (the demo app), not 0 (= no relaunch)
        public long steamAppId = DemoAppId;
    }

    /// <summary>The demo app id from Resources/DemoMode.json (0 = no relaunch; DemoAppId when the file doesn't say, as
    /// DemoMode reads it); <paramref name="present"/> is false without the file.</summary>
    static long ReadDemoAppId(out bool present)
    {
        present = false;
        var file = Resources.Load<TextAsset>("DemoMode");
        demoBuild = file != null ? 1 : 0;   // Demo
        if (file == null) return 0;
        present = true;
        long id = DemoAppId;   // Demo: an unreadable file means DemoConfig's defaults in DemoMode too
        try { id = JsonUtility.FromJson<DemoSwitch>(file.text)?.steamAppId ?? DemoAppId; }
        catch (Exception e) { Debug.LogWarning("[Steam] Couldn't read Resources/DemoMode.json (" + e.Message + ")."); }
        Resources.UnloadAsset(file);
        return id;
    }

    /// <summary>One line for logs and tools: "Steam: ready, Name (7656...), app 4427670" or why it isn't.</summary>
    public static string Describe()
    {
        if (!IsCompiledIn) return "Steam: not in this build (Steamworks.NET missing, or a platform it doesn't support).";
        if (IsRestarting) return "Steam: restarting the game through the Steam client.";
        if (!IsInitialized) return "Steam: not running (" + (InitError ?? "not started yet") + ").";
        return "Steam: ready, " + PersonaName + " (" + SteamId + "), app " + AppId + (IsLoggedOn ? "" : ", OFFLINE MODE (no tickets)");
    }
}
