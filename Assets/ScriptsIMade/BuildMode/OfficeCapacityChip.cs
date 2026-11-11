using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The office's capacity on the HUD (the owner's decision, 2026-09-29: capacity is a hard limit on hiring and it must be
/// visible at all times). A paper chip above the desk's Skip / Hire buttons: "EMPLOYEES 17 / 25". It bumps when a number
/// changes and floats the difference ("+10 places" after a build, "+1" after a hire). When every place is taken it turns
/// into the warning banner: "OFFICE FULL · 25 / 25 employees", "Build or upgrade office space to hire more." and a Build (Q)
/// button that opens build mode; it shakes when the desk refuses a hire (GameFlowManager.HireRefused).
/// - Reads OfficeCapacity (TowerBuilder provides the number). Hidden while there is no limit, and while a pop-up, crisis,
///   risk card, build mode (the tower's top bar shows capacity there) or a run-end screen covers the HUD.
/// - Own overlay canvas (sorting order 71, takes clicks for its button). With Root empty it is built at runtime in the HUD's
///   stationery style; a designed one can fill the references (the UI session's version), or a HUD can read OfficeCapacity
///   and switch this component off, which hides the chip.
/// - Words from the "Tower" String Table (tower.capacity.*), written again, in the language's font, when the language changes.
/// </summary>
[DisallowMultipleComponent]
public class OfficeCapacityChip : MonoBehaviour
{
    [Header("Look")]
    [Tooltip("Font (empty = the Coming Soon font the HUD uses, else TextMesh Pro's default)")]
    public TMP_FontAsset font;
    [Tooltip("Sorting order of the runtime canvas (the heat, debt and board chips use 70)")]
    public int sortingOrder = 71;
    [Tooltip("Where the chip's bottom edge sits: this far above the bottom of the screen (the desk's Skip / Hire buttons end at 136)")]
    public float bottom = 146f;

    [Header("Animation (real time)")]
    public bool animate = true;
    public float bumpSeconds = 0.4f;
    public float bumpScale = 0.18f;
    public float floatSeconds = 1.4f;
    public float shakeSeconds = 0.45f;

    [Header("Chip (leave Root empty to build it at runtime)")]
    public RectTransform root;
    public StatusBox box;
    public TowerIcon icon;
    [Tooltip("\"EMPLOYEES\" / \"OFFICE FULL\"")]
    public TMP_Text titleText;
    [Tooltip("\"17 / 25\" (bumps on a change)")]
    public TMP_Text countText;
    [Tooltip("\"Build or upgrade office space to hire more.\" (only while full)")]
    public TMP_Text bodyText;
    [Tooltip("Build (Q): opens build mode (only while full)")]
    public Button buildButton;
    [Tooltip("\"+10\" rising from the chip after a change")]
    public TMP_Text floatText;

    Canvas runtimeCanvas;
    bool builtAtRuntime;
    bool languageChanged;
    bool dirty = true;
    int shownEmployees = -1, shownCapacity = -1;
    bool shownFull;
    float bumpAge = 10f, floatAge = 10f, shakeAge = 10f;
    Vector2 home;

    void OnEnable()
    {
        L10n.LanguageChanged += OnLanguageChanged;
        OfficeCapacity.Changed += OnCapacityChanged;
        GameFlowManager.HireRefused += OnHireRefused;
        dirty = true;
    }

    void OnDisable()
    {
        L10n.LanguageChanged -= OnLanguageChanged;
        OfficeCapacity.Changed -= OnCapacityChanged;
        GameFlowManager.HireRefused -= OnHireRefused;
        if (root != null) root.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (runtimeCanvas != null) Destroy(runtimeCanvas.gameObject);
    }

    void OnLanguageChanged()
    {
        languageChanged = true;
    }

    void OnCapacityChanged()
    {
        dirty = true;
    }

    void OnHireRefused()
    {
        shakeAge = 0f;
        bumpAge = 0f;
    }

