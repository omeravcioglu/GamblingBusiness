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
/// Builds the office HUD from the design canvas ("Office HUD: reviewing CVs", 1920x1080) into
/// Assets/UI/Prefabs/OfficeHUD.prefab and installs it in MainGame:
/// - GameFlowManager's Skip / Hire / Next batch buttons point at the new buttons
/// - HiringTaskManager's old UI fields are cleared (the new clipboard reads the manager itself)
/// - build mode hides the new HUD's desk layer
/// - the old HUD objects (stats panel, task panel, Hire, Skip, NewBatch, Skills, Stocks buttons) are switched off
/// Tools > Game > New UI > Restore Old HUD undoes the scene part. Rebuilding overwrites manual edits to the prefab.
/// Fixed labels are bound to the Hud String Table (Localize, Assets/Localization/Source/Hud.csv); the HUD's views write
/// the changing texts. Buttons, stamps and badges grow with their text, which is longer in some languages.
/// </summary>
public static class OfficeHudBuilder
{
    public const string PrefabPath = "Assets/UI/Prefabs/OfficeHUD.prefab";
    public const string ScenePath = "Assets/Scenes/MainGame.unity";
    const string T = "Hud";

    static readonly string[] OldHudPaths =
    {
        "Canvas/CompanyStatsPanel", "Canvas/HiringTaskPanel", "Canvas/Hire", "Canvas/Skip",
        "Canvas/NewBatch", "Canvas/SkillTreeMan", "Canvas/StockMarket"
    };

    static readonly Color Black07 = UIColors.Dark(0.07f);
    static readonly Color Black06 = UIColors.Dark(0.06f);
    static readonly Color HoleColor = UIColors.Hex(0x2A332F);

    // ------------------------------------------------------------------
    // Menu / batch entry points

