using System;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Look and building blocks of the City screens (UI Toolkit, styled in code so the compiler checks it):
/// the design's "Stationery" style - paper, ink outlines, hard offset shadows, yellow highlighter.
/// </summary>
public static class CityUi
{
    // ---- Colours (design tokens) --------------------------------------------------------------------------------

    public static readonly Color Ink = Hex("1D2433");
    public static readonly Color InkSoft = Hex("474F60");
    public static readonly Color Muted = Hex("676B74");
    public static readonly Color Paper = Hex("F7F2E6");
    public static readonly Color Card = Hex("FFFDF6");
    public static readonly Color MapPaper = Hex("EFE6CF");
    public static readonly Color Rule = Hex("C6D6E6");
    public static readonly Color Highlight = Hex("FFE14D");
    public static readonly Color Red = Hex("C0352B");
    public static readonly Color Green = Hex("1E7B45");
    public static readonly Color Gold = Hex("E2B13C");
    public static readonly Color GoldDark = Hex("9C7418");
    public static readonly Color Blue = Hex("2B5BA8");
    public static readonly Color PastelBlue = Hex("BFE3F5");
    public static readonly Color PastelYellow = Hex("FFE680");
    public static readonly Color PastelGreen = Hex("CDEBA6");
    public static readonly Color PastelPink = Hex("FFC7D1");
    public static readonly Color PastelRed = Hex("F6D8D3");
    public static readonly Color Felt = Hex("1D5A3E");
    public static readonly Color FeltLight = Hex("2A7652");
    public static readonly Color FeltDark = Hex("123D29");
    public static readonly Color Wood = Hex("6E4829");
    public static readonly Color Scrim = new Color(16 / 255f, 13 / 255f, 8 / 255f, 0.66f);

    public static Color Hex(string hex)
    {
        return ColorUtility.TryParseHtmlString(hex.StartsWith("#") ? hex : "#" + hex, out var c) ? c : Color.magenta;
    }

    public static Color Alpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

    // ---- Panel and font ---------------------------------------------------------------------------------------

    static bool fontReady;
    static FontDefinition fontDefinition;

