using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Find the different item among similar ones
/// </summary>
public class OddOneOutMiniGame : MiniGameBase
{

    [Header("Game Elements")]
    public Button[] itemButtons;
    public Image[] itemImages;
    public TextMeshProUGUI scoreText;

    [Header("Settings")]
    public int roundsToWin = 5;
    public Color[] possibleColors = { Color.red, Color.blue, Color.green, Color.yellow, Color.magenta };
    public Color idleColor = Color.gray;

    private static readonly Color[] fallbackColors = { Color.red, Color.blue, Color.green, Color.yellow, Color.magenta };

    private int correctIndex = -1;
    private int rounds;

    void Awake()
    {
        gameName = "Odd One Out";
        timeLimit = 25f;
    }

    protected override void GameUpdate()
    {

        // Input handled by buttons
    }


    protected override IEnumerator SetupGame()
    {
        yield return StartCoroutine(base.SetupGame());
        AutoFindElements();
        if (itemButtons == null || itemImages == null || itemButtons.Length < 2 || itemImages.Length != itemButtons.Length)
        {
            FailSetup("Item buttons not found");
            yield break;
        }

        rounds = 0;
        correctIndex = -1;
        SetupButtons();
        UpdateScore();

        // All the same until play starts
        foreach (var img in itemImages)
            if (img != null) img.color = idleColor;
    }

    protected override void OnPlayStarted()
    {
        GenerateRound();
    }

    void AutoFindElements()
    {
        Transform gameArea = transform.Find("GameArea");
        if (gameArea == null) return;
        if (scoreText == null) { var t = gameArea.Find("ScoreText"); if (t) scoreText = t.GetComponent<TextMeshProUGUI>(); }
        if (itemButtons == null || itemButtons.Length == 0)
        {
            List<Button> btns = new List<Button>();
            List<Image> imgs = new List<Image>();
            for (int i = 0; i < 9; i++)
            {
                var t = gameArea.Find($"ItemButton{i}");
                if (t == null) continue;
                var btn = t.GetComponent<Button>();
                var img = t.GetComponent<Image>();
                if (btn == null || img == null) continue; // keep buttons and images parallel
                btns.Add(btn);
                imgs.Add(img);
            }
            itemButtons = btns.ToArray();
            itemImages = imgs.ToArray();
        }

        // Buttons assigned in the Inspector without matching images: use each button's own Image
        if (itemButtons != null && (itemImages == null || itemImages.Length != itemButtons.Length))
        {
            itemImages = new Image[itemButtons.Length];
            for (int i = 0; i < itemButtons.Length; i++)
                itemImages[i] = itemButtons[i] != null ? itemButtons[i].GetComponent<Image>() : null;
        }
    }

    void SetupButtons()
    {
        for (int i = 0; i < itemButtons.Length; i++)
        {
            if (itemButtons[i] == null) continue;
            int idx = i;
            itemButtons[i].onClick.RemoveAllListeners();
            itemButtons[i].onClick.AddListener(() => OnItemClick(idx));
        }
    }

    void GenerateRound()
    {
        Color[] palette = (possibleColors != null && possibleColors.Length >= 2) ? possibleColors : fallbackColors;

        int mainIndex = UnityEngine.Random.Range(0, palette.Length);
        Color mainColor = palette[mainIndex];
        Color oddColor = mainColor;
        // Pick a clearly different color (bounded, so a palette of identical colors can't hang the game)
        for (int attempt = 0; attempt < 20 && ColorsMatch(oddColor, mainColor); attempt++)
            oddColor = palette[UnityEngine.Random.Range(0, palette.Length)];
        if (ColorsMatch(oddColor, mainColor))
            oddColor = new Color(1f - mainColor.r, 1f - mainColor.g, 1f - mainColor.b, 1f);

        correctIndex = UnityEngine.Random.Range(0, itemImages.Length);
        for (int i = 0; i < itemImages.Length; i++)
            if (itemImages[i] != null) itemImages[i].color = (i == correctIndex) ? oddColor : mainColor;
    }

    static bool ColorsMatch(Color a, Color b)
    {
        return Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) < 0.1f;
    }

    void OnItemClick(int idx)
    {
        if (!isPlaying || correctIndex < 0) return;

        if (idx == correctIndex)
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
            PlaySound(failSound);
            Lose(CrisisText.Result("wrong_one"));
        }
    }

    void UpdateScore()
    {
        if (scoreText != null) scoreText.text = $"{rounds} / {roundsToWin}";
        ShowProgress(rounds, roundsToWin);
    }
}
