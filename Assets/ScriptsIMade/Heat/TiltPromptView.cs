using System;
using System.Collections;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// "Win it back?": right after a voluntary gamble is lost while the company runs hot, the game offers one coin flip for
/// the money just lost. The tilt the title asks about, as a button.
/// - When: a loss reported to GambleLedger (not by this prompt's own flips) while the heat is at least minHeat (60, Hot)
///   and the board hasn't frozen gambling, at least cooldownSeconds (45, real time) after the last prompt closed. It
///   waits for a calm screen (RunEndDirector.IsScreenCalm: no panel, crisis, risk card, build mode, bet in play...; no
///   mouse button held or just let go) for calmSeconds, and is dropped if that takes longer than maxWaitSeconds (10) or
///   a win or push is reported meanwhile. Heat and freeze are checked again then. Build mode's budget gamble pop-up, if
///   still up (idle, showing its last result), is closed first.
/// - "Win it back": a coin flip (headsChance 50%) for the amount lost, at most company value - reserve ($1,000). The
///   money is taken when the coin is tossed and settled when its face shows: heads pays twice the bet, tails loses it.
///   The flip reports to GambleLedger as source "tilt", so it adds heat like any bet, and it never starts a new prompt by
///   itself: after tails the result offers the next flip (for that flip's bet), at most maxFlipsInARow (2) flips in a
///   row, while the heat and the freeze still allow it. The texts count the whole chain (the loss plus the flips lost
///   since): "back to even" only when a heads wins all of it back, else "still down $X". A furniture loss was build
///   budget, so its flip wins it back as company value (never "back to even").
/// - "Walk away" (or Escape): heat -5 and a small toast. Escape does nothing while the coin is in the air.
/// - A pausing panel (UIManager). With Root empty it is built at runtime on its own overlay canvas: a felt table, a paper
///   note with the offer and a gold coin. A designed prefab can fill the references instead.
/// - Texts come from the "Heat" String Table (heat.tilt.*) through L10n. The kicker is set in capitals with the
///   language's casing rules (L10n.ToUpper; TMP's UpperCase style would turn a Turkish "i" into "I"). When the language
///   changes while the prompt is open, its texts are written again and every text follows the language's font.
/// </summary>
[DisallowMultipleComponent]
public class TiltPromptView : MonoBehaviour
{
    [Header("When it shows")]
    [Tooltip("Heat the company needs for a lost gamble to bring the offer (60 = Hot)")]
    public float minHeat = 60f;
    [Tooltip("Real seconds after the last prompt closed before a new one can come")]
    public float cooldownSeconds = 45f;
    [Tooltip("Real seconds the screen must stay calm before the prompt opens")]
    public float calmSeconds = 0.5f;
    [Tooltip("A prompt still waiting for a calm screen after this many real seconds is dropped")]
    public float maxWaitSeconds = 10f;
    [Tooltip("\"Win it back\" flips in a row; after that many the offer doesn't come back after a loss")]
    public int maxFlipsInARow = 2;

    [Header("The flip")]
    [Tooltip("Chance of heads")]
    [Range(0f, 1f)] public float headsChance = 0.5f;
    [Tooltip("Heads pays the bet times this (2 = the bet back plus the loss: even)")]
    public double headsPayout = 2.0;
    [Tooltip("The bet never leaves the company with less than this")]
    public double reserve = 1000;
    [Tooltip("Losses smaller than this aren't worth an offer")]
    public double minimumBet = 1;
    [Tooltip("Heat change for walking away")]
    public float walkAwayHeat = -5f;

    [Header("Look")]
    [Tooltip("Font (empty = the Coming Soon font the HUD uses, else TextMesh Pro's default)")]
    public TMP_FontAsset font;
    [Tooltip("Sorting order of the runtime canvas: above the HUD and its pop-ups, below the toasts (900)")]
    public int sortingOrder = 130;
    [Tooltip("Length of the coin flip (real seconds)")]
    public float coinFlipSeconds = 1.3f;
    [Tooltip("Half-turns the coin makes while it flips")]
    public int coinHalfTurns = 9;
    [Tooltip("How high the coin jumps (reference pixels)")]
    public float coinJump = 120f;
    [Tooltip("Pause after the coin lands before the result shows (real seconds)")]
    public float resultDelay = 0.35f;

