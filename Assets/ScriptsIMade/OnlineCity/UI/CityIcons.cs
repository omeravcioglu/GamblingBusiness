using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The City's drawings, as SVG path data in the design's style (24x24 box, 2.4 pen, round ends): the logo maker's
/// icons (ids from CityLogo.Icons), the logo background shapes (100x100 box) and the interface icons.
/// </summary>
public static class CityIcons
{
    public struct Part
    {
        public string Path;
        public float Rotate; // degrees around the box centre
        public Part(string path, float rotate = 0f) { Path = path; Rotate = rotate; }
    }

    static string C(float cx, float cy, float r) => CitySvg.Circle(cx, cy, r);
    static string E(float cx, float cy, float rx, float ry) => CitySvg.Ellipse(cx, cy, rx, ry);
    static string R(float x, float y, float w, float h, float r) => CitySvg.RoundRect(x, y, w, h, r);

    const string GlobeLines = "M3 12h18M12 3c2.5 2.6 3.8 5.6 3.8 9s-1.3 6.4-3.8 9c-2.5-2.6-3.8-5.6-3.8-9S9.5 5.6 12 3z";
    const string CoinS = "M14.6 9.2c-.5-.8-1.5-1.3-2.6-1.3-1.5 0-2.7.8-2.7 2 0 2.8 5.5 1.5 5.5 4.3 0 1.2-1.2 2-2.8 2-1.2 0-2.3-.5-2.9-1.4M12 6.5v1.4M12 16.9v1.6";
    const string CloverLeaf = "M12 11.5c-2.2-1.6-4.6-3.6-4.6-5.8 0-1.6 1.2-2.7 2.6-2.7 1 0 1.7.6 2 1.3.3-.7 1-1.3 2-1.3 1.4 0 2.6 1.1 2.6 2.7 0 2.2-2.4 4.2-4.6 5.8z";
    const string Pin = "M12 21s-7-6.2-7-11.5a7 7 0 0 1 14 0C19 14.8 12 21 12 21z";
    const string Tag = "M3 12V4.5A1.5 1.5 0 0 1 4.5 3H12l9 9-9 9z";
    const string TrophyCup = "M8 4h8v5a4 4 0 0 1-8 0z";
    const string TrophyRest = "M8 6H4.5a3.5 3.5 0 0 0 3.8 4.2M16 6h3.5a3.5 3.5 0 0 1-3.8 4.2M12 13v4M8.5 20.5h7M10 17h4v3.5h-4z";

