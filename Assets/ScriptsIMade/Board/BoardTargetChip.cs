using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A small paper chip at the bottom-left of the screen while a board target is active: the Board's navy mark, "Board:
/// +$4,200 of +$9,900 · due day 30" (the growth so far in green once the target is reached, red while below the start) and
/// a thin progress bar (yellow, green once reached). Own overlay canvas (sorting order 70, never takes clicks), stacked
/// above RunEnd's debt chip and the heat chip while they show, so the three never overlap and none of them covers the
/// HUD's ledger, tabs, clipboard, Skip / Hire / Next batch or toasts.
/// Hidden while a pop-up, crisis, risk card, build mode or a run-end screen is up, like those chips. With Root empty it is
/// built at runtime; a designed one can fill the references (or a HUD can read BoardMeetings and switch this component
/// off, which hides the chip).
/// The label comes from the "Board" String Table (board.chip.label) and is written again, in the language's font, when the
/// language changes.
/// </summary>
[DisallowMultipleComponent]
public class BoardTargetChip : MonoBehaviour
{
    [Header("Look")]
    [Tooltip("Font (empty = the Coming Soon font the HUD uses, else TextMesh Pro's default)")]
    public TMP_FontAsset font;
    [Tooltip("Sorting order of the runtime canvas (the debt and heat chips use 70 too)")]
    public int sortingOrder = 70;
    [Tooltip("Distance from the bottom-left corner of the screen (reference pixels)")]
    public Vector2 margin = new Vector2(24f, 24f);
    [Tooltip("Sit above RunEnd's debt chip and the heat chip while they show (runtime chip only; a designed chip stays where it is put)")]
    public bool stackAboveOtherChips = true;
    [Tooltip("Gap above the chip below (reference pixels)")]
    public float chipGap = 10f;
    [Tooltip("Seconds the runtime chip takes to slide to its new spot when a chip below it comes or goes (0 = jump)")]
    public float slideSeconds = 0.15f;

    [Header("Chip (leave Root empty to build it at runtime)")]
    public RectTransform root;
    [Tooltip("\"Board: +$4,200 of +$9,900 · due day 30\"")]
    public TMP_Text label;
    [Tooltip("The progress bar's fill: its right anchor is set to the progress (0-1)")]
    public RectTransform progressFill;
    [Tooltip("Yellow while short of the target, green once it is reached (a StatusBox gets its fill set, anything else its colour)")]
    public Graphic progressTint;

    Canvas runtimeCanvas;
    bool builtAtRuntime;
    bool snapNext = true;
    double shownGain = double.NaN;
    double shownGoal = double.NaN;
    int shownDue = -1;
    float shownProgress = -1f;
    bool shownReached;
    bool tintApplied;
    bool languageChanged;
    bool labelFollowsLanguage;
    Vector2 slideVelocity;
    readonly Vector3[] corners = new Vector3[4];

    void OnEnable()
    {
        L10n.LanguageChanged += OnLanguageChanged;
    }

    void OnDisable()
    {
        L10n.LanguageChanged -= OnLanguageChanged;
        // Switched off (e.g. by a HUD that shows the target itself): don't leave the chip on screen
        if (root != null) root.gameObject.SetActive(false);
        snapNext = true;
    }

