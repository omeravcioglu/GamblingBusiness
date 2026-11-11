#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Editor tool to refine the SkillTree panel to match Company Stats UI style
/// </summary>
public class SkillTreeUIRefiner : EditorWindow
{
    private GameObject targetPanel;
    
    // Style settings
    private readonly Color bgColor = new Color(0.05f, 0.05f, 0.08f, 0.95f);
    private readonly Color headerBgColor = new Color(0.1f, 0.1f, 0.15f, 1f);
    private readonly Color detailPanelBg = new Color(0, 0, 0, 0.85f);
    private readonly Color textColor = Color.white;
    private readonly Color labelColor = new Color(0.7f, 0.7f, 0.7f);
    private readonly Color goldColor = new Color(1f, 0.8f, 0.2f);
    private readonly Color greenColor = new Color(0.2f, 0.8f, 0.2f);
    private readonly Color blueColor = new Color(0.3f, 0.6f, 0.9f);
    private readonly Color purpleColor = new Color(0.6f, 0.3f, 0.8f);
    private readonly Color redColor = new Color(0.8f, 0.3f, 0.3f);
    private readonly Color chaosColor = new Color(0.9f, 0.2f, 0.4f);
    private readonly Color separatorColor = new Color(1, 1, 1, 0.2f);
    
    [MenuItem("Tools/Game/Refine SkillTree UI")]
    public static void ShowWindow()
    {
        GetWindow<SkillTreeUIRefiner>("SkillTree Refiner");
    }
    
    void OnGUI()
    {
        GUILayout.Label("SkillTree Panel Refiner", EditorStyles.boldLabel);
        GUILayout.Space(10);
        
        EditorGUILayout.HelpBox(
            "Refines the SkillTree panel to match Company Stats UI style:\n\n" +
            "• Dark themed background\n" +
            "• Styled header with skill points\n" +
            "• Category buttons (Growth, Productivity, Chaos)\n" +
            "• Skill detail panel styling\n" +
            "• Consistent colors throughout",
            MessageType.Info);
        
        GUILayout.Space(10);
        
        targetPanel = (GameObject)EditorGUILayout.ObjectField("Target Panel", targetPanel, typeof(GameObject), true);
        
        GUILayout.Space(5);
        
        if (targetPanel == null)
        {
            if (GUILayout.Button("Find SkillTree Panel"))
            {
                FindSkillTreePanel();
            }
        }
        
        GUILayout.Space(15);
        
        GUI.enabled = targetPanel != null;
        
        if (GUILayout.Button("REFINE PANEL STYLE", GUILayout.Height(40)))
        {
            RefinePanel();
        }
        
        GUILayout.Space(10);
        
        GUILayout.Label("Individual Sections", EditorStyles.boldLabel);
        
        if (GUILayout.Button("Style Header Only"))
        {
            StyleHeaderSection();
        }
        
        if (GUILayout.Button("Style Category Buttons Only"))
        {
            StyleCategoryButtons();
        }
        
        if (GUILayout.Button("Style Detail Panel Only"))
        {
            StyleDetailPanel();
        }
        
        if (GUILayout.Button("Style Skill Grid Boxes"))
        {
            StyleSkillGridBoxes();
        }
        
        if (GUILayout.Button("Fix Detail Panel Position"))
        {
            FixDetailPanelPosition();
        }
        
        GUI.enabled = true;
    }
    