    static readonly Dictionary<string, Part[]> logoIcons = new Dictionary<string, Part[]>
    {
        { "star", P("M12 3.5l2.6 5.4 5.9.8-4.3 4.1 1 5.8L12 16.9l-5.2 2.7 1-5.8-4.3-4.1 5.9-.8z") },
        { "crown", P("M4 17.5L2.8 7.8l5.1 3.9L12 5l4.1 6.7 5.1-3.9L20 17.5z", "M4.5 20.5h15") },
        { "rocket", P("M12 2.8c3.2 2.3 4.6 5.7 4.2 10.2l-2.2 3.5h-4l-2.2-3.5C7.4 8.5 8.8 5.1 12 2.8z", C(12, 9, 1.6f),
            "M7.8 13.3L5 15.8l1.2 3.5 3.8-2.8M16.2 13.3l2.8 2.5-1.2 3.5-3.8-2.8M10.5 19.5L12 21.5l1.5-2") },
        { "bolt", P("M13.5 2.5L5 13.5h6l-1 8 8.5-11h-6z") },
        { "gem", P("M6.5 4h11l3.5 5-9 11.5L3 9z", "M3 9h18M9.5 9L12 4l2.5 5M9.5 9L12 20.5 14.5 9") },
        { "dollar", P(C(12, 12, 9), CoinS) },
        { "chart", P("M3.5 3.5v17h17", "M7 15l3.5-4.5 3 3L19 6", "M15 6h4v4") },
        { "dice", P(R(3.5f, 3.5f, 17, 17, 3.5f), "M8.5 8.5h.01M15.5 8.5h.01M12 12h.01M8.5 15.5h.01M15.5 15.5h.01") },
        { "cards", P(R(3.5f, 6.5f, 10, 14, 1.8f), R(10.5f, 3.5f, 10, 14, 1.8f), "M15.5 8v4M13.5 10h4") },
        {
            "clover", new[]
            {
                new Part(CloverLeaf), new Part(CloverLeaf, 90), new Part(CloverLeaf, 180), new Part(CloverLeaf, 270),
            }
        },
        { "horseshoe", P("M6.5 20.5V11a5.5 5.5 0 0 1 11 0v9.5", "M4.5 20.5h4M15.5 20.5h4", "M6.5 14h.01M17.5 14h.01M7.4 9.2h.01M16.6 9.2h.01") },
        { "flame", P("M12 21.5c-3.9 0-6.5-2.6-6.5-6.3 0-3.3 2.2-5.1 3.4-7.6.4 1.6 1.3 2.6 2.4 3 .2-3.2 1.7-5.6 4.2-7.6-.3 3 .5 5.2 2 7.2 1 1.4 1.5 2.8 1.5 4.5 0 4.2-2.9 6.8-7 6.8z") },
        { "leaf", P("M5 19C5 10 10.5 4.5 20 4.5c0 9.5-5.5 15-15 14.5z", "M5 19l9-9") },
        { "anchor", P(C(12, 5, 1.8f), "M12 6.8V21M8 10.5h8", "M4.5 13.5c0 4.1 3.4 7.5 7.5 7.5s7.5-3.4 7.5-7.5M4.5 13.5L3 15.5M4.5 13.5l2 1.3M19.5 13.5l1.5 2M19.5 13.5l-2 1.3") },
        { "globe", P(C(12, 12, 9), GlobeLines) },
        { "key", P(C(8, 15.5f, 4), "M10.9 12.6L19.5 4M16.5 7L19 9.5M14 9.5l2 2") },
        { "tower", P("M8 21V6l4-3 4 3v15M4.5 21h15", "M10.5 9h3M10.5 12h3M10.5 15h3") },
        { "heart", P("M12 20s-8-5.2-8-11.1C4 6.2 6 4.5 8.3 4.5c1.6 0 2.9.9 3.7 2.2.8-1.3 2.1-2.2 3.7-2.2C18 4.5 20 6.2 20 8.9 20 14.8 12 20 12 20z") },
        { "gear", P(Gear(12, 12, 9.2f, 7f, 8), C(12, 12, 3)) },
        {
            "atom", new[]
            {
                new Part(C(12, 12, 1.3f)), new Part(E(12, 12, 9.5f, 3.6f)), new Part(E(12, 12, 9.5f, 3.6f), 60),
                new Part(E(12, 12, 9.5f, 3.6f), 120),
            }
        },
        { "mountain", P("M2.5 19.5l6-10 4 6 2.5-3.5 6.5 7.5z", "M6.8 12.3L8.5 14l1.8-1.5") },
        { "sun", P(C(12, 12, 4), "M12 2.5V5M12 19v2.5M2.5 12H5M19 12h2.5M5.3 5.3l1.8 1.8M16.9 16.9l1.8 1.8M5.3 18.7l1.8-1.8M16.9 7.1l1.8-1.8") },
        { "moon", P("M20 14.5A8.5 8.5 0 1 1 9.5 4a7 7 0 0 0 10.5 10.5z") },
        { "drop", P("M12 3s-6.5 7.2-6.5 11.5a6.5 6.5 0 0 0 13 0C18.5 10.2 12 3 12 3z", "M9 15a3 3 0 0 0 3 3") },
        { "factory", P("M3 20.5V11l5 3v-3l5 3v-3l5 3V4.5h3v16z", "M2 20.5h20M7 17.5h2M12 17.5h2") },
        { "plane", P("M21 3L3 10.5l7 2.5 2.5 7z", "M10 13L21 3") },
        { "briefcase", P(R(3, 7, 18, 13, 2), "M8.5 7V5a1.5 1.5 0 0 1 1.5-1.5h4A1.5 1.5 0 0 1 15.5 5v2M3 12.5h18M12 11v3") },
        { "bell", P("M6 16.5V11a6 6 0 0 1 12 0v5.5l1.5 2h-15z", "M10 20.5a2 2 0 0 0 4 0M12 3v2") },
        { "trophy", P(TrophyCup, TrophyRest) },
        { "eye", P("M2.5 12s3.5-6.5 9.5-6.5 9.5 6.5 9.5 6.5-3.5 6.5-9.5 6.5S2.5 12 2.5 12z", C(12, 12, 3)) },
        { "spade", P("M12 3.5S4.5 9 4.5 13.3c0 2.3 1.8 3.9 3.9 3.9 1.5 0 2.7-.8 3.2-1.9-.3 2.2-1.2 3.9-2.6 5.2h6c-1.4-1.3-2.3-3-2.6-5.2.5 1.1 1.7 1.9 3.2 1.9 2.1 0 3.9-1.6 3.9-3.9C19.5 9 12 3.5 12 3.5z") },
        { "chip", P(C(12, 12, 9), C(12, 12, 4.8f), "M12 3v3M12 18v3M3 12h3M18 12h3M5.6 5.6l2.1 2.1M16.3 16.3l2.1 2.1M5.6 18.4l2.1-2.1M16.3 7.7l2.1-2.1") },
        { "pin", P(Pin, C(12, 9.5f, 2.5f)) },
        { "tag", P(Tag, C(7.5f, 7.5f, 1.5f)) },
        { "skyline", P("M2.5 20.5h19M4 20.5v-9h4v9M8 20.5V6h5v14.5M13 20.5v-11h4v11M17 20.5v-6h3v6", "M10 9h1M10 12h1M10 15h1") },
        { "coffee", P("M5 8.5h11v6a5 5 0 0 1-5 5h-1a5 5 0 0 1-5-5z", "M16 10h1.5a2.5 2.5 0 0 1 0 5H16M8 3.5c-.8 1 .8 2 0 3M11.5 3.5c-.8 1 .8 2 0 3M4 21.5h13") },
        {
            "paw", P(C(6.5f, 10, 1.7f), C(9.8f, 6.2f, 1.7f), C(14.2f, 6.2f, 1.7f), C(17.5f, 10, 1.7f),
                "M12 11.5c-2.8 0-5.5 3.6-5.5 6 0 1.6 1.3 2.6 2.8 2.6 1.1 0 1.8-.6 2.7-.6s1.6.6 2.7.6c1.5 0 2.8-1 2.8-2.6 0-2.4-2.7-6-5.5-6z")
        },
        { "note", P("M9 18.5V5.5l11-2v12.5M9 9l11-2", C(6.5f, 18.5f, 2.5f), C(17.5f, 16, 2.5f)) },
        { "wave", P("M2.5 8c2 0 2-2 4-2s2 2 4 2 2-2 4-2 2 2 4 2 2-2 3-2M2.5 13c2 0 2-2 4-2s2 2 4 2 2-2 4-2 2 2 4 2 2-2 3-2M2.5 18c2 0 2-2 4-2s2 2 4 2 2-2 4-2 2 2 4 2 2-2 3-2") },
    };

