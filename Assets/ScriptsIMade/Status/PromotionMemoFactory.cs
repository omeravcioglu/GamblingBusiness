using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Builds PromotionMemoView's screen at runtime when no designed one is assigned, following the design "Status upgrade:
/// promotion moment" (1920 x 1080): a light scrim and the 1040 x 780 memo, placed like the design's CSS (x and y from
/// the memo's top-left corner, its 3 px border included, y down). Fills in the view's references, so the view works
/// the same way it will with a designed prefab.
/// The fixed texts (INTERNAL MEMO, the routing line, the sign-off, Nice, PROMOTED) are L10nText components on the
/// "Status" String Table, so they follow language changes by themselves; the view fills in the rest.
/// </summary>
public static class PromotionMemoFactory
{
    const float MemoWidth = 1040f;
    const float MemoHeight = 780f;
    const float Left = 51f;             // 3 px border + 48 px padding
    const float ContentWidth = 938f;
    const float ContentBottom = 737f;   // above the 40 px bottom padding

    static readonly Color Navy = UIColors.Hex(0x22385C);
    static readonly Color Brass = UIColors.Hex(0xC2A04A);
    static readonly Color ClipBrass = UIColors.Hex(0x7D6424);
    static readonly Color ClipShine = UIColors.Hex(0xE3C981);

    // Corner radii (top-left, top-right, bottom-right, bottom-left): the design's elliptical radii, averaged
    static readonly Vector4 PaperRadii = new Vector4(23f, 22f, 23f, 25f);
    static readonly Vector4 ButtonRadii = new Vector4(17f, 16f, 17f, 18f);
    static readonly Vector4 BarRadii = new Vector4(12f, 11f, 12f, 13f);

    // The Board's signature (SVG path in a 190 x 52 box) as cubic Bezier segments: the start point, then
    // (control, control, end) for each segment
    static readonly Vector2[] SignatureScribble =
    {
        new Vector2(12f, 38f),
        new Vector2(22f, 18f), new Vector2(30f, 8f), new Vector2(34f, 12f),
        new Vector2(38f, 16f), new Vector2(26f, 38f), new Vector2(32f, 36f),
        new Vector2(38f, 34f), new Vector2(46f, 8f), new Vector2(52f, 10f),
        new Vector2(58f, 12f), new Vector2(48f, 32f), new Vector2(54f, 32f),
        new Vector2(60f, 32f), new Vector2(64f, 18f), new Vector2(70f, 18f),
        new Vector2(76f, 18f), new Vector2(72f, 30f), new Vector2(78f, 30f),
        new Vector2(84f, 30f), new Vector2(90f, 12f), new Vector2(98f, 14f),
        new Vector2(106f, 16f), new Vector2(96f, 28f), new Vector2(104f, 28f),
        new Vector2(112f, 28f), new Vector2(120f, 18f), new Vector2(130f, 16f),
        new Vector2(140f, 14f), new Vector2(150f, 18f), new Vector2(160f, 14f)
    };
    static readonly Vector2[] SignatureUnderline =
    {
        new Vector2(40f, 44f), new Vector2(70f, 40f), new Vector2(110f, 38f), new Vector2(160f, 40f)
    };

    /// <summary>Build the memo on a new overlay canvas and assign the view's references. Returns that canvas.</summary>
    public static Canvas Build(PromotionMemoView view, TMP_FontAsset font)
    {
        Canvas canvas = CreateCanvas(view);
        EnsureEventSystem();

        // Built inactive, so every component initialises with its references already set
        RectTransform root = StatusUI.NewRect("PromotionMemo (Runtime)", canvas.transform);
        root.gameObject.SetActive(false);
        StatusUI.Stretch(root);
        view.root = root;

        // A light scrim: a celebration, though the game waits underneath
        Image scrim = StatusUI.NewRect("Scrim", root).gameObject.AddComponent<Image>();
        scrim.color = UIColors.ScrimLight;
        StatusUI.Stretch(scrim.rectTransform);

        RectTransform memo = StatusUI.NewRect("Memo", root);
        memo.anchorMin = memo.anchorMax = memo.pivot = new Vector2(0.5f, 0.5f);
        memo.anchoredPosition = Vector2.zero;
        memo.sizeDelta = new Vector2(MemoWidth, MemoHeight);
        StatusBox paper = memo.gameObject.AddComponent<StatusBox>();
        paper.SetStyle(UIColors.PaperHi, UIColors.Ink, 3f);
        paper.Radii = PaperRadii;
        paper.SetShadow(UIColors.Dark(0.45f), new Vector2(0f, 22f), 48f);
        view.memo = memo;

        BuildPaperClip(memo);
        BuildLetterhead(view, memo, font);
        BuildMessage(view, memo, font);
        BuildPictures(view, memo, font);
        BuildNextTier(view, memo, font);
        BuildSignOff(view, memo, font);
        view.closeButton = BuildCloseButton(memo);
        view.stamp = BuildStamp(memo, font);
        return canvas;
    }

