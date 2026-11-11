using System.Collections;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The volume dice on the designed main menu's card table (2D). Roll throws both dice onto the ROLL HERE strip;
/// the total sets the master volume like the old 3D dice: (total - 2) x 10%, never below minimumVolume, so luck
/// can't mute the game. A place row 2-12 is printed on the felt and the ON puck sits on the current value; the
/// Settings slider can still set any exact volume (the puck follows it).
/// </summary>
[DisallowMultipleComponent]
public class MenuDiceVolume2D : MonoBehaviour
{
    [System.Serializable]
    public class Die
    {
        public RectTransform root;
        [Tooltip("Pips in this order: top-left, top-right, middle-left, centre, middle-right, bottom-left, bottom-right")]
        public GameObject[] pips = new GameObject[7];
    }

    public Die die1 = new Die();
    public Die die2 = new Die();
    public Button rollButton;
    [Tooltip("Clicking the dice rolls too")]
    public Button diceButton;

    [Header("Place row 2-12")]
    [Tooltip("The 11 boxes' fill images, totals 2 to 12")]
    public Image[] boxes = new Image[11];
    public Color boxCurrent = new Color(1f, 0.882f, 0.302f, 0.22f);
    public RectTransform puck;

    [Header("Words")]
    public TMP_Text caption;
    public GameObject stamp;
    public TMP_Text stampText;
    public Graphic[] stampLines;
    public Vector2 loudStampPos;
    public Vector2 quietStampPos;

    [Header("Volume")]
    [Range(0f, 1f)] public float minimumVolume = 0.1f;

    [Header("Roll")]
    public float rollTime = 1.0f;
    public float jump = 46f;

    [Header("Audio (optional)")]
    public AudioSource audioSource;
    public AudioClip rollClip;
    public AudioClip hitClip;

    static readonly bool[][] Faces =
    {
        null,
        new[] { false, false, false, true, false, false, false },
        new[] { true, false, false, false, false, false, true },
        new[] { true, false, false, true, false, false, true },
        new[] { true, true, false, false, false, true, true },
        new[] { true, true, false, true, false, true, true },
        new[] { true, true, true, false, true, true, true },
    };

    static readonly Color LoudInk = new Color32(0xC0, 0x35, 0x2B, 0xFF);
    static readonly Color QuietInk = new Color32(0x2B, 0x5B, 0xA8, 0xFF);

    bool rolling;
    int shownTotal = -1;
    Vector2 rest1, rest2;
    float restRot1, restRot2;
    bool restKnown;
    Coroutine puckMove;

    public bool IsRolling => rolling;

    void Awake()
    {
        RememberRest();
        if (rollButton != null) rollButton.onClick.AddListener(Roll);
        if (diceButton != null) diceButton.onClick.AddListener(Roll);
    }

    void OnEnable()
    {
        GameSettings.Changed += OnSettingsChanged;
        L10n.LanguageChanged += OnSettingsChanged;   // the caption and stamp in the new language
        ShowSaved();
    }

    void OnDisable()
    {
        GameSettings.Changed -= OnSettingsChanged;
        L10n.LanguageChanged -= OnSettingsChanged;
        if (rolling)
        {
            StopAllCoroutines();
            rolling = false;
            SetRollButton(true);
            ShowSaved();
        }
    }

    void RememberRest()
    {
        if (restKnown) return;
        if (die1.root != null) { rest1 = die1.root.anchoredPosition; restRot1 = die1.root.localEulerAngles.z; }
        if (die2.root != null) { rest2 = die2.root.anchoredPosition; restRot2 = die2.root.localEulerAngles.z; }
        restKnown = true;
    }

    void OnSettingsChanged()
    {
        if (!rolling) ShowSaved();
    }

    public static float VolumeForTotal(int total, float floor) => Mathf.Max(Mathf.Clamp01(floor), Mathf.Clamp01((total - 2) / 10f));

