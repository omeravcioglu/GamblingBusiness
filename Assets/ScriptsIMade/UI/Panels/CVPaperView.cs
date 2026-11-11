using System.Collections;
using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The CV paper's face (board "CV direction A: typed résumé"): a world-space canvas on the ApplicantPaper prefab,
/// built by CVPaperBuilder. ApplicantUIBinder.Display calls Show; PaperManager stamps it HIRED / SKIPPED before the
/// paper flies off. The field that fits the active hiring task gets a highlighter mark; golden applicants get the
/// gold rule, drawn stars, a seal and a glow; paper stock and trims follow the company's status tier.
/// </summary>
[DisallowMultipleComponent]
public class CVPaperView : MonoBehaviour
{
    public enum Stamp { None, Hired, Skipped }

    /// <summary>What the active hiring task looks at (the field that gets the highlighter mark).</summary>
    public enum Field { None, Age, Gender, Race, Degree, Major, Gpa, Iq, Experience, Salary, Record, Golden, Trait }

    [System.Serializable]
    public class TraitRow
    {
        public GameObject root;
        public Image icon;
        public TMP_Text label;
    }

    [Header("Sheet")]
    public RectTransform sheet;
    public Image paperFill;
    public Image paperBorder;
    public Image stockTile;
    public Sprite linenTile;
    public Sprite laidTile;
    [Tooltip("Inset ring 4-6 px (tier 5 ink, tier 6 gold)")]
    public Image[] rimOuter;
    [Tooltip("Inset ring 6-7 px (tier 5 gold)")]
    public Image[] rimInner;
    public GameObject goldenGlow;

    [Header("Letterhead (tier 2+)")]
    public GameObject letterhead;
    public Image letterheadBand;
    public Image letterheadLogo;
    public Image letterheadStar;
    public TMP_Text letterheadName;

    [Header("Header")]
    public TMP_Text firstName;
    public TMP_Text lastName;
    public TMP_Text meta;
    public RectTransform stars;
    public Image photo;
    public GameObject photoPlaceholder;
    public Image clip;
    public Image clipHighlight;
    public RectTransform ruleTop;
    public RectTransform ruleBottom;

    [Header("Body")]
    public RectTransform body;
    public RectTransform afterMajor;
    public Image[] headingLines;
    public TMP_Text degree;
    public TMP_Text major;
    public TMP_Text gpa;
    public TMP_Text experienceNumber;
    public TMP_Text experienceUnit;
    public TMP_Text salaryNumber;
    public TMP_Text salaryPer;
    public TraitRow[] traits;
    public Sprite goodIcon;
    public Sprite badIcon;
    public Sprite quirkyIcon;
    public TMP_Text applicantNumber;

    [Header("Margin")]
    public TMP_Text iq;
    public GameObject iqNote;
    [Tooltip("The record stamp's two lines (the entry's two lines: word order differs per language)")]
    public TMP_Text recordTop;
    public TMP_Text recordBottom;
    public Graphic[] recordInk;
    public GameObject recordNote;
    public GameObject seal;
    public GameObject sealNote;

    [Header("Stamps")]
    public GameObject hiredStamp;
    public GameObject skippedStamp;
    public float stampAlpha = 0.88f;
    public float stampSeconds = 0.14f;

    [Header("Layout (set by the builder)")]
    public float nameWidth = 286f;
    public float starsWidth = 80f;
    public float majorWidth = 216f;
    public float majorLineSpacing = -38.5f;
    public float majorTwoLineExtra = 12.8f;
    public float unitGap = 6f;

    static readonly Color Ink = new Color32(0x1D, 0x24, 0x33, 0xFF);
    static readonly Color Gold = new Color32(0xE2, 0xB1, 0x3C, 0xFF);
    static readonly Color GoldDark = new Color32(0x9C, 0x74, 0x18, 0xFF);
    static readonly Color Green = new Color32(0x1E, 0x7B, 0x45, 0xFF);
    static readonly Color Red = new Color32(0xC0, 0x35, 0x2B, 0xFF);
    static readonly Color Blue = new Color32(0x2B, 0x5B, 0xA8, 0xFF);

    /// <summary>String Table of the paper's texts (Assets/Localization/Source/Panels.csv).</summary>
    const string L10nTable = "Panels";

