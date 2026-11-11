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
/// Builds the designed risk-card event (canvas boards "Risk cards", "Risk result: good" and "Risk result: bad") into
/// Assets/UI/Prefabs/RiskEvent.prefab and wires it to MainGame's EventManager: the three playing cards on the felt,
/// then the newspaper clipping with the outcome. RiskCardsView / RiskResultView do the design-only parts.
/// Fixed labels are bound to the Hud String Table (Localize, Assets/Localization/Source/Hud.csv); the views write the rest.
/// </summary>
public static class RiskEventBuilder
{
    public const string PrefabPath = "Assets/UI/Prefabs/RiskEvent.prefab";
    const string T = "Hud";

    static readonly Color Newsprint = UIColors.Hex(0xF1ECDD);

    [MenuItem("Tools/Game/New UI/Build Risk Event")]
    public static void BuildFromMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Build Risk Event", "Stop Play mode first.", "OK");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var log = new List<string>();
        var prefab = BuildPrefab(log);
        if (prefab != null) Install(prefab, log);
        Debug.Log("[RiskEventBuilder]\n" + string.Join("\n", log));
        EditorUtility.DisplayDialog("Risk event", string.Join("\n", log), "OK");
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
        var root = new GameObject("RiskEvent", typeof(RectTransform));
        root.layer = 5;
        var rt = (RectTransform)root.transform;
        rt.sizeDelta = new Vector2(1920f, 1080f);

        var scrim = StretchBox(rt, "Scrim", null, UIColors.Scrim);
        scrim.raycastTarget = true;
        CompanyNameBuilder.PausedTag(rt);

