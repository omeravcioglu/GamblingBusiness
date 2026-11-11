using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Stop a moving bar in the target zone
/// </summary>
public class StopTheBarMiniGame : MiniGameBase
{

    [Header("Game Elements")]
    public RectTransform movingBar;
    public RectTransform targetZone;
    public RectTransform track;
    public TextMeshProUGUI scoreText;

    [Header("Settings")]
    public int successesNeeded = 3;
    public float startSpeed = 300f;
    public float speedIncrease = 75f;

    private float barPosition;
    private float direction = 1f;
    private float currentSpeed;
    private int successes;
    private float trackWidth = 400f;   // half the usable track width
    private float barY;

    void Awake()
    {
        gameName = "Stop The Bar";
        timeLimit = 0f;
    }

    protected override void GameUpdate()
    {

        barPosition += currentSpeed * direction * Mathf.Min(Time.unscaledDeltaTime, 0.05f);

        // Bounce at the ends (clamped, so a long frame can't leave it stuck outside the track)
        if (barPosition > trackWidth)
        {
            barPosition = trackWidth;
            direction = -1f;
        }
        else if (barPosition < -trackWidth)
        {
            barPosition = -trackWidth;
            direction = 1f;
        }
        movingBar.anchoredPosition = new Vector2(barPosition, barY);
        movingBar.localScale = new Vector3(direction >= 0f ? 1f : -1f, 1f, 1f);   // CRT: the trail points back along the travel

        if (Input.GetKeyDown(KeyCode.Space))
        {
            if (IsBarInTarget())
            {
                successes++;
                UpdateScore();
                PlaySound(successSound);
                StartCoroutine(FlashEffect(successFlashColor));
                if (successes >= successesNeeded) Win();
                else { currentSpeed += speedIncrease; RandomizeTarget(); }
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
        if (movingBar == null || targetZone == null)
        {
            FailSetup("MovingBar or TargetZone not found");
            yield break;
        }

        successes = 0;
        currentSpeed = startSpeed;
        barPosition = 0;
        direction = 1f;
        barY = movingBar.anchoredPosition.y;
        if (track != null) trackWidth = Mathf.Max(60f, track.rect.width / 2f - 20f);
        movingBar.anchoredPosition = new Vector2(barPosition, barY);
        RandomizeTarget();
        UpdateScore();
    }

    void AutoFindElements()
    {
        if (movingBar == null) movingBar = FindElement<RectTransform>("MovingBar");
        if (movingBar == null) movingBar = FindElement<RectTransform>("Bar");
        if (targetZone == null) targetZone = FindElement<RectTransform>("TargetZone");
        if (targetZone == null) targetZone = FindElement<RectTransform>("Target");
        if (track == null) track = FindElement<RectTransform>("Track");
        if (scoreText == null) scoreText = FindElement<TextMeshProUGUI>("ScoreText");
        if (scoreText == null) scoreText = FindElement<TextMeshProUGUI>("Score");
    }

    void RandomizeTarget()
    {
        if (targetZone == null) return;
        float range = Mathf.Max(0f, (track != null ? track.rect.width * 0.5f : trackWidth + 20f) - targetZone.rect.width * 0.5f);   // the zone stays on the track
        targetZone.anchoredPosition = new Vector2(UnityEngine.Random.Range(-range, range), targetZone.anchoredPosition.y);
        ShowStat(Times(currentSpeed / Mathf.Max(1f, startSpeed)));
    }

    bool IsBarInTarget()
    {
        if (targetZone == null) return false;
        float targetX = targetZone.anchoredPosition.x;
        // Counts when the bar touches the zone by at least half its width
        float halfWidth = targetZone.rect.width / 2f + movingBar.rect.width / 4f;
        return Mathf.Abs(barPosition - targetX) <= halfWidth;
    }

    void UpdateScore()
    {
        if (scoreText != null) scoreText.text = $"{successes} / {successesNeeded}";
        ShowProgress(successes, successesNeeded);
    }
}