    public static int TotalForVolume(float volume) => Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(volume) * 10f) + 2, 2, 12);

    /// <summary>Show the saved master volume: dice that add up to it, the puck on it.</summary>
    public void ShowSaved()
    {
        int total = TotalForVolume(GameSettings.MasterVolume);
        int a = Mathf.Clamp(total / 2, 1, 6), b = Mathf.Clamp(total - a, 1, 6);
        ShowDice(a, b);
        MovePuck(total, false);
        SetCaption(CompanyText.Get("menu.dice.saved", ("volume", Pct(GameSettings.MasterVolume)), ("floor", Pct(minimumVolume))));
        SetStamp(0);
    }

    public void Roll()
    {
        if (rolling || !isActiveAndEnabled) return;
        StartCoroutine(RollRoutine());
    }

    IEnumerator RollRoutine()
    {
        rolling = true;
        RememberRest();
        SetRollButton(false);
        SetStamp(0);
        SetCaption(CompanyText.Get("menu.dice.rolling"));
        if (puck != null) puck.gameObject.SetActive(false);
        Play(rollClip);

        int a = Random.Range(1, 7), b = Random.Range(1, 7);
        float spin1 = Random.Range(300f, 540f) * (Random.value < 0.5f ? -1f : 1f);
        float spin2 = Random.Range(300f, 540f) * (Random.value < 0.5f ? -1f : 1f);
        float end1 = restRot1 + Random.Range(-12f, 12f), end2 = restRot2 + Random.Range(-12f, 12f);
        float t = 0f, nextFace = 0f;
        while (t < rollTime)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / rollTime);
            float arc = 4f * k * (1f - k);
            float ease = 1f - (1f - k) * (1f - k);
            Pose(die1.root, rest1 + new Vector2(-10f * arc, jump * arc), Mathf.Lerp(restRot1, end1 + spin1, ease), 1f + 0.18f * arc);
            Pose(die2.root, rest2 + new Vector2(12f * arc, jump * 1.2f * arc), Mathf.Lerp(restRot2, end2 + spin2, ease), 1f + 0.18f * arc);
            if (t >= nextFace && k < 0.85f)
            {
                nextFace = t + 0.07f;
                ShowDice(Random.Range(1, 7), Random.Range(1, 7));
            }
            yield return null;
        }
        Pose(die1.root, rest1, end1, 1f);
        Pose(die2.root, rest2, end2, 1f);
        ShowDice(a, b);
        Play(hitClip);

        int total = a + b;
        float volume = VolumeForTotal(total, minimumVolume);
        rolling = true;   // keep OnSettingsChanged from resetting the dice while we save
        GameSettings.MasterVolume = volume;
        GameSettings.Save();
        MovePuck(total, true);
        SetCaption(RollLine(a, b, total, volume));
        SetStamp(total > 10 ? 1 : total <= 3 ? -1 : 0);
        SetRollButton(true);
        rolling = false;
    }

    static void Pose(RectTransform rt, Vector2 pos, float rot, float scale)
    {
        if (rt == null) return;
        rt.anchoredPosition = pos;
        rt.localEulerAngles = new Vector3(0f, 0f, rot);
        rt.localScale = new Vector3(scale, scale, 1f);
    }

    void ShowDice(int a, int b)
    {
        ShowFace(die1, a);
        ShowFace(die2, b);
    }

    static void ShowFace(Die d, int n)
    {
        if (d == null || d.pips == null) return;
        var face = Faces[Mathf.Clamp(n, 1, 6)];
        for (int i = 0; i < d.pips.Length && i < 7; i++)
            if (d.pips[i] != null && d.pips[i].activeSelf != face[i]) d.pips[i].SetActive(face[i]);
    }

    void MovePuck(int total, bool animate)
    {
        shownTotal = total;
        for (int i = 0; i < boxes.Length; i++)
            if (boxes[i] != null) boxes[i].color = i == total - 2 ? boxCurrent : new Color(boxCurrent.r, boxCurrent.g, boxCurrent.b, 0f);
        if (puck == null) return;
        puck.gameObject.SetActive(true);
        int i2 = Mathf.Clamp(total - 2, 0, boxes.Length - 1);
        if (boxes[i2] == null) return;
        float x = PuckX(boxes[i2].rectTransform);
        if (puckMove != null) StopCoroutine(puckMove);
        if (!animate || !isActiveAndEnabled)
        {
            puck.anchoredPosition = new Vector2(x, puck.anchoredPosition.y);
            return;
        }
        puckMove = StartCoroutine(SlidePuck(x));
    }

    float PuckX(RectTransform box)
    {
        // the box's centre in the puck's parent space
        var parent = puck.parent as RectTransform;
        Vector3 world = box.TransformPoint(box.rect.center);
        return parent != null ? ((Vector2)parent.InverseTransformPoint(world)).x : box.anchoredPosition.x;
    }

    IEnumerator SlidePuck(float x)
    {
        float from = puck.anchoredPosition.x, t = 0f;
        while (t < 0.25f)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.SmoothStep(0f, 1f, t / 0.25f);
            puck.anchoredPosition = new Vector2(Mathf.Lerp(from, x, k), puck.anchoredPosition.y);
            yield return null;
        }
        puck.anchoredPosition = new Vector2(x, puck.anchoredPosition.y);
        puckMove = null;
    }

    void SetCaption(string text)
    {
        if (caption != null) caption.text = text;
    }

    /// <summary>1 = LOUD!, -1 = QUIET..., 0 = none.</summary>
    public void SetStamp(int kind)
    {
        if (stamp == null) return;
        stamp.SetActive(kind != 0);
        if (kind == 0) return;
        Color c = kind > 0 ? LoudInk : QuietInk;
        if (stampText != null)
        {
            stampText.text = CompanyText.Get(kind > 0 ? "menu.dice.loud" : "menu.dice.quiet");
            stampText.color = c;
        }
        if (stampLines != null)
            foreach (var g in stampLines)
                if (g != null) g.color = c;
        var rt = (RectTransform)stamp.transform;
        rt.anchoredPosition = kind > 0 ? loudStampPos : quietStampPos;
    }

    void SetRollButton(bool on)
    {
        if (rollButton != null) rollButton.interactable = on;
        if (diceButton != null) diceButton.interactable = on;
    }

    void Play(AudioClip clip)
    {
        if (audioSource != null && clip != null)
            audioSource.PlayOneShot(clip, GameSettings.SfxVolume);
    }

    // A volume as the whole number of a "{volume}%" placeholder: the Company table's entry puts the % sign where its language wants it
    static string Pct(float v) => Mathf.RoundToInt(v * 100f).ToString(CultureInfo.InvariantCulture);

    /// <summary>"3 + 4 = 7 → 50% volume" (or the floor's line) in the current language (Company table, menu.dice.result*).</summary>
    string RollLine(int a, int b, int total, float volume)
    {
        bool floor = volume > (total - 2) / 10f + 0.001f;
        return CompanyText.Get(floor ? "menu.dice.result.floor" : "menu.dice.result",
            ("a", a.ToString(CultureInfo.InvariantCulture)), ("b", b.ToString(CultureInfo.InvariantCulture)),
            ("total", total.ToString(CultureInfo.InvariantCulture)), ("volume", Pct(volume)));
    }

    /// <summary>Show a finished roll without animating (previews).</summary>
    public void ShowRoll(int a, int b)
    {
        RememberRest();
        ShowDice(a, b);
        int total = a + b;
        MovePuck(total, false);
        float volume = VolumeForTotal(total, minimumVolume);
        SetCaption(RollLine(a, b, total, volume));
        SetStamp(total > 10 ? 1 : total <= 3 ? -1 : 0);
    }

    /// <summary>Freeze mid-roll (previews).</summary>
    public void ShowRolling()
    {
        RememberRest();
        Pose(die1.root, rest1 + new Vector2(-8f, jump * 0.8f), restRot1 + 36f, 1.18f);
        Pose(die2.root, rest2 + new Vector2(10f, jump), restRot2 - 41f, 1.18f);
        ShowDice(4, 2);
        if (puck != null) puck.gameObject.SetActive(false);
        SetCaption(CompanyText.Get("menu.dice.rolling"));
        SetStamp(0);
        SetRollButton(false);
    }
}
