using UnityEngine;

[System.Serializable]
public class OfficeItem
{
    public string itemName;
    public string description;
    public int currentLevel = 0;
    public int maxLevel = 5;

    // Bonuses per level
    public float dailyBonusPerLevel = 100f;      // Extra money per day per level
    public float productivityPerLevel = 0.02f;   // % productivity boost per level

    // Upgrade costs
    public int baseCost = 1000;
    public float costMultiplier = 1.5f;          // Cost increases per level

    public string[] levelNames = { "Basic", "Standard", "Good", "Premium", "Deluxe", "Ultimate" };

    /// <summary>
    /// The item's name as players see it: the Company table's office.item.&lt;slug&gt; ("Sofa" → office.item.sofa), the English
    /// itemName when there is none. itemName itself stays English (the upgrade screens find the item's icon by it).
    /// </summary>
    public string DisplayName => DisplayItemName(itemName);

    /// <summary>An office item's name (English, as typed in the scene) in the current language.</summary>
    public static string DisplayItemName(string english)
    {
        if (string.IsNullOrEmpty(english)) return english ?? "";
        string slug = L10n.Slug(english);
        return slug.Length == 0 ? english : L10n.GetOr(CompanyText.Table, "office.item." + slug, english);
    }

    public int GetUpgradeCost()
    {
        return Mathf.RoundToInt(baseCost * Mathf.Pow(costMultiplier, Mathf.Max(0, currentLevel)));
    }

    public string GetCurrentLevelName()
    {
        return GetLevelName(currentLevel);
    }

    public string GetNextLevelName()
    {
        return GetLevelName(currentLevel + 1);
    }

    /// <summary>
    /// Display name of a level in the current language: levelNames[level] through the Company table (office.level.&lt;slug&gt;:
    /// Basic ... Ultimate; a name the table doesn't have shows as typed), "Level N" (office.level.number) when no name is
    /// set. For display only: saves keep currentLevel.
    /// </summary>
    public string GetLevelName(int level)
    {
        if (levelNames != null && level >= 0 && level < levelNames.Length && !string.IsNullOrEmpty(levelNames[level]))
        {
            string english = levelNames[level];
            string slug = L10n.Slug(english);
            return slug.Length == 0 ? english : L10n.GetOr(CompanyText.Table, "office.level." + slug, english);
        }
        return L10n.GetOr(CompanyText.Table, "office.level.number", "Level {level}",
                          ("level", level.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    public bool CanUpgrade()
    {
        return currentLevel < maxLevel;
    }

    /// <summary>Keep the level between 0 and maxLevel.</summary>
    public void ClampLevel()
    {
        currentLevel = Mathf.Clamp(currentLevel, 0, Mathf.Max(0, maxLevel));
    }

    public float GetTotalDailyBonus()
    {
        return dailyBonusPerLevel * Mathf.Max(0, currentLevel);
    }

    public float GetTotalProductivityBonus()
    {
        return productivityPerLevel * Mathf.Max(0, currentLevel);
    }
}

public enum UpgradeMethod
{
    Blackjack,
    RandomChance
}
