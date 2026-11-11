using System;
using System.Collections.Generic;

public enum CityPlotSize { Small, Medium, Tall, Large, Landmark }

/// <summary>What fills one slot of a city's grid.</summary>
public enum CitySlotKind { Open, Block, Landmark, Park, Water, Bridge, Island }

/// <summary>A plot on the map. Everything here is fixed; who owns it lives in CityDistrict.</summary>
public class CityPlotDef
{
    public string Id;          // e.g. "bey014" - stored in Cloud Save, never change once the city is live
    public string CityId;
    public string DistrictId;
    public string BlockId;
    public int Number;         // shown as "Plot #214" (per city)
    public float X, Y, W, H;   // map units, origin top-left
    public CityPlotSize Size;
    public long BasePrice;
    public bool Waterfront;
    public string Name;        // landmarks only

    public float CenterX => X + W * 0.5f;
    public float CenterY => Y + H * 0.5f;

    public string SizeName
    {
        get
        {
            switch (Size)
            {
                case CityPlotSize.Medium: return "Wide lot";
                case CityPlotSize.Tall: return "Deep lot";
                case CityPlotSize.Large: return "Big lot";
                case CityPlotSize.Landmark: return "Landmark";
                default: return "Corner lot";
            }
        }
    }

    public bool Contains(float x, float y) => x >= X && x <= X + W && y >= Y && y <= Y + H;
}

public struct CityRectF
{
    public float X, Y, W, H;
    public CityRectF(float x, float y, float w, float h) { X = x; Y = y; W = w; H = h; }
    public float CenterX => X + W * 0.5f;
    public float CenterY => Y + H * 0.5f;
}

public class CityBlockDef
{
    public string Id;
    public string DistrictId;
    public int Column, Row;
    public float X, Y, W, H;
    public bool IsLandmark;
    public bool IsIsland;
    public List<CityPlotDef> Plots = new List<CityPlotDef>();
}

public class CityDistrictDef
{
    public string Id;
    public string Name;
    public string CityId;
    public int Index;          // grid letter: 0 = 'a'
    public double PriceRate;
    public float LabelX, LabelY;
    public List<CityBlockDef> Blocks = new List<CityBlockDef>();
    public List<CityPlotDef> Plots = new List<CityPlotDef>();
}

public class CityParkDef
{
    public string Name;
    public List<CityRectF> Areas = new List<CityRectF>();
    public float LabelX, LabelY;
}

public class CityBridgeDef
{
    public int Column, Row;
    public bool Horizontal;
    public CityRectF Deck;
}

public class CityIslandDef
{
    public int Column, Row;
    public float CenterX, CenterY, Radius;
    public string PlotId;
}

/// <summary>One city's map, built from its grid (CityWorlds). The game and the server build the exact same map.</summary>
public class CityMap
{
    public string CityId;
    public CityWorldDef World;
    public int Columns, Rows;
    public float Width, Height;
    public CitySlotKind[,] Kinds;
    public int[,] DistrictAt;    // district index of block/landmark slots, -1 elsewhere
    public List<CityDistrictDef> Districts = new List<CityDistrictDef>();
    public List<CityBlockDef> Blocks = new List<CityBlockDef>();
    public List<CityPlotDef> Plots = new List<CityPlotDef>();
    public List<CityParkDef> Parks = new List<CityParkDef>();
    public List<CityBridgeDef> Bridges = new List<CityBridgeDef>();
    public List<CityIslandDef> Islands = new List<CityIslandDef>();
    public Dictionary<string, CityPlotDef> PlotsById = new Dictionary<string, CityPlotDef>();
    public Dictionary<string, CityBlockDef> BlocksById = new Dictionary<string, CityBlockDef>();
    public Dictionary<string, CityDistrictDef> DistrictsById = new Dictionary<string, CityDistrictDef>();

    public string Name => World.Name;
    public IEnumerable<string> DistrictIds { get { foreach (var d in Districts) yield return d.Id; } }

    public bool InGrid(int column, int row) => column >= 0 && row >= 0 && column < Columns && row < Rows;

    public CitySlotKind KindAt(int column, int row) => InGrid(column, row) ? Kinds[row, column] : CitySlotKind.Open;

    public bool IsWet(int column, int row)
    {
        var kind = KindAt(column, row);
        return kind == CitySlotKind.Water || kind == CitySlotKind.Bridge || kind == CitySlotKind.Island;
    }

