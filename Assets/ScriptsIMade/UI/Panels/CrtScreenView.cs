using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The office CRT every crisis mini-game plays on (boards "Crisis incoming", "CRT frame" and "Crisis result"),
/// built by CrisisGamesBuilder on each mini-game panel. MiniGameBase drives it when the panel has one:
/// the warning card with its countdown, then the monitor (timer segments, title, crisis sticky, stakes, controls,
/// Give up) around the game's own GameArea, then the result on the glass with a stamp and Back to work.
/// Games report their progress through MiniGameBase (ShowProgress / ShowStat / ShowLives), drawn in the rows at the top
/// of the glass.
/// Texts (l10n): what this view writes comes from the Crisis table (CrisisText) in the current language, in its font
/// (L10nFont on every text); the builder binds the fixed labels itself (L10nText).
/// </summary>
[DisallowMultipleComponent]
public class CrtScreenView : MonoBehaviour
{
    public static readonly Color Green = new Color32(0x7E, 0xD6, 0x8E, 0xFF);
    public static readonly Color Red = new Color32(0xF0, 0x8A, 0x84, 0xFF);
    public static readonly Color Yellow = new Color32(0xFF, 0xE1, 0x4D, 0xFF);
    public static readonly Color Blue = new Color32(0xBF, 0xE3, 0xF5, 0xFF);
    public static readonly Color Pink = new Color32(0xFF, 0xC7, 0xD1, 0xFF);
    public static readonly Color Paper = new Color32(0xFF, 0xFD, 0xF6, 0xFF);
    public static readonly Color Ink = new Color32(0x1D, 0x24, 0x33, 0xFF);
    public static readonly Color Black = new Color32(0x0B, 0x0B, 0x0C, 0xFF);

    /// <summary>A light colour on the dark glass at the alpha that looks like CSS rgba(c, cssAlpha).</summary>
    public static Color Lit(Color c, float cssAlpha) => UIColors.Over(c, cssAlpha, Ink);

    [Header("Phases")]
    public GameObject warning;          // scrim vignette, "Game paused" chip and the card
    public GameObject crt;              // the monitor
    public GameObject gameArea;
    public GameObject resultLayer;      // texts on the glass
    public GameObject resultStamp;
    public GameObject playChin;         // controls sticky + Give up
    public GameObject resultChin;       // Back to work + closes-in
    public GameObject playGlass;        // timer + title + progress rows
    public GameObject glassFx;          // scanlines + flash over the glass

    [Header("Warning card")]
    public TMP_Text warningCrisis;
    public TMP_Text warningGame;
    [Tooltip("The strip's divider, keys and controls: moved to start after the game's name (its width depends on the language)")]
    public RectTransform warningStripTail;
    public TMP_Text warningReward;
    public TMP_Text warningPenalty;
    public UIShape countdownRing;
    public TMP_Text countdownNumber;
    public Button startNowButton;
    [Tooltip("Seconds the warning card counts down before the game starts")]
    public float countdownSeconds = 3f;
    [Tooltip("Seconds between the monitor appearing and input counting")]
    public float readySeconds = 0.6f;

    [Header("Monitor")]
    public UIShape[] segments;
    public UIShape[] segmentGlows;
    public TMP_Text timeText;
    public TMP_Text title;
    public TMP_Text crisisText;
    public TMP_Text rewardText;
    public TMP_Text penaltyText;
    public Button giveUpButton;
    public TMP_Text giveUpLabel;
    public Button backButton;
    public TMP_Text closesText;
    [Tooltip("Seconds the result stays up (Back to work closes it sooner)")]
    public float resultSeconds = 3f;

    [Header("Progress rows")]
    public RectTransform progressRow;
    public TMP_Text progressLabel;
    public RectTransform pipsBox;
    public TMP_Text progressCount;
    public RectTransform statRow;
    public TMP_Text statLabel;
    public TMP_Text statValue;
    public RectTransform livesBox;
    public Sprite heartFill;
    public Sprite heartLine;

