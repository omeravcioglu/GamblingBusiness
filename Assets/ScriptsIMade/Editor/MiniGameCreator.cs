#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using TMPro;
using System.IO;
using System.Collections.Generic;

/// <summary>
/// Editor tool to create minigame scripts and complete UI panels
/// </summary>
public class MiniGameCreator : EditorWindow
{
    private class MiniGameTemplate
    {
        public string name;
        public string description;
        public string instructions;
        public float timeLimit;
        public string category;
        public string gameLogic;
        public string customMethods;
        public string customFields;
        public System.Action<GameObject, MiniGameTemplate> createUI;
    }
    
    private static List<MiniGameTemplate> templates;
    
    private Vector2 scrollPosition;
    private string searchFilter = "";
    private string categoryFilter = "All";
    private string[] categories = { "All", "Timing", "Reaction", "Memory", "Puzzle", "Skill", "Quick" };
    
    [MenuItem("Tools/Minigame Creator")]
    public static void ShowWindow()
    {
        InitTemplates();
        var window = GetWindow<MiniGameCreator>("Minigame Creator");
        window.minSize = new Vector2(450, 600);
    }
    
    static void InitTemplates()
    {
        templates = new List<MiniGameTemplate>
        {
            // === TIMING GAMES ===
            new MiniGameTemplate
            {
                name = "Arrow Timing",
                description = "Press Space when the rotating arrow is in the target zone",
                instructions = "Press SPACE when arrow is in the green zone!",
                timeLimit = 0,
                category = "Timing",
                customFields = @"
    [Header(""Game Elements"")]
    public RectTransform arrowPivot;
    public Image targetZone;
    public TextMeshProUGUI scoreText;
    
    [Header(""Settings"")]
    public int hitsRequired = 5;
    public float startSpeed = 120f;
    public float speedIncrease = 20f;
    public float startTargetSize = 60f;
    public float targetShrink = 8f;
    
    private float currentAngle;
    private float currentSpeed;
    private float targetStart;
    private float targetSize;
    private int hits;",
                gameLogic = @"
        currentAngle += currentSpeed * Time.deltaTime;
        if (currentAngle >= 360f) currentAngle -= 360f;
        if (arrowPivot != null)
            arrowPivot.localRotation = Quaternion.Euler(0, 0, -currentAngle);
        
        if (Input.GetKeyDown(KeyCode.Space))
        {
            if (IsInZone())
            {
                hits++;
                UpdateScore();
                PlaySound(successSound);
                StartCoroutine(FlashEffect(successFlashColor));
                if (hits >= hitsRequired) Win();
                else NextRound();
            }
            else Lose();
        }",
                customMethods = @"
    protected override IEnumerator SetupGame()
    {
        yield return StartCoroutine(base.SetupGame());
        hits = 0;
        currentSpeed = startSpeed;
        targetSize = startTargetSize;
        currentAngle = 0;
        targetStart = Random.Range(45f, 315f);
        UpdateTargetVisual();
        UpdateScore();
    }
    
    bool IsInZone()
    {
        float end = targetStart + targetSize;
        if (end > 360f)
            return currentAngle >= targetStart || currentAngle <= (end - 360f);
        return currentAngle >= targetStart && currentAngle <= end;
    }
    
    void NextRound()
    {
        currentSpeed += speedIncrease;
        targetSize = Mathf.Max(20f, targetSize - targetShrink);
        targetStart = Random.Range(0f, 360f);
        UpdateTargetVisual();
    }
    
    void UpdateTargetVisual()
    {
        if (targetZone == null) return;
        targetZone.fillAmount = targetSize / 360f;
        targetZone.rectTransform.localRotation = Quaternion.Euler(0, 0, -targetStart);
    }
    
    void UpdateScore()
    {
        if (scoreText != null) scoreText.text = $""{hits} / {hitsRequired}"";
    }",
                createUI = CreateArrowTimingUI
            },
            
            new MiniGameTemplate
            {
                name = "Stop The Bar",
                description = "Stop a moving bar in the target zone",
                instructions = "Press SPACE to stop the bar in the green zone!",
                timeLimit = 0,
                category = "Timing",
                customFields = @"
    [Header(""Game Elements"")]
    public RectTransform movingBar;
    public RectTransform targetZone;
    public RectTransform track;
    public TextMeshProUGUI scoreText;
    
    [Header(""Settings"")]
    public int successesNeeded = 3;
    public float startSpeed = 300f;
    public float speedIncrease = 75f;
    
    private float barPosition;
    private float direction = 1f;
    private float currentSpeed;
    private int successes;
    private float trackWidth = 400f;",
                gameLogic = @"
        barPosition += currentSpeed * direction * Time.deltaTime;
        if (Mathf.Abs(barPosition) > trackWidth)
            direction *= -1f;
        if (movingBar != null)
            movingBar.anchoredPosition = new Vector2(barPosition, 0);
        
        if (Input.GetKeyDown(KeyCode.Space))
        {
            if (IsBarInTarget())
            {
                successes++;
                UpdateScore();
                PlaySound(successSound);
                StartCoroutine(FlashEffect(successFlashColor));
                if (successes >= successesNeeded) Win();
                else { currentSpeed += speedIncrease; RandomizeTarget(); }
            }
            else Lose();
        }",
                customMethods = @"
    protected override IEnumerator SetupGame()
    {
        yield return StartCoroutine(base.SetupGame());
        successes = 0;
        currentSpeed = startSpeed;
        barPosition = 0;
        direction = 1f;
        if (track != null) trackWidth = track.sizeDelta.x / 2f - 20f;
        RandomizeTarget();
        UpdateScore();
    }
    
    void RandomizeTarget()
    {
        if (targetZone != null)
            targetZone.anchoredPosition = new Vector2(Random.Range(-trackWidth + 50, trackWidth - 50), 0);
    }
    
    bool IsBarInTarget()
    {
        if (targetZone == null) return false;
        float targetX = targetZone.anchoredPosition.x;
        float targetWidth = targetZone.sizeDelta.x / 2f;
        return Mathf.Abs(barPosition - targetX) < targetWidth;
    }
    
    void UpdateScore()
    {
        if (scoreText != null) scoreText.text = $""{successes} / {successesNeeded}"";
    }",
                createUI = CreateStopTheBarUI
            },
            
            // === REACTION GAMES ===
            new MiniGameTemplate
            {
                name = "Color Match",
                description = "Press the key matching the shown color",
                instructions = "Press R G B Y to match the color!",
                timeLimit = 25f,
                category = "Reaction",
                customFields = @"
    [Header(""Game Elements"")]
    public Image colorDisplay;
    public TextMeshProUGUI keyPrompt;
    public TextMeshProUGUI scoreText;
    public Image[] keyIndicators;
    
    [Header(""Settings"")]
    public int matchesNeeded = 10;
    public float startReactionTime = 2f;
    public float reactionDecrease = 0.1f;
    
    private int matches;
    private KeyCode currentKey;
    private float reactionTimer;
    private float maxReactionTime;
    private Color[] colors = { Color.red, Color.green, Color.blue, Color.yellow };
    private KeyCode[] keys = { KeyCode.R, KeyCode.G, KeyCode.B, KeyCode.Y };",
                gameLogic = @"
        reactionTimer -= Time.deltaTime;
        
        if (reactionTimer <= 0)
        {
            PlaySound(failSound);
            Lose();
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
                else Lose();
                break;
            }
        }",
                customMethods = @"
    protected override IEnumerator SetupGame()
    {
        yield return StartCoroutine(base.SetupGame());
        matches = 0;
        maxReactionTime = startReactionTime;
        UpdateScore();
        SetupKeyIndicators();
        ShowNextColor();
    }
    
    void SetupKeyIndicators()
    {
        if (keyIndicators == null) return;
        for (int i = 0; i < keyIndicators.Length && i < colors.Length; i++)
            keyIndicators[i].color = colors[i];
    }
    
    void ShowNextColor()
    {
        int index = Random.Range(0, colors.Length);
        if (colorDisplay != null) colorDisplay.color = colors[index];
        currentKey = keys[index];
        if (keyPrompt != null) keyPrompt.text = currentKey.ToString();
        maxReactionTime = Mathf.Max(0.5f, maxReactionTime - reactionDecrease);
        reactionTimer = maxReactionTime;
    }
    
    void UpdateScore()
    {
        if (scoreText != null) scoreText.text = $""{matches} / {matchesNeeded}"";
    }",
                createUI = CreateColorMatchUI
            },
            
            new MiniGameTemplate
            {
                name = "Whack A Mole",
                description = "Click moles as they pop up from holes",
                instructions = "Click the moles before they hide!",
                timeLimit = 30f,
                category = "Reaction",
                customFields = @"
    [Header(""Game Elements"")]
    public Button[] holeButtons;
    public Image[] holeImages;
    public TextMeshProUGUI scoreText;
    
    [Header(""Settings"")]
    public int scoreToWin = 15;
    public float startMoleTime = 1.2f;
    public float minMoleTime = 0.4f;
    public Color holeColor = Color.gray;
    public Color moleColor = Color.green;
    
    private int score;
    private int activeMole = -1;
    private float moleTimer;
    private float currentMoleTime;",
                gameLogic = @"
        moleTimer -= Time.deltaTime;
        if (moleTimer <= 0)
        {
            HideMole();
            ShowRandomMole();
        }",
                customMethods = @"
    protected override IEnumerator SetupGame()
    {
        yield return StartCoroutine(base.SetupGame());
        score = 0;
        currentMoleTime = startMoleTime;
        UpdateScore();
        SetupHoles();
        ShowRandomMole();
    }
    
    void SetupHoles()
    {
        for (int i = 0; i < holeButtons.Length; i++)
        {
            int index = i;
            holeButtons[i].onClick.RemoveAllListeners();
            holeButtons[i].onClick.AddListener(() => OnHoleClicked(index));
            if (holeImages != null && i < holeImages.Length)
                holeImages[i].color = holeColor;
        }
    }
    
    void ShowRandomMole()
    {
        activeMole = Random.Range(0, holeButtons.Length);
        if (holeImages != null && activeMole < holeImages.Length)
            holeImages[activeMole].color = moleColor;
        moleTimer = currentMoleTime;
    }
    
    void HideMole()
    {
        if (activeMole >= 0 && holeImages != null && activeMole < holeImages.Length)
            holeImages[activeMole].color = holeColor;
        activeMole = -1;
    }
    
    public void OnHoleClicked(int index)
    {
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
        if (scoreText != null) scoreText.text = $""{score} / {scoreToWin}"";
    }",
                createUI = CreateWhackAMoleUI
            },
            
            // === MEMORY GAMES ===
            new MiniGameTemplate
            {
                name = "Simon Says",
                description = "Repeat the pattern of colors/sounds",
                instructions = "Watch the pattern, then repeat it!",
                timeLimit = 0,
                category = "Memory",
                customFields = @"
    [Header(""Game Elements"")]
    public Button[] colorButtons;
    public Image[] buttonImages;
    public TextMeshProUGUI statusText;
    
    [Header(""Settings"")]
    public int patternLengthToWin = 5;
    public float flashDuration = 0.4f;
    public float pauseDuration = 0.2f;
    public Color[] buttonColors = { Color.red, Color.green, Color.blue, Color.yellow };
    public Color flashColor = Color.white;
    
    private List<int> pattern = new List<int>();
    private int playerIndex;
    private bool showingPattern;
    private bool canInput;",
                gameLogic = @"
        // Input handled by button clicks",
                customMethods = @"
    protected override IEnumerator SetupGame()
    {
        yield return StartCoroutine(base.SetupGame());
        pattern.Clear();
        playerIndex = 0;
        showingPattern = false;
        canInput = false;
        SetupButtons();
        pattern.Add(Random.Range(0, colorButtons.Length));
        StartCoroutine(ShowPattern());
    }
    
    void SetupButtons()
    {
        for (int i = 0; i < colorButtons.Length; i++)
        {
            int index = i;
            colorButtons[i].onClick.RemoveAllListeners();
            colorButtons[i].onClick.AddListener(() => OnColorClicked(index));
            if (buttonImages != null && i < buttonImages.Length)
                buttonImages[i].color = buttonColors[i % buttonColors.Length];
        }
    }
    
    IEnumerator ShowPattern()
    {
        showingPattern = true;
        canInput = false;
        if (statusText != null) statusText.text = ""Watch..."";
        yield return new WaitForSeconds(0.5f);
        
        foreach (int index in pattern)
        {
            if (buttonImages != null && index < buttonImages.Length)
            {
                buttonImages[index].color = flashColor;
                PlaySound(successSound);
                yield return new WaitForSeconds(flashDuration);
                buttonImages[index].color = buttonColors[index % buttonColors.Length];
                yield return new WaitForSeconds(pauseDuration);
            }
        }
        
        showingPattern = false;
        canInput = true;
        playerIndex = 0;
        if (statusText != null) statusText.text = ""Your turn!"";
    }
    
    public void OnColorClicked(int index)
    {
        if (!canInput || showingPattern) return;
        
        // Flash the button
        StartCoroutine(FlashButton(index));
        
        if (index == pattern[playerIndex])
        {
            playerIndex++;
            PlaySound(successSound);
            
            if (playerIndex >= pattern.Count)
            {
                if (pattern.Count >= patternLengthToWin)
                {
                    Win();
                }
                else
                {
                    pattern.Add(Random.Range(0, colorButtons.Length));
                    StartCoroutine(ShowPattern());
                }
            }
        }
        else
        {
            Lose();
        }
    }
    
    IEnumerator FlashButton(int index)
    {
        if (buttonImages != null && index < buttonImages.Length)
        {
            buttonImages[index].color = flashColor;
            yield return new WaitForSeconds(0.15f);
            buttonImages[index].color = buttonColors[index % buttonColors.Length];
        }
    }",
                createUI = CreateSimonSaysUI
            },
            
            new MiniGameTemplate
            {
                name = "Number Sequence",
                description = "Remember and type a sequence of numbers",
                instructions = "Memorize the numbers, then type them!",
                timeLimit = 0,
                category = "Memory",
                customFields = @"
    [Header(""Game Elements"")]
    public TextMeshProUGUI displayText;
    public TMP_InputField inputField;
    public TextMeshProUGUI levelText;
    
    [Header(""Settings"")]
    public int startLength = 3;
    public int winLength = 7;
    public float displayTime = 0.5f;
    
    private string sequence;
    private int currentLength;
    private bool inputPhase;",
                gameLogic = @"
        if (inputPhase && Input.GetKeyDown(KeyCode.Return))
        {
            CheckAnswer();
        }",
                customMethods = @"
    protected override IEnumerator SetupGame()
    {
        yield return StartCoroutine(base.SetupGame());
        currentLength = startLength;
        inputPhase = false;
        if (inputField != null) inputField.gameObject.SetActive(false);
        UpdateLevel();
        StartCoroutine(ShowSequence());
    }
    
    IEnumerator ShowSequence()
    {
        inputPhase = false;
        if (inputField != null) inputField.gameObject.SetActive(false);
        
        sequence = """";
        for (int i = 0; i < currentLength; i++)
            sequence += Random.Range(0, 10).ToString();
        
        if (displayText != null)
        {
            displayText.text = sequence;
            displayText.fontSize = 72;
        }
        
        yield return new WaitForSeconds(1f + currentLength * displayTime);
        
        if (displayText != null)
        {
            displayText.text = ""Type it!"";
            displayText.fontSize = 36;
        }
        
        inputPhase = true;
        if (inputField != null)
        {
            inputField.gameObject.SetActive(true);
            inputField.text = """";
            inputField.ActivateInputField();
        }
    }
    
    void CheckAnswer()
    {
        if (inputField == null) return;
        
        if (inputField.text == sequence)
        {
            currentLength++;
            UpdateLevel();
            PlaySound(successSound);
            StartCoroutine(FlashEffect(successFlashColor));
            
            if (currentLength > winLength)
                Win();
            else
                StartCoroutine(ShowSequence());
        }
        else
        {
            Lose();
        }
    }
    
    void UpdateLevel()
    {
        if (levelText != null)
            levelText.text = $""Level: {currentLength - startLength + 1}"";
    }",
                createUI = CreateNumberSequenceUI
            },
            
