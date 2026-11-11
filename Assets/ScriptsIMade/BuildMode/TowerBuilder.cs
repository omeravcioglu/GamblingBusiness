using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>The tool tray of the campus (keys 1-6). New values are appended.</summary>
public enum TowerTool
{
    None,
    BuildRoom,
    AddFloor,
    Furniture,
    Move,
    Sell,
    Campus   // buy buildings, build skybridges (2026-09-29)
}

/// <summary>A furniture gamble that is running.</summary>
public enum TowerGambleKind
{
    None,
    Blackjack,
    Roll
}

/// <summary>How a furniture upgrade ended (the upgrade sheet shows it).</summary>
public struct TowerUpgradeResult
{
    /// <summary>None = paid.</summary>
    public TowerGambleKind kind;
    /// <summary>The room and the slot: the screens name them in the current language (TowerText.ResultSlotName).</summary>
    public TowerRoom room;
    public int slot;
    /// <summary>English names of the room and the furniture (for logs).</summary>
    public string roomName;
    public string slotName;
    public int levelBefore;
    public int levelAfter;
    /// <summary>Roll: the outcome's level change (before the 0..5 limits).</summary>
    public int rolled;
    public BlackjackResult hand;
    public double stake;
    public bool refunded;
}

/// <summary>What the purchase slip is about to buy.</summary>
public enum TowerPurchaseKind
{
    None,
    Building,     // a new building to the right of the last one
    Connection,   // a skybridge between two neighbours
    RoomLevel     // a room's next level
}

/// <summary>A purchase waiting on the slip (TowerBuilder.Purchase).</summary>
public struct TowerPurchase
{
    public TowerPurchaseKind kind;
    public TowerBuildingType buildingType;
    public TowerConnectionType connectionType;
    /// <summary>Connection: the building on the left and the floor.</summary>
    public TowerBuilding left;
    public int floor;
    /// <summary>RoomLevel: the room.</summary>
    public TowerRoom room;
}

/// <summary>
/// The HQ campus's controller: owns the TowerPlan (rules, build budget), the tool, selection, hover and the placement
/// ghosts, reads the mouse and keys while build mode is open, runs the furniture upgrades (pay, blackjack, random chance),
/// buys buildings, skybridges and room levels, and saves the campus under "building". BuildModeCamera opens and closes
/// it; TowerView draws it.
/// - Keys: 1 Build room, 2 Add floor (to the building in view), 3 Furniture, 4 Move, 5 Sell, 6 Campus (again = off); E
///   upgrades the picked piece in the room view; W/S and the up/down arrows scroll, A/D and the left/right arrows pan along
///   the campus, the mouse wheel scrolls (Shift pans, Ctrl zooms), +/- zoom, Tab / Shift+Tab go to the next / previous
///   building, Home to the HQ, the middle mouse button drags. Right click cancels placing or moving.
/// - Escape (through UIManager) steps back: a result or the upgrade sheet, the purchase slip, the sell or add-floor slip,
///   the first-visit note, the room view, moving/placing, the selected room, the tool, then leaves build mode.
/// - Build mode is a pausing UIManager panel: the day, desk interaction and interrupts wait; it runs on unscaled time.
/// - Money: everything is paid from the build budget, which only gambling fills (the owner's decision, 2026-09-29): every
///   purchase that is short offers "Gamble for it" (GambleFor), which opens BuildGambleView with the bet preset so a win
///   covers the shortfall.
/// - Furniture gambles cost the same as paying and take the money first. Blackjack (the scene's BlackjackGame, on the HUD
///   canvas, so the tower screen is hidden meanwhile): a win gives +2 levels (capped), a push refunds, a loss gives
///   nothing; it settles when the hand ends (BlackjackGame.HandEnded). Random chance: +3 / +2 / 0 / -1 levels by the
///   catalog's odds, never below Basic; decided at once and applied when the roll lands (or when build mode closes).
///   A save never gives an unsettled stake back. No gamble starts while HeatMeter.CanGamble says the board froze
///   gambling (paying still works), and each settled one is reported to GambleLedger (source "furniture").
/// - Capacity is a HARD limit on hiring (owner, 2026-09-29): this provides OfficeCapacity's number (the rooms' capacity)
///   and calls OfficeCapacity.NotifyChanged whenever capacity or headcount changes. Selling a room whose places are in use
///   is refused. An older save that is over capacity is grandfathered (nobody is fired; hiring waits).
/// - Upkeep is a daily company-money cost: this is the IDailyEconomyHook "upkeep" (DayBreakdown.recurring): the campus's
///   upkeep after savings, minus the accounting rooms' salary savings. Knowledge rooms add skill-point progress each day
///   (SkillTreeManager.AddKnowledge).
/// - Its messages come from the "Tower" String Table (TowerText); TowerPlan, TowerRules and TowerSave stay English data.
/// </summary>
[DisallowMultipleComponent]
public class TowerBuilder : MonoBehaviour, ISaveParticipant, IDailyEconomyHook
{
    public static TowerBuilder Instance { get; private set; }

    /// <summary>True while build mode is open.</summary>
    public static bool IsBuildModeOpen => Instance != null && Instance.IsOpen;

    [Header("Catalog")]
    [Tooltip("Rooms, buildings, prices and rules (empty = the built-in defaults, TowerRules.CreateDefault)")]
    public TowerCatalog catalog;

    [Header("Controls")]
    [Tooltip("Scrolling per mouse-wheel step (design pixels; a floor is 144)")]
    public float wheelScrollStep = 144f;
    [Tooltip("Scrolling with W/S or the arrow keys (design pixels per second)")]
    public float keyScrollSpeed = 900f;
    [Tooltip("Panning along the campus with A/D or the arrow keys (design pixels per second at zoom 1)")]
    public float keyPanSpeed = 1400f;
    [Tooltip("Zoom change per +/- press or Ctrl + wheel step")]
    public float zoomStep = 1.15f;
    [Tooltip("Two clicks on a room within this time open it")]
    public float doubleClickSeconds = 0.35f;
    [Tooltip("Length of the random chance roll (real time)")]
    public float rollSeconds = 1.2f;
    [Tooltip("Right clicks are ignored this long after something was placed (seconds)")]
    public float rightClickGuard = 0.3f;

    [Header("Capacity")]
    [Tooltip("Toast when a hire fills the last place in the offices (the field kept its old name: it toasted hires without a seat)")]
    public bool toastUnseatedHires = true;

    /// <summary>The build budget changed.</summary>
    public event Action<double> BudgetChanged;
    /// <summary>Rooms, floors, buildings, links or furniture changed (or were loaded).</summary>
    public event Action PlanChanged;
    /// <summary>Tool, selection, slips, sheet or room view changed.</summary>
    public event Action StateChanged;
    /// <summary>Short feedback for the player (text, is an error). The view shows it.</summary>
    public event Action<string, bool> Message;
    /// <summary>Build mode opened (true) or closed (false).</summary>
    public event Action<bool> OpenChanged;
    /// <summary>A furniture upgrade was paid or a furniture gamble settled.</summary>
    public event Action<TowerUpgradeResult> UpgradeSettled;
    /// <summary>Capacity or headcount changed: (employees, capacity). OfficeCapacity.Changed is raised too.</summary>
    public event Action<int, int> CapacityChanged;

    TowerPlan plan;
    TowerRules rules;
    TowerTotals totals;
    int totalsVersion = -1;
    double lastBudget = double.NaN;
    Action exitRequest;
    bool introSeen;
    TowerRoom lastClickRoom;
    float lastClickTime = -10f;
    float lastPlacedTime = -10f;
    CompanyManager hookedCompany;
    bool registeredHooks;
    string requestedSpot;   // a manager decision's build-mode spot, applied once build mode is open
    int notifiedCapacity = -1, notifiedEmployees = -1;
    int capacityVersion = -1, cachedCapacity;
    Vector2 dragFrom;
    bool dragging;

    // Furniture gamble
    TowerGambleKind gamble;
    double pendingStake;
    bool gambleDiscounted;   // the stake was a discounted price (reported to the manager's perks once it is kept)
    TowerRoom gambleRoom;
    int gambleSlot;
    int gambleLevelBefore;
    double gambleValueBefore;
    BlackjackGame handTable;
    bool handSettled;
    int rollOutcome;
    float rollStartedAt;
    float rollEndsAt;
    TowerUpgradeResult lastResult;

    // ------------------------------------------------------------------
    // State the view reads
    // ------------------------------------------------------------------

    public TowerPlan Plan
    {
        get
        {
            EnsurePlan();
            return plan;
        }
    }

    public TowerRules Rules
    {
        get
        {
            EnsurePlan();
            return rules;
        }
    }

    public double Budget => Plan.Budget;
    public bool IsOpen { get; private set; }
    public TowerTool Tool { get; private set; }
    /// <summary>Room type picked in the catalog (Build room), null = none.</summary>
    public TowerRoomDefinition Pick { get; private set; }
    /// <summary>Room being moved.</summary>
    public TowerRoom Carried { get; private set; }
    /// <summary>Room whose card (clipboard) is open.</summary>
    public TowerRoom Selected { get; private set; }
    /// <summary>Room the sell slip is open for.</summary>
    public TowerRoom SellCandidate { get; private set; }
    public bool FloorSlipOpen { get; private set; }
    /// <summary>The building the add-floor slip is for.</summary>
    public TowerBuilding FloorSlipBuilding { get; private set; }
    /// <summary>Room whose inside is shown (room view), null = the campus.</summary>
    public TowerRoom InsideRoom { get; private set; }
    /// <summary>Furniture slot picked in the room view or the Furniture drawer.</summary>
    public int SelectedSlot { get; private set; } = -1;
    public bool UpgradeOpen { get; private set; }
    public TowerRoom UpgradeRoom { get; private set; }
    public int UpgradeSlot { get; private set; } = -1;
    /// <summary>The upgrade sheet shows the last result.</summary>
    public bool ResultOpen { get; private set; }
    public TowerUpgradeResult LastResult => lastResult;
    public bool IntroOpen { get; private set; }
    public TowerRoom HoverRoom { get; private set; }
    /// <summary>The building under the pointer (null in a gap or outside the campus).</summary>
    public TowerBuilding HoverBuilding { get; private set; }
    /// <summary>The pointer is over a building's add-floor slot (SlotBuilding's).</summary>
    public bool HoverSlot { get; private set; }
    public TowerBuilding SlotBuilding { get; private set; }
    public bool GhostVisible { get; private set; }
    public TowerBuilding GhostBuilding { get; private set; }
    public int GhostFloor { get; private set; }
    public int GhostTile { get; private set; }
    public TowerCheck GhostCheck { get; private set; }
    /// <summary>Link type picked in the Campus drawer (placing a skybridge), null = none.</summary>
    public TowerConnectionType PickLink { get; private set; }
    public bool LinkGhostVisible { get; private set; }
    public TowerBuilding LinkGhostLeft { get; private set; }
    public int LinkGhostFloor { get; private set; }
    public TowerCheck LinkGhostCheck { get; private set; }
    /// <summary>The purchase slip's purchase (kind None = closed).</summary>
    public TowerPurchase Purchase { get; private set; }
    public bool PurchaseOpen => Purchase.kind != TowerPurchaseKind.None;
    /// <summary>The building the view is centred on (the HQ at first).</summary>
    public TowerBuilding FocusBuilding { get; private set; }
    /// <summary>Last pointer position over the campus (design pixels).</summary>
    public Vector2 PointerPoint { get; private set; }
    public TowerGambleKind Gamble => gamble;
    /// <summary>Money taken for a furniture gamble that hasn't settled.</summary>
    public double PendingStake => pendingStake;
    /// <summary>0..1 while the random chance roll runs.</summary>
    public float RollProgress => gamble == TowerGambleKind.Roll ? Mathf.Clamp01((Time.unscaledTime - rollStartedAt) / Mathf.Max(0.05f, rollEndsAt - rollStartedAt)) : 0f;
    /// <summary>Goes up whenever rooms, floors, buildings, links or furniture change.</summary>
    public int PlanVersion { get; private set; }
    /// <summary>Goes up whenever anything the view shows changes (tool, selection, hover, ghost, slips, plan).</summary>
    public int StateVersion { get; private set; }
    public bool IntroSeen => introSeen;
    /// <summary>What the last day's upkeep cost and the salary savings saved (the day breakdown's "recurring" parts).</summary>
    public double LastUpkeepPaid { get; private set; }
    public double LastSalarySaved { get; private set; }

