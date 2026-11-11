using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>What a RunEndDecor draws.</summary>
public enum RunEndDecorKind
{
    None,
    /// <summary>Green-bar ledger paper: every second row (pitch high) in the primary colour, kept inside the rounded corners.</summary>
    LedgerBands,
    /// <summary>Tractor-feed strips down both sides: punched holes (primary) and a dashed tear line (secondary).</summary>
    TractorFeed,
    /// <summary>The "CEO of the year" gold rosette with red ribbon tails (the design's 190 x 230 SVG, scaled to fit).</summary>
    Seal
}

/// <summary>
/// Paper details of the run-end screens that a BlueprintBox can't draw, sized to the RectTransform (y down, like the
/// design's CSS): ledger bands, tractor-feed holes and the gold seal. Put it over the sheet's fill and under its border.
/// Requires a CanvasRenderer (Unity UI 2.0's Graphic no longer adds one; see BlueprintBox).
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class RunEndDecor : MaskableGraphic
{
    static readonly BlueprintMeshBuilder Shared = new BlueprintMeshBuilder();
    static readonly List<Vector2> P = new List<Vector2>(16);

    [SerializeField] RunEndDecorKind kind = RunEndDecorKind.None;
    [Tooltip("Ledger: band colour. Tractor feed: hole colour.")]
    [SerializeField] Color primary = UIColors.LedgerBand;
    [Tooltip("Tractor feed: dashed tear line colour (clear = none).")]
    [SerializeField] Color secondary = UIColors.LedgerLine;
    [Tooltip("Ledger: row height. Tractor feed: distance between holes.")]
    [SerializeField] float pitch = 34f;
    [Tooltip("Tractor feed: width of each strip (the tear line sits on its inner edge).")]
    [SerializeField] float stripWidth = 26f;
    [Tooltip("Tractor feed: hole radius.")]
    [SerializeField] float holeRadius = 6f;
    [Tooltip("Corner radii of the sheet under it (top-left, top-right, bottom-right, bottom-left), so nothing sticks out.")]
    [SerializeField] Vector4 radii = Vector4.zero;

    public RunEndDecorKind Kind
    {
        get => kind;
        set { if (kind != value) { kind = value; SetVerticesDirty(); } }
    }

    /// <summary>A stretched decor under <paramref name="parent"/> (no raycasts).</summary>
    public static RunEndDecor Create(Transform parent, string name, RunEndDecorKind kind, Color primary, Color secondary,
                                     float pitch, Vector4 radii)
    {
        RectTransform rect = BlueprintUI.Stretch(BlueprintUI.NewRect(name, parent));
        RunEndDecor decor = rect.gameObject.AddComponent<RunEndDecor>();
        decor.kind = kind;
        decor.primary = primary;
        decor.secondary = secondary;
        decor.pitch = pitch;
        decor.radii = radii;
        decor.raycastTarget = false;
        return decor;
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        if (kind == RunEndDecorKind.None || r.width <= 0f || r.height <= 0f) return;

        BlueprintMeshBuilder m = Shared;
        m.Clear();
        m.Feather = BlueprintBox.SoftEdge(canvas);
        m.MiterLimit = 3f;
        m.SetYDownFrame(new Vector2(r.xMin, r.yMax));

        switch (kind)
        {
            case RunEndDecorKind.LedgerBands:
                DrawBands(m, r.width, r.height);
                break;
            case RunEndDecorKind.TractorFeed:
                DrawTractorFeed(m, r.width, r.height);
                break;
            case RunEndDecorKind.Seal:
                float scale = Mathf.Min(r.width / 190f, r.height / 230f);
                m.SetYDownFrame(new Vector2(r.center.x - 95f * scale, r.center.y + 115f * scale), scale);
                DrawSeal(m);
                break;
        }
        vh.AddUIVertexStream(m.Vertices, m.Indices);
    }

    Color32 Tint(Color c)
    {
        return c * color;
    }

    // ------------------------------------------------------------------
    // Ledger bands: rows 1, 3, 5 ... (the first row is the paper colour)
    // ------------------------------------------------------------------

    void DrawBands(BlueprintMeshBuilder m, float w, float h)
    {
        if (pitch < 2f || primary.a <= 0f) return;
        Color32 c = Tint(primary);
        for (int row = 1; row * pitch < h; row += 2)
        {
            float y0 = row * pitch;
            float y1 = Mathf.Min(h, y0 + pitch);
            float left = Mathf.Max(TopInset(y0, radii.x), BottomInset(h - y1, radii.w));
            float right = Mathf.Max(TopInset(y0, radii.y), BottomInset(h - y1, radii.z));
            if (w - left - right > 0f)
                m.FillRect(new Rect(left, y0, w - left - right, y1 - y0), c, 0f);
        }
    }

    /// <summary>How far a rounded corner of radius <paramref name="r"/> cuts in at <paramref name="d"/> px from its edge.</summary>
    static float TopInset(float d, float r)
    {
        if (r <= 0f || d >= r) return 0f;
        float dy = r - d;
        return r - Mathf.Sqrt(Mathf.Max(0f, r * r - dy * dy));
    }

    static float BottomInset(float d, float r)
    {
        return TopInset(d, r);
    }

    // ------------------------------------------------------------------
    // Tractor feed: holes every pitch px, centred in a strip on each side, and the dashed tear lines
    // ------------------------------------------------------------------

    void DrawTractorFeed(BlueprintMeshBuilder m, float w, float h)
    {
        if (pitch < 2f) return;
        if (primary.a > 0f)
        {
            Color32 hole = Tint(primary);
            float cxLeft = stripWidth * 0.5f;
            float cxRight = w - stripWidth * 0.5f;
            for (float y = pitch * 0.5f; y + holeRadius <= h; y += pitch)
            {
                if (InsideRounded(new Vector2(cxLeft, y), w, h)) m.FillCircle(new Vector2(cxLeft, y), holeRadius, hole);
                if (InsideRounded(new Vector2(cxRight, y), w, h)) m.FillCircle(new Vector2(cxRight, y), holeRadius, hole);
            }
        }
        if (secondary.a > 0f)
        {
            Color32 line = Tint(secondary);
            float top = Mathf.Max(radii.x, radii.y) * 0.5f;
            float bottom = h - Mathf.Max(radii.z, radii.w) * 0.5f;
            DashedLine(m, new Vector2(stripWidth + 1f, top), new Vector2(stripWidth + 1f, bottom), line);
            DashedLine(m, new Vector2(w - stripWidth - 1f, top), new Vector2(w - stripWidth - 1f, bottom), line);
        }
    }

    static void DashedLine(BlueprintMeshBuilder m, Vector2 a, Vector2 b, Color32 c)
    {
        P.Clear();
        P.Add(a);
        P.Add(b);
        m.Dashed(P, 2f, c, false, 6f, 5f);
    }

    /// <summary>True if the point is inside the rounded rectangle (0, 0, w, h) with this decor's corner radii.</summary>
    bool InsideRounded(Vector2 p, float w, float h)
    {
        if (p.x < 0f || p.y < 0f || p.x > w || p.y > h) return false;
        return InCorner(p, new Vector2(radii.x, radii.x), radii.x)
            && InCorner(p, new Vector2(w - radii.y, radii.y), radii.y)
            && InCorner(p, new Vector2(w - radii.z, h - radii.z), radii.z)
            && InCorner(p, new Vector2(radii.w, h - radii.w), radii.w);
    }

    /// <summary>False only when the point is in the corner square outside the corner's arc.</summary>
    static bool InCorner(Vector2 p, Vector2 center, float r)
    {
        if (r <= 0f) return true;
        bool left = center.x <= r + 0.01f;
        bool top = center.y <= r + 0.01f;
        bool inSquare = (left ? p.x < center.x : p.x > center.x) && (top ? p.y < center.y : p.y > center.y);
        return !inSquare || (p - center).sqrMagnitude <= r * r;
    }

    // ------------------------------------------------------------------
    // Seal (design units: 190 x 230, y down)
    // ------------------------------------------------------------------

    static readonly Color SealRed = UIColors.Loss;
    static readonly Color SealLight = UIColors.Hex(0xF2CF6B);

    void DrawSeal(BlueprintMeshBuilder m)
    {
        Color32 ink = Tint(UIColors.Ink);
        Color32 red = Tint(SealRed);
        Color32 gold = Tint(UIColors.Gold);
        Color32 goldDark = Tint(UIColors.GoldDark);
        Color32 light = Tint(SealLight);
        Vector2 center = new Vector2(95f, 88f);

        // Ribbon tails (each a notched ribbon: two convex parts, then the outline)
        Ribbon(m, new Vector2(63f, 124f), new Vector2(39f, 212f), new Vector2(63f, 198f), new Vector2(81f, 218f), new Vector2(103f, 132f), red, ink);
        Ribbon(m, new Vector2(127f, 124f), new Vector2(151f, 212f), new Vector2(127f, 198f), new Vector2(109f, 218f), new Vector2(87f, 132f), red, ink);

        // Scalloped rim: 30 round dots on r 80 (ink, then gold inside them)
        const int Dots = 30;
        for (int i = 0; i < Dots; i++)
        {
            float a = i * Mathf.PI * 2f / Dots;
            m.FillCircle(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 80f, 11.5f, ink);
        }
        for (int i = 0; i < Dots; i++)
        {
            float a = i * Mathf.PI * 2f / Dots;
            m.FillCircle(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 80f, 8.5f, gold);
        }

        // Medal: gold disc with an ink ring, a dashed dark-gold ring and a light centre
        m.FillCircle(center, 76f, gold);
        m.Circle(center, 76f, 3f, ink);
        m.Circle(center, 68f, 8f, goldDark, 4f, 4.5f);
        m.FillCircle(center, 58f, light);
        m.Circle(center, 58f, 2.5f, ink);
    }

    static void Ribbon(BlueprintMeshBuilder m, Vector2 top, Vector2 outer, Vector2 notch, Vector2 inner, Vector2 topInner, Color32 fill, Color32 ink)
    {
        P.Clear();
        P.Add(top); P.Add(outer); P.Add(notch); P.Add(topInner);
        m.FillConvex(P, fill);
        P.Clear();
        P.Add(notch); P.Add(inner); P.Add(topInner);
        m.FillConvex(P, fill);
        P.Clear();
        P.Add(top); P.Add(outer); P.Add(notch); P.Add(inner); P.Add(topInner);
        m.Stroke(P, 3f, ink, true);
    }
}
