using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// The online cities screen (full screen, over the office; the game is paused while it's open). It opens on the
/// city picker (six postcards); a city shows its own map. Built entirely in code with UI Toolkit.
/// This file: opening/closing, moving between the picker and a city, top bar, map, tooltip, toasts, rich list.
/// CityScreen.Picker.cs: the postcards. CityScreen.Plot.cs: the plot panel. CityScreen.Challenge.cs: blackjack.
/// CityScreen.Dialogs.cs: company and logo (drawing or preset), bank, inbox, tools.
/// </summary>
public partial class CityScreen : MonoBehaviour
{
    public static CityScreen Instance { get; private set; }
    public static bool IsOpen => Instance != null && Instance.open;

    UIDocument document;
    CityOnline city;
    VisualElement root, pickerLayer, cityLayer, mapHost, modalLayer, toastLayer, tooltip, sidePanel, sideBody, loadingOverlay;
    CityMapView mapView;
    bool open, built;
    CityPlotDef selected;
    readonly List<VisualElement> modals = new List<VisualElement>();
    readonly List<Action> modalCloseHandlers = new List<Action>();

    // Top bar
    VisualElement joinBox, marketChip;
    CityUi.InkButton companyButton, bankButton, collectButton, toolsButton;
    CityLogoView companyLogo;
    Label cityTitle, cityCountry, testLabel, companyName, balanceLabel, inPlayLabel, rentLabel, inboxBadgeText;
    VisualElement inboxBadge;

    // Rich list
    VisualElement leaderList;

    // Legend and colouring
    VisualElement ownersLegend, pricesLegend;
    Label cheapLabel, dearLabel;
    CityUi.InkButton ownersButton, pricesButton;
    CityMapView.Colouring colouring = CityMapView.Colouring.Owners;

    // Loading
    Label loadingTitle, loadingText;
    VisualElement loadingButtons;

    string pendingCity, pendingFocus;

    /// <summary>The city on screen (null on the picker).</summary>
    string ShownCity => mapView != null && cityLayer != null && cityLayer.style.display == DisplayStyle.Flex ? mapView.Map.CityId : null;

    // ---- Opening and closing ----------------------------------------------------------------------------------

    /// <summary>Open the city picker, or go straight to a city (and a plot in it).</summary>
    public static void Open(string cityId = null, string focusPlotId = null)
    {
        var screen = Ensure();
        screen.pendingCity = cityId;
        screen.pendingFocus = focusPlotId;
        screen.Show();
    }

    public static void CloseScreen()
    {
        if (Instance != null) Instance.Hide();
    }

    static CityScreen Ensure()
    {
        if (Instance != null) return Instance;
        var go = new GameObject("CityScreen");
        go.SetActive(false);
        DontDestroyOnLoad(go);
        var doc = go.AddComponent<UIDocument>();
        doc.panelSettings = CityUi.CreatePanelSettings(200);
        var screen = go.AddComponent<CityScreen>();
        screen.document = doc;
        Instance = screen;
        go.SetActive(true);
        return screen;
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        if (document == null) document = GetComponent<UIDocument>();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        Unsubscribe();
        if (UIManager.Exists) UIManager.Instance.ClosePanel(this);
        if (Instance == this) Instance = null;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (open) Hide();
    }

    void Show()
    {
        city = CityOnline.Ensure();
        if (!built) Build();
        Subscribe();

        root.style.display = DisplayStyle.Flex;
        bool wasOpen = open;
        open = true;
        city.ScreenOpen = true;
        if (!wasOpen) UIManager.Instance.OpenPanel(this, true, HandleEscape);

        string target = pendingCity;
        pendingCity = null;
        if (target != null) _ = GoToCity(target);
        else ShowPicker();
    }

    void Hide()
    {
        if (!built) return;
        CloseAllModals();
        root.style.display = DisplayStyle.None;
        open = false;
        if (city != null) city.ScreenOpen = false;
        HideTooltip();
        if (UIManager.Exists) UIManager.Instance.ClosePanel(this);
    }

    /// <summary>Escape: close the top dialog, then the plot panel, then go back to the picker, then close.</summary>
    void HandleEscape()
    {
        if (modals.Count > 0) CloseTopModal();
        else if (selected != null) SelectPlot(null);
        else if (ShownCity != null) ShowPicker();
        else Hide();
    }

    // ---- Picker and cities -------------------------------------------------------------------------------------

