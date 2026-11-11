using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>A city's postcard picture: its skyline in ink and paper colours (drawn in a 100 x 40 box, scaled to fit).</summary>
public class CitySkyline : VisualElement
{
    readonly string cityId;
    readonly Color accent;

    Painter2D p;
    Rect box;

    public CitySkyline(string cityId, Color accent)
    {
        this.cityId = cityId;
        this.accent = accent;
        pickingMode = PickingMode.Ignore;
        generateVisualContent += Draw;
    }

    static readonly Color Ink = CityUi.Ink;
    static readonly Color Paper = CityUi.Card;
    static readonly Color Water = CityUi.PastelBlue;

    void Draw(MeshGenerationContext ctx)
    {
        p = ctx.painter2D;
        var r = contentRect;
        if (r.width < 10 || r.height < 10) return;
        // Keep the 100:40 shape, centred
        float scale = Mathf.Min(r.width / 100f, r.height / 40f);
        box = new Rect(r.x + (r.width - 100f * scale) * 0.5f, r.y + (r.height - 40f * scale) * 0.5f, 100f * scale, 40f * scale);
        p.lineJoin = LineJoin.Round;
        p.lineCap = LineCap.Round;

        switch (cityId)
        {
            case "ist": Istanbul(); break;
            case "lon": London(); break;
            case "nyc": NewYork(); break;
            case "tyo": Tokyo(); break;
            case "dxb": Dubai(); break;
            case "sha": Shanghai(); break;
        }
    }

    // ---- Drawing helpers (box units: x 0..100, y 0..40) --------------------------------------------------------------

    Vector2 P(float x, float y) => new Vector2(box.x + x / 100f * box.width, box.y + y / 40f * box.height);
    float S(float units) => units / 100f * box.width;

    void Shape(Color? fill, float stroke, params Vector2[] points)
    {
        p.BeginPath();
        p.MoveTo(P(points[0].x, points[0].y));
        for (int i = 1; i < points.Length; i++) p.LineTo(P(points[i].x, points[i].y));
        p.ClosePath();
        Finish(fill, stroke);
    }

    void Box(float x, float y, float w, float h, Color? fill, float stroke = 1.6f) =>
        Shape(fill, stroke, new Vector2(x, y), new Vector2(x + w, y), new Vector2(x + w, y + h), new Vector2(x, y + h));

    void Circle(float x, float y, float r, Color? fill, float stroke = 1.6f)
    {
        p.BeginPath();
        p.Arc(P(x, y), S(r), Angle.Degrees(0), Angle.Degrees(360));
        p.ClosePath();
        Finish(fill, stroke);
    }

    /// <summary>The top half of an ellipse sitting on y.</summary>
    void Dome(float cx, float y, float rx, float ry, Color? fill, float stroke = 1.6f)
    {
        var pts = new List<Vector2>();
        for (int i = 0; i <= 16; i++)
        {
            float a = Mathf.PI * i / 16f;
            pts.Add(new Vector2(cx - Mathf.Cos(a) * rx, y - Mathf.Sin(a) * ry));
        }
        Shape(fill, stroke, pts.ToArray());
    }

    void Line(float x1, float y1, float x2, float y2, Color colour, float width = 1.6f)
    {
        p.BeginPath();
        p.MoveTo(P(x1, y1));
        p.LineTo(P(x2, y2));
        p.strokeColor = colour;
        p.lineWidth = S(width * 0.35f);
        p.Stroke();
    }

    void Finish(Color? fill, float stroke)
    {
        if (fill.HasValue)
        {
            p.fillColor = fill.Value;
            p.Fill();
        }
        if (stroke > 0)
        {
            p.strokeColor = Ink;
            p.lineWidth = S(stroke * 0.35f);
            p.Stroke();
        }
    }