    // ------------------------------------------------------------------
    // Sections (top to bottom)
    // ------------------------------------------------------------------

    static void BuildPaperClip(RectTransform memo)
    {
        // Over the top edge: the 24 x 60 drawing at 1.25x, tilted 4 degrees counter-clockwise
        RectTransform clip = StatusUI.PlaceRotated(StatusUI.NewRect("PaperClip", memo), 473f, -27f, 30f, 76f, 4f);
        Vector2[] path = StatusUI.PaperClip(1.25f);
        StatusUI.Stroke(clip, "Brass", ClipBrass, 4.2f * 1.25f, path);
        StatusUI.Stroke(clip, "Shine", ClipShine, 2f * 1.25f, path);
    }

    static void BuildLetterhead(PromotionMemoView view, RectTransform memo, TMP_FontAsset font)
    {
        // Company mark: a navy disc with a paper-white sparkle
        StatusBox mark = StatusUI.Box(memo, "Mark", Navy, UIColors.Ink, 2f, Vector4.one * 24f);
        StatusUI.Place(mark.rectTransform, Left, 35f, 48f, 48f);
        StatusUI.Star(mark.rectTransform, "Sparkle", UIColors.PaperHi, 18f);

        TextMeshProUGUI company = StatusUI.Text(memo, "Company", "", 32f, Navy, font, TextAlignmentOptions.Left, true);
        company.characterSpacing = 12f;
        company.overflowMode = TextOverflowModes.Ellipsis;
        StatusUI.AutoSize(company, 22f, 32f);
        StatusUI.Place(company.rectTransform, Left + 64f, 35f, 610f, 48f);
        view.letterheadText = company;

        // Right-aligned, 64 px short of the close button's column
        TextMeshProUGUI memoLabel = StatusUI.Text(memo, "InternalMemo", "", 19f, UIColors.Pencil, font, TextAlignmentOptions.Right);
        memoLabel.characterSpacing = 12f;
        StatusUI.Place(memoLabel.rectTransform, 625f, 35f, 300f, 48f);
        L10nText.Attach(memoLabel, StatusLadder.Table, "status.memo.kicker");   // "INTERNAL MEMO"

        // Letterhead rule: navy over a thin brass line
        StatusUI.Place(StatusUI.Block(memo, "RuleNavy", Navy).rectTransform, Left, 101f, ContentWidth, 3f);
        StatusUI.Place(StatusUI.Block(memo, "RuleBrass", Brass).rectTransform, Left, 106f, ContentWidth, 1f);

        // TO The CEO   FROM The Board   RE Status upgrade
        RectTransform routing = StatusUI.Place(StatusUI.NewRect("Routing", memo), Left, 125f, ContentWidth, 26.6f);
        Row(routing, 28f);
        RoutingField(routing, "To", "status.memo.to", font);
        RoutingField(routing, "From", "status.memo.from", font);
        RoutingField(routing, "Re", "status.memo.re", font);
    }

    /// <summary>"TO The CEO": the entry's sentence, with its label (key + ".label") in small grey spaced capitals.</summary>
    static void RoutingField(RectTransform row, string name, string key, TMP_FontAsset font)
    {
        TextMeshProUGUI text = StatusUI.Text(row, name, "", 19f, UIColors.Ink, font);
        L10nText.Attach(text, StatusLadder.Table, key, () => new object[]
        {
            L10n.Args(("label", "<color=#676B74><cspace=0.08em>" + L10n.Get(StatusLadder.Table, key + ".label") + "</cspace></color>"))
        });
    }

