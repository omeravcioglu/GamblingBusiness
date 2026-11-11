using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// One city's map: paper ground with its water, coastline, bridges, islands, parks and district borders, plus one
/// element per plot showing its owner's logo and name. Drag to pan, scroll to zoom (towards the cursor), click a plot
/// to select it. District names show from afar; names and prices appear as you zoom in.
/// </summary>
public class CityMapView : VisualElement
{
    public event Action<CityPlotDef> PlotClicked;
    /// <summary>Plot under the pointer (null when none) and the pointer position in this element.</summary>
    public event Action<CityPlotDef, Vector2> PlotHovered;

    public const float MaxZoom = 1.8f;
    const float DragThreshold = 6f;

    /// <summary>Pale tints that tell the districts apart (district index order).</summary>
    public static readonly Color[] DistrictTints =
    {
        CityUi.Hex("F6D7C3"), CityUi.Hex("D9E6F5"), CityUi.Hex("E5DDF3"), CityUi.Hex("DDEFD5"),
        CityUi.Hex("F8E7B8"), CityUi.Hex("F3D6DE"), CityUi.Hex("D5EEEA"), CityUi.Hex("E9E1D2"),
    };

    /// <summary>How plots are coloured: by their owner's logo colour, or by what they cost you.</summary>
    public enum Colouring { Owners, Prices }

    /// <summary>Price colours from cheap to dear (the Prices view and its legend).</summary>
    public static readonly Color[] PriceRamp =
    {
        CityUi.Hex("CDEBA6"), CityUi.Hex("FFE680"), CityUi.Hex("F6B98C"), CityUi.Hex("E8742C"), CityUi.Hex("C0352B"), CityUi.Hex("7A4FB5"),
    };

    public readonly CityMap Map;
    Colouring colouring = Colouring.Owners;
    CityOnline lastCity;
    long cheapest, dearest;
    readonly VisualElement content;
    readonly VisualElement farLabels;
    readonly VisualElement nearLabels;
    readonly Dictionary<string, PlotView> views = new Dictionary<string, PlotView>();

    float zoom = 0.25f;
    Vector2 pan;
    bool fitted;
    int lod = -1;

    int activePointer = -1;
    Vector2 downLocal, lastLocal;
    bool dragging;
    string hoveredId, selectedId;

    public float Zoom => zoom;
    public float MinZoom { get; private set; } = 0.15f;

    public CityMapView(CityMap map)
    {
        Map = map;
        style.flexGrow = 1;
        style.overflow = Overflow.Hidden;
        style.backgroundColor = CityUi.MapPaper;

        content = new VisualElement { name = "map-content" };
        content.style.position = Position.Absolute;
        content.style.left = 0;
        content.style.top = 0;
        content.style.width = map.Width;
        content.style.height = map.Height;
        content.style.transformOrigin = new TransformOrigin(0, 0, 0);
        content.usageHints = UsageHints.DynamicTransform;
        content.pickingMode = PickingMode.Ignore;
        Add(content);

        content.Add(new GroundLayer(map));
        nearLabels = Layer();
        content.Add(nearLabels);

        var plotLayer = Layer();
        content.Add(plotLayer);
        foreach (var def in map.Plots)
        {
            var view = new PlotView(def);
            views[def.Id] = view;
            plotLayer.Add(view);
        }

        farLabels = Layer();
        content.Add(farLabels);
        AddLabels();

        RegisterCallback<GeometryChangedEvent>(OnGeometry);
        RegisterCallback<WheelEvent>(OnWheel);
        RegisterCallback<PointerDownEvent>(OnDown);
        RegisterCallback<PointerMoveEvent>(OnMove);
        RegisterCallback<PointerUpEvent>(OnUp);
        RegisterCallback<PointerLeaveEvent>(_ =>
        {
            if (activePointer < 0) SetHover(null, default);
        });
        RegisterCallback<PointerCaptureOutEvent>(_ =>
        {
            activePointer = -1;
            dragging = false;
        });
    }

    VisualElement Layer()
    {
        var layer = new VisualElement();
        layer.pickingMode = PickingMode.Ignore;
        layer.style.position = Position.Absolute;
        layer.style.left = 0;
        layer.style.top = 0;
        layer.style.width = Map.Width;
        layer.style.height = Map.Height;
        return layer;
    }

    // ---- Data ---------------------------------------------------------------------------------------------------