    public bool IsLand(int column, int row)
    {
        var kind = KindAt(column, row);
        return InGrid(column, row) && (kind == CitySlotKind.Block || kind == CitySlotKind.Landmark || kind == CitySlotKind.Park);
    }

    public float SlotX(int column) => CityLayout.Margin + column * CityLayout.SlotW;
    public float SlotY(int row) => CityLayout.Margin + row * CityLayout.SlotH;
    public CityRectF SlotRect(int column, int row) => new CityRectF(SlotX(column), SlotY(row), CityLayout.SlotW, CityLayout.SlotH);

    public CityPlotDef PlotAt(float x, float y)
    {
        foreach (var plot in Plots)
        {
            if (plot.Contains(x, y)) return plot;
        }
        return null;
    }
}

/// <summary>
/// Builds the six city maps from their grids: each block slot is cut into plots of different sizes (a pattern picked
/// by a seeded random), landmark blocks hold one big named plot, islands one landmark plot in the water.
/// Deterministic: the game and the server build the exact same maps from this code.
/// LayoutVersion 2 is live data - changing the grids or the cutting moves or renames plots people own.
/// </summary>
public static class CityLayout
{
    public const int LayoutVersion = 2;

    public const float Cell = 100f;
    public const int BlockCellsX = 4, BlockCellsY = 3;
    public const float Street = 50f;
    public const float SlotW = BlockCellsX * Cell + Street;   // 450
    public const float SlotH = BlockCellsY * Cell + Street;   // 350
    public const float Margin = 200f;
    public const float PlotInset = 8f;

    public const long SmallPrice = 100_000_000L;
    public const long MediumPrice = 220_000_000L;
    public const long LargePrice = 480_000_000L;
    public const long LandmarkPrice = 2_000_000_000L;
    public const double WaterfrontRate = 1.1;

    /// <summary>The cities in the order the city picker shows them.</summary>
    public static IReadOnlyList<CityWorldDef> Worlds => CityWorlds.All;

    // Ways to cut a 4x3-cell block into plots: equal letters form one plot; 'L' is the landmark.
    static readonly string[][] Patterns =
    {
        new[] { "abcd", "efgh", "ijkl" },
        new[] { "aabc", "aade", "ffgg" },
        new[] { "aabb", "cdef", "gghi" },
        new[] { "abcc", "decc", "ffgg" },
        new[] { "acde", "acfg", "bhii" },
    };
    static readonly int[] PatternWeights = { 1, 3, 3, 3, 2 };
    static readonly string[] LandmarkPattern = { "aLLb", "cLLd", "eeff" };

    static readonly Dictionary<string, CityMap> cache = new Dictionary<string, CityMap>();
    static readonly object cacheLock = new object();

    /// <summary>A city's map (null for an unknown city id).</summary>
    public static CityMap Get(string cityId)
    {
        lock (cacheLock)
        {
            if (cityId != null && cache.TryGetValue(cityId, out var map)) return map;
            var world = CityWorlds.Get(cityId);
            if (world == null) return null;
            map = Build(world);
            cache[cityId] = map;
            return map;
        }
    }

