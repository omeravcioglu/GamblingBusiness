using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds TutorialView's screen at runtime from the design "First-day tour" (1920 x 1080 reference pixels, x and y from a
/// parent's top-left corner, y down, like the design's CSS): the click blocker, the scrim with its hole (TutorialScrim), the
/// dashed ring, the arrow, the sticky note and the memo paper, plus the small pictures of some memos (BuildExample). Sizes
/// and places that depend on the words are set by the view's layout. Uses the status kit (StatusBox, StatusUI), the board
/// kit (BoardUI) and the UI kit (UIColors, UIShape, MarkerButton), so it looks like the promotion memo and the board memo.
/// Every text gets L10nFont (the language's font); the view writes the words.
/// </summary>
public static class TutorialViewFactory
{
    /// <summary>The design's scrim: rgba(16, 13, 8, 0.72), as dark as the browser draws it (linear colour space).</summary>
    static readonly Color ScrimColor = new Color(16f / 255f, 13f / 255f, 8f / 255f, UIColors.SrgbAlpha(0.72f));
    static readonly Color WhiteboardFill = UIColors.Hex(0xFBFBF8);
    static readonly Color WhiteboardFrame = UIColors.Hex(0xB7BCC3);
    static readonly Color WhiteboardLine = UIColors.Hex(0x8E949C);
    static readonly Color PinRed = UIColors.Hex(0xD8413A);
    static readonly Color PinShine = UIColors.Hex(0xF08A84);

    // The Board's signature on the design's memo (SVG path in a 170 x 44 box) as cubic Bezier segments: the start point,
    // then (control, control, end) for each segment
    static readonly Vector2[] Signature =
    {
        new Vector2(6f, 32f),
        new Vector2(16f, 10f), new Vector2(24f, 8f), new Vector2(26f, 20f),
        new Vector2(28f, 32f), new Vector2(20f, 36f), new Vector2(28f, 28f),
        new Vector2(36f, 20f), new Vector2(42f, 8f), new Vector2(46f, 16f),
        new Vector2(50f, 24f), new Vector2(40f, 32f), new Vector2(52f, 26f),
        new Vector2(64f, 20f), new Vector2(68f, 10f), new Vector2(78f, 12f),
        new Vector2(88f, 14f), new Vector2(76f, 26f), new Vector2(90f, 22f),
        new Vector2(104f, 18f), new Vector2(110f, 8f), new Vector2(124f, 10f),
        new Vector2(138f, 12f), new Vector2(138f, 14f), new Vector2(158f, 12f)
    };

    /// <summary>Build the screen on a new overlay canvas beside the view and fill in the view's references.</summary>
    public static void Build(TutorialView view, TMP_FontAsset font)
    {
        Canvas canvas = BoardUI.CreateCanvas("TutorialCanvas (Runtime)", view.sortingOrder, view.transform, true);
        view.canvas = canvas;

        // Built inactive, so every component initialises with its references already set
        RectTransform root = StatusUI.NewRect("Tutorial (Runtime)", canvas.transform);
        root.gameObject.SetActive(false);
        StatusUI.Stretch(root);
        view.root = root;
        view.rootGroup = root.gameObject.AddComponent<CanvasGroup>();

        // The tour is modal: clicks stop here, also inside the spotlight's hole
        StatusUI.Stretch(BoardUI.Block(root, "Blocker", Color.clear, true).rectTransform);

        TutorialScrim scrim = StatusUI.NewRect("Scrim", root).gameObject.AddComponent<TutorialScrim>();
        StatusUI.Stretch(scrim.rectTransform);
        scrim.color = ScrimColor;
        scrim.raycastTarget = false;
        view.scrim = scrim;

        // outline: 4px dashed #FFE14D; outline-offset: 6px (the view sizes it around the hole)
        RectTransform ringRect = StatusUI.NewRect("Ring", root);
        UIShape ring = ringRect.gameObject.AddComponent<UIShape>();
        ring.color = UIColors.Highlight;
        ring.border = 4f;
        ring.dash = 12f;
        ring.gap = 8f;
        ring.raycastTarget = false;
        ring.Round(24f);
        view.ring = ring;
        ringRect.gameObject.SetActive(false);

        // The arrow from the note to the spot: paper-white 11 px under ink 4.5 px (drawn under the note)
        RectTransform arrow = StatusUI.NewRect("Arrow", root);
        StatusUI.Stretch(arrow);
        arrow.pivot = new Vector2(0f, 1f);   // local (0, 0) = the top-left corner
        view.arrowUnder = Stroke(arrow, "Under", UIColors.PaperHi, 11f);
        view.arrowInk = Stroke(arrow, "Ink", UIColors.Ink, 4.5f);
        view.arrowLayer = arrow;
        arrow.gameObject.SetActive(false);

        BuildNote(view, root, font);
        BuildMemo(view, root, font);

        L10nFont.AttachAll(root);
    }