    /// <summary>Show every plot the way the city copy says it is.</summary>
    public Colouring Mode
    {
        get => colouring;
        set
        {
            if (colouring == value) return;
            colouring = value;
            RefreshAll(lastCity);
        }
    }

    /// <summary>The cheapest and dearest plot on the map right now (the Prices legend shows them).</summary>
    public long Cheapest => cheapest;
    public long Dearest => dearest;

    public void RefreshAll(CityOnline city)
    {
        lastCity = city;
        MeasurePrices(city);
        foreach (var view in views.Values) RefreshView(view, city);
    }

    public void RefreshPlots(IEnumerable<string> plotIds, CityOnline city)
    {
        lastCity = city;
        long before = dearest;
        MeasurePrices(city);
        if (colouring == Colouring.Prices && dearest != before)
        {
            RefreshAll(city); // The scale moved: every colour changes
            return;
        }
        foreach (string id in plotIds)
        {
            if (id != null && views.TryGetValue(id, out var view)) RefreshView(view, city);
        }
    }

    /// <summary>What a plot costs you: the City's price when it's free, the stake when someone owns it.</summary>
    static long PriceOf(CityPlotDef def, CityPlot plot) => plot == null ? def.BasePrice : CityRules.ChallengeStake(def, plot);

    void MeasurePrices(CityOnline city)
    {
        cheapest = long.MaxValue;
        dearest = 0;
        foreach (var def in Map.Plots)
        {
            long price = PriceOf(def, city?.PlotState(def));
            cheapest = Math.Min(cheapest, price);
            dearest = Math.Max(dearest, price);
        }
        if (cheapest == long.MaxValue) cheapest = 0;
    }

    /// <summary>A price's colour on a log scale between the cheapest and dearest plot (stakes grow 1.5x a time).</summary>
    public Color PriceColour(long price)
    {
        if (dearest <= cheapest || price <= 0) return PriceRamp[0];
        double t = (Math.Log(price) - Math.Log(Math.Max(1, cheapest))) / (Math.Log(dearest) - Math.Log(Math.Max(1, cheapest)));
        t = Math.Max(0, Math.Min(1, t)) * (PriceRamp.Length - 1);
        int i = Math.Min(PriceRamp.Length - 2, (int)t);
        return Color.Lerp(PriceRamp[i], PriceRamp[i + 1], (float)(t - i));
    }

    void RefreshView(PlotView view, CityOnline city)
    {
        var plot = city != null ? city.PlotState(view.Def) : null;
        bool mine = city != null && city.IsMine(plot);
        bool shielded = plot != null && city != null && plot.ShieldUntil > city.Now;
        bool myHand = city?.Account?.Game != null && city.Account.Game.IsOpen && city.Account.Game.PlotId == view.Def.Id;
        Color? priceColour = colouring == Colouring.Prices ? PriceColour(PriceOf(view.Def, plot)) : (Color?)null;
        view.Show(plot, mine, shielded, myHand, priceColour);
    }

    // ---- Camera -------------------------------------------------------------------------------------------------

    void OnGeometry(GeometryChangedEvent e)
    {
        var size = layout.size;
        if (size.x < 10 || size.y < 10) return;
        MinZoom = Mathf.Min(size.x / Map.Width, size.y / Map.Height) * 0.9f;
        if (!fitted)
        {
            fitted = true;
            FitAll();
        }
        else
        {
            ApplyTransform();
        }
    }

    public void FitAll()
    {
        var size = layout.size;
        if (size.x < 10) return;
        zoom = Mathf.Min(size.x / Map.Width, size.y / Map.Height) * 0.98f;
        pan = (size - new Vector2(Map.Width, Map.Height) * zoom) * 0.5f;
        ApplyTransform();
    }

    /// <summary>Centre the view on a plot, zooming in to at least the given zoom.</summary>
    public void FocusOn(CityPlotDef def, float atLeastZoom = 0.9f)
    {
        if (def == null) return;
        var size = layout.size;
        zoom = Mathf.Clamp(Mathf.Max(zoom, atLeastZoom), MinZoom, MaxZoom);
        pan = size * 0.5f - new Vector2(def.CenterX, def.CenterY) * zoom;
        // Keep the plot clear of the side panel on the right
        pan.x -= Mathf.Min(260f, size.x * 0.15f);
        ApplyTransform();
    }

    public void ZoomBy(float factor) => ZoomAround(layout.size * 0.5f, factor);