    void Update()
    {
        bool show = OfficeCapacity.IsLimited && CompanyManager.Instance != null && HudVisible();
        if (show) EnsureBuilt();
        if (root != null && root.gameObject.activeSelf != show) root.gameObject.SetActive(show);
        if (!show) return;

        int employees = OfficeCapacity.Employees, capacity = OfficeCapacity.Capacity;
        if (dirty || languageChanged || employees != shownEmployees || capacity != shownCapacity) Refresh(employees, capacity);
        if (animate) Animate();
    }

    /// <summary>Nothing covers the HUD: no panel, crisis, risk card, build mode or run-end screen.</summary>
    static bool HudVisible()
    {
        if (UIManager.Exists && UIManager.Instance.IsAnyPanelOpen()) return false;
        if (MiniGameManager.Instance != null && MiniGameManager.Instance.IsMiniGameActive()) return false;
        if (EventManager.Instance != null && EventManager.Instance.IsEventInProgress()) return false;
        if (BuildModeCamera.Instance != null && BuildModeCamera.Instance.IsInBuildMode()) return false;
        if (TowerView.Instance != null && TowerView.Instance.IsShowing) return false;
        RunEndDirector director = RunEndDirector.Instance;
        if (director != null && (director.IsRunOver || director.IsAnyScreenOpen)) return false;
        return true;
    }

    void Refresh(int employees, int capacity)
    {
        bool first = shownCapacity < 0;
        int addedPlaces = first ? 0 : capacity - shownCapacity;
        int addedPeople = first ? 0 : employees - shownEmployees;
        bool full = employees >= capacity;
        bool changedLook = full != shownFull || first || languageChanged;
        shownEmployees = employees;
        shownCapacity = capacity;
        shownFull = full;
        dirty = false;
        languageChanged = false;

        string count = TowerText.EmployeesOf(employees, capacity);
        if (countText != null) countText.text = full ? TowerText.Get("tower.capacity.full.count", ("count", count)) : count;
        if (changedLook)
        {
            if (titleText != null) titleText.text = TowerText.Get(full ? "tower.capacity.full.title" : "tower.capacity.chip.label");
            if (bodyText != null)
            {
                bodyText.text = TowerText.Get("tower.capacity.full.body");
                bodyText.gameObject.SetActive(full);
            }
            if (buildButton != null) buildButton.gameObject.SetActive(full);
            if (box != null)
            {
                box.Fill = full ? UIColors.LossSoft : UIColors.PaperHi;
                box.Border = full ? UIColors.Loss : UIColors.Ink;
                box.BorderWidth = full ? 3f : 2f;
            }
            if (icon != null)
            {
                icon.Kind = full ? TowerIconKind.Warning : TowerIconKind.People;
                icon.SetColor(full ? UIColors.Loss : UIColors.Ink);
            }
            if (titleText != null) titleText.color = full ? UIColors.Loss : UIColors.InkSoft;
            if (root != null) LayoutRebuilder.ForceRebuildLayoutImmediate(root);
        }
        if (addedPlaces != 0 || addedPeople != 0)
        {
            bumpAge = 0f;
            if (floatText != null)
            {
                floatText.text = addedPlaces > 0 ? TowerText.Get("tower.capacity.float.places", ("count", addedPlaces))
                               : addedPlaces < 0 ? TowerText.Get("tower.capacity.float.places.minus", ("count", -addedPlaces))
                               : (addedPeople > 0 ? "+" : UIFormat.Minus) + Mathf.Abs(addedPeople).ToString(CultureInfo.InvariantCulture);
                floatText.color = addedPlaces > 0 ? UIColors.Money : addedPlaces < 0 ? UIColors.Loss : UIColors.Ink;
                floatAge = 0f;
            }
        }
    }

