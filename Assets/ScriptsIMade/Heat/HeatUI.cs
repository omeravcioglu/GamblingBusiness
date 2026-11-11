using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Building blocks for the heat screens built at runtime (the heat chip and the "Win it back?" prompt) in the
/// "Stationery vs. Felt" look: the status kit's rounded boxes (StatusBox, StatusUI) and the UI kit (UIColors,
/// MarkerButton). Only used when a view has no designed prefab wired in.
/// Positions are 1920 x 1080 reference pixels, x and y from the parent's top-left corner, y down (like the design's CSS).
/// </summary>
public static class HeatUI
{
    /// <summary>Name of the game font asset (Assets/UI/Fonts/ComingSoon SDF.asset).</summary>
    public const string FontAssetName = "ComingSoon SDF";
#if UNITY_EDITOR
    const string FontAssetPath = "Assets/UI/Fonts/ComingSoon SDF.asset";
#endif

    /// <summary>Hand-drawn corners (top-left, top-right, bottom-right, bottom-left) for chips, buttons and cards.</summary>
    public static readonly Vector4 SmallRadii = new Vector4(10f, 13f, 9f, 14f);
    public static readonly Vector4 ButtonRadii = new Vector4(17f, 16f, 17f, 18f);
    public static readonly Vector4 CardRadii = new Vector4(22f, 26f, 21f, 27f);
    public static readonly Color SheetShadow = new Color(0f, 0f, 0f, 0.45f);

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

