using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The designed upgrade choice sheet: what the item becomes, the price, the blackjack card (felt) and the dice card
/// with the real odds. OfficeUpgradeManager (writeTexts off) runs the flow through the sheet's buttons and sets
/// which ones can be pressed; this view writes the texts (Panels table) and the odds bar.
/// </summary>
[DisallowMultipleComponent]
public class UpgradeChoiceView : MonoBehaviour
{
    public OfficeUpgradeManager manager;
    public UpgradeFlowView flow;

    [Header("Header")]
    public Image itemIcon;
    public TMP_Text title;
    public TMP_Text levels;
    public TMP_Text cost;

    [Header("Blackjack card")]
    [Tooltip("\"About 42% to win\"")]
    public TMP_Text chance;

    [Header("Dice odds")]
    public RectTransform upgradeSegment;
    public RectTransform nothingSegment;
    public RectTransform loseSegment;
    public TMP_Text odds;

    [Header("Footer")]
    public TMP_Text footer;
    public Button closeButton;

    /// <summary>The design's chance to win one hand of blackjack, in percent.</summary>
    const int BlackjackWinPercent = 42;
    const string L10nTable = "Panels";

    OfficeItem shownItem;
    int shownLevel = -1;
    bool shownAfford;

    OfficeUpgradeManager Manager => manager != null ? manager : OfficeUpgradeManager.Instance;

    void Awake()
    {
        if (flow == null) flow = GetComponentInParent<UpgradeFlowView>(true);
        if (closeButton != null)
            closeButton.onClick.AddListener(() => { var m = Manager; if (m != null) m.CancelUpgrade(); });
        L10nFont.AttachAll(this);   // each language's font (English keeps the design font)
    }

    void OnEnable()
    {
        L10n.LanguageChanged += OnLanguageChanged;
        shownItem = null;
        Refresh();
    }

    void OnDisable()
    {
        L10n.LanguageChanged -= OnLanguageChanged;
    }

    void OnLanguageChanged() => Refresh();

    void LateUpdate()
    {
        var m = Manager;
        var data = m != null && m.CurrentItem != null ? m.CurrentItem.itemData : null;
        if (data != shownItem || (data != null && (data.currentLevel != shownLevel || Afford(data) != shownAfford)))
            Refresh();
    }

    static bool Afford(OfficeItem d) => CompanyManager.Instance != null && CompanyManager.Instance.companyValue >= d.GetUpgradeCost();

    void Refresh()
    {
        var m = Manager;
        var data = m != null && m.CurrentItem != null ? m.CurrentItem.itemData : null;
        shownItem = data;
        if (data == null) return;
        shownLevel = data.currentLevel;
        shownAfford = Afford(data);
        m.GetRandomChanceOdds(data, out float crit, out float success, out float critFail, out float fail);
        long money = CompanyManager.Instance != null ? (long)CompanyManager.Instance.companyValue : 0;
        Show(data, crit + success, fail, critFail, shownAfford, data.GetUpgradeCost() - money);
    }

    /// <summary>Fill the sheet for an item (also used for previews).</summary>
    public void Show(OfficeItem data, float upgrade, float nothing, float lose, bool afford, long shortBy)
    {
        if (flow == null) flow = GetComponentInParent<UpgradeFlowView>(true);
        int level = data.currentLevel;
        string price = UIFormat.Money(data.GetUpgradeCost());

        if (itemIcon != null && flow != null)
        {
            var icon = flow.IconFor(data.itemName);
            if (icon != null) itemIcon.sprite = icon;
        }
        if (title != null) title.text = L10n.Get(L10nTable, "upgrade.choice.title", ("item", data.DisplayName));
        if (levels != null)
        {
            levels.text = UpgradeFlowView.NamedLevel(data, level) + " › <b>" + UpgradeFlowView.NamedLevel(data, level + 1) + "</b>" +
                          "<space=18><color=" + UpgradeFlowView.Hex(UIColors.Pencil) + ">·</color><space=18>" +
                          UpgradeFlowView.Daily(data, level) + " › <b><color=" + UpgradeFlowView.Hex(UIColors.Money) + ">" +
                          UpgradeFlowView.Daily(data, level + 1) + "</color></b>";
        }
        if (cost != null) cost.text = L10n.Get(L10nTable, "upgrade.choice.cost.up_front", ("price", "<b>" + price + "</b>"));
        if (chance != null)
        {
            chance.text = L10n.Get(L10nTable, "upgrade.choice.blackjack.chance",
                ("chance", "<b><color=" + UpgradeFlowView.Hex(UIColors.Gold) + ">" + UpgradeFlowView.Percent(BlackjackWinPercent) + "</color></b>"));
        }

        // odds bar: upgrade | nothing | lose a level (the last one only once there's a level to lose)
        float total = Mathf.Max(0.0001f, upgrade + nothing + lose);
        float a = upgrade / total, b = (upgrade + nothing) / total;
        SetSegment(upgradeSegment, 0f, a);
        SetSegment(nothingSegment, a, b);
        SetSegment(loseSegment, b, 1f);
        if (loseSegment != null) loseSegment.gameObject.SetActive(lose > 0.0001f);
        if (nothingSegment != null)
        {
            nothingSegment.gameObject.SetActive(nothing > 0.0001f);
            var edge = nothingSegment.Find("EdgeRight");
            if (edge != null) edge.gameObject.SetActive(lose > 0.0001f);   // no divider against the bar's own border
        }
        if (odds != null)
        {
            string sep = "<space=8><color=" + UpgradeFlowView.Hex(UIColors.Ink) + ">·</color><space=8>";
            string text = Colored(UIColors.Money, L10n.Get(L10nTable, "upgrade.choice.odds.upgrade", ("percent", Pct(upgrade))));
            if (nothing > 0.0001f) text += sep + Colored(UIColors.InkSoft, L10n.Get(L10nTable, "upgrade.choice.odds.nothing", ("percent", Pct(nothing))));
            if (lose > 0.0001f) text += sep + Colored(UIColors.Loss, L10n.Get(L10nTable, "upgrade.choice.odds.lose", ("percent", Pct(lose))));
            odds.text = text;
        }

        if (footer != null)
        {
            footer.text = afford
                ? L10n.Get(L10nTable, "upgrade.choice.footer", ("price", price))
                : Colored(UIColors.Loss, L10n.Get(L10nTable, "upgrade.choice.footer.short", ("amount", UIFormat.Money(System.Math.Max(0L, shortBy)))));
        }
    }

    /// <summary>A chance as a whole number of percent (the entries write the % sign).</summary>
    static string Pct(float chance) => UpgradeFlowView.Int(Mathf.RoundToInt(chance * 100f));

    static string Colored(Color color, string text) => "<color=" + UpgradeFlowView.Hex(color) + ">" + text + "</color>";

    static void SetSegment(RectTransform seg, float from, float to)
    {
        if (seg == null) return;
        seg.anchorMin = new Vector2(from, 0f);
        seg.anchorMax = new Vector2(to, 1f);
        seg.offsetMin = seg.offsetMax = Vector2.zero;
    }
}
