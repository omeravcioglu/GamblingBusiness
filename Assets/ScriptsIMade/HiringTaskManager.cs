using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Manages hiring tasks/challenges - gives players objectives with time limits
/// </summary>
public class HiringTaskManager : MonoBehaviour
{
    public static HiringTaskManager Instance;

    [Header("Database")]
    public HiringTaskDatabase taskDatabase;

    [Header("UI Elements")]
    public GameObject taskPanel;
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI descriptionText;
    public TextMeshProUGUI requirementText;
    public TextMeshProUGUI timerText;
    public TextMeshProUGUI rewardText;
    public TextMeshProUGUI penaltyText;
    public Image timerFill;
    public GameObject successEffect;
    public GameObject failEffect;

    [Header("Settings")]
    public float delayBetweenTasks = 10f;       // Seconds between tasks
    public float taskStartDelay = 30f;          // Delay before first task
    public bool autoStartTasks = true;
    [Tooltip("Freeze the countdown (and don't start new tasks) while hiring is blocked by an open panel, mini-game or risk event")]
    public bool pauseWhileDeskBlocked = true;
    [Tooltip("Freeze the countdown (and don't start new tasks) while every office place is taken: nobody can be hired then (OfficeCapacity, 2026-09-29)")]
    public bool pauseWhileOfficeFull = true;

    [Header("Audio")]
    public AudioSource audioSource;
    public AudioClip taskStartSound;
    public AudioClip taskSuccessSound;
    public AudioClip taskFailSound;
    public AudioClip tickSound;

    const float ResultEffectTime = 2f;          // TASK COMPLETE / FAILED overlay
    const float PanelHideDelay = 1.5f;          // panel closes this long after a result

    // Skills that change tasks ("projects")
    const float RnDRewardBonus = 0.25f;         // R&D Laboratory: +25% task rewards...
    const float RnDProjectChance = 0.3f;        // ...and 30% of tasks become research projects from the hardest ones:
    const int RnDMinDifficulty = 4;             //    difficulty 4-5,
    const float ResearchRewardMultiplier = 1.5f;  //  x1.5 reward,
    const float ResearchTimeMultiplier = 1.5f;    //  x1.5 time
    const float ResearchPenaltyMultiplier = 0.5f; //  and half the penalty (failed research is cheap)
    const float MaxRewardBonus = 1f;            // all reward skills together
    const float MaxPenaltyReduction = 0.5f;     // Project Management: -10% penalty (cap)
    const float MaxTimeBonus = 0.5f;            // Agile Methodology: +15% time (cap)
    const float CoinFlipDoubleChance = 0.5f;    // Coin Flip: a completed task pays double this often
    const string ResearchTitlePrefix = "R&D PROJECT: ";

    // Current task state
    private HiringTask currentTask;
    private HiringTask lastTask;
    private float remainingTime;
    private float taskDuration = 1f;
    private bool taskActive = false;
    private float nextTaskTimer;
    private int tasksCompleted = 0;
    private int tasksFailed = 0;
    private int currentReward;                  // this task's reward and penalty after skills
    private int currentPenalty;
    private bool currentIsResearch;

    // Events
    public delegate void OnTaskStarted(HiringTask task);
    public event OnTaskStarted TaskStarted;

    public delegate void OnTaskCompleted(HiringTask task, bool success, int moneyChange);
    public event OnTaskCompleted TaskCompleted;

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void Start()
    {
        SetupResultOverlay(successEffect);
        SetupResultOverlay(failEffect);

        if (taskPanel != null)
            taskPanel.SetActive(false);

        if (successEffect != null)
            successEffect.SetActive(false);
        if (failEffect != null)
            failEffect.SetActive(false);

        // Auto-initialize database if needed
        if (taskDatabase != null)
            taskDatabase.InitializeDefaultTasks();

        // Start the task timer
        if (DemoMode.Active) taskStartDelay = DemoMode.Config.taskStartDelay;   // Demo: the first task comes sooner
        nextTaskTimer = taskStartDelay;
    }

