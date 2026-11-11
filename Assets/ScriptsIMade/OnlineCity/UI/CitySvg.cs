using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Draws SVG path data ("M12 3l2.6 5.4 ...") with UI Toolkit's Painter2D, in the design's pen style: round caps and
/// joins, strokes scaled with the box. Supports M L H V C S Q T A Z (absolute and relative). Parsed paths are cached.
/// </summary>
public static class CitySvg
{
    public struct Segment
    {
        public bool Cubic;
        public Vector2 C1, C2, P;
    }

    public class SubPath
    {
        public Vector2 Start;
        public List<Segment> Segments = new List<Segment>();
        public bool Closed;
    }

    public class Shape
    {
        public List<SubPath> Paths = new List<SubPath>();
    }

    static readonly Dictionary<string, Shape> cache = new Dictionary<string, Shape>();

    public static Shape Parse(string d)
    {
        if (string.IsNullOrEmpty(d)) return new Shape();
        if (cache.TryGetValue(d, out var cached)) return cached;
        var shape = new Parser(d).Run();
        cache[d] = shape;
        return shape;
    }

    // ---- Drawing ------------------------------------------------------------------------------------------------

    /// <summary>Stroke the path, mapping its viewBox (0..viewBox square) into the rect.</summary>
    public static void Stroke(Painter2D p, Shape shape, Rect rect, Color color, float strokeInViewBox = 2.4f,
        float viewBox = 24f, float rotateDegrees = 0f)
    {
        if (shape == null || shape.Paths.Count == 0) return;
        float scale = Mathf.Min(rect.width, rect.height) / viewBox;
        p.lineWidth = Mathf.Max(0.5f, strokeInViewBox * scale);
        p.strokeColor = color;
        p.lineCap = LineCap.Round;
        p.lineJoin = LineJoin.Round;
        p.BeginPath();
        Emit(p, shape, rect, viewBox, rotateDegrees);
        p.Stroke();
    }

    public static void Fill(Painter2D p, Shape shape, Rect rect, Color color, float viewBox = 24f, float rotateDegrees = 0f)
    {
        if (shape == null || shape.Paths.Count == 0) return;
        p.fillColor = color;
        p.BeginPath();
        Emit(p, shape, rect, viewBox, rotateDegrees);
        p.Fill(FillRule.NonZero);
    }

    static void Emit(Painter2D p, Shape shape, Rect rect, float viewBox, float rotateDegrees)
    {
        float scale = Mathf.Min(rect.width, rect.height) / viewBox;
        var offset = new Vector2(rect.x + (rect.width - viewBox * scale) * 0.5f, rect.y + (rect.height - viewBox * scale) * 0.5f);
        float rad = rotateDegrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
        var center = new Vector2(viewBox * 0.5f, viewBox * 0.5f);

        Vector2 T(Vector2 v)
        {
            if (rotateDegrees != 0f)
            {
                var d = v - center;
                v = center + new Vector2(d.x * cos - d.y * sin, d.x * sin + d.y * cos);
            }
            return offset + v * scale;
        }

        foreach (var sub in shape.Paths)
        {
            p.MoveTo(T(sub.Start));
            if (sub.Segments.Count == 0)
            {
                // A lone point ("M8 8h.01" makes a dot): a tiny line gets round caps
                p.LineTo(T(sub.Start + new Vector2(0.01f, 0f)));
                continue;
            }
            foreach (var seg in sub.Segments)
            {
                if (seg.Cubic) p.BezierCurveTo(T(seg.C1), T(seg.C2), T(seg.P));
                else p.LineTo(T(seg.P));
            }
            if (sub.Closed) p.ClosePath();
        }
    }

    // ---- Helpers for path strings ---------------------------------------------------------------------------------

    public static string Circle(float cx, float cy, float r) =>
        F($"M{cx - r} {cy}A{r} {r} 0 1 0 {cx + r} {cy}A{r} {r} 0 1 0 {cx - r} {cy}Z");

    public static string Ellipse(float cx, float cy, float rx, float ry) =>
        F($"M{cx - rx} {cy}A{rx} {ry} 0 1 0 {cx + rx} {cy}A{rx} {ry} 0 1 0 {cx - rx} {cy}Z");

    public static string RoundRect(float x, float y, float w, float h, float r)
    {
        r = Mathf.Min(r, Mathf.Min(w, h) * 0.5f);
        return F($"M{x + r} {y}H{x + w - r}A{r} {r} 0 0 1 {x + w} {y + r}V{y + h - r}A{r} {r} 0 0 1 {x + w - r} {y + h}")
             + F($"H{x + r}A{r} {r} 0 0 1 {x} {y + h - r}V{y + r}A{r} {r} 0 0 1 {x + r} {y}Z");
    }