    [Header("Screen (leave Root empty to build it at runtime)")]
    [Tooltip("Everything of the prompt, scrim included; shown while it is open")]
    public RectTransform root;
    [Tooltip("Small caps line above the title: \"Heat: Hot 72\"")]
    public TMP_Text kickerText;
    [Tooltip("\"Win it back?\", \"HEADS!\", \"TAILS.\"")]
    public TMP_Text titleText;
    [Tooltip("What was lost and what the flip pays")]
    public TMP_Text bodyText;
    [Tooltip("\"Win or lose, the flip adds +8 heat.\"")]
    public TMP_Text noteText;
    [Tooltip("\"Win it back · $12,000\" (\"Back to work\" on a result without another offer)")]
    public Button winBackButton;
    public TMP_Text winBackLabel;
    [Tooltip("\"Walk away · heat −5\" (hidden on a result without another offer)")]
    public Button walkAwayButton;
    public TMP_Text walkAwayLabel;
    [Tooltip("The coin (squashed sideways and lifted while it flips)")]
    public RectTransform coin;
    [Tooltip("\"H\" / \"T\" on the coin")]
    public TMP_Text coinLetter;

    enum Stage { Hidden, Offer, Flipping, Result }

    // How the flip on the result screen ended (its texts are written again when the language changes)
    enum Ending { Heads, Tails, TailsOffer, TailsTableClosed, TailsFrozen }

    const string T = HeatMeter.Table;

    Stage stage = Stage.Hidden;
    Ending ending;
    Func<string> bodyWriter;   // writes the body on screen again after a language change
    bool coinShowsHeads = true;
    string headsLetter;
    string tailsLetter;
    bool hooked;
    Canvas runtimeCanvas;
    Coroutine flipRoutine;
    Vector2 coinHome;
    bool coinHomeKnown;

    // A loss waiting for a calm screen
    bool pending;
    double pendingLoss;
    string pendingSource;
    float pendingSince;
    float calmTimer;
    float lastClosedAt = float.NegativeInfinity;

    // The offer on screen
    double offerLoss;        // what was just lost
    double offerAmount;      // what the flip puts up: the loss, or less when the company can't cover it
    string offerSource;
    int flipsInARow;
    bool resultOffers;       // the result screen offers another flip
    double chainLost;        // lost in this chain so far: the loss that brought the offer plus every flip lost since
    string chainSource;      // where that first loss came from

    // The flip
    double flipStake;
    double flipValueBefore;
    bool flipHeads;
    bool flipSettled;

    /// <summary>The prompt is on screen.</summary>
    public bool IsOpen => stage != Stage.Hidden;
    /// <summary>The coin is in the air (its money is taken, the outcome not shown yet).</summary>
    public bool IsFlipping => stage == Stage.Flipping;
    /// <summary>A loss is waiting for a calm screen to bring the offer.</summary>
    public bool HasPendingPrompt => pending;

