using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One uGUI graphic that draws everything on the cash floor in a single mesh and draw call: every bill and its shadow,
/// the workers, the tool, the hose, hazards and particles. The view rebuilds it each frame (Begin, Add..., End); it is
/// the only thing on its canvas that changes, so nothing else gets rebuilt. Coordinates are this RectTransform's local
/// space. Shapes without a sprite use the atlas's white square.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class CashBatchGraphic : MaskableGraphic
{
    const int MaxQuads = 16000;   // 64,000 vertices: under the 16-bit index limit

    Texture texture;
    Mesh mesh;
    readonly List<Vector3> verts = new List<Vector3>(8192);
    readonly List<Color32> colors = new List<Color32>(8192);
    readonly List<Vector2> uvs = new List<Vector2>(8192);
    readonly List<int> tris = new List<int>(12288);
    CashAtlas.Sprite white;

    public int QuadCount => verts.Count / 4;

    public override Texture mainTexture => texture != null ? texture : s_WhiteTexture;

    public void SetAtlas(CashAtlas atlas)
    {
        texture = atlas != null ? atlas.Texture : null;
        white = atlas != null ? atlas.White : default;
        SetMaterialDirty();
    }

    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (mesh != null)
        {
            if (Application.isPlaying) Destroy(mesh);
            else DestroyImmediate(mesh);
        }
    }

    // ------------------------------------------------------------------
    // Building
    // ------------------------------------------------------------------

    public void Begin()
    {
        verts.Clear();
        colors.Clear();
        uvs.Clear();
        tris.Clear();
    }

    public void End()
    {
        SetVerticesDirty();
    }

    bool Room => verts.Count < MaxQuads * 4;

    /// <summary>A sprite centered on its pivot at <paramref name="pos"/>, <paramref name="size"/> wide/high, turned by <paramref name="degrees"/>.</summary>
    public void Sprite(in CashAtlas.Sprite sprite, Vector2 pos, Vector2 size, float degrees, Color32 color)
    {
        if (!Room) return;
        float rad = degrees * Mathf.Deg2Rad;
        float c = Mathf.Cos(rad), s = Mathf.Sin(rad);
        Vector2 min = -Vector2.Scale(sprite.pivot, size);
        Vector2 max = min + size;
        Rect uv = sprite.uv;
        AddQuad(Rot(pos, min.x, min.y, c, s), Rot(pos, min.x, max.y, c, s), Rot(pos, max.x, max.y, c, s), Rot(pos, max.x, min.y, c, s),
                new Vector2(uv.xMin, uv.yMin), new Vector2(uv.xMin, uv.yMax), new Vector2(uv.xMax, uv.yMax), new Vector2(uv.xMax, uv.yMin), color);
    }

    /// <summary>A sprite flipped horizontally (a worker walking left).</summary>
    public void SpriteFlipped(in CashAtlas.Sprite sprite, Vector2 pos, Vector2 size, float degrees, Color32 color, bool flipX)
    {
        if (!flipX)
        {
            Sprite(sprite, pos, size, degrees, color);
            return;
        }
        if (!Room) return;
        float rad = degrees * Mathf.Deg2Rad;
        float c = Mathf.Cos(rad), s = Mathf.Sin(rad);
        Vector2 pivot = new Vector2(1f - sprite.pivot.x, sprite.pivot.y);
        Vector2 min = -Vector2.Scale(pivot, size);
        Vector2 max = min + size;
        Rect uv = sprite.uv;
        AddQuad(Rot(pos, min.x, min.y, c, s), Rot(pos, min.x, max.y, c, s), Rot(pos, max.x, max.y, c, s), Rot(pos, max.x, min.y, c, s),
                new Vector2(uv.xMax, uv.yMin), new Vector2(uv.xMax, uv.yMax), new Vector2(uv.xMin, uv.yMax), new Vector2(uv.xMin, uv.yMin), color);
    }

    public void Rect(Rect r, Color32 color)
    {
        if (!Room) return;
        Rect uv = white.uv;
        Vector2 u = uv.center;
        AddQuad(new Vector2(r.xMin, r.yMin), new Vector2(r.xMin, r.yMax), new Vector2(r.xMax, r.yMax), new Vector2(r.xMax, r.yMin), u, u, u, u, color);
    }

    /// <summary>A solid rotated box (centered).</summary>
    public void Box(Vector2 center, Vector2 size, float degrees, Color32 color)
    {
        Sprite(new CashAtlas.Sprite { uv = new Rect(white.uv.center, Vector2.zero), pivot = new Vector2(0.5f, 0.5f) }, center, size, degrees, color);
    }

    /// <summary>A line segment <paramref name="width"/> thick.</summary>
    public void Line(Vector2 a, Vector2 b, float width, Color32 color)
    {
        if (!Room) return;
        Vector2 d = b - a;
        float len = d.magnitude;
        if (len < 0.01f) return;
        Vector2 n = new Vector2(-d.y, d.x) / len * (width * 0.5f);
        Vector2 u = white.uv.center;
        AddQuad(a - n, a + n, b + n, b - n, u, u, u, u, color);
    }

    /// <summary>A filled circle (a triangle fan made of quads).</summary>
    public void Circle(Vector2 center, float radius, Color32 color, int segments = 24)
    {
        if (radius <= 0f) return;
        Vector2 u = white.uv.center;
        float step = Mathf.PI * 2f / segments;
        for (int i = 0; i < segments; i += 2)
        {
            if (!Room) return;
            Vector2 p0 = center + new Vector2(Mathf.Cos(i * step), Mathf.Sin(i * step)) * radius;
            Vector2 p1 = center + new Vector2(Mathf.Cos((i + 1) * step), Mathf.Sin((i + 1) * step)) * radius;
            Vector2 p2 = center + new Vector2(Mathf.Cos((i + 2) * step), Mathf.Sin((i + 2) * step)) * radius;
            AddQuad(center, p0, p1, p2, u, u, u, u, color);
        }
    }

    /// <summary>A ring <paramref name="width"/> thick, optionally only part of it (0..1 of the turn, from the top, clockwise).</summary>
    public void Ring(Vector2 center, float radius, float width, Color32 color, float fraction = 1f, int segments = 40)
    {
        if (radius <= 0f || fraction <= 0f) return;
        Vector2 u = white.uv.center;
        int count = Mathf.Max(2, Mathf.CeilToInt(segments * Mathf.Clamp01(fraction)));
        float total = Mathf.PI * 2f * Mathf.Clamp01(fraction);
        float inner = Mathf.Max(0f, radius - width * 0.5f), outer = radius + width * 0.5f;
        for (int i = 0; i < count; i++)
        {
            if (!Room) return;
            float a0 = Mathf.PI * 0.5f - total * i / count;
            float a1 = Mathf.PI * 0.5f - total * (i + 1) / count;
            Vector2 d0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0));
            Vector2 d1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));
            AddQuad(center + d0 * inner, center + d0 * outer, center + d1 * outer, center + d1 * inner, u, u, u, u, color);
        }
    }

    void AddQuad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud, Color32 color)
    {
        int start = verts.Count;
        verts.Add(a);
        verts.Add(b);
        verts.Add(c);
        verts.Add(d);
        uvs.Add(ua);
        uvs.Add(ub);
        uvs.Add(uc);
        uvs.Add(ud);
        colors.Add(color);
        colors.Add(color);
        colors.Add(color);
        colors.Add(color);
        tris.Add(start);
        tris.Add(start + 1);
        tris.Add(start + 2);
        tris.Add(start + 2);
        tris.Add(start + 3);
        tris.Add(start);
    }

    static Vector2 Rot(Vector2 pos, float x, float y, float c, float s)
    {
        return new Vector2(pos.x + x * c - y * s, pos.y + x * s + y * c);
    }

    // ------------------------------------------------------------------
    // uGUI
    // ------------------------------------------------------------------

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();   // the geometry comes from UpdateGeometry below
    }

    protected override void UpdateGeometry()
    {
        if (mesh == null)
        {
            mesh = new Mesh { name = "CashFloorBatch" };
            mesh.MarkDynamic();
        }
        mesh.Clear();
        if (verts.Count > 0)
        {
            mesh.SetVertices(verts);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0, false);
        }
        canvasRenderer.SetMesh(mesh);
    }
}
