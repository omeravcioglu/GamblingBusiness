using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds BoardMeetingView's screen at runtime when no designed one is assigned: a scrim and a 1240 x 864 internal memo in
/// the promotion memo's look (the same letterhead, brass paper clip and signature: it is the same board), placed like the
/// design's CSS (x and y from the memo's top-left corner, its 3 px border included, y down). While the vote of no confidence
/// shows, its block pushes the proposals down and the memo grows by 120 px. Also builds the "Skip the meeting?" slip that
/// Escape brings up. Fills in the view's references, so the view works the same way it will with a designed prefab.
/// The fixed texts (INTERNAL MEMO, the routing line, the section headers, the sign-off, the buttons and stamps) are
/// L10nText components on the "Board" String Table, so they follow language changes by themselves; the view fills in the rest.
/// </summary>
public static class BoardMeetingFactory
{
    const string T = BoardMeetings.Table;

    public const float MemoWidth = 1240f;
    public const float BaseHeight = 864f;
    const float Left = 51f;              // 3 px border + 48 px padding
    const float ContentWidth = 1138f;
    const float ResultTop = 266f;
    const float ResultHeight = 104f;
    const float StampColumn = 262f;      // the result text starts right of the stamp
    const float VoteTop = 386f;
    const float VoteHeight = 104f;
    const float VoteGap = 16f;
    const float LowerTop = 386f;         // the proposals, the signature and Skip
    const float LowerHeight = 444f;
    const float CardTop = 80f;
    const float CardWidth = 360f;
    const float CardHeight = 256f;
    const float CardGap = 29f;
    const float BandHeight = 44f;

    /// <summary>Band colours of the Safe, Stretch and Moonshot cards.</summary>
    static readonly Color[] TierBands = { UIColors.StickyGreen, UIColors.StickyYellow, UIColors.StickyPink };

    // The Board's signature, as on the promotion memo (SVG path in a 190 x 52 box) as cubic Bezier segments: the start
    // point, then (control, control, end) for each segment
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
    public static Canvas Build(BoardMeetingView view, TMP_FontAsset font)
    {
        Canvas canvas = BoardUI.CreateCanvas("BoardMeetingCanvas (Runtime)", view.sortingOrder, view.transform, true);

        // Built inactive, so every component initialises with its references already set
        RectTransform root = StatusUI.NewRect("BoardMeeting (Runtime)", canvas.transform);
        root.gameObject.SetActive(false);
        StatusUI.Stretch(root);
        view.root = root;

        // The game waits underneath: the paused pop-ups' scrim, which also takes the clicks meant for the HUD
        StatusUI.Stretch(BoardUI.Block(root, "Scrim", UIColors.Scrim, true).rectTransform);

        RectTransform memo = StatusUI.NewRect("Memo", root);
        memo.anchorMin = memo.anchorMax = memo.pivot = new Vector2(0.5f, 0.5f);
        memo.anchoredPosition = Vector2.zero;
        memo.sizeDelta = new Vector2(MemoWidth, BaseHeight);
        StatusBox paper = memo.gameObject.AddComponent<StatusBox>();
        paper.SetStyle(UIColors.PaperHi, UIColors.Ink, 3f);
        paper.Radii = BoardUI.PaperRadii;
        paper.SetShadow(UIColors.Dark(0.45f), new Vector2(0f, 22f), 48f);
        paper.raycastTarget = true;   // clicks on the paper stop here
        view.memo = memo;

        BuildPaperClip(memo);
        BuildLetterhead(view, memo, font);
        BuildResult(view, memo, font);
        BuildNoConfidence(view, memo, font);

        RectTransform lower = StatusUI.Place(StatusUI.NewRect("Proposals", memo), Left, LowerTop, ContentWidth, LowerHeight);
        BuildProposals(view, lower, font);
        BuildSignOff(view, lower, font);

        BuildConfirm(view, root, font);

        view.runtimeLower = lower;
        view.runtimeLowerHome = lower.anchoredPosition;
        view.runtimeBaseHeight = BaseHeight;
        view.runtimeVoteShift = VoteHeight + VoteGap;
        return canvas;
    }

