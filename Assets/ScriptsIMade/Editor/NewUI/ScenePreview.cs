using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Renders the MainGame desk through its Main Camera (HDRP, as the player sees it) with a batch of CV papers on the
/// spawn point, optionally with the office HUD drawn by the same camera. Batch mode, the scene isn't saved.
///   Unity.exe -batchmode -projectPath . -executeMethod ScenePreview.RenderDeskCli -out desk.png [-hud]
/// </summary>
public static class ScenePreview
{
    public static void RenderDeskCli()
    {
        var args = System.Environment.GetCommandLineArgs();
        int o = System.Array.IndexOf(args, "-out");
        string png = o >= 0 && o + 1 < args.Length ? args[o + 1] : "desk.png";
        bool hud = System.Array.IndexOf(args, "-hud") >= 0;
        var log = new List<string>();
        int code = 0;
        try
        {
            RenderDesk(png, hud, log);
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            code = 1;
        }
        foreach (var l in log) Debug.Log("[ScenePreview] " + l);
        EditorApplication.Exit(code);
    }

    /// <summary>
    /// Samples the paper's Hire / Skip clips (whatever its Animator plays) on a paper at the pile and logs where the
    /// paper is at the start and the end, against the spawn point, the hired box and the bin.
    ///   Unity.exe -batchmode -projectPath . -executeMethod ScenePreview.CheckPaperClipsCli
    /// </summary>
    public static void CheckPaperClipsCli()
    {
        int code = 0;
        try
        {
            EditorSceneManager.OpenScene("Assets/Scenes/MainGame.unity", OpenSceneMode.Single);
            var gfm = Object.FindAnyObjectByType<GameFlowManager>();
            var paper = (GameObject)PrefabUtility.InstantiatePrefab(gfm.paperPrefab);
            var anim = paper.GetComponent<Animator>();
            var controller = anim.runtimeAnimatorController;
            Debug.Log("[ScenePreview] paper controller: " + (controller != null ? controller.name : "none"));
            foreach (var clip in controller.animationClips)
            {
                if (clip.name.IndexOf("Hire", System.StringComparison.OrdinalIgnoreCase) < 0 &&
                    clip.name.IndexOf("Skip", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                var lines = new List<string>();
                foreach (float t in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
                {
                    gfm.GetPaperPose(0, out Vector3 pos, out Quaternion rot);
                    paper.transform.SetPositionAndRotation(pos, rot);
                    clip.SampleAnimation(paper, t * clip.length);
                    var cube = paper.transform.Find("Cube");
                    lines.Add("t=" + t.ToString("0.00") + " cube " + cube.position.ToString("F3") + " rot " + cube.rotation.eulerAngles.ToString("F1"));
                }
                Debug.Log("[ScenePreview] " + clip.name + " (" + clip.length + " s)\n  " + string.Join("\n  ", lines) +
                    "\n  spawn " + gfm.paperSpawnPoint.position.ToString("F3") + " rot " + gfm.paperSpawnPoint.rotation.eulerAngles.ToString("F1") +
                    "\n  hired box " + (gfm.hiredBoxPoint != null ? gfm.hiredBoxPoint.position.ToString("F3") : "-") +
                    "  bin " + (gfm.trashPoint != null ? gfm.trashPoint.position.ToString("F3") : "-"));
            }
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            code = 1;
        }
        EditorApplication.Exit(code);
    }

    /// <summary>Sample applicants for previews (the design's Maya Collins and Haruto Sato, plus one with a record).</summary>
    public static ApplicantData Sample(int i)
    {
        var a = new ApplicantData();
        switch (i % 3)
        {
            case 0:
                a.name = "Maya Collins"; a.age = 29; a.gender = "Female"; a.race = "Mixed"; a.degree = "Master";
                a.major = "Economics"; a.GPA = 3.62f; a.IQ = 124; a.salary = 84000; a.experienceYears = 6;
                a.traits.Add(new PersonalityTrait("Fast Learner", TraitType.Positive));
                a.traits.Add(new PersonalityTrait("Addicted to Coffee", TraitType.Neutral));
                a.traits.Add(new PersonalityTrait("Talks Too Much", TraitType.Negative));
                a.portraitIndex = 12;
                break;
            case 1:
                a.name = "Haruto Sato"; a.age = 41; a.gender = "Male"; a.race = "Asian"; a.degree = "PhD";
                a.major = "Physics"; a.GPA = 3.97f; a.IQ = 151; a.salary = 48000; a.experienceYears = 18; a.isGolden = true;
                a.traits.Add(new PersonalityTrait("Hard Worker", TraitType.Positive));
                a.traits.Add(new PersonalityTrait("Calm Under Pressure", TraitType.Positive));
                a.traits.Add(new PersonalityTrait("Organized", TraitType.Positive));
                a.portraitIndex = 3800;
                break;
            default:
                a.name = "Christopher Richardson"; a.age = 52; a.gender = "Male"; a.race = "White"; a.degree = "High School";
                a.major = "Meeting That Could've Been an Email"; a.GPA = 2.1f; a.IQ = 88; a.salary = 215000; a.experienceYears = 1;
                a.criminalRecord = true;
                a.traits.Add(new PersonalityTrait("Gets Offended Easily", TraitType.Negative));
                a.traits.Add(new PersonalityTrait("Obsessed with Crypto", TraitType.Neutral));
                a.traits.Add(new PersonalityTrait("Team Player", TraitType.Positive));
                a.portraitIndex = 4100;
                break;
        }
        a.photo = PortraitLibrary.Load(a.portraitIndex);
        return a;
    }

    public static void RenderDesk(string png, bool withHud, List<string> log, System.Action<List<GameObject>> setup = null)
    {
        if (!Application.isBatchMode)
        {
            log.Add("Desk preview skipped (batch mode only).");
            return;
        }
        UIPreview.EnsureProjectPipeline(log);
        EditorSceneManager.OpenScene("Assets/Scenes/MainGame.unity", OpenSceneMode.Single);

        var gfm = Object.FindAnyObjectByType<GameFlowManager>();
        var cam = Camera.main;
        if (gfm == null || gfm.paperPrefab == null || cam == null)
        {
            log.Add("Desk preview: GameFlowManager, its paper prefab or the Main Camera is missing.");
            return;
        }

        var papers = new List<GameObject>();
        for (int slot = 0; slot < 3; slot++)
        {
            var p = (GameObject)PrefabUtility.InstantiatePrefab(gfm.paperPrefab);
            gfm.GetPaperPose(slot, out Vector3 pos, out Quaternion rot);
            p.transform.SetPositionAndRotation(pos, rot);
            var binder = p.GetComponent<ApplicantUIBinder>();
            if (binder != null) binder.Display(Sample(slot));
            papers.Add(p);
        }
        setup?.Invoke(papers);
        // edit mode: no LateUpdate, so lay the papers' texts and highlighter marks out by hand
        Canvas.ForceUpdateCanvases();
        foreach (var p in papers)
        {
            foreach (var tmp in p.GetComponentsInChildren<TMPro.TMP_Text>(false))
                tmp.ForceMeshUpdate(true, true);
            foreach (var hl in p.GetComponentsInChildren<TextHighlighter>(false))
                if (hl.enabled) hl.Rebuild();
        }

        if (withHud)
        {
            foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!canvas.isRootCanvas || canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                if (canvas.GetComponent<OfficeHud>() == null) continue;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = cam;
                canvas.planeDistance = cam.nearClipPlane + 0.01f;
                canvas.sortingOrder = 100;   // in the game it's an overlay, always above the papers
            }
        }

        var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        try
        {
            cam.targetTexture = rt;
            cam.aspect = 1920f / 1080f;
            for (int i = 0; i < 12; i++)
            {
                Canvas.ForceUpdateCanvases();
                cam.Render();
            }

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            string dir = Path.GetDirectoryName(png);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllBytes(png, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            log.Add("Desk preview: " + png);

            // Where the top paper lands on screen (for lining the HUD up with it)
            var sheet = papers[0].GetComponentInChildren<CVPaperView>(true);
            Transform box = papers[0].transform.Find("Cube");
            if (sheet != null && sheet.sheet != null)
            {
                var corners = new Vector3[4];
                sheet.sheet.GetWorldCorners(corners);
                var pts = new List<string>();
                foreach (var c in corners)
                {
                    Vector3 s = cam.WorldToScreenPoint(c);
                    pts.Add("(" + Mathf.RoundToInt(s.x) + ", " + Mathf.RoundToInt(1080f - s.y) + ")");
                }
                log.Add("Top CV sheet corners on screen (x, y down; bl, tl, tr, br): " + string.Join(" ", pts));
            }
            else if (box != null)
            {
                var pts = new List<string>();
                foreach (var c in new[] { new Vector3(-0.5f, 0.5f, -0.5f), new Vector3(0.5f, 0.5f, -0.5f), new Vector3(0.5f, -0.5f, -0.5f), new Vector3(-0.5f, -0.5f, -0.5f) })
                {
                    Vector3 s = cam.WorldToScreenPoint(box.TransformPoint(c));
                    pts.Add("(" + Mathf.RoundToInt(s.x) + ", " + Mathf.RoundToInt(1080f - s.y) + ")");
                }
                log.Add("Top paper corners on screen (x, y down): " + string.Join(" ", pts));
            }
        }
        finally
        {
            cam.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(rt);
        }
    }
}
