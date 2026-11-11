using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>What a CasinoIcon draws.</summary>
public enum CasinoIconKind
{
    None,
    // Slot symbols, in the order of SlotMachineGame's symbols (48 x 48, full colour with ink outlines)
    Cherry,
    Lemon,
    Orange,
    Grape,
    Bell,
    Star,
    Seven,
    Diamond,
    // Card suits (24 x 24, filled in the graphic's colour)
    Spade,
    Heart,
    DiamondSuit,
    Club,
    // Line icons (24 x 24, stroked in the graphic's colour)
    Back,
    Sparkle,
    Spin,
    Cards,
    Coin,
    Banknote,
    Play,
    Plus,
    Minus,
    Close,
    Check,
    Pause,
    // Pieces
    /// <summary>The slot machine's lever (72 x 300, fitted into the rect); Amount = how far it is pulled (0-1).</summary>
    Lever,
    /// <summary>45 degree stripes over the whole rect (a card back); Amount = stripe period (default 14), StrokeWidth = stripe width (default 6).</summary>
    Hatch,
    /// <summary>Shade along the top and bottom edges of the rect (a reel window); Amount = band height.</summary>
    EdgeShade,
    /// <summary>A dotted line along the middle of the rect, dots as tall as the rect; Amount = gap between dots.</summary>
    Dots,
    /// <summary>A dashed line along the middle of the rect, as thick as the rect; StrokeWidth = dash length, Amount = gap.</summary>
    Dashes,
    /// <summary>Round dots along the rect's rounded edge (CSS border: dotted, radius 12); StrokeWidth = dot size, Amount = gap.</summary>
    DotFrame,
    /// <summary>A gold triangle with an ink outline pointing right (20 x 28, fitted); StrokeWidth = outline.</summary>
    Pointer
}

