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
/// Builds the designed company-name sheet (canvas boards "Name your company" and "Pause: rename company") into
/// Assets/UI/Prefabs/CompanyName.prefab and wires it to MainGame's CompanyNameUI (panel, input, Incorporate/Save,
/// Random, error text). CompanyNameView adds the first-run/rename differences and the live business card.
/// Fixed labels are bound to the Hud String Table (Localize, Assets/Localization/Source/Hud.csv); the views write the rest.
/// Buttons grow with their label, which is longer in some languages.
/// </summary>
public static class CompanyNameBuilder
{
    public const string PrefabPath = "Assets/UI/Prefabs/CompanyName.prefab";
    const string T = "Hud";

    [MenuItem("Tools/Game/New UI/Build Company Name")]
    public static void BuildFromMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Build Company Name", "Stop Play mode first.", "OK");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var log = new List<string>();
        var prefab = BuildPrefab(log);
        if (prefab != null) Install(prefab, log);
        Debug.Log("[CompanyNameBuilder]\n" + string.Join("\n", log));
        EditorUtility.DisplayDialog("Company name", string.Join("\n", log), "OK");
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
        var root = new GameObject("CompanyNamePopup", typeof(RectTransform));
        root.layer = 5;
        var rt = (RectTransform)root.transform;
        rt.sizeDelta = new Vector2(1920f, 1080f);
        var view = root.AddComponent<CompanyNameView>();

        var scrim = StretchBox(rt, "Scrim", null, UIColors.Scrim);
        scrim.raycastTarget = true;
        PausedTag(rt);

        // Sheet: 420, 260, 1080 x 560 (paper, PANEL radius), padding 40, columns 506 | 48 | 440
        Shadow(rt, "SheetShadow", 420f, 260f, 1080f, 560f, 0f, 22f, 48f, 0.45f, Anchor.Center);
        var sheet = Shape(rt, "Sheet", 420f, 260f, 1080f, 560f, "panel", UIColors.Paper, UIColors.Ink, 3, Anchor.Center);

        // --- left column (x 43, width 506, y 43..517)
        view.title = Text(sheet, "Title", 43f, 43f, 506f, 138f, "Name your\ncompany", 60f, UIColors.Ink, bold: true, lineHeight: 1.15f);

        var block = Node(sheet, "InputBlock", 43f, 205f, 506f, 200f);
        PivotTopLeft(block);
        VLayout(block.gameObject, 12f, null, TextAnchor.UpperLeft, true, true, true, false);

        var row = NewRect(block, "Row");
        Layout(row, -1f, 98.6f, -1f, 98.6f);
        Localize(Text(row, "Label", 0f, 0f, 310f, 26.6f, "Company name", 19f, UIColors.Pencil), T, "hud.name.input.label");
        view.input = NameInput(row, 0f, 34.6f, 310f, 64f, view);
        float randomW = 22f + 28f + 12f + TextWidth("Random", 22f) + 22f;
        var random = MarkerButtonNode(row, "RandomButton", 326f, 34.6f + 5f, randomW, 54f, Anchor.TopLeft, UIColors.PaperHi, new Vector2(4f, 5f),
            out var rFace, out var rContent);
        HLayout(rContent.gameObject, 12f, Pad(22, 22, 0, 0), TextAnchor.MiddleCenter, true, true, false, false);
        var rIcon = LayoutIcon(rContent, "Icon", 28f, "dice", UIColors.Ink);
        var rLabel = Localize(LayoutText(rContent, "Label", "Random", 22f, UIColors.Ink), T, "hud.name.random");
        SetupMarker(random, rFace, new Graphic[] { rIcon, rLabel });
        PivotLeft(random);   // a longer word widens it to the right, away from the name field
        FitWidth(random, rContent, randomW);

        // error: the text object is what CompanyNameUI switches on and off, so the icon lives inside it
        var error = LayoutText(block, "Error", "At least 3 characters.", 19f, UIColors.Loss, lineHeight: 1.4f);
        error.margin = new Vector4(32f, 0f, 0f, 0f);
        var warn = Icon(error.transform, "Icon", 0f, 1f, 24f, "warning", UIColors.Loss);
        warn.rectTransform.anchorMin = warn.rectTransform.anchorMax = new Vector2(0f, 0.5f);
        warn.rectTransform.anchoredPosition = new Vector2(12f, 0f);
        error.gameObject.SetActive(false);
        view.errorRow = error.gameObject;