    // Status tiers 0-6 (CVResume renderVals)
    static readonly uint[] Stock = { 0xFFFDF6, 0xFFFFFF, 0xFFFFFF, 0xFCF7EA, 0xFCF7EA, 0xFCF8EE, 0xFCF8EE };
    static readonly uint[] BandBg = { 0x22385C, 0x22385C, 0x22385C, 0x5E1B21, 0x3F2718, 0x1B1B1F, 0xE2B13C };
    static readonly uint[] BandInk = { 0xFFFDF6, 0xFFFDF6, 0xFFFDF6, 0xE2B13C, 0xE3C981, 0xE2B13C, 0x1D2433 };

    static int nextNumber = -1;

    ApplicantData applicant;
    int number;
    int shownTier = -1;
    Field shownField = Field.None;
    string traitTarget;
    bool decided;
    bool fieldForced;
    float pollIn;
    Vector2 bodyBase, afterMajorBase, ruleBottomBase, starsBase;
    bool basesRead;

    public ApplicantData Applicant => applicant;
    public Stamp CurrentStamp { get; private set; }

    // Localization (2026-09-29): write the paper again when the language changes
    void OnEnable()
    {
        L10n.LanguageChanged += OnLanguageChanged;
        L10nFont.AttachAll(this);   // localization: each language's font (English keeps the design font and material)
    }
    void OnDisable() { L10n.LanguageChanged -= OnLanguageChanged; }
    void OnLanguageChanged() { if (applicant != null) ApplyTier(shownTier >= 0 ? shownTier : StatusTier.Current); }

    // ------------------------------------------------------------------

    /// <summary>Fill the paper with an applicant (tier and task highlight follow the running game).</summary>
    public void Show(ApplicantData a, float photoSaturation = 1f)
    {
        if (a == null) return;
        applicant = a;
        if (nextNumber < 0) nextNumber = Random.Range(120, 480);
        number = nextNumber++;
        decided = false;
        fieldForced = false;
        CurrentStamp = Stamp.None;
        SetStampObjects(Stamp.None);
        ApplyPhoto(photoSaturation);
        shownField = ActiveTaskField(out traitTarget);
        ApplyTier(StatusTier.Current);
        pollIn = 0.25f;
    }

    /// <summary>Previews and tests: a fixed tier, highlight, number and stamp.</summary>
    public void ShowPreview(ApplicantData a, int tier, Field highlight, int applicantNo, Stamp stamp = Stamp.None, string trait = null)
    {
        applicant = a;
        number = applicantNo;
        decided = stamp != Stamp.None;
        fieldForced = true;
        CurrentStamp = stamp;
        ApplyPhoto(1f);
        shownField = highlight;
        traitTarget = trait;
        ApplyTier(tier);
        SetStampObjects(stamp);
        if (goldenGlow != null) goldenGlow.SetActive(GlowWanted());
    }

    /// <summary>The decision stamp (PaperManager, right before the paper flies to the tray or the bin).</summary>
    public void ShowStamp(bool hired)
    {
        decided = true;
        CurrentStamp = hired ? Stamp.Hired : Stamp.Skipped;
        // the design drops the highlighter and the glow once the paper is decided
        if (shownField != Field.None)
        {
            shownField = Field.None;
            ApplyTexts();
        }
        if (goldenGlow != null && hired) goldenGlow.SetActive(false);
        SetStampObjects(CurrentStamp);
        GameObject stamp = hired ? hiredStamp : skippedStamp;
        if (stamp != null && Application.isPlaying && isActiveAndEnabled)
            StartCoroutine(Thud(stamp));
    }

    void Update()
    {
        if (applicant == null || decided || fieldForced) return;
        pollIn -= Time.unscaledDeltaTime;
        if (pollIn > 0f) return;
        pollIn = 0.25f;

        if (goldenGlow != null && goldenGlow.activeSelf != GlowWanted())
            goldenGlow.SetActive(!goldenGlow.activeSelf);

        int tier = StatusTier.Current;
        Field field = ActiveTaskField(out string trait);
        if (tier != shownTier)
        {
            shownField = field;
            traitTarget = trait;
            ApplyTier(tier);
        }
        else if (field != shownField || trait != traitTarget)
        {
            shownField = field;
            traitTarget = trait;
            ApplyTexts();
        }
    }

