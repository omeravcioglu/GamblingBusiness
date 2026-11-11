using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// Fills the 3D CV paper with an applicant's data.
/// With the designed paper face (CVPaperView, built by CVPaperBuilder) Display fills that instead of the text fields.
/// Text fields left empty in the Inspector are found by child name (NameText, AgeText, EducationText, ...).
/// </summary>
public class ApplicantUIBinder : MonoBehaviour
{
    public TextMeshPro nameText;
    public TextMeshPro ageText;
    public TextMeshPro genderText;
    public TextMeshPro raceText;
    [Tooltip("Where the applicant studied (e.g. University). Found by the child name EducationText if left empty.")]
    public TextMeshPro educationText;
    public TextMeshPro degreeText;
    public TextMeshPro majorText;
    public TextMeshPro gpaText;
    public TextMeshPro iqText;
    public TextMeshPro salaryText;
    public TextMeshPro experienceText;
    public TextMeshPro criminalRecordText;
    public TextMeshPro traitsText;

    public SpriteRenderer photoRenderer;

    [Header("Designed CV")]
    [Tooltip("The typed-résumé face (CVPaperBuilder). When set (or found in the children), Display fills it and the text fields below are not used.")]
    public CVPaperView cvView;

    [Header("Golden Applicant")]
    public SpriteRenderer paperRenderer;           // The paper background
    public Sprite normalPaperSprite;               // Normal paper look
    public Sprite goldenPaperSprite;               // Golden paper look
    public Color goldenTextColor = new Color(1f, 0.84f, 0f); // Gold color (#FFD700)
    [Tooltip("Optional child object shown only for golden applicants (particles, glow). Never the paper itself or one of its parents.")]
    public GameObject goldenEffects;

    [Header("Photo Settings")]
    [Range(0f, 1f)]
    [Tooltip("Controls photo saturation. 1 = full color, 0.7 = slightly desaturated, 0.5 = moderate, 0 = grayscale")]
    public float photoSaturation = 0.7f;          // Default: slightly desaturated

    /// <summary>The applicant currently shown on this paper.</summary>
    public ApplicantData Applicant { get; private set; }

    // Original color of each text, restored for normal applicants
    private readonly Dictionary<TextMeshPro, Color> normalTextColors = new Dictionary<TextMeshPro, Color>();
    private TextMeshPro[] childTexts;
    private bool initialized;

    private static bool warnedGoldenEffects;
    private static bool warnedMissingPhoto;

    void Awake()
    {
        EnsureInitialized();
    }

    void EnsureInitialized()
    {
        if (initialized) return;
        initialized = true;

        if (cvView == null) cvView = GetComponentInChildren<CVPaperView>(true);
        AutoFindReferences();
        FixMajorLabel();

        // Store normal text colors
        foreach (TextMeshPro t in AllTexts())
        {
            if (t != null && !normalTextColors.ContainsKey(t))
                normalTextColors[t] = t.color;
        }
    }

    public void Display(ApplicantData a)
    {
        EnsureInitialized();

        if (a == null)
        {
            Debug.LogWarning("ApplicantUIBinder: Display was called without an applicant.");
            return;
        }
        Applicant = a;

        if (cvView != null)
        {
            cvView.Show(a, photoSaturation);
            if (DemoMode.Active) DemoCvProfitTag.Show(this, a);   // Demo: the "+$X/day" line under the salary
            return;
        }

        // Apply golden styling if rare applicant
        if (a.isGolden)
        {
            ApplyGoldenStyle();
        }
        else
        {
            ApplyNormalStyle();
        }

        // Plain text only: the CV font has no star/emoji glyphs
        SetText(nameText, a.isGolden ? a.name + " (GOLDEN)" : a.name);
        SetText(ageText, a.age.ToString(CultureInfo.InvariantCulture));
        // Fields in the current language (ApplicantText); the data keeps the English
        SetText(genderText, ApplicantText.Gender(a.gender));
        SetText(raceText, ApplicantText.Race(a.race));
        SetText(educationText, ApplicantText.Education(a));
        SetText(degreeText, ApplicantText.DegreeLine(a));
        SetText(majorText, ApplicantText.Major(a.major));
        SetText(gpaText, a.GPA.ToString("0.00", CultureInfo.InvariantCulture));
        SetText(iqText, a.IQ.ToString(CultureInfo.InvariantCulture));
        SetText(salaryText, "$" + a.salary.ToString("N0", CultureInfo.InvariantCulture));
        SetText(experienceText, a.experienceYears == 1 ? "1 yr" : a.experienceYears + " yrs");
        SetText(criminalRecordText, a.criminalRecord ? "YES" : "NO");
        if (DemoMode.Active) DemoCvProfitTag.Show(this, a);   // Demo: the "+$X/day" line under the salary

        // Photo
        if (photoRenderer != null)
        {
            if (a.photo != null)
            {
                photoRenderer.sprite = a.photo;
                photoRenderer.enabled = true;

                // Apply desaturation to photo
                // Lower saturation value = more desaturated (closer to grayscale)
                // photoSaturation 0.7 = slightly desaturated, 0.5 = moderate, 0 = grayscale
                if (photoSaturation < 1f)
                {
                    // Interpolate between grayscale and full color
                    // SpriteRenderer color multiplies with sprite, so gray tint = desaturation
                    float grayValue = Mathf.Lerp(0.5f, 1f, photoSaturation); // 0.5 = gray, 1.0 = white
                    photoRenderer.color = new Color(grayValue, grayValue, grayValue, 1f);
                }
                else
                {
                    // Full saturation - use white (original colors, no tint)
                    photoRenderer.color = Color.white;
                }
            }
            else
            {
                photoRenderer.enabled = false;
                if (!warnedMissingPhoto)
                {
                    warnedMissingPhoto = true;
                    Debug.LogWarning("ApplicantUIBinder: No photo for applicant " + a.name + " (further missing-photo warnings are hidden).");
                }
            }
        }

        // Traits
        if (traitsText != null)
        {
            StringBuilder sb = new StringBuilder();
            if (a.traits != null)
            {
                foreach (var t in a.traits)
                {
                    if (t != null && !string.IsNullOrEmpty(t.traitName))
                        sb.Append("- ").Append(ApplicantText.Trait(t.traitName)).Append('\n');
                }
            }
            traitsText.text = sb.ToString();
        }
    }

