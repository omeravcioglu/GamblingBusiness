using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// The hiring desk: spawns a batch of CV papers and handles Hire / Skip / Next Batch.
/// Desk buttons call HireCurrent / SkipApplicant / NextBatch. Those calls are ignored while a panel is open,
/// the game is paused, or a mini-game / risk event is running (the buttons turn non-interactable meanwhile).
/// Office capacity is a hard limit on hiring (owner, 2026-09-29; OfficeCapacity, provided by the HQ campus): while every
/// place is taken the Hire button stays clickable but explains itself (a toast, and the HUD's capacity chip shakes:
/// HireRefused) instead of hiring; the batch just waits (the last CV still can't be skipped).
/// </summary>
public class GameFlowManager : MonoBehaviour
{
    [Header("Prefabs & Positions")]
    public GameObject paperPrefab;
    public Transform paperSpawnPoint;
    public Transform hiredBoxPoint;
    public Transform trashPoint;

    [Header("UI")]
    public Button skipButton;
    public Button hireButton;
    public Button nextBatchButton;

    [Header("Batch Settings")]
    public int batchSize = 3;  // Number of applicants per batch
    [Tooltip("Seconds a hired/skipped paper keeps animating before it is removed")]
    public float paperRemoveDelay = 5f;

    [Header("Entrance Animation")]
    [Tooltip("Delay between each paper's entrance animation")]
    public float entranceStaggerDelay = 0.15f;
    [Tooltip("Play entrance animation when papers spawn")]
    public bool playEntranceAnimation = true;

    [Header("Paper Pile")]
    [Tooltip("Rotation of each paper of a batch around its face, in degrees (slot 0 lies on top): a hand-stacked pile")]
    public float[] paperStackAngles = { 0.8f, 2.4f, -3.2f };

    /// <summary>Fires whenever what the desk allows changes (new batch, hire, skip, blocked / unblocked, office full or not).</summary>
    public event System.Action DeskStateChanged;

