using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Removes what's left of the old HUD on MainGame's root "Canvas": the old mini-game panels, blackjack table, upgrade,
/// name, stats, hiring-task, skill tree and risk-card panels, the old HUD buttons, the 3D build-mode leftovers and the
/// debug buttons. The new UI (OfficeHUD, CRT crisis panels, UpgradeFlow, SkillBoard, risk cards…) replaced all of them.
/// Safety: every component in the scene is scanned first; an object that anything outside the deleted set still
/// references is kept (and listed). The debug F9 "unstick" key moves to an invisible "DebugTools (dev only)" object.
/// If the Canvas ends up empty and unreferenced, it goes too. Undo (Ctrl+Z) works until the editor closes.
/// Menu: Tools > Game > Clean Up Old HUD Canvas
/// </summary>
public static class OldCanvasCleanup
{
    const string MainGamePath = "Assets/Scenes/MainGame.unity";
    const string CanvasName = "Canvas";
    const string DebugHolderName = "DebugTools (dev only)";

    static readonly string[] Targets =
    {
        // old crisis mini-game panels (MiniGameManager uses the CRT panels now)
        "ArrowTimingPanel", "ButtonMashPanel", "CatchFallingPanel", "CoinFlipPanel", "ColorMatchPanel", "HigherLowerPanel",
        "KeypadCodePanel", "MathBlitzPanel", "NumberSequencePanel", "OddOneOutPanel", "PatternMatchPanel", "QuickTapPanel",
        "RapidKeysPanel", "SafeCrackerPanel", "SequenceMemoryPanel", "SimonSaysPanel", "SpeedCountPanel", "StopTheBarPanel",
        "WhackAMolePanel", "WordTypePanel",
        // old panels replaced by the designed ones
        "Blackjack", "ChoiceSelection", "Results", "CompanyNamePanel", "CompanyStatsPanel", "HiringTaskPanel", "Event Panel",
        "SkillTreePanel",
        // old HUD buttons (the OfficeHUD has its own)
        "Hire", "NewBatch", "Skip", "SkillTreeMan", "StockMarket",
        // the 3D build mode (its scripts were deleted)
        "BuildingGambleUI", "BuildingPanel",
        // debug buttons (their F9 unstick key moves to an invisible object)
        "Debug", "Debug (1)", "Debug (2)", "Debug (3)",
    };

    struct Ref
    {
        public GameObject from;
        public Component component;
        public string path;
        public string what;
    }

