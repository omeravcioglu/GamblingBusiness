using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static UIBuild;

/// <summary>
/// CV paper (boards "CV direction A: typed résumé" and "CV paper: all states"): builds the typed résumé as a
/// world-space canvas on Assets/Prefabs/ApplicantPaper.prefab (CVPaperView; ApplicantUIBinder fills it), and with
/// Install moves the paper pile to where the office HUD design draws it: centred, 740 of 1080 px tall. The hire and
/// skip clips get copies that start from the new spot (an override controller); the original clips stay as they are.
/// The faces are on the UI layer and drawn after post-processing by a custom pass (the office's ACES tonemapping and
/// +1.8 EV would turn the paper white grey, while the HUD next to it keeps the design's colours).
/// Coordinates are the design's 560 x 740 sheet (outer border box, y down).
/// </summary>
public static class CVPaperBuilder
{
    public const string PrefabPath = "Assets/Prefabs/ApplicantPaper.prefab";
    const string ControllerPath = "Assets/Animations/ApplicantPaper.controller";
    const string HireClipPath = "Assets/Animations/Hire.anim";
    const string SkipClipPath = "Assets/Animations/Skip.anim";
    const string AnimDir = "Assets/Animations/CVPaper";
    const string OverridePath = AnimDir + "/ApplicantPaperCV.overrideController";

    /// <summary>The paper in metres (the design's 560 x 740 sheet).</summary>
    public const float PaperHeight = 0.3072f;
    public const float PaperWidth = PaperHeight * W / H;
    const float W = 560f, H = 740f;
    /// <summary>Centre of the top sheet on the office HUD board (1920 x 1080).</summary>
    static readonly Vector2 DesignCentre = new Vector2(960f, 520f);

    // The pile before Install (for Restore)
    static readonly Vector3 OldSpawnPosition = new Vector3(0.198f, 1.097f, 2.794f);
    static readonly Quaternion OldSpawnRotation = new Quaternion(0.017483752f, -0.9972861f, 0.07120126f, 0.0067240633f);
    static readonly Vector3 OldCubeScale = new Vector3(0.25400224f, 0.39409f, 0.002f);
    static readonly Vector3 OldCubePosition = new Vector3(0f, -0.0022f, 0f);

    static readonly Color Mark = new Color32(0xFF, 0xE1, 0x4D, 0xF2);

    /// <summary>String Table of the fixed labels (Assets/Localization/Source/Panels.csv).</summary>
    const string L10nTable = "Panels";

    /// <summary>Layer of the paper faces: the Main Camera skips it, the "CV Paper Faces" custom pass draws it after post.</summary>
    const int FaceLayer = 5;   // UI
    const string FacePassName = "CV Paper Faces";

    // ------------------------------------------------------------------
    // Menu