    void ZoomAround(Vector2 local, float factor)
    {
        float newZoom = Mathf.Clamp(zoom * factor, MinZoom, MaxZoom);
        if (Mathf.Approximately(newZoom, zoom)) return;
        Vector2 mapPoint = (local - pan) / zoom;
        zoom = newZoom;
        pan = local - mapPoint * zoom;
        ApplyTransform();
    }

    public void PanBy(Vector2 delta)
    {
        pan += delta;
        ApplyTransform();
    }

    void ApplyTransform()
    {
        var size = layout.size;
        Vector2 mapSize = new Vector2(Map.Width, Map.Height) * zoom;
        // Keep the map on screen (centred on an axis where it's smaller than the view)
        float margin = 240f;
        pan.x = mapSize.x <= size.x ? (size.x - mapSize.x) * 0.5f : Mathf.Clamp(pan.x, size.x - mapSize.x - margin, margin);
        pan.y = mapSize.y <= size.y ? (size.y - mapSize.y) * 0.5f : Mathf.Clamp(pan.y, size.y - mapSize.y - margin, margin);

        content.style.translate = new Translate(pan.x, pan.y);
        content.style.scale = new Scale(new Vector3(zoom, zoom, 1f));

        int newLod = zoom < 0.36f ? 0 : zoom < 0.75f ? 1 : 2;
        if (newLod != lod)
        {
            lod = newLod;
            foreach (var view in views.Values) view.SetDetail(lod);
            farLabels.style.display = lod == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            nearLabels.style.opacity = lod == 0 ? 0.7f : 1f;
        }
    }

    public Vector2 MapToLocal(Vector2 mapPoint) => pan + mapPoint * zoom;

    // ---- Pointer ------------------------------------------------------------------------------------------------

    CityPlotDef PlotAtLocal(Vector2 local)
    {
        Vector2 m = (local - pan) / zoom;
        return Map.PlotAt(m.x, m.y);
    }

    void OnWheel(WheelEvent e)
    {
        var local = this.WorldToLocal(e.mousePosition);
        float factor = e.delta.y > 0 ? 1f / 1.15f : 1.15f;
        ZoomAround(local, factor);
        e.StopPropagation();
    }

    void OnDown(PointerDownEvent e)
    {
        if (activePointer >= 0) return;
        activePointer = e.pointerId;
        downLocal = lastLocal = this.WorldToLocal(e.position);
        dragging = false;
        this.CapturePointer(e.pointerId);
        e.StopPropagation();
    }

    void OnMove(PointerMoveEvent e)
    {
        var local = this.WorldToLocal(e.position);
        if (activePointer == e.pointerId)
        {
            if (!dragging && (local - downLocal).magnitude > DragThreshold)
            {
                dragging = true;
                SetHover(null, local);
            }
            if (dragging) PanBy(local - lastLocal);
            lastLocal = local;
            return;
        }
        SetHover(PlotAtLocal(local), local);
    }

    void OnUp(PointerUpEvent e)
    {
        if (activePointer != e.pointerId) return;
        var local = this.WorldToLocal(e.position);
        bool click = !dragging && e.button == 0;
        activePointer = -1;
        dragging = false;
        if (this.HasPointerCapture(e.pointerId)) this.ReleasePointer(e.pointerId);
        if (click)
        {
            var def = PlotAtLocal(local);
            if (def != null) PlotClicked?.Invoke(def);
        }
        SetHover(PlotAtLocal(local), local);
        e.StopPropagation();
    }

    void SetHover(CityPlotDef def, Vector2 local)
    {
        string id = def?.Id;
        if (id != hoveredId)
        {
            if (hoveredId != null && views.TryGetValue(hoveredId, out var old)) old.SetHover(false);
            hoveredId = id;
            if (id != null && views.TryGetValue(id, out var now)) now.SetHover(true);
        }
        PlotHovered?.Invoke(def, local);
    }

    public void Select(string plotId)
    {
        if (selectedId == plotId) return;
        if (selectedId != null && views.TryGetValue(selectedId, out var old)) old.SetSelected(false);
        selectedId = plotId;
        if (plotId != null && views.TryGetValue(plotId, out var now)) now.SetSelected(true);
    }

    // ---- Labels -------------------------------------------------------------------------------------------------