        view.hint = LayoutText(block, "Hint", "3–25 characters. You can rename it later from the pause menu.", 19f, UIColors.Pencil,
            lineHeight: 1.4f, wrap: true);

        // bottom row (y 437): Incorporate / Save name (80 tall) + Cancel (68) in rename mode, 20 px apart; each button grows
        // with its label (CompanyNameView sets the confirm button's designed width per mode)
        float firstW = 40f + 28f + 12f + TextWidth("Incorporate", 32f, true) + 40f;
        float renameW = 40f + 28f + 12f + TextWidth("Save name", 32f, true) + 40f;
        var buttons = Node(sheet, "Buttons", 43f, 437f, 506f, 80f);
        HLayout(buttons.gameObject, 20f, null, TextAnchor.MiddleLeft, false, false, false, false);
        var confirm = MarkerButtonNode(buttons, "ConfirmButton", 0f, 0f, firstW, 80f, Anchor.TopLeft, UIColors.Highlight, new Vector2(5f, 6f),
            out var cFace, out var cContent);
        HLayout(cContent.gameObject, 12f, Pad(40, 40, 0, 0), TextAnchor.MiddleCenter, true, true, false, false);
        var cIcon = LayoutIcon(cContent, "Icon", 28f, "stamp", UIColors.Ink);
        var cLabel = LayoutText(cContent, "Label", "Incorporate", 32f, UIColors.Ink, bold: true);
        SetupMarker(confirm, cFace, new Graphic[] { cIcon, cLabel });
        FitWidth(confirm, cContent, firstW);
        view.confirmLabel = cLabel;
        view.confirmIcon = cIcon;
        view.incorporateIcon = IconSprite("stamp");
        view.saveIcon = IconSprite("pencil");
        view.confirmRect = confirm;
        view.confirmLeft = 43f;
        view.firstWidth = firstW;
        view.renameWidth = renameW;

        float cancelW = 32f + TextWidth("Cancel", 28f) + 32f;
        var cancel = MarkerButtonNode(buttons, "CancelButton", renameW + 20f, 6f, cancelW, 68f, Anchor.TopLeft, UIColors.PaperHi, new Vector2(4f, 5f),
            out var xFace, out var xContent);
        HLayout(xContent.gameObject, 0f, Pad(32, 32, 0, 0), TextAnchor.MiddleCenter, true, true, false, false);
        var xLabel = Localize(LayoutText(xContent, "Label", "Cancel", 28f, UIColors.Ink), T, "hud.name.cancel");
        SetupMarker(cancel, xFace, new Graphic[] { xLabel });
        FitWidth(cancel, xContent, cancelW);
        view.cancelButton = cancel.GetComponent<Button>();
        view.cancelRect = cancel;
        cancel.gameObject.SetActive(false);

        // --- right column: "Preview" with a curved arrow right after the word, pointing at the business card (x 597, width 440)
        var preview = Node(sheet, "Preview", 621f, 123f, 416f, 48f);
        HLayout(preview.gameObject, 8f, null, TextAnchor.LowerLeft, true, true, false, false);
        var previewLabel = Localize(LayoutText(preview, "Label", "Preview", 22f, UIColors.Pencil), T, "hud.name.preview");
        Layout(previewLabel, -1f, 30.8f, -1f, 30.8f);
        LayoutIcon(preview, "Arrow", 48f, "arrow-curve", UIColors.Pencil);
        BusinessCard(sheet, view);

        view.closeButton = IconButton(rt, "CloseButton", 1466f, 232f, 60, "cross", Anchor.Center, 28f);
        view.closeButton.gameObject.SetActive(false);

