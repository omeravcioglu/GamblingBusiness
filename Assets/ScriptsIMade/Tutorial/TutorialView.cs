using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// The first-day tour and the Board's feature memos on screen (design "First-day tour", 1920 x 1080), built at runtime by
/// TutorialViewFactory on its own overlay canvas (sorting order 455: above the HUD, its pop-ups and the promotion memo,
/// below the toasts):
/// - the welcome memo ("FROM THE DESK OF THE BOARD", Show me around / Skip the tour) and the feature memos in the same
///   paper look, with an optional small picture and "Got it";
/// - spotlight steps: the dark scrim with a rounded hole over one HUD element, a dashed yellow ring, a curved arrow and a
///   turned sticky note with STEP n OF 5, a title, a line or two, step dots and Skip tour / Next (the last step: "That's
///   the job." and Let's hire someone). A memo about a feature with a HUD tab spotlights that tab the same way.
/// Everything is laid out from the texts it shows (translations run longer), again for two frames after each change and
/// whenever the screen size changes. TutorialDirector decides what shows and owns pausing (UIManager); this only draws
/// and reports clicks.
/// </summary>
[DisallowMultipleComponent]
public class TutorialView : MonoBehaviour
{
    enum Page { None, Welcome, Step, Memo }

    [Header("Look")]
    [Tooltip("Font (empty = the game's Coming Soon font)")]
    public TMP_FontAsset font;
    [Tooltip("Sorting order of the tour's canvas: above the HUD, its pop-ups and the promotion memo (450), below the toasts (900)")]
    public int sortingOrder = 455;

    const float NoteWidth = 440f;
    const float MemoWidth = 880f;
    const float MemoWidthWithExample = 980f;
    const float OpenSeconds = 0.16f;
    const float PopSeconds = 0.2f;

    // ---- Built by TutorialViewFactory ----
    internal Canvas canvas;
    internal RectTransform root;
    internal CanvasGroup rootGroup;
    internal TutorialScrim scrim;
    internal UIShape ring;
    internal RectTransform arrowLayer;
    internal UIShape arrowUnder;
    internal UIShape arrowInk;

    internal RectTransform note;
    internal TextMeshProUGUI noteLabel, noteTitle, noteBody;
    internal RectTransform dotsRow;
    internal readonly List<StatusBox> dots = new List<StatusBox>();
    internal RectTransform skipLink;
    internal TextMeshProUGUI skipLabel;
    internal RectTransform skipWaveRect;
    internal UIShape skipWave;
    internal RectTransform nextButton;
    internal RectTransform nextFace;
    internal TextMeshProUGUI nextLabel;
    internal RectTransform nextIcon;
    internal TextMeshProUGUI doneNote;
    internal RectTransform doneButton;
    internal RectTransform doneFace;
    internal TextMeshProUGUI doneLabel;
    internal RectTransform doneIcon;

    internal RectTransform memo;
    internal TextMeshProUGUI memoKicker, memoTo, memoTitle, memoBody;
    internal RectTransform ruleTop, ruleBottom;
    internal RectTransform exampleHost;
    internal RectTransform primaryButton;
    internal RectTransform primaryFace;
    internal TextMeshProUGUI primaryLabel;
    internal RectTransform primaryIcon;
    internal RectTransform secondaryButton;
    internal RectTransform secondaryFace;
    internal TextMeshProUGUI secondaryLabel;
    internal RectTransform signature;
    internal TextMeshProUGUI signedLabel;

    // ---- What is showing ----
    Page screen;
    TourStep step;
    int stepNumber;
    int stepCount;
    Func<RectTransform, Rect?> findSpot;
    Action onPrimary, onSecondary, onDone;
    MemoExample example = MemoExample.None;
    float exampleHeight;
    int layoutFrames;
    Vector2 laidOutSize;
    float openAge = 10f;
    float popAge = 10f;
    bool built;

    /// <summary>True while the tour or a memo is on screen.</summary>
    public bool IsOpen => root != null && root.gameObject.activeSelf;