    void ApplyGoldenStyle()
    {
        // Change paper to golden
        if (paperRenderer != null && goldenPaperSprite != null)
            paperRenderer.sprite = goldenPaperSprite;

        // Change all text to gold color
        foreach (TextMeshPro t in AllTexts())
        {
            if (t != null) t.color = goldenTextColor;
        }

        // Enable golden effects
        if (CanToggleGoldenEffects())
            goldenEffects.SetActive(true);
    }

    void ApplyNormalStyle()
    {
        // Change paper to normal
        if (paperRenderer != null && normalPaperSprite != null)
            paperRenderer.sprite = normalPaperSprite;

        // Reset text colors
        foreach (TextMeshPro t in AllTexts())
        {
            if (t != null && normalTextColors.TryGetValue(t, out Color c))
                t.color = c;
        }

        // Disable golden effects
        if (CanToggleGoldenEffects())
            goldenEffects.SetActive(false);
    }

    // goldenEffects must be a separate child: turning off the paper itself (or a parent) would hide the whole CV
    bool CanToggleGoldenEffects()
    {
        if (goldenEffects == null) return false;

        if (transform.IsChildOf(goldenEffects.transform))
        {
            if (!warnedGoldenEffects)
            {
                warnedGoldenEffects = true;
                Debug.LogWarning("ApplicantUIBinder: 'goldenEffects' points at the paper itself or one of its parents, so it is ignored. Assign a separate child object (or leave it empty).", this);
            }
            return false;
        }
        return true;
    }

    IEnumerable<TextMeshPro> AllTexts()
    {
        yield return nameText;
        yield return ageText;
        yield return genderText;
        yield return raceText;
        yield return educationText;
        yield return degreeText;
        yield return majorText;
        yield return gpaText;
        yield return iqText;
        yield return salaryText;
        yield return experienceText;
        yield return criminalRecordText;
        yield return traitsText;
    }

    static void SetText(TextMeshPro target, string value)
    {
        if (target != null)
            target.text = value ?? "";
    }

    // ------------------------------------------------------------------
    // Auto-wiring (fields left empty in the Inspector)
    // ------------------------------------------------------------------

    void AutoFindReferences()
    {
        if (nameText == null) nameText = FindText("NameText");
        if (ageText == null) ageText = FindText("AgeText");
        if (genderText == null) genderText = FindText("GenderText");
        if (raceText == null) raceText = FindText("RaceText");
        if (educationText == null) educationText = FindText("EducationText");
        if (degreeText == null) degreeText = FindText("DegreeText");
        if (majorText == null) majorText = FindText("MajorText");
        if (gpaText == null) gpaText = FindText("GpaText", "Gpatext", "GPAText");
        if (iqText == null) iqText = FindText("IQText");
        if (salaryText == null) salaryText = FindText("SalaryText");
        if (experienceText == null) experienceText = FindText("ExperienceText");
        if (criminalRecordText == null) criminalRecordText = FindText("CriminalText", "CriminalRecordText");
        if (traitsText == null) traitsText = FindText("TraitsText");

        if (photoRenderer == null)
        {
            foreach (SpriteRenderer sr in GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (sr != paperRenderer && (sr.name == "Picture" || sr.name == "Photo"))
                {
                    photoRenderer = sr;
                    break;
                }
            }
        }
    }

    TextMeshPro FindText(params string[] names)
    {
        if (childTexts == null)
            childTexts = GetComponentsInChildren<TextMeshPro>(true);

        foreach (string n in names)
        {
            foreach (TextMeshPro t in childTexts)
            {
                if (t != null && string.Equals(t.name, n, System.StringComparison.OrdinalIgnoreCase))
                    return t;
            }
        }
        return null;
    }

    // The prefab's "Major" label was copied from the GPA label and still reads "GPA:"
    void FixMajorLabel()
    {
        if (childTexts == null)
            childTexts = GetComponentsInChildren<TextMeshPro>(true);

        foreach (TextMeshPro t in childTexts)
        {
            if (t == null || t == majorText || t == gpaText || t.name != "Major") continue;
            if (t.text != null && t.text.Trim() == "GPA:")
                t.text = "Major:";
        }
    }
}
