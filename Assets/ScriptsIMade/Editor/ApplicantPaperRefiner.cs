#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using TMPro;

/// <summary>
/// Editor tool to refine and organize the Applicant Paper layout
/// </summary>
public class ApplicantPaperRefiner : EditorWindow
{
    private GameObject targetPaper;
    
    // Layout settings
    private float paperWidth = 2.5f;
    private float paperHeight = 3.5f;
    private float margin = 0.15f;
    private float lineSpacing = 0.18f;
    private float sectionSpacing = 0.25f;
    
    // Text sizes
    private float nameSize = 0.25f;
    private float labelSize = 0.12f;
    private float valueSize = 0.14f;
    private float smallSize = 0.10f;
    
    // Colors
    private Color nameColor = new Color(0.1f, 0.1f, 0.1f);
    private Color labelColor = new Color(0.3f, 0.3f, 0.3f);
    private Color valueColor = new Color(0.15f, 0.15f, 0.15f);
    
    [MenuItem("Tools/Game/Refine Applicant Paper")]
    public static void ShowWindow()
    {
        GetWindow<ApplicantPaperRefiner>("Applicant Paper Refiner");
    }
    
    void OnGUI()
    {
        GUILayout.Label("Applicant Paper Refiner", EditorStyles.boldLabel);
        GUILayout.Space(10);
        
        EditorGUILayout.HelpBox(
            "Organizes the Applicant Paper into a clean resume layout:\n\n" +
            "• Name at top (large, centered)\n" +
            "• Photo on right side\n" +
            "• Personal info section (Age, Gender, Race)\n" +
            "• Education section (Education, Degree, GPA, Major)\n" +
            "• Professional section (Experience, IQ, Salary)\n" +
            "• Additional info (Criminal, Traits)",
            MessageType.Info);
        
        GUILayout.Space(10);
        
        targetPaper = (GameObject)EditorGUILayout.ObjectField("Target Paper", targetPaper, typeof(GameObject), true);
        
        GUILayout.Space(5);
        
        if (targetPaper == null)
        {
            if (GUILayout.Button("Find Applicant Paper"))
            {
                FindApplicantPaper();
            }
        }
        
        GUILayout.Space(10);
        
        EditorGUILayout.LabelField("Layout Settings", EditorStyles.boldLabel);
        lineSpacing = EditorGUILayout.Slider("Line Spacing", lineSpacing, 0.1f, 0.3f);
        sectionSpacing = EditorGUILayout.Slider("Section Spacing", sectionSpacing, 0.15f, 0.4f);
        
        GUILayout.Space(10);
        
        EditorGUILayout.LabelField("Text Sizes", EditorStyles.boldLabel);
        nameSize = EditorGUILayout.Slider("Name Size", nameSize, 0.15f, 0.4f);
        labelSize = EditorGUILayout.Slider("Label Size", labelSize, 0.08f, 0.2f);
        valueSize = EditorGUILayout.Slider("Value Size", valueSize, 0.1f, 0.25f);
        
        GUILayout.Space(15);
        
        GUI.enabled = targetPaper != null;
        
        if (GUILayout.Button("ORGANIZE PAPER LAYOUT", GUILayout.Height(40)))
        {
            OrganizePaper();
        }
        
        GUILayout.Space(10);
        
        if (GUILayout.Button("Style Text Only (Keep Positions)"))
        {
            StyleTextOnly();
        }
        
        if (GUILayout.Button("Reset to Default Positions"))
        {
            ResetPositions();
        }
        
        GUI.enabled = true;
    }
    
    void FindApplicantPaper()
    {
        GameObject found = GameObject.Find("ApplicantPaper");
        if (found == null) found = GameObject.Find("Applicant Paper");
        if (found == null) found = GameObject.Find("ApplicationPaper");
        if (found == null) found = GameObject.Find("Resume");
        
        if (found != null)
        {
            targetPaper = found;
            Debug.Log($"Found paper: {found.name}");
        }
        else if (Selection.activeGameObject != null)
        {
            targetPaper = Selection.activeGameObject;
            Debug.Log($"Using selected object: {targetPaper.name}");
        }
        else
        {
            Debug.LogWarning("Could not find Applicant Paper. Please select it manually.");
        }
    }
    