    [MenuItem("Tools/Game/New UI/Build Office HUD")]
    public static void BuildFromMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Build Office HUD", "Stop Play mode first.", "OK");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;
        var log = new List<string>();
        var prefab = BuildPrefab(log);
        if (prefab != null)
            InstallInMainGame(prefab, log);
        string summary = string.Join("\n", log);
        Debug.Log("[OfficeHudBuilder]\n" + summary);
        EditorUtility.DisplayDialog("Office HUD", summary.Length > 1500 ? summary.Substring(0, 1500) + "\n…" : summary, "OK");
    }

    /// <summary>Batch mode: -executeMethod OfficeHudBuilder.BuildFromCommandLine [-installHud] [-hudPreview path.png]</summary>
    public static void BuildFromCommandLine()
    {
        int code = 0;
        try
        {
            var log = new List<string>();
            var args = System.Environment.GetCommandLineArgs();
            var prefab = BuildPrefab(log);
            if (prefab == null) code = 1;
            else
            {
                if (System.Array.IndexOf(args, "-installHud") >= 0)
                    InstallInMainGame(prefab, log);
                int i = System.Array.IndexOf(args, "-hudPreview");
                if (i >= 0 && i + 1 < args.Length)
                    UIPreview.RenderOfficeHud(prefab, args[i + 1], log);
            }
            Debug.Log("[OfficeHudBuilder]\n" + string.Join("\n", log));
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            code = 1;
        }
        EditorApplication.Exit(code);
    }

    // ------------------------------------------------------------------
    // Prefab

    public static GameObject BuildPrefab(List<string> log)
    {
        UIKitCheck(log);
        Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
        GameObject root = null;
        try
        {
            root = BuildHud();
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool ok);
            if (!ok || prefab == null)
            {
                log.Add("Could not save " + PrefabPath);
                return null;
            }
            log.Add("Saved " + PrefabPath + (UIFonts.HasComingSoon ? "" : " (font: LiberationSans until ComingSoon-Regular.ttf is in Assets/UI/Fonts)"));
            return prefab;
        }
        finally
        {
            if (root != null) Object.DestroyImmediate(root);
        }
    }

    static void UIKitCheck(List<string> log)
    {
        var probe = S("Kit/card_fill");
        if (probe == null || probe.border == Vector4.zero)
        {
            UIKitImportSettings.ReimportAll();
            log.Add("Reimported the UI kit sprites (Assets/UI).");
        }
    }

    static GameObject BuildHud()
    {
        var root = new GameObject("OfficeHUD", typeof(RectTransform));
        root.layer = 5;
        var rootRt = (RectTransform)root.transform;
        rootRt.sizeDelta = new Vector2(1920f, 1080f);   // design frame while placing children

        var hud = root.AddComponent<OfficeHud>();
        var tooltip = root.AddComponent<HudTooltip>();

        var desk = Stretch(rootRt, "Desk");
        hud.deskLayer = desk.gameObject;

        BuildApplicantSticky(desk, hud);
        BuildGoldenSticky(desk, hud);
        BuildLedger(desk, hud);
        BuildValuePop(desk, hud);
        BuildToolbar(desk, hud);
        BuildPause(desk, hud);
        BuildTaskBoard(desk);
        BuildActionBar(desk, hud);
        BuildToasts(rootRt);
        BuildTooltip(rootRt, tooltip);

        // The canvas goes on last: a root canvas resizes its rect to the screen, and the anchored
        // positions above are computed in the 1920x1080 design frame
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = -1;   // pop-ups on the main canvas (order 0) draw above the HUD
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        scaler.referencePixelsPerUnit = 100f;
        root.AddComponent<GraphicRaycaster>();
        return root;
    }

    // ------------------------------------------------------------------
    // Company ledger (green-bar paper), top-left: 32, 24, 420 x 310

    static void BuildLedger(RectTransform desk, OfficeHud hud)
    {
        const float L = 32f, T = 24f, W = 420f, H = 310f;
        Shadow(desk, "LedgerShadow", L, T, W, H, 0f, 22f, 48f, 0.45f);
        var ledger = Node(desk, "Ledger", L, T, W, H);

        var paper = StretchBox(ledger, "Paper", S("Kit/panel_fill"), UIColors.Ledger);
        paper.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        var bands = StretchBox(paper.transform, "Bands", S("Kit/Tiles/ledger_band68"), Color.white, 3f, 3f, 3f, 3f);
        bands.type = Image.Type.Tiled;

        var strip = Node(paper.transform, "TractorFeed", 3f, 3f, 28f, 304f);
        var perforation = Box(strip, "Perforation", 26f, 0f, 2f, 304f, S("Kit/Tiles/dash_v2"), UIColors.LedgerLine);
        perforation.type = Image.Type.Tiled;
        for (int i = 0; i < 9; i++)
            Box(strip, "Hole", 7f, 14f + i * 34f, 12f, 12f, S("Kit/Circles/circle12_fill"), HoleColor);

        StretchBox(ledger, "Border", S("Kit/panel_line3"), UIColors.Ink);

        // Content column: x 49..395 (346 wide); rows stack from y 17 with 6 px gaps (CSS flex column).
        // Row heights use the font's real "normal" line height, like the browser did for the design.
        float normal = UIFonts.NaturalLineHeight(UIBuild.Font);
        float y = 17f;
        float h = 26f * 1.3f;
        var name = Text(ledger, "CompanyName", 49f, y, 346f, h, "Nova Holdings", 26f, UIColors.Ink, bold: true);
        name.overflowMode = TextOverflowModes.Ellipsis;
        hud.companyNameText = name;
        y += h + 6f;

        h = 17f * normal;
        Localize(Text(ledger, "ValueLabel", 49f, y, 346f, h, "COMPANY VALUE", 17f, UIColors.Pencil, letterSpacingEm: 0.08f),
            "Status", "status.ledger.value_label");   // the Status ledger pictures' label
        y += h + 6f;

        h = 54f * 1.05f;
        hud.valueText = Text(ledger, "Value", 49f, y, 346f, h, "$3,284,100", 54f, UIColors.Ink, bold: true);
        y += h + 6f;

        h = Mathf.Max(26f, 22f * normal);
        var up = Icon(ledger, "TrendUp", 49f, y + (h - 26f) * 0.5f, 26f, "trend-up", UIColors.Money, "b");
        var down = Icon(ledger, "TrendDown", 49f, y + (h - 26f) * 0.5f, 26f, "trend-down", UIColors.Loss, "b");
        down.gameObject.SetActive(false);
        hud.changeUpIcon = up.gameObject;
        hud.changeDownIcon = down.gameObject;
        hud.changeText = Text(ledger, "Change", 83f, y, 312f, h, "+$12,400 today", 22f, UIColors.Money);
        y += h + 6f;

        // dashed rule: 2 px line with 2 px margins
        var rule = Box(ledger, "Rule", 49f, y + 2f, 346f, 2f, S("Kit/Tiles/dash_h2"), UIColors.LedgerLine);
        rule.type = Image.Type.Tiled;
        y += 6f + 6f;

        // Day + day progress (left) and staff (right)
        h = Mathf.Max(24f, 19f * normal);
        var dayGroup = Node(ledger, "Day", 49f, y, 230f, h);
        HLayout(dayGroup.gameObject, 8f, null, TextAnchor.MiddleLeft);
        LayoutIcon(dayGroup, "Icon", 24f, "calendar", UIColors.Ink);
        hud.dayText = LayoutText(dayGroup, "DayText", "Day 42", 19f, UIColors.Ink);
        var dayBar = Bar(dayGroup, "DayBar", 78f, 12f, UIColors.Highlight, 0.64f, out var dayFill);
        Layout(dayBar, 78f, 12f, 78f, 12f);
        hud.dayFill = dayFill;

        var staffGroup = Node(ledger, "Staff", 245f, y, 150f, h);
        HLayout(staffGroup.gameObject, 8f, null, TextAnchor.MiddleRight);
        LayoutIcon(staffGroup, "Icon", 24f, "people", UIColors.Ink);
        hud.staffText = LayoutText(staffGroup, "StaffText", "18 staff", 19f, UIColors.Ink);
        y += h + 6f;

        MeterRow(ledger, "Happiness", y, h, "smile", UIColors.Money, "Happiness", "hud.ledger.happiness", 0.72f,
            out hud.happinessFill, out hud.happinessText);
        y += h + 6f;
        MeterRow(ledger, "Trust", y, h, "shield", UIColors.Info, "Investor trust", "hud.ledger.trust", 0.58f,
            out hud.trustFill, out hud.trustText);
    }

    static void MeterRow(RectTransform ledger, string name, float top, float h, string icon, Color color, string label, string key,
        float value, out RectTransform fill, out TMP_Text valueText)
    {
        Icon(ledger, name + "Icon", 49f, top + (h - 24f) * 0.5f, 24f, icon, color);
        Shrink(Localize(Text(ledger, name + "Label", 83f, top, 128f, h, label, 19f, UIColors.Ink), T, key), 15f);   // the bar starts right after it
        var bar = Bar(ledger, name + "Bar", 118f, 14f, color, value, out fill);
        Place(bar, 221f, top + (h - 14f) * 0.5f, 118f, 14f);
        valueText = Text(ledger, name + "Value", 349f, top, 46f, h, Mathf.RoundToInt(value * 100f) + "%", 19f, UIColors.Ink,
            align: TextAlignmentOptions.Right);
    }

    /// <summary>Progress bar: paper-hi track with a 2 px ink border; the fill's right edge is anchorMax.x (0..1).</summary>
    static RectTransform Bar(Transform parent, string name, float width, float height, Color fillColor, float value, out RectTransform fill)
    {
        var bar = NewRect(parent, name);
        bar.sizeDelta = new Vector2(width, height);
        int h = Mathf.RoundToInt(height);
        AddImage(bar.gameObject, S("Kit/Bars/bar" + h + "_fill"), UIColors.PaperHi);
        var inner = Stretch(bar, "Inner", 2f, 2f, 2f, 2f);
        var f = NewRect(inner, "Fill");
        f.anchorMin = Vector2.zero;
        f.anchorMax = new Vector2(value, 1f);
        f.offsetMin = f.offsetMax = Vector2.zero;
        AddImage(f.gameObject, null, fillColor);
        StretchBox(bar, "Border", S("Kit/Bars/bar" + h + "_line2"), UIColors.Ink);
        fill = f;
        return bar;
    }

    // ------------------------------------------------------------------
    // Value pop after a hire: "+$4,200" floating off the ledger

    static void BuildValuePop(RectTransform desk, OfficeHud hud)
    {
        var pop = Node(desk, "ValuePop", 356f, 92f, 190f, 46f, Anchor.TopLeft, -6f);
        var group = pop.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;
        Icon(pop, "Arrow", 0f, 8f, 30f, "arrow-up", UIColors.Money, "b");
        var text = Text(pop, "Amount", 36f, 0f, 154f, 46f, "+$4,200", 34f, UIColors.Money, bold: true);
        var mat = UIFonts.PopMaterial;
        if (mat != null) text.fontSharedMaterial = mat;
        hud.valuePop = pop;
        hud.valuePopText = text;
        hud.valuePopGroup = group;
    }

    // ------------------------------------------------------------------
    // Toolbar: sticky-note tabs hanging from the top edge (138 x 94, top -12)

    static void BuildToolbar(RectTransform desk, OfficeHud hud)
    {
        var bar = Stretch(desk, "Toolbar");

        // Every tab can be locked (FeatureGate keeps the early ones locked for a while). The hints here are the English
        // samples: OfficeHud and FeatureTabs write them in the player's language.
        hud.skillsTab = Tab(bar, "Skills", 506f, -1.5f, UIColors.StickyYellow, UIColors.Ink, "sparkle", "Skills", "hud.tab.skills", null);
        CountBadge(hud.skillsTab);
        NewBadge(hud.skillsTab, true);

        hud.staffTab = Tab(bar, "Staff", 660f, 1f, UIColors.StickyBlue, UIColors.Ink, "people", "Staff", "hud.tab.staff", null);
        NewBadge(hud.staffTab, false);

        hud.stocksTab = Tab(bar, "Stocks", 814f, -0.8f, UIColors.StickyGreen, UIColors.Ink, "chart", "Stocks", "hud.tab.stocks",
            "unlock with Stock Market Access (Growth, tier 3)");
        NewBadge(hud.stocksTab, false);
        hud.casinoTab = Tab(bar, "Casino", 968f, 1.2f, UIColors.StickyPink, UIColors.Ink, "dice", "Casino", "hud.tab.casino",
            "unlock with Casino License (Chaos & Luck, tier 5)");
        NewBadge(hud.casinoTab, false);

        hud.buildTab = Tab(bar, "Build", 1122f, -1f, UIColors.Blueprint, UIColors.BlueprintInk, "pencil", "Build", "hud.tab.build", null,
            blueprint: true);
        Keycap(hud.buildTab.openLook.transform, "Keycap", 114f, 74f, "Q", 18f, 34f);
        NewBadge(hud.buildTab, false);

        hud.onlineTab = Tab(bar, "Online", 1276f, 0.8f, UIColors.PaperHi, UIColors.Ink, "globe", "Online", "hud.tab.online",
            "opens when the company is worth $1,000,000,000", band: Black06);
        OnlineBadges(hud.onlineTab);

        // design "mid" scenario: Skills has 2 points, the rest locked
        hud.stocksTab.openLook.SetActive(false);
        hud.stocksTab.lockedLook.SetActive(true);
        hud.casinoTab.openLook.SetActive(false);
        hud.casinoTab.lockedLook.SetActive(true);
        hud.onlineTab.openLook.SetActive(false);
        hud.onlineTab.lockedLook.SetActive(true);
    }

    static HudTab Tab(RectTransform parent, string name, float left, float rotation, Color color, Color textColor,
        string icon, string label, string key, string lockedHint, bool blueprint = false, Color? band = null)
    {
        var rt = Node(parent, name + "Tab", left, -12f, 138f, 94f, Anchor.Top, rotation);
        var hit = AddImage(rt.gameObject, null, Clear, true);
        var button = rt.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = hit;
        var nav = button.navigation;
        nav.mode = Navigation.Mode.None;
        button.navigation = nav;

        var tab = rt.gameObject.AddComponent<HudTab>();
        tab.button = button;
        tab.title = label;
        tab.titleKey = key;
        tab.lockedHint = lockedHint ?? "";
        var swing = Stretch(rt, "Swing");
        tab.swing = swing;

        // Open: a sticky note (or blueprint paper for Build)
        var open = Stretch(swing, "Open");
        StretchShadow(open, 0f, 10f, 16f, 0.3f);
        StretchBox(open, "Fill", S("Kit/tab_fill"), color);
        if (blueprint)
        {
            var grid = StretchBox(open, "Grid", S("Kit/Tiles/blueprint16"), Color.white);
            grid.type = Image.Type.Tiled;
        }
        else
        {
            Box(open, "Band", 0f, 0f, 138f, 22f, null, band ?? Black07);
        }
        Icon(open, "Icon", 54f, 24f, 30f, icon, textColor);
        Shrink(Localize(Text(open, "Label", 0f, 56f, 138f, 31f, label, 22f, textColor, align: TextAlignmentOptions.Center), T, key), 16f);
        tab.openLook = open.gameObject;

        // Locked: dashed paper-shade tab, 4 px shorter, lock badge (FeatureTabs keeps a designed look instead of copying one)
        var locked = Stretch(swing, "Locked", 0f, 0f, 0f, 4f);
        StretchShadow(locked, 0f, 8f, 12f, 0.22f);
        StretchBox(locked, "Fill", S("Kit/tab_fill"), UIColors.PaperShade);
        Dashed(StretchBox(locked, "Border", S("Kit/tab_dash3_notop"), UIColors.Pencil));
        Icon(locked, "Icon", 54f, 24f, 30f, icon, UIColors.Pencil);
        Shrink(Localize(Text(locked, "Label", 0f, 56f, 138f, 31f, label, 22f, UIColors.Pencil, align: TextAlignmentOptions.Center), T, key), 16f);
        if (name != "Online")
        {
            var badge = Node(locked, "LockBadge", 118f, 16f, 32f, 32f);
            AddImage(badge.gameObject, S("Kit/Circles/circle32_fill"), UIColors.PaperHi);
            StretchBox(badge, "Ring", S("Kit/Circles/circle32_line2"), UIColors.Ink);
            Icon(badge, "Lock", 7f, 7f, 18f, "lock", UIColors.Ink, "b");
        }
        tab.lockedLook = locked.gameObject;
        locked.gameObject.SetActive(false);
        return tab;
    }

    static void CountBadge(HudTab tab)
    {
        var badge = Node(tab.openLook.transform, "CountBadge", 118f, 18f, 30f, 30f);
        Box(badge, "Ring", -2f, -2f, 34f, 34f, S("Kit/Circles/circle34_line2"), UIColors.Ink);
        StretchBox(badge, "Fill", S("Kit/Circles/circle30_fill"), UIColors.Loss);
        StretchBox(badge, "Edge", S("Kit/Circles/circle30_line2"), UIColors.PaperHi);
        tab.countText = Text(badge, "Count", 0f, 0f, 30f, 30f, "2", 17f, UIColors.PaperHi, bold: true, align: TextAlignmentOptions.Center);
        tab.countBadge = badge.gameObject;
    }

    static void OnlineBadges(HudTab tab)
    {
        // Locked: "$1B" chip with a small lock
        float chipW = 8f + 16f + 3f + TextWidth("$1B", 17f, true) + 8f;
        var chip = Node(tab.lockedLook.transform, "PriceChip", 138f + 18f - chipW, 14f, chipW, 32f);
        ShapeLayers(chip, "small", UIColors.PaperHi, UIColors.Ink, 2);
        Icon(chip, "Lock", 8f, 8f, 16f, "lock", UIColors.Ink, "b");
        Text(chip, "Price", 27f, 0f, chipW - 27f, 32f, "$1B", 17f, UIColors.Ink, bold: true);

        // Unlocked: the red "NEW" pill
        NewBadge(tab, false);
    }

    /// <summary>
    /// Red "NEW" pill at a top corner of the open tab, 16 px past its edge, as wide as the word (it differs per language);
    /// Skills keeps its right corner for the point count. OfficeHud (Online) and FeatureTabs (the others) switch it on;
    /// FeatureTabs keeps a designed badge instead of drawing one.
    /// </summary>
    static void NewBadge(HudTab tab, bool leftCorner)
    {
        float newW = 10f + TextWidth("NEW", 17f, true) + 10f;
        var pill = Node(tab.openLook.transform, "NewBadge", leftCorner ? -16f : 138f + 16f - newW, 14f, newW, 30f);
        if (leftCorner) PivotLeft(pill);
        else PivotRight(pill);
        IgnoreLayout(StretchBox(pill, "Ring", S("Kit/Pills/pill34_line2"), UIColors.Ink, -2f, -2f, -2f, -2f));
        IgnoreLayout(StretchBox(pill, "Fill", S("Kit/Pills/pill30_fill"), UIColors.Loss));
        IgnoreLayout(StretchBox(pill, "Edge", S("Kit/Pills/pill30_line2"), UIColors.PaperHi));
        HLayout(pill.gameObject, 0f, Pad(10, 10, 0, 0), TextAnchor.MiddleCenter, true, true, false, false);
        Fit(pill.gameObject, true, false);
        Localize(LayoutText(pill, "Label", "NEW", 17f, UIColors.PaperHi, bold: true, align: TextAlignmentOptions.Center), T, "hud.tab.new");
        tab.newBadge = pill.gameObject;
        pill.gameObject.SetActive(false);
    }

    // ------------------------------------------------------------------
    // Pause button (top-right) with the Esc keycap

    static void BuildPause(RectTransform desk, OfficeHud hud)
    {
        float escW = Mathf.Max(36f, 18f + TextWidth("Esc", 19f, true));
        Keycap(desk, "EscKeycap", 1814f - escW, 44f, "Esc", 19f, 36f, Anchor.TopRight);

        var button = IconButton(desk, "PauseButton", 1824f, 30f, 64, "pause", Anchor.TopRight, 30f, "b");
        hud.pauseButton = button;
    }

    // ------------------------------------------------------------------
    // Hiring-task clipboard (top-right): 1484, 112, 404 x 360 (grows if the text needs it)

    static void BuildTaskBoard(RectTransform desk)
    {
        var board = Node(desk, "TaskBoard", 1484f, 112f, 404f, 360f, Anchor.TopRight);
        PivotTop(board);
        var tb = board.gameObject.AddComponent<HudTaskBoard>();
        VLayout(board.gameObject, 0f, Pad(17, 17, 17, 17), TextAnchor.UpperLeft, true, true, true, false);
        Fit(board.gameObject, false, true);

        StretchShadow(board, 0f, 22f, 48f, 0.45f);
        var glow = Stretch(board, "UrgentGlow");
        IgnoreLayout(glow);
        StretchShadow(glow, 0f, 0f, 30f, 0.45f, UIColors.Loss, 8f);
        var ring = StretchBox(glow, "Ring", S("Kit/card_line5"), UIColors.WithAlpha(UIColors.Loss, 0.7f), -5f, -5f, -5f, -5f);
        IgnoreLayout(ring);
        glow.gameObject.SetActive(false);
        tb.urgentGlow = glow.gameObject;

        ShapeLayers(board, "card", UIColors.Clipboard, UIColors.Ink, 3);

        // Paper
        var paper = NewRect(board, "Paper");
        Layout(paper, -1f, 326f);   // at least the designed 360 px board; grows with the text
        var pf = StretchBox(paper, "Fill", S("Kit/tiny_fill"), UIColors.PaperHi);
        IgnoreLayout(pf);
        var rules = StretchBox(paper, "Rules", S("Kit/Tiles/rule35"), Color.white, 2f, 2f, 2f, 2f);
        rules.type = Image.Type.Tiled;
        IgnoreLayout(rules);
        var pb = StretchBox(paper, "Border", S("Kit/tiny_line2"), UIColors.Ink);
        IgnoreLayout(pb);
        VLayout(paper.gameObject, 0f, Pad(22, 22, 36, 16), TextAnchor.UpperLeft, true, true, true, false);

        // --- active task
        var active = NewRect(paper, "Active");
        VLayout(active.gameObject, 10f, null, TextAnchor.UpperLeft, true, true, true, false);
        tb.activeContent = active.gameObject;

        var header = NewRect(active, "Header");
        Layout(header, -1f, 24f, -1f, 24f);
        tb.headerText = Text(header, "Label", 0f, 0f, 220f, 24f, "HIRING TASK", 17f, UIColors.Pencil, letterSpacingEm: 0.1f);
        var stars = Node(header, "Stars", 326f - 98f, 3f, 98f, 18f);
        tb.starFills = new Image[5];
        tb.starLines = new Image[5];
        for (int i = 0; i < 5; i++)
        {
            var f = Icon(stars, "StarFill" + i, i * 20f, 0f, 18f, "star", UIColors.Gold, "fill");
            var l = Icon(stars, "Star" + i, i * 20f, 0f, 18f, "star", i < 3 ? UIColors.Ink : UIColors.Pencil, "t");
            f.enabled = i < 3;
            tb.starFills[i] = f;
            tb.starLines[i] = l;
        }

        tb.titleText = LayoutText(active, "Title", "Big Brain Time", 30f, UIColors.Ink, bold: true, lineHeight: 1.15f, wrap: true);
        tb.descriptionText = LayoutText(active, "Description", "The strategy team needs reinforcements.", 19f, UIColors.InkSoft, lineHeight: 1.35f, wrap: true);

        var objective = NewRect(active, "Objective");
        HLayout(objective.gameObject, 10f, null, TextAnchor.MiddleLeft, true, true, false, false);
        var box = NewRect(objective, "Checkbox");
        AddImage(box.gameObject, S("Kit/r6_line3"), UIColors.Ink);
        Layout(box, 24f, 24f, 24f, 24f);
        tb.objectiveText = LayoutText(objective, "Text", "Hire someone with <link=\"hl\">IQ 120+</link>", 22f, UIColors.Ink, wrap: true);
        Layout(tb.objectiveText, -1f, -1f, -1f, -1f, 1f);
        tb.objectiveText.gameObject.AddComponent<TextHighlighter>();

        var timer = NewRect(active, "Timer");
        Layout(timer, -1f, 34f, -1f, 34f);
        HLayout(timer.gameObject, 10f, null, TextAnchor.MiddleLeft, true, true, false, false);
        tb.clockIcon = LayoutIcon(timer, "Clock", 30f, "stopwatch", UIColors.Ink);
        var time = LayoutText(timer, "Time", "0:47", 34f, UIColors.Ink, bold: true);
        Layout(time, -1f, 34f, -1f, 34f);
        tb.timerText = time;
        var timerBar = Bar(timer, "Bar", 100f, 16f, UIColors.Highlight, 0.52f, out var timerFill);
        Layout(timerBar, 40f, 16f, 100f, 16f, 1f);
        tb.timerBar = timerBar.gameObject;   // hidden while the task is on hold: "0:57 · on hold" takes the row
        tb.timerFill = timerFill;
        tb.timerFillImage = timerFill.GetComponent<Image>();

        var chips = NewRect(active, "Stakes");
        var flow = chips.gameObject.AddComponent<FlowLayoutGroup>();
        flow.spacingX = 10f;
        flow.spacingY = 8f;
        tb.rewardText = Chip(chips, "Reward", "+$5,000", "reward", "hud.board.chip.reward", UIColors.Money, UIColors.MoneySoft);
        tb.penaltyText = Chip(chips, "Penalty", UIFormat.Minus + "$2,000", "if missed", "hud.board.chip.missed", UIColors.Loss, UIColors.LossSoft);

        // --- nothing on the board
        var empty = NewRect(paper, "Empty");
        VLayout(empty.gameObject, 10f, null, TextAnchor.UpperLeft, true, true, true, false);
        Localize(LayoutText(empty, "Label", "HIRING TASK", 17f, UIColors.Pencil, letterSpacingEm: 0.1f), T, "hud.board.empty.label");
        Localize(LayoutText(empty, "Title", "Nothing on the board yet.", 28f, UIColors.Ink, bold: true, lineHeight: 1.2f, wrap: true),
            T, "hud.board.empty.title");
        Localize(LayoutText(empty, "Body", "Tasks show up while you hire. Beat the clock for a bonus, miss it and pay up.", 19f,
            UIColors.InkSoft, lineHeight: 1.4f, wrap: true), T, "hud.board.empty.body");
        var iconRow = NewRect(empty, "IconRow");
        Layout(iconRow, -1f, 80f, -1f, 80f);
        Icon(iconRow, "Clipboard", 127f, 8f, 72f, "clipboard", UIColors.Pencil, "t");
        tb.emptyContent = empty.gameObject;
        empty.gameObject.SetActive(false);

        // Clip at the top (over the board edge)
        var clip = Box(board, "Clip", 139f, -19f, 128f, 46f, S("Kit/Special/clip_metal"), Color.white);
        IgnoreLayout(clip);

        // Result stamps
        tb.stampComplete = Stamp(board, "StampComplete", "COMPLETE", "hud.board.stamp.complete", "+$5,000", UIColors.Money,
            out tb.stampCompleteAmount);
        tb.stampFailed = Stamp(board, "StampFailed", "FAILED", "hud.board.stamp.failed", UIFormat.Minus + "$2,000", UIColors.Loss,
            out tb.stampFailedAmount);
    }

    static void PivotTop(RectTransform rt)
    {
        Vector2 size = rt.sizeDelta;
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition += new Vector2(0f, size.y * 0.5f);
    }

    static TMP_Text Chip(Transform parent, string name, string amount, string note, string noteKey, Color amountColor, Color fill)
    {
        var chip = NewRect(parent, name);
        ShapeLayers(chip, "small", fill, UIColors.Ink, 2);
        HLayout(chip.gameObject, 6f, Pad(12, 12, 5, 5), TextAnchor.MiddleLeft, true, true, false, false);
        var a = LayoutText(chip, "Amount", amount, 22f, amountColor, bold: true);
        var n = Localize(LayoutText(chip, "Note", note, 17f, UIColors.Ink), T, noteKey);
        n.alignment = TextAlignmentOptions.BottomLeft;
        Layout(n, -1f, 28f, -1f, 28f);
        Layout(a, -1f, 28f, -1f, 28f);
        return a;
    }

    /// <summary>Rubber stamp over the clipboard: the word (a key) over the amount, as wide as the longer of the two.</summary>
    static GameObject Stamp(RectTransform board, string name, string word, string key, string amount, Color color, out TMP_Text amountText)
    {
        float w = Mathf.Max(TextWidth(word, 34f, true, 0.12f), TextWidth(amount, 28f, true)) + 40f + 10f;
        const float h = 98f;
        var stamp = NewRect(board, name);
        stamp.anchorMin = stamp.anchorMax = new Vector2(0.5f, 0.5f);
        stamp.pivot = new Vector2(0.5f, 0.5f);
        stamp.sizeDelta = new Vector2(w, h);
        stamp.anchoredPosition = new Vector2(0f, -6f);
        stamp.localEulerAngles = new Vector3(0f, 0f, 10f);
        IgnoreLayout(stamp);
        var group = stamp.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0.9f;
        group.blocksRaycasts = false;
        IgnoreLayout(StretchBox(stamp, "Wash", S("Kit/r10_fill"), UIColors.WithAlpha(UIColors.PaperHi, 0.6f)));
        IgnoreLayout(StretchBox(stamp, "Border", S("Kit/r10_line5"), color));
        IgnoreLayout(StretchBox(stamp, "Outline", S("Kit/r14_line2"), color, -6f, -6f, -6f, -6f));
        // 8 + 40 + 2 + 36 + 12 = 98 tall; the width follows the longer line (the word differs per language)
        VLayout(stamp.gameObject, 2f, Pad(25, 25, 8, 12), TextAnchor.UpperCenter, true, true, false, false);
        Fit(stamp.gameObject, true, false);
        var wordText = Localize(LayoutText(stamp, "Word", word, 34f, color, bold: true, align: TextAlignmentOptions.Center,
            letterSpacingEm: 0.12f), T, key);
        Layout(wordText, -1f, 40f, -1f, 40f);
        amountText = LayoutText(stamp, "Amount", amount, 28f, color, bold: true, align: TextAlignmentOptions.Center);
        Layout(amountText, -1f, 36f, -1f, 36f);
        stamp.gameObject.SetActive(false);
        return stamp.gameObject;
    }

    // ------------------------------------------------------------------
    // Applicant counter sticky (on the CV's top-right corner) and the golden-applicant sticky

    static void BuildApplicantSticky(RectTransform desk, OfficeHud hud)
    {
        var sticky = Sticky(desk, "ApplicantSticky", 1166f, 170f, 178f, 139f, 6f, UIColors.StickyYellow, Anchor.Top, Pad(18, 18, 30, 16), 4f);
        hud.applicantSticky = sticky.gameObject;

        var review = NewRect(sticky, "Review");
        VLayout(review.gameObject, 4f, null, TextAnchor.UpperLeft, true, true, true, false);
        Localize(LayoutText(review, "Label", "APPLICANT", 17f, UIColors.Pencil, letterSpacingEm: 0.08f), T, "hud.sticky.applicant.label");
        hud.applicantCountText = LayoutText(review, "Count", "1 of 3", 34f, UIColors.Ink, bold: true, lineHeight: 1.1f);
        var dots = NewRect(review, "Skips");
        HLayout(dots.gameObject, 8f, null, TextAnchor.MiddleLeft, true, true, false, false);
        Shrink(Localize(LayoutText(dots, "Label", "Skips left", 17f, UIColors.InkSoft), T, "hud.sticky.skips_left"), 13f);   // the dots follow it
        hud.skipDots = new Image[2];
        hud.skipRings = new Image[2];
        for (int i = 0; i < 2; i++)
        {
            var dot = NewRect(dots, "Dot" + i);
            Layout(dot, 14f, 14f, 14f, 14f);
            hud.skipDots[i] = AddImage(dot.gameObject, S("Kit/Circles/circle14_fill"), UIColors.Ink);
            var ringImg = StretchBox(dot, "Used", S("Kit/Circles/circle14_line2"), UIColors.Pencil);
            ringImg.enabled = false;
            hud.skipRings[i] = ringImg;
        }
        hud.applicantReview = review.gameObject;

        var done = NewRect(sticky, "Done");
        VLayout(done.gameObject, 4f, null, TextAnchor.UpperLeft, true, true, true, false);
        Localize(LayoutText(done, "Label", "BATCH", 17f, UIColors.Pencil, letterSpacingEm: 0.08f), T, "hud.sticky.batch.label");
        Localize(LayoutText(done, "Title", "Done!", 30f, UIColors.Ink, bold: true, lineHeight: 1.1f), T, "hud.sticky.batch.done");
        Localize(LayoutText(done, "Body", "Next batch whenever you're ready.", 17f, UIColors.InkSoft, wrap: true), T, "hud.sticky.batch.body");
        hud.applicantDone = done.gameObject;
        done.gameObject.SetActive(false);
    }

    static void BuildGoldenSticky(RectTransform desk, OfficeHud hud)
    {
        var sticky = Sticky(desk, "GoldenSticky", 468f, 548f, 204f, 190f, -5f, UIColors.StickyYellow, Anchor.Center, Pad(16, 16, 30, 16), 6f);
        var star = NewRect(sticky, "Star");
        Layout(star, 34f, 34f, 34f, 34f);
        var starRow = star;
        var sf = StretchBox(starRow, "Fill", IconSprite("star", "fill"), UIColors.Gold);
        sf.preserveAspect = true;
        var sl = StretchBox(starRow, "Line", IconSprite("star", "t"), UIColors.Ink);
        sl.preserveAspect = true;
        // keep the star at its size inside the stretching column
        var starLe = star.GetComponent<LayoutElement>();
        starLe.flexibleWidth = 0f;
        Localize(LayoutText(sticky, "Title", "Golden applicant!", 24f, UIColors.Ink, bold: true, lineHeight: 1.15f, wrap: true),
            T, "hud.sticky.golden.title");
        Localize(LayoutText(sticky, "Body", "1 in 100. Maybe don’t skip this one.", 19f, UIColors.InkSoft, lineHeight: 1.3f, wrap: true),
            T, "hud.sticky.golden.body");
        hud.goldenSticky = sticky.gameObject;
        sticky.gameObject.SetActive(false);
    }

    /// <summary>Sticky note: yellow paper, darker top band, soft shadow, slight rotation; grows with its content.</summary>
    static RectTransform Sticky(Transform parent, string name, float left, float top, float width, float height, float rotation,
        Color color, Anchor anchor, RectOffset padding, float spacing)
    {
        var sticky = Node(parent, name, left, top, width, height, anchor, rotation);
        PivotTop(sticky);
        VLayout(sticky.gameObject, spacing, padding, TextAnchor.UpperLeft, true, true, true, false);
        Fit(sticky.gameObject, false, true);
        StretchShadow(sticky, 0f, 12f, 20f, 0.28f);
        var fill = StretchBox(sticky, "Paper", null, color);
        IgnoreLayout(fill);
        var band = NewRect(sticky, "Band");
        band.anchorMin = new Vector2(0f, 1f);
        band.anchorMax = new Vector2(1f, 1f);
        band.pivot = new Vector2(0.5f, 1f);
        band.sizeDelta = new Vector2(0f, 20f);
        band.anchoredPosition = Vector2.zero;
        AddImage(band.gameObject, null, Black07);
        IgnoreLayout(band);
        return sticky;
    }

    // ------------------------------------------------------------------
    // Action bar (bottom centre): Skip + Hire while reviewing, Next batch when the batch is done

    static void BuildActionBar(RectTransform desk, OfficeHud hud)
    {
        float chipW = 10f + TextWidth("2 left", 17f) + 10f;
        float skipW = 30f + 32f + 12f + TextWidth("Skip", 32f) + 12f + chipW + 30f;
        float hireW = 48f + 38f + 14f + TextWidth("HIRE", 38f, true, 0.06f) + 48f;
        float total = skipW + 28f + hireW;
        float x0 = 960f - total * 0.5f;

        var review = Stretch(desk, "ReviewBar");
        hud.reviewBar = review.gameObject;
        // Skip + Hire stay a centred pair 28 px apart while each grows with its label (longer in some languages)
        var row = Node(review, "Buttons", 0f, 944f, 1920f, 88f, Anchor.Bottom);
        HLayout(row.gameObject, 28f, null, TextAnchor.MiddleCenter, false, false, false, false);

        var skip = MarkerButtonNode(row, "SkipButton", x0, 4f, skipW, 80f, Anchor.TopLeft, UIColors.PaperHi, new Vector2(4f, 5f),
            out var skipFace, out var skipContent);
        HLayout(skipContent.gameObject, 12f, Pad(30, 30, 0, 0), TextAnchor.MiddleCenter, true, true, false, false);
        var skipIcon = LayoutIcon(skipContent, "Icon", 32f, "trash", UIColors.Ink);
        var skipLabel = Localize(LayoutText(skipContent, "Label", "Skip", 32f, UIColors.Ink), T, "hud.bar.skip");
        var chip = NewRect(skipContent, "LeftChip");   // "2 left" pill, as wide as its text (OfficeHud writes it)
        Layout(chip, -1f, 30f, -1f, 30f);
        AddImage(chip.gameObject, S("Kit/Pills/pill30_fill"), UIColors.PaperShade);
        HLayout(chip.gameObject, 0f, Pad(10, 10, 0, 0), TextAnchor.MiddleCenter, true, true, false, false);
        var chipText = LayoutText(chip, "Text", "2 left", 17f, UIColors.InkSoft, align: TextAlignmentOptions.Center);
        hud.skipsLeftText = chipText;
        SetupMarker(skip, skipFace, new Graphic[] { skipIcon, skipLabel, chipText });
        FitWidth(skip, skipContent, skipW);
        hud.skipButton = skip.GetComponent<Button>();

        var hire = MarkerButtonNode(row, "HireButton", x0 + skipW + 28f, 0f, hireW, 88f, Anchor.TopLeft, UIColors.Money, new Vector2(5f, 6f),
            out var hireFace, out var hireContent);
        HLayout(hireContent.gameObject, 14f, Pad(48, 48, 0, 0), TextAnchor.MiddleCenter, true, true, false, false);
        var hireIcon = LayoutIcon(hireContent, "Icon", 38f, "stamp", UIColors.PaperHi);
        var hireLabel = Localize(LayoutText(hireContent, "Label", "HIRE", 38f, UIColors.PaperHi, bold: true, letterSpacingEm: 0.06f),
            T, "hud.bar.hire");
        SetupMarker(hire, hireFace, new Graphic[] { hireIcon, hireLabel });
        FitWidth(hire, hireContent, hireW);
        hud.hireButton = hire.GetComponent<Button>();

        float nextW = 48f + TextWidth("Next batch", 34f, true) + 14f + 36f + 48f;
        var next = MarkerButtonNode(desk, "NextBatchButton", 960f - nextW * 0.5f, 944f, nextW, 88f, Anchor.Bottom, UIColors.Highlight, new Vector2(5f, 6f),
            out var nextFace, out var nextContent);
        HLayout(nextContent.gameObject, 14f, Pad(48, 48, 0, 0), TextAnchor.MiddleCenter, true, true, false, false);
        var nextLabel = Localize(LayoutText(nextContent, "Label", "Next batch", 34f, UIColors.Ink, bold: true), T, "hud.bar.next_batch");
        var nextIcon = LayoutIcon(nextContent, "Icon", 36f, "arrow-right", UIColors.Ink, "b");
        SetupMarker(next, nextFace, new Graphic[] { nextLabel, nextIcon });
        FitWidth(next, nextContent, nextW);   // centred pivot: grows to both sides
        hud.nextBatchButton = next.GetComponent<Button>();
        next.gameObject.SetActive(false);
    }

    // ------------------------------------------------------------------
    // Toasts (bottom-right memo slips) and the tooltip slip

    static void BuildToasts(RectTransform root)
    {
        var layer = Stretch(root, "Toasts");
        var c = layer.gameObject.AddComponent<Canvas>();
        c.overrideSorting = true;
        c.sortingOrder = 900;   // above pop-ups, like the old fallback toasts
        var toasts = layer.gameObject.AddComponent<HudToasts>();

        var t = NewRect(layer, "ToastTemplate");
        t.anchorMin = t.anchorMax = new Vector2(1f, 0f);
        t.pivot = new Vector2(1f, 0f);
        t.sizeDelta = new Vector2(400f, 116f);
        t.anchoredPosition = new Vector2(-32f, 32f);
        t.localEulerAngles = new Vector3(0f, 0f, 1f);
        StretchShadow(t, 0f, 12f, 24f, 0.35f);
        StretchBox(t, "Fill", S("Kit/card_fill"), UIColors.PaperHi);
        StretchBox(t, "Border", S("Kit/card_line2"), UIColors.Ink);
        Box(t, "Tape", -22f, -12f, 84f, 26f, null, UIColors.Tape, Anchor.TopLeft, -28f);

        var view = t.gameObject.AddComponent<HudToastView>();
        var circle = Node(t, "IconCircle", 18f, 36f, 44f, 44f, Anchor.Left);
        view.iconCircle = AddImage(circle.gameObject, S("Kit/Circles/circle44_fill"), UIColors.StickyYellow);
        StretchBox(circle, "Ring", S("Kit/Circles/circle44_line2"), UIColors.Ink);
        view.icon = Icon(circle, "Icon", 9f, 9f, 26f, "sparkle", UIColors.Ink);

        var msg = Stretch(t, "Message", 78f, 14f, 20f, 14f);
        view.message = SetupText(msg.gameObject, "Skill point earned! 2 points to spend in the Skills menu.", 22f, UIColors.Ink,
            lineHeight: 1.3f, wrap: true);

        var chip = NewRect(t, "KindChip");
        chip.anchorMin = chip.anchorMax = new Vector2(1f, 1f);
        chip.pivot = new Vector2(1f, 1f);
        chip.anchoredPosition = new Vector2(10f, 15f);
        chip.sizeDelta = new Vector2(100f, 28f);
        HLayout(chip.gameObject, 0f, Pad(10, 10, 0, 0), TextAnchor.MiddleCenter, true, true, false, true);
        Fit(chip.gameObject, true, false);
        var chipFill = StretchBox(chip, "Fill", S("Kit/small_fill"), UIColors.StickyYellow);
        IgnoreLayout(chipFill);
        var chipBorder = StretchBox(chip, "Border", S("Kit/small_line2"), UIColors.Ink);
        IgnoreLayout(chipBorder);
        view.kindLabel = LayoutText(chip, "Kind", "SPECIAL", 17f, UIColors.Ink, bold: true, align: TextAlignmentOptions.Center, letterSpacingEm: 0.08f);
        view.kindChip = chipFill;

        toasts.template = t;
        toasts.infoIcon = IconSprite("bell");
        toasts.goodIcon = IconSprite("check");
        toasts.badIcon = IconSprite("cross");
        toasts.specialIcon = IconSprite("sparkle");
        t.gameObject.SetActive(false);
    }

    static void BuildTooltip(RectTransform root, HudTooltip tooltip)
    {
        var slip = NewRect(root, "TooltipSlip");
        slip.anchorMin = slip.anchorMax = new Vector2(0.5f, 0.5f);
        slip.pivot = new Vector2(0f, 1f);
        slip.sizeDelta = new Vector2(340f, 80f);
        StretchBox(slip, "Shadow", S("Kit/small_fill"), UIColors.WithAlpha(UIColors.Ink, 0.9f), 3f, 4f, -3f, -4f);
        StretchBox(slip, "Fill", S("Kit/small_fill"), UIColors.PaperHi);
        StretchBox(slip, "Border", S("Kit/small_line2"), UIColors.Ink);
        var label = Stretch(slip, "Label", 16f, 12f, 16f, 12f);
        tooltip.label = SetupText(label.gameObject, "<b>Stocks</b> — unlock with Stock Market Access (Growth, tier 3)", 19f, UIColors.Ink,
            lineHeight: 1.4f, wrap: true);
        var pointer = NewRect(slip, "Pointer");
        pointer.anchorMin = pointer.anchorMax = new Vector2(0f, 1f);
        pointer.pivot = new Vector2(0.5f, 0f);
        pointer.sizeDelta = new Vector2(24f, 14f);
        pointer.anchoredPosition = new Vector2(91f, -2f);
        AddImage(pointer.gameObject, S("Kit/Special/tip_pointer_up"), Color.white);
        tooltip.slip = slip;
        tooltip.pointer = pointer;
        SetAllRaycast(slip.gameObject, false);
        slip.gameObject.SetActive(false);
    }

    // ------------------------------------------------------------------
    // Small helpers for texts that are longer in some languages

    /// <summary>A fixed box's text shrinks (down to min) instead of overflowing when a language needs more room.</summary>
    static TText Shrink<TText>(TText text, float min) where TText : TMP_Text
    {
        text.enableAutoSizing = true;
        text.fontSizeMin = min;
        text.fontSizeMax = text.fontSize;
        return text;
    }

    /// <summary>The button keeps its designed width and grows with its content row (a longer language): HudFitWidth.</summary>
    static void FitWidth(RectTransform button, RectTransform content, float designWidth)
    {
        var fit = button.gameObject.AddComponent<HudFitWidth>();
        fit.content = content;
        fit.minWidth = designWidth;
    }

    /// <summary>Pivot on the right edge (same place on screen), so a width change grows the rect leftwards.</summary>
    static void PivotRight(RectTransform rt)
    {
        rt.pivot = new Vector2(1f, rt.pivot.y);
        rt.anchoredPosition += new Vector2(rt.sizeDelta.x * 0.5f, 0f);
    }

    /// <summary>Pivot on the left edge (same place on screen), so a width change grows the rect rightwards.</summary>
    static void PivotLeft(RectTransform rt)
    {
        rt.pivot = new Vector2(0f, rt.pivot.y);
        rt.anchoredPosition -= new Vector2(rt.sizeDelta.x * 0.5f, 0f);
    }

    // ------------------------------------------------------------------
    // Scene install / restore

    public static void InstallInMainGame(GameObject prefab, List<string> log)
    {
        Scene scene = OpenMainGame();
        if (!scene.IsValid())
        {
            log.Add("MainGame scene not found at " + ScenePath);
            return;
        }

        OfficeHud hud = FindInScene<OfficeHud>(scene);
        if (hud == null)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.name = "OfficeHUD";
            hud = instance.GetComponent<OfficeHud>();
            log.Add("MainGame: added the OfficeHUD prefab.");
        }
        else
        {
            if (!hud.gameObject.activeSelf) hud.gameObject.SetActive(true);
            log.Add("MainGame: OfficeHUD already there (the prefab update applies to it).");
        }

        var flow = FindInScene<GameFlowManager>(scene);
        if (flow != null)
        {
            SetRef(flow, "skipButton", hud.skipButton);
            SetRef(flow, "hireButton", hud.hireButton);
            SetRef(flow, "nextBatchButton", hud.nextBatchButton);
            log.Add("MainGame: GameFlowManager Skip / Hire / Next batch -> OfficeHUD buttons.");
        }

        var tasks = FindInScene<HiringTaskManager>(scene);
        if (tasks != null)
        {
            foreach (string field in new[] { "taskPanel", "titleText", "descriptionText", "requirementText", "timerText",
                                             "rewardText", "penaltyText", "timerFill", "successEffect", "failEffect" })
                SetRef(tasks, field, null);
            log.Add("MainGame: HiringTaskManager's old UI fields cleared (the new clipboard reads the manager).");
        }

        var buildCam = FindInScene<BuildModeCamera>(scene);
        if (buildCam != null)
        {
            var so = new SerializedObject(buildCam);
            var list = so.FindProperty("uiToHideInBuildMode");
            var keep = new List<Object>();
            for (int i = 0; i < list.arraySize; i++)
            {
                var o = list.GetArrayElementAtIndex(i).objectReferenceValue;
                if (o != null && !IsOldHud(o as GameObject) && o != hud.deskLayer) keep.Add(o);
            }
            keep.Add(hud.deskLayer);
            list.arraySize = keep.Count;
            for (int i = 0; i < keep.Count; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = keep[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            log.Add("MainGame: build mode hides the OfficeHUD desk layer.");
        }

        int off = 0;
        foreach (string path in OldHudPaths)
        {
            var go = FindByPath(scene, path);
            if (go != null && go.activeSelf)
            {
                go.SetActive(false);
                off++;
            }
        }
        log.Add("MainGame: switched off " + off + " old HUD objects (kept in the scene for Restore Old HUD).");

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        log.Add("MainGame saved.");
    }

    [MenuItem("Tools/Game/New UI/Restore Old HUD")]
    public static void RestoreOldHud()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Scene scene = OpenMainGame();
        if (!scene.IsValid()) return;

        foreach (string path in OldHudPaths)
        {
            var go = FindByPath(scene, path);
            if (go != null) go.SetActive(true);
        }
        var flow = FindInScene<GameFlowManager>(scene);
        if (flow != null)
        {
            SetRef(flow, "skipButton", FindByPath(scene, "Canvas/Skip")?.GetComponent<Button>());
            SetRef(flow, "hireButton", FindByPath(scene, "Canvas/Hire")?.GetComponent<Button>());
            SetRef(flow, "nextBatchButton", FindByPath(scene, "Canvas/NewBatch")?.GetComponent<Button>());
        }
        var tasks = FindInScene<HiringTaskManager>(scene);
        if (tasks != null)
        {
            SetRef(tasks, "taskPanel", FindByPath(scene, "Canvas/HiringTaskPanel"));
            SetRef(tasks, "titleText", Tmp(scene, "Canvas/HiringTaskPanel/TitleRow/Value"));
            SetRef(tasks, "descriptionText", Tmp(scene, "Canvas/HiringTaskPanel/Description"));
            SetRef(tasks, "requirementText", Tmp(scene, "Canvas/HiringTaskPanel/Requirement"));
            SetRef(tasks, "timerText", Tmp(scene, "Canvas/HiringTaskPanel/TimerRow/TimerText"));
            SetRef(tasks, "rewardText", Tmp(scene, "Canvas/HiringTaskPanel/RewardRow/Value"));
            SetRef(tasks, "penaltyText", Tmp(scene, "Canvas/HiringTaskPanel/PenaltyRow/Value"));
            SetRef(tasks, "timerFill", FindByPath(scene, "Canvas/HiringTaskPanel/TimerRow/TimerBackground/TimerFill")?.GetComponent<Image>());
            SetRef(tasks, "successEffect", FindByPath(scene, "Canvas/HiringTaskPanel/SuccessEffect"));
            SetRef(tasks, "failEffect", FindByPath(scene, "Canvas/HiringTaskPanel/FailEffect"));
        }
        var hud = FindInScene<OfficeHud>(scene);
        var buildCam = FindInScene<BuildModeCamera>(scene);
        if (buildCam != null)
        {
            var so = new SerializedObject(buildCam);
            var list = so.FindProperty("uiToHideInBuildMode");
            var keep = new List<Object>();
            for (int i = 0; i < list.arraySize; i++)
            {
                var o = list.GetArrayElementAtIndex(i).objectReferenceValue;
                if (o != null && (hud == null || o != hud.deskLayer)) keep.Add(o);
            }
            var stats = FindByPath(scene, "Canvas/CompanyStatsPanel");
            if (stats != null && !keep.Contains(stats)) keep.Add(stats);
            list.arraySize = keep.Count;
            for (int i = 0; i < keep.Count; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = keep[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        if (hud != null) hud.gameObject.SetActive(false);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[OfficeHudBuilder] Old HUD restored in MainGame (OfficeHUD switched off).");
    }

    static TMP_Text Tmp(Scene scene, string path)
    {
        var go = FindByPath(scene, path);
        return go != null ? go.GetComponent<TMP_Text>() : null;
    }

    static bool IsOldHud(GameObject go)
    {
        if (go == null) return false;
        string p = PathOf(go);
        foreach (string old in OldHudPaths)
            if (p == old) return true;
        return false;
    }

    static Scene OpenMainGame()
    {
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        if (scene.IsValid() && scene.isLoaded) return scene;
        if (!File.Exists(ScenePath)) return default;
        return EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
    }

    static void SetRef(Object target, string field, Object value)
    {
        var so = new SerializedObject(target);
        var p = so.FindProperty(field);
        if (p == null)
        {
            Debug.LogWarning("[OfficeHudBuilder] " + target.GetType().Name + " has no field " + field);
            return;
        }
        p.objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static T FindInScene<T>(Scene scene) where T : Component
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            var c = root.GetComponentInChildren<T>(true);
            if (c != null) return c;
        }
        return null;
    }

    static GameObject FindByPath(Scene scene, string path)
    {
        string[] parts = path.Split('/');
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.name != parts[0]) continue;
            Transform t = root.transform;
            for (int i = 1; i < parts.Length && t != null; i++)
                t = FindChild(t, parts[i]);
            if (t != null) return t.gameObject;
        }
        return null;
    }

    static Transform FindChild(Transform parent, string name)
    {
        for (int i = 0; i < parent.childCount; i++)
            if (parent.GetChild(i).name == name) return parent.GetChild(i);
        return null;
    }

    static string PathOf(GameObject go)
    {
        string p = go.name;
        Transform t = go.transform.parent;
        while (t != null)
        {
            p = t.name + "/" + p;
            t = t.parent;
        }
        return p;
    }
}
