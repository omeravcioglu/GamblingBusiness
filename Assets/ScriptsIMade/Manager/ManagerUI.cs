using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Building blocks for the manager's runtime screens (the side chip, the desk, the candidate pick) in the "Stationery vs.
/// Felt" look, on top of the status kit (StatusBox, StatusUI) and the heat kit (HeatUI: canvases, paper buttons, fonts).
/// Positions are 1920 x 1080 reference pixels, x and y from the parent's top-left corner, y down.
/// </summary>
public static class ManagerUI
{
    public static readonly Vector4 CardRadii = HeatUI.CardRadii;
    public static readonly Vector4 SmallRadii = HeatUI.SmallRadii;

    /// <summary>The font for new texts in the current language (Coming Soon in English).</summary>
    public static TMP_FontAsset Font => L10n.ResolveFont(HeatUI.ResolveFont(null));

    /// <summary>The sticky-note colour of a personality.</summary>
    public static Color PersonalityColor(string personality)
    {
        switch (personality)
        {
            case ManagerSchema.Aggressive: return UIColors.StickyPink;
            case ManagerSchema.People: return UIColors.StickyGreen;
            case ManagerSchema.Numbers: return UIColors.StickyYellow;
            default: return UIColors.StickyBlue;
        }
    }

    public static TextMeshProUGUI Text(RectTransform parent, string name, string text, float size, Color color,
                                       float x, float y, float width, float height,
                                       TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft, bool bold = false)
    {
        return HeatUI.Text(parent, name, text, size, color, Font, x, y, width, height, alignment, bold);
    }

    /// <summary>A wrapped text block <paramref name="width"/> wide whose height follows its text (returns the height used).</summary>
    public static TextMeshProUGUI Block(RectTransform parent, string name, string text, float size, Color color, float x, float y, float width,
                                        out float height, bool bold = false, float lineHeight = 1.3f, float maxHeight = 2000f)
    {
        TextMeshProUGUI label = Text(parent, name, text, size, color, x, y, width, 10f, TextAlignmentOptions.TopLeft, bold);
        HeatUI.Wrapped(label, lineHeight);
        height = Mathf.Min(maxHeight, Mathf.Ceil(Measure(label, width)));
        label.rectTransform.sizeDelta = new Vector2(width, height);
        if (height >= maxHeight) label.overflowMode = TextOverflowModes.Ellipsis;
        return label;
    }

    /// <summary>The height a wrapped text needs at this width.</summary>
    public static float Measure(TMP_Text label, float width)
    {
        if (label == null || string.IsNullOrEmpty(label.text)) return 0f;
        Vector2 size = label.GetPreferredValues(label.text, width, 0f);
        return Mathf.Max(label.fontSize * 1.1f, size.y + 2f);
    }

    /// <summary>A round portrait (the photo by its portrait number, or initials) with an ink ring.</summary>
    public static RectTransform Portrait(RectTransform parent, string name, int portraitIndex, string personName, float size, float x, float y)
    {
        RectTransform root = StatusUI.Place(StatusUI.NewRect(name, parent), x, y, size, size);
        Vector4 round = Vector4.one * (size * 0.5f);
        StatusBox back = StatusUI.Box(root, "Back", UIColors.PaperShade, Color.clear, 0f, round);
        StatusUI.Stretch(back.rectTransform);
        Mask mask = back.gameObject.AddComponent<Mask>();
        mask.showMaskGraphic = true;

        Sprite photo = portraitIndex >= 0 ? PortraitLibrary.Load(portraitIndex) : null;
        if (photo != null)
        {
            Image image = StatusUI.NewRect("Photo", back.rectTransform).gameObject.AddComponent<Image>();
            image.sprite = photo;
            image.preserveAspect = true;
            image.raycastTarget = false;
            StatusUI.Stretch(image.rectTransform);
        }
        else
        {
            TextMeshProUGUI initials = StatusUI.Text(back.rectTransform, "Initials", Initials(personName), size * 0.36f, UIColors.InkSoft, Font, TextAlignmentOptions.Center, true);
            StatusUI.Stretch(initials.rectTransform);
        }
        StatusBox ring = StatusUI.Box(root, "Ring", Color.clear, UIColors.Ink, Mathf.Max(2f, size / 30f), round);
        StatusUI.Stretch(ring.rectTransform);
        return root;
    }

    static string Initials(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        string[] parts = name.Trim().Split(' ');
        string first = parts[0].Length > 0 ? parts[0].Substring(0, 1) : "";
        string last = parts.Length > 1 && parts[parts.Length - 1].Length > 0 ? parts[parts.Length - 1].Substring(0, 1) : "";
        return first + last;
    }

    /// <summary>
    /// A small sticky-note label turned a little (a personality tag, the "I'd do this" note), as wide as its text.
    /// <paramref name="x"/> is its left edge, or its right edge when <paramref name="alignRight"/>. Build it under an active
    /// parent: TMP measures nothing on inactive objects.
    /// </summary>
    public static RectTransform Sticky(RectTransform parent, string name, string text, Color color, float size, float x, float y, float degrees,
                                       out TextMeshProUGUI label, bool alignRight = false)
    {
        StatusBox note = StatusUI.Box(parent, name, color, UIColors.WithAlpha(UIColors.Ink, 0.35f), 1f, Vector4.one * 3f);
        note.SetShadow(new Color(0f, 0f, 0f, 0.22f), new Vector2(2f, 3f), 3f);
        RectTransform rect = note.rectTransform;
        label = StatusUI.Text(rect, "Label", text, size, UIColors.Ink, Font, TextAlignmentOptions.Center, true);
        float width = Mathf.Ceil(label.GetPreferredValues(text, 900f, 0f).x) + 26f;
        float height = Mathf.Ceil(size * 1.9f);
        StatusUI.PlaceRotated(rect, alignRight ? x - width : x, y, width, height, degrees);
        StatusUI.Stretch(label.rectTransform, 6f, 6f, 0f, 0f);
        return rect;
    }