    // The design glows only the CV on top of the pile (a golden applicant further down waits for its turn)
    static GameFlowManager flow;

    bool GlowWanted()
    {
        if (applicant == null || !applicant.isGolden || CurrentStamp == Stamp.Hired) return false;
        if (fieldForced) return true;
        if (flow == null) flow = FindAnyObjectByType<GameFlowManager>();
        return flow == null || ReferenceEquals(flow.CurrentApplicant, applicant);
    }

    // ------------------------------------------------------------------
    // Hiring task

    Field ActiveTaskField(out string trait)
    {
        trait = null;
        HiringTaskManager tasks = HiringTaskManager.Instance;
        if (tasks == null || !tasks.IsTaskActive || tasks.CurrentTask == null || applicant == null) return Field.None;
        HiringTask task = tasks.CurrentTask;
        if (!task.CheckRequirement(applicant)) return Field.None;
        switch (task.requirementType)
        {
            case TaskRequirementType.HireRace: return Field.Race;
            case TaskRequirementType.HireGender: return Field.Gender;
            case TaskRequirementType.HireAgeMin:
            case TaskRequirementType.HireAgeMax:
            case TaskRequirementType.HireAgeRange: return Field.Age;
            case TaskRequirementType.HireDegree: return Field.Degree;
            case TaskRequirementType.HireMajorContains: return Field.Major;
            case TaskRequirementType.HireGPAMin:
            case TaskRequirementType.HireGPAMax: return Field.Gpa;
            case TaskRequirementType.HireIQMin:
            case TaskRequirementType.HireIQMax:
            case TaskRequirementType.HireIQRange: return Field.Iq;
            case TaskRequirementType.HireExperienceMin:
            case TaskRequirementType.HireExperienceMax: return Field.Experience;
            case TaskRequirementType.HireSalaryMin:
            case TaskRequirementType.HireSalaryMax: return Field.Salary;
            case TaskRequirementType.HireWithCriminalRecord:
            case TaskRequirementType.HireWithoutCriminalRecord: return Field.Record;
            case TaskRequirementType.HireGolden: return Field.Golden;
            case TaskRequirementType.HireWithTrait:
                trait = task.targetValue;
                return Field.Trait;
            default: return Field.None;
        }
    }

    // ------------------------------------------------------------------
    // Status tier: paper stock, letterhead band, rims, heading underlines, clip, edge

    void ApplyTier(int tier)
    {
        tier = Mathf.Clamp(tier, 0, StatusTier.Max);
        shownTier = tier;
        bool golden = applicant != null && applicant.isGolden;

        if (paperFill != null) paperFill.color = Hex(Stock[tier]);
        if (stockTile != null)
        {
            Sprite tile = tier == 1 || tier == 2 ? linenTile : (tier == 3 || tier == 4 ? laidTile : null);
            stockTile.sprite = tile;
            stockTile.enabled = tile != null;
        }
        SetRing(rimOuter, tier >= 5, tier == 6 ? Gold : new Color32(0x1B, 0x1B, 0x1F, 0xFF));
        SetRing(rimInner, tier == 5, Gold);
        if (paperBorder != null)
            paperBorder.color = tier == 6 ? (Color)new Color32(0xB8, 0x87, 0x1F, 0xFF) : (golden ? GoldDark : Ink);

        if (letterhead != null)
        {
            letterhead.SetActive(tier >= 2);
            if (letterheadBand != null) letterheadBand.color = Hex(BandBg[tier]);
            if (letterheadLogo != null) letterheadLogo.color = Hex(BandInk[tier]);
            if (letterheadStar != null) letterheadStar.color = Hex(BandBg[tier]);
            if (letterheadName != null)
            {
                letterheadName.color = Hex(BandInk[tier]);
                letterheadName.text = CompanyLetterhead();
            }
        }
        if (headingLines != null)
            foreach (Image line in headingLines)
                if (line != null) line.gameObject.SetActive(tier >= 3);

        if (clip != null) clip.color = tier >= 5 ? GoldDark : (tier >= 2 ? (Color)new Color32(0x8A, 0x6B, 0x25, 0xFF) : (Color)new Color32(0x7E, 0x83, 0x8B, 0xFF));
        if (clipHighlight != null)
        {
            clipHighlight.gameObject.SetActive(tier >= 2);
            clipHighlight.color = tier >= 5 ? (Color)new Color32(0xF7, 0xDC, 0x87, 0xFF) : (Color)new Color32(0xE3, 0xC9, 0x81, 0xFF);
        }
        ApplyTexts();
    }

