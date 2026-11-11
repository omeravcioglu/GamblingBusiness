using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A furniture hotspot in the room view: a ring around the drawing (dashed; highlighter yellow when picked) and a tag
/// under the floor with the slot's name and five level pips. TowerView clones a template per slot.
/// </summary>
public class TowerSlotSpot : MonoBehaviour
{
    public Button button;
    public BlueprintBox ring;
    public RectTransform tagRect;
    public BlueprintBox tagBox;
    public TextMeshProUGUI nameText;
    public BlueprintBox[] pips = new BlueprintBox[5];

    /// <summary>The slot this hotspot is for.</summary>
    [NonSerialized] public int slot = -1;

    int shownKey;

    public void Bind(int slotIndex, string slotName, int level, int maxLevel, bool selected)
    {
        unchecked
        {
            int key = ((slotIndex * 31 + (slotName != null ? slotName.GetHashCode() : 0)) * 31 + level) * 31 + maxLevel * 2 + (selected ? 1 : 0);
            if (key == shownKey && slot == slotIndex) return;
            shownKey = key;
        }
        slot = slotIndex;
        if (nameText != null) nameText.text = slotName;
        if (ring != null)
        {
            ring.SetStyle(Color.clear, selected ? UIColors.Highlight : UIColors.WithAlpha(UIColors.Ink, 0.4f), selected ? 4f : 2f);
            ring.Dashed = !selected;
            ring.SetOutline(selected ? UIColors.Ink : Color.clear, 2f, 0f);
        }
        if (tagBox != null) tagBox.Fill = selected ? UIColors.Highlight : UIColors.PaperHi;
        for (int i = 0; i < pips.Length; i++)
        {
            if (pips[i] == null) continue;
            pips[i].Fill = i < level ? UIColors.Ink : UIColors.PaperHi;
            pips[i].gameObject.SetActive(i < maxLevel);
        }
    }
}