        // Canvas last
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 110;
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        root.AddComponent<GraphicRaycaster>();
        return root;
    }

    static void PivotTopLeft(RectTransform rt)
    {
        Vector2 size = rt.sizeDelta;
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition += new Vector2(-size.x * 0.5f, size.y * 0.5f);
    }

    /// <summary>Pivot on the left edge (same place on screen), so a width change grows the rect rightwards.</summary>
    static void PivotLeft(RectTransform rt)
    {
        rt.pivot = new Vector2(0f, rt.pivot.y);
        rt.anchoredPosition -= new Vector2(rt.sizeDelta.x * 0.5f, 0f);
    }

    /// <summary>The button keeps its designed width and grows with its content row (a longer language): HudFitWidth.</summary>
    static void FitWidth(RectTransform button, RectTransform content, float designWidth)
    {
        var fit = button.gameObject.AddComponent<HudFitWidth>();
        fit.content = content;
        fit.minWidth = designWidth;
    }

    /// <summary>"Game paused" chip: top centre, 28 px down, over the scrim; as wide as its words (they differ per language).</summary>
    public static void PausedTag(RectTransform root)
    {
        var tag = Node(root, "PausedTag", 846f, 28f, 228f, 52f, Anchor.Top);
        IgnoreLayout(StretchBox(tag, "Shadow", S("Kit/small_fill"), UIColors.WithAlpha(UIColors.Ink, 0.9f), 3f, 4f, -3f, -4f));
        IgnoreLayout(StretchBox(tag, "Fill", S("Kit/small_fill"), UIColors.PaperHi));
        IgnoreLayout(StretchBox(tag, "Border", S("Kit/small_line2"), UIColors.Ink));
        // icon, 12 px, the words; the design's side padding around them (228 px wide in English)
        float tw = TextWidth("Game paused", 22f);
        int side = Mathf.Max(16, Mathf.RoundToInt((228f - (28f + 12f + tw)) * 0.5f));
        HLayout(tag.gameObject, 12f, Pad(side, side, 0, 0), TextAnchor.MiddleCenter, true, true, false, false);
        Fit(tag.gameObject, true, false);
        LayoutIcon(tag, "Icon", 28f, "pause", UIColors.Ink);
        Localize(LayoutText(tag, "Label", "Game paused", 22f, UIColors.Ink), T, "hud.paused");
    }

    static TMP_InputField NameInput(RectTransform parent, float left, float top, float width, float height, CompanyNameView view)
    {
        var go = TMP_DefaultControls.CreateInputField(new TMP_DefaultControls.Resources());
        go.name = "NameInput";
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 5;
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        Place(rt, left, top, width, height);

        var field = go.GetComponent<TMP_InputField>();
        var bg = go.GetComponent<Image>();
        bg.sprite = S("Kit/small_fill");
        bg.type = Image.Type.Sliced;
        bg.color = UIColors.PaperHi;
        field.transition = Selectable.Transition.None;
        // inset shadow: inset 0 3px 0 rgba(29,36,51,0.08)
        var inset = Box(rt, "InsetShadow", 3f, 3f, width - 6f, 3f, null, UIColors.WithAlpha(UIColors.Ink, UIColors.SrgbAlpha(0.08f)));
        inset.transform.SetSiblingIndex(0);
        var border = StretchBox(rt, "Border", S("Kit/small_line3"), UIColors.Ink);
        view.inputBorder = border;

        var area = rt.Find("Text Area") as RectTransform;
        if (area != null)
        {
            area.offsetMin = new Vector2(20f, 3f);
            area.offsetMax = new Vector2(-76f, -3f);
        }
        var text = field.textComponent as TextMeshProUGUI;
        if (text != null)
        {
            text.font = UIBuild.Font;
            text.fontSize = 28f;
            text.color = UIColors.Ink;
            text.alignment = TextAlignmentOptions.Left;
            text.textWrappingMode = TextWrappingModes.NoWrap;
        }
        var placeholder = field.placeholder as TextMeshProUGUI;
        if (placeholder != null)
        {
            placeholder.font = UIBuild.Font;
            placeholder.fontSize = 28f;
            placeholder.color = UIColors.WithAlpha(UIColors.Pencil, 0.7f);
            placeholder.fontStyle = FontStyles.Normal;
            placeholder.alignment = TextAlignmentOptions.Left;
            placeholder.text = "Type a name";
            Localize(placeholder, T, "hud.name.input.placeholder");
        }
        field.text = "Nova Holdings";
        field.characterLimit = 25;
        field.caretColor = UIColors.Ink;
        field.customCaretColor = true;
        field.caretWidth = 2;
        field.selectionColor = UIColors.WithAlpha(UIColors.Highlight, 0.75f);
        field.fontAsset = UIBuild.Font;
        field.pointSize = 28f;

        view.counter = Text(rt, "Counter", width - 16f - 60f, 0f, 60f, height, "13/25", 19f, UIColors.Pencil, align: TextAlignmentOptions.Right);
        return field;
    }

    /// <summary>Business card 440 x 250 at (597, 187), rotated 2 deg, SMALL radius, hard + soft shadow.</summary>
    static void BusinessCard(RectTransform sheet, CompanyNameView view)
    {
        var card = Node(sheet, "BusinessCard", 597f, 187f, 440f, 250f, Anchor.TopLeft, 2f);
        StretchShadow(card, 0f, 14f, 28f, 0.18f);
        StretchBox(card, "HardShadow", S("Kit/small_fill"), UIColors.WithAlpha(UIColors.Ink, 0.9f), 3f, 4f, -3f, -4f);
        StretchBox(card, "Fill", S("Kit/small_fill"), UIColors.PaperHi);
        StretchBox(card, "Border", S("Kit/small_line2"), UIColors.Ink);

        // logo placeholder: 64 px blob with a dashed blue ring and a seal icon
        var logo = Node(card, "Logo", 32f, 28f, 64f, 64f);
        AddImage(logo.gameObject, S("Kit/Circles/circle64_dash2"), UIColors.Info);
        Icon(logo, "Seal", 12f, 12f, 40f, "seal", UIColors.Info);

        view.cardName = Text(card, "Name", 112f, 28f - 1f, 296f, 36.8f, "NOVA HOLDINGS", 32f, UIColors.Ink, bold: true, letterSpacingEm: 0.02f);
        view.cardName.overflowMode = TextOverflowModes.Ellipsis;
        Localize(Text(card, "Since", 112f, 28f + 36.8f, 296f, 26.6f, "Est. Day 1", 19f, UIColors.InkSoft), T, "hud.name.card.since");
        Box(card, "Rule", 32f, 125.2f, 376f, 2f, null, UIColors.Ink);
        Localize(Text(card, "Ceo", 32f, 160.4f, 376f, 30.8f, "CEO: You", 22f, UIColors.Ink), T, "hud.name.card.ceo");
        var value = Text(card, "Value", 32f, 191.2f, 376f, 30.8f, "Company value <b>$50,000</b>", 22f, UIColors.Ink);   // CompanyNameView writes it
        view.cardValue = value;
    }

    // ------------------------------------------------------------------

    public static void Install(GameObject prefab, List<string> log)
    {
        Scene scene = SceneManager.GetSceneByPath(OfficeHudBuilder.ScenePath);
        if (!scene.IsValid() || !scene.isLoaded)
            scene = EditorSceneManager.OpenScene(OfficeHudBuilder.ScenePath, OpenSceneMode.Single);

        CompanyNameUI ui = null;
        CompanyNameView existing = null;
        foreach (var r in scene.GetRootGameObjects())
        {
            if (ui == null) ui = r.GetComponentInChildren<CompanyNameUI>(true);
            if (existing == null) existing = r.GetComponentInChildren<CompanyNameView>(true);
        }
        if (ui == null)
        {
            log.Add("MainGame has no CompanyNameUI.");
            return;
        }

        GameObject instance;
        if (existing == null)
        {
            instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.name = "CompanyNamePopup";
            log.Add("MainGame: added the CompanyName prefab.");
        }
        else
        {
            instance = existing.gameObject;
            log.Add("MainGame: CompanyName popup already there (the prefab update applies to it).");
        }
        instance.SetActive(false);
        var view = instance.GetComponent<CompanyNameView>();

        var so = new SerializedObject(ui);
        so.FindProperty("nameInputPanel").objectReferenceValue = instance;
        so.FindProperty("companyNameInput").objectReferenceValue = view.input;
        so.FindProperty("confirmButton").objectReferenceValue = view.confirmRect.GetComponent<Button>();
        so.FindProperty("randomButton").objectReferenceValue = instance.transform.Find("Sheet/InputBlock/Row/RandomButton")?.GetComponent<Button>();
        so.FindProperty("errorText").objectReferenceValue = view.errorRow.GetComponent<TextMeshProUGUI>();
        so.FindProperty("titleText").objectReferenceValue = null;   // the view writes the two-line title
        so.ApplyModifiedPropertiesWithoutUndo();
        log.Add("MainGame: CompanyNameUI -> CompanyName popup (old Canvas/CompanyNamePanel no longer used).");

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        log.Add("MainGame saved.");
    }
}