    /// <summary>Employees now (0 without a company).</summary>
    public static int Employees => CompanyManager.Instance != null && CompanyManager.Instance.employees != null ? CompanyManager.Instance.employees.Count : 0;

    /// <summary>Employees the campus holds (the hard limit on hiring).</summary>
    public int Capacity
    {
        get
        {
            EnsurePlan();
            if (capacityVersion != plan.Version)
            {
                cachedCapacity = plan.Capacity;
                capacityVersion = plan.Version;
            }
            return cachedCapacity;
        }
    }

    /// <summary>
    /// Places outside the campus that OfficeCapacity counts too (OfficeCapacity.RegisterExtra, e.g. office space the manager
    /// rents); 0 while this campus doesn't provide the limit.
    /// </summary>
    public static int ExtraPlaces => OfficeCapacity.IsLimited ? OfficeCapacity.ExtraCapacity : 0;

    /// <summary>Employees the company can hold: the campus plus ExtraPlaces (what the HUD's counter shows).</summary>
    public int TotalCapacity => (int)Math.Min(int.MaxValue - 1, (long)Capacity + ExtraPlaces);

    /// <summary>No free place for another hire.</summary>
    public bool IsFull => Employees >= TotalCapacity;

    /// <summary>Employees the campus's own places must hold (the extras hold the rest).</summary>
    static int EmployeesInCampus => Math.Max(0, Employees - ExtraPlaces);

    TowerView View => TowerView.Instance;

