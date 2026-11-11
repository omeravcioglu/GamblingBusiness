using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class ArrowTimingMiniGame : MiniGameBase
{
    [Header("Arrow Timing Settings")]
    public RectTransform arrowPivot;
    public Image targetZone;
    public TextMeshProUGUI scoreText;

    [Header("Game Settings")]
    public float startSpeed = 180f;
    public float speedIncrease = 20f;
    public float startTargetSize = 60f;
    public float targetShrink = 5f;
    public int hitsRequired = 5;
    [Tooltip("Extra degrees accepted on each side of the green zone (covers frame timing)")]
    public float hitTolerance = 5f;

    private float currentSpeed;
    private float currentAngle;
    private float targetStart;
    private float targetSize;
    private int hits;
    private RectTransform hitLabel;   // CRT: "HIT HERE" beside the zone

    void Awake()
    {
        gameName = "Arrow Timing";
        timeLimit = 30f;
    }

    protected override void GameUpdate()
    {
        if (arrowPivot == null) return;

        // Rotate arrow (clockwise, 0 = up)
        currentAngle = Mathf.Repeat(currentAngle + currentSpeed * Mathf.Min(Time.unscaledDeltaTime, 0.05f), 360f);
        arrowPivot.localRotation = Quaternion.Euler(0, 0, -currentAngle);

        // Check for hit
        if (Input.GetKeyDown(KeyCode.Space))
        {
            if (IsInZone())
            {
                hits++;
                UpdateScore();
                PlaySound(successSound);
                StartCoroutine(FlashEffect(successFlashColor));

                if (hits >= hitsRequired)
                    Win();
                else
                    NextRound();
            }
            else
            {
                PlaySound(failSound);
                Lose(CrisisText.Result("missed"));
            }
        }
    }

    protected override IEnumerator SetupGame()
    {
        yield return StartCoroutine(base.SetupGame());
        AutoFindElements();

        if (arrowPivot == null)
        {
            FailSetup("ArrowPivot not found");
            yield break;
        }

        // Configure target zone as radial fill (the base gives it a sprite so the fill shows)
        if (targetZone != null)
        {
            targetZone.type = Image.Type.Filled;
            targetZone.fillMethod = Image.FillMethod.Radial360;
            targetZone.fillOrigin = (int)Image.Origin360.Top;
            targetZone.fillClockwise = true;
            targetZone.preserveAspect = false;

            // Make sure it has a color
            if (targetZone.color.a < 0.1f)
                targetZone.color = new Color(0.2f, 0.8f, 0.2f, 0.7f); // Green
        }

        // Reset arrow position
        arrowPivot.localRotation = Quaternion.identity;

        hits = 0;
        currentSpeed = startSpeed;
        targetSize = startTargetSize;
        currentAngle = 0;
        targetStart = UnityEngine.Random.Range(45f, 315f - targetSize);
        UpdateTargetVisual();
        UpdateScore();
    }

    void AutoFindElements()
    {
        if (arrowPivot == null)
        {
            arrowPivot = FindElement<RectTransform>("ArrowPivot");
            if (arrowPivot == null) arrowPivot = FindElement<RectTransform>("Arrow");
        }
        if (targetZone == null)
        {
            targetZone = FindElement<Image>("TargetZone");
            if (targetZone == null) targetZone = FindElement<Image>("Target");
        }
        if (scoreText == null) scoreText = FindElement<TextMeshProUGUI>("ScoreText");
        if (scoreText == null) scoreText = FindElement<TextMeshProUGUI>("Score");
        if (hitLabel == null) hitLabel = FindElement<RectTransform>("HitLabel");
    }

    bool IsInZone()
    {
        // Angle past the (widened) zone start, wrapped to 0..360
        float start = targetStart - hitTolerance;
        float size = targetSize + hitTolerance * 2f;
        return Mathf.Repeat(currentAngle - start, 360f) <= size;
    }

    void NextRound()
    {
        currentSpeed += speedIncrease;
        ShowStat(Times(currentSpeed / Mathf.Max(1f, startSpeed)));
        targetSize = Mathf.Max(20f, targetSize - targetShrink);

        // New zone somewhere the arrow isn't right now, so every hit needs a fresh timing
        targetStart = Mathf.Repeat(currentAngle + UnityEngine.Random.Range(60f, 300f - targetSize), 360f);
        UpdateTargetVisual();
    }

    void UpdateTargetVisual()
    {
        if (targetZone == null) return;
        targetZone.fillAmount = targetSize / 360f;
        targetZone.rectTransform.localRotation = Quaternion.Euler(0, 0, -targetStart);
        PlaceHitLabel();
    }

    void UpdateScore()
    {
        if (scoreText != null) scoreText.text = $"{hits} / {hitsRequired}";
        ShowProgress(hits, hitsRequired);
        ShowStat(Times(currentSpeed / Mathf.Max(1f, startSpeed)));
    }

    /// <summary>CRT panels: keep "HIT HERE" just outside the dial, beside the middle of the zone.</summary>
    void PlaceHitLabel()
    {
        if (hitLabel == null || arrowPivot == null) return;
        float mid = (targetStart + targetSize * 0.5f) * Mathf.Deg2Rad;
        Vector2 dir = new Vector2(Mathf.Sin(mid), Mathf.Cos(mid));
        var dial = arrowPivot.parent as RectTransform;
        float radius = dial != null ? dial.rect.width * 0.5f + 30f : 200f;
        bool right = dir.x >= 0f;
        hitLabel.pivot = new Vector2(right ? 0f : 1f, 0.5f);
        hitLabel.anchoredPosition = arrowPivot.anchoredPosition + dir * radius;
        float w = hitLabel.rect.width;
        var chevron = hitLabel.Find("Chevron") as RectTransform;
        if (chevron != null)
        {
            chevron.anchoredPosition = new Vector2(right ? 15f : w - 15f, 0f);
            chevron.localScale = new Vector3(right ? 1f : -1f, 1f, 1f);
        }
        var text = hitLabel.Find("Text") as RectTransform;
        if (text != null)
        {
            text.anchoredPosition = new Vector2(right ? 44f + text.rect.width * 0.5f : w - 44f - text.rect.width * 0.5f, 0f);
            var tmp = text.GetComponent<TMP_Text>();
            if (tmp != null) tmp.alignment = right ? TextAlignmentOptions.Left : TextAlignmentOptions.Right;
        }
    }
}