    void Sea(float top)
    {
        Box(-1, top, 102, 41 - top, Water, 0);
        Line(-1, top, 101, top, Ink, 1.8f);
        for (int i = 0; i < 6; i++)
        {
            float x = 6 + i * 16 + (i % 2) * 5, y = top + 3 + (i % 3) * 1.6f;
            p.BeginPath();
            p.MoveTo(P(x, y));
            p.BezierCurveTo(P(x + 1.5f, y - 1.2f), P(x + 3, y - 1.2f), P(x + 4.5f, y));
            p.BezierCurveTo(P(x + 6, y + 1.2f), P(x + 7.5f, y + 1.2f), P(x + 9, y));
            p.strokeColor = CityUi.Alpha(CityUi.Blue, 0.7f);
            p.lineWidth = S(0.5f);
            p.Stroke();
        }
    }

    void Birds(params float[] xy)
    {
        for (int i = 0; i + 1 < xy.Length; i += 2)
        {
            float x = xy[i], y = xy[i + 1];
            p.BeginPath();
            p.MoveTo(P(x - 1.6f, y - 0.8f));
            p.BezierCurveTo(P(x - 0.8f, y - 1.4f), P(x - 0.3f, y - 0.8f), P(x, y));
            p.BezierCurveTo(P(x + 0.3f, y - 0.8f), P(x + 0.8f, y - 1.4f), P(x + 1.6f, y - 0.8f));
            p.strokeColor = Ink;
            p.lineWidth = S(0.45f);
            p.Stroke();
        }
    }

    // ---- The six skylines ---------------------------------------------------------------------------------------------

    void Istanbul()
    {
        Sea(33);
        // A great mosque: body, domes, minarets
        Shape(Paper, 1.6f, new Vector2(8, 13), new Vector2(9.5f, 9), new Vector2(11, 13));
        Box(8.6f, 13, 1.8f, 20, Paper);
        Shape(Paper, 1.6f, new Vector2(42, 13), new Vector2(43.5f, 9), new Vector2(45, 13));
        Box(42.6f, 13, 1.8f, 20, Paper);
        Box(13, 24, 27, 9, Paper);
        Dome(18, 24, 4.5f, 3.2f, Paper);
        Dome(35, 24, 4.5f, 3.2f, Paper);
        Box(19, 20, 15, 4, Paper);
        Dome(26.5f, 20, 8.5f, 7, accent);
        Line(26.5f, 13, 26.5f, 10.5f, Ink, 1.4f);
        // Galata Tower
        Box(66, 14, 10, 19, Paper);
        Box(65, 12.5f, 12, 2.5f, Paper);
        Shape(accent, 1.6f, new Vector2(64.5f, 12.5f), new Vector2(71, 3), new Vector2(77.5f, 12.5f));
        for (int i = 0; i < 3; i++) Box(68 + i * 2.4f, 17, 1.2f, 2.4f, Ink, 0);
        for (int i = 0; i < 3; i++) Box(68 + i * 2.4f, 24, 1.2f, 2.4f, Ink, 0);
        // Houses between them
        Box(48, 27, 7, 6, Paper);
        Shape(accent, 1.4f, new Vector2(47.5f, 27), new Vector2(51.5f, 24), new Vector2(55.5f, 27));
        Box(56, 25, 7, 8, Paper);
        Shape(accent, 1.4f, new Vector2(55.5f, 25), new Vector2(59.5f, 22), new Vector2(63.5f, 25));
        Box(80, 26, 9, 7, Paper);
        Shape(accent, 1.4f, new Vector2(79.5f, 26), new Vector2(84.5f, 22.5f), new Vector2(89.5f, 26));
        Birds(52, 8, 57, 6, 88, 10);
    }