    /// <summary>The overlay's full-screen rect (targets are measured in it). Null until built.</summary>
    public RectTransform Space => root;

    // ------------------------------------------------------------------
    // Showing
    // ------------------------------------------------------------------

    /// <summary>Build the screen if it isn't built yet. False if it couldn't be built.</summary>
    public bool EnsureBuilt()
    {
        if (built && root != null) return true;
        try
        {
            TutorialViewFactory.Build(this, BoardUI.ResolveFont(font));
            built = root != null;
        }
        catch (Exception e)
        {
            Debug.LogException(e, this);
            built = false;
        }
        return built;
    }

    /// <summary>The tour's welcome memo. Primary = Show me around, secondary = Skip the tour.</summary>
    public void ShowWelcome(WelcomeCard card, Action primary, Action secondary)
    {
        if (card == null || !EnsureBuilt()) return;
        screen = Page.Welcome;
        onPrimary = primary;
        onSecondary = secondary;
        onDone = null;
        findSpot = null;
        memoKicker.text = card.kicker;
        memoTo.text = card.to;
        memoTitle.text = card.title;
        memoBody.text = card.body;
        primaryLabel.text = card.show;
        secondaryLabel.text = card.skip;
        signedLabel.text = card.signed;
        SetExample(MemoExample.None);
        Open();
    }

    /// <summary>A spotlight step. <paramref name="spot"/> finds the element in the overlay's space (null = no spotlight).</summary>
    public void ShowStep(TourStep tourStep, int number, int count, Func<RectTransform, Rect?> spot, Action next, Action skip, Action done)
    {
        if (tourStep == null || !EnsureBuilt()) return;
        screen = Page.Step;
        step = tourStep;
        stepNumber = number;
        stepCount = Mathf.Max(1, count);
        findSpot = spot;
        onPrimary = next;
        onSecondary = skip;
        onDone = done;
        noteLabel.text = TutorialContent.StepLabel(number, stepCount);
        noteTitle.text = tourStep.title;
        noteBody.text = tourStep.body;
        skipLabel.text = TutorialContent.SkipTour;
        nextLabel.text = TutorialContent.Next;
        doneNote.text = TutorialContent.DoneNote;
        doneLabel.text = TutorialContent.Done;
        EnsureDots(stepCount);
        Open();
    }

    /// <summary>A Board memo about a feature. <paramref name="tabSpot"/> finds its HUD tab (null = no spotlight).</summary>
    public void ShowMemo(TutorialMemo content, Func<RectTransform, Rect?> tabSpot, Action close)
    {
        if (content == null || !EnsureBuilt()) return;
        screen = Page.Memo;
        onPrimary = close;
        onSecondary = null;
        onDone = null;
        findSpot = tabSpot;
        memoKicker.text = content.kicker;
        memoTo.text = content.to;
        memoTitle.text = content.title;
        memoBody.text = content.body;
        primaryLabel.text = content.button;
        signedLabel.text = content.signed;
        SetExample(content.example);
        Open();
    }

    public void Hide()
    {
        screen = Page.None;
        onPrimary = onSecondary = onDone = null;
        findSpot = null;
        if (root != null) root.gameObject.SetActive(false);
    }

    // Buttons (wired by the factory)
    internal void ClickPrimary() => onPrimary?.Invoke();
    internal void ClickSecondary() => onSecondary?.Invoke();
    internal void ClickDone() => onDone?.Invoke();

    void Open()
    {
        bool opening = !root.gameObject.activeSelf;
        bool isStep = screen == Page.Step;
        bool welcome = screen == Page.Welcome;
        note.gameObject.SetActive(isStep);
        memo.gameObject.SetActive(!isStep);
        if (!isStep)
        {
            secondaryButton.gameObject.SetActive(welcome);
            primaryIcon.gameObject.SetActive(welcome);
        }

        root.gameObject.SetActive(true);
        if (opening)
        {
            openAge = 0f;
            if (rootGroup != null) rootGroup.alpha = 0f;
        }
        popAge = 0f;
        layoutFrames = 2;
        Layout();
        Animate();
        BoardUI.ClearSelection();   // Enter / Space mustn't click a HUD button behind the tour
    }

