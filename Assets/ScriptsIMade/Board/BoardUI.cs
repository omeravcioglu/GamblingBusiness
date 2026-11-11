using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Building blocks for the board screens built at runtime (the meeting memo and the target chip) in the "Stationery vs.
/// Felt" look of the promotion memo: the status kit's rounded boxes (StatusBox, StatusUI) and the UI kit (UIColors,
/// UIFormat, MarkerButton, UIPolyline). Only used when a view has no designed prefab wired in.
/// Positions are 1920 x 1080 reference pixels, x and y from the parent's top-left corner, y down (like the design's CSS).
/// </summary>
public static class BoardUI
{
    /// <summary>Name of the game font asset (Assets/UI/Fonts/ComingSoon SDF.asset).</summary>
    public const string FontAssetName = "ComingSoon SDF";
    const string DesignFontFamily = "Coming Soon";
#if UNITY_EDITOR
    const string FontAssetPath = "Assets/UI/Fonts/ComingSoon SDF.asset";
#endif

    /// <summary>The Board's letterhead colours (the promotion memo's navy and brass).</summary>
    public static readonly Color Navy = UIColors.Hex(0x22385C);
    public static readonly Color Brass = UIColors.Hex(0xC2A04A);
    public static readonly Color ClipBrass = UIColors.Hex(0x7D6424);
    public static readonly Color ClipShine = UIColors.Hex(0xE3C981);

    /// <summary>Hand-drawn corners (top-left, top-right, bottom-right, bottom-left).</summary>
    public static readonly Vector4 PaperRadii = new Vector4(23f, 22f, 23f, 25f);
    public static readonly Vector4 CardRadii = new Vector4(16f, 19f, 15f, 20f);
    public static readonly Vector4 BoxRadii = new Vector4(12f, 14f, 11f, 15f);
    public static readonly Vector4 ButtonRadii = new Vector4(17f, 16f, 17f, 18f);
    public static readonly Vector4 SmallRadii = new Vector4(10f, 13f, 9f, 14f);

    /// <summary>Coming Soon's line box is about 1.55 em tall: text boxes are at least this many times the font size.</summary>
    public const float LineBox = 1.55f;

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static TMP_FontAsset foundFont;