    void ShowPicker()
    {
        CloseAllModals();
        SelectPlot(null);
        HideTooltip();
        if (city.CityId != null) city.LeaveCity();
        cityLayer.style.display = DisplayStyle.None;
        pickerLayer.style.display = DisplayStyle.Flex;
        RefreshPicker();
        if (!city.IsReady) _ = ConnectForPicker();
        else _ = city.RefreshMyAccounts();
    }

    async Task ConnectForPicker()
    {
        ShowLoading("Connecting to the cities…", null, false);
        await city.Connect();
        if (!open) return;
        if (city.IsReady)
        {
            HideLoading();
            RefreshPicker();
        }
        else ShowLoading("Couldn't reach the cities", city.LastError, true);
    }

    async Task GoToCity(string cityId)
    {
        var map = CityLayout.Get(cityId);
        if (map == null) return;
        CloseAllModals();
        SelectPlot(null);
        ShowLoading($"Going to {map.Name}…", null, false);
        bool ok = await city.EnterCity(cityId);
        if (!open) return;
        if (!ok)
        {
            ShowLoading($"Couldn't reach {map.Name}", city.LastError ?? "Try again in a moment.", true);
            return;
        }

        if (mapView == null || mapView.Map.CityId != cityId)
        {
            mapView?.RemoveFromHierarchy();
            mapView = new CityMapView(map) { Mode = colouring };
            mapView.PlotClicked += SelectPlot;
            mapView.PlotHovered += OnHover;
            mapHost.Add(mapView);
        }
        pickerLayer.style.display = DisplayStyle.None;
        cityLayer.style.display = DisplayStyle.Flex;
        HideLoading();
        RefreshEverything();
        if (!city.Joined) OpenCompanyDialog(true);
    }

    // ---- City events ------------------------------------------------------------------------------------------

    bool subscribed;

    void Subscribe()
    {
        if (subscribed || city == null) return;
        subscribed = true;
        city.MapReloaded += OnMapReloaded;
        city.PlotsChanged += OnPlotsChanged;
        city.AccountChanged += OnAccountChanged;
        city.StatusChanged += OnStatusChanged;
        city.NewsArrived += OnNews;
        city.MyAccountsChanged += RefreshPicker;
    }

    void Unsubscribe()
    {
        if (!subscribed || city == null) return;
        subscribed = false;
        city.MapReloaded -= OnMapReloaded;
        city.PlotsChanged -= OnPlotsChanged;
        city.AccountChanged -= OnAccountChanged;
        city.StatusChanged -= OnStatusChanged;
        city.NewsArrived -= OnNews;
        city.MyAccountsChanged -= RefreshPicker;
    }

    void OnMapReloaded()
    {
        if (ShownCity != null && ShownCity == city.CityId) RefreshEverything();
    }

    void OnPlotsChanged(List<CityPlotChange> changes)
    {
        if (!built || ShownCity == null) return;
        mapView.RefreshPlots(changes.Select(c => c.PlotId), city);
        if (selected != null && changes.Any(c => c.PlotId == selected.Id)) RefreshSidePanel();
        RefreshTopBar();
        RefreshLeaderboard();
        RefreshLegend();
    }

    void OnAccountChanged()
    {
        if (!built || ShownCity == null) return;
        RefreshTopBar();
        RefreshSidePanel();
        // My hand on the table shows on its plot
        var game = city.Account?.Game;
        if (game != null) mapView.RefreshPlots(new[] { game.PlotId }, city);
    }

    void OnStatusChanged()
    {
        if (!built || !open) return;
        if (city.State == CityOnline.Status.Ready && ShownCity == null) HideLoading();
        RefreshTopBar();
    }

    void OnNews(CityMessage news)
    {
        if (!built || !open || news == null) return;
        string where = news.CityId != null && news.CityId != city.CityId && CityLayout.Get(news.CityId) != null
            ? CityLayout.Get(news.CityId).Name + ": " : "";
        Toast(where + news.Text, news.Kind != CityMessageKind.TakenOver && news.Kind != CityMessageKind.LogoHidden);
    }

    void RefreshEverything()
    {
        if (!built || ShownCity == null) return;
        mapView.RefreshAll(city);
        RefreshTopBar();
        RefreshLeaderboard();
        RefreshLegend();
        RefreshSidePanel();
        if (pendingFocus != null && mapView.Map.PlotsById.TryGetValue(pendingFocus, out var def))
        {
            pendingFocus = null;
            SelectPlot(def);
            mapView.FocusOn(def);
        }
    }

    // ---- Building -----------------------------------------------------------------------------------------------