    void Update()
    {
        // No countdown while the player can't hire anyway (panel open, mini-game, risk event)
        if (pauseWhileDeskBlocked && GameFlowManager.IsDeskInputBlocked())
            return;
        // ...or while the offices are full (capacity is a hard limit on hiring): the task waits for a free place
        if (pauseWhileOfficeFull && IsWaitingForOffice)
            return;

        if (taskActive)
        {
            UpdateActiveTask();
        }
        else if (autoStartTasks && taskDatabase != null)
        {
            // Wait for next task
            nextTaskTimer -= Time.deltaTime;
            if (nextTaskTimer <= 0f)
            {
                StartRandomTask();
            }
        }
    }

    /// <summary>Hiring waits for office space (every place taken): the countdown is held and no new task starts.</summary>
    public bool IsWaitingForOffice => OfficeCapacity.IsLimited && OfficeCapacity.IsFull;

    void UpdateActiveTask()
    {
        remainingTime -= Time.deltaTime;

        UpdateTimerUI();

        // Play tick sound in last 10 seconds
        if (remainingTime <= 10f && remainingTime > 0f)
        {
            if (Mathf.FloorToInt(remainingTime) != Mathf.FloorToInt(remainingTime + Time.deltaTime))
            {
                PlaySound(tickSound);
            }
        }

        // Time's up!
        if (remainingTime <= 0f)
        {
            FailTask();
        }
    }

    void UpdateTimerUI()
    {
        if (timerText != null)
        {
            int seconds = Mathf.Max(0, Mathf.CeilToInt(remainingTime));
            timerText.text = $"{seconds / 60}:{(seconds % 60):00}";

            // Color warning
            if (remainingTime <= 10f)
                timerText.color = Color.red;
            else if (remainingTime <= 30f)
                timerText.color = Color.yellow;
            else
                timerText.color = Color.white;
        }

        if (timerFill != null)
        {
            timerFill.fillAmount = TimeFraction;
        }
    }

    /// <summary>
    /// Start a random task from the database
    /// </summary>
    public void StartRandomTask()
    {
        if (taskDatabase == null || taskActive) return;

        // R&D Laboratory: some tasks are special research projects, picked from the hardest ones
        HiringTask task = null;
        bool research = false;
        if (SkillTreeManager.Instance != null && SkillTreeManager.Instance.IsRnDUnlocked() && Random.value < RnDProjectChance)
        {
            task = PickResearchTask();
            research = task != null;
        }

        if (task == null)
        {
            task = taskDatabase.GetRandomTask();

            // Avoid the same task twice in a row
            int taskCount = taskDatabase.tasks != null ? taskDatabase.tasks.Count : 0;
            for (int i = 0; i < 3 && task != null && task == lastTask && taskCount > 1; i++)
                task = taskDatabase.GetRandomTask();
        }

        if (task != null)
        {
            StartTask(task, research);
        }
        else
        {
            // Empty database: try again later instead of every frame
            nextTaskTimer = delayBetweenTasks;
        }
    }

    HiringTask PickResearchTask()
    {
        if (taskDatabase.tasks == null) return null;
        List<HiringTask> hard = taskDatabase.tasks.FindAll(t => t != null && t.difficulty >= RnDMinDifficulty && t != lastTask);
        return hard.Count > 0 ? hard[Random.Range(0, hard.Count)] : null;
    }