    void SetExample(MemoExample kind)
    {
        // Built again each time: its words follow the language and its numbers the live systems
        for (int i = exampleHost.childCount - 1; i >= 0; i--)
            Destroy(exampleHost.GetChild(i).gameObject);
        example = kind;
        exampleHeight = 0f;
        exampleHost.gameObject.SetActive(kind != MemoExample.None);
        if (kind == MemoExample.None) return;
        try
        {
            exampleHeight = TutorialViewFactory.BuildExample(kind, exampleHost, MemoWidthWithExample - 2f * MemoEdge, BoardUI.ResolveFont(font));
            L10nFont.AttachAll(exampleHost);
        }
        catch (Exception e)
        {
            Debug.LogException(e, this);
            example = MemoExample.None;
            exampleHeight = 0f;
            exampleHost.gameObject.SetActive(false);
        }
    }

    void EnsureDots(int count)
    {
        while (dots.Count < count)
            dots.Add(TutorialViewFactory.Dot(dotsRow, dots.Count));
        for (int i = 0; i < dots.Count; i++)
            dots[i].gameObject.SetActive(i < count);
    }

    // ------------------------------------------------------------------
    // Frame
    // ------------------------------------------------------------------

    void Update()
    {
        if (IsOpen) Animate();
    }

    void LateUpdate()
    {
        if (!IsOpen) return;
        // Right after a change (TMP and the language's font settle) and whenever the screen size changes
        if (layoutFrames > 0 || root.rect.size != laidOutSize)
        {
            if (layoutFrames > 0) layoutFrames--;
            Layout();
        }
    }

    void Animate()
    {
        float dt = Time.unscaledDeltaTime;   // the game is paused
        if (openAge < OpenSeconds)
        {
            openAge += dt;
            if (rootGroup != null) rootGroup.alpha = Mathf.Clamp01(openAge / OpenSeconds);
        }
        else if (rootGroup != null && rootGroup.alpha < 1f)
        {
            rootGroup.alpha = 1f;
        }

        if (popAge <= PopSeconds)
        {
            popAge += dt;
            float t = Mathf.Clamp01(popAge / PopSeconds);
            float ease = 1f - (1f - t) * (1f - t) * (1f - t);
            float scale = Mathf.Lerp(0.95f, 1f, ease);
            RectTransform card = screen == Page.Step ? note : memo;
            if (card != null) card.localScale = new Vector3(scale, scale, 1f);
        }
    }

    // ------------------------------------------------------------------
    // Layout
    // ------------------------------------------------------------------

    void Layout()
    {
        if (root == null) return;
        try
        {
            if (screen == Page.Step) LayoutStep();
            else if (screen == Page.Welcome || screen == Page.Memo) LayoutMemo();
            laidOutSize = root.rect.size;
        }
        catch (Exception e)
        {
            Debug.LogException(e, this);
        }
    }

    Rect? FindSpot()
    {
        if (findSpot == null) return null;
        try { return findSpot(root); }
        catch (Exception e)
        {
            Debug.LogException(e, this);
            return null;
        }
    }

