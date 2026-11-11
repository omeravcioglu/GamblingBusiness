using UnityEngine;

[System.Serializable]
public class RandomEvent
{
    /// <summary>The String Table with the risk cards' texts (Assets/Localization/Source/Events.csv).</summary>
    public const string Table = "Events";

    public string eventName;
    [TextArea(2, 4)]
    public string description;
    public bool isGood;
    public float moneyChange;        // Direct money gain/loss
    public float percentageChange;   // Percentage of company value change (e.g., 0.1 = 10%)

    /// <summary>
    /// The card's translation key, set by EventDatabase when it builds its lists: "event." + list + "." + L10n.Slug(eventName)
    /// (e.g. "event.excellent_good.viral_product"); the Events table has key + ".title" and key + ".desc". Null for a card
    /// made anywhere else (it shows its English). eventName stays English: run stats save it.
    /// </summary>
    [System.NonSerialized]
    public string key;

    public RandomEvent(string name, string desc, bool good, float money, float percentage = 0f)
    {
        eventName = name;
        description = desc;
        isGood = good;
        moneyChange = money;
        percentageChange = percentage;
    }

    /// <summary>The title in the current language (Events table), else the English eventName.</summary>
    public string DisplayName => Localized(".title", eventName);

    /// <summary>
    /// The story in the current language, else the English description; always one line (the result view splits the text
    /// it is shown in at its first line break: the story, then the skill notes).
    /// </summary>
    public string DisplayDescription
    {
        get
        {
            string text = Localized(".desc", description);
            return text.IndexOf('\n') < 0 && text.IndexOf('\r') < 0 ? text : text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
        }
    }

    // English always shows the data itself (an edited card shows at once; Sync Content CSVs updates the table)
    string Localized(string part, string english)
    {
        if (english == null) english = "";
        if (string.IsNullOrEmpty(key) || english.Length == 0 || L10n.CurrentCode == L10n.English) return english;
        return L10n.GetOr(Table, key + part, english);
    }
}

public enum CardRisk
{
    HighRisk,    // 25% good, 75% bad - but excellent reward
    Balanced,    // 50% good, 50% bad - average reward
    LowRisk      // 75% good, 25% bad - below average reward
}