    void London()
    {
        Sea(34);
        // Big Ben and Parliament
        Box(13, 11, 8, 23, Paper);
        Box(12, 9, 10, 2.5f, Paper);
        Shape(accent, 1.6f, new Vector2(12, 9), new Vector2(17, 1), new Vector2(22, 9));
        Circle(17, 15, 2.6f, Paper);
        Line(17, 15, 17, 13.3f, Ink, 1.2f);
        Line(17, 15, 18.3f, 15, Ink, 1.2f);
        Box(21, 22, 20, 12, Paper);
        for (int i = 0; i < 5; i++) Shape(Paper, 1.3f, new Vector2(22 + i * 4, 22), new Vector2(23.5f + i * 4, 18.5f), new Vector2(25 + i * 4, 22));
        for (int i = 0; i < 5; i++) Box(22.6f + i * 4, 25, 1.6f, 3, Ink, 0);
        // The Splinter
        Shape(CityUi.PastelBlue, 1.6f, new Vector2(56, 34), new Vector2(61, 3), new Vector2(66, 34));
        Line(61, 3, 61, 34, CityUi.Alpha(Ink, 0.5f), 0.8f);
        // The big wheel
        Circle(84, 18, 11, null, 1.8f);
        for (int i = 0; i < 8; i++)
        {
            float a = Mathf.PI * i / 4f;
            Line(84, 18, 84 + Mathf.Cos(a) * 11, 18 + Mathf.Sin(a) * 11, CityUi.Alpha(Ink, 0.6f), 0.8f);
            Circle(84 + Mathf.Cos(a) * 11, 18 + Mathf.Sin(a) * 11, 1.2f, accent, 1f);
        }
        Line(84, 18, 78, 34, Ink, 1.6f);
        Line(84, 18, 90, 34, Ink, 1.6f);
        Birds(40, 8, 45, 6);
    }

    void NewYork()
    {
        Box(-1, 34, 102, 7, Water, 0);
        Line(-1, 34, 101, 34, Ink, 1.8f);
        // Skyline
        float[,] towers = { { 4, 22 }, { 10, 16 }, { 17, 25 }, { 23, 12 }, { 30, 20 }, { 55, 18 }, { 62, 24 }, { 68, 14 } };
        for (int i = 0; i < towers.GetLength(0); i++)
        {
            float x = towers[i, 0], y = towers[i, 1];
            Box(x, y, 6, 34 - y, Paper);
            for (float wy = y + 3; wy < 32; wy += 3.5f) Box(x + 1.2f, wy, 3.6f, 1.2f, CityUi.Alpha(Ink, 0.55f), 0);
        }
        // The Umpire State Building
        Box(38, 16, 12, 18, Ink, 1.6f);
        Box(40, 10, 8, 6, Ink, 1.6f);
        Box(42, 5, 4, 5, Ink, 1.6f);
        Line(44, 5, 44, 0.5f, Ink, 1.6f);
        for (float wy = 18; wy < 32; wy += 3) Box(40, wy, 8, 0.9f, accent, 0);
        // Liberty
        Box(80, 26, 10, 8, Paper);
        Shape(CityUi.Hex("7FD8B6"), 1.5f, new Vector2(82.5f, 26), new Vector2(84, 13), new Vector2(86, 13), new Vector2(87.5f, 26));
        Circle(85, 11, 1.6f, CityUi.Hex("7FD8B6"), 1.3f);
        Line(86.3f, 14, 89, 6, Ink, 1.5f);
        Shape(accent, 1.2f, new Vector2(88, 6), new Vector2(89.2f, 2.5f), new Vector2(90.4f, 6));
        // A yellow cab
        Box(22, 30.5f, 9, 3.2f, accent, 1.2f);
        Circle(24, 34, 1, Ink, 0);
        Circle(29, 34, 1, Ink, 0);
    }