    // ------------------------------------------------------------------
    // Sections (top to bottom)
    // ------------------------------------------------------------------

    static void BuildPaperClip(RectTransform memo)
    {
        // Over the top edge: the 24 x 60 drawing at 1.25x, tilted 4 degrees counter-clockwise (as on the promotion memo)
        RectTransform clip = StatusUI.PlaceRotated(StatusUI.NewRect("PaperClip", memo), 566f, -27f, 30f, 76f, 4f);
        Vector2[] path = StatusUI.PaperClip(1.25f);
        StatusUI.Stroke(clip, "Brass", BoardUI.ClipBrass, 4.2f * 1.25f, path);
        StatusUI.Stroke(clip, "Shine", BoardUI.ClipShine, 2f * 1.25f, path);
    }

    static void BuildLetterhead(BoardMeetingView view, RectTransform memo, TMP_FontAsset font)
    {
        // Company mark: a navy disc with a paper-white sparkle
        StatusBox mark = StatusUI.Box(memo, "Mark", BoardUI.Navy, UIColors.Ink, 2f, Vector4.one * 24f);
        StatusUI.Place(mark.rectTransform, Left, 35f, 48f, 48f);
        StatusUI.Star(mark.rectTransform, "Sparkle", UIColors.PaperHi, 18f);

        TextMeshProUGUI company = BoardUI.Text(memo, "Company", "", 32f, BoardUI.Navy, font, Left + 64f, 33f, 700f, 52f, TextAlignmentOptions.Left, true);
        company.characterSpacing = 12f;
        company.overflowMode = TextOverflowModes.Ellipsis;
        StatusUI.AutoSize(company, 22f, 32f);
        view.letterheadText = company;

        TextMeshProUGUI memoLabel = BoardUI.Text(memo, "InternalMemo", "", 19f, UIColors.Pencil, font,
                                                 Left + ContentWidth - 320f, 35f, 320f, 48f, TextAlignmentOptions.Right);
        memoLabel.characterSpacing = 12f;
        StatusUI.AutoSize(memoLabel, 14f, 19f);
        L10nText.Attach(memoLabel, T, "board.memo.kicker");   // "INTERNAL MEMO"

        // Letterhead rule: navy over a thin brass line
        StatusUI.Place(StatusUI.Block(memo, "RuleNavy", BoardUI.Navy).rectTransform, Left, 101f, ContentWidth, 3f);
        StatusUI.Place(StatusUI.Block(memo, "RuleBrass", BoardUI.Brass).rectTransform, Left, 106f, ContentWidth, 1f);

        // TO The CEO   FROM The Board   RE Board meeting, day N (the view writes the day)
        RectTransform routing = StatusUI.Place(StatusUI.NewRect("Routing", memo), Left, 123f, ContentWidth, 30f);
        Row(routing, 28f);
        RoutingField(StatusUI.Text(routing, "To", "", 19f, UIColors.Ink, font), "board.memo.to");
        RoutingField(StatusUI.Text(routing, "From", "", 19f, UIColors.Ink, font), "board.memo.from");
        view.reText = StatusUI.Text(routing, "Re", L10n.Get(T, "board.memo.re.plain", ("label", BoardMeetingView.RoutingLabel(L10n.Get(T, "board.memo.re.label")))),
                                    19f, UIColors.Ink, font);

        TextMeshProUGUI headline = BoardUI.Text(memo, "Headline", "", 38f, UIColors.Ink, font, Left, 164f, ContentWidth, 60f, TextAlignmentOptions.Left, true);
        headline.overflowMode = TextOverflowModes.Ellipsis;
        StatusUI.AutoSize(headline, 26f, 38f);
        view.headlineText = headline;
    }