    void Animate()
    {
        float dt = Time.unscaledDeltaTime;
        if (countText != null)
        {
            bumpAge += dt;
            float t = Mathf.Clamp01(bumpAge / Mathf.Max(0.01f, bumpSeconds));
            float s = 1f + bumpScale * Mathf.Sin(t * Mathf.PI);
            countText.rectTransform.localScale = new Vector3(s, s, 1f);
        }
        if (floatText != null)
        {
            floatAge += dt;
            float t = Mathf.Clamp01(floatAge / Mathf.Max(0.01f, floatSeconds));
            bool on = t < 1f;
            if (floatText.gameObject.activeSelf != on) floatText.gameObject.SetActive(on);
            if (on)
            {
                floatText.rectTransform.anchoredPosition = new Vector2(0f, 8f + 40f * (1f - (1f - t) * (1f - t)));
                Color c = floatText.color;
                c.a = t > 0.7f ? 1f - (t - 0.7f) / 0.3f : 1f;
                floatText.color = c;
            }
        }
        if (root != null && builtAtRuntime)
        {
            shakeAge += dt;
            float t = Mathf.Clamp01(shakeAge / Mathf.Max(0.01f, shakeSeconds));
            float dx = t < 1f ? Mathf.Sin(t * Mathf.PI * 8f) * 10f * (1f - t) : 0f;
            Vector2 target = home + new Vector2(dx, 0f);
            if (root.anchoredPosition != target) root.anchoredPosition = target;
        }
    }

    /// <summary>The banner's button: to build mode (the Build tab's rules: the tutorial may still lock it).</summary>
    void OpenBuildMode()
    {
        if (!UIManager.GameplayInputAllowed()) return;
        BuildModeCamera camera = BuildModeCamera.Instance;
        if (camera == null || !camera.TryEnterBuildMode()) return;
        // Demo: open on the fix (the add-floor slip when the HQ has no spot for an office, else the Offices catalog),
        // once the first-visit note has had its turn
        TowerBuilder builder = TowerBuilder.Instance;
        if (DemoMode.Active && builder != null && builder.IntroSeen)
            ManagerBuildPerks.RaiseBuildModeRequested(DemoTowerRules.NeedsFloor(builder.Plan) ? "expand" : "office");
    }

    // ------------------------------------------------------------------
    // Runtime chip
    // ------------------------------------------------------------------

    void EnsureBuilt()
    {
        if (root != null) return;
        runtimeCanvas = HeatUI.CreateCanvas("CapacityChipCanvas (Runtime)", sortingOrder, transform, true);
        Build((RectTransform)runtimeCanvas.transform, HeatUI.ResolveFont(font));
        builtAtRuntime = true;
        L10nFont.AttachAll(root);
        if (buildButton != null) buildButton.onClick.AddListener(OpenBuildMode);
    }

