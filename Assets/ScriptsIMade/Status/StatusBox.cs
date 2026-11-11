using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A rounded box in the design's stationery style, sized to its RectTransform: fill (flat or a top-to-bottom gradient),
/// a solid or dashed border inside the edge, per-corner radii for the hand-drawn look, a hard or soft drop shadow, an
/// outline outside the edge (like CSS outline + outline-offset) and optional corner covers that paint the rect's
/// corners outside the rounding, so a rounded frame can sit on top of clipped content. Edges are softened over about
/// one screen pixel; everything is tinted by the graphic's colour. Used by the status screens built at runtime.
/// Requires a CanvasRenderer: Unity UI 2.0's Graphic no longer adds one.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class StatusBox : MaskableGraphic
{
    // Outline buffers shared by every box (meshes are built one at a time on the main thread)
    static readonly List<Vector2> PathA = new List<Vector2>(64);
    static readonly List<Vector2> PathB = new List<Vector2>(64);
    static readonly List<Vector2> PathC = new List<Vector2>(64);
    static readonly List<Vector2> PathD = new List<Vector2>(64);
    static readonly List<Vector2> PathMid = new List<Vector2>(64);

    [SerializeField] Color fill = Color.white;
    [Tooltip("Fade the fill from Fill (top) to Fill Bottom")]
    [SerializeField] bool gradient;
    [SerializeField] Color fillBottom = Color.white;
    [SerializeField] Color border = new Color32(29, 36, 51, 255);
    [SerializeField] float borderWidth = 2f;
    [Tooltip("Corner radii: top-left, top-right, bottom-right, bottom-left")]
    [SerializeField] Vector4 radii = new Vector4(10f, 14f, 9f, 15f);
    [SerializeField] bool dashed;
    [SerializeField] float dashLength = 6f;
    [SerializeField] float gapLength = 4f;

    [Header("Shadow")]
    [SerializeField] Color shadowColor = Color.clear;
    [Tooltip("Offset in pixels, y pointing down")]
    [SerializeField] Vector2 shadowOffset = new Vector2(3f, 4f);
    [Tooltip("0 = a hard shadow")]
    [SerializeField] float shadowBlur;

    [Header("Outline (outside the edge)")]
    [SerializeField] Color outlineColor = Color.clear;
    [SerializeField] float outlineWidth;
    [SerializeField] float outlineOffset;

    [Header("Corner covers")]
    [Tooltip("Paints the rect's corners outside the rounding in this colour (clear = off)")]
    [SerializeField] Color cornerColor = Color.clear;

    /// <summary>Flat fill colour (switches a gradient off).</summary>
    public Color Fill { get => fill; set { fill = value; gradient = false; SetVerticesDirty(); } }
    public Color Border { get => border; set { border = value; SetVerticesDirty(); } }
    public float BorderWidth { get => borderWidth; set { borderWidth = value; SetVerticesDirty(); } }
    public Vector4 Radii { get => radii; set { radii = value; SetVerticesDirty(); } }
    public Color CornerColor { get => cornerColor; set { cornerColor = value; SetVerticesDirty(); } }

    public void SetStyle(Color fillColor, Color borderColor, float width)
    {
        fill = fillColor;
        gradient = false;
        border = borderColor;
        borderWidth = width;
        SetVerticesDirty();
    }

    /// <summary>Fill that fades from <paramref name="top"/> to <paramref name="bottom"/>.</summary>
    public void SetGradient(Color top, Color bottom)
    {
        fill = top;
        fillBottom = bottom;
        gradient = true;
        SetVerticesDirty();
    }

    public void SetDash(bool on, float dash = 6f, float gap = 4f)
    {
        dashed = on;
        dashLength = dash;
        gapLength = gap;
        SetVerticesDirty();
    }

    /// <summary>Drop shadow; offset in pixels with y pointing down. Blur 0 = a hard shadow.</summary>
    public void SetShadow(Color color, Vector2 offset, float blur = 0f)
    {
        shadowColor = color;
        shadowOffset = offset;
        shadowBlur = blur;
        SetVerticesDirty();
    }

    /// <summary>A line around the box, <paramref name="offset"/> pixels outside its edge.</summary>
    public void SetOutline(Color color, float width, float offset)
    {
        outlineColor = color;
        outlineWidth = width;
        outlineOffset = offset;
        SetVerticesDirty();
    }

    /// <summary>About one screen pixel in canvas units (the width of the softened edges).</summary>
    public static float SoftEdge(Canvas c)
    {
        if (c == null) return 1f;
        Canvas root = c.rootCanvas != null ? c.rootCanvas : c;
        return 1f / Mathf.Max(0.05f, root.scaleFactor);
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        if (r.width <= 0f || r.height <= 0f) return;

        float soft = SoftEdge(canvas);
        float reach = Mathf.Max(shadowBlur * 0.5f, outlineOffset + outlineWidth);
        float largest = Mathf.Max(Mathf.Max(radii.x, radii.y), Mathf.Max(radii.z, radii.w));
        int steps = Mathf.Clamp(Mathf.CeilToInt((largest + reach) / 3f), 2, 12);

        if (shadowColor.a > 0f)
        {
            Color32 c = Tint(shadowColor);
            FillShape(vh, r, new Vector2(shadowOffset.x, -shadowOffset.y), c, c, Mathf.Max(soft, shadowBlur), steps);
        }
        if (fill.a > 0f || (gradient && fillBottom.a > 0f))
            FillShape(vh, r, Vector2.zero, Tint(fill), Tint(gradient ? fillBottom : fill), soft, steps);
        if (cornerColor.a > 0f)
            CornerCovers(vh, r, Tint(cornerColor), steps);
        if (outlineColor.a > 0f && outlineWidth > 0f)
            Ring(vh, r, outlineOffset + outlineWidth, outlineOffset, Tint(outlineColor), soft, steps, false);
        if (border.a > 0f && borderWidth > 0f)
            Ring(vh, r, 0f, -borderWidth, Tint(border), soft, steps, dashed);
    }

    Color32 Tint(Color c)
    {
        return c * color;
    }

    /// <summary>
    /// The box's outline grown by <paramref name="grow"/> (negative = inset), clockwise from the top-left corner.
    /// Every corner has steps + 1 points, so outlines grown by different amounts match point for point.
    /// </summary>
    void BuildPath(List<Vector2> into, Rect r, float grow, int steps)
    {
        into.Clear();
        float x0 = r.xMin - grow, x1 = r.xMax + grow;
        float y0 = r.yMin - grow, y1 = r.yMax + grow;   // local space: y points up, y1 is the top
        if (x1 < x0) x0 = x1 = (x0 + x1) * 0.5f;
        if (y1 < y0) y0 = y1 = (y0 + y1) * 0.5f;
        float limit = Mathf.Min(x1 - x0, y1 - y0) * 0.5f;
        float tl = Mathf.Clamp(radii.x + grow, 0f, limit);
        float tr = Mathf.Clamp(radii.y + grow, 0f, limit);
        float br = Mathf.Clamp(radii.z + grow, 0f, limit);
        float bl = Mathf.Clamp(radii.w + grow, 0f, limit);
        Arc(into, new Vector2(x0 + tl, y1 - tl), tl, 180f, 90f, steps);
        Arc(into, new Vector2(x1 - tr, y1 - tr), tr, 90f, 0f, steps);
        Arc(into, new Vector2(x1 - br, y0 + br), br, 0f, -90f, steps);
        Arc(into, new Vector2(x0 + bl, y0 + bl), bl, -90f, -180f, steps);
    }

    static void Arc(List<Vector2> into, Vector2 center, float radius, float fromDegrees, float toDegrees, int steps)
    {
        for (int i = 0; i <= steps; i++)
        {
            float a = Mathf.Lerp(fromDegrees, toDegrees, i / (float)steps) * Mathf.Deg2Rad;
            into.Add(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
        }
    }

    /// <summary>The rounded shape, offset, with a soft edge <paramref name="feather"/> wide (a blurred shadow uses its blur).</summary>
    void FillShape(VertexHelper vh, Rect r, Vector2 offset, Color32 top, Color32 bottom, float feather, int steps)
    {
        float half = feather * 0.5f;
        BuildPath(PathA, r, -half, steps);
        BuildPath(PathB, r, half, steps);
        int n = PathA.Count;

        int center = vh.currentVertCount;
        vh.AddVert(r.center + offset, Shade(top, bottom, r, r.center.y, 1f), Vector4.zero);
        for (int i = 0; i < n; i++)
            vh.AddVert(PathA[i] + offset, Shade(top, bottom, r, PathA[i].y, 1f), Vector4.zero);
        for (int i = 0; i < n; i++)
            vh.AddVert(PathB[i] + offset, Shade(top, bottom, r, PathB[i].y, 0f), Vector4.zero);

        int inner = center + 1, outer = center + 1 + n;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            vh.AddTriangle(center, inner + i, inner + j);
            vh.AddTriangle(inner + i, outer + i, outer + j);
            vh.AddTriangle(inner + i, outer + j, inner + j);
        }
    }

    static Color32 Shade(Color32 top, Color32 bottom, Rect r, float y, float alpha)
    {
        float t = r.height > 0f ? Mathf.Clamp01((r.yMax - y) / r.height) : 0f;
        Color32 c = Color32.Lerp(top, bottom, t);
        c.a = (byte)Mathf.RoundToInt(c.a * alpha);
        return c;
    }

    /// <summary>A line between two outlines of the box (grow values), solid or dashed, with soft edges on both sides.</summary>
    void Ring(VertexHelper vh, Rect r, float outerGrow, float innerGrow, Color32 color, float soft, int steps, bool dash)
    {
        float half = soft * 0.5f;
        float width = outerGrow - innerGrow;
        Color32 solid = color;
        Color32 clear = color;
        clear.a = 0;
        float outer = outerGrow - half, inner = innerGrow + half;
        if (width < soft)
        {
            // Thinner than the soft edge: one soft line of the same weight
            outer = inner = (outerGrow + innerGrow) * 0.5f;
            solid.a = (byte)Mathf.RoundToInt(color.a * width / soft);
        }
        BuildPath(PathA, r, outer + half, steps);
        BuildPath(PathB, r, outer, steps);
        BuildPath(PathC, r, inner, steps);
        BuildPath(PathD, r, inner - half, steps);
        int n = PathA.Count;

        if (!dash || dashLength <= 0f || gapLength <= 0f)
        {
            int start = vh.currentVertCount;
            AddRow(vh, PathA, clear);
            AddRow(vh, PathB, solid);
            AddRow(vh, PathC, solid);
            AddRow(vh, PathD, clear);
            for (int row = 0; row < 3; row++)
            {
                int a = start + row * n, b = a + n;
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    vh.AddTriangle(a + i, b + i, b + j);
                    vh.AddTriangle(a + i, b + j, a + j);
                }
            }
            return;
        }

        // Dashes are measured along the middle of the line
        BuildPath(PathMid, r, (outerGrow + innerGrow) * 0.5f, steps);
        float period = dashLength + gapLength;
        float travelled = 0f;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            float length = Vector2.Distance(PathMid[i], PathMid[j]);
            if (length < 0.001f) continue;
            float s = 0f;
            while (s < length)
            {
                float phase = (travelled + s) % period;
                float next;
                if (phase < dashLength)
                {
                    next = Mathf.Min(length, s + dashLength - phase);
                    DashPiece(vh, i, j, s / length, next / length, solid, clear);
                }
                else
                {
                    next = Mathf.Min(length, s + period - phase);
                }
                s = Mathf.Max(next, s + 0.0001f);   // rounding can't stall the walk
            }
            travelled += length;
        }
    }

    static void AddRow(VertexHelper vh, List<Vector2> path, Color32 c)
    {
        for (int i = 0; i < path.Count; i++)
            vh.AddVert(path[i], c, Vector4.zero);
    }

    static void DashPiece(VertexHelper vh, int i, int j, float from, float to, Color32 solid, Color32 clear)
    {
        int start = vh.currentVertCount;
        AddSpan(vh, PathA, i, j, from, to, clear);
        AddSpan(vh, PathB, i, j, from, to, solid);
        AddSpan(vh, PathC, i, j, from, to, solid);
        AddSpan(vh, PathD, i, j, from, to, clear);
        for (int row = 0; row < 3; row++)
        {
            int a = start + row * 2, b = a + 2;
            vh.AddTriangle(a, b, b + 1);
            vh.AddTriangle(a, b + 1, a + 1);
        }
    }

    static void AddSpan(VertexHelper vh, List<Vector2> path, int i, int j, float from, float to, Color32 c)
    {
        vh.AddVert(Vector2.Lerp(path[i], path[j], from), c, Vector4.zero);
        vh.AddVert(Vector2.Lerp(path[i], path[j], to), c, Vector4.zero);
    }

    /// <summary>Fill each corner of the rect outside the rounding (a fan from the corner point along the arc).</summary>
    void CornerCovers(VertexHelper vh, Rect r, Color32 c, int steps)
    {
        BuildPath(PathA, r, 0f, steps);
        int per = steps + 1;
        for (int k = 0; k < 4; k++)
        {
            Vector2 corner;
            switch (k)
            {
                case 0: corner = new Vector2(r.xMin, r.yMax); break;
                case 1: corner = new Vector2(r.xMax, r.yMax); break;
                case 2: corner = new Vector2(r.xMax, r.yMin); break;
                default: corner = new Vector2(r.xMin, r.yMin); break;
            }
            int start = vh.currentVertCount;
            vh.AddVert(corner, c, Vector4.zero);
            for (int s = 0; s < per; s++)
                vh.AddVert(PathA[k * per + s], c, Vector4.zero);
            for (int s = 0; s < steps; s++)
                vh.AddTriangle(start, start + 1 + s, start + 2 + s);
        }
    }
}
