using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// Skill tree browser panel.
/// Works both when opened through OpenPanel() / TogglePanel() and when the panel GameObject is
/// switched on directly (e.g. a HUD button calling SetActive(true)).
/// While visible it is registered with UIManager as a non-pausing panel, so it still blocks
/// risk-card events and world interaction. Escape closes the details popup first, then the panel.
/// </summary>
public class SkillTreeUI : MonoBehaviour
{
    [Header("Main Panel")]
    [Tooltip("Panel root. May be this object; if left empty this object is used.")]
    public GameObject skillTreePanel;
    public Button closeButton;

    [Header("External Open Button (Optional)")]
    [Tooltip("Optional - you can also call OpenPanel() or TogglePanel() from any button's OnClick event. " +
             "This field is hooked up when this component wakes up, so if the component sits on a panel that starts inactive, use the button's OnClick instead.")]
    public Button openButton;

    [Header("Skill Points Display")]
    public TextMeshProUGUI skillPointsText;
    public TextMeshProUGUI progressText;
    public Image progressBar;

    [Header("Company Info")]
    public TextMeshProUGUI companyValueText;

    [Header("Branch Tabs")]
    public Button growthTabButton;
    public Button productivityTabButton;
    public Button chaosTabButton;
    public Image growthTabHighlight;
    public Image productivityTabHighlight;
    public Image chaosTabHighlight;

    [Header("Skill Grid")]
    public Transform skillGridParent;
    public GameObject skillNodePrefab;

    [Header("Skill Details Panel")]
    public GameObject detailsPanel;
    public Button detailsCloseButton;  // Close button for details panel
    public TextMeshProUGUI detailsName;
    public TextMeshProUGUI detailsDescription;
    public TextMeshProUGUI detailsCost;
    public TextMeshProUGUI detailsPrereqs;
    public Button unlockButton;
    public TextMeshProUGUI unlockButtonText;

    [Header("Branch Colors")]
    public Color growthColor = new Color(0.2f, 0.8f, 0.3f);      // Green
    public Color productivityColor = new Color(0.3f, 0.5f, 0.9f); // Blue
    public Color chaosColor = new Color(0.9f, 0.3f, 0.8f);        // Purple/Pink

    [Header("Node Colors")]
    public Color lockedColor = new Color(0.3f, 0.3f, 0.3f);
    public Color availableColor = new Color(0.9f, 0.9f, 0.5f);
    public Color unlockedColor = new Color(0.5f, 1f, 0.5f);

    const string SecretSkillId = SkillTreeManager.SecretSkillId;

    private SkillBranch currentBranch = SkillBranch.Growth;
    private Skill selectedSkill;
    private Dictionary<string, SkillNodeUI> skillNodes = new Dictionary<string, SkillNodeUI>();
    private bool initialized;
    private bool isShown;
    private bool bringToFrontPending;
    private bool skillPointsTextFitted;
    private SkillTreeManager subscribedManager;

    /// <summary>True while the skill tree is on screen.</summary>
    public bool IsOpen => isShown;

    GameObject Panel => skillTreePanel != null ? skillTreePanel : gameObject;

    // True when this component sits on the panel (or inside it), so it is enabled and disabled with it
    bool LivesOnPanel => skillTreePanel == null || skillTreePanel == gameObject || transform.IsChildOf(skillTreePanel.transform);

    void Awake()
    {
        Initialize();
    }

    void Start()
    {
        // Hide a separately referenced panel at start. Never hide the panel this component sits on:
        // Start runs the first time that panel is opened, so hiding it here would close it straight away.
        if (!LivesOnPanel && !isShown && skillTreePanel.activeSelf)
            skillTreePanel.SetActive(false);
    }

    void OnEnable()
    {
        Initialize();
        SubscribeToManager();

        // This component lives on the panel: being enabled means the panel was just shown
        if (LivesOnPanel)
            HandleShown();
    }

    void OnDisable()
    {
        UnsubscribeFromManager();
        if (isShown)
            HandleHidden();
    }

    void OnDestroy()
    {
        UnsubscribeFromManager();
        if (UIManager.Exists)
            UIManager.Instance.ClosePanel(this);
    }

