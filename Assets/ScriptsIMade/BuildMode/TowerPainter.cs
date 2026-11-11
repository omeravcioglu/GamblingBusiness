using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Geometry of the tower in design pixels (1920 x 1080 screen, y down), as on the "HQ tower (2D build)" boards: 8 tiles
/// of 104 px from x 544, floors 144 px tall (a 12 px slab on top of a 132 px room), street level at y 880, the lift shaft
/// left of the building. Floor n spans BottomOf(n) - 144 .. BottomOf(n).
/// </summary>
public static class TowerGeo
{
    public const float Tile = 104f;
    public const float X0 = 544f;
    public const float FloorHeight = 144f;
    public const float Slab = 12f;
    public const float RoomHeight = 132f;
    public const float Ground = 880f;
    public const float Wall = 6f;
    public const float ShaftX = 486f;
    public const float CarLeft = 492f;
    public const float CarWidth = 42f;
    public const float CarHeight = 62f;
    public const float NumberX = 452f;
    /// <summary>Paper rooms have these corner radii (4px 6px 4px 6px).</summary>
    public static readonly Vector4 RoomRadii = new Vector4(4f, 6f, 4f, 6f);

    public static float BottomOf(int floor) => Ground - (floor - 1) * FloorHeight;
    public static float TopOf(int floor) => BottomOf(floor) - FloorHeight;
    public static float RoomTop(int floor) => TopOf(floor) + Slab;
    public static float RoomLeft(int tile) => X0 + Tile * tile + 3f;
    public static float RoomWidth(int tiles) => Tile * tiles - 6f;
    /// <summary>Right edge of the interior for a floor of <paramref name="tiles"/> tiles (1376 for 8).</summary>
    public static float Right(int tiles) => X0 + Tile * tiles;
    public static float CarTop(int floor) => BottomOf(floor) - 64f;

    /// <summary>The floor at design height <paramref name="y"/> (0 below street level; may be above the roof).</summary>
    public static int FloorAt(float y)
    {
        if (y > Ground) return 0;
        return 1 + Mathf.FloorToInt((Ground - y) / FloorHeight);
    }

    /// <summary>The tile under design x (may be outside 0..tiles-1).</summary>
    public static int TileAt(float x)
    {
        return Mathf.FloorToInt((x - X0) / Tile);
    }

    public static Rect RoomRect(int floor, int tile, int tiles)
    {
        return new Rect(RoomLeft(tile), RoomTop(floor), RoomWidth(tiles), RoomHeight);
    }

    // ------------------------------------------------------------------
    // The campus (2026-09-29): buildings stand in a row, the HQ first. Every building is drawn with the HQ's
    // coordinates shifted right by its origin (OriginX); a building's span runs from its floor-number column to its
    // right wall, and neighbours are BuildingGap apart (a skybridge crosses the gap).
    // ------------------------------------------------------------------

    /// <summary>Space between one building's right wall and the next one's floor-number column.</summary>
    public const float BuildingGap = 260f;
    /// <summary>A skybridge's walkway: from this far below the room top to that far (it sits on the floor, under the floor number).</summary>
    public const float BridgeTop = 90f;
    public const float BridgeBottom = 128f;

    /// <summary>Width of a building of <paramref name="tiles"/> tiles from its floor-number column to its right wall (8 tiles: 930).</summary>
    public static float SpanWidth(int tiles) => Right(tiles) + Wall - NumberX;

    /// <summary>Design x added to every coordinate of <paramref name="building"/> (the HQ: 0).</summary>
    public static float OriginX(TowerPlan plan, TowerBuilding building)
    {
        float x = 0f;
        if (plan == null) return x;
        foreach (TowerBuilding b in plan.Buildings)
        {
            if (b == building) return x;
            x += SpanWidth(b.TilesPerFloor) + BuildingGap;
        }
        return x;
    }

    /// <summary>Left edge of the building's span (its floor-number column), design x.</summary>
    public static float SpanLeft(TowerPlan plan, TowerBuilding building) => OriginX(plan, building) + NumberX;

    /// <summary>Right edge of the building's span (its outer right wall), design x.</summary>
    public static float SpanRight(TowerPlan plan, TowerBuilding building) => OriginX(plan, building) + Right(building.TilesPerFloor) + Wall;

    /// <summary>Middle of the building's interior, design x.</summary>
    public static float CenterX(TowerPlan plan, TowerBuilding building) => OriginX(plan, building) + (X0 + Right(building.TilesPerFloor)) * 0.5f;

    /// <summary>The whole row from the HQ's floor-number column to the last building's right wall.</summary>
    public static float CampusLeft(TowerPlan plan) => NumberX;

    public static float CampusRight(TowerPlan plan)
    {
        if (plan == null || plan.Buildings.Count == 0) return Right(8) + Wall;
        return SpanRight(plan, plan.Buildings[plan.Buildings.Count - 1]);
    }

    /// <summary>The building whose span (with <paramref name="margin"/> on both sides) holds design x, or null (a gap).</summary>
    public static TowerBuilding BuildingAt(TowerPlan plan, float x, float margin = 30f)
    {
        if (plan == null) return null;
        float origin = 0f;
        foreach (TowerBuilding b in plan.Buildings)
        {
            float left = origin + NumberX - margin, right = origin + Right(b.TilesPerFloor) + Wall + margin;
            if (x >= left && x < right) return b;
            origin += SpanWidth(b.TilesPerFloor) + BuildingGap;
        }
        return null;
    }

