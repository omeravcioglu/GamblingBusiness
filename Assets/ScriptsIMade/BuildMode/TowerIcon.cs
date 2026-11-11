using UnityEngine;
using UnityEngine.UI;

/// <summary>Line icons of the HQ tower design (24 x 24), see TowerArt.Icon.</summary>
public enum TowerIconKind
{
    None,
    Home,
    Briefcase,
    People,
    Coffee,
    Clipboard,
    Monitor,
    Chart,
    Dice,
    Plus,
    Minus,
    Building,
    Sofa,
    Move,
    Tag,
    Check,
    Warning,
    Lock,
    Cash,
    Cards,
    Close,
    ArrowRight,
    ArrowLeft,
    TrendUp,
    TrendDown,
    Coin,
    Sparkle,
    ChevronUp,
    ChevronDown,
    Pin,
    Pause,
    Desk,
    Plant,
    Bookshelf,
    Fish,
    Reception,
    Arcade,
    Table,
    Whiteboard,
    Projector,
    ServerRack,
    Cooler,
    Terminal,
    Ticker,
    SlotMachine,
    Roulette,
    CardTable,
    Bar,
    Person,
    // The campus (2026-09-29)
    Treadmill,
    Dumbbell,
    Food,
    Cabinet,
    Flask,
    Cubicle,
    Trophy,
    Helicopter,
    Parasol,
    Scales,
    Star,
    Campus,
    Bridge,
    Upgrade,
    IdBadge
}

/// <summary>
/// Draws one of the tower design's line icons in the graphic's colour, fitted into its RectTransform. Stroke width is in
/// icon units (the design uses 2.4 of 24); the mesh is only rebuilt when the icon, size or colour changes.
/// Requires a CanvasRenderer (Unity UI 2.0's Graphic no longer adds one; see BlueprintBox).
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class TowerIcon : MaskableGraphic
{
    static readonly BlueprintMeshBuilder Shared = new BlueprintMeshBuilder();

    [SerializeField] TowerIconKind kind = TowerIconKind.None;
    [SerializeField] float strokeWidth = 2.4f;

    public TowerIconKind Kind
    {
        get => kind;
        set { if (kind != value) { kind = value; SetVerticesDirty(); } }
    }

    public float StrokeWidth
    {
        get => strokeWidth;
        set { if (strokeWidth != value) { strokeWidth = value; SetVerticesDirty(); } }
    }

    /// <summary>Change-only colour setter (writing the same colour still dirties the canvas).</summary>
    public void SetColor(Color value)
    {
        if (color != value) color = value;
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (kind == TowerIconKind.None) return;
        Rect r = rectTransform.rect;
        float size = Mathf.Min(r.width, r.height);
        if (size <= 0f) return;
        TowerPart[] parts = TowerArt.Icon(kind);
        if (parts == null) return;

        BlueprintMeshBuilder m = Shared;
        m.Clear();
        m.Feather = BlueprintBox.SoftEdge(canvas);
        m.MiterLimit = 1.6f;
        m.SetYDownFrame(new Vector2(r.center.x - size * 0.5f, r.center.y + size * 0.5f), size / 24f);
        TowerArt.DrawParts(m, parts, TowerPalette.For(Color.white, Color.white), strokeWidth / 2.4f);
        // The icon takes the graphic's colour (Button tints included)
        Color32 tint = color;
        for (int i = 0; i < m.Vertices.Count; i++)
        {
            UIVertex v = m.Vertices[i];
            v.color = new Color32((byte)(v.color.r * tint.r / 255), (byte)(v.color.g * tint.g / 255), (byte)(v.color.b * tint.b / 255),
                                  (byte)(v.color.a * tint.a / 255));
            m.Vertices[i] = v;
        }
        vh.AddUIVertexStream(m.Vertices, m.Indices);
    }
}
