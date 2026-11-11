using System.Collections.Generic;

/// <summary>
/// The skills for Skills.csv (Tools > Game > Localization > Sync Content CSVs): SkillTreeManager's skills in tree order,
/// each one's name, effect sentence and quote under "skill." + id + ".name" / ".effect" / ".quote" (Skill.DisplayName,
/// DisplayEffect, DisplayQuote read them). SkillTreeManager's toasts in the same table ("skilltree.toast...") are
/// hand-written rows: the sync leaves them alone.
/// </summary>
public sealed class SkillsTextSource : IContentTextSource
{
    public string Table => Skill.Table;

    public IEnumerable<ContentText> Collect()
    {
        foreach (Skill skill in SkillTreeManager.CreateDefaultSkills())
        {
            if (skill == null || string.IsNullOrEmpty(skill.id)) continue;
            string name = skill.skillName ?? "";
            yield return new ContentText(skill.TextKey + ".name", NameContext(skill), name);
            yield return new ContentText(skill.TextKey + ".effect", EffectContext(skill, name), skill.EnglishEffect);
            if (skill.EnglishQuote.Length > 0)
                yield return new ContentText(skill.TextKey + ".quote", QuoteContext(name), skill.EnglishQuote);
        }
    }

    static string Branch(SkillBranch branch)
    {
        switch (branch)
        {
            case SkillBranch.Productivity: return "Productivity";
            case SkillBranch.Chaos: return "Chaos & Luck";
            default: return "Growth";
        }
    }

    static string NameContext(Skill skill)
    {
        return "Skill name (" + Branch(skill.branch) + ", tier " + skill.tier + ") on the skill board: a pinned index card (148 px, 2 lines at " +
               "19 px: about 7 CJK characters a line), the open folder's title and toasts.";
    }

    static string EffectContext(Skill skill, string name)
    {
        return "What the skill “" + name + "” does (" + Branch(skill.branch) + ", tier " + skill.tier + "): one sentence in the skill board's " +
               "open folder (wraps). Keep every number exactly as in the English and add no digits (write a number the English spells " +
               "out as a word): a translation whose numbers differ shows the English. The sentence is highlighted from its first number on.";
    }

    static string QuoteContext(string name)
    {
        return "The joke quote under “" + name + "” in the skill board's open folder (italic; the game adds curly quotes, so write none). " +
               "Corporate satire: transcreate.";
    }
}
