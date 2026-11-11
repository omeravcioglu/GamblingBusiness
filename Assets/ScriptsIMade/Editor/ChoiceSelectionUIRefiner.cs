#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Editor tool to refine the ChoiceSelection panel to match Company Stats UI style
/// </summary>
public class ChoiceSelectionUIRefiner : EditorWindow
{
    private GameObject targetPanel;
    
    // Style settings matching Company Stats UI
    private readonly Color bgColor = new Color(0, 0, 0, 0.7f);
    private readonly Color textColor = Color.white;
    private readonly Color labelColor = new Color(0.7f, 0.7f, 0.7f);
    private readonly Color accentColor = new Color(1f, 0.8f, 0.2f); // Gold
    private readonly Color greenColor = new Color(0.2f, 0.8f, 0.2f);
    private readonly Color redColor = new Color(0.9f, 0.3f, 0.3f);
    private readonly Color separatorColor = new Color(1, 1, 1, 0.2f);
    
    [MenuItem("Tools/Game/Refine ChoiceSelection UI")]
    public static void ShowWindow()
    {
        GetWindow<ChoiceSelectionUIRefiner>("ChoiceSelection Refiner");
    }
    
    void OnGUI()
    {
        GUILayout.Label("ChoiceSelection Panel Refiner", EditorStyles.boldLabel);
        GUILayout.Space(10);
        
        EditorGUILayout.HelpBox(
            "This tool refines the ChoiceSelection panel to match the Company Stats UI style:\n\n" +
            "• Dark semi-transparent background\n" +
            "• Vertical layout with proper spacing\n" +
            "• Consistent text colors and sizes\n" +
            "• Section separators\n" +
            "• Auto-sizing panel",
            MessageType.Info);
        
        GUILayout.Space(10);
        
        targetPanel = (GameObject)EditorGUILayout.ObjectField("Target Panel", targetPanel, typeof(GameObject), true);
        
        GUILayout.Space(5);
        
        if (targetPanel == null)
        {
            if (GUILayout.Button("Find ChoiceSelection Panel"))
            {
                FindChoiceSelectionPanel();
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
    
    void FindChoiceSelectionPanel()
    {
        // Try to find by name
        GameObject found = GameObject.Find("ChoiceSelection");
        if (found == null) found = GameObject.Find("ChoiceSelectionPanel");
        if (found == null) found = GameObject.Find("UpgradePanel");
        
        if (found != null)
        {
            targetPanel = found;
            Debug.Log($"Found panel: {found.name}");
        }
        else
        {
            // Try to find in selection
            if (Selection.activeGameObject != null)
            {
                targetPanel = Selection.activeGameObject;
                Debug.Log($"Using selected object: {targetPanel.name}");
            }
            else
            {
                Debug.LogWarning("Could not find ChoiceSelection panel. Please select it manually.");
            }
        }
    }
    
    void RefinePanel()
    {
        if (targetPanel == null) return;
        
        Undo.RegisterCompleteObjectUndo(targetPanel, "Refine ChoiceSelection Panel");
        
        // Get or add Image component for background
        Image bg = targetPanel.GetComponent<Image>();
        if (bg == null) bg = targetPanel.AddComponent<Image>();
        bg.color = bgColor;
        
        // Get or add VerticalLayoutGroup
        VerticalLayoutGroup vlg = targetPanel.GetComponent<VerticalLayoutGroup>();
        if (vlg == null) vlg = targetPanel.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(15, 15, 15, 15);
        vlg.spacing = 8;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        
        // Get or add ContentSizeFitter
        ContentSizeFitter csf = targetPanel.GetComponent<ContentSizeFitter>();
        if (csf == null) csf = targetPanel.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        
        // Style all child text elements
        StyleChildElements(targetPanel.transform);
        
        EditorUtility.SetDirty(targetPanel);
        Debug.Log("Panel refined successfully!");
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
            
            // Add layout element for proper sizing
            LayoutElement le = child.GetComponent<LayoutElement>();
            if (le == null) le = child.gameObject.AddComponent<LayoutElement>();
            
            // Set min heights based on type
            if (tmp != null)
            {
                le.preferredHeight = tmp.fontSize + 10;
            }
            else if (btn != null)
            {
                le.preferredHeight = 35;
                le.minWidth = 120;
            }
            
            // Recurse into children
            if (child.childCount > 0)
            {
                StyleChildElements(child);
            }
        }
    }
    
    void StyleTextElement(TextMeshProUGUI tmp, string elementName)
    {
        Undo.RecordObject(tmp, "Style Text Element");
        
        // Determine styling based on name
        if (elementName.Contains("name") || elementName.Contains("title") || elementName.Contains("item"))
        {
            // Title text - larger, bold, gold
            tmp.fontSize = 24;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = accentColor;
            tmp.alignment = TextAlignmentOptions.Center;
        }
        else if (elementName.Contains("level") || elementName.Contains("current"))
        {
            // Level text - medium, white
            tmp.fontSize = 18;
            tmp.fontStyle = FontStyles.Normal;
            tmp.color = textColor;
            tmp.alignment = TextAlignmentOptions.Center;
        }
        else if (elementName.Contains("cost") || elementName.Contains("price"))
        {
            // Cost text - medium, green/gold
            tmp.fontSize = 20;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = greenColor;
            tmp.alignment = TextAlignmentOptions.Center;
        }
        else if (elementName.Contains("info") || elementName.Contains("desc"))
        {
            // Info text - smaller, gray, italic
            tmp.fontSize = 14;
            tmp.fontStyle = FontStyles.Italic;
            tmp.color = labelColor;
            tmp.alignment = TextAlignmentOptions.Center;
        }
        else
        {
            // Default text style
            tmp.fontSize = 16;
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
            
            if (buttonName.Contains("cancel") || buttonName.Contains("close") || buttonName.Contains("no"))
            {
                btnImg.color = new Color(0.6f, 0.2f, 0.2f, 0.9f); // Red-ish
            }
            else if (buttonName.Contains("blackjack") || buttonName.Contains("gamble"))
            {
                btnImg.color = new Color(0.8f, 0.6f, 0.1f, 0.9f); // Gold
            }
            else if (buttonName.Contains("random") || buttonName.Contains("chance"))
            {
                btnImg.color = new Color(0.4f, 0.3f, 0.6f, 0.9f); // Purple
            }
            else
            {
                btnImg.color = new Color(0.2f, 0.5f, 0.3f, 0.9f); // Green
            }
            
            EditorUtility.SetDirty(btnImg);
        }
        
        // Style button text
        TextMeshProUGUI btnText = btn.GetComponentInChildren<TextMeshProUGUI>();
        if (btnText != null)
        {
            Undo.RecordObject(btnText, "Style Button Text");
            btnText.fontSize = 18;
            btnText.fontStyle = FontStyles.Bold;
            btnText.color = Color.white;
            btnText.alignment = TextAlignmentOptions.Center;
            EditorUtility.SetDirty(btnText);
        }
        
        // Set button colors
        ColorBlock colors = btn.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.2f, 1.2f, 1.2f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
        colors.selectedColor = Color.white;
        btn.colors = colors;
        
        EditorUtility.SetDirty(btn);
    }
    
    void CreateNewStyledPanel()
    {
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            EditorUtility.DisplayDialog("Error", "No Canvas found in scene!", "OK");
            return;
        }
        
        // Create main panel
        GameObject panel = new GameObject("ChoiceSelectionPanel");
        panel.transform.SetParent(canvas.transform, false);
        Undo.RegisterCreatedObjectUndo(panel, "Create ChoiceSelection Panel");
        
        RectTransform panelRect = panel.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = Vector2.zero;
        
        Image panelBg = panel.AddComponent<Image>();
        panelBg.color = bgColor;
        
        VerticalLayoutGroup vlg = panel.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(20, 20, 20, 20);
        vlg.spacing = 10;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        
        ContentSizeFitter csf = panel.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        
        // Create header
        CreateHeader(panel.transform, "UPGRADE", accentColor);
        
        // Item Name
        CreateTextElement(panel.transform, "ItemName", "Item Name", 24, FontStyles.Bold, accentColor, 180);
        
        // Separator
        CreateSeparator(panel.transform);
        
        // Current Level
        CreateStatRow(panel.transform, "LevelRow", "Level", "1", textColor);
        
        // Upgrade Cost
        CreateStatRow(panel.transform, "CostRow", "Cost", "$1,000", greenColor);
        
        // Separator
        CreateSeparator(panel.transform);
        
        // Info
        CreateTextElement(panel.transform, "UpgradeInfo", "Upgrade description goes here", 14, FontStyles.Italic, labelColor, 180);
        
        // Separator
        CreateSeparator(panel.transform);
        
        // Header for actions
        CreateHeader(panel.transform, "ACTIONS", labelColor);
        
        // Buttons container
        GameObject buttonsContainer = new GameObject("Buttons");
        buttonsContainer.transform.SetParent(panel.transform, false);
        RectTransform btnContainerRect = buttonsContainer.AddComponent<RectTransform>();
        VerticalLayoutGroup btnVLG = buttonsContainer.AddComponent<VerticalLayoutGroup>();
        btnVLG.spacing = 8;
        btnVLG.childAlignment = TextAnchor.UpperCenter;
        btnVLG.childControlWidth = true;
        btnVLG.childControlHeight = true;
        btnVLG.childForceExpandWidth = true;
        btnVLG.childForceExpandHeight = false;
        
        // Blackjack button
        CreateStyledButton(buttonsContainer.transform, "Blackjack", "BLACKJACK", new Color(0.8f, 0.6f, 0.1f, 0.9f));
        
        // Random Chance button
        CreateStyledButton(buttonsContainer.transform, "RandomChance", "RANDOM CHANCE", new Color(0.4f, 0.3f, 0.6f, 0.9f));
        
        // Cancel button
        CreateStyledButton(buttonsContainer.transform, "Cancel", "CANCEL", new Color(0.6f, 0.2f, 0.2f, 0.9f));
        
        Selection.activeGameObject = panel;
        targetPanel = panel;
        
        Debug.Log("Created new styled ChoiceSelection panel!");
    }
    
    void CreateHeader(Transform parent, string text, Color color)
    {
        GameObject header = new GameObject(text.Replace(" ", "") + "Header");
        header.transform.SetParent(parent, false);
        
        RectTransform headerRect = header.AddComponent<RectTransform>();
        headerRect.sizeDelta = new Vector2(200, 20);
        
        TextMeshProUGUI headerTMP = header.AddComponent<TextMeshProUGUI>();
        headerTMP.text = text;
        headerTMP.fontSize = 12;
        headerTMP.color = color;
        headerTMP.alignment = TextAlignmentOptions.Center;
        headerTMP.fontStyle = FontStyles.Bold;
        
        LayoutElement le = header.AddComponent<LayoutElement>();
        le.preferredHeight = 20;
    }
    
    void CreateTextElement(Transform parent, string name, string text, int fontSize, FontStyles style, Color color, float width)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        
        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(width, fontSize + 10);
        
        TextMeshProUGUI tmp = obj.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = true;
        
        LayoutElement le = obj.AddComponent<LayoutElement>();
        le.preferredWidth = width;
        le.preferredHeight = fontSize + 10;
    }
    