    void Tokyo()
    {
        // Fuji behind everything
        Shape(CityUi.Hex("BFE3F5"), 1.6f, new Vector2(46, 34), new Vector2(67, 7), new Vector2(73, 7), new Vector2(96, 34));
        Shape(Paper, 1.2f, new Vector2(62.5f, 12.5f), new Vector2(67, 7), new Vector2(73, 7), new Vector2(77.5f, 12.5f),
            new Vector2(74, 11), new Vector2(71, 13), new Vector2(68, 11), new Vector2(65.5f, 13));
        Box(-1, 34, 102, 7, CityUi.Hex("EDE1C4"), 0);
        Line(-1, 34, 101, 34, Ink, 1.8f);
        // Tokyo Tower
        var orange = CityUi.Hex("E8742C");
        Shape(orange, 1.6f, new Vector2(13, 34), new Vector2(21.5f, 4), new Vector2(22.5f, 4), new Vector2(31, 34));
        Box(16.5f, 21, 11, 2, Paper, 1.2f);
        Box(19, 13, 6, 1.6f, Paper, 1.2f);
        Line(22, 4, 22, 0.5f, Ink, 1.4f);
        for (int i = 0; i < 4; i++)
        {
            float y = 24 + i * 2.5f;
            Line(15 + i * 0.7f, y, 29 - i * 0.7f, y + 2.5f, CityUi.Alpha(Ink, 0.5f), 0.8f);
            Line(29 - i * 0.7f, y, 15 + i * 0.7f, y + 2.5f, CityUi.Alpha(Ink, 0.5f), 0.8f);
        }
        // Torii gate
        var red = CityUi.Red;
        Box(37, 20, 1.8f, 14, red, 1.3f);
        Box(45.2f, 20, 1.8f, 14, red, 1.3f);
        Shape(red, 1.4f, new Vector2(34, 16.5f), new Vector2(50, 16.5f), new Vector2(49, 19), new Vector2(35, 19));
        Box(36, 21.5f, 12, 1.4f, red, 1.2f);
        // Blossoms
        foreach (var (x, y) in new[] { (6f, 10f), (9f, 7f), (4f, 14f), (88f, 30f), (93f, 27f) })
            Circle(x, y, 1.6f, accent, 0.9f);
    }

    void Dubai()
    {
        // Dunes
        p.BeginPath();
        p.MoveTo(P(-1, 34));
        p.BezierCurveTo(P(15, 29), P(30, 30), P(45, 34));
        p.BezierCurveTo(P(60, 30), P(80, 29), P(101, 33));
        p.LineTo(P(101, 41));
        p.LineTo(P(-1, 41));
        p.ClosePath();
        p.fillColor = CityUi.Hex("F3E3B5");
        p.Fill();
        p.strokeColor = Ink;
        p.lineWidth = S(0.6f);
        p.Stroke();
        Circle(12, 9, 5, accent, 1.4f);
        // The tallest tower
        Shape(Paper, 1.5f, new Vector2(40, 34), new Vector2(40, 26), new Vector2(42, 26), new Vector2(42, 18), new Vector2(44, 18),
            new Vector2(44, 10), new Vector2(45.5f, 10), new Vector2(46, 3), new Vector2(46.5f, 10), new Vector2(48, 10),
            new Vector2(48, 18), new Vector2(50, 18), new Vector2(50, 26), new Vector2(52, 26), new Vector2(52, 34));
        Line(46, 3, 46, 0.3f, Ink, 1.3f);
        // The sail hotel
        p.BeginPath();
        p.MoveTo(P(70, 33));
        p.BezierCurveTo(P(70, 22), P(76, 12), P(82, 5));
        p.LineTo(P(82, 33));
        p.ClosePath();
        Finish(Paper, 1.6f);
        Line(82, 3, 82, 33, Ink, 1.6f);
        Line(72, 26, 82, 26, CityUi.Alpha(Ink, 0.5f), 0.8f);
        Line(74.5f, 18, 82, 18, CityUi.Alpha(Ink, 0.5f), 0.8f);
        // A palm
        p.BeginPath();
        p.MoveTo(P(24, 33));
        p.BezierCurveTo(P(24.5f, 26), P(25.5f, 20), P(27, 15));
        p.strokeColor = CityUi.Hex("6E4829");
        p.lineWidth = S(1.1f);
        p.Stroke();
        var green = CityUi.Green;
        foreach (var (dx, dy) in new[] { (-7f, 3f), (-5f, -2f), (6f, 3f), (5f, -2.5f), (0f, -4f) })
        {
            p.BeginPath();
            p.MoveTo(P(27, 15));
            p.BezierCurveTo(P(27 + dx * 0.4f, 15 + dy - 2), P(27 + dx * 0.8f, 15 + dy - 1), P(27 + dx, 15 + dy + 2));
            p.strokeColor = green;
            p.lineWidth = S(0.9f);
            p.Stroke();
        }
    }

