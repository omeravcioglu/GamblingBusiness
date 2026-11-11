using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds UI mesh geometry in the blueprint style: strokes with mitered corners, dashes, arcs, rounded rectangles,
/// discs and radial fills, each with a soft edge about one pixel wide. Points go through a frame (an affine transform),
/// so a drawing can be written in plan pixels with y pointing down, or in a piece's own frame that turns with it.
/// Rectangle corner radii are (top-left, top-right, bottom-right, bottom-left) in a y-down frame.
/// </summary>
public sealed class BlueprintMeshBuilder
{
    /// <summary>A UI mesh can't hold more (16-bit indices); anything beyond is skipped with a warning.</summary>
    public const int MaxVertices = 60000;

    public readonly List<UIVertex> Vertices = new List<UIVertex>(1024);
    public readonly List<int> Indices = new List<int>(2048);

    /// <summary>Width of the soft edge in output units (about one screen pixel). 0 = hard edges.</summary>
    public float Feather = 1f;
    /// <summary>Longest miter at sharp corners, in half stroke widths.</summary>
    public float MiterLimit = 3f;

    struct Frame
    {
        public Vector2 origin, axisX, axisY;
    }

    static readonly Frame Identity = new Frame { origin = Vector2.zero, axisX = Vector2.right, axisY = Vector2.up };

    Frame frame = Identity;
    readonly Stack<Frame> frames = new Stack<Frame>();
    readonly List<Vector2> linePath = new List<Vector2>(2);
    readonly List<Vector2> shapePath = new List<Vector2>(256);
    readonly List<Vector2> dashPath = new List<Vector2>(64);
    readonly List<Vector2> mapped = new List<Vector2>(256);
    bool warnedFull;

    /// <summary>Output units per frame unit.</summary>
    public float Scale => frame.axisX.magnitude;

    /// <summary>Empty the mesh and reset the frame.</summary>
    public void Clear()
    {
        Vertices.Clear();
        Indices.Clear();
        frames.Clear();
        frame = Identity;
        warnedFull = false;
    }

    // ------------------------------------------------------------------
    // Frames
    // ------------------------------------------------------------------

    public void SetFrame(Vector2 origin, Vector2 axisX, Vector2 axisY)
    {
        frame = new Frame { origin = origin, axisX = axisX, axisY = axisY };
    }

    /// <summary>Frame units = output units, (0, 0) at <paramref name="topLeft"/>, y pointing down.</summary>
    public void SetYDownFrame(Vector2 topLeft, float scale = 1f)
    {
        frame = new Frame { origin = topLeft, axisX = new Vector2(scale, 0f), axisY = new Vector2(0f, -scale) };
    }

    public void PushFrame()
    {
        frames.Push(frame);
    }

    public void PopFrame()
    {
        if (frames.Count > 0) frame = frames.Pop();
    }

    /// <summary>
    /// Enter a piece's own frame: (0, 0) is its top-left corner before rotation and <paramref name="canonicalSize"/> its
    /// size before rotation. When <paramref name="rotated"/>, the drawing turns 90° clockwise inside its footprint.
    /// Call PopFrame when done.
    /// </summary>
    public void PushPieceFrame(Vector2 topLeft, Vector2 canonicalSize, bool rotated)
    {
        frames.Push(frame);
        if (!rotated)
        {
            frame = new Frame { origin = Map(topLeft), axisX = frame.axisX, axisY = frame.axisY };
            return;
        }
        // Clockwise (y down): local (u, v) -> topLeft + (height - v, u)
        Vector2 origin = Map(topLeft + new Vector2(canonicalSize.y, 0f));
        Vector2 ax = MapDir(new Vector2(0f, 1f));
        Vector2 ay = MapDir(new Vector2(-1f, 0f));
        frame = new Frame { origin = origin, axisX = ax, axisY = ay };
    }

    public Vector2 Map(Vector2 p)
    {
        return frame.origin + frame.axisX * p.x + frame.axisY * p.y;
    }