    // ------------------------------------------------------------------
    // The sticky note of a spotlight step
    // ------------------------------------------------------------------

    static void BuildNote(TutorialView view, RectTransform root, TMP_FontAsset font)
    {
        RectTransform note = StatusUI.NewRect("Note", root);
        StatusUI.PlaceRotated(note, 0f, 0f, 440f, 320f, 0f);
        StatusBox paper = note.gameObject.AddComponent<StatusBox>();
        paper.SetStyle(UIColors.StickyYellow, Color.clear, 0f);
        paper.Radii = Vector4.zero;
        paper.SetShadow(UIColors.Dark(0.35f), new Vector2(0f, 14f), 24f);
        paper.raycastTarget = true;
        view.note = note;

        // The sticky strip along the top: 22 px, 7% darker
        Image band = BoardUI.Block(note, "Band", UIColors.Dark(0.07f));
        RectTransform bandRect = band.rectTransform;
        bandRect.anchorMin = new Vector2(0f, 1f);
        bandRect.anchorMax = new Vector2(1f, 1f);
        bandRect.pivot = new Vector2(0.5f, 1f);
        bandRect.anchoredPosition = Vector2.zero;
        bandRect.sizeDelta = new Vector2(0f, 22f);

        view.noteLabel = Text(note, "Label", 17f, UIColors.InkSoft, font, false, 10f);
        view.noteTitle = Text(note, "Title", 32f, UIColors.Ink, font, true, 0f);
        view.noteBody = Text(note, "Body", 22f, UIColors.Ink, font, false, 0f);
        view.dotsRow = StatusUI.NewRect("Dots", note);

        // Skip tour: a plain link with a wavy underline
        RectTransform skip = StatusUI.NewRect("SkipTour", note);
        Image hit = skip.gameObject.AddComponent<Image>();
        hit.color = Color.clear;
        Button skipButton = skip.gameObject.AddComponent<Button>();
        skipButton.transition = Selectable.Transition.None;
        skipButton.targetGraphic = hit;
        skipButton.navigation = new Navigation { mode = Navigation.Mode.None };
        skipButton.onClick.AddListener(view.ClickSecondary);
        view.skipLink = skip;
        view.skipLabel = Text(skip, "Label", 22f, UIColors.Ink, font, false, 0f);
        view.skipLabel.textWrappingMode = TextWrappingModes.NoWrap;
        view.skipWaveRect = StatusUI.NewRect("Wave", skip);
        view.skipWave = view.skipWaveRect.gameObject.AddComponent<UIShape>();
        view.skipWave.color = UIColors.Ink;
        view.skipWave.strokeWidth = 2f;
        view.skipWave.raycastTarget = false;
        view.skipWave.kind = UIShape.Kind.Stroke;   // nothing drawn until the view gives it its wave

        // Next: paper-white marker button with an arrow
        view.nextButton = StatusUI.NewRect("Next", note);
        Button next = BoardUI.MakeMarkerButton(view.nextButton, UIColors.PaperHi, 3f, BoardUI.ButtonRadii, new Vector2(4f, 5f), out StatusBox nextFace);
        next.onClick.AddListener(view.ClickPrimary);
        view.nextFace = nextFace.rectTransform;
        view.nextLabel = ButtonLabel(view.nextFace, 22f, UIColors.Ink, font, true);
        view.nextIcon = Icon(view.nextFace, "Arrow", 24f, UIColors.Ink, 2.6f, ArrowIcon);

        // The last step: "That's the job." and the green Let's hire someone with the stamp
        view.doneNote = Text(note, "DoneNote", 19f, UIColors.InkSoft, font, false, 0f);
        view.doneNote.textWrappingMode = TextWrappingModes.NoWrap;
        view.doneButton = StatusUI.NewRect("LetsHire", note);
        Button done = BoardUI.MakeMarkerButton(view.doneButton, UIColors.Money, 3f, BoardUI.ButtonRadii, new Vector2(4f, 5f), out StatusBox doneFace);
        done.onClick.AddListener(view.ClickDone);
        view.doneFace = doneFace.rectTransform;
        view.doneIcon = Icon(view.doneFace, "Stamp", 24f, UIColors.PaperHi, 2.4f, StampIcon);
        view.doneLabel = ButtonLabel(view.doneFace, 22f, UIColors.PaperHi, font, true);

        note.gameObject.SetActive(false);
    }

