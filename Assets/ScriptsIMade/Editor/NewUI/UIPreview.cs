using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>
/// Renders UI prefabs to PNG files for checking them against the design canvas without opening Play mode.
/// Batch mode only: it switches to the built-in render pipeline for the capture (UI colours then match the
/// design exactly, no HDRP exposure or tonemapping) and restores the project's pipeline afterwards.
/// Layers are drawn in their canvases' sorting order (the HUD at -1, pop-ups above it).
/// </summary>
public static class UIPreview
{
    static readonly Color Backdrop = UIColors.Hex(0x2A332F);

    public struct Layer
    {
        public GameObject prefab;
        public System.Action<GameObject> setup;
        public Layer(GameObject prefab, System.Action<GameObject> setup = null)
        {
            this.prefab = prefab;
            this.setup = setup;
        }
    }

    public static void RenderOfficeHud(GameObject prefab, string pngPath, List<string> log)
    {
        if (!Application.isBatchMode)
        {
            log.Add("Preview skipped (batch mode only).");
            return;
        }
        string dir = Path.GetDirectoryName(pngPath);
        string alt = Path.Combine(dir ?? "", Path.GetFileNameWithoutExtension(pngPath) + "_alt.png");
        Render(pngPath, log, new Layer(prefab));
        Render(alt, log, new Layer(prefab, AltState));
    }

    /// <summary>The other HUD states: tabs unlocked with a NEW badge, batch done, golden applicant, a toast, a stamp.</summary>
    static void AltState(GameObject hud)
    {
        var h = hud.GetComponent<OfficeHud>();
        foreach (var tab in new[] { h.stocksTab, h.casinoTab, h.onlineTab })
        {
            tab.openLook.SetActive(true);
            tab.lockedLook.SetActive(false);
        }
        if (h.onlineTab.newBadge != null) h.onlineTab.newBadge.SetActive(true);
        h.applicantReview.SetActive(false);
        h.applicantDone.SetActive(true);
        h.goldenSticky.SetActive(true);
        h.reviewBar.SetActive(false);
        h.nextBatchButton.gameObject.SetActive(true);
        h.valuePop.gameObject.SetActive(true);

        var board = hud.GetComponentInChildren<HudTaskBoard>(true);
        board.stampComplete.SetActive(true);
        board.urgentGlow.SetActive(true);

        var toasts = hud.GetComponentInChildren<HudToasts>(true);
        var t = toasts.template;
        float y = t.anchoredPosition.y;
        var kinds = new[] { NotificationKind.Good, NotificationKind.Bad, NotificationKind.Info };
        var msgs = new[] { "Maya Collins joined the team. +$4,200 company value.",
                           "Mystery Box: it was an invoice... " + UIFormat.Minus + "$1,200", "Autosaved." };
        var icons = new[] { toasts.goodIcon, toasts.badIcon, toasts.infoIcon };
        for (int i = 0; i < 3; i++)
        {
            var copy = Object.Instantiate(t, t.parent);
            copy.gameObject.SetActive(true);
            copy.GetComponent<HudToastView>().Set(msgs[i], kinds[i], icons[i]);
            copy.anchoredPosition = new Vector2(t.anchoredPosition.x, y);
            y += t.sizeDelta.y + toasts.spacing;
        }

        var tip = hud.GetComponent<HudTooltip>();
        tip.slip.gameObject.SetActive(true);
    }

