using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Solve simple math problems quickly
/// </summary>
public class MathBlitzMiniGame : MiniGameBase
{

    [Header("Game Elements")]
    public TextMeshProUGUI equationText;   // GameArea/ProblemText (the base's problemText is the crisis line)
    public Button[] answerButtons;
    public TextMeshProUGUI[] answerTexts;
    public TextMeshProUGUI scoreText;

    [Header("Settings")]
    public int problemsToSolve = 8;

    private int correctAnswer;
    private int correctIndex = -1;
    private int solved;

    void Awake()
    {
        gameName = "Math Blitz";
        timeLimit = 30f;
    }

    protected override void GameUpdate()
    {

        // Input handled by button clicks
    }


    protected override IEnumerator SetupGame()
    {
        yield return StartCoroutine(base.SetupGame());
        AutoFindElements();
        if (answerButtons == null || answerButtons.Length < 2)
        {
            FailSetup("Answer buttons not found");
            yield break;
        }

        solved = 0;
        correctIndex = -1;
        SetupButtons();
        UpdateScore();

        // Blank until play starts
        if (equationText != null) equationText.text = "? + ? = ?";
        for (int i = 0; i < answerButtons.Length; i++)
            SetAnswerText(i, "");
    }

    protected override void OnPlayStarted()
    {
        GenerateProblem();
    }

    void AutoFindElements()
    {
        Transform gameArea = transform.Find("GameArea");
        if (gameArea == null) return;

        if (equationText == null)
        {
            var t = gameArea.Find("ProblemText");
            if (t == null) t = gameArea.Find("EquationText");
            if (t) equationText = t.GetComponent<TextMeshProUGUI>();
        }
        if (scoreText == null)
        {
            var t = gameArea.Find("ScoreText");
            if (t) scoreText = t.GetComponent<TextMeshProUGUI>();
        }

        // Find answer buttons and their text components (texts stay parallel to buttons)
        if (answerButtons == null || answerButtons.Length == 0)
        {
            List<Button> btns = new List<Button>();
            List<TextMeshProUGUI> txts = new List<TextMeshProUGUI>();

            for (int i = 0; i < 4; i++)
            {
                var btnObj = gameArea.Find($"AnswerButton{i}");
                if (btnObj == null) continue;
                var btn = btnObj.GetComponent<Button>();
                if (btn == null) continue;
                btns.Add(btn);

                // Find text component - could be on button or child
                txts.Add(btnObj.GetComponentInChildren<TextMeshProUGUI>(true));
            }

            answerButtons = btns.ToArray();
            answerTexts = txts.ToArray();
        }
    }

    void SetupButtons()
    {
        for (int i = 0; i < answerButtons.Length; i++)
        {
            if (answerButtons[i] == null) continue;
            int index = i;
            answerButtons[i].onClick.RemoveAllListeners();
            answerButtons[i].onClick.AddListener(() => OnAnswerClicked(index));
        }
    }

    void GenerateProblem()
    {
        int a = UnityEngine.Random.Range(1, 15);
        int b = UnityEngine.Random.Range(1, 15);
        int op = UnityEngine.Random.Range(0, 3);
        string equation;

        switch (op)
        {
            case 0:
                equation = $"{a} + {b} = ?";
                correctAnswer = a + b;
                break;
            case 1:
                equation = $"{a + b} - {b} = ?";
                correctAnswer = a;
                break;
            default:
                a = UnityEngine.Random.Range(1, 10);
                b = UnityEngine.Random.Range(1, 10);
                equation = $"{a} × {b} = ?";
                correctAnswer = a * b;
                break;
        }
        if (equationText != null) equationText.text = equation.Replace("?", "<color=#FFE14D>?</color>");

        // One correct answer, the rest unique wrong answers
        var used = new HashSet<int> { correctAnswer };
        correctIndex = UnityEngine.Random.Range(0, answerButtons.Length);

        for (int i = 0; i < answerButtons.Length; i++)
        {
            int displayValue = i == correctIndex ? correctAnswer : MakeWrongAnswer(used);
            used.Add(displayValue);
            SetAnswerText(i, displayValue.ToString());
        }
    }

    int MakeWrongAnswer(HashSet<int> used)
    {
        for (int attempt = 0; attempt < 30; attempt++)
        {
            int offset = UnityEngine.Random.Range(1, 11) * (UnityEngine.Random.value < 0.5f ? -1 : 1);
            int candidate = correctAnswer + offset;
            if (candidate >= 1 && !used.Contains(candidate)) return candidate;
        }
        int fallback = correctAnswer + 1;
        while (used.Contains(fallback)) fallback++;
        return fallback;
    }

    void SetAnswerText(int index, string text)
    {
        TextMeshProUGUI txt = (answerTexts != null && index < answerTexts.Length) ? answerTexts[index] : null;
        if (txt == null && answerButtons[index] != null)
            txt = answerButtons[index].GetComponentInChildren<TextMeshProUGUI>(true);
        if (txt != null) txt.text = text;
    }

    public void OnAnswerClicked(int index)
    {
        if (!isPlaying || correctIndex < 0) return;

        if (index == correctIndex)
        {
            solved++;
            UpdateScore();
            PlaySound(successSound);
            StartCoroutine(FlashEffect(successFlashColor));
            if (solved >= problemsToSolve)
                Win();
            else
                GenerateProblem();
        }
        else
        {
            PlaySound(failSound);
            if (equationText != null) equationText.text = equationText.text.Replace("?", correctAnswer.ToString());
            Lose(CrisisText.Result("wrong_answer"));
        }
    }

    void UpdateScore()
    {
        if (scoreText != null)
            scoreText.text = $"{solved} / {problemsToSolve}";
        ShowProgress(solved, problemsToSolve);
    }
}