    void Update()
    {
        // A separate panel can be shown or hidden from outside (e.g. a button calling SetActive)
        if (!LivesOnPanel)
        {
            bool visible = skillTreePanel.activeInHierarchy;
            if (visible && !isShown)
                HandleShown();
            else if (!visible && isShown)
                HandleHidden();
        }

        if (!isShown) return;

        // Pick up a SkillTreeManager that appeared after the panel opened
        if (subscribedManager == null && SkillTreeManager.Instance != null)
            Refresh();

        if (bringToFrontPending)
        {
            // Done a frame late: the sibling order shouldn't change while the panel is being activated
            bringToFrontPending = false;
            UIManager.BringToFront(Panel);
        }
    }

    void Initialize()
    {
        if (initialized) return;
        initialized = true;

        if (openButton != null)
            openButton.onClick.AddListener(OpenPanel);
        if (closeButton != null)
            closeButton.onClick.AddListener(ClosePanel);

        if (growthTabButton != null)
            growthTabButton.onClick.AddListener(SelectGrowthBranch);
        if (productivityTabButton != null)
            productivityTabButton.onClick.AddListener(SelectProductivityBranch);
        if (chaosTabButton != null)
            chaosTabButton.onClick.AddListener(SelectChaosBranch);

        if (unlockButton != null)
            unlockButton.onClick.AddListener(UnlockSelectedSkill);

        if (detailsCloseButton != null)
            detailsCloseButton.onClick.AddListener(CloseDetailsPanel);
    }

    // ------------------------------------------------------------------
    // Open / close
    // ------------------------------------------------------------------

    /// <summary>Show the skill tree, refreshed and drawn above other panels.</summary>
    public void OpenPanel()
    {
        Initialize();
        GameObject panel = Panel;
        bool wasShown = isShown;

        if (!panel.activeSelf)
        {
            UIManager.BringToFront(panel); // Reorder while still inactive
            panel.SetActive(true);         // When this component is on the panel, OnEnable does the rest
        }

        if (!panel.activeInHierarchy) return; // A parent is hidden

        if (!isShown)
        {
            HandleShown();
        }
        else if (wasShown)
        {
            // Already open: move it back on top
            RegisterWithUIManager();
            UIManager.BringToFront(panel);
        }
    }

    /// <summary>Hide the skill tree.</summary>
    public void ClosePanel()
    {
        CloseDetailsPanel();

        GameObject panel = Panel;
        if (panel.activeSelf)
            panel.SetActive(false); // When this component is on the panel, OnDisable unregisters it

        if (isShown)
            HandleHidden();
    }

    public void TogglePanel()
    {
        if (isShown && Panel.activeInHierarchy)
            ClosePanel();
        else
            OpenPanel();
    }

    void HandleShown()
    {
        isShown = true;
        CloseDetailsPanel();
        Refresh();
        RegisterWithUIManager();
        bringToFrontPending = true;
    }

    void HandleHidden()
    {
        isShown = false;
        bringToFrontPending = false;
        selectedSkill = null;

        if (UIManager.Exists)
            UIManager.Instance.ClosePanel(this);
    }

    void RegisterWithUIManager()
    {
        // Browsing panel: doesn't pause, but blocks events and world interaction
        UIManager ui = UIManager.Instance;
        if (ui != null)
            ui.OpenPanel(this, pauseGame: false, onEscape: HandleEscape);
    }

    void HandleEscape()
    {
        if (detailsPanel != null && detailsPanel.activeSelf)
            CloseDetailsPanel();
        else
            ClosePanel();
    }

    /// <summary>Redraw skill points, company value and the current branch.</summary>
    public void Refresh()
    {
        SubscribeToManager();
        UpdateSkillPointsDisplay();
        UpdateCompanyValueDisplay();
        SelectBranch(currentBranch);
        UpdateUnlockButton();
    }

    // ------------------------------------------------------------------
    // SkillTreeManager events
    // ------------------------------------------------------------------

    void SubscribeToManager()
    {
        SkillTreeManager manager = SkillTreeManager.Instance;
        if (manager == null || ReferenceEquals(manager, subscribedManager)) return;

        UnsubscribeFromManager();
        manager.SkillPointsChanged += OnSkillPointsChanged;
        manager.SkillUnlocked += OnSkillUnlocked;
        subscribedManager = manager;
    }

