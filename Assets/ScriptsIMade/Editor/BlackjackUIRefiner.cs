#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Editor tool to refine the Blackjack panel to match Company Stats UI style
/// </summary>
public class BlackjackUIRefiner : EditorWindow
{
    private GameObject targetPanel;
    
    // Style settings matching Company Stats UI
    private readonly Color bgColor = new Color(0, 0, 0, 0.85f);
    private readonly Color textColor = Color.white;
    private readonly Color labelColor = new Color(0.7f, 0.7f, 0.7f);
    private readonly Color goldColor = new Color(1f, 0.8f, 0.2f);
    private readonly Color greenColor = new Color(0.2f, 0.8f, 0.2f);
    private readonly Color redColor = new Color(0.9f, 0.3f, 0.3f);
    private readonly Color blueColor = new Color(0.3f, 0.5f, 0.9f);
    private readonly Color separatorColor = new Color(1, 1, 1, 0.2f);
    
    [MenuItem("Tools/Game/Refine Blackjack UI")]
    public static void ShowWindow()
    {
        GetWindow<BlackjackUIRefiner>("Blackjack Refiner");
    }
    
    void OnGUI()
    {
        GUILayout.Label("Blackjack Panel Refiner", EditorStyles.boldLabel);
        GUILayout.Space(10);
        
        EditorGUILayout.HelpBox(
            "Refines the Blackjack panel to match Company Stats UI style:\n\n" +
            "• Dark semi-transparent background\n" +
            "• Organized layout with sections\n" +
            "• Styled scores and hands display\n" +
            "• Color-coded buttons (Hit=Green, Stand=Blue, Close=Red)",
            MessageType.Info);
        
        GUILayout.Space(10);
        
        targetPanel = (GameObject)EditorGUILayout.ObjectField("Target Panel", targetPanel, typeof(GameObject), true);
        
        GUILayout.Space(5);
        
        if (targetPanel == null)
        {
            if (GUILayout.Button("Find Blackjack Panel"))
            {
                FindBlackjackPanel();
            }
        }
        
        GUILayout.Space(15);
        
        GUI.enabled = targetPanel != null;
        
        if (GUILayout.Button("REFINE PANEL STYLE", GUILayout.Height(40)))
        {
            RefinePanel();
        }
        
        GUILayout.Space(10);
        
        if (GUILayout.Button("CREATE NEW STYLED PANEL", GUILayout.Height(30)))
        {
            CreateNewStyledPanel();
        }
        
        GUI.enabled = true;
    }
    
    void FindBlackjackPanel()
    {
        GameObject found = GameObject.Find("Blackjack");
        if (found == null) found = GameObject.Find("BlackjackPanel");
        if (found == null) found = GameObject.Find("BlackJack");
        
        if (found != null)
        {
            targetPanel = found;
            Debug.Log($"Found panel: {found.name}");
        }
        else if (Selection.activeGameObject != null)
        {
            targetPanel = Selection.activeGameObject;
            Debug.Log($"Using selected object: {targetPanel.name}");
        }
        else
        {
            Debug.LogWarning("Could not find Blackjack panel. Please select it manually.");
        }
    }
    
    void RefinePanel()
    {
        if (targetPanel == null) return;
        
        Undo.RegisterCompleteObjectUndo(targetPanel, "Refine Blackjack Panel");
        
        // Background
        Image bg = targetPanel.GetComponent<Image>();
        if (bg == null) bg = targetPanel.AddComponent<Image>();
        bg.color = bgColor;
        
        // Add VerticalLayoutGroup
        VerticalLayoutGroup vlg = targetPanel.GetComponent<VerticalLayoutGroup>();
        if (vlg == null) vlg = targetPanel.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(20, 20, 20, 20);
        vlg.spacing = 10;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        
        // ContentSizeFitter
        ContentSizeFitter csf = targetPanel.GetComponent<ContentSizeFitter>();
        if (csf == null) csf = targetPanel.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        
        // Style all children
        StyleChildElements(targetPanel.transform);
        
        EditorUtility.SetDirty(targetPanel);
        Debug.Log("Blackjack panel refined successfully!");
    }
    
    void StyleChildElements(Transform parent)
    {
        foreach (Transform child in parent)
        {
            string childName = child.name.ToLower();
            
            // Style TextMeshPro elements
            TextMeshProUGUI tmp = child.GetComponent<TextMeshProUGUI>();
            if (tmp != null)
            {
                StyleTextElement(tmp, childName);
            }
            
            // Style buttons
            Button btn = child.GetComponent<Button>();
            if (btn != null)
            {
                StyleButton(btn, childName);
            }
            
            // Add layout element
            LayoutElement le = child.GetComponent<LayoutElement>();
            if (le == null) le = child.gameObject.AddComponent<LayoutElement>();
            
            if (tmp != null)
            {
                le.preferredHeight = Mathf.Max(tmp.fontSize + 10, 25);
            }
            else if (btn != null)
            {
                le.preferredHeight = 45;
                le.minWidth = 100;
            }
            
            // Recurse
            if (child.childCount > 0)
            {
                StyleChildElements(child);
            }
        }
    }
    
