using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Building blocks for the runtime-made run-end screens (bankruptcy hearing, game over, win, debt chip) in the
/// "Stationery vs. Felt" look: the build mode kit's boxes and icons (BlueprintUI, BlueprintBox) plus the UI kit
/// (UIColors, UIFormat, MarkerButton, TextHighlighter). Only used when a view has no designed prefab wired in.
/// Positions are in 1920 x 1080 reference pixels, y down from a parent's top-left (like the design's CSS).
/// The screens' texts come from the "RunEnd" String Table (Assets/Localization/Source/RunEnd.csv).
/// </summary>
public static class RunEndUI
{
    /// <summary>The String Table collection of the run-end texts (Assets/Localization/Source/RunEnd.csv).</summary>
    public const string Table = "RunEnd";

    /// <summary>Name of the game font asset (Assets/UI/Fonts/ComingSoon SDF.asset).</summary>
    public const string FontAssetName = "ComingSoon SDF";
#if UNITY_EDITOR
    const string FontAssetPath = "Assets/UI/Fonts/ComingSoon SDF.asset";
#endif

    public static readonly Color SheetShadow = new Color(0f, 0f, 0f, 0.45f);
    /// <summary>The desk behind the paper (tractor-feed holes show it).</summary>
    public static readonly Color Desk = UIColors.Hex(0x2A332F);
    /// <summary>Highlighter marks: yellow (rgba 255,225,77,0.95), green and pink (0.85).</summary>
    public static readonly Color MarkYellow = new Color32(0xFF, 0xE1, 0x4D, 0xF2);
    public static readonly Color MarkGreen = new Color32(0x7E, 0xD6, 0x8E, 0xD9);
    public static readonly Color MarkPink = new Color32(0xFF, 0x96, 0xAA, 0xD9);

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    static TMP_FontAsset foundFont;
    static TextMeshProUGUI measurer;
    static Transform measureHost;

    // ------------------------------------------------------------------
    // Font
    // ------------------------------------------------------------------