    void Build()
    {
        built = true;
        root = document.rootVisualElement;
        CityUi.ApplyRootStyle(root);
        root.pickingMode = PickingMode.Position;
        root.style.backgroundColor = CityUi.MapPaper;
        CityUi.Fill(root);

        pickerLayer = BuildPicker();
        root.Add(pickerLayer);

        cityLayer = new VisualElement();
        CityUi.Fill(cityLayer);
        cityLayer.style.display = DisplayStyle.None;
        root.Add(cityLayer);

        mapHost = new VisualElement();
        CityUi.Absolute(mapHost, 0, 92);
        mapHost.style.right = 0;
        mapHost.style.bottom = 0;
        cityLayer.Add(mapHost);

        cityLayer.Add(BuildTopBar());
        cityLayer.Add(BuildLegend());
        cityLayer.Add(BuildZoomControls());
        cityLayer.Add(BuildLeaderboard());

        sidePanel = BuildSidePanel();
        cityLayer.Add(sidePanel);

        tooltip = new VisualElement();
        tooltip.pickingMode = PickingMode.Ignore;
        tooltip.style.position = Position.Absolute;
        tooltip.style.backgroundColor = CityUi.Card;
        CityUi.Border(tooltip, 2, CityUi.Ink);
        CityUi.Radius(tooltip, 10, 14, 9, 15);
        CityUi.Padding(tooltip, 10, 16);
        tooltip.style.maxWidth = 380;
        tooltip.style.display = DisplayStyle.None;
        cityLayer.Add(tooltip);

        toastLayer = new VisualElement();
        toastLayer.pickingMode = PickingMode.Ignore;
        toastLayer.style.position = Position.Absolute;
        toastLayer.style.left = 0;
        toastLayer.style.right = 0;
        toastLayer.style.bottom = 36;
        toastLayer.style.alignItems = Align.Center;
        root.Add(toastLayer);

        loadingOverlay = BuildLoadingOverlay();
        root.Add(loadingOverlay);

        modalLayer = new VisualElement();
        modalLayer.pickingMode = PickingMode.Ignore;
        CityUi.Fill(modalLayer);
        root.Add(modalLayer);

        // Rent ticks up on screen every second
        root.schedule.Execute(() => { if (open && ShownCity != null) RefreshRent(); }).Every(1000);
    }

    /// <summary>A button with a small caption over a value ("Bank" / "$1.2B").</summary>
    static CityUi.InkButton StatButton(string icon, string caption, out Label value, Action onClick, CityUi.ButtonKind kind)
    {
        var button = new CityUi.InkButton(null, onClick, kind, icon, 60, 26);
        var column = CityUi.Column(0);
        column.pickingMode = PickingMode.Ignore;
        column.Add(CityUi.Text(caption, 15, CityUi.InkSoft));
        value = CityUi.OneLine(CityUi.Text("", 24, CityUi.Ink, true));
        column.Add(value);
        button.Face.Add(column);
        return button;
    }