    void UnsubscribeFromManager()
    {
        // ReferenceEquals: also unsubscribe from a manager that was already destroyed
        if (!ReferenceEquals(subscribedManager, null))
        {
            subscribedManager.SkillPointsChanged -= OnSkillPointsChanged;
            subscribedManager.SkillUnlocked -= OnSkillUnlocked;
        }
        subscribedManager = null;
    }

    void OnSkillPointsChanged(int points)
    {
        if (!isShown) return; // Refreshed when opened
        UpdateSkillPointsDisplay();
        UpdateAllNodeStates();
        UpdateUnlockButton();
    }

    void OnSkillUnlocked(Skill skill)
    {
        if (!isShown) return;
        UpdateCompanyValueDisplay(); // Refresh after spending points

        // Unlocking the last regular skill reveals the secret one (it sits in the Growth branch)
        SkillTreeManager manager = SkillTreeManager.Instance;
        if (currentBranch == SkillBranch.Growth && manager != null && !skillNodes.ContainsKey(SecretSkillId) &&
            manager.IsSecretSkillRevealed())
            RefreshSkillGrid();
    }

    // ------------------------------------------------------------------
    // Branches and grid
    // ------------------------------------------------------------------

    public void SelectGrowthBranch() => SelectBranch(SkillBranch.Growth);
    public void SelectProductivityBranch() => SelectBranch(SkillBranch.Productivity);
    public void SelectChaosBranch() => SelectBranch(SkillBranch.Chaos);

    public void SelectBranch(SkillBranch branch)
    {
        currentBranch = branch;

        // Update tab highlights
        if (growthTabHighlight != null)
            growthTabHighlight.enabled = (branch == SkillBranch.Growth);
        if (productivityTabHighlight != null)
            productivityTabHighlight.enabled = (branch == SkillBranch.Productivity);
        if (chaosTabHighlight != null)
            chaosTabHighlight.enabled = (branch == SkillBranch.Chaos);

        RefreshSkillGrid();
    }

    Color GetBranchColor(SkillBranch branch)
    {
        return branch switch
        {
            SkillBranch.Growth => growthColor,
            SkillBranch.Productivity => productivityColor,
            SkillBranch.Chaos => chaosColor,
            _ => Color.white
        };
    }

    void RefreshSkillGrid()
    {
        skillNodes.Clear();
        if (skillGridParent == null) return;

        // Clear existing nodes
        for (int i = skillGridParent.childCount - 1; i >= 0; i--)
            Destroy(skillGridParent.GetChild(i).gameObject);

        SkillTreeManager manager = SkillTreeManager.Instance;
        if (manager == null) return;

        if (skillNodePrefab == null)
        {
            Debug.LogWarning("[SkillTreeUI] skillNodePrefab is not assigned, so the skill grid can't be shown.");
            return;
        }

        List<Skill> branchSkills = manager.GetSkillsByBranch(currentBranch);
        Color branchColor = GetBranchColor(currentBranch);

        foreach (Skill skill in branchSkills)
        {
            // The secret skill stays hidden until every other skill is unlocked
            if (skill.id == SecretSkillId && !manager.IsSecretSkillRevealed()) continue;

            GameObject nodeObj = Instantiate(skillNodePrefab, skillGridParent);
            SkillNodeUI nodeUI = nodeObj.GetComponent<SkillNodeUI>();

            if (nodeUI != null)
            {
                nodeUI.Setup(skill, branchColor, this);
                skillNodes[skill.id] = nodeUI;
            }
        }

        UpdateAllNodeStates();
    }

    void UpdateAllNodeStates()
    {
        SkillTreeManager manager = SkillTreeManager.Instance;
        if (manager == null) return;

        foreach (var kvp in skillNodes)
        {
            if (kvp.Value == null) continue;

            Skill skill = manager.GetSkill(kvp.Key);
            if (skill == null) continue;

            bool canUnlock = manager.CanUnlockSkill(skill);

            Color nodeColor;
            if (skill.unlocked)
                nodeColor = unlockedColor;
            else if (canUnlock)
                nodeColor = availableColor;
            else
                nodeColor = lockedColor;

            kvp.Value.SetState(skill.unlocked, canUnlock, nodeColor);
        }
    }

    // ------------------------------------------------------------------
    // Details popup
    // ------------------------------------------------------------------

