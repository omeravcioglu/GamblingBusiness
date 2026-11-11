using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Random = UnityEngine.Random;

/// <summary>
/// The slot machine screen (design board Slots): the red cabinet with the SLOTS marquee, three reels of drawn symbols
/// behind the payline, the result banner, the bet (- / +) and SPIN (the lever spins too), the coin tray, the payout card
/// (the machine's own multipliers; the row the last spin hit is marked "This spin") and Back to casino.
/// Drives SlotMachineGame: SPIN calls Spin; on SpinStarted the reels spin and land one by one on the game's
/// ReelStopTime schedule (real time: the casino pauses the game); SpinFinished (the moment the spin is paid) shows the
/// result. Built at runtime by Build unless a designed screen fills the references.
/// </summary>
public class CasinoSlotsView : CasinoScreen
{
    /// <summary>One reel: a masked window whose cells show the strip of symbols scrolling down.</summary>
    [Serializable]
    public class Reel
    {
        [Tooltip("Four symbol cells, reused as the strip scrolls (the window masks them)")]
        public CasinoIcon[] cells = new CasinoIcon[4];

        [NonSerialized] public int[] strip;
        [NonSerialized] public float position;
        [NonSerialized] public bool spinning;
        [NonSerialized] public bool decelerating;
        [NonSerialized] public float startTime;
        [NonSerialized] public float stopTime;
        [NonSerialized] public float decelStart;
        [NonSerialized] public float decelFrom;
        [NonSerialized] public int landing;
        [NonSerialized] public int result;
        [NonSerialized] public float blur;
    }

    const int StripLength = 24;
    const float SymbolPitch = 139f;
    const float WindowCenterY = 127f;
    const float SpinSpeed = 14f;          // symbols per second
    const float DecelSeconds = 0.55f;
    const float CenterSymbol = 140f;
    const float SideSymbol = 100f;

    [Tooltip("Empty = the casino's slot machine")]
    public SlotMachineGame game;

    [Header("Machine")]
    public Reel[] reels = { new Reel(), new Reel(), new Reel() };
    public TMP_Text bannerText;
    [Tooltip("The sparkles either side of a win")]
    public RectTransform[] bannerSparkles = new RectTransform[2];
    public CasinoButton spinButton;
    [Tooltip("The lever spins too, and is pulled when a spin starts")]
    public Button leverButton;
    public CasinoIcon lever;
    [Tooltip("Coins in the tray: a few at rest, more after a win")]
    public RectTransform[] trayCoins = new RectTransform[0];

    [Header("Bet")]
    public CasinoButton betMinusButton;
    public CasinoButton betPlusButton;
    public TMP_Text betText;

    [Header("Payout card (top to bottom: the rarest symbol first, any two last)")]
    public TMP_Text[] payoutValues = new TMP_Text[0];
    [Tooltip("Highlighter band and \"This spin\" tag of each row")]
    public GameObject[] payoutMarks = new GameObject[0];

    SlotMachineGame subscribed;
    float leverStart = -10f;
    float trayStart = -10f;
    int trayCount = 4;
    long shownBet = -1;
    bool shownSpinning;
    string bannerMessage;
    Func<string> bannerWriter;   // writes the banner again after a language change (null: a message from the game, shown as it came)
    Color bannerColor;
    bool bannerSparkling;

    SlotMachineGame Game
    {
        get
        {
            if (game != null) return game;
            CasinoManager casino = Manager;
            if (casino != null && casino.slotMachine != null) return casino.slotMachine;
            return FindAnyObjectByType<SlotMachineGame>(FindObjectsInactive.Include);
        }
    }

    public override bool IsBusy
    {
        get
        {
            foreach (Reel reel in reels)
                if (reel != null && reel.spinning) return true;
            return false;
        }
    }

    protected override void OnWire()
    {
        if (spinButton != null && spinButton.button != null) spinButton.button.onClick.AddListener(Spin);
        if (leverButton != null) leverButton.onClick.AddListener(Spin);
        if (betMinusButton != null && betMinusButton.button != null) betMinusButton.button.onClick.AddListener(() => Step(-1));
        if (betPlusButton != null && betPlusButton.button != null) betPlusButton.button.onClick.AddListener(() => Step(1));
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        shownBet = -1;
        Subscribe(Game);
        PrepareReels();
        SlotMachineGame g = Game;
        if (g != null && g.IsSpinning) ShowBanner(() => L10n.Get(T, "casino.slots.banner.spinning"), UIColors.WithAlpha(UIColors.Gold, 0.75f), false);
        else ShowIdleBanner();
        MarkRow(-1);
        SetTray(4, false);
        FillPayouts();
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        Subscribe(null);
    }

