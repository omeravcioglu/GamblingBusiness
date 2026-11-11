using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Call heads or tails: get enough right before you run out of lives. The keys, and the letter on the coin, stay H and T
/// in every language (English mnemonics); the words HEADS / TAILS and the feedback come from the Crisis table.
/// </summary>
public class CoinFlipMiniGame : MiniGameBase
{

    [Header("Game Elements")]
    public TextMeshProUGUI scoreText;
    public TextMeshProUGUI feedbackText;   // GameArea/ResultText (per-flip feedback, not the base result text)
    public Image coinImage;

    [Header("Settings")]
    public int winsNeeded = 3;
    [Tooltip("Wrong calls allowed before losing (the last one loses)")]
    public int lives = 3;
    public float flipTime = 0.45f;
    public Color headsColor = Color.yellow;
    public Color tailsColor = Color.gray;

    private int wins;
    private int livesLeft;
    private bool flipping;
    private Vector3 coinScale = Vector3.one;
    private bool coinScaleCaptured;
    // CRT panels: the letter on the coin, and the HEADS / TAILS tile lit while that call spins
    private TMP_Text coinLetter;
    private Transform headsTile, tailsTile;
    // CRT panels: motion arcs while the coin spins, and the coin's edge (its thickness) behind it
    private GameObject spinLines;
    private Graphic coinEdge, coinGlow;

    void Awake()
    {
        gameName = "Coin Flip";
        timeLimit = 0f;
    }

    protected override void GameUpdate()
    {
        if (flipping) return;

        if (Input.GetKeyDown(KeyCode.H))
        {
            StartCoroutine(Flip(true));
        }
        else if (Input.GetKeyDown(KeyCode.T))
        {
            StartCoroutine(Flip(false));
        }
    }


    protected override IEnumerator SetupGame()
    {
        yield return StartCoroutine(base.SetupGame());
        AutoFindElements();
        wins = 0;
        livesLeft = Mathf.Max(1, lives);
        flipping = false;

        if (coinImage != null)
        {
            if (!coinScaleCaptured)
            {
                coinScale = coinImage.rectTransform.localScale;
                coinScaleCaptured = true;
            }
            coinImage.rectTransform.localScale = coinScale;
            PaintCoin(headsColor);
        }
        SpinLook(false);
        SetFeedback(CrisisText.Get("crisis.game.coin_flip.make_your_call"), CrtScreenView.Paper);
        if (coinLetter != null) coinLetter.text = "H";
        LightTile(null);
        UpdateScore();
    }

    void AutoFindElements()
    {
        Transform gameArea = transform.Find("GameArea");
        if (gameArea == null) return;
        if (feedbackText == null) { var t = gameArea.Find("ResultText"); if (t) feedbackText = t.GetComponent<TextMeshProUGUI>(); }
        if (feedbackText == null) { var t = gameArea.Find("FeedbackText"); if (t) feedbackText = t.GetComponent<TextMeshProUGUI>(); }
        if (scoreText == null) { var t = gameArea.Find("ScoreText"); if (t) scoreText = t.GetComponent<TextMeshProUGUI>(); }
        if (coinImage == null) { var t = gameArea.Find("CoinImage"); if (t) coinImage = t.GetComponent<Image>(); }
        if (coinLetter == null) { var t = gameArea.Find("CoinImage/Letter"); if (t) coinLetter = t.GetComponent<TMP_Text>(); }
        if (headsTile == null) headsTile = gameArea.Find("HeadsTile");
        if (tailsTile == null) tailsTile = gameArea.Find("TailsTile");
        if (spinLines == null) { var t = gameArea.Find("SpinLines"); if (t) spinLines = t.gameObject; }
        if (coinEdge == null) { var t = gameArea.Find("CoinEdge"); if (t) coinEdge = t.GetComponent<Graphic>(); }
        if (coinGlow == null) { var t = gameArea.Find("CoinImage/Glow"); if (t) coinGlow = t.GetComponent<Graphic>(); }
    }