    /// <summary>The building left of the gap that design x is in (between its right wall and the next one's lift shaft), or null.</summary>
    public static TowerBuilding GapLeftOf(TowerPlan plan, float x)
    {
        if (plan == null) return null;
        float origin = 0f;
        for (int i = 0; i + 1 < plan.Buildings.Count; i++)
        {
            TowerBuilding b = plan.Buildings[i];
            float wall = origin + Right(b.TilesPerFloor) + Wall;
            float nextOrigin = origin + SpanWidth(b.TilesPerFloor) + BuildingGap;
            if (x >= wall - 40f && x < nextOrigin + ShaftX + 20f) return b;
            origin = nextOrigin;
        }
        return null;
    }

    /// <summary>A skybridge's walkway between <paramref name="left"/> and its neighbour at <paramref name="floor"/> (design px).</summary>
    public static Rect BridgeRect(TowerPlan plan, TowerBuilding left, int floor)
    {
        float from = SpanRight(plan, left);
        float to = OriginX(plan, left) + SpanWidth(left.TilesPerFloor) + BuildingGap + ShaftX;
        float top = RoomTop(floor);
        return new Rect(from, top + BridgeTop, Mathf.Max(8f, to - from), BridgeBottom - BridgeTop);
    }
}

/// <summary>
/// Draws the HQ tower into BlueprintMeshBuilders in design pixels (y down): the blueprint grid, the section drawing in
/// white ink (street, walls, lift shaft, roof, clouds), each floor (slab, empty tiles, paper rooms with their furniture
/// and staff) and the overlays (selection ring, placement ghost, add-floor slot). Line weights follow the Tower board.
/// </summary>
public static class TowerPainter
{
    static readonly List<Vector2> path = new List<Vector2>(64);
    static readonly List<TowerThing> order = new List<TowerThing>(24);

    public static Color Chalk => UIColors.BlueprintInk;
    static Color ChalkA(float a) => UIColors.WithAlpha(UIColors.BlueprintInk, a);
    public static readonly Color RoomShadow = new Color32(29, 36, 51, 140);   // rgba(29,36,51,0.55)
    static readonly Color FeltCenter = UIColors.Hex(0x2A7652);
    static readonly Color FeltEdge = UIColors.Hex(0x123D29);
    static readonly Color GoldInset = new Color32(226, 177, 60, 204);

    // ------------------------------------------------------------------
    // Grid
    // ------------------------------------------------------------------

    /// <summary>Blueprint grid over <paramref name="area"/>: 1 px every 24, 2 px every 96 (as the design's CSS), at least one screen pixel wide.</summary>
    public static void DrawGrid(BlueprintMeshBuilder m, Rect area, float pixel)
    {
        Color32 minor = ChalkA(0.07f), major = ChalkA(0.16f);
        float minorWidth = Mathf.Max(1f, pixel), majorWidth = Mathf.Max(2f, pixel);
        int x0 = Mathf.FloorToInt(area.xMin / 24f), x1 = Mathf.CeilToInt(area.xMax / 24f);
        for (int i = x0; i <= x1; i++)
        {
            bool isMajor = i % 4 == 0;
            float x = i * 24f + (isMajor ? 1f : 0.5f);
            m.Line(new Vector2(x, area.yMin), new Vector2(x, area.yMax), isMajor ? majorWidth : minorWidth, isMajor ? major : minor);
        }
        int y0 = Mathf.FloorToInt(area.yMin / 24f), y1 = Mathf.CeilToInt(area.yMax / 24f);
        for (int i = y0; i <= y1; i++)
        {
            bool isMajor = i % 4 == 0;
            float y = i * 24f + (isMajor ? 1f : 0.5f);
            m.Line(new Vector2(area.xMin, y), new Vector2(area.xMax, y), isMajor ? majorWidth : minorWidth, isMajor ? major : minor);
        }
    }

    // ------------------------------------------------------------------
    // Section drawing
    // ------------------------------------------------------------------

    /// <summary>
    /// Street with hatching and the HQ tower's section drawing (the original single tower): kept for callers that draw one
    /// building. The campus draws DrawStreet once and DrawBuilding for every building.
    /// </summary>
    public static void DrawStructure(BlueprintMeshBuilder m, int floors, int tiles, float left, float right)
    {
        DrawStreet(m, left, right);
        DrawBuilding(m, null, floors, tiles, true);
    }

    /// <summary>The street under the campus and its hatching, from <paramref name="left"/> to <paramref name="right"/> (design x).</summary>
    public static void DrawStreet(BlueprintMeshBuilder m, float left, float right)
    {
        Color32 chalk = Chalk;
        float ground = TowerGeo.Ground;
        m.Line(new Vector2(left, ground), new Vector2(right, ground), 4f, chalk);
        Color32 hatch = ChalkA(0.55f);
        m.Line(new Vector2(left, ground + 16f), new Vector2(right, ground + 16f), 2f, hatch);
        for (float x = Mathf.Floor((left + 6f) / 18f) * 18f - 6f; x < right; x += 18f)
            m.Line(new Vector2(x, ground + 16f), new Vector2(x + 12f, ground + 4f), 2f, hatch);
    }

