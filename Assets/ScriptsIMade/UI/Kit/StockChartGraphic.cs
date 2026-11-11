using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The stock market's price chart (board "Stock market"), drawn in one mesh in a 548 x 284 design box scaled to the
/// rect: dashed grid lines, hatching under the price line, the dashed start line, the axes and their ticks, the price
/// line (3.5 px, round joins) and the end dot (green when the price is up on the window, red when down).
/// The Graphic colour is the ink. Prices map to y through the grid: the top grid line (y 60.8) is <see cref="top"/>,
/// each of the 3 lower ones (65 apart) <see cref="step"/> less. Everything is anti-aliased with a 1 unit fringe.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class StockChartGraphic : MaskableGraphic
{
    public const float DesignWidth = 548f, DesignHeight = 284f;
    public const float PlotLeft = 64f, PlotRight = 532f, PlotTop = 8f, PlotBottom = 272f;
    public const float GridTop = 60.8f, GridSpacing = 65f;
    public static readonly float[] TickXs = { 64f, 144.7f, 225.4f, 306.1f, 386.8f, 467.4f, 532f };

    [SerializeField] List<float> prices = new List<float>();
    [Tooltip("Price at the top grid line")]
    public float top = 100f;
    [Tooltip("Price between two grid lines")]
    public float step = 10f;
    public Color upColor = new Color32(0x1E, 0x7B, 0x45, 0xFF);
    public Color downColor = new Color32(0xC0, 0x35, 0x2B, 0xFF);
    [Tooltip("Opacity (0-1) of the grid, the hatching and the start line, as blended by this project (linear space)")]
    public float gridAlpha = 0.5f, hatchAlpha = 0.22f, startAlpha = 0.75f;
    public float feather = 1f;

    static readonly List<Vector2> points = new List<Vector2>();
    Rect box;
    float sx = 1f, sy = 1f;

    public IReadOnlyList<float> Prices => prices;

    /// <summary>Show these prices (oldest first) on a grid whose top line is <paramref name="gridTop"/>.</summary>
    public void SetPrices(IList<float> values, float gridTop, float gridStep)
    {
        prices.Clear();
        if (values != null)
            for (int i = 0; i < values.Count; i++)
                prices.Add(values[i]);
        top = gridTop;
        step = gridStep > 0f ? gridStep : 1f;
        SetVerticesDirty();
    }

    /// <summary>Design y (0 = top of the 284 box, down) of a price.</summary>
    public float PriceToY(float price) => GridTop + (top - price) * GridSpacing / Mathf.Max(1e-6f, step);

    /// <summary>Design x of the i-th of n prices.</summary>
    public static float IndexToX(int i, int n) => n <= 1 ? PlotRight : PlotLeft + i * (PlotRight - PlotLeft) / (n - 1);

    /// <summary>
    /// Grid for these prices: the smallest "nice" step (1, 2, 2.5, 5 x 10^k, at least 0.4% of the price) whose 4 labelled
    /// lines, the top one a multiple of the step, keep every price inside the plot (6 units of room at the top and
    /// bottom); among the top lines that fit, the one that centres the prices.
    /// </summary>
    public static void NiceGrid(IList<float> values, out float gridTop, out float gridStep)
    {
        float lo = float.MaxValue, hi = float.MinValue;
        if (values != null)
            foreach (float v in values) { lo = Mathf.Min(lo, v); hi = Mathf.Max(hi, v); }
        if (lo > hi) lo = hi = 1f;
        float minStep = Mathf.Max(0.01f, (lo + hi) * 0.5f * 0.004f);
        // steps of room above the top line and below the lowest line
        float above = (GridTop - PlotTop - 6f) / GridSpacing;
        float below = (PlotBottom - (GridTop + 3f * GridSpacing) - 6f) / GridSpacing;
        float[] mult = { 1f, 2f, 2.5f, 5f };
        for (int e = -2; e <= 12; e++)
        {
            float pow = Mathf.Pow(10f, e);
            foreach (float m in mult)
            {
                float s = m * pow;
                if (s < minStep) continue;
                float tMin = Mathf.Ceil((hi - above * s) / s - 1e-4f) * s;
                float tMax = Mathf.Floor((lo + (3f + below) * s) / s + 1e-4f) * s;
                if (tMin > tMax) continue;
                float centre = ((hi - above * s) + (lo + (3f + below) * s)) * 0.5f;
                gridTop = Mathf.Clamp(Mathf.Round(centre / s) * s, tMin, tMax);
                gridStep = s;
                return;
            }
        }
        gridStep = Mathf.Max(minStep, hi - lo);
        gridTop = hi;
    }

    Vector2 L(float x, float y) => new Vector2(box.xMin + x * sx, box.yMax - y * sy);

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        box = rectTransform.rect;
        sx = box.width / DesignWidth;
        sy = box.height / DesignHeight;
        float s = Mathf.Min(sx, sy);
        Color ink = color;

        // dashed grid lines (2 on, 6 off, 1 wide)
        Color grid = WithAlpha(ink, ink.a * gridAlpha);
        for (int i = 0; i < 4; i++)
        {
            float y = GridTop + i * GridSpacing;
            Dashed(vh, PlotLeft, PlotRight, y, 1f * s, 3f, 5f, grid);
        }

        points.Clear();
        int n = prices.Count;
        for (int i = 0; i < n; i++)
            points.Add(new Vector2(IndexToX(i, n), Mathf.Clamp(PriceToY(prices[i]), PlotTop, PlotBottom)));

        // hatching under the price line: 45 degree lines, 11 apart
        if (n >= 2)
            Hatch(vh, WithAlpha(ink, ink.a * hatchAlpha), 1.5f * s);

        // dashed start line (10 on, 7 off, 2 wide)
        if (n >= 1)
            Dashed(vh, PlotLeft, PlotRight, points[0].y, 2f * s, 10f, 7f, WithAlpha(ink, ink.a * startAlpha));

        // axes (2.5) and ticks (2)
        float a = 2.5f * s;
        Segment(vh, L(63f, 8f), L(63f, 272.5f), a, ink);
        Segment(vh, L(63f, 272.5f), L(536f, 272.5f), a, ink);
        Disc(vh, L(63f, 272.5f), a * 0.5f, ink);
        Disc(vh, L(63f, 8f), a * 0.5f, ink);
        Disc(vh, L(536f, 272.5f), a * 0.5f, ink);
        float t = 2f * s;
        foreach (float x in TickXs)
            Segment(vh, L(x, 272f), L(x, 280f), t, ink);
        for (int i = 0; i < 4; i++)
        {
            float y = GridTop + i * GridSpacing;
            Segment(vh, L(57f, y), L(63f, y), t, ink);
        }

        // the price line: 3.5 wide, round joins and caps
        float w = 3.5f * s;
        for (int i = 0; i + 1 < n; i++)
            Segment(vh, L(points[i].x, points[i].y), L(points[i + 1].x, points[i + 1].y), w, ink);
        for (int i = 0; i < n; i++)
            Disc(vh, L(points[i].x, points[i].y), w * 0.5f, ink);

        // end dot: r 7 filled green/red, 2.5 ink ring on it
        if (n >= 1)
        {
            bool up = prices[n - 1] >= prices[0];
            Vector2 end = L(points[n - 1].x, points[n - 1].y);
            Disc(vh, end, 7f * s, up ? upColor : downColor);
            Ring(vh, end, 7f * s - 1.25f * s, 7f * s + 1.25f * s, ink);
        }
    }

    // ------------------------------------------------------------------
    // Hatching: for each line x + y = c (design units, y down), the parts that lie under the price line and above the
    // x axis, found per price segment (c - x - P(x) is linear on each).

    void Hatch(VertexHelper vh, Color c, float width)
    {
        float spacing = 11f * Mathf.Sqrt(2f);
        float minY = PlotBottom;
        foreach (var p in points) minY = Mathf.Min(minY, p.y);
        float cMin = PlotLeft + minY, cMax = PlotRight + PlotBottom;
        for (float k = Mathf.Ceil(cMin / spacing); k * spacing <= cMax; k++)
        {
            float cc = k * spacing;
            float xa = Mathf.Max(PlotLeft, cc - PlotBottom), xb = Mathf.Min(PlotRight, cc - minY);
            if (xb - xa < 0.01f) continue;
            float runStart = float.NaN, runEnd = float.NaN;
            for (int i = 0; i + 1 < points.Count; i++)
            {
                float x0 = points[i].x, x1 = points[i + 1].x;
                float lo = Mathf.Max(x0, xa), hi = Mathf.Min(x1, xb);
                if (hi <= lo) continue;
                float g0 = G(cc, lo, i), g1 = G(cc, hi, i);
                float inLo = float.NaN, inHi = float.NaN;
                if (g0 >= 0f && g1 >= 0f) { inLo = lo; inHi = hi; }
                else if (g0 >= 0f) { inLo = lo; inHi = lo + (hi - lo) * g0 / (g0 - g1); }
                else if (g1 >= 0f) { inLo = lo + (hi - lo) * g0 / (g0 - g1); inHi = hi; }
                if (float.IsNaN(inLo))
                    continue;
                if (!float.IsNaN(runEnd) && Mathf.Abs(inLo - runEnd) < 0.01f)
                    runEnd = inHi;
                else
                {
                    EmitHatch(vh, cc, runStart, runEnd, width, c);
                    runStart = inLo;
                    runEnd = inHi;
                }
            }
            EmitHatch(vh, cc, runStart, runEnd, width, c);
        }
    }

    /// <summary>c - x - P(x) on price segment i: positive under the price line.</summary>
    float G(float cc, float x, int i)
    {
        Vector2 a = points[i], b = points[i + 1];
        float t = b.x - a.x > 1e-4f ? (x - a.x) / (b.x - a.x) : 0f;
        float py = Mathf.Lerp(a.y, b.y, t);
        return cc - x - py;
    }

    void EmitHatch(VertexHelper vh, float cc, float x0, float x1, float width, Color c)
    {
        if (float.IsNaN(x0) || x1 - x0 < 0.2f) return;
        Segment(vh, L(x0, cc - x0), L(x1, cc - x1), width, c);
    }

    // ------------------------------------------------------------------
    // Primitives (local space)

    void Dashed(VertexHelper vh, float x0, float x1, float y, float width, float dash, float gap, Color c)
    {
        for (float x = x0; x < x1; x += dash + gap)
            Segment(vh, L(x, y), L(Mathf.Min(x + dash, x1), y), width, c);
    }

    /// <summary>A butt-ended bar from a to b, <paramref name="width"/> wide, with an anti-aliased fringe along its sides.</summary>
    void Segment(VertexHelper vh, Vector2 a, Vector2 b, float width, Color c)
    {
        Vector2 d = b - a;
        float len = d.magnitude;
        if (len < 1e-4f) return;
        d /= len;
        Vector2 nrm = new Vector2(-d.y, d.x);
        float h = Mathf.Max(0.01f, width * 0.5f - feather * 0.5f);
        float o = h + feather;
        Color32 on = c, off = WithAlpha(c, 0f);
        int s = vh.currentVertCount;
        vh.AddVert(a + nrm * o, off, Vector4.zero);
        vh.AddVert(a + nrm * h, on, Vector4.zero);
        vh.AddVert(a - nrm * h, on, Vector4.zero);
        vh.AddVert(a - nrm * o, off, Vector4.zero);
        vh.AddVert(b + nrm * o, off, Vector4.zero);
        vh.AddVert(b + nrm * h, on, Vector4.zero);
        vh.AddVert(b - nrm * h, on, Vector4.zero);
        vh.AddVert(b - nrm * o, off, Vector4.zero);
        for (int k = 0; k < 3; k++)
        {
            vh.AddTriangle(s + k, s + k + 1, s + 4 + k + 1);
            vh.AddTriangle(s + k, s + 4 + k + 1, s + 4 + k);
        }
    }

    void Disc(VertexHelper vh, Vector2 ctr, float r, Color c)
    {
        int steps = Mathf.Clamp(Mathf.CeilToInt(r * 3f), 10, 40);
        float inner = Mathf.Max(0.01f, r - feather * 0.5f), outer = inner + feather;
        Color32 on = c, off = WithAlpha(c, 0f);
        int s = vh.currentVertCount;
        vh.AddVert(ctr, on, Vector4.zero);
        for (int i = 0; i <= steps; i++)
        {
            float ang = i * Mathf.PI * 2f / steps;
            var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            vh.AddVert(ctr + dir * inner, on, Vector4.zero);
            vh.AddVert(ctr + dir * outer, off, Vector4.zero);
        }
        for (int i = 0; i < steps; i++)
        {
            int p0 = s + 1 + 2 * i, p1 = p0 + 2;
            vh.AddTriangle(s, p0, p1);
            vh.AddTriangle(p0, p0 + 1, p1 + 1);
            vh.AddTriangle(p0, p1 + 1, p1);
        }
    }

    void Ring(VertexHelper vh, Vector2 ctr, float rIn, float rOut, Color c)
    {
        int steps = Mathf.Clamp(Mathf.CeilToInt(rOut * 3f), 16, 48);
        float f = feather * 0.5f;
        float[] radii = { rIn - f, rIn + f, rOut - f, rOut + f };
        Color32 on = c, off = WithAlpha(c, 0f);
        int s = vh.currentVertCount;
        for (int i = 0; i <= steps; i++)
        {
            float ang = i * Mathf.PI * 2f / steps;
            var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            vh.AddVert(ctr + dir * Mathf.Max(0f, radii[0]), off, Vector4.zero);
            vh.AddVert(ctr + dir * radii[1], on, Vector4.zero);
            vh.AddVert(ctr + dir * radii[2], on, Vector4.zero);
            vh.AddVert(ctr + dir * radii[3], off, Vector4.zero);
        }
        for (int i = 0; i < steps; i++)
        {
            int a = s + 4 * i, b = a + 4;
            for (int k = 0; k < 3; k++)
            {
                vh.AddTriangle(a + k, a + k + 1, b + k + 1);
                vh.AddTriangle(a + k, b + k + 1, b + k);
            }
        }
    }

    static Color WithAlpha(Color c, float a)
    {
        c.a = a;
        return c;
    }
}
