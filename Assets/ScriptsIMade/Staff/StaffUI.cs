using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Building blocks for the staff files built at runtime, in the "Stationery vs. Felt" look: the build mode kit's boxes
/// (BlueprintUI, BlueprintBox), StaffIcon and the UI kit (UIColors, UIFormat, MarkerButton). Only used when the view has
/// no designed prefab wired in. Positions are 1920 x 1080 reference pixels from a parent's top-left corner, y down, like
/// the design's CSS; single-line texts are placed by the centre of their line.
/// </summary>
public static class StaffUI
{
    /// <summary>Name of the game font asset (Assets/UI/Fonts/ComingSoon SDF.asset).</summary>
    public const string FontAssetName = "ComingSoon SDF";
#if UNITY_EDITOR
    const string FontAssetPath = "Assets/UI/Fonts/ComingSoon SDF.asset";
#endif

    public static readonly Color SheetShadow = new Color(0f, 0f, 0f, 0.45f);
    public static readonly Color RowShadow = new Color32(29, 36, 51, 140);     // rgba(29,36,51,0.55)
    public static readonly Color Desk = UIColors.Hex(0x2A332F);

    static TMP_FontAsset foundFont;

    /// <summary>
    /// The font to build with: the view's own (Inspector), else the Coming Soon asset the HUD already has loaded, else
    /// (Editor only) the asset itself. Null = TextMesh Pro's default font.
    /// </summary>
    public static TMP_FontAsset ResolveFont(TMP_FontAsset preferred)
    {
        if (preferred != null) return preferred;
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

    /// <summary>An overlay canvas like the game's (1920 x 1080, match 0.5) that takes clicks, as a child of <paramref name="owner"/>.</summary>
    public static Canvas CreateCanvas(string name, int sortingOrder, Transform owner)
    {
        var go = new GameObject(name, typeof(RectTransform));
        int uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer >= 0) go.layer = uiLayer;
        if (owner != null && owner.GetComponentInParent<Canvas>(true) == null)
            go.transform.SetParent(owner, false);

        Canvas canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
        CanvasScaler scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        go.AddComponent<GraphicRaycaster>();
        BlueprintUI.EnsureEventSystem();
        if (measureHost == null) measureHost = canvas.transform;
        return canvas;
    }

