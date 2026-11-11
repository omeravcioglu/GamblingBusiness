using System;
using System.Globalization;
using System.Net;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.CloudCode;
using Unity.Services.CloudSave;
using Unity.Services.Core;
using UnityEngine;

/// <summary>Where PlayerAccount's sign-in stands.</summary>
public enum PlayerAccountState
{
    /// <summary>Not tried yet, or signed out.</summary>
    NotSignedIn,
    SigningIn,
    SignedIn,
    /// <summary>The last attempt failed (no network, Steam offline...): the game plays offline and retries every few minutes.</summary>
    Offline
}

/// <summary>How the current session was signed in.</summary>
public enum PlayerSignInMethod
{
    None,
    /// <summary>A Steam web API ticket (the release path): the account follows the Steam account to any PC.</summary>
    Steam,
    /// <summary>Anonymous, in the Editor or a development build without Steam (reuses this PC's cached session).</summary>
    Dev,
    /// <summary>Someone else signed the session in first (the Online City's own sign-in) and it was kept.</summary>
    Session
}

/// <summary>
/// One sign-in for the whole game (Unity Authentication), started at boot so the cloud save and the Online City find the
/// session ready:
/// - SignInAsync() (idempotent; every caller shares the attempt in flight): Unity Services start (or finish starting)
///   -> already signed in? done -> Steam running and logged on: a web API ticket for "unityauthenticationservice" ->
///   SignInWithSteamAsync -> the ticket is cancelled -> otherwise, in the Editor or a development build, an anonymous
///   "dev" sign-in (logged as such) -> otherwise (a release build without Steam) no sign-in: local saves only.
/// - Never throws at callers: no network is normal (offline play is allowed). While Offline it tries again every
///   RetrySeconds (a release build without Steam every LocalOnlyRetrySeconds); an expired session signs in again by itself.
/// - Release builds use only Steam sessions: a session someone else signed in (the Online City's anonymous sign-in) is used
///   only when its player has this Steam user's Steam ID linked; otherwise it is ignored and Steam is tried again later,
///   so the cloud save never runs under another player id (CanUseCloudSave). The Editor and development builds accept any
///   session (Method Session).
/// - State + StateChanged, SignedIn (raised each time a session becomes ready), PlayerId, Method, LastError, IsOffline,
///   IsLocalOnly (release build without Steam right now: local saves only).
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("")]
public sealed class PlayerAccount : MonoBehaviour
{
    /// <summary>The identity Steam web API tickets are made for (Unity Authentication's Steam provider).</summary>
    public const string SteamIdentity = "unityauthenticationservice";

    /// <summary>Seconds between sign-in attempts while Offline.</summary>
    public static float RetrySeconds = 180f;
    /// <summary>Seconds between attempts while a release build has no Steam (Steam may be started later).</summary>
    public static float LocalOnlyRetrySeconds = 120f;
    /// <summary>After a failed switch from another sign-in's session to Steam: seconds before the next switch is tried.</summary>
    public static float SessionSwitchRetrySeconds = 600f;

    const float ServicesStartTimeout = 30f;
    const float TicketTimeout = 15f;

    static PlayerAccount runner;
    static Task<bool> signInTask;
    static float lastAttemptEnded = float.NegativeInfinity;
    static float retryOverride = -1f; // one sooner retry (expired session)
    static bool authHooked;
    static float switchBlockedUntil = float.NegativeInfinity; // a Steam sign-in after signing another session out failed: no switch before this

    public static PlayerAccountState State { get; private set; }
    public static PlayerSignInMethod Method { get; private set; }
    /// <summary>The Unity player id of this session (null while not signed in).</summary>
    public static string PlayerId { get; private set; }
    /// <summary>Why the last attempt failed (English, for logs and tools); null after a sign-in.</summary>
    public static string LastError { get; private set; }

