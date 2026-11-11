using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// The game's side of the six online cities (persists across scenes, created automatically).
/// - Connects to the real cities (Unity Gaming Services) or, in the Editor/development builds, to local test cities.
/// - Knows my account in every city (the city picker), and for the city being visited a copy of its map and my
///   account there, updated by every reply and by live pushes.
/// - Bank transfers move money between the single-player company and a city's bank. They are recorded in the save
///   first (ISaveParticipant "onlineCity"), so a transfer interrupted by a crash or a lost connection finishes (or is
///   undone) later instead of losing or doubling money.
/// - Watches the company value: reaching CityRules.UnlockCompanyValue opens the cities (Unlocked event).
/// </summary>
public class CityOnline : MonoBehaviour, ISaveParticipant
{
    public static CityOnline Instance { get; private set; }

    public const string UseLocalKey = "City_UseLocalTestCity";
    public const string JoinedKey = "City_Joined";

    public enum Status { Idle, Connecting, Ready, Failed }

    public Status State { get; private set; } = Status.Idle;
    public string LastError { get; private set; }
    public bool IsReady => State == Status.Ready;
    public bool IsLive => backend != null && backend.IsLive;
    public string PlayerId => backend?.PlayerId;

    /// <summary>The city being visited (null on the city picker).</summary>
    public string CityId { get; private set; }
    public CityMap Map => CityId != null ? CityLayout.Get(CityId) : null;
    /// <summary>My account in the city being visited.</summary>
    public CityAccountView Account { get; private set; }
    public bool Joined => Account != null && Account.Joined;
    /// <summary>The visited city's districts.</summary>
    public Dictionary<string, CityDistrict> Districts { get; } = new Dictionary<string, CityDistrict>();
    /// <summary>My account in every city I joined, as last read (the city picker).</summary>
    public Dictionary<string, CityAccount> MyAccounts { get; } = new Dictionary<string, CityAccount>();

    /// <summary>Set by the city screen while it's open (the map and account refresh on their own then).</summary>
    public bool ScreenOpen { get; set; }

    /// <summary>Unix seconds on the server's clock.</summary>
    public long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds() + clockOffset;

    public event Action StatusChanged;
    public event Action AccountChanged;
    public event Action MapReloaded;
    public event Action<List<CityPlotChange>> PlotsChanged;
    public event Action<CityMessage> NewsArrived;
    public event Action MyAccountsChanged;
    public event Action Unlocked;

    ICityBackend backend;
    Task connecting;
    long clockOffset;
    float nextUnlockCheck, nextTransferRetry, nextMapRefresh, nextAccountRefresh;
    readonly ConcurrentQueue<Action> mainThread = new ConcurrentQueue<Action>();

    // ---- Lifetime ------------------------------------------------------------------------------------------------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() => Ensure();