    /// <summary>Stop Enter/Space from clicking a button behind a pop-up that just opened.</summary>
    public static void ClearSelection()
    {
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    // ------------------------------------------------------------------
    // Shapes
    // ------------------------------------------------------------------

    /// <summary>A box placed at (x, y) from the parent's top-left.</summary>
    public static BlueprintBox Box(Transform parent, string name, Color fill, Color border, float borderWidth, Vector4 radii,
                                   float x, float y, float width, float height, bool raycast = false)
    {
        BlueprintBox box = BlueprintUI.Box(parent, name, fill, border, borderWidth, radii, raycast);
        BlueprintUI.TopLeft(box.rectTransform, x, y, width, height);
        return box;
    }

    /// <summary>A plain rectangle that takes the clicks meant for what's behind it (scrims, click-away blockers).</summary>
    public static Image Blocker(Transform parent, string name, Color color)
    {
        RectTransform rect = BlueprintUI.Stretch(BlueprintUI.NewRect(name, parent));
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = true;
        return image;
    }

    /// <summary>An icon placed at (x, y) from the parent's top-left.</summary>
    public static StaffIcon Icon(Transform parent, StaffIconKind kind, float x, float y, float size, Color color, float stroke = 2.4f)
    {
        StaffIcon icon = StaffIcon.Create(parent, kind, size, color, stroke);
        BlueprintUI.TopLeft(icon.rectTransform, x, y, size, size);
        return icon;
    }

    /// <summary>A vertical dashed line (the green-bar paper's tractor-feed perforation).</summary>
    public static void DashedLine(Transform parent, float x, float top, float bottom, float width, Color color, float dash, float gap)
    {
        for (float y = top; y < bottom; y += dash + gap)
            Box(parent, "Dash", color, Color.clear, 0f, Vector4.zero, x, y, width, Mathf.Min(dash, bottom - y));
    }

    /// <summary>Green-bar ledger paper: fill, bands every <paramref name="band"/> px, tractor-feed holes down the left edge.</summary>
    public static BlueprintBox Ledger(Transform parent, string name, float x, float y, float width, float height, float band = 34f)
    {
        BlueprintBox paper = Box(parent, name, UIColors.Ledger, UIColors.Ink, 2f, BlueprintUI.RadiusSmall, x, y, width, height);
        Transform t = paper.transform;
        for (float top = band; top < height - 2f; top += band * 2f)
            Box(t, "Band", UIColors.LedgerBand, Color.clear, 0f, Vector4.zero, 2f, top, width - 4f, Mathf.Min(band, height - 2f - top));
        for (float cy = band * 0.5f; cy + 6f < height; cy += band)
            Box(t, "Hole", Desk, Color.clear, 0f, BlueprintUI.Round(6f), 7f, cy - 6f, 12f, 12f);
        DashedLine(t, 24f, 2f, height - 2f, 2f, UIColors.LedgerLine, 5f, 4f);
        return paper;
    }

    // ------------------------------------------------------------------
    // Text
    // ------------------------------------------------------------------

    /// <summary>
    /// One line of text whose line centre is at <paramref name="centerY"/>. The rect is taller than the font's line
    /// (Coming Soon is tall), so TMP never hides it for not fitting vertically.
    /// </summary>
    public static TextMeshProUGUI Line(Transform parent, string name, string text, float size, Color color, TMP_FontAsset font,
                                       float x, float centerY, float width,
                                       TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft, bool bold = false)
    {
        TextMeshProUGUI label = BlueprintUI.Text(parent, name, text, size, color, font, alignment, bold);
        float height = Mathf.Ceil(size * 1.7f);
        BlueprintUI.TopLeft(label.rectTransform, x, centerY - height * 0.5f, width, height);
        return label;
    }

    /// <summary>Wrapped text in a box, with a CSS line-height (a multiple of the font size).</summary>
    public static TextMeshProUGUI Block(Transform parent, string name, string text, float size, Color color, TMP_FontAsset font,
                                        float x, float y, float width, float height, float cssLineHeight,
                                        TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft)
    {
        TextMeshProUGUI label = BlueprintUI.Text(parent, name, text, size, color, font, alignment);
        BlueprintUI.TopLeft(label.rectTransform, x, y, width, height);
        label.textWrappingMode = TextWrappingModes.Normal;
        label.lineSpacing = LineSpacing(label, cssLineHeight);
        return label;
    }

    /// <summary>TMP line spacing that gives a CSS line-height (a multiple of the font size) in the text's font.</summary>
    public static float LineSpacing(TMP_Text text, float cssLineHeight)
    {
        TMP_FontAsset face = text != null && text.font != null ? text.font : TMP_Settings.defaultFontAsset;
        float natural = face != null && face.faceInfo.pointSize > 0f ? face.faceInfo.lineHeight / face.faceInfo.pointSize : 1.2f;
        return (cssLineHeight - natural) * 100f;
    }

    /// <summary>Shrink a long line (down to <paramref name="min"/>) instead of letting it spill out.</summary>
    public static TextMeshProUGUI Shrink(TextMeshProUGUI text, float min)
    {
        text.enableAutoSizing = true;
        text.fontSizeMax = text.fontSize;
        text.fontSizeMin = Mathf.Min(min, text.fontSize);
        return text;
    }

    /// <summary>Cut a long line with "..." (names).</summary>
    public static TextMeshProUGUI Ellipsis(TextMeshProUGUI text)
    {
        text.overflowMode = TextOverflowModes.Ellipsis;
        return text;
    }

    /// <summary>
    /// A capitals label's CSS letter-spacing in em. The text itself comes in capitals (written so in the String Table): no
    /// UpperCase style, which would turn a Turkish "i" into "I" instead of "İ".
    /// </summary>
    public static TextMeshProUGUI Caps(TextMeshProUGUI text, float letterSpacingEm)
    {
        text.characterSpacing = letterSpacingEm * 100f;
        return text;
    }

    /// <summary>Width of <paramref name="text"/> on one line in <paramref name="like"/>'s font, size, weight and spacing.</summary>
    public static float Measure(TMP_Text like, string text)
    {
        if (like == null) return 0f;
        return Measure(like.font, text, like.fontSize, (like.fontStyle & FontStyles.Bold) != 0, like.characterSpacing / 100f);
    }

    /// <summary>
    /// Width of one line of text. A hidden, always-active measurer does it: the texts being built sit under an inactive
    /// parent (so MarkerButton starts right), where TMP hasn't woken up yet.
    /// </summary>
    public static float Measure(TMP_FontAsset font, string text, float size, bool bold, float letterSpacingEm = 0f)
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

    static TextMeshProUGUI measurer;
    static Transform measureHost;

    static TextMeshProUGUI Measurer()
    {
        if (measurer != null) return measurer;
        GameObject go = new GameObject("Staff Text Measure", typeof(RectTransform));
        go.hideFlags = HideFlags.HideAndDontSave;
        if (measureHost != null) go.transform.SetParent(measureHost, false);
        measurer = go.AddComponent<TextMeshProUGUI>();
        measurer.raycastTarget = false;
        measurer.enabled = false;   // only measures, never draws
        return measurer;
    }

    /// <summary>CSS rotate(deg) turns clockwise on screen; Unity's z rotation turns the other way.</summary>
    public static Quaternion Rotation(float cssDegrees)
    {
        return Quaternion.Euler(0f, 0f, -cssDegrees);
    }

    /// <summary>"#RRGGBB" for rich text colour tags.</summary>
    public static string Hex(Color color)
    {
        return "#" + ColorUtility.ToHtmlStringRGB(color);
    }

    // ------------------------------------------------------------------
    // Buttons
    // ------------------------------------------------------------------

    /// <summary>
    /// The design's marker-pen button: a face (fill, 3 px ink border, hand-drawn corners) over a hard ink shadow, with an
    /// optional icon and a label in <paramref name="ink"/>. MarkerButton lifts the face on hover and presses it into the
    /// shadow. Width 0 = fit the content. A StaffButtonFit lays it out again when the label changes (another language).
    /// Build it under an inactive parent so MarkerButton starts with its face set.
    /// </summary>
    public static Button PaperButton(Transform parent, string name, string label, StaffIconKind icon, TMP_FontAsset font,
                                     Color fill, Color ink, float x, float y, float width, float height, float fontSize, bool bold,
                                     Vector2 shadowOffset, Vector4 radii, out TextMeshProUGUI text, float iconSize = 24f, float padding = 24f)
    {
        RectTransform root = BlueprintUI.NewRect(name, parent);
        Image hit = root.gameObject.AddComponent<Image>();
        hit.color = Color.clear;
        hit.raycastTarget = true;
        Button button = root.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = hit;
        button.navigation = new Navigation { mode = Navigation.Mode.None };

        BlueprintBox shadow = BlueprintUI.Box(root, "Shadow", UIColors.Ink, Color.clear, 0f, radii);
        BlueprintUI.Stretch(shadow.rectTransform, shadowOffset.x, shadowOffset.y, -shadowOffset.x, -shadowOffset.y);
        BlueprintBox face = BlueprintUI.Box(root, "Face", fill, UIColors.Ink, 3f, radii);
        BlueprintUI.Stretch(face.rectTransform);

        text = BlueprintUI.Text(face.transform, "Label", label, fontSize, ink, font, TextAlignmentOptions.Midline, bold);
        BlueprintUI.TopLeft(root, x, y, Mathf.Max(0f, width), height);
        StaffIcon glyph = icon != StaffIconKind.None ? Icon(face.transform, icon, 0f, 0f, iconSize, ink) : null;
        text.alignment = icon != StaffIconKind.None ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Midline;
        Shrink(text, Mathf.Round(fontSize * 0.7f));

        // Icon and label centred together, the width fitted to the content when none is given: now and whenever the
        // label changes (a language change)
        StaffButtonFit fit = root.gameObject.AddComponent<StaffButtonFit>();
        fit.label = text;
        fit.icon = glyph != null ? glyph.rectTransform : null;
        fit.iconSize = iconSize;
        fit.fontSize = fontSize;
        fit.fitWidth = width <= 0f;
        fit.padding = padding;
        fit.Layout();

        MarkerButton marker = root.gameObject.AddComponent<MarkerButton>();
        marker.face = face.rectTransform;
        marker.shadow = shadow.gameObject;
        return button;
    }

    /// <summary>A round paper button with an icon (close, pager arrows).</summary>
    public static Button RoundButton(Transform parent, string name, StaffIconKind icon, float x, float y, float size, float iconSize,
                                     Vector2 shadowOffset, out BlueprintBox face)
    {
        RectTransform root = BlueprintUI.NewRect(name, parent);
        BlueprintUI.TopLeft(root, x, y, size, size);
        Image hit = root.gameObject.AddComponent<Image>();
        hit.color = Color.clear;
        hit.raycastTarget = true;
        Button button = root.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = hit;
        button.navigation = new Navigation { mode = Navigation.Mode.None };

        Vector4 round = new Vector4(size * 0.5f, size * 0.46f, size * 0.52f, size * 0.48f);
        BlueprintBox shadow = BlueprintUI.Box(root, "Shadow", UIColors.Ink, Color.clear, 0f, round);
        BlueprintUI.Stretch(shadow.rectTransform, shadowOffset.x, shadowOffset.y, -shadowOffset.x, -shadowOffset.y);
        face = BlueprintUI.Box(root, "Face", UIColors.PaperHi, UIColors.Ink, 3f, round);
        BlueprintUI.Stretch(face.rectTransform);
        StaffIcon glyph = Icon(face.transform, icon, (size - iconSize) * 0.5f, (size - iconSize) * 0.5f, iconSize, UIColors.Ink);
        glyph.name = "Glyph";

        MarkerButton marker = root.gameObject.AddComponent<MarkerButton>();
        marker.face = face.rectTransform;
        marker.shadow = shadow.gameObject;
        return button;
    }

    /// <summary>Enable or disable a paper button; a disabled one fades (MarkerButton also hides its shadow).</summary>
    public static void SetEnabled(Button button, bool on)
    {
        BlueprintUI.SetInteractable(button, on);
    }

    // ------------------------------------------------------------------
    // Stamps
    // ------------------------------------------------------------------

    /// <summary>
    /// A rubber stamp: bold spaced capitals, a border and an outer line (CSS outline + offset), turned like the design and
    /// slightly see-through. <paramref name="lines"/> may hold a line break. Centred on (cx, cy) of the parent.
    /// </summary>
    public static TextMeshProUGUI Stamp(Transform parent, string name, string lines, float fontSize, Color color, TMP_FontAsset font,
                                        float cx, float cy, float cssRotation, out BlueprintBox box, out BlueprintBox outline)
    {
        box = BlueprintUI.Box(parent, name, Color.clear, color, 3f, BlueprintUI.Round(8f));
        RectTransform rect = box.rectTransform;
        TextMeshProUGUI text = BlueprintUI.Text(rect, "Label", lines, fontSize, color, font, TextAlignmentOptions.Center, true);
        text.characterSpacing = 10f;   // 0.1em
        text.lineSpacing = LineSpacing(text, 1.05f);
        float width = 0f;
        foreach (string line in lines.Split('\n'))
            width = Mathf.Max(width, Measure(text, line));
        int count = lines.Split('\n').Length;
        float height = count * fontSize * 1.05f + 12f + 6f;
        width = Mathf.Ceil(width) + 28f + 6f;

        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(cx, -cy);
        rect.localRotation = Rotation(cssRotation);
        BlueprintUI.Stretch(text.rectTransform, 3f, -8f, 3f, -8f);

        // The outer line of the design's outline: a second box 3 px outside the first
        outline = BlueprintUI.Box(rect, "Outline", Color.clear, color, 2f, BlueprintUI.Round(11f));
        BlueprintUI.Stretch(outline.rectTransform, -5f, -5f, -5f, -5f);
        outline.transform.SetAsFirstSibling();

        // As wide as its longest line, whatever it says later (the record stamp changes its words, a language its font)
        StatusFitWidth fit = rect.gameObject.AddComponent<StatusFitWidth>();
        fit.text = text;
        fit.padding = 28f + 6f;

        CanvasGroup group = rect.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0.9f;
        group.blocksRaycasts = false;
        group.interactable = false;
        return text;
    }
}

/// <summary>
/// Numbers and words of the staff files ("$230/day", "12 days", "6 yrs experience"), in the current language (the "Staff"
/// String Table).
/// </summary>
public static class StaffText
{
    const string T = StaffLedger.Table;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>"12 days", "1 day".</summary>
    public static string Days(int days)
    {
        return L10n.Get(T, "staff.days", ("days", days));
    }