    static string CompanyLetterhead()
    {
        string name = null;
        try
        {
            name = CompanyNameUI.GetCompanyName();
        }
        catch (System.Exception)
        {
            // no company name system in this scene
        }
        // Localization (2026-09-29): the default "New Company" (also for no name) in the player's language; capitals by the language's rules (Turkish i -> İ)
        return L10n.ToUpper(CompanyNameUI.DisplayName(name));
    }

    static void SetRing(Image[] ring, bool on, Color color)
    {
        if (ring == null) return;
        foreach (Image edge in ring)
        {
            if (edge == null) continue;
            edge.gameObject.SetActive(on);
            edge.color = color;
        }
    }

    // ------------------------------------------------------------------
    // Texts and marks

    void ApplyTexts()
    {
        ReadBases();
        ApplicantData a = applicant;
        if (a == null) return;
        bool golden = a.isGolden;
        Field f = shownField;
        var inv = CultureInfo.InvariantCulture;

        // name: first word large, the rest below; long names shrink (the golden stars need room too)
        string[] words = (a.name ?? "").Trim().Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
        string first = words.Length > 0 ? words[0] : "";
        string last = words.Length > 1 ? string.Join(" ", words, 1, words.Length - 1) : "";
        float firstWidth = Fit(firstName, first, 54f, 30f, nameWidth - (golden ? starsWidth : 0f));
        Fit(lastName, last, 54f, 30f, nameWidth);
        if (stars != null)
        {
            stars.gameObject.SetActive(golden);
            stars.anchoredPosition = new Vector2(starsBase.x + firstWidth, starsBase.y);
        }

        string age = a.age.ToString(inv);
        string gender = ApplicantText.Gender(a.gender);   // localization (2026-09-29): shown translated, the data keeps the English
        string race = ApplicantText.Race(a.race);
        var parts = new System.Collections.Generic.List<string>();
        if (age.Length > 0) parts.Add(Mark(age, f == Field.Age));
        if (gender.Length > 0) parts.Add(Mark(gender, f == Field.Gender));
        if (race.Length > 0) parts.Add(Mark(race, f == Field.Race));
        SetText(meta, string.Join(" · ", parts));

        // rule: gold and twice as thick on a golden applicant, the body moves down with it
        float extraRule = golden ? 4f : 0f;
        if (ruleTop != null)
        {
            ruleTop.sizeDelta = new Vector2(ruleTop.sizeDelta.x, 4f + extraRule);
            ruleTop.anchoredPosition = new Vector2(ruleTop.anchoredPosition.x, -(ruleTopTop + (4f + extraRule) * 0.5f));
            SetColor(ruleTop, golden ? Gold : Ink);
        }
        if (ruleBottom != null)
        {
            ruleBottom.anchoredPosition = ruleBottomBase - new Vector2(0f, extraRule);
            SetColor(ruleBottom, golden ? GoldDark : Ink);
        }
        if (body != null) body.anchoredPosition = bodyBase - new Vector2(0f, extraRule);

        // Localization (2026-09-29): degree and major translated (ApplicantText; "Diploma", "Planned: ...", "Undeclared" included)
        SetText(degree, Mark(ApplicantText.DegreeLine(a), f == Field.Degree));
        string majorShown = ApplicantText.Major(a.major);
        float majorExtra = FitMajor(Mark(majorShown, f == Field.Major), majorShown);
        if (afterMajor != null) afterMajor.anchoredPosition = afterMajorBase - new Vector2(0f, majorExtra);
        SetText(gpa, Mark(a.GPA.ToString("0.00", inv), f == Field.Gpa));

        SetText(experienceNumber, Mark(a.experienceYears.ToString(inv), f == Field.Experience));
        SetText(experienceUnit, Mark(L10n.Get(L10nTable, "cv.experience.unit", ("years", a.experienceYears)), f == Field.Experience));
        PlaceAfter(experienceUnit, experienceNumber);
        SetText(salaryNumber, Mark("$" + a.salary.ToString("N0", inv), f == Field.Salary));
        SetText(salaryPer, Mark(L10n.Get(L10nTable, "cv.salary.unit"), f == Field.Salary));
        PlaceAfter(salaryPer, salaryNumber);

        for (int i = 0; traits != null && i < traits.Length; i++)
        {
            TraitRow row = traits[i];
            if (row == null || row.root == null) continue;
            PersonalityTrait t = a.traits != null && i < a.traits.Count ? a.traits[i] : null;
            bool show = t != null && !string.IsNullOrEmpty(t.traitName);
            row.root.SetActive(show);
            if (!show) continue;
            bool marked = f == Field.Trait && !string.IsNullOrWhiteSpace(traitTarget)
                && t.traitName.IndexOf(traitTarget.Trim(), System.StringComparison.OrdinalIgnoreCase) >= 0;
            SetText(row.label, Mark(ApplicantText.Trait(t.traitName), marked));   // localization (2026-09-29): translated name, matched on the English
            if (row.icon != null)
            {
                row.icon.sprite = t.type == TraitType.Positive ? goodIcon : (t.type == TraitType.Negative ? badIcon : quirkyIcon);
                row.icon.color = t.type == TraitType.Positive ? Green : (t.type == TraitType.Negative ? Red : Blue);
            }
        }

        SetText(applicantNumber, L10n.Get(L10nTable, "cv.applicant_number", ("number", number.ToString("0000", inv))));

        SetText(iq, Mark(a.IQ.ToString(inv), f == Field.Iq));
        if (iqNote != null) iqNote.SetActive(f == Field.Iq);

        Color rec = a.criminalRecord ? Red : Green;
        string[] record = L10n.Get(L10nTable, a.criminalRecord ? "cv.record.has" : "cv.record.clean").Split('\n');
        SetText(recordTop, record[0]);
        SetText(recordBottom, record.Length > 1 ? record[1] : "");
        if (recordInk != null)
            foreach (Graphic g in recordInk)
                if (g != null) g.color = rec;
        if (recordNote != null) recordNote.SetActive(f == Field.Record);

        if (seal != null) seal.SetActive(golden);
        if (sealNote != null) sealNote.SetActive(golden && f == Field.Golden);
        if (goldenGlow != null) goldenGlow.SetActive(GlowWanted());
    }