    public static CityOnline Ensure()
    {
        if (Instance != null) return Instance;
        var go = new GameObject("CityOnline");
        DontDestroyOnLoad(go);
        return go.AddComponent<CityOnline>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance != this) return;
        backend?.Stop();
        Instance = null;
    }

    void OnApplicationQuit()
    {
        backend?.Stop();
    }

    void Update()
    {
        while (mainThread.TryDequeue(out var action))
        {
            try { action(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        if (backend is CityLocalBackend local) local.Tick();

        float now = Time.unscaledTime;
        if (now >= nextUnlockCheck)
        {
            nextUnlockCheck = now + 1f;
            CheckUnlock();
        }
        if (!IsReady) return;

        if (save.pending.Count > 0 && now >= nextTransferRetry)
        {
            nextTransferRetry = now + 20f;
            _ = FinishPendingTransfers();
        }
        if (ScreenOpen && CityId != null && now >= nextMapRefresh)
        {
            nextMapRefresh = now + 60f;
            _ = ReloadMap();
        }
        if (ScreenOpen && CityId != null && now >= nextAccountRefresh)
        {
            nextAccountRefresh = now + 30f;
            _ = RefreshAccount();
        }
    }

    // ---- Connecting ----------------------------------------------------------------------------------------------

    /// <summary>The local test cities exist in the Editor and development builds only.</summary>
    public static bool LocalTestCityAllowed => Application.isEditor || Debug.isDebugBuild;

    /// <summary>Editor snapshots and tests: force the local test cities without touching the saved setting.</summary>
    public static bool ForceLocalTestCity;

    public static bool UseLocalTestCity
    {
        get => ForceLocalTestCity || (LocalTestCityAllowed && PlayerPrefs.GetInt(UseLocalKey, 0) == 1);
        set
        {
            PlayerPrefs.SetInt(UseLocalKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    /// <summary>Connect (or finish connecting). Safe to call any number of times.</summary>
    public Task Connect()
    {
        if (State == Status.Ready) return Task.CompletedTask;
        if (connecting != null && !connecting.IsCompleted) return connecting;
        connecting = ConnectRoutine();
        return connecting;
    }

    /// <summary>Drop the connection and connect again (e.g. after switching to or from the local test cities).</summary>
    public Task Reconnect()
    {
        backend?.Stop();
        backend = null;
        Account = null;
        Districts.Clear();
        MyAccounts.Clear();
        State = Status.Idle;
        StatusChanged?.Invoke();
        return Connect();
    }

    async Task ConnectRoutine()
    {
        State = Status.Connecting;
        LastError = null;
        StatusChanged?.Invoke();
        try
        {
            if (backend == null)
            {
                backend = UseLocalTestCity ? new CityLocalBackend() : (ICityBackend)new CityUgsBackend();
                backend.PlotsPushed += push => mainThread.Enqueue(() => OnPush(push));
                backend.NewsPushed += news => mainThread.Enqueue(() => OnNews(news));
            }
            await backend.Start();
            await LoadMyAccountsNow();
            if (CityId != null)
            {
                await ReloadMap();
                var result = await backend.Call(CityId, "GetAccount", null);
                if (!result.Ok) throw new Exception(result.Error);
                Apply(result);
            }

            State = Status.Ready;
            StatusChanged?.Invoke();
            _ = FinishPendingTransfers();
        }
        catch (Exception e)
        {
            LastError = CityUgsBackend.Describe(e);
            Debug.LogWarning($"[City] Couldn't connect: {e}");
            State = Status.Failed;
            StatusChanged?.Invoke();
        }
    }

    // ---- Visiting a city -----------------------------------------------------------------------------------------

    /// <summary>Go to a city: its map and my account there are loaded (the city screen shows them).</summary>
    public async Task<bool> EnterCity(string cityId)
    {
        if (CityLayout.Get(cityId) == null) return false;
        await Connect();
        if (!IsReady) return false;
        if (CityId != cityId)
        {
            CityId = cityId;
            Account = null;
            Districts.Clear();
        }
        await ReloadMap();
        await RefreshAccount();
        return Account != null;
    }

    /// <summary>Back to the city picker.</summary>
    public void LeaveCity()
    {
        CityId = null;
        Account = null;
        Districts.Clear();
        _ = RefreshMyAccounts();
    }

    /// <summary>My accounts in every city again (the picker's cards).</summary>
    public async Task RefreshMyAccounts()
    {
        if (backend == null || !IsReady) return;
        try
        {
            await LoadMyAccountsNow();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[City] Couldn't read your city accounts: {e.Message}");
        }
    }

    async Task LoadMyAccountsNow()
    {
        var accounts = await backend.LoadMyAccounts();
        MyAccounts.Clear();
        foreach (var pair in accounts) MyAccounts[pair.Key] = pair.Value;
        if (MyAccounts.Count > 0)
        {
            PlayerPrefs.SetInt(JoinedKey, 1);
            PlayerPrefs.Save();
        }
        MyAccountsChanged?.Invoke();
    }

    /// <summary>Money in every city bank I have (for "can the company still raise money").</summary>
    public long TotalBankBalance
    {
        get
        {
            long total = 0;
            foreach (var pair in MyAccounts)
            {
                if (pair.Key == CityId && Account != null && Account.Joined) total += Math.Max(0L, Account.Balance);
                else if (pair.Value != null) total += Math.Max(0L, pair.Value.Balance);
            }
            return total;
        }
    }

    /// <summary>Read the visited city's districts again (districts nobody changed yet look the way the city opens).</summary>
    public async Task ReloadMap()
    {
        string cityId = CityId;
        var map = Map;
        if (backend == null || map == null) return;
        Dictionary<string, CityDistrict> loaded;
        try
        {
            loaded = await backend.LoadDistricts(cityId);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[City] Couldn't refresh the map: {e.Message}");
            return;
        }
        if (cityId != CityId) return; // Went somewhere else meanwhile

        Districts.Clear();
        foreach (var def in map.Districts)
        {
            Districts[def.Id] = loaded != null && loaded.TryGetValue(def.Id, out var district) && district != null
                ? district
                : CityNpcs.SeedDistrict(map, def);
        }
        MapReloaded?.Invoke();
    }

    public async Task RefreshAccount()
    {
        string cityId = CityId;
        if (backend == null || cityId == null) return;
        try
        {
            var result = await backend.Call(cityId, "GetAccount", null);
            if (result.Ok && cityId == CityId) Apply(result);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[City] Couldn't refresh the account: {e.Message}");
        }
    }

    /// <summary>Player drawings of a city (CityLogoCache asks for them).</summary>
    public async Task<Dictionary<string, CityLogoRecord>> LoadLogos(string cityId, List<string> hashes)
    {
        await Connect();
        if (!IsReady || backend == null) return new Dictionary<string, CityLogoRecord>();
        return await backend.LoadLogos(cityId, hashes);
    }

    void OnPush(CityPush push)
    {
        if (push == null || push.CityId != CityId) return; // Another city
        if (push.Reload)
        {
            _ = ReloadMap();
            return;
        }
        ApplyChanges(push.Changes);
    }

    void OnNews(CityMessage news)
    {
        if (news == null) return;
        NewsArrived?.Invoke(news);
        if (news.CityId == CityId) _ = RefreshAccount(); // The money that came with it
        else _ = RefreshMyAccounts();
    }

    // ---- Operations (in the city being visited) ------------------------------------------------------------------

    public Task<CityResult> Join(string companyName, string logo, string pixels) =>
        Do("Join", new Dictionary<string, object> { { "name", companyName }, { "logo", logo ?? "" }, { "pixels", pixels ?? "" } });

    public Task<CityResult> UpdateProfile(string companyName, string logo, string pixels) =>
        Do("UpdateProfile", new Dictionary<string, object> { { "name", companyName }, { "logo", logo ?? "" }, { "pixels", pixels ?? "" } });

    public Task<CityResult> CollectRent() => Do("CollectRent", null);

    public Task<CityResult> BuyPlot(string plotId, long price) =>
        Do("BuyPlot", new Dictionary<string, object> { { "opId", NewOpId() }, { "plotId", plotId }, { "price", price } });

    public Task<CityResult> StartChallenge(string plotId) =>
        Do("StartChallenge", new Dictionary<string, object> { { "opId", NewOpId() }, { "plotId", plotId } });

    public Task<CityResult> ChallengeMove(string move) =>
        Do("ChallengeMove", new Dictionary<string, object> { { "opId", NewOpId() }, { "move", move } });

    public Task<CityResult> ReportLogo(string plotId) =>
        Do("ReportLogo", new Dictionary<string, object> { { "plotId", plotId } });

    public Task<CityResult> MarkInboxRead() => Do("MarkInboxRead", null);

    async Task<CityResult> Do(string function, Dictionary<string, object> args)
    {
        await Connect();
        if (!IsReady) return CityResult.Fail(LastError ?? "The cities aren't available right now.");
        if (CityId == null) return CityResult.Fail("Pick a city first.");

        var (result, reached) = await CallRaw(CityId, function, args);
        if (reached && result.Ok && function == "Join")
        {
            PlayerPrefs.SetInt(JoinedKey, 1);
            PlayerPrefs.Save();
            _ = RefreshMyAccounts();
        }
        return result;
    }

    /// <summary>Call a city; reached = false when the request may not have arrived (network trouble).</summary>
    async Task<(CityResult result, bool reached)> CallRaw(string cityId, string function, Dictionary<string, object> args)
    {
        try
        {
            var result = await backend.Call(cityId, function, args) ?? CityResult.Fail("The city sent an empty answer.");
            if (cityId == CityId) Apply(result);
            return (result, true);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[City] {function} failed: {e}");
            return (CityResult.Fail(CityUgsBackend.Describe(e)), false);
        }
    }

    void Apply(CityResult result)
    {
        if (result == null) return;
        if (result.Account != null && (result.Account.CityId == null || result.Account.CityId == CityId))
        {
            Account = result.Account;
            if (Account.ServerTime > 0)
                clockOffset = Account.ServerTime - DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (Account.Joined)
                PlayerPrefs.SetInt(JoinedKey, 1);
            AccountChanged?.Invoke();
        }
        if (result.Changes != null && result.Changes.Count > 0)
            ApplyChanges(result.Changes);
    }

    void ApplyChanges(List<CityPlotChange> changes)
    {
        if (changes == null || changes.Count == 0) return;
        foreach (var change in changes)
        {
            if (change == null || !Districts.TryGetValue(change.DistrictId ?? "", out var district)) continue;
            if (change.Plot == null) district.Plots.Remove(change.PlotId);
            else district.Plots[change.PlotId] = change.Plot;
        }
        PlotsChanged?.Invoke(changes);
    }

    static string NewOpId() => Guid.NewGuid().ToString("N").Substring(0, 20);

    // ---- What the map says about me -------------------------------------------------------------------------------

    public CityPlot PlotState(CityPlotDef def)
    {
        if (def == null || def.CityId != CityId || !Districts.TryGetValue(def.DistrictId, out var district)) return null;
        district.Plots.TryGetValue(def.Id, out var plot);
        return plot;
    }

    public bool IsMine(CityPlot plot) => plot != null && PlayerId != null && plot.OwnerId == PlayerId;

    public void RentInfo(out long ready, out long perDay, out int plots, out long plotsValue)
    {
        long collectedAt = Account != null ? Account.RentCollectedAt : Now;
        CityRules.RentSummary(Map, Districts, PlayerId, collectedAt, Now, out ready, out perDay, out plots, out plotsValue);
    }

    // ---- Bank transfers -------------------------------------------------------------------------------------------

    [Serializable]
    public class PendingTransfer
    {
        public string opId;
        public string kind;   // "deposit" or "withdraw"
        public string city;   // the city bank it goes to or comes from
        public string player; // the account that started it (another account can't finish it)
        public long amount;
        public long time;
    }

    [Serializable]
    class SaveState
    {
        public bool unlocked;
        public bool certificateShown;
        public List<PendingTransfer> pending = new List<PendingTransfer>();
    }

    SaveState save = new SaveState();
    bool resolvingTransfers;

    public bool HasPendingTransfers => save.pending.Count > 0;

    static bool CompanyLoaded => CompanyManager.Instance != null && SaveSystem.Instance != null && SaveSystem.Instance.IsGameLoaded;

    /// <summary>Wire money from the company into the visited city's bank.</summary>
    public async Task<CityResult> Deposit(long amount)
    {
        await Connect();
        if (!IsReady) return CityResult.Fail(LastError ?? "The cities aren't available right now.");
        if (CityId == null) return CityResult.Fail("Pick a city first.");
        if (!CompanyLoaded) return CityResult.Fail("Wire money from your office.");
        if (amount < CityRules.MinTransfer) return CityResult.Fail($"Wire at least {CityRules.Money(CityRules.MinTransfer)}.");
        if (Account != null && amount > Account.DepositLeft)
            return CityResult.Fail($"You can wire {CityRules.Money(Account.DepositLeft)} more into {Map.Name} today.");
        if (CompanyManager.Instance.companyValue < amount)
            return CityResult.Fail("Your company doesn't have that much money.");

        // The company pays first, and the save remembers the transfer until the city confirms it
        var transfer = new PendingTransfer { opId = NewOpId(), kind = "deposit", city = CityId, player = PlayerId, amount = amount, time = Now };
        CompanyManager.Instance.AddMoney(-amount, true);
        save.pending.Add(transfer);
        SaveGameNow();

        var (result, reached) = await CallRaw(transfer.city, "Deposit", TransferArgs(transfer));
        if (!reached)
            return CityResult.Fail(result.Error + " The transfer will finish once you're back online.");
        Settle(transfer, result);
        return result;
    }

    /// <summary>Wire money from the visited city's bank back to the company.</summary>
    public async Task<CityResult> Withdraw(long amount)
    {
        await Connect();
        if (!IsReady) return CityResult.Fail(LastError ?? "The cities aren't available right now.");
        if (CityId == null) return CityResult.Fail("Pick a city first.");
        if (!CompanyLoaded) return CityResult.Fail("Wire money from your office.");
        if (amount < CityRules.MinTransfer) return CityResult.Fail($"Wire at least {CityRules.Money(CityRules.MinTransfer)}.");
        if (Account != null && amount > Account.Balance)
            return CityResult.Fail($"Your {Map.Name} bank only has {CityRules.Money(Account.Balance)}.");

        var transfer = new PendingTransfer { opId = NewOpId(), kind = "withdraw", city = CityId, player = PlayerId, amount = amount, time = Now };
        save.pending.Add(transfer);
        SaveGameNow();

        var (result, reached) = await CallRaw(transfer.city, "Withdraw", TransferArgs(transfer));
        if (!reached)
            return CityResult.Fail(result.Error + " The transfer will finish once you're back online.");
        Settle(transfer, result);
        return result;
    }

    static Dictionary<string, object> TransferArgs(PendingTransfer t) =>
        new Dictionary<string, object> { { "opId", t.opId }, { "amount", t.amount } };

    /// <summary>The city answered: give the company its money back (refused deposit) or pay it (withdrawal).</summary>
    void Settle(PendingTransfer transfer, CityResult result)
    {
        if (!save.pending.Remove(transfer)) return;
        var company = CompanyManager.Instance;
        if (transfer.kind == "deposit" && !result.Ok)
            company?.AddMoney(transfer.amount, true);
        else if (transfer.kind == "withdraw" && result.Ok && result.Amount > 0)
            company?.AddMoney(result.Amount, true);
        SaveGameNow();
        _ = RefreshMyAccounts();
    }

    /// <summary>Retry transfers that never got an answer (the city recognises them by id, so nothing doubles).</summary>
    async Task FinishPendingTransfers()
    {
        if (resolvingTransfers || !IsReady || !CompanyLoaded) return;
        resolvingTransfers = true;
        try
        {
            foreach (var transfer in save.pending.ToArray())
            {
                // Only the account that started a transfer can finish it (a different sign-in waits for its owner)
                if (!string.IsNullOrEmpty(transfer.player) && transfer.player != PlayerId) continue;
                if (CityLayout.Get(transfer.city) == null)
                {
                    // Transfers from before the six cities, or a city that's gone: a deposit goes back to the company
                    save.pending.Remove(transfer);
                    if (transfer.kind == "deposit") CompanyManager.Instance.AddMoney(transfer.amount, true);
                    SaveGameNow();
                    continue;
                }
                string function = transfer.kind == "deposit" ? "Deposit" : "Withdraw";
                var (result, reached) = await CallRaw(transfer.city, function, TransferArgs(transfer));
                if (!reached) break; // Still offline: try again later
                if (!CompanyLoaded) break;
                Settle(transfer, result);
            }
        }
        finally
        {
            resolvingTransfers = false;
        }
    }

    static void SaveGameNow()
    {
        if (SaveSystem.Instance != null) SaveSystem.Instance.SaveGame();
    }

    // ---- Unlock ---------------------------------------------------------------------------------------------------

    /// <summary>True once this company reached the unlock value, or this PC already joined a city.</summary>
    public bool IsUnlocked => save.unlocked || PlayerPrefs.GetInt(JoinedKey, 0) == 1;

    /// <summary>True once this company (this save) reached the unlock value.</summary>
    public bool ReachedUnlock => save.unlocked;

    public bool CertificateShown
    {
        get => save.certificateShown;
        set => save.certificateShown = value;
    }

    void CheckUnlock()
    {
        if (save.unlocked || !CompanyLoaded) return;

        // Saves from before the cities remember their unlock in SaveData
        if (!legacyChecked)
        {
            legacyChecked = true;
            if (SaveSystem.TryReadLocalSave(out var data) && data != null && data.hasUnlockedOnlineWorld)
            {
                save.unlocked = true;
                save.certificateShown = true;
                return;
            }
        }

        if (CompanyManager.Instance.companyValue >= CityRules.UnlockCompanyValue)
        {
            save.unlocked = true;
            Unlocked?.Invoke();
        }
    }

    bool legacyChecked;

    /// <summary>Check the unlock value now (CompanyManager calls this when money changes; it also runs every second).</summary>
    public void CheckUnlockNow() => CheckUnlock();

    /// <summary>Debug: open the cities without reaching the unlock value.</summary>
    public void ForceUnlock()
    {
        if (save.unlocked) return;
        save.unlocked = true;
        Unlocked?.Invoke();
    }

    /// <summary>
    /// New game from the main menu: the new company has to reach the unlock value itself. The city accounts stay
    /// (they're online); transfers still on their way finish into whichever company is loaded.
    /// </summary>
    public static void BeginNewGame()
    {
        if (Instance == null) return;
        var pending = Instance.save.pending;
        Instance.save = new SaveState { pending = pending ?? new List<PendingTransfer>() };
        Instance.legacyChecked = false;
    }

    // ---- Save participant -----------------------------------------------------------------------------------------

    public string SaveKey => "onlineCity";

    public string CaptureState() => JsonUtility.ToJson(save);

    public void RestoreState(string json)
    {
        try
        {
            var restored = JsonUtility.FromJson<SaveState>(json);
            if (restored == null) return;
            if (restored.pending == null) restored.pending = new List<PendingTransfer>();
            save = restored;
            legacyChecked = true;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[City] Couldn't read the saved city state: {e.Message}");
        }
    }

    // ---- Local test city tools ------------------------------------------------------------------------------------

    public CityLocalBackend LocalBackend => backend as CityLocalBackend;

    public async Task<string> LocalTestAction(string action)
    {
        var local = LocalBackend;
        if (local == null) return "Only the local test cities have test tools.";
        if (CityId == null) return "Pick a city first.";
        string message = action == "money"
            ? await local.AddTestMoney(CityId, PlayerId, 5L * CityRules.DepositCapPerDay)
            : await local.RivalAct(CityId, action);
        await ReloadMap();
        await RefreshAccount();
        return message;
    }
}
