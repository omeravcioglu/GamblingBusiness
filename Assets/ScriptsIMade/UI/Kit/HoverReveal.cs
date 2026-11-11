using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Shows something while the pointer is over this object: a GameObject (e.g. a band behind a list item's label)
/// and/or a Behaviour such as a TextHighlighter.
/// </summary>
[DisallowMultipleComponent]
public class HoverReveal : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public GameObject target;
    public Behaviour targetBehaviour;
    [Tooltip("Keep it visible all the time (e.g. the item that is already marked)")]
    public bool alwaysShown;

    void OnEnable() => Set(alwaysShown);

    public void OnPointerEnter(PointerEventData eventData) => Set(true);

    public void OnPointerExit(PointerEventData eventData) => Set(alwaysShown);

    void Set(bool shown)
    {
        if (target != null && target.activeSelf != shown) target.SetActive(shown);
        if (targetBehaviour != null && targetBehaviour.enabled != shown) targetBehaviour.enabled = shown;
    }
}
