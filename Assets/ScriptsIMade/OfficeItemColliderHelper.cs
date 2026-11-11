using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// Helper component that relays mouse hover events from child colliders back to the parent OfficeItemInteractable.
/// This is automatically added to child objects with colliders. Clicks are handled by the parent itself.
/// </summary>
public class OfficeItemColliderHelper : MonoBehaviour
{
    [HideInInspector]
    public OfficeItemInteractable parentInteractable;

    void OnMouseEnter()
    {
        if (parentInteractable != null)
            parentInteractable.OnChildMouseEnter();
    }

    void OnMouseOver()
    {
        if (parentInteractable != null)
            parentInteractable.OnChildMouseOver();
    }

    void OnMouseExit()
    {
        if (parentInteractable != null)
            parentInteractable.OnChildMouseExit();
    }
}