    float ruleTopTop;

    void ReadBases()
    {
        if (basesRead) return;
        basesRead = true;
        if (body != null) bodyBase = body.anchoredPosition;
        if (afterMajor != null) afterMajorBase = afterMajor.anchoredPosition;
        if (ruleBottom != null) ruleBottomBase = ruleBottom.anchoredPosition;
        if (stars != null) starsBase = stars.anchoredPosition;
        if (ruleTop != null) ruleTopTop = -ruleTop.anchoredPosition.y - ruleTop.sizeDelta.y * 0.5f;
        if (major != null)
        {
            majorBasePos = major.rectTransform.anchoredPosition;
            majorBaseSize = major.rectTransform.sizeDelta;
        }
    }

    static string Mark(string s, bool on)
    {
        if (string.IsNullOrEmpty(s) || !on) return s ?? "";
        return "<link=\"" + TextHighlighter.LinkId + "\">" + s + "</link>";
    }

    static void SetText(TMP_Text t, string s)
    {
        if (t != null && t.text != s) t.text = s;
    }

    static void SetColor(RectTransform rt, Color c)
    {
        var g = rt.GetComponent<Graphic>();
        if (g != null) g.color = c;
    }

    /// <summary>Largest size (max..min, 2 px steps) that fits the width on one line; returns the text's width.</summary>
    static float Fit(TMP_Text t, string s, float max, float min, float width)
    {
        if (t == null) return 0f;
        SetText(t, s);
        float size = max;
        float w = 0f;
        for (; size >= min; size -= 2f)
        {
            t.fontSize = size;
            w = t.GetPreferredValues(s, 10000f, 1000f).x;
            if (w <= width) return w;
        }
        t.fontSize = min;
        return Mathf.Min(w, width);
    }