    public Vector2 MapDir(Vector2 d)
    {
        return frame.axisX * d.x + frame.axisY * d.y;
    }

    // ------------------------------------------------------------------
    // Strokes
    // ------------------------------------------------------------------

    public void Line(Vector2 a, Vector2 b, float width, Color32 color)
    {
        linePath.Clear();
        linePath.Add(a);
        linePath.Add(b);
        Stroke(linePath, width, color, false);
    }

    /// <summary>Stroke a polyline (frame units) with mitered corners and flat ends.</summary>
    public void Stroke(List<Vector2> path, float width, Color32 color, bool closed)
    {
        MapPath(path, closed);
        if (mapped.Count < 2) return;
        StrokeMapped(width * Scale, color, closed);
    }

    /// <summary>Dashed polyline. Dash and gap lengths are frame units.</summary>
    public void Dashed(List<Vector2> path, float width, Color32 color, bool closed, float dash, float gap, float phase = 0f)
    {
        int n = path.Count;
        if (n < 2) return;
        if (dash <= 0f || gap <= 0f)
        {
            Stroke(path, width, color, closed);
            return;
        }

        float period = dash + gap;
        float t = Mathf.Repeat(phase, period);
        bool inDash = t < dash;
        dashPath.Clear();
        if (inDash) dashPath.Add(path[0]);

        int segments = closed ? n : n - 1;
        for (int s = 0; s < segments; s++)
        {
            Vector2 a = path[s];
            Vector2 b = path[(s + 1) % n];
            float length = Vector2.Distance(a, b);
            if (length < 1e-5f) continue;
            Vector2 dir = (b - a) / length;
            float d = 0f;
            while (d < length - 1e-5f)
            {
                if (inDash)
                {
                    float step = Mathf.Min(dash - t, length - d);
                    d += step;
                    t += step;
                    dashPath.Add(a + dir * d);
                    if (t >= dash - 1e-4f)
                    {
                        if (dashPath.Count >= 2) Stroke(dashPath, width, color, false);
                        dashPath.Clear();
                        inDash = false;
                    }
                }
                else
                {
                    float step = Mathf.Min(period - t, length - d);
                    d += step;
                    t += step;
                    if (t >= period - 1e-4f)
                    {
                        t = 0f;
                        inDash = true;
                        dashPath.Clear();
                        dashPath.Add(a + dir * d);
                    }
                }
            }
        }
        if (inDash && dashPath.Count >= 2) Stroke(dashPath, width, color, false);
        dashPath.Clear();
    }

    /// <summary>Arc around <paramref name="center"/>. Angles in degrees; in a y-down frame they turn clockwise.</summary>
    public void Arc(Vector2 center, float radius, float fromDegrees, float toDegrees, float width, Color32 color,
                    float dash = 0f, float gap = 0f)
    {
        BuildArc(shapePath, center, radius, fromDegrees, toDegrees);
        if (dash > 0f) Dashed(shapePath, width, color, false, dash, gap);
        else Stroke(shapePath, width, color, false);
    }

    public void Circle(Vector2 center, float radius, float width, Color32 color, float dash = 0f, float gap = 0f)
    {
        BuildArc(shapePath, center, radius, 0f, 360f);
        shapePath.RemoveAt(shapePath.Count - 1);
        if (dash > 0f) Dashed(shapePath, width, color, true, dash, gap);
        else Stroke(shapePath, width, color, true);
    }

    public void StrokeRect(Rect r, float width, Color32 color, float dash = 0f, float gap = 0f)
    {
        StrokeRoundRect(r, Vector4.zero, width, color, dash, gap);
    }

    public void StrokeRoundRect(Rect r, Vector4 radii, float width, Color32 color, float dash = 0f, float gap = 0f)
    {
        RoundRectPath(shapePath, r, radii);
        if (dash > 0f) Dashed(shapePath, width, color, true, dash, gap);
        else Stroke(shapePath, width, color, true);
    }

    public void StrokeRoundRect(Rect r, float radius, float width, Color32 color)
    {
        StrokeRoundRect(r, new Vector4(radius, radius, radius, radius), width, color);
    }

