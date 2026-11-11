using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static UIBuild;

/// <summary>
/// The stock market (page "Stocks &amp; casino": boards "Stock market", "Stock market: selling (Owned tab)", "… buying
/// locked", "… not enough money"). Builds Assets/UI/Prefabs/StockMarket.prefab (canvas 85: above the HUD and below the
/// skill board, pause menu and pop-ups) with a StockMarketView, and installs it in MainGame: StockMarketUI.view points at
/// it and the fallback panel's references are cleared. Coordinates are the boards' 1920 x 1080; the newspaper sheet's
/// content box ("Page") starts at 111, 79.
/// </summary>
public static class StockMarketBuilder
{
    public const string PrefabPath = "Assets/UI/Prefabs/StockMarket.prefab";
    const string Table = "Stocks";

    static readonly Color Sheet = UIColors.Hex(0xF1ECDD);
    static readonly Color DisabledFill = UIColors.Hex(0xE7E1D0);
    static readonly Color DisabledText = UIColors.Hex(0x9A9DA4);
    static readonly Vector4 TicketX = new Vector4(12, 16, 0, 0), TicketY = new Vector4(16, 12, 0, 0);
    static readonly Vector4 TabX = new Vector4(10, 14, 0, 0), TabY = new Vector4(12, 10, 0, 0);
    static Sprite White => S("Kit/Special/white");

    // ------------------------------------------------------------------
    // Menu / batch

