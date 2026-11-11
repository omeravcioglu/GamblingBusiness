using System;
using UnityEngine;

/// <summary>
/// The bank loan offered at a bankruptcy hearing (only one at a time).
/// - Amount: max(minimumLoan, peakShare x the run's peak company value), rounded to roundTo ($50,000 / 10% / $1,000).
/// - The debt starts at the amount. At the end of every day (CompanyManager.DayEnded) it grows by dailyInterest (3%,
///   compounding), then autoRepayShare (50%) of that day's positive operating profit
///   (CompanyManager.LastDayBreakdown.operatingResult) pays it down automatically.
/// - RepayAsMuchAsPossible (the debt chip's Repay button) pays what the company can spare, keeping repayReserve so paying
///   the bank can't bankrupt it, until the debt is $0. A toast says when the loan is paid off.
/// - Saved with the game (ISaveParticipant "loan"). A HUD can read Debt / HasLoan and listen to the static DebtChanged.
/// - Its toasts come from the "RunEnd" String Table (runend.loan.toast.*).
/// Created at runtime by RunEndDirector when MainGame loads.
/// </summary>
[DisallowMultipleComponent]
public class BankLoan : MonoBehaviour, ISaveParticipant
{
    public static BankLoan Instance { get; private set; }

    /// <summary>
    /// Raised whenever the debt changes (argument: the debt now; 0 = no loan). Static, so a HUD can subscribe before the
    /// loan manager exists; it also fires after a save is loaded.
    /// </summary>
    public static event Action<double> DebtChanged;

    [Header("Loan amount")]
    [Tooltip("The smallest loan the bank gives")]
    public double minimumLoan = 50000;
    [Tooltip("...or this share of the run's peak company value, when that is more")]
    public double peakShare = 0.10;
    [Tooltip("The amount is rounded to a multiple of this")]
    public double roundTo = 1000;

    [Header("Paying it back")]
    [Tooltip("The debt grows by this share at the end of every day (compounding)")]
    public double dailyInterest = 0.03;
    [Tooltip("Share of each day's positive operating profit that pays the debt down automatically")]
    public double autoRepayShare = 0.5;
    [Tooltip("The Repay button always leaves the company at least this much, so paying the bank can't bankrupt it")]
    public double repayReserve = 1000;

    [Serializable]
    class State
    {
        public double debt;
        public double borrowed;       // amount of the current (or last) loan
        public int loansTaken;
        public double repaid;         // everything paid back this run
        public double interest;       // everything the debt grew by this run
    }

    State state = new State();
    bool restoredThisLoad;
    CompanyManager hookedCompany;

    /// <summary>What the company owes the bank ($0 = no loan).</summary>
    public double Debt => state.debt;
    public bool HasLoan => state.debt > 0.0;
    /// <summary>Amount of the current (or last) loan.</summary>
    public double Borrowed => state.borrowed;
    public int LoansTaken => state.loansTaken;
    public double TotalRepaid => state.repaid;
    public double TotalInterest => state.interest;

