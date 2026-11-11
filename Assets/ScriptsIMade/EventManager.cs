using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// Risk-card events. After a random amount of game time the player picks a High / Balanced / Low
/// risk card; a good or bad event is rolled for that tier, modified by skills and applied to company value.
/// - A due event waits while any other panel, a mini-game or a text field is active, then shows.
/// - The event is a pausing modal registered with UIManager (this script never writes Time.timeScale).
/// - If the panel isn't wired up, the event is skipped with an error instead of freezing the game.
/// - Texts: the card's title and story in the current language (RandomEvent.DisplayName / DisplayDescription), the skill
///   notes, the JACKPOT toast and the old card faces from the Events String Table ("risk." keys, Events.csv).
/// </summary>
public class EventManager : MonoBehaviour
{
    public static EventManager Instance;

    [Header("References")]
    public EventDatabase eventDB;
    public CompanyManager companyManager;

    [Header("Event UI Panel")]
    public GameObject eventPanel;
    public GameObject cardSelectionPanel;
    public GameObject resultPanel;

    [Header("Card Buttons")]
    public Button highRiskCard;
    public Button balancedCard;
    public Button lowRiskCard;

    [Header("Card Texts")]
    public TextMeshProUGUI highRiskText;
    public TextMeshProUGUI balancedText;
    public TextMeshProUGUI lowRiskText;

    [Header("Result Display")]
    public TextMeshProUGUI resultTitleText;
    public TextMeshProUGUI resultDescriptionText;
    public TextMeshProUGUI resultValueText;
    public Button continueButton;

    [Header("Event Timing")]
    public float minTimeBetweenEvents = 60f;  // Minimum seconds between events
    public float maxTimeBetweenEvents = 180f; // Maximum seconds between events
    [Tooltip("When an event comes due while another panel or a mini-game is open, it appears this many seconds after that closes")]
    public float delayAfterBlocked = 1f;

    [Header("Result Colors")]
    public Color goodResultColor = new Color(0.1f, 0.55f, 0.2f);
    public Color badResultColor = new Color(0.8f, 0.15f, 0.15f);

    [Header("Runtime Text Fixes")]
    [Tooltip("Wrap and auto-size the card and result texts and make the result description dark. Turn off once the panel is redesigned.")]
    public bool applyTextFixes = true;
    public Color resultDescriptionColor = new Color(0.15f, 0.15f, 0.15f);

    // Chance that each card gives a good event, before skills
    const float HighRiskGoodChance = 0.25f;
    const float BalancedGoodChance = 0.50f;
    const float LowRiskGoodChance = 0.75f;

    // Luck skills (chaos branch) and investor trust
    const float MaxGoodChance = 0.95f;          // all good-chance bonuses together never go above 95%
    const int MaxLuckyStreakSteps = 3;          // Lucky Streak: +15% per consecutive good result, up to +45%
    const float RealityWarpChance = 0.10f;      // Reality Warp: a bad card counts as good
    const float MaxReverseKarmaChance = 0.75f;  // Reverse Karma: bad turns good (25%)
    const float MaxJinxRemovalChance = 0.9f;    // Jinx Removal: a bad event is cancelled (30%)
    const float MaxUnluckyProtection = 0.9f;    // Unlucky Protection: bad events hurt 25% less
    const float DoubleOrNothingChance = 0.5f;   // Double or Nothing: a good outcome pays x2 or x0
    const float JackpotMultiplier = 10f;        // Jackpot Chance: 2% of good outcomes pay x10
    const float MaxJackpotChance = 0.25f;
    const float MaxEventPercentGain = 1f;       // one card adds at most +100% of company value through its percentage

    const string T = RandomEvent.Table;         // Events String Table: the cards and this script's own texts ("risk.")

    enum EventState { Idle, ChoosingCard, ShowingResult }

    struct EventOutcome
    {
        public double valueChange;    // Change actually applied to company value
        public bool cancelled;        // Jinx Removal cancelled a bad event: nothing happened
    }

    private EventState state = EventState.Idle;
    private float eventTimer;
    private float nextEventTime;
    private float blockedDelayTimer;
    private bool lastEventWasBad = false; // For Gambler's Fallacy skill
    private int goodStreak;               // consecutive good results (Lucky Streak)
    private int wildCardUsedDay = -1;     // Wild Card: the in-game day it was last used