    /// <summary>
    /// The font to build with: the view's own (Inspector), else the Coming Soon asset the HUD already has loaded (by asset
    /// name, then by any scene text using the family), else (Editor only) the asset itself. Null = TextMesh Pro's default.
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
        foreach (TMP_Text text in Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            TMP_FontAsset asset = text != null ? text.font : null;
            if (asset != null && asset.faceInfo.familyName == DesignFontFamily)
            {
                foundFont = asset;
                return foundFont;
            }
        }
#if UNITY_EDITOR
        foundFont = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
#endif
        return foundFont;
    }

    /// <summary>
    /// An overlay canvas like the game's (1920 x 1080, match 0.5) as a child of <paramref name="owner"/> (at the scene root
    /// when the owner sits inside another canvas). <paramref name="takesClicks"/> adds a GraphicRaycaster.
    /// </summary>
    public static Canvas CreateCanvas(string name, int sortingOrder, Transform owner, bool takesClicks)
    {
        var go = new GameObject(name, typeof(RectTransform));
        int uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer >= 0) go.layer = uiLayer;
        if (owner != null && owner.GetComponentInParent<Canvas>(true) == null)
            go.transform.SetParent(owner, false);

        Canvas canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        CanvasScaler scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        if (takesClicks)
        {
            go.AddComponent<GraphicRaycaster>();
            EnsureEventSystem();
        }
        return canvas;
    }

    static void EnsureEventSystem()
    {
        if (EventSystem.current != null || Object.FindAnyObjectByType<EventSystem>() != null) return;
        new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
    }

    /// <summary>Stop Enter/Space from clicking a button behind a pop-up that just opened.</summary>
    public static void ClearSelection()
    {
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    // ------------------------------------------------------------------
    // Shapes and text
    // ------------------------------------------------------------------

    /// <summary>A plain coloured rectangle (a scrim takes the clicks meant for what's behind it).</summary>
    public static Image Block(RectTransform parent, string name, Color color, bool takesClicks = false)
    {
        Image image = StatusUI.NewRect(name, parent).gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = takesClicks;
        return image;
    }

    /// <summary>A text placed at (x, y) from the parent's top-left, <paramref name="width"/> x <paramref name="height"/>.</summary>
    public static TextMeshProUGUI Text(RectTransform parent, string name, string text, float size, Color color, TMP_FontAsset font,
                                       float x, float y, float width, float height,
                                       TextAlignmentOptions alignment = TextAlignmentOptions.Left, bool bold = false)
    {
        TextMeshProUGUI label = StatusUI.Text(parent, name, text, size, color, font, alignment, bold);
        StatusUI.Place(label.rectTransform, x, y, width, height);
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
        return text;
    }

    /// <summary>
    /// The design's marker-pen button on <paramref name="rect"/>: a face (fill, ink border, hand-drawn corners) over a hard
    /// ink shadow. MarkerButton lifts the face on hover and presses it into the shadow. Returns the button; its content goes
    /// in <paramref name="face"/>. Build it under an inactive parent so MarkerButton starts with its face in place.
    /// </summary>
    public static Button MakeMarkerButton(RectTransform rect, Color fill, float borderWidth, Vector4 radii, Vector2 shadowOffset, out StatusBox face)
    {
        StatusBox shadow = StatusUI.Box(rect, "Shadow", UIColors.Ink, Color.clear, 0f, radii);
        StatusUI.Stretch(shadow.rectTransform);
        shadow.rectTransform.anchoredPosition = new Vector2(shadowOffset.x, -shadowOffset.y);

        face = StatusUI.Box(rect, "Face", fill, UIColors.Ink, borderWidth, radii);
        StatusUI.Stretch(face.rectTransform);
        face.raycastTarget = true;   // takes the button's clicks

        Button button = rect.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;   // MarkerButton draws the states
        button.targetGraphic = face;
        button.navigation = new Navigation { mode = Navigation.Mode.None };

        MarkerButton marker = rect.gameObject.AddComponent<MarkerButton>();
        marker.face = face.rectTransform;
        marker.shadow = shadow.gameObject;
        marker.pressOffset = new Vector2(shadowOffset.x, -shadowOffset.y);   // pressed right into the shadow
        return button;
    }

    /// <summary>A labelled marker-pen button at (x, y), <paramref name="width"/> x <paramref name="height"/>.</summary>
    public static Button PaperButton(RectTransform parent, string name, string label, Color fill, TMP_FontAsset font, float fontSize,
                                     float x, float y, float width, float height, out TextMeshProUGUI text)
    {
        RectTransform rect = StatusUI.Place(StatusUI.NewRect(name, parent), x, y, width, height);
        Button button = MakeMarkerButton(rect, fill, 3f, ButtonRadii, new Vector2(4f, 5f), out StatusBox face);
        text = StatusUI.Text(face.rectTransform, "Label", label, fontSize, UIColors.Ink, font, TextAlignmentOptions.Center, true);
        StatusUI.Stretch(text.rectTransform, 18f, 18f, 0f, 0f);
        Shrink(text, Mathf.Round(fontSize * 0.7f));
        return button;
    }

    /// <summary>
    /// A rubber stamp: a word in a thick border with a thin line around it, turned <paramref name="degrees"/>
    /// (positive = counter-clockwise), centred at (x, y) and as wide as its word plus padding. Its graphics are returned in
    /// <paramref name="tinted"/> (borders and word) so a view can recolour it. Fades via its CanvasGroup (starts at 0.88).
    /// </summary>
    public static RectTransform Stamp(RectTransform parent, string name, string word, Color color, TMP_FontAsset font, float fontSize,
                                      float centerX, float centerY, float height, float degrees, out TextMeshProUGUI text, out Graphic[] tinted)
    {
        float width = fontSize * 4f;
        RectTransform stamp = StatusUI.PlaceRotated(StatusUI.NewRect(name, parent), centerX - width * 0.5f, centerY - height * 0.5f, width, height, degrees);

        StatusBox wash = StatusUI.Box(stamp, "Wash", UIColors.WithAlpha(UIColors.PaperHi, 0.55f), Color.clear, 0f, Vector4.one * 10f);
        StatusUI.Stretch(wash.rectTransform);
        StatusBox ring = StatusUI.Box(stamp, "Ring", Color.clear, color, 5f, Vector4.one * 10f);
        StatusUI.Stretch(ring.rectTransform);
        StatusBox outline = StatusUI.Box(stamp, "Outline", Color.clear, color, 2f, Vector4.one * 14f);
        StatusUI.Stretch(outline.rectTransform, -6f, -6f, -6f, -6f);

        text = StatusUI.Text(stamp, "Word", word, fontSize, color, font, TextAlignmentOptions.Center, true);
        text.characterSpacing = 10f;
        StatusUI.Stretch(text.rectTransform, 20f, 20f, 0f, 0f);

        StatusFitWidth fit = stamp.gameObject.AddComponent<StatusFitWidth>();
        fit.text = text;
        fit.padding = 44f;

        CanvasGroup group = stamp.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0.88f;
        group.blocksRaycasts = false;
        group.interactable = false;

        tinted = new Graphic[] { ring, outline, text };
        return stamp;
    }

    /// <summary>Recolour a stamp's graphics: a StatusBox gets its border set, anything else its colour.</summary>
    public static void Tint(Graphic[] graphics, Color color)
    {
        if (graphics == null) return;
        foreach (Graphic graphic in graphics)
        {
            if (graphic == null) continue;
            if (graphic is StatusBox box) box.Border = color;
            else graphic.color = color;
        }
    }

    /// <summary>A dashed horizontal rule (one graphic with round-capped dashes) at (x, y), <paramref name="length"/> long.</summary>
    public static UIPolyline DashedRule(RectTransform parent, string name, Color color, float thickness, float x, float y, float length,
                                        float dash = 8f, float gap = 6f)
    {
        RectTransform rect = StatusUI.Place(StatusUI.NewRect(name, parent), x, y - thickness, length, thickness * 2f);
        rect.pivot = new Vector2(0f, 1f);   // local (0, 0) = the top-left corner, like StatusUI.Stroke
        UIPolyline line = rect.gameObject.AddComponent<UIPolyline>();
        line.color = color;
        line.width = thickness;
        line.raycastTarget = false;

        // Round caps reach half the thickness past each end: shorten the paths so every dash is `dash` long
        float half = thickness * 0.5f;
        for (float from = 0f; from < length; from += dash + gap)
        {
            float to = Mathf.Min(length, from + dash);
            if (to - from <= thickness) break;
            line.AddPath(StatusUI.Points(new Vector2(from + half, thickness), new Vector2(to - half, thickness)));
        }
        return line;
    }

    // ------------------------------------------------------------------
    // Words and numbers
    // ------------------------------------------------------------------

    /// <summary>"#RRGGBB" for rich text colour tags.</summary>
    public static string Hex(Color color)
    {
        return "#" + ColorUtility.ToHtmlStringRGB(color);
    }

    /// <summary>Wrap <paramref name="text"/> in a colour tag.</summary>
    public static string Colored(string text, Color color)
    {
        return "<color=" + Hex(color) + ">" + text + "</color>";
    }

    /// <summary>
    /// Short money for tight spots: "$99,999" in full below $100,000, then "$456.8K", "$12.35M", "$1.2B" (negative values get
    /// the minus sign).
    /// </summary>
    public static string CompactMoney(double value)
    {
        double rounded = System.Math.Round(value);
        double size = System.Math.Abs(rounded);
        if (size < 100000.0) return UIFormat.Money(rounded);
        // Each unit takes over where the smaller one would round up to 1000 ("$1000K")
        string body;
        if (size < 999950.0) body = (size / 1e3).ToString("0.#", Inv) + "K";
        else if (size < 999995000.0) body = (size / 1e6).ToString("0.##", Inv) + "M";
        else body = (size / 1e9).ToString("0.##", Inv) + "B";
        return (rounded < 0.0 ? UIFormat.Minus : "") + "$" + body;
    }

    /// <summary>CompactMoney with a sign: "+$9,900", "−$2,000", "+$1.5M", "$0".</summary>
    public static string SignedCompactMoney(double value)
    {
        double rounded = System.Math.Round(value);
        if (rounded > 0.0) return "+" + CompactMoney(rounded);
        return CompactMoney(rounded);
    }

    /// <summary>0.02 -> "2%", 0.025 -> "2.5%" (Console and debugging; the player's texts put the % sign in their String Table entry).</summary>
    public static string Percent(double share)
    {
        return PercentNumber(share) + "%";
    }

    /// <summary>0.02 -> "2", 0.025 -> "2.5": a share as the number for a String Table entry's {percent} (Turkish writes %2).</summary>
    public static string PercentNumber(double share)
    {
        return (share * 100.0).ToString("0.#", Inv);
    }

    /// <summary>1.5 -> "1.5", 3 -> "3".</summary>
    public static string Number(float value)
    {
        return value.ToString("0.#", Inv);
    }
}
