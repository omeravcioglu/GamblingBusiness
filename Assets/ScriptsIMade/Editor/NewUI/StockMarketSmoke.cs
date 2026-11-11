using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Batch smoke test for StockMarket.prefab with the live market: in an empty scene (a CompanyManager worth $3,284,100,
/// a StockMarketManager, a StockMarketUI using the designed view; no skill tree, so buying is allowed; no tutorial
/// runner, so the market opens) it opens the market (live: the game isn't paused), picks TSLA from the list, buys 12,
/// switches to the Owned tab, sells them all with All, checks the recent trades, the not-enough-money state, the
/// stepper, a price tick, a language switch (Japanese, then back) and closes with Back to office.
/// Exit code 0 = all passed.
///   Unity.exe -batchmode -projectPath . -executeMethod StockMarketSmoke.Run -logFile smoke.log
/// </summary>
[InitializeOnLoad]
public static class StockMarketSmoke
{
    const string RunningKey = "StockMarketSmoke.Running";

    enum Step { Open, Pick, Buy, Owned, Sell, Broke, Stepper, Tick, Language, LanguageBack, Close, Done }

    static Step step;
    static int frames;
    static bool started;
    static string originalLanguage;
    static readonly List<string> problems = new List<string>();
    static readonly List<string> logs = new List<string>();
    static StockMarketUI ui;
    static StockMarketView view;
    static StockMarketManager market;
    static CompanyManager company;
    static double cashBefore;

    static StockMarketSmoke()
    {
        if (!SessionState.GetBool(RunningKey, false)) return;
        if (!Application.isBatchMode)
        {
            SessionState.EraseBool(RunningKey);
            return;
        }
        EditorApplication.update += Tick;
        Application.logMessageReceived += OnLog;
    }

    public static void Run()
    {
        if (!Application.isBatchMode)
        {
            Debug.LogWarning("[StockMarketSmoke] Batch mode only (it quits the editor when done).");
            return;
        }
        try
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(StockMarketBuilder.PrefabPath);
            if (prefab == null) throw new Exception("No " + StockMarketBuilder.PrefabPath + " - build it first.");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var screen = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var v = screen.GetComponent<StockMarketView>();
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            new GameObject("Camera", typeof(Camera));
            new GameObject("Company", typeof(CompanyManager));
            var m = new GameObject("Market", typeof(StockMarketManager));
            var u = m.AddComponent<StockMarketUI>();
            u.view = v;
            u.stockMarketPanel = v.root;
            u.closeButton = v.closeButton;
            u.shareAmountInput = v.amountInput;
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
        if (type == LogType.Log || type == LogType.Warning || !started) return;
        logs.Add(type + ": " + message.Split('\n')[0]);
    }

    static void Check(bool ok, string problem)
    {
        if (!ok) problems.Add(step + ": " + problem);
    }

    static void Go(Step s)
    {
        step = s;
        frames = 0;
    }

    static int ActiveChildren(RectTransform parent)
    {
        int n = 0;
        if (parent == null) return 0;
        foreach (Transform c in parent)
            if (c.gameObject.activeSelf) n++;
        return n;
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        frames++;
        if (frames < 3) return;   // let Start and the first renders run
        try
        {
            if (!started)
            {
                started = true;
                ui = UnityEngine.Object.FindAnyObjectByType<StockMarketUI>();
                market = StockMarketManager.Instance;
                company = CompanyManager.Instance;
                view = ui != null ? ui.view : null;
                if (ui == null || market == null || company == null || view == null)
                {
                    Finish("setup: StockMarketUI, StockMarketManager, CompanyManager or the view is missing");
                    return;
                }
                company.companyValue = 3284100;
                company.displayedValue = 3284100;
                originalLanguage = L10n.CurrentCode;
                Go(Step.Open);
                return;
            }

            switch (step)
            {
                case Step.Open:
                    if (frames == 3)
                    {
                        ui.OpenStockMarket();
                        return;
                    }
                    Check(ui.IsOpen && view.root.activeInHierarchy, "the market didn't open");
                    Check(UIManager.Instance != null && UIManager.Instance.IsPanelOpen(ui), "the market isn't an open panel (Escape wouldn't close it)");
                    Check(Time.timeScale > 0f, "opening the market paused the game (it's a live screen)");
                    Check(ActiveChildren(view.allRows) == market.stocks.Count, "the list shows " + ActiveChildren(view.allRows) + " rows for " + market.stocks.Count + " stocks");
                    Check(view.symbolText.text == market.stocks[0].symbol, "the first stock isn't selected (" + view.symbolText.text + ")");
                    Check(view.ownedCountText.text == "0", "the Owned tab counts " + view.ownedCountText.text);
                    Check(view.chart.Prices.Count == 30, "the chart has " + view.chart.Prices.Count + " prices");
                    Go(Step.Pick);
                    return;

                case Step.Pick:
                    if (frames == 3)
                    {
                        StockRowView row = null;
                        foreach (Transform c in view.allRows)
                        {
                            var r = c.GetComponent<StockRowView>();
                            if (r != null && c.gameObject.activeSelf && r.symbol == "TSLA") row = r;
                        }
                        Check(row != null, "no TSLA row");
                        if (row != null) row.button.onClick.Invoke();
                        return;
                    }
                    Check(view.symbolText.text == "TSLA", "clicking the TSLA row selected " + view.symbolText.text);
                    Check(view.buyTab.Current == StockTabLook.State.Active, "the ticket isn't on Buy");
                    Check(view.sellTab.Current == StockTabLook.State.Disabled, "Sell isn't disabled for a stock you don't own");
                    Check(view.noneBox.activeSelf && !view.ownBox.activeSelf, "the detail doesn't say you own no TSLA");
                    view.amountInput.text = "12";
                    Go(Step.Buy);
                    return;

                case Step.Buy:
                    if (frames == 3)
                    {
                        Check(view.actionText.text.Contains("12") && view.actionText.text.Contains("TSLA"), "the button reads '" + view.actionText.text + "'");
                        Check(view.actionButton.interactable, "the Buy button is off");
                        cashBefore = company.companyValue;
                        view.actionButton.onClick.Invoke();
                        return;
                    }
                    var tsla = market.GetStock("TSLA");
                    Check(tsla.ownedShares == 12, "bought " + tsla.ownedShares + " TSLA, not 12");
                    Check(company.companyValue < cashBefore, "buying didn't take money from the company");
                    Check(market.RecentTrades.Count == 1 && market.RecentTrades[0].buy && market.RecentTrades[0].shares == 12, "the trade wasn't recorded");
                    Check(view.messageGroup.activeSelf && view.messageOk.enabled, "no success message");
                    Check(view.ownBox.activeSelf, "the detail doesn't show the shares you own");
                    Check(view.ownedCountText.text == "1", "the Owned tab counts " + view.ownedCountText.text);
                    Check(view.sellTab.Current == StockTabLook.State.Inactive, "Sell isn't available after buying");
                    view.ownedTab.button.onClick.Invoke();
                    Go(Step.Owned);
                    return;

                case Step.Owned:
                    if (frames < 3) return;
                    Check(view.ownedView.activeSelf && !view.allView.activeSelf, "the Owned tab didn't open");
                    Check(ActiveChildren(view.ownedRows) == 1, "the Owned tab lists " + ActiveChildren(view.ownedRows) + " stocks");
                    Check(ActiveChildren(view.tradeRows) == 1, "recent trades show " + ActiveChildren(view.tradeRows) + " lines");
                    Check(view.totalsRow.activeSelf && view.splitBlock.activeSelf, "totals or the holdings bar are hidden");
                    view.sellTab.button.onClick.Invoke();
                    view.quickButton.onClick.Invoke();
                    Go(Step.Sell);
                    return;

                case Step.Sell:
                    if (frames == 3)
                    {
                        Check(view.sellTab.Current == StockTabLook.State.Active, "the ticket isn't on Sell");
                        Check(view.amountInput.text == "12", "All filled in '" + view.amountInput.text + "'");
                        Check(view.lines[4].row.activeSelf, "the sell breakdown has no profit line");
                        view.actionButton.onClick.Invoke();
                        return;
                    }
                    Check(market.GetStock("TSLA").ownedShares == 0, "selling left " + market.GetStock("TSLA").ownedShares + " TSLA");
                    Check(market.RecentTrades.Count == 2 && !market.RecentTrades[0].buy, "the sale wasn't recorded first");
                    Check(view.buyTab.Current == StockTabLook.State.Active, "the ticket didn't go back to Buy once nothing is owned");
                    Check(ActiveChildren(view.tradeRows) == 2, "recent trades show " + ActiveChildren(view.tradeRows) + " lines");
                    Check(view.ownedEmptyText.gameObject.activeSelf, "the empty Owned tab doesn't say so");
                    view.allTab.button.onClick.Invoke();
                    view.amountInput.text = "999999999";
                    Go(Step.Broke);
                    return;

                case Step.Broke:
                    if (frames < 3) return;
                    Check(!view.actionButton.interactable, "Buy stays on without the money");
                    Check(view.messageGroup.activeSelf && view.messageError.enabled, "no not-enough-money message");
                    view.quickButton.onClick.Invoke();
                    Go(Step.Stepper);
                    return;

                case Step.Stepper:
                    if (frames == 3)
                    {
                        long max = market.GetMaxAffordableShares(market.GetStock("TSLA"));
                        Check(view.amountInput.text == max.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), "Max filled in '" + view.amountInput.text + "' (max " + max + ")");
                        Check(view.actionButton.interactable, "Buy is off at the max amount");
                        view.amountInput.text = "5";
                        view.moreButton.onClick.Invoke();
                        view.moreButton.onClick.Invoke();
                        view.lessButton.onClick.Invoke();
                        return;
                    }
                    Check(view.amountInput.text == "6", "5 +1 +1 -1 gave '" + view.amountInput.text + "'");
                    market.ForceUpdate();
                    Go(Step.Tick);
                    return;

                case Step.Tick:
                    if (frames < 3) return;
                    Check(view.chart.Prices.Count == 30, "after a tick the chart has " + view.chart.Prices.Count + " prices");
                    Check(Mathf.Approximately(view.chart.Prices[29], market.GetStock("TSLA").currentPrice), "the chart doesn't end at the price");
                    L10n.SetLanguage("ja");
                    Go(Step.Language);
                    return;

                case Step.Language:
                    if (frames < 5) return;
                    Check(view.ticketSubText.text.Contains("1株"), "in Japanese the ticket reads '" + view.ticketSubText.text + "'");
                    var title = view.ticketRoot.GetComponentsInChildren<L10nText>(true);
                    bool found = false;
                    foreach (var t in title)
                        if (t.key == "stocks.ticket.title") { found = t.Label.text == "注文票"; break; }
                    Check(found, "the ticket title didn't switch to Japanese");
                    L10n.SetLanguage(string.IsNullOrEmpty(originalLanguage) ? L10n.English : originalLanguage);
                    Go(Step.LanguageBack);
                    return;

                case Step.LanguageBack:
                    if (frames < 5) return;
                    view.backButton.onClick.Invoke();
                    Go(Step.Close);
                    return;

                case Step.Close:
                    if (frames < 3) return;
                    Check(!ui.IsOpen && !view.root.activeInHierarchy, "Back to office didn't close the market");
                    Check(UIManager.Instance == null || !UIManager.Instance.IsPanelOpen(ui), "the market is still an open panel");
                    Finish(null);
                    return;
            }
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            Finish("exception in " + step + ": " + e.Message);
        }
    }

    static void Finish(string fatal)
    {
        if (step == Step.Done) return;
        step = Step.Done;
        if (fatal != null) problems.Add(fatal);
        foreach (var l in logs) problems.Add("log " + l);
        SessionState.EraseBool(RunningKey);
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= OnLog;
        if (!string.IsNullOrEmpty(originalLanguage)) L10n.SetLanguage(originalLanguage);
        Debug.Log(problems.Count == 0 ? "[StockMarketSmoke] PASSED" : "[StockMarketSmoke] FAILED\n- " + string.Join("\n- ", problems));
        EditorApplication.isPlaying = false;
        EditorApplication.delayCall += () => EditorApplication.Exit(problems.Count == 0 ? 0 : 1);
    }
}
