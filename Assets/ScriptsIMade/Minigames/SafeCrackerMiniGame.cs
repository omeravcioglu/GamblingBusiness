using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Turn the dial to crack the safe combination
/// </summary>
public class SafeCrackerMiniGame : MiniGameBase
{

    [Header("Game Elements")]
    public RectTransform dial;
    public TextMeshProUGUI targetText;
    public TextMeshProUGUI currentText;
    public TextMeshProUGUI progressText;

    [Header("Settings")]
    public int numbersToFind = 3;
    public float dialSpeed = 100f;

    private int[] combination;
    private int currentIndex;
    private float dialAngle;
    private int currentNumber;
    // CRT panels: the combination as cells, the number under the pointer lit, the arrow being held lit
    private Transform findCells, dialNumbers, turnLeft, turnRight;
    private int shownNumber = -1;
    private int shownTurn = -1;

    void Awake()
    {
        gameName = "Safe Cracker";
        timeLimit = 30f;
    }

    protected override void GameUpdate()
    {
        if (combination == null || combination.Length == 0) return;

        // Turn dial
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        if (Input.GetKey(KeyCode.LeftArrow)) dialAngle += dialSpeed * dt;
        if (Input.GetKey(KeyCode.RightArrow)) dialAngle -= dialSpeed * dt;
        dialAngle = Mathf.Repeat(dialAngle, 360f);

        currentNumber = Mathf.RoundToInt(dialAngle / 36f) % 10;
        if (dial != null) dial.localRotation = Quaternion.Euler(0, 0, dialAngle);
        if (currentText != null) currentText.text = currentNumber.ToString();
        LightDial();
        ShowTurning(Input.GetKey(KeyCode.LeftArrow), Input.GetKey(KeyCode.RightArrow));

        // Confirm
        if (Input.GetKeyDown(KeyCode.Space) && currentIndex < combination.Length)
        {
            if (currentNumber == combination[currentIndex])
            {
                currentIndex++;
                PlaySound(successSound);
                StartCoroutine(FlashEffect(successFlashColor));
                UpdateProgress();
                if (currentIndex >= combination.Length) Win();
            }
            else
            {
                PlaySound(failSound);
                Lose(CrisisText.Result("wrong_number"));
            }
        }
    }


    protected override IEnumerator SetupGame()
    {
        yield return StartCoroutine(base.SetupGame());
        AutoFindElements();
        if (currentText == null)
        {
            FailSetup("CurrentText not found (the dial number can't be read without it)");
            yield break;
        }

        numbersToFind = Mathf.Max(1, numbersToFind);
        combination = new int[numbersToFind];
        for (int i = 0; i < numbersToFind; i++)
            combination[i] = UnityEngine.Random.Range(0, 10);
        currentIndex = 0;
        dialAngle = 0;
        currentNumber = 0;
        if (dial != null) dial.localRotation = Quaternion.identity;
        if (currentText != null) currentText.text = "0";
        shownNumber = -1;
        shownTurn = -1;
        LightDial();
        ShowTurning(false, false);
        UpdateProgress();
    }

    void AutoFindElements()
    {
        Transform gameArea = transform.Find("GameArea");
        if (gameArea == null) return;
        if (dial == null) { var t = gameArea.Find("Dial"); if (t) dial = t.GetComponent<RectTransform>(); }
        if (targetText == null) { var t = gameArea.Find("TargetText"); if (t) targetText = t.GetComponent<TextMeshProUGUI>(); }
        if (currentText == null) { var t = gameArea.Find("CurrentText"); if (t) currentText = t.GetComponent<TextMeshProUGUI>(); }
        if (progressText == null) { var t = gameArea.Find("ProgressText"); if (t) progressText = t.GetComponent<TextMeshProUGUI>(); }
        if (findCells == null) findCells = gameArea.Find("FindCells");
        if (dialNumbers == null && dial != null) dialNumbers = dial.Find("Numbers");
        if (turnLeft == null) turnLeft = gameArea.Find("TurnLeft");
        if (turnRight == null) turnRight = gameArea.Find("TurnRight");
    }

    void UpdateProgress()
    {
        if (progressText != null) progressText.text = $"{currentIndex} / {numbersToFind}";
        ShowProgress(currentIndex, numbersToFind);
        RenderFind();

        // Found numbers green, the one to find next highlighted
        if (targetText != null && combination != null)
        {
            var parts = new string[combination.Length];
            for (int i = 0; i < combination.Length; i++)
            {
                if (i < currentIndex) parts[i] = $"<color=#55FF55>{combination[i]}</color>";
                else if (i == currentIndex) parts[i] = $"<color=#FFD84D><u>{combination[i]}</u></color>";
                else parts[i] = combination[i].ToString();
            }
            targetText.text = CrisisText.Get("crisis.legacy.safe_cracker.find", ("digits", string.Join("-", parts)));
        }
    }

    /// <summary>CRT panels: the number under the pointer lights up.</summary>
    void LightDial()
    {
        if (dialNumbers == null || currentNumber == shownNumber) return;
        shownNumber = currentNumber;
        for (int i = 0; i < dialNumbers.childCount; i++)
        {
            var t = dialNumbers.GetChild(i).GetComponentInChildren<TMP_Text>();
            if (t != null) t.color = i == currentNumber ? CrtScreenView.Paper : CrtScreenView.Blue;
        }
    }

    /// <summary>CRT panels: the arrow and key being held light up (TurnLeft / TurnRight, each with Lit and Idle).</summary>
    void ShowTurning(bool left, bool right)
    {
        int state = (left ? 1 : 0) + (right ? 2 : 0);
        if (state == shownTurn) return;
        shownTurn = state;
        SetLit(turnLeft, left);
        SetLit(turnRight, right);
    }

    static void SetLit(Transform t, bool lit)
    {
        if (t == null) return;
        var on = t.Find("Lit");
        var off = t.Find("Idle");
        if (on != null) on.gameObject.SetActive(lit);
        if (off != null) off.gameObject.SetActive(!lit);
    }

    /// <summary>CRT panels: found numbers green with a check, the next one outlined and underlined.</summary>
    void RenderFind()
    {
        if (findCells == null || combination == null) return;
        for (int i = 0; i < findCells.childCount; i++)
        {
            var cell = findCells.GetChild(i);
            bool used = i < combination.Length;
            cell.gameObject.SetActive(used);
            if (!used) continue;
            bool found = i < currentIndex, next = i == currentIndex;
            var digit = cell.Find("Digit");
            if (digit != null)
            {
                var tmp = digit.GetComponent<TMP_Text>();
                tmp.text = combination[i].ToString();
                tmp.color = found ? CrtScreenView.Ink : (next ? CrtScreenView.Yellow : CrtScreenView.Blue);
            }
            SetActiveChild(cell, "Fill", found);
            SetActiveChild(cell, "Check", found);
            SetActiveChild(cell, "Frame", next);
            SetActiveChild(cell, "FrameIdle", !found && !next);
            SetActiveChild(cell, "Underline", next);
        }
    }

    static void SetActiveChild(Transform parent, string child, bool on)
    {
        var t = parent.Find(child);
        if (t != null && t.gameObject.activeSelf != on) t.gameObject.SetActive(on);
    }
}