    /// <summary>One of the note's step dots (the view sizes and colours it).</summary>
    public static StatusBox Dot(RectTransform row, int index)
    {
        StatusBox dot = StatusUI.Box(row, "Dot" + (index + 1), Color.clear, UIColors.Ink, 2f, Vector4.one * 7f);
        StatusUI.Place(dot.rectTransform, index * 22f, 2f, 14f, 14f);
        return dot;
    }

    // ------------------------------------------------------------------
    // The memo paper (the welcome memo and the feature memos)
    // ------------------------------------------------------------------

    static void BuildMemo(TutorialView view, RectTransform root, TMP_FontAsset font)
    {
        RectTransform memo = StatusUI.NewRect("Memo", root);
        StatusUI.PlaceRotated(memo, 0f, 0f, 880f, 460f, 0f);
        StatusBox paper = memo.gameObject.AddComponent<StatusBox>();
        paper.SetStyle(UIColors.Paper, UIColors.Ink, 3f);
        paper.Radii = BoardUI.PaperRadii;
        paper.SetShadow(UIColors.Dark(0.45f), new Vector2(0f, 22f), 48f);
        paper.raycastTarget = true;   // clicks on the paper stop here
        view.memo = memo;

        // Masking tape over the top edge: 160 x 38, 18 px above it, turned 3 degrees counter-clockwise
        RectTransform tape = StatusUI.NewRect("Tape", memo);
        tape.anchorMin = tape.anchorMax = new Vector2(0.5f, 1f);
        tape.pivot = new Vector2(0.5f, 0.5f);
        tape.anchoredPosition = new Vector2(0f, -1f);
        tape.sizeDelta = new Vector2(160f, 38f);
        tape.localEulerAngles = new Vector3(0f, 0f, 3f);
        StatusBox tapeBox = tape.gameObject.AddComponent<StatusBox>();
        tapeBox.SetStyle(UIColors.Tape, Color.clear, 0f);
        tapeBox.Radii = Vector4.zero;
        tapeBox.SetShadow(UIColors.Dark(0.18f), new Vector2(0f, 2f), 4f);
        tapeBox.raycastTarget = false;

        view.memoKicker = Text(memo, "Kicker", 17f, UIColors.Pencil, font, false, 14f);
        view.memoTo = Text(memo, "To", 19f, UIColors.Pencil, font, false, 0f);
        view.ruleTop = BoardUI.Block(memo, "RuleTop", UIColors.Ink).rectTransform;
        view.ruleBottom = BoardUI.Block(memo, "RuleBottom", UIColors.Ink).rectTransform;
        view.memoTitle = Text(memo, "Title", 60f, UIColors.Ink, font, true, 0f);
        view.memoBody = Text(memo, "Body", 26f, UIColors.InkSoft, font, false, 0f);
        view.memoBody.gameObject.AddComponent<TextHighlighter>();   // the welcome memo's money gets the highlighter mark
        view.exampleHost = StatusUI.NewRect("Example", memo);

        // Show me around (yellow, with an arrow) / Got it
        view.primaryButton = StatusUI.NewRect("Primary", memo);
        Button primary = BoardUI.MakeMarkerButton(view.primaryButton, UIColors.Highlight, 3f, BoardUI.ButtonRadii, new Vector2(5f, 6f), out StatusBox primaryFace);
        primary.onClick.AddListener(view.ClickPrimary);
        view.primaryFace = primaryFace.rectTransform;
        view.primaryLabel = ButtonLabel(view.primaryFace, 28f, UIColors.Ink, font, true);
        view.primaryIcon = Icon(view.primaryFace, "Arrow", 28f, UIColors.Ink, 2.4f, ArrowIcon);

        // Skip the tour (paper-white)
        view.secondaryButton = StatusUI.NewRect("Secondary", memo);
        Button secondary = BoardUI.MakeMarkerButton(view.secondaryButton, UIColors.PaperHi, 3f, BoardUI.ButtonRadii, new Vector2(4f, 5f), out StatusBox secondaryFace);
        secondary.onClick.AddListener(view.ClickSecondary);
        view.secondaryFace = secondaryFace.rectTransform;
        view.secondaryLabel = ButtonLabel(view.secondaryFace, 28f, UIColors.Ink, font, false);

        // The Board's blue signature and "The Board" under it
        view.signature = StatusUI.NewRect("Signature", memo);
        StatusUI.Place(view.signature, 0f, 0f, 170f, 44f);
        UIShape ink = view.signature.gameObject.AddComponent<UIShape>();
        ink.color = UIColors.Info;
        ink.strokeWidth = 2.6f;
        ink.raycastTarget = false;
        ink.AddStroke(Local(Bezier(Signature)));
        view.signedLabel = Text(memo, "Signed", 17f, UIColors.Pencil, font, false, 0f);
        view.signedLabel.alignment = TextAlignmentOptions.Right;
        view.signedLabel.textWrappingMode = TextWrappingModes.NoWrap;

        memo.gameObject.SetActive(false);
    }

