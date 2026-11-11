using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// One-click scene, prefab and project-settings fixes that code alone can't make.
/// Safe to run more than once: every step checks before changing anything and logs what it did.
/// Menu: Tools > Game > Apply Playability Fixes
/// </summary>
public static class PlayabilityFixes
{
    const string MainGamePath = "Assets/Scenes/MainGame.unity";
    const string MainMenuPath = "Assets/Scenes/Main_Menu.unity";
    const string PaperPrefabPath = "Assets/Prefabs/ApplicantPaper.prefab";
    const string PortraitsFolder = "Assets/Resources/Portraits";

    static readonly string[] RequiredTags = { "Ground" };

    // The 3D build mode's objects; build mode is now the 2D HQ tower and their scripts were removed
    static readonly string[] OldBuildObjectPaths = { "BuildingSystem", "BuildingBudget", "Canvas/BuildingPanel", "Canvas/BuildingGambleUI" };
    const string BuildModeObjectPath = "Buildingview/BuildModeCamera";
    const string DesignFontPath = "Assets/UI/Fonts/ComingSoon SDF.asset";

    [MenuItem("Tools/Game/Apply Playability Fixes")]
    public static void ApplyAll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Apply Playability Fixes", "Stop Play mode first, then run this again.", "OK");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        // "Don't Save" leaves scenes modified; saving our fixes would also save those declined edits
        foreach (string path in new[] { MainGamePath, MainMenuPath })
        {
            Scene open = SceneManager.GetSceneByPath(path);
            if (open.isLoaded && open.isDirty)
            {
                EditorUtility.DisplayDialog("Apply Playability Fixes",
                    $"{System.IO.Path.GetFileNameWithoutExtension(path)} has unsaved changes. Save or revert them, then run this again.\n\nNothing was changed.", "OK");
                return;
            }
        }

        var log = new List<string>();
        try
        {
            RunStep("Tags", 0.05f, () => AddTags(log), log);
            RunStep("MainGame scene", 0.2f, () => WithScene(MainGamePath, scene => FixMainGame(scene, log), log), log);
            RunStep("CV paper prefab", 0.45f, () => FixApplicantPaper(log), log);
            RunStep("Portrait import settings", 0.5f, () => FixPortraitSpriteModes(log), log);
            RunStep("Main menu scene", 0.55f, () => WithScene(MainMenuPath, scene => FixMainMenu(scene, log), log), log);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        AssetDatabase.SaveAssets();

        string summary = log.Count == 0
            ? "Everything was already fixed. Nothing changed."
            : string.Join("\n", log.Select(l => "• " + l));
        Debug.Log("[PlayabilityFixes]\n" + summary);
        EditorUtility.DisplayDialog("Playability fixes applied", summary.Length > 1800 ? summary.Substring(0, 1800) + "\n… (full list in the Console)" : summary, "OK");
    }

    // ------------------------------------------------------------------
    // Project settings
    // ------------------------------------------------------------------

    static void AddTags(List<string> log)
    {
        var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (assets == null || assets.Length == 0) return;
        var tagManager = new SerializedObject(assets[0]);
        var tags = tagManager.FindProperty("tags");
        var existing = new HashSet<string>();
        for (int i = 0; i < tags.arraySize; i++)
            existing.Add(tags.GetArrayElementAtIndex(i).stringValue);

        foreach (var tag in RequiredTags)
        {
            if (existing.Contains(tag)) continue;
            tags.InsertArrayElementAtIndex(tags.arraySize);
            tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = tag;
            log.Add($"Added tag \"{tag}\".");
        }
        tagManager.ApplyModifiedPropertiesWithoutUndo();
    }

    // ------------------------------------------------------------------
    // MainGame
    // ------------------------------------------------------------------

    static bool FixMainGame(Scene scene, List<string> log)
    {
        bool changed = false;

        // Old 3D build mode (before the missing-script cleanup below, which would strip the removed
        // scripts and leave empty objects). Only objects that still carry a removed script are deleted.
        foreach (string path in OldBuildObjectPaths)
        {
            var old = FindByPath<Transform>(scene, path);
            if (old == null || PrefabUtility.IsPartOfPrefabInstance(old.gameObject)) continue;
            if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(old.gameObject) == 0) continue;
            Undo.DestroyObjectImmediate(old.gameObject);
            log.Add($"MainGame: deleted the old 3D build-mode object \"{path}\" (build mode is now the 2D HQ tower).");
            changed = true;
        }

