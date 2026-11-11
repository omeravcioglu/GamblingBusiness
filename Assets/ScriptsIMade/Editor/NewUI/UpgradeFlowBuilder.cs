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
/// Builds the designed office-upgrade flow (canvas boards "Upgrade: choose how", "Blackjack", "Upgrade result" and
/// "Hover tag") into Assets/UI/Prefabs/UpgradeFlow.prefab and wires it to MainGame's OfficeUpgradeManager and
/// BlackjackGame. The root stays active (the hover tag lives on it); the three pages are the managers' panels.
/// Fixed labels are bound to the Panels String Table (Assets/Localization/Source/Panels.csv); buttons are as wide as
/// their longest wording there.
/// </summary>
public static class UpgradeFlowBuilder
{
    public const string PrefabPath = "Assets/UI/Prefabs/UpgradeFlow.prefab";

    static readonly Color FeltShadow = UIColors.Hex(0x0B2A1B);

    const string L10nTable = "Panels";
    // Panels.csv rows by key (English and the translations), read once per build
    static Dictionary<string, List<string>> panelTexts;

    [MenuItem("Tools/Game/New UI/Build Upgrade Flow")]
    public static void BuildFromMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Build Upgrade Flow", "Stop Play mode first.", "OK");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var log = new List<string>();
        var prefab = BuildPrefab(log);
        if (prefab != null) Install(prefab, log);
        Debug.Log("[UpgradeFlowBuilder]\n" + string.Join("\n", log));
        EditorUtility.DisplayDialog("Upgrade flow", string.Join("\n", log), "OK");
    }

    public static GameObject BuildPrefab(List<string> log)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
        GameObject root = null;
        panelTexts = null;
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
        var root = new GameObject("UpgradeFlow", typeof(RectTransform));
        root.layer = 5;
        var rt = (RectTransform)root.transform;
        rt.sizeDelta = new Vector2(1920f, 1080f);

        var flow = root.AddComponent<UpgradeFlowView>();
        flow.itemIcons = new[]
        {
            new UpgradeFlowView.ItemIcon { keywords = "sofa couch chair armchair seat", icon = IconSprite("sofa") },
            new UpgradeFlowView.ItemIcon { keywords = "coffee espresso kitchen", icon = IconSprite("coffee") },
            new UpgradeFlowView.ItemIcon { keywords = "desk computer monitor screen pc tv", icon = IconSprite("monitor") },
            new UpgradeFlowView.ItemIcon { keywords = "lamp light", icon = IconSprite("bulb") },
            new UpgradeFlowView.ItemIcon { keywords = "music speaker radio jukebox", icon = IconSprite("music") },
            new UpgradeFlowView.ItemIcon { keywords = "trophy award", icon = IconSprite("trophy") },
        };
        flow.fallbackIcon = IconSprite("star");
        flow.stakeIcon = IconSprite("coin");

        var hover = Stretch(rt, "HoverTag");
        BuildHoverTag(hover, flow);

        var choice = Stretch(rt, "Choice");
        BuildChoice(choice, flow);
        choice.gameObject.SetActive(false);

        var table = Stretch(rt, "Blackjack");
        BuildTable(table, flow);
        table.gameObject.SetActive(false);

        var result = Stretch(rt, "Result");
        BuildResult(result, flow);
        result.gameObject.SetActive(false);

        // Canvas last (it would resize the root while building)
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 106;
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        root.AddComponent<GraphicRaycaster>();
        return root;
    }

    // ------------------------------------------------------------------
    // Shared pieces

    static void PageBase(RectTransform page)
    {
        var scrim = StretchBox(page, "Scrim", null, UIColors.Scrim);
        scrim.raycastTarget = true;
        CompanyNameBuilder.PausedTag(page);
    }

    static RectTransform TopLeftNode(Transform parent, string name, float left, float top, float width = 0f, float height = 0f)
    {
        var rt = NewRect(parent, name);
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.sizeDelta = new Vector2(width, height);
        rt.anchoredPosition = new Vector2(left, -top);
        return rt;
    }

    static Color HardShadow(Color c, float cssAlpha) => UIColors.WithAlpha(c, UIColors.SrgbAlpha(cssAlpha));

    /// <summary>
    /// Width of the widest wording of a Panels entry at this size: the English and each translation in Panels.csv (so a
    /// button fits every language). Just the English while the entry isn't in the file.
    /// </summary>
    static float LongestWidth(string english, string key, float size, bool bold = false)
    {
        if (panelTexts == null)
        {
            panelTexts = new Dictionary<string, List<string>>();
            string path = L10n.SourceFolder + "/" + L10nTable + ".csv";
            if (File.Exists(path))
            {
                var sheet = L10nCsv.ReadSheet(File.ReadAllText(path, System.Text.Encoding.UTF8));
                int keyColumn = sheet.Column("Key"), first = sheet.Column(L10n.English);
                foreach (var row in sheet.Rows)
                {
                    string rowKey = L10nCsv.Sheet.Cell(row, keyColumn).Trim();
                    if (rowKey.Length == 0 || first < 0) continue;
                    var texts = new List<string>();
                    for (int c = first; c < sheet.Header.Count; c++) texts.Add(L10nCsv.NormalizeNewlines(L10nCsv.Sheet.Cell(row, c)));
                    panelTexts[rowKey] = texts;
                }
            }
        }
        float width = TextWidth(english, size, bold);
        if (panelTexts.TryGetValue(key, out var wordings))
            foreach (string text in wordings)
                if (!string.IsNullOrEmpty(text)) width = Mathf.Max(width, TextWidth(text, size, bold));
        return width;
    }

    /// <summary>Lets a one-line text shrink (down to min) when a language's wording is wider than its box.</summary>
    static void ShrinkToFit(TMP_Text text, float min)
    {
        text.enableAutoSizing = true;
        text.fontSizeMax = text.fontSize;
        text.fontSizeMin = min;
    }

    /// <summary>Marker button with an icon and/or a label (icon first unless iconAfter); the label is the Panels entry key.</summary>
    static RectTransform LabelButton(Transform parent, string name, float left, float top, string label, string key, string icon, bool bold, Color fill,
        Vector2 shadowOffset, Color shadowColor, float width = -1f, bool iconAfter = false, Anchor anchor = Anchor.TopLeft)
    {
        float w = width > 0f ? width : 36f + (icon != null ? 28f + 12f : 0f) + LongestWidth(label, key, 28f, bold) + 36f;
        var btn = MarkerButtonNode(parent, name, left, top, w, 68f, anchor, fill, shadowOffset, out var face, out var content);
        btn.Find("Shadow").GetComponent<Image>().color = shadowColor;
        HLayout(content.gameObject, 12f, Pad(36, 36, 0, 0), TextAnchor.MiddleCenter, true, true, false, false);
        var graphics = new List<Graphic>();
        if (icon != null && !iconAfter) graphics.Add(LayoutIcon(content, "Icon", 28f, icon, UIColors.Ink));
        graphics.Add(Localize(LayoutText(content, "Label", label, 28f, UIColors.Ink, bold: bold), L10nTable, key));
        if (icon != null && iconAfter) graphics.Add(LayoutIcon(content, "Icon", 28f, icon, UIColors.Ink));
        SetupMarker(btn, face, graphics.ToArray());
        return btn;
    }

    /// <summary>Row of level pips at the parent's right edge (the view shows one per level of the item).</summary>
    static Image[] Pips(RectTransform parent, string name, float centerY, int size, float gap, int count)
    {
        var row = NewRect(parent, name);
        row.anchorMin = row.anchorMax = new Vector2(1f, 1f);
        row.pivot = new Vector2(1f, 0.5f);
        row.anchoredPosition = new Vector2(0f, -centerY);
        row.sizeDelta = new Vector2(count * size + (count - 1) * gap, size);
        HLayout(row.gameObject, gap, null, TextAnchor.MiddleRight, false, false, false, false);
        Fit(row.gameObject, true, false);
        var fills = new Image[count];
        for (int i = 0; i < count; i++)
        {
            var pip = NewRect(row, "Pip" + (i + 1));
            pip.sizeDelta = new Vector2(size, size);
            Layout(pip, size, size, size, size);
            fills[i] = StretchBox(pip, "Fill", S("Kit/Circles/circle" + size + "_fill"), i < 3 ? UIColors.Ink : UIColors.PaperHi);
            StretchBox(pip, "Ring", S("Kit/Circles/circle" + size + "_line2"), UIColors.Ink);
            if (i >= 5) pip.gameObject.SetActive(false);
        }
        return fills;
    }

    /// <summary>Felt fill clipped to a rounded shape: radial gradient (felt_radial) under a mask.</summary>
    static void Felt(RectTransform parent, string maskSprite, float inset)
    {
        var mask = StretchBox(parent, "Felt", S(maskSprite), Color.white, inset, inset, inset, inset);
        mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
        var g = StretchBox(mask.transform, "Gradient", S("Kit/Special/felt_radial"), Color.white);
        g.type = Image.Type.Simple;
    }

    // ------------------------------------------------------------------
    // Hover tag (no scrim: hovering doesn't pause)

    static void BuildHoverTag(RectTransform page, UpgradeFlowView flow)
    {
        var view = page.gameObject.AddComponent<HoverTagView>();
        view.flow = flow;

        var str = Stretch(page, "String");
        view.stringCurve = str.gameObject.AddComponent<UIStringCurve>();
        view.stringCurve.raycastTarget = false;
        view.stringCurve.edgeColor = UIColors.Ink;
        view.stringCurve.coreColor = UIColors.Manila;

        var knot = NewRect(page, "Knot");
        knot.anchorMin = knot.anchorMax = new Vector2(0.5f, 0.5f);
        knot.sizeDelta = new Vector2(12f, 12f);
        knot.anchoredPosition = new Vector2(452f - 960f, 540f - 402f);
        AddImage(knot.gameObject, S("Kit/Circles/circle12_fill"), UIColors.Ink);
        var core = NewRect(knot, "Core");
        core.sizeDelta = new Vector2(8f, 8f);
        AddImage(core.gameObject, S("Kit/Circles/circle8_fill"), UIColors.Manila);
        view.knot = knot;

        // tag 340 x 260 at 500, 312, rotated -3 deg
        var tag = Node(page, "Tag", 500f, 312f, 340f, 260f, Anchor.Center, -3f);
        view.tagRect = tag;
        var shadow = Box(tag, "Shadow", -21f, -21f + 12f, 382f, 302f, S("Kit/Special/luggage_tag_shadow"), HardShadow(Color.black, 0.35f));
        shadow.type = Image.Type.Simple;
        view.shadow = shadow.rectTransform;
        var art = Box(tag, "Art", 0f, 0f, 340f, 260f, S("Kit/Special/luggage_tag"), Color.white);
        art.type = Image.Type.Simple;
        view.art = art.rectTransform;

        // text column: x 70, width 256, padding 16 / 14, space-between
        var content = TopLeftNode(tag, "Content", 70f, 0f, 256f, 260f);
        view.content = content;
        view.contentLeft = 70f;
        view.contentLeftMirrored = 14f;

        view.icon = Icon(content, "Icon", 0f, 16f + 2.4f, 32f, "sofa", UIColors.Ink);
        view.itemName = Text(content, "Name", 42f, 16f, 214f, 36.8f, "Sofa", 32f, UIColors.Ink, bold: true);
        view.itemName.overflowMode = TextOverflowModes.Ellipsis;
        // the level shares its row with up to five pips (86 px): long level names shrink a little
        view.levelText = Text(content, "Level", 0f, 58.8f, 160f, 28.6f, "Good · Lvl 3/5", 22f, UIColors.Ink);
        view.levelText.enableAutoSizing = true;
        view.levelText.fontSizeMin = 15f;
        view.levelText.fontSizeMax = 22f;
        view.pips = Pips(content, "Pips", 58.8f + 14.3f, 14, 4f, 8);
        view.bonusText = Text(content, "Bonus", 0f, 93.4f, 256f, 25.65f,
            "<color=#1E7B45>+$300/day</color><space=8>· +6% productivity", 19f, UIColors.Ink);

        var rule = Box(content, "Rule", 0f, 144.2f, 256f, 2f, S("Kit/Tiles/dash_h2"), UIColors.Pencil);
        rule.type = Image.Type.Tiled;

        // [Click] or [E] to upgrade: a row, so each language's wording and keycaps take the room they need
        var hint = TopLeftNode(content, "Hint", 0f, 171.4f, 256f, 36f);
        HLayout(hint.gameObject, 8f, null, TextAnchor.MiddleLeft, true, false, false, false);
        Localize(RowKeycap(hint, "ClickKey", "Click", 19f, 36f), L10nTable, "upgrade.tag.click");
        Localize(RowText(hint, "Or", "or", 19f), L10nTable, "upgrade.tag.or");
        view.keyText = RowKeycap(hint, "InteractKey", "E", 19f, 36f);
        Localize(RowText(hint, "ToUpgrade", "to upgrade", 19f), L10nTable, "upgrade.tag.to_upgrade");
        view.hintRow = hint.gameObject;

        view.nextText = Text(content, "Next", 0f, 217.4f, 256f, 28.6f, "Next level: <link=\"hl\"><b>$18,821</b></link>", 22f, UIColors.Ink);
        view.nextText.gameObject.AddComponent<TextHighlighter>();

        // shown by the view while an item is hovered
        tag.gameObject.SetActive(false);
        str.gameObject.SetActive(false);
        knot.gameObject.SetActive(false);
    }

    /// <summary>Keycap as wide as its label (UIBuild.Keycap's look: paper-hi, 2 px ink border, 5 px bottom edge) in a layout row.</summary>
    static TMP_Text RowKeycap(Transform row, string name, string key, float size, float height)
    {
        var cap = NewRect(row, name);
        cap.sizeDelta = new Vector2(Mathf.Max(height, 18f + TextWidth(key, size, true)), height);
        HLayout(cap.gameObject, 0f, Pad(9, 9, 0, 3), TextAnchor.MiddleCenter, true, false, false, false);
        Layout(cap, height, height, -1f, height);   // a single letter stays square
        IgnoreLayout(StretchBox(cap, "Fill", S("Kit/small_fill"), UIColors.PaperHi));
        IgnoreLayout(StretchBox(cap, "Border", S("Kit/small_keycap"), UIColors.Ink));
        var label = LayoutText(cap, "Key", key, size, UIColors.Ink, bold: true, align: TextAlignmentOptions.Center);
        label.rectTransform.sizeDelta = new Vector2(TextWidth(key, size, true), height - 3f);
        return label;
    }

    /// <summary>One-line text in a layout row (as tall as the font needs).</summary>
    static TMP_Text RowText(Transform row, string name, string text, float size)
    {
        var label = LayoutText(row, name, text, size, UIColors.Ink);
        label.rectTransform.sizeDelta = new Vector2(TextWidth(text, size), Mathf.Max(26.6f, size * UIFonts.ContentHeight(UIBuild.Font)));
        return label;
    }

    // ------------------------------------------------------------------
    // Choice sheet: 400, 194, 1120 x 692; content box 43..1077 x 39..653 (padding 36 40 + 3 border)

    static void BuildChoice(RectTransform page, UpgradeFlowView flow)
    {
        var view = page.gameObject.AddComponent<UpgradeChoiceView>();
        view.flow = flow;
        PageBase(page);

        Shadow(page, "SheetShadow", 400f, 194f, 1120f, 692f, 0f, 22f, 48f, 0.45f, Anchor.Center);
        var sheet = Shape(page, "Sheet", 400f, 194f, 1120f, 692f, "panel", UIColors.Paper, UIColors.Ink, 3, Anchor.Center);

        // header: icon box 76 | gap 22 | title / levels / cost chip (gap 10), padding-right 84
        var iconBox = Shape(sheet, "IconBox", 43f, 39f, 76f, 76f, "card", UIColors.StickyYellow, UIColors.Ink, 3);
        view.itemIcon = Icon(iconBox, "Icon", 16f, 16f, 44f, "sofa", UIColors.Ink);
        view.title = Text(sheet, "Title", 141f, 39f, 852f, 50.6f, "Upgrade the Sofa?", 44f, UIColors.Ink, bold: true);
        view.title.overflowMode = TextOverflowModes.Ellipsis;
        // the 33.8 px line (a little taller box, so only its width can shrink it): long level names in some languages
        view.levels = Text(sheet, "Levels", 141f, 99.6f + 16.9f - 21f, 852f, 42f,
            "Good (Lvl 3) › <b>Premium (Lvl 4)</b><space=18><color=#676B74>·</color><space=18>+$300/day › <b><color=#1E7B45>+$400/day</color></b>",
            26f, UIColors.Ink);
        ShrinkToFit(view.levels, 18f);

        var chip = TopLeftNode(sheet, "Cost", 141f, 143.4f);
        HLayout(chip.gameObject, 8f, Pad(16, 16, 8, 8), TextAnchor.MiddleLeft, true, true, false, false);
        Fit(chip.gameObject, true, true);
        IgnoreLayout(StretchBox(chip, "Fill", S("Kit/small_fill"), UIColors.Highlight));
        IgnoreLayout(StretchBox(chip, "Border", S("Kit/small_line2"), UIColors.Ink));
        LayoutIcon(chip, "Icon", 24f, "coin", UIColors.Ink);
        view.cost = LayoutText(chip, "Text", "Costs <b>$18,821</b> up front", 22f, UIColors.Ink, lineHeight: 1.3f);

        BlackjackOption(sheet, view);
        DiceOption(sheet, view);

        // footer (y 585..653): Esc + note ........ Cancel
        var esc = Keycap(sheet, "EscKey", 43f, 601f, "Esc", 19f, 36f);
        float noteX = 43f + esc.sizeDelta.x + 10f;
        float cancelW = 36f + LongestWidth("Cancel", "upgrade.choice.cancel", 28f) + 36f;
        view.footer = Text(sheet, "Footer", noteX, 605.7f, 1077f - cancelW - 24f - noteX, 26.6f,
            "Either way, the $18,821 leaves the ledger first.", 19f, UIColors.Pencil);
        LabelButton(sheet, "CancelButton", 1077f - cancelW, 585f, "Cancel", "upgrade.choice.cancel", null, false, UIColors.PaperHi,
            new Vector2(4f, 5f), UIColors.Ink, cancelW);

        view.closeButton = IconButton(sheet, "CloseButton", 1033f, 27f, 60, "cross", Anchor.TopLeft, 28f);
    }

    // Felt card 497 x 320 at 43, 226.5 (two 500 px cards + 40 gap shrink to the 1034 px row)
    static void BlackjackOption(RectTransform sheet, UpgradeChoiceView view)
    {
        var card = Node(sheet, "BlackjackOption", 43f, 226.5f, 497f, 320f);
        Box(card, "Shadow", 3f, 4f, 497f, 320f, S("Kit/card_fill"), HardShadow(UIColors.Ink, 0.9f));
        Felt(card, "Kit/card_fill", 0f);
        StretchBox(card, "Border", S("Kit/card_line3"), UIColors.Ink);
        StretchBox(card, "GoldLine", S("Kit/small_line2"), UIColors.WithAlpha(UIColors.Gold, 0.8f), 11f, 11f, 11f, 11f);

        // padding 26 30 30 -> content x 33, y 29..287; rows at 29 / 90.7 / 219
        Icon(card, "Icon", 33f, 29.4f, 36f, "cards", UIColors.Gold);
        Localize(Text(card, "Title", 81f, 29f, 300f, 36.8f, "Play blackjack", 32f, UIColors.Gold, bold: true), L10nTable, "upgrade.choice.blackjack.title");
        Localize(Text(card, "Rules", 33f, 90.7f, 431f, 61.6f, "Win the hand › upgrade.\nLose › the money’s gone.", 22f, UIColors.PaperHi,
            lineHeight: 1.4f, wrap: true), L10nTable, "upgrade.choice.blackjack.rules");
        view.chance = Text(card, "Chance", 33f, 160.3f, 431f, 33.8f, "About <b><color=#E2B13C>42%</color></b> to win", 26f, UIColors.PaperHi);
        LabelButton(card, "DealButton", 33f, 219f, "Deal me in", "upgrade.choice.blackjack.deal", "cards", true, UIColors.Gold, new Vector2(5f, 6f), FeltShadow);

        // decorative mini cards (absolutely positioned, over the text layer)
        MiniCard(card, "MiniAce", 376f, 25f, -8f, "A", "spade", UIColors.Ink);
        MiniCard(card, "MiniKing", 416f, 29f, 7f, "K", "heart", UIColors.Loss);
    }

    static void MiniCard(RectTransform parent, string name, float left, float top, float rotation, string rank, string suit, Color color)
    {
        var c = Node(parent, name, left, top, 48f, 68f, Anchor.TopLeft, rotation);
        Box(c, "Shadow", 2f, 3f, 48f, 68f, S("Kit/r8_fill"), HardShadow(FeltShadow, 0.8f));
        StretchBox(c, "Fill", S("Kit/r8_fill"), UIColors.PaperHi);
        StretchBox(c, "Border", S("Kit/r8_line2"), UIColors.Ink);
        Text(c, "Rank", 8f, 6f, 30f, 20f, rank, 20f, color, bold: true);
        Icon(c, "Suit", 8f, 26f, 18f, suit, color, "fill");
    }

    // Paper card 497 x 320 at 580, 226.5
    static void DiceOption(RectTransform sheet, UpgradeChoiceView view)
    {
        var card = Node(sheet, "DiceOption", 580f, 226.5f, 497f, 320f);
        Box(card, "Shadow", 3f, 4f, 497f, 320f, S("Kit/card_fill"), HardShadow(UIColors.Ink, 0.9f));
        StretchBox(card, "Fill", S("Kit/card_fill"), UIColors.PaperHi);
        StretchBox(card, "Border", S("Kit/card_line3"), UIColors.Ink);

        Icon(card, "Icon", 33f, 29.4f, 36f, "dice", UIColors.Ink);
        Localize(Text(card, "Title", 81f, 29f, 300f, 36.8f, "Roll the dice", 32f, UIColors.Ink, bold: true), L10nTable, "upgrade.choice.dice.title");

        // odds bar 431 x 32 (3 px border, segments inside it)
        var bar = Node(card, "OddsBar", 33f, 108.05f, 431f, 32f);
        AddImage(bar.gameObject, S("Kit/small_fill"), UIColors.PaperHi).gameObject.AddComponent<Mask>().showMaskGraphic = true;
        var inner = Stretch(bar, "Segments", 3f, 3f, 3f, 3f);
        view.upgradeSegment = Segment(inner, "Upgrade", UIColors.Money, 0f, 0.6f);
        view.nothingSegment = Segment(inner, "Nothing", UIColors.PaperShade, 0.6f, 0.9f);
        foreach (var side in new[] { 0f, 1f })
        {
            var edge = NewRect(view.nothingSegment, side == 0f ? "EdgeLeft" : "EdgeRight");
            edge.anchorMin = new Vector2(side, 0f);
            edge.anchorMax = new Vector2(side, 1f);
            edge.pivot = new Vector2(side, 0.5f);
            edge.sizeDelta = new Vector2(3f, 0f);
            AddImage(edge.gameObject, null, UIColors.Ink);
        }
        view.loseSegment = Segment(inner, "Lose", UIColors.Loss, 0.9f, 1f);
        StretchBox(bar, "Border", S("Kit/small_line3"), UIColors.Ink);

        // the 24.7 px line (a little taller box, so only its width can shrink it): longer wordings shrink
        view.odds = Text(card, "OddsText", 33f, 152.05f + 12.35f - 15.5f, 431f, 31f,
            "<color=#1E7B45>60% upgrade</color><space=8>·<space=8><color=#474F60>30% nothing</color><space=8>·<space=8><color=#C0352B>10% lose a level</color>",
            19f, UIColors.Ink);
        ShrinkToFit(view.odds, 14f);
        LabelButton(card, "RollButton", 33f, 219f, "Roll for it", "upgrade.choice.dice.roll", "dice", false, UIColors.PaperHi, new Vector2(4f, 5f), UIColors.Ink);
    }

    static RectTransform Segment(RectTransform parent, string name, Color color, float from, float to)
    {
        var seg = NewRect(parent, name);
        seg.anchorMin = new Vector2(from, 0f);
        seg.anchorMax = new Vector2(to, 1f);
        seg.offsetMin = seg.offsetMax = Vector2.zero;
        AddImage(seg.gameObject, null, color);
        return seg;
    }

    // ------------------------------------------------------------------
    // Blackjack table: 360, 160, 1200 x 760, 14 px wood rail; children in inner coords (1172 x 732)

    static void BuildTable(RectTransform page, UpgradeFlowView flow)
    {
        var view = page.gameObject.AddComponent<BlackjackTableView>();
        view.flow = flow;
        PageBase(page);

        Shadow(page, "TableShadow", 360f, 160f, 1200f, 760f, 0f, 22f, 48f, 0.45f, Anchor.Center);
        Box(page, "InkRing", 357f, 157f, 1206f, 766f, S("Kit/panel_fill"), UIColors.Ink, Anchor.Center);
        var table = Node(page, "Table", 360f, 160f, 1200f, 760f, Anchor.Center);
        AddImage(table.gameObject, S("Kit/panel_fill"), UIColors.ClipboardDark);
        Felt(table, "Kit/tiny_fill", 14f);
        var inner = Stretch(table, "Inner", 14f, 14f, 14f, 14f);
        StretchBox(inner, "GoldLine", S("Kit/card_line3"), UIColors.WithAlpha(UIColors.Gold, 0.8f), 12f, 12f, 12f, 12f);

        // header (56, 36, 1060 wide): title left, stake chip right, centred on the 50.6 px title line; the title
        // shrinks to stay clear of the widest stake chip (its left edge at 362)
        Icon(inner, "TitleIcon", 56f, 39.3f, 44f, "cards", UIColors.Gold);
        var title = Localize(Text(inner, "Title", 114f, 36f + 25.3f - 35f, 240f, 70f, "Blackjack", 44f, UIColors.Gold, bold: true), L10nTable, "upgrade.table.title");
        ShrinkToFit(title, 26f);
        var chip = NewRect(inner, "Stake");
        chip.anchorMin = chip.anchorMax = new Vector2(0f, 1f);
        chip.pivot = new Vector2(1f, 0.5f);
        chip.anchoredPosition = new Vector2(1116f, -(36f + 25.3f));
        HLayout(chip.gameObject, 10f, Pad(18, 18, 10, 10), TextAnchor.MiddleLeft, true, true, false, false);
        Fit(chip.gameObject, true, true);
        IgnoreLayout(StretchBox(chip, "Shadow", S("Kit/small_fill"), HardShadow(FeltShadow, 0.8f), 3f, 4f, -3f, -4f));
        IgnoreLayout(StretchBox(chip, "Fill", S("Kit/small_fill"), UIColors.PaperHi));
        IgnoreLayout(StretchBox(chip, "Border", S("Kit/small_line2"), UIColors.Ink));
        view.stakeIcon = LayoutIcon(chip, "Icon", 28f, "sofa", UIColors.Ink);
        view.stakeText = LayoutText(chip, "Text", "At stake: Sofa upgrade · <b>$18,821</b>", 22f, UIColors.Ink, lineHeight: 1.3f, wrap: true);
        view.stakeWidth = Layout(view.stakeText, -1f, -1f, TextWidth("At stake: Sofa upgrade · $18,821", 22f) + 12f, -1f);

        // rows: dealer at 132, player at 440 (label column 206..402, cards from 428)
        view.dealerTotal = HandLabel(inner, "Dealer", "Dealer", "upgrade.table.dealer", 132f, "10 + ?");
        view.playerTotal = HandLabel(inner, "Player", "You", "upgrade.table.you", 440f, "16");
        view.dealerRow = TopLeftNode(inner, "DealerCards", 428f, 132f, 420f, 210f);
        view.playerRow = TopLeftNode(inner, "PlayerCards", 428f, 440f, 370f, 210f);
        view.firstCardX = 0f;
        view.cardStep = 168f;
        view.dealerLastMaxX = 272f;   // the shoe starts at 860
        view.playerLastMaxX = 222f;   // the buttons start at 812

        // the shoe (decor)
        Box(inner, "ShoeShadow", 875f, 160f, 120f, 168f, S("Kit/r10_fill"), HardShadow(FeltShadow, 0.8f));
        Shape(inner, "Shoe3", 872f, 156f, 120f, 168f, "r10", UIColors.PaperHi, UIColors.Ink, 2);
        Shape(inner, "Shoe2", 866f, 150f, 120f, 168f, "r10", UIColors.PaperHi, UIColors.Ink, 2);
        var top = Box(inner, "Shoe1", 860f, 144f, 120f, 168f, S("Kit/Special/card_back"), Color.white);
        top.type = Image.Type.Simple;

        // rules / outcome placard (the view writes it)
        var plac = Node(inner, "Placard", 351f, 370f, 470f, 42f, Anchor.TopLeft, -1f);
        Box(plac, "Shadow", 2f, 3f, 470f, 42f, S("Kit/small_fill"), HardShadow(FeltShadow, 0.8f));
        StretchBox(plac, "Fill", S("Kit/small_fill"), UIColors.PaperHi);
        StretchBox(plac, "Border", S("Kit/small_line2"), UIColors.Ink);
        view.placard = Text(plac, "Text", 8f, 0f, 454f, 42f, BlackjackTableView.RulesLine, 19f, UIColors.Ink, align: TextAlignmentOptions.Center);

        // actions (812, 469, 220 wide): Hit / Stand while playing, Continue once the hand is over
        var play = Stretch(inner, "PlayButtons");
        LabelButton(play, "HitButton", 812f, 469f, "Hit", "upgrade.table.hit", "plus", true, UIColors.Gold, new Vector2(5f, 6f), FeltShadow, 220f);
        LabelButton(play, "StandButton", 812f, 553f, "Stand", "upgrade.table.stand", null, false, UIColors.PaperHi, new Vector2(4f, 5f), FeltShadow, 220f);
        view.playButtons = play.gameObject;
        var cont = LabelButton(inner, "ContinueButton", 812f, 469f, "Continue", "upgrade.table.continue", "arrow-right", true, UIColors.Gold,
            new Vector2(5f, 6f), FeltShadow, 220f, iconAfter: true);
        cont.gameObject.SetActive(false);
        view.continueButton = cont.gameObject;

        view.cardTemplate = CardTemplate(inner);
        view.cardTemplate.gameObject.SetActive(false);
    }

    static TMP_Text HandLabel(RectTransform inner, string name, string label, string key, float rowTop, string total)
    {
        // column 206..402, content (label 31.2 + 12 + chip 45.8) centred in the 210 px row
        float y = rowTop + (210f - 89f) * 0.5f;
        Localize(Text(inner, name + "Label", 206f, y, 196f, 31.2f, label, 26f, UIColors.PaperHi, align: TextAlignmentOptions.Right), L10nTable, key);
        var chip = NewRect(inner, name + "Total");
        chip.anchorMin = chip.anchorMax = new Vector2(0f, 1f);
        chip.pivot = new Vector2(1f, 1f);
        chip.anchoredPosition = new Vector2(402f, -(y + 31.2f + 12f));
        HLayout(chip.gameObject, 0f, Pad(16, 16, 6, 6), TextAnchor.MiddleCenter, true, true, false, false);
        Fit(chip.gameObject, true, true);
        IgnoreLayout(StretchBox(chip, "Fill", S("Kit/small_fill"), UIColors.PaperHi));
        IgnoreLayout(StretchBox(chip, "Border", S("Kit/small_line2"), UIColors.Ink));
        return LayoutText(chip, "Text", total, 26f, UIColors.Ink, bold: true, lineHeight: 1.3f);
    }

    /// <summary>150 x 210 card: hard shadow, face (corners + big suit) and back.</summary>
    static PlayingCardView CardTemplate(RectTransform parent)
    {
        var card = TopLeftNode(parent, "CardTemplate", 428f, 132f, 150f, 210f);
        var view = card.gameObject.AddComponent<PlayingCardView>();
        Box(card, "Shadow", 3f, 4f, 150f, 210f, S("Kit/r12_fill"), HardShadow(UIColors.Ink, 0.85f));

        var face = Stretch(card, "Face");
        StretchBox(face, "Fill", S("Kit/r12_fill"), UIColors.PaperHi);
        StretchBox(face, "Border", S("Kit/r12_line2"), UIColors.Ink);
        view.cornerTop = Corner(face, "CornerTop", out view.rankTop, out view.suitTop);
        view.cornerBottom = Corner(face, "CornerBottom", out view.rankBottom, out view.suitBottom);
        view.cornerBottom.localEulerAngles = new Vector3(0f, 0f, 180f);
        view.suitCenter = Icon(face, "SuitCenter", 39f, 69f, 72f, "spade", UIColors.Ink, "fill");
        view.face = face.gameObject;

        var back = Box(card, "Back", 0f, 0f, 150f, 210f, S("Kit/Special/card_back"), Color.white);
        back.type = Image.Type.Simple;
        back.gameObject.SetActive(false);
        view.back = back.gameObject;

        view.suitSprites = new[] { IconSprite("spade", "fill"), IconSprite("heart", "fill"), IconSprite("diamond", "fill"), IconSprite("club", "fill") };
        view.cornerInset = new Vector2(12f, 10f);
        view.cornerHeight = 56f;
        view.cornerSuitSize = 24f;
        view.Set(12, true);   // K of spades, as on the board
        return view;
    }

    static RectTransform Corner(RectTransform face, string name, out TMP_Text rank, out Image suit)
    {
        // rank (30 px, line-height 1) over the suit (24 px), gap 2, centred on each other
        var group = NewRect(face, name);
        group.pivot = new Vector2(0.5f, 0.5f);
        var r = NewRect(group, "Rank");
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 1f);
        r.pivot = new Vector2(0.5f, 0.5f);
        r.sizeDelta = new Vector2(60f, 30f * UIFonts.ContentHeight(UIBuild.Font));
        r.anchoredPosition = new Vector2(0f, -15f);
        rank = SetupText(r.gameObject, "K", 30f, UIColors.Ink, true, TextAlignmentOptions.Center);
        var s = NewRect(group, "Suit");
        s.anchorMin = s.anchorMax = new Vector2(0.5f, 1f);
        s.pivot = new Vector2(0.5f, 1f);
        s.sizeDelta = new Vector2(24f, 24f);
        s.anchoredPosition = new Vector2(0f, -32f);
        suit = AddImage(s.gameObject, IconSprite("spade", "fill"), UIColors.Ink);
        suit.preserveAspect = true;
        return group;
    }

    // ------------------------------------------------------------------
    // Result sheet: 570, 226, 780 x 628, centred column (gap 22)

    static void BuildResult(RectTransform page, UpgradeFlowView flow)
    {
        var view = page.gameObject.AddComponent<UpgradeResultView>();
        view.flow = flow;
        PageBase(page);

        Shadow(page, "SheetShadow", 570f, 226f, 780f, 628f, 0f, 22f, 48f, 0.45f, Anchor.Center);
        var sheet = Shape(page, "Sheet", 570f, 226f, 780f, 628f, "panel", UIColors.Paper, UIColors.Ink, 3, Anchor.Center);
        VLayout(sheet.gameObject, 22f, Pad(51, 51, 43, 47), TextAnchor.MiddleCenter, true, true, false, false);

        // stamp slot (104 tall so every outcome keeps the same rhythm)
        var slot = NewRect(sheet, "StampSlot");
        Layout(slot, 684f, 104f, 684f, 104f);
        var stamp = NewRect(slot, "Stamp");
        stamp.anchorMin = stamp.anchorMax = new Vector2(0.5f, 0.5f);
        stamp.localEulerAngles = new Vector3(0f, 0f, 10f);
        HLayout(stamp.gameObject, 0f, Pad(27, 27, 11, 11), TextAnchor.MiddleCenter, true, true, false, false);
        Fit(stamp.gameObject, true, true);
        var group = stamp.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0.88f;
        group.blocksRaycasts = false;
        var s1 = StretchBox(stamp, "Border", S("Kit/r10_line5"), UIColors.Money);
        IgnoreLayout(s1);
        var s2 = StretchBox(stamp, "Outline", S("Kit/r14_line2"), UIColors.Money, -6f, -6f, -6f, -6f);
        IgnoreLayout(s2);
        view.stampLines = new[] { s1, s2 };
        view.stampText = LayoutText(stamp, "Word", "UPGRADED!", 44f, UIColors.Money, bold: true, letterSpacingEm: 0.12f, lineHeight: 1.15f);
        view.stamp = stamp.gameObject;

        var none = Node(slot, "NoStamp", 300f, 10f, 84f, 84f, Anchor.TopLeft, -6f);
        Dashed(StretchBox(none, "Border", S("Kit/card_dash3"), UIColors.Pencil));
        Icon(none, "Icon", 16f, 16f, 52f, "dice", UIColors.Pencil);
        none.gameObject.SetActive(false);
        view.noStamp = none.gameObject;

        view.title = LayoutText(sheet, "Title", "The Sofa is now Premium.", 44f, UIColors.Ink, bold: true, align: TextAlignmentOptions.Center,
            lineHeight: 1.15f, wrap: true);
        Layout(view.title, -1f, -1f, 640f, -1f);
        view.sub = LayoutText(sheet, "Sub", "The dice shrugged. The Sofa stays Good.", 26f, UIColors.InkSoft, align: TextAlignmentOptions.Center,
            lineHeight: 1.4f, wrap: true);
        Layout(view.sub, -1f, -1f, 684f, -1f);
        view.sub.gameObject.SetActive(false);

        // state card: 560 wide, padding 14 20 (+2 border), gap 6
        var card = NewRect(sheet, "StateCard");
        VLayout(card.gameObject, 6f, Pad(22, 22, 16, 16), TextAnchor.UpperLeft, true, true, true, false);
        Layout(card, 560f, -1f, 560f, -1f);
        IgnoreLayout(StretchBox(card, "Shadow", S("Kit/card_fill"), HardShadow(UIColors.Ink, 0.9f), 3f, 4f, -3f, -4f));
        IgnoreLayout(StretchBox(card, "Fill", S("Kit/card_fill"), UIColors.PaperHi));
        IgnoreLayout(StretchBox(card, "Border", S("Kit/card_line2"), UIColors.Ink));
        var row = NewRect(card, "Row");
        row.sizeDelta = new Vector2(516f, 33.8f);
        Layout(row, -1f, 33.8f, -1f, 33.8f);
        view.stateIcon = Icon(row, "Icon", 0f, 1.9f, 30f, "sofa", UIColors.Ink);
        view.stateName = Text(row, "Name", 40f, 0f, 360f, 33.8f, "Sofa · <b>Premium</b>", 26f, UIColors.Ink);
        view.pips = Pips(row, "Pips", 16.9f, 18, 6f, 8);
        view.stateLine = LayoutText(card, "Line", "Lvl 4/5 · +$400/day · +8% productivity", 22f, UIColors.Ink, lineHeight: 1.35f);

        view.money = LayoutText(sheet, "Money", "−$18,821 paid up front", 26f, UIColors.InkSoft, bold: true, align: TextAlignmentOptions.Center,
            lineHeight: 1.3f);

        float backW = 36f + LongestWidth("Back to work", "upgrade.result.back", 28f, true) + 12f + 28f + 36f;
        var back = LabelButton(sheet, "BackButton", 0f, 0f, "Back to work", "upgrade.result.back", "arrow-right", true, UIColors.Highlight,
            new Vector2(5f, 6f), UIColors.Ink, backW, iconAfter: true);
        Layout(back, backW, 68f, backW, 68f);

        view.closeButton = IconButton(page, "CloseButton", 1263f, 253f, 60, "cross", Anchor.Center, 28f);
    }

    // ------------------------------------------------------------------

    public static void Install(GameObject prefab, List<string> log)
    {
        Scene scene = SceneManager.GetSceneByPath(OfficeHudBuilder.ScenePath);
        if (!scene.IsValid() || !scene.isLoaded)
            scene = EditorSceneManager.OpenScene(OfficeHudBuilder.ScenePath, OpenSceneMode.Single);

        OfficeUpgradeManager manager = null;
        UpgradeFlowView existing = null;
        foreach (var r in scene.GetRootGameObjects())
        {
            if (manager == null) manager = r.GetComponentInChildren<OfficeUpgradeManager>(true);
            if (existing == null) existing = r.GetComponentInChildren<UpgradeFlowView>(true);
        }
        if (manager == null)
        {
            log.Add("MainGame has no OfficeUpgradeManager.");
            return;
        }
        var game = manager.blackjackGame != null ? manager.blackjackGame : manager.GetComponent<BlackjackGame>();

        GameObject instance;
        if (existing == null)
        {
            instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.name = "UpgradeFlow";
            log.Add("MainGame: added the UpgradeFlow prefab.");
        }
        else
        {
            instance = existing.gameObject;
            log.Add("MainGame: UpgradeFlow already there (the prefab update applies to it).");
        }
        instance.SetActive(true);   // the hover tag lives on the root; the pages are toggled by the managers
        var t = instance.transform;

        var so = new SerializedObject(manager);
        HideOld(so, "upgradeChoicePanel", t, log);
        HideOld(so, "randomResultPanel", t, log);
        so.FindProperty("upgradeChoicePanel").objectReferenceValue = t.Find("Choice").gameObject;
        so.FindProperty("randomResultPanel").objectReferenceValue = t.Find("Result").gameObject;
        foreach (var name in new[] { "itemNameText", "currentLevelText", "upgradeCostText", "upgradeInfoText", "randomResultText", "randomOutcomeText" })
            so.FindProperty(name).objectReferenceValue = null;
        so.FindProperty("blackjackButton").objectReferenceValue = t.Find("Choice/Sheet/BlackjackOption/DealButton").GetComponent<Button>();
        so.FindProperty("randomChanceButton").objectReferenceValue = t.Find("Choice/Sheet/DiceOption/RollButton").GetComponent<Button>();
        so.FindProperty("cancelButton").objectReferenceValue = t.Find("Choice/Sheet/CancelButton").GetComponent<Button>();
        so.FindProperty("randomCloseButton").objectReferenceValue = t.Find("Result/Sheet/BackButton").GetComponent<Button>();
        so.FindProperty("writeTexts").boolValue = false;
        so.ApplyModifiedPropertiesWithoutUndo();
        log.Add("MainGame: OfficeUpgradeManager -> UpgradeFlow (choice sheet, result sheet, buttons; texts drawn by the views).");

        if (game != null)
        {
            var sg = new SerializedObject(game);
            HideOld(sg, "blackjackPanel", t, log);
            sg.FindProperty("blackjackPanel").objectReferenceValue = t.Find("Blackjack").gameObject;
            foreach (var name in new[] { "playerHandText", "dealerHandText", "playerScoreText", "dealerScoreText", "resultText", "stakeText" })
                sg.FindProperty(name).objectReferenceValue = null;
            sg.FindProperty("hitButton").objectReferenceValue = t.Find("Blackjack/Table/Inner/PlayButtons/HitButton").GetComponent<Button>();
            sg.FindProperty("standButton").objectReferenceValue = t.Find("Blackjack/Table/Inner/PlayButtons/StandButton").GetComponent<Button>();
            sg.FindProperty("closeButton").objectReferenceValue = t.Find("Blackjack/Table/Inner/ContinueButton").GetComponent<Button>();
            sg.ApplyModifiedPropertiesWithoutUndo();
            log.Add("MainGame: BlackjackGame -> the felt table (Hit, Stand, Continue).");
        }
        else
        {
            log.Add("MainGame: no BlackjackGame next to the OfficeUpgradeManager; the table isn't wired.");
        }

        // the views talk to the scene's managers
        var choiceView = instance.GetComponentInChildren<UpgradeChoiceView>(true);
        var resultView = instance.GetComponentInChildren<UpgradeResultView>(true);
        var tableView = instance.GetComponentInChildren<BlackjackTableView>(true);
        SetRef(choiceView, "manager", manager);
        SetRef(resultView, "manager", manager);
        if (game != null) SetRef(tableView, "game", game);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        log.Add("MainGame saved.");
    }

    static void HideOld(SerializedObject so, string property, Transform instance, List<string> log)
    {
        var old = so.FindProperty(property).objectReferenceValue as GameObject;
        if (old == null || old.transform.IsChildOf(instance)) return;
        if (old.activeSelf)
        {
            old.SetActive(false);
            log.Add("MainGame: switched off the old '" + old.name + "' panel.");
        }
    }

    static void SetRef(Object target, string property, Object value)
    {
        if (target == null) return;
        var so = new SerializedObject(target);
        so.FindProperty(property).objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