    // ------------------------------------------------------------------
    // Memo pictures
    // ------------------------------------------------------------------

    /// <summary>Build a memo's picture into <paramref name="host"/> (<paramref name="width"/> wide). Returns its height.</summary>
    public static float BuildExample(MemoExample kind, RectTransform host, float width, TMP_FontAsset font)
    {
        switch (kind)
        {
            case MemoExample.RiskCards: return RiskCards(host, width, font);
            case MemoExample.Crisis: return Crisis(host, width, font);
            case MemoExample.Board: return BoardCards(host, width, font);
            case MemoExample.Heat: return HeatLevels(host, width, font);
            default: return 0f;
        }
    }

    /// <summary>Three risk cards on the felt: High risk and Low risk tilted, Balanced raised with a yellow ring.</summary>
    static float RiskCards(RectTransform host, float width, TMP_FontAsset font)
    {
        const float Height = 214f, CardW = 118f, CardH = 170f, CardGap = 28f;
        StatusBox felt = StatusUI.Box(host, "Felt", UIColors.FeltHi, UIColors.ClipboardDark, 10f, BoardUI.ButtonRadii);
        felt.SetGradient(UIColors.FeltHi, UIColors.FeltDark);
        StatusUI.Place(felt.rectTransform, 0f, 0f, width, Height);

        CardRisk[] risks = { CardRisk.HighRisk, CardRisk.Balanced, CardRisk.LowRisk };
        float[] turns = { 6f, 0f, -6f };   // CSS rotate(-6deg), none, rotate(6deg)
        float x0 = (width - (CardW * 3f + CardGap * 2f)) * 0.5f;
        for (int i = 0; i < risks.Length; i++)
        {
            bool picked = risks[i] == CardRisk.Balanced;
            float y = (Height - CardH) * 0.5f + (picked ? -8f : 4f);
            RectTransform card = StatusUI.PlaceRotated(StatusUI.NewRect("Card" + (i + 1), host), x0 + i * (CardW + CardGap), y, CardW, CardH, turns[i]);
            StatusBox face = card.gameObject.AddComponent<StatusBox>();
            face.SetStyle(UIColors.PaperHi, UIColors.Ink, 2f);
            face.Radii = Vector4.one * 12f;
            face.SetShadow(UIColors.WithAlpha(UIColors.Ink, 0.85f), new Vector2(3f, 4f), 0f);
            if (picked) face.SetOutline(UIColors.Highlight, 4f, 0f);
            face.raycastTarget = false;

            TextMeshProUGUI name = StatusUI.Text(card, "Name", TutorialContent.RiskName(risks[i]), 19f, UIColors.Ink, font, TextAlignmentOptions.Center, true);
            StatusUI.AutoSize(name, 12f, 19f);
            StatusUI.Place(name.rectTransform, 6f, 16f, CardW - 12f, 30f);

            float good = TutorialContent.RiskOdds(risks[i]);
            TextMeshProUGUI odds = StatusUI.Text(card, "Odds", TutorialContent.RiskOddsText(good), 17f, UIColors.InkSoft, font, TextAlignmentOptions.Center);
            StatusUI.AutoSize(odds, 11f, 17f);
            StatusUI.Place(odds.rectTransform, 6f, 70f, CardW - 12f, 28f);

            // Good / bad bar: green for the good share, red for the rest
            StatusBox bar = StatusUI.Box(card, "Bar", UIColors.PaperHi, UIColors.Ink, 2f, Vector4.one * 6f);
            StatusUI.Place(bar.rectTransform, (CardW - 90f) * 0.5f, CardH - 14f - 12f, 90f, 12f);
            float inner = 86f, goodW = Mathf.Round(inner * Mathf.Clamp01(good));
            if (goodW > 0.5f)
            {
                StatusBox green = StatusUI.Box(bar.rectTransform, "Good", UIColors.Money, Color.clear, 0f, new Vector4(4f, goodW >= inner ? 4f : 0f, goodW >= inner ? 4f : 0f, 4f));
                StatusUI.Place(green.rectTransform, 2f, 2f, goodW, 8f);
            }
            if (inner - goodW > 0.5f)
            {
                StatusBox red = StatusUI.Box(bar.rectTransform, "Bad", UIColors.Loss, Color.clear, 0f, new Vector4(goodW <= 0.5f ? 4f : 0f, 4f, 4f, goodW <= 0.5f ? 4f : 0f));
                StatusUI.Place(red.rectTransform, 2f + goodW, 2f, inner - goodW, 8f);
            }
        }
        return Height;
    }

