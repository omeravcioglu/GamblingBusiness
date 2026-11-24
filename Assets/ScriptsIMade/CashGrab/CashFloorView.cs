using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The cash floor screen (Tab from the desk): a full-screen 2D view in the "Stationery vs. Felt" look, built at runtime.
/// - Top bar: your cash (the gambling money), company value, the day, today's haul, and doors to the shop (B), the casino,
///   build mode (Q), stocks and back to the desk (Tab / Esc).
/// - Left: the three departments (budget, level, next upgrade, their company perk).
/// - Middle: the felt floor, drawn by one CashBatchGraphic: delivery tubes, the safe, the crates, every bill, the workers,
///   the CEO's tool and the effects.
/// - Bottom: the tool row (1-6, mouse wheel), the gadgets (7-9, right mouse button) and the deposit streak.
/// Not a UIManager panel: the office clock keeps running here (this IS the game). It hides itself while any panel is open
/// (crises, risk cards, the casino... draw over the office) and comes back when they close. Sorting order 75: above the HUD
/// and its chips, below the stock market, the casino and every pop-up. Texts: the "CashGrab" String Table.
/// </summary>
public partial class CashFloorView : MonoBehaviour
{
    public const int SortingOrder = 75;

    // Layout (1920 x 1080 frame)
    const float TopBarHeight = 96f;
    const float LeftPanelWidth = 330f;
    const float Margin = 24f;
    const float ToolbarHeight = 108f;

    CashGrabSystem system;
    Canvas canvas;
    RectTransform root;
    TMP_FontAsset font;

    // Felt
    RectTransform feltArea;      // where the felt may go (anchored)
    RectTransform felt;          // the aspect-locked floor
    RawImage feltImage;
    CashBatchGraphic batch;
    RectTransform textLayer;
    RectTransform shakeRoot;

    // Top bar
    TextMeshProUGUI cashText, cashLabel, valueText, dayText, todayText, taskText, countInText;
    RectTransform dayFill;
    GameObject taskChip;

    // Left panel
    readonly DeptCard[] deptCards = new DeptCard[CashLedger.DeptCount];
    TextMeshProUGUI floorInfoText;

    // Toolbar
    readonly ToolSlot[] toolSlots = new ToolSlot[CashRules.ToolCount];
    readonly ToolSlot[] gadgetSlots = new ToolSlot[3];
    GameObject glovesBadge;
    TextMeshProUGUI glovesText;
    TextMeshProUGUI streakText;
    RectTransform streakFill;

    CashShopView shop;
    CashCoach coach;

    public bool IsOpen { get; private set; }
    public CashShopView Shop => shop;
    public CashCoach Coach => coach;

    bool pressOnFloor;
    bool cursorHidden;
    bool shownLastFrame;
    double shownCash = -1.0;
    double cashAnim;
    string lastValue, lastDay, lastToday;

    class DeptCard
    {
        public GameObject root;
        public TextMeshProUGUI name, level, budget, next, perk, status;
        public RectTransform fill;
        public Image[] pips;
        public BlueprintBox box;
    }

    class ToolSlot
    {
        public Button button;
        public BlueprintBox box;
        public RawImage icon;
        public TextMeshProUGUI key, label, price;
        public GameObject lockIcon;
        public Image[] pips;
        public Image cooldown;
    }

    // ------------------------------------------------------------------
    // Creation
    // ------------------------------------------------------------------

    public static CashFloorView Create(CashGrabSystem owner)
    {
        Canvas c = BlueprintUI.CreateOverlayCanvas("CashFloorCanvas (Runtime)", SortingOrder);
        if (owner != null && owner.gameObject.scene.IsValid() && c.gameObject.scene != owner.gameObject.scene)
        {
            try { UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(c.gameObject, owner.gameObject.scene); }
            catch (Exception) { }
        }
        var view = c.gameObject.AddComponent<CashFloorView>();
        view.system = owner;
        view.canvas = c;
        view.Build();
        c.enabled = false;
        return view;
    }