    /// <summary>A hire was refused because every office place is taken (the HUD's capacity chip shakes).</summary>
    public static event System.Action HireRefused;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        HireRefused = null;   // entering Play mode without a domain reload keeps statics
    }

    float lastRefusalToast = -10f;

    // Papers still lying on the desk, in batch order. A hired/skipped paper is set to null here:
    // it animates away on its own and is destroyed after paperRemoveDelay, so it's never destroyed twice.
    readonly List<GameObject> currentPapers = new List<GameObject>();
    ApplicantData[] batch = new ApplicantData[0];
    int index = 0;

    // Logical desk state (what the buttons allow when nothing is blocking input)
    bool batchActive;   // a batch is on the desk waiting for a decision
    bool spawning;
    Coroutine spawnRoutine;

    bool buttonsDirty = true;
    bool lastBlocked;

    // ------------------------------------------------------------------
    // Public state (for UI)
    // ------------------------------------------------------------------

    /// <summary>The applicant whose CV is on top of the pile, or null.</summary>
    public ApplicantData CurrentApplicant =>
        batchActive && batch != null && index >= 0 && index < batch.Length ? batch[index] : null;

    /// <summary>Position of the current applicant in the batch (0-based).</summary>
    public int CurrentIndex => index;

    /// <summary>Number of applicants in the current batch.</summary>
    public int ApplicantCount => batch != null ? batch.Length : 0;

    public bool IsSpawning => spawning;

    /// <summary>Hiring the current applicant is possible (ignoring input blocking).</summary>
    public bool CanHire => batchActive && !spawning && CurrentApplicant != null;

    /// <summary>Skipping is possible (ignoring input blocking). The last applicant of a batch can't be skipped.</summary>
    public bool CanSkip => CanHire && index < batch.Length - 1;

    /// <summary>Every office place is taken (OfficeCapacity; false while no building system sets a limit).</summary>
    public bool IsOfficeFull => OfficeCapacity.IsLimited && OfficeCapacity.IsFull;

    /// <summary>A hire would go through now: a CV on the desk and a free office place (ignoring input blocking).</summary>
    public bool CanHireNow => CanHire && !IsOfficeFull;

    /// <summary>A new batch can be requested (the current one is decided, or spawning failed).</summary>
    public bool CanStartNextBatch => !batchActive && !spawning;

    /// <summary>True while desk decisions have to wait (see IsDeskInputBlocked).</summary>
    public bool IsInputBlocked => lastBlocked;

    /// <summary>
    /// True while desk decisions must wait: a panel is open, the game is paused,
    /// or a mini-game / risk-card event is running.
    /// </summary>
    public static bool IsDeskInputBlocked()
    {
        if (Time.timeScale <= 0f) return true;
        if (UIManager.Exists && UIManager.Instance.IsAnyPanelOpen()) return true;
        if (MiniGameManager.Instance != null && MiniGameManager.Instance.IsMiniGameActive()) return true;
        if (EventManager.Instance != null && EventManager.Instance.IsEventInProgress()) return true;
        return false;
    }

    // ------------------------------------------------------------------

    private void OnEnable()
    {
        OfficeCapacity.Changed += MarkDirty;   // office full or free again: the desk tells its listeners
    }

    private void OnDisable()
    {
        OfficeCapacity.Changed -= MarkDirty;
    }

    private void Start()
    {
        if (nextBatchButton != null)
            nextBatchButton.gameObject.SetActive(false);

        lastBlocked = IsDeskInputBlocked();
        StartCoroutine(StartFirstBatchWhenLoaded());
    }

    /// <summary>
    /// Deal the first batch once SaveSystem has applied the save (a couple of frames after the scene loads),
    /// so loaded skills (applicant quality, golden chance) already count. Never waits more than a second.
    /// </summary>
    IEnumerator StartFirstBatchWhenLoaded()
    {
        float waited = 0f;
        while (SaveSystem.Instance != null && !SaveSystem.Instance.IsGameLoaded && waited < 1f)
        {
            waited += Time.unscaledDeltaTime;
            yield return null;
        }
        StartNewBatch();
    }

    private void Update()
    {
        bool blocked = IsDeskInputBlocked();
        if (blocked != lastBlocked)
        {
            lastBlocked = blocked;
            buttonsDirty = true;
        }

        if (buttonsDirty)
            ApplyButtonStates();
    }

    /// <summary>Replace whatever is on the desk with a fresh batch (not blocked by open panels).</summary>
    public void StartNewBatch()
    {
        if (!isActiveAndEnabled) return;

        if (spawnRoutine != null)
            StopCoroutine(spawnRoutine);
        spawnRoutine = StartCoroutine(SpawnBatchCoroutine());
    }

    IEnumerator SpawnBatchCoroutine()
    {
        spawning = true;
        batchActive = false;
        MarkDirty();

        ClearRemainingPapers();
        batch = new ApplicantData[0];
        index = 0;

        // Check for required references
        if (paperPrefab == null)
        {
            Debug.LogError("GameFlowManager: paperPrefab is not assigned!");
            FinishSpawning();
            yield break;
        }

        ApplicantGenerator generator = FindAnyObjectByType<ApplicantGenerator>();
        if (generator == null)
        {
            Debug.LogError("GameFlowManager: No ApplicantGenerator found in scene!");
            FinishSpawning();
            yield break;
        }

        Transform spawn = paperSpawnPoint != null ? paperSpawnPoint : transform;
        if (paperSpawnPoint == null)
            Debug.LogWarning("GameFlowManager: paperSpawnPoint is not assigned - spawning papers at the GameFlowManager.");

        // Generate applicants and spawn their papers
        List<ApplicantData> applicants = new List<ApplicantData>();
        int count = Mathf.Max(1, batchSize);
        for (int i = 0; i < count; i++)
        {
            ApplicantData applicant = null;
            try
            {
                applicant = generator.Generate();
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
            }
            if (applicant == null) continue;

            int slot = applicants.Count;
            GetPaperPose(slot, out Vector3 paperPos, out Quaternion paperRot);
            GameObject p = Instantiate(paperPrefab, paperPos, paperRot);

            // Fill paper data
            ApplicantUIBinder binder = p.GetComponent<ApplicantUIBinder>();
            if (binder != null)
            {
                try
                {
                    binder.Display(applicant);
                }
                catch (System.Exception e)
                {
                    Debug.LogException(e);
                }
            }
            else
            {
                Debug.LogError("GameFlowManager: Paper prefab is missing ApplicantUIBinder component!");
            }

            applicants.Add(applicant);
            currentPapers.Add(p);
        }

        batch = applicants.ToArray();
        index = 0;

        Debug.Log("Spawned " + currentPapers.Count + " papers at " + spawn.position);

        // Wait one frame so the papers' Animators are running before triggering the entrance
        yield return null;

        if (currentPapers.Count > 0) GameSfx.Play(Sfx.CardFan);   // a new batch of CVs lands on the desk

        // Play entrance animation for each paper with stagger
        if (playEntranceAnimation)
        {
            for (int i = 0; i < currentPapers.Count; i++)
            {
                if (currentPapers[i] == null) continue;
                PaperManager pm = currentPapers[i].GetComponent<PaperManager>();
                if (pm != null)
                {
                    // Stagger the entrance animations so they don't all play at once
                    pm.PlayEntranceDelayed(i * entranceStaggerDelay);
                }
            }
        }

        if (batch.Length == 0)
            Debug.LogWarning("GameFlowManager: No applicants could be generated - press Next Batch to try again.");

        FinishSpawning();
    }

    void FinishSpawning()
    {
        spawning = false;
        batchActive = batch != null && batch.Length > 0;
        spawnRoutine = null;
        MarkDirty();
    }

    /// <summary>Where the paper of a batch slot lies: the pile on the spawn point, each one 1 cm further back and
    /// turned a little around its face (paperStackAngles).</summary>
    public void GetPaperPose(int slot, out Vector3 position, out Quaternion rotation)
    {
        Transform spawn = paperSpawnPoint != null ? paperSpawnPoint : transform;
        position = spawn.position + new Vector3(0, 0, -0.01f * slot);
        float angle = paperStackAngles != null && paperStackAngles.Length > 0 ? paperStackAngles[slot % paperStackAngles.Length] : 0f;
        rotation = spawn.rotation * Quaternion.Euler(0f, 0f, angle);
    }

    // When SKIP button clicked
    public void SkipApplicant()
    {
        if (IsDeskInputBlocked() || !CanSkip) return; // Cannot skip last applicant

        SendPaperAway(index, hired: false);
        GameSfx.PlayPair(Sfx.UiScratch, Sfx.CardToss, 0.35f);   // SKIPPED stamp, then the paper flies off (stampHold)
        TryTrashToTreasure();

        index++;
        MarkDirty();
    }

    // When HIRE button clicked
    public void HireCurrent()
    {
        if (IsDeskInputBlocked() || !CanHire) return;
        if (IsOfficeFull)
        {
            RefuseHire();
            return;
        }

        ApplicantData applicant = batch[index];
        SendPaperAway(index, hired: true);
        GameSfx.PlayPair(Sfx.UiConfirm, Sfx.UiScratch, 0.1f);   // hired: the HIRED stamp lands as it scales in

        try
        {
            if (CompanyManager.Instance != null)
                CompanyManager.Instance.HireEmployee(applicant);
            else
                Debug.LogWarning("GameFlowManager: No CompanyManager in scene - the hire was not recorded.");
        }
        catch (System.Exception e)
        {
            // Never leave the desk stuck mid-batch
            Debug.LogException(e);
        }

        // Skip all remaining papers automatically
        for (int i = index + 1; i < currentPapers.Count; i++)
            SendPaperAway(i, hired: false);

        EndBatch();
    }

    /// <summary>Every office place is taken: say why (a toast at most every 1.5 s) and let the HUD's capacity chip shake.</summary>
    void RefuseHire()
    {
        GameSfx.Play(Sfx.UiError, 0.6f);
        if (Time.unscaledTime - lastRefusalToast >= 1.5f)
        {
            lastRefusalToast = Time.unscaledTime;
            GameNotifications.Show(TowerText.Get("tower.capacity.hire_refused",
                ("count", TowerText.EmployeesOf(OfficeCapacity.Employees, OfficeCapacity.Capacity))), NotificationKind.Bad);
        }
        try { HireRefused?.Invoke(); }
        catch (System.Exception e) { Debug.LogException(e); }
    }

    public void NextBatch()
    {
        if (IsDeskInputBlocked() || spawning) return;
        StartNewBatch();
    }

    void EndBatch()
    {
        batchActive = false;
        MarkDirty();
    }

    // Trash to Treasure skill: chance to get bonus from skipped applicants (Serendipity makes it more likely)
    void TryTrashToTreasure()
    {
        SkillTreeManager skills = SkillTreeManager.Instance;
        if (skills == null || CompanyManager.Instance == null) return;

        float skipBonusChance = skills.GetSkipBonus() * (1f + Mathf.Max(0f, skills.GetEffect(SkillEffect.Serendipity)));
        if (skipBonusChance > 0f && Random.value < Mathf.Min(skipBonusChance, 0.9f))
        {
            // Get the bonus amount from skill's effectValue2 (+25% with Corporate Enlightenment)
            Skill skipSkill = skills.GetSkill("chaos_1_skip");
            float bonusAmount = (skipSkill != null && skipSkill.effectValue2 > 0f ? skipSkill.effectValue2 : 500f) * skills.EffectMultiplier;
            CompanyManager.Instance.AddMoney(bonusAmount, true);
            Debug.Log("Trash to Treasure! Found $" + bonusAmount + " in the skipped application!");
            GameNotifications.Show(CompanyText.Get("company.luck.trash_to_treasure",
                ("skill", skipSkill != null ? skipSkill.DisplayName : CompanyText.SkillName("chaos_1_skip", "Trash to Treasure")),
                ("amount", UIFormat.Money(bonusAmount))), NotificationKind.Good);
        }
    }

    // Play the hire/skip animation and schedule the paper's removal. The slot is cleared right away.
    void SendPaperAway(int i, bool hired)
    {
        if (i < 0 || i >= currentPapers.Count) return;

        GameObject paper = currentPapers[i];
        currentPapers[i] = null;
        if (paper == null) return;

        PaperManager pm = paper.GetComponent<PaperManager>();
        if (pm != null)
        {
            Transform target = hired ? hiredBoxPoint : trashPoint;
            Vector3 targetPos = target != null ? target.position : paper.transform.position;
            if (hired)
                pm.PlayHire(targetPos);
            else
                pm.PlaySkip(targetPos);
        }

        Destroy(paper, Mathf.Max(0f, paperRemoveDelay));
    }

    // Remove papers still lying on the desk (papers already animating away are left alone)
    void ClearRemainingPapers()
    {
        foreach (var p in currentPapers)
        {
            if (p != null) Destroy(p);
        }
        currentPapers.Clear();
    }

    void MarkDirty()
    {
        buttonsDirty = true;
    }

    // Buttons show the logical state, and are non-interactable while input is blocked
    void ApplyButtonStates()
    {
        buttonsDirty = false;
        bool blocked = lastBlocked;

        if (hireButton != null)
            hireButton.interactable = CanHire && !blocked;
        if (skipButton != null)
            skipButton.interactable = CanSkip && !blocked;
        if (nextBatchButton != null)
        {
            bool show = CanStartNextBatch;
            if (nextBatchButton.gameObject.activeSelf != show)
                nextBatchButton.gameObject.SetActive(show);
            nextBatchButton.interactable = !blocked;
        }

        DeskStateChanged?.Invoke();
    }
}