    VisualElement BuildTopBar()
    {
        var bar = CityUi.Row(12);
        CityUi.Absolute(bar, 0, 0);
        bar.style.right = 0;
        bar.style.height = 92;
        bar.style.backgroundColor = CityUi.Paper;
        bar.style.borderBottomWidth = 3;
        bar.style.borderBottomColor = CityUi.Ink;
        CityUi.Padding(bar, 0, 20);

        var back = new CityUi.InkButton("Cities", ShowPicker, CityUi.ButtonKind.Paper, "back", 60, 22);
        bar.Add(back);

        var title = CityUi.Column(0);
        title.style.flexShrink = 0;
        // The city's name changes city (like the design's "London: change city")
        title.RegisterCallback<ClickEvent>(_ => ShowPicker());
        title.RegisterCallback<PointerEnterEvent>(_ => title.style.translate = new Translate(0, -2));
        title.RegisterCallback<PointerLeaveEvent>(_ => title.style.translate = new Translate(0, 0));
        var titleRow = CityUi.Row(10);
        cityTitle = CityUi.OneLine(CityUi.Text("", 36, CityUi.Ink, true));
        titleRow.Add(cityTitle);
        testLabel = CityUi.Text("TEST", 15, CityUi.Card, true);
        testLabel.style.backgroundColor = CityUi.Blue;
        CityUi.Radius(testLabel, 6);
        CityUi.Padding(testLabel, 2, 8);
        testLabel.style.rotate = new Rotate(-4);
        titleRow.Add(testLabel);
        title.Add(titleRow);
        cityCountry = CityUi.Text("", 15, CityUi.Muted);
        title.Add(cityCountry);
        bar.Add(title);

        marketChip = CityUi.Chip("", CityUi.PastelBlue, "people", 17);
        bar.Add(marketChip);

        bar.Add(CityUi.Spacer());

        // My company (or the join button)
        companyButton = new CityUi.InkButton(null, () => OpenCompanyDialog(false), CityUi.ButtonKind.Paper, null, 60, 22);
        companyLogo = new CityLogoView(null, 46);
        companyButton.Face.Add(companyLogo);
        companyName = CityUi.OneLine(CityUi.Text("", 22, CityUi.Ink, true));
        companyName.style.maxWidth = 190;
        companyButton.Face.Add(companyName);
        bar.Add(companyButton);

        joinBox = new CityUi.InkButton("Set up your company", () => OpenCompanyDialog(true), CityUi.ButtonKind.Primary, "building", 60, 22);
        bar.Add(joinBox);

        bankButton = StatButton("bank", "Bank", out balanceLabel, OpenBankDialog, CityUi.ButtonKind.Paper);
        inPlayLabel = CityUi.OneLine(CityUi.Text("", 14, CityUi.Card, true));
        inPlayLabel.style.position = Position.Absolute;
        inPlayLabel.style.top = -12;
        inPlayLabel.style.right = -8;
        inPlayLabel.style.backgroundColor = CityUi.Felt;
        CityUi.Border(inPlayLabel, 2, CityUi.Card);
        CityUi.Radius(inPlayLabel, 10);
        CityUi.Padding(inPlayLabel, 1, 8);
        bankButton.Add(inPlayLabel);
        bar.Add(bankButton);

        collectButton = StatButton("rent", "Rent - collect", out rentLabel, () => _ = CollectRent(), CityUi.ButtonKind.Primary);
        bar.Add(collectButton);

        // Inbox
        var inbox = new CityUi.InkButton(null, OpenInboxDialog, CityUi.ButtonKind.Paper, "mail", 60, 24);
        inbox.style.width = 72;
        inboxBadge = new VisualElement();
        inboxBadge.pickingMode = PickingMode.Ignore;
        inboxBadge.style.position = Position.Absolute;
        inboxBadge.style.top = -8;
        inboxBadge.style.right = -4;
        inboxBadge.style.minWidth = 28;
        inboxBadge.style.height = 28;
        inboxBadge.style.alignItems = Align.Center;
        inboxBadge.style.justifyContent = Justify.Center;
        inboxBadge.style.backgroundColor = CityUi.Red;
        CityUi.Border(inboxBadge, 2, CityUi.Card);
        CityUi.Radius(inboxBadge, 14);
        inboxBadgeText = CityUi.Text("", 15, CityUi.Card, true);
        inboxBadge.Add(inboxBadgeText);
        inbox.Add(inboxBadge);
        bar.Add(inbox);

        toolsButton = new CityUi.InkButton(null, OpenTestTools, CityUi.ButtonKind.Paper, "flask", 60, 24);
        toolsButton.style.width = 72;
        bar.Add(toolsButton);

        bar.Add(new CityUi.InkButton("Office", Hide, CityUi.ButtonKind.Paper, "building", 60, 22));
        return bar;
    }

    void RefreshTopBar()
    {
        if (!built || ShownCity == null) return;
        var map = mapView.Map;
        bool ready = city != null && city.IsReady;
        bool joined = ready && city.Joined;
        var account = city?.Account;

        cityTitle.text = map.Name;
        cityCountry.text = map.World.Country + " · change city";
        companyButton.style.display = joined ? DisplayStyle.Flex : DisplayStyle.None;
        bankButton.style.display = joined ? DisplayStyle.Flex : DisplayStyle.None;
        collectButton.style.display = joined ? DisplayStyle.Flex : DisplayStyle.None;
        joinBox.style.display = ready && !joined ? DisplayStyle.Flex : DisplayStyle.None;
        if (joined)
        {
            companyLogo.Set(account.Logo, map.CityId);
            companyName.text = account.CompanyName;
        }

        balanceLabel.text = joined ? CityRules.Money(account.Balance) : "-";
        inPlayLabel.text = joined && account.InPlay > 0 ? CityRules.Money(account.InPlay) + " on the table" : "";
        inPlayLabel.style.display = joined && account.InPlay > 0 ? DisplayStyle.Flex : DisplayStyle.None;

        int unread = joined ? account.Unread : 0;
        inboxBadge.style.display = unread > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        inboxBadgeText.text = unread > 9 ? "9+" : unread.ToString();

        // City summary from the map
        var owners = new HashSet<string>();
        int taken = 0;
        foreach (var district in city.Districts.Values)
        {
            foreach (var plot in district.Plots.Values)
            {
                if (plot == null) continue;
                taken++;
                owners.Add(plot.OwnerId);
            }
        }
        CityUi.SetChipText(marketChip, $"{owners.Count} companies · {taken}/{map.Plots.Count} owned");

        bool local = ready && !city.IsLive;
        testLabel.style.display = local ? DisplayStyle.Flex : DisplayStyle.None;
        toolsButton.style.display = local ? DisplayStyle.Flex : DisplayStyle.None;

        RefreshRent();
    }

