using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// The office sofa in MainGame was a debug rig for office upgrades: a hover highlight, the "Upgrade" luggage tag, a
/// blackjack or dice upgrade on click, and four sofa models, one per level. The owner asked (2026-11-16) to keep it as
/// plain furniture: this keeps the model its level 0 shows, switches the other level models off and removes its
/// OfficeItemInteractable, so nothing happens on hover or click. Every system that looks for office items finds none
/// and carries on (saves, the panel lock, the daily bonus, the cursor).
/// Run: Tools > Game > Office > Make The Sofa Plain Furniture, or with Unity closed:
/// Unity.exe -batchmode -projectPath &lt;project&gt; -executeMethod OfficeSofaCleanup.RunBatch -logFile &lt;file&gt;
/// </summary>
public static class OfficeSofaCleanup
{
    const string ScenePath = "Assets/Scenes/MainGame.unity";

    [MenuItem("Tools/Game/Office/Make The Sofa Plain Furniture")]
    public static void RunFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Debug.Log("[Office] " + Run());
    }

    public static void RunBatch()
    {
        int code = 0;
        try
        {
            Debug.Log("[Office] " + Run());
        }
        catch (System.Exception e)
        {
            Debug.LogError("[Office] Sofa cleanup failed: " + e);
            code = 1;
        }
        EditorApplication.Exit(code);
    }

    static string Run()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var items = new List<OfficeItemInteractable>();
        foreach (GameObject root in scene.GetRootGameObjects())
            items.AddRange(root.GetComponentsInChildren<OfficeItemInteractable>(true));
        if (items.Count == 0) return "No OfficeItemInteractable in " + ScenePath + ": nothing to do.";

        var log = new StringBuilder();
        foreach (OfficeItemInteractable item in items)
        {
            int level = item.itemData != null ? item.itemData.currentLevel : 0;
            GameObject keep = VisualForLevel(item.levelVisuals, level);
            int switchedOff = 0;
            if (item.levelVisuals != null)
            {
                foreach (GameObject visual in item.levelVisuals)
                {
                    if (visual == null) continue;
                    bool show = keep == null || visual == keep;
                    if (visual.activeSelf != show) visual.SetActive(show);
                    if (!show) switchedOff++;
                }
            }
            foreach (OfficeItemColliderHelper helper in item.GetComponentsInChildren<OfficeItemColliderHelper>(true))
                Object.DestroyImmediate(helper);

            string path = PathOf(item.transform);
            Object.DestroyImmediate(item);
            log.Append(path + ": removed its upgrade (OfficeItemInteractable); kept the level-" + level + " model '" +
                       (keep != null ? keep.name : "(none assigned)") + "', switched off " + switchedOff + " other level model(s). ");
        }

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new System.Exception("could not save " + ScenePath);
        return log + "Saved " + ScenePath + ".";
    }

    /// <summary>The same choice OfficeItemInteractable makes: the highest model at or below the level, else the lowest above.</summary>
    static GameObject VisualForLevel(GameObject[] visuals, int level)
    {
        if (visuals == null || visuals.Length == 0) return null;
        for (int i = Mathf.Min(level, visuals.Length - 1); i >= 0; i--)
            if (visuals[i] != null) return visuals[i];
        for (int i = Mathf.Max(0, level + 1); i < visuals.Length; i++)
            if (visuals[i] != null) return visuals[i];
        return null;
    }

    static string PathOf(Transform t)
    {
        string path = t.name;
        for (Transform p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
        return path;
    }
}