    void CreateStatRow(Transform parent, string name, string label, string value, Color valueColor)
    {
        GameObject row = new GameObject(name);
        row.transform.SetParent(parent, false);
        
        RectTransform rowRect = row.AddComponent<RectTransform>();
        HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 10;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = false;
        hlg.childControlHeight = true;
        
        // Label
        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(row.transform, false);
        RectTransform labelRect = labelObj.AddComponent<RectTransform>();
        labelRect.sizeDelta = new Vector2(80, 25);
        TextMeshProUGUI labelTMP = labelObj.AddComponent<TextMeshProUGUI>();
        labelTMP.text = label;
        labelTMP.fontSize = 14;
        labelTMP.color = labelColor;
        labelTMP.alignment = TextAlignmentOptions.Right;
        
        // Value
        GameObject valueObj = new GameObject("Value");
        valueObj.transform.SetParent(row.transform, false);
        RectTransform valueRect = valueObj.AddComponent<RectTransform>();
        valueRect.sizeDelta = new Vector2(80, 25);
        TextMeshProUGUI valueTMP = valueObj.AddComponent<TextMeshProUGUI>();
        valueTMP.text = value;
        valueTMP.fontSize = 18;
        valueTMP.fontStyle = FontStyles.Bold;
        valueTMP.color = valueColor;
        valueTMP.alignment = TextAlignmentOptions.Left;
        
        LayoutElement le = row.AddComponent<LayoutElement>();
        le.preferredHeight = 25;
    }
    
