using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// One sticky-note tab of the office toolbar (Skills, Staff, Stocks, Casino, Build, Online).
/// Open: coloured sticky note. Locked: dashed paper-shade tab with a lock badge; hovering explains how to unlock.
/// Badges: a red count (skill points) or a "NEW" pill (a feature that just unlocked).
/// OfficeHud sets the state and handles clicks. The hints are written in the current language (OfficeHud, FeatureTabs);
/// the title comes from the Hud String Table (titleKey).
/// </summary>
[DisallowMultipleComponent]
public class HudTab : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    const string Table = "Hud";   // Assets/Localization/Source/Hud.csv

    public Button button;
    [Tooltip("Moves down a little on hover (the note hanging from the top edge)")]
    public RectTransform swing;
    public GameObject openLook;
    public GameObject lockedLook;
    public GameObject countBadge;
    public TMP_Text countText;
    public GameObject newBadge;

    [Header("Tooltip")]
    public string title = "Tab";
    [Tooltip("Hud String Table key of the title, the tooltip's bold word (empty: the title as written)")]
    public string titleKey = "";
    [TextArea] public string lockedHint = "";
    [TextArea] public string openHint = "";

    public bool IsLocked { get; private set; }

    HudTooltip tooltip;
    Vector2 swingHome;
    bool hovered;
    int shownCount = -1;
    bool stateKnown;

    void Awake()
    {
        if (swing != null)
            swingHome = swing.anchoredPosition;
        tooltip = GetComponentInParent<HudTooltip>(true);
        if (tooltip == null)
            tooltip = FindAnyObjectByType<HudTooltip>(FindObjectsInactive.Include);
    }

    void OnDisable()
    {
        if (hovered && tooltip != null)
            tooltip.Hide(this);
        hovered = false;
        if (swing != null)
            swing.anchoredPosition = swingHome;
    }

    public void SetLocked(bool locked)
    {
        if (stateKnown && locked == IsLocked) return;
        stateKnown = true;
        IsLocked = locked;
        if (openLook != null) openLook.SetActive(!locked);
        if (lockedLook != null) lockedLook.SetActive(locked);
        if (hovered)
            ShowTooltip();
    }

    public void SetCount(int count)
    {
        if (count == shownCount) return;
        shownCount = count;
        if (countBadge != null)
            countBadge.SetActive(count > 0);
        if (countText != null && count > 0)
            countText.text = count > 99 ? "99+" : count.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public void SetNew(bool isNew)
    {
        if (newBadge != null && newBadge.activeSelf != isNew)
            newBadge.SetActive(isNew);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        hovered = true;
        if (swing != null)
            swing.anchoredPosition = swingHome + new Vector2(0f, IsLocked ? 0f : -4f);
        ShowTooltip();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        hovered = false;
        if (swing != null)
            swing.anchoredPosition = swingHome;
        if (tooltip != null)
            tooltip.Hide(this);
    }

    void ShowTooltip()
    {
        if (tooltip == null) return;
        string hint = IsLocked ? lockedHint : openHint;
        if (string.IsNullOrEmpty(hint))
        {
            tooltip.Hide(this);
            return;
        }
        tooltip.Show(this, transform as RectTransform, string.IsNullOrEmpty(titleKey) ? title : L10n.Get(Table, titleKey), hint);
    }
}
