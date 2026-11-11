using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The design's pill toggle: a 72 x 38 track (money green when on, paper-shade when off) with a 26 px knob
/// that sits right when on and left when off. Drives the looks of a Toggle (whose own graphic is unused).
/// </summary>
[RequireComponent(typeof(Toggle))]
[DisallowMultipleComponent]
public class PillToggle : MonoBehaviour
{
    public Image track;
    public RectTransform knob;
    public Color onColor = new Color32(0x1E, 0x7B, 0x45, 0xFF);
    public Color offColor = new Color32(0xEA, 0xE2, 0xCF, 0xFF);
    public float knobOnX = 20f;
    public float knobOffX = -20f;

    Toggle toggle;

    void Awake()
    {
        toggle = GetComponent<Toggle>();
        toggle.onValueChanged.AddListener(Apply);
    }

    void OnEnable()
    {
        if (toggle == null) toggle = GetComponent<Toggle>();
        Apply(toggle.isOn);
    }

    void LateUpdate()
    {
        // SetIsOnWithoutNotify doesn't raise onValueChanged: keep the looks in step anyway
        if (knob != null && Mathf.Abs(knob.anchoredPosition.x - (toggle.isOn ? knobOnX : knobOffX)) > 0.01f)
            Apply(toggle.isOn);
    }

    public void Apply(bool on)
    {
        if (track != null) track.color = on ? onColor : offColor;
        if (knob != null)
        {
            Vector2 p = knob.anchoredPosition;
            p.x = on ? knobOnX : knobOffX;
            knob.anchoredPosition = p;
        }
    }
}
