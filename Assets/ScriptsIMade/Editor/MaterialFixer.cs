#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using System.Collections.Generic;

/// <summary>
/// Scans scene for materials with HDRP compatibility issues and helps fix them.
/// </summary>
public class MaterialFixer : EditorWindow
{
    private Vector2 scrollPosition;
    private List<MaterialIssue> issues = new List<MaterialIssue>();
    private bool hasScanned = false;
    
    private class MaterialIssue
    {
        public Material material;
        public GameObject gameObject;
        public string issue;
        public bool isSelected;
    }
    
    [MenuItem("Tools/Material Fixer (HDRP)")]
    public static void ShowWindow()
    {
        GetWindow<MaterialFixer>("Material Fixer");
    }
    
    void OnGUI()
    {
        GUILayout.Label("HDRP Material Fixer", EditorStyles.boldLabel);
        EditorGUILayout.Space();
        
        EditorGUILayout.HelpBox(
            "This tool scans your scene for materials that may cause issues with HDRP:\n" +
            "• Non-HDRP shaders\n" +
            "• Missing materials\n" +
            "• Standard/Legacy shaders",
            MessageType.Info);
        
        EditorGUILayout.Space();
        
        if (GUILayout.Button("Scan Scene for Material Issues", GUILayout.Height(35)))
        {
            ScanScene();
        }
        
        EditorGUILayout.Space();
        
        if (hasScanned)
        {
            if (issues.Count == 0)
            {
                EditorGUILayout.HelpBox("No material issues found! All materials appear to be HDRP compatible.", MessageType.Info);
            }
            else
            {
                GUILayout.Label($"Found {issues.Count} potential issues:", EditorStyles.boldLabel);
                
                EditorGUILayout.Space();
                
                // Bulk actions
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Select All"))
                {
                    foreach (var issue in issues) issue.isSelected = true;
                }
                if (GUILayout.Button("Deselect All"))
                {
                    foreach (var issue in issues) issue.isSelected = false;
                }
                EditorGUILayout.EndHorizontal();
                
                if (GUILayout.Button("Fix Selected Materials (Convert to HDRP/Lit)", GUILayout.Height(30)))
                {
                    FixSelectedMaterials();
                }
                
                EditorGUILayout.Space();
                
                // List of issues
                scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(300));
                
                foreach (var issue in issues)
                {
                    EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                    
                    issue.isSelected = EditorGUILayout.Toggle(issue.isSelected, GUILayout.Width(20));
                    
                    EditorGUILayout.BeginVertical();
                    
                    // Material name
                    string matName = issue.material != null ? issue.material.name : "NULL";
                    EditorGUILayout.LabelField(matName, EditorStyles.boldLabel);
                    
                    // Issue description
                    EditorGUILayout.LabelField(issue.issue, EditorStyles.wordWrappedMiniLabel);
                    
                    // GameObject reference
                    if (issue.gameObject != null)
                    {
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField("On: " + issue.gameObject.name, GUILayout.Width(200));
                        if (GUILayout.Button("Select", GUILayout.Width(60)))
                        {
                            Selection.activeGameObject = issue.gameObject;
                            EditorGUIUtility.PingObject(issue.gameObject);
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                    
                    EditorGUILayout.EndVertical();
                    
                    // Individual fix button
                    if (issue.material != null)
                    {
                        if (GUILayout.Button("Fix", GUILayout.Width(50), GUILayout.Height(40)))
                        {
                            FixMaterial(issue.material);
                            ScanScene(); // Rescan
                        }
                    }
                    
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.Space(2);
                }
                
                EditorGUILayout.EndScrollView();
            }
        }
        
        EditorGUILayout.Space();
        
        GUILayout.Label("Manual Fixes:", EditorStyles.boldLabel);
        
        if (GUILayout.Button("Convert ALL Project Materials to HDRP"))
        {
            if (EditorUtility.DisplayDialog("Convert All Materials",
                "This will convert ALL materials in your project to HDRP.\n\nThis may take a while and cannot be undone easily.\n\nContinue?",
                "Yes, Convert All", "Cancel"))
            {
                EditorApplication.ExecuteMenuItem("Edit/Rendering/Materials/Convert All Built-in Materials to HDRP");
            }
        }
        
        if (GUILayout.Button("Open Render Pipeline Converter"))
        {
            EditorApplication.ExecuteMenuItem("Window/Rendering/Render Pipeline Converter");
        }
    }
    
    void ScanScene()
    {
        issues.Clear();
        hasScanned = true;
        
        // Find all renderers in scene
        Renderer[] renderers = FindObjectsOfType<Renderer>(true);
        HashSet<Material> checkedMaterials = new HashSet<Material>();
        
        foreach (var renderer in renderers)
        {
            Material[] mats = renderer.sharedMaterials;
            
            for (int i = 0; i < mats.Length; i++)
            {
                Material mat = mats[i];
                
                // Skip if already checked
                if (mat != null && checkedMaterials.Contains(mat))
                    continue;
                
                if (mat != null)
                    checkedMaterials.Add(mat);
                
                string issue = CheckMaterial(mat);
                
                if (!string.IsNullOrEmpty(issue))
                {
                    issues.Add(new MaterialIssue
                    {
                        material = mat,
                        gameObject = renderer.gameObject,
                        issue = issue,
                        isSelected = true
                    });
                }
            }
        }
        
        Debug.Log($"[MaterialFixer] Scanned {renderers.Length} renderers, found {issues.Count} material issues");
    }
    
    string CheckMaterial(Material mat)
    {
        if (mat == null)
        {
            return "Missing/Null material";
        }
        
        if (mat.shader == null)
        {
            return "Missing shader";
        }
        
        string shaderName = mat.shader.name;
        
        // Check for obviously non-HDRP shaders
        if (shaderName.StartsWith("Standard"))
        {
            return $"Standard shader (not HDRP): {shaderName}";
        }
        
        if (shaderName.StartsWith("Legacy"))
        {
            return $"Legacy shader: {shaderName}";
        }
        
        if (shaderName.StartsWith("Mobile/"))
        {
            return $"Mobile shader (not HDRP): {shaderName}";
        }
        
        if (shaderName.StartsWith("Particles/"))
        {
            return $"Built-in Particles shader: {shaderName}";
        }
        
        if (shaderName.StartsWith("Unlit/") && !shaderName.Contains("HDRP"))
        {
            return $"Built-in Unlit shader: {shaderName}";
        }
        
        if (shaderName.StartsWith("UI/") && !shaderName.Contains("HDRP"))
        {
            // UI shaders are usually fine, skip
            return null;
        }
        
        if (shaderName == "Hidden/InternalErrorShader")
        {
            return "Error shader (missing/broken shader)";
        }
        
        // Check for shaders that might work but aren't HDRP native
        if (!shaderName.Contains("HDRP") && !shaderName.Contains("HD Render") && 
            !shaderName.StartsWith("Shader Graphs/") && !shaderName.StartsWith("UI/"))
        {
            // Could be a custom shader that works, but flag it for review
            if (shaderName.StartsWith("Custom/") || shaderName.StartsWith("Sprites/"))
            {
                return $"Non-HDRP shader (may need conversion): {shaderName}";
            }
        }
        
        return null; // No issue
    }
    
    void FixMaterial(Material mat)
    {
        if (mat == null) return;
        
        Undo.RecordObject(mat, "Convert Material to HDRP");
        
        // Store some basic properties
        Color mainColor = Color.white;
        Texture mainTex = null;
        
        if (mat.HasProperty("_Color"))
            mainColor = mat.GetColor("_Color");
        else if (mat.HasProperty("_BaseColor"))
            mainColor = mat.GetColor("_BaseColor");
        
        if (mat.HasProperty("_MainTex"))
            mainTex = mat.GetTexture("_MainTex");
        else if (mat.HasProperty("_BaseMap"))
            mainTex = mat.GetTexture("_BaseMap");
        
        // Find HDRP/Lit shader
        Shader hdrpLit = Shader.Find("HDRP/Lit");
        if (hdrpLit == null)
        {
            Debug.LogError("[MaterialFixer] Could not find HDRP/Lit shader!");
            return;
        }
        
        // Convert
        mat.shader = hdrpLit;
        
        // Restore basic properties
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", mainColor);
        
        if (mainTex != null && mat.HasProperty("_BaseColorMap"))
            mat.SetTexture("_BaseColorMap", mainTex);
        
        EditorUtility.SetDirty(mat);
        
        Debug.Log($"[MaterialFixer] Converted '{mat.name}' to HDRP/Lit");
    }
    
    void FixSelectedMaterials()
    {
        int fixed_count = 0;
        
        foreach (var issue in issues)
        {
            if (issue.isSelected && issue.material != null)
            {
                FixMaterial(issue.material);
                fixed_count++;
            }
        }
        
        AssetDatabase.SaveAssets();
        
        Debug.Log($"[MaterialFixer] Fixed {fixed_count} materials");
        
        // Rescan
        ScanScene();
    }
}
#endif
