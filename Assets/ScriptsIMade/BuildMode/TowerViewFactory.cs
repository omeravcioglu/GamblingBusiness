using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds the HQ campus screen at runtime (used when TowerView has no designed root assigned) and fills in the view's
/// references, so TowerView works exactly as it will with a designed prefab. Layout, colours and sizes follow the
/// "HQ tower (2D build)" boards at the 1920 x 1080 reference: top bar 84 px, tool tray at the bottom, the campus in the
/// middle (street level 200 px above the bottom), the left drawer and the right clipboard 420 x 828 at y 116.
/// The campus parts (2026-09-29, not on the boards yet: runtime versions in the boards' style for the UI session to
/// design): the campus bar under the top bar, a sixth tool (Campus), the drawer's Campus tab and scrolling catalogs with
/// category filters, the room card's level block, the hint strip's "Gamble for it" and the purchase slip.
/// Fixed words keep to the "Tower" String Table by themselves (L10nText); TowerView writes everything else.
/// </summary>
public static class TowerViewFactory
{
    static readonly Vector4 TabRadii = new Vector4(14f, 20f, 0f, 0f);
    static readonly Vector4 ToolRadii = new Vector4(14f, 20f, 8f, 10f);
    static readonly Color SoftShadow = new Color(0f, 0f, 0f, 0.45f);
    static readonly Color TopBarShadow = new Color(0f, 0f, 0f, 0.25f);
    static readonly Color RuleBlue = UIColors.Rule;
    static readonly Color MarginRed = UIColors.Margin;

    public static void Build(TowerView view, TMP_FontAsset font)
    {
        Canvas canvas = BlueprintUI.CreateOverlayCanvas("BuildModeCanvas (Runtime)", view.sortingOrder);
        RectTransform root = BlueprintUI.Stretch(BlueprintUI.NewRect("TowerScreen", canvas.transform));
        root.gameObject.SetActive(false);
        root.gameObject.AddComponent<CanvasGroup>();
        view.root = root;

        // Blueprint (takes the clicks on the tower and keeps them from the HUD below) and its grid
        RectTransform blueprint = BlueprintUI.Stretch(BlueprintUI.NewRect("Blueprint", root));
        Image surface = blueprint.gameObject.AddComponent<Image>();
        surface.color = UIColors.Blueprint;
        surface.raycastTarget = true;
        view.towerSurface = surface;
        RectTransform grid = BlueprintUI.NewRect("Grid", root);
        grid.anchorMin = grid.anchorMax = new Vector2(0f, 1f);
        grid.pivot = new Vector2(0f, 1f);
        grid.sizeDelta = Vector2.zero;
        view.gridLayer = grid.gameObject.AddComponent<BlueprintGraphic>();
        view.gridLayer.raycastTarget = false;

        // The tower (design pixels, origin at street level under the middle of the building)
        RectTransform content = BlueprintUI.NewRect("TowerContent", root);
        content.anchorMin = content.anchorMax = new Vector2(0.5f, 0f);
        content.pivot = new Vector2(0.5f, 0f);
        content.sizeDelta = Vector2.zero;
        content.anchoredPosition = new Vector2(0f, TowerView.GroundFromBottom);
        view.towerContent = content;
        view.structureLayer = Layer(content, "Structure");
        view.floorLayer = Point(BlueprintUI.NewRect("Floors", content));
        view.cablesLayer = Layer(content, "Cables");
        view.liftCar = LiftCar(content);
        view.labelLayer = Point(BlueprintUI.NewRect("Labels", content));
        view.overlayLayer = Layer(content, "Overlay");
        BuildChips(view, content, font);
        BuildIntro(view, content, font);
        BuildFloorSlip(view, content, font);
        BuildSellSlip(view, content, font);

        BuildTitleBlock(view, root, font);
        BuildScrollControl(view, root, font);
        BuildHintStrip(view, root, font);
        BuildTray(view, root, font);
        BuildRoomView(view, root, font);
        BuildCampusBar(view, root, font);
        BuildDrawer(view, root, font);
        BuildClipboard(view, root, font);
        BuildTopBar(view, root, font);
        BuildMessage(view, root, font);
        BuildPurchaseSlip(view, root, font);
        BuildSheet(view, root, font);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    /// <summary>A mesh layer whose origin is its parent's origin (a zero-size rect, pivot bottom-centre).</summary>
    public static BlueprintGraphic Layer(RectTransform parent, string name)
    {
        RectTransform rect = Point(BlueprintUI.NewRect(name, parent));
        BlueprintGraphic graphic = rect.gameObject.AddComponent<BlueprintGraphic>();
        graphic.raycastTarget = false;
        return graphic;
    }

    /// <summary>A zero-size rect at its parent's bottom-centre point (the tower's origin).</summary>
    public static RectTransform Point(RectTransform rect)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;
        return rect;
    }