    void LayoutStep()
    {
        const float PadX = 26f, PadTop = 34f, PadBottom = 22f, Gap = 12f;
        Rect area = root.rect;
        float cw = NoteWidth - 2f * PadX;

        float y = PadTop;
        float h = Block(noteLabel, cw, Natural(noteLabel));
        StatusUI.Place(noteLabel.rectTransform, PadX, y, cw, h);
        y += h + Gap;
        h = Block(noteTitle, cw, 1.15f);
        StatusUI.Place(noteTitle.rectTransform, PadX, y, cw, h);
        y += h + Gap;
        h = Block(noteBody, cw, 1.4f);
        StatusUI.Place(noteBody.rectTransform, PadX, y, cw, h);
        y += h + Gap;

        y += 4f;
        LayoutDots(PadX, y);
        y += 18f + Gap;

        y += 4f;
        LayoutNoteButtons(PadX, y, cw);
        y += 54f + PadBottom;
        float noteHeight = y;

        // Next to the spotlight, where the design has it, kept to its side and on screen
        Rect? spot = FindSpot();
        Vector2 pos;
        if (spot.HasValue)
        {
            Rect s = spot.Value;
            pos = s.position + (step.designNote - step.designSpot.position);
            pos = KeepToSide(pos, NoteWidth, noteHeight, s, step.side);
        }
        else
        {
            pos = new Vector2((area.width - NoteWidth) * 0.5f, (area.height - noteHeight) * 0.5f);
        }
        pos.x = Mathf.Clamp(pos.x, 16f, Mathf.Max(16f, area.width - NoteWidth - 16f));
        pos.y = Mathf.Clamp(pos.y, 16f, Mathf.Max(16f, area.height - noteHeight - 16f));
        StatusUI.PlaceRotated(note, pos.x, pos.y, NoteWidth, noteHeight, -step.rotation);   // CSS turns clockwise

        if (spot.HasValue)
        {
            Rect noteRect = new Rect(pos.x, pos.y, NoteWidth, noteHeight);
            SetSpot(spot.Value, step.radius);
            DrawArrow(PointOn(noteRect, step.arrowFrom), PointOn(spot.Value, step.arrowTo), step.bend);
        }
        else
        {
            ClearSpot();
        }
    }

    void LayoutDots(float x, float y)
    {
        StatusUI.Place(dotsRow, x, y, 260f, 18f);
        float cx = 0f;
        for (int i = 0; i < dots.Count; i++)
        {
            StatusBox dot = dots[i];
            if (!dot.gameObject.activeSelf) continue;
            int n = i + 1;
            float size = n == stepNumber ? 18f : 14f;
            StatusUI.Place(dot.rectTransform, cx, (18f - size) * 0.5f, size, size);
            dot.Radii = Vector4.one * (size * 0.5f);
            dot.Fill = n == stepNumber ? UIColors.Ink : (n < stepNumber ? UIColors.Pencil : Color.clear);
            cx += size + 8f;
        }
    }

    void LayoutNoteButtons(float x, float y, float cw)
    {
        bool last = stepNumber >= stepCount;
        skipLink.gameObject.SetActive(!last);
        nextButton.gameObject.SetActive(!last);
        doneNote.gameObject.SetActive(last);
        doneButton.gameObject.SetActive(last);

        if (!last)
        {
            // "Skip tour" (a link with a wavy underline) left, Next right; long words shrink both a little
            float size = 22f, skipW, nextW;
            while (true)
            {
                skipLabel.fontSize = size;
                nextLabel.fontSize = size;
                skipW = LineWidth(skipLabel) + 8f;
                nextW = 24f + LineWidth(nextLabel) + 10f + 24f + 24f;
                if (skipW + 16f + nextW <= cw || size <= 16f) break;
                size -= 1f;
            }
            StatusUI.Place(skipLink, x, y + 5f, skipW, 44f);
            StatusUI.Place(skipLabel.rectTransform, 4f, 0f, skipW - 8f, 44f);
            DrawWave(skipW - 8f);

            StatusUI.Place(nextButton, x + cw - nextW, y, nextW, 54f);
            float labelW = LineWidth(nextLabel);
            StatusUI.Place(nextLabel.rectTransform, 24f, 0f, labelW + 2f, 54f);
            StatusUI.Place(nextIcon, 24f + labelW + 10f, 15f, 24f, 24f);
        }
        else
        {
            // "That's the job." left, the green Let's hire someone right
            float size = 22f, noteSize = 19f, noteW, doneW;
            while (true)
            {
                doneLabel.fontSize = size;
                doneNote.fontSize = noteSize;
                noteW = LineWidth(doneNote);
                doneW = 24f + 24f + 10f + LineWidth(doneLabel) + 24f;
                if (noteW + 16f + doneW <= cw || size <= 16f) break;
                size -= 1f;
                noteSize = Mathf.Max(14f, noteSize - 1f);
            }
            doneW = Mathf.Min(doneW, cw);
            StatusUI.Place(doneNote.rectTransform, x, y, Mathf.Max(0f, cw - doneW - 12f), 54f);
            StatusUI.Place(doneButton, x + cw - doneW, y, doneW, 54f);
            StatusUI.Place(doneIcon, 24f, 15f, 24f, 24f);
            StatusUI.Place(doneLabel.rectTransform, 24f + 24f + 10f, 0f, Mathf.Max(0f, doneW - 24f - 24f - 10f - 20f), 54f);
        }
    }

