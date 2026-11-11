using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One hanging file in the staff files (the "Staff files" board): portrait, name with the golden star, hire day and days
/// employed, IQ, degree and experience, salary per day, profit per day (green/red) with the lifetime profit, tag chips
/// (golden, criminal record, traits) and "Let go". StaffFilesView keeps a handful of these and moves them while the list
/// scrolls. The runtime builder fills the references; a designed row prefab can fill them instead (empty ones are skipped).
/// Its words come from the "Staff" String Table; StaffFilesView binds the rows again when the language changes.
/// </summary>
[DisallowMultipleComponent]
public class StaffFileRow : MonoBehaviour
{
    const string T = StaffLedger.Table;

    [Tooltip("The folder body: clicking it opens the file")]
    public Button openButton;
    [Tooltip("\"Let go\": straight to the fire confirmation")]
    public Button letGoButton;

    [Header("Portrait")]
    public Image portrait;
    [Tooltip("Shown while there is no portrait")]
    public GameObject portraitPlaceholder;

    [Header("Texts")]
    public TMP_Text nameText;
    [Tooltip("Placed right after the name for golden hires")]
    public RectTransform goldenStar;
    [Tooltip("\"Hired day 41 · 12 days\"")]
    public TMP_Text hiredText;
    public TMP_Text iqText;
    public TMP_Text degreeText;
    public TMP_Text experienceText;
    [Tooltip("\"$230/day\"")]
    public TMP_Text salaryText;
    [Tooltip("\"$84,000 a year\"")]
    public TMP_Text salaryYearText;
    [Tooltip("\"+$412/day\" in green, a loss in red")]
    public TMP_Text profitText;
    [Tooltip("\"lifetime +$12,400\"")]
    public TMP_Text lifetimeText;

    [Header("Tags")]
    [Tooltip("Chips, left to right: each a Graphic (its fill) with a TMP_Text child. Only whole chips that fit the first one's parent are shown")]
    public Graphic[] tagChips;
    [Tooltip("Room left and right of a chip's label")]
    public float tagPadding = 14f;
    public float tagGap = 8f;

    [Header("Folder tab")]
    [Tooltip("Gold for golden hires")]
    public Graphic tab;
    [Tooltip("Covers the tab's bottom edge (same colour as the tab)")]
    public Graphic tabSeam;

    /// <summary>The person this row shows (null while hidden).</summary>
    public ApplicantData Employee { get; private set; }
    /// <summary>Portrait the row is waiting for (-1 = none).</summary>
    public int PendingPortrait { get; private set; } = -1;

    TMP_Text[] tagTexts;
    int shownPortrait = -1;

    struct Tag
    {
        public string label;
        public Color fill;
        public Color ink;
    }

    static readonly List<Tag> Tags = new List<Tag>(8);

    /// <summary>Show <paramref name="employee"/>. The portrait comes from StaffFilesView (its cache, or a background load).</summary>
    public void Bind(ApplicantData employee, StaffStats stats)
    {
        Employee = employee;
        if (employee == null) return;

        string name = StaffLedger.DisplayName(employee);
        Set(nameText, name);
        PlaceStar(employee.isGolden, name);

        // "Hired day 41 · 12 days"; someone hired before the staff files were kept is "On file since day 12 · 3 days"
        // (no "first day" for them: they have been around longer than their file)
        string day = UIFormat.Int(stats.hireDay);
        string hired;
        if (stats.hireDayKnown)
            hired = stats.hasWorkedADay ? L10n.Get(T, "staff.row.hired.days", ("day", day), ("days", stats.daysEmployed))
                                        : L10n.Get(T, "staff.row.hired.first_day", ("day", day));
        else
            hired = stats.hasWorkedADay ? L10n.Get(T, "staff.row.on_file.days", ("day", day), ("days", stats.daysEmployed))
                                        : L10n.Get(T, "staff.row.on_file", ("day", day));
        Set(hiredText, hired);
        string soft = StaffUI.Hex(UIColors.InkSoft);
        Set(iqText, L10n.Get(T, "staff.row.iq", ("label", "<size=77%><color=" + soft + ">" + L10n.Get(T, "staff.row.iq.label") + "</color></size>"),
                             ("iq", "<b>" + employee.IQ.ToString(System.Globalization.CultureInfo.InvariantCulture) + "</b>")));
        Set(degreeText, ApplicantText.DegreeLine(employee));
        Set(experienceText, StaffText.Experience(employee.experienceYears));
        string perDay = L10n.Get(T, "staff.unit.per_day");
        Set(salaryText, L10n.Get(T, "staff.row.salary", ("amount", UIFormat.Money(stats.salaryPerDay)),
                                 ("unit", "<size=77%><color=" + soft + ">" + perDay + "</color></size>")));
        Set(salaryYearText, L10n.Get(T, "staff.row.salary_year", ("amount", UIFormat.Money(employee.salary))));

        double profit = stats.profitPerDay;
        Color profitColor = profit >= 0.0 ? UIColors.Money : UIColors.Loss;
        string profitUnit = "<size=77%>" + perDay + "</size>";
        Set(profitText, stats.hasWorkedADay
            ? L10n.Get(T, "staff.row.profit", ("amount", UIFormat.SignedMoney(profit)), ("unit", profitUnit))
            : L10n.Get(T, "staff.row.profit.estimate", ("estimate", "<size=77%><color=" + StaffUI.Hex(UIColors.Pencil) + ">" + L10n.Get(T, "staff.row.estimate") + "</color></size>"),
                       ("amount", UIFormat.SignedMoney(profit)), ("unit", profitUnit)));
        if (profitText != null) profitText.color = profitColor;
        // Every booked day added up (only since the file started, for someone on file only); nothing yet before the first
        string lifetime;
        if (stats.hasWorkedADay)
            lifetime = stats.hireDayKnown ? L10n.Get(T, "staff.row.lifetime", ("amount", UIFormat.SignedMoney(stats.lifetimeProfit)))
                                          : L10n.Get(T, "staff.row.lifetime.since", ("day", day), ("amount", UIFormat.SignedMoney(stats.lifetimeProfit)));
        else
            lifetime = L10n.Get(T, stats.hireDayKnown ? "staff.row.lifetime.none" : "staff.row.lifetime.none_on_file");
        Set(lifetimeText, lifetime);

        Color tabColor = employee.isGolden ? UIColors.Gold : UIColors.Manila;
        if (tab != null) tab.color = tabColor;
        if (tabSeam != null) tabSeam.color = tabColor;

        BindTags(employee);
        WantPortrait(employee.portraitIndex);
    }

