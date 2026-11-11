using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Lays children out left to right at their preferred size and wraps to a new row when the next one
/// doesn't fit (like CSS "display: flex; flex-wrap: wrap"). Used for chips and tags whose text length varies.
/// </summary>
[AddComponentMenu("Layout/Flow Layout Group")]
public class FlowLayoutGroup : LayoutGroup
{
    public float spacingX = 10f;
    public float spacingY = 8f;

    readonly List<float> rowHeights = new List<float>();

    public override void CalculateLayoutInputHorizontal()
    {
        base.CalculateLayoutInputHorizontal();
        float min = 0f, pref = padding.horizontal;
        for (int i = 0; i < rectChildren.Count; i++)
        {
            var child = rectChildren[i];
            min = Mathf.Max(min, LayoutUtility.GetMinWidth(child));
            pref += LayoutUtility.GetPreferredWidth(child) + (i > 0 ? spacingX : 0f);
        }
        SetLayoutInputForAxis(min + padding.horizontal, pref, -1f, 0);
    }

    public override void CalculateLayoutInputVertical()
    {
        float height = Arrange(false);
        SetLayoutInputForAxis(height, height, -1f, 1);
    }

    public override void SetLayoutHorizontal()
    {
        Arrange(true);
    }

    public override void SetLayoutVertical()
    {
        Arrange(true);
    }

    // Place (or just measure) the children; returns the total height
    float Arrange(bool apply)
    {
        float width = rectTransform.rect.width - padding.horizontal;
        rowHeights.Clear();

        // pass 1: row heights
        float x = 0f, rowHeight = 0f;
        for (int i = 0; i < rectChildren.Count; i++)
        {
            var child = rectChildren[i];
            float w = Mathf.Min(LayoutUtility.GetPreferredWidth(child), width);
            float h = LayoutUtility.GetPreferredHeight(child);
            if (x > 0f && x + w > width + 0.01f)
            {
                rowHeights.Add(rowHeight);
                x = 0f;
                rowHeight = 0f;
            }
            x += w + spacingX;
            rowHeight = Mathf.Max(rowHeight, h);
        }
        if (rectChildren.Count > 0)
            rowHeights.Add(rowHeight);

        float total = padding.vertical;
        for (int r = 0; r < rowHeights.Count; r++)
            total += rowHeights[r] + (r > 0 ? spacingY : 0f);

        if (!apply) return total;

        // pass 2: positions
        x = 0f;
        float y = padding.top;
        int row = 0;
        for (int i = 0; i < rectChildren.Count; i++)
        {
            var child = rectChildren[i];
            float w = Mathf.Min(LayoutUtility.GetPreferredWidth(child), width);
            float h = LayoutUtility.GetPreferredHeight(child);
            if (x > 0f && x + w > width + 0.01f)
            {
                y += rowHeights[row] + spacingY;
                row++;
                x = 0f;
            }
            float rh = row < rowHeights.Count ? rowHeights[row] : h;
            SetChildAlongAxis(child, 0, padding.left + x, w);
            SetChildAlongAxis(child, 1, y + (rh - h) * 0.5f, h);
            x += w + spacingX;
        }
        return total;
    }
}
