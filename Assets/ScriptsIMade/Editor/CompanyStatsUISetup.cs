#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Editor tool to create the Company Stats UI panel
/// </summary>
public class CompanyStatsUISetup : EditorWindow
{
    private enum PanelPosition
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight,
        TopCenter
    }
    
    private PanelPosition panelPosition = PanelPosition.TopLeft;
    private bool showAllStats = true;
    private bool showDemographics = true;
    private bool showRaceStats = true;
    private bool showHappinessTrust = true;
    private bool darkTheme = true;
    
    [MenuItem("Tools/Create Company Stats UI")]
    public static void ShowWindow()
    {
        GetWindow<CompanyStatsUISetup>("Company Stats UI Setup");
    }
    
    void OnGUI()
    {
        GUILayout.Label("Company Stats UI Creator", EditorStyles.boldLabel);
        EditorGUILayout.Space();
        
        panelPosition = (PanelPosition)EditorGUILayout.EnumPopup("Panel Position", panelPosition);
        darkTheme = EditorGUILayout.Toggle("Dark Theme", darkTheme);
        
        EditorGUILayout.Space();
        GUILayout.Label("Stats to Display:", EditorStyles.boldLabel);
        showAllStats = EditorGUILayout.Toggle("Main Stats (Value, Day, Employees)", showAllStats);
        showDemographics = EditorGUILayout.Toggle("Demographics (Ratio, IQ, Age, Exp)", showDemographics);
        showRaceStats = EditorGUILayout.Toggle("Race Statistics", showRaceStats);
        showHappinessTrust = EditorGUILayout.Toggle("Happiness & Trust (with bars)", showHappinessTrust);
        
        EditorGUILayout.Space();
        
        if (GUILayout.Button("Create Stats Panel", GUILayout.Height(40)))
        {
            CreateStatsPanel();
        }
        
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "This will create a Company Stats UI panel on the Canvas.\n\n" +
            "Make sure you have a Canvas in your scene.\n\n" +
            "After creation, the panel will have the CompanyStatsUI script attached with all text fields connected.",
            MessageType.Info);
    }
    
    void CreateStatsPanel()
    {
        // Find Canvas
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            EditorUtility.DisplayDialog("Error", "No Canvas found in the scene! Please create a Canvas first.", "OK");
            return;
        }
        
        // Create main panel
        GameObject statsPanel = new GameObject("CompanyStatsPanel");
        statsPanel.transform.SetParent(canvas.transform, false);
        
        RectTransform panelRect = statsPanel.AddComponent<RectTransform>();
        Image panelBg = statsPanel.AddComponent<Image>();
        VerticalLayoutGroup vlg = statsPanel.AddComponent<VerticalLayoutGroup>();
        ContentSizeFitter csf = statsPanel.AddComponent<ContentSizeFitter>();
        
        // Panel background
        panelBg.color = darkTheme ? new Color(0, 0, 0, 0.7f) : new Color(1, 1, 1, 0.8f);
        
        // Layout settings
        vlg.padding = new RectOffset(15, 15, 15, 15);
        vlg.spacing = 8;
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        
        // Position the panel
        SetPanelPosition(panelRect);
        
        // Add CompanyStatsUI component
        CompanyStatsUI statsUI = statsPanel.AddComponent<CompanyStatsUI>();
        
        Color textColor = darkTheme ? Color.white : Color.black;
        Color labelColor = darkTheme ? new Color(0.7f, 0.7f, 0.7f) : new Color(0.3f, 0.3f, 0.3f);
        
        // Create stats rows
        if (showAllStats)
        {
            // Company Value (larger, prominent)
            GameObject valueRow = CreateStatRow(statsPanel.transform, "ValueRow", "Company Value", "$0", 32, textColor, labelColor);
            statsUI.valueText = valueRow.transform.Find("Value").GetComponent<TextMeshProUGUI>();
            statsUI.valueLabelText = valueRow.transform.Find("Label").GetComponent<TextMeshProUGUI>();
            
            // Daily change indicator
            GameObject changeRow = CreateStatRow(statsPanel.transform, "DailyChangeRow", "Today", "+$0", 18, new Color(0.2f, 0.8f, 0.2f), labelColor);
            statsUI.dailyChangeText = changeRow.transform.Find("Value").GetComponent<TextMeshProUGUI>();
            
            // Separator
            CreateSeparator(statsPanel.transform, darkTheme);
            
            // Day
            GameObject dayRow = CreateStatRow(statsPanel.transform, "DayRow", "Day", "1", 24, textColor, labelColor);
            statsUI.dayText = dayRow.transform.Find("Value").GetComponent<TextMeshProUGUI>();
            statsUI.dayLabelText = dayRow.transform.Find("Label").GetComponent<TextMeshProUGUI>();
            
            // Employees
            GameObject empRow = CreateStatRow(statsPanel.transform, "EmployeeRow", "Employees", "0", 20, textColor, labelColor);
            statsUI.employeeCountText = empRow.transform.Find("Value").GetComponent<TextMeshProUGUI>();
            statsUI.employeeLabelText = empRow.transform.Find("Label").GetComponent<TextMeshProUGUI>();
        }
        
        if (showDemographics)
        {
            CreateSeparator(statsPanel.transform, darkTheme);
            
            // Create header
            CreateHeader(statsPanel.transform, "Demographics", labelColor);
            
            // M:F Ratio
            GameObject ratioRow = CreateStatRow(statsPanel.transform, "RatioRow", "M : F Ratio", "0 : 0", 18, textColor, labelColor);
            statsUI.ratioText = ratioRow.transform.Find("Value").GetComponent<TextMeshProUGUI>();
            statsUI.ratioLabelText = ratioRow.transform.Find("Label").GetComponent<TextMeshProUGUI>();
            
            // Avg IQ
            GameObject iqRow = CreateStatRow(statsPanel.transform, "IQRow", "Avg IQ", "0", 18, textColor, labelColor);
            statsUI.avgIQText = iqRow.transform.Find("Value").GetComponent<TextMeshProUGUI>();
            statsUI.iqLabelText = iqRow.transform.Find("Label").GetComponent<TextMeshProUGUI>();
            
            // Avg Age
            GameObject ageRow = CreateStatRow(statsPanel.transform, "AgeRow", "Avg Age", "0", 18, textColor, labelColor);
            statsUI.avgAgeText = ageRow.transform.Find("Value").GetComponent<TextMeshProUGUI>();
            statsUI.ageLabelText = ageRow.transform.Find("Label").GetComponent<TextMeshProUGUI>();
            
            // Experience
            GameObject expRow = CreateStatRow(statsPanel.transform, "ExpRow", "Experience", "0 yrs", 18, textColor, labelColor);
            statsUI.avgExperienceText = expRow.transform.Find("Value").GetComponent<TextMeshProUGUI>();
            statsUI.expLabelText = expRow.transform.Find("Label").GetComponent<TextMeshProUGUI>();
        }
        
        if (showRaceStats)
        {
            CreateSeparator(statsPanel.transform, darkTheme);
            
            // Create header
            CreateHeader(statsPanel.transform, "Diversity", labelColor);
            
            // Most common race
            GameObject mostRow = CreateStatRow(statsPanel.transform, "MostRaceRow", "Most Common", "-", 18, textColor, labelColor);
            statsUI.mostRaceText = mostRow.transform.Find("Value").GetComponent<TextMeshProUGUI>();
            statsUI.mostRaceLabelText = mostRow.transform.Find("Label").GetComponent<TextMeshProUGUI>();
            
            // Least common race
            GameObject leastRow = CreateStatRow(statsPanel.transform, "LeastRaceRow", "Least Common", "-", 18, textColor, labelColor);
            statsUI.leastRaceText = leastRow.transform.Find("Value").GetComponent<TextMeshProUGUI>();
            statsUI.leastRaceLabelText = leastRow.transform.Find("Label").GetComponent<TextMeshProUGUI>();
        }
        
        if (showHappinessTrust)
        {
            CreateSeparator(statsPanel.transform, darkTheme);
            
            // Create header
            CreateHeader(statsPanel.transform, "Company Health", labelColor);
            
            // Employee Happiness with progress bar
            GameObject happinessRow = CreateStatRowWithBar(statsPanel.transform, "HappinessRow", "Employee Happiness", "50%", 18, textColor, labelColor, new Color(0.2f, 0.8f, 0.4f));
            statsUI.employeeHappinessText = happinessRow.transform.Find("TopRow/Value").GetComponent<TextMeshProUGUI>();
            statsUI.employeeHappinessFill = happinessRow.transform.Find("BarBg/BarFill").GetComponent<Image>();
            
            // Investor Trust with progress bar
            GameObject trustRow = CreateStatRowWithBar(statsPanel.transform, "TrustRow", "Investor Trust", "50%", 18, textColor, labelColor, new Color(0.3f, 0.6f, 0.9f));
            statsUI.investorTrustText = trustRow.transform.Find("TopRow/Value").GetComponent<TextMeshProUGUI>();
            statsUI.investorTrustFill = trustRow.transform.Find("BarBg/BarFill").GetComponent<Image>();
        }
        
        // Select the created panel
        Selection.activeGameObject = statsPanel;
        EditorUtility.SetDirty(statsPanel);
        
        Debug.Log("Company Stats UI Panel created successfully!");
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
    
    GameObject CreateStatRow(Transform parent, string name, string labelText, string valueText, int fontSize, Color valueColor, Color labelColor)
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
        labelTMP.color = labelColor;
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
    
    GameObject CreateStatRowWithBar(Transform parent, string name, string labelText, string valueText, int fontSize, Color valueColor, Color labelColor, Color barColor)
    {
        GameObject row = new GameObject(name);
        row.transform.SetParent(parent, false);
        
        RectTransform rowRect = row.AddComponent<RectTransform>();
        VerticalLayoutGroup vlg = row.AddComponent<VerticalLayoutGroup>();
        
        vlg.spacing = 4;
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        
        // Top row with label and value
        GameObject topRow = new GameObject("TopRow");
        topRow.transform.SetParent(row.transform, false);
        RectTransform topRowRect = topRow.AddComponent<RectTransform>();
        HorizontalLayoutGroup hlg = topRow.AddComponent<HorizontalLayoutGroup>();
        
        hlg.spacing = 10;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = false;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        
        // Label
        GameObject labelGO = new GameObject("Label");
        labelGO.transform.SetParent(topRow.transform, false);
        RectTransform labelRect = labelGO.AddComponent<RectTransform>();
        labelRect.sizeDelta = new Vector2(140, 25);
        TextMeshProUGUI labelTMP = labelGO.AddComponent<TextMeshProUGUI>();
        labelTMP.text = labelText;
        labelTMP.fontSize = fontSize * 0.6f;
        labelTMP.color = labelColor;
        labelTMP.alignment = TextAlignmentOptions.Left;
        
        // Value (moved to row level for script access)
        GameObject valueGO = new GameObject("Value");
        valueGO.transform.SetParent(row.transform, false);
        valueGO.transform.SetSiblingIndex(0); // Put it at the root for easy access
        RectTransform valueRect = valueGO.AddComponent<RectTransform>();
        valueRect.sizeDelta = new Vector2(80, 25);
        TextMeshProUGUI valueTMP = valueGO.AddComponent<TextMeshProUGUI>();
        valueTMP.text = valueText;
        valueTMP.fontSize = fontSize;
        valueTMP.color = valueColor;
        valueTMP.alignment = TextAlignmentOptions.Right;
        valueTMP.fontStyle = FontStyles.Bold;
        
        // Reparent value to top row
        valueGO.transform.SetParent(topRow.transform, false);
        
        // Progress bar background
        GameObject barBg = new GameObject("BarBg");
        barBg.transform.SetParent(row.transform, false);
        RectTransform barBgRect = barBg.AddComponent<RectTransform>();
        barBgRect.sizeDelta = new Vector2(220, 8);
        Image barBgImage = barBg.AddComponent<Image>();
        barBgImage.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);
        
        LayoutElement barBgLE = barBg.AddComponent<LayoutElement>();
        barBgLE.preferredHeight = 8;
        barBgLE.flexibleWidth = 1;
        
        // Progress bar fill (uses anchorMax.x to control fill amount)
        GameObject barFill = new GameObject("BarFill");
        barFill.transform.SetParent(barBg.transform, false);
        RectTransform barFillRect = barFill.AddComponent<RectTransform>();
        
        // Anchor-based fill: left edge anchored at 0, right edge at 0.5 (50%)
        barFillRect.anchorMin = Vector2.zero;
        barFillRect.anchorMax = new Vector2(0.5f, 1f); // 50% width
        barFillRect.offsetMin = Vector2.zero;
        barFillRect.offsetMax = Vector2.zero;
        barFillRect.pivot = new Vector2(0f, 0.5f);
        
        Image barFillImage = barFill.AddComponent<Image>();
        barFillImage.color = barColor;
        // Simple image type works with anchor scaling
        barFillImage.type = Image.Type.Simple;
        
        return row;
    }
    
    void CreateSeparator(Transform parent, bool dark)
    {
        GameObject sep = new GameObject("Separator");
        sep.transform.SetParent(parent, false);
        
        RectTransform sepRect = sep.AddComponent<RectTransform>();
        sepRect.sizeDelta = new Vector2(200, 2);
        
        Image sepImage = sep.AddComponent<Image>();
        sepImage.color = dark ? new Color(1, 1, 1, 0.2f) : new Color(0, 0, 0, 0.2f);
        
        LayoutElement le = sep.AddComponent<LayoutElement>();
        le.preferredHeight = 2;
        le.flexibleWidth = 1;
    }
    
    void CreateHeader(Transform parent, string text, Color color)
    {
        GameObject header = new GameObject(text + "Header");
        header.transform.SetParent(parent, false);
        
        RectTransform headerRect = header.AddComponent<RectTransform>();
        headerRect.sizeDelta = new Vector2(200, 25);
        
        TextMeshProUGUI headerTMP = header.AddComponent<TextMeshProUGUI>();
        headerTMP.text = text.ToUpper();
        headerTMP.fontSize = 12;
        headerTMP.color = color;
        headerTMP.alignment = TextAlignmentOptions.Left;
        headerTMP.fontStyle = FontStyles.Bold;
        
        LayoutElement le = header.AddComponent<LayoutElement>();
        le.preferredHeight = 25;
    }
}
#endif
