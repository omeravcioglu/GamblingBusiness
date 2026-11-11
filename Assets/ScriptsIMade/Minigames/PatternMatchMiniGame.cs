using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Recreate the pattern shown using arrow keys
/// </summary>
public class PatternMatchMiniGame : MiniGameBase
{

    [Header("Game Elements")]
    public TextMeshProUGUI patternDisplay;
    public TextMeshProUGUI inputDisplay;
    public TextMeshProUGUI statusText;

    [Header("Settings")]
    [Tooltip("Arrows in the first pattern; each solved pattern adds one (reset every play)")]
    public int patternLength = 4;
    public int patternsToWin = 3;

    private static readonly string[] arrows = { "↑", "↓", "←", "→" };
    private List<string> pattern = new List<string>();
    private List<string> playerInput = new List<string>();
    private int currentLength;
    private int wins;
    private bool inputPhase;
    // CRT panels: the pattern and the input as rows of arrow slots
    private Transform patternSlots, inputSlots;

    void Awake()
    {
        gameName = "Pattern Match";
        timeLimit = 0f;
    }

    protected override void GameUpdate()
    {

        if (!inputPhase) return;

        string key = null;
        if (Input.GetKeyDown(KeyCode.UpArrow)) key = "↑";
        else if (Input.GetKeyDown(KeyCode.DownArrow)) key = "↓";
        else if (Input.GetKeyDown(KeyCode.LeftArrow)) key = "←";
        else if (Input.GetKeyDown(KeyCode.RightArrow)) key = "→";

        if (key == null) return;

        playerInput.Add(key);
        UpdateInputDisplay();

        // Wrong arrow: fail right away and show the pattern
        if (playerInput[playerInput.Count - 1] != pattern[playerInput.Count - 1])
        {
            inputPhase = false;
            PlaySound(failSound);
            if (patternDisplay != null) patternDisplay.text = string.Join(" ", pattern);
            RenderPattern(true);
            Lose(CrisisText.Result("wrong_pattern"));
            return;
        }

        if (playerInput.Count >= pattern.Count)
        {
            inputPhase = false;
            wins++;
            ShowProgress(wins, patternsToWin);
            PlaySound(successSound);
            StartCoroutine(FlashEffect(successFlashColor));
            if (wins >= patternsToWin) Win();
            else
            {
                currentLength++;
                StartCoroutine(ShowPattern(true));
            }
        }
    }


    protected override IEnumerator SetupGame()
    {
        yield return StartCoroutine(base.SetupGame());
        AutoFindElements();
        if (patternDisplay == null && patternSlots == null)
        {
            FailSetup("PatternDisplay not found");
            yield break;
        }

        wins = 0;
        currentLength = Mathf.Max(1, patternLength); // fresh length every play
        inputPhase = false;
        pattern.Clear();
        playerInput.Clear();
        if (patternDisplay != null) patternDisplay.text = "? ? ? ?";
        RenderPattern(false);
        RenderInput();
        if (inputDisplay != null) inputDisplay.text = "";
        SetStatus(CrisisText.Get("crisis.status.get_ready"));
    }

    protected override void OnPlayStarted()
    {
        StartCoroutine(ShowPattern(false));
    }

    void AutoFindElements()
    {
        Transform gameArea = transform.Find("GameArea");
        if (gameArea == null) return;
        if (patternDisplay == null) { var t = gameArea.Find("PatternDisplay"); if (t) patternDisplay = t.GetComponent<TextMeshProUGUI>(); }
        if (inputDisplay == null) { var t = gameArea.Find("InputDisplay"); if (t) inputDisplay = t.GetComponent<TextMeshProUGUI>(); }
        if (statusText == null) { var t = gameArea.Find("StatusText"); if (t) statusText = t.GetComponent<TextMeshProUGUI>(); }
        if (patternSlots == null) patternSlots = gameArea.Find("PatternSlots");
        if (inputSlots == null) inputSlots = gameArea.Find("InputSlots");
    }

