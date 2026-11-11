using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// On a HUD tab that FeatureGate locks (added at runtime by FeatureTabs): clicking the locked tab says when it opens (a
/// toast; hovering already shows the same hint in the tab's tooltip). A locked tab's Button is not interactable, but the
/// click still reaches the other click handlers on its object, so this needs no change to the HUD's code. Open tabs are
/// left to OfficeHud.
/// </summary>
[DisallowMultipleComponent]
public class FeatureTabClick : MonoBehaviour, IPointerClickHandler
{
    public Feature feature;
    public HudTab tab;

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData != null && eventData.button != PointerEventData.InputButton.Left) return;
        if (tab != null && !tab.IsLocked) return;
        if (FeatureGate.IsUnlocked(feature)) return;
        if (!UIManager.GameplayInputAllowed()) return;
        FeatureGate.ShowLockedToast(feature);
    }
}