    /// <summary>"6 yrs experience", "1 yr experience", "No experience".</summary>
    public static string Experience(int years)
    {
        if (years <= 0) return L10n.Get(T, "staff.experience.none");
        return L10n.Get(T, "staff.experience", ("years", years));
    }

    /// <summary>"29 · Female · Mixed": gender and ethnicity in the current language (ApplicantText; the data keeps the English).</summary>
    public static string Meta(ApplicantData person)
    {
        if (person == null) return "";
        string text = person.age > 0 ? person.age.ToString(Inv) : "";
        if (!string.IsNullOrEmpty(person.gender)) text += (text.Length > 0 ? " · " : "") + ApplicantText.Gender(person.gender);
        if (!string.IsNullOrEmpty(person.race)) text += (text.Length > 0 ? " · " : "") + ApplicantText.Race(person.race);
        return text;
    }

    /// <summary>"Hired day 41", "On file since day 12" (someone from a save older than the staff files).</summary>
    public static string Hired(StaffStats stats)
    {
        return L10n.Get(T, stats.hireDayKnown ? "staff.hired" : "staff.hired.on_file", ("day", UIFormat.Int(stats.hireDay)));
    }

    /// <summary>"3.62".</summary>
    public static string Gpa(float gpa)
    {
        return gpa.ToString("0.00", Inv);
    }

    /// <summary>A short file number from the employee id: "3F9A2C".</summary>
    public static string FileNumber(string id)
    {
        if (string.IsNullOrEmpty(id)) return "-";
        return (id.Length > 6 ? id.Substring(0, 6) : id).ToUpperInvariant();
    }
}