    void RefreshRent()
    {
        if (!built || city == null || !city.IsReady || !city.Joined || ShownCity == null)
        {
            if (rentLabel != null) rentLabel.text = "-";
            collectButton?.SetEnabledLook(false);
            return;
        }
        city.RentInfo(out long ready, out _, out int plots, out _);
        rentLabel.text = plots == 0 ? "no plots yet" : CityRules.Money(ready);
        collectButton.SetEnabledLook(ready >= CityRules.MinTransfer);
    }

    VisualElement BuildLegend()
    {
        var card = CityUi.CardBox(CityUi.Card, 2);
        CityUi.Radius(card, 14, 20, 12, 22);
        CityUi.Padding(card, 8, 16);

        var switcher = CityUi.Row(10);
        switcher.style.marginBottom = 8;
        switcher.Add(CityUi.Text("Colour by", 17, CityUi.Muted, true));
        ownersButton = new CityUi.InkButton("Owners", () => SetColouring(CityMapView.Colouring.Owners), CityUi.ButtonKind.Paper, "people", 40, 17);
        pricesButton = new CityUi.InkButton("Prices", () => SetColouring(CityMapView.Colouring.Prices), CityUi.ButtonKind.Paper, "tag", 40, 17);
        switcher.Add(ownersButton);
        switcher.Add(pricesButton);
        card.Add(switcher);

        // What a price colour means: the cheapest plot on the map to the dearest
        pricesLegend = CityUi.Row(8);
        cheapLabel = CityUi.Text("", 17, CityUi.InkSoft, true);
        pricesLegend.Add(cheapLabel);
        foreach (var colour in CityMapView.PriceRamp)
        {
            var step = new VisualElement();
            step.style.width = 34;
            step.style.height = 22;
            step.style.backgroundColor = colour;
            CityUi.Border(step, 2, CityUi.Ink);
            CityUi.Radius(step, 5);
            pricesLegend.Add(step);
        }
        dearLabel = CityUi.Text("", 17, CityUi.InkSoft, true);
        pricesLegend.Add(dearLabel);
        pricesLegend.Add(CityUi.Text("to buy or to stake", 15, CityUi.Muted));
        card.Add(pricesLegend);

        var list = CityUi.Row(22);
        ownersLegend = list;
        list.Add(LegendRow(CityUi.Alpha(CityUi.Card, 0.8f), CityUi.Green, "Free"));
        list.Add(LegendRow(Color.Lerp(CityUi.Card, CityUi.Red, 0.3f), CityUi.Ink, "Owned"));
        list.Add(LegendRow(CityUi.Card, CityUi.Hex("D9A900"), "Yours"));
        var shielded = CityUi.Row(10);
        var badge = new VisualElement();
        badge.style.width = 26;
        badge.style.height = 26;
        badge.style.alignItems = Align.Center;
        badge.style.justifyContent = Justify.Center;
        badge.style.backgroundColor = CityUi.Blue;
        CityUi.Border(badge, 2, CityUi.Ink);
        CityUi.Radius(badge, 13);
        badge.Add(new CityUi.IconView("lock", 15, CityUi.Card, 3f));
        shielded.Add(badge);
        shielded.Add(CityUi.Text("Protected", 18));
        list.Add(shielded);
        card.Add(list);
        SetColouring(colouring);
        var wrapper = CityUi.Inked(card);
        wrapper.style.position = Position.Absolute;
        wrapper.style.left = 24;
        wrapper.style.bottom = 24;
        return wrapper;
    }

