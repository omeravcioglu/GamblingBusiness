using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Strokes one or more polylines (round joins and caps), like SVG paths with stroke-linecap/linejoin round.
/// Used for the skill board's red strings and its hand-drawn arrow. Points are in this graphic's local space.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class UIPolyline : MaskableGraphic
{
    public float width = 3f;
    [Range(4, 24)] public int capSteps = 10;

    readonly List<Vector2[]> paths = new List<Vector2[]>();

    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false;
    }

    public void ClearPaths()
    {
        paths.Clear();
        SetVerticesDirty();
    }

    public void AddPath(params Vector2[] points)
    {
        if (points != null && points.Length >= 2)
            paths.Add(points);
        SetVerticesDirty();
    }

    /// <summary>Points along a cubic Bezier (excluding p0), for building paths.</summary>
    public static void AppendCubic(List<Vector2> into, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, int steps = 16)
    {
        for (int i = 1; i <= steps; i++)
        {
            float t = i / (float)steps, u = 1f - t;
            into.Add(u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3);
        }
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        float h = width * 0.5f;
        Color32 col = color;
        foreach (var p in paths)
        {
            for (int i = 0; i + 1 < p.Length; i++)
            {
                Vector2 a = p[i], b = p[i + 1];
                Vector2 d = b - a;
                if (d.sqrMagnitude < 1e-6f) continue;
                Vector2 n = new Vector2(-d.y, d.x).normalized * h;
                int s = vh.currentVertCount;
                vh.AddVert(a + n, col, Vector4.zero);
                vh.AddVert(a - n, col, Vector4.zero);
                vh.AddVert(b - n, col, Vector4.zero);
                vh.AddVert(b + n, col, Vector4.zero);
                vh.AddTriangle(s, s + 1, s + 2);
                vh.AddTriangle(s, s + 2, s + 3);
            }
            // round joins and caps
            for (int i = 0; i < p.Length; i++)
                Disc(vh, p[i], h, col);
        }
    }

    void Disc(VertexHelper vh, Vector2 c, float r, Color32 col)
    {
        int steps = Mathf.Max(4, capSteps);
        int s = vh.currentVertCount;
        vh.AddVert(c, col, Vector4.zero);
        for (int i = 0; i < steps; i++)
        {
            float a = i * Mathf.PI * 2f / steps;
            vh.AddVert(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r, col, Vector4.zero);
        }
        for (int i = 0; i < steps; i++)
            vh.AddTriangle(s, s + 1 + i, s + 1 + (i + 1) % steps);
    }
}