    void StyleTextElement(TextMeshProUGUI tmp, string elementName)
    {
        Undo.RecordObject(tmp, "Style Text");
        
        if (elementName.Contains("playerhand") || elementName.Contains("player") && elementName.Contains("hand"))
        {
            tmp.fontSize = 16;
            tmp.color = textColor;
            tmp.alignment = TextAlignmentOptions.Center;
        }
        else if (elementName.Contains("dealerhand") || elementName.Contains("dealer") && elementName.Contains("hand"))
        {
            tmp.fontSize = 16;
            tmp.color = textColor;
            tmp.alignment = TextAlignmentOptions.Center;
        }
        else if (elementName.Contains("playerscore"))
        {
            tmp.fontSize = 28;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = blueColor;
            tmp.alignment = TextAlignmentOptions.Center;
        }
        else if (elementName.Contains("dealerscore"))
        {
            tmp.fontSize = 28;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = redColor;
            tmp.alignment = TextAlignmentOptions.Center;
        }
        else if (elementName.Contains("result"))
        {
            tmp.fontSize = 32;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = goldColor;
            tmp.alignment = TextAlignmentOptions.Center;
        }
        else if (elementName.Contains("stake") || elementName.Contains("bet"))
        {
            tmp.fontSize = 22;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = greenColor;
            tmp.alignment = TextAlignmentOptions.Center;
        }
        else
        {
            tmp.fontSize = 18;
            tmp.color = textColor;
            tmp.alignment = TextAlignmentOptions.Center;
        }
        
        EditorUtility.SetDirty(tmp);
    }
    
    void StyleButton(Button btn, string buttonName)
    {
        Undo.RecordObject(btn, "Style Button");
        
        Image btnImg = btn.GetComponent<Image>();
        if (btnImg != null)
        {
            Undo.RecordObject(btnImg, "Style Button Image");
            
            if (buttonName.Contains("hit"))
            {
                btnImg.color = new Color(0.2f, 0.6f, 0.3f, 0.95f); // Green
            }
            else if (buttonName.Contains("stand"))
            {
                btnImg.color = new Color(0.3f, 0.4f, 0.7f, 0.95f); // Blue
            }
            else if (buttonName.Contains("close") || buttonName.Contains("quit") || buttonName.Contains("exit"))
            {
                btnImg.color = new Color(0.6f, 0.2f, 0.2f, 0.95f); // Red
            }
            else if (buttonName.Contains("double"))
            {
                btnImg.color = new Color(0.7f, 0.5f, 0.1f, 0.95f); // Gold
            }
            else
            {
                btnImg.color = new Color(0.3f, 0.3f, 0.4f, 0.95f); // Gray
            }
            
            EditorUtility.SetDirty(btnImg);
        }
        
        // Style button text
        TextMeshProUGUI btnText = btn.GetComponentInChildren<TextMeshProUGUI>();
        if (btnText != null)
        {
            Undo.RecordObject(btnText, "Style Button Text");
            btnText.fontSize = 20;
            btnText.fontStyle = FontStyles.Bold;
            btnText.color = Color.white;
            btnText.alignment = TextAlignmentOptions.Center;
            EditorUtility.SetDirty(btnText);
        }
        
        ColorBlock colors = btn.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.2f, 1.2f, 1.2f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
        btn.colors = colors;
        
        EditorUtility.SetDirty(btn);
    }
    
    void CreateNewStyledPanel()
    {
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            EditorUtility.DisplayDialog("Error", "No Canvas found!", "OK");
            return;
        }
        
        // Main panel
        GameObject panel = new GameObject("BlackjackPanel");
        panel.transform.SetParent(canvas.transform, false);
        Undo.RegisterCreatedObjectUndo(panel, "Create Blackjack Panel");
        
        RectTransform panelRect = panel.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = Vector2.zero;
        
        Image panelBg = panel.AddComponent<Image>();
        panelBg.color = bgColor;
        
        VerticalLayoutGroup vlg = panel.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(25, 25, 20, 20);
        vlg.spacing = 8;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        
        ContentSizeFitter csf = panel.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        
        // ===== TITLE =====
        CreateHeader(panel.transform, "BLACKJACK", goldColor, 28);
        
        CreateSeparator(panel.transform);
        
        // ===== STAKE =====
        CreateTextElement(panel.transform, "Stake", "Stake: $1,000", 20, FontStyles.Bold, greenColor);
        
        CreateSeparator(panel.transform);
        
        // ===== DEALER SECTION =====
        CreateHeader(panel.transform, "DEALER", labelColor, 12);
        CreateTextElement(panel.transform, "DealerHand", "🂠 🂠", 24, FontStyles.Normal, textColor);
        CreateScoreRow(panel.transform, "DealerScore", "Score", "?", redColor);
        
        CreateSeparator(panel.transform);
        