    void Shanghai()
    {
        Sea(33);
        // The Bund: old stone banks and the customs house clock tower
        Box(3, 24, 9, 9, Paper);
        Box(12, 22, 9, 11, Paper);
        Box(21, 25, 9, 8, Paper);
        Box(14.5f, 15, 4, 7, Paper);
        Shape(accent, 1.4f, new Vector2(14, 15), new Vector2(16.5f, 10.5f), new Vector2(19, 15));
        Circle(16.5f, 18, 1.3f, Paper, 1f);
        for (int i = 0; i < 3; i++)
        {
            Box(4.5f + i * 2.4f, 27, 1.2f, 3, CityUi.Alpha(Ink, 0.55f), 0);
            Box(22.5f + i * 2.4f, 27.5f, 1.2f, 3, CityUi.Alpha(Ink, 0.55f), 0);
        }
        for (int i = 0; i < 3; i++) Box(13.5f + i * 2.4f, 25, 1.2f, 4, CityUi.Alpha(Ink, 0.55f), 0);

        // Across the river: the pearl tower on its legs
        var pink = CityUi.Hex("FF8FA3");
        Line(57, 33, 61, 22, Ink, 1.5f);
        Line(65, 33, 61, 22, Ink, 1.5f);
        Line(61, 33, 61, 22, Ink, 1.5f);
        Box(60.2f, 7, 1.6f, 14, Paper, 1.3f);
        Circle(61, 21, 4.2f, pink, 1.5f);
        Circle(61, 12.5f, 1.4f, pink, 1.2f);
        Circle(61, 7.5f, 2.4f, pink, 1.4f);
        Line(61, 5, 61, 0.8f, Ink, 1.3f);

        // The twisting tower, the tallest
        p.BeginPath();
        p.MoveTo(P(71, 33));
        p.BezierCurveTo(P(71.5f, 20), P(72.5f, 10), P(74, 3));
        p.LineTo(P(77.5f, 1.5f));
        p.BezierCurveTo(P(78, 10), P(79.5f, 20), P(81, 33));
        p.ClosePath();
        Finish(CityUi.PastelBlue, 1.6f);
        p.BeginPath();
        p.MoveTo(P(73.5f, 33));
        p.BezierCurveTo(P(75, 22), P(74.5f, 12), P(76.5f, 2.5f));
        p.strokeColor = CityUi.Alpha(Ink, 0.5f);
        p.lineWidth = S(0.3f);
        p.Stroke();

        // A tiered tower beside it
        Shape(Paper, 1.5f, new Vector2(84, 33), new Vector2(84, 22), new Vector2(85, 22), new Vector2(85, 15),
            new Vector2(86, 15), new Vector2(86, 9), new Vector2(87.5f, 9), new Vector2(88, 5), new Vector2(88.5f, 9),
            new Vector2(90, 9), new Vector2(90, 15), new Vector2(91, 15), new Vector2(91, 22), new Vector2(92, 22), new Vector2(92, 33));
        for (float y = 11; y < 31; y += 3.2f) Line(86.5f, y, 89.5f, y, CityUi.Alpha(Ink, 0.4f), 0.6f);
        Birds(40, 8, 46, 11);
    }
}