    void CreateSeparator(Transform parent)
    {
        GameObject sep = new GameObject("Separator");
        sep.transform.SetParent(parent, false);
        
        RectTransform sepRect = sep.AddComponent<RectTransform>();
        sepRect.sizeDelta = new Vector2(160, 2);
        
        Image sepImage = sep.AddComponent<Image>();
        sepImage.color = separatorColor;
        
        LayoutElement le = sep.AddComponent<LayoutElement>();
        le.preferredHeight = 2;
        le.flexibleWidth = 1;
    }
    
    void CreateStyledButton(Transform parent, string name, string text, Color bgColor)
    {
        GameObject btnObj = new GameObject(name);
        btnObj.transform.SetParent(parent, false);
        
        RectTransform btnRect = btnObj.AddComponent<RectTransform>();
        btnRect.sizeDelta = new Vector2(180, 40);
        
        Image btnImg = btnObj.AddComponent<Image>();
        btnImg.color = bgColor;
        
        Button btn = btnObj.AddComponent<Button>();
        ColorBlock colors = btn.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.2f, 1.2f, 1.2f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
        btn.colors = colors;
        
        // Button text
        GameObject textObj = new GameObject("Text (TMP)");
        textObj.transform.SetParent(btnObj.transform, false);
        
        RectTransform textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = Vector2.zero;
        
        TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = 16;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        
        LayoutElement le = btnObj.AddComponent<LayoutElement>();
        le.preferredHeight = 40;
        le.preferredWidth = 180;
    }
}
#endif
