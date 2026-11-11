using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A tab of the designed stock market screen, drawn in one of four looks (set every refresh, also in edit-mode previews):
/// - Active: its fill colour and text colour, solid border, hard shadow when it has one, the active height.
/// - Inactive: the inactive fill (may be clear), solid or dashed border (<see cref="dashedWhenInactive"/>), no shadow.
/// - Disabled / Locked: the disabled fill, a dashed border, muted text, no shadow; Locked also shows the lock icon.
/// Heights (list tabs stand on the table's rule: active 40, inactive 34) apply when greater than 0.
/// </summary>
public class StockTabLook : MonoBehaviour
{
    public enum State { Active, Inactive, Disabled, Locked }

    public Button button;
    public Graphic fill;
    public GameObject solidBorder;
    public GameObject dashedBorder;
    public GameObject shadow;
    public TMP_Text label;
    [Tooltip("Other content that takes the text colour (icons)")]
    public Graphic[] tinted = new Graphic[0];
    public GameObject lockIcon;

    public Color activeFill = Color.white;
    public Color activeText = Color.black;
    public Color inactiveFill = Color.white;
    public Color inactiveText = Color.black;
    public bool dashedWhenInactive;
    public Color disabledFill = Color.white;
    public Color disabledText = Color.gray;
    public float activeHeight;
    public float inactiveHeight;

    public State Current { get; private set; }

    public void Apply(State state)
    {
        Current = state;
        bool active = state == State.Active;
        bool off = state == State.Disabled || state == State.Locked;
        Color text = active ? activeText : off ? disabledText : inactiveText;
        if (fill != null) fill.color = active ? activeFill : off ? disabledFill : inactiveFill;
        bool dashed = off || (state == State.Inactive && dashedWhenInactive);
        if (solidBorder != null) solidBorder.SetActive(!dashed);
        if (dashedBorder != null) dashedBorder.SetActive(dashed);
        if (shadow != null) shadow.SetActive(active);
        if (label != null) label.color = text;
        if (tinted != null)
            foreach (var g in tinted)
                if (g != null) g.color = text;
        if (lockIcon != null) lockIcon.SetActive(state == State.Locked);
        float h = active ? activeHeight : inactiveHeight;
        if (h > 0f)
        {
            var rt = (RectTransform)transform;
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, h);
        }
    }
}
