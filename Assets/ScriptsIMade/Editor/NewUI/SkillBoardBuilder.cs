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
/// Builds the designed skill board (canvas boards "Skill board: Growth / Productivity / Chaos & Luck") into
/// Assets/UI/Prefabs/SkillBoard.prefab and adds it to MainGame; OfficeHud's Skills tab opens it (SkillBoardView).
/// Board children use cork-inner coordinates (1692 x 936) like the design. Fixed labels are bound to the "SkillBoard"
/// table (Localize); SkillBoardView writes the rest in the current language.
/// </summary>
public static class SkillBoardBuilder
{
    public const string PrefabPath = "Assets/UI/Prefabs/SkillBoard.prefab";
    const string Table = SkillText.Table;

    static readonly Color Wood = UIColors.Hex(0x8B5E3C);
    static readonly Color YellowRing = UIColors.Highlight;

    // icons per skill, from the design boards
    static readonly string[] IconTable =
    {
        "Investor Confidence=trend-up", "Salary Negotiation=percent", "Budget Optimization=trend-up", "Employee Retention Bonus=trend-up",
        "Operating Efficiency=trend-up", "Public Relations=shield", "Tax Optimization=percent", "Increased Revenue=trend-up",
        "Overhead Reduction=trend-up", "Salary Optimization=percent", "Stock Market Access=chart", "Corporate Savings Account=coin",
        "Investor Injection=coin", "Financial Leverage=trend-up", "Investment Returns=coin", "Golden Parachute=shield",
        "Merger Mentality=briefcase", "Stock Market Expertise=chart", "Asset Appreciation=building", "Advanced Tax Strategy=percent",
        "Market Manipulation=chart", "Market Dominance=trend-up", "Public Valuation Boost=trend-up", "Expansion Bonus=building",
        "Growth Through Scale=building", "Premium Positioning=star", "Efficient Capital=coin", "Financial Safety Net=shield",
        "Scalable Operations=building", "Premium Talent Attraction=star",
        "Office Efficiency=trend-up", "Onboarding Program=person", "Workspace Optimization=monitor", "Flexible Hours=stopwatch",
        "Work From Home=home", "Skill Development=bulb", "Quality Control=shield", "Mentorship Program=people",
        "Performance Reviews=clipboard", "Skill Certification=seal", "IQ Synergy=bulb", "PhD Appreciation=seal",
        "Lean Management=trend-up", "Six Sigma=target", "Agile Methodology=refresh", "R&D Laboratory=bulb", "Team Synergy=people",
        "Time Tracking=stopwatch", "Project Management=clipboard", "Collaboration Tools=monitor", "Automation Initiative=sliders",
        "AI Assistant=monitor", "Communication Boost=envelope", "Stress Reduction=smile", "Employee Retention=people",
        "Talent Scouting=search", "Headhunter Bonus=target", "Recruiting Network=globe", "Employee Referral=person", "Culture Fit=smile",
        "Beginner's Luck=sparkle", "Trash to Treasure=trash", "Lucky Charm=star", "Fortune Cookie=envelope", "Lottery Ticket=tag",
        "Lucky Hire=star", "Salary Roulette=percent", "Unlucky Protection=shield", "Coin Flip=coin", "Mystery Box=help",
        "Corporate Roulette=refresh", "Critical Success=target", "Random Raise=money", "Dice Roll=dice", "Tarot Card=cards",
        "Chaotic Energy=fire", "Gambler's Fallacy=scales", "Surprise Bonus=sparkle", "Lucky Streak=trend-up", "Jinx Removal=shield",
        "Casino License=dice", "Wild Card=cards", "Double or Nothing=coin", "Jackpot Chance=trophy", "Serendipity=star",
        "Karma Bonus=smile", "Reverse Karma=refresh", "Probability Manipulation=percent", "Reality Warp=globe", "Quantum Luck=sparkle",
        "Corporate Enlightenment=sparkle",
    };

