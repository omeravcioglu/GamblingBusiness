using System.Globalization;
using UnityEngine;

/// <summary>
/// The "Company" String Table (Assets/Localization/Source/Company.csv) in the current language: the company's own
/// messages (morale, the Chaos &amp; Luck branch's daily luck, lucky hires, Trash to Treasure), the company name popup,
/// office item upgrades (item and level names, the old choice and result panels), the pause menu's save card and status
/// lines, and the main menu's coin, next-flip line and volume dice. Keys: company.*, office.*, upgrade.*, pause.*, menu.*.
/// </summary>
public static class CompanyText
{
    /// <summary>The String Table collection of these texts.</summary>
    public const string Table = "Company";

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Get(string key) => L10n.Get(Table, key);

    public static string Get(string key, params (string name, object value)[] args) => L10n.Get(Table, key, args);

    /// <summary>
    /// A skill's name as the skill board shows it (Skill.DisplayName: the Skills table's "skill.&lt;id&gt;.name"), for the
    /// messages that name the skill that acted; <paramref name="english"/> when there is no such skill or translation.
    /// </summary>
    public static string SkillName(string skillId, string english)
    {
        SkillTreeManager skills = SkillTreeManager.Instance;
        Skill skill = skills != null ? skills.GetSkill(skillId) : null;
        if (skill != null) return skill.DisplayName;
        if (string.IsNullOrEmpty(skillId) || L10n.CurrentCode == L10n.English) return english ?? "";
        return L10n.GetOr(Skill.Table, "skill." + skillId + ".name", english);
    }

    /// <summary>A share (0.25) as the whole number of a "{percent}%" placeholder ("25"): the entry puts the % sign where its language wants it.</summary>
    public static string Percent(float share) => Mathf.RoundToInt(share * 100f).ToString(Inv);

    /// <summary>A number as a placeholder's plain digits ("12"), culture-independent.</summary>
    public static string Number(float value) => value.ToString("0", Inv);

    /// <summary>
    /// The quality presets (QualitySettings.names: High Fidelity, Balanced, Performant) for a Quality dropdown, in the
    /// current language (pause.quality.&lt;slug&gt;; a preset the table doesn't have shows its English). Same order and count,
    /// so the dropdown's index is still the quality level.
    /// </summary>
    public static System.Collections.Generic.List<string> QualityNames()
    {
        string[] names = QualitySettings.names;
        var list = new System.Collections.Generic.List<string>(names.Length);
        foreach (string english in names)
        {
            string slug = L10n.Slug(english);
            list.Add(slug.Length == 0 ? english : L10n.GetOr(Table, "pause.quality." + slug, english));
        }
        return list;
    }
}
