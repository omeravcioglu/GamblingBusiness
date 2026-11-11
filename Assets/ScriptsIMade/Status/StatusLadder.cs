using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The company's status: Start-up, Company ($1M), Corporation ($10M), Executive ($50M), Tycoon ($100M), Mogul ($500M),
/// Billionaire ($1B).
/// - The status is the highest tier the company value has reached; it never drops when the value does.
/// - When the value passes one or more thresholds during play, the company is promoted to the highest one at once
///   (TierChanged), and ONE promotion memo for it (PromotionMemoView) opens with a "Status upgrade" toast once the
///   screen is calm: no panel, mini-game or risk event, nothing being typed, for a second, and the HUD's value counter
///   has rolled past the threshold (or waited a few seconds more).
/// - TierChanged is also raised after every load (LastChangeKind = Load), so the HUD can set its materials.
/// - Saved under "status" (highest tier reached, plus a promotion whose memo wasn't shown yet). Saves from before the
///   ladder existed get the tier their value has reached, silently.
/// - Created automatically in every scene with a CompanyManager, and goes away with that scene.
/// - Tier names and "New:" lines come from the "Status" String Table (Assets/Localization/Source/Status.csv, keys
///   status.tier.&lt;id&gt; and status.tier.&lt;id&gt;.new) in the current language; saves and logs keep the English names.
///   Custom tiers that keep a built-in name stay localized; other custom names show as typed.
/// </summary>
[DisallowMultipleComponent]
public class StatusLadder : MonoBehaviour, ISaveParticipant
{
    /// <summary>One rung of the ladder: its name, the company value that reaches it and what changes with it.</summary>
    [Serializable]
    public class Tier
    {
        public string name;
        [Tooltip("Company value that reaches this tier")]
        public double threshold;
        [Tooltip("What changes at this tier: the promotion memo's \"New:\" line")]
        public string whatsNew;

        public Tier() { }

        public Tier(string name, double threshold, string whatsNew)
        {
            this.name = name;
            this.threshold = threshold;
            this.whatsNew = whatsNew;
        }
    }

    /// <summary>Why TierChanged was raised.</summary>
    public enum ChangeKind
    {
        Load,       // a game was loaded or started: set the look, don't celebrate
        Promotion,  // the company value reached a higher tier during play
        Reset       // the tier was reset to what the current value has reached (debug)
    }

    public const string Key = "status";
    /// <summary>The String Table collection of the status texts (Assets/Localization/Source/Status.csv).</summary>
    public const string Table = "Status";

    public static StatusLadder Instance { get; private set; }

    /// <summary>
    /// The status tier changed: (old tier, new tier). Also raised once after every load, even when the tier stays the
    /// same. Read LastChangeKind in the handler: Promotion = celebrate; Load or Reset = just set the look.
    /// </summary>
    public static event Action<int, int> TierChanged;

    /// <summary>Why TierChanged was raised last (valid inside a TierChanged handler).</summary>
    public static ChangeKind LastChangeKind { get; private set; }

    static readonly Tier[] DefaultTiers =
    {
        new Tier("Start-up", 0d, "sticky-note tabs · rubber stamp"),
        new Tier("Company", 1000000d, "ring binder · plastic tabs · chrome clip"),
        new Tier("Corporation", 10000000d, "company letterhead · brass clips"),
        new Tier("Executive", 50000000d, "stitched leather · embossed seal"),
        new Tier("Tycoon", 100000000d, "walnut · brass corners · wax seal"),
        new Tier("Mogul", 500000000d, "black leather · gold trim · nameplate"),
        new Tier("Billionaire", 1000000000d, "gold tabs · gilt edges · crown seal")
    };

    // The built-in tiers' ids in the String Table: status.tier.<id> (name), status.tier.<id>.new, status.memo.headline.<id>
    static readonly string[] DefaultTierIds = { "startup", "company", "corporation", "executive", "tycoon", "mogul", "billionaire" };

    [Header("Tiers")]
    [Tooltip("Leave empty for the built-in ladder (Start-up $0, Company $1M, Corporation $10M, Executive $50M, Tycoon $100M, " +
             "Mogul $500M, Billionaire $1B). Thresholds must rise. The HUD and the memo's pictures have looks for 7 tiers.")]
    [SerializeField] List<Tier> customTiers = new List<Tier>();

    [Header("Promotion memo")]
    [Tooltip("Seconds of calm (no panel, mini-game, risk event or typing) before the memo opens")]
    [SerializeField] float calmSeconds = 1f;
    [Tooltip("Extra seconds the memo may wait for the HUD's value counter to roll past the threshold")]
    [SerializeField] float counterWaitSeconds = 5f;
    [Tooltip("The memo to open (empty = the scene's PromotionMemoView, created at runtime if there is none)")]
    [SerializeField] PromotionMemoView memoView;