    void Build(RectTransform parent, TMP_FontAsset useFont)
    {
        // Built inactive; Update shows it. Bottom centre, above the desk's buttons.
        StatusBox chip = StatusUI.Box(parent, "CapacityChip", UIColors.PaperHi, UIColors.Ink, 2f, HeatUI.SmallRadii);
        chip.SetShadow(UIColors.Ink, new Vector2(3f, 4f));
        RectTransform rect = chip.rectTransform;
        rect.gameObject.SetActive(false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        home = new Vector2(0f, bottom);
        rect.anchoredPosition = home;
        root = rect;
        box = chip;

        HorizontalLayoutGroup row = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.padding = new RectOffset(14, 16, 7, 9);
        row.spacing = 10f;
        row.childAlignment = TextAnchor.MiddleCenter;
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = false;
        ContentSizeFitter fit = rect.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        RectTransform iconRect = StatusUI.NewRect("Icon", rect);
        icon = iconRect.gameObject.AddComponent<TowerIcon>();
        icon.Kind = TowerIconKind.People;
        icon.color = UIColors.Ink;
        icon.raycastTarget = false;
        LayoutElement iconLayout = iconRect.gameObject.AddComponent<LayoutElement>();
        iconLayout.minWidth = iconLayout.preferredWidth = 26f;
        iconLayout.minHeight = iconLayout.preferredHeight = 26f;

        // Title and count on one line; the body under them while full
        RectTransform texts = StatusUI.NewRect("Texts", rect);
        VerticalLayoutGroup column = texts.gameObject.AddComponent<VerticalLayoutGroup>();
        column.spacing = 0f;
        column.childAlignment = TextAnchor.MiddleLeft;
        column.childControlWidth = true;
        column.childControlHeight = true;
        column.childForceExpandWidth = false;
        column.childForceExpandHeight = false;
        RectTransform line = StatusUI.NewRect("Line", texts);
        HorizontalLayoutGroup lineRow = line.gameObject.AddComponent<HorizontalLayoutGroup>();
        lineRow.spacing = 10f;
        lineRow.childAlignment = TextAnchor.MiddleLeft;
        lineRow.childControlWidth = true;
        lineRow.childControlHeight = true;
        lineRow.childForceExpandWidth = false;
        lineRow.childForceExpandHeight = false;
        TextMeshProUGUI title = StatusUI.Text(line, "Title", "", 17f, UIColors.InkSoft, useFont, TextAlignmentOptions.MidlineLeft, true);
        title.characterSpacing = 8f;   // spaced capitals (written as capitals in the String Table)
        titleText = title;
        TextMeshProUGUI count = StatusUI.Text(line, "Count", "", 24f, UIColors.Ink, useFont, TextAlignmentOptions.MidlineLeft, true);
        countText = count;
        TextMeshProUGUI body = StatusUI.Text(texts, "Body", "", 18f, UIColors.Ink, useFont, TextAlignmentOptions.MidlineLeft);
        bodyText = body;
        body.gameObject.SetActive(false);

        // Build (Q) while full: the banner's way out
        StatusBox button = StatusUI.Box(rect, "Build", UIColors.Highlight, UIColors.Ink, 2f, HeatUI.SmallRadii);
        button.SetShadow(UIColors.Ink, new Vector2(2f, 3f));
        button.raycastTarget = true;
        HorizontalLayoutGroup buttonRow = button.gameObject.AddComponent<HorizontalLayoutGroup>();
        buttonRow.padding = new RectOffset(10, 12, 4, 6);
        buttonRow.spacing = 6f;
        buttonRow.childAlignment = TextAnchor.MiddleCenter;
        buttonRow.childControlWidth = true;
        buttonRow.childControlHeight = true;
        buttonRow.childForceExpandWidth = false;
        buttonRow.childForceExpandHeight = false;
        RectTransform buildIcon = StatusUI.NewRect("Icon", button.rectTransform);
        TowerIcon glyph = buildIcon.gameObject.AddComponent<TowerIcon>();
        glyph.Kind = TowerIconKind.Building;
        glyph.color = UIColors.Ink;
        glyph.raycastTarget = false;
        LayoutElement glyphLayout = buildIcon.gameObject.AddComponent<LayoutElement>();
        glyphLayout.minWidth = glyphLayout.preferredWidth = 22f;
        glyphLayout.minHeight = glyphLayout.preferredHeight = 22f;
        TextMeshProUGUI label = StatusUI.Text(button.rectTransform, "Label", "", 19f, UIColors.Ink, useFont, TextAlignmentOptions.Center, true);
        L10nText.Attach(label, TowerText.Table, "tower.capacity.full.button");
        buildButton = button.gameObject.AddComponent<Button>();
        buildButton.targetGraphic = button;
        buildButton.navigation = new Navigation { mode = Navigation.Mode.None };
        button.gameObject.SetActive(false);

        // "+10 places" floating up from the chip's top edge
        TextMeshProUGUI floater = StatusUI.Text(rect, "Float", "", 22f, UIColors.Money, useFont, TextAlignmentOptions.Center, true);
        LayoutElement floatLayout = floater.gameObject.AddComponent<LayoutElement>();
        floatLayout.ignoreLayout = true;
        RectTransform floatRect = floater.rectTransform;
        floatRect.anchorMin = floatRect.anchorMax = new Vector2(0.5f, 1f);
        floatRect.pivot = new Vector2(0.5f, 0f);
        floatRect.sizeDelta = new Vector2(240f, 30f);
        floatText = floater;
        floater.gameObject.SetActive(false);
    }
}