    // ------------------------------------------------------------------
    // Fills
    // ------------------------------------------------------------------

    public void FillRect(Rect r, Color32 color, float feather = -1f)
    {
        FillRoundRect(r, Vector4.zero, color, feather);
    }

    public void FillRoundRect(Rect r, Vector4 radii, Color32 color, float feather = -1f)
    {
        if (r.width <= 0f || r.height <= 0f) return;
        RoundRectPath(shapePath, r, radii);
        FillConvex(shapePath, color, feather);
    }

    public void FillRoundRect(Rect r, float radius, Color32 color)
    {
        FillRoundRect(r, new Vector4(radius, radius, radius, radius), color);
    }

    public void FillCircle(Vector2 center, float radius, Color32 color, float feather = -1f)
    {
        if (radius <= 0f) return;
        BuildArc(shapePath, center, radius, 0f, 360f);
        shapePath.RemoveAt(shapePath.Count - 1);
        FillConvex(shapePath, color, feather);
    }

    /// <summary>Fill a convex polygon (frame units). <paramref name="feather"/> &lt; 0 uses Feather; a wide one makes a soft shadow.</summary>
    public void FillConvex(List<Vector2> path, Color32 color, float feather = -1f)
    {
        FillMapped(path, color, color, false, Vector2.zero, feather);
    }

    /// <summary>Convex fill whose colour goes from <paramref name="inner"/> at <paramref name="center"/> to <paramref name="outer"/> at the edge.</summary>
    public void FillRadial(List<Vector2> path, Vector2 center, Color32 inner, Color32 outer, float feather = -1f)
    {
        FillMapped(path, inner, outer, true, center, feather);
    }

    public void FillRoundRectRadial(Rect r, Vector4 radii, Vector2 center, Color32 inner, Color32 outer, float feather = -1f)
    {
        if (r.width <= 0f || r.height <= 0f) return;
        RoundRectPath(shapePath, r, radii);
        FillMapped(shapePath, inner, outer, true, center, feather);
    }