    void FindSkillTreePanel()
    {
        GameObject found = GameObject.Find("SkillTreePanel");
        if (found == null) found = GameObject.Find("SkillTree");
        if (found == null) found = GameObject.Find("SkillPanel");
        
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
            Debug.LogWarning("Could not find SkillTree panel. Please select it manually.");
        }
    }
    
    void RefinePanel()
    {
        if (targetPanel == null) return;
        
        Undo.RegisterCompleteObjectUndo(targetPanel, "Refine SkillTree Panel");
        
        // Main panel background
        Image bg = targetPanel.GetComponent<Image>();
        if (bg == null) bg = targetPanel.AddComponent<Image>();
        bg.color = bgColor;
        
        // Style all sections
        StyleHeaderSection();
        StyleCategoryButtons();
        StyleDetailPanel();
        StyleScrollView();
        StyleSkillGridBoxes();
        FixDetailPanelPosition();
        
        EditorUtility.SetDirty(targetPanel);
        Debug.Log("SkillTree panel refined!");
    }
    
    void StyleHeaderSection()
    {
        if (targetPanel == null) return;
        
        Transform header = targetPanel.transform.Find("Header");
        if (header == null) return;
        
        Undo.RecordObject(header.gameObject, "Style Header");
        
        // Header background
        Image headerBg = header.GetComponent<Image>();
        if (headerBg == null) headerBg = header.gameObject.AddComponent<Image>();
        headerBg.color = headerBgColor;
        
        // Header layout
        HorizontalLayoutGroup hlg = header.GetComponent<HorizontalLayoutGroup>();
        if (hlg == null) hlg = header.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(15, 15, 10, 10);
        hlg.spacing = 15;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = false;
        hlg.childControlHeight = true;
        
        // Style header text
        Transform headerText = header.Find("header");
        if (headerText != null)
        {
            TextMeshProUGUI tmp = headerText.GetComponent<TextMeshProUGUI>();
            if (tmp != null)
            {
                tmp.fontSize = 28;
                tmp.fontStyle = FontStyles.Bold;
                tmp.color = goldColor;
                EditorUtility.SetDirty(tmp);
            }
        }
        
        // Style close button
        Transform closeBtn = header.Find("Closebtn");
        if (closeBtn != null)
        {
            StyleButton(closeBtn.GetComponent<Button>(), "close");
        }
        
        // Style skill points display
        Transform skillPoints = targetPanel.transform.Find("SkillPoints");
        if (skillPoints != null)
        {
            TextMeshProUGUI spTmp = skillPoints.GetComponent<TextMeshProUGUI>();
            if (spTmp != null)
            {
                spTmp.fontSize = 22;
                spTmp.fontStyle = FontStyles.Bold;
                spTmp.color = goldColor;
                spTmp.alignment = TextAlignmentOptions.Center;
                EditorUtility.SetDirty(spTmp);
            }
            
            // Add background
            Image spBg = skillPoints.GetComponent<Image>();
            if (spBg == null) spBg = skillPoints.gameObject.AddComponent<Image>();
            spBg.color = new Color(0.15f, 0.12f, 0.05f, 0.9f);
            
            // Layout element
            LayoutElement le = skillPoints.GetComponent<LayoutElement>();
            if (le == null) le = skillPoints.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 40;
            le.preferredWidth = 200;
        }
        
        EditorUtility.SetDirty(header.gameObject);
        Debug.Log("Header styled!");
    }
    
    void StyleCategoryButtons()
    {
        if (targetPanel == null) return;
        
        // Growth button
        Transform growth = targetPanel.transform.Find("Growth");
        if (growth != null)
        {
            StyleCategoryButton(growth, greenColor, "GROWTH");
        }
        
        // Productivity button
        Transform productivity = targetPanel.transform.Find("Productivity");
        if (productivity != null)
        {
            StyleCategoryButton(productivity, blueColor, "PRODUCTIVITY");
        }
        
        // Chaos button
        Transform chaos = targetPanel.transform.Find("Chaos Button");
        if (chaos == null) chaos = targetPanel.transform.Find("ChaosButton");
        if (chaos == null) chaos = targetPanel.transform.Find("Chaos");
        if (chaos != null)
        {
            StyleCategoryButton(chaos, chaosColor, "CHAOS");
        }
        
        Debug.Log("Category buttons styled!");
    }
    
    void StyleCategoryButton(Transform btnTransform, Color color, string label)
    {
        Undo.RecordObject(btnTransform.gameObject, "Style Category Button");
        
        // Background
        Image bg = btnTransform.GetComponent<Image>();
        if (bg == null) bg = btnTransform.gameObject.AddComponent<Image>();
        bg.color = new Color(color.r * 0.3f, color.g * 0.3f, color.b * 0.3f, 0.9f);
        
        // Button component
        Button btn = btnTransform.GetComponent<Button>();
        if (btn != null)
        {
            ColorBlock colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.3f, 1.3f, 1.3f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
            colors.selectedColor = new Color(1.2f, 1.2f, 1.2f);
            btn.colors = colors;
        }
        
        // Text
        TextMeshProUGUI tmp = btnTransform.GetComponentInChildren<TextMeshProUGUI>();
        if (tmp != null)
        {
            tmp.fontSize = 18;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            EditorUtility.SetDirty(tmp);
        }
        
        // Layout
        LayoutElement le = btnTransform.GetComponent<LayoutElement>();
        if (le == null) le = btnTransform.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = 45;
        le.preferredWidth = 150;
        le.flexibleWidth = 1;
        
        EditorUtility.SetDirty(btnTransform.gameObject);
    }
    
    void StyleDetailPanel()
    {
        if (targetPanel == null) return;
        
        Transform detailPanel = targetPanel.transform.Find("SkillDetailPanel");
        if (detailPanel == null) return;
        
        Undo.RecordObject(detailPanel.gameObject, "Style Detail Panel");
        
        // Background
        Image bg = detailPanel.GetComponent<Image>();
        if (bg == null) bg = detailPanel.gameObject.AddComponent<Image>();
        bg.color = detailPanelBg;
        
        // Layout
        VerticalLayoutGroup vlg = detailPanel.GetComponent<VerticalLayoutGroup>();
        if (vlg == null) vlg = detailPanel.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(20, 20, 15, 15);
        vlg.spacing = 10;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        
        // Skill Name
        Transform skillName = detailPanel.Find("SkillName");
        if (skillName != null)
        {
            StyleDetailText(skillName, 24, FontStyles.Bold, goldColor);
        }
        
        // Skill Description
        Transform skillDesc = detailPanel.Find("SkillDecription"); // Note: typo in original
        if (skillDesc == null) skillDesc = detailPanel.Find("SkillDescription");
        if (skillDesc != null)
        {
            StyleDetailText(skillDesc, 14, FontStyles.Italic, labelColor, true);
        }
        
        // Cost Display
        Transform costDisplay = detailPanel.Find("CostDisplay");
        if (costDisplay != null)
        {
            StyleDetailText(costDisplay, 18, FontStyles.Bold, greenColor);
        }
        
        // Pre-requisites
        Transform prereqs = detailPanel.Find("Pre-requisites");
        if (prereqs == null) prereqs = detailPanel.Find("Prerequisites");
        if (prereqs != null)
        {
            StyleDetailText(prereqs, 14, FontStyles.Normal, labelColor, true);
        }
        
        // Unlock Button
        Transform unlockBtn = detailPanel.Find("UnlockButton");
        if (unlockBtn != null)
        {
            StyleButton(unlockBtn.GetComponent<Button>(), "unlock");
        }
        
        // Close Button
        Transform closeBtn = detailPanel.Find("Close");
        if (closeBtn != null)
        {
            StyleButton(closeBtn.GetComponent<Button>(), "close");
        }
        
        EditorUtility.SetDirty(detailPanel.gameObject);
        Debug.Log("Detail panel styled!");
    }
    
    void StyleDetailText(Transform textTransform, int fontSize, FontStyles style, Color color, bool wordWrap = false)
    {
        TextMeshProUGUI tmp = textTransform.GetComponent<TextMeshProUGUI>();
        if (tmp == null) return;
        
        Undo.RecordObject(tmp, "Style Detail Text");
        
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = wordWrap;
        if (wordWrap)
        {
            tmp.overflowMode = TextOverflowModes.Ellipsis;
        }
        
        // Layout element
        LayoutElement le = textTransform.GetComponent<LayoutElement>();
        if (le == null) le = textTransform.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = wordWrap ? fontSize * 3 : fontSize + 15;
        le.preferredWidth = 280;
        
        EditorUtility.SetDirty(tmp);
    }
    
    void StyleScrollView()
    {
        if (targetPanel == null) return;
        
        Transform scrollView = targetPanel.transform.Find("SkillGridScrollView");
        if (scrollView == null) return;
        
        Undo.RecordObject(scrollView.gameObject, "Style Scroll View");
        
        // Scroll view background
        Image scrollBg = scrollView.GetComponent<Image>();
        if (scrollBg == null) scrollBg = scrollView.gameObject.AddComponent<Image>();
        scrollBg.color = new Color(0.02f, 0.02f, 0.05f, 0.8f);
        
        // Style scrollbar if exists
        Scrollbar vScroll = scrollView.GetComponentInChildren<Scrollbar>();
        if (vScroll != null)
        {
            Image scrollbarBg = vScroll.GetComponent<Image>();
            if (scrollbarBg != null)
            {
                scrollbarBg.color = new Color(0.1f, 0.1f, 0.15f, 0.9f);
            }
            
            Image handleImg = vScroll.handleRect?.GetComponent<Image>();
            if (handleImg != null)
            {
                handleImg.color = new Color(0.4f, 0.4f, 0.5f, 0.9f);
            }
        }
        
        // Viewport
        Transform viewport = scrollView.Find("Viewport");
        if (viewport != null)
        {
            Image vpMask = viewport.GetComponent<Image>();
            if (vpMask != null)
            {
                vpMask.color = Color.white; // Mask needs to be white
            }
        }
        
        EditorUtility.SetDirty(scrollView.gameObject);
        Debug.Log("Scroll view styled!");
    }
    
    void StyleButton(Button btn, string buttonType)
    {
        if (btn == null) return;
        
        Undo.RecordObject(btn, "Style Button");
        
        Image btnImg = btn.GetComponent<Image>();
        if (btnImg != null)
        {
            Undo.RecordObject(btnImg, "Style Button Image");
            
            switch (buttonType.ToLower())
            {
                case "close":
                case "cancel":
                    btnImg.color = new Color(0.5f, 0.2f, 0.2f, 0.95f);
                    break;
                case "unlock":
                case "buy":
                case "purchase":
                    btnImg.color = new Color(0.2f, 0.5f, 0.3f, 0.95f);
                    break;
                default:
                    btnImg.color = new Color(0.3f, 0.3f, 0.4f, 0.95f);
                    break;
            }
            
            EditorUtility.SetDirty(btnImg);
        }
        
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
        
        ColorBlock colors = btn.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.2f, 1.2f, 1.2f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
        btn.colors = colors;
        
        // Layout element
        LayoutElement le = btn.GetComponent<LayoutElement>();
        if (le == null) le = btn.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = 40;
        le.preferredWidth = 120;
        
        EditorUtility.SetDirty(btn);
    }
    
    void FixDetailPanelPosition()
    {
        if (targetPanel == null) return;
        
        Transform detailPanel = targetPanel.transform.Find("SkillDetailPanel");
        if (detailPanel == null) return;
        
        Undo.RecordObject(detailPanel.gameObject, "Fix Detail Panel Position");
        
        RectTransform rect = detailPanel.GetComponent<RectTransform>();
        if (rect == null) return;
        
        // Position to the right side of the skill tree, not overlapping
        rect.anchorMin = new Vector2(1, 0.5f);
        rect.anchorMax = new Vector2(1, 0.5f);
        rect.pivot = new Vector2(0, 0.5f);
        rect.anchoredPosition = new Vector2(20, 0); // 20px gap from right edge of parent
        
        // Set a fixed width
        rect.sizeDelta = new Vector2(280, 350);
        
        // Make sure it has a proper background
        Image bg = detailPanel.GetComponent<Image>();
        if (bg == null) bg = detailPanel.gameObject.AddComponent<Image>();
        bg.color = new Color(0.08f, 0.08f, 0.12f, 0.98f);
        
        // Add outline for better visibility
        Outline outline = detailPanel.GetComponent<Outline>();
        if (outline == null) outline = detailPanel.gameObject.AddComponent<Outline>();
        outline.effectColor = goldColor;
        outline.effectDistance = new Vector2(2, -2);
        
        EditorUtility.SetDirty(detailPanel.gameObject);
        Debug.Log("Detail panel position fixed!");
    }
    
    void StyleSkillGridBoxes()
    {
        if (targetPanel == null) return;
        
        // Find the grid content
        Transform scrollView = targetPanel.transform.Find("SkillGridScrollView");
        if (scrollView == null) return;
        
        Transform viewport = scrollView.Find("Viewport");
        if (viewport == null) return;
        
        Transform gridContent = viewport.Find("SkillGridContent");
        if (gridContent == null) gridContent = viewport.Find("Content");
        if (gridContent == null) return;
        
        Undo.RecordObject(gridContent.gameObject, "Style Skill Grid");
        
        // Style the grid layout
        GridLayoutGroup grid = gridContent.GetComponent<GridLayoutGroup>();
        if (grid == null) grid = gridContent.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(80, 80);
        grid.spacing = new Vector2(15, 15);
        grid.padding = new RectOffset(20, 20, 20, 20);
        grid.childAlignment = TextAnchor.UpperCenter;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 4;
        
        // Style each skill box
        foreach (Transform skillBox in gridContent)
        {
            StyleSkillBox(skillBox);
        }
        
        EditorUtility.SetDirty(gridContent.gameObject);
        Debug.Log($"Styled {gridContent.childCount} skill boxes!");
    }
    
    void StyleSkillBox(Transform skillBox)
    {
        Undo.RecordObject(skillBox.gameObject, "Style Skill Box");
        
        // Main box background
        Image bg = skillBox.GetComponent<Image>();
        if (bg != null)
        {
            // Check if skill is unlocked (yellow) or locked (gray)
            bool isUnlocked = bg.color.r > 0.5f && bg.color.g > 0.5f;
            
            if (isUnlocked)
            {
                bg.color = new Color(0.2f, 0.18f, 0.08f, 0.95f); // Dark gold background
            }
            else
            {
                bg.color = new Color(0.1f, 0.1f, 0.12f, 0.95f); // Dark gray background
            }
            
            EditorUtility.SetDirty(bg);
        }
        
        // Add/update outline
        Outline outline = skillBox.GetComponent<Outline>();
        if (outline == null) outline = skillBox.gameObject.AddComponent<Outline>();
        
        // Check button state for outline color
        Button btn = skillBox.GetComponent<Button>();
        if (btn != null)
        {
            // Style button colors
            ColorBlock colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.3f, 1.3f, 1.0f);
            colors.pressedColor = new Color(0.9f, 0.9f, 0.7f);
            colors.disabledColor = new Color(0.5f, 0.5f, 0.5f);
            btn.colors = colors;
            EditorUtility.SetDirty(btn);
        }
        
        // Determine outline color based on skill state
        Image bgImg = skillBox.GetComponent<Image>();
        if (bgImg != null && bgImg.color.r > 0.15f) // Unlocked
        {
            outline.effectColor = goldColor;
            outline.effectDistance = new Vector2(3, -3);
        }
        else // Locked
        {
            outline.effectColor = new Color(0.4f, 0.4f, 0.5f, 0.8f);
            outline.effectDistance = new Vector2(2, -2);
        }
        
        // Style icon if exists
        Transform icon = skillBox.Find("Icon");
        if (icon == null) icon = skillBox.Find("Image");
        if (icon != null)
        {
            Image iconImg = icon.GetComponent<Image>();
            if (iconImg != null)
            {
                // Center the icon
                RectTransform iconRect = icon.GetComponent<RectTransform>();
                if (iconRect != null)
                {
                    iconRect.anchorMin = new Vector2(0.15f, 0.15f);
                    iconRect.anchorMax = new Vector2(0.85f, 0.85f);
                    iconRect.anchoredPosition = Vector2.zero;
                    iconRect.sizeDelta = Vector2.zero;
                }
            }
        }
        
        // Style any text labels
        TextMeshProUGUI[] texts = skillBox.GetComponentsInChildren<TextMeshProUGUI>();
        foreach (var tmp in texts)
        {
            tmp.fontSize = 10;
            tmp.color = new Color(0.9f, 0.85f, 0.6f);
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Truncate;
            EditorUtility.SetDirty(tmp);
        }
        
        EditorUtility.SetDirty(skillBox.gameObject);
    }
}
#endif
