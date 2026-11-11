using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The HQ campus screen (design canvas page "HQ tower (2D build)", grown into a campus on 2026-09-29): a side view of the
/// company's buildings on a blueprint that covers the screen while build mode is open. It draws every building's floors,
/// rooms, furniture and staff figures, their lifts, the skybridges between them, the placement ghosts and selection, the
/// add-floor slots, and the top bar, campus bar, tool tray, title block, hint strip, drawer (rooms / furniture / campus),
/// room card (clipboard), room view, upgrade sheet, purchase slip and the add-floor / sell slips, and forwards clicks to
/// TowerBuilder.
/// - With Root empty, TowerViewFactory builds all of it at runtime on its own overlay canvas (sorting order 60: above the
///   HUD, below the toasts). A designed prefab can fill in the same references instead.
/// - The campus is a row of buildings, the HQ first (TowerGeo.OriginX): the camera pans along it, scrolls up and down and
///   zooms (TowerViewCampus.cs), by moving and scaling towerContent (no mesh rebuild; the meshes redraw once the zoom
///   settles, for crisp lines). Each floor of each building is its own mesh, rebuilt only when that floor changes and
///   switched off while it is off screen. Text is pooled.
/// - While the opaque screen covers everything, the main camera is switched off (HDRP would still render the office);
///   it comes back whenever the screen is hidden (the blackjack table), closed, disabled or the scene changes.
/// - Its words come from the "Tower" String Table (TowerText). On a language change everything is written again, in the
///   language's font (L10nFont on every text, pooled ones included).
/// </summary>
public partial class TowerView : MonoBehaviour
{
    /// <summary>What the mouse is over (the top-most UI element under it).</summary>
    public enum PointerTarget
    {
        None,
        Tower,
        Ui
    }

    public static TowerView Instance { get; private set; }

    /// <summary>Street level sits this far above the bottom of the screen (the design: y 880 of 1080).</summary>
    public const float GroundFromBottom = 200f;
    const float TopBarHeight = 84f;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    [Header("Look")]
    [Tooltip("Font of the tower screens (empty = the Coming Soon font the HUD has loaded, else TextMesh Pro's default)")]
    public TMP_FontAsset font;
    [Tooltip("Sorting order of the runtime canvas: above the HUD, below the toasts (900)")]
    public int sortingOrder = 60;
    [Tooltip("Also switch the 3D camera off under the opaque tower in the Editor (saves GPU, but the Game view then shows " +
             "\"No cameras rendering\"). Player builds always switch it off.")]
    public bool hideCameraInEditor = false;

    [Header("Screen (leave Root empty to build everything at runtime)")]
    [Tooltip("Everything of build mode; shown while build mode is open")]
    public RectTransform root;
    [Tooltip("Opaque blueprint behind the tower; it takes the clicks on the tower")]
    public Graphic towerSurface;
    [Tooltip("Blueprint grid (full screen; it moves with the scroll)")]
    public BlueprintGraphic gridLayer;

    [Header("Campus (design pixels: origin = street level under the middle of the HQ)")]
    [Tooltip("Anchored bottom-centre; moved and scaled for the camera")]
    public RectTransform towerContent;
    [Tooltip("The street, every building's walls, roof and lift shaft, and the skybridges")]
    public BlueprintGraphic structureLayer;
    [Tooltip("Every building's lift cables")]
    public BlueprintGraphic cablesLayer;
    [Tooltip("Parent of the per-building, per-floor drawings (made at runtime)")]
    public RectTransform floorLayer;
    [Tooltip("Parent of the text on the campus (made at runtime)")]
    public RectTransform labelLayer;
    public BlueprintGraphic overlayLayer;
    [Tooltip("The HQ's lift car (the other buildings get copies)")]
    public RectTransform liftCar;
    public TowerChips chips = new TowerChips();

    [Header("Around the campus")]
    public TowerTopBar topBar = new TowerTopBar();
    [Tooltip("Build room, Add floor, Furniture, Move, Sell, Campus")]
    public Button[] toolButtons = new Button[6];
    public TextMeshProUGUI titleBlockName;
    public TextMeshProUGUI titleBlockInfo;
    public TowerHintStrip hintStrip = new TowerHintStrip();
    public RectTransform messageChip;
    public TextMeshProUGUI messageText;
    public TowerScrollControl scrollControl = new TowerScrollControl();
    public TowerCampusBar campusBar = new TowerCampusBar();

    [Header("Panels")]
    public TowerDrawer drawer = new TowerDrawer();
    public TowerClipboard clipboard = new TowerClipboard();
    public TowerRoomView roomView = new TowerRoomView();
    public TowerUpgradeSheet sheet = new TowerUpgradeSheet();
    public TowerFloorSlip floorSlip = new TowerFloorSlip();
    public TowerSellSlip sellSlip = new TowerSellSlip();
    public TowerIntroNote intro = new TowerIntroNote();
    public TowerPurchaseSlip purchaseSlip = new TowerPurchaseSlip();

    Canvas canvas;
    Canvas runtimeCanvas;
    CanvasGroup rootGroup;
    CanvasGroup messageGroup;
    bool hooked;
    bool fontsFollowLanguage;
    Camera hiddenWorldCamera;

    Vector2 viewSize;
    Vector2 shownGridOffset = new Vector2(float.NaN, float.NaN);

    // Drawing
    /// <summary>One building's drawings: its floors (under a root at the building's origin) and its lift car.</summary>
    sealed class BuildingDrawing
    {
        public TowerBuilding building;
        public RectTransform root;
        public RectTransform car;
        public readonly List<BlueprintGraphic> floors = new List<BlueprintGraphic>();
        public readonly List<int> floorKeys = new List<int>();
        public float carY = float.NaN;
        public int visibleLow = -1, visibleHigh = -1;
        public bool onScreen = true;
    }

    readonly List<BuildingDrawing> drawings = new List<BuildingDrawing>();
    readonly List<TowerRoom> scratchRooms = new List<TowerRoom>();
    readonly Dictionary<TowerRoom, int> seated = new Dictionary<TowerRoom, int>();
    int seatedEmployees = -1, seatedVersion = -1;
    Vector2[] toolBase;
    readonly List<TextMeshProUGUI> labels = new List<TextMeshProUGUI>();
    readonly List<RectTransform> pads = new List<RectTransform>();
    int usedLabels, usedPads;
    int drawnPlanVersion = -1;
    int drawnStateVersion = -1;
    int drawnStaffKey = -1;
    int drawnStructureKey = -1;
    float drawnScale = -1f;
    int visibilityKey = int.MinValue;
    float drawnCablesKey = float.NaN;

    // Panels
    int topKey, trayKey, hintKey, scrollKey, drawerKey, clipKey, roomKey, sheetKey, floorSlipKey, sellKey, introKey, chipsKey;
    int shownRollFace = int.MinValue;
    float messageUntil;
    bool heatOk = true;
    string frozenReason = "";
    float nextHeatCheck;
    readonly List<TowerCatalogCard> cards = new List<TowerCatalogCard>();
    int catalogFilter;          // 0 all, 1 offices, 2 staff, 3 business, 4 prestige
    int shownDrawerPage;
    readonly List<TowerSlotRow> drawerRows = new List<TowerSlotRow>();
    readonly List<TowerSlotRow> clipRows = new List<TowerSlotRow>();
    readonly List<TowerSlotSpot> spots = new List<TowerSlotSpot>();
    readonly List<int> viewOrder = new List<int>(8);
    string shownCompany;
    float nextNameCheck;

    PointerEventData pointerData;
    EventSystem pointerSystem;
    readonly List<RaycastResult> raycastResults = new List<RaycastResult>();
    int pointerFrame = -1;
    PointerTarget pointerTarget;

    public bool IsVisible => root != null && root.gameObject.activeInHierarchy;
    /// <summary>Open and on screen (not hidden for the blackjack table).</summary>
    public bool IsShowing => IsVisible && (rootGroup == null || rootGroup.alpha > 0f);
    public float Scroll => scroll;

    /// <summary>The canvas build mode is drawn on.</summary>
    public Canvas Canvas
    {
        get
        {
            EnsureBuilt();
            return canvas;
        }
    }

    /// <summary>Everything of build mode (the budget gamble pop-up goes under it too).</summary>
    public RectTransform Root
    {
        get
        {
            EnsureBuilt();
            return root;
        }
    }