    /// <summary>
    /// Fill a simple polygon (frame units), convex or not, in either winding, with a soft edge. Convex ones go through
    /// FillConvex; the others are cut into triangles by ear clipping (the HQ tower's figures and furniture outlines).
    /// </summary>
    public void FillPolygon(List<Vector2> path, Color32 color, float feather = -1f)
    {
        MapPath(path, true);
        RemoveCollinear();
        int n = mapped.Count;
        if (n < 3) return;
        float area = 0f;
        for (int i = 0; i < n; i++)
        {
            Vector2 a = mapped[i], b = mapped[(i + 1) % n];
            area += a.x * b.y - b.x * a.y;
        }
        if (Mathf.Abs(area) < 1e-6f) return;
        bool ccw = area > 0f;
        if (IsConvex(ccw))
        {
            FillConvex(path, color, feather);
            return;
        }
        if (!HasRoom(n * 2)) return;

        int first = Vertices.Count;
        for (int i = 0; i < n; i++) AddVertex(mapped[i], color);

        earIndices.Clear();
        for (int i = 0; i < n; i++) earIndices.Add(i);
        int guard = n * n + 8;
        while (earIndices.Count > 3 && guard-- > 0)
        {
            int count = earIndices.Count;
            int ear = -1;
            for (int k = 0; k < count && ear < 0; k++)
            {
                int ia = earIndices[(k + count - 1) % count], ib = earIndices[k], ic = earIndices[(k + 1) % count];
                Vector2 a = mapped[ia], b = mapped[ib], c = mapped[ic];
                float cross = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
                if (ccw ? cross <= 0f : cross >= 0f) continue;   // reflex corner
                bool blocked = false;
                for (int j = 0; j < count && !blocked; j++)
                {
                    int iv = earIndices[j];
                    if (iv != ia && iv != ib && iv != ic && InTriangle(mapped[iv], a, b, c)) blocked = true;
                }
                if (!blocked) ear = k;
            }
            if (ear < 0) ear = 0;   // not a simple polygon: keep cutting rather than leave a hole
            int pa = earIndices[(ear + count - 1) % count], pb = earIndices[ear], pc = earIndices[(ear + 1) % count];
            Triangle(first + pa, first + pb, first + pc);
            earIndices.RemoveAt(ear);
        }
        if (earIndices.Count == 3) Triangle(first + earIndices[0], first + earIndices[1], first + earIndices[2]);

        float f = feather < 0f ? Feather : feather;
        if (f <= 0f) return;
        float outward = ccw ? -1f : 1f;
        Color32 clear = color;
        clear.a = 0;
        int ring = Vertices.Count;
        for (int i = 0; i < n; i++) AddVertex(mapped[i] + MiterAt(mapped, i, true) * (outward * f), clear);
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            Quad(first + i, first + j, ring + j, ring + i);
        }
    }

    readonly List<int> earIndices = new List<int>(64);

    /// <summary>Drop points of the mapped outline that lie (within a hundredth of a unit) on the line through their neighbours.</summary>
    void RemoveCollinear()
    {
        for (int i = mapped.Count - 1; i >= 0 && mapped.Count > 3; i--)
        {
            int n = mapped.Count;
            Vector2 a = mapped[(i + n - 1) % n], b = mapped[i], c = mapped[(i + 1) % n];
            float cross = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
            float span = (c - a).magnitude;
            if (Mathf.Abs(cross) <= 0.01f * Mathf.Max(span, 1e-4f)) mapped.RemoveAt(i);
        }
    }

    bool IsConvex(bool ccw)
    {
        int n = mapped.Count;
        for (int i = 0; i < n; i++)
        {
            Vector2 a = mapped[i], b = mapped[(i + 1) % n], c = mapped[(i + 2) % n];
            float cross = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
            if (ccw ? cross < 0f : cross > 0f) return false;
        }
        return true;
    }

    static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
        float d2 = (c.x - b.x) * (p.y - b.y) - (c.y - b.y) * (p.x - b.x);
        float d3 = (a.x - c.x) * (p.y - c.y) - (a.y - c.y) * (p.x - c.x);
        return (d1 > 0f && d2 > 0f && d3 > 0f) || (d1 < 0f && d2 < 0f && d3 < 0f);
    }

    // ------------------------------------------------------------------
    // Paths
    // ------------------------------------------------------------------

    /// <summary>Points of a rounded rectangle, clockwise in a y-down frame.</summary>
    public void RoundRectPath(List<Vector2> into, Rect r, Vector4 radii)
    {
        into.Clear();
        float max = Mathf.Max(0f, Mathf.Min(r.width, r.height) * 0.5f);
        float tl = Mathf.Clamp(radii.x, 0f, max);
        float tr = Mathf.Clamp(radii.y, 0f, max);
        float br = Mathf.Clamp(radii.z, 0f, max);
        float bl = Mathf.Clamp(radii.w, 0f, max);
        Corner(into, new Vector2(r.xMax - tr, r.yMin + tr), tr, -90f, 0f);
        Corner(into, new Vector2(r.xMax - br, r.yMax - br), br, 0f, 90f);
        Corner(into, new Vector2(r.xMin + bl, r.yMax - bl), bl, 90f, 180f);
        Corner(into, new Vector2(r.xMin + tl, r.yMin + tl), tl, 180f, 270f);
    }

    void Corner(List<Vector2> into, Vector2 center, float radius, float from, float to)
    {
        if (radius <= 0.01f)
        {
            into.Add(center);
            return;
        }
        int segments = Mathf.Clamp(Mathf.CeilToInt(radius * Scale / 2.5f), 2, 12);
        for (int i = 0; i <= segments; i++)
        {
            float a = Mathf.Lerp(from, to, i / (float)segments) * Mathf.Deg2Rad;
            into.Add(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
        }
    }

    /// <summary>Points along an arc, both ends included.</summary>
    public void BuildArc(List<Vector2> into, Vector2 center, float radius, float fromDegrees, float toDegrees)
    {
        into.Clear();
        float sweep = (toDegrees - fromDegrees) * Mathf.Deg2Rad;
        int segments = Mathf.Clamp(Mathf.CeilToInt(Mathf.Abs(sweep) * radius * Scale / 3f), 3, 96);
        float start = fromDegrees * Mathf.Deg2Rad;
        for (int i = 0; i <= segments; i++)
        {
            float a = start + sweep * i / segments;
            into.Add(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
        }
    }

    /// <summary>
    /// Append an SVG-style circular arc (rx = ry, no rotation) from the path's last point to <paramref name="to"/>.
    /// Angles follow the frame, so in a y-down frame sweep = true turns clockwise like in SVG.
    /// </summary>
    public static void ArcTo(List<Vector2> path, float radius, bool largeArc, bool sweep, Vector2 to, int segments = 10)
    {
        if (path.Count == 0)
        {
            path.Add(to);
            return;
        }
        Vector2 from = path[path.Count - 1];
        Vector2 half = (from - to) * 0.5f;
        float d2 = half.sqrMagnitude;
        if (d2 < 1e-8f || radius <= 0f)
        {
            path.Add(to);
            return;
        }
        float r = Mathf.Max(radius, Mathf.Sqrt(d2));
        float factor = Mathf.Sqrt(Mathf.Max(0f, (r * r - d2) / d2));
        if (largeArc == sweep) factor = -factor;
        Vector2 centerOffset = factor * new Vector2(half.y, -half.x);
        Vector2 center = centerOffset + (from + to) * 0.5f;

        float a0 = Mathf.Atan2((half.y - centerOffset.y) / r, (half.x - centerOffset.x) / r);
        float a1 = Mathf.Atan2((-half.y - centerOffset.y) / r, (-half.x - centerOffset.x) / r);
        float delta = a1 - a0;
        if (sweep && delta < 0f) delta += 2f * Mathf.PI;
        else if (!sweep && delta > 0f) delta -= 2f * Mathf.PI;

        for (int i = 1; i <= segments; i++)
        {
            float a = a0 + delta * i / segments;
            path.Add(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
        }
        path[path.Count - 1] = to;
    }

    /// <summary>Append a cubic Bézier from the path's last point.</summary>
    public static void CubicTo(List<Vector2> path, Vector2 c1, Vector2 c2, Vector2 to, int segments = 8)
    {
        if (path.Count == 0)
        {
            path.Add(to);
            return;
        }
        Vector2 p0 = path[path.Count - 1];
        for (int i = 1; i <= segments; i++)
        {
            float t = i / (float)segments;
            float u = 1f - t;
            path.Add(u * u * u * p0 + 3f * u * u * t * c1 + 3f * u * t * t * c2 + t * t * t * to);
        }
    }

    // ------------------------------------------------------------------
    // Mesh
    // ------------------------------------------------------------------

    void MapPath(List<Vector2> path, bool closed)
    {
        mapped.Clear();
        for (int i = 0; i < path.Count; i++)
        {
            Vector2 p = Map(path[i]);
            if (mapped.Count > 0 && (p - mapped[mapped.Count - 1]).sqrMagnitude < 1e-6f) continue;
            mapped.Add(p);
        }
        if (closed && mapped.Count > 2 && (mapped[0] - mapped[mapped.Count - 1]).sqrMagnitude < 1e-6f)
            mapped.RemoveAt(mapped.Count - 1);
    }

    void StrokeMapped(float width, Color32 color, bool closed)
    {
        int n = mapped.Count;
        if (closed && n < 3) closed = false;
        bool soft = Feather > 0f;
        int stride = soft ? 4 : 2;
        if (!HasRoom(n * stride)) return;

        float half = width * 0.5f;
        float core = soft ? Mathf.Max(0f, half - Feather * 0.5f) : half;
        float outer = half + (soft ? Feather * 0.5f : 0f);
        Color32 clear = color;
        clear.a = 0;

        int first = Vertices.Count;
        for (int i = 0; i < n; i++)
        {
            Vector2 p = mapped[i];
            Vector2 m = MiterAt(mapped, i, closed);
            if (soft)
            {
                AddVertex(p + m * outer, clear);
                AddVertex(p + m * core, color);
                AddVertex(p - m * core, color);
                AddVertex(p - m * outer, clear);
            }
            else
            {
                AddVertex(p + m * half, color);
                AddVertex(p - m * half, color);
            }
        }

        int segments = closed ? n : n - 1;
        for (int s = 0; s < segments; s++)
        {
            int a = first + s * stride;
            int b = first + ((s + 1) % n) * stride;
            for (int k = 0; k < stride - 1; k++)
                Quad(a + k, b + k, b + k + 1, a + k + 1);
        }
    }

    void FillMapped(List<Vector2> path, Color32 inner, Color32 outer, bool radial, Vector2 center, float feather)
    {
        MapPath(path, true);
        int n = mapped.Count;
        if (n < 3) return;
        float f = feather < 0f ? Feather : feather;
        if (!HasRoom(n * 2 + 1)) return;

        Vector2 middle;
        if (radial)
        {
            middle = Map(center);
        }
        else
        {
            middle = Vector2.zero;
            for (int i = 0; i < n; i++) middle += mapped[i];
            middle /= n;
        }

        int centerIndex = AddVertex(middle, inner);
        int first = Vertices.Count;
        for (int i = 0; i < n; i++) AddVertex(mapped[i], outer);
        for (int i = 0; i < n; i++) Triangle(centerIndex, first + i, first + (i + 1) % n);

        if (f <= 0f) return;

        float area = 0f;
        for (int i = 0; i < n; i++)
        {
            Vector2 a = mapped[i], b = mapped[(i + 1) % n];
            area += a.x * b.y - b.x * a.y;
        }
        float outward = area > 0f ? -1f : 1f;
        Color32 clear = outer;
        clear.a = 0;
        int ring = Vertices.Count;
        for (int i = 0; i < n; i++) AddVertex(mapped[i] + MiterAt(mapped, i, true) * (outward * f), clear);
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            Quad(first + i, first + j, ring + j, ring + i);
        }
    }

    Vector2 MiterAt(List<Vector2> pts, int i, bool closed)
    {
        int n = pts.Count;
        Vector2 p = pts[i];
        bool hasPrev = closed || i > 0;
        bool hasNext = closed || i < n - 1;
        Vector2 dPrev = hasPrev ? (p - pts[(i - 1 + n) % n]).normalized : Vector2.zero;
        Vector2 dNext = hasNext ? (pts[(i + 1) % n] - p).normalized : Vector2.zero;
        if (!hasPrev) dPrev = dNext;
        if (!hasNext) dNext = dPrev;

        Vector2 nPrev = new Vector2(-dPrev.y, dPrev.x);
        Vector2 nNext = new Vector2(-dNext.y, dNext.x);
        Vector2 m = nPrev + nNext;
        float length = m.magnitude;
        if (length < 1e-4f) return nNext;
        m /= length;
        float cos = Vector2.Dot(m, nNext);
        return m / Mathf.Max(cos, 1f / Mathf.Max(1f, MiterLimit));
    }

    bool HasRoom(int vertices)
    {
        if (Vertices.Count + vertices <= MaxVertices) return true;
        if (!warnedFull)
        {
            warnedFull = true;
            Debug.LogWarning("[BlueprintMeshBuilder] Too much geometry for one UI mesh; some lines were skipped.");
        }
        return false;
    }

    int AddVertex(Vector2 p, Color32 color)
    {
        UIVertex v = UIVertex.simpleVert;
        v.position = new Vector3(p.x, p.y, 0f);
        v.color = color;
        Vertices.Add(v);
        return Vertices.Count - 1;
    }

    void Triangle(int a, int b, int c)
    {
        Indices.Add(a);
        Indices.Add(b);
        Indices.Add(c);
    }

    void Quad(int a, int b, int c, int d)
    {
        Triangle(a, b, c);
        Triangle(c, d, a);
    }
}