    /// <summary>An URGENT note pinned to the office whiteboard.</summary>
    static float Crisis(RectTransform host, float width, TMP_FontAsset font)
    {
        const float Height = 214f, NoteW = 300f, NoteH = 150f;
        StatusBox board = StatusUI.Box(host, "Whiteboard", WhiteboardFill, WhiteboardFrame, 10f, Vector4.one * 6f);
        StatusUI.Place(board.rectTransform, 0f, 0f, width, Height);
        StatusBox line = StatusUI.Box(host, "Inset", Color.clear, WhiteboardLine, 2f, Vector4.zero);
        StatusUI.Place(line.rectTransform, 10f, 10f, width - 20f, Height - 20f);

        RectTransform note = StatusUI.PlaceRotated(StatusUI.NewRect("Note", host), (width - NoteW) * 0.5f, (Height - NoteH) * 0.5f + 4f, NoteW, NoteH, -2f);
        StatusBox paper = note.gameObject.AddComponent<StatusBox>();
        paper.SetStyle(UIColors.PaperHi, UIColors.Ink, 2f);
        paper.Radii = Vector4.zero;
        paper.SetShadow(UIColors.Dark(0.2f), new Vector2(0f, 8f), 14f);
        paper.raycastTarget = false;

        // The red pin at the top
        UIShape pin = Ellipse(note, "Pin", PinRed, (NoteW - 22f) * 0.5f, -11f, 22f);
        Ellipse(pin.rectTransform, "Shine", PinShine, 5f, 4f, 7f);
        UIShape rim = Ellipse(note, "PinRim", UIColors.Ink, (NoteW - 22f) * 0.5f, -11f, 22f);
        rim.border = 2f;

        // URGENT: a red rubber stamp, turned 6 degrees counter-clockwise
        RectTransform stamp = StatusUI.PlaceRotated(StatusUI.NewRect("Stamp", note), 20f, 22f, 150f, 36f, 6f);
        StatusBox ring = stamp.gameObject.AddComponent<StatusBox>();
        ring.SetStyle(Color.clear, UIColors.Loss, 3f);
        ring.Radii = Vector4.one * 8f;
        ring.SetOutline(UIColors.Loss, 2f, 3f);
        ring.raycastTarget = false;
        TextMeshProUGUI word = StatusUI.Text(stamp, "Word", TutorialContent.CrisisStamp, 24f, UIColors.Loss, font, TextAlignmentOptions.Center, true);
        word.characterSpacing = 12f;
        StatusUI.Stretch(word.rectTransform, 10f, 10f, 0f, 0f);
        StatusFitWidth fit = stamp.gameObject.AddComponent<StatusFitWidth>();
        fit.text = word;
        fit.padding = 30f;

        TextMeshProUGUI text = StatusUI.Text(note, "Line", TutorialContent.CrisisLine, 22f, UIColors.Ink, font);
        text.textWrappingMode = TextWrappingModes.Normal;
        StatusUI.AutoSize(text, 13f, 22f);
        StatusUI.Place(text.rectTransform, 20f, 70f, NoteW - 40f, NoteH - 70f - 12f);
        return Height;
    }

