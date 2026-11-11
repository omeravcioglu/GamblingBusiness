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
/// Builds the designed pause menu from the canvas boards "Pause menu" (legal-pad to-do list + current save card)
/// and "Pause: settings (in game)" (manila Settings folder) into Assets/UI/Prefabs/PauseMenu.prefab, and puts it in
/// MainGame next to SettingsPanel, which takes the controls over at start (PauseMenuView.ApplyTo).
/// </summary>
public static class PauseMenuBuilder
{
    public const string PrefabPath = "Assets/UI/Prefabs/PauseMenu.prefab";
    /// <summary>The String Table of the menus' fixed labels (Assets/Localization/Source/Menu.csv).</summary>
    const string Table = "Menu";

    static readonly Color Binding = UIColors.Hex(0x7A2A24);
    static readonly Color LegalRule = UIColors.Hex(0xB9CFE0);
    static readonly Color StapleMetal = UIColors.Hex(0xC4C8CF);
    static readonly Color GreenMark = new Color32(126, 214, 142, 217);
    static readonly Color YellowMark = new Color32(255, 225, 77, 242);
    static readonly Color BoxFill = new Color32(255, 253, 246, 153);

    [MenuItem("Tools/Game/New UI/Build Pause Menu")]
    public static void BuildFromMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Build Pause Menu", "Stop Play mode first.", "OK");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var log = new List<string>();
        var prefab = BuildPrefab(log);
        if (prefab != null) Install(prefab, log);
        Debug.Log("[PauseMenuBuilder]\n" + string.Join("\n", log));
        EditorUtility.DisplayDialog("Pause menu", string.Join("\n", log), "OK");
    }

    /// <summary>Batch mode: -executeMethod PauseMenuBuilder.BuildFromCommandLine [-installPause] [-pausePreview path.png]</summary>
    public static void BuildFromCommandLine()
    {
        int code = 0;
        try
        {
            var log = new List<string>();
            var args = System.Environment.GetCommandLineArgs();
            var prefab = BuildPrefab(log);
            if (prefab == null) code = 1;
            else
            {
                if (System.Array.IndexOf(args, "-installPause") >= 0)
                    Install(prefab, log);
                int i = System.Array.IndexOf(args, "-pausePreview");
                if (i >= 0 && i + 1 < args.Length)
                {
                    var hud = AssetDatabase.LoadAssetAtPath<GameObject>(OfficeHudBuilder.PrefabPath);
                    string png = args[i + 1];
                    string settingsPng = Path.Combine(Path.GetDirectoryName(png) ?? "", Path.GetFileNameWithoutExtension(png) + "_settings.png");
                    UIPreview.Render(png, log, new UIPreview.Layer(hud), new UIPreview.Layer(prefab));
                    UIPreview.Render(settingsPng, log, new UIPreview.Layer(hud), new UIPreview.Layer(prefab, p =>
                    {
                        var v = p.GetComponent<PauseMenuView>();
                        v.menuView.SetActive(false);
                        v.settingsView.SetActive(true);
                    }));
                }
            }
            Debug.Log("[PauseMenuBuilder]\n" + string.Join("\n", log));
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            code = 1;
        }
        EditorApplication.Exit(code);
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
        var root = new GameObject("PauseMenu", typeof(RectTransform));
        root.layer = 5;
        var rt = (RectTransform)root.transform;
        rt.sizeDelta = new Vector2(1920f, 1080f);
        var view = root.AddComponent<PauseMenuView>();

        // Scrim over the paused office: blocks every click behind the menu
        var scrim = StretchBox(rt, "Scrim", null, UIColors.Scrim);
        scrim.raycastTarget = true;

        var menu = Stretch(rt, "Menu");
        BuildPad(menu, view);
        BuildSaveCard(menu, view);
        view.menuView = menu.gameObject;

        var settings = Stretch(rt, "Settings");
        BuildSettings(settings, view);
        view.settingsView = settings.gameObject;
        settings.gameObject.SetActive(false);

        // Canvas last (a root canvas resizes itself to the screen)
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        root.AddComponent<GraphicRaycaster>();
        return root;
    }

    // ------------------------------------------------------------------
    // The legal pad: left 560, top 130, 620 x 800

    static void BuildPad(RectTransform menu, PauseMenuView view)
    {
        const float L = 560f, T = 130f, W = 620f, H = 800f;
        Shadow(menu, "PadShadow", L, T, W, H, 0f, 22f, 48f, 0.45f, Anchor.Center);
        var pad = Node(menu, "Pad", L, T, W, H, Anchor.Center);

        var paper = StretchBox(pad, "Paper", S("Kit/panel_fill"), UIColors.LegalPad);
        paper.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        var rules = StretchBox(paper.transform, "Rules", S("Kit/Tiles/legal_rule41"), Color.white, 3f, 3f, 3f, 3f);
        rules.type = Image.Type.Tiled;
        // binding strip with staples, margin line
        Box(paper.transform, "Binding", 3f, 3f, 614f, 31f, null, Binding);
        Box(paper.transform, "BindingEdge", 3f, 34f, 614f, 3f, null, UIColors.Ink);
        float gap = (614f - 6f * 28f) / 7f;
        for (int i = 0; i < 6; i++)
        {
            var staple = Node(paper.transform, "Staple", 3f + gap + i * (28f + gap), 3f + 11.5f, 28f, 8f);
            AddImage(staple.gameObject, S("Kit/r4_fill"), StapleMetal);
            StretchBox(staple, "Edge", S("Kit/r4_line2"), UIColors.Ink);
        }
        Box(paper.transform, "MarginLine", 3f + 64f, 37f, 2f, 760f, null, UIColors.Margin);
        StretchBox(pad, "Border", S("Kit/panel_line3"), UIColors.Ink);

        view.closeButton = IconButton(pad, "CloseButton", 533f, 55f, 60, "cross", Anchor.TopLeft, 28f);

        // Header: "Paused" + Esc keycap in a row that grows with the title, then the subtitle (block 117 tall from y 91, text from x 85)
        var header = NewRect(pad, "Header");
        header.anchorMin = header.anchorMax = new Vector2(0f, 1f);
        header.pivot = new Vector2(0f, 0.5f);
        header.anchoredPosition = new Vector2(85f, -(91f + 69f * 0.5f));
        HLayout(header.gameObject, 16f, null, TextAnchor.MiddleLeft, true, true, false, false);
        Fit(header.gameObject, true, true);
        Localize(LayoutText(header, "Title", "Paused", 60f, UIColors.Ink, bold: true), Table, "pausemenu.title");
        float escW = Mathf.Max(36f, 18f + TextWidth("Esc", 19f, true));
        Layout(Keycap(header, "EscKeycap", 0f, 0f, "Esc", 19f, 36f), escW, 36f, escW, 36f);
        Localize(Text(pad, "Subtitle", 85f, 91f + 117f - 27f, 480f, 27f, "Things to do, in order of importance:", 19f, UIColors.Pencil),
            Table, "pausemenu.subtitle");

        // The list: 5 items of 82 px from y 208, a 41 px gap, then Quit
        float y = 208f;
        view.resumeButton = Item(pad, view, "Resume", y, "Resume", "pausemenu.item.resume", "play", UIColors.Ink, null, null, true, true, out _);
        y += 82f;
        view.openSettingsButton = Item(pad, view, "Settings", y, "Settings", "pausemenu.item.settings", "sliders", UIColors.Ink, null, null,
            false, false, out _);
        y += 82f;
        view.saveButton = Item(pad, view, "Save", y, "Save game", "pausemenu.item.save", "save", UIColors.Ink, "Last saved 12 seconds ago", null,
            false, false, out view.lastSavedText);
        y += 82f;
        view.renameButton = Item(pad, view, "Rename", y, "Rename company", "pausemenu.item.rename", "pencil", UIColors.Ink, null, null,
            false, false, out _);
        y += 82f;
        view.mainMenuButton = Item(pad, view, "MainMenu", y, "Main menu", "pausemenu.item.main_menu", "home", UIColors.Ink, "We save first.",
            "pausemenu.item.main_menu.caption", false, false, out _);
        y += 82f + 41f;
        view.quitButton = Item(pad, view, "Quit", y, "Quit to desktop", "pausemenu.item.quit", "power", UIColors.Loss, null, null,
            false, false, out _);
    }

    /// <summary>
    /// One to-do line (82 tall, x 15..593): checkbox, label, right-side icon (Resume adds the Esc keycap),
    /// optional pencil caption on the second row. Hovering marks the label with the highlighter. The view writes the
    /// label (key) in the current language; a caption with a captionKey is bound to it (SettingsPanel writes the others).
    /// </summary>
    static Button Item(RectTransform pad, PauseMenuView view, string name, float top, string label, string key, string icon, Color color,
        string caption, string captionKey, bool isResume, bool marked, out TextMeshProUGUI captionText)
    {
        var item = Node(pad, name + "Item", 15f, top, 578f, 82f);
        var hit = AddImage(item.gameObject, null, Clear, true);
        var button = item.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = hit;

        // checkbox: 34 px, SMALL radius, 3 px border in the item's colour; Resume is ticked
        var box = Node(item, "Box", 12f, 20.5f - 17f, 34f, 34f);
        AddImage(box.gameObject, S("Kit/small_fill"), BoxFill);
        StretchBox(box, "Border", S("Kit/small_line3"), color);
        if (isResume)
            Icon(box, "Tick", 4f, 4f, 26f, "check", UIColors.Ink, "b");

        // label (34 px, 41 px line) with the highlighter behind it
        float labelW = WidestText(Table, key, label, 34f) + 8f;
        var text = Text(item, "Label", 70f, 0f, labelW, 41f, "<link=\"hl\">" + label + "</link>", 34f, color);
        AddHighlight(view, text, key);
        var hl = text.gameObject.AddComponent<TextHighlighter>();
        hl.color = marked ? GreenMark : YellowMark;
        var reveal = item.gameObject.AddComponent<HoverReveal>();
        reveal.targetBehaviour = hl;
        reveal.alwaysShown = marked;
        hl.enabled = marked;

        // right side: icon 32 (Resume: Esc keycap + icon)
        Icon(item, "Icon", 578f - 12f - 32f, 20.5f - 16f, 32f, icon, color);
        if (isResume)
        {
            float capW = Mathf.Max(36f, 18f + TextWidth("Esc", 19f, true));
            Keycap(item, "EscKeycap", 578f - 12f - 32f - 12f - capW, 20.5f - 18f, "Esc", 19f, 36f);
        }

        captionText = null;
        if (caption != null)
        {
            captionText = Text(item, "Caption", 70f, 41f, 496f, 41f, caption, 19f, UIColors.Pencil);
            if (captionKey != null) Localize(captionText, Table, captionKey);
        }
        return button;
    }

    // ------------------------------------------------------------------
    // Current save card: left 1230, top 200, 420 x 240, rotated 2 deg

    static void BuildSaveCard(RectTransform menu, PauseMenuView view)
    {
        const float L = 1230f, T = 200f, W = 420f, H = 240f;
        var holder = Node(menu, "SaveCard", L, T, W, H, Anchor.Center, 2f);
        StretchShadow(holder, 0f, 22f, 48f, 0.45f);
        StretchBox(holder, "Fill", S("Kit/card_fill"), UIColors.PaperHi);
        var lines = StretchBox(holder, "Rules", S("Kit/Tiles/rule30"), Color.white, 2f, 2f, 2f, 2f);
        lines.type = Image.Type.Tiled;
        Box(holder, "HeaderLine", 2f, 48f, W - 4f, 2f, null, UIColors.Margin);
        StretchBox(holder, "Border", S("Kit/card_line2"), UIColors.Ink);
        Box(holder, "Tape", 135f, -20f, 150f, 38f, null, UIColors.Tape, Anchor.TopLeft, -4f);

        view.cardName = Text(holder, "Name", 30f, 2f, 360f, 46f, "Nova Holdings", 32f, UIColors.Ink, bold: true);
        view.cardName.overflowMode = TextOverflowModes.Ellipsis;
        Icon(holder, "DayIcon", 30f, 62f + 3f, 24f, "calendar", UIColors.Ink);
        view.cardDay = Text(holder, "Day", 66f, 62f, 320f, 30f, "Day 42", 22f, UIColors.Ink);
        Icon(holder, "ValueIcon", 30f, 92f + 16f, 28f, "money", UIColors.Ink);
        view.cardValue = Text(holder, "Value", 70f, 92f, 320f, 60f, "$3,284,100", 44f, UIColors.Ink, bold: true);
        Icon(holder, "StaffIcon", 30f, 152f + 3f, 24f, "people", UIColors.Ink);
        view.cardStaff = Text(holder, "Staff", 66f, 152f, 320f, 30f, "18 staff", 22f, UIColors.Ink);
        Icon(holder, "SaveIcon", 31f, 182f + 4f, 22f, "save", UIColors.Pencil);
        view.cardAutosave = Text(holder, "Autosave", 65f, 182f, 320f, 30f, "Autosaves every 60 s", 19f, UIColors.Pencil);
    }

    // ------------------------------------------------------------------
    // Settings folder: body 340, 210, 1240 x 720; tab 380, 150; close 1500, 144

    public static void BuildSettings(RectTransform settings, PauseMenuView view)
    {
        Shadow(settings, "FolderShadow", 340f, 210f, 1240f, 720f, 0f, 22f, 48f, 0.45f, Anchor.Center);
        Shape(settings, "Folder", 340f, 210f, 1240f, 720f, "panel", UIColors.Manila, UIColors.Ink, 3, Anchor.Center);
        Shape(settings, "BackPage", 372f, 242f, 1180f, 660f, "card", UIColors.PaperShade, UIColors.Ink, 3, Anchor.Center);
        var page = Shape(settings, "FrontPage", 364f, 234f, 1180f, 660f, "card", UIColors.Paper, UIColors.Ink, 3, Anchor.Center);

        var tab = Node(settings, "Tab", 380f, 150f, 272f, 63f, Anchor.Center);
        StretchBox(tab, "Fill", S("Kit/foldertab_fill"), UIColors.Manila);
        StretchBox(tab, "Border", S("Kit/foldertab_line3_nobottom"), UIColors.Ink);
        // icon + label centred on the tab, whatever the label's length
        var tabRow = Stretch(tab, "Row", 0f, 0f, 0f, 3f);
        HLayout(tabRow.gameObject, 12f, null, TextAnchor.MiddleCenter, true, true, false, false);
        LayoutIcon(tabRow, "Icon", 32f, "sliders", UIColors.Ink);
        Localize(LayoutText(tabRow, "Label", "Settings", 32f, UIColors.Ink, bold: true), Table, "settings.tab");

        view.settingsCloseButton = IconButton(settings, "CloseButton", 1500f, 144f, 60, "cross", Anchor.Center, 28f);

        // Page content: x 51..1129 of the page (the page starts at 364, 234), blocks spaced like "space-between"
        const float X = 51f;            // 3 border + 48 padding
        float normal = UIFonts.NaturalLineHeight(UIBuild.Font);

        // Sound (x 51, w 590)
        Heading(page, view, "Sound", "settings.heading.sound", X, 43f);
        float rowTop = 43f + 36.8f + 24f;
        view.masterSlider = SliderRow(page, "Master", X, rowTop, "volume", "Master", 0.8f, out view.masterValue, "settings.master");
        view.musicSlider = SliderRow(page, "Music", X, rowTop + 76f, "music", "Music", 0.7f, out view.musicValue, "settings.music");
        view.sfxSlider = SliderRow(page, "Sfx", X, rowTop + 152f, "bell", "Sound effects", 0.6f, out view.sfxValue, "settings.sfx");

        // Display (x 51 + 590 + 72 = 713, w 416); LanguagePicker copies QualityLabel for its label
        const float DX = 713f;
        Heading(page, view, "Display", "settings.heading.display", DX, 43f);
        Localize(Text(page, "ResolutionLabel", DX, rowTop, 150f, 60f, "Resolution", 26f, UIColors.Ink), Table, "settings.resolution");
        view.resolutionDropdown = Dropdown(page, "Resolution", DX + 166f, rowTop, 250f, 60f, "1920 × 1080");
        Localize(Text(page, "QualityLabel", DX, rowTop + 76f, 150f, 60f, "Quality", 26f, UIColors.Ink), Table, "settings.quality");
        view.qualityDropdown = Dropdown(page, "Quality", DX + 166f, rowTop + 76f, 250f, 60f, "High");
        Localize(Text(page, "FullscreenLabel", DX, rowTop + 152f, 150f, 60f, "Fullscreen", 26f, UIColors.Ink), Table, "settings.fullscreen");
        view.fullscreenToggle = PillSwitch(page, "Fullscreen", DX + 166f, rowTop + 152f + 11f, true);
        view.fullscreenState = Text(page, "FullscreenState", DX + 166f + 72f + 12f, rowTop + 152f, 120f, 60f, "On", 22f, UIColors.InkSoft);

        // Controls (block 2)
        const float controlsTop = 371f;
        Heading(page, view, "Controls", "settings.heading.controls", X, controlsTop);
        var keys = Node(page, "Keys", X, controlsTop + 36.8f + 24f, 1078f, 36f);
        HLayout(keys.gameObject, 48f, null, TextAnchor.MiddleLeft, true, true, false, false);
        KeyHint(keys, "Q", "Build mode", "settings.controls.build");
        KeyHint(keys, "E", "Upgrade hovered item", "settings.controls.upgrade");
        KeyHint(keys, "R", "Rotate (build)", "settings.controls.rotate");
        KeyHint(keys, "Esc", "Pause / cancel", "settings.controls.pause");

        // Footer (block 3): rule, then Reset link (left) and "Changes save automatically." + Done (right). Sized for the
        // widest language: LanguagePicker puts the language dropdown between the link and the note
        const float footerTop = 523f;
        Box(page, "FooterRule", X, footerTop, 1078f, 2f, null, UIColors.Rule);
        float rowY = footerTop + 2f + 24f;
        view.resetDefaultsButton = TextLink(page, "ResetDefaults", X, rowY + 8f, "Reset to defaults", 22f, "settings.reset");

        float doneTextW = WidestText(Table, "settings.done", "Done", 28f, true);
        float doneW = 36f + 28f + 12f + doneTextW + 36f;
        float doneX = X + 1078f - doneW;
        var done = MarkerButtonNode(page, "DoneButton", doneX, rowY, doneW, 68f, Anchor.TopLeft, UIColors.Highlight, new Vector2(5f, 6f),
            out var doneFace, out var doneContent);
        HLayout(doneContent.gameObject, 12f, Pad(36, 36, 0, 0), TextAnchor.MiddleCenter, true, true, false, false);
        var doneIcon = LayoutIcon(doneContent, "Icon", 28f, "check", UIColors.Ink, "b");
        var doneLabel = Localize(LayoutText(doneContent, "Label", "Done", 28f, UIColors.Ink, bold: true), Table, "settings.done");
        FitWidth(doneLabel, 28f);
        SetupMarker(done, doneFace, new Graphic[] { doneIcon, doneLabel });
        view.settingsDoneButton = done.GetComponent<Button>();

        float noteW = WidestText(Table, "settings.autosave_note", "Changes save automatically.", 19f) + 4f;
        Localize(Text(page, "AutoSaveNote", doneX - 24f - noteW, rowY, noteW, 68f, "Changes save automatically.", 19f, UIColors.Pencil,
            align: TextAlignmentOptions.Right), Table, "settings.autosave_note");
    }

    /// <summary>Heading under the highlighter; PauseMenuView writes it in the current language.</summary>
    static void Heading(RectTransform page, PauseMenuView view, string text, string key, float x, float y)
    {
        float w = WidestText(Table, key, text, 32f, true) + 16f;
        var t = Text(page, text + "Heading", x, y, w, 36.8f, "<link=\"hl\">" + text + "</link>", 32f, UIColors.Ink, bold: true);
        t.gameObject.AddComponent<TextHighlighter>().color = YellowMark;
        AddHighlight(view, t, key);
    }

    /// <summary>A button label that shrinks (down to 75 %) rather than leave its button when a language runs long.</summary>
    public static void FitWidth(TMP_Text label, float size)
    {
        label.enableAutoSizing = true;
        label.fontSizeMax = size;
        label.fontSizeMin = Mathf.Round(size * 0.75f);
    }

    /// <summary>Volume row: icon 28 | label 180 | slider 270 | value 64 (gaps 16), 60 tall.</summary>
    static Slider SliderRow(RectTransform page, string name, float x, float top, string icon, string label, float value, out TextMeshProUGUI valueText,
        string key = null)
    {
        Icon(page, name + "Icon", x, top + 16f, 28f, icon, UIColors.Ink);
        var labelText = Text(page, name + "Label", x + 44f, top, 180f, 60f, label, 26f, UIColors.Ink);
        if (key != null) Localize(labelText, Table, key);
        var slider = CoinSlider(page, name + "Slider", x + 240f, top + 10f, 270f, 40f, value);
        valueText = Text(page, name + "Value", x + 526f, top, 64f, 60f, Mathf.RoundToInt(value * 100f) + "%", 26f, UIColors.Ink,
            align: TextAlignmentOptions.Right);
        return slider;
    }

    /// <summary>Slider: 12 px paper-hi track with a 2 px ink border and highlighter fill, gold coin thumb (34 px).</summary>
    static Slider CoinSlider(RectTransform parent, string name, float left, float top, float width, float height, float value)
    {
        var root = Node(parent, name, left, top, width, height);
        var hit = AddImage(root.gameObject, null, Clear, true);

        var track = NewRect(root, "Track");
        track.anchorMin = new Vector2(0f, 0.5f);
        track.anchorMax = new Vector2(1f, 0.5f);
        track.sizeDelta = new Vector2(0f, 12f);
        track.anchoredPosition = Vector2.zero;
        AddImage(track.gameObject, S("Kit/Bars/bar12_fill"), UIColors.PaperHi);
        var inner = Stretch(track, "FillArea", 2f, 2f, 2f, 2f);
        var fill = NewRect(inner, "Fill");
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = new Vector2(value, 1f);
        fill.offsetMin = fill.offsetMax = Vector2.zero;
        AddImage(fill.gameObject, null, UIColors.Highlight);
        StretchBox(track, "Border", S("Kit/Bars/bar12_line2"), UIColors.Ink);

        var area = Stretch(root, "HandleArea");
        var handle = NewRect(area, "Handle");
        handle.anchorMin = new Vector2(value, 0f);
        handle.anchorMax = new Vector2(value, 1f);
        handle.sizeDelta = new Vector2(34f, 0f);
        var handleImg = AddImage(handle.gameObject, S("Kit/Special/coin34"), Color.white, true);
        handleImg.preserveAspect = true;

        var slider = root.gameObject.AddComponent<Slider>();
        slider.fillRect = fill;
        slider.handleRect = handle;
        slider.targetGraphic = handleImg;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = value;
        slider.transition = Selectable.Transition.None;
        var nav = slider.navigation;
        nav.mode = Navigation.Mode.None;
        slider.navigation = nav;
        return slider;
    }

    /// <summary>Dropdown (TMP): paper-hi box, 3 px ink border, SMALL radius, 24 px text, chevron at the right; list on paper.</summary>
    static TMP_Dropdown Dropdown(RectTransform parent, string name, float left, float top, float width, float height, string shown)
    {
        var go = TMP_DefaultControls.CreateDropdown(new TMP_DefaultControls.Resources());
        go.name = name + "Dropdown";
        go.layer = 5;
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        Place(rt, left, top, width, height);
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 5;

        var dd = go.GetComponent<TMP_Dropdown>();
        var bg = go.GetComponent<Image>();
        bg.sprite = S("Kit/small_fill");
        bg.type = Image.Type.Sliced;
        bg.color = UIColors.PaperHi;
        var border = StretchBox(rt, "Border", S("Kit/small_line3"), UIColors.Ink);
        border.transform.SetSiblingIndex(0);
        dd.transition = Selectable.Transition.None;

        var label = (TextMeshProUGUI)dd.captionText;
        label.font = UIBuild.Font;
        label.fontSize = 24f;
        label.color = UIColors.Ink;
        label.alignment = TextAlignmentOptions.Left;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.text = shown;
        label.rectTransform.offsetMin = new Vector2(18f, 0f);
        label.rectTransform.offsetMax = new Vector2(-56f, 0f);

        var arrow = rt.Find("Arrow") as RectTransform;
        if (arrow != null)
        {
            var img = arrow.GetComponent<Image>();
            img.sprite = IconSprite("chevron-down", "b");
            img.color = UIColors.Ink;
            img.preserveAspect = true;
            arrow.anchorMin = arrow.anchorMax = new Vector2(1f, 0.5f);
            arrow.pivot = new Vector2(1f, 0.5f);
            arrow.sizeDelta = new Vector2(28f, 28f);
            arrow.anchoredPosition = new Vector2(-18f, 0f);
        }

        // The list: paper-hi with a 2 px ink border; hovered item gets the highlighter
        var template = (RectTransform)dd.template;
        template.anchorMin = new Vector2(0f, 0f);
        template.anchorMax = new Vector2(1f, 0f);
        template.pivot = new Vector2(0.5f, 1f);
        template.anchoredPosition = new Vector2(0f, -6f);
        template.sizeDelta = new Vector2(0f, 4f * 44f + 12f);
        var tImg = template.GetComponent<Image>();
        tImg.sprite = S("Kit/small_fill");
        tImg.type = Image.Type.Sliced;
        tImg.color = UIColors.PaperHi;
        var tBorder = StretchBox(template, "Border", S("Kit/small_line2"), UIColors.Ink);
        tBorder.transform.SetAsLastSibling();

        var viewport = template.Find("Viewport") as RectTransform;
        if (viewport != null)
        {
            viewport.offsetMin = new Vector2(4f, 6f);
            viewport.offsetMax = new Vector2(-14f, -6f);
        }
        var item = template.GetComponentInChildren<Toggle>(true);
        if (item != null)
        {
            var itemRt = (RectTransform)item.transform;
            itemRt.sizeDelta = new Vector2(itemRt.sizeDelta.x, 44f);
            var content = itemRt.parent as RectTransform;
            if (content != null) content.sizeDelta = new Vector2(content.sizeDelta.x, 44f);
            var itemBg = item.transform.Find("Item Background")?.GetComponent<Image>();
            if (itemBg != null)
            {
                itemBg.sprite = S("Kit/small_fill");
                itemBg.type = Image.Type.Sliced;
                itemBg.color = UIColors.Highlight;
                var colors = item.colors;
                colors.normalColor = new Color(1f, 1f, 1f, 0f);
                colors.highlightedColor = Color.white;
                colors.pressedColor = Color.white;
                colors.selectedColor = new Color(1f, 1f, 1f, 0.6f);
                colors.disabledColor = new Color(1f, 1f, 1f, 0f);
                item.colors = colors;
            }
            var check = item.transform.Find("Item Checkmark")?.GetComponent<Image>();
            if (check != null)
            {
                check.sprite = IconSprite("check", "b");
                check.color = UIColors.Money;
                check.preserveAspect = true;
                var crt = check.rectTransform;
                crt.sizeDelta = new Vector2(24f, 24f);
                crt.anchoredPosition = new Vector2(18f, 0f);
            }
            var itemLabel = (TextMeshProUGUI)dd.itemText;
            itemLabel.font = UIBuild.Font;
            itemLabel.fontSize = 22f;
            itemLabel.color = UIColors.Ink;
            itemLabel.rectTransform.offsetMin = new Vector2(40f, 1f);
        }
        var scrollbar = template.Find("Scrollbar");
        if (scrollbar != null)
        {
            var sImg = scrollbar.GetComponent<Image>();
            sImg.sprite = null;
            sImg.color = new Color(0f, 0f, 0f, 0f);
            var handle = scrollbar.GetComponentInChildren<Scrollbar>(true);
            if (handle != null && handle.handleRect != null)
            {
                var hImg = handle.handleRect.GetComponent<Image>();
                hImg.sprite = S("Kit/Pills/pill20_fill");
                hImg.color = UIColors.WithAlpha(UIColors.Ink, 0.5f);
            }
        }
        dd.options.Clear();
        return dd;
    }

    /// <summary>72 x 38 pill toggle: track money / paper-shade, 26 px knob.</summary>
    static Toggle PillSwitch(RectTransform parent, string name, float left, float top, bool on)
    {
        var root = Node(parent, name + "Toggle", left, top, 72f, 38f);
        var track = AddImage(root.gameObject, S("Kit/Pills/pill38_fill"), on ? UIColors.Money : UIColors.PaperShade, true);
        StretchBox(root, "Border", S("Kit/Pills/pill38_line3"), UIColors.Ink);
        var knob = Node(root, "Knob", 23f, 6f, 26f, 26f);
        AddImage(knob.gameObject, S("Kit/Circles/circle26_fill"), UIColors.PaperHi);
        StretchBox(knob, "Ring", S("Kit/Circles/circle26_line2"), UIColors.Ink);
        knob.anchorMin = knob.anchorMax = new Vector2(0.5f, 0.5f);
        knob.anchoredPosition = new Vector2(on ? 17f : -17f, 0f);
        var toggle = root.gameObject.AddComponent<Toggle>();
        toggle.transition = Selectable.Transition.None;
        toggle.targetGraphic = track;
        toggle.graphic = null;
        toggle.isOn = on;
        var pill = root.gameObject.AddComponent<PillToggle>();
        pill.track = track;
        pill.knob = knob;
        pill.knobOnX = 17f;
        pill.knobOffX = -17f;
        return toggle;
    }

    static void KeyHint(RectTransform row, string key, string label, string labelKey = null)
    {
        var group = NewRect(row, key + "Hint");
        HLayout(group.gameObject, 12f, null, TextAnchor.MiddleLeft, true, true, false, false);
        float w = Mathf.Max(36f, 18f + TextWidth(key, 19f, true));
        var cap = Keycap(group, "Keycap", 0f, 0f, key, 19f, 36f);
        Layout(cap, w, 36f, w, 36f);
        var text = LayoutText(group, "Label", label, 22f, UIColors.Ink);
        if (labelKey != null) Localize(text, Table, labelKey);
    }

    /// <summary>
    /// TEXT LINK: no box, wavy ink underline 7 px under the baseline. With a key, the link's box fits the widest language
    /// (the language picker is placed after it) and the text and its underline grow with the words shown.
    /// </summary>
    static Button TextLink(RectTransform parent, string name, float left, float top, string label, float size, string key = null)
    {
        float w = (key != null ? WidestText(Table, key, label, size) : TextWidth(label, size)) + 8f;
        var link = Node(parent, name, left, top, w, 52f);
        var hit = AddImage(link.gameObject, null, Clear, true);
        var button = link.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = hit;
        var text = Text(link, "Label", 4f, 0f, w - 8f, 44f, label, size, UIColors.Ink);
        if (key == null)
        {
            var wave = Box(link, "Underline", 4f, 38f, w - 8f, 6f, S("Kit/Tiles/wave_h2"), UIColors.Ink);
            wave.type = Image.Type.Tiled;
            return button;
        }
        Localize(text, Table, key);
        var textRect = text.rectTransform;
        textRect.pivot = new Vector2(0f, 0.5f);
        textRect.anchoredPosition -= new Vector2((w - 8f) * 0.5f, 0f);
        Fit(text.gameObject, true, false);
        // the underline: the text's bottom 6 px, as wide as the text
        var underline = StretchBox(textRect, "Underline", S("Kit/Tiles/wave_h2"), UIColors.Ink);
        underline.type = Image.Type.Tiled;
        var lineRect = underline.rectTransform;
        lineRect.anchorMax = new Vector2(1f, 0f);
        lineRect.pivot = new Vector2(0.5f, 0f);
        lineRect.offsetMin = Vector2.zero;
        lineRect.offsetMax = new Vector2(0f, 6f);
        return button;
    }

    // ------------------------------------------------------------------
    // Localization

    /// <summary>A label under a highlighter that PauseMenuView writes in the current language (inside the same link).</summary>
    static void AddHighlight(PauseMenuView view, TMP_Text text, string key)
    {
        var labels = new List<PauseMenuView.HighlightLabel>(view.highlightLabels ?? new PauseMenuView.HighlightLabel[0]);
        labels.Add(new PauseMenuView.HighlightLabel { text = text, key = key });
        view.highlightLabels = labels.ToArray();
    }

    static readonly Dictionary<string, (System.DateTime stamp, L10nCsv.Sheet sheet)> TableCache =
        new Dictionary<string, (System.DateTime, L10nCsv.Sheet)>();

    /// <summary>
    /// One-line width of the widest version of a String Table entry, for boxes sized at build time: the English measured
    /// in the design font, the translations of Assets/Localization/Source/&lt;table&gt;.csv estimated for their fonts
    /// (EstimatedWidth; checked against the real fonts: within 2 %, usually 5-10 % above). The main menu and the endings
    /// use it too.
    /// </summary>
    public static float WidestText(string table, string key, string english, float size, bool bold = false, float letterSpacingEm = 0f)
    {
        float widest = TextWidth(english, size, bold, letterSpacingEm);
        L10nCsv.Sheet sheet = ReadTable(table);
        if (sheet == null) return widest;
        int keyColumn = sheet.Column("Key"), contextColumn = sheet.Column("Context"), englishColumn = sheet.Column("en");
        foreach (List<string> row in sheet.Rows)
        {
            if (L10nCsv.Sheet.Cell(row, keyColumn) != key) continue;
            for (int c = 0; c < sheet.Header.Count; c++)
            {
                if (c == keyColumn || c == contextColumn || c == englishColumn) continue;
                // Turkish uses Patrick Hand, narrower than the CJK fonts' Latin letters
                float latinScale = sheet.Header[c].Trim() == "tr" ? 0.88f : 1f;
                foreach (string line in L10nCsv.NormalizeNewlines(L10nCsv.Sheet.Cell(row, c)).Split('\n'))
                    widest = Mathf.Max(widest, EstimatedWidth(line, size, bold, letterSpacingEm, latinScale));
            }
            break;
        }
        return widest;
    }

    /// <summary>CJK, kana, full-width forms and … 1 em; Hangul 0.85 em; other letters by shape (0.3-0.9 em, × latinScale); bold +0.07 em; +5 %.</summary>
    static float EstimatedWidth(string text, float size, bool bold, float letterSpacingEm, float latinScale)
    {
        float em = 0f;
        foreach (char c in text)
        {
            float e;
            if (c >= 0xAC00 && c <= 0xD7AF) e = 0.85f;
            else if ((c >= 0x2E80 && c <= 0x9FFF) || (c >= 0xF900 && c <= 0xFAFF) || (c >= 0xFF00 && c <= 0xFF60) || c == '…') e = 1f;
            else if (c == '%') e = 0.9f * latinScale;
            else if (char.IsDigit(c)) e = 0.6f * latinScale;
            else if (c == ' ') e = 0.35f * latinScale;
            else if ("ilIjtfr.,:;'!|ı".IndexOf(c) >= 0) e = 0.3f * latinScale;
            else if ("mwMW".IndexOf(c) >= 0) e = 0.75f * latinScale;
            else if (char.IsUpper(c)) e = 0.65f * latinScale;
            else e = 0.5f * latinScale;
            em += e + (bold ? 0.07f : 0f) + letterSpacingEm;
        }
        return em * size * 1.05f;
    }

    static L10nCsv.Sheet ReadTable(string table)
    {
        string path = L10n.SourceFolder + "/" + table + ".csv";
        if (!File.Exists(path)) return null;
        System.DateTime stamp = File.GetLastWriteTimeUtc(path);
        if (TableCache.TryGetValue(table, out var cached) && cached.stamp == stamp) return cached.sheet;
        L10nCsv.Sheet sheet = L10nCsv.ReadSheet(File.ReadAllText(path));
        TableCache[table] = (stamp, sheet);
        return sheet;
    }

    // ------------------------------------------------------------------
    // Scene

    public static void Install(GameObject prefab, List<string> log)
    {
        Scene scene = SceneManager.GetSceneByPath(OfficeHudBuilder.ScenePath);
        if (!scene.IsValid() || !scene.isLoaded)
            scene = EditorSceneManager.OpenScene(OfficeHudBuilder.ScenePath, OpenSceneMode.Single);

        SettingsPanel settings = null;
        PauseMenuView existing = null;
        foreach (var r in scene.GetRootGameObjects())
        {
            if (settings == null) settings = r.GetComponentInChildren<SettingsPanel>(true);
            if (existing == null) existing = r.GetComponentInChildren<PauseMenuView>(true);
        }
        if (settings == null)
        {
            log.Add("MainGame has no SettingsPanel: nothing to hook the pause menu to.");
            return;
        }

        GameObject instance;
        if (existing == null)
        {
            instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.name = "PauseMenu";
            log.Add("MainGame: added the PauseMenu prefab.");
        }
        else
        {
            instance = existing.gameObject;
            log.Add("MainGame: PauseMenu already there (the prefab update applies to it).");
        }
        instance.SetActive(false);

        var so = new SerializedObject(settings);
        so.FindProperty("panel").objectReferenceValue = instance;
        so.ApplyModifiedPropertiesWithoutUndo();
        log.Add("MainGame: SettingsPanel.panel -> PauseMenu (it takes the controls over at start).");

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        log.Add("MainGame saved.");
    }
}