    /// <summary>Render the layers (bottom first) into one 1920x1080 PNG.</summary>
    public static void Render(string pngPath, List<string> log, params Layer[] layers)
    {
        RenderWith(pngPath, log, cam =>
        {
            var made = new List<GameObject>();
            for (int i = 0; i < layers.Length; i++)
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(layers[i].prefab);
                inst.SetActive(true);
                var canvas = inst.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = cam;
                canvas.planeDistance = 10f - i;
                layers[i].setup?.Invoke(inst);
                made.Add(inst);
            }
            return made;
        });
    }

    /// <summary>One capture of RenderShots: the PNG and what to change before it is taken (may be null).</summary>
    public struct Shot
    {
        public string png;
        public System.Action prepare;
        public Shot(string png, System.Action prepare = null)
        {
            this.png = png;
            this.prepare = prepare;
        }
    }

    /// <summary>
    /// The screen the previews are taken on (1920 x 1080 unless a batch run sets another, e.g. NewUIBatch -previewSize
    /// 3440x1440): every canvas that scales with the screen gets the scale it would have there.
    /// </summary>
    public static Vector2Int ScreenSize = new Vector2Int(1920, 1080);

    /// <summary>Sets ScreenSize from the command line's -previewSize WxH (e.g. 2560x1080), if there is one.</summary>
    public static void ReadScreenSizeArg()
    {
        var args = System.Environment.GetCommandLineArgs();
        int i = System.Array.IndexOf(args, "-previewSize");
        if (i < 0 || i + 1 >= args.Length) return;
        var parts = args[i + 1].ToLowerInvariant().Split('x');
        if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h) && w > 0 && h > 0)
            ScreenSize = new Vector2Int(w, h);
    }

    /// <summary>
    /// Render whatever the setup builds into one PNG (the size of ScreenSize unless one is given). The setup gets the preview
    /// camera (orthographic, in a new empty scene) and returns the objects it made, which are laid out, captured and destroyed.
    /// </summary>
    public static void RenderWith(string pngPath, List<string> log, System.Func<Camera, List<GameObject>> setup,
        int width = 0, int height = 0) =>
        RenderShots(log, setup, new[] { new Shot(pngPath) }, width, height);

    /// <summary>Like RenderWith, but the setup's objects are captured once per shot (an animation's frames, states).</summary>
    public static void RenderShots(List<string> log, System.Func<Camera, List<GameObject>> setup, IList<Shot> shots,
        int width = 0, int height = 0)
    {
        if (!Application.isBatchMode)
        {
            log.Add("Preview skipped (batch mode only).");
            return;
        }
        if (width <= 0 || height <= 0)
        {
            width = ScreenSize.x;
            height = ScreenSize.y;
        }
        foreach (var shot in shots)
        {
            string dir = Path.GetDirectoryName(shot.png);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        }

        // Remember the pipelines by asset path: the new preview scene unloads unused assets, and with the pipeline
        // cleared the HDRP assets are unused, so references kept across it come back dead (that saved an empty pipeline once)
        string pipelinePath = AssetDatabase.GetAssetPath(GraphicsSettings.defaultRenderPipeline);
        string qualityPipelinePath = AssetDatabase.GetAssetPath(QualitySettings.renderPipeline);
        GameObject camGo = null;
        var instances = new List<GameObject>();
        RenderTexture rt = null;
        try
        {
            GraphicsSettings.defaultRenderPipeline = null;
            QualitySettings.renderPipeline = null;

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            camGo = new GameObject("PreviewCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Backdrop;
            cam.orthographic = true;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 100f;

            rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.antiAliasing = 1;
            cam.targetTexture = rt;

            instances.AddRange(setup(cam));
            foreach (var inst in instances)
            {
                ApplyLanguage(inst);
                FitToScreen(inst, width, height);
            }

            foreach (var shot in shots)
            {
                shot.prepare?.Invoke();
                for (int pass = 0; pass < 3; pass++)
                {
                    Canvas.ForceUpdateCanvases();
                    foreach (var inst in instances)
                    {
                        if (inst.transform is RectTransform instRect)
                            LayoutRebuilder.ForceRebuildLayoutImmediate(instRect);
                        foreach (var tmp in inst.GetComponentsInChildren<TMP_Text>(false))
                            tmp.ForceMeshUpdate(true, true);
                    }
                }
                foreach (var inst in instances)
                {
                    foreach (var ul in inst.GetComponentsInChildren<TextUnderline>(false))
                        if (ul.enabled) ul.Rebuild();
                    foreach (var hl in inst.GetComponentsInChildren<TextHighlighter>(false))
                        if (hl.enabled) hl.Rebuild();
                }
                Canvas.ForceUpdateCanvases();
                cam.Render();

                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                File.WriteAllBytes(shot.png, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                if (shots.Count <= 12) log.Add("Preview: " + shot.png);
            }
            if (shots.Count > 12) log.Add("Preview: " + shots.Count + " frames, " + shots[0].png + " ...");
        }
        finally
        {
            foreach (var inst in instances)
                if (inst != null) Object.DestroyImmediate(inst);
            if (camGo != null) Object.DestroyImmediate(camGo);
            if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
            GraphicsSettings.defaultRenderPipeline = LoadPipeline(pipelinePath);
            QualitySettings.renderPipeline = LoadPipeline(qualityPipelinePath);
            EnsureProjectPipeline(log);
        }
    }

    /// <summary>
    /// Puts a preview instance in the current language (L10n.SetLanguage). L10nText and L10nFont only run in Play mode, so
    /// here every bound label gets its entry and every text its language's font (English keeps the design fonts).
    /// </summary>
    public static void ApplyLanguage(GameObject root)
    {
        if (root == null) return;
        foreach (var localized in root.GetComponentsInChildren<L10nText>(true))
            localized.Refresh();
        L10nFont.PreviewLooks = true;   // outline and glow presets on the language fonts, as in Play mode
        try
        {
            foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                var font = L10nFont.Attach(text);
                if (font != null) font.Apply();   // Attach only applies on active texts
            }
        }
        finally
        {
            L10nFont.PreviewLooks = false;
        }
    }

    /// <summary>
    /// What CanvasScaler does on a width x height screen, for the root canvases under root that scale with the screen
    /// (its Update doesn't run in batch mode, so the preview sets the scale itself).
    /// </summary>
    public static void FitToScreen(GameObject root, int width, int height)
    {
        if (root == null) return;
        foreach (var scaler in root.GetComponentsInChildren<CanvasScaler>(true))
        {
            var canvas = scaler.GetComponent<Canvas>();
            if (canvas == null || !canvas.isRootCanvas || scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize) continue;
            Vector2 reference = scaler.referenceResolution;
            float scale;
            switch (scaler.screenMatchMode)
            {
                case CanvasScaler.ScreenMatchMode.Expand:
                    scale = Mathf.Min(width / reference.x, height / reference.y);
                    break;
                case CanvasScaler.ScreenMatchMode.Shrink:
                    scale = Mathf.Max(width / reference.x, height / reference.y);
                    break;
                default:
                    float logWidth = Mathf.Log(width / reference.x, 2f), logHeight = Mathf.Log(height / reference.y, 2f);
                    scale = Mathf.Pow(2f, Mathf.Lerp(logWidth, logHeight, scaler.matchWidthOrHeight));
                    break;
            }
            scaler.enabled = false;
            canvas.scaleFactor = scale;
        }
    }

    static RenderPipelineAsset LoadPipeline(string path) =>
        string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(path);

    // The project's HDRP assets (as in the committed ProjectSettings); see also RenderPipelineGuard
    const string DefaultPipelinePath = "Assets/Settings/HDRPDefaultResources/HDRenderPipelineAsset.asset";
    static readonly Dictionary<string, string> QualityPipelines = new Dictionary<string, string>
    {
        { "High Fidelity", "Assets/Settings/HDRP High Fidelity.asset" },
        { "Balanced", "Assets/Settings/HDRP Balanced.asset" },
        { "Performant", "Assets/Settings/HDRP Performant.asset" },
    };

    /// <summary>
    /// Never leave the project without its render pipeline: the previews switch to the built-in renderer while they
    /// capture, and an empty pipeline saved to ProjectSettings makes every HDRP material draw magenta in the editor.
    /// Fills empty slots from the known assets and saves them (a pipeline that is set is left alone).
    /// </summary>
    public static void EnsureProjectPipeline(List<string> log)
    {
        bool changed = false;
        if (GraphicsSettings.defaultRenderPipeline == null)
        {
            var asset = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(DefaultPipelinePath);
            if (asset != null)
            {
                GraphicsSettings.defaultRenderPipeline = asset;
                changed = true;
                log?.Add("Render pipeline: default slot was empty, set back to " + DefaultPipelinePath);
            }
        }
        string[] names = QualitySettings.names;
        int level = QualitySettings.GetQualityLevel();
        if (QualitySettings.renderPipeline == null && level >= 0 && level < names.Length
            && QualityPipelines.TryGetValue(names[level], out string path))
        {
            var asset = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(path);
            if (asset != null)
            {
                QualitySettings.renderPipeline = asset;
                changed = true;
                log?.Add("Render pipeline: quality \"" + names[level] + "\" was empty, set back to " + path);
            }
        }
        if (changed) AssetDatabase.SaveAssets();
    }
}