    void SetColouring(CityMapView.Colouring mode)
    {
        colouring = mode;
        if (mapView != null) mapView.Mode = mode;
        bool prices = mode == CityMapView.Colouring.Prices;
        ownersButton.Face.style.backgroundColor = prices ? CityUi.Card : CityUi.Highlight;
        pricesButton.Face.style.backgroundColor = prices ? CityUi.Highlight : CityUi.Card;
        ownersLegend.style.display = prices ? DisplayStyle.None : DisplayStyle.Flex;
        pricesLegend.style.display = prices ? DisplayStyle.Flex : DisplayStyle.None;
        RefreshLegend();
    }

    void RefreshLegend()
    {
        if (cheapLabel == null || mapView == null) return;
        cheapLabel.text = CityRules.Money(mapView.Cheapest);
        dearLabel.text = CityRules.Money(mapView.Dearest);
    }

    static VisualElement LegendRow(Color fill, Color border, string text)
    {
        var row = CityUi.Row(12);
        var swatch = new VisualElement();
        swatch.style.width = 30;
        swatch.style.height = 22;
        swatch.style.backgroundColor = fill;
        CityUi.Border(swatch, 3, border);
        CityUi.Radius(swatch, 5);
        row.Add(swatch);
        row.Add(CityUi.Text(text, 18));
        return row;
    }

    VisualElement BuildZoomControls()
    {
        var box = CityUi.Column(10);
        box.style.position = Position.Absolute;
        box.style.bottom = 24;
        box.style.right = 24;
        var plus = new CityUi.InkButton(null, () => mapView?.ZoomBy(1.35f), CityUi.ButtonKind.Paper, "plus", 52, 24);
        var minus = new CityUi.InkButton(null, () => mapView?.ZoomBy(1f / 1.35f), CityUi.ButtonKind.Paper, "minus", 52, 24);
        var fit = new CityUi.InkButton(null, () => mapView?.FitAll(), CityUi.ButtonKind.Paper, "fit", 52, 24);
        foreach (var b in new[] { plus, minus, fit }) b.style.width = 64;
        box.Add(plus);
        box.Add(minus);
        box.Add(fit);
        return box;
    }

    VisualElement BuildLeaderboard()
    {
        var card = CityUi.CardBox(CityUi.Card, 2);
        CityUi.Radius(card, 14, 20, 12, 22);
        CityUi.Padding(card, 12, 16);
        var header = CityUi.Row(10);
        header.Add(new CityUi.IconView("trophy", 26));
        header.Add(CityUi.Text("Biggest landlords", 22, CityUi.Ink, true));
        var arrow = CityUi.Text("show", 16, CityUi.Blue, true);
        arrow.style.marginLeft = 8;
        header.Add(arrow);
        card.Add(header);
        leaderList = CityUi.Column(6);
        leaderList.style.marginTop = 8;
        leaderList.style.width = 320;
        leaderList.style.display = DisplayStyle.None;
        card.Add(leaderList);
        // Folded by default so it doesn't cover the map; click to open or fold it
        header.RegisterCallback<ClickEvent>(_ =>
        {
            bool show = leaderList.style.display == DisplayStyle.None;
            leaderList.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            arrow.text = show ? "hide" : "show";
        });
        var wrapper = CityUi.Inked(card);
        wrapper.style.position = Position.Absolute;
        wrapper.style.left = 24;
        wrapper.style.top = 116;
        return wrapper;
    }

    void RefreshLeaderboard()
    {
        if (!built || leaderList == null || ShownCity == null) return;
        leaderList.Clear();
        var map = mapView.Map;
        var counts = new Dictionary<string, (int count, long value, string name, string logo)>();
        foreach (var district in city.Districts.Values)
        {
            foreach (var pair in district.Plots)
            {
                var plot = pair.Value;
                if (plot == null || !map.PlotsById.TryGetValue(pair.Key, out var def)) continue;
                counts.TryGetValue(plot.OwnerId, out var entry);
                counts[plot.OwnerId] = (entry.count + 1, entry.value + CityRules.PlotValue(def, plot), plot.OwnerName, plot.Logo);
            }
        }
        int rank = 1;
        foreach (var pair in counts.OrderByDescending(p => p.Value.value).Take(5))
        {
            var row = CityUi.Row(10);
            row.Add(CityUi.Text(rank + ".", 18, CityUi.Muted));
            row.Add(new CityLogoView(pair.Value.logo, 30, map.CityId));
            var name = CityUi.OneLine(CityUi.Text(pair.Value.name, 18, CityUi.Ink, pair.Key == city.PlayerId));
            name.style.flexGrow = 1;
            name.style.flexShrink = 1;
            row.Add(name);
            row.Add(CityUi.Text($"{pair.Value.count} · {CityRules.Money(pair.Value.value)}", 16, CityUi.InkSoft));
            if (pair.Key == city.PlayerId) row.style.backgroundColor = CityUi.Alpha(CityUi.Highlight, 0.6f);
            leaderList.Add(row);
            rank++;
        }
    }

