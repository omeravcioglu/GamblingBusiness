using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The designed upgrade result sheet: a rubber stamp (or the dashed dice box when nothing happened), what happened,
/// the item's state card with level pips, and the money line (texts: Panels table). Reads OfficeUpgradeManager.LastResult /
/// LastCost / LastPrice / LastItem, which the manager records right before it shows this page.
/// </summary>
[DisallowMultipleComponent]
public class UpgradeResultView : MonoBehaviour
{
    public OfficeUpgradeManager manager;
    public UpgradeFlowView flow;

    [Header("Stamp slot")]
    public GameObject stamp;
    public TMP_Text stampText;
    public Image[] stampLines;
    public GameObject noStamp;

    [Header("Texts")]
    public TMP_Text title;
    public TMP_Text sub;
    public TMP_Text money;

    [Header("State card")]
    public Image stateIcon;
    public TMP_Text stateName;
    public TMP_Text stateLine;
    public Image[] pips;

    public Button closeButton;

    const string L10nTable = "Panels";
    /// <summary>A stamp word wider than "UPGRADED!" (283 px) tilts less, or it would reach the title below the 104 px slot.</summary>
    const float LongStampWidth = 320f;

    OfficeUpgradeManager.ResultKind shownKind;
    OfficeItem shownData;
    int shownPrice;
    bool shownBust;

    OfficeUpgradeManager Manager => manager != null ? manager : OfficeUpgradeManager.Instance;

    void Awake()
    {
        if (flow == null) flow = GetComponentInParent<UpgradeFlowView>(true);
        if (closeButton != null)
            closeButton.onClick.AddListener(() => { var m = Manager; if (m != null) m.CloseRandomResult(); });
        L10nFont.AttachAll(this);   // each language's font (English keeps the design font)
    }

    void OnEnable()
    {
        L10n.LanguageChanged += OnLanguageChanged;
        var m = Manager;
        if (m == null || m.LastItem == null) return;
        var bj = BlackjackGame.Instance;
        bool bust = bj != null && bj.PlayerTotal > 21;
        Show(m.LastResult, m.LastItem, m.LastPrice, bust);
    }

    void OnDisable()
    {
        L10n.LanguageChanged -= OnLanguageChanged;
    }

    void OnLanguageChanged()
    {
        if (shownData != null) Show(shownKind, shownData, shownPrice, shownBust);
    }

    /// <summary>Fill the sheet (also used for previews). price = the upgrade price of the attempt.</summary>
    public void Show(OfficeUpgradeManager.ResultKind kind, OfficeItem data, int price, bool playerBust)
    {
        if (flow == null) flow = GetComponentInParent<UpgradeFlowView>(true);
        shownKind = kind;
        shownData = data;
        shownPrice = price;
        shownBust = playerBust;

        string item = data.DisplayName;
        string level = data.GetCurrentLevelName();
        string paid = UIFormat.Money(price);
        string stays = L10n.Get(L10nTable, "upgrade.result.sub.stays", ("item", item), ("level", level));

        string stampKey = null, titleText, subText = "", moneyText = "";
        Color stampColor = UIColors.Money, moneyColor = UIColors.Loss;
        switch (kind)
        {
            case OfficeUpgradeManager.ResultKind.Upgraded:
            case OfficeUpgradeManager.ResultKind.BlackjackWin:
                stampKey = "upgrade.result.stamp.upgraded";
                titleText = L10n.Get(L10nTable, "upgrade.result.title.is_now", ("item", item), ("level", level));
                if (kind == OfficeUpgradeManager.ResultKind.BlackjackWin) subText = L10n.Get(L10nTable, "upgrade.result.sub.won");
                moneyText = L10n.Get(L10nTable, "upgrade.result.money.paid", ("amount", UIFormat.Minus + paid));
                moneyColor = UIColors.InkSoft;
                break;
            case OfficeUpgradeManager.ResultKind.UpgradedFree:
                stampKey = "upgrade.result.stamp.upgraded";
                titleText = L10n.Get(L10nTable, "upgrade.result.title.is_now", ("item", item), ("level", level));
                subText = L10n.Get(L10nTable, "upgrade.result.sub.free");
                moneyText = L10n.Get(L10nTable, "upgrade.result.money.free", ("amount", paid));
                moneyColor = UIColors.Money;
                break;
            case OfficeUpgradeManager.ResultKind.Downgraded:
                stampKey = "upgrade.result.stamp.downgraded";
                stampColor = UIColors.Loss;
                titleText = L10n.Get(L10nTable, "upgrade.result.title.is_now.numbered", ("item", item), ("level", level),
                    ("number", UpgradeFlowView.Int(data.currentLevel)));
                moneyText = UIFormat.Minus + paid;
                break;
            case OfficeUpgradeManager.ResultKind.BlackjackLoss:
                stampKey = playerBust ? "upgrade.result.stamp.bust" : "upgrade.result.stamp.lost";
                stampColor = UIColors.Loss;
                titleText = L10n.Get(L10nTable, "upgrade.result.title.lost", ("amount", paid));
                subText = stays;
                break;
            case OfficeUpgradeManager.ResultKind.BlackjackPush:
                stampKey = "upgrade.result.stamp.push";
                stampColor = UIColors.Info;
                titleText = L10n.Get(L10nTable, "upgrade.result.title.push");
                subText = stays;
                moneyText = L10n.Get(L10nTable, "upgrade.result.money.push", ("amount", paid));
                moneyColor = UIColors.InkSoft;
                break;
            default: // Nothing
                titleText = L10n.Get(L10nTable, "upgrade.result.title.nothing");
                subText = L10n.Get(L10nTable, "upgrade.result.sub.nothing", ("item", item), ("level", level));
                moneyText = UIFormat.Minus + paid;
                break;
        }

        bool hasStamp = stampKey != null;
        if (stamp != null) stamp.SetActive(hasStamp);
        if (noStamp != null) noStamp.SetActive(!hasStamp);
        if (hasStamp)
        {
            // the entries hold the stamp's capitals in each language's own case
            string stampWord = L10n.Get(L10nTable, stampKey);
            float wordWidth = 0f;
            if (stampText != null)
            {
                stampText.text = stampWord;
                stampText.color = stampColor;
                wordWidth = stampText.GetPreferredValues(stampWord).x;
            }
            // a long word tilted 10 degrees would reach the title below the 104 px slot
            if (stamp != null) stamp.transform.localEulerAngles = new Vector3(0f, 0f, wordWidth > LongStampWidth ? 6f : 10f);
            if (stampLines != null)
                foreach (var line in stampLines)
                    if (line != null) line.color = stampColor;
        }

        if (title != null) title.text = titleText;
        if (sub != null)
        {
            sub.text = subText;
            sub.gameObject.SetActive(subText.Length > 0);
        }
        if (money != null)
        {
            money.text = moneyText;
            money.color = moneyColor;
            money.gameObject.SetActive(moneyText.Length > 0);
        }

        if (stateIcon != null && flow != null)
        {
            var icon = flow.IconFor(data.itemName);
            if (icon != null) stateIcon.sprite = icon;
        }
        if (stateName != null) stateName.text = item + " · <b>" + level + "</b>";
        if (stateLine != null) stateLine.text = UpgradeFlowView.LevelLine(data);
        UpgradeFlowView.SetPips(pips, data.currentLevel, data.maxLevel);
    }
}
