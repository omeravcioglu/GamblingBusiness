using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Mash a button as fast as possible
/// </summary>
public class ButtonMashMiniGame : MiniGameBase
{

    [Header("Game Elements")]
    public TextMeshProUGUI countText;
    public Image progressBar;
    public RectTransform mashButton;

    [Header("Settings")]
    public int targetPresses = 40;
    public float buttonPunchScale = 0.9f;

    private int presses;
    private Vector3 originalScale = Vector3.one;
    private bool scaleCaptured;

    void Awake()
    {
        gameName = "Button Mash";
        timeLimit = 10f;
    }

    protected override void GameUpdate()
    {

        if (Input.GetKeyDown(KeyCode.Space))
        {
            presses++;
            UpdateUI();
            PlaySound(successSound);
            StartCoroutine(PunchButton());

            if (presses >= targetPresses)
                Win();
        }
    }


    protected override IEnumerator SetupGame()
    {
        yield return StartCoroutine(base.SetupGame());
        AutoFindElements();
        presses = 0;

        // Remember the real size once; a punch cut short by the game ending must not shrink it for good
        if (mashButton != null)
        {
            if (!scaleCaptured)
            {
                originalScale = mashButton.localScale;
                scaleCaptured = true;
            }
            mashButton.localScale = originalScale;
        }

        // Filled bar (the base gives it a sprite so the fill shows)
        if (progressBar != null) progressBar.type = Image.Type.Filled;

        UpdateUI();
    }

    void AutoFindElements()
    {
        if (countText == null) countText = FindElement<TextMeshProUGUI>("CountText");
        if (countText == null) countText = FindElement<TextMeshProUGUI>("Count");
        if (progressBar == null) progressBar = FindElement<Image>("ProgressBar");
        if (progressBar == null) progressBar = FindElement<Image>("Progress");
        if (mashButton == null) mashButton = FindElement<RectTransform>("MashButton");
        if (mashButton == null) mashButton = FindElement<RectTransform>("Button");
    }

    void UpdateUI()
    {
        int target = Mathf.Max(1, targetPresses);
        if (countText != null) countText.text = $"<nobr>{presses}<size=50%>/{target}</size></nobr>";
        if (progressBar != null) progressBar.fillAmount = Mathf.Clamp01((float)presses / target);
        ShowProgress(presses, target);
        ShowStat(Mathf.Max(0, target - presses).ToString());
    }

    IEnumerator PunchButton()
    {
        if (mashButton == null) yield break;
        mashButton.localScale = originalScale * buttonPunchScale;
        yield return new WaitForSecondsRealtime(0.05f);
        mashButton.localScale = originalScale;
    }

    protected override void OnGameEnded()
    {
        if (mashButton != null && scaleCaptured) mashButton.localScale = originalScale;
    }
}