            // === PUZZLE GAMES ===
            new MiniGameTemplate
            {
                name = "Keypad Code",
                description = "Enter the correct code shown briefly",
                instructions = "Remember and enter the code!",
                timeLimit = 20f,
                category = "Puzzle",
                customFields = @"
    [Header(""Game Elements"")]
    public Button[] numberButtons;
    public TextMeshProUGUI codeDisplay;
    public TextMeshProUGUI inputDisplay;
    public Button clearButton;
    
    [Header(""Settings"")]
    public int codeLength = 4;
    public float displayTime = 2f;
    
    private string targetCode;
    private string inputCode = """";
    private bool canInput;",
                gameLogic = @"
        // Input handled by button clicks",
                customMethods = @"
    protected override IEnumerator SetupGame()
    {
        yield return StartCoroutine(base.SetupGame());
        canInput = false;
        inputCode = """";
        SetupButtons();
        StartCoroutine(ShowCode());
    }
    
    void SetupButtons()
    {
        for (int i = 0; i < numberButtons.Length; i++)
        {
            int num = i;
            numberButtons[i].onClick.RemoveAllListeners();
            numberButtons[i].onClick.AddListener(() => OnNumberPressed(num));
        }
        if (clearButton != null)
        {
            clearButton.onClick.RemoveAllListeners();
            clearButton.onClick.AddListener(OnClearPressed);
        }
        UpdateInputDisplay();
    }
    
    IEnumerator ShowCode()
    {
        targetCode = """";
        for (int i = 0; i < codeLength; i++)
            targetCode += Random.Range(0, 10).ToString();
        
        if (codeDisplay != null)
        {
            codeDisplay.text = targetCode;
            codeDisplay.color = Color.yellow;
        }
        
        yield return new WaitForSeconds(displayTime);
        
        if (codeDisplay != null)
        {
            codeDisplay.text = new string('-', codeLength);
            codeDisplay.color = Color.white;
        }
        
        canInput = true;
    }
    
    public void OnNumberPressed(int num)
    {
        if (!canInput) return;
        
        inputCode += num.ToString();
        UpdateInputDisplay();
        PlaySound(successSound);
        
        if (inputCode.Length >= codeLength)
        {
            canInput = false;
            if (inputCode == targetCode)
                Win();
            else
                Lose();
        }
    }
    
    public void OnClearPressed()
    {
        inputCode = """";
        UpdateInputDisplay();
    }
    