    /// <summary>A rect on the tower: anchored at the tower's origin, positioned by TowerView.ToContent.</summary>
    static RectTransform OnTower(RectTransform rect, float width, float height, Vector2 pivot)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = pivot;
        rect.sizeDelta = new Vector2(width, height);
        return rect;
    }

    static RectTransform BottomLeft(RectTransform rect, float left, float bottom, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = Vector2.zero;
        rect.pivot = Vector2.zero;
        rect.anchoredPosition = new Vector2(left, bottom);
        rect.sizeDelta = new Vector2(width, height);
        return rect;
    }

    static RectTransform BottomCentre(RectTransform rect, float x, float bottom, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(x, bottom);
        rect.sizeDelta = new Vector2(width, height);
        return rect;
    }

    public static TowerIcon Icon(Transform parent, TowerIconKind kind, float size, Color color, float stroke = 2.4f)
    {
        RectTransform rect = BlueprintUI.NewRect("Icon", parent);
        rect.sizeDelta = new Vector2(size, size);
        TowerIcon icon = rect.gameObject.AddComponent<TowerIcon>();
        icon.Kind = kind;
        icon.StrokeWidth = stroke;
        icon.color = color;
        icon.raycastTarget = false;
        BlueprintUI.Layout(icon, size, size, 0f, 0f, size, size);
        return icon;
    }

    static TextMeshProUGUI Text(Transform parent, string name, string text, float size, Color color, TMP_FontAsset font,
                                TextAlignmentOptions alignment = TextAlignmentOptions.Left, bool bold = false)
    {
        return BlueprintUI.Text(parent, name, text, size, color, font, alignment, bold);
    }

    static TextMeshProUGUI Placed(TextMeshProUGUI text, float x, float y, float width, float height)
    {
        BlueprintUI.TopLeft(text.rectTransform, x, y, width, height);
        return text;
    }

    /// <summary>A fixed text: the Tower String Table entry <paramref name="key"/>, written again on a language change.</summary>
    static TextMeshProUGUI Fixed(TextMeshProUGUI text, string key)
    {
        L10nText.Attach(text, TowerText.Table, key);
        return text;
    }

    /// <summary>A key cap whose word is a String Table entry ("Click").</summary>
    static RectTransform WordKeycap(Transform parent, string name, string key, TMP_FontAsset font)
    {
        RectTransform keycap = BlueprintUI.Keycap(parent, name, font);
        Fixed(keycap.GetComponentInChildren<TextMeshProUGUI>(true), key);
        return keycap;
    }

    /// <summary>A gold coin (radial gradient, ink ring) with the coin icon.</summary>
    static RectTransform Coin(Transform parent, float size, float iconSize)
    {
        BlueprintBox coin = BlueprintUI.Box(parent, "Coin", UIColors.Gold, UIColors.Ink, 2f, BlueprintUI.Round(size * 0.5f));
        coin.SetGradient(UIColors.Hex(0xF7DC87), new Vector2(0.38f, 0.32f));
        BlueprintUI.Layout(coin, size, size, 0f, 0f, size, size);
        coin.rectTransform.sizeDelta = new Vector2(size, size);
        if (iconSize > 0f)
        {
            TowerIcon icon = Icon(coin.transform, TowerIconKind.Coin, iconSize, UIColors.Ink);
            BlueprintUI.Layout(icon).ignoreLayout = true;
            BlueprintUI.Centered(icon.rectTransform, iconSize, iconSize);
        }
        return coin.rectTransform;
    }

    /// <summary>
    /// A button sized to its content: optional icon, label, optional key cap (for layout groups). With
    /// <paramref name="textKey"/> the label is that fixed String Table entry; without, the view writes it.
    /// </summary>
    static Button Button(Transform parent, string name, string label, TowerIconKind icon, TMP_FontAsset font, float height, float fontSize,
                         Color fill, Color textColor, bool bold, float border, Color shadow, Vector2 shadowOffset, int padLeft, int padRight,
                         out TextMeshProUGUI text, float iconSize = 26f, string key = null, string textKey = null)
    {
        BlueprintBox box = BlueprintUI.Box(parent, name, fill, UIColors.Ink, border, BlueprintUI.RadiusLarge, true);
        if (shadow.a > 0f) box.SetShadow(shadow, shadowOffset);
        Button button = BlueprintUI.MakeButton(box);
        BlueprintUI.HRow(box, 10f, new RectOffset(padLeft, padRight, 0, 0), TextAnchor.MiddleCenter);
        if (icon != TowerIconKind.None) Icon(box.transform, icon, iconSize, textColor);
        text = Text(box.transform, "Label", label, fontSize, textColor, font, TextAlignmentOptions.Center, bold);
        if (textKey != null) Fixed(text, textKey);
        if (key != null) BlueprintUI.Keycap(box.transform, key, font);
        BlueprintUI.Layout(box, -1f, height, -1f, -1f, -1f, height);
        return button;
    }

    /// <summary>Round paper close button with an X (the design's 60 / 52 / 48 px).</summary>
    static Button RoundClose(Transform parent, float size, float iconSize)
    {
        BlueprintBox close = BlueprintUI.Box(parent, "Close", UIColors.PaperHi, UIColors.Ink, 3f,
                                             new Vector4(size * 0.5f, size * 0.46f, size * 0.52f, size * 0.48f), true);
        close.SetShadow(UIColors.Ink, new Vector2(3f, 4f));
        close.rectTransform.sizeDelta = new Vector2(size, size);
        BlueprintUI.Layout(close, size, size, 0f, 0f, size, size);
        TowerIcon icon = Icon(close.transform, TowerIconKind.Close, iconSize, UIColors.Ink);
        BlueprintUI.Layout(icon).ignoreLayout = true;
        BlueprintUI.Centered(icon.rectTransform, iconSize, iconSize);
        return BlueprintUI.MakeButton(close);
    }

    /// <summary>A text button underlined with a wavy line ("cancel", "Fill the budget"): its word is the String Table entry <paramref name="key"/>.</summary>
    static Button Link(Transform parent, string name, string key, TowerIconKind icon, float fontSize, Color color, TMP_FontAsset font)
    {
        RectTransform rect = BlueprintUI.NewRect(name, parent);
        Image hit = rect.gameObject.AddComponent<Image>();
        hit.color = Color.clear;
        hit.raycastTarget = true;
        BlueprintUI.HRow(rect, 8f, new RectOffset(0, 0, 0, 6), TextAnchor.MiddleLeft);
        if (icon != TowerIconKind.None) Icon(rect, icon, fontSize + 2f, color);
        TextMeshProUGUI text = Fixed(Text(rect, "Label", "", fontSize, color, font), key);
        text.fontStyle = FontStyles.Underline;
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = hit;
        button.transition = Selectable.Transition.None;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        return button;
    }

    /// <summary>A small square turned 45° (the pointer of a slip or tooltip).</summary>
    static RectTransform Pointer(Transform parent, float size, Color fill, float border)
    {
        BlueprintBox tip = BlueprintUI.Box(parent, "Pointer", fill, UIColors.Ink, border, BlueprintUI.Round(1f));
        BlueprintUI.Layout(tip).ignoreLayout = true;
        RectTransform rect = tip.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(size, size);
        rect.localEulerAngles = new Vector3(0f, 0f, 45f);
        return rect;
    }

    static BlueprintBox Tape(Transform parent, float width, float x, float y, float degrees)
    {
        BlueprintBox tape = BlueprintUI.Box(parent, "Tape", UIColors.Tape, Color.clear, 0f, BlueprintUI.Round(0f));
        tape.SetShadow(new Color(0f, 0f, 0f, 0.18f), new Vector2(0f, 2f), 4f);
        BlueprintUI.Layout(tape).ignoreLayout = true;
        RectTransform rect = tape.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(width, 32f);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.localEulerAngles = new Vector3(0f, 0f, -degrees);   // CSS rotate(+) turns clockwise
        return tape;
    }

    /// <summary>Ruled lines drawn once over a card from its top-left: a red margin line and blue rules (y down).</summary>
    static BlueprintGraphic Rules(Transform parent, float width, float height, float margin, float step, float first)
    {
        RectTransform rect = BlueprintUI.NewRect("Lines", parent);
        BlueprintUI.Layout(rect).ignoreLayout = true;
        BlueprintUI.TopLeft(rect, 0f, 0f, width, height);
        BlueprintGraphic lines = rect.gameObject.AddComponent<BlueprintGraphic>();
        lines.raycastTarget = false;
        BlueprintMeshBuilder m = lines.Mesh;
        m.Clear();
        m.SetYDownFrame(Vector2.zero);
        for (float y = first; y < height - 2f; y += step)
            m.Line(new Vector2(3f, y + 0.5f), new Vector2(width - 3f, y + 0.5f), 1f, RuleBlue);
        if (margin > 0f) m.Line(new Vector2(3f, margin + 1f), new Vector2(width - 3f, margin + 1f), 2f, MarginRed);
        lines.Commit();
        return lines;
    }

    static RectTransform Row(Transform parent, string name, float spacing, TextAnchor alignment = TextAnchor.MiddleLeft, float height = -1f)
    {
        RectTransform rect = BlueprintUI.NewRect(name, parent);
        BlueprintUI.HRow(rect, spacing, new RectOffset(), alignment);
        if (height > 0f) BlueprintUI.Layout(rect, -1f, height, -1f, -1f, -1f, height);
        return rect;
    }

    static RectTransform Column(Transform parent, string name, float spacing, TextAnchor alignment = TextAnchor.UpperLeft)
    {
        RectTransform rect = BlueprintUI.NewRect(name, parent);
        BlueprintUI.VColumn(rect, spacing, new RectOffset(), alignment);
        return rect;
    }

    static TextMeshProUGUI Flex(TextMeshProUGUI text)
    {
        BlueprintUI.Layout(text, 0f, -1f, 1f, -1f, 0f);
        return text;
    }

    // ------------------------------------------------------------------
    // On the tower: lift car, chips, the first-visit note, slips
    // ------------------------------------------------------------------

    static RectTransform LiftCar(RectTransform content)
    {
        BlueprintBox car = BlueprintUI.Box(content, "LiftCar", UIColors.Paper, UIColors.Ink, 2f, new Vector4(4f, 5f, 3f, 5f));
        car.SetShadow(new Color32(29, 36, 51, 153), new Vector2(2f, 3f));
        RectTransform rect = OnTower(car.rectTransform, TowerGeo.CarWidth, TowerGeo.CarHeight, new Vector2(0f, 1f));
        BlueprintBox bar = BlueprintUI.Box(car.transform, "Top", UIColors.Ink, Color.clear, 0f, BlueprintUI.Round(2f));
        BlueprintUI.TopLeft(bar.rectTransform, 3f, 3f, 32f, 6f);
        BlueprintBox door = BlueprintUI.Box(car.transform, "Door", UIColors.Ink, Color.clear, 0f, BlueprintUI.Round(0f));
        BlueprintUI.TopLeft(door.rectTransform, 18f, 13f, 2f, 43f);
        return rect;
    }

    /// <summary>"empty · 2 tiles" on a blueprint pad (TowerView pools these).</summary>
    public static RectTransform EmptyPad(RectTransform parent, TMP_FontAsset font)
    {
        BlueprintBox pad = BlueprintUI.Box(parent, "Empty", UIColors.Blueprint, Color.clear, 0f, BlueprintUI.Round(0f));
        RectTransform rect = OnTower(pad.rectTransform, 80f, 23f, new Vector2(0.5f, 0.5f));
        BlueprintUI.HRow(pad, 0f, new RectOffset(8, 8, 3, 3), TextAnchor.MiddleCenter);
        BlueprintUI.Fit(pad, true, false);
        Text(pad.transform, "Text", "", 17f, UIColors.BlueprintInk, font, TextAlignmentOptions.Center);   // TowerView writes it
        return rect;
    }

    static void BuildChips(TowerView view, RectTransform content, TMP_FontAsset font)
    {
        TowerChips chips = view.chips;

        // "8 tiles" under the street (TowerView writes the texts the chips show)
        TextMeshProUGUI dimension = Text(content, "EightTiles", "", 17f, UIColors.BlueprintInk, font, TextAlignmentOptions.Center);
        chips.dimensionText = OnTower(dimension.rectTransform, 100f, 24f, new Vector2(0.5f, 0.5f));

        // Add-floor slot: "+ Add floor 5", the price chip, and the Build floor button while hovered
        RectTransform label = Row(content, "SlotLabel", 8f, TextAnchor.MiddleCenter);
        OnTower(label, 400f, 30f, new Vector2(0.5f, 0.5f));
        Icon(label, TowerIconKind.Plus, 28f, UIColors.BlueprintInk, 2.8f);
        chips.slotLabelText = Text(label, "Text", "", 26f, UIColors.BlueprintInk, font);
        chips.slotLabel = label;

        BlueprintBox chip = BlueprintUI.Box(content, "SlotChip", UIColors.PaperHi, UIColors.Ink, 2f, BlueprintUI.RadiusSmall);
        chip.SetShadow(new Color32(29, 36, 51, 217), new Vector2(2f, 3f));
        OnTower(chip.rectTransform, 140f, 36f, new Vector2(0.5f, 0.5f));
        BlueprintUI.HRow(chip, 8f, new RectOffset(8, 14, 0, 0), TextAnchor.MiddleCenter);
        BlueprintUI.Fit(chip, true, false);
        Coin(chip.transform, 22f, 0f);
        chips.slotChipText = Text(chip.transform, "Price", "$10,000", 22f, UIColors.Ink, font, TextAlignmentOptions.Left, true);
        chips.slotChip = chip.rectTransform;

        BlueprintBox slotButton = BlueprintUI.Box(content, "SlotButton", UIColors.Highlight, UIColors.Ink, 3f, BlueprintUI.RadiusLarge);
        slotButton.SetShadow(UIColors.Ink, new Vector2(5f, 6f));
        OnTower(slotButton.rectTransform, 300f, 54f, new Vector2(0.5f, 0.5f));
        BlueprintUI.HRow(slotButton, 10f, new RectOffset(22, 22, 0, 0), TextAnchor.MiddleCenter);
        BlueprintUI.Fit(slotButton, true, false);
        chips.slotButtonIcon = Icon(slotButton.transform, TowerIconKind.Building, 26f, UIColors.Ink);
        chips.slotButtonText = Text(slotButton.transform, "Text", "", 22f, UIColors.Ink, font, TextAlignmentOptions.Left, true);
        chips.slotButton = slotButton.rectTransform;
        chips.slotButtonBox = slotButton;
        slotButton.gameObject.SetActive(false);

        // "Selected" tag (136 x 30) inside the selected room's top-right corner
        BlueprintBox selected = BlueprintUI.Box(content, "SelectedTag", UIColors.Highlight, UIColors.Ink, 2f, BlueprintUI.RadiusSmall);
        selected.SetShadow(new Color32(29, 36, 51, 217), new Vector2(2f, 3f));
        OnTower(selected.rectTransform, 136f, 30f, new Vector2(0f, 1f));
        BlueprintUI.HRow(selected, 6f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleCenter);
        Icon(selected.transform, TowerIconKind.Check, 18f, UIColors.Ink, 3f);
        Fixed(BlueprintUI.AutoSize(Text(selected.transform, "Text", "", 19f, UIColors.Ink, font), 13f), "tower.chip.selected");   // "Selected"
        chips.selectedTag = selected.rectTransform;
        selected.gameObject.SetActive(false);

        // Ghost tag 50 px above the ghost: "Place · $5,000" / "Doesn't fit"
        BlueprintBox ghost = BlueprintUI.Box(content, "GhostTag", UIColors.MoneySoft, UIColors.Ink, 2f, BlueprintUI.RadiusSmall);
        ghost.SetShadow(new Color32(29, 36, 51, 217), new Vector2(2f, 3f));
        OnTower(ghost.rectTransform, 200f, 36f, new Vector2(0f, 1f));
        BlueprintUI.HRow(ghost, 6f, new RectOffset(8, 12, 0, 0), TextAnchor.MiddleLeft);
        BlueprintUI.Fit(ghost, true, false);
        chips.ghostTagIcon = Icon(ghost.transform, TowerIconKind.Check, 22f, UIColors.Money, 2.8f);
        chips.ghostTagText = Text(ghost.transform, "Text", "", 19f, UIColors.Ink, font);
        chips.ghostTag = ghost.rectTransform;
        chips.ghostTagBox = ghost;
        ghost.gameObject.SetActive(false);

        // Cursor tooltip when a room doesn't fit
        BlueprintBox tip = BlueprintUI.Box(content, "Tooltip", UIColors.PaperHi, UIColors.Ink, 2f, BlueprintUI.RadiusSmall);
        tip.SetShadow(BlueprintUI.InkShadow, new Vector2(3f, 4f));
        OnTower(tip.rectTransform, 300f, 76f, new Vector2(0f, 1f));
        BlueprintUI.HRow(tip, 10f, new RectOffset(16, 16, 12, 12), TextAnchor.UpperLeft);
        BlueprintUI.Fit(tip, false, true);
        RectTransform arrow = Pointer(tip.transform, 14f, UIColors.PaperHi, 2f);
        arrow.anchoredPosition = new Vector2(15f, 1f);
        Icon(tip.transform, TowerIconKind.Warning, 24f, UIColors.Loss);
        RectTransform tipTexts = Column(tip.transform, "Texts", 0f);
        BlueprintUI.Layout(tipTexts, 0f, -1f, 1f, -1f, 0f);
        chips.tooltipTitle = Text(tipTexts, "Title", "", 19f, UIColors.Loss, font, TextAlignmentOptions.Left, true);
        chips.tooltipText = BlueprintUI.Wrap(Text(tipTexts, "Reason", "", 19f, UIColors.Ink, font));
        chips.tooltip = tip.rectTransform;
        tip.gameObject.SetActive(false);
    }

    static void BuildIntro(TowerView view, RectTransform content, TMP_FontAsset font)
    {
        BlueprintBox note = BlueprintUI.Box(content, "FirstVisitNote", UIColors.StickyYellow, Color.clear, 0f, BlueprintUI.Round(1f), true);
        note.SetShadow(new Color(0f, 0f, 0f, 0.28f), new Vector2(0f, 12f), 20f);
        RectTransform rect = OnTower(note.rectTransform, 400f, 340f, new Vector2(0f, 1f));
        rect.localEulerAngles = new Vector3(0f, 0f, -2f);
        BlueprintUI.VColumn(note, 12f, new RectOffset(26, 26, 36, 22));
        BlueprintUI.Fit(note, false, true);

        BlueprintBox band = BlueprintUI.Box(note.transform, "Band", new Color(0f, 0f, 0f, 0.07f), Color.clear, 0f, BlueprintUI.Round(0f));
        BlueprintUI.Layout(band).ignoreLayout = true;
        band.rectTransform.anchorMin = new Vector2(0f, 1f);
        band.rectTransform.anchorMax = new Vector2(1f, 1f);
        band.rectTransform.pivot = new Vector2(0.5f, 1f);
        band.rectTransform.sizeDelta = new Vector2(0f, 22f);
        band.rectTransform.anchoredPosition = Vector2.zero;

        Fixed(BlueprintUI.Caps(Text(note.transform, "Label", "", 17f, UIColors.InkSoft, font), 0.1f), "tower.intro.kicker");   // "FIRST VISIT"
        Fixed(BlueprintUI.Wrap(Text(note.transform, "Title", "", 32f, UIColors.Ink, font, TextAlignmentOptions.Left, true)), "tower.intro.title");
        Fixed(BlueprintUI.Wrap(Text(note.transform, "Body", "", 22f, UIColors.Ink, font)), "tower.intro.body").lineSpacing = 12f;
        RectTransform fillRow = Row(note.transform, "FillRow", 0f, TextAnchor.MiddleLeft, 40f);
        view.intro.fillBudgetButton = Button(fillRow, "FillBudget", "", TowerIconKind.Dice, font, 38f, 19f, UIColors.Highlight, UIColors.Ink,
                                             true, 2f, Color.clear, Vector2.zero, 10, 12, out _, 20f, textKey: "tower.button.fill_budget");
        Fixed(BlueprintUI.Wrap(Text(note.transform, "Aside", "", 19f, UIColors.InkSoft, font)), "tower.intro.aside");
        RectTransform buttons = Row(note.transform, "Buttons", 0f, TextAnchor.MiddleRight, 58f);
        view.intro.gotItButton = Button(buttons, "GotIt", "", TowerIconKind.Check, font, 54f, 22f, UIColors.PaperHi, UIColors.Ink, false, 3f,
                                        UIColors.Ink, new Vector2(4f, 5f), 22, 22, out _, 24f, textKey: "tower.intro.got_it");

        // White-ink arrow from the note down to the add-floor slot
        RectTransform arrowRect = BlueprintUI.NewRect("Arrow", note.transform);
        BlueprintUI.Layout(arrowRect).ignoreLayout = true;
        arrowRect.anchorMin = arrowRect.anchorMax = new Vector2(0f, 1f);
        arrowRect.pivot = new Vector2(0f, 1f);
        arrowRect.sizeDelta = Vector2.zero;
        BlueprintGraphic arrow = arrowRect.gameObject.AddComponent<BlueprintGraphic>();
        arrow.raycastTarget = false;
        BlueprintMeshBuilder m = arrow.Mesh;
        m.Clear();
        m.SetYDownFrame(Vector2.zero);
        var curve = new System.Collections.Generic.List<Vector2> { new Vector2(-8f, 200f) };
        BlueprintMeshBuilder.CubicTo(curve, new Vector2(-32f, 212f), new Vector2(-48f, 232f), new Vector2(-62f, 260f), 12);
        m.Stroke(curve, 4f, UIColors.BlueprintInk, false);
        var head = new System.Collections.Generic.List<Vector2> { new Vector2(-63f, 242f), new Vector2(-62f, 260f), new Vector2(-47f, 250f) };
        m.Stroke(head, 4f, UIColors.BlueprintInk, false);
        arrow.Commit();

        view.intro.note = rect;
        note.gameObject.SetActive(false);
    }

    static void BuildFloorSlip(TowerView view, RectTransform content, TMP_FontAsset font)
    {
        TowerFloorSlip slip = view.floorSlip;
        BlueprintBox box = BlueprintUI.Box(content, "FloorSlip", UIColors.PaperHi, UIColors.Ink, 2f, BlueprintUI.RadiusLarge, true);
        box.SetShadow(new Color(0f, 0f, 0f, 0.35f), new Vector2(0f, 12f), 24f);
        RectTransform rect = OnTower(box.rectTransform, 440f, 300f, new Vector2(0f, 1f));
        BlueprintUI.VColumn(box, 12f, new RectOffset(24, 24, 22, 22));
        BlueprintUI.Fit(box, false, true);
        RectTransform pointer = Pointer(box.transform, 16f, UIColors.PaperHi, 2f);
        pointer.anchoredPosition = new Vector2(-1f, -110f);
        Tape(box.transform, 120f, 340f, 0f, 4f);

        slip.titleText = BlueprintUI.Wrap(Text(box.transform, "Title", "", 26f, UIColors.Ink, font, TextAlignmentOptions.Left, true));
        slip.bodyText = BlueprintUI.Wrap(Text(box.transform, "Body", "", 19f, UIColors.InkSoft, font));
        RectTransform table = Column(box.transform, "Table", 4f);
        RectTransform budgetRow = Row(table, "Budget", 12f, TextAnchor.MiddleLeft, 28f);
        Fixed(Flex(Text(budgetRow, "Label", "", 19f, UIColors.Pencil, font)), "tower.label.build_budget");
        slip.budgetValue = Text(budgetRow, "Value", "$0", 19f, UIColors.Ink, font, TextAlignmentOptions.Right);
        RectTransform floorRow = Row(table, "Floor", 12f, TextAnchor.MiddleLeft, 28f);
        slip.floorLabel = Flex(Text(floorRow, "Label", "", 19f, UIColors.Pencil, font));
        slip.floorValue = Text(floorRow, "Value", "", 19f, UIColors.Loss, font, TextAlignmentOptions.Right);
        BlueprintBox rule = BlueprintUI.Box(table, "Rule", UIColors.Ink, Color.clear, 0f, BlueprintUI.Round(0f));
        BlueprintUI.Layout(rule, -1f, 2f, -1f, -1f, -1f, 2f);
        RectTransform afterRow = Row(table, "After", 12f, TextAnchor.MiddleLeft, 32f);
        Fixed(Flex(Text(afterRow, "Label", "", 19f, UIColors.Ink, font)), "tower.floor_slip.after");   // "Budget after"
        slip.afterValue = Text(afterRow, "Value", "$0", 22f, UIColors.Ink, font, TextAlignmentOptions.Right, true);

        RectTransform buttons = Row(box.transform, "Buttons", 16f, TextAnchor.MiddleLeft, 60f);
        slip.buildButton = Button(buttons, "Build", "", TowerIconKind.Check, font, 54f, 22f, UIColors.Highlight, UIColors.Ink, true, 3f,
                                  UIColors.Ink, new Vector2(5f, 6f), 22, 22, out slip.buildText, 26f, textKey: "tower.floor_slip.build");
        slip.cancelButton = Button(buttons, "Cancel", "", TowerIconKind.None, font, 54f, 22f, UIColors.PaperHi, UIColors.Ink, false, 3f,
                                   UIColors.Ink, new Vector2(4f, 5f), 20, 12, out _, 0f, "Esc", "tower.button.cancel");
        slip.fillLink = Link(box.transform, "FillBudget", "tower.link.fill_budget", TowerIconKind.Dice, 22f, UIColors.Ink, font);
        slip.slip = rect;
        box.gameObject.SetActive(false);
    }

    static void BuildSellSlip(TowerView view, RectTransform content, TMP_FontAsset font)
    {
        TowerSellSlip slip = view.sellSlip;
        BlueprintBox box = BlueprintUI.Box(content, "SellSlip", UIColors.Paper, UIColors.Ink, 3f, BlueprintUI.RadiusLarge, true);
        box.SetShadow(SoftShadow, new Vector2(0f, 22f), 40f);
        RectTransform rect = OnTower(box.rectTransform, 600f, 380f, new Vector2(0f, 1f));
        BlueprintUI.VColumn(box, 18f, new RectOffset(32, 32, 28, 32));
        BlueprintUI.Fit(box, false, true);
        slip.pointer = Pointer(box.transform, 22f, UIColors.Paper, 3f);

        RectTransform header = Row(box.transform, "Header", 14f, TextAnchor.MiddleLeft, 56f);
        BlueprintBox badge = BlueprintUI.Box(header, "Badge", UIColors.LossSoft, UIColors.Ink, 2f, BlueprintUI.RadiusSmall);
        BlueprintUI.Layout(badge, 52f, 52f, 0f, 0f, 52f, 52f);
        TowerIcon tag = Icon(badge.transform, TowerIconKind.Tag, 30f, UIColors.Ink);
        BlueprintUI.Layout(tag).ignoreLayout = true;
        BlueprintUI.Centered(tag.rectTransform, 30f, 30f);
        slip.titleText = Flex(BlueprintUI.AutoSize(Text(header, "Title", "", 32f, UIColors.Ink, font, TextAlignmentOptions.Left, true), 22f));
        BlueprintUI.Spacer(header, 0f, 0f).GetComponent<LayoutElement>().preferredWidth = 56f;

        RectTransform body = Column(box.transform, "Body", 6f);
        slip.refundText = BlueprintUI.Wrap(Text(body, "Refund", "", 22f, UIColors.Ink, font));
        slip.peopleText = BlueprintUI.Wrap(Text(body, "People", "", 22f, UIColors.Ink, font));
        slip.furnitureText = BlueprintUI.Wrap(Text(body, "Furniture", "", 19f, UIColors.InkSoft, font));
        slip.budgetText = Text(body, "Budget", "", 19f, UIColors.InkSoft, font);

        RectTransform buttons = Row(box.transform, "Buttons", 16f, TextAnchor.MiddleLeft, 74f);
        slip.sellButton = Button(buttons, "Sell", "", TowerIconKind.Tag, font, 68f, 28f, UIColors.Loss, UIColors.PaperHi, true, 3f,
                                 UIColors.Ink, new Vector2(5f, 6f), 28, 28, out slip.sellText, 28f);
        slip.keepButton = Button(buttons, "Keep", "", TowerIconKind.None, font, 68f, 28f, UIColors.PaperHi, UIColors.Ink, false, 3f,
                                 UIColors.Ink, new Vector2(4f, 5f), 28, 28, out _, textKey: "tower.sell.keep");

        slip.closeButton = RoundClose(box.transform, 52f, 26f);
        BlueprintUI.Layout(slip.closeButton).ignoreLayout = true;
        BlueprintUI.TopRight((RectTransform)slip.closeButton.transform, 20f, 20f, 52f, 52f);
        slip.slip = rect;
        box.gameObject.SetActive(false);
    }

    // ------------------------------------------------------------------
    // Around the tower: title block, scroll control, hints, tool tray, message, top bar
    // ------------------------------------------------------------------

    static void BuildTitleBlock(TowerView view, RectTransform root, TMP_FontAsset font)
    {
        BlueprintBox block = BlueprintUI.Box(root, "TitleBlock", Color.clear, UIColors.BlueprintInk, 2f, BlueprintUI.Round(0f));
        BottomLeft(block.rectTransform, 32f, 32f, 300f, 80f);
        BlueprintBox divider = BlueprintUI.Box(block.transform, "Divider", UIColors.BlueprintInk, Color.clear, 0f, BlueprintUI.Round(0f));
        BlueprintUI.TopLeft(divider.rectTransform, 0f, 39f, 300f, 2f);
        // TowerView writes both lines (the name already in capitals)
        view.titleBlockName = BlueprintUI.AutoSize(BlueprintUI.Caps(Text(block.transform, "Company", "", 17f, UIColors.BlueprintInk, font)), 12f);
        Placed(view.titleBlockName, 12f, 0f, 276f, 40f).verticalAlignment = VerticalAlignmentOptions.Middle;
        view.titleBlockInfo = BlueprintUI.AutoSize(Text(block.transform, "Info", "", 17f, UIColors.BlueprintInk, font), 12f);
        Placed(view.titleBlockInfo, 12f, 41f, 276f, 39f).verticalAlignment = VerticalAlignmentOptions.Middle;
    }

    static void BuildScrollControl(TowerView view, RectTransform root, TMP_FontAsset font)
    {
        TowerScrollControl control = view.scrollControl;
        BlueprintBox panel = BlueprintUI.Box(root, "ScrollControl", UIColors.PaperHi, UIColors.Ink, 2f, BlueprintUI.RadiusSmall, true);
        panel.SetShadow(BlueprintUI.InkShadow, new Vector2(3f, 4f));
        BottomCentre(panel.rectTransform, 344f - 960f, 433f, 176f, 176f);
        BlueprintUI.VColumn(panel, 0f, new RectOffset(0, 0, 10, 10), TextAnchor.MiddleCenter).childForceExpandWidth = false;
        control.upButton = RoundArrow(panel.transform, TowerIconKind.ChevronUp);
        RectTransform middle = Column(panel.transform, "Floors", 2f, TextAnchor.MiddleCenter);
        BlueprintUI.Layout(middle, 160f, 68f, 0f, 1f, 160f, 60f);
        middle.GetComponent<VerticalLayoutGroup>().childForceExpandWidth = true;
        control.rangeText = BlueprintUI.AutoSize(Text(middle, "Range", "", 19f, UIColors.Ink, font, TextAlignmentOptions.Center, true), 14f);
        control.totalText = Text(middle, "Total", "", 17f, UIColors.InkSoft, font, TextAlignmentOptions.Center);
        control.downButton = RoundArrow(panel.transform, TowerIconKind.ChevronDown);
        control.panel = panel.rectTransform;
        panel.gameObject.SetActive(false);
    }

    static Button RoundArrow(Transform parent, TowerIconKind icon)
    {
        BlueprintBox box = BlueprintUI.Box(parent, "Scroll", UIColors.PaperHi, UIColors.Ink, 3f, new Vector4(22f, 20f, 23f, 21f), true);
        box.SetShadow(UIColors.Ink, new Vector2(2f, 3f));
        BlueprintUI.Layout(box, 44f, 44f, 0f, 0f, 44f, 44f);
        TowerIcon glyph = Icon(box.transform, icon, 24f, UIColors.Ink, 2.8f);
        BlueprintUI.Layout(glyph).ignoreLayout = true;
        BlueprintUI.Centered(glyph.rectTransform, 24f, 24f);
        return BlueprintUI.MakeButton(box);
    }

    static void BuildHintStrip(TowerView view, RectTransform root, TMP_FontAsset font)
    {
        TowerHintStrip hints = view.hintStrip;
        BlueprintBox strip = BlueprintUI.Box(root, "HintStrip", UIColors.PaperHi, UIColors.Ink, 2f, BlueprintUI.RadiusSmall, true);
        strip.SetShadow(BlueprintUI.InkShadow, new Vector2(3f, 4f));
        BottomCentre(strip.rectTransform, 0f, 124f, 800f, 56f);
        BlueprintUI.HRow(strip, 14f, new RectOffset(20, 20, 0, 0), TextAnchor.MiddleCenter);
        BlueprintUI.Fit(strip, true, false);

        RectTransform click = Row(strip.transform, "Click", 8f);
        WordKeycap(click, "Click", "tower.key.click", font);
        hints.clickText = Text(click, "Text", "", 19f, UIColors.Ink, font);   // TowerView writes the hints
        Text(strip.transform, "Dot", "·", 19f, UIColors.Pencil, font);
        RectTransform escape = Row(strip.transform, "Escape", 8f);
        BlueprintUI.Keycap(escape, "Esc", font);
        hints.cancelButton = Link(escape, "Cancel", "tower.hint.cancel", TowerIconKind.None, 19f, UIColors.Ink, font);
        Text(strip.transform, "Dot", "·", 19f, UIColors.Pencil, font);
        hints.noteText = Text(strip.transform, "Note", "", 19f, UIColors.InkSoft, font);
        hints.gambleButton = Button(strip.transform, "GambleForIt", "", TowerIconKind.Dice, font, 40f, 19f, UIColors.Highlight, UIColors.Ink, true, 2f,
                                    UIColors.Ink, new Vector2(3f, 4f), 10, 12, out _, 20f, textKey: "tower.button.gamble_for_it");
        hints.gambleButton.gameObject.SetActive(false);
        hints.strip = strip.rectTransform;
        strip.gameObject.SetActive(false);
    }

    static void BuildTray(TowerView view, RectTransform root, TMP_FontAsset font)
    {
        string[] labels = { "Build room", "Add floor", "Furniture", "Move", "Sell", "Campus" };   // object names; the words: tower.tool.*
        string[] keys = { "tower.tool.build_room", "tower.tool.add_floor", "tower.tool.furniture", "tower.tool.move", "tower.tool.sell", "tower.tool.campus" };
        TowerIconKind[] icons = { TowerIconKind.Plus, TowerIconKind.Building, TowerIconKind.Sofa, TowerIconKind.Move, TowerIconKind.Tag, TowerIconKind.Campus };
        float[] widths = { 220f, 196f, 196f, 150f, 136f, 176f };
        float total = 0f;
        foreach (float w in widths) total += w;
        total += 10f * (widths.Length - 1);
        float trayWidth = total + 32f;

        BlueprintBox tray = BlueprintUI.Box(root, "ToolTray", UIColors.Paper, UIColors.Ink, 3f, BlueprintUI.RadiusLarge, true);
        tray.SetShadow(SoftShadow, new Vector2(0f, 22f), 48f);
        BottomCentre(tray.rectTransform, 0f, 32f, trayWidth, 80f);
        float x = (trayWidth - total) * 0.5f;
        view.toolButtons = new Button[labels.Length];
        for (int i = 0; i < labels.Length; i++)
        {
            BlueprintBox box = BlueprintUI.Box(tray.transform, labels[i], UIColors.PaperHi, UIColors.Ink, 2f, ToolRadii, true);
            box.SetShadow(BlueprintUI.InkShadow, new Vector2(3f, 4f));
            BlueprintUI.TopLeft(box.rectTransform, x, 12f, widths[i], 56f);
            BlueprintUI.HRow(box, 8f, new RectOffset(12, 10, 0, 0), TextAnchor.MiddleLeft);
            Icon(box.transform, icons[i], 26f, UIColors.Ink);
            Fixed(Flex(BlueprintUI.AutoSize(Text(box.transform, "Label", "", 22f, UIColors.Ink, font), 15f)), keys[i]);   // shrinks in a long language
            BlueprintUI.Keycap(box.transform, (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), font);
            view.toolButtons[i] = BlueprintUI.MakeButton(box);
            x += widths[i] + 10f;
        }
    }

    static void BuildMessage(TowerView view, RectTransform root, TMP_FontAsset font)
    {
        BlueprintBox chip = BlueprintUI.Box(root, "Message", UIColors.PaperHi, UIColors.Ink, 2f, BlueprintUI.RadiusSmall);
        chip.SetShadow(BlueprintUI.InkShadow, new Vector2(3f, 4f));
        BottomCentre(chip.rectTransform, 0f, 196f, 400f, 46f);
        BlueprintUI.HRow(chip, 0f, new RectOffset(18, 18, 0, 0), TextAnchor.MiddleCenter);
        BlueprintUI.Fit(chip, true, false);
        view.messageText = Text(chip.transform, "Text", "", 20f, UIColors.Ink, font, TextAlignmentOptions.Center);
        view.messageChip = chip.rectTransform;
        chip.gameObject.SetActive(false);
    }

    static void BuildTopBar(TowerView view, RectTransform root, TMP_FontAsset font)
    {
        TowerTopBar top = view.topBar;
        BlueprintBox bar = BlueprintUI.Box(root, "TopBar", UIColors.Paper, Color.clear, 0f, BlueprintUI.Round(0f), true);
        bar.SetShadow(TopBarShadow, new Vector2(0f, 8f), 18f);
        RectTransform rect = bar.rectTransform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(0f, 84f);
        rect.anchoredPosition = Vector2.zero;
        BlueprintBox edge = BlueprintUI.Box(bar.transform, "Edge", UIColors.Ink, Color.clear, 0f, BlueprintUI.Round(0f));
        BlueprintUI.Layout(edge).ignoreLayout = true;
        edge.rectTransform.anchorMin = Vector2.zero;
        edge.rectTransform.anchorMax = new Vector2(1f, 0f);
        edge.rectTransform.pivot = new Vector2(0.5f, 0f);
        edge.rectTransform.sizeDelta = new Vector2(0f, 3f);
        edge.rectTransform.anchoredPosition = Vector2.zero;
        BlueprintUI.HRow(bar, 12f, new RectOffset(32, 32, 0, 3), TextAnchor.MiddleLeft);

        RectTransform title = Row(bar.transform, "Title", 12f);
        Icon(title, TowerIconKind.Building, 32f, UIColors.Ink);
        Fixed(Text(title, "Text", "", 44f, UIColors.Ink, font, TextAlignmentOptions.Left, true), "tower.top.title");   // "HQ"

        BlueprintBox company = BlueprintUI.Box(bar.transform, "Company", UIColors.StickyBlue, UIColors.Ink, 2f, BlueprintUI.RadiusSmall);
        BlueprintUI.HRow(company, 0f, new RectOffset(14, 14, 0, 0), TextAnchor.MiddleCenter);
        BlueprintUI.Layout(company, -1f, 44f, -1f, -1f, -1f, 44f);
        top.companyText = Text(company.transform, "Text", "", 19f, UIColors.Ink, font);   // TowerView writes it

        BlueprintBox budget = BlueprintUI.Box(bar.transform, "BuildBudget", UIColors.PaperHi, UIColors.Ink, 2f, BlueprintUI.RadiusSmall);
        budget.SetShadow(UIColors.Ink, new Vector2(3f, 4f));
        BlueprintUI.HRow(budget, 10f, new RectOffset(8, 16, 0, 0), TextAnchor.MiddleLeft);
        BlueprintUI.Layout(budget, -1f, 52f, -1f, -1f, -1f, 52f);
        Coin(budget.transform, 34f, 22f);
        Fixed(Text(budget.transform, "Label", "", 19f, UIColors.InkSoft, font), "tower.label.build_budget");
        top.budgetText = Text(budget.transform, "Amount", "$0", 26f, UIColors.Ink, font, TextAlignmentOptions.Left, true);

        top.fillBudgetButton = Button(bar.transform, "FillBudget", "", TowerIconKind.Dice, font, 54f, 22f, UIColors.Highlight, UIColors.Ink,
                                      true, 3f, UIColors.Ink, new Vector2(5f, 6f), 22, 22, out _, textKey: "tower.button.fill_budget");

        BlueprintUI.Spacer(bar.transform, 1f, 0f);

        BlueprintBox seats = BlueprintUI.Box(bar.transform, "Seats", UIColors.PaperHi, UIColors.Ink, 2f, BlueprintUI.RadiusSmall);
        BlueprintUI.HRow(seats, 8f, new RectOffset(10, 14, 0, 0), TextAnchor.MiddleLeft);
        BlueprintUI.Layout(seats, -1f, 44f, -1f, -1f, -1f, 44f);
        top.seatsIcon = Icon(seats.transform, TowerIconKind.People, 24f, UIColors.Ink);
        top.seatsText = Text(seats.transform, "Text", "", 19f, UIColors.Ink, font);   // "Employees 12 / 20" (TowerView writes it)
        top.seatsBox = seats;

        top.upkeepText = Text(bar.transform, "Upkeep", "", 19f, UIColors.Loss, font);   // "Upkeep −$1,234/day"

        RectTransform value = Row(bar.transform, "CompanyValue", 8f);
        Fixed(Text(value, "Label", "", 19f, UIColors.Pencil, font), "tower.label.company_value");
        top.valueText = Text(value, "Amount", "$0", 19f, UIColors.Ink, font);

        top.exitButton = Button(bar.transform, "BackToOffice", "", TowerIconKind.Home, font, 54f, 22f, UIColors.PaperHi, UIColors.Ink,
                                false, 3f, UIColors.Ink, new Vector2(4f, 5f), 20, 12, out _, 24f, "Q", "tower.top.back");
    }

    // ------------------------------------------------------------------
    // The campus (2026-09-29): the campus bar, the purchase slip, scrolling lists and the catalog's filters
    // ------------------------------------------------------------------

    /// <summary>
    /// The campus bar hanging under the top bar, in the middle: previous / the building in view (click: centre it) / next,
    /// "Buy a building" (the Campus drawer), zoom out / in and the whole-campus view.
    /// </summary>
    static void BuildCampusBar(TowerView view, RectTransform root, TMP_FontAsset font)
    {
        TowerCampusBar bar = view.campusBar;
        BlueprintBox strip = BlueprintUI.Box(root, "CampusBar", UIColors.Paper, UIColors.Ink, 2f, new Vector4(0f, 0f, 14f, 16f), true);
        strip.SetShadow(BlueprintUI.InkShadow, new Vector2(3f, 4f));
        RectTransform rect = strip.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -84f);
        rect.sizeDelta = new Vector2(760f, 54f);
        BlueprintUI.HRow(strip, 8f, new RectOffset(10, 10, 5, 7), TextAnchor.MiddleCenter);
        BlueprintUI.Fit(strip, true, false);

        bar.previousButton = SmallSquare(strip.transform, "Previous", TowerIconKind.ArrowLeft);
        BlueprintBox name = BlueprintUI.Box(strip.transform, "Building", UIColors.PaperHi, UIColors.Ink, 2f, BlueprintUI.RadiusSmall, true);
        BlueprintUI.HRow(name, 8f, new RectOffset(12, 14, 0, 0), TextAnchor.MiddleCenter);
        BlueprintUI.Layout(name, -1f, 40f, -1f, -1f, 200f, 40f);
        Icon(name.transform, TowerIconKind.Building, 22f, UIColors.Ink);
        bar.nameText = Text(name.transform, "Name", "", 19f, UIColors.Ink, font, TextAlignmentOptions.Left, true);
        bar.countText = Text(name.transform, "Count", "", 17f, UIColors.InkSoft, font);
        bar.nameButton = BlueprintUI.MakeButton(name);
        bar.nextButton = SmallSquare(strip.transform, "Next", TowerIconKind.ArrowRight);
        bar.buyButton = Button(strip.transform, "BuyBuilding", "", TowerIconKind.Plus, font, 40f, 19f, UIColors.Highlight, UIColors.Ink, true, 2f,
                               UIColors.Ink, new Vector2(3f, 4f), 10, 12, out _, 20f, textKey: "tower.campus_bar.buy");
        BlueprintUI.Spacer(strip.transform, 0f, 0f).GetComponent<LayoutElement>().preferredWidth = 6f;
        bar.zoomOutButton = SmallSquare(strip.transform, "ZoomOut", TowerIconKind.Minus);
        bar.zoomInButton = SmallSquare(strip.transform, "ZoomIn", TowerIconKind.Plus);
        bar.overviewButton = Button(strip.transform, "Overview", "", TowerIconKind.Campus, font, 40f, 19f, UIColors.PaperHi, UIColors.Ink, false, 2f,
                                    UIColors.Ink, new Vector2(3f, 4f), 10, 12, out bar.overviewText, 22f);
        bar.panel = rect;
    }

    /// <summary>A 40 px paper button with an icon (the campus bar's arrows and zoom).</summary>
    static Button SmallSquare(Transform parent, string name, TowerIconKind icon)
    {
        BlueprintBox box = BlueprintUI.Box(parent, name, UIColors.PaperHi, UIColors.Ink, 2f, BlueprintUI.RadiusSmall, true);
        box.SetShadow(UIColors.Ink, new Vector2(2f, 3f));
        BlueprintUI.Layout(box, 40f, 40f, 0f, 0f, 40f, 40f);
        TowerIcon glyph = Icon(box.transform, icon, 22f, UIColors.Ink, 2.8f);
        BlueprintUI.Layout(glyph).ignoreLayout = true;
        BlueprintUI.Centered(glyph.rectTransform, 22f, 22f);
        return BlueprintUI.MakeButton(box);
    }

    /// <summary>
    /// The purchase slip (a building, a skybridge, a room's next level) over a light scrim: what it is and adds, the build
    /// budget before and after, Buy, "Gamble for it" while the budget is short, and Cancel.
    /// </summary>
    static void BuildPurchaseSlip(TowerView view, RectTransform root, TMP_FontAsset font)
    {
        TowerPurchaseSlip slip = view.purchaseSlip;
        RectTransform layer = BlueprintUI.Stretch(BlueprintUI.NewRect("PurchaseSlip", root));
        Image scrim = layer.gameObject.AddComponent<Image>();
        scrim.color = UIColors.ScrimLight;
        scrim.raycastTarget = true;
        slip.layer = layer;

        BlueprintBox paper = BlueprintUI.Box(layer, "Slip", UIColors.PaperHi, UIColors.Ink, 3f, BlueprintUI.RadiusLarge, true);
        paper.SetShadow(SoftShadow, new Vector2(0f, 22f), 40f);
        RectTransform rect = BlueprintUI.Centered(paper.rectTransform, 600f, 420f);
        BlueprintUI.VColumn(paper, 14f, new RectOffset(30, 30, 28, 28));
        BlueprintUI.Fit(paper, false, true);
        Tape(paper.transform, 140f, 300f, 0f, -3f);
        slip.slip = rect;

        RectTransform header = Row(paper.transform, "Header", 14f, TextAnchor.MiddleLeft, 60f);
        slip.iconBox = BlueprintUI.Box(header, "IconBox", UIColors.StickyBlue, UIColors.Ink, 2f, TowerGeo.RoomRadii);
        BlueprintUI.Layout(slip.iconBox, 56f, 56f, 0f, 0f, 56f, 56f);
        slip.icon = Icon(slip.iconBox.transform, TowerIconKind.Building, 32f, UIColors.Ink);
        BlueprintUI.Layout(slip.icon).ignoreLayout = true;
        BlueprintUI.Centered(slip.icon.rectTransform, 32f, 32f);
        slip.titleText = Flex(BlueprintUI.AutoSize(BlueprintUI.Wrap(Text(header, "Title", "", 28f, UIColors.Ink, font, TextAlignmentOptions.Left, true)), 18f));
        BlueprintUI.Spacer(header, 0f, 0f).GetComponent<LayoutElement>().preferredWidth = 48f;

        slip.bodyText = BlueprintUI.Wrap(Text(paper.transform, "Body", "", 20f, UIColors.Ink, font));
        slip.noteText = BlueprintUI.Wrap(Text(paper.transform, "Note", "", 19f, UIColors.InkSoft, font));

        RectTransform table = Column(paper.transform, "Table", 4f);
        RectTransform budgetRow = Row(table, "Budget", 12f, TextAnchor.MiddleLeft, 28f);
        Fixed(Flex(Text(budgetRow, "Label", "", 19f, UIColors.Pencil, font)), "tower.label.build_budget");
        slip.budgetValue = Text(budgetRow, "Value", "$0", 19f, UIColors.Ink, font, TextAlignmentOptions.Right);
        RectTransform costRow = Row(table, "Cost", 12f, TextAnchor.MiddleLeft, 28f);
        Fixed(Flex(Text(costRow, "Label", "", 19f, UIColors.Pencil, font)), "tower.purchase.cost");
        slip.costValue = Text(costRow, "Value", "", 19f, UIColors.Loss, font, TextAlignmentOptions.Right);
        BlueprintBox rule = BlueprintUI.Box(table, "Rule", UIColors.Ink, Color.clear, 0f, BlueprintUI.Round(0f));
        BlueprintUI.Layout(rule, -1f, 2f, -1f, -1f, -1f, 2f);
        RectTransform afterRow = Row(table, "After", 12f, TextAnchor.MiddleLeft, 32f);
        Fixed(Flex(Text(afterRow, "Label", "", 19f, UIColors.Ink, font)), "tower.floor_slip.after");   // "Budget after"
        slip.afterValue = Text(afterRow, "Value", "$0", 22f, UIColors.Ink, font, TextAlignmentOptions.Right, true);

        slip.reasonText = BlueprintUI.Wrap(Text(paper.transform, "Reason", "", 19f, UIColors.Loss, font));
        slip.reasonText.gameObject.SetActive(false);

        RectTransform buttons = Row(paper.transform, "Buttons", 14f, TextAnchor.MiddleLeft, 60f);
        slip.confirmButton = Button(buttons, "Buy", "", TowerIconKind.Check, font, 54f, 22f, UIColors.Highlight, UIColors.Ink, true, 3f,
                                    UIColors.Ink, new Vector2(5f, 6f), 20, 20, out slip.confirmText, 24f);
        slip.gambleButton = Button(buttons, "GambleForIt", "", TowerIconKind.Dice, font, 54f, 20f, UIColors.Felt, UIColors.Gold, true, 3f,
                                   BlueprintUI.FeltShadow, new Vector2(5f, 6f), 18, 18, out slip.gambleText, 24f);
        slip.cancelButton = Button(buttons, "Cancel", "", TowerIconKind.None, font, 54f, 22f, UIColors.PaperHi, UIColors.Ink, false, 3f,
                                   UIColors.Ink, new Vector2(4f, 5f), 20, 12, out _, 0f, "Esc", "tower.button.cancel");

        slip.closeButton = RoundClose(paper.transform, 48f, 24f);
        BlueprintUI.Layout(slip.closeButton).ignoreLayout = true;
        BlueprintUI.TopRight((RectTransform)slip.closeButton.transform, 16f, 16f, 48f, 48f);
        layer.gameObject.SetActive(false);
    }

    /// <summary>
    /// A scrolling list that takes the rest of its column (the mouse wheel scrolls it). Returns the content: a column that
    /// grows with what is put in it.
    /// </summary>
    static RectTransform ScrollList(Transform parent, string name, float spacing)
    {
        RectTransform scroller = BlueprintUI.NewRect(name + "Scroll", parent);
        BlueprintUI.Layout(scroller, -1f, 100f, -1f, 1f, -1f, 60f);
        RectTransform content = ScrollArea(scroller, name);
        BlueprintUI.VColumn(content, spacing, new RectOffset(0, 6, 4, 10));   // 6 px on the right for the cards' shadows
        return content;
    }

    /// <summary>A viewport filling <paramref name="holder"/> with a content that grows downward (a ScrollRect on the holder).</summary>
    static RectTransform ScrollArea(RectTransform holder, string name)
    {
        RectTransform viewport = BlueprintUI.Stretch(BlueprintUI.NewRect("Viewport", holder));
        Image catcher = viewport.gameObject.AddComponent<Image>();   // takes the wheel between the cards
        catcher.color = Color.clear;
        catcher.raycastTarget = true;
        viewport.gameObject.AddComponent<RectMask2D>();

        RectTransform content = BlueprintUI.NewRect(name, viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;
        BlueprintUI.Fit(content, false, true);

        ScrollRect scroll = holder.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.inertia = true;
        scroll.decelerationRate = 0.135f;
        scroll.scrollSensitivity = 40f;
        return content;
    }

    /// <summary>The Rooms page's category filters: All, Offices, Staff, Business, Prestige (TowerView keeps the choice).</summary>
    static Button[] FilterRow(Transform parent, TMP_FontAsset font)
    {
        RectTransform row = Row(parent, "Filters", 6f, TextAnchor.MiddleLeft, 34f);
        string[] keys = { "tower.filter.all", "tower.filter.offices", "tower.filter.staff", "tower.filter.business", "tower.filter.prestige" };
        var buttons = new Button[keys.Length];
        for (int i = 0; i < keys.Length; i++)
        {
            BlueprintBox box = BlueprintUI.Box(row, "Filter " + i, UIColors.PaperHi, UIColors.Ink, 2f, BlueprintUI.RadiusSmall, true);
            BlueprintUI.HRow(box, 0f, new RectOffset(8, 8, 0, 0), TextAnchor.MiddleCenter);
            BlueprintUI.Layout(box, -1f, 32f, 1f, -1f, 40f, 32f);
            Fixed(BlueprintUI.AutoSize(Text(box.transform, "Label", "", 17f, UIColors.Ink, font, TextAlignmentOptions.Center), 11f), keys[i]);
            buttons[i] = BlueprintUI.MakeButton(box);
        }
        return buttons;
    }


    // ------------------------------------------------------------------
    // Left drawer: Rooms, Furniture and Campus tabs on a manila folder
    // ------------------------------------------------------------------

    static void BuildDrawer(TowerView view, RectTransform root, TMP_FontAsset font)
    {
        TowerDrawer drawer = view.drawer;
        RectTransform panel = BlueprintUI.NewRect("Drawer", root);
        BlueprintUI.TopLeft(panel, 32f, 116f, 420f, 828f);
        drawer.panel = panel;

        drawer.roomsTab = FolderTab(panel, "Rooms", "tower.drawer.rooms", TowerIconKind.Building, 12f, 118f, font);
        drawer.furnitureTab = FolderTab(panel, "Furniture", "tower.drawer.furniture", TowerIconKind.Sofa, 134f, 128f, font);
        drawer.campusTab = FolderTab(panel, "Campus", "tower.drawer.campus", TowerIconKind.Campus, 266f, 100f, font);

        BlueprintBox folder = BlueprintUI.Box(panel, "Folder", UIColors.Manila, UIColors.Ink, 3f, BlueprintUI.RadiusCard, true);
        folder.SetShadow(SoftShadow, new Vector2(0f, 22f), 48f);
        BlueprintUI.TopLeft(folder.rectTransform, 0f, 49f, 420f, 779f);
        // CSS box model: the 3 px border is inside the box, so the design's 14/16 px padding starts 3 px further in
        BlueprintUI.VColumn(folder, 12f, new RectOffset(19, 19, 17, 17));
        folder.transform.SetSiblingIndex(1);

        drawer.closeButton = RoundClose(panel, 48f, 24f);
        BlueprintUI.TopLeft((RectTransform)drawer.closeButton.transform, 368f, 0f, 48f, 48f);

        // Rooms page: "Pick a room, then click a free spot.", the category filters and the index cards (they scroll)
        RectTransform rooms = Column(folder.transform, "RoomsPage", 10f);
        BlueprintUI.Layout(rooms, -1f, -1f, -1f, 1f);
        Fixed(BlueprintUI.Wrap(Text(rooms, "Hint", "", 19f, UIColors.InkSoft, font)), "tower.drawer.rooms.hint");
        drawer.filterButtons = FilterRow(rooms, font);
        drawer.cardList = ScrollList(rooms, "Cards", 8f);
        drawer.cardTemplate = CatalogCard(folder.transform, font);
        drawer.roomsPage = rooms;

        // Campus page: buy a building (it goes to the right of the last one), build a skybridge between neighbours
        RectTransform campus = Column(folder.transform, "CampusPage", 10f);
        BlueprintUI.Layout(campus, -1f, -1f, -1f, 1f);
        Fixed(BlueprintUI.Wrap(Text(campus, "Hint", "", 19f, UIColors.InkSoft, font)), "tower.drawer.campus.hint");
        drawer.campusSummary = BlueprintUI.AutoSize(Text(campus, "Summary", "", 17f, UIColors.Ink, font), 12f);
        RectTransform campusList = ScrollList(campus, "Lists", 8f);
        Fixed(BlueprintUI.AutoSize(BlueprintUI.Caps(Text(campusList, "BuildingsLabel", "", 17f, UIColors.Pencil, font)), 12f), "tower.drawer.campus.buildings");
        drawer.buildingList = Column(campusList, "Buildings", 8f);
        Fixed(BlueprintUI.AutoSize(BlueprintUI.Caps(Text(campusList, "LinksLabel", "", 17f, UIColors.Pencil, font)), 12f), "tower.drawer.campus.links");
        drawer.linkList = Column(campusList, "Links", 8f);
        drawer.campusPage = campus;
        campus.gameObject.SetActive(false);

        // Furniture page: the selected room's slots and the picked slot's card
        RectTransform furniture = Column(folder.transform, "FurniturePage", 12f);
        drawer.emptyText = Fixed(BlueprintUI.Wrap(Text(furniture, "Empty", "", 22f, UIColors.InkSoft, font)), "tower.drawer.furniture.empty");
        RectTransform info = Column(furniture, "Room", 2f);
        drawer.roomTitle = BlueprintUI.AutoSize(Text(info, "Name", "", 26f, UIColors.Ink, font, TextAlignmentOptions.Left, true), 18f);
        drawer.roomInfo = BlueprintUI.AutoSize(Text(info, "Info", "", 19f, UIColors.InkSoft, font), 15f);
        drawer.roomInfoGroup = info;
        drawer.slotList = Column(furniture, "Slots", 8f);
        drawer.slotRowTemplate = DrawerRow(folder.transform, font);
        BuildItemCard(drawer, furniture, font);
        drawer.furniturePage = furniture;
        furniture.gameObject.SetActive(false);

        panel.gameObject.SetActive(false);
    }

    static Button FolderTab(RectTransform panel, string name, string key, TowerIconKind icon, float x, float width, TMP_FontAsset font)
    {
        BlueprintBox tab = BlueprintUI.Box(panel, name + "Tab", UIColors.ManilaEdge, UIColors.Ink, 3f, TabRadii, true);
        BlueprintUI.TopLeft(tab.rectTransform, x, 8f, width, 44f);
        BlueprintUI.HRow(tab, 8f, new RectOffset(12, 12, 0, 0), TextAnchor.MiddleCenter);
        Icon(tab.transform, icon, 24f, UIColors.Ink);
        Fixed(BlueprintUI.AutoSize(Text(tab.transform, "Label", "", 22f, UIColors.Ink, font), 15f), key);
        // Covers the folder's top edge under the tab when it's the active one (drawn over the folder)
        BlueprintBox patch = BlueprintUI.Box(tab.transform, "Patch", UIColors.Manila, Color.clear, 0f, BlueprintUI.Round(0f));
        BlueprintUI.Layout(patch).ignoreLayout = true;
        RectTransform rect = patch.rectTransform;
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(3f, -4f);
        rect.offsetMax = new Vector2(-3f, 4f);
        rect.anchoredPosition = new Vector2(0f, 0f);
        return BlueprintUI.MakeButton(tab);
    }

    static TowerCatalogCard CatalogCard(Transform parent, TMP_FontAsset font)
    {
        BlueprintBox box = BlueprintUI.Box(parent, "CardTemplate", UIColors.PaperHi, UIColors.Ink, 2f, BlueprintUI.RadiusLarge, true);
        box.SetShadow(BlueprintUI.InkShadow, new Vector2(3f, 4f));
        BlueprintUI.Layout(box, -1f, 90f, -1f, -1f, -1f, 90f);
        TowerCatalogCard card = box.gameObject.AddComponent<TowerCatalogCard>();
        card.background = box;
        card.button = BlueprintUI.MakeButton(box);
        card.lines = Rules(box.transform, 382f, 90f, 46f, 30f, 29f);

        BlueprintBox iconBox = BlueprintUI.Box(box.transform, "IconBox", UIColors.StickyBlue, UIColors.Ink, 2f, TowerGeo.RoomRadii);
        iconBox.rectTransform.anchorMin = iconBox.rectTransform.anchorMax = new Vector2(0f, 0.5f);
        iconBox.rectTransform.pivot = new Vector2(0f, 0.5f);
        iconBox.rectTransform.sizeDelta = new Vector2(56f, 56f);
        iconBox.rectTransform.anchoredPosition = new Vector2(10f, 0f);
        card.iconBox = iconBox;
        card.icon = Icon(iconBox.transform, TowerIconKind.People, 32f, UIColors.Ink);
        BlueprintUI.Layout(card.icon).ignoreLayout = true;
        BlueprintUI.Centered(card.icon.rectTransform, 32f, 32f);
        BlueprintBox badge = BlueprintUI.Box(iconBox.transform, "Lock", UIColors.PaperHi, UIColors.Ink, 2f, BlueprintUI.Round(14f));
        BlueprintUI.TopRight(badge.rectTransform, -12f, -12f, 28f, 28f);
        TowerIcon lockIcon = Icon(badge.transform, TowerIconKind.Lock, 16f, UIColors.Ink, 2.8f);
        BlueprintUI.Layout(lockIcon).ignoreLayout = true;
        BlueprintUI.Centered(lockIcon.rectTransform, 16f, 16f);
        card.lockBadge = badge.rectTransform;
        badge.gameObject.SetActive(false);

        // Top line (46 px): name and price; bottom line: what it does and its width (TowerCatalogCard.Bind writes them)
        card.nameText = BlueprintUI.AutoSize(Placed(Text(box.transform, "Name", "", 22f, UIColors.Ink, font, TextAlignmentOptions.Left, true), 78f, 0f, 190f, 46f), 16f);
        card.nameText.verticalAlignment = VerticalAlignmentOptions.Middle;
        card.costText = Text(box.transform, "Cost", "$0", 22f, UIColors.Ink, font, TextAlignmentOptions.Right, true);
        BlueprintUI.TopRight(card.costText.rectTransform, 14f, 0f, 110f, 46f);
        card.costText.verticalAlignment = VerticalAlignmentOptions.Middle;
        card.effectText = BlueprintUI.AutoSize(Placed(Text(box.transform, "Effect", "", 19f, UIColors.InkSoft, font), 78f, 56f, 210f, 28f), 14f);
        card.tilesText = BlueprintUI.AutoSize(Text(box.transform, "Tiles", "", 19f, UIColors.InkSoft, font, TextAlignmentOptions.Right), 14f);
        BlueprintUI.TopRight(card.tilesText.rectTransform, 14f, 56f, 80f, 28f);
        card.reasonText = BlueprintUI.Wrap(Placed(Text(box.transform, "Reason", "", 19f, UIColors.InkSoft, font), 78f, 44f, 290f, 52f));
        card.reasonText.lineSpacing = -8f;
        card.reasonText.gameObject.SetActive(false);
        box.gameObject.SetActive(false);
        return card;
    }

    static TowerSlotRow DrawerRow(Transform parent, TMP_FontAsset font)
    {
        BlueprintBox box = BlueprintUI.Box(parent, "SlotRowTemplate", UIColors.PaperHi, UIColors.Ink, 2f, BlueprintUI.RadiusSmall, true);
        box.SetShadow(BlueprintUI.InkShadow, new Vector2(3f, 4f));
        BlueprintUI.Layout(box, -1f, 52f, -1f, -1f, -1f, 52f);
        BlueprintUI.HRow(box, 10f, new RectOffset(12, 14, 0, 0), TextAnchor.MiddleLeft);
        TowerSlotRow row = box.gameObject.AddComponent<TowerSlotRow>();
        row.background = box;
        row.button = BlueprintUI.MakeButton(box);
        row.icon = Icon(box.transform, TowerIconKind.Desk, 28f, UIColors.Ink);
        row.nameText = Flex(BlueprintUI.AutoSize(Text(box.transform, "Name", "", 22f, UIColors.Ink, font), 16f));   // TowerSlotRow.Bind writes it
        RectTransform pips = Row(box.transform, "Pips", 3f);
        row.pips = Pips(pips, 12f);
        box.gameObject.SetActive(false);
        return row;
    }

    static BlueprintBox[] Pips(Transform parent, float size)
    {
        var pips = new BlueprintBox[5];
        for (int i = 0; i < 5; i++)
        {
            pips[i] = BlueprintUI.Box(parent, "Pip " + (i + 1), UIColors.PaperHi, UIColors.Ink, 2f, BlueprintUI.Round(size * 0.5f));
            BlueprintUI.Layout(pips[i], size, size, 0f, 0f, size, size);
        }
        return pips;
    }

    static void BuildItemCard(TowerDrawer drawer, RectTransform parent, TMP_FontAsset font)
    {
        BlueprintBox card = BlueprintUI.Box(parent, "ItemCard", UIColors.PaperHi, UIColors.Ink, 2f, BlueprintUI.RadiusLarge);
        card.SetShadow(BlueprintUI.InkShadow, new Vector2(3f, 4f));
        BlueprintUI.VColumn(card, 10f, new RectOffset(16, 16, 0, 16));
        card.gameObject.AddComponent<RectMask2D>();
        Rules(card.transform, 382f, 400f, 46f, 30f, 29f);

        RectTransform header = Row(card.transform, "Header", 10f, TextAnchor.MiddleLeft, 46f);
        drawer.itemIcon = Icon(header, TowerIconKind.Desk, 28f, UIColors.Ink);
        drawer.itemName = Flex(BlueprintUI.AutoSize(Text(header, "Name", "", 26f, UIColors.Ink, font, TextAlignmentOptions.Left, true), 18f));
        drawer.itemLevel = Text(header, "Level", "", 19f, UIColors.InkSoft, font, TextAlignmentOptions.Right);
        drawer.itemNext = BlueprintUI.AutoSize(Text(card.transform, "Next", "", 19f, UIColors.Ink, font), 14f);
        BlueprintUI.Layout(drawer.itemNext, -1f, 26f);

        BlueprintBox upgrade = BlueprintUI.Box(card.transform, "Upgrade", UIColors.Highlight, UIColors.Ink, 3f, BlueprintUI.RadiusLarge, true);
        upgrade.SetShadow(UIColors.Ink, new Vector2(5f, 6f));
        BlueprintUI.Layout(upgrade, -1f, 60f, -1f, -1f, -1f, 60f);
        BlueprintUI.HRow(upgrade, 10f, new RectOffset(16, 16, 0, 0), TextAnchor.MiddleCenter);
        Icon(upgrade.transform, TowerIconKind.Coin, 24f, UIColors.Ink);
        drawer.upgradeText = Text(upgrade.transform, "Label", "", 22f, UIColors.Ink, font, TextAlignmentOptions.Center, true);   // TowerView writes it
        drawer.upgradeButton = BlueprintUI.MakeButton(upgrade);

        RectTransform need = Row(card.transform, "Need", 8f, TextAnchor.UpperLeft);
        Icon(need, TowerIconKind.Warning, 22f, UIColors.Loss);
        RectTransform needTexts = Column(need, "Texts", 0f);
        drawer.needTitle = Text(needTexts, "Title", "", 19f, UIColors.Loss, font, TextAlignmentOptions.Left, true);
        drawer.needText = Text(needTexts, "Text", "", 19f, UIColors.InkSoft, font);
        drawer.needNote = need;
        drawer.fillLink = Link(card.transform, "FillBudget", "tower.link.fill_budget", TowerIconKind.Dice, 22f, UIColors.Ink, font);
        drawer.itemCard = card.rectTransform;
    }

    // ------------------------------------------------------------------
    // Right clipboard: the room card and the furniture card
    // ------------------------------------------------------------------

    static void BuildClipboard(TowerView view, RectTransform root, TMP_FontAsset font)
    {
        TowerClipboard clip = view.clipboard;
        BlueprintBox board = BlueprintUI.Box(root, "Clipboard", UIColors.Clipboard, UIColors.Ink, 3f, BlueprintUI.RadiusLarge, true);
        board.SetShadow(SoftShadow, new Vector2(0f, 22f), 48f);
        BlueprintUI.TopRight(board.rectTransform, 32f, 116f, 420f, 828f);
        BlueprintBox paper = BlueprintUI.Box(board.transform, "Paper", UIColors.PaperHi, UIColors.Ink, 2f, TowerGeo.RoomRadii, true);
        BlueprintUI.Stretch(paper.rectTransform, 17f, 17f, 17f, 17f);
        Rules(paper.transform, 386f, 794f, 0f, 35f, 33f);

        // Metal clip at the top
        BlueprintBox clamp = BlueprintUI.Box(board.transform, "Clip", UIColors.Hex(0x9EA3AB), UIColors.Ink, 3f, BlueprintUI.Round(10f));
        clamp.SetGradient(UIColors.Hex(0xC4C8CF), new Vector2(0.5f, 0.1f));
        BlueprintUI.TopLeft(clamp.rectTransform, 143f, -22f, 128f, 44f);
        BlueprintBox slot = BlueprintUI.Box(clamp.transform, "Slot", UIColors.Hex(0x5A5F66), UIColors.Ink, 2f, BlueprintUI.Round(7f));
        BlueprintUI.TopLeft(slot.rectTransform, 38f, 8f, 46f, 14f);

        BuildRoomPage(clip, paper.rectTransform, font);
        BuildSlotPage(clip, paper.rectTransform, font);
        clip.closeButton = RoundClose(paper.transform, 52f, 26f);
        BlueprintUI.TopRight((RectTransform)clip.closeButton.transform, 10f, 10f, 52f, 52f);
        clip.panel = board.rectTransform;
        board.gameObject.SetActive(false);
    }

    static void BuildRoomPage(TowerClipboard clip, RectTransform paper, TMP_FontAsset font)
    {
        // The card scrolls when a big room's furniture list runs long (the level block made it taller)
        RectTransform holder = BlueprintUI.Stretch(BlueprintUI.NewRect("RoomPage", paper));
        clip.roomPage = holder;
        RectTransform page = ScrollArea(holder, "Content");
        BlueprintUI.VColumn(page, 10f, new RectOffset(18, 18, 38, 18));

        RectTransform header = Column(page, "Header", 2f);
        header.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(0, 66, 0, 0);
        RectTransform nameRow = Row(header, "NameRow", 10f, TextAnchor.MiddleLeft, 44f);
        clip.swatch = BlueprintUI.Box(nameRow, "Swatch", UIColors.StickyBlue, UIColors.Ink, 2f, TowerGeo.RoomRadii);
        BlueprintUI.Layout(clip.swatch, 40f, 40f, 0f, 0f, 40f, 40f);
        clip.swatchIcon = Icon(clip.swatch.transform, TowerIconKind.People, 26f, UIColors.Ink);
        BlueprintUI.Layout(clip.swatchIcon).ignoreLayout = true;
        BlueprintUI.Centered(clip.swatchIcon.rectTransform, 26f, 26f);
        // TowerView writes the card (the fixed words keep to the String Table themselves)
        clip.nameText = Flex(BlueprintUI.AutoSize(Text(nameRow, "Name", "", 32f, UIColors.Ink, font, TextAlignmentOptions.Left, true), 22f));
        clip.floorText = Text(header, "Floor", "", 19f, UIColors.InkSoft, font);

        RectTransform seats = Row(page, "Seats", 10f, TextAnchor.MiddleLeft, 36f);
        clip.seatsLabel = Text(seats, "Label", "", 19f, UIColors.Pencil, font);
        clip.seatsText = BlueprintUI.AutoSize(Text(seats, "Value", "0 / 4", 26f, UIColors.Ink, font, TextAlignmentOptions.Left, true), 17f);
        RectTransform pipsRect = BlueprintUI.NewRect("SeatPips", seats);
        pipsRect.pivot = new Vector2(0f, 1f);   // drawn from its top-left, whatever size the layout gives it
        BlueprintUI.Layout(pipsRect, 150f, 18f, 0f, 0f, 150f, 18f);
        clip.seatPips = pipsRect.gameObject.AddComponent<BlueprintGraphic>();
        clip.seatPips.raycastTarget = false;
        clip.seatsRow = seats;

        // The room's level: "Level 2 of 4 · upkeep −$80/day" and the upgrade (a purchase slip; grey but clickable when short)
        RectTransform level = Column(page, "Level", 4f);
        clip.levelText = BlueprintUI.AutoSize(Text(level, "Text", "", 19f, UIColors.InkSoft, font), 14f);
        clip.levelButton = Button(level, "Upgrade", "", TowerIconKind.Upgrade, font, 44f, 19f, UIColors.Highlight, UIColors.Ink, true, 3f,
                                  UIColors.Ink, new Vector2(4f, 5f), 14, 14, out clip.levelButtonText, 22f);
        BlueprintUI.AutoSize(clip.levelButtonText, 14f);
        clip.levelBlock = level;

        clip.descriptionText = BlueprintUI.Wrap(Text(page, "Description", "", 19f, UIColors.Ink, font));

        RectTransform furniture = Column(page, "Furniture", 0f);
        RectTransform head = Row(furniture, "Head", 12f, TextAnchor.MiddleLeft, 28f);
        clip.furnitureHeader = Flex(BlueprintUI.AutoSize(BlueprintUI.Caps(Text(head, "Label", "", 17f, UIColors.Pencil, font)), 13f));
        Fixed(Text(head, "PerDay", "", 17f, UIColors.Pencil, font, TextAlignmentOptions.Right), "tower.card.per_day");
        clip.slotRows = Column(furniture, "Rows", 0f);
        clip.rowTemplate = CardRow(furniture, font);
        BlueprintBox total = BlueprintUI.Box(furniture, "Total", Color.clear, UIColors.Ink, 0f, BlueprintUI.Round(0f));
        BlueprintUI.HRow(total, 12f, new RectOffset(0, 0, 4, 0), TextAnchor.MiddleLeft);
        BlueprintUI.Layout(total, -1f, 44f, -1f, -1f, -1f, 44f);
        RectTransform dash = BlueprintUI.NewRect("Dash", total.transform);
        BlueprintUI.Layout(dash).ignoreLayout = true;
        dash.anchorMin = new Vector2(0f, 1f);
        dash.anchorMax = new Vector2(1f, 1f);
        dash.pivot = new Vector2(0.5f, 1f);
        dash.sizeDelta = new Vector2(0f, 2f);
        BlueprintBox dashLine = dash.gameObject.AddComponent<BlueprintBox>();
        dashLine.SetStyle(Color.clear, UIColors.Ink, 2f);
        dashLine.Radii = BlueprintUI.Round(0f);
        dashLine.SetDash(true, 6f, 4f);
        dashLine.raycastTarget = false;
        Fixed(Flex(Text(total.transform, "Label", "", 19f, UIColors.Ink, font)), "tower.card.furniture_total");
        clip.furnitureTotal = Text(total.transform, "Value", "", 22f, UIColors.Money, font, TextAlignmentOptions.Right, true);

        RectTransform actions = Column(page, "Actions", 12f);
        clip.openButton = Button(actions, "OpenRoom", "", TowerIconKind.None, font, 68f, 28f, UIColors.Highlight, UIColors.Ink, true, 3f,
                                 UIColors.Ink, new Vector2(5f, 6f), 28, 28, out _, textKey: "tower.card.open_room");
        Icon(clip.openButton.transform, TowerIconKind.ArrowRight, 28f, UIColors.Ink);
        clip.moveButton = Button(actions, "Move", "", TowerIconKind.Move, font, 54f, 22f, UIColors.PaperHi, UIColors.Ink, false, 3f,
                                 UIColors.Ink, new Vector2(4f, 5f), 22, 22, out _, 24f, textKey: "tower.card.move");
        clip.sellButton = Button(actions, "Sell", "", TowerIconKind.Tag, font, 54f, 22f, UIColors.PaperHi, UIColors.Loss, false, 3f,
                                 UIColors.Ink, new Vector2(4f, 5f), 22, 22, out clip.sellText, 24f);
        clip.fixedNote = Fixed(BlueprintUI.Wrap(Text(actions, "Fixed", "", 19f, UIColors.InkSoft, font, TextAlignmentOptions.Center)), "tower.card.fixed");
    }

    static TowerSlotRow CardRow(Transform parent, TMP_FontAsset font)
    {
        RectTransform rect = Row(parent, "RowTemplate", 10f, TextAnchor.MiddleLeft, 34f);
        TowerSlotRow row = rect.gameObject.AddComponent<TowerSlotRow>();
        row.nameText = Flex(BlueprintUI.AutoSize(Text(rect, "Name", "", 19f, UIColors.Ink, font), 15f));   // TowerSlotRow.Bind writes the row
        RectTransform level = Column(rect, "Level", 4f);
        BlueprintUI.Layout(level, 92f, 34f, 0f, 0f, 92f, 34f);
        row.levelText = BlueprintUI.AutoSize(Text(level, "Text", "", 17f, UIColors.InkSoft, font), 12f);
        BlueprintBox bar = BlueprintUI.Box(level, "Bar", UIColors.PaperHi, UIColors.Ink, 2f, BlueprintUI.Round(3f));
        BlueprintUI.Layout(bar, 92f, 8f, 0f, 0f, 92f, 8f);
        BlueprintBox fill = BlueprintUI.Box(bar.transform, "Fill", UIColors.Highlight, Color.clear, 0f, BlueprintUI.Round(1f));
        RectTransform fillRect = fill.rectTransform;
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = new Vector2(0f, 1f);
        fillRect.offsetMin = new Vector2(2f, 2f);
        fillRect.offsetMax = new Vector2(-2f, -2f);
        row.barFill = fillRect;
        row.perDayText = Text(rect, "PerDay", "+$0", 19f, UIColors.Money, font, TextAlignmentOptions.Right);
        BlueprintUI.Layout(row.perDayText, 68f, -1f, 0f, -1f, 68f);
        rect.gameObject.SetActive(false);
        return row;
    }

    static void BuildSlotPage(TowerClipboard clip, RectTransform paper, TMP_FontAsset font)
    {
        RectTransform page = BlueprintUI.Stretch(BlueprintUI.NewRect("SlotPage", paper));
        BlueprintUI.VColumn(page, 10f, new RectOffset(20, 20, 34, 16));
        clip.slotPage = page;

        // TowerView writes the card (the room line already in capitals)
        clip.slotRoomText = BlueprintUI.AutoSize(BlueprintUI.Caps(Text(page, "Room", "", 19f, UIColors.Pencil, font)), 14f);
        clip.slotRoomText.margin = new Vector4(0f, 0f, 56f, 0f);
        RectTransform header = Row(page, "Header", 14f, TextAnchor.MiddleLeft, 60f);
        clip.slotSwatch = BlueprintUI.Box(header, "Swatch", UIColors.StickyBlue, UIColors.Ink, 2f, TowerGeo.RoomRadii);
        BlueprintUI.Layout(clip.slotSwatch, 60f, 60f, 0f, 0f, 60f, 60f);
        clip.slotIcon = Icon(clip.slotSwatch.transform, TowerIconKind.Coffee, 36f, UIColors.Ink);
        BlueprintUI.Layout(clip.slotIcon).ignoreLayout = true;
        BlueprintUI.Centered(clip.slotIcon.rectTransform, 36f, 36f);
        RectTransform names = Column(header, "Names", 2f, TextAnchor.MiddleLeft);
        BlueprintUI.Layout(names, 0f, -1f, 1f, -1f, 0f);
        clip.slotName = BlueprintUI.AutoSize(Text(names, "Name", "", 28f, UIColors.Ink, font, TextAlignmentOptions.Left, true), 18f);
        clip.slotLevel = BlueprintUI.AutoSize(Text(names, "Level", "", 19f, UIColors.InkSoft, font), 14f);

        // Level ladder: LEVEL / PRICE, one row of 33 px per level
        RectTransform ladder = Column(page, "Ladder", 4f);
        RectTransform ladderHead = Row(ladder, "Head", 0f, TextAnchor.MiddleLeft, 26f);
        Fixed(Flex(BlueprintUI.Caps(Text(ladderHead, "Level", "", 19f, UIColors.Pencil, font))), "tower.slot_card.level");   // "LEVEL"
        Fixed(BlueprintUI.Caps(Text(ladderHead, "Price", "", 19f, UIColors.Pencil, font, TextAlignmentOptions.Right)), "tower.slot_card.price");
        RectTransform rows = BlueprintUI.NewRect("Rows", ladder);
        BlueprintUI.Layout(rows, -1f, 198f, -1f, -1f, -1f, 198f);
        RectTransform lineRect = BlueprintUI.NewRect("Spine", rows);
        BlueprintUI.TopLeft(lineRect, 13f, 0f, 3f, 198f);
        clip.ladderLine = lineRect.gameObject.AddComponent<BlueprintGraphic>();
        clip.ladderLine.raycastTarget = false;
        clip.ladder = new TowerLadderRow[6];
        for (int i = 0; i < 6; i++) clip.ladder[i] = LadderRow(rows, i, font);

        // What the next level gives
        BlueprintBox next = BlueprintUI.Box(page, "Next", UIColors.MoneySoft, UIColors.Ink, 2f, BlueprintUI.RadiusLarge);
        next.SetShadow(BlueprintUI.InkShadow, new Vector2(3f, 4f));
        BlueprintUI.HRow(next, 12f, new RectOffset(16, 16, 10, 10), TextAnchor.MiddleLeft);
        RectTransform nextTexts = Column(next.transform, "Texts", 2f);
        BlueprintUI.Layout(nextTexts, 0f, -1f, 1f, -1f, 0f);
        clip.nextLabel = BlueprintUI.AutoSize(Text(nextTexts, "Label", "", 19f, UIColors.InkSoft, font), 14f);
        clip.nextValue = Text(nextTexts, "Value", "", 32f, UIColors.Money, font, TextAlignmentOptions.Left, true);
        Icon(next.transform, TowerIconKind.TrendUp, 44f, UIColors.Money);

        RectTransform upgrade = Column(page, "Upgrade", 6f, TextAnchor.UpperCenter);
        clip.upgradeButton = Button(upgrade, "Upgrade", "", TowerIconKind.None, font, 68f, 28f, UIColors.Highlight, UIColors.Ink, true, 3f,
                                    UIColors.Ink, new Vector2(5f, 6f), 16, 16, out clip.upgradeText);
        clip.upgradeNote = BlueprintUI.AutoSize(Text(upgrade, "Note", "", 19f, UIColors.InkSoft, font, TextAlignmentOptions.Center), 14f);

        // "This room" on green-bar ledger paper
        RectTransform room = Column(page, "ThisRoom", 6f);
        Fixed(BlueprintUI.Caps(Text(room, "Label", "", 19f, UIColors.Pencil, font)), "tower.slot_card.this_room");   // "THIS ROOM"
        BlueprintBox ledger = BlueprintUI.Box(room, "Ledger", UIColors.Ledger, UIColors.Ink, 2f, BlueprintUI.RadiusSmall);
        BlueprintUI.VColumn(ledger, 0f, new RectOffset(14, 14, 0, 0));
        RectTransform first = LedgerRow(ledger.transform, TowerIconKind.People, "Seats", font, UIColors.Ledger, out clip.ledgerFirstIcon, out clip.ledgerFirstLabel, out clip.ledgerFirstValue);
        first.name = "Seats";
        LedgerRow(ledger.transform, TowerIconKind.Sofa, "Furniture", font, UIColors.LedgerBand, out _, out TextMeshProUGUI furnitureLabel, out clip.ledgerFurniture);
        Fixed(furnitureLabel, "tower.slot_card.ledger.furniture");
        clip.ledgerFurniture.color = UIColors.Money;

        BlueprintUI.Spacer(page, 0f, 1f);
        clip.backButton = Button(page, "BackToTower", "", TowerIconKind.ArrowLeft, font, 54f, 22f, UIColors.PaperHi, UIColors.Ink, false, 3f,
                                 UIColors.Ink, new Vector2(4f, 5f), 20, 12, out _, 24f, "Esc", "tower.slot_card.back");
        page.gameObject.SetActive(false);
    }

    static TowerLadderRow LadderRow(RectTransform rows, int index, TMP_FontAsset font)
    {
        var row = new TowerLadderRow();
        RectTransform rect = BlueprintUI.NewRect("Level " + index, rows);
        BlueprintUI.TopLeft(rect, 0f, index * 33f, 380f - 40f, 33f);
        row.row = rect;
        row.marker = BlueprintUI.Box(rect, "Marker", UIColors.PaperHi, UIColors.Ink, 3f, BlueprintUI.Round(11f));
        row.marker.rectTransform.anchorMin = row.marker.rectTransform.anchorMax = new Vector2(0f, 0.5f);
        row.marker.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        row.marker.rectTransform.anchoredPosition = new Vector2(14.5f, 0f);
        row.marker.rectTransform.sizeDelta = new Vector2(22f, 22f);
        row.check = Icon(row.marker.transform, TowerIconKind.Check, 14f, UIColors.PaperHi, 3.6f);
        BlueprintUI.Layout(row.check).ignoreLayout = true;
        BlueprintUI.Centered(row.check.rectTransform, 14f, 14f);

        BlueprintBox highlight = BlueprintUI.Box(rect, "Mark", UIColors.WithAlpha(UIColors.Highlight, 0.95f), Color.clear, 0f, BlueprintUI.Round(2f));
        BlueprintUI.TopLeft(highlight.rectTransform, 36f, 17f, 110f, 12f);
        row.highlight = highlight.rectTransform;
        row.nameText = BlueprintUI.AutoSize(Placed(Text(rect, "Name", "", 22f, UIColors.Ink, font), 43f, 0f, 124f, 33f), 15f);   // TowerView writes it
        row.nameText.verticalAlignment = VerticalAlignmentOptions.Middle;
        BlueprintBox now = BlueprintUI.Box(rect, "Now", UIColors.Highlight, UIColors.Ink, 2f, BlueprintUI.Round(13f));
        BlueprintUI.TopLeft(now.rectTransform, 170f, 3.5f, 58f, 26f);
        TextMeshProUGUI nowText = Fixed(BlueprintUI.AutoSize(Text(now.transform, "Text", "", 17f, UIColors.Ink, font, TextAlignmentOptions.Center, true), 11f),
                                        "tower.slot_card.now");   // "Now"
        BlueprintUI.Stretch(nowText.rectTransform);
        row.nowPill = now.rectTransform;
        row.priceText = Text(rect, "Price", "$0", 19f, UIColors.InkSoft, font, TextAlignmentOptions.Right);
        BlueprintUI.TopRight(row.priceText.rectTransform, 0f, 0f, 120f, 33f);
        row.priceText.verticalAlignment = VerticalAlignmentOptions.Middle;
        return row;
    }

    static RectTransform LedgerRow(Transform parent, TowerIconKind icon, string label, TMP_FontAsset font, Color band, out TowerIcon iconOut,
                                   out TextMeshProUGUI labelText, out TextMeshProUGUI value)
    {
        BlueprintBox row = BlueprintUI.Box(parent, label, band, Color.clear, 0f, BlueprintUI.Round(0f));
        BlueprintUI.HRow(row, 10f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft);
        BlueprintUI.Layout(row, -1f, 34f, -1f, -1f, -1f, 34f);
        iconOut = Icon(row.transform, icon, 24f, UIColors.Ink);
        labelText = Flex(Text(row.transform, "Label", label, 19f, UIColors.InkSoft, font));
        value = Text(row.transform, "Value", "", 19f, UIColors.Ink, font, TextAlignmentOptions.Right, true);
        return row.rectTransform;
    }

    // ------------------------------------------------------------------
    // Room view: inside a room (TowerRoom board)
    // ------------------------------------------------------------------

    static void BuildRoomView(TowerView view, RectTransform root, TMP_FontAsset font)
    {
        TowerRoomView room = view.roomView;
        RectTransform panel = BlueprintUI.Stretch(BlueprintUI.NewRect("RoomView", root));
        room.panel = panel;
        Image scrim = BlueprintUI.Stretch(BlueprintUI.NewRect("Scrim", panel), 0f, 84f, 0f, 0f).gameObject.AddComponent<Image>();
        scrim.color = UIColors.ScrimLight;
        scrim.raycastTarget = true;

        // Title in white ink: the floor number in a ring, the name and where it is
        RectTransform title = BlueprintUI.NewRect("Title", panel);
        BlueprintUI.TopLeft(title, 180f, 112f, 1200f, 80f);
        BlueprintBox ring = BlueprintUI.Box(title, "Floor", Color.clear, UIColors.BlueprintInk, 3f, BlueprintUI.Round(28f));
        BlueprintUI.TopLeft(ring.rectTransform, 0f, 12f, 56f, 56f);
        room.floorNumber = Text(ring.transform, "Number", "1", 26f, UIColors.BlueprintInk, font, TextAlignmentOptions.Center, true);
        BlueprintUI.Stretch(room.floorNumber.rectTransform);
        room.titleText = Placed(Text(title, "Name", "", 44f, UIColors.BlueprintInk, font, TextAlignmentOptions.Left, true), 72f, 6f, 1100f, 50f);
        room.subtitleText = Placed(Text(title, "Where", "", 19f, UIColors.BlueprintInk, font), 72f, 54f, 1100f, 26f);

        // The shell: hatched ceiling and floor slabs, the outer wall and the partition
        RectTransform shellRect = BlueprintUI.NewRect("Shell", panel);
        shellRect.anchorMin = shellRect.anchorMax = new Vector2(0f, 1f);
        shellRect.pivot = new Vector2(0f, 1f);
        shellRect.sizeDelta = Vector2.zero;
        BlueprintGraphic shell = shellRect.gameObject.AddComponent<BlueprintGraphic>();
        shell.raycastTarget = false;
        BlueprintMeshBuilder m = shell.Mesh;
        m.Clear();
        m.SetYDownFrame(Vector2.zero);
        Color32 chalk = UIColors.BlueprintInk, hatch = UIColors.WithAlpha(UIColors.BlueprintInk, 0.75f);
        foreach (float y in new[] { 204f, 648f })
        {
            m.Line(new Vector2(168f, y + 1f), new Vector2(1400f, y + 1f), 2f, chalk);
            m.Line(new Vector2(168f, y + 19f), new Vector2(1400f, y + 19f), 2f, chalk);
            for (float x = 168f; x < 1400f; x += 10f)
                m.Line(new Vector2(x, y + 18f), new Vector2(Mathf.Min(x + 16f, 1400f), y + 2f), 2f, hatch);
        }
        m.FillRect(new Rect(168f, 204f, 12f, 464f), chalk);
        m.FillRect(new Rect(1380f, 204f, 6f, 464f), chalk);
        shell.Commit();

        // The room: paper box with the back wall and furniture drawn inside, and the hotspots on top
        BlueprintBox paper = BlueprintUI.Box(panel, "Room", UIColors.StickyBlue, UIColors.Ink, 3f, new Vector4(6f, 9f, 6f, 9f));
        BlueprintUI.TopLeft(paper.rectTransform, 180f, 224f, 1200f, 424f);
        room.paper = paper;
        RectTransform artRect = BlueprintUI.NewRect("Art", paper.transform);
        BlueprintUI.TopLeft(artRect, 3f, 3f, 1194f, 418f);
        room.art = artRect.gameObject.AddComponent<BlueprintGraphic>();
        room.art.raycastTarget = false;
        RectTransform spots = BlueprintUI.NewRect("Spots", paper.transform);
        BlueprintUI.TopLeft(spots, 3f, 3f, 1194f, 418f);
        room.spotLayer = spots;
        room.spotTemplate = SpotTemplate(paper.transform, font);

        // Key hints under the room, in white ink
        RectTransform hints = Row(panel, "Hints", 14f, TextAnchor.MiddleCenter);
        BlueprintUI.TopLeft(hints, 460f, 772f, 640f, 40f);
        HintPair(hints, "Click", "tower.room_view.hint.pick", font, "tower.key.click");
        Text(hints, "Dot", "·", 19f, UIColors.BlueprintInk, font);
        HintPair(hints, "E", "tower.room_view.hint.upgrade", font);
        Text(hints, "Dot", "·", 19f, UIColors.BlueprintInk, font);
        HintPair(hints, "Esc", "tower.room_view.hint.back", font);

        panel.gameObject.SetActive(false);
    }

    /// <summary>A key cap and what it does (String Table entries; a key cap with a word, like Click, has its own entry).</summary>
    static void HintPair(Transform parent, string key, string actionKey, TMP_FontAsset font, string keycapKey = null)
    {
        RectTransform pair = Row(parent, "Hint " + key, 8f);
        if (keycapKey != null) WordKeycap(pair, key, keycapKey, font);
        else BlueprintUI.Keycap(pair, key, font);
        Fixed(Text(pair, "Action", "", 19f, UIColors.BlueprintInk, font), actionKey);
    }

    static TowerSlotSpot SpotTemplate(Transform parent, TMP_FontAsset font)
    {
        BlueprintBox ring = BlueprintUI.Box(parent, "SpotTemplate", Color.clear, UIColors.WithAlpha(UIColors.Ink, 0.4f), 2f, BlueprintUI.RadiusSmall, true);
        ring.SetDash(true, 7f, 5f);
        TowerSlotSpot spot = ring.gameObject.AddComponent<TowerSlotSpot>();
        spot.ring = ring;
        spot.button = BlueprintUI.MakeButton(ring);

        BlueprintBox tag = BlueprintUI.Box(ring.transform, "Tag", UIColors.PaperHi, UIColors.Ink, 2f, BlueprintUI.RadiusSmall);
        tag.SetShadow(BlueprintUI.InkShadow, new Vector2(3f, 4f));
        RectTransform tagRect = tag.rectTransform;
        tagRect.anchorMin = tagRect.anchorMax = new Vector2(0f, 1f);
        tagRect.pivot = new Vector2(0f, 1f);
        tagRect.sizeDelta = new Vector2(120f, 60f);
        BlueprintUI.VColumn(tag, 4f, new RectOffset(10, 10, 6, 6), TextAnchor.UpperCenter).childForceExpandWidth = false;
        spot.nameText = Text(tag.transform, "Name", "", 19f, UIColors.Ink, font, TextAlignmentOptions.Center);   // TowerSlotSpot.Bind writes it
        RectTransform pips = Row(tag.transform, "Pips", 4f, TextAnchor.MiddleCenter);
        spot.pips = Pips(pips, 14f);
        spot.tagRect = tagRect;
        spot.tagBox = tag;
        ring.gameObject.SetActive(false);
        return spot;
    }

    // ------------------------------------------------------------------
    // Upgrade sheet (TowerFurniture board)
    // ------------------------------------------------------------------

    static void BuildSheet(TowerView view, RectTransform root, TMP_FontAsset font)
    {
        TowerUpgradeSheet sheet = view.sheet;
        RectTransform layer = BlueprintUI.Stretch(BlueprintUI.NewRect("UpgradeSheet", root));
        Image scrim = layer.gameObject.AddComponent<Image>();
        scrim.color = UIColors.Scrim;
        scrim.raycastTarget = true;
        sheet.layer = layer;

        BlueprintBox paper = BlueprintUI.Box(layer, "Sheet", UIColors.Paper, UIColors.Ink, 3f, BlueprintUI.RadiusCard, true);
        paper.SetShadow(SoftShadow, new Vector2(0f, 22f), 48f);
        RectTransform sheetRect = BlueprintUI.Centered(paper.rectTransform, 1440f, 700f);

        sheet.closeButton = RoundClose(sheetRect, 60f, 28f);
        BlueprintUI.TopRight((RectTransform)sheet.closeButton.transform, 24f, 24f, 60f, 60f);

        // Header: icon box, the question, levels and income, the cost chips
        sheet.iconBox = BlueprintUI.Box(sheetRect, "IconBox", UIColors.StickyBlue, UIColors.Ink, 3f, BlueprintUI.RadiusLarge);
        BlueprintUI.TopLeft(sheet.iconBox.rectTransform, 40f, 36f, 76f, 76f);
        sheet.icon = Icon(sheet.iconBox.transform, TowerIconKind.Coffee, 44f, UIColors.Ink);
        BlueprintUI.Layout(sheet.icon).ignoreLayout = true;
        BlueprintUI.Centered(sheet.icon.rectTransform, 44f, 44f);
        // TowerView writes the sheet (the fixed words keep to the String Table themselves)
        sheet.titleText = BlueprintUI.AutoSize(Placed(Text(sheetRect, "Title", "", 44f, UIColors.Ink, font, TextAlignmentOptions.Left, true), 138f, 30f, 1150f, 56f), 30f);
        sheet.levelsText = BlueprintUI.AutoSize(Placed(Text(sheetRect, "Levels", "", 26f, UIColors.Ink, font), 138f, 92f, 1180f, 36f), 18f);
        RectTransform chips = Row(sheetRect, "Chips", 12f);
        BlueprintUI.TopLeft(chips, 138f, 138f, 1180f, 44f);
        sheet.costText = Chip(chips, "Cost", UIColors.Highlight, TowerIconKind.Coin, font);
        sheet.budgetText = Chip(chips, "Budget", UIColors.PaperHi, TowerIconKind.None, font);

        // Three ways to pay (430 x 330 each, 32 apart)
        const float cardsTop = 214f, cardHeight = 330f;
        BlueprintBox pay = OptionCard(sheetRect, "Pay", 40f, cardsTop, UIColors.PaperHi, false);
        OptionTitle(pay.transform, TowerIconKind.Cash, "tower.sheet.pay.title", UIColors.Ink, font);
        Fixed(BlueprintUI.AutoSize(BlueprintUI.Wrap(Placed(Text(pay.transform, "Body", "", 22f, UIColors.Ink, font), 30f, 92f, 370f, 64f)), 16f),
              "tower.sheet.pay.body");
        BlueprintBox fullBar = BlueprintUI.Box(pay.transform, "Bar", UIColors.Money, UIColors.Ink, 3f, BlueprintUI.RadiusSmall);
        BlueprintUI.TopLeft(fullBar.rectTransform, 30f, 164f, 370f, 32f);
        sheet.payNote = BlueprintUI.AutoSize(Placed(Text(pay.transform, "Note", "", 19f, UIColors.Money, font), 30f, 202f, 370f, 26f), 14f);
        sheet.payButton = CardButton(pay.transform, "PayButton", "", TowerIconKind.Check, UIColors.Money, UIColors.PaperHi, UIColors.Ink, true,
                                     cardHeight, font, out sheet.payText);

        BlueprintBox felt = OptionCard(sheetRect, "Blackjack", 40f + 430f + 32f, cardsTop, UIColors.Felt, true);
        felt.SetGradient(UIColors.FeltHi, new Vector2(0.5f, 0.35f));
        BlueprintBox gold = BlueprintUI.Box(felt.transform, "GoldLine", Color.clear, UIColors.WithAlpha(UIColors.Gold, 0.8f), 2f, new Vector4(10f, 16f, 9f, 18f));
        BlueprintUI.Stretch(gold.rectTransform, 8f, 8f, 8f, 8f);
        MiniCard(felt.transform, "A", UIColors.Ink, 332f, 20f, -8f, font);
        MiniCard(felt.transform, "K", UIColors.Loss, 366f, 24f, 7f, font);
        OptionTitle(felt.transform, TowerIconKind.Cards, "tower.sheet.blackjack.title", UIColors.Gold, font);
        sheet.blackjackBody = BlueprintUI.AutoSize(BlueprintUI.Wrap(Placed(Text(felt.transform, "Body", "", 22f, UIColors.PaperHi, font), 30f, 88f, 370f, 100f)), 16f);
        sheet.blackjackBody.lineSpacing = -6f;
        sheet.blackjackOdds = BlueprintUI.AutoSize(Placed(Text(felt.transform, "Odds", "", 26f, UIColors.PaperHi, font), 30f, 196f, 370f, 34f), 18f);
        sheet.blackjackButton = CardButton(felt.transform, "DealButton", "", TowerIconKind.Cards, UIColors.Gold, UIColors.Ink, BlueprintUI.FeltShadow, true,
                                           cardHeight, font, out sheet.blackjackText);

        BlueprintBox roll = OptionCard(sheetRect, "Roll", 40f + (430f + 32f) * 2f, cardsTop, UIColors.PaperHi, false);
        OptionTitle(roll.transform, TowerIconKind.Dice, "tower.sheet.roll.title", UIColors.Ink, font);
        RectTransform barRect = BlueprintUI.NewRect("OddsBar", roll.transform);
        BlueprintUI.TopLeft(barRect, 30f, 92f, 370f, 32f);
        sheet.rollBar = barRect.gameObject.AddComponent<BlueprintGraphic>();
        sheet.rollBar.raycastTarget = false;
        sheet.rollLabels = BlueprintUI.AutoSize(Placed(Text(roll.transform, "Labels", "", 19f, UIColors.Ink, font), 30f, 132f, 370f, 26f), 14f);
        sheet.rollNote = BlueprintUI.AutoSize(Placed(Text(roll.transform, "Note", "", 19f, UIColors.InkSoft, font), 30f, 164f, 370f, 26f), 14f);
        BlueprintBox rollButtonBox = BlueprintUI.Box(roll.transform, "RollButton", UIColors.PaperHi, UIColors.Ink, 3f, BlueprintUI.RadiusLarge, true);
        rollButtonBox.SetShadow(UIColors.Ink, new Vector2(4f, 5f));
        BlueprintUI.TopLeft(rollButtonBox.rectTransform, 30f, cardHeight - 30f - 96f, 370f, 90f);
        BlueprintUI.VColumn(rollButtonBox, 2f, new RectOffset(12, 12, 8, 8), TextAnchor.MiddleCenter).childForceExpandWidth = true;
        RectTransform rollLine = Row(rollButtonBox.transform, "Line", 12f, TextAnchor.MiddleCenter, 40f);
        Icon(rollLine, TowerIconKind.Dice, 28f, UIColors.Ink);
        Fixed(Text(rollLine, "Label", "", 28f, UIColors.Ink, font), "tower.sheet.roll.button");   // "Roll for it"
        sheet.rollButtonOdds = BlueprintUI.AutoSize(Text(rollButtonBox.transform, "Odds", "", 17f, UIColors.InkSoft, font, TextAlignmentOptions.Center), 13f);
        sheet.rollButton = BlueprintUI.MakeButton(rollButtonBox);

        // Footer: Esc and the honest line, Cancel
        RectTransform footer = Row(sheetRect, "Footer", 10f);
        BlueprintUI.TopLeft(footer, 40f, 700f - 36f - 68f, 1100f, 68f);
        BlueprintUI.Keycap(footer, "Esc", font);
        sheet.footerText = BlueprintUI.AutoSize(Text(footer, "Text", "", 19f, UIColors.Pencil, font), 14f);
        sheet.fillLink = Link(footer, "FillBudget", "tower.button.fill_budget", TowerIconKind.Dice, 22f, UIColors.Ink, font);
        sheet.cancelButton = Button(sheetRect, "Cancel", "", TowerIconKind.None, font, 68f, 28f, UIColors.PaperHi, UIColors.Ink, false, 3f,
                                    UIColors.Ink, new Vector2(4f, 5f), 36, 36, out _, textKey: "tower.button.cancel");
        BlueprintUI.Layout(sheet.cancelButton).ignoreLayout = true;
        BlueprintUI.BottomRight((RectTransform)sheet.cancelButton.transform, 40f, 36f, 180f, 68f);

        BuildRolling(sheet, sheetRect, font);
        BuildResult(sheet, sheetRect, font);
        layer.gameObject.SetActive(false);
    }

    static TextMeshProUGUI Chip(Transform parent, string name, Color fill, TowerIconKind icon, TMP_FontAsset font)
    {
        BlueprintBox chip = BlueprintUI.Box(parent, name, fill, UIColors.Ink, 2f, BlueprintUI.RadiusSmall);
        BlueprintUI.HRow(chip, 8f, new RectOffset(14, 14, 6, 6), TextAnchor.MiddleLeft);
        BlueprintUI.Layout(chip, -1f, 44f, -1f, -1f, -1f, 44f);
        if (icon != TowerIconKind.None) Icon(chip.transform, icon, 24f, UIColors.Ink);
        return Text(chip.transform, "Text", "", 22f, UIColors.Ink, font);
    }

    static BlueprintBox OptionCard(RectTransform sheet, string name, float x, float y, Color fill, bool felt)
    {
        BlueprintBox card = BlueprintUI.Box(sheet, name, fill, UIColors.Ink, 3f, BlueprintUI.RadiusLarge);
        card.SetShadow(BlueprintUI.InkShadow, new Vector2(3f, 4f));
        BlueprintUI.TopLeft(card.rectTransform, x, y, 430f, 330f);
        return card;
    }

    /// <summary>An option card's icon and title (the String Table entry <paramref name="key"/>; it shrinks to fit 370 px).</summary>
    static void OptionTitle(Transform card, TowerIconKind icon, string key, Color color, TMP_FontAsset font)
    {
        RectTransform row = Row(card, "Title", 12f);
        BlueprintUI.TopLeft(row, 30f, 26f, 370f, 40f);
        Icon(row, icon, 36f, color);
        Fixed(Flex(BlueprintUI.AutoSize(Text(row, "Text", "", 32f, color, font, TextAlignmentOptions.Left, true), 20f)), key);
    }

    static Button CardButton(Transform card, string name, string label, TowerIconKind icon, Color fill, Color textColor, Color shadow, bool bold,
                             float cardHeight, TMP_FontAsset font, out TextMeshProUGUI text)
    {
        Button button = Button(card, name, label, icon, font, 68f, 28f, fill, textColor, bold, 3f, shadow, new Vector2(5f, 6f), 36, 36, out text, 28f);
        BlueprintUI.Layout(button).ignoreLayout = true;
        RectTransform rect = (RectTransform)button.transform;
        BlueprintUI.TopLeft(rect, 30f, cardHeight - 30f - 68f, 260f, 68f);
        ContentSizeFitter fitter = button.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        return button;
    }

    static void MiniCard(Transform parent, string rank, Color ink, float x, float y, float degrees, TMP_FontAsset font)
    {
        BlueprintBox card = BlueprintUI.Box(parent, "Card " + rank, UIColors.PaperHi, UIColors.Ink, 2f, BlueprintUI.Round(6f));
        card.SetShadow(new Color32(11, 42, 27, 204), new Vector2(2f, 3f));
        BlueprintUI.TopLeft(card.rectTransform, x, y, 40f, 56f);
        card.rectTransform.localEulerAngles = new Vector3(0f, 0f, -degrees);
        Placed(Text(card.transform, "Rank", rank, 18f, ink, font, TextAlignmentOptions.Left, true), 5f, 2f, 30f, 22f);
    }

    static void BuildRolling(TowerUpgradeSheet sheet, RectTransform sheetRect, TMP_FontAsset font)
    {
        RectTransform layer = BlueprintUI.Stretch(BlueprintUI.NewRect("Rolling", sheetRect));
        BlueprintUI.AddBox(layer, new Color(0.063f, 0.051f, 0.031f, 0.45f), Color.clear, 0f, BlueprintUI.RadiusCard, true);
        BlueprintBox die = BlueprintUI.Box(layer, "Die", UIColors.PaperHi, UIColors.Ink, 4f, BlueprintUI.Round(28f));
        die.SetShadow(UIColors.Ink, new Vector2(6f, 8f));
        BlueprintUI.Centered(die.rectTransform, 180f, 180f, 0f, 20f);
        sheet.rollingFace = Text(die.transform, "Face", "+2", 72f, UIColors.Ink, font, TextAlignmentOptions.Center, true);
        BlueprintUI.Stretch(sheet.rollingFace.rectTransform);
        TextMeshProUGUI label = Fixed(Text(layer, "Label", "", 32f, UIColors.PaperHi, font, TextAlignmentOptions.Center, true), "tower.sheet.rolling");
        BlueprintUI.Centered(label.rectTransform, 400f, 40f, 0f, -110f);
        sheet.rollingLayer = layer;
        layer.gameObject.SetActive(false);
    }

    static void BuildResult(TowerUpgradeSheet sheet, RectTransform sheetRect, TMP_FontAsset font)
    {
        RectTransform layer = BlueprintUI.Stretch(BlueprintUI.NewRect("Result", sheetRect));
        BlueprintUI.AddBox(layer, new Color(0.063f, 0.051f, 0.031f, 0.5f), Color.clear, 0f, BlueprintUI.RadiusCard, true);
        BlueprintBox slip = BlueprintUI.Box(layer, "Slip", UIColors.PaperHi, UIColors.Ink, 3f, BlueprintUI.RadiusLarge, true);
        slip.SetShadow(SoftShadow, new Vector2(0f, 22f), 48f);
        RectTransform slipRect = BlueprintUI.Centered(slip.rectTransform, 760f, 420f);
        Tape(slip.transform, 150f, 380f, 0f, -3f);

        BlueprintBox stamp = BlueprintUI.Box(slip.transform, "Stamp", Color.clear, UIColors.Money, 5f, BlueprintUI.Round(10f));
        stamp.SetOutline(UIColors.Money, 2f, 4f);
        BlueprintUI.TopRight(stamp.rectTransform, 40f, 34f, 220f, 72f);
        stamp.rectTransform.localEulerAngles = new Vector3(0f, 0f, 10f);
        // TowerView writes the result (the stamps are capitals in the String Table)
        sheet.resultStamp = BlueprintUI.AutoSize(BlueprintUI.Caps(Text(stamp.transform, "Text", "", 44f, UIColors.Money, font, TextAlignmentOptions.Center, true), 0.12f), 24f);
        BlueprintUI.Stretch(sheet.resultStamp.rectTransform);
        sheet.resultStampBox = stamp;

        sheet.resultTitle = BlueprintUI.AutoSize(Placed(Text(slipRect, "Title", "", 44f, UIColors.Ink, font, TextAlignmentOptions.Left, true), 36f, 40f, 440f, 56f), 28f);
        sheet.resultLine = BlueprintUI.Wrap(Placed(Text(slipRect, "Line", "", 26f, UIColors.Ink, font), 36f, 130f, 690f, 120f));
        sheet.resultBudget = BlueprintUI.AutoSize(Placed(Text(slipRect, "Budget", "", 19f, UIColors.InkSoft, font), 36f, 262f, 690f, 28f), 14f);
        sheet.resultButton = Button(slipRect, "Ok", "", TowerIconKind.Check, font, 68f, 28f, UIColors.Highlight, UIColors.Ink, true, 3f,
                                    UIColors.Ink, new Vector2(5f, 6f), 40, 40, out _, 28f, textKey: "tower.sheet.ok");
        BlueprintUI.Layout(sheet.resultButton).ignoreLayout = true;
        BlueprintUI.TopLeft((RectTransform)sheet.resultButton.transform, 36f, 420f - 36f - 68f, 180f, 68f);
        sheet.resultLayer = layer;
        layer.gameObject.SetActive(false);
    }
}
