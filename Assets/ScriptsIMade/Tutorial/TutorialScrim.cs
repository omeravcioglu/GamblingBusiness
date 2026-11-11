using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The tour's dark overlay with a rounded hole over the element a step is about (the design's
/// "box-shadow: 0 0 0 4000px rgba(16, 13, 8, 0.72)" around the spot): one mesh made of four bands around the hole and
/// four corner pieces that fill the hole's corners outside its rounding, so no part is drawn twice (the scrim is
/// translucent). Without a hole it covers its whole rect. The hole is given in this rect's top-left coordinates (y down),
/// like the design's CSS; it may reach past the edges (the toolbar's tabs hang from the top of the screen).
/// Tinted by the graphic's colour. Takes no clicks (the tour puts a transparent blocker under it).
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class TutorialScrim : MaskableGraphic
{
    const int CornerSteps = 10;

    bool hasHole;
    Rect hole;
    float radius;

    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false;
    }

    /// <summary>Cut a rounded hole: <paramref name="topLeftRect"/> from this rect's top-left corner, y down.</summary>
    public void SetHole(Rect topLeftRect, float cornerRadius)
    {
        hasHole = topLeftRect.width > 0f && topLeftRect.height > 0f;
        hole = topLeftRect;
        radius = Mathf.Max(0f, cornerRadius);
        SetVerticesDirty();
    }

    /// <summary>No hole: the scrim covers everything.</summary>
    public void ClearHole()
    {
        if (!hasHole) return;
        hasHole = false;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        if (r.width <= 0f || r.height <= 0f) return;
        Color32 c = color;

        if (!hasHole)
        {
            Quad(vh, r.xMin, r.yMin, r.xMax, r.yMax, c);
            return;
        }

        // The hole in local space (y up)
        float hx0 = r.xMin + hole.xMin, hx1 = r.xMin + hole.xMax;
        float hy1 = r.yMax - hole.yMin, hy0 = r.yMax - hole.yMax;

        // Bands: above and below the hole across the whole width, then left and right of it
        if (hy1 < r.yMax) Quad(vh, r.xMin, Mathf.Max(hy1, r.yMin), r.xMax, r.yMax, c);
        if (hy0 > r.yMin) Quad(vh, r.xMin, r.yMin, r.xMax, Mathf.Min(hy0, r.yMax), c);
        float my0 = Mathf.Max(hy0, r.yMin), my1 = Mathf.Min(hy1, r.yMax);
        if (my1 > my0)
        {
            if (hx0 > r.xMin) Quad(vh, r.xMin, my0, Mathf.Min(hx0, r.xMax), my1, c);
            if (hx1 < r.xMax) Quad(vh, Mathf.Max(hx1, r.xMin), my0, r.xMax, my1, c);
        }

        // Corners of the hole outside its rounding
        float rad = Mathf.Min(radius, Mathf.Min(hx1 - hx0, hy1 - hy0) * 0.5f);
        if (rad <= 0.01f) return;
        Corner(vh, new Vector2(hx0, hy1), new Vector2(hx0 + rad, hy1 - rad), rad, 180f, 90f, c);   // top-left
        Corner(vh, new Vector2(hx1, hy1), new Vector2(hx1 - rad, hy1 - rad), rad, 90f, 0f, c);     // top-right
        Corner(vh, new Vector2(hx1, hy0), new Vector2(hx1 - rad, hy0 + rad), rad, 0f, -90f, c);    // bottom-right
        Corner(vh, new Vector2(hx0, hy0), new Vector2(hx0 + rad, hy0 + rad), rad, -90f, -180f, c); // bottom-left
    }

    static void Quad(VertexHelper vh, float x0, float y0, float x1, float y1, Color32 c)
    {
        if (x1 <= x0 || y1 <= y0) return;
        int s = vh.currentVertCount;
        vh.AddVert(new Vector3(x0, y0), c, Vector4.zero);
        vh.AddVert(new Vector3(x0, y1), c, Vector4.zero);
        vh.AddVert(new Vector3(x1, y1), c, Vector4.zero);
        vh.AddVert(new Vector3(x1, y0), c, Vector4.zero);
        vh.AddTriangle(s, s + 1, s + 2);
        vh.AddTriangle(s, s + 2, s + 3);
    }

    /// <summary>A fan from the hole's corner point along the rounding's arc (the part of the corner the hole doesn't show).</summary>
    static void Corner(VertexHelper vh, Vector2 corner, Vector2 centre, float rad, float fromDegrees, float toDegrees, Color32 c)
    {
        int s = vh.currentVertCount;
        vh.AddVert(corner, c, Vector4.zero);
        for (int i = 0; i <= CornerSteps; i++)
        {
            float a = Mathf.Lerp(fromDegrees, toDegrees, i / (float)CornerSteps) * Mathf.Deg2Rad;
            vh.AddVert(centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rad, c, Vector4.zero);
        }
        for (int i = 0; i < CornerSteps; i++)
            vh.AddTriangle(s, s + 1 + i, s + 2 + i);
    }
}