    /// <summary>
    /// An overlay canvas like the game's (1920 x 1080, match 0.5) as a child of <paramref name="owner"/> (at the scene
    /// root when the owner sits inside another canvas). <paramref name="takesClicks"/> adds a GraphicRaycaster.
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
        RectTransform rect = StatusUI.NewRect(name, parent);
        rect.gameObject.AddComponent<CanvasRenderer>();
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = takesClicks;
        return image;
    }

    /// <summary>A text placed at (x, y) from the parent's top-left, <paramref name="width"/> x <paramref name="height"/>.</summary>
    public static TextMeshProUGUI Text(RectTransform parent, string name, string text, float size, Color color, TMP_FontAsset font,
                                       float x, float y, float width, float height,
                                       TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft, bool bold = false)
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

    /// <summary>Place a centre-pivot rect so its centre is at (x, y) from the parent's top-left.</summary>
    public static void PlaceCenter(RectTransform rect, float x, float y)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(x, -y);
    }

    /// <summary>"#RRGGBB" for rich text colour tags.</summary>
    public static string Hex(Color color)
    {
        return "#" + ColorUtility.ToHtmlStringRGB(color);
    }

    // ------------------------------------------------------------------
    // Pieces
    // ------------------------------------------------------------------

    /// <summary>
    /// The design's marker-pen button: a face (fill, 3 px ink border, hand-drawn corners) over a hard ink shadow and a
    /// bold label. MarkerButton lifts the face on hover and presses it into the shadow; SetButtonEnabled fades it.
    /// Build it under an inactive parent so MarkerButton starts with its face in place.
    /// </summary>
    public static Button PaperButton(RectTransform parent, string name, string label, Color fill, TMP_FontAsset font, float fontSize,
                                     float x, float y, float width, float height, Vector2 shadowOffset, out TextMeshProUGUI text)
    {
        RectTransform rect = StatusUI.Place(StatusUI.NewRect(name, parent), x, y, width, height);

        StatusBox shadow = StatusUI.Box(rect, "Shadow", UIColors.Ink, Color.clear, 0f, ButtonRadii);
        StatusUI.Stretch(shadow.rectTransform);
        shadow.rectTransform.anchoredPosition = new Vector2(shadowOffset.x, -shadowOffset.y);

        StatusBox face = StatusUI.Box(rect, "Face", fill, UIColors.Ink, 3f, ButtonRadii);
        StatusUI.Stretch(face.rectTransform);
        face.raycastTarget = true;   // takes the button's clicks

        text = StatusUI.Text(face.rectTransform, "Label", label, fontSize, UIColors.Ink, font, TextAlignmentOptions.Center, true);
        StatusUI.Stretch(text.rectTransform, 16f, 16f, 0f, 0f);
        Shrink(text, Mathf.Round(fontSize * 0.7f));

        Button button = rect.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;   // MarkerButton draws the states
        button.targetGraphic = face;
        button.navigation = new Navigation { mode = Navigation.Mode.None };

        MarkerButton marker = rect.gameObject.AddComponent<MarkerButton>();
        marker.face = face.rectTransform;
        marker.shadow = shadow.gameObject;
        marker.pressOffset = new Vector2(shadowOffset.x, -shadowOffset.y);   // pressed right into the shadow
        marker.content = new Graphic[] { text };
        rect.gameObject.AddComponent<CanvasGroup>();
        return button;
    }

    /// <summary>Enable or disable a button; a disabled paper button fades (MarkerButton also hides its shadow).</summary>
    public static void SetButtonEnabled(Button button, bool on)
    {
        if (button == null) return;
        button.interactable = on;
        CanvasGroup group = button.GetComponent<CanvasGroup>();
        if (group != null) group.alpha = on ? 1f : 0.6f;
    }

    /// <summary>
    /// The design's gold coin: a face with a light-to-gold shine, a dotted and a solid ring and a big letter, over a darker
    /// edge that gives it thickness. <paramref name="size"/> = face diameter; the coin's pivot is its centre, anchored to
    /// the parent's top-left (place it with PlaceCenter).
    /// </summary>
    public static RectTransform Coin(RectTransform parent, string name, float size, TMP_FontAsset font, out TextMeshProUGUI letter)
    {
        float s = size / 220f;
        float edge = 12f * s;
        RectTransform coin = StatusUI.NewRect(name, parent);
        coin.anchorMin = coin.anchorMax = new Vector2(0f, 1f);
        coin.pivot = new Vector2(0.5f, 0.5f);
        coin.sizeDelta = new Vector2(size, size + edge);

        Vector4 round = Vector4.one * (size * 0.5f);
        StatusBox rim = StatusUI.Box(coin, "Edge", UIColors.GoldDark, UIColors.Ink, 4f, round);
        StatusUI.Place(rim.rectTransform, 0f, edge, size, size);

        StatusBox face = StatusUI.Box(coin, "Face", UIColors.Gold, UIColors.Ink, 4f, round);
        face.SetGradient(UIColors.Hex(0xF7DC87), UIColors.Gold);
        StatusUI.Place(face.rectTransform, 0f, 0f, size, size);

        float dotsInset = 12f * s;
        float dotWidth = 3f * Mathf.Max(0.6f, s);
        StatusBox dots = StatusUI.Box(face.rectTransform, "Dots", Color.clear, UIColors.WithAlpha(UIColors.GoldDark, 0.8f), dotWidth,
                                      Vector4.one * (size * 0.5f - dotsInset));
        dots.SetDash(true, dotWidth, 5f * Mathf.Max(0.6f, s));
        StatusUI.Stretch(dots.rectTransform, dotsInset, dotsInset, dotsInset, dotsInset);

        float ringInset = 26f * s;
        StatusBox ring = StatusUI.Box(face.rectTransform, "Ring", Color.clear, UIColors.GoldDark, 2f, Vector4.one * (size * 0.5f - ringInset));
        StatusUI.Stretch(ring.rectTransform, ringInset, ringInset, ringInset, ringInset);

        letter = StatusUI.Text(face.rectTransform, "Letter", "H", 110f * s, UIColors.GoldDark, font, TextAlignmentOptions.Center, true);
        StatusUI.Stretch(letter.rectTransform);
        return coin;
    }

    /// <summary>A small "Game paused" chip at the top centre of the screen (like the other paused pop-ups), in the current language.</summary>
    public static void PausedChip(RectTransform parent, TMP_FontAsset font)
    {
        StatusBox chip = StatusUI.Box(parent, "Paused", UIColors.PaperHi, UIColors.Ink, 2f, SmallRadii);
        chip.SetShadow(UIColors.Ink, new Vector2(3f, 4f));
        RectTransform rect = chip.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(224f, 48f);
        rect.anchoredPosition = new Vector2(0f, -28f);
        TextMeshProUGUI text = StatusUI.Text(rect, "Text", "", 22f, UIColors.Ink, font, TextAlignmentOptions.Center);
        StatusUI.Stretch(text.rectTransform);
        L10nText.Attach(text, HeatMeter.Table, "heat.ui.paused");   // "Game paused"
    }

    /// <summary>Masking tape across the top edge of a card, turned <paramref name="degrees"/> (positive = counter-clockwise).</summary>
    public static StatusBox Tape(RectTransform parent, float width, float degrees)
    {
        StatusBox tape = StatusUI.Box(parent, "Tape", UIColors.Tape, Color.clear, 0f, Vector4.one * 2f);
        tape.SetShadow(new Color(0f, 0f, 0f, 0.18f), new Vector2(0f, 2f), 4f);
        RectTransform rect = tape.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(width, 36f);
        rect.anchoredPosition = new Vector2(0f, -2f);
        rect.localEulerAngles = new Vector3(0f, 0f, degrees);
        return tape;
    }
}