    void OrganizePaper()
    {
        if (targetPaper == null) return;
        
        Undo.RegisterCompleteObjectUndo(targetPaper, "Organize Applicant Paper");
        
        // Find the Cube child which contains all text
        Transform cube = targetPaper.transform.Find("Cube");
        if (cube == null) cube = targetPaper.transform;
        
        float currentY = paperHeight / 2 - margin;
        float leftX = -paperWidth / 2 + margin;
        float rightX = paperWidth / 2 - margin - 0.6f; // Space for photo
        float centerX = 0f;
        
        // ===== NAME (Top, Centered) =====
        PositionText(cube, "NameText", new Vector3(centerX, currentY, -0.01f), nameSize, nameColor, TextAlignmentOptions.Center, true);
        currentY -= sectionSpacing;
        
        // ===== PHOTO (Top Right) =====
        Transform picture = cube.Find("Picture");
        if (picture != null)
        {
            picture.localPosition = new Vector3(rightX + 0.3f, currentY + 0.1f, -0.01f);
            picture.localScale = new Vector3(0.5f, 0.6f, 1f);
        }
        
        // ===== PERSONAL INFO SECTION =====
        currentY -= lineSpacing * 0.5f;
        
        // Age
        PositionLabelValue(cube, "Age", "AgeText", leftX, currentY);
        currentY -= lineSpacing;
        
        // Gender
        PositionLabelValue(cube, "Gender", "GenderText", leftX, currentY);
        currentY -= lineSpacing;
        
        // Race
        PositionLabelValue(cube, "Race", "RaceText", leftX, currentY);
        currentY -= sectionSpacing;
        
        // ===== EDUCATION SECTION =====
        // Education level
        PositionLabelValue(cube, "Education", "EducationText", leftX, currentY);
        currentY -= lineSpacing;
        
        // Degree
        PositionLabelValue(cube, "Degree", "DegreeText", leftX, currentY);
        currentY -= lineSpacing;
        
        // Major (find both Major elements)
        Transform major1 = cube.Find("Major");
        Transform major2 = null;
        foreach (Transform child in cube)
        {
            if (child.name == "Major" && child != major1)
            {
                major2 = child;
                break;
            }
        }
        if (major1 != null)
        {
            PositionText(cube, major1, new Vector3(leftX, currentY, -0.01f), labelSize, labelColor, TextAlignmentOptions.Left, false);
        }
        if (major2 != null)
        {
            PositionText(cube, major2, new Vector3(leftX + 0.5f, currentY, -0.01f), valueSize, valueColor, TextAlignmentOptions.Left, false);
        }
        currentY -= lineSpacing;
        
        // GPA
        PositionLabelValue(cube, "Gpa", "GpaText", leftX, currentY);
        currentY -= sectionSpacing;
        
        // ===== PROFESSIONAL SECTION =====
        // Experience
        PositionLabelValue(cube, "Experience", "ExperienceText", leftX, currentY);
        currentY -= lineSpacing;
        
        // IQ
        PositionLabelValue(cube, "IQ", "IQText", leftX, currentY);
        currentY -= lineSpacing;
        
        // Salary
        PositionLabelValue(cube, "Salary", "SalaryText", leftX, currentY);
        currentY -= sectionSpacing;
        
        // ===== ADDITIONAL INFO =====
        // Criminal Record
        PositionLabelValue(cube, "Criminal", "CriminalText", leftX, currentY);
        currentY -= lineSpacing;
        
        // Traits
        PositionLabelValue(cube, "Traits", "TraitsText", leftX, currentY);
        
        EditorUtility.SetDirty(targetPaper);
        Debug.Log("Applicant Paper organized!");
    }
    
    void PositionLabelValue(Transform parent, string labelName, string valueName, float x, float y)
    {
        float labelWidth = 0.6f;
        
        // Position label
        Transform label = parent.Find(labelName);
        if (label != null)
        {
            PositionText(parent, label, new Vector3(x, y, -0.01f), labelSize, labelColor, TextAlignmentOptions.Left, false);
            
            // Set label text to include colon if not present
            TextMeshPro tmp = label.GetComponent<TextMeshPro>();
            if (tmp != null && !tmp.text.EndsWith(":"))
            {
                // Don't modify the text, just style it
            }
        }
        
        // Position value
        Transform value = parent.Find(valueName);
        if (value != null)
        {
            PositionText(parent, value, new Vector3(x + labelWidth, y, -0.01f), valueSize, valueColor, TextAlignmentOptions.Left, false);
        }
    }
    
    void PositionText(Transform parent, string childName, Vector3 position, float size, Color color, TextAlignmentOptions alignment, bool bold)
    {
        Transform child = parent.Find(childName);
        if (child != null)
        {
            PositionText(parent, child, position, size, color, alignment, bold);
        }
    }
    