    /// <summary>The design's wavy underline (text-decoration: underline wavy 2px, offset 7px) under Skip tour.</summary>
    void DrawWave(float width)
    {
        StatusUI.Place(skipWaveRect, 4f, 34f, Mathf.Max(1f, width), 8f);
        skipWave.ClearStrokes();
        if (width < 4f) return;
        const float Amplitude = 2f, Wavelength = 9f;
        int steps = Mathf.Clamp(Mathf.CeilToInt(width / 1.5f), 4, 400);
        var points = new Vector2[steps + 1];
        for (int i = 0; i <= steps; i++)
        {
            float px = width * i / steps;
            points[i] = new Vector2(px, -(4f + Amplitude * Mathf.Sin(px / Wavelength * Mathf.PI * 2f)));
        }
        skipWave.AddStroke(points);
    }

    void LayoutMemo()
    {
        const float Top = 3f + 44f, Bottom = 3f + 40f, Gap = 20f;
        Rect area = root.rect;
        bool welcome = screen == Page.Welcome;
        bool hasExample = example != MemoExample.None && exampleHeight > 0f;
        float width = hasExample ? MemoWidthWithExample : MemoWidth;
        float cw = width - 2f * MemoEdge;

        float y = Top;
        float h = Block(memoKicker, cw, Natural(memoKicker));
        StatusUI.Place(memoKicker.rectTransform, MemoEdge, y, cw, h);
        y += h + 6f;
        h = Block(memoTo, cw, Natural(memoTo));
        StatusUI.Place(memoTo.rectTransform, MemoEdge, y, cw, h);
        y += h + 12f;
        // border-bottom: 6px double = two 2 px lines with a 2 px gap
        StatusUI.Place(ruleTop, MemoEdge, y, cw, 2f);
        StatusUI.Place(ruleBottom, MemoEdge, y + 4f, cw, 2f);
        y += 6f + Gap;

        memoTitle.fontSize = welcome ? 60f : 44f;
        h = Block(memoTitle, cw, 1.15f);
        StatusUI.Place(memoTitle.rectTransform, MemoEdge, y, cw, h);
        y += h + Gap;
        memoBody.fontSize = welcome ? 26f : 24f;
        h = Block(memoBody, cw, 1.45f);
        StatusUI.Place(memoBody.rectTransform, MemoEdge, y, cw, h);
        y += h;
        if (hasExample)
        {
            y += Gap;
            StatusUI.Place(exampleHost, MemoEdge, y, cw, exampleHeight);
            y += exampleHeight;
        }
        y += Gap + 8f;
        y += LayoutMemoButtons(MemoEdge, y, cw, welcome);
        y += Bottom;
        float height = y;

        // Centred a little above the middle like the design's welcome memo; below a spotlighted tab
        Rect? spot = FindSpot();
        float x = (area.width - width) * 0.5f;
        float top = (area.height - height) * 0.5f - 30f;
        if (spot.HasValue) top = Mathf.Max(top, spot.Value.yMax + 80f);
        top = Mathf.Clamp(top, 24f, Mathf.Max(24f, area.height - height - 16f));
        StatusUI.PlaceRotated(memo, x, top, width, height, 0f);

        if (spot.HasValue)
        {
            Rect s = spot.Value;
            Vector2 end = new Vector2(s.center.x, s.yMax + 10f);
            Vector2 start = new Vector2(Mathf.Clamp(s.center.x + 40f, x + 60f, x + width - 60f), top + 6f);   // starts under the paper
            SetSpot(s, 14f);
            DrawArrow(start, end, start.x <= end.x ? -22f : 22f);
        }
        else
        {
            ClearSpot();
        }
    }