    void AddLabels()
    {
        // District names over their blocks, big and pale (only from afar, where plots are small)
        for (int i = 0; i < Map.Districts.Count; i++)
        {
            var d = Map.Districts[i];
            var label = CityUi.OneLine(CityUi.Text(CityUi.Upper(d.Name, Map.World.Locale == "tr"), 86, CityUi.Ink, true));
            label.style.letterSpacing = 8;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.backgroundColor = CityUi.Alpha(DistrictTints[i % DistrictTints.Length], 0.88f);
            CityUi.Border(label, 4, CityUi.Ink);
            CityUi.Radius(label, 20);
            CityUi.Padding(label, 4, 26);
            Centred(farLabels, label, d.LabelX, d.LabelY, 1400, 150);
        }

        // Water names and park names, written on the map itself
        foreach (var water in Map.World.WaterLabels)
        {
            var text = CityUi.OneLine(CityUi.Text(water.Text, 64, CityUi.Alpha(CityUi.Blue, 0.6f), true));
            text.style.letterSpacing = 18;
            text.style.unityTextAlign = TextAnchor.MiddleCenter;
            float x = CityLayout.Margin + water.X * CityLayout.SlotW;
            float y = CityLayout.Margin + water.Y * CityLayout.SlotH;
            var box = Centred(nearLabels, text, x, y, 1600, 90);
            if (Mathf.Abs(water.Rotate) > 0.01f) box.style.rotate = new Rotate(water.Rotate);
        }
        foreach (var park in Map.Parks)
        {
            var text = CityUi.OneLine(CityUi.Text(park.Name, 34, CityUi.Hex("1D5A3E"), true));
            text.style.unityTextAlign = TextAnchor.MiddleCenter;
            text.style.backgroundColor = CityUi.Alpha(CityUi.Card, 0.85f);
            CityUi.Radius(text, 12);
            CityUi.Padding(text, 2, 14);
            Centred(nearLabels, text, park.LabelX, park.LabelY, 900, 60);
        }
    }

    /// <summary>A box of the given size centred on a map point, with the element centred inside it.</summary>
    static VisualElement Centred(VisualElement layer, VisualElement element, float x, float y, float width, float height)
    {
        var box = new VisualElement();
        box.pickingMode = PickingMode.Ignore;
        CityUi.Absolute(box, x - width * 0.5f, y - height * 0.5f, width, height);
        box.style.alignItems = Align.Center;
        box.style.justifyContent = Justify.Center;
        element.pickingMode = PickingMode.Ignore;
        box.Add(element);
        layer.Add(box);
        return box;
    }

    // ---- Ground painting ----------------------------------------------------------------------------------------

    /// <summary>Paints the whole city ground once: land, water and coastline, bridges, islands, blocks, parks, borders.</summary>
    class GroundLayer : VisualElement
    {
        readonly CityMap map;

        public GroundLayer(CityMap map)
        {
            this.map = map;
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.left = 0;
            style.top = 0;
            style.width = map.Width;
            style.height = map.Height;
            generateVisualContent += Draw;
        }

        static readonly Color Street = CityUi.Card;
        static readonly Color BlockGround = CityUi.Hex("EDE1C4");
        static readonly Color Sand = CityUi.Hex("F3E3B5");

        void Draw(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            p.lineJoin = LineJoin.Round;
            p.lineCap = LineCap.Round;

            // Land (streets) and water, slot by slot: same colours touching look like one area
            for (int row = 0; row < map.Rows; row++)
            {
                for (int column = 0; column < map.Columns; column++)
                {
                    var rect = ToRect(map.SlotRect(column, row));
                    if (map.IsWet(column, row)) FillRect(p, rect, CityUi.PastelBlue);
                    else if (map.IsLand(column, row)) FillRect(p, rect, Street);
                }
            }

            Ripples(p);
            Edges(p);

            foreach (var bridge in map.Bridges) DrawBridge(p, bridge);
            foreach (var island in map.Islands) DrawIsland(p, island);

            foreach (var block in map.Blocks)
            {
                if (block.IsIsland) continue;
                var district = map.DistrictsById[block.DistrictId];
                var tint = Color.Lerp(BlockGround, DistrictTints[district.Index % DistrictTints.Length], 0.55f);
                Rounded(p, new Rect(block.X - 4f, block.Y - 4f, block.W + 8f, block.H + 8f), 14f, tint, CityUi.Ink, 3f);
            }
            foreach (var park in map.Parks) DrawPark(p, park);

            DistrictBorders(p);

            // Frame around the whole map
            Rounded(p, new Rect(30f, 30f, map.Width - 60f, map.Height - 60f), 40f, null, CityUi.Ink, 6f);
            Rounded(p, new Rect(46f, 46f, map.Width - 92f, map.Height - 92f), 32f, null, CityUi.Alpha(CityUi.Ink, 0.35f), 3f);
        }

