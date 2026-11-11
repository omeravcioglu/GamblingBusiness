using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// The player's language choice in Settings: a TMP_Dropdown that lists L10n.Languages by their own names (English,
/// 简体中文, 日本語, 한국어, Türkçe); picking one calls L10n.SetLanguage (the package remembers it for the next start). It
/// follows the language when something else changes it (e.g. the debug menu) and writes its "Language" label in the
/// current language (Company table, pause.language).
/// - Setup(designed, quality): use a designed dropdown when the menu has one, else copy the designed Quality dropdown
///   and its label into the Settings page's footer (between "Reset to defaults" and "Changes save automatically."),
///   so the picker matches the page. Used by SettingsPanel (pause menu) and MainMenuUI (main menu).
/// </summary>
[DisallowMultipleComponent]
public class LanguagePicker : MonoBehaviour
{
    public TMP_Dropdown dropdown;
    [Tooltip("Optional label written in the current language (\"Language\", \"语言\", \"Dil\"...)")]
    public TMP_Text label;

    readonly List<string> codes = new List<string>();

    const string LabelTable = "Company";
    const string LabelKey = "pause.language";

    /// <summary>
    /// Wire <paramref name="designed"/> if there is one; otherwise place a copy of <paramref name="quality"/> (and its
    /// "QualityLabel" sibling) in the Settings page's footer. Returns the picker, or null when neither is possible.
    /// Calling it again for the same page returns the existing picker.
    /// </summary>
    public static LanguagePicker Setup(TMP_Dropdown designed, TMP_Dropdown quality)
    {
        if (designed != null) return Attach(designed, null);
        if (quality == null) return null;

        var page = quality.transform.parent as RectTransform;
        if (page == null) return null;
        Transform existing = page.Find("LanguageDropdown");
        if (existing != null && existing.TryGetComponent(out LanguagePicker already)) return already;

        return CreateInFooter(page, quality);
    }

    /// <summary>Make <paramref name="target"/> a language picker (adds the component on its object).</summary>
    public static LanguagePicker Attach(TMP_Dropdown target, TMP_Text optionalLabel)
    {
        if (target == null) return null;
        if (!target.TryGetComponent(out LanguagePicker picker))
            picker = target.gameObject.AddComponent<LanguagePicker>();
        picker.dropdown = target;
        if (optionalLabel != null) picker.label = optionalLabel;
        picker.Wire();
        if (picker.isActiveAndEnabled) picker.Refresh();
        return picker;
    }

    // ------------------------------------------------------------------

