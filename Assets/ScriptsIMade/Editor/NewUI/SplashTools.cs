using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The start-up splash (SplashScreen plays SplashView):
/// - Install: gives the SplashScreen scene's SplashScreen the Assets/UI/Splash sprites and the Coming Soon font, removes
///   the old video canvas, saves the scene and moves the old splash video out of Assets (to ../_removed_splash_backup,
///   kept, not deleted). Menu Tools > Game > Splash > Install New Splash, or -executeMethod SplashTools.InstallBatch.
/// - Preview (batch mode, no Play mode): -executeMethod SplashTools.Preview [-previewDir d] [-frames] renders moments of
///   the animation at several screen shapes (16:9, 21:9, 32:9, 16:10) and, with -frames, every 30th of a second at
///   1280x720 for a video. The canvas scale is the one Expand gives on that screen.
/// The sprites come from Tools/Splash/make_splash_assets.py.
/// </summary>
public static class SplashTools
{
    const string ScenePath = "Assets/Scenes/SplashScreen.unity";
    const string SpriteFolder = "Assets/UI/Splash";
    const string OldVideoPath = "Assets/Scenes/Mchunter In.mp4";

    [MenuItem("Tools/Game/Splash/Install New Splash")]
    public static void Install()
    {
        var log = new List<string>();
        bool ok = InstallInto(log);
        foreach (var line in log) Debug.Log("[Splash] " + line);
        if (!ok) Debug.LogError("[Splash] Install failed, see above.");
    }

    public static void InstallBatch()
    {
        var log = new List<string>();
        bool ok = false;
        try
        {
            ok = InstallInto(log);
        }
        catch (System.Exception e)
        {
            log.Add("EXCEPTION " + e);
        }
        foreach (var line in log) Debug.Log("[Splash] " + line);
        EditorApplication.Exit(ok ? 0 : 1);
    }

