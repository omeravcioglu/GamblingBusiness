#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Editor tool to refine the Card Selection / Event panel to match Company Stats UI style
/// </summary>
public class CardSelectionUIRefiner : EditorWindow
{
    private GameObject targetPanel;
    
    // Style settings
    private readonly Color bgColor = new Color(0.05f, 0.05f, 0.08f, 0.95f);
    private readonly Color cardPanelBg = new Color(0, 0, 0, 0.85f);
    private readonly Color textColor = Color.white;
    private readonly Color labelColor = new Color(0.7f, 0.7f, 0.7f);
    private readonly Color goldColor = new Color(1f, 0.8f, 0.2f);
    private readonly Color highRiskColor = new Color(0.9f, 0.25f, 0.25f);
    private readonly Color avgRiskColor = new Color(0.9f, 0.7f, 0.2f);
    private readonly Color lowRiskColor = new Color(0.3f, 0.75f, 0.35f);
    private readonly Color continueColor = new Color(0.3f, 0.5f, 0.8f);
    private readonly Color separatorColor = new Color(1, 1, 1, 0.2f);
    
    [MenuItem("Tools/Game/Refine CardSelection UI")]
    public static void ShowWindow()
    {
        GetWindow<CardSelectionUIRefiner>("CardSelection Refiner");
    }
    
    void OnGUI()
    {
        GUILayout.Label("Card Selection Panel Refiner", EditorStyles.boldLabel);
        GUILayout.Space(10);
        
        EditorGUILayout.HelpBox(
            "Refines the Card Selection / Event panel:\n\n" +
            "• Dark themed background\n" +
            "• Risk buttons: High (Red), Avg (Yellow), Low (Green)\n" +
            "• Styled result panel\n" +
            "• Continue button styling",
            MessageType.Info);
        
        GUILayout.Space(10);
        
        targetPanel = (GameObject)EditorGUILayout.ObjectField("Target Panel", targetPanel, typeof(GameObject), true);
        
        GUILayout.Space(5);
        
        if (targetPanel == null)
        {
            if (GUILayout.Button("Find Event/Card Panel"))
            {
                FindPanel();
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
        
        if (GUILayout.Button("Style Risk Buttons Only"))
        {
            StyleRiskButtons();
        }
        
        if (GUILayout.Button("Style Result Panel Only"))
        {
            StyleResultPanel();
        }
        
        GUI.enabled = true;
    }
    
    void FindPanel()
    {
        GameObject found = GameObject.Find("CardSelectionPanel");
        if (found == null) found = GameObject.Find("Event Panel");
        if (found == null) found = GameObject.Find("EventPanel");
        if (found == null) found = GameObject.Find("CardPanel");
        
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
            Debug.LogWarning("Could not find Card Selection panel. Please select it manually.");
        }
    }
    
    void RefinePanel()
    {
        if (targetPanel == null) return;
        
        Undo.RegisterCompleteObjectUndo(targetPanel, "Refine Card Selection Panel");
        
        // Find CardSelectionPanel if we're on Event Panel
        Transform cardPanel = targetPanel.transform.Find("CardSelectionPanel");
        if (cardPanel == null) cardPanel = targetPanel.transform;
        
        // Main panel background - transparent to show game behind
        Image bg = cardPanel.GetComponent<Image>();
        if (bg != null)
        {
            bg.color = new Color(0, 0, 0, 0.3f); // Semi-transparent dark overlay
        }
        
        // Remove vertical layout if exists, add horizontal for cards
        VerticalLayoutGroup vlg = cardPanel.GetComponent<VerticalLayoutGroup>();
        if (vlg != null) DestroyImmediate(vlg);
        
        HorizontalLayoutGroup hlg = cardPanel.GetComponent<HorizontalLayoutGroup>();
        if (hlg == null) hlg = cardPanel.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(40, 40, 30, 30);
        hlg.spacing = 25;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = false;
        hlg.childControlHeight = false;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        
        StyleRiskButtons();
        StyleResultPanel();
        StyleContinueButton();
        
        EditorUtility.SetDirty(cardPanel.gameObject);
        Debug.Log("Card Selection panel refined!");
    }
    
    void StyleRiskButtons()
    {
        if (targetPanel == null) return;
        
        Transform cardPanel = targetPanel.transform.Find("CardSelectionPanel");
        if (cardPanel == null) cardPanel = targetPanel.transform;
        
        // High Risk
        Transform highRisk = cardPanel.Find("HighRisk");
        if (highRisk != null)
        {
            StyleRiskButton(highRisk, highRiskColor, "HIGH RISK", "High reward, high danger!");
        }
        
        // Avg Risk
        Transform avgRisk = cardPanel.Find("AvgRisk");
        if (avgRisk == null) avgRisk = cardPanel.Find("MediumRisk");
        if (avgRisk == null) avgRisk = cardPanel.Find("MidRisk");
        if (avgRisk != null)
        {
            StyleRiskButton(avgRisk, avgRiskColor, "MEDIUM RISK", "Balanced risk and reward");
        }
        
        // Low Risk
        Transform lowRisk = cardPanel.Find("LowRisk");
        if (lowRisk != null)
        {
            StyleRiskButton(lowRisk, lowRiskColor, "LOW RISK", "Safe but modest gains");
        }
        
        Debug.Log("Risk buttons styled!");
    }
    
    void StyleRiskButton(Transform btnTransform, Color color, string title, string subtitle = "")
    {
        Undo.RecordObject(btnTransform.gameObject, "Style Risk Button");
        
        // Card-style background - light/white with slight tint
        Image bg = btnTransform.GetComponent<Image>();
        if (bg == null) bg = btnTransform.gameObject.AddComponent<Image>();
        bg.color = new Color(0.95f, 0.95f, 0.93f, 1f); // Off-white card color
        
        // Remove outline if exists (cards don't have colored outlines)
        Outline outline = btnTransform.GetComponent<Outline>();
        if (outline != null) DestroyImmediate(outline);
        
        // Add shadow effect instead
        Shadow shadow = btnTransform.GetComponent<Shadow>();
        if (shadow == null) shadow = btnTransform.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0, 0, 0, 0.3f);
        shadow.effectDistance = new Vector2(4, -4);
        
        // Set RectTransform for card size
        RectTransform rect = btnTransform.GetComponent<RectTransform>();
        if (rect != null)
        {
            rect.sizeDelta = new Vector2(120, 180); // Card proportions
        }
        
        // Button
        Button btn = btnTransform.GetComponent<Button>();
        if (btn != null)
        {
            ColorBlock colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.92f, 0.92f, 0.88f); // Slight darken on hover
            colors.pressedColor = new Color(0.85f, 0.85f, 0.82f);
            colors.selectedColor = Color.white;
            btn.colors = colors;
            EditorUtility.SetDirty(btn);
        }
        
