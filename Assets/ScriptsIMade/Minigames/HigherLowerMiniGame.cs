using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Guess if the next number is higher or lower. The next number is revealed after every guess.
/// </summary>
public class HigherLowerMiniGame : MiniGameBase
{

    [Header("Game Elements")]
    public TextMeshProUGUI currentNumberText;
    public TextMeshProUGUI scoreText;
    public Button higherButton;
    public Button lowerButton;

    [Header("Settings")]
    public int roundsToWin = 5;
    [Tooltip("Wrong guesses allowed before losing (the last one loses)")]
    public int lives = 2;
    public int minNumber = 1;
    public int maxNumber = 100;
    public float revealTime = 0.9f;

    private int currentNumber;
    private int nextNumber;
    private int wins;
    private int livesLeft;
    private bool revealing;
    private Color numberColor = Color.white;
    private bool numberColorCaptured;
    private TMP_Text hintText;   // CRT: "The next one is 1 to 100, never 52"

    void Awake()
    {
        gameName = "Higher Lower";
        timeLimit = 30f;
    }

    protected override void GameUpdate()
    {
        // Buttons, or the arrow keys
        if (Input.GetKeyDown(KeyCode.UpArrow)) Guess(true);
        else if (Input.GetKeyDown(KeyCode.DownArrow)) Guess(false);
    }


    protected override IEnumerator SetupGame()
    {
        yield return StartCoroutine(base.SetupGame());
        AutoFindElements();
        if (currentNumberText == null)
        {
            FailSetup("CurrentNumber text not found");
            yield break;
        }

        if (!numberColorCaptured)
        {
            numberColor = currentNumberText.color;
            numberColorCaptured = true;
        }

        SetupButtons();
        wins = 0;
        livesLeft = Mathf.Max(1, lives);
        revealing = false;
        currentNumber = UnityEngine.Random.Range(minNumber, maxNumber + 1);
        ShowCurrent();
        UpdateScore();
        SetButtonsInteractable(false); // until play starts
    }

    protected override void OnPlayStarted()
    {
        SetButtonsInteractable(true);
    }

    protected override string GetInstructionsText(string sceneInstructions)
    {
        return string.IsNullOrEmpty(sceneInstructions)
            ? CrisisText.Get("crisis.legacy.higher_lower.instructions")
            : CrisisText.Get("crisis.legacy.higher_lower.instructions_suffix", ("instructions", sceneInstructions));
    }

    void AutoFindElements()
    {
        Transform gameArea = transform.Find("GameArea");
        if (gameArea == null) return;
        if (currentNumberText == null) { var t = gameArea.Find("CurrentNumber"); if (t) currentNumberText = t.GetComponent<TextMeshProUGUI>(); }
        if (scoreText == null) { var t = gameArea.Find("ScoreText"); if (t) scoreText = t.GetComponent<TextMeshProUGUI>(); }
        if (higherButton == null) { var t = gameArea.Find("HigherButton"); if (t) higherButton = t.GetComponent<Button>(); }
        if (lowerButton == null) { var t = gameArea.Find("LowerButton"); if (t) lowerButton = t.GetComponent<Button>(); }
        if (hintText == null) { var t = gameArea.Find("HintText"); if (t) hintText = t.GetComponent<TMP_Text>(); }
    }

    void SetupButtons()
    {
        if (higherButton != null)
        {
            higherButton.onClick.RemoveAllListeners();
            higherButton.onClick.AddListener(OnHigher);
        }
        if (lowerButton != null)
        {
            lowerButton.onClick.RemoveAllListeners();
            lowerButton.onClick.AddListener(OnLower);
        }
    }

    void SetButtonsInteractable(bool interactable)
    {
        if (higherButton != null) higherButton.interactable = interactable;
        if (lowerButton != null) lowerButton.interactable = interactable;
    }

    void OnHigher() { Guess(true); }
    void OnLower() { Guess(false); }

    void Guess(bool guessedHigher)
    {
        if (!isPlaying || revealing) return;

        // The hidden next number is drawn now and never equals the current one
        int range = maxNumber - minNumber + 1;
        do { nextNumber = UnityEngine.Random.Range(minNumber, maxNumber + 1); } while (nextNumber == currentNumber && range > 1);

        bool correct = guessedHigher ? nextNumber > currentNumber : nextNumber < currentNumber;
        StartCoroutine(Reveal(correct));
    }

    IEnumerator Reveal(bool correct)
    {
        revealing = true;
        SetButtonsInteractable(false);

        if (correct)
        {
            wins++;
            PlaySound(successSound);
        }
        else
        {
            livesLeft--;
            PlaySound(failSound);
        }
        UpdateScore();

        // Show where the number went: "42 -> 67"
        currentNumberText.text = $"<nobr><size=50%>{currentNumber} → </size>{nextNumber}</nobr>";
        currentNumberText.color = correct ? CrtScreenView.Green : CrtScreenView.Red;

        if (wins >= roundsToWin)
        {
            Win();
            yield break;
        }
        if (livesLeft <= 0)
        {
            Lose(CrisisText.Result("wrong_guess"));
            yield break;
        }

        StartCoroutine(FlashEffect(correct ? successFlashColor : failFlashColor));
        yield return new WaitForSecondsRealtime(revealTime);
        if (!isPlaying) yield break;

        currentNumber = nextNumber;
        ShowCurrent();
        revealing = false;
        SetButtonsInteractable(true);
    }

    void ShowCurrent()
    {
        if (currentNumberText == null) return;
        currentNumberText.text = currentNumber.ToString();
        currentNumberText.color = numberColor;
        if (hintText != null)
            hintText.text = CrisisText.Get("crisis.game.higher_lower.hint", ("min", minNumber), ("max", maxNumber), ("current", currentNumber));
    }

    void UpdateScore()
    {
        if (scoreText != null) scoreText.text = $"{wins} / {roundsToWin}\n<size=60%>" + CrisisText.Get("crisis.legacy.lives", ("count", Mathf.Max(0, livesLeft))) + "</size>";
        ShowProgress(wins, roundsToWin);
        ShowLives(Mathf.Max(0, livesLeft), Mathf.Max(1, lives));
    }
}