    IEnumerator Flip(bool guessedHeads)
    {
        flipping = true;
        SetFeedback(CrisisText.Get("crisis.game.coin_flip.you_called",
            ("side", guessedHeads ? "<color=#FFE14D>" + SideWord(true) + "</color>" : "<color=#BFE3F5>" + SideWord(false) + "</color>")), CrtScreenView.Paper);
        LightTile(guessedHeads);

        // Spin the coin for a moment
        SpinLook(true);
        float elapsed = 0f;
        while (elapsed < flipTime)
        {
            elapsed += Time.unscaledDeltaTime;
            if (coinImage != null)
            {
                float spin = Mathf.Cos(elapsed * 30f);
                coinImage.rectTransform.localScale = new Vector3(coinScale.x * Mathf.Max(0.05f, Mathf.Abs(spin)), coinScale.y, coinScale.z);
                if (coinEdge != null) coinEdge.rectTransform.localScale = coinImage.rectTransform.localScale;
                PaintCoin(spin >= 0f ? headsColor : tailsColor);
                if (coinLetter != null) coinLetter.text = spin >= 0f ? "H" : "T";
            }
            yield return null;
        }
        if (coinImage != null) coinImage.rectTransform.localScale = coinScale;
        SpinLook(false);
        if (!isPlaying) yield break;

        bool isHeads = UnityEngine.Random.value < 0.5f;
        string result = SideWord(isHeads);
        PaintCoin(isHeads ? headsColor : tailsColor);
        if (coinLetter != null) coinLetter.text = isHeads ? "H" : "T";

        if (guessedHeads == isHeads)
        {
            wins++;
            UpdateScore();
            PlaySound(successSound);
            SetFeedback(CrisisText.Get("crisis.game.coin_flip.correct", ("side", result)), CrtScreenView.Green);
            if (wins >= winsNeeded)
            {
                Win();
                yield break;
            }
            StartCoroutine(FlashEffect(successFlashColor));
        }
        else
        {
            livesLeft--;
            UpdateScore();
            PlaySound(failSound);
            SetFeedback(CrisisText.Get("crisis.game.coin_flip.wrong", ("side", result)), CrtScreenView.Red);
            if (livesLeft <= 0)
            {
                Lose();
                yield break;
            }
            StartCoroutine(FlashEffect(failFlashColor));
        }
        flipping = false;
        LightTile(null);
    }

    /// <summary>HEADS or TAILS in the current language (the keys stay H and T).</summary>
    static string SideWord(bool heads) => CrisisText.Get(heads ? "crisis.game.coin_flip.heads" : "crisis.game.coin_flip.tails");

    void SetFeedback(string text, Color color)
    {
        if (feedbackText == null) return;
        feedbackText.text = text;
        feedbackText.color = color;
        feedbackText.gameObject.SetActive(true);
    }

    void UpdateScore()
    {
        if (scoreText != null) scoreText.text = $"{wins} / {winsNeeded}\n<size=60%>" + CrisisText.Get("crisis.legacy.lives", ("count", Mathf.Max(0, livesLeft))) + "</size>";
        ShowProgress(wins, winsNeeded);
        ShowLives(Mathf.Max(0, livesLeft), Mathf.Max(1, lives));
    }

    protected override void OnGameEnded()
    {
        if (coinImage != null && coinScaleCaptured) coinImage.rectTransform.localScale = coinScale;
        SpinLook(false);
    }

    void PaintCoin(Color c)
    {
        if (coinImage != null) coinImage.color = c;
        if (coinEdge != null) coinEdge.color = CrtScreenView.Lit(c, 0.38f);
        if (coinGlow != null) coinGlow.color = CrtScreenView.Lit(c, 0.7f);
    }

    /// <summary>CRT panels: motion arcs on while it spins; the edge back to full width when it stops.</summary>
    void SpinLook(bool spinning)
    {
        if (spinLines != null) spinLines.SetActive(spinning);
        if (!spinning && coinEdge != null) coinEdge.rectTransform.localScale = coinScaleCaptured ? coinScale : Vector3.one;
    }

    /// <summary>CRT panels: the tile of the side that was called lights up (null = both idle).</summary>
    void LightTile(bool? heads)
    {
        SetTile(headsTile, heads == true);
        SetTile(tailsTile, heads == false);
    }

    static void SetTile(Transform tile, bool lit)
    {
        if (tile == null) return;
        var on = tile.Find("Lit");
        var off = tile.Find("Idle");
        if (on != null) on.gameObject.SetActive(lit);
        if (off != null) off.gameObject.SetActive(!lit);
    }
}
