#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using TMPro;

public class MainMenuUIRefiner : EditorWindow
{
    // Style settings
    private Color backgroundColor = new Color(0.06f, 0.06f, 0.08f, 0.98f);
    private Color titleColor = new Color(0.95f, 0.8f, 0.25f);
    private Color subtitleColor = new Color(0.7f, 0.7f, 0.7f);
    private Color buttonBgColor = new Color(0.12f, 0.12f, 0.15f, 0.95f);
    private Color buttonTextColor = Color.white;
    private Color buttonHoverColor = new Color(0.18f, 0.18f, 0.22f);
    private Color accentColor = new Color(0.9f, 0.75f, 0.3f);
    private Color playButtonColor = new Color(0.2f, 0.5f, 0.25f);
    private Color quitButtonColor = new Color(0.5f, 0.2f, 0.2f);
    
    private int titleFontSize = 72;
    private int subtitleFontSize = 24;
    private int buttonFontSize = 22;
    private float buttonWidth = 280f;
    private float buttonHeight = 55f;
    private float buttonSpacing = 15f;
    
    private Vector2 scrollPos;
    
    [MenuItem("Tools/Game/Refine Main Menu UI")]
    public static void ShowWindow()
    {
        GetWindow<MainMenuUIRefiner>("Main Menu Refiner");
    }
    
    void OnGUI()
    {
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
        
        GUILayout.Label("Main Menu UI Refiner", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Refines and styles the Main Menu UI.\n" +
            "• Dark theme with gold accents\n" +
            "• Styled buttons with hover effects\n" +
            "• Professional game menu look",
            MessageType.Info);
        
        EditorGUILayout.Space(10);
        
        // Color settings
        GUILayout.Label("Colors", EditorStyles.boldLabel);
        backgroundColor = EditorGUILayout.ColorField("Background", backgroundColor);
        titleColor = EditorGUILayout.ColorField("Title Color", titleColor);
        subtitleColor = EditorGUILayout.ColorField("Subtitle Color", subtitleColor);
        accentColor = EditorGUILayout.ColorField("Accent Color", accentColor);
        buttonBgColor = EditorGUILayout.ColorField("Button Background", buttonBgColor);
        buttonTextColor = EditorGUILayout.ColorField("Button Text", buttonTextColor);
        playButtonColor = EditorGUILayout.ColorField("Play Button", playButtonColor);
        quitButtonColor = EditorGUILayout.ColorField("Quit Button", quitButtonColor);
        
        EditorGUILayout.Space(10);
        
        // Size settings
        GUILayout.Label("Sizes", EditorStyles.boldLabel);
        titleFontSize = EditorGUILayout.IntSlider("Title Font Size", titleFontSize, 36, 120);
        subtitleFontSize = EditorGUILayout.IntSlider("Subtitle Font Size", subtitleFontSize, 14, 48);
        buttonFontSize = EditorGUILayout.IntSlider("Button Font Size", buttonFontSize, 14, 36);
        buttonWidth = EditorGUILayout.Slider("Button Width", buttonWidth, 150f, 400f);
        buttonHeight = EditorGUILayout.Slider("Button Height", buttonHeight, 35f, 80f);
        buttonSpacing = EditorGUILayout.Slider("Button Spacing", buttonSpacing, 5f, 30f);
        
        EditorGUILayout.Space(20);
        
        // Action buttons
        GUILayout.Label("Actions", EditorStyles.boldLabel);
        
        if (GUILayout.Button("Find & Refine Main Menu", GUILayout.Height(35)))
        {
            FindAndRefineMainMenu();
        }
        
        if (GUILayout.Button("Refine Selected Panel", GUILayout.Height(30)))
        {
            RefineSelectedPanel();
        }
        
        EditorGUILayout.Space(10);
        
        if (GUILayout.Button("Create New Styled Main Menu", GUILayout.Height(35)))
        {
            CreateNewMainMenu();
        }
        
        EditorGUILayout.Space(10);
        
        GUILayout.Label("Individual Styling", EditorStyles.boldLabel);
        
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Style All Buttons"))
        {
            StyleAllButtonsInSelection();
        }
        if (GUILayout.Button("Style All Text"))
        {
            StyleAllTextInSelection();
        }
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.EndScrollView();
    }
    
