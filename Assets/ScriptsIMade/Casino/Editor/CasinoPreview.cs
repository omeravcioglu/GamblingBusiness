using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Batch preview of the casino's runtime screens in the states of the design boards (hub; slots after a win; coin flip
/// after a win; blackjack while playing and after a win), rendered to PNGs by the UI session's UIPreview.RenderWith
/// (built-in pipeline for the capture, the project's pipeline restored after). UIPreview is looked up by name, so this
/// file doesn't depend on it to compile.
///   Unity.exe -batchmode -projectPath . -executeMethod CasinoPreview.Run -previewDir &lt;dir&gt; -logFile casino_preview.log
/// </summary>
public static class CasinoPreview
{
    const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    public static void Run()
    {
        string dir = "CasinoPreview";
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "-previewDir") dir = args[i + 1];
        var log = new List<string>();
        int failures = 0;
        System.Type preview = System.Type.GetType("UIPreview");
        MethodInfo renderWith = preview != null ? preview.GetMethod("RenderWith", BindingFlags.Public | BindingFlags.Static) : null;
        MethodInfo ensurePipeline = preview != null ? preview.GetMethod("EnsureProjectPipeline", BindingFlags.Public | BindingFlags.Static) : null;
        if (renderWith == null)
        {
            Debug.LogError("[CasinoPreview] UIPreview.RenderWith isn't there (Editor/NewUI); nothing rendered.");
            if (Application.isBatchMode) EditorApplication.Exit(1);
            return;
        }
        void Shot(string name, System.Action<CasinoView> setup)
        {
            try
            {
                System.Func<Camera, List<GameObject>> build = cam =>
                {
                    var made = new List<GameObject>();
                    var gameObjects = new GameObject("Games");
                    made.Add(gameObjects);
                    CasinoView view = CasinoView.Build(null, null, 110);
                    made.Add(view.gameObject);
                    Canvas canvas = view.GetComponent<Canvas>();
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = cam;
                    canvas.planeDistance = 10f;
                    view.slots.game = gameObjects.AddComponent<SlotMachineGame>();
                    view.root.gameObject.SetActive(true);
                    foreach (CasinoScreen screen in new CasinoScreen[] { view.hub, view.slots, view.coinFlip, view.blackjack })
                        screen.gameObject.SetActive(false);
                    setup(view);
                    return made;
                };
                renderWith.Invoke(null, new object[] { System.IO.Path.Combine(dir, name + ".png"), log, build });
            }
            catch (System.Exception e)
            {
                failures++;
                log.Add("FAILED " + name + ": " + e);
            }
        }

        Shot("casino_hub", Hub);
        Shot("casino_slots", Slots);
        Shot("casino_coinflip", CoinFlip);
        Shot("casino_blackjack_play", view => Blackjack(view, false));
        Shot("casino_blackjack_result", view => Blackjack(view, true));
        if (ensurePipeline != null) ensurePipeline.Invoke(null, new object[] { log });
        Debug.Log("[CasinoPreview]\n" + string.Join("\n", log));
        if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    static object Call(object target, string method, params object[] args)
    {
        System.Type type = target as System.Type ?? target.GetType();
        for (System.Type t = type; t != null; t = t.BaseType)
        {
            MethodInfo info = t.GetMethod(method, Any);
            if (info != null) return info.Invoke(target is System.Type ? null : target, args);
        }
        throw new MissingMethodException(type.Name + "." + method);
    }

    class MissingMethodException : System.Exception
    {
        public MissingMethodException(string what) : base("No method " + what) { }
    }

    static void Hub(CasinoView view)
    {
        CasinoHubView hub = view.hub;
        hub.gameObject.SetActive(true);
        hub.companyValueText.text = "$3,284,100";
        Call(hub, "OnValueShown");
        hub.chipGraphics[2].Selected = true;
        hub.chipLabels[2].anchoredPosition = new Vector2(0f, hub.chipGraphics[2].Lift);
        hub.customAmountInput.SetTextWithoutNotify("$1,000");
        hub.keepHintText.text = "The company keeps $1,000";
    }

    static void Slots(CasinoView view)
    {
        CasinoSlotsView slots = view.slots;
        slots.gameObject.SetActive(true);
        slots.companyValueText.text = "$3,286,500";
        slots.keepHintText.text = "The company keeps $1,000";
        slots.betText.text = "$100";
        // Reels like the board: cherry / BELL / seven, diamond / BELL / lemon, grape / BELL / star
        int[,] shown = { { 0, 4, 6 }, { 7, 4, 1 }, { 3, 4, 5 } };
        for (int r = 0; r < 3; r++)
        {
            CasinoSlotsView.Reel reel = slots.reels[r];
            reel.strip = new int[24];
            for (int i = 0; i < 24; i++) reel.strip[i] = (i * 3 + r) % 8;
            reel.position = 10f;
            reel.strip[11] = shown[r, 0];
            reel.strip[10] = shown[r, 1];
            reel.strip[9] = shown[r, 2];
            Call(slots, "DrawReel", reel);
        }
        Call(slots, "FillPayouts");
        Call(slots, "MarkRow", 3);
        Call(slots, "SetTray", 8, false);
        Call(slots, "ShowBanner", "WIN! $2,000 (20×)", UIColors.Gold, true);
    }