    [Header("Result")]
    public TMP_Text resultTag;
    public TMP_Text resultHeadline;
    public TMP_Text resultLine;
    public TMP_Text resultAmount;
    public TMP_Text resultMaths;
    public TMP_Text resultStampText;
    public Graphic[] resultStampInk;
    public Material glowGreen;
    public Material glowRed;
    public Material glowPink;
    public Material glowYellow;

    [Header("Game (set by the builder)")]
    public string gameTitle = "Mini-game";

    /// <summary>Start now / Give up / Back to work, wired by MiniGameBase.</summary>
    public event Action StartNowClicked;
    public event Action GiveUpClicked;
    public event Action BackClicked;

    readonly List<UIShape[]> pips = new List<UIShape[]>();   // fill, ring, glow
    readonly List<Image[]> hearts = new List<Image[]>();
    int shownPips = -1;
    string shownTime;
    int shownCountdown = -1;
    int shownClosesIn = -1;
    bool wired;
    string statLabelEnglish;                // the stat's label as built, shown in the current language by ShowPlay
    float warningCrisisPitch, crisisPitch;  // the crisis lines' designed line pitch (em)

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    void Awake()
    {
        Wire();
        // taken as built (English, design font) before the language's font goes on the texts
        if (statLabel != null) statLabelEnglish = statLabel.text;
        warningCrisisPitch = LinePitch(warningCrisis);
        crisisPitch = LinePitch(crisisText);
        L10nFont.AttachAll(this);   // localization: each language's font (English keeps the design font and material)
    }

    void Wire()
    {
        if (wired) return;
        wired = true;
        if (startNowButton != null) startNowButton.onClick.AddListener(() => StartNowClicked?.Invoke());
        if (giveUpButton != null) giveUpButton.onClick.AddListener(() => GiveUpClicked?.Invoke());
        if (backButton != null) backButton.onClick.AddListener(() => BackClicked?.Invoke());
    }

    // ------------------------------------------------------------------
    // Phases

    /// <summary>The "Crisis incoming" card over the paused office.</summary>
    public void ShowWarning(string crisis, float reward, float penalty)
    {
        Wire();
        SetActive(warning, true);
        SetActive(crt, false);
        SetActive(glassFx, false);
        SetActive(gameArea, false);
        SetActive(resultLayer, false);
        SetActive(resultStamp, false);
        SetText(warningCrisis, crisis);
        KeepLinePitch(warningCrisis, warningCrisisPitch);
        // "Mini-game: Stop the Bar": the label grey, the game's name bold, in the current language
        SetText(warningGame, CrisisText.Get("crisis.screen.warning.game_line",
            ("label", "<color=#474F60>" + CrisisText.Get("crisis.screen.warning.game_label") + "</color>"),
            ("game", "<b>" + CrisisText.GameName(gameTitle) + "</b>")));
        LayoutWarningStrip();
        SetText(warningReward, CrisisText.Get("crisis.screen.warning.reward", ("amount", UIFormat.SignedMoney(reward))));
        SetText(warningPenalty, CrisisText.Get("crisis.screen.warning.penalty", ("amount", UIFormat.SignedMoney(penalty))));
        shownCountdown = -1;
        SetCountdown(countdownSeconds, countdownSeconds);
    }

    public void SetCountdown(float secondsLeft, float total)
    {
        if (countdownRing != null)
            countdownRing.SetSector(0f, 360f * Mathf.Clamp01(total > 0f ? secondsLeft / total : 0f));
        int n = Mathf.Max(1, Mathf.CeilToInt(secondsLeft - 0.001f));
        if (n != shownCountdown && countdownNumber != null)
        {
            shownCountdown = n;
            countdownNumber.text = n.ToString(Inv);
        }
    }

