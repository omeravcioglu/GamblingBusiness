using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Removes what was left of the old online world from the scenes: its managers and panels in MainGame (map selection,
/// $1B popup, old Online button) and the Istanbul scene from Build Settings. The Online City needs no scene objects.
/// Safe to run more than once. Tools > Online City > Remove the Old Online World, or in batch mode:
///   Unity -batchmode -projectPath . -executeMethod CityRetireOldOnlineWorld.RunBatch
/// </summary>
public static class CityRetireOldOnlineWorld
{
    const string MainGamePath = "Assets/Scenes/MainGame.unity";
    const string IstanbulPath = "Assets/Scenes/OnlineWorld_Istanbul.unity";

    static readonly string[] OldObjects =
    {
        "OnlineManagers", "MapSelectionController", "Canvas/MapSelectionPanel", "Canvas/OnlineWorld",
        "BillioinUnlockUi", "Canvas/BillionUnlockPanel", "NetworkTest",
    };

    static readonly string[] OldScripts =
    {
        "OnlineWorldManager", "FirebaseManager", "MapSelectionUI", "OnlineWorldButton", "BillionUnlockUI", "NetworkTest",
    };

    /// <summary>Objects that stay but carried an old script (the debug button also had the old Online button's script).</summary>
    static readonly string[] CleanScriptsOn = { "Canvas/Debug" };

    [MenuItem("Tools/Online City/Remove the Old Online World", false, 100)]
    static void FromMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Old online world", "Stop Play mode first, then run this again.", "OK");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorUtility.DisplayDialog("Old online world", string.Join("\n", Run()), "OK");
    }

    public static void RunBatch()
    {
        int code = 0;
        try
        {
            foreach (string line in Run()) Debug.Log("[CityRetire] " + line);
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            code = 1;
        }
        EditorApplication.Exit(code);
    }

    static List<string> Run()
    {
        var log = new List<string>();
        var scene = EditorSceneManager.OpenScene(MainGamePath, OpenSceneMode.Single);
        int removed = 0;

        foreach (string path in OldObjects)
        {
            foreach (var go in FindAll(scene, path))
            {
                Object.DestroyImmediate(go);
                log.Add($"MainGame: deleted \"{path}\".");
                removed++;
            }
        }

        foreach (string path in CleanScriptsOn)
        {
            foreach (var go in FindAll(scene, path))
            {
                foreach (var behaviour in go.GetComponents<MonoBehaviour>())
                {
                    if (behaviour == null || !OldScripts.Contains(behaviour.GetType().Name)) continue;
                    log.Add($"MainGame: removed {behaviour.GetType().Name} from \"{path}\".");
                    Object.DestroyImmediate(behaviour);
                    removed++;
                }
                int missing = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
                if (missing > 0)
                {
                    log.Add($"MainGame: removed {missing} missing script(s) from \"{path}\" (the old Online button's).");
                    removed += missing;
                }
            }
        }

        // Anything else still running an old script, wherever it sits
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null || !OldScripts.Contains(behaviour.GetType().Name)) continue;
                log.Add($"MainGame: removed {behaviour.GetType().Name} from \"{PathOf(behaviour.transform)}\".");
                Object.DestroyImmediate(behaviour);
                removed++;
            }
        }

        if (removed > 0)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            log.Add("MainGame saved.");
        }
        else
        {
            log.Add("MainGame: nothing of the old online world is left.");
        }

        var scenes = EditorBuildSettings.scenes.ToList();
        if (scenes.RemoveAll(s => s.path == IstanbulPath) > 0)
        {
            EditorBuildSettings.scenes = scenes.ToArray();
            log.Add("Build Settings: removed OnlineWorld_Istanbul.");
        }
        return log;
    }

    /// <summary>Every object at this path ("Canvas/Debug"), in case several roots share a name.</summary>
    static IEnumerable<GameObject> FindAll(Scene scene, string path)
    {
        string[] parts = path.Split('/');
        var current = scene.GetRootGameObjects().Where(g => g.name == parts[0]).ToList();
        for (int i = 1; i < parts.Length; i++)
        {
            var next = new List<GameObject>();
            foreach (var go in current)
            {
                foreach (Transform child in go.transform)
                    if (child.name == parts[i]) next.Add(child.gameObject);
            }
            current = next;
        }
        return current;
    }

    static string PathOf(Transform t)
    {
        var parts = new List<string>();
        for (; t != null; t = t.parent) parts.Insert(0, t.name);
        return string.Join("/", parts);
    }
}
