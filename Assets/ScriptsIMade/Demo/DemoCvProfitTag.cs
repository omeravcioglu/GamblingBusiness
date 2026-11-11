using TMPro;
using UnityEngine;

/// <summary>
/// Demo only: a small "+$114/day" line under the salary on each CV, what the applicant would add to the company per day
/// after their pay (StaffLedger.ProjectDailyProfit). Green from $0 up, red below. Built at runtime as a copy of the
/// salary's unit label (the designed CV) or of the salary text (the plain CV); no prefab edits.
/// </summary>
[DisallowMultipleComponent]
public class DemoCvProfitTag : MonoBehaviour
{
    const string ObjectName = "DemoProfitTag (Runtime)";
    const float SizeScale = 0.8f;   // a step smaller than the salary's unit

    TMP_Text label;
    ApplicantData applicant;

    /// <summary>Adds the line to the binder's CV (or reuses it) and writes it for <paramref name="a"/>.</summary>
    public static void Show(ApplicantUIBinder binder, ApplicantData a)
    {
        if (!DemoMode.Active || binder == null || a == null) return;
        DemoCvProfitTag tag = binder.GetComponentInChildren<DemoCvProfitTag>(true);
        if (tag == null) tag = Create(binder);
        if (tag == null) return;
        tag.applicant = a;
        tag.Refresh();
    }

    static DemoCvProfitTag Create(ApplicantUIBinder binder)
    {
        CVPaperView cv = binder.cvView;
        if (cv != null)
        {
            if (cv.salaryPer == null || cv.salaryNumber == null) return null;
            return CreateUnder(cv.salaryPer, cv.salaryNumber);
        }
        if (binder.salaryText == null) return null;
        return CreateUnder(binder.salaryText, binder.salaryText);
    }

    // A copy of <template> as a sibling, left-aligned with <row> and placed just under it
    static DemoCvProfitTag CreateUnder(TMP_Text template, TMP_Text row)
    {
        RectTransform rowRt = row.rectTransform;
        GameObject go = Instantiate(template.gameObject, template.transform.parent, false);
        go.name = ObjectName;
        foreach (Transform child in go.transform) Destroy(child.gameObject);
        TextHighlighter highlighter = go.GetComponent<TextHighlighter>();
        if (highlighter != null) Destroy(highlighter);

        TMP_Text text = go.GetComponent<TMP_Text>();
        text.fontSize = template.fontSize * SizeScale;
        text.enableAutoSizing = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.raycastTarget = false;

        RectTransform rt = text.rectTransform;
        if (template != row && rt.parent == rowRt.parent)
        {
            // Designed CV (uGUI): the row's anchors and width, its top edge a little inside the row's bottom edge
            float height = text.fontSize * 1.15f;
            float overlap = rowRt.sizeDelta.y * 0.12f;
            rt.anchorMin = rowRt.anchorMin;
            rt.anchorMax = rowRt.anchorMax;
            rt.pivot = rowRt.pivot;
            rt.sizeDelta = new Vector2(rowRt.sizeDelta.x, height);
            float rowBottom = rowRt.anchoredPosition.y - rowRt.pivot.y * rowRt.sizeDelta.y;
            float top = rowBottom + overlap;
            rt.anchoredPosition = new Vector2(rowRt.anchoredPosition.x, top - (1f - rt.pivot.y) * height);
        }
        else
        {
            // Plain CV (3D text): one line of the salary text further down, in the text's own orientation
            float lineHeight = rowRt.rect.height > 0f ? rowRt.rect.height : template.fontSize * 0.1f;
            Vector3 down = row.transform.localRotation * Vector3.down * (lineHeight * row.transform.localScale.y);
            go.transform.localPosition = row.transform.localPosition + down;
        }

        L10nFont.Attach(text);
        return go.AddComponent<DemoCvProfitTag>();
    }

    void Awake()
    {
        label = GetComponent<TMP_Text>();
    }

    void OnEnable()
    {
        L10n.LanguageChanged += Refresh;
    }

    void OnDisable()
    {
        L10n.LanguageChanged -= Refresh;
    }

    void Refresh()
    {
        if (label == null) label = GetComponent<TMP_Text>();
        if (label == null) return;
        StaffLedger ledger = StaffLedger.Instance;
        bool show = ledger != null && applicant != null;
        if (!show)
        {
            label.enabled = false;
            return;
        }
        double perDay = ledger.ProjectDailyProfit(applicant);
        label.text = DemoMode.T("demo.cv.per_day", ("amount", UIFormat.SignedMoney(perDay)));
        label.color = System.Math.Round(perDay) >= 0 ? UIColors.Money : UIColors.Loss;
        label.enabled = true;
    }
}
