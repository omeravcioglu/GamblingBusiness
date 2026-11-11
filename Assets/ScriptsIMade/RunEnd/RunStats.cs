using System;
using UnityEngine;

/// <summary>
/// What happened this run, for the Game Over and Win screens:
/// - peak company value; hires and golden hires; risk cards played with the biggest win and loss and their titles;
///   crises solved of all crises;
/// - casino net: money won minus money bet on pure-money gambles, i.e. the build-budget gamble (a payout into the build
///   budget counts as won, a push as 0) and the casino if it is ever installed. Office upgrades don't count;
/// - whether the $10B win was already shown this run.
/// Listens to CompanyManager.EmployeeHired, EventManager.CardResolvedWithKey, MiniGameManager.MiniGameCompleted,
/// BuildGambleView.GambleSettled and CasinoManager.MoneyMoved. Saved with the game (ISaveParticipant "runStats"); an
/// old save without it starts from the current state (peak = current value, everything else 0).
/// Created at runtime by RunEndDirector (next to BankLoan) when MainGame loads.
/// The biggest win / loss keep the risk card's English title (as before) and its translation key (RandomEvent.key, e.g.
/// "event.small_good.tip_jar"); the end screens show the title in the current language through CardTitle, inside sentences
/// from the "RunEnd" String Table. A save from before the keys finds its card by the English title.
/// </summary>
[DisallowMultipleComponent]
public class RunStats : MonoBehaviour, ISaveParticipant
{
    public static RunStats Instance { get; private set; }

    /// <summary>Raised when a counted stat changes (not for every new peak value).</summary>
    public static event Action Changed;

    [Serializable]
    class State
    {
        public double peakValue;
        public int hires;
        public int goldenHires;
        public int riskCardsPlayed;
        // The cards' keys are at the end (biggestWinCard / biggestLossCard); the titles stay for older saves and as notes
        public double biggestWin;            // > 0, or 0 = no winning card yet
        public string biggestWinTitle = "";  // the card's title as EventManager reported it (English); "" = untitled
        public double biggestLoss;           // < 0, or 0 = no losing card yet
        public string biggestLossTitle = ""; // likewise
        public int crisesSolved;
        public int crisesTotal;
        public double casinoWon;
        public double casinoBet;
        public bool winShown;
        // Bankruptcy restarts (the hearing's coin flip came up heads). The cloud save server reads it: a restart puts
        // money back without a ledger entry, so it only counts as explained when this goes up.
        public int restarts;
        // The biggest win / loss card's translation key (RandomEvent.key; the Events table has key + ".title"); "" = none, or
        // a save from before the keys (CardTitle then finds the card by its English title)
        public string biggestWinCard = "";
        public string biggestLossCard = "";
    }

    State state = new State();
    bool restoredThisLoad;
    CompanyManager hookedCompany;
    EventManager hookedEvents;
    MiniGameManager hookedCrises;