    [MenuItem("Tools/Game/New UI/Build CV Paper")]
    public static void BuildFromMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Build CV Paper", "Stop Play mode first.", "OK");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var log = new List<string>();
        var prefab = BuildPrefab(log);
        if (prefab != null) Install(prefab, log);
        Debug.Log("[CVPaperBuilder]\n" + string.Join("\n", log));
        EditorUtility.DisplayDialog("CV paper", string.Join("\n", log), "OK");
    }

    [MenuItem("Tools/Game/New UI/Restore Old CV Paper")]
    public static void RestoreFromMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var log = new List<string>();
        Restore(log);
        Debug.Log("[CVPaperBuilder]\n" + string.Join("\n", log));
        EditorUtility.DisplayDialog("CV paper", string.Join("\n", log), "OK");
    }

    /// <summary>Batch: Unity.exe -batchmode -executeMethod CVPaperBuilder.BatchRun [-install] [-preview folder]</summary>
    public static void BatchRun()
    {
        int code = 0;
        var log = new List<string>();
        try
        {
            UIPreview.EnsureProjectPipeline(log);
            var args = System.Environment.GetCommandLineArgs();
            bool install = System.Array.IndexOf(args, "-install") >= 0;
            int p = System.Array.IndexOf(args, "-preview");
            string dir = p >= 0 && p + 1 < args.Length ? args[p + 1] : null;
            var prefab = BuildPrefab(log);
            if (prefab == null) code = 1;
            if (prefab != null && install) Install(prefab, log);
            if (prefab != null && dir != null) RenderPreviews(prefab, dir, log);
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            code = 1;
        }
        finally
        {
            try { UIPreview.EnsureProjectPipeline(log); } catch (System.Exception e) { Debug.LogException(e); }
        }
        Debug.Log("[CVPaperBuilder]\n" + string.Join("\n", log));
        EditorApplication.Exit(code);
    }

    // ------------------------------------------------------------------
    // Prefab

    public static GameObject BuildPrefab(List<string> log)
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var cube = root.transform.Find("Cube");
            if (cube == null)
            {
                log.Add("CV paper: " + PrefabPath + " has no 'Cube' child (the hire animation moves it), nothing built.");
                return null;
            }
            var oldFace = cube.Find("CVFace");
            if (oldFace != null) Object.DestroyImmediate(oldFace.gameObject);
            // the old face (3D texts and the photo sprite) is kept, switched off (Restore turns it back on)
            foreach (Transform child in cube)
                child.gameObject.SetActive(false);

            // the box stays for the hire flip and the collider, a little smaller than the drawn sheet so its
            // corners hide under the paper's rounded corners
            float cw = PaperWidth * (W - 6f) / W, ch = PaperHeight * (H - 6f) / H;
            cube.localPosition = Vector3.zero;
            cube.localRotation = Quaternion.identity;
            cube.localScale = new Vector3(cw, ch, 0.002f);

            // undo the box's scale, 1.5 mm in front of the side that faces the camera (local -z)
            var face = new GameObject("CVFace").transform;
            face.SetParent(cube, false);
            face.localPosition = new Vector3(0f, 0f, -1.25f);
            face.localScale = new Vector3(1f / cw, 1f / ch, 1f / 0.002f);

            var view = BuildFace(face);
            foreach (Transform t in face.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = FaceLayer;

            var binder = root.GetComponent<ApplicantUIBinder>();
            if (binder != null)
            {
                binder.cvView = view;
                binder.goldenEffects = null;   // pointed at the paper itself (ignored with a warning)
            }

            var saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool ok);
            log.Add(ok ? "CV paper: typed résumé face built on " + PrefabPath : "CV paper: could not save " + PrefabPath);
            return ok ? saved : null;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static CVPaperView BuildFace(Transform face)
    {
        var canvasRt = NewRect(face, "CV");
        canvasRt.sizeDelta = new Vector2(W, H);
        var view = canvasRt.gameObject.AddComponent<CVPaperView>();

        var sheet = NewRect(canvasRt, "Sheet");
        sheet.anchorMin = sheet.anchorMax = sheet.pivot = new Vector2(0.5f, 0.5f);
        sheet.sizeDelta = new Vector2(W, H);
        view.sheet = sheet;

        BuildPaper(sheet, view);
        BuildHeader(sheet, view);
        BuildBody(sheet, view);
        BuildMargin(sheet, view);
        BuildStamps(sheet, view);

        view.goodIcon = IconSprite("check", "b");
        view.badIcon = IconSprite("cross", "b");
        view.quirkyIcon = IconSprite("wave", "b");
        // a major too long for one line wraps onto two tighter 18 px lines (1.15), so the traits still clear the footer
        view.majorLineSpacing = (1.15f - UIFonts.NaturalLineHeight(UIBuild.Font)) * 100f;
        view.majorTwoLineExtra = 2f * 1.15f * 18f - 28.6f;

        // canvas last (adding it resizes the rect in batch mode)
        var canvas = canvasRt.gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
        canvasRt.anchorMin = canvasRt.anchorMax = canvasRt.pivot = new Vector2(0.5f, 0.5f);
        canvasRt.sizeDelta = new Vector2(W, H);
        canvasRt.localPosition = Vector3.zero;
        canvasRt.localRotation = Quaternion.identity;
        canvasRt.localScale = Vector3.one * (PaperWidth / W);
        return view;
    }

    // Paper: golden glow (HUD board), drop shadow, stock, stock texture, tier rims, 3 px edge, letterhead band
    static void BuildPaper(RectTransform sheet, CVPaperView view)
    {
        var glow = Node(sheet, "GoldenGlow", -154f, -154f, W + 308f, H + 308f);
        // box-shadow 0 0 80px 26px rgba(226,177,60,0.55) around a box 8 px outside the sheet, and its 4 px gold ring
        var soft = StretchBox(glow, "Glow", S("Shadows/shadow_s40"), WithAlpha(UIColors.Gold, Mathf.Pow(0.55f, 2.2f)));
        soft.type = Image.Type.Sliced;
        Box(glow, "Ring", 154f - 12f, 154f - 12f, W + 24f, H + 24f, S("Kit/r14_fill"), WithAlpha(UIColors.Gold, 0.95f));
        glow.gameObject.SetActive(false);
        view.goldenGlow = glow.gameObject;

        Shadow(sheet, "Shadow", 0f, 0f, W, H, 0f, 18f, 36f, 0.38f);
        view.paperFill = Box(sheet, "Fill", 0f, 0f, W, H, S("Kit/cvpaper_fill"), UIColors.PaperHi);

        view.linenTile = S("Kit/Tiles/cv_linen");
        view.laidTile = S("Kit/Tiles/cv_laid");
        var tile = StretchBox(sheet, "Stock", view.linenTile, Color.white, 3f, 3f, 3f, 3f);
        tile.type = Image.Type.Tiled;
        tile.enabled = false;
        view.stockTile = tile;

        // tier 5-6 inset rings (inside the 3 px border): 4-6 px and 6-7 px
        var rim = Stretch(sheet, "Rim", 3f, 3f, 3f, 3f);
        float iw = W - 6f, ih = H - 6f;
        view.rimOuter = new[]
        {
            Box(rim, "OuterTop", 4f, 4f, iw - 8f, 2f, null, UIColors.Ink),
            Box(rim, "OuterBottom", 4f, ih - 6f, iw - 8f, 2f, null, UIColors.Ink),
            Box(rim, "OuterLeft", 4f, 6f, 2f, ih - 12f, null, UIColors.Ink),
            Box(rim, "OuterRight", iw - 6f, 6f, 2f, ih - 12f, null, UIColors.Ink),
        };
        view.rimInner = new[]
        {
            Box(rim, "InnerTop", 6f, 6f, iw - 12f, 1f, null, UIColors.Gold),
            Box(rim, "InnerBottom", 6f, ih - 7f, iw - 12f, 1f, null, UIColors.Gold),
            Box(rim, "InnerLeft", 6f, 7f, 1f, ih - 14f, null, UIColors.Gold),
            Box(rim, "InnerRight", iw - 7f, 7f, 1f, ih - 14f, null, UIColors.Gold),
        };
        foreach (var img in view.rimOuter) img.gameObject.SetActive(false);
        foreach (var img in view.rimInner) img.gameObject.SetActive(false);

        view.paperBorder = Box(sheet, "Border", 0f, 0f, W, H, S("Kit/cvpaper_line3"), UIColors.Ink);

        // status tier 2+: the company letterhead printed across the top edge
        var lh = Node(sheet, "Letterhead", 3f, 3f, W - 6f, 22f);
        view.letterhead = lh.gameObject;
        view.letterheadBand = StretchBox(lh, "Band", null, UIColors.Hex(0x22385C));
        view.letterheadLogo = Box(lh, "Logo", 32f, 4f, 14f, 14f, S("Kit/Circles/circle14_fill"), UIColors.PaperHi);
        view.letterheadStar = Icon(lh, "Star", 34f, 6f, 10f, "sparkle4", UIColors.Hex(0x22385C), "fill");
        view.letterheadName = Text(lh, "Company", 54f, 0f, W - 6f - 86f, 22f, "NOVA HOLDINGS", 17f, UIColors.PaperHi, letterSpacingEm: 0.14f);
        lh.gameObject.SetActive(false);
    }

    // Name, one-line bio, clipped square photo, letterhead rule
    static void BuildHeader(RectTransform sheet, CVPaperView view)
    {
        Localize(Text(sheet, "Kicker", 35f, 31f, 286f, 26.6f, "CURRICULUM VITAE", 19f, UIColors.Pencil, letterSpacingEm: 0.12f), L10nTable, "cv.kicker");
        view.firstName = Text(sheet, "FirstName", 35f, 63.6f, 286f, 56.7f, "Maya", 54f, UIColors.Ink, bold: true);
        view.lastName = Text(sheet, "LastName", 35f, 120.3f, 286f, 56.7f, "Collins", 54f, UIColors.Ink, bold: true);
        view.nameWidth = 286f;

        // golden: two drawn stars after the first name (placed after its width at runtime)
        var stars = Node(sheet, "Stars", 35f + 8f, 63.6f + (56.7f - 32f) * 0.5f, 72f, 32f);
        DrawnStar(stars, "Star1", 0f, 0f, 32f, UIColors.Gold);
        DrawnStar(stars, "Star2", 40f, 0f, 32f, UIColors.Gold);
        stars.gameObject.SetActive(false);
        view.stars = stars;
        view.starsWidth = 80f;

        view.meta = Text(sheet, "Meta", 35f, 183f, 286f, 30.8f, "29 · Female · Mixed", 22f, UIColors.InkSoft);
        Highlighter(view.meta);

        // photo: 180 x 180, 2 px ink frame, hard 3,4 shadow; the portrait fills the frame's inside
        Box(sheet, "PhotoShadow", 348f, 35f, 180f, 180f, null, WithAlpha(UIColors.Ink, UIColors.SrgbAlpha(0.25f)));
        var frame = Node(sheet, "Photo", 345f, 31f, 180f, 180f);
        AddImage(frame.gameObject, null, UIColors.Ink);
        var inner = Node(frame, "Inner", 2f, 2f, 176f, 176f);
        AddImage(inner.gameObject, null, UIColors.PaperShade);
        inner.gameObject.AddComponent<RectMask2D>();
        var placeholder = Node(inner, "Placeholder", 0f, 0f, 176f, 176f);
        Box(placeholder, "Person", 13f, 176f + 18f - 150f, 150f, 150f, S("Kit/Special/cv_person"), UIColors.Pencil);
        Localize(Text(placeholder, "Label", 0f, 10f, 176f, 26.6f, "PHOTO", 19f, UIColors.Pencil, align: TextAlignmentOptions.Center, letterSpacingEm: 0.08f),
            L10nTable, "cv.photo");
        view.photoPlaceholder = placeholder.gameObject;
        view.photo = Box(inner, "Picture", 0f, 0f, 176f, 176f, null, Color.white);
        view.photo.enabled = false;

        view.clip = Box(sheet, "Clip", 345f + 24f, 31f - 20f, 26f, 64f, S("Kit/Special/cv_clip"), UIColors.Hex(0x7E838B), rotation: -6f);
        view.clipHighlight = Box(sheet, "ClipLight", 345f + 24f, 31f - 20f, 26f, 64f, S("Kit/Special/cv_clip_hi"), UIColors.Hex(0xE3C981), rotation: -6f);
        view.clipHighlight.gameObject.SetActive(false);

        // letterhead rule: 4 px + 3 px gap + 2 px (the top one 8 px gold on a golden applicant)
        view.ruleTop = Box(sheet, "RuleTop", 35f, 227.8f, 490f, 4f, null, UIColors.Ink).rectTransform;
        view.ruleBottom = Box(sheet, "RuleBottom", 35f, 234.8f, 490f, 2f, null, UIColors.Ink).rectTransform;
    }

    // Education / Work / Personality; the right 150 px stay free for pen marks. Rows are baseline-aligned like
    // the design's grid: a 19 px label sits 1.8 px lower than a 22 px value and 4.8 px lower than a 26 px one.
    static void BuildBody(RectTransform sheet, CVPaperView view)
    {
        var body = Node(sheet, "Body", 35f, 250.8f, 340f, 470f);
        view.body = body;
        var lines = new List<Image>();

        lines.Add(Heading(body, "Education", 0f, "EDUCATION", "cv.heading.education"));
        Label(body, "DegreeLabel", 31.8f + 1.813f, "Degree", "cv.label.degree");
        view.degree = Value(body, "Degree", 31.8f, 28.6f, "Master", 22f, false);
        Label(body, "MajorLabel", 62.4f + 1.813f, "Major", "cv.label.major");
        view.major = Value(body, "Major", 62.4f, 28.6f, "Economics", 22f, false);
        view.majorRowTop = 62.4f;
        view.majorWidth = 216f;

        var after = Node(body, "AfterMajor", 0f, 93f, 340f, 340f);
        view.afterMajor = after;
        Label(after, "GpaLabel", 4.847f, "GPA", "cv.label.gpa");
        view.gpa = Value(after, "Gpa", 0f, 32.5f, "3.62", 26f, true);
        Box(after, "Rule1", 0f, 46.5f, 340f, 2f, null, UIColors.Ink);

        lines.Add(Heading(after, "Work", 62.5f, "WORK", "cv.heading.work"));
        Label(after, "ExperienceLabel", 94.3f + 4.847f, "Experience", "cv.label.experience");
        view.experienceNumber = Value(after, "ExperienceNumber", 94.3f, 32.5f, "6", 26f, true);
        view.experienceUnit = Unit(after, "ExperienceUnit", 94.3f + 4.847f, "yrs");
        Label(after, "SalaryLabel", 128.8f + 4.847f, "Salary ask", "cv.label.salary");
        view.salaryNumber = Value(after, "SalaryNumber", 128.8f, 32.5f, "$84,000", 26f, true);
        view.salaryPer = Unit(after, "SalaryPer", 128.8f + 4.847f, "/ yr");
        Box(after, "Rule2", 0f, 175.3f, 340f, 2f, null, UIColors.Ink);

        lines.Add(Heading(after, "Personality", 191.3f, "PERSONALITY", "cv.heading.personality"));
        string[] sample = { "Fast Learner", "Addicted to Coffee", "Talks Too Much" };
        string[] icons = { "check", "wave", "cross" };
        Color[] colors = { UIColors.Money, UIColors.Info, UIColors.Loss };
        view.traits = new CVPaperView.TraitRow[3];
        for (int i = 0; i < 3; i++)
        {
            var row = Node(after, "Trait" + (i + 1), 0f, 223.1f + i * 34.8f, 340f, 30.8f);
            var icon = Icon(row, "Icon", 0f, 3.4f, 24f, icons[i], colors[i], "b");
            var label = Text(row, "Label", 34f, 0f, 306f, 30.8f, sample[i], 22f, UIColors.Ink);
            Highlighter(label);
            view.traits[i] = new CVPaperView.TraitRow { root = row.gameObject, icon = icon, label = label };
        }
        view.headingLines = lines.ToArray();

        view.applicantNumber = Text(sheet, "ApplicantNo", 35f, 686.4f, 320f, 26.6f, "Applicant No. 0421", 19f, UIColors.Pencil);
    }

    static Image Heading(RectTransform parent, string name, float top, string text, string key)
    {
        var head = Localize(Text(parent, name + "Head", 0f, top, 340f, 23.8f, text, 17f, UIColors.Ink, bold: true, letterSpacingEm: 0.14f),
            L10nTable, key);
        // the box hugs the heading (left edge kept), so the underline matches it in every language
        var rt = head.rectTransform;
        rt.pivot = new Vector2(0f, 0.5f);
        rt.sizeDelta = new Vector2(TextWidth(text, 17f, true, 0.14f), rt.sizeDelta.y);
        rt.anchoredPosition = new Vector2(0f, rt.anchoredPosition.y);
        Fit(head.gameObject, true, false);
        // tier 3+: underline 2 px #2B5BA8, 5 px under the baseline (17 px text in a 23.8 px line: baseline at 16.5)
        var line = NewRect(rt, name + "Line");
        line.anchorMin = new Vector2(0f, 0.5f);
        line.anchorMax = new Vector2(1f, 0.5f);
        line.sizeDelta = new Vector2(0f, 2f);
        line.anchoredPosition = new Vector2(0f, 23.8f * 0.5f - (16.51f + 5f + 1f));
        var img = AddImage(line.gameObject, null, UIColors.Info);
        img.gameObject.SetActive(false);
        return img;
    }

    static void Label(RectTransform parent, string name, float top, string text, string key)
    {
        Localize(Text(parent, name, 0f, top, 112f, 26.6f, text, 19f, UIColors.Pencil), L10nTable, key);
    }

    static TextMeshProUGUI Value(RectTransform parent, string name, float top, float lineBox, string text, float size, bool bold)
    {
        var t = Text(parent, name, 124f, top, 216f, lineBox, text, size, UIColors.Ink, bold: bold);
        Highlighter(t);
        return t;
    }

    static TextMeshProUGUI Unit(RectTransform parent, string name, float top, string text)
    {
        var t = Text(parent, name, 124f + 20f, top, 90f, 26.6f, text, 19f, UIColors.InkSoft);
        Highlighter(t);
        return t;
    }

    // IQ graded in red pen, the criminal record stamp, the golden seal (+ "fits the task" notes)
    static void BuildMargin(RectTransform sheet, CVPaperView view)
    {
        var iq = Node(sheet, "Iq", 385f, 249f, 150f, 176f);
        Localize(Text(iq, "Label", 4f, 0f, TextWidth("IQ", 26f, true), 26f, "IQ", 26f, UIColors.Loss, bold: true, rotation: -8f), L10nTable, "cv.iq");
        Box(iq, "Circle", 0f, 22f, 150f, 120f, S("Kit/Special/cv_pen_circle"), UIColors.Loss);
        view.iq = Text(iq, "Value", 0f, 22f + (120f - 58f) * 0.5f, 150f, 58f, "124", 58f, UIColors.Loss, bold: true, align: TextAlignmentOptions.Center);
        var hl = Highlighter(view.iq);
        hl.bandTop = 0.5f;
        hl.bandBottom = 0.92f;
        view.iqNote = FitsNote(iq, 0f, 150f);

        // record stamp: 24 px bold, 3 px border (radius 8) + 2 px outline 3 px out, -8 deg, 90 %
        float rw = TextWidth("RECORD", 24f, true, 0.1f) + 28f + 6f, rh = 2f * 25.2f + 12f + 6f;
        var rec = Node(sheet, "Record", 391f, 455f, rw, rh, rotation: -8f);
        rec.gameObject.AddComponent<CanvasGroup>().alpha = 0.9f;
        var outline = Box(rec, "Outline", -5f, -5f, rw + 10f, rh + 10f, S("Kit/r12_line2"), UIColors.Money);
        var border = StretchBox(rec, "Border", S("Kit/r8_line3"), UIColors.Money);
        // both lines are written by the view (CLEAN / HAS A over RECORD; other languages put the words the other way round)
        view.recordTop = Text(rec, "Top", 0f, 9f, rw, 25.2f, "CLEAN", 24f, UIColors.Money, bold: true, align: TextAlignmentOptions.Center, letterSpacingEm: 0.1f);
        view.recordBottom = Text(rec, "Bottom", 0f, 34.2f, rw, 25.2f, "RECORD", 24f, UIColors.Money, bold: true, align: TextAlignmentOptions.Center, letterSpacingEm: 0.1f);
        view.recordInk = new Graphic[] { outline, border, view.recordTop, view.recordBottom };
        view.recordNote = FitsNote(sheet, 391f + rw * 0.5f - 75f, 455f + rh + 16f);

        // golden seal: medallion with two ribbon tails, 8 deg
        var seal = Node(sheet, "Seal", 407f, 559f, 112f, 124f, rotation: 8f);
        Box(seal, "RibbonLeft", 30f, 70f, 22f, 52f, S("Kit/Special/cv_ribbon"), Color.white, rotation: 18f);
        Box(seal, "RibbonRight", 60f, 70f, 22f, 52f, S("Kit/Special/cv_ribbon"), Color.white, rotation: -18f);
        Box(seal, "MedalShadow", 6f - 15f, -15f + 6f, 130f, 130f, S("Kit/Special/cv_seal_shadow"), UIColors.Dark(0.25f));
        Box(seal, "Medal", 6f, 0f, 100f, 100f, S("Kit/Special/cv_seal"), Color.white);
        float colTop = 3f + (94f - (34f + 18.7f)) * 0.5f;
        DrawnStar(seal, "Star", 6f + 3f + (94f - 34f) * 0.5f, colTop, 34f, UIColors.PaperHi);
        // the medal's width at that height: longer wordings shrink (the box is a little taller than the line, so only width shrinks it)
        var odds = Localize(Text(seal, "Odds", 6f + 7f, colTop + 34f + 18.7f * 0.5f - 14f, 86f, 28f, "1 in 100", 17f, UIColors.Ink, bold: true,
            align: TextAlignmentOptions.Center), L10nTable, "cv.seal.odds");
        odds.enableAutoSizing = true;
        odds.fontSizeMin = 12f;
        odds.fontSizeMax = 17f;
        seal.gameObject.SetActive(false);
        view.seal = seal.gameObject;
        view.sealNote = FitsNote(sheet, 388f, 688f);
    }

    // "✓ fits the task" in green, centred in a 150 px column (a row that follows the wording's width)
    static GameObject FitsNote(RectTransform parent, float left, float top)
    {
        const string text = "fits the task";
        var note = Node(parent, "FitsNote", left, top, 150f, 23.8f);
        HLayout(note.gameObject, 4f, null, TextAnchor.MiddleCenter, true, false, false, false);
        LayoutIcon(note, "Check", 18f, "check", UIColors.Money, "b");
        var label = Localize(LayoutText(note, "Text", text, 17f, UIColors.Money), L10nTable, "cv.note.fits_task");
        label.rectTransform.sizeDelta = new Vector2(TextWidth(text, 17f), Mathf.Max(23.8f, 17f * UIFonts.ContentHeight(UIBuild.Font)));
        note.gameObject.SetActive(false);
        return note.gameObject;
    }

    // HIRED / SKIPPED: 6 px border (radius 12) + 3 px outline 5 px out, -14 deg, 88 %
    static void BuildStamps(RectTransform sheet, CVPaperView view)
    {
        view.hiredStamp = Stamp(sheet, "HiredStamp", 121f, 303f, "HIRED", "cv.stamp.hired", 72f, UIColors.Money);
        view.skippedStamp = Stamp(sheet, "SkippedStamp", 91f, 303f, "SKIPPED", "cv.stamp.skipped", 64f, UIColors.Loss);
    }

    static GameObject Stamp(RectTransform sheet, string name, float left, float top, string word, string key, float size, Color ink)
    {
        float line = size * 1.4f;
        float w = TextWidth(word, size, true, 0.12f) + 2f * (26f + 6f), h = line + 2f * (6f + 6f);
        var stamp = Node(sheet, name, left, top, w, h, rotation: -14f);
        stamp.gameObject.AddComponent<CanvasGroup>().alpha = 0.88f;
        // as wide as the word in every language (26 px padding + the 6 px border either side), around the same centre
        HLayout(stamp.gameObject, 0f, Pad(32, 32, 12, 12), TextAnchor.MiddleCenter, true, false, false, false);
        Fit(stamp.gameObject, true, false);
        IgnoreLayout(StretchBox(stamp, "Outline", S("Kit/r20_line3"), ink, -8f, -8f, -8f, -8f));
        IgnoreLayout(StretchBox(stamp, "Border", S("Kit/r12_line6"), ink));
        var label = Localize(LayoutText(stamp, "Word", word, size, ink, bold: true, align: TextAlignmentOptions.Center, letterSpacingEm: 0.12f),
            L10nTable, key);
        label.rectTransform.sizeDelta = new Vector2(w - 64f, line);
        stamp.gameObject.SetActive(false);
        return stamp.gameObject;
    }

    // A star drawn like the design's SVG: a coloured fill with a thin ink outline (no font glyph)
    static void DrawnStar(RectTransform parent, string name, float left, float top, float size, Color fill)
    {
        var star = Node(parent, name, left, top, size, size);
        var f = AddImage(star.gameObject, IconSprite("star", "fill"), fill);
        f.preserveAspect = true;
        var line = Icon(star, "Line", 0f, 0f, size, "star", UIColors.Ink, "t");
        line.preserveAspect = true;
    }

    static TextHighlighter Highlighter(TMP_Text t)
    {
        var hl = t.gameObject.AddComponent<TextHighlighter>();
        hl.color = Mark;
        hl.bandTop = 0.5f;
        hl.bandBottom = 0.92f;
        hl.paddingX = 6f;
        return hl;
    }

    // ------------------------------------------------------------------
    // MainGame: move the pile, point the paper at the shifted clips

    public static void Install(GameObject prefab, List<string> log)
    {
        Scene scene = SceneManager.GetSceneByPath(OfficeHudBuilder.ScenePath);
        if (!scene.IsValid() || !scene.isLoaded)
            scene = EditorSceneManager.OpenScene(OfficeHudBuilder.ScenePath, OpenSceneMode.Single);

        GameFlowManager flow = null;
        Camera cam = null;
        foreach (var r in scene.GetRootGameObjects())
        {
            if (flow == null) flow = r.GetComponentInChildren<GameFlowManager>(true);
            foreach (var c in r.GetComponentsInChildren<Camera>(true))
                if (cam == null || c.CompareTag("MainCamera")) cam = c;
        }
        if (flow == null || flow.paperSpawnPoint == null || cam == null)
        {
            log.Add("CV paper: MainGame has no GameFlowManager with a paper spawn point, or no camera; the pile was not moved.");
            return;
        }

        Transform spawn = flow.paperSpawnPoint;
        float tan = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float depth = PaperHeight / (2f * tan * (H / 1080f));
        float up = (540f - DesignCentre.y) / 540f * tan * depth;
        Transform ct = cam.transform;
        spawn.SetPositionAndRotation(ct.position + ct.forward * depth + ct.up * up, ct.rotation);
        EditorUtility.SetDirty(spawn);
        log.Add("MainGame: paper pile moved where the HUD design draws the CV (" + depth.ToString("0.000") + " m in front of the camera, facing it).");

        if (flow.paperPrefab != prefab)
        {
            flow.paperPrefab = prefab;
            EditorUtility.SetDirty(flow);
        }

        // the faces are drawn after post-processing, like the HUD (see the class summary)
        cam.cullingMask &= ~(1 << FaceLayer);
        EditorUtility.SetDirty(cam);
        GameObject passGo = null;
        foreach (var r in scene.GetRootGameObjects())
            if (r.name == FacePassName) passGo = r;
        if (passGo == null)
        {
            passGo = new GameObject(FacePassName);
            SceneManager.MoveGameObjectToScene(passGo, scene);
        }
        var volume = passGo.GetComponent<CustomPassVolume>();
        if (volume == null) volume = passGo.AddComponent<CustomPassVolume>();
        volume.isGlobal = true;
        volume.injectionPoint = CustomPassInjectionPoint.AfterPostProcess;
        volume.customPasses.Clear();
        var pass = volume.AddPassOfType<DrawRenderersCustomPass>();
        pass.name = "CV paper faces (UI layer, after post)";
        pass.layerMask = 1 << FaceLayer;
        pass.renderQueueType = CustomPass.RenderQueueType.All;
        pass.sortingCriteria = SortingCriteria.CommonTransparent;
        pass.overrideMode = DrawRenderersCustomPass.OverrideMaterialMode.None;
        pass.overrideMaterial = null;
        pass.targetColorBuffer = CustomPass.TargetBuffer.Camera;
        pass.targetDepthBuffer = CustomPass.TargetBuffer.Camera;
        pass.clearFlags = ClearFlag.None;
        EditorUtility.SetDirty(volume);
        log.Add("MainGame: the Main Camera leaves the UI layer to the '" + FacePassName + "' custom pass (after post-processing), so the CV keeps the design's paper white.");

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        log.Add("MainGame saved.");

        InstallClips(spawn.position, spawn.rotation, log);
    }

    static void InstallClips(Vector3 position, Quaternion rotation, List<string> log)
    {
        var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
        var hire = AssetDatabase.LoadAssetAtPath<AnimationClip>(HireClipPath);
        var skip = AssetDatabase.LoadAssetAtPath<AnimationClip>(SkipClipPath);
        if (controller == null || hire == null || skip == null)
        {
            log.Add("CV paper: the paper's controller or its Hire/Skip clips are missing; animations left as they are.");
            return;
        }
        Directory.CreateDirectory(AnimDir);
        var hireCv = ShiftedCopy(hire, AnimDir + "/HireCV.anim", position, rotation);
        var skipCv = ShiftedCopy(skip, AnimDir + "/SkipCV.anim", position, rotation);

        var ov = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(OverridePath);
        if (ov == null)
        {
            ov = new AnimatorOverrideController(controller);
            AssetDatabase.CreateAsset(ov, OverridePath);
        }
        ov.runtimeAnimatorController = controller;
        var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        ov.GetOverrides(pairs);
        for (int i = 0; i < pairs.Count; i++)
        {
            if (pairs[i].Key == hire) pairs[i] = new KeyValuePair<AnimationClip, AnimationClip>(hire, hireCv);
            else if (pairs[i].Key == skip) pairs[i] = new KeyValuePair<AnimationClip, AnimationClip>(skip, skipCv);
        }
        ov.ApplyOverrides(pairs);
        EditorUtility.SetDirty(ov);
        AssetDatabase.SaveAssets();

        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var anim = root.GetComponent<Animator>();
            if (anim != null)
            {
                anim.runtimeAnimatorController = ov;
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
        log.Add("CV paper: Hire/Skip play copies that start from the new pile (" + OverridePath + "); the original clips are unchanged.");
    }

    /// <summary>
    /// Copy of a clip whose root position and rotation start at the pile: the difference to the clip's first key
    /// is added in full at the start and fades out linearly, so the paper still lands in the tray / bin.
    /// </summary>
    static AnimationClip ShiftedCopy(AnimationClip src, string path, Vector3 position, Quaternion rotation)
    {
        var dst = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        bool create = dst == null;
        if (create) dst = new AnimationClip();
        EditorUtility.CopySerialized(src, dst);
        dst.name = Path.GetFileNameWithoutExtension(path);
        if (create) AssetDatabase.CreateAsset(dst, path);

        float length = Mathf.Max(0.01f, src.length);
        Vector3 euler = rotation.eulerAngles;
        foreach (var b in AnimationUtility.GetCurveBindings(dst))
        {
            if (b.path != "" || b.type != typeof(Transform)) continue;
            float target;
            bool angle = false;
            switch (b.propertyName)
            {
                case "m_LocalPosition.x": target = position.x; break;
                case "m_LocalPosition.y": target = position.y; break;
                case "m_LocalPosition.z": target = position.z; break;
                case "localEulerAnglesRaw.x": target = euler.x; angle = true; break;
                case "localEulerAnglesRaw.y": target = euler.y; angle = true; break;
                case "localEulerAnglesRaw.z": target = euler.z; angle = true; break;
                default: continue;
            }
            var curve = AnimationUtility.GetEditorCurve(dst, b);
            if (curve == null || curve.length == 0) continue;
            var keys = curve.keys;
            float start = keys[0].value;
            float delta = angle ? Mathf.DeltaAngle(start, target) : target - start;
            for (int i = 0; i < keys.Length; i++)
            {
                keys[i].value += delta * (1f - Mathf.Clamp01(keys[i].time / length));
                keys[i].inTangent -= delta / length;
                keys[i].outTangent -= delta / length;
            }
            curve.keys = keys;
            AnimationUtility.SetEditorCurve(dst, b, curve);
        }
        EditorUtility.SetDirty(dst);
        return dst;
    }

    // ------------------------------------------------------------------
    // Restore the old paper (texts on the box, old pile, original clips)

    public static void Restore(List<string> log)
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var cube = root.transform.Find("Cube");
            if (cube != null)
            {
                var face = cube.Find("CVFace");
                if (face != null) Object.DestroyImmediate(face.gameObject);
                foreach (Transform child in cube) child.gameObject.SetActive(true);
                cube.localScale = OldCubeScale;
                cube.localPosition = OldCubePosition;
            }
            var binder = root.GetComponent<ApplicantUIBinder>();
            if (binder != null) binder.cvView = null;
            var anim = root.GetComponent<Animator>();
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
            if (anim != null && controller != null) anim.runtimeAnimatorController = controller;
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            log.Add("CV paper: old face, size and animations restored on " + PrefabPath + ".");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        Scene scene = SceneManager.GetSceneByPath(OfficeHudBuilder.ScenePath);
        if (!scene.IsValid() || !scene.isLoaded)
            scene = EditorSceneManager.OpenScene(OfficeHudBuilder.ScenePath, OpenSceneMode.Single);
        foreach (var r in scene.GetRootGameObjects())
        {
            if (r.name == FacePassName)
            {
                Object.DestroyImmediate(r);
                log.Add("MainGame: removed the '" + FacePassName + "' custom pass.");
                continue;
            }
            foreach (var c in r.GetComponentsInChildren<Camera>(true))
            {
                if (!c.CompareTag("MainCamera")) continue;
                c.cullingMask |= 1 << FaceLayer;
                EditorUtility.SetDirty(c);
            }
            var flow = r.GetComponentInChildren<GameFlowManager>(true);
            if (flow == null || flow.paperSpawnPoint == null) continue;
            flow.paperSpawnPoint.SetPositionAndRotation(OldSpawnPosition, OldSpawnRotation);
            EditorUtility.SetDirty(flow.paperSpawnPoint);
            log.Add("MainGame: paper pile back at its old spot.");
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    // ------------------------------------------------------------------
    // Previews

    public struct SheetState
    {
        public int sample;
        public int tier;
        public CVPaperView.Field highlight;
        public CVPaperView.Stamp stamp;
        public string trait;
        public bool golden;
        public bool noPhoto;
    }

    public static SheetState State(int sample, int tier = 0, CVPaperView.Field highlight = CVPaperView.Field.None,
        CVPaperView.Stamp stamp = CVPaperView.Stamp.None, bool golden = false, string trait = null, bool noPhoto = false)
    {
        return new SheetState { sample = sample, tier = tier, highlight = highlight, stamp = stamp, golden = golden, trait = trait, noPhoto = noPhoto };
    }

    public static void RenderPreviews(GameObject prefab, string dir, List<string> log)
    {
        var F = CVPaperView.Field.None;
        RenderSheets(prefab, Path.Combine(dir, "cv_states.png"), log,
            State(0, highlight: CVPaperView.Field.Iq), State(1, golden: true), State(2, highlight: CVPaperView.Field.Record));
        RenderSheets(prefab, Path.Combine(dir, "cv_stamps.png"), log,
            State(0, stamp: CVPaperView.Stamp.Hired), State(2, stamp: CVPaperView.Stamp.Skipped),
            State(0, highlight: CVPaperView.Field.Trait, trait: "Coffee", noPhoto: true));
        RenderSheets(prefab, Path.Combine(dir, "cv_tiers_a.png"), log,
            State(0, tier: 1, highlight: CVPaperView.Field.Gender), State(0, tier: 2, highlight: CVPaperView.Field.Major), State(0, tier: 3, highlight: CVPaperView.Field.Salary));
        RenderSheets(prefab, Path.Combine(dir, "cv_tiers_b.png"), log,
            State(0, tier: 4, highlight: CVPaperView.Field.Experience), State(0, tier: 5, highlight: F), State(1, tier: 6, golden: true, highlight: CVPaperView.Field.Golden));

        // in the office, through the Main Camera (HDRP), with the HUD (last: these open MainGame without saving it)
        ScenePreview.RenderDesk(Path.Combine(dir, "desk_cv.png"), true, log, papers =>
            papers[0].GetComponentInChildren<CVPaperView>(true).ShowPreview(ScenePreview.Sample(0), 0, CVPaperView.Field.Iq, 421));
        ScenePreview.RenderDesk(Path.Combine(dir, "desk_cv_golden.png"), true, log, papers =>
        {
            papers[0].GetComponentInChildren<CVPaperView>(true).ShowPreview(ScenePreview.Sample(1), 0, CVPaperView.Field.None, 433);
            papers[1].GetComponentInChildren<CVPaperView>(true).ShowPreview(ScenePreview.Sample(0), 0, CVPaperView.Field.None, 434);
        });
        ScenePreview.RenderDesk(Path.Combine(dir, "desk_cv_hired.png"), false, log, papers =>
            papers[0].GetComponentInChildren<CVPaperView>(true).ShowPreview(ScenePreview.Sample(2), 0, CVPaperView.Field.None, 440, CVPaperView.Stamp.Hired));
    }

    /// <summary>Up to three sheets side by side, drawn flat (the face's canvas switched to screen space).</summary>
    public static void RenderSheets(GameObject prefab, string png, List<string> log, params SheetState[] states)
    {
        UIPreview.RenderWith(png, log, cam =>
        {
            var made = new List<GameObject>();
            for (int i = 0; i < states.Length; i++)
            {
                var paper = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                made.Add(paper);
                var view = paper.GetComponentInChildren<CVPaperView>(true);
                var canvas = view.GetComponent<Canvas>();
                canvas.transform.SetParent(null, false);
                made.Add(canvas.gameObject);
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = cam;
                canvas.planeDistance = 10f - i;
                float x = states.Length == 1 ? 0f : (i - (states.Length - 1) * 0.5f) * 620f;
                view.sheet.anchoredPosition = new Vector2(x, 0f);

                var s = states[i];
                var a = ScenePreview.Sample(s.sample);
                if (s.golden) a.isGolden = true;
                if (s.noPhoto) a.photo = null;
                view.ShowPreview(a, s.tier, s.highlight, 421 + s.sample * 12, s.stamp, s.trait);
            }
            return made;
        });
    }
}
