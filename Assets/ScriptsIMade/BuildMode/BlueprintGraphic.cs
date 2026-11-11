using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A UI graphic that shows whatever was drawn into its Mesh builder (lines, dashes, arcs, rounded boxes).
/// Draw, then call Commit(); the mesh is only rebuilt then, not every frame.
/// Coordinates are local to this RectTransform (y up); use Mesh.SetYDownFrame for plan pixels.
/// Requires a CanvasRenderer (Unity UI 2.0's Graphic no longer adds one; see BlueprintBox).
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class BlueprintGraphic : MaskableGraphic
{
    readonly BlueprintMeshBuilder mesh = new BlueprintMeshBuilder();

    /// <summary>Draw into this, then call Commit().</summary>
    public BlueprintMeshBuilder Mesh => mesh;

    /// <summary>Empty the drawing (Commit() to show it).</summary>
    public void ClearDrawing()
    {
        mesh.Clear();
    }

    /// <summary>Show what was drawn.</summary>
    public void Commit()
    {
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (mesh.Vertices.Count > 0 && mesh.Indices.Count > 0)
            vh.AddUIVertexStream(mesh.Vertices, mesh.Indices);
    }
}