    TMP_FontAsset ResolveFont()
    {
        if (TowerView.Instance != null)
        {
            TMP_FontAsset f = TowerView.Instance.ResolveFont();
            if (f != null) return f;
        }
        foreach (TMP_FontAsset candidate in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            if (candidate != null && candidate.name == L10n.DefaultFontName) return candidate;
        return L10n.DefaultFont;
    }

    void Build()
    {
        font = ResolveFont();
        root = BlueprintUI.Stretch(BlueprintUI.NewRect("CashFloor", canvas.transform));

        // The room around the felt: dark wood floor, takes every click (nothing reaches the desk below)
        BlueprintBox backdrop = BlueprintUI.AddBox(BlueprintUI.Stretch(BlueprintUI.NewRect("Backdrop", root)), UIColors.Hex(0x2A1F17), Color.clear, 0f, Vector4.zero, true);
        backdrop.SetStripes(UIColors.WithAlpha(UIColors.Hex(0x3A2C21), 0.55f), 64f, 30f);

        BuildFelt();
        BuildTopBar();
        BuildLeftPanel();
        BuildToolbar();
        BuildExtras();

        shop = CashShopView.Create(this, root, font);
        coach = CashCoach.Create(this, root, font);

        L10nFont.AttachAll(root);
        L10n.LanguageChanged += OnLanguageChanged;
    }

    void OnDestroy()
    {
        L10n.LanguageChanged -= OnLanguageChanged;
        RestoreCursor();
        LiftToasts(false);
        if (UIManager.Exists) UIManager.Instance.RemoveEscapeHandler(this);
    }

    // The HUD's toasts sort with the HUD (under this screen): while the floor is open, they get a canvas of their own above it
    Canvas liftedToasts;
    bool liftedOverride, liftedAdded;
    int liftedOrder;

    void LiftToasts(bool lift)
    {
        if (!lift)
        {
            if (liftedToasts == null) return;
            if (liftedAdded) Destroy(liftedToasts);
            else
            {
                liftedToasts.overrideSorting = liftedOverride;
                liftedToasts.sortingOrder = liftedOrder;
            }
            liftedToasts = null;
            return;
        }
        if (liftedToasts != null) return;
        HudToasts toasts = FindAnyObjectByType<HudToasts>();
        if (toasts == null) return;
        Transform holder = toasts.template != null && toasts.template.parent != null ? toasts.template.parent : toasts.transform;
        Canvas nested = holder.GetComponentInParent<Canvas>(true);
        liftedAdded = false;
        if (nested == null || nested.isRootCanvas)
        {
            // No canvas of their own: give the toast column one (never lift the whole HUD)
            if (holder.GetComponent<Canvas>() != null) return;
            nested = holder.gameObject.AddComponent<Canvas>();
            liftedAdded = true;
        }
        liftedToasts = nested;
        liftedOverride = nested.overrideSorting;
        liftedOrder = nested.sortingOrder;
        nested.overrideSorting = true;
        nested.sortingOrder = 900;
    }

    void OnLanguageChanged()
    {
        lastValue = lastDay = lastToday = null;
        shownCash = -1.0;
        WriteStaticTexts();
        RefreshPanels(true);
    }

    TextMeshProUGUI Label(Transform parent, string name, string text, float size, Color color, TextAlignmentOptions align = TextAlignmentOptions.Left, bool bold = false)
    {
        return BlueprintUI.Text(parent, name, text, size, color, font, align, bold);
    }

    void BuildFelt()
    {
        feltArea = BlueprintUI.NewRect("FeltArea", root);
        feltArea.anchorMin = Vector2.zero;
        feltArea.anchorMax = Vector2.one;
        feltArea.offsetMin = new Vector2(Margin + LeftPanelWidth + 18f, Margin + ToolbarHeight + 10f);
        feltArea.offsetMax = new Vector2(-Margin, -(TopBarHeight + 16f));

        shakeRoot = BlueprintUI.NewRect("Shake", feltArea);
        shakeRoot.anchorMin = shakeRoot.anchorMax = new Vector2(0.5f, 0.5f);
        shakeRoot.sizeDelta = Vector2.zero;

        felt = BlueprintUI.NewRect("Felt", shakeRoot);
        felt.anchorMin = felt.anchorMax = new Vector2(0.5f, 0.5f);
        felt.pivot = new Vector2(0.5f, 0.5f);
        felt.sizeDelta = CashRules.BaseFloorSize;

        // Wooden rim + shadow
        BlueprintBox rim = BlueprintUI.AddBox(BlueprintUI.Stretch(BlueprintUI.NewRect("Rim", felt), -16f, -16f, -16f, -16f), UIColors.Clipboard, UIColors.Ink, 3f, BlueprintUI.RadiusCard);
        rim.SetShadow(UIColors.Dark(0.55f), new Vector2(6f, -8f));

        var feltGo = BlueprintUI.Stretch(BlueprintUI.NewRect("FeltCloth", felt));
        feltImage = feltGo.gameObject.AddComponent<RawImage>();
        CashAtlas atlas = CashAtlas.Shared;
        feltImage.texture = atlas.Felt;
        feltImage.color = atlas.Felt != null ? UIColors.Felt : UIColors.Felt;
        feltImage.raycastTarget = true;
        feltGo.gameObject.AddComponent<RectMask2D>();

        // A soft light in the middle of the felt
        BlueprintBox glow = BlueprintUI.AddBox(BlueprintUI.Stretch(BlueprintUI.NewRect("Glow", feltGo)), Color.clear, Color.clear, 0f, Vector4.zero);
        glow.SetGradient(UIColors.WithAlpha(UIColors.FeltHi, 0.55f), new Vector2(0.5f, 0.55f));

        BlueprintBox stitch = BlueprintUI.AddBox(BlueprintUI.Stretch(BlueprintUI.NewRect("Stitch", feltGo), 10f, 10f, 10f, 10f), Color.clear, UIColors.WithAlpha(UIColors.Gold, 0.45f), 2f, BlueprintUI.Round(14f));
        stitch.SetDash(true, 10f, 8f);

        batch = BlueprintUI.Stretch(BlueprintUI.NewRect("Batch", feltGo)).gameObject.AddComponent<CashBatchGraphic>();
        batch.SetAtlas(atlas);

        textLayer = BlueprintUI.Stretch(BlueprintUI.NewRect("Texts", feltGo));
        BuildTextPool();
        BuildCrateLabels();
    }

    void BuildTopBar()
    {
        RectTransform bar = BlueprintUI.NewRect("TopBar", root);
        bar.anchorMin = new Vector2(0f, 1f);
        bar.anchorMax = new Vector2(1f, 1f);
        bar.pivot = new Vector2(0.5f, 1f);
        bar.offsetMin = new Vector2(Margin, -(TopBarHeight + 8f));
        bar.offsetMax = new Vector2(-Margin, -8f);
        BlueprintBox paper = BlueprintUI.AddBox(bar, UIColors.Paper, UIColors.Ink, 3f, BlueprintUI.RadiusLarge, true);
        paper.SetShadow(UIColors.Dark(0.5f), new Vector2(4f, -5f));

        TextMeshProUGUI title = BlueprintUI.Caps(Label(bar, "Title", "", 18f, UIColors.Pencil));
        BlueprintUI.TopLeft(title.rectTransform, 26f, 14f, 260f, 26f);
        title.name = "Title";
        TextMeshProUGUI hint = Label(bar, "Hint", "", 15f, UIColors.InkSoft);
        BlueprintUI.TopLeft(hint.rectTransform, 26f, 44f, 260f, 44f);
        BlueprintUI.AutoSize(BlueprintUI.Wrap(hint), 11f);
        hint.name = "TitleHint";

        cashLabel = BlueprintUI.Caps(Label(bar, "CashLabel", "", 16f, UIColors.Pencil));
        BlueprintUI.TopLeft(cashLabel.rectTransform, 304f, 12f, 280f, 22f);
        cashText = Label(bar, "Cash", "$0", 46f, UIColors.Money, TextAlignmentOptions.Left, true);
        BlueprintUI.TopLeft(cashText.rectTransform, 302f, 30f, 330f, 58f);
        BlueprintUI.AutoSize(cashText, 24f);

        TextMeshProUGUI valueLabel = BlueprintUI.Caps(Label(bar, "ValueLabel", "", 16f, UIColors.Pencil));
        BlueprintUI.TopLeft(valueLabel.rectTransform, 648f, 12f, 230f, 22f);
        valueLabel.name = "ValueLabel";
        valueText = Label(bar, "Value", "$0", 28f, UIColors.Ink, TextAlignmentOptions.Left, true);
        BlueprintUI.TopLeft(valueText.rectTransform, 648f, 36f, 220f, 40f);
        BlueprintUI.AutoSize(valueText, 16f);

        dayText = Label(bar, "Day", "", 20f, UIColors.Ink);
        BlueprintUI.TopLeft(dayText.rectTransform, 886f, 16f, 180f, 28f);
        countInText = Label(bar, "CountIn", "", 14f, UIColors.Pencil);
        BlueprintUI.TopLeft(countInText.rectTransform, 886f, 68f, 180f, 22f);
        BlueprintUI.AutoSize(countInText, 10f);
        BlueprintBox track = BlueprintUI.Box(bar, "DayTrack", UIColors.PaperShade, UIColors.Ink, 2f, BlueprintUI.Round(6f));
        BlueprintUI.TopLeft(track.rectTransform, 886f, 52f, 170f, 14f);
        dayFill = BlueprintUI.NewRect("Fill", track.transform);
        dayFill.anchorMin = Vector2.zero;
        dayFill.anchorMax = new Vector2(0f, 1f);
        dayFill.offsetMin = new Vector2(2f, 2f);
        dayFill.offsetMax = new Vector2(-2f, -2f);
        dayFill.gameObject.AddComponent<Image>().color = UIColors.Highlight;

        // Doors (right side)
        RectTransform doors = BlueprintUI.NewRect("Doors", bar);
        doors.anchorMin = doors.anchorMax = new Vector2(1f, 0.5f);
        doors.pivot = new Vector2(1f, 0.5f);
        doors.anchoredPosition = new Vector2(-16f, 0f);
        doors.sizeDelta = new Vector2(10f, 64f);
        HorizontalLayoutGroup row = BlueprintUI.HRow(doors, 8f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleRight);
        row.childForceExpandHeight = false;
        BlueprintUI.Fit(doors, true, false);

        Door(doors, "Shop", "cash.door.shop", BlueprintIconKind.Coin, UIColors.Highlight, "B", () => shop.Toggle());
        Door(doors, "Casino", "cash.door.casino", BlueprintIconKind.Cards, UIColors.PaperHi, null, () => OpenHud(h => h.OpenCasino()));
        Door(doors, "Build", "cash.door.build", BlueprintIconKind.Building, UIColors.StickyBlue, "Q", () => OpenHud(h => h.ToggleBuildMode()));
        Door(doors, "Stocks", "cash.door.stocks", BlueprintIconKind.TrendUp, UIColors.PaperHi, null, () => OpenHud(h => h.OpenStocks()));
        Door(doors, "Desk", "cash.door.desk", BlueprintIconKind.Clipboard, UIColors.StickyGreen, "Tab", Close);
    }

    void Door(Transform parent, string name, string key, BlueprintIconKind icon, Color fill, string keycap, Action action)
    {
        Button button = BlueprintUI.PaperButton(parent, name, CashText.Get(key), icon, font, 52f, 18f, fill, UIColors.Ink, true,
                                                BlueprintUI.RadiusLarge, 2.5f, UIColors.Ink, new Vector2(0f, -4f), 11f, out TextMeshProUGUI text, 22f);
        text.name = "Label:" + key;
        if (!string.IsNullOrEmpty(keycap))
        {
            RectTransform cap = BlueprintUI.Keycap(button.transform, keycap, font, 13f);
            BlueprintUI.Layout(cap, -1f, 30f, -1f, -1f, 30f, 30f);
        }
        button.onClick.AddListener(() => action());
    }

    void OpenHud(Action<OfficeHud> open)
    {
        OfficeHud hud = FindAnyObjectByType<OfficeHud>();
        if (hud != null) open(hud);
    }

    void BuildLeftPanel()
    {
        RectTransform panel = BlueprintUI.NewRect("Departments", root);
        panel.anchorMin = new Vector2(0f, 0f);
        panel.anchorMax = new Vector2(0f, 1f);
        panel.pivot = new Vector2(0f, 1f);
        panel.offsetMin = new Vector2(Margin, Margin);
        panel.offsetMax = new Vector2(Margin + LeftPanelWidth, -(TopBarHeight + 16f));
        BlueprintBox paper = BlueprintUI.AddBox(panel, UIColors.Ledger, UIColors.Ink, 3f, BlueprintUI.RadiusCard, true);
        paper.SetShadow(UIColors.Dark(0.5f), new Vector2(4f, -5f));

        TextMeshProUGUI head = BlueprintUI.Caps(Label(panel, "Head", "", 18f, UIColors.Pencil));
        BlueprintUI.TopLeft(head.rectTransform, 22f, 14f, 290f, 26f);
        head.name = "DeptHead";
        TextMeshProUGUI sub = BlueprintUI.Wrap(Label(panel, "Sub", "", 15f, UIColors.InkSoft));
        BlueprintUI.TopLeft(sub.rectTransform, 22f, 40f, 292f, 44f);
        sub.name = "DeptSub";

        for (int d = 0; d < CashLedger.DeptCount; d++)
            deptCards[d] = BuildDeptCard(panel, d, 92f + d * 192f);

        // Today's haul and what's on the floor, at the bottom
        todayText = Label(panel, "Today", "", 18f, UIColors.Money, TextAlignmentOptions.Left, true);
        todayText.rectTransform.anchorMin = new Vector2(0f, 0f);
        todayText.rectTransform.anchorMax = new Vector2(1f, 0f);
        todayText.rectTransform.pivot = new Vector2(0.5f, 0f);
        todayText.rectTransform.offsetMin = new Vector2(22f, 70f);
        todayText.rectTransform.offsetMax = new Vector2(-18f, 96f);
        BlueprintUI.AutoSize(todayText, 12f);

        floorInfoText = BlueprintUI.Wrap(Label(panel, "FloorInfo", "", 15f, UIColors.InkSoft));
        floorInfoText.rectTransform.anchorMin = new Vector2(0f, 0f);
        floorInfoText.rectTransform.anchorMax = new Vector2(1f, 0f);
        floorInfoText.rectTransform.pivot = new Vector2(0.5f, 0f);
        floorInfoText.rectTransform.offsetMin = new Vector2(22f, 14f);
        floorInfoText.rectTransform.offsetMax = new Vector2(-18f, 66f);
        BlueprintUI.AutoSize(floorInfoText, 11f);

        // Hiring task chip (the clipboard is on the desk: say when a task is running)
        BlueprintBox chip = BlueprintUI.Box(panel, "TaskChip", UIColors.StickyYellow, UIColors.Ink, 2f, BlueprintUI.RadiusSmall);
        chip.rectTransform.anchorMin = new Vector2(0f, 0f);
        chip.rectTransform.anchorMax = new Vector2(1f, 0f);
        chip.rectTransform.pivot = new Vector2(0.5f, 0f);
        chip.rectTransform.offsetMin = new Vector2(16f, 104f);
        chip.rectTransform.offsetMax = new Vector2(-16f, 142f);
        chip.SetShadow(UIColors.Ink, new Vector2(2f, -3f));
        taskChip = chip.gameObject;
        taskText = Label(chip.transform, "Text", "", 16f, UIColors.Ink, TextAlignmentOptions.Center, true);
        BlueprintUI.Stretch(taskText.rectTransform, 8f, 2f, 8f, 2f);
        BlueprintUI.AutoSize(taskText, 11f);
        taskChip.SetActive(false);
    }

    DeptCard BuildDeptCard(RectTransform panel, int dept, float top)
    {
        var card = new DeptCard();
        BlueprintBox box = BlueprintUI.Box(panel, "Dept" + dept, UIColors.PaperHi, UIColors.Ink, 2f, BlueprintUI.RadiusLarge);
        BlueprintUI.TopLeft(box.rectTransform, 16f, top, LeftPanelWidth - 32f, 182f);
        card.box = box;
        card.root = box.gameObject;

        BlueprintBox band = BlueprintUI.Box(box.transform, "Band", CashRules.DeptColors[dept], Color.clear, 0f, new Vector4(12f, 18f, 0f, 0f));
        band.rectTransform.anchorMin = new Vector2(0f, 1f);
        band.rectTransform.anchorMax = new Vector2(1f, 1f);
        band.rectTransform.pivot = new Vector2(0.5f, 1f);
        band.rectTransform.offsetMin = new Vector2(2f, -10f);
        band.rectTransform.offsetMax = new Vector2(-2f, -2f);

        card.name = Label(box.transform, "Name", "", 22f, UIColors.Ink, TextAlignmentOptions.Left, true);
        BlueprintUI.TopLeft(card.name.rectTransform, 14f, 14f, 180f, 30f);
        BlueprintUI.AutoSize(card.name, 14f);
        card.level = Label(box.transform, "Level", "", 16f, UIColors.InkSoft, TextAlignmentOptions.Right);
        BlueprintUI.TopRight(card.level.rectTransform, 14f, 18f, 150f, 22f);
        BlueprintUI.AutoSize(card.level, 11f);

        card.pips = new Image[CashRules.MaxDeptLevel];
        for (int i = 0; i < card.pips.Length; i++)
        {
            RectTransform pip = BlueprintUI.NewRect("Pip" + i, box.transform);
            BlueprintUI.TopLeft(pip, 14f + i * 20f, 48f, 15f, 15f);
            card.pips[i] = pip.gameObject.AddComponent<Image>();
        }

        card.budget = Label(box.transform, "Budget", "", 19f, UIColors.Ink, TextAlignmentOptions.Right, true);
        BlueprintUI.TopRight(card.budget.rectTransform, 14f, 44f, 160f, 26f);
        BlueprintUI.AutoSize(card.budget, 13f);

        BlueprintBox track = BlueprintUI.Box(box.transform, "Track", UIColors.PaperShade, UIColors.Ink, 1.5f, BlueprintUI.Round(5f));
        BlueprintUI.TopLeft(track.rectTransform, 14f, 76f, LeftPanelWidth - 60f, 12f);
        card.fill = BlueprintUI.NewRect("Fill", track.transform);
        card.fill.anchorMin = Vector2.zero;
        card.fill.anchorMax = new Vector2(0f, 1f);
        card.fill.offsetMin = new Vector2(1.5f, 1.5f);
        card.fill.offsetMax = new Vector2(-1.5f, -1.5f);
        card.fill.gameObject.AddComponent<Image>().color = CashRules.DeptColors[dept];

        card.next = Label(box.transform, "Next", "", 15f, UIColors.InkSoft);
        BlueprintUI.TopLeft(card.next.rectTransform, 14f, 92f, LeftPanelWidth - 60f, 22f);
        BlueprintUI.AutoSize(card.next, 11f);
        card.perk = BlueprintUI.Wrap(Label(box.transform, "Perk", "", 15f, UIColors.Money));
        BlueprintUI.TopLeft(card.perk.rectTransform, 14f, 114f, LeftPanelWidth - 60f, 22f);
        BlueprintUI.AutoSize(card.perk, 11f);
        card.status = BlueprintUI.Wrap(Label(box.transform, "Status", "", 14f, UIColors.Pencil));
        BlueprintUI.TopLeft(card.status.rectTransform, 14f, 136f, LeftPanelWidth - 56f, 42f);
        BlueprintUI.AutoSize(card.status, 11f);
        return card;
    }

    void BuildToolbar()
    {
        RectTransform bar = BlueprintUI.NewRect("Toolbar", root);
        bar.anchorMin = new Vector2(0f, 0f);
        bar.anchorMax = new Vector2(1f, 0f);
        bar.pivot = new Vector2(0.5f, 0f);
        bar.offsetMin = new Vector2(Margin + LeftPanelWidth + 18f, Margin);
        bar.offsetMax = new Vector2(-Margin, Margin + ToolbarHeight);

        RectTransform strip = BlueprintUI.NewRect("Strip", bar);
        strip.anchorMin = strip.anchorMax = new Vector2(0.5f, 0.5f);
        strip.pivot = new Vector2(0.5f, 0.5f);
        HorizontalLayoutGroup row = BlueprintUI.HRow(strip, 10f, new RectOffset(16, 16, 8, 8), TextAnchor.MiddleCenter);
        row.childForceExpandHeight = false;
        BlueprintUI.Fit(strip, true, true);
        BlueprintBox paper = BlueprintUI.AddBox(strip, UIColors.Paper, UIColors.Ink, 3f, BlueprintUI.RadiusLarge, true);
        paper.SetShadow(UIColors.Dark(0.5f), new Vector2(4f, -5f));

        for (int t = 0; t < CashRules.ToolCount; t++)
        {
            CashTool tool = (CashTool)t;
            toolSlots[t] = Slot(strip, "Tool" + t, CashText.ToolIcon(tool), (t + 1).ToString(), () => OnToolSlot(tool));
        }

        RectTransform gap = BlueprintUI.NewRect("Gap", strip);
        BlueprintUI.Layout(gap, 3f, 70f);
        gap.gameObject.AddComponent<Image>().color = UIColors.WithAlpha(UIColors.Ink, 0.35f);

        CashGadget[] active = { CashGadget.Stapler, CashGadget.Paperweight, CashGadget.Fan };
        for (int g = 0; g < active.Length; g++)
        {
            CashGadget gadget = active[g];
            gadgetSlots[g] = Slot(strip, "Gadget" + g, CashText.GadgetIcon(gadget), (g + 7).ToString(), () => OnGadgetSlot(gadget));
            // The cooldown: a dark shade over the slot that shrinks from the bottom up (anchors, not a sprite fill)
            RectTransform cdRect = BlueprintUI.NewRect("Cooldown", gadgetSlots[g].box.transform);
            cdRect.anchorMin = new Vector2(0f, 1f);
            cdRect.anchorMax = new Vector2(1f, 1f);
            cdRect.offsetMin = new Vector2(3f, 0f);
            cdRect.offsetMax = new Vector2(-3f, -3f);
            Image cd = cdRect.gameObject.AddComponent<Image>();
            cd.color = UIColors.Dark(0.55f);
            cd.raycastTarget = false;
            gadgetSlots[g].cooldown = cd;
        }

        // Gloves: a passive badge
        BlueprintBox gloves = BlueprintUI.Box(strip, "Gloves", UIColors.PaperHi, UIColors.Ink, 2f, BlueprintUI.RadiusSmall);
        BlueprintUI.Layout(gloves, 74f, 84f);
        RawImage gIcon = Icon(gloves.transform, "icon_gloves", 44f, UIColors.Ink);
        gIcon.rectTransform.anchoredPosition = new Vector2(0f, 10f);
        glovesText = Label(gloves.transform, "Level", "", 13f, UIColors.InkSoft, TextAlignmentOptions.Center);
        glovesText.rectTransform.anchorMin = new Vector2(0f, 0f);
        glovesText.rectTransform.anchorMax = new Vector2(1f, 0f);
        glovesText.rectTransform.pivot = new Vector2(0.5f, 0f);
        glovesText.rectTransform.offsetMin = new Vector2(2f, 4f);
        glovesText.rectTransform.offsetMax = new Vector2(-2f, 24f);
        glovesBadge = gloves.gameObject;

        RectTransform gap2 = BlueprintUI.NewRect("Gap", strip);
        BlueprintUI.Layout(gap2, 3f, 70f);
        gap2.gameObject.AddComponent<Image>().color = UIColors.WithAlpha(UIColors.Ink, 0.35f);

        // Streak meter
        RectTransform streak = BlueprintUI.NewRect("Streak", strip);
        BlueprintUI.Layout(streak, 170f, 84f);
        streakText = Label(streak, "Text", "", 22f, UIColors.Ink, TextAlignmentOptions.Center, true);
        BlueprintUI.TopLeft(streakText.rectTransform, 0f, 8f, 170f, 36f);
        BlueprintUI.AutoSize(streakText, 13f);
        BlueprintBox track = BlueprintUI.Box(streak, "Track", UIColors.PaperShade, UIColors.Ink, 2f, BlueprintUI.Round(6f));
        BlueprintUI.TopLeft(track.rectTransform, 10f, 52f, 150f, 16f);
        streakFill = BlueprintUI.NewRect("Fill", track.transform);
        streakFill.anchorMin = Vector2.zero;
        streakFill.anchorMax = new Vector2(0f, 1f);
        streakFill.offsetMin = new Vector2(2f, 2f);
        streakFill.offsetMax = new Vector2(-2f, -2f);
        streakFill.gameObject.AddComponent<Image>().color = UIColors.Loss;
    }

    ToolSlot Slot(Transform parent, string name, string icon, string key, Action onClick)
    {
        var slot = new ToolSlot();
        BlueprintBox box = BlueprintUI.Box(parent, name, UIColors.PaperHi, UIColors.Ink, 2.5f, BlueprintUI.RadiusSmall, true);
        BlueprintUI.Layout(box, 88f, 88f);
        slot.box = box;
        slot.button = BlueprintUI.MakeButton(box);
        slot.button.onClick.AddListener(() => onClick());
        slot.icon = Icon(box.transform, icon, 46f, UIColors.Ink);
        slot.icon.rectTransform.anchoredPosition = new Vector2(0f, 8f);
        slot.key = Label(box.transform, "Key", key, 14f, UIColors.Pencil, TextAlignmentOptions.Left, true);
        BlueprintUI.TopLeft(slot.key.rectTransform, 7f, 3f, 30f, 20f);
        slot.price = Label(box.transform, "Price", "", 13f, UIColors.Money, TextAlignmentOptions.Center, true);
        slot.price.rectTransform.anchorMin = new Vector2(0f, 0f);
        slot.price.rectTransform.anchorMax = new Vector2(1f, 0f);
        slot.price.rectTransform.pivot = new Vector2(0.5f, 0f);
        slot.price.rectTransform.offsetMin = new Vector2(2f, 3f);
        slot.price.rectTransform.offsetMax = new Vector2(-2f, 21f);
        BlueprintUI.AutoSize(slot.price, 9f);
        slot.pips = new Image[CashRules.MaxToolLevel];
        for (int i = 0; i < slot.pips.Length; i++)
        {
            RectTransform pip = BlueprintUI.NewRect("Pip" + i, box.transform);
            pip.anchorMin = pip.anchorMax = new Vector2(0.5f, 0f);
            pip.pivot = new Vector2(0.5f, 0f);
            pip.anchoredPosition = new Vector2((i - 1) * 14f, 8f);
            pip.sizeDelta = new Vector2(10f, 10f);
            slot.pips[i] = pip.gameObject.AddComponent<Image>();
            slot.pips[i].raycastTarget = false;
        }
        RectTransform lockRect = BlueprintUI.NewRect("Lock", box.transform);
        lockRect.anchorMin = lockRect.anchorMax = new Vector2(1f, 1f);
        lockRect.pivot = new Vector2(1f, 1f);
        lockRect.anchoredPosition = new Vector2(-4f, -4f);
        lockRect.sizeDelta = new Vector2(24f, 24f);
        RawImage lockIcon = Icon(lockRect, "icon_lock", 24f, UIColors.Pencil);
        lockIcon.rectTransform.anchoredPosition = Vector2.zero;
        slot.lockIcon = lockRect.gameObject;
        return slot;
    }

    RawImage Icon(Transform parent, string sprite, float size, Color color)
    {
        RectTransform rect = BlueprintUI.NewRect("Icon", parent);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(size, size);
        RawImage image = rect.gameObject.AddComponent<RawImage>();
        CashAtlas atlas = CashAtlas.Shared;
        image.texture = atlas.Texture;
        image.uvRect = atlas.Get(sprite).uv;
        image.color = color;
        image.raycastTarget = false;
        if (!atlas.HasArt) image.color = UIColors.WithAlpha(color, 0.25f);
        return image;
    }

    void BuildCrateLabels()
    {
        for (int d = 0; d < CashLedger.DeptCount; d++)
        {
            TextMeshProUGUI t = Label(textLayer, "Crate" + d, "", 15f, UIColors.Ink, TextAlignmentOptions.Center, true);
            t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0f, 1f);
            t.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            t.rectTransform.sizeDelta = new Vector2(150f, 24f);
            BlueprintUI.AutoSize(t, 9f);
            crateLabels[d] = t;
        }
        safeLabel = Label(textLayer, "SafeHint", "", 16f, UIColors.Highlight, TextAlignmentOptions.Center, true);
        safeLabel.rectTransform.anchorMin = safeLabel.rectTransform.anchorMax = new Vector2(0f, 1f);
        safeLabel.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        safeLabel.rectTransform.sizeDelta = new Vector2(240f, 24f);
        safeLabel.gameObject.SetActive(false);
    }