/// <summary>
/// The casino's drawn pictures, as geometry (no sprites, no emoji): the eight slot symbols of the design boards
/// (cherry, lemon, orange, grape, bell, star, seven, diamond: the boards' SVGs with their colours and ink outlines),
/// the four card suits, the line icons of the casino screens, the slot machine's lever and a few patterns. Symbols and
/// the lever keep their own colours, tinted by the graphic's colour (its alpha fades them); suits, line icons and
/// patterns are drawn in the graphic's colour. Uses the build mode's mesh builder.
/// Requires a CanvasRenderer (Unity UI 2.0's Graphic no longer adds one).
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class CasinoIcon : MaskableGraphic
{
    public const int SymbolCount = 8;

    static readonly BlueprintMeshBuilder Shared = new BlueprintMeshBuilder();
    static readonly List<Vector2> P = new List<Vector2>(96);
    static readonly List<Vector2> ClipA = new List<Vector2>(8);
    static readonly List<Vector2> ClipB = new List<Vector2>(8);

    // The boards' symbol colours
    static readonly Color Red = UIColors.Loss;
    static readonly Color Leaf = UIColors.Money;
    static readonly Color Ink = UIColors.Ink;
    static readonly Color Paper = UIColors.PaperHi;
    static readonly Color LemonYellow = UIColors.Hex(0xF3D34A);
    static readonly Color OrangePeel = UIColors.Hex(0xE8892B);
    static readonly Color GrapePurple = UIColors.Hex(0x7A4FA3);
    static readonly Color Gold = UIColors.Gold;
    static readonly Color GoldDark = UIColors.GoldDark;
    static readonly Color GoldLight = UIColors.Hex(0xF7DC87);
    static readonly Color Ice = UIColors.Hex(0x5FB4E6);
    static readonly Color Steel = UIColors.Hex(0x7E838B);
    static readonly Color SteelLight = UIColors.Hex(0xA9AEB6);

    [SerializeField] CasinoIconKind kind = CasinoIconKind.None;
    [Tooltip("Outline or line width in icon units; below 0 = the kind's own (symbols 1.5 of 48, line icons 2.4 of 24)")]
    [SerializeField] float strokeWidth = -1f;
    [Tooltip("Lever: how far it is pulled (0-1). Hatch: stripe period. EdgeShade: band height. Dots / Dashes: the gap")]
    [SerializeField] float amount;

    public CasinoIconKind Kind
    {
        get => kind;
        set { if (kind != value) { kind = value; SetVerticesDirty(); } }
    }

    public float StrokeWidth
    {
        get => strokeWidth;
        set { if (strokeWidth != value) { strokeWidth = value; SetVerticesDirty(); } }
    }

    public float Amount
    {
        get => amount;
        set { if (amount != value) { amount = value; SetVerticesDirty(); } }
    }

    /// <summary>The slot symbol with this index (SlotMachineGame's order: cherry ... diamond).</summary>
    public static CasinoIconKind Symbol(int index)
    {
        return CasinoIconKind.Cherry + Mathf.Clamp(index, 0, SymbolCount - 1);
    }

    /// <summary>The suit of a card suit number (0 spades, 1 hearts, 2 diamonds, 3 clubs).</summary>
    public static CasinoIconKind Suit(int suit)
    {
        switch (suit)
        {
            case 1: return CasinoIconKind.Heart;
            case 2: return CasinoIconKind.DiamondSuit;
            case 3: return CasinoIconKind.Club;
            default: return CasinoIconKind.Spade;
        }
    }

    public static bool IsSymbol(CasinoIconKind k)
    {
        return k >= CasinoIconKind.Cherry && k <= CasinoIconKind.Diamond;
    }

    /// <summary>A new icon <paramref name="size"/> px square, centred on (x, y) from the parent's top-left.</summary>
    public static CasinoIcon Create(Transform parent, CasinoIconKind kind, float size, Color color, float x, float y, float stroke = -1f)
    {
        RectTransform rect = BlueprintUI.NewRect(kind.ToString(), parent);
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(size, size);
        CasinoIcon icon = rect.gameObject.AddComponent<CasinoIcon>();
        icon.kind = kind;
        icon.strokeWidth = stroke;
        icon.color = color;
        icon.raycastTarget = false;
        return icon;
    }

    Color32 Tint(Color c)
    {
        return c * color;
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (kind == CasinoIconKind.None) return;
        Rect r = rectTransform.rect;
        if (r.width <= 0f || r.height <= 0f) return;
        if (kind == CasinoIconKind.EdgeShade)
        {
            DrawEdgeShade(vh, r);
            return;
        }

        BlueprintMeshBuilder m = Shared;
        m.Clear();
        m.Feather = BlueprintBox.SoftEdge(canvas);
        m.MiterLimit = 2f;

        switch (kind)
        {
            case CasinoIconKind.Hatch:
                m.SetYDownFrame(new Vector2(r.xMin, r.yMax));
                DrawHatch(m, r.width, r.height, amount > 1f ? amount : 14f, strokeWidth > 0f ? strokeWidth : 6f, color);
                break;
            case CasinoIconKind.Dots:
            case CasinoIconKind.Dashes:
                m.SetYDownFrame(new Vector2(r.xMin, r.yMax));
                DrawLine(m, r.width, r.height);
                break;
            case CasinoIconKind.DotFrame:
                m.SetYDownFrame(new Vector2(r.xMin, r.yMax));
                DrawDotFrame(m, r.width, r.height, strokeWidth > 0f ? strokeWidth : 6f, amount > 0f ? amount : -1f, color);
                break;
            case CasinoIconKind.Pointer:
            {
                float scale = Mathf.Min(r.width / 20f, r.height / 28f);
                m.SetYDownFrame(new Vector2(r.center.x - 10f * scale, r.center.y + 14f * scale), scale);
                Begin(2f, 2f); L(18f, 14f); L(2f, 26f);
                FillOutlined(m, Gold, strokeWidth > 0f ? strokeWidth : 2.5f);
                break;
            }
            case CasinoIconKind.Lever:
            {
                float scale = Mathf.Min(r.width / 72f, r.height / 300f);
                m.SetYDownFrame(new Vector2(r.center.x - 36f * scale, r.center.y + 150f * scale), scale);
                DrawLever(m, Mathf.Clamp01(amount));
                break;
            }
            default:
            {
                float box = IsSymbol(kind) ? 48f : 24f;
                float size = Mathf.Min(r.width, r.height);
                m.SetYDownFrame(new Vector2(r.center.x - size * 0.5f, r.center.y + size * 0.5f), size / box);
                if (IsSymbol(kind)) DrawSymbol(m, kind, strokeWidth >= 0f ? strokeWidth : 1.5f);
                else DrawGlyph(m, kind, strokeWidth > 0f ? strokeWidth : 2.4f, color);
                break;
            }
        }
        vh.AddUIVertexStream(m.Vertices, m.Indices);
    }

    // ------------------------------------------------------------------
    // Path helpers (icon units, y down)
    // ------------------------------------------------------------------

    static void Begin(float x, float y)
    {
        P.Clear();
        P.Add(new Vector2(x, y));
    }

    static void L(float x, float y)
    {
        P.Add(new Vector2(x, y));
    }

    static void C(float x1, float y1, float x2, float y2, float x, float y)
    {
        BlueprintMeshBuilder.CubicTo(P, new Vector2(x1, y1), new Vector2(x2, y2), new Vector2(x, y), 10);
    }

    static void A(float radius, bool large, bool sweep, float x, float y)
    {
        BlueprintMeshBuilder.ArcTo(P, radius, large, sweep, new Vector2(x, y), 10);
    }

    /// <summary>Fill the current path, then outline it in ink (w = 0: no outline).</summary>
    void FillOutlined(BlueprintMeshBuilder m, Color fill, float w)
    {
        m.FillPolygon(P, Tint(fill));
        if (w > 0f) m.Stroke(P, w, Tint(Ink), true);
    }

    void Disc(BlueprintMeshBuilder m, float x, float y, float radius, Color fill, float w)
    {
        m.FillCircle(new Vector2(x, y), radius, Tint(fill));
        if (w > 0f) m.Circle(new Vector2(x, y), radius, w, Tint(Ink));
    }

    /// <summary>Stroke the current path open, with round ends (SVG stroke-linecap: round).</summary>
    static void RoundLine(BlueprintMeshBuilder m, Color32 c, float w)
    {
        if (P.Count < 2) return;
        m.Stroke(P, w, c, false);
        m.FillCircle(P[0], w * 0.5f, c);
        m.FillCircle(P[P.Count - 1], w * 0.5f, c);
    }

    static Color WithAlpha(Color c, float a)
    {
        c.a *= a;
        return c;
    }

    // ------------------------------------------------------------------
    // Slot symbols (the boards' 48 x 48 SVGs; w = ink outline width)
    // ------------------------------------------------------------------

    void DrawSymbol(BlueprintMeshBuilder m, CasinoIconKind symbol, float w)
    {
        switch (symbol)
        {
            case CasinoIconKind.Cherry:
                Begin(17f, 31f); C(18f, 21f, 23f, 12f, 31f, 8f); RoundLine(m, Tint(Leaf), 3f);
                Begin(33f, 33f); C(32f, 23f, 32f, 14f, 31f, 8f); RoundLine(m, Tint(Leaf), 3f);
                Begin(31f, 8f); C(35f, 3.5f, 41f, 3.5f, 44f, 6.5f); C(40.5f, 10.5f, 34.5f, 11f, 31f, 8f); FillOutlined(m, Leaf, w);
                Disc(m, 15.5f, 35f, 9f, Red, w);
                Disc(m, 33f, 37f, 8.5f, Red, w);
                Disc(m, 12.5f, 31.5f, 2.2f, Paper, 0f);
                Disc(m, 30.3f, 33.8f, 2f, Paper, 0f);
                break;

            case CasinoIconKind.Lemon:
                Begin(4f, 30f); C(3f, 19f, 14f, 10f, 27f, 11f); C(38f, 12f, 45f, 20f, 44f, 25f); C(43f, 34f, 32f, 40f, 20f, 38f);
                C(11f, 37f, 5f, 34f, 4f, 30f); FillOutlined(m, LemonYellow, w);
                if (w > 0f)
                {
                    Begin(4.3f, 31f); L(1.8f, 32.8f); RoundLine(m, Tint(Ink), w);
                    Begin(43.8f, 23.5f); L(46.3f, 21.5f); RoundLine(m, Tint(Ink), w);
                }
                Begin(13f, 20f); C(16.5f, 16.5f, 21f, 15f, 26f, 15f); RoundLine(m, Tint(Paper), 2.5f);
                break;

            case CasinoIconKind.Orange:
                Disc(m, 24f, 27f, 16f, OrangePeel, w);
                Begin(24f, 11f); C(26f, 6f, 32f, 4.5f, 36f, 6f); C(34f, 10.5f, 28.5f, 12f, 24f, 11f); FillOutlined(m, Leaf, w);
                Begin(15f, 23f); C(16.5f, 19f, 19.5f, 16.5f, 23f, 16f); RoundLine(m, Tint(Paper), 2.5f);
                Disc(m, 30f, 33f, 1.2f, WithAlpha(Ink, 0.35f), 0f);
                Disc(m, 34f, 26f, 1.2f, WithAlpha(Ink, 0.35f), 0f);
                Disc(m, 22f, 36f, 1.2f, WithAlpha(Ink, 0.35f), 0f);
                break;

            case CasinoIconKind.Grape:
                Begin(24f, 13f); C(24f, 9f, 26.5f, 6f, 30.5f, 4f); RoundLine(m, Tint(Leaf), 3f);
                Begin(24f, 11f); C(20f, 6.5f, 13.5f, 6.5f, 10.5f, 9.5f); C(13.5f, 13.5f, 20f, 14.5f, 24f, 11f); FillOutlined(m, Leaf, w);
                Disc(m, 13.5f, 20f, 6.2f, GrapePurple, w);
                Disc(m, 34.5f, 20f, 6.2f, GrapePurple, w);
                Disc(m, 24f, 18.5f, 6.2f, GrapePurple, w);
                Disc(m, 18.7f, 30f, 6.2f, GrapePurple, w);
                Disc(m, 29.3f, 30f, 6.2f, GrapePurple, w);
                Disc(m, 24f, 40f, 6.2f, GrapePurple, w);
                Disc(m, 22f, 16.5f, 1.5f, Paper, 0f);
                Disc(m, 16.7f, 28f, 1.5f, Paper, 0f);
                Disc(m, 22f, 38f, 1.5f, Paper, 0f);
                break;

            case CasinoIconKind.Bell:
                Disc(m, 24f, 5.5f, 2.6f, Gold, w);
                Disc(m, 24f, 39.5f, 4f, GoldDark, w);
                Begin(24f, 7.5f); C(16.5f, 7.5f, 12.5f, 13f, 12.5f, 21f); L(12.5f, 29f); L(8f, 35f); L(40f, 35f); L(35.5f, 29f); L(35.5f, 21f);
                C(35.5f, 13f, 31.5f, 7.5f, 24f, 7.5f); FillOutlined(m, Gold, w);
                if (w > 0f)
                {
                    Begin(11f, 32f); L(37f, 32f); RoundLine(m, Tint(Ink), w);
                }
                Begin(17.5f, 14.5f); C(16.8f, 17.5f, 16.5f, 21f, 16.5f, 26f); RoundLine(m, Tint(GoldLight), 2.5f);
                break;

            case CasinoIconKind.Star:
                Begin(24f, 5f); L(29.3f, 17.7f); L(43f, 18.8f); L(32.6f, 27.8f); L(35.8f, 41.2f); L(24f, 34f); L(12.2f, 41.2f); L(15.4f, 27.8f);
                L(5f, 18.8f); L(18.7f, 17.7f); FillOutlined(m, Gold, w);
                Begin(22.6f, 12.5f); L(20.9f, 17.2f); RoundLine(m, Tint(GoldLight), 2.2f);
                break;

            case CasinoIconKind.Seven:
                // The boards set a red "7" with an ink outline; drawn here so every reel looks the same
                Begin(8.5f, 7.5f); L(39.5f, 6f); C(40.6f, 6f, 41.2f, 7f, 40.8f, 8.2f); L(40.2f, 12.6f); L(27.6f, 42f); L(18.2f, 42f);
                L(30.4f, 14.2f); L(9.4f, 14.8f); C(8.4f, 14.8f, 7.8f, 14.2f, 7.8f, 13.2f); L(7.8f, 8.6f); C(7.8f, 8f, 8f, 7.6f, 8.5f, 7.5f);
                FillOutlined(m, Red, w);
                Begin(11.5f, 10.2f); L(20f, 9.8f); RoundLine(m, Tint(WithAlpha(Paper, 0.6f)), 1.8f);
                break;

            case CasinoIconKind.Diamond:
                Begin(13f, 11f); L(35f, 11f); L(44f, 20f); L(24f, 42f); L(4f, 20f); FillOutlined(m, Ice, w);
                if (w > 0f)
                {
                    Color32 facet = Tint(WithAlpha(Ink, 0.55f));
                    Begin(4f, 20f); L(44f, 20f); m.Stroke(P, w, facet, false);
                    Begin(13f, 11f); L(18f, 20f); L(24f, 11f); L(30f, 20f); L(35f, 11f); m.Stroke(P, w, facet, false);
                    Begin(18f, 20f); L(24f, 42f); L(30f, 20f); m.Stroke(P, w, facet, false);
                }
                Begin(15.5f, 12.5f); L(21.5f, 12.5f); L(18f, 17.5f); m.FillPolygon(P, Tint(WithAlpha(Paper, 0.85f)));
                break;
        }
    }

    // ------------------------------------------------------------------
    // Suits and line icons (24 x 24, the graphic's colour)
    // ------------------------------------------------------------------

    static void DrawGlyph(BlueprintMeshBuilder m, CasinoIconKind glyph, float w, Color32 c)
    {
        switch (glyph)
        {
            case CasinoIconKind.Spade:
                Begin(12f, 2.5f); C(12f, 2.5f, 4.5f, 7.9f, 4.5f, 13f); A(3.7f, false, false, 10.9f, 15.5f); L(10f, 21f); L(14f, 21f); L(13.1f, 15.5f);
                A(3.7f, false, false, 19.5f, 13f); C(19.5f, 7.9f, 12f, 2.5f, 12f, 2.5f);
                m.FillPolygon(P, c);
                break;
            case CasinoIconKind.Heart:
                Begin(12f, 20.5f); C(12f, 20.5f, 4.5f, 15.9f, 4.5f, 10.5f); A(4.2f, false, true, 12f, 8f); A(4.2f, false, true, 19.5f, 10.5f);
                C(19.5f, 15.9f, 12f, 20.5f, 12f, 20.5f);
                m.FillPolygon(P, c);
                break;
            case CasinoIconKind.DiamondSuit:
                Begin(12f, 2.5f); L(19f, 12f); L(12f, 21.5f); L(5f, 12f);
                m.FillPolygon(P, c);
                break;
            case CasinoIconKind.Club:
                m.FillCircle(new Vector2(12f, 7.5f), 3.4f, c);
                m.FillCircle(new Vector2(7.4f, 13.2f), 3.4f, c);
                m.FillCircle(new Vector2(16.6f, 13.2f), 3.4f, c);
                Begin(12f, 12f); L(10.4f, 21f); L(13.6f, 21f);
                m.FillPolygon(P, c);
                break;

            case CasinoIconKind.Back:
                // M20 12H5 M11 6l-6 6 6 6
                Begin(20f, 12f); L(5f, 12f); RoundLine(m, c, w);
                Begin(11f, 6f); L(5f, 12f); L(11f, 18f); RoundLine(m, c, w);
                break;
            case CasinoIconKind.Sparkle:
                // M12 3c.6 4.6 2.4 6.4 7 7-4.6.6-6.4 2.4-7 7-.6-4.6-2.4-6.4-7-7 4.6-.6 6.4-2.4 7-7z M19 16.5v4 M17 18.5h4
                Begin(12f, 3f); C(12.6f, 7.6f, 14.4f, 9.4f, 19f, 10f); C(14.4f, 10.6f, 12.6f, 12.4f, 12f, 17f);
                C(11.4f, 12.4f, 9.6f, 10.6f, 5f, 10f); C(9.6f, 9.4f, 11.4f, 7.6f, 12f, 3f);
                m.Stroke(P, w, c, true);
                Begin(19f, 16.5f); L(19f, 20.5f); RoundLine(m, c, w);
                Begin(17f, 18.5f); L(21f, 18.5f); RoundLine(m, c, w);
                break;
            case CasinoIconKind.Spin:
                RunEndIcon.Draw(m, RunEndIconKind.Restart, w, c);
                break;
            case CasinoIconKind.Cards:
                BlueprintIcon.Draw(m, BlueprintIconKind.Cards, w, c);
                break;
            case CasinoIconKind.Coin:
                BlueprintIcon.Draw(m, BlueprintIconKind.Coin, w, c);
                break;
            case CasinoIconKind.Banknote:
                RunEndIcon.Draw(m, RunEndIconKind.Bank, w, c);
                break;
            case CasinoIconKind.Play:
                RunEndIcon.Draw(m, RunEndIconKind.Play, w, c);
                break;
            case CasinoIconKind.Plus:
                BlueprintIcon.Draw(m, BlueprintIconKind.Plus, w, c);
                break;
            case CasinoIconKind.Minus:
                BlueprintIcon.Draw(m, BlueprintIconKind.Minus, w, c);
                break;
            case CasinoIconKind.Close:
                BlueprintIcon.Draw(m, BlueprintIconKind.Close, w, c);
                break;
            case CasinoIconKind.Check:
                BlueprintIcon.Draw(m, BlueprintIconKind.Check, w, c);
                break;
            case CasinoIconKind.Pause:
                BlueprintIcon.Draw(m, BlueprintIconKind.Pause, w, c);
                break;
        }
    }

    // ------------------------------------------------------------------
    // Pieces
    // ------------------------------------------------------------------

    /// <summary>The board's lever (72 x 300): steel base and joint, the arm and the red knob, pulled down by <paramref name="pull"/>.</summary>
    void DrawLever(BlueprintMeshBuilder m, float pull)
    {
        const float w = 3f;
        Color32 ink = Tint(Ink);
        Rect baseRect = new Rect(0f, 190f, 30f, 100f);
        m.FillRoundRect(baseRect, Round(8f), Tint(Steel));
        m.StrokeRoundRect(baseRect, Round(8f), w, ink);
        Rect joint = new Rect(14f, 236f, 40f, 22f);
        m.FillRoundRect(joint, Round(8f), Tint(SteelLight));
        m.StrokeRoundRect(joint, Round(8f), w, ink);

        float drop = pull * 150f;
        float armTop = 40f + drop;
        Rect arm = new Rect(36f, armTop, 14f, Mathf.Max(14f, 254f - armTop));
        m.FillRoundRect(arm, Round(7f), Tint(SteelLight));
        m.StrokeRoundRect(arm, Round(7f), w, ink);

        Vector2 knob = new Vector2(43f, 30f + drop);
        m.FillCircle(knob, 22f, Tint(Red));
        m.Circle(knob, 22f, w, ink);
        m.FillCircle(knob + new Vector2(-7f, -7f), 6f, Tint(WithAlpha(Paper, 0.55f)));
    }

    static Vector4 Round(float r)
    {
        return new Vector4(r, r, r, r);
    }

    /// <summary>CSS repeating-linear-gradient(45deg, colour 0 stripe, transparent stripe period): stripes running down-right.</summary>
    static void DrawHatch(BlueprintMeshBuilder m, float width, float height, float period, float stripe, Color32 c)
    {
        // Distance along the gradient direction (up-right in a y-down frame)
        const float k = 0.70710678f;
        float min = -height * k, max = width * k;
        int first = Mathf.FloorToInt(min / period) - 1, last = Mathf.CeilToInt(max / period) + 1;
        for (int i = first; i <= last; i++)
        {
            float from = i * period, to = from + Mathf.Min(stripe, period);
            ClipA.Clear();
            ClipA.Add(new Vector2(0f, 0f));
            ClipA.Add(new Vector2(width, 0f));
            ClipA.Add(new Vector2(width, height));
            ClipA.Add(new Vector2(0f, height));
            Clip(ClipA, ClipB, k, -k, from, true);
            Clip(ClipB, ClipA, k, -k, to, false);
            if (ClipA.Count >= 3) m.FillConvex(ClipA, c, 0f);
        }
    }

    /// <summary>Keep the part of a convex polygon where a*x + b*y is at least (above = true) or at most <paramref name="limit"/>.</summary>
    static void Clip(List<Vector2> input, List<Vector2> output, float a, float b, float limit, bool above)
    {
        output.Clear();
        int n = input.Count;
        for (int i = 0; i < n; i++)
        {
            Vector2 p = input[i], q = input[(i + 1) % n];
            float dp = a * p.x + b * p.y - limit, dq = a * q.x + b * q.y - limit;
            if (!above) { dp = -dp; dq = -dq; }
            bool pin = dp >= 0f, qin = dq >= 0f;
            if (pin) output.Add(p);
            if (pin != qin)
            {
                float t = dp / (dp - dq);
                output.Add(Vector2.Lerp(p, q, t));
            }
        }
    }

    /// <summary>A dotted (round dots) or dashed line along the middle of the rect.</summary>
    void DrawLine(BlueprintMeshBuilder m, float width, float height)
    {
        Color32 c = color;
        float thick = Mathf.Max(0.5f, height);
        if (kind == CasinoIconKind.Dots)
        {
            float gap = amount > 0f ? amount : thick;
            float step = thick + gap;
            for (float x = thick * 0.5f; x <= width - thick * 0.5f + 0.01f; x += step)
                m.FillCircle(new Vector2(x, height * 0.5f), thick * 0.5f, c);
            return;
        }
        P.Clear();
        P.Add(new Vector2(0f, height * 0.5f));
        P.Add(new Vector2(width, height * 0.5f));
        float dash = strokeWidth > 0f ? strokeWidth : thick * 3f;
        float space = amount > 0f ? amount : thick * 3f;
        m.Dashed(P, thick, c, false, dash, space);
    }

    /// <summary>Round dots of <paramref name="dot"/> px along a rounded rectangle (radius 12) just inside the rect.</summary>
    static void DrawDotFrame(BlueprintMeshBuilder m, float width, float height, float dot, float gap, Color32 c)
    {
        if (gap <= 0f) gap = dot;
        float inset = dot * 0.5f;
        m.RoundRectPath(P, new Rect(inset, inset, width - dot, height - dot), Round(Mathf.Max(0f, 12f - inset)));
        int n = P.Count;
        if (n < 2) return;
        float length = 0f;
        for (int i = 0; i < n; i++) length += Vector2.Distance(P[i], P[(i + 1) % n]);
        int count = Mathf.Max(4, Mathf.RoundToInt(length / (dot + gap)));
        float step = length / count;
        float travelled = 0f, next = 0f;
        for (int i = 0; i < n && count > 0; i++)
        {
            Vector2 a = P[i], b = P[(i + 1) % n];
            float segment = Vector2.Distance(a, b);
            while (next <= travelled + segment && count > 0)
            {
                float t = segment > 0f ? (next - travelled) / segment : 0f;
                m.FillCircle(Vector2.Lerp(a, b, t), dot * 0.5f, c);
                next += step;
                count--;
            }
            travelled += segment;
        }
    }

    /// <summary>
    /// CSS inset shadows along the top and bottom edges (the reel windows' "inset 0 22px 18px -14px"): the graphic's
    /// colour at the edge, fading out over Amount px.
    /// </summary>
    void DrawEdgeShade(VertexHelper vh, Rect r)
    {
        float band = Mathf.Min(amount > 0f ? amount : 24f, r.height * 0.5f);
        Color32 solid = color;
        Color32 half = color;
        half.a = (byte)(solid.a * 0.45f);
        Color32 clear = color;
        clear.a = 0;
        Band(vh, r.yMax, -band, r.xMin, r.xMax, solid, half, clear);
        Band(vh, r.yMin, band, r.xMin, r.xMax, solid, half, clear);
    }

    static void Band(VertexHelper vh, float edge, float depth, float x0, float x1, Color32 solid, Color32 half, Color32 clear)
    {
        int s = vh.currentVertCount;
        float mid = edge + depth * 0.4f, end = edge + depth;
        vh.AddVert(new Vector3(x0, edge), solid, Vector4.zero);
        vh.AddVert(new Vector3(x1, edge), solid, Vector4.zero);
        vh.AddVert(new Vector3(x0, mid), half, Vector4.zero);
        vh.AddVert(new Vector3(x1, mid), half, Vector4.zero);
        vh.AddVert(new Vector3(x0, end), clear, Vector4.zero);
        vh.AddVert(new Vector3(x1, end), clear, Vector4.zero);
        vh.AddTriangle(s, s + 1, s + 3);
        vh.AddTriangle(s + 3, s + 2, s);
        vh.AddTriangle(s + 2, s + 3, s + 5);
        vh.AddTriangle(s + 5, s + 4, s + 2);
    }
}
