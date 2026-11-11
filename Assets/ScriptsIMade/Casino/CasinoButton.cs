using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The looks of a casino button (design boards Casino*): Gold (the casino's action), Paper (secondary), Money (collect)
/// and Highlight (deal again), each with an optional highlighter ring (5 px yellow, 3 px ink: the suggested move or the
/// picked option), and the boards' Off look whenever the button can't be pressed: paper shade, dashed pencil border,
/// pencil text, no shadow. MarkerButton (on the same object) lifts and presses the face. SetLabel changes the label and,
/// for a button that fits its label (maxWidth above 0), its width. A label changed another way (an L10nText after a
/// language change, a new font) is fitted again on the next LateUpdate.
/// </summary>
[DisallowMultipleComponent]
public class CasinoButton : MonoBehaviour
{
    public enum Look
    {
        Gold,
        Paper,
        Money,
        Highlight
    }

    public Button button;
    [Tooltip("Fill, border and ring (moves on hover and press)")]
    public BlueprintBox face;
    [Tooltip("The hard shadow behind the face")]
    public BlueprintBox shadow;
    [Tooltip("Texts and icons: ink (paper on Money), pencil while off")]
    public Graphic[] content;
    public Look look = Look.Gold;
    [Tooltip("Highlighter ring around the face")]
    public bool ring;
    [Tooltip("With the ring on, keep a shadow (spread around the ring); off = no shadow while ringed")]
    public bool ringShadow = true;
    public Color shadowColor = BlueprintUI.FeltShadow;
    [Tooltip("Shadow offset in px, y down")]
    public Vector2 shadowOffset = new Vector2(5f, 6f);
    public float borderWidth = 3f;

    [Header("Label")]
    public TMP_Text label;
    [Tooltip("Width the label may take (its LayoutElement); the text shrinks to fit it")]
    public LayoutElement labelLayout;
    public float padding = 36f;
    [Tooltip("Icon width plus the gap to the label (0 = no icon)")]
    public float iconSpace;
    [Tooltip("A button that fits its label: its width stays between these (Max 0 = the width never changes)")]
    public float minWidth;
    public float maxWidth;

    bool applied;
    bool lastOn;
    Look lastLook;
    bool lastRing;
    string fittedText;
    TMP_FontAsset fittedFont;

    public bool Interactable
    {
        get => button != null && button.interactable;
        set { if (button != null && button.interactable != value) button.interactable = value; }
    }

    /// <summary>Change the look (and the ring) from code.</summary>
    public void SetLook(Look value, bool withRing)
    {
        if (look == value && ring == withRing && applied) return;
        look = value;
        ring = withRing;
        Apply(button == null || button.IsInteractable());
    }

    /// <summary>New label text; a button that fits its label also gets its new width.</summary>
    public void SetLabel(string text)
    {
        if (label == null || label.text == text && applied) return;
        label.text = text;
        FitLabel();
    }

    /// <summary>Size the label (and a fitting button) for the current text.</summary>
    public void FitLabel()
    {
        if (label == null) return;
        fittedText = label.text;
        fittedFont = label.font;
        RectTransform rect = (RectTransform)transform;
        float size = label.enableAutoSizing ? label.fontSizeMax : label.fontSize;
        float textWidth = Mathf.Ceil(RunEndUI.TextWidth(label.font, label.text, size, (label.fontStyle & FontStyles.Bold) != 0)) + 4f;
        float width = rect.rect.width > 0f ? rect.rect.width : rect.sizeDelta.x;
        if (maxWidth > 0f)
        {
            width = Mathf.Clamp(textWidth + iconSpace + padding * 2f, Mathf.Min(minWidth, maxWidth), maxWidth);
            LayoutElement own = GetComponent<LayoutElement>();
            if (own != null) own.preferredWidth = width;
            else rect.sizeDelta = new Vector2(width, rect.sizeDelta.y);
        }
        if (labelLayout != null)
            labelLayout.preferredWidth = Mathf.Max(8f, Mathf.Min(textWidth, width - padding * 2f - iconSpace));
    }

    void OnEnable()
    {
        applied = false;
    }

    void LateUpdate()
    {
        // The label changed behind SetLabel's back (a language change: new words, another font)
        if (label != null && labelLayout != null && (label.text != fittedText || label.font != fittedFont)) FitLabel();
        bool on = button == null || button.IsInteractable();
        if (applied && on == lastOn && look == lastLook && ring == lastRing) return;
        Apply(on);
    }

    void Apply(bool on)
    {
        applied = true;
        lastOn = on;
        lastLook = look;
        lastRing = ring;

        Color fill, ink;
        switch (look)
        {
            case Look.Paper: fill = UIColors.PaperHi; ink = UIColors.Ink; break;
            case Look.Money: fill = UIColors.Money; ink = UIColors.PaperHi; break;
            case Look.Highlight: fill = UIColors.Highlight; ink = UIColors.Ink; break;
            default: fill = UIColors.Gold; ink = UIColors.Ink; break;
        }
        if (!on)
        {
            fill = UIColors.PaperShade;
            ink = UIColors.Pencil;
        }

        bool withRing = on && ring;
        if (face != null)
        {
            face.SetStyle(fill, on ? UIColors.Ink : UIColors.Pencil, borderWidth);
            face.SetDash(!on, 9f, 6f);
            face.SetRings(withRing ? UIColors.Highlight : Color.clear, withRing ? 5f : 0f, withRing ? UIColors.Ink : Color.clear, withRing ? 3f : 0f);
        }

        if (shadow != null)
        {
            bool show = on && (!ring || ringShadow);
            if (shadow.gameObject.activeSelf != show) shadow.gameObject.SetActive(show);
            shadow.SetStyle(shadowColor, Color.clear, 0f);
            // CSS "5px 6px 0 8px": with the ring the shadow spreads 8 px around it
            float spread = withRing ? 8f : 0f;
            RectTransform rect = shadow.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(shadowOffset.x - spread, -shadowOffset.y - spread);
            rect.offsetMax = new Vector2(shadowOffset.x + spread, -shadowOffset.y + spread);
            Vector4 radii = face != null ? face.Radii : BlueprintUI.RadiusLarge;
            shadow.Radii = radii + Vector4.one * spread;
        }

        if (content != null)
        {
            foreach (Graphic graphic in content)
                if (graphic != null) graphic.color = ink;
        }
    }
}