    readonly TextMeshProUGUI[] crateLabels = new TextMeshProUGUI[CashLedger.DeptCount];
    TextMeshProUGUI safeLabel;

    void WriteStaticTexts()
    {
        SetText(root, "TopBar/Title", CashText.Get("cash.title"));
        SetText(root, "TopBar/TitleHint", CashText.Get("cash.title.hint"));
        if (cashLabel != null) cashLabel.text = CashText.Get("cash.bar.your_cash");
        SetText(root, "TopBar/ValueLabel", CashText.Get("cash.bar.company_value"));
        SetText(root, "Departments/DeptHead", CashText.Get("cash.dept.head"));
        SetText(root, "Departments/DeptSub", CashText.Get("cash.dept.sub"));
        for (int d = 0; d < CashLedger.DeptCount; d++)
        {
            if (deptCards[d] != null) deptCards[d].name.text = CashText.DeptName(d);
            if (crateLabels[d] != null) crateLabels[d].text = L10n.ToUpper(CashText.DeptName(d));
        }
        if (safeLabel != null) safeLabel.text = CashText.Get("cash.safe.drop_here");
        foreach (TextMeshProUGUI t in root.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (!t.name.StartsWith("Label:")) continue;
            t.text = CashText.Get(t.name.Substring(6));
        }
    }