    // The fonts switch by themselves; the banner is written again (a message from the game stays as it came)
    protected override void OnLanguageRefresh()
    {
        if (bannerWriter != null) ShowBanner(bannerWriter, bannerColor, bannerSparkling);
    }

    void Subscribe(SlotMachineGame target)
    {
        if (subscribed == target) return;
        if (subscribed != null)
        {
            subscribed.SpinStarted -= OnSpinStarted;
            subscribed.SpinFinished -= OnSpinFinished;
            subscribed.SpinRefused -= OnSpinRefused;
        }
        subscribed = target;
        if (subscribed != null)
        {
            subscribed.SpinStarted += OnSpinStarted;
            subscribed.SpinFinished += OnSpinFinished;
            subscribed.SpinRefused += OnSpinRefused;
        }
    }

    void Step(int direction)
    {
        CasinoManager casino = Manager;
        if (casino != null && !IsBusy) casino.StepBet(direction);
    }

    void Spin()
    {
        SlotMachineGame g = Game;
        if (g == null || g.IsSpinning || IsBusy) return;
        Subscribe(g);
        g.Spin();   // raises SpinStarted, or SpinRefused with the reason
    }

    // ------------------------------------------------------------------
    // Game events
    // ------------------------------------------------------------------

    void OnSpinStarted(long bet, int[] results)
    {
        SlotMachineGame g = Game;
        float now = Time.unscaledTime;
        for (int i = 0; i < reels.Length && i < 3; i++)
        {
            Reel reel = reels[i];
            if (reel == null) continue;
            EnsureStrip(reel);
            reel.result = results != null && i < results.Length ? results[i] : 0;
            reel.spinning = true;
            reel.decelerating = false;
            reel.startTime = now;
            reel.stopTime = now + (g != null ? g.ReelStopTime(i) : 2f + i * 0.3f);
            reel.position = Mathf.Round(reel.position);
        }
        leverStart = now;
        MarkRow(-1);
        SetTray(4, false);
        ShowBanner(() => L10n.Get(T, "casino.slots.banner.spinning"), UIColors.WithAlpha(UIColors.Gold, 0.75f), false);
    }

    void OnSpinFinished(float multiplier, double winnings)
    {
        SlotMachineGame g = Game;
        for (int i = 0; i < reels.Length && i < 3; i++)
        {
            Reel reel = reels[i];
            if (reel == null) continue;
            if (g != null) reel.result = g.GetReelResult(i);
            Land(reel);
        }

        if (multiplier > 0f)
        {
            string amount = UIFormat.Money(winnings);
            string times = MultiplierNumber(multiplier);
            string key = multiplier >= 50f ? "casino.slots.banner.jackpot" : multiplier >= 10f ? "casino.slots.banner.big_win" : "casino.slots.banner.win";
            ShowBanner(() => L10n.Get(T, key, ("amount", amount), ("multiplier", times)), UIColors.Gold, true);
            SetTray(multiplier >= 5f ? 12 : 8, true);
        }
        else
        {
            ShowBanner(() => L10n.Get(T, "casino.slots.banner.no_match"), UIColors.WithAlpha(UIColors.PaperHi, 0.85f), false);
        }
        MarkRow(g != null ? RowFor(g.GetReelResult(0), g.GetReelResult(1), g.GetReelResult(2), g) : -1);
    }

    // The game's own reason (the freeze, not enough money), shown as it came
    void OnSpinRefused(string reason)
    {
        ShowBanner(reason, CasinoUI.SoftRed, false);
    }

    void ShowIdleBanner()
    {
        CasinoManager casino = Manager;
        if (casino != null && !casino.CanBetAtAll)
            ShowBanner(casino.NotEnoughMessage, CasinoUI.SoftRed, false);
        else
            ShowBanner(() => L10n.Get(T, "casino.slots.banner.idle"), UIColors.Gold, false);
    }

