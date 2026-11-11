using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Where the cloud save stands, for a status line (CloudSaveSync.Status / StatusText).</summary>
public enum CloudSyncStatus
{
    /// <summary>Nothing to report (not started yet).</summary>
    Idle,
    SigningIn,
    /// <summary>Comparing with, uploading to or downloading from the server.</summary>
    Syncing,
    /// <summary>Local changes waiting for their upload (normal between uploads).</summary>
    Pending,
    /// <summary>The server has the latest save.</summary>
    Synced,
    /// <summary>The server can't be reached: saving on this PC, uploading later.</summary>
    Offline,
    /// <summary>A release build without Steam: saving on this PC only (Steam is tried again now and then).</summary>
    LocalOnly,
    /// <summary>The save grew past what the server takes: it keeps saving on this PC, uploads are off this session.</summary>
    TooBig
}

/// <summary>
/// The cloud save's client (persistent, created at boot). The save lives on the server, tied to the player's account;
/// only the GameSave Cloud Code module writes it, after SaveRules checked it. The local file stays the game's working
/// copy and is used offline.
/// - Local save chain: SaveData.runId (the company), seq (+1 on every local write, SaveSystem calls StampLocalWrite) and
///   ownerId (the player it belongs to). CloudSyncState (cloudsync.json) remembers baseSeq (the server seq this chain
///   continues from), pendingNewRun / pendingEndRun, the ledger index the server counted and the last accept.
/// - Reconcile (after every sign-in - so at startup and on reconnect -, after a failure, on request): read save_meta and
///   save_data directly, then:
///     another player's local save            -> set aside as savegame_&lt;ownerId&gt;.json (this player's comes back)
///     pending EndRun                          -> EndRun first
///     pending NewRun (a company started here) -> NewRun (unless the server has it already)
///     no local save                           -> download the server's save if it has one ("restored")
///     local save, server has none             -> submit (the server imports a run it never saw)
///     same run, server run ended              -> the local save goes (Game Over happened elsewhere)
///     same run, local ahead on the same chain -> submit
///     same run, same seq on the same chain    -> in step
///     same run, server ahead or branched      -> adopt the server's save
///     different runs                          -> adopt the server's save (or close the local one if it has none)
/// - During play: each local write marks an upload due 120 s after the last upload (explicit, pause and quit saves: right
///   away, at least 15 s apart). Accepted -> baseSeq = seq, accepted ledger entries pruned. stale / no_run / ended ->
///   the server's copy is adopted; time / invalid -> the last accepted copy is adopted once per run and session (a second
///   refusal stops uploads instead, so a client/server mismatch can't keep throwing progress away). Adopting while a
///   company is running goes back to the main menu with a toast (a running company is never swapped). busy -> retry in a
///   few seconds; network or server errors -> keep playing, retry with backoff (30 s .. 5 min), status Offline.
/// - Quit (players, not the Editor): Application.wantsToQuit holds the quit up to 4 s for a final save and upload.
/// - New Game -> BeginNewRun (new run id; NewRun at the next sync). Game Over -> EndRun. SceneFlow.Load into the game
///   waits up to 10 s for a sync in progress (DeferGameLoad). LocalSaveChanged fires when the sync replaced, closed or
///   restored the local save (the main menu refreshes its Continue).
/// - Online City money: a loaded save brings its own city state only (its own account's transfers; nothing from the copy
///   it replaces). Before the server's save replaces the local one (or the local save moves to its owner) with a company
///   loaded, transfers that can still settle get up to CityTransferHoldSeconds; closing the local save doesn't wait (a
///   transfer settling then would pay into a company that is being closed). The city money that moved since the server's
///   save was accepted goes into it before it is written, in one write, by the server's own rule (SettleCityMoney: the
///   acct_* totals against its meta, and its pending transfers classified by SaveRules.ClassifyPending). When the accounts can't be read the local save stays and it is tried again; after CityReadAttempts
///   failures for the same server save it is loaded without (logged as an error).
/// - Shared PC: another Steam user's (or player's) sync state and save are set aside together with their company
///   PlayerPrefs (another Steam user's by Steam ID: cloudsync_steam_&lt;id&gt;.json, savegame_steam_&lt;id&gt;.json) and come
///   back when that user plays here again.
/// - Offline play always works: nothing here blocks the game except the two short waits above.
/// - Editor: Tools > Game > Cloud Save (status, upload / reconcile now, the local test server that runs SaveRules
///   in-process against files in persistentDataPath/cloudtest/).
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("")]
public sealed class CloudSaveSync : MonoBehaviour
{
    /// <summary>The String Table with the cloud save's texts (Assets/Localization/Source/Cloud.csv).</summary>
    public const string Table = "Cloud";
    /// <summary>Seconds between uploads of ordinary (autosave) writes.</summary>
    public const float AutoUploadInterval = 120f;
    /// <summary>Explicit, pause and quit saves upload right away, but never closer together than this.</summary>
    public const float MinUploadGap = 15f;
    /// <summary>How long a quit waits for the final upload.</summary>
    public const float QuitHoldSeconds = 4f;
    /// <summary>How long loading the game waits for a sync in progress.</summary>
    public const float GameLoadWaitSeconds = 10f;

    /// <summary>Uploads wait at most this long for an Online City transfer on its way (then they go anyway).</summary>
    public const float CityTransferHoldSeconds = 60f;
    /// <summary>Copies kept per kind of local save the sync replaced or closed (savegame_replaced_*, _discarded_*, _offline_*).</summary>
    public const int KeptCopies = 3;
    /// <summary>Reads of the Online City accounts that may fail for one server save before it is loaded without its city money.</summary>
    public const int CityReadAttempts = 3;

    const float ToastSettleSeconds = 0.75f;
    const int MaxBusyRetries = 5;
    const int RejectionsBeforeStopping = 2;
    const string CompanyNameKey = "CompanyName";
    const string HasSetCompanyNameKey = "HasSetCompanyName";
    const string DefaultCompanyName = "New Company";

    const string ToastOffline = "cloud.toast.offline";
    const string ToastSteamOff = "cloud.toast.steam_off";
    const string ToastNewerElsewhere = "cloud.toast.newer_elsewhere";
    const string ToastNewRunElsewhere = "cloud.toast.new_run_elsewhere";
    const string ToastEndedElsewhere = "cloud.toast.ended_elsewhere";
    const string ToastRejected = "cloud.toast.rejected";
    const string ToastRestored = "cloud.toast.restored";
    const string ToastOtherAccount = "cloud.toast.other_account";
    const string ToastSuperseded = "cloud.toast.superseded";
    const string ToastBankruptcyStale = "cloud.toast.bankruptcy_stale";
    const string ToastNewGameBlocked = "cloud.toast.new_game_blocked";
    /// <summary>CityOnline.SaveKey: a save's city state is read without the city.</summary>
    const string CitySaveKey = "onlineCity";
    /// <summary>
    /// The company's PlayerPrefs: the keys MainMenuUI.CompanyPrefsKeys clears for New Game. They follow the local save when
    /// it changes owner on a shared PC (kept with the owner's set-aside sync state).
    /// </summary>
    static readonly string[] CompanyPrefKeys =
    {
        "CompanyName", "HasSetCompanyName", "BillionNotificationDismissed", "ChosenMapId", "ChosenMapName", "HasUnlockedOnlineWorld"
    };

    public static CloudSaveSync Instance { get; private set; }

    /// <summary>The sync replaced, closed, set aside or restored the local save file (refresh anything that shows it).</summary>
    public static event Action LocalSaveChanged;
    /// <summary>Status changed (checked every frame).</summary>
    public static event Action StatusChanged;

    CloudSyncState state = new CloudSyncState();
    ICloudSaveBackend backend;
    CloudLedger ledger;
    int generation;                 // bumped by New Game / Game Over / a backend switch: requests in flight are dropped
    Task currentOp;
    string opName;
    bool needsReconcile = true;
    bool dirty;                     // local writes not uploaded yet
    float nextUploadAt;
    float lastUploadStartedAt = -1000f;
    float retryAt;
    int failures;
    int busyRetries;
    bool serverTrouble;             // the last request failed (network or server error)
    string lastError;
    int rejections;                 // time / invalid refusals of rejectedRunId this session
    string rejectedRunId;
    bool uploadsBlocked;            // refused too often, or too big: no more uploads (or adopts) this session
    bool saveTooBig;                // uploadsBlocked because the save is over the server's size limits
    float cityHoldSince = -1f;      // since when uploads wait for an Online City transfer (-1 = none on its way)
    string cityReadFailedFor;       // the server save (run, seq) whose Online City accounts couldn't be read, and how often
    int cityReadFailures;
    SaveMeta lastServerMeta;
    bool quitHoldStarted;
    bool quitReady;
    bool waitingToLoadGame;
    bool resumeSavingOnNextScene;
    bool sceneChangePending;
    float lastSceneLoadedAt;
    readonly List<KeyValuePair<string, NotificationKind>> pendingToasts = new List<KeyValuePair<string, NotificationKind>>();
    bool offlineToastShown;
    bool steamToastShown;
    static string localSavePath;    // SaveSystem.LocalSaveFilePath, read on the main thread (files are read on workers)
    string lastLocalRunId;          // highest seq written this session, per run (keeps seq growing whatever the caller passes)
    long lastLocalSeq;
    CloudSyncStatus lastStatus = (CloudSyncStatus)(-1);

    // ------------------------------------------------------------------
    // Public state
    // ------------------------------------------------------------------

    public static CloudSyncStatus Status
    {
        get
        {
            if (Instance != null) return Instance.ComputeStatus();
            return PlayerAccount.IsLocalOnly ? CloudSyncStatus.LocalOnly : CloudSyncStatus.Idle;
        }
    }

    /// <summary>Status as a short line in the current language ("" for Idle and Pending).</summary>
    public static string StatusText
    {
        get
        {
            switch (Status)
            {
                case CloudSyncStatus.SigningIn: return L10n.Get(Table, "cloud.status.signing_in");
                case CloudSyncStatus.Syncing: return L10n.Get(Table, "cloud.status.syncing");
                case CloudSyncStatus.Synced: return L10n.Get(Table, "cloud.status.synced");
                case CloudSyncStatus.Offline: return L10n.Get(Table, "cloud.status.offline");
                case CloudSyncStatus.LocalOnly: return L10n.Get(Table, "cloud.status.local_only");
                case CloudSyncStatus.TooBig: return L10n.Get(Table, "cloud.status.too_big");
                default: return "";
            }
        }
    }

    /// <summary>A request to the server is in flight.</summary>
    public static bool IsBusy => Instance != null && Instance.OpRunning;
    /// <summary>A reconcile (compare with the server) is in flight.</summary>
    public static bool IsReconciling => IsBusy && Instance.opName == "reconcile";
    /// <summary>Local writes the server doesn't have yet.</summary>
    public static bool HasUnsyncedChanges => Instance != null && Instance.dirty;
    /// <summary>Why the last request failed (English), or null.</summary>
    public static string LastError => Instance != null ? Instance.lastError : null;
    /// <summary>The server's meta as last read or returned (null = unknown or none).</summary>
    public static SaveMeta LastServerMeta => Instance != null ? Instance.lastServerMeta : null;
    /// <summary>A copy of the sync state (for tools).</summary>
    public static CloudSyncState StateCopy => Instance != null ? Instance.state.Clone() : null;
    /// <summary>The run of the local save chain (null when there is none).</summary>
    public static string CurrentRunId => Instance != null && !string.IsNullOrEmpty(Instance.state.runId) ? Instance.state.runId : null;
    public static string BackendName => Instance != null && Instance.backend != null ? Instance.backend.Name : null;

    /// <summary>The ledger index the server has counted for this run (0 when unknown or another run).</summary>
    public static long KnownLedgerIndex(string runId)
    {
        CloudSaveSync sync = Instance;
        if (sync == null || string.IsNullOrEmpty(runId) || sync.state.runId != runId) return 0;
        return sync.state.ledgerIndex;
    }

    /// <summary>EditorPrefs key of Tools > Game > Cloud Save > Use Local Test Server.</summary>
    public static string LocalTestServerPrefKey => Application.productName + ".CloudSave.UseLocalTestServer";

    /// <summary>The Editor's local test server is on (always false in players).</summary>
    public static bool UseLocalTestServer
    {
        get
        {
#if UNITY_EDITOR
            return UnityEditor.EditorPrefs.GetBool(LocalTestServerPrefKey, false);
#else
            return false;
#endif
        }
    }

    bool OpRunning => currentOp != null && !currentOp.IsCompleted;
    string StateBaseName => backend != null && backend.IsLocalTest ? "cloudsync_localtest" : "cloudsync";
    string StatePath => CloudSyncState.CurrentPath(StateBaseName);

    /// <summary>The running Steam user's Steam ID as text (null without Steam): known offline too.</summary>
    static string CurrentSteamId => SteamPlatform.SteamId != 0 ? SteamPlatform.SteamId.ToString(CultureInfo.InvariantCulture) : null;

    /// <summary>The session may talk to the server for the cloud save (release builds: Steam only; the local test server always).</summary>
    bool IdentityAllowed => backend != null && (backend.IsLocalTest || PlayerAccount.CanUseCloudSave);
    /// <summary>The signed-in player's id for owning saves (never the local test server's stand-in id).</summary>
    string CurrentPlayerId
    {
        get
        {
            string id = backend != null ? backend.PlayerId : null;
            return backend != null && backend.IsLocalTest && id == LocalTestSaveBackend.OfflinePlayerId ? null : id;
        }
    }