    [MenuItem("Tools/Game/New UI/Build Skill Board")]
    public static void BuildFromMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Build Skill Board", "Stop Play mode first.", "OK");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var log = new List<string>();
        var prefab = BuildPrefab(log);
        if (prefab != null) Install(prefab, log);
        Debug.Log("[SkillBoardBuilder]\n" + string.Join("\n", log));
        EditorUtility.DisplayDialog("Skill board", string.Join("\n", log), "OK");
    }

    public static GameObject BuildPrefab(List<string> log)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
        GameObject root = null;
        try
        {
            root = Build();
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool ok);
            log.Add(ok ? "Saved " + PrefabPath : "Could not save " + PrefabPath);
            return ok ? prefab : null;
        }
        finally
        {
            if (root != null) Object.DestroyImmediate(root);
        }
    }

    static GameObject Build()
    {
        var root = new GameObject("SkillBoard", typeof(RectTransform));
        root.layer = 5;
        var rt = (RectTransform)root.transform;
        rt.sizeDelta = new Vector2(1920f, 1080f);
        var view = root.AddComponent<SkillBoardView>();

        var page = Stretch(rt, "Page");
        view.page = page.gameObject;
        var scrim = StretchBox(page, "Scrim", null, UIColors.ScrimLight);
        scrim.raycastTarget = true;

        // board 1728 x 972 at 96, 54: 3 px ink ring, drop shadow, 18 px wood frame
        Shadow(page, "BoardShadow", 96f, 54f, 1728f, 972f, 0f, 22f, 48f, 0.45f, Anchor.Center);
        Box(page, "InkRing", 93f, 51f, 1734f, 978f, S("Kit/panel_fill"), UIColors.Ink, Anchor.Center);
        var board = Node(page, "Board", 96f, 54f, 1728f, 972f, Anchor.Center);
        AddImage(board.gameObject, S("Kit/panel_fill"), Wood);
        var inner = Stretch(board, "Inner", 18f, 18f, 18f, 18f);
        var cork = StretchBox(inner, "Cork", S("Kit/Special/cork_board"), Color.white);
        cork.type = Image.Type.Simple;

        view.innerSize = new Vector2(1692f, 936f);
        view.gridOrigin = new Vector2(104f, 146f);
        view.cardPitch = new Vector2(224f, 114f);
        view.cardSize = new Vector2(204f, 94f);
        view.arrowEndX = 1212f;

        // red strings behind the cards
        view.stringShadow = Polyline(inner, "StringShadow", UIColors.WithAlpha(Color.black, UIColors.SrgbAlpha(0.25f)), 3f);
        view.strings = Polyline(inner, "Strings", UIColors.Loss, 3f);

        HeaderStrip(inner);
        Box(inner, "TabLine", 104f, 118f, 1100f, 3f, null, UIColors.Ink);
        BuildTabs(inner, view);
        Sticky(inner, view);
        TierLabels(inner, view);

        var grid = TopLeft(inner, "Cards", 0f, 0f, 1692f, 936f);
        for (int i = 0; i < 30; i++)
            view.cards[i] = Card(grid, i);

        view.arrowUnder = Polyline(inner, "ArrowUnder", UIColors.WithAlpha(UIColors.PaperHi, 0.9f), 10f);
        view.arrowInk = Polyline(inner, "ArrowInk", UIColors.Ink, 4f);

        Folder(inner, view);
        Envelope(inner, view);
        Legend(inner);

        view.closeButton = IconButton(page, "CloseButton", 1752f, 28f, 60, "cross", Anchor.Center, 28f);

        // sprites and table data the view needs at runtime
        view.branchColors = new[] { UIColors.StickyGreen, UIColors.StickyBlue, UIColors.StickyPink };
        view.branchIcons = new[] { IconSprite("trend-up"), IconSprite("stopwatch"), IconSprite("dice") };
        view.sparkleIcon = IconSprite("sparkle");
        view.lockIcon = IconSprite("lock");
        view.checkIcon = IconSprite("check", "b");
        view.fallbackIcon = IconSprite("sparkle");
        var icons = new List<SkillBoardView.NamedIcon>();
        foreach (string e in IconTable)
        {
            int eq = e.IndexOf('=');
            icons.Add(new SkillBoardView.NamedIcon { name = e.Substring(0, eq), icon = IconSprite(e.Substring(eq + 1)) });
        }
        view.skillIcons = icons.ToArray();

        page.gameObject.SetActive(false);

        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 90;
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        root.AddComponent<GraphicRaycaster>();
        return root;
    }

    // ------------------------------------------------------------------
    // helpers

    static RectTransform TopLeft(Transform parent, string name, float left, float top, float width = 0f, float height = 0f)
    {
        var rt = NewRect(parent, name);
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.sizeDelta = new Vector2(width, height);
        rt.anchoredPosition = new Vector2(left, -top);
        return rt;
    }

    static UIPolyline Polyline(RectTransform parent, string name, Color color, float width)
    {
        var rt = Stretch(parent, name);
        var line = rt.gameObject.AddComponent<UIPolyline>();
        line.color = color;
        line.width = width;
        line.raycastTarget = false;
        return line;
    }

    static Color Hard(Color c, float cssAlpha) => UIColors.WithAlpha(c, UIColors.SrgbAlpha(cssAlpha));

    /// <summary>A one-line text that shrinks (down to <paramref name="min"/>) when a translation is wider than its box.</summary>
    static T ShrinkToFit<T>(T text, float min) where T : TMP_Text
    {
        text.enableAutoSizing = true;
        text.fontSizeMin = min;
        text.fontSizeMax = text.fontSize;
        return text;
    }

    /// <summary>Red push pin (sprite with its shadow baked in) whose 22 px head sits at left/top.</summary>
    static void Pin(Transform parent, float left, float top)
    {
        var img = Box(parent, "Pin", left - 5f, top - 4f, 32f, 34f, S("Kit/Special/pin_red"), Color.white);
        img.type = Image.Type.Simple;
    }

    /// <summary>A rubber-stamp word (auto width, the table entry <paramref name="key"/>) centred at cx, cy; returns the stamp root.</summary>
    static RectTransform Stamp(Transform parent, string name, float cx, float cy, string word, string key, float size, Color color,
        float rotation, float alpha, string lineSprite, string outlineSprite, float outlineGrow, RectOffset pad, Color? background = null)
    {
        var st = NewRect(parent, name);
        st.anchorMin = st.anchorMax = new Vector2(0f, 1f);
        st.pivot = new Vector2(0.5f, 0.5f);
        st.anchoredPosition = new Vector2(cx, -cy);
        st.localEulerAngles = new Vector3(0f, 0f, -rotation);
        HLayout(st.gameObject, 0f, pad, TextAnchor.MiddleCenter, true, true, false, false);
        Fit(st.gameObject, true, true);
        var group = st.gameObject.AddComponent<CanvasGroup>();
        group.alpha = alpha;
        group.blocksRaycasts = false;
        if (background.HasValue) IgnoreLayout(StretchBox(st, "Paper", S(lineSprite.Replace("_line3", "_fill").Replace("_line5", "_fill")), background.Value));
        IgnoreLayout(StretchBox(st, "Border", S(lineSprite), color));
        IgnoreLayout(StretchBox(st, "Outline", S(outlineSprite), color, -outlineGrow, -outlineGrow, -outlineGrow, -outlineGrow));
        Localize(LayoutText(st, "Word", word, size, color, bold: true, letterSpacingEm: 0.12f, lineHeight: 1.15f), Table, key);
        return st;
    }

    // ------------------------------------------------------------------
    // header, tabs, sticky, tiers

    static void HeaderStrip(RectTransform inner)
    {
        Shadow(inner, "HeaderShadow", 20f, 14f, 500f, 88f, 0f, 10f, 18f, 0.30f, Anchor.TopLeft, -1f);
        var strip = Shape(inner, "Header", 20f, 14f, 500f, 88f, "card", UIColors.PaperHi, UIColors.Ink, 3, Anchor.TopLeft, -1f);
        Icon(strip, "Icon", 27f, 22f, 44f, "sparkle", UIColors.Ink);
        ShrinkToFit(Localize(Text(strip, "Title", 85f, 9.5f, 400f, 69f, "Skill board", 60f, UIColors.Ink, bold: true), Table,
            "skillboard.header.title"), 40f);
        Pin(strip, 239f, -9f);
    }

    static void BuildTabs(RectTransform inner, SkillBoardView view)
    {
        string[] names = { "Growth", "Productivity", "Chaos & Luck" };
        string[] keys = { "skillboard.branch.growth", "skillboard.branch.productivity", "skillboard.branch.chaos" };
        string[] icons = { "trend-up", "stopwatch", "dice" };
        Color[] colors = { UIColors.StickyGreen, UIColors.StickyBlue, UIColors.StickyPink };
        for (int i = 0; i < 3; i++)
        {
            float x = 540f + i * 226f;
            var node = Node(inner, names[i].Replace(" & ", "") + "Tab", x, 41f, 212f, 80f);
            var hit = AddImage(node.gameObject, null, Clear, true);
            var button = node.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = hit;
            var tab = new SkillBoardView.BranchTab { button = button };
            tab.activeLook = TabLook(node, "Active", 0f, 80f, colors[i], icons[i], names[i], keys[i], true, out tab.activeCount).gameObject;
            tab.inactiveLook = TabLook(node, "Inactive", 7f, 70f, colors[i], icons[i], names[i], keys[i], false, out tab.inactiveCount).gameObject;
            tab.activeLook.SetActive(i == 0);
            tab.inactiveLook.SetActive(i != 0);
            view.tabs[i] = tab;
        }
    }

    static RectTransform TabLook(RectTransform node, string name, float top, float height, Color color, string icon, string label, string key,
        bool active, out TMP_Text count)
    {
        var look = Node(node, name, 0f, top, 212f, height);
        StretchBox(look, "Fill", S("Kit/foldertab_fill"), color);
        StretchBox(look, "Border", S("Kit/foldertab_line3_nobottom"), UIColors.Ink);
        float labelTop = active ? 18.15f : 13.3f, labelH = active ? 29.9f : 27.6f, size = active ? 26f : 24f;
        Icon(look, "Icon", 19f, labelTop + (labelH - 22f) * 0.5f, 22f, icon, UIColors.Ink);
        Localize(Text(look, "Label", 49f, labelTop, 160f, labelH, label, size, UIColors.Ink, bold: active), Table, key);
        count = Text(look, "Count", 19f, labelTop + labelH + 2f, 180f, 22.8f, "3 / 30 owned", 19f, UIColors.InkSoft);
        Pin(look, 95f, -9f);
        return look;
    }

    static void Sticky(RectTransform inner, SkillBoardView view)
    {
        Shadow(inner, "StickyShadow", 1224f, 10f, 400f, 146f, 0f, 12f, 20f, 0.28f, Anchor.TopLeft, 1.5f);
        var sticky = Box(inner, "Sticky", 1224f, 10f, 400f, 146f, null, UIColors.StickyYellow, Anchor.TopLeft, 1.5f).rectTransform;
        Box(sticky, "Band", 0f, 0f, 400f, 20f, null, UIColors.Dark(0.07f));

        var row = TopLeft(sticky, "SP", 20f, 22f, 200f, 54f);
        HLayout(row.gameObject, 10f, null, TextAnchor.MiddleLeft, true, true, false, false);
        Fit(row.gameObject, true, false);
        LayoutIcon(row, "Icon", 34f, "sparkle", UIColors.Ink);
        view.spText = LayoutText(row, "Points", "2", 54f, UIColors.Ink, bold: true, lineHeight: 1f);
        Localize(LayoutText(row, "Label", "SP to spend", 22f, UIColors.Ink, lineHeight: 1.15f), Table, "skillboard.sticky.sp_label");

        Localize(Text(sticky, "ValueLabel", 180f, 26.2f, 200f, 22.8f, "Company value", 19f, UIColors.Pencil, align: TextAlignmentOptions.Right),
            Table, "skillboard.sticky.company_value");
        view.companyValueText = Text(sticky, "Value", 180f, 49f, 200f, 22.8f, "$3,284,100", 19f, UIColors.Ink, align: TextAlignmentOptions.Right);

        var bar = Node(sticky, "Bar", 20f, 84f, 360f, 18f);
        AddImage(bar.gameObject, S("Kit/Bars/bar18_fill"), UIColors.PaperHi).gameObject.AddComponent<Mask>().showMaskGraphic = true;
        var fill = NewRect(bar, "Fill");
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = new Vector2(5f / 7f, 1f);
        fill.offsetMin = fill.offsetMax = Vector2.zero;
        AddImage(fill.gameObject, null, UIColors.Money);
        view.progressFill = fill;
        StretchBox(bar, "Border", S("Kit/Bars/bar18_line2"), UIColors.Ink);

        view.progressText = ShrinkToFit(Text(sticky, "Progress", 20f, 110f, 360f, 22.8f, "<b>5 / 7</b> high-IQ hires to the next SP", 19f,
            UIColors.Ink), 14f);
    }

    static void TierLabels(RectTransform inner, SkillBoardView view)
    {
        for (int n = 1; n <= 6; n++)
        {
            float top = 146f + (n - 1) * 114f + 17.4f;
            var chip = Node(inner, "Tier" + n, 20f, top, 72f, 59.2f);
            Box(chip, "Shadow", 2f, 3f, 72f, 59.2f, S("Kit/small_fill"), Hard(UIColors.Ink, 0.9f));
            StretchBox(chip, "Fill", S("Kit/small_fill"), UIColors.PaperHi);
            StretchBox(chip, "Border", S("Kit/small_line2"), UIColors.Ink);
            view.tierLabels[n - 1] = ShrinkToFit(Text(chip, "Tier", 0f, 8f, 72f, 22.8f, "Tier " + n, 19f, UIColors.Ink,
                align: TextAlignmentOptions.Center), 13f);
            view.tierCosts[n - 1] = Text(chip, "Cost", 0f, 30.8f, 72f, 20.4f, n + " SP", 17f, UIColors.InkSoft, align: TextAlignmentOptions.Center);
        }
    }

    // ------------------------------------------------------------------
    // cards: 204 x 94 at 104 + col * 224, 146 + tier * 114

    static SkillCardView Card(RectTransform grid, int slot)
    {
        int t = slot / 5, c = slot % 5;
        var node = Node(grid, "Card" + (t + 1) + "_" + (c + 1), 104f + c * 224f, 146f + t * 114f, 204f, 94f);
        var view = node.gameObject.AddComponent<SkillCardView>();
        var hit = AddImage(node.gameObject, null, Clear, true);
        view.button = node.gameObject.AddComponent<Button>();
        view.button.transition = Selectable.Transition.None;
        view.button.targetGraphic = hit;

        view.glowSmall = S("Shadows/shadow_s12");
        view.glowLarge = S("Shadows/shadow_s16");
        view.glowSmallPad = 36f;
        view.glowLargePad = 48f;
        view.glow = StretchBox(node, "Glow", view.glowSmall, UIColors.WithAlpha(YellowRing, 0.5f), -42f, -42f, -42f, -42f);
        view.glow.type = Image.Type.Sliced;
        view.outerRing = StretchBox(node, "Ring", S("Kit/card_fill"), YellowRing, -4f, -4f, -4f, -4f);
        view.inkRing = StretchBox(node, "InkRing", S("Kit/card_fill"), UIColors.Ink, -2f, -2f, -2f, -2f).gameObject;
        view.hardShadow = StretchBox(node, "Shadow", S("Kit/card_fill"), Hard(UIColors.Ink, 0.9f), 3f, 4f, -3f, -4f).gameObject;
        view.fill = StretchBox(node, "Fill", S("Kit/card_fill"), UIColors.PaperHi);
        var rules = StretchBox(node, "Rules", S("Kit/Special/card_owned_rules"), Color.white);
        rules.type = Image.Type.Simple;
        view.rules = rules.gameObject;
        view.solidBorder = StretchBox(node, "Border", S("Kit/card_line2"), UIColors.Ink).gameObject;
        view.dashedBorder = Dashed(StretchBox(node, "Dashed", S("Kit/card_dash3"), UIColors.Pencil)).gameObject;

        // padding 14 10 8 12 inside the 2 px border; name lines 1.12 (glyphs centred on each line, as in CSS)
        view.nameText = Text(node, "Name", 14f, 12.2f, 148f, 50f, "Investor Confidence", 19f, UIColors.Ink,
            align: TextAlignmentOptions.TopLeft, lineHeight: 1.12f, wrap: true);
        view.icon = Icon(node, "Icon", 168f, 16f, 24f, "trend-up", UIColors.Ink);
        view.effectText = ShrinkToFit(Text(node, "Effect", 14f, 63.6f, 182f, 20.4f, "+10% daily profit", 17f, UIColors.InkSoft), 12f);

        Pin(node, 91f, -9f);

        // owned: green check badge (2 px paper edge + 2 px ink ring)
        var owned = Node(node, "Owned", 184f, -14f, 34f, 34f);
        AddImage(owned.gameObject, S("Kit/Circles/circle34_fill"), UIColors.Ink);
        Box(owned, "Green", 2f, 2f, 30f, 30f, S("Kit/Circles/circle30_fill"), UIColors.Money);
        Box(owned, "Edge", 2f, 2f, 30f, 30f, S("Kit/Circles/circle30_line2"), UIColors.PaperHi);
        Icon(owned, "Check", 8f, 8f, 18f, "check", UIColors.PaperHi, "b");
        view.ownedBadge = owned.gameObject;

        // available: price chip
        var chip = NewRect(node, "Cost");
        chip.anchorMin = chip.anchorMax = new Vector2(0f, 1f);
        chip.pivot = new Vector2(1f, 1f);
        chip.anchoredPosition = new Vector2(214f, 13f);
        chip.sizeDelta = new Vector2(70f, 28f);
        HLayout(chip.gameObject, 4f, Pad(12, 12, 0, 0), TextAnchor.MiddleCenter, true, false, false, false);
        Fit(chip.gameObject, true, false);
        IgnoreLayout(StretchBox(chip, "Shadow", S("Kit/small_fill"), UIColors.Ink, 2f, 2f, -2f, -2f));
        IgnoreLayout(StretchBox(chip, "Fill", S("Kit/small_fill"), UIColors.PaperHi));
        IgnoreLayout(StretchBox(chip, "Border", S("Kit/small_line2"), UIColors.Ink));
        LayoutIcon(chip, "Icon", 16f, "sparkle", UIColors.Ink);
        view.costText = LayoutText(chip, "Text", "2 SP", 17f, UIColors.Ink, bold: true);
        view.costText.rectTransform.sizeDelta = new Vector2(40f, 28f);
        view.costChip = chip.gameObject;

        // locked: lock badge
        var lockB = Node(node, "Locked", 186f, -12f, 30f, 30f);
        AddImage(lockB.gameObject, S("Kit/Circles/circle30_fill"), UIColors.PaperHi);
        Box(lockB, "Ring", 0f, 0f, 30f, 30f, S("Kit/Circles/circle30_line2"), UIColors.Pencil);
        Icon(lockB, "Lock", 6.5f, 6.5f, 17f, "lock", UIColors.Pencil);
        view.lockBadge = lockB.gameObject;

        // just unlocked: APPROVED
        float w = TextWidth("APPROVED", 26f, true, 0.12f) + 32f, h = 26f * 1.15f + 12f;
        var stamp = Stamp(node, "Approved", 18f + w * 0.5f, 26f + h * 0.5f, "APPROVED", "skillboard.card.approved", 26f, UIColors.Money,
            -12f, 0.92f, "Kit/r8_line3", "Kit/r12_line2", 5f, Pad(16, 16, 6, 6), UIColors.WithAlpha(UIColors.PaperHi, 0.6f));
        view.approvedStamp = stamp.gameObject;

        view.Show("Investor Confidence", "+10% daily profit", IconSprite("trend-up"), 1, SkillCardView.State.Owned, false, false);
        return view;
    }

    // ------------------------------------------------------------------
    // folder 444 x 720 at 1224, 196 (content 382 wide), tab at 1248, 160

    static void Folder(RectTransform inner, SkillBoardView view)
    {
        Box(inner, "FolderShadow", 1227f, 200f, 444f, 720f, S("Kit/panel_fill"), Hard(UIColors.Ink, 0.9f));
        var folder = Shape(inner, "Folder", 1224f, 196f, 444f, 720f, "panel", UIColors.Manila, UIColors.Ink, 3);
        var tab = Node(inner, "FolderTab", 1248f, 160f, 200f, 39f);
        StretchBox(tab, "Fill", S("Kit/foldertab_fill"), UIColors.Manila);
        StretchBox(tab, "Border", S("Kit/foldertab_line3_nobottom"), UIColors.Ink);
        Localize(Text(tab, "Label", 21f, 3f, 170f, 36f, "Selected skill", 19f, UIColors.Ink), Table, "skillboard.folder.tab");

        var col = VLayout(folder.gameObject, 0f, Pad(31, 31, 29, 29), TextAnchor.UpperLeft, true, true, true, false);

        // top: icon box + chips, then the title
        var top = Group(folder, "Top", 12f);
        var row = NewRect(top, "Row");
        HLayout(row.gameObject, 14f, null, TextAnchor.MiddleLeft, true, true, false, false);
        var box = NewRect(row, "IconBox");
        Layout(box, 60f, 60f, 60f, 60f);
        view.iconBox = AddImage(box.gameObject, S("Kit/card_fill"), UIColors.StickyGreen);
        StretchBox(box, "Border", S("Kit/card_line3"), UIColors.Ink);
        view.skillIcon = Icon(box, "Icon", 13f, 13f, 34f, "shield", UIColors.Ink);
        var chips = NewRect(row, "Chips");
        var flow = chips.gameObject.AddComponent<FlowLayoutGroup>();
        flow.spacingX = 8f;
        flow.spacingY = 8f;
        flow.childAlignment = TextAnchor.UpperLeft;
        Layout(chips, 200f, -1f, 308f, -1f, 1f, -1f);
        view.branchChip = Chip(chips, "Branch", "Growth", UIColors.StickyGreen, null, false, out view.branchChipFill);
        view.tierChip = Chip(chips, "Tier", "Tier 2", UIColors.PaperHi, null, false, out _);
        view.costChip = Chip(chips, "Cost", "2 SP", UIColors.Highlight, "sparkle", true, out _);
        view.title = LayoutText(top, "Title", "Public Relations", 44f, UIColors.Ink, bold: true, lineHeight: 1.15f, wrap: true);

        Spacer(folder);

        var mid = Group(folder, "Effect", 8f);
        view.effect = LayoutText(mid, "Text", "Negative events hurt <link=\"hl\">20% less.</link>", 26f, UIColors.Ink, lineHeight: 1.4f, wrap: true);
        view.effect.gameObject.AddComponent<TextHighlighter>();
        view.example = LayoutText(mid, "Example", "e.g. a −$25,000 hit becomes −$20,000", 19f, UIColors.InkSoft, lineHeight: 1.4f, wrap: true);
        view.quote = LayoutText(mid, "Quote", "“No comment.”", 24f, UIColors.Pencil, lineHeight: 1.3f, wrap: true);
        view.quote.fontStyle = FontStyles.Italic;

        Spacer(folder);

        // requires / leads to
        var reqBox = NewRect(folder, "Links");
        VLayout(reqBox.gameObject, 12f, Pad(18, 18, 16, 16), TextAnchor.UpperLeft, true, true, true, false);
        IgnoreLayout(StretchBox(reqBox, "Fill", S("Kit/card_fill"), UIColors.PaperHi));
        IgnoreLayout(StretchBox(reqBox, "Border", S("Kit/card_line2"), UIColors.Ink));
        view.requires = LinkBlock(reqBox, "Requires", "Requires:", "skillboard.folder.requires");
        var rule = NewRect(reqBox, "Rule");
        Layout(rule, -1f, 2f, -1f, 2f);
        AddImage(rule.gameObject, S("Kit/Tiles/dash_h2"), UIColors.Rule).type = Image.Type.Tiled;
        view.leads = LinkBlock(reqBox, "Leads", "Leads to:", "skillboard.folder.leads");

        Spacer(folder);

        // action, hint, Back
        var bottom = NewRect(folder, "Actions");
        VLayout(bottom.gameObject, 8f, null, TextAnchor.UpperCenter, true, true, false, false);
        var slot = NewRect(bottom, "Slot");
        Layout(slot, 382f, 68f, 382f, 68f);
        var btn = MarkerButtonNode(slot, "UnlockButton", 0f, 0f, 382f, 68f, Anchor.TopLeft, UIColors.Highlight, new Vector2(5f, 6f),
            out var face, out var content);
        HLayout(content.gameObject, 12f, Pad(36, 36, 0, 0), TextAnchor.MiddleCenter, true, true, false, false);
        view.actionIcon = LayoutIcon(content, "Icon", 28f, "sparkle", UIColors.Ink);
        view.actionLabel = LayoutText(content, "Label", "Unlock for 2 SP", 28f, UIColors.Ink, bold: true);
        SetupMarker(btn, face, new Graphic[] { view.actionIcon, view.actionLabel });
        view.actionButton = btn.GetComponent<Button>();
        var stamp = Stamp(slot, "Unlocked", 191f, 34f, "UNLOCKED", "skillboard.folder.unlocked", 36f, UIColors.Money, -6f, 0.9f, "Kit/r10_line5",
            "Kit/r14_line2", 6f, Pad(27, 27, 9, 9));
        stamp.gameObject.SetActive(false);
        view.unlockedStamp = stamp.gameObject;

        view.hint = LayoutText(bottom, "Hint", "Unlock Lucky Hire, Corporate Roulette and Chaotic Energy first.", 19f, UIColors.InkSoft,
            align: TextAlignmentOptions.Center, lineHeight: 1.3f, wrap: true);
        Layout(view.hint, -1f, -1f, 382f, -1f);
        view.hint.gameObject.SetActive(false);

        // Back: an underlined word; the button takes the word's width in every language
        var back = NewRect(bottom, "BackButton");
        HLayout(back.gameObject, 0f, Pad(12, 12, 0, 0), TextAnchor.MiddleCenter, true, true, false, true);
        Layout(back, -1f, 44f, -1f, 44f);
        var backHit = AddImage(back.gameObject, null, Clear, true);
        var backButton = back.gameObject.AddComponent<Button>();
        backButton.transition = Selectable.Transition.None;
        backButton.targetGraphic = backHit;
        var backLabel = Localize(LayoutText(back, "Label", "Back", 22f, UIColors.Ink, align: TextAlignmentOptions.Center), Table,
            "skillboard.folder.back");
        var wave = NewRect(backLabel.rectTransform, "Wave");
        wave.anchorMin = Vector2.zero;
        wave.anchorMax = new Vector2(1f, 0f);
        wave.pivot = new Vector2(0.5f, 0f);
        wave.sizeDelta = new Vector2(0f, 6f);
        wave.anchoredPosition = new Vector2(0f, 9f);   // 29 px down the 44 px row, as designed
        AddImage(wave.gameObject, S("Kit/Tiles/wave_h2"), UIColors.Ink).type = Image.Type.Tiled;
        view.backButton = backButton;
    }

    static RectTransform Group(RectTransform parent, string name, float gap)
    {
        var g = NewRect(parent, name);
        VLayout(g.gameObject, gap, null, TextAnchor.UpperLeft, true, true, true, false);
        return g;
    }

    static void Spacer(RectTransform parent)
    {
        var s = NewRect(parent, "Spacer");
        Layout(s, -1f, 0f, -1f, 0f, -1f, 1f);
    }

    static TMP_Text Chip(RectTransform parent, string name, string text, Color fill, string icon, bool bold, out Image fillImage)
    {
        var chip = NewRect(parent, name);
        HLayout(chip.gameObject, 6f, Pad(14, 14, 6, 6), TextAnchor.MiddleLeft, true, true, false, false);
        fillImage = StretchBox(chip, "Fill", S("Kit/small_fill"), fill);
        IgnoreLayout(fillImage);
        IgnoreLayout(StretchBox(chip, "Border", S("Kit/small_line2"), UIColors.Ink));
        if (icon != null) LayoutIcon(chip, "Icon", 18f, icon, UIColors.Ink);
        return LayoutText(chip, "Text", text, 18f, UIColors.Ink, bold: bold);
    }

    static SkillBoardView.ReqRow LinkBlock(RectTransform parent, string name, string label, string key)
    {
        var block = Group(parent, name, 2f);
        Localize(LayoutText(block, "Label", label, 19f, UIColors.Pencil, lineHeight: 1.3f), Table, key);
        var rowRt = NewRect(block, "Row");
        var flow = rowRt.gameObject.AddComponent<FlowLayoutGroup>();
        flow.spacingX = 10f;
        flow.spacingY = 2f;
        flow.childAlignment = TextAnchor.MiddleLeft;
        var row = new SkillBoardView.ReqRow
        {
            names = new TMP_Text[3], statuses = new GameObject[3], statusIcons = new Image[3], statusTexts = new TMP_Text[3]
        };
        for (int i = 0; i < 3; i++)
        {
            row.names[i] = LayoutText(rowRt, "Name" + i, "Investor Confidence", 22f, UIColors.Ink, lineHeight: 1.3f);
            var st = NewRect(rowRt, "Status" + i);
            HLayout(st.gameObject, 4f, null, TextAnchor.MiddleLeft, true, true, false, false);
            row.statusIcons[i] = LayoutIcon(st, "Icon", 20f, "check", UIColors.Money, "b");
            row.statusTexts[i] = LayoutText(st, "Text", "owned", 19f, UIColors.Money, lineHeight: 1.3f);
            row.statuses[i] = st.gameObject;
            if (i > 0)
            {
                row.names[i].gameObject.SetActive(false);
                st.gameObject.SetActive(false);
            }
        }
        return row;
    }

    // ------------------------------------------------------------------
    // secret envelope (20, 826, 780 x 90) and legend (824, 826, 380 x 90)

    static void Envelope(RectTransform inner, SkillBoardView view)
    {
        var env = Node(inner, "Secret", 20f, 826f, 780f, 90f);
        var hit = AddImage(env.gameObject, null, Clear, true);
        view.envelope = env.gameObject.AddComponent<Button>();
        view.envelope.transition = Selectable.Transition.None;
        view.envelope.targetGraphic = hit;
        Box(env, "Shadow", 3f, 4f, 780f, 90f, S("Kit/card_fill"), Hard(UIColors.Ink, 0.9f));
        StretchBox(env, "Fill", S("Kit/card_fill"), UIColors.Manila);
        StretchBox(env, "Border", S("Kit/card_line3"), UIColors.Ink);
        Box(env, "Tape", -18f, -14f, 120f, 32f, null, UIColors.Tape, Anchor.TopLeft, -8f);

        float stampW = TextWidth("TOP SECRET", 22f, true, 0.12f) + 30f;
        Stamp(env, "TopSecret", 27f + stampW * 0.5f, 45f, "TOP SECRET", "skillboard.secret.stamp", 22f, UIColors.Loss, -6f, 0.9f, "Kit/r8_line3",
            "Kit/r12_line2", 5f, Pad(15, 15, 5, 5));
        // title + text, centred on the envelope whatever the text's line count
        float x = 27f + stampW + 20f, w = 697f - 20f - x;
        var words = Node(env, "Words", x, 3f, w, 84f);
        VLayout(words.gameObject, 2f, null, TextAnchor.MiddleLeft, true, true, true, false);
        view.envelopeTitle = LayoutText(words, "Title", "Corporate Enlightenment?", 24f, UIColors.Ink, bold: true, lineHeight: 1.15f);
        view.envelopeText = LayoutText(words, "Text", "Own every other skill on the board to break the seal. All effects +25%.", 19f,
            UIColors.Ink, lineHeight: 1.25f, wrap: true);

        var seal = Box(env, "Seal", 697f, 14f, 62f, 62f, S("Kit/Special/wax_seal"), Color.white);
        seal.type = Image.Type.Simple;
        view.sealIcon = Icon(seal.rectTransform, "Icon", 20f, 20f, 22f, "lock", UIColors.PaperHi);
    }

    static void Legend(RectTransform inner)
    {
        var card = Node(inner, "Legend", 824f, 826f, 380f, 90f);
        Box(card, "Shadow", 3f, 4f, 380f, 90f, S("Kit/card_fill"), Hard(UIColors.Ink, 0.9f));
        StretchBox(card, "Fill", S("Kit/card_fill"), UIColors.PaperHi);
        StretchBox(card, "Border", S("Kit/card_line2"), UIColors.Ink);

        Box(card, "StringSwatch", 18f, 28.5f, 26f, 3f, null, UIColors.Loss);
        Localize(Text(card, "StringText", 54f, 19.8f, 320f, 20.4f, "Red string: needs the card above it", 17f, UIColors.InkSoft), Table,
            "skillboard.legend.string");

        // swatch, 8, word, 8, swatch...: a row that lays the words out, so they may take any length
        var row = Node(card, "Key", 18f, 48.2f, 344f, 22f);
        HLayout(row.gameObject, 8f, null, TextAnchor.MiddleLeft, true, true, false, false);

        var owned = Swatch(row, "OwnedSwatch");
        AddImage(owned.gameObject, S("Kit/Circles/circle22_fill"), UIColors.Money);
        Box(owned, "Ring", 0f, 0f, 22f, 22f, S("Kit/Circles/circle22_line2"), UIColors.Ink);
        Icon(owned, "Check", 4f, 4f, 14f, "check", UIColors.PaperHi, "b");
        LegendWord(row, "Owned", "skillboard.legend.owned");

        var avail = Swatch(row, "AvailSwatch");
        Box(avail, "Ring", -3f, -3f, 28f, 28f, S("Kit/r8_fill"), YellowRing);
        Box(avail, "Fill", 0f, 0f, 22f, 22f, S("Kit/r6_fill"), UIColors.StickyYellow);
        Box(avail, "Border", 0f, 0f, 22f, 22f, S("Kit/r6_line2"), UIColors.Ink);
        LegendWord(row, "Available", "skillboard.legend.available");

        var locked = Swatch(row, "LockedSwatch");
        Box(locked, "Fill", 0f, 0f, 22f, 22f, S("Kit/r6_fill"), UIColors.PaperShade);
        Dashed(Box(locked, "Border", 0f, 0f, 22f, 22f, S("Kit/r6_dash2"), UIColors.Pencil));
        LegendWord(row, "Locked", "skillboard.legend.locked");
    }

    /// <summary>A 22 px swatch of the legend's key row (its children are placed in its own 22 x 22 frame).</summary>
    static RectTransform Swatch(RectTransform row, string name)
    {
        var rt = NewRect(row, name);
        rt.sizeDelta = new Vector2(22f, 22f);
        Layout(rt, 22f, 22f, 22f, 22f);
        return rt;
    }

    static void LegendWord(RectTransform row, string word, string key)
    {
        Localize(LayoutText(row, word, word, 17f, UIColors.Ink), Table, key);
    }

    // ------------------------------------------------------------------

    public static void Install(GameObject prefab, List<string> log)
    {
        Scene scene = SceneManager.GetSceneByPath(OfficeHudBuilder.ScenePath);
        if (!scene.IsValid() || !scene.isLoaded)
            scene = EditorSceneManager.OpenScene(OfficeHudBuilder.ScenePath, OpenSceneMode.Single);

        SkillBoardView existing = null;
        SkillTreeUI old = null;
        foreach (var r in scene.GetRootGameObjects())
        {
            if (existing == null) existing = r.GetComponentInChildren<SkillBoardView>(true);
            if (old == null) old = r.GetComponentInChildren<SkillTreeUI>(true);
        }

        GameObject instance;
        if (existing == null)
        {
            instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.name = "SkillBoard";
            log.Add("MainGame: added the SkillBoard prefab.");
        }
        else
        {
            instance = existing.gameObject;
            log.Add("MainGame: SkillBoard already there (the prefab update applies to it).");
        }
        instance.SetActive(true);   // the view lives on the root; its Page opens and closes

        if (old != null)
        {
            var panel = old.skillTreePanel != null ? old.skillTreePanel : old.gameObject;
            if (panel.activeSelf)
            {
                panel.SetActive(false);
                log.Add("MainGame: switched off the old skill tree panel '" + panel.name + "' (the HUD's Skills tab opens the board).");
            }
            else
            {
                log.Add("MainGame: the old skill tree panel stays off; the HUD's Skills tab opens the board.");
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        log.Add("MainGame saved.");
    }
}
