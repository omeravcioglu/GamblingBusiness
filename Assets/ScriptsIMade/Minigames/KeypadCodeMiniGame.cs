using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Enter the correct code shown briefly (click the keypad or type the digits)
/// </summary>
public class KeypadCodeMiniGame : MiniGameBase
{

    [Header("Game Elements")]
    public Button[] numberButtons;   // index = digit (0-9)
    public TextMeshProUGUI codeDisplay;
    public TextMeshProUGUI inputDisplay;
    public Button clearButton;

    [Header("Settings")]
    public int codeLength = 4;
    public float displayTime = 2f;

    private string targetCode = "";
    private string inputCode = "";
    private bool canInput;
    // CRT panels: the code as digits over a draining bar, the typed slots, the keypad dimmed while the code shows
    private TMP_Text[] codeDigits;
    private GameObject codeShown, codeHidden, lockPill;
    private Image hideBar;
    private CanvasGroup keypadGroup;
    private RectTransform slots;

    void Awake()
    {
        gameName = "Keypad Code";
        timeLimit = 20f;
    }

    protected override void GameUpdate()
    {
        if (!canInput) return;

        // Keyboard: number row / numpad digits, Backspace or Delete clears
        for (int d = 0; d <= 9; d++)
        {
            if (Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha0 + d)) || Input.GetKeyDown((KeyCode)((int)KeyCode.Keypad0 + d)))
            {
                OnNumberPressed(d);
                return;
            }
        }
        if (Input.GetKeyDown(KeyCode.Backspace) || Input.GetKeyDown(KeyCode.Delete))
            OnClearPressed();
    }


    protected override IEnumerator SetupGame()
    {
        yield return StartCoroutine(base.SetupGame());
        AutoFindElements();
        if (codeDisplay == null && (codeDigits == null || codeDigits.Length == 0))
        {
            FailSetup("CodeDisplay not found");
            yield break;
        }

        canInput = false;
        inputCode = "";
        targetCode = "";
        codeLength = Mathf.Max(1, codeLength);
        SetupButtons();
        FixClearButtonOverlap();

        if (codeDisplay != null)
        {
            codeDisplay.text = new string('-', codeLength);
            codeDisplay.color = Color.white;
        }
        SetCodeState(false, null);
        UpdateInputDisplay();
    }

    protected override void OnPlayStarted()
    {
        StartCoroutine(ShowCode());
    }

    protected override string GetInstructionsText(string sceneInstructions)
    {
        return string.IsNullOrEmpty(sceneInstructions)
            ? CrisisText.Get("crisis.legacy.keypad_code.instructions")
            : CrisisText.Get("crisis.legacy.keypad_code.instructions_suffix", ("instructions", sceneInstructions));
    }

    void AutoFindElements()
    {
        if (codeDisplay == null) codeDisplay = FindElement<TextMeshProUGUI>("CodeDisplay");
        if (inputDisplay == null) inputDisplay = FindElement<TextMeshProUGUI>("InputDisplay");
        if (clearButton == null) clearButton = FindElement<Button>("ClearButton");
        if (codeShown == null) { var t = FindElement<RectTransform>("CodeShown"); if (t != null) codeShown = t.gameObject; }
        if (codeHidden == null) { var t = FindElement<RectTransform>("CodeHidden"); if (t != null) codeHidden = t.gameObject; }
        if (lockPill == null) { var t = FindElement<RectTransform>("LockPill"); if (t != null) lockPill = t.gameObject; }
        if (hideBar == null) hideBar = FindElement<Image>("HideBar");
        if (keypadGroup == null) keypadGroup = FindElement<CanvasGroup>("Keypad");
        if (slots == null) slots = FindElement<RectTransform>("Slots");
        if ((codeDigits == null || codeDigits.Length == 0) && codeShown != null)
        {
            var list = new List<TMP_Text>();
            for (int i = 0; i < 8; i++)
            {
                var t = codeShown.transform.Find("Digit" + i);
                if (t != null) list.Add(t.GetComponent<TMP_Text>());
            }
            codeDigits = list.ToArray();
        }

        // Find number buttons if not assigned (slot = digit, so a missing key can't shift the others)
        if (numberButtons == null || numberButtons.Length == 0)
        {
            var buttons = new Button[10];
            int found = 0;
            for (int i = 0; i <= 9; i++)
            {
                var btn = FindElement<Button>($"NumButton{i}");
                if (btn == null) btn = FindElement<Button>($"Button{i}");
                if (btn == null) btn = FindElement<Button>($"Num{i}");
                buttons[i] = btn;
                if (btn != null) found++;
            }
            if (found > 0) numberButtons = buttons;
        }

        if (numberButtons == null || numberButtons.Length == 0)
            Debug.LogWarning("[KeypadCode] No number buttons found - keyboard input only.");
    }

    void SetupButtons()
    {
        if (numberButtons != null)
        {
            for (int i = 0; i < numberButtons.Length; i++)
            {
                if (numberButtons[i] == null) continue;
                int num = i;
                numberButtons[i].onClick.RemoveAllListeners();
                numberButtons[i].onClick.AddListener(() => OnNumberPressed(num));
                var lit = numberButtons[i].transform.Find("Lit");
                if (lit != null) lit.gameObject.SetActive(false);
            }
        }
        if (clearButton != null)
        {
            clearButton.onClick.RemoveAllListeners();
            clearButton.onClick.AddListener(OnClearPressed);
        }
    }

    /// <summary>
    /// In the scene the CLEAR button sits on top of the 0 key. Move it next to the 0 key if they overlap.
    /// </summary>
    void FixClearButtonOverlap()
    {
        if (clearButton == null || numberButtons == null || numberButtons.Length == 0 || numberButtons[0] == null) return;
        var clearRect = clearButton.transform as RectTransform;
        var zeroRect = numberButtons[0].transform as RectTransform;
        if (clearRect == null || zeroRect == null || clearRect.parent == null) return;

        Rect clear = RectInSpace(clearRect, clearRect.parent);
        Rect zero = RectInSpace(zeroRect, clearRect.parent);
        if (!clear.Overlaps(zero)) return;

        const float gap = 15f;
        Vector2 target = new Vector2(zero.xMax + gap + clear.width * 0.5f, zero.center.y);
        clearRect.anchoredPosition += target - clear.center;
    }

    static Rect RectInSpace(RectTransform rt, Transform space)
    {
        var corners = new Vector3[4];
        rt.GetWorldCorners(corners);
        Vector3 a = space.InverseTransformPoint(corners[0]);
        Vector3 b = space.InverseTransformPoint(corners[2]);
        return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
    }

    IEnumerator ShowCode()
    {
        targetCode = "";
        for (int i = 0; i < codeLength; i++)
            targetCode += UnityEngine.Random.Range(0, 10).ToString();

        if (codeDisplay != null)
        {
            codeDisplay.text = targetCode;
            codeDisplay.color = Color.yellow;
        }

        SetCodeState(true, null);
        float left = displayTime;
        string hidesIn = CrisisText.Get("crisis.game.keypad_code.stat.hides_in");
        while (left > 0f)
        {
            if (hideBar != null) hideBar.fillAmount = displayTime > 0f ? left / displayTime : 0f;
            ShowStat(Seconds(left), hidesIn, true);
            yield return null;
            if (!isPlaying) yield break;
            left -= Time.unscaledDeltaTime;
        }
        if (!isPlaying) yield break;

        if (codeDisplay != null)
        {
            codeDisplay.text = new string('-', codeLength);
            codeDisplay.color = Color.white;
        }

        canInput = true;
        SetCodeState(false, null);
        ShowStat("1", CrisisText.Get("crisis.game.keypad_code.stat.tries_left"));
    }

    public void OnNumberPressed(int num)
    {
        if (!isPlaying || !canInput) return;

        inputCode += num.ToString();
        UpdateInputDisplay();
        PlaySound(successSound);
        FlashKey(num);

        if (inputCode.Length >= codeLength)
        {
            canInput = false;
            bool correct = inputCode == targetCode;
            SetCodeState(true, correct);

            // Reveal the real code either way
            if (codeDisplay != null)
            {
                codeDisplay.text = targetCode;
                codeDisplay.color = correct ? Color.green : Color.red;
            }

            if (correct)
                Win();
            else
                Lose(CrisisText.Result("wrong_code"));
        }
    }

    /// <summary>CRT panels: the key of the digit just entered (clicked or typed) lights up for a moment.</summary>
    void FlashKey(int num)
    {
        if (numberButtons == null || num < 0 || num >= numberButtons.Length || numberButtons[num] == null) return;
        var lit = numberButtons[num].transform.Find("Lit");
        if (lit != null) StartCoroutine(FlashKeyRoutine(lit.gameObject));
    }

    IEnumerator FlashKeyRoutine(GameObject lit)
    {
        lit.SetActive(true);
        float end = Time.unscaledTime + 0.15f;
        while (Time.unscaledTime < end) yield return null;
        lit.SetActive(false);
    }

    public void OnClearPressed()
    {
        if (!isPlaying || !canInput) return;
        inputCode = "";
        UpdateInputDisplay();
    }

    void UpdateInputDisplay()
    {
        if (inputDisplay != null)
            inputDisplay.text = inputCode + new string('_', Mathf.Max(0, codeLength - inputCode.Length));
        RenderSlots();
        ShowProgress(inputCode.Length, codeLength, -1, true);
    }

    /// <summary>
    /// CRT panels: the code shown (yellow while memorizing; green or red once answered) or hidden behind bars, the keypad
    /// dimmed and locked while it shows.
    /// </summary>
    void SetCodeState(bool show, bool? correct)
    {
        if (codeShown != null) codeShown.SetActive(show);
        if (codeHidden != null) codeHidden.SetActive(!show);
        bool memorizing = show && correct == null;
        if (lockPill != null) lockPill.SetActive(memorizing);
        if (keypadGroup != null) keypadGroup.alpha = memorizing ? 0.4f : 1f;
        if (hideBar != null) hideBar.transform.parent.gameObject.SetActive(memorizing);
        if (codeDigits == null) return;
        Color c = correct == null ? CrtScreenView.Yellow : (correct.Value ? CrtScreenView.Green : CrtScreenView.Red);
        for (int i = 0; i < codeDigits.Length; i++)
        {
            if (codeDigits[i] == null) continue;
            codeDigits[i].text = i < targetCode.Length ? targetCode[i].ToString() : "";
            codeDigits[i].color = c;
        }
    }

    /// <summary>CRT panels: what has been typed, one slot per digit (Char text, Bar underline: yellow under the next one).</summary>
    void RenderSlots()
    {
        if (slots == null) return;
        for (int i = 0; i < slots.childCount; i++)
        {
            var slot = slots.GetChild(i);
            bool used = i < codeLength;
            slot.gameObject.SetActive(used);
            if (!used) continue;
            bool typed = i < inputCode.Length, next = i == inputCode.Length;
            var ch = slot.Find("Char");
            if (ch != null) ch.GetComponent<TMP_Text>().text = typed ? inputCode[i].ToString() : "";
            var bar = slot.Find("Bar");
            if (bar != null) bar.gameObject.SetActive(!typed);
            var cursor = slot.Find("Cursor");
            if (cursor != null) cursor.gameObject.SetActive(next && canInput);
        }
    }
}