    static void BuildMessage(PromotionMemoView view, RectTransform memo, TMP_FontAsset font)
    {
        TextMeshProUGUI headline = StatusUI.Text(memo, "Headline", "", 44f, UIColors.Ink, font, TextAlignmentOptions.Left, true);
        headline.overflowMode = TextOverflowModes.Ellipsis;
        StatusUI.AutoSize(headline, 30f, 44f);
        StatusUI.Place(headline.rectTransform, Left, 169.6f, ContentWidth, 50.6f);
        view.headlineText = headline;

        // These two lines stay left of the PROMOTED stamp
        TextMeshProUGUI passed = StatusUI.Text(memo, "Passed", "", 26f, UIColors.Ink, font);
        StatusUI.Place(passed.rectTransform, Left, 238.2f, 580f, 36.4f);
        passed.gameObject.AddComponent<TextHighlighter>();   // the amount is marked with the yellow highlighter
        view.passedText = passed;

        TextMeshProUGUI whatsNew = StatusUI.Text(memo, "WhatsNew", "", 26f, UIColors.Ink, font);
        StatusUI.AutoSize(whatsNew, 19f, 26f);
        StatusUI.Place(whatsNew.rectTransform, Left, 292.6f, 580f, 36.4f);
        view.whatsNewText = whatsNew;
    }

    static void BuildPictures(PromotionMemoView view, RectTransform memo, TMP_FontAsset font)
    {
        const float top = 347f;
        view.beforePicture = Picture(memo, "BeforePicture", Left, top, font);
        view.afterPicture = Picture(memo, "AfterPicture", Left + 480f, top, font);
        view.beforeCaption = PictureCaption(memo, "BeforeCaption", Left, top + 170f, font);
        view.afterCaption = PictureCaption(memo, "AfterCaption", Left + 480f, top + 170f, font);

        // Arrow from before to after: a 24-unit icon drawn 48 px wide
        RectTransform arrow = StatusUI.Place(StatusUI.NewRect("Arrow", memo), Left + 416f, top + 58f, 48f, 48f);
        UIPolyline line = StatusUI.Stroke(arrow, "Line", UIColors.Ink, 5.2f, new Vector2(8f, 24f), new Vector2(38f, 24f));
        line.AddPath(StatusUI.Points(new Vector2(26f, 12f), new Vector2(38f, 24f), new Vector2(26f, 36f)));
    }

    static StatusLedgerCorner Picture(RectTransform memo, string name, float x, float y, TMP_FontAsset font)
    {
        RectTransform rect = StatusUI.Place(StatusUI.NewRect(name, memo), x, y, 400f, 164f);
        StatusLedgerCorner picture = rect.gameObject.AddComponent<StatusLedgerCorner>();
        picture.font = font;
        picture.surroundColor = UIColors.PaperHi;
        return picture;
    }

    static TextMeshProUGUI PictureCaption(RectTransform memo, string name, float x, float y, TMP_FontAsset font)
    {
        TextMeshProUGUI caption = StatusUI.Text(memo, name, "", 19f, UIColors.InkSoft, font);
        caption.overflowMode = TextOverflowModes.Ellipsis;
        StatusUI.Place(caption.rectTransform, x, y, 400f, 26.6f);
        return caption;
    }

    static void BuildNextTier(PromotionMemoView view, RectTransform memo, TMP_FontAsset font)
    {
        TextMeshProUGUI next = StatusUI.Text(memo, "Next", "", 22f, UIColors.Ink, font);
        StatusUI.Place(next.rectTransform, Left, 563f, ContentWidth, 30.8f);
        view.nextText = next;

        // Bar, percent, "$value of $next"
        RectTransform row = StatusUI.Place(StatusUI.NewRect("Progress", memo), Left, 601.8f, ContentWidth, 28.6f);
        Row(row, 16f);
        view.progressGroup = row.gameObject;

        StatusBox bar = StatusUI.Box(row, "Bar", UIColors.PaperHi, UIColors.Ink, 2f, BarRadii);
        LayoutElement size = bar.gameObject.AddComponent<LayoutElement>();
        size.minWidth = size.preferredWidth = 520f;
        size.minHeight = size.preferredHeight = 18f;
        RectTransform track = StatusUI.NewRect("Track", bar.rectTransform);
        StatusUI.Stretch(track, 2f, 2f, 2f, 2f);
        StatusBox fill = StatusUI.Box(track, "Fill", UIColors.Highlight, Color.clear, 0f, Vector4.one * 7f);
        RectTransform fillRect = fill.rectTransform;
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = new Vector2(0f, 1f);   // the view sets the right anchor to the progress
        fillRect.pivot = new Vector2(0f, 0.5f);
        fillRect.offsetMin = fillRect.offsetMax = Vector2.zero;
        view.progressFill = fillRect;

        view.progressPercentText = StatusUI.Text(row, "Percent", "0%", 22f, UIColors.Ink, font, TextAlignmentOptions.Left, true);
        view.progressValueText = StatusUI.Text(row, "Amount", "", 19f, UIColors.InkSoft, font);
    }