    /// <summary>
    /// One building in its own coordinates (the HQ's; the caller shifts the frame to the building's origin): a faint wash of
    /// its type's tint, the width dimension line under it, clouds higher up, the outer walls, the lift shaft with its
    /// machine room, and the roof by facade (the HQ: railing, its flag and, from 10 floors, a helipad; the annex: air
    /// conditioning; the office tower: an antenna; the skyscraper: a crown and a spire). The HQ also keeps its street doodles.
    /// </summary>
    public static void DrawBuilding(BlueprintMeshBuilder m, TowerBuildingType type, int floors, int tiles, bool headquarters)
    {
        Color32 chalk = Chalk;
        float interiorRight = TowerGeo.Right(tiles);
        float roof = TowerGeo.TopOf(floors);
        float ground = TowerGeo.Ground;
        TowerFacade facade = type != null ? type.facade : TowerFacade.Hq;

        // A faint wash behind the building (its type's tint)
        if (type != null && type.tint.a > 0.001f)
            m.FillRect(new Rect(TowerGeo.X0 - TowerGeo.Wall, roof, interiorRight - TowerGeo.X0 + TowerGeo.Wall * 2f, ground - roof), type.tint, 0f);

        // The width dimension line under the building
        Color32 dim = ChalkA(0.8f);
        m.Line(new Vector2(TowerGeo.X0, ground + 22f), new Vector2(TowerGeo.X0, ground + 58f), 1.5f, dim);
        m.Line(new Vector2(interiorRight, ground + 22f), new Vector2(interiorRight, ground + 58f), 1.5f, dim);
        float mid = (TowerGeo.X0 + interiorRight) * 0.5f;
        m.Line(new Vector2(TowerGeo.X0, ground + 46f), new Vector2(mid - 50f, ground + 46f), 1.5f, dim);
        m.Line(new Vector2(mid + 50f, ground + 46f), new Vector2(interiorRight, ground + 46f), 1.5f, dim);
        for (int k = 0; k <= tiles; k++)
        {
            float x = TowerGeo.X0 - 5f + TowerGeo.Tile * k;
            m.Line(new Vector2(x, ground + 51f), new Vector2(x + 10f, ground + 41f), 1.5f, dim);
        }

        if (headquarters)
        {
            // Doodles on the street: a tree left of the lift, a lamp post and a bench right of the building, birds
            m.PushFrame();
            m.SetFrame(m.Map(new Vector2(interiorRight - 1376f, 0f)), m.MapDir(Vector2.right), m.MapDir(new Vector2(0f, 1f)));
            StrokePath(m, "M1424 880h18M1433 880V772c0-11 7-15 17-15h6M1452 757h16l-3 7h-10zM1460 770v7M1451 768l-4 5M1469 768l4 5" +
                          "M1496 862h74M1502 862v18M1564 862v18M1498 846h70M1503 846v16M1563 846v16", 2.5f, chalk);
            m.PopFrame();
            StrokePath(m, "M366 880V842M366 856l-9-8M366 850l8-7M344 842c-9 0-15-8-12-15-7-5-4-16 5-18 0-10 11-16 20-12 5-8 18-8 22 0 9-3 17 5 14 13 8 4 7 16-2 18 0 9-9 14-18 11-7 6-21 6-29 3z", 2.5f, chalk);
            StrokePath(m, "M322 222q8-7 16 0q8-7 16 0M364 204q6-5 12 0q6-5 12 0M1540 252q8-7 16 0q8-7 16 0", 2.5f, chalk);
        }

        // Clouds and birds higher up (they scroll with the campus); the left ones only by the HQ (the others' left is a gap)
        for (int k = 0; k < 40; k++)
        {
            float y = ground - 880f - 360f * k - (k % 3) * 70f;
            if (y < roof - 1200f) break;
            bool leftSide = k % 2 == 0;
            if (leftSide && !headquarters) continue;
            float x = leftSide ? 150f + (k % 5) * 18f : interiorRight + 110f + (k % 4) * 30f;
            StrokePath(m, Cloud(x, y), 2.5f, chalk);
            if (k % 3 == 1) StrokePath(m, Birds(x + 150f, y + 90f), 2.5f, chalk);
        }

        // Outer walls and the lift shaft with its machine room on top
        m.FillRect(new Rect(TowerGeo.X0 - TowerGeo.Wall, roof, TowerGeo.Wall, ground - roof), chalk);
        m.FillRect(new Rect(interiorRight, roof, TowerGeo.Wall, ground - roof), chalk);
        m.Line(new Vector2(TowerGeo.ShaftX, roof - 32f), new Vector2(TowerGeo.ShaftX, ground), 4f, chalk);
        path.Clear();
        path.Add(new Vector2(TowerGeo.ShaftX, roof));
        path.Add(new Vector2(TowerGeo.ShaftX, roof - 32f));
        path.Add(new Vector2(542f, roof - 32f));
        path.Add(new Vector2(542f, roof));
        m.Stroke(path, 2.5f, chalk, false);
        m.Circle(new Vector2(514f, roof - 16f), 7f, 2.5f, chalk);
        m.Line(new Vector2(509f, roof - 16f), new Vector2(519f, roof - 16f), 2.5f, chalk);

        switch (facade)
        {
            case TowerFacade.Annex: DrawAnnexRoof(m, roof, interiorRight); break;
            case TowerFacade.Tower: DrawTowerRoof(m, roof, interiorRight); break;
            case TowerFacade.Skyscraper: DrawSkyscraperRoof(m, roof, interiorRight); break;
            default: DrawHqRoof(m, floors, roof, interiorRight); break;
        }
    }

    static void Railing(BlueprintMeshBuilder m, float roof, float from, float to)
    {
        Color32 chalk = Chalk;
        m.Line(new Vector2(from, roof - 18f), new Vector2(to, roof - 18f), 2f, chalk);
        m.Line(new Vector2(from, roof - 9f), new Vector2(to, roof - 9f), 2f, chalk);
        for (float x = from; x <= to + 0.1f; x += 24f)
            m.Line(new Vector2(x, roof), new Vector2(x, roof - 18f), 2f, chalk);
    }