    // ------------------------------------------------------------------
    // Lifecycle
    // ------------------------------------------------------------------

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[TowerView] Removed a second TowerView on '" + name + "'.");
            Destroy(this);
            return;
        }
        Instance = this;
    }

    void OnEnable()
    {
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
        L10n.LanguageChanged += OnLanguageChanged;
    }

    void OnDisable()
    {
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        L10n.LanguageChanged -= OnLanguageChanged;
        RestoreWorldCamera();
    }

    // Write everything again on the next frame (the fonts switch by themselves: L10nFont)
    void OnLanguageChanged()
    {
        drawnPlanVersion = -1;   // the room names, plates and "empty" pads on the campus (the floor drawings keep their own keys)
        ResetPanelKeys();
        shownRollFace = int.MinValue;
    }

    void ResetPanelKeys()
    {
        topKey = trayKey = hintKey = scrollKey = drawerKey = clipKey = roomKey = sheetKey = floorSlipKey = sellKey = introKey = chipsKey = 0;
        campusBarKey = purchaseKey = 0;
    }

    void OnDestroy()
    {
        RestoreWorldCamera();
        if (Instance != this) return;
        Instance = null;
        if (runtimeCanvas != null) Destroy(runtimeCanvas.gameObject);
    }

    void OnActiveSceneChanged(Scene from, Scene to)
    {
        RestoreWorldCamera();
    }

    void OnSceneUnloaded(Scene scene)
    {
        RestoreWorldCamera();
    }

    /// <summary>The font to build with: this view's, else the Coming Soon asset the HUD has loaded, else TMP's default.</summary>
    public TMP_FontAsset ResolveFont()
    {
        if (font != null) return font;
        foreach (TMP_FontAsset candidate in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
        {
            if (candidate != null && candidate.name == "ComingSoon SDF")
            {
                font = candidate;
                break;
            }
        }
        return font;
    }

    /// <summary>Build the screen at runtime if no designed one is assigned, and connect the buttons.</summary>
    public void EnsureBuilt()
    {
        if (root == null)
        {
            TowerViewFactory.Build(this, ResolveFont());
            runtimeCanvas = root.GetComponentInParent<Canvas>(true);
        }
        if (!fontsFollowLanguage)
        {
            // Built here or designed: every text takes the language's font (the card, row and spot templates too, so their
            // clones do; the pooled labels on the tower get theirs when they're made)
            L10nFont.AttachAll(root);
            fontsFollowLanguage = true;
        }
        Canvas parentCanvas = root.GetComponentInParent<Canvas>(true);
        canvas = parentCanvas != null ? parentCanvas.rootCanvas : null;
        if (rootGroup == null)
        {
            rootGroup = root.GetComponent<CanvasGroup>();
            if (rootGroup == null) rootGroup = root.gameObject.AddComponent<CanvasGroup>();
        }
        EnsureTowerLayers();
        HookButtons();
    }

    /// <summary>A designed root may leave out the campus layers: add what is missing.</summary>
    void EnsureTowerLayers()
    {
        if (towerContent == null)
        {
            towerContent = BlueprintUI.NewRect("TowerContent", root);
            towerContent.SetSiblingIndex(Mathf.Min(1, root.childCount - 1));
        }
        towerContent.anchorMin = towerContent.anchorMax = new Vector2(0.5f, 0f);
        towerContent.pivot = new Vector2(0.5f, 0f);
        towerContent.sizeDelta = Vector2.zero;
        if (structureLayer == null) structureLayer = TowerViewFactory.Layer(towerContent, "Structure");
        if (cablesLayer == null) cablesLayer = TowerViewFactory.Layer(towerContent, "Cables");
        if (floorLayer == null) floorLayer = TowerViewFactory.Point(BlueprintUI.NewRect("Floors", towerContent));
        if (labelLayer == null) labelLayer = TowerViewFactory.Point(BlueprintUI.NewRect("Labels", towerContent));
        if (overlayLayer == null) overlayLayer = TowerViewFactory.Layer(towerContent, "Overlay");
    }

    void HookButtons()
    {
        if (hooked) return;
        hooked = true;

        for (int i = 0; i < toolButtons.Length; i++)
        {
            if (toolButtons[i] == null) continue;
            TowerTool tool = (TowerTool)(i + 1);
            toolButtons[i].onClick.AddListener(() => WithBuilder(b => b.ToggleTool(tool)));
        }
        AddClick(topBar.fillBudgetButton, b => b.OpenBudgetGamble());
        AddClick(topBar.exitButton, b => b.RequestExit());
        AddClick(hintStrip.cancelButton, b => b.CancelCurrentAction());
        AddClick(hintStrip.gambleButton, b => b.GambleForCurrent());
        if (scrollControl.upButton != null) scrollControl.upButton.onClick.AddListener(() => ScrollBy(TowerGeo.FloorHeight));
        if (scrollControl.downButton != null) scrollControl.downButton.onClick.AddListener(() => ScrollBy(-TowerGeo.FloorHeight));

        AddClick(drawer.roomsTab, b => b.SetTool(TowerTool.BuildRoom));
        AddClick(drawer.furnitureTab, b => b.SetTool(TowerTool.Furniture));
        AddClick(drawer.campusTab, b => b.SetTool(TowerTool.Campus));
        AddClick(drawer.closeButton, b => b.SetTool(TowerTool.None));
        for (int i = 0; i < drawer.filterButtons.Length; i++)
        {
            int filter = i;
            if (drawer.filterButtons[i] != null) drawer.filterButtons[i].onClick.AddListener(() => { catalogFilter = filter; drawerKey = 0; });
        }
        AddClick(drawer.upgradeButton, b => b.OpenUpgrade(b.Selected, b.SelectedSlot));
        AddClick(drawer.fillLink, b => b.GambleFor(b.Plan.UpgradePrice(b.Selected, b.SelectedSlot), TowerText.SlotName(b.Selected, b.SelectedSlot)));

        AddClick(clipboard.closeButton, b => { if (b.InsideRoom != null) b.CloseRoom(); else b.Deselect(); });
        AddClick(clipboard.openButton, b => b.OpenRoom(b.Selected));
        AddClick(clipboard.moveButton, b => b.StartMove(b.Selected));
        AddClick(clipboard.sellButton, b => b.AskSell(b.Selected));
        AddClick(clipboard.upgradeButton, b => b.OpenUpgrade(b.InsideRoom, b.SelectedSlot));
        AddClick(clipboard.backButton, b => b.CloseRoom());
        AddClick(clipboard.levelButton, b => b.OpenRoomUpgrade(b.Selected));

        AddClick(sheet.closeButton, b => b.CloseUpgrade());
        AddClick(sheet.cancelButton, b => b.CloseUpgrade());
        AddClick(sheet.payButton, b => b.UpgradePay());
        AddClick(sheet.blackjackButton, b => b.UpgradeBlackjack());
        AddClick(sheet.rollButton, b => b.UpgradeRoll());
        AddClick(sheet.fillLink, b => b.GambleForCurrent());
        AddClick(sheet.resultButton, b => b.CloseResult());

        AddClick(floorSlip.buildButton, b => b.ConfirmFloor());
        AddClick(floorSlip.cancelButton, b => b.CancelFloorSlip());
        AddClick(floorSlip.fillLink, b => b.GambleForCurrent());
        AddClick(sellSlip.sellButton, b => b.ConfirmSell());
        AddClick(sellSlip.keepButton, b => b.CancelSell());
        AddClick(sellSlip.closeButton, b => b.CancelSell());
        AddClick(intro.gotItButton, b => b.DismissIntro());
        AddClick(intro.fillBudgetButton, IntroFillBudget);   // Demo: aims at the first office (plain Fill budget otherwise)
        HookCampusButtons();
        // Demo: the campus is in the full game
        if (toolButtons.Length > 5) DimForDemo(toolButtons[5]);
        DimForDemo(drawer.campusTab);
    }

    static void AddClick(Button button, Action<TowerBuilder> action)
    {
        if (button != null) button.onClick.AddListener(() => WithBuilder(action));
    }

    static void WithBuilder(Action<TowerBuilder> action)
    {
        TowerBuilder builder = TowerBuilder.Instance;
        if (builder != null && builder.IsOpen) action(builder);
    }

    // ------------------------------------------------------------------
    // Showing
    // ------------------------------------------------------------------

    public void Show()
    {
        EnsureBuilt();
        if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);
        SetCanvasVisible(true);
        root.SetAsLastSibling();
        drawnPlanVersion = drawnStateVersion = drawnStaffKey = drawnStructureKey = -1;
        ResetPanelKeys();
        shownRollFace = int.MinValue;
        visibilityKey = int.MinValue;
        shownCompany = null;
        nextNameCheck = 0f;
        shownGridOffset = new Vector2(float.NaN, float.NaN);
        seatedEmployees = seatedVersion = -1;
        scroll = scrollTarget = 0f;
        foreach (BuildingDrawing d in drawings) d.carY = float.NaN;
        drawnCablesKey = float.NaN;
        if (messageChip != null) messageChip.gameObject.SetActive(false);
    }

    public void Hide()
    {
        RestoreWorldCamera();
        if (root != null && root.gameObject.activeSelf) root.gameObject.SetActive(false);
    }

    /// <summary>Hide or show the screen without closing it (the blackjack table is on the HUD canvas below).</summary>
    public void SetCanvasVisible(bool visible)
    {
        // The table is drawn over the 3D office: the camera comes back right away (LateUpdate switches it off again)
        if (!visible) RestoreWorldCamera();
        if (rootGroup == null) return;
        rootGroup.alpha = visible ? 1f : 0f;
        rootGroup.blocksRaycasts = visible;
        rootGroup.interactable = visible;
    }

    /// <summary>
    /// A short message above the tool tray, on top of everything on the tower screen (the gamble pop-up included).
    /// While the screen isn't showing it becomes a game notification.
    /// </summary>
    public void ShowMessage(string text, bool error)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (messageChip == null || messageText == null || !IsShowing)
        {
            GameNotifications.Show(text, error ? NotificationKind.Bad : NotificationKind.Info);
            return;
        }
        messageText.text = text;
        BlueprintBox box = messageChip.GetComponent<BlueprintBox>();
        if (box != null) box.Fill = error ? UIColors.LossSoft : UIColors.PaperHi;
        if (messageGroup == null)
        {
            messageGroup = messageChip.GetComponent<CanvasGroup>();
            if (messageGroup == null) messageGroup = messageChip.gameObject.AddComponent<CanvasGroup>();
            messageGroup.blocksRaycasts = false;
        }
        messageGroup.alpha = 1f;
        messageChip.gameObject.SetActive(true);
        messageChip.SetAsLastSibling();   // above the gamble pop-up (it goes on this screen's root too)
        messageUntil = Time.unscaledTime + (error ? 3f : 2.4f);
    }

    // ------------------------------------------------------------------
    // Frame
    // ------------------------------------------------------------------

    void LateUpdate()
    {
        UpdateWorldCamera();
        if (!IsVisible) return;
        TowerBuilder b = TowerBuilder.Instance;
        if (b == null) return;
        TowerPlan plan = b.Plan;

        UpdateViewSize(b);
        UpdateCamera(b);

        int employees = TowerBuilder.Employees;
        if (employees != seatedEmployees || b.PlanVersion != seatedVersion)
        {
            seatedEmployees = employees;
            seatedVersion = b.PlanVersion;
            plan.CountSeated(employees, seated);
        }
        int staffKey = StaffKey(employees, b.TotalCapacity);
        // Line widths and edges follow the screen pixel: redraw what is drawn on the campus at a new scale, once the zoom has
        // settled (the floors only redraw on a plan or staff change, the overlay on a state change, the cables when a car moves)
        float scale = (canvas != null ? canvas.scaleFactor : 1f) * (ZoomSettled ? zoom : drawnZoom);
        if (!Mathf.Approximately(scale, drawnScale))
        {
            drawnScale = scale;
            drawnZoom = ZoomSettled ? zoom : drawnZoom;
            DrawGrid();
            drawnStructureKey = -1;
            foreach (BuildingDrawing d in drawings)
                for (int i = 0; i < d.floorKeys.Count; i++) d.floorKeys[i] = 0;
            drawnPlanVersion = drawnStateVersion = -1;
            drawnCablesKey = float.NaN;
        }

        SyncBuildingDrawings(plan);
        int structureKey = StructureKey(plan);
        if (structureKey != drawnStructureKey)
        {
            drawnStructureKey = structureKey;
            DrawStructure(plan);
            drawnCablesKey = float.NaN;
        }
        if (b.PlanVersion != drawnPlanVersion || staffKey != drawnStaffKey)
        {
            drawnPlanVersion = b.PlanVersion;
            drawnStaffKey = staffKey;
            DrawFloors(b, employees);
            RebuildLabels(b);
            visibilityKey = int.MinValue;
        }
        UpdateFloorVisibility(plan);
        UpdateLifts(b);

        if (b.StateVersion != drawnStateVersion)
        {
            drawnStateVersion = b.StateVersion;
            DrawOverlay(b);
        }

        RefreshTopBar(b, employees);
        RefreshTray(b);
        RefreshHints(b);
        RefreshScrollControl(b);
        RefreshCampusBar(b);
        RefreshChips(b);
        RefreshDrawer(b);
        RefreshClipboard(b);
        RefreshRoomView(b);
        RefreshSheet(b);
        RefreshFloorSlip(b);
        RefreshSellSlip(b);
        RefreshIntro(b);
        RefreshPurchaseSlip(b);
        UpdateMessage();
    }

    static int StaffKey(int employees, int capacity)
    {
        return Math.Min(employees, capacity + 1) * 7919 + (employees > 0 ? 1 : 0);
    }

    void UpdateViewSize(TowerBuilder b)
    {
        Vector2 size = root != null ? root.rect.size : Vector2.zero;
        if (size == viewSize) return;
        viewSize = size;
        DrawGrid();
        ResetPanelKeys();
    }

    float ScreenHeight => viewSize.y > 1f ? viewSize.y : 1080f;
    /// <summary>Visible width in design pixels: less than 1920 on a 4:3 or 5:4 screen (1663 at 1440 x 1080), more on a wide one.</summary>
    float ScreenWidth => viewSize.x > 1f ? viewSize.x : 1920f;

    // ------------------------------------------------------------------
    // The 3D camera under the tower
    // ------------------------------------------------------------------

    /// <summary>
    /// HDRP would still render the whole office under the opaque tower: switch the main camera off while the screen covers
    /// everything, and back on as soon as it doesn't. Only when it's the one camera running (with no camera left, the
    /// engine draws the overlay canvases itself; with others running, HDRP might not draw them at all).
    /// </summary>
    void UpdateWorldCamera()
    {
#if UNITY_EDITOR
        // With no camera on, the Editor's Game view prints "No cameras rendering" over build mode: keep it on there
        if (!hideCameraInEditor)
        {
            RestoreWorldCamera();
            return;
        }
#endif
        if (!CoversScreen())
        {
            RestoreWorldCamera();
            return;
        }
        if (hiddenWorldCamera != null)
        {
            if (Camera.allCamerasCount > 0) RestoreWorldCamera();   // another camera came on: see above
            return;
        }
        Camera cam = Camera.main;
        if (cam == null || !cam.isActiveAndEnabled || cam.targetTexture != null || Camera.allCamerasCount != 1) return;
        cam.enabled = false;
        hiddenWorldCamera = cam;
    }

    /// <summary>The opaque blueprint fills an overlay canvas that is on screen.</summary>
    bool CoversScreen()
    {
        return isActiveAndEnabled && IsShowing && (rootGroup == null || rootGroup.alpha >= 0.99f)
               && canvas != null && canvas.isActiveAndEnabled && canvas.renderMode == RenderMode.ScreenSpaceOverlay
               && towerSurface != null && towerSurface.isActiveAndEnabled && towerSurface.color.a >= 0.99f;
    }

    /// <summary>Switch the camera this view switched off back on (never leaves it off).</summary>
    void RestoreWorldCamera()
    {
        Camera cam = hiddenWorldCamera;
        hiddenWorldCamera = null;
        if (cam != null && !cam.enabled) cam.enabled = true;
    }

    // ------------------------------------------------------------------
    // Pointer
    // ------------------------------------------------------------------

    Camera EventCamera => canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;

    /// <summary>Screen point to design pixels of the campus (x right, y down, street level at 880), at the current camera.</summary>
    public bool TryGetTowerPoint(Vector2 screen, out Vector2 design)
    {
        design = default;
        if (towerContent == null) return false;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(towerContent, screen, EventCamera, out Vector2 local)) return false;
        design = new Vector2(local.x + 960f, TowerGeo.Ground - local.y);
        return true;
    }

    /// <summary>The top-most UI element under the mouse (cached per frame).</summary>
    public PointerTarget GetPointerTarget()
    {
        if (pointerFrame == Time.frameCount) return pointerTarget;
        pointerFrame = Time.frameCount;
        pointerTarget = FindPointerTarget();
        return pointerTarget;
    }

    PointerTarget FindPointerTarget()
    {
        if (!IsVisible || towerSurface == null) return PointerTarget.None;
        EventSystem system = EventSystem.current;
        if (system == null) return PointerTarget.None;
        if (pointerData == null || pointerSystem != system)
        {
            pointerData = new PointerEventData(system);
            pointerSystem = system;
        }
        pointerData.position = Input.mousePosition;
        raycastResults.Clear();
        system.RaycastAll(pointerData, raycastResults);
        if (raycastResults.Count == 0) return PointerTarget.None;
        return raycastResults[0].gameObject == towerSurface.gameObject ? PointerTarget.Tower : PointerTarget.Ui;
    }

    /// <summary>Design point to towerContent's local position.</summary>
    public static Vector2 ToContent(float x, float y)
    {
        return new Vector2(x - 960f, TowerGeo.Ground - y);
    }

    // ------------------------------------------------------------------
    // Drawing the campus
    // ------------------------------------------------------------------

    float Pixel => 1f / Mathf.Max(0.05f, drawnScale > 0f ? drawnScale : (canvas != null ? canvas.scaleFactor : 1f));

    void Begin(BlueprintGraphic layer, out BlueprintMeshBuilder m)
    {
        m = layer.Mesh;
        m.Clear();
        m.Feather = Pixel;
        m.MiterLimit = 3f;
        // Design pixels (y down) with the origin at the layer's pivot = street level under the middle of the HQ
        m.SetYDownFrame(new Vector2(-960f, TowerGeo.Ground));
    }

    /// <summary>Move the builder's frame right by <paramref name="dx"/> design pixels (pop it with m.PopFrame()).</summary>
    static void Shift(BlueprintMeshBuilder m, float dx)
    {
        m.PushFrame();
        m.SetFrame(m.Map(new Vector2(dx, 0f)), m.MapDir(new Vector2(1f, 0f)), m.MapDir(new Vector2(0f, 1f)));
    }

    void DrawGrid()
    {
        if (gridLayer == null) return;
        BlueprintMeshBuilder m = gridLayer.Mesh;
        m.Clear();
        m.Feather = 1f / Mathf.Max(0.05f, canvas != null ? canvas.scaleFactor : 1f);
        float width = Mathf.Max(1920f, viewSize.x) + 192f, height = Mathf.Max(1080f, viewSize.y) + 192f;
        m.SetYDownFrame(Vector2.zero);
        TowerPainter.DrawGrid(m, new Rect(0f, 0f, width, height), m.Feather);
        gridLayer.Commit();
    }

    int StructureKey(TowerPlan plan)
    {
        unchecked
        {
            int k = 17;
            foreach (TowerBuilding building in plan.Buildings)
                k = k * 31 + building.Id * 7 + building.Floors * 131 + building.TilesPerFloor;
            foreach (TowerConnection link in plan.Connections)
                k = k * 31 + link.Id * 17 + link.Floor;
            k = k * 31 + Mathf.RoundToInt(Mathf.Max(1920f, viewSize.x));
            return k == 0 ? 1 : k;
        }
    }

    /// <summary>The street under the whole campus, every building's walls, lift shaft and roof, and the skybridges.</summary>
    void DrawStructure(TowerPlan plan)
    {
        if (structureLayer == null) return;
        Begin(structureLayer, out BlueprintMeshBuilder m);
        float half = Mathf.Max(1920f, viewSize.x) * 0.5f + 200f;
        float left = Mathf.Min(960f - half, TowerGeo.CampusLeft(plan) - half), right = Mathf.Max(960f + half, TowerGeo.CampusRight(plan) + half);
        TowerPainter.DrawStreet(m, left, right);
        foreach (TowerBuilding building in plan.Buildings)
        {
            Shift(m, TowerGeo.OriginX(plan, building));
            TowerPainter.DrawBuilding(m, building.Type, building.Floors, building.TilesPerFloor, building.IsHeadquarters);
            m.PopFrame();
        }
        foreach (TowerConnection link in plan.Connections)
            TowerPainter.DrawBridge(m, TowerGeo.BridgeRect(plan, link.Left, link.Floor), link.Floor);
        structureLayer.Commit();
    }

    /// <summary>One drawing per building (in slot order), made when a building is bought, dropped when a load removed one.</summary>
    void SyncBuildingDrawings(TowerPlan plan)
    {
        if (floorLayer == null) return;
        IReadOnlyList<TowerBuilding> buildings = plan.Buildings;
        for (int i = 0; i < buildings.Count; i++)
        {
            if (i < drawings.Count && drawings[i].building == buildings[i]) continue;
            if (i >= drawings.Count) drawings.Add(NewDrawing(i));
            BuildingDrawing d = drawings[i];
            d.building = buildings[i];
            for (int k = 0; k < d.floorKeys.Count; k++) d.floorKeys[k] = 0;
            d.carY = float.NaN;
            d.visibleLow = d.visibleHigh = -1;
        }
        for (int i = drawings.Count - 1; i >= buildings.Count; i--)
        {
            BuildingDrawing d = drawings[i];
            if (d.root != null) Destroy(d.root.gameObject);
            if (d.car != null && d.car != liftCar) Destroy(d.car.gameObject);
            drawings.RemoveAt(i);
        }
        for (int i = 0; i < drawings.Count; i++)
        {
            BuildingDrawing d = drawings[i];
            float origin = TowerGeo.OriginX(plan, d.building);
            if (d.root != null) BlueprintUI.SetAnchoredPosition(d.root, new Vector2(origin, 0f));
        }
    }

    BuildingDrawing NewDrawing(int index)
    {
        var d = new BuildingDrawing();
        d.root = TowerViewFactory.Point(BlueprintUI.NewRect("Building " + (index + 1), floorLayer));
        if (index == 0 || liftCar == null) d.car = liftCar;
        else
        {
            d.car = Instantiate(liftCar, liftCar.parent, false);
            d.car.name = "LiftCar " + (index + 1);
        }
        return d;
    }

    void DrawFloors(TowerBuilder b, int employees)
    {
        if (floorLayer == null) return;
        TowerPlan plan = b.Plan;
        bool staffed = employees > 0;
        bool full = staffed && employees >= b.TotalCapacity;
        foreach (BuildingDrawing d in drawings)
        {
            TowerBuilding building = d.building;
            while (d.floors.Count < building.Floors)
            {
                BlueprintGraphic g = TowerViewFactory.Layer(d.root, "Floor " + (d.floors.Count + 1));
                d.floors.Add(g);
                d.floorKeys.Add(0);
            }
            for (int i = 0; i < d.floors.Count; i++)
            {
                int floor = i + 1;
                if (floor > building.Floors)
                {
                    if (d.floors[i].gameObject.activeSelf) d.floors[i].gameObject.SetActive(false);
                    d.floorKeys[i] = 0;
                    continue;
                }
                scratchRooms.Clear();
                plan.GetRoomsOn(building, floor, scratchRooms);
                int key = FloorKey(floor, building.TilesPerFloor, scratchRooms, staffed, full);
                if (key == d.floorKeys[i]) continue;
                d.floorKeys[i] = key;
                Begin(d.floors[i], out BlueprintMeshBuilder m);
                TowerPainter.DrawFloor(m, floor, building.TilesPerFloor, scratchRooms, seated, staffed, full, null);
                d.floors[i].Commit();
            }
        }
        scratchRooms.Clear();
    }

    int FloorKey(int floor, int tiles, List<TowerRoom> rooms, bool staffed, bool full)
    {
        unchecked
        {
            int k = floor * 131 + tiles;
            k = k * 31 + (staffed ? 1 : 0);
            k = k * 31 + (full ? 1 : 0);
            foreach (TowerRoom room in rooms)
            {
                k = k * 31 + room.Id;
                k = k * 31 + room.Tile;
                k = k * 31 + room.Level;
                k = k * 31 + room.Definition.GetHashCode();
                k = k * 31 + (seated.TryGetValue(room, out int n) ? n : 0);
            }
            return k == 0 ? 1 : k;
        }
    }

    /// <summary>Floors (and buildings) off screen are switched off (their meshes stay built).</summary>
    void UpdateFloorVisibility(TowerPlan plan)
    {
        float height = ScreenHeight;
        float margin = TowerGeo.FloorHeight * zoom;
        int key = 17;
        foreach (BuildingDrawing d in drawings)
        {
            TowerBuilding building = d.building;
            float left = ScreenX(TowerGeo.SpanLeft(plan, building)), right = ScreenX(TowerGeo.SpanRight(plan, building));
            bool onScreen = right >= -200f && left <= ScreenWidth + 200f;
            int low = int.MaxValue, high = 0;
            if (onScreen)
            {
                for (int floor = 1; floor <= building.Floors; floor++)
                {
                    float top = ScreenY(TowerGeo.TopOf(floor)), bottom = ScreenY(TowerGeo.BottomOf(floor));
                    if (bottom < -margin || top > height + margin) continue;
                    low = Mathf.Min(low, floor);
                    high = Mathf.Max(high, floor);
                }
            }
            unchecked { key = key * 31 + low * 7 + high * 131 + (onScreen ? 1 : 0) + (zoom < 0.34f ? 3 : 0); }
            if (low == d.visibleLow && high == d.visibleHigh && onScreen == d.onScreen) continue;
            d.visibleLow = low;
            d.visibleHigh = high;
            d.onScreen = onScreen;
            for (int i = 0; i < d.floors.Count; i++)
            {
                int floor = i + 1;
                bool on = onScreen && floor <= building.Floors && floor >= low && floor <= high;
                if (d.floors[i].gameObject.activeSelf != on) d.floors[i].gameObject.SetActive(on);
            }
            if (d.car != null && d.car.gameObject.activeSelf != onScreen) d.car.gameObject.SetActive(onScreen);
        }
        if (key == visibilityKey) return;
        visibilityKey = key;
        UpdateLabelVisibility();
    }

    /// <summary>Every building's lift car rides to the floor of its selected / hovered room (or the ghost); the others stay.</summary>
    void UpdateLifts(TowerBuilder b)
    {
        TowerPlan plan = b.Plan;
        TowerRoom focus = b.Selected ?? b.HoverRoom;
        float cablesKey = 0f;
        foreach (BuildingDrawing d in drawings)
        {
            TowerBuilding building = d.building;
            int floor = -1;
            if (focus != null && focus.Building == building) floor = focus.Floor;
            else if (b.GhostVisible && b.GhostBuilding == building) floor = b.GhostFloor;
            float target = floor > 0 ? TowerGeo.CarTop(Mathf.Clamp(floor, 1, building.Floors))
                         : float.IsNaN(d.carY) ? TowerGeo.CarTop(1) : Mathf.Max(d.carY, TowerGeo.CarTop(building.Floors));
            if (float.IsNaN(d.carY)) d.carY = target;
            d.carY = Mathf.Abs(d.carY - target) < 0.5f ? target : Mathf.Lerp(d.carY, target, 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime));
            float origin = TowerGeo.OriginX(plan, building);
            if (d.car != null) BlueprintUI.SetAnchoredPosition(d.car, ToContent(origin + TowerGeo.CarLeft, d.carY));
            cablesKey = cablesKey * 1.37f + d.carY + building.Floors * 1000f + origin;
        }
        if (cablesLayer != null && (float.IsNaN(drawnCablesKey) || Mathf.Abs(drawnCablesKey - cablesKey) > 0.25f))
        {
            drawnCablesKey = cablesKey;
            Begin(cablesLayer, out BlueprintMeshBuilder m);
            foreach (BuildingDrawing d in drawings)
            {
                if (float.IsNaN(d.carY)) continue;
                Shift(m, TowerGeo.OriginX(plan, d.building));
                TowerPainter.DrawCables(m, d.building.Floors, d.carY);
                m.PopFrame();
            }
            cablesLayer.Commit();
        }
    }

    void DrawOverlay(TowerBuilder b)
    {
        if (overlayLayer == null) return;
        TowerPlan plan = b.Plan;
        Begin(overlayLayer, out BlueprintMeshBuilder m);
        bool towerMode = b.InsideRoom == null;

        // Add-floor slots over every roof that can still grow
        if (towerMode && b.Pick == null && b.Carried == null && b.PickLink == null)
        {
            foreach (TowerBuilding building in plan.Buildings)
            {
                if (building.IsTopFloorReached) continue;
                Shift(m, TowerGeo.OriginX(plan, building));
                TowerPainter.DrawFloorSlot(m, building.Floors, building.TilesPerFloor, b.HoverSlot && b.SlotBuilding == building,
                                           b.FloorSlipOpen && b.FloorSlipBuilding == building);
                m.PopFrame();
            }
        }

        TowerRoom hover = b.HoverRoom;
        if (towerMode && hover != null && hover != b.Selected && !b.GhostVisible)
        {
            Shift(m, TowerGeo.OriginX(plan, hover.Building));
            TowerPainter.DrawHover(m, TowerGeo.RoomRect(hover.Floor, hover.Tile, hover.Tiles), b.Tool, hover.IsFixed);
            m.PopFrame();
        }
        if (towerMode && b.Selected != null && b.Carried == null)
        {
            Shift(m, TowerGeo.OriginX(plan, b.Selected.Building));
            TowerPainter.DrawSelection(m, TowerGeo.RoomRect(b.Selected.Floor, b.Selected.Tile, b.Selected.Tiles));
            m.PopFrame();
        }
        if (towerMode && b.Carried != null)
        {
            Shift(m, TowerGeo.OriginX(plan, b.Carried.Building));
            TowerPainter.DrawMoveSource(m, TowerGeo.RoomRect(b.Carried.Floor, b.Carried.Tile, b.Carried.Tiles));
            m.PopFrame();
        }
        if (towerMode && b.GhostVisible && b.GhostBuilding != null)
        {
            TowerRoomDefinition definition = b.Carried != null ? b.Carried.Definition : b.Pick;
            if (definition != null)
            {
                Shift(m, TowerGeo.OriginX(plan, b.GhostBuilding));
                TowerPainter.DrawGhost(m, definition, b.GhostFloor, b.GhostTile, b.GhostCheck.Ok);
                m.PopFrame();
            }
        }
        if (towerMode && b.LinkGhostVisible && b.LinkGhostLeft != null)
            TowerPainter.DrawBridgeGhost(m, TowerGeo.BridgeRect(plan, b.LinkGhostLeft, Mathf.Max(1, b.LinkGhostFloor)), b.LinkGhostCheck.Ok || b.LinkGhostCheck.issue == TowerIssue.CantAfford);
        overlayLayer.Commit();
    }

    // ------------------------------------------------------------------
    // Text on the campus
    // ------------------------------------------------------------------

    /// <summary>Visibility tag of a label: the building's slot and the floor (0 = the street under it).</summary>
    static int Tag(TowerBuilding building, int floor)
    {
        return (building != null ? building.Slot : 0) * 1000 + floor;
    }

    void RebuildLabels(TowerBuilder b)
    {
        usedLabels = 0;
        usedPads = 0;
        TowerPlan plan = b.Plan;
        if (labelLayer != null)
        {
            foreach (TowerBuilding building in plan.Buildings)
            {
                float origin = TowerGeo.OriginX(plan, building);
                int tiles = building.TilesPerFloor;
                // The plate under the building ("HQ", "Annex 2") and its width
                float mid = origin + (TowerGeo.X0 + TowerGeo.Right(tiles)) * 0.5f;
                Label(L10n.ToUpper(TowerText.BuildingName(building)), mid, TowerGeo.Ground + 82f, 22f, UIColors.BlueprintInk, TextAlignmentOptions.Center,
                      new Vector2(0.5f, 0.5f), TowerGeo.SpanWidth(tiles), Tag(building, 0), FontStyles.Bold, 0.12f);
                Label(TowerText.Get("tower.tower.width", ("count", tiles)), mid, TowerGeo.Ground + 46f, 17f, UIColors.BlueprintInk, TextAlignmentOptions.Center,
                      new Vector2(0.5f, 0.5f), 100f, Tag(building, 0));

                for (int floor = 1; floor <= building.Floors; floor++)
                {
                    float top = TowerGeo.TopOf(floor);
                    Label(floor.ToString(Inv), origin + TowerGeo.NumberX + 14f, top + 78f, 17f, UIColors.BlueprintInk, TextAlignmentOptions.Center,
                          new Vector2(0.5f, 0.5f), 28f, Tag(building, floor));

                    scratchRooms.Clear();
                    plan.GetRoomsOn(building, floor, scratchRooms);
                    float roomTop = TowerGeo.RoomTop(floor);
                    foreach (TowerRoom room in scratchRooms)
                    {
                        string name = TowerText.RoomName(room);
                        if (room.Definition.HasLevels && room.Level > 1) name = TowerText.Get("tower.tower.room_level", ("room", name), ("level", room.Level));
                        Label(name, origin + TowerGeo.RoomLeft(room.Tile) + 36f, roomTop + 18f, 19f, room.Definition.ink, TextAlignmentOptions.Left,
                              new Vector2(0f, 0.5f), TowerGeo.RoomWidth(room.Tiles) - 44f, Tag(building, floor));
                    }
                    // Free runs: "empty" / "empty · 2 tiles" on a blueprint pad
                    int t = 0;
                    while (t < tiles)
                    {
                        TowerRoom occupant = plan.RoomAt(building, floor, t);
                        if (occupant != null)
                        {
                            t = occupant.EndTile;
                            continue;
                        }
                        int end = t;
                        while (end + 1 < tiles && plan.RoomAt(building, floor, end + 1) == null) end++;
                        int count = end - t + 1;
                        float padMid = origin + TowerGeo.RoomLeft(t) + TowerGeo.RoomWidth(count) * 0.5f;
                        Pad(count == 1 ? TowerText.Get("tower.tower.empty") : TowerText.Get("tower.tower.empty.tiles", ("count", count)), padMid, roomTop + 66f,
                            Tag(building, floor));
                        t = end + 1;
                    }
                }
            }
            scratchRooms.Clear();
        }
        for (int i = usedLabels; i < labels.Count; i++)
            if (labels[i].gameObject.activeSelf) labels[i].gameObject.SetActive(false);
        for (int i = usedPads; i < pads.Count; i++)
            if (pads[i].gameObject.activeSelf) pads[i].gameObject.SetActive(false);
        labelBuildings = plan.BuildingCount;
    }

    int labelBuildings;

    void Label(string text, float x, float y, float size, Color color, TextAlignmentOptions alignment, Vector2 pivot, float width, int tag,
               FontStyles style = FontStyles.Normal, float spacingEm = 0f)
    {
        TextMeshProUGUI label;
        if (usedLabels < labels.Count)
        {
            label = labels[usedLabels];
        }
        else
        {
            label = BlueprintUI.Text(labelLayer, "Label", "", size, color, ResolveFont(), alignment);
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            label.overflowMode = TextOverflowModes.Ellipsis;
            L10nFont.Attach(label);
            labels.Add(label);
        }
        usedLabels++;
        label.name = "Label " + tag;
        if (label.text != text) label.text = text;
        label.fontSize = size;
        label.color = color;
        label.alignment = alignment;
        label.fontStyle = style;
        label.characterSpacing = spacingEm * 100f;
        RectTransform rect = label.rectTransform;
        rect.pivot = pivot;
        rect.sizeDelta = new Vector2(Mathf.Max(8f, width), size * 1.6f);
        rect.anchoredPosition = ToContent(x, y);
        label.gameObject.SetActive(true);
        SetTag(label.gameObject, tag);
    }

    void Pad(string text, float x, float y, int tag)
    {
        RectTransform pad;
        if (usedPads < pads.Count)
        {
            pad = pads[usedPads];
        }
        else
        {
            pad = TowerViewFactory.EmptyPad(labelLayer, ResolveFont());
            L10nFont.AttachAll(pad);
            pads.Add(pad);
        }
        usedPads++;
        pad.name = "Empty " + tag;
        TextMeshProUGUI text3 = pad.GetComponentInChildren<TextMeshProUGUI>(true);
        if (text3 != null && text3.text != text) text3.text = text;
        pad.anchoredPosition = ToContent(x, y);
        pad.gameObject.SetActive(true);
        SetTag(pad.gameObject, tag);
    }

    readonly Dictionary<GameObject, int> labelTags = new Dictionary<GameObject, int>();

    void SetTag(GameObject go, int tag)
    {
        labelTags[go] = tag;
    }

    void UpdateLabelVisibility()
    {
        for (int i = 0; i < usedLabels; i++) ShowByTag(labels[i].gameObject);
        for (int i = 0; i < usedPads; i++) ShowByTag(pads[i].gameObject);
    }

    void ShowByTag(GameObject go)
    {
        bool on = false;
        if (labelTags.TryGetValue(go, out int tag))
        {
            int slot = tag / 1000, floor = tag % 1000;
            if (slot >= 0 && slot < drawings.Count)
            {
                BuildingDrawing d = drawings[slot];
                on = d.onScreen && (floor == 0 || (floor >= d.visibleLow && floor <= d.visibleHigh));
                // Room names get too small to read far out: the plates stay
                if (floor > 0 && zoom < 0.34f) on = false;
            }
        }
        if (go.activeSelf != on) go.SetActive(on);
    }

    // ------------------------------------------------------------------
    // Top bar, tool tray, title block, hints, scroll control
    // ------------------------------------------------------------------

    void RefreshTopBar(TowerBuilder b, int employees)
    {
        TowerPlan plan = b.Plan;
        CompanyManager company = CompanyManager.Instance;
        double value = company != null ? company.companyValue : 0.0;
        int capacity = b.TotalCapacity;
        TowerTotals totals = b.GetDailyTotals();
        if (Time.unscaledTime >= nextNameCheck)
        {
            nextNameCheck = Time.unscaledTime + 1f;
            string companyName = CompanyNameUI.GetDisplayName();   // localization (2026-09-29): the default name in the player's language
            if (companyName != shownCompany)
            {
                shownCompany = companyName;
                topKey = 0;
            }
        }
        int key = Hash(plan.TotalFloors * 31 + plan.BuildingCount, employees, capacity, plan.Budget.GetHashCode(), Hash(value.GetHashCode(), totals.upkeep.GetHashCode(), 0, 0, 0));
        if (key == topKey) return;
        topKey = key;

        string company3 = string.IsNullOrEmpty(shownCompany) ? TowerText.Get("tower.company.fallback") : shownCompany;
        SetText(topBar.companyText, plan.BuildingCount > 1
            ? TowerText.Get("tower.top.company.campus", ("company", company3), ("count", plan.BuildingCount))
            : TowerText.Get("tower.top.company", ("company", company3), ("count", plan.Floors)));
        SetText(topBar.budgetText, UIFormat.Money(plan.Budget));
        SetText(topBar.valueText, UIFormat.Money(value));
        bool full = employees >= capacity;
        bool over = employees > capacity;
        string count = "<b>" + TowerText.EmployeesOf(employees, capacity) + "</b>";
        SetText(topBar.seatsText, full ? TowerText.Get("tower.top.full", ("employees", count)) : TowerText.Get("tower.top.employees", ("employees", count)));
        if (topBar.seatsText != null) topBar.seatsText.color = full ? UIColors.Loss : UIColors.Ink;
        if (topBar.seatsIcon != null)
        {
            topBar.seatsIcon.Kind = full ? TowerIconKind.Warning : TowerIconKind.People;
            topBar.seatsIcon.SetColor(full ? UIColors.Loss : UIColors.Ink);
        }
        if (topBar.seatsBox != null) topBar.seatsBox.Fill = over ? UIColors.LossSoft : full ? UIColors.Hex(0xFBE6D8) : UIColors.PaperHi;
        SetText(topBar.upkeepText, TowerText.Get("tower.top.upkeep", ("amount", TowerText.Upkeep(totals.upkeep))));

        // The title block's name is in spaced capitals (L10n.ToUpper keeps Turkish İ / ı right)
        SetText(titleBlockName, L10n.ToUpper(TowerText.Get("tower.title_block.name", ("company", company3))));
        SetText(titleBlockInfo, TowerText.Get("tower.title_block.info.campus", ("buildings", plan.BuildingCount), ("floors", plan.TotalFloors),
                                              ("staff", TowerText.EmployeesOf(employees, capacity))));
    }

    void RefreshTray(TowerBuilder b)
    {
        int key = Hash((int)b.Tool, b.FloorSlipOpen ? 1 : 0, b.InsideRoom != null ? 1 : 0, 0, 0);
        if (key == trayKey) return;
        trayKey = key;
        if (toolBase == null || toolBase.Length != toolButtons.Length)
        {
            toolBase = new Vector2[toolButtons.Length];
            for (int i = 0; i < toolButtons.Length; i++)
                if (toolButtons[i] != null) toolBase[i] = ((RectTransform)toolButtons[i].transform).anchoredPosition;
        }
        for (int i = 0; i < toolButtons.Length; i++)
        {
            TowerTool tool = (TowerTool)(i + 1);
            bool pressed = b.Tool == tool || (tool == TowerTool.AddFloor && b.FloorSlipOpen);
            StyleToolButton(toolButtons[i], pressed, toolBase[i]);
        }
    }

    /// <summary>The pressed tool is highlighter yellow, bold and pushed into its shadow (moved 3, 4 px).</summary>
    static void StyleToolButton(Button button, bool pressed, Vector2 basePosition)
    {
        if (button == null) return;
        BlueprintBox box = button.GetComponent<BlueprintBox>();
        if (box != null)
        {
            box.Fill = pressed ? UIColors.Highlight : UIColors.PaperHi;
            box.SetShadow(pressed ? Color.clear : BlueprintUI.InkShadow, new Vector2(3f, 4f));
        }
        BlueprintUI.SetAnchoredPosition((RectTransform)button.transform, pressed ? basePosition + new Vector2(3f, -4f) : basePosition);
        foreach (TextMeshProUGUI text in button.GetComponentsInChildren<TextMeshProUGUI>(true))
            if (text.name == "Label") text.fontStyle = pressed ? FontStyles.Bold : FontStyles.Normal;
    }

    void RefreshHints(TowerBuilder b)
    {
        // Which hint (no strings are made unless it changes)
        int mode = 0;
        if (b.InsideRoom == null && !b.UpgradeOpen && !b.IntroOpen && !b.PurchaseOpen)
        {
            if (b.Carried != null) mode = 1;
            else if (b.Pick != null) mode = 2;
            else if (b.PickLink != null) mode = 9;
            else if (b.Tool == TowerTool.BuildRoom) mode = 3;
            else if (b.Tool == TowerTool.Move) mode = 4;
            else if (b.Tool == TowerTool.Sell) mode = 5;
            else if (b.Tool == TowerTool.Furniture) mode = b.Selected != null ? 6 : 7;
            else if (b.Tool == TowerTool.AddFloor) mode = 8;
            else if (b.Tool == TowerTool.Campus) mode = 10;
        }
        bool show = mode != 0;
        // "Gamble for it" when what the player is placing costs more than the budget
        double shortfall = 0.0;
        if (mode == 2) shortfall = b.Plan.Shortfall(b.Plan.RoomPrice(b.Pick));
        else if (mode == 9 && b.LinkGhostVisible) shortfall = b.Plan.Shortfall(b.LinkGhostCheck.price);
        else if (mode == 8 && b.FloorSlipBuilding != null) shortfall = b.Plan.Shortfall(b.Plan.FloorPriceFor(b.FloorSlipBuilding));
        bool gambleForIt = shortfall >= 0.5;
        int floors = b.FloorSlipBuilding != null ? b.FloorSlipBuilding.Floors : b.Plan.Floors;
        int key = show ? Hash(mode, floors, gambleForIt ? shortfall.GetHashCode() : 0, b.FloorSlipBuilding != null ? b.FloorSlipBuilding.Id : 0, b.PlanVersion) : -1;
        if (key == hintKey) return;
        hintKey = key;
        if (hintStrip.strip != null && hintStrip.strip.gameObject.activeSelf != show) hintStrip.strip.gameObject.SetActive(show);
        if (!show) return;

        string click, note;
        switch (mode)
        {
            case 1: click = TowerText.Get("tower.hint.drop"); note = TowerText.Get("tower.hint.drop.note"); break;
            case 2: click = TowerText.Get("tower.hint.build"); note = TowerText.Get("tower.hint.snap.note"); break;
            case 3: click = TowerText.Get("tower.hint.pick"); note = TowerText.Get("tower.hint.snap.note"); break;
            case 4: click = TowerText.Get("tower.hint.move"); note = TowerText.Get(b.Plan.BuildingCount > 1 ? "tower.hint.move.note.campus" : "tower.hint.move.note"); break;
            case 5:
                click = TowerText.Get("tower.hint.sell", ("percent", TowerText.Percent(b.Rules.sellRefundShare * 100.0)));
                note = TowerText.Get(b.Plan.StarterRoom != null ? "tower.hint.sell.note.campus" : "tower.hint.sell.note");
                break;
            case 6:
            case 7:
                click = TowerText.Get(mode == 6 ? "tower.hint.furnish.other" : "tower.hint.furnish");
                note = TowerText.Get("tower.hint.furnish.note", ("amount", TowerText.PlusPerDay(b.Rules.incomePerLevel)));
                break;
            case 9:
                click = TowerText.Get("tower.hint.link");
                note = TowerText.Get("tower.hint.link.note");
                break;
            case 10:
                click = TowerText.Get("tower.hint.campus");
                note = TowerText.Get("tower.hint.campus.note");
                break;
            default:
                TowerBuilding building = b.FloorSlipBuilding ?? b.Plan.Headquarters;
                click = TowerText.Get("tower.hint.slip");
                note = TowerText.Get("tower.hint.slip.note", ("floor", building.Floors + 1), ("amount", UIFormat.Money(b.Plan.FloorPriceFor(building))));
                break;
        }
        if (gambleForIt) note = TowerText.Get("tower.hint.short", ("amount", UIFormat.Money(shortfall)));
        SetText(hintStrip.clickText, click);
        SetText(hintStrip.noteText, note);
        if (hintStrip.noteText != null) hintStrip.noteText.color = gambleForIt ? UIColors.Loss : UIColors.InkSoft;
        if (hintStrip.gambleButton != null && hintStrip.gambleButton.gameObject.activeSelf != gambleForIt)
            hintStrip.gambleButton.gameObject.SetActive(gambleForIt);
        if (hintStrip.strip != null) LayoutRebuilder.ForceRebuildLayoutImmediate(hintStrip.strip);
    }

    void RefreshScrollControl(TowerBuilder b)
    {
        bool show = maxScroll > 1f && b.InsideRoom == null && !b.UpgradeOpen;
        TowerBuilding building = b.FocusBuilding ?? b.Plan.Headquarters;
        BuildingDrawing d = building.Slot < drawings.Count ? drawings[building.Slot] : null;
        int low = Mathf.Max(1, d != null ? d.visibleLow : 1), high = Mathf.Min(building.Floors, d != null ? d.visibleHigh : building.Floors);
        // Only floors mostly on screen count (between the top bar and the tool tray)
        for (int floor = low; floor <= high; floor++)
        {
            if (ScreenY(TowerGeo.BottomOf(floor)) - 40f * zoom > TopBarHeight) break;
            low = floor + 1;
        }
        for (int floor = high; floor >= low; floor--)
        {
            if (ScreenY(TowerGeo.TopOf(floor)) + 40f * zoom < ScreenHeight - 110f) break;
            high = floor - 1;
        }
        int key = show ? Hash(low, high, building.Floors, building.Id, 1) : -1;
        if (key == scrollKey) return;
        scrollKey = key;
        if (scrollControl.panel != null && scrollControl.panel.gameObject.activeSelf != show) scrollControl.panel.gameObject.SetActive(show);
        if (!show) return;
        SetText(scrollControl.rangeText, high > low ? TowerText.Get("tower.scroll.range", ("low", low), ("high", high))
                                                    : TowerText.Get("tower.scroll.single", ("floor", Mathf.Max(Mathf.Max(1, low), Mathf.Min(high, building.Floors)))));
        SetText(scrollControl.totalText, TowerText.Get("tower.scroll.total", ("count", building.Floors)));
    }

    // ------------------------------------------------------------------
    // Chips on the campus: selected tag, ghost tag and tooltip, add-floor slot
    // ------------------------------------------------------------------

    void RefreshChips(TowerBuilder b)
    {
        TowerPlan plan = b.Plan;
        bool towerMode = b.InsideRoom == null;
        TowerRoom selected = towerMode && b.Carried == null ? b.Selected : null;
        TowerRoomDefinition ghostRoom = b.Carried != null ? b.Carried.Definition : b.Pick;
        bool ghost = towerMode && b.GhostVisible && ghostRoom != null && b.GhostBuilding != null;
        bool link = towerMode && b.LinkGhostVisible && b.PickLink != null && b.LinkGhostLeft != null;
        // The slot's chips follow the slot under the pointer, else the slip's building, else the building in view
        TowerBuilding slotBuilding = b.HoverSlot && b.SlotBuilding != null ? b.SlotBuilding : b.FloorSlipOpen && b.FloorSlipBuilding != null ? b.FloorSlipBuilding
                                   : b.FocusBuilding ?? plan.Headquarters;
        bool slot = towerMode && !slotBuilding.IsTopFloorReached && b.Pick == null && b.Carried == null && b.PickLink == null;
        bool slotHot = slot && ((b.HoverSlot && b.SlotBuilding == slotBuilding) || (b.FloorSlipOpen && b.FloorSlipBuilding == slotBuilding));
        TowerCheck check = link ? b.LinkGhostCheck : b.GhostCheck;
        Vector2 pointer = b.PointerPoint;

        int key = Hash(selected != null ? selected.Id * 17 + selected.Floor * 3 + selected.Tile + selected.Building.Slot * 7919 : 0,
                       ghost ? Hash(b.GhostFloor, b.GhostTile, (int)check.issue, check.tilesLeft, check.blocker != null ? check.blocker.Id : 0) + b.GhostBuilding.Id
                             : link ? Hash(b.LinkGhostFloor, b.LinkGhostLeft.Id, (int)check.issue, check.price.GetHashCode(), 7) : 0,
                       Hash(slot ? 1 : 0, slotHot ? 1 : 0, slotBuilding.Floors * 31 + slotBuilding.Id, plan.Budget.GetHashCode(), ghostRoom != null ? ghostRoom.GetHashCode() : 0),
                       (ghost || link) && !check.Ok ? Mathf.RoundToInt(pointer.x) * 7 + Mathf.RoundToInt(pointer.y) : 0,
                       Hash(b.Carried != null ? 1 : 0, Mathf.RoundToInt(zoom * 1000f), plan.BuildingCount, b.PlanVersion, 0));
        if (key == chipsKey) return;
        chipsKey = key;
        Vector3 chipScale = ChipScale;

        // "Selected" tag inside the room's top-right corner
        if (chips.selectedTag != null)
        {
            bool show = selected != null;
            if (chips.selectedTag.gameObject.activeSelf != show) chips.selectedTag.gameObject.SetActive(show);
            if (show)
            {
                float origin = TowerGeo.OriginX(plan, selected.Building);
                float right = TowerGeo.RoomLeft(selected.Tile) + TowerGeo.RoomWidth(selected.Tiles);
                chips.selectedTag.anchoredPosition = ToContent(origin + right - 144f, TowerGeo.RoomTop(selected.Floor) + 6f);
            }
        }

        // Ghost tag 50 px above the ghost; the reason next to the pointer when it doesn't fit
        if (chips.ghostTag != null)
        {
            bool show = ghost || link;
            if (chips.ghostTag.gameObject.activeSelf != show) chips.ghostTag.gameObject.SetActive(show);
            if (show)
            {
                string text;
                if (check.Ok) text = link ? TowerText.Get("tower.chip.place", ("price", UIFormat.Money(check.price)))
                                    : b.Carried != null ? TowerText.Get("tower.chip.move_here") : TowerText.Get("tower.chip.place", ("price", UIFormat.Money(check.price)));
                else if (check.issue == TowerIssue.CantAfford) text = TowerText.Get("tower.chip.needs_more", ("amount", UIFormat.Money(check.price - plan.Budget)));
                else if (check.issue == TowerIssue.Locked) text = TowerText.Get("tower.issue.locked");
                else text = TowerText.Get(link ? "tower.chip.cant_link" : "tower.chip.doesnt_fit");
                SetText(chips.ghostTagText, text);
                if (chips.ghostTagIcon != null)
                {
                    chips.ghostTagIcon.Kind = check.Ok ? TowerIconKind.Check : TowerIconKind.Warning;
                    chips.ghostTagIcon.SetColor(check.Ok ? UIColors.Money : UIColors.Loss);
                }
                if (chips.ghostTagBox != null) chips.ghostTagBox.Fill = check.Ok ? UIColors.MoneySoft : UIColors.LossSoft;
                if (link)
                {
                    Rect bridge = TowerGeo.BridgeRect(plan, b.LinkGhostLeft, Mathf.Max(1, b.LinkGhostFloor));
                    chips.ghostTag.anchoredPosition = ToContent(bridge.x, bridge.y - 46f);
                }
                else
                {
                    float origin = TowerGeo.OriginX(plan, b.GhostBuilding);
                    chips.ghostTag.anchoredPosition = ToContent(origin + TowerGeo.RoomLeft(b.GhostTile), TowerGeo.RoomTop(b.GhostFloor) - 50f);
                }
                BlueprintUI.SetLocalScale(chips.ghostTag, chipScale);
            }
        }
        if (chips.tooltip != null)
        {
            bool show = (ghost || link) && !check.Ok && check.issue != TowerIssue.CantAfford;
            if (chips.tooltip.gameObject.activeSelf != show) chips.tooltip.gameObject.SetActive(show);
            if (show)
            {
                SetText(chips.tooltipTitle, TowerText.Get(check.issue == TowerIssue.Locked ? "tower.chip.tooltip.locked" : "tower.chip.tooltip.doesnt_fit"));
                SetText(chips.tooltipText, b.Describe(check, ghostRoom, true));   // the reason alone: the title says "Doesn't fit:"
                LayoutRebuilder.ForceRebuildLayoutImmediate(chips.tooltip);
                chips.tooltip.anchoredPosition = ToContent(pointer.x + 16f / zoom, pointer.y + 70f / zoom);
                BlueprintUI.SetLocalScale(chips.tooltip, chipScale);
            }
        }

        // The add-floor slot's text: "+ Add floor 5" and its price (hovered: the Build floor button)
        float slotOrigin = TowerGeo.OriginX(plan, slotBuilding);
        float slotTop = TowerGeo.TopOf(slotBuilding.Floors + 1);
        float slotMid = slotOrigin + (TowerGeo.X0 + TowerGeo.Right(slotBuilding.TilesPerFloor)) * 0.5f;
        double price = plan.FloorPriceFor(slotBuilding);
        bool afford = plan.CanAfford(price);
        // Demo: the HQ's roof at the demo's floor cap says the taller towers are in the full game
        bool demoRoof = !slot && DemoMode.Active && towerMode && slotBuilding.IsHeadquarters && slotBuilding.IsTopFloorReached
                        && b.Pick == null && b.Carried == null && b.PickLink == null;
        if (chips.slotLabel != null)
        {
            bool showLabel = slot || demoRoof;
            if (chips.slotLabel.gameObject.activeSelf != showLabel) chips.slotLabel.gameObject.SetActive(showLabel);
            if (DemoMode.Active && chips.slotLabel.childCount > 0)
            {
                Transform plus = chips.slotLabel.GetChild(0);   // the "+" icon
                if (chips.slotLabelText == null || plus != chips.slotLabelText.transform) plus.gameObject.SetActive(slot);
            }
            if (demoRoof)
            {
                SetText(chips.slotLabelText, DemoMode.T("demo.lock.floors"));
                chips.slotLabel.anchoredPosition = ToContent(slotMid, slotTop + 50f);
                BlueprintUI.SetLocalScale(chips.slotLabel, chipScale);
            }
            else if (slot)
            {
                SetText(chips.slotLabelText, TowerText.Get("tower.chip.add_floor", ("floor", slotBuilding.Floors + 1)));
                chips.slotLabel.anchoredPosition = ToContent(slotMid, slotTop + 50f);
                BlueprintUI.SetLocalScale(chips.slotLabel, chipScale);
            }
        }
        if (chips.slotChip != null)
        {
            bool show = slot && !slotHot;
            if (chips.slotChip.gameObject.activeSelf != show) chips.slotChip.gameObject.SetActive(show);
            if (show)
            {
                SetText(chips.slotChipText, UIFormat.Money(price));
                if (chips.slotChipText != null) chips.slotChipText.color = afford ? UIColors.Ink : UIColors.Loss;
                chips.slotChip.anchoredPosition = ToContent(slotMid, slotTop + 94f);
                BlueprintUI.SetLocalScale(chips.slotChip, chipScale);
            }
        }
        if (chips.slotButton != null)
        {
            if (chips.slotButton.gameObject.activeSelf != slotHot) chips.slotButton.gameObject.SetActive(slotHot);
            if (slotHot)
            {
                SetText(chips.slotButtonText, afford ? TowerText.Get("tower.chip.build_floor", ("price", UIFormat.Money(price)))
                                                     : TowerText.Get("tower.chip.need_more", ("amount", UIFormat.Money(price - plan.Budget))));
                if (chips.slotButtonText != null) chips.slotButtonText.color = afford ? UIColors.Ink : UIColors.Pencil;
                if (chips.slotButtonBox != null)
                {
                    chips.slotButtonBox.SetStyle(afford ? UIColors.Highlight : UIColors.PaperShade, afford ? UIColors.Ink : UIColors.Pencil, 3f);
                    chips.slotButtonBox.Dashed = !afford;
                    chips.slotButtonBox.SetShadow(afford ? UIColors.Ink : Color.clear, new Vector2(5f, 6f));
                }
                if (chips.slotButtonIcon != null) chips.slotButtonIcon.SetColor(afford ? UIColors.Ink : UIColors.Pencil);
                chips.slotButton.anchoredPosition = ToContent(slotMid, slotTop + 100f);
                BlueprintUI.SetLocalScale(chips.slotButton, chipScale);
            }
        }
        // The width under the street is a label per building now (RebuildLabels)
        if (chips.dimensionText != null && chips.dimensionText.gameObject.activeSelf) chips.dimensionText.gameObject.SetActive(false);
    }

    // ------------------------------------------------------------------
    // Left drawer: room catalog (Build room), furniture (Furniture), campus (Campus)
    // ------------------------------------------------------------------

    void RefreshDrawer(TowerBuilder b)
    {
        bool rooms = b.Tool == TowerTool.BuildRoom;
        bool furniture = b.Tool == TowerTool.Furniture;
        bool campus = b.Tool == TowerTool.Campus;
        bool show = (rooms || furniture || campus) && b.InsideRoom == null;
        TowerRoom room = furniture ? b.Selected : null;
        int unlockMask = UnlockMask(b);
        int page = rooms ? 1 : furniture ? 2 : 3;
        int key = show ? Hash(page, Hash(b.PlanVersion, b.Plan.Budget.GetHashCode(), unlockMask, b.Pick != null ? b.Pick.GetHashCode() : 0,
                                         b.PickLink != null ? b.PickLink.GetHashCode() : 0),
                              room != null ? room.Id : 0, b.SelectedSlot, TowerBuilder.Employees * 7 + catalogFilter) : -1;
        if (!show) shownDrawerPage = 0;
        if (key == drawerKey) return;
        drawerKey = key;
        if (drawer.panel != null && drawer.panel.gameObject.activeSelf != show) drawer.panel.gameObject.SetActive(show);
        if (!show) return;

        // Opening the catalog while the offices are full shows the offices first
        if (page != shownDrawerPage)
        {
            shownDrawerPage = page;
            if (rooms && b.IsFull) catalogFilter = 1;
        }
        StyleTab(drawer.roomsTab, rooms);
        StyleTab(drawer.furnitureTab, furniture);
        StyleTab(drawer.campusTab, campus);
        if (drawer.roomsPage != null) drawer.roomsPage.gameObject.SetActive(rooms);
        if (drawer.furniturePage != null) drawer.furniturePage.gameObject.SetActive(furniture);
        if (drawer.campusPage != null) drawer.campusPage.gameObject.SetActive(campus);
        if (rooms) RefreshCatalog(b);
        else if (furniture) RefreshFurnitureList(b, room);
        else RefreshCampusPage(b);
    }

    /// <summary>What is locked changes with skills, value, status, day, floors and buildings: one number to notice it.</summary>
    static int UnlockMask(TowerBuilder b)
    {
        int mask = 0, bit = 1;
        foreach (TowerRoomDefinition definition in b.Rules.rooms)
        {
            if (!definition.isFixed && b.IsRoomUnlocked(definition)) mask |= bit;
            bit = bit << 1 == 0 ? 1 : bit << 1;
        }
        unchecked
        {
            foreach (TowerBuildingType type in b.Rules.buildingTypes) mask = mask * 3 + (b.Plan.LockOf(type).IsLocked ? 1 : 2);
            foreach (TowerConnectionType type in b.Rules.connectionTypes) mask = mask * 3 + (b.Plan.LockOf(type).IsLocked ? 1 : 2);
        }
        return mask;
    }

    /// <summary>The active folder tab covers the folder's top edge (taller, folder colour); the others sit behind it.</summary>
    static void StyleTab(Button tab, bool active)
    {
        if (tab == null) return;
        BlueprintBox box = tab.GetComponent<BlueprintBox>();
        if (box != null) box.Fill = active ? UIColors.Manila : UIColors.ManilaEdge;
        RectTransform rect = (RectTransform)tab.transform;
        rect.sizeDelta = new Vector2(rect.sizeDelta.x, active ? 52f : 44f);
        rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, active ? 0f : -8f);
        if (active) rect.SetAsLastSibling();
        else rect.SetSiblingIndex(0);
        foreach (TextMeshProUGUI text in tab.GetComponentsInChildren<TextMeshProUGUI>(true))
            text.fontStyle = active ? FontStyles.Bold : FontStyles.Normal;
    }

    void RefreshCatalog(TowerBuilder b)
    {
        if (drawer.cardList == null || drawer.cardTemplate == null) return;
        TowerPlan plan = b.Plan;
        for (int i = 0; i < drawer.filterButtons.Length; i++) StyleFilter(drawer.filterButtons[i], i == catalogFilter);
        int count = 0;
        foreach (TowerRoomDefinition definition in b.Rules.rooms)
        {
            if (definition.isFixed || !InFilter(definition.category, catalogFilter)) continue;
            if (count >= cards.Count)
            {
                TowerCatalogCard card = Instantiate(drawer.cardTemplate, drawer.cardList, false);
                card.name = "Card " + (cards.Count + 1);
                TowerCatalogCard clicked = card;
                if (card.button != null) card.button.onClick.AddListener(() => WithBuilder(x => x.PickRoom(clicked.definition)));
                cards.Add(card);
            }
            TowerCatalogCard c = cards[count++];
            if (!c.gameObject.activeSelf) c.gameObject.SetActive(true);
            TowerLock lockInfo = plan.LockOf(definition);
            bool atLimit = definition.maxPerCompany > 0 && plan.CountRooms(definition) >= definition.maxPerCompany;
            string reason = lockInfo.IsLocked ? TowerText.LockText(lockInfo, definition, b.Rules)
                          : atLimit ? TowerText.Get("tower.issue.limit.company", ("count", definition.maxPerCompany)) : "";
            string right = TowerText.Get("tower.catalog.tiles_upkeep", ("count", definition.Tiles), ("upkeep", TowerText.Upkeep(plan.UpkeepIfBuilt(definition))));
            string effect = CatalogEffect(b, definition);
            double price = plan.RoomPrice(definition);   // after the manager's discount, if any
            c.Bind(definition, lockInfo.IsLocked || atLimit, plan.CanAfford(price), definition == b.Pick, effect, right, reason, price);
        }
        for (int i = count; i < cards.Count; i++)
            if (cards[i].gameObject.activeSelf) cards[i].gameObject.SetActive(false);
    }

    /// <summary>Show the room catalog on a filter (0 all, 1 offices, 2 facilities, 3 business, 4 prestige): a manager decision's "office" spot.</summary>
    public void ShowCatalogFilter(int filter)
    {
        catalogFilter = Mathf.Clamp(filter, 0, 4);
        drawerKey = 0;
    }

    /// <summary>Which catalog filter shows a category: offices; staff (facilities, executive); business (departments, research, utility, money); prestige.</summary>
    static bool InFilter(TowerRoomCategory category, int filter)
    {
        switch (filter)
        {
            case 1: return category == TowerRoomCategory.Office;
            case 2: return category == TowerRoomCategory.Facility || category == TowerRoomCategory.Executive;
            case 3: return category == TowerRoomCategory.Department || category == TowerRoomCategory.Research || category == TowerRoomCategory.Utility
                           || category == TowerRoomCategory.Money;
            case 4: return category == TowerRoomCategory.Prestige;
            default: return true;
        }
    }

    static void StyleFilter(Button button, bool active)
    {
        if (button == null) return;
        BlueprintBox box = button.GetComponent<BlueprintBox>();
        if (box != null)
        {
            box.Fill = active ? UIColors.Highlight : UIColors.PaperHi;
            box.SetShadow(active ? Color.clear : BlueprintUI.InkShadow, new Vector2(2f, 3f));
        }
        foreach (TextMeshProUGUI text in button.GetComponentsInChildren<TextMeshProUGUI>(true))
            text.fontStyle = active ? FontStyles.Bold : FontStyles.Normal;
    }

    /// <summary>A catalog card's effect line: what it does and its rules ("Room for 24 staff · 1 per building").</summary>
    static string CatalogEffect(TowerBuilder b, TowerRoomDefinition definition)
    {
        string effect = EffectText(definition);
        string rules = TowerText.RoomRules(b.Rules, definition);
        return rules.Length > 0 ? effect + TowerText.Get("tower.list.separator") + rules : effect;
    }

    void RefreshFurnitureList(TowerBuilder b, TowerRoom room)
    {
        TowerPlan plan = b.Plan;
        bool has = room != null;
        if (drawer.emptyText != null) drawer.emptyText.gameObject.SetActive(!has);
        if (drawer.roomInfoGroup != null) drawer.roomInfoGroup.gameObject.SetActive(has);
        if (drawer.itemCard != null) drawer.itemCard.gameObject.SetActive(has && b.SelectedSlot >= 0);
        BindRows(drawerRows, drawer.slotList, drawer.slotRowTemplate, b, room, true);
        if (!has) return;

        SetText(drawer.roomTitle, TowerText.RoomName(room));
        SetText(drawer.roomInfo, TowerText.Get("tower.drawer.room_info", ("floor", room.Floor), ("count", room.SlotCount),
                                               ("amount", TowerText.PlusPerDay(plan.RoomDailyIncome(room)))));

        int slot = b.SelectedSlot;
        TowerFurnitureSlot data = room.Definition.GetSlot(slot);
        if (data == null) return;
        int level = room.GetLevel(slot);
        bool max = level >= plan.MaxLevel;
        double price = plan.UpgradePrice(room, slot);
        bool afford = plan.CanAfford(price);
        if (drawer.itemIcon != null) drawer.itemIcon.Kind = TowerArt.IconFor(data.art);
        SetText(drawer.itemName, TowerText.SlotName(data));
        SetText(drawer.itemLevel, TowerText.LevelWithNumber(b.Rules, level));
        SetText(drawer.itemNext, max ? TowerText.Get("tower.drawer.top_level", ("level", TowerText.LevelName(b.Rules, level)))
                                     : TowerText.Get("tower.drawer.next", ("level", TowerText.LevelName(b.Rules, level + 1)),
                                                     ("amount", TowerText.PlusPerDay(b.Rules.incomePerLevel))));
        if (drawer.upgradeButton != null)
        {
            drawer.upgradeButton.gameObject.SetActive(!max);
            SetText(drawer.upgradeText, TowerText.Get("tower.button.upgrade", ("price", UIFormat.Money(price))));
            StylePrimary(drawer.upgradeButton, drawer.upgradeText, afford);
        }
        bool need = !max && !afford;
        if (drawer.needNote != null) drawer.needNote.gameObject.SetActive(need);
        if (need)
        {
            SetText(drawer.needTitle, TowerText.Get("tower.drawer.needs", ("amount", UIFormat.Money(price))));
            SetText(drawer.needText, TowerText.Get("tower.drawer.you_have", ("amount", UIFormat.Money(plan.Budget))));
        }
        if (drawer.fillLink != null) drawer.fillLink.gameObject.SetActive(need);
    }

    /// <summary>Yellow and clickable, or dashed grey (the design's disabled Upgrade). Either may be missing on a designed screen.</summary>
    static void StylePrimary(Button button, TextMeshProUGUI text, bool enabled)
    {
        if (button == null)
        {
            if (text != null) text.color = enabled ? UIColors.Ink : UIColors.Pencil;
            return;
        }
        BlueprintBox box = button.GetComponent<BlueprintBox>();
        if (box != null)
        {
            box.SetStyle(enabled ? UIColors.Highlight : UIColors.PaperShade, enabled ? UIColors.Ink : UIColors.Pencil, 3f);
            box.Dashed = !enabled;
            box.SetShadow(enabled ? UIColors.Ink : Color.clear, new Vector2(5f, 6f));
        }
        if (text != null) text.color = enabled ? UIColors.Ink : UIColors.Pencil;
        foreach (TowerIcon icon in button.GetComponentsInChildren<TowerIcon>(true)) icon.SetColor(enabled ? UIColors.Ink : UIColors.Pencil);
        if (button.interactable != enabled) button.interactable = enabled;
    }

    void BindRows(List<TowerSlotRow> rows, RectTransform list, TowerSlotRow template, TowerBuilder b, TowerRoom room, bool selectable)
    {
        if (list == null || template == null) return;
        int count = room != null ? room.SlotCount : 0;
        while (rows.Count < count)
        {
            TowerSlotRow row = Instantiate(template, list, false);
            row.name = "Slot " + (rows.Count + 1);
            TowerSlotRow clicked = row;
            if (row.button != null && selectable) row.button.onClick.AddListener(() => WithBuilder(x => x.SelectSlot(clicked.slot)));
            rows.Add(row);
        }
        for (int i = 0; i < rows.Count; i++)
        {
            bool used = i < count;
            if (rows[i].gameObject.activeSelf != used) rows[i].gameObject.SetActive(used);
            if (!used) continue;
            int level = room.GetLevel(i);
            TowerFurnitureSlot data = room.Definition.GetSlot(i);
            rows[i].Bind(i, data, TowerText.SlotName(data), level, b.Plan.MaxLevel, TowerText.LevelName(b.Rules, level), level * b.Rules.incomePerLevel,
                         selectable && i == b.SelectedSlot);
        }
    }

    // ------------------------------------------------------------------
    // Right panel: the room card (campus) and the furniture card (room view)
    // ------------------------------------------------------------------

    void RefreshClipboard(TowerBuilder b)
    {
        TowerRoom inside = b.InsideRoom;
        TowerRoom room = inside ?? (b.Tool == TowerTool.None && !b.FloorSlipOpen && !b.PurchaseOpen ? b.Selected : null);
        bool show = room != null;
        int seats = show ? SeatedIn(room) : 0;
        int key = show ? Hash(room.Id, inside != null ? 1 + b.SelectedSlot * 3 : 0, b.PlanVersion, seats, Hash(b.Plan.Budget.GetHashCode(), TowerBuilder.Employees, 0, 0, 0)) : -1;
        if (key == clipKey) return;
        clipKey = key;
        if (clipboard.panel != null && clipboard.panel.gameObject.activeSelf != show) clipboard.panel.gameObject.SetActive(show);
        if (!show) return;
        if (clipboard.roomPage != null) clipboard.roomPage.gameObject.SetActive(inside == null);
        if (clipboard.slotPage != null) clipboard.slotPage.gameObject.SetActive(inside != null);
        if (inside == null) BindRoomCard(b, room, seats);
        else BindSlotCard(b, room, b.SelectedSlot, seats);
    }

    int SeatedIn(TowerRoom room)
    {
        return room != null && seated.TryGetValue(room, out int n) ? n : 0;
    }

    void BindRoomCard(TowerBuilder b, TowerRoom room, int seatedHere)
    {
        TowerPlan plan = b.Plan;
        TowerRoomDefinition d = room.Definition;
        if (clipboard.swatch != null) clipboard.swatch.Fill = d.paper;
        if (clipboard.swatchIcon != null)
        {
            clipboard.swatchIcon.Kind = TowerArt.IconFor(d.role);
            clipboard.swatchIcon.SetColor(d.ink);
        }
        SetText(clipboard.nameText, TowerText.RoomName(room));
        SetText(clipboard.floorText, plan.BuildingCount > 1
            ? TowerText.Get("tower.card.floor.campus", ("building", TowerText.BuildingName(room.Building)), ("floor", room.Floor), ("count", room.Tiles))
            : TowerText.Get("tower.card.floor", ("floor", room.Floor), ("count", room.Tiles)));

        int places = room.Capacity;
        bool seatsRoom = places > 0;
        string bonus = BonusText(plan, room);
        if (clipboard.seatsRow != null) clipboard.seatsRow.gameObject.SetActive(seatsRoom || bonus.Length > 0);
        SetText(clipboard.seatsLabel, TowerText.Get(seatsRoom ? "tower.card.staff" : "tower.card.bonus"));
        SetText(clipboard.seatsText, seatsRoom ? seatedHere + " / " + places : bonus);
        DrawSeatPips(clipboard.seatPips, seatsRoom ? places : 0, seatedHere);

        // The room's level: what it is, what the next one gives, and the upgrade button (a purchase slip)
        BindLevelBlock(b, room);

        string perLevel = TowerText.PlusPerDay(b.Rules.incomePerLevel);
        string text;
        if (room.IsStarter)
        {
            text = TowerText.Get("tower.card.text.starter", ("seats", places), ("free", Math.Max(0, places - seatedHere)));
        }
        else if (seatsRoom && !d.HasBonus)
        {
            int free = Math.Max(0, places - seatedHere);
            text = free == 0 ? TowerText.Get("tower.card.text.staff.full", ("seats", places), ("amount", perLevel))
                             : TowerText.Get("tower.card.text.staff", ("seats", places), ("free", free), ("amount", perLevel));
        }
        else if (d.HasBonus)
        {
            text = TowerText.Get("tower.card.text.bonus", ("effect", EffectText(d)), ("level", TowerText.LevelName(b.Rules, 0)),
                                 ("percent", TowerText.Percent(b.Rules.bonusPerLevel * 100f)), ("amount", perLevel));
        }
        else
        {
            string description = TowerText.RoomDescription(d);
            text = description.Length > 0 ? TowerText.Get("tower.card.text.plain", ("description", description), ("amount", perLevel))
                                          : TowerText.Get("tower.card.text.plain.nodescription", ("amount", perLevel));
        }
        SetText(clipboard.descriptionText, text);
        SetText(clipboard.furnitureHeader, TowerText.Get("tower.card.furniture_header", ("count", room.SlotCount)));   // capitals in the String Table
        BindRows(clipRows, clipboard.slotRows, clipboard.rowTemplate, b, room, false);
        SetText(clipboard.furnitureTotal, TowerText.PlusPerDay(plan.FurnitureIncome(room)));

        bool movable = !room.IsFixed;
        if (clipboard.moveButton != null) clipboard.moveButton.gameObject.SetActive(movable);
        if (clipboard.sellButton != null) clipboard.sellButton.gameObject.SetActive(movable);
        SetText(clipboard.sellText, TowerText.Get("tower.card.sell", ("amount", UIFormat.Money(plan.SellValue(room)))));
        if (clipboard.fixedNote != null)
        {
            clipboard.fixedNote.gameObject.SetActive(!movable);
            SetText(clipboard.fixedNote, TowerText.Get(room.IsStarter ? "tower.card.fixed.starter" : "tower.card.fixed"));
        }
    }

    /// <summary>"Level 2 of 4 · upkeep −$80/day" and "Upgrade to level 3 · $60,000" (the slip shows what it adds).</summary>
    void BindLevelBlock(TowerBuilder b, TowerRoom room)
    {
        TowerPlan plan = b.Plan;
        TowerRoomDefinition d = room.Definition;
        double upkeep = plan.RoomUpkeep(room) * (1.0 - plan.UpkeepSavingsShare);
        if (clipboard.levelBlock != null) clipboard.levelBlock.gameObject.SetActive(true);
        SetText(clipboard.levelText, d.HasLevels
            ? TowerText.Get("tower.card.level", ("level", room.Level), ("max", d.MaxLevel), ("upkeep", TowerText.Upkeep(upkeep)))
            : TowerText.Get("tower.card.level.none", ("upkeep", TowerText.Upkeep(upkeep))));
        bool canGrow = d.HasLevels && !room.IsTopRoomLevel;
        if (clipboard.levelButton != null)
        {
            clipboard.levelButton.gameObject.SetActive(canGrow);
            if (canGrow)
            {
                double price = plan.RoomUpgradePrice(room);
                int addedPlaces = d.CapacityAt(room.Level + 1) - room.Capacity;
                SetText(clipboard.levelButtonText, addedPlaces > 0
                    ? TowerText.Get("tower.card.level.upgrade.places", ("level", room.Level + 1), ("count", addedPlaces), ("price", UIFormat.Money(price)))
                    : TowerText.Get("tower.card.level.upgrade", ("level", room.Level + 1), ("price", UIFormat.Money(price))));
                StyleSecondary(clipboard.levelButton, clipboard.levelButtonText, plan.CanAfford(price));
            }
        }
    }

    /// <summary>A paper button that looks ready when affordable and pencil-grey (still clickable: its slip offers "Gamble for it") when not.</summary>
    static void StyleSecondary(Button button, TextMeshProUGUI text, bool affordable)
    {
        if (button == null) return;
        BlueprintBox box = button.GetComponent<BlueprintBox>();
        if (box != null)
        {
            box.SetStyle(affordable ? UIColors.Highlight : UIColors.PaperHi, UIColors.Ink, affordable ? 3f : 2f);
            box.Dashed = !affordable;
            box.SetShadow(affordable ? UIColors.Ink : Color.clear, new Vector2(4f, 5f));
        }
        if (text != null) text.color = affordable ? UIColors.Ink : UIColors.InkSoft;
        if (!button.interactable) button.interactable = true;
    }

    void BindSlotCard(TowerBuilder b, TowerRoom room, int slot, int seatedHere)
    {
        TowerPlan plan = b.Plan;
        TowerRules rules = b.Rules;
        TowerRoomDefinition d = room.Definition;
        TowerFurnitureSlot data = d.GetSlot(slot);
        // Spaced capitals (L10n.ToUpper keeps Turkish İ / ı right)
        SetText(clipboard.slotRoomText, L10n.ToUpper(TowerText.Get("tower.slot_card.room", ("room", TowerText.RoomName(room)), ("floor", room.Floor))));
        if (clipboard.slotSwatch != null) clipboard.slotSwatch.Fill = d.paper;
        if (clipboard.slotIcon != null)
        {
            clipboard.slotIcon.Kind = data != null ? TowerArt.IconFor(data.art) : TowerIconKind.Sofa;
            clipboard.slotIcon.SetColor(d.ink);
        }
        int level = room.GetLevel(slot);
        int max = plan.MaxLevel;
        SetText(clipboard.slotName, TowerText.SlotName(data));
        SetText(clipboard.slotLevel, TowerText.LevelWithNumber(rules, level, max));

        for (int i = 0; i < clipboard.ladder.Length; i++)
        {
            TowerLadderRow row = clipboard.ladder[i];
            if (row == null || row.row == null) continue;
            bool used = i <= max;
            row.row.gameObject.SetActive(used);
            if (!used) continue;
            bool done = i < level, now = i == level, next = i == level + 1;
            SetText(row.nameText, TowerText.LevelName(rules, i));
            if (row.nameText != null)
            {
                row.nameText.fontStyle = now ? FontStyles.Bold : FontStyles.Normal;
                row.nameText.color = now || next ? UIColors.Ink : UIColors.InkSoft;
            }
            if (row.marker != null)
            {
                row.marker.SetStyle(done ? UIColors.Ink : now ? UIColors.Highlight : UIColors.PaperHi,
                                    done || now || next ? UIColors.Ink : UIColors.Pencil, done || now || next ? 3f : 2f);
                float size = now ? 28f : 22f;
                row.marker.rectTransform.sizeDelta = new Vector2(size, size);
                row.marker.Radii = BlueprintUI.Round(size * 0.5f);
            }
            if (row.check != null) row.check.gameObject.SetActive(done);
            if (row.nowPill != null) row.nowPill.gameObject.SetActive(now);
            if (row.highlight != null) row.highlight.gameObject.SetActive(next);
            bool priced = i > level;
            if (row.priceText != null)
            {
                row.priceText.gameObject.SetActive(priced);
                row.priceText.text = UIFormat.Money(plan.LevelPrice(i));
                row.priceText.fontStyle = next ? FontStyles.Bold : FontStyles.Normal;
                row.priceText.color = next ? UIColors.Ink : UIColors.InkSoft;
            }
        }
        DrawLadderLine(clipboard.ladderLine, level, max);

        bool top = level >= max;
        SetText(clipboard.nextLabel, top ? TowerText.Get("tower.slot_card.top", ("level", TowerText.LevelName(rules, level)))
                                         : TowerText.Get("tower.slot_card.next", ("level", TowerText.LevelName(rules, level + 1))));
        SetText(clipboard.nextValue, TowerText.PlusPerDay(top ? level * rules.incomePerLevel : rules.incomePerLevel));
        double price = plan.UpgradePrice(room, slot);
        if (clipboard.upgradeButton != null) clipboard.upgradeButton.gameObject.SetActive(!top);
        SetText(clipboard.upgradeText, TowerText.Get("tower.button.upgrade", ("price", UIFormat.Money(price))));
        SetText(clipboard.upgradeNote, top ? TowerText.Get("tower.slot_card.fully")
                                       : plan.CanAfford(price) ? TowerText.Get("tower.slot_card.pay_or_gamble")
                                       : TowerText.Get("tower.slot_card.needs_more", ("amount", UIFormat.Money(price - plan.Budget))));

        int places = room.Capacity;
        bool seatsRoom = places > 0;
        SetText(clipboard.ledgerFirstLabel, TowerText.Get(seatsRoom ? "tower.card.staff" : "tower.card.bonus"));
        string bonus = BonusText(plan, room);
        SetText(clipboard.ledgerFirstValue, seatsRoom ? seatedHere + " / " + places : bonus.Length > 0 ? bonus : TowerText.Get("tower.card.none"));
        if (clipboard.ledgerFirstIcon != null) clipboard.ledgerFirstIcon.Kind = seatsRoom ? TowerIconKind.People : TowerIconKind.TrendUp;
        SetText(clipboard.ledgerFurniture, TowerText.PlusPerDay(plan.FurnitureIncome(room)));
    }

    /// <summary>Filled figures for seated people, dashed outlines for free places (at most 12 drawn; big rooms show their share).</summary>
    void DrawSeatPips(BlueprintGraphic graphic, int seats, int taken)
    {
        if (graphic == null) return;
        graphic.gameObject.SetActive(seats > 0);
        BlueprintMeshBuilder m = graphic.Mesh;
        m.Clear();
        m.Feather = 1f / Mathf.Max(0.05f, canvas != null ? canvas.scaleFactor : 1f);
        Rect r = graphic.rectTransform.rect;
        m.SetYDownFrame(new Vector2(r.xMin, r.yMax));
        TowerPart[] person = TowerArt.Icon(TowerIconKind.Person);
        var solid = TowerPalette.For(UIColors.Ink, UIColors.PaperHi);
        int shown = Mathf.Min(seats, 10);
        int filled = seats > shown ? Mathf.CeilToInt(taken * (float)shown / seats) : taken;
        for (int i = 0; i < shown; i++)
        {
            m.PushFrame();
            m.SetFrame(m.Map(new Vector2(i * 15f, 0f)), m.MapDir(Vector2.right), m.MapDir(new Vector2(0f, 1f)));
            if (i < filled)
            {
                TowerArt.DrawParts(m, person, solid);
            }
            else
            {
                TowerPath p = TowerSvg.Get(person[0].d);
                foreach (Vector2[] sub in p.subpaths)
                {
                    scratchPoints.Clear();
                    scratchPoints.AddRange(sub);
                    m.Dashed(scratchPoints, 1.4f, UIColors.Pencil, true, 2f, 2f);
                }
            }
            m.PopFrame();
        }
        graphic.Commit();
    }

    readonly List<Vector2> scratchPoints = new List<Vector2>(32);

    /// <summary>The ladder's spine: solid up to the current level, dashed above it.</summary>
    void DrawLadderLine(BlueprintGraphic graphic, int level, int max)
    {
        if (graphic == null) return;
        BlueprintMeshBuilder m = graphic.Mesh;
        m.Clear();
        m.Feather = 1f / Mathf.Max(0.05f, canvas != null ? canvas.scaleFactor : 1f);
        Rect r = graphic.rectTransform.rect;
        m.SetYDownFrame(new Vector2(r.xMin, r.yMax));
        const float row = 33f, x = 1.5f;
        float nowY = row * level + 16f;
        if (level > 0) m.Line(new Vector2(x, 16f), new Vector2(x, nowY), 3f, UIColors.Ink);
        if (level < max)
        {
            scratchPoints.Clear();
            scratchPoints.Add(new Vector2(x, nowY));
            scratchPoints.Add(new Vector2(x, row * max + 16f));
            m.Dashed(scratchPoints, 3f, UIColors.Pencil, false, 6f, 5f);
        }
        graphic.Commit();
    }

    // ------------------------------------------------------------------
    // Room view (inside a room)
    // ------------------------------------------------------------------

    void RefreshRoomView(TowerBuilder b)
    {
        TowerRoom room = b.InsideRoom;
        bool show = room != null;
        int seatedHere = show ? SeatedIn(room) : 0;
        int key = show ? Hash(room.Id, b.PlanVersion, b.SelectedSlot, seatedHere, room.TotalLevels * 31 + room.Level) : -1;
        if (key == roomKey) return;
        roomKey = key;
        if (roomView.panel != null && roomView.panel.gameObject.activeSelf != show) roomView.panel.gameObject.SetActive(show);
        if (!show) return;

        TowerRoomDefinition d = room.Definition;
        SetText(roomView.floorNumber, room.Floor.ToString(Inv));
        SetText(roomView.titleText, TowerText.RoomName(room));
        SetText(roomView.subtitleText, TowerText.Get("tower.room_view.subtitle", ("floor", room.Floor), ("from", room.Tile + 1), ("to", room.EndTile),
                                                     ("count", room.SlotCount)));
        if (roomView.paper != null)
        {
            bool casino = d.role == TowerRoomRole.CasinoRoom;
            roomView.paper.Fill = casino ? UIColors.Felt : d.paper;
            roomView.paper.SetGradient(casino ? UIColors.FeltHi : Color.clear, new Vector2(0.5f, 0.35f));
        }

        // Slots left to right by their view order, spread over the room
        viewOrder.Clear();
        for (int i = 0; i < room.SlotCount; i++) viewOrder.Add(i);
        viewOrder.Sort((x, y) =>
        {
            int c = d.GetSlot(x).viewOrder.CompareTo(d.GetSlot(y).viewOrder);
            return c != 0 ? c : x.CompareTo(y);
        });
        const float innerWidth = 1194f, floorY = 400f;
        float total = 0f;
        foreach (int i in viewOrder) total += ArtWidth(d.GetSlot(i).art);
        float scale = total > innerWidth - 40f ? (innerWidth - 40f) / total : 1f;
        float gap = (innerWidth - total * scale) / (viewOrder.Count + 1);

        if (roomView.art != null)
        {
            BlueprintMeshBuilder m = roomView.art.Mesh;
            m.Clear();
            m.Feather = 1f / Mathf.Max(0.05f, canvas != null ? canvas.scaleFactor : 1f);
            Rect r = roomView.art.rectTransform.rect;
            m.SetYDownFrame(new Vector2(r.xMin, r.yMax));
            DrawBackWall(m, d, innerWidth, floorY);
            TowerPainter.DrawRoomViewLook(m, d.LookAt(room.Level), innerWidth, floorY);
        }

        float x = gap;
        int deskIndex = 0, personIndex = 0;
        int spotCount = 0;
        // Desks show two people each; a big room shows its share of the people seated there
        int deskSeats = 0;
        foreach (int slot in viewOrder) if (d.GetSlot(slot).art == TowerFurnitureArt.Desk) deskSeats += 2;
        int people = room.Capacity > deskSeats && room.Capacity > 0 ? Mathf.CeilToInt(seatedHere * (float)deskSeats / room.Capacity) : seatedHere;
        foreach (int slot in viewOrder)
        {
            TowerFurnitureSlot data = d.GetSlot(slot);
            float width = ArtWidth(data.art) * scale;
            TowerArt.RoomViewArt(data.art, out string shapeKey, out float artScale, out float wallY, out string extra, out float extraY);
            TowerShape shape = TowerArt.Shape(shapeKey);
            float height = shape != null ? shape.height * artScale * scale : 100f;
            float y = float.IsNaN(wallY) ? floorY - height : wallY * scale;
            if (roomView.art != null && shape != null)
            {
                BlueprintMeshBuilder m = roomView.art.Mesh;
                var palette = TowerPalette.For(d.ink, d.paper);
                int hidden = 0;
                if (data.art == TowerFurnitureArt.Desk)
                {
                    palette.shirtA = Shirt(deskIndex * 2);
                    palette.shirtB = Shirt(deskIndex * 2 + 1);
                    palette.hairA = Hair(deskIndex * 2);
                    palette.hairB = Hair(deskIndex * 2 + 1);
                    if (personIndex >= people) hidden |= 1;
                    if (personIndex + 1 >= people) hidden |= 2;
                    personIndex += 2;
                    deskIndex++;
                }
                float strokeScale = artScale > 1f ? 1.5f / artScale : 1f;
                TowerArt.Draw(m, shape, x, y, artScale * scale, false, palette, strokeScale, hidden);
                TowerShape extraShape = extra != null ? TowerArt.Shape(extra) : null;
                if (extraShape != null)
                    TowerArt.Draw(m, extraShape, x, y + extraY * scale, artScale * scale, false, palette, strokeScale);
            }
            float spotTop = Mathf.Min(168f, y - 10f);
            BindSpot(b, spotCount++, slot, data, room.GetLevel(slot), x, spotTop, width, floorY - spotTop, slot == b.SelectedSlot);
            x += width + gap;
        }
        for (int i = spotCount; i < spots.Count; i++)
            if (spots[i].gameObject.activeSelf) spots[i].gameObject.SetActive(false);
        if (roomView.art != null) roomView.art.Commit();
    }

    static float ArtWidth(TowerFurnitureArt art)
    {
        TowerArt.RoomViewArt(art, out string shapeKey, out float scale, out _, out _, out _);
        TowerShape shape = TowerArt.Shape(shapeKey);
        return shape != null ? Mathf.Max(96f, shape.width * scale) : 96f;
    }

    static Color Shirt(int i)
    {
        uint[] shirts = { 0xFFE680, 0xFFC7D1, 0xCDEBA6, 0xFFFDF6, 0xFFC7D1, 0xE7C98C, 0xFFFDF6, 0xCDEBA6, 0xBFE3F5, 0xFFE680 };
        return UIColors.Hex(shirts[i % shirts.Length]);
    }

    static Color Hair(int i)
    {
        uint[] hair = { 0x1D2433, 0x8B5E3C, 0x474F60, 0x1D2433, 0xE2B13C, 0x1D2433, 0x8B5E3C, 0x474F60 };
        return UIColors.Hex(hair[i % hair.Length]);
    }

    /// <summary>The room view's back wall: two windows, a chart poster, a clock and a wooden floor (TowerRoom board).</summary>
    static void DrawBackWall(BlueprintMeshBuilder m, TowerRoomDefinition d, float width, float floorY)
    {
        Color32 ink = UIColors.Ink;
        float s = width / 1194f;
        for (int w = 0; w < 2; w++)
        {
            float wx = (w == 0 ? 250f : 700f) * s;
            Rect glass = new Rect(wx, 40f, 200f * s, 112f);
            m.FillRoundRect(glass, 4f, UIColors.BlueprintInk);
            m.Line(new Vector2(wx + 12f * s, 152f), new Vector2(wx + 12f * s, 122f), 2f, UIColors.Pencil);
            m.Line(new Vector2(wx + 50f * s, 152f), new Vector2(wx + 50f * s, 106f), 2f, UIColors.Pencil);
            m.Line(new Vector2(wx + 82f * s, 106f), new Vector2(wx + 82f * s, 152f), 2f, UIColors.Pencil);
            m.Line(new Vector2(wx + 154f * s, 152f), new Vector2(wx + 154f * s, 128f), 2f, UIColors.Pencil);
            m.StrokeRoundRect(glass, new Vector4(4f, 4f, 4f, 4f), 3f, ink);
            m.Line(new Vector2(wx + 100f * s, 40f), new Vector2(wx + 100f * s, 152f), 3f, ink);
            m.Line(new Vector2(wx, 96f), new Vector2(wx + 200f * s, 96f), 3f, ink);
            Rect sill = new Rect(wx - 10f * s, 152f, 220f * s, 9f);
            m.FillRoundRect(sill, 2f, UIColors.PaperHi);
            m.StrokeRoundRect(sill, new Vector4(2f, 2f, 2f, 2f), 3f, ink);
        }
        // Chart poster (turned a little) with its tape
        m.PushFrame();
        Vector2 pivot = m.Map(new Vector2(575f * s, 96f));
        float a = 2f * Mathf.Deg2Rad;
        Vector2 ax = m.MapDir(new Vector2(Mathf.Cos(a), Mathf.Sin(a))), ay = m.MapDir(new Vector2(-Mathf.Sin(a), Mathf.Cos(a)));
        m.SetFrame(pivot - ax * 575f * s - ay * 96f, ax, ay);
        Rect poster = new Rect(520f * s, 44f, 110f, 104f);
        m.FillRoundRect(poster, 3f, UIColors.PaperHi);
        m.StrokeRoundRect(poster, new Vector4(3f, 3f, 3f, 3f), 3f, ink);
        m.Line(new Vector2(538f * s, 62f), new Vector2(538f * s, 130f), 2.4f, ink);
        m.Line(new Vector2(538f * s, 130f), new Vector2(538f * s + 74f, 130f), 2.4f, ink);
        Color32 green = UIColors.Money;
        m.Line(new Vector2(538f * s + 8f, 118f), new Vector2(538f * s + 28f, 96f), 3f, green);
        m.Line(new Vector2(538f * s + 28f, 96f), new Vector2(538f * s + 46f, 108f), 3f, green);
        m.Line(new Vector2(538f * s + 46f, 108f), new Vector2(538f * s + 68f, 74f), 3f, green);
        m.PopFrame();
        m.FillRect(new Rect(552f * s, 32f, 46f, 14f), UIColors.Tape);
        // Clock
        Vector2 clock = new Vector2(width - 66f, 84f);
        m.FillCircle(clock, 30f, UIColors.PaperHi);
        m.Circle(clock, 30f, 3f, ink);
        m.Line(clock, clock + new Vector2(0f, -16f), 3f, ink);
        m.Line(clock, clock + new Vector2(11f, 7f), 3f, ink);
        // Wooden floor with its boards
        m.FillRect(new Rect(0f, floorY, width, 18f), UIColors.Manila, 0f);
        for (float bx = 48f; bx < width; bx += 96f)
            m.Line(new Vector2(bx, floorY + 3f), new Vector2(bx, floorY + 15f), 2f, ink);
        m.Line(new Vector2(0f, floorY), new Vector2(width, floorY), 3f, ink);
    }

    void BindSpot(TowerBuilder b, int index, int slot, TowerFurnitureSlot data, int level, float x, float top, float width, float height, bool selected)
    {
        if (roomView.spotLayer == null || roomView.spotTemplate == null) return;
        while (spots.Count <= index)
        {
            TowerSlotSpot spot = Instantiate(roomView.spotTemplate, roomView.spotLayer, false);
            spot.name = "Spot " + (spots.Count + 1);
            TowerSlotSpot clicked = spot;
            if (spot.button != null) spot.button.onClick.AddListener(() => WithBuilder(x2 => x2.SelectSlot(clicked.slot)));
            spots.Add(spot);
        }
        TowerSlotSpot s = spots[index];
        if (!s.gameObject.activeSelf) s.gameObject.SetActive(true);
        RectTransform rect = (RectTransform)s.transform;
        BlueprintUI.TopLeft(rect, x - 6f, top - 6f, width + 12f, height + 12f);
        string slotName = TowerText.SlotName(data);
        if (s.tagRect != null)
        {
            // Wide characters (CJK, Hangul) count twice; long names get a wider tag
            int length = TowerText.VisualLength(slotName);
            float tagWidth = length > 8 ? Mathf.Clamp(20f + length * 10.5f, 184f, 260f) : 120f;
            s.tagRect.sizeDelta = new Vector2(tagWidth, 60f);
            s.tagRect.anchoredPosition = new Vector2((width + 12f - tagWidth) * 0.5f, -(283f + 168f - top + 6f));
        }
        s.Bind(slot, slotName, level, b.Plan.MaxLevel, selected);
    }

    // ------------------------------------------------------------------
    // Upgrade sheet (pay / blackjack / random chance) and its results
    // ------------------------------------------------------------------

    void RefreshSheet(TowerBuilder b)
    {
        bool show = b.UpgradeOpen && b.UpgradeRoom != null;
        TowerRoom room = b.UpgradeRoom;
        int slot = b.UpgradeSlot;
        // The heat meter can freeze gambling: then only paying works (asked twice a second while the sheet is up)
        if (show && Time.unscaledTime >= nextHeatCheck)
        {
            nextHeatCheck = Time.unscaledTime + 0.5f;
            heatOk = HeatMeter.CanGamble(out frozenReason);
        }
        if (!show) nextHeatCheck = 0f;
        string frozen = frozenReason;
        // Keyed on data only: while the die rolls just its face changes, set on its own (not by rebinding the whole sheet)
        int key = show ? Hash(room.Id * 7 + slot, b.PlanVersion, b.Plan.Budget.GetHashCode(), (int)b.Gamble * 3 + (b.ResultOpen ? 1 : 0),
                              (b.IsBlackjackAvailable ? 1 : 0) + (heatOk ? 2 : 0)) : -1;
        if (show && b.Gamble == TowerGambleKind.Roll) ShowRollingFace(b.RollFace);
        if (key == sheetKey) return;
        sheetKey = key;
        bool appearing = show && sheet.layer != null && !sheet.layer.gameObject.activeSelf;
        if (sheet.layer != null && sheet.layer.gameObject.activeSelf != show) sheet.layer.gameObject.SetActive(show);
        if (!show) return;
        // On top when it opens (not on every change: a "Gamble for it" pop-up above it must stay above it)
        if (appearing) sheet.layer.SetAsLastSibling();

        TowerPlan plan = b.Plan;
        TowerRules rules = b.Rules;
        TowerFurnitureSlot data = room.Definition.GetSlot(slot);
        string slotName = TowerText.SlotName(data);
        int level = room.GetLevel(slot);
        int max = plan.MaxLevel;
        bool atTop = level >= max;
        double price = plan.UpgradePrice(room, slot);
        bool afford = plan.CanAfford(price);
        bool idle = b.Gamble == TowerGambleKind.None;

        if (sheet.iconBox != null) sheet.iconBox.Fill = room.Definition.paper;
        if (sheet.icon != null)
        {
            sheet.icon.Kind = data != null ? TowerArt.IconFor(data.art) : TowerIconKind.Sofa;
            sheet.icon.SetColor(room.Definition.ink);
        }
        SetText(sheet.titleText, TowerText.Get("tower.sheet.title", ("slot", slotName)));
        string budgetLabel = "<color=#474F60>" + TowerText.Get("tower.label.build_budget") + "</color>";
        if (!atTop)
        {
            SetText(sheet.levelsText, TowerText.Get("tower.sheet.levels", ("from", TowerText.LevelInBrackets(rules, level)),
                                                    ("to", "<b>" + TowerText.LevelInBrackets(rules, level + 1) + "</b>"),
                                                    ("income_from", TowerText.PlusPerDay(level * rules.incomePerLevel)),
                                                    ("income_to", "<b><color=#1E7B45>" + TowerText.PlusPerDay((level + 1) * rules.incomePerLevel) + "</color></b>")));
            SetText(sheet.costText, TowerText.Get("tower.sheet.cost", ("amount", "<b>" + UIFormat.Money(price) + "</b>")));
            SetText(sheet.budgetText, TowerText.Get("tower.sheet.budget", ("label", budgetLabel), ("from", UIFormat.Money(plan.Budget)),
                                                    ("to", "<b>" + UIFormat.Money(Math.Max(0.0, plan.Budget - price)) + "</b>")));
        }
        else
        {
            SetText(sheet.levelsText, TowerText.Get("tower.sheet.levels.top", ("level", TowerText.LevelInBrackets(rules, level))));
            SetText(sheet.costText, TowerText.Get("tower.sheet.cost.none"));
            SetText(sheet.budgetText, TowerText.Get("tower.sheet.budget.top", ("label", budgetLabel), ("amount", UIFormat.Money(plan.Budget))));
        }

        bool can = idle && afford && !atTop;
        SetText(sheet.payText, TowerText.Get("tower.sheet.pay.button", ("amount", UIFormat.Money(price))));
        SetInteractable(sheet.payButton, can);
        SetText(sheet.payNote, TowerText.Get("tower.sheet.pay.note"));

        int winTo = Math.Min(max, level + rules.blackjackWinLevels);
        int winLevels = winTo - level;
        SetText(sheet.blackjackBody, TowerText.Get(winLevels < rules.blackjackWinLevels ? "tower.sheet.blackjack.body.capped" : "tower.sheet.blackjack.body",
                                                   ("count", winLevels), ("level", TowerText.LevelName(rules, winTo)), ("amount", UIFormat.Money(price))));
        SetText(sheet.blackjackOdds, TowerText.Get("tower.sheet.blackjack.odds",
                                                   ("percent", "<b><color=#E2B13C>" + TowerText.Percent(rules.blackjackWinChance * 100f) + "</color></b>")));
        // One level left: the gambles' +2 / +3 would be capped to +1, so only Pay makes sense
        bool gambleWorth = max - level >= 2;
        bool table = b.IsBlackjackAvailable;
        SetInteractable(sheet.blackjackButton, can && table && heatOk && gambleWorth);
        SetText(sheet.blackjackText, TowerText.Get(!heatOk ? "tower.sheet.blackjack.frozen" : !gambleWorth ? "tower.sheet.blackjack.pay_instead"
                                                   : table ? "tower.sheet.blackjack.deal" : "tower.sheet.blackjack.no_table"));

        string odds = RollOdds(rules);
        SetText(sheet.rollLabels, odds);
        SetText(sheet.rollButtonOdds, !heatOk ? TowerText.Get("tower.sheet.roll.frozen") : !gambleWorth ? TowerText.Get("tower.sheet.roll.one_left") : odds);
        SetText(sheet.rollNote, TowerText.Get("tower.sheet.roll.note", ("level", TowerText.LevelName(rules, 0))));
        SetInteractable(sheet.rollButton, can && heatOk && gambleWorth);
        DrawOddsBar(sheet.rollBar, rules);

        string footer;
        if (!afford && !atTop) footer = TowerText.Get("tower.sheet.footer.short", ("amount", UIFormat.Money(price - plan.Budget)));
        else if (!heatOk) footer = TowerText.Get("tower.sheet.footer.frozen", ("reason", frozen));
        else if (!gambleWorth && !atTop) footer = TowerText.Get("tower.message.one_level_left");
        else footer = TowerText.Get("tower.sheet.footer", ("amount", UIFormat.Money(price)));
        SetText(sheet.footerText, footer);
        if (sheet.footerText != null) sheet.footerText.color = !heatOk && (afford || atTop) ? UIColors.Loss : UIColors.Pencil;
        if (sheet.fillLink != null) sheet.fillLink.gameObject.SetActive(!afford && !atTop && idle);
        SetInteractable(sheet.cancelButton, idle);
        SetInteractable(sheet.closeButton, idle);

        // The roll in progress (its face: ShowRollingFace, every frame it changes)
        bool rollingNow = b.Gamble == TowerGambleKind.Roll;
        if (sheet.rollingLayer != null && sheet.rollingLayer.gameObject.activeSelf != rollingNow) sheet.rollingLayer.gameObject.SetActive(rollingNow);

        // The result
        bool result = b.ResultOpen && idle;
        if (sheet.resultLayer != null && sheet.resultLayer.gameObject.activeSelf != result) sheet.resultLayer.gameObject.SetActive(result);
        if (result) BindResult(b.LastResult, rules, plan.Budget);
    }

    /// <summary>The rolling die's face ("+2", "±0"): the one thing of the sheet that changes while it spins.</summary>
    void ShowRollingFace(int face)
    {
        if (face == shownRollFace) return;
        shownRollFace = face;
        SetText(sheet.rollingFace, TowerText.LevelChange(face));
    }

    void BindResult(TowerUpgradeResult r, TowerRules rules, double budget)
    {
        // The stamps are capitals in the String Table (the stamp's Caps style only spaces them)
        string stamp, title;
        Color stampColor;
        if (r.kind == TowerGambleKind.Blackjack)
        {
            if (r.hand == BlackjackResult.Win) { stamp = "tower.result.stamp.won"; title = "tower.result.title.won"; stampColor = UIColors.Money; }
            else if (r.hand == BlackjackResult.Push) { stamp = "tower.result.stamp.push"; title = "tower.result.title.push"; stampColor = UIColors.Pencil; }
            else { stamp = "tower.result.stamp.lost"; title = "tower.result.title.lost"; stampColor = UIColors.Loss; }
            stamp = TowerText.Get(stamp);
        }
        else if (r.kind == TowerGambleKind.Roll)
        {
            stamp = TowerText.LevelChange(r.rolled);
            title = r.rolled > 0 ? "tower.result.title.lucky" : r.rolled == 0 ? "tower.result.title.nothing" : "tower.result.title.ouch";
            stampColor = r.rolled > 0 ? UIColors.Money : r.rolled == 0 ? UIColors.Pencil : UIColors.Loss;
        }
        else
        {
            stamp = TowerText.Get("tower.result.stamp.paid");
            title = "tower.result.title.paid";
            stampColor = UIColors.Money;
        }
        title = TowerText.Get(title);
        SetText(sheet.resultStamp, stamp);
        if (sheet.resultStamp != null) sheet.resultStamp.color = stampColor;
        if (sheet.resultStampBox != null)
        {
            sheet.resultStampBox.Border = stampColor;
            sheet.resultStampBox.SetOutline(stampColor, 2f, 4f);
        }
        SetText(sheet.resultTitle, title);
        SetText(sheet.resultLine, ResultSummary(r, rules));
        SetText(sheet.resultBudget, TowerText.Get("tower.result.budget", ("amount", UIFormat.Money(budget)), ("slot", TowerText.ResultSlotName(r)),
                                                  ("income", TowerText.PlusPerDay(r.levelAfter * rules.incomePerLevel))));
    }

    /// <summary>One line about an upgrade's outcome (also the notification when build mode closed meanwhile).</summary>
    public static string ResultSummary(TowerUpgradeResult r, TowerRules rules)
    {
        int gained = r.levelAfter - r.levelBefore;
        string slot = TowerText.ResultSlotName(r);
        string before = TowerText.LevelName(rules, r.levelBefore), after = TowerText.LevelName(rules, r.levelAfter);
        string stake = UIFormat.Money(r.stake);
        if (r.kind == TowerGambleKind.Blackjack)
        {
            if (r.hand == BlackjackResult.Win) return TowerText.Get("tower.result.gained", ("slot", slot), ("from", before), ("to", after), ("count", gained));
            if (r.hand == BlackjackResult.Push) return TowerText.Get("tower.result.push", ("amount", stake));
            return TowerText.Get("tower.result.lost", ("amount", stake), ("slot", slot), ("level", before));
        }
        if (r.kind == TowerGambleKind.Roll)
        {
            if (gained > 0) return TowerText.Get("tower.result.gained", ("slot", slot), ("from", before), ("to", after), ("count", gained));
            if (gained < 0) return TowerText.Get("tower.result.dropped", ("slot", slot), ("level", after), ("amount", stake));
            if (r.rolled < 0) return TowerText.Get("tower.result.already_bottom", ("level", TowerText.LevelName(rules, 0)), ("amount", stake));
            return TowerText.Get("tower.result.lost", ("amount", stake), ("slot", slot), ("level", before));
        }
        return TowerText.Get("tower.result.paid", ("slot", slot), ("from", before), ("to", after), ("income", TowerText.PlusPerDay(rules.incomePerLevel)));
    }

    /// <summary>"10% +3 · 40% +2 · 35% ±0 · 15% −1"</summary>
    public static string RollOdds(TowerRules rules)
    {
        float total = 0f;
        foreach (TowerRollOutcome o in rules.rollOutcomes) total += Mathf.Max(0f, o.chance);
        var parts = new List<string>(rules.rollOutcomes.Count);
        foreach (TowerRollOutcome o in rules.rollOutcomes)
            parts.Add(TowerText.Get("tower.sheet.roll.odds_part", ("percent", TowerText.Percent(total > 0f ? o.chance / total * 100f : 0f)),
                                    ("change", TowerText.LevelChange(o.levels))));
        return string.Join(TowerText.Get("tower.list.separator"), parts);
    }

    /// <summary>The random chance odds as a bar: dark green / green / paper / red segments.</summary>
    void DrawOddsBar(BlueprintGraphic graphic, TowerRules rules)
    {
        if (graphic == null) return;
        BlueprintMeshBuilder m = graphic.Mesh;
        m.Clear();
        m.Feather = 1f / Mathf.Max(0.05f, canvas != null ? canvas.scaleFactor : 1f);
        Rect r = graphic.rectTransform.rect;
        m.SetYDownFrame(new Vector2(r.xMin, r.yMax));
        float total = 0f;
        foreach (TowerRollOutcome o in rules.rollOutcomes) total += Mathf.Max(0f, o.chance);
        float x = 0f, w = r.width, h = r.height;
        Vector4 radii = BlueprintUI.RadiusSmall;
        m.FillRoundRect(new Rect(0f, 0f, w, h), radii, UIColors.PaperHi);
        foreach (TowerRollOutcome o in rules.rollOutcomes)
        {
            float part = total > 0f ? Mathf.Max(0f, o.chance) / total * w : 0f;
            Color c = o.levels >= 3 ? UIColors.Hex(0x145C33) : o.levels > 0 ? UIColors.Money : o.levels == 0 ? UIColors.PaperShade : UIColors.Loss;
            m.FillRect(new Rect(x + 1.5f, 1.5f, Mathf.Max(0f, part - 1.5f), h - 3f), c, 0f);
            x += part;
            if (x < w - 2f) m.Line(new Vector2(x, 0f), new Vector2(x, h), 3f, UIColors.Ink);
        }
        m.StrokeRoundRect(TowerPainter.Inset(new Rect(0f, 0f, w, h), 1.5f), radii, 3f, UIColors.Ink);
        graphic.Commit();
    }

    // ------------------------------------------------------------------
    // Slips on the campus: add floor, sell; the first-visit note
    // ------------------------------------------------------------------

    void RefreshFloorSlip(TowerBuilder b)
    {
        TowerPlan plan = b.Plan;
        TowerBuilding building = b.FloorSlipBuilding ?? plan.Headquarters;
        bool show = b.FloorSlipOpen && b.InsideRoom == null && !building.IsTopFloorReached && !b.PurchaseOpen;
        // Its content is keyed on data only; where it sits follows the camera every frame (PlaceFloorSlip, change-only)
        int key = show ? Hash(building.Floors * 31 + building.Id, plan.Budget.GetHashCode(), building.TilesPerFloor, b.PlanVersion, 0) : -1;
        if (key != floorSlipKey)
        {
            floorSlipKey = key;
            if (floorSlip.slip != null && floorSlip.slip.gameObject.activeSelf != show) floorSlip.slip.gameObject.SetActive(show);
            if (show) BindFloorSlip(plan, building);
        }
        if (show) PlaceFloorSlip(plan, building);
    }

    void BindFloorSlip(TowerPlan plan, TowerBuilding building)
    {
        int next = building.Floors + 1;
        double price = plan.FloorPriceFor(building);
        bool afford = plan.CanAfford(price);
        SetText(floorSlip.titleText, plan.BuildingCount > 1
            ? TowerText.Get("tower.floor_slip.title.campus", ("building", TowerText.BuildingName(building)), ("floor", next), ("price", UIFormat.Money(price)))
            : TowerText.Get("tower.floor_slip.title", ("floor", next), ("price", UIFormat.Money(price))));
        SetText(floorSlip.bodyText, TowerText.Get("tower.floor_slip.body.campus", ("count", building.TilesPerFloor),
                                                  ("upkeep", TowerText.Upkeep(building.Type.upkeepPerFloor * (1.0 - plan.UpkeepSavingsShare)))));
        SetText(floorSlip.budgetValue, UIFormat.Money(plan.Budget));
        SetText(floorSlip.floorLabel, TowerText.Get("tower.floor_slip.floor", ("floor", next)));
        SetText(floorSlip.floorValue, UIFormat.Minus + UIFormat.Money(price));
        SetText(floorSlip.afterValue, afford ? UIFormat.Money(plan.Budget - price)
                                             : TowerText.Get("tower.floor_slip.needs_more", ("amount", UIFormat.Money(price - plan.Budget))));
        if (floorSlip.afterValue != null) floorSlip.afterValue.color = afford ? UIColors.Ink : UIColors.Loss;
        StylePrimary(floorSlip.buildButton, floorSlip.buildText, afford);
        if (floorSlip.fillLink != null) floorSlip.fillLink.gameObject.SetActive(!afford);
    }

    /// <summary>
    /// Right of the slot, its pointer on the slot's centre line; kept below the top bar, and moved left over the slot's end
    /// when there is no room for it on the right of the screen.
    /// </summary>
    void PlaceFloorSlip(TowerPlan plan, TowerBuilding building)
    {
        if (floorSlip.slip == null) return;
        float origin = TowerGeo.OriginX(plan, building);
        float top = TowerGeo.TopOf(building.Floors + 1) - 40f / zoom;
        float screenTop = ScreenY(top);
        float limit = TopBarHeight + CampusBarSpace + 12f;
        if (screenTop < limit) top += (limit - screenTop) / zoom;
        float x = KeepOnScreen(floorSlip.slip, origin + TowerGeo.Right(building.TilesPerFloor) + 36f);
        BlueprintUI.SetAnchoredPosition(floorSlip.slip, ToContent(x, top));
        BlueprintUI.SetLocalScale(floorSlip.slip, ChipScale);
    }

    /// <summary>
    /// Design x for <paramref name="rect"/> (on the campus, its pivot at <paramref name="x"/>, drawn at ChipScale), moved left
    /// as far as needed to keep its right edge 16 px inside the visible screen, but never off the left edge.
    /// </summary>
    float KeepOnScreen(RectTransform rect, float x)
    {
        float width = rect.rect.width * ChipScale.x, pivot = rect.pivot.x;
        float half = ScreenWidth * 0.5f / zoom;
        float maxX = panX + half - 16f / zoom - (1f - pivot) * width;
        float minX = panX - half + 16f / zoom + pivot * width;
        return Mathf.Max(minX, Mathf.Min(x, maxX));
    }

    void RefreshSellSlip(TowerBuilder b)
    {
        TowerRoom room = b.SellCandidate;
        bool show = room != null && b.InsideRoom == null;
        int seatedHere = show ? SeatedIn(room) : 0;
        // Its content (and the layout rebuild it needs) is keyed on data only; where it sits follows the camera every frame
        int key = show ? Hash(room.Id, b.PlanVersion, b.Plan.Budget.GetHashCode(), seatedHere, 1) : -1;
        if (key != sellKey)
        {
            sellKey = key;
            if (sellSlip.slip != null && sellSlip.slip.gameObject.activeSelf != show) sellSlip.slip.gameObject.SetActive(show);
            if (show) BindSellSlip(b, room, seatedHere);
        }
        if (show) PlaceSellSlip(b.Plan, room);
    }

    void BindSellSlip(TowerBuilder b, TowerRoom room, int seatedHere)
    {
        TowerPlan plan = b.Plan;
        double refund = plan.SellValue(room);
        double share = b.Rules.sellRefundShare;
        SetText(sellSlip.titleText, TowerText.Get("tower.sell.title", ("room", TowerText.RoomName(room))));
        string shareText = TowerText.Percent(share * 100.0);
        string refundText = "<b>" + UIFormat.Money(refund) + "</b>";
        double furniture = b.Rules.refundFurnitureOnSell ? room.TotalFurniturePaid + room.LevelPaid : 0.0;
        SetText(sellSlip.refundText, furniture > 0.0
            ? TowerText.Get("tower.sell.refund.furniture", ("refund", refundText), ("share", shareText), ("paid", UIFormat.Money(room.Paid)),
                            ("furniture", UIFormat.Money(furniture)))
            : TowerText.Get("tower.sell.refund", ("refund", refundText), ("share", shareText), ("paid", UIFormat.Money(room.Paid))));
        bool people = seatedHere > 0;
        if (sellSlip.peopleText != null) sellSlip.peopleText.gameObject.SetActive(people);
        if (people)
            SetText(sellSlip.peopleText, seatedHere == 1 ? TowerText.Get("tower.sell.people.move.one") : TowerText.Get("tower.sell.people.move.many", ("count", seatedHere)));
        SetText(sellSlip.furnitureText, TowerText.Get(room.TotalLevels > 0 ? "tower.sell.furniture" : "tower.sell.no_furniture"));
        SetText(sellSlip.budgetText, TowerText.Get("tower.sell.budget", ("label", TowerText.Get("tower.label.build_budget")), ("from", UIFormat.Money(plan.Budget)),
                                                   ("to", "<b><color=#1D2433>" + UIFormat.Money(plan.Budget + refund) + "</color></b>")));
        SetText(sellSlip.sellText, TowerText.Get("tower.sell.button", ("amount", UIFormat.Money(refund))));

        // Its size follows the texts: lay it out now, so PlaceSellSlip can use it this frame
        if (sellSlip.slip != null) LayoutRebuilder.ForceRebuildLayoutImmediate(sellSlip.slip);
    }

    /// <summary>Above the room (below it when there's no room above), the pointer aimed at the room's middle, inside the visible screen.</summary>
    void PlaceSellSlip(TowerPlan plan, TowerRoom room)
    {
        if (sellSlip.slip == null) return;
        float s = ChipScale.x;   // design pixels per slip pixel (the slip is counter-scaled)
        Vector2 size = sellSlip.slip.rect.size * s;
        float origin = TowerGeo.OriginX(plan, room.Building);
        float roomTop = TowerGeo.RoomTop(room.Floor), roomLeft = origin + TowerGeo.RoomLeft(room.Tile), roomWidth = TowerGeo.RoomWidth(room.Tiles);
        float centre = roomLeft + roomWidth * 0.5f;
        float halfScreen = ScreenWidth * 0.5f / zoom;
        float left = Mathf.Clamp(centre - 232f * s, panX - halfScreen + 16f / zoom, panX + halfScreen - 16f / zoom - size.x);
        bool above = ScreenY(roomTop - 50f * s - size.y) >= TopBarHeight + CampusBarSpace + 8f;
        float top = above ? roomTop - 50f * s - size.y : roomTop + TowerGeo.RoomHeight + 40f * s;
        BlueprintUI.SetAnchoredPosition(sellSlip.slip, ToContent(left, top));
        BlueprintUI.SetLocalScale(sellSlip.slip, ChipScale);
        if (sellSlip.pointer != null)
        {
            Vector2 slipSize = sellSlip.slip.rect.size;
            BlueprintUI.SetAnchoredPosition(sellSlip.pointer, new Vector2(Mathf.Clamp((centre - left) / s, 30f, slipSize.x - 30f), above ? -slipSize.y + 2f : -2f));
            float angle = above ? 45f : 225f;
            if (Mathf.Abs(Mathf.DeltaAngle(sellSlip.pointer.localEulerAngles.z, angle)) > 0.01f)
                sellSlip.pointer.localEulerAngles = new Vector3(0f, 0f, angle);
        }
    }

    /// <summary>Demo: the first-visit note tells about the Board's construction grant (its "Body" text, kept on the Demo entry).</summary>
    void SetDemoIntro()
    {
        Transform body = intro.note.Find("Body");
        TMP_Text text = body != null ? body.GetComponent<TMP_Text>() : null;
        if (text == null) return;
        L10nText localized = text.GetComponent<L10nText>();
        if (localized != null && localized.table == DemoMode.Table) return;
        L10nText.Attach(text, DemoMode.Table, "demo.build.intro",
                        () => new object[] { L10n.Args(("grant", UIFormat.Money(DemoMode.Config.startingBuildBudget))) });
    }

    void RefreshIntro(TowerBuilder b)
    {
        bool show = b.IntroOpen && b.InsideRoom == null;
        int key = show ? Hash(b.Plan.Floors, 1, Mathf.RoundToInt(zoom * 100f), Mathf.RoundToInt(panX), 0) : -1;
        if (key == introKey) return;
        introKey = key;
        if (intro.note != null && intro.note.gameObject.activeSelf != show) intro.note.gameObject.SetActive(show);
        if (!show || intro.note == null) return;
        if (DemoMode.Active) SetDemoIntro();
        // Right of the HQ's add-floor slot, its arrow pointing down into it; moved left on a narrow screen (a resize re-places it)
        float slotTop = TowerGeo.TopOf(b.Plan.Floors + 1);
        BlueprintUI.SetLocalScale(intro.note, ChipScale);
        intro.note.anchoredPosition = ToContent(KeepOnScreen(intro.note, TowerGeo.Right(b.Plan.TilesPerFloor) + 84f), slotTop - 192f);
    }

    void UpdateMessage()
    {
        if (messageChip == null || !messageChip.gameObject.activeSelf) return;
        float left = messageUntil - Time.unscaledTime;
        if (left <= 0f)
        {
            messageChip.gameObject.SetActive(false);
        }
        else if (messageGroup != null)
        {
            float alpha = Mathf.Clamp01(left / 0.3f);
            if (!Mathf.Approximately(messageGroup.alpha, alpha)) messageGroup.alpha = alpha;
        }
    }

    // ------------------------------------------------------------------
    // Texts and helpers
    // ------------------------------------------------------------------

    /// <summary>What a room does, as the catalog card says it ("Room for 5 staff", "+5% happiness", "+$1,500/day"), in the current language.</summary>
    public static string EffectText(TowerRoomDefinition d)
    {
        return TowerText.EffectText(d);
    }

    /// <summary>A room's bonus with its furniture and level ("+6.3% happiness"), empty for rooms without one.</summary>
    static string BonusText(TowerPlan plan, TowerRoom room)
    {
        return TowerText.BonusText(plan, room);
    }

    static void SetText(TextMeshProUGUI text, string value)
    {
        if (text != null && text.text != value) text.text = value;
    }

    static void SetInteractable(Button button, bool on)
    {
        BlueprintUI.SetInteractable(button, on);
    }

    /// <summary>Demo: a button for something only the full game has looks dimmed but still clicks (the builder says why).</summary>
    static void DimForDemo(Button button)
    {
        if (button == null || !DemoMode.Active) return;
        CanvasGroup group = button.GetComponent<CanvasGroup>();
        if (group == null) group = button.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0.5f;
    }

    /// <summary>Demo: the intro note's Fill budget aims at the first office (the grant pays the floor), so one win builds it.</summary>
    static void IntroFillBudget(TowerBuilder b)
    {
        if (DemoMode.Active)
        {
            double price = DemoTowerRules.NextOfficePrice(b.Plan, out string goal);
            if (price > 0.0)
            {
                b.GambleFor(price, goal);
                return;
            }
        }
        b.OpenBudgetGamble();
    }

    static int Hash(int a, int b, int c, int d, int e)
    {
        unchecked
        {
            int k = 17;
            k = k * 31 + a;
            k = k * 31 + b;
            k = k * 31 + c;
            k = k * 31 + d;
            k = k * 31 + e;
            return k == 0 ? 1 : k;
        }
    }
}