    /// <summary>The monitor with the game on its glass.</summary>
    public void ShowPlay(string crisis, float reward, float penalty)
    {
        Wire();
        SetActive(warning, false);
        SetActive(crt, true);
        SetActive(glassFx, true);
        SetActive(gameArea, true);
        SetActive(playGlass, true);
        SetActive(playChin, true);
        SetActive(resultChin, false);
        SetActive(resultLayer, false);
        SetActive(resultStamp, false);
        SetText(title, CrisisText.GameName(gameTitle));   // l10n (crisis batch): the game's name in the current language
        SetText(crisisText, crisis);
        KeepLinePitch(crisisText, crisisPitch);
        SetText(rewardText, UIFormat.SignedMoney(reward));
        SetText(penaltyText, UIFormat.SignedMoney(penalty));
        SetText(giveUpLabel, CrisisText.Get("crisis.screen.give_up", ("amount", UIFormat.SignedMoney(penalty))));
        ShowStatLabel();
        shownTime = null;
    }

    /// <summary>30 segments for the time left of the limit (games without a limit show their 60 s safety cap).</summary>
    public void SetTime(float remaining, float limit)
    {
        remaining = Mathf.Max(0f, remaining);
        int lit = limit > 0f ? Mathf.Clamp(Mathf.CeilToInt(remaining / limit * 30f - 0.001f), 0, 30) : 0;
        Color tone = lit <= 5 ? Red : (lit <= 10 ? Yellow : Blue);
        Color off = Lit(Paper, 0.08f);
        if (segments != null)
        {
            for (int i = 0; i < segments.Length; i++)
            {
                bool on = i < lit;
                if (segments[i] != null) segments[i].color = on ? tone : off;
                if (segmentGlows != null && i < segmentGlows.Length && segmentGlows[i] != null)
                {
                    segmentGlows[i].enabled = on;
                    segmentGlows[i].color = Lit(tone, lit <= 5 ? 0.75f : (lit <= 10 ? 0.7f : 0.6f));
                }
            }
        }
        int secs = Mathf.CeilToInt(remaining - 0.001f);
        string t = (secs / 60).ToString(Inv) + ":" + (secs % 60).ToString("00", Inv);
        if (timeText != null && t != shownTime)
        {
            shownTime = t;
            timeText.text = t;
        }
        if (timeText != null) timeText.color = tone;
    }

    /// <summary>The result on the glass, the stamp and Back to work. tag: SOLVED, or why it failed (TIME'S UP, GAVE UP...).</summary>
    public void ShowResult(bool success, string line, float amount, string maths, string tag)
    {
        Wire();
        SetActive(warning, false);
        SetActive(crt, true);
        SetActive(glassFx, true);
        SetActive(gameArea, false);
        SetActive(playGlass, false);
        SetActive(playChin, false);
        SetActive(resultChin, true);
        SetActive(resultLayer, true);
        SetActive(resultStamp, true);
        Color c = success ? Green : Red;
        Color ink = success ? UIColors.Money : UIColors.Loss;
        // the tag arrives in its final case (crisis.result.*.tag); the game's name is upper-cased by the language's rules
        if (string.IsNullOrEmpty(tag)) tag = CrisisText.Get(success ? "crisis.result.success.tag" : "crisis.result.failed.tag");
        SetText(resultTag, CrisisText.Get("crisis.screen.result.tag", ("game", L10n.ToUpper(CrisisText.GameName(gameTitle ?? ""))), ("tag", tag)));
        if (resultHeadline != null)
        {
            resultHeadline.text = CrisisText.Get(success ? "crisis.screen.result.headline.solved" : "crisis.screen.result.headline.failed");
            resultHeadline.color = c;
            SetGlow(resultHeadline, success ? glowGreen : glowRed);
        }
        SetText(resultLine, line);
        if (resultAmount != null)
        {
            resultAmount.text = UIFormat.SignedMoney(amount);
            resultAmount.color = c;
            SetGlow(resultAmount, success ? glowGreen : glowRed);
        }
        SetText(resultMaths, maths);
        SetText(resultStampText, CrisisText.Get(success ? "crisis.screen.result.stamp.solved" : "crisis.screen.result.stamp.failed"));
        shownClosesIn = -1;
        if (resultStampInk != null)
            foreach (var g in resultStampInk)
                if (g != null) g.color = ink;
    }