    /// <summary>The HQ's roof: its railing, the flag that says it's the first building, and (from 10 floors) a helipad.</summary>
    static void DrawHqRoof(BlueprintMeshBuilder m, int floors, float roof, float interiorRight)
    {
        Color32 chalk = Chalk;
        bool helipad = floors >= 10;
        float railEnd = helipad ? interiorRight - 340f : interiorRight - 28f;
        Railing(m, roof, 556f, railEnd);
        // The flag on the machine room
        m.Line(new Vector2(530f, roof - 32f), new Vector2(530f, roof - 92f), 2.5f, chalk);
        path.Clear();
        path.Add(new Vector2(530f, roof - 92f));
        path.Add(new Vector2(572f, roof - 92f));
        path.Add(new Vector2(562f, roof - 81f));
        path.Add(new Vector2(572f, roof - 70f));
        path.Add(new Vector2(530f, roof - 70f));
        m.FillPolygon(path, ChalkA(0.25f));
        m.Stroke(path, 2.5f, chalk, false);
        if (helipad)
        {
            m.PushFrame();
            m.SetFrame(m.Map(new Vector2(interiorRight - 1376f, roof - 160f)), m.MapDir(Vector2.right), m.MapDir(new Vector2(0f, 1f)));
            StrokePath(m, "M1070 142h250v6h-250zM1080 148V160M1160 148V160M1240 148V160M1310 148V160" +
                          "M1140 140h58M1152 140v-6M1184 140v-6M1146 134h34c6 0 10-4 10-10v-2c0-6-4-10-10-10h-20c-10 0-18 8-18 16 0 3 2 6 4 6zM1148 126c1-6 5-10 11-10h3v10zM1188 118h34l5-10M1229 104v10M1224 109h10M1170 112v-5M1128 107h86" +
                          "M1334 160V116M1334 118l24 3v7l-24 3zM1342 119v10M1350 120v8", 2f, chalk);
            m.PopFrame();
        }
    }

    /// <summary>The annex's flat roof: a low parapet and air-conditioning units.</summary>
    static void DrawAnnexRoof(BlueprintMeshBuilder m, float roof, float interiorRight)
    {
        Color32 chalk = Chalk;
        m.Line(new Vector2(TowerGeo.X0 - TowerGeo.Wall, roof - 10f), new Vector2(interiorRight + TowerGeo.Wall, roof - 10f), 2.5f, chalk);
        m.Line(new Vector2(TowerGeo.X0 - TowerGeo.Wall, roof - 10f), new Vector2(TowerGeo.X0 - TowerGeo.Wall, roof), 2.5f, chalk);
        m.Line(new Vector2(interiorRight + TowerGeo.Wall, roof - 10f), new Vector2(interiorRight + TowerGeo.Wall, roof), 2.5f, chalk);
        for (int i = 0; i < 3; i++)
        {
            float x = TowerGeo.X0 + 120f + i * (interiorRight - TowerGeo.X0 - 240f) / 2f - 40f;
            Rect unit = new Rect(x, roof - 44f, 80f, 34f);
            m.StrokeRect(unit, 2.5f, chalk);
            m.Circle(new Vector2(unit.x + 26f, unit.y + 17f), 11f, 2f, chalk);
            m.Line(new Vector2(unit.x + 26f, unit.y + 7f), new Vector2(unit.x + 26f, unit.y + 27f), 1.5f, ChalkA(0.8f));
            m.Line(new Vector2(unit.x + 16f, unit.y + 17f), new Vector2(unit.x + 36f, unit.y + 17f), 1.5f, ChalkA(0.8f));
            for (float gx = unit.x + 46f; gx < unit.xMax - 6f; gx += 7f)
                m.Line(new Vector2(gx, unit.y + 6f), new Vector2(gx, unit.yMax - 6f), 1.5f, ChalkA(0.7f));
        }
    }

    /// <summary>The office tower's roof: a railing and an antenna mast with its cross bars and a light.</summary>
    static void DrawTowerRoof(BlueprintMeshBuilder m, float roof, float interiorRight)
    {
        Color32 chalk = Chalk;
        Railing(m, roof, 556f, interiorRight - 28f);
        float x = (TowerGeo.X0 + interiorRight) * 0.5f + 60f;
        m.Line(new Vector2(x, roof), new Vector2(x, roof - 130f), 3f, chalk);
        m.Line(new Vector2(x - 18f, roof - 60f), new Vector2(x + 18f, roof - 60f), 2f, chalk);
        m.Line(new Vector2(x - 12f, roof - 90f), new Vector2(x + 12f, roof - 90f), 2f, chalk);
        m.Line(new Vector2(x - 26f, roof), new Vector2(x, roof - 40f), 2f, ChalkA(0.8f));
        m.Line(new Vector2(x + 26f, roof), new Vector2(x, roof - 40f), 2f, ChalkA(0.8f));
        m.FillCircle(new Vector2(x, roof - 134f), 5f, UIColors.Loss);
        m.Circle(new Vector2(x, roof - 134f), 5f, 2f, chalk);
    }

