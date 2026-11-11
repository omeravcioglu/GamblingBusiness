using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Makes layout groups size a TMP text like a browser does with CSS line-height: every line takes
/// lineHeight x font size, and the font's ascent + descent sits centred in that box (glyphs may stick out a
/// little when the line height is smaller than the font, like "line-height: 1.15" on a tall font).
/// The text itself keeps TMP's rendering; keep its vertical alignment on Middle.
/// </summary>
[RequireComponent(typeof(TMP_Text))]
[DisallowMultipleComponent]
public class CssLineHeight : MonoBehaviour, ILayoutElement
{
    [Tooltip("CSS line-height as a multiple of the font size")]
    public float lineHeight = 1.3f;

    TMP_Text text;

    TMP_Text Text
    {
        get
        {
            if (text == null) text = GetComponent<TMP_Text>();
            return text;
        }
    }

    /// <summary>Ascent + descent of the text's font in em.</summary>
    public static float ContentEm(TMP_Text t)
    {
        var font = t != null ? t.font : null;
        if (font == null || font.faceInfo.pointSize <= 0) return 1.2f;
        return (font.faceInfo.ascentLine - font.faceInfo.descentLine) / font.faceInfo.pointSize;
    }

    /// <summary>CSS height of the text when wrapped at the given width.</summary>
    public static float Height(TMP_Text t, float lineHeight, float width)
    {
        float tmpHeight = t.GetPreferredValues(t.text, width, 0f).y;
        return Mathf.Max(0f, tmpHeight - (ContentEm(t) - lineHeight) * t.fontSize);
    }

    public float preferredHeight
    {
        get
        {
            var t = Text;
            if (t == null) return -1f;
            // TMP's own preferred height is (lines - 1) x line pitch + ascent + descent
            return Mathf.Max(0f, t.preferredHeight - (ContentEm(t) - lineHeight) * t.fontSize);
        }
    }

    public float minHeight => preferredHeight;
    public float flexibleHeight => -1f;
    public float minWidth => -1f;
    public float preferredWidth => -1f;
    public float flexibleWidth => -1f;
    public int layoutPriority => 2;

    public void CalculateLayoutInputHorizontal() { }
    public void CalculateLayoutInputVertical() { }

    void OnEnable() => SetDirty();
    void OnDisable() => SetDirty();
    void OnValidate() => SetDirty();

    void SetDirty()
    {
        if (transform is RectTransform rt)
            LayoutRebuilder.MarkLayoutForRebuild(rt);
    }
}