    /// <summary>Debt of the live loan manager (0 when there is none).</summary>
    public static double CurrentDebt => Instance != null ? Instance.Debt : 0.0;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Instance = null;
        DebtChanged = null;
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[BankLoan] Removed a second BankLoan on '" + name + "'.");
            Destroy(this);
            return;
        }
        Instance = this;
        if (DemoMode.Active) minimumLoan = DemoMode.StartingValue;   // Demo: the bank lends at least the demo's start
    }

    void OnEnable()
    {
        Hook();
    }

    void OnDisable()
    {
        Unhook();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        Hook();
    }

    void Hook()
    {
        CompanyManager company = CompanyManager.Instance;
        if (company == hookedCompany) return;
        Unhook();
        hookedCompany = company;
        if (company != null)
        {
            company.DayEnded += OnDayEnded;
            company.StateLoaded += OnStateLoaded;
        }
    }

    void Unhook()
    {
        if (hookedCompany != null)
        {
            hookedCompany.DayEnded -= OnDayEnded;
            hookedCompany.StateLoaded -= OnStateLoaded;
        }
        hookedCompany = null;
    }

    // ------------------------------------------------------------------
    // Loan
    // ------------------------------------------------------------------

    /// <summary>What the bank would lend right now: max(minimumLoan, peakShare x peak value), rounded to roundTo.</summary>
    public double QuoteLoan()
    {
        double peak = RunStats.Instance != null ? RunStats.Instance.PeakValue : 0.0;
        if (CompanyManager.Instance != null) peak = Math.Max(peak, CompanyManager.Instance.companyValue);
        return RoundAmount(Math.Max(minimumLoan, peak * peakShare), roundTo);
    }

    /// <summary>Round to a multiple of <paramref name="step"/> (nearest; halves away from zero).</summary>
    public static double RoundAmount(double amount, double step)
    {
        if (double.IsNaN(amount) || double.IsInfinity(amount)) return 0.0;
        if (step <= 0.0) return Math.Round(amount);
        return Math.Round(amount / step, MidpointRounding.AwayFromZero) * step;
    }

    /// <summary>Borrow QuoteLoan() into company value. False when a loan is already out (or there's no company).</summary>
    public bool TakeLoan()
    {
        CompanyManager company = CompanyManager.Instance;
        if (HasLoan || company == null) return false;
        double amount = QuoteLoan();
        if (amount <= 0.0) return false;

        state.debt = amount;
        state.borrowed = amount;
        state.loansTaken++;
        company.AddMoney(amount, true);
        Notify(state.debt);
        return true;
    }

    /// <summary>
    /// Pay as much of the debt as the company can spare (it keeps repayReserve). Returns what was paid (0 = nothing).
    /// Tells the player how it went.
    /// </summary>
    public double RepayAsMuchAsPossible()
    {
        CompanyManager company = CompanyManager.Instance;
        if (!HasLoan || company == null) return 0.0;

        double spare = Math.Floor(company.companyValue - Math.Max(0.0, repayReserve));
        double pay = Math.Min(state.debt, spare);
        if (pay <= 0.0)
        {
            GameNotifications.Show(L10n.Get(RunEndUI.Table, "runend.loan.toast.nothing_to_spare", ("amount", UIFormat.Money(repayReserve))), NotificationKind.Bad);
            return 0.0;
        }

        Pay(pay);
        if (HasLoan)
            GameNotifications.Show(L10n.Get(RunEndUI.Table, "runend.loan.toast.paid", ("amount", UIFormat.Money(pay)), ("debt", UIFormat.Money(state.debt))),
                                   NotificationKind.Info);
        return pay;
    }

    /// <summary>Wipe the debt (the last coin flip came up heads). No toast: the hearing says it.</summary>
    public void Forgive()
    {
        if (!HasLoan) return;
        state.debt = 0.0;
        Notify(0.0);
    }

    void Pay(double amount)
    {
        CompanyManager company = CompanyManager.Instance;
        if (company == null || amount <= 0.0) return;
        amount = Math.Min(amount, state.debt);
        company.AddMoney(-amount, true);
        state.debt -= amount;
        state.repaid += amount;
        if (state.debt < 0.005) state.debt = 0.0;
        Notify(state.debt);
        if (!HasLoan)
            GameNotifications.Show(L10n.Get(RunEndUI.Table, "runend.loan.toast.paid_off"), NotificationKind.Good);
        if (!HasLoan) GameSfx.Play(Sfx.UiConfirm);   // paid off
    }

    // End of a day: interest first, then half of the day's profit goes to the bank
    void OnDayEnded(int day, double change)
    {
        CompanyManager company = CompanyManager.Instance;
        if (!HasLoan || company == null) return;

        double interest = state.debt * Math.Max(0.0, dailyInterest);
        if (interest > 0.0 && !double.IsInfinity(interest))
        {
            state.debt += interest;
            state.interest += interest;
        }

        // The company keeps repayReserve, like the Repay button: luck applied after the day's profit can leave little
        // cash, and repaying down to $0 would reopen the bankruptcy hearing
        double profit = company.LastDayBreakdown.operatingResult;
        double pay = 0.0;
        if (profit > 0.0 && autoRepayShare > 0.0)
            pay = Math.Min(Math.Min(state.debt, profit * autoRepayShare), Math.Max(0.0, company.companyValue - Math.Max(0.0, repayReserve)));

        if (pay > 0.0) Pay(pay);
        else Notify(state.debt);
    }

    static void Notify(double debt)
    {
        // Each listener on its own: one that throws (or outlived its scene) mustn't silence the others
        Action<double> handlers = DebtChanged;
        if (handlers == null) return;
        foreach (Action<double> handler in handlers.GetInvocationList())
        {
            try { handler(debt); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }

    // ------------------------------------------------------------------
    // Save
    // ------------------------------------------------------------------

    public string SaveKey => "loan";

    public string CaptureState()
    {
        return JsonUtility.ToJson(state);
    }

    public void RestoreState(string json)
    {
        State loaded = null;
        if (!string.IsNullOrEmpty(json))
        {
            try { loaded = JsonUtility.FromJson<State>(json); }
            catch (Exception e) { Debug.LogWarning("[BankLoan] Couldn't read the saved loan (starting without one): " + e.Message); }
        }
        state = loaded ?? new State();
        if (double.IsNaN(state.debt) || double.IsInfinity(state.debt) || state.debt < 0.005) state.debt = 0.0;
        restoredThisLoad = true;
        Notify(state.debt);
    }

    /// <summary>After a load: a save without a loan entry means no loan.</summary>
    void OnStateLoaded()
    {
        if (!restoredThisLoad)
        {
            state = new State();
            Notify(0.0);
        }
        restoredThisLoad = false;
    }
}
