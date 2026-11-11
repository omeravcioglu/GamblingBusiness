using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The campus part of TowerView (2026-09-29): the camera that pans along the row of buildings, scrolls up and down and
/// zooms out to the whole campus; the campus bar (previous / next building, zoom, the overview); the purchase slip
/// (a building, a skybridge, a room's next level: what it costs against the build budget and "Gamble for it" when it is
/// short); and the drawer's Campus page (building types and links).
/// - Camera: towerContent is scaled by the zoom and moved so design x panX is in the middle of the screen; the vertical
///   scroll keeps the street 200 px above the bottom at 0. Chips and slips on the campus are counter-scaled (ChipScale) so
///   they stay readable when zoomed out. Room names hide below zoom 0.34; the buildings' plates stay.
/// </summary>
public partial class TowerView
{
    const float MinZoom = 0.16f;
    const float MaxZoom = 1.25f;
    /// <summary>Height the campus bar takes under the top bar (the camera keeps roofs below it).</summary>
    const float CampusBarSpace = 60f;

    // Camera (design pixels)
    float scroll;
    float scrollTarget;
    float maxScroll;
    float zoom = 1f;
    float zoomTarget = 1f;
    float drawnZoom = 1f;
    float panX = 960f;
    float panTarget = 960f;
    bool overview;

    int campusBarKey, purchaseKey;
    readonly List<TowerCatalogCard> buildingCards = new List<TowerCatalogCard>();
    readonly List<TowerCatalogCard> linkCards = new List<TowerCatalogCard>();

    /// <summary>The camera's zoom (1 = the design's size).</summary>
    public float Zoom => zoom;
    /// <summary>The zoom has arrived where it was going (the meshes redraw crisp then).</summary>
    bool ZoomSettled => Mathf.Abs(zoom - zoomTarget) < 0.0005f;
    /// <summary>Scale of chips and slips on the campus: the inverse of the zoom (capped), so they keep their size on screen.</summary>
    Vector3 ChipScale
    {
        get
        {
            float s = 1f / Mathf.Max(0.34f, zoom);
            return new Vector3(s, s, 1f);
        }
    }

    // ------------------------------------------------------------------
    // Camera
    // ------------------------------------------------------------------

    /// <summary>Screen y (from the top, canvas units) of design height <paramref name="y"/> at the current camera.</summary>
    float ScreenY(float y)
    {
        return ScreenHeight - (GroundFromBottom - scroll + zoom * (TowerGeo.Ground - y));
    }

    /// <summary>Screen x (from the left, canvas units) of design x <paramref name="x"/> at the current camera.</summary>
    float ScreenX(float x)
    {
        return ScreenWidth * 0.5f + zoom * (x - panX);
    }

    static float Approach(float current, float target, float rate)
    {
        if (Mathf.Abs(current - target) <= 0.0005f * Mathf.Max(1f, Mathf.Abs(target))) return target;
        return Mathf.Lerp(current, target, 1f - Mathf.Exp(-rate * Time.unscaledDeltaTime));
    }

    void UpdateCamera(TowerBuilder b)
    {
        TowerPlan plan = b.Plan;
        zoomTarget = Mathf.Clamp(zoomTarget, MinZoom, MaxZoom);
        zoom = Approach(zoom, zoomTarget, 12f);

        // Pan: keep the view on the campus (centred when the whole row fits)
        float left = TowerGeo.CampusLeft(plan) - 160f, right = TowerGeo.CampusRight(plan) + 160f;
        float half = ScreenWidth * 0.5f / zoom;
        if (right - left <= half * 2f) panTarget = (left + right) * 0.5f;
        else panTarget = Mathf.Clamp(panTarget, left + half, right - half);
        panX = Approach(panX, panTarget, 12f);

        // Scroll: up to the tallest roof (and its add-floor slot) just below the top bar and the campus bar
        float top = TowerGeo.Ground;
        foreach (TowerBuilding building in plan.Buildings)
            top = Mathf.Min(top, TowerGeo.TopOf(building.Floors) - (building.IsTopFloorReached ? 60f : TowerGeo.FloorHeight + 16f));
        maxScroll = Mathf.Max(0f, TopBarHeight + CampusBarSpace + 24f - ScreenHeight + GroundFromBottom + zoom * (TowerGeo.Ground - top));
        scrollTarget = Mathf.Clamp(scrollTarget, 0f, maxScroll);
        if (Mathf.Abs(scroll - scrollTarget) > 0.25f)
            scroll = Mathf.Lerp(scroll, scrollTarget, 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime));
        else
            scroll = scrollTarget;
        scroll = Mathf.Max(0f, scroll);

