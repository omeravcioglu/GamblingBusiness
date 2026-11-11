using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The hiring-task clipboard. Reads HiringTaskManager (which keeps its own UI fields empty for this HUD):
/// - active task: difficulty stars, title, description, objective with a highlighter mark (TextHighlighter), countdown and stakes;
///   the time turns warn-orange from 0:30 and loss-red with a red glow from 0:10
/// - office full: the clock holds ("0:57 · on hold", smaller) and the frozen bar gives that line its room
/// - result: a COMPLETE / FAILED rubber stamp over the paper for a moment
/// - no task: "Nothing on the board yet."
/// </summary>
[DisallowMultipleComponent]
public class HudTaskBoard : MonoBehaviour
{
    [Header("States")]
    public GameObject activeContent;
    public GameObject emptyContent;

    [Header("Active task")]
    public TMP_Text headerText;
    public TMP_Text titleText;
    public TMP_Text descriptionText;
    public TMP_Text objectiveText;
    public TMP_Text timerText;
    public Image clockIcon;
    [Tooltip("The countdown bar next to the time; hidden while the task is on hold")]
    public GameObject timerBar;
    public RectTransform timerFill;
    public Image timerFillImage;
    public TMP_Text rewardText;
    public TMP_Text penaltyText;
    [Tooltip("Gold star fills, 5 of them")]
    public Image[] starFills;
    [Tooltip("Star outlines, 5 of them (ink when earned, pencil when not)")]
    public Image[] starLines;
    public GameObject urgentGlow;

    [Header("Result stamps")]
    public GameObject stampComplete;
    public TMP_Text stampCompleteAmount;
    public GameObject stampFailed;
    public TMP_Text stampFailedAmount;
    public float stampSeconds = 2.4f;

    [Header("Timing")]
    public float warnSeconds = 30f;
    public float dangerSeconds = 10f;
    [Tooltip("Font size of the time while the task is on hold (the line is longer: \"0:57 · on hold\", longer still in some languages)")]
    public float waitingTimeSize = 28f;

    // TextHighlighter draws the yellow band behind the words inside this link
    const string HighlightOpen = "<link=\"" + TextHighlighter.LinkId + "\">";
    const string HighlightClose = "</link>";

    HiringTaskManager subscribed;
    HiringTask shownTask;
    bool shownResearch;
    float stampLeft;
    int lastSecond = -1;
    int lastBand = -1;
    bool shownWaiting;   // the office is full: the task waits (added 2026-09-29 with office capacity)
    float timeSize;      // the time's designed font size

    void Awake()
    {
        timeSize = timerText != null ? timerText.fontSize : 0f;
    }

    void OnEnable()
    {
        shownTask = null;
        lastSecond = -1;
        lastBand = -1;
        stampLeft = 0f;
        SetStamp(null);
        Refresh(true);
        L10n.LanguageChanged += OnLanguageChanged;   // localization (2026-09-29)
        L10nFont.AttachAll(this);   // localization: each language's font (English keeps the design font and material)
    }

    void OnDisable()
    {
        Unsubscribe();
        L10n.LanguageChanged -= OnLanguageChanged;   // localization (2026-09-29)
    }

    // Localization (2026-09-29): the next Refresh writes the task again in the new language
    void OnLanguageChanged()
    {
        shownTask = null;
    }

    void Subscribe()
    {
        var m = HiringTaskManager.Instance;
        if (m == subscribed) return;
        Unsubscribe();
        if (m == null) return;
        m.TaskCompleted += OnTaskCompleted;
        subscribed = m;
    }

    void Unsubscribe()
    {
        if (subscribed != null)
            subscribed.TaskCompleted -= OnTaskCompleted;
        subscribed = null;
    }

    void OnTaskCompleted(HiringTask task, bool success, int moneyChange)
    {
        stampLeft = stampSeconds;
        ShowWaiting(false);   // the bar shows the result
        shownWaiting = false;
        if (success)
        {
            if (stampCompleteAmount != null) stampCompleteAmount.text = UIFormat.SignedMoney(moneyChange);
            SetStamp(stampComplete);
            SetBar(1f, UIColors.Money);
            SetTimerColor(UIColors.Ink);
        }
        else
        {
            if (stampFailedAmount != null) stampFailedAmount.text = UIFormat.SignedMoney(moneyChange);
            SetStamp(stampFailed);
            SetBar(0f, UIColors.Loss);
            if (timerText != null) timerText.text = "0:00";
            SetTimerColor(UIColors.Loss);
        }
        if (urgentGlow != null) urgentGlow.SetActive(false);
        Show(true);
    }

    void Update()
    {
        Subscribe();
        Refresh(false);
    }

