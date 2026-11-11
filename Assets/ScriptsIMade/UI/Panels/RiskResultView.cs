using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The designed risk-event result: a newspaper clipping ("BREAKING · DAY 42", GOOD NEWS / BAD NEWS, headline,
/// story, the amount as a rubber stamp, skill notes and the card you played). EventManager writes into hidden
/// source texts (resultTitleText, resultDescriptionText, resultValueText), already in the player's language; this view
/// lays them out and adds its own lines from the Hud String Table.
/// </summary>
[DisallowMultipleComponent]
public class RiskResultView : MonoBehaviour
{
    const string T = "Hud";   // Assets/Localization/Source/Hud.csv

    [Header("Written by EventManager (hidden)")]
    public TMP_Text sourceTitle;
    public TMP_Text sourceDescription;
    public TMP_Text sourceValue;

    [Header("Clipping")]
    public TMP_Text dateline;
    public TMP_Text verdict;
    public TMP_Text headline;
    public TMP_Text story;
    public TMP_Text notes;
    public Image artIcon;
    public Sprite goodArt;
    public Sprite badArt;

    [Header("Stamp")]
    public TMP_Text stampText;
    public Image[] stampLines;
    public GameObject ouchSticky;

    string shownTitle, shownDescription, shownValue;

    void Awake()
    {
        L10nFont.AttachAll(this);   // the language's font on every text of the clipping
    }

    void OnEnable()
    {
        shownTitle = shownDescription = shownValue = null;
        L10n.LanguageChanged += Refresh;
        Refresh();
    }

    void OnDisable()
    {
        L10n.LanguageChanged -= Refresh;
    }

    // EventManager fills the texts right after it switches this page on
    void LateUpdate()
    {
        if (sourceTitle != null && sourceTitle.text != shownTitle) Refresh();
        else if (sourceDescription != null && sourceDescription.text != shownDescription) Refresh();
        else if (sourceValue != null && sourceValue.text != shownValue) Refresh();
    }

    void Refresh()
    {
        shownTitle = sourceTitle != null ? sourceTitle.text : "";
        shownDescription = sourceDescription != null ? sourceDescription.text : "";
        shownValue = sourceValue != null ? sourceValue.text : "";

        bool bad = shownValue.StartsWith("-") || shownValue.StartsWith(UIFormat.Minus);
        Color c = bad ? UIColors.Loss : UIColors.Money;

        int day = CompanyManager.Instance != null ? CompanyManager.Instance.currentDay : 1;
        if (dateline != null) dateline.text = L10n.Get(T, "hud.risk.result.dateline", ("day", day));
        if (verdict != null)
        {
            verdict.text = bad ? L10n.Get(T, "hud.risk.result.verdict.bad") : L10n.Get(T, "hud.risk.result.verdict.good");
            verdict.color = c;
        }
        if (headline != null) headline.text = shownTitle;

        // The first line is the event's own text; the rest are EventManager's skill notes
        string storyText = shownDescription;
        string noteText = "";
        int br = shownDescription.IndexOf('\n');
        if (br >= 0)
        {
            storyText = shownDescription.Substring(0, br);
            noteText = shownDescription.Substring(br + 1).Trim();
        }
        if (story != null) story.text = storyText;
        if (notes != null)
        {
            string played = RiskCardsView.LastGoodPercent < 0 ? ""
                : L10n.Get(T, "hud.risk.result.played", ("card", RiskCardsView.CardName(RiskCardsView.LastRisk)), ("percent", RiskCardsView.LastGoodPercent));
            notes.text = string.IsNullOrEmpty(noteText) ? played : (string.IsNullOrEmpty(played) ? noteText : noteText + "\n" + played);
        }

        if (stampText != null)
        {
            stampText.text = shownValue;
            stampText.color = c;
        }
        if (stampLines != null)
            foreach (var img in stampLines)
                if (img != null) img.color = c;
        if (artIcon != null)
        {
            var art = bad ? badArt : goodArt;
            if (art != null) artIcon.sprite = art;
            artIcon.color = c;
        }
        if (ouchSticky != null && ouchSticky.activeSelf != bad) ouchSticky.SetActive(bad);
    }
}