    static void CoinFlip(CasinoView view)
    {
        CasinoCoinFlipView flip = view.coinFlip;
        flip.gameObject.SetActive(true);
        flip.companyValueText.text = "$3,283,950";
        flip.keepHintText.text = "The company keeps $1,000";
        Call(flip, "StyleCall", flip.headsButton, flip.headsCheck, true, true);
        Call(flip, "StyleCall", flip.tailsButton, flip.tailsCheck, false, true);
        Call(typeof(CasinoScreen), "FitBetChip", flip.betChip, flip.betText, flip.betPlusButton.button, "$1,000", 320f);
        flip.resultTitle.text = "HEADS — you win!";
        flip.resultTitle.color = UIColors.Money;
        flip.amountLabel.text = "Pot:";
        flip.amountValue.text = "$1,950";
        flip.streakText.text = "Streak 1";
        for (int i = 0; i < flip.streakPips.Length; i++)
        {
            bool lit = i < 1;
            flip.streakPips[i].SetStyle(lit ? UIColors.Gold : UIColors.PaperHi, lit ? UIColors.Ink : UIColors.Pencil, 2f);
            flip.streakPips[i].SetDash(!lit, 3f, 2.5f);
        }
        flip.flipButton.gameObject.SetActive(false);
        flip.doubleButton.gameObject.SetActive(true);
        flip.collectButton.gameObject.SetActive(true);
        flip.doubleButton.SetLabel("Double or nothing (pot $3,900)");
        flip.collectButton.SetLabel("Collect $1,950");
        Call(flip, "LayoutActions", flip.doubleButton, flip.collectButton);
    }

    static void Blackjack(CasinoView view, bool result)
    {
        CasinoBlackjackView table = view.blackjack;
        table.gameObject.SetActive(true);
        table.companyValueText.text = result ? "$3,285,100" : "$3,283,100";
        table.keepHintText.text = "The company keeps $1,000";
        table.emptySlots.SetActive(false);
        // Dealer 9 of diamonds + hole (9 of spades), you 6 of clubs, 5 of hearts (+ 9 of hearts)
        Card(table, 8 + 26, true, 526f, 180f);
        Card(table, 8, result, 694f, 180f);
        Card(table, 5 + 39, true, 526f, 502f);
        Card(table, 4 + 13, true, 694f, 502f);
        if (result) Card(table, 8 + 13, true, 862f, 502f);
        table.dealerTotalChip.SetActive(true);
        table.playerTotalChip.SetActive(true);
        table.dealerTotal.text = result ? "18" : "9 + ?";
        table.playerTotal.text = result ? "20" : "11";
        Call(typeof(CasinoScreen), "FitBetChip", table.betChip, table.betText, table.betPlusButton.button, "$1,000", 300f);
        CasinoUI.ChipColors(1000, out Color chip, out Color spots, out Color label);
        foreach (CasinoChip c in table.stackChips) { c.ChipColor = chip; c.SpotColor = spots; }
        table.stackLabel.text = "$1K";
        table.stackLabel.color = label;
        table.betSlip.SetActive(false);
        table.resultBanner.SetActive(result);
        foreach (GameObject sparkle in table.resultSparkles) sparkle.SetActive(result);
        table.playActions.SetActive(!result);
        table.resultActions.SetActive(result);
        if (result)
        {
            table.resultTitle.text = "You WIN! 20 vs 18";
            table.resultTitle.color = UIColors.Money;
            table.resultAmount.text = "+$1,000";
            table.resultAmount.color = UIColors.Money;
            table.resultNote.text = "Paid 1:1 on your $1,000 bet";
        }
        else
        {
            table.dealButton.Interactable = false;
            table.doubleButton.SetLook(CasinoButton.Look.Gold, true);
            table.doubleNote.text = "bet $2,000 · one more card";
            // The Off look is applied in LateUpdate at runtime; apply it now for the capture
            table.dealButton.SetLook(CasinoButton.Look.Paper, false);
            table.dealButton.SetLook(CasinoButton.Look.Gold, false);
        }
    }

    static void Card(CasinoBlackjackView table, int code, bool faceUp, float x, float y)
    {
        CasinoCard card = CasinoUI.Card(table.cardLayer, "Card", table.font != null ? table.font : CasinoUI.ResolveFont(null), 0f, 0f);
        RectTransform rect = (RectTransform)card.transform;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(x + 75f, -(y + 105f));
        card.Set(code, faceUp);
    }
}
