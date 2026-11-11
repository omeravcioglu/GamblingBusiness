using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static UIBuild;

/// <summary>
/// Crises on the office CRT (page "Interruptions": "Crisis incoming", "CRT frame", "Crisis result" and the 20 Crt*
/// boards): builds Assets/UI/Prefabs/CrisisGames.prefab, one panel per mini-game with its script, the shared monitor
/// (CrtScreenView) and the game's own screen in a GameArea over the glass, and points MiniGameManager at them.
/// Coordinates are design pixels: the monitor on the 1920 x 1080 board, games on the 1380 x 772 glass at 270, 88.
/// </summary>
public static partial class CrisisGamesBuilder
{
    public const string PrefabPath = "Assets/UI/Prefabs/CrisisGames.prefab";
    const float GX = 270f, GY = 88f, GW = 1380f, GH = 772f;

    static readonly Color Blue = CrtScreenView.Blue, Green = CrtScreenView.Green, Red = CrtScreenView.Red, Yellow = CrtScreenView.Yellow,
        Pink = CrtScreenView.Pink, Paper = CrtScreenView.Paper, Ink = CrtScreenView.Ink, Black = CrtScreenView.Black;
    static readonly Color Beige = UIColors.Hex(0xEAE2CF);
    static readonly Color LossSoft = UIColors.Hex(0xF6D8D3);

    static Color Lit(Color c, float a) => CrtScreenView.Lit(c, a);

    /// <summary>
    /// TMP glow colour for the CSS text-shadow glows (0 0 14-28px, alpha 0.5-0.7). TMP's glow can only reach as far as
    /// the atlas padding (~0.1 em), is twice its alpha at the glyph edge and blends in linear space, so a CSS-like alpha
    /// saturates the edge and the text looks bold: aim for a CSS-like alpha at the edge instead (0.2-0.35).
    /// </summary>
    static Color TextGlow(Color c, float edge)
    {
        c.a = UIColors.BlendAlpha(edge, c, Ink) * 0.5f;
        return c;
    }

    // CSS radius families
    static readonly Vector4 CardX = new Vector4(14, 20, 12, 22), CardY = new Vector4(20, 12, 22, 14);
    static readonly Vector4 CardX2 = new Vector4(20, 14, 22, 12), CardY2 = new Vector4(12, 22, 14, 20);
    static readonly Vector4 SmallX = new Vector4(10, 14, 9, 15), SmallY = new Vector4(14, 9, 15, 10);
    static readonly Vector4 PanelX = new Vector4(18, 26, 20, 30), PanelY = new Vector4(28, 18, 26, 20);

    static Material glowYellow, glowGreen, glowRed, glowPink, glowPaper, glowBlue;

    class Spec
    {
        public string panel;
        public Type script;
        public string title;
        public string controls;
        public string[] keys;
        public string progress;
        public string stat;
        public bool lives;
        public string rules;
        public Action<RectTransform, MiniGameBase> build;
        public string field;   // MiniGameManager field

        /// <summary>Key of one of the game's texts in Crisis.csv: "crisis.game." + L10n.Slug(title) + "." + part.</summary>
        public string Key(string part) => CrisisText.GameKey(title) + "." + part;
    }

    static Spec[] specs;

    static Spec[] Specs => specs ?? (specs = new[]
    {
        new Spec { panel = "ArrowTimingPanel", field = "arrowTimingPanel", script = typeof(ArrowTimingMiniGame), title = "Arrow Timing",
            controls = "Press when the arrow is in the green", keys = new[] { "Space" }, progress = "HITS", stat = "SPEED",
            rules = "5 hits to fix it · the zone shrinks every hit · one miss and it's over", build = ArrowTiming },
        new Spec { panel = "StopTheBarPanel", field = "stopTheBarPanel", script = typeof(StopTheBarMiniGame), title = "Stop the Bar",
            controls = "Stop the bar in the green zone", keys = new[] { "Space" }, progress = "HITS", stat = "SPEED",
            rules = "3 hits to fix it · one miss and it's over", build = StopTheBar },
        new Spec { panel = "ColorMatchPanel", field = "colorMatchPanel", script = typeof(ColorMatchMiniGame), title = "Color Match",
            controls = "Press the matching colour", keys = new[] { "R", "G", "B", "Y" }, progress = "MATCHED", stat = "PER COLOUR",
            rules = "10 colours to fix it · wrong key or too slow and it's over", build = ColorMatch },
        new Spec { panel = "WhackAMolePanel", field = "whackAMolePanel", script = typeof(WhackAMoleMiniGame), title = "Whack-A-Mole",
            controls = "Click the moles before they hide", keys = new[] { "click" }, progress = "WHACKED", stat = "SPEED",
            rules = "15 whacks to fix it · misses are free, the clock isn't", build = WhackAMole },
        new Spec { panel = "SimonSaysPanel", field = "simonSaysPanel", script = typeof(SimonSaysMiniGame), title = "Simon Says",
            controls = "Watch the pattern, then repeat it", keys = new[] { "click" }, progress = "ROUND", stat = "CLICKED",
            rules = "5 rounds to fix it · one wrong pad and it's over", build = SimonSays },
        new Spec { panel = "NumberSequencePanel", field = "numberSequencePanel", script = typeof(NumberSequenceMiniGame), title = "Number Sequence",
            controls = "Memorize the numbers, then type them", keys = new[] { "Enter" }, progress = "LEVEL", stat = "DIGITS",
            rules = "5 levels, 3 to 7 digits · one wrong number and it's over", build = NumberSequence },
        new Spec { panel = "KeypadCodePanel", field = "keypadCodePanel", script = typeof(KeypadCodeMiniGame), title = "Keypad Code",
            controls = "Remember and enter the code (or type it)", keys = new[] { "click" }, progress = "DIGITS", stat = "HIDES IN",
            rules = "4 digits, shown for 2 s · one wrong code and it's over", build = KeypadCode },
        new Spec { panel = "ButtonMashPanel", field = "buttonMashPanel", script = typeof(ButtonMashMiniGame), title = "Button Mash",
            controls = "Mash as fast as you can", keys = new[] { "Space" }, progress = "PRESSES", stat = "TO GO",
            rules = "40 presses to fix it · beat the 10-second clock", build = ButtonMash },
        new Spec { panel = "MathBlitzPanel", field = "mathBlitzPanel", script = typeof(MathBlitzMiniGame), title = "Math Blitz",
            controls = "Click the correct answer", keys = new[] { "click" }, progress = "SOLVED", stat = null,
            rules = "8 right to fix it · one wrong and it's over", build = MathBlitz },
        new Spec { panel = "QuickTapPanel", field = "quickTapPanel", script = typeof(QuickTapMiniGame), title = "Quick Tap",
            controls = "Click the targets quickly", keys = new[] { "click" }, progress = "HITS", stat = "PER TARGET",
            rules = "12 hits to fix it · too slow once and it's over", build = QuickTap },
        new Spec { panel = "RapidKeysPanel", field = "rapidKeysPanel", script = typeof(RapidKeysMiniGame), title = "Rapid Keys",
            controls = "Press the keys as they appear!", keys = new[] { "A", "S", "D", "F", "J", "K", "L" }, progress = "KEYS", stat = null,
            rules = "15 keys to fix it · one wrong key and it's over", build = RapidKeys },
        new Spec { panel = "SequenceMemoryPanel", field = "sequenceMemoryPanel", script = typeof(SequenceMemoryMiniGame), title = "Sequence Memory",
            controls = "Watch the sequence, then repeat it!", keys = new[] { "click" }, progress = "SEQUENCE", stat = null,
            rules = "Repeat the 5 flashes in order · one wrong tile and it's over", build = SequenceMemory },
        new Spec { panel = "PatternMatchPanel", field = "patternMatchPanel", script = typeof(PatternMatchMiniGame), title = "Pattern Match",
            controls = "Memorize the pattern, then enter it with the arrow keys", keys = new[] { "left", "up", "down", "right" }, progress = "PATTERNS", stat = "ARROWS",
            rules = "3 patterns to fix it · each adds an arrow · one wrong arrow and it's over", build = PatternMatch },
        new Spec { panel = "SafeCrackerPanel", field = "safeCrackerPanel", script = typeof(SafeCrackerMiniGame), title = "Safe Cracker",
            controls = "Turn the dial, Space to confirm", keys = new[] { "left", "right", "Space" }, progress = "NUMBERS", stat = null,
            rules = "3 numbers to crack it · one wrong number and it's over", build = SafeCracker },
        new Spec { panel = "OddOneOutPanel", field = "oddOneOutPanel", script = typeof(OddOneOutMiniGame), title = "Odd One Out",
            controls = "Click the one that's different!", keys = new[] { "click" }, progress = "FOUND", stat = null,
            rules = "Spot 5 to fix it · new colours every round · one wrong click and it's over", build = OddOneOut },
        new Spec { panel = "CatchFallingPanel", field = "catchFallingPanel", script = typeof(CatchFallingMiniGame), title = "Catch Falling",
            controls = "Catch the falling items", keys = new[] { "left", "right" }, progress = "CAUGHT", stat = "LIVES", lives = true,
            rules = "Catch 10 to fix it · 3 drops and it's over · it falls faster every catch", build = CatchFalling },
        new Spec { panel = "HigherLowerPanel", field = "higherLowerPanel", script = typeof(HigherLowerMiniGame), title = "Higher Lower",
            controls = "Next number higher or lower?", keys = new[] { "click", "up", "down" }, progress = "CORRECT", stat = "LIVES", lives = true,
            rules = "5 right calls to fix it · 2 wrong and it's over", build = HigherLower },
        new Spec { panel = "WordTypePanel", field = "wordTypePanel", script = typeof(WordTypeMiniGame), title = "Word Type",
            controls = "Type the word and press Enter", keys = new[] { "Enter" }, progress = "TYPED", stat = null,
            rules = "6 words to fix it · a wrong word just clears the line · caps don't matter", build = WordType },
        new Spec { panel = "CoinFlipPanel", field = "coinFlipPanel", script = typeof(CoinFlipMiniGame), title = "Coin Flip",
            controls = "Press H for heads, T for tails", keys = new[] { "H", "T" }, progress = "RIGHT CALLS", stat = "LIVES", lives = true,
            rules = "3 right calls to fix it · 3 wrong and it's over · a fair 50 / 50", build = CoinFlip },
        new Spec { panel = "SpeedCountPanel", field = "speedCountPanel", script = typeof(SpeedCountMiniGame), title = "Speed Count",
            controls = "Count the items and enter the number", keys = new[] { "Enter" }, progress = "COUNTED", stat = "LIVES", lives = true,
            rules = "5 right counts to fix it · a wrong count deals a new set · 3 wrong and it's over", build = SpeedCount },
    });