        // Components whose script no longer exists (CameraModeController, [Template] ItemButton)
        foreach (var go in AllGameObjects(scene))
        {
            // Missing scripts inside prefab instances belong to the prefab asset (e.g. an asset-pack prefab); leave them alone
            if (PrefabUtility.IsPartOfPrefabInstance(go)) continue;
            int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
            if (missing == 0) continue;
            Undo.RegisterCompleteObjectUndo(go, "Remove missing scripts");
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
            log.Add($"MainGame: removed {missing} missing-script component(s) from \"{PathOf(go)}\".");
            changed = true;
        }

        // Firebase was removed: its network-test object has nothing left on it
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.name != "NetworkTest" || PrefabUtility.IsPartOfPrefabInstance(root)) continue;
            if (root.transform.childCount > 0 || root.GetComponents<Component>().Length > 1) continue; // only a Transform left
            Undo.DestroyObjectImmediate(root);
            log.Add("MainGame: deleted the empty \"NetworkTest\" object (its Firebase test script was removed).");
            changed = true;
            break;
        }

        // HUD buttons overlapped (Skills/Stocks by 56 px, Hire/Skip by 64 px)
        changed |= SetAnchoredPosition(scene, "Canvas/SkillTreeMan", new Vector2(-90f, -26f), log);
        changed |= SetAnchoredPosition(scene, "Canvas/StockMarket", new Vector2(90f, -26f), log);
        changed |= SetAnchoredPosition(scene, "Canvas/Hire", new Vector2(-340f, -107f), log);
        changed |= SetAnchoredPosition(scene, "Canvas/Skip", new Vector2(-160f, -107f), log);
        changed |= SetAnchoredPosition(scene, "Canvas/NewBatch", new Vector2(-250f, -164f), log);

        // Skills button: open through SkillTreeUI instead of SetActive (which skipped the panel's refresh)
        var skillsButton = FindByPath<Button>(scene, "Canvas/SkillTreeMan");
        var skillTreeUI = FindInScene<SkillTreeUI>(scene);
        if (skillsButton != null && skillTreeUI != null && !HasOnlyListener(skillsButton.onClick, skillTreeUI, "OpenPanel"))
        {
            Undo.RecordObject(skillsButton, "Rewire Skills button");
            ClearPersistent(skillsButton.onClick);
            UnityEventTools.AddPersistentListener(skillsButton.onClick, skillTreeUI.OpenPanel);
            EditorUtility.SetDirty(skillsButton);
            log.Add("MainGame: Skills button now calls SkillTreeUI.OpenPanel.");
            changed = true;
        }

        // Stocks button: give the stock market a real controller and drop the empty binding
        var stocksButton = FindByPath<Button>(scene, "Canvas/StockMarket");
        var stockUI = FindInScene<StockMarketUI>(scene);
        var stockManager = FindInScene<StockMarketManager>(scene);
        if (stockUI == null && stockManager != null)
        {
            stockUI = Undo.AddComponent<StockMarketUI>(stockManager.gameObject);
            log.Add($"MainGame: added StockMarketUI to \"{PathOf(stockManager.gameObject)}\".");
            changed = true;
        }
        if (stocksButton != null && stocksButton.onClick.GetPersistentEventCount() > 0)
        {
            Undo.RecordObject(stocksButton, "Clear Stocks button binding");
            ClearPersistent(stocksButton.onClick);
            EditorUtility.SetDirty(stocksButton);
            log.Add("MainGame: removed the Stocks button's empty click binding.");
            changed = true;
        }
        if (stocksButton != null && stockUI != null)
            changed |= SetReference(stockUI, "openStockMarketButton", stocksButton, "MainGame: StockMarketUI.openStockMarketButton → Canvas/StockMarket.", log);

        // 2D HQ tower build mode: its components live beside BuildModeCamera (Q / the Build tab), so a
        // designed prefab can later be wired into their fields. Empty fields = the screen is built at runtime.
        var buildModeCamera = FindByPath<BuildModeCamera>(scene, BuildModeObjectPath);
        if (buildModeCamera != null)
        {
            var target = buildModeCamera.gameObject;
            changed |= AddComponentIfMissing<TowerBuilder>(scene, target, log);
            changed |= AddComponentIfMissing<TowerView>(scene, target, log);
            changed |= AddComponentIfMissing<BuildGambleView>(scene, target, log);

            var towerView = FindInScene<TowerView>(scene);
            var designFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(DesignFontPath);
            if (designFont != null && towerView != null)
                changed |= SetReferenceIfEmpty(towerView, "font", designFont, "MainGame: the HQ tower uses the Coming Soon font.", log);
        }

        // Autosave every 2.2 s rewrote the save file constantly
        var saveSystem = FindInScene<SaveSystem>(scene);
        if (saveSystem != null)
            changed |= SetFloat(saveSystem, "autoSaveInterval", 30f, v => v < 10f, "MainGame: SaveSystem autosave interval → 30 s.", log);

        // Crises every 15–38 s (plus ~6 s of warning/result framing each) left little time to hire
        var miniGames = FindInScene<MiniGameManager>(scene);
        if (miniGames != null)
        {
            changed |= SetFloat(miniGames, "minTimeBetweenGames", 30f, v => v < 20f, "MainGame: mini-game interval minimum → 30 s (was under 20 s).", log);
            changed |= SetFloat(miniGames, "maxTimeBetweenGames", 120f, v => v < 60f, "MainGame: mini-game interval maximum → 120 s (was under 60 s).", log);
        }

        // Risk cards every 30-111 s on top of the crises: back to the code's 60-180 s
        var events = FindInScene<EventManager>(scene);
        if (events != null)
        {
            changed |= SetFloat(events, "minTimeBetweenEvents", 60f, v => v < 45f, "MainGame: risk-card interval minimum → 60 s (was under 45 s).", log);
            changed |= SetFloat(events, "maxTimeBetweenEvents", 180f, v => v < 120f, "MainGame: risk-card interval maximum → 180 s (was under 120 s).", log);
        }

        // Leftover test values: a new company should start with no skill points
        var skillManager = FindInScene<SkillTreeManager>(scene);
        if (skillManager != null)
        {
            changed |= SetInt(skillManager, "skillPoints", 0, v => v != 0, "MainGame: starting skill points → 0 (was a test value).", log);
            changed |= SetInt(skillManager, "totalHighIQHires", 0, v => v != 0, "MainGame: starting high-IQ hire count → 0.", log);
            // One point per 7 high-IQ hires made the tree a ~20 hour grind
            changed |= SetInt(skillManager, "hiresPerSkillPoint", 3, v => v > 3, "MainGame: a skill point now takes 3 high-IQ hires (was 7).", log);
        }

        // The old HUD Canvas, once Tools > Game > Clean Up Old HUD Canvas has emptied it
        changed |= OldCanvasCleanup.RemoveLeftoverEmptyCanvas(scene, log);

        return changed;
    }

    // ------------------------------------------------------------------
    // CV paper prefab
    // ------------------------------------------------------------------

    static void FixApplicantPaper(List<string> log)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PaperPrefabPath) == null) return;

        GameObject root = PrefabUtility.LoadPrefabContents(PaperPrefabPath);
        bool changed = false;
        try
        {
            // goldenEffects pointed at the paper itself, so normal applicants hid their own paper
            var binder = root.GetComponent<ApplicantUIBinder>();
            if (binder != null)
            {
                var so = new SerializedObject(binder);
                var golden = so.FindProperty("goldenEffects");
                if (golden != null && golden.objectReferenceValue == root)
                {
                    golden.objectReferenceValue = null;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    log.Add("CV paper: cleared goldenEffects (it pointed at the paper itself).");
                    changed = true;
                }

                // Education value existed on the paper but was never bound
                var education = so.FindProperty("educationText");
                var educationObject = root.transform.Find("Cube/EducationText");
                if (education != null && education.objectReferenceValue == null && educationObject != null)
                {
                    var tmp = educationObject.GetComponent<TMP_Text>();
                    if (tmp != null && education.propertyType == SerializedPropertyType.ObjectReference)
                    {
                        education.objectReferenceValue = tmp;
                        so.ApplyModifiedPropertiesWithoutUndo();
                        log.Add("CV paper: bound educationText to Cube/EducationText.");
                        changed = true;
                    }
                }
            }

            // The label beside the major read "GPA:"
            foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text.name == "Major" && text.text.Trim() == "GPA:")
                {
                    text.text = "Major:";
                    log.Add("CV paper: the label beside the major now reads \"Major:\".");
                    changed = true;
                }
            }

            if (changed)
                PrefabUtility.SaveAsPrefabAsset(root, PaperPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>Portraits must import as a single Sprite, or Resources.Load&lt;Sprite&gt; returns nothing for them.</summary>
    static void FixPortraitSpriteModes(List<string> log)
    {
        if (!AssetDatabase.IsValidFolder(PortraitsFolder)) return;
        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { PortraitsFolder });
        var broken = new List<string>();
        for (int i = 0; i < guids.Length; i++)
        {
            if (i % 250 == 0)
                EditorUtility.DisplayProgressBar("Apply Playability Fixes", $"Checking portrait import settings ({i:N0}/{guids.Length:N0})", 0.5f + 0.05f * i / guids.Length);
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) continue;
            if (importer.textureType == TextureImporterType.Sprite && importer.spriteImportMode == SpriteImportMode.Single) continue;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.SaveAndReimport();
            broken.Add(System.IO.Path.GetFileName(path));
        }
        if (broken.Count > 0)
            log.Add($"Portraits: {broken.Count} imported without a usable sprite, fixed ({string.Join(", ", broken.Take(5))}{(broken.Count > 5 ? ", …" : "")}).");
    }

    // ------------------------------------------------------------------
    // Main menu
    // ------------------------------------------------------------------

    static bool FixMainMenu(Scene scene, List<string> log)
    {
        bool changed = false;
        foreach (var scaler in AllComponents<CanvasScaler>(scene))
        {
            if (scaler.GetComponent<Canvas>() == null || !scaler.GetComponent<Canvas>().isRootCanvas) continue;
            if (scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize) continue;
            Undo.RecordObject(scaler, "Scale menu with screen size");
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            EditorUtility.SetDirty(scaler);
            log.Add($"Main_Menu: \"{PathOf(scaler.gameObject)}\" now scales with screen size (1920×1080).");
            changed = true;
        }
        return changed;
    }

    // ------------------------------------------------------------------
    // Optional: portrait import settings (slow, run on its own)
    // ------------------------------------------------------------------

    [MenuItem("Tools/Game/Optimize Portrait Imports (512 px)")]
    public static void OptimizePortraitImports()
    {
        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { PortraitsFolder });
        if (guids.Length == 0)
        {
            EditorUtility.DisplayDialog("Optimize Portrait Imports", $"No textures found under {PortraitsFolder}.", "OK");
            return;
        }
        if (!EditorUtility.DisplayDialog("Optimize Portrait Imports",
            $"This reimports {guids.Length:N0} portraits at a maximum of 512 px with mipmaps. " +
            "It makes builds much smaller and uses far less memory, but the reimport can take 10 minutes or more.",
            "Reimport", "Cancel"))
            return;

        int changedCount = 0;
        AssetDatabase.StartAssetEditing();
        try
        {
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (i % 50 == 0 && EditorUtility.DisplayCancelableProgressBar("Optimize Portrait Imports", path, (float)i / guids.Length))
                    break;
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;
                if (importer.maxTextureSize == 512 && importer.mipmapEnabled) continue;
                importer.maxTextureSize = 512;
                importer.mipmapEnabled = true;
                importer.SaveAndReimport();
                changedCount++;
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            EditorUtility.ClearProgressBar();
        }
        Debug.Log($"[PlayabilityFixes] Reimported {changedCount:N0} portraits at 512 px with mipmaps.");
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    static void Progress(string step, float t)
    {
        EditorUtility.DisplayProgressBar("Apply Playability Fixes", step, t);
    }

    /// <summary>Run one step; a failure is logged and reported without skipping the remaining steps.</summary>
    static void RunStep(string name, float progress, System.Action step, List<string> log)
    {
        Progress(name, progress);
        try
        {
            step();
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            log.Add($"FAILED: {name} ({e.GetType().Name}: {e.Message}). The other steps still ran; details are in the Console.");
        }
    }

    /// <summary>Run a fix on a scene, opening it additively if needed, and save it when anything changed.</summary>
    static void WithScene(string path, System.Func<Scene, bool> fix, List<string> log)
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
        {
            log.Add($"Skipped {path}: scene not found.");
            return;
        }

        Scene scene = SceneManager.GetSceneByPath(path);
        bool openedHere = false;
        if (!scene.isLoaded)
        {
            scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            openedHere = true;
        }

        try
        {
            if (fix(scene))
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
        }
        finally
        {
            if (openedHere)
                EditorSceneManager.CloseScene(scene, true);
        }
    }

    static IEnumerable<GameObject> AllGameObjects(Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                yield return t.gameObject;
    }

    static IEnumerable<T> AllComponents<T>(Scene scene) where T : Component
    {
        foreach (var root in scene.GetRootGameObjects())
            foreach (var c in root.GetComponentsInChildren<T>(true))
                yield return c;
    }

    static T FindInScene<T>(Scene scene) where T : Component
    {
        return AllComponents<T>(scene).FirstOrDefault();
    }

    /// <summary>Find a component by hierarchy path like "Canvas/SkillTreeMan" (inactive objects included).</summary>
    static T FindByPath<T>(Scene scene, string path) where T : Component
    {
        string[] parts = path.Split('/');
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.name != parts[0]) continue;
            Transform t = root.transform;
            for (int i = 1; i < parts.Length && t != null; i++)
                t = t.Find(parts[i]);
            if (t != null)
            {
                var c = t.GetComponent<T>();
                if (c != null) return c;
            }
        }
        return null;
    }

    static bool SetAnchoredPosition(Scene scene, string path, Vector2 position, List<string> log)
    {
        var rt = FindByPath<RectTransform>(scene, path);
        if (rt == null || Vector2.Distance(rt.anchoredPosition, position) < 0.5f) return false;
        Undo.RecordObject(rt, "Move HUD element");
        rt.anchoredPosition = position;
        EditorUtility.SetDirty(rt);
        log.Add($"MainGame: moved {path} to ({position.x:0}, {position.y:0}) so HUD buttons no longer overlap.");
        return true;
    }

    static void ClearPersistent(UnityEventBase evt)
    {
        for (int i = evt.GetPersistentEventCount() - 1; i >= 0; i--)
            UnityEventTools.RemovePersistentListener(evt, i);
    }

    static bool HasOnlyListener(UnityEventBase evt, Object target, string method)
    {
        return evt.GetPersistentEventCount() == 1
            && evt.GetPersistentTarget(0) == target
            && evt.GetPersistentMethodName(0) == method;
    }

    static bool SetReference(Object target, string property, Object value, string message, List<string> log)
    {
        var so = new SerializedObject(target);
        var p = so.FindProperty(property);
        if (p == null || p.propertyType != SerializedPropertyType.ObjectReference || p.objectReferenceValue == value) return false;
        p.objectReferenceValue = value;
        so.ApplyModifiedProperties();
        log.Add(message);
        return true;
    }

    static bool SetReferenceIfEmpty(Object target, string property, Object value, string message, List<string> log)
    {
        var so = new SerializedObject(target);
        var p = so.FindProperty(property);
        if (p == null || p.propertyType != SerializedPropertyType.ObjectReference || p.objectReferenceValue != null) return false;
        p.objectReferenceValue = value;
        so.ApplyModifiedProperties();
        log.Add(message);
        return true;
    }

    /// <summary>Add T to the object unless the scene already has one somewhere.</summary>
    static bool AddComponentIfMissing<T>(Scene scene, GameObject target, List<string> log) where T : Component
    {
        if (FindInScene<T>(scene) != null) return false;
        Undo.AddComponent<T>(target);
        log.Add($"MainGame: added {typeof(T).Name} to \"{PathOf(target)}\".");
        return true;
    }

    static bool SetFloat(Object target, string property, float value, System.Func<float, bool> shouldChange, string message, List<string> log)
    {
        var so = new SerializedObject(target);
        var p = so.FindProperty(property);
        if (p == null || p.propertyType != SerializedPropertyType.Float || !shouldChange(p.floatValue)) return false;
        p.floatValue = value;
        so.ApplyModifiedProperties();
        log.Add(message);
        return true;
    }

    static bool SetInt(Object target, string property, int value, System.Func<int, bool> shouldChange, string message, List<string> log)
    {
        var so = new SerializedObject(target);
        var p = so.FindProperty(property);
        if (p == null || p.propertyType != SerializedPropertyType.Integer || !shouldChange(p.intValue)) return false;
        p.intValue = value;
        so.ApplyModifiedProperties();
        log.Add(message);
        return true;
    }

    static string PathOf(GameObject go)
    {
        var parts = new List<string>();
        for (Transform t = go.transform; t != null; t = t.parent)
            parts.Add(t.name);
        parts.Reverse();
        return string.Join("/", parts);
    }
}