    /// <summary>The buttons (left) and the Board's signature (right). Returns the row's height.</summary>
    float LayoutMemoButtons(float x, float y, float cw, bool welcome)
    {
        float buttonH = welcome ? 68f : 60f;
        float primaryPad = welcome ? 36f : 32f;
        float size = welcome ? 28f : 26f;
        float signedH = Natural(signedLabel) * signedLabel.fontSize;
        float signatureH = 44f + signedH;
        float primaryW, secondaryW, buttonsW;
        while (true)
        {
            primaryLabel.fontSize = size;
            secondaryLabel.fontSize = size;
            primaryW = primaryPad * 2f + LineWidth(primaryLabel) + (welcome ? 12f + 28f : 0f);
            secondaryW = welcome ? 32f * 2f + LineWidth(secondaryLabel) : 0f;
            buttonsW = primaryW + (welcome ? 20f + secondaryW : 0f);
            if (buttonsW + 24f + 170f <= cw || size <= 20f) break;
            size -= 1f;
        }
        bool signatureBelow = buttonsW + 24f + 170f > cw;
        float rowH = signatureBelow ? buttonH : Mathf.Max(buttonH, signatureH);
        float by = y + (rowH - buttonH) * 0.5f;

        StatusUI.Place(primaryButton, x, by, primaryW, buttonH);
        float labelW = LineWidth(primaryLabel);
        StatusUI.Place(primaryLabel.rectTransform, primaryPad, 0f, labelW + 2f, buttonH);
        if (welcome) StatusUI.Place(primaryIcon, primaryPad + labelW + 12f, (buttonH - 28f) * 0.5f, 28f, 28f);
        if (welcome)
        {
            StatusUI.Place(secondaryButton, x + primaryW + 20f, by, secondaryW, buttonH);
            StatusUI.Place(secondaryLabel.rectTransform, 32f, 0f, LineWidth(secondaryLabel) + 2f, buttonH);
        }

        float sy = signatureBelow ? y + rowH + 12f : y + (rowH - signatureH) * 0.5f;
        StatusUI.Place(signature, x + cw - 170f, sy, 170f, 44f);
        StatusUI.Place(signedLabel.rectTransform, x + cw - 300f, sy + 44f, 300f, signedH);
        return signatureBelow ? rowH + 12f + signatureH : rowH;
    }

    // ------------------------------------------------------------------
    // Spotlight and arrow
    // ------------------------------------------------------------------

    void SetSpot(Rect spot, float radius)
    {
        scrim.SetHole(spot, radius);
        // The design's outline: 4 px dashed yellow, 6 px outside the hole
        StatusUI.Place(ring.rectTransform, spot.x - 10f, spot.y - 10f, spot.width + 20f, spot.height + 20f);
        ring.Round(radius + 10f);
        ring.gameObject.SetActive(true);
    }

    void ClearSpot()
    {
        scrim.ClearHole();
        ring.gameObject.SetActive(false);
        arrowLayer.gameObject.SetActive(false);
    }