    VisualElement BuildLoadingOverlay()
    {
        var overlay = new VisualElement();
        CityUi.Fill(overlay);
        overlay.style.backgroundColor = CityUi.Alpha(CityUi.MapPaper, 0.92f);
        overlay.style.alignItems = Align.Center;
        overlay.style.justifyContent = Justify.Center;
        overlay.style.display = DisplayStyle.None;

        var card = CityUi.CardBox();
        card.style.width = 760;
        CityUi.Padding(card, 36, 44);
        card.style.alignItems = Align.Center;
        card.Add(new CityUi.IconView("globe", 80));
        loadingTitle = CityUi.Text("", 40, CityUi.Ink, true);
        loadingTitle.style.marginTop = 16;
        loadingTitle.style.unityTextAlign = TextAnchor.MiddleCenter;
        card.Add(loadingTitle);
        loadingText = CityUi.Text("", 22, CityUi.InkSoft);
        loadingText.style.marginTop = 12;
        loadingText.style.unityTextAlign = TextAnchor.MiddleCenter;
        card.Add(loadingText);
        loadingButtons = CityUi.Row(16);
        loadingButtons.style.marginTop = 24;
        card.Add(loadingButtons);
        overlay.Add(CityUi.Lifted(card));
        return overlay;
    }

    void ShowLoading(string title, string text, bool failed)
    {
        loadingOverlay.style.display = DisplayStyle.Flex;
        loadingTitle.text = title;
        loadingText.text = text ?? "";
        loadingText.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
        loadingButtons.Clear();
        if (!failed) return;
        loadingButtons.Add(new CityUi.InkButton("Try again", () => _ = Retry(false), CityUi.ButtonKind.Primary, "refresh", 60, 24));
        if (CityOnline.LocalTestCityAllowed)
        {
            string label = CityOnline.UseLocalTestCity ? "Use the real cities" : "Use the local test cities";
            loadingButtons.Add(new CityUi.InkButton(label, () => _ = Retry(true), CityUi.ButtonKind.Paper, "flask", 60, 22));
        }
        loadingButtons.Add(new CityUi.InkButton("Back to office", Hide, CityUi.ButtonKind.Paper, "back", 60, 22));
    }

    async Task Retry(bool switchMode)
    {
        if (switchMode) CityOnline.UseLocalTestCity = !CityOnline.UseLocalTestCity;
        string target = city.CityId ?? ShownCity;
        ShowLoading("Connecting to the cities…", null, false);
        await city.Reconnect();
        if (!open) return;
        if (!city.IsReady)
        {
            ShowLoading("Couldn't reach the cities", city.LastError, true);
            return;
        }
        HideLoading();
        if (target != null) await GoToCity(target);
        else ShowPicker();
    }

    void HideLoading() => loadingOverlay.style.display = DisplayStyle.None;

    // ---- Hover tooltip ------------------------------------------------------------------------------------------

    void OnHover(CityPlotDef def, Vector2 localInMap)
    {
        if (def == null || modals.Count > 0)
        {
            HideTooltip();
            return;
        }
        tooltip.Clear();
        var district = mapView.Map.DistrictsById[def.DistrictId];
        tooltip.Add(CityUi.Text($"Plot #{def.Number} · {district.Name}", 20, CityUi.Ink, true));
        var plot = city?.PlotState(def);
        string line;
        if (plot == null) line = $"Free · {CityRules.Money(def.BasePrice)}";
        else if (city.IsMine(plot)) line = "Yours" + (plot.Defended > 0 ? $" · held out {plot.Defended}x" : "");
        else line = $"{plot.OwnerName} · win it for a {CityRules.Money(CityRules.ChallengeStake(def, plot))} stake";
        tooltip.Add(CityUi.Text(line, 18, CityUi.InkSoft));
        if (def.Name != null) tooltip.Add(CityUi.Text(def.Name, 18, CityUi.GoldDark, true));

        var pos = localInMap + new Vector2(0, 92);
        tooltip.style.left = Mathf.Min(pos.x + 22, 1920 - 400);
        tooltip.style.top = pos.y + 22;
        tooltip.style.display = DisplayStyle.Flex;
    }

    void HideTooltip()
    {
        if (tooltip != null) tooltip.style.display = DisplayStyle.None;
    }