    /// <summary>A banner of this screen's own words, written again when the language changes.</summary>
    void ShowBanner(Func<string> writer, Color color, bool sparkles)
    {
        ShowBanner(writer(), color, sparkles);
        bannerWriter = writer;
    }

    void ShowBanner(string message, Color color, bool sparkles)
    {
        bannerWriter = null;
        bannerColor = color;
        bannerSparkling = sparkles;
        bannerMessage = message;
        if (bannerText != null)
        {
            bannerText.text = message;
            bannerText.color = color;
        }
        for (int i = 0; i < bannerSparkles.Length; i++)
        {
            RectTransform sparkle = bannerSparkles[i];
            if (sparkle == null) continue;
            if (sparkle.gameObject.activeSelf != sparkles) sparkle.gameObject.SetActive(sparkles);
            if (!sparkles || bannerText == null) continue;
            // Either side of the text
            bannerText.ForceMeshUpdate();
            float half = Mathf.Min(bannerText.textBounds.size.x * 0.5f, bannerText.rectTransform.rect.width * 0.5f);
            float x = bannerText.rectTransform.anchoredPosition.x + bannerText.rectTransform.rect.width * 0.5f;
            sparkle.anchoredPosition = new Vector2(x + (i == 0 ? -1f : 1f) * (half + 38f), sparkle.anchoredPosition.y);
        }
    }

    /// <summary>"500×" (a payout on the card; the same in every language).</summary>
    static string Multiplier(float value)
    {
        return MultiplierNumber(value) + "×";
    }

