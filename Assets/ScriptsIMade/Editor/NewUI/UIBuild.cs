using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Editor helpers that turn the design canvas' CSS numbers into uGUI objects.
/// Coordinates are design pixels of the 1920x1080 artboard (1 px = 1 UI unit): left/top/width/height relative to
/// the parent's top-left corner, like CSS "position: absolute". The anchor keeps the element attached to a side of
/// its parent when the screen isn't 16:9 (the offset from that anchor stays as designed).
/// Rotations are CSS degrees (clockwise) around the element's centre.
/// Sprites come from the generated kit (Tools/UIKit/gen_kit.py -> Assets/UI).
/// </summary>
public static class UIBuild
{
    public enum Anchor { TopLeft, Top, TopRight, Left, Center, Right, BottomLeft, Bottom, BottomRight }

    public const string UIRoot = "Assets/UI/";
    static readonly int[] ShadowSigmas = { 2, 4, 6, 8, 10, 12, 16, 18, 24 };

    public static TMP_FontAsset Font => UIFonts.Main;

    // ------------------------------------------------------------------
    // Sprites

    /// <summary>Kit sprite by path under Assets/UI without extension, e.g. "Kit/card_fill".</summary>
    public static Sprite S(string rel)
    {
        var s = AssetDatabase.LoadAssetAtPath<Sprite>(UIRoot + rel + ".png");
        if (s == null)
            Debug.LogWarning("[UIBuild] Missing sprite " + rel + " (run Tools/UIKit/gen_kit.py, then Tools > Game > New UI > Reimport UI Kit Sprites)");
        return s;
    }

    /// <summary>Icon sprite. weight: null = 2.4 stroke, "b" = 3.0, "t" = 1.8, "fill" = filled silhouette.</summary>
    public static Sprite IconSprite(string name, string weight = null)
    {
        switch (weight)
        {
            case "b": return S("Icons/Bold/" + name + "_b");
            case "t": return S("Icons/Thin/" + name + "_t");
            case "fill": return S("Icons/Fill/" + name + "_fill");
            default: return S("Icons/" + name);
        }
    }

    // ------------------------------------------------------------------
    // RectTransforms

    public static Vector2 AnchorVec(Anchor a)
    {
        switch (a)
        {
            case Anchor.Top: return new Vector2(0.5f, 1f);
            case Anchor.TopRight: return new Vector2(1f, 1f);
            case Anchor.Left: return new Vector2(0f, 0.5f);
            case Anchor.Center: return new Vector2(0.5f, 0.5f);
            case Anchor.Right: return new Vector2(1f, 0.5f);
            case Anchor.BottomLeft: return new Vector2(0f, 0f);
            case Anchor.Bottom: return new Vector2(0.5f, 0f);
            case Anchor.BottomRight: return new Vector2(1f, 0f);
            default: return new Vector2(0f, 1f);
        }
    }

