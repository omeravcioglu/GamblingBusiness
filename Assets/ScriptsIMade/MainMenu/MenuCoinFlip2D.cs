using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The New Game coin on the designed main menu's card table (2D): it jumps off the felt, flips end over end
/// (heads and tails taking turns as it spins) and lands in its circle showing the result, with sparkles for
/// heads and thud marks for tails. MainMenuUI drives it through IMenuCoin, like the old 3D toss.
/// </summary>
[DisallowMultipleComponent]
public class MenuCoinFlip2D : MonoBehaviour, IMenuCoin
{
    [Header("Parts")]
    [Tooltip("The coin (face + edge), at rest in its printed circle")]
    public RectTransform coin;
    public RectTransform edge;
    public Image faceFill;
    public TMP_Text faceMark;
    [Tooltip("Light copy under the letter (its embossed highlight)")]
    public TMP_Text faceMarkLight;
    public RectTransform shadow;
    public GameObject[] sparkles;
    public GameObject thud;

    [Header("Faces")]
    public Color headsColor = new Color32(0xE2, 0xB1, 0x3C, 0xFF);
    public Color tailsColor = new Color32(0xC9, 0x98, 0x2C, 0xFF);

    [Header("Toss")]
    [Tooltip("The first flip's chance of heads (MainMenuUI raises it after misses)")]
    [Range(0f, 1f)] public float successChance = 1f / 3f;
    public float riseHeight = 150f;
    public float duration = 1.5f;
    [Tooltip("Half turns while in the air (each one swaps the face; one more when it lands tails)")]
    public int halfTurns = 8;
    public float settleTime = 0.35f;

    [Header("Audio (optional)")]
    public AudioSource audioSource;
    public AudioClip flipSound;
    public AudioClip landSound;

    bool tossing;
    bool lastResult;
    Vector2 restPos;
    bool restKnown;
    bool showingHeads = true;

    public float BaseSuccessChance => successChance;
    public bool IsTossing => tossing;
    public bool LastResultWasSuccess => lastResult;

    void Awake()
    {
        Remember();
        ShowRest();
    }

    void OnEnable()
    {
        L10n.LanguageChanged += OnLanguageChanged;
    }

    void OnDisable()
    {
        L10n.LanguageChanged -= OnLanguageChanged;
        if (tossing)
        {
            StopAllCoroutines();
            tossing = false;
            ShowRest();
        }
    }

    // The face letter ("H" / "T") in the new language
    void OnLanguageChanged()
    {
        SetFace(showingHeads);
    }

    void Remember()
    {
        if (restKnown || coin == null) return;
        restPos = coin.anchoredPosition;
        restKnown = true;
    }

    public void StartCoinToss(float chance)
    {
        if (tossing || !isActiveAndEnabled) return;
        Remember();
        lastResult = Random.value < Mathf.Clamp01(chance);
        StartCoroutine(Toss());
    }

    public void CleanupCoin()
    {
        if (tossing) return;
        ShowRest();
    }

    void ShowRest()
    {
        if (coin != null)
        {
            if (restKnown) coin.anchoredPosition = restPos;
            coin.localScale = Vector3.one;
            coin.localEulerAngles = Vector3.zero;
        }
        SetFace(true);
        if (edge != null) edge.gameObject.SetActive(true);
        SetShadow(1f, 1f);
        SetSparkles(false);
        if (thud != null) thud.SetActive(false);
    }

    void SetFace(bool heads)
    {
        showingHeads = heads;
        string mark = CompanyText.Get(heads ? "menu.coin.heads" : "menu.coin.tails");   // "H" / "T" in the current language
        if (faceMark != null) faceMark.text = mark;
        if (faceMarkLight != null) faceMarkLight.text = mark;
        if (faceFill != null) faceFill.color = heads ? headsColor : tailsColor;
    }

    void SetShadow(float scale, float alpha)
    {
        if (shadow == null) return;
        shadow.localScale = new Vector3(scale, scale, 1f);
        var g = shadow.GetComponent<Graphic>();
        if (g != null)
        {
            var c = g.color;
            c.a = alpha;
            g.color = c;
        }
    }

    void SetSparkles(bool on)
    {
        if (sparkles == null) return;
        foreach (var s in sparkles)
            if (s != null) s.SetActive(on);
    }

    void Play(AudioClip clip)
    {
        if (audioSource != null && clip != null)
            audioSource.PlayOneShot(clip, GameSettings.SfxVolume);
    }

    IEnumerator Toss()
    {
        tossing = true;
        SetSparkles(false);
        if (thud != null) thud.SetActive(false);
        Play(flipSound);

        float tilt = Random.Range(-14f, -8f);
        // it starts heads up: an even number of half turns lands heads, an odd one tails
        int turns = Mathf.Max(2, (halfTurns / 2) * 2) + (lastResult ? 0 : 1);
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / duration);
            // up and down (a parabola), a little bigger at the top (closer to the lamp)
            float arc = 4f * k * (1f - k);
            if (coin != null)
            {
                coin.anchoredPosition = restPos + new Vector2(0f, riseHeight * arc);
                // end over end: the face squashes to its edge and back; every half turn shows the other side
                float angle = k * turns * Mathf.PI;
                float squash = Mathf.Abs(Mathf.Cos(angle));
                float grow = 1f + 0.15f * arc;
                coin.localScale = new Vector3(grow, Mathf.Max(0.08f, squash) * grow, 1f);
                coin.localEulerAngles = new Vector3(0f, 0f, tilt * arc);
                int half = Mathf.FloorToInt(k * turns + 0.5f);
                SetFace(half % 2 == 0);
                if (edge != null) edge.gameObject.SetActive(squash > 0.5f);
            }
            SetShadow(1f - 0.35f * arc, 1f - 0.5f * arc);
            yield return null;
        }

        // land: small squash, then settle
        SetFace(lastResult);
        if (edge != null) edge.gameObject.SetActive(true);
        Play(landSound);
        float s = 0f;
        while (s < settleTime)
        {
            s += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(s / settleTime);
            float bump = Mathf.Sin(k * Mathf.PI) * 0.06f;
            if (coin != null)
            {
                coin.anchoredPosition = restPos;
                coin.localScale = new Vector3(1f + bump, 1f - bump, 1f);
                coin.localEulerAngles = Vector3.zero;
            }
            SetShadow(1f, 1f);
            yield return null;
        }
        if (coin != null) coin.localScale = Vector3.one;
        SetSparkles(lastResult);
        if (thud != null) thud.SetActive(!lastResult);
        tossing = false;
    }

    /// <summary>Show a landed result without animating (previews).</summary>
    public void ShowResult(bool heads)
    {
        Remember();
        ShowRest();
        SetFace(heads);
        SetSparkles(heads);
        if (thud != null) thud.SetActive(!heads);
    }

    /// <summary>Freeze mid-flip (previews): up in the air, edge-on.</summary>
    public void ShowInAir()
    {
        Remember();
        if (coin == null) return;
        coin.anchoredPosition = restPos + new Vector2(0f, riseHeight * 0.8f);
        coin.localScale = new Vector3(1.12f, 0.46f, 1f);
        coin.localEulerAngles = new Vector3(0f, 0f, -12f);
        SetShadow(0.7f, 0.5f);
        SetSparkles(false);
        if (thud != null) thud.SetActive(false);
    }
}
