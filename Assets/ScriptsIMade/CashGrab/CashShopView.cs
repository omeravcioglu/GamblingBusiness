using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The cash floor's equipment shop (B, or the Shop door): collection tools on the left, counter-gadgets on the right, all
/// paid from Player Cash. Tools unlock with office seats (expand the HQ in build mode), gadgets when the department they
/// counter shows up. A pausing UIManager panel while open (shopping takes no office time); Escape or B closes it.
/// </summary>
public class CashShopView : MonoBehaviour
{
    class Row
    {
        public CashTool tool;
        public CashGadget gadget = CashGadget.None;
        public BlueprintBox box;
        public RawImage icon;
        public TextMeshProUGUI name, blurb, stats, action;
        public Button button;
        public BlueprintBox buttonBox;
        public Image[] pips;
    }

    CashFloorView view;
    TMP_FontAsset font;
    RectTransform root;
    TextMeshProUGUI cashText, messageText;
    readonly Row[] toolRows = new Row[CashRules.ToolCount];
    readonly Row[] gadgetRows = new Row[CashRules.GadgetCount];
    float refreshTimer;
    float messageTimer;
    Row highlight;
    float highlightTimer;

    public bool IsOpen { get; private set; }

    public static CashShopView Create(CashFloorView owner, RectTransform parent, TMP_FontAsset font)
    {
        RectTransform rect = BlueprintUI.Stretch(BlueprintUI.NewRect("Shop", parent));
        var shop = rect.gameObject.AddComponent<CashShopView>();
        shop.view = owner;
        shop.font = font;
        shop.root = rect;
        shop.Build();
        rect.gameObject.SetActive(false);
        return shop;
    }

    TextMeshProUGUI Label(Transform parent, string name, string text, float size, Color color, TextAlignmentOptions align = TextAlignmentOptions.Left, bool bold = false)
    {
        return BlueprintUI.Text(parent, name, text, size, color, font, align, bold);
    }

    void Build()
    {
        BlueprintBox scrim = BlueprintUI.AddBox(BlueprintUI.Stretch(BlueprintUI.NewRect("Scrim", root)), UIColors.Scrim, Color.clear, 0f, Vector4.zero, true);
        scrim.raycastTarget = true;

        BlueprintBox sheet = BlueprintUI.Box(root, "Sheet", UIColors.Paper, UIColors.Ink, 3f, BlueprintUI.RadiusCard, true);
        BlueprintUI.Centered(sheet.rectTransform, 1240f, 820f);
        sheet.SetShadow(UIColors.Dark(0.6f), new Vector2(8f, -10f));

        TextMeshProUGUI title = Label(sheet.transform, "Title", "", 40f, UIColors.Ink, TextAlignmentOptions.Left, true);
        BlueprintUI.TopLeft(title.rectTransform, 40f, 26f, 600f, 52f);
        title.name = "ShopTitle";
        TextMeshProUGUI sub = BlueprintUI.Wrap(Label(sheet.transform, "Sub", "", 18f, UIColors.InkSoft));
        BlueprintUI.TopLeft(sub.rectTransform, 40f, 80f, 760f, 50f);
        sub.name = "ShopSub";

        TextMeshProUGUI cashLabel = BlueprintUI.Caps(Label(sheet.transform, "CashLabel", "", 16f, UIColors.Pencil, TextAlignmentOptions.Right));
        BlueprintUI.TopRight(cashLabel.rectTransform, 110f, 28f, 300f, 22f);
        cashLabel.name = "ShopCashLabel";
        cashText = Label(sheet.transform, "Cash", "", 36f, UIColors.Money, TextAlignmentOptions.Right, true);
        BlueprintUI.TopRight(cashText.rectTransform, 110f, 50f, 360f, 46f);
        BlueprintUI.AutoSize(cashText, 20f);

        BlueprintBox close = BlueprintUI.Box(sheet.transform, "Close", UIColors.PaperHi, UIColors.Ink, 2.5f, BlueprintUI.RadiusSmall, true);
        BlueprintUI.TopRight(close.rectTransform, 30f, 30f, 56f, 56f);
        close.SetShadow(UIColors.Ink, new Vector2(0f, -3f));
        BlueprintUI.MakeButton(close).onClick.AddListener(Close);
        BlueprintIcon x = BlueprintUI.Icon(close.transform, BlueprintIconKind.Close, 26f, UIColors.Ink, 3f);
        x.rectTransform.anchorMin = x.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        x.rectTransform.anchoredPosition = Vector2.zero;

        TextMeshProUGUI toolsHead = BlueprintUI.Caps(Label(sheet.transform, "ToolsHead", "", 17f, UIColors.Pencil));
        BlueprintUI.TopLeft(toolsHead.rectTransform, 40f, 138f, 560f, 24f);
        toolsHead.name = "ShopToolsHead";
        TextMeshProUGUI gadgetsHead = BlueprintUI.Caps(Label(sheet.transform, "GadgetsHead", "", 17f, UIColors.Pencil));
        BlueprintUI.TopLeft(gadgetsHead.rectTransform, 640f, 138f, 560f, 24f);
        gadgetsHead.name = "ShopGadgetsHead";

        for (int t = 0; t < CashRules.ToolCount; t++)
            toolRows[t] = BuildRow(sheet.transform, 40f, 168f + t * 104f, (CashTool)t, CashGadget.None);
        for (int g = 0; g < CashRules.GadgetCount; g++)
            gadgetRows[g] = BuildRow(sheet.transform, 640f, 168f + g * 104f, CashTool.Hands, (CashGadget)g);

        messageText = BlueprintUI.Wrap(Label(sheet.transform, "Message", "", 19f, UIColors.Loss, TextAlignmentOptions.Center, true));
        messageText.rectTransform.anchorMin = new Vector2(0.5f, 0f);
        messageText.rectTransform.anchorMax = new Vector2(0.5f, 0f);
        messageText.rectTransform.pivot = new Vector2(0.5f, 0f);
        messageText.rectTransform.anchoredPosition = new Vector2(300f, 30f);
        messageText.rectTransform.sizeDelta = new Vector2(540f, 60f);
    }