    void PositionText(Transform parent, Transform child, Vector3 position, float size, Color color, TextAlignmentOptions alignment, bool bold)
    {
        Undo.RecordObject(child, "Position Text");
        
        child.localPosition = position;
        
        // Try TextMeshPro (3D)
        TextMeshPro tmp3D = child.GetComponent<TextMeshPro>();
        if (tmp3D != null)
        {
            Undo.RecordObject(tmp3D, "Style TextMeshPro");
            tmp3D.fontSize = size * 100; // TMP uses different scale
            tmp3D.color = color;
            tmp3D.alignment = alignment;
            tmp3D.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            tmp3D.enableWordWrapping = true;
            tmp3D.overflowMode = TextOverflowModes.Ellipsis;
            EditorUtility.SetDirty(tmp3D);
        }
        
        // Try TextMeshProUGUI (UI)
        TextMeshProUGUI tmpUI = child.GetComponent<TextMeshProUGUI>();
        if (tmpUI != null)
        {
            Undo.RecordObject(tmpUI, "Style TextMeshProUGUI");
            tmpUI.fontSize = size * 100;
            tmpUI.color = color;
            tmpUI.alignment = alignment;
            tmpUI.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            tmpUI.enableWordWrapping = true;
            tmpUI.overflowMode = TextOverflowModes.Ellipsis;
            EditorUtility.SetDirty(tmpUI);
        }
        
        EditorUtility.SetDirty(child.gameObject);
    }
    
    void StyleTextOnly()
    {
        if (targetPaper == null) return;
        
        Undo.RegisterCompleteObjectUndo(targetPaper, "Style Applicant Paper Text");
        
        Transform cube = targetPaper.transform.Find("Cube");
        if (cube == null) cube = targetPaper.transform;
        
        // Style name
        StyleSingleText(cube, "NameText", nameSize, nameColor, true);
        
        // Style labels (smaller, gray)
        string[] labels = { "Age", "Gender", "Race", "Education", "Degree", "Gpa", "Salary", "Criminal", "Traits", "Experience", "IQ" };
        foreach (string label in labels)
        {
            StyleSingleText(cube, label, labelSize, labelColor, false);
        }
        
        // Style values (slightly larger, darker)
        string[] values = { "AgeText", "GenderText", "RaceText", "EducationText", "DegreeText", "GpaText", "SalaryText", "CriminalText", "TraitsText", "ExperienceText", "IQText" };
        foreach (string value in values)
        {
            StyleSingleText(cube, value, valueSize, valueColor, false);
        }
        
        // Style Major elements
        foreach (Transform child in cube)
        {
            if (child.name == "Major")
            {
                StyleSingleText(cube, child, valueSize, valueColor, false);
            }
        }
        
        Debug.Log("Text styled!");
    }
    
    void StyleSingleText(Transform parent, string childName, float size, Color color, bool bold)
    {
        Transform child = parent.Find(childName);
        if (child != null)
        {
            StyleSingleText(parent, child, size, color, bold);
        }
    }
    
    void StyleSingleText(Transform parent, Transform child, float size, Color color, bool bold)
    {
        TextMeshPro tmp3D = child.GetComponent<TextMeshPro>();
        if (tmp3D != null)
        {
            Undo.RecordObject(tmp3D, "Style Text");
            tmp3D.fontSize = size * 100;
            tmp3D.color = color;
            tmp3D.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            EditorUtility.SetDirty(tmp3D);
        }
        
        TextMeshProUGUI tmpUI = child.GetComponent<TextMeshProUGUI>();
        if (tmpUI != null)
        {
            Undo.RecordObject(tmpUI, "Style Text");
            tmpUI.fontSize = size * 100;
            tmpUI.color = color;
            tmpUI.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            EditorUtility.SetDirty(tmpUI);
        }
    }
    
    void ResetPositions()
    {
        if (targetPaper == null) return;
        
        Undo.RegisterCompleteObjectUndo(targetPaper, "Reset Paper Positions");
        
        Transform cube = targetPaper.transform.Find("Cube");
        if (cube == null) cube = targetPaper.transform;
        
        // Reset all children to origin
        foreach (Transform child in cube)
        {
            Undo.RecordObject(child, "Reset Position");
            child.localPosition = Vector3.zero;
        }
        
        Debug.Log("Positions reset!");
    }
}
#endif
