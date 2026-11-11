using System.Collections.Generic;

/// <summary>
/// One piece of content text for the translation tables, read from the game's own data (the risk cards in
/// EventDatabase, the hiring tasks in HiringTaskDatabase.asset, the majors in MajorDatabase.asset, the skills in
/// SkillTreeManager, the traits in TraitDatabase…): its key, a note for translators and the English as the game has it.
/// </summary>
public struct ContentText
{
    public string Key;
    public string Context;
    public string English;

    public ContentText(string key, string context, string english)
    {
        Key = key;
        Context = context;
        English = english;
    }
}

/// <summary>
/// A kind of content whose English lives in game data. Implement it (Editor code, a class with a public parameterless
/// constructor) and ContentTextSync picks it up: Tools > Game > Localization > Sync Content CSVs merges every source into
/// Assets/Localization/Source/&lt;Table&gt;.csv — new keys are added with their English (translations empty until drafted),
/// changed English replaces the old and its translations are kept but listed for re-translation, keys the data no longer
/// has are kept and listed. The runtime reads these tables with L10n.GetOr(table, key, english).
/// </summary>
public interface IContentTextSource
{
    /// <summary>The String Table / CSV name, e.g. "Events".</summary>
    string Table { get; }

    /// <summary>Every text of this content, in a stable order (the CSV keeps it).</summary>
    IEnumerable<ContentText> Collect();
}