    static void BuildResult(BoardMeetingView view, RectTransform memo, TMP_FontAsset font)
    {
        view.resultLabelText = SectionLabel(memo, "LastPeriodLabel", Left, ResultTop - 30f, 800f, font);

        StatusBox box = StatusUI.Box(memo, "Result", UIColors.Paper, UIColors.Ink, 2f, BoardUI.BoxRadii);
        RectTransform rect = StatusUI.Place(box.rectTransform, Left, ResultTop, ContentWidth, ResultHeight);

        view.resultStamp = BoardUI.Stamp(rect, "Verdict", L10n.Get(T, "board.memo.stamp.hit"), UIColors.Money, font, 34f, StampColumn * 0.5f,
                                         ResultHeight * 0.5f, 62f, 7f, out TextMeshProUGUI stampText, out Graphic[] tinted);
        view.resultStampText = stampText;
        view.resultStampTinted = tinted;

        TextMeshProUGUI text = BoardUI.Text(rect, "Details", "", 20f, UIColors.Ink, font, StampColumn, 6f, ContentWidth - StampColumn - 20f,
                                            ResultHeight - 12f, TextAlignmentOptions.Left);
        BoardUI.Wrapped(text, 1.35f);
        BoardUI.Shrink(text, 15f);   // longer languages shrink rather than spill out of the box
        view.resultText = text;
    }

    static void BuildNoConfidence(BoardMeetingView view, RectTransform memo, TMP_FontAsset font)
    {
        RectTransform group = StatusUI.Place(StatusUI.NewRect("NoConfidence", memo), Left, VoteTop, ContentWidth, VoteHeight);
        StatusBox box = StatusUI.Box(group, "Box", UIColors.LossSoft, UIColors.Loss, 3f, BoardUI.BoxRadii);
        StatusUI.Stretch(box.rectTransform);

        TextMeshProUGUI header = BoardUI.Text(group, "Header", "", 22f, UIColors.Loss, font, 22f, 8f, 700f, 36f,
                                              TextAlignmentOptions.Left, true);
        header.characterSpacing = 8f;
        L10nText.Attach(header, T, "board.memo.vote.header");   // "VOTE OF NO CONFIDENCE"

        TextMeshProUGUI body = BoardUI.Text(group, "Body", "", 20f, UIColors.Ink, font, 22f, 44f, ContentWidth - 44f, 56f, TextAlignmentOptions.TopLeft);
        BoardUI.Wrapped(body, 1.3f);
        BoardUI.Shrink(body, 15f);
        view.noConfidenceText = body;

        view.noConfidenceGroup = group.gameObject;
        group.gameObject.SetActive(false);
    }

    static void BuildProposals(BoardMeetingView view, RectTransform lower, TMP_FontAsset font)
    {
        view.proposalsLabelText = SectionLabel(lower, "ProposalsLabel", 0f, 0f, 700f, font);

        TextMeshProUGUI value = BoardUI.Text(lower, "ValueToday", "", 19f, UIColors.InkSoft, font, ContentWidth - 520f, 0f, 520f, 30f,
                                             TextAlignmentOptions.Right);
        view.valueTodayText = value;

        TextMeshProUGUI intro = BoardUI.Text(lower, "Intro", "", 22f, UIColors.Ink, font, 0f, 32f, ContentWidth, 36f);
        BoardUI.Shrink(intro, 17f);
        view.introText = intro;

        view.cards = new BoardMeetingView.ProposalCard[BoardMeetings.TierCount];
        for (int i = 0; i < view.cards.Length; i++)
            view.cards[i] = Card(lower, i, i * (CardWidth + CardGap), CardTop, font);
    }

