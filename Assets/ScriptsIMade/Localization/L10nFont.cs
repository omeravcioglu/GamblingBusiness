using TMPro;
using UnityEngine;
using UnityEngine.TextCore;

/// <summary>
/// Keeps a TMP text on the current language's font (L10n.MainFont) and switches it when the language changes.
/// - While the language uses the English font (English and Pseudo) the text keeps its design font and material (glow,
///   outline and other presets) exactly as built. Other languages get their font with its default material, or in Play
///   mode a copy of a designed preset on their atlas (as TextMesh Pro draws fallback fonts).
/// - Switching fonts keeps the line pitch: line spacing is tuned for a font's own line height (UIBuild's CSS line
///   heights), and the language fonts' lines are shorter than Coming Soon's (1.18-1.45 em against 1.54).
/// - For UIs built in code: L10nFont.Attach(label), or L10nFont.AttachAll(screenRoot) once a screen is built.
///   L10nText adds one by itself.
/// - Named L10nFont (not LocalizedFont) so it never clashes with UnityEngine.Localization.LocalizedFont.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Localization/Localized Font (L10n)")]
public class L10nFont : MonoBehaviour
{
    [Tooltip("The font the text is designed with (empty = its font when this component starts); kept while the language uses the same face")]
    public TMP_FontAsset designFont;
    [Tooltip("The material the text is designed with (empty = its material when this component starts); restored with the design font")]
    public Material designMaterial;

    TMP_Text label;

    void Awake()
    {
        Capture();
    }

    void OnEnable()
    {
        L10n.LanguageChanged += Apply;
        Apply();
    }

    void OnDisable()
    {
        L10n.LanguageChanged -= Apply;
    }

    /// <summary>Remember the text's design font and material (only while it still shows the design font).</summary>
    void Capture()
    {
        if (label == null) label = GetComponent<TMP_Text>();
        if (label == null) return;
        if (designFont == null) designFont = label.font;
        if (designMaterial == null && designFont != null && label.font == designFont) designMaterial = label.fontSharedMaterial;
    }

    /// <summary>Put the text on the current language's font now (also called on every language change).</summary>
    public void Apply()
    {
        Capture();
        if (label == null) return;
        TMP_FontAsset target = L10n.ResolveFont(designFont);
        if (target == null) return;

        TMP_FontAsset before = label.font;
        if (target == designFont)
        {
            if (label.font != designFont) label.font = designFont;
            if (designMaterial != null && label.fontSharedMaterial != designMaterial) label.fontSharedMaterial = designMaterial;
        }
        else
        {
            if (label.font != target) label.font = target;   // TMP switches to the font's own material
            Material look = LookFor(target);
            if (look != null && label.fontSharedMaterial != look) label.fontSharedMaterial = look;
        }

        // Only at the switch, so spacing a view sets for the font on screen afterwards stays as it is
        if (before != null && label.font != null && before != label.font)
            label.lineSpacing += (NaturalLineHeight(before) - NaturalLineHeight(label.font)) * 100f;
    }

    /// <summary>
    /// Lets edit-mode previews (UIPreview) show designed presets in other languages. Otherwise only Play mode does:
    /// a builder would save the generated material into its prefab.
    /// </summary>
    public static bool PreviewLooks;

    /// <summary>
    /// The designed preset (outline, glow, underlay) on the language font's atlas, the way TextMesh Pro draws fallback
    /// fonts. Null when the text uses its design font's own material: the language font's own material is right then.
    /// </summary>
    Material LookFor(TMP_FontAsset target)
    {
        if (!Application.isPlaying && !PreviewLooks) return null;
        if (designMaterial == null || designFont == null || target == null || target.material == null) return null;
        if (designMaterial == designFont.material) return null;
        return TMP_MaterialManager.GetFallbackMaterial(designMaterial, target.material);
    }

    /// <summary>
    /// A line spacing written for a text's design font, converted to the font it shows now (the same line pitch). For
    /// views that set spacing themselves after the language's font went on.
    /// </summary>
    public static float SpacingFor(TMP_Text text, float designSpacing)
    {
        if (text == null || text.font == null) return designSpacing;
        L10nFont localized = text.GetComponent<L10nFont>();
        TMP_FontAsset design = localized != null && localized.designFont != null ? localized.designFont : text.font;
        return designSpacing + (NaturalLineHeight(design) - NaturalLineHeight(text.font)) * 100f;
    }

    /// <summary>A font's own line height in em (TMP line spacing is added on top of it, in hundredths of an em).</summary>
    static float NaturalLineHeight(TMP_FontAsset font)
    {
        FaceInfo face = font.faceInfo;
        return face.pointSize > 0f ? face.lineHeight * face.scale / face.pointSize : 1.2f;
    }

    /// <summary>The text's L10nFont, added if it has none; the design font and material are taken from the text now.</summary>
    public static L10nFont Attach(TMP_Text text)
    {
        if (text == null) return null;
        L10nFont localized = text.GetComponent<L10nFont>();
        if (localized == null)
        {
            localized = text.gameObject.AddComponent<L10nFont>();
            // Taken right away: a component added to an inactive object only wakes up when the object is shown
            localized.label = text;
            if (localized.designFont == null) localized.designFont = text.font;
            if (localized.designMaterial == null && text.font == localized.designFont) localized.designMaterial = text.fontSharedMaterial;
        }
        if (localized.isActiveAndEnabled) localized.Apply();
        return localized;
    }

    /// <summary>A L10nFont on every TMP text under <paramref name="root"/>, inactive ones included.</summary>
    public static void AttachAll(Component root)
    {
        if (root == null) return;
        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
            Attach(text);
    }
}