    [Header("Debug")]
    [SerializeField] bool logChanges = true;

    /// <summary>Highest tier reached (0 = Start-up). Valid once IsTierKnown.</summary>
    public int CurrentTier { get; private set; }
    /// <summary>False until the saved game (or a new one) has been loaded.</summary>
    public bool IsTierKnown { get; private set; }
    /// <summary>A promotion whose memo is waiting for a calm moment.</summary>
    public bool HasPendingMemo => pendingTier >= 0;

    public string CurrentTierName => GetTierName(CurrentTier);
    public bool IsTopTier => CurrentTier >= TierCount - 1;
    /// <summary>The next tier, or -1 at the top.</summary>
    public int NextTier => IsTopTier ? -1 : CurrentTier + 1;
    /// <summary>Company value of the next tier (the top tier's own threshold at the top).</summary>
    public double NextThreshold => GetTierThreshold(IsTopTier ? CurrentTier : CurrentTier + 1);

    /// <summary>Company value as a share of the next tier's threshold (0-1; 1 at the top), as the memo's bar shows it.</summary>
    public float ProgressToNext
    {
        get
        {
            if (IsTopTier) return 1f;
            double next = NextThreshold;
            if (next <= 0d) return 1f;
            double value = CompanyManager.Instance != null ? CompanyManager.Instance.companyValue : 0d;
            return Mathf.Clamp01((float)(value / next));
        }
    }

    public string SaveKey => Key;

    List<Tier> activeCustomTiers;   // validated copy of customTiers, null = the built-in ladder
    CompanyManager subscribedCompany;
    bool restoredThisLoad;
    int pendingTier = -1;
    int pendingFromTier = -1;
    float calmTime;

    [Serializable]
    class SavedStatus
    {
        public int highestTier;
        public string tierName;          // for people reading the save file; not read back
        public int pendingTier = -1;     // promoted, but the memo hasn't been shown yet
        public int pendingFromTier = -1; // the tier before that promotion (the memo's "Before" picture)
    }

    // ------------------------------------------------------------------
    // Tier table
    // ------------------------------------------------------------------

    static IReadOnlyList<Tier> Tiers
    {
        get
        {
            if (Instance != null && Instance.activeCustomTiers != null) return Instance.activeCustomTiers;
            return DefaultTiers;
        }
    }

    public static int TierCount => Tiers.Count;

    /// <summary>The tier's name in the current language ("Corporation", "集团", "Holding"...).</summary>
    public static string GetTierName(int tier)
    {
        string id = GetTierId(tier);
        return id != null ? L10n.Get(Table, "status.tier." + id) : TierAt(tier).name;
    }

    /// <summary>The tier's English name as set in the ladder (for saves and logs).</summary>
    public static string GetTierEnglishName(int tier)
    {
        return TierAt(tier).name;
    }

    /// <summary>
    /// The built-in id of the tier ("startup" ... "billionaire"), which its String Table keys use; null for a custom tier
    /// with a name of its own. A custom tier that keeps a built-in name keeps that tier's id.
    /// </summary>
    public static string GetTierId(int tier)
    {
        int index = DefaultIndexOf(TierAt(tier).name);
        return index >= 0 ? DefaultTierIds[index] : null;
    }

    public static double GetTierThreshold(int tier)
    {
        return TierAt(tier).threshold;
    }

    /// <summary>What changes at this tier (the memo's "New:" line), in the current language.</summary>
    public static string GetTierWhatsNew(int tier)
    {
        Tier t = TierAt(tier);
        int index = DefaultIndexOf(t.name);
        if (index >= 0 && t.whatsNew == DefaultTiers[index].whatsNew)
            return L10n.Get(Table, "status.tier." + DefaultTierIds[index] + ".new");
        return t.whatsNew;
    }

    static int DefaultIndexOf(string tierName)
    {
        for (int i = 0; i < DefaultTiers.Length; i++)
        {
            if (DefaultTiers[i].name == tierName) return i;
        }
        return -1;
    }

    /// <summary>The highest tier this company value reaches.</summary>
    public static int TierForValue(double value)
    {
        IReadOnlyList<Tier> tiers = Tiers;
        int tier = 0;
        for (int i = 1; i < tiers.Count; i++)
        {
            if (value >= tiers[i].threshold) tier = i;
            else break;
        }
        return tier;
    }

    static Tier TierAt(int tier)
    {
        IReadOnlyList<Tier> tiers = Tiers;
        return tiers[Mathf.Clamp(tier, 0, tiers.Count - 1)];
    }

