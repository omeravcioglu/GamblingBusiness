using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A box in the "stationery" style of the design, sized to its RectTransform: fill (optionally a radial gradient),
/// border (solid or dashed), per-corner radii for the hand-drawn look, a hard or soft drop shadow, outer rings
/// (selection) and vertical stripes (wood). Everything is tinted by the graphic's colour, so Button tints work.
/// Requires a CanvasRenderer: Unity UI 2.0's Graphic no longer adds one, and in the Editor its lazy fallback keeps a
/// fake null, so the box would stay invisible and the raycaster would throw MissingComponentException.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class BlueprintBox : MaskableGraphic
{
    static readonly BlueprintMeshBuilder Shared = new BlueprintMeshBuilder();

    [SerializeField] Color fill = Color.white;
    [SerializeField] Color border = new Color32(29, 36, 51, 255);
    [SerializeField] float borderWidth = 2f;
    [Tooltip("Corner radii: top-left, top-right, bottom-right, bottom-left")]
    [SerializeField] Vector4 radii = new Vector4(10f, 14f, 9f, 15f);
    [SerializeField] bool dashed;
    [SerializeField] float dashLength = 8f;
    [SerializeField] float gapLength = 6f;

    [Header("Shadow")]
    [SerializeField] Color shadowColor = Color.clear;
    [Tooltip("Offset in pixels, y pointing down")]
    [SerializeField] Vector2 shadowOffset = new Vector2(3f, 4f);
    [Tooltip("0 = a hard shadow")]
    [SerializeField] float shadowBlur;

    [Header("Rings (outside the box)")]
    [SerializeField] Color ringColor = Color.clear;
    [SerializeField] float ringWidth;
    [SerializeField] Color ringOuterColor = Color.clear;
    [SerializeField] float ringOuterWidth;
    [Tooltip("A second border line outside the box (like CSS outline + outline-offset)")]
    [SerializeField] Color outlineColor = Color.clear;
    [SerializeField] float outlineWidth;
    [SerializeField] float outlineOffset;

    [Header("Extras")]
    [Tooltip("Centre colour of a radial gradient toward the fill colour (clear = flat fill)")]
    [SerializeField] Color gradientColor = Color.clear;
    [SerializeField] Vector2 gradientCenter = new Vector2(0.5f, 0.35f);
    [Tooltip("Vertical stripes (clear = none)")]
    [SerializeField] Color stripeColor = Color.clear;
    [SerializeField] float stripeSpacing = 11f;
    [SerializeField] float stripeWidth = 2f;

    public Color Fill { get => fill; set { if (fill != value) { fill = value; SetVerticesDirty(); } } }
    public Color Border { get => border; set { if (border != value) { border = value; SetVerticesDirty(); } } }
    public float BorderWidth { get => borderWidth; set { if (borderWidth != value) { borderWidth = value; SetVerticesDirty(); } } }
    public Vector4 Radii { get => radii; set { if (radii != value) { radii = value; SetVerticesDirty(); } } }
    public bool Dashed { get => dashed; set { if (dashed != value) { dashed = value; SetVerticesDirty(); } } }

    public void SetStyle(Color fillColor, Color borderColor, float width)
    {
        fill = fillColor;
        border = borderColor;
        borderWidth = width;
        SetVerticesDirty();
    }

    public void SetDash(bool on, float dash = 8f, float gap = 6f)
    {
        dashed = on;
        dashLength = dash;
        gapLength = gap;
        SetVerticesDirty();
    }

    /// <summary>Drop shadow; offset in pixels with y pointing down. Blur 0 = hard shadow.</summary>
    public void SetShadow(Color color, Vector2 offset, float blur = 0f)
    {
        shadowColor = color;
        shadowOffset = offset;
        shadowBlur = blur;
        SetVerticesDirty();
    }

    public void SetRings(Color inner, float innerWidth, Color outer, float outerWidth)
    {
        ringColor = inner;
        ringWidth = innerWidth;
        ringOuterColor = outer;
        ringOuterWidth = outerWidth;
        SetVerticesDirty();
    }

    /// <summary>A line around the box, <paramref name="offset"/> pixels outside its edge.</summary>
    public void SetOutline(Color color, float width, float offset)
    {
        outlineColor = color;
        outlineWidth = width;
        outlineOffset = offset;
        SetVerticesDirty();
    }

    public void SetGradient(Color centerColor, Vector2 center)
    {
        gradientColor = centerColor;
        gradientCenter = center;
        SetVerticesDirty();
    }

    public void SetStripes(Color color, float spacing, float width)
    {
        stripeColor = color;
        stripeSpacing = spacing;
        stripeWidth = width;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        if (r.width <= 0f || r.height <= 0f) return;

        BlueprintMeshBuilder m = Shared;
        m.Clear();
        m.Feather = SoftEdge(canvas);
        m.MiterLimit = 3f;
        m.SetYDownFrame(new Vector2(r.xMin, r.yMax));
        Rect box = new Rect(0f, 0f, r.width, r.height);

        if (shadowColor.a > 0f)
        {
            Rect s = box;
            s.position += shadowOffset;
            if (shadowBlur > 0f)
                m.FillRoundRect(Inflate(s, -shadowBlur * 0.5f), Grow(radii, -shadowBlur * 0.5f), Tint(shadowColor), shadowBlur);
            else
                m.FillRoundRect(s, radii, Tint(shadowColor));
        }
        if (ringOuterColor.a > 0f && ringOuterWidth > 0f)
        {
            float grow = ringWidth + ringOuterWidth;
            m.FillRoundRect(Inflate(box, grow), Grow(radii, grow), Tint(ringOuterColor));
        }
        if (ringColor.a > 0f && ringWidth > 0f)
            m.FillRoundRect(Inflate(box, ringWidth), Grow(radii, ringWidth), Tint(ringColor));

        if (fill.a > 0f)
        {
            if (gradientColor.a > 0f)
                m.FillRoundRectRadial(box, radii, new Vector2(box.width * gradientCenter.x, box.height * gradientCenter.y),
                                      Tint(gradientColor), Tint(fill));
            else
                m.FillRoundRect(box, radii, Tint(fill));
        }

        if (stripeColor.a > 0f && stripeSpacing > 1f && stripeWidth > 0f)
            DrawStripes(m, Inflate(box, -borderWidth), Tint(stripeColor));

        if (outlineColor.a > 0f && outlineWidth > 0f)
        {
            float grow = outlineOffset + outlineWidth * 0.5f;
            m.StrokeRoundRect(Inflate(box, grow), Grow(radii, grow), outlineWidth, Tint(outlineColor));
        }

        if (border.a > 0f && borderWidth > 0f)
        {
            Rect line = Inflate(box, -borderWidth * 0.5f);
            Vector4 lineRadii = Grow(radii, -borderWidth * 0.5f);
            if (dashed) m.StrokeRoundRect(line, lineRadii, borderWidth, Tint(border), dashLength, gapLength);
            else m.StrokeRoundRect(line, lineRadii, borderWidth, Tint(border));
        }

        vh.AddUIVertexStream(m.Vertices, m.Indices);
    }

    void DrawStripes(BlueprintMeshBuilder m, Rect area, Color32 c)
    {
        if (area.width <= 0f || area.height <= 0f) return;
        for (float x = area.xMin; x < area.xMax; x += stripeSpacing)
        {
            float x1 = Mathf.Min(x + stripeWidth, area.xMax);
            float mid = (x + x1) * 0.5f - area.xMin;
            float top = Mathf.Max(CornerInset(mid, radii.x), CornerInset(area.width - mid, radii.y));
            float bottom = Mathf.Max(CornerInset(mid, radii.w), CornerInset(area.width - mid, radii.z));
            float height = area.height - top - bottom;
            if (height > 0f) m.FillRect(new Rect(x, area.yMin + top, x1 - x, height), c, 0f);
        }
    }

    static float CornerInset(float distance, float radius)
    {
        if (radius <= 0f || distance >= radius) return 0f;
        float dx = radius - distance;
        return radius - Mathf.Sqrt(Mathf.Max(0f, radius * radius - dx * dx));
    }

    Color32 Tint(Color c)
    {
        return c * color;
    }

    /// <summary>About one screen pixel in canvas units.</summary>
    public static float SoftEdge(Canvas c)
    {
        if (c == null) return 1f;
        Canvas root = c.rootCanvas != null ? c.rootCanvas : c;
        return 1f / Mathf.Max(0.05f, root.scaleFactor);
    }

    static Rect Inflate(Rect r, float by)
    {
        return new Rect(r.xMin - by, r.yMin - by, r.width + by * 2f, r.height + by * 2f);
    }

    static Vector4 Grow(Vector4 r, float by)
    {
        return new Vector4(Mathf.Max(0f, r.x + by), Mathf.Max(0f, r.y + by), Mathf.Max(0f, r.z + by), Mathf.Max(0f, r.w + by));
    }
}
