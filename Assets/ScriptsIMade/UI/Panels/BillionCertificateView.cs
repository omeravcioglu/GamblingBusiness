using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The $1B certificate (board "$1B: online world unlocked"): "Certificate of Obscene Wealth" over the paused office -
/// gold double frame, the company's name, "has reached $1,000,000,000 in company value.", what the online cities offer, a warning,
/// Pick a city / Later and a close button, a gold $1B seal, the Board's signature and confetti that falls into place.
/// Built by EndingsBuilder (RunEndScreens.prefab in MainGame). Whoever unlocks the online cities shows it with
/// <see cref="TryShow"/>, which returns false when no designed certificate is loaded (the caller then shows its own).
/// Pausing; Escape = Later. The company, the value line and the city lines come from the Menu table (endings.cert.*) and
/// are written again when the language changes; the builder binds the fixed labels.
/// </summary>
[DisallowMultipleComponent]
public class BillionCertificateView : MonoBehaviour
{
    const string Table = "Menu";
    static readonly string[] LineKeys = { "endings.cert.line.cities", "endings.cert.line.plots", "endings.cert.line.rent" };

    public static BillionCertificateView Instance { get; private set; }

    [Header("Certificate (built by EndingsBuilder)")]
    public RectTransform root;
    [Tooltip("\"NOVA HOLDINGS\"")]
    public TMP_Text companyText;
    [Tooltip("\"has reached <link=\"hl\">$1,000,000,000</link> in company value.\"")]
    public TMP_Text reachedText;
    [Tooltip("The three lines about the online cities")]
    public TMP_Text[] lineTexts = new TMP_Text[3];
    public TMP_Text warningText;
    public Button enterButton;
    public Button laterButton;
    public Button closeButton;

    [Header("Confetti")]
    public RectTransform[] confetti = new RectTransform[0];
    [Tooltip("Real seconds the confetti takes to fall into place")]
    public float confettiFallSeconds = 1.1f;

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    Action onEnter, onLater;
    bool hooked;
    string shownCompany;
    double shownValue;
    string[] shownLines;
    Vector2[] confettiHome;
    float[] confettiAngle, confettiDrop, confettiSpin, confettiDelay;
    float confettiClock;
    bool confettiFalling;

    public bool IsOpen { get; private set; }