        void Ripples(Painter2D p)
        {
            var rng = new CityRandom("ripples:" + map.CityId);
            p.strokeColor = CityUi.Alpha(CityUi.Blue, 0.45f);
            p.lineWidth = 4f;
            for (int row = 0; row < map.Rows; row++)
            {
                for (int column = 0; column < map.Columns; column++)
                {
                    if (map.KindAt(column, row) != CitySlotKind.Water) continue;
                    var slot = map.SlotRect(column, row);
                    for (int i = 0; i < 2; i++)
                    {
                        float x = slot.X + 60f + (float)rng.NextDouble() * (slot.W - 180f);
                        float y = slot.Y + 60f + (float)rng.NextDouble() * (slot.H - 120f);
                        p.BeginPath();
                        p.MoveTo(new Vector2(x, y));
                        p.BezierCurveTo(new Vector2(x + 12, y - 12), new Vector2(x + 24, y - 12), new Vector2(x + 36, y));
                        p.BezierCurveTo(new Vector2(x + 48, y + 12), new Vector2(x + 60, y + 12), new Vector2(x + 72, y));
                        p.Stroke();
                    }
                }
            }
        }

        /// <summary>Ink along every coast (water meets land) and around the city's edge (land meets open ground).</summary>
        void Edges(Painter2D p)
        {
            for (int row = 0; row < map.Rows; row++)
            {
                for (int column = 0; column < map.Columns; column++)
                {
                    bool wet = map.IsWet(column, row);
                    bool land = map.IsLand(column, row);
                    if (!wet && !land) continue;
                    var s = map.SlotRect(column, row);
                    foreach (var (dc, dr) in new[] { (0, -1), (1, 0), (0, 1), (-1, 0) })
                    {
                        int nc = column + dc, nr = row + dr;
                        if (!map.InGrid(nc, nr)) continue; // The map's edge: water and land run on to the frame
                        bool coast = wet && !map.IsWet(nc, nr);
                        bool rim = land && !map.IsWet(nc, nr) && !map.IsLand(nc, nr);
                        if (!coast && !rim) continue;

                        Vector2 a, b;
                        if (dr == -1) { a = new Vector2(s.X, s.Y); b = new Vector2(s.X + s.W, s.Y); }
                        else if (dr == 1) { a = new Vector2(s.X, s.Y + s.H); b = new Vector2(s.X + s.W, s.Y + s.H); }
                        else if (dc == -1) { a = new Vector2(s.X, s.Y); b = new Vector2(s.X, s.Y + s.H); }
                        else { a = new Vector2(s.X + s.W, s.Y); b = new Vector2(s.X + s.W, s.Y + s.H); }
                        p.strokeColor = coast ? CityUi.Ink : CityUi.Alpha(CityUi.Ink, 0.55f);
                        p.lineWidth = coast ? 6f : 4f;
                        p.BeginPath();
                        p.MoveTo(a);
                        p.LineTo(b);
                        p.Stroke();
                    }
                }
            }
        }

        void DrawBridge(Painter2D p, CityBridgeDef bridge)
        {
            var deck = ToRect(bridge.Deck);
            // Reach a little onto the land on both ends
            if (bridge.Horizontal) { deck.xMin -= 20f; deck.xMax += 20f; }
            else { deck.yMin -= 20f; deck.yMax += 20f; }
            Rounded(p, deck, 12f, Street, CityUi.Ink, 4f);
            p.strokeColor = CityUi.Alpha(CityUi.Ink, 0.6f);
            p.lineWidth = 3f;
            p.BeginPath();
            if (bridge.Horizontal)
            {
                p.MoveTo(new Vector2(deck.xMin + 24f, deck.yMin + 16f));
                p.LineTo(new Vector2(deck.xMax - 24f, deck.yMin + 16f));
                p.MoveTo(new Vector2(deck.xMin + 24f, deck.yMax - 16f));
                p.LineTo(new Vector2(deck.xMax - 24f, deck.yMax - 16f));
            }
            else
            {
                p.MoveTo(new Vector2(deck.xMin + 16f, deck.yMin + 24f));
                p.LineTo(new Vector2(deck.xMin + 16f, deck.yMax - 24f));
                p.MoveTo(new Vector2(deck.xMax - 16f, deck.yMin + 24f));
                p.LineTo(new Vector2(deck.xMax - 16f, deck.yMax - 24f));
            }
            p.Stroke();
        }