// ----------------------------------------------------------------------
// References of the screen's parts (filled by TowerViewFactory, or by a designed prefab)
// ----------------------------------------------------------------------

[Serializable]
public class TowerChips
{
    [Tooltip("\"Selected\" tag inside the selected room's top-right corner")]
    public RectTransform selectedTag;
    public RectTransform ghostTag;
    public BlueprintBox ghostTagBox;
    public TowerIcon ghostTagIcon;
    public TextMeshProUGUI ghostTagText;
    [Tooltip("\"Doesn't fit: 1 tile left on this side\" next to the pointer")]
    public RectTransform tooltip;
    public TextMeshProUGUI tooltipTitle;
    public TextMeshProUGUI tooltipText;
    [Tooltip("\"+ Add floor 5\" in the slot over the roof")]
    public RectTransform slotLabel;
    public TextMeshProUGUI slotLabelText;
    public RectTransform slotChip;
    public TextMeshProUGUI slotChipText;
    [Tooltip("\"Build floor · $41,000\" while the slot is hovered (the slot itself takes the click)")]
    public RectTransform slotButton;
    public BlueprintBox slotButtonBox;
    public TowerIcon slotButtonIcon;
    public TextMeshProUGUI slotButtonText;
    [Tooltip("Old: \"8 tiles\" under the street (every building has a label for it now; this one is hidden)")]
    public RectTransform dimensionText;
}