    /// <summary>Hide the row's content (it is off the list).</summary>
    public void Clear()
    {
        Employee = null;
        PendingPortrait = -1;
        ShowPortrait(null, -1);
    }

    // ------------------------------------------------------------------
    // Portrait
    // ------------------------------------------------------------------

    void WantPortrait(int index)
    {
        if (index == shownPortrait && (portrait == null || portrait.sprite != null || index < 0))
        {
            PendingPortrait = -1;
            return;
        }
        ShowPortrait(null, -1);
        PendingPortrait = index >= 0 && portrait != null ? index : -1;
    }

    /// <summary>Show a loaded portrait (null = the placeholder).</summary>
    public void ShowPortrait(Sprite sprite, int index)
    {
        shownPortrait = sprite != null ? index : -1;
        if (PendingPortrait == index) PendingPortrait = -1;
        if (portrait != null)
        {
            portrait.sprite = sprite;
            portrait.enabled = sprite != null;
        }
        if (portraitPlaceholder != null && portraitPlaceholder.activeSelf == (sprite != null))
            portraitPlaceholder.SetActive(sprite == null);
    }

    // ------------------------------------------------------------------
    // Name and tags
    // ------------------------------------------------------------------

    void PlaceStar(bool golden, string name)
    {
        if (goldenStar == null) return;
        if (goldenStar.gameObject.activeSelf != golden) goldenStar.gameObject.SetActive(golden);
        if (!golden || nameText == null) return;
        RectTransform nameRect = nameText.rectTransform;
        float width = Mathf.Min(StaffUI.Measure(nameText, name), nameRect.rect.width);
        Vector2 position = goldenStar.anchoredPosition;
        position.x = nameRect.anchoredPosition.x + width + 8f;
        goldenStar.anchoredPosition = position;
    }

    void BindTags(ApplicantData employee)
    {
        if (tagChips == null || tagChips.Length == 0) return;
        if (tagTexts == null || tagTexts.Length != tagChips.Length)
        {
            tagTexts = new TMP_Text[tagChips.Length];
            for (int i = 0; i < tagChips.Length; i++)
                tagTexts[i] = tagChips[i] != null ? tagChips[i].GetComponentInChildren<TMP_Text>(true) : null;
        }

        Tags.Clear();
        if (employee.isGolden) Tags.Add(new Tag { label = L10n.Get(T, "staff.tag.golden"), fill = UIColors.Gold, ink = UIColors.Ink });
        if (employee.criminalRecord) Tags.Add(new Tag { label = L10n.Get(T, "staff.tag.record"), fill = UIColors.Loss, ink = UIColors.PaperHi });
        // Trait names are the applicant generator's (PersonalityTrait.traitName), shown in the current language (ApplicantText)
        if (employee.traits != null)
        {
            foreach (PersonalityTrait trait in employee.traits)
            {
                if (trait == null || string.IsNullOrEmpty(trait.traitName)) continue;
                Color fill = trait.type == TraitType.Positive ? UIColors.StickyGreen
                           : trait.type == TraitType.Negative ? UIColors.StickyPink : UIColors.StickyBlue;
                Tags.Add(new Tag { label = ApplicantText.Trait(trait.traitName), fill = fill, ink = UIColors.Ink });
            }
        }

        RectTransform area = tagChips[0] != null ? tagChips[0].rectTransform.parent as RectTransform : null;
        float room = area != null ? area.rect.width : 0f;
        float x = 0f;
        int shown = 0;
        for (int i = 0; i < tagChips.Length; i++)
        {
            Graphic chip = tagChips[i];
            if (chip == null) continue;
            bool show = false;
            if (shown < Tags.Count && tagTexts[i] != null)
            {
                Tag tag = Tags[shown];
                float width = Mathf.Ceil(StaffUI.Measure(tagTexts[i], tag.label)) + tagPadding * 2f + 4f;
                if (x + width <= room + 0.5f)
                {
                    show = true;
                    shown++;
                    chip.color = tag.fill;
                    tagTexts[i].text = tag.label;
                    tagTexts[i].color = tag.ink;
                    RectTransform rect = chip.rectTransform;
                    rect.anchoredPosition = new Vector2(x, rect.anchoredPosition.y);
                    rect.sizeDelta = new Vector2(width, rect.sizeDelta.y);
                    x += width + tagGap;
                }
                else
                {
                    shown = Tags.Count;   // the rest won't fit either (keep the order)
                }
            }
            if (chip.gameObject.activeSelf != show) chip.gameObject.SetActive(show);
        }
    }

    static void Set(TMP_Text text, string value)
    {
        if (text != null && text.text != value) text.text = value;
    }
}