        void DrawIsland(Painter2D p, CityIslandDef island)
        {
            p.BeginPath();
            p.Arc(new Vector2(island.CenterX, island.CenterY), island.Radius, Angle.Degrees(0), Angle.Degrees(360));
            p.ClosePath();
            p.fillColor = Sand;
            p.Fill();
            p.strokeColor = CityUi.Ink;
            p.lineWidth = 5f;
            p.Stroke();
        }

        void DrawPark(Painter2D p, CityParkDef park)
        {
            var rng = new CityRandom("park:" + map.CityId + ":" + park.Name);
            foreach (var area in park.Areas)
            {
                var rect = ToRect(area);
                Rounded(p, rect, 22f, CityUi.PastelGreen, CityUi.Ink, 3f);
                // A path through the park
                p.BeginPath();
                p.MoveTo(new Vector2(rect.xMin + 20f, rect.center.y + rect.height * 0.12f));
                p.BezierCurveTo(new Vector2(rect.xMin + rect.width * 0.35f, rect.center.y - rect.height * 0.3f),
                    new Vector2(rect.xMax - rect.width * 0.35f, rect.center.y + rect.height * 0.35f),
                    new Vector2(rect.xMax - 20f, rect.center.y - rect.height * 0.1f));
                p.strokeColor = CityUi.Ink;
                p.lineWidth = 20f;
                p.Stroke();
                p.strokeColor = Street;
                p.lineWidth = 13f;
                p.Stroke();
                // Trees
                int trees = Mathf.Clamp(Mathf.RoundToInt(rect.width * rect.height / 9000f), 10, 60);
                for (int i = 0; i < trees; i++)
                {
                    float x = rect.xMin + 36f + (float)rng.NextDouble() * (rect.width - 72f);
                    float y = rect.yMin + 36f + (float)rng.NextDouble() * (rect.height - 72f);
                    float r = 16f + (float)rng.NextDouble() * 12f;
                    p.BeginPath();
                    p.Arc(new Vector2(x, y), r, Angle.Degrees(0), Angle.Degrees(360));
                    p.ClosePath();
                    p.fillColor = CityUi.Hex("9CCB78");
                    p.Fill();
                    p.strokeColor = CityUi.Ink;
                    p.lineWidth = 2.5f;
                    p.Stroke();
                }
            }
        }

        /// <summary>A dashed line down the street between two districts.</summary>
        void DistrictBorders(Painter2D p)
        {
            p.strokeColor = CityUi.Alpha(CityUi.Ink, 0.45f);
            p.lineWidth = 5f;
            for (int row = 0; row < map.Rows; row++)
            {
                for (int column = 0; column < map.Columns; column++)
                {
                    int here = map.DistrictAt[row, column];
                    if (here < 0) continue;
                    var s = map.SlotRect(column, row);
                    // Right and bottom neighbours only (each border once)
                    if (map.InGrid(column + 1, row))
                    {
                        int right = map.DistrictAt[row, column + 1];
                        if (right >= 0 && right != here)
                            Dashes(p, new Vector2(s.X + s.W, s.Y + 10f), new Vector2(s.X + s.W, s.Y + s.H - 10f));
                    }
                    if (map.InGrid(column, row + 1))
                    {
                        int below = map.DistrictAt[row + 1, column];
                        if (below >= 0 && below != here)
                            Dashes(p, new Vector2(s.X + 10f, s.Y + s.H), new Vector2(s.X + s.W - 10f, s.Y + s.H));
                    }
                }
            }
        }

        static void Dashes(Painter2D p, Vector2 from, Vector2 to)
        {
            float length = Vector2.Distance(from, to);
            var dir = (to - from) / length;
            const float dash = 26f, gap = 18f;
            p.BeginPath();
            for (float t = 0; t < length; t += dash + gap)
            {
                p.MoveTo(from + dir * t);
                p.LineTo(from + dir * Mathf.Min(length, t + dash));
            }
            p.Stroke();
        }

        static Rect ToRect(CityRectF r) => new Rect(r.X, r.Y, r.W, r.H);