    // ------------------------------------------------------------------
    // Lifecycle
    // ------------------------------------------------------------------

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[TowerBuilder] Removed a second TowerBuilder on '" + name + "' (only one may save under 'building').");
            Destroy(this);
            return;
        }
        Instance = this;
        EnsurePlan();
    }

    void OnEnable()
    {
        if (Instance != this) return;
        RegisterHooks();
    }

    void OnDisable()
    {
        // The roll's outcome is already decided: apply it rather than lose the stake
        if (gamble == TowerGambleKind.Roll) SettleRoll();
        if (Instance == this) UnregisterHooks();
    }

    void OnDestroy()
    {
        if (Instance != this) return;
        Instance = null;
        if (gamble == TowerGambleKind.Roll) SettleRoll();
        if (handTable != null) handTable.HandEnded -= OnHandEnded;
        handTable = null;
        UnhookCompany();
        UnregisterHooks();
        if (UIManager.Exists) UIManager.Instance.ClosePanel(this);
        if (plan != null) plan.Changed -= OnPlanChanged;
    }

    /// <summary>Provide OfficeCapacity's number, pay the upkeep every day and hear about fires.</summary>
    void RegisterHooks()
    {
        if (registeredHooks) return;
        registeredHooks = true;
        OfficeCapacity.SetProvider(CapacityForHiring);
        DailyEconomyHooks.Register(this);
        StaffLedger.EmployeeFired -= OnEmployeeFired;
        StaffLedger.EmployeeFired += OnEmployeeFired;
        // The manager's build perks: discounts (prices refresh when they change) and build-mode spots
        ManagerBuildPerks.Changed -= OnPerksChanged;
        ManagerBuildPerks.Changed += OnPerksChanged;
        ManagerBuildPerks.BuildModeRequested -= OnBuildModeRequested;
        ManagerBuildPerks.BuildModeRequested += OnBuildModeRequested;
    }

    void UnregisterHooks()
    {
        if (!registeredHooks) return;
        registeredHooks = false;
        OfficeCapacity.SetProvider(null);
        DailyEconomyHooks.Unregister(this);
        StaffLedger.EmployeeFired -= OnEmployeeFired;
        ManagerBuildPerks.Changed -= OnPerksChanged;
        ManagerBuildPerks.BuildModeRequested -= OnBuildModeRequested;
    }

    /// <summary>The manager's perks changed (granted, used up, expired, loaded): every shown price follows at once.</summary>
    void OnPerksChanged()
    {
        if (plan != null) plan.PricesChanged();
    }

    /// <summary>
    /// A manager decision opened build mode with a spot hint (ManagerBuildPerks.BuildModeRequested, raised right after
    /// BuildModeCamera.TryEnterBuildMode): "expand" opens the add-floor slip (the HQ, or the first building that can still
    /// grow; at the top it says why), "new_building" the Campus page (its cards open the purchase slip) with the view on the last building, where a
    /// new one goes, and "office" the room catalog on its Offices filter.
    /// </summary>
    void OnBuildModeRequested(string spot)
    {
        requestedSpot = spot ?? "";
        if (IsOpen) ApplyRequestedSpot();
    }

    void ApplyRequestedSpot()
    {
        string spot = requestedSpot;
        requestedSpot = null;
        if (string.IsNullOrEmpty(spot) || gamble != TowerGambleKind.None) return;
        IntroOpen = false;   // the decision says where to look (the first-visit note comes back next time until it is dismissed)
        switch (spot)
        {
            case "expand":
                TowerBuilding grow = plan.Headquarters;
                if (grow.IsTopFloorReached)
                {
                    foreach (TowerBuilding building in plan.Buildings)
                        if (!building.IsTopFloorReached) { grow = building; break; }
                }
                FocusOn(grow, true);
                SetTool(TowerTool.AddFloor);   // the slip for the building in view, scrolled to its roof
                break;
            case "new_building":
                SetTool(TowerTool.Campus);
                FocusOn(plan.BuildingAt(plan.BuildingCount - 1), true);
                break;
            case "office":
                SetTool(TowerTool.BuildRoom);
                if (View != null) View.ShowCatalogFilter(1);
                break;
            default:
                Changed();
                break;
        }
    }

    int CapacityForHiring()
    {
        return Capacity;
    }

    void EnsurePlan()
    {
        if (plan != null) return;
        rules = LoadRules();
        plan = new TowerPlan(rules)
        {
            UnlockCheck = IsUnlocked, ProgressProvider = CurrentProgress,
            // The manager's build-budget discounts: every price is shown and charged through them; a discounted purchase counts a use
            PriceMultiplier = ManagerBuildPerks.PriceMultiplier, DiscountUsed = ManagerBuildPerks.NotifyPurchase
        };
        // Demo: the Board's one-time construction grant (a loaded save replaces it with its own budget: TowerSave.Restore)
        if (DemoMode.Active) plan.SetBudget(DemoMode.Config.startingBuildBudget);
        plan.Changed += OnPlanChanged;
        lastBudget = plan.Budget;
        FocusBuilding = plan.Headquarters;
    }

    /// <summary>A copy of the catalog's rules, with the demo's prices and requirements while demo mode is on.</summary>
    TowerRules LoadRules()
    {
        TowerRules loaded = LoadCatalogRules();
        if (DemoMode.Active) DemoTowerRules.ApplyTo(loaded);   // Demo: before TowerPlan sanitizes them
        return loaded;
    }

    /// <summary>A copy of the catalog's rules (TowerPlan fixes invalid values, which must not change the asset).</summary>
    TowerRules LoadCatalogRules()
    {
        TowerRules source = catalog != null ? catalog.rules : null;
        if (source == null || source.rooms == null || source.rooms.Count == 0)
        {
            if (catalog != null) Debug.LogWarning("[TowerBuilder] Catalog '" + catalog.name + "' has no rooms; using the default rules.");
            return TowerRules.CreateDefault();
        }
        TowerRules copy = JsonUtility.FromJson<TowerRules>(JsonUtility.ToJson(source));
        return copy ?? TowerRules.CreateDefault();
    }

    /// <summary>Rooms that need a skill: Stock Market Access (trading floor) and the Casino License. No skill tree = unlocked.</summary>
    static bool IsUnlocked(TowerUnlock unlock)
    {
        SkillTreeManager skills = SkillTreeManager.Instance;
        if (skills == null) return true;
        switch (unlock)
        {
            case TowerUnlock.StockMarket: return skills.HasEffect(SkillEffect.UnlockStockMarket);
            case TowerUnlock.CasinoLicense: return skills.IsCasinoUnlocked();   // Demo: follows the open casino (playtest and demo too)
            default: return true;
        }
    }

    /// <summary>The run's progress for requirements: the highest value reached, the status tier (highest reached) and the day.</summary>
    static TowerProgress CurrentProgress()
    {
        CompanyManager company = CompanyManager.Instance;
        double peak = company != null ? company.companyValue : 0.0;
        RunStats stats = RunStats.Instance;
        if (stats != null) peak = Math.Max(peak, stats.PeakValue);
        int tier = StatusLadder.TierForValue(peak);
        StatusLadder ladder = StatusLadder.Instance;
        if (ladder != null && ladder.IsTierKnown) tier = Math.Max(tier, ladder.CurrentTier);
        return new TowerProgress { peakValue = peak, statusTier = tier, day = company != null ? company.currentDay : 1 };
    }

    public bool IsRoomUnlocked(TowerRoomDefinition definition)
    {
        return Plan.IsUnlocked(definition);
    }

    void OnPlanChanged()
    {
        PlanVersion++;
        StateVersion++;
        if (Carried != null && !plan.Contains(Carried)) Carried = null;
        if (Selected != null && !plan.Contains(Selected)) Selected = null;
        if (SellCandidate != null && !plan.Contains(SellCandidate)) SellCandidate = null;
        if (InsideRoom != null && !plan.Contains(InsideRoom)) InsideRoom = null;
        if (HoverRoom != null && !plan.Contains(HoverRoom)) HoverRoom = null;
        if (FocusBuilding == null || !plan.Contains(FocusBuilding)) FocusBuilding = plan.Headquarters;
        if (FloorSlipBuilding != null && !plan.Contains(FloorSlipBuilding)) { FloorSlipBuilding = null; FloorSlipOpen = false; }
        if (HoverBuilding != null && !plan.Contains(HoverBuilding)) HoverBuilding = null;
        if (Purchase.kind == TowerPurchaseKind.RoomLevel && !plan.Contains(Purchase.room)) Purchase = default;
        if (Purchase.kind == TowerPurchaseKind.Connection && !plan.Contains(Purchase.left)) Purchase = default;
        if (UpgradeRoom != null && !plan.Contains(UpgradeRoom) && gamble == TowerGambleKind.None)
        {
            UpgradeRoom = null;
            UpgradeOpen = false;
            ResultOpen = false;
        }
        PlanChanged?.Invoke();
        if (plan.Budget != lastBudget)
        {
            lastBudget = plan.Budget;
            BudgetChanged?.Invoke(plan.Budget);
        }
        NotifyCapacityIfChanged();
    }

    void Changed()
    {
        StateVersion++;
        StateChanged?.Invoke();
    }

    /// <summary>Tell OfficeCapacity (the HUD counter, the hiring desk, the tutorial) when capacity or headcount changed.</summary>
    void NotifyCapacityIfChanged()
    {
        int capacity = TotalCapacity, employees = Employees;
        if (capacity == notifiedCapacity && employees == notifiedEmployees) return;
        notifiedCapacity = capacity;
        notifiedEmployees = employees;
        OfficeCapacity.NotifyChanged();
        try { CapacityChanged?.Invoke(employees, capacity); }
        catch (Exception e) { Debug.LogException(e); }
    }

    // ------------------------------------------------------------------
    // Opening
    // ------------------------------------------------------------------

    /// <summary>Open build mode (BuildModeCamera calls this). <paramref name="requestExit"/> leaves build mode (Escape, Back to office).</summary>
    public void Open(Action requestExit = null)
    {
        EnsurePlan();
        exitRequest = requestExit;
        if (IsOpen) return;

        IsOpen = true;
        ResetTransientState();
        Tool = TowerTool.None;
        IntroOpen = !introSeen && plan.Floors == 1 && plan.BuiltRoomCount == 0 && plan.BuildingCount == 1;
        Register();

        TowerView view = View;
        if (view != null)
            view.Show();
        else
            Debug.LogWarning("[TowerBuilder] No TowerView in the scene, so build mode has no screen.");
        if (view != null && FocusBuilding != null) view.FocusBuilding(FocusBuilding, true);

        Changed();
        OpenChanged?.Invoke(true);
        if (requestedSpot != null) ApplyRequestedSpot();
    }

    /// <summary>Close build mode (BuildModeCamera calls this). A move is put back; a running roll settles now.</summary>
    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        if (gamble == TowerGambleKind.Roll) SettleRoll();
        ResetTransientState();

        BuildGambleView budgetGamble = BuildGambleView.Instance;
        if (budgetGamble != null) budgetGamble.ForceClose();

        TowerView view = View;
        if (view != null) view.Hide();
        if (UIManager.Exists) UIManager.Instance.ClosePanel(this);

        Changed();
        OpenChanged?.Invoke(false);
    }

    /// <summary>Leave build mode the normal way (Back to office, Escape).</summary>
    public void RequestExit()
    {
        if (exitRequest != null)
            exitRequest.Invoke();
        else
            Close();
    }

    void Register()
    {
        // Pausing: the days (income, growth), crises and risk cards wait while building. Build mode runs on unscaled time.
        UIManager ui = UIManager.Instance;
        if (ui != null) ui.OpenPanel(this, true, HandleEscape);
    }

    void ResetTransientState()
    {
        Pick = null;
        PickLink = null;
        Carried = null;
        Selected = null;
        SellCandidate = null;
        FloorSlipOpen = false;
        FloorSlipBuilding = null;
        InsideRoom = null;
        SelectedSlot = -1;
        Purchase = default;
        if (gamble == TowerGambleKind.None)
        {
            UpgradeOpen = false;
            ResultOpen = false;
            UpgradeRoom = null;
            UpgradeSlot = -1;
        }
        IntroOpen = false;
        HoverRoom = null;
        HoverBuilding = null;
        HoverSlot = false;
        SlotBuilding = null;
        GhostVisible = false;
        LinkGhostVisible = false;
        dragging = false;
    }

    void HandleEscape()
    {
        if (!CancelCurrentAction()) RequestExit();
    }

    /// <summary>One Escape step. Returns false when there was nothing left to cancel (then Escape leaves build mode).</summary>
    public bool CancelCurrentAction()
    {
        if (gamble != TowerGambleKind.None) return true;   // a hand or a roll is running
        if (ResultOpen) { CloseResult(); return true; }
        if (UpgradeOpen) { CloseUpgrade(); return true; }
        if (PurchaseOpen) { CancelPurchase(); return true; }
        if (SellCandidate != null) { CancelSell(); return true; }
        if (FloorSlipOpen) { CancelFloorSlip(); return true; }
        if (IntroOpen) { DismissIntro(); return true; }
        if (InsideRoom != null) { CloseRoom(); return true; }
        if (Carried != null) { CancelMove(); return true; }
        if (Pick != null) { PickRoom(null); return true; }
        if (PickLink != null) { PickConnection(null); return true; }
        if (Selected != null) { Deselect(); return true; }
        if (Tool != TowerTool.None) { SetTool(TowerTool.None); return true; }
        return false;
    }

    // ------------------------------------------------------------------
    // Input
    // ------------------------------------------------------------------

    /// <summary>No typing, no other panel (the budget gamble counts), no mini-game or risk event.</summary>
    bool InputAllowed()
    {
        if (UIManager.IsTypingInInputField()) return false;
        if (UIManager.Exists && UIManager.Instance.IsAnyPanelOpen(this)) return false;
        if (MiniGameManager.Instance != null && MiniGameManager.Instance.IsMiniGameActive()) return false;
        if (EventManager.Instance != null && EventManager.Instance.IsEventInProgress()) return false;
        return true;
    }

    void Update()
    {
        HookCompany();
        NotifyCapacityIfChanged();   // headcount can change from anywhere (hires, fires, debug tools, a load)
        if (!IsOpen) return;

        // UI state was reset (debug tools) while build mode stayed open: register again
        if (UIManager.Exists && !UIManager.Instance.IsPanelOpen(this)) Register();
        if (gamble == TowerGambleKind.Roll && Time.unscaledTime >= rollEndsAt) SettleRoll();

        TowerView view = View;
        if (view == null || !view.IsShowing) return;
        if (!InputAllowed())
        {
            ClearHover();
            dragging = false;
            return;
        }

        bool modal = UpgradeOpen || ResultOpen || IntroOpen || PurchaseOpen || gamble != TowerGambleKind.None;
        if (!modal && InsideRoom == null)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) ToggleTool(TowerTool.BuildRoom);
            else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) ToggleTool(TowerTool.AddFloor);
            else if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)) ToggleTool(TowerTool.Furniture);
            else if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4)) ToggleTool(TowerTool.Move);
            else if (Input.GetKeyDown(KeyCode.Alpha5) || Input.GetKeyDown(KeyCode.Keypad5)) ToggleTool(TowerTool.Sell);
            else if (Input.GetKeyDown(KeyCode.Alpha6) || Input.GetKeyDown(KeyCode.Keypad6)) ToggleTool(TowerTool.Campus);
            else if (Input.GetKeyDown(KeyCode.Tab))
                FocusNext(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? -1 : 1);
            else if (Input.GetKeyDown(KeyCode.Home)) FocusOn(plan.Headquarters);
        }
        if (!modal && InsideRoom != null && Input.GetKeyDown(KeyCode.E))
            OpenUpgrade(InsideRoom, SelectedSlot);

        TowerView.PointerTarget target = view.GetPointerTarget();
        if (modal || InsideRoom != null)
        {
            ClearHover();
            dragging = false;
            return;
        }

        HandleScroll(view, target);
        Vector2 point = default;
        bool overTower = target == TowerView.PointerTarget.Tower && view.TryGetTowerPoint(Input.mousePosition, out point);
        UpdateHover(overTower, point);

        if (Input.GetMouseButtonDown(1))
            OnRightClick();
        else if (Input.GetMouseButtonDown(0) && overTower)
            OnLeftClick();
    }

    void HandleScroll(TowerView view, TowerView.PointerTarget target)
    {
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) || Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);
        float wheel = Input.mouseScrollDelta.y;
        if (Mathf.Abs(wheel) > 0.01f && target != TowerView.PointerTarget.Ui)
        {
            if (ctrl) view.ZoomBy(Mathf.Pow(Mathf.Max(1.01f, zoomStep), wheel), Input.mousePosition);
            else if (shift) view.PanBy(-wheel * Mathf.Max(1f, wheelScrollStep) * 1.5f);
            else view.ScrollBy(wheel * Mathf.Max(1f, wheelScrollStep));
        }

        float direction = 0f;
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) direction += 1f;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) direction -= 1f;
        if (direction != 0f) view.ScrollBy(direction * keyScrollSpeed * Time.unscaledDeltaTime, true);
        float pan = 0f;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) pan += 1f;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) pan -= 1f;
        if (pan != 0f) view.PanBy(pan * keyPanSpeed * Time.unscaledDeltaTime / Mathf.Max(0.1f, view.Zoom), true);
        if (Input.GetKeyDown(KeyCode.PageUp)) view.ScrollBy(TowerGeo.FloorHeight * 3f);
        if (Input.GetKeyDown(KeyCode.PageDown)) view.ScrollBy(-TowerGeo.FloorHeight * 3f);
        if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.Plus) || Input.GetKeyDown(KeyCode.KeypadPlus)) view.ZoomBy(Mathf.Max(1.01f, zoomStep));
        if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus)) view.ZoomBy(1f / Mathf.Max(1.01f, zoomStep));

        // Middle mouse drag: pan along the campus and scroll up and down
        if (Input.GetMouseButtonDown(2) && target != TowerView.PointerTarget.Ui)
        {
            dragging = true;
            dragFrom = Input.mousePosition;
        }
        if (dragging)
        {
            if (!Input.GetMouseButton(2))
            {
                dragging = false;
            }
            else
            {
                Vector2 now = Input.mousePosition;
                Vector2 delta = view.ScreenToCanvasDelta(now - dragFrom);
                dragFrom = now;
                if (delta.sqrMagnitude > 0f)
                {
                    view.PanBy(-delta.x / Mathf.Max(0.1f, view.Zoom), true);
                    view.ScrollBy(-delta.y, true);
                }
            }
        }
    }

    void ClearHover()
    {
        if (HoverRoom == null && !HoverSlot && !GhostVisible && !LinkGhostVisible && HoverBuilding == null) return;
        HoverRoom = null;
        HoverBuilding = null;
        HoverSlot = false;
        SlotBuilding = null;
        GhostVisible = false;
        LinkGhostVisible = false;
        StateVersion++;
    }

    static bool SameCheck(TowerCheck a, TowerCheck b)
    {
        return a.issue == b.issue && a.price == b.price && a.blocker == b.blocker && a.tilesLeft == b.tilesLeft && a.limit == b.limit
               && a.floorRule == b.floorRule && a.lockInfo.kind == b.lockInfo.kind;
    }

    void UpdateHover(bool overTower, Vector2 p)
    {
        if (!overTower)
        {
            ClearHover();
            return;
        }
        PointerPoint = p;
        TowerBuilding building = TowerGeo.BuildingAt(plan, p.x);
        float localX = building != null ? p.x - TowerGeo.OriginX(plan, building) : 0f;
        int floor = TowerGeo.FloorAt(p.y);
        int tile = TowerGeo.TileAt(localX);
        bool inside = building != null && localX >= TowerGeo.X0 && localX < TowerGeo.Right(building.TilesPerFloor);

        TowerRoom room = null;
        if (inside && floor >= 1 && floor <= building.Floors && p.y >= TowerGeo.RoomTop(floor))
            room = plan.RoomAt(building, floor, tile, Carried);
        bool slot = inside && !building.IsTopFloorReached && floor == building.Floors + 1 && Carried == null && Pick == null && PickLink == null;

        TowerRoomDefinition definition = Carried != null ? Carried.Definition : Pick;
        bool ghost = definition != null && building != null && floor >= 1 && floor <= building.Floors;
        int ghostTile = GhostTile;
        TowerCheck check = GhostCheck;
        if (ghost)
        {
            ghostTile = Mathf.RoundToInt((localX - TowerGeo.X0) / TowerGeo.Tile - definition.Tiles * 0.5f);
            check = Carried != null ? plan.CheckMove(Carried, building, floor, ghostTile) : plan.CheckPlace(definition, building, floor, ghostTile);
        }

        // A skybridge being placed: the gap under the pointer and its floor
        bool link = false;
        TowerBuilding linkLeft = LinkGhostLeft;
        TowerCheck linkCheck = LinkGhostCheck;
        if (PickLink != null)
        {
            linkLeft = TowerGeo.GapLeftOf(plan, p.x);
            link = linkLeft != null && floor >= 1;
            if (link) linkCheck = plan.CheckConnection(PickLink, linkLeft, floor);
        }

        bool changed = room != HoverRoom || slot != HoverSlot || ghost != GhostVisible || building != HoverBuilding || link != LinkGhostVisible
                       || (slot && building != SlotBuilding);
        if (ghost && (building != GhostBuilding || floor != GhostFloor || ghostTile != GhostTile || !SameCheck(check, GhostCheck)))
            changed = true;
        if (link && (linkLeft != LinkGhostLeft || floor != LinkGhostFloor || !SameCheck(linkCheck, LinkGhostCheck)))
            changed = true;

        HoverRoom = room;
        HoverBuilding = building;
        HoverSlot = slot;
        SlotBuilding = slot ? building : null;
        GhostVisible = ghost;
        if (ghost)
        {
            GhostBuilding = building;
            GhostFloor = floor;
            GhostTile = ghostTile;
            GhostCheck = check;
        }
        LinkGhostVisible = link;
        if (link)
        {
            LinkGhostLeft = linkLeft;
            LinkGhostFloor = floor;
            LinkGhostCheck = linkCheck;
        }
        if (changed) StateVersion++;
    }

    void OnLeftClick()
    {
        if (SellCandidate != null)
        {
            CancelSell();   // a click outside the slip closes it
            return;
        }
        if (FloorSlipOpen)
        {
            // The slip is the confirmation: a click on its slot keeps it open, anywhere else closes it
            if (!HoverSlot || SlotBuilding != FloorSlipBuilding) CancelFloorSlip();
            return;
        }
        if (Carried != null)
        {
            TryDrop();
            return;
        }
        if (Pick != null)
        {
            TryPlace();
            return;
        }
        if (PickLink != null)
        {
            TryPlaceLink();
            return;
        }
        if (HoverSlot)
        {
            OpenFloorSlip(SlotBuilding);
            return;
        }

        TowerRoom room = HoverRoom;
        if (room == null)
        {
            if (Selected != null) Deselect();
            else if (HoverBuilding != null && HoverBuilding != FocusBuilding) FocusOn(HoverBuilding);   // click a building to look at it
            return;
        }
        switch (Tool)
        {
            case TowerTool.Move:
                StartMove(room);
                break;
            case TowerTool.Sell:
                AskSell(room);
                break;
            default:
                bool twice = room == lastClickRoom && Time.unscaledTime - lastClickTime < doubleClickSeconds;
                lastClickRoom = room;
                lastClickTime = Time.unscaledTime;
                if (Tool == TowerTool.BuildRoom || Tool == TowerTool.AddFloor || Tool == TowerTool.Campus) SetTool(TowerTool.None);
                if (twice) OpenRoom(room);
                else Select(room);
                break;
        }
    }

    void OnRightClick()
    {
        if (Time.unscaledTime - lastPlacedTime < rightClickGuard) return;
        if (Carried != null) CancelMove();
        else if (Pick != null) PickRoom(null);
        else if (PickLink != null) PickConnection(null);
        else if (SellCandidate != null) CancelSell();
        else if (FloorSlipOpen) CancelFloorSlip();
    }

    // ------------------------------------------------------------------
    // Tools and selection (also used by the view's buttons)
    // ------------------------------------------------------------------

    /// <summary>Switch tools (the key of the active tool again, or None, switches it off).</summary>
    public void SetTool(TowerTool tool)
    {
        if (gamble != TowerGambleKind.None) return;
        if (tool == TowerTool.Campus && DemoTowerRules.LocksCampus)
        {
            // Demo: the tool button, key 6 and the manager's "new_building" spot all land here
            Say(DemoTowerRules.CampusLockedText, true);
            return;
        }
        if (InsideRoom != null) InsideRoom = null;
        UpgradeOpen = false;
        ResultOpen = false;
        Pick = null;
        PickLink = null;
        Carried = null;
        SellCandidate = null;
        FloorSlipOpen = false;
        IntroOpen = false;
        Purchase = default;
        TowerRoom selected = Selected;
        Tool = tool;

        switch (tool)
        {
            case TowerTool.BuildRoom:
            case TowerTool.Campus:
                Selected = null;
                break;
            case TowerTool.AddFloor:
                TowerBuilding building = FocusBuilding ?? plan.Headquarters;
                if (building.IsTopFloorReached)
                {
                    // No slot above the roof any more, so there is no slip to show: say why instead
                    Tool = TowerTool.None;
                    Say(Describe(plan.CheckAddFloor(building), null), true);
                    break;
                }
                FloorSlipOpen = true;
                FloorSlipBuilding = building;
                if (View != null) View.ScrollToRoof(building);
                break;
            case TowerTool.Move:
                if (selected != null && !selected.IsFixed)
                {
                    StartMove(selected);
                    return;
                }
                break;
            case TowerTool.Sell:
                if (selected != null && !selected.IsFixed)
                {
                    AskSell(selected);
                    return;
                }
                break;
        }
        Changed();
    }

    public void ToggleTool(TowerTool tool)
    {
        GameSfx.Play(Sfx.UiToggle);   // keys 1-6 and the tool buttons
        SetTool(Tool == tool ? TowerTool.None : tool);
    }

    /// <summary>Pick a room type in the catalog (the same one again, or null, puts it down).</summary>
    public void PickRoom(TowerRoomDefinition definition)
    {
        if (definition != null && definition == Pick) definition = null;
        if (definition != null)
        {
            TowerLock lockInfo = plan.LockOf(definition);
            if (lockInfo.IsLocked)
            {
                Say(TowerText.LockText(lockInfo, definition, rules), true);
                return;
            }
            if (definition.maxPerCompany > 0 && plan.CountRooms(definition) >= definition.maxPerCompany)
            {
                Say(Describe(new TowerCheck { issue = TowerIssue.LimitReached, limit = definition.maxPerCompany }, definition), true);
                return;
            }
        }
        Carried = null;
        SellCandidate = null;
        FloorSlipOpen = false;
        PickLink = null;
        Purchase = default;
        Pick = definition;
        GhostVisible = false;
        if (definition != null) Tool = TowerTool.BuildRoom;
        Changed();
    }

    void TryPlace()
    {
        TowerRoomDefinition definition = Pick;
        if (definition == null || !GhostVisible || GhostBuilding == null) return;
        TowerRoom room = plan.Place(definition, GhostBuilding, GhostFloor, GhostTile, out TowerCheck check);
        if (room == null)
        {
            Say(Describe(check, definition), true);
            return;
        }
        lastPlacedTime = Time.unscaledTime;
        GameSfx.Play(Sfx.UiDrop);
        Say(plan.BuildingCount > 1
                ? TowerText.Get("tower.message.built.campus", ("room", TowerText.RoomName(definition)), ("building", TowerText.BuildingName(room.Building)),
                                ("floor", room.Floor), ("price", UIFormat.Money(check.price)))
                : TowerText.Get("tower.message.built", ("room", TowerText.RoomName(definition)), ("floor", room.Floor), ("price", UIFormat.Money(check.price))),
            false);
        // Shift+click keeps building the same room
        if (!(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
        {
            Pick = null;
            GhostVisible = false;
        }
        Changed();
    }

    public void Select(TowerRoom room)
    {
        if (room != null && !plan.Contains(room)) return;
        SellCandidate = null;
        Selected = room;
        if (room != null && (SelectedSlot < 0 || SelectedSlot >= room.SlotCount)) SelectedSlot = FirstSlot(room);
        Changed();
    }

    public void Deselect()
    {
        if (Selected == null && SellCandidate == null) return;
        Selected = null;
        SellCandidate = null;
        Changed();
    }

    /// <summary>Open the inside of a room (its furniture).</summary>
    public void OpenRoom(TowerRoom room)
    {
        if (room == null || !plan.Contains(room) || gamble != TowerGambleKind.None) return;
        Pick = null;
        PickLink = null;
        Carried = null;
        SellCandidate = null;
        FloorSlipOpen = false;
        IntroOpen = false;
        Purchase = default;
        if (Tool != TowerTool.Furniture) Tool = TowerTool.None;
        Selected = room;
        InsideRoom = room;
        SelectedSlot = FirstSlot(room);
        GameSfx.Play(Sfx.UiOpen);   // inside the room
        Changed();
    }

    public void CloseRoom()
    {
        if (InsideRoom == null || gamble != TowerGambleKind.None) return;
        GameSfx.Play(Sfx.UiClose);
        InsideRoom = null;
        UpgradeOpen = false;
        ResultOpen = false;
        Changed();
    }

    public void SelectSlot(int slot)
    {
        TowerRoom room = InsideRoom ?? Selected;
        if (room == null || slot < 0 || slot >= room.SlotCount || slot == SelectedSlot) return;
        SelectedSlot = slot;
        Changed();
    }

    /// <summary>The slot furthest left in the room view.</summary>
    public static int FirstSlot(TowerRoom room)
    {
        if (room == null || room.SlotCount == 0) return -1;
        int best = 0;
        for (int i = 1; i < room.SlotCount; i++)
        {
            TowerFurnitureSlot a = room.Definition.GetSlot(i), b = room.Definition.GetSlot(best);
            if (a != null && b != null && a.viewOrder < b.viewOrder) best = i;
        }
        return best;
    }

    // ------------------------------------------------------------------
    // The campus: where the view looks
    // ------------------------------------------------------------------

    /// <summary>Centre the view on a building (the campus bar, Tab, a click on it, a new building).</summary>
    public void FocusOn(TowerBuilding building, bool instant = false)
    {
        if (building == null || !plan.Contains(building)) return;
        FocusBuilding = building;
        TowerView view = View;
        if (view != null) view.FocusBuilding(building, instant);
        Changed();
    }

    /// <summary>The next (+1) or previous (-1) building, wrapping around.</summary>
    public void FocusNext(int direction)
    {
        int count = plan.BuildingCount;
        if (count <= 1)
        {
            FocusOn(plan.Headquarters);
            return;
        }
        int slot = FocusBuilding != null ? FocusBuilding.Slot : 0;
        slot = ((slot + direction) % count + count) % count;
        FocusOn(plan.BuildingAt(slot));
    }

    /// <summary>Zoom out to the whole campus, or back in to the building in focus.</summary>
    public void ToggleOverview()
    {
        TowerView view = View;
        if (view != null) view.ToggleOverview(FocusBuilding);
    }

    // ------------------------------------------------------------------
    // Moving, selling, floors
    // ------------------------------------------------------------------

    public void StartMove(TowerRoom room)
    {
        if (room == null || !plan.Contains(room) || gamble != TowerGambleKind.None) return;
        if (room.IsFixed)
        {
            Say(TowerText.Get("tower.message.stays", ("room", TowerText.RoomName(room))), true);
            return;
        }
        InsideRoom = null;
        SellCandidate = null;
        FloorSlipOpen = false;
        Pick = null;
        PickLink = null;
        Purchase = default;
        Selected = null;
        Tool = TowerTool.Move;
        Carried = room;
        GhostVisible = false;
        Changed();
    }

    public void CancelMove()
    {
        if (Carried == null) return;
        Carried = null;
        GhostVisible = false;
        Changed();
    }

    void TryDrop()
    {
        TowerRoom room = Carried;
        if (room == null || !GhostVisible || GhostBuilding == null) return;
        if (!plan.Move(room, GhostBuilding, GhostFloor, GhostTile, out TowerCheck check))
        {
            Say(Describe(check, room.Definition), true);
            return;
        }
        lastPlacedTime = Time.unscaledTime;
        Carried = null;
        GhostVisible = false;
        Tool = TowerTool.None;
        Selected = room;
        GameSfx.Play(Sfx.UiDrop);
        Say(plan.BuildingCount > 1
                ? TowerText.Get("tower.message.moved.campus", ("room", TowerText.RoomName(room)), ("building", TowerText.BuildingName(room.Building)),
                                ("floor", room.Floor))
                : TowerText.Get("tower.message.moved", ("room", TowerText.RoomName(room)), ("floor", room.Floor)),
            false);
        Changed();
    }

    /// <summary>Open the sell slip for a room (built-in rooms can't be sold; a room whose places are in use can't either).</summary>
    public void AskSell(TowerRoom room)
    {
        if (room == null || !plan.Contains(room) || gamble != TowerGambleKind.None) return;
        if (room.IsFixed)
        {
            Say(TowerText.Get(plan.StarterRoom != null ? "tower.message.cant_sell.built_in" : "tower.message.cant_sell"), true);
            return;
        }
        TowerCheck check = plan.CheckSell(room, EmployeesInCampus);
        if (!check.Ok)
        {
            Say(Describe(check, room.Definition), true);
            return;
        }
        InsideRoom = null;
        Carried = null;
        Pick = null;
        PickLink = null;
        FloorSlipOpen = false;
        Purchase = default;
        Tool = TowerTool.Sell;
        Selected = room;
        SellCandidate = room;
        Changed();
    }

    public void ConfirmSell()
    {
        TowerRoom room = SellCandidate;
        SellCandidate = null;
        if (room == null || !plan.Contains(room) || room.IsFixed)
        {
            Changed();
            return;
        }
        // Someone may have been hired while the slip was open
        TowerCheck check = plan.CheckSell(room, EmployeesInCampus);
        if (!check.Ok)
        {
            Say(Describe(check, room.Definition), true);
            Changed();
            return;
        }
        string roomName = TowerText.RoomName(room);
        if (Selected == room) Selected = null;
        double refund = plan.Sell(room);
        GameSfx.Play(Sfx.ChipsCollect);
        Say(TowerText.Get("tower.message.sold", ("room", roomName), ("refund", UIFormat.SignedMoney(refund))), false);
        Changed();
    }

    public void CancelSell()
    {
        if (SellCandidate == null) return;
        SellCandidate = null;
        Changed();
    }

    /// <summary>Open the add-floor slip for the building in view.</summary>
    public void OpenFloorSlip()
    {
        OpenFloorSlip(FocusBuilding ?? plan.Headquarters);
    }

    /// <summary>Open the add-floor slip for <paramref name="building"/>.</summary>
    public void OpenFloorSlip(TowerBuilding building)
    {
        if (gamble != TowerGambleKind.None) return;
        if (building == null || !plan.Contains(building)) building = plan.Headquarters;
        if (building.IsTopFloorReached)
        {
            Say(Describe(plan.CheckAddFloor(building), null), true);
            return;
        }
        InsideRoom = null;
        Carried = null;
        Pick = null;
        PickLink = null;
        SellCandidate = null;
        Purchase = default;
        Tool = TowerTool.AddFloor;
        FloorSlipOpen = true;
        FloorSlipBuilding = building;
        Changed();
    }

    public void ConfirmFloor()
    {
        TowerBuilding building = FloorSlipBuilding ?? plan.Headquarters;
        if (!plan.AddFloor(building, out TowerCheck check))
        {
            Say(Describe(check, null), true);
            return;
        }
        GameSfx.Play(Sfx.UiConfirm);
        Say(plan.BuildingCount > 1
                ? TowerText.Get("tower.message.floor_built.campus", ("building", TowerText.BuildingName(building)), ("floor", building.Floors),
                                ("price", UIFormat.Money(check.price)))
                : TowerText.Get("tower.message.floor_built", ("floor", building.Floors), ("price", UIFormat.Money(check.price))),
            false);
        FloorSlipOpen = false;
        Tool = TowerTool.None;
        if (View != null) View.ScrollToRoof(building);
        Changed();
    }

    public void CancelFloorSlip()
    {
        if (!FloorSlipOpen) return;
        FloorSlipOpen = false;
        if (Tool == TowerTool.AddFloor) Tool = TowerTool.None;
        Changed();
    }

    public void DismissIntro()
    {
        if (!IntroOpen && introSeen) return;
        IntroOpen = false;
        introSeen = true;
        Changed();
    }

    // ------------------------------------------------------------------
    // Buildings, skybridges, room levels: the purchase slip
    // ------------------------------------------------------------------

    /// <summary>Open the purchase slip for a new building (a locked one or one at its limit just says why).</summary>
    public void OpenBuyBuilding(TowerBuildingType type)
    {
        if (gamble != TowerGambleKind.None || type == null) return;
        if (DemoTowerRules.LocksCampus)
        {
            Say(DemoTowerRules.CampusLockedText, true);   // Demo: no buildings past the HQ
            return;
        }
        TowerCheck check = plan.CheckBuyBuilding(type);
        if (!check.Ok && check.issue != TowerIssue.CantAfford)
        {
            Say(Describe(check, null), true);
            return;
        }
        OpenPurchase(new TowerPurchase { kind = TowerPurchaseKind.Building, buildingType = type });
    }

    /// <summary>Open the purchase slip for a room's next level.</summary>
    public void OpenRoomUpgrade(TowerRoom room)
    {
        if (gamble != TowerGambleKind.None || room == null || !plan.Contains(room)) return;
        TowerCheck check = plan.CheckRoomUpgrade(room);
        if (!check.Ok && check.issue != TowerIssue.CantAfford)
        {
            Say(Describe(check, room.Definition), true);
            return;
        }
        OpenPurchase(new TowerPurchase { kind = TowerPurchaseKind.RoomLevel, room = room });
    }

    /// <summary>Pick a link type in the Campus drawer (the same one again, or null, puts it down); then click a gap.</summary>
    public void PickConnection(TowerConnectionType type)
    {
        if (type != null && type == PickLink) type = null;
        if (type != null)
        {
            TowerLock lockInfo = plan.LockOf(type);
            if (lockInfo.IsLocked)
            {
                Say(TowerText.LockText(lockInfo, null, rules), true);
                return;
            }
        }
        Pick = null;
        Carried = null;
        SellCandidate = null;
        FloorSlipOpen = false;
        Purchase = default;
        PickLink = type;
        LinkGhostVisible = false;
        if (type != null)
        {
            Tool = TowerTool.Campus;
            Selected = null;
        }
        Changed();
    }

    void TryPlaceLink()
    {
        if (PickLink == null || !LinkGhostVisible || LinkGhostLeft == null) return;
        TowerCheck check = LinkGhostCheck;
        if (!check.Ok && check.issue != TowerIssue.CantAfford)
        {
            Say(Describe(check, null), true);
            return;
        }
        OpenPurchase(new TowerPurchase { kind = TowerPurchaseKind.Connection, connectionType = PickLink, left = LinkGhostLeft, floor = LinkGhostFloor });
    }

    void OpenPurchase(TowerPurchase purchase)
    {
        SellCandidate = null;
        FloorSlipOpen = false;
        IntroOpen = false;
        Carried = null;
        GhostVisible = false;
        Purchase = purchase;
        GameSfx.Play(Sfx.UiOpen);
        Changed();
    }

    /// <summary>The purchase slip's check as things stand now (the budget may have grown from a gamble).</summary>
    public TowerCheck PurchaseCheck => CheckPurchase(Purchase);

    TowerCheck CheckPurchase(TowerPurchase purchase)
    {
        switch (purchase.kind)
        {
            case TowerPurchaseKind.Building: return plan.CheckBuyBuilding(purchase.buildingType);
            case TowerPurchaseKind.Connection: return plan.CheckConnection(purchase.connectionType, purchase.left, purchase.floor);
            case TowerPurchaseKind.RoomLevel: return plan.CheckRoomUpgrade(purchase.room);
            default: return TowerCheck.Fail(TowerIssue.Missing);
        }
    }

    /// <summary>What the purchase slip buys, in the current language ("Office tower", "Skybridge", "Big office · level 3").</summary>
    public string PurchaseName(TowerPurchase purchase)
    {
        switch (purchase.kind)
        {
            case TowerPurchaseKind.Building: return TowerText.BuildingTypeName(purchase.buildingType);
            case TowerPurchaseKind.Connection: return TowerText.ConnectionName(purchase.connectionType);
            case TowerPurchaseKind.RoomLevel:
                return purchase.room != null
                    ? TowerText.Get("tower.purchase.room_level.name", ("room", TowerText.RoomName(purchase.room)), ("level", purchase.room.Level + 1))
                    : "";
            default: return "";
        }
    }

    public void ConfirmPurchase()
    {
        TowerPurchase purchase = Purchase;
        if (purchase.kind == TowerPurchaseKind.None || gamble != TowerGambleKind.None) return;
        TowerCheck check;
        switch (purchase.kind)
        {
            case TowerPurchaseKind.Building:
                TowerBuilding building = plan.BuyBuilding(purchase.buildingType, out check);
                if (building == null)
                {
                    Say(Describe(check, null), true);
                    return;
                }
                Purchase = default;
                PickLink = null;
                Tool = TowerTool.None;
                GameSfx.Play(Sfx.UiConfirm);
                Say(TowerText.Get("tower.message.building_bought", ("building", TowerText.BuildingName(building)), ("price", UIFormat.Money(check.price))), false);
                FocusOn(building);
                break;
            case TowerPurchaseKind.Connection:
                TowerConnection link = plan.BuildConnection(purchase.connectionType, purchase.left, purchase.floor, out check);
                if (link == null)
                {
                    Say(Describe(check, null), true);
                    return;
                }
                Purchase = default;
                PickLink = null;
                Tool = TowerTool.None;
                GameSfx.Play(Sfx.UiConfirm);
                Say(TowerText.Get("tower.message.link_built", ("link", TowerText.ConnectionName(link.Type)), ("left", TowerText.BuildingName(link.Left)),
                                  ("right", TowerText.BuildingName(link.Right)), ("floor", link.Floor), ("price", UIFormat.Money(check.price))), false);
                break;
            case TowerPurchaseKind.RoomLevel:
                TowerRoom room = purchase.room;
                int placesBefore = room != null ? room.Capacity : 0;
                if (!plan.UpgradeRoom(room, out check))
                {
                    Say(Describe(check, room != null ? room.Definition : null), true);
                    return;
                }
                Purchase = default;
                GameSfx.Play(Sfx.UiConfirm);
                int added = room.Capacity - placesBefore;
                Say(added > 0
                        ? TowerText.Get("tower.message.room_upgraded.places", ("room", TowerText.RoomName(room)), ("level", room.Level), ("count", added),
                                        ("price", UIFormat.Money(check.price)))
                        : TowerText.Get("tower.message.room_upgraded", ("room", TowerText.RoomName(room)), ("level", room.Level), ("price", UIFormat.Money(check.price))),
                    false);
                break;
        }
        Changed();
    }

    public void CancelPurchase()
    {
        if (!PurchaseOpen) return;
        Purchase = default;
        Changed();
    }

    // ------------------------------------------------------------------
    // The build budget: "Fill budget" and "Gamble for it"
    // ------------------------------------------------------------------

    /// <summary>Open "Fill the build budget" (the gamble pop-up).</summary>
    public void OpenBudgetGamble()
    {
        if (gamble != TowerGambleKind.None) return;
        BuildGambleView gambleView = FindGambleView();
        if (gambleView != null)
        {
            if (IntroOpen) DismissIntro();
            gambleView.Open();
        }
        else
        {
            Say(TowerText.Get("tower.message.no_gamble"), true);
        }
    }

    static BuildGambleView FindGambleView()
    {
        BuildGambleView gambleView = BuildGambleView.Instance;
        if (gambleView == null) gambleView = FindAnyObjectByType<BuildGambleView>(FindObjectsInactive.Include);
        return gambleView;
    }

    /// <summary>
    /// "Gamble for it": the build gamble with the bet preset so a win covers what the build budget lacks for
    /// <paramref name="price"/> (the pop-up keeps the company's $1,000 reserve and the table's limits). <paramref name="goal"/>
    /// names the purchase on the pop-up. With nothing missing it just opens the pop-up.
    /// </summary>
    public void GambleFor(double price, string goal)
    {
        if (gamble != TowerGambleKind.None) return;
        BuildGambleView gambleView = FindGambleView();
        if (gambleView == null)
        {
            Say(TowerText.Get("tower.message.no_gamble"), true);
            return;
        }
        if (IntroOpen) DismissIntro();
        double shortfall = Plan.Shortfall(price);
        if (shortfall < 0.5) gambleView.Open();
        else gambleView.OpenFor(shortfall, goal);
    }

    /// <summary>The purchase the player is trying to make right now (price and name), or false.</summary>
    public bool TryGetCurrentPurchase(out double price, out string name)
    {
        price = 0.0;
        name = "";
        if (PurchaseOpen)
        {
            price = PurchaseCheck.price;
            name = PurchaseName(Purchase);
            return true;
        }
        if (UpgradeOpen && UpgradeRoom != null && gamble == TowerGambleKind.None)
        {
            price = plan.UpgradePrice(UpgradeRoom, UpgradeSlot);
            name = TowerText.SlotName(UpgradeRoom, UpgradeSlot);
            return true;
        }
        if (FloorSlipOpen && FloorSlipBuilding != null)
        {
            price = plan.FloorPriceFor(FloorSlipBuilding);
            name = TowerText.Get("tower.purchase.floor.name", ("building", TowerText.BuildingName(FloorSlipBuilding)), ("floor", FloorSlipBuilding.Floors + 1));
            return true;
        }
        if (Pick != null)
        {
            price = plan.RoomPrice(Pick);
            name = TowerText.RoomName(Pick);
            return true;
        }
        if (PickLink != null && LinkGhostVisible)
        {
            price = LinkGhostCheck.price;
            name = TowerText.ConnectionName(PickLink);
            return true;
        }
        return false;
    }

    /// <summary>"Gamble for it" for whatever the player is trying to buy now (the hint strip's and the slips' buttons).</summary>
    public void GambleForCurrent()
    {
        if (TryGetCurrentPurchase(out double price, out string name)) GambleFor(price, name);
        else OpenBudgetGamble();
    }

    // ------------------------------------------------------------------
    // Furniture upgrades
    // ------------------------------------------------------------------

    /// <summary>Open the upgrade sheet (pay, blackjack or random chance) for a piece of furniture.</summary>
    public void OpenUpgrade(TowerRoom room, int slot)
    {
        if (room == null || !plan.Contains(room) || slot < 0 || slot >= room.SlotCount || gamble != TowerGambleKind.None) return;
        if (room.GetLevel(slot) >= plan.MaxLevel)
        {
            Say(TowerText.Get("tower.message.already_top", ("slot", TowerText.SlotName(room, slot)), ("level", TowerText.LevelName(rules, plan.MaxLevel))), false);
            return;
        }
        SelectedSlot = slot;
        UpgradeRoom = room;
        UpgradeSlot = slot;
        UpgradeOpen = true;
        ResultOpen = false;
        Changed();
    }

    public void CloseUpgrade()
    {
        if (gamble != TowerGambleKind.None) return;
        if (!UpgradeOpen && !ResultOpen) return;
        UpgradeOpen = false;
        ResultOpen = false;
        Changed();
    }

    /// <summary>Close the result (and the sheet) after an upgrade.</summary>
    public void CloseResult()
    {
        if (gamble != TowerGambleKind.None) return;
        ResultOpen = false;
        UpgradeOpen = false;
        Changed();
    }

    bool UpgradeReady(out TowerCheck check)
    {
        check = plan.CheckUpgrade(UpgradeRoom, UpgradeSlot);
        if (gamble != TowerGambleKind.None) return false;
        if (check.Ok) return true;
        Say(Describe(check, null), true);
        return false;
    }

    /// <summary>The heat meter's freeze: while the board has frozen gambling, a furniture gamble says why and charges nothing.</summary>
    bool GamblingAllowed()
    {
        if (HeatMeter.CanGamble(out string reason)) return true;
        Say(string.IsNullOrEmpty(reason) ? TowerText.Get("tower.message.frozen") : reason, true);
        return false;
    }

    /// <summary>With one level left a gamble can't give more than paying (its +2 / +3 get capped), so only Pay is offered.</summary>
    bool GambleWorthIt()
    {
        if (UpgradeRoom == null) return false;
        if (plan.MaxLevel - UpgradeRoom.GetLevel(UpgradeSlot) >= 2) return true;
        Say(TowerText.Get("tower.message.one_level_left"), true);
        return false;
    }

    /// <summary>
    /// Tell the heat system how a furniture gamble went: the price charged, what came back as money (a push = the stake,
    /// each level gained = its price, a loss or a lost level = 0) and the company value before the charge.
    /// </summary>
    void ReportGamble(double stake, double payout)
    {
        GambleLedger.Report(GambleLedger.SourceFurniture, stake, payout, gambleValueBefore);
    }

    /// <summary>The furniture's English name (the result's note for logs; the screens use TowerText.SlotName).</summary>
    static string SlotName(TowerRoom room, int slot)
    {
        TowerFurnitureSlot s = room != null ? room.Definition.GetSlot(slot) : null;
        return s != null ? s.name : "Furniture";
    }

    /// <summary>Pay for the next level (+1).</summary>
    public void UpgradePay()
    {
        if (!UpgradeReady(out _)) return;
        TowerRoom room = UpgradeRoom;
        int slot = UpgradeSlot;
        int before = room.GetLevel(slot);
        if (!plan.BuyLevel(room, slot, out TowerCheck check))
        {
            Say(Describe(check, null), true);
            return;
        }
        GameSfx.Play(Sfx.UiConfirm);   // paid for the next level
        Settle(new TowerUpgradeResult
        {
            kind = TowerGambleKind.None, room = room, slot = slot, roomName = room.Name, slotName = SlotName(room, slot),
            levelBefore = before, levelAfter = room.GetLevel(slot), stake = check.price
        });
    }

    public bool IsBlackjackAvailable
    {
        get
        {
            BlackjackGame table = FindTable();
            return table != null && table.CanStartGame && table.blackjackPanel != null
                   && (table.blackjackPanel.transform.parent == null || table.blackjackPanel.transform.parent.gameObject.activeInHierarchy);
        }
    }

    static BlackjackGame FindTable()
    {
        return BlackjackGame.Instance != null ? BlackjackGame.Instance : FindAnyObjectByType<BlackjackGame>(FindObjectsInactive.Include);
    }

    /// <summary>Stake the level's price on a blackjack hand: win +2 levels, push refunds, loss nothing.</summary>
    public void UpgradeBlackjack()
    {
        if (!UpgradeReady(out _) || !GamblingAllowed() || !GambleWorthIt()) return;
        BlackjackGame table = FindTable();
        if (!IsBlackjackAvailable)
        {
            Say(TowerText.Get("tower.message.no_table"), true);
            return;
        }
        if (!plan.TakeStake(UpgradeRoom, UpgradeSlot, out double stake))
        {
            Say(Describe(plan.CheckUpgrade(UpgradeRoom, UpgradeSlot), null), true);
            return;
        }
        BeginGamble(TowerGambleKind.Blackjack, stake);

        // The table is on the HUD canvas, under the tower screen
        TowerView view = View;
        if (view != null) view.SetCanvasVisible(false);

        string stakeText = TowerText.Get("tower.blackjack.stake", ("stake", UIFormat.Money(stake)), ("slot", TowerText.SlotName(gambleRoom, gambleSlot)),
                                         ("count", rules.blackjackWinLevels));
        // Listen first: a natural blackjack ends the hand inside StartGame
        handSettled = false;
        handTable = table;
        table.HandEnded -= OnHandEnded;
        table.HandEnded += OnHandEnded;
        bool started;
        try
        {
            started = table.StartGame(stakeText, OnBlackjackFinished);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            started = false;
        }

        if (!started)
        {
            table.HandEnded -= OnHandEnded;
            handTable = null;
            double refund = pendingStake;
            pendingStake = 0.0;
            gamble = TowerGambleKind.None;
            plan.AddBudget(refund);
            gambleDiscounted = false;   // the stake came back: no discount used
            ShowCanvasAgain();
            Say(TowerText.Get("tower.message.blackjack_failed", ("amount", UIFormat.Money(refund))), false);
            Changed();
        }
    }

    void BeginGamble(TowerGambleKind kind, double stake)
    {
        gamble = kind;
        pendingStake = stake;
        gambleDiscounted = plan.IsDiscounted(TowerPlan.PriceFurniture);
        gambleRoom = UpgradeRoom;
        gambleSlot = UpgradeSlot;
        gambleLevelBefore = gambleRoom.GetLevel(gambleSlot);
        // The stake comes out of the build budget, so the company value is the same before and after the charge
        gambleValueBefore = CompanyManager.Instance != null ? CompanyManager.Instance.companyValue : 0.0;
        ResultOpen = false;
        Changed();
    }

    /// <summary>The hand is decided (its result is still on the table): settle now, so a save before Continue keeps it.</summary>
    void OnHandEnded(BlackjackResult result)
    {
        if (handTable != null) handTable.HandEnded -= OnHandEnded;
        handTable = null;
        if (gamble != TowerGambleKind.Blackjack || handSettled) return;
        handSettled = true;
        double stake = pendingStake;
        pendingStake = 0.0;
        bool refunded = false;
        double payout = 0.0;
        if (result == BlackjackResult.Win)
        {
            int gained = plan.ChangeLevel(gambleRoom, gambleSlot, rules.blackjackWinLevels, stake);
            payout = Math.Max(0, gained) * stake;
        }
        else if (result == BlackjackResult.Push)
        {
            plan.AddBudget(stake);
            refunded = true;
            payout = stake;
        }
        ReportGamble(stake, payout);
        if (!refunded) ReportGambleDiscount();
        lastResult = new TowerUpgradeResult
        {
            kind = TowerGambleKind.Blackjack, room = gambleRoom, slot = gambleSlot, roomName = gambleRoom.Name, slotName = SlotName(gambleRoom, gambleSlot),
            levelBefore = gambleLevelBefore, levelAfter = gambleRoom.GetLevel(gambleSlot), hand = result, stake = stake, refunded = refunded
        };
    }

    void OnBlackjackFinished(BlackjackResult result)
    {
        // The table closed without reporting the end of the hand: settle now
        if (!handSettled) OnHandEnded(result);
        bool ours = gamble == TowerGambleKind.Blackjack;
        gamble = TowerGambleKind.None;
        ShowCanvasAgain();
        if (ours) Settle(lastResult);
        else Changed();
    }

    void ShowCanvasAgain()
    {
        TowerView view = View;
        if (view != null) view.SetCanvasVisible(true);
    }

    /// <summary>Stake the level's price on the random chance roll (+3 / +2 / 0 / -1 levels by the catalog's odds).</summary>
    public void UpgradeRoll()
    {
        if (!UpgradeReady(out _) || !GamblingAllowed() || !GambleWorthIt()) return;
        if (!plan.TakeStake(UpgradeRoom, UpgradeSlot, out double stake))
        {
            Say(Describe(plan.CheckUpgrade(UpgradeRoom, UpgradeSlot), null), true);
            return;
        }
        BeginGamble(TowerGambleKind.Roll, stake);
        GameSfx.Play(Sfx.DiceShake);   // until the result (rollSeconds; the cue fades out at 1.05 s)
        rollOutcome = RollOutcome();
        rollStartedAt = Time.unscaledTime;
        rollEndsAt = rollStartedAt + Mathf.Max(0.2f, rollSeconds);
        Changed();
    }

    int RollOutcome()
    {
        List<TowerRollOutcome> outcomes = rules.rollOutcomes;
        float total = 0f;
        foreach (TowerRollOutcome o in outcomes) total += Mathf.Max(0f, o.chance);
        if (total <= 0f) return 0;
        float pick = UnityEngine.Random.value * total;
        foreach (TowerRollOutcome o in outcomes)
        {
            pick -= Mathf.Max(0f, o.chance);
            if (pick <= 0f) return o.levels;
        }
        return outcomes[outcomes.Count - 1].levels;
    }

    /// <summary>What the rolling die shows while it spins (the real outcome at the end).</summary>
    public int RollFace
    {
        get
        {
            if (gamble != TowerGambleKind.Roll) return lastResult.rolled;
            List<TowerRollOutcome> outcomes = rules.rollOutcomes;
            if (RollProgress >= 0.85f || outcomes.Count == 0) return rollOutcome;
            int step = Mathf.FloorToInt((Time.unscaledTime - rollStartedAt) * 12f);
            return outcomes[step % outcomes.Count].levels;
        }
    }

    void SettleRoll()
    {
        if (gamble != TowerGambleKind.Roll) return;
        double stake = pendingStake;
        pendingStake = 0.0;
        gamble = TowerGambleKind.None;
        GameSfx.Play(Sfx.DiceThrow);   // the roll's result
        int gained = plan.ChangeLevel(gambleRoom, gambleSlot, rollOutcome, stake);
        ReportGamble(stake, Math.Max(0, gained) * stake);
        ReportGambleDiscount();
        Settle(new TowerUpgradeResult
        {
            kind = TowerGambleKind.Roll, room = gambleRoom, slot = gambleSlot, roomName = gambleRoom.Name, slotName = SlotName(gambleRoom, gambleSlot),
            levelBefore = gambleLevelBefore, levelAfter = gambleRoom.GetLevel(gambleSlot), rolled = rollOutcome, stake = stake
        });
    }

    /// <summary>The gamble kept a discounted stake: the manager's perk counts a use.</summary>
    void ReportGambleDiscount()
    {
        if (!gambleDiscounted) return;
        gambleDiscounted = false;
        plan.ReportDiscountUsed(TowerPlan.PriceFurniture);
    }

    void Settle(TowerUpgradeResult result)
    {
        lastResult = result;
        try { UpgradeSettled?.Invoke(result); }
        catch (Exception e) { Debug.LogException(e); }

        TowerView view = View;
        if (IsOpen && view != null && view.IsShowing)
        {
            UpgradeOpen = true;
            ResultOpen = true;
        }
        else
        {
            // Build mode closed meanwhile: just tell the player
            GameNotifications.Show(TowerView.ResultSummary(result, rules), result.levelAfter > result.levelBefore ? NotificationKind.Good : NotificationKind.Info);
        }
        Changed();
    }

    /// <summary>A loaded save replaces the campus: a gamble that hadn't settled belongs to the old one and is dropped.</summary>
    void AbandonGamble()
    {
        if (gamble == TowerGambleKind.None) return;
        if (handTable != null) handTable.HandEnded -= OnHandEnded;
        handTable = null;
        gamble = TowerGambleKind.None;
        pendingStake = 0.0;
        ShowCanvasAgain();
    }

    // ------------------------------------------------------------------
    // Money, effects, capacity, upkeep, knowledge
    // ------------------------------------------------------------------

    /// <summary>Add money to the build budget (gamble winnings). Negative amounts take money out.</summary>
    public void AddBudget(double amount)
    {
        Plan.AddBudget(amount);
    }

    /// <summary>Daily income, productivity, happiness, trust, capacity, upkeep and knowledge of the campus (CompanyManager applies its caps).</summary>
    public TowerTotals GetDailyTotals()
    {
        EnsurePlan();
        if (totalsVersion != plan.Version)
        {
            totals = plan.ComputeTotals();
            totalsVersion = plan.Version;
        }
        return totals;
    }

    /// <summary>Happiness a day that employees over capacity cost (0 by default: an older save over capacity is grandfathered).</summary>
    public float GetSeatPenalty(int employees)
    {
        return Plan.SeatPenalty(employees);
    }

    /// <summary>Old name of TotalCapacity (the staff files read it).</summary>
    public int TotalSeats => TotalCapacity;

    /// <summary>What the accounting rooms save on salaries a day now (salaries after Tax Optimization x the savings share).</summary>
    public double SalarySavingsPerDay
    {
        get
        {
            CompanyManager company = CompanyManager.Instance;
            return SalarySavings(company, GetDailyTotals().salarySavings);
        }
    }

    static double SalarySavings(CompanyManager company, float share)
    {
        if (company == null || share <= 0f) return 0.0;
        SkillTreeManager skills = SkillTreeManager.Instance;
        float tax = skills != null ? Mathf.Clamp01(skills.GetTaxOptimization()) : 0f;
        return Math.Max(0.0, company.totalSalary / 365.0 * (1.0 - tax) * share);
    }

    /// <summary>The day's recurring money of the campus (DailyEconomyHooks, "upkeep" in DayBreakdown.recurring).</summary>
    public string HookName => "upkeep";

    /// <summary>The campus's upkeep after savings (a cost), plus the accounting rooms' salary savings. Never more than salaries back.</summary>
    public double DailyMoney(CompanyManager company, double resultBeforeHooks)
    {
        TowerTotals t = GetDailyTotals();
        double savings = SalarySavings(company, t.salarySavings);
        LastUpkeepPaid = t.upkeep;
        LastSalarySaved = savings;
        return savings - t.upkeep;
    }

    /// <summary>The campus's meters come through CompanyManager.GetBuildingBonuses (with their own caps): nothing here.</summary>
    public void DailyMeters(CompanyManager company, ref float happiness, ref float trust)
    {
    }

    void HookCompany()
    {
        CompanyManager company = CompanyManager.Instance;
        if (company == hookedCompany) return;
        UnhookCompany();
        hookedCompany = company;
        if (company == null) return;
        company.EmployeeHired += OnEmployeeHired;
        company.DayEnded += OnDayEnded;
    }

    void UnhookCompany()
    {
        if (hookedCompany != null)
        {
            hookedCompany.EmployeeHired -= OnEmployeeHired;
            hookedCompany.DayEnded -= OnDayEnded;
        }
        hookedCompany = null;
    }

    /// <summary>Capacity is a hard limit: the hire that fills the last place gets a toast pointing at build mode.</summary>
    void OnEmployeeHired(ApplicantData hire)
    {
        NotifyCapacityIfChanged();
        if (!toastUnseatedHires) return;
        int employees = Employees, capacity = TotalCapacity;
        if (employees < capacity) return;
        GameNotifications.Show(TowerText.Get("tower.capacity.toast.full", ("count", employees), ("capacity", capacity)), NotificationKind.Info);
    }

    /// <summary>Someone was let go (the staff files): the place is free again at once.</summary>
    void OnEmployeeFired(ApplicantData employee, double severance)
    {
        NotifyCapacityIfChanged();
    }

    /// <summary>Knowledge rooms add their skill-point progress once a day.</summary>
    void OnDayEnded(int day, double change)
    {
        float knowledge = GetDailyTotals().knowledgePerDay;
        if (knowledge <= 0f) return;
        SkillTreeManager skills = SkillTreeManager.Instance;
        if (skills == null) return;
        int points = skills.AddKnowledge(knowledge);
        if (points > 0)
            GameNotifications.Show(TowerText.Get("tower.toast.knowledge", ("count", points)), NotificationKind.Special);
    }

    // ------------------------------------------------------------------
    // Texts
    // ------------------------------------------------------------------

    /// <summary>Player text for a failed check (the message chip).</summary>
    public string Describe(TowerCheck check, TowerRoomDefinition definition)
    {
        return Describe(check, definition, false);
    }

    /// <summary>
    /// Player text for a failed check. <paramref name="underTitle"/>: the reason alone ("1 tile left on this side"), for the
    /// tooltip whose title already says "Doesn't fit:".
    /// </summary>
    public string Describe(TowerCheck check, TowerRoomDefinition definition, bool underTitle)
    {
        switch (check.issue)
        {
            case TowerIssue.None:
                return "";
            case TowerIssue.OffTheFloor:
                return check.tilesLeft > 0
                    ? TowerText.Get(underTitle ? "tower.issue.off_floor.tiles.short" : "tower.issue.off_floor.tiles", ("count", check.tilesLeft))
                    : TowerText.Get("tower.issue.off_floor");
            case TowerIssue.Overlaps:
                return check.blocker != null
                    ? TowerText.Get(underTitle ? "tower.issue.overlaps.room.short" : "tower.issue.overlaps.room", ("room", TowerText.RoomName(check.blocker)))
                    : TowerText.Get("tower.issue.overlaps");
            case TowerIssue.NoSuchFloor:
                return TowerText.Get("tower.issue.no_floor");
            case TowerIssue.CantAfford:
                return TowerText.Get("tower.issue.cant_afford", ("amount", UIFormat.Money(Math.Max(0.0, check.price - Plan.Budget))));
            case TowerIssue.Locked:
                string reason = check.lockInfo.IsLocked ? TowerText.LockText(check.lockInfo, definition, rules) : TowerText.LockReason(definition);
                return reason.Length > 0 ? reason : TowerText.Get("tower.issue.locked");
            case TowerIssue.BuiltIn:
                return TowerText.Get(Plan.StarterRoom != null ? "tower.issue.built_in.campus" : "tower.issue.built_in");
            case TowerIssue.TopFloor:
                if (DemoMode.Active) return DemoMode.T("demo.lock.floors");   // Demo: only the HQ, capped at 6 floors
                return TowerText.Get("tower.issue.top_floor", ("count", check.limit > 0 ? check.limit : Plan.Rules.maxFloors));
            case TowerIssue.MaxLevel:
                return TowerText.Get("tower.issue.max_level", ("level", TowerText.LevelName(Rules, Plan.MaxLevel)));
            case TowerIssue.LimitReached:
                return TowerText.Get(check.perBuilding ? "tower.issue.limit.building" : "tower.issue.limit.company", ("count", check.limit));
            case TowerIssue.WrongBuilding:
                string only = TowerText.AllowedBuildings(rules, definition);
                return only.Length > 0 ? TowerText.Get("tower.issue.wrong_building.only", ("buildings", only)) : TowerText.Get("tower.issue.wrong_building");
            case TowerIssue.FloorRule:
                if (check.floorRule == TowerFloorRule.GroundOnly) return TowerText.Get("tower.rule.ground");
                if (check.floorRule == TowerFloorRule.TopOnly) return TowerText.Get("tower.rule.top");
                return TowerText.Get("tower.rule.min_floor", ("floor", check.floorValue));
            case TowerIssue.CapacityInUse:
                return TowerText.Get("tower.issue.capacity_in_use", ("employees", Employees), ("count", check.limit + ExtraPlaces));
            case TowerIssue.NotConnectable:
                return TowerText.Get("tower.issue.link.neighbour");
            case TowerIssue.TooManyLinks:
                return TowerText.Get("tower.issue.link.max", ("count", check.limit));
            case TowerIssue.TopRoomLevel:
                return TowerText.Get("tower.issue.room_top");
            case TowerIssue.NoRoomLevels:
                return TowerText.Get("tower.issue.room_no_levels");
            case TowerIssue.BuildingLimit:
                return TowerText.Get("tower.issue.building_limit", ("count", check.limit));
            default:
                return TowerText.Get("tower.issue.gone");
        }
    }

    void Say(string text, bool error)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (error) GameSfx.Play(Sfx.UiError, 0.5f);   // a refused click (can't place, locked, can't afford ...)
        Message?.Invoke(text, error);
        TowerView view = View;
        if (view != null) view.ShowMessage(text, error);
        else GameNotifications.Show(text, error ? NotificationKind.Bad : NotificationKind.Info);
    }

    // ------------------------------------------------------------------
    // Save / load (ISaveParticipant)
    // ------------------------------------------------------------------

    public string SaveKey => "building";

    public string CaptureState()
    {
        EnsurePlan();
        // A roll's outcome is fixed when the stake is taken: apply it before saving, so quitting during the animation
        // (the save on quit runs before OnDisable) keeps the result instead of only the lost stake
        if (gamble == TowerGambleKind.Roll) SettleRoll();
        return JsonUtility.ToJson(TowerSave.Capture(plan, introSeen));
    }

    /// <summary>Reads the campus (format 4) and converts the tower (3), blueprint (2) and old 3D (1) build saves.</summary>
    public void RestoreState(string json)
    {
        EnsurePlan();
        if (string.IsNullOrEmpty(json)) return;

        TowerLoadData data;
        try
        {
            data = JsonUtility.FromJson<TowerLoadData>(json);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[TowerBuilder] Couldn't read the saved build data: " + e.Message);
            return;
        }
        if (data == null) return;

        AbandonGamble();
        bool wasOpen = IsOpen;
        ResetTransientState();
        TowerRestoreReport report = TowerSave.Restore(plan, data);
        introSeen = report.introSeen;
        FocusBuilding = plan.Headquarters;
        foreach (string line in report.log)
            Debug.Log("[TowerBuilder] " + line);
        if (report.format != TowerSave.Version)
            Debug.Log("[TowerBuilder] Converted a format " + report.format + " build save: " + report.officesConverted + " offices, "
                      + report.floorsAdded + " free floors, " + report.levelsGranted + " furniture levels, first office "
                      + (report.starterAdded ? "added" : "not added") + ", " + UIFormat.Money(report.refunded) + " refunded.");
        int employees = Employees, capacity = TotalCapacity;
        if (employees > capacity)
            Debug.Log("[TowerBuilder] " + employees + " employees and " + capacity + " places: nobody is fired (grandfathered), hiring waits for more office space.");
        string message = TowerText.RestoreMessage(report);   // report.message is the English one
        if (!string.IsNullOrEmpty(message))
        {
            TowerView view = View;
            if (wasOpen && view != null && view.IsShowing) view.ShowMessage(message, false);
            else GameNotifications.Show(message, NotificationKind.Info);
        }
        NotifyCapacityIfChanged();
        Changed();
    }
}