[Serializable]
public class TowerTopBar
{
    public TextMeshProUGUI companyText;
    public TextMeshProUGUI budgetText;
    public Button fillBudgetButton;
    [Tooltip("\"Employees 12 / 20\" (red \"Office full\" when capacity is reached)")]
    public BlueprintBox seatsBox;
    public TowerIcon seatsIcon;
    public TextMeshProUGUI seatsText;
    [Tooltip("\"Upkeep −$1,234/day\"")]
    public TextMeshProUGUI upkeepText;
    public TextMeshProUGUI valueText;
    public Button exitButton;
}

[Serializable]
public class TowerHintStrip
{
    public RectTransform strip;
    [Tooltip("The words after the Click key cap")]
    public TextMeshProUGUI clickText;
    [Tooltip("The Esc key cap's \"cancel\"")]
    public Button cancelButton;
    public TextMeshProUGUI noteText;
    [Tooltip("\"Gamble for it\": shown while what is being placed costs more than the build budget")]
    public Button gambleButton;
}

[Serializable]
public class TowerScrollControl
{
    public RectTransform panel;
    public Button upButton;
    public Button downButton;
    public TextMeshProUGUI rangeText;
    public TextMeshProUGUI totalText;
}

[Serializable]
public class TowerDrawer
{
    public RectTransform panel;
    public Button roomsTab;
    public Button furnitureTab;
    public Button campusTab;
    public Button closeButton;
    [Header("Rooms")]
    public RectTransform roomsPage;
    [Tooltip("Category filters: All, Offices, Staff, Business, Prestige")]
    public Button[] filterButtons = new Button[5];
    public RectTransform cardList;
    [Tooltip("Inactive card cloned for every room (and every building and link type on the Campus page)")]
    public TowerCatalogCard cardTemplate;
    [Header("Furniture")]
    public RectTransform furniturePage;
    public TextMeshProUGUI emptyText;
    public RectTransform roomInfoGroup;
    public TextMeshProUGUI roomTitle;
    public TextMeshProUGUI roomInfo;
    public RectTransform slotList;
    public TowerSlotRow slotRowTemplate;
    public RectTransform itemCard;
    public TowerIcon itemIcon;
    public TextMeshProUGUI itemName;
    public TextMeshProUGUI itemLevel;
    public TextMeshProUGUI itemNext;
    public Button upgradeButton;
    public TextMeshProUGUI upgradeText;
    public RectTransform needNote;
    public TextMeshProUGUI needTitle;
    public TextMeshProUGUI needText;
    public Button fillLink;
    [Header("Campus")]
    public RectTransform campusPage;
    public RectTransform buildingList;
    public RectTransform linkList;
    public TextMeshProUGUI campusSummary;
}