    /// <summary>
    /// The City's font: Coming Soon (Resources/OnlineCity/CityFont, the design's font). Letters it doesn't have
    /// (Turkish ğ Ğ ş Ş İ) come from Patrick Hand (CityFontFallback, a similar handwriting), then Unity's built-in font.
    /// Without the file: the built-in font.
    /// </summary>
    public static FontDefinition FontDef
    {
        get
        {
            if (fontReady) return fontDefinition;
            fontReady = true;
            var builtin = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var main = Resources.Load<Font>("OnlineCity/CityFont");
            fontDefinition = FontDefinition.FromFont(main != null ? main : builtin);
            if (main == null) return fontDefinition;
            try
            {
                var mainAsset = UnityEngine.TextCore.Text.FontAsset.CreateFontAsset(main);
                var fallbacks = new System.Collections.Generic.List<UnityEngine.TextCore.Text.FontAsset>();
                var patrick = Resources.Load<Font>("OnlineCity/CityFontFallback");
                var patrickAsset = patrick != null ? UnityEngine.TextCore.Text.FontAsset.CreateFontAsset(patrick) : null;
                if (patrickAsset != null)
                {
                    // Matched to Coming Soon like the UI session's TMP copy: 1.15x to reach its letter heights, and
                    // thinner, because Patrick Hand's strokes are about 1.5x as thick at the same height
                    var face = patrickAsset.faceInfo;
                    face.scale = 1.15f;
                    patrickAsset.faceInfo = face;
                    patrickAsset.regularStyleWeight = -0.25f;
                    patrickAsset.boldStyleWeight = 0.35f;
                    fallbacks.Add(patrickAsset);
                }
                var builtinAsset = builtin != null ? UnityEngine.TextCore.Text.FontAsset.CreateFontAsset(builtin) : null;
                if (builtinAsset != null) fallbacks.Add(builtinAsset);
                if (mainAsset != null)
                {
                    mainAsset.fallbackFontAssetTable = fallbacks;
                    fontDefinition = FontDefinition.FromSDFFont(mainAsset);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[City] Couldn't set up the font fallback ({e.Message}); Turkish letters may be missing.");
            }
            return fontDefinition;
        }
    }

    /// <summary>Editor snapshots only: render every City panel into this texture instead of the screen.</summary>
    public static RenderTexture RenderTarget;

    public static PanelSettings CreatePanelSettings(int sortingOrder)
    {
        // A new PanelSettings warns in OnEnable, inside CreateInstance, that it has no theme yet; it gets one below
        bool logEnabled = Debug.unityLogger.logEnabled;
        Debug.unityLogger.logEnabled = false;
        PanelSettings settings;
        try
        {
            settings = ScriptableObject.CreateInstance<PanelSettings>();
        }
        finally
        {
            Debug.unityLogger.logEnabled = logEnabled;
        }
        if (RenderTarget != null) settings.targetTexture = RenderTarget;
        settings.name = "CityPanelSettings";
        settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        settings.referenceResolution = new Vector2Int(1920, 1080);
        settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
        settings.match = 0.5f;
        settings.sortingOrder = sortingOrder;
        settings.clearColor = RenderTarget != null;
        settings.colorClearValue = Color.black;
        var theme = Resources.Load<ThemeStyleSheet>("OnlineCity/CityTheme");
        if (theme != null) settings.themeStyleSheet = theme;
        else Debug.LogWarning("[City] Resources/OnlineCity/CityTheme.tss is missing, the City UI will not render properly.");
        return settings;
    }

    public static void ApplyRootStyle(VisualElement root)
    {
        root.style.unityFontDefinition = FontDef;
        root.style.color = Ink;
        root.style.fontSize = 22;
    }

    // ---- Style helpers ----------------------------------------------------------------------------------------

    public static void Border(VisualElement e, float width, Color color)
    {
        e.style.borderTopWidth = width;
        e.style.borderRightWidth = width;
        e.style.borderBottomWidth = width;
        e.style.borderLeftWidth = width;
        e.style.borderTopColor = color;
        e.style.borderRightColor = color;
        e.style.borderBottomColor = color;
        e.style.borderLeftColor = color;
    }

    /// <summary>Slightly uneven corners, like the design's hand-drawn boxes.</summary>
    public static void Radius(VisualElement e, float tl, float tr, float br, float bl)
    {
        e.style.borderTopLeftRadius = tl;
        e.style.borderTopRightRadius = tr;
        e.style.borderBottomRightRadius = br;
        e.style.borderBottomLeftRadius = bl;
    }

    public static void Radius(VisualElement e, float r) => Radius(e, r, r, r, r);

    public static void Padding(VisualElement e, float v, float h)
    {
        e.style.paddingTop = v;
        e.style.paddingBottom = v;
        e.style.paddingLeft = h;
        e.style.paddingRight = h;
    }

    public static void Padding(VisualElement e, float all) => Padding(e, all, all);

    public static void Margin(VisualElement e, float top, float right, float bottom, float left)
    {
        e.style.marginTop = top;
        e.style.marginRight = right;
        e.style.marginBottom = bottom;
        e.style.marginLeft = left;
    }

    public static void Absolute(VisualElement e, float left, float top, float width = -1, float height = -1)
    {
        e.style.position = Position.Absolute;
        e.style.left = left;
        e.style.top = top;
        if (width >= 0) e.style.width = width;
        if (height >= 0) e.style.height = height;
    }

    public static void Fill(VisualElement e)
    {
        e.style.position = Position.Absolute;
        e.style.left = 0;
        e.style.top = 0;
        e.style.right = 0;
        e.style.bottom = 0;
    }

    public static VisualElement Row(float gap = 0, Align align = Align.Center)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = align;
        if (gap > 0) row.RegisterCallback<GeometryChangedEvent>(_ => ApplyGap(row, gap, true));
        return row;
    }

    public static VisualElement Column(float gap = 0)
    {
        var col = new VisualElement();
        col.style.flexDirection = FlexDirection.Column;
        if (gap > 0) col.RegisterCallback<GeometryChangedEvent>(_ => ApplyGap(col, gap, false));
        return col;
    }

    /// <summary>Space between children (older UI Toolkit has no "gap"): a margin on every child but the first.</summary>
    static void ApplyGap(VisualElement parent, float gap, bool row)
    {
        int i = 0;
        foreach (var child in parent.Children())
        {
            if (child.style.position == Position.Absolute) continue;
            float m = i == 0 ? 0 : gap;
            if (row) { if (child.style.marginLeft != m) child.style.marginLeft = m; }
            else { if (child.style.marginTop != m) child.style.marginTop = m; }
            i++;
        }
    }

    public static VisualElement Spacer()
    {
        var s = new VisualElement();
        s.style.flexGrow = 1;
        return s;
    }

    public static Label Text(string text, float size = 22, Color? color = null, bool bold = false)
    {
        var label = new Label(text);
        label.style.fontSize = size;
        label.style.color = color ?? Ink;
        label.style.unityFontStyleAndWeight = bold ? FontStyle.Bold : FontStyle.Normal;
        label.style.whiteSpace = WhiteSpace.Normal;
        label.style.marginTop = 0;
        label.style.marginBottom = 0;
        label.style.paddingTop = 0;
        label.style.paddingBottom = 0;
        label.pickingMode = PickingMode.Ignore;
        return label;
    }