    /// <summary>
    /// Show the certificate if a designed one is in the scene. enterCity / later run after it closes (Escape and the
    /// close button count as Later). lines (optional) replace the three lines about the cities.
    /// </summary>
    public static bool TryShow(string companyName, double value, Action enterCity, Action later, string[] lines = null)
    {
        if (Instance == null || Instance.root == null) return false;
        Instance.Show(companyName, value, enterCity, later, lines);
        return true;
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[BillionCertificateView] A second certificate on '" + name + "' is ignored.");
            return;
        }
        Instance = this;
        Hook();
        if (root != null) L10nFont.AttachAll(root);   // every text on the language's font
    }

    void OnEnable()
    {
        L10n.LanguageChanged += OnLanguageChanged;
    }

    void OnLanguageChanged()
    {
        if (IsOpen) Fill();
    }

    void OnDisable()
    {
        L10n.LanguageChanged -= OnLanguageChanged;
        if (IsOpen)
        {
            IsOpen = false;
            if (UIManager.Exists) UIManager.Instance.ClosePanel(this);
        }
    }

    void OnDestroy()
    {
        if (UIManager.Exists) UIManager.Instance.ClosePanel(this);
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        if (!IsOpen) return;
        // UI state was reset (e.g. by a debug tool) while the certificate is up: keep the game paused
        if (UIManager.Exists && !UIManager.Instance.IsPanelOpen(this))
            UIManager.Instance.OpenPanel(this, true, OnLater);
        if (confettiFalling) AnimateConfetti(Time.unscaledDeltaTime);
    }

    void Hook()
    {
        if (hooked) return;
        hooked = true;
        if (enterButton != null) enterButton.onClick.AddListener(OnEnter);
        if (laterButton != null) laterButton.onClick.AddListener(OnLater);
        if (closeButton != null) closeButton.onClick.AddListener(OnLater);
    }

    public void Show(string companyName, double value, Action enterCity, Action later, string[] lines = null)
    {
        Hook();
        onEnter = enterCity;
        onLater = later;
        shownCompany = companyName;
        shownValue = value;
        shownLines = lines;
        Fill();

        root.gameObject.SetActive(true);
        UIManager.BringToFront(gameObject);
        IsOpen = true;
        StartConfetti();
        var es = UnityEngine.EventSystems.EventSystem.current;
        if (es != null && !es.alreadySelecting) es.SetSelectedGameObject(null);
        if (UIManager.Instance != null) UIManager.Instance.OpenPanel(this, true, OnLater);
    }

    /// <summary>The company, the value line and the city lines (a caller's own lines, else the table's) in the current language.</summary>
    void Fill()
    {
        // The player's name in capitals by the language's rules (Turkish i -> İ); the default name in the player's language
        string company = string.IsNullOrWhiteSpace(shownCompany)
            ? L10n.Get(Table, "endings.cert.company.fallback")
            : L10n.ToUpper(CompanyNameUI.DisplayName(shownCompany));
        if (companyText != null) companyText.text = company;
        if (reachedText != null) reachedText.text = ReachedLine(shownValue);
        if (lineTexts == null) return;
        for (int i = 0; i < lineTexts.Length; i++)
        {
            if (lineTexts[i] == null) continue;
            string custom = shownLines != null && i < shownLines.Length ? shownLines[i] : null;
            if (!string.IsNullOrEmpty(custom)) lineTexts[i].text = custom;
            else if (i < LineKeys.Length) lineTexts[i].text = L10n.Get(Table, LineKeys[i]);
        }
    }

    /// <summary>"has reached $1,000,000,000 in company value." with the amount under the highlighter.</summary>
    public static string ReachedLine(double value)
    {
        return L10n.Get(Table, "endings.cert.reached",
                        ("amount", "<link=\"" + TextHighlighter.LinkId + "\"><space=6>" + UIFormat.Money(value) + "<space=6></link>"));
    }

    /// <summary>Hide it without calling back.</summary>
    public void Close()
    {
        confettiFalling = false;
        if (root != null) root.gameObject.SetActive(false);
        onEnter = onLater = null;
        if (!IsOpen) return;
        IsOpen = false;
        if (UIManager.Exists) UIManager.Instance.ClosePanel(this);
    }

    void OnEnter()
    {
        Action after = onEnter;
        Close();
        Invoke(after);
    }

    void OnLater()
    {
        Action after = onLater;
        Close();
        Invoke(after);
    }

    static void Invoke(Action action)
    {
        if (action == null) return;
        try { action(); }
        catch (Exception e) { Debug.LogException(e); }
    }

    // ------------------------------------------------------------------
    // Confetti: every piece starts above its spot and falls into place, spinning

    void StartConfetti()
    {
        if (confetti == null || confetti.Length == 0) return;
        int n = confetti.Length;
        if (confettiHome == null || confettiHome.Length != n)
        {
            confettiHome = new Vector2[n];
            confettiAngle = new float[n];
            confettiDrop = new float[n];
            confettiSpin = new float[n];
            confettiDelay = new float[n];
            for (int i = 0; i < n; i++)
            {
                if (confetti[i] == null) continue;
                confettiHome[i] = confetti[i].anchoredPosition;
                confettiAngle[i] = confetti[i].localEulerAngles.z;
            }
        }
        for (int i = 0; i < n; i++)
        {
            confettiDrop[i] = UnityEngine.Random.Range(260f, 720f);
            confettiSpin[i] = UnityEngine.Random.Range(-540f, 540f);
            confettiDelay[i] = UnityEngine.Random.Range(0f, 0.35f);
        }
        confettiClock = 0f;
        confettiFalling = true;
        AnimateConfetti(0f);
    }

    void AnimateConfetti(float dt)
    {
        confettiClock += dt;
        float duration = Mathf.Max(0.1f, confettiFallSeconds);
        bool done = true;
        for (int i = 0; i < confetti.Length; i++)
        {
            RectTransform piece = confetti[i];
            if (piece == null) continue;
            float p = Mathf.Clamp01((confettiClock - confettiDelay[i]) / duration);
            if (p < 1f) done = false;
            float eased = 1f - (1f - p) * (1f - p) * (1f - p);
            piece.anchoredPosition = confettiHome[i] + new Vector2(0f, (1f - eased) * confettiDrop[i]);
            piece.localRotation = Quaternion.Euler(0f, 0f, confettiAngle[i] + (1f - eased) * confettiSpin[i]);
        }
        if (done) confettiFalling = false;
    }
}