    /// <summary>"500", "2.5": a payout for a String Table entry's {multiplier}.</summary>
    static string MultiplierNumber(float value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    /// <summary>The payout row three symbols hit: 0 = the rarest symbol's three of a kind ... last = any two; -1 = none.</summary>
    static int RowFor(int a, int b, int c, SlotMachineGame g)
    {
        int count = g != null ? g.SymbolCount : CasinoIcon.SymbolCount;
        if (a == b && b == c) return count - 1 - a;
        if (a == b || b == c || a == c) return count;
        return -1;
    }

    void MarkRow(int row)
    {
        for (int i = 0; i < payoutMarks.Length; i++)
            if (payoutMarks[i] != null && payoutMarks[i].activeSelf != (i == row)) payoutMarks[i].SetActive(i == row);
    }

    void FillPayouts()
    {
        SlotMachineGame g = Game;
        if (g == null) return;
        int count = g.SymbolCount;
        for (int i = 0; i < payoutValues.Length; i++)
        {
            if (payoutValues[i] == null) continue;
            float value = i < count ? g.ThreeOfAKindPayout(count - 1 - i) : g.TwoOfAKindPayout;
            payoutValues[i].text = Multiplier(value);
        }
    }

    // ------------------------------------------------------------------
    // Reels
    // ------------------------------------------------------------------

    void PrepareReels()
    {
        SlotMachineGame g = Game;
        for (int i = 0; i < reels.Length; i++)
        {
            Reel reel = reels[i];
            if (reel == null) continue;
            EnsureStrip(reel);
            if (g != null && g.IsSpinning) continue;   // keeps spinning when the screen comes back mid-spin (it can't, but be safe)
            reel.spinning = false;
            reel.decelerating = false;
            reel.blur = 0f;
            int landing = Mathf.RoundToInt(reel.position);
            if (g != null && g.HasResult) reel.strip[Mod(landing, StripLength)] = g.GetReelResult(i);
            reel.position = landing;
            DrawReel(reel);
        }
    }

    void EnsureStrip(Reel reel)
    {
        if (reel.strip != null && reel.strip.Length == StripLength) return;
        reel.strip = new int[StripLength];
        int last = -1;
        for (int i = 0; i < StripLength; i++)
        {
            int symbol = Random.Range(0, CasinoIcon.SymbolCount);
            if (symbol == last) symbol = (symbol + 1 + Random.Range(0, CasinoIcon.SymbolCount - 1)) % CasinoIcon.SymbolCount;
            reel.strip[i] = symbol;
            last = symbol;
        }
        reel.position = 0f;
    }

    void Land(Reel reel)
    {
        EnsureStrip(reel);
        if (reel.spinning && reel.decelerating)
        {
            reel.position = reel.landing;
        }
        else
        {
            reel.landing = Mathf.CeilToInt(reel.position);
            reel.position = reel.landing;
        }
        reel.strip[Mod(reel.landing, StripLength)] = reel.result;
        reel.spinning = false;
        reel.decelerating = false;
        reel.blur = 0f;
        DrawReel(reel);
    }

    static int Mod(int value, int length)
    {
        int m = value % length;
        return m < 0 ? m + length : m;
    }

    void Update()
    {
        float now = Time.unscaledTime;
        foreach (Reel reel in reels)
        {
            if (reel == null || !reel.spinning) continue;
            AnimateReel(reel, now);
            DrawReel(reel);
        }

        // The lever: pulled down fast, back up slower
        if (lever != null)
        {
            float t = now - leverStart;
            float pull = t < 0.18f ? CasinoUI.EaseOut(t / 0.18f) : t < 0.6f ? 1f - CasinoUI.EaseOut((t - 0.18f) / 0.42f) : 0f;
            lever.Amount = pull;
        }

        // Coins popping into the tray after a win
        if (trayCoins != null && now - trayStart < 1.2f)
        {
            for (int i = 0; i < trayCoins.Length; i++)
            {
                RectTransform coin = trayCoins[i];
                if (coin == null || !coin.gameObject.activeSelf) continue;
                float local = Mathf.Clamp01((now - trayStart - (i - 4) * 0.06f) / 0.3f);
                float scale = i < 4 ? 1f : CasinoUI.EaseOutBack(local);
                coin.localScale = new Vector3(scale, scale, 1f);
            }
        }
    }

    void AnimateReel(Reel reel, float now)
    {
        float decelAt = reel.stopTime - DecelSeconds;
        if (!reel.decelerating)
        {
            reel.position += SpinSpeed * Time.unscaledDeltaTime;
            reel.blur = Mathf.Clamp01((now - reel.startTime) / 0.25f);
            if (now >= decelAt)
            {
                // Pick the landing spot far enough ahead to slow down into, and put the result there
                reel.decelerating = true;
                reel.decelStart = now;
                reel.decelFrom = reel.position;
                float left = Mathf.Max(0.05f, reel.stopTime - now);
                reel.landing = Mathf.CeilToInt(reel.position + SpinSpeed * left / 3f) + 1;
                reel.strip[Mod(reel.landing, StripLength)] = reel.result;
            }
            return;
        }

        float duration = Mathf.Max(0.05f, reel.stopTime - reel.decelStart);
        float u = Mathf.Clamp01((now - reel.decelStart) / duration);
        reel.position = Mathf.LerpUnclamped(reel.decelFrom, reel.landing, CasinoUI.EaseOutBack(u, 0.9f));
        reel.blur = Mathf.Clamp01(1f - u * 1.6f);
        if (u >= 1f)
        {
            reel.position = reel.landing;
            reel.spinning = false;
            reel.decelerating = false;
            reel.blur = 0f;
        }
    }

    /// <summary>Place the cells: the strip index at the payline gets the big size, its neighbours the small one.</summary>
    void DrawReel(Reel reel)
    {
        if (reel.cells == null || reel.strip == null) return;
        int floor = Mathf.FloorToInt(reel.position);
        float stretch = 1f + 0.22f * reel.blur;
        float alpha = 1f - 0.18f * reel.blur;
        for (int k = 0; k < reel.cells.Length; k++)
        {
            CasinoIcon cell = reel.cells[k];
            if (cell == null) continue;
            int index = floor + 2 - k;              // k = 0 is coming in at the top, k = 3 leaving at the bottom
            float offset = reel.position - index;   // pitches below the payline (-2 .. 2)
            float size = SideSymbol + (CenterSymbol - SideSymbol) * Mathf.Clamp01(1f - Mathf.Abs(offset));
            RectTransform rect = cell.rectTransform;
            rect.sizeDelta = new Vector2(size, size);
            rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, -(WindowCenterY + offset * SymbolPitch));
            rect.localScale = new Vector3(1f, stretch, 1f);
            cell.Kind = CasinoIcon.Symbol(reel.strip[Mod(index, StripLength)]);
            Color c = Color.white;
            c.a = alpha;
            if (cell.color != c) cell.color = c;
        }
    }

