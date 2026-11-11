using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Root of the designed office-upgrade flow (canvas boards "Upgrade: choose how", "Blackjack", "Upgrade result"
/// and "Hover tag"). The pages share the item icons (found by the item's English name) and the wording helpers kept
/// here (Panels table).
/// </summary>
[DisallowMultipleComponent]
public class UpgradeFlowView : MonoBehaviour
{
    [Serializable]
    public struct ItemIcon
    {
        [Tooltip("Lower-case words separated by spaces; the first one found in the item's name picks this icon")]
        public string keywords;
        public Sprite icon;
    }

    public ItemIcon[] itemIcons;
    public Sprite fallbackIcon;
    [Tooltip("Stake chip icon when the blackjack hand isn't an office upgrade (e.g. the build budget)")]
    public Sprite stakeIcon;

    static readonly char[] Space = { ' ' };

    /// <summary>String Table of the flow's texts (Assets/Localization/Source/Panels.csv).</summary>
    const string L10nTable = "Panels";

    public Sprite IconFor(string itemName)
    {
        string n = (itemName ?? "").ToLowerInvariant();
        if (itemIcons != null)
        {
            foreach (var entry in itemIcons)
            {
                if (entry.icon == null || string.IsNullOrEmpty(entry.keywords)) continue;
                foreach (string word in entry.keywords.Split(Space, StringSplitOptions.RemoveEmptyEntries))
                    if (n.Contains(word)) return entry.icon;
            }
        }
        return fallbackIcon;
    }

    public static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>"42%" ("%42" in Turkish)</summary>
    public static string Percent(int value) => L10n.Get(L10nTable, "upgrade.percent", ("percent", Int(value)));

    /// <summary>"+$400/day"</summary>
    public static string Daily(OfficeItem d, int level) =>
        L10n.Get(L10nTable, "upgrade.stat.daily", ("amount", UIFormat.Money(d.dailyBonusPerLevel * Mathf.Max(0, level))));

    /// <summary>"+8% productivity"</summary>
    public static string Productivity(OfficeItem d, int level) =>
        L10n.Get(L10nTable, "upgrade.stat.productivity", ("percent", Int(Mathf.RoundToInt(d.productivityPerLevel * Mathf.Max(0, level) * 100f))));

    /// <summary>"Lvl 4/5"</summary>
    public static string LevelOf(int level, int max) => L10n.Get(L10nTable, "upgrade.stat.level", ("level", Int(level)), ("max", Int(max)));

    /// <summary>"Good (Lvl 3)": the level's name (OfficeItem.GetLevelName, translated) and number</summary>
    public static string NamedLevel(OfficeItem d, int level) =>
        L10n.Get(L10nTable, "upgrade.stat.level.named", ("level", d.GetLevelName(level)), ("number", Int(level)));

    /// <summary>"Lvl 4/5 · +$400/day · +8% productivity"</summary>
    public static string LevelLine(OfficeItem d) =>
        LevelOf(d.currentLevel, d.maxLevel) + " · " + Daily(d, d.currentLevel) + " · " + Productivity(d, d.currentLevel);

    /// <summary>Level pips: filled up to the level, one pip per level up to the pips there are.</summary>
    public static void SetPips(Image[] pips, int level, int max)
    {
        if (pips == null) return;
        for (int i = 0; i < pips.Length; i++)
        {
            if (pips[i] == null) continue;
            var go = pips[i].transform.parent != null && pips[i].transform.parent.name.StartsWith("Pip") ? pips[i].transform.parent.gameObject : pips[i].gameObject;
            bool shown = i < Mathf.Max(1, max);
            if (go.activeSelf != shown) go.SetActive(shown);
            pips[i].color = i < level ? UIColors.Ink : UIColors.PaperHi;
        }
    }

    public static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);
}
