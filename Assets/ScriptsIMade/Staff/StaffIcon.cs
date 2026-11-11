using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Line icons of the staff files (24 x 24 like the design's SVGs; PenLoop is the CV's 150 x 120 red-pen circle).</summary>
public enum StaffIconKind
{
    None,
    People,
    Person,
    Close,
    Search,
    ChevronDown,
    ArrowLeft,
    ArrowRight,
    Pause,
    Star,
    Medal,
    Check,
    Cross,
    Wave,
    PenLoop
}

/// <summary>
/// Draws one of the staff files' line icons in the graphic's colour, fitted into its RectTransform (stroke width in icon
/// units: the design uses 2.4 of 24). A fill colour (clear = none) fills the closed shapes first: the gold star.
/// Uses the build mode's mesh builder, so it needs no sprites.
/// Requires a CanvasRenderer (Unity UI 2.0's Graphic no longer adds one; see BlueprintBox).
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class StaffIcon : MaskableGraphic
{
    static readonly BlueprintMeshBuilder Shared = new BlueprintMeshBuilder();
    static readonly List<Vector2> P = new List<Vector2>(64);

    [SerializeField] StaffIconKind kind = StaffIconKind.None;
    [SerializeField] float strokeWidth = 2.4f;
    [Tooltip("Fill of the closed shape (the star); clear = outline only")]
    [SerializeField] Color fillColor = Color.clear;

    public StaffIconKind Kind
    {
        get => kind;
        set { if (kind != value) { kind = value; SetVerticesDirty(); } }
    }

    public float StrokeWidth
    {
        get => strokeWidth;
        set { if (strokeWidth != value) { strokeWidth = value; SetVerticesDirty(); } }
    }

    public Color FillColor
    {
        get => fillColor;
        set { if (fillColor != value) { fillColor = value; SetVerticesDirty(); } }
    }

    /// <summary>A new icon of <paramref name="size"/> px, anchored to the parent's top-left corner.</summary>
    public static StaffIcon Create(Transform parent, StaffIconKind kind, float size, Color color, float stroke = 2.4f)
    {
        RectTransform rect = BlueprintUI.NewRect("Icon " + kind, parent);
        BlueprintUI.TopLeft(rect, 0f, 0f, size, size);
        StaffIcon icon = rect.gameObject.AddComponent<StaffIcon>();
        icon.kind = kind;
        icon.strokeWidth = stroke;
        icon.color = color;
        icon.raycastTarget = false;
        return icon;
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (kind == StaffIconKind.None) return;
        Rect r = rectTransform.rect;
        if (r.width <= 0f || r.height <= 0f) return;

        BlueprintMeshBuilder m = Shared;
        m.Clear();
        m.Feather = BlueprintBox.SoftEdge(canvas);
        m.MiterLimit = 1.6f;
        if (kind == StaffIconKind.PenLoop)
        {
            // The CV's 150 x 120 viewBox, stretched over the rect
            m.SetFrame(new Vector2(r.xMin, r.yMax), new Vector2(r.width / 150f, 0f), new Vector2(0f, -r.height / 120f));
        }
        else
        {
            float size = Mathf.Min(r.width, r.height);
            m.SetYDownFrame(new Vector2(r.center.x - size * 0.5f, r.center.y + size * 0.5f), size / 24f);
        }
        Color fill = fillColor;
        fill.a *= color.a;   // the fill keeps its own colour; only fades follow the graphic
        Draw(m, kind, strokeWidth, color, fill);
        vh.AddUIVertexStream(m.Vertices, m.Indices);
    }

    /// <summary>Draw an icon into the builder's current frame (24 x 24 units, y down; PenLoop 150 x 120).</summary>
    public static void Draw(BlueprintMeshBuilder m, StaffIconKind kind, float w, Color32 c, Color32 fill)
    {
        switch (kind)
        {
            case StaffIconKind.People:
                // circle 9,8 r3.2 / M3 20c0-3.5 2.7-6 6-6s6 2.5 6 6 / circle 17.5,9 r2.5 / M16.5 14.2c2.6.4 4.5 2.6 4.5 5.8
                m.Circle(new Vector2(9f, 8f), 3.2f, w, c);
                Begin(3f, 20f); C(3f, 16.5f, 5.7f, 14f, 9f, 14f); C(12.3f, 14f, 15f, 16.5f, 15f, 20f); m.Stroke(P, w, c, false);
                m.Circle(new Vector2(17.5f, 9f), 2.5f, w, c);
                Begin(16.5f, 14.2f); C(19.1f, 14.6f, 21f, 16.8f, 21f, 20f); m.Stroke(P, w, c, false);
                break;
            case StaffIconKind.Person:
                // circle 12,8 r4 / M4 21c0-4.4 3.6-7 8-7s8 2.6 8 7
                m.Circle(new Vector2(12f, 8f), 4f, w, c);
                Begin(4f, 21f); C(4f, 16.6f, 7.6f, 14f, 12f, 14f); C(16.4f, 14f, 20f, 16.6f, 20f, 21f); m.Stroke(P, w, c, false);
                break;
            case StaffIconKind.Close:
                BlueprintIcon.Draw(m, BlueprintIconKind.Close, w, c);
                break;
            case StaffIconKind.Search:
                // circle 10.5,10.5 r6.5 / M15.5 15.5L21 21
                m.Circle(new Vector2(10.5f, 10.5f), 6.5f, w, c);
                m.Line(new Vector2(15.5f, 15.5f), new Vector2(21f, 21f), w, c);
                break;
            case StaffIconKind.ChevronDown:
                Begin(6f, 9f); L(12f, 15f); L(18f, 9f); m.Stroke(P, w, c, false);
                break;
            case StaffIconKind.ArrowLeft:
                // M20 12H5 / M11 6l-6 6 6 6
                m.Line(new Vector2(20f, 12f), new Vector2(5f, 12f), w, c);
                Begin(11f, 6f); L(5f, 12f); L(11f, 18f); m.Stroke(P, w, c, false);
                break;
            case StaffIconKind.ArrowRight:
                // M4 12h15 / M13 6l6 6-6 6
                m.Line(new Vector2(4f, 12f), new Vector2(19f, 12f), w, c);
                Begin(13f, 6f); L(19f, 12f); L(13f, 18f); m.Stroke(P, w, c, false);
                break;
            case StaffIconKind.Pause:
                BlueprintIcon.Draw(m, BlueprintIconKind.Pause, w, c);
                break;
            case StaffIconKind.Star:
                // M12 3l2.7 5.6 6.1.9-4.4 4.3 1 6.1L12 17l-5.4 2.9 1-6.1-4.4-4.3 6.1-.9z
                Begin(12f, 3f); L(14.7f, 8.6f); L(20.8f, 9.5f); L(16.4f, 13.8f); L(17.4f, 19.9f); L(12f, 17f);
                L(6.6f, 19.9f); L(7.6f, 13.8f); L(3.2f, 9.5f); L(9.3f, 8.6f);
                if (fill.a > 0) m.FillPolygon(P, fill);
                m.Stroke(P, w, c, true);
                break;
            case StaffIconKind.Medal:
                // circle 12,9 r6 / M8.5 13.8L7 21l5-2.5 5 2.5-1.5-7.2
                m.Circle(new Vector2(12f, 9f), 6f, w, c);
                Begin(8.5f, 13.8f); L(7f, 21f); L(12f, 18.5f); L(17f, 21f); L(15.5f, 13.8f); m.Stroke(P, w, c, false);
                break;
            case StaffIconKind.Check:
                BlueprintIcon.Draw(m, BlueprintIconKind.Check, w, c);
                break;
            case StaffIconKind.Cross:
                BlueprintIcon.Draw(m, BlueprintIconKind.Close, w, c);
                break;
            case StaffIconKind.Wave:
                // M3 13c2-3 4-3 6 0s4 3 6 0 4-3 6 0
                Begin(3f, 13f); C(5f, 10f, 7f, 10f, 9f, 13f); C(11f, 16f, 13f, 16f, 15f, 13f); C(17f, 10f, 19f, 10f, 21f, 13f);
                m.Stroke(P, w, c, false);
                break;
            case StaffIconKind.PenLoop:
                // M40 20C80 4 138 12 144 56C150 98 104 118 68 116C28 114 6 92 9 60C12 30 46 14 92 12
                Begin(40f, 20f); C(80f, 4f, 138f, 12f, 144f, 56f); C(150f, 98f, 104f, 118f, 68f, 116f);
                C(28f, 114f, 6f, 92f, 9f, 60f); C(12f, 30f, 46f, 14f, 92f, 12f);
                m.Stroke(P, w, c, false);
                break;
        }
    }

    static void Begin(float x, float y)
    {
        P.Clear();
        P.Add(new Vector2(x, y));
    }

    static void L(float x, float y)
    {
        P.Add(new Vector2(x, y));
    }

    static void C(float x1, float y1, float x2, float y2, float x, float y)
    {
        BlueprintMeshBuilder.CubicTo(P, new Vector2(x1, y1), new Vector2(x2, y2), new Vector2(x, y));
    }
}