        // ===== PLAYER SECTION =====
        CreateHeader(panel.transform, "YOUR HAND", labelColor, 12);
        CreateTextElement(panel.transform, "PlayerHand", "🂡 🂮", 24, FontStyles.Normal, textColor);
        CreateScoreRow(panel.transform, "PlayerScore", "Score", "21", blueColor);
        
        CreateSeparator(panel.transform);
        
        // ===== RESULT =====
        CreateTextElement(panel.transform, "Result", "", 28, FontStyles.Bold, goldColor);
        
        CreateSeparator(panel.transform);
        
        // ===== BUTTONS =====
        GameObject buttonRow = new GameObject("ButtonRow");
        buttonRow.transform.SetParent(panel.transform, false);
        RectTransform btnRowRect = buttonRow.AddComponent<RectTransform>();
        HorizontalLayoutGroup hlg = buttonRow.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 15;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = false;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        LayoutElement btnRowLE = buttonRow.AddComponent<LayoutElement>();
        btnRowLE.preferredHeight = 50;
        
        CreateStyledButton(buttonRow.transform, "Hit", "HIT", new Color(0.2f, 0.6f, 0.3f, 0.95f), 90);
        CreateStyledButton(buttonRow.transform, "Stand", "STAND", new Color(0.3f, 0.4f, 0.7f, 0.95f), 90);
        CreateStyledButton(buttonRow.transform, "Close", "CLOSE", new Color(0.6f, 0.2f, 0.2f, 0.95f), 90);
        
        Selection.activeGameObject = panel;
        targetPanel = panel;
        
        Debug.Log("Created new styled Blackjack panel!");
    }
    
    void CreateHeader(Transform parent, string text, Color color, int fontSize)
    {
        GameObject header = new GameObject(text.Replace(" ", "") + "Header");
        header.transform.SetParent(parent, false);
        
        RectTransform rect = header.AddComponent<RectTransform>();
        
        TextMeshProUGUI tmp = header.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontStyle = FontStyles.Bold;
        
        LayoutElement le = header.AddComponent<LayoutElement>();
        le.preferredHeight = fontSize + 8;
    }
    
    void CreateTextElement(Transform parent, string name, string text, int fontSize, FontStyles style, Color color)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        
        RectTransform rect = obj.AddComponent<RectTransform>();
        
        TextMeshProUGUI tmp = obj.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        
        LayoutElement le = obj.AddComponent<LayoutElement>();
        le.preferredHeight = fontSize + 10;
    }
    
    void CreateScoreRow(Transform parent, string name, string label, string value, Color valueColor)
    {
        GameObject row = new GameObject(name);
        row.transform.SetParent(parent, false);
        
        HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 10;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = false;
        hlg.childControlHeight = true;
        
        // Label
        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(row.transform, false);
        RectTransform labelRect = labelObj.AddComponent<RectTransform>();
        labelRect.sizeDelta = new Vector2(60, 30);
        TextMeshProUGUI labelTMP = labelObj.AddComponent<TextMeshProUGUI>();
        labelTMP.text = label;
        labelTMP.fontSize = 16;
        labelTMP.color = labelColor;
        labelTMP.alignment = TextAlignmentOptions.Right;
        
        // Value
        GameObject valueObj = new GameObject("Value");
        valueObj.transform.SetParent(row.transform, false);
        RectTransform valueRect = valueObj.AddComponent<RectTransform>();
        valueRect.sizeDelta = new Vector2(60, 30);
        TextMeshProUGUI valueTMP = valueObj.AddComponent<TextMeshProUGUI>();
        valueTMP.text = value;
        valueTMP.fontSize = 28;
        valueTMP.fontStyle = FontStyles.Bold;
        valueTMP.color = valueColor;
        valueTMP.alignment = TextAlignmentOptions.Left;
        
        LayoutElement le = row.AddComponent<LayoutElement>();
        le.preferredHeight = 35;
    }
    
    void CreateSeparator(Transform parent)
    {
        GameObject sep = new GameObject("Separator");
        sep.transform.SetParent(parent, false);
        
        RectTransform rect = sep.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(200, 2);
        
        Image img = sep.AddComponent<Image>();
        img.color = separatorColor;
        
        LayoutElement le = sep.AddComponent<LayoutElement>();
        le.preferredHeight = 2;
        le.flexibleWidth = 1;
    }
    
    void CreateStyledButton(Transform parent, string name, string text, Color bgColor, float width)
    {
        GameObject btnObj = new GameObject(name);
        btnObj.transform.SetParent(parent, false);
        
        RectTransform rect = btnObj.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(width, 45);
        
        Image img = btnObj.AddComponent<Image>();
        img.color = bgColor;
        
        Button btn = btnObj.AddComponent<Button>();
        ColorBlock colors = btn.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.2f, 1.2f, 1.2f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
        btn.colors = colors;
        
        // Text
        GameObject textObj = new GameObject("Text (TMP)");
        textObj.transform.SetParent(btnObj.transform, false);
        
        RectTransform textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = Vector2.zero;
        
        TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = 18;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
    }
}
#endif