    static readonly Dictionary<string, Part[]> uiIcons = new Dictionary<string, Part[]>
    {
        { "globe", P(C(12, 12, 9), GlobeLines) },
        { "close", P("M6 6l12 12M18 6L6 18") },
        { "back", P("M20 12H5M11 6l-6 6 6 6") },
        { "pin", P(Pin, C(12, 9.5f, 2.5f)) },
        { "tag", P(Tag, C(7.5f, 7.5f, 1.5f)) },
        { "people", P(C(9, 8, 3.2f), "M3 20c0-3.5 2.7-6 6-6s6 2.5 6 6", C(17.5f, 9, 2.5f), "M16.5 14.2c2.6.4 4.5 2.6 4.5 5.8") },
        { "trophy", P(TrophyCup, TrophyRest) },
        { "warning", P("M12 3.5L2.5 20h19z", "M12 10v4.5M12 17.5v.01") },
        { "coin", P(C(12, 12, 9), CoinS) },
        { "cards", P(R(3.5f, 6.5f, 10, 14, 1.8f), R(10.5f, 3.5f, 10, 14, 1.8f)) },
        { "lock", P(R(4.5f, 10.5f, 15, 10, 2), "M8 10.5V7.5a4 4 0 0 1 8 0v3") },
        { "plus", P("M12 5v14M5 12h14") },
        { "minus", P("M5 12h14") },
        { "fit", P("M4 9V4h5M20 9V4h-5M4 15v5h5M20 15v5h-5") },
        { "mail", P(R(3, 5.5f, 18, 13, 2), "M3.5 7l8.5 6 8.5-6") },
        { "bank", P("M3 9.5L12 4l9 5.5M4.5 10v8M9 10v8M15 10v8M19.5 10v8M3 20h18") },
        { "wireIn", P("M12 3v12M7 10l5 5 5-5M4 20h16") },
        { "wireOut", P("M12 15V3M7 8l5-5 5 5M4 20h16") },
        { "check", P("M5 12.5l4.5 4.5L19 7.5") },
        { "info", P(C(12, 12, 9), "M12 11v5M12 8v.01") },
        { "building", P("M4 21V8l8-4 8 4v13M9 21v-6h6v6M3 21h18") },
        { "rent", P(C(12, 12, 9), CoinS, "M4.5 4.5l2 2") },
        { "sword", P("M14.5 3.5H20v5.5L9 20l-2.5.5-.5-2.5z", "M5 14l5 5M3.5 20.5l2-2") },
        { "hand", P("M8 11V5.5a1.5 1.5 0 0 1 3 0V11M11 10V4.5a1.5 1.5 0 0 1 3 0V10M14 10.5V6a1.5 1.5 0 0 1 3 0v8c0 4-2.5 6.5-6 6.5-2.5 0-4-1.3-5.2-3.5L4 13.5a1.5 1.5 0 0 1 2.6-1.5L8 14") },
        { "flask", P("M9 3h6M10 3v6l-5.5 9.5A1.7 1.7 0 0 0 6 21h12a1.7 1.7 0 0 0 1.5-2.5L14 9V3", "M7.5 15h9") },
        { "star", P("M12 3.5l2.6 5.4 5.9.8-4.3 4.1 1 5.8L12 16.9l-5.2 2.7 1-5.8-4.3-4.1 5.9-.8z") },
        { "shield", P("M12 3l7.5 2.8V11c0 4.8-3.2 8.5-7.5 10-4.3-1.5-7.5-5.2-7.5-10V5.8z") },
        { "clock", P(C(12, 12, 9), "M12 7v5l3.5 2") },
        { "refresh", P("M20 11a8 8 0 1 0-2.3 5.7M20 5v6h-6") },
        { "shuffle", P("M3.5 7h3.8c4.8 0 5.9 10 10.7 10h2.5M17.5 14.5L20 17l-2.5 2.5M3.5 17h3.8c1.9 0 3.1-1.5 4.1-3.3M13.1 10.3C14.1 8.5 15.3 7 17.2 7h2.8M17.5 4.5L20 7l-2.5 2.5") },
        // Drawing tools
        { "pencil", P("M4.5 19.5l1.2-4.8L16.2 4.2a2 2 0 0 1 2.8 0l.8.8a2 2 0 0 1 0 2.8L9.3 18.3z", "M14 6.5l3.5 3.5") },
        { "eraser", P("M8.5 20H20", "M4.6 14.6l8.8-8.8a2 2 0 0 1 2.8 0l3 3a2 2 0 0 1 0 2.8L12 19.8H8.8z", "M9 10.3l5.7 5.7") },
        { "bucket", P("M5.5 11.5l6-6 7 7-6 6a2 2 0 0 1-2.8 0L5.5 14.3a2 2 0 0 1 0-2.8z", "M5 12h13.5", "M20 15.5c.9 1.2 1.5 2.2 1.5 3a1.5 1.5 0 0 1-3 0c0-.8.6-1.8 1.5-3z", "M9 3l2.5 2.5") },
        { "picker", P("M13.5 6.5l4 4", "M15.5 4.5l1.2-1.2a2 2 0 0 1 2.8 2.8l-1.2 1.2", "M14.5 7.5L6 16v3h3l8.5-8.5") },
        { "mirror", P("M12 3v18", "M9 7L4 12l5 5z", "M15 7l5 5-5 5z") },
        { "undo", P("M9 14L4 9l5-5", "M4 9h10.5a5.5 5.5 0 0 1 0 11H11") },
        { "redo", P("M15 14l5-5-5-5", "M20 9H9.5a5.5 5.5 0 0 0 0 11H13") },
        { "trash", P("M4 7h16", "M10 11v6M14 11v6", "M6 7l1 13h10l1-13", "M9 7V4h6v3") },
        { "flag", P("M5 21V4", "M5 4h11l-2 4 2 4H5") },
        { "grid", P(R(3, 3, 18, 18, 2), "M9 3v18M15 3v18M3 9h18M3 15h18") },
        { "line", P("M5 19L19 5", C(5, 19, 1.2f), C(19, 5, 1.2f)) },
        { "box", P(R(4.5f, 4.5f, 15, 15, 1.5f)) },
        { "ring", P(C(12, 12, 7.5f)) },
        { "letters", P("M4 19l5-14 5 14", "M5.8 14h6.4", "M15.5 10.5h4.5v8.5", "M15.5 14.5h4.5") },
    };