    void SetTray(int count, bool pop)
    {
        if (trayCoins == null) return;
        trayCount = Mathf.Clamp(count, 0, trayCoins.Length);
        for (int i = 0; i < trayCoins.Length; i++)
        {
            RectTransform coin = trayCoins[i];
            if (coin == null) continue;
            bool show = i < trayCount;
            if (coin.gameObject.activeSelf != show) coin.gameObject.SetActive(show);
            coin.localScale = pop && i >= 4 ? new Vector3(0f, 0f, 1f) : Vector3.one;
        }
        if (pop) trayStart = Time.unscaledTime;
    }

    // ------------------------------------------------------------------
    // Buttons and the bet
    // ------------------------------------------------------------------

    protected override void LateUpdate()
    {
        base.LateUpdate();
        CasinoManager casino = Manager;
        SlotMachineGame g = Game;
        bool spinning = IsBusy || (g != null && g.IsSpinning);
        long bet = casino != null ? casino.GetCurrentBet() : 0;
        if (bet != shownBet && betText != null)
        {
            shownBet = bet;
            betText.text = UIFormat.Money(bet);
        }
        bool canBet = casino != null && casino.CanBetAtAll;
        if (spinButton != null) spinButton.Interactable = !spinning && g != null && canBet;
        if (leverButton != null) leverButton.interactable = !spinning && g != null && canBet;
        if (betMinusButton != null) betMinusButton.Interactable = !spinning && casino != null && casino.CanLowerBet;
        if (betPlusButton != null) betPlusButton.Interactable = !spinning && casino != null && casino.CanRaiseBet;

        // After a spin, if the company can no longer bet, say so
        if (spinning != shownSpinning)
        {
            shownSpinning = spinning;
            if (!spinning && !canBet && casino != null && bannerMessage != casino.NotEnoughMessage && (g == null || !g.HasResult))
                ShowIdleBanner();
        }
    }

    // ------------------------------------------------------------------
    // Runtime layout (board Slots, 1920 x 1080)
    // ------------------------------------------------------------------