        if (towerContent != null)
        {
            BlueprintUI.SetLocalScale(towerContent, new Vector3(zoom, zoom, 1f));
            BlueprintUI.SetAnchoredPosition(towerContent, new Vector2(-(panX - 960f) * zoom, GroundFromBottom - scroll));
        }
        if (gridLayer != null)
        {
            // Grid lines sit on the design's 24/96 px lines of the HQ at zoom 1, whatever the screen's shape, pan and scroll
            Vector2 offset = new Vector2(Mathf.Repeat((viewSize.x - 1920f) * 0.5f - (panX - 960f) * zoom, 96f) - 96f,
                                         96f - Mathf.Repeat(ScreenHeight - 1080f + scroll, 96f));
            if (offset != shownGridOffset)
            {
                shownGridOffset = offset;
                BlueprintUI.SetAnchoredPosition(gridLayer.rectTransform, offset);
            }
        }
    }

    /// <summary>Scroll the campus by design pixels at zoom 1 (positive = show higher floors).</summary>
    public void ScrollBy(float amount, bool immediate = false)
    {
        scrollTarget = Mathf.Clamp(scrollTarget + amount * Mathf.Max(0.25f, zoom), 0f, maxScroll);
        if (immediate) scroll = scrollTarget;
    }

    /// <summary>Pan along the campus by design pixels (positive = further right).</summary>
    public void PanBy(float amount, bool immediate = false)
    {
        panTarget += amount;
        overview = false;
        if (immediate) panX = panTarget;
    }

    /// <summary>Zoom by <paramref name="factor"/> around the middle of the screen.</summary>
    public void ZoomBy(float factor)
    {
        zoomTarget = Mathf.Clamp(zoomTarget * factor, MinZoom, MaxZoom);
        overview = false;
    }

    /// <summary>Zoom by <paramref name="factor"/>, keeping the campus point under <paramref name="screen"/> where it is.</summary>
    public void ZoomBy(float factor, Vector2 screen)
    {
        float before = zoomTarget;
        float after = Mathf.Clamp(zoomTarget * factor, MinZoom, MaxZoom);
        if (Mathf.Approximately(before, after)) return;
        float canvasX = ScreenToCanvasDelta(screen).x - ScreenWidth * 0.5f;
        float designX = panTarget + canvasX / Mathf.Max(0.01f, before);
        panTarget = designX - canvasX / after;
        zoomTarget = after;
        overview = false;
    }

    /// <summary>A screen-pixel distance in canvas units.</summary>
    public Vector2 ScreenToCanvasDelta(Vector2 screenDelta)
    {
        float scale = canvas != null ? Mathf.Max(0.01f, canvas.scaleFactor) : 1f;
        return screenDelta / scale;
    }

    /// <summary>Centre the view on a building at zoom 1 (leaving the overview).</summary>
    public void FocusBuilding(TowerBuilding building, bool instant = false)
    {
        TowerBuilder b = TowerBuilder.Instance;
        if (building == null || b == null) return;
        panTarget = TowerGeo.CenterX(b.Plan, building);
        if (overview || zoomTarget < 0.6f) zoomTarget = 1f;
        overview = false;
        if (instant)
        {
            panX = panTarget;
            zoom = zoomTarget;
        }
        campusBarKey = 0;
    }

    /// <summary>Zoom out to the whole campus, or back to <paramref name="focus"/> at zoom 1.</summary>
    public void ToggleOverview(TowerBuilding focus)
    {
        TowerBuilder b = TowerBuilder.Instance;
        if (b == null) return;
        TowerPlan plan = b.Plan;
        if (!overview)
        {
            float width = TowerGeo.CampusRight(plan) - TowerGeo.CampusLeft(plan) + 320f;
            float tallest = 1f;
            foreach (TowerBuilding building in plan.Buildings) tallest = Mathf.Max(tallest, building.Floors + 1);
            float byWidth = (ScreenWidth - 120f) / Mathf.Max(1f, width);
            float byHeight = (ScreenHeight - TopBarHeight - CampusBarSpace - GroundFromBottom - 40f) / Mathf.Max(1f, tallest * TowerGeo.FloorHeight);
            zoomTarget = Mathf.Clamp(Mathf.Min(byWidth, Mathf.Max(byHeight, byWidth * 0.5f)), MinZoom, 1f);
            panTarget = (TowerGeo.CampusLeft(plan) + TowerGeo.CampusRight(plan)) * 0.5f;
            scrollTarget = 0f;
            overview = true;
        }
        else
        {
            overview = false;
            zoomTarget = 1f;
            if (focus != null) panTarget = TowerGeo.CenterX(plan, focus);
        }
        campusBarKey = 0;
    }

    /// <summary>Scroll so the HQ's roof and its add-floor slot are in view.</summary>
    public void ScrollToRoof()
    {
        TowerBuilder b = TowerBuilder.Instance;
        if (b != null) ScrollToRoof(b.Plan.Headquarters);
    }

    /// <summary>Scroll (and pan, if it's off screen) so <paramref name="building"/>'s roof and add-floor slot are in view.</summary>
    public void ScrollToRoof(TowerBuilding building)
    {
        TowerBuilder b = TowerBuilder.Instance;
        if (b == null || building == null) return;
        UpdateCamera(b);
        float top = TowerGeo.TopOf(building.Floors) - (building.IsTopFloorReached ? 60f : TowerGeo.FloorHeight + 16f);
        float needed = TopBarHeight + CampusBarSpace + 24f - ScreenHeight + GroundFromBottom + zoom * (TowerGeo.Ground - top);
        scrollTarget = Mathf.Clamp(needed, 0f, maxScroll);
        float x = ScreenX(TowerGeo.CenterX(b.Plan, building));
        if (x < 0f || x > ScreenWidth) panTarget = TowerGeo.CenterX(b.Plan, building);
    }

    // ------------------------------------------------------------------
    // Buttons of the campus parts
    // ------------------------------------------------------------------

    void HookCampusButtons()
    {
        AddClick(campusBar.previousButton, b => b.FocusNext(-1));
        AddClick(campusBar.nextButton, b => b.FocusNext(1));
        AddClick(campusBar.nameButton, b => b.FocusOn(b.FocusBuilding));
        if (campusBar.zoomOutButton != null) campusBar.zoomOutButton.onClick.AddListener(() => ZoomBy(1f / 1.25f));
        if (campusBar.zoomInButton != null) campusBar.zoomInButton.onClick.AddListener(() => ZoomBy(1.25f));
        AddClick(campusBar.overviewButton, b => b.ToggleOverview());
        AddClick(campusBar.buyButton, b => b.SetTool(TowerTool.Campus));
        DimForDemo(campusBar.buyButton);   // Demo: refused with the campus text (TowerBuilder.SetTool)

        AddClick(purchaseSlip.confirmButton, b => b.ConfirmPurchase());
        AddClick(purchaseSlip.gambleButton, b => b.GambleForCurrent());
        AddClick(purchaseSlip.cancelButton, b => b.CancelPurchase());
        AddClick(purchaseSlip.closeButton, b => b.CancelPurchase());
    }

    // ------------------------------------------------------------------
    // The campus bar (top centre): previous / the building in view / next, buy, zoom, overview
    // ------------------------------------------------------------------

    void RefreshCampusBar(TowerBuilder b)
    {
        TowerPlan plan = b.Plan;
        bool show = b.InsideRoom == null && !b.UpgradeOpen;
        TowerBuilding focus = b.FocusBuilding ?? plan.Headquarters;
        int key = show ? Hash(focus.Id, focus.Floors, plan.BuildingCount, overview ? 1 : 0, Mathf.RoundToInt(zoomTarget * 100f)) : -1;
        if (key == campusBarKey) return;
        campusBarKey = key;
        if (campusBar.panel != null && campusBar.panel.gameObject.activeSelf != show) campusBar.panel.gameObject.SetActive(show);
        if (!show) return;

        bool many = plan.BuildingCount > 1;
        SetText(campusBar.nameText, TowerText.Get("tower.campus_bar.building", ("building", TowerText.BuildingName(focus)), ("count", focus.Floors)));
        SetText(campusBar.countText, many ? TowerText.Get("tower.campus_bar.count", ("index", focus.Slot + 1), ("count", plan.BuildingCount))
                                          : TowerText.Get("tower.campus_bar.single"));
        SetInteractable(campusBar.previousButton, many);
        SetInteractable(campusBar.nextButton, many);
        SetInteractable(campusBar.zoomOutButton, zoomTarget > MinZoom + 0.001f);
        SetInteractable(campusBar.zoomInButton, zoomTarget < MaxZoom - 0.001f);
        SetText(campusBar.overviewText, TowerText.Get(overview ? "tower.campus_bar.back" : "tower.campus_bar.overview"));
        if (campusBar.panel != null) LayoutRebuilder.ForceRebuildLayoutImmediate(campusBar.panel);
    }

    // ------------------------------------------------------------------
    // The drawer's Campus page: building types and links
    // ------------------------------------------------------------------

    void RefreshCampusPage(TowerBuilder b)
    {
        TowerPlan plan = b.Plan;
        TowerRules rules = b.Rules;
        TowerTotals totals = b.GetDailyTotals();
        SetText(drawer.campusSummary, TowerText.Get("tower.campus.summary", ("buildings", plan.BuildingCount), ("floors", plan.TotalFloors),
                                                    ("links", plan.Connections.Count), ("upkeep", TowerText.Upkeep(totals.upkeep))));
        if (drawer.cardTemplate == null) return;

        int count = 0;
        if (drawer.buildingList != null)
        {
            foreach (TowerBuildingType type in rules.buildingTypes)
            {
                if (type.isHeadquarters) continue;
                TowerCatalogCard card = CampusCard(buildingCards, drawer.buildingList, count++, "Building");
                card.buildingType = type;
                card.connectionType = null;
                TowerCheck check = plan.CheckBuyBuilding(type, false);
                bool locked = check.issue == TowerIssue.Locked || check.issue == TowerIssue.BuildingLimit;
                string reason = check.issue == TowerIssue.Locked ? TowerText.LockText(check.lockInfo, null, rules)
                              : check.issue == TowerIssue.BuildingLimit ? TowerText.Get("tower.issue.building_limit", ("count", check.limit)) : "";
                string effect = TowerText.Get("tower.campus.building.effect", ("tiles", type.tiles), ("floors", type.maxFloors),
                                              ("upkeep", TowerText.Upkeep(type.upkeepPerFloor)));
                string right = type.maxCount > 0 ? TowerText.Get("tower.campus.owned", ("count", plan.CountBuildings(type)), ("max", type.maxCount)) : "";
                double price = plan.BuildingPrice(type);   // after the manager's discount, if any
                card.BindCustom(TowerText.BuildingTypeName(type), price, effect, right, reason, locked, plan.CanAfford(price), false,
                                TowerIconKind.Building, BuildingPaper(type), UIColors.Ink);
            }
        }
        for (int i = count; i < buildingCards.Count; i++)
            if (buildingCards[i].gameObject.activeSelf) buildingCards[i].gameObject.SetActive(false);

        count = 0;
        if (drawer.linkList != null)
        {
            foreach (TowerConnectionType type in rules.connectionTypes)
            {
                if (type.kind != TowerConnectionKind.Skybridge) continue;   // the one kind built today
                TowerCatalogCard card = CampusCard(linkCards, drawer.linkList, count++, "Link");
                card.connectionType = type;
                card.buildingType = null;
                TowerLock lockInfo = plan.LockOf(type);
                string effect = TowerText.Get("tower.campus.link.effect", ("effect", TowerText.ConnectionEffect(type)), ("upkeep", TowerText.Upkeep(type.upkeep)));
                string right = TowerText.Get("tower.campus.link.floor", ("floor", type.minFloor));
                double from = plan.LinkPrice(type, type.minFloor);
                card.BindCustom(TowerText.ConnectionName(type), from, effect, right, lockInfo.IsLocked ? TowerText.LockText(lockInfo, null, rules) : "",
                                lockInfo.IsLocked, plan.CanAfford(from), type == b.PickLink, TowerIconKind.Bridge, UIColors.StickyBlue, UIColors.Ink, true);
            }
        }
        for (int i = count; i < linkCards.Count; i++)
            if (linkCards[i].gameObject.activeSelf) linkCards[i].gameObject.SetActive(false);
    }

    TowerCatalogCard CampusCard(List<TowerCatalogCard> pool, RectTransform list, int index, string name)
    {
        while (pool.Count <= index)
        {
            TowerCatalogCard card = Instantiate(drawer.cardTemplate, list, false);
            card.name = name + " " + (pool.Count + 1);
            TowerCatalogCard clicked = card;
            if (card.button != null)
                card.button.onClick.AddListener(() => WithBuilder(x =>
                {
                    if (clicked.buildingType != null) x.OpenBuyBuilding(clicked.buildingType);
                    else if (clicked.connectionType != null) x.PickConnection(clicked.connectionType);
                }));
            pool.Add(card);
        }
        TowerCatalogCard c = pool[index];
        if (!c.gameObject.activeSelf) c.gameObject.SetActive(true);
        return c;
    }

    /// <summary>The icon box's colour on a building card (its type's wash, stronger).</summary>
    static Color BuildingPaper(TowerBuildingType type)
    {
        Color tint = type.tint;
        tint.a = 1f;
        return Color.Lerp(UIColors.PaperHi, tint, 0.7f);
    }

    // ------------------------------------------------------------------
    // The purchase slip: a building, a skybridge or a room's next level
    // ------------------------------------------------------------------

    void RefreshPurchaseSlip(TowerBuilder b)
    {
        TowerPurchase purchase = b.Purchase;
        bool show = b.PurchaseOpen && b.InsideRoom == null && b.Gamble == TowerGambleKind.None;
        TowerCheck check = show ? b.PurchaseCheck : default;
        int key = show ? Hash((int)purchase.kind, PurchaseId(purchase), b.PlanVersion, b.Plan.Budget.GetHashCode(), Hash((int)check.issue, TowerBuilder.Employees, 0, 0, 0)) : -1;
        if (key == purchaseKey) return;
        purchaseKey = key;
        bool appearing = show && purchaseSlip.layer != null && !purchaseSlip.layer.gameObject.activeSelf;
        if (purchaseSlip.layer != null && purchaseSlip.layer.gameObject.activeSelf != show) purchaseSlip.layer.gameObject.SetActive(show);
        if (!show) return;
        // On top when it opens (not on every change: the "Gamble for it" pop-up above it must stay above it)
        if (appearing) purchaseSlip.layer.SetAsLastSibling();

        TowerPlan plan = b.Plan;
        double price = check.price;
        bool afford = plan.CanAfford(price);
        bool buyable = check.Ok;
        string title, body, note;
        TowerIconKind icon;
        Color paper;
        DescribePurchase(b, purchase, out title, out body, out note, out icon, out paper);

        SetText(purchaseSlip.titleText, title);
        SetText(purchaseSlip.bodyText, body);
        SetText(purchaseSlip.noteText, note);
        if (purchaseSlip.noteText != null) purchaseSlip.noteText.gameObject.SetActive(!string.IsNullOrEmpty(note));
        if (purchaseSlip.icon != null) purchaseSlip.icon.Kind = icon;
        if (purchaseSlip.iconBox != null) purchaseSlip.iconBox.Fill = paper;
        SetText(purchaseSlip.budgetValue, UIFormat.Money(plan.Budget));
        SetText(purchaseSlip.costValue, UIFormat.Minus + UIFormat.Money(price));
        SetText(purchaseSlip.afterValue, afford ? UIFormat.Money(plan.Budget - price)
                                                : TowerText.Get("tower.floor_slip.needs_more", ("amount", UIFormat.Money(price - plan.Budget))));
        if (purchaseSlip.afterValue != null) purchaseSlip.afterValue.color = afford ? UIColors.Ink : UIColors.Loss;

        // Short of money: "Gamble for it" (a win covers the gap); another reason (a limit reached meanwhile): say it
        bool blockedOtherwise = !buyable && check.issue != TowerIssue.CantAfford;
        SetText(purchaseSlip.reasonText, blockedOtherwise ? b.Describe(check, purchase.room != null ? purchase.room.Definition : null) : "");
        if (purchaseSlip.reasonText != null) purchaseSlip.reasonText.gameObject.SetActive(blockedOtherwise);
        SetText(purchaseSlip.confirmText, TowerText.Get("tower.purchase.buy", ("price", UIFormat.Money(price))));
        StylePrimary(purchaseSlip.confirmButton, purchaseSlip.confirmText, buyable);
        bool gamble = !afford && !blockedOtherwise;
        if (purchaseSlip.gambleButton != null) purchaseSlip.gambleButton.gameObject.SetActive(gamble);
        SetText(purchaseSlip.gambleText, TowerText.Get("tower.purchase.gamble", ("amount", UIFormat.Money(Math.Max(0.0, price - plan.Budget)))));
        if (purchaseSlip.slip != null) LayoutRebuilder.ForceRebuildLayoutImmediate(purchaseSlip.slip);
    }

    static int PurchaseId(TowerPurchase purchase)
    {
        switch (purchase.kind)
        {
            case TowerPurchaseKind.Building: return purchase.buildingType != null ? purchase.buildingType.GetHashCode() : 0;
            case TowerPurchaseKind.Connection: return (purchase.left != null ? purchase.left.Id * 131 : 0) + purchase.floor;
            case TowerPurchaseKind.RoomLevel: return purchase.room != null ? purchase.room.Id * 31 + purchase.room.Level : 0;
            default: return 0;
        }
    }

    /// <summary>The slip's words: what it is, what it adds (places, effects, upkeep) and a note.</summary>
    static void DescribePurchase(TowerBuilder b, TowerPurchase purchase, out string title, out string body, out string note, out TowerIconKind icon, out Color paper)
    {
        TowerPlan plan = b.Plan;
        int employees = TowerBuilder.Employees, capacity = b.TotalCapacity;
        float savings = plan.UpkeepSavingsShare;
        note = "";
        switch (purchase.kind)
        {
            case TowerPurchaseKind.Building:
            {
                TowerBuildingType type = purchase.buildingType;
                title = TowerText.Get("tower.purchase.building.title", ("building", TowerText.BuildingTypeName(type)));
                string description = TowerText.BuildingTypeDescription(type);
                body = TowerText.Join(description, TowerText.Get("tower.purchase.building.body", ("tiles", type.tiles), ("floors", type.maxFloors)));
                note = TowerText.Get("tower.purchase.upkeep.floor", ("upkeep", TowerText.Upkeep(type.upkeepPerFloor * (1.0 - savings))));
                icon = TowerIconKind.Building;
                paper = BuildingPaper(type);
                break;
            }
            case TowerPurchaseKind.Connection:
            {
                TowerConnectionType type = purchase.connectionType;
                TowerBuilding right = plan.RightNeighbour(purchase.left);
                title = TowerText.Get("tower.purchase.link.title", ("link", TowerText.ConnectionName(type)), ("left", TowerText.BuildingName(purchase.left)),
                                      ("right", TowerText.BuildingName(right)), ("floor", purchase.floor));
                body = TowerText.Join(TowerText.ConnectionDescription(type), TowerText.ConnectionEffect(type));
                note = TowerText.Get("tower.purchase.upkeep", ("upkeep", TowerText.Upkeep(type.upkeep * (1.0 - savings))));
                icon = TowerIconKind.Bridge;
                paper = UIColors.StickyBlue;
                break;
            }
            case TowerPurchaseKind.RoomLevel:
            {
                TowerRoom room = purchase.room;
                TowerRoomDefinition d = room.Definition;
                int next = Math.Min(d.MaxLevel, room.Level + 1);
                title = TowerText.Get("tower.purchase.level.title", ("room", TowerText.RoomName(room)), ("level", next));
                var parts = new List<string>(3);
                int placesNow = room.Capacity, placesNext = d.CapacityAt(next);
                if (placesNext != placesNow)
                    parts.Add(TowerText.Get("tower.purchase.level.places", ("from", placesNow), ("to", placesNext),
                                            ("employees", TowerText.EmployeesOf(employees, capacity)),
                                            ("after", TowerText.EmployeesOf(employees, capacity - placesNow + placesNext))));
                float multNow = d.MultiplierAt(room.Level), multNext = d.MultiplierAt(next);
                if (d.HasBonus && !Mathf.Approximately(multNow, multNext))
                    parts.Add(TowerText.Get("tower.purchase.level.effects", ("from", multNow.ToString("0.0#", Inv)), ("to", multNext.ToString("0.0#", Inv))));
                if (d.LookAt(next) > d.LookAt(room.Level)) parts.Add(TowerText.Get("tower.purchase.level.look"));
                body = string.Join("\n", parts);
                note = TowerText.Get("tower.purchase.level.upkeep", ("from", TowerText.Upkeep(d.UpkeepAt(room.Level) * (1.0 - savings))),
                                     ("to", TowerText.Upkeep(d.UpkeepAt(next) * (1.0 - savings))));
                icon = TowerIconKind.Upgrade;
                paper = d.paper;
                break;
            }
            default:
                title = body = "";
                icon = TowerIconKind.None;
                paper = UIColors.PaperHi;
                break;
        }
    }
}