    void Refresh(bool force)
    {
        if (stampLeft > 0f)
        {
            stampLeft -= Time.unscaledDeltaTime;
            if (stampLeft > 0f) return;
            SetStamp(null);
            shownTask = null;
            force = true;
        }

        var m = HiringTaskManager.Instance;
        bool active = m != null && m.IsTaskActive && m.CurrentTask != null;
        Show(active);
        if (!active) return;

        if (force || m.CurrentTask != shownTask || m.IsResearchProject != shownResearch)
        {
            shownTask = m.CurrentTask;
            shownResearch = m.IsResearchProject;
            FillTask(m);
            lastSecond = -1;
            lastBand = -1;
        }

        float remaining = m.RemainingTime;
        int second = Mathf.Max(0, Mathf.CeilToInt(remaining));
        bool waiting = m.IsWaitingForOffice;   // office full: HiringTaskManager holds the clock (2026-09-29)
        if ((second != lastSecond || waiting != shownWaiting) && timerText != null)
        {
            lastSecond = second;
            shownWaiting = waiting;
            timerText.text = waiting ? TowerText.Get("tower.capacity.task_paused", ("time", UIFormat.Duration(remaining))) : UIFormat.Duration(remaining);
            ShowWaiting(waiting);
        }

        int band = waiting ? 3 : remaining <= dangerSeconds ? 2 : (remaining <= warnSeconds ? 1 : 0);
        if (band != lastBand)
        {
            lastBand = band;
            Color text = band == 3 ? UIColors.Pencil : band == 2 ? UIColors.Loss : (band == 1 ? UIColors.Warn : UIColors.Ink);
            Color bar = band == 3 ? UIColors.PaperShade : band == 2 ? UIColors.Loss : (band == 1 ? UIColors.Warn : UIColors.Highlight);
            SetTimerColor(text);
            if (timerFillImage != null) timerFillImage.color = bar;
            if (urgentGlow != null) urgentGlow.SetActive(band == 2);
        }
        SetBar(m.TimeFraction, null);
    }

    void FillTask(HiringTaskManager m)
    {
        HiringTask task = m.CurrentTask;
        // Localization (2026-09-29): header, title, description and objective from the Hiring table (HiringText); the
        // objective marks its own values (GetRequirementText's highlight), no English phrases are parsed here
        if (headerText != null) headerText.text = HiringText.Get(m.IsResearchProject ? "hiring.board.header.research" : "hiring.board.header");
        if (titleText != null) titleText.text = task.DisplayTitle;
        if (descriptionText != null) descriptionText.text = task.DisplayDescription;
        if (objectiveText != null) objectiveText.text = HiringTaskManager.GetRequirementText(task, Mark);
        if (rewardText != null) rewardText.text = UIFormat.SignedMoney(m.CurrentReward);
        if (penaltyText != null) penaltyText.text = UIFormat.SignedMoney(-m.CurrentPenalty);

        int stars = Mathf.Clamp(task.difficulty, 1, 5);
        for (int i = 0; i < 5; i++)
        {
            bool on = i < stars;
            if (starFills != null && i < starFills.Length && starFills[i] != null)
                starFills[i].enabled = on;
            if (starLines != null && i < starLines.Length && starLines[i] != null)
                starLines[i].color = on ? UIColors.Ink : UIColors.Pencil;
        }
    }

    /// <summary>The highlighter mark around a part of the objective (GetRequirementText picks the parts: the values, or a whole line without one).</summary>
    static string Mark(string part)
    {
        return string.IsNullOrEmpty(part) ? "" : HighlightOpen + part + HighlightClose;
    }

    void SetBar(float fraction, Color? color)
    {
        if (timerFill != null)
        {
            Vector2 max = timerFill.anchorMax;
            max.x = Mathf.Clamp01(fraction);
            timerFill.anchorMax = max;
        }
        if (color.HasValue && timerFillImage != null)
            timerFillImage.color = color.Value;
    }

    /// <summary>On hold: the frozen bar hides so "0:57 · on hold" gets the row, and the time is set a little smaller.</summary>
    void ShowWaiting(bool waiting)
    {
        if (timerBar != null && timerBar.activeSelf == waiting) timerBar.SetActive(!waiting);
        float size = waiting ? waitingTimeSize : timeSize;
        if (timerText != null && size > 0f && !Mathf.Approximately(timerText.fontSize, size)) timerText.fontSize = size;
    }

    void SetTimerColor(Color c)
    {
        if (timerText != null) timerText.color = c;
        if (clockIcon != null) clockIcon.color = c;
    }

    void SetStamp(GameObject stamp)
    {
        if (stampComplete != null) stampComplete.SetActive(stamp == stampComplete && stamp != null);
        if (stampFailed != null) stampFailed.SetActive(stamp == stampFailed && stamp != null);
    }

    void Show(bool active)
    {
        if (activeContent != null && activeContent.activeSelf != active) activeContent.SetActive(active);
        if (emptyContent != null && emptyContent.activeSelf == active) emptyContent.SetActive(!active);
    }
}
