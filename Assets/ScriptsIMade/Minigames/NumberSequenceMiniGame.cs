using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Remember and type a sequence of numbers
/// </summary>
public class NumberSequenceMiniGame : MiniGameBase
{

    [Header("Game Elements")]
    public TextMeshProUGUI displayText;
    public TMP_InputField inputField;
    public TextMeshProUGUI levelText;

    [Header("Settings")]
    public int startLength = 3;
    public int winLength = 7;
    public float displayTime = 0.5f;

    private string sequence = "";
    private int currentLength;
    private bool inputPhase;
    private bool submitHooked;
    private int lastSubmitFrame = -1;
    // CRT panels: the number in digit cells (typing is drawn into them) and the two step pills
    private RectTransform cells;
    private GameObject stepMemorize, stepMemorizeDone, stepType, stepTypeIdle;

    void Awake()
    {
        gameName = "Number Sequence";
        timeLimit = 0f;
    }

    protected override void GameUpdate()
    {
        if (inputPhase && inputField != null && cells != null)
            RenderCells(inputField.text, Mathf.Min(inputField.text.Length, currentLength - 1), null);
        // Enter also works when the field lost focus (e.g. after a click elsewhere)
        if (inputPhase && inputField != null && !inputField.isFocused &&
            (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)))
        {
            CheckAnswer();
        }
    }


    protected override IEnumerator SetupGame()
    {
        yield return StartCoroutine(base.SetupGame());
        AutoFindElements();
        if (inputField == null || (displayText == null && cells == null))
        {
            FailSetup("DisplayText or InputField not found");
            yield break;
        }

        startLength = Mathf.Max(1, startLength);
        currentLength = startLength;
        inputPhase = false;
        inputField.text = "";
        inputField.gameObject.SetActive(false);
        if (displayText != null)
        {
            displayText.text = CrisisText.Get("crisis.status.get_ready");
            displayText.color = Color.white;
            displayText.fontSize = 36;
        }
        RenderCells("", -1, null);
        SetSteps(false);
        UpdateLevel();
    }

    protected override void OnPlayStarted()
    {
        StartCoroutine(ShowSequence(false));
    }

    void AutoFindElements()
    {
        if (displayText == null) displayText = FindElement<TextMeshProUGUI>("DisplayText");
        if (displayText == null) displayText = FindElement<TextMeshProUGUI>("SequenceText");
        if (displayText == null) displayText = FindElement<TextMeshProUGUI>("Display");

        if (inputField == null) inputField = FindElement<TMP_InputField>("InputField");
        if (inputField == null) inputField = FindElement<TMP_InputField>("Input");

        if (levelText == null) levelText = FindElement<TextMeshProUGUI>("LevelText");
        if (levelText == null) levelText = FindElement<TextMeshProUGUI>("Level");
        if (cells == null) cells = FindElement<RectTransform>("Cells");
        if (stepMemorize == null) stepMemorize = FindObject("StepMemorize");
        if (stepMemorizeDone == null) stepMemorizeDone = FindObject("StepMemorizeDone");
        if (stepType == null) stepType = FindObject("StepType");
        if (stepTypeIdle == null) stepTypeIdle = FindObject("StepTypeIdle");

        // Submit (Enter) listener - registered once
        if (inputField != null && !submitHooked)
        {
            inputField.onSubmit.AddListener(OnSubmitted);
            submitHooked = true;
        }
    }

    void OnSubmitted(string text)
    {
        CheckAnswer();
    }

    IEnumerator ShowSequence(bool afterCorrect)
    {
        inputPhase = false;
        if (inputField != null) inputField.gameObject.SetActive(false);

        if (afterCorrect)
        {
            if (displayText != null)
            {
                displayText.text = CrisisText.Get("crisis.status.correct");
                displayText.color = Color.green;
                displayText.fontSize = 36;
            }
            RenderCells(inputField != null ? inputField.text : "", -1, CrtScreenView.Green);
            yield return new WaitForSecondsRealtime(0.5f);
            if (!isPlaying) yield break;
        }

        sequence = "";
        for (int i = 0; i < currentLength; i++)
            sequence += UnityEngine.Random.Range(0, 10).ToString();

        if (displayText != null)
        {
            displayText.text = sequence;
            displayText.color = Color.white;
            displayText.fontSize = 72;
        }

        RenderCells(sequence, -1, null);
        SetSteps(false);
        SetMemorizeTime(1f + currentLength * displayTime);
        if (inputField != null) inputField.characterLimit = currentLength;
        ShowStat(currentLength.ToString());
        ShowProgress(currentLength - startLength, Mathf.Max(1, winLength - startLength + 1), currentLength - startLength);
        yield return new WaitForSecondsRealtime(1f + currentLength * displayTime);
        if (!isPlaying) yield break;

        if (displayText != null)
        {
            displayText.text = CrisisText.Get("crisis.game.number_sequence.type_it");
            displayText.fontSize = 36;
        }

        inputPhase = true;
        SetSteps(true);
        RenderCells("", 0, null);

        if (inputField != null)
        {
            inputField.gameObject.SetActive(true);
            inputField.text = "";
            inputField.Select();
            inputField.ActivateInputField();
        }
    }

    void CheckAnswer()
    {
        if (!isPlaying || !inputPhase || inputField == null) return;
        if (Time.frameCount == lastSubmitFrame) return; // Enter handled twice in one frame
        lastSubmitFrame = Time.frameCount;
        inputPhase = false;

        if (inputField.text.Trim() == sequence)
        {
            currentLength++;
            UpdateLevel();
            PlaySound(successSound);
            StartCoroutine(FlashEffect(successFlashColor));

            if (currentLength > winLength)
                Win();
            else
                StartCoroutine(ShowSequence(true));
        }
        else
        {
            PlaySound(failSound);
            if (displayText != null)
            {
                displayText.text = sequence;
                displayText.color = Color.red;
                displayText.fontSize = 72;
            }
            RenderCells(sequence, -1, CrtScreenView.Red);
            Lose(CrisisText.Result("wrong_number"));
        }
    }

    void UpdateLevel()
    {
        if (levelText != null)
            levelText.text = CrisisText.Get("crisis.legacy.level", ("level", Mathf.Min(currentLength, winLength) - startLength + 1),
                                            ("levels", Mathf.Max(1, winLength - startLength + 1)));
    }

    GameObject FindObject(string elementName)
    {
        var t = FindElement<RectTransform>(elementName);
        return t != null ? t.gameObject : null;
    }

    /// <summary>
    /// CRT panels: digits in the cells (Cell0..6 with Digit, Frame, FrameCaret, FrameEmpty); only this level's cells show.
    /// caret: the cell being typed into (-1 = none); tint colours all digits (null = paper).
    /// </summary>
    void RenderCells(string digits, int caret, Color? tint)
    {
        if (cells == null) return;
        int count = Mathf.Max(1, currentLength);
        for (int i = 0; i < cells.childCount; i++)
        {
            Transform cell = cells.GetChild(i);
            bool used = i < count;
            if (cell.gameObject.activeSelf != used) cell.gameObject.SetActive(used);
            if (!used) continue;
            bool filled = digits != null && i < digits.Length;
            bool isCaret = i == caret && !filled;
            var text = cell.Find("Digit");
            if (text != null)
            {
                var tmp = text.GetComponent<TMP_Text>();
                tmp.text = filled ? digits[i].ToString() : "";
                tmp.color = tint ?? CrtScreenView.Paper;
            }
            Toggle(cell, "Frame", filled);
            Toggle(cell, "FrameCaret", isCaret);
            Toggle(cell, "FrameEmpty", !filled && !isCaret);
        }
    }

    static void Toggle(Transform parent, string child, bool on)
    {
        var t = parent.Find(child);
        if (t != null && t.gameObject.activeSelf != on) t.gameObject.SetActive(on);
    }

    void SetSteps(bool typing)
    {
        if (stepMemorize != null) stepMemorize.SetActive(!typing);
        if (stepMemorizeDone != null) stepMemorizeDone.SetActive(typing);
        if (stepType != null) stepType.SetActive(typing);
        if (stepTypeIdle != null) stepTypeIdle.SetActive(!typing);
    }

    void SetMemorizeTime(float seconds)
    {
        foreach (var step in new[] { stepMemorize, stepMemorizeDone })
        {
            if (step == null) continue;
            var t = step.transform.Find("Time") ?? step.transform.Find("Content/Time");
            if (t != null) t.GetComponent<TMP_Text>().text = CrisisText.Seconds(seconds, "0.#");
        }
    }
}
