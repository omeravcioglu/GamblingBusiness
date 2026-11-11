using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Line icons of the run-end screens (24 x 24 like the design's SVGs). Coin, Close and Check come from the build mode kit.</summary>
public enum RunEndIconKind
{
    None,
    Restart,
    Home,
    Play,
    Bank,
    Person,
    Coin,
    Close,
    Check
}

/// <summary>
/// Draws one of the design's line icons in the graphic's colour, fitted into its RectTransform (stroke width in icon
/// units: the design uses 2.4 of 24). Uses the build mode's mesh builder, so it needs no sprites.
/// Requires a CanvasRenderer (Unity UI 2.0's Graphic no longer adds one; see BlueprintBox).
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class RunEndIcon : MaskableGraphic
{
    static readonly BlueprintMeshBuilder Shared = new BlueprintMeshBuilder();
    static readonly List<Vector2> P = new List<Vector2>(64);

    [SerializeField] RunEndIconKind kind = RunEndIconKind.None;
    [SerializeField] float strokeWidth = 2.4f;

    public RunEndIconKind Kind
    {
        get => kind;
        set { if (kind != value) { kind = value; SetVerticesDirty(); } }
    }

    public float StrokeWidth
    {
        get => strokeWidth;
        set { if (strokeWidth != value) { strokeWidth = value; SetVerticesDirty(); } }
    }

    /// <summary>A new icon of <paramref name="size"/> px (with a LayoutElement, for layout groups).</summary>
    public static RunEndIcon Create(Transform parent, RunEndIconKind kind, float size, Color color, float stroke = 2.4f)
    {
        RectTransform rect = BlueprintUI.NewRect("Icon", parent);
        rect.sizeDelta = new Vector2(size, size);
        RunEndIcon icon = rect.gameObject.AddComponent<RunEndIcon>();
        icon.kind = kind;
        icon.strokeWidth = stroke;
        icon.color = color;
        icon.raycastTarget = false;
        BlueprintUI.Layout(icon, size, size);
        return icon;
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (kind == RunEndIconKind.None) return;
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
    public static void Draw(BlueprintMeshBuilder m, RunEndIconKind kind, float w, Color32 c)
    {
        switch (kind)
        {
            case RunEndIconKind.Restart:
                // M20 12a8 8 0 1 1-2.4-5.7 / M20.5 4v5h-5
                Begin(20f, 12f);
                BlueprintMeshBuilder.ArcTo(P, 8f, true, true, new Vector2(17.6f, 6.3f), 28);
                m.Stroke(P, w, c, false);
                Begin(20.5f, 4f); L(20.5f, 9f); L(15.5f, 9f);
                m.Stroke(P, w, c, false);
                break;
            case RunEndIconKind.Home:
                // M4 11l8-7 8 7v9.5H4z / M10 20.5v-6h4v6
                Begin(4f, 11f); L(12f, 4f); L(20f, 11f); L(20f, 20.5f); L(4f, 20.5f);
                m.Stroke(P, w, c, true);
                Begin(10f, 20.5f); L(10f, 14.5f); L(14f, 14.5f); L(14f, 20.5f);
                m.Stroke(P, w, c, false);
                break;
            case RunEndIconKind.Play:
                // M7 4.5v15l12-7.5z
                Begin(7f, 4.5f); L(7f, 19.5f); L(19f, 12f);
                m.Stroke(P, w, c, true);
                break;
            case RunEndIconKind.Bank:
                // A banknote: rect 2.5,6 19x12 rx 2, circle r 2.8, two dots
                m.StrokeRoundRect(new Rect(2.5f, 6f, 19f, 12f), 2f, w, c);
                m.Circle(new Vector2(12f, 12f), 2.8f, w, c);
                m.FillCircle(new Vector2(6f, 9.5f), w * 0.5f, c);
                m.FillCircle(new Vector2(18f, 14.5f), w * 0.5f, c);
                break;
            case RunEndIconKind.Person:
                // circle 12,8 r4 / M4 21c0-4.4 3.6-7 8-7s8 2.6 8 7
                m.Circle(new Vector2(12f, 8f), 4f, w, c);
                Begin(4f, 21f);
                BlueprintMeshBuilder.CubicTo(P, new Vector2(4f, 16.6f), new Vector2(7.6f, 14f), new Vector2(12f, 14f));
                BlueprintMeshBuilder.CubicTo(P, new Vector2(16.4f, 14f), new Vector2(20f, 16.6f), new Vector2(20f, 21f));
                m.Stroke(P, w, c, false);
                break;
            case RunEndIconKind.Coin:
                BlueprintIcon.Draw(m, BlueprintIconKind.Coin, w, c);
                break;
            case RunEndIconKind.Close:
                BlueprintIcon.Draw(m, BlueprintIconKind.Close, w, c);
                break;
            case RunEndIconKind.Check:
                BlueprintIcon.Draw(m, BlueprintIconKind.Check, w, c);
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
}
