using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Repeat the pattern of colors/sounds
/// </summary>
public class SimonSaysMiniGame : MiniGameBase
{

    [Header("Game Elements")]
    public Button[] colorButtons;
    public Image[] buttonImages;
    public TextMeshProUGUI statusText;

    [Header("Settings")]
    public int patternLengthToWin = 5;
    public float simonFlashDuration = 0.4f;  // Renamed to avoid conflict with base class
    public float pauseDuration = 0.2f;
    public Color[] buttonColors = { Color.red, Color.green, Color.blue, Color.yellow };
    public Color flashColor = Color.white;

    private List<int> pattern = new List<int>();
    private int playerIndex;
    private bool showingPattern;
    private bool canInput;

    void Awake()
    {
        gameName = "Simon Says";
        timeLimit = 0f;
    }

    protected override void GameUpdate()
    {

        // Input handled by button clicks
    }


    protected override IEnumerator SetupGame()
    {
        yield return StartCoroutine(base.SetupGame());
        AutoFindElements();
        if (colorButtons == null || colorButtons.Length == 0)
        {
            FailSetup("Color buttons not found");
            yield break;
        }

        pattern.Clear();
        playerIndex = 0;
        showingPattern = false;
        canInput = false;
        SetupButtons();
        pattern.Add(UnityEngine.Random.Range(0, colorButtons.Length));
        SetStatus(CrisisText.Get("crisis.status.get_ready"));
        ShowProgress(0, patternLengthToWin, 0);
        ShowStat("0 / 1");
    }

    protected override void OnPlayStarted()
    {
        StartCoroutine(ShowPattern());
    }

    void AutoFindElements()
    {
        if (statusText == null) statusText = FindElement<TextMeshProUGUI>("StatusText");
        if (statusText == null) statusText = FindElement<TextMeshProUGUI>("Status");

        // Find color buttons if not assigned
        if (colorButtons == null || colorButtons.Length == 0)
        {
            var buttons = new List<Button>();
            string[] names = { "Red", "Green", "Blue", "Yellow", "ColorButton0", "ColorButton1", "ColorButton2", "ColorButton3" };
            foreach (var name in names)
            {
                var btn = FindElement<Button>(name);
                if (btn != null && !buttons.Contains(btn)) buttons.Add(btn);
            }
            if (buttons.Count > 0) colorButtons = buttons.ToArray();
        }

        // Images parallel to the buttons (a missing one stays null instead of shifting the rest)
        if (colorButtons != null && (buttonImages == null || buttonImages.Length != colorButtons.Length))
        {
            buttonImages = new Image[colorButtons.Length];
            for (int i = 0; i < colorButtons.Length; i++)
                buttonImages[i] = colorButtons[i] != null ? colorButtons[i].GetComponent<Image>() : null;
        }
    }

    void SetupButtons()
    {
        for (int i = 0; i < colorButtons.Length; i++)
        {
            if (colorButtons[i] == null) continue;
            int index = i;
            colorButtons[i].onClick.RemoveAllListeners();
            colorButtons[i].onClick.AddListener(() => OnColorClicked(index));
            SetButtonColor(i, GetButtonColor(i));
        }
    }

    IEnumerator ShowPattern()
    {
        showingPattern = true;
        canInput = false;
        SetStatus(CrisisText.Get("crisis.game.simon_says.watch", ("round", pattern.Count), ("rounds", patternLengthToWin)));
        ShowProgress(pattern.Count - 1, patternLengthToWin, pattern.Count - 1);
        ShowStat("0 / " + pattern.Count);
        yield return new WaitForSecondsRealtime(0.5f);

        foreach (int index in pattern)
        {
            SetButtonColor(index, flashColor);
            PlaySound(successSound);
            yield return new WaitForSecondsRealtime(simonFlashDuration);
            SetButtonColor(index, GetButtonColor(index));
            yield return new WaitForSecondsRealtime(pauseDuration);
        }
        if (!isPlaying) yield break;

        showingPattern = false;
        canInput = true;
        playerIndex = 0;
        SetStatus(CrisisText.Get("crisis.game.simon_says.your_turn"));
        ShowStat(playerIndex + " / " + pattern.Count);
    }

    public void OnColorClicked(int index)
    {
        if (!isPlaying || !canInput || showingPattern || playerIndex >= pattern.Count) return;

        // Flash the button
        StartCoroutine(FlashButton(index));

        if (index == pattern[playerIndex])
        {
            playerIndex++;
            ShowStat(playerIndex + " / " + pattern.Count);
            PlaySound(successSound);

            if (playerIndex >= pattern.Count)
            {
                canInput = false;
                if (pattern.Count >= patternLengthToWin)
                {
                    ShowProgress(pattern.Count, patternLengthToWin);
                    Win();
                }
                else
                {
                    pattern.Add(UnityEngine.Random.Range(0, colorButtons.Length));
                    StartCoroutine(ShowPattern());
                }
            }
        }
        else
        {
            canInput = false;
            PlaySound(failSound);
            Lose(CrisisText.Result("wrong_color"));
        }
    }

    IEnumerator FlashButton(int index)
    {
        SetButtonColor(index, flashColor);
        yield return new WaitForSecondsRealtime(0.15f);
        SetButtonColor(index, GetButtonColor(index));
    }

    Color GetButtonColor(int index)
    {
        return (buttonColors != null && buttonColors.Length > 0) ? buttonColors[index % buttonColors.Length] : Color.gray;
    }

    void SetButtonColor(int index, Color color)
    {
        if (buttonImages != null && index >= 0 && index < buttonImages.Length && buttonImages[index] != null)
            buttonImages[index].color = color;
            SetPadGlow(index, color == flashColor);
    }

    void SetStatus(string text)
    {
        if (statusText != null) statusText.text = text;
    }

    /// <summary>CRT panels: the lit pad glows (its "Glow" child).</summary>
    void SetPadGlow(int index, bool on)
    {
        if (colorButtons == null || index < 0 || index >= colorButtons.Length || colorButtons[index] == null) return;
        var glow = colorButtons[index].transform.Find("Glow");
        if (glow != null) glow.gameObject.SetActive(on);
    }
}
