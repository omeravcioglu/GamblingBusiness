using TMPro;
using UnityEngine;

/// <summary>
/// The CV's floor-impact slip (design canvas board "HireLink", 2026-11-24): a ledger slip paper-clipped to the right edge
/// of every CV, saying what hiring this applicant does on the cash floor: the department they join (from their major,
/// CashRules.DeptForMajor) or the CEO's side as an intern, the cash a day they bring to the floor, and what that does to
/// the race. CashGrabSystem adds it to each CVPaperView it finds at run time, so the CV prefab needs no rebuild.
/// </summary>
[DisallowMultipleComponent]
public class CashCvSlip : MonoBehaviour
{
    CVPaperView paper;
    RectTransform slip;
    BlueprintBox chip;
    TextMeshProUGUI head, joins, cash, effect;
    ApplicantData shown;
    float refresh;
    static GameFlowManager flow;

    /// <summary>Give every CV face in the scene its slip (cheap: a CV already holding one is skipped).</summary>
    public static void AttachAll()
    {
        foreach (CVPaperView view in FindObjectsByType<CVPaperView>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (view == null || view.sheet == null || view.GetComponent<CashCvSlip>() != null) continue;
            view.gameObject.AddComponent<CashCvSlip>();
        }
    }

    void Awake()
    {
        paper = GetComponent<CVPaperView>();
    }

    void Build()
    {
        TMP_FontAsset font = paper.degree != null ? paper.degree.font : L10n.DefaultFont;

        slip = BlueprintUI.NewRect("CashFloorSlip", paper.sheet);
        slip.anchorMin = slip.anchorMax = new Vector2(1f, 1f);
        slip.pivot = new Vector2(0f, 1f);
        slip.anchoredPosition = new Vector2(-26f, -430f);   // overlaps the paper's edge like a clipped slip
        slip.sizeDelta = new Vector2(250f, 182f);
        slip.localRotation = Quaternion.Euler(0f, 0f, -3f);
        BlueprintBox paperBox = BlueprintUI.AddBox(slip, UIColors.Ledger, UIColors.Ink, 2f, BlueprintUI.Round(4f));
        paperBox.SetStripes(UIColors.WithAlpha(UIColors.Hex(0xA9C9A0), 0.6f), 30f, 1.5f);
        paperBox.SetShadow(new Color(0f, 0f, 0f, 0.3f), new Vector2(5f, -7f));

        // The paper clip
        BlueprintBox clip = BlueprintUI.Box(slip, "Clip", Color.clear, UIColors.Hex(0x8E949D), 3.5f, BlueprintUI.Round(10f));
        BlueprintUI.TopLeft(clip.rectTransform, 6f, -18f, 20f, 58f);
        clip.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 8f);

        head = BlueprintUI.Caps(BlueprintUI.Text(slip, "Head", "", 13f, UIColors.Ink, font, TextAlignmentOptions.Left, true));
        BlueprintUI.TopLeft(head.rectTransform, 30f, 8f, 210f, 24f);
        BlueprintUI.AutoSize(head, 9f);

        chip = BlueprintUI.Box(slip, "Dept", CashRules.DeptColors[0], UIColors.Ink, 2f, BlueprintUI.Round(6f));
        BlueprintUI.TopLeft(chip.rectTransform, 14f, 40f, 222f, 40f);
        joins = BlueprintUI.Text(chip.transform, "Text", "", 18f, UIColors.PaperHi, font, TextAlignmentOptions.Center, true);
        BlueprintUI.Stretch(joins.rectTransform, 6f, 2f, 6f, 2f);
        BlueprintUI.AutoSize(joins, 10f);

        cash = BlueprintUI.Text(slip, "Cash", "", 18f, UIColors.Money, font, TextAlignmentOptions.Left, true);
        BlueprintUI.TopLeft(cash.rectTransform, 16f, 88f, 222f, 30f);
        BlueprintUI.AutoSize(cash, 10f);

        effect = BlueprintUI.Wrap(BlueprintUI.Text(slip, "Effect", "", 14f, UIColors.InkSoft, font));
        BlueprintUI.TopLeft(effect.rectTransform, 16f, 120f, 222f, 56f);
        BlueprintUI.AutoSize(effect, 9f);

        L10nFont.AttachAll(slip);
    }

    void Update()
    {
        if (paper == null || paper.sheet == null) return;
        ApplicantData a = paper.Applicant;
        // Only the CV on top of the pile, until it's stamped (the papers underneath stay tidy)
        if (flow == null) flow = FindAnyObjectByType<GameFlowManager>();
        bool onTop = flow == null || ReferenceEquals(flow.CurrentApplicant, a);
        bool wanted = a != null && onTop && paper.CurrentStamp == CVPaperView.Stamp.None && CashGrabSystem.Instance != null && CompanyManager.Instance != null;
        if (!wanted)
        {
            if (slip != null && slip.gameObject.activeSelf) slip.gameObject.SetActive(false);
            return;
        }
        if (slip == null) Build();
        if (!slip.gameObject.activeSelf) slip.gameObject.SetActive(true);

        // Rewrite for a new applicant, a new language, and now and then (the company's numbers move)
        refresh -= Time.unscaledDeltaTime;
        if (ReferenceEquals(a, shown) && refresh > 0f) return;
        shown = a;
        refresh = 2f;
        Write(a);
    }

    void OnEnable() => L10n.LanguageChanged += OnLanguageChanged;
    void OnDisable() => L10n.LanguageChanged -= OnLanguageChanged;
    void OnLanguageChanged() => shown = null;

    void Write(ApplicantData a)
    {
        CompanyManager company = CompanyManager.Instance;
        head.text = CashText.Get("cash.slip.head");

        int dept = CashRules.DeptForMajor(a.major);
        if (dept >= 0)
        {
            chip.Fill = CashRules.DeptColors[dept];
            joins.color = CashRules.DeptInk(dept);
            joins.text = CashText.Get("cash.slip.joins", ("dept", CashText.DeptName(dept)));
            effect.text = CashText.Get("cash.slip.effect_dept", ("dept", CashText.DeptName(dept)), ("percent", Mathf.RoundToInt(CashRules.HireSpeedPerHire * 100f)));
        }
        else
        {
            chip.Fill = CashRules.PlayerColor;
            joins.color = UIColors.Ink;
            joins.text = CashText.Get("cash.slip.intern");
            effect.text = CashText.Get("cash.slip.effect_intern", ("percent", Mathf.RoundToInt(CashRules.InternReachPerHire * 100f)));
        }

        double profit = company.ProjectEmployeeDay(a, true).Profit;
        if (profit > 0.0)
        {
            cash.color = UIColors.Money;
            cash.text = CashText.Get("cash.slip.cash", ("amount", UIFormat.Money(System.Math.Round(profit * (1.0 - CashRules.RetainedShare)))));
        }
        else
        {
            cash.color = UIColors.Loss;
            cash.text = CashText.Get("cash.slip.costs", ("amount", UIFormat.Money(System.Math.Round(-profit))));
        }
    }
}