// ----------------------------------------------------------------------
// References of the campus parts (filled by TowerViewFactory, or by a designed prefab)
// ----------------------------------------------------------------------

[Serializable]
public class TowerCampusBar
{
    public RectTransform panel;
    public Button previousButton;
    public Button nextButton;
    [Tooltip("The building in view (a click centres it again)")]
    public Button nameButton;
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI countText;
    [Tooltip("Opens the Campus drawer (buy a building, build a skybridge)")]
    public Button buyButton;
    public Button zoomOutButton;
    public Button zoomInButton;
    public Button overviewButton;
    public TextMeshProUGUI overviewText;
}

[Serializable]
public class TowerPurchaseSlip
{
    [Tooltip("Scrim over the campus, the slip on top")]
    public RectTransform layer;
    public RectTransform slip;
    public BlueprintBox iconBox;
    public TowerIcon icon;
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI bodyText;
    public TextMeshProUGUI noteText;
    public TextMeshProUGUI budgetValue;
    public TextMeshProUGUI costValue;
    public TextMeshProUGUI afterValue;
    [Tooltip("Why it can't be bought when it isn't the money")]
    public TextMeshProUGUI reasonText;
    public Button confirmButton;
    public TextMeshProUGUI confirmText;
    [Tooltip("\"Gamble for it\": shown while the build budget is short")]
    public Button gambleButton;
    public TextMeshProUGUI gambleText;
    public Button cancelButton;
    public Button closeButton;
}