    /// <summary>
    /// Check if a card event is currently in progress
    /// </summary>
    public bool IsEventInProgress() => state != EventState.Idle;

    /// <summary>Fires when a risk card resolves: (event title, change actually applied to company value; 0 when cancelled).</summary>
    public event System.Action<string, double> CardResolved;

    /// <summary>
    /// The same moment with the card's translation key: (English title, key such as "event.small_good.tip_jar" or null,
    /// change applied to company value). RunStats saves both, so the end screens name the card in the current language.
    /// </summary>
    public event System.Action<string, string, double> CardResolvedWithKey;

    /// <summary>
    /// Fires when a risk card settles, for the cloud save's ledger (CloudLedger): (the card picked, whether the event was
    /// good, company value just before it, change actually applied to company value; 0 when Jinx Removal cancelled a bad
    /// event). Static, so listeners outlive the scene.
    /// </summary>
    public static event System.Action<CardRisk, bool, double, double> RiskCardSettled;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        // Entering Play mode without a domain reload keeps statics
        RiskCardSettled = null;
    }

    void Awake()
    {
        Instance = this;

        if (eventDB == null) eventDB = GetComponent<EventDatabase>();
        if (eventDB == null) eventDB = FindAnyObjectByType<EventDatabase>();

        // Setup button listeners
        if (highRiskCard != null)
            highRiskCard.onClick.AddListener(ChooseHighRisk);
        if (balancedCard != null)
            balancedCard.onClick.AddListener(ChooseBalanced);
        if (lowRiskCard != null)
            lowRiskCard.onClick.AddListener(ChooseLowRisk);
        if (continueButton != null)
            continueButton.onClick.AddListener(CloseEventPanel);

        if (applyTextFixes)
            ApplyTextFixes();

        // Hide panels at start
        if (eventPanel != null)
        {
            if (eventPanel == gameObject || transform.IsChildOf(eventPanel.transform))
                Debug.LogWarning("[EventManager] EventManager sits on its own event panel, so hiding the panel stops the event timer. Move it to an object outside the panel.");
            eventPanel.SetActive(false);
        }
    }

    void Start()
    {
        // Setup card description texts
        UpdateCardTexts();

        // Demo: the demo's card timers win over the scene's
        if (DemoMode.Active)
        {
            minTimeBetweenEvents = DemoMode.Config.riskCardMin;
            maxTimeBetweenEvents = DemoMode.Config.riskCardMax;
        }

        // Set first event time
        SetNextEventTime();

        // A save is applied a few frames after Start: re-roll the timer so loaded skills (Chaotic Energy) count
        subscribedCompany = CompanyManager.Instance;
        if (subscribedCompany != null)
            subscribedCompany.StateLoaded += OnCompanyStateLoaded;
    }

    CompanyManager subscribedCompany;

    void OnCompanyStateLoaded()
    {
        if (state == EventState.Idle)
            SetNextEventTime();
    }

    void OnDisable()
    {
        // Never leave the game paused behind an event that can't be finished
        if (state != EventState.Idle)
            EndEvent();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (UIManager.Exists) UIManager.Instance.ClosePanel(this);
        if (subscribedCompany != null)
            subscribedCompany.StateLoaded -= OnCompanyStateLoaded;
    }

    void Update()
    {
        if (state != EventState.Idle)
        {
            // Safety net: if the panel was hidden or destroyed from outside, end the event so the game can't stay paused
            if (eventPanel == null || !eventPanel.activeInHierarchy)
            {
                Debug.LogWarning("[EventManager] The event panel was hidden while an event was open; ending the event.");
                CloseEventPanel();
            }
            else if (UIManager.Exists && !UIManager.Instance.IsPanelOpen(this))
            {
                // UI state was reset (e.g. by a debug tool) while the event is still on screen
                UIManager.Instance.OpenPanel(this, pauseGame: true, onEscape: SwallowEscape);
            }
            return;
        }

        // Count down in game time (stops while the game is paused, and while any panel is open, like the day timer,
        // so closing the skill tree or stock market doesn't fire a card that came due while browsing)
        if (eventTimer < nextEventTime)
        {
            // Tutorial: no risk cards until the company's first few hires (FeatureGate.RiskCardsAfterHires)
            if (!FeatureGate.IsUnlocked(Feature.RiskCards)) return;
            if (!UIManager.Exists || !UIManager.Instance.IsAnyPanelOpen(this))
                eventTimer += Time.deltaTime;
            return;
        }

        // The event is due: hold it until nothing else is in the way
        if (IsBlocked())
        {
            blockedDelayTimer = delayAfterBlocked;
            return;
        }
        if (blockedDelayTimer > 0f)
        {
            blockedDelayTimer -= Time.unscaledDeltaTime;
            return;
        }

        // Tutorial: a player's first risk card comes after the Board's one-time memo about them (it pauses; the card follows)
        if (FeatureGate.HoldForMemo(Feature.RiskCards))
        {
            blockedDelayTimer = delayAfterBlocked;
            return;
        }

        ShowEvent();
    }

    void SetNextEventTime()
    {
        eventTimer = 0f;
        blockedDelayTimer = 0f;

        float min = Mathf.Max(1f, Mathf.Min(minTimeBetweenEvents, maxTimeBetweenEvents));
        float max = Mathf.Max(min, Mathf.Max(minTimeBetweenEvents, maxTimeBetweenEvents));
        nextEventTime = Random.Range(min, max);

        // Demo: the run's first card comes sooner (the timer only runs once risk cards are open)
        if (DemoMode.Active && (RunStats.Instance == null || RunStats.Instance.RiskCardsPlayed == 0))
            nextEventTime = Random.Range(DemoMode.Config.firstRiskCardMin, DemoMode.Config.firstRiskCardMax);

        // Chaotic Energy skill: events more frequent
        if (SkillTreeManager.Instance != null && SkillTreeManager.Instance.HasEffect(SkillEffect.ChaoticEnergy))
        {
            nextEventTime *= 0.7f; // 30% faster events
        }
    }

    /// <summary>Escape does nothing while a card is showing: the pause menu must not open on top of it
    /// (quitting from there would skip the pending card).</summary>
    void SwallowEscape() { }

    /// <summary>True while something else should keep a due event waiting.</summary>
    bool IsBlocked()
    {
        // Any other panel, including browsing panels (skill tree, stock market, build mode) and the company-name prompt
        if (UIManager.Instance != null && UIManager.Instance.IsAnyPanelOpen(this))
            return true;

        if (MiniGameManager.Instance != null && MiniGameManager.Instance.IsMiniGameActive())
            return true;

        if (UIManager.IsTypingInInputField())
            return true;

        // Paused by something that doesn't go through UIManager
        if (Time.timeScale <= 0f)
            return true;

        return false;
    }

    /// <summary>
    /// Ask for an event now (debug / scripted). It shows immediately if nothing is in the way,
    /// otherwise as soon as the other panel or mini-game closes.
    /// </summary>
    public void TriggerRandomEvent()
    {
        if (state != EventState.Idle) return;

        eventTimer = nextEventTime; // Mark the event as due
        blockedDelayTimer = 0f;
        if (!IsBlocked())
            ShowEvent();
    }

    void ShowEvent()
    {
        if (!HasRequiredUI())
        {
            SetNextEventTime(); // Skip this event instead of pausing with nothing to click
            return;
        }

        eventPanel.SetActive(true);
        if (!eventPanel.activeInHierarchy)
        {
            // A parent (e.g. the canvas) is hidden right now; try again shortly
            eventPanel.SetActive(false);
            blockedDelayTimer = Mathf.Max(1f, delayAfterBlocked);
            return;
        }

        state = EventState.ChoosingCard;

        // Show event panel with card selection
        if (cardSelectionPanel != null) cardSelectionPanel.SetActive(true);
        if (resultPanel != null) resultPanel.SetActive(false);
        SetCardsInteractable(true);
        UpdateCardTexts();

        // Don't let Enter/Space click a HUD button hidden behind the event
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);

        // Draw above every other panel and pause the game
        UIManager.BringToFront(eventPanel);
        UIManager ui = UIManager.Instance;
        if (ui != null)
            ui.OpenPanel(this, pauseGame: true, onEscape: SwallowEscape);
        GameSfx.Play(Sfx.CardFan);   // the risk cards are dealt out
    }

    bool HasRequiredUI()
    {
        string missing = "";
        if (eventPanel == null) missing += " eventPanel";
        if (highRiskCard == null && balancedCard == null && lowRiskCard == null) missing += " card buttons";
        if (continueButton == null) missing += " continueButton";
        if (eventDB == null) missing += " eventDB";
        if (GetCompany() == null) missing += " CompanyManager";

        if (missing.Length == 0) return true;

        Debug.LogError("[EventManager] Can't show a risk-card event, missing:" + missing + ". The event was skipped.");
        return false;
    }

    CompanyManager GetCompany()
    {
        return CompanyManager.Instance != null ? CompanyManager.Instance : companyManager;
    }

    /// <summary>
    /// Current chance (0-0.95) that a card gives a good event, including skills (Beginner's Luck, Lucky Charm,
    /// Probability Manipulation, Gambler's Fallacy after a bad result), investor trust (up to +-5 points) and heat
    /// (HeatMeter: -5 points when Hot, -10 On fire).
    /// </summary>
    public float GetGoodChance(CardRisk risk)
    {
        float goodChance;
        switch (risk)
        {
            case CardRisk.HighRisk:
                goodChance = HighRiskGoodChance;
                break;
            case CardRisk.LowRisk:
                goodChance = LowRiskGoodChance;
                break;
            default:
                goodChance = BalancedGoodChance;
                break;
        }

        // Skill tree modifiers
        SkillTreeManager skills = SkillTreeManager.Instance;
        if (skills != null)
        {
            // Beginner's Luck, Lucky Charm, Probability Manipulation: +X points of good event chance
            goodChance += skills.GetGoodEventChance();
            goodChance += Mathf.Max(0f, skills.GetEffect(SkillEffect.LuckyCharm));
            goodChance += Mathf.Max(0f, skills.GetEffect(SkillEffect.ProbabilityManipulation));

            // Gambler's Fallacy: after bad event, better odds
            if (lastEventWasBad && skills.HasEffect(SkillEffect.GamblersFallacy))
                goodChance += skills.GetEffect(SkillEffect.GamblersFallacy);
        }

        // Investor trust: -5 points at 0 trust, +5 at 100
        CompanyManager cm = GetCompany();
        if (cm != null)
            goodChance += cm.TrustGoodChanceBonus;

        // Heat: regulators watching a gambling company (-5 points when Hot, -10 On fire)
        goodChance += HeatMeter.RiskCardChanceModifier;

        return Mathf.Clamp(goodChance, 0f, MaxGoodChance);
    }

    void UpdateCardTexts()
    {
        SetCardText(highRiskText, "risk.card.high", CardRisk.HighRisk);
        SetCardText(balancedText, "risk.card.balanced", CardRisk.Balanced);
        SetCardText(lowRiskText, "risk.card.low", CardRisk.LowRisk);
    }

    void SetCardText(TextMeshProUGUI text, string key, CardRisk risk)
    {
        if (text == null) return;
        int good = Mathf.RoundToInt(GetGoodChance(risk) * 100f);
        text.text = L10n.Get(T, key, ("good", good), ("bad", 100 - good));
    }

    void SetCardsInteractable(bool interactable)
    {
        if (highRiskCard != null) highRiskCard.interactable = interactable;
        if (balancedCard != null) balancedCard.interactable = interactable;
        if (lowRiskCard != null) lowRiskCard.interactable = interactable;
    }

    // No-argument versions so card buttons can also be wired in the Inspector
    public void ChooseHighRisk() => ChooseCard(CardRisk.HighRisk);
    public void ChooseBalanced() => ChooseCard(CardRisk.Balanced);
    public void ChooseLowRisk() => ChooseCard(CardRisk.LowRisk);

    /// <summary>Pick a card while the card selection is showing. Ignored at any other time.</summary>
    public void ChooseCard(CardRisk risk)
    {
        if (state != EventState.ChoosingCard) return; // Double clicks, or no event open
        SetCardsInteractable(false);
        GameSfx.Play(Sfx.CardPlace);

        SkillTreeManager skills = SkillTreeManager.Instance;
        System.Text.StringBuilder notes = new System.Text.StringBuilder(); // what the luck skills did

        // Determine outcome based on card risk (and skills)
        float goodChance = GetGoodChance(risk);
        bool isGood = RollOutcome(goodChance, skills, notes);

        // The heat meter counts the risk taken (High +10, Balanced +4, Low +0), after the roll so it can't change its own odds
        HeatMeter.OnRiskCardChosen(risk);

        RandomEvent chosenEvent = eventDB != null ? eventDB.GetRandomEvent(risk, isGood) : null;
        if (chosenEvent == null)
        {
            Debug.LogError("[EventManager] The event database has no events; closing the event.");
            CloseEventPanel();
            return;
        }

        CompanyManager company = GetCompany();
        double valueBefore = company != null ? company.companyValue : 0.0;

        // Jinx Removal: sometimes a bad event just doesn't happen
        EventOutcome outcome;
        if (!chosenEvent.isGood && skills != null &&
            Random.value < Mathf.Min(skills.GetEffect(SkillEffect.JinxRemoval), MaxJinxRemovalChance))
        {
            outcome = new EventOutcome { cancelled = true };
            AddNote(notes, L10n.Get(T, "risk.note.jinx_removed"));
        }
        else
        {
            outcome = ApplyEvent(chosenEvent, skills, notes);

            // Track for Gambler's Fallacy and Lucky Streak (a cancelled bad event changes neither)
            lastEventWasBad = !chosenEvent.isGood;
            goodStreak = chosenEvent.isGood ? goodStreak + 1 : 0;
        }

        // Run stats and other listeners (a listener's error must not leave the card half-played)
        try { CardResolved?.Invoke(chosenEvent.eventName, outcome.valueChange); }
        catch (System.Exception e) { Debug.LogException(e, this); }
        try { CardResolvedWithKey?.Invoke(chosenEvent.eventName, chosenEvent.key, outcome.valueChange); }
        catch (System.Exception e) { Debug.LogException(e, this); }
        try { RiskCardSettled?.Invoke(risk, chosenEvent.isGood, valueBefore, outcome.valueChange); }
        catch (System.Exception e) { Debug.LogException(e, this); }

        ShowResult(chosenEvent, outcome, notes.ToString());
    }

    /// <summary>
    /// Roll good or bad, then let the luck skills save a bad roll: Quantum Luck (roll twice, keep the better),
    /// Reality Warp (10%: counts as good), Reverse Karma (25%: bad becomes good), Wild Card (once a day: reroll).
    /// </summary>
    bool RollOutcome(float goodChance, SkillTreeManager skills, System.Text.StringBuilder notes)
    {
        bool isGood = Random.value < goodChance;
        if (isGood || skills == null) return isGood;

        if (skills.HasEffect(SkillEffect.QuantumLuck) && Random.value < goodChance)
        {
            AddNote(notes, L10n.Get(T, "risk.note.quantum_luck"));
            return true;
        }

        if (skills.HasEffect(SkillEffect.RealityWarp) && Random.value < RealityWarpChance * skills.EffectMultiplier)
        {
            AddNote(notes, L10n.Get(T, "risk.note.reality_warp"));
            return true;
        }

        if (Random.value < Mathf.Min(skills.GetEffect(SkillEffect.ReverseKarma), MaxReverseKarmaChance))
        {
            AddNote(notes, L10n.Get(T, "risk.note.reverse_karma"));
            return true;
        }

        CompanyManager cm = GetCompany();
        int today = cm != null ? cm.currentDay : 0;
        if (skills.HasEffect(SkillEffect.WildCard) && wildCardUsedDay != today)
        {
            wildCardUsedDay = today;
            isGood = Random.value < goodChance;
            AddNote(notes, L10n.Get(T, isGood ? "risk.note.wild_card.paid" : "risk.note.wild_card.bad"));
        }
        return isGood;
    }

    static void AddNote(System.Text.StringBuilder notes, string line)
    {
        if (notes.Length > 0) notes.Append('\n');
        notes.Append(line);
    }

    /// <summary>A share as a whole percent number, without the sign: 0.25 → "25" (the String Table entry places the %).</summary>
    static string PercentNumber(double share)
    {
        return (share * 100.0).ToString("0", System.Globalization.CultureInfo.InvariantCulture);
    }

    EventOutcome ApplyEvent(RandomEvent evt, SkillTreeManager skills, System.Text.StringBuilder notes)
    {
        EventOutcome outcome = new EventOutcome();
        CompanyManager cm = GetCompany();
        if (cm == null) return outcome;

        double moneyChange = evt.moneyChange;
        double percentChange = evt.percentageChange;
        bool jackpot = false;

        // Skill tree modifiers
        if (skills != null)
        {
            if (!evt.isGood)
            {
                // Negative Event Reduction (Public Relations)
                float reduction = Mathf.Clamp01(skills.GetNegativeEventReduction());
                moneyChange *= (1f - reduction);
                percentChange *= (1f - reduction);

                // Golden Parachute: lose less on value drops
                float parachute = Mathf.Clamp01(skills.GetEffect(SkillEffect.GoldenParachute));
                moneyChange *= (1f - parachute);
                percentChange *= (1f - parachute);

                // Unlucky Protection: bad luck hurts less
                float protection = Mathf.Clamp(skills.GetEffect(SkillEffect.UnluckyProtection), 0f, MaxUnluckyProtection);
                if (protection > 0f)
                {
                    moneyChange *= (1f - protection);
                    percentChange *= (1f - protection);
                    AddNote(notes, L10n.Get(T, "risk.note.unlucky_protection", ("percent", PercentNumber(protection))));
                }
            }
            else
            {
                double multiplier = 1.0;

                // Chaotic Energy: better rewards from good events
                float chaoticBonus = Mathf.Max(0f, skills.GetEffect(SkillEffect.ChaoticEnergy));
                multiplier *= 1.0 + chaoticBonus;

                // Lucky Streak: +15% for each good result in a row before this one (up to 3)
                float streakStep = Mathf.Max(0f, skills.GetEffect(SkillEffect.LuckyStreak));
                int streakSteps = Mathf.Min(goodStreak, MaxLuckyStreakSteps);
                if (streakStep > 0f && streakSteps > 0)
                {
                    multiplier *= 1.0 + streakStep * streakSteps;
                    AddNote(notes, L10n.Get(T, "risk.note.lucky_streak", ("count", goodStreak + 1), ("percent", PercentNumber(streakStep * streakSteps))));
                }

                // Critical Success: chance for double rewards
                float critChance = skills.GetCriticalSuccessChance();
                if (critChance > 0f && Random.value < critChance)
                {
                    multiplier *= 2.0;
                    AddNote(notes, L10n.Get(T, "risk.note.critical_success"));
                    Debug.Log("CRITICAL SUCCESS! Event rewards doubled!");
                }

                // Double or Nothing: x2 or x0
                if (skills.HasEffect(SkillEffect.DoubleOrNothing))
                {
                    if (Random.value < DoubleOrNothingChance)
                    {
                        multiplier *= 2.0;
                        AddNote(notes, L10n.Get(T, "risk.note.double_or_nothing.doubled"));
                    }
                    else
                    {
                        multiplier = 0.0;
                        AddNote(notes, L10n.Get(T, "risk.note.double_or_nothing.nothing"));
                    }
                }

                // Jackpot Chance: rarely x10
                float jackpotChance = Mathf.Min(skills.GetEffect(SkillEffect.JackpotChance), MaxJackpotChance);
                if (multiplier > 0.0 && jackpotChance > 0f && Random.value < jackpotChance)
                {
                    multiplier *= JackpotMultiplier;
                    jackpot = true;
                    AddNote(notes, L10n.Get(T, "risk.note.jackpot", ("multiplier", JackpotMultiplier.ToString("0", System.Globalization.CultureInfo.InvariantCulture))));
                }

                moneyChange *= multiplier;
                // The percentage part compounds on the whole company, so one card can at most double it
                percentChange = System.Math.Min(percentChange * multiplier, MaxEventPercentGain);
            }
        }

        double valueBefore = cm.companyValue;

        // Demo: one bad card takes at most RiskLossCap of the company (outcome.valueChange below still matches)
        float lossCap = DemoMode.RiskLossCap;
        if (!evt.isGood && lossCap < 1f && valueBefore > 0.0)
        {
            double loss = -(moneyChange + (valueBefore + moneyChange) * percentChange);
            if (loss > lossCap * valueBefore)
            {
                moneyChange = -lossCap * valueBefore;
                percentChange = 0;
                AddNote(notes, DemoMode.T("demo.risk.capped", ("percent", PercentNumber(lossCap))));
            }
        }

        // Apply direct money change, then the percentage (instant = true for immediate display)
        cm.AddMoney(moneyChange, true);
        if (percentChange != 0f)
            cm.AddPercentage(percentChange, true);

        // The change actually applied (company value never goes below zero), worked out from the inputs
        outcome.valueChange = cm.companyValue <= 0.0
            ? -valueBefore
            : moneyChange + (valueBefore + moneyChange) * percentChange;

        // Notify CompanyManager of event outcome for happiness/trust tracking
        cm.OnRiskCardResolved(evt.isGood, outcome.valueChange);

        if (jackpot)
            GameNotifications.Show(L10n.Get(T, "risk.toast.jackpot", ("title", evt.DisplayName), ("amount", FormatSignedMoney(outcome.valueChange))),
                                   NotificationKind.Special);
        return outcome;
    }

    void ShowResult(RandomEvent evt, EventOutcome outcome, string notes)
    {
        state = EventState.ShowingResult;
        // Good: chips; a bad event Jinx Removal cancelled: relief; bad: a glitch (after the chosen card's sound)
        GameSfx.PlayDelayed(evt.isGood ? Sfx.ChipsWin : outcome.cancelled ? Sfx.UiConfirm : Sfx.UiGlitch, 0.3f);

        // Hide card selection, show result
        if (cardSelectionPanel != null) cardSelectionPanel.SetActive(false);
        if (resultPanel != null) resultPanel.SetActive(true);

        // A cancelled bad event (Jinx Removal) is a lucky escape
        Color outcomeColor = evt.isGood || outcome.cancelled ? goodResultColor : badResultColor;

        if (resultTitleText != null)
        {
            resultTitleText.text = evt.DisplayName;
            resultTitleText.color = outcomeColor;
        }

        if (resultDescriptionText != null)
        {
            string description = evt.DisplayDescription;   // one line: RiskResultView splits story / notes at the first break
            if (!string.IsNullOrEmpty(notes))
                description += "\n" + notes;
            resultDescriptionText.text = description;
        }

        if (resultValueText != null)
        {
            resultValueText.text = FormatSignedMoney(outcome.valueChange);
            resultValueText.color = outcomeColor;
        }

        if (continueButton != null)
            continueButton.interactable = true;
    }

    /// <summary>Hide the event, resume the game and start the timer for the next event.</summary>
    public void CloseEventPanel()
    {
        EndEvent();
        SetNextEventTime();
    }

    void EndEvent()
    {
        state = EventState.Idle;
        if (eventPanel != null && eventPanel.activeSelf)
            eventPanel.SetActive(false);

        // Resume game
        if (UIManager.Exists)
            UIManager.Instance.ClosePanel(this);
    }

    /// <summary>"+$1,234", "−$20,000" (true minus sign) or "$0".</summary>
    public static string FormatSignedMoney(double amount)
    {
        return UIFormat.SignedMoney(amount);
    }

    void ApplyTextFixes()
    {
        // Card labels were 24 pt without wrapping in 120 px cards
        FitText(highRiskText, 12f, 24f);
        FitText(balancedText, 12f, 24f);
        FitText(lowRiskText, 12f, 24f);

        // Long event names and descriptions shrink to fit instead of spilling out
        if (resultTitleText != null)
            FitText(resultTitleText, 14f, Mathf.Max(14f, resultTitleText.fontSize));

        if (resultDescriptionText != null)
        {
            // The description was white on the off-white result card
            resultDescriptionText.color = resultDescriptionColor;
            FitText(resultDescriptionText, 10f, Mathf.Max(10f, resultDescriptionText.fontSize));
        }
    }

    static void FitText(TextMeshProUGUI text, float minSize, float maxSize)
    {
        if (text == null) return;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.enableAutoSizing = true;
        text.fontSizeMin = minSize;
        text.fontSizeMax = maxSize;
    }

    // Call this to manually trigger an event (for testing)
    public void ForceEvent()
    {
        TriggerRandomEvent();
    }
}
