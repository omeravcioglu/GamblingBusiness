using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The promotion memo (design "Status upgrade: promotion moment"): an internal memo on the company letterhead, to the
/// CEO from the Board: "Nova Holdings is now a Corporation", the threshold passed, what's new at this tier, a
/// before/after picture of the ledger's corner, progress to the next tier, "Signed, the Board" and a PROMOTED stamp.
/// - StatusLadder opens it (Show) once the screen is calm after a promotion. It pauses the game (UIManager);
///   Nice, the X and Escape close it.
/// - With Root empty, PromotionMemoFactory builds it at runtime on its own overlay canvas (sorting order 450: above the
///   HUD and build mode, below the toasts). A designed prefab can fill in the same references instead; references it
///   leaves empty are skipped.
/// - Texts come from the "Status" String Table (status.memo.*) through L10n; when the language changes while the memo is
///   open it is filled again, and every text under Root follows the language's font (L10nFont).
/// </summary>
[DisallowMultipleComponent]
public class PromotionMemoView : MonoBehaviour
{
    public static PromotionMemoView Instance { get; private set; }

    const string DesignFontFamily = "Coming Soon";
    const float OpenSeconds = 0.28f;    // the memo fades in and settles
    const float StampDelay = 0.35f;     // then the stamp comes down
    const float StampSeconds = 0.18f;

    [Header("Look")]
    [Tooltip("Font of the memo (empty = the Coming Soon font the scene's UI already uses, else TextMesh Pro's default)")]
    public TMP_FontAsset font;
    [Tooltip("Sorting order of the runtime canvas: above the HUD and build mode, below the toasts (900)")]
    public int sortingOrder = 450;

    [Header("Screen (leave Root empty to build everything at runtime)")]
    [Tooltip("Everything of the memo, scrim included; shown while it is open")]
    public RectTransform root;
    [Tooltip("The memo paper (fades in and settles when it opens)")]
    public RectTransform memo;

    [Header("Texts")]
    [Tooltip("Letterhead: the company name (written in capitals)")]
    public TMP_Text letterheadText;
    [Tooltip("\"Nova Holdings is now a Corporation\"")]
    public TMP_Text headlineText;
    [Tooltip("\"You passed $10,000,000\"; the amount is wrapped in <link=\"hl\"> for a TextHighlighter")]
    public TMP_Text passedText;
    [Tooltip("\"New: company letterhead · brass clips\" (hidden when the tier has no New line)")]
    public TMP_Text whatsNewText;

    [Header("Before / after")]
    public StatusLedgerCorner beforePicture;
    public StatusLedgerCorner afterPicture;
    [Tooltip("\"Before · Company ($1M)\"")]
    public TMP_Text beforeCaption;
    [Tooltip("\"After · Corporation ($10M)\"")]
    public TMP_Text afterCaption;

    [Header("Next tier")]
    [Tooltip("\"Next: Executive at $50,000,000\", or the top-of-the-ladder line")]
    public TMP_Text nextText;
    [Tooltip("The progress bar with its percent and amount; hidden at the top tier")]
    public GameObject progressGroup;
    [Tooltip("Fill of the progress bar: its right anchor is set to the progress (0-1)")]
    public RectTransform progressFill;
    [Tooltip("\"20%\"")]
    public TMP_Text progressPercentText;
    [Tooltip("\"$10,012,400 of $50,000,000\"")]
    public TMP_Text progressValueText;

    [Header("Buttons and stamp")]
    public Button niceButton;
    public Button closeButton;
    [Tooltip("The PROMOTED stamp (comes down onto the memo after it opens)")]
    public RectTransform stamp;

    /// <summary>True while the memo is on screen.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>Raised when the memo closes.</summary>
    public event Action Closed;