        static void FillRect(Painter2D p, Rect rect, Color colour)
        {
            // A hair bigger so neighbouring slots meet without a seam
            p.BeginPath();
            p.MoveTo(new Vector2(rect.xMin - 0.5f, rect.yMin - 0.5f));
            p.LineTo(new Vector2(rect.xMax + 0.5f, rect.yMin - 0.5f));
            p.LineTo(new Vector2(rect.xMax + 0.5f, rect.yMax + 0.5f));
            p.LineTo(new Vector2(rect.xMin - 0.5f, rect.yMax + 0.5f));
            p.ClosePath();
            p.fillColor = colour;
            p.Fill();
        }

        static void Rounded(Painter2D p, Rect rect, float radius, Color? fill, Color stroke, float width)
        {
            p.BeginPath();
            CityUi.RoundedRect(p, rect, radius);
            if (fill.HasValue)
            {
                p.fillColor = fill.Value;
                p.Fill();
            }
            p.strokeColor = stroke;
            p.lineWidth = width;
            p.Stroke();
        }
    }

    // ---- One plot -----------------------------------------------------------------------------------------------

    class PlotView : VisualElement
    {
        public readonly CityPlotDef Def;
        readonly float side;
        readonly bool big;
        readonly VisualElement body;
        readonly VisualElement dollar;
        readonly Label price;
        readonly CityLogoView logo;
        readonly Label owner;
        readonly Label landmark;
        // Made the first time they're needed: most plots never show them (keeps the map light)
        VisualElement youTag, lockTag, handTag, selectRing;

        bool owned, mine, hovered, selected;
        Color fill;
        int detail = 1;

        public PlotView(CityPlotDef def)
        {
            Def = def;
            side = Mathf.Min(def.W, def.H);
            big = side >= 150f;
            pickingMode = PickingMode.Ignore;
            CityUi.Absolute(this, def.X, def.Y, def.W, def.H);

            body = new VisualElement();
            body.pickingMode = PickingMode.Ignore;
            CityUi.Fill(body);
            CityUi.Radius(body, 10, 13, 9, 14);
            body.style.alignItems = Align.Center;
            body.style.justifyContent = Justify.Center;
            Add(body);

            dollar = new VisualElement();
            dollar.pickingMode = PickingMode.Ignore;
            float d = side * 0.42f;
            dollar.style.width = d;
            dollar.style.height = d;
            dollar.style.alignItems = Align.Center;
            dollar.style.justifyContent = Justify.Center;
            dollar.style.backgroundColor = CityUi.Card;
            CityUi.Border(dollar, 3, def.Size == CityPlotSize.Landmark ? CityUi.GoldDark : CityUi.Green);
            CityUi.Radius(dollar, d * 0.5f);
            if (def.Size == CityPlotSize.Landmark)
            {
                dollar.Add(new CityUi.IconView("star", d * 0.6f, CityUi.GoldDark));
            }
            else
            {
                var dollarText = CityUi.Text("$", d * 0.62f, CityUi.Green, true);
                dollarText.style.unityTextAlign = TextAnchor.MiddleCenter;
                dollar.Add(dollarText);
            }
            body.Add(dollar);

            price = CityUi.OneLine(CityUi.Text(CityRules.Money(def.BasePrice), big ? 22 : 16, CityUi.Green, true));
            price.style.marginTop = 4;
            body.Add(price);

            logo = new CityLogoView(null, side * (big ? 0.5f : 0.54f), def.CityId);
            body.Add(logo);

            owner = CityUi.OneLine(CityUi.Text("", big ? 22 : 15, CityUi.Ink, true));
            owner.style.maxWidth = def.W - 12;
            owner.style.marginTop = 3;
            owner.style.unityTextAlign = TextAnchor.MiddleCenter;
            body.Add(owner);

            if (def.Name != null)
            {
                landmark = CityUi.OneLine(CityUi.Text(CityUi.Upper(def.Name), 18, CityUi.GoldDark, true));
                landmark.style.position = Position.Absolute;
                landmark.style.top = 8;
                landmark.style.left = 8;
                landmark.style.right = 8;
                landmark.style.unityTextAlign = TextAnchor.MiddleCenter;
                landmark.style.letterSpacing = 1;
                Add(landmark);
            }

            Show(null, false, false, false, null);
        }

