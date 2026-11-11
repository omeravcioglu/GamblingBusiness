using TMPro;
using UnityEngine;

/// <summary>
/// Keeps a StaffUI paper button (or a chip built the same way) laid out for its label as it reads now: the icon and the
/// label centred together on the face, the label as wide as its text (it shrinks when the face is too narrow), and a
/// button made to fit its content as wide as that content. Laid out again whenever the label's text or font changes, so a
/// language change (a longer word, another font) keeps it tidy. Positions are from the face's top-left corner, y down.
/// </summary>
[DisallowMultipleComponent]
public class StaffButtonFit : MonoBehaviour
{
    [Tooltip("The label (on the face)")]
    public TMP_Text label;
    [Tooltip("The icon left of the label (optional)")]
    public RectTransform icon;
    public float iconSize = 24f;
    [Tooltip("Room between the icon and the label")]
    public float iconGap = 10f;
    [Tooltip("The label's full size (it shrinks from there when it doesn't fit)")]
    public float fontSize = 22f;
    [Tooltip("The button's width follows its content (plus Padding on both sides)")]
    public bool fitWidth;
    public float padding = 24f;
    [Tooltip("Room kept free at each side of a fixed-width face")]
    public float margin = 6f;

    string laidOutText;
    TMP_FontAsset laidOutFont;

    void OnEnable()
    {
        Layout();
    }

    void LateUpdate()
    {
        if (label != null && (label.text != laidOutText || label.font != laidOutFont)) Layout();
    }

    /// <summary>Lay the button out for the label's current text and font now.</summary>
    public void Layout()
    {
        if (label == null) return;
        laidOutText = label.text;
        laidOutFont = label.font;

        var root = (RectTransform)transform;
        float textWidth = Mathf.Ceil(StaffUI.Measure(label.font, label.text, fontSize, (label.fontStyle & FontStyles.Bold) != 0,
                                                     label.characterSpacing / 100f)) + 2f;
        float iconSpace = icon != null ? iconSize + iconGap : 0f;
        Vector2 size = root.sizeDelta;
        if (fitWidth)
        {
            size.x = textWidth + iconSpace + padding * 2f;
            root.sizeDelta = size;
        }

        float contentWidth = Mathf.Max(0f, Mathf.Min(size.x - margin * 2f, textWidth + iconSpace));
        float left = (size.x - contentWidth) * 0.5f;
        if (icon != null) BlueprintUI.TopLeft(icon, left, (size.y - iconSize) * 0.5f, iconSize, iconSize);
        float textHeight = Mathf.Ceil(fontSize * 1.7f);
        BlueprintUI.TopLeft(label.rectTransform, left + iconSpace, (size.y - textHeight) * 0.5f, Mathf.Max(0f, contentWidth - iconSpace), textHeight);
    }
}