    /// <summary>Build the slots screen under <paramref name="parent"/> (an inactive root).</summary>
    public static CasinoSlotsView Build(RectTransform parent, TMP_FontAsset font)
    {
        RectTransform root = CasinoUI.FullRect(parent, "CasinoSlots");
        root.gameObject.SetActive(false);
        CasinoSlotsView view = root.gameObject.AddComponent<CasinoSlotsView>();
        view.font = font;
        CasinoUI.Felt(root, "Felt", true);
        RectTransform screen = CasinoUI.Stage(root);

        // The cabinet: red, 6 px gold border, 3 px ink ring, soft shadow
        BlueprintBox cabinet = CasinoUI.Box(screen, "Cabinet", UIColors.Loss, UIColors.Gold, 6f, CasinoUI.RadiusCard, 180f, 90f, 860f, 900f);
        cabinet.SetRings(UIColors.Ink, 3f, Color.clear, 0f);
        cabinet.SetShadow(CasinoUI.SheetShadow, new Vector2(0f, 22f), 48f);

        // Marquee
        BlueprintBox marquee = CasinoUI.Box(screen, "Marquee", UIColors.Ink, UIColors.Gold, 4f, CasinoUI.RadiusLarge, 330f, 146f, 560f, 124f);
        RectTransform lights = BlueprintUI.Stretch(BlueprintUI.NewRect("Lights", marquee.transform), 14f, 14f, 14f, 14f);
        CasinoIcon dots = lights.gameObject.AddComponent<CasinoIcon>();
        dots.Kind = CasinoIconKind.DotFrame;
        dots.StrokeWidth = 6f;
        dots.Amount = 6f;
        dots.color = UIColors.Highlight;
        dots.raycastTarget = false;
        TextMeshProUGUI title = CasinoUI.Text(marquee.transform, "Title", "", 84f, UIColors.Gold, font, 0f, 0f, 560f, 124f,
                                              TextAlignmentOptions.Center, true);
        title.characterSpacing = 18f;
        CasinoUI.Shrink(title, 48f);
        L10nText.Attach(title, T, "casino.slots.title");   // "SLOTS" (capitals written so in the String Table)

        BuildReels(view, screen);

        // Result banner
        BlueprintBox banner = CasinoUI.Box(screen, "Banner", UIColors.Ink, UIColors.Gold, 4f, CasinoUI.RadiusLarge, 238f, 630f, 744f, 80f);
        view.bannerText = CasinoUI.Shrink(CasinoUI.Text(banner.transform, "Text", L10n.Get(T, "casino.slots.banner.idle"), 48f, UIColors.Gold, font,
                                                        70f, 0f, 604f, 80f, TextAlignmentOptions.Center, true), 18f);
        view.bannerText.textWrappingMode = TextWrappingModes.NoWrap;
        view.bannerSparkles[0] = CasinoUI.Sparkle(banner.transform, 0f, 22f, 36f).rectTransform;
        view.bannerSparkles[1] = CasinoUI.Sparkle(banner.transform, 0f, 22f, 36f).rectTransform;

        // Bet: caption, - amount +; SPIN
        TextMeshProUGUI caption = CasinoUI.Text(screen, "BetCaption", "", 19f, UIColors.PaperHi, font, 238f, 744f, 272f, 26f);
        BlueprintUI.Caps(caption);
        L10nText.Attach(caption, T, "casino.slots.bet_caption");   // "BET" (capitals written so in the String Table)
        view.betMinusButton = CasinoUI.IconButton(screen, "BetMinus", CasinoIconKind.Minus, 238f, 778f, 56f, new Vector2(3f, 4f), UIColors.Ink);
        BlueprintBox amount = CasinoUI.Box(screen, "BetAmount", UIColors.Ink, UIColors.Gold, 3f, CasinoUI.RadiusSmall, 304f, 778f, 140f, 56f);
        view.betText = CasinoUI.Shrink(CasinoUI.Text(amount.transform, "Text", "$100", 36f, UIColors.Gold, font, 8f, 0f, 124f, 56f,
                                                     TextAlignmentOptions.Center, true), 14f);
        view.betPlusButton = CasinoUI.IconButton(screen, "BetPlus", CasinoIconKind.Plus, 454f, 778f, 56f, new Vector2(3f, 4f), UIColors.Ink);
        view.spinButton = CasinoUI.Button(screen, "Spin", L10n.Get(T, "casino.slots.button.spin"), CasinoIconKind.Spin, CasinoButton.Look.Gold, font, 48f, true,
                                          538f, 738f, 444f, 96f, new Vector2(5f, 6f), CasinoUI.FeltShadow, 40f, 40f);
        if (view.spinButton.label != null)
        {
            view.spinButton.label.characterSpacing = 8f;
            L10nText.Attach(view.spinButton.label, T, "casino.slots.button.spin");   // "SPIN"
        }

        // Coin tray: 4 coins at rest, up to 12 after a win
        BlueprintBox tray = CasinoUI.Box(screen, "CoinTray", UIColors.Ink, UIColors.Gold, 4f, new Vector4(10f, 10f, 36f, 36f), 350f, 862f, 520f, 72f);
        view.trayCoins = new RectTransform[12];
        float trayWidth = 12f * 30f + 11f * 8f;
        for (int i = 0; i < 12; i++)
        {
            // The first four in the middle, the rest alternately right and left, so any even count stays centred
            int extra = i - 4;
            int slot = i < 4 ? 4 + i : extra % 2 == 0 ? 8 + extra / 2 : 3 - extra / 2;
            float cx = (520f - trayWidth) * 0.5f + slot * 38f + 15f;
            BlueprintBox coin = CasinoUI.Circle(tray.transform, "Coin" + i, UIColors.Gold, UIColors.Ink, 2f, 30f, cx, 43f);
            coin.SetGradient(CasinoUI.GoldLight, new Vector2(0.38f, 0.32f));
            view.trayCoins[i] = coin.rectTransform;
        }

        // The lever (it spins too)
        RectTransform leverRect = CasinoUI.Rect(screen, "Lever", 1043f, 300f, 72f, 300f);
        view.lever = leverRect.gameObject.AddComponent<CasinoIcon>();
        view.lever.Kind = CasinoIconKind.Lever;
        view.lever.raycastTarget = true;
        view.leverButton = leverRect.gameObject.AddComponent<Button>();
        view.leverButton.transition = Selectable.Transition.None;
        view.leverButton.targetGraphic = view.lever;
        view.leverButton.navigation = new Navigation { mode = Navigation.Mode.None };

        BuildPayoutCard(view, screen, font);
        BuildGameHeader(view, screen, font, 1120f, 852f);
        return view;
    }