    Row BuildRow(Transform parent, float x, float y, CashTool tool, CashGadget gadget)
    {
        var row = new Row { tool = tool, gadget = gadget };
        BlueprintBox box = BlueprintUI.Box(parent, gadget == CashGadget.None ? "Tool" + (int)tool : "Gadget" + (int)gadget, UIColors.PaperHi, UIColors.Ink, 2f, BlueprintUI.RadiusLarge);
        BlueprintUI.TopLeft(box.rectTransform, x, y, 560f, 94f);
        row.box = box;

        BlueprintBox iconBack = BlueprintUI.Box(box.transform, "IconBack", UIColors.Felt, UIColors.Ink, 2f, BlueprintUI.RadiusSmall);
        BlueprintUI.TopLeft(iconBack.rectTransform, 12f, 12f, 70f, 70f);
        RectTransform iconRect = BlueprintUI.NewRect("Icon", iconBack.transform);
        BlueprintUI.Centered(iconRect, 52f, 52f);
        row.icon = iconRect.gameObject.AddComponent<RawImage>();
        CashAtlas atlas = CashAtlas.Shared;
        row.icon.texture = atlas.Texture;
        row.icon.uvRect = atlas.Get(gadget == CashGadget.None ? CashText.ToolIcon(tool) : CashText.GadgetIcon(gadget)).uv;
        row.icon.color = UIColors.PaperHi;
        row.icon.raycastTarget = false;

        row.name = Label(box.transform, "Name", "", 22f, UIColors.Ink, TextAlignmentOptions.Left, true);
        BlueprintUI.TopLeft(row.name.rectTransform, 96f, 8f, 260f, 30f);
        BlueprintUI.AutoSize(row.name, 14f);
        row.blurb = BlueprintUI.Wrap(Label(box.transform, "Blurb", "", 15f, UIColors.InkSoft));
        BlueprintUI.TopLeft(row.blurb.rectTransform, 96f, 36f, 290f, 38f);
        BlueprintUI.AutoSize(row.blurb, 10f);
        row.stats = Label(box.transform, "Stats", "", 13f, UIColors.Pencil);
        BlueprintUI.TopLeft(row.stats.rectTransform, 96f, 72f, 290f, 18f);
        BlueprintUI.AutoSize(row.stats, 9f);

        row.pips = new Image[CashRules.MaxToolLevel];
        for (int i = 0; i < row.pips.Length; i++)
        {
            RectTransform pip = BlueprintUI.NewRect("Pip" + i, box.transform);
            BlueprintUI.TopLeft(pip, 360f + i * 16f, 14f, 12f, 12f);
            row.pips[i] = pip.gameObject.AddComponent<Image>();
        }

        BlueprintBox button = BlueprintUI.Box(box.transform, "Buy", UIColors.Highlight, UIColors.Ink, 2.5f, BlueprintUI.RadiusLarge, true);
        BlueprintUI.TopRight(button.rectTransform, 14f, 26f, 150f, 48f);
        button.SetShadow(UIColors.Ink, new Vector2(0f, -4f));
        row.buttonBox = button;
        row.button = BlueprintUI.MakeButton(button);
        row.action = Label(button.transform, "Label", "", 18f, UIColors.Ink, TextAlignmentOptions.Center, true);
        BlueprintUI.Stretch(row.action.rectTransform, 6f, 2f, 6f, 2f);
        BlueprintUI.Wrap(BlueprintUI.AutoSize(row.action, 11f));
        row.button.onClick.AddListener(() => Buy(row));
        return row;
    }

