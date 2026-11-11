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
/// Builds the designed 2D main menu (canvas boards "Main menu", "New game: coin in the air / tails / heads",
/// "New game: confirm", "Volume dice") into Assets/UI/Prefabs/MainMenu.prefab and wires it to the Main_Menu scene's
/// MainMenuUI: the legal pad's buttons, the confirm sheet, the Settings folder (the pause menu's, reused) and the
/// card table's 2D coin (New game) and dice (master volume) instead of the 3D toss and dice.
/// </summary>
public static class MainMenuBuilder
{
    public const string PrefabPath = "Assets/UI/Prefabs/MainMenu.prefab";
    public const string ScenePath = "Assets/Scenes/Main_Menu.unity";
    /// <summary>The String Table of the fixed labels (Assets/Localization/Source/Menu.csv).</summary>
    const string Table = "Menu";

    static readonly Color Wood = UIColors.ClipboardDark;
    static readonly Color FeltShadow = UIColors.Hex(0x0B2A1B);
    static readonly Color Cream = UIColors.PaperHi;
    static readonly Color Night = UIColors.Hex(0x0B0B0C);

    [MenuItem("Tools/Game/New UI/Build Main Menu")]
    public static void BuildFromMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Build Main Menu", "Stop Play mode first.", "OK");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var log = new List<string>();
        var prefab = BuildPrefab(log);
        if (prefab != null) Install(prefab, log);
        Debug.Log("[MainMenuBuilder]\n" + string.Join("\n", log));
        EditorUtility.DisplayDialog("Main menu", string.Join("\n", log), "OK");
    }

    public static GameObject BuildPrefab(List<string> log)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
        GameObject root = null;
        try
        {
            root = Build();
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool ok);
            log.Add(ok ? "Saved " + PrefabPath : "Could not save " + PrefabPath);
            return ok ? prefab : null;
        }
        finally
        {
            if (root != null) Object.DestroyImmediate(root);
        }
    }

    static GameObject Build()
    {
        var root = new GameObject("MainMenu", typeof(RectTransform));
        root.layer = 5;
        var rt = (RectTransform)root.transform;
        rt.sizeDelta = new Vector2(1920f, 1080f);
        var view = root.AddComponent<MainMenuView>();
        var audio = root.AddComponent<AudioSource>();
        audio.playOnAwake = false;

        // the back room: gradient, lamp light, lamp
        var room = StretchBox(rt, "Room", S("Kit/Special/menu_room"), Color.white);
        room.type = Image.Type.Simple;
        room.raycastTarget = true;   // the menu is opaque: nothing behind it takes clicks
        var shaft = Box(rt, "LampLight", 1000f, 40f, 640f, 900f, S("Kit/Special/light_shaft"), Color.white, Anchor.Center);
        shaft.type = Image.Type.Simple;
        Box(rt, "LampCord", 1318f, 0f, 4f, 26f, null, Night, Anchor.Center);
        var lamp = Box(rt, "Lamp", 1210f, 20f, 220f, 70f, S("Kit/Special/menu_lamp"), Color.white, Anchor.Center);
        lamp.type = Image.Type.Simple;

        var coin = BuildTable(rt, view, audio);
        view.coin = coin;

        var menu = Stretch(rt, "Menu");
        view.menuGroup = menu.gameObject.AddComponent<CanvasGroup>();
        view.logo = BuildLogo(menu);
        BuildPad(menu, view);
        var chip = NewRect(menu, "Version");
        chip.anchorMin = chip.anchorMax = new Vector2(1f, 0f);
        chip.pivot = new Vector2(1f, 0f);
        chip.anchoredPosition = new Vector2(-32f, 32f);
        HLayout(chip.gameObject, 0f, Pad(14, 14, 4, 4), TextAnchor.MiddleCenter, true, true, false, false);
        Fit(chip.gameObject, true, true);
        IgnoreLayout(StretchBox(chip, "Fill", S("Kit/small_fill"), UIColors.Paper));
        IgnoreLayout(StretchBox(chip, "Border", S("Kit/small_line2"), UIColors.Ink));
        view.versionText = LayoutText(chip, "Text", "v0.1", 18f, UIColors.Ink, lineHeight: 1.4f);

        BuildAirNote(rt, view);
        BuildSlip(rt, view);
        BuildConfirm(rt, view);
        BuildSettingsPage(rt, view);
        BuildCredits(rt, view);

        // MainMenuUI writes SUCCESS / FAILURE here; the view shows the slip instead (kept active, drawn invisible)
        var sources = Stretch(rt, "Sources");
        var hidden = sources.gameObject.AddComponent<CanvasGroup>();
        hidden.alpha = 0f;
        hidden.blocksRaycasts = false;
        view.sourceResult = Text(sources, "Result", 0f, 0f, 400f, 60f, "FAILURE", 30f, UIColors.Ink);
        view.sourceResult.gameObject.SetActive(false);

        // scene fade (MainMenuUI.fadeOverlay)
        var fade = StretchBox(rt, "Fade", null, Color.black);
        var fadeGroup = fade.gameObject.AddComponent<CanvasGroup>();
        fadeGroup.alpha = 0f;
        fadeGroup.blocksRaycasts = false;
        fadeGroup.interactable = false;

        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20;
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        root.AddComponent<GraphicRaycaster>();
        return root;
    }

    // ------------------------------------------------------------------
    // helpers

    static Color Hard(Color c, float cssAlpha) => UIColors.WithAlpha(c, UIColors.SrgbAlpha(cssAlpha));

    /// <summary>The label bound to its Menu table entry (a null key leaves it as it is: a name, a number, a text a view writes).</summary>
    static T Bind<T>(T text, string key) where T : TMP_Text => key != null ? Localize(text, Table, key) : text;

    static void PivotTopLeft(RectTransform rt)
    {
        Vector2 size = rt.sizeDelta;
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition += new Vector2(-size.x * 0.5f, size.y * 0.5f);
    }

    /// <summary>Rubber stamp word (auto width) centred at cx, cy of the parent's top-left frame.</summary>
    static RectTransform Stamp(Transform parent, string name, float cx, float cy, string word, float size, Color color, float rotation,
        float alpha, RectOffset pad, Color? paper = null, string key = null)
    {
        var st = NewRect(parent, name);
        st.anchorMin = st.anchorMax = new Vector2(0f, 1f);
        st.pivot = new Vector2(0.5f, 0.5f);
        st.anchoredPosition = new Vector2(cx, -cy);
        st.localEulerAngles = new Vector3(0f, 0f, -rotation);
        HLayout(st.gameObject, 0f, pad, TextAnchor.MiddleCenter, true, true, false, false);
        Fit(st.gameObject, true, true);
        var g = st.gameObject.AddComponent<CanvasGroup>();
        g.alpha = alpha;
        g.blocksRaycasts = false;
        if (paper.HasValue) IgnoreLayout(StretchBox(st, "Paper", S("Kit/r10_fill"), paper.Value));
        IgnoreLayout(StretchBox(st, "Border", S("Kit/r10_line5"), color));
        IgnoreLayout(StretchBox(st, "Outline", S("Kit/r14_line2"), color, -6f, -6f, -6f, -6f));
        Bind(LayoutText(st, "Word", word, size, color, bold: true, letterSpacingEm: 0.12f, lineHeight: 1.15f), key);
        return st;
    }

    static float StampWidth(string word, float size) => TextWidth(word, size, true, 0.12f) + 2f * (5f + 22f);

    /// <summary>
    /// Marker button with icon and label (icon after when iconAfter). With a key, the label is bound to its Menu entry,
    /// the button fits the widest language and the label shrinks a little rather than leave the button.
    /// </summary>
    static RectTransform LabelButton(Transform parent, string name, float left, float top, string label, string icon, bool bold, Color fill,
        Color ink, Vector2 shadowOffset, Color shadow, float width = -1f, bool iconAfter = false, float height = 68f, float textSize = 28f,
        int padX = 36, string key = null)
    {
        float w = width > 0f ? width : ButtonWidth(label, key, icon, bold, textSize, padX);
        var btn = MarkerButtonNode(parent, name, left, top, w, height, Anchor.TopLeft, fill, shadowOffset, out var face, out var content);
        btn.Find("Shadow").GetComponent<Image>().color = shadow;
        HLayout(content.gameObject, 12f, Pad(padX, padX, 0, 0), TextAnchor.MiddleCenter, true, true, false, false);
        var graphics = new List<Graphic>();
        if (icon != null && !iconAfter) graphics.Add(LayoutIcon(content, "Icon", 28f, icon, ink));
        var text = LayoutText(content, "Label", label, textSize, ink, bold: bold);
        if (key != null)
        {
            Localize(text, Table, key);
            PauseMenuBuilder.FitWidth(text, textSize);
        }
        graphics.Add(text);
        if (icon != null && iconAfter) graphics.Add(LayoutIcon(content, "Icon", 28f, icon, ink));
        SetupMarker(btn, face, graphics.ToArray());
        return btn;
    }

    /// <summary>Width of a label button (with a key: for the widest language of its Menu entry).</summary>
    static float ButtonWidth(string label, string key, string icon, bool bold, float textSize = 28f, float padX = 36f) =>
        2f * padX + (icon != null ? 40f : 0f) + (key != null ? PauseMenuBuilder.WidestText(Table, key, label, textSize, bold) : TextWidth(label, textSize, bold));

    // ------------------------------------------------------------------
    // the card table: 1040 x 876 at 800, 132, 18 px wood rail; children in felt coords (1004 x 840)

    static MenuCoinFlip2D BuildTable(RectTransform rt, MainMenuView view, AudioSource audio)
    {
        Shadow(rt, "TableShadow", 800f, 132f, 1040f, 876f, 0f, 30f, 60f, 0.55f, Anchor.Center);
        Box(rt, "TableInk", 797f, 129f, 1046f, 882f, S("Kit/panel_fill"), UIColors.Ink, Anchor.Center);
        var table = Node(rt, "Table", 800f, 132f, 1040f, 876f, Anchor.Center);
        AddImage(table.gameObject, S("Kit/panel_fill"), Wood);
        var felt = StretchBox(table, "Felt", S("Kit/tiny_fill"), Color.white, 18f, 18f, 18f, 18f);
        felt.gameObject.AddComponent<Mask>().showMaskGraphic = false;
        var grad = StretchBox(felt.transform, "Gradient", S("Kit/Special/felt_radial"), Color.white);
        grad.type = Image.Type.Simple;
        var inner = Stretch(table, "Inner", 18f, 18f, 18f, 18f);
        StretchBox(inner, "GoldLine", S("Kit/card_line3"), UIColors.WithAlpha(UIColors.Gold, 0.8f), 12f, 12f, 12f, 12f);

        Bind(Text(inner, "Name", 40f, 30f, 400f, 26.4f, "THE BOARDROOM", 22f, UIColors.Gold, bold: true, letterSpacingEm: 0.2f), "mainmenu.table.name");
        Bind(Text(inner, "Limit", 564f, 34f, 400f, 20.4f, "TABLE LIMIT: YOUR LIFE SAVINGS", 17f, UIColors.WithAlpha(Cream, 0.7f),
            align: TextAlignmentOptions.Right, letterSpacingEm: 0.12f), "mainmenu.table.limit");

        // ----- the coin (New game)
        Bind(Text(inner, "CoinLabel", 0f, 78f, 1004f, 22.8f, "NEW GAME: HEADS OR TAILS", 19f, UIColors.Gold, align: TextAlignmentOptions.Center,
            letterSpacingEm: 0.16f), "mainmenu.table.coin_label");
        Box(inner, "Ring", 352f, 112f, 300f, 300f, S("Kit/Circles/circle300_line3"), UIColors.WithAlpha(UIColors.Gold, 0.85f));
        Dashed(Box(inner, "RingDashed", 366f, 126f, 272f, 272f, S("Kit/Circles/circle272_dash2"), UIColors.WithAlpha(UIColors.Gold, 0.5f))).type =
            Image.Type.Simple;
        Bind(Text(inner, "Heads", 60f, 222f, 262f, 44f, "HEADS", 40f, Cream, bold: true, align: TextAlignmentOptions.Right, letterSpacingEm: 0.08f),
            "mainmenu.table.heads");
        Bind(Text(inner, "HeadsLine", 60f, 270f, 262f, 28.6f, "the bank funds you", 22f, UIColors.WithAlpha(Cream, 0.8f), align: TextAlignmentOptions.Right),
            "mainmenu.table.heads_line");
        Bind(Text(inner, "Tails", 682f, 222f, 262f, 44f, "TAILS", 40f, Cream, bold: true, letterSpacingEm: 0.08f), "mainmenu.table.tails");
        Bind(Text(inner, "TailsLine", 682f, 270f, 262f, 28.6f, "back to the queue", 22f, UIColors.WithAlpha(Cream, 0.8f)), "mainmenu.table.tails_line");

        var coinView = inner.gameObject.AddComponent<MenuCoinFlip2D>();
        coinView.audioSource = audio;
        var shadow = Box(inner, "CoinShadow", 402f, 180f, 208f, 200f, S("Kit/Special/soft_ellipse"), Color.black);
        shadow.type = Image.Type.Simple;
        coinView.shadow = shadow.rectTransform;
        var coin = Node(inner, "Coin", 398f, 158f, 208f, 208f);
        coinView.coin = coin;
        var edge = Node(coin, "Edge", 0f, 10f, 208f, 208f);
        AddImage(edge.gameObject, S("Kit/Circles/circle208_fill"), UIColors.GoldDark);
        StretchBox(edge, "Ring", S("Kit/Circles/circle208_line4"), UIColors.Ink);
        coinView.edge = edge;
        var face = StretchBox(coin, "Face", S("Kit/Special/coin_face208"), Color.white);
        face.type = Image.Type.Simple;
        coinView.faceFill = face;
        coinView.headsColor = Color.white;
        coinView.tailsColor = UIColors.Hex(0xD8C08A);
        coinView.faceMarkLight = Text(coin, "MarkLight", 3f, 3f, 208f, 208f, "H", 104f, UIColors.Hex(0xF7DC87), bold: true,
            align: TextAlignmentOptions.Center);
        coinView.faceMark = Text(coin, "Mark", 0f, 0f, 208f, 208f, "H", 104f, UIColors.GoldDark, bold: true, align: TextAlignmentOptions.Center);

        var sparkles = new List<GameObject>();
        foreach (var (x, y, s) in new[] { (640f, 130f, 48f), (330f, 150f, 36f), (632f, 350f, 30f) })
        {
            var sp = Node(inner, "Sparkle", x, y, s, s);
            AddImage(sp.gameObject, IconSprite("sparkle", "fill"), UIColors.Highlight).preserveAspect = true;
            Icon(sp, "Outline", 0f, 0f, s, "sparkle", UIColors.Ink, "t");
            sp.gameObject.SetActive(false);
            sparkles.Add(sp.gameObject);
        }
        coinView.sparkles = sparkles.ToArray();
        var thud = Node(inner, "Thud", 352f, 384f, 300f, 40f);
        Box(thud, "L", 18f, 16f, 28f, 4f, null, UIColors.WithAlpha(Cream, 0.7f), Anchor.TopLeft, 42f);
        Box(thud, "M", 148f, 14f, 4f, 22f, null, UIColors.WithAlpha(Cream, 0.7f));
        Box(thud, "R", 254f, 16f, 28f, 4f, null, UIColors.WithAlpha(Cream, 0.7f), Anchor.TopLeft, -42f);
        thud.gameObject.SetActive(false);
        coinView.thud = thud.gameObject;

        // ----- the volume dice: place row 2-12 + ON puck + roll strip
        var dice = inner.gameObject.AddComponent<MenuDiceVolume2D>();
        dice.audioSource = audio;
        Bind(Text(inner, "VolumeLabel", 0f, 422f, 1004f, 22.8f, "VOLUME: ROLL THE DICE", 19f, UIColors.Gold, align: TextAlignmentOptions.Center,
            letterSpacingEm: 0.16f), "mainmenu.table.volume_label");
        // the percentages: the view writes them the language's way (Turkish %10)
        view.placePercents = new TMP_Text[11];
        view.placePercentValues = new int[11];
        for (int n = 2; n <= 12; n++)
        {
            float x = 44f + (n - 2) * 84f;
            var box = Node(inner, "Box" + n, x, 494f, 76f, 96f);
            dice.boxes[n - 2] = AddImage(box.gameObject, S("Kit/small_fill"), UIColors.WithAlpha(dice.boxCurrent, 0f));
            StretchBox(box, "Border", S("Kit/small_line2"), UIColors.Gold);
            Text(box, "Number", 0f, 10f, 76f, 38f, n.ToString(), 38f, Cream, bold: true, align: TextAlignmentOptions.Center);
            int pct = Mathf.Max(10, (n - 2) * 10);
            view.placePercents[n - 2] = Text(box, "Pct", 0f, 65.6f, 76f, 20.4f, pct + "%", 17f, UIColors.WithAlpha(Cream, 0.75f),
                align: TextAlignmentOptions.Center);
            view.placePercentValues[n - 2] = pct;
        }
        var puck = Node(inner, "Puck", 44f + 5 * 84f + 12f, 446f, 52f, 52f, Anchor.Center);
        var puckShadow = Box(puck, "Shadow", -6f, 0f, 64f, 64f, S("Kit/Special/soft_ellipse"), UIColors.WithAlpha(Color.black, 0.5f));
        puckShadow.type = Image.Type.Simple;
        Box(puck, "Fill", 0f, 0f, 52f, 52f, S("Kit/Circles/circle52_fill"), Cream);
        Box(puck, "Ring", 0f, 0f, 52f, 52f, S("Kit/Circles/circle52_line3"), UIColors.Ink);
        Bind(Text(puck, "On", 0f, 0f, 52f, 52f, "ON", 19f, UIColors.Ink, bold: true, align: TextAlignmentOptions.Center, letterSpacingEm: 0.04f),
            "mainmenu.table.puck_on");
        dice.puck = puck;

        Box(inner, "Strip", 44f, 628f, 916f, 110f, S("Kit/Pills/pill110_line2"), UIColors.WithAlpha(UIColors.Gold, 0.85f));
        Bind(Text(inner, "RollHere", 84f, 668f, 220f, 26.4f, "ROLL HERE", 22f, UIColors.Gold, bold: true, letterSpacingEm: 0.2f), "mainmenu.table.roll_here");
        var diceHit = Node(inner, "DiceHit", 330f, 630f, 380f, 106f);
        var diceHitImg = AddImage(diceHit.gameObject, null, Clear, true);
        dice.diceButton = diceHit.gameObject.AddComponent<Button>();
        dice.diceButton.transition = Selectable.Transition.None;
        dice.diceButton.targetGraphic = diceHitImg;
        dice.die1 = Die(inner, "Die1", 392f, 651f, -8f);
        dice.die2 = Die(inner, "Die2", 482f, 659f, 7f);
        // as drawn: 3 + 4 = 7 (50%), the puck on 7 (the view shows the saved volume at start)
        bool[] three = { true, false, false, true, false, false, true }, four = { true, true, false, false, false, true, true };
        for (int i = 0; i < 7; i++)
        {
            dice.die1.pips[i].SetActive(three[i]);
            dice.die2.pips[i].SetActive(four[i]);
        }
        dice.boxes[5].color = dice.boxCurrent;
        var roll = LabelButton(inner, "RollButton", 736f, 649f, "Roll", "dice", true, UIColors.Highlight, UIColors.Ink, new Vector2(5f, 6f),
            FeltShadow, 196f, key: "mainmenu.table.roll");
        dice.rollButton = roll.GetComponent<Button>();
        dice.caption = Text(inner, "Caption", 0f, 758f, 1004f, 26.6f, "Master volume 50% · the lowest roll still plays at 10%", 19f,
            UIColors.WithAlpha(Cream, 0.85f), align: TextAlignmentOptions.Center);

        float loudW = StampWidth("LOUD!", 36f), quietW = StampWidth("QUIET…", 36f), stampH = 36f * 1.15f + 18f;
        var stamp = Stamp(inner, "Verdict", 744f + loudW * 0.5f, 390f + stampH * 0.5f, "LOUD!", 36f, UIColors.Loss, -8f, 1f, Pad(23, 23, 9, 9),
            UIColors.WithAlpha(Cream, 0.9f));
        dice.stamp = stamp.gameObject;
        dice.stampText = stamp.Find("Word").GetComponent<TMP_Text>();
        dice.stampLines = new Graphic[] { stamp.Find("Border").GetComponent<Image>(), stamp.Find("Outline").GetComponent<Image>() };
        dice.loudStampPos = new Vector2(744f + loudW * 0.5f, -(390f + stampH * 0.5f));
        dice.quietStampPos = new Vector2(60f + quietW * 0.5f, -(390f + stampH * 0.5f));
        stamp.gameObject.SetActive(false);
        return coinView;
    }

    static MenuDiceVolume2D.Die Die(RectTransform parent, string name, float left, float top, float rotation)
    {
        var die = new MenuDiceVolume2D.Die();
        var rt = Node(parent, name, left, top, 64f, 64f, Anchor.TopLeft, rotation);
        die.root = rt;
        Box(rt, "Shadow", 4f, 5f, 64f, 64f, S("Kit/r12_fill"), Hard(FeltShadow, 0.7f));
        Box(rt, "Face", 0f, 0f, 64f, 64f, S("Kit/r12_fill"), Cream);
        Box(rt, "Border", 0f, 0f, 64f, 64f, S("Kit/r12_line3"), UIColors.Ink);
        // spots: tl, tr, ml, c, mr, bl, br at 25 / 50 / 75 % of the 58 px face inside the 3 px border
        float[] f = { 0.25f, 0.5f, 0.75f };
        (int, int)[] spots = { (0, 0), (2, 0), (0, 1), (1, 1), (2, 1), (0, 2), (2, 2) };
        const float pip = 11f;
        for (int i = 0; i < 7; i++)
        {
            float cx = 3f + 58f * f[spots[i].Item1], cy = 3f + 58f * f[spots[i].Item2];
            var p = Box(rt, "Pip" + i, cx - pip * 0.5f, cy - pip * 0.5f, pip, pip, S("Kit/Circles/circle12_fill"), UIColors.Ink);
            die.pips[i] = p.gameObject;
        }
        return die;
    }

    // ------------------------------------------------------------------
    // logo (the comic logo, 500 wide, over the pad's left edge; DemoMenuDecor's stamp and sticky go on its right) and the
    // legal pad (112, 440, 580 x 604)

    // Assets/UI/Logo (Tools/Splash/make_splash_assets.py): the user's original logo, only cropped, and its shape blurred for
    // the shadow, padded by LogoShadowPad of the logo's own pixels on every side. Shown at an even scale, never stretched.
    const float LogoLeft = 112f, LogoTop = 28f, LogoWidth = 500f;
    const float LogoShadowPad = 42f;

    /// <summary>The game's logo, an image (the same in every language), with a soft drop shadow.</summary>
    static RectTransform BuildLogo(RectTransform menu)
    {
        Sprite art = S("Logo/game_logo"), shadow = S("Logo/game_logo_shadow");
        float k = art != null ? LogoWidth / art.rect.width : 1f;
        float height = art != null ? art.rect.height * k : 400f;
        var logo = Node(menu, "Logo", LogoLeft, LogoTop, LogoWidth, height, Anchor.Center);
        PivotTopLeft(logo);
        if (shadow != null)
            Box(logo, "Shadow", -LogoShadowPad * k + 6f, -LogoShadowPad * k + 12f, shadow.rect.width * k, shadow.rect.height * k,
                shadow, Hard(Night, 0.55f));
        Box(logo, "Art", 0f, 0f, LogoWidth, height, art, Color.white);
        return logo;
    }

    static void BuildPad(RectTransform menu, MainMenuView view)
    {
        Shadow(menu, "PadShadow", 112f, 440f, 580f, 604f, 0f, 22f, 48f, 0.45f, Anchor.Center);
        var pad = Shape(menu, "Pad", 112f, 440f, 580f, 604f, "panel", UIColors.LegalPad, UIColors.Ink, 3, Anchor.Center);
        var clip = StretchBox(pad, "Clip", S("Kit/panel_fill"), Color.white, 3f, 3f, 3f, 3f);
        clip.gameObject.AddComponent<Mask>().showMaskGraphic = false;
        clip.transform.SetSiblingIndex(1);
        var rules = Box(clip.transform, "Rules", 0f, 31f, 574f, 41f * 14f, S("Kit/Tiles/legal_rule41"), Color.white);   // tiles start at the bottom
        rules.type = Image.Type.Tiled;
        Box(clip.transform, "Margin", 73f, 31f, 2f, 567f, null, UIColors.Margin);
        Box(clip.transform, "Binding", 0f, 0f, 574f, 28f, null, UIColors.Hex(0x7A2A24));
        Box(clip.transform, "BindingEdge", 0f, 28f, 574f, 3f, null, UIColors.Ink);
        float gap = (574f - 5f * 28f) / 6f;
        for (int i = 0; i < 5; i++)
        {
            var staple = Box(clip.transform, "Staple" + i, gap + i * (28f + gap), 10.5f, 28f, 7f, S("Kit/r4_fill"), UIColors.Hex(0xA9AEB6));
            Box(staple.rectTransform, "Edge", 0f, 0f, 28f, 7f, S("Kit/r4_line2"), UIColors.Ink);
        }

        // items (x 100, width 446): header, then rows of 82 (Continue 123), labels on the rules
        const float X = 100f;
        Bind(Text(pad, "Todo", X, 84f, 446f, 32f, "Today’s to-do:", 32f, UIColors.Pencil), "mainmenu.pad.todo");
        float y = 116f;
        var newGame = Item(pad, "NewGame", X, y, "New game", "mainmenu.pad.new_game", 0f);
        y += 82f;
        var cont = Item(pad, "Continue", X, y, "Continue", "mainmenu.pad.continue", 1.5f);
        view.continueButton = cont.GetComponent<Button>();
        view.continueGraphics = cont.GetComponent<MarkerButton>().content;
        Box(pad, "ContinueMark", X + 40f, y + 72f, 196f, 20f, IconSprite("underline-hand"), UIColors.Ink).type = Image.Type.Simple;
        Box(pad, "ContinueArrow", X - 88f, y + 44f, 70f, 40f, IconSprite("arrow-hand"), UIColors.Info).type = Image.Type.Simple;
        view.saveLine = Text(pad, "SaveLine", X + 52f, y + 104f, 394f, 19f, "Nova Holdings · Day 42 · $3,284,100", 19f, UIColors.InkSoft);
        y += 123f;
        var settings = Item(pad, "Settings", X, y, "Settings", "mainmenu.pad.settings", -1f);
        y += 82f;
        var credits = Item(pad, "Credits", X, y, "Credits", "mainmenu.pad.credits", 1f);
        view.creditsButton = credits.GetComponent<Button>();
        y += 82f;
        var quit = Item(pad, "Quit", X, y, "Quit", "mainmenu.pad.quit", -1.5f);

        // MainMenuUI's buttons are found by name at install
        newGame.name = "NewGameButton";
        cont.name = "ContinueButton";
        settings.name = "SettingsButton";
        credits.name = "CreditsButton";
        quit.name = "QuitButton";
    }

    /// <summary>A to-do line: hand-drawn box (34) + 40 px label, both sitting on the rule at the row's bottom.</summary>
    static RectTransform Item(RectTransform pad, string name, float x, float rowTop, string label, string key, float boxTilt)
    {
        var node = Node(pad, name, x - 8f, rowTop, 454f, 82f);
        var hit = AddImage(node.gameObject, null, Clear, true);
        var button = node.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = hit;
        var face = Stretch(node, "Face");
        var box = Icon(face, "Box", 8f, 44f, 34f, "todo-box", UIColors.Ink, null, Anchor.TopLeft, boxTilt);
        var text = Bind(Text(face, "Label", 60f, 42f, 390f, 40f, label, 40f, UIColors.Ink), key);
        var marker = node.gameObject.AddComponent<MarkerButton>();
        marker.face = face;
        marker.content = new Graphic[] { box, text };
        marker.disabledContentColor = UIColors.Pencil;
        return node;
    }

    // ------------------------------------------------------------------
    // coin notes: "Flipping..." sticky and the torn result slip

    static void BuildAirNote(RectTransform rt, MainMenuView view)
    {
        var group = Stretch(rt, "FlippingNote");
        Shadow(group, "NoteShadow", 128f, 736f, 560f, 236f, 0f, 16f, 28f, 0.4f, Anchor.Center, -2f);
        var note = Box(group, "Note", 128f, 736f, 560f, 236f, null, UIColors.StickyYellow, Anchor.Center, -2f).rectTransform;
        Box(note, "Band", 0f, 0f, 560f, 20f, null, UIColors.Dark(0.07f));
        Bind(Text(note, "Title", 32f, 34f, 500f, 50.6f, "Flipping…", 44f, UIColors.Ink, bold: true), "mainmenu.note.title");
        Bind(Text(note, "Rules", 32f, 94.6f, 500f, 64.8f, "Heads: the bank funds your company.\nTails: back to the queue.", 24f, UIColors.Ink,
            align: TextAlignmentOptions.TopLeft, lineHeight: 1.35f, wrap: true), "mainmenu.note.rules");
        Bind(Text(note, "Odds", 32f, 169.4f, 500f, 26.6f, "Every miss improves the odds.", 19f, UIColors.InkSoft), "mainmenu.note.odds");
        view.airNote = group.gameObject;
        group.gameObject.SetActive(false);
    }

    static void BuildSlip(RectTransform rt, MainMenuView view)
    {
        var slipRoot = Stretch(rt, "Slip");
        Shadow(slipRoot, "SlipShadow", 72f, 616f, 720f, 372f, 0f, 22f, 48f, 0.45f, Anchor.Center);
        var slip = Node(slipRoot, "Paper", 72f, 604f, 720f, 396f, Anchor.Center);
        var paper = AddImage(slip.gameObject, S("Kit/Special/torn_slip"), Color.white, true);
        paper.type = Image.Type.Simple;
        view.slip = slipRoot.gameObject;

        // tails
        var tails = Stretch(slip, "Tails");
        SlipHead(tails, "TAILS.", "mainmenu.slip.tails.word", "The bank said no.", "mainmenu.slip.tails.line");
        view.tailsNext = Text(tails, "Next", 56f, 200.4f, 608f, 30.8f, "Next flip: 1 in 2 gets you funded.", 22f, UIColors.Pencil);
        float flipW = ButtonWidth("Flip again", "mainmenu.slip.flip_again", "refresh", true);
        var flip = LabelButton(tails, "FlipAgainButton", 56f, 280f, "Flip again", "refresh", true, UIColors.Highlight, UIColors.Ink,
            new Vector2(5f, 6f), UIColors.Ink, flipW, key: "mainmenu.slip.flip_again");
        flip.gameObject.SetActive(false);   // MainMenuUI shows it once the result is in (its retryButton)
        var back = LabelButton(tails, "BackButton", 56f + flipW + 24f, 280f, "Back to menu", "home", false, UIColors.PaperHi, UIColors.Ink,
            new Vector2(4f, 5f), UIColors.Ink, key: "mainmenu.button.back");
        view.backToMenuButton = back.GetComponent<Button>();
        float deniedW = StampWidth("DENIED", 40f), sh = 40f * 1.15f + 22f;
        Stamp(tails, "Denied", 720f - 56f - deniedW * 0.5f, 44f + sh * 0.5f, "DENIED", 40f, UIColors.Loss, -10f, 0.88f, Pad(27, 27, 11, 11),
            key: "mainmenu.slip.denied");
        view.tailsContent = tails.gameObject;

        // heads
        var heads = Stretch(slip, "Heads");
        SlipHead(heads, "HEADS!", "mainmenu.slip.heads.word", "You’re in business.", "mainmenu.slip.heads.line");
        // the funding line with the starting-value chip right after it, whatever the words' length
        var fundingRow = NewRect(heads, "FundingRow");
        fundingRow.anchorMin = fundingRow.anchorMax = new Vector2(0f, 1f);
        fundingRow.pivot = new Vector2(0f, 0.5f);
        fundingRow.anchoredPosition = new Vector2(56f, -(200.4f + 15.4f));
        HLayout(fundingRow.gameObject, 16f, null, TextAnchor.MiddleLeft, true, true, false, false);
        Fit(fundingRow.gameObject, true, true);
        Bind(LayoutText(fundingRow, "Funding", "The bank is funding you.", 22f, UIColors.Pencil), "mainmenu.slip.heads.funding");
        var chip = NewRect(fundingRow, "Start");
        HLayout(chip.gameObject, 6f, Pad(14, 14, 6, 6), TextAnchor.MiddleLeft, true, true, false, false);
        IgnoreLayout(StretchBox(chip, "Fill", S("Kit/small_fill"), UIColors.MoneySoft));
        IgnoreLayout(StretchBox(chip, "Border", S("Kit/small_line2"), UIColors.Ink));
        LayoutIcon(chip, "Icon", 22f, "money", UIColors.Ink);
        view.startValueText = LayoutText(chip, "Text", "Starting value $50,000", 18f, UIColors.Ink, lineHeight: 1.4f);
        Icon(heads, "Arrow", 56f, 300f, 28f, "arrow-right", UIColors.InkSoft);
        Bind(Text(heads, "Opening", 96f, 280f, 568f, 68f, "Opening your office: name your company next.", 26f, UIColors.InkSoft, wrap: true),
            "mainmenu.slip.heads.opening");
        float apprW = StampWidth("APPROVED", 40f);
        Stamp(heads, "Approved", 720f - 40f - apprW * 0.5f, 40f + sh * 0.5f, "APPROVED", 40f, UIColors.Money, -10f, 0.88f, Pad(27, 27, 11, 11),
            key: "mainmenu.slip.approved");
        view.headsContent = heads.gameObject;
        heads.gameObject.SetActive(false);

        slipRoot.gameObject.SetActive(false);
    }

    static void SlipHead(RectTransform content, string word, string wordKey, string line, string lineKey)
    {
        Bind(Text(content, "Kind", 56f, 44f, 400f, 26.6f, "LOAN DECISION", 19f, UIColors.Pencil, letterSpacingEm: 0.08f), "mainmenu.slip.kind");
        Bind(Text(content, "Word", 56f, 78.6f, 400f, 69f, word, 60f, UIColors.Ink, bold: true), wordKey);
        Bind(Text(content, "Line", 56f, 155.6f, 608f, 36.8f, line, 32f, UIColors.Ink), lineKey);
    }

    // ------------------------------------------------------------------
    // new game confirm: sheet 780 x 552 at 570, 264

    static void BuildConfirm(RectTransform rt, MainMenuView view)
    {
        var page = Stretch(rt, "Confirm");
        var scrim = StretchBox(page, "Scrim", null, UIColors.Scrim);
        scrim.raycastTarget = true;
        Shadow(page, "SheetShadow", 570f, 264f, 780f, 552f, 0f, 22f, 48f, 0.45f, Anchor.Center);
        var sheet = Shape(page, "Sheet", 570f, 264f, 780f, 552f, "panel", UIColors.Paper, UIColors.Ink, 3, Anchor.Center);

        Bind(Text(sheet, "Title", 43f, 35f, 560f, 117f, "Start a new\ncompany?", 44f, UIColors.Ink, bold: true, align: TextAlignmentOptions.TopLeft,
            lineHeight: 1.15f, wrap: true), "mainmenu.confirm.title");
        view.confirmDeleteText = Text(sheet, "Delete", 43f, 168.2f, 694f, 36.4f,
            "Starting a new game will <b><color=#C0352B>DELETE</color></b> your current save:", 26f, UIColors.Ink);
        var save = NewRect(sheet, "Save");
        save.anchorMin = save.anchorMax = new Vector2(0f, 1f);
        save.pivot = new Vector2(0f, 1f);
        save.anchoredPosition = new Vector2(43f, -228.6f);
        HLayout(save.gameObject, 12f, Pad(22, 22, 14, 14), TextAnchor.MiddleLeft, true, true, false, false);
        Fit(save.gameObject, true, true);
        IgnoreLayout(StretchBox(save, "Shadow", S("Kit/small_fill"), Hard(UIColors.Ink, 0.9f), 3f, 4f, -3f, -4f));
        IgnoreLayout(StretchBox(save, "Fill", S("Kit/small_fill"), UIColors.PaperHi));
        IgnoreLayout(StretchBox(save, "Border", S("Kit/small_line2"), UIColors.Ink));
        LayoutIcon(save, "Icon", 28f, "folder", UIColors.Ink);
        view.confirmSaveLine = LayoutText(save, "Text", "Nova Holdings · Day 42 · $3,284,100", 26f, UIColors.Ink, lineHeight: 1.15f);
        // up to two lines (the English is one; the sheet has room below it)
        view.confirmOdds = Text(sheet, "Odds", 43f, 310.5f, 724f, 36.4f,
            "After that, the bank flips a coin: <link=\"hl\">1 in 3</link> chance they fund you.", 26f, UIColors.Ink,
            align: TextAlignmentOptions.TopLeft, wrap: true);
        view.confirmOdds.gameObject.AddComponent<TextHighlighter>();

        float delW = ButtonWidth("Delete save & start", "mainmenu.confirm.delete_button", "trash", true, 28f, 28f);
        float keepW = ButtonWidth("Keep my company", "mainmenu.confirm.keep", null, false, 28f, 28f);
        LabelButton(sheet, "DeleteButton", 737f - delW, 441f, "Delete save & start", "trash", true, UIColors.Loss, UIColors.PaperHi,
            new Vector2(5f, 6f), UIColors.Ink, delW, padX: 28, key: "mainmenu.confirm.delete_button");
        var keep = LabelButton(sheet, "KeepButton", 737f - delW - 24f - keepW, 441f, "Keep my company", null, false, UIColors.PaperHi, UIColors.Ink,
            new Vector2(4f, 5f), UIColors.Ink, keepW, padX: 28, key: "mainmenu.confirm.keep");
        view.confirmNoButton = keep.GetComponent<Button>();
        float warnW = StampWidth("WARNING", 44f), wh = 44f * 1.15f + 22f;
        Stamp(sheet, "Warning", 780f - 3f - 112f - warnW * 0.5f, 3f + 34f + wh * 0.5f, "WARNING", 44f, UIColors.Loss, -10f, 0.88f, Pad(27, 27, 11, 11),
            key: "mainmenu.confirm.warning");
        view.confirmCloseButton = IconButton(sheet, "CloseButton", 697f, 23f, 60, "cross", Anchor.TopLeft, 28f);
        page.gameObject.SetActive(false);
    }

    // ------------------------------------------------------------------
    // settings: the pause menu's folder (same board), over a scrim

    static void BuildSettingsPage(RectTransform rt, MainMenuView view)
    {
        var page = Stretch(rt, "Settings");
        var scrim = StretchBox(page, "Scrim", null, UIColors.Scrim);
        scrim.raycastTarget = true;
        var pm = page.gameObject.AddComponent<PauseMenuView>();
        PauseMenuBuilder.BuildSettings(page, pm);
        pm.settingsView = page.gameObject;
        view.settings = pm;
        page.gameObject.SetActive(false);
    }

    // ------------------------------------------------------------------
    // credits: a receipt printing out of the office adding machine (board "Credits")

    // kind, label, value, label's Menu key, value's Menu key; heights per kind as on the board. No key: a name (the game, a
    // person, a studio, an asset pack, a licence) or an amount, kept as it is
    static readonly string[][] Receipt =
    {
        new[] { "title", "Is Gambling Bad for Business?" },
        new[] { "meta", "RECEIPT No. 0001 \u00B7 DAY 1", null, "mainmenu.credits.meta" },
        new[] { "rule" },
        new[] { "section", "STAFF", null, "mainmenu.credits.section.staff" },
        new[] { "item", "A game by", "McHunter Studio", "mainmenu.credits.item.game_by" },
        new[] { "item", "Design & code", "Ömer Avcıoğlu", "mainmenu.credits.item.design_code" },
        new[] { "item", "Art & UI", "Ömer Avcıoğlu", "mainmenu.credits.item.art_ui" },
        new[] { "item", "Music", "Ömer Avcıoğlu", "mainmenu.credits.item.music" },
        new[] { "item", "Playtesting", "Ömer Avcıoğlu", "mainmenu.credits.item.playtesting" },
        new[] { "item", "Everything else", "Ömer Avcıoğlu", "mainmenu.credits.item.everything_else" },
        new[] { "rule" },
        new[] { "section", "SUPPLIERS (ASSET PACKS)", null, "mainmenu.credits.section.suppliers" },
        new[] { "line", "80s Brutalist Office" },
        new[] { "line", "Office Vintage" },
        new[] { "line", "Manager Office" },
        new[] { "line", "VNB \u00B7 Office Set" },
        new[] { "line", "HQ Residential House" },
        new[] { "line", "Leartes Studios" },
        new[] { "line", "NOT_Lonely" },
        new[] { "line", "Kenney · Casino Audio, Interface Sounds" },
        new[] { "rule" },
        new[] { "section", "STATIONERY", null, "mainmenu.credits.section.stationery" },
        new[] { "item", "Coming Soon font", "Open Window", "mainmenu.credits.item.font" },
        new[] { "line", "Apache License 2.0" },
        new[] { "item", "Built with", "Unity 6 \u00B7 HDRP", "mainmenu.credits.item.built_with" },
        new[] { "rule" },
        new[] { "section", "SPECIAL THANKS", null, "mainmenu.credits.section.thanks" },
        new[] { "item", "And you", "for playing", "mainmenu.credits.item.and_you", "mainmenu.credits.item.and_you.value" },
        new[] { "rule" },
        new[] { "item", "Subtotal", "1 game", "mainmenu.credits.item.subtotal", "mainmenu.credits.item.subtotal.value" },
        new[] { "item", "Tax", "0%", "mainmenu.credits.item.tax", "mainmenu.credits.item.tax.value" },
        new[] { "total", "TOTAL DUE", "$0.00", "mainmenu.credits.item.total" },
        new[] { "thanks", "THANK YOU FOR YOUR BUSINESS", null, "mainmenu.credits.thanks" },
        new[] { "small", "NO REFUNDS \u00B7 NO REGRETS", null, "mainmenu.credits.small" },
        new[] { "barcode" },
    };

    static float LineHeight(string kind)
    {
        switch (kind)
        {
            case "title": return 96f;
            case "meta": return 32f;
            case "rule": return 28f;
            case "section": return 44f;
            case "item": return 36f;
            case "line": return 34f;
            case "total": return 50f;
            case "thanks": return 72f;
            case "small": return 30f;
            case "barcode": return 120f;
        }
        return 34f;
    }

    static void BuildCredits(RectTransform rt, MainMenuView view)
    {
        var page = Stretch(rt, "Credits");
        var scrim = StretchBox(page, "Scrim", null, UIColors.Dark(0.72f));
        scrim.raycastTarget = true;
        var roll = page.gameObject.AddComponent<CreditsRollView>();

        Bind(Text(page, "Title", 120f, 120f, 460f, 69f, "Credits", 60f, Cream, bold: true, anchor: Anchor.Center), "mainmenu.credits.title");
        Bind(Text(page, "Sub", 120f, 203f, 460f, 64f, "Everyone who helped, itemised. Printed on the office adding machine.", 22f,
            UIColors.Hex(0xC9CEC9), align: TextAlignmentOptions.TopLeft, lineHeight: 1.45f, wrap: true, anchor: Anchor.Center), "mainmenu.credits.sub");

        // the receipt, clipped by the screen top and the machine's slot (window 700..1220 x 0..912)
        var window = Node(page, "Window", 700f, 0f, 520f, 912f, Anchor.Center);
        window.gameObject.AddComponent<RectMask2D>();
        float total = 40f;
        foreach (var l in Receipt) total += LineHeight(l[0]);
        var paper = NewRect(window, "Receipt");
        paper.anchorMin = paper.anchorMax = new Vector2(0f, 1f);
        paper.pivot = new Vector2(0f, 1f);
        paper.sizeDelta = new Vector2(520f, total);
        paper.anchoredPosition = new Vector2(0f, -60f);
        StretchBox(paper, "Ring", null, UIColors.Ink, -2f, -2f, -2f, -2f);
        StretchBox(paper, "Fill", null, UIColors.PaperHi);
        var edgeL = StretchBox(paper, "EdgeL", null, UIColors.Dark(0.05f));
        edgeL.rectTransform.anchorMax = new Vector2(0f, 1f);
        edgeL.rectTransform.offsetMax = new Vector2(2f, 0f);
        var edgeR = StretchBox(paper, "EdgeR", null, UIColors.Dark(0.05f));
        edgeR.rectTransform.anchorMin = new Vector2(1f, 0f);
        edgeR.rectTransform.offsetMin = new Vector2(-2f, 0f);

        const float X = 34f, W = 452f;
        float y = 0f;
        foreach (var l in Receipt)
        {
            string kind = l[0], label = l.Length > 1 ? l[1] : "", value = l.Length > 2 ? l[2] ?? "" : "";
            string labelKey = l.Length > 3 ? l[3] : null, valueKey = l.Length > 4 ? l[4] : null;
            float h = LineHeight(kind);
            switch (kind)
            {
                case "title":
                    Text(paper, "Title", X, y + h - 34.5f, W, 34.5f, label, 30f, UIColors.Ink, bold: true, align: TextAlignmentOptions.Center);
                    break;
                case "meta":
                    Bind(Text(paper, "Meta", X, y, W, h, label, 19f, UIColors.InkSoft, align: TextAlignmentOptions.Center, letterSpacingEm: 0.08f), labelKey);
                    break;
                case "rule":
                    Box(paper, "Rule", X, y + 12.5f, W, 3f, S("Kit/Tiles/dash_h2"), UIColors.Pencil).type = Image.Type.Tiled;
                    break;
                case "section":
                    Bind(Text(paper, "Section", X, y + h - 26f, W, 26f, label, 19f, UIColors.Ink, bold: true, letterSpacingEm: 0.12f), labelKey);
                    break;
                case "item":
                case "total":
                {
                    // label | dotted leader | value in a row: the leader takes whatever the words (in any language) leave
                    bool isTotal = kind == "total";
                    float size = isTotal ? 32f : 22f;
                    float lh = isTotal ? 36.8f : 26.4f;
                    float dotsUp = isTotal ? 8f : 6f;
                    var row = Node(paper, "Item", X, y + h - lh, W, lh);
                    HLayout(row.gameObject, 8f, null, TextAnchor.MiddleLeft, true, true, false, false);
                    Bind(LayoutText(row, "Label", label, size, UIColors.Ink, bold: isTotal), labelKey);
                    var leader = NewRect(row, "Leader");
                    Layout(leader, 0f, lh, 0f, lh, 1f);
                    var dots = StretchBox(leader, "Dots", S("Kit/Tiles/dot_h3"), UIColors.Pencil);
                    dots.type = Image.Type.Tiled;
                    var dotsRect = dots.rectTransform;
                    dotsRect.anchorMax = new Vector2(1f, 0f);
                    dotsRect.pivot = new Vector2(0.5f, 0f);
                    dotsRect.offsetMin = new Vector2(0f, dotsUp);
                    dotsRect.offsetMax = new Vector2(0f, dotsUp + 3f);
                    string shown = isTotal ? "<link=\"hl\">" + value + "</link>" : value;
                    var v = Bind(LayoutText(row, "Value", shown, size, UIColors.Ink, bold: true, align: TextAlignmentOptions.Right), valueKey);
                    if (isTotal)
                    {
                        v.gameObject.AddComponent<TextHighlighter>();
                        Layout(v, prefW: TextWidth(value, size, true) + 12f);   // room for the highlighter band
                    }
                    break;
                }
                case "line":
                    Text(paper, "Line", X, y + h - 26.4f, W, 26.4f, label, 22f, UIColors.Ink);
                    break;
                case "thanks":
                    Bind(Text(paper, "Thanks", X, y + h - 31.2f, W, 31.2f, label, 26f, UIColors.Ink, bold: true, align: TextAlignmentOptions.Center),
                        labelKey);
                    break;
                case "small":
                    Bind(Text(paper, "Small", X, y + h - 22f, W, 22f, label, 17f, UIColors.InkSoft, align: TextAlignmentOptions.Center,
                        letterSpacingEm: 0.1f), labelKey);
                    break;
                case "barcode":
                {
                    int[] widths = { 4, 2, 6, 2, 3, 5, 2, 4, 2, 6, 3, 2, 5, 2, 4, 3, 6, 2, 3, 2, 5, 4, 2, 6, 3, 2, 4, 5 };
                    float bw = 0f;
                    foreach (int bwi in widths) bw += bwi;
                    bw += 3f * (widths.Length - 1);
                    float bx = X + (W - bw) * 0.5f;
                    foreach (int bwi in widths)
                    {
                        Box(paper, "Bar", bx, y + 16f, bwi, 64f, null, UIColors.Ink);
                        bx += bwi + 3f;
                    }
                    Text(paper, "Digits", X, y + 86f, W, 20.4f, "0 0 0 1 \u00B7 1 0 0", 17f, UIColors.InkSoft, align: TextAlignmentOptions.Center,
                        letterSpacingEm: 0.3f);
                    break;
                }
            }
            y += h;
        }
        roll.roll = paper;
        roll.startTop = 60f;
        roll.endTop = 912f - total;

        // the adding machine the receipt comes out of (640, 896, 640 x 220)
        Shadow(page, "MachineShadow", 640f, 896f, 640f, 220f, 0f, -8f, 24f, 0.35f, Anchor.Center);
        var machine = Shape(page, "Machine", 640f, 896f, 640f, 220f, "r20", UIColors.PaperShade, UIColors.Ink, 3, Anchor.Center);
        Box(machine, "Shine", 4f, 4f, 632f, 8f, null, UIColors.WithAlpha(UIColors.PaperHi, 0.55f));
        Box(machine, "Slot", 50f, 10f, 532f, 14f, S("Kit/r6_fill"), UIColors.Ink);
        float keyW = (400f - 5f * 10f) / 6f;
        for (int i = 0; i < 12; i++)
        {
            int col = i % 6, row = i / 6;
            Color c = i == 5 ? UIColors.Loss : i == 11 ? UIColors.Money : UIColors.PaperHi;
            var key = Node(machine, "Key" + i, 120f + col * (keyW + 10f), 52f + row * 48f, keyW, 38f);
            StretchBox(key, "Fill", S("Kit/small_fill"), c);
            StretchBox(key, "Border", S("Kit/small_keycap"), UIColors.Ink);
        }
        Shape(machine, "Lever", 604f, 40f, 16f, 90f, "r8", UIColors.Hex(0x7E838B), UIColors.Ink, 3);
        Box(machine, "Knob", 594f, 120f, 40f, 40f, S("Kit/Circles/circle40_fill"), UIColors.Loss);
        Box(machine, "KnobRing", 594f, 120f, 40f, 40f, S("Kit/Circles/circle40_line3"), UIColors.Ink);

        // right: Space hint + Back to menu (Esc)
        var space = Keycap(page, "SpaceKey", 1330f, 780f, "Space", 19f, 36f, Anchor.Center);
        Bind(Text(page, "SpaceHint", 1330f + space.sizeDelta.x + 12f, 780f, 400f, 36f, "Hold to print faster", 22f, Cream, anchor: Anchor.Center),
            "mainmenu.credits.hold_hint");
        float backLabelW = PauseMenuBuilder.WidestText(Table, "mainmenu.button.back", "Back to menu", 28f);
        float escW = Mathf.Max(36f, 14f + TextWidth("Esc", 17f, true));
        float bw2 = 32f + 28f + 12f + backLabelW + 12f + escW + 32f;
        var back = MarkerButtonNode(page, "BackButton", 1330f, 838f, bw2, 68f, Anchor.Center, UIColors.PaperHi, new Vector2(4f, 5f),
            out var face, out var content);
        HLayout(content.gameObject, 12f, Pad(32, 32, 0, 0), TextAnchor.MiddleCenter, true, true, false, false);
        var icon = LayoutIcon(content, "Icon", 28f, "arrow-left", UIColors.Ink);
        var labelT = Bind(LayoutText(content, "Label", "Back to menu", 28f, UIColors.Ink), "mainmenu.button.back");
        PauseMenuBuilder.FitWidth(labelT, 28f);
        var esc = NewRect(content, "Esc");
        Layout(esc, escW, 32f, escW, 32f);
        StretchBox(esc, "Fill", S("Kit/small_fill"), UIColors.PaperHi);
        StretchBox(esc, "Border", S("Kit/small_keycap"), UIColors.Ink);
        Text(esc, "Key", 0f, 0f, escW, 29f, "Esc", 17f, UIColors.Ink, bold: true, align: TextAlignmentOptions.Center);
        SetupMarker(back, face, new Graphic[] { icon, labelT });

        view.creditsPage = page.gameObject;
        view.creditsBackButton = back.GetComponent<Button>();
        page.gameObject.SetActive(false);
    }

    // ------------------------------------------------------------------

    public static void Install(GameObject prefab, List<string> log)
    {
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        if (!scene.IsValid() || !scene.isLoaded)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        MainMenuUI menuUI = null;
        MainMenuView existing = null;
        MenuCoinToss oldCoin = null;
        VolumeSettingsDice oldDice = null;
        foreach (var r in scene.GetRootGameObjects())
        {
            if (menuUI == null) menuUI = r.GetComponentInChildren<MainMenuUI>(true);
            if (existing == null) existing = r.GetComponentInChildren<MainMenuView>(true);
            if (oldCoin == null) oldCoin = r.GetComponentInChildren<MenuCoinToss>(true);
            if (oldDice == null) oldDice = r.GetComponentInChildren<VolumeSettingsDice>(true);
        }
        if (menuUI == null)
        {
            log.Add("Main_Menu has no MainMenuUI.");
            return;
        }

        GameObject instance;
        if (existing == null)
        {
            instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.name = "MainMenu (designed)";
            log.Add("Main_Menu: added the MainMenu prefab.");
        }
        else
        {
            instance = existing.gameObject;
            log.Add("Main_Menu: MainMenu prefab already there (the prefab update applies to it).");
        }
        instance.SetActive(true);
        var t = instance.transform;
        var view = instance.GetComponent<MainMenuView>();
        var coin = instance.GetComponentInChildren<MenuCoinFlip2D>(true);
        var dice = instance.GetComponentInChildren<MenuDiceVolume2D>(true);
        var pm = view.settings;

        var so = new SerializedObject(menuUI);
        GameObject oldCanvas = so.FindProperty("mainMenuUI").objectReferenceValue as GameObject;
        GameObject oldSettings = so.FindProperty("settingsPanel").objectReferenceValue as GameObject;

        Button Find(string path) => t.Find(path).GetComponent<Button>();
        so.FindProperty("newGameButton").objectReferenceValue = Find("Menu/Pad/NewGameButton");
        so.FindProperty("continueButton").objectReferenceValue = Find("Menu/Pad/ContinueButton");
        so.FindProperty("settingsButton").objectReferenceValue = Find("Menu/Pad/SettingsButton");
        so.FindProperty("quitButton").objectReferenceValue = Find("Menu/Pad/QuitButton");
        so.FindProperty("settingsPanel").objectReferenceValue = t.Find("Settings").gameObject;
        so.FindProperty("confirmNewGamePanel").objectReferenceValue = t.Find("Confirm").gameObject;
        so.FindProperty("settingsCloseButton").objectReferenceValue = pm.settingsCloseButton;
        so.FindProperty("masterVolumeSlider").objectReferenceValue = pm.masterSlider;
        so.FindProperty("musicVolumeSlider").objectReferenceValue = pm.musicSlider;
        so.FindProperty("sfxVolumeSlider").objectReferenceValue = pm.sfxSlider;
        so.FindProperty("qualityDropdown").objectReferenceValue = pm.qualityDropdown;
        so.FindProperty("resolutionDropdown").objectReferenceValue = pm.resolutionDropdown;
        so.FindProperty("fullscreenToggle").objectReferenceValue = pm.fullscreenToggle;
        so.FindProperty("confirmYesButton").objectReferenceValue = Find("Confirm/Sheet/DeleteButton");
        so.FindProperty("confirmNoButton").objectReferenceValue = view.confirmNoButton;
        so.FindProperty("confirmText").objectReferenceValue = null;
        so.FindProperty("continueInfoText").objectReferenceValue = null;
        so.FindProperty("fadeOverlay").objectReferenceValue = t.Find("Fade").GetComponent<CanvasGroup>();
        so.FindProperty("coinTossUI").objectReferenceValue = null;
        so.FindProperty("mainMenuUI").objectReferenceValue = null;
        so.FindProperty("resultText").objectReferenceValue = view.sourceResult;
        so.FindProperty("retryButton").objectReferenceValue = Find("Slip/Paper/Tails/FlipAgainButton");
        so.FindProperty("resultCanvasGroup").objectReferenceValue = null;
        var coinProp = so.FindProperty("coinFlip2D");
        if (coinProp != null)
        {
            coinProp.objectReferenceValue = coin;
            log.Add("Main_Menu: MainMenuUI tosses the 2D coin on the card table.");
        }
        else
        {
            log.Add("Main_Menu: MainMenuUI has no coinFlip2D field yet, so New game still uses the 3D toss.");
        }
        string successWord = so.FindProperty("successText").stringValue;
        so.ApplyModifiedPropertiesWithoutUndo();

        view.menu = menuUI;
        var sv = new SerializedObject(view);
        sv.FindProperty("menu").objectReferenceValue = menuUI;
        if (!string.IsNullOrEmpty(successWord)) sv.FindProperty("successWord").stringValue = successWord;
        sv.ApplyModifiedPropertiesWithoutUndo();

        // the 2D coin keeps the scene's odds and sounds; the 2D dice keep the old dice's sounds
        if (oldCoin != null)
        {
            var oc = new SerializedObject(oldCoin);
            var nc = new SerializedObject(coin);
            nc.FindProperty("successChance").floatValue = oc.FindProperty("successChance").floatValue;
            nc.FindProperty("flipSound").objectReferenceValue = oc.FindProperty("coinFlipSound").objectReferenceValue;
            nc.FindProperty("landSound").objectReferenceValue = oc.FindProperty("coinSettleSound").objectReferenceValue;
            nc.ApplyModifiedPropertiesWithoutUndo();
            if (coinProp != null && oldCoin.gameObject.activeSelf)
            {
                oldCoin.gameObject.SetActive(false);
                log.Add("Main_Menu: switched off the 3D coin toss '" + oldCoin.name + "' (odds and sounds copied to the 2D coin).");
            }
        }
        if (oldDice != null)
        {
            var nd = new SerializedObject(dice);
            nd.FindProperty("minimumVolume").floatValue = oldDice.minimumVolume;
            if (oldDice.diceRollSound != null) nd.FindProperty("rollClip").objectReferenceValue = oldDice.diceRollSound.clip;
            if (oldDice.diceHitSound != null) nd.FindProperty("hitClip").objectReferenceValue = oldDice.diceHitSound.clip;
            nd.ApplyModifiedPropertiesWithoutUndo();
            if (oldDice.gameObject.activeSelf)
            {
                oldDice.gameObject.SetActive(false);
                log.Add("Main_Menu: switched off the 3D volume dice '" + oldDice.name + "' (the card table's dice set the volume now).");
            }
        }
        foreach (var old in new[] { oldCanvas, oldSettings != null ? oldSettings.transform.root.gameObject : null })
        {
            if (old != null && old != instance && !old.transform.IsChildOf(instance.transform) && old.activeSelf && old.GetComponent<Canvas>() != null)
            {
                old.SetActive(false);
                log.Add("Main_Menu: switched off the old menu canvas '" + old.name + "'.");
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        log.Add("Main_Menu saved.");
    }
}