    void FindAndRefineMainMenu()
    {
        // Try to find main menu by common names
        string[] possibleNames = { "MainMenu", "Main Menu", "MenuPanel", "Menu Panel", "MainMenuPanel", "StartMenu", "TitleScreen" };
        
        GameObject menuPanel = null;
        
        foreach (string name in possibleNames)
        {
            menuPanel = GameObject.Find(name);
            if (menuPanel != null) break;
        }
        
        if (menuPanel == null)
        {
            // Try finding in canvas children
            Canvas canvas = FindObjectOfType<Canvas>();
            if (canvas != null)
            {
                foreach (Transform child in canvas.transform)
                {
                    string childName = child.name.ToLower();
                    if (childName.Contains("menu") || childName.Contains("main") || childName.Contains("title"))
                    {
                        menuPanel = child.gameObject;
                        break;
                    }
                }
            }
        }
        
        if (menuPanel == null)
        {
            EditorUtility.DisplayDialog("Not Found", 
                "Could not find Main Menu panel.\n\n" +
                "Try one of these:\n" +
                "1. Select your menu panel and click 'Refine Selected Panel'\n" +
                "2. Click 'Create New Styled Main Menu'", "OK");
            return;
        }
        
        RefinePanel(menuPanel);
        Selection.activeGameObject = menuPanel;
        EditorUtility.DisplayDialog("Success", $"Refined '{menuPanel.name}' main menu!", "OK");
    }
    
    void RefineSelectedPanel()
    {
        GameObject selected = Selection.activeGameObject;
        if (selected == null)
        {
            EditorUtility.DisplayDialog("No Selection", "Please select a panel to refine.", "OK");
            return;
        }
        
        RefinePanel(selected);
        EditorUtility.DisplayDialog("Success", $"Refined '{selected.name}'!", "OK");
    }
    
    void RefinePanel(GameObject panel)
    {
        Undo.RegisterCompleteObjectUndo(panel, "Refine Main Menu");
        
        // Style background
        Image bg = panel.GetComponent<Image>();
        if (bg != null)
        {
            bg.color = backgroundColor;
        }
        
        // Add outline if not present
        Outline outline = panel.GetComponent<Outline>();
        if (outline == null)
        {
            outline = Undo.AddComponent<Outline>(panel);
        }
        outline.effectColor = new Color(accentColor.r, accentColor.g, accentColor.b, 0.4f);
        outline.effectDistance = new Vector2(3, 3);
        
        // Style all child elements
        StyleChildElements(panel.transform);
        
        EditorUtility.SetDirty(panel);
    }
    
    void StyleChildElements(Transform parent)
    {
        foreach (Transform child in parent)
        {
            string childName = child.name.ToLower();
            
            // Style buttons
            Button btn = child.GetComponent<Button>();
            if (btn != null)
            {
                StyleButton(btn, childName);
            }
            
            // Style text (not in buttons)
            if (btn == null)
            {
                TextMeshProUGUI tmp = child.GetComponent<TextMeshProUGUI>();
                if (tmp != null)
                {
                    StyleText(tmp, childName);
                }
            }
            
            // Recurse into children
            if (child.childCount > 0)
            {
                StyleChildElements(child);
            }
        }
    }
    
    void StyleButton(Button btn, string name)
    {
        Undo.RecordObject(btn.gameObject, "Style Button");
        
        // Determine button color based on name
        Color bgColor = buttonBgColor;
        if (name.Contains("play") || name.Contains("start") || name.Contains("new") || name.Contains("continue"))
        {
            bgColor = playButtonColor;
        }
        else if (name.Contains("quit") || name.Contains("exit"))
        {
            bgColor = quitButtonColor;
        }
        else if (name.Contains("settings") || name.Contains("options"))
        {
            bgColor = new Color(0.3f, 0.35f, 0.5f);
        }
        else if (name.Contains("load"))
        {
            bgColor = new Color(0.3f, 0.45f, 0.5f);
        }
        else if (name.Contains("credits"))
        {
            bgColor = new Color(0.4f, 0.35f, 0.3f);
        }
        
        // Style background
        Image bg = btn.GetComponent<Image>();
        if (bg != null)
        {
            bg.color = bgColor;
            Undo.RecordObject(bg, "Style Button Image");
        }
        
        // Set size
        RectTransform rect = btn.GetComponent<RectTransform>();
        if (rect != null)
        {
            // Only set size if using layout, otherwise keep manual positioning
            LayoutElement le = btn.GetComponent<LayoutElement>();
            if (le == null)
            {
                le = Undo.AddComponent<LayoutElement>(btn.gameObject);
            }
            le.preferredWidth = buttonWidth;
            le.preferredHeight = buttonHeight;
        }
        
        // Add outline
        Outline outline = btn.GetComponent<Outline>();
        if (outline == null)
        {
            outline = Undo.AddComponent<Outline>(btn.gameObject);
        }
        outline.effectColor = new Color(accentColor.r, accentColor.g, accentColor.b, 0.5f);
        outline.effectDistance = new Vector2(2, 2);
        
        // Button colors
        ColorBlock colors = btn.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
        colors.pressedColor = new Color(0.85f, 0.85f, 0.85f);
        colors.selectedColor = colors.highlightedColor;
        btn.colors = colors;
        
        // Style button text
        TextMeshProUGUI btnText = btn.GetComponentInChildren<TextMeshProUGUI>();
        if (btnText != null)
        {
            Undo.RecordObject(btnText, "Style Button Text");
            btnText.color = buttonTextColor;
            btnText.fontSize = buttonFontSize;
            btnText.fontStyle = FontStyles.Bold;
            btnText.alignment = TextAlignmentOptions.Center;
        }
        
        EditorUtility.SetDirty(btn.gameObject);
    }
    
