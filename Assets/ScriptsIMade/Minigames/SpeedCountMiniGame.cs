using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Count how many objects appear on screen
/// </summary>
public class SpeedCountMiniGame : MiniGameBase
{

    [Header("Game Elements")]
    public RectTransform itemContainer;
    public GameObject itemPrefab;
    public TMP_InputField inputField;
    public TextMeshProUGUI scoreText;

    [Header("Settings")]
    public int roundsToWin = 5;
    [Tooltip("Wrong answers allowed before losing (the last one loses). A wrong answer starts a new round.")]
    public int lives = 3;
    public int minItems = 3;
    public int maxItems = 11;
    public Vector2 itemSize = new Vector2(30, 30);
    public Color itemColor = Color.yellow;

    private int correctCount;
    private int rounds;
    private int livesLeft;
    private bool submitHooked;
    private int lastSubmitFrame = -1;
    private TMP_Text rangeText;   // CRT: "between 3 and 11"
    private List<GameObject> spawnedItems = new List<GameObject>();   // pooled, reused every round

    void Awake()
    {
        gameName = "Speed Count";
        timeLimit = 30f;
    }

    protected override void GameUpdate()
    {
        if (inputField == null || inputField.isFocused) return;

        // Enter also works when the field lost focus; any other key gives it focus back
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            OnSubmit(inputField.text);
        else if (Input.anyKeyDown && !Input.GetMouseButtonDown(0) && !Input.GetMouseButtonDown(1))
            FocusInputField(inputField);
    }


    protected override IEnumerator SetupGame()
    {
        yield return StartCoroutine(base.SetupGame());
        AutoFindElements();
        if (itemContainer == null || inputField == null)
        {
            FailSetup("ItemContainer or InputField not found");
            yield break;
        }

        rounds = 0;
        livesLeft = Mathf.Max(1, lives);
        correctCount = -1;
        HideItems();
        inputField.text = "";
        if (rangeText != null) rangeText.text = CrisisText.Get("crisis.game.speed_count.range", ("min", minItems), ("max", maxItems));
        UpdateScore();
    }

    protected override void OnPlayStarted()
    {
        GenerateRound();
    }

    void AutoFindElements()
    {
        Transform gameArea = transform.Find("GameArea");
        if (gameArea == null) return;
        if (itemContainer == null) { var t = gameArea.Find("ItemContainer"); if (t) itemContainer = t.GetComponent<RectTransform>(); }
        if (inputField == null) { var t = gameArea.Find("InputField"); if (t) inputField = t.GetComponent<TMP_InputField>(); }
        if (scoreText == null) { var t = gameArea.Find("ScoreText"); if (t) scoreText = t.GetComponent<TextMeshProUGUI>(); }
        if (rangeText == null) { var t = gameArea.Find("RangeText"); if (t) rangeText = t.GetComponent<TMP_Text>(); }

        // Submit (Enter) listener - registered once
        if (inputField != null && !submitHooked)
        {
            inputField.onSubmit.AddListener(OnSubmit);
            submitHooked = true;
        }
    }

    void GenerateRound()
    {
        HideItems();

        // Lay the items out on a grid of cells (with a little jitter) so they never overlap
        Rect area = itemContainer.rect;
        float cellSize = Mathf.Max(itemSize.x, itemSize.y) + 10f;
        int cols = Mathf.Max(1, Mathf.FloorToInt(area.width / cellSize));
        int rows = Mathf.Max(1, Mathf.FloorToInt(area.height / cellSize));
        float cellW = area.width / cols;
        float cellH = area.height / rows;
        int cellCount = cols * rows;

        int low = Mathf.Clamp(minItems, 1, cellCount);
        int high = Mathf.Clamp(maxItems, low, cellCount);
        correctCount = UnityEngine.Random.Range(low, high + 1);

        var cells = new List<int>(cellCount);
        for (int i = 0; i < cellCount; i++) cells.Add(i);
        for (int i = 0; i < correctCount; i++)
        {
            int pick = UnityEngine.Random.Range(i, cellCount);
            int tmp = cells[i]; cells[i] = cells[pick]; cells[pick] = tmp;

            int col = cells[i] % cols;
            int row = cells[i] / cols;
            float jitterX = Mathf.Max(0f, (cellW - itemSize.x) * 0.4f);
            float jitterY = Mathf.Max(0f, (cellH - itemSize.y) * 0.4f);
            float x = area.xMin + (col + 0.5f) * cellW + UnityEngine.Random.Range(-jitterX, jitterX);
            float y = area.yMin + (row + 0.5f) * cellH + UnityEngine.Random.Range(-jitterY, jitterY);

            GameObject item = GetItem(i);
            var rect = item.GetComponent<RectTransform>();
            rect.anchoredPosition = new Vector2(x - area.center.x, y - area.center.y);
            item.SetActive(true);
        }

        inputField.text = "";
        FocusInputField(inputField);
    }

    GameObject GetItem(int index)
    {
        while (spawnedItems.Count <= index)
        {
            GameObject item;
            if (itemPrefab != null)
            {
                item = Instantiate(itemPrefab, itemContainer, false);
            }
            else
            {
                item = new GameObject("CountItem", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                item.transform.SetParent(itemContainer, false);
                var img = item.GetComponent<Image>();
                img.color = itemColor;
                img.raycastTarget = false;
            }
            var rect = item.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = itemSize;
            }
            spawnedItems.Add(item);
        }
        return spawnedItems[index];
    }

    void HideItems()
    {
        spawnedItems.RemoveAll(item => item == null);
        foreach (var item in spawnedItems) item.SetActive(false);
    }

    void OnSubmit(string text)
    {
        if (!isPlaying || correctCount < 0) return;
        if (Time.frameCount == lastSubmitFrame) return; // Enter handled twice in one frame
        lastSubmitFrame = Time.frameCount;

        if (string.IsNullOrWhiteSpace(text))
        {
            FocusInputField(inputField);
            return;
        }

        if (int.TryParse(text.Trim(), out int guess) && guess == correctCount)
        {
            rounds++;
            UpdateScore();
            PlaySound(successSound);
            StartCoroutine(FlashEffect(successFlashColor));
            if (rounds >= roundsToWin) Win();
            else GenerateRound();
        }
        else
        {
            livesLeft--;
            UpdateScore();
            PlaySound(failSound);
            if (livesLeft <= 0)
            {
                Lose(CrisisText.Result("it_was", ("count", correctCount)));
                return;
            }
            StartCoroutine(FlashEffect(failFlashColor));
            GenerateRound(); // a new set, so guessing numbers doesn't work
        }
    }

    void UpdateScore()
    {
        if (scoreText != null) scoreText.text = $"{rounds} / {roundsToWin}\n<size=60%>" + CrisisText.Get("crisis.legacy.lives", ("count", Mathf.Max(0, livesLeft))) + "</size>";
        ShowProgress(rounds, roundsToWin);
        ShowLives(Mathf.Max(0, livesLeft), Mathf.Max(1, lives));
    }
}