    Canvas runtimeCanvas;
    CanvasGroup memoGroup;
    CanvasGroup stampGroup;
    Vector3 memoScale = Vector3.one;
    Vector3 stampScale = Vector3.one;
    float stampAlpha = 1f;
    float openAge;
    bool animating;
    bool hooked;
    bool fontsFollowLanguage;
    int shownFrom = -1;
    int shownTo = -1;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[PromotionMemoView] Removed a second PromotionMemoView on '" + name + "'.");
            Destroy(this);
            return;
        }
        Instance = this;
    }

    void OnEnable()
    {
        L10n.LanguageChanged += OnLanguageChanged;
    }

    void OnDisable()
    {
        L10n.LanguageChanged -= OnLanguageChanged;
        // Leaving the scene while open: give the game its time back
        ReleasePanel();
    }

    // The fonts switch by themselves (L10nFont); the texts are filled again in the new language
    void OnLanguageChanged()
    {
        if (IsOpen && shownTo >= 0) Fill(shownFrom, shownTo);
    }

    void OnDestroy()
    {
        ReleasePanel();
        if (Instance != this) return;
        Instance = null;
        if (runtimeCanvas != null)
            Destroy(runtimeCanvas.gameObject);
    }

    /// <summary>Build the memo at runtime if no designed one is assigned, and connect the buttons.</summary>
    public void EnsureBuilt()
    {
        if (root == null)
            runtimeCanvas = PromotionMemoFactory.Build(this, font != null ? font : FindDesignFont());

        if (memo != null && memoGroup == null)
        {
            memoGroup = GetOrAdd<CanvasGroup>(memo.gameObject);
            memoScale = memo.localScale;
        }
        if (stamp != null && stampGroup == null)
        {
            stampGroup = GetOrAdd<CanvasGroup>(stamp.gameObject);
            stampGroup.blocksRaycasts = false;
            stampAlpha = stampGroup.alpha;
            stampScale = stamp.localScale;
        }
        if (root != null && !fontsFollowLanguage)
        {
            // Every text of the memo (built here or designed) takes the language's font
            L10nFont.AttachAll(root);
            fontsFollowLanguage = true;
        }
        HookButtons();
    }

    void HookButtons()
    {
        if (hooked) return;
        hooked = true;
        if (niceButton != null) niceButton.onClick.AddListener(Close);
        if (closeButton != null) closeButton.onClick.AddListener(Close);
    }

    /// <summary>Open the memo for a promotion from <paramref name="fromTier"/> to <paramref name="toTier"/> (pauses the game).</summary>
    public void Show(int fromTier, int toTier)
    {
        EnsureBuilt();
        if (root == null) return;

        int top = StatusLadder.TierCount - 1;
        toTier = Mathf.Clamp(toTier, 0, top);
        fromTier = Mathf.Clamp(fromTier, 0, toTier);
        Fill(fromTier, toTier);

        root.gameObject.SetActive(true);
        UIManager.BringToFront(root.gameObject);
        IsOpen = true;
        UIManager.Instance.OpenPanel(this, true, Close);
        GameSfx.PlayPair(Sfx.UiBong, Sfx.UiScratch, StampDelay + StampSeconds);   // the PROMOTED stamp lands after the memo opens

        openAge = 0f;
        animating = true;
        Animate();
    }

    /// <summary>Close the memo and let the game run again (Nice, the X and Escape call this).</summary>
    public void Close()
    {
        if (!IsOpen) return;
        if (root != null) root.gameObject.SetActive(false);
        ReleasePanel();
        Closed?.Invoke();
    }

    void ReleasePanel()
    {
        if (!IsOpen) return;
        IsOpen = false;
        animating = false;
        if (UIManager.Exists) UIManager.Instance.ClosePanel(this);
    }

    void Update()
    {
        if (!IsOpen || !animating) return;
        openAge += Time.unscaledDeltaTime; // the game is paused
        Animate();
    }

    void Animate()
    {
        float t = Mathf.Clamp01(openAge / OpenSeconds);
        float ease = 1f - (1f - t) * (1f - t) * (1f - t);
        if (memo != null) memo.localScale = memoScale * Mathf.Lerp(0.94f, 1f, ease);
        if (memoGroup != null) memoGroup.alpha = ease;

        // The stamp speeds up as it comes down onto the paper
        float s = Mathf.Clamp01((openAge - StampDelay) / StampSeconds);
        if (stamp != null) stamp.localScale = stampScale * Mathf.Lerp(1.6f, 1f, s * s);
        if (stampGroup != null) stampGroup.alpha = stampAlpha * Mathf.Clamp01(s * 2f);

        if (t >= 1f && s >= 1f) animating = false;
    }

    // ------------------------------------------------------------------
    // Content
    // ------------------------------------------------------------------

    void Fill(int fromTier, int toTier)
    {
        const string T = StatusLadder.Table;
        shownFrom = fromTier;
        shownTo = toTier;
        string company = CompanyNameUI.GetDisplayName();   // the default name in the player's language
        double value = CompanyManager.Instance != null ? CompanyManager.Instance.companyValue : 0d;

        // The player's company name in capitals, by the language's rules (Turkish i -> İ)
        SetText(letterheadText, L10n.ToUpper(company));
        SetText(headlineText, Headline(company, toTier));
        string passed = "<link=\"" + TextHighlighter.LinkId + "\">" + UIFormat.Money(StatusLadder.GetTierThreshold(toTier)) + "</link>";
        SetText(passedText, L10n.Get(T, "status.memo.passed", ("amount", passed)));

        string whatsNew = StatusLadder.GetTierWhatsNew(toTier);
        if (whatsNewText != null)
        {
            bool any = !string.IsNullOrWhiteSpace(whatsNew);
            whatsNewText.gameObject.SetActive(any);
            if (any) whatsNewText.text = L10n.Get(T, "status.memo.new", ("label", Bold(L10n.Get(T, "status.memo.new.label"))), ("items", whatsNew));
        }

        if (beforePicture != null) beforePicture.Show(fromTier, company, value);
        if (afterPicture != null) afterPicture.Show(toTier, company, value);
        SetText(beforeCaption, Caption("status.memo.caption.before.label", fromTier));
        SetText(afterCaption, Caption("status.memo.caption.after.label", toTier));

        int next = toTier + 1;
        bool hasNext = next < StatusLadder.TierCount;
        if (progressGroup != null) progressGroup.SetActive(hasNext);
        string nextLabel = Bold(L10n.Get(T, "status.memo.next.label"));
        if (!hasNext)
        {
            SetText(nextText, L10n.Get(T, "status.memo.next.top", ("label", nextLabel)));
            return;
        }

        double threshold = StatusLadder.GetTierThreshold(next);
        float progress = threshold > 0d ? Mathf.Clamp01((float)(value / threshold)) : 1f;
        SetText(nextText, L10n.Get(T, "status.memo.next", ("label", nextLabel), ("tier", StatusLadder.GetTierName(next)), ("amount", UIFormat.Money(threshold))));
        // Rounded down: 100% only once it's reached
        string percent = Mathf.FloorToInt(progress * 100f).ToString(CultureInfo.InvariantCulture);
        SetText(progressPercentText, L10n.Get(T, "status.memo.progress.percent", ("percent", percent)));
        SetText(progressValueText, L10n.Get(T, "status.memo.progress.amount", ("value", UIFormat.Money(value)), ("target", UIFormat.Money(threshold))));
        if (progressFill != null)
        {
            Vector2 max = progressFill.anchorMax;
            max.x = progress;
            progressFill.anchorMax = max;
        }
    }

    static void SetText(TMP_Text text, string value)
    {
        if (text != null) text.text = value;
    }

    /// <summary>"Nova Holdings is now a Corporation": one sentence per built-in tier (each language words it its own way).</summary>
    static string Headline(string company, int tier)
    {
        string id = StatusLadder.GetTierId(tier);
        if (id != null) return L10n.Get(StatusLadder.Table, "status.memo.headline." + id, ("company", company));
        string tierName = StatusLadder.GetTierName(tier);
        return L10n.Get(StatusLadder.Table, "status.memo.headline.custom", ("company", company), ("article", Article(tierName)), ("tier", tierName));
    }

    /// <summary>"Before · Company ($1M)" with the first word in bold ink.</summary>
    static string Caption(string labelKey, int tier)
    {
        const string T = StatusLadder.Table;
        string label = "<color=#1D2433>" + Bold(L10n.Get(T, labelKey)) + "</color>";
        string name = StatusLadder.GetTierName(tier);
        double threshold = StatusLadder.GetTierThreshold(tier);
        return threshold > 0d
            ? L10n.Get(T, "status.memo.caption", ("label", label), ("tier", name), ("amount", ShortMoney(threshold)))
            : L10n.Get(T, "status.memo.caption.noamount", ("label", label), ("tier", name));
    }

    static string Bold(string text)
    {
        return "<b>" + text + "</b>";
    }

    static string Article(string word)
    {
        return !string.IsNullOrEmpty(word) && "AEIOUaeiou".IndexOf(word[0]) >= 0 ? "an" : "a";
    }

    /// <summary>"$1M", "$10M", "$1B", "$2.5M".</summary>
    static string ShortMoney(double value)
    {
        CultureInfo inv = CultureInfo.InvariantCulture;
        if (value >= 1e9) return "$" + (value / 1e9).ToString("0.##", inv) + "B";
        if (value >= 1e6) return "$" + (value / 1e6).ToString("0.##", inv) + "M";
        if (value >= 1e3) return "$" + (value / 1e3).ToString("0.##", inv) + "K";
        return UIFormat.Money(value);
    }

    /// <summary>The design's font, borrowed from the scene's UI (the HUD uses it); null if nothing uses it.</summary>
    static TMP_FontAsset FindDesignFont()
    {
        foreach (TMP_Text text in FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            TMP_FontAsset asset = text != null ? text.font : null;
            if (asset != null && asset.faceInfo.familyName == DesignFontFamily)
                return asset;
        }
        return null;
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
