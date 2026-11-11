using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Building blocks for the status screens built at runtime (the promotion memo and its ledger pictures).
/// Positions follow the design's CSS: x and y measured from the parent's top-left corner, y pointing down.
/// </summary>
public static class StatusUI
{
    public static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        return rect;
    }

    /// <summary>Put <paramref name="rect"/> at (x, y) from its parent's top-left corner (y down), with this size.</summary>
    public static RectTransform Place(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
        return rect;
    }

    /// <summary>Like Place, turned around its centre. Positive degrees turn counter-clockwise (CSS rotate(-deg)).</summary>
    public static RectTransform PlaceRotated(RectTransform rect, float x, float y, float width, float height, float degrees)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(x + width * 0.5f, -(y + height * 0.5f));
        rect.sizeDelta = new Vector2(width, height);
        rect.localEulerAngles = new Vector3(0f, 0f, degrees);
        return rect;
    }

    public static void Stretch(RectTransform rect, float left = 0f, float right = 0f, float top = 0f, float bottom = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    /// <summary>A plain coloured rectangle that doesn't take clicks.</summary>
    public static Image Block(RectTransform parent, string name, Color color)
    {
        Image image = NewRect(name, parent).gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    /// <summary>A rounded box that doesn't take clicks (set raycastTarget for a button face).</summary>
    public static StatusBox Box(RectTransform parent, string name, Color fill, Color border, float borderWidth, Vector4 radii)
    {
        StatusBox box = NewRect(name, parent).gameObject.AddComponent<StatusBox>();
        box.SetStyle(fill, border, borderWidth);
        box.Radii = radii;
        box.raycastTarget = false;
        return box;
    }

    /// <summary>A four-pointed sparkle <paramref name="size"/> wide, centred in its parent.</summary>
    public static StatusStar Star(RectTransform parent, string name, Color color, float size)
    {
        RectTransform rect = NewRect(name, parent);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(size, size);
        StatusStar star = rect.gameObject.AddComponent<StatusStar>();
        star.color = color;
        star.raycastTarget = false;
        return star;
    }

    /// <summary>
    /// A stroked polyline (round caps and joins) filling <paramref name="parent"/>, with points in the parent's
    /// drawing space: from its top-left corner, y down (like SVG). Add more paths with AddPath(Points(...)).
    /// </summary>
    public static UIPolyline Stroke(RectTransform parent, string name, Color color, float width, params Vector2[] points)
    {
        RectTransform rect = NewRect(name, parent);
        Stretch(rect);
        rect.pivot = new Vector2(0f, 1f);   // local (0, 0) = the top-left corner
        UIPolyline line = rect.gameObject.AddComponent<UIPolyline>();
        line.color = color;
        line.width = width;
        line.raycastTarget = false;
        line.AddPath(Points(points));
        return line;
    }

    /// <summary>Drawing-space points (y down) to a polyline's local points (y up).</summary>
    public static Vector2[] Points(params Vector2[] drawing)
    {
        var local = new Vector2[drawing.Length];
        for (int i = 0; i < drawing.Length; i++)
            local[i] = new Vector2(drawing[i].x, -drawing[i].y);
        return local;
    }

    /// <summary>Multiply drawing-space points (e.g. a 24-unit icon drawn at another size).</summary>
    public static Vector2[] Scaled(float scale, params Vector2[] points)
    {
        var scaled = new Vector2[points.Length];
        for (int i = 0; i < points.Length; i++)
            scaled[i] = points[i] * scale;
        return scaled;
    }

    public static TextMeshProUGUI Text(RectTransform parent, string name, string text, float size, Color color, TMP_FontAsset font,
                                       TextAlignmentOptions alignment = TextAlignmentOptions.Left, bool bold = false)
    {
        TextMeshProUGUI label = NewRect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) label.font = font;
        label.fontSize = size; // an explicit size also stops TMP from applying its defaults on Awake
        label.text = text;
        label.color = color;
        label.alignment = alignment;
        label.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Overflow;
        label.richText = true;
        label.raycastTarget = false;
        return label;
    }

    /// <summary>Shrink long text (down to <paramref name="min"/>) to fit its width.</summary>
    public static TextMeshProUGUI AutoSize(TextMeshProUGUI text, float min, float max)
    {
        text.enableAutoSizing = true;
        text.fontSizeMin = min;
        text.fontSizeMax = max;
        return text;
    }

    /// <summary>
    /// The design's brass paper clip (SVG "M16 40V10a5 5 0 0 0-10 0v36a3.5 3.5 0 0 0 7 0V14" in a 24 x 60 box),
    /// as drawing-space points multiplied by <paramref name="scale"/>.
    /// </summary>
    public static Vector2[] PaperClip(float scale)
    {
        var points = new List<Vector2> { new Vector2(16f, 40f), new Vector2(16f, 10f) };
        AddArc(points, new Vector2(11f, 10f), 5f, 0f, -180f, 10);    // over the top
        points.Add(new Vector2(6f, 46f));
        AddArc(points, new Vector2(9.5f, 46f), 3.5f, 180f, 0f, 8);   // round the bottom
        points.Add(new Vector2(13f, 14f));
        for (int i = 0; i < points.Count; i++)
            points[i] *= scale;
        return points.ToArray();
    }

    /// <summary>Points along a circular arc in drawing space (y down, so -90 degrees is straight up).</summary>
    public static void AddArc(List<Vector2> into, Vector2 center, float radius, float fromDegrees, float toDegrees, int steps)
    {
        for (int i = 0; i <= steps; i++)
        {
            float a = Mathf.Lerp(fromDegrees, toDegrees, i / (float)steps) * Mathf.Deg2Rad;
            into.Add(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
        }
    }
}
