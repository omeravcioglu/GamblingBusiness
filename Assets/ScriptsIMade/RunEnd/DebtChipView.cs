using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A small paper chip at the bottom-left of the screen while a bank loan is out: "Loan: $X owed" and a Repay button that
/// pays as much as the company can spare (BankLoan.RepayAsMuchAsPossible). Own overlay canvas (sorting order 70).
/// Hidden while a pop-up, crisis, risk card, build mode or a run-end screen is up, like the rest of the HUD under them.
/// With Root empty the chip is built at runtime; a designed one can fill the references (a HUD can also read
/// BankLoan.Debt / BankLoan.DebtChanged and switch this off).
/// Its words come from the "RunEnd" String Table (runend.debt_chip.*); the chip grows with them and is written again, in
/// the language's font, when the language changes.
/// </summary>
[DisallowMultipleComponent]
public class DebtChipView : MonoBehaviour
{
    [Header("Look")]
    [Tooltip("Font (empty = the Coming Soon font the HUD uses, else TextMesh Pro's default)")]
    public TMP_FontAsset font;
    [Tooltip("Sorting order of the runtime canvas")]
    public int sortingOrder = 70;
    [Tooltip("Distance from the bottom-left corner of the screen (reference pixels)")]
    public Vector2 margin = new Vector2(24f, 24f);

    [Header("Chip (leave Root empty to build it at runtime)")]
    public RectTransform root;
    [Tooltip("\"Loan: $X owed\"")]
    public TMP_Text debtText;
    public Button repayButton;

    bool hooked;
    double shownDebt = -1.0;
    bool fontsFollowLanguage;
    TMP_Text runtimeRepayLabel;   // the runtime chip's Repay label: its button fits it again after a language change

    void OnEnable()
    {
        L10n.LanguageChanged += OnLanguageChanged;
    }

    void OnDisable()
    {
        L10n.LanguageChanged -= OnLanguageChanged;
    }

    // Write the label again on the next Update (the fonts switch by themselves: L10nFont)
    void OnLanguageChanged()
    {
        shownDebt = -1.0;
    }

    void Update()
    {
        BankLoan loan = BankLoan.Instance;
        bool show = loan != null && loan.HasLoan && HudVisible();
        if (show) EnsureBuilt();
        if (root != null && root.gameObject.activeSelf != show) root.gameObject.SetActive(show);
        if (show && loan.Debt != shownDebt) Refresh(loan.Debt);
    }

    /// <summary>Nothing covers the HUD: no panel, crisis, risk card, build mode or run-end screen.</summary>
    static bool HudVisible()
    {
        if (UIManager.Exists && UIManager.Instance.IsAnyPanelOpen()) return false;
        if (MiniGameManager.Instance != null && MiniGameManager.Instance.IsMiniGameActive()) return false;
        if (EventManager.Instance != null && EventManager.Instance.IsEventInProgress()) return false;
        if (TowerView.Instance != null && TowerView.Instance.IsShowing) return false;
        RunEndDirector director = RunEndDirector.Instance;
        if (director != null && (director.IsRunOver || director.IsAnyScreenOpen)) return false;
        return true;
    }

    void Refresh(double debt)
    {
        shownDebt = debt;
        if (debtText != null)
            debtText.text = L10n.Get(RunEndUI.Table, "runend.debt_chip.label", ("amount", "<b>" + UIFormat.Money(System.Math.Ceiling(debt)) + "</b>"));
        FitRepayButton();
    }

    /// <summary>The runtime chip's Repay button as wide as its word in the current language (and font).</summary>
    void FitRepayButton()
    {
        if (runtimeRepayLabel == null || repayButton == null) return;
        float width = Mathf.Ceil(RunEndUI.TextWidth(runtimeRepayLabel.font, runtimeRepayLabel.text, 20f, true)) + 4f + 18f * 2f;
        LayoutElement layout = repayButton.GetComponent<LayoutElement>();
        if (layout != null && !Mathf.Approximately(layout.preferredWidth, width)) layout.preferredWidth = layout.minWidth = width;
    }

    void OnRepay()
    {
        BankLoan loan = BankLoan.Instance;
        if (loan != null) loan.RepayAsMuchAsPossible();
    }

    void EnsureBuilt()
    {
        if (root == null)
        {
            Canvas canvas = RunEndUI.CreateCanvas("DebtChipCanvas (Runtime)", sortingOrder, transform);
            Build(this, (RectTransform)canvas.transform, RunEndUI.ResolveFont(font));
        }
        if (root != null && !fontsFollowLanguage)
        {
            L10nFont.AttachAll(root);   // the chip's texts (built here or designed) take the language's font
            fontsFollowLanguage = true;
        }
        if (hooked) return;
        hooked = true;
        if (repayButton != null) repayButton.onClick.AddListener(OnRepay);
    }

    static void Build(DebtChipView view, RectTransform parent, TMP_FontAsset font)
    {
        // Built inactive, so MarkerButton starts with its face set; Update shows it
        BlueprintBox chip = BlueprintUI.Box(parent, "DebtChip", UIColors.PaperHi, UIColors.Ink, 2f, BlueprintUI.RadiusSmall, true);
        chip.SetShadow(UIColors.Ink, new Vector2(3f, 4f));
        RectTransform rect = chip.rectTransform;
        rect.gameObject.SetActive(false);
        rect.anchorMin = rect.anchorMax = Vector2.zero;
        rect.pivot = Vector2.zero;
        rect.anchoredPosition = view.margin;
        view.root = rect;

        BlueprintUI.HRow(chip, 12f, new RectOffset(16, 12, 10, 12), TextAnchor.MiddleLeft);
        BlueprintUI.Fit(chip, true, true);
        RunEndIcon.Create(rect, RunEndIconKind.Bank, 28f, UIColors.Loss);
        view.debtText = BlueprintUI.Text(rect, "Debt", L10n.Get(RunEndUI.Table, "runend.debt_chip.label", ("amount", "$0")), 22f, UIColors.Ink, font,
                                         TextAlignmentOptions.MidlineLeft);

        view.repayButton = RunEndUI.PaperButton(rect, "Repay", L10n.Get(RunEndUI.Table, "runend.debt_chip.repay"), RunEndIconKind.None, font,
                                                UIColors.Highlight, 0f, 44f, 20f, true, new Vector2(3f, 4f), out TextMeshProUGUI repay, 18f);
        L10nText.Attach(repay, RunEndUI.Table, "runend.debt_chip.repay");   // "Repay"
        view.runtimeRepayLabel = repay;
        RectTransform button = (RectTransform)view.repayButton.transform;
        BlueprintUI.Layout(button, button.sizeDelta.x, 44f, -1f, -1f, button.sizeDelta.x, 44f);
    }
}