        // Text - dark text on light card
        TextMeshProUGUI tmp = btnTransform.GetComponentInChildren<TextMeshProUGUI>();
        if (tmp != null)
        {
            Undo.RecordObject(tmp, "Style Risk Button Text");
            tmp.fontSize = 24;
            tmp.fontStyle = FontStyles.Normal;
            tmp.color = new Color(0.2f, 0.2f, 0.2f, 1f); // Dark gray text
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;
            EditorUtility.SetDirty(tmp);
        }
        
        // Layout element for horizontal arrangement
        LayoutElement le = btnTransform.GetComponent<LayoutElement>();
        if (le == null) le = btnTransform.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = 180;
        le.preferredWidth = 120;
        le.minHeight = 160;
        le.minWidth = 100;
        
        EditorUtility.SetDirty(btnTransform.gameObject);
    }
    
    void StyleResultPanel()
    {
        if (targetPanel == null) return;
        
        Transform cardPanel = targetPanel.transform.Find("CardSelectionPanel");
        if (cardPanel == null) cardPanel = targetPanel.transform;
        
        Transform resultPanel = cardPanel.Find("ResultPanel");
        if (resultPanel == null) return;
        
        Undo.RecordObject(resultPanel.gameObject, "Style Result Panel");
        
        // Card-style background - same as risk cards but slightly different
        Image bg = resultPanel.GetComponent<Image>();
        if (bg == null) bg = resultPanel.gameObject.AddComponent<Image>();
        bg.color = new Color(0.95f, 0.95f, 0.93f, 1f); // Off-white card
        
        // Remove outline, add shadow
        Outline outline = resultPanel.GetComponent<Outline>();
        if (outline != null) DestroyImmediate(outline);
        
        Shadow shadow = resultPanel.GetComponent<Shadow>();
        if (shadow == null) shadow = resultPanel.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0, 0, 0, 0.3f);
        shadow.effectDistance = new Vector2(4, -4);
        
        // Layout
        VerticalLayoutGroup vlg = resultPanel.GetComponent<VerticalLayoutGroup>();
        if (vlg == null) vlg = resultPanel.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(20, 20, 20, 20);
        vlg.spacing = 10;
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        
        // Set size like a card
        RectTransform rect = resultPanel.GetComponent<RectTransform>();
        if (rect != null)
        {
            rect.sizeDelta = new Vector2(280, 200);
        }
        
        // Layout element
        LayoutElement le = resultPanel.GetComponent<LayoutElement>();
        if (le == null) le = resultPanel.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = 280;
        le.preferredHeight = 200;
        le.ignoreLayout = true; // Don't participate in horizontal layout
        
        // Style all text children - dark text on light card
        TextMeshProUGUI[] texts = resultPanel.GetComponentsInChildren<TextMeshProUGUI>();
        for (int i = 0; i < texts.Length; i++)
        {
            Undo.RecordObject(texts[i], "Style Result Text");
            
            if (i == 0)
            {
                // First text - title/result
                texts[i].fontSize = 24;
                texts[i].fontStyle = FontStyles.Bold;
                texts[i].color = new Color(0.15f, 0.15f, 0.15f, 1f); // Dark text
                texts[i].alignment = TextAlignmentOptions.Center;
            }
            else if (i == 1)
            {
                // Second text - description/outcome
                texts[i].fontSize = 16;
                texts[i].fontStyle = FontStyles.Normal;
                texts[i].color = new Color(0.3f, 0.3f, 0.3f, 1f);
                texts[i].alignment = TextAlignmentOptions.Center;
                texts[i].enableWordWrapping = true;
            }
            else
            {
                // Additional text - details
                texts[i].fontSize = 14;
                texts[i].fontStyle = FontStyles.Italic;
                texts[i].color = new Color(0.4f, 0.4f, 0.4f, 1f);
                texts[i].alignment = TextAlignmentOptions.Center;
                texts[i].enableWordWrapping = true;
            }
            
            // Layout element for text
            LayoutElement textLE = texts[i].GetComponent<LayoutElement>();
            if (textLE == null) textLE = texts[i].gameObject.AddComponent<LayoutElement>();
            textLE.preferredHeight = texts[i].fontSize + 12;
            textLE.preferredWidth = 240;
            
            EditorUtility.SetDirty(texts[i]);
        }
        
        EditorUtility.SetDirty(resultPanel.gameObject);
        Debug.Log("Result panel styled!");
    }
    
    void StyleContinueButton()
    {
        if (targetPanel == null) return;
        
        Transform cardPanel = targetPanel.transform.Find("CardSelectionPanel");
        if (cardPanel == null) cardPanel = targetPanel.transform;
        
        Transform continueBtn = cardPanel.Find("Continue");
        if (continueBtn == null) continueBtn = cardPanel.Find("ContinueButton");
        if (continueBtn == null) continueBtn = cardPanel.Find("OK");
        if (continueBtn == null) return;
        
        Undo.RecordObject(continueBtn.gameObject, "Style Continue Button");
        
        // Card-style background
        Image bg = continueBtn.GetComponent<Image>();
        if (bg == null) bg = continueBtn.gameObject.AddComponent<Image>();
        bg.color = new Color(0.92f, 0.92f, 0.88f, 1f); // Slightly darker off-white
        
        // Remove outline, add shadow
        Outline outline = continueBtn.GetComponent<Outline>();
        if (outline != null) DestroyImmediate(outline);
        
        Shadow shadow = continueBtn.GetComponent<Shadow>();
        if (shadow == null) shadow = continueBtn.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0, 0, 0, 0.25f);
        shadow.effectDistance = new Vector2(3, -3);
        
        // Button
        Button btn = continueBtn.GetComponent<Button>();
        if (btn != null)
        {
            ColorBlock colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.88f, 0.88f, 0.84f);
            colors.pressedColor = new Color(0.82f, 0.82f, 0.78f);
            btn.colors = colors;
            EditorUtility.SetDirty(btn);
        }
        
        // Text - dark on light
        TextMeshProUGUI tmp = continueBtn.GetComponentInChildren<TextMeshProUGUI>();
        if (tmp != null)
        {
            Undo.RecordObject(tmp, "Style Continue Text");
            tmp.fontSize = 20;
            tmp.fontStyle = FontStyles.Normal;
            tmp.color = new Color(0.25f, 0.25f, 0.25f, 1f);
            tmp.alignment = TextAlignmentOptions.Center;
            EditorUtility.SetDirty(tmp);
        }
        
        // Layout - ignore main layout, position separately
        LayoutElement le = continueBtn.GetComponent<LayoutElement>();
        if (le == null) le = continueBtn.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = 45;
        le.preferredWidth = 140;
        le.ignoreLayout = true;
        
        // Position at bottom
        RectTransform rect = continueBtn.GetComponent<RectTransform>();
        if (rect != null)
        {
            rect.anchorMin = new Vector2(0.5f, 0);
            rect.anchorMax = new Vector2(0.5f, 0);
            rect.pivot = new Vector2(0.5f, 0);
            rect.anchoredPosition = new Vector2(0, 20);
            rect.sizeDelta = new Vector2(140, 45);
        }
        
        EditorUtility.SetDirty(continueBtn.gameObject);
        Debug.Log("Continue button styled!");
    }
}
#endif
