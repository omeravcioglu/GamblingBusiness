using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Building blocks for the runtime-made build mode screens, in the look of the design (1920 x 1080 reference):
/// paper boxes with ink borders and hard shadows, key caps, text and icons. Only used when no designed prefab is wired in.
/// </summary>
public static class BlueprintUI
{
    // Hand-drawn corner radii of the design: small controls, large buttons/cards, big cards
    public static readonly Vector4 RadiusSmall = new Vector4(10f, 14f, 9f, 15f);
    public static readonly Vector4 RadiusLarge = new Vector4(14f, 20f, 12f, 22f);
    public static readonly Vector4 RadiusCard = new Vector4(18f, 26f, 20f, 30f);

    public static readonly Color InkShadow = new Color32(29, 36, 51, 230);       // rgba(29,36,51,0.9)
    public static readonly Color FeltShadow = UIColors.Hex(0x0B2A1B);
    public static readonly Color SoftShadow = new Color(0f, 0f, 0f, 0.3f);
    public static readonly Color GhostGood = UIColors.Hex(0x7ED68E);
    public static readonly Color GhostBad = UIColors.Hex(0xFF7A6B);
    public static readonly Color PinRed = UIColors.Hex(0xD8413A);
    public static readonly Color PinLight = UIColors.Hex(0xF08A84);
    public static readonly Color InkWash = UIColors.WithAlpha(UIColors.BlueprintInk, 0.06f);

    public static Vector4 Round(float r)
    {
        return new Vector4(r, r, r, r);
    }

    // ------------------------------------------------------------------
    // Rects
    // ------------------------------------------------------------------

