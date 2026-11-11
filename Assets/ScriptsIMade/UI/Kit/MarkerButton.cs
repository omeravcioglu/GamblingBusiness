using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The design's marker-pen button feel. The hard shadow stays put; the face lifts 2 px up-left on hover
/// and presses 4 px right / 5 px down into the shadow. A non-interactable button switches to the
/// "disabled" look: paper-shade fill, dashed pencil border, no shadow, pencil-coloured content.
/// Put it next to a Button whose transition is None (this component draws all the states).
/// </summary>
[DisallowMultipleComponent]
public class MarkerButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    [Tooltip("Everything that moves (fill, border, content). The shadow must not be inside it.")]
    public RectTransform face;
    [Tooltip("Hard offset shadow drawn behind the face (hidden while disabled)")]
    public GameObject shadow;

    public Vector2 hoverOffset = new Vector2(-2f, 2f);
    public Vector2 pressOffset = new Vector2(4f, -5f);

    [Header("Disabled look (optional)")]
    public Image fill;
    public Color disabledFillColor = new Color32(0xEA, 0xE2, 0xCF, 0xFF);
    public GameObject solidBorder;
    public GameObject dashedBorder;
    [Tooltip("Texts and icons that turn pencil grey while disabled")]
    public Graphic[] content;
    public Color disabledContentColor = new Color32(0x67, 0x6B, 0x74, 0xFF);
    [Tooltip("Shown only while disabled (e.g. a lock badge)")]
    public GameObject disabledOnly;

    Selectable selectable;
    Vector2 faceHome;
    bool homeKnown;
    bool hovered, pressed;
    bool lastInteractable = true;
    bool lookApplied;
    Color fillColor;
    Color[] contentColors;

    void Awake()
    {
        selectable = GetComponent<Selectable>();
        CaptureColors();
        if (face != null)
        {
            faceHome = face.anchoredPosition;
            homeKnown = true;
        }
    }

    void OnEnable()
    {
        hovered = pressed = false;
        lookApplied = false;
        ApplyOffset();
    }

    void CaptureColors()
    {
        if (fill != null)
            fillColor = fill.color;
        if (content != null)
        {
            contentColors = new Color[content.Length];
            for (int i = 0; i < content.Length; i++)
                contentColors[i] = content[i] != null ? content[i].color : Color.white;
        }
    }

    bool ShowingDisabled => lookApplied && !lastInteractable;

    /// <summary>Change the normal (enabled) fill colour from code.</summary>
    public void SetFillColor(Color color)
    {
        fillColor = color;
        if (fill != null && !ShowingDisabled)
            fill.color = color;
    }

    /// <summary>Change the normal (enabled) colour of one content graphic from code.</summary>
    public void SetContentColor(int index, Color color)
    {
        if (content == null || index < 0 || index >= content.Length) return;
        if (contentColors != null && index < contentColors.Length)
            contentColors[index] = color;
        if (content[index] != null && !ShowingDisabled)
            content[index].color = color;
    }

    bool Interactable => selectable == null || selectable.IsInteractable();

    void LateUpdate()
    {
        bool on = Interactable;
        if (!lookApplied || on != lastInteractable)
        {
            lastInteractable = on;
            ApplyLook(on);
            lookApplied = true;
            if (!on)
            {
                hovered = pressed = false;
                ApplyOffset();
            }
        }
    }

    void ApplyLook(bool on)
    {
        if (shadow != null && shadow.activeSelf != on)
            shadow.SetActive(on);
        if (fill != null)
            fill.color = on ? fillColor : disabledFillColor;
        if (dashedBorder != null && dashedBorder.activeSelf == on)
            dashedBorder.SetActive(!on);
        if (solidBorder != null && solidBorder.activeSelf != on && dashedBorder != null)
            solidBorder.SetActive(on);
        if (disabledOnly != null && disabledOnly.activeSelf == on)
            disabledOnly.SetActive(!on);
        if (content != null && contentColors != null)
        {
            for (int i = 0; i < content.Length; i++)
                if (content[i] != null)
                    content[i].color = on ? contentColors[i] : disabledContentColor;
        }
    }

    void ApplyOffset()
    {
        if (face == null || !homeKnown) return;
        Vector2 offset = Vector2.zero;
        if (Interactable)
        {
            if (pressed) offset = pressOffset;
            else if (hovered) offset = hoverOffset;
        }
        face.anchoredPosition = faceHome + offset;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        hovered = true;
        ApplyOffset();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        hovered = false;
        pressed = false;
        ApplyOffset();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        pressed = true;
        ApplyOffset();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        pressed = false;
        ApplyOffset();
    }
}
