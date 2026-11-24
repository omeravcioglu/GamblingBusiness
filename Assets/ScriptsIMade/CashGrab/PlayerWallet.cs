using System;
using UnityEngine;

/// <summary>
/// The CEO's own money (Player Cash) for every system the player spends with: the casino, the build-budget gamble, the
/// "win it back" flip, the stock market and the cash floor's tools. Company income never lands here by itself: only bills
/// the CEO physically collects and deposits in their safe on the cash floor (CashGrabSystem / CashLedger).
/// - Player Cash is part of company value (the score), so a bet still moves company value: spending takes from both,
///   winnings add to both.
/// - Without a cash floor in the scene (an editor test scene, a smoke test) it falls back to company value, so those
///   systems keep working exactly as before.
/// </summary>
public static class PlayerWallet
{
    /// <summary>True when Player Cash is in charge (the cash floor runs and its save is loaded).</summary>
    public static bool Active => CashGrabSystem.Instance != null && CashGrabSystem.Instance.Ready;

    /// <summary>The money the CEO can spend now (Player Cash; company value without a cash floor).</summary>
    public static double Balance
    {
        get
        {
            if (Active) return Math.Max(0.0, CashGrabSystem.Instance.Ledger.PlayerCash);
            CompanyManager company = CompanyManager.Instance;
            return company != null ? Math.Max(0.0, company.companyValue) : 0.0;
        }
    }

    /// <summary>Whole dollars of Balance.</summary>
    public static double WholeBalance => Math.Floor(Balance + 1e-6);

    public static bool CanAfford(double amount)
    {
        if (double.IsNaN(amount) || amount <= 0.0) return false;
        return amount <= Balance + 0.005;
    }

    /// <summary>
    /// Take <paramref name="amount"/> (a bet, a share purchase, a tool). False when it isn't there, and then nothing is
    /// taken. <paramref name="source"/> names the spender for the log.
    /// </summary>
    public static bool TrySpend(double amount, string source)
    {
        if (double.IsNaN(amount) || double.IsInfinity(amount) || amount <= 0.0) return false;
        if (Active)
        {
            bool ok = CashGrabSystem.Instance.SpendPlayerCash(amount, source);
            return ok;
        }
        CompanyManager company = CompanyManager.Instance;
        if (company == null || amount > company.companyValue + 0.005) return false;
        company.AddMoney(-amount, true);
        return true;
    }

    /// <summary>Money coming back to the CEO (winnings, a refunded bet, a share sale).</summary>
    public static void Receive(double amount, string source)
    {
        if (double.IsNaN(amount) || double.IsInfinity(amount) || amount <= 0.0) return;
        if (Active)
        {
            CashGrabSystem.Instance.ReceivePlayerCash(amount, source);
            return;
        }
        CompanyManager company = CompanyManager.Instance;
        if (company != null) company.AddMoney(amount, true);
    }

    /// <summary>"Your cash" or "Company value" for a balance label, whichever Balance is.</summary>
    public static string BalanceLabel => Active ? CashText.Get("cash.wallet.label") : CashText.Get("cash.wallet.label_company");

    /// <summary>Why a spend can't happen: "Not enough cash: collect money on the cash floor (Tab)."</summary>
    public static string NotEnoughMessage(double needed)
    {
        return Active ? CashText.Get("cash.wallet.not_enough", ("amount", UIFormat.Money(needed)), ("cash", UIFormat.Money(Balance)))
                      : CashText.Get("cash.wallet.not_enough_company", ("amount", UIFormat.Money(needed)));
    }
}
