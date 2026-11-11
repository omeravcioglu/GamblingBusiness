using System.Collections.Generic;

/// <summary>
/// The risk cards for Events.csv (Tools > Game > Localization > Sync Content CSVs): EventDatabase's six lists in order,
/// every card's title and story under its key, "event." + list + "." + L10n.Slug(title) + ".title" / ".desc"
/// (RandomEvent.key). EventManager's own texts in the same table ("risk.note...", "risk.toast...", "risk.card...") are
/// hand-written rows: the sync leaves them alone.
/// </summary>
public sealed class EventsTextSource : IContentTextSource
{
    public string Table => RandomEvent.Table;

    public IEnumerable<ContentText> Collect()
    {
        foreach (KeyValuePair<string, List<RandomEvent>> list in EventDatabase.DefaultLists())
        {
            foreach (RandomEvent card in list.Value)
            {
                if (card == null || string.IsNullOrEmpty(card.key)) continue;
                string title = card.eventName ?? "";
                yield return new ContentText(card.key + ".title", TitleContext(list.Key), title);
                yield return new ContentText(card.key + ".desc", StoryContext(list.Key, title), card.description ?? "");
            }
        }
    }

    /// <summary>Which card and outcome a list is, for the translators.</summary>
    static string Kind(string list)
    {
        switch (list)
        {
            case EventDatabase.ExcellentGoodList: return "High risk card, good outcome: a huge win";
            case EventDatabase.TerribleBadList: return "High risk card, bad outcome: a disaster";
            case EventDatabase.AverageGoodList: return "Balanced card, good outcome";
            case EventDatabase.AverageBadList: return "Balanced card, bad outcome";
            case EventDatabase.SmallGoodList: return "Low risk card, good outcome: a small win";
            case EventDatabase.SmallBadList: return "Low risk card, bad outcome: a small nuisance";
            default: return list;
        }
    }

    static string TitleContext(string list)
    {
        return "Risk card title (" + Kind(list) + "): the headline of the result's newspaper clipping (60 px bold, one line, cut with " +
               "… when longer: ~25 characters, CJK ~12) and the card named on the end screens (biggest win / loss). Satire: keep the " +
               "joke and brand, person and place names.";
    }

    static string StoryContext(string list, string title)
    {
        return "Risk card story (" + Kind(list) + ") under the headline “" + title + "”: ONE line, no line breaks (wraps in a 454 px " +
               "column: ~90 characters, CJK ~60). The amount shows separately. Satire: keep the joke.";
    }
}
