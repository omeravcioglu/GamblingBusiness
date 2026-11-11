using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Type the word shown as fast as possible. The words stay English (Latin letters) in every language: players type them on
/// any keyboard / IME; only the instructions around them are translated (Crisis table).
/// </summary>
public class WordTypeMiniGame : MiniGameBase
{

    [Header("Game Elements")]
    public TextMeshProUGUI wordDisplay;
    public TMP_InputField inputField;
    public TextMeshProUGUI scoreText;

    [Header("Settings")]
    public int wordsToType = 6;
    private string[] words = { "PROFIT", "MONEY", "TRADE", "STOCK", "BANK", "CASH", "GOLD", "DEAL", "RICH", "EARN" };

    private string currentWord = "";
    private int typed;
    private bool submitHooked;
    private int lastSubmitFrame = -1;

    void Awake()
    {
        gameName = "Word Type";
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
        if (wordDisplay == null || inputField == null)
        {
            FailSetup("WordDisplay or InputField not found");
            yield break;
        }

        typed = 0;
        currentWord = "";
        wordDisplay.text = "...";
        inputField.text = "";
        UpdateScore();
    }

    protected override void OnPlayStarted()
    {
        ShowNextWord();
    }

    void AutoFindElements()
    {
        Transform gameArea = transform.Find("GameArea");
        if (gameArea == null) return;
        if (wordDisplay == null) { var t = gameArea.Find("WordDisplay"); if (t) wordDisplay = t.GetComponent<TextMeshProUGUI>(); }
        if (inputField == null) { var t = gameArea.Find("InputField"); if (t) inputField = t.GetComponent<TMP_InputField>(); }
        if (scoreText == null) { var t = gameArea.Find("ScoreText"); if (t) scoreText = t.GetComponent<TextMeshProUGUI>(); }

        // Submit (Enter) listener - registered once
        if (inputField != null && !submitHooked)
        {
            inputField.onSubmit.AddListener(OnSubmit);
            submitHooked = true;
        }
    }

    void ShowNextWord()
    {
        // Never the same word twice in a row
        string next = words[UnityEngine.Random.Range(0, words.Length)];
        if (next == currentWord && words.Length > 1)
        {
            int index = Array.IndexOf(words, next);
            next = words[(index + UnityEngine.Random.Range(1, words.Length)) % words.Length];
        }
        currentWord = next;
        if (wordDisplay != null) wordDisplay.text = currentWord;
        if (inputField != null)
        {
            inputField.text = "";
            FocusInputField(inputField);
        }
    }

    void OnSubmit(string text)
    {
        if (!isPlaying || string.IsNullOrEmpty(currentWord)) return;
        if (Time.frameCount == lastSubmitFrame) return; // Enter handled twice in one frame
        lastSubmitFrame = Time.frameCount;

        // Culture-safe: invariant upper-casing (with a Turkish system locale "profit".ToUpper() is "PROFİT"), compared ordinally
        if (text != null && string.Equals(NormalizeTyped(text), currentWord, StringComparison.Ordinal))
        {
            typed++;
            UpdateScore();
            PlaySound(successSound);
            StartCoroutine(FlashEffect(successFlashColor));
            if (typed >= wordsToType) Win();
            else ShowNextWord();
        }
        else
        {
            PlaySound(failSound);
            if (inputField != null)
            {
                inputField.text = "";
                FocusInputField(inputField);
            }
        }
    }

    /// <summary>
    /// What was typed, comparable with the shown word: trimmed, full-width letters from an IME folded (FormKC:
    /// "ＰＲＯＦＩＴ" → "PROFIT"), upper case by the invariant culture, and Turkish İ / ı (Caps Lock or the ı key) read as I.
    /// </summary>
    static string NormalizeTyped(string text)
    {
        string s = text.Trim().Normalize(System.Text.NormalizationForm.FormKC).ToUpperInvariant();
        return s.Replace('\u0130', 'I').Replace('\u0131', 'I');
    }

    void UpdateScore()
    {
        if (scoreText != null) scoreText.text = $"{typed} / {wordsToType}";
        ShowProgress(typed, wordsToType);
    }
}