    static Part[] P(params string[] paths)
    {
        var parts = new Part[paths.Length];
        for (int i = 0; i < paths.Length; i++) parts[i] = new Part(paths[i]);
        return parts;
    }

    /// <summary>A cog outline: teeth alternating between two radii.</summary>
    static string Gear(float cx, float cy, float outer, float inner, int teeth)
    {
        var points = new List<Vector2>();
        int steps = teeth * 4;
        for (int i = 0; i < steps; i++)
        {
            float angle = (i / (float)steps) * Mathf.PI * 2f - Mathf.PI / 2f;
            float r = (i % 4 == 1 || i % 4 == 2) ? outer : inner;
            points.Add(new Vector2(cx + Mathf.Cos(angle) * r, cy + Mathf.Sin(angle) * r));
        }
        return CitySvg.Polygon(points);
    }

    public static bool HasLogoIcon(string id) => id != null && logoIcons.ContainsKey(id);

    /// <summary>Every drawing by id (for previews and checks).</summary>
    public static IEnumerable<KeyValuePair<string, Part[]>> LogoIconParts => logoIcons;
    public static IEnumerable<KeyValuePair<string, Part[]>> UiIconParts => uiIcons;
    public static string LogoShapePath(string id)
    {
        LogoShape(id);
        return shapes[id ?? ""];
    }

