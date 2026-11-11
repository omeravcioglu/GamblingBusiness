using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The logo drawing editor: a 32x32 canvas (pencil, eraser, fill, colour picker, line, box, circle, mirror, grid,
/// undo/redo, clear), the palette, starter shapes and a letter stamp. Changed fires after every edit; Logo holds the drawing.
/// </summary>
public class CityPixelEditor : VisualElement
{
    public enum Tool { Pencil, Eraser, Fill, Picker, Line, Box, Circle }

    const int N = CityPixelLogo.Size;
    const float CellPx = 17f;
    const int MaxUndo = 60;

    public event Action Changed;
    public CityPixelLogo Logo { get; private set; }
    public Texture2D Preview { get; }

    Tool tool = Tool.Pencil;
    byte colour = 1;
    bool mirror = true;
    bool grid = true;
    readonly Stack<byte[]> undo = new Stack<byte[]>();
    readonly Stack<byte[]> redo = new Stack<byte[]>();

    readonly Canvas canvas;
    readonly Dictionary<Tool, CityUi.InkButton> toolButtons = new Dictionary<Tool, CityUi.InkButton>();
    readonly List<(VisualElement tile, byte index)> swatches = new List<(VisualElement, byte)>();
    CityUi.InkButton mirrorButton, gridButton, undoButton, redoButton;
    TextField lettersField;

    public CityPixelEditor(CityPixelLogo start)
    {
        Logo = start != null ? start.Clone() : new CityPixelLogo();
        Preview = CityLogoCache.MakeTexture(Logo);
        style.flexDirection = FlexDirection.Row;
        style.alignItems = Align.FlexStart;

        // Tools, two to a row
        var tools = new VisualElement();
        tools.style.flexDirection = FlexDirection.Row;
        tools.style.flexWrap = Wrap.Wrap;
        tools.style.width = 168;
        tools.style.marginRight = 14;
        tools.Add(ToolButton(Tool.Pencil, "pencil"));
        tools.Add(ToolButton(Tool.Eraser, "eraser"));
        tools.Add(ToolButton(Tool.Line, "line"));
        tools.Add(ToolButton(Tool.Box, "box"));
        tools.Add(ToolButton(Tool.Circle, "ring"));
        tools.Add(ToolButton(Tool.Fill, "bucket"));
        tools.Add(ToolButton(Tool.Picker, "picker"));
        mirrorButton = SquareButton("mirror", () => { mirror = !mirror; RefreshTools(); canvas.MarkDirtyRepaint(); });
        tools.Add(mirrorButton);
        gridButton = SquareButton("grid", () => { grid = !grid; RefreshTools(); canvas.MarkDirtyRepaint(); });
        tools.Add(gridButton);
        undoButton = SquareButton("undo", Undo);
        redoButton = SquareButton("redo", Redo);
        tools.Add(undoButton);
        tools.Add(redoButton);
        tools.Add(SquareButton("trash", () => Edit(() => Array.Clear(Logo.Pixels, 0, Logo.Pixels.Length))));
        Add(tools);

        // Canvas, in an ink frame
        canvas = new Canvas(this);
        var frame = new VisualElement();
        CityUi.Border(frame, 4, CityUi.Ink);
        CityUi.Radius(frame, 8);
        frame.style.overflow = Overflow.Hidden;
        frame.Add(canvas);
        Add(CityUi.Inked(frame));

        // Palette, shapes, letters
        var side = CityUi.Column(12);
        side.style.marginLeft = 24;
        side.style.width = 360;
        side.Add(CityUi.Text("Colours", 20, CityUi.Ink, true));
        var palette = new VisualElement();
        palette.style.flexDirection = FlexDirection.Row;
        palette.style.flexWrap = Wrap.Wrap;
        for (int i = 1; i < CityPixelLogo.Palette.Length; i++)
        {
            byte index = (byte)i;
            var tile = new VisualElement();
            tile.style.width = 38;
            tile.style.height = 38;
            tile.style.marginRight = 5;
            tile.style.marginBottom = 5;
            tile.style.backgroundColor = CityUi.Hex(CityPixelLogo.Palette[i]);
            CityUi.Radius(tile, 8);
            tile.RegisterCallback<ClickEvent>(_ =>
            {
                colour = index;
                if (tool == Tool.Eraser || tool == Tool.Picker) tool = Tool.Pencil;
                RefreshTools();
            });
            swatches.Add((tile, index));
            palette.Add(tile);
        }
        side.Add(palette);

        side.Add(CityUi.Text("Start from a shape", 20, CityUi.Ink, true));
        var shapes = new VisualElement();
        shapes.style.flexDirection = FlexDirection.Row;
        shapes.style.flexWrap = Wrap.Wrap;
        foreach (var shape in new[] { "circle", "square", "shield", "diamond", "hexagon", "heart", "star" })
        {
            string captured = shape;
            var button = new CityUi.InkButton(Cap(shape), () => Edit(() => StampShape(captured)), CityUi.ButtonKind.Paper, null, 40, 17);
            button.style.marginRight = 8;
            button.style.marginBottom = 8;
            shapes.Add(button);
        }
        side.Add(shapes);

        side.Add(CityUi.Text("Letters", 20, CityUi.Ink, true));
        var letters = CityUi.Row(10);
        lettersField = CityUi.Field("", 3, 24);
        lettersField.style.width = 110;
        letters.Add(lettersField);
        letters.Add(new CityUi.InkButton("Stamp", () => Edit(() => StampLetters(lettersField.value)), CityUi.ButtonKind.Paper, "letters", 44, 18));
        side.Add(letters);
        side.Add(CityUi.Text("Letters go in the middle in the colour you picked. Mirror paints both halves at once.", 16, CityUi.Muted));
        Add(side);

        RefreshTools();
    }

    CityUi.InkButton ToolButton(Tool which, string icon)
    {
        var button = SquareButton(icon, () => { tool = which; RefreshTools(); });
        toolButtons[which] = button;
        return button;
    }

    static CityUi.InkButton SquareButton(string icon, Action onClick)
    {
        var button = new CityUi.InkButton(null, onClick, CityUi.ButtonKind.Paper, icon, 56, 26);
        button.style.width = 72;
        button.style.marginRight = 10;
        button.style.marginBottom = 10;
        return button;
    }

    void RefreshTools()
    {
        foreach (var pair in toolButtons)
            pair.Value.Face.style.backgroundColor = pair.Key == tool ? CityUi.Highlight : CityUi.Card;
        mirrorButton.Face.style.backgroundColor = mirror ? CityUi.Highlight : CityUi.Card;
        gridButton.Face.style.backgroundColor = grid ? CityUi.Highlight : CityUi.Card;
        undoButton.SetEnabledLook(undo.Count > 0);
        redoButton.SetEnabledLook(redo.Count > 0);
        foreach (var (tile, index) in swatches)
        {
            bool on = index == colour && tool != Tool.Eraser;
            CityUi.Border(tile, on ? 5 : 2, on ? CityUi.Ink : CityUi.Alpha(CityUi.Ink, 0.4f));
            tile.style.translate = on ? new Translate(0, -3) : new Translate(0, 0);
        }
    }

    static string Cap(string s) => char.ToUpperInvariant(s[0]) + s.Substring(1);

    // ---- Editing --------------------------------------------------------------------------------------------------

    /// <summary>Replace the whole drawing (starting over from a saved logo).</summary>
    public void Load(CityPixelLogo logo)
    {
        Edit(() => Array.Copy(logo.Pixels, Logo.Pixels, Logo.Pixels.Length));
    }

    void Edit(Action change)
    {
        PushUndo();
        change();
        AfterEdit();
    }

    void PushUndo()
    {
        undo.Push((byte[])Logo.Pixels.Clone());
        if (undo.Count > MaxUndo)
        {
            var keep = undo.ToArray();
            undo.Clear();
            for (int i = MaxUndo - 1; i >= 0; i--) undo.Push(keep[i]);
        }
        redo.Clear();
    }

    void AfterEdit()
    {
        CityLogoCache.Paint(Preview, Logo);
        canvas.MarkDirtyRepaint();
        RefreshTools();
        Changed?.Invoke();
    }

    void Undo()
    {
        if (undo.Count == 0) return;
        redo.Push((byte[])Logo.Pixels.Clone());
        Array.Copy(undo.Pop(), Logo.Pixels, Logo.Pixels.Length);
        AfterEdit();
    }

    void Redo()
    {
        if (redo.Count == 0) return;
        undo.Push((byte[])Logo.Pixels.Clone());
        Array.Copy(redo.Pop(), Logo.Pixels, Logo.Pixels.Length);
        AfterEdit();
    }

    void Paint(int x, int y, byte value)
    {
        if (x < 0 || y < 0 || x >= N || y >= N) return;
        Logo[x, y] = value;
        if (mirror) Logo[N - 1 - x, y] = value;
    }

    void Flood(int x, int y, byte value)
    {
        if (x < 0 || y < 0 || x >= N || y >= N) return;
        byte target = Logo[x, y];
        if (target == value) return;
        var stack = new Stack<(int x, int y)>();
        stack.Push((x, y));
        while (stack.Count > 0)
        {
            var (cx, cy) = stack.Pop();
            if (cx < 0 || cy < 0 || cx >= N || cy >= N || Logo[cx, cy] != target) continue;
            Logo[cx, cy] = value;
            stack.Push((cx + 1, cy));
            stack.Push((cx - 1, cy));
            stack.Push((cx, cy + 1));
            stack.Push((cx, cy - 1));
        }
    }

    // ---- Pointer on the canvas ------------------------------------------------------------------------------------

    bool stroking;
    Vector2Int last, shapeStart;
    byte[] beforeShape;

    void Down(Vector2Int cell)
    {
        switch (tool)
        {
            case Tool.Picker:
                byte picked = Logo[cell.x, cell.y];
                if (picked != 0) colour = picked;
                tool = Tool.Pencil;
                RefreshTools();
                return;
            case Tool.Fill:
                Edit(() =>
                {
                    Flood(cell.x, cell.y, colour);
                    if (mirror) Flood(N - 1 - cell.x, cell.y, colour);
                });
                return;
            case Tool.Line:
            case Tool.Box:
            case Tool.Circle:
                // Drag out the shape: it is redrawn over the picture from before it on every move
                PushUndo();
                stroking = true;
                shapeStart = last = cell;
                beforeShape = (byte[])Logo.Pixels.Clone();
                DrawShape(shapeStart, cell);
                AfterEdit();
                return;
            default:
                PushUndo();
                stroking = true;
                last = cell;
                Paint(cell.x, cell.y, tool == Tool.Eraser ? (byte)0 : colour);
                AfterEdit();
                return;
        }
    }

    void DrawShape(Vector2Int a, Vector2Int b)
    {
        switch (tool)
        {
            case Tool.Line:
                Line(a.x, a.y, b.x, b.y, colour);
                break;
            case Tool.Box:
                Line(a.x, a.y, b.x, a.y, colour);
                Line(b.x, a.y, b.x, b.y, colour);
                Line(b.x, b.y, a.x, b.y, colour);
                Line(a.x, b.y, a.x, a.y, colour);
                break;
            case Tool.Circle:
            {
                // An ellipse inside the dragged box: every column and every row gets its edge points (no gaps)
                float cx = (a.x + b.x) * 0.5f, cy = (a.y + b.y) * 0.5f;
                float rx = Mathf.Abs(b.x - a.x) * 0.5f, ry = Mathf.Abs(b.y - a.y) * 0.5f;
                if (rx < 0.5f || ry < 0.5f)
                {
                    Line(a.x, a.y, b.x, b.y, colour);
                    break;
                }
                for (int x = Mathf.Min(a.x, b.x); x <= Mathf.Max(a.x, b.x); x++)
                {
                    float dx = (x - cx) / rx;
                    float dy = Mathf.Sqrt(Mathf.Max(0f, 1f - dx * dx)) * ry;
                    Paint(x, Mathf.RoundToInt(cy - dy), colour);
                    Paint(x, Mathf.RoundToInt(cy + dy), colour);
                }
                for (int y = Mathf.Min(a.y, b.y); y <= Mathf.Max(a.y, b.y); y++)
                {
                    float dy = (y - cy) / ry;
                    float dx = Mathf.Sqrt(Mathf.Max(0f, 1f - dy * dy)) * rx;
                    Paint(Mathf.RoundToInt(cx - dx), y, colour);
                    Paint(Mathf.RoundToInt(cx + dx), y, colour);
                }
                break;
            }
        }
    }

    /// <summary>Every cell on the way from one cell to another.</summary>
    void Line(int x0, int y0, int x1, int y1, byte value)
    {
        int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        while (true)
        {
            Paint(x0, y0, value);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    void Drag(Vector2Int cell)
    {
        if (!stroking || cell == last) return;
        if (tool == Tool.Line || tool == Tool.Box || tool == Tool.Circle)
        {
            Array.Copy(beforeShape, Logo.Pixels, Logo.Pixels.Length);
            DrawShape(shapeStart, cell);
        }
        else
        {
            // Every cell on the way (fast strokes leave no gaps)
            Line(last.x, last.y, cell.x, cell.y, tool == Tool.Eraser ? (byte)0 : colour);
        }
        last = cell;
        CityLogoCache.Paint(Preview, Logo);
        canvas.MarkDirtyRepaint();
        Changed?.Invoke();
    }

    void Up()
    {
        stroking = false;
        beforeShape = null;
    }

    /// <summary>The 32x32 grid itself: a checkerboard where it's see-through, the pixels, a fine grid, the mirror line.</summary>
    class Canvas : VisualElement
    {
        readonly CityPixelEditor editor;
        int pointer = -1;

        public Canvas(CityPixelEditor editor)
        {
            this.editor = editor;
            style.width = N * CellPx;
            style.height = N * CellPx;
            style.backgroundColor = CityUi.Card;
            generateVisualContent += Draw;

            RegisterCallback<PointerDownEvent>(e =>
            {
                if (pointer >= 0 || e.button != 0) return;
                pointer = e.pointerId;
                this.CapturePointer(e.pointerId);
                editor.Down(Cell(e.localPosition));
                e.StopPropagation();
            });
            RegisterCallback<PointerMoveEvent>(e =>
            {
                if (e.pointerId == pointer) editor.Drag(Cell(e.localPosition));
            });
            RegisterCallback<PointerUpEvent>(e =>
            {
                if (e.pointerId != pointer) return;
                pointer = -1;
                if (this.HasPointerCapture(e.pointerId)) this.ReleasePointer(e.pointerId);
                editor.Up();
            });
            RegisterCallback<PointerCaptureOutEvent>(_ =>
            {
                pointer = -1;
                editor.Up();
            });
        }

        static Vector2Int Cell(Vector3 local)
        {
            int x = Mathf.Clamp(Mathf.FloorToInt(local.x / CellPx), 0, N - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(local.y / CellPx), 0, N - 1);
            return new Vector2Int(x, y);
        }

        void Draw(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            var colours = CityLogoCache.PaletteColours;
            var logo = editor.Logo;
            var light = CityUi.Hex("F1ECE0");
            var dark = CityUi.Hex("E2DCCD");

            for (int y = 0; y < N; y++)
            {
                // Runs of the same colour in a row are one rectangle
                int x = 0;
                while (x < N)
                {
                    byte value = logo[x, y];
                    int end = x + 1;
                    while (end < N && logo[end, y] == value) end++;
                    if (value == 0)
                    {
                        for (int cx = x; cx < end; cx++) Cell(p, cx, y, 1, ((cx + y) & 1) == 0 ? light : dark);
                    }
                    else
                    {
                        Cell(p, x, y, end - x, colours[value]);
                    }
                    x = end;
                }
            }

            // Fine grid, a stronger line every 8 cells
            for (int i = 1; i < N && editor.grid; i++)
            {
                p.strokeColor = CityUi.Alpha(CityUi.Ink, i % 8 == 0 ? 0.28f : 0.1f);
                p.lineWidth = i % 8 == 0 ? 1.5f : 1f;
                p.BeginPath();
                p.MoveTo(new Vector2(i * CellPx, 0));
                p.LineTo(new Vector2(i * CellPx, N * CellPx));
                p.MoveTo(new Vector2(0, i * CellPx));
                p.LineTo(new Vector2(N * CellPx, i * CellPx));
                p.Stroke();
            }

            if (editor.mirror)
            {
                p.strokeColor = CityUi.Alpha(CityUi.Red, 0.7f);
                p.lineWidth = 2.5f;
                p.BeginPath();
                for (float y = 0; y < N * CellPx; y += 16f)
                {
                    p.MoveTo(new Vector2(N * CellPx * 0.5f, y));
                    p.LineTo(new Vector2(N * CellPx * 0.5f, Mathf.Min(N * CellPx, y + 9f)));
                }
                p.Stroke();
            }
        }

        static void Cell(Painter2D p, int x, int y, int width, Color colour)
        {
            p.BeginPath();
            p.MoveTo(new Vector2(x * CellPx, y * CellPx));
            p.LineTo(new Vector2((x + width) * CellPx, y * CellPx));
            p.LineTo(new Vector2((x + width) * CellPx, (y + 1) * CellPx));
            p.LineTo(new Vector2(x * CellPx, (y + 1) * CellPx));
            p.ClosePath();
            p.fillColor = colour;
            p.Fill();
        }
    }

    // ---- Starter shapes and letters ------------------------------------------------------------------------------

    void StampShape(string shape)
    {
        for (int y = 0; y < N; y++)
        {
            for (int x = 0; x < N; x++)
            {
                if (Inside(shape, x + 0.5f - N * 0.5f, y + 0.5f - N * 0.5f)) Logo[x, y] = colour;
            }
        }
    }

    /// <summary>Is a point (relative to the middle, in pixels) inside the shape?</summary>
    static bool Inside(string shape, float x, float y)
    {
        switch (shape)
        {
            case "circle":
                return x * x + y * y <= 14.5f * 14.5f;
            case "square":
            {
                float ax = Mathf.Max(0, Mathf.Abs(x) - 10f), ay = Mathf.Max(0, Mathf.Abs(y) - 10f);
                return Mathf.Abs(x) <= 14.5f && Mathf.Abs(y) <= 14.5f && ax * ax + ay * ay <= 4.5f * 4.5f;
            }
            case "diamond":
                return Mathf.Abs(x) + Mathf.Abs(y) <= 15.5f;
            case "hexagon":
            {
                float ax = Mathf.Abs(x), ay = Mathf.Abs(y);
                return ay <= 13.5f && ax <= 15.5f - ay * 0.5774f;
            }
            case "shield":
            {
                if (y < -13.5f || y > 15.5f) return false;
                if (y <= 1f) return Mathf.Abs(x) <= 13.5f;
                float t = (y - 1f) / 14.5f;
                return Mathf.Abs(x) <= 13.5f * Mathf.Sqrt(Mathf.Max(0f, 1f - t * t));
            }
            case "heart":
            {
                float hx = x / 13f, hy = -(y + 2f) / 13f;
                float a = hx * hx + hy * hy - 1f;
                return a * a * a - hx * hx * hy * hy * hy <= 0f;
            }
            case "star":
            {
                // Five points: inside when inside the pentagram's outline
                float angle = Mathf.Atan2(x, -y);
                float r = Mathf.Sqrt(x * x + y * y);
                float sector = Mathf.PI * 2f / 5f;
                float local = Mathf.Repeat(angle + sector * 0.5f, sector) - sector * 0.5f;
                float outer = 15.5f, inner = 6.5f;
                // Distance to the edge between an outer point (0) and an inner corner (sector/2), by angle
                float t = Mathf.Abs(local) / (sector * 0.5f);
                float edge = 1f / ((1f - t) / outer + t / inner);
                return r <= edge;
            }
        }
        return false;
    }

    void StampLetters(string text) => StampLetters(Logo, text, colour);

    /// <summary>Up to 3 letters in the middle of a drawing (big when there are 1 or 2).</summary>
    public static void StampLetters(CityPixelLogo logo, string text, byte colour)
    {
        var glyphs = new List<string[]>();
        foreach (char raw in (text ?? "").ToUpperInvariant())
        {
            if (Font.TryGetValue(raw, out var glyph)) glyphs.Add(glyph);
            if (glyphs.Count == 3) break;
        }
        if (glyphs.Count == 0) return;

        int scale = glyphs.Count <= 2 ? 2 : 1;
        int width = glyphs.Count * 5 * scale + (glyphs.Count - 1) * scale;
        int height = 7 * scale;
        int left = (N - width) / 2, top = (N - height) / 2;
        for (int g = 0; g < glyphs.Count; g++)
        {
            int gx = left + g * (5 + 1) * scale;
            for (int row = 0; row < 7; row++)
            {
                for (int col = 0; col < 5; col++)
                {
                    if (glyphs[g][row][col] != '#') continue;
                    for (int sy = 0; sy < scale; sy++)
                    for (int sx = 0; sx < scale; sx++)
                    {
                        int px = gx + col * scale + sx, py = top + row * scale + sy;
                        if (px >= 0 && py >= 0 && px < N && py < N) logo[px, py] = colour;
                    }
                }
            }
        }
    }

    /// <summary>A 5x7 pixel font for the letter stamp.</summary>
    static readonly Dictionary<char, string[]> Font = new Dictionary<char, string[]>
    {
        { 'A', new[] { ".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" } },
        { 'B', new[] { "####.", "#...#", "#...#", "####.", "#...#", "#...#", "####." } },
        { 'C', new[] { ".###.", "#...#", "#....", "#....", "#....", "#...#", ".###." } },
        { 'D', new[] { "####.", "#...#", "#...#", "#...#", "#...#", "#...#", "####." } },
        { 'E', new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#####" } },
        { 'F', new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#...." } },
        { 'G', new[] { ".###.", "#...#", "#....", "#.###", "#...#", "#...#", ".####" } },
        { 'H', new[] { "#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" } },
        { 'I', new[] { "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "#####" } },
        { 'J', new[] { "..###", "...#.", "...#.", "...#.", "...#.", "#..#.", ".##.." } },
        { 'K', new[] { "#...#", "#..#.", "#.#..", "##...", "#.#..", "#..#.", "#...#" } },
        { 'L', new[] { "#....", "#....", "#....", "#....", "#....", "#....", "#####" } },
        { 'M', new[] { "#...#", "##.##", "#.#.#", "#.#.#", "#...#", "#...#", "#...#" } },
        { 'N', new[] { "#...#", "##..#", "#.#.#", "#..##", "#...#", "#...#", "#...#" } },
        { 'O', new[] { ".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." } },
        { 'P', new[] { "####.", "#...#", "#...#", "####.", "#....", "#....", "#...." } },
        { 'Q', new[] { ".###.", "#...#", "#...#", "#...#", "#.#.#", "#..#.", ".##.#" } },
        { 'R', new[] { "####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#" } },
        { 'S', new[] { ".####", "#....", "#....", ".###.", "....#", "....#", "####." } },
        { 'T', new[] { "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.." } },
        { 'U', new[] { "#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." } },
        { 'V', new[] { "#...#", "#...#", "#...#", "#...#", "#...#", ".#.#.", "..#.." } },
        { 'W', new[] { "#...#", "#...#", "#...#", "#.#.#", "#.#.#", "##.##", "#...#" } },
        { 'X', new[] { "#...#", "#...#", ".#.#.", "..#..", ".#.#.", "#...#", "#...#" } },
        { 'Y', new[] { "#...#", "#...#", ".#.#.", "..#..", "..#..", "..#..", "..#.." } },
        { 'Z', new[] { "#####", "....#", "...#.", "..#..", ".#...", "#....", "#####" } },
        { '0', new[] { ".###.", "#...#", "#..##", "#.#.#", "##..#", "#...#", ".###." } },
        { '1', new[] { "..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###." } },
        { '2', new[] { ".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####" } },
        { '3', new[] { "####.", "....#", "....#", ".###.", "....#", "....#", "####." } },
        { '4', new[] { "...#.", "..##.", ".#.#.", "#..#.", "#####", "...#.", "...#." } },
        { '5', new[] { "#####", "#....", "####.", "....#", "....#", "#...#", ".###." } },
        { '6', new[] { ".###.", "#....", "#....", "####.", "#...#", "#...#", ".###." } },
        { '7', new[] { "#####", "....#", "...#.", "..#..", ".#...", ".#...", ".#..." } },
        { '8', new[] { ".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###." } },
        { '9', new[] { ".###.", "#...#", "#...#", ".####", "....#", "....#", ".###." } },
        { '$', new[] { "..#..", ".####", "#.#..", ".###.", "..#.#", "####.", "..#.." } },
        { '&', new[] { ".##..", "#..#.", "#.#..", ".#...", "#.#.#", "#..#.", ".##.#" } },
    };
}