    /// <summary>The board's three target cards (Safe, Stretch, Moonshot) with what a hit and a miss do to trust.</summary>
    static float BoardCards(RectTransform host, float width, TMP_FontAsset font)
    {
        const float Height = 126f, Gap = 24f;
        Color[] bands = { UIColors.StickyGreen, UIColors.StickyYellow, UIColors.StickyPink };
        float cardW = Mathf.Floor((width - Gap * 2f) / 3f);
        BoardMeetings board = BoardMeetings.Instance;
        for (int tier = 0; tier < BoardMeetings.TierCount; tier++)
        {
            RectTransform card = StatusUI.Place(StatusUI.NewRect("Card" + tier, host), tier * (cardW + Gap), 2f, cardW, Height - 8f);
            StatusBox face = card.gameObject.AddComponent<StatusBox>();
            face.SetStyle(UIColors.PaperHi, UIColors.Ink, 2f);
            face.Radii = BoardUI.BoxRadii;
            face.SetShadow(UIColors.WithAlpha(UIColors.Ink, 0.85f), new Vector2(3f, 4f), 0f);
            face.raycastTarget = false;

            StatusBox band = StatusUI.Box(card, "Band", bands[tier], Color.clear, 0f, new Vector4(10f, 12f, 0f, 0f));
            StatusUI.Place(band.rectTransform, 2f, 2f, cardW - 4f, 36f);
            TextMeshProUGUI name = StatusUI.Text(card, "Name", BoardMeetings.TierNameCaps(tier), 19f, UIColors.Ink, font, TextAlignmentOptions.Center, true);
            name.characterSpacing = 8f;
            StatusUI.AutoSize(name, 12f, 19f);
            StatusUI.Place(name.rectTransform, 8f, 2f, cardW - 16f, 36f);

            float hit = board != null ? board.GetRule(tier).hitTrust : new[] { 4f, 10f, 20f }[tier];
            float miss = board != null ? board.GetRule(tier).missTrust : new[] { -6f, -12f, -25f }[tier];
            TextMeshProUGUI hitLine = StatusUI.Text(card, "Hit", BoardLine("board.card.hit", "board.card.hit.label", hit), 17f, UIColors.Ink, font);
            StatusUI.AutoSize(hitLine, 11f, 17f);
            StatusUI.Place(hitLine.rectTransform, 16f, 46f, cardW - 32f, 28f);
            TextMeshProUGUI missLine = StatusUI.Text(card, "Miss", BoardLine("board.card.miss", "board.card.miss.label", miss), 17f, UIColors.Ink, font);
            StatusUI.AutoSize(missLine, 11f, 17f);
            StatusUI.Place(missLine.rectTransform, 16f, 76f, cardW - 32f, 28f);
        }
        return Height;
    }