    static BoardMeetingView.ProposalCard Card(RectTransform lower, int tier, float x, float y, TMP_FontAsset font)
    {
        var card = new BoardMeetingView.ProposalCard();
        string name = tier == BoardMeetings.TierSafe ? "Safe" : tier == BoardMeetings.TierMoonshot ? "Moonshot" : "Stretch";
        RectTransform rect = StatusUI.Place(StatusUI.NewRect(name + "Card", lower), x, y, CardWidth, CardHeight);
        card.button = BoardUI.MakeMarkerButton(rect, UIColors.PaperHi, 3f, BoardUI.CardRadii, new Vector2(5f, 6f), out StatusBox face);
        RectTransform f = face.rectTransform;

        // Coloured band inside the border, the tier's name on it
        Vector4 r = BoardUI.CardRadii;
        StatusBox band = StatusUI.Box(f, "Band", TierBands[Mathf.Clamp(tier, 0, TierBands.Length - 1)], Color.clear, 0f,
                                      new Vector4(Mathf.Max(0f, r.x - 3f), Mathf.Max(0f, r.y - 3f), 0f, 0f));
        StatusUI.Place(band.rectTransform, 3f, 3f, CardWidth - 6f, BandHeight);
        StatusUI.Place(StatusUI.Block(f, "BandRule", UIColors.Ink).rectTransform, 3f, 3f + BandHeight, CardWidth - 6f, 2f);

        // The tier's name in capitals (written so in the String Table; the view writes it again for each meeting)
        TextMeshProUGUI tierText = BoardUI.Text(f, "Tier", BoardMeetings.TierNameCaps(tier), 22f, UIColors.Ink, font, 20f, 3f, 200f, BandHeight,
                                                TextAlignmentOptions.Left, true);
        tierText.characterSpacing = 10f;
        BoardUI.Shrink(tierText, 15f);
        card.tierText = tierText;
        TextMeshProUGUI paceText = BoardUI.Text(f, "Pace", "", 17f, UIColors.InkSoft, font, CardWidth - 200f, 3f, 180f, BandHeight, TextAlignmentOptions.Right);
        BoardUI.Shrink(paceText, 13f);
        card.paceText = paceText;

        card.gainText = BoardUI.Text(f, "Gain", "", 46f, UIColors.Ink, font, 20f, 54f, CardWidth - 40f, 70f, TextAlignmentOptions.Left, true);
        BoardUI.Shrink((TextMeshProUGUI)card.gainText, 28f);

        card.reachText = BoardUI.Text(f, "Reach", "", 20f, UIColors.InkSoft, font, 20f, 122f, CardWidth - 40f, 32f);
        BoardUI.Shrink((TextMeshProUGUI)card.reachText, 15f);

        BoardUI.DashedRule(f, "Rule", UIColors.WithAlpha(UIColors.Pencil, 0.7f), 2f, 20f, 163f, CardWidth - 40f);

        TextMeshProUGUI terms = BoardUI.Text(f, "Terms", "", 19f, UIColors.Ink, font, 20f, 171f, CardWidth - 38f, 78f, TextAlignmentOptions.TopLeft);
        BoardUI.Wrapped(terms, 1.3f);
        BoardUI.Shrink(terms, 14f);
        card.termsText = terms;

        // Stamped on when it is chosen (the view shows it)
        card.chosenStamp = BoardUI.Stamp(f, "Approved", "", UIColors.Money, font, 30f, CardWidth * 0.5f, 150f, 56f, -9f,
                                         out TextMeshProUGUI approved, out _);
        L10nText.Attach(approved, T, "board.card.stamp.approved");   // "APPROVED"
        return card;
    }

    static void BuildSignOff(BoardMeetingView view, RectTransform lower, TMP_FontAsset font)
    {
        const float top = CardTop + CardHeight + 26f;
        RectTransform signature = StatusUI.Place(StatusUI.NewRect("Signature", lower), 0f, top, 170f, 46f);
        UIPolyline ink = StatusUI.Stroke(signature, "Ink", UIColors.Ink, 2.1f, SignaturePoints(SignatureScribble));
        ink.AddPath(StatusUI.Points(SignaturePoints(SignatureUnderline)));
        StatusUI.Place(StatusUI.Block(lower, "SignatureLine", UIColors.Ink).rectTransform, 0f, top + 48f, 170f, 2f);
        TextMeshProUGUI signed = BoardUI.Text(lower, "Signed", "", 19f, UIColors.Pencil, font, 0f, top + 54f, 300f, 28f);
        StatusUI.AutoSize(signed, 14f, 19f);
        L10nText.Attach(signed, T, "board.memo.signed");   // "Signed, the Board"

        // Skip: bottom-right, a plain paper button (the cards are the real choice); the view writes its trust cost
        const float width = 430f;
        view.skipButton = BoardUI.PaperButton(lower, "SkipButton", L10n.Get(T, "board.memo.button.skip.plain"), UIColors.PaperHi, font, 24f,
                                              ContentWidth - width, top + 6f, width, 64f, out TextMeshProUGUI label);
        label.fontStyle = FontStyles.Normal;
        view.skipText = label;
    }