    static void BuildSignOff(PromotionMemoView view, RectTransform memo, TMP_FontAsset font)
    {
        RectTransform signature = StatusUI.Place(StatusUI.NewRect("Signature", memo), Left, 654.4f, 170f, 46f);
        UIPolyline ink = StatusUI.Stroke(signature, "Ink", UIColors.Ink, 2.1f, SignaturePoints(SignatureScribble));
        ink.AddPath(StatusUI.Points(SignaturePoints(SignatureUnderline)));
        StatusUI.Place(StatusUI.Block(memo, "SignatureLine", UIColors.Ink).rectTransform, Left, 704.4f, 170f, 2f);
        TextMeshProUGUI signed = StatusUI.Text(memo, "Signed", "", 19f, UIColors.Pencil, font);
        StatusUI.Place(signed.rectTransform, Left, 710.4f, 300f, 26.6f);
        L10nText.Attach(signed, StatusLadder.Table, "status.memo.signed");   // "Signed, the Board"

        view.niceButton = BuildNiceButton(memo, font);
    }

    /// <summary>Points along the signature's Bezier segments, fitted from its 190 x 52 box into 170 x 46.</summary>
    static Vector2[] SignaturePoints(Vector2[] segments)
    {
        const float scale = 46f / 52f;                    // the height decides (SVG "meet")
        var offset = new Vector2((170f - 190f * scale) * 0.5f, 0f);
        var points = new List<Vector2> { segments[0] };
        for (int i = 0; i + 3 < segments.Length; i += 3)
            UIPolyline.AppendCubic(points, segments[i], segments[i + 1], segments[i + 2], segments[i + 3], 10);
        for (int i = 0; i < points.Count; i++)
            points[i] = points[i] * scale + offset;
        return points.ToArray();
    }

    static Button BuildNiceButton(RectTransform memo, TMP_FontAsset font)
    {
        // Bottom-right of the memo; as wide as its label plus 36 px on each side (137 px in the design)
        const float height = 68f;
        RectTransform rect = StatusUI.NewRect("NiceButton", memo);
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(Left + ContentWidth, -(ContentBottom - height));
        rect.sizeDelta = new Vector2(137f, height);

        RectTransform face = MarkerParts(rect, UIColors.Highlight, 3f, ButtonRadii, new Vector2(5f, 6f));
        TextMeshProUGUI label = StatusUI.Text(face, "Label", "", 28f, UIColors.Ink, font, TextAlignmentOptions.Center, true);
        StatusUI.Stretch(label.rectTransform);
        L10nText.Attach(label, StatusLadder.Table, "status.memo.nice");   // "Nice"
        FitWidth(rect, label, 72f);
        return rect.GetComponent<Button>();
    }

    static Button BuildCloseButton(RectTransform memo)
    {
        RectTransform rect = StatusUI.Place(StatusUI.NewRect("CloseButton", memo), 953f, 27f, 60f, 60f);
        RectTransform face = MarkerParts(rect, UIColors.PaperHi, 3f, Vector4.one * 30f, new Vector2(3f, 4f));
        // X: a 24-unit icon drawn 28 px wide
        RectTransform icon = StatusUI.Place(StatusUI.NewRect("Icon", face), 16f, 16f, 28f, 28f);
        float s = 28f / 24f;
        UIPolyline cross = StatusUI.Stroke(icon, "Cross", UIColors.Ink, 2.4f * s, new Vector2(6f, 6f) * s, new Vector2(18f, 18f) * s);
        cross.AddPath(StatusUI.Points(new Vector2(18f, 6f) * s, new Vector2(6f, 18f) * s));
        return rect.GetComponent<Button>();
    }