    /// <summary>The skyscraper's roof: two setbacks and a spire.</summary>
    static void DrawSkyscraperRoof(BlueprintMeshBuilder m, float roof, float interiorRight)
    {
        Color32 chalk = Chalk;
        float left = TowerGeo.X0 - TowerGeo.Wall, right = interiorRight + TowerGeo.Wall;
        float w = right - left;
        path.Clear();
        path.Add(new Vector2(left + w * 0.12f, roof));
        path.Add(new Vector2(left + w * 0.12f, roof - 40f));
        path.Add(new Vector2(left + w * 0.26f, roof - 40f));
        path.Add(new Vector2(left + w * 0.26f, roof - 80f));
        path.Add(new Vector2(left + w * 0.74f, roof - 80f));
        path.Add(new Vector2(left + w * 0.74f, roof - 40f));
        path.Add(new Vector2(left + w * 0.88f, roof - 40f));
        path.Add(new Vector2(left + w * 0.88f, roof));
        m.Stroke(path, 3f, chalk, false);
        for (float x = left + w * 0.3f; x < left + w * 0.72f; x += 22f)
            m.Line(new Vector2(x, roof - 74f), new Vector2(x, roof - 46f), 1.5f, ChalkA(0.7f));
        float mid = left + w * 0.5f;
        path.Clear();
        path.Add(new Vector2(mid - 22f, roof - 80f));
        path.Add(new Vector2(mid, roof - 230f));
        path.Add(new Vector2(mid + 22f, roof - 80f));
        m.Stroke(path, 3f, chalk, false);
        m.Line(new Vector2(mid, roof - 230f), new Vector2(mid, roof - 280f), 2.5f, chalk);
        m.FillCircle(new Vector2(mid, roof - 284f), 5f, UIColors.Loss);
        m.Circle(new Vector2(mid, roof - 284f), 5f, 2f, chalk);
    }

    /// <summary>
    /// A skybridge's walkway across the gap between two buildings (design px, see TowerGeo.BridgeRect): a glass tube with
    /// mullions, a roof and a floor slab, in white ink with a faint fill.
    /// </summary>
    public static void DrawBridge(BlueprintMeshBuilder m, Rect bridge, int floor)
    {
        Color32 chalk = Chalk;
        m.FillRect(bridge, ChalkA(0.14f), 0f);
        m.StrokeRect(bridge, 2.5f, chalk);
        m.Line(new Vector2(bridge.x, bridge.yMax + 4f), new Vector2(bridge.xMax, bridge.yMax + 4f), 3f, chalk);   // the slab
        m.Line(new Vector2(bridge.x, bridge.y - 5f), new Vector2(bridge.xMax, bridge.y - 5f), 2f, ChalkA(0.8f));  // the roof
        for (float x = bridge.x + 22f; x < bridge.xMax - 8f; x += 22f)
            m.Line(new Vector2(x, bridge.y + 2f), new Vector2(x, bridge.yMax - 2f), 1.5f, ChalkA(0.7f));
        // Two little people crossing
        TowerShape walker = TowerArt.Shape("stand");
        if (walker != null && bridge.width > 90f)
        {
            var palette = TowerPalette.For(UIColors.BlueprintInk, UIColors.Blueprint, 0.9f);
            float y = bridge.yMax - 2f - walker.height * 0.9f;
            TowerArt.Draw(m, walker, bridge.x + bridge.width * 0.35f, y, 0.9f, false, palette);
            TowerArt.Draw(m, walker, bridge.x + bridge.width * 0.6f, y, 0.9f, true, palette);
        }
    }