    public static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        if (parent != null) go.layer = parent.gameObject.layer;
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        return rect;
    }

    public static RectTransform Stretch(RectTransform rect, float left = 0f, float top = 0f, float right = 0f, float bottom = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
        return rect;
    }

    /// <summary>Anchored to the parent's top-left; x/y measure from there with y pointing down.</summary>
    public static RectTransform TopLeft(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
        return rect;
    }

    public static RectTransform TopRight(RectTransform rect, float right, float top, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-right, -top);
        rect.sizeDelta = new Vector2(width, height);
        return rect;
    }

    public static RectTransform BottomRight(RectTransform rect, float right, float bottom, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(1f, 0f);
        rect.anchoredPosition = new Vector2(-right, bottom);
        rect.sizeDelta = new Vector2(width, height);
        return rect;
    }

    public static RectTransform Centered(RectTransform rect, float width, float height, float x = 0f, float y = 0f)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(width, height);
        return rect;
    }

    // Change-only setters for per-frame layout: writing an unchanged value still dirties the transform and the canvas

    public static void SetAnchoredPosition(RectTransform rect, Vector2 value)
    {
        if (rect != null && !Near(rect.anchoredPosition, value)) rect.anchoredPosition = value;
    }

    public static void SetLocalScale(Transform transform, Vector3 value)
    {
        if (transform == null) return;
        Vector3 current = transform.localScale;
        if (Mathf.Abs(current.x - value.x) > 0.0001f || Mathf.Abs(current.y - value.y) > 0.0001f || Mathf.Abs(current.z - value.z) > 0.0001f)
            transform.localScale = value;
    }

    static bool Near(Vector2 a, Vector2 b)
    {
        return Mathf.Abs(a.x - b.x) <= 0.001f && Mathf.Abs(a.y - b.y) <= 0.001f;
    }

    // ------------------------------------------------------------------
    // Graphics
    // ------------------------------------------------------------------

    public static BlueprintBox Box(Transform parent, string name, Color fill, Color border, float borderWidth, Vector4 radii,
                                   bool raycast = false)
    {
        RectTransform rect = NewRect(name, parent);
        return AddBox(rect, fill, border, borderWidth, radii, raycast);
    }

    public static BlueprintBox AddBox(RectTransform rect, Color fill, Color border, float borderWidth, Vector4 radii, bool raycast = false)
    {
        BlueprintBox box = rect.gameObject.AddComponent<BlueprintBox>();
        box.SetStyle(fill, border, borderWidth);
        box.Radii = radii;
        box.raycastTarget = raycast;
        return box;
    }

    public static TextMeshProUGUI Text(Transform parent, string name, string text, float size, Color color, TMP_FontAsset font,
                                       TextAlignmentOptions alignment = TextAlignmentOptions.Left, bool bold = false)
    {
        RectTransform rect = NewRect(name, parent);
        TextMeshProUGUI label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) label.font = font;
        label.fontSize = size;   // an explicit size also stops TMP from applying its defaults on Awake
        label.text = text;
        label.color = color;
        label.alignment = alignment;
        label.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Overflow;
        label.richText = true;
        label.raycastTarget = false;
        return label;
    }

    /// <summary>Allow wrapping onto more lines.</summary>
    public static TextMeshProUGUI Wrap(TextMeshProUGUI text)
    {
        text.textWrappingMode = TextWrappingModes.Normal;
        return text;
    }

    /// <summary>Shrink long text (down to <paramref name="min"/>) before it spills out.</summary>
    public static TextMeshProUGUI AutoSize(TextMeshProUGUI text, float min)
    {
        text.enableAutoSizing = true;
        text.fontSizeMax = text.fontSize;
        text.fontSizeMin = Mathf.Min(min, text.fontSize);
        text.overflowMode = TextOverflowModes.Ellipsis;
        return text;
    }

    /// <summary>
    /// Design labels in spaced capitals: 0.08 em letter spacing. The capitals are written in the text itself (capitals in
    /// the String Table, or L10n.ToUpper for names): TMP's UpperCase style can't turn Turkish i into İ.
    /// </summary>
    public static TextMeshProUGUI Caps(TextMeshProUGUI text, float letterSpacingEm = 0.08f)
    {
        text.characterSpacing = letterSpacingEm * 100f;
        return text;
    }

    public static BlueprintIcon Icon(Transform parent, BlueprintIconKind kind, float size, Color color, float stroke = 2.4f)
    {
        RectTransform rect = NewRect("Icon", parent);
        rect.sizeDelta = new Vector2(size, size);
        BlueprintIcon icon = rect.gameObject.AddComponent<BlueprintIcon>();
        icon.Kind = kind;
        icon.StrokeWidth = stroke;
        icon.color = color;
        icon.raycastTarget = false;
        Layout(icon, size, size);
        return icon;
    }

    // ------------------------------------------------------------------
    // Controls
    // ------------------------------------------------------------------

    /// <summary>Make the box a button (the box takes the clicks and the tint).</summary>
    public static Button MakeButton(BlueprintBox box)
    {
        box.raycastTarget = true;
        Button button = box.gameObject.AddComponent<Button>();
        button.targetGraphic = box;
        button.colors = Tint();
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        return button;
    }

    /// <summary>A paper button with an optional icon and a label, sized to its content (for layout groups).</summary>
    public static Button PaperButton(Transform parent, string name, string label, BlueprintIconKind icon, TMP_FontAsset font,
                                     float height, float fontSize, Color fill, Color textColor, bool bold, Vector4 radii,
                                     float borderWidth, Color shadow, Vector2 shadowOffset, float padding, out TextMeshProUGUI text,
                                     float iconSize = 28f)
    {
        BlueprintBox box = Box(parent, name, fill, UIColors.Ink, borderWidth, radii, true);
        if (shadow.a > 0f) box.SetShadow(shadow, shadowOffset);
        Button button = MakeButton(box);
        HRow(box, 10f, new RectOffset((int)padding, (int)padding, 0, 0), TextAnchor.MiddleCenter);
        if (icon != BlueprintIconKind.None) Icon(box.transform, icon, iconSize, textColor);
        text = Text(box.transform, "Label", label, fontSize, textColor, font, TextAlignmentOptions.Center, bold);
        Layout(box, -1f, height, -1f, -1f, -1f, height);
        return button;
    }

    /// <summary>A key cap ("Q", "Esc") like the design: paper, ink border, thick bottom edge.</summary>
    public static RectTransform Keycap(Transform parent, string label, TMP_FontAsset font, float fontSize = 19f)
    {
        BlueprintBox box = Box(parent, "Key " + label, UIColors.PaperHi, UIColors.Ink, 2f, RadiusSmall);
        box.SetShadow(UIColors.Ink, new Vector2(0f, 3f));
        HRow(box, 0f, new RectOffset(9, 9, 0, 3), TextAnchor.MiddleCenter);
        Text(box.transform, "Label", label, fontSize, UIColors.Ink, font, TextAlignmentOptions.Center, true);
        Layout(box, -1f, 36f, -1f, -1f, 36f, 36f);
        return box.rectTransform;
    }

    public static ColorBlock Tint()
    {
        ColorBlock colors = ColorBlock.defaultColorBlock;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(0.93f, 0.93f, 0.93f, 1f);
        colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
        colors.selectedColor = Color.white;
        colors.disabledColor = Color.white;   // disabled buttons are dimmed as a whole (SetInteractable)
        colors.fadeDuration = 0.06f;
        return colors;
    }

    /// <summary>Enable/disable a button and dim everything on it while disabled.</summary>
    public static void SetInteractable(Selectable control, bool on)
    {
        if (control == null) return;
        if (control.interactable != on) control.interactable = on;
        CanvasGroup group = control.GetComponent<CanvasGroup>();
        if (group == null)
        {
            if (on) return;
            group = control.gameObject.AddComponent<CanvasGroup>();
        }
        group.alpha = on ? 1f : 0.5f;
    }

    // ------------------------------------------------------------------
    // Layout
    // ------------------------------------------------------------------

    public static HorizontalLayoutGroup HRow(Component target, float spacing, RectOffset padding, TextAnchor alignment = TextAnchor.MiddleLeft)
    {
        var group = target.gameObject.AddComponent<HorizontalLayoutGroup>();
        group.spacing = spacing;
        group.padding = padding;
        group.childAlignment = alignment;
        group.childControlWidth = true;
        group.childControlHeight = true;
        group.childForceExpandWidth = false;
        group.childForceExpandHeight = false;
        return group;
    }

    public static VerticalLayoutGroup VColumn(Component target, float spacing, RectOffset padding, TextAnchor alignment = TextAnchor.UpperLeft)
    {
        var group = target.gameObject.AddComponent<VerticalLayoutGroup>();
        group.spacing = spacing;
        group.padding = padding;
        group.childAlignment = alignment;
        group.childControlWidth = true;
        group.childControlHeight = true;
        group.childForceExpandWidth = true;
        group.childForceExpandHeight = false;
        return group;
    }

    /// <summary>Set layout sizes (-1 = not set).</summary>
    public static LayoutElement Layout(Component target, float prefWidth = -1f, float prefHeight = -1f, float flexWidth = -1f,
                                       float flexHeight = -1f, float minWidth = -1f, float minHeight = -1f)
    {
        LayoutElement element = target.GetComponent<LayoutElement>();
        if (element == null) element = target.gameObject.AddComponent<LayoutElement>();
        element.preferredWidth = prefWidth;
        element.preferredHeight = prefHeight;
        element.flexibleWidth = flexWidth;
        element.flexibleHeight = flexHeight;
        element.minWidth = minWidth;
        element.minHeight = minHeight;
        return element;
    }

    public static ContentSizeFitter Fit(Component target, bool horizontal, bool vertical)
    {
        ContentSizeFitter fitter = target.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = horizontal ? ContentSizeFitter.FitMode.PreferredSize : ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = vertical ? ContentSizeFitter.FitMode.PreferredSize : ContentSizeFitter.FitMode.Unconstrained;
        return fitter;
    }

    /// <summary>A flexible gap in a layout group.</summary>
    public static RectTransform Spacer(Transform parent, float flexWidth = 0f, float flexHeight = 0f)
    {
        RectTransform rect = NewRect("Spacer", parent);
        Layout(rect, 0f, 0f, flexWidth, flexHeight, 0f, 0f);
        return rect;
    }

    // ------------------------------------------------------------------
    // Canvas
    // ------------------------------------------------------------------

    /// <summary>A Screen Space Overlay canvas scaled like the game's (1920 x 1080, match 0.5).</summary>
    public static Canvas CreateOverlayCanvas(string name, int sortingOrder)
    {
        var go = new GameObject(name, typeof(RectTransform));
        int uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer >= 0) go.layer = uiLayer;
        Canvas canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        canvas.pixelPerfect = false;
        canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
        CanvasScaler scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        go.AddComponent<GraphicRaycaster>();
        EnsureEventSystem();
        return canvas;
    }

    public static void EnsureEventSystem()
    {
        if (EventSystem.current != null || Object.FindAnyObjectByType<EventSystem>() != null) return;
        new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        Debug.Log("[BlueprintUI] No EventSystem in the scene; created one for build mode.");
    }
}