    // ------------------------------------------------------------------

    public void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    public void Open()
    {
        if (IsOpen || view == null || !view.IsOpen) return;
        IsOpen = true;
        root.gameObject.SetActive(true);
        root.SetAsLastSibling();
        if (UIManager.Instance != null) UIManager.Instance.OpenPanel(this, true, Close);
        messageTimer = 0f;
        messageText.text = "";
        WriteTexts();
        Refresh();
        CashGrabSystem.Instance?.Audio?.Play("open_floor", 0.5f, 1.3f, 0.1f, 0f);
        view.Coach?.OnShopOpened();
    }

    public void OpenAt(CashTool tool)
    {
        Open();
        Highlight(toolRows[(int)tool]);
    }

    public void OpenAt(CashGadget gadget)
    {
        Open();
        if (gadget != CashGadget.None) Highlight(gadgetRows[(int)gadget]);
    }

    void Highlight(Row row)
    {
        highlight = row;
        highlightTimer = 1.6f;
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        root.gameObject.SetActive(false);
        if (UIManager.Exists) UIManager.Instance.ClosePanel(this);
    }

    void OnDestroy()
    {
        if (UIManager.Exists) UIManager.Instance.ClosePanel(this);
    }

    void Update()
    {
        if (!IsOpen) return;
        refreshTimer -= Time.unscaledDeltaTime;
        if (refreshTimer <= 0f)
        {
            refreshTimer = 0.25f;
            Refresh();
        }
        if (messageTimer > 0f)
        {
            messageTimer -= Time.unscaledDeltaTime;
            if (messageTimer <= 0f) messageText.text = "";
        }
        if (highlightTimer > 0f)
        {
            highlightTimer -= Time.unscaledDeltaTime;
            if (highlight != null)
            {
                float pulse = Mathf.Abs(Mathf.Sin(highlightTimer * 9f));
                highlight.box.SetRings(UIColors.Highlight, highlightTimer > 0f ? 4f + pulse * 4f : 0f, Color.clear, 0f);
            }
        }
    }

    void WriteTexts()
    {
        Transform sheet = root.Find("Sheet");
        SetText(sheet, "ShopTitle", CashText.Get("cash.shop.title"));
        SetText(sheet, "ShopSub", CashText.Get("cash.shop.sub"));
        SetText(sheet, "ShopCashLabel", CashText.Get("cash.bar.your_cash"));
        SetText(sheet, "ShopToolsHead", CashText.Get("cash.shop.tools"));
        SetText(sheet, "ShopGadgetsHead", CashText.Get("cash.shop.gadgets"));
    }

    static void SetText(Transform parent, string name, string text)
    {
        if (parent == null) return;
        Transform t = parent.Find(name);
        if (t != null && t.TryGetComponent(out TextMeshProUGUI label)) label.text = text;
    }

    void Refresh()
    {
        CashGrabSystem system = CashGrabSystem.Instance;
        if (system == null) return;
        cashText.text = UIFormat.Money(system.Ledger.PlayerCash);
        foreach (Row row in toolRows) RefreshRow(system, row);
        foreach (Row row in gadgetRows) RefreshRow(system, row);
    }

    void RefreshRow(CashGrabSystem system, Row row)
    {
        bool isTool = row.gadget == CashGadget.None;
        int level = isTool ? system.ToolLevel(row.tool) : system.GadgetLevel(row.gadget);
        int max = isTool ? CashRules.MaxLevel(row.tool) : CashRules.MaxGadgetLevel;
        bool unlocked = isTool ? system.ToolUnlocked(row.tool) : system.GadgetUnlocked(row.gadget);
        double price = isTool ? CashRules.ToolPrice(row.tool, level) : CashRules.GadgetPrice(row.gadget, level);

        row.name.text = isTool ? CashText.ToolName(row.tool) : CashText.GadgetName(row.gadget);
        row.blurb.text = isTool ? CashText.ToolBlurb(row.tool) : CashText.GadgetBlurb(row.gadget);
        row.stats.text = Stats(row, Math.Max(1, level));
        for (int i = 0; i < row.pips.Length; i++)
        {
            row.pips[i].enabled = i < max && max > 1;
            row.pips[i].color = i < level ? UIColors.Money : UIColors.WithAlpha(UIColors.Ink, 0.18f);
        }
        row.box.Fill = level > 0 ? UIColors.PaperHi : unlocked ? UIColors.Paper : UIColors.PaperShade;

        bool canAct = false;
        if (level >= max)
        {
            row.action.text = CashText.Get(isTool && row.tool == CashTool.Hands ? "cash.shop.owned" : "cash.shop.max");
            row.buttonBox.Fill = UIColors.StickyGreen;
        }
        else if (level == 0 && !unlocked)
        {
            row.action.text = isTool ? CashText.Get("cash.shop.locked_seats", ("seats", CashRules.ToolSeatsNeeded(row.tool)))
                                     : CashText.Get("cash.shop.locked_gadget");
            row.buttonBox.Fill = UIColors.PaperShade;
        }
        else
        {
            bool afford = PlayerWallet.CanAfford(price) || price <= 0.0;
            row.action.text = CashText.Get(level == 0 ? "cash.shop.buy" : "cash.shop.upgrade", ("amount", UIFormat.Money(price)));
            row.buttonBox.Fill = afford ? UIColors.Highlight : UIColors.LossSoft;
            canAct = true;
        }
        BlueprintUI.SetInteractable(row.button, canAct);
    }