    /// <summary>The design's curved arrow: a quadratic curve bowed by <paramref name="bend"/>, its head along the end tangent.</summary>
    void DrawArrow(Vector2 from, Vector2 to, float bend)
    {
        Vector2 d = to - from;
        float length = d.magnitude;
        if (length < 8f)
        {
            arrowLayer.gameObject.SetActive(false);
            return;
        }
        Vector2 mid = (from + to) * 0.5f;
        Vector2 control = new Vector2(mid.x - d.y / length * bend, mid.y + d.x / length * bend);
        const int Steps = 28;
        var curve = new Vector2[Steps + 1];
        for (int i = 0; i <= Steps; i++)
        {
            float t = i / (float)Steps, u = 1f - t;
            curve[i] = Local(u * u * from + 2f * u * t * control + t * t * to);
        }
        Vector2 tangent = to - control;
        Vector2 dir = tangent.sqrMagnitude > 0.01f ? tangent.normalized : d / length;
        Vector2 headA = new Vector2(to.x - dir.x * 24f - dir.y * 13f, to.y - dir.y * 24f + dir.x * 13f);
        Vector2 headB = new Vector2(to.x - dir.x * 24f + dir.y * 13f, to.y - dir.y * 24f - dir.x * 13f);

        foreach (UIShape shape in new[] { arrowUnder, arrowInk })
        {
            shape.ClearStrokes();
            shape.AddStroke(curve);
            // The head as two strokes: round caps meet at the tip like the design's round joins
            shape.AddStroke(Local(headA), Local(to));
            shape.AddStroke(Local(to), Local(headB));
        }
        arrowLayer.gameObject.SetActive(true);
    }

    /// <summary>Overlay space (top-left, y down) to the arrow layer's local space (top-left pivot, y up).</summary>
    static Vector2 Local(Vector2 p) => new Vector2(p.x, -p.y);

    static Vector2 PointOn(Rect r, Vector4 anchor)
    {
        return new Vector2(r.x + anchor.x * r.width + anchor.y, r.y + anchor.z * r.height + anchor.w);
    }

    static Vector2 KeepToSide(Vector2 pos, float width, float height, Rect spot, NoteSide side)
    {
        switch (side)
        {
            case NoteSide.Right: pos.x = Mathf.Max(pos.x, spot.xMax + 40f); break;
            case NoteSide.Left: pos.x = Mathf.Min(pos.x, spot.xMin - 40f - width); break;
            case NoteSide.Above: pos.y = Mathf.Min(pos.y, spot.yMin - 40f - height); break;
            case NoteSide.Below: pos.y = Mathf.Max(pos.y, spot.yMax + 50f); break;
        }
        return pos;
    }

    // ------------------------------------------------------------------
    // Text measuring (CSS line boxes, like CssLineHeight)
    // ------------------------------------------------------------------

    /// <summary>The memo's side padding: its 3 px border plus 48 px.</summary>
    internal const float MemoEdge = 51f;

    /// <summary>The font's "normal" line height in em (Coming Soon: about 1.54).</summary>
    internal static float Natural(TMP_Text t)
    {
        TMP_FontAsset f = t != null ? t.font : null;
        return f != null && f.faceInfo.pointSize > 0f ? f.faceInfo.lineHeight / f.faceInfo.pointSize : 1.2f;
    }

    static float ContentEm(TMP_Text t)
    {
        TMP_FontAsset f = t != null ? t.font : null;
        return f != null && f.faceInfo.pointSize > 0f ? (f.faceInfo.ascentLine - f.faceInfo.descentLine) / f.faceInfo.pointSize : 1.2f;
    }

    /// <summary>
    /// Wrap <paramref name="t"/> at <paramref name="width"/> with every line <paramref name="cssLineHeight"/> x its size apart
    /// and return its CSS height (lines x line height). Keep the text's vertical alignment on Middle.
    /// </summary>
    internal static float Block(TMP_Text t, float width, float cssLineHeight)
    {
        if (t == null) return 0f;
        t.textWrappingMode = TextWrappingModes.Normal;
        t.lineSpacing = (cssLineHeight - Natural(t)) * 100f;
        if (string.IsNullOrEmpty(t.text)) return 0f;
        float tmp = t.GetPreferredValues(t.text, Mathf.Max(1f, width), 0f).y;
        float css = tmp - (ContentEm(t) - cssLineHeight) * t.fontSize;
        return Mathf.Ceil(Mathf.Max(cssLineHeight * t.fontSize, css));
    }

    /// <summary>The width of a text on one line.</summary>
    internal static float LineWidth(TMP_Text t)
    {
        if (t == null || string.IsNullOrEmpty(t.text)) return 0f;
        return Mathf.Ceil(t.GetPreferredValues(t.text, 100000f, 0f).x);
    }
}