    /// <summary>The player turned an offer down ("Walk away" or Escape). Static, so listeners can outlive the scene (achievements).</summary>
    public static event Action WalkedAway;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        // Entering Play mode without a domain reload keeps statics
        WalkedAway = null;
    }

    void OnEnable()
    {
        GambleLedger.Gambled -= OnGambled;
        GambleLedger.Gambled += OnGambled;
        L10n.LanguageChanged += OnLanguageChanged;
    }

    void OnDisable()
    {
        GambleLedger.Gambled -= OnGambled;
        L10n.LanguageChanged -= OnLanguageChanged;
        // Close the way Close does: never leave the game paused, or a tossed coin unsettled (its outcome was decided when
        // it was tossed), or the scrim up (this component switched off on its own leaves its canvas on screen, where the
        // scrim would eat every click with the game running). A root this object sits in is being switched off already.
        if (stage == Stage.Flipping) SettleFlip();
        StopFlip();
        if (root != null && root.gameObject.activeSelf && !transform.IsChildOf(root)) root.gameObject.SetActive(false);
        if (stage != Stage.Hidden)
        {
            stage = Stage.Hidden;
            resultOffers = false;
            flipsInARow = 0;
            lastClosedAt = Time.realtimeSinceStartup;
            if (UIManager.Exists) UIManager.Instance.ClosePanel(this);
        }
    }

    void OnDestroy()
    {
        if (UIManager.Exists) UIManager.Instance.ClosePanel(this);
        if (runtimeCanvas != null) Destroy(runtimeCanvas.gameObject);
    }

    void Update()
    {
        if (stage != Stage.Hidden)
        {
            // UI state was reset (e.g. by a debug tool) while the prompt is up: keep the game paused
            if (UIManager.Exists && !UIManager.Instance.IsPanelOpen(this))
                UIManager.Instance.OpenPanel(this, true, OnEscape);
            return;
        }
        if (!pending) return;

        if (Time.realtimeSinceStartup - pendingSince > maxWaitSeconds)
        {
            pending = false;   // the moment has passed
            return;
        }
        if (!IsCalm())
        {
            calmTimer = 0f;
            return;
        }
        calmTimer += Time.unscaledDeltaTime;
        if (calmTimer < calmSeconds) return;

        pending = false;
        double amount = AffordableBet(pendingLoss);
        if (!OfferAllowed() || amount < minimumBet) return;
        flipsInARow = 0;
        Open(pendingLoss, amount, pendingSource);
    }

    // ------------------------------------------------------------------
    // When
    // ------------------------------------------------------------------

    void OnGambled(GambleRecord record)
    {
        // Its own flips never start a prompt: the result screen offers the next one (up to maxFlipsInARow)
        if (record.source == GambleLedger.SourceTilt) return;
        if (!record.IsLoss)
        {
            // A win or a push since the loss (e.g. Again and a winning flip inside the calm wait): nothing to win back now
            pending = false;
            return;
        }
        if (stage != Stage.Hidden) return;
        if (Time.realtimeSinceStartup - lastClosedAt < cooldownSeconds) return;

        // A newer loss replaces one that is still waiting
        pending = true;
        pendingLoss = record.AmountLost;
        pendingSource = record.source;
        pendingSince = Time.realtimeSinceStartup;
        calmTimer = 0f;
    }

    /// <summary>
    /// Nothing else is going on: RunEndDirector's calm check and no run-end screen. Build mode is a pausing panel, so it
    /// gets its own check: after a budget-gamble or furniture loss the prompt may show over the tower (its canvas sorts
    /// above the build screen) once nothing but build mode and an idle gamble pop-up is open. Either way no click may be
    /// under way (see ClickUnderWay).
    /// </summary>
    static bool IsCalm()
    {
        if (ClickUnderWay()) return false;
        RunEndDirector director = RunEndDirector.Instance;
        if (director != null && (director.IsRunOver || director.IsAnyScreenOpen)) return false;
        if (BuildModeCamera.Instance != null && BuildModeCamera.Instance.IsInBuildMode())
            return IsCalmInBuildMode();
        return RunEndDirector.IsScreenCalm();
    }

    static bool IsCalmInBuildMode()
    {
        if (UIManager.IsTypingInInputField()) return false;
        TowerBuilder builder = TowerBuilder.Instance;
        if (builder == null || !builder.IsOpen) return false;
        if (builder.PendingStake > 0.0) return false;   // a furniture gamble is still running
        BuildGambleView gamble = BuildGambleView.Instance;
        if (gamble != null && (gamble.IsBusy || gamble.PendingBet > 0.0)) return false;
        if (BlackjackGame.Instance != null && BlackjackGame.Instance.IsOpen) return false;
        if (UIManager.Exists && UIManager.Instance.IsAnyPanelOpen(builder, gamble)) return false;
        return true;
    }

    /// <summary>
    /// A mouse button is held or was let go this frame. The prompt opens from its own Update, and on that frame its scrim
    /// isn't raycast yet: a click let go then would land on what is under it (the gamble's Play / Again, the sheet's Deal /
    /// Roll).
    /// </summary>
    static bool ClickUnderWay()
    {
        return Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButtonUp(0) || Input.GetMouseButtonUp(1);
    }

    /// <summary>Hot enough, and the board hasn't frozen gambling.</summary>
    bool OfferAllowed()
    {
        HeatMeter meter = HeatMeter.Instance;
        return meter != null && meter.Heat >= minHeat && !meter.IsLockedOut && CompanyManager.Instance != null;
    }

    /// <summary>The flip for a loss: the loss, at most what the company can put up and still keep the reserve (whole dollars).</summary>
    double AffordableBet(double loss)
    {
        CompanyManager company = CompanyManager.Instance;
        if (company == null || double.IsNaN(loss) || loss <= 0.0) return 0.0;
        return Math.Floor(Math.Max(0.0, Math.Min(loss, company.companyValue - Math.Max(0.0, reserve))));
    }

    // ------------------------------------------------------------------
    // Open / close
    // ------------------------------------------------------------------

    void Open(double loss, double amount, string source)
    {
        EnsureBuilt();
        if (root == null) return;
        // Build mode's budget gamble may still be up, idle on its last result (IsCalmInBuildMode allows that): close it
        // (Close is safe on its result card; it only refuses while a flip or hand runs), or that stale card, whose company
        // value line the flips would outdate, sits under the prompt and is still there after it
        BuildGambleView budgetGamble = BuildGambleView.Instance;
        if (budgetGamble != null && budgetGamble.IsOpen) budgetGamble.Close();

        offerLoss = loss;
        offerAmount = amount;
        offerSource = source;
        chainLost = loss;
        chainSource = source;
        resultOffers = false;
        stage = Stage.Offer;
        FillOffer();

        root.gameObject.SetActive(true);
        UIManager.BringToFront(root.gameObject);
        HeatUI.ClearSelection();
        UIManager ui = UIManager.Instance;
        if (ui != null) ui.OpenPanel(this, true, OnEscape);
    }

    /// <summary>Close the prompt and let the game run again.</summary>
    public void Close()
    {
        if (stage == Stage.Flipping) SettleFlip();
        StopFlip();
        if (root != null) root.gameObject.SetActive(false);
        bool wasOpen = stage != Stage.Hidden;
        stage = Stage.Hidden;
        resultOffers = false;
        flipsInARow = 0;
        pending = false;
        if (wasOpen) lastClosedAt = Time.realtimeSinceStartup;
        if (UIManager.Exists) UIManager.Instance.ClosePanel(this);
    }

    /// <summary>Debug: open the prompt now for a made-up loss (5% of company value, at least $1,000), whatever the heat.</summary>
    public void DebugShow()
    {
        if (IsOpen) return;
        CompanyManager company = CompanyManager.Instance;
        if (company == null)
        {
            Debug.LogWarning("[TiltPrompt] No company in this scene.");
            return;
        }
        double loss = Math.Max(1000.0, Math.Round(company.companyValue * 0.05));
        double amount = AffordableBet(loss);
        if (amount < minimumBet)
        {
            Debug.LogWarning("[TiltPrompt] The company can't put up a flip (it always keeps " + UIFormat.Money(reserve) + ").");
            return;
        }
        pending = false;
        flipsInARow = 0;
        Open(loss, amount, "debug");
    }

    void EnsureBuilt()
    {
        if (root == null)
        {
            runtimeCanvas = HeatUI.CreateCanvas("TiltPromptCanvas (Runtime)", sortingOrder, transform, true);
            Build((RectTransform)runtimeCanvas.transform, HeatUI.ResolveFont(font));
        }
        if (coin != null && !coinHomeKnown)
        {
            coinHome = coin.anchoredPosition;
            coinHomeKnown = true;
        }
        if (hooked) return;
        hooked = true;
        if (winBackButton != null) winBackButton.onClick.AddListener(OnWinBack);
        if (walkAwayButton != null) walkAwayButton.onClick.AddListener(OnWalkAway);
        // Every text of the prompt (built here or designed) takes the language's font
        if (root != null) L10nFont.AttachAll(root);
    }

    // ------------------------------------------------------------------
    // Choices
    // ------------------------------------------------------------------

    bool OfferOnScreen => stage == Stage.Offer || (stage == Stage.Result && resultOffers);

    void OnEscape()
    {
        if (stage == Stage.Flipping) return;   // the coin is in the air
        OnWalkAway();                          // on a result without another offer this just closes it
    }

    void OnWinBack()
    {
        if (stage == Stage.Result && !resultOffers)
        {
            Close();   // "Back to work"
            return;
        }
        if (!OfferOnScreen) return;

        CompanyManager company = CompanyManager.Instance;
        if (company == null)
        {
            Close();
            return;
        }
        if (!HeatMeter.CanGamble(out string frozen))
        {
            GameNotifications.Show(frozen, NotificationKind.Bad);
            Close();
            return;
        }
        double amount = Math.Min(offerAmount, AffordableBet(offerAmount));
        if (amount < minimumBet)
        {
            Close();
            return;
        }

        // The money goes when the coin is tossed; the flip settles when its face shows (SettleFlip)
        flipStake = amount;
        flipValueBefore = Math.Max(0.0, company.companyValue);
        flipHeads = UnityEngine.Random.value < headsChance;
        flipSettled = false;
        company.AddMoney(-amount, true);
        flipsInARow++;
        GameSfx.Play(Sfx.CoinFlick);

        stage = Stage.Flipping;
        SetButtons(ButtonLayout.Both, false);
        RenderFlipping();
        flipRoutine = StartCoroutine(Flip());
    }

    void OnWalkAway()
    {
        if (!OfferOnScreen)
        {
            if (stage == Stage.Result) Close();
            return;
        }
        GameSfx.Play(Sfx.UiBack);   // walked away
        GameNotifications.Show(walkAwayHeat != 0f
                                   ? L10n.Get(T, "heat.tilt.walked_away", ("heat", HeatMeter.Signed(walkAwayHeat)))
                                   : L10n.Get(T, "heat.tilt.walked_away.plain"),
                               NotificationKind.Good);
        HeatMeter meter = HeatMeter.Instance;
        if (meter != null && walkAwayHeat != 0f) meter.AddHeat(walkAwayHeat, "walked away from Win it back");
        Close();
        try { WalkedAway?.Invoke(); }
        catch (Exception e) { Debug.LogException(e); }
    }

    // ------------------------------------------------------------------
    // The flip
    // ------------------------------------------------------------------

    IEnumerator Flip()
    {
        // Squash the coin sideways, swapping faces at the thin moments (real time: the game is paused)
        float duration = Mathf.Max(0.3f, coinFlipSeconds);
        int halfTurns = Mathf.Max(1, coinHalfTurns);
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(t / duration);
            float eased = 1f - Mathf.Pow(1f - progress, 3f);
            float turns = eased * halfTurns;
            if (coin != null)
            {
                coin.localScale = new Vector3(Mathf.Max(0.04f, Mathf.Abs(Mathf.Cos(turns * Mathf.PI))), 1f, 1f);
                coin.anchoredPosition = coinHome + new Vector2(0f, Mathf.Sin(progress * Mathf.PI) * coinJump);
            }
            // The face that lands is the rolled one: count faces back from the last half-turn
            int face = Mathf.FloorToInt(turns + 0.5f);
            SetCoinFace(((halfTurns - face) % 2 == 0) == flipHeads);
            yield return null;
        }
        ResetCoin();
        SetCoinFace(flipHeads);
        GameSfx.Play(Sfx.CoinLand);

        SettleFlip();   // the moment its face shows: a save or a quit from here on keeps the outcome

        yield return new WaitForSecondsRealtime(Mathf.Max(0f, resultDelay));
        flipRoutine = null;
        ShowResult();
    }

    /// <summary>Pay a heads (tails was paid when the coin was tossed) and report the flip to the ledger, once.</summary>
    void SettleFlip()
    {
        if (flipSettled || flipStake <= 0.0) return;
        flipSettled = true;
        double payout = flipHeads ? flipStake * Math.Max(0.0, headsPayout) : 0.0;
        CompanyManager company = CompanyManager.Instance;
        if (payout > 0.0 && company != null) company.AddMoney(payout, true);
        GambleLedger.Report(GambleLedger.SourceTilt, flipStake, payout, flipValueBefore);
    }

    void ShowResult()
    {
        stage = Stage.Result;
        HeatMeter meter = HeatMeter.Instance;

        if (flipHeads)
        {
            ending = Ending.Heads;
            resultOffers = false;
            GameSfx.Play(Sfx.ChipsWin);
        }
        else
        {
            chainLost += flipStake;   // the next offer counts from here
            double next = AffordableBet(flipStake);
            int max = Mathf.Max(1, maxFlipsInARow);
            resultOffers = flipsInARow < max && OfferAllowed() && next >= minimumBet;
            if (resultOffers)
            {
                offerLoss = flipStake;
                offerAmount = next;
                offerSource = GambleLedger.SourceTilt;
                ending = Ending.TailsOffer;
            }
            else if (flipsInARow >= max) ending = Ending.TailsTableClosed;
            else if (meter != null && meter.IsLockedOut) ending = Ending.TailsFrozen;
            else ending = Ending.Tails;
        }

        RenderResult(meter);
        SetButtons(resultOffers ? ButtonLayout.Both : ButtonLayout.PrimaryOnly, true);
        HeatUI.ClearSelection();
    }

    /// <summary>The result screen's texts, from the flip's outcome (written again when the language changes).</summary>
    void RenderResult(HeatMeter meter)
    {
        SetKicker(meter != null ? L10n.Get(T, "heat.tilt.kicker.result", HeatLineArgs(meter)) : L10n.Get(T, "heat.tilt.kicker.result.nometer"));

        string stake = UIFormat.Money(flipStake);
        if (ending == Ending.Heads)
        {
            // "Back to even" only when this heads wins back the whole chain (the loss and every flip lost since)
            string payout = UIFormat.Money(flipStake * Math.Max(0.0, headsPayout));
            double down = DownAfterHeads(flipStake);
            string lost = UIFormat.Money(chainLost);
            string source = chainSource;
            if (down > 0.5)
                WriteBody(() => L10n.Get(T, "heat.tilt.result.heads.down", ("payout", payout), ("down", UIFormat.Money(down))));
            else if (FromFurniture)
                WriteBody(() => L10n.Get(T, "heat.tilt.result.heads.furniture", ("payout", payout), ("lost", lost), ("source", GambleLedger.DescribeLocalized(source))));
            else
                WriteBody(() => L10n.Get(T, "heat.tilt.result.heads.even", ("payout", payout)));
            SetText(titleText, L10n.Get(T, "heat.tilt.title.heads"), UIColors.Money);
        }
        else
        {
            int flips = flipsInARow;
            switch (ending)
            {
                case Ending.TailsOffer:
                    WriteBody(() => L10n.Get(T, "heat.tilt.result.tails.offer", ("stake", stake), ("terms", FlipTerms())));
                    break;
                case Ending.TailsTableClosed:
                    WriteBody(() => L10n.Get(T, "heat.tilt.result.tails.closed", ("stake", stake), ("count", flips)));
                    break;
                case Ending.TailsFrozen:
                    WriteBody(() => L10n.Get(T, "heat.tilt.result.tails.frozen", ("stake", stake)));
                    break;
                default:
                    WriteBody(() => L10n.Get(T, "heat.tilt.result.tails", ("stake", stake)));
                    break;
            }
            SetText(titleText, L10n.Get(T, "heat.tilt.title.tails"), UIColors.Loss);
        }

        if (resultOffers)
        {
            SetText(noteText, HeatNote(meter));
            SetOfferLabels();
        }
        else
        {
            SetText(noteText, "");
            SetText(winBackLabel, L10n.Get(T, "heat.tilt.button.back_to_work"));
        }
    }

    /// <summary>The coin in the air: kicker, title and note (the body and the buttons keep what they showed).</summary>
    void RenderFlipping()
    {
        SetKicker(L10n.Get(T, "heat.tilt.kicker.flip"));
        SetText(titleText, L10n.Get(T, "heat.tilt.title.flipping"), UIColors.Ink);
        SetText(noteText, "");
    }

    // The fonts switch by themselves (L10nFont); the prompt's texts are written again in the new language
    void OnLanguageChanged()
    {
        headsLetter = tailsLetter = null;
        if (stage == Stage.Hidden) return;
        HeatMeter meter = HeatMeter.Instance;
        switch (stage)
        {
            case Stage.Offer:
                RenderOffer(meter);
                break;
            case Stage.Flipping:
                RenderFlipping();
                if (bodyWriter != null) SetText(bodyText, bodyWriter());
                SetOfferLabels();
                break;
            case Stage.Result:
                RenderResult(meter);
                break;
        }
        SetCoinFace(coinShowsHeads);
    }

    void StopFlip()
    {
        if (flipRoutine != null) StopCoroutine(flipRoutine);
        flipRoutine = null;
        ResetCoin();
    }

    void ResetCoin()
    {
        if (coin == null || !coinHomeKnown) return;
        coin.localScale = Vector3.one;
        coin.anchoredPosition = coinHome;
    }

    /// <summary>"H" / "T" on the coin (Chinese 正 / 反, Japanese 表 / 裏, ...), looked up once per language.</summary>
    void SetCoinFace(bool heads)
    {
        coinShowsHeads = heads;
        if (headsLetter == null) headsLetter = L10n.Get(T, "heat.tilt.coin.heads");
        if (tailsLetter == null) tailsLetter = L10n.Get(T, "heat.tilt.coin.tails");
        SetText(coinLetter, heads ? headsLetter : tailsLetter);
    }

    // ------------------------------------------------------------------
    // Texts
    // ------------------------------------------------------------------

    void FillOffer()
    {
        RenderOffer(HeatMeter.Instance);
        SetButtons(ButtonLayout.Both, true);
        SetCoinFace(true);
    }

    /// <summary>The offer's texts (written again when the language changes).</summary>
    void RenderOffer(HeatMeter meter)
    {
        SetKicker(meter != null ? L10n.Get(T, "heat.tilt.kicker", HeatLineArgs(meter)) : L10n.Get(T, "heat.tilt.kicker.nometer"));
        SetText(titleText, L10n.Get(T, "heat.tilt.title.offer"), UIColors.Ink);
        WriteBody(OfferBody);
        SetText(noteText, HeatNote(meter));
        SetOfferLabels();
    }

    /// <summary>"You just lost $12,000 on the casino. One coin flip: ..." ("of build budget" after a furniture gamble).</summary>
    string OfferBody()
    {
        return L10n.Get(T, FromFurniture ? "heat.tilt.body.offer.furniture" : "heat.tilt.body.offer",
                        ("amount", UIFormat.Money(offerLoss)), ("source", GambleLedger.DescribeLocalized(offerSource)), ("terms", FlipTerms()));
    }

    /// <summary>Write the body, remembering how, so a language change can write it again (even while the coin is in the air).</summary>
    void WriteBody(Func<string> writer)
    {
        bodyWriter = writer;
        SetText(bodyText, writer());
    }

    /// <summary>The kicker in capitals by the language's rules (TMP's UpperCase style would turn a Turkish "i" into "I").</summary>
    void SetKicker(string value)
    {
        SetText(kickerText, L10n.ToUpper(value));
    }

    void SetOfferLabels()
    {
        SetText(winBackLabel, L10n.Get(T, "heat.tilt.button.win_back", ("amount", UIFormat.Money(offerAmount))));
        SetText(walkAwayLabel, walkAwayHeat != 0f
                                   ? L10n.Get(T, "heat.tilt.button.walk_away", ("heat", HeatMeter.Signed(walkAwayHeat)))
                                   : L10n.Get(T, "heat.tilt.button.walk_away.plain"));
    }

    /// <summary>"One coin flip: heads pays $24,000 (back to even), tails loses $12,000 more."</summary>
    string FlipTerms()
    {
        string key = offerAmount < offerLoss - 0.5 ? "heat.tilt.terms.capped" : "heat.tilt.terms";   // capped: "The company can put up $X. ..."
        return L10n.Get(T, key, ("pays", UIFormat.Money(offerAmount * Math.Max(0.0, headsPayout))), ("note", HeadsNote(offerAmount)),
                        ("stake", UIFormat.Money(offerAmount)));
    }

    /// <summary>
    /// What a heads on <paramref name="stake"/> leaves of the chain: "back to even", "you'd still be down $12,000", or for a
    /// furniture loss (build budget, won back as company value) "the $12,000 back, as company value".
    /// </summary>
    string HeadsNote(double stake)
    {
        double down = DownAfterHeads(stake);
        if (down > 0.5) return L10n.Get(T, "heat.tilt.note.down", ("amount", UIFormat.Money(down)));
        return FromFurniture ? L10n.Get(T, "heat.tilt.note.furniture", ("amount", UIFormat.Money(chainLost))) : L10n.Get(T, "heat.tilt.note.even");
    }

    /// <summary>What the chain (the loss plus the flips lost since) is still down after a heads on <paramref name="stake"/>; 0 = all of it back.</summary>
    double DownAfterHeads(double stake)
    {
        return Math.Max(0.0, chainLost - stake * (Math.Max(0.0, headsPayout) - 1.0));
    }

    /// <summary>The chain started with a furniture gamble: build budget was lost, and the flips pay company value.</summary>
    bool FromFurniture => chainSource == GambleLedger.SourceFurniture;

    /// <summary>{level} (in the level's ink) and {heat} for "Heat: Hot 72".</summary>
    static (string name, object value)[] HeatLineArgs(HeatMeter meter)
    {
        string level = "<color=" + HeatUI.Hex(HeatMeter.LevelInkColor(meter.Level)) + ">" + meter.LevelName + "</color>";
        return new (string name, object value)[] { ("level", level), ("heat", meter.DisplayHeat.ToString(CultureInfo.InvariantCulture)) };
    }

    string HeatNote(HeatMeter meter)
    {
        CompanyManager company = CompanyManager.Instance;
        float add = meter != null && company != null ? Mathf.Round(meter.BetHeat(offerAmount, company.companyValue)) : 0f;
        return add > 0f ? L10n.Get(T, "heat.tilt.heat_note", ("heat", HeatMeter.Signed(add))) : L10n.Get(T, "heat.tilt.heat_note.plain");
    }

    enum ButtonLayout { Both, PrimaryOnly }

    void SetButtons(ButtonLayout layout, bool enabled)
    {
        if (winBackButton != null)
        {
            if (!winBackButton.gameObject.activeSelf) winBackButton.gameObject.SetActive(true);
            HeatUI.SetButtonEnabled(winBackButton, enabled);
        }
        if (walkAwayButton != null)
        {
            bool show = layout == ButtonLayout.Both;
            if (walkAwayButton.gameObject.activeSelf != show) walkAwayButton.gameObject.SetActive(show);
            HeatUI.SetButtonEnabled(walkAwayButton, enabled);
        }
    }

    static void SetText(TMP_Text text, string value)
    {
        if (text != null) text.text = value;
    }

    static void SetText(TMP_Text text, string value, Color color)
    {
        if (text == null) return;
        text.text = value;
        text.color = color;
    }

    // ------------------------------------------------------------------
    // Runtime layout (1920 x 1080: a 1000 x 600 felt table in the middle, a paper note on it, the coin to its right)
    // ------------------------------------------------------------------

    const float FeltWidth = 1000f, FeltHeight = 600f;
    const float NoteX = 56f, NoteY = 52f, NoteWidth = 560f, NoteHeight = 390f, NotePad = 34f;
    const float CoinX = 812f, CoinY = 246f, CoinSize = 220f;
    const float ButtonY = 474f, ButtonHeight = 68f, WinBackWidth = 440f, WalkAwayWidth = 340f, ButtonGap = 28f;

    void Build(RectTransform parent, TMP_FontAsset useFont)
    {
        // Built inactive, so MarkerButton starts with its face in place
        RectTransform screen = StatusUI.NewRect("TiltPrompt", parent);
        StatusUI.Stretch(screen);
        screen.gameObject.SetActive(false);
        root = screen;

        StatusUI.Stretch(HeatUI.Block(screen, "Scrim", UIColors.Scrim, true).rectTransform);
        HeatUI.PausedChip(screen, useFont);

        // The felt table
        RectTransform felt = StatusUI.NewRect("Felt", screen);
        felt.anchorMin = felt.anchorMax = felt.pivot = new Vector2(0.5f, 0.5f);
        felt.anchoredPosition = new Vector2(0f, -10f);
        felt.sizeDelta = new Vector2(FeltWidth, FeltHeight);
        StatusBox table = felt.gameObject.AddComponent<StatusBox>();
        table.SetStyle(UIColors.Felt, UIColors.Ink, 3f);
        table.SetGradient(UIColors.FeltHi, UIColors.Felt);
        table.Radii = HeatUI.CardRadii;
        table.SetShadow(HeatUI.SheetShadow, new Vector2(0f, 22f), 40f);
        table.raycastTarget = false;

        StatusBox rail = StatusUI.Box(felt, "Rail", Color.clear, UIColors.WithAlpha(UIColors.Gold, 0.7f), 2f, HeatUI.CardRadii - Vector4.one * 12f);
        rail.SetDash(true, 10f, 7f);
        StatusUI.Stretch(rail.rectTransform, 16f, 16f, 16f, 16f);

        // The paper note with the offer, taped to the felt
        RectTransform note = StatusUI.PlaceRotated(StatusUI.NewRect("Note", felt), NoteX, NoteY, NoteWidth, NoteHeight, 0.8f);
        StatusBox paper = note.gameObject.AddComponent<StatusBox>();
        paper.SetStyle(UIColors.PaperHi, UIColors.Ink, 3f);
        paper.Radii = new Vector4(14f, 18f, 13f, 19f);
        paper.SetShadow(new Color(0f, 0f, 0f, 0.35f), new Vector2(0f, 10f), 18f);
        paper.raycastTarget = false;
        HeatUI.Tape(note, 150f, -2f);

        float textWidth = NoteWidth - NotePad * 2f;
        // The kicker is written in capitals by SetKicker (no UpperCase style: TMP's ignores Turkish i / İ)
        TextMeshProUGUI kicker = HeatUI.Text(note, "Kicker", L10n.ToUpper(L10n.Get(T, "heat.tilt.kicker.nometer")), 19f, UIColors.Pencil, useFont,
                                             NotePad, 28f, textWidth, 32f);
        kicker.characterSpacing = 8f;
        HeatUI.Shrink(kicker, 14f);
        kickerText = kicker;
        titleText = HeatUI.Shrink(HeatUI.Text(note, "Title", L10n.Get(T, "heat.tilt.title.offer"), 50f, UIColors.Ink, useFont, NotePad, 60f, textWidth, 80f,
                                              TextAlignmentOptions.MidlineLeft, true), 32f);
        TextMeshProUGUI body = HeatUI.Text(note, "Body", "", 24f, UIColors.InkSoft, useFont, NotePad, 148f, textWidth, 172f, TextAlignmentOptions.TopLeft);
        bodyText = HeatUI.Shrink(HeatUI.Wrapped(body, 1.3f), 16f);
        noteText = HeatUI.Shrink(HeatUI.Text(note, "Note", "", 20f, UIColors.Loss, useFont, NotePad, 326f, textWidth, 36f), 15f);

        // The coin on the felt, over its shadow
        StatusBox coinShadow = StatusUI.Box(felt, "CoinShadow", UIColors.Dark(0.35f), Color.clear, 0f, new Vector4(13f, 13f, 13f, 13f));
        HeatUI.PlaceCenter(coinShadow.rectTransform, CoinX, CoinY + CoinSize * 0.5f + 34f);
        coinShadow.rectTransform.sizeDelta = new Vector2(CoinSize * 0.8f, 26f);
        coin = HeatUI.Coin(felt, "Coin", CoinSize, useFont, out TextMeshProUGUI letter);
        HeatUI.PlaceCenter(coin, CoinX, CoinY);
        coinLetter = letter;
        SetCoinFace(true);

        // Win it back | Walk away
        winBackButton = HeatUI.PaperButton(felt, "WinBack", L10n.Get(T, "heat.tilt.button.win_back.plain"), UIColors.Highlight, useFont, 26f,
                                           NoteX, ButtonY, WinBackWidth, ButtonHeight, new Vector2(5f, 6f), out TextMeshProUGUI winLabel);
        winBackLabel = winLabel;
        walkAwayButton = HeatUI.PaperButton(felt, "WalkAway", L10n.Get(T, "heat.tilt.button.walk_away.plain"), UIColors.PaperHi, useFont, 26f,
                                            NoteX + WinBackWidth + ButtonGap, ButtonY, WalkAwayWidth, ButtonHeight, new Vector2(4f, 5f),
                                            out TextMeshProUGUI walkLabel);
        walkAwayLabel = walkLabel;

        TextMeshProUGUI hint = HeatUI.Text(felt, "Hint", "", 18f, UIColors.WithAlpha(UIColors.PaperHi, 0.75f), useFont,
                                           40f, FeltHeight - 44f, FeltWidth - 80f, 28f, TextAlignmentOptions.MidlineRight);
        L10nText.Attach(hint, T, "heat.tilt.hint");   // "Esc walks away"
    }
}
