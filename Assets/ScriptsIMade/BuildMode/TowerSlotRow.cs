using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One furniture slot in a list: the Furniture drawer's buttons (icon, name, five level pips) and the room card's rows
/// (name, level name with a bar, income per day). Any of the fields may be empty. TowerView clones a template per slot.
/// </summary>
public class TowerSlotRow : MonoBehaviour
{
    public Button button;
    public BlueprintBox background;
    public TowerIcon icon;
    public TextMeshProUGUI nameText;
    [Tooltip("Level pips 1-5 (filled = reached)")]
    public BlueprintBox[] pips = new BlueprintBox[5];
    public TextMeshProUGUI levelText;
    [Tooltip("Width follows the level (anchorMax.x = level / top level)")]
    public RectTransform barFill;
    public TextMeshProUGUI perDayText;

    /// <summary>The slot this row shows.</summary>
    [NonSerialized] public int slot = -1;

    int shownKey;

    /// <summary>Show a slot, its furniture named as typed (the tower screen names it in the current language: the overload below).</summary>
    public void Bind(int slotIndex, TowerFurnitureSlot data, int level, int maxLevel, string levelName, double perDay, bool selected)
    {
        Bind(slotIndex, data, data != null ? data.name : "", level, maxLevel, levelName, perDay, selected);
    }

    /// <summary>Show a slot; <paramref name="slotName"/> and <paramref name="levelName"/> are the words to show (TowerText).</summary>
    public void Bind(int slotIndex, TowerFurnitureSlot data, string slotName, int level, int maxLevel, string levelName, double perDay, bool selected)
    {
        int key = Key(slotIndex, data, level, maxLevel, levelName, perDay, selected);
        unchecked { key = key * 31 + (slotName != null ? slotName.GetHashCode() : 0); }
        if (key == shownKey) return;
        shownKey = key;
        slot = slotIndex;
        if (data == null) return;

        if (nameText != null) nameText.text = slotName;
        if (icon != null) icon.Kind = TowerArt.IconFor(data.art);
        if (levelText != null) levelText.text = levelName;
        if (perDayText != null) perDayText.text = "+" + UIFormat.Money(perDay);
        for (int i = 0; i < pips.Length; i++)
        {
            if (pips[i] == null) continue;
            bool on = i < level;
            pips[i].Fill = on ? UIColors.Ink : UIColors.PaperHi;
            pips[i].gameObject.SetActive(i < maxLevel);
        }
        if (barFill != null)
        {
            float t = maxLevel > 0 ? Mathf.Clamp01(level / (float)maxLevel) : 0f;
            barFill.anchorMax = new Vector2(t, 1f);
        }
        if (background != null)
        {
            background.SetShadow(selected ? Color.clear : BlueprintUI.InkShadow, new Vector2(3f, 4f));
            background.SetRings(selected ? UIColors.Highlight : Color.clear, selected ? 5f : 0f, selected ? UIColors.Ink : Color.clear, selected ? 2f : 0f);
        }
    }

    static int Key(int slotIndex, TowerFurnitureSlot data, int level, int maxLevel, string levelName, double perDay, bool selected)
    {
        unchecked
        {
            int k = slotIndex + 7;
            k = k * 31 + (data != null ? data.GetHashCode() : 0);
            k = k * 31 + level;
            k = k * 31 + maxLevel;
            k = k * 31 + (levelName != null ? levelName.GetHashCode() : 0);
            k = k * 31 + perDay.GetHashCode();
            k = k * 31 + (selected ? 1 : 0);
            return k == 0 ? 1 : k;
        }
    }
}