    IEnumerator ShowPattern(bool afterCorrect)
    {
        inputPhase = false;

        if (afterCorrect)
        {
            SetStatus(CrisisText.Get("crisis.status.correct"));
            yield return new WaitForSecondsRealtime(0.5f);
            if (!isPlaying) yield break;
        }

        pattern.Clear();
        playerInput.Clear();
        for (int i = 0; i < currentLength; i++)
            pattern.Add(arrows[UnityEngine.Random.Range(0, arrows.Length)]);

        SetStatus(CrisisText.Get("crisis.game.pattern_match.memorize", ("round", wins + 1), ("rounds", patternsToWin)));
        if (patternDisplay != null) patternDisplay.text = string.Join(" ", pattern);
        RenderPattern(true);
        RenderInput();
        ShowStat(currentLength.ToString());
        ShowProgress(wins, patternsToWin);
        if (inputDisplay != null) inputDisplay.text = "";

        yield return new WaitForSecondsRealtime(2f + currentLength * 0.3f);
        if (!isPlaying) yield break;

        if (patternDisplay != null) patternDisplay.text = "???";
        RenderPattern(false);
        SetStatus(CrisisText.Get("crisis.game.pattern_match.enter_pattern"));
        inputPhase = true;
    }

    void UpdateInputDisplay()
    {
        if (inputDisplay != null) inputDisplay.text = string.Join(" ", playerInput);
        RenderInput();
    }

    void SetStatus(string text)
    {
        if (statusText != null) statusText.text = text;
    }

    enum SlotLook { Hidden, Shown, Done, Wrong, Cursor, Empty }

    /// <summary>CRT panels: the pattern row, arrows while memorizing, question marks once hidden.</summary>
    void RenderPattern(bool show)
    {
        if (patternSlots == null) return;
        int count = Mathf.Max(currentLength, pattern.Count);
        for (int i = 0; i < patternSlots.childCount; i++)
        {
            var slot = patternSlots.GetChild(i);
            bool used = i < count;
            slot.gameObject.SetActive(used);
            if (!used) continue;
            bool has = show && i < pattern.Count;
            SetSlot(slot, has ? pattern[i] : null, has ? SlotLook.Shown : SlotLook.Hidden);
        }
    }

    /// <summary>CRT panels: the input row, right arrows green, the next slot marked.</summary>
    void RenderInput()
    {
        if (inputSlots == null) return;
        int count = Mathf.Max(currentLength, pattern.Count);
        for (int i = 0; i < inputSlots.childCount; i++)
        {
            var slot = inputSlots.GetChild(i);
            bool used = i < count;
            slot.gameObject.SetActive(used);
            if (!used) continue;
            if (i < playerInput.Count)
            {
                bool right = i < pattern.Count && playerInput[i] == pattern[i];
                SetSlot(slot, playerInput[i], right ? SlotLook.Done : SlotLook.Wrong);
            }
            else
            {
                SetSlot(slot, null, i == playerInput.Count && inputPhase ? SlotLook.Cursor : SlotLook.Empty);
            }
        }
    }

    static void SetSlot(Transform slot, string arrow, SlotLook look)
    {
        var frame = slot.Find("Frame");
        var fill = slot.Find("Fill");
        var arrowImg = slot.Find("Arrow");
        var mark = slot.Find("Mark");
        var cursor = slot.Find("Cursor");
        if (fill != null)
        {
            fill.gameObject.SetActive(look == SlotLook.Done || look == SlotLook.Wrong);
            var g = fill.GetComponent<Graphic>();
            if (g != null) g.color = look == SlotLook.Wrong ? CrtScreenView.Red : CrtScreenView.Green;
        }
        if (frame != null)
        {
            frame.gameObject.SetActive(look != SlotLook.Done && look != SlotLook.Wrong && look != SlotLook.Hidden);
            var g = frame.GetComponent<Graphic>();
            if (g != null)
                g.color = look == SlotLook.Cursor ? CrtScreenView.Yellow
                    : look == SlotLook.Shown ? CrtScreenView.Blue
                    : CrtScreenView.Lit(CrtScreenView.Blue, look == SlotLook.Hidden ? 0.45f : 0.3f);
        }
        if (cursor != null) cursor.gameObject.SetActive(look == SlotLook.Cursor);
        if (mark != null) mark.gameObject.SetActive(look == SlotLook.Hidden);
        if (arrowImg != null)
        {
            arrowImg.gameObject.SetActive(arrow != null);
            if (arrow != null)
            {
                float z = arrow == "\u2191" ? 90f : arrow == "\u2193" ? -90f : arrow == "\u2190" ? 180f : 0f;
                arrowImg.localRotation = Quaternion.Euler(0f, 0f, z);
                var g = arrowImg.GetComponent<Graphic>();
                if (g != null) g.color = look == SlotLook.Shown ? CrtScreenView.Blue : CrtScreenView.Ink;
            }
        }
    }
}
