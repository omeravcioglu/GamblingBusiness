using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The online cities' entry in the office: a tab hanging from the top of the screen (where the HUD design puts "Online"),
/// locked with a "$1B" badge until the company is worth a billion, and the certificate that celebrates the unlock.
/// Shown only while a company is loaded and no other panel is open. Created automatically.
/// </summary>
public class CityOfficeHud : MonoBehaviour
{
    public static CityOfficeHud Instance { get; private set; }

    /// <summary>Tab position on the 1920x1080 layout (the HUD design's "Online" slot).</summary>
    public static Vector2 TabPosition = new Vector2(1276, -12);

    UIDocument document;
    VisualElement root, tab, tabFace, lockBadge, newBadge, certificate, bubble;
    CityUi.IconView tabIcon;
    Label tabLabel;
    CityOnline city;
    bool certificateOpen, subscribed;
    float bubbleUntil;

    const string SeenKey = "City_TabSeen";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Instance != null) return;
        var go = new GameObject("CityOfficeHud");
        go.SetActive(false);
        DontDestroyOnLoad(go);
        var doc = go.AddComponent<UIDocument>();
        doc.panelSettings = CityUi.CreatePanelSettings(150);
        var hud = go.AddComponent<CityOfficeHud>();
        hud.document = doc;
        go.SetActive(true);
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
    }

    void OnDestroy()
    {
        if (city != null && subscribed) city.Unlocked -= OnUnlocked;
        HookNewHud(false);
        if (UIManager.Exists) UIManager.Instance.ClosePanel(this);
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        city = CityOnline.Ensure();
        city.Unlocked += OnUnlocked;
        subscribed = true;
        HookNewHud(true);
        Build();
    }

    // ---- The redesigned office HUD (OfficeHud) has its own "Online" tab -----------------------------------------
    // Found by name (reflection) so this keeps compiling while that HUD changes: its tab opens the City, and this
    // tab steps aside whenever that one is in the scene.

    static System.Type newHudType;
    static System.Reflection.FieldInfo newHudOnlineTab;
    Object newHud;
    float nextHudCheck;

    void HookNewHud(bool add)
    {
        if (newHudType == null) newHudType = System.Type.GetType("OfficeHud");
        var ev = newHudType?.GetEvent("OnlineCityRequested", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        if (ev == null) return;
        var handler = new System.Action(OpenFromNewHud);
        if (add) ev.AddEventHandler(null, handler);
        else ev.RemoveEventHandler(null, handler);
    }

    void OpenFromNewHud()
    {
        if (city != null && !city.IsUnlocked) city.ForceUnlock(); // That HUD only lets it be clicked at the unlock value
        CityScreen.Open();
    }

    bool NewHudHasOnlineTab()
    {
        if (newHudType == null) return false;
        if (Time.unscaledTime >= nextHudCheck || newHud == null)
        {
            nextHudCheck = Time.unscaledTime + 1f;
            newHud = FindAnyObjectByType(newHudType);
        }
        if (newHud == null) return false;
        if (newHudOnlineTab == null) newHudOnlineTab = newHudType.GetField("onlineTab");
        return newHudOnlineTab != null && newHudOnlineTab.GetValue(newHud) is Object tab && tab != null;
    }

    void Build()
    {
        root = document.rootVisualElement;
        CityUi.ApplyRootStyle(root);
        CityUi.Fill(root);
        root.pickingMode = PickingMode.Ignore;

        tab = new VisualElement();
        CityUi.Absolute(tab, TabPosition.x, TabPosition.y, 138, 94);
        tab.style.rotate = new Rotate(0.8f);

        var shadow = new VisualElement();
        shadow.pickingMode = PickingMode.Ignore;
        CityUi.Absolute(shadow, 0, 10, 138, 94);
        shadow.style.backgroundColor = new Color(0, 0, 0, 0.25f);
        CityUi.Radius(shadow, 0, 0, 6, 4);
        tab.Add(shadow);

        tabFace = new VisualElement();
        CityUi.Fill(tabFace);
        tabFace.style.alignItems = Align.Center;
        tabFace.style.justifyContent = Justify.FlexEnd;
        tabFace.style.paddingBottom = 10;
        CityUi.Radius(tabFace, 0, 0, 6, 4);
        tabFace.pickingMode = PickingMode.Ignore;
        tabIcon = new CityUi.IconView("globe", 30);
        tabFace.Add(tabIcon);
        tabLabel = CityUi.Text("Cities", 22);
        tabFace.Add(tabLabel);
        tab.Add(tabFace);

        lockBadge = CityUi.Row(3);
        lockBadge.pickingMode = PickingMode.Ignore;
        lockBadge.style.position = Position.Absolute;
        lockBadge.style.right = -18;
        lockBadge.style.top = 26;
        lockBadge.style.height = 32;
        lockBadge.style.backgroundColor = CityUi.Card;
        CityUi.Border(lockBadge, 2, CityUi.Ink);
        CityUi.Radius(lockBadge, 10, 14, 9, 15);
        CityUi.Padding(lockBadge, 0, 8);
        lockBadge.Add(new CityUi.IconView("lock", 16, CityUi.Ink, 2.8f));
        lockBadge.Add(CityUi.Text(CityRules.Money(CityRules.UnlockCompanyValue), 17, CityUi.Ink, true));
        tab.Add(lockBadge);

        newBadge = new VisualElement();
        newBadge.pickingMode = PickingMode.Ignore;
        newBadge.style.position = Position.Absolute;
        newBadge.style.right = -16;
        newBadge.style.top = 26;
        newBadge.style.height = 30;
        newBadge.style.justifyContent = Justify.Center;
        newBadge.style.backgroundColor = CityUi.Red;
        CityUi.Border(newBadge, 2, CityUi.Card);
        CityUi.Radius(newBadge, 15);
        CityUi.Padding(newBadge, 0, 10);
        newBadge.Add(CityUi.Text("NEW", 17, CityUi.Card, true));
        tab.Add(newBadge);

        tab.RegisterCallback<ClickEvent>(_ => OnTabClicked());
        tab.RegisterCallback<PointerEnterEvent>(_ => tabFace.style.translate = new Translate(0, 4));
        tab.RegisterCallback<PointerLeaveEvent>(_ => tabFace.style.translate = new Translate(0, 0));
        root.Add(tab);

        bubble = CityUi.CardBox(CityUi.Card, 2);
        CityUi.Radius(bubble, 10, 14, 9, 15);
        CityUi.Padding(bubble, 10, 14);
        bubble.pickingMode = PickingMode.Ignore;
        CityUi.Absolute(bubble, TabPosition.x - 120, 100, 380);
        bubble.Add(CityUi.Text($"The online cities open when your company is worth {CityRules.Money(CityRules.UnlockCompanyValue)}.", 19));
        bubble.style.display = DisplayStyle.None;
        root.Add(bubble);
    }

    void Update()
    {
        if (root == null || city == null) return;
        bool office = CompanyManager.Instance != null && SaveSystem.Instance != null && SaveSystem.Instance.IsGameLoaded;
        bool otherPanel = UIManager.Exists && UIManager.Instance.IsAnyPanelOpen(this);
        bool show = office && !CityScreen.IsOpen && !certificateOpen && !otherPanel && !NewHudHasOnlineTab();
        var display = show ? DisplayStyle.Flex : DisplayStyle.None;
        if (tab.style.display != display) tab.style.display = display;
        if (!show) bubble.style.display = DisplayStyle.None;
        else if (bubble.style.display == DisplayStyle.Flex && Time.unscaledTime > bubbleUntil) bubble.style.display = DisplayStyle.None;
        if (!show) return;

        bool unlocked = city.IsUnlocked;
        bool fresh = unlocked && PlayerPrefs.GetInt(SeenKey, 0) == 0;
        int look = (unlocked ? 1 : 0) + (fresh ? 2 : 0);
        if (look != lastLook)
        {
            lastLook = look;
            tabFace.style.backgroundColor = unlocked ? CityUi.Card : CityUi.Hex("EAE2CF");
            CityUi.Border(tabFace, unlocked ? 0 : 3, CityUi.Muted);
            tabFace.style.borderTopWidth = 0;
            tabIcon.Set("globe", unlocked ? CityUi.Ink : CityUi.Muted);
            tabLabel.style.color = unlocked ? CityUi.Ink : CityUi.Muted;
            lockBadge.style.display = unlocked ? DisplayStyle.None : DisplayStyle.Flex;
            newBadge.style.display = fresh ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // A certificate that couldn't show yet (e.g. a panel was open) shows as soon as it can
        if (city.ReachedUnlock && !city.CertificateShown) ShowCertificate();
    }

    int lastLook = -1;

    void OnTabClicked()
    {
        if (!city.IsUnlocked)
        {
            bubble.style.display = DisplayStyle.Flex;
            bubbleUntil = Time.unscaledTime + 4f;
            return;
        }
        PlayerPrefs.SetInt(SeenKey, 1);
        PlayerPrefs.Save();
        CityScreen.Open();
    }

    void OnUnlocked()
    {
        if (root != null && CompanyManager.Instance != null) ShowCertificate();
    }

    // ---- The certificate ---------------------------------------------------------------------------------------

    void ShowCertificate()
    {
        if (certificateOpen || city.CertificateShown || CityScreen.IsOpen) return;
        if (UIManager.Exists && UIManager.Instance.IsAnyPanelOpen(this)) return;

        // The designed certificate from the UI session, when MainGame has it; ours below is the fallback.
        // It pauses and closes itself (Escape and its close button count as Later), so certificateOpen stays false.
        if (BillionCertificateView.TryShow(CompanyNameUI.GetCompanyName(), CityRules.UnlockCompanyValue,
                () => { PlayerPrefs.SetInt(SeenKey, 1); CityScreen.Open(); }, null))
        {
            city.CertificateShown = true;
            return;
        }

        city.CertificateShown = true;
        certificateOpen = true;
        UIManager.Instance.OpenPanel(this, true, CloseCertificate);

        certificate = new VisualElement();
        CityUi.Fill(certificate);
        certificate.style.backgroundColor = CityUi.Scrim;
        certificate.style.alignItems = Align.Center;
        certificate.style.justifyContent = Justify.Center;
        certificate.pickingMode = PickingMode.Position;

        var frame = new VisualElement();
        frame.style.width = 1040;
        frame.style.backgroundColor = CityUi.Card;
        CityUi.Border(frame, 10, CityUi.GoldDark);
        CityUi.Radius(frame, 14, 20, 12, 22);
        CityUi.Padding(frame, 8);
        var inner = new VisualElement();
        CityUi.Border(inner, 2, CityUi.Ink);
        CityUi.Radius(inner, 6);
        CityUi.Padding(inner, 30, 40);
        inner.style.alignItems = Align.Center;
        frame.Add(inner);

        var title = CityUi.Text("Certificate of Obscene Wealth", 58, CityUi.Ink, true);
        inner.Add(title);
        var line = new VisualElement();
        line.style.width = 260;
        line.style.height = 2;
        line.style.backgroundColor = CityUi.GoldDark;
        line.style.marginTop = 8;
        line.style.marginBottom = 10;
        inner.Add(line);
        var certifies = CityUi.Text("THIS CERTIFIES THAT", 19, CityUi.Muted);
        certifies.style.letterSpacing = 4;
        inner.Add(certifies);
        string company = CompanyNameUI.GetCompanyName();
        var name = CityUi.Text(string.IsNullOrWhiteSpace(company) ? "YOUR COMPANY" : CityUi.Upper(company), 66, CityUi.Ink, true);
        name.style.borderBottomWidth = 2;
        name.style.borderBottomColor = CityUi.Ink;
        name.style.paddingLeft = 30;
        name.style.paddingRight = 30;
        inner.Add(name);
        var reached = CityUi.Row(0);
        reached.style.marginTop = 10;
        reached.Add(CityUi.Text("has reached ", 26));
        reached.Add(CityUi.Highlighted(CityRules.MoneyFull(CityRules.UnlockCompanyValue), 26));
        reached.Add(CityUi.Text(" in company value.", 26));
        inner.Add(reached);

        var list = CityUi.Column(10);
        list.style.marginTop = 24;
        list.Add(Bullet("pin", CityUi.PastelBlue, "Six online cities, from London to Shanghai"));
        list.Add(Bullet("pencil", CityUi.PastelYellow, "Buy plots and put your drawn logo on them"));
        list.Add(Bullet("cards", CityUi.PastelGreen, "Earn rent, win rivals' plots at blackjack"));
        inner.Add(list);

        var buttons = CityUi.Row(24);
        buttons.style.marginTop = 30;
        buttons.Add(new CityUi.InkButton("Pick a city", () =>
        {
            CloseCertificate();
            PlayerPrefs.SetInt(SeenKey, 1);
            CityScreen.Open();
        }, CityUi.ButtonKind.Primary, "globe", 68, 28));
        buttons.Add(new CityUi.InkButton("Later", CloseCertificate, CityUi.ButtonKind.Paper, null, 68, 28));
        inner.Add(buttons);

        var seal = new Seal();
        seal.style.position = Position.Absolute;
        seal.style.left = -40;
        seal.style.bottom = -30;
        seal.style.rotate = new Rotate(-6);
        frame.Add(seal);

        certificate.Add(CityUi.Lifted(frame));
        root.Add(certificate);
    }

    static VisualElement Bullet(string icon, Color colour, string text)
    {
        var row = CityUi.Row(16);
        var badge = new VisualElement();
        badge.style.width = 42;
        badge.style.height = 42;
        badge.style.alignItems = Align.Center;
        badge.style.justifyContent = Justify.Center;
        badge.style.backgroundColor = colour;
        CityUi.Border(badge, 2, CityUi.Ink);
        CityUi.Radius(badge, 21);
        badge.Add(new CityUi.IconView(icon, 24));
        row.Add(badge);
        row.Add(CityUi.Text(text, 26));
        return row;
    }

    void CloseCertificate()
    {
        if (!certificateOpen) return;
        certificateOpen = false;
        certificate?.RemoveFromHierarchy();
        certificate = null;
        if (UIManager.Exists) UIManager.Instance.ClosePanel(this);
    }

    /// <summary>The gold "$1B" seal with red ribbons.</summary>
    class Seal : VisualElement
    {
        public Seal()
        {
            style.width = 180;
            style.height = 220;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            var text = CityUi.Text("$1B", 40, CityUi.Ink, true);
            text.style.position = Position.Absolute;
            text.style.left = 0;
            text.style.right = 0;
            text.style.top = 60;
            text.style.unityTextAlign = TextAnchor.MiddleCenter;
            Add(text);
        }

        void Draw(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            p.lineJoin = LineJoin.Round;
            foreach (var ribbon in new[]
            {
                new[] { new Vector2(58, 120), new Vector2(34, 208), new Vector2(58, 194), new Vector2(76, 214), new Vector2(98, 128) },
                new[] { new Vector2(122, 120), new Vector2(146, 208), new Vector2(122, 194), new Vector2(104, 214), new Vector2(82, 128) },
            })
            {
                p.BeginPath();
                p.MoveTo(ribbon[0]);
                for (int i = 1; i < ribbon.Length; i++) p.LineTo(ribbon[i]);
                p.ClosePath();
                p.fillColor = CityUi.Red;
                p.Fill();
                p.strokeColor = CityUi.Ink;
                p.lineWidth = 3;
                p.Stroke();
            }
            Circle(p, 78, CityUi.Gold, CityUi.Ink, 3);
            Circle(p, 58, CityUi.Hex("F2CF6B"), CityUi.Ink, 2.5f);
            p.strokeColor = CityUi.GoldDark;
            p.lineWidth = 2;
            p.BeginPath();
            p.Arc(new Vector2(90, 84), 68, Angle.Degrees(0), Angle.Degrees(360));
            p.Stroke();
        }

        static void Circle(Painter2D p, float r, Color fill, Color stroke, float width)
        {
            p.BeginPath();
            p.Arc(new Vector2(90, 84), r, Angle.Degrees(0), Angle.Degrees(360));
            p.ClosePath();
            p.fillColor = fill;
            p.Fill();
            p.strokeColor = stroke;
            p.lineWidth = width;
            p.Stroke();
        }
    }
}