    [MenuItem("Tools/Game/Clean Up Old HUD Canvas")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Clean Up Old HUD Canvas", "Stop Play mode first, then run this again.", "OK");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        Scene scene = SceneManager.GetSceneByPath(MainGamePath);
        bool openedHere = false;
        if (!scene.isLoaded)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MainGamePath) == null)
            {
                EditorUtility.DisplayDialog("Clean Up Old HUD Canvas", MainGamePath + " wasn't found.", "OK");
                return;
            }
            scene = EditorSceneManager.OpenScene(MainGamePath, OpenSceneMode.Additive);
            openedHere = true;
        }
        else if (scene.isDirty)
        {
            EditorUtility.DisplayDialog("Clean Up Old HUD Canvas", "MainGame has unsaved changes. Save or revert them, then run this again.\n\nNothing was changed.", "OK");
            return;
        }

        try
        {
            Clean(scene);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            if (openedHere) EditorSceneManager.CloseScene(scene, true);
        }
    }

    static void Clean(Scene scene)
    {
        GameObject canvas = scene.GetRootGameObjects().FirstOrDefault(g => g.name == CanvasName && g.GetComponent<Canvas>() != null);
        if (canvas == null)
        {
            EditorUtility.DisplayDialog("Clean Up Old HUD Canvas", "There is no root \"Canvas\" in MainGame: nothing to clean.", "OK");
            return;
        }

        // What we'd delete: the listed direct children (not prefab instances)
        var candidates = new List<GameObject>();
        foreach (Transform child in canvas.transform)
        {
            if (Targets.Contains(child.name) && !PrefabUtility.IsPartOfPrefabInstance(child.gameObject))
                candidates.Add(child.gameObject);
        }
        EditorUtility.DisplayProgressBar("Clean Up Old HUD Canvas", "Checking what still uses the old Canvas…", 0.3f);
        Dictionary<GameObject, List<Ref>> refs = ReferencesInto(scene, canvas);

        if (candidates.Count == 0)
        {
            EditorUtility.ClearProgressBar();
            // A second run: only the (now empty) Canvas itself may be left
            if (canvas.transform.childCount == 0)
            {
                var emptyLog = new List<string>();
                Undo.SetCurrentGroupName("Remove Empty Old Canvas");
                int emptyGroup = Undo.GetCurrentGroup();
                if (RemoveEmptyCanvas(canvas, refs, new HashSet<GameObject> { canvas }, emptyLog))
                {
                    Undo.CollapseUndoOperations(emptyGroup);
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                    emptyLog.Add("MainGame saved.");
                }
                string emptySummary = string.Join("\n", emptyLog);
                Debug.Log("[OldCanvasCleanup]\n" + emptySummary);
                EditorUtility.DisplayDialog("Clean Up Old HUD Canvas", emptySummary, "OK");
                return;
            }
            EditorUtility.DisplayDialog("Clean Up Old HUD Canvas", "The old HUD objects are already gone.", "OK");
            return;
        }

        // Keep anything that an object outside the deleted set still references (repeat until stable: keeping one makes
        // its own references count)
        var kept = new Dictionary<GameObject, string>();
        bool changed = true;
        while (changed)
        {
            changed = false;
            var deleting = new HashSet<GameObject>(candidates.Where(c => !kept.ContainsKey(c)).SelectMany(Subtree));
            foreach (GameObject candidate in candidates)
            {
                if (kept.ContainsKey(candidate)) continue;
                string reason = ExternalReference(refs, candidate, deleting);
                if (reason == null) continue;
                kept[candidate] = reason;
                changed = true;
            }
        }
        List<GameObject> toDelete = candidates.Where(c => !kept.ContainsKey(c)).ToList();
        EditorUtility.ClearProgressBar();
        if (toDelete.Count == 0)
        {
            EditorUtility.DisplayDialog("Clean Up Old HUD Canvas", "Everything left on the Canvas is still in use:\n\n" +
                string.Join("\n", kept.Select(k => "• " + k.Key.name + " (" + k.Value + ")")), "OK");
            return;
        }

        string list = string.Join(", ", toDelete.Select(g => g.name));
        string keptText = kept.Count == 0 ? "" : "\n\nKept because something still uses them:\n" +
            string.Join("\n", kept.Select(k => "• " + k.Key.name + " (" + k.Value + ")"));
        if (!EditorUtility.DisplayDialog("Clean Up Old HUD Canvas",
                "Delete " + toDelete.Count + " old HUD objects from MainGame's Canvas?\n\n" + list + keptText +
                "\n\nThe scene is saved afterwards; Ctrl+Z undoes it until you close the editor.", "Delete", "Cancel"))
            return;

        var log = new List<string>();
        Undo.SetCurrentGroupName("Clean Up Old HUD Canvas");
        int group = Undo.GetCurrentGroup();

        // Keep the F9 unstick key (editor / development builds) on an invisible object
        if (toDelete.Any(g => g.GetComponent<DebugTools>() != null) &&
            !scene.GetRootGameObjects().Any(g => g.name == DebugHolderName))
        {
            var holder = new GameObject(DebugHolderName);
            SceneManager.MoveGameObjectToScene(holder, scene);
            Undo.RegisterCreatedObjectUndo(holder, "Keep DebugTools");
            Undo.AddComponent<DebugTools>(holder);
            log.Add("Added \"" + DebugHolderName + "\" (DebugTools: the F9 unstick key, editor/development builds only).");
        }

        var deleted = new HashSet<GameObject>(toDelete.SelectMany(Subtree));
        foreach (GameObject g in toDelete)
        {
            string name = g.name;
            Undo.DestroyObjectImmediate(g);
            log.Add("Deleted Canvas/" + name + ".");
        }

        // The Canvas itself, once nothing is left on it and nothing outside points at it
        if (canvas.transform.childCount == 0)
        {
            deleted.Add(canvas);
            RemoveEmptyCanvas(canvas, refs, deleted, log);
        }
        foreach (var k in kept)
            log.Add("Kept Canvas/" + k.Key.name + ": " + k.Value + ".");

        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        log.Add("MainGame saved.");

        string summary = string.Join("\n", log);
        Debug.Log("[OldCanvasCleanup]\n" + summary);
        EditorUtility.DisplayDialog("Old HUD Canvas cleaned up", summary.Length > 1800 ? summary.Substring(0, 1800) + "\n… (full list in the Console)" : summary, "OK");
    }

    /// <summary>
    /// For Tools > Game > Apply Playability Fixes: if MainGame's old root "Canvas" is still there but empty, remove it
    /// (re-pointing MiniGameManager's fallback canvas). Does nothing while it still has objects on it. Returns true if the
    /// scene changed.
    /// </summary>
    public static bool RemoveLeftoverEmptyCanvas(Scene scene, List<string> log)
    {
        GameObject canvas = scene.GetRootGameObjects().FirstOrDefault(g => g.name == CanvasName && g.GetComponent<Canvas>() != null);
        if (canvas == null || canvas.transform.childCount > 0) return false;
        var lines = new List<string>();
        bool removed = RemoveEmptyCanvas(canvas, ReferencesInto(scene, canvas), new HashSet<GameObject> { canvas }, lines);
        foreach (string line in lines) log.Add("MainGame: " + line);
        return removed;
    }

    /// <summary>
    /// Delete the empty old Canvas when nothing outside <paramref name="deleted"/> points at it. The one known holder is
    /// MiniGameManager.mainCanvas, only a fallback for finding panels by name: it's re-pointed to the canvas of the
    /// crisis panels MiniGameManager really uses (or cleared), then the Canvas goes. Returns true if anything changed.
    /// </summary>
    static bool RemoveEmptyCanvas(GameObject canvas, Dictionary<GameObject, List<Ref>> refs, HashSet<GameObject> deleted, List<string> log)
    {
        var holders = new List<Ref>();
        if (refs.TryGetValue(canvas, out List<Ref> list))
            holders.AddRange(list.Where(r => r.from != null && !deleted.Contains(r.from)));

        if (holders.Any(r => !(r.component is MiniGameManager) || r.path != "mainCanvas"))
        {
            log.Add("Kept the empty old \"Canvas\": " + holders.First(r => !(r.component is MiniGameManager) || r.path != "mainCanvas").what + ".");
            return false;
        }

        foreach (Ref r in holders)
        {
            var manager = (MiniGameManager)r.component;
            Canvas replacement = CrisisPanelsCanvas(manager, canvas);
            var so = new SerializedObject(manager);
            so.FindProperty("mainCanvas").objectReferenceValue = replacement;
            so.ApplyModifiedProperties();   // recorded for Undo
            log.Add("MiniGameManager.mainCanvas → " + (replacement != null ? "\"" + replacement.name + "\" (the crisis panels' canvas)" : "none (it finds panels on any canvas)") + ".");
        }

        Undo.DestroyObjectImmediate(canvas);
        log.Add("Deleted the now empty old \"Canvas\".");
        return true;
    }

    /// <summary>The root canvas of the first crisis panel MiniGameManager is wired to (not the old Canvas), or null.</summary>
    static Canvas CrisisPanelsCanvas(MiniGameManager manager, GameObject oldCanvas)
    {
        var so = new SerializedObject(manager);
        SerializedProperty p = so.GetIterator();
        while (p.Next(true))
        {
            if (p.propertyType != SerializedPropertyType.ObjectReference || !p.propertyPath.EndsWith("Panel")) continue;
            var panel = p.objectReferenceValue as GameObject;
            if (panel == null) continue;
            Canvas c = panel.GetComponentInParent<Canvas>(true);
            if (c != null) c = c.rootCanvas;
            if (c != null && c.gameObject != oldCanvas) return c;
        }
        return null;
    }

    static IEnumerable<GameObject> Subtree(GameObject root)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            yield return t.gameObject;
    }

    /// <summary>One pass over every component in the scene: the references that point at objects under <paramref name="under"/>.</summary>
    static Dictionary<GameObject, List<Ref>> ReferencesInto(Scene scene, GameObject under)
    {
        var inCanvas = new HashSet<GameObject>(Subtree(under));
        var map = new Dictionary<GameObject, List<Ref>>();
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Component component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null || component is Transform) continue;   // null = missing script
                var so = new SerializedObject(component);
                SerializedProperty p = so.GetIterator();
                while (p.Next(true))
                {
                    if (p.propertyType != SerializedPropertyType.ObjectReference) continue;
                    Object value = p.objectReferenceValue;
                    if (value == null) continue;
                    GameObject referenced = value as GameObject ?? (value as Component)?.gameObject;
                    if (referenced == null || referenced == component.gameObject || !inCanvas.Contains(referenced)) continue;
                    if (!map.TryGetValue(referenced, out List<Ref> list)) map[referenced] = list = new List<Ref>();
                    list.Add(new Ref
                    {
                        from = component.gameObject,
                        component = component,
                        path = p.propertyPath,
                        what = component.GetType().Name + "." + p.propertyPath + " on " + component.gameObject.name
                    });
                }
            }
        }
        return map;
    }

    /// <summary>
    /// The first reference into <paramref name="target"/>'s subtree from a component outside that subtree and outside
    /// <paramref name="deleting"/> ("OfficeHud.someField on OfficeHUD"), or null when nothing else uses it.
    /// </summary>
    static string ExternalReference(Dictionary<GameObject, List<Ref>> refs, GameObject target, HashSet<GameObject> deleting)
    {
        var inside = new HashSet<GameObject>(Subtree(target));
        foreach (GameObject g in inside)
        {
            if (!refs.TryGetValue(g, out List<Ref> list)) continue;
            foreach (Ref r in list)
                if (r.from != null && !inside.Contains(r.from) && !deleting.Contains(r.from)) return r.what;
        }
        return null;
    }
}