    /// <summary>
    /// How skills change tasks: reward bonus (R&D Laboratory +25%, Project Management +10%), penalty reduction
    /// (Project Management -10%) and extra time (Agile Methodology +15%).
    /// </summary>
    public static void GetTaskModifiers(out float rewardBonus, out float penaltyReduction, out float timeBonus)
    {
        rewardBonus = 0f;
        penaltyReduction = 0f;
        timeBonus = 0f;

        SkillTreeManager skills = SkillTreeManager.Instance;
        if (skills == null) return;

        float projectManagement = Mathf.Max(0f, skills.GetEffect(SkillEffect.ProjectManagement));
        if (skills.IsRnDUnlocked())
            rewardBonus += RnDRewardBonus * skills.EffectMultiplier;
        rewardBonus = Mathf.Min(rewardBonus + projectManagement, MaxRewardBonus);
        penaltyReduction = Mathf.Min(projectManagement, MaxPenaltyReduction);
        timeBonus = Mathf.Clamp(skills.GetEffect(SkillEffect.AgileMethodology), 0f, MaxTimeBonus);
    }

    /// <summary>
    /// Start a specific task
    /// </summary>
    public void StartTask(HiringTask task)
    {
        StartTask(task, false);
    }

    /// <summary>Start a specific task; a research project (R&D Laboratory) is labelled as one.</summary>
    public void StartTask(HiringTask task, bool researchProject)
    {
        if (task == null || taskActive) return;

        // A previous result may still be scheduled to hide the panel
        CancelInvoke(nameof(HideTaskPanel));
        CancelInvoke(nameof(HideSuccessEffect));
        CancelInvoke(nameof(HideFailEffect));
        HideSuccessEffect();
        HideFailEffect();

        currentTask = task;
        lastTask = task;
        currentIsResearch = researchProject;

        // Skills change this task's numbers (the shared task data itself is never modified)
        GetTaskModifiers(out float rewardBonus, out float penaltyReduction, out float timeBonus);
        float rewardFactor = (1f + rewardBonus) * (researchProject ? ResearchRewardMultiplier : 1f);
        float penaltyFactor = (1f - penaltyReduction) * (researchProject ? ResearchPenaltyMultiplier : 1f);
        float timeFactor = (1f + timeBonus) * (researchProject ? ResearchTimeMultiplier : 1f);
        currentReward = Mathf.Max(0, Mathf.RoundToInt(task.rewardMoney * rewardFactor));
        currentPenalty = Mathf.Max(0, Mathf.RoundToInt(task.penaltyMoney * penaltyFactor));
        taskDuration = Mathf.Max(1f, task.timeLimitSeconds * timeFactor);
        remainingTime = taskDuration;
        taskActive = true;

        // Show UI
        if (taskPanel != null)
            taskPanel.SetActive(true);

        UpdateTaskUI();
        UpdateTimerUI();

        PlaySound(taskStartSound);
        TaskStarted?.Invoke(task);

        Debug.Log($"[HiringTaskManager] Task started: {(researchProject ? ResearchTitlePrefix : "")}{task.title} (${currentReward:N0} / -${currentPenalty:N0}, {taskDuration:0}s)");
    }

    void UpdateTaskUI()
    {
        if (currentTask == null) return;

        // Texts in the current language (Hiring table); the data keeps the English
        if (titleText != null)
            titleText.text = currentIsResearch ? HiringText.Get("hiring.legacy.research_title", ("title", currentTask.DisplayTitle)) : currentTask.DisplayTitle;

        if (descriptionText != null)
            descriptionText.text = currentTask.DisplayDescription;

        if (requirementText != null)
            requirementText.text = "► " + GetRequirementText(currentTask);

        // Handle separate or combined reward/penalty display
        if (rewardText != null)
        {
            if (penaltyText != null)
            {
                // Separate fields
                rewardText.text = FormatMoney(currentReward);
                penaltyText.text = "-" + FormatMoney(currentPenalty);
            }
            else
            {
                // Combined field
                rewardText.text = HiringText.Get("hiring.legacy.stakes", ("reward", FormatMoney(currentReward)), ("penalty", FormatMoney(currentPenalty)));
            }
        }
    }