    public void SetClosesIn(float seconds)
    {
        int n = Mathf.Max(1, Mathf.CeilToInt(seconds - 0.001f));
        if (closesText == null || n == shownClosesIn) return;
        shownClosesIn = n;
        closesText.text = CrisisText.Get("crisis.screen.closes_in", ("seconds", n.ToString(Inv)));
    }

    // ------------------------------------------------------------------
    // Progress rows

    /// <summary>Done / needed as pips (15 or fewer) and "done / needed". current &gt;= 0 marks that pip as in play.</summary>
    public void SetProgress(int done, int needed, int current = -1, bool blue = false)
    {
        if (progressRow == null) return;
        progressRow.gameObject.SetActive(true);
        needed = Mathf.Max(0, needed);
        done = Mathf.Clamp(done, 0, Mathf.Max(needed, done));
        int count = needed <= 15 ? needed : 0;
        EnsurePips(count);
        for (int i = 0; i < pips.Count; i++)
        {
            var p = pips[i];
            bool isDone = i < done, isCurrent = i == current && !isDone;
            p[0].enabled = isDone || isCurrent;
            Color on = blue ? Blue : Green;
            p[0].color = isDone ? on : Lit(Yellow, 0.25f);
            p[1].enabled = !isDone;
            p[1].color = isCurrent ? Yellow : Lit(on, blue ? 0.5f : 0.55f);
            p[2].enabled = isDone || isCurrent;
            p[2].color = isDone ? Lit(on, 0.7f) : Lit(Yellow, 0.6f);
        }
        // rounds and levels count the one in play ("4 / 5" while the 4th runs), everything else what is done
        int shown = current >= done ? current + 1 : done;
        SetText(progressCount, shown.ToString(Inv) + " / " + needed.ToString(Inv));
        LayoutProgress();
    }

    /// <summary>The stat at the right of the progress row: pink, or yellow for a countdown (HIDES IN).</summary>
    public void SetStat(string value, string label = null, bool yellow = false)
    {
        if (statRow == null) return;
        statRow.gameObject.SetActive(true);
        if (label != null && statLabel != null) statLabel.text = label;
        if (statValue != null)
        {
            statValue.gameObject.SetActive(true);
            statValue.text = value;
            statValue.color = yellow ? Yellow : Pink;
            SetGlow(statValue, yellow ? glowYellow : glowPink);
        }
        if (livesBox != null) livesBox.gameObject.SetActive(false);
        LayoutStat();
    }

    public void SetLives(int left, int max)
    {
        if (statRow == null || livesBox == null) return;
        statRow.gameObject.SetActive(true);
        if (statValue != null) statValue.gameObject.SetActive(false);
        livesBox.gameObject.SetActive(true);
        while (hearts.Count < max)
        {
            var rt = NewRect(livesBox, "Heart" + hearts.Count);
            rt.sizeDelta = new Vector2(44f, 44f);
            var glowImg = AddHeart(rt, "Glow", heartFill, Lit(Red, 0.35f), 1.3f);
            var fill = AddHeart(rt, "Fill", heartFill, Red, 1f);
            var line = AddHeart(rt, "Line", heartLine, Lit(Red, 0.55f), 1f);
            hearts.Add(new[] { glowImg, fill, line });
        }
        for (int i = 0; i < hearts.Count; i++)
        {
            bool shown = i < max, full = i < left;
            hearts[i][0].transform.parent.gameObject.SetActive(shown);
            hearts[i][0].enabled = full;
            hearts[i][1].enabled = full;
            hearts[i][2].enabled = !full;
        }
        LayoutStat();
    }