    static LanguagePicker CreateInFooter(RectTransform page, TMP_Dropdown quality)
    {
        var qualityRect = (RectTransform)quality.transform;
        RectTransform reset = FindRect(page, "ResetDefaults");
        RectTransform note = FindRect(page, "AutoSaveNote");
        RectTransform done = FindRect(page, "DoneButton");
        RectTransform qualityLabel = FindRect(page, "QualityLabel");

        // The footer row: the Reset link's line (else the Done button's)
        RectTransform rowRef = reset != null ? reset : done;
        if (rowRef == null)
        {
            Debug.LogWarning("[LanguagePicker] The Settings page has no footer to put the language picker in.");
            return null;
        }

        float left = reset != null ? Right(reset) + 40f : Left(qualityRect);
        float right = note != null ? Left(note) - 24f : (done != null ? Left(done) - 24f : left + 420f);
        float height = qualityRect.rect.height;
        float centreY = Top(rowRef) - rowRef.rect.height * 0.5f;   // anchoredPosition space (y grows up)
        if (done != null) centreY = Top(done) - done.rect.height * 0.5f;

        // Label: a copy of "Quality" in the same style
        TMP_Text labelText = null;
        float labelWidth = 0f;
        if (qualityLabel != null)
        {
            GameObject labelCopy = Instantiate(qualityLabel.gameObject, page, false);
            labelCopy.name = "LanguageLabel";
            // A copied binding would keep writing "Quality" over the label: drop it (this picker writes the label itself)
            foreach (L10nText bound in labelCopy.GetComponents<L10nText>())
            {
                bound.enabled = false;
                Destroy(bound);
            }
            foreach (UnityEngine.Localization.Components.LocalizeStringEvent bound in
                     labelCopy.GetComponents<UnityEngine.Localization.Components.LocalizeStringEvent>())
            {
                bound.enabled = false;
                Destroy(bound);
            }
            labelText = labelCopy.GetComponent<TMP_Text>();
            var labelRect = (RectTransform)labelCopy.transform;
            labelWidth = Mathf.Max(60f, labelRect.rect.width * 0.8f);
            PlaceTopLeft(labelRect, left, centreY + labelRect.rect.height * 0.5f, labelWidth, labelRect.rect.height);
            if (labelText != null)
            {
                labelText.enableAutoSizing = true;
                labelText.fontSizeMax = labelText.fontSize;
                labelText.fontSizeMin = Mathf.Max(12f, labelText.fontSize * 0.6f);
            }
        }

        // Dropdown: a copy of Quality (same box, list and fonts), without its listeners
        float dropLeft = labelText != null ? left + labelWidth + 12f : left;
        float dropWidth = Mathf.Clamp(right - dropLeft, 170f, qualityRect.rect.width);
        GameObject copy = Instantiate(quality.gameObject, page, false);
        copy.name = "LanguageDropdown";
        var copyRect = (RectTransform)copy.transform;
        PlaceTopLeft(copyRect, dropLeft, centreY + height * 0.5f, dropWidth, height);

        var drop = copy.GetComponent<TMP_Dropdown>();
        drop.onValueChanged = new TMP_Dropdown.DropdownEvent();
        drop.ClearOptions();
        foreach (LanguagePicker stray in copy.GetComponentsInChildren<LanguagePicker>(true))
            if (stray.gameObject != copy) Destroy(stray);
        L10nFont.AttachAll(copy.transform);
        if (labelText != null) L10nFont.Attach(labelText);
        return Attach(drop, labelText);
    }

    static RectTransform FindRect(RectTransform page, string name)
    {
        Transform found = page.Find(name);
        return found as RectTransform;
    }

    // Edges in the page's anchoredPosition space, assuming the builder's top-left anchors and pivot (UIBuild.Place)
    static float Left(RectTransform rect) => rect.anchoredPosition.x - rect.pivot.x * rect.rect.width;
    static float Right(RectTransform rect) => Left(rect) + rect.rect.width;
    static float Top(RectTransform rect) => rect.anchoredPosition.y + (1f - rect.pivot.y) * rect.rect.height;

    static void PlaceTopLeft(RectTransform rect, float left, float top, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(left, top);
    }

    // ------------------------------------------------------------------

    bool wired;

    void Wire()
    {
        if (wired || dropdown == null) return;
        wired = true;
        dropdown.onValueChanged.AddListener(OnPicked);
    }

    void OnEnable()
    {
        Wire();
        L10n.LanguageChanged += Refresh;
        Refresh();
    }

    void OnDisable()
    {
        L10n.LanguageChanged -= Refresh;
    }

    void OnDestroy()
    {
        if (dropdown != null) dropdown.onValueChanged.RemoveListener(OnPicked);
    }

    /// <summary>Fill the list (each language in its own name), select the current one, write the label.</summary>
    public void Refresh()
    {
        if (dropdown == null) return;
        codes.Clear();
        var names = new List<string>();
        foreach (L10n.Language language in L10n.Languages)
        {
            codes.Add(language.Code);
            names.Add(language.NativeName);
        }
        string current = L10n.CurrentCode;
        int index = codes.IndexOf(current);
        if (index < 0 && current == L10n.PseudoCode)
        {
            // The debug pseudo-locale: show it so the picker doesn't claim another language
            codes.Add(L10n.PseudoCode);
            names.Add(L10n.NativeName(L10n.PseudoCode));
            index = codes.Count - 1;
        }

        dropdown.ClearOptions();
        dropdown.AddOptions(names);
        dropdown.SetValueWithoutNotify(Mathf.Max(0, index));
        dropdown.RefreshShownValue();

        if (label != null) label.text = L10n.Get(LabelTable, LabelKey);
    }

    void OnPicked(int index)
    {
        if (index < 0 || index >= codes.Count) return;
        string code = codes[index];
        if (code == L10n.CurrentCode) return;
        L10n.SetLanguage(code);   // LanguageChanged refreshes this picker and every localized text
    }
}