    // ---- Toasts -------------------------------------------------------------------------------------------------

    public void Toast(string message, bool good = true)
    {
        if (!built || string.IsNullOrEmpty(message)) return;
        var card = CityUi.CardBox(good ? CityUi.Card : CityUi.PastelRed, 2);
        CityUi.Radius(card, 12, 16, 11, 17);
        CityUi.Padding(card, 12, 20);
        var row = CityUi.Row(12);
        row.Add(new CityUi.IconView(good ? "check" : "warning", 26, good ? CityUi.Green : CityUi.Red));
        var text = CityUi.Text(message, 21);
        text.style.maxWidth = 900;
        row.Add(text);
        card.Add(row);
        var toast = CityUi.Inked(card);
        toast.style.marginTop = 10;
        toast.pickingMode = PickingMode.Ignore;
        toastLayer.Add(toast);
        while (toastLayer.childCount > 4) toastLayer.RemoveAt(0);
        toast.schedule.Execute(() => toast.RemoveFromHierarchy()).StartingIn(good ? 3500 : 5500);
    }

    /// <summary>Run a city call with a busy button, then show its message.</summary>
    async Task<CityResult> Run(Func<Task<CityResult>> call, CityUi.InkButton button = null, string busyText = "Working…")
    {
        string original = button?.Label?.text;
        if (button != null)
        {
            button.SetEnabledLook(false);
            if (original != null) button.SetText(busyText);
        }
        CityResult result;
        try
        {
            result = await call();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            result = CityResult.Fail("Something went wrong. Try again.");
        }
        if (button != null)
        {
            button.SetEnabledLook(true);
            if (original != null) button.SetText(original);
        }
        if (result != null)
        {
            if (result.Ok && !string.IsNullOrEmpty(result.Message)) Toast(result.Message, true);
            else if (!result.Ok) Toast(result.Error, false);
        }
        return result;
    }

    async Task CollectRent()
    {
        var result = await Run(() => city.CollectRent(), collectButton, "Collecting…");
        if (result != null && result.Ok) RefreshRent();
    }

    /// <summary>Editor snapshots: show a screen state by name.</summary>
    public async Task DebugShow(string what, string arg = null, float zoom = 0f)
    {
        CityPlotDef def = null;
        mapView?.Map.PlotsById.TryGetValue(arg ?? "", out def);
        switch (what)
        {
            case "picker": ShowPicker(); break;
            case "city": await GoToCity(arg); break;
            case "company": OpenCompanyDialog(false); break;
            case "join": OpenCompanyDialog(true); break;
            case "presets": OpenCompanyDialog(false, false); break;
            case "bank": OpenBankDialog(); break;
            case "inbox": OpenInboxDialog(); break;
            case "tools": OpenTestTools(); break;
            case "challenge": if (def != null) OpenChallengeDialog(def); break;
            case "select":
                if (def == null) break;
                SelectPlot(def);
                mapView.FocusOn(def, zoom > 0 ? zoom : 0.9f);
                break;
            case "fit":
                SelectPlot(null);
                mapView?.FitAll();
                break;
            case "zoom":
                mapView?.FitAll();
                mapView?.ZoomBy(zoom);
                break;
            case "prices": SetColouring(CityMapView.Colouring.Prices); break;
            case "owners": SetColouring(CityMapView.Colouring.Owners); break;
            case "close": CloseAllModals(); break;
        }
    }

    // ---- Modals -------------------------------------------------------------------------------------------------

    VisualElement PushModal(VisualElement dialog, Action onClose = null)
    {
        HideTooltip();
        var scrim = CityUi.Modal(modalLayer, dialog, () => CloseTopModal());
        modals.Add(scrim);
        modalCloseHandlers.Add(onClose);
        return scrim;
    }

    void CloseTopModal()
    {
        if (modals.Count == 0) return;
        int i = modals.Count - 1;
        var scrim = modals[i];
        var handler = modalCloseHandlers[i];
        modals.RemoveAt(i);
        modalCloseHandlers.RemoveAt(i);
        scrim.RemoveFromHierarchy();
        handler?.Invoke();
    }

    void CloseModal(VisualElement scrim)
    {
        int i = modals.IndexOf(scrim);
        if (i < 0) return;
        var handler = modalCloseHandlers[i];
        modals.RemoveAt(i);
        modalCloseHandlers.RemoveAt(i);
        scrim.RemoveFromHierarchy();
        handler?.Invoke();
    }

    void CloseAllModals()
    {
        while (modals.Count > 0) CloseTopModal();
    }
}
