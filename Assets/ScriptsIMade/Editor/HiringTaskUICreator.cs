#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Editor tool to create the Hiring Task UI panel - matches Company Stats UI style
/// </summary>
public class HiringTaskUICreator : EditorWindow
{
    private enum PanelPosition
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight,
        TopCenter
    }
    
    private Canvas targetCanvas;
    private bool createManager = true;
    private bool createDatabase = true;
    private PanelPosition panelPosition = PanelPosition.TopRight;
    
    // Style settings to match Company Stats UI
    private readonly Color bgColor = new Color(0, 0, 0, 0.7f);
    private readonly Color textColor = Color.white;
    private readonly Color labelColor = new Color(0.7f, 0.7f, 0.7f);
    private readonly Color accentColor = new Color(1f, 0.8f, 0.2f); // Gold
    private readonly Color timerGreen = new Color(0.2f, 0.8f, 0.2f);
    private readonly Color separatorColor = new Color(1, 1, 1, 0.2f);
    
    [MenuItem("Tools/Game/Create Hiring Task UI")]
    public static void ShowWindow()
    {
        GetWindow<HiringTaskUICreator>("Hiring Task UI Creator");
    }
    
    void OnGUI()
    {
        GUILayout.Label("Hiring Task UI Creator", EditorStyles.boldLabel);
        GUILayout.Space(10);
        
        EditorGUILayout.HelpBox(
            "Creates a Hiring Task UI panel that matches the Company Stats UI style.\n\n" +
            "• Same dark theme\n" +
            "• Same layout style\n" +
            "• Same colors and spacing",
            MessageType.Info);
        
        GUILayout.Space(10);
        
        targetCanvas = (Canvas)EditorGUILayout.ObjectField("Target Canvas", targetCanvas, typeof(Canvas), true);
        panelPosition = (PanelPosition)EditorGUILayout.EnumPopup("Panel Position", panelPosition);
        
        GUILayout.Space(5);
        createManager = EditorGUILayout.Toggle("Create Manager GameObject", createManager);
        createDatabase = EditorGUILayout.Toggle("Create Database Asset", createDatabase);
        
        GUILayout.Space(15);
        
        if (targetCanvas == null)
        {
            EditorGUILayout.HelpBox("Please assign a Canvas to create the UI in.", MessageType.Warning);
            
            if (GUILayout.Button("Find Canvas in Scene"))
            {
                targetCanvas = FindObjectOfType<Canvas>();
                if (targetCanvas != null)
                    Debug.Log($"Found canvas: {targetCanvas.name}");
                else
                    Debug.LogWarning("No Canvas found in scene. Please create one first.");
            }
        }
        
        GUILayout.Space(10);
        
        GUI.enabled = targetCanvas != null;
        
        if (GUILayout.Button("CREATE HIRING TASK UI", GUILayout.Height(40)))
        {
            CreateHiringTaskUI();
        }
        
        GUI.enabled = true;
        
        GUILayout.Space(20);
        GUILayout.Label("Individual Actions", EditorStyles.boldLabel);
        
        if (GUILayout.Button("Create Database Asset Only"))
        {
            CreateDatabaseAsset();
        }
        
        if (GUILayout.Button("Create Manager GameObject Only"))
        {
            CreateManagerGameObject();
        }
    }
    
    void CreateHiringTaskUI()
    {
        if (targetCanvas == null)
        {
            Debug.LogError("No canvas assigned!");
            return;
        }
        
        // Create the main panel
        GameObject taskPanel = CreateTaskPanel();
        
        // Create manager if requested
        HiringTaskManager manager = null;
        if (createManager)
        {
            manager = CreateManagerGameObject();
        }
        else
        {
            manager = FindObjectOfType<HiringTaskManager>();
        }
        
        // Create database if requested
        HiringTaskDatabase database = null;
        if (createDatabase)
        {
            database = CreateDatabaseAsset();
        }
        
        // Wire up references
        if (manager != null)
        {
            WireUpManager(manager, taskPanel, database);
        }
        
        // Select the panel
        Selection.activeGameObject = taskPanel;
        
        Debug.Log("Hiring Task UI created successfully!");
        EditorUtility.DisplayDialog("Success", 
            "Hiring Task UI has been created!\n\n" +
            "The panel is set up with all necessary elements.\n" +
            "You may want to adjust colors and fonts to match your game style.", 
            "OK");
    }
    
    GameObject CreateTaskPanel()
    {
        // Check if panel already exists
        Transform existing = targetCanvas.transform.Find("HiringTaskPanel");
        if (existing != null)
        {
            Debug.Log("HiringTaskPanel already exists - updating it");
            return existing.gameObject;
        }
        
        // Main Panel - matches CompanyStatsPanel style
        GameObject panel = new GameObject("HiringTaskPanel");
        panel.transform.SetParent(targetCanvas.transform, false);
        Undo.RegisterCreatedObjectUndo(panel, "Create Hiring Task Panel");
        
        RectTransform panelRect = panel.AddComponent<RectTransform>();
        SetPanelPosition(panelRect);
        
        Image panelImage = panel.AddComponent<Image>();
        panelImage.color = bgColor;
        
        // Vertical Layout Group - same as CompanyStatsUI
        VerticalLayoutGroup vlg = panel.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(15, 15, 15, 15);
        vlg.spacing = 8;
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        
        ContentSizeFitter csf = panel.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        
        // ===== HEADER SECTION =====
        CreateHeader(panel.transform, "HIRING TASK", accentColor);
        
        // Title Row
        GameObject titleRow = CreateStatRow(panel.transform, "TitleRow", "Task", "URGENT TASK", 24, accentColor, labelColor);
        
        CreateSeparator(panel.transform);
        
        // ===== DESCRIPTION SECTION =====
        GameObject descObj = new GameObject("Description");
        descObj.transform.SetParent(panel.transform, false);
        RectTransform descRect = descObj.AddComponent<RectTransform>();
        descRect.sizeDelta = new Vector2(280, 50);
        TextMeshProUGUI descText = descObj.AddComponent<TextMeshProUGUI>();
        descText.text = "The board thinks we need more diversity. Time to fix the optics.";
        descText.fontSize = 14;
        descText.fontStyle = FontStyles.Italic;
        descText.alignment = TextAlignmentOptions.Left;
        descText.color = labelColor;
        descText.enableWordWrapping = true;
        LayoutElement descLE = descObj.AddComponent<LayoutElement>();
        descLE.preferredWidth = 280;
        descLE.preferredHeight = 50;
        
        CreateSeparator(panel.transform);
        
        // ===== REQUIREMENT SECTION =====
        CreateHeader(panel.transform, "OBJECTIVE", labelColor);
        
        GameObject reqObj = new GameObject("Requirement");
        reqObj.transform.SetParent(panel.transform, false);
        RectTransform reqRect = reqObj.AddComponent<RectTransform>();
        reqRect.sizeDelta = new Vector2(280, 30);
        TextMeshProUGUI reqText = reqObj.AddComponent<TextMeshProUGUI>();
        reqText.text = "► Hire a Black employee";
        reqText.fontSize = 18;
        reqText.fontStyle = FontStyles.Bold;
        reqText.alignment = TextAlignmentOptions.Left;
        reqText.color = accentColor;
        LayoutElement reqLE = reqObj.AddComponent<LayoutElement>();
        reqLE.preferredWidth = 280;
        reqLE.preferredHeight = 30;
        
        CreateSeparator(panel.transform);
        
        // ===== TIMER SECTION =====
        CreateHeader(panel.transform, "TIME REMAINING", labelColor);
        
        // Timer Row with bar
        GameObject timerRow = new GameObject("TimerRow");
        timerRow.transform.SetParent(panel.transform, false);
        RectTransform timerRowRect = timerRow.AddComponent<RectTransform>();
        HorizontalLayoutGroup timerHLG = timerRow.AddComponent<HorizontalLayoutGroup>();
        timerHLG.spacing = 10;
        timerHLG.childAlignment = TextAnchor.MiddleLeft;
        timerHLG.childControlWidth = false;
        timerHLG.childControlHeight = true;
        LayoutElement timerRowLE = timerRow.AddComponent<LayoutElement>();
        timerRowLE.preferredHeight = 25;
        
        // Timer Text
        GameObject timerTextObj = new GameObject("TimerText");
        timerTextObj.transform.SetParent(timerRow.transform, false);
        RectTransform timerTextRect = timerTextObj.AddComponent<RectTransform>();
        timerTextRect.sizeDelta = new Vector2(60, 25);
        TextMeshProUGUI timerText = timerTextObj.AddComponent<TextMeshProUGUI>();
        timerText.text = "1:30";
        timerText.fontSize = 20;
        timerText.fontStyle = FontStyles.Bold;
        timerText.alignment = TextAlignmentOptions.Left;
        timerText.color = textColor;
        
        // Timer Bar Background
        GameObject timerBg = new GameObject("TimerBackground");
        timerBg.transform.SetParent(timerRow.transform, false);
        RectTransform timerBgRect = timerBg.AddComponent<RectTransform>();
        timerBgRect.sizeDelta = new Vector2(180, 20);
        Image timerBgImg = timerBg.AddComponent<Image>();
        timerBgImg.color = new Color(0.2f, 0.2f, 0.2f, 1f);
        
        // Timer Fill
        GameObject timerFill = new GameObject("TimerFill");
        timerFill.transform.SetParent(timerBg.transform, false);
        RectTransform timerFillRect = timerFill.AddComponent<RectTransform>();
        timerFillRect.anchorMin = Vector2.zero;
        timerFillRect.anchorMax = Vector2.one;
        timerFillRect.sizeDelta = Vector2.zero;
        timerFillRect.anchoredPosition = Vector2.zero;
        Image timerFillImg = timerFill.AddComponent<Image>();
        timerFillImg.color = timerGreen;
        timerFillImg.type = Image.Type.Filled;
        timerFillImg.fillMethod = Image.FillMethod.Horizontal;
        timerFillImg.fillAmount = 0.75f;
        
        CreateSeparator(panel.transform);
        
        // ===== REWARD SECTION =====
        GameObject rewardRow = CreateStatRow(panel.transform, "RewardRow", "Reward", "$5,000", 18, timerGreen, labelColor);
        GameObject penaltyRow = CreateStatRow(panel.transform, "PenaltyRow", "Penalty", "-$2,000", 18, new Color(1f, 0.4f, 0.4f), labelColor);
        
        // ===== SUCCESS EFFECT (overlay, hidden) =====
        GameObject successEffect = new GameObject("SuccessEffect");
        successEffect.transform.SetParent(panel.transform, false);
        RectTransform successRect = successEffect.AddComponent<RectTransform>();
        successRect.anchorMin = Vector2.zero;
        successRect.anchorMax = Vector2.one;
        successRect.sizeDelta = Vector2.zero;
        Image successImg = successEffect.AddComponent<Image>();
        successImg.color = new Color(0.2f, 1f, 0.2f, 0.8f);
        successImg.raycastTarget = false;
        
        GameObject successTextObj = new GameObject("SuccessText");
        successTextObj.transform.SetParent(successEffect.transform, false);
        RectTransform successTextRect = successTextObj.AddComponent<RectTransform>();
        successTextRect.anchorMin = new Vector2(0.5f, 0.5f);
        successTextRect.anchorMax = new Vector2(0.5f, 0.5f);
        successTextRect.sizeDelta = new Vector2(250, 50);
        TextMeshProUGUI successText = successTextObj.AddComponent<TextMeshProUGUI>();
        successText.text = "TASK COMPLETE!";
        successText.fontSize = 24;
        successText.fontStyle = FontStyles.Bold;
        successText.alignment = TextAlignmentOptions.Center;
        successText.color = Color.white;
        successEffect.SetActive(false);
        
        // ===== FAIL EFFECT (overlay, hidden) =====
        GameObject failEffect = new GameObject("FailEffect");
        failEffect.transform.SetParent(panel.transform, false);
        RectTransform failRect = failEffect.AddComponent<RectTransform>();
        failRect.anchorMin = Vector2.zero;
        failRect.anchorMax = Vector2.one;
        failRect.sizeDelta = Vector2.zero;
        Image failImg = failEffect.AddComponent<Image>();
        failImg.color = new Color(1f, 0.2f, 0.2f, 0.8f);
        failImg.raycastTarget = false;
        
        GameObject failTextObj = new GameObject("FailText");
        failTextObj.transform.SetParent(failEffect.transform, false);
        RectTransform failTextRect = failTextObj.AddComponent<RectTransform>();
        failTextRect.anchorMin = new Vector2(0.5f, 0.5f);
        failTextRect.anchorMax = new Vector2(0.5f, 0.5f);
        failTextRect.sizeDelta = new Vector2(250, 50);
        TextMeshProUGUI failText = failTextObj.AddComponent<TextMeshProUGUI>();
        failText.text = "TASK FAILED!";
        failText.fontSize = 24;
        failText.fontStyle = FontStyles.Bold;
        failText.alignment = TextAlignmentOptions.Center;
        failText.color = Color.white;
        failEffect.SetActive(false);
        
        // Start hidden
        panel.SetActive(false);
        
        return panel;
    }
    
    void SetPanelPosition(RectTransform rect)
    {
        switch (panelPosition)
        {
            case PanelPosition.TopLeft:
                rect.anchorMin = new Vector2(0, 1);
                rect.anchorMax = new Vector2(0, 1);
                rect.pivot = new Vector2(0, 1);
                rect.anchoredPosition = new Vector2(20, -20);
                break;
            case PanelPosition.TopRight:
                rect.anchorMin = new Vector2(1, 1);
                rect.anchorMax = new Vector2(1, 1);
                rect.pivot = new Vector2(1, 1);
                rect.anchoredPosition = new Vector2(-20, -20);
                break;
            case PanelPosition.BottomLeft:
                rect.anchorMin = new Vector2(0, 0);
                rect.anchorMax = new Vector2(0, 0);
                rect.pivot = new Vector2(0, 0);
                rect.anchoredPosition = new Vector2(20, 20);
                break;
            case PanelPosition.BottomRight:
                rect.anchorMin = new Vector2(1, 0);
                rect.anchorMax = new Vector2(1, 0);
                rect.pivot = new Vector2(1, 0);
                rect.anchoredPosition = new Vector2(-20, 20);
                break;
            case PanelPosition.TopCenter:
                rect.anchorMin = new Vector2(0.5f, 1);
                rect.anchorMax = new Vector2(0.5f, 1);
                rect.pivot = new Vector2(0.5f, 1);
                rect.anchoredPosition = new Vector2(0, -20);
                break;
        }
    }
    
    GameObject CreateStatRow(Transform parent, string name, string labelText, string valueText, int fontSize, Color valueColor, Color lblColor)
    {
        GameObject row = new GameObject(name);
        row.transform.SetParent(parent, false);
        
        RectTransform rowRect = row.AddComponent<RectTransform>();
        HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
        
        hlg.spacing = 10;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = false;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        
        // Label
        GameObject labelGO = new GameObject("Label");
        labelGO.transform.SetParent(row.transform, false);
        RectTransform labelRect = labelGO.AddComponent<RectTransform>();
        labelRect.sizeDelta = new Vector2(120, 30);
        TextMeshProUGUI labelTMP = labelGO.AddComponent<TextMeshProUGUI>();
        labelTMP.text = labelText;
        labelTMP.fontSize = fontSize * 0.6f;
        labelTMP.color = lblColor;
        labelTMP.alignment = TextAlignmentOptions.Left;
        
        // Value
        GameObject valueGO = new GameObject("Value");
        valueGO.transform.SetParent(row.transform, false);
        RectTransform valueRect = valueGO.AddComponent<RectTransform>();
        valueRect.sizeDelta = new Vector2(150, 30);
        TextMeshProUGUI valueTMP = valueGO.AddComponent<TextMeshProUGUI>();
        valueTMP.text = valueText;
        valueTMP.fontSize = fontSize;
        valueTMP.color = valueColor;
        valueTMP.alignment = TextAlignmentOptions.Right;
        valueTMP.fontStyle = FontStyles.Bold;
        
        return row;
    }
    
    void CreateSeparator(Transform parent)
    {
        GameObject sep = new GameObject("Separator");
        sep.transform.SetParent(parent, false);
        
        RectTransform sepRect = sep.AddComponent<RectTransform>();
        sepRect.sizeDelta = new Vector2(200, 2);
        
        Image sepImage = sep.AddComponent<Image>();
        sepImage.color = separatorColor;
        
        LayoutElement le = sep.AddComponent<LayoutElement>();
        le.preferredHeight = 2;
        le.flexibleWidth = 1;
    }
    
    void CreateHeader(Transform parent, string text, Color color)
    {
        GameObject header = new GameObject(text.Replace(" ", "") + "Header");
        header.transform.SetParent(parent, false);
        
        RectTransform headerRect = header.AddComponent<RectTransform>();
        headerRect.sizeDelta = new Vector2(200, 20);
        
        TextMeshProUGUI headerTMP = header.AddComponent<TextMeshProUGUI>();
        headerTMP.text = text.ToUpper();
        headerTMP.fontSize = 12;
        headerTMP.color = color;
        headerTMP.alignment = TextAlignmentOptions.Left;
        headerTMP.fontStyle = FontStyles.Bold;
        
        LayoutElement le = header.AddComponent<LayoutElement>();
        le.preferredHeight = 20;
    }
    
    GameObject CreateUIElement(GameObject parent, string name, Vector2 anchorMin, Vector2 anchorMax, 
        Vector2 anchoredPos, Vector2 sizeDelta)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent.transform, false);
        
        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = sizeDelta;
        
        return obj;
    }
    
    HiringTaskManager CreateManagerGameObject()
    {
        // Check if manager already exists
        HiringTaskManager existing = FindObjectOfType<HiringTaskManager>();
        if (existing != null)
        {
            Debug.Log("HiringTaskManager already exists in scene");
            return existing;
        }
        
        GameObject managerObj = new GameObject("HiringTaskManager");
        Undo.RegisterCreatedObjectUndo(managerObj, "Create Hiring Task Manager");
        
        HiringTaskManager manager = managerObj.AddComponent<HiringTaskManager>();
        AudioSource audio = managerObj.AddComponent<AudioSource>();
        manager.audioSource = audio;
        
        Debug.Log("Created HiringTaskManager GameObject");
        return manager;
    }
    
    HiringTaskDatabase CreateDatabaseAsset()
    {
        // Check if database already exists
        string[] guids = AssetDatabase.FindAssets("t:HiringTaskDatabase");
        if (guids.Length > 0)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            HiringTaskDatabase existing = AssetDatabase.LoadAssetAtPath<HiringTaskDatabase>(path);
            Debug.Log($"Found existing HiringTaskDatabase at {path}");
            return existing;
        }
        
        // Create new database
        HiringTaskDatabase database = ScriptableObject.CreateInstance<HiringTaskDatabase>();
        database.InitializeDefaultTasks();
        
        string assetPath = "Assets/ScriptsIMade/HiringTaskDatabase.asset";
        
        // Ensure directory exists
        string directory = System.IO.Path.GetDirectoryName(assetPath);
        if (!System.IO.Directory.Exists(directory))
        {
            System.IO.Directory.CreateDirectory(directory);
        }
        
        AssetDatabase.CreateAsset(database, assetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        
        Debug.Log($"Created HiringTaskDatabase at {assetPath}");
        return database;
    }
    
    void WireUpManager(HiringTaskManager manager, GameObject panel, HiringTaskDatabase database)
    {
        Undo.RecordObject(manager, "Wire Up Hiring Task Manager");
        
        manager.taskPanel = panel;
        manager.taskDatabase = database;
        
        // Title - in TitleRow
        Transform titleRow = panel.transform.Find("TitleRow");
        if (titleRow != null)
        {
            Transform titleValue = titleRow.Find("Value");
            if (titleValue != null)
                manager.titleText = titleValue.GetComponent<TextMeshProUGUI>();
        }
        
        // Description
        Transform desc = panel.transform.Find("Description");
        if (desc != null)
            manager.descriptionText = desc.GetComponent<TextMeshProUGUI>();
        
        // Requirement
        Transform req = panel.transform.Find("Requirement");
        if (req != null)
            manager.requirementText = req.GetComponent<TextMeshProUGUI>();
        
        // Timer Row
        Transform timerRow = panel.transform.Find("TimerRow");
        if (timerRow != null)
        {
            Transform timerText = timerRow.Find("TimerText");
            if (timerText != null)
                manager.timerText = timerText.GetComponent<TextMeshProUGUI>();
            
            Transform timerBg = timerRow.Find("TimerBackground");
            if (timerBg != null)
            {
                Transform timerFill = timerBg.Find("TimerFill");
                if (timerFill != null)
                    manager.timerFill = timerFill.GetComponent<Image>();
            }
        }
        
        // Reward Row
        Transform rewardRow = panel.transform.Find("RewardRow");
        if (rewardRow != null)
        {
            Transform rewardValue = rewardRow.Find("Value");
            if (rewardValue != null)
                manager.rewardText = rewardValue.GetComponent<TextMeshProUGUI>();
        }
        
        // Penalty Row
        Transform penaltyRow = panel.transform.Find("PenaltyRow");
        if (penaltyRow != null)
        {
            Transform penaltyValue = penaltyRow.Find("Value");
            if (penaltyValue != null)
                manager.penaltyText = penaltyValue.GetComponent<TextMeshProUGUI>();
        }
        
        // Effects
        Transform success = panel.transform.Find("SuccessEffect");
        if (success != null)
            manager.successEffect = success.gameObject;
        
        Transform fail = panel.transform.Find("FailEffect");
        if (fail != null)
            manager.failEffect = fail.gameObject;
        
        EditorUtility.SetDirty(manager);
        
        Debug.Log("Wired up all UI references to HiringTaskManager");
    }
}
#endif
