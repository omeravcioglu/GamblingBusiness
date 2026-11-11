using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Tap the target before it disappears
/// </summary>
public class QuickTapMiniGame : MiniGameBase
{

    [Header("Game Elements")]
    public Button targetButton;
    public RectTransform spawnArea;
    public TextMeshProUGUI scoreText;

    [Header("Settings")]
    public int targetsToHit = 12;
    public float startTargetTime = 1.5f;
    public float minTargetTime = 0.75f;
    public float targetTimeDecrease = 0.06f;

    private int hits;
    private float targetTimer;
    private float currentTargetTime;
    // CRT panels: the ring that closes in on the target, and where the last one was hit
    private UIShape ring;
    private RectTransform lastHit;
    private Coroutine lastHitFade;

    void Awake()
    {
        gameName = "Quick Tap";
        timeLimit = 20f;
    }

    protected override void GameUpdate()
    {

        targetTimer -= Time.unscaledDeltaTime;
        UpdateRing();
        if (targetTimer <= 0)
        {
            PlaySound(failSound);
            Lose(CrisisText.Result("too_slow"));
        }
    }


    protected override IEnumerator SetupGame()
    {
        yield return StartCoroutine(base.SetupGame());
        AutoFindElements();
        if (targetButton == null)
        {
            FailSetup("TargetButton not found");
            yield break;
        }

        hits = 0;
        currentTargetTime = startTargetTime;
        UpdateScore();

        targetButton.onClick.RemoveAllListeners();
        targetButton.onClick.AddListener(OnTargetClicked);
        targetButton.gameObject.SetActive(false); // appears when play starts
        if (lastHit != null) lastHit.gameObject.SetActive(false);
    }

    protected override void OnPlayStarted()
    {
        targetButton.gameObject.SetActive(true);
        MoveTarget();
    }

    void AutoFindElements()
    {
        if (targetButton == null) targetButton = FindElement<Button>("TargetButton");
        if (targetButton == null) targetButton = FindElement<Button>("Target");
        if (spawnArea == null) spawnArea = FindElement<RectTransform>("SpawnArea");
        if (spawnArea == null) spawnArea = FindElement<RectTransform>("GameArea");
        if (scoreText == null) scoreText = FindElement<TextMeshProUGUI>("ScoreText");
        if (scoreText == null) scoreText = FindElement<TextMeshProUGUI>("Score");
        if (ring == null) ring = FindElement<UIShape>("Ring");
        if (lastHit == null) lastHit = FindElement<RectTransform>("LastHit");
    }

    void MoveTarget()
    {
        targetTimer = currentTargetTime;
        ShowStat(Seconds(currentTargetTime));
        UpdateRing();
        if (targetButton == null || spawnArea == null) return;

        var targetRect = targetButton.GetComponent<RectTransform>();
        Vector2 size = spawnArea.rect.size;
        float margin = Mathf.Max(40f, targetRect.rect.width * 0.5f + 12f);   // the whole target stays inside the field
        float halfX = Mathf.Max(0f, size.x / 2 - margin);
        float halfY = Mathf.Max(0f, size.y / 2 - margin);
        Vector2 newPos = new Vector2(
            UnityEngine.Random.Range(-halfX, halfX),
            UnityEngine.Random.Range(-halfY, halfY));

        targetRect.anchoredPosition = newPos;
    }

    public void OnTargetClicked()
    {
        if (!isPlaying) return;

        ShowLastHit();
        hits++;
        UpdateScore();
        PlaySound(successSound);
        currentTargetTime = Mathf.Max(minTargetTime, currentTargetTime - targetTimeDecrease);

        if (hits >= targetsToHit)
            Win();
        else
            MoveTarget();
    }

    void UpdateScore()
    {
        if (scoreText != null)
            scoreText.text = $"{hits} / {targetsToHit}";
        ShowProgress(hits, targetsToHit);
    }

    /// <summary>CRT panels: the ring closes in on the target over its window (ring meets target = too slow).</summary>
    void UpdateRing()
    {
        if (ring == null || targetButton == null) return;
        float t = currentTargetTime > 0f ? Mathf.Clamp01(targetTimer / currentTargetTime) : 0f;
        var target = (RectTransform)targetButton.transform;
        float size = Mathf.Lerp(target.rect.width + 12f, 340f, t);
        ring.rectTransform.sizeDelta = new Vector2(size, size);
    }

    /// <summary>CRT panels: a check where the target was just hit, fading out.</summary>
    void ShowLastHit()
    {
        if (lastHit == null || targetButton == null || !isActiveAndEnabled) return;
        lastHit.anchoredPosition = ((RectTransform)targetButton.transform).anchoredPosition;
        lastHit.gameObject.SetActive(true);
        if (lastHitFade != null) StopCoroutine(lastHitFade);
        lastHitFade = StartCoroutine(FadeLastHit());
    }

    IEnumerator FadeLastHit()
    {
        var group = lastHit.GetComponent<CanvasGroup>();
        float t = 0f;
        while (t < 0.6f)
        {
            t += Time.unscaledDeltaTime;
            if (group != null) group.alpha = 1f - t / 0.6f;
            yield return null;
        }
        lastHit.gameObject.SetActive(false);
        if (group != null) group.alpha = 1f;
    }
}
