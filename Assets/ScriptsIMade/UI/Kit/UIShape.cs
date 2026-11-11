using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Anti-aliased vector shapes drawn from vertices (no sprites), for screens with many sizes of rounded boxes, circles,
/// rings and glows (the office CRT mini-games):
///  - RoundRect: CSS border-radius per corner (horizontal / vertical radii, clamped like CSS), Ellipse: fills the rect,
///    Sector: a pie slice of the ellipse (degrees clockwise from 12 o'clock), Stroke: polylines with round caps,
///    Polygon: filled convex polygons (AddPolygon).
///  - border 0 fills the shape, border > 0 draws a ring that wide inside the rect (a CSS border; borderBottom makes
///    the bottom edge thicker, like a keycap).
///  - glow > 0 draws only a soft halo outside the shape (CSS box-shadow 0 0 glow color: it starts at half strength at
///    the edge and fades out over the blur), glow &lt; 0 a soft band inside it (inset box-shadow).
///  - spread grows (or shrinks) the shape first (box-shadow spread).
/// The Graphic colour tints it. Coordinates are this RectTransform's local space.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class UIShape : MaskableGraphic
{
    public enum Kind { RoundRect, Ellipse, Sector, Stroke, Polygon }

    public Kind kind = Kind.RoundRect;
    [Tooltip("Horizontal corner radii: top-left, top-right, bottom-right, bottom-left")]
    public Vector4 radiusX;
    [Tooltip("Vertical corner radii (same order); negative = same as the horizontal ones")]
    public Vector4 radiusY = new Vector4(-1f, -1f, -1f, -1f);
    [Tooltip("0 = filled, > 0 = ring this wide inside the rect")]
    public float border;
    [Tooltip("Ring only: a thicker bottom edge (keycaps, CSS border-bottom-width); negative = same as border")]
    public float borderBottom = -1f;
    [Tooltip("Ring only: dash and gap lengths along the outer edge (0 = solid)")]
    public float dash;
    public float gap;
    [Tooltip("> 0: soft halo outside only; < 0: soft band inside only")]
    public float glow;
    public float spread;
    [Tooltip("Sector: start angle and sweep, degrees clockwise from 12 o'clock")]
    public float startAngle;
    public float sweepAngle = 90f;
    [Tooltip("Stroke width")]
    public float strokeWidth = 4f;
    [Tooltip("Anti-aliasing fringe in UI units")]
    public float feather = 1f;

    [SerializeField] List<Vector2> strokePoints = new List<Vector2>();
    [SerializeField] List<int> strokeBreaks = new List<int>();   // index where each path after the first starts

    static readonly List<Vector2> outer = new List<Vector2>();
    static readonly List<Vector2> inner = new List<Vector2>();

    // ------------------------------------------------------------------
    // Setup helpers

    public UIShape Round(float r) => Round(r, r, r, r);

    public UIShape Round(float tl, float tr, float br, float bl)
    {
        radiusX = new Vector4(tl, tr, br, bl);
        radiusY = new Vector4(-1f, -1f, -1f, -1f);
        kind = Kind.RoundRect;
        SetVerticesDirty();
        return this;
    }

    /// <summary>CSS "a b c d / e f g h" elliptical radii.</summary>
    public UIShape Round(Vector4 horizontal, Vector4 vertical)
    {
        radiusX = horizontal;
        radiusY = vertical;
        kind = Kind.RoundRect;
        SetVerticesDirty();
        return this;
    }

    public void SetSector(float start, float sweep)
    {
        kind = Kind.Sector;
        startAngle = start;
        sweepAngle = sweep;
        SetVerticesDirty();
    }

    public void ClearStrokes()
    {
        strokePoints.Clear();
        strokeBreaks.Clear();
        SetVerticesDirty();
    }

    /// <summary>Adds a polyline (local coordinates, y up).</summary>
    public void AddStroke(params Vector2[] points)
    {
        if (points == null || points.Length < 2) return;
        if (strokePoints.Count > 0) strokeBreaks.Add(strokePoints.Count);
        strokePoints.AddRange(points);
        kind = Kind.Stroke;
        SetVerticesDirty();
    }

    /// <summary>Adds a filled convex polygon (local coordinates, y up, either winding).</summary>
    public void AddPolygon(params Vector2[] points)
    {
        if (points == null || points.Length < 3) return;
        if (strokePoints.Count > 0) strokeBreaks.Add(strokePoints.Count);
        strokePoints.AddRange(points);
        kind = Kind.Polygon;
        SetVerticesDirty();
    }

    // ------------------------------------------------------------------
    // Mesh

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        Color32 c = color;
        if (kind == Kind.Stroke)
        {
            BuildStrokes(vh, c);
            return;
        }
        if (kind == Kind.Polygon)
        {
            BuildPolygons(vh, c);
            return;
        }

        r = new Rect(r.xMin - spread, r.yMin - spread, r.width + 2f * spread, r.height + 2f * spread);
        if (r.width <= 0f || r.height <= 0f) return;

        if (kind == Kind.Sector)
        {
            BuildSector(vh, r, c);
            return;
        }

        Vector4 rx, ry;
        Radii(r, out rx, out ry);
        int steps = CornerSteps(rx, ry);
        Outline(r, rx, ry, steps, outer);

        if (glow > 0f)
        {
            // halo outside: Gaussian-like falloff from half strength at the edge
            Band(vh, outer, 0f, glow * 0.35f, 0.5f, 0.2f, c);
            Band(vh, outer, glow * 0.35f, glow * 0.7f, 0.2f, 0.05f, c);
            Band(vh, outer, glow * 0.7f, glow, 0.05f, 0f, c);
            return;
        }
        if (glow < 0f)
        {
            // inset outlines (radii shrink to 0) rather than offsets along the normals, which fold over at corners
            // tighter than the band
            float g = -glow;
            float[] d = { 0f, g * 0.35f, g * 0.7f, g };
            float[] a = { 0.5f, 0.2f, 0.05f, 0f };
            for (int k = 0; k < 3; k++)
            {
                Inset(r, rx, ry, d[k], steps, outer);
                Inset(r, rx, ry, d[k + 1], steps, inner);
                Color32 ca = c, cb = c;
                ca.a = (byte)Mathf.RoundToInt(c.a * a[k]);
                cb.a = (byte)Mathf.RoundToInt(c.a * a[k + 1]);
                Strip(vh, outer, inner, ca, cb);
            }
            return;
        }

        float bottom = borderBottom >= 0f ? borderBottom : border;
        if (border > 0f && border * 2f < r.width && border + bottom < r.height)
        {
            // y up: the bottom edge is yMin; the inner corners shrink by the width of the edges they join, like CSS
            Rect ir = new Rect(r.xMin + border, r.yMin + bottom, r.width - 2f * border, r.height - border - bottom);
            Vector4 irx = Max0(rx - Vector4.one * border), iry = Max0(ry - new Vector4(border, border, bottom, bottom));
            Outline(ir, irx, iry, steps, inner);
            if (dash > 0f && gap > 0f)
            {
                DashedStrip(vh, outer, inner, c);
                return;
            }
            Strip(vh, outer, inner, c, c);
            Band(vh, outer, 0f, feather, 1f, 0f, c);       // outer fringe
            Band(vh, inner, 0f, -feather, 1f, 0f, c);      // inner fringe
            return;
        }

        // filled
        int s = vh.currentVertCount;
        vh.AddVert(r.center, c, Vector4.zero);
        for (int i = 0; i < outer.Count; i++)
            vh.AddVert(outer[i], c, Vector4.zero);
        for (int i = 0; i < outer.Count; i++)
            vh.AddTriangle(s, s + 1 + i, s + 1 + (i + 1) % outer.Count);
        Band(vh, outer, 0f, feather, 1f, 0f, c);
    }

    void Radii(Rect r, out Vector4 rx, out Vector4 ry)
    {
        if (kind == Kind.Ellipse)
        {
            rx = Vector4.one * (r.width * 0.5f);
            ry = Vector4.one * (r.height * 0.5f);
            return;
        }
        rx = Max0(radiusX + Vector4.one * (spread > 0f ? spread : 0f));
        ry = new Vector4(radiusY.x < 0f ? radiusX.x : radiusY.x, radiusY.y < 0f ? radiusX.y : radiusY.y,
                         radiusY.z < 0f ? radiusX.z : radiusY.z, radiusY.w < 0f ? radiusX.w : radiusY.w);
        ry = Max0(ry + Vector4.one * (spread > 0f ? spread : 0f));
        // CSS: scale all radii down together until adjacent ones fit their side
        float f = 1f;
        f = Fit(f, r.width, rx.x + rx.y);
        f = Fit(f, r.width, rx.w + rx.z);
        f = Fit(f, r.height, ry.x + ry.w);
        f = Fit(f, r.height, ry.y + ry.z);
        rx *= f;
        ry *= f;
    }

    static float Fit(float f, float length, float sum) => sum > 0f ? Mathf.Min(f, length / sum) : f;

    static Vector4 Max0(Vector4 v) => new Vector4(Mathf.Max(0f, v.x), Mathf.Max(0f, v.y), Mathf.Max(0f, v.z), Mathf.Max(0f, v.w));

    static int CornerSteps(Vector4 rx, Vector4 ry)
    {
        float m = Mathf.Max(Mathf.Max(rx.x, rx.y, rx.z, rx.w), Mathf.Max(ry.x, ry.y, ry.z, ry.w));
        return Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(m) * 2.2f), 2, 40);
    }

    /// <summary>Closed outline, clockwise starting at the top-left corner (y up). Same point count for any radii.</summary>
    static void Outline(Rect r, Vector4 rx, Vector4 ry, int steps, List<Vector2> pts)
    {
        pts.Clear();
        // top-left: from (left, top - ry) to (left + rx, top)
        Corner(pts, new Vector2(r.xMin + rx.x, r.yMax - ry.x), rx.x, ry.x, 180f, 90f, steps);
        Corner(pts, new Vector2(r.xMax - rx.y, r.yMax - ry.y), rx.y, ry.y, 90f, 0f, steps);
        Corner(pts, new Vector2(r.xMax - rx.z, r.yMin + ry.z), rx.z, ry.z, 0f, -90f, steps);
        Corner(pts, new Vector2(r.xMin + rx.w, r.yMin + ry.w), rx.w, ry.w, -90f, -180f, steps);
    }

    /// <summary>The outline shrunk by d on every side, its radii shrunk with it (never past the centre).</summary>
    static void Inset(Rect r, Vector4 rx, Vector4 ry, float d, int steps, List<Vector2> pts)
    {
        d = Mathf.Min(d, Mathf.Min(r.width, r.height) * 0.5f - 0.01f);
        var ir = new Rect(r.xMin + d, r.yMin + d, r.width - 2f * d, r.height - 2f * d);
        Outline(ir, Max0(rx - Vector4.one * d), Max0(ry - Vector4.one * d), steps, pts);
    }

    static void Corner(List<Vector2> pts, Vector2 c, float rx, float ry, float a0, float a1, int steps)
    {
        for (int i = 0; i <= steps; i++)
        {
            float a = Mathf.Lerp(a0, a1, i / (float)steps) * Mathf.Deg2Rad;
            pts.Add(new Vector2(c.x + Mathf.Cos(a) * rx, c.y + Mathf.Sin(a) * ry));
        }
    }

    /// <summary>A band along a closed outline, offset d0..d1 along the outward normal, alpha a0..a1 (x colour alpha).</summary>
    static void Band(VertexHelper vh, List<Vector2> pts, float d0, float d1, float a0, float a1, Color32 c)
    {
        int n = pts.Count;
        if (n < 3) return;
        int s = vh.currentVertCount;
        Color32 c0 = c, c1 = c;
        c0.a = (byte)Mathf.RoundToInt(c.a * a0);
        c1.a = (byte)Mathf.RoundToInt(c.a * a1);
        for (int i = 0; i < n; i++)
        {
            Vector2 nrm = Normal(pts, i);
            vh.AddVert(pts[i] + nrm * d0, c0, Vector4.zero);
            vh.AddVert(pts[i] + nrm * d1, c1, Vector4.zero);
        }
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            vh.AddTriangle(s + 2 * i, s + 2 * i + 1, s + 2 * j + 1);
            vh.AddTriangle(s + 2 * i, s + 2 * j + 1, s + 2 * j);
        }
    }

    /// <summary>The ring between two outlines in dashes along the outer one (CSS dashed border, roughly).</summary>
    void DashedStrip(VertexHelper vh, List<Vector2> a, List<Vector2> b, Color32 c)
    {
        int n = Mathf.Min(a.Count, b.Count);
        float period = dash + gap;
        float pos = 0f;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            float len = Vector2.Distance(a[i], a[j]);
            if (len < 1e-4f) continue;
            // split this segment at dash boundaries
            float t0 = 0f;
            while (t0 < 1f)
            {
                float phase = Mathf.Repeat(pos + t0 * len, period);
                bool on = phase < dash;
                float left = on ? dash - phase : period - phase;
                float t1 = Mathf.Min(1f, t0 + left / len);
                if (on)
                {
                    int s = vh.currentVertCount;
                    vh.AddVert(Vector2.Lerp(a[i], a[j], t0), c, Vector4.zero);
                    vh.AddVert(Vector2.Lerp(b[i], b[j], t0), c, Vector4.zero);
                    vh.AddVert(Vector2.Lerp(b[i], b[j], t1), c, Vector4.zero);
                    vh.AddVert(Vector2.Lerp(a[i], a[j], t1), c, Vector4.zero);
                    vh.AddTriangle(s, s + 1, s + 2);
                    vh.AddTriangle(s, s + 2, s + 3);
                }
                t0 = t1 + 1e-5f;
            }
            pos += len;
        }
    }

    static void Strip(VertexHelper vh, List<Vector2> a, List<Vector2> b, Color32 ca, Color32 cb)
    {
        int n = Mathf.Min(a.Count, b.Count);
        int s = vh.currentVertCount;
        for (int i = 0; i < n; i++)
        {
            vh.AddVert(a[i], ca, Vector4.zero);
            vh.AddVert(b[i], cb, Vector4.zero);
        }
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            vh.AddTriangle(s + 2 * i, s + 2 * i + 1, s + 2 * j + 1);
            vh.AddTriangle(s + 2 * i, s + 2 * j + 1, s + 2 * j);
        }
    }

    /// <summary>Outward normal of a clockwise (y up) closed outline at point i.</summary>
    static Vector2 Normal(List<Vector2> pts, int i)
    {
        int n = pts.Count;
        Vector2 prev = pts[(i - 1 + n) % n], next = pts[(i + 1) % n];
        int k = 1;
        while (k < n && (next - prev).sqrMagnitude < 1e-6f)
        {
            k++;
            prev = pts[(i - k + n) % n];
            next = pts[(i + k) % n];
        }
        Vector2 d = (next - prev).normalized;
        return new Vector2(-d.y, d.x);   // clockwise outline (y up): the outside is on the left of the direction
    }

    void BuildSector(VertexHelper vh, Rect r, Color32 c)
    {
        float sweep = Mathf.Clamp(sweepAngle, -360f, 360f);
        if (Mathf.Abs(sweep) < 0.01f) return;
        int steps = Mathf.Max(3, Mathf.CeilToInt(Mathf.Abs(sweep) / 4f));
        Vector2 ctr = r.center;
        float hx = r.width * 0.5f, hy = r.height * 0.5f;
        outer.Clear();
        for (int i = 0; i <= steps; i++)
        {
            float a = (startAngle + sweep * i / steps) * Mathf.Deg2Rad;   // clockwise from up
            outer.Add(ctr + new Vector2(Mathf.Sin(a) * hx, Mathf.Cos(a) * hy));
        }
        int s = vh.currentVertCount;
        vh.AddVert(ctr, c, Vector4.zero);
        foreach (var p in outer) vh.AddVert(p, c, Vector4.zero);
        for (int i = 0; i < steps; i++)
            vh.AddTriangle(s, s + 1 + i, s + 2 + i);
        // fringe along the arc
        Color32 z = c;
        z.a = 0;
        int f = vh.currentVertCount;
        for (int i = 0; i <= steps; i++)
        {
            Vector2 d = (outer[i] - ctr).normalized;
            vh.AddVert(outer[i], c, Vector4.zero);
            vh.AddVert(outer[i] + d * feather, z, Vector4.zero);
        }
        for (int i = 0; i < steps; i++)
        {
            vh.AddTriangle(f + 2 * i, f + 2 * i + 1, f + 2 * i + 3);
            vh.AddTriangle(f + 2 * i, f + 2 * i + 3, f + 2 * i + 2);
        }
    }

    void BuildPolygons(VertexHelper vh, Color32 c)
    {
        int start = 0;
        for (int p = 0; p <= strokeBreaks.Count; p++)
        {
            int end = p < strokeBreaks.Count ? strokeBreaks[p] : strokePoints.Count;
            outer.Clear();
            for (int i = start; i < end; i++) outer.Add(strokePoints[i]);
            start = end;
            if (outer.Count < 3) continue;
            // the fringe needs a clockwise outline (y up): negative signed area
            float area = 0f;
            for (int i = 0; i < outer.Count; i++)
            {
                Vector2 a = outer[i], b = outer[(i + 1) % outer.Count];
                area += a.x * b.y - b.x * a.y;
            }
            if (area > 0f) outer.Reverse();
            Vector2 ctr = Vector2.zero;
            foreach (var v in outer) ctr += v;
            ctr /= outer.Count;
            int s = vh.currentVertCount;
            vh.AddVert(ctr, c, Vector4.zero);
            for (int i = 0; i < outer.Count; i++) vh.AddVert(outer[i], c, Vector4.zero);
            for (int i = 0; i < outer.Count; i++)
                vh.AddTriangle(s, s + 1 + i, s + 1 + (i + 1) % outer.Count);
            Band(vh, outer, 0f, feather, 1f, 0f, c);
        }
    }

    static readonly List<Vector2> path = new List<Vector2>();

    void BuildStrokes(VertexHelper vh, Color32 c)
    {
        float h = strokeWidth * 0.5f;
        Color32 z = c;
        z.a = 0;
        int start = 0;
        for (int p = 0; p <= strokeBreaks.Count; p++)
        {
            int end = p < strokeBreaks.Count ? strokeBreaks[p] : strokePoints.Count;
            path.Clear();
            for (int i = start; i < end; i++)
                if (path.Count == 0 || (strokePoints[i] - path[path.Count - 1]).sqrMagnitude > 1e-4f)
                    path.Add(strokePoints[i]);
            start = end;
            if (path.Count == 1) Cap(vh, path[0], h, c, z);
            else if (path.Count > 1) Polyline(vh, path, h, c, z);
        }
    }

    /// <summary>One strip along the path with mitred joins (a translucent stroke gets no darker overlaps at its joints)
    /// and round caps at its two ends.</summary>
    void Polyline(VertexHelper vh, List<Vector2> pts, float h, Color32 c, Color32 z)
    {
        int n = pts.Count;
        int s = vh.currentVertCount;
        for (int i = 0; i < n; i++)
        {
            Vector2 dPrev = i > 0 ? (pts[i] - pts[i - 1]).normalized : Vector2.zero;
            Vector2 dNext = i < n - 1 ? (pts[i + 1] - pts[i]).normalized : Vector2.zero;
            Vector2 n1 = new Vector2(-dPrev.y, dPrev.x), n2 = new Vector2(-dNext.y, dNext.x);
            Vector2 nrm;
            float scale = 1f;
            if (i == 0) nrm = n2;
            else if (i == n - 1) nrm = n1;
            else
            {
                nrm = n1 + n2;
                if (nrm.sqrMagnitude < 1e-6f) nrm = n1;   // folds straight back
                nrm.Normalize();
                scale = 1f / Mathf.Max(0.3f, Vector2.Dot(nrm, n1));
            }
            vh.AddVert(pts[i] + nrm * ((h + feather) * scale), z, Vector4.zero);
            vh.AddVert(pts[i] + nrm * (h * scale), c, Vector4.zero);
            vh.AddVert(pts[i] - nrm * (h * scale), c, Vector4.zero);
            vh.AddVert(pts[i] - nrm * ((h + feather) * scale), z, Vector4.zero);
        }
        for (int i = 0; i + 1 < n; i++)
        {
            int a = s + 4 * i, b = s + 4 * (i + 1);
            for (int k = 0; k < 3; k++)
            {
                vh.AddTriangle(a + k, a + k + 1, b + k + 1);
                vh.AddTriangle(a + k, b + k + 1, b + k);
            }
        }
        HalfCap(vh, pts[0], (pts[0] - pts[1]).normalized, h, c, z);
        HalfCap(vh, pts[n - 1], (pts[n - 1] - pts[n - 2]).normalized, h, c, z);
    }

    /// <summary>Round cap outside a stroke's end (back points away from the stroke).</summary>
    void HalfCap(VertexHelper vh, Vector2 ctr, Vector2 back, float h, Color32 c, Color32 z)
    {
        int steps = Mathf.Clamp(Mathf.CeilToInt(h * 1.25f), 4, 16);
        Vector2 side = new Vector2(-back.y, back.x);
        int s = vh.currentVertCount;
        vh.AddVert(ctr, c, Vector4.zero);
        for (int i = 0; i <= steps; i++)
        {
            float a = Mathf.PI * (i / (float)steps - 0.5f);
            Vector2 d = back * Mathf.Cos(a) + side * Mathf.Sin(a);
            vh.AddVert(ctr + d * h, c, Vector4.zero);
            vh.AddVert(ctr + d * (h + feather), z, Vector4.zero);
        }
        for (int i = 0; i < steps; i++)
        {
            int p0 = s + 1 + 2 * i, p1 = s + 1 + 2 * (i + 1);
            vh.AddTriangle(s, p0, p1);
            vh.AddTriangle(p0, p0 + 1, p1 + 1);
            vh.AddTriangle(p0, p1 + 1, p1);
        }
    }

    void Cap(VertexHelper vh, Vector2 ctr, float h, Color32 c, Color32 z)
    {
        int steps = Mathf.Clamp(Mathf.CeilToInt(h * 2.5f), 8, 32);
        int s = vh.currentVertCount;
        vh.AddVert(ctr, c, Vector4.zero);
        for (int i = 0; i < steps; i++)
        {
            float a = i * Mathf.PI * 2f / steps;
            var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            vh.AddVert(ctr + d * h, c, Vector4.zero);
            vh.AddVert(ctr + d * (h + feather), z, Vector4.zero);
        }
        for (int i = 0; i < steps; i++)
        {
            int j = (i + 1) % steps;
            vh.AddTriangle(s, s + 1 + 2 * i, s + 1 + 2 * j);
            vh.AddTriangle(s + 1 + 2 * i, s + 2 + 2 * i, s + 2 + 2 * j);
            vh.AddTriangle(s + 1 + 2 * i, s + 2 + 2 * j, s + 1 + 2 * j);
        }
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        SetVerticesDirty();
    }
#endif
}