    static RectTransform BuildStamp(RectTransform memo, TMP_FontAsset font)
    {
        // 327 x 72.6 in the design (44 px text at line-height 1.15, 6 / 22 px padding, 5 px border), right edge 67 px
        // from the memo's; it fits its text around its centre and turns 10 degrees counter-clockwise
        const float width = 327f;
        const float height = 72.6f;
        RectTransform stamp = StatusUI.PlaceRotated(StatusUI.NewRect("Stamp", memo), 973f - width, 245f, width, height, 10f);
        TextMeshProUGUI text = StatusUI.Text(stamp, "Text", "", 44f, UIColors.Money, font, TextAlignmentOptions.Center, true);
        text.characterSpacing = 12f;
        StatusUI.Stretch(text.rectTransform, 27f, 27f, 11f, 11f);
        L10nText.Attach(text, StatusLadder.Table, "status.memo.stamp");   // "PROMOTED", written in capitals in every language
        FitWidth(stamp, text, 54f);

        // Rubber stamp: a thick green border with a thin line around it
        StatusBox ring = stamp.gameObject.AddComponent<StatusBox>();
        ring.SetStyle(Color.clear, UIColors.Money, 5f);
        ring.Radii = Vector4.one * 10f;
        ring.SetOutline(UIColors.Money, 2f, 4f);
        ring.raycastTarget = false;

        CanvasGroup group = stamp.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0.88f;   // the view fades the stamp in up to this
        group.blocksRaycasts = false;
        group.interactable = false;
        return stamp;
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// A marker-pen button on <paramref name="rect"/>: a hard ink shadow and a face that lifts on hover and presses into
    /// the shadow (MarkerButton). Returns the face, for the button's content.
    /// </summary>
    static RectTransform MarkerParts(RectTransform rect, Color fill, float borderWidth, Vector4 radii, Vector2 shadowOffset)
    {
        StatusBox shadow = StatusUI.Box(rect, "Shadow", UIColors.Ink, Color.clear, 0f, radii);
        StatusUI.Stretch(shadow.rectTransform);
        shadow.rectTransform.anchoredPosition = new Vector2(shadowOffset.x, -shadowOffset.y);

        StatusBox face = StatusUI.Box(rect, "Face", fill, UIColors.Ink, borderWidth, radii);
        StatusUI.Stretch(face.rectTransform);
        face.raycastTarget = true;   // takes the button's clicks

        Button button = rect.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;   // MarkerButton draws the states
        button.navigation = new Navigation { mode = Navigation.Mode.None };

        MarkerButton marker = rect.gameObject.AddComponent<MarkerButton>();
        marker.face = face.rectTransform;
        marker.shadow = shadow.gameObject;
        marker.pressOffset = new Vector2(shadowOffset.x, -shadowOffset.y);   // pressed right into the shadow
        return face.rectTransform;
    }

    /// <summary>Keep <paramref name="rect"/> as wide as the text plus <paramref name="padding"/> (measured once TMP is awake).</summary>
    static void FitWidth(RectTransform rect, TMP_Text text, float padding)
    {
        StatusFitWidth fit = rect.gameObject.AddComponent<StatusFitWidth>();
        fit.text = text;
        fit.padding = padding;
    }

    static void Row(RectTransform rect, float spacing)
    {
        HorizontalLayoutGroup row = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.spacing = spacing;
        row.childAlignment = TextAnchor.MiddleLeft;
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = false;
    }

    static Canvas CreateCanvas(PromotionMemoView view)
    {
        var go = new GameObject("PromotionMemoCanvas (Runtime)", typeof(RectTransform));
        int uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer >= 0) go.layer = uiLayer;
        // Beside the view so it goes away with it; at the scene root if the view sits inside another canvas
        if (view.GetComponentInParent<Canvas>(true) == null)
            go.transform.SetParent(view.transform, false);

        Canvas canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = view.sortingOrder;
        CanvasScaler scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        go.AddComponent<GraphicRaycaster>();
        return canvas;
    }

    static void EnsureEventSystem()
    {
        if (EventSystem.current != null || Object.FindAnyObjectByType<EventSystem>() != null) return;
        new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
    }
}