    /// <summary>
    /// Capitals for labels. The dotless ı always becomes I; with turkish, the dotted i becomes İ
    /// (so "Şişli" reads ŞİŞLİ and "Kadıköy" KADIKÖY, not KADıKÖY).
    /// </summary>
    public static string Upper(string text, bool turkish = false)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var chars = text.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            char c = chars[i];
            chars[i] = c == '\u0131' ? 'I' : turkish && c == 'i' ? '\u0130' : char.ToUpperInvariant(c);
        }
        return new string(chars);
    }

    public static Label OneLine(Label label)
    {
        label.style.whiteSpace = WhiteSpace.NoWrap;
        label.style.overflow = Overflow.Hidden;
        label.style.textOverflow = TextOverflow.Ellipsis;
        return label;
    }

    /// <summary>A paper card with an ink outline.</summary>
    public static VisualElement CardBox(Color? background = null, float border = 3f)
    {
        var card = new VisualElement();
        card.style.backgroundColor = background ?? Paper;
        Border(card, border, Ink);
        Radius(card, 18, 26, 20, 30);
        return card;
    }

    /// <summary>
    /// Put a card on a shadow: a wrapper holding an offset dark copy behind the card (UI Toolkit has no box-shadow).
    /// Position and size the returned wrapper; the card fills it.
    /// </summary>
    public static VisualElement Shadowed(VisualElement card, Vector2 offset, Color color)
    {
        var wrapper = new VisualElement();
        wrapper.style.flexShrink = 0;
        var shadow = new VisualElement();
        shadow.style.position = Position.Absolute;
        shadow.style.left = offset.x;
        shadow.style.top = offset.y;
        shadow.style.right = -offset.x;
        shadow.style.bottom = -offset.y;
        shadow.style.backgroundColor = color;
        shadow.style.borderTopLeftRadius = card.style.borderTopLeftRadius;
        shadow.style.borderTopRightRadius = card.style.borderTopRightRadius;
        shadow.style.borderBottomLeftRadius = card.style.borderBottomLeftRadius;
        shadow.style.borderBottomRightRadius = card.style.borderBottomRightRadius;
        shadow.pickingMode = PickingMode.Ignore;
        wrapper.Add(shadow);
        card.style.flexGrow = 1;
        wrapper.Add(card);
        return wrapper;
    }

    /// <summary>The design's lifted-panel shadow.</summary>
    public static VisualElement Lifted(VisualElement card) => Shadowed(card, new Vector2(0, 14), new Color(0, 0, 0, 0.32f));

    /// <summary>The design's hard ink shadow (small cards, chips on the map).</summary>
    public static VisualElement Inked(VisualElement card) => Shadowed(card, new Vector2(3, 4), Alpha(Ink, 0.9f));

    public static void RoundedRect(Painter2D p, Rect r, float radius)
    {
        radius = Mathf.Min(radius, Mathf.Min(r.width, r.height) * 0.5f);
        p.MoveTo(new Vector2(r.xMin + radius, r.yMin));
        p.LineTo(new Vector2(r.xMax - radius, r.yMin));
        p.ArcTo(new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMin + radius), radius);
        p.LineTo(new Vector2(r.xMax, r.yMax - radius));
        p.ArcTo(new Vector2(r.xMax, r.yMax), new Vector2(r.xMax - radius, r.yMax), radius);
        p.LineTo(new Vector2(r.xMin + radius, r.yMax));
        p.ArcTo(new Vector2(r.xMin, r.yMax), new Vector2(r.xMin, r.yMax - radius), radius);
        p.LineTo(new Vector2(r.xMin, r.yMin + radius));
        p.ArcTo(new Vector2(r.xMin, r.yMin), new Vector2(r.xMin + radius, r.yMin), radius);
        p.ClosePath();
    }

    public static VisualElement Chip(string text, Color background, string icon = null, float size = 19)
    {
        var chip = Row(8);
        chip.style.backgroundColor = background;
        Border(chip, 2, Ink);
        Radius(chip, 10, 14, 9, 15);
        Padding(chip, 3, 12);
        chip.style.flexShrink = 0;
        if (icon != null) chip.Add(new IconView(icon, size + 3));
        var label = Text(text, size);
        label.name = "text";
        label.style.whiteSpace = WhiteSpace.NoWrap;
        chip.Add(label);
        return chip;
    }

    public static void SetChipText(VisualElement chip, string text)
    {
        var label = chip.Q<Label>("text");
        if (label != null) label.text = text;
    }

    /// <summary>A line of a ledger: "Takeover price:" ...... "$375M" (blue rule underneath, like the design).</summary>
    public static VisualElement LedgerLine(string label, string value, Color? valueColor = null, float valueSize = 24)
    {
        var row = Row(12);
        row.style.justifyContent = Justify.SpaceBetween;
        row.style.minHeight = 48;
        row.style.borderBottomWidth = 2;
        row.style.borderBottomColor = Rule;
        var l = Text(label, 20, InkSoft);
        var v = Text(value, valueSize, valueColor ?? Ink, true);
        v.name = "value";
        v.style.unityTextAlign = TextAnchor.MiddleRight;
        row.Add(l);
        row.Add(v);
        return row;
    }

    /// <summary>Yellow highlighter behind a word, like the design's marked text.</summary>
    public static Label Highlighted(string text, float size)
    {
        var label = Text(text, size, Ink, true);
        label.style.backgroundColor = Alpha(Highlight, 0.95f);
        label.style.paddingLeft = 6;
        label.style.paddingRight = 6;
        return label;
    }

    public static TextField Field(string value, int maxLength, float fontSize = 26)
    {
        var field = new TextField { value = value ?? "", maxLength = maxLength };
        field.style.marginLeft = 0;
        field.style.marginRight = 0;
        field.style.marginTop = 0;
        field.style.marginBottom = 0;
        field.style.fontSize = fontSize;
        field.RegisterCallback<AttachToPanelEvent>(_ => StyleFieldInput(field));
        StyleFieldInput(field);
        return field;
    }

    static void StyleFieldInput(TextField field)
    {
        var input = field.Q(className: "unity-base-text-field__input") ?? field.Q(className: "unity-text-field__input");
        if (input == null) return;
        input.style.backgroundColor = Card;
        Border(input, 3, Ink);
        Radius(input, 10, 14, 9, 15);
        input.style.minHeight = 56;
        Padding(input, 6, 16);
        input.style.color = Ink;
        input.style.fontSize = field.style.fontSize;
        input.style.unityFontDefinition = FontDef;
    }

    // ---- Buttons ----------------------------------------------------------------------------------------------

    public enum ButtonKind { Paper, Primary, Gold, Danger, Felt, Ghost }

    /// <summary>
    /// The design's button: rounded ink outline, hard offset shadow; it lifts on hover and presses down on click.
    /// </summary>
    public class InkButton : VisualElement
    {
        readonly VisualElement shadow;
        readonly VisualElement face;
        readonly Label label;
        readonly IconView icon;
        readonly Action onClick;
        bool hovered, pressed, enabled = true;
        Vector2 shadowOffset;

        public Label Label => label;
        /// <summary>The button's face: add your own content here for buttons with more than an icon and a word.</summary>
        public VisualElement Face => face;

        public InkButton(string text, Action onClick, ButtonKind kind = ButtonKind.Paper, string iconId = null,
            float height = 64, float fontSize = 26)
        {
            this.onClick = onClick;
            style.flexShrink = 0;
            style.height = height + 6;
            style.paddingRight = 6;
            style.paddingBottom = 6;

            shadowOffset = kind == ButtonKind.Ghost ? Vector2.zero : new Vector2(4, 5);
            shadow = new VisualElement();
            shadow.style.position = Position.Absolute;
            shadow.style.left = shadowOffset.x;
            shadow.style.top = shadowOffset.y;
            shadow.style.right = 6 - shadowOffset.x;
            shadow.style.bottom = 6 - shadowOffset.y;
            shadow.style.backgroundColor = kind == ButtonKind.Felt ? FeltDark : Ink;
            Radius(shadow, 14, 20, 12, 22);
            shadow.pickingMode = PickingMode.Ignore;
            Add(shadow);

            face = Row(12);
            face.style.justifyContent = Justify.Center;
            face.style.height = height;
            Padding(face, 0, height >= 60 ? 32 : 20);
            Border(face, 3, Ink);
            Radius(face, 14, 20, 12, 22);
            face.pickingMode = PickingMode.Ignore;
            Color bg, fg = Ink;
            switch (kind)
            {
                case ButtonKind.Primary: bg = Highlight; break;
                case ButtonKind.Gold: bg = Gold; break;
                case ButtonKind.Danger: bg = Red; fg = Card; break;
                case ButtonKind.Felt: bg = Felt; fg = Card; break;
                case ButtonKind.Ghost: bg = Alpha(Card, 0); break;
                default: bg = Card; break;
            }
            face.style.backgroundColor = bg;
            if (kind == ButtonKind.Ghost) Border(face, 0, Ink);
            Add(face);

            if (iconId != null)
            {
                icon = new IconView(iconId, fontSize + 2, fg);
                face.Add(icon);
            }
            if (!string.IsNullOrEmpty(text))
            {
                label = Text(text, fontSize, fg, kind == ButtonKind.Primary || kind == ButtonKind.Gold || kind == ButtonKind.Danger);
                OneLine(label);
                face.Add(label);
            }

            RegisterCallback<PointerEnterEvent>(_ => { hovered = true; Refresh(); });
            RegisterCallback<PointerLeaveEvent>(_ => { hovered = false; pressed = false; Refresh(); });
            RegisterCallback<PointerDownEvent>(e =>
            {
                if (!enabled || e.button != 0) return;
                pressed = true;
                Refresh();
            });
            RegisterCallback<PointerUpEvent>(e =>
            {
                if (e.button != 0) return;
                bool fire = pressed && enabled;
                pressed = false;
                Refresh();
                if (fire)
                {
                    e.StopPropagation();
                    this.onClick?.Invoke();
                }
            });
        }

        public void SetText(string text)
        {
            if (label != null) label.text = text;
        }

        public void SetEnabledLook(bool on)
        {
            enabled = on;
            style.opacity = on ? 1f : 0.45f;
            Refresh();
        }

        public bool IsOn => enabled;

        void Refresh()
        {
            float x = 0, y = 0;
            if (enabled && pressed) { x = shadowOffset.x - 1; y = shadowOffset.y - 1; }
            else if (enabled && hovered) { x = -1; y = -2; }
            face.style.translate = new Translate(x, y);
        }
    }

    /// <summary>The round close button (top-right of panels).</summary>
    public static InkButton CloseButton(Action onClose)
    {
        var button = new InkButton(null, onClose, ButtonKind.Paper, "close", 56, 26);
        button.style.width = 62;
        return button;
    }

    // ---- Icons ------------------------------------------------------------------------------------------------

    public class IconView : VisualElement
    {
        string id;
        Color color;
        readonly float pen;

        public IconView(string id, float size, Color? color = null, float pen = 2.4f)
        {
            this.id = id;
            this.color = color ?? CityUi.Ink;
            this.pen = pen;
            style.width = size;
            style.height = size;
            style.flexShrink = 0;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        public void Set(string newId, Color? newColor = null)
        {
            id = newId;
            if (newColor.HasValue) color = newColor.Value;
            MarkDirtyRepaint();
        }

        void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            if (r.width < 1 || r.height < 1) return;
            CityIcons.DrawUiIcon(ctx.painter2D, id, r, color, pen);
        }
    }

    // ---- Overlays ---------------------------------------------------------------------------------------------

    /// <summary>A full-screen dark scrim that blocks the map, with the dialog centred on it.</summary>
    public static VisualElement Modal(VisualElement root, VisualElement dialog, Action onScrimClick = null)
    {
        var scrim = new VisualElement();
        Fill(scrim);
        scrim.style.backgroundColor = Scrim;
        scrim.style.alignItems = Align.Center;
        scrim.style.justifyContent = Justify.Center;
        scrim.RegisterCallback<PointerDownEvent>(e =>
        {
            if (e.target == scrim) onScrimClick?.Invoke();
            e.StopPropagation();
        });
        scrim.RegisterCallback<WheelEvent>(e => e.StopPropagation());
        scrim.Add(dialog);
        root.Add(scrim);
        return scrim;
    }

    /// <summary>A dialog card with a title and a close button (returned on its shadow, ready for Modal).</summary>
    public static VisualElement Dialog(string title, float width, Action onClose, out VisualElement body)
    {
        var card = CardBox();
        Padding(card, 32, 40);
        var header = Row(16);
        header.style.marginBottom = 20;
        var titleLabel = Text(title, 44, Ink, true);
        titleLabel.name = "title";
        titleLabel.style.flexGrow = 1;
        titleLabel.style.flexShrink = 1;
        header.Add(titleLabel);
        if (onClose != null) header.Add(CloseButton(onClose));
        card.Add(header);
        body = Column(18);
        card.Add(body);
        var lifted = Lifted(card);
        lifted.style.width = width;
        return lifted;
    }

    public static string Ago(long seconds)
    {
        if (seconds < 60) return "just now";
        if (seconds < 3600) return (seconds / 60) + " min ago";
        if (seconds < 86400) return (seconds / 3600) + " h ago";
        return (seconds / 86400) + " days ago";
    }
}
