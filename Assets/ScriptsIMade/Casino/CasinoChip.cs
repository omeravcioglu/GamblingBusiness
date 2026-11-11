using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A casino chip of the design boards, fitted into its rect: a coloured disc with four light edge spots (the boards'
/// conic-gradient), an ink border and, when showInner is on, the inner disc with a dashed ring where the value label
/// sits. Selected: raised by Lift px inside a highlighter ring (5 px yellow, 2 px ink) while its hard shadow stays on
/// the felt. Everything is tinted by the graphic's colour. Requires a CanvasRenderer.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class CasinoChip : MaskableGraphic
{
    static readonly BlueprintMeshBuilder Shared = new BlueprintMeshBuilder();
    static readonly System.Collections.Generic.List<Vector2> Wedge = new System.Collections.Generic.List<Vector2>(16);

    [SerializeField] Color chipColor = UIColors.Loss;
    [Tooltip("The edge spots and the dashed inner ring")]
    [SerializeField] Color spotColor = UIColors.PaperHi;
    [SerializeField] Color borderColor = UIColors.Ink;
    [SerializeField] float borderWidth = 3f;
    [SerializeField] bool showInner = true;
    [Tooltip("Inner disc diameter as a share of the chip")]
    [SerializeField] float innerShare = 50f / 76f;
    [Header("Shadow and selection")]
    [SerializeField] Color shadowColor = Color.clear;
    [Tooltip("Hard shadow below the chip's resting place (px, y down)")]
    [SerializeField] float shadowDrop = 5f;
    [SerializeField] bool selected;
    [Tooltip("How far a selected chip is raised (px)")]
    [SerializeField] float lift = 10f;

    public Color ChipColor { get => chipColor; set { chipColor = value; SetVerticesDirty(); } }
    public Color SpotColor { get => spotColor; set { spotColor = value; SetVerticesDirty(); } }
    public bool Selected { get => selected; set { if (selected != value) { selected = value; SetVerticesDirty(); } } }
    public float Lift => selected ? lift : 0f;

    public void SetStyle(Color chip, Color spots, float border, bool inner)
    {
        chipColor = chip;
        spotColor = spots;
        borderWidth = border;
        showInner = inner;
        SetVerticesDirty();
    }

    public void SetShadow(Color color, float drop)
    {
        shadowColor = color;
        shadowDrop = drop;
        SetVerticesDirty();
    }

    Color32 Tint(Color c)
    {
        return c * color;
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        float size = Mathf.Min(r.width, r.height);
        if (size <= 0f) return;

        BlueprintMeshBuilder m = Shared;
        m.Clear();
        m.Feather = BlueprintBox.SoftEdge(canvas);
        m.SetYDownFrame(new Vector2(r.center.x - size * 0.5f, r.center.y + size * 0.5f));
        float radius = size * 0.5f;
        Vector2 rest = new Vector2(radius, radius);
        Vector2 c = rest - new Vector2(0f, Lift);

        if (shadowColor.a > 0f)
            m.FillCircle(rest + new Vector2(0f, shadowDrop), radius, Tint(shadowColor));
        if (selected)
        {
            m.FillCircle(c, radius + 7f, Tint(UIColors.Ink));
            m.FillCircle(c, radius + 5f, Tint(UIColors.Highlight));
        }

        m.FillCircle(c, radius, Tint(chipColor));
        // Four spots on the edge, 3% of the turn each, centred at 12.5%, 37.5%, 62.5% and 87.5% (clockwise from the top)
        for (int i = 0; i < 4; i++)
        {
            float from = (0.11f + i * 0.25f) * 360f, to = (0.14f + i * 0.25f) * 360f;
            Wedge.Clear();
            Wedge.Add(c);
            for (int s = 0; s <= 4; s++)
            {
                float turn = Mathf.Lerp(from, to, s / 4f) * Mathf.Deg2Rad;
                Wedge.Add(c + new Vector2(Mathf.Sin(turn), -Mathf.Cos(turn)) * radius);
            }
            m.FillConvex(Wedge, Tint(spotColor));
        }
        if (borderWidth > 0f)
            m.Circle(c, radius - borderWidth * 0.5f, borderWidth, Tint(borderColor));

        if (showInner)
        {
            float inner = radius * Mathf.Clamp01(innerShare);
            m.FillCircle(c, inner, Tint(chipColor));
            float ring = Mathf.Max(1f, size / 38f);
            m.Circle(c, inner - ring * 0.5f, ring, Tint(spotColor), ring * 2.5f, ring * 2f);
        }
        vh.AddUIVertexStream(m.Vertices, m.Indices);
    }
}
