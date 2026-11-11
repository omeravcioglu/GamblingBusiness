using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A small paper chip at the bottom-left of the screen: a thermometer and "Heat: Hot 72", coloured by the level (Cool
/// blue, Warm gold, Hot orange, On fire red), with "· frozen" while the board has frozen gambling. Own overlay canvas
/// (sorting order 70), stacked just above RunEnd's debt chip while that one shows.
/// Shown whenever there is heat; hidden while a pop-up, crisis, risk card, build mode or a run-end screen is up, like
/// the debt chip. With Root empty it is built at runtime; a designed one can fill the references (or a HUD can read
/// HeatMeter and its events and switch this component off, which hides the chip).
/// The label comes from the "Heat" String Table (heat.chip.*) and is written again, in the language's font, when the
/// language changes.
/// </summary>
[DisallowMultipleComponent]
public class HeatChipView : MonoBehaviour
{
    [Header("Look")]
    [Tooltip("Font (empty = the Coming Soon font the HUD uses, else TextMesh Pro's default)")]
    public TMP_FontAsset font;
    [Tooltip("Sorting order of the runtime canvas")]
    public int sortingOrder = 70;
    [Tooltip("Distance from the bottom-left corner of the screen (reference pixels)")]
    public Vector2 margin = new Vector2(24f, 24f);
    [Tooltip("Sit just above RunEnd's debt chip while it shows (runtime chip only; a designed chip stays where it is put)")]
    public bool stackAboveDebtChip = true;
    [Tooltip("Gap above the debt chip (reference pixels)")]
    public float debtChipGap = 10f;

    [Header("Animation (real time)")]
    public bool animate = true;
    [Tooltip("Length of the bump when the heat rises (seconds)")]
    public float bumpSeconds = 0.35f;
    [Tooltip("How much the bump grows the thermometer (0.12 = 12%)")]
    public float bumpScale = 0.12f;
    [Tooltip("How much the bulb swells while Hot or On fire (0.1 = 10%)")]
    public float pulseScale = 0.1f;
    [Tooltip("Pulse speed while Hot (radians per second)")]
    public float hotPulseSpeed = 4f;
    [Tooltip("Pulse speed while On fire (radians per second)")]
    public float onFirePulseSpeed = 7f;

    [Header("Chip (leave Root empty to build it at runtime)")]
    public RectTransform root;
    [Tooltip("\"Heat: Hot 72\" (the level word in the level's colour; \"· frozen\" while gambling is frozen)")]
    public TMP_Text label;
    [Tooltip("The thermometer's fill: its right anchor is set to the heat's share of the maximum")]
    public RectTransform fill;
    [Tooltip("Graphics coloured by the level (a StatusBox gets its fill set, anything else its colour)")]
    public Graphic[] levelTinted;
    [Tooltip("Shown only while the board has frozen gambling (optional)")]
    public GameObject frozenBadge;
    [Tooltip("Bumps when the heat rises (e.g. the thermometer)")]
    public RectTransform bumpTarget;
    [Tooltip("Pulses while Hot or On fire (e.g. the thermometer's bulb)")]
    public RectTransform pulseTarget;

    const float ThermoWidth = 124f, ThermoHeight = 30f, BulbSize = 28f, TubeHeight = 16f, TubeInset = 4f;

    Canvas runtimeCanvas;
    bool builtAtRuntime;
    float shownHeat = -1f;
    int shownLevel = -1;
    bool shownFrozen;
    float bumpAge = 10f;
    bool languageChanged;
    bool labelFollowsLanguage;
    DebtChipView debtChip;
    readonly Vector3[] corners = new Vector3[4];

    void OnEnable()
    {
        L10n.LanguageChanged += OnLanguageChanged;
    }

    void OnDisable()
    {
        L10n.LanguageChanged -= OnLanguageChanged;
        // Switched off (e.g. by a HUD that shows the heat itself): don't leave the chip on screen
        if (root != null) root.gameObject.SetActive(false);
    }

    // Rewrite the label in the new language next frame (its font switches by itself: L10nFont)
    void OnLanguageChanged()
    {
        languageChanged = true;
    }

    void OnDestroy()
    {
        if (runtimeCanvas != null) Destroy(runtimeCanvas.gameObject);
    }

    void Update()
    {
        HeatMeter meter = HeatMeter.Instance;
        // Tutorial: the chip joins the HUD once heat is part of the game (FeatureGate: the first gamble, or heat reaching Warm)
        bool show = meter != null && meter.DisplayHeat > 0 && FeatureGate.IsUnlocked(Feature.Heat) && HudVisible();
        if (show) EnsureBuilt();
        if (root != null && root.gameObject.activeSelf != show) root.gameObject.SetActive(show);
        if (!show) return;

        if (languageChanged || meter.Heat != shownHeat || meter.Level != shownLevel || meter.IsLockedOut != shownFrozen) Refresh(meter);
        if (builtAtRuntime) Place();
        if (animate) Animate(meter);
    }

    /// <summary>Nothing covers the HUD: no panel, crisis, risk card, build mode or run-end screen.</summary>
    static bool HudVisible()
    {
        if (UIManager.Exists && UIManager.Instance.IsAnyPanelOpen()) return false;
        if (MiniGameManager.Instance != null && MiniGameManager.Instance.IsMiniGameActive()) return false;
        if (EventManager.Instance != null && EventManager.Instance.IsEventInProgress()) return false;
        if (BuildModeCamera.Instance != null && BuildModeCamera.Instance.IsInBuildMode()) return false;
        RunEndDirector director = RunEndDirector.Instance;
        if (director != null && (director.IsRunOver || director.IsAnyScreenOpen)) return false;
        return true;
    }

    void Refresh(HeatMeter meter)
    {
        bool rose = shownHeat >= 0f && meter.Heat > shownHeat + 0.001f;
        shownHeat = meter.Heat;
        shownLevel = meter.Level;
        shownFrozen = meter.IsLockedOut;
        languageChanged = false;

        if (label != null) label.text = LabelText(meter.LevelName, shownLevel, meter.DisplayHeat, shownFrozen);

        if (fill != null)
        {
            Vector2 max = fill.anchorMax;
            max.x = meter.Fraction;
            fill.anchorMax = max;
        }

        Color color = HeatMeter.LevelColor(shownLevel);
        if (levelTinted != null)
        {
            foreach (Graphic graphic in levelTinted)
            {
                if (graphic == null) continue;
                if (graphic is StatusBox box) box.Fill = color;
                else graphic.color = color;
            }
        }
        if (frozenBadge != null && frozenBadge.activeSelf != shownFrozen) frozenBadge.SetActive(shownFrozen);
        if (rose) bumpAge = 0f;
    }

    /// <summary>"Heat: Hot 72" with the level word bold in the level's ink, plus "· frozen" in red while gambling is frozen.</summary>
    static string LabelText(string levelName, int level, int heat, bool frozen)
    {
        const string T = HeatMeter.Table;
        string levelWord = "<b><color=" + HeatUI.Hex(HeatMeter.LevelInkColor(level)) + ">" + levelName + "</color></b>";
        string number = heat.ToString(CultureInfo.InvariantCulture);
        if (!frozen) return L10n.Get(T, "heat.chip.label", ("level", levelWord), ("heat", number));
        string frozenWord = "<color=" + HeatUI.Hex(UIColors.Loss) + ">" + L10n.Get(T, "heat.chip.frozen") + "</color>";
        return L10n.Get(T, "heat.chip.label.frozen", ("level", levelWord), ("heat", number), ("frozen", frozenWord));
    }

    // Bottom-left, just above the debt chip while it shows
    void Place()
    {
        float y = margin.y;
        RectTransform debt = stackAboveDebtChip ? DebtChipRoot() : null;
        if (debt != null && debt.gameObject.activeInHierarchy && runtimeCanvas != null)
        {
            debt.GetWorldCorners(corners);
            Canvas debtCanvas = debt.GetComponentInParent<Canvas>();
            Camera cam = debtCanvas != null && debtCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? debtCanvas.worldCamera : null;
            float top = RectTransformUtility.WorldToScreenPoint(cam, corners[1]).y / Mathf.Max(0.0001f, runtimeCanvas.scaleFactor);
            y = Mathf.Max(y, top + debtChipGap);
        }
        Vector2 target = new Vector2(margin.x, y);
        if (root.anchoredPosition != target) root.anchoredPosition = target;
    }

    RectTransform DebtChipRoot()
    {
        if (debtChip == null && RunEndDirector.Instance != null)
            debtChip = RunEndDirector.Instance.debtChipView;
        return debtChip != null ? debtChip.root : null;
    }

    void Animate(HeatMeter meter)
    {
        if (bumpTarget != null)
        {
            bumpAge += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(bumpAge / Mathf.Max(0.01f, bumpSeconds));
            float s = 1f + bumpScale * Mathf.Sin(t * Mathf.PI);
            bumpTarget.localScale = new Vector3(s, s, 1f);
        }
        if (pulseTarget != null)
        {
            float s = 1f;
            if (meter.Level >= HeatMeter.LevelHot)
            {
                float speed = meter.Level >= HeatMeter.LevelOnFire ? onFirePulseSpeed : hotPulseSpeed;
                s = 1f + pulseScale * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * speed));
            }
            pulseTarget.localScale = new Vector3(s, s, 1f);
        }
    }

    // ------------------------------------------------------------------
    // Runtime chip
    // ------------------------------------------------------------------

    void EnsureBuilt()
    {
        if (root == null)
        {
            runtimeCanvas = HeatUI.CreateCanvas("HeatChipCanvas (Runtime)", sortingOrder, transform, false);   // never takes clicks
            Build((RectTransform)runtimeCanvas.transform, HeatUI.ResolveFont(font));
            builtAtRuntime = true;
        }
        // The label (built here or designed) takes the language's font, switched when the language changes
        if (!labelFollowsLanguage && label != null)
        {
            L10nFont.Attach(label);
            labelFollowsLanguage = true;
        }
    }

    void Build(RectTransform parent, TMP_FontAsset useFont)
    {
        // Built inactive; Update shows it
        StatusBox chip = StatusUI.Box(parent, "HeatChip", UIColors.PaperHi, UIColors.Ink, 2f, HeatUI.SmallRadii);
        chip.SetShadow(UIColors.Ink, new Vector2(3f, 4f));
        RectTransform rect = chip.rectTransform;
        rect.gameObject.SetActive(false);
        rect.anchorMin = rect.anchorMax = Vector2.zero;
        rect.pivot = Vector2.zero;
        rect.anchoredPosition = margin;
        root = rect;

        HorizontalLayoutGroup row = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.padding = new RectOffset(14, 18, 9, 11);
        row.spacing = 12f;
        row.childAlignment = TextAnchor.MiddleLeft;
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = false;
        ContentSizeFitter fit = rect.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Thermometer: a bulb with a tube to its right; the fill runs from under the bulb along the tube
        RectTransform thermo = StatusUI.NewRect("Thermometer", rect);
        LayoutElement thermoLayout = thermo.gameObject.AddComponent<LayoutElement>();
        thermoLayout.minWidth = thermoLayout.preferredWidth = ThermoWidth;
        thermoLayout.minHeight = thermoLayout.preferredHeight = ThermoHeight;
        bumpTarget = thermo;

        StatusBox tube = StatusUI.Box(thermo, "Tube", UIColors.PaperShade, UIColors.Ink, 2f, Vector4.one * (TubeHeight * 0.5f));
        RectTransform tubeRect = tube.rectTransform;
        tubeRect.anchorMin = new Vector2(0f, 0.5f);
        tubeRect.anchorMax = new Vector2(1f, 0.5f);
        tubeRect.pivot = new Vector2(0.5f, 0.5f);
        tubeRect.offsetMin = new Vector2(BulbSize * 0.5f, -TubeHeight * 0.5f);
        tubeRect.offsetMax = new Vector2(0f, TubeHeight * 0.5f);

        RectTransform fillArea = StatusUI.NewRect("FillArea", tubeRect);
        StatusUI.Stretch(fillArea, 0f, TubeInset, TubeInset, TubeInset);
        StatusBox fillBox = StatusUI.Box(fillArea, "Fill", HeatMeter.LevelColor(HeatMeter.LevelCool), Color.clear, 0f,
                                         Vector4.one * ((TubeHeight - TubeInset * 2f) * 0.5f));
        RectTransform fillRect = fillBox.rectTransform;
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = new Vector2(0f, 1f);
        fillRect.pivot = new Vector2(0f, 0.5f);
        fillRect.offsetMin = fillRect.offsetMax = Vector2.zero;
        fill = fillRect;

        // Marks where Warm, Hot and On fire start
        HeatMeter meter = HeatMeter.Instance;
        if (meter != null)
        {
            for (int level = HeatMeter.LevelWarm; level <= HeatMeter.LevelOnFire; level++)
            {
                float at = Mathf.Clamp01(meter.GetRule(level).from / meter.MaxHeat);
                RectTransform mark = HeatUI.Block(fillArea, "Mark", UIColors.WithAlpha(UIColors.Ink, 0.45f)).rectTransform;
                mark.anchorMin = new Vector2(at, 0f);
                mark.anchorMax = new Vector2(at, 1f);
                mark.pivot = new Vector2(0.5f, 0.5f);
                mark.sizeDelta = new Vector2(2f, 4f);
                mark.anchoredPosition = Vector2.zero;
            }
        }

        StatusBox bulb = StatusUI.Box(thermo, "Bulb", HeatMeter.LevelColor(HeatMeter.LevelCool), UIColors.Ink, 2f, Vector4.one * (BulbSize * 0.5f));
        RectTransform bulbRect = bulb.rectTransform;
        bulbRect.anchorMin = bulbRect.anchorMax = new Vector2(0f, 0.5f);
        bulbRect.pivot = new Vector2(0.5f, 0.5f);
        bulbRect.sizeDelta = new Vector2(BulbSize, BulbSize);
        bulbRect.anchoredPosition = new Vector2(BulbSize * 0.5f, 0f);
        pulseTarget = bulbRect;

        levelTinted = new Graphic[] { fillBox, bulb };

        string placeholder = L10n.Get(HeatMeter.Table, "heat.chip.label", ("level", HeatMeter.GetLevelName(HeatMeter.LevelCool)), ("heat", "0"));
        TextMeshProUGUI text = StatusUI.Text(rect, "Label", placeholder, 22f, UIColors.Ink, useFont, TextAlignmentOptions.MidlineLeft);
        LayoutElement textLayout = text.gameObject.AddComponent<LayoutElement>();
        textLayout.minHeight = 34f;
        label = text;
    }
}
