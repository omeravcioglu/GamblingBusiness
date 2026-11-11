using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A filled star sized to its RectTransform; by default the design's four-pointed sparkle (the company mark on the
/// letterhead and the ledger's logo). Edges are softened over about one screen pixel.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class StatusStar : MaskableGraphic
{
    [Range(3, 12)]
    [SerializeField] int points = 4;
    [Tooltip("Distance of the inner corners from the centre, as a share of the points' distance")]
    [Range(0.05f, 1f)]
    [SerializeField] float innerRatio = 0.28f;

    public int Points { get => points; set { points = Mathf.Clamp(value, 3, 12); SetVerticesDirty(); } }
    public float InnerRatio { get => innerRatio; set { innerRatio = Mathf.Clamp(value, 0.05f, 1f); SetVerticesDirty(); } }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        float radius = Mathf.Min(r.width, r.height) * 0.5f;
        if (radius <= 0f) return;

        float half = StatusBox.SoftEdge(canvas) * 0.5f;
        Color32 solid = color;
        Color32 clear = solid;
        clear.a = 0;
        Vector2 center = r.center;
        int corners = Mathf.Clamp(points, 3, 12) * 2;

        vh.AddVert(center, solid, Vector4.zero);
        for (int k = 0; k < corners; k++)
        {
            float angle = Mathf.PI * 0.5f + k * Mathf.PI * 2f / corners;   // the first point straight up
            Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            float distance = k % 2 == 0 ? radius : radius * innerRatio;
            vh.AddVert(center + dir * Mathf.Max(0f, distance - half), solid, Vector4.zero);
            vh.AddVert(center + dir * (distance + half), clear, Vector4.zero);
        }
        for (int k = 0; k < corners; k++)
        {
            int a = 1 + k * 2, b = 1 + (k + 1) % corners * 2;
            vh.AddTriangle(0, a, b);
            vh.AddTriangle(a, a + 1, b + 1);
            vh.AddTriangle(a, b + 1, b);
        }
    }
}