    // ------------------------------------------------------------------
    // Menu / batch

    [MenuItem("Tools/Game/New UI/Build Crisis Games")]
    public static void BuildFromMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Build Crisis Games", "Stop Play mode first.", "OK");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var log = new List<string>();
        var prefab = BuildPrefab(log);
        if (prefab != null) Install(prefab, log);
        Debug.Log("[CrisisGamesBuilder]\n" + string.Join("\n", log));
        EditorUtility.DisplayDialog("Crisis games", string.Join("\n", log), "OK");
    }

    [MenuItem("Tools/Game/New UI/Restore Old Crisis Panels")]
    public static void RestoreFromMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var log = new List<string>();
        Restore(log);
        Debug.Log("[CrisisGamesBuilder]\n" + string.Join("\n", log));
        EditorUtility.DisplayDialog("Crisis games", string.Join("\n", log), "OK");
    }

    /// <summary>Unity.exe -batchmode -executeMethod CrisisGamesBuilder.BatchRun [-install] [-preview folder]</summary>
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
            var prefab = BuildPrefab(log);
            if (prefab == null) code = 1;
            if (prefab != null && install) Install(prefab, log);
            if (prefab != null && dir != null)
            {
                var hud = AssetDatabase.LoadAssetAtPath<GameObject>(OfficeHudBuilder.PrefabPath);
                RenderPreviews(hud, prefab, dir, log);
            }
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
        Debug.Log("[CrisisGamesBuilder]\n" + string.Join("\n", log));
        EditorApplication.Exit(code);
    }

    /// <summary>Unity.exe -batchmode -executeMethod CrisisGamesBuilder.PreviewRun -preview folder: renders the built prefab as is.</summary>
    public static void PreviewRun()
    {
        int code = 0;
        var log = new List<string>();
        try
        {
            var args = Environment.GetCommandLineArgs();
            int p = Array.IndexOf(args, "-preview");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var hud = AssetDatabase.LoadAssetAtPath<GameObject>(OfficeHudBuilder.PrefabPath);
            if (prefab == null || p < 0 || p + 1 >= args.Length) throw new Exception("Needs the built " + PrefabPath + " and -preview folder.");
            RenderPreviews(hud, prefab, args[p + 1], log);
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
        Debug.Log("[CrisisGamesBuilder]\n" + string.Join("\n", log));
        EditorApplication.Exit(code);
    }

    // ------------------------------------------------------------------
    // Prefab

    public static GameObject BuildPrefab(List<string> log)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
        glowYellow = UIFonts.GlowMaterial("Yellow", TextGlow(Yellow, 0.34f), 1f);
        glowGreen = UIFonts.GlowMaterial("Green", TextGlow(Green, 0.34f), 1f);
        glowRed = UIFonts.GlowMaterial("Red", TextGlow(Red, 0.34f), 1f);
        glowPink = UIFonts.GlowMaterial("Pink", TextGlow(Pink, 0.3f), 1f);
        glowPaper = UIFonts.GlowMaterial("Paper", TextGlow(Paper, 0.2f), 1f);   // the big numbers (72-220 px)
        glowBlue = UIFonts.GlowMaterial("Blue", TextGlow(Blue, 0.3f), 1f);
        AssetDatabase.SaveAssets();
        crisisTexts = null;   // read Crisis.csv again (WidestText)

        GameObject root = null;
        try
        {
            root = new GameObject("CrisisGames", typeof(RectTransform));
            root.layer = 5;
            var rt = (RectTransform)root.transform;
            rt.sizeDelta = new Vector2(1920f, 1080f);
            foreach (var g in Specs)
                BuildPanel(rt, g).gameObject.SetActive(false);

            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 104;   // above the HUD and the skill board / pause menu, below risk events
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            root.AddComponent<GraphicRaycaster>();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool ok);
            log.Add(ok ? "Saved " + PrefabPath + " (" + Specs.Length + " crisis games)" : "Could not save " + PrefabPath);
            return ok ? prefab : null;
        }
        finally
        {
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
        }
    }

    static RectTransform BuildPanel(RectTransform root, Spec g)
    {
        var panel = Stretch(root, g.panel);
        var game = (MiniGameBase)panel.gameObject.AddComponent(g.script);
        game.successFlashColor = Lit(Green, 0.28f);
        game.failFlashColor = Lit(Red, 0.32f);
        var audio = panel.gameObject.AddComponent<AudioSource>();
        audio.playOnAwake = false;
        game.audioSource = audio;
        var view = panel.gameObject.AddComponent<CrtScreenView>();
        view.gameTitle = g.title;
        view.heartFill = IconSprite("heart", "fill");
        view.heartLine = IconSprite("heart", "b");
        view.glowGreen = glowGreen;
        view.glowRed = glowRed;
        view.glowPink = glowPink;
        view.glowYellow = glowYellow;

        StretchBox(panel, "Scrim", null, UIColors.Scrim).raycastTarget = true;   // clicks never reach the office
        BuildMonitor(panel, view, g);
        var area = Node(panel, "GameArea", GX, GY, GW, GH);
        area.gameObject.AddComponent<RectMask2D>();
        view.gameArea = area.gameObject;
        g.build(area, game);
        view.glassFx = BuildGlassFx(panel);
        BuildResult(panel, view);
        BuildWarning(panel, view, g);
        return panel;
    }

    // ------------------------------------------------------------------
    // The monitor (board "CRT frame")

    static void BuildMonitor(RectTransform panel, CrtScreenView view, Spec g)
    {
        var crt = Stretch(panel, "Crt");
        view.crt = crt.gameObject;

        // beige body filling the screen, 4 px ink edge, bevels, drop shadow
        Shadow(crt, "BodyShadow", 10f, 8f, 1900f, 1120f, 0f, 22f, 48f, 0.45f);
        var bodyR = new Vector4(72, 72, 24, 24);
        var bodyRY = new Vector4(64, 64, 24, 24);
        Shape(crt, "BodyEdge", 10f, 8f, 1900f, 1120f, Ink).Round(bodyR, bodyRY);
        Shape(crt, "Body", 14f, 12f, 1892f, 1112f, Beige).Round(bodyR - Vector4.one * 4f, bodyRY - Vector4.one * 4f);
        Box(crt, "BevelTop", 82f, 12f, 1756f, 10f, null, UIColors.Over(Paper, 0.55f, Beige));
        Box(crt, "BevelLeft", 14f, 76f, 14f, 1044f, null, UIColors.Over(Paper, 0.25f, Beige));
        Box(crt, "BevelRight", 1892f, 76f, 14f, 1044f, null, UIColors.Dark(0.06f));
        var line = Shape(crt, "InnerLine", 34f, 30f, 1852f, 1100f, UIColors.WithAlpha(Ink, UIColors.SrgbAlpha(0.18f)));
        line.Round(new Vector4(56, 56, 0, 0), new Vector4(50, 50, 0, 0));
        line.border = 2f;

        // screen recess and the curved dark glass
        var recess = Shape(crt, "Recess", 246f, 64f, 1428f, 820f, UIColors.WithAlpha(Ink, UIColors.SrgbAlpha(0.14f)));
        recess.Round(new Vector4(52, 52, 52, 52), new Vector4(48, 48, 48, 48));
        Box(crt, "RecessShadeTop", 298f, 67f, 1324f, 8f, null, UIColors.Dark(0.16f));
        Box(crt, "RecessLightBottom", 298f, 877f, 1324f, 4f, null, UIColors.Over(Paper, 0.45f, Beige));
        var recessEdge = Shape(crt, "RecessEdge", 246f, 64f, 1428f, 820f, Ink);
        recessEdge.Round(new Vector4(52, 52, 52, 52), new Vector4(48, 48, 48, 48));
        recessEdge.border = 3f;

        var glass = Shape(crt, "Glass", GX, GY, GW, GH, Color.white);
        glass.Round(new Vector4(44, 44, 44, 44), new Vector4(40, 40, 40, 40));
        glass.gameObject.AddComponent<Mask>().showMaskGraphic = false;
        var bg = StretchBox(glass.rectTransform, "Background", S("Kit/Special/crt_glass"), Color.white);
        bg.type = Image.Type.Simple;
        var vignette = StretchShape(glass.rectTransform, "Vignette", UIColors.Dark(0.7f));
        vignette.Round(new Vector4(44, 44, 44, 44), new Vector4(40, 40, 40, 40));
        vignette.glow = -90f;
        var rim = StretchShape(glass.rectTransform, "Rim", Black);
        rim.Round(new Vector4(44, 44, 44, 44), new Vector4(40, 40, 40, 40));
        rim.border = 4f;

        // timer, title, progress rows (hidden in the result state)
        var play = Node(crt, "PlayGlass", GX, GY, GW, GH);
        view.playGlass = play.gameObject;
        Localize(Text(play, "TimeLabel", 56f, 42f, 80f, 30.8f, "TIME", 22f, Blue, letterSpacingEm: 0.1f), CrisisText.Table, "crisis.screen.time");
        var segs = Node(play, "Segments", 144f, 38f, 1043f, 40f);
        view.segments = new UIShape[30];
        view.segmentGlows = new UIShape[30];
        for (int i = 0; i < 30; i++)
        {
            float x = i * 35f;
            var glowShape = Shape(segs, "Glow" + i, x, 0f, 28f, 40f, Lit(Blue, 0.6f), 4f);
            glowShape.glow = 10f;
            view.segmentGlows[i] = glowShape;
            view.segments[i] = Shape(segs, "Seg" + i, x, 0f, 28f, 40f, Blue, 4f);
        }
        view.timeText = Text(play, "Time", 1206f, 26f, 136f, 62.1f, "0:30", 54f, Blue, bold: true, align: TextAlignmentOptions.Right);
        view.title = Text(play, "Title", 0f, 108f, GW, 105.6f, g.title, 96f, Yellow, bold: true, align: TextAlignmentOptions.Center);   // the view writes the name
        view.title.fontSharedMaterial = glowYellow;

        // progress row (label, pips, count) and the stat on the right
        var row = Node(play, "ProgressRow", 110f, 224f, 780f, 56f);
        view.progressRow = row;
        view.progressLabel = Localize(RowText(row, "Label", g.progress, 22f, Blue, false, 0.1f), CrisisText.Table, g.Key("stat." + L10n.Slug(g.progress)));
        view.pipsBox = Stretch(row, "Pips");
        view.progressCount = RowText(row, "Count", "0 / 5", 44f, Paper, true, 0f);
        if (g.stat != null)
        {
            var stat = Node(play, "StatRow", 900f, 224f, 370f, 56f);
            view.statRow = stat;
            // the view shows it in the current language (and a game may write its own: KeypadCode)
            view.statLabel = RowText(stat, "Label", g.stat, 22f, Blue, false, 0.1f);
            view.statValue = RowText(stat, "Value", "", 44f, Pink, true, 0f);
            view.statValue.fontSharedMaterial = glowPink;
            view.livesBox = NewRect(stat, "Lives");
            view.livesBox.gameObject.SetActive(false);
        }
        Localize(Text(play, "Rules", 0f, 664f, GW, 36.4f, g.rules, 26f, Blue, align: TextAlignmentOptions.Center), CrisisText.Table, g.Key("rules"));

        // crisis line: pink sticky on the left bezel
        var crisis = StickyNote(crt, "CrisisSticky", 24f, 110f, 244f, 284f, UIColors.StickyPink, -3f);
        Icon(crisis, "Icon", 22f, 34f, 28f, "warning", UIColors.Loss);
        Localize(Text(crisis, "Label", 58f, 32f, 170f, 29.9f, "CRISIS!", 26f, UIColors.Loss, bold: true, letterSpacingEm: 0.06f), CrisisText.Table, "crisis.screen.crisis");
        view.crisisText = Text(crisis, "Crisis", 22f, 74f, 200f, 190f, "Server is crashing! Get systems back online!", 26f, UIColors.Ink, wrap: true);
        view.crisisText.alignment = TextAlignmentOptions.TopLeft;
        view.crisisText.enableAutoSizing = true;
        view.crisisText.fontSizeMin = 18f;
        view.crisisText.fontSizeMax = 26f;
        view.crisisText.lineSpacing = (1.3f - UIFonts.NaturalLineHeight(UIBuild.Font)) * 100f;

        // stakes: paper note taped to the right bezel
        var stakes = Node(crt, "Stakes", 1688f, 144f, 204f, 300f, rotation: 2.5f);
        Shadow(stakes, "Shadow", 0f, 0f, 204f, 300f, 0f, 12f, 20f, 0.28f);
        Shape(stakes, "Paper", 0f, 0f, 204f, 300f, Paper).Round(SmallX, SmallY);
        var edge = Shape(stakes, "Edge", 0f, 0f, 204f, 300f, Ink);
        edge.Round(SmallX, SmallY);
        edge.border = 2f;
        Localize(Text(stakes, "Label", 20f, 32f, 170f, 26.6f, "STAKES", 19f, UIColors.Pencil, letterSpacingEm: 0.08f), CrisisText.Table, "crisis.screen.stakes");
        view.rewardText = Text(stakes, "Reward", 20f, 62.6f, 170f, 36.8f, "+$2,000", 32f, UIColors.Money, bold: true);
        Localize(Text(stakes, "IfSolved", 20f, 103.4f, 170f, 26.6f, "if solved", 19f, UIColors.InkSoft), CrisisText.Table, "crisis.screen.stakes.if_solved");
        var dashes = Node(stakes, "Dashes", 20f, 134f, 164f, 2f);
        for (float dx = 0f; dx < 164f; dx += 14f)
            Box(dashes, "Dash", dx, 0f, Mathf.Min(8f, 164f - dx), 2f, null, UIColors.Pencil);
        view.penaltyText = Text(stakes, "Penalty", 20f, 140f, 170f, 36.8f, "−$1,000", 32f, UIColors.Loss, bold: true);
        Localize(Text(stakes, "IfNot", 20f, 180.8f, 170f, 26.6f, "if not", 19f, UIColors.InkSoft), CrisisText.Table, "crisis.screen.stakes.if_not");
        Localize(Text(stakes, "Grows", 20f, 211.4f, 170f, 47.6f, "grows every crisis, max ×1.8", 17f, UIColors.InkSoft, wrap: true),
            CrisisText.Table, "crisis.screen.stakes.grows");
        Box(crt, "Tape", 1730f, 126f, 120f, 34f, null, UIColors.Tape, rotation: -6f);

        // chin: vents, power, light, knobs, floppy slot
        for (int i = 0; i < 5; i++)
            Shape(crt, "Vent" + i, 64f, 928f + i * 24f, 180f, 10f, UIColors.Dark(0.3f), 5f);
        Icon(crt, "Power", 1540f, 950f, 26f, "power", UIColors.WithAlpha(Ink, UIColors.SrgbAlpha(0.45f)));
        var ledGlow = Shape(crt, "LedGlow", 1576f, 954f, 18f, 18f, UIColors.WithAlpha(Green, 0.5f));
        ledGlow.kind = UIShape.Kind.Ellipse;
        ledGlow.glow = 12f;
        Oval(crt, "LedEdge", 1576f, 954f, 18f, 18f, Ink);
        Oval(crt, "Led", 1578f, 956f, 14f, 14f, Green);
        Knob(crt, "KnobA", 1640f, 936f, 0f);
        Knob(crt, "KnobB", 1720f, 936f, 40f);
        Shape(crt, "SlotShade", 1580f, 1019f, 260f, 6f, UIColors.Dark(0.2f), 3f);
        Shape(crt, "Slot", 1580f, 1022f, 260f, 14f, Ink, 4f);

        BuildPlayChin(crt, view, g);
        BuildResultChin(crt, view);
    }

    static void Knob(RectTransform parent, string name, float left, float top, float angle)
    {
        Oval(parent, name + "Drop", left, top + 3f, 56f, 56f, UIColors.WithAlpha(Ink, UIColors.SrgbAlpha(0.25f)));
        var knob = Node(parent, name, left, top, 56f, 56f, rotation: angle);
        Oval(knob, "Edge", 0f, 0f, 56f, 56f, Ink);
        Oval(knob, "Face", 3f, 3f, 50f, 50f, Beige);
        var shade = Shape(knob, "Shade", 3f, 3f, 50f, 50f, UIColors.WithAlpha(Ink, UIColors.SrgbAlpha(0.14f)));
        shade.kind = UIShape.Kind.Sector;
        shade.startAngle = 105f;
        shade.sweepAngle = 150f;
        Shape(knob, "Tick", 26f, 8f, 4f, 16f, Ink, 2f);
    }

    // controls sticky (keys + what to do) and Give up
    static void BuildPlayChin(RectTransform crt, CrtScreenView view, Spec g)
    {
        var chin = Stretch(crt, "PlayChin");
        view.playChin = chin.gameObject;
        var sticky = StickyNote(chin, "ControlsSticky", 296f, 906f, 820f, 124f, UIColors.StickyBlue, -1.5f);
        float x = 28f;
        var keys = Node(sticky, "Keys", 28f, 16f + (108f - 56f) * 0.5f, 520f, 56f);
        float kx = 0f;
        foreach (var k in g.keys)
            kx += Keycap(keys, k, kx, 0f) + 8f;
        kx -= 8f;
        keys.sizeDelta = new Vector2(kx, 56f);
        keys.anchoredPosition = new Vector2(28f + kx * 0.5f, keys.anchoredPosition.y);
        x += kx + 16f;
        var controls = Localize(Text(sticky, "Instructions", x, 16f, 820f - x - 24f, 108f, g.controls, 28f, UIColors.Ink, wrap: true),
            CrisisText.Table, g.Key("instructions"));
        controls.alignment = TextAlignmentOptions.MidlineLeft;
        controls.enableAutoSizing = true;
        controls.fontSizeMin = 18f;
        controls.fontSizeMax = 28f;
        controls.lineSpacing = (1.2f - UIFonts.NaturalLineHeight(UIBuild.Font)) * 100f;

        // Give up (−$1,000) Esc
        var btn = MarkerButtonNode(chin, "GiveUpButton", 1150f, 940f, 352f, 54f, Anchor.TopLeft, UIColors.PaperHi, new Vector2(4f, 5f),
            out RectTransform face, out RectTransform content);
        // flag, label and the Esc key centred as a group (the label's amount changes with the penalty)
        var row = Stretch(content, "Row");
        HLayout(row.gameObject, 10f, Pad(18, 18, 0, 0), TextAnchor.MiddleCenter, true, true, false, false);
        var flag = LayoutIcon(row, "Flag", 24f, "flag", UIColors.Ink);
        view.giveUpLabel = ShrinkToFit(LayoutText(row, "Label", "Give up (−$1,000)", 22f, UIColors.Ink), 18f);   // the view writes it
        float escW = Mathf.Max(36f, TextWidth("Esc", 17f, true) + 18f);
        var esc = NewRect(row, "Esc");
        esc.sizeDelta = new Vector2(escW, 32f);
        Layout(esc, escW, 32f, escW, 32f);
        Shape(esc, "Edge", 0f, 0f, escW, 32f, UIColors.Ink).Round(SmallX, SmallY);
        Shape(esc, "Face", 2f, 2f, escW - 4f, 26f, UIColors.PaperHi).Round(SmallX - Vector4.one * 2f, SmallY - new Vector4(2f, 2f, 4f, 4f));
        Localize(Text(esc, "Key", 0f, 0f, escW, 28f, "Esc", 17f, UIColors.Ink, bold: true, align: TextAlignmentOptions.Center), CrisisText.Table, "crisis.key.esc");
        SetupMarker(btn, face, new Graphic[] { flag, view.giveUpLabel });
        view.giveUpButton = btn.GetComponent<Button>();
    }

    // result state: Back to work (closes by itself)
    static void BuildResultChin(RectTransform crt, CrtScreenView view)
    {
        var chin = Stretch(crt, "ResultChin");
        view.resultChin = chin.gameObject;
        float tw = TextWidth("Back to work", 32f, true);
        float w = 44f + tw + 12f + 32f + 44f;
        var btn = MarkerButtonNode(chin, "BackButton", 296f, 916f, w, 80f, Anchor.TopLeft, UIColors.Highlight, new Vector2(5f, 6f),
            out RectTransform face, out RectTransform content);
        // label and arrow centred as a group in the English width (a longer translation shrinks)
        var row = Stretch(content, "Row");
        HLayout(row.gameObject, 12f, Pad(44, 44, 0, 0), TextAnchor.MiddleCenter, true, true, false, false);
        var label = ShrinkToFit(Localize(LayoutText(row, "Label", "Back to work", 32f, UIColors.Ink, true), CrisisText.Table, "crisis.screen.back_to_work"), 24f);
        var arrow = LayoutIcon(row, "Arrow", 32f, "arrow-right", UIColors.Ink, "b");
        SetupMarker(btn, face, new Graphic[] { label, arrow });
        view.backButton = btn.GetComponent<Button>();
        view.closesText = Text(chin, "ClosesIn", 296f + w + 28f, 916f + 24f, 400f, 30.8f, "closes by itself in 3…", 22f, UIColors.InkSoft);
        chin.gameObject.SetActive(false);
    }

    // scanlines over the game, and the flash the games use
    static GameObject BuildGlassFx(RectTransform panel)
    {
        var fx = Shape(panel, "GlassFx", GX, GY, GW, GH, Color.white);
        fx.Round(new Vector4(44, 44, 44, 44), new Vector4(40, 40, 40, 40));
        fx.gameObject.AddComponent<Mask>().showMaskGraphic = false;
        var flash = StretchBox(fx.rectTransform, "FlashOverlay", null, new Color(0f, 0f, 0f, 0f));
        flash.raycastTarget = false;
        var scan = StretchBox(fx.rectTransform, "Scanlines", S("Kit/Tiles/crt_scan"), Lit(Paper, 0.045f));
        scan.type = Image.Type.Tiled;
        scan.raycastTarget = false;
        return fx.gameObject;
    }

    // ------------------------------------------------------------------
    // Result on the glass (board "Crisis result")

    static void BuildResult(RectTransform panel, CrtScreenView view)
    {
        var layer = Node(panel, "ResultLayer", GX, GY, GW, GH);
        view.resultLayer = layer.gameObject;
        view.resultTag = Text(layer, "Tag", 0f, 96f, GW, 30.8f, "STOP THE BAR · SOLVED", 22f, Blue, align: TextAlignmentOptions.Center, letterSpacingEm: 0.14f);
        view.resultHeadline = Text(layer, "Headline", 0f, 144.8f, GW, 115.5f, "CRISIS SOLVED", 110f, Green, bold: true, align: TextAlignmentOptions.Center);
        view.resultHeadline.fontSharedMaterial = glowGreen;
        view.resultLine = Text(layer, "Line", 0f, 278.3f, GW, 41.6f, "Servers are back online.", 32f, Paper, align: TextAlignmentOptions.Center);
        view.resultAmount = Text(layer, "Amount", 0f, 432f, GW, 100.8f, "+$2,400", 96f, Green, bold: true, align: TextAlignmentOptions.Center);
        view.resultAmount.fontSharedMaterial = glowGreen;
        view.resultMaths = Text(layer, "Maths", 0f, 542.8f, GW, 36.4f, "$2,000 × 1.2 · the bonus grows with every crisis", 26f, Blue, align: TextAlignmentOptions.Center);
        layer.gameObject.SetActive(false);

        // paper stamp slapped across the glass and bezel
        float tw = Mathf.Max(TextWidth("SOLVED", 64f, true, 0.12f), TextWidth("FAILED", 64f, true, 0.12f));
        float w = tw + 60f + 12f, h = 64f * 1.535f + 16f + 12f;
        var stamp = Node(panel, "ResultStamp", 1210f, 600f, w, h, rotation: -12f);
        view.resultStamp = stamp.gameObject;
        Shadow(stamp, "Shadow", 0f, 0f, w, h, 0f, 10f, 18f, 0.3f);
        Shape(stamp, "Paper", 0f, 0f, w, h, UIColors.WithAlpha(Paper, 0.9f), 12f);
        var border = Shape(stamp, "Border", 0f, 0f, w, h, UIColors.Money, 12f);
        border.border = 6f;
        var outline = Shape(stamp, "Outline", -8f, -8f, w + 16f, h + 16f, UIColors.Money, 20f);
        outline.border = 3f;
        view.resultStampText = ShrinkToFit(Text(stamp, "Word", 0f, 14f, w, h - 28f, "SOLVED", 64f, UIColors.Money, bold: true, align: TextAlignmentOptions.Center,
            letterSpacingEm: 0.12f), 48f);   // the view writes it; a longer word shrinks
        view.resultStampInk = new Graphic[] { border, outline, view.resultStampText };
        stamp.gameObject.SetActive(false);
    }

    // ------------------------------------------------------------------
    // "Crisis incoming" card over the paused office

    static void BuildWarning(RectTransform panel, CrtScreenView view, Spec g)
    {
        var w = Stretch(panel, "Warning");
        view.warning = w.gameObject;
        var vig = StretchShape(w, "Vignette", UIColors.Over(UIColors.Hex(0xC0352B), 0.55f, UIColors.Hex(0x14110D)));
        vig.glow = -240f;

        // "Game paused" chip
        var chip = Node(w, "Paused", 846f, 28f, 228f, 52f);
        Shape(chip, "Shadow", 3f, 4f, 228f, 52f, UIColors.WithAlpha(Ink, UIColors.SrgbAlpha(0.9f))).Round(SmallX, SmallY);
        Shape(chip, "Paper", 0f, 0f, 228f, 52f, UIColors.PaperHi).Round(SmallX, SmallY);
        var chipEdge = Shape(chip, "Edge", 0f, 0f, 228f, 52f, Ink);
        chipEdge.Round(SmallX, SmallY);
        chipEdge.border = 2f;
        // icon and label centred as a group, in every language
        var chipRow = Stretch(chip, "Row");
        HLayout(chipRow.gameObject, 10f, Pad(12, 12, 0, 0), TextAnchor.MiddleCenter, true, true, false, false);
        LayoutIcon(chipRow, "Icon", 24f, "pause", Ink);
        ShrinkToFit(Localize(LayoutText(chipRow, "Label", "Game paused", 22f, Ink), CrisisText.Table, "crisis.screen.warning.paused"), 16f);

        // the card
        const float L = 500f, T = 280f, W = 920f, H = 520f;
        var card = Node(w, "Card", L, T, W, H);
        Shadow(card, "Shadow", 0f, 0f, W, H, 0f, 22f, 48f, 0.45f);
        Shape(card, "Paper", 0f, 0f, W, H, UIColors.PaperHi).Round(PanelX, PanelY);
        var cardEdge = Shape(card, "Edge", 0f, 0f, W, H, Ink);
        cardEdge.Round(PanelX, PanelY);
        cardEdge.border = 3f;
        // pin
        Oval(card, "PinShadow", 451f, -5f, 22f, 22f, UIColors.Dark(0.35f));
        Oval(card, "PinEdge", 449f, -9f, 22f, 22f, Ink);
        Oval(card, "Pin", 451f, -7f, 18f, 18f, UIColors.Hex(0xD8413A));
        Oval(card, "PinShine", 455f, -4f, 7f, 7f, UIColors.Hex(0xF08A84));

        // countdown
        Localize(Text(card, "StartingIn", 680f, 39f, 200f, 30.8f, "Starting in…", 22f, UIColors.InkSoft, align: TextAlignmentOptions.Center),
            CrisisText.Table, "crisis.screen.warning.starting_in");
        var ring = Node(card, "Countdown", 696f, 79.8f, 168f, 168f);
        Oval(ring, "Rest", 0f, 0f, 168f, 168f, LossSoft);
        var sweep = Shape(ring, "Sweep", 0f, 0f, 168f, 168f, UIColors.Loss);
        sweep.SetSector(0f, 316.8f);
        view.countdownRing = sweep;
        var ringEdge = Oval(ring, "Edge", 0f, 0f, 168f, 168f, Ink);
        ringEdge.border = 3f;
        Oval(ring, "Face", 20f, 20f, 128f, 128f, UIColors.PaperHi);
        var faceEdge = Oval(ring, "FaceEdge", 20f, 20f, 128f, 128f, Ink);
        faceEdge.border = 3f;
        view.countdownNumber = Text(ring, "Number", 20f, 20f, 128f, 128f, "3", 96f, Ink, bold: true, align: TextAlignmentOptions.Center);

        // URGENT stamp and the crisis
        float uw = TextWidth("URGENT", 44f, true, 0.12f) + 44f + 10f, uh = 44f * 1.15f + 12f + 10f;
        var urgent = Node(card, "Urgent", 43f, 47f, uw, uh, rotation: -10f);
        urgent.gameObject.AddComponent<CanvasGroup>().alpha = 0.88f;
        var ub = Shape(urgent, "Border", 0f, 0f, uw, uh, UIColors.Loss, 10f);
        ub.border = 5f;
        var uo = Shape(urgent, "Outline", -6f, -6f, uw + 12f, uh + 12f, UIColors.Loss, 16f);
        uo.border = 2f;
        Localize(Text(urgent, "Word", 0f, 0f, uw, uh, "URGENT", 44f, UIColors.Loss, bold: true, align: TextAlignmentOptions.Center, letterSpacingEm: 0.12f),
            CrisisText.Table, "crisis.screen.warning.urgent");
        // two CSS line boxes of 1.15: TMP needs the first line's full glyph height (1.515 em) to fit two lines, or the
        // auto-size shrinks the text, so the box grows around the same first baseline
        float glyphs = 44f * UIFonts.ContentHeight(UIBuild.Font), pitch = 44f * 1.15f;
        view.warningCrisis = Text(card, "Crisis", 43f, 47f + uh + 24f - (glyphs - pitch) * 0.5f, 600f, glyphs + pitch + 2f,
            "Server is crashing! Get systems back online!", 44f, Ink, bold: true, wrap: true);
        view.warningCrisis.alignment = TextAlignmentOptions.TopLeft;
        view.warningCrisis.enableAutoSizing = true;
        view.warningCrisis.fontSizeMin = 30f;
        view.warningCrisis.fontSizeMax = 44f;
        view.warningCrisis.lineSpacing = (1.15f - UIFonts.NaturalLineHeight(UIBuild.Font)) * 100f;

        // Mini-game: Title | keys + controls. The view writes the title line in the current language and moves the rest (the
        // Tail: divider, keys, controls) to start after it; as built it follows the English
        float stripTop = 47f + uh + 24f + 101.2f + 28f;
        var strip = Node(card, "Strip", 43f, stripTop, W - 86f, 64f);
        Shape(strip, "Paper", 0f, 0f, W - 86f, 64f, UIColors.Paper).Round(SmallX, SmallY);
        var stripEdge = Shape(strip, "Edge", 0f, 0f, W - 86f, 64f, Ink);
        stripEdge.Round(SmallX, SmallY);
        stripEdge.border = 2f;
        string mini = "<color=#474F60>Mini-game:</color> <b>" + g.title + "</b>";
        view.warningGame = Text(strip, "Game", 20f, 0f, 360f, 64f, mini, 22f, Ink);
        view.warningGame.richText = true;
        float gw = TextWidth("Mini-game: ", 22f) + TextWidth(g.title, 22f, true) + 4f;
        var tail = Stretch(strip, "Tail", 20f + gw + 16f, 0f, 16f, 0f);
        view.warningStripTail = tail;
        Box(tail, "Divider", 0f, 14f, 2f, 36f, null, Ink);
        var keys = Node(tail, "Keys", 18f, 10f, 400f, 44f);
        float kx = 0f;
        foreach (var k in g.keys)
            kx += Keycap(keys, k, kx, 0f) + 8f;
        kx = Mathf.Max(0f, kx - 8f);
        keys.localScale = new Vector3(0.65f, 0.65f, 1f);
        keys.pivot = new Vector2(0f, 0.5f);
        keys.sizeDelta = new Vector2(kx, 56f);
        keys.anchoredPosition = new Vector2(18f, -32f);
        // the controls take the rest: one line, two when long, shrinking to 15 px
        var ctl = Localize(SetupText(Stretch(tail, "Controls", 18f + kx * 0.65f + 12f, 0f, 0f, 0f).gameObject, g.controls, 22f, Ink, wrap: true),
            CrisisText.Table, g.Key("instructions"));
        ctl.enableAutoSizing = true;
        ctl.fontSizeMin = 15f;
        ctl.fontSizeMax = 22f;

        // stakes chips and Start now
        float rowTop = stripTop + 64f + 28f;
        var chips = Node(card, "Chips", 43f, rowTop + 12f, 520f, 44f);
        HLayout(chips.gameObject, 12f, null, TextAnchor.MiddleLeft, true, true, false, false);
        view.warningReward = Chip(chips, "Reward", UIColors.Money, "check", "+$2,000 if solved");
        view.warningPenalty = Chip(chips, "Penalty", UIColors.Loss, "cross", "−$1,000 if not");
        float bw = 32f + 28f + 12f + TextWidth("Start now", 28f, true) + 32f;
        var start = MarkerButtonNode(card, "StartButton", W - 43f - bw, rowTop, bw, 68f, Anchor.TopLeft, UIColors.Highlight, new Vector2(5f, 6f),
            out RectTransform sface, out RectTransform scontent);
        // icon and label centred as a group in the English width (a longer translation shrinks)
        var srow = Stretch(scontent, "Row");
        HLayout(srow.gameObject, 12f, Pad(32, 32, 0, 0), TextAnchor.MiddleCenter, true, true, false, false);
        var play = LayoutIcon(srow, "Play", 28f, "play", Ink);
        var slabel = ShrinkToFit(Localize(LayoutText(srow, "Label", "Start now", 28f, Ink, true), CrisisText.Table, "crisis.screen.warning.start_now"), 22f);
        SetupMarker(start, sface, new Graphic[] { play, slabel });
        view.startNowButton = start.GetComponent<Button>();
    }

    static TextMeshProUGUI Chip(RectTransform parent, string name, Color fill, string icon, string text)
    {
        var chip = NewRect(parent, name);
        HLayout(chip.gameObject, 8f, Pad(16, 16, 0, 0), TextAnchor.MiddleLeft, true, true, false, false);
        Layout(chip, -1f, 44f, -1f, 44f);
        var bg = StretchShape(chip, "Fill", fill);
        bg.Round(SmallX, SmallY);
        IgnoreLayout(bg);
        var edge = StretchShape(chip, "Edge", Ink);
        edge.Round(SmallX, SmallY);
        edge.border = 2f;
        IgnoreLayout(edge);
        LayoutIcon(chip, "Icon", 24f, icon, UIColors.PaperHi);
        return LayoutText(chip, "Label", text, 22f, UIColors.PaperHi);
    }

    // ------------------------------------------------------------------
    // Keycaps: Space / R / Enter labels, arrow keys, and the Click pill (56 px tall). Space, Enter and Click are words in
    // each language; the letter keys stay the letters they are.

    static float Keycap(RectTransform parent, string token, float x, float y)
    {
        var rot = new Dictionary<string, float> { { "right", 0f }, { "down", -90f }, { "left", 180f }, { "up", 90f } };
        if (token == "click")
        {
            // room for the widest language's word (Japanese needs twice the English), the label centred in it
            float lw = Mathf.Max(TextWidth("Click", 26f, true), WidestText("crisis.key.click", 26f, true));
            float w = 16f + 28f + 8f + lw + 16f;
            var pill = Node(parent, "Click", x, y, w, 56f);
            Shape(pill, "Edge", 0f, 0f, w, 56f, Ink, 28f);
            Shape(pill, "Fill", 3f, 3f, w - 6f, 50f, UIColors.Info, 25f);
            Icon(pill, "Mouse", 16f, 14f, 28f, "mouse", UIColors.PaperHi);
            ShrinkToFit(Localize(Text(pill, "Label", 52f, 0f, lw, 56f, "Click", 26f, UIColors.PaperHi, bold: true, align: TextAlignmentOptions.Center),
                CrisisText.Table, "crisis.key.click"), 20f);
            return w;
        }
        if (rot.ContainsKey(token))
        {
            var key = Node(parent, "Key_" + token, x, y, 56f, 56f);
            KeyBody(key, 56f, UIColors.PaperHi);
            var arrow = Icon(key, "Arrow", 13f, 10f, 30f, "arrow-right", Ink, "b");
            arrow.rectTransform.localEulerAngles = new Vector3(0f, 0f, rot[token]);
            return 56f;
        }
        var tint = new Dictionary<string, Color[]>
        {
            { "R", new[] { UIColors.Loss, UIColors.PaperHi } }, { "G", new[] { UIColors.Money, UIColors.PaperHi } },
            { "B", new[] { UIColors.Info, UIColors.PaperHi } }, { "Y", new[] { UIColors.Highlight, Ink } },
        };
        Color bg = tint.ContainsKey(token) ? tint[token][0] : UIColors.PaperHi;
        Color ink = tint.ContainsKey(token) ? tint[token][1] : Ink;
        float kw = Mathf.Max(token.Length > 2 ? 120f : 56f, 28f + TextWidth(token, 26f, true));
        var cap = Node(parent, "Key_" + token, x, y, kw, 56f);
        KeyBody(cap, kw, bg);
        var label = Text(cap, "Label", 6f, 0f, kw - 12f, 52f, token, 26f, ink, bold: true, align: TextAlignmentOptions.Center);
        if (token == "Space") ShrinkToFit(Localize(label, CrisisText.Table, "crisis.key.space"), 18f);
        else if (token == "Enter") ShrinkToFit(Localize(label, CrisisText.Table, "crisis.key.enter"), 18f);
        return kw;
    }

    // 3 px ink border, 7 px at the bottom
    static void KeyBody(RectTransform key, float w, Color face)
    {
        Shape(key, "Edge", 0f, 0f, w, 56f, Ink).Round(SmallX, SmallY);
        Shape(key, "Face", 3f, 3f, w - 6f, 46f, face).Round(SmallX - Vector4.one * 3f, SmallY - Vector4.one * 3f);
    }

    // ------------------------------------------------------------------
    // Shape helpers (design coordinates, y down, in the parent's top-left frame)

    static UIShape Shape(Transform parent, string name, float left, float top, float w, float h, Color color, float radius = 0f,
        Anchor anchor = Anchor.TopLeft, float rotation = 0f)
    {
        var rt = Node(parent, name, left, top, w, h, anchor, rotation);
        var s = rt.gameObject.AddComponent<UIShape>();
        s.raycastTarget = false;
        s.color = color;
        if (radius > 0f) s.Round(radius);
        return s;
    }

    static UIShape Oval(Transform parent, string name, float left, float top, float w, float h, Color color, Anchor anchor = Anchor.TopLeft)
    {
        var s = Shape(parent, name, left, top, w, h, color, 0f, anchor);
        s.kind = UIShape.Kind.Ellipse;
        return s;
    }

    static UIShape StretchShape(RectTransform parent, string name, Color color)
    {
        var rt = Stretch(parent, name);
        var s = rt.gameObject.AddComponent<UIShape>();
        s.raycastTarget = false;
        s.color = color;
        return s;
    }

    /// <summary>Soft halo around a box (CSS box-shadow 0 0 blur colour), outside only.</summary>
    static UIShape Halo(Transform parent, string name, float left, float top, float w, float h, Color color, float blur, float radius, bool ellipse = false)
    {
        var s = Shape(parent, name, left, top, w, h, color, radius);
        if (ellipse) s.kind = UIShape.Kind.Ellipse;
        s.glow = blur;
        return s;
    }

    /// <summary>Stroked polylines; points in the node's box, y down (like the SVG they come from).</summary>
    static UIShape Lines(Transform parent, string name, float left, float top, float w, float h, Color color, float width, params Vector2[][] paths)
    {
        var s = Shape(parent, name, left, top, w, h, color);
        s.strokeWidth = width;
        foreach (var p in paths)
        {
            var local = new Vector2[p.Length];
            for (int i = 0; i < p.Length; i++) local[i] = new Vector2(p[i].x - w * 0.5f, h * 0.5f - p[i].y);
            s.AddStroke(local);
        }
        return s;
    }

    /// <summary>Filled convex polygon; points in the node's box, y down.</summary>
    static UIShape Poly(Transform parent, string name, float left, float top, float w, float h, Color color, params Vector2[] pts)
    {
        var s = Shape(parent, name, left, top, w, h, color);
        var local = new Vector2[pts.Length];
        for (int i = 0; i < pts.Length; i++) local[i] = new Vector2(pts[i].x - w * 0.5f, h * 0.5f - pts[i].y);
        s.AddPolygon(local);
        return s;
    }

    static Vector2 V(float x, float y) => new Vector2(x, y);

    /// <summary>Points along a circular arc (degrees, SVG y-down angles).</summary>
    static Vector2[] Arc(float cx, float cy, float r, float a0, float a1, int steps = 24)
    {
        var pts = new Vector2[steps + 1];
        for (int i = 0; i <= steps; i++)
        {
            float a = Mathf.Lerp(a0, a1, i / (float)steps) * Mathf.Deg2Rad;
            pts[i] = new Vector2(cx + Mathf.Cos(a) * r, cy + Mathf.Sin(a) * r);
        }
        return pts;
    }

    static TextMeshProUGUI RowText(RectTransform row, string name, string text, float size, Color color, bool bold, float spacing)
    {
        var rt = NewRect(row, name);
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
        rt.pivot = new Vector2(0f, 0.5f);
        rt.sizeDelta = new Vector2(200f, size * UIFonts.ContentHeight(UIBuild.Font));
        rt.anchoredPosition = Vector2.zero;
        return SetupText(rt.gameObject, text, size, color, bold, TextAlignmentOptions.Left, spacing);
    }

    /// <summary>A sticky note with its darker top band and soft shadow (fixed size).</summary>
    static RectTransform StickyNote(Transform parent, string name, float left, float top, float w, float h, Color color, float rotation)
    {
        var note = Node(parent, name, left, top, w, h, rotation: rotation);
        Shadow(note, "Shadow", 0f, 0f, w, h, 0f, 12f, 20f, 0.28f);
        Box(note, "Paper", 0f, 0f, w, h, null, color);
        Box(note, "Band", 0f, 0f, w, 20f, null, UIColors.Dark(0.07f));
        return note;
    }

    static TextMeshProUGUI Glowing(TextMeshProUGUI t, Material m)
    {
        if (t != null && m != null) t.fontSharedMaterial = m;
        return t;
    }

    /// <summary>A box measured from the English: a longer translation shrinks to fit (down to min) instead of spilling out.</summary>
    static T ShrinkToFit<T>(T text, float min) where T : TMP_Text
    {
        text.enableAutoSizing = true;
        text.fontSizeMin = min;
        text.fontSizeMax = text.fontSize;
        return text;
    }

    static Dictionary<string, string[]> crisisTexts;   // Crisis.csv: key -> its text in every language

    /// <summary>One line of a Crisis.csv entry at its widest across the languages (the builder's font and its fallbacks).</summary>
    static float WidestText(string key, float size, bool bold = false, float letterSpacingEm = 0f)
    {
        if (crisisTexts == null)
        {
            crisisTexts = new Dictionary<string, string[]>();
            string path = L10n.SourceFolder + "/" + CrisisText.Table + ".csv";
            if (File.Exists(path))
            {
                var sheet = L10nCsv.ReadSheet(File.ReadAllText(path, System.Text.Encoding.UTF8));
                int keyColumn = sheet.Column("Key");
                foreach (var row in sheet.Rows)
                {
                    var texts = new List<string>();
                    foreach (var language in L10n.Languages)
                    {
                        string text = L10nCsv.Sheet.Cell(row, sheet.Column(language.Code));
                        if (text.Length > 0) texts.Add(text);
                    }
                    crisisTexts[L10nCsv.Sheet.Cell(row, keyColumn).Trim()] = texts.ToArray();
                }
            }
        }
        float w = 0f;
        if (crisisTexts.TryGetValue(key, out var all))
            foreach (string text in all)
                w = Mathf.Max(w, TextWidth(text, size, bold, letterSpacingEm));
        return w;
    }

    /// <summary>TMP input field with its viewport; text drawn by the field (or hidden when the game draws it).</summary>
    static TMP_InputField InputBox(RectTransform parent, string name, float left, float top, float w, float h, float size, Color textColor,
        bool bold, float spacing, TMP_InputField.ContentType type, int limit)
    {
        var rt = Node(parent, name, left, top, w, h);
        var hit = AddImage(rt.gameObject, null, UIBuild.Clear, true);
        var area = Stretch(rt, "Text Area", 12f, 0f, 12f, 0f);
        area.gameObject.AddComponent<RectMask2D>();
        var textRt = Stretch(area, "Text");
        var text = SetupText(textRt.gameObject, "", size, textColor, bold, TextAlignmentOptions.Center, spacing);
        text.textWrappingMode = TextWrappingModes.NoWrap;
        var input = rt.gameObject.AddComponent<TMP_InputField>();
        input.textViewport = area;
        input.textComponent = text;
        input.targetGraphic = hit;
        input.fontAsset = UIBuild.Font;
        input.pointSize = size;
        input.contentType = type;
        input.characterLimit = limit;
        input.customCaretColor = true;
        input.caretColor = Yellow;
        input.caretWidth = 3;
        input.selectionColor = Lit(Yellow, 0.35f);
        input.richText = false;
        input.transition = Selectable.Transition.None;
        return input;
    }

    // ------------------------------------------------------------------
    // MainGame

    public static void Install(GameObject prefab, List<string> log)
    {
        Scene scene = SceneManager.GetSceneByPath(OfficeHudBuilder.ScenePath);
        if (!scene.IsValid() || !scene.isLoaded)
            scene = EditorSceneManager.OpenScene(OfficeHudBuilder.ScenePath, OpenSceneMode.Single);

        MiniGameManager manager = null;
        GameObject existing = null;
        foreach (var r in scene.GetRootGameObjects())
        {
            if (manager == null) manager = r.GetComponentInChildren<MiniGameManager>(true);
            if (existing == null && PrefabUtility.GetCorrespondingObjectFromSource(r) == prefab) existing = r;
        }
        if (manager == null)
        {
            log.Add("MainGame: no MiniGameManager, crisis games not installed.");
            return;
        }
        GameObject instance = existing;
        if (instance == null)
        {
            instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.name = "CrisisGames";
            log.Add("MainGame: added the CrisisGames prefab.");
        }
        else
        {
            log.Add("MainGame: CrisisGames already there (the prefab update applies to it).");
        }
        instance.SetActive(true);

        var so = new SerializedObject(manager);
        int wired = 0;
        foreach (var g in Specs)
        {
            var panel = instance.transform.Find(g.panel);
            var prop = so.FindProperty(g.field);
            if (panel == null || prop == null) continue;
            prop.objectReferenceValue = panel.gameObject;
            wired++;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        log.Add("MainGame: MiniGameManager plays " + wired + " crisis games on the office CRT (the old panels stay in the scene, unused).");

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        log.Add("MainGame saved.");
    }

    /// <summary>Point MiniGameManager back at the old panels (by name under any other canvas) and switch CrisisGames off.</summary>
    public static void Restore(List<string> log)
    {
        Scene scene = SceneManager.GetSceneByPath(OfficeHudBuilder.ScenePath);
        if (!scene.IsValid() || !scene.isLoaded)
            scene = EditorSceneManager.OpenScene(OfficeHudBuilder.ScenePath, OpenSceneMode.Single);
        MiniGameManager manager = null;
        GameObject crisis = null;
        foreach (var r in scene.GetRootGameObjects())
        {
            if (manager == null) manager = r.GetComponentInChildren<MiniGameManager>(true);
            if (r.name == "CrisisGames") crisis = r;
        }
        if (manager == null) return;
        var so = new SerializedObject(manager);
        int wired = 0;
        foreach (var g in Specs)
        {
            GameObject old = null;
            foreach (var canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (crisis != null && canvas.transform.IsChildOf(crisis.transform)) continue;
                var t = canvas.transform.Find(g.panel);
                if (t != null) { old = t.gameObject; break; }
            }
            var prop = so.FindProperty(g.field);
            if (prop == null) continue;
            prop.objectReferenceValue = old;
            if (old != null) wired++;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        if (crisis != null) crisis.SetActive(false);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        log.Add("MainGame: " + wired + " old crisis panels back in use, CrisisGames switched off.");
    }
}