    string Stats(Row row, int level)
    {
        if (row.gadget != CashGadget.None)
        {
            switch (row.gadget)
            {
                case CashGadget.Stapler: return CashText.Get("cash.stats.stapler", ("stun", CashRules.StaplerStun(level).ToString("0.#")), ("cooldown", CashRules.StaplerCooldown(level).ToString("0")));
                case CashGadget.Paperweight: return CashText.Get("cash.stats.paperweight", ("seconds", CashRules.PaperweightDuration(level).ToString("0")));
                case CashGadget.Fan: return CashText.Get("cash.stats.fan", ("cooldown", CashRules.FanCooldown(level).ToString("0")));
                default: return CashText.Get("cash.stats.gloves", ("percent", Mathf.RoundToInt(CashRules.GlovesResist(level) * 100f)));
            }
        }
        switch (row.tool)
        {
            case CashTool.Scoop: return CashText.Get("cash.stats.carry", ("count", CashRules.ScoopCapacity(level)));
            case CashTool.Rake: return CashText.Get("cash.stats.rake", ("width", Mathf.RoundToInt(CashRules.RakeLength(level))));
            case CashTool.Tray: return CashText.Get("cash.stats.carry", ("count", CashRules.TrayCapacity(level)));
            case CashTool.Vacuum: return CashText.Get("cash.stats.vacuum", ("reach", Mathf.RoundToInt(CashRules.VacuumRadius(level))));
            case CashTool.Industrial: return CashText.Get("cash.stats.industrial", ("reach", Mathf.RoundToInt(CashRules.IndustrialRadius(level))));
            default: return CashText.Get("cash.stats.hands");
        }
    }

    void Buy(Row row)
    {
        CashGrabSystem system = CashGrabSystem.Instance;
        if (system == null) return;
        CashGrabSystem.BuyResult result = row.gadget == CashGadget.None ? system.BuyTool(row.tool) : system.BuyGadget(row.gadget);
        switch (result)
        {
            case CashGrabSystem.BuyResult.Bought:
                system.Audio?.Play("buy", 0.9f, 1f, 0.1f, 0f);
                Say(CashText.Get("cash.shop.bought", ("item", row.gadget == CashGadget.None ? CashText.ToolName(row.tool) : CashText.GadgetName(row.gadget))), UIColors.Money);
                break;
            case CashGrabSystem.BuyResult.NotEnoughCash:
                system.Audio?.Play("denied", 0.7f, 1f, 0.1f, 0f);
                double level = row.gadget == CashGadget.None ? system.ToolLevel(row.tool) : system.GadgetLevel(row.gadget);
                double price = row.gadget == CashGadget.None ? CashRules.ToolPrice(row.tool, (int)level) : CashRules.GadgetPrice(row.gadget, (int)level);
                Say(PlayerWallet.NotEnoughMessage(price), UIColors.Loss);
                break;
            case CashGrabSystem.BuyResult.Locked:
                system.Audio?.Play("denied", 0.7f, 1f, 0.1f, 0f);
                Say(row.gadget == CashGadget.None ? CashText.Get("cash.shop.locked_seats_long", ("seats", CashRules.ToolSeatsNeeded(row.tool)), ("now", CashGrabSystem.OfficeSeats()))
                                                  : CashText.Get("cash.shop.locked_gadget"), UIColors.Loss);
                break;
        }
        Refresh();
    }

    void Say(string message, Color color)
    {
        messageText.text = message;
        messageText.color = color;
        messageTimer = 3.5f;
    }
}