    void EnsurePips(int count)
    {
        if (count == shownPips) return;
        shownPips = count;
        float size = count <= 5 ? 44f : (count <= 10 ? 34f : (count <= 12 ? 30f : 24f));
        float radius = count <= 5 ? 8f : (count <= 10 ? 7f : (count <= 12 ? 6f : 5f));
        while (pips.Count < count)
        {
            var rt = NewRect(pipsBox, "Pip" + pips.Count);
            var glowShape = AddShape(rt, "Glow");
            glowShape.glow = 12f;
            var fill = AddShape(rt, "Fill");
            var ring = AddShape(rt, "Ring");
            ring.border = 3f;
            pips.Add(new[] { fill, ring, glowShape });
        }
        for (int i = 0; i < pips.Count; i++)
        {
            var rt = (RectTransform)pips[i][0].transform.parent;
            rt.gameObject.SetActive(i < count);
            rt.sizeDelta = new Vector2(size, size);
            foreach (var s in pips[i]) s.Round(radius);
        }
    }

    void LayoutProgress()
    {
        // label, 14, pips (gap by size), 14, count - left aligned, centred on the row
        float x = 0f;
        x = Place(progressLabel, x);
        float gap = shownPips <= 5 ? 10f : (shownPips <= 12 ? 8f : 6f);
        if (shownPips > 0)
        {
            x += 14f;
            float size = shownPips <= 5 ? 44f : (shownPips <= 10 ? 34f : (shownPips <= 12 ? 30f : 24f));
            for (int i = 0; i < shownPips; i++)
            {
                var rt = (RectTransform)pips[i][0].transform.parent;
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
                rt.pivot = new Vector2(0f, 0.5f);
                rt.anchoredPosition = new Vector2(x, 0f);
                x += size + (i < shownPips - 1 ? gap : 0f);
            }
        }
        x += 14f;
        Place(progressCount, x);
    }

    float Place(TMP_Text t, float x)
    {
        if (t == null) return x;
        var rt = t.rectTransform;
        float w = t.GetPreferredValues(t.text, 10000f, 1000f).x;
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
        rt.pivot = new Vector2(0f, 0.5f);
        rt.sizeDelta = new Vector2(w + 2f, rt.sizeDelta.y);
        rt.anchoredPosition = new Vector2(x, rt.anchoredPosition.y);
        return x + w;
    }

    void LayoutStat()
    {
        // right aligned: label, 14, value (or hearts)
        float right = statRow.rect.width;
        float x = right;
        if (livesBox != null && livesBox.gameObject.activeSelf)
        {
            int n = 0;
            foreach (var h in hearts) if (h[0].transform.parent.gameObject.activeSelf) n++;
            float w = n * 44f + Mathf.Max(0, n - 1) * 8f;
            x -= w;
            livesBox.anchorMin = livesBox.anchorMax = new Vector2(0f, 0.5f);
            livesBox.pivot = new Vector2(0f, 0.5f);
            livesBox.sizeDelta = new Vector2(w, 44f);
            livesBox.anchoredPosition = new Vector2(x, 0f);
            int k = 0;
            foreach (var h in hearts)
            {
                var rt = (RectTransform)h[0].transform.parent;
                if (!rt.gameObject.activeSelf) continue;
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
                rt.pivot = new Vector2(0f, 0.5f);
                rt.anchoredPosition = new Vector2(k * 52f, 0f);
                k++;
            }
        }
        else if (statValue != null)
        {
            float w = statValue.GetPreferredValues(statValue.text, 10000f, 1000f).x;
            x -= w;
            var rt = statValue.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(w + 2f, rt.sizeDelta.y);
            rt.anchoredPosition = new Vector2(x, rt.anchoredPosition.y);
        }
        if (statLabel != null)
        {
            float w = statLabel.GetPreferredValues(statLabel.text, 10000f, 1000f).x;
            x -= 14f + w;
            var rt = statLabel.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(w + 2f, rt.sizeDelta.y);
            rt.anchoredPosition = new Vector2(x, rt.anchoredPosition.y);
        }
    }