        var cards = Stretch(rt, "Cards");
        BuildCards(cards);
        var result = Stretch(rt, "Result");
        BuildResult(result);
        result.gameObject.SetActive(false);

        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 105;
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        root.AddComponent<GraphicRaycaster>();
        return root;
    }

    // ------------------------------------------------------------------
    // Card choice

    static void BuildCards(RectTransform page)
    {
        var view = page.gameObject.AddComponent<RiskCardsView>();

        // banner: 440, 128, 1040 x 140, taped at both ends
        Shadow(page, "BannerShadow", 440f, 128f, 1040f, 140f, 0f, 22f, 48f, 0.45f, Anchor.Center);
        var banner = Shape(page, "Banner", 440f, 128f, 1040f, 140f, "panel", UIColors.Paper, UIColors.Ink, 3, Anchor.Center);
        Box(banner, "TapeLeft", -44f, -16f, 150f, 38f, null, UIColors.Tape, Anchor.TopLeft, -9f);
        Box(banner, "TapeRight", 1040f + 44f - 150f, -16f, 150f, 38f, null, UIColors.Tape, Anchor.TopLeft, 8f);
        view.headline = Shrink(Text(banner, "Headline", 40f, 22f, 960f, 50.6f, "Something’s brewing at Nova Holdings…", 44f, UIColors.Ink,
            bold: true, align: TextAlignmentOptions.Center), 28f);   // a long company name or language shrinks it
        Localize(Text(banner, "Subline", 40f, 80.6f, 960f, 30.8f, "Pick a card. The safer the card, the smaller the prize.", 22f, UIColors.InkSoft,
            align: TextAlignmentOptions.Center), T, "hud.risk.subline");

        // felt band: 0, 290, full width x 560, gold rules top and bottom
        var feltShadow = Shadow(page, "FeltShadow", 0f, 290f, 1920f, 560f, 0f, 22f, 48f, 0.45f, Anchor.Center);
        var felt = Node(page, "Felt", 0f, 290f, 1920f, 560f, Anchor.Center);
        felt.anchorMin = new Vector2(0f, 0.5f);
        felt.anchorMax = new Vector2(1f, 0.5f);
        felt.sizeDelta = new Vector2(0f, 560f);
        feltShadow.rectTransform.anchorMin = new Vector2(0f, 0.5f);
        feltShadow.rectTransform.anchorMax = new Vector2(1f, 0.5f);
        feltShadow.rectTransform.sizeDelta = new Vector2(0f, feltShadow.rectTransform.sizeDelta.y);
        AddImage(felt.gameObject, null, UIColors.FeltDark);
        var glow = StretchBox(felt, "Glow", S("Shadows/shadow_s24"), UIColors.FeltHi, 180f, -60f, 180f, 120f);
        glow.type = Image.Type.Sliced;
        var mid = StretchBox(felt, "Tone", S("Shadows/shadow_s24"), UIColors.WithAlpha(UIColors.Felt, 0.9f), -40f, -40f, -40f, -40f);
        mid.type = Image.Type.Sliced;
        mid.transform.SetSiblingIndex(0);
        var topRule = NewRect(felt, "GoldTop");
        topRule.anchorMin = new Vector2(0f, 1f);
        topRule.anchorMax = new Vector2(1f, 1f);
        topRule.pivot = new Vector2(0.5f, 1f);
        topRule.sizeDelta = new Vector2(0f, 3f);
        AddImage(topRule.gameObject, null, UIColors.Gold);
        var bottomRule = NewRect(felt, "GoldBottom");
        bottomRule.anchorMin = new Vector2(0f, 0f);
        bottomRule.anchorMax = new Vector2(1f, 0f);
        bottomRule.pivot = new Vector2(0.5f, 0f);
        bottomRule.sizeDelta = new Vector2(0f, 3f);
        AddImage(bottomRule.gameObject, null, UIColors.Gold);

        // three cards: 360 x 500 at x 372 / 780 / 1188, y 320
        view.highRisk = Card(page, "HighRisk", 372f, "fire", UIColors.Loss, UIColors.LossSoft, "High risk", "hud.risk.card.high.title", 0.25f,
            "Excellent reward — or a terrible hit", "hud.risk.card.high.blurb");
        view.balanced = Card(page, "Balanced", 780f, "scales", UIColors.Ink, UIColors.StickyYellow, "Balanced", "hud.risk.card.balanced.title", 0.5f,
            "Average reward", "hud.risk.card.balanced.blurb");
        view.lowRisk = Card(page, "LowRisk", 1188f, "shield", UIColors.Money, UIColors.MoneySoft, "Low risk", "hud.risk.card.low.title", 0.75f,
            "Small reward", "hud.risk.card.low.blurb");

        // footnote: a dice icon and the line, centred under the felt whatever the line's length (it differs per language)
        var foot = Node(page, "Footnote", 0f, 874f, 1920f, 32f, Anchor.Center);
        HLayout(foot.gameObject, 10f, null, TextAnchor.MiddleCenter, true, true, false, false);
        LayoutIcon(foot, "Icon", 24f, "dice", UIColors.PaperHi);
        Localize(LayoutText(foot, "Text", "The event itself is random — you’re only choosing the odds.", 19f, UIColors.PaperHi),
            T, "hud.risk.footnote");
    }

    static RiskCardsView.Card Card(RectTransform page, string name, float left, string icon, Color accent, Color circle, string title,
        string titleKey, float baseChance, string blurb, string blurbKey)
    {
        var card = new RiskCardsView.Card();
        var node = Node(page, name + "Card", left, 320f, 360f, 500f, Anchor.Center);
        var hit = AddImage(node.gameObject, null, Clear, true);
        var button = node.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = hit;
        card.button = button;

        StretchShadow(node, 0f, 18f, 32f, 0.35f);
        var hard = StretchBox(node, "Shadow", S("Kit/r18_fill"), UIColors.Ink, 5f, 6f, -5f, -6f);
        var face = Stretch(node, "Face");
        StretchBox(face, "Fill", S("Kit/r18_fill"), UIColors.PaperHi);
        StretchBox(face, "Border", S("Kit/r18_line3"), UIColors.Ink);
        Icon(face, "CornerTop", 14f, 14f, 30f, icon, accent);
        Icon(face, "CornerBottom", 360f - 14f - 30f, 500f - 14f - 30f, 30f, icon, accent, null, Anchor.TopLeft, 180f);
        var marker = node.gameObject.AddComponent<MarkerButton>();
        marker.face = face;
        marker.shadow = hard.gameObject;

        // content column: padding 40 29 0, gap 18, centred
        var col = Node(face, "Content", 29f, 40f, 302f, 440f);
        col.pivot = new Vector2(0.5f, 1f);
        col.anchoredPosition += new Vector2(0f, 220f);
        VLayout(col.gameObject, 18f, null, TextAnchor.UpperCenter, true, true, false, false);

        var circleNode = NewRect(col, "IconCircle");
        Layout(circleNode, 104f, 104f, 104f, 104f);
        AddImage(circleNode.gameObject, S("Kit/Blobs/blob104_fill"), circle);
        StretchBox(circleNode, "Border", S("Kit/Blobs/blob104_line3"), UIColors.Ink);
        Icon(circleNode, "Icon", 20f, 20f, 64f, icon, accent);

        Localize(LayoutText(col, "Title", title, 44f, UIColors.Ink, bold: true, align: TextAlignmentOptions.Center, lineHeight: 1.15f),
            T, titleKey);

        // odds: bar (296 x 34) + text
        var odds = NewRect(col, "Odds");
        Layout(odds, 296f, 74.8f, 296f, 74.8f);
        var bar = Node(odds, "Bar", 0f, 0f, 296f, 34f);
        AddImage(bar.gameObject, S("Kit/small_fill"), UIColors.PaperHi).gameObject.AddComponent<Mask>().showMaskGraphic = true;
        card.goodSegment = Segment(bar, "Good", UIColors.Money, "check", baseChance, false);
        card.bonusSegment = Segment(bar, "Bonus", UIColors.Money, null, 0f, true);
        card.badSegment = Segment(bar, "Bad", UIColors.Loss, "cross", 1f - baseChance, false);
        card.bonusSegment.gameObject.SetActive(false);
        SetSegment(card.goodSegment, 0f, baseChance);
        SetSegment(card.badSegment, baseChance, 1f);
        StretchBox(bar, "Border", S("Kit/small_line2"), UIColors.Ink);
        int pct = Mathf.RoundToInt(baseChance * 100f);
        card.odds = Text(odds, "OddsText", 0f, 44f, 296f, 30.8f,
            "<color=#1E7B45>" + pct + "% good</color> · <color=#C0352B>" + (100 - pct) + "% bad</color>", 22f, UIColors.Ink,
            align: TextAlignmentOptions.Center);

        // chip: "+15% Beginner's Luck" (sticky green)
        var chip = NewRect(col, "Chip");   // sized by the column's layout (its HLayout reports the preferred size)
        HLayout(chip.gameObject, 6f, Pad(12, 12, 4, 4), TextAnchor.MiddleCenter, true, true, false, false);
        var chipFill = StretchBox(chip, "Fill", S("Kit/small_fill"), UIColors.StickyGreen);
        IgnoreLayout(chipFill);
        var chipBorder = StretchBox(chip, "Border", S("Kit/small_line2"), UIColors.Ink);
        IgnoreLayout(chipBorder);
        LayoutIcon(chip, "Icon", 20f, "sparkle", UIColors.Ink);
        card.chipText = LayoutText(chip, "Text", "+15% Beginner’s Luck", 18f, UIColors.Ink, lineHeight: 1.4f);
        card.chip = chip.gameObject;
        chip.gameObject.SetActive(false);

        var blurbText = Localize(LayoutText(col, "Blurb", blurb, 19f, UIColors.InkSoft, align: TextAlignmentOptions.Center, lineHeight: 1.4f,
            wrap: true), T, blurbKey);
        Layout(blurbText, 302f, -1f, 302f, -1f);
        return card;
    }

    static RectTransform Segment(RectTransform bar, string name, Color color, string icon, float width, bool hatched)
    {
        var seg = NewRect(bar, name);
        AddImage(seg.gameObject, null, color);
        if (hatched)
        {
            var h = StretchBox(seg, "Hatch", S("Kit/Tiles/hatch45"), Color.white);
            h.type = Image.Type.Tiled;
        }
        if (icon != null)
        {
            var ic = NewRect(seg, "Icon");
            ic.anchorMin = ic.anchorMax = new Vector2(0.5f, 0.5f);
            ic.sizeDelta = new Vector2(20f, 20f);
            var img = AddImage(ic.gameObject, IconSprite(icon, "b"), UIColors.PaperHi);
            img.preserveAspect = true;
        }
        if (name == "Bad")
        {
            // border-left: 2px solid ink between good and bad
            var edge = NewRect(seg, "Edge");
            edge.anchorMin = new Vector2(0f, 0f);
            edge.anchorMax = new Vector2(0f, 1f);
            edge.pivot = new Vector2(0f, 0.5f);
            edge.sizeDelta = new Vector2(2f, 0f);
            AddImage(edge.gameObject, null, UIColors.Ink);
        }
        return seg;
    }

    static void SetSegment(RectTransform seg, float from, float to)
    {
        seg.anchorMin = new Vector2(from, 0f);
        seg.anchorMax = new Vector2(to, 1f);
        seg.offsetMin = seg.offsetMax = Vector2.zero;
    }

    /// <summary>A fixed box's text shrinks (down to min) instead of overflowing when a language or a name needs more room.</summary>
    static TText Shrink<TText>(TText text, float min) where TText : TMP_Text
    {
        text.enableAutoSizing = true;
        text.fontSizeMin = min;
        text.fontSizeMax = text.fontSize;
        return text;
    }

    /// <summary>Pivot on the left edge (same place on screen), so a width change grows the rect rightwards.</summary>
    static void PivotLeft(RectTransform rt)
    {
        rt.pivot = new Vector2(0f, rt.pivot.y);
        rt.anchoredPosition -= new Vector2(rt.sizeDelta.x * 0.5f, 0f);
    }

    // ------------------------------------------------------------------
    // Result: the newspaper clipping (510, 220, 900 x 640, rotated -1 deg)

    static void BuildResult(RectTransform page)
    {
        var view = page.gameObject.AddComponent<RiskResultView>();

        // hidden texts EventManager writes into
        var sources = Stretch(page, "EventManagerTexts");
        view.sourceTitle = Text(sources, "Title", 0f, 0f, 10f, 10f, "Viral Product!", 10f, UIColors.Ink);
        view.sourceDescription = Text(sources, "Description", 0f, 0f, 10f, 10f, "Your product went viral on social media! Massive sales incoming!", 10f, UIColors.Ink);
        view.sourceValue = Text(sources, "Value", 0f, 0f, 10f, 10f, "+$18,750", 10f, UIColors.Ink);
        sources.gameObject.SetActive(false);

        var sheet = Node(page, "Clipping", 510f, 220f, 900f, 640f, Anchor.Center, -1f);
        StretchShadow(sheet, 0f, 22f, 48f, 0.45f);
        StretchBox(sheet, "Fill", S("Kit/small_fill"), Newsprint);
        StretchBox(sheet, "Border", S("Kit/small_line3"), UIColors.Ink);
        Box(sheet, "TapeLeft", -46f, -12f, 150f, 38f, null, UIColors.Tape, Anchor.TopLeft, -32f);
        Box(sheet, "TapeRight", 900f + 46f - 150f, -12f, 150f, 38f, null, UIColors.Tape, Anchor.TopLeft, 30f);

        const float X = 43f, W = 814f;
        // masthead: 6px double rule, text row, 3px rule
        Box(sheet, "RuleTop1", X, 39f, W, 2f, null, UIColors.Ink);
        Box(sheet, "RuleTop2", X, 43f, W, 2f, null, UIColors.Ink);
        view.dateline = Text(sheet, "Dateline", X, 51f, 500f, 26.6f, "BREAKING · DAY 42", 19f, UIColors.InkSoft, letterSpacingEm: 0.08f);
        view.verdict = Text(sheet, "Verdict", X + W - 300f, 51f, 300f, 26.6f, "GOOD NEWS", 19f, UIColors.Money, align: TextAlignmentOptions.Right,
            letterSpacingEm: 0.08f);
        Box(sheet, "RuleBottom", X, 83.6f, W, 3f, null, UIColors.Ink);

        view.headline = Shrink(Text(sheet, "Headline", X, 104.6f, W, 69f, "Viral Product!", 60f, UIColors.Ink, bold: true), 40f);
        view.headline.overflowMode = TextOverflowModes.Ellipsis;   // after shrinking, for the longest translated headlines

        // photo spot + story
        var art = Node(sheet, "Art", X, 191.6f, 300f, 210f);
        AddImage(art.gameObject, S("Kit/small_fill"), UIColors.Dark(0.04f));
        Dashed(StretchBox(art, "Border", S("Kit/small_dash3"), UIColors.Pencil));
        view.artIcon = Icon(art, "Icon", 114f, 69f, 72f, "trend-up", UIColors.Money);
        view.goodArt = IconSprite("trend-up");
        view.badArt = IconSprite("trend-down");
        Box(sheet, "StoryRule", X + 330f, 191.6f, 2f, 210f, null, UIColors.Ink);
        view.story = Text(sheet, "Story", X + 360f, 191.6f, W - 360f, 210f,
            "Your product went viral on social media! Massive sales incoming!", 26f, UIColors.Ink,
            align: TextAlignmentOptions.TopLeft, lineHeight: 1.4f, wrap: true);

        // stamp (auto width), rotated -10 deg, 88% opacity
        var stamp = NewRect(sheet, "Stamp");
        stamp.anchorMin = stamp.anchorMax = new Vector2(0f, 1f);
        stamp.pivot = new Vector2(0.5f, 0.5f);
        float stampH = 54f * 1.15f + 12f + 10f;
        float stampW = TextWidth("+$18,750", 54f, true, 0.12f) + 44f + 10f;
        stamp.sizeDelta = new Vector2(stampW, stampH);
        stamp.anchoredPosition = new Vector2(X + stampW * 0.5f, -(419.6f + stampH * 0.5f));
        stamp.localEulerAngles = new Vector3(0f, 0f, 10f);
        HLayout(stamp.gameObject, 0f, Pad(27, 27, 11, 11), TextAnchor.MiddleCenter, true, true, false, false);
        Fit(stamp.gameObject, true, false);
        var group = stamp.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0.88f;
        group.blocksRaycasts = false;
        var s1 = StretchBox(stamp, "Border", S("Kit/r10_line5"), UIColors.Money);
        IgnoreLayout(s1);
        var s2 = StretchBox(stamp, "Outline", S("Kit/r14_line2"), UIColors.Money, -6f, -6f, -6f, -6f);
        IgnoreLayout(s2);
        view.stampLines = new[] { s1, s2 };
        view.stampText = LayoutText(stamp, "Amount", "+$18,750", 54f, UIColors.Money, bold: true, letterSpacingEm: 0.12f, lineHeight: 1.15f);

        view.notes = Text(sheet, "Notes", X, 419.6f + stampH + 40f, W, 57.2f,
            "CRITICAL SUCCESS! Rewards doubled.\nYou played: Balanced (65% good)", 19f, UIColors.InkSoft,
            align: TextAlignmentOptions.TopLeft, lineHeight: 1.4f, wrap: true);

        // "Ouch." sticky for bad news
        var ouch = Node(page, "Ouch", 1262f, 500f, 168f, 132f, Anchor.Center, 4f);
        StretchShadow(ouch, 0f, 12f, 20f, 0.28f);
        StretchBox(ouch, "Paper", null, UIColors.StickyPink);
        Box(ouch, "Band", 0f, 0f, 168f, 20f, null, UIColors.Dark(0.07f));
        Localize(Text(ouch, "Text", 0f, 20f, 168f, 112f, "Ouch.", 32f, UIColors.Ink, align: TextAlignmentOptions.Center), T, "hud.risk.result.ouch");
        view.ouchSticky = ouch.gameObject;
        ouch.gameObject.SetActive(false);

        // Continue (240 x 68 at 1130, 756)
        var cont = MarkerButtonNode(page, "ContinueButton", 1130f, 756f, 240f, 68f, Anchor.Center, UIColors.Highlight, new Vector2(5f, 6f),
            out var face, out var content);
        HLayout(content.gameObject, 12f, Pad(36, 36, 0, 0), TextAnchor.MiddleCenter, true, true, false, false);
        var label = Localize(LayoutText(content, "Label", "Continue", 28f, UIColors.Ink, bold: true), T, "hud.risk.result.continue");
        var arrow = LayoutIcon(content, "Icon", 28f, "arrow-right", UIColors.Ink, "b");
        SetupMarker(cont, face, new Graphic[] { label, arrow });
        // a longer word widens the button to the right (its left edge stays on the design)
        PivotLeft(cont);
        var fit = cont.gameObject.AddComponent<HudFitWidth>();
        fit.content = content;
        fit.minWidth = 240f;
    }

    // ------------------------------------------------------------------

    public static void Install(GameObject prefab, List<string> log)
    {
        Scene scene = SceneManager.GetSceneByPath(OfficeHudBuilder.ScenePath);
        if (!scene.IsValid() || !scene.isLoaded)
            scene = EditorSceneManager.OpenScene(OfficeHudBuilder.ScenePath, OpenSceneMode.Single);

        EventManager events = null;
        RiskCardsView existing = null;
        foreach (var r in scene.GetRootGameObjects())
        {
            if (events == null) events = r.GetComponentInChildren<EventManager>(true);
            if (existing == null) existing = r.GetComponentInChildren<RiskCardsView>(true);
        }
        if (events == null)
        {
            log.Add("MainGame has no EventManager.");
            return;
        }

        GameObject instance;
        if (existing == null)
        {
            instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.name = "RiskEvent";
            log.Add("MainGame: added the RiskEvent prefab.");
        }
        else
        {
            instance = existing.transform.parent.gameObject;
            log.Add("MainGame: RiskEvent already there (the prefab update applies to it).");
        }
        instance.SetActive(false);

        var cards = instance.GetComponentInChildren<RiskCardsView>(true);
        var result = instance.GetComponentInChildren<RiskResultView>(true);
        var so = new SerializedObject(events);
        so.FindProperty("eventPanel").objectReferenceValue = instance;
        so.FindProperty("cardSelectionPanel").objectReferenceValue = cards.gameObject;
        so.FindProperty("resultPanel").objectReferenceValue = result.gameObject;
        so.FindProperty("highRiskCard").objectReferenceValue = cards.highRisk.button;
        so.FindProperty("balancedCard").objectReferenceValue = cards.balanced.button;
        so.FindProperty("lowRiskCard").objectReferenceValue = cards.lowRisk.button;
        so.FindProperty("highRiskText").objectReferenceValue = null;
        so.FindProperty("balancedText").objectReferenceValue = null;
        so.FindProperty("lowRiskText").objectReferenceValue = null;
        so.FindProperty("resultTitleText").objectReferenceValue = result.sourceTitle;
        so.FindProperty("resultDescriptionText").objectReferenceValue = result.sourceDescription;
        so.FindProperty("resultValueText").objectReferenceValue = result.sourceValue;
        so.FindProperty("continueButton").objectReferenceValue = instance.transform.Find("Result/ContinueButton").GetComponent<Button>();
        so.FindProperty("goodResultColor").colorValue = UIColors.Money;
        so.FindProperty("badResultColor").colorValue = UIColors.Loss;
        so.FindProperty("applyTextFixes").boolValue = false;
        so.ApplyModifiedPropertiesWithoutUndo();
        log.Add("MainGame: EventManager -> RiskEvent (cards, result, Continue).");
        HideLegacyEventPanels(scene, instance, log);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        log.Add("MainGame saved.");
    }

    /// <summary>
    /// The old Canvas/Event Panel is saved ACTIVE: EventManager used to hide it in Awake through its eventPanel field,
    /// which now points at RiskEvent, so nothing hid it and it stayed on screen with the old cards. Switch it off.
    /// </summary>
    static void HideLegacyEventPanels(Scene scene, GameObject designed, List<string> log)
    {
        int hidden = 0;
        foreach (var r in scene.GetRootGameObjects())
        {
            foreach (var t in r.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != "Event Panel" || !t.gameObject.activeSelf) continue;
                if (designed != null && t.IsChildOf(designed.transform)) continue;
                t.gameObject.SetActive(false);
                hidden++;
            }
        }
        log.Add(hidden > 0 ? "MainGame: switched off the old Event Panel (it sat on screen with the old cards)."
                           : "MainGame: the old Event Panel was already off.");
    }

    [MenuItem("Tools/Game/New UI/Hide Old Event Panel")]
    public static void HideOldEventPanelFromMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Hide Old Event Panel", "Stop Play mode first (changes made while playing aren't kept).", "OK");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Scene scene = SceneManager.GetSceneByPath(OfficeHudBuilder.ScenePath);
        if (!scene.IsValid() || !scene.isLoaded)
            scene = EditorSceneManager.OpenScene(OfficeHudBuilder.ScenePath, OpenSceneMode.Single);
        GameObject designed = null;
        foreach (var r in scene.GetRootGameObjects())
        {
            var cards = r.GetComponentInChildren<RiskCardsView>(true);
            if (cards != null) { designed = cards.transform.parent.gameObject; break; }
        }
        var log = new List<string>();
        HideLegacyEventPanels(scene, designed, log);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        log.Add("MainGame saved.");
        Debug.Log("[RiskEventBuilder]\n" + string.Join("\n", log));
        EditorUtility.DisplayDialog("Risk event", string.Join("\n", log), "OK");
    }
}