[Serializable]
public class TowerLadderRow
{
    public RectTransform row;
    public BlueprintBox marker;
    public TowerIcon check;
    public TextMeshProUGUI nameText;
    public RectTransform highlight;
    public RectTransform nowPill;
    public TextMeshProUGUI priceText;
}

[Serializable]
public class TowerClipboard
{
    public RectTransform panel;
    public Button closeButton;
    [Header("Room card (campus)")]
    public RectTransform roomPage;
    public BlueprintBox swatch;
    public TowerIcon swatchIcon;
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI floorText;
    public RectTransform seatsRow;
    public TextMeshProUGUI seatsLabel;
    public TextMeshProUGUI seatsText;
    public BlueprintGraphic seatPips;
    [Tooltip("The room's level and its upgrade button")]
    public RectTransform levelBlock;
    public TextMeshProUGUI levelText;
    public Button levelButton;
    public TextMeshProUGUI levelButtonText;
    public TextMeshProUGUI descriptionText;
    public TextMeshProUGUI furnitureHeader;
    public RectTransform slotRows;
    public TowerSlotRow rowTemplate;
    public TextMeshProUGUI furnitureTotal;
    public Button openButton;
    public Button moveButton;
    public Button sellButton;
    public TextMeshProUGUI sellText;
    public TextMeshProUGUI fixedNote;
    [Header("Furniture card (room view)")]
    public RectTransform slotPage;
    public TextMeshProUGUI slotRoomText;
    public BlueprintBox slotSwatch;
    public TowerIcon slotIcon;
    public TextMeshProUGUI slotName;
    public TextMeshProUGUI slotLevel;
    public TowerLadderRow[] ladder = new TowerLadderRow[6];
    public BlueprintGraphic ladderLine;
    public TextMeshProUGUI nextLabel;
    public TextMeshProUGUI nextValue;
    public Button upgradeButton;
    public TextMeshProUGUI upgradeText;
    public TextMeshProUGUI upgradeNote;
    public TowerIcon ledgerFirstIcon;
    public TextMeshProUGUI ledgerFirstLabel;
    public TextMeshProUGUI ledgerFirstValue;
    public TextMeshProUGUI ledgerFurniture;
    public Button backButton;
}