    /// <summary>"Hit: trust +4" in the board's own words (Board.csv), the label in bold.</summary>
    static string BoardLine(string key, string labelKey, float trust)
    {
        string label = "<b>" + L10n.Get(BoardMeetings.Table, labelKey) + "</b>";
        return L10n.Get(BoardMeetings.Table, key, ("label", label), ("trust", Signed(trust)));
    }

    /// <summary>The heat levels as a bar: Cool, Warm, Hot and On fire from their thresholds, and the freeze at the end.</summary>
    static float HeatLevels(RectTransform host, float width, TMP_FontAsset font)
    {
        const float Height = 88f, BarTop = 30f, BarH = 22f;
        HeatMeter meter = HeatMeter.Instance;
        float max = meter != null ? meter.MaxHeat : 100f;
        float[] from =
        {
            0f,
            meter != null ? meter.warm.from : 30f,
            meter != null ? meter.hot.from : 60f,
            meter != null ? meter.onFire.from : 85f,
            max
        };

        for (int level = HeatMeter.LevelCool; level <= HeatMeter.LevelOnFire; level++)
        {
            float x0 = width * Mathf.Clamp01(from[level] / max), x1 = width * Mathf.Clamp01(from[level + 1] / max);
            if (x1 - x0 < 2f) continue;
            Vector4 radii = new Vector4(level == 0 ? 11f : 0f, level == HeatMeter.LevelOnFire ? 11f : 0f, level == HeatMeter.LevelOnFire ? 11f : 0f, level == 0 ? 11f : 0f);
            StatusBox segment = StatusUI.Box(host, "Level" + level, HeatMeter.LevelColor(level), UIColors.Ink, 2f, radii);
            StatusUI.Place(segment.rectTransform, x0, BarTop, x1 - x0 + (level < HeatMeter.LevelOnFire ? 2f : 0f), BarH);

            TextMeshProUGUI name = StatusUI.Text(host, "Name" + level, HeatMeter.GetLevelName(level), 17f, HeatMeter.LevelInkColor(level), font, TextAlignmentOptions.Left, true);
            StatusUI.AutoSize(name, 11f, 17f);
            StatusUI.Place(name.rectTransform, x0 + 4f, 0f, Mathf.Max(10f, x1 - x0 - 8f), 28f);

            TextMeshProUGUI number = StatusUI.Text(host, "From" + level, Number(from[level]), 15f, UIColors.Pencil, font);
            StatusUI.Place(number.rectTransform, x0 + 2f, BarTop + BarH + 4f, 60f, 24f);
        }

        TextMeshProUGUI end = StatusUI.Text(host, "Freeze", TutorialContent.HeatFreeze + "  " + Number(meter != null ? Mathf.Min(meter.lockoutAt, max) : 100f),
                                            15f, UIColors.Loss, font, TextAlignmentOptions.Right, true);
        StatusUI.Place(end.rectTransform, width - 260f, BarTop + BarH + 4f, 260f, 24f);
        return Height;
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    static readonly Vector2[][] ArrowIcon =
    {
        new[] { new Vector2(4f, 12f), new Vector2(19f, 12f) },
        new[] { new Vector2(13f, 6f), new Vector2(19f, 12f) },
        new[] { new Vector2(19f, 12f), new Vector2(13f, 18f) }
    };

    static readonly Vector2[][] StampIcon =
    {
        new[] { new Vector2(9f, 3.5f), new Vector2(15f, 3.5f), new Vector2(15f, 9f), new Vector2(17.5f, 12.5f), new Vector2(6.5f, 12.5f), new Vector2(9f, 9f), new Vector2(9f, 3.5f) },
        new[] { new Vector2(4.5f, 12.5f), new Vector2(19.5f, 12.5f), new Vector2(19.5f, 16.5f), new Vector2(4.5f, 16.5f), new Vector2(4.5f, 12.5f) },
        new[] { new Vector2(5.5f, 20.5f), new Vector2(18.5f, 20.5f) }
    };

    static TextMeshProUGUI Text(RectTransform parent, string name, float size, Color color, TMP_FontAsset font, bool bold, float letterSpacing)
    {
        TextMeshProUGUI text = StatusUI.Text(parent, name, "", size, color, font, TextAlignmentOptions.Left, bold);
        text.characterSpacing = letterSpacing;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.raycastTarget = false;
        return text;
    }

    static TextMeshProUGUI ButtonLabel(RectTransform face, float size, Color color, TMP_FontAsset font, bool bold)
    {
        TextMeshProUGUI text = StatusUI.Text(face, "Label", "", size, color, font, TextAlignmentOptions.Left, bold);
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        return text;
    }

    static UIShape Stroke(RectTransform parent, string name, Color color, float width)
    {
        RectTransform rect = StatusUI.NewRect(name, parent);
        StatusUI.Stretch(rect);
        rect.pivot = new Vector2(0f, 1f);
        UIShape shape = rect.gameObject.AddComponent<UIShape>();
        shape.color = color;
        shape.strokeWidth = width;
        shape.raycastTarget = false;
        shape.kind = UIShape.Kind.Stroke;
        return shape;
    }

    /// <summary>A 24-unit line icon drawn <paramref name="size"/> wide (the design's SVG icons), in a top-left-pivoted rect.</summary>
    static RectTransform Icon(RectTransform parent, string name, float size, Color color, float strokeWidth, Vector2[][] paths)
    {
        RectTransform rect = StatusUI.Place(StatusUI.NewRect(name, parent), 0f, 0f, size, size);
        UIShape shape = rect.gameObject.AddComponent<UIShape>();
        shape.color = color;
        float scale = size / 24f;
        shape.strokeWidth = strokeWidth * scale;
        shape.raycastTarget = false;
        foreach (Vector2[] path in paths)
        {
            var points = new Vector2[path.Length];
            for (int i = 0; i < path.Length; i++)
                points[i] = new Vector2(path[i].x * scale, -path[i].y * scale);
            shape.AddStroke(points);
        }
        return rect;
    }

    static UIShape Ellipse(RectTransform parent, string name, Color color, float x, float y, float size)
    {
        RectTransform rect = StatusUI.Place(StatusUI.NewRect(name, parent), x, y, size, size);
        UIShape shape = rect.gameObject.AddComponent<UIShape>();
        shape.kind = UIShape.Kind.Ellipse;
        shape.color = color;
        shape.raycastTarget = false;
        return shape;
    }

    /// <summary>Points along cubic Bezier segments (start, then control, control, end for each), in drawing space.</summary>
    static Vector2[] Bezier(Vector2[] segments)
    {
        var points = new List<Vector2> { segments[0] };
        for (int i = 0; i + 3 < segments.Length; i += 3)
            UIPolyline.AppendCubic(points, segments[i], segments[i + 1], segments[i + 2], segments[i + 3], 10);
        return points.ToArray();
    }

    /// <summary>Drawing space (y down) to a top-left-pivoted shape's local space (y up).</summary>
    static Vector2[] Local(Vector2[] drawing)
    {
        var local = new Vector2[drawing.Length];
        for (int i = 0; i < drawing.Length; i++)
            local[i] = new Vector2(drawing[i].x, -drawing[i].y);
        return local;
    }

    /// <summary>"+4", "−6" (a true minus sign), "0".</summary>
    static string Signed(float value)
    {
        string number = Mathf.Abs(value).ToString("0.#", L10n.Culture);
        if (value > 0f) return "+" + number;
        if (value < 0f) return UIFormat.Minus + number;
        return number;
    }

    static string Number(float value)
    {
        return value.ToString("0.#", L10n.Culture);
    }
}