    public double PeakValue => state.peakValue;
    public int Hires => state.hires;
    public int GoldenHires => state.goldenHires;
    public int RiskCardsPlayed => state.riskCardsPlayed;
    public double BiggestWin => state.biggestWin;
    public string BiggestWinTitle => state.biggestWinTitle;
    public double BiggestLoss => state.biggestLoss;
    public string BiggestLossTitle => state.biggestLossTitle;
    public string BiggestWinCard => state.biggestWinCard;
    public string BiggestLossCard => state.biggestLossCard;
    public int CrisesSolved => state.crisesSolved;
    public int CrisesTotal => state.crisesTotal;
    public double CasinoWon => state.casinoWon;
    public double CasinoBet => state.casinoBet;
    /// <summary>Won minus bet on pure-money gambles (negative = the house won).</summary>
    public double CasinoNet => state.casinoWon - state.casinoBet;
    public bool HasGambled => state.casinoBet > 0.0 || state.casinoWon > 0.0;
    /// <summary>The $10B win screen was already shown this run.</summary>
    public bool WinShown => state.winShown;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Instance = null;
        Changed = null;
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[RunStats] Removed a second RunStats on '" + name + "'.");
            Destroy(this);
            return;
        }
        Instance = this;
    }

    void OnEnable()
    {
        BuildGambleView.GambleSettled += OnGambleSettled;
        CasinoManager.MoneyMoved += OnGambleSettled;
        Hook();
    }

    void OnDisable()
    {
        BuildGambleView.GambleSettled -= OnGambleSettled;
        CasinoManager.MoneyMoved -= OnGambleSettled;
        Unhook();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        Hook();   // the managers can be replaced (e.g. a scene reload)
        TrackPeak();
    }

    // ------------------------------------------------------------------
    // Listening
    // ------------------------------------------------------------------

    void Hook()
    {
        CompanyManager company = CompanyManager.Instance;
        if (company != hookedCompany)
        {
            if (hookedCompany != null)
            {
                hookedCompany.EmployeeHired -= OnEmployeeHired;
                hookedCompany.StateLoaded -= OnStateLoaded;
            }
            hookedCompany = company;
            if (company != null)
            {
                company.EmployeeHired += OnEmployeeHired;
                company.StateLoaded += OnStateLoaded;
            }
        }

        EventManager events = EventManager.Instance;
        if (events != hookedEvents)
        {
            if (hookedEvents != null) hookedEvents.CardResolvedWithKey -= OnCardResolved;
            hookedEvents = events;
            if (events != null) events.CardResolvedWithKey += OnCardResolved;
        }

        MiniGameManager crises = MiniGameManager.Instance;
        if (crises != hookedCrises)
        {
            if (hookedCrises != null) hookedCrises.MiniGameCompleted -= OnCrisisFinished;
            hookedCrises = crises;
            if (crises != null) crises.MiniGameCompleted += OnCrisisFinished;
        }
    }

    void Unhook()
    {
        if (hookedCompany != null)
        {
            hookedCompany.EmployeeHired -= OnEmployeeHired;
            hookedCompany.StateLoaded -= OnStateLoaded;
        }
        if (hookedEvents != null) hookedEvents.CardResolvedWithKey -= OnCardResolved;
        if (hookedCrises != null) hookedCrises.MiniGameCompleted -= OnCrisisFinished;
        hookedCompany = null;
        hookedEvents = null;
        hookedCrises = null;
    }

    /// <summary>The saved game is in place (or there is no SaveSystem): values from before that would be the fresh scene's.</summary>
    static bool GameReady()
    {
        return SaveSystem.Instance == null || SaveSystem.Instance.IsGameLoaded;
    }

    static double CompanyValue()
    {
        return CompanyManager.Instance != null ? Math.Max(0.0, CompanyManager.Instance.companyValue) : 0.0;
    }

    void TrackPeak()
    {
        if (CompanyManager.Instance == null || !GameReady()) return;
        double value = CompanyManager.Instance.companyValue;
        if (value > state.peakValue && !double.IsNaN(value) && !double.IsInfinity(value))
            state.peakValue = value;
    }

    void OnEmployeeHired(ApplicantData applicant)
    {
        if (applicant == null) return;
        state.hires++;
        if (applicant.isGolden) state.goldenHires++;
        RaiseChanged();
    }

    void OnCardResolved(string title, string key, double change)
    {
        if (double.IsNaN(change) || double.IsInfinity(change)) change = 0.0;
        state.riskCardsPlayed++;
        // The English title and the card's key; an untitled card is saved as "" (the screens say "a risk card": CardTitle)
        string cardName = string.IsNullOrWhiteSpace(title) ? "" : title.Trim();
        string cardKey = string.IsNullOrWhiteSpace(key) ? "" : key.Trim();
        if (change > state.biggestWin)
        {
            state.biggestWin = change;
            state.biggestWinTitle = cardName;
            state.biggestWinCard = cardKey;
        }
        if (change < state.biggestLoss)
        {
            state.biggestLoss = change;
            state.biggestLossTitle = cardName;
            state.biggestLossCard = cardKey;
        }
        RaiseChanged();
    }

    void OnCrisisFinished(bool success, float reward)
    {
        state.crisesTotal++;
        if (success) state.crisesSolved++;
        RaiseChanged();
    }

    void OnGambleSettled(double bet, double won)
    {
        if (bet > 0.0 && !double.IsInfinity(bet)) state.casinoBet += bet;
        if (won > 0.0 && !double.IsInfinity(won)) state.casinoWon += won;
        RaiseChanged();
    }

    static void RaiseChanged()
    {
        // Each listener on its own: one that throws (or outlived its scene) mustn't silence the others
        Action handlers = Changed;
        if (handlers == null) return;
        foreach (Action handler in handlers.GetInvocationList())
        {
            try { handler(); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }

    // ------------------------------------------------------------------
    // Win flag and summary
    // ------------------------------------------------------------------

    /// <summary>Remember that the $10B win was shown (it never shows again this run).</summary>
    public void MarkWinShown()
    {
        state.winShown = true;
    }

    /// <summary>Bankruptcy restarts this run (RunEndDirector.ApplyCoinFlip on heads).</summary>
    public int Restarts => state.restarts;

    /// <summary>Count a bankruptcy restart (saved with the run stats; the cloud save server checks it).</summary>
    public void CountRestart()
    {
        state.restarts++;
    }

    /// <summary>
    /// A saved biggest win / loss card as the end screens name it: its title in the current language (the Events table,
    /// by the saved key; a save from before the keys finds the card by its English title), the saved title itself when the
    /// card is unknown, or "a risk card" in the current language for an untitled one (older saves wrote that English
    /// stand-in into the save: it is recognised).
    /// </summary>
    public static string CardTitle(string savedTitle, string savedKey)
    {
        string title = savedTitle != null ? savedTitle.Trim() : "";
        if (title.Length == 0 || title == LegacyUnnamedCard) return L10n.Get(RunEndUI.Table, "runend.card.unnamed");
        if (L10n.CurrentCode == L10n.English) return title;
        string key = !string.IsNullOrWhiteSpace(savedKey) ? savedKey.Trim() : EventDatabase.KeyForTitle(title);
        return string.IsNullOrEmpty(key) ? title : L10n.GetOr(RandomEvent.Table, key + ".title", title);
    }

    /// <summary>A saved title without its card key (CardTitle(title, key) with the key looked up by the English title).</summary>
    public static string CardTitle(string savedTitle) => CardTitle(savedTitle, null);

    /// <summary>What saves from before the localization wrote for an untitled risk card.</summary>
    const string LegacyUnnamedCard = "a risk card";

    /// <summary>Everything the end screens show, with the company as it is now.</summary>
    public RunSummary Summarize()
    {
        CompanyManager company = CompanyManager.Instance;
        TrackPeak();
        return new RunSummary
        {
            companyName = CompanyNameUI.GetCompanyName(),
            day = company != null ? Mathf.Max(1, company.currentDay) : 1,
            companyValue = CompanyValue(),
            peakValue = Math.Max(state.peakValue, CompanyValue()),
            hires = state.hires,
            goldenHires = state.goldenHires,
            riskCardsPlayed = state.riskCardsPlayed,
            biggestWin = state.biggestWin,
            biggestWinTitle = state.biggestWinTitle,
            biggestWinCard = state.biggestWinCard,
            biggestLoss = state.biggestLoss,
            biggestLossTitle = state.biggestLossTitle,
            biggestLossCard = state.biggestLossCard,
            crisesSolved = state.crisesSolved,
            crisesTotal = state.crisesTotal,
            casinoNet = CasinoNet,
            hasGambled = HasGambled
        };
    }

    // ------------------------------------------------------------------
    // Save
    // ------------------------------------------------------------------

    public string SaveKey => "runStats";

    public string CaptureState()
    {
        TrackPeak();
        return JsonUtility.ToJson(state);
    }

    public void RestoreState(string json)
    {
        State loaded = null;
        if (!string.IsNullOrEmpty(json))
        {
            try { loaded = JsonUtility.FromJson<State>(json); }
            catch (Exception e) { Debug.LogWarning("[RunStats] Couldn't read the saved run stats (starting from the current state): " + e.Message); }
        }

        if (loaded == null)
        {
            ResetToCurrent();
        }
        else
        {
            state = loaded;
            Sanitize();
            state.peakValue = Math.Max(state.peakValue, CompanyValue());
        }
        restoredThisLoad = true;
        RaiseChanged();
    }

    /// <summary>After a load: a save without run stats (older version) starts from the current state.</summary>
    void OnStateLoaded()
    {
        if (!restoredThisLoad)
        {
            ResetToCurrent();
            RaiseChanged();
        }
        restoredThisLoad = false;
    }

    void ResetToCurrent()
    {
        state = new State { peakValue = CompanyValue() };
    }

    void Sanitize()
    {
        state.hires = Mathf.Max(0, state.hires);
        state.goldenHires = Mathf.Clamp(state.goldenHires, 0, state.hires);
        state.riskCardsPlayed = Mathf.Max(0, state.riskCardsPlayed);
        state.crisesTotal = Mathf.Max(0, state.crisesTotal);
        state.crisesSolved = Mathf.Clamp(state.crisesSolved, 0, state.crisesTotal);
        state.peakValue = Finite(Math.Max(0.0, state.peakValue));
        state.biggestWin = Finite(Math.Max(0.0, state.biggestWin));
        state.biggestLoss = Finite(Math.Min(0.0, state.biggestLoss));
        state.casinoWon = Finite(Math.Max(0.0, state.casinoWon));
        state.casinoBet = Finite(Math.Max(0.0, state.casinoBet));
        if (state.biggestWinTitle == null) state.biggestWinTitle = "";
        if (state.biggestLossTitle == null) state.biggestLossTitle = "";
        if (state.biggestWinCard == null) state.biggestWinCard = "";
        if (state.biggestLossCard == null) state.biggestLossCard = "";
    }

    static double Finite(double value)
    {
        return double.IsNaN(value) || double.IsInfinity(value) ? 0.0 : value;
    }
}

/// <summary>A snapshot of the run for the end screens (RunStats.Summarize).</summary>
[Serializable]
public class RunSummary
{
    public string companyName = "";
    public int day = 1;
    public double companyValue;
    public double peakValue;
    public int hires;
    public int goldenHires;
    public int riskCardsPlayed;
    public double biggestWin;
    public string biggestWinTitle = "";
    public string biggestWinCard = "";    // the card's translation key ("" = none: RunStats.CardTitle finds it by the title)
    public double biggestLoss;
    public string biggestLossTitle = "";
    public string biggestLossCard = "";
    public int crisesSolved;
    public int crisesTotal;
    public double casinoNet;
    public bool hasGambled;
}