    /// <summary>A paper sheet with an ink border and a soft shadow.</summary>
    public static StatusBox Sheet(RectTransform parent, string name, Color fill, float x, float y, float width, float height, float border = 3f)
    {
        StatusBox sheet = StatusUI.Box(parent, name, fill, UIColors.Ink, border, CardRadii);
        sheet.SetShadow(HeatUI.SheetShadow, new Vector2(0f, 14f), 26f);
        StatusUI.Place(sheet.rectTransform, x, y, width, height);
        return sheet;
    }

    /// <summary>A button made of a paper face over a hard shadow (MarkerButton feel) that sizes to the given rect.</summary>
    public static Button PaperButton(RectTransform parent, string name, string label, Color fill, float fontSize, float x, float y, float width, float height, out TextMeshProUGUI text)
    {
        return HeatUI.PaperButton(parent, name, label, fill, Font, fontSize, x, y, width, height, new Vector2(4f, 5f), out text);
    }

    /// <summary>A text button without a frame (underlined on hover by colour), e.g. "Decide later".</summary>
    public static Button TextButton(RectTransform parent, string name, string label, float size, float x, float y, float width, float height, out TextMeshProUGUI text)
    {
        RectTransform rect = StatusUI.Place(StatusUI.NewRect(name, parent), x, y, width, height);
        Image hit = rect.gameObject.AddComponent<Image>();
        hit.color = new Color(1f, 1f, 1f, 0f);
        text = StatusUI.Text(rect, "Label", label, size, UIColors.InkSoft, Font, TextAlignmentOptions.Center);
        StatusUI.Stretch(text.rectTransform);
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = hit;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = Color.white;
        colors.pressedColor = Color.white;
        button.colors = colors;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        return button;
    }

    /// <summary>Shrink a screen-sized root so a sheet of this size fits the canvas with a margin (small windows).</summary>
    public static float FitScale(RectTransform canvasRect, float width, float height, float margin)
    {
        if (canvasRect == null) return 1f;
        Vector2 size = canvasRect.rect.size;
        if (size.x <= 1f || size.y <= 1f) return 1f;
        return Mathf.Min(1f, (size.x - margin * 2f) / width, (size.y - margin * 2f) / height);
    }

    /// <summary>
    /// A key cap ("M", "Esc") as on the design canvas: paper, an ink border and a thicker bottom edge, 36 tall. <paramref name="x"/>
    /// is its left edge, or its right edge when <paramref name="alignRight"/>. Build it under an active parent (TMP measures).
    /// </summary>
    public static RectTransform Keycap(RectTransform parent, string label, float fontSize, float x, float y, bool alignRight = false)
    {
        StatusBox key = StatusUI.Box(parent, "Key " + label, UIColors.PaperHi, UIColors.Ink, 2f, SmallRadii);
        key.SetShadow(UIColors.Ink, new Vector2(0f, 3f));
        TextMeshProUGUI text = StatusUI.Text(key.rectTransform, "Label", label, fontSize, UIColors.Ink, Font, TextAlignmentOptions.Center, true);
        float width = Mathf.Max(36f, Mathf.Ceil(text.GetPreferredValues(label, 400f, 0f).x) + 18f);
        StatusUI.Place(key.rectTransform, alignRight ? x - width : x, y, width, 33f);
        StatusUI.Stretch(text.rectTransform);
        return key.rectTransform;
    }

    /// <summary>
    /// The manager's satisfaction (0-100) as the five mood bands of <see cref="ManagerText.SatisfactionBand"/> (fed up, unhappy,
    /// restless, fine, content) on a small track, with a paper pin at the value.
    /// </summary>
    public static RectTransform MoodMeter(RectTransform parent, float satisfaction, float x, float y, float width)
    {
        RectTransform root = StatusUI.Place(StatusUI.NewRect("MoodMeter", parent), x, y, width, 14f);
        StatusBox track = StatusUI.Box(root, "Track", UIColors.PaperHi, UIColors.Ink, 2f, Vector4.one * 5f);
        StatusUI.Stretch(track.rectTransform);
        float[] edges = { 0f, 20f, 35f, 50f, 70f, 100f };
        Color[] colors = { UIColors.Loss, UIColors.Loss, UIColors.Warn, UIColors.Money, UIColors.Money };
        float inner = width - 4f;
        for (int i = 0; i < colors.Length; i++)
        {
            Image band = StatusUI.Block(root, "Band" + i, UIColors.WithAlpha(colors[i], i == 0 || i == colors.Length - 1 ? 0.45f : 0.32f));
            StatusUI.Place(band.rectTransform, 2f + inner * edges[i] / 100f, 2f, inner * (edges[i + 1] - edges[i]) / 100f, 10f);
        }
        float at = 2f + inner * Mathf.Clamp(satisfaction, 0f, 100f) / 100f;
        StatusBox pin = StatusUI.Box(root, "Pin", UIColors.PaperHi, UIColors.Ink, 2f, Vector4.one * 3f);
        StatusUI.Place(pin.rectTransform, Mathf.Clamp(at - 7f, -2f, width - 12f), -5f, 14f, 24f);
        return root;
    }
}
