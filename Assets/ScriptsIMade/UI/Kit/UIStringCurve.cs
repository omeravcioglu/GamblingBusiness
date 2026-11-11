using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A tag string: two cubic curves (a loop through an eyelet) drawn as an ink edge with a lighter core, like the
/// design's SVG strokes (ink 5 px under manila 2.5 px, round caps). Points are in this graphic's local space.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class UIStringCurve : MaskableGraphic
{
    public Color edgeColor = new Color32(0x1D, 0x24, 0x33, 0xFF);
    public float edgeWidth = 5f;
    public Color coreColor = new Color32(0xE7, 0xC9, 0x8C, 0xFF);
    public float coreWidth = 2.5f;
    [Range(4, 64)] public int segments = 24;

    [SerializeField] Vector2[] strandA = new Vector2[4];
    [SerializeField] Vector2[] strandB = new Vector2[4];

    static readonly Vector2[] Samples = new Vector2[65];

    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false;
    }

    /// <summary>Set both strands (cubic Bezier control points, local space).</summary>
    public void SetStrands(Vector2 a0, Vector2 a1, Vector2 a2, Vector2 a3, Vector2 b0, Vector2 b1, Vector2 b2, Vector2 b3)
    {
        if (strandA == null || strandA.Length != 4) strandA = new Vector2[4];
        if (strandB == null || strandB.Length != 4) strandB = new Vector2[4];
        strandA[0] = a0; strandA[1] = a1; strandA[2] = a2; strandA[3] = a3;
        strandB[0] = b0; strandB[1] = b1; strandB[2] = b2; strandB[3] = b3;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Stroke(vh, strandA, edgeWidth, edgeColor);
        Stroke(vh, strandB, edgeWidth, edgeColor);
        Stroke(vh, strandA, coreWidth, coreColor);
        Stroke(vh, strandB, coreWidth, coreColor);
    }

    static Vector2 Bezier(Vector2[] p, float t)
    {
        float u = 1f - t;
        return u * u * u * p[0] + 3f * u * u * t * p[1] + 3f * u * t * t * p[2] + t * t * t * p[3];
    }

    void Stroke(VertexHelper vh, Vector2[] p, float width, Color c)
    {
        if (p == null || p.Length != 4 || width <= 0f) return;
        int n = Mathf.Clamp(segments, 4, Samples.Length - 1);
        for (int i = 0; i <= n; i++) Samples[i] = Bezier(p, i / (float)n);
        Color32 col = c * color;
        float h = width * 0.5f;

        int start = vh.currentVertCount;
        for (int i = 0; i <= n; i++)
        {
            Vector2 dir;
            if (i == 0) dir = Samples[1] - Samples[0];
            else if (i == n) dir = Samples[n] - Samples[n - 1];
            else dir = (Samples[i + 1] - Samples[i - 1]);
            if (dir.sqrMagnitude < 1e-8f) dir = Vector2.right;
            dir.Normalize();
            var normal = new Vector2(-dir.y, dir.x) * h;
            vh.AddVert(Samples[i] + normal, col, Vector4.zero);
            vh.AddVert(Samples[i] - normal, col, Vector4.zero);
        }
        for (int i = 0; i < n; i++)
        {
            int a = start + i * 2;
            vh.AddTriangle(a, a + 1, a + 3);
            vh.AddTriangle(a, a + 3, a + 2);
        }
        Cap(vh, Samples[0], h, col);
        Cap(vh, Samples[n], h, col);
    }

    static void Cap(VertexHelper vh, Vector2 centre, float radius, Color32 col)
    {
        const int steps = 12;
        int c = vh.currentVertCount;
        vh.AddVert(centre, col, Vector4.zero);
        for (int i = 0; i < steps; i++)
        {
            float a = i * Mathf.PI * 2f / steps;
            vh.AddVert(centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius, col, Vector4.zero);
        }
        for (int i = 0; i < steps; i++)
            vh.AddTriangle(c, c + 1 + i, c + 1 + (i + 1) % steps);
    }
}
