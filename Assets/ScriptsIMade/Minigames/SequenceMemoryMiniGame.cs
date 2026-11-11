using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Watch the sequence and click in the same order
/// </summary>
public class SequenceMemoryMiniGame : MiniGameBase
{

    [Header("Game Elements")]
    public Button[] gridButtons;
    public Image[] gridImages;
    public TextMeshProUGUI statusText;

    [Header("Settings")]
    public int sequenceLength = 5;
    public float flashTime = 0.5f;
    public Color normalColor = Color.gray;
    public Color flashColor = Color.cyan;

    private List<int> sequence = new List<int>();
    private int playerIndex;
    private bool inputPhase;
    private GameObject watchPhase, watchDone, turnPhase;   // CRT: the two phases beside the grid

    void Awake()
    {
        gameName = "Sequence Memory";
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
        if (gridButtons == null || gridButtons.Length == 0)
        {
            FailSetup("Grid buttons not found");
            yield break;
        }

        inputPhase = false;
        playerIndex = 0;
        sequence.Clear();
        for (int i = 0; i < Mathf.Max(1, sequenceLength); i++)
            sequence.Add(UnityEngine.Random.Range(0, gridButtons.Length));
        SetupButtons();
        SetStatus(CrisisText.Get("crisis.status.get_ready"));
        SetPhase(false);
        ShowProgress(0, sequence.Count);
    }

    protected override void OnPlayStarted()
    {
        StartCoroutine(ShowSequence());
    }

    void AutoFindElements()
    {
        Transform gameArea = transform.Find("GameArea");
        if (gameArea == null) return;
        if (statusText == null) { var t = gameArea.Find("StatusText"); if (t) statusText = t.GetComponent<TextMeshProUGUI>(); }
        if (watchPhase == null) { var t = gameArea.Find("WatchPhase"); if (t) watchPhase = t.gameObject; }
        if (watchDone == null) { var t = gameArea.Find("WatchPhase/Done"); if (t) watchDone = t.gameObject; }
        if (turnPhase == null) { var t = gameArea.Find("TurnPhase"); if (t) turnPhase = t.gameObject; }
        if (gridButtons == null || gridButtons.Length == 0)
        {
            List<Button> btns = new List<Button>();
            List<Image> imgs = new List<Image>();
            for (int i = 0; i < 9; i++)
            {
                var t = gameArea.Find($"GridButton{i}");
                if (t == null) continue;
                var btn = t.GetComponent<Button>();
                if (btn == null) continue;
                btns.Add(btn);
                imgs.Add(t.GetComponent<Image>()); // parallel to the buttons (may be null)
            }
            gridButtons = btns.ToArray();
            gridImages = imgs.ToArray();
        }

        // Buttons assigned in the Inspector without matching images: use each button's own Image
        if (gridButtons != null && (gridImages == null || gridImages.Length != gridButtons.Length))
        {
            gridImages = new Image[gridButtons.Length];
            for (int i = 0; i < gridButtons.Length; i++)
                gridImages[i] = gridButtons[i] != null ? gridButtons[i].GetComponent<Image>() : null;
        }
    }

    void SetupButtons()
    {
        for (int i = 0; i < gridButtons.Length; i++)
        {
            if (gridButtons[i] != null)
            {
                int idx = i;
                gridButtons[i].onClick.RemoveAllListeners();
                gridButtons[i].onClick.AddListener(() => OnGridClick(idx));
            }
            SetTileColor(i, normalColor);
        }
    }

    IEnumerator ShowSequence()
    {
        inputPhase = false;
        SetStatus(CrisisText.Get("crisis.game.sequence_memory.watch"));
        SetPhase(false);
        yield return new WaitForSecondsRealtime(0.5f);

        foreach (int idx in sequence)
        {
            SetTileColor(idx, flashColor);
            yield return new WaitForSecondsRealtime(flashTime);
            SetTileColor(idx, normalColor);
            yield return new WaitForSecondsRealtime(0.2f);
        }
        if (!isPlaying) yield break;

        inputPhase = true;
        playerIndex = 0;
        SetStatus(CrisisText.Get("crisis.game.sequence_memory.your_turn"));
        SetPhase(true);
    }

    void OnGridClick(int idx)
    {
        if (!isPlaying || !inputPhase || playerIndex >= sequence.Count) return;

        if (idx == sequence[playerIndex])
        {
            playerIndex++;
            ShowProgress(playerIndex, sequence.Count);
            PlaySound(successSound);
            StartCoroutine(FlashButton(idx));
            if (playerIndex >= sequence.Count)
            {
                inputPhase = false;
                Win();
            }
        }
        else
        {
            inputPhase = false;
            PlaySound(failSound);
            SetTileColor(sequence[playerIndex], CrtScreenView.Red); // the tile that was next
            Lose(CrisisText.Result("wrong_tile"));
        }
    }

    IEnumerator FlashButton(int idx)
    {
        SetTileColor(idx, flashColor);
        yield return new WaitForSecondsRealtime(0.15f);
        SetTileColor(idx, normalColor);
    }

    void SetTileColor(int idx, Color color)
    {
        if (gridImages != null && idx >= 0 && idx < gridImages.Length && gridImages[idx] != null)
            gridImages[idx].color = color;
            SetTileGlow(idx, color == flashColor);
    }

    void SetStatus(string text)
    {
        if (statusText != null) statusText.text = text;
    }

    /// <summary>CRT panels: "Watch..." then "Your turn!" beside the grid.</summary>
    void SetPhase(bool turn)
    {
        if (turnPhase != null) turnPhase.SetActive(turn);
        if (watchDone != null) watchDone.SetActive(turn);
        if (watchPhase != null)
        {
            var group = watchPhase.GetComponent<CanvasGroup>();
            if (group != null) group.alpha = turn ? 0.6f : 1f;
        }
    }

    /// <summary>CRT panels: a flashing tile glows and its frame lights up.</summary>
    void SetTileGlow(int idx, bool on)
    {
        if (gridButtons == null || idx < 0 || idx >= gridButtons.Length || gridButtons[idx] == null) return;
        var glow = gridButtons[idx].transform.Find("Glow");
        if (glow != null) glow.gameObject.SetActive(on);
        var frame = gridButtons[idx].transform.Find("Frame");
        if (frame != null)
        {
            var shape = frame.GetComponent<UIShape>();
            if (shape != null) shape.color = on ? flashColor : CrtScreenView.Lit(CrtScreenView.Blue, 0.5f);
        }
    }
}