    public static void DrawLogoIcon(Painter2D p, string id, Rect rect, Color color, float pen = 2.4f)
    {
        if (id == null || !logoIcons.TryGetValue(id, out var parts)) return;
        foreach (var part in parts)
            CitySvg.Stroke(p, CitySvg.Parse(part.Path), rect, color, pen, 24f, part.Rotate);
    }

    public static void DrawUiIcon(Painter2D p, string id, Rect rect, Color color, float pen = 2.4f)
    {
        if (id == null) return;
        if (!uiIcons.TryGetValue(id, out var parts) && !logoIcons.TryGetValue(id, out parts)) return;
        foreach (var part in parts)
            CitySvg.Stroke(p, CitySvg.Parse(part.Path), rect, color, pen, 24f, part.Rotate);
    }

    // ---- Logo background shapes (100x100) ------------------------------------------------------------------------

    static readonly Dictionary<string, string> shapes = new Dictionary<string, string>();

    public static CitySvg.Shape LogoShape(string id)
    {
        if (!shapes.TryGetValue(id ?? "", out var path))
        {
            path = BuildShape(id);
            shapes[id ?? ""] = path;
        }
        return CitySvg.Parse(path);
    }

    static string BuildShape(string id)
    {
        switch (id)
        {
            case "square":
                return R(6, 6, 88, 88, 20);
            case "shield":
                return "M50 4L91 16V46C91 72 73 88 50 96 27 88 9 72 9 46V16Z";
            case "hexagon":
            {
                var pts = new List<Vector2>();
                for (int i = 0; i < 6; i++)
                {
                    float a = Mathf.PI / 3f * i - Mathf.PI / 2f;
                    pts.Add(new Vector2(50 + Mathf.Cos(a) * 47f, 50 + Mathf.Sin(a) * 47f));
                }
                return CitySvg.Polygon(pts);
            }
            case "diamond":
                return "M50 3L97 50 50 97 3 50Z";
            case "badge":
            {
                // Scalloped seal
                var pts = new List<Vector2>();
                int bumps = 14, steps = bumps * 6;
                for (int i = 0; i < steps; i++)
                {
                    float a = i / (float)steps * Mathf.PI * 2f;
                    float r = 44f + 3.5f * Mathf.Cos(a * bumps);
                    pts.Add(new Vector2(50 + Mathf.Cos(a) * r, 50 + Mathf.Sin(a) * r));
                }
                return CitySvg.Polygon(pts);
            }
            default:
                return C(50, 50, 46);
        }
    }
}