    static void BuildReels(CasinoSlotsView view, RectTransform screen)
    {
        BlueprintBox box = CasinoUI.Box(screen, "Reels", UIColors.Ink, UIColors.Gold, 4f, CasinoUI.RadiusSmall, 238f, 298f, 744f, 304f);
        for (int i = 0; i < 3; i++)
        {
            BlueprintBox window = CasinoUI.Box(box.transform, "Reel" + (i + 1), UIColors.PaperHi, Color.clear, 0f, BlueprintUI.Round(10f),
                                               22f + i * 240f, 22f, 220f, 260f);
            RectTransform mask = BlueprintUI.Stretch(BlueprintUI.NewRect("Strip", window.transform), 3f, 3f, 3f, 3f);
            mask.gameObject.AddComponent<RectMask2D>();
            Reel reel = view.reels[i] = new Reel();
            for (int k = 0; k < reel.cells.Length; k++)
                reel.cells[k] = CasinoIcon.Create(mask, CasinoIcon.Symbol(k), CenterSymbol, Color.white, 107f, WindowCenterY + (k - 1) * SymbolPitch);

            RectTransform shadeRect = BlueprintUI.Stretch(BlueprintUI.NewRect("Shade", window.transform), 3f, 3f, 3f, 3f);
            CasinoIcon shade = shadeRect.gameObject.AddComponent<CasinoIcon>();
            shade.Kind = CasinoIconKind.EdgeShade;
            shade.Amount = 26f;
            shade.color = UIColors.WithAlpha(UIColors.Ink, UIColors.SrgbAlpha(0.35f));
            shade.raycastTarget = false;
            BlueprintBox rim = BlueprintUI.Box(window.transform, "Rim", Color.clear, UIColors.Ink, 3f, BlueprintUI.Round(10f));
            BlueprintUI.Stretch(rim.rectTransform);
        }

        // The payline across the reels and its two pointers
        BlueprintBox payline = CasinoUI.Box(box.transform, "Payline", UIColors.Loss, Color.clear, 0f, Vector4.zero, -26f, 150f, 796f, 5f);
        payline.SetRings(UIColors.Ink, 1.5f, Color.clear, 0f);
        RectTransform left = CasinoUI.Rect(box.transform, "PointerLeft", -44f, 138f, 20f, 28f);
        CasinoIcon leftIcon = left.gameObject.AddComponent<CasinoIcon>();
        leftIcon.Kind = CasinoIconKind.Pointer;
        leftIcon.raycastTarget = false;
        RectTransform right = CasinoUI.Rect(box.transform, "PointerRight", 768f, 138f, 20f, 28f);
        right.localScale = new Vector3(-1f, 1f, 1f);
        right.pivot = new Vector2(0.5f, 0.5f);
        right.anchoredPosition = new Vector2(768f + 10f, -(138f + 14f));
        CasinoIcon rightIcon = right.gameObject.AddComponent<CasinoIcon>();
        rightIcon.Kind = CasinoIconKind.Pointer;
        rightIcon.raycastTarget = false;
    }