    static string Cloud(float x, float y)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        return "M" + x.ToString("0.#", inv) + " " + y.ToString("0.#", inv) +
               "h72c9 0 12-12 3-16 0-12-13-17-21-10-4-10-21-10-25 1-11-2-18 11-8 17-7 1-21 4-21 8z";
    }

    static string Birds(float x, float y)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        return "M" + x.ToString("0.#", inv) + " " + y.ToString("0.#", inv) + "q8-7 16 0q8-7 16 0";
    }

    /// <summary>The lift's cables from the machine room down to the car.</summary>
    public static void DrawCables(BlueprintMeshBuilder m, int floors, float carTop)
    {
        float roof = TowerGeo.TopOf(floors);
        Color32 c = ChalkA(0.85f);
        m.Line(new Vector2(507f, roof - 16f), new Vector2(507f, carTop), 1.5f, c);
        m.Line(new Vector2(521f, roof - 16f), new Vector2(521f, carTop), 1.5f, c);
    }

    static void StrokePath(BlueprintMeshBuilder m, string d, float width, Color32 color)
    {
        TowerPath p = TowerSvg.Get(d);
        if (p == null) return;
        for (int i = 0; i < p.subpaths.Count; i++)
        {
            path.Clear();
            path.AddRange(p.subpaths[i]);
            m.Stroke(path, width, color, p.closed[i]);
        }
    }

    // ------------------------------------------------------------------
    // Floors
    // ------------------------------------------------------------------

    /// <summary>
    /// One floor: its hatched slab, the level mark at the lift, the circled floor number, dashed boxes over the runs of
    /// free tiles (their labels are text on top) and the rooms in <paramref name="rooms"/> (this floor's, left to right).
    /// </summary>
    public static void DrawFloor(BlueprintMeshBuilder m, int floor, int tiles, List<TowerRoom> rooms, Dictionary<TowerRoom, int> seated,
                                 bool staffed, bool full, TowerRoom hidden)
    {
        Color32 chalk = Chalk;
        float top = TowerGeo.TopOf(floor), bottom = TowerGeo.BottomOf(floor);
        float right = TowerGeo.Right(tiles);

        // Slab (12 px, hatched every 12 px)
        m.StrokeRect(new Rect(TowerGeo.X0 - TowerGeo.Wall, top, right - TowerGeo.X0 + TowerGeo.Wall * 2f, TowerGeo.Slab), 2f, chalk);
        Color32 hatch = ChalkA(0.7f);
        for (float x = TowerGeo.X0 - 4f; x <= right - 8f; x += 12f)
            m.Line(new Vector2(x, top + 11f), new Vector2(x + 10f, top + 1f), 1.5f, hatch);
        // Level mark at the lift and the floor number's ring
        path.Clear();
        path.Add(new Vector2(490f, bottom));
        path.Add(new Vector2(536f, bottom));
        m.Dashed(path, 1.5f, ChalkA(0.7f), false, 4f, 5f);
        m.Circle(new Vector2(TowerGeo.NumberX + 14f, top + 78f), 13f, 2f, chalk);

        // Free runs of tiles
        float roomTop = TowerGeo.RoomTop(floor);
        int t = 0;
        while (t < tiles)
        {
            TowerRoom occupant = RoomCovering(rooms, t, hidden);
            if (occupant != null)
            {
                t = occupant.EndTile;
                continue;
            }
            int end = t;
            while (end + 1 < tiles && RoomCovering(rooms, end + 1, hidden) == null) end++;
            DrawEmptyRun(m, floor, t, end - t + 1);
            t = end + 1;
        }

        foreach (TowerRoom room in rooms)
        {
            if (room == hidden) continue;
            int people = seated != null && seated.TryGetValue(room, out int n) ? n : 0;
            DrawRoom(m, room.Definition, TowerGeo.RoomLeft(room.Tile), roomTop, people, staffed, full, 1f, true, room.Definition.LookAt(room.Level), room.Capacity);
        }
    }

    static TowerRoom RoomCovering(List<TowerRoom> rooms, int tile, TowerRoom hidden)
    {
        foreach (TowerRoom room in rooms)
            if (room != hidden && tile >= room.Tile && tile < room.EndTile) return room;
        return null;
    }

    /// <summary>A dashed box over free tiles with dotted tile dividers.</summary>
    public static void DrawEmptyRun(BlueprintMeshBuilder m, int floor, int tile, int count)
    {
        float roomTop = TowerGeo.RoomTop(floor);
        Rect box = new Rect(TowerGeo.RoomLeft(tile), roomTop + 4f, TowerGeo.RoomWidth(count), 124f);
        m.StrokeRoundRect(Inset(box, 1f), TowerGeo.RoomRadii, 2f, ChalkA(0.85f), 7f, 5f);
        Color32 tick = ChalkA(0.45f);
        for (int k = tile + 1; k < tile + count; k++)
        {
            float x = TowerGeo.X0 + TowerGeo.Tile * k;
            path.Clear();
            path.Add(new Vector2(x, roomTop + 16f));
            path.Add(new Vector2(x, roomTop + TowerGeo.RoomHeight - 16f));
            m.Dashed(path, 1.5f, tick, false, 3f, 7f);
        }
    }

    /// <summary>
    /// A room: coloured paper cut-out with its shadow, border and icon, and what stands in it. Seat figures show one per
    /// person seated there; the others show once there is staff (<paramref name="staffed"/>) or when the tower is full.
    /// </summary>
    public static void DrawRoom(BlueprintMeshBuilder m, TowerRoomDefinition definition, float left, float top, int seated, bool staffed,
                                bool full, float alpha, bool things = true, int look = 0, int capacity = -1)
    {
        Rect r = new Rect(left, top, TowerGeo.RoomWidth(definition.Tiles), TowerGeo.RoomHeight);
        Vector4 radii = TowerGeo.RoomRadii;
        Color paper = definition.paper;
        paper.a *= alpha;
        m.FillRoundRect(new Rect(r.x + 2f, r.y + 3f, r.width, r.height), radii, UIColors.WithAlpha(RoomShadow, RoomShadow.a * alpha));
        if (definition.role == TowerRoomRole.CasinoRoom)
        {
            // The gradient's centre is in frame units (50% across, 35% down the room)
            m.FillRoundRectRadial(r, radii, new Vector2(r.x + r.width * 0.5f, r.y + r.height * 0.35f), UIColors.WithAlpha(FeltCenter, alpha),
                                  UIColors.WithAlpha(FeltEdge, alpha));
            m.StrokeRoundRect(Inset(r, 3.5f), Grow(radii, -3.5f), 3f, UIColors.WithAlpha(GoldInset, GoldInset.a * alpha));
        }
        else
        {
            m.FillRoundRect(r, radii, paper);
        }
        if (look > 0) DrawLook(m, r, radii, look, alpha, definition.ink);
        m.StrokeRoundRect(Inset(r, 1f), Grow(radii, -1f), 2f, UIColors.WithAlpha(UIColors.Ink, alpha));

        // Icon at the top-left (the name is text drawn on top)
        TowerPart[] icon = TowerArt.Icon(TowerArt.IconFor(definition.role));
        if (icon != null)
        {
            m.PushFrame();
            m.SetFrame(m.Map(new Vector2(left + 8f, top + 7f)), m.MapDir(new Vector2(22f / 24f, 0f)), m.MapDir(new Vector2(0f, 22f / 24f)));
            TowerArt.DrawParts(m, icon, TowerPalette.For(definition.ink, definition.paper, alpha));
            m.PopFrame();
        }

        if (things) DrawThings(m, definition, left, top, seated, staffed, full, alpha, capacity);
    }

    /// <summary>
    /// A room's level, drawn (TowerRoomLevel.look): 1 a pencil trim inside the wall, 2 also a wooden floor strip and gold corner
    /// marks, 3 also a gold inset line and a star by the name.
    /// </summary>
    static void DrawLook(BlueprintMeshBuilder m, Rect r, Vector4 radii, int look, float alpha, Color ink)
    {
        Color32 trim = UIColors.WithAlpha(UIColors.Ink, 0.28f * alpha);
        m.StrokeRoundRect(Inset(r, 7f), Grow(radii, -6f), 1.5f, trim);
        if (look >= 2)
        {
            m.FillRect(new Rect(r.x + 3f, r.yMax - 8f, r.width - 6f, 5f), UIColors.WithAlpha(UIColors.Manila, alpha), 0f);
            m.Line(new Vector2(r.x + 3f, r.yMax - 8f), new Vector2(r.xMax - 3f, r.yMax - 8f), 1.5f, trim);
            Color32 gold = UIColors.WithAlpha(GoldInset, GoldInset.a * alpha);
            float c = 10f;
            m.Line(new Vector2(r.xMax - 12f, r.y + 5f), new Vector2(r.xMax - 12f + c, r.y + 5f), 2f, gold);
            m.Line(new Vector2(r.xMax - 5f, r.y + 5f), new Vector2(r.xMax - 5f, r.y + 5f + c), 2f, gold);
        }
        if (look >= 3)
        {
            m.StrokeRoundRect(Inset(r, 4f), Grow(radii, -3f), 2.5f, UIColors.WithAlpha(GoldInset, GoldInset.a * alpha));
            TowerPart[] star = TowerArt.Icon(TowerIconKind.Star);
            if (star != null)
            {
                m.PushFrame();
                m.SetFrame(m.Map(new Vector2(r.xMax - 30f, r.y + 8f)), m.MapDir(new Vector2(16f / 24f, 0f)), m.MapDir(new Vector2(0f, 16f / 24f)));
                TowerArt.DrawParts(m, star, TowerPalette.For(UIColors.Gold, UIColors.Gold, alpha), 1.2f);
                m.PopFrame();
            }
        }
    }

    /// <summary>The room view's touches for a room's level: a framed picture (1), a rug and a gold wall trim (2), a chandelier (3).</summary>
    public static void DrawRoomViewLook(BlueprintMeshBuilder m, int look, float width, float floorY)
    {
        if (look <= 0) return;
        Color32 ink = UIColors.Ink;
        // A framed picture right of the poster
        Rect frame = new Rect(width * 0.62f, 50f, 70f, 54f);
        m.FillRoundRect(frame, 3f, UIColors.PaperHi);
        m.StrokeRoundRect(frame, new Vector4(3f, 3f, 3f, 3f), 4f, UIColors.Gold);
        m.Line(new Vector2(frame.x + 12f, frame.yMax - 12f), new Vector2(frame.x + 30f, frame.y + 20f), 3f, UIColors.Money);
        m.Line(new Vector2(frame.x + 30f, frame.y + 20f), new Vector2(frame.xMax - 12f, frame.yMax - 14f), 3f, UIColors.Money);
        if (look >= 2)
        {
            // A rug on the floor and a gold trim along the wall
            m.FillRoundRect(new Rect(width * 0.3f, floorY + 3f, width * 0.4f, 12f), 6f, UIColors.WithAlpha(UIColors.Loss, 0.55f));
            m.Line(new Vector2(0f, 190f), new Vector2(width, 190f), 3f, UIColors.WithAlpha(UIColors.Gold, 0.85f));
        }
        if (look >= 3)
        {
            // A chandelier
            Vector2 c = new Vector2(width * 0.5f, 36f);
            m.Line(new Vector2(c.x, 0f), c, 2f, ink);
            m.FillRoundRect(new Rect(c.x - 36f, c.y, 72f, 10f), 4f, UIColors.Gold);
            m.StrokeRoundRect(new Rect(c.x - 36f, c.y, 72f, 10f), new Vector4(4f, 4f, 4f, 4f), 2f, ink);
            for (int i = -2; i <= 2; i++)
            {
                Vector2 p = new Vector2(c.x + i * 16f, c.y + 18f);
                m.FillCircle(p, 5f, UIColors.Highlight);
                m.Circle(p, 5f, 1.5f, ink);
            }
        }
    }

    /// <summary>Furniture at the back, then the figures, then counters in front (as on the Tower board).</summary>
    public static void DrawThings(BlueprintMeshBuilder m, TowerRoomDefinition definition, float left, float top, int seated, bool staffed,
                                  bool full, float alpha, int capacity = -1)
    {
        TowerThing[] layout = TowerArt.Layout(definition.role, definition.Tiles);
        if (layout == null) return;
        // More places than seat figures (an open-plan office, a cubicle farm): every figure stands for a share of the people
        int figures = 0;
        foreach (TowerThing thing in layout) if (thing.set == TowerThingSet.Seat) figures++;
        if (capacity > figures && figures > 0 && seated > 0) seated = Mathf.Max(1, Mathf.CeilToInt(seated * (float)figures / capacity));
        var palette = TowerPalette.For(definition.ink, definition.paper, alpha);
        float bottom = top + TowerGeo.RoomHeight;
        float roomWidth = TowerGeo.RoomWidth(definition.Tiles);

        order.Clear();
        int seatIndex = 0;
        for (int pass = 0; pass < 3; pass++)
        {
            seatIndex = 0;
            foreach (TowerThing thing in layout)
            {
                int wanted = thing.front ? 2 : thing.figure ? 1 : 0;
                bool shown;
                switch (thing.set)
                {
                    case TowerThingSet.Seat: shown = seatIndex++ < seated; break;
                    case TowerThingSet.Staffed: shown = staffed; break;
                    case TowerThingSet.Full: shown = full; break;
                    default: shown = true; break;
                }
                if (wanted != pass || !shown) continue;
                TowerShape shape = TowerArt.Shape(thing.shape);
                if (shape == null || thing.x + shape.width > roomWidth + 1f) continue;
                float y = float.IsNaN(thing.y) ? bottom - 2f - shape.height : top + thing.y;
                TowerArt.Draw(m, shape, left + thing.x, y, 1f, thing.flip, palette);
            }
        }
    }

    // ------------------------------------------------------------------
    // Overlays
    // ------------------------------------------------------------------

    /// <summary>Highlighter ring around a room: 4 px yellow, 5 px out, with a thin ink line on both sides.</summary>
    public static void DrawSelection(BlueprintMeshBuilder m, Rect room)
    {
        Rect ring = new Rect(room.x - 5f, room.y - 5f, room.width + 10f, room.height + 10f);
        Vector4 radii = new Vector4(6f, 9f, 6f, 9f);
        m.StrokeRoundRect(Inset(ring, -1f), Grow(radii, 1f), 2f, UIColors.Ink);
        m.StrokeRoundRect(Inset(ring, 2f), radii, 4f, UIColors.Highlight);
        m.StrokeRoundRect(Inset(ring, 5f), Grow(radii, -3f), 2f, UIColors.Ink);
    }

    /// <summary>Light outline of the room under the pointer (red for Sell, dashed for Move).</summary>
    public static void DrawHover(BlueprintMeshBuilder m, Rect room, TowerTool tool, bool builtIn)
    {
        Rect r = new Rect(room.x - 3f, room.y - 3f, room.width + 6f, room.height + 6f);
        Vector4 radii = new Vector4(5f, 7f, 5f, 7f);
        if (tool == TowerTool.Sell && !builtIn)
        {
            m.FillRoundRect(r, radii, UIColors.WithAlpha(UIColors.Loss, 0.18f));
            m.StrokeRoundRect(Inset(r, 1.5f), radii, 3f, UIColors.Loss);
        }
        else if (tool == TowerTool.Move && !builtIn)
        {
            m.StrokeRoundRect(Inset(r, 1.5f), radii, 3f, UIColors.Highlight, 10f, 6f);
        }
        else
        {
            m.StrokeRoundRect(Inset(r, 1f), radii, 2f, UIColors.WithAlpha(UIColors.Highlight, 0.75f));
        }
    }

    /// <summary>Placement ghost: the room at 60% with a solid green (fits) or dashed red (doesn't) outline 5 px out.</summary>
    public static void DrawGhost(BlueprintMeshBuilder m, TowerRoomDefinition definition, int floor, int tile, bool fits)
    {
        float left = TowerGeo.RoomLeft(tile), top = TowerGeo.RoomTop(floor);
        DrawRoom(m, definition, left, top, 0, false, false, 0.6f);
        Rect outline = new Rect(left - 5f, top - 5f, TowerGeo.RoomWidth(definition.Tiles) + 10f, TowerGeo.RoomHeight + 10f);
        Vector4 radii = new Vector4(6f, 9f, 6f, 9f);
        if (fits)
            m.StrokeRoundRect(Inset(outline, 2f), radii, 4f, UIColors.Money);
        else
            m.StrokeRoundRect(Inset(outline, 2f), radii, 4f, UIColors.Loss, 12f, 8f);
    }

    /// <summary>A skybridge being placed: its walkway in green (it can go there) or red.</summary>
    public static void DrawBridgeGhost(BlueprintMeshBuilder m, Rect bridge, bool fits)
    {
        Rect r = new Rect(bridge.x - 4f, bridge.y - 4f, bridge.width + 8f, bridge.height + 8f);
        m.FillRoundRect(r, 4f, UIColors.WithAlpha(fits ? BlueprintUI.GhostGood : BlueprintUI.GhostBad, 0.35f));
        if (fits) m.StrokeRoundRect(Inset(r, 2f), new Vector4(4f, 4f, 4f, 4f), 4f, UIColors.Money);
        else m.StrokeRoundRect(Inset(r, 2f), new Vector4(4f, 4f, 4f, 4f), 4f, UIColors.Loss, 12f, 8f);
    }

    /// <summary>Where a room being moved came from: a dashed outline.</summary>
    public static void DrawMoveSource(BlueprintMeshBuilder m, Rect room)
    {
        m.StrokeRoundRect(Inset(room, 1f), TowerGeo.RoomRadii, 2f, ChalkA(0.8f), 8f, 6f);
    }

    /// <summary>The add-floor slot over the roof: dashed while idle, a solid highlighter outline on a yellow wash when hovered.</summary>
    public static void DrawFloorSlot(BlueprintMeshBuilder m, int floors, int tiles, bool hover, bool open)
    {
        float top = TowerGeo.TopOf(floors + 1);
        Rect r = new Rect(TowerGeo.X0, top, TowerGeo.Right(tiles) - TowerGeo.X0, TowerGeo.FloorHeight);
        if (hover || open)
        {
            m.FillRoundRect(r, TowerGeo.RoomRadii, UIColors.WithAlpha(UIColors.Highlight, 0.14f));
            m.StrokeRoundRect(Inset(r, 2f), TowerGeo.RoomRadii, 4f, UIColors.Highlight);
        }
        else
        {
            m.StrokeRoundRect(Inset(r, 1.5f), TowerGeo.RoomRadii, 3f, ChalkA(0.9f), 10f, 7f);
        }
    }

    public static Rect Inset(Rect r, float by)
    {
        return new Rect(r.x + by, r.y + by, r.width - by * 2f, r.height - by * 2f);
    }

    static Vector4 Grow(Vector4 r, float by)
    {
        return new Vector4(Mathf.Max(0f, r.x + by), Mathf.Max(0f, r.y + by), Mathf.Max(0f, r.z + by), Mathf.Max(0f, r.w + by));
    }
}