        static VisualElement Tag(Color colour, string text, float size, float angle)
        {
            var tag = new VisualElement();
            tag.pickingMode = PickingMode.Ignore;
            tag.style.position = Position.Absolute;
            tag.style.backgroundColor = colour;
            CityUi.Border(tag, 2, CityUi.Ink);
            CityUi.Radius(tag, 6);
            CityUi.Padding(tag, 1, 6);
            tag.style.rotate = new Rotate(angle);
            tag.Add(CityUi.Text(text, size, CityUi.Ink, true));
            return tag;
        }

        static VisualElement Badge(Color colour, string icon)
        {
            var badge = new VisualElement();
            badge.pickingMode = PickingMode.Ignore;
            badge.style.position = Position.Absolute;
            badge.style.width = 34;
            badge.style.height = 34;
            badge.style.alignItems = Align.Center;
            badge.style.justifyContent = Justify.Center;
            badge.style.backgroundColor = colour;
            CityUi.Border(badge, 2, CityUi.Ink);
            CityUi.Radius(badge, 17);
            badge.Add(new CityUi.IconView(icon, 20, CityUi.Card, 3f));
            return badge;
        }

        /// <summary>Show or hide an optional part, making it the first time it's shown.</summary>
        void Toggle(ref VisualElement part, bool on, Func<VisualElement> make)
        {
            if (part == null)
            {
                if (!on) return;
                part = make();
                Add(part);
            }
            part.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void Show(CityPlot plot, bool isMine, bool shielded, bool myHand, Color? priceColour)
        {
            owned = plot != null;
            mine = isMine;

            logo.style.display = owned ? DisplayStyle.Flex : DisplayStyle.None;
            if (owned)
            {
                logo.Set(plot.Logo, Def.CityId);
                owner.text = plot.OwnerName;
                fill = Color.Lerp(CityUi.Card, CityLogoView.BrandColour(plot.Logo), 0.3f);
            }
            else
            {
                owner.text = "";
                fill = CityUi.Alpha(CityUi.Card, 0.8f);
            }

            if (priceColour.HasValue) fill = priceColour.Value;

            Toggle(ref youTag, mine, () =>
            {
                var tag = Tag(CityUi.Highlight, "YOU", big ? 16 : 12, -6);
                if (landmark != null) tag.style.bottom = -12;
                else tag.style.top = -12;
                tag.style.left = -8;
                return tag;
            });
            // Protected (recently changed hands): nobody can play for it yet
            Toggle(ref lockTag, shielded, () =>
            {
                var badge = Badge(CityUi.Blue, "lock");
                badge.style.bottom = -10;
                badge.style.right = -10;
                return badge;
            });
            // My hand is on the table for this plot
            Toggle(ref handTag, myHand, () =>
            {
                var badge = Badge(CityUi.Felt, "cards");
                badge.style.top = -12;
                badge.style.right = -10;
                return badge;
            });

            ApplyLook();
            SetDetail(detail);
        }

        public void SetDetail(int level)
        {
            detail = level;
            owner.style.display = owned && level >= 1 ? DisplayStyle.Flex : DisplayStyle.None;
            price.style.display = !owned && level >= 1 ? DisplayStyle.Flex : DisplayStyle.None;
            // Zoomed all the way out, free plots are just green outlines (less clutter)
            dollar.style.display = !owned && level >= 1 ? DisplayStyle.Flex : DisplayStyle.None;
            if (landmark != null) landmark.style.display = level >= 1 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void SetHover(bool on)
        {
            hovered = on;
            ApplyLook();
            if (on) BringToFront();
        }

        public void SetSelected(bool on)
        {
            selected = on;
            Toggle(ref selectRing, on, () =>
            {
                var ring = new VisualElement();
                ring.pickingMode = PickingMode.Ignore;
                CityUi.Absolute(ring, -14, -14);
                ring.style.right = -14;
                ring.style.bottom = -14;
                CityUi.Border(ring, 8, CityUi.Highlight);
                CityUi.Radius(ring, 18);
                return ring;
            });
            selectRing?.SendToBack();
            ApplyLook();
            if (on) BringToFront();
        }

        void ApplyLook()
        {
            body.style.backgroundColor = fill;
            Color border = owned ? CityUi.Ink : CityUi.Green;
            float width = 3f;
            if (mine) { border = CityUi.Hex("D9A900"); width = 6f; }
            if (Def.Size == CityPlotSize.Landmark && !mine) { border = CityUi.Gold; width = 6f; }
            if (hovered) border = CityUi.Blue;
            CityUi.Border(body, width, border);
            body.style.translate = hovered || selected ? new Translate(-3, -4) : new Translate(0, 0);
        }
    }
}