    void StyleText(TextMeshProUGUI tmp, string name)
    {
        Undo.RecordObject(tmp, "Style Text");
        
        // Title styling
        if (name.Contains("title") || name.Contains("game") || name.Contains("logo"))
        {
            tmp.color = titleColor;
            tmp.fontSize = titleFontSize;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            
            // Add shadow effect
            Shadow shadow = tmp.GetComponent<Shadow>();
            if (shadow == null)
            {
                shadow = Undo.AddComponent<Shadow>(tmp.gameObject);
            }
            shadow.effectColor = new Color(0, 0, 0, 0.5f);
            shadow.effectDistance = new Vector2(3, -3);
        }
        // Subtitle styling
        else if (name.Contains("subtitle") || name.Contains("version") || name.Contains("sub"))
        {
            tmp.color = subtitleColor;
            tmp.fontSize = subtitleFontSize;
            tmp.fontStyle = FontStyles.Italic;
            tmp.alignment = TextAlignmentOptions.Center;
        }
        // Default text styling
        else
        {
            tmp.color = Color.white;
            tmp.fontSize = 18;
        }
        
        EditorUtility.SetDirty(tmp.gameObject);
    }
    
    void StyleAllButtonsInSelection()
    {
        GameObject selected = Selection.activeGameObject;
        if (selected == null)
        {
            EditorUtility.DisplayDialog("No Selection", "Please select a panel first.", "OK");
            return;
        }
        
        Button[] buttons = selected.GetComponentsInChildren<Button>(true);
        foreach (Button btn in buttons)
        {
            StyleButton(btn, btn.name.ToLower());
        }
        
        EditorUtility.DisplayDialog("Done", $"Styled {buttons.Length} buttons.", "OK");
    }
    
    void StyleAllTextInSelection()
    {
        GameObject selected = Selection.activeGameObject;
        if (selected == null)
        {
            EditorUtility.DisplayDialog("No Selection", "Please select a panel first.", "OK");
            return;
        }
        
        TextMeshProUGUI[] texts = selected.GetComponentsInChildren<TextMeshProUGUI>(true);
        int styled = 0;
        foreach (TextMeshProUGUI tmp in texts)
        {
            // Skip button text
            if (tmp.GetComponentInParent<Button>() != null) continue;
            StyleText(tmp, tmp.name.ToLower());
            styled++;
        }
        
        EditorUtility.DisplayDialog("Done", $"Styled {styled} text elements.", "OK");
    }
    
    void CreateNewMainMenu()
    {
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            EditorUtility.DisplayDialog("No Canvas", "Please create a Canvas first.", "OK");
            return;
        }
        
        // Check if already exists
        Transform existing = canvas.transform.Find("MainMenu");
        if (existing != null)
        {
            if (!EditorUtility.DisplayDialog("Already Exists", 
                "MainMenu already exists. Replace it?", "Yes", "No"))
            {
                return;
            }
            Undo.DestroyObjectImmediate(existing.gameObject);
        }
        
        // Create main panel
        GameObject mainMenu = new GameObject("MainMenu");
        Undo.RegisterCreatedObjectUndo(mainMenu, "Create Main Menu");
        mainMenu.transform.SetParent(canvas.transform, false);
        
        RectTransform rect = mainMenu.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.sizeDelta = Vector2.zero;
        
        Image bg = mainMenu.AddComponent<Image>();
        bg.color = backgroundColor;
        
        // Create content container
        GameObject content = new GameObject("Content");
        content.transform.SetParent(mainMenu.transform, false);
        