    static void SetText(Transform from, string path, string text)
    {
        Transform t = from.Find(path);
        if (t != null && t.TryGetComponent(out TextMeshProUGUI label)) label.text = text;
    }

    // ------------------------------------------------------------------
    // Opening and closing
    // ------------------------------------------------------------------

    public void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    /// <summary>Open the floor (from the desk: Tab, the desk chip). Refused while a panel, crisis or risk card is up.</summary>
    public void Open()
    {
        if (IsOpen) return;
        if (system == null || !system.Ready) return;
        if (!UIManager.GameplayInputAllowed()) return;
        IsOpen = true;
        if (UIManager.Instance != null) UIManager.Instance.PushEscapeHandler(this, Close);
        WriteStaticTexts();
        shownCash = -1.0;
        RefreshPanels(true);
        canvas.enabled = true;
        shownLastFrame = true;
        LiftToasts(true);
        if (system.Audio != null) system.Audio.Play("open_floor", 0.7f, 1f, 0.2f, 0f);
        coach?.OnFloorOpened();
        CashGrabSystem.NotifyFloorToggled(true);
    }

    public void Close()
    {
        if (!IsOpen) return;
        if (shop != null && shop.IsOpen) shop.Close();
        IsOpen = false;
        system?.Sim?.DropHeld();
        if (UIManager.Exists) UIManager.Instance.RemoveEscapeHandler(this);
        canvas.enabled = false;
        RestoreCursor();
        LiftToasts(false);
        if (system != null && system.Audio != null)
        {
            system.Audio.StopAll();
            system.Audio.Play("close_floor", 0.6f, 1f, 0.2f, 0f);
        }
        CashGrabSystem.NotifyFloorToggled(false);
    }