    /// <summary>
    /// Player-facing objective for a task in the current language, e.g. "Hire someone with IQ 85 or lower" (Hiring
    /// table, hiring.req.*: one template per requirement type, English in HiringText). Wording matches the check in
    /// HiringTask.CheckRequirement (limits are inclusive); targets show through ApplicantText, the data keeps the English.
    /// </summary>
    public static string GetRequirementText(HiringTask task)
    {
        return GetRequirementText(task, null);
    }

    /// <summary>
    /// The objective with <paramref name="highlight"/> applied to what to look for on a CV: each value (the ethnicity,
    /// the age, the IQ range, the amount...), or the whole sentence for objectives without a value (criminal record,
    /// clean record, golden, anyone, no experience). The task board passes its highlighter mark; null leaves the text plain.
    /// </summary>
    public static string GetRequirementText(HiringTask task, System.Func<string, string> highlight)
    {
        if (task == null) return "";

        string value = task.targetValue != null ? task.targetValue.Trim() : "";
        string max = task.targetValueMax != null ? task.targetValueMax.Trim() : "";
        string Mark(string part) => highlight != null && !string.IsNullOrEmpty(part) ? highlight(part) : part;
        string Whole(string text) => highlight != null && !string.IsNullOrEmpty(text) ? highlight(text) : text;

        switch (task.requirementType)
        {
            case TaskRequirementType.HireRace:
                return HiringText.Get(TakesAn(value) ? "hiring.req.race.an" : "hiring.req.race", ("race", Mark(ApplicantText.Race(value))));
            case TaskRequirementType.HireGender:
                return HiringText.Get(TakesAn(value) ? "hiring.req.gender.an" : "hiring.req.gender", ("gender", Mark(ApplicantText.Gender(value))));
            case TaskRequirementType.HireAgeMin:
                return HiringText.Get("hiring.req.age_min", ("age", Mark(FormatNumber(value))));
            case TaskRequirementType.HireAgeMax:
                return HiringText.Get("hiring.req.age_max", ("age", Mark(FormatNumber(value))));
            case TaskRequirementType.HireAgeRange:
                if (value == max)
                    return HiringText.Get("hiring.req.age_exact", ("age", Mark(FormatNumber(value))));
                return HiringText.Get("hiring.req.age_range", ("range", Mark(FormatRange(value, max))));
            case TaskRequirementType.HireDegree:
                if (string.Equals(value, "High School", System.StringComparison.OrdinalIgnoreCase))
                    return HiringText.Get("hiring.req.degree.high_school", ("degree", Mark(ApplicantText.Degree(value))));
                return HiringText.Get(TakesAn(value) ? "hiring.req.degree.an" : "hiring.req.degree", ("degree", Mark(ApplicantText.Degree(value))));
            case TaskRequirementType.HireMajorContains:
                return HiringText.Get("hiring.req.major", ("major", Mark(ApplicantText.Major(value))));
            case TaskRequirementType.HireGPAMin:
                return HiringText.Get("hiring.req.gpa_min", ("gpa", Mark(FormatGpa(value))));
            case TaskRequirementType.HireGPAMax:
                return HiringText.Get("hiring.req.gpa_max", ("gpa", Mark(FormatGpa(value))));
            case TaskRequirementType.HireIQMin:
                return HiringText.Get("hiring.req.iq_min", ("iq", Mark(FormatNumber(value))));
            case TaskRequirementType.HireIQMax:
                return HiringText.Get("hiring.req.iq_max", ("iq", Mark(FormatNumber(value))));
            case TaskRequirementType.HireIQRange:
                return HiringText.Get("hiring.req.iq_range", ("range", Mark(FormatRange(value, max))));
            case TaskRequirementType.HireExperienceMin:
                return HiringText.Get("hiring.req.experience_min", ("years", Mark(FormatNumber(value))));
            case TaskRequirementType.HireExperienceMax:
                if (value == "0")
                    return Whole(HiringText.Get("hiring.req.experience_max.none"));
                // {years} is shown (maybe marked), {count} picks year/years
                HiringTask.TryParseInt(value, out int years);
                return HiringText.Get("hiring.req.experience_max", ("years", Mark(FormatNumber(value))), ("count", years));
            case TaskRequirementType.HireSalaryMin:
                return HiringText.Get("hiring.req.salary_min", ("amount", Mark(FormatMoney(value))));
            case TaskRequirementType.HireSalaryMax:
                return HiringText.Get("hiring.req.salary_max", ("amount", Mark(FormatMoney(value))));
            case TaskRequirementType.HireWithCriminalRecord:
                return Whole(HiringText.Get("hiring.req.criminal_record"));
            case TaskRequirementType.HireWithoutCriminalRecord:
                return Whole(HiringText.Get("hiring.req.clean_record"));
            case TaskRequirementType.HireGolden:
                return Whole(HiringText.Get("hiring.req.golden"));
            case TaskRequirementType.HireWithTrait:
                return HiringText.Get("hiring.req.trait", ("trait", Mark(ApplicantText.Trait(value))));
            case TaskRequirementType.HireAny:
                return Whole(HiringText.Get("hiring.req.any"));
            default:
                return HiringText.Get("hiring.req.default");
        }
    }