        RectTransform contentRect = content.AddComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0.5f, 0.5f);
        contentRect.anchorMax = new Vector2(0.5f, 0.5f);
        contentRect.sizeDelta = new Vector2(400, 500);
        
        VerticalLayoutGroup vlg = content.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.spacing = 10;
        vlg.padding = new RectOffset(20, 20, 30, 30);
        
        ContentSizeFitter csf = content.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        
        // Title
        GameObject titleObj = CreateTextElement(content.transform, "Title", "YOUR GAME", titleFontSize, titleColor, 100);
        TextMeshProUGUI titleTmp = titleObj.GetComponent<TextMeshProUGUI>();
        titleTmp.fontStyle = FontStyles.Bold;
        Shadow shadow = titleObj.AddComponent<Shadow>();
        shadow.effectColor = new Color(0, 0, 0, 0.6f);
        shadow.effectDistance = new Vector2(4, -4);
        
        // Subtitle
        CreateTextElement(content.transform, "Subtitle", "A Game About Business", subtitleFontSize, subtitleColor, 35);
        
        // Spacer
        CreateSpacer(content.transform, 40);
        
        // Buttons container
        GameObject buttonsContainer = new GameObject("Buttons");
        buttonsContainer.transform.SetParent(content.transform, false);
        
        RectTransform btnContRect = buttonsContainer.AddComponent<RectTransform>();
        
        VerticalLayoutGroup btnVlg = buttonsContainer.AddComponent<VerticalLayoutGroup>();
        btnVlg.childAlignment = TextAnchor.UpperCenter;
        btnVlg.childControlWidth = false;
        btnVlg.childControlHeight = false;
        btnVlg.childForceExpandWidth = false;
        btnVlg.childForceExpandHeight = false;
        btnVlg.spacing = buttonSpacing;
        
        LayoutElement btnContLE = buttonsContainer.AddComponent<LayoutElement>();
        btnContLE.preferredHeight = 300;
        
        // Create buttons
        CreateMenuButton(buttonsContainer.transform, "PlayButton", "PLAY", playButtonColor);
        CreateMenuButton(buttonsContainer.transform, "ContinueButton", "CONTINUE", new Color(0.25f, 0.45f, 0.35f));
        CreateMenuButton(buttonsContainer.transform, "SettingsButton", "SETTINGS", new Color(0.3f, 0.35f, 0.5f));
        CreateMenuButton(buttonsContainer.transform, "CreditsButton", "CREDITS", new Color(0.4f, 0.35f, 0.3f));
        CreateMenuButton(buttonsContainer.transform, "QuitButton", "QUIT", quitButtonColor);
        
        // Version text at bottom
        CreateSpacer(content.transform, 20);
        GameObject versionObj = CreateTextElement(content.transform, "Version", "v1.0.0", 14, new Color(0.5f, 0.5f, 0.5f), 25);
        
        Selection.activeGameObject = mainMenu;
        
        EditorUtility.DisplayDialog("Created", 
            "Main Menu created!\n\n" +
            "• Edit the Title text to your game name\n" +
            "• Wire up button OnClick events\n" +
            "• Adjust colors in the refiner tool",
            "OK");
    }
    
    GameObject CreateTextElement(Transform parent, string name, string text, int fontSize, Color color, float height)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        
        RectTransform rect = obj.AddComponent<RectTransform>();
        
        LayoutElement le = obj.AddComponent<LayoutElement>();
        le.preferredHeight = height;
        le.flexibleWidth = 1;
        
        TextMeshProUGUI tmp = obj.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        
        return obj;
    }
    
    void CreateSpacer(Transform parent, float height)
    {
        GameObject spacer = new GameObject("Spacer");
        spacer.transform.SetParent(parent, false);
        
        RectTransform rect = spacer.AddComponent<RectTransform>();
        
        LayoutElement le = spacer.AddComponent<LayoutElement>();
        le.preferredHeight = height;
    }
    
    void CreateMenuButton(Transform parent, string name, string text, Color bgColor)
    {
        GameObject btnObj = new GameObject(name);
        btnObj.transform.SetParent(parent, false);
        
        RectTransform rect = btnObj.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(buttonWidth, buttonHeight);
        
        Image bg = btnObj.AddComponent<Image>();
        bg.color = bgColor;
        
        // Outline
        Outline outline = btnObj.AddComponent<Outline>();
        outline.effectColor = new Color(accentColor.r, accentColor.g, accentColor.b, 0.5f);
        outline.effectDistance = new Vector2(2, 2);
        
        Button btn = btnObj.AddComponent<Button>();
        btn.targetGraphic = bg;
        
        ColorBlock colors = btn.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.2f, 1.2f, 1.2f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
        btn.colors = colors;
        
        // Button text
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(btnObj.transform, false);
        
        RectTransform textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = Vector2.zero;
        
        TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = buttonFontSize;
        tmp.color = buttonTextColor;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontStyle = FontStyles.Bold;
    }
}
#endif
