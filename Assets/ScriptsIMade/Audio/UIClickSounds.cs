using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The click of every uGUI button, without touching any button: when the left mouse button goes up over the same
/// interactable Selectable it went down on, ui_click plays (ui_toggle for a Toggle). Dragging a Slider ticks (ui_tick,
/// every 5% of its range, or every step of a whole-number slider). A press that turned into a scroll or a text drag
/// doesn't click, and neither do sliders and scrollbars when released.
/// - The sounds are GameSfx's lowest level, decided at the end of the next frame: a button that plays its own sound, even
///   through a screen that reacts a frame later, or posts a toast, isn't heard clicking as well.
/// - Costs nothing while the mouse isn't pressed. GameSfx adds it to its own object. UI Toolkit screens aren't covered.
/// </summary>
[DefaultExecutionOrder(-1000)]   // before the EventSystem: the pointer still finds what was clicked (the click may hide it)
[DisallowMultipleComponent]
public class UIClickSounds : MonoBehaviour
{
    const float SliderTickStep = 0.05f;   // a continuous slider ticks every 5% of its range

    static readonly List<RaycastResult> hits = new List<RaycastResult>();

    PointerEventData pointer;
    EventSystem pointerSystem;
    Selectable pressed;
    Vector2 pressedAt;
    Slider dragged;
    float draggedValue;

    void Update()
    {
        if (Input.GetMouseButtonDown(0)) Press();
        if (Input.GetMouseButtonUp(0)) Release();
    }

    void LateUpdate()
    {
        // The EventSystem has moved the slider this frame by now
        if (dragged == null) return;
        if (!Input.GetMouseButton(0) || !dragged.isActiveAndEnabled || !dragged.IsInteractable())
        {
            dragged = null;
            return;
        }
        float step = dragged.wholeNumbers ? 1f : Mathf.Abs(dragged.maxValue - dragged.minValue) * SliderTickStep;
        if (step <= 0f || Mathf.Abs(dragged.value - draggedValue) < step * 0.999f) return;
        draggedValue = dragged.value;
        GameSfx.PlayFeedback(Sfx.UiTick);
    }

    void Press()
    {
        pressed = SelectableUnderPointer();
        pressedAt = Input.mousePosition;
        dragged = pressed as Slider;
        if (dragged != null && !dragged.IsInteractable()) dragged = null;
        if (dragged != null) draggedValue = dragged.value;
    }

    void Release()
    {
        Selectable down = pressed;
        pressed = null;
        if (down == null || !down.IsInteractable() || down is Slider || down is Scrollbar) return;
        if (SelectableUnderPointer() != down) return;   // let go somewhere else: the EventSystem doesn't click either

        // Moved past the drag threshold under something draggable (a scroll view, text selection): no click
        EventSystem system = EventSystem.current;
        float threshold = system != null ? system.pixelDragThreshold : 10f;
        if (((Vector2)Input.mousePosition - pressedAt).sqrMagnitude > threshold * threshold
            && down.GetComponentInParent<IDragHandler>() != null)
            return;

        GameSfx.PlayFeedback(down is Toggle ? Sfx.UiToggle : Sfx.UiClick);
    }

    /// <summary>The Selectable of the top-most UI object under the mouse (what the EventSystem would click), or null.</summary>
    Selectable SelectableUnderPointer()
    {
        EventSystem system = EventSystem.current;
        if (system == null) return null;
        if (pointer == null || pointerSystem != system)
        {
            pointer = new PointerEventData(system);
            pointerSystem = system;
        }
        pointer.position = Input.mousePosition;

        hits.Clear();
        system.RaycastAll(pointer, hits);
        GameObject top = null;
        foreach (RaycastResult hit in hits)
        {
            if (hit.gameObject == null) continue;
            top = hit.gameObject;
            break;
        }
        hits.Clear();
        return top != null ? top.GetComponentInParent<Selectable>() : null;
    }
}