    /// <summary>
    /// The font to build with: the view's own (Inspector), else the build mode screen's, else the Coming Soon asset that
    /// the HUD already has loaded, else (Editor only) the asset itself. Null = TextMesh Pro's default font.
    /// </summary>
    public static TMP_FontAsset ResolveFont(TMP_FontAsset preferred)
    {
        if (preferred != null) return preferred;
        if (TowerView.Instance != null && TowerView.Instance.font != null) return TowerView.Instance.font;
        if (foundFont != null) return foundFont;
        foreach (TMP_FontAsset candidate in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
        {
            if (candidate != null && candidate.name == FontAssetName)
            {
                foundFont = candidate;
                return foundFont;
            }
        }
#if UNITY_EDITOR
        foundFont = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
#endif
        return foundFont;
    }

    // ------------------------------------------------------------------
    // Canvas and big pieces
    // ------------------------------------------------------------------

    /// <summary>An overlay canvas like the game's (1920 x 1080, match 0.5) as a child of <paramref name="owner"/>.</summary>
    public static Canvas CreateCanvas(string name, int sortingOrder, Transform owner)
    {
        Canvas canvas = BlueprintUI.CreateOverlayCanvas(name, sortingOrder);
        if (owner != null) canvas.transform.SetParent(owner, false);
        measureHost = canvas.transform;
        return canvas;
    }

    /// <summary>Full-screen dim that also takes the clicks meant for what's behind.</summary>
    public static Image Scrim(Transform parent, Color color)
    {
        RectTransform rect = BlueprintUI.Stretch(BlueprintUI.NewRect("Scrim", parent));
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = true;
        return image;
    }

    /// <summary>A paper sheet: fill, 3 px ink border, hand-drawn corners and the design's soft drop shadow.</summary>
    public static BlueprintBox Sheet(Transform parent, string name, Color fill, Vector4 radii, float x, float y, float width, float height)
    {
        BlueprintBox box = BlueprintUI.Box(parent, name, fill, UIColors.Ink, 3f, radii, true);
        box.SetShadow(SheetShadow, new Vector2(0f, 22f), 40f);
        BlueprintUI.TopLeft(box.rectTransform, x, y, width, height);
        return box;
    }

    /// <summary>A rect inside <paramref name="parent"/>, inset by <paramref name="inset"/> px on every side (a sheet's padding box).</summary>
    public static RectTransform Inner(Transform parent, float inset)
    {
        return BlueprintUI.Stretch(BlueprintUI.NewRect("Inner", parent), inset, inset, inset, inset);
    }

    /// <summary>Masking tape across the top edge of a card.</summary>
    public static BlueprintBox Tape(Transform parent, float width, float cssRotation)
    {
        BlueprintBox tape = BlueprintUI.Box(parent, "Tape", UIColors.Tape, Color.clear, 0f, BlueprintUI.Round(0f));
        tape.SetShadow(new Color(0f, 0f, 0f, 0.18f), new Vector2(0f, 2f), 4f);
        RectTransform rect = tape.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(width, 38f);
        rect.anchoredPosition = new Vector2(0f, -3f);
        rect.localRotation = Rotation(cssRotation);
        return tape;
    }

    /// <summary>A small "Game paused" chip at the top centre of the screen (like the other paused pop-ups).</summary>
    public static void PausedChip(Transform parent, TMP_FontAsset font)
    {
        BlueprintBox paused = BlueprintUI.Box(parent, "Paused", UIColors.PaperHi, UIColors.Ink, 2f, BlueprintUI.RadiusSmall);
        paused.SetShadow(BlueprintUI.InkShadow, new Vector2(3f, 4f));
        RectTransform rect = paused.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(224f, 48f);
        rect.anchoredPosition = new Vector2(0f, -28f);
        BlueprintUI.HRow(paused, 10f, new RectOffset(14, 14, 0, 0), TextAnchor.MiddleCenter);
        BlueprintUI.Icon(paused.transform, BlueprintIconKind.Pause, 24f, UIColors.Ink);
        TextMeshProUGUI text = BlueprintUI.Text(paused.transform, "Text", "", 22f, UIColors.Ink, font);
        L10nText.Attach(text, Table, "runend.ui.paused");   // "Game paused" (the row centres it, whatever its length)
    }

    /// <summary>CSS rotate(deg) turns clockwise on screen; Unity's z rotation turns the other way.</summary>
    public static Quaternion Rotation(float cssDegrees)
    {
        return Quaternion.Euler(0f, 0f, -cssDegrees);
    }

    // ------------------------------------------------------------------
    // Text
    // ------------------------------------------------------------------

    /// <summary>A text placed at (x, y) from the parent's top-left, <paramref name="width"/> x <paramref name="height"/>.</summary>
    public static TextMeshProUGUI Text(Transform parent, string name, string text, float size, Color color, TMP_FontAsset font,
                                       float x, float y, float width, float height,
                                       TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft, bool bold = false)
    {
        TextMeshProUGUI label = BlueprintUI.Text(parent, name, text, size, color, font, alignment, bold);
        BlueprintUI.TopLeft(label.rectTransform, x, y, width, height);
        return label;
    }

    /// <summary>Wrap onto more lines with a CSS line-height (a multiple of the font size).</summary>
    public static TextMeshProUGUI Wrapped(TextMeshProUGUI text, float cssLineHeight)
    {
        text.textWrappingMode = TextWrappingModes.Normal;
        TMP_FontAsset font = text.font != null ? text.font : TMP_Settings.defaultFontAsset;
        float natural = font != null && font.faceInfo.pointSize > 0f ? font.faceInfo.lineHeight / font.faceInfo.pointSize : 1.2f;
        text.lineSpacing = (cssLineHeight - natural) * 100f;
        return text;
    }

    /// <summary>Shrink a long text (down to <paramref name="min"/>) instead of letting it spill out.</summary>
    public static TextMeshProUGUI Shrink(TextMeshProUGUI text, float min)
    {
        text.enableAutoSizing = true;
        text.fontSizeMax = text.fontSize;
        text.fontSizeMin = Mathf.Min(min, text.fontSize);
        // TMP also shrinks a line that is taller than its rect. Coming Soon is tall (ascent + descent = 1.5 em, more than
        // the design's CSS line boxes), so a single line gets a rect that tall, around the same centre: only width shrinks it.
        if (text.textWrappingMode == TextWrappingModes.NoWrap) FitLineHeight(text);
        return text;
    }

    static void FitLineHeight(TMP_Text text)
    {
        RectTransform rect = text.rectTransform;
        if (!Mathf.Approximately(rect.anchorMin.y, rect.anchorMax.y)) return;   // stretched vertically: its parent decides
        float delta = ContentEm(text) * text.fontSize + 2f - rect.sizeDelta.y;
        if (delta <= 0f) return;
        rect.sizeDelta += new Vector2(0f, delta);
        rect.anchoredPosition += new Vector2(0f, delta * (rect.pivot.y - 0.5f));
    }

    /// <summary>Ascent + descent of the text's font, in em (what TMP needs for one line).</summary>
    static float ContentEm(TMP_Text text)
    {
        TMP_FontAsset font = text.font != null ? text.font : TMP_Settings.defaultFontAsset;
        if (font == null || font.faceInfo.pointSize <= 0f) return 1.3f;
        return (font.faceInfo.ascentLine - font.faceInfo.descentLine) / font.faceInfo.pointSize;
    }

    /// <summary>The design's highlighter band behind the words wrapped in &lt;link="hl"&gt;...&lt;/link&gt;.</summary>
    public static TextHighlighter Highlight(TMP_Text text, Color color, float bandTop = 0.52f, float bandBottom = 0.90f, float paddingX = 6f)
    {
        TextHighlighter highlighter = text.gameObject.AddComponent<TextHighlighter>();
        highlighter.color = color;
        highlighter.bandTop = bandTop;
        highlighter.bandBottom = bandBottom;
        highlighter.paddingX = paddingX;
        return highlighter;
    }

    /// <summary>Wrap words for TextHighlighter.</summary>
    public static string Mark(string words)
    {
        return "<link=\"" + TextHighlighter.LinkId + "\">" + words + "</link>";
    }

    /// <summary>"#RRGGBB" for rich text colour tags.</summary>
    public static string Hex(Color color)
    {
        return "#" + ColorUtility.ToHtmlStringRGB(color);
    }

    /// <summary>Width of one line of text in <paramref name="font"/> (null = TMP default) at <paramref name="size"/>.</summary>
    public static float TextWidth(TMP_FontAsset font, string text, float size, bool bold = false, float letterSpacingEm = 0f)
    {
        if (string.IsNullOrEmpty(text)) return 0f;
        TextMeshProUGUI m = Measurer();
        TMP_FontAsset use = font != null ? font : TMP_Settings.defaultFontAsset;
        if (m == null || use == null) return text.Length * size * 0.55f;
        if (m.font != use) m.font = use;
        m.fontSize = size;
        m.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        m.characterSpacing = letterSpacingEm * 100f;
        m.textWrappingMode = TextWrappingModes.NoWrap;
        m.richText = true;
        return m.GetPreferredValues(text).x;
    }

    static TextMeshProUGUI Measurer()
    {
        if (measurer != null) return measurer;
        GameObject go = new GameObject("RunEnd Text Measure", typeof(RectTransform));
        go.hideFlags = HideFlags.HideInHierarchy;
        if (measureHost != null) go.transform.SetParent(measureHost, false);
        measurer = go.AddComponent<TextMeshProUGUI>();
        measurer.raycastTarget = false;
        measurer.enabled = false;   // only measures, never draws
        return measurer;
    }

    // ------------------------------------------------------------------
    // Buttons
    // ------------------------------------------------------------------

    /// <summary>
    /// The design's paper button: a face (fill, 3 px ink border, hand-drawn corners) over a hard ink shadow, an optional
    /// icon and a label. The kit's MarkerButton lifts the face on hover and presses it into the shadow (hidden while
    /// disabled). Width 0 = fit the content. Build it under an inactive parent so MarkerButton starts with its face set.
    /// </summary>
    public static Button PaperButton(Transform parent, string name, string label, RunEndIconKind icon, TMP_FontAsset font,
                                     Color fill, float width, float height, float fontSize, bool bold, Vector2 shadowOffset,
                                     out TextMeshProUGUI text, float padding = 36f, float iconSize = 28f)
    {
        if (width <= 0f)
            width = FitWidth(font, label, fontSize, bold, icon != RunEndIconKind.None ? iconSize : 0f, padding);

        RectTransform root = BlueprintUI.NewRect(name, parent);
        root.sizeDelta = new Vector2(width, height);
        Image hit = root.gameObject.AddComponent<Image>();
        hit.color = Color.clear;
        hit.raycastTarget = true;
        Button button = root.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = hit;
        button.navigation = new Navigation { mode = Navigation.Mode.None };

        BlueprintBox shadow = BlueprintUI.Box(root, "Shadow", UIColors.Ink, Color.clear, 0f, BlueprintUI.RadiusLarge);
        BlueprintUI.Stretch(shadow.rectTransform, shadowOffset.x, shadowOffset.y, -shadowOffset.x, -shadowOffset.y);
        BlueprintBox face = BlueprintUI.Box(root, "Face", fill, UIColors.Ink, 3f, BlueprintUI.RadiusLarge);
        BlueprintUI.Stretch(face.rectTransform);
        BlueprintUI.HRow(face, 12f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleCenter);
        if (icon != RunEndIconKind.None) RunEndIcon.Create(face.transform, icon, iconSize, UIColors.Ink);
        text = BlueprintUI.Text(face.transform, "Label", label, fontSize, UIColors.Ink, font, TextAlignmentOptions.Center, bold);
        // A longer label (another language) shrinks rather than spill over the face's edges
        text.enableAutoSizing = true;
        text.fontSizeMax = fontSize;
        text.fontSizeMin = Mathf.Round(fontSize * 0.7f);

        MarkerButton marker = root.gameObject.AddComponent<MarkerButton>();
        marker.face = face.rectTransform;
        marker.shadow = shadow.gameObject;
        return button;
    }

    /// <summary>
    /// A text link with an icon (the Win sheet's "Main menu"): underlined label (an Underline style, so it stays underlined
    /// whatever the label says later), no box.
    /// </summary>
    public static Button LinkButton(Transform parent, string name, string label, RunEndIconKind icon, TMP_FontAsset font,
                                    float fontSize, float height, out TextMeshProUGUI text, float iconSize = 28f)
    {
        float width = FitWidth(font, label, fontSize, false, icon != RunEndIconKind.None ? iconSize : 0f, 0f);
        RectTransform root = BlueprintUI.NewRect(name, parent);
        root.sizeDelta = new Vector2(width, height);
        Image hit = root.gameObject.AddComponent<Image>();
        hit.color = Color.clear;
        hit.raycastTarget = true;
        Button button = root.gameObject.AddComponent<Button>();
        button.targetGraphic = hit;
        button.transition = Selectable.Transition.None;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        BlueprintUI.HRow(root, 12f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft);
        if (icon != RunEndIconKind.None) RunEndIcon.Create(root, icon, iconSize, UIColors.Ink);
        text = BlueprintUI.Text(root, "Label", label, fontSize, UIColors.Ink, font, TextAlignmentOptions.Left);
        text.fontStyle |= FontStyles.Underline;
        return button;
    }

    static float FitWidth(TMP_FontAsset font, string label, float fontSize, bool bold, float iconSize, float padding)
    {
        float content = Mathf.Ceil(TextWidth(font, label, fontSize, bold)) + 4f;
        if (iconSize > 0f) content += iconSize + 12f;
        return content + padding * 2f;
    }

    /// <summary>Enable or disable a paper button; a disabled one fades (MarkerButton also hides its shadow).</summary>
    public static void SetEnabled(Button button, bool on)
    {
        BlueprintUI.SetInteractable(button, on);
    }

    /// <summary>Stop Enter/Space from clicking a button behind a pop-up that just opened.</summary>
    public static void ClearSelection()
    {
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    // ------------------------------------------------------------------
    // Stamps, coin
    // ------------------------------------------------------------------

    /// <summary>
    /// A rubber stamp: bold spaced capitals in <paramref name="color"/>, a border, an outer line (CSS outline + offset),
    /// rotated like the design and slightly see-through. Sized to its text; the caller positions it (pivot centre).
    /// </summary>
    public static RectTransform Stamp(Transform parent, string name, string label, float fontSize, Color color, TMP_FontAsset font,
                                      float border, float outlineWidth, float outlineOffset, float radius, float letterSpacingEm,
                                      float padX, float padY, float cssRotation, float alpha = 0.88f)
    {
        float width = Mathf.Ceil(TextWidth(font, label, fontSize, true, letterSpacingEm)) + padX * 2f + border * 2f;
        float height = fontSize * 1.1f + padY * 2f + border * 2f;
        BlueprintBox box = BlueprintUI.Box(parent, name, Color.clear, color, border, BlueprintUI.Round(radius));
        if (outlineWidth > 0f) box.SetOutline(color, outlineWidth, outlineOffset);
        RectTransform rect = box.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(width, height);
        rect.localRotation = Rotation(cssRotation);

        TextMeshProUGUI text = BlueprintUI.Text(rect, "Label", label, fontSize, color, font, TextAlignmentOptions.Midline, true);
        text.characterSpacing = letterSpacingEm * 100f;
        BlueprintUI.Stretch(text.rectTransform, border, 0f, border, 0f);

        CanvasGroup group = rect.gameObject.AddComponent<CanvasGroup>();
        group.alpha = alpha;
        group.blocksRaycasts = false;
        group.interactable = false;
        return rect;
    }

    /// <summary>Place a centre-pivot rect so its centre is at (x, y) from the parent's top-left.</summary>
    public static void PlaceCenter(RectTransform rect, float x, float y)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(x, -y);
    }

    /// <summary>
    /// The design's gold coin: a face with a radial shine, a dotted and a solid inner ring and a big letter, over a
    /// darker edge that gives it thickness. <paramref name="size"/> = face diameter; returns the coin (pivot centre).
    /// </summary>
    public static RectTransform Coin(Transform parent, string name, float size, TMP_FontAsset font, out TextMeshProUGUI letter)
    {
        float s = size / 260f;
        float edge = 12f * s;
        RectTransform coin = BlueprintUI.NewRect(name, parent);
        coin.sizeDelta = new Vector2(size, size + edge);

        BlueprintBox rim = BlueprintUI.Box(coin, "Edge", UIColors.GoldDark, UIColors.Ink, 4f, BlueprintUI.Round(size * 0.5f));
        BlueprintUI.TopLeft(rim.rectTransform, 0f, edge, size, size);

        BlueprintBox face = BlueprintUI.Box(coin, "Face", UIColors.Gold, UIColors.Ink, 4f, BlueprintUI.Round(size * 0.5f));
        face.SetGradient(UIColors.Hex(0xF7DC87), new Vector2(0.38f, 0.32f));
        BlueprintUI.TopLeft(face.rectTransform, 0f, 0f, size, size);

        BlueprintBox dotted = BlueprintUI.Box(face.transform, "Dots", Color.clear, UIColors.WithAlpha(UIColors.GoldDark, 0.8f), 3f * Mathf.Max(0.6f, s),
                                              BlueprintUI.Round(size * 0.5f - 10f * s));
        dotted.SetDash(true, 3f * Mathf.Max(0.6f, s), 5f * Mathf.Max(0.6f, s));
        BlueprintUI.Stretch(dotted.rectTransform, 10f * s, 10f * s, 10f * s, 10f * s);

        BlueprintBox ring = BlueprintUI.Box(face.transform, "Ring", Color.clear, UIColors.GoldDark, 2f, BlueprintUI.Round(size * 0.5f - 24f * s));
        BlueprintUI.Stretch(ring.rectTransform, 24f * s, 24f * s, 24f * s, 24f * s);

        TextMeshProUGUI glow = BlueprintUI.Text(face.transform, "LetterLight", "H", 128f * s, UIColors.Hex(0xF7DC87), font, TextAlignmentOptions.Center, true);
        BlueprintUI.Stretch(glow.rectTransform, 3f * s, 3f * s, -3f * s, -3f * s);
        letter = BlueprintUI.Text(face.transform, "Letter", "H", 128f * s, UIColors.GoldDark, font, TextAlignmentOptions.Center, true);
        BlueprintUI.Stretch(letter.rectTransform);

        coin.anchorMin = coin.anchorMax = new Vector2(0.5f, 0.5f);
        coin.pivot = new Vector2(0.5f, 0.5f);
        return coin;
    }

    /// <summary>Set the coin's letter (and the light copy behind it).</summary>
    public static void SetCoinLetter(TextMeshProUGUI letter, string value)
    {
        if (letter == null) return;
        if (letter.text != value) letter.text = value;
        Transform light = letter.transform.parent != null ? letter.transform.parent.Find("LetterLight") : null;
        TextMeshProUGUI glow = light != null ? light.GetComponent<TextMeshProUGUI>() : null;
        if (glow != null && glow.text != value) glow.text = value;
    }

    // ------------------------------------------------------------------
    // Numbers
    // ------------------------------------------------------------------

    /// <summary>Short money for cover lines: "+$2.1M", "−$212K", "$950", "$0". <paramref name="signed"/> adds "+" to gains.</summary>
    public static string CompactMoney(double value, bool signed)
    {
        double abs = System.Math.Abs(System.Math.Round(value));
        string sign = value < -0.5 ? UIFormat.Minus : signed && value > 0.5 ? "+" : "";
        string body;
        if (abs >= 1e12) body = (abs / 1e12).ToString("0.#", Inv) + "T";
        else if (abs >= 1e9) body = (abs / 1e9).ToString("0.#", Inv) + "B";
        else if (abs >= 1e6) body = (abs / 1e6).ToString("0.#", Inv) + "M";
        else if (abs >= 1e4) body = (abs / 1e3).ToString("0", Inv) + "K";
        else body = abs.ToString("N0", Inv);
        return sign + "$" + body;
    }

    /// <summary>
    /// Build a runtime screen in the current language, and again when the language has changed since (its layout is
    /// measured by its words): a view calls this where it would build. <paramref name="canvas"/> and
    /// <paramref name="builtLanguage"/> are the view's own fields; <paramref name="build"/> makes the screen (a new canvas
    /// and everything on it) and returns the canvas. Returns true when it (re)built. A screen on show is never rebuilt.
    /// </summary>
    public static bool EnsureBuiltInLanguage(ref Canvas canvas, ref string builtLanguage, bool showing, System.Func<Canvas> build)
    {
        string language = L10n.CurrentCode;
        if (canvas != null && (builtLanguage == language || showing)) return false;
        if (canvas != null) Object.Destroy(canvas.gameObject);   // built in another language
        canvas = build();
        builtLanguage = language;
        return canvas != null;
    }
}