    static void BuildPayoutCard(CasinoSlotsView view, RectTransform screen, TMP_FontAsset font)
    {
        const int rows = CasinoIcon.SymbolCount + 1;
        const float rowHeight = 56f;
        float height = 3f + 32f + 68f + 16f + rows * rowHeight + 32f + 3f;
        BlueprintBox card = CasinoUI.Box(screen, "Payouts", UIColors.Paper, UIColors.Ink, 3f, CasinoUI.RadiusCard, 1120f, 150f, 620f, height);
        card.SetShadow(CasinoUI.SheetShadow, new Vector2(0f, 22f), 48f);
        const float x = 43f, width = 534f;
        L10nText.Attach(CasinoUI.Text(card.transform, "Title", "", 32f, UIColors.Ink, font, x, 35f, width, 38f, TextAlignmentOptions.MidlineLeft, true),
                        T, "casino.slots.payouts.title");   // "Payouts"
        L10nText.Attach(CasinoUI.Shrink(CasinoUI.Text(card.transform, "Subtitle", "", 19f, UIColors.InkSoft, font, x, 77f, width, 26f), 14f),
                        T, "casino.slots.payouts.subtitle");   // "Pays your bet × the multiplier."

        view.payoutValues = new TMP_Text[rows];
        view.payoutMarks = new GameObject[rows];
        float top = 3f + 32f + 68f + 16f;
        for (int row = 0; row < rows; row++)
        {
            RectTransform line = CasinoUI.Rect(card.transform, "Row" + row, x, top + row * rowHeight, width, rowHeight);
            bool anyTwo = row == rows - 1;

            // "This spin": the highlighter band (behind everything) and the tag
            GameObject mark = new GameObject("ThisSpin", typeof(RectTransform));
            mark.layer = line.gameObject.layer;
            RectTransform markRect = (RectTransform)mark.transform;
            markRect.SetParent(line, false);
            BlueprintUI.Stretch(markRect);
            BlueprintBox band = BlueprintUI.Box(markRect, "Band", RunEndUI.MarkYellow, Color.clear, 0f, Vector4.zero);
            BlueprintUI.Stretch(band.rectTransform, 0f, rowHeight * 0.12f, 0f, rowHeight * 0.12f);
            mark.SetActive(false);
            view.payoutMarks[row] = mark;

            if (anyTwo)
            {
                L10nText.Attach(CasinoUI.Shrink(CasinoUI.Text(line, "Label", "", 22f, UIColors.Ink, font, 0f, 0f, 148f, rowHeight), 14f),
                                T, "casino.slots.payouts.any_two");   // "Any 2 match"
            }
            else
            {
                int symbol = CasinoIcon.SymbolCount - 1 - row;
                for (int k = 0; k < 3; k++)
                    CasinoIcon.Create(line, CasinoIcon.Symbol(symbol), 44f, Color.white, 22f + k * 52f, rowHeight * 0.5f, 2.4f);
            }

            RectTransform leaderRect = CasinoUI.Rect(line, "Leader", 164f, rowHeight * 0.5f - 1.5f, width - 164f - 112f, 3f);
            CasinoIcon leader = leaderRect.gameObject.AddComponent<CasinoIcon>();
            leader.Kind = CasinoIconKind.Dots;
            leader.Amount = 4f;
            leader.color = UIColors.WithAlpha(UIColors.Ink, 0.4f);
            leader.raycastTarget = false;

            BlueprintBox tag = CasinoUI.Box(markRect, "Tag", UIColors.PaperHi, UIColors.Ink, 2f, CasinoUI.RadiusSmall, 164f, rowHeight * 0.5f - 15f, 0f, 30f);
            string thisSpin = L10n.Get(T, "casino.slots.payouts.this_spin");
            float tagText = Mathf.Ceil(CasinoUI.TextWidth(font, thisSpin, 17f)) + 2f;
            tag.rectTransform.sizeDelta = new Vector2(tagText + 24f, 30f);
            TextMeshProUGUI tagLabel = CasinoUI.Text(tag.transform, "Text", thisSpin, 17f, UIColors.Ink, font, 12f, 0f, tagText, 30f);
            L10nText.Attach(tagLabel, T, "casino.slots.payouts.this_spin");   // "This spin"
            // The tag stays as wide as its words (another language, another font): the label spans it, the tag follows the label
            BlueprintUI.Stretch(tagLabel.rectTransform, 12f, 0f, 12f, 0f);
            StatusFitWidth tagFit = tag.gameObject.AddComponent<StatusFitWidth>();
            tagFit.text = tagLabel;
            tagFit.padding = 26f;
            if (anyTwo) tag.rectTransform.anchoredPosition = new Vector2(150f, tag.rectTransform.anchoredPosition.y);

            view.payoutValues[row] = CasinoUI.Text(line, "Value", "", 32f, UIColors.Ink, font, width - 96f, 0f, 96f, rowHeight,
                                                   TextAlignmentOptions.MidlineRight, true);

            if (!anyTwo)
            {
                RectTransform rule = CasinoUI.Rect(line, "Rule", 0f, rowHeight - 2f, width, 2f);
                CasinoIcon dashes = rule.gameObject.AddComponent<CasinoIcon>();
                dashes.Kind = CasinoIconKind.Dashes;
                dashes.StrokeWidth = 6f;
                dashes.Amount = 5f;
                dashes.color = UIColors.WithAlpha(UIColors.Ink, 0.25f);
                dashes.raycastTarget = false;
            }
        }
    }
}