    // ------------------------------------------------------------------
    // Texts in every language

    /// <summary>The stat's label as built, in the current language (a game may still write its own: KeypadCode's HIDES IN / TRIES LEFT).</summary>
    void ShowStatLabel()
    {
        if (statLabel == null || statRow == null) return;
        if (statLabelEnglish == null) statLabelEnglish = statLabel.text;   // Edit-mode previews skip Awake
        if (statLabelEnglish.Length == 0) return;
        statLabel.text = CrisisText.GameText(gameTitle, "stat." + L10n.Slug(statLabelEnglish), statLabelEnglish);
        LayoutStat();
    }

    /// <summary>The warning strip's divider, keys and controls start after the game's name, as wide as the language makes it.</summary>
    void LayoutWarningStrip()
    {
        if (warningGame == null || warningStripTail == null) return;
        var game = warningGame.rectTransform;
        float left = game.anchoredPosition.x - game.rect.width * game.pivot.x;   // anchored to the strip's left edge
        float w = warningGame.GetPreferredValues(warningGame.text, 10000f, 1000f).x;
        warningStripTail.offsetMin = new Vector2(left + w + 16f, warningStripTail.offsetMin.y);
    }

    /// <summary>
    /// A glow material, only on a text still drawn with the font it was made for (the design font): another language's
    /// font keeps its own material, since a material samples its own font's atlas.
    /// </summary>
    static void SetGlow(TMP_Text t, Material m)
    {
        if (t == null || m == null || t.font == null || t.fontSharedMaterial == m) return;
        if (m.mainTexture == t.font.atlasTexture) t.fontSharedMaterial = m;
    }

    /// <summary>Line pitch (em) of a multi-line text: its design font's line height plus its line spacing.</summary>
    static float LinePitch(TMP_Text t)
    {
        if (t == null) return 0f;
        var localized = t.GetComponent<L10nFont>();
        float natural = FontLineHeight(localized != null && localized.designFont != null ? localized.designFont : t.font);
        return natural > 0f ? natural + t.lineSpacing * 0.01f : 0f;
    }

    /// <summary>Keeps the designed line pitch when another language's font, with its own line height, draws the text.</summary>
    static void KeepLinePitch(TMP_Text t, float pitch)
    {
        float natural = t != null ? FontLineHeight(t.font) : 0f;
        if (pitch <= 0f || natural <= 0f) return;
        float spacing = (pitch - natural) * 100f;
        if (!Mathf.Approximately(t.lineSpacing, spacing)) t.lineSpacing = spacing;
    }

    static float FontLineHeight(TMP_FontAsset font)
    {
        if (font == null || font.faceInfo.pointSize <= 0f) return 0f;
        return font.faceInfo.lineHeight / font.faceInfo.pointSize * font.faceInfo.scale;
    }

    // ------------------------------------------------------------------

    static RectTransform NewRect(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    static UIShape AddShape(RectTransform parent, string name)
    {
        var rt = NewRect(parent, name);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var s = rt.gameObject.AddComponent<UIShape>();
        s.raycastTarget = false;
        return s;
    }

    static Image AddHeart(RectTransform parent, string name, Sprite sprite, Color color, float scale)
    {
        var rt = NewRect(parent, name);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(44f * scale, 44f * scale);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;
        img.preserveAspect = true;
        return img;
    }

    static void SetActive(GameObject go, bool on)
    {
        if (go != null && go.activeSelf != on) go.SetActive(on);
    }

    static void SetText(TMP_Text t, string s)
    {
        if (t != null) t.text = s ?? "";
    }
}
