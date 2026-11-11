using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Puts the HDRP render pipeline back when it goes missing: without it Unity falls back to the built-in renderer and
/// every HDRP material draws magenta. An editor tool that renders UI previews in batch mode clears
/// GraphicsSettings.defaultRenderPipeline and the quality level's pipeline while it works, and that empty state got
/// saved once. Only empty slots are filled (a pipeline that is set is never changed); runs after every script reload
/// in the editor (not in batch mode) and from Tools > Game > Fix Render Pipeline (Purple Materials).
/// </summary>
[InitializeOnLoad]
static class RenderPipelineGuard
{
    // What the project had (from the committed ProjectSettings)
    const string DefaultPipelinePath = "Assets/Settings/HDRPDefaultResources/HDRenderPipelineAsset.asset";

    static readonly Dictionary<string, string> QualityLevelPipelines = new Dictionary<string, string>
    {
        { "High Fidelity", "Assets/Settings/HDRP High Fidelity.asset" },
        { "Balanced", "Assets/Settings/HDRP Balanced.asset" },
        { "Performant", "Assets/Settings/HDRP Performant.asset" },
    };

    static RenderPipelineGuard()
    {
        // Batch-mode tools clear the pipeline on purpose while they render; leave them alone
        if (Application.isBatchMode) return;
        EditorApplication.delayCall += () => Restore(false);
    }

    [MenuItem("Tools/Game/Fix Render Pipeline (Purple Materials)")]
    static void RestoreFromMenu()
    {
        Restore(true);
    }

    static void Restore(bool fromMenu)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode && !fromMenu) return;

        var log = new List<string>();

        if (GraphicsSettings.defaultRenderPipeline == null)
        {
            var asset = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(DefaultPipelinePath);
            if (asset != null)
            {
                GraphicsSettings.defaultRenderPipeline = asset;
                log.Add("Graphics > Default Render Pipeline → " + DefaultPipelinePath);
            }
            else
            {
                log.Add("Couldn't find " + DefaultPipelinePath + " to use as the default render pipeline.");
            }
        }

        // Quality levels: the active one through the API (switches the pipeline right away), the others in the asset
        int current = QualitySettings.GetQualityLevel();
        string[] names = QualitySettings.names;
        if (current >= 0 && current < names.Length && QualitySettings.renderPipeline == null
            && QualityLevelPipelines.TryGetValue(names[current], out string currentPath))
        {
            var asset = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(currentPath);
            if (asset != null)
            {
                QualitySettings.renderPipeline = asset;
                log.Add("Quality \"" + names[current] + "\" > Render Pipeline Asset → " + currentPath);
            }
        }

        Object qualityObject = QualitySettings.GetQualitySettings();
        if (qualityObject != null)
        {
            var so = new SerializedObject(qualityObject);
            SerializedProperty levels = so.FindProperty("m_QualitySettings");
            bool changed = false;
            for (int i = 0; levels != null && i < levels.arraySize; i++)
            {
                if (i == current) continue;
                SerializedProperty level = levels.GetArrayElementAtIndex(i);
                SerializedProperty pipeline = level.FindPropertyRelative("customRenderPipeline");
                string levelName = level.FindPropertyRelative("name")?.stringValue;
                if (pipeline == null || pipeline.objectReferenceValue != null || levelName == null) continue;
                if (!QualityLevelPipelines.TryGetValue(levelName, out string path)) continue;
                var asset = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(path);
                if (asset == null) continue;
                pipeline.objectReferenceValue = asset;
                changed = true;
                log.Add("Quality \"" + levelName + "\" > Render Pipeline Asset → " + path);
            }
            if (changed) so.ApplyModifiedPropertiesWithoutUndo();
        }

        if (log.Count > 0)
        {
            // Write the settings to disk now, so the fix survives a restart
            Object graphicsObject = GraphicsSettings.GetGraphicsSettings();
            if (graphicsObject != null) EditorUtility.SetDirty(graphicsObject);
            if (qualityObject != null) EditorUtility.SetDirty(qualityObject);
            AssetDatabase.SaveAssets();
            Debug.Log("[RenderPipelineGuard] The HDRP render pipeline was missing (materials drew magenta). Restored:\n• " + string.Join("\n• ", log));
        }
        else if (fromMenu)
        {
            Debug.Log("[RenderPipelineGuard] The render pipeline is already set: nothing to fix.");
        }
    }
}
