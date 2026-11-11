#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Editor tool to refine the Results panel to match Company Stats UI style
/// </summary>
public class ResultsUIRefiner : EditorWindow
{
    private GameObject targetPanel;
    
    // Style settings
    private readonly Color bgColor = new Color(0, 0, 0, 0.85f);
    private readonly Color textColor = Color.white;
    private readonly Color labelColor = new Color(0.7f, 0.7f, 0.7f);
    private readonly Color goldColor = new Color(1f, 0.8f, 0.2f);
    private readonly Color greenColor = new Color(0.2f, 0.8f, 0.2f);
    private readonly Color redColor = new Color(0.9f, 0.3f, 0.3f);
    private readonly Color separatorColor = new Color(1, 1, 1, 0.2f);
    
    [MenuItem("Tools/Game/Refine Results UI")]
    public static void ShowWindow()
    {
        GetWindow<ResultsUIRefiner>("Results Refiner");
    }
    
    void OnGUI()
    {
        GUILayout.Label("Results Panel Refiner", EditorStyles.boldLabel);
        GUILayout.Space(10);
        
        EditorGUILayout.HelpBox(
            "Refines the Results panel to match Company Stats UI style:\n\n" +
            "• Dark semi-transparent background\n" +
            "• Result text (Win/Lose) in gold\n" +
            "• Outcome details styled\n" +
            "• Close button in red",
            MessageType.Info);
        
        GUILayout.Space(10);
        
        targetPanel = (GameObject)EditorGUILayout.ObjectField("Target Panel", targetPanel, typeof(GameObject), true);
        
        GUILayout.Space(5);
        
        if (targetPanel == null)
        {
            if (GUILayout.Button("Find Results Panel"))
            {
                FindResultsPanel();
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
    
    void FindResultsPanel()
    {
        GameObject found = GameObject.Find("Results");
        if (found == null) found = GameObject.Find("ResultsPanel");
        if (found == null) found = GameObject.Find("ResultPanel");
        if (found == null) found = GameObject.Find("Result");
        
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
            Debug.LogWarning("Could not find Results panel. Please select it manually.");
        }
    }
    
    void RefinePanel()
    {
        if (targetPanel == null) return;
        
        Undo.RegisterCompleteObjectUndo(targetPanel, "Refine Results Panel");
        
        // Background
        Image bg = targetPanel.GetComponent<Image>();
        if (bg == null) bg = targetPanel.AddComponent<Image>();
        bg.color = bgColor;
        
        // VerticalLayoutGroup
        VerticalLayoutGroup vlg = targetPanel.GetComponent<VerticalLayoutGroup>();
        if (vlg == null) vlg = targetPanel.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(30, 30, 25, 25);
        vlg.spacing = 12;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = false;
        vlg.childForceExpandHeight = false;
        
        // ContentSizeFitter
        ContentSizeFitter csf = targetPanel.GetComponent<ContentSizeFitter>();
        if (csf == null) csf = targetPanel.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        
        // Style children
        StyleChildElements(targetPanel.transform);
        
        EditorUtility.SetDirty(targetPanel);
        Debug.Log("Results panel refined!");
    }
    
    void StyleChildElements(Transform parent)
    {
        foreach (Transform child in parent)
        {
            string childName = child.name.ToLower();
            
            TextMeshProUGUI tmp = child.GetComponent<TextMeshProUGUI>();
            if (tmp != null)
            {
                StyleTextElement(tmp, childName);
            }
            
            Button btn = child.GetComponent<Button>();
            if (btn != null)
            {
                StyleButton(btn, childName);
            }
            
            LayoutElement le = child.GetComponent<LayoutElement>();
            if (le == null) le = child.gameObject.AddComponent<LayoutElement>();
            
            if (tmp != null)
            {
                le.preferredHeight = Mathf.Max(tmp.fontSize * 2, 40);
                le.preferredWidth = 280;
                le.minWidth = 250;
            }
            else if (btn != null)
            {
                le.preferredHeight = 45;
                le.preferredWidth = 150;
            }
            
            if (child.childCount > 0)
            {
                StyleChildElements(child);
            }
        }
    }
    
    void StyleTextElement(TextMeshProUGUI tmp, string elementName)
    {
        Undo.RecordObject(tmp, "Style Text");
        
        // Enable word wrapping and overflow handling
        tmp.enableWordWrapping = true;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        
        if (elementName.Contains("result") && !elementName.Contains("panel"))
        {
            // Main result text (WIN/LOSE)
            tmp.fontSize = 28;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = goldColor;
            tmp.alignment = TextAlignmentOptions.Center;
        }
        else if (elementName.Contains("outcome") || elementName.Contains("detail") || elementName.Contains("amount"))
        {
            // Outcome/details
            tmp.fontSize = 18;
            tmp.fontStyle = FontStyles.Normal;
            tmp.color = textColor;
            tmp.alignment = TextAlignmentOptions.Center;
        }
        else if (elementName.Contains("message") || elementName.Contains("info"))
        {
            tmp.fontSize = 14;
            tmp.fontStyle = FontStyles.Italic;
            tmp.color = labelColor;
            tmp.alignment = TextAlignmentOptions.Center;
        }
        else
        {
            tmp.fontSize = 16;
            tmp.color = textColor;
            tmp.alignment = TextAlignmentOptions.Center;
        }
        
        // Set RectTransform size
        RectTransform rect = tmp.GetComponent<RectTransform>();
        if (rect != null)
        {
            rect.sizeDelta = new Vector2(250, rect.sizeDelta.y);
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
            
            if (buttonName.Contains("close") || buttonName.Contains("ok") || buttonName.Contains("done"))
            {
                btnImg.color = new Color(0.5f, 0.2f, 0.2f, 0.95f); // Dark red
            }
            else if (buttonName.Contains("retry") || buttonName.Contains("again"))
            {
                btnImg.color = new Color(0.2f, 0.5f, 0.3f, 0.95f); // Green
            }
            else
            {
                btnImg.color = new Color(0.3f, 0.3f, 0.4f, 0.95f); // Gray
            }
            
            EditorUtility.SetDirty(btnImg);
        }
        
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
        GameObject panel = new GameObject("ResultsPanel");
        panel.transform.SetParent(canvas.transform, false);
        Undo.RegisterCreatedObjectUndo(panel, "Create Results Panel");
        
        RectTransform panelRect = panel.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = Vector2.zero;
        
        Image panelBg = panel.AddComponent<Image>();
        panelBg.color = bgColor;
        
        VerticalLayoutGroup vlg = panel.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(30, 30, 25, 25);
        vlg.spacing = 15;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        
        ContentSizeFitter csf = panel.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        
        // Result text (WIN/LOSE)
        CreateTextElement(panel.transform, "Result", "YOU WIN!", 28, FontStyles.Bold, goldColor, 260);
        
        CreateSeparator(panel.transform);
        
        // Outcome
        CreateTextElement(panel.transform, "Outcome", "+$5,000", 22, FontStyles.Bold, greenColor, 260);
        
        CreateSeparator(panel.transform);
        
        // Close button
        CreateStyledButton(panel.transform, "Close", "CLOSE", new Color(0.5f, 0.2f, 0.2f, 0.95f));
        
        Selection.activeGameObject = panel;
        targetPanel = panel;
        
        Debug.Log("Created new styled Results panel!");
    }
    
    void CreateTextElement(Transform parent, string name, string text, int fontSize, FontStyles style, Color color, float width)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        
        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(width, fontSize * 2);
        
        TextMeshProUGUI tmp = obj.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = true;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        
        LayoutElement le = obj.AddComponent<LayoutElement>();
        le.preferredHeight = fontSize * 2;
        le.preferredWidth = width;
        le.minWidth = width;
    }
    
    void CreateSeparator(Transform parent)
    {
        GameObject sep = new GameObject("Separator");
        sep.transform.SetParent(parent, false);
        
        RectTransform rect = sep.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(180, 2);
        
        Image img = sep.AddComponent<Image>();
        img.color = separatorColor;
        
        LayoutElement le = sep.AddComponent<LayoutElement>();
        le.preferredHeight = 2;
        le.flexibleWidth = 1;
    }
    
    void CreateStyledButton(Transform parent, string name, string text, Color btnBgColor)
    {
        GameObject btnObj = new GameObject(name);
        btnObj.transform.SetParent(parent, false);
        
        RectTransform rect = btnObj.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(150, 45);
        
        Image img = btnObj.AddComponent<Image>();
        img.color = btnBgColor;
        
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
        tmp.fontSize = 20;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        
        LayoutElement le = btnObj.AddComponent<LayoutElement>();
        le.preferredHeight = 45;
        le.preferredWidth = 150;
    }
}
#endif
