using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Press the key matching the shown color
/// </summary>
public class ColorMatchMiniGame : MiniGameBase
{

    [Header("Game Elements")]
    public Image colorDisplay;
    public TextMeshProUGUI keyPrompt;
    public TextMeshProUGUI scoreText;
    public Image[] keyIndicators;

    [Header("Settings")]
    public int matchesNeeded = 10;
    public float startReactionTime = 2f;
    public float reactionDecrease = 0.1f;

    private int matches;
    private int currentIndex = -1;
    private KeyCode currentKey = KeyCode.None;
    private float reactionTimer;
    private float maxReactionTime;
    // CRT panels: the draining window for this colour and the swatch's glow
    private Image windowFill;
    private TMP_Text windowText;
    private UIShape colorGlow;
    private Color[] colors = { CrtScreenView.Red, CrtScreenView.Green, CrtScreenView.Blue, CrtScreenView.Yellow };
    private KeyCode[] keys = { KeyCode.R, KeyCode.G, KeyCode.B, KeyCode.Y };

    void Awake()
    {
        gameName = "Color Match";
        timeLimit = 25f;
    }

    protected override void GameUpdate()
    {
        reactionTimer -= Time.unscaledDeltaTime;
        UpdateWindow();

        if (reactionTimer <= 0)
        {
            PlaySound(failSound);
            Lose(CrisisText.Result("too_slow"));
            return;
        }

        for (int i = 0; i < keys.Length; i++)
        {
            if (Input.GetKeyDown(keys[i]))
            {
                if (keys[i] == currentKey)
                {
                    matches++;
                    UpdateScore();
                    PlaySound(successSound);
                    StartCoroutine(FlashEffect(successFlashColor));
                    if (matches >= matchesNeeded) Win();
                    else ShowNextColor();
                }
                else
                {
                    PlaySound(failSound);
                    Lose(CrisisText.Result("wrong_color"));
                }
                break;
            }
        }
    }

    protected override IEnumerator SetupGame()
    {
        yield return StartCoroutine(base.SetupGame());
        AutoFindElements();
        matches = 0;
        currentIndex = -1;
        currentKey = KeyCode.None;
        maxReactionTime = startReactionTime;
        UpdateScore();
        SetupKeyIndicators();

        // Neutral until play starts
        if (colorDisplay != null) colorDisplay.color = CrtScreenView.Lit(CrtScreenView.Blue, 0.12f);
        if (colorGlow != null) colorGlow.enabled = false;
        if (windowFill != null) windowFill.fillAmount = 1f;
        if (windowText != null) windowText.text = "";
        if (keyPrompt != null) keyPrompt.text = "?";
    }

    protected override void OnPlayStarted()
    {
        ShowNextColor();
    }

    void AutoFindElements()
    {
        if (colorDisplay == null) colorDisplay = FindElement<Image>("ColorDisplay");
        if (keyPrompt == null) keyPrompt = FindElement<TextMeshProUGUI>("KeyPrompt");
        if (windowFill == null) windowFill = FindElement<Image>("WindowFill");
        if (windowText == null) windowText = FindElement<TMP_Text>("WindowText");
        if (colorGlow == null) colorGlow = FindElement<UIShape>("ColorGlow");
        if (scoreText == null) scoreText = FindElement<TextMeshProUGUI>("ScoreText");
        if (scoreText == null) scoreText = FindElement<TextMeshProUGUI>("Score");

        // Try to find key indicators
        if (keyIndicators == null || keyIndicators.Length == 0)
        {
            var indicators = new List<Image>();
            for (int i = 0; i < 4; i++)
            {
                var indicator = FindElement<Image>($"KeyIndicator{i}");
                if (indicator != null) indicators.Add(indicator);
            }
            if (indicators.Count > 0) keyIndicators = indicators.ToArray();
        }
    }

    void SetupKeyIndicators()
    {
        if (keyIndicators == null) return;
        for (int i = 0; i < keyIndicators.Length && i < colors.Length; i++)
            if (keyIndicators[i] != null) keyIndicators[i].color = colors[i];
    }

    void ShowNextColor()
    {
        // Never the same color twice in a row, so every new round is visible
        int index = UnityEngine.Random.Range(0, colors.Length);
        if (index == currentIndex) index = (index + UnityEngine.Random.Range(1, colors.Length)) % colors.Length;
        currentIndex = index;

        if (colorDisplay != null) colorDisplay.color = colors[index];
        if (colorGlow != null)
        {
            colorGlow.enabled = true;
            colorGlow.color = CrtScreenView.Lit(colors[index], 0.65f);
        }
        currentKey = keys[index];
        if (keyPrompt != null) keyPrompt.text = currentKey.ToString();
        maxReactionTime = Mathf.Max(0.5f, maxReactionTime - reactionDecrease);
        reactionTimer = maxReactionTime;
        ShowStat(Seconds(maxReactionTime));
        UpdateWindow();
    }

    void UpdateScore()
    {
        if (scoreText != null) scoreText.text = $"{matches} / {matchesNeeded}";
        ShowProgress(matches, matchesNeeded);
    }

    /// <summary>CRT panels: how much of this colour's time is left.</summary>
    void UpdateWindow()
    {
        float t = maxReactionTime > 0f ? Mathf.Clamp01(reactionTimer / maxReactionTime) : 0f;
        if (windowFill != null) windowFill.fillAmount = t;
        if (windowText != null) windowText.text = Seconds(Mathf.Max(0f, reactionTimer));
    }
}