    // English needs "an" before a vowel ("Hire an Asian employee"): a variant key that translations word like the "a" one
    static bool TakesAn(string word)
    {
        return !string.IsNullOrEmpty(word) && "AEIOUaeiou".IndexOf(word[0]) >= 0;
    }

    static string FormatNumber(string value)
    {
        return HiringTask.TryParseInt(value, out int number) ? number.ToString(CultureInfo.InvariantCulture) : value;
    }

    /// <summary>"24-28" (hiring.req.range: each language's range style).</summary>
    static string FormatRange(string min, string max)
    {
        return HiringText.Get("hiring.req.range", ("min", FormatNumber(min)), ("max", FormatNumber(max)));
    }

    static string FormatMoney(int amount)
    {
        return "$" + amount.ToString("N0", CultureInfo.InvariantCulture);
    }

    static string FormatMoney(string value)
    {
        return HiringTask.TryParseInt(value, out int amount) ? FormatMoney(amount) : "$" + value;
    }

    static string FormatGpa(string value)
    {
        return HiringTask.TryParseFloat(value, out float gpa) ? gpa.ToString("0.0#", CultureInfo.InvariantCulture) : value;
    }

    /// <summary>
    /// Called when an employee is hired - check if it completes the task.
    /// Pass the applicant as the CV showed them (before hire-time skill bonuses).
    /// </summary>
    public void OnEmployeeHired(ApplicantData employee)
    {
        if (!taskActive || currentTask == null) return;

        if (currentTask.CheckRequirement(employee))
        {
            CompleteTask();
        }
    }

    void CompleteTask()
    {
        if (!taskActive) return;

        taskActive = false;
        tasksCompleted++;

        // Give reward (Coin Flip skill: 50% chance it's doubled)
        int reward = currentReward;
        bool coinFlipDoubled = reward > 0 && SkillTreeManager.Instance != null &&
                               SkillTreeManager.Instance.HasEffect(SkillEffect.CoinFlip) && Random.value < CoinFlipDoubleChance;
        if (coinFlipDoubled)
            reward *= 2;

        if (CompanyManager.Instance != null)
        {
            CompanyManager.Instance.AddMoney(reward, true);
        }
        if (coinFlipDoubled)
            GameNotifications.Show(HiringText.Get("hiring.toast.coin_flip", ("amount", "+" + FormatMoney(reward))), NotificationKind.Good);

        // Show success
        HideFailEffect();
        if (successEffect != null)
        {
            successEffect.SetActive(true);
            CancelInvoke(nameof(HideSuccessEffect));
            Invoke(nameof(HideSuccessEffect), ResultEffectTime);
        }

        PlaySound(taskSuccessSound);
        TaskCompleted?.Invoke(currentTask, true, reward);

        Debug.Log($"[HiringTaskManager] Task completed! Reward: ${reward}");

        // Hide panel after delay
        CancelInvoke(nameof(HideTaskPanel));
        Invoke(nameof(HideTaskPanel), PanelHideDelay);

        // Set next task timer
        nextTaskTimer = delayBetweenTasks;
    }