    /// <summary>A closed polygon through the points.</summary>
    public static string Polygon(IList<Vector2> points)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < points.Count; i++)
        {
            sb.Append(i == 0 ? "M" : "L");
            sb.Append(points[i].x.ToString("0.###", CultureInfo.InvariantCulture)).Append(' ');
            sb.Append(points[i].y.ToString("0.###", CultureInfo.InvariantCulture));
        }
        sb.Append('Z');
        return sb.ToString();
    }

    /// <summary>Format numbers with a dot whatever the PC's language (Turkish Windows uses commas).</summary>
    static string F(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

    // ---- Parser -------------------------------------------------------------------------------------------------

    class Parser
    {
        readonly string d;
        int i;
        readonly Shape shape = new Shape();
        SubPath current;
        Vector2 point, subStart, lastControl;
        char lastCommand;

        public Parser(string d) { this.d = d; }

        public Shape Run()
        {
            char command = ' ';
            while (true)
            {
                SkipSeparators();
                if (i >= d.Length) break;
                char c = d[i];
                if (char.IsLetter(c))
                {
                    command = c;
                    i++;
                }
                else if (command == ' ')
                {
                    break; // Garbage before the first command
                }
                else if (command == 'M') command = 'L'; // Extra pairs after a move are lines
                else if (command == 'm') command = 'l';

                if (!Execute(command)) break;
                lastCommand = command;
            }
            return shape;
        }

        bool Execute(char command)
        {
            bool rel = char.IsLower(command);
            Vector2 origin = rel ? point : Vector2.zero;
            switch (char.ToUpperInvariant(command))
            {
                case 'M':
                {
                    if (!Pair(out var p)) return false;
                    point = origin + p;
                    subStart = point;
                    current = new SubPath { Start = point };
                    shape.Paths.Add(current);
                    lastControl = point;
                    return true;
                }
                case 'L':
                {
                    if (!Pair(out var p)) return false;
                    LineTo(origin + p);
                    return true;
                }
                case 'H':
                {
                    if (!Number(out float x)) return false;
                    LineTo(new Vector2(rel ? point.x + x : x, point.y));
                    return true;
                }
                case 'V':
                {
                    if (!Number(out float y)) return false;
                    LineTo(new Vector2(point.x, rel ? point.y + y : y));
                    return true;
                }
                case 'C':
                {
                    if (!Pair(out var c1) || !Pair(out var c2) || !Pair(out var p)) return false;
                    CubicTo(origin + c1, origin + c2, origin + p);
                    return true;
                }
                case 'S':
                {
                    if (!Pair(out var c2) || !Pair(out var p)) return false;
                    Vector2 c1 = "CcSs".IndexOf(lastCommand) >= 0 ? point * 2 - lastControl : point;
                    CubicTo(c1, origin + c2, origin + p);
                    return true;
                }
                case 'Q':
                {
                    if (!Pair(out var q) || !Pair(out var p)) return false;
                    QuadTo(origin + q, origin + p);
                    return true;
                }
                case 'T':
                {
                    if (!Pair(out var p)) return false;
                    Vector2 q = "QqTt".IndexOf(lastCommand) >= 0 ? point * 2 - lastControl : point;
                    QuadTo(q, origin + p);
                    return true;
                }
                case 'A':
                {
                    if (!Number(out float rx) || !Number(out float ry) || !Number(out float rotation)
                        || !Flag(out bool large) || !Flag(out bool sweep) || !Pair(out var p)) return false;
                    ArcTo(rx, ry, rotation, large, sweep, origin + p);
                    return true;
                }
                case 'Z':
                {
                    if (current != null) current.Closed = true;
                    point = subStart;
                    lastControl = point;
                    return true;
                }
            }
            return false;
        }

        void Ensure()
        {
            if (current != null) return;
            current = new SubPath { Start = point };
            shape.Paths.Add(current);
        }

        void LineTo(Vector2 p)
        {
            Ensure();
            current.Segments.Add(new Segment { P = p });
            point = p;
            lastControl = p;
        }

        void CubicTo(Vector2 c1, Vector2 c2, Vector2 p)
        {
            Ensure();
            current.Segments.Add(new Segment { Cubic = true, C1 = c1, C2 = c2, P = p });
            point = p;
            lastControl = c2;
        }

        void QuadTo(Vector2 q, Vector2 p)
        {
            Vector2 c1 = point + (q - point) * (2f / 3f);
            Vector2 c2 = p + (q - p) * (2f / 3f);
            CubicTo(c1, c2, p);
            lastControl = q;
        }

        /// <summary>SVG elliptical arc to cubic Béziers (endpoint to centre conversion, then up to 90° pieces).</summary>
        void ArcTo(float rx, float ry, float rotationDeg, bool largeArc, bool sweep, Vector2 end)
        {
            Vector2 start = point;
            if (rx == 0 || ry == 0 || start == end)
            {
                LineTo(end);
                return;
            }
            rx = Mathf.Abs(rx);
            ry = Mathf.Abs(ry);
            float phi = rotationDeg * Mathf.Deg2Rad;
            float cosPhi = Mathf.Cos(phi), sinPhi = Mathf.Sin(phi);

            float dx = (start.x - end.x) * 0.5f, dy = (start.y - end.y) * 0.5f;
            float x1 = cosPhi * dx + sinPhi * dy;
            float y1 = -sinPhi * dx + cosPhi * dy;

            float lambda = (x1 * x1) / (rx * rx) + (y1 * y1) / (ry * ry);
            if (lambda > 1f)
            {
                float s = Mathf.Sqrt(lambda);
                rx *= s;
                ry *= s;
            }

            float num = rx * rx * ry * ry - rx * rx * y1 * y1 - ry * ry * x1 * x1;
            float den = rx * rx * y1 * y1 + ry * ry * x1 * x1;
            float coef = den == 0 ? 0 : Mathf.Sqrt(Mathf.Max(0f, num / den));
            if (largeArc == sweep) coef = -coef;
            float cxp = coef * (rx * y1 / ry);
            float cyp = coef * -(ry * x1 / rx);

            float cx = cosPhi * cxp - sinPhi * cyp + (start.x + end.x) * 0.5f;
            float cy = sinPhi * cxp + cosPhi * cyp + (start.y + end.y) * 0.5f;

            float theta1 = Angle(1, 0, (x1 - cxp) / rx, (y1 - cyp) / ry);
            float delta = Angle((x1 - cxp) / rx, (y1 - cyp) / ry, (-x1 - cxp) / rx, (-y1 - cyp) / ry);
            if (!sweep && delta > 0) delta -= 2 * Mathf.PI;
            else if (sweep && delta < 0) delta += 2 * Mathf.PI;

            int pieces = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(delta) / (Mathf.PI / 2f) - 0.0001f));
            float step = delta / pieces;
            float k = 4f / 3f * Mathf.Tan(step / 4f);

            Vector2 Pt(float t) => new Vector2(
                cx + rx * Mathf.Cos(t) * cosPhi - ry * Mathf.Sin(t) * sinPhi,
                cy + rx * Mathf.Cos(t) * sinPhi + ry * Mathf.Sin(t) * cosPhi);
            Vector2 Deriv(float t) => new Vector2(
                -rx * Mathf.Sin(t) * cosPhi - ry * Mathf.Cos(t) * sinPhi,
                -rx * Mathf.Sin(t) * sinPhi + ry * Mathf.Cos(t) * cosPhi);

            float a = theta1;
            for (int n = 0; n < pieces; n++)
            {
                float b = a + step;
                Vector2 p0 = Pt(a), p3 = n == pieces - 1 ? end : Pt(b);
                CubicTo(p0 + Deriv(a) * k, p3 - Deriv(b) * k, p3);
                a = b;
            }
        }

        static float Angle(float ux, float uy, float vx, float vy)
        {
            float dot = ux * vx + uy * vy;
            float len = Mathf.Sqrt((ux * ux + uy * uy) * (vx * vx + vy * vy));
            float ang = Mathf.Acos(Mathf.Clamp(len == 0 ? 1 : dot / len, -1f, 1f));
            return ux * vy - uy * vx < 0 ? -ang : ang;
        }

        void SkipSeparators()
        {
            while (i < d.Length && (char.IsWhiteSpace(d[i]) || d[i] == ',')) i++;
        }

        bool Pair(out Vector2 v)
        {
            v = default;
            if (!Number(out float x) || !Number(out float y)) return false;
            v = new Vector2(x, y);
            return true;
        }

        bool Flag(out bool flag)
        {
            SkipSeparators();
            flag = false;
            if (i >= d.Length || (d[i] != '0' && d[i] != '1')) return false;
            flag = d[i] == '1';
            i++;
            return true;
        }

        bool Number(out float value)
        {
            SkipSeparators();
            value = 0;
            int start = i;
            if (i < d.Length && (d[i] == '-' || d[i] == '+')) i++;
            bool digits = false, dot = false;
            while (i < d.Length)
            {
                char c = d[i];
                if (c >= '0' && c <= '9') { digits = true; i++; }
                else if (c == '.' && !dot) { dot = true; i++; }
                else break;
            }
            if (i < d.Length && (d[i] == 'e' || d[i] == 'E'))
            {
                int save = i;
                i++;
                if (i < d.Length && (d[i] == '-' || d[i] == '+')) i++;
                if (i < d.Length && char.IsDigit(d[i])) { while (i < d.Length && char.IsDigit(d[i])) i++; }
                else i = save;
            }
            if (!digits)
            {
                i = start;
                return false;
            }
            return float.TryParse(d.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }
}