[Serializable]
public class TowerRoomView
{
    public RectTransform panel;
    public TextMeshProUGUI floorNumber;
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI subtitleText;
    public BlueprintBox paper;
    [Tooltip("Back wall and furniture drawings (1194 x 418, y down from the top-left)")]
    public BlueprintGraphic art;
    public RectTransform spotLayer;
    public TowerSlotSpot spotTemplate;
}

[Serializable]
public class TowerUpgradeSheet
{
    public RectTransform layer;
    public Button closeButton;
    public BlueprintBox iconBox;
    public TowerIcon icon;
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI levelsText;
    public TextMeshProUGUI costText;
    public TextMeshProUGUI budgetText;
    [Header("Just pay")]
    public Button payButton;
    public TextMeshProUGUI payText;
    public TextMeshProUGUI payNote;
    [Header("Blackjack")]
    public Button blackjackButton;
    public TextMeshProUGUI blackjackText;
    public TextMeshProUGUI blackjackBody;
    public TextMeshProUGUI blackjackOdds;
    [Header("Random chance")]
    public Button rollButton;
    public TextMeshProUGUI rollButtonOdds;
    public BlueprintGraphic rollBar;
    public TextMeshProUGUI rollLabels;
    public TextMeshProUGUI rollNote;
    [Header("Footer")]
    public TextMeshProUGUI footerText;
    public Button cancelButton;
    public Button fillLink;
    [Header("Roll and result")]
    public RectTransform rollingLayer;
    public TextMeshProUGUI rollingFace;
    public RectTransform resultLayer;
    public BlueprintBox resultStampBox;
    public TextMeshProUGUI resultStamp;
    public TextMeshProUGUI resultTitle;
    public TextMeshProUGUI resultLine;
    public TextMeshProUGUI resultBudget;
    public Button resultButton;
}

[Serializable]
public class TowerFloorSlip
{
    public RectTransform slip;
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI bodyText;
    public TextMeshProUGUI budgetValue;
    public TextMeshProUGUI floorLabel;
    public TextMeshProUGUI floorValue;
    public TextMeshProUGUI afterValue;
    public Button buildButton;
    public TextMeshProUGUI buildText;
    public Button cancelButton;
    public Button fillLink;
}

[Serializable]
public class TowerSellSlip
{
    public RectTransform slip;
    public RectTransform pointer;
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI refundText;
    public TextMeshProUGUI peopleText;
    public TextMeshProUGUI furnitureText;
    public TextMeshProUGUI budgetText;
    public Button sellButton;
    public TextMeshProUGUI sellText;
    public Button keepButton;
    public Button closeButton;
}

[Serializable]
public class TowerIntroNote
{
    public RectTransform note;
    public Button gotItButton;
    public Button fillBudgetButton;
}