    static CityMap Build(CityWorldDef world)
    {
        int rows = world.Grid.Length;
        int columns = world.Grid[0].Length;
        foreach (string line in world.Grid)
        {
            if (line.Length != columns) throw new InvalidOperationException($"{world.Id}: every grid row needs {columns} slots");
        }

        var map = new CityMap
        {
            CityId = world.Id,
            World = world,
            Columns = columns,
            Rows = rows,
            Width = Margin * 2 + columns * SlotW,
            Height = Margin * 2 + rows * SlotH,
            Kinds = new CitySlotKind[rows, columns],
            DistrictAt = new int[rows, columns],
        };

        for (int i = 0; i < world.Districts.Length; i++)
        {
            var seed = world.Districts[i];
            var district = new CityDistrictDef { Id = seed.Id, Name = seed.Name, CityId = world.Id, Index = i, PriceRate = seed.Rate };
            map.Districts.Add(district);
            map.DistrictsById[district.Id] = district;
        }

        // What every slot is
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                char c = world.Grid[row][column];
                int district = -1;
                CitySlotKind kind;
                if (c >= 'a' && c <= 'h') { kind = CitySlotKind.Block; district = c - 'a'; }
                else if (c >= 'A' && c <= 'H') { kind = CitySlotKind.Landmark; district = c - 'A'; }
                else if (c == '~') kind = CitySlotKind.Water;
                else if (c == '=') kind = CitySlotKind.Bridge;
                else if (c == '@') kind = CitySlotKind.Island;
                else if (c == '*') kind = CitySlotKind.Park;
                else if (c == '.') kind = CitySlotKind.Open;
                else throw new InvalidOperationException($"{world.Id}: unknown grid character '{c}'");
                if (district >= world.Districts.Length) throw new InvalidOperationException($"{world.Id}: no district for '{c}'");
                map.Kinds[row, column] = kind;
                map.DistrictAt[row, column] = district;
            }
        }

        // Blocks, landmarks and islands in reading order: that order numbers the plots
        int number = 1, landmarkIndex = 0;
        var localIndex = new Dictionary<string, int>();
        var blockIndex = new Dictionary<string, int>();
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                var kind = map.Kinds[row, column];
                if (kind != CitySlotKind.Block && kind != CitySlotKind.Landmark && kind != CitySlotKind.Island) continue;

                string landmarkName = null;
                CityDistrictDef district;
                if (kind == CitySlotKind.Block)
                {
                    district = map.Districts[map.DistrictAt[row, column]];
                }
                else
                {
                    if (landmarkIndex >= world.Landmarks.Length) throw new InvalidOperationException($"{world.Id}: more landmarks on the grid than names");
                    var landmark = world.Landmarks[landmarkIndex++];
                    landmarkName = landmark.Name;
                    if (kind == CitySlotKind.Island)
                    {
                        if (landmark.DistrictId == null || !map.DistrictsById.TryGetValue(landmark.DistrictId, out district))
                            throw new InvalidOperationException($"{world.Id}: island '{landmark.Name}' needs a district");
                    }
                    else
                    {
                        district = map.Districts[map.DistrictAt[row, column]];
                    }
                }

                blockIndex.TryGetValue(district.Id, out int bi);
                blockIndex[district.Id] = bi + 1;
                var block = new CityBlockDef
                {
                    Id = $"{district.Id}-b{bi:D2}",
                    DistrictId = district.Id,
                    Column = column,
                    Row = row,
                    X = map.SlotX(column) + Street * 0.5f,
                    Y = map.SlotY(row) + Street * 0.5f,
                    W = BlockCellsX * Cell,
                    H = BlockCellsY * Cell,
                    IsLandmark = kind != CitySlotKind.Block,
                    IsIsland = kind == CitySlotKind.Island,
                };
                district.Blocks.Add(block);
                map.Blocks.Add(block);
                map.BlocksById[block.Id] = block;

                List<PatternCut> cuts;
                if (kind == CitySlotKind.Island) cuts = new List<PatternCut> { new PatternCut { X = 1, Y = 0, W = 2, H = 2, Size = CityPlotSize.Landmark } };
                else if (kind == CitySlotKind.Landmark) cuts = Cut(LandmarkPattern);
                else cuts = Cut(PickPattern(new CityRandom($"block:{world.Id}:{column}:{row}")));

                foreach (var cut in cuts)
                {
                    localIndex.TryGetValue(district.Id, out int local);
                    local++;
                    localIndex[district.Id] = local;

                    var plot = new CityPlotDef
                    {
                        Id = $"{district.Id}{local:D3}",
                        CityId = world.Id,
                        DistrictId = district.Id,
                        BlockId = block.Id,
                        Number = number++,
                        Size = cut.Size,
                        Name = cut.Size == CityPlotSize.Landmark ? landmarkName : null,
                    };
                    if (kind == CitySlotKind.Island)
                    {
                        // One round island in the middle of the water slot, the landmark plot on top of it
                        float size = 2 * Cell - PlotInset * 2;
                        var slot = map.SlotRect(column, row);
                        plot.X = slot.CenterX - size * 0.5f;
                        plot.Y = slot.CenterY - size * 0.5f;
                        plot.W = size;
                        plot.H = size;
                        plot.Waterfront = true;
                        map.Islands.Add(new CityIslandDef
                        {
                            Column = column,
                            Row = row,
                            CenterX = slot.CenterX,
                            CenterY = slot.CenterY,
                            Radius = Cell * 1.45f,
                            PlotId = plot.Id,
                        });
                    }
                    else
                    {
                        plot.X = block.X + cut.X * Cell + PlotInset;
                        plot.Y = block.Y + cut.Y * Cell + PlotInset;
                        plot.W = cut.W * Cell - PlotInset * 2;
                        plot.H = cut.H * Cell - PlotInset * 2;
                        plot.Waterfront = (cut.Y == 0 && map.IsWet(column, row - 1))
                            || (cut.Y + cut.H == BlockCellsY && map.IsWet(column, row + 1))
                            || (cut.X == 0 && map.IsWet(column - 1, row))
                            || (cut.X + cut.W == BlockCellsX && map.IsWet(column + 1, row));
                    }
                    plot.BasePrice = PriceFor(plot.Size, district.PriceRate, plot.Waterfront);

                    block.Plots.Add(plot);
                    district.Plots.Add(plot);
                    map.Plots.Add(plot);
                    map.PlotsById[plot.Id] = plot;
                }
            }
        }
        if (landmarkIndex != world.Landmarks.Length) throw new InvalidOperationException($"{world.Id}: {world.Landmarks.Length} landmark names for {landmarkIndex} landmarks");

        PlaceDistrictLabels(map);
        BuildParks(map);
        BuildBridges(map);
        return map;
    }

    /// <summary>Each district's name goes on its slot nearest to the middle of all its slots.</summary>
    static void PlaceDistrictLabels(CityMap map)
    {
        foreach (var district in map.Districts)
        {
            var slots = new List<(int column, int row)>();
            foreach (var block in district.Blocks)
            {
                if (!block.IsIsland) slots.Add((block.Column, block.Row));
            }
            if (slots.Count == 0)
            {
                var first = district.Blocks.Count > 0 ? district.Blocks[0] : null;
                if (first != null) slots.Add((first.Column, first.Row));
            }
            if (slots.Count == 0) continue;

            float cx = 0, cy = 0;
            foreach (var (column, row) in slots) { cx += column; cy += row; }
            cx /= slots.Count;
            cy /= slots.Count;
            var best = slots[0];
            float bestDistance = float.MaxValue;
            foreach (var slot in slots)
            {
                float dx = slot.column - cx, dy = slot.row - cy;
                float distance = dx * dx + dy * dy;
                if (distance < bestDistance - 0.0001f)
                {
                    bestDistance = distance;
                    best = slot;
                }
            }
            var rect = map.SlotRect(best.column, best.row);
            district.LabelX = rect.CenterX;
            district.LabelY = rect.CenterY;
        }
    }

    /// <summary>Touching park slots form one park (named in reading order); a rectangle of slots becomes one big lawn.</summary>
    static void BuildParks(CityMap map)
    {
        var seen = new bool[map.Rows, map.Columns];
        int index = 0;
        for (int row = 0; row < map.Rows; row++)
        {
            for (int column = 0; column < map.Columns; column++)
            {
                if (map.Kinds[row, column] != CitySlotKind.Park || seen[row, column]) continue;

                var slots = new List<(int column, int row)>();
                var stack = new Stack<(int column, int row)>();
                stack.Push((column, row));
                seen[row, column] = true;
                while (stack.Count > 0)
                {
                    var (c, r) = stack.Pop();
                    slots.Add((c, r));
                    foreach (var (dc, dr) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        int nc = c + dc, nr = r + dr;
                        if (!map.InGrid(nc, nr) || seen[nr, nc] || map.Kinds[nr, nc] != CitySlotKind.Park) continue;
                        seen[nr, nc] = true;
                        stack.Push((nc, nr));
                    }
                }

                int minC = int.MaxValue, minR = int.MaxValue, maxC = int.MinValue, maxR = int.MinValue;
                foreach (var (c, r) in slots)
                {
                    minC = Math.Min(minC, c); maxC = Math.Max(maxC, c);
                    minR = Math.Min(minR, r); maxR = Math.Max(maxR, r);
                }

                var names = map.World.Parks;
                var park = new CityParkDef { Name = names != null && index < names.Length ? names[index] : "City Park" };
                index++;
                float inset = Street * 0.5f;
                if (slots.Count == (maxC - minC + 1) * (maxR - minR + 1))
                {
                    park.Areas.Add(new CityRectF(map.SlotX(minC) + inset, map.SlotY(minR) + inset,
                        (maxC - minC + 1) * SlotW - inset * 2, (maxR - minR + 1) * SlotH - inset * 2));
                }
                else
                {
                    foreach (var (c, r) in slots)
                        park.Areas.Add(new CityRectF(map.SlotX(c) + inset, map.SlotY(r) + inset, SlotW - inset * 2, SlotH - inset * 2));
                }
                park.LabelX = (map.SlotX(minC) + map.SlotX(maxC) + SlotW) * 0.5f;
                park.LabelY = (map.SlotY(minR) + map.SlotY(maxR) + SlotH) * 0.5f;
                map.Parks.Add(park);
            }
        }
    }

    /// <summary>A bridge runs across (land on its left and right) or along the column (land above and below).</summary>
    static void BuildBridges(CityMap map)
    {
        for (int row = 0; row < map.Rows; row++)
        {
            for (int column = 0; column < map.Columns; column++)
            {
                if (map.Kinds[row, column] != CitySlotKind.Bridge) continue;

                int left = column, right = column;
                while (map.KindAt(left - 1, row) == CitySlotKind.Bridge) left--;
                while (map.KindAt(right + 1, row) == CitySlotKind.Bridge) right++;
                bool across = map.IsLand(left - 1, row) && map.IsLand(right + 1, row);

                int top = row, bottom = row;
                while (map.KindAt(column, top - 1) == CitySlotKind.Bridge) top--;
                while (map.KindAt(column, bottom + 1) == CitySlotKind.Bridge) bottom++;
                bool along = map.IsLand(column, top - 1) && map.IsLand(column, bottom + 1);

                bool horizontal = across || !along;
                var slot = map.SlotRect(column, row);
                const float deck = 110f;
                map.Bridges.Add(new CityBridgeDef
                {
                    Column = column,
                    Row = row,
                    Horizontal = horizontal,
                    Deck = horizontal
                        ? new CityRectF(slot.X, slot.CenterY - deck * 0.5f, slot.W, deck)
                        : new CityRectF(slot.CenterX - deck * 0.5f, slot.Y, deck, slot.H),
                });
            }
        }
    }

    static string[] PickPattern(CityRandom rng)
    {
        int total = 0;
        foreach (int w in PatternWeights) total += w;
        int roll = rng.Range(0, total);
        for (int i = 0; i < Patterns.Length; i++)
        {
            if (roll < PatternWeights[i]) return Patterns[i];
            roll -= PatternWeights[i];
        }
        return Patterns[0];
    }

    public static long PriceFor(CityPlotSize size, double districtRate, bool waterfront)
    {
        long basePrice;
        switch (size)
        {
            case CityPlotSize.Medium:
            case CityPlotSize.Tall: basePrice = MediumPrice; break;
            case CityPlotSize.Large: basePrice = LargePrice; break;
            case CityPlotSize.Landmark: basePrice = LandmarkPrice; break;
            default: basePrice = SmallPrice; break;
        }
        return CityRules.RoundNice(basePrice * districtRate * (waterfront ? WaterfrontRate : 1.0));
    }

    struct PatternCut
    {
        public int X, Y, W, H;
        public CityPlotSize Size;
    }

    /// <summary>Turn a letter pattern into plot rectangles (in cells), in reading order of their top-left cell.</summary>
    static List<PatternCut> Cut(string[] pattern)
    {
        var cuts = new List<PatternCut>();
        var seen = new HashSet<char>();
        for (int y = 0; y < pattern.Length; y++)
        {
            for (int x = 0; x < pattern[y].Length; x++)
            {
                char c = pattern[y][x];
                if (!seen.Add(c)) continue;

                int w = 0, h = 0;
                while (x + w < pattern[y].Length && pattern[y][x + w] == c) w++;
                while (y + h < pattern.Length && pattern[y + h][x] == c) h++;

                CityPlotSize size;
                if (c == 'L') size = CityPlotSize.Landmark;
                else if (w * h >= 4) size = CityPlotSize.Large;
                else if (w * h == 2) size = w > h ? CityPlotSize.Medium : CityPlotSize.Tall;
                else size = CityPlotSize.Small;

                cuts.Add(new PatternCut { X = x, Y = y, W = w, H = h, Size = size });
            }
        }
        return cuts;
    }
}