    /// <summary>Major: 22 px on one line, down to 18 px; longer ones wrap onto a second 18 px line.
    /// Returns how much taller the row got.</summary>
    float FitMajor(string marked, string plain)
    {
        if (major == null) return 0f;
        SetText(major, marked);
        RectTransform rt = major.rectTransform;
        major.textWrappingMode = TextWrappingModes.NoWrap;
        major.lineSpacing = 0f;
        for (float size = 22f; size >= 18f; size -= 1f)
        {
            major.fontSize = size;
            if (major.GetPreferredValues(plain, 10000f, 1000f).x <= majorWidth)
            {
                // the builder's single-line box (grown around the row's centre so TMP fits the font's full height)
                rt.sizeDelta = majorBaseSize;
                rt.anchoredPosition = majorBasePos;
                return 0f;
            }
        }
        // two tighter 18 px lines from the row's top (the builder sets their spacing and the extra height)
        major.fontSize = 18f;
        major.textWrappingMode = TextWrappingModes.Normal;
        major.lineSpacing = L10nFont.SpacingFor(major, majorLineSpacing);   // majorLineSpacing is tuned for the design font
        float h = 28.6f + majorTwoLineExtra;
        rt.sizeDelta = new Vector2(majorBaseSize.x, h);
        rt.anchoredPosition = new Vector2(majorBasePos.x, -(majorRowTop + h * 0.5f));
        return majorTwoLineExtra;
    }

    [Tooltip("Top of the Major row in its parent (set by the builder)")]
    public float majorRowTop;
    Vector2 majorBasePos, majorBaseSize;

    void PlaceAfter(TMP_Text unit, TMP_Text number)
    {
        if (unit == null || number == null) return;
        RectTransform n = number.rectTransform;
        RectTransform u = unit.rectTransform;
        float left = n.anchoredPosition.x - n.sizeDelta.x * 0.5f;
        float w = number.GetPreferredValues(StripTags(number.text), 10000f, 1000f).x;
        u.anchoredPosition = new Vector2(left + w + unitGap + u.sizeDelta.x * 0.5f, u.anchoredPosition.y);
    }

    static string StripTags(string s)
    {
        if (string.IsNullOrEmpty(s) || s.IndexOf('<') < 0) return s;
        var sb = new StringBuilder(s.Length);
        bool inTag = false;
        foreach (char c in s)
        {
            if (c == '<') inTag = true;
            else if (c == '>') inTag = false;
            else if (!inTag) sb.Append(c);
        }
        return sb.ToString();
    }

    void ApplyPhoto(float saturation)
    {
        Sprite p = applicant != null ? applicant.photo : null;
        if (photo != null)
        {
            photo.sprite = p;
            photo.enabled = p != null;
            float grey = Mathf.Lerp(0.5f, 1f, Mathf.Clamp01(saturation));
            photo.color = new Color(grey, grey, grey, 1f);
        }
        if (photoPlaceholder != null) photoPlaceholder.SetActive(p == null);
    }

    // ------------------------------------------------------------------
    // Stamps

    void SetStampObjects(Stamp s)
    {
        if (hiredStamp != null)
        {
            hiredStamp.SetActive(s == Stamp.Hired);
            ResetStamp(hiredStamp);
        }
        if (skippedStamp != null)
        {
            skippedStamp.SetActive(s == Stamp.Skipped);
            ResetStamp(skippedStamp);
        }
    }

    void ResetStamp(GameObject stamp)
    {
        var cg = stamp.GetComponent<CanvasGroup>();
        if (cg != null) cg.alpha = stampAlpha;
        stamp.transform.localScale = Vector3.one;
    }

    IEnumerator Thud(GameObject stamp)
    {
        var cg = stamp.GetComponent<CanvasGroup>();
        Transform t = stamp.transform;
        float time = 0f;
        while (time < stampSeconds)
        {
            time += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(time / stampSeconds);
            float ease = 1f - (1f - k) * (1f - k);
            t.localScale = Vector3.one * Mathf.Lerp(1.35f, 1f, ease);
            if (cg != null) cg.alpha = Mathf.Lerp(0f, stampAlpha, ease);
            yield return null;
        }
        t.localScale = Vector3.one;
        if (cg != null) cg.alpha = stampAlpha;
    }

    static Color Hex(uint rgb)
    {
        return new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
    }
}