    /// <summary>The splash sprites, sorted by name (imported as sprites first if they aren't yet).</summary>
    public static List<Sprite> LoadSprites(List<string> log)
    {
        var sprites = new List<Sprite>();
        foreach (string file in Directory.GetFiles(SpriteFolder, "*.png").OrderBy(f => f, System.StringComparer.Ordinal))
        {
            string path = file.Replace('\\', '/');
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null && importer.textureType != TextureImporterType.Sprite)
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate); // UIKitImportSettings makes it a sprite
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite != null) sprites.Add(sprite);
            else log?.Add("Not a sprite: " + path);
        }
        return sprites;
    }

    static bool InstallInto(List<string> log)
    {
        AssetDatabase.Refresh();
        var sprites = LoadSprites(log);
        log.Add("Sprites: " + sprites.Count);
        if (sprites.Count == 0) return false;

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var splash = Object.FindFirstObjectByType<SplashScreen>(FindObjectsInactive.Include);
        if (splash == null)
        {
            log.Add("No SplashScreen in " + ScenePath);
            return false;
        }

        var so = new SerializedObject(splash);
        var list = so.FindProperty("sprites");
        list.arraySize = sprites.Count;
        for (int i = 0; i < sprites.Count; i++)
            list.GetArrayElementAtIndex(i).objectReferenceValue = sprites[i];
        so.FindProperty("hintFont").objectReferenceValue = UIFonts.Main;
        so.ApplyModifiedPropertiesWithoutUndo();

        // The old picture: a canvas holding only the video's RawImage and the old hint text
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.name != "Canvas" || root.GetComponent<Canvas>() == null) continue;
            var children = root.transform.Cast<Transform>().Select(c => c.name).ToList();
            if (children.All(n => n == "RawImage" || n == "Text (TMP)"))
            {
                log.Add("Removed the old video canvas (" + string.Join(", ", children) + ")");
                Object.DestroyImmediate(root);
            }
            else
            {
                log.Add("Kept the canvas \"Canvas\": it holds " + string.Join(", ", children));
            }
        }
        foreach (var player in splash.GetComponents<UnityEngine.Video.VideoPlayer>())
        {
            Object.DestroyImmediate(player);
            log.Add("Removed a VideoPlayer from " + splash.name);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
        {
            log.Add("Couldn't save " + ScenePath);
            return false;
        }
        log.Add("Saved " + ScenePath);

        MoveOldVideo(log);
        return true;
    }

    // Out of Assets (so it leaves the build) but kept next to the project, in case it's wanted again
    static void MoveOldVideo(List<string> log)
    {
        if (!File.Exists(OldVideoPath)) return;
        string guid = AssetDatabase.AssetPathToGUID(OldVideoPath);
        if (!string.IsNullOrEmpty(guid) && File.ReadAllText(ScenePath).Contains(guid))
        {
            log.Add("The scene still uses the old video: it stays in Assets.");
            return;
        }
        string backup = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "_removed_splash_backup"));
        Directory.CreateDirectory(backup);
        foreach (string file in new[] { OldVideoPath, OldVideoPath + ".meta" })
        {
            string target = Path.Combine(backup, Path.GetFileName(file));
            if (File.Exists(target)) File.Delete(target);
            File.Move(file, target);
        }
        AssetDatabase.Refresh();
        log.Add("Moved the old splash video to " + backup);
    }

    // ------------------------------------------------------------------ preview

    static readonly (string name, int w, int h)[] Screens =
    {
        ("16x9", 1920, 1080), ("21x9", 2560, 1080), ("ultrawide_3440", 3440, 1440), ("32x9", 5120, 1440), ("16x10", 1920, 1200)
    };

    public static void Preview()
    {
        var log = new List<string>();
        bool ok = false;
        try
        {
            string dir = Arg("-previewDir") ?? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "SplashPreview"));
            var sprites = LoadSprites(log);
            var font = UIFonts.Main;
            log.Add("Sprites: " + sprites.Count);

            float[] moments = { 0.75f, 1.2f, 2.3f, 3.45f, 3.9f, 5.6f };
            foreach (var screen in Screens)
            {
                bool all = screen.name == "16x9";
                var times = all ? moments : new[] { 2.3f, 5.6f };
                SplashView view = null;
                var shots = times.Select(t => new UIPreview.Shot(Path.Combine(dir, $"splash_{screen.name}_{t:0.00}s.png"),
                    () => view.Pose(t))).ToList();
                UIPreview.RenderShots(log, cam => Setup(cam, sprites, font, screen.w, screen.h, out view, log), shots, screen.w, screen.h);
            }

            if (HasArg("-frames"))
            {
                SplashView view = null;
                const int fps = 30;
                int count = Mathf.CeilToInt((SplashView.End + 0.5f) * fps);
                var shots = new List<UIPreview.Shot>();
                for (int i = 0; i < count; i++)
                {
                    float t = i / (float)fps;
                    shots.Add(new UIPreview.Shot(Path.Combine(dir, "frames", $"f{i:0000}.png"), () =>
                    {
                        view.Pose(t);
                        // the end: the fade to black SplashScreen runs
                        view.Fade.alpha = Mathf.Clamp01((t - SplashView.End) / 0.45f);
                        view.Hint.alpha = t < 0.5f ? 0f : Mathf.Min(0.6f, (t - 0.5f) / 0.4f * 0.6f) * (1f - view.Fade.alpha);
                    }));
                }
                UIPreview.RenderShots(log, cam => Setup(cam, sprites, font, 1280, 720, out view, log), shots, 1280, 720);
            }
            ok = true;
        }
        catch (System.Exception e)
        {
            log.Add("EXCEPTION " + e);
        }
        foreach (var line in log) Debug.Log("[SplashPreview] " + line);
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
    }

    static List<GameObject> Setup(Camera cam, List<Sprite> sprites, TMP_FontAsset font, int width, int height, out SplashView view, List<string> log)
    {
        var holder = new GameObject("SplashPreview");
        view = SplashView.Build(holder.transform, sprites, font);
        if (view == null) throw new System.Exception("SplashView.Build returned null");
        view.Canvas.renderMode = RenderMode.ScreenSpaceCamera;
        view.Canvas.worldCamera = cam;
        view.Canvas.planeDistance = 10f;
        view.Hint.alpha = 0.6f;
        // UIPreview scales the canvas as Expand would on this screen; check it took
        UIPreview.FitToScreen(holder, width, height);
        Canvas.ForceUpdateCanvases();
        log.Add($"{width}x{height}: canvas {((RectTransform)view.Root.transform).rect.size} (scale {view.Canvas.scaleFactor:0.###})");
        return new List<GameObject> { holder };
    }

    static string Arg(string name)
    {
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == name) return args[i + 1];
        return null;
    }

    static bool HasArg(string name) => System.Environment.GetCommandLineArgs().Contains(name);
}