    // ------------------------------------------------------------------
    // Lifetime
    // ------------------------------------------------------------------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        // Entering Play mode without a domain reload keeps statics: start every session clean
        Instance = null;
        LocalSaveChanged = null;
        StatusChanged = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        if (DemoMode.Active) Debug.Log("[CloudSave] Off in the demo: local save only.");   // Demo
        Ensure();
    }

    public static CloudSaveSync Ensure()
    {
        if (Instance != null) return Instance;
        if (!Application.isPlaying) return null;
        if (PlaytestMode.Active) return null;   // playtest builds start with extra money: local saves only
        if (DemoMode.Active) return null;       // Demo: the demo keeps its own local save only
        var go = new GameObject("CloudSaveSync");
        DontDestroyOnLoad(go);
        go.AddComponent<CloudLedger>();
        return go.AddComponent<CloudSaveSync>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;

        ledger = GetComponent<CloudLedger>();
        if (ledger == null) ledger = gameObject.AddComponent<CloudLedger>();
        backend = CreateBackend();
        state = CloudSyncState.Load(StatePath) ?? new CloudSyncState();
        localSavePath = SaveSystem.LocalSaveFilePath;
        // A shared PC: another Steam user's sync state and save go aside before the menu offers Continue or New Game
        SteamPlatform.Initialize();
        SwitchToSteamUsersState();
        MigrateLocalSave();
        ledger.LoadFromLocalSave();

        PlayerAccount.SignedIn += OnSignedIn;
        PlayerAccount.StateChanged += OnAccountStateChanged;
        Application.wantsToQuit += OnWantsToQuit;
        SceneManager.sceneLoaded += OnSceneLoaded;
        lastSceneLoadedAt = Time.realtimeSinceStartup;
        Log("Ready (" + backend.Name + "). " + DescribeChain());
    }

    void OnDestroy()
    {
        if (Instance != this) return;
        PlayerAccount.SignedIn -= OnSignedIn;
        PlayerAccount.StateChanged -= OnAccountStateChanged;
        Application.wantsToQuit -= OnWantsToQuit;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        Instance = null;
    }

    static ICloudSaveBackend CreateBackend()
    {
        return UseLocalTestServer ? new LocalTestSaveBackend() : (ICloudSaveBackend)new UgsSaveBackend();
    }

    void Update()
    {
        ShowSettledToasts();
        CheckStatusChanged();

        if (OpRunning || quitReady || backend == null) return;
        float now = Time.realtimeSinceStartup;
        if (now < retryAt || !backend.CanCall) return;

        if (needsReconcile || state.pendingEndRun || state.pendingNewRun)
        {
            RunOp("reconcile", ReconcileRoutine);
            return;
        }
        if (dirty && !uploadsBlocked && now >= nextUploadAt && !SavingSuspended())
            RunOp("upload", UploadRoutine);
    }

    void OnSignedIn()
    {
        // At startup and on every reconnect: compare with the server first
        needsReconcile = true;
        retryAt = 0f;
        failures = 0;
        serverTrouble = false;
    }

    void OnAccountStateChanged()
    {
        if (backend == null || backend.IsLocalTest) return;
        if (PlayerAccount.IsLocalOnly)
        {
            if (!steamToastShown)
            {
                steamToastShown = true;
                QueueToast(ToastSteamOff, NotificationKind.Info);
            }
        }
        else if (PlayerAccount.IsOffline)
        {
            QueueOfflineToast();
        }
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single) return;
        lastSceneLoadedAt = Time.realtimeSinceStartup;
        sceneChangePending = false;
        if (resumeSavingOnNextScene)
        {
            resumeSavingOnNextScene = false;
            SaveSystem save = SaveSystem.Instance;
            if (save != null && save.SavingSuspended)
                save.ResumeSaving();
        }
    }

    // ------------------------------------------------------------------
    // Hooks: SaveSystem, main menu, run end, SceneFlow, debug tools
    // ------------------------------------------------------------------

    /// <summary>
    /// SaveSystem, just before every local write: keep (or give) the save its run id, add one to its seq and set its owner
    /// when it has none: the signed-in player - offline the last one, but only for the same Steam user - plus the Steam ID.
    /// A company that started without New Game gets a new run that uploads like an import (no NewRun), so it can never
    /// replace the server's company. Works without an instance (then only seq grows).
    /// </summary>
    public static void StampLocalWrite(SaveData data)
    {
        if (data == null) return;
        CloudSaveSync sync = Instance;
        CloudSyncState st = sync != null ? sync.state : null;

        if (string.IsNullOrEmpty(data.runId))
        {
            // A company without a run id is a new one: it takes the chain's run only while that run has no accepted save
            // yet (a New Game before or just after its NewRun). Otherwise - all local copies unreadable, a day-0 save - it
            // gets its own run, so a fresh company can never be uploaded over the server's company
            if (st != null && !string.IsNullOrEmpty(st.runId) && (st.pendingNewRun || st.baseSeq == 0))
            {
                data.runId = st.runId;
            }
            else
            {
                // A company that started without New Game (the game scene opened directly): a new run like an import
                data.runId = NewRunId();
                data.seq = 0;
                if (sync != null)
                {
                    st.runId = data.runId;
                    st.baseSeq = 0;
                    st.ledgerIndex = 0;
                    st.pendingNewRun = false;
                    st.runStartedAt = SaveCloud.UnixNow();
                    sync.SaveState();
                    sync.Log("A company without a run: it is run " + Short(data.runId) + " (uploaded like an import, never replacing the server's company).");
                }
            }
        }

        long seq = Math.Max(0L, data.seq);
        if (sync != null)
        {
            if (data.runId == sync.lastLocalRunId) seq = Math.Max(seq, sync.lastLocalSeq);
            if (data.runId == st.runId) seq = Math.Max(seq, st.baseSeq);
        }
        data.seq = seq + 1;
        // Where this chain stands on the server: EndRun and a restored save can tell even when the sync state is gone
        if (st != null && data.runId == st.runId) data.baseSeq = st.baseSeq;

        // The owner is set once; a save of another player is sorted out by the next reconcile, never re-labelled here
        if (sync != null)
        {
            string steamId = CurrentSteamId;
            string me = sync.CurrentPlayerId;
            sync.BindStateToSteamUser(steamId, me);
            // Offline, the last signed-in player owns it only when Steam says it is the same user (no Steam: can't tell)
            bool sameSteamUser = steamId == null || st.ownerSteamId == steamId;
            if (string.IsNullOrEmpty(data.ownerId))
            {
                if (!string.IsNullOrEmpty(me))
                    data.ownerId = me;
                else if (!string.IsNullOrEmpty(st.ownerId) && sameSteamUser)
                    data.ownerId = st.ownerId; // offline: the last signed-in player, when it is the same Steam user
            }
            bool ownersSave = string.IsNullOrEmpty(data.ownerId) || data.ownerId == me || (data.ownerId == st.ownerId && sameSteamUser);
            if (string.IsNullOrEmpty(data.ownerSteamId) && steamId != null && ownersSave)
                data.ownerSteamId = steamId;
            sync.lastLocalRunId = data.runId;
            sync.lastLocalSeq = data.seq;
        }
    }

    /// <summary>SaveSystem, after every local write: schedule its upload (urgent = explicit, pause or quit save).</summary>
    public static void NotifyLocalWrite(bool urgent)
    {
        CloudSaveSync sync = Instance;
        if (sync == null) return;
        float now = Time.realtimeSinceStartup;
        float due = Mathf.Max(now, sync.lastUploadStartedAt + (urgent ? MinUploadGap : AutoUploadInterval));
        sync.nextUploadAt = sync.dirty ? Mathf.Min(sync.nextUploadAt, due) : due;
        sync.dirty = true;
    }

    /// <summary>
    /// New Game (and the debug resets): a new run id for the company that starts now. The server hears of it at the next
    /// sync (NewRun); until then the run is pending, also across restarts.
    /// </summary>
    public static void BeginNewRun()
    {
        CloudSaveSync sync = Ensure();
        if (sync == null) return;
        sync.generation++;
        string runId = NewRunId();
        sync.state.runId = runId;
        sync.state.baseSeq = 0;
        sync.state.ledgerIndex = 0;
        sync.state.pendingNewRun = true;
        sync.state.runStartedAt = SaveCloud.UnixNow();
        sync.state.lastAcceptedAt = 0;
        // What this PC knew of the server when the company started: NewRun sends it, so a New Game made on an out-of-date
        // PC can't replace a company another PC kept playing since (the answer is then superseded)
        sync.state.newRunKnownAcceptedAt = sync.state.knownAcceptedAt;
        sync.state.newRunSteamId = CurrentSteamId;
        if (string.IsNullOrEmpty(sync.state.ownerId))
            sync.state.ownerId = sync.CurrentPlayerId;
        sync.BindStateToSteamUser(CurrentSteamId, sync.CurrentPlayerId);
        // The previous run's unanswered upload stays recorded: if it was accepted, NewRun's "superseded" is our own save
        sync.SaveState();
        sync.ledger.ResetForRun(runId, 0);
        sync.lastLocalRunId = runId;
        sync.lastLocalSeq = 0;
        sync.dirty = false;
        sync.lastServerMeta = null;
        sync.ResetUploadBlocks();
        sync.needsReconcile = true;
        sync.Log("New run " + Short(runId) + " (the server registers it at the next sync).");
    }

    /// <summary>
    /// Game Over: the run is over. The server is told (EndRun) now or at the next sync, with what shows whether this copy
    /// was up to date (a copy another PC moved on since can't end the run: the server answers stale and the company comes
    /// back). Nothing of this run is uploaded any more. Call it before the save is deleted.
    /// </summary>
    public static void EndRun()
    {
        CloudSaveSync sync = Ensure();
        if (sync == null) return;
        CloudSyncState st = sync.state;
        // The run that ended is the local save's (the company that was running); the chain's when there is no save
        LocalSave local = LocalSave.Read();
        string runId = local != null && !string.IsNullOrEmpty(local.RunId) ? local.RunId : st.runId;
        sync.generation++;
        if (!string.IsNullOrEmpty(runId))
        {
            // A run the server never registered needs no EndRun
            bool serverKnowsIt = !(st.pendingNewRun && st.runId == runId);
            if (serverKnowsIt)
            {
                st.pendingEndRun = true;
                st.endedRunId = runId;
                st.endedSteamId = CurrentSteamId;
                // Where this copy stood: the chain's base, or - when the sync state doesn't know this run - the base stamped
                // into the save (-1 = unknown)
                st.endedBaseSeq = st.runId == runId ? st.baseSeq : (local != null && local.RunId == runId ? Math.Max(0L, local.Data.baseSeq) : -1);
                bool unanswered = st.inFlightRun == runId && !string.IsNullOrEmpty(st.inFlightHash);
                st.endedInFlightSeq = unanswered ? st.inFlightSeq : -1;
                st.endedInFlightHash = unanswered ? st.inFlightHash : null;
            }
        }
        sync.BindStateToSteamUser(CurrentSteamId, sync.CurrentPlayerId);
        st.runId = null;
        st.baseSeq = 0;
        st.ledgerIndex = 0;
        st.pendingNewRun = false;
        sync.SaveState();
        sync.ledger.ResetForRun(null, 0);
        sync.lastLocalRunId = null;
        sync.lastLocalSeq = 0;
        sync.dirty = false;
        sync.needsReconcile = true;
        sync.Log("Run " + Short(runId) + " is over" + (st.pendingEndRun ? " (the server is told at the next sync)." : "."));
    }

    /// <summary>
    /// New Game (MainMenuUI): the local save goes. A save that belongs to another Steam user or player (a shared PC) is set
    /// aside instead of deleted, so their company isn't lost. When it can't be set aside nothing is deleted and New Game
    /// must stop: false (logged, and a toast says so).
    /// </summary>
    public static bool ClearLocalSaveForNewGame()
    {
        CloudSaveSync sync = Instance;
        if (sync != null)
        {
            LocalSave local = LocalSave.Read();
            string owner = local != null ? sync.OtherOwnerKeyOf(local) : null;
            if (owner != null)
            {
                if (sync.SetAside(local, owner)) return true;
                Debug.LogError("[CloudSave] New Game stopped: the local save belongs to " + owner + " and couldn't be set aside, so it isn't deleted.");
                sync.QueueToast(ToastNewGameBlocked, NotificationKind.Bad);
                return false;
            }
        }
        SaveSystem.DeleteLocalSaveFiles();
        return true;
    }

    /// <summary>
    /// SceneFlow.Load into the game: when a sync is running (or about to, right after sign-in), wait for it (at most
    /// GameLoadWaitSeconds) and load the scene then. Continue without a local save waits as well (the sync may download
    /// one) and goes back to a fresh menu when there still is none: the game never starts an unplanned company. New Game
    /// never waits. True = the load was taken over; false = load now.
    /// </summary>
    public static bool DeferGameLoad(string sceneName)
    {
        CloudSaveSync sync = Instance;
        if (sync == null) return false;
        if (sync.waitingToLoadGame) return true; // already waiting: that load happens when the wait ends
        if (PlayerPrefs.GetInt(SaveSystem.NewGameFlagKey, 0) == 1) return false; // a new company doesn't need the old save
        if (SaveSystem.LocalSaveExists() && !sync.SyncPendingForGameLoad()) return false;
        sync.StartCoroutine(sync.LoadGameWhenSynced(sceneName));
        return true;
    }

    /// <summary>Tools: compare with the server now (signs in first if needed).</summary>
    public static void ReconcileNow()
    {
        CloudSaveSync sync = Ensure();
        if (sync == null) return;
        sync.needsReconcile = true;
        sync.retryAt = 0f;
        sync.failures = 0;
        sync.uploadsBlocked = false;
        if (!sync.backend.CanCall) _ = PlayerAccount.SignInAsync();
    }

    /// <summary>Tools: save now (when a company runs) and upload right away.</summary>
    public static void UploadNow()
    {
        CloudSaveSync sync = Ensure();
        if (sync == null) return;
        SaveSystem save = SaveSystem.Instance;
        if (save != null && CompanyManager.Instance != null && save.IsGameLoaded)
            save.SaveGame();
        sync.dirty = true;
        sync.nextUploadAt = 0f;
        sync.retryAt = 0f;
        sync.uploadsBlocked = false;
        if (!sync.backend.CanCall) _ = PlayerAccount.SignInAsync();
    }

    /// <summary>Tools: the local test server was switched on or off. Each server keeps its own sync state file.</summary>
    public static void OnBackendSettingChanged()
    {
        CloudSaveSync sync = Instance;
        if (sync == null) return;
        sync.generation++;
        sync.backend = CreateBackend();
        sync.state = CloudSyncState.Load(sync.StatePath) ?? new CloudSyncState();
        sync.ledger.LoadFromLocalSave();
        sync.lastServerMeta = null;
        sync.lastLocalRunId = null;
        sync.lastLocalSeq = 0;
        sync.needsReconcile = true;
        sync.retryAt = 0f;
        sync.failures = 0;
        sync.serverTrouble = false;
        sync.uploadsBlocked = false;
        sync.Log("Now using the " + sync.backend.Name + ". " + sync.DescribeChain());
    }

    // ------------------------------------------------------------------
    // Requests
    // ------------------------------------------------------------------

    void RunOp(string name, Func<int, Task> routine)
    {
        opName = name;
        currentOp = RunOpAsync(name, routine, generation);
    }

    async Task RunOpAsync(string name, Func<int, Task> routine, int gen)
    {
        try
        {
            await routine(gen);
        }
        catch (Exception e)
        {
            if (this != null && gen == generation) OnRequestFailed(e);
        }
        finally
        {
            // A pending NewRun / EndRun that couldn't be settled mustn't be retried every frame
            if (this != null && name == "reconcile" && (state.pendingEndRun || state.pendingNewRun) && retryAt <= Time.realtimeSinceStartup)
                retryAt = Time.realtimeSinceStartup + 30f;
        }
    }

    bool Alive(int gen)
    {
        return this != null && gen == generation && Application.isPlaying;
    }

    async Task ReconcileRoutine(int gen)
    {
        needsReconcile = false;
        if (!await backend.EnsureReady() || !IdentityAllowed)
        {
            // Release builds: never under a non-Steam session (another sign-in's anonymous player)
            MarkOffline(PlayerAccount.LastError ?? "Not signed in with Steam.");
            return;
        }
        if (!Alive(gen)) return;
        string me = backend.PlayerId;
        if (string.IsNullOrEmpty(me))
        {
            MarkOffline("No player id.");
            return;
        }

        // Whose save is this? (another player's save and state are set aside; this player's come back). The local test
        // server's stand-in id while nobody is signed in owns nothing: it would set aside the developer's own save.
        bool standInId = backend.IsLocalTest && me == LocalTestSaveBackend.OfflinePlayerId;
        if (!standInId)
        {
            // (read on a worker: ApplyOwnership reads it again, fresh, when it acts)
            LocalSave owned = await LocalSave.ReadAsync();
            if (!Alive(gen)) return;
            if (LocalSaveChangesOwner(owned, me))
            {
                // The Online City state goes with the save: transfers on their way settle first
                await WaitForCityTransfers(gen, "moving the local save to its owner");
                if (!Alive(gen)) return;
            }
            if (ApplyOwnership(me))
            {
                needsReconcile = true; // went back to the menu: compare again there
                return;
            }
        }

        // A New Game or Game Over another Steam user made on this PC isn't this player's to report
        string mySteamId = CurrentSteamId;
        if (mySteamId != null)
        {
            if (state.pendingEndRun && !string.IsNullOrEmpty(state.endedSteamId) && state.endedSteamId != mySteamId)
            {
                Log("Dropping a Game Over another Steam user made on this PC (run " + Short(state.endedRunId) + ").");
                ClearPendingEndRun();
            }
            if (state.pendingNewRun && !string.IsNullOrEmpty(state.newRunSteamId) && state.newRunSteamId != mySteamId)
            {
                Log("Dropping a New Game another Steam user made on this PC (run " + Short(state.runId) + ").");
                state.pendingNewRun = false;
                state.runId = null;
                state.baseSeq = 0;
                state.ledgerIndex = 0;
                SaveState();
            }
        }

        // A Game Over this PC hasn't reported yet (no_run - another run is current - counts as done too)
        if (state.pendingEndRun)
        {
            if (!string.IsNullOrEmpty(state.endedRunId))
            {
                string endedRun = state.endedRunId;
                SaveResult ended = await backend.EndRun(endedRun, state.endedBaseSeq, state.endedInFlightSeq, state.endedInFlightHash);
                if (!Alive(gen)) return;
                if (!IsFinalAnswer(ended))
                {
                    OnServerTrouble(ended, "EndRun");
                    return;
                }
                if (ended.Code == SaveCodes.Stale)
                {
                    // Another PC moved the company on since this copy: the bankruptcy here happened on an old copy
                    Log("The Game Over of run " + Short(endedRun) + " came from an out-of-date copy: the company goes on (" + ended.Message + ").");
                    ClearPendingEndRun();
                    if (!state.pendingNewRun)
                    {
                        // Nothing new was started here: the company comes back. (A New Game started since is decided by its
                        // NewRun below: superseded, since the server's company moved on after this PC last synced.)
                        await AdoptFromServer(gen, ToastBankruptcyStale);
                        return;
                    }
                }
                else
                {
                    Log("The server knows run " + Short(endedRun) + " is over (" + ended.Code + ").");
                    ClearPendingEndRun();
                }
            }
            else
            {
                ClearPendingEndRun();
            }
        }

        CloudSnapshot server = await backend.Load() ?? new CloudSnapshot();
        if (!Alive(gen)) return;
        await PrepareSnapshotAsync(server);
        if (!Alive(gen)) return;
        LocalSave local = await LocalSave.ReadAsync();
        if (!Alive(gen)) return;

        // A company this PC started that the server doesn't know yet
        if (state.pendingNewRun && !string.IsNullOrEmpty(state.runId))
        {
            if (local != null && local.RunId != state.runId)
            {
                // The flag belongs to a run whose save isn't the local one any more
                state.pendingNewRun = false;
                SaveState();
            }
            else
            {
                if (server.Meta == null || server.Meta.RunId != state.runId)
                {
                    SaveResult started = await backend.NewRun(state.runId, state.newRunKnownAcceptedAt);
                    if (!Alive(gen)) return;
                    if (started != null && started.Code == SaveCodes.Superseded && IsOwnLastUpload(server, started.Meta))
                    {
                        // What the server calls newer is this PC's own last upload of the previous run (its answer was lost
                        // or came after New Game): the New Game stands
                        Log("NewRun was superseded by this PC's own last upload (run " + Short(started.Meta.RunId) + " seq " + started.Meta.Seq + "): asking again.");
                        state.newRunKnownAcceptedAt = started.Meta.AcceptedAt;
                        SaveState();
                        started = await backend.NewRun(state.runId, state.newRunKnownAcceptedAt);
                        if (!Alive(gen)) return;
                    }
                    if (started != null && started.Code == SaveCodes.Superseded)
                    {
                        await OnNewRunSuperseded(gen, started);
                        return;
                    }
                    if (started == null || !started.Ok || started.Meta == null || started.Meta.RunId != state.runId)
                    {
                        // Not registered (failed, declined, or an answer about another run): the run stays pending and
                        // the local save stays; tried again later
                        OnServerTrouble(started, "NewRun");
                        return;
                    }
                    server = new CloudSnapshot { Meta = started.Meta };
                    Log("The server registered run " + Short(state.runId) + ".");
                }
                // The server's current run is now this PC's company
                NoteServerMeta(server.Meta);
                state.pendingNewRun = false;
                state.baseSeq = server.Meta != null ? server.Meta.Seq : 0;
                state.ledgerIndex = server.Meta != null ? server.Meta.LedgerIndex : 0;
                state.lastSyncAt = SaveCloud.UnixNow();
                SaveState();
            }
        }

        lastServerMeta = server.Meta;
        OnRequestSucceeded();
        await Decide(gen, local, server);
    }

    /// <summary>The server's save is exactly this PC's unanswered upload (same run, seq and SavePacking.Hash).</summary>
    bool IsOwnLastUpload(CloudSnapshot server, SaveMeta meta)
    {
        if (meta == null || string.IsNullOrEmpty(state.inFlightHash) || state.inFlightRun != meta.RunId || state.inFlightSeq != meta.Seq) return false;
        return server != null && server.Meta != null && server.Meta.RunId == meta.RunId && server.Meta.Seq == meta.Seq &&
               server.SaveHash != null && string.Equals(server.SaveHash, state.inFlightHash, StringComparison.OrdinalIgnoreCase);
    }

    void ClearPendingEndRun()
    {
        state.pendingEndRun = false;
        state.endedRunId = null;
        state.endedSteamId = null;
        state.endedBaseSeq = -1;
        state.endedInFlightSeq = -1;
        state.endedInFlightHash = null;
        SaveState();
    }

    /// <summary>
    /// NewRun answered superseded: another PC played on after this one last synced, so the New Game made here must not
    /// replace that company. The company started here is kept aside (savegame_offline_*.json) and the server's is loaded.
    /// </summary>
    async Task OnNewRunSuperseded(int gen, SaveResult answer)
    {
        Log("The server kept its company: another PC played on after this one last synced (" + answer.Message + "). " +
            "The company started here (run " + Short(state.runId) + ") is set aside.");
        state.pendingNewRun = false;
        SaveState();
        // The new company may be running already: its latest state goes into the file that is kept
        SaveSystem save = SaveSystem.Instance;
        if (save != null && !save.SavingSuspended && CompanyManager.Instance != null && save.IsGameLoaded)
            save.SaveGame();
        CloudSnapshot snapshot = await backend.Load() ?? new CloudSnapshot();
        if (!Alive(gen)) return;
        await PrepareSnapshotAsync(snapshot);
        if (!Alive(gen)) return;
        lastServerMeta = snapshot.Meta;
        await AdoptServerSaveAsync(gen, snapshot, ToastSuperseded, "offline");
    }

    async Task Decide(int gen, LocalSave local, CloudSnapshot server)
    {
        SaveMeta meta = server.Meta;

        if (local == null)
        {
            if (server.DataBroken && meta != null && !meta.Ended)
            {
                OnServerTrouble(null, "reading the server's save");
                return;
            }
            if (meta == null || meta.Ended || string.IsNullOrEmpty(server.SaveJson))
            {
                // Nothing to download: no save yet, a finished run, or a run that hasn't saved yet
                dirty = false;
                return;
            }
            await AdoptServerSaveAsync(gen, server, ToastRestored);
            return;
        }

        if (meta == null)
        {
            // The server has nothing yet: this save goes up (the server imports a run it never saw)
            await SubmitLocal(gen, local, false);
            return;
        }

        if (meta.RunId == local.RunId)
        {
            if (meta.Ended)
            {
                DiscardLocal(ToastEndedElsewhere, meta);
                return;
            }
            bool chainKnown = state.runId == local.RunId;
            if (local.Seq == meta.Seq && ((chainKnown && state.baseSeq == meta.Seq) || string.Equals(local.Text, server.SaveJson, StringComparison.Ordinal)))
            {
                // In step already
                MarkServerHas(meta);
                dirty = false;
                return;
            }
            if (local.Seq > meta.Seq && chainKnown && state.baseSeq == meta.Seq)
            {
                await SubmitLocal(gen, local, false);
                return;
            }
            if (IsOurUnansweredUpload(server))
            {
                // The answer to an upload of ours was lost, but the server stored it (the exact text we sent): accepted
                Log("The server has save " + meta.Seq + " of run " + Short(meta.RunId) + " from this PC (its answer never arrived): counted as accepted.");
                if (local.Seq < meta.Seq)
                {
                    // The local copy is older than that upload (it fell back to a previous copy): the server's is newer
                    await AdoptServerSaveAsync(gen, server, null);
                    return;
                }
                MarkServerHas(meta);
                if (local.Seq > meta.Seq)
                    await SubmitLocal(gen, local, false);
                else
                    dirty = false;
                return;
            }
            if (meta.Seq == 0 && string.IsNullOrEmpty(server.SaveJson) && !server.DataBroken)
            {
                // The server has this run registered without a save (NewRun, nothing accepted yet): this PC's copy is its
                // only save, so it goes up as the run's first save
                Log("The server has run " + Short(meta.RunId) + " registered without a save: uploading this PC's copy as its first save.");
                state.runId = local.RunId;
                state.baseSeq = 0;
                state.ledgerIndex = meta.LedgerIndex;
                state.pendingNewRun = false;
                SaveState();
                await SubmitLocal(gen, local, false);
                return;
            }
            // The server is ahead, or this PC's copy branched off it: the server's copy wins
            await AdoptServerSaveAsync(gen, server, ToastNewerElsewhere);
            return;
        }

        // Different runs: the server's current run wins (a run started here was registered above)
        await AdoptServerSaveAsync(gen, server, ToastNewerElsewhere);
    }

    /// <summary>The server's save is the very upload whose answer never arrived (same run, seq and text): proof enough.</summary>
    bool IsOurUnansweredUpload(CloudSnapshot server)
    {
        SaveMeta meta = server.Meta;
        return meta != null && !string.IsNullOrEmpty(state.inFlightHash) && state.inFlightRun == meta.RunId && state.inFlightSeq == meta.Seq &&
               server.SaveHash != null && string.Equals(server.SaveHash, state.inFlightHash, StringComparison.OrdinalIgnoreCase);
    }



    /// <summary>The server has this chain's save of <paramref name="meta"/>: continue from it.</summary>
    void MarkServerHas(SaveMeta meta)
    {
        state.runId = meta.RunId;
        state.baseSeq = meta.Seq;
        state.ledgerIndex = meta.LedgerIndex;
        state.lastAcceptedAt = meta.AcceptedAt;
        state.knownAcceptedAt = meta.AcceptedAt;
        state.lastSyncAt = SaveCloud.UnixNow();
        state.inFlightRun = null;
        state.inFlightSeq = 0;
        state.inFlightHash = null;
        SaveState();
        ledger.Prune(meta.LedgerIndex);
    }

    async Task UploadRoutine(int gen)
    {
        if (!IdentityAllowed)
        {
            MarkOffline(PlayerAccount.LastError ?? "Not signed in with Steam.");
            return;
        }
        LocalSave local = await LocalSave.ReadAsync();
        if (!Alive(gen)) return;
        if (local == null)
        {
            dirty = false;
            return;
        }
        string me = backend.PlayerId;
        bool standInId = backend.IsLocalTest && me == LocalTestSaveBackend.OfflinePlayerId;
        bool otherOwner = !standInId && !string.IsNullOrEmpty(me) && OtherOwnerKey(local.OwnerId, local.OwnerSteamId, me, CurrentSteamId) != null;
        if (otherOwner || local.RunId != state.runId || state.pendingNewRun || state.pendingEndRun)
        {
            needsReconcile = true; // the reconcile sorts out whose save and which run this is
            return;
        }
        if (local.Seq <= state.baseSeq)
        {
            dirty = false;
            return;
        }
        await SubmitLocal(gen, local, true);
    }

    /// <summary>
    /// Upload the local save. It is read right before the request (unless <paramref name="justRead"/>): the server's time
    /// rule and card bound assume the save was captured just before the upload, and every retry comes through here.
    /// Reading, packing, fingerprinting and measuring run off the main thread. The upload is remembered (in flight) until
    /// its answer; an answer that arrives after New Game / Game Over still settles its bookkeeping (OnLateSubmitAnswer).
    /// </summary>
    async Task SubmitLocal(int gen, LocalSave local, bool justRead)
    {
        if (uploadsBlocked)
        {
            dirty = false;
            return;
        }
        if (CityTransferHold())
        {
            // An Online City transfer is on its way: its save waits (a refused deposit's refund mustn't look like growth)
            dirty = true;
            nextUploadAt = Time.realtimeSinceStartup + 5f;
            return;
        }
        LocalSave fresh = local;
        if (!justRead)
        {
            fresh = await LocalSave.ReadAsync();
            if (!Alive(gen)) return;
        }
        if (fresh == null || fresh.RunId != local.RunId || fresh.Seq < local.Seq)
        {
            needsReconcile = true; // the local save changed hands in the meantime: compare again
            return;
        }
        // This text goes up: a write from now on (while preparing or during the request) marks the next upload
        dirty = false;

        bool chainKnown = state.runId == fresh.RunId;
        long countedIndex = chainKnown ? state.ledgerIndex : 0;
        long baseSeq = chainKnown ? state.baseSeq : 0;
        string build = Application.version;
        bool dev = Application.isEditor || Debug.isDebugBuild;
        PreparedSave prepared = await Task.Run(() => PreparedSave.From(fresh, countedIndex, baseSeq, build, dev));
        if (!Alive(gen)) return;
        if (prepared.Problem != null)
        {
            if (prepared.TooBig)
            {
                saveTooBig = true;
                BlockUploads("Not uploading: " + prepared.Problem + " The save stays on this PC.");
            }
            else
            {
                Debug.LogWarning("[CloudSave] Not uploading: " + prepared.Problem);
            }
            return;
        }

        // Until its answer arrives: a lost answer to an accepted upload is recognised at the next reconcile
        SubmitRequest request = prepared.Request;
        state.inFlightRun = request.RunId;
        state.inFlightSeq = request.Seq;
        state.inFlightHash = prepared.Hash;
        SaveState();

        lastUploadStartedAt = Time.realtimeSinceStartup;
        SaveResult result;
        try
        {
            result = await backend.Submit(request, prepared.RequestJson);
        }
        catch (Exception e) when (IsPayloadTooLarge(e))
        {
            // The server takes less than MaxRequestBytes after all: no retries (they'd all fail the same way)
            if (!Alive(gen)) return;
            saveTooBig = true;
            BlockUploads("The server refused the upload as too large (" + e.Message + "). The save stays on this PC.");
            return;
        }
        if (!Alive(gen))
        {
            OnLateSubmitAnswer(request, prepared.Hash, result);
            return;
        }
        await HandleSubmitResult(gen, request, result, prepared.LedgerCount);
    }

    /// <summary>
    /// An upload's answer that came after New Game / Game Over / a backend switch started another chain: only its
    /// bookkeeping counts. An accept of that upload means the server's save is ours - a New Game waiting for its NewRun
    /// then counts it as known (its NewRun would otherwise be "superseded" by this PC's own save).
    /// </summary>
    void OnLateSubmitAnswer(SubmitRequest request, string hash, SaveResult result)
    {
        if (this == null || !Application.isPlaying || result == null) return;
        bool ours = state.inFlightRun == request.RunId && state.inFlightSeq == request.Seq && state.inFlightHash == hash;
        if (result.Code == SaveCodes.Accepted && result.Meta != null && result.Meta.RunId == request.RunId)
        {
            if (state.pendingNewRun && result.Meta.AcceptedAt > state.newRunKnownAcceptedAt)
                state.newRunKnownAcceptedAt = result.Meta.AcceptedAt;
            if (result.Meta.AcceptedAt > state.knownAcceptedAt)
                state.knownAcceptedAt = result.Meta.AcceptedAt;
            Log("A late answer: save " + request.Seq + " of run " + Short(request.RunId) + " was accepted (after the chain moved on).");
        }
        if (ours && IsFinalSubmitCode(result.Code))
        {
            state.inFlightRun = null;
            state.inFlightSeq = 0;
            state.inFlightHash = null;
        }
        SaveState();
    }

    /// <summary>A "payload too large" (HTTP 413) failure, however the SDK words it.</summary>
    static bool IsPayloadTooLarge(Exception e)
    {
        for (Exception inner = e; inner != null; inner = inner.InnerException)
        {
            string message = inner.Message ?? "";
            if (Regex.IsMatch(message, @"\b413\b") ||
                message.IndexOf("too large", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("entity too large", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    /// <summary>
    /// A save made ready for upload off the main thread: packed save (SaveGz), packed ledger (LedgerGz), the request and its
    /// JSON measured against SaveCloud.MaxRequestBytes, and SavePacking.Hash of the text (the in-flight fingerprint).
    /// </summary>
    sealed class PreparedSave
    {
        public SubmitRequest Request;
        public string RequestJson;
        public string Hash;
        public int LedgerCount;
        public string Problem;
        public bool TooBig;

        public static PreparedSave From(LocalSave local, long countedIndex, long baseSeq, string build, bool dev)
        {
            var prepared = new PreparedSave();
            try
            {
                string text = local.Text;
                SaveData data = local.Data;
                if (string.IsNullOrEmpty(text))
                {
                    prepared.Problem = "the local save is empty.";
                    return prepared;
                }
                int bytes = Encoding.UTF8.GetByteCount(text);
                if (bytes > SaveCloud.MaxSaveBytes)
                {
                    prepared.TooBig = true;
                    prepared.Problem = "the save is " + bytes + " bytes, more than the server takes (" + SaveCloud.MaxSaveBytes + ").";
                    return prepared;
                }
                // Packed saves are ~3x smaller (measured on realistic saves)
                string packed = SavePacking.Pack(text);
                if (packed.Length > SaveCloud.MaxPackedSaveBytes)
                {
                    prepared.TooBig = true;
                    prepared.Problem = "the packed save is " + packed.Length + " bytes, more than the server takes (" + SaveCloud.MaxPackedSaveBytes + ").";
                    return prepared;
                }
                List<LedgerEntry> ledger = CloudLedger.EntriesInSave(data, countedIndex);
                var request = new SubmitRequest
                {
                    RunId = local.RunId,
                    Seq = local.Seq,
                    BaseSeq = baseSeq,
                    Day = data.currentDay,      // the server reads the same numbers from the same text (it allows $0.50)
                    Value = data.companyValue,
                    SaveJson = null,
                    SaveGz = packed,            // the server unpacks it before any check
                    Ledger = new List<LedgerEntry>(),
                    LedgerGz = ledger.Count > 0 ? SavePacking.Pack(JsonConvert.SerializeObject(ledger)) : null,
                    Build = build,
                    Dev = dev,
                    ClientTime = SaveCloud.UnixNow()
                };
                string json = JsonConvert.SerializeObject(request);
                int requestBytes = Encoding.UTF8.GetByteCount(json);
                if (requestBytes > SaveCloud.MaxRequestBytes)
                {
                    prepared.TooBig = true;
                    prepared.Problem = "the upload would be " + requestBytes + " bytes, more than a request may be (" + SaveCloud.MaxRequestBytes + ").";
                    return prepared;
                }
                prepared.Request = request;
                prepared.RequestJson = json;
                prepared.LedgerCount = ledger.Count;
                prepared.Hash = SavePacking.Hash(text);
            }
            catch (Exception e)
            {
                prepared.Problem = "the save couldn't be prepared (" + e.Message + ").";
            }
            return prepared;
        }
    }



    async Task HandleSubmitResult(int gen, SubmitRequest request, SaveResult result, int ledgerCount)
    {
        if (result == null)
        {
            OnServerTrouble(null, "SubmitSave");
            return;
        }
        if (IsFinalSubmitCode(result.Code))
            ClearInFlight(); // answered: nothing to recognise later

        switch (result.Code)
        {
            case SaveCodes.Accepted:
                OnAccepted(request, result, ledgerCount);
                return;

            case SaveCodes.Busy:
                busyRetries++;
                dirty = true;
                needsReconcile = false;
                if (busyRetries <= MaxBusyRetries)
                {
                    retryAt = Time.realtimeSinceStartup + 2f * busyRetries;
                    nextUploadAt = 0f;
                    return;
                }
                busyRetries = 0;
                OnServerTrouble(result, "SubmitSave");
                return;

            case SaveCodes.Stale:
            case SaveCodes.NoRun:
                // Another PC got ahead, branched off, or started another company: its copy wins
                Log("The server has newer data (" + result.Code + ": " + result.Message + ").");
                await AdoptFromServer(gen, ToastNewerElsewhere);
                return;

            case SaveCodes.Ended:
                Log("The run ended on the server (" + result.Message + ").");
                await AdoptFromServer(gen, ToastEndedElsewhere);
                return;

            case SaveCodes.Time:
            case SaveCodes.Invalid:
                OnRejected(request, result);
                if (uploadsBlocked) return;
                await AdoptFromServer(gen, ToastRejected);
                return;

            default:
                OnServerTrouble(result, "SubmitSave");
                return;
        }
    }

    void OnAccepted(SubmitRequest request, SaveResult result, int ledgerCount)
    {
        SaveMeta meta = result.Meta;
        if (string.IsNullOrEmpty(state.ownerId)) state.ownerId = CurrentPlayerId;
        state.runId = request.RunId;
        state.baseSeq = request.Seq;
        if (meta != null)
        {
            state.ledgerIndex = meta.LedgerIndex;
            state.lastAcceptedAt = meta.AcceptedAt;
        }
        state.pendingNewRun = false;
        state.lastSyncAt = SaveCloud.UnixNow();
        if (meta != null) state.knownAcceptedAt = meta.AcceptedAt;
        SaveState();
        lastServerMeta = meta;
        busyRetries = 0;
        ledger.Prune(state.ledgerIndex);
        OnRequestSucceeded();

        Log("Save " + request.Seq + " of run " + Short(request.RunId) + " accepted (day " + request.Day + ", " +
            UIFormat.Money(request.Value) + ", " + ledgerCount + " ledger entries).");
        if (result.NewFlags != null && result.NewFlags.Count > 0)
        {
            var reasons = new StringBuilder();
            foreach (SaveFlag flag in result.NewFlags)
            {
                if (flag == null) continue;
                if (reasons.Length > 0) reasons.Append("; ");
                reasons.Append(flag.Reason).Append(" (").Append(flag.Detail).Append(')');
            }
            Debug.LogWarning("[CloudSave] The server flagged this run: " + reasons);
        }
    }

    void OnRejected(SubmitRequest request, SaveResult result)
    {
        Debug.LogWarning("[CloudSave] The server refused save " + request.Seq + " of run " + Short(request.RunId) + ": " +
                         result.Code + " (" + result.Message + ").");
        if (rejectedRunId != request.RunId)
        {
            rejectedRunId = request.RunId;
            rejections = 0;
        }
        rejections++;
        if (rejections >= RejectionsBeforeStopping)
            BlockUploads("The server refused this run's saves " + rejections + " times (" + result.Code + "): uploads stop until the next start. " +
                         "The local save is kept. If this is a bug, compare the client's save with SaveRules.");
    }

    /// <summary>No more uploads this session (the local save stays and keeps working); logged as an error.</summary>
    void BlockUploads(string reason)
    {
        uploadsBlocked = true;
        dirty = false;
        lastError = reason;
        Debug.LogError("[CloudSave] " + reason);
    }

    async Task AdoptFromServer(int gen, string toastKey)
    {
        CloudSnapshot snapshot = await backend.Load() ?? new CloudSnapshot();
        if (!Alive(gen)) return;
        await PrepareSnapshotAsync(snapshot);
        if (!Alive(gen)) return;
        lastServerMeta = snapshot.Meta;
        await AdoptServerSaveAsync(gen, snapshot, toastKey);
    }

    // ------------------------------------------------------------------
    // Changing the local save
    // ------------------------------------------------------------------

    /// <summary>
    /// AdoptServerSave, with the Online City money when the server's save is going to be loaded: transfers on their way
    /// first get their chance to settle (WaitForCityTransfers), then the player's city accounts are read (SettleCityMoney).
    /// When they can't be read the local save stays as it is and it is tried again later; only after CityReadAttempts
    /// failures for the same server save is it loaded without (logged as an error). Closing or keeping the local save
    /// neither waits nor reads: a transfer settling then would pay into a company that is being closed.
    /// </summary>
    async Task AdoptServerSaveAsync(int gen, CloudSnapshot server, string toastKey, string keepAs = "replaced")
    {
        if (!WillLoadServerSave(server))
        {
            AdoptServerSave(server, toastKey, keepAs, null, null);
            return;
        }
        await WaitForCityTransfers(gen, "loading the server's save");
        if (!Alive(gen)) return;
        HashSet<string> before = OwnCityTransferIds(CityOnline.Instance);
        CityMoney money = await ReadCityMoney();
        if (!Alive(gen)) return;
        if (money != null && !before.SetEquals(OwnCityTransferIds(CityOnline.Instance)))
        {
            // A transfer started or settled while the accounts were read may be missing from them: once more
            await WaitForCityTransfers(gen, "loading the server's save");
            if (!Alive(gen)) return;
            money = await ReadCityMoney();
            if (!Alive(gen)) return;
        }
        if (money == null)
        {
            if (!GiveUpOnCityMoney(server.Meta)) return; // tried again later; the local save stays as it is
        }
        else
        {
            cityReadFailedFor = null;
            cityReadFailures = 0;
        }
        // The save's pending city transfers exactly as the server reads them (SaveSnapshot, off the main thread)
        List<SavePendingTransfer> pending = null;
        if (money != null && money.Known)
        {
            string json = server.SaveJson;
            pending = await Task.Run(() => PendingTransfersOf(json));
            if (!Alive(gen)) return;
        }
        AdoptServerSave(server, toastKey, keepAs, money, pending);
    }

    /// <summary>A save's pending Online City transfers as the server reads them (SaveSnapshot); null when it doesn't parse there.</summary>
    static List<SavePendingTransfer> PendingTransfersOf(string json)
    {
        SaveSnapshot snapshot = SaveSnapshot.Parse(json);
        return snapshot.Ok ? snapshot.PendingTransfers : null;
    }

    /// <summary>
    /// The Online City accounts couldn't be read for the server's save of <paramref name="meta"/>. False: try again later (the
    /// local save stays as it is). True after CityReadAttempts failures for that same save: load it without its city money.
    /// </summary>
    bool GiveUpOnCityMoney(SaveMeta meta)
    {
        string save = meta.RunId + " seq " + meta.Seq.ToString(CultureInfo.InvariantCulture);
        if (cityReadFailedFor != save)
        {
            cityReadFailedFor = save;
            cityReadFailures = 0;
        }
        cityReadFailures++;
        if (cityReadFailures < CityReadAttempts)
        {
            OnServerTrouble(null, "reading the Online City accounts, attempt " + cityReadFailures + " of " + CityReadAttempts +
                                  " (the local save stays until the server's save can be loaded with its city money)");
            return false;
        }
        Debug.LogError("[CloudSave] The Online City accounts couldn't be read " + cityReadFailures + " times for the server's save (run " +
                       Short(meta.RunId) + " seq " + meta.Seq + "): loading it WITHOUT the city money that moved since it was accepted.");
        cityReadFailedFor = null;
        cityReadFailures = 0;
        return true;
    }

    /// <summary>AdoptServerSave would load the server's save (rather than keep or close the local one).</summary>
    static bool WillLoadServerSave(CloudSnapshot server)
    {
        SaveMeta meta = server != null ? server.Meta : null;
        if (meta == null || meta.Ended || server.DataBroken || string.IsNullOrEmpty(server.SaveJson)) return false;
        SaveData data = server.Data ?? ParseSave(server.SaveJson);
        return data != null && data.currentDay > 0;
    }

    /// <summary>The player's Online City money from the server; null when it couldn't be read (logged).</summary>
    async Task<CityMoney> ReadCityMoney()
    {
        try
        {
            CityMoney money = await backend.LoadCityMoney();
            if (money == null) throw new InvalidOperationException("no answer");
            return money;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[CloudSave] Couldn't read the Online City accounts (" + PlayerAccount.Describe(e) + ").");
            return null;
        }
    }

    /// <summary>
    /// Make the server's save the local one, or close the local one when the server's current run is over or is another
    /// company without a save yet. Never drops the local save for a server copy that can't be read, nor when the server
    /// has no save of the local save's own run to fall back to. A running company goes back to the main menu first. A
    /// loaded save gets the Online City money that moved since the server accepted it before it is written (SettleCityMoney;
    /// money = the player's city accounts, null = load without; pending = the save's pending transfers as the server reads
    /// them).
    /// </summary>
    void AdoptServerSave(CloudSnapshot server, string toastKey, string keepAs, CityMoney money, List<SavePendingTransfer> pending)
    {
        SaveMeta meta = server != null ? server.Meta : null;
        if (meta == null)
        {
            Log("The server has no save to load: the local save stays.");
            needsReconcile = true;
            retryAt = Time.realtimeSinceStartup + 30f;
            return;
        }
        LocalSave local = LocalSave.Read();
        if (meta.Ended)
        {
            string key = null;
            if (local != null)
                key = local.RunId == meta.RunId ? ToastEndedElsewhere : ToastNewRunElsewhere;
            DiscardLocal(key, meta, keepAs == "replaced" ? "discarded" : keepAs);
            return;
        }
        if (server.DataBroken)
        {
            Debug.LogError("[CloudSave] The server's save couldn't be read: the local save stays.");
            OnServerTrouble(null, "reading the server's save");
            return;
        }
        string json = server.SaveJson;
        if (string.IsNullOrEmpty(json))
        {
            if (local != null && local.RunId == meta.RunId)
            {
                // No save of this run on the server to fall back to (a run registered by NewRun, nothing accepted yet):
                // this PC holds the only copy
                if (toastKey == ToastRejected)
                    BlockUploads("The server refused this run's save and has no save of it to fall back to: the local save stays; " +
                                 "uploads stop until the next start.");
                else
                    OnServerTrouble(null, "the server has no save of run " + Short(meta.RunId) + " to load (the local save stays)");
                return;
            }
            DiscardLocal(local != null ? (toastKey == ToastSuperseded ? ToastSuperseded : ToastNewRunElsewhere) : null, meta,
                         keepAs == "replaced" ? "discarded" : keepAs);
            return;
        }

        SaveData data = server.Data ?? ParseSave(json);
        if (data == null || data.currentDay <= 0)
        {
            Debug.LogError("[CloudSave] The server's save couldn't be read: the local save stays.");
            OnServerTrouble(null, "download");
            return;
        }
        if (!string.IsNullOrEmpty(data.runId) && data.runId != meta.RunId)
            Debug.LogWarning("[CloudSave] The server's save says run " + Short(data.runId) + " but its meta says " + Short(meta.RunId) + ".");

        bool inGame = CompanyManager.Instance != null;
        bool suspended = inGame && SuspendSavingUntilNextScene();
        // The Online City money that moved after the server accepted this save (by a copy that is gone now) goes into it
        // before anything is written: one write, so a failed one leaves the local save and the sync state as they were
        CitySettlement settlement = SettleCityMoney(meta, data, money, pending);
        string text = settlement != null ? JsonUtility.ToJson(data, false) : json;
        // A local save with progress the server doesn't have (another run, unsent writes, or a chain this PC can't place)
        // is kept as a file before it is replaced (.prev alone is gone with the next write)
        if (local != null && (local.RunId != meta.RunId || local.Seq > state.baseSeq || state.runId != local.RunId))
            KeepCopy(keepAs, local);
        if (!SaveSystem.WriteLocalSaveText(text))
        {
            if (suspended) ResumeSavingNow(); // the running company is still the local save
            OnServerTrouble(null, "writing the downloaded save");
            return;
        }

        if (!string.IsNullOrEmpty(CurrentPlayerId)) state.ownerId = CurrentPlayerId;
        state.runId = meta.RunId;
        state.baseSeq = meta.Seq;
        state.ledgerIndex = meta.LedgerIndex;
        state.lastAcceptedAt = meta.AcceptedAt;
        state.pendingNewRun = false;
        state.lastSyncAt = SaveCloud.UnixNow();
        state.knownAcceptedAt = meta.AcceptedAt;
        state.inFlightRun = null;
        state.inFlightSeq = 0;
        state.inFlightHash = null;
        SaveState();
        ledger.ResetForRun(meta.RunId, meta.LedgerIndex);
        lastLocalRunId = meta.RunId;
        lastLocalSeq = Math.Max(meta.Seq, data.seq);
        dirty = false;
        lastServerMeta = meta;
        ResetUploadBlocks(); // another save: its own size and answers
        if (settlement != null)
        {
            NotifyLocalWrite(false); // the settled save is a local write on top of the server's: it goes up with the next upload
            LogCitySettlement(settlement, data);
        }
        // The city state and the company name come from the save as it was written
        WriteCompanyName(data.companyName);
        ReloadPersistentParticipants(data);

        Log("Loaded the server's save: run " + Short(meta.RunId) + ", seq " + meta.Seq + ", day " + data.currentDay + ", " +
            UIFormat.Money(data.companyValue) + (local != null ? " (replaced the local run " + Short(local.RunId) + " seq " + local.Seq + ")." : "."));
        QueueToast(toastKey, toastKey == ToastRejected ? NotificationKind.Bad : toastKey == ToastRestored ? NotificationKind.Good : NotificationKind.Info);
        RaiseLocalSaveChanged();
        LeaveGameIfRunning(inGame);
    }

    /// <summary>
    /// Close the local save (a copy is kept: savegame_discarded_*.json, the newest three): its run ended, or another company
    /// replaced it. The chain then has no run (the next company gets its own), and the Online City state is emptied.
    /// </summary>
    void DiscardLocal(string toastKey, SaveMeta meta, string keepAs = "discarded")
    {
        LocalSave local = LocalSave.Read();
        bool inGame = CompanyManager.Instance != null;
        if (inGame) SuspendSavingUntilNextScene();
        if (local != null)
            KeepCopy(keepAs, local);
        SaveSystem.DeleteLocalSaveFiles();
        DeleteCompanyPrefs();
        PlayerPrefs.Save();
        ReloadPersistentParticipants(null); // no save: an empty Online City state (nothing is carried over)

        // No run for this chain: the server's run is either over or a company this PC has no save for
        state.runId = null;
        state.baseSeq = 0;
        state.ledgerIndex = 0;
        state.pendingNewRun = false;
        state.lastSyncAt = SaveCloud.UnixNow();
        state.inFlightRun = null;
        state.inFlightSeq = 0;
        state.inFlightHash = null;
        SaveState();
        ledger.ResetForRun(null, 0);
        lastLocalRunId = null;
        lastLocalSeq = 0;
        dirty = false;
        lastServerMeta = meta;
        ResetUploadBlocks();

        if (local != null)
        {
            Log("Closed the local save (run " + Short(local.RunId) + "): " + (meta != null && meta.Ended ? "its run is over on the server." : "the server's current run is another company without a save yet."));
            if (toastKey != null) QueueToast(toastKey, toastKey == ToastEndedElsewhere ? NotificationKind.Bad : NotificationKind.Info);
            RaiseLocalSaveChanged();
        }
        LeaveGameIfRunning(inGame);
    }

    /// <summary>
    /// Whose save is this? A sync state of another player (by player id) or of another Steam user (by Steam ID) goes aside
    /// and this player's comes back; a local save of someone else is set aside (savegame_&lt;playerId&gt;.json, another Steam
    /// user's savegame_steam_&lt;steamId&gt;.json) and this player's set-aside save comes back; a save nobody owns yet becomes
    /// this player's. The company PlayerPrefs follow the local save. True when a running company had to go back to the menu.
    /// </summary>
    bool ApplyOwnership(string me)
    {
        string mySteamId = CurrentSteamId;
        bool inGame = CompanyManager.Instance != null;
        LocalSave local = LocalSave.Read();
        string otherOwner = local != null ? OtherOwnerKey(local.OwnerId, local.OwnerSteamId, me, mySteamId) : null;

        List<CloudSyncState.PrefValue> ownPrefs = null;
        bool prefsLeft = false;
        if (IsOtherOwnersState(state, me, mySteamId))
        {
            string previous = StateKeyOf(state, mySteamId);
            // The company PlayerPrefs leave with that state when its company leaves too (or there is none)
            prefsLeft = local == null || otherOwner != null;
            ownPrefs = SwitchSyncState(me, mySteamId, prefsLeft);
            Log("Player " + Short(me) + " signed in: the sync state of " + previous + " was set aside.");
        }
        else if (state.ownerId != me || (mySteamId != null && state.ownerSteamId != mySteamId))
        {
            state.ownerId = me;
            if (mySteamId != null) state.ownerSteamId = mySteamId;
            SaveState();
        }

        bool changed = false;
        bool leftGame = false;
        if (otherOwner != null)
        {
            bool suspended = inGame && SuspendSavingUntilNextScene();
            if (SetAside(local, otherOwner))
            {
                QueueToast(ToastOtherAccount, NotificationKind.Info);
                changed = true;
                local = null;
                if (inGame)
                {
                    leftGame = LeaveGameIfRunning(true);
                    inGame = false;
                }
            }
            else if (suspended)
            {
                ResumeSavingNow(); // the save stays where it is, and so does its running company
            }
        }

        if (local == null && !inGame)
        {
            // This player's set-aside save comes back (the newer of the player-id and Steam-ID copies)
            if (RestoreSetAsideSave(me, mySteamId))
                changed = true;
        }
        else if (local != null && otherOwner == null && string.IsNullOrEmpty(local.OwnerId))
        {
            // A save nobody owned yet (made offline, or before the cloud save): this player's now
            local.Data.ownerId = me;
            if (string.IsNullOrEmpty(local.Data.ownerSteamId) && mySteamId != null) local.Data.ownerSteamId = mySteamId;
            SaveSystem.WriteLocalSaveText(JsonUtility.ToJson(local.Data, false));
        }

        // The company PlayerPrefs follow the local save (the next user of a shared PC doesn't see the last one's company)
        if (changed || prefsLeft)
            ApplyCompanyPrefs(ownPrefs);
        if (changed)
        {
            ledger.LoadFromLocalSave();
            LocalSave now = LocalSave.Read();
            ReloadPersistentParticipants(now != null ? now.Data : null);
            RaiseLocalSaveChanged();
        }
        return leftGame;
    }

    /// <summary>Boot: a save from before the cloud save gets a run id (seq 0); its first upload imports it.</summary>
    void MigrateLocalSave()
    {
        LocalSave local = LocalSave.Read();
        if (local == null || !string.IsNullOrEmpty(local.RunId)) return;
        local.Data.runId = NewRunId();
        local.Data.seq = 0;
        if (!SaveSystem.WriteLocalSaveText(JsonUtility.ToJson(local.Data, false))) return;
        if (string.IsNullOrEmpty(state.runId))
        {
            state.runId = local.Data.runId;
            state.baseSeq = 0;
            state.ledgerIndex = 0;
            state.pendingNewRun = false;
            SaveState();
        }
        Log("The local save is from before the cloud save: it is run " + Short(local.Data.runId) + " now (its first upload imports it).");
    }

    /// <summary>
    /// Move the local save aside as savegame_&lt;ownerKey&gt;.json (restored when that player signs in here). An earlier
    /// set-aside save of the same owner is never overwritten: it is renamed with a timestamp first.
    /// </summary>
    bool SetAside(LocalSave local, string ownerKey)
    {
        string path = SetAsideSavePath(ownerKey);
        try
        {
            if (File.Exists(path))
                File.Move(path, UniqueFilePath(Path.GetFileNameWithoutExtension(path) + "_older"));
            CloudSyncState.WriteAtomically(path, local.Text);
            SaveSystem.DeleteLocalSaveFiles();
            Log("Set aside the local save of " + ownerKey + " (run " + Short(local.RunId) + ", seq " + local.Seq + ") as " + Path.GetFileName(path) + ".");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError("[CloudSave] Couldn't set the local save aside: " + e.Message);
            return false;
        }
    }

    /// <summary>
    /// Bring back a set-aside save of this player: the newer of savegame_&lt;playerId&gt;.json and savegame_steam_&lt;id&gt;.json
    /// (the other is kept under a timestamped name, so it neither blocks nor gets lost). False when there is none.
    /// </summary>
    bool RestoreSetAsideSave(string playerId, string steamId)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrEmpty(playerId)) candidates.Add(SetAsideSavePath(playerId));
        if (!string.IsNullOrEmpty(steamId)) candidates.Add(SetAsideSavePath("steam_" + steamId));
        string newest = null;
        foreach (string path in candidates)
        {
            if (!File.Exists(path)) continue;
            if (newest == null || File.GetLastWriteTimeUtc(path) > File.GetLastWriteTimeUtc(newest)) newest = path;
        }
        if (newest == null) return false;
        try
        {
            if (!SaveSystem.WriteLocalSaveText(File.ReadAllText(newest))) return false;
            TryDelete(newest);
            foreach (string path in candidates)
            {
                if (path != newest && File.Exists(path))
                    File.Move(path, UniqueFilePath(Path.GetFileNameWithoutExtension(path) + "_older"));
            }
            Log("Brought back this player's set-aside save (" + Path.GetFileName(newest) + ").");
            AdoptRestoredChain();
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[CloudSave] Couldn't bring back the set-aside save: " + e.Message);
            return false;
        }
    }

    /// <summary>
    /// A restored save whose run the sync state doesn't know continues its own chain from the base stamped into it
    /// (SaveData.baseSeq): its progress can then go up instead of the server's copy replacing it (the server checks that base).
    /// </summary>
    void AdoptRestoredChain()
    {
        LocalSave restored = LocalSave.Read();
        if (restored == null || string.IsNullOrEmpty(restored.RunId) || restored.RunId == state.runId) return;
        state.runId = restored.RunId;
        state.baseSeq = Math.Max(0L, restored.Data.baseSeq);
        state.ledgerIndex = 0;
        state.pendingNewRun = false;
        SaveState();
        lastLocalRunId = null;
        lastLocalSeq = 0;
        Log("The restored save continues run " + Short(restored.RunId) + " from server save " + state.baseSeq + " (stamped in the save).");
    }

    /// <summary>
    /// A shared PC (another Steam user on this Windows account): as soon as Steam names the user - offline too, at boot,
    /// before the menu offers Continue or New Game - a sync state of another Steam user is set aside (with the company
    /// PlayerPrefs of its save), and so is a local save of another Steam user (by its Steam ID; one from before Steam IDs
    /// were stamped goes with its player's state); this user's own state and set-aside save come back. Nothing this user
    /// does can then touch the other's company (a New Game can't register over it, a Game Over can't end it).
    /// </summary>
    void SwitchToSteamUsersState()
    {
        string mySteamId = CurrentSteamId;
        if (mySteamId == null) return;
        bool otherState = IsOtherOwnersState(state, null, mySteamId);
        LocalSave local = LocalSave.Read();
        string saveOwner = null;
        if (local != null)
        {
            saveOwner = OtherOwnerKey(local.OwnerId, local.OwnerSteamId, null, mySteamId);
            if (saveOwner == null && otherState && string.IsNullOrEmpty(local.OwnerSteamId) && !string.IsNullOrEmpty(local.OwnerId) &&
                local.OwnerId == state.ownerId)
                saveOwner = local.OwnerId;
        }
        if (!otherState && saveOwner == null) return;

        string previous = otherState ? StateKeyOf(state, mySteamId) : saveOwner;
        bool setAside = saveOwner != null && SetAside(local, saveOwner);
        // The company PlayerPrefs leave with that user's state when their company leaves too (or there is none)
        bool prefsLeft = otherState && (local == null || setAside);
        List<CloudSyncState.PrefValue> ownPrefs = otherState ? SwitchSyncState(null, mySteamId, prefsLeft) : null;
        bool restored = !SaveSystem.LocalSaveExists() && RestoreSetAsideSave(state.ownerId, mySteamId);
        if (setAside || restored || prefsLeft)
            ApplyCompanyPrefs(ownPrefs);
        Log("Steam user " + mySteamId + " on a PC last used by " + previous + ": " +
            (otherState ? (setAside ? "that user's sync state and save were set aside" : "that user's sync state was set aside") : "that user's save was set aside") +
            (restored ? "; this user's set-aside save came back." : "."));
    }

    /// <summary>The sync state belongs to another Steam user (by Steam ID) or another player (by player id; skipped when unknown).</summary>
    static bool IsOtherOwnersState(CloudSyncState st, string me, string mySteamId)
    {
        if (mySteamId != null && !string.IsNullOrEmpty(st.ownerSteamId) && st.ownerSteamId != mySteamId) return true;
        return !string.IsNullOrEmpty(me) && !string.IsNullOrEmpty(st.ownerId) && st.ownerId != me;
    }

    /// <summary>The key a sync state is set aside under: another Steam user's by Steam ID (steam_&lt;id&gt;), else its player id.</summary>
    static string StateKeyOf(CloudSyncState st, string mySteamId)
    {
        if (!string.IsNullOrEmpty(st.ownerSteamId) && st.ownerSteamId != mySteamId) return "steam_" + st.ownerSteamId;
        return !string.IsNullOrEmpty(st.ownerId) ? st.ownerId : "steam_" + st.ownerSteamId;
    }

    /// <summary>
    /// The key a save that isn't this user's is set aside under: another Steam user's by Steam ID (steam_&lt;id&gt;), another
    /// player's by player id. Null when it is this user's, or nobody's yet (an id that isn't known isn't compared).
    /// </summary>
    static string OtherOwnerKey(string ownerId, string ownerSteamId, string me, string mySteamId)
    {
        if (mySteamId != null && !string.IsNullOrEmpty(ownerSteamId) && ownerSteamId != mySteamId) return "steam_" + ownerSteamId;
        if (!string.IsNullOrEmpty(me) && !string.IsNullOrEmpty(ownerId) && ownerId != me) return ownerId;
        return null;
    }

    /// <summary>OtherOwnerKey against this user: the signed-in player (offline: the sync state's) and the running Steam user.</summary>
    string OtherOwnerKeyOf(LocalSave local)
    {
        return OtherOwnerKey(local.OwnerId, local.OwnerSteamId, CurrentPlayerId ?? state.ownerId, CurrentSteamId);
    }

    /// <summary>ApplyOwnership is about to move the local save: another owner's goes aside, or this player's comes back.</summary>
    bool LocalSaveChangesOwner(LocalSave local, string me)
    {
        string mySteamId = CurrentSteamId;
        if (local != null) return OtherOwnerKey(local.OwnerId, local.OwnerSteamId, me, mySteamId) != null;
        return CompanyManager.Instance == null &&
               ((!string.IsNullOrEmpty(me) && File.Exists(SetAsideSavePath(me))) ||
                (mySteamId != null && File.Exists(SetAsideSavePath("steam_" + mySteamId))));
    }

    /// <summary>
    /// Set the current sync state aside for its owner and make this user's own the current one (by player id or Steam ID;
    /// a fresh one when there is none). keepPrefs: the company PlayerPrefs leave with the old owner (their company leaves
    /// too). The new current state is saved first and only then is the set-aside file it came from removed, so a crash in
    /// between keeps a copy rather than none. Returns the company PlayerPrefs kept with this user's state (null: none).
    /// </summary>
    List<CloudSyncState.PrefValue> SwitchSyncState(string me, string mySteamId, bool keepPrefs)
    {
        CloudSyncState previous = state;
        string previousKey = StateKeyOf(previous, mySteamId);
        string previousPath = CloudSyncState.SetAsidePath(StateBaseName, previousKey);
        previous.companyPrefs = keepPrefs ? CaptureCompanyPrefs() : new List<CloudSyncState.PrefValue>();
        if (!previous.Save(previousPath))
            Debug.LogError("[CloudSave] The sync state of " + previousKey + " couldn't be set aside (this user's replaces it).");

        CloudSyncState own = FindSetAsideState(me, mySteamId, previousPath, out string ownPath);
        state = own ?? new CloudSyncState();
        if (!string.IsNullOrEmpty(me)) state.ownerId = me;
        if (mySteamId != null) state.ownerSteamId = mySteamId;
        List<CloudSyncState.PrefValue> ownPrefs = state.companyPrefs != null && state.companyPrefs.Count > 0 ? state.companyPrefs : null;
        state.companyPrefs = new List<CloudSyncState.PrefValue>();
        lastLocalRunId = null;
        lastLocalSeq = 0;
        if (state.Save(StatePath) && ownPath != null)
            TryDelete(ownPath);
        return ownPrefs;
    }

    /// <summary>
    /// This user's set-aside sync state: one whose player id or Steam ID is this user's while its other id doesn't name
    /// someone else (the most recently synced of several); null when there is none. Its file isn't removed here.
    /// </summary>
    CloudSyncState FindSetAsideState(string me, string mySteamId, string excludePath, out string path)
    {
        path = null;
        CloudSyncState best = null;
        if (string.IsNullOrEmpty(me) && mySteamId == null) return null;
        try
        {
            string baseName = StateBaseName;
            string excluded = excludePath != null ? Path.GetFullPath(excludePath) : null;
            foreach (string file in Directory.GetFiles(Application.persistentDataPath, baseName + "_*.json"))
            {
                string name = Path.GetFileName(file);
                if (baseName == "cloudsync" && name.StartsWith("cloudsync_localtest", StringComparison.Ordinal)) continue;
                if (excluded != null && string.Equals(Path.GetFullPath(file), excluded, StringComparison.OrdinalIgnoreCase)) continue;
                CloudSyncState candidate = CloudSyncState.Load(file);
                if (candidate == null) continue;
                bool idMatches = !string.IsNullOrEmpty(me) && candidate.ownerId == me;
                bool steamMatches = mySteamId != null && candidate.ownerSteamId == mySteamId;
                if (!idMatches && !steamMatches) continue;
                if (!string.IsNullOrEmpty(me) && !string.IsNullOrEmpty(candidate.ownerId) && candidate.ownerId != me) continue;
                if (mySteamId != null && !string.IsNullOrEmpty(candidate.ownerSteamId) && candidate.ownerSteamId != mySteamId) continue;
                if (best == null || candidate.lastSyncAt > best.lastSyncAt)
                {
                    best = candidate;
                    path = file;
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[CloudSave] Couldn't look for this user's set-aside sync state: " + e.Message);
        }
        return best;
    }

    /// <summary>The sync state belongs to the Steam user writing here when it has no owner yet (or this signed-in player's).</summary>
    void BindStateToSteamUser(string steamId, string playerId)
    {
        if (steamId == null || !string.IsNullOrEmpty(state.ownerSteamId)) return;
        if (!string.IsNullOrEmpty(state.ownerId) && state.ownerId != playerId) return;
        state.ownerSteamId = steamId;
        SaveState();
    }

    /// <summary>A path in persistentDataPath for &lt;stem&gt;_&lt;utc time&gt;.json that doesn't exist yet.</summary>
    static string UniqueFilePath(string stem)
    {
        string folder = Application.persistentDataPath;
        string baseName = stem + "_" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string path = Path.Combine(folder, baseName + ".json");
        for (int n = 2; File.Exists(path); n++)
            path = Path.Combine(folder, baseName + "_" + n + ".json");
        return path;
    }

    void ResetUploadBlocks()
    {
        uploadsBlocked = false;
        saveTooBig = false;
        rejections = 0;
    }

    /// <summary>
    /// Keep a copy of a local save the sync is about to replace or close: savegame_&lt;kind&gt;_&lt;run8&gt;_&lt;seq&gt;.json
    /// (kinds: replaced, discarded, offline), the newest KeptCopies of each kind.
    /// </summary>
    void KeepCopy(string kind, LocalSave local)
    {
        if (local == null || string.IsNullOrEmpty(local.Text)) return;
        string folder = Application.persistentDataPath;
        string prefix = "savegame_" + kind + "_";
        // Timestamped and never overwriting: the same run and seq can be kept twice (after a download of the same save)
        string path = UniqueFilePath(prefix + CloudSyncState.SafeFilePart(Short(local.RunId)) + "_" + local.Seq);
        try
        {
            CloudSyncState.WriteAtomically(path, local.Text);
            Log("Kept the local save (run " + Short(local.RunId) + ", seq " + local.Seq + ") as " + Path.GetFileName(path) + ".");
            var copies = new List<FileInfo>();
            foreach (string file in Directory.GetFiles(folder, prefix + "*.json"))
                copies.Add(new FileInfo(file));
            copies.Sort((a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
            for (int i = KeptCopies; i < copies.Count; i++)
                TryDelete(copies[i].FullName);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[CloudSave] Couldn't keep a copy of the local save: " + e.Message);
        }
    }

    /// <summary>CityOnline's saved state, as its JSON reads (unlock, certificate, transfers on their way).</summary>
    [Serializable]
    class CityStateCopy
    {
        public bool unlocked;
        public bool certificateShown;
        public List<CityOnline.PendingTransfer> pending = new List<CityOnline.PendingTransfer>();
    }

    /// <summary>
    /// After the local save was replaced, closed or changed owner: the persistent systems that keep state in the save (the
    /// Online City) take the new save's state, so the next save doesn't write the old company's city state into the new one.
    /// Transfers come only from the save being loaded - nothing is carried over from the copy it replaces (what that copy's
    /// transfers moved is in the server's city totals: SettleCityMoney) - and only the current account's (another account's
    /// transfer can't be finished here: dropped, logged). Null = no save any more: an empty city state.
    /// </summary>
    static void ReloadPersistentParticipants(SaveData newSave)
    {
        CityOnline city = CityOnline.Instance;
        if (city == null) return;
        try
        {
            CityStateCopy loaded = newSave != null ? CityStateOf(newSave, CurrentCityPlayerId(), true) : new CityStateCopy();
            city.RestoreState(JsonUtility.ToJson(loaded));
        }
        catch (Exception e)
        {
            Debug.LogWarning("[CloudSave] Couldn't set the Online City state for the loaded save: " + e.Message);
        }
    }

    /// <summary>
    /// The Online City state a save brings: its unlock and certificate, and its transfers still on their way - without the
    /// ones another account started (only that account can finish them) when the current account is known.
    /// </summary>
    static CityStateCopy CityStateOf(SaveData save, string player, bool logDropped)
    {
        CityStateCopy copy = ParseCityState(FindExtra(save, CitySaveKey)) ?? new CityStateCopy();
        if (copy.pending == null) copy.pending = new List<CityOnline.PendingTransfer>();
        copy.unlocked = copy.unlocked || save.hasUnlockedOnlineWorld;
        for (int i = copy.pending.Count - 1; i >= 0; i--)
        {
            CityOnline.PendingTransfer transfer = copy.pending[i];
            if (transfer != null && (string.IsNullOrEmpty(player) || string.IsNullOrEmpty(transfer.player) || transfer.player == player)) continue;
            if (transfer != null && logDropped)
                Debug.LogWarning("[CloudSave] Not carrying a city " + transfer.kind + " of another account into the loaded save (" + transfer.opId + ", " +
                                 transfer.amount + (transfer.kind == "deposit" ? " to " : " from ") + transfer.city + "): only that account can finish it.");
            copy.pending.RemoveAt(i);
        }
        return copy;
    }

    /// <summary>The account Online City transfers are finished under now: the city's, else the one it will use (null = unknown).</summary>
    static string CurrentCityPlayerId()
    {
        try
        {
            CityOnline city = CityOnline.Instance;
            if (city != null && !string.IsNullOrEmpty(city.PlayerId)) return city.PlayerId;
            if (CityOnline.UseLocalTestCity) return CityLocalBackend.LocalPlayerId;
        }
        catch (Exception)
        {
            // The city's services aren't up yet: the sign-in's player below
        }
        return PlayerAccount.IsSignedIn && !string.IsNullOrEmpty(PlayerAccount.PlayerId) ? PlayerAccount.PlayerId : null;
    }

    /// <summary>
    /// Op ids of the city's transfers on their way that the current account can still finish (another account's never
    /// settle here). A city state that can't be read counts as one unknown transfer.
    /// </summary>
    static HashSet<string> OwnCityTransferIds(CityOnline city)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (city == null || !city.HasPendingTransfers) return ids;
        CityStateCopy current = ParseCityState(city.CaptureState());
        if (current == null || current.pending == null)
        {
            ids.Add("?");
            return ids;
        }
        string player = CurrentCityPlayerId();
        foreach (CityOnline.PendingTransfer transfer in current.pending)
        {
            if (transfer != null && (player == null || string.IsNullOrEmpty(transfer.player) || transfer.player == player))
                ids.Add(transfer.opId ?? "");
        }
        return ids;
    }

    /// <summary>The city holds transfers the current account can still finish.</summary>
    static bool HasOwnCityTransfers(CityOnline city)
    {
        return OwnCityTransferIds(city).Count > 0;
    }

    /// <summary>The city is connected or connecting: a transfer on its way can still get its answer.</summary>
    static bool CityCanAnswer(CityOnline city)
    {
        return city != null && (city.IsReady || city.State == CityOnline.Status.Connecting);
    }

    /// <summary>
    /// Before the server's save replaces the local one (or the local save moves to its owner): Online City transfers the
    /// current account can still settle get up to CityTransferHoldSeconds to do so, so the city money settled into the
    /// loaded save (SettleCityMoney) includes them. Transfers settle only while a company is loaded (CityOnline finishes
    /// them only then, connected for that); without one there is nothing to wait for (CityOnline counts no calls in flight).
    /// </summary>
    async Task WaitForCityTransfers(int gen, string what)
    {
        CityOnline city = CityOnline.Instance;
        if (!CompanyLoadedForCity() || !HasOwnCityTransfers(city)) return;
        if (city.State == CityOnline.Status.Idle || city.State == CityOnline.Status.Failed)
            _ = city.Connect(); // a connected city finishes them (CityOnline.FinishPendingTransfers)
        if (!CityCanAnswer(city)) return;

        string limit = CityTransferHoldSeconds.ToString("0", CultureInfo.InvariantCulture);
        Log("Waiting for Online City transfers on their way before " + what + " (up to " + limit + " s).");
        float start = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - start < CityTransferHoldSeconds)
        {
            await Task.Delay(250);
            if (!Alive(gen)) return;
            city = CityOnline.Instance;
            if (!HasOwnCityTransfers(city))
            {
                Log("The Online City transfers settled.");
                return;
            }
            if (!CompanyLoadedForCity() || !CityCanAnswer(city))
            {
                Log("The Online City transfers can't settle now: going ahead with " + what + ".");
                return;
            }
        }
        Debug.LogWarning("[CloudSave] Online City transfers are still on their way after " + limit + " s: going ahead with " + what +
                         " (a transfer the loaded save doesn't have is dropped).");
    }

    /// <summary>A company is loaded: CityOnline finishes transfers only then (its CompanyLoaded).</summary>
    static bool CompanyLoadedForCity()
    {
        SaveSystem save = SaveSystem.Instance;
        return CompanyManager.Instance != null && save != null && save.IsGameLoaded;
    }

    /// <summary>What SettleCityMoney put into a loaded save (for the log).</summary>
    sealed class CitySettlement
    {
        public CityMoneyMath.Result Result;
        public double ValueBefore;
        public string Totals;
    }

    /// <summary>
    /// Before the server's save is written here: the Online City money that moved after it was accepted (by a copy that
    /// is gone now) goes into it (in memory) as a local write on top of it: seq + 1, the chain's base = meta.Seq, uploaded
    /// with the next upload. The rule is the server's (CityMoneyMath over SaveRules.ClassifyPending), so that upload measures
    /// exactly the city's own movement since the accept: the transfers the city booked are settled (they leave the pending
    /// list), everything else stays pending. Null when nothing changes (no money moved, the local test server, no accounts).
    /// </summary>
    CitySettlement SettleCityMoney(SaveMeta meta, SaveData data, CityMoney money, List<SavePendingTransfer> pending)
    {
        if (money == null || !money.Known || meta == null || data == null) return null;
        if (pending == null)
        {
            Debug.LogWarning("[CloudSave] The server's save doesn't read as the server reads it: its pending city transfers are taken from its city state.");
            pending = PendingTransfersOf(ParseCityState(FindExtra(data, CitySaveKey)));
        }
        CityMoneyMath.Result result = CityMoneyMath.Settle(data.companyValue, meta, money.Deposited, money.Withdrawn, pending, money.PlayerId,
                                                           money.Transfers, money.Ops);
        string totals = "city dep/wd " + money.Deposited + "/" + money.Withdrawn + " now, " + meta.CityDeposited + "/" + meta.CityWithdrawn +
                        " at the accept, " + money.Accounts + " accounts";
        if (result.Change == 0.0 && result.Pending.Booked.Count == 0)
        {
            Log("No Online City money moved since the server's save (" + totals + ").");
            return null;
        }

        var settlement = new CitySettlement { Result = result, ValueBefore = data.companyValue, Totals = totals };
        data.companyValue = result.Value;
        // The transfers the city booked are settled; another account's aren't carried (CityStateOf); the rest stay pending
        if (FindExtra(data, CitySaveKey) != null)
        {
            CityStateCopy city = CityStateOf(data, CurrentCityPlayerId(), false);
            var booked = new HashSet<string>(result.Pending.Booked, StringComparer.Ordinal);
            city.pending.RemoveAll(transfer => booked.Contains(transfer.opId));
            SetExtra(data, CitySaveKey, JsonUtility.ToJson(city));
        }
        // A local write on top of the server's save (what StampLocalWrite gives it once the chain is the server's)
        string me = CurrentPlayerId;
        string steamId = CurrentSteamId;
        data.runId = meta.RunId;
        data.seq = Math.Max(meta.Seq, data.seq) + 1;
        data.baseSeq = meta.Seq;
        if (string.IsNullOrEmpty(data.ownerId) && !string.IsNullOrEmpty(me)) data.ownerId = me;
        if (string.IsNullOrEmpty(data.ownerSteamId) && steamId != null && (string.IsNullOrEmpty(data.ownerId) || data.ownerId == me))
            data.ownerSteamId = steamId;
        data.savedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        return settlement;
    }

    /// <summary>A city state's pending list by SaveSnapshot's rules (only for a save that doesn't parse there).</summary>
    static List<SavePendingTransfer> PendingTransfersOf(CityStateCopy city)
    {
        var list = new List<SavePendingTransfer>();
        if (city == null || city.pending == null) return list;
        foreach (CityOnline.PendingTransfer transfer in city.pending)
        {
            if (transfer == null || string.IsNullOrEmpty(transfer.opId) || transfer.amount <= 0 || list.Count >= 1000) continue;
            list.Add(new SavePendingTransfer
            {
                OpId = transfer.opId, Deposit = transfer.kind == "deposit", Amount = transfer.amount, City = transfer.city,
                Player = string.IsNullOrEmpty(transfer.player) ? null : transfer.player
            });
        }
        return list;
    }

    void LogCitySettlement(CitySettlement settlement, SaveData data)
    {
        CityMoneyMath.Result result = settlement.Result;
        Log("Online City money since the server's save: " + Signed(result.Change) + " (city " + Signed(result.CityNet) + ", in transit at the accept " +
            Signed(result.InTransitAtAccept) + ", deposits still unbooked " + Signed(-result.Pending.UnbookedDeposits) + ", " + result.Pending.Booked.Count +
            " booked transfers settled; " + settlement.Totals + "): " + UIFormat.Money(settlement.ValueBefore) + " -> " + UIFormat.Money(data.companyValue) +
            ", written as seq " + data.seq + " (it goes up with the next upload).");
        if (result.Wanted < 0.0)
            Debug.LogWarning("[CloudSave] The loaded save can't cover the city money that left it: " + UIFormat.Money(-result.Wanted) +
                             " more than it had (it stays at $0).");
    }

    static string Signed(double amount)
    {
        return (amount < 0 ? "-" : "+") + UIFormat.Money(Math.Abs(amount));
    }

    /// <summary>
    /// The Online City money a save loaded from the server is missing, by the rule the server's growth check uses (plain C#
    /// over the shared code: no Unity, no CityOnline). The server measures growth on the effective value = value + the money
    /// in transit that is still the company's (unbooked pending deposits + booked pending withdrawals). With the server's
    /// save S, its meta M (the in-transit sums the server classified at the accept) and one read R of the city accounts:
    ///   value' = max(0, S.value + cityNet + M.PendingDepositsUnbooked + M.PendingWithdrawalsBooked - c.UnbookedDeposits),
    ///   cityNet = max(0, wd_now - M.CityWithdrawn) - max(0, dep_now - M.CityDeposited), c = SaveRules.ClassifyPending(S.pending, me, R).
    /// The transfers c books are settled (a booked withdrawal is paid through M's sum or cityNet, a booked deposit already
    /// left S) and leave the pending list; everything else stays pending.
    /// </summary>
    internal static class CityMoneyMath
    {
        public sealed class Result
        {
            /// <summary>The save's value after the settlement (never below 0); what it would be without that floor; the change.</summary>
            public double Value;
            public double Wanted;
            public double Change;
            /// <summary>Withdrawals minus deposits since the accept (the read's lifetime totals against the meta's).</summary>
            public long CityNet;
            /// <summary>The money in transit at the accept as the server classified it (M.PendingDepositsUnbooked + M.PendingWithdrawalsBooked).</summary>
            public long InTransitAtAccept;
            /// <summary>The save's pending transfers against this read (SaveRules.ClassifyPending): its Booked op ids are settled.</summary>
            public SavePendingCheck Pending;
        }

        public static Result Settle(double value, SaveMeta meta, long depositedNow, long withdrawnNow, IEnumerable<SavePendingTransfer> pending,
                                    string playerId, IEnumerable<SaveCityTransfer> stamps, IEnumerable<string> ops)
        {
            var result = new Result
            {
                CityNet = Math.Max(0L, withdrawnNow - meta.CityWithdrawn) - Math.Max(0L, depositedNow - meta.CityDeposited),
                InTransitAtAccept = Saturated(meta.PendingDepositsUnbooked, meta.PendingWithdrawalsBooked),
                Pending = SaveRules.ClassifyPending(pending, playerId, stamps, ops)
            };
            // Whole dollars, exact as doubles: no change means exactly the save's value
            result.Change = (double)result.CityNet + result.InTransitAtAccept - result.Pending.UnbookedDeposits;
            result.Wanted = value + result.Change;
            result.Value = Math.Max(0.0, result.Wanted);
            return result;
        }

        /// <summary>SaveRules' saturating sum (PendingBefore).</summary>
        static long Saturated(long a, long b)
        {
            long sum = a + b;
            return a > 0 && b > 0 && sum < 0 ? long.MaxValue : sum;
        }
    }

    static CityStateCopy ParseCityState(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonUtility.FromJson<CityStateCopy>(json); }
        catch (Exception) { return null; }
    }

    static string FindExtra(SaveData data, string key)
    {
        if (data == null || data.extras == null) return null;
        foreach (ExtraSaveEntry extra in data.extras)
            if (extra != null && extra.key == key) return extra.json;
        return null;
    }

    static void SetExtra(SaveData data, string key, string json)
    {
        if (data.extras == null) data.extras = new List<ExtraSaveEntry>();
        foreach (ExtraSaveEntry extra in data.extras)
        {
            if (extra != null && extra.key == key)
            {
                extra.json = json;
                return;
            }
        }
        data.extras.Add(new ExtraSaveEntry(key, json));
    }

    /// <summary>Forget the upload in flight (answered, or the chain changed).</summary>
    void ClearInFlight()
    {
        if (string.IsNullOrEmpty(state.inFlightRun) && string.IsNullOrEmpty(state.inFlightHash) && state.inFlightSeq == 0) return;
        state.inFlightRun = null;
        state.inFlightSeq = 0;
        state.inFlightHash = null;
        SaveState();
    }

    /// <summary>Remember the newest server AcceptedAt seen (NewRun's knownAcceptedAt for a company started later).</summary>
    void NoteServerMeta(SaveMeta meta)
    {
        if (meta == null || state.knownAcceptedAt == meta.AcceptedAt) return;
        state.knownAcceptedAt = meta.AcceptedAt;
        SaveState();
    }

    static bool IsFinalSubmitCode(string code)
    {
        return code == SaveCodes.Accepted || code == SaveCodes.Stale || code == SaveCodes.NoRun || code == SaveCodes.Ended ||
               code == SaveCodes.Time || code == SaveCodes.Invalid;
    }



    /// <summary>
    /// True while uploads wait for an Online City transfer on its way (at most CityTransferHoldSeconds per wait, then they
    /// go anyway: the city may not be connected this session). Another account's transfers never settle here: no wait.
    /// </summary>
    bool CityTransferHold()
    {
        if (!HasOwnCityTransfers(CityOnline.Instance))
        {
            cityHoldSince = -1f;
            return false;
        }
        float now = Time.realtimeSinceStartup;
        if (cityHoldSince < 0f)
        {
            cityHoldSince = now;
            Log("An Online City transfer is on its way: uploads wait for it (up to " + CityTransferHoldSeconds.ToString("0", CultureInfo.InvariantCulture) + " s).");
        }
        return now - cityHoldSince < CityTransferHoldSeconds;
    }

    /// <summary>CityTransferHold without starting the wait (for the quit).</summary>
    bool CityTransferHoldActive()
    {
        return HasOwnCityTransfers(CityOnline.Instance) && (cityHoldSince < 0f || Time.realtimeSinceStartup - cityHoldSince < CityTransferHoldSeconds);
    }

    static void WriteCompanyName(string companyName)
    {
        if (string.IsNullOrWhiteSpace(companyName) || companyName.Trim() == DefaultCompanyName)
        {
            PlayerPrefs.DeleteKey(CompanyNameKey);
            PlayerPrefs.DeleteKey(HasSetCompanyNameKey);
        }
        else
        {
            PlayerPrefs.SetString(CompanyNameKey, companyName.Trim());
            PlayerPrefs.SetInt(HasSetCompanyNameKey, 1);
            CompanyNameUI.ApplyRestoredName(companyName);
        }
        PlayerPrefs.Save();
    }

    /// <summary>The company PlayerPrefs as they are now (with their types), to keep with a sync state that goes aside.</summary>
    static List<CloudSyncState.PrefValue> CaptureCompanyPrefs()
    {
        var prefs = new List<CloudSyncState.PrefValue>();
        foreach (string key in CompanyPrefKeys)
        {
            CloudSyncState.PrefValue value = ReadPref(key);
            if (value != null) prefs.Add(value);
        }
        return prefs;
    }

    /// <summary>
    /// One PlayerPrefs value with its type (null when unset): the company name keys by their known types, the legacy keys
    /// (nothing writes them any more) by trying each type.
    /// </summary>
    static CloudSyncState.PrefValue ReadPref(string key)
    {
        if (!PlayerPrefs.HasKey(key)) return null;
        var value = new CloudSyncState.PrefValue { key = key };
        if (key == CompanyNameKey)
        {
            value.kind = CloudSyncState.PrefValue.Text;
            value.text = PlayerPrefs.GetString(key, "");
            return value;
        }
        if (key == HasSetCompanyNameKey)
        {
            value.kind = CloudSyncState.PrefValue.Int;
            value.number = PlayerPrefs.GetInt(key, 0);
            return value;
        }
        const string NoText = "<no text value>";
        string text = PlayerPrefs.GetString(key, NoText);
        if (text != NoText)
        {
            value.kind = CloudSyncState.PrefValue.Text;
            value.text = text;
            return value;
        }
        int low = PlayerPrefs.GetInt(key, int.MinValue);
        if (low == PlayerPrefs.GetInt(key, int.MaxValue))
        {
            value.kind = CloudSyncState.PrefValue.Int;
            value.number = low;
            return value;
        }
        float real = PlayerPrefs.GetFloat(key, float.NaN);
        if (float.IsNaN(real)) return null;
        value.kind = CloudSyncState.PrefValue.Float;
        value.real = real;
        return value;
    }

    static void DeleteCompanyPrefs()
    {
        foreach (string key in CompanyPrefKeys)
            PlayerPrefs.DeleteKey(key);
    }

    /// <summary>
    /// After the local save changed owner: the company PlayerPrefs follow it - none without a save; else the owner's own
    /// (kept with their sync state while they were away) and the save's company name.
    /// </summary>
    static void ApplyCompanyPrefs(List<CloudSyncState.PrefValue> ownPrefs)
    {
        DeleteCompanyPrefs();
        LocalSave now = LocalSave.Read();
        if (now != null)
        {
            if (ownPrefs != null)
            {
                foreach (CloudSyncState.PrefValue pref in ownPrefs)
                {
                    if (pref == null || Array.IndexOf(CompanyPrefKeys, pref.key) < 0) continue;
                    if (pref.kind == CloudSyncState.PrefValue.Text) PlayerPrefs.SetString(pref.key, pref.text ?? "");
                    else if (pref.kind == CloudSyncState.PrefValue.Int) PlayerPrefs.SetInt(pref.key, pref.number);
                    else if (pref.kind == CloudSyncState.PrefValue.Float) PlayerPrefs.SetFloat(pref.key, pref.real);
                }
            }
            string name = now.Data.companyName;
            if (!string.IsNullOrWhiteSpace(name) && name.Trim() != DefaultCompanyName)
            {
                WriteCompanyName(name); // the save's name wins (this saves the prefs)
                return;
            }
        }
        PlayerPrefs.Save();
    }

    /// <summary>With a company running: back to the main menu (saving stays off until then). True when it left.</summary>
    bool LeaveGameIfRunning(bool inGame)
    {
        if (!inGame || quitHoldStarted) return false;
        sceneChangePending = true; // toasts wait for the menu
        if (!SceneFlow.Load(SceneFlow.MainMenuScene))
        {
            sceneChangePending = false;
            return false;
        }
        return true;
    }

    /// <summary>Saving off until the next scene loads (a running company mustn't overwrite what the sync did). True when this switched it off.</summary>
    bool SuspendSavingUntilNextScene()
    {
        SaveSystem save = SaveSystem.Instance;
        if (save == null || save.SavingSuspended) return false;
        save.SuspendSaving();
        resumeSavingOnNextScene = !quitHoldStarted;
        return true;
    }

    /// <summary>Undo SuspendSavingUntilNextScene when the change it guarded didn't happen (the running company keeps saving).</summary>
    void ResumeSavingNow()
    {
        resumeSavingOnNextScene = false;
        SaveSystem save = SaveSystem.Instance;
        if (save != null && save.SavingSuspended) save.ResumeSaving();
    }

    // ------------------------------------------------------------------
    // Quit and game load waits
    // ------------------------------------------------------------------

    bool OnWantsToQuit()
    {
        // The Editor never holds (Application.Quit does nothing there); a second quit request while holding goes through
        if (quitReady || quitHoldStarted || Application.isEditor) return true;
        if (!NeedsQuitFlush()) return true;
        quitHoldStarted = true;
        StartCoroutine(QuitFlush());
        return false;
    }

    bool NeedsQuitFlush()
    {
        if (backend == null || !backend.CanCall || !IdentityAllowed || serverTrouble || uploadsBlocked) return false;
        if (OpRunning || state.pendingEndRun || state.pendingNewRun) return true;
        if (CityTransferHoldActive()) return false; // that save waits for the transfer; the next start uploads it
        SaveSystem save = SaveSystem.Instance;
        if (save != null && !save.SavingSuspended && CompanyManager.Instance != null && save.IsGameLoaded) return true;
        return dirty;
    }

    IEnumerator QuitFlush()
    {
        Log("Holding the quit (up to " + QuitHoldSeconds.ToString("0", CultureInfo.InvariantCulture) + " s) for the last upload.");
        // The final save (SaveSystem saves once more when the game really quits; the next start uploads that one)
        SaveSystem save = SaveSystem.Instance;
        if (save != null && !save.SavingSuspended && CompanyManager.Instance != null && save.IsGameLoaded)
            save.SaveGame();
        nextUploadAt = 0f;
        retryAt = 0f;
        float deadline = Time.realtimeSinceStartup + QuitHoldSeconds;
        yield return null;
        while (Time.realtimeSinceStartup < deadline)
        {
            bool work = backend.CanCall && !uploadsBlocked && !serverTrouble &&
                        (needsReconcile || dirty || state.pendingEndRun || state.pendingNewRun);
            if (!OpRunning && !work) break;
            yield return null;
        }
        if (OpRunning) Log("The upload didn't finish in time: the next start sends it.");
        quitReady = true;
        Application.Quit();
    }

    bool SyncPendingForGameLoad()
    {
        if (OpRunning) return true;
        if (backend == null) return false;
        if (PlayerAccount.IsSigningIn) return true;
        return backend.CanCall && (needsReconcile || state.pendingEndRun || state.pendingNewRun) && Time.realtimeSinceStartup >= retryAt;
    }

    IEnumerator LoadGameWhenSynced(string sceneName)
    {
        waitingToLoadGame = true;
        bool hadSave = SaveSystem.LocalSaveExists();
        float start = Time.realtimeSinceStartup;
        bool toastShown = false;
        if (SyncPendingForGameLoad())
            Log("Loading the game waits for the cloud sync (up to " + GameLoadWaitSeconds.ToString("0", CultureInfo.InvariantCulture) + " s).");
        while (Time.realtimeSinceStartup - start < GameLoadWaitSeconds && SyncPendingForGameLoad())
        {
            if (!toastShown && Time.realtimeSinceStartup - start > ToastSettleSeconds)
            {
                toastShown = true;
                GameNotifications.Show(L10n.Get(Table, "cloud.status.syncing"), NotificationKind.Info);
            }
            yield return null;
        }
        waitingToLoadGame = false;

        if (!SaveSystem.LocalSaveExists())
        {
            // Nothing to continue - the company went away during the sync (it ended on another PC), or Continue only knew
            // a remembered company name: a fresh menu, never an unplanned new company
            if (!hadSave)
            {
                PlayerPrefs.DeleteKey(CompanyNameKey);
                PlayerPrefs.DeleteKey(HasSetCompanyNameKey);
                PlayerPrefs.Save();
                Log("Continue has no save to load (only a remembered company name): back to the menu.");
            }
            else
            {
                Log("The local save was closed during the sync: back to the menu.");
            }
            sceneChangePending = true;
            SceneFlow.LoadNow(SceneFlow.MainMenuScene);
            yield break;
        }
        SceneFlow.LoadNow(sceneName);
    }

    // ------------------------------------------------------------------
    // Failures and status
    // ------------------------------------------------------------------

    void OnRequestFailed(Exception e)
    {
        failures++;
        float delay = Backoff(failures);
        retryAt = Time.realtimeSinceStartup + delay;
        needsReconcile = true;
        dirty = true;
        serverTrouble = true;
        lastError = PlayerAccount.Describe(e);
        Debug.LogWarning("[CloudSave] " + lastError + " Playing on; trying again in " + delay.ToString("0", CultureInfo.InvariantCulture) + " s.");
        if (PlayerAccount.IsNetworkError(e) && !backend.IsLocalTest)
            QueueOfflineToast();
    }

    void OnServerTrouble(SaveResult result, string what)
    {
        failures++;
        float delay = Backoff(failures);
        retryAt = Time.realtimeSinceStartup + delay;
        needsReconcile = true;
        dirty = true;
        serverTrouble = true;
        lastError = what + ": " + (result == null ? "no usable answer" : result.Code + " (" + result.Message + ")");
        Debug.LogWarning("[CloudSave] " + lastError + ". Trying again in " + delay.ToString("0", CultureInfo.InvariantCulture) + " s.");
    }

    void MarkOffline(string reason)
    {
        needsReconcile = true;
        serverTrouble = true;
        lastError = reason;
        if (retryAt < Time.realtimeSinceStartup + 30f)
            retryAt = Time.realtimeSinceStartup + 30f;
    }

    void OnRequestSucceeded()
    {
        failures = 0;
        serverTrouble = false;
        lastError = null;
    }

    static bool IsFinalAnswer(SaveResult result)
    {
        return result != null && result.Code != SaveCodes.Busy && result.Code != SaveCodes.Error;
    }

    static float Backoff(int failureCount)
    {
        // 30 s, 1, 2, 4, then 5 minutes
        return Mathf.Min(300f, 15f * Mathf.Pow(2f, Mathf.Clamp(failureCount, 1, 5)));
    }

    CloudSyncStatus ComputeStatus()
    {
        if (OpRunning || waitingToLoadGame) return CloudSyncStatus.Syncing;
        if (PlayerAccount.IsSigningIn) return CloudSyncStatus.SigningIn;
        if (backend == null) return CloudSyncStatus.Idle;
        if (!backend.IsLocalTest && PlayerAccount.IsLocalOnly) return CloudSyncStatus.LocalOnly;
        if (!backend.CanCall) return PlayerAccount.IsOffline ? CloudSyncStatus.Offline : CloudSyncStatus.Idle;
        if (saveTooBig) return CloudSyncStatus.TooBig;
        if (uploadsBlocked) return CloudSyncStatus.Pending; // saving on this PC; the log says why
        if (serverTrouble) return CloudSyncStatus.Offline;
        if (needsReconcile || dirty || state.pendingEndRun || state.pendingNewRun) return CloudSyncStatus.Pending;
        return CloudSyncStatus.Synced;
    }

    void CheckStatusChanged()
    {
        CloudSyncStatus status = ComputeStatus();
        if (status == lastStatus) return;
        lastStatus = status;
        Raise(StatusChanged);
    }

    void QueueOfflineToast()
    {
        if (offlineToastShown) return;
        offlineToastShown = true;
        QueueToast(ToastOffline, NotificationKind.Info);
    }

    void QueueToast(string key, NotificationKind kind)
    {
        if (string.IsNullOrEmpty(key)) return;
        foreach (KeyValuePair<string, NotificationKind> queued in pendingToasts)
            if (queued.Key == key) return;
        pendingToasts.Add(new KeyValuePair<string, NotificationKind>(key, kind));
    }

    /// <summary>Toasts wait for the menu or the game to settle (the toast view clears itself on every scene load).</summary>
    void ShowSettledToasts()
    {
        if (pendingToasts.Count == 0 || waitingToLoadGame || sceneChangePending) return;
        if (Time.realtimeSinceStartup - lastSceneLoadedAt < ToastSettleSeconds) return;
        string scene = SceneManager.GetActiveScene().name;
        if (scene != SceneFlow.MainMenuScene && scene != SceneFlow.MainGameScene) return;
        foreach (KeyValuePair<string, NotificationKind> toast in pendingToasts)
            GameNotifications.Show(L10n.Get(Table, toast.Key), toast.Value);
        pendingToasts.Clear();
    }

    void RaiseLocalSaveChanged()
    {
        Raise(LocalSaveChanged);
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

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    /// <summary>The local save as the sync sees it: the exact text and its data. Null when there is none (or it's empty).</summary>
    sealed class LocalSave
    {
        public string Text;
        public SaveData Data;
        public string RunId => Data.runId;
        public long Seq => Data.seq;
        public string OwnerId => Data.ownerId;
        public string OwnerSteamId => Data.ownerSteamId;

        public static LocalSave Read()
        {
            if (!SaveSystem.TryReadLocalSaveText(out string text, out SaveData data) || data == null) return null;
            if (data.currentDay <= 0) return null; // written after its scene closed (older versions): counts as none
            return new LocalSave { Text = text, Data = data };
        }

        /// <summary>The same, read and parsed on a worker thread (big saves would hitch the main thread).</summary>
        public static Task<LocalSave> ReadAsync()
        {
            string path = localSavePath ?? SaveSystem.LocalSaveFilePath;
            return Task.Run(() =>
            {
                if (!SaveSystem.TryReadSaveFileText(path, out string text, out SaveData data) || data == null) return null;
                if (data.currentDay <= 0) return null;
                return new LocalSave { Text = text, Data = data };
            });
        }
    }

    void SaveState()
    {
        state.Save(StatePath);
    }

    bool SavingSuspended()
    {
        return SaveSystem.Instance != null && SaveSystem.Instance.SavingSuspended;
    }

    static string SetAsideSavePath(string ownerId)
    {
        return Path.Combine(Application.persistentDataPath, "savegame_" + CloudSyncState.SafeFilePart(ownerId) + ".json");
    }

    static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[CloudSave] Couldn't delete " + Path.GetFileName(path) + ": " + e.Message);
        }
    }

    /// <summary>
    /// Unpack, parse and fingerprint the server's save off the main thread (PrepareSnapshot). Only JsonUtility and plain
    /// C# run there.
    /// </summary>
    static Task PrepareSnapshotAsync(CloudSnapshot server)
    {
        if (server == null || string.IsNullOrEmpty(server.RawData)) return Task.CompletedTask;
        return Task.Run(() => PrepareSnapshot(server));
    }

    /// <summary>
    /// Fill SaveJson / Data / SaveHash from RawData, keeping the save only when it belongs to the meta's run: dropped for an
    /// ended run and for a leftover of an older run (NewRun writes the new meta first and deletes the old save_data after
    /// it); save_data that doesn't unpack or read as a save marks the snapshot DataBroken.
    /// </summary>
    static void PrepareSnapshot(CloudSnapshot server)
    {
        server.SaveJson = null;
        server.Data = null;
        server.SaveHash = null;
        SaveMeta meta = server.Meta;
        if (meta == null || meta.Ended || string.IsNullOrEmpty(server.RawData)) return;
        string json = UnwrapSaveJson(server.RawData, out bool broken);
        if (broken)
        {
            server.DataBroken = true;
            return;
        }
        if (string.IsNullOrEmpty(json)) return;
        SaveData data = ParseSave(json);
        if (data == null || data.currentDay <= 0)
        {
            Debug.LogWarning("[CloudSave] The server's save_data doesn't read as a save.");
            server.DataBroken = true;
            return;
        }
        if (!string.IsNullOrEmpty(data.runId) && data.runId != meta.RunId) return;
        server.SaveJson = json;
        server.Data = data;
        server.SaveHash = SavePacking.Hash(json);
    }

    static SaveData ParseSave(string json)
    {
        try
        {
            SaveData data = JsonUtility.FromJson<SaveData>(json);
            if (data != null) data.EnsureLists();
            return data;
        }
        catch (Exception)
        {
            return null;
        }
    }

    static string NewRunId()
    {
        return Guid.NewGuid().ToString("N");
    }

    static string Short(string id)
    {
        if (string.IsNullOrEmpty(id)) return "(none)";
        return id.Length <= 8 ? id : id.Substring(0, 8);
    }

    /// <summary>The save's JSON text from save_data (see the overload); null when there is none or it can't be read.</summary>
    public static string UnwrapSaveJson(string raw)
    {
        return UnwrapSaveJson(raw, out _);
    }

    /// <summary>
    /// The save's JSON text from save_data: a JSON string value (accepted quoted too) holding packed text (SavePacking) -
    /// or, from before packing, the JSON text itself. Null when there is none; null with <paramref name="broken"/> set when
    /// it can't be unquoted or unpacked (logged, never thrown).
    /// </summary>
    public static string UnwrapSaveJson(string raw, out bool broken)
    {
        broken = false;
        if (string.IsNullOrEmpty(raw)) return null;
        string text = raw;
        string trimmed = raw.TrimStart();
        if (trimmed.Length > 0 && trimmed[0] == '"')
        {
            try
            {
                text = JsonConvert.DeserializeObject<string>(trimmed);
            }
            catch (Exception e)
            {
                broken = true;
                Debug.LogWarning("[CloudSave] The server's save_data isn't a readable JSON string (" + e.Message + ").");
                return null;
            }
        }
        try
        {
            string json = SavePacking.Unpack(text);
            return string.IsNullOrEmpty(json) ? null : json;
        }
        catch (Exception e)
        {
            broken = true;
            Debug.LogWarning("[CloudSave] The server's save_data couldn't be unpacked (" + e.Message + ").");
            return null;
        }
    }

    void Log(string message)
    {
        Debug.Log("[CloudSave] " + message);
    }

    // ------------------------------------------------------------------
    // Descriptions (logs, Tools > Game > Cloud Save > Status)
    // ------------------------------------------------------------------

    /// <summary>"run 1a2b3c4d seq 12, day 30, $1,234,567, peak ..., accepted ..., ledger 55, flags: growth".</summary>
    public static string DescribeMeta(SaveMeta meta)
    {
        if (meta == null) return "nothing";
        var text = new StringBuilder();
        text.Append("run ").Append(Short(meta.RunId)).Append(" seq ").Append(meta.Seq)
            .Append(", day ").Append(meta.Day)
            .Append(", ").Append(UIFormat.Money(meta.Value))
            .Append(" (peak ").Append(UIFormat.Money(meta.Peak)).Append(')')
            .Append(", accepted ").Append(DescribeTime(meta.AcceptedAt))
            .Append(", ledger ").Append(meta.LedgerIndex)
            .Append(", city dep/wd ").Append(meta.CityDeposited).Append('/').Append(meta.CityWithdrawn);
        if (meta.Ended) text.Append(", ENDED");
        if (meta.Flagged)
        {
            text.Append(", FLAGS:");
            foreach (SaveFlag flag in meta.Flags)
                if (flag != null) text.Append(' ').Append(flag.Reason).Append(" (").Append(flag.Detail).Append(')');
        }
        if (meta.Luck != null)
            text.Append(", luck z gambles ").Append(meta.Luck.Gambles.Z.ToString("0.0", CultureInfo.InvariantCulture))
                .Append(" (n ").Append(meta.Luck.Gambles.Count).Append("), cards ")
                .Append(meta.Luck.Cards.Z.ToString("0.0", CultureInfo.InvariantCulture))
                .Append(" (n ").Append(meta.Luck.Cards.Count).Append(')');
        return text.ToString();
    }

    static string DescribeTime(long unixSeconds)
    {
        if (unixSeconds <= 0) return "never";
        return DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    string DescribeChain()
    {
        return "Chain: run " + Short(state.runId) + ", base seq " + state.baseSeq + (state.pendingNewRun ? ", NewRun pending" : "") +
               (state.pendingEndRun ? ", EndRun pending (" + Short(state.endedRunId) + ")" : "") + ".";
    }

    /// <summary>Everything known locally (works in Edit mode too): account, backend, sync state, local save.</summary>
    public static string DescribeLocal()
    {
        var text = new StringBuilder("[CloudSave] Status\n");
        bool localTest = UseLocalTestServer;
        CloudSaveSync sync = Instance;
        text.Append("  ").Append(PlayerAccount.DescribeState()).Append('\n');
        text.Append("  ").Append(SteamPlatform.Describe()).Append('\n');
        text.Append("  Server: ").Append(sync != null ? sync.backend.Name : (localTest ? "local test server" : "UGS (GameSave module)")).Append('\n');
        if (sync != null)
        {
            text.Append("  Sync: ").Append(sync.ComputeStatus())
                .Append(sync.OpRunning ? " (" + sync.opName + " in flight)" : "")
                .Append(sync.dirty ? ", upload due in " + Mathf.Max(0f, sync.nextUploadAt - Time.realtimeSinceStartup).ToString("0", CultureInfo.InvariantCulture) + " s" : "")
                .Append(sync.retryAt > Time.realtimeSinceStartup ? ", retry in " + (sync.retryAt - Time.realtimeSinceStartup).ToString("0", CultureInfo.InvariantCulture) + " s" : "")
                .Append(sync.uploadsBlocked ? ", UPLOADS STOPPED" : "")
                .Append(sync.lastError != null ? ", last error: " + sync.lastError : "")
                .Append('\n');
            text.Append("  Server meta (last seen): ").Append(DescribeMeta(sync.lastServerMeta)).Append('\n');
            text.Append("  Ledger in memory: ").Append(sync.ledger.Count).Append(" entries, next index ").Append(sync.ledger.NextIndex).Append('\n');
        }

        string baseName = localTest ? "cloudsync_localtest" : "cloudsync";
        CloudSyncState st = sync != null ? sync.state : CloudSyncState.Load(CloudSyncState.CurrentPath(baseName));
        if (st == null)
            text.Append("  Sync state: none (").Append(CloudSyncState.CurrentPath(baseName)).Append(")\n");
        else
            text.Append("  Sync state (").Append(CloudSyncState.CurrentPath(baseName)).Append("): owner ").Append(Short(st.ownerId))
                .Append(", run ").Append(Short(st.runId)).Append(", base seq ").Append(st.baseSeq)
                .Append(", ledger ").Append(st.ledgerIndex)
                .Append(st.pendingNewRun ? ", NewRun pending" : "")
                .Append(st.pendingEndRun ? ", EndRun pending (" + Short(st.endedRunId) + ")" : "")
                .Append(", last accept ").Append(DescribeTime(st.lastAcceptedAt))
                .Append(", known server accept ").Append(DescribeTime(st.knownAcceptedAt))
                .Append(", last sync ").Append(DescribeTime(st.lastSyncAt))
                .Append(string.IsNullOrEmpty(st.inFlightHash) ? "" : ", upload in flight: run " + Short(st.inFlightRun) + " seq " + st.inFlightSeq)
                .Append('\n');

        if (SaveSystem.TryReadLocalSaveText(out string saveText, out SaveData data) && data != null)
        {
            int entries = CloudLedger.EntriesInSave(data, 0).Count;
            text.Append("  Local save (").Append(SaveSystem.LocalSaveFilePath).Append("): run ").Append(Short(data.runId))
                .Append(" seq ").Append(data.seq).Append(" (base ").Append(data.baseSeq).Append("), owner ").Append(Short(data.ownerId))
                .Append(string.IsNullOrEmpty(data.ownerSteamId) ? "" : " / Steam " + data.ownerSteamId)
                .Append(", day ").Append(data.currentDay).Append(", ").Append(UIFormat.Money(data.companyValue))
                .Append(", ").Append(entries).Append(" ledger entries, ").Append(saveText.Length).Append(" chars\n");
        }
        else
        {
            text.Append("  Local save: none\n");
        }

        try
        {
            foreach (string file in Directory.GetFiles(Application.persistentDataPath, "savegame_*.json"))
            {
                string name = Path.GetFileName(file);
                if (name == "savegame_backup.json") continue;
                bool kept = name.StartsWith("savegame_replaced_") || name.StartsWith("savegame_discarded_") || name.StartsWith("savegame_offline_");
                text.Append(kept ? "  Kept copy: " : "  Set aside: ").Append(name).Append('\n');
            }
        }
        catch (Exception) { }

        if (localTest)
        {
            string playerId = PlayerAccount.IsSignedIn ? PlayerAccount.PlayerId : LocalTestSaveBackend.OfflinePlayerId;
            text.Append("  ").Append(LocalTestSaveBackend.DescribeRecord(playerId)).Append('\n');
        }
        return text.ToString();
    }

    /// <summary>Play mode: read the server's meta now and describe it.</summary>
    public static async Task<string> DescribeServerAsync()
    {
        CloudSaveSync sync = Instance;
        if (sync == null) return "[CloudSave] Server: the sync isn't running (Play mode only).";
        if (!sync.backend.CanCall && !await sync.backend.EnsureReady())
            return "[CloudSave] Server: not signed in (" + PlayerAccount.LastError + ").";
        try
        {
            CloudSnapshot snapshot = await sync.backend.Load();
            if (snapshot == null) return "[CloudSave] Server: no answer.";
            await PrepareSnapshotAsync(snapshot);
            string save = snapshot.SaveJson != null ? ", save_data " + snapshot.SaveJson.Length + " chars unpacked"
                        : snapshot.DataBroken ? ", save_data UNREADABLE" : ", no save_data";
            return "[CloudSave] Server (" + sync.backend.Name + ", player " + sync.backend.PlayerId + "): " + DescribeMeta(snapshot.Meta) + save;
        }
        catch (Exception e)
        {
            return "[CloudSave] Server: couldn't read (" + PlayerAccount.Describe(e) + ").";
        }
    }
}
