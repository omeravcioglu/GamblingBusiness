using TMPro;
using UnityEngine;

/// <summary>
/// The HUD's tooltip slip (paper-hi, 2 px ink border, pointer on top): "<b>Stocks</b> — unlock with ...".
/// One instance per HUD; tabs call Show/Hide. It never blocks clicks. The title and hint arrive in the current
/// language; the way they're joined is the Hud String Table's hud.tooltip.titled.
/// </summary>
[DisallowMultipleComponent]
public class HudTooltip : MonoBehaviour
{
    const string T = "Hud";   // Assets/Localization/Source/Hud.csv

    public RectTransform slip;
    public TMP_Text label;
    [Tooltip("The small rotated square that points at the hovered element")]
    public RectTransform pointer;
    public float maxWidth = 340f;
    public Vector2 padding = new Vector2(16f, 12f);
    [Tooltip("Gap between the hovered element's bottom edge and the slip")]
    public float gap = 22f;

    object owner;
    L10nFont labelFont;

    void Awake()
    {
        labelFont = L10nFont.Attach(label);
        if (slip != null)
            slip.gameObject.SetActive(false);
    }

    public void Show(object who, RectTransform target, string title, string text)
    {
        if (slip == null || label == null || target == null) return;
        owner = who;

        if (labelFont != null) labelFont.Apply();   // measured below in the language's font, while the slip is still hidden
        label.text = string.IsNullOrEmpty(title) ? text : L10n.Get(T, "hud.tooltip.titled", ("title", "<b>" + title + "</b>"), ("text", text));
        float innerMax = maxWidth - padding.x * 2f;
        Vector2 pref = label.GetPreferredValues(label.text, innerMax, 0f);
        float width = Mathf.Min(maxWidth, Mathf.Ceil(pref.x) + padding.x * 2f + 2f);
        var css = label.GetComponent<CssLineHeight>();
        float textHeight = css != null ? CssLineHeight.Height(label, css.lineHeight, width - padding.x * 2f)
                                       : label.GetPreferredValues(label.text, width - padding.x * 2f, 0f).y;
        float height = Mathf.Ceil(textHeight) + padding.y * 2f;
        slip.sizeDelta = new Vector2(width, height);

        // Below the target, left edge a little left of the target's centre, kept on screen
        RectTransform parent = slip.parent as RectTransform;
        Vector3[] corners = new Vector3[4];
        target.GetWorldCorners(corners);
        Vector3 bottomCenterWorld = (corners[0] + corners[3]) * 0.5f;
        Vector2 local = parent.InverseTransformPoint(bottomCenterWorld);
        Rect bounds = parent.rect;
        float x = local.x - 80f;
        x = Mathf.Clamp(x, bounds.xMin + 16f, bounds.xMax - width - 16f);
        float y = local.y - gap;
        slip.pivot = new Vector2(0f, 1f);
        slip.anchorMin = slip.anchorMax = new Vector2(0.5f, 0.5f);
        slip.anchoredPosition = new Vector2(x, y) - (Vector2)bounds.center;
        if (pointer != null)
            pointer.anchoredPosition = new Vector2(Mathf.Clamp(local.x - x, 18f, width - 18f), pointer.anchoredPosition.y);

        slip.gameObject.SetActive(true);
        slip.SetAsLastSibling();
    }

    public void Hide(object who)
    {
        if (who != null && who != owner) return;
        owner = null;
        if (slip != null)
            slip.gameObject.SetActive(false);
    }
}
