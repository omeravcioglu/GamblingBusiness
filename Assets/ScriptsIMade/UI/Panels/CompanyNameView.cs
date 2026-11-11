using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The designed company-name sheet (built by Tools > Game > New UI > Build Company Name). CompanyNameUI keeps the
/// logic (validation, saving, pausing); this view adds what the design shows on top of it:
/// - first run ("Name your company", Incorporate, no way out) vs rename ("Rename your company", Save name, Cancel, close)
/// - the live business-card preview, the "13/25" counter, and the red input border while CompanyNameUI shows an error
/// Texts come from the Hud String Table in the current language (again when it changes); the buttons grow with a longer
/// label (HudFitWidth) and the button row places them.
/// </summary>
[DisallowMultipleComponent]
public class CompanyNameView : MonoBehaviour
{
    const string HasSetNameKey = "HasSetCompanyName";
    const string T = "Hud";   // Assets/Localization/Source/Hud.csv

    public TMP_InputField input;
    public TMP_Text title;
    public TMP_Text hint;
    public TMP_Text counter;
    public Image inputBorder;
    [Tooltip("CompanyNameUI's errorText: while it is shown, the input and counter turn red")]
    public GameObject errorRow;

    [Header("Buttons")]
    public TMP_Text confirmLabel;
    public Image confirmIcon;
    public Sprite incorporateIcon;
    public Sprite saveIcon;
    public Button cancelButton;
    public Button closeButton;
    [Tooltip("The confirm button's designed widths for \"Incorporate\" and \"Save name\" (its HudFitWidth minimum); Cancel sits 20 px after it")]
    public RectTransform confirmRect;
    public float confirmLeft;
    public float firstWidth;
    public float renameWidth;
    public RectTransform cancelRect;

    [Header("Business card")]
    public TMP_Text cardName;
    public TMP_Text cardValue;

    bool renaming;
    bool listening;

    void Awake()
    {
        L10nFont.AttachAll(this);   // typed names, the error line and every label in the language's font
    }

    void OnEnable()
    {
        renaming = PlayerPrefs.HasKey(HasSetNameKey);

        FillTexts();
        if (confirmIcon != null)
        {
            var icon = renaming ? saveIcon : incorporateIcon;
            if (icon != null) confirmIcon.sprite = icon;
        }
        if (cancelButton != null) cancelButton.gameObject.SetActive(renaming);
        if (closeButton != null) closeButton.gameObject.SetActive(renaming);
        SizeConfirm();

        if (!listening)
        {
            listening = true;
            if (input != null) input.onValueChanged.AddListener(OnTyped);
            if (cancelButton != null) cancelButton.onClick.AddListener(Cancel);
            if (closeButton != null) closeButton.onClick.AddListener(Cancel);
        }
        L10n.LanguageChanged += OnLanguageChanged;
        // CompanyNameUI fills the field when it opens; show whatever is there now
        OnTyped(input != null ? input.text : "");
    }

    void OnDisable()
    {
        L10n.LanguageChanged -= OnLanguageChanged;
    }

    void OnLanguageChanged()
    {
        FillTexts();
        OnTyped(input != null ? input.text : "");
    }

    /// <summary>The title, hint, confirm label and the card's value line for this mode, in the current language.</summary>
    void FillTexts()
    {
        if (title != null) title.text = renaming ? L10n.Get(T, "hud.name.title.rename") : L10n.Get(T, "hud.name.title.first");
        if (hint != null)
        {
            CompanyNameUI ui = CompanyNameUI.Instance;
            int min = ui != null ? ui.minNameLength : 3;
            int max = input != null && input.characterLimit > 0 ? input.characterLimit : (ui != null ? ui.maxNameLength : 25);
            hint.text = renaming
                ? L10n.Get(T, "hud.name.hint.rename", ("min", min), ("max", max),
                    ("company", CompanyNameUI.DisplayName(CompanyNameUI.CurrentCompanyName)))
                : L10n.Get(T, "hud.name.hint.first", ("min", min), ("max", max));
        }
        if (confirmLabel != null)
            confirmLabel.text = renaming ? L10n.Get(T, "hud.name.confirm.rename") : L10n.Get(T, "hud.name.confirm.first");
        if (cardValue != null && CompanyManager.Instance != null)
            cardValue.text = L10n.Get(T, "hud.name.card.value", ("value", "<b>" + UIFormat.Money(CompanyManager.Instance.companyValue) + "</b>"));
    }

    /// <summary>The confirm button's designed width for this mode; HudFitWidth widens it when the label needs more room.</summary>
    void SizeConfirm()
    {
        if (confirmRect == null || firstWidth <= 0f || renameWidth <= 0f) return;
        float w = renaming ? renameWidth : firstWidth;
        var fit = confirmRect.GetComponent<HudFitWidth>();
        if (fit != null) fit.minWidth = w;
        confirmRect.sizeDelta = new Vector2(w, confirmRect.sizeDelta.y);
        if (confirmRect.parent != null && confirmRect.parent.GetComponent<LayoutGroup>() != null) return;   // the button row places them

        // Without a button row: placed from the sheet's top-left corner with a centred pivot
        confirmRect.anchoredPosition = new Vector2(confirmLeft + w * 0.5f, confirmRect.anchoredPosition.y);
        if (cancelRect != null)
            cancelRect.anchoredPosition = new Vector2(confirmLeft + w + 20f + cancelRect.sizeDelta.x * 0.5f, cancelRect.anchoredPosition.y);
    }

    void Cancel()
    {
        if (CompanyNameUI.Instance != null)
            CompanyNameUI.Instance.CancelNameChange();
    }

    void OnTyped(string text)
    {
        int limit = input != null && input.characterLimit > 0 ? input.characterLimit : 25;
        if (counter != null) counter.text = (text ?? "").Length + "/" + limit;
        if (cardName != null)
            cardName.text = string.IsNullOrWhiteSpace(text) ? L10n.Get(T, "hud.name.card.placeholder") : L10n.ToUpper(text.Trim());
    }

    void LateUpdate()
    {
        bool error = errorRow != null && errorRow.activeSelf;
        if (inputBorder != null) inputBorder.color = error ? UIColors.Loss : UIColors.Ink;
        if (counter != null) counter.color = error ? UIColors.Loss : UIColors.Pencil;
    }
}