    public static bool IsSignedIn => State == PlayerAccountState.SignedIn;
    public static bool IsSigningIn => State == PlayerAccountState.SigningIn;
    public static bool IsOffline => State == PlayerAccountState.Offline;
    /// <summary>A release build without Steam right now: nothing signs in (local saves only; tried again slowly).</summary>
    public static bool IsLocalOnly { get; private set; }
    /// <summary>The Editor and development builds may sign in anonymously when Steam isn't there.</summary>
    public static bool DevSignInAllowed => Application.isEditor || Debug.isDebugBuild;
    /// <summary>
    /// The session may carry the cloud save: a Steam session in release builds, any session in the Editor and development
    /// builds. CloudSaveSync never talks to the server otherwise.
    /// </summary>
    public static bool CanUseCloudSave => IsSignedIn && (DevSignInAllowed || Method == PlayerSignInMethod.Steam);

    /// <summary>State, PlayerId, Method or LastError changed.</summary>
    public static event Action StateChanged;
    /// <summary>A session is ready (after every successful sign-in, including a sign-in again after being offline).</summary>
    public static event Action SignedIn;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        // Entering Play mode without a domain reload keeps statics: start every session clean
        runner = null;
        signInTask = null;
        lastAttemptEnded = float.NegativeInfinity;
        retryOverride = -1f;
        authHooked = false;
        switchBlockedUntil = float.NegativeInfinity;
        State = PlayerAccountState.NotSignedIn;
        Method = PlayerSignInMethod.None;
        PlayerId = null;
        LastError = null;
        IsLocalOnly = false;
        StateChanged = null;
        SignedIn = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        // Demo: no UGS sign-in (no cloud save, no Online City in the demo)
        if (DemoMode.Active)
        {
            Debug.Log("[PlayerAccount] Off in the demo: no sign-in to Unity services (local saves only).");
            return;
        }
        EnsureRunner();
        _ = SignInAsync();
    }

    static void EnsureRunner()
    {
        if (runner != null || !Application.isPlaying) return;
        var go = new GameObject("PlayerAccount");
        go.hideFlags = HideFlags.HideInHierarchy;
        DontDestroyOnLoad(go);
        runner = go.AddComponent<PlayerAccount>();
    }

    void Update()
    {
        // Offline: try again now and then (a release build without Steam too, more slowly: Steam may be started later)
        if (State != PlayerAccountState.Offline) return;
        float wait = retryOverride >= 0f ? retryOverride : IsLocalOnly ? LocalOnlyRetrySeconds : RetrySeconds;
        if (Time.realtimeSinceStartup - lastAttemptEnded < wait) return;
        retryOverride = -1f;
        _ = SignInAsync();
    }

    // ------------------------------------------------------------------
    // Signing in
    // ------------------------------------------------------------------

    /// <summary>
    /// Sign in (or wait for the attempt in flight). True when a session is ready. Never throws: false means offline (see
    /// LastError), and the game carries on with local saves.
    /// </summary>
    public static Task<bool> SignInAsync()
    {
        if (IsSignedIn && SessionReady()) return Task.FromResult(true);
        if (signInTask != null && !signInTask.IsCompleted) return signInTask;
        if (!Application.isPlaying) return Task.FromResult(false);
        if (DemoMode.Active) return Task.FromResult(false);   // Demo: never signs in (see Boot)
        EnsureRunner();
        signInTask = SignInRoutine();
        return signInTask;
    }

    /// <summary>True while Unity Authentication has a live (not expired) session.</summary>
    static bool SessionReady()
    {
        try
        {
            return UnityServices.State == ServicesInitializationState.Initialized && AuthenticationService.Instance.IsAuthorized;
        }
        catch (Exception)
        {
            return false;
        }
    }

    static async Task<bool> SignInRoutine()
    {
        SetState(PlayerAccountState.SigningIn, null);
        bool replacedSession = false;
        try
        {
            if (!await StartServices())
                return Fail("Unity Gaming Services didn't start in time.", false);
            if (!Application.isPlaying) return false;

            HookAuthEvents();
            IAuthenticationService auth = AuthenticationService.Instance;
            if (auth.IsAuthorized)
            {
                // Signed in already: by an earlier attempt of ours, or by someone else (the Online City)
                if (Method == PlayerSignInMethod.Steam || Method == PlayerSignInMethod.Dev)
                {
                    Complete(Method);
                    return true;
                }
                if (DevSignInAllowed)
                {
                    Complete(PlayerSignInMethod.Session);
                    return true;
                }
                // Release: only this Steam user's player may carry the cloud save
                if (await IsThisSteamUsersSession(auth))
                {
                    if (!Application.isPlaying) return false;
                    Complete(PlayerSignInMethod.Steam);
                    return true;
                }
                // Another player's session (the Online City's anonymous sign-in): replaced by a Steam sign-in, but only when
                // Steam is there and the city has nothing on its way (its transfers belong to the session that started them)
                SteamPlatform.Initialize();
                if (!SteamPlatform.IsAvailable)
                    return Fail("Another sign-in (not this Steam user's) holds the session: the cloud save waits for Steam.", false);
                if (Time.realtimeSinceStartup < switchBlockedUntil)
                    return Fail("Another sign-in (not this Steam user's) holds the session; the last switch to Steam failed, the next try is in " +
                                Mathf.CeilToInt((switchBlockedUntil - Time.realtimeSinceStartup) / 60f) + " min.", false);
                if (!OnlineCitySafeToSwitch())
                    return Fail("Another sign-in holds the session and the Online City is busy: switching to Steam later.", false);
                Debug.Log("[PlayerAccount] Signing out another sign-in's session (not this Steam user's player) to sign in with Steam.");
                auth.SignOut();
                replacedSession = true;
            }

            SteamPlatform.Initialize();
            if (SteamPlatform.IsAvailable)
            {
                try
                {
                    await SignInWithSteam(auth);
                    if (!Application.isPlaying) return false;
                    Complete(PlayerSignInMethod.Steam);
                    if (replacedSession) ReconnectOnlineCity();
                    return true;
                }
                catch (Exception) when (replacedSession)
                {
                    // Don't sign the city out again and again for a Steam sign-in that fails: try again after a long pause
                    switchBlockedUntil = Time.realtimeSinceStartup + SessionSwitchRetrySeconds;
                    ReconnectOnlineCity();
                    throw;
                }
                catch (Exception e) when (DevSignInAllowed && !IsNetworkError(e))
                {
                    // A Steam provider that isn't set up yet shouldn't stop development
                    Debug.LogWarning("[PlayerAccount] Steam sign-in failed (" + e.Message + "). Using the dev sign-in instead " +
                                     "(Editor / development builds only).");
                }
            }

            if (DevSignInAllowed)
            {
                await SignInAgain(auth, () => auth.SignInAnonymouslyAsync());
                if (!Application.isPlaying) return false;
                Complete(PlayerSignInMethod.Dev);
                return true;
            }

            // A release build: only Steam signs in
            bool steamOff = !SteamPlatform.IsInitialized;
            return Fail(steamOff ? "Steam isn't running (" + (SteamPlatform.InitError ?? "not started") + ")." : "Steam is in offline mode.", steamOff);
        }
        catch (Exception e)
        {
            return Fail(Describe(e), false);
        }
    }

    static async Task SignInWithSteam(IAuthenticationService auth)
    {
        SteamWebTicket ticket = await SteamPlatform.GetWebApiTicketAsync(SteamIdentity, TicketTimeout);
        if (ticket == null || !ticket.Ok)
            throw new InvalidOperationException("No Steam ticket: " + (ticket != null ? ticket.Error : "no answer"));
        try
        {
            // The app id argument is for additional app ids (Playtest, Demo...); the main app uses the plain overload
            uint appId = SteamPlatform.AppId;
            if (appId != SteamPlatform.DefaultAppId)
            {
                string additional = appId.ToString(CultureInfo.InvariantCulture);
                await SignInAgain(auth, () => auth.SignInWithSteamAsync(ticket.Hex, SteamIdentity, additional));
            }
            else
            {
                await SignInAgain(auth, () => auth.SignInWithSteamAsync(ticket.Hex, SteamIdentity));
            }
        }
        finally
        {
            SteamPlatform.CancelTicket(ticket);
        }
    }

    /// <summary>
    /// True when the signed-in player has the running Steam user's Steam ID linked (a session someone else signed in may
    /// still be this player's: an anonymous sign-in reuses the cached session of the last Steam sign-in). False without
    /// Steam, without a linked Steam identity, or when it can't be read.
    /// </summary>
    static async Task<bool> IsThisSteamUsersSession(IAuthenticationService auth)
    {
        SteamPlatform.Initialize();
        ulong steamId = SteamPlatform.SteamId;
        if (steamId == 0) return false;
        try
        {
            string linked = auth.PlayerInfo != null ? auth.PlayerInfo.GetSteamId() : null;
            if (string.IsNullOrEmpty(linked))
            {
                PlayerInfo info = await auth.GetPlayerInfoAsync();
                linked = info != null ? info.GetSteamId() : null;
            }
            bool mine = !string.IsNullOrEmpty(linked) && linked == steamId.ToString(CultureInfo.InvariantCulture);
            if (!mine)
                Debug.Log("[PlayerAccount] The session someone else signed in isn't this Steam user's player (linked Steam id: " +
                          (string.IsNullOrEmpty(linked) ? "none" : linked) + "): not used for the cloud save.");
            return mine;
        }
        catch (Exception e)
        {
            Debug.Log("[PlayerAccount] Couldn't check the session's Steam identity (" + e.Message + "): not used for the cloud save.");
            return false;
        }
    }

    /// <summary>
    /// The Online City can lose its session now: no transfer on its way (it is finished by the session that started it) and
    /// not in the middle of connecting.
    /// </summary>
    static bool OnlineCitySafeToSwitch()
    {
        CityOnline city = CityOnline.Instance;
        return city == null || (!city.HasPendingTransfers && city.State != CityOnline.Status.Connecting);
    }

    /// <summary>After the session changed player: the Online City reloads its accounts under the new session.</summary>
    static void ReconnectOnlineCity()
    {
        CityOnline city = CityOnline.Instance;
        if (city != null && (city.State == CityOnline.Status.Ready || city.State == CityOnline.Status.Failed))
            _ = city.Reconnect();
    }

    /// <summary>Run a sign-in call; an expired session that refuses a new sign-in is signed out and tried once more.</summary>
    static async Task SignInAgain(IAuthenticationService auth, Func<Task> signIn)
    {
        try
        {
            await signIn();
        }
        catch (AuthenticationException e) when (e.ErrorCode == AuthenticationErrorCodes.ClientInvalidUserState && auth.IsExpired)
        {
            auth.SignOut();
            await signIn();
        }
    }

    static async Task<bool> StartServices()
    {
        if (UnityServices.State == ServicesInitializationState.Initialized) return true;
        if (UnityServices.State == ServicesInitializationState.Uninitialized)
        {
            await UnityServices.InitializeAsync();
        }
        else
        {
            // Someone else (the Online City) is starting them: wait
            float deadline = Time.realtimeSinceStartup + ServicesStartTimeout;
            while (UnityServices.State == ServicesInitializationState.Initializing && Time.realtimeSinceStartup < deadline)
                await Task.Yield();
        }
        return UnityServices.State == ServicesInitializationState.Initialized;
    }

    static void Complete(PlayerSignInMethod method)
    {
        string id = null;
        try { id = AuthenticationService.Instance.PlayerId; }
        catch (Exception) { }

        bool changed = State != PlayerAccountState.SignedIn || PlayerId != id || Method != method;
        PlayerId = id;
        Method = method;
        LastError = null;
        IsLocalOnly = false;
        lastAttemptEnded = Time.realtimeSinceStartup;
        State = PlayerAccountState.SignedIn;
        if (!changed) return;

        switch (method)
        {
            case PlayerSignInMethod.Dev:
                Debug.Log("[PlayerAccount] DEV sign-in (anonymous, no Steam; Editor / development build): player " + id);
                break;
            case PlayerSignInMethod.Steam:
                Debug.Log("[PlayerAccount] Signed in with Steam (" + SteamPlatform.PersonaName + "): player " + id);
                break;
            default:
                Debug.Log("[PlayerAccount] Using the session that was already signed in: player " + id);
                break;
        }
        Raise(StateChanged);
        Raise(SignedIn);
    }

    static bool Fail(string error, bool localOnly)
    {
        LastError = error;
        IsLocalOnly = localOnly;
        lastAttemptEnded = Time.realtimeSinceStartup;
        State = PlayerAccountState.Offline;
        PlayerId = null;
        Method = PlayerSignInMethod.None;
        Debug.Log("[PlayerAccount] Not signed in: " + error + (localOnly ? " Saving on this PC only." : " Playing offline; trying again later."));
        Raise(StateChanged);
        return false;
    }

    static void SetState(PlayerAccountState state, string error)
    {
        if (State == state && LastError == error) return;
        State = state;
        LastError = error;
        Raise(StateChanged);
    }

    // ------------------------------------------------------------------
    // Session events
    // ------------------------------------------------------------------

    static void HookAuthEvents()
    {
        if (authHooked) return;
        authHooked = true;
        IAuthenticationService auth = AuthenticationService.Instance;
        // -= first: a Play mode without a domain reload keeps the service (and its handlers)
        auth.SignedIn -= OnAuthSignedIn;
        auth.SignedIn += OnAuthSignedIn;
        auth.SignedOut -= OnAuthSignedOut;
        auth.SignedOut += OnAuthSignedOut;
        auth.Expired -= OnAuthExpired;
        auth.Expired += OnAuthExpired;
    }

    static void OnAuthSignedIn()
    {
        // Our own sign-in: SignInRoutine finishes it. Anyone else's (the Online City): the Editor and development builds use
        // that session; release builds use it only when it is this Steam user's player (the routine checks, else waits)
        if (State == PlayerAccountState.SigningIn || State == PlayerAccountState.SignedIn) return;
        if (DevSignInAllowed)
            Complete(PlayerSignInMethod.Session);
        else
            _ = SignInAsync();
    }

    static void OnAuthSignedOut()
    {
        if (State == PlayerAccountState.SigningIn) return;
        PlayerId = null;
        Method = PlayerSignInMethod.None;
        // Signed out from elsewhere: sign in again soon (the Update loop does it)
        LastError = "Signed out.";
        lastAttemptEnded = Time.realtimeSinceStartup;
        retryOverride = 5f;
        State = PlayerAccountState.Offline;
        Raise(StateChanged);
    }

    static void OnAuthExpired()
    {
        if (State == PlayerAccountState.SigningIn) return;
        LastError = "The session expired.";
        lastAttemptEnded = Time.realtimeSinceStartup;
        retryOverride = 5f; // sign in again soon (the Update loop does it)
        State = PlayerAccountState.Offline;
        Debug.Log("[PlayerAccount] The session expired: signing in again shortly.");
        Raise(StateChanged);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    /// <summary>True for errors that only mean "no connection right now" (worth waiting out, not working around).</summary>
    public static bool IsNetworkError(Exception e)
    {
        for (Exception inner = e; inner != null; inner = inner.InnerException)
        {
            switch (inner)
            {
                case CloudCodeException cc when cc.Reason == CloudCodeExceptionReason.NoInternetConnection
                                             || cc.Reason == CloudCodeExceptionReason.ServiceUnavailable:
                    return true;
                case CloudSaveException cs when cs.Reason == CloudSaveExceptionReason.NoInternetConnection:
                    return true;
                case RequestFailedException rf when rf.ErrorCode == CommonErrorCodes.TransportError
                                                 || rf.ErrorCode == CommonErrorCodes.Timeout
                                                 || rf.ErrorCode == CommonErrorCodes.ServiceUnavailable:
                    return true;
                case WebException _:
                case TimeoutException _:
                case TaskCanceledException _:
                    return true;
            }
        }
        return false;
    }

    /// <summary>A short English reason for logs and tools.</summary>
    public static string Describe(Exception e)
    {
        if (e == null) return "unknown error";
        if (IsNetworkError(e)) return "No connection (" + e.Message + ").";
        if (e is ServicesInitializationException) return "Unity Gaming Services couldn't start (" + e.Message + "). Is the project linked?";
        return e.GetType().Name + ": " + e.Message;
    }

    static void Raise(Action handlers)
    {
        if (handlers == null) return;
        foreach (Action handler in handlers.GetInvocationList())
        {
            try { handler(); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }

    /// <summary>One line for logs and tools.</summary>
    public static string DescribeState()
    {
        switch (State)
        {
            case PlayerAccountState.SignedIn: return "Account: signed in (" + Method + "), player " + PlayerId;
            case PlayerAccountState.SigningIn: return "Account: signing in...";
            case PlayerAccountState.Offline: return "Account: " + (IsLocalOnly ? "local only" : "offline") + " (" + LastError + ")";
            default: return "Account: not signed in";
        }
    }
}