    // ------------------------------------------------------------------
    // Creation
    // ------------------------------------------------------------------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        // Entering Play mode without a domain reload keeps statics: start every session clean
        Instance = null;
        TierChanged = null;
        LastChangeKind = ChangeKind.Load;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Ensure(); // the first scene is loaded already
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Ensure();
    }

    /// <summary>The scene's ladder, created if it's missing. Null in a scene without a CompanyManager.</summary>
    public static StatusLadder Ensure()
    {
        if (Instance != null) return Instance;
        if (!Application.isPlaying || CompanyManager.Instance == null) return null;

        StatusLadder existing = FindAnyObjectByType<StatusLadder>();
        if (existing != null) return existing;
        return new GameObject("StatusLadder").AddComponent<StatusLadder>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[StatusLadder] Removed a second StatusLadder on '" + name + "'.");
            Destroy(this);
            return;
        }
        Instance = this;
        ValidateCustomTiers();
        TrackCompany();
    }

    void OnValidate()
    {
        ValidateCustomTiers();
    }

    void OnDestroy()
    {
        if (subscribedCompany != null)
            subscribedCompany.StateLoaded -= OnStateLoaded;
        subscribedCompany = null;
        if (Instance == this)
            Instance = null;
    }

    /// <summary>Use the custom tiers if they're usable (names filled in, thresholds rising), else the built-in ladder.</summary>
    void ValidateCustomTiers()
    {
        activeCustomTiers = null;
        if (customTiers == null || customTiers.Count == 0) return;

        var list = new List<Tier>(customTiers.Count);
        for (int i = 0; i < customTiers.Count; i++)
        {
            Tier tier = customTiers[i];
            if (tier == null || double.IsNaN(tier.threshold) || (i > 0 && !(tier.threshold > list[i - 1].threshold)))
            {
                Debug.LogWarning("[StatusLadder] Custom tier " + i + " needs a threshold above the one before it - using the built-in ladder.");
                return;
            }
            string tierName = string.IsNullOrWhiteSpace(tier.name) ? "Tier " + i : tier.name.Trim();
            list.Add(new Tier(tierName, tier.threshold, tier.whatsNew ?? ""));
        }
        activeCustomTiers = list;
    }

    [ContextMenu("Fill Custom Tiers With the Built-in Ladder")]
    void FillCustomTiers()
    {
        customTiers = new List<Tier>();
        foreach (Tier tier in DefaultTiers)
            customTiers.Add(new Tier(tier.name, tier.threshold, tier.whatsNew));
        ValidateCustomTiers();
    }

    // ------------------------------------------------------------------
    // Loading
    // ------------------------------------------------------------------

    void TrackCompany()
    {
        CompanyManager company = CompanyManager.Instance;
        if (company == subscribedCompany) return;
        if (subscribedCompany != null)
            subscribedCompany.StateLoaded -= OnStateLoaded;
        subscribedCompany = company;
        if (company != null)
            company.StateLoaded += OnStateLoaded;
    }

    public string CaptureState()
    {
        int tier = IsTierKnown ? CurrentTier : TierForValue(CompanyValue);
        var data = new SavedStatus
        {
            highestTier = tier,
            tierName = GetTierEnglishName(tier),
            pendingTier = pendingTier,
            pendingFromTier = pendingFromTier
        };
        return JsonUtility.ToJson(data);
    }

    public void RestoreState(string json)
    {
        SavedStatus data = null;
        if (!string.IsNullOrEmpty(json))
        {
            try
            {
                data = JsonUtility.FromJson<SavedStatus>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[StatusLadder] Couldn't read the saved status (" + e.Message + ") - using the company value's tier.");
            }
        }
        if (data == null) return; // OnStateLoaded falls back to the value's tier

        int top = TierCount - 1;
        int tier = Mathf.Clamp(data.highestTier, 0, top);
        int pending = data.pendingTier >= 1 && data.pendingTier <= tier ? data.pendingTier : -1;
        int from = pending >= 0 ? Mathf.Clamp(data.pendingFromTier, 0, pending - 1) : -1;
        restoredThisLoad = true;
        ApplyLoadedTier(tier, pending, from, "loaded");
    }

    // SaveSystem raises this after it applied a save (after RestoreState, if the save had a status)
    void OnStateLoaded()
    {
        if (restoredThisLoad)
        {
            restoredThisLoad = false;
            return;
        }
        // A save from before the status ladder: the tier its value has reached, without a memo
        ApplyLoadedTier(TierForValue(CompanyValue), -1, -1, "from the company value (older save)");
    }

    void ApplyLoadedTier(int tier, int pending, int pendingFrom, string how)
    {
        IsTierKnown = true;
        pendingTier = pending;
        pendingFromTier = pendingFrom;
        calmTime = 0f;
        SetTier(tier, ChangeKind.Load, how);
    }

    // ------------------------------------------------------------------
    // Promotions
    // ------------------------------------------------------------------

    static double CompanyValue => CompanyManager.Instance != null ? CompanyManager.Instance.companyValue : 0d;

    void Update()
    {
        TrackCompany();
        CompanyManager company = subscribedCompany;
        if (company == null) return;

        if (!IsTierKnown)
        {
            // A new game (or no save): SaveSystem finishes without handing over any data
            if (SaveSystem.Instance == null || SaveSystem.Instance.IsGameLoaded)
                ApplyLoadedTier(TierForValue(company.companyValue), -1, -1, "new company");
            return;
        }

        int reached = TierForValue(company.companyValue);
        if (reached > CurrentTier)
            Promote(reached);

        if (pendingTier >= 0)
            WaitToShowMemo(company);
    }

    void Promote(int tier)
    {
        // Several thresholds before the memo opens: one memo, from the look the player saw last
        if (pendingTier < 0)
            pendingFromTier = CurrentTier;
        pendingTier = tier;
        calmTime = 0f;
        SetTier(tier, ChangeKind.Promotion, "at " + UIFormat.Money(CompanyValue));
    }

    void WaitToShowMemo(CompanyManager company)
    {
        if (!IsCalm())
        {
            calmTime = 0f;
            return;
        }
        calmTime += Time.unscaledDeltaTime;
        if (calmTime < calmSeconds) return;

        // The HUD's value counter catches up over a few seconds: open when it shows the threshold passed
        bool counterPast = company.displayedValue >= GetTierThreshold(pendingTier);
        if (!counterPast && calmTime < calmSeconds + counterWaitSeconds) return;

        ShowPendingMemo();
    }

    /// <summary>
    /// Nothing else is going on: no panel (the memo included), mini-game or risk event, nothing being typed, the game
    /// isn't paused some other way and the saved game has been loaded.
    /// </summary>
    static bool IsCalm()
    {
        if (Time.timeScale <= 0f) return false;
        SaveSystem save = SaveSystem.Instance;
        if (save != null && (save.IsLoading || !save.IsGameLoaded)) return false;
        return UIManager.GameplayInputAllowed();
    }

    void ShowPendingMemo()
    {
        int tier = pendingTier;
        int from = pendingFromTier;
        pendingTier = pendingFromTier = -1;
        calmTime = 0f;
        ShowMemo(from, tier);
    }

    void ShowMemo(int fromTier, int toTier)
    {
        PromotionMemoView view = FindMemoView();
        if (view != null)
            view.Show(fromTier, toTier);
        GameNotifications.Show(L10n.Get(Table, "status.toast.upgrade", ("tier", GetTierName(toTier))), NotificationKind.Special);
    }

    PromotionMemoView FindMemoView()
    {
        if (memoView == null) memoView = PromotionMemoView.Instance;
        if (memoView == null) memoView = FindAnyObjectByType<PromotionMemoView>(FindObjectsInactive.Include);
        if (memoView == null) memoView = gameObject.AddComponent<PromotionMemoView>();
        return memoView;
    }

    void SetTier(int tier, ChangeKind kind, string how)
    {
        int old = CurrentTier;
        CurrentTier = tier;
        LastChangeKind = kind;
        if (logChanges)
        {
            string change = kind == ChangeKind.Promotion ? "promoted from " + GetTierEnglishName(old) + " to " : kind == ChangeKind.Reset ? "reset to " : "";
            Debug.Log("[StatusLadder] Status " + change + GetTierEnglishName(tier) + " (" + how + ")");
        }

        Action<int, int> handlers = TierChanged;
        if (handlers == null) return;
        foreach (Action<int, int> handler in handlers.GetInvocationList())
        {
            // One broken listener mustn't keep the others (or the ladder) from updating
            try { handler(old, tier); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }

    // ------------------------------------------------------------------
    // Resets and debug
    // ------------------------------------------------------------------

    /// <summary>
    /// Forget the highest tier reached: back to the tier the current company value has reached, without a memo
    /// (TierChanged with LastChangeKind = Reset). Also for a new run that keeps the scene loaded.
    /// </summary>
    public void ResetToValue()
    {
        IsTierKnown = true;
        pendingTier = pendingFromTier = -1;
        calmTime = 0f;
        SetTier(TierForValue(CompanyValue), ChangeKind.Reset, UIFormat.Money(CompanyValue));
    }

    /// <summary>
    /// Debug: promote to the next tier now and open its memo right away (at the top tier, show the top tier's memo
    /// again). False while the game is still loading.
    /// </summary>
    public bool DebugShowNextTierMemo()
    {
        if (!IsTierKnown) return false;
        int top = TierCount - 1;
        if (top <= 0) return false;

        if (CurrentTier < top)
        {
            Promote(CurrentTier + 1);
            ShowPendingMemo();
        }
        else
        {
            ShowMemo(top - 1, top);
        }
        return true;
    }
}
