using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Click moles as they pop up from holes
/// </summary>
public class WhackAMoleMiniGame : MiniGameBase
{

    [Header("Game Elements")]
    public Button[] holeButtons;
    public Image[] holeImages;
    public TextMeshProUGUI scoreText;

    [Header("Settings")]
    public int scoreToWin = 15;
    public float startMoleTime = 1.2f;
    public float minMoleTime = 0.4f;
    public Color holeColor = Color.gray;
    public Color moleColor = Color.green;

    private int score;
    private int activeMole = -1;
    private int lastMole = -1;
    private float moleTimer;
    private float currentMoleTime;

    void Awake()
    {
        gameName = "Whack A Mole";
        timeLimit = 30f;
    }

    protected override void GameUpdate()
    {

        moleTimer -= Time.unscaledDeltaTime;
        if (moleTimer <= 0)
        {
            HideMole();
            ShowRandomMole();
        }
    }


    protected override IEnumerator SetupGame()
    {
        yield return StartCoroutine(base.SetupGame());
        AutoFindElements();
        if (holeButtons == null || holeButtons.Length == 0)
        {
            FailSetup("Hole buttons not found");
            yield break;
        }

        score = 0;
        activeMole = -1;
        lastMole = -1;
        currentMoleTime = startMoleTime;
        UpdateScore();
        SetupHoles();
    }

    protected override void OnPlayStarted()
    {
        ShowRandomMole();
    }

    void AutoFindElements()
    {
        if (scoreText == null) scoreText = FindElement<TextMeshProUGUI>("ScoreText");
        if (scoreText == null) scoreText = FindElement<TextMeshProUGUI>("Score");

        // Find hole buttons if not assigned
        if (holeButtons == null || holeButtons.Length == 0)
        {
            var buttons = new List<Button>();
            for (int i = 0; i < 9; i++)
            {
                var btn = FindElement<Button>($"Hole{i}");
                if (btn != null) buttons.Add(btn);
            }
            if (buttons.Count > 0) holeButtons = buttons.ToArray();
        }

        // Images parallel to the buttons (a missing one stays null instead of shifting the rest)
        if (holeButtons != null && (holeImages == null || holeImages.Length != holeButtons.Length))
        {
            holeImages = new Image[holeButtons.Length];
            for (int i = 0; i < holeButtons.Length; i++)
                holeImages[i] = holeButtons[i] != null ? holeButtons[i].GetComponent<Image>() : null;
        }
    }

    void SetupHoles()
    {
        for (int i = 0; i < holeButtons.Length; i++)
        {
            if (holeButtons[i] == null) continue;
            int index = i;
            holeButtons[i].onClick.RemoveAllListeners();
            holeButtons[i].onClick.AddListener(() => OnHoleClicked(index));
            SetMole(i, false);
        }
    }

    void ShowRandomMole()
    {
        // A different hole than last time, so every new mole is visible
        int index = UnityEngine.Random.Range(0, holeButtons.Length);
        if (index == lastMole && holeButtons.Length > 1)
            index = (index + UnityEngine.Random.Range(1, holeButtons.Length)) % holeButtons.Length;
        activeMole = index;
        lastMole = index;
        SetMole(activeMole, true);
        ShowStat(Times(startMoleTime / Mathf.Max(0.05f, currentMoleTime)));
        moleTimer = currentMoleTime;
    }

    void HideMole()
    {
        if (activeMole >= 0) SetMole(activeMole, false);
        activeMole = -1;
    }

    void SetHoleColor(int index, Color color)
    {
        if (holeImages != null && index >= 0 && index < holeImages.Length && holeImages[index] != null)
            holeImages[index].color = color;
    }

    public void OnHoleClicked(int index)
    {
        if (!isPlaying) return;

        if (index == activeMole)
        {
            score++;
            UpdateScore();
            PlaySound(successSound);
            HideMole();
            currentMoleTime = Mathf.Max(minMoleTime, currentMoleTime - 0.05f);
            if (score >= scoreToWin) Win();
            else ShowRandomMole();
        }
    }

    void UpdateScore()
    {
        if (scoreText != null) scoreText.text = $"{score} / {scoreToWin}";
        ShowProgress(score, scoreToWin);
    }

    /// <summary>CRT panels: the office mole pops out of the hole (its "Mole" child); older panels colour the hole.</summary>
    void SetMole(int index, bool up)
    {
        if (holeButtons == null || index < 0 || index >= holeButtons.Length || holeButtons[index] == null) return;
        Transform mole = holeButtons[index].transform.Find("Mole");
        if (mole == null)
        {
            SetHoleColor(index, up ? moleColor : holeColor);
            return;
        }
        mole.gameObject.SetActive(up);
        if (up && isActiveAndEnabled && mole.childCount > 0)
            StartCoroutine(RaiseMole((RectTransform)mole.GetChild(0)));
    }

    IEnumerator RaiseMole(RectTransform body)
    {
        const float time = 0.12f;
        float t = 0f;
        while (t < time && body != null)
        {
            t += Time.unscaledDeltaTime;
            float k = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / time), 2f);
            body.anchoredPosition = new Vector2(body.anchoredPosition.x, Mathf.Lerp(-110f, 0f, k));
            yield return null;
        }
        if (body != null) body.anchoredPosition = new Vector2(body.anchoredPosition.x, 0f);
    }
}