    void UpdateInputDisplay()
    {
        if (inputDisplay != null)
            inputDisplay.text = inputCode + new string('_', codeLength - inputCode.Length);
    }",
                createUI = CreateKeypadCodeUI
            },
            
            // === SKILL GAMES ===
            new MiniGameTemplate
            {
                name = "Button Mash",
                description = "Mash a button as fast as possible",
                instructions = "Mash SPACE as fast as you can!",
                timeLimit = 10f,
                category = "Skill",
                customFields = @"
    [Header(""Game Elements"")]
    public TextMeshProUGUI countText;
    public Image progressBar;
    public RectTransform mashButton;
    
    [Header(""Settings"")]
    public int targetPresses = 50;
    public float buttonPunchScale = 0.9f;
    
    private int presses;
    private Vector3 originalScale;",
                gameLogic = @"
        if (Input.GetKeyDown(KeyCode.Space))
        {
            presses++;
            UpdateUI();
            PlaySound(successSound);
            StartCoroutine(PunchButton());
            
            if (presses >= targetPresses)
                Win();
        }",
                customMethods = @"
    protected override IEnumerator SetupGame()
    {
        yield return StartCoroutine(base.SetupGame());
        presses = 0;
        if (mashButton != null) originalScale = mashButton.localScale;
        UpdateUI();
    }
    
    void UpdateUI()
    {
        if (countText != null) countText.text = presses.ToString();
        if (progressBar != null) progressBar.fillAmount = (float)presses / targetPresses;
    }
    
    IEnumerator PunchButton()
    {
        if (mashButton == null) yield break;
        mashButton.localScale = originalScale * buttonPunchScale;
        yield return new WaitForSeconds(0.05f);
        mashButton.localScale = originalScale;
    }",
                createUI = CreateButtonMashUI
            },
            
            new MiniGameTemplate
            {
                name = "Balance Beam",
                description = "Keep the ball balanced on a tilting beam",
                instructions = "Use LEFT/RIGHT to keep the ball centered!",
                timeLimit = 15f,
                category = "Skill",
                customFields = @"
    [Header(""Game Elements"")]
    public RectTransform beam;
    public RectTransform ball;
    public RectTransform leftEdge;
    public RectTransform rightEdge;
    
    [Header(""Settings"")]
    public float tiltSpeed = 60f;
    public float ballRollSpeed = 150f;
    public float maxTilt = 25f;
    
    private float beamAngle;
    private float ballPosition;
    private float beamWidth = 300f;",
                gameLogic = @"
        // Tilt beam with input
        float input = 0;
        if (Input.GetKey(KeyCode.LeftArrow)) input = 1f;
        if (Input.GetKey(KeyCode.RightArrow)) input = -1f;
        
        beamAngle += input * tiltSpeed * Time.deltaTime;
        beamAngle = Mathf.Clamp(beamAngle, -maxTilt, maxTilt);
        if (beam != null)
            beam.localRotation = Quaternion.Euler(0, 0, beamAngle);
        
        // Ball rolls based on tilt
        ballPosition += Mathf.Sin(beamAngle * Mathf.Deg2Rad) * ballRollSpeed * Time.deltaTime;
        if (ball != null)
            ball.anchoredPosition = new Vector2(ballPosition, 25);
        
        // Lose if ball falls off
        if (Mathf.Abs(ballPosition) > beamWidth / 2f)
            Lose();",
                customMethods = @"
    protected override IEnumerator SetupGame()
    {
        yield return StartCoroutine(base.SetupGame());
        beamAngle = 0;
        ballPosition = 0;
        if (beam != null)
        {
            beam.localRotation = Quaternion.identity;
            beamWidth = beam.sizeDelta.x;
        }
        if (ball != null)
            ball.anchoredPosition = new Vector2(0, 25);
    }",
                createUI = CreateBalanceBeamUI
            },
            
            // === QUICK GAMES ===
            new MiniGameTemplate
            {
                name = "Math Blitz",
                description = "Solve simple math problems quickly",
                instructions = "Click the correct answer!",
                timeLimit = 30f,
                category = "Quick",
                customFields = @"
    [Header(""Game Elements"")]
    public TextMeshProUGUI problemText;
    public Button[] answerButtons;
    public TextMeshProUGUI[] answerTexts;
    public TextMeshProUGUI scoreText;
    
    [Header(""Settings"")]
    public int problemsToSolve = 8;
    
    private int correctAnswer;
    private int correctIndex;
    private int solved;",
                gameLogic = @"
        // Input handled by button clicks",
                customMethods = @"
    protected override IEnumerator SetupGame()
    {
        yield return StartCoroutine(base.SetupGame());
        solved = 0;
        SetupButtons();
        UpdateScore();
        GenerateProblem();
    }
    
    void SetupButtons()
    {
        for (int i = 0; i < answerButtons.Length; i++)
        {
            int index = i;
            answerButtons[i].onClick.RemoveAllListeners();
            answerButtons[i].onClick.AddListener(() => OnAnswerClicked(index));
        }
    }
    
    void GenerateProblem()
    {
        int a = Random.Range(1, 15);
        int b = Random.Range(1, 15);
        int op = Random.Range(0, 3);
        
        switch (op)
        {
            case 0:
                problemText.text = $""{a} + {b} = ?"";
                correctAnswer = a + b;
                break;
            case 1:
                problemText.text = $""{a + b} - {b} = ?"";
                correctAnswer = a;
                break;
            case 2:
                a = Random.Range(1, 10);
                b = Random.Range(1, 10);
                problemText.text = $""{a} × {b} = ?"";
                correctAnswer = a * b;
                break;
        }
        
        correctIndex = Random.Range(0, answerButtons.Length);
        for (int i = 0; i < answerTexts.Length; i++)
        {
            if (i == correctIndex)
                answerTexts[i].text = correctAnswer.ToString();
            else
            {
                int wrong = correctAnswer + Random.Range(-10, 11);
                if (wrong == correctAnswer) wrong += 1;
                if (wrong < 0) wrong = Random.Range(1, 20);
                answerTexts[i].text = wrong.ToString();
            }
        }
    }
    
    public void OnAnswerClicked(int index)
    {
        if (index == correctIndex)
        {
            solved++;
            UpdateScore();
            PlaySound(successSound);
            StartCoroutine(FlashEffect(successFlashColor));
            if (solved >= problemsToSolve)
                Win();
            else
                GenerateProblem();
        }
        else
        {
            Lose();
        }
    }
    
    void UpdateScore()
    {
        if (scoreText != null)
            scoreText.text = $""{solved} / {problemsToSolve}"";
    }",
                createUI = CreateMathBlitzUI
            },
            
            new MiniGameTemplate
            {
                name = "Quick Tap",
                description = "Tap the target before it disappears",
                instructions = "Click the targets quickly!",
                timeLimit = 20f,
                category = "Quick",
                customFields = @"
    [Header(""Game Elements"")]
    public Button targetButton;
    public RectTransform spawnArea;
    public TextMeshProUGUI scoreText;
    
    [Header(""Settings"")]
    public int targetsToHit = 12;
    public float startTargetTime = 1.5f;
    public float minTargetTime = 0.5f;
    
    private int hits;
    private float targetTimer;
    private float currentTargetTime;",
                gameLogic = @"
        targetTimer -= Time.deltaTime;
        if (targetTimer <= 0)
        {
            Lose();
        }",
                customMethods = @"
    protected override IEnumerator SetupGame()
    {
        yield return StartCoroutine(base.SetupGame());
        hits = 0;
        currentTargetTime = startTargetTime;
        UpdateScore();
        
        if (targetButton != null)
        {
            targetButton.onClick.RemoveAllListeners();
            targetButton.onClick.AddListener(OnTargetClicked);
        }
        
        MoveTarget();
    }
    
    void MoveTarget()
    {
        if (targetButton == null || spawnArea == null) return;
        
        Vector2 size = spawnArea.sizeDelta;
        Vector2 newPos = new Vector2(
            Random.Range(-size.x/2 + 40, size.x/2 - 40),
            Random.Range(-size.y/2 + 40, size.y/2 - 40));
        
        targetButton.GetComponent<RectTransform>().anchoredPosition = newPos;
        targetTimer = currentTargetTime;
    }
    
    public void OnTargetClicked()
    {
        hits++;
        UpdateScore();
        PlaySound(successSound);
        currentTargetTime = Mathf.Max(minTargetTime, currentTargetTime - 0.08f);
        
        if (hits >= targetsToHit)
            Win();
        else
            MoveTarget();
    }
    
    void UpdateScore()
    {
        if (scoreText != null)
            scoreText.text = $""{hits} / {targetsToHit}"";
    }",
                createUI = CreateQuickTapUI
            },
            
            // === NEW GAMES ===
            
            new MiniGameTemplate
            {
                name = "Catch Falling",
                description = "Move left/right to catch falling objects",
                instructions = "Use LEFT/RIGHT arrows to catch the falling items!",
                timeLimit = 25f,
                category = "Skill",
                customFields = @"
    [Header(""Game Elements"")]
    public RectTransform catcher;
    public RectTransform fallingObject;
    public RectTransform spawnArea;
    public TextMeshProUGUI scoreText;
    
    [Header(""Settings"")]
    public int catchesToWin = 10;
    public float catcherSpeed = 400f;
    public float fallSpeed = 200f;
    public float fallSpeedIncrease = 15f;
    
    private int catches;
    private float currentFallSpeed;
    private float catcherX;",
                gameLogic = @"
        // Move catcher
        if (Input.GetKey(KeyCode.LeftArrow)) catcherX -= catcherSpeed * Time.deltaTime;
        if (Input.GetKey(KeyCode.RightArrow)) catcherX += catcherSpeed * Time.deltaTime;
        catcherX = Mathf.Clamp(catcherX, -250f, 250f);
        if (catcher != null)
            catcher.anchoredPosition = new Vector2(catcherX, catcher.anchoredPosition.y);
        
        // Move falling object
        if (fallingObject != null)
        {
            Vector2 pos = fallingObject.anchoredPosition;
            pos.y -= currentFallSpeed * Time.deltaTime;
            fallingObject.anchoredPosition = pos;
            
            // Check catch
            if (pos.y < -150f)
            {
                if (Mathf.Abs(pos.x - catcherX) < 50f)
                {
                    catches++;
                    UpdateScore();
                    PlaySound(successSound);
                    if (catches >= catchesToWin) Win();
                    else SpawnNewObject();
                }
                else
                {
                    Lose();
                }
            }
        }",
                customMethods = @"
    protected override IEnumerator SetupGame()
    {
        yield return StartCoroutine(base.SetupGame());
        AutoFindElements();
        catches = 0;
        currentFallSpeed = fallSpeed;
        catcherX = 0;
        UpdateScore();
        SpawnNewObject();
    }
    
    void AutoFindElements()
    {
        Transform gameArea = transform.Find(""GameArea"");
        if (gameArea == null) return;
        if (catcher == null) { var t = gameArea.Find(""Catcher""); if (t) catcher = t.GetComponent<RectTransform>(); }
        if (fallingObject == null) { var t = gameArea.Find(""FallingObject""); if (t) fallingObject = t.GetComponent<RectTransform>(); }
        if (scoreText == null) { var t = gameArea.Find(""ScoreText""); if (t) scoreText = t.GetComponent<TextMeshProUGUI>(); }
    }
    
    void SpawnNewObject()
    {
        if (fallingObject != null)
        {
            float x = UnityEngine.Random.Range(-200f, 200f);
            fallingObject.anchoredPosition = new Vector2(x, 180f);
            currentFallSpeed += fallSpeedIncrease;
        }
    }
    
    void UpdateScore()
    {
        if (scoreText != null) scoreText.text = $""{catches} / {catchesToWin}"";
    }",
                createUI = CreateCatchFallingUI
            },
            
            new MiniGameTemplate
            {
                name = "Rapid Keys",
                description = "Press the keys shown on screen as fast as possible",
                instructions = "Press the keys as they appear!",
                timeLimit = 20f,
                category = "Reaction",
                customFields = @"
    [Header(""Game Elements"")]
    public TextMeshProUGUI keyDisplay;
    public TextMeshProUGUI scoreText;
    
    [Header(""Settings"")]
    public int keysToPress = 15;
    
    private int pressed;
    private KeyCode currentKey;
    private KeyCode[] possibleKeys = { KeyCode.A, KeyCode.S, KeyCode.D, KeyCode.F, KeyCode.J, KeyCode.K, KeyCode.L };",
                gameLogic = @"
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
                    Lose();
                    break;
                }
            }
        }",
                customMethods = @"
    protected override IEnumerator SetupGame()
    {
        yield return StartCoroutine(base.SetupGame());
        AutoFindElements();
        pressed = 0;
        UpdateScore();
        ShowNextKey();
    }
    
    void AutoFindElements()
    {
        Transform gameArea = transform.Find(""GameArea"");
        if (gameArea == null) return;
        if (keyDisplay == null) { var t = gameArea.Find(""KeyDisplay""); if (t) keyDisplay = t.GetComponent<TextMeshProUGUI>(); }
        if (scoreText == null) { var t = gameArea.Find(""ScoreText""); if (t) scoreText = t.GetComponent<TextMeshProUGUI>(); }
    }
    
    void ShowNextKey()
    {
        currentKey = possibleKeys[UnityEngine.Random.Range(0, possibleKeys.Length)];
        if (keyDisplay != null) keyDisplay.text = currentKey.ToString();
    }
    
    void UpdateScore()
    {
        if (scoreText != null) scoreText.text = $""{pressed} / {keysToPress}"";
    }",
                createUI = CreateRapidKeysUI
            },
            
            new MiniGameTemplate
            {
                name = "Higher Lower",
                description = "Guess if the next number is higher or lower",
                instructions = "Is the next number HIGHER or LOWER?",
                timeLimit = 30f,
                category = "Quick",
                customFields = @"
    [Header(""Game Elements"")]
    public TextMeshProUGUI currentNumberText;
    public TextMeshProUGUI scoreText;
    public Button higherButton;
    public Button lowerButton;
    
    [Header(""Settings"")]
    public int roundsToWin = 8;
    
    private int currentNumber;
    private int nextNumber;
    private int wins;",
                gameLogic = @"
        // Input handled by buttons",
                customMethods = @"
    protected override IEnumerator SetupGame()
    {
        yield return StartCoroutine(base.SetupGame());
        AutoFindElements();
        wins = 0;
        GenerateNumbers();
        UpdateScore();
    }
    
    void AutoFindElements()
    {
        Transform gameArea = transform.Find(""GameArea"");
        if (gameArea == null) return;
        if (currentNumberText == null) { var t = gameArea.Find(""CurrentNumber""); if (t) currentNumberText = t.GetComponent<TextMeshProUGUI>(); }
        if (scoreText == null) { var t = gameArea.Find(""ScoreText""); if (t) scoreText = t.GetComponent<TextMeshProUGUI>(); }
        if (higherButton == null) { var t = gameArea.Find(""HigherButton""); if (t) { higherButton = t.GetComponent<Button>(); higherButton.onClick.AddListener(OnHigher); } }
        if (lowerButton == null) { var t = gameArea.Find(""LowerButton""); if (t) { lowerButton = t.GetComponent<Button>(); lowerButton.onClick.AddListener(OnLower); } }
    }
    
    void GenerateNumbers()
    {
        currentNumber = UnityEngine.Random.Range(1, 100);
        do { nextNumber = UnityEngine.Random.Range(1, 100); } while (nextNumber == currentNumber);
        if (currentNumberText != null) currentNumberText.text = currentNumber.ToString();
    }
    
    void OnHigher() { CheckGuess(nextNumber > currentNumber); }
    void OnLower() { CheckGuess(nextNumber < currentNumber); }
    
    void CheckGuess(bool correct)
    {
        if (correct)
        {
            wins++;
            UpdateScore();
            PlaySound(successSound);
            if (wins >= roundsToWin) Win();
            else { currentNumber = nextNumber; GenerateNumbers(); }
        }
        else Lose();
    }
    
    void UpdateScore()
    {
        if (scoreText != null) scoreText.text = $""{wins} / {roundsToWin}"";
    }",
                createUI = CreateHigherLowerUI
            },
            
            new MiniGameTemplate
            {
                name = "Word Type",
                description = "Type the word shown as fast as possible",
                instructions = "Type the word and press ENTER!",
                timeLimit = 30f,
                category = "Quick",
                customFields = @"
    [Header(""Game Elements"")]
    public TextMeshProUGUI wordDisplay;
    public TMP_InputField inputField;
    public TextMeshProUGUI scoreText;
    
    [Header(""Settings"")]
    public int wordsToType = 6;
    private string[] words = { ""PROFIT"", ""MONEY"", ""TRADE"", ""STOCK"", ""BANK"", ""CASH"", ""GOLD"", ""DEAL"", ""RICH"", ""EARN"" };
    
    private string currentWord;
    private int typed;",
                gameLogic = @"
        // Input handled by input field",
                customMethods = @"
    protected override IEnumerator SetupGame()
    {
        yield return StartCoroutine(base.SetupGame());
        AutoFindElements();
        typed = 0;
        UpdateScore();
        ShowNextWord();
    }
    
    void AutoFindElements()
    {
        Transform gameArea = transform.Find(""GameArea"");
        if (gameArea == null) return;
        if (wordDisplay == null) { var t = gameArea.Find(""WordDisplay""); if (t) wordDisplay = t.GetComponent<TextMeshProUGUI>(); }
        if (inputField == null) { var t = gameArea.Find(""InputField""); if (t) inputField = t.GetComponent<TMP_InputField>(); }
        if (scoreText == null) { var t = gameArea.Find(""ScoreText""); if (t) scoreText = t.GetComponent<TextMeshProUGUI>(); }
        if (inputField != null) inputField.onSubmit.AddListener(OnSubmit);
    }
    
    void ShowNextWord()
    {
        currentWord = words[UnityEngine.Random.Range(0, words.Length)];
        if (wordDisplay != null) wordDisplay.text = currentWord;
        if (inputField != null) { inputField.text = """"; inputField.ActivateInputField(); }
    }
    
    void OnSubmit(string text)
    {
        if (text.ToUpper() == currentWord)
        {
            typed++;
            UpdateScore();
            PlaySound(successSound);
            if (typed >= wordsToType) Win();
            else ShowNextWord();
        }
        else
        {
            if (inputField != null) { inputField.text = """"; inputField.ActivateInputField(); }
        }
    }
    
    void UpdateScore()
    {
        if (scoreText != null) scoreText.text = $""{typed} / {wordsToType}"";
    }",
                createUI = CreateWordTypeUI
            },
            
            new MiniGameTemplate
            {
                name = "Safe Cracker",
                description = "Turn the dial to crack the safe combination",
                instructions = "Use LEFT/RIGHT to turn dial, SPACE to confirm!",
                timeLimit = 30f,
                category = "Puzzle",
                customFields = @"
    [Header(""Game Elements"")]
    public RectTransform dial;
    public TextMeshProUGUI targetText;
    public TextMeshProUGUI currentText;
    public TextMeshProUGUI progressText;
    
    [Header(""Settings"")]
    public int numbersToFind = 3;
    public float dialSpeed = 100f;
    
    private int[] combination;
    private int currentIndex;
    private float dialAngle;
    private int currentNumber;",
                gameLogic = @"
        // Turn dial
        if (Input.GetKey(KeyCode.LeftArrow)) dialAngle += dialSpeed * Time.deltaTime;
        if (Input.GetKey(KeyCode.RightArrow)) dialAngle -= dialSpeed * Time.deltaTime;
        if (dialAngle < 0) dialAngle += 360;
        if (dialAngle >= 360) dialAngle -= 360;
        
        currentNumber = Mathf.RoundToInt(dialAngle / 36f) % 10;
        if (dial != null) dial.localRotation = Quaternion.Euler(0, 0, dialAngle);
        if (currentText != null) currentText.text = currentNumber.ToString();
        
        // Confirm
        if (Input.GetKeyDown(KeyCode.Space))
        {
            if (currentNumber == combination[currentIndex])
            {
                currentIndex++;
                PlaySound(successSound);
                UpdateProgress();
                if (currentIndex >= numbersToFind) Win();
            }
            else Lose();
        }",
                customMethods = @"
    protected override IEnumerator SetupGame()
    {
        yield return StartCoroutine(base.SetupGame());
        AutoFindElements();
        combination = new int[numbersToFind];
        for (int i = 0; i < numbersToFind; i++)
            combination[i] = UnityEngine.Random.Range(0, 10);
        currentIndex = 0;
        dialAngle = 0;
        UpdateProgress();
        if (targetText != null) targetText.text = ""Find: "" + string.Join(""-"", combination);
    }
    
    void AutoFindElements()
    {
        Transform gameArea = transform.Find(""GameArea"");
        if (gameArea == null) return;
        if (dial == null) { var t = gameArea.Find(""Dial""); if (t) dial = t.GetComponent<RectTransform>(); }
        if (targetText == null) { var t = gameArea.Find(""TargetText""); if (t) targetText = t.GetComponent<TextMeshProUGUI>(); }
        if (currentText == null) { var t = gameArea.Find(""CurrentText""); if (t) currentText = t.GetComponent<TextMeshProUGUI>(); }
        if (progressText == null) { var t = gameArea.Find(""ProgressText""); if (t) progressText = t.GetComponent<TextMeshProUGUI>(); }
    }
    
    void UpdateProgress()
    {
        if (progressText != null) progressText.text = $""{currentIndex} / {numbersToFind}"";
    }",
                createUI = CreateSafeCrackerUI
            },
            
            new MiniGameTemplate
            {
                name = "Coin Flip",
                description = "Guess heads or tails correctly in a row",
                instructions = "Press H for Heads, T for Tails!",
                timeLimit = 0f,
                category = "Quick",
                customFields = @"
    [Header(""Game Elements"")]
    public TextMeshProUGUI resultText;
    public TextMeshProUGUI scoreText;
    public Image coinImage;
    
    [Header(""Settings"")]
    public int winsNeeded = 5;
    public Color headsColor = Color.yellow;
    public Color tailsColor = Color.gray;
    
    private int wins;
    private bool isHeads;",
                gameLogic = @"
        if (Input.GetKeyDown(KeyCode.H))
        {
            FlipCoin(true);
        }
        else if (Input.GetKeyDown(KeyCode.T))
        {
            FlipCoin(false);
        }",
                customMethods = @"
    protected override IEnumerator SetupGame()
    {
        yield return StartCoroutine(base.SetupGame());
        AutoFindElements();
        wins = 0;
        UpdateScore();
        if (resultText != null) resultText.text = ""Make your guess!"";
    }
    
    void AutoFindElements()
    {
        Transform gameArea = transform.Find(""GameArea"");
        if (gameArea == null) return;
        if (resultText == null) { var t = gameArea.Find(""ResultText""); if (t) resultText = t.GetComponent<TextMeshProUGUI>(); }
        if (scoreText == null) { var t = gameArea.Find(""ScoreText""); if (t) scoreText = t.GetComponent<TextMeshProUGUI>(); }
        if (coinImage == null) { var t = gameArea.Find(""CoinImage""); if (t) coinImage = t.GetComponent<Image>(); }
    }
    
    void FlipCoin(bool guessedHeads)
    {
        isHeads = UnityEngine.Random.value > 0.5f;
        string result = isHeads ? ""HEADS"" : ""TAILS"";
        if (coinImage != null) coinImage.color = isHeads ? headsColor : tailsColor;
        
        if (guessedHeads == isHeads)
        {
            wins++;
            UpdateScore();
            PlaySound(successSound);
            if (resultText != null) resultText.text = result + "" - Correct!"";
            if (wins >= winsNeeded) Win();
        }
        else
        {
            if (resultText != null) resultText.text = result + "" - Wrong!"";
            Lose();
        }
    }
    
    void UpdateScore()
    {
        if (scoreText != null) scoreText.text = $""{wins} / {winsNeeded}"";
    }",
                createUI = CreateCoinFlipUI
            },
            
            new MiniGameTemplate
            {
                name = "Sequence Memory",
                description = "Watch the sequence and click in the same order",
                instructions = "Watch the sequence, then repeat it!",
                timeLimit = 0f,
                category = "Memory",
                customFields = @"
    [Header(""Game Elements"")]
    public Button[] gridButtons;
    public Image[] gridImages;
    public TextMeshProUGUI statusText;
    
    [Header(""Settings"")]
    public int sequenceLength = 5;
    public float flashTime = 0.5f;
    public Color normalColor = Color.gray;
    public Color flashColor = Color.cyan;
    
    private List<int> sequence = new List<int>();
    private int playerIndex;
    private bool inputPhase;",
                gameLogic = @"
        // Input handled by button clicks",
                customMethods = @"
    protected override IEnumerator SetupGame()
    {
        yield return StartCoroutine(base.SetupGame());
        AutoFindElements();
        sequence.Clear();
        for (int i = 0; i < sequenceLength; i++)
            sequence.Add(UnityEngine.Random.Range(0, gridButtons.Length));
        SetupButtons();
        StartCoroutine(ShowSequence());
    }
    
    void AutoFindElements()
    {
        Transform gameArea = transform.Find(""GameArea"");
        if (gameArea == null) return;
        if (statusText == null) { var t = gameArea.Find(""StatusText""); if (t) statusText = t.GetComponent<TextMeshProUGUI>(); }
        if (gridButtons == null || gridButtons.Length == 0)
        {
            List<Button> btns = new List<Button>();
            List<Image> imgs = new List<Image>();
            for (int i = 0; i < 9; i++)
            {
                var t = gameArea.Find($""GridButton{i}"");
                if (t) { btns.Add(t.GetComponent<Button>()); imgs.Add(t.GetComponent<Image>()); }
            }
            gridButtons = btns.ToArray();
            gridImages = imgs.ToArray();
        }
    }
    
    void SetupButtons()
    {
        for (int i = 0; i < gridButtons.Length; i++)
        {
            int idx = i;
            gridButtons[i].onClick.RemoveAllListeners();
            gridButtons[i].onClick.AddListener(() => OnGridClick(idx));
            if (gridImages != null && i < gridImages.Length) gridImages[i].color = normalColor;
        }
    }
    
    IEnumerator ShowSequence()
    {
        inputPhase = false;
        if (statusText != null) statusText.text = ""Watch..."";
        yield return new WaitForSeconds(0.5f);
        
        foreach (int idx in sequence)
        {
            if (gridImages != null && idx < gridImages.Length)
            {
                gridImages[idx].color = flashColor;
                yield return new WaitForSeconds(flashTime);
                gridImages[idx].color = normalColor;
                yield return new WaitForSeconds(0.2f);
            }
        }
        
        inputPhase = true;
        playerIndex = 0;
        if (statusText != null) statusText.text = ""Your turn!"";
    }
    
    void OnGridClick(int idx)
    {
        if (!inputPhase) return;
        
        if (idx == sequence[playerIndex])
        {
            playerIndex++;
            PlaySound(successSound);
            StartCoroutine(FlashButton(idx));
            if (playerIndex >= sequence.Count) Win();
        }
        else Lose();
    }
    
    IEnumerator FlashButton(int idx)
    {
        if (gridImages != null && idx < gridImages.Length)
        {
            gridImages[idx].color = flashColor;
            yield return new WaitForSeconds(0.15f);
            gridImages[idx].color = normalColor;
        }
    }",
                createUI = CreateSequenceMemoryUI
            },
            
            new MiniGameTemplate
            {
                name = "Odd One Out",
                description = "Find the different item among similar ones",
                instructions = "Click the one that's different!",
                timeLimit = 25f,
                category = "Puzzle",
                customFields = @"
    [Header(""Game Elements"")]
    public Button[] itemButtons;
    public Image[] itemImages;
    public TextMeshProUGUI scoreText;
    
    [Header(""Settings"")]
    public int roundsToWin = 5;
    public Color[] possibleColors = { Color.red, Color.blue, Color.green, Color.yellow, Color.magenta };
    
    private int correctIndex;
    private int rounds;",
                gameLogic = @"
        // Input handled by buttons",
                customMethods = @"
    protected override IEnumerator SetupGame()
    {
        yield return StartCoroutine(base.SetupGame());
        AutoFindElements();
        rounds = 0;
        SetupButtons();
        UpdateScore();
        GenerateRound();
    }
    
    void AutoFindElements()
    {
        Transform gameArea = transform.Find(""GameArea"");
        if (gameArea == null) return;
        if (scoreText == null) { var t = gameArea.Find(""ScoreText""); if (t) scoreText = t.GetComponent<TextMeshProUGUI>(); }
        if (itemButtons == null || itemButtons.Length == 0)
        {
            List<Button> btns = new List<Button>();
            List<Image> imgs = new List<Image>();
            for (int i = 0; i < 9; i++)
            {
                var t = gameArea.Find($""ItemButton{i}"");
                if (t) { btns.Add(t.GetComponent<Button>()); imgs.Add(t.GetComponent<Image>()); }
            }
            itemButtons = btns.ToArray();
            itemImages = imgs.ToArray();
        }
    }
    
    void SetupButtons()
    {
        for (int i = 0; i < itemButtons.Length; i++)
        {
            int idx = i;
            itemButtons[i].onClick.RemoveAllListeners();
            itemButtons[i].onClick.AddListener(() => OnItemClick(idx));
        }
    }
    
    void GenerateRound()
    {
        Color mainColor = possibleColors[UnityEngine.Random.Range(0, possibleColors.Length)];
        Color oddColor;
        do { oddColor = possibleColors[UnityEngine.Random.Range(0, possibleColors.Length)]; } while (oddColor == mainColor);
        
        correctIndex = UnityEngine.Random.Range(0, itemImages.Length);
        for (int i = 0; i < itemImages.Length; i++)
            itemImages[i].color = (i == correctIndex) ? oddColor : mainColor;
    }
    
    void OnItemClick(int idx)
    {
        if (idx == correctIndex)
        {
            rounds++;
            UpdateScore();
            PlaySound(successSound);
            if (rounds >= roundsToWin) Win();
            else GenerateRound();
        }
        else Lose();
    }
    
    void UpdateScore()
    {
        if (scoreText != null) scoreText.text = $""{rounds} / {roundsToWin}"";
    }",
                createUI = CreateOddOneOutUI
            },
            
            new MiniGameTemplate
            {
                name = "Speed Count",
                description = "Count how many objects appear on screen",
                instructions = "Count the items and enter the number!",
                timeLimit = 30f,
                category = "Quick",
                customFields = @"
    [Header(""Game Elements"")]
    public RectTransform itemContainer;
    public GameObject itemPrefab;
    public TMP_InputField inputField;
    public TextMeshProUGUI scoreText;
    
    [Header(""Settings"")]
    public int roundsToWin = 5;
    
    private int correctCount;
    private int rounds;
    private List<GameObject> spawnedItems = new List<GameObject>();",
                gameLogic = @"
        // Input handled by input field",
                customMethods = @"
    protected override IEnumerator SetupGame()
    {
        yield return StartCoroutine(base.SetupGame());
        AutoFindElements();
        rounds = 0;
        UpdateScore();
        GenerateRound();
    }
    
    void AutoFindElements()
    {
        Transform gameArea = transform.Find(""GameArea"");
        if (gameArea == null) return;
        if (itemContainer == null) { var t = gameArea.Find(""ItemContainer""); if (t) itemContainer = t.GetComponent<RectTransform>(); }
        if (inputField == null) { var t = gameArea.Find(""InputField""); if (t) inputField = t.GetComponent<TMP_InputField>(); }
        if (scoreText == null) { var t = gameArea.Find(""ScoreText""); if (t) scoreText = t.GetComponent<TextMeshProUGUI>(); }
        if (inputField != null) inputField.onSubmit.AddListener(OnSubmit);
    }
    
    void GenerateRound()
    {
        foreach (var item in spawnedItems) if (item) Destroy(item);
        spawnedItems.Clear();
        
        correctCount = UnityEngine.Random.Range(3, 12);
        for (int i = 0; i < correctCount; i++)
        {
            GameObject item = new GameObject(""CountItem"");
            item.transform.SetParent(itemContainer, false);
            var rect = item.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(30, 30);
            rect.anchoredPosition = new Vector2(UnityEngine.Random.Range(-180f, 180f), UnityEngine.Random.Range(-80f, 80f));
            var img = item.AddComponent<Image>();
            img.color = Color.yellow;
            spawnedItems.Add(item);
        }
        
        if (inputField != null) { inputField.text = """"; inputField.ActivateInputField(); }
    }
    
    void OnSubmit(string text)
    {
        if (int.TryParse(text, out int guess) && guess == correctCount)
        {
            rounds++;
            UpdateScore();
            PlaySound(successSound);
            if (rounds >= roundsToWin) Win();
            else GenerateRound();
        }
        else
        {
            if (inputField != null) { inputField.text = """"; inputField.ActivateInputField(); }
        }
    }
    
    void UpdateScore()
    {
        if (scoreText != null) scoreText.text = $""{rounds} / {roundsToWin}"";
    }",
                createUI = CreateSpeedCountUI
            },
            
            new MiniGameTemplate
            {
                name = "Pattern Match",
                description = "Recreate the pattern shown using arrow keys",
                instructions = "Memorize the pattern, then enter it with arrow keys!",
                timeLimit = 0f,
                category = "Memory",
                customFields = @"
    [Header(""Game Elements"")]
    public TextMeshProUGUI patternDisplay;
    public TextMeshProUGUI inputDisplay;
    public TextMeshProUGUI statusText;
    
    [Header(""Settings"")]
    public int patternLength = 4;
    public int patternsToWin = 3;
    
    private List<string> pattern = new List<string>();
    private List<string> playerInput = new List<string>();
    private int wins;
    private bool inputPhase;",
                gameLogic = @"
        if (!inputPhase) return;
        
        string key = null;
        if (Input.GetKeyDown(KeyCode.UpArrow)) key = ""↑"";
        if (Input.GetKeyDown(KeyCode.DownArrow)) key = ""↓"";
        if (Input.GetKeyDown(KeyCode.LeftArrow)) key = ""←"";
        if (Input.GetKeyDown(KeyCode.RightArrow)) key = ""→"";
        
        if (key != null)
        {
            playerInput.Add(key);
            UpdateInputDisplay();
            
            if (playerInput.Count >= pattern.Count)
            {
                CheckPattern();
            }
        }",
                customMethods = @"
    protected override IEnumerator SetupGame()
    {
        yield return StartCoroutine(base.SetupGame());
        AutoFindElements();
        wins = 0;
        StartCoroutine(ShowPattern());
    }
    
    void AutoFindElements()
    {
        Transform gameArea = transform.Find(""GameArea"");
        if (gameArea == null) return;
        if (patternDisplay == null) { var t = gameArea.Find(""PatternDisplay""); if (t) patternDisplay = t.GetComponent<TextMeshProUGUI>(); }
        if (inputDisplay == null) { var t = gameArea.Find(""InputDisplay""); if (t) inputDisplay = t.GetComponent<TextMeshProUGUI>(); }
        if (statusText == null) { var t = gameArea.Find(""StatusText""); if (t) statusText = t.GetComponent<TextMeshProUGUI>(); }
    }
    
    IEnumerator ShowPattern()
    {
        inputPhase = false;
        pattern.Clear();
        playerInput.Clear();
        
        string[] arrows = { ""↑"", ""↓"", ""←"", ""→"" };
        for (int i = 0; i < patternLength; i++)
            pattern.Add(arrows[UnityEngine.Random.Range(0, 4)]);
        
        if (statusText != null) statusText.text = ""Memorize!"";
        if (patternDisplay != null) patternDisplay.text = string.Join("" "", pattern);
        if (inputDisplay != null) inputDisplay.text = """";
        
        yield return new WaitForSeconds(2f + patternLength * 0.3f);
        
        if (patternDisplay != null) patternDisplay.text = ""???"";
        if (statusText != null) statusText.text = ""Enter pattern!"";
        inputPhase = true;
    }
    
    void UpdateInputDisplay()
    {
        if (inputDisplay != null) inputDisplay.text = string.Join("" "", playerInput);
    }
    
    void CheckPattern()
    {
        bool correct = true;
        for (int i = 0; i < pattern.Count; i++)
        {
            if (playerInput[i] != pattern[i]) { correct = false; break; }
        }
        
        if (correct)
        {
            wins++;
            PlaySound(successSound);
            if (wins >= patternsToWin) Win();
            else { patternLength++; StartCoroutine(ShowPattern()); }
        }
        else Lose();
    }",
                createUI = CreatePatternMatchUI
            }
        };
    }
    
    void OnGUI()
    {
        // Always ensure templates are initialized (force refresh)
        if (templates == null || templates.Count < 21) InitTemplates();
        
        GUILayout.Label("Minigame Template Library", EditorStyles.boldLabel);
        EditorGUILayout.Space();
        
        // Search and filter
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("Search:", GUILayout.Width(50));
        searchFilter = EditorGUILayout.TextField(searchFilter);
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("Category:", GUILayout.Width(60));
        int catIndex = System.Array.IndexOf(categories, categoryFilter);
        catIndex = EditorGUILayout.Popup(catIndex, categories);
        categoryFilter = categories[catIndex];
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.Space();
        
        // Create All Button
        if (GUILayout.Button("CREATE ALL MINIGAMES", GUILayout.Height(35)))
        {
            CreateAllMinigames();
        }
        
        EditorGUILayout.Space();
        
        // Template list
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        
        string lastCategory = "";
        foreach (var template in templates)
        {
            if (categoryFilter != "All" && template.category != categoryFilter) continue;
            if (!string.IsNullOrEmpty(searchFilter) && 
                !template.name.ToLower().Contains(searchFilter.ToLower()) &&
                !template.description.ToLower().Contains(searchFilter.ToLower())) continue;
            
            if (template.category != lastCategory)
            {
                EditorGUILayout.Space();
                GUILayout.Label($"━━━ {template.category.ToUpper()} ━━━", EditorStyles.boldLabel);
                lastCategory = template.category;
            }
            
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(template.name, EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (template.timeLimit > 0)
                GUILayout.Label($"⏱ {template.timeLimit}s", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
            
            GUILayout.Label(template.description, EditorStyles.wordWrappedLabel);
            
            if (GUILayout.Button("Create Game", GUILayout.Height(25)))
            {
                CreateGame(template);
            }
            
            EditorGUILayout.EndVertical();
        }
        
        EditorGUILayout.EndScrollView();
    }
    
    void CreateAllMinigames()
    {
        if (!EditorUtility.DisplayDialog("Create All Minigames", 
            $"This will create {templates.Count} minigame scripts and UI panels.\n\nExisting ones will be SKIPPED (not overwritten).\n\nContinue?", "Yes", "Cancel"))
            return;
        
        foreach (var template in templates)
        {
            CreateGame(template);
        }
        
        Debug.Log($"Created {templates.Count} minigames!");
        EditorUtility.DisplayDialog("Complete", $"Created {templates.Count} minigames!\n\nAssign the panels to MiniGameManager.", "OK");
    }
    
    void CreateGame(MiniGameTemplate template)
    {
        CreateScript(template);
        CreatePanel(template);
    }
    
    void CreateScript(MiniGameTemplate template)
    {
        string className = template.name.Replace(" ", "") + "MiniGame";
        string path = $"Assets/ScriptsIMade/Minigames/{className}.cs";
        
        // Skip if script already exists
        if (File.Exists(path))
        {
            Debug.Log($"Script already exists, skipping: {path}");
            return;
        }
        
        Directory.CreateDirectory("Assets/ScriptsIMade/Minigames");
        
        string script = $@"using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// {template.description}
/// </summary>
public class {className} : MiniGameBase
{{
    {template.customFields}
    
    void Awake()
    {{
        gameName = ""{template.name}"";
        timeLimit = {template.timeLimit}f;
    }}
    
    protected override void GameUpdate()
    {{
        {template.gameLogic}
    }}
    
    {template.customMethods}
}}
";
        File.WriteAllText(path, script);
        Debug.Log($"Created script: {path}");
    }
    
    void CreatePanel(MiniGameTemplate template)
    {
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("No Canvas found!");
            return;
        }
        
        string panelName = template.name.Replace(" ", "") + "Panel";
        
        // Skip if panel already exists
        var existing = canvas.transform.Find(panelName);
        if (existing != null)
        {
            Debug.Log($"Panel already exists, skipping: {panelName}");
            return;
        }
        
        // Create base panel
        GameObject panel = new GameObject(panelName);
        panel.transform.SetParent(canvas.transform, false);
        
        RectTransform panelRect = panel.AddComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.sizeDelta = Vector2.zero;
        
        Image bg = panel.AddComponent<Image>();
        bg.color = new Color(0.08f, 0.08f, 0.12f, 0.97f);
        
        AudioSource audio = panel.AddComponent<AudioSource>();
        audio.playOnAwake = false;
        
        // Title
        CreateText(panel.transform, "Title", template.name.ToUpper(), 40,
            new Vector2(0.5f, 1f), new Vector2(0, -35), new Vector2(500, 50), Color.white);
        
        // Business Problem Text (shows the crisis/situation)
        var problemObj = CreateText(panel.transform, "ProblemText", "A problem has occurred!", 24,
            new Vector2(0.5f, 1f), new Vector2(0, -75), new Vector2(700, 35), new Color(1f, 0.85f, 0.3f));
        problemObj.GetComponent<TextMeshProUGUI>().fontStyle = FontStyles.Italic;
        
        // Instructions
        CreateText(panel.transform, "Instructions", template.instructions, 20,
            new Vector2(0.5f, 1f), new Vector2(0, -110), new Vector2(600, 30), new Color(0.7f, 0.7f, 0.7f));
        
        // Timer
        if (template.timeLimit > 0)
        {
            CreateText(panel.transform, "Timer", "30", 48,
                new Vector2(1f, 1f), new Vector2(-60, -60), new Vector2(80, 60), Color.white);
        }
        
        // Game Area
        GameObject gameArea = new GameObject("GameArea");
        gameArea.transform.SetParent(panel.transform, false);
        RectTransform gameRect = gameArea.AddComponent<RectTransform>();
        gameRect.anchorMin = new Vector2(0.5f, 0.5f);
        gameRect.anchorMax = new Vector2(0.5f, 0.5f);
        gameRect.sizeDelta = new Vector2(700, 450);
        
        // Create game-specific UI
        if (template.createUI != null)
        {
            template.createUI(gameArea, template);
        }
        
        // Result text
        var result = CreateText(panel.transform, "ResultText", "SUCCESS!", 56,
            new Vector2(0.5f, 0.5f), new Vector2(0, -200), new Vector2(400, 70), Color.green);
        result.SetActive(false);
        
        // Flash overlay
        GameObject flash = new GameObject("FlashOverlay");
        flash.transform.SetParent(panel.transform, false);
        flash.transform.SetAsLastSibling();
        RectTransform flashRect = flash.AddComponent<RectTransform>();
        flashRect.anchorMin = Vector2.zero;
        flashRect.anchorMax = Vector2.one;
        flashRect.sizeDelta = Vector2.zero;
        Image flashImg = flash.AddComponent<Image>();
        flashImg.color = new Color(0, 0, 0, 0);
        flashImg.raycastTarget = false;
        
        panel.SetActive(false);
        
        EditorUtility.SetDirty(panel);
        Debug.Log($"Created panel: {panelName}");
        
        AssetDatabase.Refresh();
    }
    
    // === UI CREATION METHODS ===
    
    static void CreateArrowTimingUI(GameObject parent, MiniGameTemplate template)
    {
        float circleSize = 280f;
        
        // Circle background
        var circle = CreateImage(parent.transform, "CircleBackground", 
            Vector2.zero, new Vector2(circleSize, circleSize), new Color(0.15f, 0.15f, 0.2f));
        
        // Target zone
        var target = CreateImage(parent.transform, "TargetZone",
            Vector2.zero, new Vector2(circleSize, circleSize), new Color(0.2f, 0.8f, 0.2f, 0.5f));
        target.type = Image.Type.Filled;
        target.fillMethod = Image.FillMethod.Radial360;
        target.fillOrigin = (int)Image.Origin360.Top;
        target.fillAmount = 0.15f;
        
        // Arrow pivot
        GameObject pivot = new GameObject("ArrowPivot");
        pivot.transform.SetParent(parent.transform, false);
        RectTransform pivotRect = pivot.AddComponent<RectTransform>();
        pivotRect.anchoredPosition = Vector2.zero;
        pivotRect.sizeDelta = Vector2.zero;
        
        // Arrow
        var arrow = CreateImage(pivot.transform, "Arrow",
            new Vector2(0, circleSize * 0.2f), new Vector2(8, circleSize * 0.4f), Color.white);
        arrow.rectTransform.pivot = new Vector2(0.5f, 0f);
        
        // Arrow tip
        var tip = CreateImage(pivot.transform, "ArrowTip",
            new Vector2(0, circleSize * 0.42f), new Vector2(20, 20), Color.white);
        
        // Score
        CreateText(parent.transform, "ScoreText", "0 / 5", 32,
            new Vector2(0.5f, 0f), new Vector2(0, -80), new Vector2(150, 45), Color.white);
    }
    
    static void CreateStopTheBarUI(GameObject parent, MiniGameTemplate template)
    {
        // Track background
        var track = CreateImage(parent.transform, "Track",
            Vector2.zero, new Vector2(500, 60), new Color(0.2f, 0.2f, 0.25f));
        
        // Target zone
        var target = CreateImage(parent.transform, "TargetZone",
            new Vector2(100, 0), new Vector2(80, 50), new Color(0.2f, 0.8f, 0.2f, 0.6f));
        
        // Moving bar
        var bar = CreateImage(parent.transform, "MovingBar",
            Vector2.zero, new Vector2(12, 70), Color.white);
        
        // Score
        CreateText(parent.transform, "ScoreText", "0 / 3", 32,
            new Vector2(0.5f, 0f), new Vector2(0, -80), new Vector2(150, 45), Color.white);
    }
    
    static void CreateColorMatchUI(GameObject parent, MiniGameTemplate template)
    {
        // Color display
        var display = CreateImage(parent.transform, "ColorDisplay",
            new Vector2(0, 60), new Vector2(150, 150), Color.red);
        
        // Key prompt
        CreateText(parent.transform, "KeyPrompt", "R", 72,
            new Vector2(0.5f, 0.5f), new Vector2(0, 60), new Vector2(150, 100), Color.white);
        
        // Key indicators
        Color[] colors = { Color.red, Color.green, Color.blue, Color.yellow };
        string[] keys = { "R", "G", "B", "Y" };
        for (int i = 0; i < 4; i++)
        {
            float x = -150 + i * 100;
            var indicator = CreateImage(parent.transform, $"KeyIndicator{i}",
                new Vector2(x, -100), new Vector2(70, 70), colors[i]);
            CreateText(indicator.transform, "KeyLabel", keys[i], 28,
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(70, 70), Color.white);
        }
        
        // Score
        CreateText(parent.transform, "ScoreText", "0 / 10", 28,
            new Vector2(0.5f, 0f), new Vector2(0, -80), new Vector2(150, 40), Color.white);
    }
    
    static void CreateWhackAMoleUI(GameObject parent, MiniGameTemplate template)
    {
        // 3x3 grid of holes
        for (int row = 0; row < 3; row++)
        {
            for (int col = 0; col < 3; col++)
            {
                int index = row * 3 + col;
                float x = (col - 1) * 120;
                float y = (1 - row) * 120;
                
                GameObject hole = new GameObject($"Hole{index}");
                hole.transform.SetParent(parent.transform, false);
                RectTransform holeRect = hole.AddComponent<RectTransform>();
                holeRect.anchoredPosition = new Vector2(x, y);
                holeRect.sizeDelta = new Vector2(90, 90);
                
                Image holeImg = hole.AddComponent<Image>();
                holeImg.color = new Color(0.3f, 0.3f, 0.35f);
                
                Button btn = hole.AddComponent<Button>();
                btn.targetGraphic = holeImg;
            }
        }
        
        // Score
        CreateText(parent.transform, "ScoreText", "0 / 15", 28,
            new Vector2(0.5f, 0f), new Vector2(0, -120), new Vector2(150, 40), Color.white);
    }
    
    static void CreateSimonSaysUI(GameObject parent, MiniGameTemplate template)
    {
        // 4 colored buttons in a square
        Color[] colors = { Color.red, Color.green, Color.blue, Color.yellow };
        Vector2[] positions = { new Vector2(-70, 70), new Vector2(70, 70), new Vector2(-70, -70), new Vector2(70, -70) };
        
        for (int i = 0; i < 4; i++)
        {
            GameObject btnObj = new GameObject($"ColorButton{i}");
            btnObj.transform.SetParent(parent.transform, false);
            RectTransform btnRect = btnObj.AddComponent<RectTransform>();
            btnRect.anchoredPosition = positions[i];
            btnRect.sizeDelta = new Vector2(120, 120);
            
            Image btnImg = btnObj.AddComponent<Image>();
            btnImg.color = colors[i];
            
            Button btn = btnObj.AddComponent<Button>();
            btn.targetGraphic = btnImg;
        }
        
        // Status text
        CreateText(parent.transform, "StatusText", "Watch...", 28,
            new Vector2(0.5f, 0f), new Vector2(0, -120), new Vector2(200, 40), Color.white);
    }
    
    static void CreateNumberSequenceUI(GameObject parent, MiniGameTemplate template)
    {
        // Display text
        CreateText(parent.transform, "DisplayText", "123", 72,
            new Vector2(0.5f, 0.5f), new Vector2(0, 50), new Vector2(400, 100), Color.yellow);
        
        // Input field
        GameObject inputObj = new GameObject("InputField");
        inputObj.transform.SetParent(parent.transform, false);
        RectTransform inputRect = inputObj.AddComponent<RectTransform>();
        inputRect.anchoredPosition = new Vector2(0, -60);
        inputRect.sizeDelta = new Vector2(300, 60);
        
        Image inputBg = inputObj.AddComponent<Image>();
        inputBg.color = new Color(0.2f, 0.2f, 0.25f);
        
        TMP_InputField input = inputObj.AddComponent<TMP_InputField>();
        
        GameObject textArea = new GameObject("Text Area");
        textArea.transform.SetParent(inputObj.transform, false);
        RectTransform textAreaRect = textArea.AddComponent<RectTransform>();
        textAreaRect.anchorMin = Vector2.zero;
        textAreaRect.anchorMax = Vector2.one;
        textAreaRect.sizeDelta = new Vector2(-20, 0);
        
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(textArea.transform, false);
        RectTransform textObjRect = textObj.AddComponent<RectTransform>();
        textObjRect.anchorMin = Vector2.zero;
        textObjRect.anchorMax = Vector2.one;
        textObjRect.sizeDelta = Vector2.zero;
        TextMeshProUGUI inputText = textObj.AddComponent<TextMeshProUGUI>();
        inputText.fontSize = 36;
        inputText.alignment = TextAlignmentOptions.Center;
        input.textComponent = inputText;
        
        // Level text
        CreateText(parent.transform, "LevelText", "Level: 1", 24,
            new Vector2(0.5f, 0f), new Vector2(0, -100), new Vector2(150, 35), Color.gray);
    }
    
    static void CreateKeypadCodeUI(GameObject parent, MiniGameTemplate template)
    {
        // Code display
        CreateText(parent.transform, "CodeDisplay", "----", 56,
            new Vector2(0.5f, 0.5f), new Vector2(0, 140), new Vector2(300, 70), Color.yellow);
        
        // Input display
        CreateText(parent.transform, "InputDisplay", "____", 48,
            new Vector2(0.5f, 0.5f), new Vector2(0, 70), new Vector2(300, 60), Color.white);
        
        // Number buttons (0-9 in phone layout)
        int[,] layout = { {1,2,3}, {4,5,6}, {7,8,9}, {-1,0,-1} };
        for (int row = 0; row < 4; row++)
        {
            for (int col = 0; col < 3; col++)
            {
                int num = layout[row, col];
                if (num < 0) continue;
                
                float x = (col - 1) * 80;
                float y = -row * 70;
                
                GameObject btnObj = new GameObject($"NumButton{num}");
                btnObj.transform.SetParent(parent.transform, false);
                RectTransform btnRect = btnObj.AddComponent<RectTransform>();
                btnRect.anchoredPosition = new Vector2(x, y);
                btnRect.sizeDelta = new Vector2(65, 55);
                
                Image btnImg = btnObj.AddComponent<Image>();
                btnImg.color = new Color(0.3f, 0.3f, 0.4f);
                
                Button btn = btnObj.AddComponent<Button>();
                btn.targetGraphic = btnImg;
                
                CreateText(btnObj.transform, "Label", num.ToString(), 28,
                    new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(65, 55), Color.white);
            }
        }
        
        // Clear button
        GameObject clearObj = new GameObject("ClearButton");
        clearObj.transform.SetParent(parent.transform, false);
        RectTransform clearRect = clearObj.AddComponent<RectTransform>();
        clearRect.anchoredPosition = new Vector2(0, -230);
        clearRect.sizeDelta = new Vector2(120, 40);
        Image clearImg = clearObj.AddComponent<Image>();
        clearImg.color = new Color(0.5f, 0.2f, 0.2f);
        Button clearBtn = clearObj.AddComponent<Button>();
        clearBtn.targetGraphic = clearImg;
        CreateText(clearObj.transform, "Label", "CLEAR", 20,
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(120, 40), Color.white);
    }
    
    static void CreateButtonMashUI(GameObject parent, MiniGameTemplate template)
    {
        // Big mash button visual
        var mashBtn = CreateImage(parent.transform, "MashButton",
            new Vector2(0, 20), new Vector2(200, 200), new Color(0.8f, 0.2f, 0.2f));
        
        CreateText(mashBtn.transform, "Label", "MASH\nSPACE!", 32,
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(180, 180), Color.white);
        
        // Count text
        CreateText(parent.transform, "CountText", "0", 72,
            new Vector2(0.5f, 0.5f), new Vector2(0, -130), new Vector2(200, 90), Color.yellow);
        
        // Progress bar background
        var progressBg = CreateImage(parent.transform, "ProgressBg",
            new Vector2(0, -200), new Vector2(400, 30), new Color(0.2f, 0.2f, 0.25f));
        
        // Progress bar fill
        var progressFill = CreateImage(parent.transform, "ProgressBar",
            new Vector2(0, -200), new Vector2(400, 30), Color.green);
        progressFill.type = Image.Type.Filled;
        progressFill.fillMethod = Image.FillMethod.Horizontal;
        progressFill.fillAmount = 0f;
    }
    
    static void CreateBalanceBeamUI(GameObject parent, MiniGameTemplate template)
    {
        // Beam
        var beam = CreateImage(parent.transform, "Beam",
            new Vector2(0, -50), new Vector2(350, 20), new Color(0.4f, 0.3f, 0.2f));
        
        // Ball
        var ball = CreateImage(beam.transform, "Ball",
            new Vector2(0, 25), new Vector2(40, 40), Color.white);
        
        // Edge indicators
        CreateImage(parent.transform, "LeftEdge",
            new Vector2(-200, -50), new Vector2(10, 60), Color.red);
        CreateImage(parent.transform, "RightEdge",
            new Vector2(200, -50), new Vector2(10, 60), Color.red);
        
        // Instructions reminder
        CreateText(parent.transform, "Controls", "← LEFT    RIGHT →", 24,
            new Vector2(0.5f, 0f), new Vector2(0, -100), new Vector2(300, 35), Color.gray);
    }
    
    static void CreateMathBlitzUI(GameObject parent, MiniGameTemplate template)
    {
        // Problem text
        CreateText(parent.transform, "ProblemText", "5 + 3 = ?", 56,
            new Vector2(0.5f, 0.5f), new Vector2(0, 80), new Vector2(400, 80), Color.white);
        
        // Answer buttons (2x2 grid)
        for (int i = 0; i < 4; i++)
        {
            int row = i / 2;
            int col = i % 2;
            float x = (col == 0) ? -90 : 90;
            float y = (row == 0) ? -20 : -100;
            
            GameObject btnObj = new GameObject($"AnswerButton{i}");
            btnObj.transform.SetParent(parent.transform, false);
            RectTransform btnRect = btnObj.AddComponent<RectTransform>();
            btnRect.anchoredPosition = new Vector2(x, y);
            btnRect.sizeDelta = new Vector2(150, 65);
            
            Image btnImg = btnObj.AddComponent<Image>();
            btnImg.color = new Color(0.25f, 0.25f, 0.35f);
            
            Button btn = btnObj.AddComponent<Button>();
            btn.targetGraphic = btnImg;
            
            CreateText(btnObj.transform, "AnswerText", "8", 36,
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(150, 65), Color.white);
        }
        
        // Score
        CreateText(parent.transform, "ScoreText", "0 / 8", 28,
            new Vector2(0.5f, 0f), new Vector2(0, -120), new Vector2(150, 40), Color.white);
    }
    
    static void CreateQuickTapUI(GameObject parent, MiniGameTemplate template)
    {
        // Spawn area background
        var spawnArea = CreateImage(parent.transform, "SpawnArea",
            Vector2.zero, new Vector2(500, 350), new Color(0.12f, 0.12f, 0.18f));
        
        // Target button
        GameObject targetObj = new GameObject("TargetButton");
        targetObj.transform.SetParent(spawnArea.transform, false);
        RectTransform targetRect = targetObj.AddComponent<RectTransform>();
        targetRect.anchoredPosition = Vector2.zero;
        targetRect.sizeDelta = new Vector2(70, 70);
        
        Image targetImg = targetObj.AddComponent<Image>();
        targetImg.color = Color.red;
        
        Button targetBtn = targetObj.AddComponent<Button>();
        targetBtn.targetGraphic = targetImg;
        
        // Score
        CreateText(parent.transform, "ScoreText", "0 / 12", 28,
            new Vector2(0.5f, 0f), new Vector2(0, -120), new Vector2(150, 40), Color.white);
    }
    
    // === NEW GAME UI METHODS ===
    
    static void CreateCatchFallingUI(GameObject parent, MiniGameTemplate template)
    {
        // Catcher at bottom
        var catcher = CreateImage(parent.transform, "Catcher",
            new Vector2(0, -120), new Vector2(80, 25), Color.white);
        
        // Falling object
        var falling = CreateImage(parent.transform, "FallingObject",
            new Vector2(0, 180), new Vector2(35, 35), Color.yellow);
        
        // Score
        CreateText(parent.transform, "ScoreText", "0 / 10", 28,
            new Vector2(0.5f, 0f), new Vector2(0, -180), new Vector2(150, 40), Color.white);
    }
    
    static void CreateRapidKeysUI(GameObject parent, MiniGameTemplate template)
    {
        // Key display (big letter)
        CreateText(parent.transform, "KeyDisplay", "A", 96,
            new Vector2(0.5f, 0.5f), new Vector2(0, 30), new Vector2(200, 120), Color.yellow);
        
        // Instructions
        CreateText(parent.transform, "KeyHint", "A S D F J K L", 24,
            new Vector2(0.5f, 0.5f), new Vector2(0, -60), new Vector2(300, 35), Color.gray);
        
        // Score
        CreateText(parent.transform, "ScoreText", "0 / 15", 28,
            new Vector2(0.5f, 0f), new Vector2(0, -120), new Vector2(150, 40), Color.white);
    }
    
    static void CreateHigherLowerUI(GameObject parent, MiniGameTemplate template)
    {
        // Current number display
        CreateText(parent.transform, "CurrentNumber", "50", 72,
            new Vector2(0.5f, 0.5f), new Vector2(0, 60), new Vector2(200, 90), Color.white);
        
        // Buttons
        GameObject higherBtn = new GameObject("HigherButton");
        higherBtn.transform.SetParent(parent.transform, false);
        var hRect = higherBtn.AddComponent<RectTransform>();
        hRect.anchoredPosition = new Vector2(-80, -40);
        hRect.sizeDelta = new Vector2(120, 60);
        var hImg = higherBtn.AddComponent<Image>();
        hImg.color = Color.green;
        higherBtn.AddComponent<Button>().targetGraphic = hImg;
        CreateText(higherBtn.transform, "Label", "HIGHER", 24,
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(120, 60), Color.white);
        
        GameObject lowerBtn = new GameObject("LowerButton");
        lowerBtn.transform.SetParent(parent.transform, false);
        var lRect = lowerBtn.AddComponent<RectTransform>();
        lRect.anchoredPosition = new Vector2(80, -40);
        lRect.sizeDelta = new Vector2(120, 60);
        var lImg = lowerBtn.AddComponent<Image>();
        lImg.color = Color.red;
        lowerBtn.AddComponent<Button>().targetGraphic = lImg;
        CreateText(lowerBtn.transform, "Label", "LOWER", 24,
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(120, 60), Color.white);
        
        // Score
        CreateText(parent.transform, "ScoreText", "0 / 8", 28,
            new Vector2(0.5f, 0f), new Vector2(0, -120), new Vector2(150, 40), Color.white);
    }
    
    static void CreateWordTypeUI(GameObject parent, MiniGameTemplate template)
    {
        // Word display
        CreateText(parent.transform, "WordDisplay", "PROFIT", 56,
            new Vector2(0.5f, 0.5f), new Vector2(0, 60), new Vector2(400, 70), Color.yellow);
        
        // Input field
        GameObject inputObj = new GameObject("InputField");
        inputObj.transform.SetParent(parent.transform, false);
        var inputRect = inputObj.AddComponent<RectTransform>();
        inputRect.anchoredPosition = new Vector2(0, -30);
        inputRect.sizeDelta = new Vector2(300, 55);
        inputObj.AddComponent<Image>().color = new Color(0.2f, 0.2f, 0.25f);
        var input = inputObj.AddComponent<TMP_InputField>();
        
        GameObject textArea = new GameObject("Text Area");
        textArea.transform.SetParent(inputObj.transform, false);
        var taRect = textArea.AddComponent<RectTransform>();
        taRect.anchorMin = Vector2.zero; taRect.anchorMax = Vector2.one; taRect.sizeDelta = new Vector2(-20, 0);
        
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(textArea.transform, false);
        var tRect = textObj.AddComponent<RectTransform>();
        tRect.anchorMin = Vector2.zero; tRect.anchorMax = Vector2.one; tRect.sizeDelta = Vector2.zero;
        var inputText = textObj.AddComponent<TextMeshProUGUI>();
        inputText.fontSize = 32; inputText.alignment = TextAlignmentOptions.Center;
        input.textComponent = inputText;
        
        // Score
        CreateText(parent.transform, "ScoreText", "0 / 6", 28,
            new Vector2(0.5f, 0f), new Vector2(0, -120), new Vector2(150, 40), Color.white);
    }
    
    static void CreateSafeCrackerUI(GameObject parent, MiniGameTemplate template)
    {
        // Dial
        var dial = CreateImage(parent.transform, "Dial",
            new Vector2(0, 20), new Vector2(150, 150), new Color(0.3f, 0.3f, 0.35f));
        
        // Dial indicator
        CreateImage(dial.transform, "Indicator",
            new Vector2(0, 60), new Vector2(10, 30), Color.red);
        
        // Target display
        CreateText(parent.transform, "TargetText", "Find: 3-7-1", 28,
            new Vector2(0.5f, 0.5f), new Vector2(0, 130), new Vector2(300, 40), Color.yellow);
        
        // Current number
        CreateText(parent.transform, "CurrentText", "0", 48,
            new Vector2(0.5f, 0.5f), new Vector2(0, 20), new Vector2(100, 60), Color.white);
        
        // Progress
        CreateText(parent.transform, "ProgressText", "0 / 3", 24,
            new Vector2(0.5f, 0f), new Vector2(0, -120), new Vector2(150, 35), Color.white);
    }
    
    static void CreateCoinFlipUI(GameObject parent, MiniGameTemplate template)
    {
        // Coin image
        var coin = CreateImage(parent.transform, "CoinImage",
            new Vector2(0, 40), new Vector2(120, 120), Color.yellow);
        
        // Result text
        CreateText(parent.transform, "ResultText", "Make your guess!", 28,
            new Vector2(0.5f, 0.5f), new Vector2(0, -50), new Vector2(350, 40), Color.white);
        
        // Key hints
        CreateText(parent.transform, "KeyHint", "H = Heads    T = Tails", 22,
            new Vector2(0.5f, 0.5f), new Vector2(0, -100), new Vector2(300, 30), Color.gray);
        
        // Score
        CreateText(parent.transform, "ScoreText", "0 / 5", 28,
            new Vector2(0.5f, 0f), new Vector2(0, -150), new Vector2(150, 40), Color.white);
    }
    
    static void CreateSequenceMemoryUI(GameObject parent, MiniGameTemplate template)
    {
        // 3x3 grid
        for (int row = 0; row < 3; row++)
        {
            for (int col = 0; col < 3; col++)
            {
                int idx = row * 3 + col;
                float x = (col - 1) * 90;
                float y = (1 - row) * 90;
                
                GameObject btn = new GameObject($"GridButton{idx}");
                btn.transform.SetParent(parent.transform, false);
                var rect = btn.AddComponent<RectTransform>();
                rect.anchoredPosition = new Vector2(x, y);
                rect.sizeDelta = new Vector2(75, 75);
                var img = btn.AddComponent<Image>();
                img.color = Color.gray;
                btn.AddComponent<Button>().targetGraphic = img;
            }
        }
        
        // Status
        CreateText(parent.transform, "StatusText", "Watch...", 24,
            new Vector2(0.5f, 0f), new Vector2(0, -150), new Vector2(200, 35), Color.white);
    }
    
    static void CreateOddOneOutUI(GameObject parent, MiniGameTemplate template)
    {
        // 3x3 grid of items
        for (int row = 0; row < 3; row++)
        {
            for (int col = 0; col < 3; col++)
            {
                int idx = row * 3 + col;
                float x = (col - 1) * 90;
                float y = (1 - row) * 90;
                
                GameObject btn = new GameObject($"ItemButton{idx}");
                btn.transform.SetParent(parent.transform, false);
                var rect = btn.AddComponent<RectTransform>();
                rect.anchoredPosition = new Vector2(x, y);
                rect.sizeDelta = new Vector2(70, 70);
                var img = btn.AddComponent<Image>();
                img.color = Color.red;
                btn.AddComponent<Button>().targetGraphic = img;
            }
        }
        
        // Score
        CreateText(parent.transform, "ScoreText", "0 / 5", 28,
            new Vector2(0.5f, 0f), new Vector2(0, -150), new Vector2(150, 40), Color.white);
    }
    
    static void CreateSpeedCountUI(GameObject parent, MiniGameTemplate template)
    {
        // Item container
        var container = CreateImage(parent.transform, "ItemContainer",
            new Vector2(0, 40), new Vector2(400, 200), new Color(0.15f, 0.15f, 0.2f));
        
        // Input field
        GameObject inputObj = new GameObject("InputField");
        inputObj.transform.SetParent(parent.transform, false);
        var inputRect = inputObj.AddComponent<RectTransform>();
        inputRect.anchoredPosition = new Vector2(0, -100);
        inputRect.sizeDelta = new Vector2(150, 50);
        inputObj.AddComponent<Image>().color = new Color(0.2f, 0.2f, 0.25f);
        var input = inputObj.AddComponent<TMP_InputField>();
        
        GameObject textArea = new GameObject("Text Area");
        textArea.transform.SetParent(inputObj.transform, false);
        var taRect = textArea.AddComponent<RectTransform>();
        taRect.anchorMin = Vector2.zero; taRect.anchorMax = Vector2.one; taRect.sizeDelta = Vector2.zero;
        
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(textArea.transform, false);
        var tRect = textObj.AddComponent<RectTransform>();
        tRect.anchorMin = Vector2.zero; tRect.anchorMax = Vector2.one; tRect.sizeDelta = Vector2.zero;
        var inputText = textObj.AddComponent<TextMeshProUGUI>();
        inputText.fontSize = 32; inputText.alignment = TextAlignmentOptions.Center;
        input.textComponent = inputText;
        input.contentType = TMP_InputField.ContentType.IntegerNumber;
        
        // Score
        CreateText(parent.transform, "ScoreText", "0 / 5", 28,
            new Vector2(0.5f, 0f), new Vector2(0, -160), new Vector2(150, 40), Color.white);
    }
    
    static void CreatePatternMatchUI(GameObject parent, MiniGameTemplate template)
    {
        // Pattern display
        CreateText(parent.transform, "PatternDisplay", "↑ ↓ ← →", 48,
            new Vector2(0.5f, 0.5f), new Vector2(0, 60), new Vector2(400, 60), Color.yellow);
        
        // Input display
        CreateText(parent.transform, "InputDisplay", "", 48,
            new Vector2(0.5f, 0.5f), new Vector2(0, -20), new Vector2(400, 60), Color.white);
        
        // Status
        CreateText(parent.transform, "StatusText", "Memorize!", 24,
            new Vector2(0.5f, 0f), new Vector2(0, -100), new Vector2(200, 35), Color.gray);
        
        // Arrow key hint
        CreateText(parent.transform, "KeyHint", "Use Arrow Keys", 20,
            new Vector2(0.5f, 0f), new Vector2(0, -140), new Vector2(200, 30), Color.gray);
    }
    
    // === HELPER METHODS ===
    
    static GameObject CreateText(Transform parent, string name, string text, int fontSize,
        Vector2 anchor, Vector2 position, Vector2 size, Color color)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        
        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        
        TextMeshProUGUI tmp = obj.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontStyle = FontStyles.Bold;
        
        return obj;
    }
    
    static Image CreateImage(Transform parent, string name, Vector2 position, Vector2 size, Color color)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        
        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        
        Image img = obj.AddComponent<Image>();
        img.color = color;
        
        return img;
    }
}
#endif
