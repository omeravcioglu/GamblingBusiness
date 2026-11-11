using System;
using TMPro;
using UnityEngine;

/// <summary>
/// Keeps a TMP text on one String Table entry: sets it from L10n.Get(table, key) when enabled and again on every language
/// change, in the language's font (a L10nFont is added next to it).
/// - For UIs built in code: L10nText.Attach(label, "Status", "status.memo.nice"). With placeholders, give an
///   arguments provider that returns the current values each time:
///   L10nText.Attach(label, "Status", "status.memo.to", () => new object[] { L10n.Args(("label", styledLabel)) }).
/// - Designed prefabs can use Unity's LocalizeStringEvent instead (it also previews in Edit mode); see
///   Assets/Localization/README.md.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Localization/Localized Text (L10n)")]
public class L10nText : MonoBehaviour
{
    [Tooltip("String Table collection, e.g. \"Status\" (= Assets/Localization/Source/Status.csv)")]
    public string table;
    [Tooltip("Entry key, e.g. \"status.memo.nice\"")]
    public string key;

    /// <summary>Optional (set from code): the Smart String arguments, asked for on every refresh.</summary>
    public Func<object[]> argsProvider;

    TMP_Text label;

    /// <summary>The TMP text this component writes to.</summary>
    public TMP_Text Label => label != null ? label : (label = GetComponent<TMP_Text>());

    void Awake()
    {
        L10nFont.Attach(Label);
    }

    void OnEnable()
    {
        L10n.LanguageChanged += Refresh;
        Refresh();
    }

    void OnDisable()
    {
        L10n.LanguageChanged -= Refresh;
    }

    /// <summary>Write the text again (after changing the key, or when the provided arguments changed).</summary>
    public void Refresh()
    {
        TMP_Text text = Label;
        if (text == null || string.IsNullOrEmpty(table) || string.IsNullOrEmpty(key)) return;

        object[] args = null;
        if (argsProvider != null)
        {
            try { args = argsProvider(); }
            catch (Exception e) { Debug.LogException(e, this); }
        }
        string value = args != null && args.Length > 0 ? L10n.Get(table, key, args) : L10n.Get(table, key);
        if (text.text != value) text.text = value;
    }

    /// <summary>Point at another entry (and arguments) and write it now.</summary>
    public void Set(string newTable, string newKey, Func<object[]> args = null)
    {
        table = newTable;
        key = newKey;
        argsProvider = args;
        Refresh();
    }

    /// <summary>The text's L10nText (added if it has none), set to this entry and written now.</summary>
    public static L10nText Attach(TMP_Text text, string table, string key, Func<object[]> args = null)
    {
        if (text == null) return null;
        L10nText localized = text.GetComponent<L10nText>();
        if (localized == null) localized = text.gameObject.AddComponent<L10nText>();
        localized.label = text;
        L10nFont.Attach(text);
        localized.Set(table, key, args);
        return localized;
    }
}