    static void BuildConfirm(BoardMeetingView view, RectTransform root, TMP_FontAsset font)
    {
        const float width = 620f, height = 272f;
        RectTransform group = StatusUI.NewRect("SkipConfirm", root);
        StatusUI.Stretch(group);
        StatusUI.Stretch(BoardUI.Block(group, "Scrim", UIColors.ScrimLight, true).rectTransform);

        RectTransform slip = StatusUI.NewRect("Slip", group);
        slip.anchorMin = slip.anchorMax = slip.pivot = new Vector2(0.5f, 0.5f);
        slip.anchoredPosition = new Vector2(0f, 20f);
        slip.sizeDelta = new Vector2(width, height);
        slip.localEulerAngles = new Vector3(0f, 0f, 1f);
        StatusBox paper = slip.gameObject.AddComponent<StatusBox>();
        paper.SetStyle(UIColors.PaperHi, UIColors.Ink, 3f);
        paper.Radii = BoardUI.CardRadii;
        paper.SetShadow(UIColors.Dark(0.45f), new Vector2(0f, 16f), 36f);
        paper.raycastTarget = true;

        TextMeshProUGUI title = BoardUI.Text(slip, "Title", "", 34f, UIColors.Ink, font, 40f, 28f, width - 80f, 54f, TextAlignmentOptions.Left, true);
        BoardUI.Shrink(title, 24f);
        L10nText.Attach(title, T, "board.confirm.title");   // "Skip the meeting?"
        TextMeshProUGUI body = BoardUI.Text(slip, "Body", "", 21f, UIColors.InkSoft, font, 40f, 88f, width - 80f, 66f, TextAlignmentOptions.TopLeft);
        BoardUI.Wrapped(body, 1.35f);
        BoardUI.Shrink(body, 16f);
        view.confirmText = body;

        const float buttonTop = 176f, buttonHeight = 60f;
        view.confirmBackButton = BoardUI.PaperButton(slip, "BackButton", "", UIColors.Highlight, font, 24f,
                                                     width - 40f - 170f, buttonTop, 170f, buttonHeight, out TextMeshProUGUI backLabel);
        L10nText.Attach(backLabel, T, "board.confirm.button.back");   // "Back"
        view.confirmSkipButton = BoardUI.PaperButton(slip, "SkipMeetingButton", "", UIColors.PaperHi, font, 24f,
                                                     width - 40f - 170f - 20f - 230f, buttonTop, 230f, buttonHeight, out TextMeshProUGUI skipLabel);
        L10nText.Attach(skipLabel, T, "board.confirm.button.skip");   // "Skip meeting"

        view.confirmGroup = group.gameObject;
        group.gameObject.SetActive(false);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    /// <summary>A routing-line field ("TO The CEO") kept on its String Table entry, the label in pencil small caps.</summary>
    static void RoutingField(TextMeshProUGUI text, string key)
    {
        L10nText.Attach(text, T, key, () => new object[]
        {
            L10n.Args(("label", BoardMeetingView.RoutingLabel(L10n.Get(T, key + ".label"))))
        });
    }

    /// <summary>"LAST PERIOD · DAYS 10–20": small pencil capitals above a section.</summary>
    static TextMeshProUGUI SectionLabel(RectTransform parent, string name, float x, float y, float width, TMP_FontAsset font)
    {
        TextMeshProUGUI label = BoardUI.Text(parent, name, "", 17f, UIColors.Pencil, font, x, y, width, 28f);
        label.characterSpacing = 8f;
        return label;
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
}
