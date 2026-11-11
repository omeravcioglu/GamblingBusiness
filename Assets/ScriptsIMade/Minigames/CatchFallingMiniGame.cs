using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Move left/right to catch falling objects
/// </summary>
public class CatchFallingMiniGame : MiniGameBase
{

    [Header("Game Elements")]
    public RectTransform catcher;
    public RectTransform fallingObject;
    public RectTransform spawnArea;
    public TextMeshProUGUI scoreText;

    [Header("Settings")]
    public int catchesToWin = 10;
    [Tooltip("Misses allowed before losing (the last one loses)")]
    public int lives = 3;
    public float catcherSpeed = 600f;
    public float fallSpeed = 200f;
    public float fallSpeedIncrease = 12f;
    public float maxFallSpeed = 340f;

    private int catches;
    private int livesLeft;
    private float currentFallSpeed;
    private float catcherX;
    private float catcherY;
    private float catcherHalfWidth = 40f;
    private float catcherHalfHeight = 12.5f;
    private float objectHalfSize = 17.5f;
    private float maxCatcherX = 250f;
    private float spawnHalfRange = 200f;
    private float spawnY = 180f;
    private bool spawnYCaptured;
    // CRT panels: speed lines trailing the catcher while it moves (drawn for moving left, mirrored for right)
    private RectTransform speedLines;

    void Awake()
    {
        gameName = "Catch Falling";
        timeLimit = 25f;
    }

    protected override void GameUpdate()
    {
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);

        // Move catcher
        float move = 0f;
        if (Input.GetKey(KeyCode.LeftArrow)) move -= 1f;
        if (Input.GetKey(KeyCode.RightArrow)) move += 1f;
        float before = catcherX;
        catcherX = Mathf.Clamp(catcherX + move * catcherSpeed * dt, -maxCatcherX, maxCatcherX);
        catcher.anchoredPosition = new Vector2(catcherX, catcherY);
        ShowSpeedLines(catcherX - before);

        // Move falling object
        Vector2 pos = fallingObject.anchoredPosition;
        pos.y -= currentFallSpeed * dt;
        fallingObject.anchoredPosition = pos;

        // Resolve when the object's bottom reaches the catcher's top
        if (pos.y - objectHalfSize > catcherY + catcherHalfHeight) return;

        if (Mathf.Abs(pos.x - catcherX) <= catcherHalfWidth + objectHalfSize * 0.5f)
        {
            catches++;
            UpdateScore();
            PlaySound(successSound);
            if (catches >= catchesToWin)
            {
                Win();
                return;
            }
            currentFallSpeed = Mathf.Min(maxFallSpeed, currentFallSpeed + fallSpeedIncrease);
            SpawnNewObject();
        }
        else
        {
            livesLeft--;
            UpdateScore();
            PlaySound(failSound);
            if (livesLeft <= 0)
            {
                Lose(CrisisText.Result("dropped_it"));
                return;
            }
            StartCoroutine(FlashEffect(failFlashColor));
            SpawnNewObject();
        }
    }


    protected override IEnumerator SetupGame()
    {
        yield return StartCoroutine(base.SetupGame());
        AutoFindElements();
        if (catcher == null || fallingObject == null)
        {
            FailSetup("Catcher or FallingObject not found");
            yield break;
        }

        // Sizes and limits from the actual layout
        catcherY = catcher.anchoredPosition.y;
        catcherHalfWidth = catcher.rect.width * 0.5f;
        catcherHalfHeight = catcher.rect.height * 0.5f;
        objectHalfSize = fallingObject.rect.height * 0.5f;
        var area = catcher.parent as RectTransform;
        float halfWidth = area != null ? area.rect.width * 0.5f : 300f;
        maxCatcherX = Mathf.Max(50f, halfWidth - catcherHalfWidth);
        spawnHalfRange = Mathf.Min(200f, maxCatcherX);
        if (!spawnYCaptured)
        {
            spawnYCaptured = true;
            float y = fallingObject.anchoredPosition.y;
            spawnY = y > catcherY + 100f ? y : catcherY + 300f;
        }

        catches = 0;
        livesLeft = Mathf.Max(1, lives);
        currentFallSpeed = fallSpeed;
        catcherX = 0;
        catcher.anchoredPosition = new Vector2(catcherX, catcherY);
        ShowSpeedLines(0f);
        UpdateScore();
        SpawnNewObject();
    }

    void AutoFindElements()
    {
        Transform gameArea = transform.Find("GameArea");
        if (gameArea == null) return;
        if (catcher == null) catcher = FindElement<RectTransform>("Catcher");   // may sit in a shaft inside GameArea
        if (fallingObject == null) fallingObject = FindElement<RectTransform>("FallingObject");
        if (speedLines == null && catcher != null) speedLines = catcher.Find("SpeedLines") as RectTransform;
        if (scoreText == null) { var t = gameArea.Find("ScoreText"); if (t) scoreText = t.GetComponent<TextMeshProUGUI>(); }
    }

    void ShowSpeedLines(float moved)
    {
        if (speedLines == null) return;
        bool on = Mathf.Abs(moved) > 0.01f;
        if (speedLines.gameObject.activeSelf != on) speedLines.gameObject.SetActive(on);
        if (on) speedLines.localScale = new Vector3(moved < 0f ? 1f : -1f, 1f, 1f);
    }

    void SpawnNewObject()
    {
        if (fallingObject != null)
        {
            float x = UnityEngine.Random.Range(-spawnHalfRange, spawnHalfRange);
            fallingObject.anchoredPosition = new Vector2(x, spawnY);
        }
    }

    void UpdateScore()
    {
        if (scoreText != null) scoreText.text = $"{catches} / {catchesToWin}\n<size=60%>" + CrisisText.Get("crisis.legacy.lives", ("count", Mathf.Max(0, livesLeft))) + "</size>";
        ShowProgress(catches, catchesToWin);
        ShowLives(Mathf.Max(0, livesLeft), Mathf.Max(1, lives));
    }
}