    public void SelectSkill(Skill skill)
    {
        if (skill == null) return;
        selectedSkill = skill;

        if (detailsPanel != null)
            detailsPanel.SetActive(true);

        if (detailsName != null)
            detailsName.text = skill.skillName;

        if (detailsDescription != null)
            detailsDescription.text = skill.description;

        if (detailsCost != null)
            detailsCost.text = "Cost: " + skill.cost + " SP";

        // Show prerequisites
        if (detailsPrereqs != null)
            detailsPrereqs.text = BuildPrerequisiteText(skill);

        // Update unlock button
        UpdateUnlockButton();
    }

    string BuildPrerequisiteText(Skill skill)
    {
        if (skill.prerequisites == null || skill.prerequisites.Length == 0)
            return "No prerequisites";

        SkillTreeManager manager = SkillTreeManager.Instance;
        List<string> parts = new List<string>();
        foreach (string prereqId in skill.prerequisites)
        {
            Skill prereq = manager != null ? manager.GetSkill(prereqId) : null;
            string prereqName = prereq != null ? prereq.skillName : prereqId;
            bool owned = prereq != null && prereq.unlocked;
            parts.Add(prereqName + (owned ? " (unlocked)" : " (locked)"));
        }
        return "Requires: " + string.Join(", ", parts);
    }

    public void CloseDetailsPanel()
    {
        if (detailsPanel != null && detailsPanel.activeSelf)
            detailsPanel.SetActive(false);
        selectedSkill = null;
    }

    void UpdateUnlockButton()
    {
        if (unlockButton == null || selectedSkill == null) return;

        SkillTreeManager manager = SkillTreeManager.Instance;

        if (selectedSkill.unlocked)
        {
            unlockButton.interactable = false;
            SetUnlockButtonText("UNLOCKED");
        }
        else if (manager != null && manager.CanUnlockSkill(selectedSkill))
        {
            unlockButton.interactable = true;
            SetUnlockButtonText("UNLOCK");
        }
        else
        {
            unlockButton.interactable = false;
            if (manager != null && manager.ArePrerequisitesMet(selectedSkill) && manager.skillPoints < selectedSkill.cost)
                SetUnlockButtonText("NEED MORE SP");
            else
                SetUnlockButtonText("LOCKED");
        }
    }

    void SetUnlockButtonText(string text)
    {
        if (unlockButtonText != null)
            unlockButtonText.text = text;
    }

    public void UnlockSelectedSkill()
    {
        if (selectedSkill == null) return;

        SkillTreeManager manager = SkillTreeManager.Instance;
        if (manager == null) return;

        if (manager.UnlockSkill(selectedSkill.id))
        {
            UpdateAllNodeStates();
            UpdateUnlockButton();
            UpdateSkillPointsDisplay();
            UpdateCompanyValueDisplay();
        }
    }

    // ------------------------------------------------------------------
    // Header texts
    // ------------------------------------------------------------------

    void UpdateSkillPointsDisplay()
    {
        SkillTreeManager manager = SkillTreeManager.Instance;
        if (manager == null) return;

        int hiresNeeded = manager.GetHiresUntilNextPoint();

        if (skillPointsText != null)
        {
            if (progressText != null)
            {
                skillPointsText.text = "Skill Points: " + manager.skillPoints;
            }
            else
            {
                // No separate progress label: explain how the next point is earned here,
                // shrinking the text (never growing it) so both lines fit
                if (!skillPointsTextFitted)
                {
                    skillPointsTextFitted = true;
                    float baseSize = skillPointsText.fontSize;
                    skillPointsText.fontSizeMax = baseSize;
                    skillPointsText.fontSizeMin = Mathf.Min(12f, baseSize);
                    skillPointsText.enableAutoSizing = true;
                }
                skillPointsText.text = "Skill Points: " + manager.skillPoints +
                                       "\nNext point: " + hiresNeeded + " more hires with IQ " + manager.iqThreshold + "+";
            }
        }

        if (progressText != null)
            progressText.text = hiresNeeded + " high-IQ hires until next SP";

        if (progressBar != null)
            progressBar.fillAmount = manager.GetSkillPointProgress();
    }

    void UpdateCompanyValueDisplay()
    {
        if (companyValueText == null) return;

        if (CompanyManager.Instance != null)
        {
            companyValueText.text = "$" + CompanyManager.Instance.companyValue.ToString("N0");
        }
        else
        {
            companyValueText.text = "$0";
        }
    }
}
