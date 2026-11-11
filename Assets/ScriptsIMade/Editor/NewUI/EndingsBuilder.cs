using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static UIBuild;

/// <summary>
/// The endings (page "Pause &amp; endings" and the Online page's $1B board): "Game over: bankrupt", "Win: you made it ($10B)"
/// and "$1B: online world unlocked". Builds Assets/UI/Prefabs/RunEndScreens.prefab with the designed screens filling the
/// run-end views' references (GameOverView, WinView: RunEndDirector finds them in the scene instead of building its
/// runtime ones) and the BillionCertificateView, and installs it in MainGame. Coordinates are the boards' 1920 x 1080.
/// </summary>
public static class EndingsBuilder
{
    public const string PrefabPath = "Assets/UI/Prefabs/RunEndScreens.prefab";
    /// <summary>The Menu table (endings.*) holds the labels the RunEnd table (the runtime screens' texts) doesn't have.</summary>
    const string Table = "Menu";

    static readonly Color Desk = UIColors.Hex(0x2A332F);
    static readonly Color Pale = UIColors.Hex(0xF2CF6B);

    // CSS radius families
    static readonly Vector4 CardX = new Vector4(14, 20, 12, 22), CardY = new Vector4(20, 12, 22, 14);
    static readonly Vector4 SmallX = new Vector4(10, 14, 9, 15), SmallY = new Vector4(14, 9, 15, 10);

    static Sprite White => S("Kit/Special/white");
    static Material heavy;

    // ------------------------------------------------------------------
    // Menu / batch

