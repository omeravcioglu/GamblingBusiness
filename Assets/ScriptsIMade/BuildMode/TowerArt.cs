using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>Colour tokens of the tower drawings ("ink" and "bg" are the room's own colours).</summary>
public enum TowerInk
{
    None,
    Ink,        // the room's ink (or the icon's colour)
    Bg,         // the room's paper
    Paper,      // #FFFDF6
    Navy,       // #1D2433
    Gold,
    Green,
    Red,
    Blue,
    Sky,
    Terra,
    Hi,
    LGreen,
    LRed,
    Grey,       // #474F60
    Shade,      // #EAE2CF
    Wood,       // #E7C98C
    Leaf,       // #CDEBA6
    Pink,       // #FFC7D1
    Brown,      // #8B5E3C
    Pencil,     // #676B74
    ShirtA,
    ShirtB,
    HairA,
    HairB
}

/// <summary>One part of a drawing: an SVG path filled and/or stroked. Group 1/2 = the first/second person at a desk.</summary>
public struct TowerPart
{
    public string d;
    public TowerInk fill;
    public TowerInk stroke;
    public float width;
    public int group;

    public TowerPart(string d, TowerInk fill, TowerInk stroke, float width, int group = 0)
    {
        this.d = d;
        this.fill = fill;
        this.stroke = stroke;
        this.width = width;
        this.group = group;
    }
}

/// <summary>A small drawing (furniture or a figure) of the design, w x h units with (0, 0) at its top-left, y down.</summary>
public sealed class TowerShape
{
    public readonly float width;
    public readonly float height;
    public readonly TowerPart[] parts;

    public TowerShape(float width, float height, params TowerPart[] parts)
    {
        this.width = width;
        this.height = height;
        this.parts = parts;
    }
}

/// <summary>Which figures of a room are drawn: always, once there is staff, when the tower is full, or one per seated person.</summary>
public enum TowerThingSet
{
    Always,
    Staffed,
    Full,
    Seat
}

/// <summary>Something standing in a room of the tower: x from the room's left edge, y from its top (NaN = on the floor).</summary>
public struct TowerThing
{
    public string shape;
    public float x;
    public float y;
    public TowerThingSet set;
    public bool flip;
    public bool front;
    public bool figure;
}

/// <summary>Colours for one drawing call.</summary>
public struct TowerPalette
{
    public Color ink;
    public Color bg;
    public Color shirtA, shirtB, hairA, hairB;
    public float alpha;

    public static TowerPalette For(Color ink, Color bg, float alpha = 1f)
    {
        return new TowerPalette
        {
            ink = ink, bg = bg, alpha = alpha,
            shirtA = TowerArt.C(0xFFE680), shirtB = TowerArt.C(0xFFC7D1), hairA = TowerArt.C(0x1D2433), hairB = TowerArt.C(0x8B5E3C)
        };
    }
}

/// <summary>
/// The drawings of the design's "HQ tower (2D build)" boards as SVG path data: the line icons (24 x 24), the tower's
/// furniture and staff figures, what stands in each room, the room view's big drawings (desk with two people, plant,
/// coffee machine) and its back wall. TowerSvg turns the paths into polylines once (cached); Draw puts a shape into a
/// BlueprintMeshBuilder at a position, scale and flip.
/// </summary>
public static class TowerArt
{
    public static Color C(uint rgb)
    {
        return new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
    }

    static readonly Color PaperHi = C(0xFFFDF6);
    static readonly Color Navy = C(0x1D2433);

    public static Color Resolve(TowerInk token, in TowerPalette palette)
    {
        Color c;
        switch (token)
        {
            case TowerInk.Ink: c = palette.ink; break;
            case TowerInk.Bg: c = palette.bg; break;
            case TowerInk.Paper: c = PaperHi; break;
            case TowerInk.Navy: c = Navy; break;
            case TowerInk.Gold: c = C(0xE2B13C); break;
            case TowerInk.Green: c = C(0x1E7B45); break;
            case TowerInk.Red: c = C(0xC0352B); break;
            case TowerInk.Blue: c = C(0x2B5BA8); break;
            case TowerInk.Sky: c = C(0xBFE3F5); break;
            case TowerInk.Terra: c = C(0xE4A199); break;
            case TowerInk.Hi: c = C(0xFFE14D); break;
            case TowerInk.LGreen: c = C(0x7ED68E); break;
            case TowerInk.LRed: c = C(0xF08A84); break;
            case TowerInk.Grey: c = C(0x474F60); break;
            case TowerInk.Shade: c = C(0xEAE2CF); break;
            case TowerInk.Wood: c = C(0xE7C98C); break;
            case TowerInk.Leaf: c = C(0xCDEBA6); break;
            case TowerInk.Pink: c = C(0xFFC7D1); break;
            case TowerInk.Brown: c = C(0x8B5E3C); break;
            case TowerInk.Pencil: c = C(0x676B74); break;
            case TowerInk.ShirtA: c = palette.shirtA; break;
            case TowerInk.ShirtB: c = palette.shirtB; break;
            case TowerInk.HairA: c = palette.hairA; break;
            case TowerInk.HairB: c = palette.hairB; break;
            default: c = Color.clear; break;
        }
        c.a *= palette.alpha;
        return c;
    }

    // ------------------------------------------------------------------
    // Path helpers (rects and circles of the design written as paths)
    // ------------------------------------------------------------------

    static string N(float v)
    {
        return v.ToString("0.###", CultureInfo.InvariantCulture);
    }

    /// <summary>Rounded rectangle as a path (clockwise, y down).</summary>
    public static string Rect(float x, float y, float w, float h, float r = 0f)
    {
        if (r <= 0f)
            return "M" + N(x) + " " + N(y) + "h" + N(w) + "v" + N(h) + "h" + N(-w) + "z";
        r = Mathf.Min(r, Mathf.Min(w, h) * 0.5f);
        string a = "a" + N(r) + " " + N(r) + " 0 0 1 ";
        return "M" + N(x + r) + " " + N(y) + "h" + N(w - 2f * r) + a + N(r) + " " + N(r) + "v" + N(h - 2f * r) + a + N(-r) + " " + N(r)
               + "h" + N(-(w - 2f * r)) + a + N(-r) + " " + N(-r) + "v" + N(-(h - 2f * r)) + a + N(r) + " " + N(-r) + "z";
    }

    public static string Circle(float cx, float cy, float r)
    {
        return "M" + N(cx + r) + " " + N(cy) + "a" + N(r) + " " + N(r) + " 0 1 1 " + N(-2f * r) + " 0a" + N(r) + " " + N(r) + " 0 1 1 " + N(2f * r) + " 0z";
    }

    /// <summary>A rectangle turned by <paramref name="degrees"/> around its centre (square corners), y down.</summary>
    static string TurnedRect(float x, float y, float w, float h, float degrees)
    {
        float a = degrees * Mathf.Deg2Rad, cos = Mathf.Cos(a), sin = Mathf.Sin(a);
        float cx = x + w * 0.5f, cy = y + h * 0.5f;
        var s = new System.Text.StringBuilder();
        float[,] corners = { { x, y }, { x + w, y }, { x + w, y + h }, { x, y + h } };
        for (int i = 0; i < 4; i++)
        {
            float dx = corners[i, 0] - cx, dy = corners[i, 1] - cy;
            s.Append(i == 0 ? "M" : "L").Append(N(cx + dx * cos - dy * sin)).Append(' ').Append(N(cy + dx * sin + dy * cos));
        }
        return s.Append('z').ToString();
    }

    static TowerPart F(string d, TowerInk fill) => new TowerPart(d, fill, TowerInk.None, 0f);
    static TowerPart S(string d, TowerInk stroke, float width) => new TowerPart(d, TowerInk.None, stroke, width);
    static TowerPart FS(string d, TowerInk fill, TowerInk stroke, float width) => new TowerPart(d, fill, stroke, width);
    static TowerPart G(int group, string d, TowerInk fill, TowerInk stroke, float width) => new TowerPart(d, fill, stroke, width, group);

    // ------------------------------------------------------------------
    // Icons (24 x 24, stroked with the icon's colour; width 2.4 unless given)
    // ------------------------------------------------------------------

    static readonly Dictionary<TowerIconKind, TowerPart[]> icons = new Dictionary<TowerIconKind, TowerPart[]>();

    static TowerPart[] I(params string[] paths)
    {
        var parts = new TowerPart[paths.Length];
        for (int i = 0; i < paths.Length; i++) parts[i] = S(paths[i], TowerInk.Ink, 2.4f);
        return parts;
    }

    public static TowerPart[] Icon(TowerIconKind kind)
    {
        if (icons.Count == 0) BuildIcons();
        return icons.TryGetValue(kind, out TowerPart[] parts) ? parts : null;
    }

    static void BuildIcons()
    {
        icons[TowerIconKind.Home] = I("M4 11l8-7 8 7v9.5H4zM10 20.5v-6h4v6");
        icons[TowerIconKind.Briefcase] = I("M5 7h14a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V9a2 2 0 0 1 2-2zM9 7V5h6v2M3 12.5h18");
        icons[TowerIconKind.People] = I("M12.2 8a3.2 3.2 0 1 1-6.4 0a3.2 3.2 0 1 1 6.4 0zM3 20c0-3.5 2.7-6 6-6s6 2.5 6 6M20 9a2.5 2.5 0 1 1-5 0a2.5 2.5 0 1 1 5 0zM16.5 14.2c2.6.4 4.5 2.6 4.5 5.8");
        icons[TowerIconKind.Coffee] = I("M4 9h13v5a5 5 0 0 1-5 5H9a5 5 0 0 1-5-5zM17 10.5h1.5a2.5 2.5 0 0 1 0 5H17M8 3.5c-.8 1 .8 2 0 3M12 3.5c-.8 1 .8 2 0 3");
        icons[TowerIconKind.Clipboard] = I("M7 4.5h10a2 2 0 0 1 2 2V19a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6.5a2 2 0 0 1 2-2zM9 4.5V3h6v1.5M9 10h6M9 14h6M9 18h3.5");
        icons[TowerIconKind.Monitor] = I("M5 4h14a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2zM8 20h8M12 16v4");
        icons[TowerIconKind.Chart] = I("M3.5 3.5v17h17M7 15l3.5-4.5 3 3L19 6");
        icons[TowerIconKind.Dice] = new[]
        {
            S("M7 3.5h10a3.5 3.5 0 0 1 3.5 3.5v10a3.5 3.5 0 0 1-3.5 3.5H7A3.5 3.5 0 0 1 3.5 17V7A3.5 3.5 0 0 1 7 3.5z", TowerInk.Ink, 2.4f),
            S("M8.5 8.5h.01M15.5 8.5h.01M12 12h.01M8.5 15.5h.01M15.5 15.5h.01", TowerInk.Ink, 3.2f)
        };
        icons[TowerIconKind.Plus] = I("M12 5v14M5 12h14");
        icons[TowerIconKind.Minus] = I("M5 12h14");
        icons[TowerIconKind.Building] = I("M4 21V5.5l8-2.5v18M12 8l8 2.5V21M2.5 21h19M7.5 8h1.5M7.5 12h1.5M7.5 16h1.5M15.5 13h1.5M15.5 17h1.5");
        icons[TowerIconKind.Sofa] = I("M5 11V8.5A2.5 2.5 0 0 1 7.5 6h9A2.5 2.5 0 0 1 19 8.5V11M3 12.5a1.8 1.8 0 0 1 3.5 0V15h11v-2.5a1.8 1.8 0 0 1 3.5 0V18H3zM5.5 18v2M18.5 18v2");
        icons[TowerIconKind.Move] = I("M12 3v18M3 12h18M9 6l3-3 3 3M9 18l3 3 3-3M6 9l-3 3 3 3M18 9l3 3-3 3");
        icons[TowerIconKind.Tag] = I("M3 12V4.5A1.5 1.5 0 0 1 4.5 3H12l9 9-9 9zM9 7.5a1.5 1.5 0 1 1-3 0a1.5 1.5 0 1 1 3 0z");
        icons[TowerIconKind.Check] = I("M4 12.5l5 5L20 6.5");
        icons[TowerIconKind.Warning] = I("M12 3.5L2.5 20h19zM12 10v4.5M12 17.5v.01");
        icons[TowerIconKind.Lock] = I("M6.5 10.5h11a2 2 0 0 1 2 2v6a2 2 0 0 1-2 2h-11a2 2 0 0 1-2-2v-6a2 2 0 0 1 2-2zM8 10.5V7.5a4 4 0 0 1 8 0v3");
        icons[TowerIconKind.Cash] = I(Rect(2.5f, 6f, 19f, 12f, 2f), Circle(12f, 12f, 2.8f), "M6 9.5h.01M18 14.5h.01");
        icons[TowerIconKind.Cards] = I(TurnedRect(3f, 6.5f, 11f, 14.5f, -10f), Rect(10f, 3f, 11f, 14.5f, 2f));
        icons[TowerIconKind.Close] = I("M6 6l12 12M18 6L6 18");
        icons[TowerIconKind.ArrowRight] = I("M4 12h15M13 6l6 6-6 6");
        icons[TowerIconKind.ArrowLeft] = I("M20 12H5M11 6l-6 6 6 6");
        icons[TowerIconKind.TrendUp] = I("M3 17l6-6 4 4 8-8M15 7h6v6");
        icons[TowerIconKind.TrendDown] = I("M3 7l6 6 4-4 8 8M15 17h6v-6");
        icons[TowerIconKind.Coin] = I(Circle(12f, 12f, 9f), "M14.6 9.2c-.5-.8-1.5-1.3-2.6-1.3-1.5 0-2.7.8-2.7 2 0 2.8 5.5 1.5 5.5 4.3 0 1.2-1.2 2-2.8 2-1.2 0-2.3-.5-2.9-1.4M12 6.5v1.4M12 16.9v1.6");
        icons[TowerIconKind.Sparkle] = new[]
        {
            S("M12 3c.6 4.6 2.4 6.4 7 7-4.6.6-6.4 2.4-7 7-.6-4.6-2.4-6.4-7-7 4.6-.6 6.4-2.4 7-7zM19 16.5v4M17 18.5h4", TowerInk.Ink, 2.2f)
        };
        icons[TowerIconKind.ChevronUp] = I("M6 15l6-6 6 6");
        icons[TowerIconKind.ChevronDown] = I("M6 9l6 6 6-6");
        icons[TowerIconKind.Pin] = I("M12 21s-7-6.2-7-11.5a7 7 0 0 1 14 0C19 14.8 12 21 12 21zM14.5 9.5a2.5 2.5 0 1 1-5 0a2.5 2.5 0 1 1 5 0z");
        icons[TowerIconKind.Pause] = I("M8.5 5v14M15.5 5v14");
        icons[TowerIconKind.Desk] = I("M2.5 12.5h19M4.5 12.5V20M19.5 12.5V20M8.5 4.5h7a1 1 0 0 1 1 1v4a1 1 0 0 1-1 1h-7a1 1 0 0 1-1-1v-4a1 1 0 0 1 1-1zM12 10.5v2");
        icons[TowerIconKind.Plant] = I("M8 15h8l-1.2 6H9.2zM12 15V9.5M12 11.5c-3.2 0-5-1.9-5-5.2 3.2 0 5 1.9 5 5.2zM12 9.5c0-3.4 1.9-5.5 5.2-5.5 0 3.4-1.9 5.5-5.2 5.5z");
        icons[TowerIconKind.Bookshelf] = I("M5 3h14v18H5zM5 9h14M5 15h14M8.5 3v6M12 9v6M15.5 15v6");
        icons[TowerIconKind.Fish] = I("M3.5 12c2.5-3.5 8-4.5 12 0-4 4.5-9.5 3.5-12 0zM15.5 12l5-3.5v7zM8 11h.01");
        icons[TowerIconKind.Reception] = I("M2 11h20M4 11v9h16v-9M14 4h6v4h-6zM17 8v3");
        icons[TowerIconKind.Arcade] = I("M7 21V4.5A1.5 1.5 0 0 1 8.5 3h7A1.5 1.5 0 0 1 17 4.5V21M7 8h10M9.5 10h5v4h-5zM10 17h4");
        icons[TowerIconKind.Table] = I("M2 10h20M5 10v9M19 10v9M9 10l1-4h4l1 4");
        icons[TowerIconKind.Whiteboard] = I("M3 4h18v12H3zM7 20l5-4 5 4M6 12l3-3 3 2 4-4");
        icons[TowerIconKind.Projector] = I("M4 8h16v8H4zM12 3v5M10 12h6M7 12h.01");
        icons[TowerIconKind.ServerRack] = I("M6 3h12v18H6zM6 9h12M6 15h12M9 6h4M9 12h4M9 18h4");
        icons[TowerIconKind.Cooler] = I("M4 4h16v16H4zM12 8v8M8 12h8");
        icons[TowerIconKind.Terminal] = I("M3 5h8v6H3zM13 5h8v6h-8zM2 14h20M5 14v6M19 14v6");
        icons[TowerIconKind.Ticker] = I("M2 7h20v10H2zM5 14l3-4 3 3 3-4 3 5");
        icons[TowerIconKind.SlotMachine] = I("M5 9h14v12H5zM6 9a6 5 0 0 1 12 0M8 12h8v4H8zM19 11h2v5");
        icons[TowerIconKind.Roulette] = I(Circle(12f, 12f, 9f), Circle(12f, 12f, 3f), "M12 3v6M12 15v6M3 12h6M15 12h6");
        icons[TowerIconKind.CardTable] = I("M2 11h20M12 11v8M8 19h8M7 5h4v5H7zM13 5h4v5h-4z");
        icons[TowerIconKind.Bar] = I("M4 12h16v8H4zM2 12h20M7 4h2v6H7zM11 3h2v7h-2zM15 5h2v5h-2z");
        icons[TowerIconKind.Person] = new[] { F("M9 3.4a3 3 0 1 1-6 0a3 3 0 1 1 6 0zM1.5 10.4c0-2 1.8-3.2 4.5-3.2s4.5 1.2 4.5 3.2V17h-9z", TowerInk.Ink) };
        // The campus (2026-09-29)
        icons[TowerIconKind.Treadmill] = I("M3 18h13l4-9h2M5 18v2.5M14 18v2.5M9.5 4.5a1.8 1.8 0 1 1 0 .01M9.5 7.5v4.5l-2.5 3.5M9.5 12l2.5 2.5M9.5 9l2.5-1.5");
        icons[TowerIconKind.Dumbbell] = I("M6.5 7.5v9M3.5 10v4M17.5 7.5v9M20.5 10v4M6.5 12h11");
        icons[TowerIconKind.Food] = I(Circle(12.5f, 12.5f, 5f), "M4 3.5v6M6.5 3.5v6M4 7.5h2.5M5.2 9.5v11M20 3.5c-1.8.8-2.8 3-2.8 5.5h2.8M20 3.5v17");
        icons[TowerIconKind.Cabinet] = I("M5 3h14v18H5zM5 9h14M5 15h14M10 6h4M10 12h4M10 18h4");
        icons[TowerIconKind.Flask] = I("M9 3h6M10 3v6l-5.4 9.3A1.8 1.8 0 0 0 6.2 21h11.6a1.8 1.8 0 0 0 1.6-2.7L14 9V3M7.2 15h9.6");
        icons[TowerIconKind.Cubicle] = I("M3 8h6v12M15 8h6v12M3 14h6M15 14h6M11 13h2v4h-2zM9 17h6");
        icons[TowerIconKind.Trophy] = I("M8 4h8v5a4 4 0 0 1-8 0zM8 6H5a3 3 0 0 0 3 4M16 6h3a3 3 0 0 1-3 4M12 13v4M9.5 17h5v3h-5zM8 20h8");
        icons[TowerIconKind.Helicopter] = I("M4 4.5h16M12 4.5v3M7 12a4 4 0 0 1 4-4.5h4a4.5 4.5 0 0 1 0 9H9a2 2 0 0 1-2-2zM2 11h5M8 20h9M10 16.5V20M15 16.5V20M15 7.5v4.5h4.3");
        icons[TowerIconKind.Parasol] = I("M3 11a9 7 0 0 1 18 0zM12 4v17M9 21h6");
        icons[TowerIconKind.Scales] = I("M12 3v18M8 21h8M5 7h14M5 7l-3 6.5a3 3 0 0 0 6 0zM19 7l-3 6.5a3 3 0 0 0 6 0z");
        icons[TowerIconKind.Star] = I("M12 3l2.6 5.6 6.1.7-4.5 4.2 1.2 6L12 16.6l-5.4 2.9 1.2-6-4.5-4.2 6.1-.7z");
        icons[TowerIconKind.Campus] = I("M2 21h20M4 21V11h5v10M9 21V5h6v16M15 21V9h5v12M11 8h2M11 12h2M11 16h2");
        icons[TowerIconKind.Bridge] = I("M2 9.5h20M2 14.5h20M2 9.5v5M22 9.5v5M7 9.5v5M12 9.5v5M17 9.5v5M4 19.5v-5M20 19.5v-5");
        icons[TowerIconKind.Upgrade] = I("M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2zM12 17V7.5M8 11.5l4-4 4 4");
        icons[TowerIconKind.IdBadge] = I("M6 4h12v17H6zM10 4V2.5h4V4M14 10.5a2 2 0 1 1-4 0a2 2 0 1 1 4 0zM8.5 17.5c0-2 1.6-3.2 3.5-3.2s3.5 1.2 3.5 3.2");
    }

    public static TowerIconKind IconFor(TowerRoomRole role)
    {
        switch (role)
        {
            case TowerRoomRole.Lobby: return TowerIconKind.Home;
            case TowerRoomRole.CeoOffice: return TowerIconKind.Briefcase;
            case TowerRoomRole.BreakRoom: return TowerIconKind.Coffee;
            case TowerRoomRole.MeetingRoom: return TowerIconKind.Clipboard;
            case TowerRoomRole.ServerRoom: return TowerIconKind.Monitor;
            case TowerRoomRole.TradingFloor: return TowerIconKind.Chart;
            case TowerRoomRole.CasinoRoom: return TowerIconKind.Dice;
            case TowerRoomRole.StarterOffice: return TowerIconKind.Star;
            case TowerRoomRole.CubicleFarm: return TowerIconKind.Cubicle;
            case TowerRoomRole.Cafeteria: return TowerIconKind.Food;
            case TowerRoomRole.Gym: return TowerIconKind.Dumbbell;
            case TowerRoomRole.ExecutiveSuite: return TowerIconKind.Briefcase;
            case TowerRoomRole.BigMeetingRoom: return TowerIconKind.Table;
            case TowerRoomRole.Marketing: return TowerIconKind.TrendUp;
            case TowerRoomRole.HrDepartment: return TowerIconKind.IdBadge;
            case TowerRoomRole.LegalDepartment: return TowerIconKind.Scales;
            case TowerRoomRole.Accounting: return TowerIconKind.Coin;
            case TowerRoomRole.ResearchLab: return TowerIconKind.Flask;
            case TowerRoomRole.TrainingCenter: return TowerIconKind.Bookshelf;
            case TowerRoomRole.DataCenter: return TowerIconKind.ServerRack;
            case TowerRoomRole.RooftopLounge: return TowerIconKind.Parasol;
            case TowerRoomRole.Helipad: return TowerIconKind.Helicopter;
            case TowerRoomRole.TrophyHall: return TowerIconKind.Trophy;
            default: return TowerIconKind.People;
        }
    }

    public static TowerIconKind IconFor(TowerFurnitureArt art)
    {
        switch (art)
        {
            case TowerFurnitureArt.Desk: return TowerIconKind.Desk;
            case TowerFurnitureArt.Plant: return TowerIconKind.Plant;
            case TowerFurnitureArt.CoffeeMachine: return TowerIconKind.Coffee;
            case TowerFurnitureArt.Sofa: return TowerIconKind.Sofa;
            case TowerFurnitureArt.Bookshelf: return TowerIconKind.Bookshelf;
            case TowerFurnitureArt.Aquarium: return TowerIconKind.Fish;
            case TowerFurnitureArt.ReceptionDesk: return TowerIconKind.Reception;
            case TowerFurnitureArt.ArcadeCabinet: return TowerIconKind.Arcade;
            case TowerFurnitureArt.BigTable: return TowerIconKind.Table;
            case TowerFurnitureArt.Whiteboard: return TowerIconKind.Whiteboard;
            case TowerFurnitureArt.Projector: return TowerIconKind.Projector;
            case TowerFurnitureArt.ServerRack: return TowerIconKind.ServerRack;
            case TowerFurnitureArt.CoolingUnit: return TowerIconKind.Cooler;
            case TowerFurnitureArt.Terminal: return TowerIconKind.Terminal;
            case TowerFurnitureArt.TickerBoard: return TowerIconKind.Ticker;
            case TowerFurnitureArt.SlotMachine: return TowerIconKind.SlotMachine;
            case TowerFurnitureArt.CardTable: return TowerIconKind.CardTable;
            case TowerFurnitureArt.RouletteWheel: return TowerIconKind.Roulette;
            case TowerFurnitureArt.Treadmill: return TowerIconKind.Treadmill;
            case TowerFurnitureArt.Weights: return TowerIconKind.Dumbbell;
            case TowerFurnitureArt.FoodCounter: return TowerIconKind.Food;
            case TowerFurnitureArt.FilingCabinet: return TowerIconKind.Cabinet;
            case TowerFurnitureArt.LabBench: return TowerIconKind.Flask;
            case TowerFurnitureArt.Cubicle: return TowerIconKind.Cubicle;
            case TowerFurnitureArt.Trophy: return TowerIconKind.Trophy;
            case TowerFurnitureArt.Helicopter: return TowerIconKind.Helicopter;
            case TowerFurnitureArt.Parasol: return TowerIconKind.Parasol;
            default: return TowerIconKind.Bar;
        }
    }

    // ------------------------------------------------------------------
    // The tower's furniture and staff (1 unit = 1 px at 1920 x 1080)
    // ------------------------------------------------------------------

    const string Stand = "M10.8 4.3a3.8 3.8 0 1 1-7.6 0a3.8 3.8 0 1 1 7.6 0zM3.2 11.8c0-2 1.4-3.1 3.8-3.1s3.8 1.1 3.8 3.1v7.4H9.6V28H7.6v-7.6H6.4V28H4.4v-8.8H3.2z";
    const string Sit = "M12.3 4.2a3.8 3.8 0 1 1-7.6 0a3.8 3.8 0 1 1 7.6 0zM5 11.6c0-1.8 1.3-2.8 3.5-2.8s3.5 1 3.5 2.8v3.9h4c1 0 1.6.6 1.6 1.6V26h-2.2v-7.4H6.6c-1 0-1.6-.6-1.6-1.6z";
    const string Arm = "M10.8 11.6l6.2 1.7-.4 1.6-6.2-1.7z";

    static readonly Dictionary<string, TowerShape> shapes = new Dictionary<string, TowerShape>();

    public static TowerShape Shape(string key)
    {
        if (shapes.Count == 0) BuildShapes();
        return key != null && shapes.TryGetValue(key, out TowerShape shape) ? shape : null;
    }

    static void BuildShapes()
    {
        TowerInk ink = TowerInk.Ink;
        shapes["stand"] = new TowerShape(14, 28, F(Stand, ink));
        shapes["cv"] = new TowerShape(18, 28, F(Stand, ink), FS("M10.5 12.5h6.5v8h-6.5z", TowerInk.Paper, TowerInk.Navy, 1.4f),
                                      S("M12.3 15.2h3M12.3 17.8h3", TowerInk.Navy, 1.2f));
        shapes["point"] = new TowerShape(18, 28, F("M14.8 4.3a3.8 3.8 0 1 1-7.6 0a3.8 3.8 0 1 1 7.6 0zM7.2 11.8c0-2 1.4-3.1 3.8-3.1s3.8 1.1 3.8 3.1v7.4h-1.2V28h-2v-7.6h-1.2V28h-2v-8.8H7.2zM8.3 10.9L2.1 6.6l-.9 1.4 6.2 4.3z", ink));
        shapes["sit"] = new TowerShape(20, 26, F(Sit + Arm, ink), S("M2.6 10.4v9.2M2.6 19.6h9.4M4.6 19.6v5.6M10 19.6v5.6", ink, 2f));
        shapes["ceo"] = new TowerShape(20, 26, F(Sit + Arm, ink), S("M2.6 4.4v15.2M1.2 4.4h2.8M2.6 19.6h9.4M4.6 19.6v5.6M10 19.6v5.6", ink, 2f));
        shapes["lounge"] = new TowerShape(20, 26, F(Sit, ink));
        shapes["desk"] = new TowerShape(44, 24, F("M16 3h12v5H16z", TowerInk.Paper), S("M2 11h40M5 11v12M39 11v12M15 2h14v7H15zM22 9v2", ink, 2f));
        shapes["plant"] = new TowerShape(20, 30,
            F("M10 16C6.5 15.5 4 12.5 4 9c3.5.5 6 3.5 6 7zM10 13c3.5-.5 6-3.5 6-7-3.5.5-6 3.5-6 7zM10 9C8.5 7 8.5 4 10 2c1.5 2 1.5 5 0 7z", TowerInk.Green),
            F("M5 21h10l-1.5 8h-7z", TowerInk.Terra),
            S("M10 21V9M5 21h10l-1.5 8h-7zM10 16C6.5 15.5 4 12.5 4 9c3.5.5 6 3.5 6 7M10 13c3.5-.5 6-3.5 6-7-3.5.5-6 3.5-6 7M10 9C8.5 7 8.5 4 10 2c1.5 2 1.5 5 0 7", ink, 1.8f));
        shapes["coffee"] = new TowerShape(24, 32, F("M3 2h18v29H3z", TowerInk.Paper), F("M15 4.5h3v2h-3z", TowerInk.Red),
            S("M3 2h18v29H3zM3 9h18M12 9v3.5M9 15.5h6v3c0 1.2-.9 2-3 2s-3-.8-3-2zM6 24h12M6.5 5.5h.01M10 5.5h.01", ink, 2f));
        shapes["reception"] = new TowerShape(56, 26, F("M3 11h50v14H3z", TowerInk.Bg), F("M37 4h10v5H37z", TowerInk.Paper),
            S("M1 11h54M3 11v14h50V11M36 3h12v7H36zM42 10v1M9 16h16", ink, 2f));
        shapes["clock"] = new TowerShape(22, 22, FS("M20 11a9 9 0 1 1-18 0a9 9 0 1 1 18 0z", TowerInk.Paper, ink, 2f), S("M11 6v5l3.5 2", ink, 2f));
        shapes["bookshelf"] = new TowerShape(36, 66, F("M7 8h3v9H7zM20 22h3v10h-3z", TowerInk.Blue), F("M13 9h3v8h-3zM9 37h3v10H9z", TowerInk.Red),
            S("M2 2h32v63H2zM2 17h32M2 32h32M2 47h32M18 47v18M14 56h1M22 56h1", ink, 2f),
            S("M7 17V8h3v9M10 17V6h3v11M13 17V9h3v8M24 17l3-9 2.5 1-3 8M6 32V22h3v10M9 32V21h3v11M20 32V22h3v10M23 32V23h3v9M9 47V37h3v10M12 47V36h3v11M22 47l-3-9 2.5-1 3 9", ink, 1.4f));
        shapes["ceodesk"] = new TowerShape(56, 24, F("M9 5h8l-1.8-4h-4.4z", TowerInk.Gold),
            S("M1 9h54M3 9v14M53 9v14M36 9v14M36 16h17M43 12.5h3M43 19.5h3M13 9V5M9 5h8l-1.8-4h-4.4zM22 9V6.5h9V9", ink, 2f));
        shapes["sofa"] = new TowerShape(60, 24,
            F("M7 13V7.5A4.5 4.5 0 0 1 11.5 3h37A4.5 4.5 0 0 1 53 7.5V13zM2 15a3 3 0 0 1 6 0v3h44v-3a3 3 0 0 1 6 0v5H2z", TowerInk.Paper),
            S("M7 13V7.5A4.5 4.5 0 0 1 11.5 3h37A4.5 4.5 0 0 1 53 7.5V13M2 15a3 3 0 0 1 6 0v3h44v-3a3 3 0 0 1 6 0v5H2zM6 20v3M54 20v3", ink, 2f));
        shapes["frame"] = new TowerShape(32, 26, FS("M2 2h28v22H2z", TowerInk.Paper, ink, 2f), S("M6 19l6-6 4 3 9-9M20 7h5v5", TowerInk.Green, 2f));
        shapes["aquarium"] = new TowerShape(48, 42, F("M3 8h42v17H3z", TowerInk.Sky),
            FS("M15 16c3-3 7-3 9 0-2 3-6 3-9 0zM24 16l3.5-2.5v5z", TowerInk.Gold, TowerInk.Navy, 1.2f),
            S("M3 3h42v22H3zM3 8h42M8 25v16M40 25v16M8 33h32M31 22c1.5-2 1.5-3.5 0-5.5", ink, 2f),
            S("M34 14h.01M37 11h.01", TowerInk.Navy, 2f));
        shapes["arcade"] = new TowerShape(26, 44, F("M3 9V4.5A2.5 2.5 0 0 1 5.5 2h15A2.5 2.5 0 0 1 23 4.5V9z", TowerInk.Red), F("M6 12h14v10H6z", TowerInk.Hi),
            S("M3 43V4.5A2.5 2.5 0 0 1 5.5 2h15A2.5 2.5 0 0 1 23 4.5V43zM3 9h20M6 12h14v10H6zM3 26h20M9 26v-3.5M15.5 25h.01M19 25h.01M10.5 34h5", ink, 2f));
        shapes["table"] = new TowerShape(100, 20, S("M2 8h96M9 8v11M91 8v11M24 8l1.5-5h9M76 8l-1.5-5h-9M46 6.5h8", ink, 2f));
        shapes["whiteboard"] = new TowerShape(60, 40, FS("M2 2h56v30H2z", TowerInk.Paper, ink, 2f), S("M6 32v3h48v-3", ink, 2f),
            S("M8 10c3-3 6 2 9 0s6-3 9 0M8 18h18M8 24h11", TowerInk.Blue, 1.8f), S("M34 26l5-7 4 3 8-10", TowerInk.Red, 1.8f));
        shapes["projector"] = new TowerShape(26, 22, F("M4 7h18v9H4z", TowerInk.Paper), S("M13 0v7M4 7h18v9H4zM12 11.5h6M4 10L.5 8.5M4 13L.5 14.5", ink, 2f));
        shapes["rack"] = new TowerShape(30, 60, F("M3 2h24v57H3z", TowerInk.Paper),
            F("M20 6h3v2.5h-3zM20 16h3v2.5h-3zM20 26h3v2.5h-3zM20 36h3v2.5h-3zM20 46h3v2.5h-3z", TowerInk.Green),
            S("M3 2h24v57H3zM3 12h24M3 22h24M3 32h24M3 42h24M3 52h24", ink, 2f), S("M7 7.2h9M7 17.2h9M7 27.2h9M7 37.2h9M7 47.2h9", ink, 1.4f));
        shapes["cooler"] = new TowerShape(36, 36, F("M3 3h30v29H3z", TowerInk.Paper),
            S("M3 3h30v29H3zM26 17.5a8 8 0 1 1-16 0a8 8 0 1 1 16 0zM18 10.5v14M11 17.5h14M7 32v3M29 32v3", ink, 2f), S("M7 7h5M24 7h5", TowerInk.Blue, 1.6f));
        shapes["terminal"] = new TowerShape(46, 30, F("M4 2h17v10H4zM25 2h17v10H25z", TowerInk.Paper),
            S("M2 15h42M6 15v14M40 15v14M4 2h17v10H4zM25 2h17v10H25zM12.5 12v3M33.5 12v3", ink, 2f),
            S("M6.5 9.5l3.5-3 3 2 5-4", TowerInk.Green, 1.6f), S("M27.5 4.5l4 4 3-2 5 3.5", TowerInk.Red, 1.6f));
        shapes["ticker"] = new TowerShape(150, 26, F("M1 1h148v24H1z", TowerInk.Navy), F("M20 10.5h16v5H20zM56 10.5h12v5H56zM94 10.5h18v5H94zM130 10.5h12v5h-12z", TowerInk.Paper),
            F("M7 16l5-8 5 8zM81 16l5-8 5 8z", TowerInk.LGreen), F("M42 8l5 8 5-8zM117 8l5 8 5-8z", TowerInk.LRed));
        shapes["slot"] = new TowerShape(28, 44, F("M5 9a9 7 0 0 1 18 0z", TowerInk.Gold), F("M7 14h14v10H7z", TowerInk.Paper),
            F("M9 17.5h2.5v3H9zM12.8 17.5h2.5v3h-2.5zM16.5 17.5h2.5v3h-2.5zM26.5 9a1.6 1.6 0 1 1 0 3.2a1.6 1.6 0 1 1 0-3.2z", TowerInk.Red),
            S("M4 9h20v34H4zM5 9a9 7 0 0 1 18 0M7 30h14M24 16h2.5v8M26.5 16v-3.5", ink, 2f));
        shapes["roulette"] = new TowerShape(44, 28, F("M7 10a15 5 0 1 0 30 0a15 5 0 1 0-30 0z", TowerInk.Gold), S("M12 8.5l3 3M22 5.2v9.6M32 8.5l-3 3", TowerInk.Navy, 1.4f),
            S("M3 16h38M8 16v11M36 16v11M7 10a15 5 0 1 0 30 0a15 5 0 1 0-30 0M22 5V1.5M19 1.5h6", ink, 2f));
        shapes["cardtable"] = new TowerShape(64, 22, FS("M19 3.5h7v5h-7zM28 3.5h7v5h-7zM37 3.5h7v5h-7z", TowerInk.Paper, TowerInk.Navy, 1.2f),
            FS("M50 4h7v4.5h-7z", TowerInk.Gold, TowerInk.Navy, 1.2f), S("M2 9h60M32 9v11M22 20.5h20", ink, 2f));
        shapes["barshelf"] = new TowerShape(70, 16, FS("M10 3h4v9h-4zM18 1h4v11h-4zM26 4h4v8h-4zM44 2h4v10h-4zM52 4h4v8h-4zM60 3h4v9h-4z", TowerInk.Gold, TowerInk.Navy, 1.2f),
            S("M4 13h62", ink, 2f));
        shapes["barcounter"] = new TowerShape(70, 22, F("M3 3h64v18H3z", TowerInk.Bg), S("M1 3h68M3 3v18h64V3M8 9h54", ink, 2f), S("M3 16h64", TowerInk.Gold, 2f));

        // The campus (2026-09-29): gym, cafeteria, offices, departments, research, prestige
        shapes["treadmill"] = new TowerShape(60, 34, F("M4 26h42v5H4z", TowerInk.Grey), FS("M46 26l8-16h4v3h-2l-7 13z", TowerInk.Shade, ink, 1.6f),
            S("M4 26h42v5H4zM8 31v3M42 31v3M52 10h6", ink, 2f));
        shapes["weights"] = new TowerShape(52, 30, F(Rect(33, 7, 4, 16) + Rect(45, 7, 4, 16), TowerInk.Navy), F("M3 17h28v4H3z", TowerInk.Red),
            S("M3 17h28v4H3zM7 21v8M27 21v8M37 15h8", ink, 2f));
        shapes["foodcounter"] = new TowerShape(90, 30, F(Rect(2, 12, 86, 17), TowerInk.Bg), F("M10 7h14v5H10zM38 6h14v6H38zM66 7h14v5H66z", TowerInk.Gold),
            S(Rect(2, 12, 86, 17) + "M0 12h90M2 18h86", ink, 2f), S("M12 7c3-4 7-4 10 0M40 6c3-4 7-4 10 0M68 7c3-4 7-4 10 0", TowerInk.Red, 1.4f));
        shapes["menu"] = new TowerShape(40, 30, FS(Rect(2, 2, 36, 25), TowerInk.Navy, ink, 2f), S("M7 9h20M7 14h24M7 19h16", TowerInk.Paper, 1.6f));
        shapes["cabinet"] = new TowerShape(30, 56, FS(Rect(2, 2, 26, 52), TowerInk.Shade, ink, 2f), S("M2 19h26M2 36h26M11 10h8M11 27h8M11 44h8", ink, 2f));
        shapes["labbench"] = new TowerShape(80, 34, F("M16 3h6v5l5 8H11l5-8z", TowerInk.Sky), F("M40 2h5v7l4 7H36l4-7z", TowerInk.LGreen), F("M58 6h8v10h-8z", TowerInk.Pink),
            S("M2 16h76M6 16v17M74 16v17M16 3h6v5l5 8H11l5-8zM40 2h5v7l4 7H36l4-7zM58 6h8v10h-8z", ink, 2f));
        shapes["cubicle"] = new TowerShape(78, 44, F("M2 4h6v39H2zM70 4h6v39h-6z", TowerInk.Grey), FS(Rect(30, 11, 20, 13, 1), TowerInk.Paper, ink, 2f),
            S("M2 4h6v39H2zM70 4h6v39h-6zM8 26h62M12 26v17M66 26v17M40 24v2", ink, 2f), F("M60 19h5v7h-5z", TowerInk.Green));
        shapes["trophy"] = new TowerShape(30, 50, F(Rect(5, 34, 20, 15), TowerInk.Wood), FS("M8 5h14v8a7 7 0 0 1-14 0z", TowerInk.Gold, ink, 2f),
            S("M15 20v8M10 29h10M8 8H5a3 3 0 0 0 3 5M22 8h3a3 3 0 0 1-3 5", ink, 2f), S(Rect(5, 34, 20, 15), ink, 2f));
        shapes["heli"] = new TowerShape(150, 60, FS("M40 28c0-8 8-14 20-14h22c12 0 20 6 20 14s-8 14-20 14H52c-7 0-12-6-12-14z", TowerInk.Red, ink, 2f),
            F("M86 18h7c4 0 8 4 8 9H86z", TowerInk.Sky), S("M40 28H14M14 21v14M20 7h110M75 7v7M50 50h52M58 42v8M94 42v8M4 58h142", ink, 2f),
            S("M68 58h14M75 52v12", TowerInk.Hi, 3f));
        shapes["parasol"] = new TowerShape(60, 70, FS("M4 24a26 20 0 0 1 52 0z", TowerInk.Pink, ink, 2f), S("M30 4v64M22 68h16", ink, 2f),
            S("M17 24c0-8 5-16 13-20M43 24c0-8-5-16-13-20", ink, 1.4f));

        // The room view's big drawings (TowerRoom board): a desk with two people, a plant and a coffee machine
        shapes["bigdesk"] = new TowerShape(224, 232,
            FS(Rect(38, 66, 48, 60, 12), TowerInk.Grey, TowerInk.Navy, 3f),
            FS(Rect(138, 66, 48, 60, 12), TowerInk.Grey, TowerInk.Navy, 3f),
            G(1, "M28 124C28 98 42 82 62 82S96 98 96 124Z", TowerInk.ShirtA, TowerInk.Navy, 3f),
            G(1, Circle(62, 58, 20), TowerInk.Paper, TowerInk.Navy, 3f),
            G(1, "M42 56a20 20 0 0 1 40 0c-8-7-32-7-40 0z", TowerInk.HairA, TowerInk.Navy, 3f),
            G(1, Circle(55, 62, 2.4f) + Circle(69, 62, 2.4f), TowerInk.Navy, TowerInk.None, 0f),
            G(1, "M56 69q6 5 12 0", TowerInk.None, TowerInk.Navy, 2.4f),
            G(2, "M128 124C128 98 142 82 162 82S196 98 196 124Z", TowerInk.ShirtB, TowerInk.Navy, 3f),
            G(2, Circle(162, 58, 20), TowerInk.Paper, TowerInk.Navy, 3f),
            G(2, "M142 56a20 20 0 0 1 40 0c-8-7-32-7-40 0z", TowerInk.HairB, TowerInk.Navy, 3f),
            G(2, Circle(155, 62, 2.4f) + Circle(169, 62, 2.4f), TowerInk.Navy, TowerInk.None, 0f),
            G(2, "M156 69q6 5 12 0", TowerInk.None, TowerInk.Navy, 2.4f),
            FS(Rect(36, 86, 52, 30, 3), TowerInk.Shade, TowerInk.Navy, 3f),
            S(Circle(62, 101, 4), TowerInk.Navy, 2f),
            FS(Rect(136, 86, 52, 30, 3), TowerInk.Shade, TowerInk.Navy, 3f),
            S(Circle(162, 101, 4), TowerInk.Navy, 2f),
            FS(Rect(6, 116, 212, 14, 3), TowerInk.Paper, TowerInk.Navy, 3f),
            FS(Rect(16, 130, 192, 76), TowerInk.Wood, TowerInk.Navy, 3f),
            S("M16 148H208", TowerInk.Navy, 3f),
            FS(Rect(22, 206, 12, 24, 2) + Rect(190, 206, 12, 24, 2), TowerInk.Grey, TowerInk.Navy, 3f),
            S("M62 206v12M46 219h32M162 206v12M146 219h32", TowerInk.Navy, 3f),
            FS(Circle(48, 226, 4) + Circle(76, 226, 4) + Circle(148, 226, 4) + Circle(176, 226, 4), TowerInk.Grey, TowerInk.Navy, 3f));
        shapes["bigplant"] = new TowerShape(96, 232,
            S("M48 168C46 140 38 118 24 96M48 168C50 138 58 114 72 90M48 168C48 130 48 98 47 62", TowerInk.Navy, 3f),
            FS("M24 96C8 94 2 78 6 62C22 64 30 80 24 96ZM72 90C88 88 94 72 90 56C74 58 66 74 72 90ZM47 62C36 52 36 32 47 20C58 32 58 52 47 62ZM44 140C32 142 22 134 20 122C32 120 42 128 44 140ZM52 134C64 136 74 128 76 116C64 114 54 122 52 134Z",
               TowerInk.Leaf, TowerInk.Navy, 3f),
            S("M24 96L12 70M72 90L84 64M47 62V28", TowerInk.Navy, 2f),
            FS(Rect(16, 166, 64, 14, 3), TowerInk.Terra, TowerInk.Navy, 3f),
            FS("M22 180H74L66 230H30Z", TowerInk.Terra, TowerInk.Navy, 3f));
        shapes["bigcoffee"] = new TowerShape(96, 232,
            S("M38 34c-4-5 4-8 0-14M56 34c-4-5 4-8 0-14", TowerInk.Navy, 2.4f),
            FS(Rect(14, 42, 68, 98, 8), TowerInk.Paper, TowerInk.Navy, 3f),
            S("M14 66H82", TowerInk.Navy, 3f),
            FS(Rect(22, 50, 30, 10, 2), TowerInk.Grey, TowerInk.Navy, 2f),
            FS(Circle(68, 55, 5), TowerInk.Red, TowerInk.Navy, 2f),
            FS(Rect(40, 74, 16, 10, 2), TowerInk.Grey, TowerInk.Navy, 2f),
            S("M48 86v12", TowerInk.Brown, 3f),
            FS(Rect(37, 100, 22, 24, 3), TowerInk.Pink, TowerInk.Navy, 3f),
            S("M59 106h4a5 5 0 0 1 0 10h-4", TowerInk.Navy, 3f),
            FS(Rect(24, 124, 48, 8, 2), TowerInk.Shade, TowerInk.Navy, 3f),
            FS(Rect(4, 140, 88, 90, 4), TowerInk.Wood, TowerInk.Navy, 3f),
            S("M48 148V222M41 178v12M55 178v12", TowerInk.Navy, 3f));
    }

    // ------------------------------------------------------------------
    // What stands in each room of the tower (Tower board LAYOUTS; x from the room's left edge)
    // ------------------------------------------------------------------

    static readonly Dictionary<TowerRoomRole, TowerThing[]> layouts = new Dictionary<TowerRoomRole, TowerThing[]>();

    static TowerThing T(string shape, float x, TowerThingSet set = TowerThingSet.Always, bool flip = false, float y = float.NaN, bool front = false)
    {
        bool figure = shape == "stand" || shape == "cv" || shape == "point" || shape == "sit" || shape == "ceo" || shape == "lounge";
        return new TowerThing { shape = shape, x = x, y = y, set = set, flip = flip, front = front, figure = figure };
    }

    /// <summary>The things of a room of <paramref name="role"/> (a big office when <paramref name="tiles"/> is 4 or more).</summary>
    public static TowerThing[] Layout(TowerRoomRole role, int tiles)
    {
        if (layouts.Count == 0) BuildLayouts();
        if (role == TowerRoomRole.Office && tiles >= 4) return bigOffice;
        return layouts.TryGetValue(role, out TowerThing[] things) ? things : null;
    }

    static TowerThing[] bigOffice;

    static void BuildLayouts()
    {
        const TowerThingSet A = TowerThingSet.Always, St = TowerThingSet.Staffed, Fu = TowerThingSet.Full, Se = TowerThingSet.Seat;
        layouts[TowerRoomRole.Lobby] = new[]
        {
            T("plant", 10), T("clock", 231, A, false, 36), T("cv", 146, St), T("cv", 172, St), T("sit", 250, Se, true), T("reception", 214, A, false, float.NaN, true)
        };
        layouts[TowerRoomRole.CeoOffice] = new[]
        {
            T("bookshelf", 10), T("frame", 182, A, false, 46), T("ceodesk", 82), T("sofa", 168), T("aquarium", 250), T("ceo", 70)
        };
        layouts[TowerRoomRole.BreakRoom] = new[] { T("sofa", 8), T("coffee", 84), T("arcade", 168), T("lounge", 26, Fu), T("stand", 148, St) };
        layouts[TowerRoomRole.Office] = new[]
        {
            T("plant", 6), T("desk", 60), T("coffee", 172), T("sit", 47, Se), T("sit", 97, Se, true), T("stand", 128, Se), T("stand", 150, Se)
        };
        bigOffice = new[]
        {
            T("plant", 4), T("desk", 40), T("desk", 118), T("desk", 196), T("desk", 274), T("coffee", 380),
            T("sit", 27, Se), T("sit", 77, Se, true), T("sit", 105, Se), T("sit", 155, Se, true), T("sit", 183, Se), T("sit", 233, Se, true),
            T("sit", 261, Se), T("sit", 311, Se, true), T("stand", 338, Se), T("stand", 358, Se)
        };
        layouts[TowerRoomRole.MeetingRoom] = new[]
        {
            T("whiteboard", 14, A, false, 46), T("projector", 204, A, false, 2), T("table", 164), T("point", 82, St), T("sit", 151, St), T("sit", 257, St, true)
        };
        layouts[TowerRoomRole.ServerRoom] = new[] { T("rack", 10), T("rack", 46), T("cooler", 156), T("stand", 102, St) };
        layouts[TowerRoomRole.TradingFloor] = new[]
        {
            T("ticker", 78, A, false, 40), T("terminal", 26), T("terminal", 128), T("terminal", 230), T("sit", 13, St), T("sit", 115, St), T("sit", 217, St)
        };
        layouts[TowerRoomRole.CasinoRoom] = new[]
        {
            T("slot", 10), T("roulette", 70), T("cardtable", 136), T("barshelf", 228, A, false, 60), T("barcounter", 228, A, false, float.NaN, true),
            T("stand", 42, St), T("sit", 123, St), T("stand", 204, St), T("stand", 262, St)
        };

        // The campus (2026-09-29). Seat figures (Se) stand for the people seated; big rooms show their share of them.
        layouts[TowerRoomRole.StarterOffice] = new[]
        {
            T("plant", 6), T("desk", 60), T("coffee", 172), T("frame", 118, A, false, 38),
            T("sit", 47, Se), T("sit", 97, Se, true), T("stand", 128, Se), T("stand", 150, Se)
        };
        var open = new List<TowerThing> { T("plant", 4), T("coffee", 530), T("whiteboard", 552, A, false, 46) };
        for (int d = 0; d < 6; d++) open.Add(T("desk", 34 + d * 80));
        for (int d = 0; d < 6; d++)
        {
            open.Add(T("sit", 21 + d * 80, Se));
            open.Add(T("sit", 71 + d * 80, Se, true));
        }
        open.Add(T("stand", 506, Se));
        layouts[TowerRoomRole.OpenPlan] = open.ToArray();
        var farm = new List<TowerThing> { T("plant", 684), T("coffee", 716), T("plant", 790) };
        for (int c = 0; c < 8; c++) farm.Add(T("cubicle", 8 + c * 84));
        for (int c = 0; c < 8; c++) farm.Add(T("sit", 30 + c * 84, Se));
        farm.Add(T("stand", 750, Se));
        layouts[TowerRoomRole.CubicleFarm] = farm.ToArray();
        layouts[TowerRoomRole.Cafeteria] = new[]
        {
            T("menu", 26, A, false, 30), T("table", 150), T("coffee", 290), T("plant", 380), T("stand", 40, St),
            T("foodcounter", 8, A, false, float.NaN, true), T("sit", 150, St), T("sit", 230, St, true), T("lounge", 190, Fu), T("stand", 330, Fu)
        };
        layouts[TowerRoomRole.Gym] = new[] { T("treadmill", 8), T("treadmill", 80), T("weights", 172), T("plant", 272), T("stand", 22, St), T("stand", 94, Fu), T("sit", 182, St) };
        layouts[TowerRoomRole.ExecutiveSuite] = new[]
        {
            T("bookshelf", 10), T("frame", 56, A, false, 40), T("ceodesk", 100), T("sofa", 180), T("plant", 272), T("ceo", 88, Se), T("sit", 198, Se), T("stand", 250, Se)
        };
        layouts[TowerRoomRole.BigMeetingRoom] = new[]
        {
            T("whiteboard", 14, A, false, 46), T("projector", 250, A, false, 2), T("table", 150), T("table", 290), T("plant", 470),
            T("point", 82, St), T("sit", 140, St), T("sit", 238, St, true), T("sit", 280, St), T("sit", 378, St, true), T("stand", 420, Fu)
        };
        layouts[TowerRoomRole.Marketing] = new[]
        {
            T("desk", 20), T("desk", 110), T("whiteboard", 186, A, false, 46), T("ticker", 250, A, false, 40), T("plant", 380),
            T("sit", 7, Se), T("sit", 57, Se, true), T("sit", 97, Se), T("sit", 147, Se, true), T("point", 300, St)
        };
        layouts[TowerRoomRole.HrDepartment] = new[]
        {
            T("plant", 6), T("desk", 50), T("cabinet", 150), T("cabinet", 186), T("frame", 232, A, false, 40),
            T("sit", 37, Se), T("sit", 87, Se, true), T("stand", 228, Se), T("cv", 262, St)
        };
        layouts[TowerRoomRole.LegalDepartment] = new[]
        {
            T("bookshelf", 8), T("bookshelf", 48), T("desk", 110), T("frame", 172, A, false, 40), T("cabinet", 232),
            T("sit", 97, Se), T("sit", 147, Se, true), T("stand", 200, Se)
        };
        layouts[TowerRoomRole.Accounting] = new[] { T("terminal", 14), T("terminal", 110), T("cabinet", 232), T("sit", 1, Se), T("sit", 97, Se), T("stand", 200, Se) };
        layouts[TowerRoomRole.ResearchLab] = new[]
        {
            T("labbench", 10), T("labbench", 110), T("rack", 220), T("whiteboard", 290, A, false, 46), T("plant", 382),
            T("stand", 28, Se), T("stand", 62, Se), T("stand", 130, Se), T("point", 300, Se)
        };
        layouts[TowerRoomRole.TrainingCenter] = new[]
        {
            T("whiteboard", 14, A, false, 46), T("projector", 220, A, false, 2), T("table", 150), T("bookshelf", 352),
            T("point", 82, Se), T("sit", 140, Se), T("sit", 238, Se, true), T("sit", 300, St)
        };
        var racks = new List<TowerThing> { T("cooler", 290), T("cooler", 340), T("terminal", 420), T("stand", 470, St) };
        for (int r = 0; r < 7; r++) racks.Add(T("rack", 10 + r * 38));
        for (int r = 0; r < 3; r++) racks.Add(T("rack", 500 + r * 38));
        layouts[TowerRoomRole.DataCenter] = racks.ToArray();
        layouts[TowerRoomRole.RooftopLounge] = new[]
        {
            T("parasol", 10), T("sofa", 80), T("barshelf", 250, A, false, 60), T("plant", 372), T("lounge", 95, St), T("lounge", 120, Fu),
            T("stand", 280, St), T("barcounter", 250, A, false, float.NaN, true), T("stand", 340, Fu)
        };
        layouts[TowerRoomRole.Helipad] = new[] { T("heli", 100), T("stand", 60, St), T("sofa", 330) };
        layouts[TowerRoomRole.TrophyHall] = new[]
        {
            T("trophy", 30), T("trophy", 90), T("trophy", 150), T("trophy", 210), T("frame", 270, A, false, 40), T("plant", 372),
            T("stand", 300, St), T("point", 330, Fu)
        };
    }

    // ------------------------------------------------------------------
    // The room view (TowerRoom board): the big drawing of each kind of furniture
    // ------------------------------------------------------------------

    /// <summary>
    /// How a piece of furniture is drawn in the room view: its shape(s), scale and size. Wall pieces hang
    /// <paramref name="wallY"/> below the ceiling (NaN = stands on the floor).
    /// </summary>
    public static void RoomViewArt(TowerFurnitureArt art, out string shape, out float scale, out float wallY, out string extra, out float extraY)
    {
        extra = null;
        extraY = 0f;
        wallY = float.NaN;
        scale = 3f;
        switch (art)
        {
            case TowerFurnitureArt.Desk: shape = "bigdesk"; scale = 1f; break;
            case TowerFurnitureArt.Plant: shape = "bigplant"; scale = 1f; break;
            case TowerFurnitureArt.CoffeeMachine: shape = "bigcoffee"; scale = 1f; break;
            case TowerFurnitureArt.Sofa: shape = "sofa"; break;
            case TowerFurnitureArt.Bookshelf: shape = "bookshelf"; break;
            case TowerFurnitureArt.Aquarium: shape = "aquarium"; break;
            case TowerFurnitureArt.ReceptionDesk: shape = "reception"; break;
            case TowerFurnitureArt.ArcadeCabinet: shape = "arcade"; break;
            case TowerFurnitureArt.BigTable: shape = "table"; break;
            case TowerFurnitureArt.Whiteboard: shape = "whiteboard"; wallY = 150f; break;
            case TowerFurnitureArt.Projector: shape = "projector"; wallY = 0f; break;
            case TowerFurnitureArt.ServerRack: shape = "rack"; break;
            case TowerFurnitureArt.CoolingUnit: shape = "cooler"; break;
            case TowerFurnitureArt.Terminal: shape = "terminal"; break;
            case TowerFurnitureArt.TickerBoard: shape = "ticker"; scale = 2f; wallY = 170f; break;
            case TowerFurnitureArt.SlotMachine: shape = "slot"; break;
            case TowerFurnitureArt.CardTable: shape = "cardtable"; break;
            case TowerFurnitureArt.RouletteWheel: shape = "roulette"; break;
            case TowerFurnitureArt.Treadmill: shape = "treadmill"; break;
            case TowerFurnitureArt.Weights: shape = "weights"; break;
            case TowerFurnitureArt.FoodCounter: shape = "foodcounter"; scale = 2.5f; extra = "menu"; extraY = -120f; break;
            case TowerFurnitureArt.FilingCabinet: shape = "cabinet"; break;
            case TowerFurnitureArt.LabBench: shape = "labbench"; scale = 2.5f; break;
            case TowerFurnitureArt.Cubicle: shape = "cubicle"; scale = 2.5f; break;
            case TowerFurnitureArt.Trophy: shape = "trophy"; break;
            case TowerFurnitureArt.Helicopter: shape = "heli"; scale = 1.5f; break;
            case TowerFurnitureArt.Parasol: shape = "parasol"; scale = 2.5f; break;
            default: shape = "barcounter"; extra = "barshelf"; extraY = -130f; break;
        }
    }

    // ------------------------------------------------------------------
    // Drawing
    // ------------------------------------------------------------------

    static readonly List<Vector2> scratch = new List<Vector2>(128);

    /// <summary>
    /// Draw <paramref name="shape"/> with its top-left at (<paramref name="x"/>, <paramref name="y"/>) in the builder's current
    /// frame (y down), scaled and optionally mirrored inside its box. Strokes are scaled by <paramref name="strokeScale"/> x
    /// the shape's scale. Parts of the groups in <paramref name="hiddenGroups"/> (bit 1 = group 1, bit 2 = group 2) are skipped.
    /// </summary>
    public static void Draw(BlueprintMeshBuilder m, TowerShape shape, float x, float y, float scale, bool flip, in TowerPalette palette,
                            float strokeScale = 1f, int hiddenGroups = 0)
    {
        if (shape == null || m == null) return;
        m.PushFrame();
        Vector2 origin = m.Map(new Vector2(flip ? x + shape.width * scale : x, y));
        m.SetFrame(origin, m.MapDir(new Vector2(flip ? -scale : scale, 0f)), m.MapDir(new Vector2(0f, scale)));
        DrawParts(m, shape.parts, palette, strokeScale, hiddenGroups);
        m.PopFrame();
    }

    /// <summary>Draw parts in the builder's current frame (icons: a 24-unit frame).</summary>
    public static void DrawParts(BlueprintMeshBuilder m, TowerPart[] parts, in TowerPalette palette, float strokeScale = 1f, int hiddenGroups = 0)
    {
        if (parts == null) return;
        foreach (TowerPart part in parts)
        {
            if (part.group != 0 && (hiddenGroups & (1 << (part.group - 1))) != 0) continue;
            TowerPath path = TowerSvg.Get(part.d);
            if (path == null) continue;
            if (part.fill != TowerInk.None)
            {
                Color fill = Resolve(part.fill, palette);
                if (fill.a > 0f)
                {
                    for (int i = 0; i < path.subpaths.Count; i++)
                    {
                        Vector2[] pts = path.subpaths[i];
                        if (pts.Length < 3) continue;
                        scratch.Clear();
                        scratch.AddRange(pts);
                        m.FillPolygon(scratch, fill);
                    }
                }
            }
            if (part.stroke != TowerInk.None && part.width > 0f)
            {
                Color stroke = Resolve(part.stroke, palette);
                if (stroke.a <= 0f) continue;
                float width = part.width * strokeScale;
                for (int i = 0; i < path.subpaths.Count; i++)
                {
                    Vector2[] pts = path.subpaths[i];
                    if (path.lengths[i] < width * 0.25f)
                    {
                        // A dot ("h.01" with a round cap)
                        m.FillCircle(pts[0], width * 0.5f, stroke);
                        continue;
                    }
                    scratch.Clear();
                    scratch.AddRange(pts);
                    m.Stroke(scratch, width, stroke, path.closed[i]);
                }
            }
        }
    }
}

/// <summary>A parsed SVG path: flattened subpaths (curves and arcs turned into points), closed or not, and their lengths.</summary>
public sealed class TowerPath
{
    public readonly List<Vector2[]> subpaths = new List<Vector2[]>();
    public readonly List<bool> closed = new List<bool>();
    public readonly List<float> lengths = new List<float>();
}

/// <summary>
/// Minimal SVG path reader for the design's drawings: M L H V C S Q T A Z (absolute and relative), numbers written the
/// compact SVG way ("1.6.6", "-7.6"). Paths are parsed once and cached.
/// </summary>
public static class TowerSvg
{
    static readonly Dictionary<string, TowerPath> cache = new Dictionary<string, TowerPath>();

    public static TowerPath Get(string d)
    {
        if (string.IsNullOrEmpty(d)) return null;
        if (cache.TryGetValue(d, out TowerPath path)) return path;
        try
        {
            path = Parse(d);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[TowerSvg] Couldn't read a path (" + e.Message + "): " + d);
            path = new TowerPath();
        }
        cache[d] = path;
        return path;
    }

    static TowerPath Parse(string d)
    {
        var path = new TowerPath();
        var points = new List<Vector2>(64);
        int i = 0;
        char command = 'M';
        Vector2 current = Vector2.zero, start = Vector2.zero, lastControl = Vector2.zero;
        char lastCommand = ' ';

        while (true)
        {
            SkipSeparators(d, ref i);
            if (i >= d.Length) break;
            int before = i;
            char c = d[i];
            if (char.IsLetter(c) && c != 'e' && c != 'E')
            {
                command = c;
                i++;
            }
            else if (command == 'M')
            {
                command = 'L';       // extra pairs after a moveto are linetos
            }
            else if (command == 'm')
            {
                command = 'l';
            }

            bool relative = char.IsLower(command);
            Vector2 origin = relative ? current : Vector2.zero;
            switch (char.ToUpperInvariant(command))
            {
                case 'M':
                    Flush(path, points, false);
                    current = origin + new Vector2(Number(d, ref i), Number(d, ref i));
                    start = current;
                    points.Add(current);
                    break;
                case 'L':
                    current = origin + new Vector2(Number(d, ref i), Number(d, ref i));
                    points.Add(current);
                    break;
                case 'H':
                    current = new Vector2((relative ? current.x : 0f) + Number(d, ref i), current.y);
                    points.Add(current);
                    break;
                case 'V':
                    current = new Vector2(current.x, (relative ? current.y : 0f) + Number(d, ref i));
                    points.Add(current);
                    break;
                case 'C':
                {
                    Vector2 c1 = origin + new Vector2(Number(d, ref i), Number(d, ref i));
                    Vector2 c2 = origin + new Vector2(Number(d, ref i), Number(d, ref i));
                    Vector2 to = origin + new Vector2(Number(d, ref i), Number(d, ref i));
                    Cubic(points, current, c1, c2, to);
                    lastControl = c2;
                    current = to;
                    break;
                }
                case 'S':
                {
                    char prev = char.ToUpperInvariant(lastCommand);
                    Vector2 c1 = prev == 'C' || prev == 'S' ? 2f * current - lastControl : current;
                    Vector2 c2 = origin + new Vector2(Number(d, ref i), Number(d, ref i));
                    Vector2 to = origin + new Vector2(Number(d, ref i), Number(d, ref i));
                    Cubic(points, current, c1, c2, to);
                    lastControl = c2;
                    current = to;
                    break;
                }
                case 'Q':
                {
                    Vector2 q = origin + new Vector2(Number(d, ref i), Number(d, ref i));
                    Vector2 to = origin + new Vector2(Number(d, ref i), Number(d, ref i));
                    Quadratic(points, current, q, to);
                    lastControl = q;
                    current = to;
                    break;
                }
                case 'T':
                {
                    char prev = char.ToUpperInvariant(lastCommand);
                    Vector2 q = prev == 'Q' || prev == 'T' ? 2f * current - lastControl : current;
                    Vector2 to = origin + new Vector2(Number(d, ref i), Number(d, ref i));
                    Quadratic(points, current, q, to);
                    lastControl = q;
                    current = to;
                    break;
                }
                case 'A':
                {
                    float rx = Mathf.Abs(Number(d, ref i)), ry = Mathf.Abs(Number(d, ref i));
                    float rotation = Number(d, ref i);
                    bool large = Flag(d, ref i), sweep = Flag(d, ref i);
                    Vector2 to = origin + new Vector2(Number(d, ref i), Number(d, ref i));
                    Arc(points, current, rx, ry, rotation, large, sweep, to);
                    current = to;
                    break;
                }
                case 'Z':
                    current = start;
                    Flush(path, points, true);
                    points.Add(current);   // a following command without M starts here
                    break;
                default:
                    throw new FormatException("unsupported command '" + command + "'");
            }
            if (i == before) throw new FormatException("unexpected '" + d[i] + "' at " + i);
            lastCommand = command;
        }
        Flush(path, points, false);
        return path;
    }

    static void Flush(TowerPath path, List<Vector2> points, bool closed)
    {
        // A lone point left by "z" or a moveto isn't a subpath
        if (points.Count >= 2 || (points.Count == 1 && closed))
        {
            if (closed && points.Count > 2 && (points[0] - points[points.Count - 1]).sqrMagnitude < 1e-8f)
                points.RemoveAt(points.Count - 1);
            float length = 0f;
            for (int k = 1; k < points.Count; k++) length += Vector2.Distance(points[k - 1], points[k]);
            if (closed && points.Count > 2) length += Vector2.Distance(points[points.Count - 1], points[0]);
            path.subpaths.Add(points.ToArray());
            path.closed.Add(closed);
            path.lengths.Add(length);
        }
        points.Clear();
    }

    static void SkipSeparators(string d, ref int i)
    {
        while (i < d.Length && (d[i] == ' ' || d[i] == ',' || d[i] == '\n' || d[i] == '\r' || d[i] == '\t')) i++;
    }

    static bool Flag(string d, ref int i)
    {
        SkipSeparators(d, ref i);
        if (i >= d.Length) throw new FormatException("missing arc flag");
        char c = d[i++];
        return c == '1';
    }

    static float Number(string d, ref int i)
    {
        SkipSeparators(d, ref i);
        int start = i;
        if (i < d.Length && (d[i] == '-' || d[i] == '+')) i++;
        bool dot = false, digits = false;
        while (i < d.Length)
        {
            char c = d[i];
            if (c >= '0' && c <= '9') { digits = true; i++; }
            else if (c == '.' && !dot) { dot = true; i++; }
            else if ((c == 'e' || c == 'E') && digits)
            {
                i++;
                if (i < d.Length && (d[i] == '-' || d[i] == '+')) i++;
            }
            else break;
        }
        if (i == start) throw new FormatException("number expected at " + start);
        return float.Parse(d.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    static void Cubic(List<Vector2> points, Vector2 p0, Vector2 c1, Vector2 c2, Vector2 p3)
    {
        float span = Vector2.Distance(p0, c1) + Vector2.Distance(c1, c2) + Vector2.Distance(c2, p3);
        int segments = Mathf.Clamp(Mathf.CeilToInt(span / 2f), 3, 24);
        for (int k = 1; k <= segments; k++)
        {
            float t = k / (float)segments, u = 1f - t;
            points.Add(u * u * u * p0 + 3f * u * u * t * c1 + 3f * u * t * t * c2 + t * t * t * p3);
        }
    }

    static void Quadratic(List<Vector2> points, Vector2 p0, Vector2 q, Vector2 p2)
    {
        float span = Vector2.Distance(p0, q) + Vector2.Distance(q, p2);
        int segments = Mathf.Clamp(Mathf.CeilToInt(span / 2f), 3, 16);
        for (int k = 1; k <= segments; k++)
        {
            float t = k / (float)segments, u = 1f - t;
            points.Add(u * u * p0 + 2f * u * t * q + t * t * p2);
        }
    }

    /// <summary>SVG elliptical arc (endpoint form) as points, following the SVG spec's centre conversion.</summary>
    static void Arc(List<Vector2> points, Vector2 from, float rx, float ry, float rotationDegrees, bool large, bool sweep, Vector2 to)
    {
        if ((from - to).sqrMagnitude < 1e-10f) return;
        if (rx < 1e-5f || ry < 1e-5f)
        {
            points.Add(to);
            return;
        }
        float phi = rotationDegrees * Mathf.Deg2Rad, cos = Mathf.Cos(phi), sin = Mathf.Sin(phi);
        Vector2 half = (from - to) * 0.5f;
        float x1 = cos * half.x + sin * half.y, y1 = -sin * half.x + cos * half.y;
        float lambda = (x1 * x1) / (rx * rx) + (y1 * y1) / (ry * ry);
        if (lambda > 1f)
        {
            float s = Mathf.Sqrt(lambda);
            rx *= s;
            ry *= s;
        }
        float num = rx * rx * ry * ry - rx * rx * y1 * y1 - ry * ry * x1 * x1;
        float den = rx * rx * y1 * y1 + ry * ry * x1 * x1;
        float factor = den > 0f ? Mathf.Sqrt(Mathf.Max(0f, num / den)) : 0f;
        if (large == sweep) factor = -factor;
        float cx1 = factor * rx * y1 / ry, cy1 = -factor * ry * x1 / rx;
        Vector2 mid = (from + to) * 0.5f;
        Vector2 center = new Vector2(cos * cx1 - sin * cy1 + mid.x, sin * cx1 + cos * cy1 + mid.y);

        float theta1 = Angle(1f, 0f, (x1 - cx1) / rx, (y1 - cy1) / ry);
        float delta = Angle((x1 - cx1) / rx, (y1 - cy1) / ry, (-x1 - cx1) / rx, (-y1 - cy1) / ry);
        if (!sweep && delta > 0f) delta -= 2f * Mathf.PI;
        else if (sweep && delta < 0f) delta += 2f * Mathf.PI;

        int segments = Mathf.Clamp(Mathf.CeilToInt(Mathf.Abs(delta) * Mathf.Max(rx, ry) / 1.5f), 4, 64);
        for (int k = 1; k <= segments; k++)
        {
            float a = theta1 + delta * k / segments;
            float ex = rx * Mathf.Cos(a), ey = ry * Mathf.Sin(a);
            points.Add(new Vector2(cos * ex - sin * ey + center.x, sin * ex + cos * ey + center.y));
        }
        points[points.Count - 1] = to;
    }

    static float Angle(float ux, float uy, float vx, float vy)
    {
        float dot = ux * vx + uy * vy;
        float len = Mathf.Sqrt((ux * ux + uy * uy) * (vx * vx + vy * vy));
        float a = Mathf.Acos(Mathf.Clamp(len > 0f ? dot / len : 1f, -1f, 1f));
        return ux * vy - uy * vx < 0f ? -a : a;
    }
}
