using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Batch smoke test for the company casino: in an empty scene with a CompanyManager (the bootstraps add the casino, the
/// heat meter and the run stats) it opens the casino and plays every game through its runtime screens - bets, MAX and the
/// $1,000 the company keeps, slots, coin flip with double or nothing, blackjack with a double down, the heat freeze,
/// Escape never mid-round - checking the money, the GambleLedger reports and RunStats' casino money after every round.
/// Any exception or error logged fails it, and so does a warning from casino code.
///   Unity.exe -batchmode -projectPath . -executeMethod CasinoSmoke.Run -logFile casino_smoke.log
/// Exit code 0 = all passed.
/// </summary>
[InitializeOnLoad]
public static class CasinoSmoke
{
    const string RunningKey = "CasinoSmoke.Running";
    const float Timeout = 300f;

    static IEnumerator script;
    static float waitUntil;
    static float started = -1f;
    static readonly List<string> problems = new List<string>();
    static readonly List<string> logs = new List<string>();
    static readonly List<string> notes = new List<string>();

    static CasinoSmoke()
    {
        if (!SessionState.GetBool(RunningKey, false)) return;
        if (!Application.isBatchMode)
        {
            // it ends by quitting the editor: never in an interactive session
            SessionState.EraseBool(RunningKey);
            return;
        }
        // after the domain reload of entering play mode
        EditorApplication.update += Tick;
        Application.logMessageReceived += OnLog;
    }

    public static void Run()
    {
        if (!Application.isBatchMode)
        {
            Debug.LogWarning("[CasinoSmoke] Batch mode only (it quits the editor when done).");
            return;
        }
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            new GameObject("Camera", typeof(Camera));
            new GameObject("Company").AddComponent<CompanyManager>();
            SessionState.SetBool(RunningKey, true);
            EditorApplication.EnterPlaymode();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            SessionState.EraseBool(RunningKey);
            EditorApplication.Exit(1);
        }
    }

    static void OnLog(string message, string stack, LogType type)
    {
        if (type == LogType.Log) return;
        // Every exception or error fails the test; a warning only when casino code logged it (other systems in the
        // scene, like the online city's UI Toolkit panels, warn about their own setup in an empty scene)
        bool serious = type == LogType.Exception || type == LogType.Error || type == LogType.Assert;
        if (!serious && (stack == null || !(stack.Contains("Casino") || stack.Contains("SlotMachineGame") || stack.Contains("CoinFlipGame"))))
            return;
        logs.Add(type + ": " + message + (serious ? "\n" + stack : ""));
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        float now = Time.realtimeSinceStartup;
        if (started < 0f) started = now;
        if (now - started > Timeout)
        {
            Finish("timed out");
            return;
        }
        if (now < waitUntil) return;
        if (script == null) script = Script();
        try
        {
            if (!script.MoveNext())
            {
                Finish(null);
                return;
            }
            if (script.Current is float seconds) waitUntil = now + seconds;
        }
        catch (Exception e)
        {
            Finish("the script threw " + e);
        }
    }

    static void Check(bool ok, string what)
    {
        if (!ok) problems.Add(what);
    }

    static bool Near(double a, double b)
    {
        return Math.Abs(a - b) < 0.01;
    }

    // ------------------------------------------------------------------

    static IEnumerator Script()
    {
        // The bootstraps run when the scene loads
        for (int i = 0; i < 100 && (CasinoManager.Instance == null || HeatMeter.Instance == null || RunStats.Instance == null); i++)
            yield return 0.05f;
        CasinoManager casino = CasinoManager.Instance;
        CompanyManager company = CompanyManager.Instance;
        if (casino == null || company == null || HeatMeter.Instance == null || RunStats.Instance == null)
        {
            problems.Add("the casino, the heat meter or the run stats wasn't created (CasinoManager " + (casino != null) + ")");
            yield break;
        }
        Check(casino.slotMachine != null && casino.blackjack != null && casino.coinFlip != null, "the three games weren't created");
        yield return 0.2f;

        // Open (no SkillTreeManager here: the casino counts as unlocked)
        casino.OpenCasino();
        yield return 0.1f;
        CasinoView view = casino.RuntimeView;
        Check(casino.IsOpen, "the casino didn't open");
        Check(view != null, "no runtime screens");
        if (view == null) yield break;
        Check(casino.CurrentScreen == CasinoManager.CasinoScreen.Hub, "not on the hub after opening");
        Check(Time.timeScale == 0f, "the game isn't paused while the casino is open");
        Check(view.hub.gameObject.activeInHierarchy, "the hub isn't showing");

        // Bets: presets, MAX (the company keeps $1,000), custom, - and +
        view.hub.chipButtons[2].onClick.Invoke();
        Check(casino.GetCurrentBet() == 1000, "the $1K chip bet " + casino.GetCurrentBet());
        view.hub.chipButtons[4].onClick.Invoke();
        long max = (long)Math.Floor(company.companyValue - 1000);
        Check(casino.GetCurrentBet() == max, "MAX bet " + casino.GetCurrentBet() + ", expected " + max);
        casino.CommitTypedBet("$2,500");
        Check(casino.GetCurrentBet() == 2500, "typed $2,500 bet " + casino.GetCurrentBet());
        casino.CommitTypedBet("99999999999");
        Check(casino.GetCurrentBet() == max, "a huge typed bet wasn't capped to MAX (" + casino.GetCurrentBet() + ")");
        casino.SetBet(1000);
        casino.StepBet(1);
        Check(casino.GetCurrentBet() == 2000, "+ from $1,000 gave " + casino.GetCurrentBet());
        casino.StepBet(-1);
        casino.StepBet(-1);
        Check(casino.GetCurrentBet() == 500, "- - from $2,000 gave " + casino.GetCurrentBet());
        yield return 0.1f;
        Check(view.hub.chipGraphics[1].Selected, "the $500 chip isn't raised for a $500 bet");

        // ---- Slots (heat back to 0 before each game, so the rounds never freeze gambling)
        HeatMeter.Instance.SetHeat(0f, "smoke");
        casino.OpenSlots();
        yield return 0.1f;
        Check(casino.CurrentScreen == CasinoManager.CasinoScreen.Slots, "Play didn't open the slots");
        for (int spin = 0; spin < 6; spin++)
        {
            double before = company.companyValue;
            int records = GambleLedger.RecordCount;
            double runBet = RunStats.Instance.CasinoBet, runWon = RunStats.Instance.CasinoWon;
            view.slots.spinButton.button.onClick.Invoke();
            yield return null;
            Check(casino.slotMachine.IsSpinning, "SPIN didn't start a spin");
            Check(Near(company.companyValue, before - 500), "the spin took " + (before - company.companyValue));
            if (spin == 0)
            {
                UIManager.Instance.HandleEscape();
                yield return null;
                Check(casino.CurrentScreen == CasinoManager.CasinoScreen.Slots, "Escape left the slots mid-spin");
                Check(!view.slots.backButton.interactable, "Back to casino works mid-spin");
                Check(!casino.BackToHub(), "BackToHub worked mid-spin");
            }
            for (int i = 0; i < 120 && (casino.slotMachine.IsSpinning || view.slots.IsBusy); i++)
                yield return 0.05f;
            Check(!casino.slotMachine.IsSpinning && !view.slots.IsBusy, "the spin never finished");
            double won = casino.slotMachine.LastWinnings;
            Check(Near(company.companyValue, before - 500 + won), "after the spin the value is " + company.companyValue + ", expected " + (before - 500 + won));
            Check(GambleLedger.RecordCount == records + 1, "the spin reported " + (GambleLedger.RecordCount - records) + " rounds");
            GambleRecord record = GambleLedger.LastRecord;
            Check(record.source == GambleLedger.SourceCasino && Near(record.stake, 500) && Near(record.payout, won) && Near(record.companyValueBefore, before),
                  "slots report: " + record.source + " stake " + record.stake + " payout " + record.payout + " before " + record.companyValueBefore);
            Check(Near(RunStats.Instance.CasinoBet, runBet + 500) && Near(RunStats.Instance.CasinoWon, runWon + won), "RunStats didn't count the spin");
            for (int reel = 0; reel < 3; reel++)
            {
                CasinoIcon centre = view.slots.reels[reel].cells[2];   // cells: coming in, above, on the payline, below
                Check(centre.Kind == CasinoIcon.Symbol(casino.slotMachine.GetReelResult(reel)),
                      "reel " + reel + " shows " + centre.Kind + " but landed on " + casino.slotMachine.SymbolName(casino.slotMachine.GetReelResult(reel)));
            }
            notes.Add("spin " + spin + ": " + casino.slotMachine.SymbolName(casino.slotMachine.GetReelResult(0)) + " "
                      + casino.slotMachine.SymbolName(casino.slotMachine.GetReelResult(1)) + " " + casino.slotMachine.SymbolName(casino.slotMachine.GetReelResult(2))
                      + " = " + UIFormat.Money(won) + " / banner '" + view.slots.bannerText.text + "'");
        }
        UIManager.Instance.HandleEscape();
        yield return 0.1f;
        Check(casino.CurrentScreen == CasinoManager.CasinoScreen.Hub, "Escape didn't go back to the hub from the slots");

        // ---- Coin flip
        HeatMeter.Instance.SetHeat(0f, "smoke");
        casino.OpenCoinFlip();
        yield return 0.1f;
        Check(casino.CurrentScreen == CasinoManager.CasinoScreen.CoinFlip, "Play didn't open the coin flip");
        bool doubled = false, collected = false;
        for (int flip = 0; flip < 8 && !(doubled && collected); flip++)
        {
            double before = company.companyValue;
            int records = GambleLedger.RecordCount;
            view.coinFlip.flipButton.button.onClick.Invoke();
            yield return null;
            Check(casino.coinFlip.IsFlipping, "Flip didn't toss the coin");
            Check(Near(company.companyValue, before - 500), "the flip took " + (before - company.companyValue));
            if (flip == 0)
            {
                UIManager.Instance.HandleEscape();
                yield return null;
                Check(casino.CurrentScreen == CasinoManager.CasinoScreen.CoinFlip, "Escape left the coin flip mid-flip");
            }
            for (int i = 0; i < 80 && (casino.coinFlip.IsFlipping || view.coinFlip.IsBusy); i++)
                yield return 0.05f;
            bool won = casino.coinFlip.LastFlipWon;
            double paid = won ? 500 * 1.95 : 0.0;
            Check(Near(company.companyValue, before - 500 + paid), "after the flip the value is " + company.companyValue + ", expected " + (before - 500 + paid));
            Check(GambleLedger.RecordCount == records + 1 && Near(GambleLedger.LastRecord.payout, paid), "the flip wasn't reported once");
            yield return 0.1f;
            if (!won)
            {
                Check(view.coinFlip.flipButton.gameObject.activeSelf, "no Flip button after a loss");
                continue;
            }
            Check(casino.coinFlip.HasPot && Near(casino.coinFlip.Pot, paid), "no pot after a win");
            Check(view.coinFlip.doubleButton.gameObject.activeSelf && view.coinFlip.collectButton.gameObject.activeSelf, "no Double / Collect after a win");
            if (!doubled)
            {
                // Double or nothing: the pot goes up again
                double pot = casino.coinFlip.Pot;
                double beforeDouble = company.companyValue;
                view.coinFlip.doubleButton.button.onClick.Invoke();
                yield return null;
                Check(casino.coinFlip.IsFlipping && casino.coinFlip.LastFlipWasDouble, "Double or nothing didn't toss the coin");
                Check(Near(company.companyValue, beforeDouble - pot), "double or nothing took " + (beforeDouble - company.companyValue) + ", pot " + pot);
                for (int i = 0; i < 80 && (casino.coinFlip.IsFlipping || view.coinFlip.IsBusy); i++)
                    yield return 0.05f;
                double back = casino.coinFlip.LastFlipWon ? pot * 2 : 0;
                Check(Near(company.companyValue, beforeDouble - pot + back), "after double or nothing the value is " + company.companyValue);
                Check(Near(GambleLedger.LastRecord.stake, pot) && Near(GambleLedger.LastRecord.payout, back), "double or nothing wasn't reported as its own round");
                doubled = true;
                notes.Add("double or nothing on " + UIFormat.Money(pot) + ": " + (casino.coinFlip.LastFlipWon ? "won" : "lost"));
                yield return 0.1f;
            }
            if (casino.coinFlip.HasPot)
            {
                double beforeCollect = company.companyValue;
                view.coinFlip.collectButton.button.onClick.Invoke();
                yield return 0.1f;
                Check(Near(company.companyValue, beforeCollect), "Collect moved money");
                Check(!casino.coinFlip.HasPot, "the pot is still open after Collect");
                collected = true;
            }
        }
        notes.Add("coin flip: doubled " + doubled + ", collected " + collected);
        UIManager.Instance.HandleEscape();
        yield return 0.1f;
        Check(casino.CurrentScreen == CasinoManager.CasinoScreen.Hub, "Escape didn't go back to the hub from the coin flip");

        // ---- Blackjack
        HeatMeter.Instance.SetHeat(0f, "smoke");
        casino.OpenBlackjack();
        yield return 0.1f;
        Check(casino.CurrentScreen == CasinoManager.CasinoScreen.Blackjack, "Play didn't open blackjack");
        Check(view.blackjack.betSlip.activeSelf, "no bet slip before the first deal");
        bool triedDouble = false;
        for (int hand = 0; hand < 10; hand++)
        {
            double before = company.companyValue;
            int records = GambleLedger.RecordCount;
            view.blackjack.dealButton.button.onClick.Invoke();
            yield return null;
            Check(casino.blackjack.HandNumber > 0, "Deal didn't deal");
            if (hand == 0 && casino.blackjack.IsHandInProgress)
            {
                UIManager.Instance.HandleEscape();
                yield return null;
                Check(casino.CurrentScreen == CasinoManager.CasinoScreen.Blackjack, "Escape left the table mid-hand");
            }
            for (int i = 0; i < 80 && view.blackjack.IsBusy; i++)
                yield return 0.05f;
            int guard = 0;
            while (casino.blackjack.IsHandInProgress && guard++ < 12)
            {
                if (!triedDouble && casino.blackjack.CanDoubleDown && view.blackjack.doubleButton.button.interactable)
                {
                    double beforeDouble = company.companyValue;
                    view.blackjack.doubleButton.button.onClick.Invoke();
                    triedDouble = true;
                    Check(casino.blackjack.Doubled && casino.blackjack.CurrentBet == 1000, "Double Down didn't double the bet");
                    Check(company.companyValue <= beforeDouble - 500 + casino.blackjack.LastPayout + 0.01, "Double Down didn't take the extra bet");
                }
                else if (casino.blackjack.PlayerTotal < 15) view.blackjack.hitButton.button.onClick.Invoke();
                else view.blackjack.standButton.button.onClick.Invoke();
                yield return null;
                for (int i = 0; i < 80 && view.blackjack.IsBusy; i++)
                    yield return 0.05f;
            }
            for (int i = 0; i < 80 && view.blackjack.IsBusy; i++)
                yield return 0.05f;
            Check(!casino.blackjack.IsHandInProgress, "a hand never ended");
            double stake = casino.blackjack.CurrentBet;
            double paid = casino.blackjack.LastPayout;
            Check(Near(company.companyValue, before - stake + paid), "after the hand the value is " + company.companyValue + ", expected " + (before - stake + paid));
            Check(GambleLedger.RecordCount == records + 1 && Near(GambleLedger.LastRecord.stake, stake) && Near(GambleLedger.LastRecord.payout, paid)
                  && Near(GambleLedger.LastRecord.companyValueBefore, before), "the hand wasn't reported once with its whole stake");
            Check(view.blackjack.resultBanner.activeSelf, "no result banner after the hand");
            Check(view.blackjack.resultActions.activeSelf, "no Deal again after the hand");
            int shown = 0;
            foreach (Transform card in view.blackjack.cardLayer)
                if (card.gameObject.activeSelf) shown++;
            Check(shown == casino.blackjack.PlayerCards.Count + casino.blackjack.DealerCards.Count,
                  "the table shows " + shown + " cards, the hand has " + (casino.blackjack.PlayerCards.Count + casino.blackjack.DealerCards.Count));
            notes.Add("hand " + hand + ": " + casino.blackjack.LastResult + " " + casino.blackjack.PlayerTotal + " vs " + casino.blackjack.DealerTotal
                      + ", stake " + stake + ", paid " + paid + " / '" + view.blackjack.resultTitle.text + "'");
            yield return 0.1f;
        }
        notes.Add("blackjack: double down tried " + triedDouble);
        UIManager.Instance.HandleEscape();
        yield return 0.1f;
        Check(casino.CurrentScreen == CasinoManager.CasinoScreen.Hub, "Escape didn't go back to the hub from blackjack");

        // ---- The company keeps $1,000
        HeatMeter.Instance.SetHeat(0f, "smoke");
        company.AddMoney(1600 - company.companyValue, true);
        Check(casino.GetMaxBet() == 600, "MAX at $1,600 is " + casino.GetMaxBet());
        casino.SetBet(5000);
        Check(casino.GetCurrentBet() == 600, "a $5,000 bet at $1,600 plays as " + casino.GetCurrentBet());
        casino.OpenSlots();
        yield return 0.1f;
        for (int spin = 0; spin < 6; spin++)
        {
            if (!casino.CanBetAtAll) break;
            HeatMeter.Instance.SetHeat(0f, "smoke");
            view.slots.spinButton.button.onClick.Invoke();
            yield return null;
            Check(company.companyValue >= 1000 - 0.001, "a spin left the company with " + company.companyValue);
            for (int i = 0; i < 120 && (casino.slotMachine.IsSpinning || view.slots.IsBusy); i++)
                yield return 0.05f;
            casino.SetMaxBet();
        }
        company.AddMoney(1000 - company.companyValue, true);
        yield return null;
        Check(!casino.CanBetAtAll, "at $1,000 the casino still takes bets");
        double atReserve = company.companyValue;
        view.slots.spinButton.button.onClick.Invoke();
        casino.slotMachine.Spin();
        yield return null;
        Check(Near(company.companyValue, atReserve) && !casino.slotMachine.IsSpinning, "a spin went through at $1,000");
        company.AddMoney(50000 - company.companyValue, true);
        yield return 0.1f;

        // ---- Heat freeze: nothing is taken
        HeatMeter.Instance.SetHeat(100f, "smoke");
        Check(!HeatMeter.CanGamble(out _), "heat 100 didn't freeze gambling");
        casino.SetBet(500);
        double frozenValue = company.companyValue;
        int frozenRecords = GambleLedger.RecordCount;
        casino.slotMachine.Spin();
        yield return null;
        Check(Near(company.companyValue, frozenValue) && GambleLedger.RecordCount == frozenRecords && !casino.slotMachine.IsSpinning,
              "a spin went through while gambling was frozen");
        Check(view.slots.bannerText.text == HeatMeter.Instance.FreezeMessage, "the slots banner says '" + view.slots.bannerText.text + "' while frozen");
        HeatMeter.Instance.SetHeat(0f, "smoke");
        yield return 0.1f;

        // ---- Close: game, hub, closed
        UIManager.Instance.HandleEscape();
        yield return 0.1f;
        Check(casino.CurrentScreen == CasinoManager.CasinoScreen.Hub, "Escape didn't go back to the hub");
        UIManager.Instance.HandleEscape();
        yield return 0.1f;
        Check(!casino.IsOpen && !view.root.gameObject.activeSelf, "Escape on the hub didn't close the casino");
        Check(Time.timeScale == 1f, "the game is still paused after closing the casino");
        notes.Add("heat after the run: " + HeatMeter.CurrentHeat + ", casino net " + UIFormat.SignedMoney(RunStats.Instance.CasinoNet) + ", rounds " + GambleLedger.RecordCount);
    }

    static void Finish(string fatal)
    {
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= OnLog;
        SessionState.EraseBool(RunningKey);
        var lines = new List<string>();
        if (fatal != null) lines.Add("FATAL: " + fatal);
        foreach (string p in problems) lines.Add("problem: " + p);
        foreach (string l in logs) lines.Add("log: " + l);
        foreach (string n in notes) lines.Add("note: " + n);
        bool ok = fatal == null && problems.Count == 0 && logs.Count == 0;
        lines.Add(ok ? "Casino smoke test passed." : "Casino smoke test FAILED.");
        Debug.Log("[CasinoSmoke]\n" + string.Join("\n", lines));
        EditorApplication.Exit(ok ? 0 : 1);
    }
}
