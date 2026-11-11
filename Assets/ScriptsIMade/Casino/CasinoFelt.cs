using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The casino felt of the design boards: CSS radial-gradient(ellipse at 50% 35%, #2A7652 0%, #1D5A3E 55%, #123D29 100%)
/// over the whole rect (the ellipse reaches its farthest corner, like CSS), drawn as a grid whose vertex colours follow
/// the gradient, so the fall-off matches the boards. Requires a CanvasRenderer (Unity UI 2.0's Graphic no longer adds one).
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class CasinoFelt : MaskableGraphic
{
    [Tooltip("Centre of the gradient as a share of the rect (x from the left, y from the top)")]
    [SerializeField] Vector2 center = new Vector2(0.5f, 0.35f);
    [SerializeField] Color innerColor = UIColors.FeltHi;
    [SerializeField] Color middleColor = UIColors.Felt;
    [SerializeField] Color outerColor = UIColors.FeltDark;
    [Tooltip("Where the middle colour sits between the centre (0) and the farthest corner (1)")]
    [Range(0.01f, 0.99f)] [SerializeField] float middleStop = 0.55f;
    [Range(2, 64)] [SerializeField] int columns = 24;
    [Range(2, 64)] [SerializeField] int rows = 14;

    public Vector2 Center
    {
        get => center;
        set { center = value; SetVerticesDirty(); }
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        if (r.width <= 0f || r.height <= 0f) return;

        // CSS "ellipse farthest-corner": the farthest-side ellipse scaled by sqrt(2) passes through the farthest corner
        float cx = r.width * center.x, cy = r.height * center.y;
        float rx = Mathf.Max(cx, r.width - cx) * 1.41421356f;
        float ry = Mathf.Max(cy, r.height - cy) * 1.41421356f;
        rx = Mathf.Max(rx, 0.001f);
        ry = Mathf.Max(ry, 0.001f);

        int cols = Mathf.Max(2, columns), rowCount = Mathf.Max(2, rows);
        for (int j = 0; j <= rowCount; j++)
        {
            float down = r.height * j / rowCount;   // from the top, like CSS
            for (int i = 0; i <= cols; i++)
            {
                float across = r.width * i / cols;
                float dx = (across - cx) / rx, dy = (down - cy) / ry;
                float t = Mathf.Sqrt(dx * dx + dy * dy);
                Color c = t <= middleStop
                    ? Color.Lerp(innerColor, middleColor, t / middleStop)
                    : Color.Lerp(middleColor, outerColor, (t - middleStop) / (1f - middleStop));
                vh.AddVert(new Vector3(r.xMin + across, r.yMax - down, 0f), c * color, Vector4.zero);
            }
        }

        int stride = cols + 1;
        for (int j = 0; j < rowCount; j++)
        {
            for (int i = 0; i < cols; i++)
            {
                int a = j * stride + i, b = a + 1, c = a + stride, d = c + 1;
                vh.AddTriangle(a, b, d);
                vh.AddTriangle(d, c, a);
            }
        }
    }
}