    public static RectTransform NewRect(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5; // UI
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    static Vector2 ParentSize(RectTransform rt)
    {
        var p = rt.parent as RectTransform;
        return p != null ? p.rect.size : new Vector2(1920f, 1080f);
    }

    /// <summary>CSS-style placement: left/top/width/height in the parent's top-left frame.</summary>
    public static void Place(RectTransform rt, float left, float top, float width, float height, Anchor anchor = Anchor.TopLeft)
    {
        Vector2 parentSize = ParentSize(rt);
        Vector2 a = AnchorVec(anchor);
        rt.anchorMin = rt.anchorMax = a;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(width, height);
        float cx = left + width * 0.5f;
        float cy = top + height * 0.5f;
        rt.anchoredPosition = new Vector2(cx - a.x * parentSize.x, -(cy - (1f - a.y) * parentSize.y));
    }

    public static RectTransform Node(Transform parent, string name, float left, float top, float width, float height,
        Anchor anchor = Anchor.TopLeft, float rotation = 0f)
    {
        var rt = NewRect(parent, name);
        Place(rt, left, top, width, height, anchor);
        if (rotation != 0f)
            rt.localEulerAngles = new Vector3(0f, 0f, -rotation);
        return rt;
    }

    /// <summary>Fills the parent, inset by left/top/right/bottom (negative = grow outside).</summary>
    public static RectTransform Stretch(Transform parent, string name, float left = 0f, float top = 0f, float right = 0f, float bottom = 0f)
    {
        var rt = NewRect(parent, name);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(-right, -top);
        return rt;
    }

    public static void IgnoreLayout(Component c)
    {
        var le = c.GetComponent<LayoutElement>();
        if (le == null) le = c.gameObject.AddComponent<LayoutElement>();
        le.ignoreLayout = true;
    }

    // ------------------------------------------------------------------
    // Images

    public static Image AddImage(GameObject go, Sprite sprite, Color color, bool raycast = false)
    {
        var img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = raycast;
        img.type = sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
        img.fillCenter = true;
        img.pixelsPerUnitMultiplier = 1f;
        if (sprite != null && sprite.border == Vector4.zero && sprite.texture != null && sprite.texture.wrapMode == TextureWrapMode.Repeat)
            img.type = Image.Type.Tiled;
        return img;
    }

    public static Image Box(Transform parent, string name, float left, float top, float width, float height,
        Sprite sprite, Color color, Anchor anchor = Anchor.TopLeft, float rotation = 0f)
    {
        var rt = Node(parent, name, left, top, width, height, anchor, rotation);
        return AddImage(rt.gameObject, sprite, color);
    }

    public static Image StretchBox(Transform parent, string name, Sprite sprite, Color color,
        float left = 0f, float top = 0f, float right = 0f, float bottom = 0f)
    {
        var rt = Stretch(parent, name, left, top, right, bottom);
        return AddImage(rt.gameObject, sprite, color);
    }

    /// <summary>A dashed border image (tiled so dashes keep their length).</summary>
    public static Image Dashed(Image img)
    {
        img.type = Image.Type.Tiled;
        return img;
    }

    static int PickSigma(float blur)
    {
        float want = blur * 0.5f;
        int best = ShadowSigmas[0];
        foreach (int s in ShadowSigmas)
            if (Mathf.Abs(s - want) < Mathf.Abs(best - want)) best = s;
        return best;
    }

    /// <summary>CSS box-shadow "dx dy blur rgba(0,0,0,alpha)" behind a box at left/top/width/height (same parent, created before
    /// the box). alpha is the CSS value; it is converted for linear-space blending (UIColors.SrgbAlpha).</summary>
    public static Image Shadow(Transform parent, string name, float left, float top, float width, float height,
        float dx, float dy, float blur, float alpha, Anchor anchor = Anchor.TopLeft, float rotation = 0f, Color? color = null)
    {
        int sigma = PickSigma(blur);
        float pad = 3f * sigma;
        var img = Box(parent, name, left - pad + dx, top - pad + dy, width + 2f * pad, height + 2f * pad,
            S("Shadows/shadow_s" + sigma), WithAlpha(color ?? Color.black, UIColors.SrgbAlpha(alpha)), anchor, rotation);
        img.type = Image.Type.Sliced;
        return img;
    }

    /// <summary>Soft shadow that follows its parent's size (for auto-sized boxes). Put it first among the children.</summary>
    public static Image StretchShadow(Transform parent, float dx, float dy, float blur, float alpha, Color? color = null, float spread = 0f)
    {
        int sigma = PickSigma(blur);
        float pad = 3f * sigma + spread;
        var img = StretchBox(parent, "Shadow", S("Shadows/shadow_s" + sigma), WithAlpha(color ?? Color.black, UIColors.SrgbAlpha(alpha)),
            -pad + dx, -pad + dy, -pad - dx, -pad - dy);
        img.type = Image.Type.Sliced;
        IgnoreLayout(img);
        return img;
    }

    public static Color WithAlpha(Color c, float a)
    {
        c.a = a;
        return c;
    }

    // ------------------------------------------------------------------
    // Text

    public static TextMeshProUGUI Text(Transform parent, string name, float left, float top, float width, float height,
        string text, float size, Color color, bool bold = false, TextAlignmentOptions align = TextAlignmentOptions.Left,
        float letterSpacingEm = 0f, float lineHeight = 0f, bool wrap = false, Anchor anchor = Anchor.TopLeft, float rotation = 0f)
    {
        if (!wrap)
        {
            // A line box shorter than the font's ascent + descent is fine in CSS (glyphs overflow it) but TMP clips
            // or ellipsises it, so grow the box around the same centre
            float content = size * UIFonts.ContentHeight(Font);
            if (height < content)
            {
                top -= (content - height) * 0.5f;
                height = content;
            }
        }
        var rt = Node(parent, name, left, top, width, height, anchor, rotation);
        return SetupText(rt.gameObject, text, size, color, bold, align, letterSpacingEm, lineHeight, wrap);
    }

    public static TextMeshProUGUI SetupText(GameObject go, string text, float size, Color color, bool bold = false,
        TextAlignmentOptions align = TextAlignmentOptions.Left, float letterSpacingEm = 0f, float lineHeight = 0f, bool wrap = false)
    {
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.font = Font;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        tmp.alignment = align;
        tmp.characterSpacing = letterSpacingEm * 100f;
        tmp.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.raycastTarget = false;
        tmp.richText = true;
        tmp.margin = Vector4.zero;
        if (lineHeight > 0f)
        {
            // CSS line-height: the line pitch is lineHeight x size (TMP line spacing), and layout groups size the
            // text with CssLineHeight (each line lineHeight x size, glyphs centred in it)
            tmp.lineSpacing = (lineHeight - UIFonts.NaturalLineHeight(Font)) * 100f;
            var css = go.AddComponent<CssLineHeight>();
            css.lineHeight = lineHeight;
        }
        tmp.text = text;
        return tmp;
    }

    /// <summary>Width the text needs on one line (for sizing buttons and chips at build time).</summary>
    public static float TextWidth(string text, float size, bool bold = false, float letterSpacingEm = 0f)
    {
        var go = new GameObject("measure", typeof(RectTransform));
        try
        {
            var tmp = SetupText(go, text, size, Color.black, bold, TextAlignmentOptions.Left, letterSpacingEm);
            return tmp.GetPreferredValues(text, 10000f, 1000f).x;
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    // ------------------------------------------------------------------
    // Icons

    public static Image Icon(Transform parent, string name, float left, float top, float size, string icon, Color color,
        string weight = null, Anchor anchor = Anchor.TopLeft, float rotation = 0f)
    {
        var img = Box(parent, name, left, top, size, size, IconSprite(icon, weight), color, anchor, rotation);
        img.type = Image.Type.Simple;
        img.preserveAspect = true;
        return img;
    }

    /// <summary>Icon whose size is driven by a layout group.</summary>
    public static Image LayoutIcon(Transform parent, string name, float size, string icon, Color color, string weight = null)
    {
        var rt = NewRect(parent, name);
        rt.sizeDelta = new Vector2(size, size);
        var img = AddImage(rt.gameObject, IconSprite(icon, weight), color);
        img.type = Image.Type.Simple;
        img.preserveAspect = true;
        var le = rt.gameObject.AddComponent<LayoutElement>();
        le.minWidth = le.preferredWidth = size;
        le.minHeight = le.preferredHeight = size;
        return img;
    }

    // ------------------------------------------------------------------
    // Layout

    public static HorizontalLayoutGroup HLayout(GameObject go, float spacing, RectOffset padding = null,
        TextAnchor align = TextAnchor.MiddleLeft, bool controlWidth = true, bool controlHeight = true, bool expandWidth = false, bool expandHeight = false)
    {
        var h = go.AddComponent<HorizontalLayoutGroup>();
        h.spacing = spacing;
        h.padding = padding ?? new RectOffset();
        h.childAlignment = align;
        h.childControlWidth = controlWidth;
        h.childControlHeight = controlHeight;
        h.childForceExpandWidth = expandWidth;
        h.childForceExpandHeight = expandHeight;
        h.childScaleWidth = false;
        h.childScaleHeight = false;
        return h;
    }

    public static VerticalLayoutGroup VLayout(GameObject go, float spacing, RectOffset padding = null,
        TextAnchor align = TextAnchor.UpperLeft, bool controlWidth = true, bool controlHeight = true, bool expandWidth = true, bool expandHeight = false)
    {
        var v = go.AddComponent<VerticalLayoutGroup>();
        v.spacing = spacing;
        v.padding = padding ?? new RectOffset();
        v.childAlignment = align;
        v.childControlWidth = controlWidth;
        v.childControlHeight = controlHeight;
        v.childForceExpandWidth = expandWidth;
        v.childForceExpandHeight = expandHeight;
        return v;
    }

    public static LayoutElement Layout(Component c, float minW = -1f, float minH = -1f, float prefW = -1f, float prefH = -1f, float flexW = -1f, float flexH = -1f)
    {
        var le = c.GetComponent<LayoutElement>();
        if (le == null) le = c.gameObject.AddComponent<LayoutElement>();
        le.minWidth = minW;
        le.minHeight = minH;
        le.preferredWidth = prefW;
        le.preferredHeight = prefH;
        le.flexibleWidth = flexW;
        le.flexibleHeight = flexH;
        return le;
    }

    public static ContentSizeFitter Fit(GameObject go, bool width, bool height)
    {
        var f = go.AddComponent<ContentSizeFitter>();
        f.horizontalFit = width ? ContentSizeFitter.FitMode.PreferredSize : ContentSizeFitter.FitMode.Unconstrained;
        f.verticalFit = height ? ContentSizeFitter.FitMode.PreferredSize : ContentSizeFitter.FitMode.Unconstrained;
        return f;
    }

    public static RectOffset Pad(int left, int right, int top, int bottom) => new RectOffset(left, right, top, bottom);

    /// <summary>Text child that a layout group sizes (preferred width/height from the text).</summary>
    public static TextMeshProUGUI LayoutText(Transform parent, string name, string text, float size, Color color, bool bold = false,
        TextAlignmentOptions align = TextAlignmentOptions.Left, float letterSpacingEm = 0f, float lineHeight = 0f, bool wrap = false)
    {
        var rt = NewRect(parent, name);
        return SetupText(rt.gameObject, text, size, color, bold, align, letterSpacingEm, lineHeight, wrap);
    }

    /// <summary>
    /// Binds a fixed label to a String Table entry (Assets/Localization/Source/&lt;table&gt;.csv): an L10nText that writes
    /// the entry in the current language when the label is enabled and on every language change, in that language's
    /// font. The prefab keeps the English it was built with. Only for texts no view writes at runtime (those use L10n.Get
    /// in the view): L10nText would put its entry back over them on a language change.
    /// </summary>
    public static T Localize<T>(T text, string table, string key) where T : TMP_Text
    {
        if (text == null) return null;
        var localized = text.GetComponent<L10nText>();
        if (localized == null) localized = text.gameObject.AddComponent<L10nText>();
        localized.table = table;
        localized.key = key;
        return text;
    }

    // ------------------------------------------------------------------
    // Composite pieces used on many screens

    /// <summary>
    /// A shape = fill image + border ring on top (+ optional dashed ring, inactive), as children of a plain rect.
    /// family: "panel", "card", "small", "tiny", "tab", "r10"... (kit names). Returns the root; fill and ring are
    /// children named "Fill" and "Border" so content added later draws above them.
    /// </summary>
    public static RectTransform Shape(Transform parent, string name, float left, float top, float width, float height,
        string family, Color fill, Color border, int borderWidth = 3, Anchor anchor = Anchor.TopLeft, float rotation = 0f, bool dashed = false)
    {
        var rt = Node(parent, name, left, top, width, height, anchor, rotation);
        ShapeLayers(rt, family, fill, border, borderWidth, dashed);
        return rt;
    }

    public static void ShapeLayers(RectTransform rt, string family, Color fill, Color border, int borderWidth = 3, bool dashed = false)
    {
        var f = StretchBox(rt, "Fill", S("Kit/" + family + "_fill"), fill);
        IgnoreLayout(f);
        if (borderWidth > 0)
        {
            string ring = dashed ? "_dash" + borderWidth : "_line" + borderWidth;
            var b = StretchBox(rt, "Border", S("Kit/" + family + ring), border);
            if (dashed) Dashed(b);
            IgnoreLayout(b);
        }
    }

    // ------------------------------------------------------------------
    // Controls shared by the screen builders

    public static readonly Color Clear = new Color(1f, 1f, 1f, 0f);

    /// <summary>Keycap hint: paper-hi, 2 px ink border with a 5 px bottom edge.</summary>
    public static RectTransform Keycap(Transform parent, string name, float left, float top, string key, float size, float height,
        Anchor anchor = Anchor.TopLeft)
    {
        float w = Mathf.Max(height, 9f * 2f + TextWidth(key, size, true));
        var cap = Node(parent, name, left, top, w, height, anchor);
        StretchBox(cap, "Fill", S("Kit/small_fill"), UIColors.PaperHi);
        StretchBox(cap, "Border", S("Kit/small_keycap"), UIColors.Ink);
        Text(cap, "Key", 0f, 0f, w, height - 3f, key, size, UIColors.Ink, bold: true, align: TextAlignmentOptions.Center);
        return cap;
    }

    /// <summary>
    /// Marker button: transparent hit area + Button; a hard ink shadow at (sx, sy) behind a Face (fill, border,
    /// dashed border for the disabled look, content). Returns the root; face and content via out.
    /// </summary>
    public static RectTransform MarkerButtonNode(Transform parent, string name, float left, float top, float width, float height, Anchor anchor,
        Color fill, Vector2 shadowOffset, out RectTransform face, out RectTransform content)
    {
        var btn = Node(parent, name, left, top, width, height, anchor);
        var hit = AddImage(btn.gameObject, null, Clear, true);
        var button = btn.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = hit;
        StretchBox(btn, "Shadow", S("Kit/card_fill"), UIColors.Ink, shadowOffset.x, shadowOffset.y, -shadowOffset.x, -shadowOffset.y);
        face = Stretch(btn, "Face");
        StretchBox(face, "Fill", S("Kit/card_fill"), fill);
        StretchBox(face, "Border", S("Kit/card_line3"), UIColors.Ink);
        var dashed = Dashed(StretchBox(face, "DashedBorder", S("Kit/card_dash3"), UIColors.Pencil));
        dashed.gameObject.SetActive(false);
        content = Stretch(face, "Content");
        return btn;
    }

    public static MarkerButton SetupMarker(RectTransform btn, RectTransform face, Graphic[] content)
    {
        var marker = btn.gameObject.AddComponent<MarkerButton>();
        marker.face = face;
        marker.shadow = btn.Find("Shadow").gameObject;
        marker.fill = face.Find("Fill").GetComponent<Image>();
        marker.solidBorder = face.Find("Border").gameObject;
        marker.dashedBorder = face.Find("DashedBorder").gameObject;
        marker.content = content;
        marker.disabledFillColor = UIColors.PaperShade;
        marker.disabledContentColor = UIColors.Pencil;
        return marker;
    }

    /// <summary>
    /// ICON-ONLY button (close, pause…): a 60–64 px blob, paper-hi with a 3 px ink border and a hard 3,4 ink shadow,
    /// the icon in the middle; MarkerButton gives it the lift/press feel.
    /// </summary>
    public static Button IconButton(Transform parent, string name, float left, float top, int size, string icon,
        Anchor anchor = Anchor.TopLeft, float iconSize = 28f, string weight = null)
    {
        var btn = Node(parent, name, left, top, size, size, anchor);
        var hit = AddImage(btn.gameObject, null, Clear, true);
        var button = btn.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = hit;
        var shadow = Box(btn, "Shadow", 3f, 4f, size, size, S("Kit/Blobs/blob" + size + "_fill"), UIColors.Ink);
        var face = Stretch(btn, "Face");
        StretchBox(face, "Fill", S("Kit/Blobs/blob" + size + "_fill"), UIColors.PaperHi);
        StretchBox(face, "Border", S("Kit/Blobs/blob" + size + "_line3"), UIColors.Ink);
        float inset = (size - iconSize) * 0.5f;
        Icon(face, "Icon", inset, inset, iconSize, icon, UIColors.Ink, weight);
        var marker = btn.gameObject.AddComponent<MarkerButton>();
        marker.face = face;
        marker.shadow = shadow.gameObject;
        return button;
    }

    public static void SetAllRaycast(GameObject root, bool value)
    {
        foreach (var g in root.GetComponentsInChildren<Graphic>(true))
            g.raycastTarget = value;
    }
}