    void FailTask()
    {
        if (!taskActive) return;

        taskActive = false;
        tasksFailed++;
        remainingTime = 0f;

        // Apply penalty
        if (CompanyManager.Instance != null)
        {
            CompanyManager.Instance.AddMoney(-currentPenalty, true);
        }

        // Show failure
        HideSuccessEffect();
        if (failEffect != null)
        {
            failEffect.SetActive(true);
            CancelInvoke(nameof(HideFailEffect));
            Invoke(nameof(HideFailEffect), ResultEffectTime);
        }

        PlaySound(taskFailSound);
        TaskCompleted?.Invoke(currentTask, false, -currentPenalty);

        Debug.Log($"[HiringTaskManager] Task failed! Penalty: -${currentPenalty}");

        // Hide panel after delay
        CancelInvoke(nameof(HideTaskPanel));
        Invoke(nameof(HideTaskPanel), PanelHideDelay);

        // Set next task timer
        nextTaskTimer = delayBetweenTasks;
    }

    void HideTaskPanel()
    {
        if (taskPanel != null)
            taskPanel.SetActive(false);
    }

    void HideSuccessEffect()
    {
        if (successEffect != null)
            successEffect.SetActive(false);
    }

    void HideFailEffect()
    {
        if (failEffect != null)
            failEffect.SetActive(false);
    }

    // The TASK COMPLETE / FAILED overlays sit inside the panel's layout group, which squashes them to zero height.
    // Take them out of the layout and stretch them over the whole panel.
    static void SetupResultOverlay(GameObject effect)
    {
        if (effect == null) return;

        RectTransform rt = effect.transform as RectTransform;
        if (rt == null || rt.parent == null || rt.parent.GetComponent<LayoutGroup>() == null) return;

        LayoutElement layoutElement = effect.GetComponent<LayoutElement>();
        if (layoutElement == null)
            layoutElement = effect.AddComponent<LayoutElement>();
        layoutElement.ignoreLayout = true;

        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.SetAsLastSibling(); // draw above the panel's rows
    }

    /// <summary>
    /// Skip the current task (with penalty)
    /// </summary>
    public void SkipTask()
    {
        if (taskActive)
        {
            FailTask();
        }
    }

    /// <summary>
    /// Force start a task now
    /// </summary>
    [ContextMenu("Force Start Random Task")]
    public void ForceStartTask()
    {
        if (!taskActive)
        {
            StartRandomTask();
        }
    }

    void PlaySound(AudioClip clip)
    {
        if (audioSource != null && clip != null)
        {
            audioSource.PlayOneShot(clip, GameSettings.SfxVolume);
        }
    }

    // Public getters
    public bool IsTaskActive => taskActive;
    public HiringTask CurrentTask => currentTask;
    /// <summary>The current task's reward after skills (before a Coin Flip double).</summary>
    public int CurrentReward => currentReward;
    /// <summary>The current task's penalty after skills.</summary>
    public int CurrentPenalty => currentPenalty;
    /// <summary>True when the current task is an R&D research project.</summary>
    public bool IsResearchProject => currentIsResearch;
    public float RemainingTime => remainingTime;
    /// <summary>Remaining share of the current task's time limit (1 = just started, 0 = out of time).</summary>
    public float TimeFraction => taskActive ? Mathf.Clamp01(remainingTime / taskDuration) : 0f;
    public int TasksCompleted => tasksCompleted;
    public int TasksFailed => tasksFailed;
}