    // Rewrite the label in the new language on the next refresh (its font switches by itself: L10nFont)
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
        BoardMeetings board = BoardMeetings.Instance;
        bool show = board != null && board.HasTarget && !board.IsMeetingOpen && HudVisible();
        if (show) EnsureBuilt();
        if (root != null && root.gameObject.activeSelf != show)
        {
            root.gameObject.SetActive(show);
            snapNext = true;   // appear in place, then slide only when the chips below change
        }
        if (show) Refresh(board);
    }

    void LateUpdate()
    {
        // After the heat chip has placed itself this frame
        if (builtAtRuntime && root != null && root.gameObject.activeSelf) Place();
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

    void Refresh(BoardMeetings board)
    {
        BoardTarget target = board.Target;
        CompanyManager company = CompanyManager.Instance;
        // The value the ledger shows (it counts up), so the chip and the ledger agree
        double value = company != null ? company.displayedValue : target.startValue;
        double gain = Math.Round(value - target.startValue);
        double goal = Math.Round(target.Gain);
        bool reached = gain >= goal;
        float progress = goal > 0.0 ? Mathf.Clamp01((float)(gain / goal)) : 1f;

        if (languageChanged || gain != shownGain || goal != shownGoal || target.dueDay != shownDue)
        {
            shownGain = gain;
            shownGoal = goal;
            shownDue = target.dueDay;
            languageChanged = false;
            if (label != null)
            {
                if (!labelFollowsLanguage)
                {
                    L10nFont.Attach(label);   // a designed label too
                    labelFollowsLanguage = true;
                }
                // Compact money from $100,000 up ("+$1.25M of +$1.5M"): the chip stays short enough to clear the HUD's Skip
                // button even when it sits in the corner on its own
                Color gainColor = reached ? UIColors.Money : gain < 0.0 ? UIColors.Loss : UIColors.Ink;
                label.text = LabelText("<b>" + BoardUI.Colored(BoardUI.SignedCompactMoney(gain), gainColor) + "</b>", BoardUI.SignedCompactMoney(goal),
                                       target.dueDay);
            }
        }

        if (progressFill != null && !Mathf.Approximately(progress, shownProgress))
        {
            shownProgress = progress;
            Vector2 max = progressFill.anchorMax;
            max.x = progress;
            progressFill.anchorMax = max;
        }

        if (!tintApplied || reached != shownReached)
        {
            tintApplied = true;
            shownReached = reached;
            Color tint = reached ? UIColors.Money : UIColors.Highlight;
            if (progressTint is StatusBox box) box.Fill = tint;
            else if (progressTint != null) progressTint.color = tint;
        }
    }

    /// <summary>"Board: +$4,200 of +$9,900 · due day 30" (<paramref name="gain"/> arrives bold and coloured).</summary>
    static string LabelText(string gain, string goal, int dueDay)
    {
        return L10n.Get(BoardMeetings.Table, "board.chip.label", ("gain", gain), ("goal", goal), ("day", dueDay));
    }

    // Bottom-left, just above the debt chip and the heat chip while they show
    void Place()
    {
        float y = margin.y;
        if (stackAboveOtherChips && runtimeCanvas != null)
        {
            y = Mathf.Max(y, TopOf(DebtChipRoot()) + chipGap);
            y = Mathf.Max(y, TopOf(HeatChipRoot()) + chipGap);
        }
        Vector2 target = new Vector2(margin.x, y);
        Vector2 current = root.anchoredPosition;
        if (snapNext || slideSeconds <= 0f || (current - target).sqrMagnitude < 0.25f)
        {
            current = target;
            slideVelocity = Vector2.zero;
            snapNext = false;
        }
        else
        {
            current = Vector2.SmoothDamp(current, target, ref slideVelocity, slideSeconds * 0.5f, Mathf.Infinity, Time.unscaledDeltaTime);
        }
        if (root.anchoredPosition != current) root.anchoredPosition = current;
    }

    /// <summary>Top edge of another chip in this canvas's units (from the screen's bottom), or -infinity while it is hidden.</summary>
    float TopOf(RectTransform other)
    {
        if (other == null || !other.gameObject.activeInHierarchy) return float.NegativeInfinity;
        other.GetWorldCorners(corners);
        Canvas otherCanvas = other.GetComponentInParent<Canvas>();
        Camera cam = otherCanvas != null && otherCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? otherCanvas.worldCamera : null;
        return RectTransformUtility.WorldToScreenPoint(cam, corners[1]).y / Mathf.Max(0.0001f, runtimeCanvas.scaleFactor);
    }

    static RectTransform DebtChipRoot()
    {
        RunEndDirector director = RunEndDirector.Instance;
        return director != null && director.debtChipView != null ? director.debtChipView.root : null;
    }

    static RectTransform HeatChipRoot()
    {
        HeatMeter meter = HeatMeter.Instance;
        return meter != null && meter.chipView != null ? meter.chipView.root : null;
    }

    // ------------------------------------------------------------------
    // Runtime chip
    // ------------------------------------------------------------------

    void EnsureBuilt()
    {
        if (root != null) return;
        runtimeCanvas = BoardUI.CreateCanvas("BoardChipCanvas (Runtime)", sortingOrder, transform, false);   // never takes clicks
        Build((RectTransform)runtimeCanvas.transform, BoardUI.ResolveFont(font));
        builtAtRuntime = true;
        snapNext = true;
    }

    void Build(RectTransform parent, TMP_FontAsset useFont)
    {
        // Built inactive; Update shows it
        StatusBox chip = StatusUI.Box(parent, "BoardChip", UIColors.PaperHi, UIColors.Ink, 2f, BoardUI.SmallRadii);
        chip.SetShadow(UIColors.Ink, new Vector2(3f, 4f));
        RectTransform rect = chip.rectTransform;
        rect.gameObject.SetActive(false);
        rect.anchorMin = rect.anchorMax = Vector2.zero;
        rect.pivot = Vector2.zero;
        rect.anchoredPosition = margin;
        root = rect;

        HorizontalLayoutGroup row = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.padding = new RectOffset(12, 18, 9, 11);
        row.spacing = 12f;
        row.childAlignment = TextAnchor.MiddleLeft;
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = false;
        ContentSizeFitter fit = rect.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // The Board's mark from the memo's letterhead: a navy disc with a paper-white sparkle
        StatusBox mark = StatusUI.Box(rect, "Mark", BoardUI.Navy, UIColors.Ink, 2f, Vector4.one * 15f);
        LayoutElement markLayout = mark.gameObject.AddComponent<LayoutElement>();
        markLayout.minWidth = markLayout.preferredWidth = 30f;
        markLayout.minHeight = markLayout.preferredHeight = 30f;
        StatusUI.Star(mark.rectTransform, "Sparkle", UIColors.PaperHi, 13f);

        // The label over a thin bar as wide as it
        RectTransform column = StatusUI.NewRect("Column", rect);
        VerticalLayoutGroup stack = column.gameObject.AddComponent<VerticalLayoutGroup>();
        stack.spacing = 4f;
        stack.childAlignment = TextAnchor.UpperLeft;
        stack.childControlWidth = true;
        stack.childControlHeight = true;
        stack.childForceExpandWidth = true;
        stack.childForceExpandHeight = false;

        TextMeshProUGUI text = StatusUI.Text(column, "Label", LabelText("$0", "+$0", 10), 22f, UIColors.Ink, useFont, TextAlignmentOptions.MidlineLeft);
        LayoutElement textLayout = text.gameObject.AddComponent<LayoutElement>();
        textLayout.minHeight = 32f;
        label = text;
        L10nFont.Attach(text);
        labelFollowsLanguage = true;

        StatusBox bar = StatusUI.Box(column, "Bar", UIColors.PaperShade, UIColors.Ink, 1.5f, Vector4.one * 4f);
        LayoutElement barLayout = bar.gameObject.AddComponent<LayoutElement>();
        barLayout.minHeight = barLayout.preferredHeight = 8f;
        barLayout.flexibleWidth = 1f;

        RectTransform track = StatusUI.NewRect("Track", bar.rectTransform);
        StatusUI.Stretch(track, 1.5f, 1.5f, 1.5f, 1.5f);
        StatusBox fillBox = StatusUI.Box(track, "Fill", UIColors.Highlight, Color.clear, 0f, Vector4.one * 2.5f);
        RectTransform fillRect = fillBox.rectTransform;
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = new Vector2(0f, 1f);
        fillRect.pivot = new Vector2(0f, 0.5f);
        fillRect.offsetMin = fillRect.offsetMax = Vector2.zero;
        progressFill = fillRect;
        progressTint = fillBox;
    }
}
