using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The board meeting memo: an internal memo on the company letterhead, TO the CEO, FROM the Board (the same board that
/// signs the promotion memos), RE Board meeting, day N. Top to bottom: the last period's result with its numbers (target,
/// actual, reward or penalty) and a HIT / MISSED stamp, the vote of no confidence when there was one, then the three
/// proposal cards (Safe, Stretch, Moonshot: target gain, "reach $X by day N", reward and penalty) and Skip.
/// - BoardMeetings opens it (Show) and hears the answer through the callback: a card index or BoardMeetings.SkipChoice.
///   It pauses the game (UIManager) and has no close button: the meeting needs an answer.
/// - Escape asks first: "Skip the meeting?" with Skip meeting / Back (Escape again = Back). The memo's own Skip button
///   skips straight away (its label shows the trust cost). Clicks in the first half second don't count.
/// - With Root empty BoardMeetingFactory builds it at runtime on its own overlay canvas (sorting order 140). A designed
///   prefab can fill in the same references instead; references it leaves empty are skipped (without a Confirm Group,
///   Escape does nothing). The memo shrinks to fit screens smaller than it.
/// - Texts come from the "Board" String Table (board.memo.*, board.card.*, board.confirm.*) through L10n. When the
///   language changes while the memo is open it is filled again, and every text under Root follows the language's font.
/// </summary>
[DisallowMultipleComponent]
public class BoardMeetingView : MonoBehaviour
{
    const string T = BoardMeetings.Table;

    /// <summary>One proposal card's references.</summary>
    [Serializable]
    public class ProposalCard
    {
        [Tooltip("The whole card: clicking it commits to the target")]
        public Button button;
        [Tooltip("\"STRETCH\"")]
        public TMP_Text tierText;
        [Tooltip("\"1.5× expected\"")]
        public TMP_Text paceText;
        [Tooltip("\"+$15,000\"")]
        public TMP_Text gainText;
        [Tooltip("\"reach $65,000 by day 20\"")]
        public TMP_Text reachText;
        [Tooltip("\"Hit: trust +10, bonus 2% of value (~$1,300)\", then \"Miss: trust −12\" on the next line")]
        public TMP_Text termsText;
        [Tooltip("Stamped onto the card when it is chosen (optional)")]
        public RectTransform chosenStamp;
    }

    const float OpenSeconds = 0.28f;        // the memo fades in and settles
    const float StampDelay = 0.35f;         // then the result stamp comes down
    const float StampSeconds = 0.18f;
    const float InputDelay = 0.45f;         // clicks this soon after opening don't count (a stray click can't commit)
    const float ChosenHoldSeconds = 0.75f;  // the chosen card's stamp shows this long before the memo closes
    const int NoChoice = -2;

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    [Header("Look")]
    [Tooltip("Font of the memo (empty = the Coming Soon font the scene's UI already uses, else TextMesh Pro's default)")]
    public TMP_FontAsset font;
    [Tooltip("Sorting order of the runtime canvas: above the HUD, the crisis games and the run-end screens, below the promotion memo and the toasts")]
    public int sortingOrder = 140;
    [Tooltip("The memo shrinks to stay at least this far from the screen edges (reference pixels)")]
    public float screenMargin = 16f;

    [Header("Screen (leave Root empty to build everything at runtime)")]
    [Tooltip("Everything of the meeting, scrim included; shown while it is open")]
    public RectTransform root;
    [Tooltip("The memo paper (fades in and settles when it opens, shrinks to fit small screens)")]
    public RectTransform memo;

    [Header("Letterhead")]
    [Tooltip("The company name (written in capitals by the language's casing rules)")]
    public TMP_Text letterheadText;
    [Tooltip("\"RE Board meeting, day 20\"")]
    public TMP_Text reText;
    [Tooltip("\"Stretch target hit. The board is impressed.\"")]
    public TMP_Text headlineText;

    [Header("Last period")]
    [Tooltip("\"LAST PERIOD · DAYS 10–20\"")]
    public TMP_Text resultLabelText;
    [Tooltip("The HIT / MISSED / SKIPPED / NO TARGET stamp (comes down after the memo opens)")]
    public RectTransform resultStamp;
    public TMP_Text resultStampText;
    [Tooltip("Coloured by the verdict: green hit, red miss, pencil without a target (a StatusBox gets its border set)")]
    public Graphic[] resultStampTinted;
    [Tooltip("Target, actual, and reward or penalty (three lines)")]
    public TMP_Text resultText;

    [Header("Vote of no confidence")]
    [Tooltip("Shown only when the board forced a restructuring at this meeting")]
    public GameObject noConfidenceGroup;
    public TMP_Text noConfidenceText;

    [Header("New proposals")]
    [Tooltip("\"NEW PROPOSALS · DAYS 20–30\"")]
    public TMP_Text proposalsLabelText;
    [Tooltip("\"Company value today: $1,250,000\"")]
    public TMP_Text valueTodayText;
    [Tooltip("\"Pick one growth target for the next 10 days. All growth counts. The board doesn't care how.\"")]
    public TMP_Text introText;
    [Tooltip("Safe, Stretch, Moonshot")]
    public ProposalCard[] cards = new ProposalCard[BoardMeetings.TierCount];

    [Header("Skip")]
    [Tooltip("Skips the meeting straight away")]
    public Button skipButton;
    [Tooltip("\"Skip this meeting · trust −3\"")]
    public TMP_Text skipText;

    [Header("Escape: \"Skip the meeting?\"")]
    [Tooltip("Shown by Escape; hidden again by Back or a second Escape")]
    public GameObject confirmGroup;
    [Tooltip("\"Trust −3. No target until day 30.\"")]
    public TMP_Text confirmText;
    public Button confirmSkipButton;
    public Button confirmBackButton;

    /// <summary>True while the memo is on screen.</summary>
    public bool IsOpen { get; private set; }
    /// <summary>The "Skip the meeting?" question is up.</summary>
    public bool IsConfirmShowing => confirmGroup != null && confirmGroup.activeSelf;

    /// <summary>Raised when the memo closes.</summary>
    public event Action Closed;

    // Set by BoardMeetingFactory: the runtime memo moves its lower half down and grows while the vote block shows
    // (a designed prefab lays itself out)
    internal RectTransform runtimeLower;
    internal Vector2 runtimeLowerHome;
    internal float runtimeBaseHeight;
    internal float runtimeVoteShift;

    Canvas runtimeCanvas;
    CanvasGroup memoGroup;
    CanvasGroup stampGroup;
    Vector3 memoScale = Vector3.one;
    Vector3 stampScale = Vector3.one;
    float stampAlpha = 1f;
    float fitScale = 1f;
    float openAge;
    bool hooked;
    Action<int> onChoice;
    int chosen = NoChoice;
    float chosenAge;
    CanvasGroup[] chosenGroups = new CanvasGroup[0];
    Vector3[] chosenScales = new Vector3[0];
    float[] chosenAlphas = new float[0];
    bool fontsFollowLanguage;
    BoardMeetingReport shownReport;   // the meeting on screen, filled again when the language changes
    string shownCompany;

    void OnEnable()
    {
        L10n.LanguageChanged += OnLanguageChanged;
    }

    void OnDisable()
    {
        L10n.LanguageChanged -= OnLanguageChanged;
        // Switched off or leaving the scene while open: don't leave the scrim up or the game paused
        if (root != null && root.gameObject.activeSelf && !transform.IsChildOf(root)) root.gameObject.SetActive(false);
        ReleasePanel();
    }

    // The fonts switch by themselves (L10nFont); the memo's texts are written again in the new language
    void OnLanguageChanged()
    {
        if (IsOpen && shownReport != null) Fill(shownReport, shownCompany);
    }

    void OnDestroy()
    {
        ReleasePanel();
        if (runtimeCanvas != null) Destroy(runtimeCanvas.gameObject);
    }

    /// <summary>Build the memo at runtime if no designed one is assigned, and connect the buttons.</summary>
    public void EnsureBuilt()
    {
        if (root == null)
            runtimeCanvas = BoardMeetingFactory.Build(this, BoardUI.ResolveFont(font));

        if (memo != null && memoGroup == null)
        {
            memoGroup = GetOrAdd<CanvasGroup>(memo.gameObject);
            memoScale = memo.localScale;
        }
        if (resultStamp != null && stampGroup == null)
        {
            stampGroup = GetOrAdd<CanvasGroup>(resultStamp.gameObject);
            stampGroup.blocksRaycasts = false;
            stampAlpha = stampGroup.alpha;
            stampScale = resultStamp.localScale;
        }
        if (root != null && !fontsFollowLanguage)
        {
            // Every text of the memo (built here or designed) takes the language's font
            L10nFont.AttachAll(root);
            fontsFollowLanguage = true;
        }
        CaptureChosenStamps();
        HookButtons();
    }

    void CaptureChosenStamps()
    {
        int count = cards != null ? cards.Length : 0;
        if (chosenGroups.Length == count) return;
        chosenGroups = new CanvasGroup[count];
        chosenScales = new Vector3[count];
        chosenAlphas = new float[count];
        for (int i = 0; i < count; i++)
        {
            RectTransform stamp = cards[i] != null ? cards[i].chosenStamp : null;
            if (stamp == null) continue;
            chosenGroups[i] = GetOrAdd<CanvasGroup>(stamp.gameObject);
            chosenGroups[i].blocksRaycasts = false;
            chosenAlphas[i] = chosenGroups[i].alpha;
            chosenScales[i] = stamp.localScale;
            stamp.gameObject.SetActive(false);
        }
    }

    void HookButtons()
    {
        if (hooked) return;
        hooked = true;
        if (cards != null)
        {
            for (int i = 0; i < cards.Length; i++)
            {
                int index = i;
                if (cards[i] != null && cards[i].button != null) cards[i].button.onClick.AddListener(() => OnCard(index));
            }
        }
        if (skipButton != null) skipButton.onClick.AddListener(OnSkip);
        if (confirmSkipButton != null) confirmSkipButton.onClick.AddListener(OnConfirmSkip);
        if (confirmBackButton != null) confirmBackButton.onClick.AddListener(OnConfirmBack);
    }

    /// <summary>
    /// Open the memo for <paramref name="report"/> (pauses the game). <paramref name="choice"/> hears the answer once: a card
    /// index (0 Safe, 1 Stretch, 2 Moonshot) or BoardMeetings.SkipChoice.
    /// </summary>
    public void Show(BoardMeetingReport report, string companyName, Action<int> choice)
    {
        if (report == null) return;
        EnsureBuilt();
        if (root == null) return;

        onChoice = choice;
        chosen = NoChoice;
        chosenAge = 0f;
        HideChosenStamps();
        SetConfirm(false);
        Fill(report, companyName);

        root.gameObject.SetActive(true);
        UIManager.BringToFront(root.gameObject);
        BoardUI.ClearSelection();
        IsOpen = true;
        UIManager ui = UIManager.Instance;
        if (ui != null) ui.OpenPanel(this, true, OnEscape);
        GameSfx.Play(report.noConfidence ? Sfx.UiGlitch : Sfx.UiQuestion);   // the board wants a word (a vote of no confidence: a glitch)
        // The result stamp lands after the memo opens: HIT with chips, MISSED with an error, SKIPPED / NO TARGET on its own
        GameSfx.PlayPair(Sfx.UiScratch, !report.hadTarget ? Sfx.None : report.hit ? Sfx.ChipsWin : Sfx.UiError, 0.12f, StampDelay + StampSeconds);

        openAge = 0f;
        FitToScreen();
        Animate();
    }

    /// <summary>Close the memo without an answer (BoardMeetings brings the meeting back once the screen is calm).</summary>
    public void Close()
    {
        if (!IsOpen) return;
        if (root != null) root.gameObject.SetActive(false);
        SetConfirm(false);
        HideChosenStamps();
        onChoice = null;
        ReleasePanel();
        try { Closed?.Invoke(); }
        catch (Exception e) { Debug.LogException(e); }
    }

    void ReleasePanel()
    {
        if (!IsOpen) return;
        IsOpen = false;
        if (UIManager.Exists) UIManager.Instance.ClosePanel(this);
    }

    void Update()
    {
        if (!IsOpen) return;
        // UI state was reset (e.g. by a debug tool) while the memo is up: keep the game paused
        if (UIManager.Exists && !UIManager.Instance.IsPanelOpen(this))
            UIManager.Instance.OpenPanel(this, true, OnEscape);

        // The game is paused. One frame counts at most 0.1 s: the frame that opened the memo (a save, the first-time
        // build) can be long and would otherwise use up the click guard and the opening animation at once.
        openAge += Mathf.Min(Time.unscaledDeltaTime, 0.1f);
        FitToScreen();
        Animate();

        if (chosen != NoChoice)
        {
            chosenAge += Time.unscaledDeltaTime;
            AnimateChosenStamp();
            if (chosenAge >= ChosenHoldSeconds) Close();
        }
    }

    // ------------------------------------------------------------------
    // Answers
    // ------------------------------------------------------------------

    bool AcceptsChoice => IsOpen && chosen == NoChoice && openAge >= InputDelay;

    void OnCard(int index)
    {
        if (!AcceptsChoice || IsConfirmShowing) return;
        Choose(index);
    }

    void OnSkip()
    {
        if (!AcceptsChoice || IsConfirmShowing) return;
        Choose(BoardMeetings.SkipChoice);
    }

    void OnConfirmSkip()
    {
        if (!AcceptsChoice) return;
        Choose(BoardMeetings.SkipChoice);
    }

    void OnConfirmBack()
    {
        SetConfirm(false);
    }

    void OnEscape()
    {
        if (!IsOpen || chosen != NoChoice) return;   // already answered: the memo is closing on its own
        SetConfirm(!IsConfirmShowing);
    }

    void Choose(int index)
    {
        chosen = index;
        chosenAge = 0f;
        SetConfirm(false);
        GameSfx.Play(index >= 0 ? Sfx.UiConfirm : Sfx.UiBack);   // a target chosen, or the meeting skipped
        Action<int> answer = onChoice;
        onChoice = null;
        try { answer?.Invoke(index); }
        catch (Exception e) { Debug.LogException(e); }

        RectTransform stamp = index >= 0 && cards != null && index < cards.Length && cards[index] != null ? cards[index].chosenStamp : null;
        if (stamp == null)
        {
            Close();
            return;
        }
        stamp.gameObject.SetActive(true);
        GameSfx.PlayDelayed(Sfx.UiScratch, StampSeconds);   // the card's stamp comes down
        AnimateChosenStamp();
    }

    void SetConfirm(bool show)
    {
        if (confirmGroup == null) return;
        if (confirmGroup.activeSelf != show) confirmGroup.SetActive(show);
        if (show) BoardUI.ClearSelection();
    }

    // ------------------------------------------------------------------
    // Animation and size
    // ------------------------------------------------------------------

    void Animate()
    {
        float t = Mathf.Clamp01(openAge / OpenSeconds);
        float ease = 1f - (1f - t) * (1f - t) * (1f - t);
        if (memo != null) memo.localScale = memoScale * (fitScale * Mathf.Lerp(0.94f, 1f, ease));
        if (memoGroup != null) memoGroup.alpha = ease;

        // The stamp speeds up as it comes down onto the paper
        float s = Mathf.Clamp01((openAge - StampDelay) / StampSeconds);
        if (resultStamp != null) resultStamp.localScale = stampScale * Mathf.Lerp(1.6f, 1f, s * s);
        if (stampGroup != null) stampGroup.alpha = stampAlpha * Mathf.Clamp01(s * 2f);
    }

    void AnimateChosenStamp()
    {
        if (chosen < 0 || cards == null || chosen >= cards.Length || chosen >= chosenGroups.Length || cards[chosen] == null) return;
        RectTransform stamp = cards[chosen].chosenStamp;
        if (stamp == null) return;
        float s = Mathf.Clamp01(chosenAge / StampSeconds);
        stamp.localScale = chosenScales[chosen] * Mathf.Lerp(1.6f, 1f, s * s);
        if (chosenGroups[chosen] != null) chosenGroups[chosen].alpha = chosenAlphas[chosen] * Mathf.Clamp01(s * 2f);
    }

    void HideChosenStamps()
    {
        if (cards == null) return;
        for (int i = 0; i < cards.Length && i < chosenScales.Length; i++)
        {
            RectTransform stamp = cards[i] != null ? cards[i].chosenStamp : null;
            if (stamp == null) continue;
            stamp.localScale = chosenScales[i];
            if (stamp.gameObject.activeSelf) stamp.gameObject.SetActive(false);
        }
    }

    /// <summary>Shrink the memo (never grow it) so it fits the canvas with screenMargin to spare.</summary>
    void FitToScreen()
    {
        fitScale = 1f;
        if (memo == null) return;
        Canvas canvas = memo.GetComponentInParent<Canvas>();
        RectTransform area = canvas != null ? (RectTransform)canvas.rootCanvas.transform : root;
        if (area == null) return;
        Rect bounds = area.rect;
        float width = memo.rect.width * Mathf.Abs(memoScale.x);
        float height = memo.rect.height * Mathf.Abs(memoScale.y);
        // A canvas made this frame isn't sized yet (the memo is still invisible then, it fades in)
        if (width <= 0f || height <= 0f || bounds.width < 320f || bounds.height < 320f) return;
        float fit = Mathf.Min((bounds.width - 2f * screenMargin) / width, (bounds.height - 2f * screenMargin) / height);
        fitScale = Mathf.Clamp(fit, 0.35f, 1f);
    }

    // ------------------------------------------------------------------
    // Content
    // ------------------------------------------------------------------

    void Fill(BoardMeetingReport r, string companyName)
    {
        shownReport = r;
        shownCompany = companyName;
        // CompanyNameUI's default name ("New Company", also for no name) shows in the player's language
        string company = CompanyNameUI.DisplayName(companyName);
        // The player's company name in capitals, by the language's rules (Turkish i -> İ)
        SetText(letterheadText, L10n.ToUpper(company));
        SetText(reText, L10n.Get(T, "board.memo.re", ("label", RoutingLabel(L10n.Get(T, "board.memo.re.label"))), ("day", r.day)));
        SetText(headlineText, L10n.Get(T, HeadlineKey(r)));
        FillResult(r);
        FillVote(r);
        FillProposals(r);

        string trust = BoardMeetings.Signed(r.skipTrust);
        string cost = BoardUI.Colored(L10n.Get(T, "board.memo.button.skip.cost", ("trust", trust)), UIColors.Pencil);
        SetText(skipText, L10n.Get(T, "board.memo.button.skip", ("cost", cost)));
        SetText(confirmText, L10n.Get(T, "board.confirm.body", ("trust", trust), ("day", r.dueDay)));
    }

    /// <summary>"TO", "FROM", "RE" (already in the language) in pencil small caps, like the promotion memo's routing line.</summary>
    public static string RoutingLabel(string word)
    {
        return "<color=#676B74><cspace=0.08em>" + word + "</cspace></color>";
    }

    static string HeadlineKey(BoardMeetingReport r)
    {
        if (r.noConfidence) return "board.memo.headline.no_confidence";
        if (r.hadTarget)
        {
            if (r.hit)
            {
                if (r.resultTier == BoardMeetings.TierMoonshot) return "board.memo.headline.hit.moonshot";
                if (r.resultTier == BoardMeetings.TierStretch) return "board.memo.headline.hit.stretch";
                return "board.memo.headline.hit.safe";
            }
            if (r.resultTier == BoardMeetings.TierMoonshot) return "board.memo.headline.miss.moonshot";
            return "board.memo.headline.miss";
        }
        if (r.lastSkipped) return "board.memo.headline.skipped";
        if (r.firstMeeting) return "board.memo.headline.first";
        return "board.memo.headline.default";
    }

    void FillResult(BoardMeetingReport r)
    {
        SetText(resultLabelText, L10n.Get(T, "board.memo.result.label", ("from", r.periodStartDay), ("to", r.day)));

        string word;
        Color color;
        string body;
        if (r.hadTarget)
        {
            word = L10n.Get(T, r.hit ? "board.memo.stamp.hit" : "board.memo.stamp.missed");
            color = r.hit ? UIColors.Money : UIColors.Loss;
            // The report keeps the English name: shown in the current language (a custom name saved back then as it was)
            string tier = BoardMeetings.TierDisplayName(r.resultTier, r.resultTierName);
            double targetGain = r.resultTargetValue - r.resultStartValue;
            double actualGain = r.resultActualValue - r.resultStartValue;
            string target = L10n.Get(T, "board.memo.result.target", ("label", Bold(L10n.Get(T, "board.memo.result.target.label", ("tier", tier)))),
                                     ("value", UIFormat.Money(r.resultTargetValue)), ("gain", UIFormat.SignedMoney(targetGain)));
            string actual = L10n.Get(T, "board.memo.result.actual", ("label", Bold(L10n.Get(T, "board.memo.result.actual.label"))),
                                     ("value", UIFormat.Money(r.resultActualValue)), ("gain", BoardUI.Colored(UIFormat.SignedMoney(actualGain), color)));
            string trust = BoardMeetings.Signed(r.resultTrustChange);
            string outcome;
            if (!r.hit)
                outcome = L10n.Get(T, "board.memo.result.penalty", ("label", Bold(L10n.Get(T, "board.memo.result.penalty.label"))), ("trust", trust));
            else if (r.resultBonus > 0.0)
                outcome = L10n.Get(T, "board.memo.result.reward.bonus", ("label", Bold(L10n.Get(T, "board.memo.result.reward.label"))), ("trust", trust),
                                   ("bonus", UIFormat.SignedMoney(r.resultBonus)));
            else
                outcome = L10n.Get(T, "board.memo.result.reward", ("label", Bold(L10n.Get(T, "board.memo.result.reward.label"))), ("trust", trust));
            body = target + "\n" + actual + "\n" + BoardUI.Colored(outcome, color);
        }
        else if (r.lastSkipped)
        {
            word = L10n.Get(T, "board.memo.stamp.skipped");
            color = UIColors.Pencil;
            body = L10n.Get(T, "board.memo.result.skipped");
        }
        else if (r.firstMeeting)
        {
            word = L10n.Get(T, "board.memo.stamp.no_target");
            color = UIColors.Pencil;
            body = L10n.Get(T, "board.memo.result.first", ("days", Mathf.Max(1, r.dueDay - r.day)));
        }
        else
        {
            word = L10n.Get(T, "board.memo.stamp.no_target");
            color = UIColors.Pencil;
            body = L10n.Get(T, "board.memo.result.none");
        }

        SetText(resultStampText, word);
        BoardUI.Tint(resultStampTinted, color);
        SetText(resultText, body);
    }

    void FillVote(BoardMeetingReport r)
    {
        bool vote = r.noConfidence;
        if (noConfidenceGroup != null && noConfidenceGroup.activeSelf != vote) noConfidenceGroup.SetActive(vote);
        if (vote)
        {
            // Rounded down, so the trust shown is below the threshold too
            string trust = Bold(Mathf.FloorToInt(r.noConfidenceTrust).ToString(Inv));
            string threshold = BoardUI.Number(r.noConfidenceBelow);
            string reset = Bold(BoardUI.Number(r.trustAfterRestructuring));
            SetText(noConfidenceText, r.restructuringCost > 0.0
                ? L10n.Get(T, "board.memo.vote.body", ("trust", trust), ("threshold", threshold), ("cost", Bold(UIFormat.SignedMoney(-r.restructuringCost))),
                           ("percent", BoardUI.PercentNumber(r.restructuringShare)), ("reset", reset))
                : L10n.Get(T, "board.memo.vote.body.nothing", ("trust", trust), ("threshold", threshold), ("reset", reset)));
        }

        if (runtimeLower != null && memo != null)
        {
            runtimeLower.anchoredPosition = runtimeLowerHome + new Vector2(0f, vote ? -runtimeVoteShift : 0f);
            memo.sizeDelta = new Vector2(memo.sizeDelta.x, runtimeBaseHeight + (vote ? runtimeVoteShift : 0f));
        }
    }

    void FillProposals(BoardMeetingReport r)
    {
        int days = Mathf.Max(1, r.dueDay - r.day);
        SetText(proposalsLabelText, L10n.Get(T, "board.memo.proposals.label", ("from", r.day), ("to", r.dueDay)));
        SetText(valueTodayText, L10n.Get(T, "board.memo.value_today", ("value", Bold(UIFormat.Money(r.startValue)))));
        SetText(introText, L10n.Get(T, "board.memo.intro", ("days", days)));
        if (cards == null) return;

        string hitLabel = BoardUI.Colored(Bold(L10n.Get(T, "board.card.hit.label")), UIColors.Money);
        string missLabel = BoardUI.Colored(Bold(L10n.Get(T, "board.card.miss.label")), UIColors.Loss);
        for (int i = 0; i < cards.Length; i++)
        {
            ProposalCard card = cards[i];
            if (card == null) continue;
            BoardProposal p = r.proposals != null && i < r.proposals.Length ? r.proposals[i] : null;
            if (card.button != null) card.button.interactable = p != null;
            if (p == null) continue;

            // Capitals written in the String Table (a custom name by the language's casing rules)
            SetText(card.tierText, BoardMeetings.TierDisplayName(p.tier, p.name, true));
            SetText(card.paceText, L10n.Get(T, "board.card.pace", ("multiplier", BoardUI.Number(p.multiplier))));
            SetText(card.gainText, UIFormat.SignedMoney(p.gain));
            SetText(card.reachText, L10n.Get(T, "board.card.reach", ("value", Bold(UIFormat.Money(p.targetValue))), ("day", p.dueDay)));

            string hitTrust = BoardMeetings.Signed(p.hitTrust);
            string hit = p.hitBonusShare > 0f
                ? L10n.Get(T, "board.card.hit.bonus", ("label", hitLabel), ("trust", hitTrust), ("percent", BoardUI.PercentNumber(p.hitBonusShare)),
                           ("amount", UIFormat.Money(p.bonusAtTarget)))
                : L10n.Get(T, "board.card.hit", ("label", hitLabel), ("trust", hitTrust));
            string miss = L10n.Get(T, "board.card.miss", ("label", missLabel), ("trust", BoardMeetings.Signed(p.missTrust)));
            SetText(card.termsText, hit + "\n" + miss);
        }
    }

    static string Bold(string text)
    {
        return "<b>" + text + "</b>";
    }

    static void SetText(TMP_Text text, string value)
    {
        if (text != null) text.text = value;
    }

    static T GetOrAdd<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        return component != null ? component : target.AddComponent<T>();
    }

#if UNITY_EDITOR
    void Reset()
    {
        // Added in the Editor: start with the design's font
        font = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/UI/Fonts/ComingSoon SDF.asset");
    }
#endif
}