    [MenuItem("Tools/Game/New UI/Build Stock Market")]
    public static void BuildFromMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Build Stock Market", "Stop Play mode first.", "OK");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var log = new List<string>();
        var prefab = BuildPrefab(log);
        if (prefab != null) Install(prefab, log);
        Debug.Log("[StockMarketBuilder]\n" + string.Join("\n", log));
        EditorUtility.DisplayDialog("Stock market", string.Join("\n", log), "OK");
    }

    [MenuItem("Tools/Game/New UI/Restore Fallback Stock Market")]
    public static void RestoreFromMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var log = new List<string>();
        Restore(log);
        Debug.Log("[StockMarketBuilder]\n" + string.Join("\n", log));
        EditorUtility.DisplayDialog("Stock market", string.Join("\n", log), "OK");
    }

    /// <summary>Batch: -executeMethod StockMarketBuilder.BatchRun [-install] [-preview &lt;dir&gt;]</summary>
    public static void BatchRun()
    {
        int code = 0;
        var log = new List<string>();
        try
        {
            UIPreview.EnsureProjectPipeline(log);
            // the "Stocks" String Table (Assets/Localization/Source/Stocks.csv): in batch mode the importer's own
            // after-reload pass may not have run yet
            try { LocalizationCsvImporter.ImportChanged("stock market build"); }
            catch (Exception e) { log.Add("Localization import failed: " + e.Message); }
            var args = Environment.GetCommandLineArgs();
            bool install = Array.IndexOf(args, "-install") >= 0;
            int p = Array.IndexOf(args, "-preview");
            string dir = p >= 0 && p + 1 < args.Length ? args[p + 1] : null;
            var prefab = BuildPrefab(log);
            if (prefab == null) code = 1;
            if (prefab != null && install) Install(prefab, log);
            if (prefab != null && dir != null)
                RenderPreviews(AssetDatabase.LoadAssetAtPath<GameObject>(OfficeHudBuilder.PrefabPath), prefab, dir, log);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            code = 1;
        }
        finally
        {
            try { UIPreview.EnsureProjectPipeline(log); } catch (Exception e) { Debug.LogException(e); }
        }
        Debug.Log("[StockMarketBuilder]\n" + string.Join("\n", log));
        EditorApplication.Exit(code);
    }

    public static GameObject BuildPrefab(List<string> log)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
        GameObject root = null;
        try
        {
            root = new GameObject("StockMarket", typeof(RectTransform));
            root.layer = 5;
            var rt = (RectTransform)root.transform;
            rt.sizeDelta = new Vector2(1920f, 1080f);
            var view = root.AddComponent<StockMarketView>();
            Build(rt, view);
            L10nFont.AttachAll(rt);

            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 85;   // above the HUD, below the skill board (90), pause menu (100) and pop-ups
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            root.AddComponent<GraphicRaycaster>();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool ok);
            log.Add(ok ? "Saved " + PrefabPath : "Could not save " + PrefabPath);
            return ok ? prefab : null;
        }
        finally
        {
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
        }
    }

    // ------------------------------------------------------------------
    // The screen

    static void Build(RectTransform rt, StockMarketView view)
    {
        var screen = Stretch(rt, "Screen");
        view.root = screen.gameObject;
        StretchBox(screen, "Scrim", null, UIColors.Scrim).raycastTarget = true;

        // the newspaper sheet: 1760 x 960 at 80, 60, 3 px border, padding 16 28 18
        Shadow(screen, "SheetShadow", 80f, 60f, 1760f, 960f, 0f, 22f, 48f, 0.45f);
        var sheet = Node(screen, "Sheet", 80f, 60f, 1760f, 960f);
        StretchBox(sheet, "Paper", S("Kit/panel_fill"), Sheet).raycastTarget = true;
        StretchBox(sheet, "Border", S("Kit/panel_line3"), UIColors.Ink);
        var page = Node(sheet, "Page", 31f, 19f, 1698f, 920f);

        Masthead(page, view);
        EditionLine(page, view);
        var list = Node(page, "List", 0f, 136f, 712f, 712f);
        StockList(list, view);
        Box(page, "Divider1", 728f, 136f, 2f, 712f, null, UIColors.Ink);
        var detail = Node(page, "Detail", 746f, 136f, 548f, 712f);
        view.detailRoot = detail.gameObject;
        Detail(detail, view);
        Box(page, "Divider2", 1310f, 136f, 2f, 712f, null, UIColors.Ink);
        var ticketColumn = Node(page, "TicketColumn", 1328f, 136f, 370f, 712f);
        view.ticketRoot = ticketColumn.gameObject;
        Ticket(ticketColumn, view);
        Footer(page, view);

        screen.gameObject.SetActive(false);
    }

    // ------------------------------------------------------------------
    // Masthead: double rule on top, motto box | THE DAILY GAMBLE | close, 3 px rule under (90 tall)

    static void Masthead(RectTransform page, StockMarketView view)
    {
        Box(page, "RuleTop1", 0f, 0f, 1698f, 2f, null, UIColors.Ink);
        Box(page, "RuleTop2", 0f, 4f, 1698f, 2f, null, UIColors.Ink);
        Box(page, "RuleBottom", 0f, 87f, 1698f, 3f, null, UIColors.Ink);

        var motto = Node(page, "Motto", 0f, 16.5f, 236f, 60f);
        Ring(motto, "Border", 0f, 0f, 236f, 60f, UIColors.Ink, 2f, 0f);
        var mottoText = Text(motto, "Text", 12f, 8f, 212f, 44f, "All the news that's fit to bet on.", 17f, UIColors.Ink,
            align: TextAlignmentOptions.Center, lineHeight: 22f / 17f, wrap: true);
        Localize(mottoText, "stocks.masthead.motto");

        var title = Text(page, "Title", 280f, 10.5f, 1138f, 72f, "THE DAILY GAMBLE", 72f, UIColors.Ink, bold: true,
            align: TextAlignmentOptions.Center, letterSpacingEm: 0.04f);
        title.enableAutoSizing = true;
        title.fontSizeMin = 40f;
        title.fontSizeMax = 72f;
        Localize(title, "stocks.masthead.title");

        view.closeButton = IconButton(page, "Close", 1638f, 16.5f, 60, "cross");
    }

    // ------------------------------------------------------------------
    // Edition line: ● LIVE · Day 42 · Top mover GME +24.8% · Prices move every 5 seconds · 1% fee each way (34, 2 px rule)

    static void EditionLine(RectTransform page, StockMarketView view)
    {
        Box(page, "EditionRule", 0f, 122f, 1698f, 2f, null, UIColors.Ink);
        var row = Node(page, "Edition", 0f, 90f, 1698f, 32f);
        HLayout(row.gameObject, 14f, null, TextAnchor.MiddleCenter, true, true, false, false);

        var live = NewRect(row, "Live");
        HLayout(live.gameObject, 8f, null, TextAnchor.MiddleCenter, true, true, false, false);
        var dot = Sized(NewRect(live, "Dot"), 18f, 18f);
        StretchBox(dot, "Halo", S("Kit/Circles/circle18_fill"), UIColors.Over(UIColors.Loss, 0.22f, Sheet)).type = Image.Type.Simple;
        Box(dot, "Dot", 3f, 3f, 12f, 12f, S("Kit/Circles/circle12_fill"), UIColors.Loss).type = Image.Type.Simple;
        Localize(LayoutText(live, "Label", "LIVE", 19f, UIColors.Loss, true, letterSpacingEm: 0.08f), "stocks.edition.live");

        Dot(row, "Sep1");
        view.dayText = LayoutText(row, "Day", "Day 42", 19f, UIColors.Ink);
        var sep2 = Dot(row, "Sep2");
        view.topMoverText = LayoutText(row, "TopMover", "Top mover GME +24.8%", 19f, UIColors.Ink);
        view.topMoverParts = new[] { sep2.gameObject, view.topMoverText.gameObject };
        Dot(row, "Sep3");
        Localize(LayoutText(row, "Ticks", "Prices move every 5 seconds", 19f, UIColors.Ink), "stocks.edition.ticks");
        Dot(row, "Sep4");
        view.feeText = LayoutText(row, "Fee", "1% fee each way", 19f, UIColors.Ink);
    }

    static TextMeshProUGUI Dot(RectTransform row, string name) => LayoutText(row, name, "·", 19f, UIColors.Pencil);

    // ------------------------------------------------------------------
    // Stock list: tabs (40), then All (header 32 + rows of 32) or Owned (header, holdings, totals, split bar, trades)

    static void StockList(RectTransform list, StockMarketView view)
    {
        var tabs = Node(list, "Tabs", 0f, 0f, 712f, 40f);
        // each tab takes its content's width in the current language (the widths below are the English build sizes);
        // heights stay StockTabLook's, standing on the rule
        HLayout(tabs.gameObject, 8f, Pad(4, 0, 0, 0), TextAnchor.LowerLeft, true, false, false, false);
        float allW = 2f + 16f + TextWidth("All stocks", 20f, true) + 10f + 36f + 16f + 2f;
        view.allTab = ListTab(tabs, "AllTab", 4f, allW, "All stocks", "stocks.list.tab.all", null, out view.allCountText, out _);
        float ownedW = 2f + 16f + 22f + 10f + TextWidth("Owned", 20f, true) + 10f + 36f + 16f + 2f;
        view.ownedTab = ListTab(tabs, "OwnedTab", 4f + allW + 8f, ownedW, "Owned", "stocks.list.tab.owned", "briefcase", out view.ownedCountText,
            out view.ownedCountFill);

        // All stocks
        var all = Node(list, "AllView", 0f, 40f, 712f, 672f);
        view.allView = all.gameObject;
        HeaderRow(all, "Header", 0f, new[]
        {
            ("SYMBOL", "stocks.list.col.symbol", 10f, 84f, false), ("COMPANY", "stocks.list.col.company", 94f, 272f, false),
            ("PRICE", "stocks.list.col.price", 366f, 128f, true), ("CHANGE", "stocks.list.col.change", 494f, 120f, true),
            ("OWNED", "stocks.list.col.owned", 614f, 88f, true)
        });
        var allContent = ScrollArea(all, "Rows", 0f, 32f, 712f, 640f);
        view.allRows = allContent;
        view.rowTemplate = StockRow(allContent);

        // Owned
        var owned = Node(list, "OwnedView", 0f, 40f, 712f, 672f);
        view.ownedView = owned.gameObject;
        HeaderRow(owned, "Header", 0f, new[]
        {
            ("SYMBOL", "stocks.list.col.symbol", 10f, 84f, false), ("COMPANY", "stocks.list.col.company", 94f, 212f, false),
            ("SHARES", "stocks.list.col.shares", 306f, 70f, true), ("VALUE", "stocks.list.col.value", 376f, 130f, true),
            ("PROFIT", "stocks.list.col.profit", 506f, 116f, true), ("%", "stocks.list.col.percent", 622f, 80f, true)
        });
        var content = ScrollArea(owned, "Content", 0f, 32f, 712f, 640f);

        var rows = NewRect(content, "OwnedRows");
        VLayout(rows.gameObject, 0f, null, TextAnchor.UpperLeft, true, true, true, false);
        view.ownedRows = rows;
        view.ownedRowTemplate = OwnedRow(rows);

        var empty = LayoutText(content, "Empty", "You don't own any shares yet. Pick a stock under All stocks to buy some.", 19f, UIColors.InkSoft,
            lineHeight: 26f / 19f);
        empty.textWrappingMode = TextWrappingModes.Normal;
        empty.margin = new Vector4(10f, 12f, 10f, 0f);
        Localize(empty, "stocks.owned.empty");
        view.ownedEmptyText = empty;

        // totals: 42 tall, 3 px double rule on top
        var totals = Sized(NewRect(content, "Totals"), 712f, 42f);
        view.totalsRow = totals.gameObject;
        Box(totals, "Rule1", 0f, 0f, 712f, 1f, null, UIColors.Ink);
        Box(totals, "Rule2", 0f, 2f, 712f, 1f, null, UIColors.Ink);
        Localize(Text(totals, "Label", 10f, 3f, 296f, 39f, "Total", 20f, UIColors.Ink, bold: true, align: TextAlignmentOptions.MidlineLeft),
            "stocks.owned.total");
        view.totalValueText = Text(totals, "Value", 376f, 3f, 130f, 39f, "$58,716.60", 20f, UIColors.Ink, bold: true, align: TextAlignmentOptions.MidlineRight);
        view.totalProfitText = Text(totals, "Profit", 506f, 3f, 116f, 39f, "+$2,196.68", 20f, UIColors.Money, bold: true, align: TextAlignmentOptions.MidlineRight);
        view.totalPercentText = Text(totals, "Percent", 622f, 3f, 80f, 39f, "+3.9%", 20f, UIColors.Money, bold: true, align: TextAlignmentOptions.MidlineRight);

        // "Where your money is": 30 margin, label 22, bar 38, best/worst 26 (10 apart)
        var split = Sized(NewRect(content, "Split"), 712f, 136f);
        view.splitBlock = split.gameObject;
        Localize(Text(split, "Label", 10f, 30f, 692f, 22f, "WHERE YOUR MONEY IS", 17f, UIColors.InkSoft, letterSpacingEm: 0.08f), "stocks.owned.split");
        var bar = Node(split, "Bar", 10f, 62f, 692f, 38f);
        var barMask = StretchBox(bar, "Mask", S("Kit/r8_fill"), Color.white);
        barMask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
        var segments = Stretch(barMask.rectTransform, "Segments", 2f, 2f, 2f, 2f);
        view.splitBar = segments;
        view.segmentTemplate = Segment(segments);
        StretchBox(bar, "Border", S("Kit/r8_line2"), UIColors.Ink);
        var bestWorst = Node(split, "BestWorst", 10f, 110f, 692f, 26f);
        HLayout(bestWorst.gameObject, 24f, null, TextAnchor.MiddleLeft, true, true, false, false);
        view.bestText = LayoutText(bestWorst, "Best", "Best GME +16.1%", 19f, UIColors.InkSoft);
        view.worstText = LayoutText(bestWorst, "Worst", "Worst KO −1.0%", 19f, UIColors.InkSoft);

        // Recent trades: 30 margin, header 32, rows of 32
        var tradesHead = Sized(NewRect(content, "TradesHeader"), 712f, 62f);
        HeaderRow(tradesHead, "Header", 30f, new[]
        {
            ("DAY", "stocks.trades.col.day", 10f, 80f, false), ("RECENT TRADES", "stocks.trades.col.trade", 90f, 330f, false),
            ("CASH", "stocks.trades.col.cash", 420f, 150f, true), ("PROFIT", "stocks.trades.col.profit", 570f, 132f, true)
        });
        var trades = NewRect(content, "Trades");
        VLayout(trades.gameObject, 0f, null, TextAnchor.UpperLeft, true, true, true, false);
        view.tradeRows = trades;
        view.tradeRowTemplate = TradeRow(trades);
        var noTrades = LayoutText(content, "NoTrades", "No trades yet.", 19f, UIColors.InkSoft, lineHeight: 26f / 19f);
        noTrades.margin = new Vector4(10f, 6f, 10f, 0f);
        Localize(noTrades, "stocks.trades.empty");
        view.tradesEmptyText = noTrades;

        owned.gameObject.SetActive(false);
    }

    /// <summary>A list tab standing on the table's rule: 40 tall when active (paper, solid border), 34 when not (clear, dashed).</summary>
    static StockTabLook ListTab(RectTransform tabs, string name, float left, float width, string label, string key, string icon,
        out TMP_Text countText, out Graphic countFill)
    {
        var tab = Node(tabs, name, left, 0f, width, 40f);
        Pivot(tab, new Vector2(0f, 0f));   // grows up from the rule
        HLayout(tab.gameObject, 0f, null, TextAnchor.MiddleLeft, true, true, false, true);   // as wide as its content
        var hit = AddImage(tab.gameObject, null, Clear, true);
        var button = tab.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = hit;

        var fill = StretchShape(tab, "Fill", UIColors.PaperHi, 0f, TabX, TabY);
        var solid = StretchShape(tab, "Border", UIColors.Ink, 2f, TabX, TabY);
        solid.borderBottom = 0f;
        var dashed = StretchShape(tab, "Dashed", UIColors.Over(UIColors.Ink, 0.35f, Sheet), 2f, TabX, TabY);
        dashed.borderBottom = 0f;
        dashed.dash = 6f;
        dashed.gap = 5f;
        IgnoreLayout(fill);
        IgnoreLayout(solid);
        IgnoreLayout(dashed);

        var row = Stretch(tab, "Content");
        HLayout(row.gameObject, 10f, Pad(18, 18, 2, 0), TextAnchor.MiddleLeft, true, true, false, false);
        var tinted = new List<Graphic>();
        if (icon != null) tinted.Add(LayoutIcon(row, "Icon", 22f, icon, UIColors.Ink));
        var text = LayoutText(row, "Label", label, 20f, UIColors.Ink, true);
        Localize(text, key);
        var pill = NewRect(row, "Count");
        Layout(pill, 36f, 24f, 36f, 24f);
        var pillFill = StretchBox(pill, "Fill", S("Kit/Pills/pill24_fill"), UIColors.PaperShade);
        StretchBox(pill, "Border", S("Kit/Pills/pill24_line2"), UIColors.Ink);
        countText = Text(pill, "Text", 0f, 0f, 36f, 24f, "20", 16f, UIColors.Ink, bold: true, align: TextAlignmentOptions.Center);
        countFill = pillFill;

        var look = tab.gameObject.AddComponent<StockTabLook>();
        look.button = button;
        look.fill = fill;
        look.solidBorder = solid.gameObject;
        look.dashedBorder = dashed.gameObject;
        look.label = text;
        look.tinted = tinted.ToArray();
        look.activeFill = UIColors.PaperHi;
        look.activeText = UIColors.Ink;
        look.inactiveFill = UIColors.WithAlpha(Sheet, 0f);
        look.inactiveText = UIColors.InkSoft;
        look.dashedWhenInactive = true;
        look.disabledFill = UIColors.WithAlpha(Sheet, 0f);
        look.disabledText = UIColors.InkSoft;
        look.activeHeight = 40f;
        look.inactiveHeight = 34f;
        return look;
    }

    /// <summary>Table header: spaced 17 px capitals on the row's bottom (4 px up), 3 px ink rule under.</summary>
    static void HeaderRow(RectTransform parent, string name, float top, (string text, string key, float left, float width, bool right)[] cols)
    {
        var head = Node(parent, name, 0f, top, 712f, 32f);
        foreach (var c in cols)
        {
            var t = Text(head, c.key.Substring(c.key.LastIndexOf('.') + 1), c.left, 1f, c.width, 22f, c.text, 17f, UIColors.InkSoft,
                align: c.right ? TextAlignmentOptions.BottomRight : TextAlignmentOptions.BottomLeft, letterSpacingEm: 0.08f);
            Localize(t, c.key);
        }
        Box(head, "Rule", 0f, 29f, 712f, 3f, null, UIColors.Ink);
    }

    /// <summary>A vertical scroll area (no bar) whose content is a column of rows.</summary>
    static RectTransform ScrollArea(RectTransform parent, string name, float left, float top, float width, float height)
    {
        var viewport = Node(parent, name, left, top, width, height);
        viewport.gameObject.AddComponent<RectMask2D>();
        AddImage(viewport.gameObject, null, Clear, true);   // catches the wheel between rows
        var content = NewRect(viewport, "Content");
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = new Vector2(0f, height);
        VLayout(content.gameObject, 0f, null, TextAnchor.UpperLeft, true, true, true, false);
        Fit(content.gameObject, false, true);
        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 32f;
        scroll.inertia = false;
        return content;
    }

    /// <summary>A row: 712 x 32, highlighter band when selected, dashed rule under, the whole row a button.</summary>
    static RectTransform RowBase(RectTransform parent, string name, out Button button, out Graphic band)
    {
        var row = NewRect(parent, name);
        row.sizeDelta = new Vector2(712f, 32f);
        Layout(row, 712f, 32f, 712f, 32f);
        var hit = AddImage(row.gameObject, null, Clear, true);
        button = row.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = hit;
        band = Box(row, "Band", 0f, 2.5f, 712f, 27f, null, UIColors.Over(UIColors.Highlight, 0.95f, Sheet));
        var dash = Box(row, "Rule", 0f, 31f, 712f, 1f, S("Kit/Tiles/dash_h2"), UIColors.Over(UIColors.Ink, 0.3f, Sheet));
        dash.type = Image.Type.Tiled;
        return row;
    }

    static StockRowView StockRow(RectTransform parent)
    {
        var row = RowBase(parent, "RowTemplate", out var button, out var band);
        var v = row.gameObject.AddComponent<StockRowView>();
        v.button = button;
        v.band = band;
        v.symbolText = Left(Text(row, "Symbol", 10f, 0f, 84f, 32f, "TSLA", 20f, UIColors.Ink, bold: true, align: TextAlignmentOptions.MidlineLeft));
        v.companyText = Left(Text(row, "Company", 94f, 0f, 200f, 32f, "Teslo Motors", 19f, UIColors.Ink, align: TextAlignmentOptions.MidlineLeft));
        Chip(row, 206f, 4f, 24f, 17f, out v.chip, out v.chipFill, out v.chipText);
        v.priceText = Text(row, "Price", 366f, 0f, 128f, 32f, "$912.40", 20f, UIColors.Ink, align: TextAlignmentOptions.MidlineRight);
        v.changeText = Left(Text(row, "Change", 494f, 0f, 120f, 32f, "+3.2%", 19f, UIColors.Money, bold: true, align: TextAlignmentOptions.MidlineRight));
        v.upIcon = (Image)Left(Icon(row, "Up", 500f, 6f, 20f, "trend-up", UIColors.Money));
        v.downIcon = (Image)Left(Icon(row, "Down", 500f, 6f, 20f, "trend-down", UIColors.Loss));
        v.ownedText = Text(row, "Owned", 614f, 0f, 88f, 32f, "24", 19f, UIColors.Ink, bold: true, align: TextAlignmentOptions.MidlineRight);
        row.gameObject.SetActive(false);
        return v;
    }

    static StockOwnedRowView OwnedRow(RectTransform parent)
    {
        var row = RowBase(parent, "OwnedRowTemplate", out var button, out var band);
        var v = row.gameObject.AddComponent<StockOwnedRowView>();
        v.button = button;
        v.band = band;
        v.symbolText = Left(Text(row, "Symbol", 10f, 0f, 84f, 32f, "GME", 20f, UIColors.Ink, bold: true, align: TextAlignmentOptions.MidlineLeft));
        v.companyText = Left(Text(row, "Company", 94f, 0f, 150f, 32f, "GameStomp", 19f, UIColors.Ink, align: TextAlignmentOptions.MidlineLeft));
        Chip(row, 200f, 4f, 24f, 17f, out v.chip, out v.chipFill, out v.chipText);
        v.sharesText = Text(row, "Shares", 306f, 0f, 70f, 32f, "40", 19f, UIColors.Ink, align: TextAlignmentOptions.MidlineRight);
        v.valueText = Text(row, "Value", 376f, 0f, 130f, 32f, "$8,464.00", 20f, UIColors.Ink, align: TextAlignmentOptions.MidlineRight);
        v.profitText = Text(row, "Profit", 506f, 0f, 116f, 32f, "+$1,172.00", 19f, UIColors.Money, bold: true, align: TextAlignmentOptions.MidlineRight);
        v.percentText = Text(row, "Percent", 622f, 0f, 80f, 32f, "+16.1%", 19f, UIColors.Money, bold: true, align: TextAlignmentOptions.MidlineRight);
        row.gameObject.SetActive(false);
        return v;
    }

    static StockTradeRowView TradeRow(RectTransform parent)
    {
        var row = NewRect(parent, "TradeRowTemplate");
        row.sizeDelta = new Vector2(712f, 32f);
        Layout(row, 712f, 32f, 712f, 32f);
        var dash = Box(row, "Rule", 0f, 31f, 712f, 1f, S("Kit/Tiles/dash_h2"), UIColors.Over(UIColors.Ink, 0.3f, Sheet));
        dash.type = Image.Type.Tiled;
        var v = row.gameObject.AddComponent<StockTradeRowView>();
        v.dayText = Text(row, "Day", 10f, 0f, 80f, 32f, "Day 42", 19f, UIColors.InkSoft, align: TextAlignmentOptions.MidlineLeft);
        v.whatText = Text(row, "What", 90f, 0f, 330f, 32f, "Bought 12 TSLA at $912.40", 19f, UIColors.Ink, align: TextAlignmentOptions.MidlineLeft);
        v.whatText.overflowMode = TextOverflowModes.Ellipsis;
        v.cashText = Text(row, "Cash", 420f, 0f, 150f, 32f, "−$11,058.29", 19f, UIColors.Ink, align: TextAlignmentOptions.MidlineRight);
        v.profitText = Text(row, "Profit", 570f, 0f, 132f, 32f, "—", 19f, UIColors.Pencil, align: TextAlignmentOptions.MidlineRight);
        row.gameObject.SetActive(false);
        return v;
    }

    static StockBarSegmentView Segment(RectTransform bar)
    {
        var seg = NewRect(bar, "SegmentTemplate");
        seg.anchorMin = new Vector2(0f, 0f);
        seg.anchorMax = new Vector2(0f, 1f);
        seg.pivot = new Vector2(0f, 0.5f);
        seg.sizeDelta = new Vector2(100f, 0f);
        seg.anchoredPosition = Vector2.zero;
        var v = seg.gameObject.AddComponent<StockBarSegmentView>();
        v.fill = AddImage(seg.gameObject, White, UIColors.StickyBlue);
        var edge = NewRect(seg, "Edge");
        edge.anchorMin = new Vector2(1f, 0f);
        edge.anchorMax = new Vector2(1f, 1f);
        edge.pivot = new Vector2(1f, 0.5f);
        edge.sizeDelta = new Vector2(2f, 0f);
        edge.anchoredPosition = Vector2.zero;
        v.edge = AddImage(edge.gameObject, null, UIColors.Ink);
        var label = Stretch(seg, "Label");
        v.label = SetupText(label.gameObject, "AAPL 39%", 17f, UIColors.Ink, true, TextAlignmentOptions.Center);
        seg.gameObject.SetActive(false);
        return v;
    }

    /// <summary>Type chip: small radius, pastel fill, 2 px ink border, 17-19 px text, 8-12 px padding (sized by the view).</summary>
    static void Chip(RectTransform parent, float left, float top, float height, float size, out RectTransform chip, out Image fill, out TMP_Text text)
    {
        chip = Node(parent, "Chip", left, top, 80f, height);
        Pivot(chip, new Vector2(0f, 1f));
        fill = StretchBox(chip, "Fill", S("Kit/small_fill"), UIColors.StickyPink);
        StretchBox(chip, "Border", S("Kit/small_line2"), UIColors.Ink);
        var label = Stretch(chip, "Label");
        text = SetupText(label.gameObject, "Volatile", size, UIColors.Ink, false, TextAlignmentOptions.Center);
    }

    // ------------------------------------------------------------------
    // Selected stock (548 wide)

    static void Detail(RectTransform d, StockMarketView view)
    {
        view.symbolText = Left(Text(d, "Symbol", 0f, 0f, 320f, 69f, "TSLA", 60f, UIColors.Ink, bold: true, align: TextAlignmentOptions.MidlineLeft));
        Chip(d, 160f, 17.5f, 34f, 19f, out view.typeChip, out view.typeChipFill, out view.typeChipText);
        view.companyText = Text(d, "Company", 0f, 71f, 548f, 36f, "Teslo Motors", 26f, UIColors.InkSoft);

        view.priceText = Left(Text(d, "Price", 0f, 119f, 360f, 62f, "$912.40", 54f, UIColors.Ink, bold: true, align: TextAlignmentOptions.MidlineLeft));
        var change = Node(d, "Change", 240f, 139.6f, 340f, 36f);
        Pivot(change, new Vector2(0f, 1f));
        view.changeGroup = change;
        view.changeUp = Icon(change, "Up", 0f, 4f, 28f, "trend-up", UIColors.Money);
        view.changeDown = Icon(change, "Down", 0f, 4f, 28f, "trend-down", UIColors.Loss);
        view.changeText = Text(change, "Text", 34f, 0f, 300f, 36f, "+3.2% in 2½ min", 26f, UIColors.Money, bold: true, align: TextAlignmentOptions.MidlineLeft);

        // chart: 548 x 284 plot, labels under it (316 in all)
        var chartBox = Node(d, "Chart", 0f, 193f, 548f, 316f);
        var plot = Node(chartBox, "Plot", 0f, 0f, 548f, 284f);
        var chart = plot.gameObject.AddComponent<StockChartGraphic>();
        chart.raycastTarget = false;
        chart.color = UIColors.Ink;
        chart.gridAlpha = UIColors.BlendAlpha(0.5f, UIColors.Ink, Sheet);
        chart.hatchAlpha = UIColors.BlendAlpha(0.22f, UIColors.Ink, Sheet);
        chart.startAlpha = UIColors.BlendAlpha(0.75f, UIColors.Ink, Sheet);
        chart.upColor = UIColors.Money;
        chart.downColor = UIColors.Loss;
        view.chart = chart;
        view.axisLabels = new TMP_Text[4];
        for (int i = 0; i < 4; i++)
            view.axisLabels[i] = Text(chartBox, "Axis" + i, 0f, 49f + 65f * i, 52f, 24f, "$920", 17f, UIColors.InkSoft, align: TextAlignmentOptions.MidlineRight);
        var start = Node(chartBox, "Start", 410f, 150f, 124f, 24f);
        Pivot(start, new Vector2(0f, 1f));
        AddImage(start.gameObject, null, Sheet);
        view.startLabel = start;
        var startText = Stretch(start, "Text", 6f, 0f, 6f, 0f);
        view.startLabelText = SetupText(startText.gameObject, "start $884.10", 17f, UIColors.InkSoft, false, TextAlignmentOptions.MidlineRight);
        Localize(Text(chartBox, "Ago", 64f, 290f, 140f, 24f, "30 ticks ago", 17f, UIColors.InkSoft), "stocks.chart.ago");
        Localize(Text(chartBox, "Tick", 218f, 290f, 180f, 24f, "5 s per tick", 17f, UIColors.InkSoft, align: TextAlignmentOptions.Center), "stocks.chart.tick");
        Localize(Text(chartBox, "Now", 468f, 290f, 80f, 24f, "now", 17f, UIColors.InkSoft, align: TextAlignmentOptions.Right), "stocks.chart.now");

        view.rangeText = Text(d, "Range", 0f, 521f, 548f, 26f, "30-tick low $867.99 · high $932.76", 19f, UIColors.InkSoft);
        Icon(d, "HintIcon", 0f, 560f, 24f, "info", UIColors.Ink);
        view.hintText = Text(d, "Hint", 34f, 559f, 514f, 60f, "<b>Volatile:</b> now and then it jumps 5–12%, up or down.", 19f, UIColors.Ink,
            align: TextAlignmentOptions.TopLeft, lineHeight: 26f / 19f, wrap: true);

        // what you own: a paper card on the column's bottom (82 tall with 2 lines, 54 with one)
        var own = Node(d, "Owned", 0f, 630f, 548f, 82f);
        StretchBox(own, "Fill", S("Kit/small_fill"), UIColors.PaperHi);
        StretchBox(own, "Border", S("Kit/small_line2"), UIColors.Ink);
        Icon(own, "Icon", 16f, 13f, 28f, "briefcase", UIColors.Ink);
        view.ownLine = Text(own, "Line", 56f, 12f, 480f, 30f, "You own <b>24 shares</b> · worth <b>$21,897.60</b>", 22f, UIColors.Ink);
        view.paidLine = Text(own, "Paid", 56f, 44f, 480f, 26f, "Paid $891.83 a share on average · +$493.68 (+2.3%)", 19f, UIColors.InkSoft);
        view.ownBox = own.gameObject;
        var none = Node(d, "NotOwned", 0f, 658f, 548f, 54f);
        StretchBox(none, "Fill", S("Kit/small_fill"), UIColors.PaperHi);
        StretchBox(none, "Border", S("Kit/small_line2"), UIColors.Ink);
        Icon(none, "Icon", 16f, 13f, 28f, "briefcase", UIColors.InkSoft);
        view.noneLine = Text(none, "Line", 56f, 12f, 480f, 30f, "You don't own any NVDA.", 22f, UIColors.InkSoft);
        view.noneBox = none.gameObject;
    }

    // ------------------------------------------------------------------
    // Order ticket (370 wide, 22 below the column's top): paper with a torn zigzag bottom, a hard shadow, tape

    static void Ticket(RectTransform column, StockMarketView view)
    {
        var ticket = Node(column, "Ticket", 0f, 22f, 370f, 600f);
        Pivot(ticket, new Vector2(0f, 1f));
        VLayout(ticket.gameObject, 0f, null, TextAnchor.UpperLeft, true, true, true, false);
        Fit(ticket.gameObject, false, true);

        var body = NewRect(ticket, "Body");
        VLayout(body.gameObject, 14f, Pad(24, 24, 26, 16), TextAnchor.UpperLeft, true, true, true, false);
        var shadow = StretchShape(body, "Shadow", UIColors.Over(UIColors.Ink, 0.9f, Sheet), 0f, TicketX, TicketY);
        Offset(shadow.rectTransform, 3f, 4f);
        IgnoreLayout(shadow);
        IgnoreLayout(StretchShape(body, "Fill", UIColors.PaperHi, 0f, TicketX, TicketY));
        var border = StretchShape(body, "Border", UIColors.Ink, 2f, TicketX, TicketY);
        border.borderBottom = 0f;
        IgnoreLayout(border);

        // title and the stock
        var head = NewRect(body, "Head");
        VLayout(head.gameObject, 2f, null, TextAnchor.UpperLeft, true, true, true, false);
        Localize(LayoutText(head, "Title", "Order ticket", 32f, UIColors.Ink, true, lineHeight: 38f / 32f), "stocks.ticket.title");
        view.ticketSubText = LayoutText(head, "Sub", "TSLA · $912.40 a share", 19f, UIColors.InkSoft, lineHeight: 26f / 19f);

        // Buy | Sell
        var tabs = NewRect(body, "Tabs");
        Layout(tabs, 322f, 50f, 322f, 50f);
        HLayout(tabs.gameObject, 10f, null, TextAnchor.MiddleLeft, true, true, true, true);
        view.buyTab = TicketTab(tabs, "Buy", "Buy", "stocks.ticket.buy", UIColors.Money, true);
        view.sellTab = TicketTab(tabs, "Sell", "Sell", "stocks.ticket.sell", UIColors.Loss, false);

        // the order: shares, breakdown, the big button
        var open = NewRect(body, "Order");
        VLayout(open.gameObject, 14f, null, TextAnchor.UpperLeft, true, true, true, false);
        view.openGroup = open.gameObject;

        var shares = Sized(NewRect(open, "Shares"), 322f, 94f);
        Localize(Text(shares, "Label", 0f, 4f, 180f, 26f, "SHARES", 19f, UIColors.Pencil, letterSpacingEm: 0.08f), "stocks.ticket.shares");
        var quick = SmallButton(shares, "Quick", 322f - 108f, 0f, 108f, 34f, "Max 3,563", 19f, out view.quickText);
        Pivot((RectTransform)quick.transform, new Vector2(1f, 1f));
        view.quickButton = quick;
        Highlighter(view.quickText);
        view.lessButton = SquareButton(shares, "Less", 0f, 42f, "minus", "One share fewer");
        view.moreButton = SquareButton(shares, "More", 270f, 42f, "plus", "One share more");
        view.amountInput = AmountField(shares, 60f, 42f, 202f, 52f);

        var lines = NewRect(open, "Breakdown");
        VLayout(lines.gameObject, 6f, null, TextAnchor.UpperLeft, true, true, true, false);
        view.lines = new StockMarketView.TicketLine[5];
        for (int i = 0; i < 5; i++)
        {
            if (i == 2)
            {
                var rule = NewRect(lines, "Rule");
                Layout(rule, 322f, 2f, 322f, 2f);
                AddImage(rule.gameObject, null, UIColors.Ink);
                view.lineRule = rule.gameObject;
            }
            view.lines[i] = TicketLine(lines, "Line" + i);
        }

        var action = PaperButtonNode(open, "Action", 322f, 68f, "card", 3, UIColors.Money, new Vector2(5f, 6f), out var face, out var content,
            out var fill, out var solid, out var dashed, out var btnShadow);
        HLayout(content.gameObject, 10f, Pad(16, 16, 0, 0), TextAnchor.MiddleCenter, true, true, false, false);
        view.actionPlus = LayoutIcon(content, "Plus", 28f, "plus", UIColors.PaperHi);
        view.actionMinus = LayoutIcon(content, "Minus", 28f, "minus", UIColors.PaperHi);
        view.actionText = LayoutText(content, "Label", "Buy 12 TSLA", 28f, UIColors.PaperHi, true);
        var marker = SetupMarker(action, face, new Graphic[] { view.actionPlus, view.actionMinus, view.actionText });
        dashed.color = UIColors.Over(UIColors.Ink, 0.4f, DisabledFill);
        marker.disabledFillColor = DisabledFill;
        view.actionButton = action.GetComponent<Button>();
        view.actionMarker = marker;
        view.actionFill = fill;
        view.actionSolidBorder = solid.gameObject;
        view.actionDashedBorder = dashed.gameObject;
        view.actionShadow = btnShadow.gameObject;

        // locked: the padlock, why, the skill board
        var locked = NewRect(body, "Locked");
        VLayout(locked.gameObject, 12f, Pad(2, 2, 8, 6), TextAnchor.UpperCenter, true, true, true, false);
        view.lockedGroup = locked.gameObject;
        Padlock(locked);
        Localize(CenteredText(locked, "Title", "Buying shares needs the Stock Market Access skill.", 24f, UIColors.Ink, true, 32f), "stocks.locked.title");
        Localize(CenteredText(locked, "Where", "It's on the skill board: Growth, tier 3.", 19f, UIColors.InkSoft, false, 26f), "stocks.locked.where");
        var skills = Sized(NewRect(locked, "OpenSkillsRow"), 318f, 56f);
        float openW = TextWidth("Open skill board", 22f, true) + 6f;
        float bw = 22f + 26f + 10f + openW + 22f;
        var open2 = PaperButtonNode(skills, "OpenSkills", bw, 56f, "card", 3, UIColors.Highlight, new Vector2(4f, 5f), out var f2, out var c2,
            out _, out _, out _, out _);
        Place(open2, (318f - bw) * 0.5f, 0f, bw, 56f);
        HLayout(c2.gameObject, 10f, Pad(22, 22, 0, 0), TextAnchor.MiddleCenter, true, true, false, false);
        var star = LayoutIcon(c2, "Icon", 26f, "star", UIColors.Ink);
        var openLabel = LayoutText(c2, "Label", "Open skill board", 22f, UIColors.Ink, true);
        Localize(openLabel, "stocks.locked.button");
        KeepWidth(openLabel, openW, 15f);
        SetupMarker(open2, f2, new Graphic[] { star, openLabel });
        view.openSkillsButton = open2.GetComponent<Button>();
        Localize(CenteredText(locked, "Note", "Shares you already own can still be sold.", 19f, UIColors.InkSoft, false, 26f), "stocks.locked.note");
        locked.gameObject.SetActive(false);

        // message: dashed rule, icon + text
        var message = NewRect(body, "Message");
        VLayout(message.gameObject, 14f, null, TextAnchor.UpperLeft, true, true, true, false);
        view.messageGroup = message.gameObject;
        var dash = NewRect(message, "Rule");
        Layout(dash, 322f, 2f, 322f, 2f);
        AddImage(dash.gameObject, S("Kit/Tiles/dash_h2"), UIColors.Over(UIColors.Ink, 0.45f, UIColors.PaperHi)).type = Image.Type.Tiled;
        var line = NewRect(message, "Line");
        HLayout(line.gameObject, 10f, null, TextAnchor.UpperLeft, true, true, false, false);
        var slot = NewRect(line, "Icon");
        Layout(slot, 26f, 26f, 26f, 26f);
        view.messageOk = Box(slot, "Ok", 0f, 0f, 26f, 26f, IconSprite("check", "b"), UIColors.Money);
        view.messageError = Box(slot, "Error", 0f, 0f, 26f, 26f, IconSprite("warning"), UIColors.Loss);
        view.messageInfo = Box(slot, "Info", 0f, 0f, 26f, 26f, IconSprite("info"), UIColors.InkSoft);
        foreach (var img in new[] { view.messageOk, view.messageError, view.messageInfo })
        {
            img.type = Image.Type.Simple;
            img.preserveAspect = true;
        }
        view.messageText = LayoutText(line, "Text", "Bought 12 TSLA for $11,058.29.", 19f, UIColors.Money, true, lineHeight: 26f / 19f);
        view.messageText.textWrappingMode = TextWrappingModes.Normal;
        Layout(view.messageText, 286f, -1f, 286f, -1f, 1f);

        // torn bottom edge (zigzag 370 x 20)
        Zigzag(ticket);

        // masking tape on the top edge and the LOCKED stamp (outside the layout)
        var tape = Box(ticket, "Tape", 110f, -20f, 150f, 38f, White, UIColors.Tape, rotation: 3f);
        IgnoreLayout(tape);
        var tapeShadow = Shadow(ticket, "TapeShadow", 110f, -20f, 150f, 38f, 0f, 2f, 4f, 0.18f, rotation: 3f);
        IgnoreLayout(tapeShadow);
        tapeShadow.transform.SetSiblingIndex(tape.transform.GetSiblingIndex());

        var stampWord = "LOCKED";
        float sw = 4f + 14f + TextWidth(stampWord, 30f, true, 0.08f) + 14f + 4f;
        var stamp = Node(ticket, "Stamp", 370f - 2f - sw, 206f, sw, 48f, rotation: 12f);
        IgnoreLayout(stamp);
        var stampColor = UIColors.WithAlpha(UIColors.Loss, 0.88f);
        Ring(stamp, "Outer", 0f, 0f, sw, 48f, stampColor, 1.4f, 9f);
        Ring(stamp, "Inner", 2.7f, 2.7f, sw - 5.4f, 48f - 5.4f, stampColor, 1.4f, 7f);
        var stampText = Text(stamp, "Word", 4f, 4f, sw - 8f, 40f, stampWord, 30f, stampColor, bold: true, align: TextAlignmentOptions.Center, letterSpacingEm: 0.08f);
        Localize(stampText, "stocks.locked.stamp");
        view.lockedStamp = stamp.gameObject;
        stamp.gameObject.SetActive(false);
    }

    static StockTabLook TicketTab(RectTransform row, string name, string label, string key, Color fill, bool withLock)
    {
        var tab = NewRect(row, name);
        Layout(tab, 100f, 50f, 156f, 50f, 1f);
        var hit = AddImage(tab.gameObject, null, Clear, true);
        var button = tab.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = hit;
        var shadow = StretchBox(tab, "Shadow", S("Kit/card_fill"), UIColors.Ink, 3f, 4f, -3f, -4f);
        var face = Stretch(tab, "Face");
        var f = StretchBox(face, "Fill", S("Kit/card_fill"), fill);
        var solid = StretchBox(face, "Border", S("Kit/card_line3"), UIColors.Ink);
        var dashed = Dashed(StretchBox(face, "Dashed", S("Kit/card_dash3"), UIColors.Over(UIColors.Ink, 0.35f, Sheet)));
        var content = Stretch(face, "Content");
        HLayout(content.gameObject, 8f, Pad(12, 12, 0, 0), TextAnchor.MiddleCenter, true, true, false, false);
        GameObject lockIcon = null;
        if (withLock)
        {
            var l = LayoutIcon(content, "Lock", 22f, "lock", DisabledText);
            lockIcon = l.gameObject;
        }
        var text = LayoutText(content, "Label", label, 24f, UIColors.PaperHi, true);
        Localize(text, key);

        var look = tab.gameObject.AddComponent<StockTabLook>();
        look.button = button;
        look.fill = f;
        look.solidBorder = solid.gameObject;
        look.dashedBorder = dashed.gameObject;
        look.shadow = shadow.gameObject;
        look.label = text;
        look.tinted = lockIcon != null ? new Graphic[] { lockIcon.GetComponent<Graphic>() } : new Graphic[0];
        look.lockIcon = lockIcon;
        look.activeFill = fill;
        look.activeText = UIColors.PaperHi;
        look.inactiveFill = UIColors.PaperHi;
        look.inactiveText = UIColors.Ink;
        look.dashedWhenInactive = false;
        look.disabledFill = Sheet;
        look.disabledText = DisabledText;
        return look;
    }

    static StockMarketView.TicketLine TicketLine(RectTransform parent, string name)
    {
        var row = NewRect(parent, name);
        var le = Layout(row, 322f, 30f, 322f, 30f);
        HLayout(row.gameObject, 8f, null, TextAnchor.MiddleLeft, true, true, false, false);
        var label = LayoutText(row, "Label", "12 × $912.40", 22f, UIColors.Ink);
        Layout(label, -1f, -1f, -1f, -1f, 1f);
        var value = LayoutText(row, "Value", "$10,948.80", 22f, UIColors.Ink);
        value.alignment = TextAlignmentOptions.MidlineRight;
        Highlighter(value);
        return new StockMarketView.TicketLine { row = row.gameObject, label = label, value = value, layout = le };
    }

    /// <summary>The padlock: yellow body (52 x 38, radius 8) with a 3.5 ink border, the shackle, the keyhole (84 px box).</summary>
    static void Padlock(RectTransform parent)
    {
        var holder = Sized(NewRect(parent, "Padlock"), 318f, 84f);
        var box = Node(holder, "Lock", 117f, 0f, 84f, 84f);
        float k = 84f / 80f;
        var shackle = Node(box, "Shackle", 0f, 0f, 84f, 84f).gameObject.AddComponent<UIShape>();
        shackle.raycastTarget = false;
        shackle.color = UIColors.Ink;
        shackle.strokeWidth = 3.5f * k;
        var pts = new List<Vector2> { P(24f, 36f, k), P(24f, 26f, k) };
        for (int i = 1; i < 16; i++)
        {
            float a = Mathf.PI - Mathf.PI * i / 16f;
            pts.Add(P(40f + 16f * Mathf.Cos(a), 26f - 16f * Mathf.Sin(a), k));
        }
        pts.Add(P(56f, 26f, k));
        pts.Add(P(56f, 36f, k));
        shackle.AddStroke(pts.ToArray());
        var body = Node(box, "Body", 14f * k, 36f * k, 52f * k, 38f * k).gameObject.AddComponent<UIShape>();
        body.raycastTarget = false;
        body.color = UIColors.StickyYellow;
        body.Round(8f * k);
        var ring = Node(box, "Ring", 14f * k - 1.75f * k, 36f * k - 1.75f * k, 55.5f * k, 41.5f * k).gameObject.AddComponent<UIShape>();
        ring.raycastTarget = false;
        ring.color = UIColors.Ink;
        ring.border = 3.5f * k;
        ring.Round(9.75f * k);
        var hole = Node(box, "Hole", 36f * k, 44f * k, 8f * k, 8f * k).gameObject.AddComponent<UIShape>();
        hole.raycastTarget = false;
        hole.color = UIColors.Ink;
        hole.kind = UIShape.Kind.Ellipse;
        var slit = Node(box, "Slit", 0f, 0f, 84f, 84f).gameObject.AddComponent<UIShape>();
        slit.raycastTarget = false;
        slit.color = UIColors.Ink;
        slit.strokeWidth = 3.5f * k;
        slit.AddStroke(P(40f, 50f, k), P(40f, 59f, k));
    }

    /// <summary>A point of the 80 x 80 padlock drawing in an 84 x 84 centred UIShape (y up).</summary>
    static Vector2 P(float x, float y, float k) => new Vector2(x * k - 42f, 42f - y * k);

    /// <summary>Paper zigzag (the ticket's torn bottom): teeth 13.14 wide, 14 deep, a 2 px ink edge, the hard shadow.</summary>
    static void Zigzag(RectTransform ticket)
    {
        var zig = Sized(NewRect(ticket, "Zigzag"), 370f, 20f);
        var shadow = Stretch(zig, "Shadow").gameObject.AddComponent<UIShape>();
        Offset(shadow.rectTransform, 3f, 4f);
        var fill = Stretch(zig, "Fill").gameObject.AddComponent<UIShape>();
        var edge = Stretch(zig, "Edge").gameObject.AddComponent<UIShape>();
        shadow.raycastTarget = fill.raycastTarget = edge.raycastTarget = false;
        shadow.color = UIColors.Over(UIColors.Ink, 0.9f, Sheet);
        fill.color = UIColors.PaperHi;
        edge.color = UIColors.Ink;
        edge.strokeWidth = 2f;
        Vector2 Z(float x, float y) => new Vector2(x - 185f, 10f - y);
        foreach (var s in new[] { shadow, fill })
        {
            s.AddPolygon(Z(1f, 0f), Z(369f, 0f), Z(369f, 4.5f), Z(1f, 4.5f));
            for (int i = 0; i < 28; i++)
            {
                float x0 = 1f + i * 13.142857f;
                s.AddPolygon(Z(x0, 4f), Z(x0 + 6.571429f, 18f), Z(x0 + 13.142857f, 4f));
            }
        }
        var path = new List<Vector2> { Z(1f, -1f), Z(1f, 4f) };
        for (int i = 0; i < 28; i++)
        {
            float x0 = 1f + i * 13.142857f;
            path.Add(Z(x0 + 6.571429f, 18f));
            path.Add(Z(x0 + 13.142857f, 4f));
        }
        path.Add(Z(369f, -1f));
        edge.AddStroke(path.ToArray());
    }

    // ------------------------------------------------------------------
    // Footer: Cash · Holdings · Profit on them · Net worth (highlighted), Back to office on the right (60, 3 px rule)

    static void Footer(RectTransform page, StockMarketView view)
    {
        Box(page, "FooterRule", 0f, 860f, 1698f, 3f, null, UIColors.Ink);
        var row = Node(page, "Footer", 0f, 863f, 1400f, 57f);
        HLayout(row.gameObject, 18f, null, TextAnchor.MiddleLeft, true, true, false, false);
        view.cashText = Pair(row, "Cash", "Cash (company value)", "stocks.footer.cash", "$3,284,100", 22f);
        Dot(row, "Sep1", 22f);
        view.holdingsText = Pair(row, "Holdings", "Holdings", "stocks.footer.holdings", "$58,716.60", 22f);
        var profit = NewRect(row, "ProfitGroup");
        HLayout(profit.gameObject, 18f, null, TextAnchor.MiddleLeft, true, true, false, false);
        Dot(profit, "Sep", 22f);
        view.profitText = Pair(profit, "Profit", "Profit on them", "stocks.footer.profit", "+$2,196.68", 22f);
        view.profitGroup = profit.gameObject;
        Dot(row, "Sep3", 22f);
        view.netText = Pair(row, "Net", "Net worth", "stocks.footer.net", "$3,342,817", 26f);
        Highlighter(view.netText);

        float backW = TextWidth("Back to office", 22f) + 6f;
        float bw = 22f + 26f + 10f + backW + 22f;
        var back = PaperButtonNode(page, "Back", bw, 54f, "card", 3, UIColors.PaperHi, new Vector2(4f, 5f), out var face, out var content,
            out _, out _, out _, out _);
        Place(back, 1698f - bw, 864.5f, bw, 54f);
        HLayout(content.gameObject, 10f, Pad(22, 22, 0, 0), TextAnchor.MiddleCenter, true, true, false, false);
        var icon = LayoutIcon(content, "Icon", 26f, "arrow-left", UIColors.Ink);
        var label = LayoutText(content, "Label", "Back to office", 22f, UIColors.Ink);
        Localize(label, "stocks.footer.back");
        KeepWidth(label, backW, 15f);
        SetupMarker(back, face, new Graphic[] { icon, label });
        view.backButton = back.GetComponent<Button>();
    }

    static TextMeshProUGUI Dot(RectTransform row, string name, float size) => LayoutText(row, name, "·", size, UIColors.Pencil);

    /// <summary>"Label value" pair (8 apart): grey label (localized), bold value; returns the value.</summary>
    static TextMeshProUGUI Pair(RectTransform row, string name, string label, string key, string value, float valueSize)
    {
        var pair = NewRect(row, name);
        HLayout(pair.gameObject, 8f, null, TextAnchor.MiddleLeft, true, true, false, false);
        Localize(LayoutText(pair, "Label", label, 22f, UIColors.InkSoft), key);
        return LayoutText(pair, "Value", value, valueSize, UIColors.Ink, true);
    }

    // ------------------------------------------------------------------
    // Buttons and fields

    /// <summary>
    /// A marker button of a radius family ("card", "small"): hard ink shadow, fill, solid border (line2/3) and a hidden dashed
    /// one, content on the face. Sized by a layout (LayoutElement) or placed afterwards.
    /// </summary>
    static RectTransform PaperButtonNode(RectTransform parent, string name, float w, float h, string family, int line, Color fill, Vector2 shadowOffset,
        out RectTransform face, out RectTransform content, out Image fillImage, out Image solid, out Image dashed, out Image shadow)
    {
        var btn = NewRect(parent, name);
        btn.sizeDelta = new Vector2(w, h);
        Layout(btn, w, h, w, h);
        var hit = AddImage(btn.gameObject, null, Clear, true);
        var button = btn.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = hit;
        shadow = StretchBox(btn, "Shadow", S("Kit/" + family + "_fill"), UIColors.Ink, shadowOffset.x, shadowOffset.y, -shadowOffset.x, -shadowOffset.y);
        face = Stretch(btn, "Face");
        fillImage = StretchBox(face, "Fill", S("Kit/" + family + "_fill"), fill);
        solid = StretchBox(face, "Border", S("Kit/" + family + "_line" + line), UIColors.Ink);
        dashed = Dashed(StretchBox(face, "DashedBorder", S("Kit/" + family + "_dash" + line), UIColors.Pencil));
        dashed.gameObject.SetActive(false);
        content = Stretch(face, "Content");
        return btn;
    }

    /// <summary>Small chip button (34 tall, 2 px border, 2,3 shadow) with a 19 px label.</summary>
    static Button SmallButton(RectTransform parent, string name, float left, float top, float w, float h, string label, float size, out TMP_Text text)
    {
        var btn = PaperButtonNode(parent, name, w, h, "small", 2, UIColors.PaperHi, new Vector2(2f, 3f), out var face, out var content,
            out _, out _, out _, out _);
        Place(btn, left, top, w, h);
        var t = Stretch(content, "Label");
        text = SetupText(t.gameObject, label, size, UIColors.Ink, false, TextAlignmentOptions.Center);
        SetupMarker(btn, face, new Graphic[] { text });
        return btn.GetComponent<Button>();
    }

    /// <summary>52 x 52 square button with an icon (the amount stepper).</summary>
    static Button SquareButton(RectTransform parent, string name, float left, float top, string icon, string aria)
    {
        var btn = PaperButtonNode(parent, name, 52f, 52f, "small", 3, UIColors.PaperHi, new Vector2(3f, 4f), out var face, out var content,
            out _, out _, out _, out _);
        Place(btn, left, top, 52f, 52f);
        var ic = Icon(content, "Icon", 13f, 13f, 26f, icon, UIColors.Ink);
        SetupMarker(btn, face, new Graphic[] { ic });
        return btn.GetComponent<Button>();
    }

    /// <summary>The share amount: 52 tall, 3 px border, a faint inset line on top, 28 px bold centred digits.</summary>
    static TMP_InputField AmountField(RectTransform parent, float left, float top, float w, float h)
    {
        var box = Node(parent, "Amount", left, top, w, h);
        var bg = StretchBox(box, "Fill", S("Kit/small_fill"), UIColors.PaperHi);
        bg.raycastTarget = true;
        Box(box, "Inset", 6f, 3f, w - 12f, 3f, null, UIColors.Over(UIColors.Ink, 0.08f, UIColors.PaperHi));
        StretchBox(box, "Border", S("Kit/small_line3"), UIColors.Ink);
        var area = Stretch(box, "TextArea", 12f, 0f, 12f, 0f);
        area.gameObject.AddComponent<RectMask2D>();
        var textRt = Stretch(area, "Text");
        var text = SetupText(textRt.gameObject, "12", 28f, UIColors.Ink, true, TextAlignmentOptions.Center);
        var placeRt = Stretch(area, "Placeholder");
        var place = SetupText(placeRt.gameObject, "0", 28f, UIColors.WithAlpha(UIColors.Pencil, 0.6f), true, TextAlignmentOptions.Center);
        var field = box.gameObject.AddComponent<TMP_InputField>();
        field.targetGraphic = bg;
        field.textViewport = area;
        field.textComponent = text;
        field.placeholder = place;
        field.contentType = TMP_InputField.ContentType.IntegerNumber;
        field.characterLimit = 13;
        field.fontAsset = UIBuild.Font;
        field.pointSize = 28f;
        field.caretColor = UIColors.Ink;
        field.customCaretColor = true;
        field.selectionColor = UIColors.WithAlpha(UIColors.Highlight, 0.6f);
        field.text = "12";
        return field;
    }

    // ------------------------------------------------------------------
    // Small helpers

    static void Localize(TMP_Text text, string key)
    {
        if (text == null) return;
        var l = text.gameObject.AddComponent<L10nText>();
        l.table = Table;
        l.key = key;
    }

    /// <summary>A button's layout label held at <paramref name="width"/>: a longer translation shrinks (to <paramref name="min"/>) inside it.</summary>
    static void KeepWidth(TMP_Text label, float width, float min)
    {
        Layout(label, -1f, -1f, width, -1f);
        label.enableAutoSizing = true;
        label.fontSizeMin = min;
        label.fontSizeMax = label.fontSize;
    }

    static TextMeshProUGUI CenteredText(RectTransform parent, string name, string text, float size, Color color, bool bold, float lineHeight)
    {
        var t = LayoutText(parent, name, text, size, color, bold, lineHeight: lineHeight / size);
        t.alignment = TextAlignmentOptions.Top;
        t.textWrappingMode = TextWrappingModes.Normal;
        return t;
    }

    static TextHighlighter Highlighter(TMP_Text text)
    {
        var h = text.gameObject.AddComponent<TextHighlighter>();
        h.color = UIColors.WithAlpha(UIColors.Highlight, 0.95f);
        return h;
    }

    static UIShape Ring(Transform parent, string name, float left, float top, float w, float h, Color color, float border, float radius)
    {
        var rt = Node(parent, name, left, top, w, h);
        var s = rt.gameObject.AddComponent<UIShape>();
        s.raycastTarget = false;
        s.color = color;
        s.border = border;
        if (radius > 0f) s.Round(radius);
        return s;
    }

    static UIShape StretchShape(RectTransform parent, string name, Color color, float border, Vector4 rx, Vector4 ry)
    {
        var s = Stretch(parent, name).gameObject.AddComponent<UIShape>();
        s.raycastTarget = false;
        s.color = color;
        s.border = border;
        s.Round(rx, ry);
        return s;
    }

    /// <summary>A layout child with a fixed size (also set on the rect, so children can be placed in it now).</summary>
    static RectTransform Sized(RectTransform rt, float w, float h)
    {
        rt.sizeDelta = new Vector2(w, h);
        Layout(rt, w, h, w, h);
        return rt;
    }

    /// <summary>Move a stretched child by (dx, dy) design px (right, down).</summary>
    static void Offset(RectTransform rt, float dx, float dy)
    {
        rt.offsetMin += new Vector2(dx, -dy);
        rt.offsetMax += new Vector2(dx, -dy);
    }

    /// <summary>Change a fixed-size node's pivot without moving it (the view moves some by their top-left corner).</summary>
    static void Pivot(RectTransform rt, Vector2 pivot)
    {
        Vector2 size = rt.sizeDelta;
        Vector2 delta = pivot - rt.pivot;
        rt.pivot = pivot;
        rt.anchoredPosition += new Vector2(delta.x * size.x, delta.y * size.y);
    }

    static T Left<T>(T c) where T : Component
    {
        Pivot((RectTransform)c.transform, new Vector2(0f, 1f));
        return c;
    }

    // ------------------------------------------------------------------
    // Install

    public static void Install(GameObject prefab, List<string> log)
    {
        Scene scene = SceneManager.GetSceneByPath(OfficeHudBuilder.ScenePath);
        if (!scene.IsValid() || !scene.isLoaded)
            scene = EditorSceneManager.OpenScene(OfficeHudBuilder.ScenePath, OpenSceneMode.Single);

        GameObject instance = null;
        foreach (var r in scene.GetRootGameObjects())
            if (PrefabUtility.GetCorrespondingObjectFromSource(r) == prefab) instance = r;
        if (instance == null)
        {
            instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.name = "StockMarket";
            log.Add("MainGame: added the StockMarket prefab.");
        }
        else
            log.Add("MainGame: StockMarket already there (the prefab update applies to it).");
        instance.SetActive(true);
        var view = instance.GetComponent<StockMarketView>();

        StockMarketUI ui = null;
        foreach (var r in scene.GetRootGameObjects())
        {
            ui = r.GetComponentInChildren<StockMarketUI>(true);
            if (ui != null) break;
        }
        if (ui == null)
        {
            StockMarketManager manager = null;
            foreach (var r in scene.GetRootGameObjects())
            {
                manager = r.GetComponentInChildren<StockMarketManager>(true);
                if (manager != null) break;
            }
            if (manager == null)
            {
                log.Add("MainGame: no StockMarketManager, so no StockMarketUI to connect.");
                EditorSceneManager.SaveScene(scene);
                return;
            }
            ui = manager.gameObject.AddComponent<StockMarketUI>();
            log.Add("MainGame: added StockMarketUI to " + manager.name + ".");
        }

        var so = new SerializedObject(ui);
        so.FindProperty("view").objectReferenceValue = view;
        so.FindProperty("stockMarketPanel").objectReferenceValue = view.root;
        so.FindProperty("closeButton").objectReferenceValue = view.closeButton;
        so.FindProperty("shareAmountInput").objectReferenceValue = view.amountInput;
        foreach (var field in new[]
                 {
                     "stockListContent", "stockDetailPanel", "stockItemPrefab", "portfolioValueText", "cashAvailableText", "totalAssetsText",
                     "detailSymbolText", "detailNameText", "detailPriceText", "detailChangeText", "detailOwnedText", "detailValueText", "detailTypeText",
                     "buyButton", "sellButton", "buyMaxButton", "sellAllButton", "tradeInfoText", "tradeMessageText"
                 })
            so.FindProperty(field).objectReferenceValue = null;
        so.FindProperty("positiveColor").colorValue = UIColors.Money;
        so.FindProperty("negativeColor").colorValue = UIColors.Loss;
        so.FindProperty("neutralColor").colorValue = UIColors.Ink;
        so.ApplyModifiedPropertiesWithoutUndo();
        log.Add("MainGame: StockMarketUI (" + ui.name + ") draws with the designed screen; the fallback panel isn't built.");

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        log.Add("MainGame saved.");
    }

    /// <summary>Remove the prefab from MainGame and clear StockMarketUI's references: the fallback panel is built again.</summary>
    public static void Restore(List<string> log)
    {
        Scene scene = SceneManager.GetSceneByPath(OfficeHudBuilder.ScenePath);
        if (!scene.IsValid() || !scene.isLoaded)
            scene = EditorSceneManager.OpenScene(OfficeHudBuilder.ScenePath, OpenSceneMode.Single);
        int removed = 0;
        foreach (var r in scene.GetRootGameObjects())
        {
            var ui = r.GetComponentInChildren<StockMarketUI>(true);
            if (ui != null)
            {
                var so = new SerializedObject(ui);
                so.FindProperty("view").objectReferenceValue = null;
                so.FindProperty("stockMarketPanel").objectReferenceValue = null;
                so.FindProperty("closeButton").objectReferenceValue = null;
                so.FindProperty("shareAmountInput").objectReferenceValue = null;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }
        foreach (var r in scene.GetRootGameObjects())
        {
            if (r.GetComponent<StockMarketView>() == null) continue;
            UnityEngine.Object.DestroyImmediate(r);
            removed++;
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        log.Add("MainGame: removed " + removed + " StockMarket screen(s); StockMarketUI builds its fallback panel again.");
    }

    // ------------------------------------------------------------------
    // Previews: the four boards' moments

    static readonly (string sym, string company, StockType type, float price, float change)[] SampleRows =
    {
        ("AAPL", "Apple Corp", StockType.Stable, 152.10f, 1.4f), ("GOOG", "Goggle Inc", StockType.Normal, 2764.00f, float.NaN),
        ("MSFT", "Macrosoft", StockType.Stable, 303.25f, 1.1f), ("AMZN", "Amazon River", StockType.Normal, 3318.40f, 0.6f),
        ("TSLA", "Teslo Motors", StockType.Volatile, 912.40f, float.NaN), ("NVDA", "Nvidio Graphics", StockType.Volatile, 243.80f, float.NaN),
        ("GME", "GameStomp", StockType.Meme, 211.60f, float.NaN), ("AMC", "AMC Cinemas", StockType.Meme, 36.20f, -9.5f),
        ("DOGE", "DogeCorp", StockType.Meme, 3.62f, 13.3f), ("MOON", "ToTheMoon Inc", StockType.Meme, 4.10f, -18.0f),
        ("KO", "Coca-Coola", StockType.Dividend, 55.40f, 0.7f), ("JNJ", "Johnson Bros", StockType.Dividend, 164.20f, -0.5f),
        ("PG", "Proctor Gambler", StockType.Dividend, 140.90f, 0.6f), ("WMT", "WallMart", StockType.Dividend, 146.30f, 0.9f),
        ("SPCE", "Space Virgin", StockType.Volatile, 22.80f, -8.8f), ("PLTR", "Palintear", StockType.Volatile, 23.40f, 6.4f),
        ("COIN", "CoinBased", StockType.Volatile, 262.00f, 4.8f), ("ARKK", "ARK Invest", StockType.Volatile, 117.30f, -2.3f),
        ("WISH", "WishDotCom", StockType.Meme, 2.61f, -13.0f), ("CLOV", "CloverLeaf", StockType.Volatile, 8.44f, 5.5f)
    };

    static readonly Dictionary<string, float[]> SampleHistory = new Dictionary<string, float[]>
    {
        { "TSLA", new[] { 884.1f, 881.53f, 889.82f, 887.66f, 884.24f, 872.21f, 870.26f, 886.93f, 894.02f, 910.05f, 914.77f, 921.65f, 925.48f, 902.2f, 915.71f, 924.25f, 932.76f, 908.93f, 884.96f, 873.49f, 867.99f, 873.27f, 873.66f, 882.02f, 874.03f, 879.4f, 886.01f, 877.72f, 903.24f, 912.4f } },
        { "GME", new[] { 169.55f, 164.02f, 166.47f, 172.12f, 170.09f, 164.02f, 164.28f, 167.24f, 173.46f, 167.83f, 162.02f, 166.07f, 169.05f, 177.64f, 183.6f, 183.69f, 182.83f, 198.18f, 197.17f, 226.79f, 217.04f, 215.54f, 214.37f, 210.1f, 207.66f, 207.33f, 212.43f, 220.31f, 222.48f, 211.6f } },
        { "NVDA", new[] { 254.2f, 250.86f, 247.66f, 249.43f, 243.15f, 242.71f, 236.7f, 239.53f, 240.01f, 243.55f, 242.15f, 243.15f, 242.33f, 240.31f, 240.64f, 237.28f, 236.29f, 238.05f, 238.15f, 237.02f, 242.74f, 242.83f, 241.21f, 241.58f, 240.13f, 239.06f, 238.08f, 243.39f, 243.39f, 243.8f } },
        { "GOOG", new[] { 2800.4f, 2802.33f, 2822.14f, 2808.19f, 2824.04f, 2820.49f, 2816.91f, 2846.98f, 2849.93f, 2849.74f, 2861.68f, 2879.95f, 2879.95f, 2889.76f, 2874.81f, 2869.5f, 2863.08f, 2842.66f, 2819.65f, 2795.0f, 2791.8f, 2789.63f, 2785.19f, 2786.71f, 2766.79f, 2766.04f, 2770.13f, 2782.07f, 2769.62f, 2764.0f } }
    };

    /// <summary>A model like the board of that state: buy (TSLA), sell (GME, Owned tab), locked (NVDA), broke (GOOG).</summary>
    public static StockScreenModel SampleModel(string state)
    {
        var m = new StockScreenModel { day = 42, feePercent = 1f, cash = 3284100 };
        bool locked = state == "locked";
        var holdings = locked
            ? new Dictionary<string, (long, float)>()
            : new Dictionary<string, (long, float)> { { "AAPL", (150, 148.20f) }, { "TSLA", (24, 891.83f) }, { "GME", (40, 182.30f) }, { "KO", (100, 55.94f) } };
        foreach (var r in SampleRows)
        {
            float change = r.change;
            if (float.IsNaN(change) && SampleHistory.TryGetValue(r.sym, out var h))
                change = (h[h.Length - 1] / h[0] - 1f) * 100f;
            holdings.TryGetValue(r.sym, out var own);
            m.rows.Add(new StockRowModel
            {
                symbol = r.sym, company = r.company, type = r.type, price = r.price, change = change, owned = own.Item1, averagePrice = own.Item2
            });
        }
        if (!locked)
        {
            m.trades.Add(new StockTradeModel { day = 42, buy = true, symbol = "TSLA", shares = 12, price = 912.40f, cash = 12 * 912.40 * 1.01 });
            m.trades.Add(new StockTradeModel { day = 42, buy = true, symbol = "TSLA", shares = 12, price = 871.25f, cash = 12 * 871.25 * 1.01 });
            m.trades.Add(new StockTradeModel { day = 41, buy = false, symbol = "AMC", shares = 60, price = 36.20f, cash = 60 * 36.20 * 0.99, profit = 60 * 36.20 * 0.99 - 60 * 39.14 });
            m.trades.Add(new StockTradeModel { day = 40, buy = true, symbol = "GME", shares = 40, price = 182.30f, cash = 40 * 182.30 * 1.01 });
            m.trades.Add(new StockTradeModel { day = 39, buy = true, symbol = "KO", shares = 100, price = 55.94f, cash = 100 * 55.94 * 1.01 });
        }

        string sel = state == "sell" ? "GME" : locked ? "NVDA" : state == "broke" ? "GOOG" : "TSLA";
        m.selected = sel;
        m.history.AddRange(SampleHistory[sel]);
        var row = m.Selected;
        m.maxBuy = (long)Math.Floor(m.cash / (row.price * 1.01));
        m.locked = locked;
        m.showOwned = state == "sell";
        m.sellMode = state == "sell";
        m.amount = state == "sell" ? 40 : state == "broke" ? 2000 : locked ? 1 : 12;
        if (state == "buy")
            m.message = L10n.Get(Table, "stocks.msg.bought", ("count", "12"), ("symbol", "TSLA"), ("amount", StockFormat.Money(12 * 912.40 * 1.01)));
        return m;
    }

    public static void RenderPreviews(GameObject hud, GameObject prefab, string dir, List<string> log)
    {
        Directory.CreateDirectory(dir);
        foreach (var state in new[] { "buy", "sell", "locked", "broke" })
        {
            var model = SampleModel(state);
            UIPreview.Render(Path.Combine(dir, "stocks_" + state + ".png"), log, new UIPreview.Layer(hud), new UIPreview.Layer(prefab, g =>
            {
                var v = g.GetComponent<StockMarketView>();
                v.root.SetActive(true);
                Canvas.ForceUpdateCanvases();
                v.Render(model);
                Canvas.ForceUpdateCanvases();
                v.Render(model);   // again, once the layout groups have sized the texts the view measures
            }));
        }
    }
}