    [MenuItem("Tools/Game/New UI/Build Endings")]
    public static void BuildFromMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Build Endings", "Stop Play mode first.", "OK");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var log = new List<string>();
        var prefab = BuildPrefab(log);
        if (prefab != null) Install(prefab, log);
        Debug.Log("[EndingsBuilder]\n" + string.Join("\n", log));
        EditorUtility.DisplayDialog("Endings", string.Join("\n", log), "OK");
    }

    [MenuItem("Tools/Game/New UI/Restore Runtime Endings")]
    public static void RestoreFromMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var log = new List<string>();
        Restore(log);
        Debug.Log("[EndingsBuilder]\n" + string.Join("\n", log));
        EditorUtility.DisplayDialog("Endings", string.Join("\n", log), "OK");
    }

    /// <summary>Unity.exe -batchmode -executeMethod EndingsBuilder.BatchRun [-install] [-preview folder]</summary>
    public static void BatchRun()
    {
        int code = 0;
        var log = new List<string>();
        try
        {
            UIPreview.EnsureProjectPipeline(log);
            var args = Environment.GetCommandLineArgs();
            bool install = Array.IndexOf(args, "-install") >= 0;
            int p = Array.IndexOf(args, "-preview");
            string dir = p >= 0 && p + 1 < args.Length ? args[p + 1] : null;
            // -company "Name" for the previews; \uXXXX escapes reach letters a shell would mangle (e.g. Turkish ones)
            int n = Array.IndexOf(args, "-company");
            string company = n >= 0 && n + 1 < args.Length ? System.Text.RegularExpressions.Regex.Unescape(args[n + 1]) : PreviewCompany;
            var prefab = BuildPrefab(log);
            if (prefab == null) code = 1;
            if (prefab != null && install) Install(prefab, log);
            if (prefab != null && dir != null)
                RenderPreviews(AssetDatabase.LoadAssetAtPath<GameObject>(OfficeHudBuilder.PrefabPath), prefab, dir, log, company);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            code = 1;
        }
        finally
        {
            try { UIPreview.EnsureProjectPipeline(log); } catch (Exception e) { Debug.LogException(e); }
        }
        Debug.Log("[EndingsBuilder]\n" + string.Join("\n", log));
        EditorApplication.Exit(code);
    }

    // ------------------------------------------------------------------
    // Prefab

    public static GameObject BuildPrefab(List<string> log)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
        heavy = UIFonts.DilateMaterial("Masthead", 0.1f);
        UIFonts.RefreshDerivedMaterials();   // glows, Pop, Heavy: same weights as the font
        AssetDatabase.SaveAssets();

        GameObject root = null;
        try
        {
            root = new GameObject("RunEndScreens", typeof(RectTransform));
            root.layer = 5;
            var rt = (RectTransform)root.transform;
            rt.sizeDelta = new Vector2(1920f, 1080f);

            var over = Stretch(rt, "GameOverScreen");
            BuildGameOver(over, over.gameObject.AddComponent<GameOverView>());
            var win = Stretch(rt, "WinScreen");
            BuildWin(win, win.gameObject.AddComponent<WinView>());
            var cert = Stretch(rt, "BillionCertificate");
            BuildCertificate(cert, cert.gameObject.AddComponent<BillionCertificateView>());

            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 130;   // above every other panel, below the toasts (900)
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            root.AddComponent<GraphicRaycaster>();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool ok);
            log.Add(ok ? "Saved " + PrefabPath + " (game over, win, $1B certificate)" : "Could not save " + PrefabPath);
            return ok ? prefab : null;
        }
        finally
        {
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
        }
    }

    // ------------------------------------------------------------------
    // Game over: a 900 x 840 green-bar ledger sheet at 510, 120 (padding 212 66 0, rows of 34 on the bands)

    static readonly string[] RowNames =
    {
        "Days in business", "Peak value", "People hired", "Risk cards played", "Biggest win", "Biggest loss", "Crises solved", "Casino net"
    };
    // their RunEnd entries (GameOverView's runtime sheet uses the same)
    static readonly string[] RowKeys =
    {
        "runend.gameover.row.days", "runend.gameover.row.peak", "runend.gameover.row.hires", "runend.gameover.row.cards",
        "runend.gameover.row.biggest_win", "runend.gameover.row.biggest_loss", "runend.gameover.row.crises", "runend.gameover.row.casino"
    };

    static void BuildGameOver(RectTransform screen, GameOverView view)
    {
        var root = Stretch(screen, "GameOver");
        view.root = root;
        StretchBox(root, "Scrim", null, UIColors.Scrim).raycastTarget = true;

        const float L = 510f, T = 120f, W = 900f, H = 840f;
        Shadow(root, "SheetShadow", L, T, W, H, 0f, 22f, 48f, 0.45f);
        var sheet = Node(root, "Sheet", L, T, W, H);
        var paper = StretchBox(sheet, "Paper", S("Kit/panel_fill"), UIColors.Ledger);
        paper.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        // green bars from the inner top: 13 whole tiles (Unity tiles from the bottom, so the top starts in phase)
        var bands = Box(paper.transform, "Bands", 3f, 3f, W - 6f, 13f * 68f, S("Kit/Tiles/ledger_band68"), Color.white);
        bands.type = Image.Type.Tiled;
        TractorFeed(paper.transform, "FeedLeft", 3f, 3f, H - 6f, true);
        TractorFeed(paper.transform, "FeedRight", W - 3f - 28f, 3f, H - 6f, false);
        StretchBox(sheet, "Border", S("Kit/panel_line3"), UIColors.Ink);

        // the padding box (inside the 3 px border)
        var c = Node(sheet, "Content", 3f, 3f, W - 6f, H - 6f);
        const float inner = 894f, padX = 66f, content = 762f;

        // BANKRUPT: 92 px bold, 0.1 em, padding 4 23 4 32, 7 px border + 3 px outline 6 px out, -8 deg, 88 %
        float sw = 7f * 2f + 32f + 23f + TextWidth("BANKRUPT", 92f, true, 0.1f);
        Stamp(c, "Bankrupt", (inner - sw) * 0.5f, 53f, sw, 7f * 2f + 8f + 92f, "BANKRUPT", 92f, UIColors.Loss, 7f, 12f, 3f, 6f, 0.1f,
            -8f, 0.88f, 32f, 23f, RunEndUI.Table, "runend.gameover.stamp");

        // header: 44 px title + 12 + 26 px line, centred in a 168 px block at 212
        float top = 212f + (168f - (44f * 1.15f + 12f + 26f * 1.4f)) * 0.5f;
        var head = Text(c, "Headline", padX, top, content, 44f * 1.15f, "Nova Holdings is out of business.", 44f, UIColors.Ink, bold: true,
            align: TextAlignmentOptions.Center);
        head.enableAutoSizing = true;
        head.fontSizeMin = 28f;
        head.fontSizeMax = 44f;
        view.headlineText = head;
        // "Was gambling bad for business? The ledger says yes.": question and answer bound to their entries, centred as one
        // line; the answer in ink on a highlighter band that follows its words (CSS: 52-90 % of the line, 6 px either side)
        var tag = Node(c, "Tagline", padX, top + 44f * 1.15f + 12f, content, 26f * 1.4f);
        HLayout(tag.gameObject, 9f, null, TextAnchor.MiddleCenter, true, true, false, false);
        Localize(LayoutText(tag, "Question", "Was gambling bad for business?", 26f, UIColors.InkSoft), Table, "endings.gameover.tagline.question");
        var answer = NewRect(tag, "Answer");
        HLayout(answer.gameObject, 0f, Pad(6, 6, 0, 0), TextAnchor.MiddleCenter, true, true, false, false);
        var band = StretchBox(answer, "Band", null, UIColors.WithAlpha(UIColors.Highlight, 0.95f));
        band.rectTransform.anchorMin = new Vector2(0f, 0.10f);
        band.rectTransform.anchorMax = new Vector2(1f, 0.48f);
        IgnoreLayout(band);
        Localize(LayoutText(answer, "Text", "The ledger says yes.", 26f, UIColors.Ink), RunEndUI.Table, "runend.gameover.tagline.answer");

        // final statement: label | note (auto column) | 200 px value column with a 2 px rule, 16 px apart
        float y0 = 212f + 168f + 28f;
        view.rowLabels = new TMP_Text[GameOverView.RowCount];
        view.rowNotes = new TMP_Text[GameOverView.RowCount];
        view.rowValues = new TMP_Text[GameOverView.RowCount];
        for (int i = 0; i < GameOverView.RowCount; i++)
        {
            float y = y0 + i * 34f;
            view.rowLabels[i] = Localize(Text(c, "Label" + (i + 1), padX, y, 300f, 34f, RowNames[i], 22f, UIColors.Ink), RunEndUI.Table, RowKeys[i]);
            var note = Text(c, "Note" + (i + 1), padX + 250f, y, 296f, 34f, "", 19f, UIColors.Pencil, align: TextAlignmentOptions.Right);
            note.overflowMode = TextOverflowModes.Ellipsis;
            view.rowNotes[i] = note;
            Box(c, "Rule" + (i + 1), padX + content - 200f, y, 2f, 34f, null, UIColors.LedgerLine);
            var value = Text(c, "Value" + (i + 1), padX + content - 200f + 18f, y, 182f, 34f, "", 24f, UIColors.Ink, bold: true,
                align: TextAlignmentOptions.Right);
            value.enableAutoSizing = true;
            value.fontSizeMin = 14f;
            value.fontSizeMax = 24f;
            view.rowValues[i] = value;
        }

        // Start a new company | Main menu, centred, 24 apart
        float by = y0 + GameOverView.RowCount * 34f + 28f;
        float w1 = ButtonWidth("Start a new company", "refresh", 28f, true, RunEndUI.Table, "runend.button.new_company");
        float w2 = ButtonWidth("Main menu", "home", 28f, false, RunEndUI.Table, "runend.button.main_menu");
        float bx = (inner - (w1 + 24f + w2)) * 0.5f;
        view.newCompanyButton = PaperButton(c, "NewCompany", bx, by, w1, 68f, "Start a new company", "refresh", 28f, true, UIColors.Highlight,
            new Vector2(5f, 6f), RunEndUI.Table, "runend.button.new_company");
        view.mainMenuButton = PaperButton(c, "MainMenu", bx + w1 + 24f, by, w2, 68f, "Main menu", "home", 28f, false, UIColors.PaperHi,
            new Vector2(4f, 5f), RunEndUI.Table, "runend.button.main_menu");

        root.gameObject.SetActive(false);
    }

    /// <summary>A 26 px tractor-feed strip (holes every 34 px) with its 2 px perforation on the paper side.</summary>
    static void TractorFeed(Transform paper, string name, float left, float top, float height, bool leftSide)
    {
        var strip = Node(paper, name, left, top, 28f, height);
        var perf = Box(strip, "Perforation", leftSide ? 26f : 0f, 0f, 2f, height, S("Kit/Tiles/dash_v2"), UIColors.LedgerLine);
        perf.type = Image.Type.Tiled;
        float cx = leftSide ? 13f : 2f + 13f;
        for (int i = 0; i * 34f < height; i++)
            Box(strip, "Hole" + i, cx - 6f, 17f + i * 34f - 6f, 12f, 12f, S("Kit/Circles/circle12_fill"), Desk);
    }

    // ------------------------------------------------------------------
    // Win: the HIGH ROLLER cover (700 x 940 at 250, 70, -2 deg), the result sheet (620 x 616 at 1040, 236), confetti

    static readonly float[,] WinConfetti =
    {
        { 58, 64, 18, 32, -24, 0 }, { 156, 150, 16, 28, 38, 1 }, { 40, 262, 22, 14, 12, 2 }, { 128, 356, 14, 30, -52, 3 },
        { 70, 470, 20, 20, 30, 4 }, { 168, 566, 16, 32, 64, 1 }, { 34, 660, 22, 16, -18, 0 }, { 120, 770, 14, 28, 22, 2 },
        { 182, 872, 18, 30, -40, 1 }, { 60, 968, 20, 14, 48, 3 },
        { 1000, 40, 16, 30, 48, 3 }, { 1096, 150, 20, 14, -10, 4 }, { 1188, 70, 18, 32, 26, 1 }, { 1290, 176, 14, 26, -70, 0 },
        { 1382, 44, 22, 16, -34, 2 }, { 1478, 132, 16, 30, 16, 1 }, { 1576, 60, 20, 20, 44, 3 }, { 1668, 168, 14, 28, -60, 4 },
        { 1760, 40, 18, 34, 20, 1 }, { 1858, 124, 22, 14, -8, 0 },
        { 1712, 284, 16, 30, 70, 1 }, { 1846, 372, 18, 26, -28, 2 }, { 1734, 470, 20, 16, 34, 3 }, { 1862, 574, 14, 30, -14, 1 },
        { 1700, 680, 22, 14, 52, 0 }, { 1830, 790, 16, 28, -36, 4 },
        { 1070, 932, 14, 26, -70, 1 }, { 1188, 1020, 18, 30, 18, 0 }, { 1316, 900, 14, 24, -20, 1 }, { 1444, 996, 20, 14, 10, 2 },
        { 1570, 920, 16, 30, 40, 3 }, { 1706, 1010, 18, 18, -30, 4 }, { 1834, 930, 16, 32, 58, 1 },
        { 996, 600, 16, 26, 28, 0 }, { 1004, 780, 14, 22, -44, 1 },
        { 420, 14, 18, 28, 32, 1 }, { 760, 10, 20, 14, -22, 2 }, { 470, 1040, 16, 28, -16, 0 }, { 846, 1036, 20, 16, 40, 1 }
    };

    static void BuildWin(RectTransform screen, WinView view)
    {
        var root = Stretch(screen, "Win");
        view.root = root;
        StretchBox(root, "Scrim", null, UIColors.Scrim).raycastTarget = true;
        BuildCover(root, view);
        BuildWinSheet(root, view);
        view.confetti = Confetti(root, WinConfetti);
        root.gameObject.SetActive(false);
    }

    static void BuildCover(RectTransform root, WinView view)
    {
        var cover = Node(root, "Cover", 250f, 70f, 700f, 940f, rotation: -2f);
        Shadow(cover, "Shadow", 0f, 0f, 700f, 940f, 0f, 22f, 48f, 0.45f);
        StretchBox(cover, "Paper", S("Kit/small_fill"), UIColors.PaperHi);
        StretchBox(cover, "Border", S("Kit/small_line3"), UIColors.Ink);
        var c = Node(cover, "Content", 3f, 3f, 694f, 934f);

        // gold frame: 3 px gold between 1 px dark-gold lines
        Ring(c, "FrameOuter", 13f, 13f, 668f, 908f, UIColors.GoldDark, 1f, 5f);
        Ring(c, "Frame", 14f, 14f, 666f, 906f, UIColors.Gold, 3f, 4f);
        Ring(c, "FrameInner", 17f, 17f, 660f, 900f, UIColors.GoldDark, 1f, 1f);

        view.issueText = Text(c, "Issue", 36f, 30f, 622f, 24f, "Day 164 issue · $50,000 to start", 19f, UIColors.Pencil,
            align: TextAlignmentOptions.Center, letterSpacingEm: 0.06f);

        // masthead: 100 px bold, stroked (heavier) and stretched to 614 px from x 40, baseline at 146. Other languages keep the
        // stretch: a longer title shrinks to the box (down to 50 px), a shorter one sits in its middle
        float natural = TextWidth("HIGH ROLLER", 100f, true);
        var mast = Localize(Text(c, "Masthead", 40f, 0f, natural + 4f, 160f, "HIGH ROLLER", 100f, UIColors.Ink, bold: true,
            align: TextAlignmentOptions.Top), RunEndUI.Table, "runend.win.masthead");
        mast.enableAutoSizing = true;
        mast.fontSizeMin = 50f;
        mast.fontSizeMax = 100f;
        var face = UIBuild.Font.faceInfo;
        float ascent = face.ascentLine / face.pointSize * 100f;
        var mr = mast.rectTransform;
        mr.anchorMin = mr.anchorMax = new Vector2(0f, 1f);
        mr.pivot = new Vector2(0f, 1f);
        mr.sizeDelta = new Vector2(natural + 4f, 160f);
        mr.anchoredPosition = new Vector2(40f, -(54f + 92f - ascent));
        mr.localScale = new Vector3(614f / Mathf.Max(1f, natural), 1f, 1f);
        if (heavy != null) mast.fontSharedMaterial = heavy;

        Box(c, "RuleInk", 36f, 158f, 622f, 10f, null, UIColors.Ink);
        Box(c, "RuleGold", 36f, 160f, 622f, 6f, null, UIColors.Gold);
        Localize(Text(c, "Tagline", 36f, 176f, 622f, 26f, "The magazine for people who bet the company.", 19f, UIColors.InkSoft,
            align: TextAlignmentOptions.Center), RunEndUI.Table, "runend.win.tagline");

        // cover photo placeholder with the CEO
        Box(c, "Photo", 36f, 218f, 622f, 616f, S("Kit/small_fill"), UIColors.WithAlpha(UIColors.Ink, UIColors.SrgbAlpha(0.04f)));
        Dashed(Box(c, "PhotoBorder", 36f, 218f, 622f, 616f, S("Kit/small_dash3"), UIColors.Pencil));
        Box(c, "Person", 400f, 330f, 180f, 180f, S("Kit/Special/cover_person"), UIColors.Pencil).type = Image.Type.Simple;
        Localize(Text(c, "PhotoCaption", 380f, 520f, 220f, 26f, "Cover photo: the CEO", 19f, UIColors.Pencil, align: TextAlignmentOptions.Center),
            RunEndUI.Table, "runend.win.photo_caption");

        // five cover lines (22 px on 30 px lines, 18 apart); highlighter or underline on each one's accent
        var list = Node(c, "CoverLines", 54f, 240f, 310f, 380f);
        VLayout(list.gameObject, 18f, null, TextAnchor.UpperLeft, true, true, true, false);
        string[] samples =
        {
            "From a $50,000 desk in <link=\"hl\">164 days</link>",
            "386 hires, <u color=#E2B13C>4 of them golden</u>",
            "Biggest win: Cure Discovery, <link=\"hl\">+$55K</link>",
            "The casino: +$2.1M <link=\"hl\">(don't ask)</link>",
            "<u color=#1D2433>18 of 27 crises</u> handled"
        };
        view.coverLines = new TMP_Text[WinView.CoverLineCount];
        for (int i = 0; i < WinView.CoverLineCount; i++)
        {
            var line = LayoutText(list, "Line" + (i + 1), samples[i], 22f, UIColors.Ink, lineHeight: 30f / 22f, wrap: true);
            // every accent keeps to one line (white-space: nowrap on the board's marks)
            if (i == 0) Highlighter(line, UIColors.Highlight, 0.95f).keepTogether = true;
            if (i == 2) Highlighter(line, UIColors.Hex(0x7ED68E), 0.85f).keepTogether = true;
            if (i == 3) Highlighter(line, UIColors.Hex(0xFF96AA), 0.85f).keepTogether = true;
            if (i == 1 || i == 4)
            {
                var ul = line.gameObject.AddComponent<TextUnderline>();
                ul.thickness = i == 1 ? 5f : 3f;
                ul.offset = 6f;
                ul.color = i == 1 ? UIColors.Gold : UIColors.Ink;
                ul.keepTogether = true;
            }
            view.coverLines[i] = line;
        }

        // NOVA HOLDINGS / HITS / $10 BILLION (highlighted 14-88 %, padding 14, tilted -1.5 deg about its left edge)
        var company = Text(c, "Company", 54f, 610f, 604f, 66f, "NOVA HOLDINGS", 60f, UIColors.Ink, bold: true);
        company.enableAutoSizing = true;
        company.fontSizeMin = 30f;
        company.fontSizeMax = 60f;
        view.coverCompanyText = company;
        Localize(Text(c, "Hits", 54f, 676f, 604f, 44f, "HITS", 40f, UIColors.Ink, bold: true), RunEndUI.Table, "runend.win.cover.hits");
        var amount = Text(c, "Amount", 68f, 720f, 576f, 92f, "<link=\"hl\">$10 BILLION</link>", 84f, UIColors.Ink, bold: true);
        amount.enableAutoSizing = true;
        amount.fontSizeMin = 48f;
        amount.fontSizeMax = 84f;
        var ar = amount.rectTransform;
        Vector2 center = ar.anchoredPosition;
        ar.pivot = new Vector2(0f, 0.5f);
        ar.anchoredPosition = center - new Vector2(ar.sizeDelta.x * 0.5f, 0f);
        ar.localEulerAngles = new Vector3(0f, 0f, 1.5f);
        // the band covers 14-88 % of the 92 px line box; TextHighlighter measures the font's content area (1.515 em)
        float contentH = 84f * UIFonts.ContentHeight(UIBuild.Font), off = (contentH - 92f) * 0.5f;
        var band = Highlighter(amount, UIColors.Highlight, 0.95f);
        band.bandTop = (off + 0.14f * 92f) / contentH;
        band.bandBottom = (off + 0.88f * 92f) / contentH;
        band.paddingX = 14f;
        view.coverAmountText = amount;

        // strip on felt between gold lines
        var strip = Node(c, "Strip", 18f, 858f, 658f, 58f);
        StretchBox(strip, "Felt", null, UIColors.Felt);
        Box(strip, "GoldTop", 0f, 0f, 658f, 3f, null, UIColors.Gold);
        Box(strip, "GoldBottom", 0f, 55f, 658f, 3f, null, UIColors.Gold);
        var words = Stretch(strip, "Words");
        HLayout(words.gameObject, 10f, null, TextAnchor.MiddleCenter, true, true, false, false);
        Localize(LayoutText(words, "Question", "IS GAMBLING BAD FOR BUSINESS?", 19f, UIColors.PaperHi, bold: true, letterSpacingEm: 0.08f),
            Table, "endings.win.strap.question");
        Localize(LayoutText(words, "Answer", "NOT FOR THEM.", 19f, UIColors.Highlight, bold: true, letterSpacingEm: 0.08f),
            RunEndUI.Table, "runend.win.strap.answer");

        // CEO OF THE YEAR rosette over the photo's corner
        var seal = Node(c, "Seal", 548f, 206f, 190f, 230f, rotation: -6f);
        StretchBox(seal, "Rosette", S("Kit/Special/rosette_ceo"), Color.white).type = Image.Type.Simple;
        Localize(Text(seal, "CEO", 0f, 47f, 190f, 34f, "CEO", 32f, UIColors.Ink, bold: true, align: TextAlignmentOptions.Center),
            RunEndUI.Table, "runend.win.seal.line1");
        Localize(Text(seal, "OfThe", 0f, 81f, 190f, 20f, "OF THE", 17f, UIColors.Ink, bold: true, align: TextAlignmentOptions.Center, letterSpacingEm: 0.1f),
            RunEndUI.Table, "runend.win.seal.line2");
        Localize(Text(seal, "Year", 0f, 101f, 190f, 28f, "YEAR", 24f, UIColors.Ink, bold: true, align: TextAlignmentOptions.Center),
            RunEndUI.Table, "runend.win.seal.line3");
    }

    static void BuildWinSheet(RectTransform root, WinView view)
    {
        const float L = 1040f, T = 236f, W = 620f, H = 616f;
        Shadow(root, "SheetShadow", L, T, W, H, 0f, 22f, 48f, 0.45f);
        var sheet = Node(root, "Sheet", L, T, W, H);
        StretchBox(sheet, "Paper", S("Kit/panel_fill"), UIColors.Paper);
        StretchBox(sheet, "Border", S("Kit/panel_line3"), UIColors.Ink);
        var c = Node(sheet, "Content", 3f, 3f, W - 6f, H - 6f);
        const float x = 48f, w = 518f;

        Localize(Text(c, "Title", x, 44f, w, 69f, "You made it.", 60f, UIColors.Ink, bold: true), RunEndUI.Table, "runend.win.title");
        var worth = Text(c, "Worth", x, 125f, w, 36f, "Nova Holdings is worth", 26f, UIColors.Ink);
        worth.enableAutoSizing = true;
        worth.fontSizeMin = 18f;
        worth.fontSizeMax = 26f;
        view.worthText = worth;
        var value = Text(c, "WorthValue", x + 6f, 161f, w - 6f, 36f, "<link=\"hl\">$10,000,000,000</link>.", 26f, UIColors.Ink);
        value.enableAutoSizing = true;
        value.fontSizeMin = 18f;
        value.fontSizeMax = 26f;
        Highlighter(value, UIColors.Highlight, 0.95f);
        view.worthValueText = value;
        Localize(Text(c, "KeepGoing", x, 209f, w, 27f, "The run keeps going if you want.", 19f, UIColors.Pencil), RunEndUI.Table, "runend.win.keep_going");
        Localize(Text(c, "YourCompany", x, 236f, w, 27f, "It's your company.", 19f, UIColors.Pencil), RunEndUI.Table, "runend.win.your_company");

        view.keepPlayingButton = PaperButton(c, "KeepPlaying", x, 295f, 420f, 80f, "Keep playing", "play", 32f, true, UIColors.Highlight,
            new Vector2(5f, 6f), RunEndUI.Table, "runend.win.button.keep_playing");
        view.newCompanyButton = PaperButton(c, "NewCompany", x, 399f, 420f, 68f, "Start a new company", "refresh", 28f, false, UIColors.PaperHi,
            new Vector2(4f, 5f), RunEndUI.Table, "runend.button.new_company");
        view.mainMenuButton = MenuLink(c, "MainMenu", x, 491f, "Main menu", 26f, RunEndUI.Table, "runend.button.main_menu");

        // WINNER beside the Main menu link: 276 x 72, 5 px border + 2 px outline 4 out, -10 deg
        Stamp(c, "Winner", 314f, 488f, 276f, 72f, "WINNER", 44f, UIColors.Money, 5f, 10f, 2f, 4f, 0.12f, -10f, 0.88f, 5f, 0f,
            RunEndUI.Table, "runend.win.stamp");
    }

    // ------------------------------------------------------------------
    // $1B certificate: 1040 x 720 at 440, 180; 10 px double gold border over a 45 deg hatch, the inner sheet 18 in

    static readonly float[,] CertConfetti =
    {
        { 214, 132, 18, 34, -24, 0 }, { 300, 262, 16, 28, 38, 1 }, { 356, 116, 22, 14, 12, 2 }, { 252, 410, 14, 30, -52, 3 },
        { 372, 522, 20, 20, 30, 4 }, { 196, 612, 16, 32, 64, 1 }, { 318, 742, 22, 16, -18, 0 }, { 236, 880, 14, 28, 22, 2 },
        { 392, 962, 18, 30, -40, 1 }, { 566, 118, 16, 30, 48, 3 }, { 704, 96, 20, 14, -10, 4 }, { 1184, 106, 18, 32, 26, 1 },
        { 1330, 126, 22, 16, -34, 2 }, { 1540, 150, 16, 30, 16, 0 }, { 1612, 292, 20, 20, 44, 3 }, { 1548, 404, 14, 28, -60, 1 },
        { 1684, 522, 18, 34, 20, 4 }, { 1560, 644, 22, 14, -8, 0 }, { 1702, 762, 16, 30, 70, 1 }, { 1532, 884, 18, 26, -28, 2 },
        { 1384, 962, 20, 16, 34, 3 }, { 1104, 1002, 14, 30, -14, 1 }, { 824, 982, 22, 14, 52, 0 }, { 604, 1010, 16, 28, -36, 4 },
        { 474, 164, 14, 26, -70, 1 }, { 1452, 872, 18, 30, 18, 0 }, { 1762, 182, 14, 24, -20, 1 }, { 152, 302, 20, 14, 10, 0 }
    };

    static void BuildCertificate(RectTransform screen, BillionCertificateView view)
    {
        var root = Stretch(screen, "Certificate");
        view.root = root;
        StretchBox(root, "Scrim", null, UIColors.Scrim).raycastTarget = true;
        PausedChip(root, 850f, 28f, 220f, 48f);

        const float L = 440f, T = 180f, W = 1040f, H = 720f;
        Shadow(root, "FrameShadow", L, T, W, H, 0f, 22f, 48f, 0.45f);
        var frame = Node(root, "Frame", L, T, W, H);
        var paper = StretchBox(frame, "Paper", S("Kit/card_fill"), UIColors.PaperHi);
        paper.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        StretchBox(paper.transform, "Hatch", S("Kit/Tiles/cert_hatch45"), UIColors.Over(UIColors.GoldDark, 0.16f, UIColors.PaperHi)).type =
            Image.Type.Tiled;
        StretchBox(frame, "BorderOuter", S("Kit/card_line3"), UIColors.GoldDark);
        Ring(frame, "BorderInner", 7f, 7f, W - 14f, H - 14f, UIColors.GoldDark, 3f, 0f).Round(Inset(CardX, 7f), Inset(CardY, 7f));
        StretchBox(frame, "SheetFill", S("Kit/r6_fill"), UIColors.PaperHi, 28f, 28f, 28f, 28f);
        StretchBox(frame, "SheetBorder", S("Kit/r6_line2"), UIColors.Ink, 28f, 28f, 28f, 28f);

        // corner diamonds on the inner sheet's corners, small ones inside
        foreach (var (cx, cy) in new[] { (28f, 28f), (1012f, 28f), (28f, 692f), (1012f, 692f) })
            Diamond(frame, "Corner", cx, cy, 20f, UIColors.Gold, true);
        foreach (var (cx, cy) in new[] { (47f, 47f), (993f, 47f), (47f, 673f), (993f, 673f) })
            Diamond(frame, "CornerDot", cx, cy, 10f, UIColors.GoldDark, false);

        // $1B seal and the Board's signature
        var seal = Node(frame, "Seal", 50f, 462f, 180f, 220f, rotation: -6f);
        StretchBox(seal, "Rosette", S("Kit/Special/rosette_1b"), Color.white).type = Image.Type.Simple;
        // the amount on the rosette's disc: a longer one (10亿美元) shrinks to fit
        var sealAmount = Localize(Text(seal, "Amount", 15f, 60f, 150f, 48f, "$1B", 40f, UIColors.Ink, bold: true, align: TextAlignmentOptions.Center),
            Table, "endings.cert.seal");
        sealAmount.enableAutoSizing = true;
        sealAmount.fontSizeMin = 20f;
        sealAmount.fontSizeMax = 40f;
        var sign = Node(frame, "Signature", 822f, 496f, 170f, 83f);
        Box(sign, "Scribble", 0f, 0f, 170f, 46f, S("Kit/Special/signature"), UIColors.Ink).type = Image.Type.Simple;
        Box(sign, "Line", 0f, 50f, 170f, 2f, null, UIColors.Ink);
        Localize(Text(sign, "Caption", -20f, 56f, 210f, 27f, "Signed, the Board", 19f, UIColors.Pencil, align: TextAlignmentOptions.Center),
            "Board", "board.memo.signed");

        // the column (20 in, padding-top 36, gap 26), centred on x 490
        var c = Node(frame, "Content", 30f, 30f, 980f, 660f);
        const float mid = 490f;
        Localize(Text(c, "Title", 0f, 36f, 980f, 69f, "Certificate of Obscene Wealth", 60f, UIColors.Ink, bold: true, align: TextAlignmentOptions.Center),
            Table, "endings.cert.title");
        Box(c, "DividerLeft", mid - 135f, 119f, 120f, 2f, null, UIColors.GoldDark);
        Box(c, "DividerRight", mid + 15f, 119f, 120f, 2f, null, UIColors.GoldDark);
        Diamond(c, "DividerDiamond", mid, 120f, 10f, UIColors.Gold, true);
        Localize(Text(c, "Certifies", 0f, 135f, 980f, 27f, "THIS CERTIFIES THAT", 19f, UIColors.Pencil, align: TextAlignmentOptions.Center,
            letterSpacingEm: 0.16f), Table, "endings.cert.certifies");

        // company name with its underline, growing with the name (padding 0 32 4, 2 px ink bottom border)
        var nameBox = NewRect(c, "Company");
        nameBox.anchorMin = nameBox.anchorMax = new Vector2(0.5f, 1f);
        nameBox.pivot = new Vector2(0.5f, 1f);
        nameBox.anchoredPosition = new Vector2(0f, -170f);
        HLayout(nameBox.gameObject, 0f, Pad(32, 32, 0, 6), TextAnchor.UpperCenter, true, true, false, false);
        Fit(nameBox.gameObject, true, true);
        var under = StretchBox(nameBox, "Underline", null, UIColors.Ink);
        under.rectTransform.anchorMin = new Vector2(0f, 0f);
        under.rectTransform.anchorMax = new Vector2(1f, 0f);
        under.rectTransform.pivot = new Vector2(0.5f, 0f);
        under.rectTransform.sizeDelta = new Vector2(0f, 2f);
        under.rectTransform.anchoredPosition = Vector2.zero;
        IgnoreLayout(under);
        var company = LayoutText(nameBox, "Name", "NOVA HOLDINGS", 72f, UIColors.Ink, bold: true, letterSpacingEm: 0.04f, lineHeight: 1.1f);
        view.companyText = company;

        var reached = Text(c, "Reached", 0f, 263.2f, 980f, 36.4f,
            "has reached <link=\"hl\"><space=6>$1,000,000,000<space=6></link> in company value.", 26f, UIColors.Ink, align: TextAlignmentOptions.Center);
        Highlighter(reached, UIColors.Highlight, 0.95f);
        view.reachedText = reached;

        // what the online cities offer: 40 px badges, 16 gap, 26 px text; rows 8 apart, left-aligned as one block centred on
        // the column, as wide as its longest line in the language shown (the view writes the lines)
        string[] lines = { "Six online cities, from London to Shanghai", "Buy plots and put your drawn logo on them", "Earn rent, win rivals' plots at blackjack" };
        string[] icons = { "map-pin", "tag", "trophy" };
        Color[] pastel = { UIColors.StickyBlue, UIColors.StickyYellow, UIColors.StickyGreen };
        float listW = 0f;
        foreach (var l in lines) listW = Mathf.Max(listW, 40f + 16f + TextWidth(l, 26f));
        var block = NewRect(c, "Lines");
        block.anchorMin = block.anchorMax = new Vector2(0f, 1f);
        block.pivot = new Vector2(0.5f, 1f);
        block.anchoredPosition = new Vector2(mid, -325.6f);
        block.sizeDelta = new Vector2(listW, 3f * 40f + 2f * 8f);
        VLayout(block.gameObject, 8f, null, TextAnchor.UpperLeft, true, true, false, false);
        Fit(block.gameObject, true, true);
        view.lineTexts = new TMP_Text[3];
        for (int i = 0; i < 3; i++)
        {
            var row = NewRect(block, "Row" + (i + 1));
            HLayout(row.gameObject, 16f, null, TextAnchor.MiddleLeft, true, true, false, false);
            var badge = NewRect(row, "Badge" + (i + 1));
            Layout(badge, 40f, 40f, 40f, 40f);
            AddImage(badge.gameObject, S("Kit/Circles/circle40_fill"), pastel[i]);
            StretchBox(badge, "Ring", S("Kit/Circles/circle40_line2"), UIColors.Ink);
            Icon(badge, "Icon", 8f, 8f, 24f, icons[i], UIColors.Ink);
            view.lineTexts[i] = LayoutText(row, "Line" + (i + 1), lines[i], 26f, UIColors.Ink);
        }

        // warning pill: Other players can take your plots over. (grows about the column's centre with the words shown)
        string warn = "Other players can take your plots over.";
        float pw = 2f + 16f + 26f + 10f + TextWidth(warn, 22f) + 16f + 2f, ph = 2f + 6f + 22f * 1.3f + 6f + 2f;
        var pill = Node(c, "Warning", mid - pw * 0.5f, 479.6f, pw, ph);
        IgnoreLayout(StretchBox(pill, "Fill", S("Kit/small_fill"), UIColors.LossSoft));
        IgnoreLayout(StretchBox(pill, "Border", S("Kit/small_line2"), UIColors.Ink));
        HLayout(pill.gameObject, 10f, Pad(18, 18, 0, 0), TextAnchor.MiddleLeft, true, true, false, false);
        Fit(pill.gameObject, true, false);
        LayoutIcon(pill, "Icon", 26f, "warning", UIColors.Loss);
        view.warningText = Localize(LayoutText(pill, "Text", warn, 22f, UIColors.Ink), Table, "endings.cert.warning");

        // Pick a city | Later
        float w1 = ButtonWidth("Pick a city", "globe", 28f, true, Table, "endings.cert.enter");
        float w2 = ButtonWidth("Later", null, 28f, false, Table, "endings.cert.later");
        float bx = mid - (w1 + 24f + w2) * 0.5f;
        view.enterButton = PaperButton(c, "EnterCity", bx, 550.2f, w1, 68f, "Pick a city", "globe", 28f, true, UIColors.Highlight,
            new Vector2(5f, 6f), Table, "endings.cert.enter");
        view.laterButton = PaperButton(c, "Later", bx + w1 + 24f, 550.2f, w2, 68f, "Later", null, 28f, false, UIColors.PaperHi,
            new Vector2(4f, 5f), Table, "endings.cert.later");

        // close button on the top-right corner (60 px, 32 px out)
        view.closeButton = IconButton(root, "Close", L + 1002f, T - 22f, 60, "cross");

        view.confetti = Confetti(root, CertConfetti);
        root.gameObject.SetActive(false);
    }

    /// <summary>"Game paused" chip (pause icon + text, centred) like the other paused pop-ups.</summary>
    static void PausedChip(RectTransform parent, float left, float top, float w, float h)
    {
        var chip = Node(parent, "Paused", left, top, w, h);
        Box(chip, "Shadow", 3f, 4f, w, h, S("Kit/small_fill"), UIColors.WithAlpha(UIColors.Ink, UIColors.SrgbAlpha(0.9f)));
        StretchBox(chip, "Paper", S("Kit/small_fill"), UIColors.PaperHi);
        StretchBox(chip, "Border", S("Kit/small_line2"), UIColors.Ink);
        var row = Stretch(chip, "Row");
        HLayout(row.gameObject, 10f, null, TextAnchor.MiddleCenter, true, true, false, false);
        LayoutIcon(row, "Icon", 24f, "pause", UIColors.Ink);
        Localize(LayoutText(row, "Label", "Game paused", 22f, UIColors.Ink), RunEndUI.Table, "runend.ui.paused");
    }

    // ------------------------------------------------------------------
    // Pieces

    /// <summary>
    /// Rubber stamp: the word in a border (width b, radius r) with an outline (width o, offset off) around it, faded and
    /// rotated about its centre. left/top/w/h is the border box; padL/padR the CSS horizontal padding. The word is bound
    /// to table/key; the stamp is at least w wide and grows about its centre when a language's word needs more.
    /// </summary>
    static RectTransform Stamp(Transform parent, string name, float left, float top, float w, float h, string word, float size, Color color,
        float b, float r, float o, float off, float spacing, float rotation, float alpha, float padL, float padR, string table = null, string key = null)
    {
        var st = Node(parent, name, left, top, w, h, rotation: rotation);
        var g = st.gameObject.AddComponent<CanvasGroup>();
        g.alpha = alpha;
        g.blocksRaycasts = false;
        g.interactable = false;
        StretchRing(st, "Border", 0f, color, b, r);
        float grow = off + o;
        StretchRing(st, "Outline", -grow, color, o, r + grow);
        Bind(Text(st, "Word", b + padL, b, w - 2f * b - padL - padR, h - 2f * b, word, size, color, bold: true,
            align: TextAlignmentOptions.Center, letterSpacingEm: spacing), table, key);
        HLayout(st.gameObject, 0f, Pad(Mathf.RoundToInt(b + padL), Mathf.RoundToInt(b + padR), 0, 0), TextAnchor.MiddleCenter, true, false, false, false);
        Layout(st, w);
        st.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        return st;
    }

    /// <summary>A ring that follows its parent's size (a negative inset puts it outside), left out of the parent's layout.</summary>
    static UIShape StretchRing(Transform parent, string name, float inset, Color color, float border, float radius)
    {
        var s = Stretch(parent, name, inset, inset, inset, inset).gameObject.AddComponent<UIShape>();
        s.raycastTarget = false;
        s.color = color;
        s.border = border;
        if (radius > 0f) s.Round(radius);
        IgnoreLayout(s);
        return s;
    }

    /// <summary>The label bound to its String Table entry (no key: left as it is).</summary>
    static T Bind<T>(T text, string table, string key) where T : TMP_Text => key != null ? Localize(text, table, key) : text;

    /// <summary>Width a one-line label needs: with a key, for the widest language of its entry (PauseMenuBuilder.WidestText).</summary>
    static float LabelWidth(string label, float size, bool bold, string table, string key) =>
        key != null ? PauseMenuBuilder.WidestText(table, key, label, size, bold) : TextWidth(label, size, bold);

    static UIShape Ring(Transform parent, string name, float left, float top, float w, float h, Color color, float border, float radius)
    {
        var rt = Node(parent, name, left, top, w, h);
        var s = rt.gameObject.AddComponent<UIShape>();
        s.raycastTarget = false;
        s.color = color;
        s.border = border;
        if (radius > 0f) s.Round(radius);
        return s;
    }

    static Vector4 Inset(Vector4 r, float d) => new Vector4(Mathf.Max(0f, r.x - d), Mathf.Max(0f, r.y - d), Mathf.Max(0f, r.z - d), Mathf.Max(0f, r.w - d));

    /// <summary>A square turned 45 deg, centred on cx, cy (with a 2 px ink border when bordered).</summary>
    static void Diamond(Transform parent, string name, float cx, float cy, float size, Color fill, bool bordered)
    {
        var d = Node(parent, name, cx - size * 0.5f, cy - size * 0.5f, size, size, rotation: 45f);
        StretchBox(d, "Fill", null, fill);
        if (bordered)
        {
            var ring = Stretch(d, "Border").gameObject.AddComponent<UIShape>();
            ring.raycastTarget = false;
            ring.color = UIColors.Ink;
            ring.border = 2f;
        }
    }

    static float ButtonWidth(string label, string icon, float size, bool bold, string table = null, string key = null) =>
        36f + (icon != null ? 28f + 12f : 0f) + LabelWidth(label, size, bold, table, key) + 36f;

    /// <summary>
    /// Marker button: fill, 3 px ink border, hard ink shadow; icon and label centred, 12 apart. The label is bound to
    /// table/key and shrinks a little rather than leave the button.
    /// </summary>
    static Button PaperButton(Transform parent, string name, float left, float top, float w, float h, string label, string icon, float size,
        bool bold, Color fill, Vector2 shadow, string table = null, string key = null)
    {
        var btn = MarkerButtonNode(parent, name, left, top, w, h, Anchor.TopLeft, fill, shadow, out var face, out var content);
        HLayout(content.gameObject, 12f, Pad(36, 36, 0, 0), TextAnchor.MiddleCenter, true, true, false, false);
        var graphics = new List<Graphic>();
        if (icon != null) graphics.Add(LayoutIcon(content, "Icon", 28f, icon, UIColors.Ink));
        var text = Bind(LayoutText(content, "Label", label, size, UIColors.Ink, bold), table, key);
        if (key != null) PauseMenuBuilder.FitWidth(text, size);
        graphics.Add(text);
        SetupMarker(btn, face, graphics.ToArray());
        return btn.GetComponent<Button>();
    }

    /// <summary>
    /// Text link: home icon + label with a wavy ink underline 7 px under the baseline. The label is bound to table/key;
    /// the link fits the widest language and the text and its underline grow with the words shown.
    /// </summary>
    static Button MenuLink(Transform parent, string name, float left, float top, string label, float size, string table = null, string key = null)
    {
        float tw = LabelWidth(label, size, false, table, key);
        var link = Node(parent, name, left, top, 40f + tw + 4f, 52f);
        var hit = AddImage(link.gameObject, null, UIBuild.Clear, true);
        var button = link.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = hit;
        Icon(link, "Icon", 0f, 12f, 28f, "home", UIColors.Ink);
        var text = Bind(Text(link, "Label", 40f, 0f, tw + 4f, 52f, label, size, UIColors.Ink), table, key);
        var face = UIBuild.Font.faceInfo;
        float content = size * UIFonts.ContentHeight(UIBuild.Font);
        float baseline = (52f - content) * 0.5f + face.ascentLine / face.pointSize * size;
        if (key == null)
        {
            var wave = Box(link, "Underline", 40f, baseline + 5f, tw, 6f, S("Kit/Tiles/wave_h2"), UIColors.Ink);
            wave.type = Image.Type.Tiled;
            return button;
        }
        // the label follows its words (left edge fixed); the underline is a child as wide as the label
        var textRect = text.rectTransform;
        textRect.pivot = new Vector2(0f, 0.5f);
        textRect.anchoredPosition -= new Vector2((tw + 4f) * 0.5f, 0f);
        Fit(text.gameObject, true, false);
        var underline = StretchBox(textRect, "Underline", S("Kit/Tiles/wave_h2"), UIColors.Ink);
        underline.type = Image.Type.Tiled;
        var lineRect = underline.rectTransform;
        lineRect.anchorMin = new Vector2(0f, 1f);
        lineRect.anchorMax = new Vector2(1f, 1f);
        lineRect.pivot = new Vector2(0.5f, 1f);
        lineRect.offsetMin = new Vector2(0f, -(baseline + 5f) - 6f);
        lineRect.offsetMax = new Vector2(0f, -(baseline + 5f));
        return button;
    }

    static TextHighlighter Highlighter(TMP_Text text, Color color, float cssAlpha)
    {
        var h = text.gameObject.AddComponent<TextHighlighter>();
        h.color = UIColors.WithAlpha(color, cssAlpha);
        return h;
    }

    /// <summary>Hand-cut confetti: [left, top, width, height, css rotation, colour] with a 2 px ink border and a hard shadow.</summary>
    static RectTransform[] Confetti(RectTransform parent, float[,] pieces)
    {
        Color[] colors = { UIColors.StickyYellow, UIColors.Gold, UIColors.StickyBlue, UIColors.StickyPink, UIColors.StickyGreen };
        var layer = Stretch(parent, "Confetti");
        int n = pieces.GetLength(0);
        var result = new RectTransform[n];
        for (int i = 0; i < n; i++)
        {
            float w = pieces[i, 2], h = pieces[i, 3];
            var p = Node(layer, "Piece" + (i + 1), pieces[i, 0], pieces[i, 1], w, h, rotation: pieces[i, 4]);
            Box(p, "Shadow", 2f, 3f, w, h, null, UIColors.Dark(0.25f));
            StretchBox(p, "Fill", null, colors[(int)pieces[i, 5]]);
            var ring = Stretch(p, "Border").gameObject.AddComponent<UIShape>();
            ring.raycastTarget = false;
            ring.color = UIColors.Ink;
            ring.border = 2f;
            result[i] = p;
        }
        return result;
    }

    // ------------------------------------------------------------------
    // MainGame

    public static void Install(GameObject prefab, List<string> log)
    {
        Scene scene = SceneManager.GetSceneByPath(OfficeHudBuilder.ScenePath);
        if (!scene.IsValid() || !scene.isLoaded)
            scene = EditorSceneManager.OpenScene(OfficeHudBuilder.ScenePath, OpenSceneMode.Single);

        GameObject existing = null;
        foreach (var r in scene.GetRootGameObjects())
            if (PrefabUtility.GetCorrespondingObjectFromSource(r) == prefab) existing = r;
        if (existing == null)
        {
            existing = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            existing.name = "RunEndScreens";
            log.Add("MainGame: added the RunEndScreens prefab (RunEndDirector uses its game over and win screens).");
        }
        else
        {
            log.Add("MainGame: RunEndScreens already there (the prefab update applies to it).");
        }
        existing.SetActive(true);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        log.Add("MainGame saved.");
    }

    /// <summary>Remove RunEndScreens from MainGame: RunEndDirector builds its runtime screens again.</summary>
    public static void Restore(List<string> log)
    {
        Scene scene = SceneManager.GetSceneByPath(OfficeHudBuilder.ScenePath);
        if (!scene.IsValid() || !scene.isLoaded)
            scene = EditorSceneManager.OpenScene(OfficeHudBuilder.ScenePath, OpenSceneMode.Single);
        int removed = 0;
        foreach (var r in scene.GetRootGameObjects())
        {
            if (r.name != "RunEndScreens") continue;
            UnityEngine.Object.DestroyImmediate(r);
            removed++;
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        log.Add("MainGame: removed " + removed + " RunEndScreens (the run-end views build their runtime screens again).");
    }

    // ------------------------------------------------------------------
    // Previews (the boards' moments)

    const string PreviewCompany = "Nova Holdings";

    public static void RenderPreviews(GameObject hud, GameObject prefab, string dir, List<string> log, string company = PreviewCompany)
    {
        var over = new RunSummary
        {
            companyName = company, day = 57, companyValue = 0, peakValue = 4912300, hires = 64, goldenHires = 1, riskCardsPlayed = 23,
            biggestWin = 55000, biggestWinTitle = "Cure Discovery", biggestLoss = -30000, biggestLossTitle = "Factory Fire",
            crisesSolved = 18, crisesTotal = 27, casinoNet = -212400, hasGambled = true
        };
        UIPreview.Render(Path.Combine(dir, "end_gameover.png"), log, new UIPreview.Layer(hud), new UIPreview.Layer(prefab, g =>
        {
            var v = g.GetComponentInChildren<GameOverView>(true);
            Invoke(v, "Fill", over);
            v.root.gameObject.SetActive(true);
        }));

        var won = new RunSummary
        {
            companyName = company, day = 164, companyValue = 10000000000d, peakValue = 10000000000d, hires = 386, goldenHires = 4,
            riskCardsPlayed = 40, biggestWin = 55000, biggestWinTitle = "Cure Discovery", biggestLoss = -30000, biggestLossTitle = "Factory Fire",
            crisesSolved = 18, crisesTotal = 27, casinoNet = 2100000, hasGambled = true
        };
        UIPreview.Render(Path.Combine(dir, "end_win.png"), log, new UIPreview.Layer(hud), new UIPreview.Layer(prefab, g =>
        {
            var v = g.GetComponentInChildren<WinView>(true);
            Invoke(v, "Fill", won, 10000000000d);
            v.root.gameObject.SetActive(true);
        }));

        UIPreview.Render(Path.Combine(dir, "end_billion.png"), log, new UIPreview.Layer(hud), new UIPreview.Layer(prefab, g =>
        {
            var v = g.GetComponentInChildren<BillionCertificateView>(true);
            v.companyText.text = L10n.ToUpper(company.Trim());
            v.reachedText.text = BillionCertificateView.ReachedLine(1000000000d);
            v.root.gameObject.SetActive(true);
        }));
    }

    static void Invoke(object target, string method, params object[] args)
    {
        var m = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (m == null) throw new MissingMethodException(target.GetType().Name, method);
        m.Invoke(target, args);
    }
}