    /// <summary>Another panel is up (a crisis, a risk card, the casino...): the floor hides under it (but stays open).</summary>
    bool OtherPanelOpen()
    {
        if (!UIManager.Exists) return false;
        return shop != null ? UIManager.Instance.IsAnyPanelOpen(shop) : UIManager.Instance.IsAnyPanelOpen();
    }

    // ------------------------------------------------------------------
    // Update
    // ------------------------------------------------------------------

    void Update()
    {
        if (system == null) return;
        bool typing = UIManager.IsTypingInInputField();

        if (!typing && Input.GetKeyDown(KeyCode.Tab))
        {
            if (IsOpen)
            {
                if (shop != null && shop.IsOpen) shop.Close();
                else if (!OtherPanelOpen()) Close();
            }
            else
            {
                Open();
            }
        }

        bool show = IsOpen && !OtherPanelOpen();
        if (canvas.enabled != show) canvas.enabled = show;
        if (!show)
        {
            if (shownLastFrame) RestoreCursor();
            shownLastFrame = false;
            return;
        }
        shownLastFrame = true;

        bool shopOpen = shop != null && shop.IsOpen;
        if (!typing && !shopOpen)
        {
            for (int t = 0; t < CashRules.ToolCount; t++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + t) || Input.GetKeyDown(KeyCode.Keypad1 + t)) OnToolSlot((CashTool)t);
            if (Input.GetKeyDown(KeyCode.Alpha7) || Input.GetKeyDown(KeyCode.Keypad7)) OnGadgetSlot(CashGadget.Stapler);
            if (Input.GetKeyDown(KeyCode.Alpha8) || Input.GetKeyDown(KeyCode.Keypad8)) OnGadgetSlot(CashGadget.Paperweight);
            if (Input.GetKeyDown(KeyCode.Alpha9) || Input.GetKeyDown(KeyCode.Keypad9)) OnGadgetSlot(CashGadget.Fan);
            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > 0.01f && PointerOnFelt(out _)) system.CycleTool(wheel > 0f ? -1 : 1);
        }
        if (!typing && Input.GetKeyDown(KeyCode.B)) shop.Toggle();
    }

    void LateUpdate()
    {
        if (system == null || !IsOpen || !canvas.enabled) return;
        LayoutFelt();
        RefreshPanels(false);
        RefreshExtras(Time.unscaledDeltaTime);
        Render(Time.unscaledDeltaTime);
    }

    void LayoutFelt()
    {
        Rect area = feltArea.rect;
        Vector2 size = system.Sim.Size;
        float scale = Mathf.Min(area.width / size.x, area.height / size.y);
        Vector2 shown = new Vector2(Mathf.Floor(size.x * scale), Mathf.Floor(size.y * scale));
        if ((felt.sizeDelta - shown).sqrMagnitude > 0.25f) felt.sizeDelta = shown;
        zoom = scale;
    }

    float zoom = 1f;

    /// <summary>World (floor units, y down) to the felt's local space.</summary>
    Vector2 W2L(Vector2 w)
    {
        Rect r = batch.rectTransform.rect;
        return new Vector2(r.xMin + w.x * zoom, r.yMax - w.y * zoom);
    }

    /// <summary>The mouse over the felt (in floor units), and nothing else on screen in the way.</summary>
    bool PointerOnFelt(out Vector2 world)
    {
        world = Vector2.zero;
        if (batch == null) return false;
        RectTransform space = batch.rectTransform;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(space, Input.mousePosition, null, out Vector2 local)) return false;
        Rect r = space.rect;
        world = new Vector2((local.x - r.xMin) / Mathf.Max(0.0001f, zoom), (r.yMax - local.y) / Mathf.Max(0.0001f, zoom));
        if (!r.Contains(local)) return false;
        return !PointerOverOtherUi();
    }

    readonly List<RaycastResult> hits = new List<RaycastResult>();
    PointerEventData pointer;

    bool PointerOverOtherUi()
    {
        EventSystem events = EventSystem.current;
        if (events == null) return false;
        if (pointer == null || pointer.currentInputModule != events.currentInputModule) pointer = new PointerEventData(events);
        pointer.position = Input.mousePosition;
        hits.Clear();
        events.RaycastAll(pointer, hits);
        if (hits.Count == 0) return false;
        GameObject top = hits[0].gameObject;
        return top != feltImage.gameObject;
    }

    /// <summary>The mouse for the simulation (CashGrabSystem calls this right before each step).</summary>
    public void FeedInput(CashFloorSim sim)
    {
        var input = new CashFloorSim.PlayerInput();
        bool usable = IsOpen && canvas.enabled && !(shop != null && shop.IsOpen) && !UIManager.IsTypingInInputField();
        if (usable)
        {
            bool onFelt = PointerOnFelt(out Vector2 world);
            input.cursor = world;
            input.onFloor = onFelt;
            if (Input.GetMouseButtonDown(0)) pressOnFloor = onFelt;
            bool lmb = Input.GetMouseButton(0);
            input.down = Input.GetMouseButtonDown(0) && onFelt;
            input.held = lmb && pressOnFloor;
            input.up = Input.GetMouseButtonUp(0) && pressOnFloor;
            input.altDown = Input.GetMouseButtonDown(1) && onFelt;
            if (!lmb && !Input.GetMouseButtonUp(0)) pressOnFloor = false;
            // While carrying, the hand may leave the felt (over a panel): it keeps following the mouse at the edge
            if (!onFelt && sim.Held.Count > 0) input.cursor = world;
            SetCursorHidden(onFelt || (input.held && sim.Held.Count > 0));
        }
        else
        {
            pressOnFloor = false;
            SetCursorHidden(false);
        }
        sim.Input = input;
    }

    void SetCursorHidden(bool hidden)
    {
        if (hidden == cursorHidden) return;
        cursorHidden = hidden;
        Cursor.visible = !hidden;
    }

    void RestoreCursor()
    {
        if (!cursorHidden) return;
        cursorHidden = false;
        Cursor.visible = true;
    }

    // ------------------------------------------------------------------
    // Toolbar actions
    // ------------------------------------------------------------------

    void OnToolSlot(CashTool tool)
    {
        if (system.ToolLevel(tool) > 0)
        {
            if (system.SelectTool(tool) && system.Audio != null) system.Audio.Play("grab", 0.5f, 1.4f, 0.05f);
            return;
        }
        shop.OpenAt(tool);
    }

    void OnGadgetSlot(CashGadget gadget)
    {
        if (system.GadgetLevel(gadget) > 0)
        {
            if (system.SelectGadget(gadget) && system.Audio != null) system.Audio.Play("grab", 0.5f, 1.2f, 0.05f);
            return;
        }
        shop.OpenAt(gadget);
    }

    // ------------------------------------------------------------------
    // Panels
    // ------------------------------------------------------------------

    void RefreshPanels(bool force)
    {
        if (system == null || system.Ledger == null) return;
        CompanyManager company = CompanyManager.Instance;

        double cash = system.Ledger.PlayerCash;
        if (shownCash < 0.0 || force) cashAnim = cash;
        cashAnim = cash > cashAnim ? Math.Min(cash, cashAnim + Math.Max(1.0, (cash - cashAnim) * Time.unscaledDeltaTime * 8.0))
                                   : Math.Max(cash, cashAnim - Math.Max(1.0, (cashAnim - cash) * Time.unscaledDeltaTime * 8.0));
        if (Math.Abs(cashAnim - shownCash) >= 0.5 || force)
        {
            shownCash = cashAnim;
            cashText.text = UIFormat.Money(cashAnim);
        }
        cashText.rectTransform.localScale = Vector3.one * (1f + cashPunch * 0.18f);
        cashPunch = Mathf.Max(0f, cashPunch - Time.unscaledDeltaTime * 3f);

        if (company != null)
        {
            string value = UIFormat.Money(company.displayedValue);
            if (value != lastValue) { lastValue = value; valueText.text = value; }
            string day = CashText.Get("cash.bar.day", ("day", company.currentDay));
            if (day != lastDay)
            {
                lastDay = day;
                dayText.text = day;
                countInText.text = CashText.Get("cash.bar.count_in", ("days", system.DaysUntilCount));
            }
            Vector2 max = dayFill.anchorMax;
            float p = company.DayProgress;
            if (Mathf.Abs(max.x - p) > 0.002f) { max.x = p; dayFill.anchorMax = max; }
        }
        string today = CashText.Get("cash.bar.today", ("amount", UIFormat.Money(system.TodayCollected)));
        if (today != lastToday) { lastToday = today; todayText.text = today; }

        RefreshTask();
        RefreshDepartments(force);
        RefreshToolbar();
    }

    float cashPunch;

    void RefreshTask()
    {
        HiringTaskManager tasks = HiringTaskManager.Instance;
        bool active = tasks != null && tasks.IsTaskActive && tasks.CurrentTask != null;
        if (taskChip.activeSelf != active) taskChip.SetActive(active);
        if (!active) return;
        taskText.text = CashText.Get("cash.bar.task", ("time", UIFormat.Duration(tasks.RemainingTime)));
    }

    float panelTimer;

    void RefreshDepartments(bool force)
    {
        panelTimer -= Time.unscaledDeltaTime;
        if (!force && panelTimer > 0f) return;
        panelTimer = 0.2f;
        CompanyManager company = CompanyManager.Instance;
        for (int d = 0; d < CashLedger.DeptCount; d++)
        {
            DeptCard card = deptCards[d];
            int level = system.DeptLevel(d);
            for (int i = 0; i < card.pips.Length; i++)
                card.pips[i].color = i < level ? CashRules.DeptColors[d] : UIColors.WithAlpha(UIColors.Ink, 0.15f);
            card.budget.text = UIFormat.Money(system.Ledger.Budget(d));
            if (level <= 0)
            {
                card.level.text = "";
                card.next.text = "";
                card.perk.text = "";
                int day = CashRules.JoinDay((CashDept)d);
                card.status.text = d == 0
                    ? CashText.Get("cash.dept.status.backend_soon", ("day", day))
                    : CashText.Get("cash.dept.status.soon", ("day", day), ("staff", CashRules.JoinHeadcount((CashDept)d)));
                SetFill(card.fill, 0f);
                card.box.Fill = UIColors.PaperShade;
                continue;
            }
            card.box.Fill = UIColors.PaperHi;
            int hires = system.DeptHires(d);
            card.level.text = hires > 0 ? CashText.Get("cash.dept.level_hires", ("level", level), ("hires", hires)) : CashText.Get("cash.dept.level", ("level", level));
            double next = system.NextDeptCost(d);
            if (double.IsInfinity(next))
            {
                card.next.text = CashText.Get("cash.dept.max");
                SetFill(card.fill, 1f);
            }
            else
            {
                card.next.text = CashText.Get("cash.dept.next", ("amount", UIFormat.Money(next)));
                SetFill(card.fill, (float)Math.Min(1.0, system.Ledger.Budget(d) / Math.Max(1.0, next)));
            }
            CashDept dept = (CashDept)d;
            float perk = CashRules.PerkPerLevel(dept) * level;
            card.perk.text = dept == CashDept.Backend
                ? CashText.Get("cash.dept.perk.backend", ("percent", Mathf.RoundToInt(perk * 100f)))
                : CashText.Get(dept == CashDept.Frontend ? "cash.dept.perk.frontend" : "cash.dept.perk.marketing", ("points", perk.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)));
            card.status.text = CashText.Get("cash.dept.style." + CashRules.DeptIds[d]);
        }

        CashFloorSim sim = system.Sim;
        floorInfoText.text = CashText.Get("cash.floor.info", ("floor", UIFormat.Money(sim.FloorValue)), ("rate", UIFormat.Money(system.Stream.RatePerSecond)));
    }

    static void SetFill(RectTransform fill, float f)
    {
        Vector2 max = fill.anchorMax;
        f = Mathf.Clamp01(f);
        if (Mathf.Abs(max.x - f) < 0.002f) return;
        max.x = f;
        fill.anchorMax = max;
    }

    void RefreshToolbar()
    {
        CashFloorSim sim = system.Sim;
        int seats = CashGrabSystem.OfficeSeats();
        for (int t = 0; t < CashRules.ToolCount; t++)
        {
            CashTool tool = (CashTool)t;
            ToolSlot slot = toolSlots[t];
            int level = system.ToolLevel(tool);
            bool owned = level > 0;
            bool selected = owned && system.SelectedTool == tool;
            bool unlocked = system.ToolUnlocked(tool);
            slot.box.Fill = selected ? UIColors.Highlight : owned ? UIColors.PaperHi : UIColors.PaperShade;
            slot.box.SetRings(selected ? UIColors.Ink : Color.clear, selected ? 3f : 0f, Color.clear, 0f);
            slot.icon.color = UIColors.WithAlpha(UIColors.Ink, owned ? 1f : 0.35f);
            slot.lockIcon.SetActive(!owned && !unlocked);
            for (int i = 0; i < slot.pips.Length; i++)
            {
                bool show = owned && CashRules.MaxLevel(tool) > 1;
                slot.pips[i].enabled = show;
                if (show) slot.pips[i].color = i < level ? UIColors.Money : UIColors.WithAlpha(UIColors.Ink, 0.18f);
            }
            if (owned) slot.price.text = "";
            else if (!unlocked) slot.price.text = CashText.Get("cash.tool.needs_seats", ("seats", CashRules.ToolSeatsNeeded(tool)));
            else slot.price.text = UIFormat.Money(CashRules.ToolPrice(tool, 0));
            slot.price.color = !unlocked ? UIColors.Pencil : PlayerWallet.CanAfford(CashRules.ToolPrice(tool, 0)) ? UIColors.Money : UIColors.Loss;
        }

        CashGadget[] active = { CashGadget.Stapler, CashGadget.Paperweight, CashGadget.Fan };
        for (int g = 0; g < active.Length; g++)
        {
            CashGadget gadget = active[g];
            ToolSlot slot = gadgetSlots[g];
            int level = system.GadgetLevel(gadget);
            bool owned = level > 0;
            bool selected = owned && system.SelectedGadget == gadget;
            bool unlocked = system.GadgetUnlocked(gadget);
            slot.box.Fill = selected ? UIColors.StickyPink : owned ? UIColors.PaperHi : UIColors.PaperShade;
            slot.box.SetRings(selected ? UIColors.Ink : Color.clear, selected ? 3f : 0f, Color.clear, 0f);
            slot.icon.color = UIColors.WithAlpha(UIColors.Ink, owned ? 1f : 0.35f);
            slot.lockIcon.SetActive(!owned && !unlocked);
            for (int i = 0; i < slot.pips.Length; i++)
            {
                slot.pips[i].enabled = owned && i < CashRules.MaxGadgetLevel;
                if (slot.pips[i].enabled) slot.pips[i].color = i < level ? UIColors.Money : UIColors.WithAlpha(UIColors.Ink, 0.18f);
            }
            slot.price.text = owned ? "" : unlocked ? UIFormat.Money(CashRules.GadgetPrice(gadget, 0)) : "";
            float total = sim.GadgetCooldownTotal(gadget);
            float left = sim.GadgetCooldownLeft(gadget);
            float shade = owned && total > 0f ? Mathf.Clamp01(left / total) : 0f;
            RectTransform cd = slot.cooldown.rectTransform;
            Vector2 min = cd.anchorMin;
            float wanted = 1f - shade;   // the shade covers the top part: from (1 - shade) up to the top
            if (Mathf.Abs(min.y - wanted) > 0.002f) cd.anchorMin = new Vector2(min.x, wanted);
            if (slot.cooldown.enabled != shade > 0.001f) slot.cooldown.enabled = shade > 0.001f;
        }

        int gloves = system.GadgetLevel(CashGadget.Gloves);
        glovesBadge.SetActive(gloves > 0 || system.GadgetUnlocked(CashGadget.Gloves));
        glovesText.text = gloves > 0 ? CashText.Get("cash.gadget.gloves.badge", ("percent", Mathf.RoundToInt(CashRules.GlovesResist(gloves) * 100f))) : CashText.Get("cash.gadget.gloves.shop");

        if (sim.Streak > 0)
        {
            streakText.text = CashText.Get("cash.streak", ("count", sim.Streak));
            SetFill(streakFill, sim.StreakTimeLeft / CashFloorSim.StreakWindow);
        }
        else
        {
            streakText.text = CashText.Get("cash.streak.none");
            SetFill(streakFill, 0f);
        }
    }
}
