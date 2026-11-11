using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Press the keys shown on screen as fast as possible
/// </summary>
public class RapidKeysMiniGame : MiniGameBase
{

    [Header("Game Elements")]
    public TextMeshProUGUI keyDisplay;
    public TextMeshProUGUI scoreText;

    [Header("Settings")]
    public int keysToPress = 15;

    private int pressed;
    private KeyCode currentKey = KeyCode.None;
    private KeyCode[] possibleKeys = { KeyCode.A, KeyCode.S, KeyCode.D, KeyCode.F, KeyCode.J, KeyCode.K, KeyCode.L };
    private Transform keyRow;   // CRT: the seven keys, the one to press lit

    void Awake()
    {
        gameName = "Rapid Keys";
        timeLimit = 20f;
    }

    protected override void GameUpdate()
    {
        if (currentKey == KeyCode.None) return;

        if (Input.GetKeyDown(currentKey))
        {
            pressed++;
            UpdateScore();
            PlaySound(successSound);
            if (pressed >= keysToPress) Win();
            else ShowNextKey();
        }
        else
        {
            // Check if wrong key pressed
            foreach (var key in possibleKeys)
            {
                if (key != currentKey && Input.GetKeyDown(key))
                {
                    PlaySound(failSound);
                    Lose(CrisisText.Result("wrong_key"));
                    break;
                }
            }
        }
    }


    protected override IEnumerator SetupGame()
    {
        yield return StartCoroutine(base.SetupGame());
        AutoFindElements();
        if (keyDisplay == null)
        {
            FailSetup("KeyDisplay not found");
            yield break;
        }

        pressed = 0;
        currentKey = KeyCode.None;
        keyDisplay.text = "?";
        LightKey(KeyCode.None);
        UpdateScore();
    }

    protected override void OnPlayStarted()
    {
        ShowNextKey();
    }

    void AutoFindElements()
    {
        Transform gameArea = transform.Find("GameArea");
        if (gameArea == null) return;
        if (keyDisplay == null) { var t = gameArea.Find("KeyDisplay"); if (t) keyDisplay = t.GetComponent<TextMeshProUGUI>(); }
        if (scoreText == null) { var t = gameArea.Find("ScoreText"); if (t) scoreText = t.GetComponent<TextMeshProUGUI>(); }
        if (keyRow == null) keyRow = gameArea.Find("KeyRow");
    }

    void ShowNextKey()
    {
        // A different key each time, so the change is always visible
        KeyCode next = possibleKeys[UnityEngine.Random.Range(0, possibleKeys.Length)];
        if (next == currentKey && possibleKeys.Length > 1)
        {
            int index = Array.IndexOf(possibleKeys, next);
            next = possibleKeys[(index + UnityEngine.Random.Range(1, possibleKeys.Length)) % possibleKeys.Length];
        }
        currentKey = next;
        if (keyDisplay != null) keyDisplay.text = currentKey.ToString();
        LightKey(currentKey);
    }

    void UpdateScore()
    {
        if (scoreText != null) scoreText.text = $"{pressed} / {keysToPress}";
        ShowProgress(pressed, keysToPress);
    }

    /// <summary>CRT panels: light the key to press in the row (Key_A ... Key_L, each with Lit and Idle children).</summary>
    void LightKey(KeyCode key)
    {
        if (keyRow == null) return;
        foreach (Transform k in keyRow)
        {
            bool lit = k.name == "Key_" + key;
            var litGo = k.Find("Lit");
            var idleGo = k.Find("Idle");
            if (litGo != null) litGo.gameObject.SetActive(lit);
            if (idleGo != null) idleGo.gameObject.SetActive(!lit);
        }
    }
}
