using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Line icons of the design (24 x 24, round-ish strokes), drawn as geometry so they need no sprites.</summary>
public enum BlueprintIconKind
{
    None,
    Pencil,
    Coin,
    Sofa,
    Star,
    Monitor,
    Cup,
    Building,
    Pin,
    Move,
    Trash,
    Plus,
    Minus,
    Close,
    Check,
    Pause,
    Clipboard,
    Rotate,
    TrendUp,
    TrendDown,
    Cards
}

/// <summary>
/// Draws one of the design's line icons in the graphic's colour, fitted into its RectTransform.
/// Stroke width is in icon units (the design uses 2.4 of 24).
/// Requires a CanvasRenderer (Unity UI 2.0's Graphic no longer adds one; see BlueprintBox).
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class BlueprintIcon : MaskableGraphic
{
    static readonly BlueprintMeshBuilder Shared = new BlueprintMeshBuilder();
    static readonly List<Vector2> P = new List<Vector2>(64);

    [SerializeField] BlueprintIconKind kind = BlueprintIconKind.None;
    [SerializeField] float strokeWidth = 2.4f;

    public BlueprintIconKind Kind
    {
        get => kind;
        set { if (kind != value) { kind = value; SetVerticesDirty(); } }
    }

    public float StrokeWidth
    {
        get => strokeWidth;
        set { if (strokeWidth != value) { strokeWidth = value; SetVerticesDirty(); } }
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (kind == BlueprintIconKind.None) return;
        Rect r = rectTransform.rect;
        float size = Mathf.Min(r.width, r.height);
        if (size <= 0f) return;

        BlueprintMeshBuilder m = Shared;
        m.Clear();
        m.Feather = BlueprintBox.SoftEdge(canvas);
        m.MiterLimit = 1.6f;
        m.SetYDownFrame(new Vector2(r.center.x - size * 0.5f, r.center.y + size * 0.5f), size / 24f);
        Draw(m, kind, strokeWidth, color);
        vh.AddUIVertexStream(m.Vertices, m.Indices);
    }

    /// <summary>Draw an icon into the builder's current frame (24 x 24 units, y down).</summary>
    public static void Draw(BlueprintMeshBuilder m, BlueprintIconKind kind, float w, Color32 c)
    {
        switch (kind)
        {
            case BlueprintIconKind.Pencil:
                Begin(4f, 20f); L(5f, 15.5f); L(16.5f, 4f); A(2.1f, false, true, 19.5f, 7f); L(8f, 18.5f); Stroke(m, w, c, true);
                m.Line(new Vector2(14.5f, 6f), new Vector2(17.5f, 9f), w, c);
                break;
            case BlueprintIconKind.Coin:
                m.Circle(new Vector2(12f, 12f), 9f, w, c);
                Begin(14.6f, 9.2f);
                C(14.1f, 8.4f, 13.1f, 7.9f, 12f, 7.9f);
                C(10.5f, 7.9f, 9.3f, 8.7f, 9.3f, 9.9f);
                C(9.3f, 12.7f, 14.8f, 11.4f, 14.8f, 14.2f);
                C(14.8f, 15.4f, 13.6f, 16.2f, 12f, 16.2f);
                C(10.8f, 16.2f, 9.7f, 15.7f, 9.1f, 14.8f);
                Stroke(m, w, c);
                m.Line(new Vector2(12f, 6.5f), new Vector2(12f, 7.9f), w, c);
                m.Line(new Vector2(12f, 16.9f), new Vector2(12f, 18.5f), w, c);
                break;
            case BlueprintIconKind.Sofa:
                Begin(5f, 11f); L(5f, 8.5f); A(2.5f, false, true, 7.5f, 6f); L(16.5f, 6f); A(2.5f, false, true, 19f, 8.5f); L(19f, 11f); Stroke(m, w, c);
                Begin(3f, 12.5f); A(1.8f, false, true, 6.5f, 12.5f); L(6.5f, 15f); L(17.5f, 15f); L(17.5f, 12.5f);
                A(1.8f, false, true, 21f, 12.5f); L(21f, 18f); L(3f, 18f); Stroke(m, w, c, true);
                m.Line(new Vector2(5.5f, 18f), new Vector2(5.5f, 20f), w, c);
                m.Line(new Vector2(18.5f, 18f), new Vector2(18.5f, 20f), w, c);
                break;
            case BlueprintIconKind.Star:
                Begin(12f, 3f); L(14.7f, 8.6f); L(20.8f, 9.5f); L(16.4f, 13.8f); L(17.4f, 19.9f); L(12f, 17f);
                L(6.6f, 19.9f); L(7.6f, 13.8f); L(3.2f, 9.5f); L(9.3f, 8.6f); Stroke(m, w, c, true);
                break;
            case BlueprintIconKind.Monitor:
                m.StrokeRoundRect(new Rect(3f, 4f, 18f, 12f), 2f, w, c);
                m.Line(new Vector2(8f, 20f), new Vector2(16f, 20f), w, c);
                m.Line(new Vector2(12f, 16f), new Vector2(12f, 20f), w, c);
                break;
            case BlueprintIconKind.Cup:
                Begin(4f, 9f); L(17f, 9f); L(17f, 14f); A(5f, false, true, 12f, 19f); L(9f, 19f); A(5f, false, true, 4f, 14f); Stroke(m, w, c, true);
                Begin(17f, 10.5f); L(18.5f, 10.5f); A(2.5f, false, true, 18.5f, 15.5f); L(17f, 15.5f); Stroke(m, w, c);
                Begin(8f, 3.5f); C(7.2f, 4.5f, 8.8f, 5.5f, 8f, 6.5f); Stroke(m, w, c);
                Begin(12f, 3.5f); C(11.2f, 4.5f, 12.8f, 5.5f, 12f, 6.5f); Stroke(m, w, c);
                break;
            case BlueprintIconKind.Building:
                Begin(4f, 21f); L(4f, 5.5f); L(12f, 3f); L(12f, 21f); Stroke(m, w, c);
                Begin(12f, 8f); L(20f, 10.5f); L(20f, 21f); Stroke(m, w, c);
                m.Line(new Vector2(2.5f, 21f), new Vector2(21.5f, 21f), w, c);
                m.Line(new Vector2(7.5f, 8f), new Vector2(9f, 8f), w, c);
                m.Line(new Vector2(7.5f, 12f), new Vector2(9f, 12f), w, c);
                m.Line(new Vector2(7.5f, 16f), new Vector2(9f, 16f), w, c);
                m.Line(new Vector2(15.5f, 13f), new Vector2(17f, 13f), w, c);
                m.Line(new Vector2(15.5f, 17f), new Vector2(17f, 17f), w, c);
                break;
            case BlueprintIconKind.Pin:
                Begin(12f, 21f); C(12f, 21f, 5f, 14.8f, 5f, 9.5f); A(7f, false, true, 19f, 9.5f); C(19f, 14.8f, 12f, 21f, 12f, 21f);
                Stroke(m, w, c, true);
                m.Circle(new Vector2(12f, 9.5f), 2.5f, w, c);
                break;
            case BlueprintIconKind.Move:
                m.Line(new Vector2(12f, 3f), new Vector2(12f, 21f), w, c);
                m.Line(new Vector2(3f, 12f), new Vector2(21f, 12f), w, c);
                Begin(9f, 6f); L(12f, 3f); L(15f, 6f); Stroke(m, w, c);
                Begin(9f, 18f); L(12f, 21f); L(15f, 18f); Stroke(m, w, c);
                Begin(6f, 9f); L(3f, 12f); L(6f, 15f); Stroke(m, w, c);
                Begin(18f, 9f); L(21f, 12f); L(18f, 15f); Stroke(m, w, c);
                break;
            case BlueprintIconKind.Trash:
                m.Line(new Vector2(4f, 7f), new Vector2(20f, 7f), w, c);
                Begin(9.5f, 7f); L(9.5f, 4.5f); L(14.5f, 4.5f); L(14.5f, 7f); Stroke(m, w, c);
                Begin(6f, 7f); L(7f, 20f); L(17f, 20f); L(18f, 7f); Stroke(m, w, c);
                m.Line(new Vector2(10f, 11f), new Vector2(10f, 16.5f), w, c);
                m.Line(new Vector2(14f, 11f), new Vector2(14f, 16.5f), w, c);
                break;
            case BlueprintIconKind.Plus:
                m.Line(new Vector2(12f, 5f), new Vector2(12f, 19f), w, c);
                m.Line(new Vector2(5f, 12f), new Vector2(19f, 12f), w, c);
                break;
            case BlueprintIconKind.Minus:
                m.Line(new Vector2(5f, 12f), new Vector2(19f, 12f), w, c);
                break;
            case BlueprintIconKind.Close:
                m.Line(new Vector2(6f, 6f), new Vector2(18f, 18f), w, c);
                m.Line(new Vector2(18f, 6f), new Vector2(6f, 18f), w, c);
                break;
            case BlueprintIconKind.Check:
                Begin(4f, 12.5f); L(9f, 17.5f); L(20f, 6.5f); Stroke(m, w, c);
                break;
            case BlueprintIconKind.Pause:
                m.Line(new Vector2(8.5f, 5f), new Vector2(8.5f, 19f), w, c);
                m.Line(new Vector2(15.5f, 5f), new Vector2(15.5f, 19f), w, c);
                break;
            case BlueprintIconKind.Clipboard:
                m.StrokeRoundRect(new Rect(5f, 4.5f, 14f, 16.5f), 2f, w, c);
                Begin(9f, 4.5f); L(9f, 3f); L(15f, 3f); L(15f, 4.5f); Stroke(m, w, c);
                m.Line(new Vector2(9f, 10f), new Vector2(15f, 10f), w, c);
                m.Line(new Vector2(9f, 14f), new Vector2(15f, 14f), w, c);
                m.Line(new Vector2(9f, 18f), new Vector2(12.5f, 18f), w, c);
                break;
            case BlueprintIconKind.Rotate:
                Begin(4f, 12f); A(8f, false, true, 17.7f, 6.4f); L(20f, 8.5f); Stroke(m, w, c);
                Begin(20f, 3.5f); L(20f, 8.5f); L(15f, 8.5f); Stroke(m, w, c);
                Begin(20f, 12f); A(8f, false, true, 6.3f, 17.6f); L(4f, 15.5f); Stroke(m, w, c);
                Begin(4f, 20.5f); L(4f, 15.5f); L(9f, 15.5f); Stroke(m, w, c);
                break;
            case BlueprintIconKind.TrendUp:
                Begin(3f, 17f); L(9f, 11f); L(13f, 15f); L(21f, 7f); Stroke(m, w, c);
                Begin(15f, 7f); L(21f, 7f); L(21f, 13f); Stroke(m, w, c);
                break;
            case BlueprintIconKind.TrendDown:
                Begin(3f, 7f); L(9f, 13f); L(13f, 9f); L(21f, 17f); Stroke(m, w, c);
                Begin(15f, 17f); L(21f, 17f); L(21f, 11f); Stroke(m, w, c);
                break;
            case BlueprintIconKind.Cards:
                m.RoundRectPath(P, new Rect(3f, 6.5f, 11f, 14.5f), new Vector4(2f, 2f, 2f, 2f));
                Rotate(P, new Vector2(8.5f, 13.75f), -10f);
                Stroke(m, w, c, true);
                m.StrokeRoundRect(new Rect(10f, 3f, 11f, 14.5f), 2f, w, c);
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

    static void A(float r, bool large, bool sweep, float x, float y)
    {
        BlueprintMeshBuilder.ArcTo(P, r, large, sweep, new Vector2(x, y));
    }

    static void C(float x1, float y1, float x2, float y2, float x, float y)
    {
        BlueprintMeshBuilder.CubicTo(P, new Vector2(x1, y1), new Vector2(x2, y2), new Vector2(x, y));
    }

    static void Stroke(BlueprintMeshBuilder m, float w, Color32 c, bool closed = false)
    {
        m.Stroke(P, w, c, closed);
    }

    static void Rotate(List<Vector2> pts, Vector2 pivot, float degrees)
    {
        float a = degrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(a), sin = Mathf.Sin(a);
        for (int i = 0; i < pts.Count; i++)
        {
            Vector2 d = pts[i] - pivot;
            pts[i] = pivot + new Vector2(d.x * cos - d.y * sin, d.x * sin + d.y * cos);
        }
    }
}
