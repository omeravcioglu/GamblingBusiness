using TMPro;
using UnityEngine;

/// <summary>
/// The demo's day chip, top centre under the HUD's tabs: "DEMO · DAY 4 / 12" (12 = the Board verdict that ends the demo),
/// in highlighter yellow from two days before the end, and "DEMO · BONUS DAYS" after Keep playing. Own overlay canvas
/// (sorting 86) without a raycaster, so it never takes clicks; hidden while a pop-up, crisis, risk card, build mode, a
/// run-end screen or a HUD tooltip covers the HUD (like the heat chip). DemoDirector adds it in demo mode only.
/// </summary>
[DisallowMultipleComponent]
public class DemoHudChip : MonoBehaviour
{
    [Tooltip("Font (empty = the Coming Soon font the HUD uses, else TextMesh Pro's default)")]
    public TMP_FontAsset font;
    public int sortingOrder = 86;
    [Tooltip("Distance from the top of the screen (reference pixels): just under the hanging tabs")]
    public float top = 98f;

    Canvas runtimeCanvas;
    RectTransform root;
    BlueprintBox chip;
    TextMeshProUGUI label;
    int shownDay = -1, shownEnd = -1;
    bool shownBonus, shownHot, languageChanged;
    HudTooltip tooltip;
    float nextTooltipLookup;

    void OnEnable()
    {
        L10n.LanguageChanged += OnLanguageChanged;
    }

    void OnDisable()
    {
        L10n.LanguageChanged -= OnLanguageChanged;
        if (root != null) root.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (runtimeCanvas != null) Destroy(runtimeCanvas.gameObject);
    }

    // Rewrite the label in the new language next frame (its font switches by itself: L10nFont)
    void OnLanguageChanged()
    {
        languageChanged = true;
    }

    void Update()
    {
        CompanyManager company = CompanyManager.Instance;
        bool show = DemoMode.Active && company != null && HudVisible();
        if (show && root == null) Build(RunEndUI.ResolveFont(font));
        if (root != null && root.gameObject.activeSelf != show) root.gameObject.SetActive(show);
        if (!show) return;

        int day = Mathf.Max(1, company.currentDay);
        int end = FinaleMeetingDay();
        bool bonus = DemoRun.KeepPlaying;
        bool hot = !bonus && day >= end - 2;
        if (languageChanged || day != shownDay || end != shownEnd || bonus != shownBonus || hot != shownHot)
            Refresh(day, end, bonus, hot);
    }

    /// <summary>The verdict's day: DemoMode.FinaleDay, or the next meeting's when an earlier meeting slipped a day.</summary>
    static int FinaleMeetingDay()
    {
        return DemoRun.EndCardShown ? DemoMode.FinaleDay : DemoDirector.FinaleMeetingDay();
    }

    /// <summary>Nothing covers the HUD: no panel, crisis, risk card, build mode, run-end screen or HUD tooltip.</summary>
    bool HudVisible()
    {
        if (TooltipShowing()) return false;   // the tab tooltips (lock hints included) hang where the chip sits
        if (UIManager.Exists && UIManager.Instance.IsAnyPanelOpen()) return false;
        if (MiniGameManager.Instance != null && MiniGameManager.Instance.IsMiniGameActive()) return false;
        if (EventManager.Instance != null && EventManager.Instance.IsEventInProgress()) return false;
        if (BuildModeCamera.Instance != null && BuildModeCamera.Instance.IsInBuildMode()) return false;
        RunEndDirector director = RunEndDirector.Instance;
        if (director != null && (director.IsRunOver || director.IsAnyScreenOpen)) return false;
        return true;
    }

    bool TooltipShowing()
    {
        if (tooltip == null && Time.unscaledTime >= nextTooltipLookup)
        {
            tooltip = FindAnyObjectByType<HudTooltip>(FindObjectsInactive.Include);
            nextTooltipLookup = Time.unscaledTime + 2f;   // no HUD yet: look again later, not every frame
        }
        return tooltip != null && tooltip.slip != null && tooltip.slip.gameObject.activeInHierarchy;
    }

    void Refresh(int day, int end, bool bonus, bool hot)
    {
        shownDay = day;
        shownEnd = end;
        shownBonus = bonus;
        shownHot = hot;
        languageChanged = false;
        label.text = bonus ? DemoMode.T("demo.chip.bonus") : DemoMode.T("demo.chip.day", ("day", UIFormat.Int(day)), ("end", UIFormat.Int(end)));
        chip.Fill = hot ? UIColors.Highlight : UIColors.PaperHi;
    }

    void Build(TMP_FontAsset useFont)
    {
        runtimeCanvas = BlueprintUI.CreateOverlayCanvas("DemoChipCanvas (Runtime)", sortingOrder);
        runtimeCanvas.transform.SetParent(transform, false);
        // Never takes clicks: the HUD below keeps them
        UnityEngine.UI.GraphicRaycaster raycaster = runtimeCanvas.GetComponent<UnityEngine.UI.GraphicRaycaster>();
        if (raycaster != null) Destroy(raycaster);

        chip = BlueprintUI.Box(runtimeCanvas.transform, "DemoChip", UIColors.PaperHi, UIColors.Ink, 2f, BlueprintUI.RadiusSmall);
        chip.SetShadow(UIColors.Ink, new Vector2(3f, 4f));
        root = chip.rectTransform;
        root.gameObject.SetActive(false);
        root.anchorMin = root.anchorMax = new Vector2(0.5f, 1f);
        root.pivot = new Vector2(0.5f, 1f);
        root.anchoredPosition = new Vector2(0f, -top);
        root.localRotation = RunEndUI.Rotation(-1f);
        BlueprintUI.HRow(chip, 0f, new RectOffset(16, 16, 6, 8), TextAnchor.MiddleCenter);
        BlueprintUI.Fit(chip, true, true);

        label = BlueprintUI.Text(root, "Label", "", 21f, UIColors.Ink, useFont, TextAlignmentOptions.Center, true);
        label.characterSpacing = 4f;
        BlueprintUI.Layout(label, -1f, -1f, -1f, -1f, -1f, 32f);
        L10nFont.Attach(label);
    }
}
