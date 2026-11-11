using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SkillNodeUI : MonoBehaviour
{
    [Header("UI Elements")]
    public Image backgroundImage;
    public Image iconImage;
    public Image borderImage;              // Unlocked glow; its GameObject is switched on when unlocked
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI tierText;
    public TextMeshProUGUI costText;       // Shows skill point cost
    public Button nodeButton;
    public GameObject lockedOverlay;

    [Header("Text")]
    [Tooltip("Use dark or light text depending on the state color so it stays readable. Turn off to use the fixed gray/yellow/white colors.")]
    public bool autoTextContrast = true;

    private Skill skill;
    private SkillTreeUI parentUI;
    private Color branchColor;

    static readonly Color DarkText = new Color(0.12f, 0.12f, 0.12f);
    static readonly Color GrayText = new Color(0.5f, 0.5f, 0.5f);

    public void Setup(Skill skill, Color branchColor, SkillTreeUI parentUI)
    {
        this.skill = skill;
        this.branchColor = branchColor;
        this.parentUI = parentUI;

        if (nodeButton == null)
            nodeButton = GetComponent<Button>();
        if (nodeButton != null)
        {
            nodeButton.onClick.RemoveListener(OnClick);
            nodeButton.onClick.AddListener(OnClick);
        }

        if (skill == null) return;

        if (nameText != null)
            nameText.text = skill.skillName;

        if (tierText != null)
            tierText.text = "T" + skill.tier;

        if (costText != null)
            costText.text = skill.cost + " SP";
    }

    public void SetState(bool unlocked, bool canUnlock, Color stateColor)
    {
        bool locked = !unlocked && !canUnlock;

        if (backgroundImage != null)
            backgroundImage.color = stateColor;

        if (lockedOverlay != null && lockedOverlay != gameObject)
            lockedOverlay.SetActive(locked);

        // Border glows when unlocked (acts as the unlocked glow).
        // It is an inactive child in the prefab, so its GameObject has to be switched on, not just the Image.
        if (borderImage != null)
        {
            if (borderImage.gameObject != gameObject)
                borderImage.gameObject.SetActive(unlocked);
            borderImage.enabled = unlocked;

            if (unlocked)
            {
                Color glow = branchColor;
                // Without a frame sprite the border is a solid rectangle over the whole node: keep it see-through
                if (borderImage.sprite == null)
                    glow.a = 0.45f;
                borderImage.color = glow;
            }
        }

        // Change text colors based on state
        Color nameColor, costColor, tierColor;
        if (autoTextContrast)
        {
            // Dark text on light state colors, light text on dark ones
            Color readable = stateColor.grayscale > 0.55f ? DarkText : Color.white;
            nameColor = costColor = tierColor = readable;
        }
        else
        {
            nameColor = locked ? GrayText : (canUnlock ? Color.yellow : Color.white);
            costColor = unlocked ? Color.green : (canUnlock ? Color.yellow : GrayText);
            tierColor = Color.white;
        }

        if (nameText != null)
            nameText.color = nameColor;

        if (tierText != null)
            tierText.color = tierColor;

        // Cost text styling
        if (costText != null)
        {
            if (unlocked)
                costText.text = "OWNED";
            else if (skill != null)
                costText.text = skill.cost + " SP";
            costText.color = costColor;
        }
    }

    void OnClick()
    {
        if (parentUI != null && skill != null)
        {
            parentUI.SelectSkill(skill);
        }
    }
}
