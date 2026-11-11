using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds the staff files screen at runtime from the design board "Staff files" (1920 x 1080; the manila filing drawer
/// 1640 x 900 at 140, 90) plus the open file (in the look of the CV board) and the fire confirmation, and fills
/// StaffFilesView's references. Only used when the view has no designed prefab (Root empty).
/// The fixed labels are L10nText components on the "Staff" String Table (they follow language changes by themselves);
/// capitals labels are written in capitals there. The view fills in the rest and gives every text the language's font.
/// </summary>
public static class StaffFilesBuilder
{
    const string T = StaffLedger.Table;

    const float FrameWidth = 1920f;
    const float FrameHeight = 1080f;
    const float PanelX = 140f, PanelY = 90f, PanelWidth = 1640f, PanelHeight = 900f, PanelBorder = 3f;
    const float RowWidth = 1536f, RowHeight = 80f;

    static readonly Vector4 RowRadii = new Vector4(6f, 20f, 12f, 22f);
    static readonly Vector4 TabRadii = new Vector4(10f, 14f, 0f, 0f);
    static readonly Vector4 WellRadii = new Vector4(14f, 20f, 12f, 22f);
    static readonly Vector4 SheetRadii = new Vector4(4f, 7f, 5f, 8f);
    static readonly Vector2 ButtonShadow = new Vector2(4f, 5f);
    static readonly Vector2 SmallShadow = new Vector2(3f, 4f);

    public static void Build(StaffFilesView view, RectTransform canvas, TMP_FontAsset font)
    {
        // Everything is built under an inactive root, so MarkerButton starts with its face in place; Open shows it
        RectTransform root = BlueprintUI.Stretch(BlueprintUI.NewRect("StaffFiles", canvas));
        root.gameObject.SetActive(false);
        view.root = root;
        StaffUI.Blocker(root, "Scrim", UIColors.Scrim);

        RectTransform frame = BlueprintUI.Centered(BlueprintUI.NewRect("Frame", root), FrameWidth, FrameHeight);
        view.fitFrame = frame;

        PausedChip(frame, font);
        BlueprintBox panel = StaffUI.Box(frame, "Drawer", UIColors.Manila, UIColors.Ink, PanelBorder, BlueprintUI.RadiusCard,
                                         PanelX, PanelY, PanelWidth, PanelHeight, true);
        panel.SetShadow(StaffUI.SheetShadow, new Vector2(0f, 22f), 40f);
        RectTransform inner = BlueprintUI.Stretch(BlueprintUI.NewRect("Inner", panel.transform), PanelBorder, PanelBorder, PanelBorder, PanelBorder);

        Header(view, inner, font);
        Totals(view, inner, font);
        List(view, inner, font);
        Footer(view, inner, font);
        SortMenu(view, frame, font);
        FileSheet(view, frame, font);
        Confirm(view, frame, font);
    }

    // ------------------------------------------------------------------
    // Drawer
    // ------------------------------------------------------------------

    static void PausedChip(Transform frame, TMP_FontAsset font)
    {
        BlueprintBox chip = StaffUI.Box(frame, "Paused", UIColors.PaperHi, UIColors.Ink, 2f, BlueprintUI.RadiusSmall, 846f, 28f, 228f, 52f);
        chip.SetShadow(BlueprintUI.InkShadow, SmallShadow);
        TextMeshProUGUI text = StaffUI.Line(chip.transform, "Text", "", 22f, UIColors.Ink, font, 0f, 26f, 180f);
        StaffUI.Shrink(text, 15f);
        L10nText.Attach(text, T, "staff.ui.paused");   // "Game paused"
        StaffIcon icon = StaffUI.Icon(chip.transform, StaffIconKind.Pause, 0f, 13f, 26f, UIColors.Ink);
        // Icon and words centred together, for whatever the words are in the current language
        StaffButtonFit fit = chip.gameObject.AddComponent<StaffButtonFit>();
        fit.label = text;
        fit.icon = icon.rectTransform;
        fit.iconSize = 26f;
        fit.fontSize = 22f;
        fit.margin = 12f;
        fit.Layout();
    }

    static void Header(StaffFilesView view, Transform inner, TMP_FontAsset font)
    {
        StaffUI.Icon(inner, StaffIconKind.People, 32f, 33f, 48f, UIColors.Ink);
        TextMeshProUGUI title = StaffUI.Line(inner, "Title", "", 60f, UIColors.Ink, font, 96f, 57f, 420f, TextAlignmentOptions.MidlineLeft, true);
        L10nText.Attach(title, T, "staff.title");   // "Staff files"
        float titleWidth = Mathf.Ceil(StaffUI.Measure(title, title.text));
        title.rectTransform.sizeDelta = new Vector2(titleWidth + 4f, title.rectTransform.sizeDelta.y);
        view.countText = StaffUI.Shrink(StaffUI.Line(inner, "Count", "", 26f, UIColors.InkSoft, font, 96f + titleWidth + 16f, 57f,
                                                     Mathf.Max(120f, 810f - (96f + titleWidth + 16f))), 18f);
        // The view places the count after the title again when the language (and so the title's width) changes
        view.runtimeTitle = title;
        view.runtimeTitleLeft = 96f;
        view.runtimeCountRight = 810f;

        TextMeshProUGUI sort = StaffUI.Shrink(StaffUI.Line(inner, "SortLabel", "", 19f, UIColors.InkSoft, font, 786f, 57f, 100f, TextAlignmentOptions.MidlineRight), 13f);
        L10nText.Attach(sort, T, "staff.header.sort");   // "Sort"
        view.sortButton = Select(inner, font, 896f, 27f, 280f, 60f, out view.sortLabel);
        view.searchField = Search(inner, font, 1196f, 27f, 330f, 60f);
        view.closeButton = StaffUI.RoundButton(inner, "Close", StaffIconKind.Close, 1550f, 27f, 60f, 28f, SmallShadow, out _);
    }

    static Button Select(Transform parent, TMP_FontAsset font, float x, float y, float width, float height, out TMP_Text label)
    {
        BlueprintBox box = StaffUI.Box(parent, "Sort", UIColors.PaperHi, UIColors.Ink, 3f, BlueprintUI.RadiusSmall, x, y, width, height, true);
        Button button = BlueprintUI.MakeButton(box);
        // The current order's name: the view writes it (in the current language)
        TextMeshProUGUI text = StaffUI.Line(box.transform, "Label", StaffFilesView.SortLabel(0), 24f, UIColors.Ink, font, 18f, height * 0.5f, width - 18f - 56f);
        label = StaffUI.Shrink(text, 16f);
        StaffUI.Icon(box.transform, StaffIconKind.ChevronDown, width - 18f - 28f, (height - 28f) * 0.5f, 28f, UIColors.Ink);
        return button;
    }

    static TMP_InputField Search(Transform parent, TMP_FontAsset font, float x, float y, float width, float height)
    {
        BlueprintBox box = StaffUI.Box(parent, "Search", UIColors.PaperHi, UIColors.Ink, 3f, BlueprintUI.RadiusSmall, x, y, width, height, true);
        StaffUI.Icon(box.transform, StaffIconKind.Search, 16f, (height - 28f) * 0.5f, 28f, UIColors.InkSoft);
        RectTransform area = BlueprintUI.NewRect("Text Area", box.transform);
        BlueprintUI.TopLeft(area, 56f, 4f, width - 56f - 16f, height - 8f);
        area.gameObject.AddComponent<RectMask2D>();
        TextMeshProUGUI placeholder = BlueprintUI.Text(area, "Placeholder", "", 24f, UIColors.Pencil, font, TextAlignmentOptions.MidlineLeft);
        BlueprintUI.Stretch(placeholder.rectTransform);
        StaffUI.Shrink(placeholder, 16f);
        L10nText.Attach(placeholder, T, "staff.header.search");   // "Search staff"
        TextMeshProUGUI text = BlueprintUI.Text(area, "Text", "", 24f, UIColors.Ink, font, TextAlignmentOptions.MidlineLeft);
        BlueprintUI.Stretch(text.rectTransform);

        TMP_InputField input = box.gameObject.AddComponent<TMP_InputField>();
        input.targetGraphic = box;
        input.colors = BlueprintUI.Tint();
        input.navigation = new Navigation { mode = Navigation.Mode.None };
        input.textViewport = area;
        input.textComponent = text;
        input.placeholder = placeholder;
        input.contentType = TMP_InputField.ContentType.Standard;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.characterLimit = 40;
        input.customCaretColor = true;
        input.caretColor = UIColors.Ink;
        input.caretWidth = 2;
        input.selectionColor = UIColors.WithAlpha(UIColors.Highlight, 0.6f);
        input.restoreOriginalTextOnEscape = false;
        if (font != null) input.fontAsset = font;
        return input;
    }

    static void Totals(StaffFilesView view, Transform inner, TMP_FontAsset font)
    {
        BlueprintBox strip = StaffUI.Ledger(inner, "Totals", 32f, 108f, 1570f, 74f);
        string[] labels =
        {
            "staff.totals.seats", "staff.totals.payroll", "staff.totals.output", "staff.totals.profit", "staff.totals.iq",
            "staff.totals.experience", "staff.totals.happiness", "staff.totals.trust"
        };
        var values = new TMP_Text[labels.Length];
        for (int i = 0; i < labels.Length; i++)
        {
            float x = 46f + i * 188f;
            TextMeshProUGUI label = StaffUI.Shrink(StaffUI.Line(strip.transform, "Label", "", 19f, UIColors.Pencil, font, x, 23f, 180f), 13f);
            L10nText.Attach(label, T, labels[i]);   // "Seats used", "Payroll / day", ...
            values[i] = StaffUI.Shrink(StaffUI.Line(strip.transform, "Value", "-", 22f, UIColors.Ink, font, x, 49f, 180f, TextAlignmentOptions.MidlineLeft, true), 15f);
        }
        view.seatsText = values[0];
        view.payrollText = values[1];
        view.outputText = values[2];
        view.profitText = values[3];
        view.avgIqText = values[4];
        view.avgExperienceText = values[5];
        view.happinessText = values[6];
        view.trustText = values[7];
    }

    static void List(StaffFilesView view, Transform inner, TMP_FontAsset font)
    {
        BlueprintBox well = StaffUI.Box(inner, "Drawer well", UIColors.ManilaEdge, UIColors.Ink, 3f, WellRadii, 32f, 196f, 1570f, 610f);
        // The well's inset shadow (CSS inset 0 8px 14px): two soft bands under the top edge
        StaffUI.Box(well.transform, "Shade", UIColors.Dark(0.12f), Color.clear, 0f, new Vector4(11f, 17f, 0f, 0f), 3f, 3f, 1564f, 9f);
        StaffUI.Box(well.transform, "Shade", UIColors.Dark(0.06f), Color.clear, 0f, Vector4.zero, 3f, 12f, 1564f, 9f);

        RectTransform viewport = BlueprintUI.NewRect("Viewport", well.transform);
        BlueprintUI.TopLeft(viewport, 3f, 3f, 1564f, 604f);
        Image catcher = viewport.gameObject.AddComponent<Image>();   // takes the wheel between files
        catcher.color = Color.clear;
        catcher.raycastTarget = true;
        viewport.gameObject.AddComponent<RectMask2D>();

        RectTransform content = BlueprintUI.NewRect("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = new Vector2(0f, 604f);

        ScrollRect scroll = well.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.inertia = true;
        scroll.decelerationRate = 0.135f;
        scroll.scrollSensitivity = view.wheelStep;
        scroll.verticalScrollbar = MakeScrollbar(well.transform, 1553f, 11f, 8f, 588f);
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        view.scrollRect = scroll;

        view.rowTemplate = Row(content, font);
        view.emptyText = StaffUI.Block(well.transform, "Empty", "", 26f, UIColors.InkSoft, font, 185f, 250f, 1200f, 120f, 1.3f, TextAlignmentOptions.Top);
        view.emptyText.gameObject.SetActive(false);
    }

    static Scrollbar MakeScrollbar(Transform parent, float x, float y, float width, float height)
    {
        BlueprintBox track = StaffUI.Box(parent, "Scrollbar", UIColors.Dark(0.12f), Color.clear, 0f, BlueprintUI.Round(width * 0.5f), x, y, width, height, true);
        RectTransform slide = BlueprintUI.Stretch(BlueprintUI.NewRect("Sliding Area", track.transform));
        BlueprintBox handle = BlueprintUI.Box(slide, "Handle", UIColors.WithAlpha(UIColors.InkSoft, 0.8f), Color.clear, 0f, BlueprintUI.Round(width * 0.5f), true);
        BlueprintUI.Stretch(handle.rectTransform);
        Scrollbar bar = track.gameObject.AddComponent<Scrollbar>();
        bar.SetValueWithoutNotify(1f);   // the top: a new bar's 0 would scroll the list to its end
        bar.handleRect = handle.rectTransform;
        bar.targetGraphic = handle;
        bar.direction = Scrollbar.Direction.BottomToTop;
        bar.colors = BlueprintUI.Tint();
        bar.navigation = new Navigation { mode = Navigation.Mode.None };
        return bar;
    }

    /// <summary>The hanging file (row template, 1536 x 80: a 14 px tab over a 66 px folder body).</summary>
    static StaffFileRow Row(Transform content, TMP_FontAsset font)
    {
        RectTransform rect = BlueprintUI.NewRect("File (template)", content);
        BlueprintUI.TopLeft(rect, 14f, 10f, RowWidth, RowHeight);
        rect.gameObject.SetActive(false);
        StaffFileRow row = rect.gameObject.AddComponent<StaffFileRow>();

        BlueprintBox body = StaffUI.Box(rect, "Folder", UIColors.Manila, UIColors.Ink, 2f, RowRadii, 0f, 14f, RowWidth, 66f, true);
        body.SetShadow(StaffUI.RowShadow, new Vector2(2f, 3f));
        row.openButton = BlueprintUI.MakeButton(body);
        // The tab sits on the folder: drawn after it, with a strip of its colour over the two borders where they meet
        // White fills: StaffFileRow colours the tab (manila, gold for golden hires) through the graphic's colour
        BlueprintBox tab = StaffUI.Box(rect, "Tab", Color.white, UIColors.Ink, 2f, TabRadii, 24f, 0f, 150f, 17f);
        BlueprintBox seam = StaffUI.Box(rect, "TabSeam", Color.white, Color.clear, 0f, Vector4.zero, 26f, 13f, 146f, 4f);
        tab.color = UIColors.Manila;
        seam.color = UIColors.Manila;
        row.tab = tab;
        row.tabSeam = seam;

        Transform b = body.transform;
        // Portrait: paper square, the photo inside, an ink rim on top
        BlueprintBox photoBox = StaffUI.Box(b, "Portrait", UIColors.PaperHi, Color.clear, 0f, BlueprintUI.Round(5f), 16f, 9f, 48f, 48f);
        RectTransform photoArea = BlueprintUI.Stretch(BlueprintUI.NewRect("Photo", photoBox.transform), 2f, 2f, 2f, 2f);
        Image photo = photoArea.gameObject.AddComponent<Image>();
        photo.preserveAspect = true;
        photo.raycastTarget = false;
        photo.enabled = false;
        row.portrait = photo;
        row.portraitPlaceholder = StaffUI.Icon(photoBox.transform, StaffIconKind.Person, 10f, 10f, 28f, UIColors.InkSoft, 2.2f).gameObject;
        StaffUI.Box(photoBox.transform, "Rim", Color.clear, UIColors.Ink, 2f, BlueprintUI.Round(5f), 0f, 0f, 48f, 48f);

        // Empty texts: every row is bound to a person before it shows
        row.nameText = StaffUI.Ellipsis(StaffUI.Line(b, "Name", "", 22f, UIColors.Ink, font, 82f, 23f, 216f));
        StaffIcon star = StaffUI.Icon(b, StaffIconKind.Star, 82f, 12f, 22f, UIColors.Ink, 2f);
        star.FillColor = UIColors.Gold;
        star.gameObject.SetActive(false);
        row.goldenStar = star.rectTransform;
        row.hiredText = StaffUI.Shrink(StaffUI.Line(b, "Hired", "", 17f, UIColors.InkSoft, font, 82f, 46f, 250f), 12f);

        row.iqText = StaffUI.Shrink(StaffUI.Line(b, "IQ", "", 22f, UIColors.Ink, font, 350f, 33f, 92f), 15f);

        StaffUI.Icon(b, StaffIconKind.Medal, 460f, 12f, 22f, UIColors.Ink);
        row.degreeText = StaffUI.Shrink(StaffUI.Line(b, "Degree", "", 19f, UIColors.Ink, font, 490f, 23f, 166f), 13f);
        row.experienceText = StaffUI.Shrink(StaffUI.Line(b, "Experience", "", 17f, UIColors.InkSoft, font, 460f, 46f, 196f), 12f);

        row.salaryText = StaffUI.Shrink(StaffUI.Line(b, "Salary", "", 22f, UIColors.Ink, font, 674f, 23f, 150f), 14f);
        row.salaryYearText = StaffUI.Shrink(StaffUI.Line(b, "SalaryYear", "", 17f, UIColors.InkSoft, font, 674f, 46f, 150f), 12f);

        row.profitText = StaffUI.Shrink(StaffUI.Line(b, "Profit", "", 22f, UIColors.Money, font, 842f, 23f, 180f, TextAlignmentOptions.MidlineLeft, true), 14f);
        row.lifetimeText = StaffUI.Shrink(StaffUI.Line(b, "Lifetime", "", 17f, UIColors.InkSoft, font, 842f, 46f, 180f), 12f);

        RectTransform tags = BlueprintUI.NewRect("Tags", b);
        BlueprintUI.TopLeft(tags, 1040f, 17f, 338f, 32f);
        row.tagChips = new Graphic[4];
        for (int i = 0; i < row.tagChips.Length; i++)
        {
            BlueprintBox chip = StaffUI.Box(tags, "Tag", Color.white, UIColors.Ink, 2f, BlueprintUI.RadiusSmall, 0f, 0f, 80f, 32f);   // coloured through .color
            TextMeshProUGUI label = BlueprintUI.Text(chip.transform, "Label", "", 18f, UIColors.Ink, font, TextAlignmentOptions.Midline);
            BlueprintUI.Stretch(label.rectTransform, 4f, -4f, 4f, -4f);
            chip.gameObject.SetActive(false);
            row.tagChips[i] = chip;
        }

        row.letGoButton = StaffUI.PaperButton(b, "LetGo", "", StaffIconKind.None, font, UIColors.Loss, UIColors.PaperHi,
                                              RowWidth - 16f - 124f, 6f, 124f, 54f, 22f, true, ButtonShadow, BlueprintUI.RadiusLarge, out TextMeshProUGUI letGo);
        L10nText.Attach(letGo, T, "staff.row.let_go");   // "Let go" (the rows are copies of this template: they carry it along)
        return row;
    }

    static void Footer(StaffFilesView view, Transform inner, TMP_FontAsset font)
    {
        view.backButton = StaffUI.PaperButton(inner, "Back", L10n.Get(T, "staff.button.back"), StaffIconKind.ArrowLeft, font, UIColors.PaperHi, UIColors.Ink,
                                              32f, 822f, 0f, 54f, 22f, false, ButtonShadow, BlueprintUI.RadiusLarge, out TextMeshProUGUI back);
        L10nText.Attach(back, T, "staff.button.back");   // "Back to the office" (the button grows with it: StaffButtonFit)
        view.prevPageButton = StaffUI.RoundButton(inner, "PrevPage", StaffIconKind.ArrowLeft, 1300f, 823f, 52f, 26f, SmallShadow, out _);
        view.pageText = StaffUI.Shrink(StaffUI.Line(inner, "Page", "", 22f, UIColors.Ink, font, 1370f, 849f, 162f, TextAlignmentOptions.Midline), 15f);
        view.nextPageButton = StaffUI.RoundButton(inner, "NextPage", StaffIconKind.ArrowRight, 1550f, 823f, 52f, 26f, SmallShadow, out _);
    }

    static void SortMenu(StaffFilesView view, Transform frame, TMP_FontAsset font)
    {
        RectTransform menu = BlueprintUI.Stretch(BlueprintUI.NewRect("SortMenu", frame));
        Image away = StaffUI.Blocker(menu, "ClickAway", Color.clear);
        Button blocker = away.gameObject.AddComponent<Button>();
        blocker.transition = Selectable.Transition.None;
        blocker.navigation = new Navigation { mode = Navigation.Mode.None };
        view.sortMenuBlocker = blocker;

        // Right under the select (panel inner 896, 27, 280 x 60)
        float x = PanelX + PanelBorder + 896f, y = PanelY + PanelBorder + 27f + 60f + 6f;
        const float optionHeight = 48f;
        int count = StaffFilesView.SortKeys.Length;
        BlueprintBox list = StaffUI.Box(menu, "Options", UIColors.PaperHi, UIColors.Ink, 3f, BlueprintUI.RadiusSmall, x, y, 280f, count * optionHeight + 12f, true);
        list.SetShadow(BlueprintUI.InkShadow, SmallShadow);
        view.sortOptions = new Button[count];
        view.sortOptionMarks = new Graphic[count];
        for (int i = 0; i < count; i++)
        {
            BlueprintBox option = StaffUI.Box(list.transform, "Option", UIColors.PaperHi, Color.clear, 0f, BlueprintUI.RadiusSmall,
                                              6f, 6f + i * optionHeight, 268f, optionHeight, true);
            view.sortOptions[i] = BlueprintUI.MakeButton(option);
            BlueprintBox mark = BlueprintUI.Box(option.transform, "Mark", UIColors.Highlight, Color.clear, 0f, BlueprintUI.RadiusSmall);
            BlueprintUI.Stretch(mark.rectTransform, 2f, 3f, 2f, 3f);
            mark.enabled = false;
            view.sortOptionMarks[i] = mark;
            TextMeshProUGUI label = StaffUI.Shrink(StaffUI.Line(option.transform, "Label", "", 22f, UIColors.Ink, font, 14f, optionHeight * 0.5f, 240f), 15f);
            L10nText.Attach(label, T, StaffFilesView.SortKeys[i]);   // "Worst profit first", ...
        }
        menu.gameObject.SetActive(false);
        view.sortMenu = menu;
    }

    // ------------------------------------------------------------------
    // The open file (CV look on the left, green-bar performance ledger on the right)
    // ------------------------------------------------------------------

    static void FileSheet(StaffFilesView view, Transform frame, TMP_FontAsset font)
    {
        StaffFileSheet s = view.file;
        RectTransform layer = BlueprintUI.Stretch(BlueprintUI.NewRect("File", frame));
        StaffUI.Blocker(layer, "Dim", UIColors.Dark(0.35f));
        BlueprintBox paper = StaffUI.Box(layer, "Sheet", UIColors.PaperHi, UIColors.Ink, 3f, SheetRadii, 400f, 140f, 1120f, 800f, true);
        paper.SetShadow(new Color(0f, 0f, 0f, 0.38f), new Vector2(0f, 18f), 36f);
        s.root = layer;
        s.paper = paper;
        Transform p = paper.transform;

        // Letterhead
        Fixed(StaffUI.Caps(StaffUI.Line(p, "Kicker", "", 19f, UIColors.Pencil, font, 32f, 41f, 300f), 0.12f), "staff.file.kicker");   // "STAFF FILE"
        s.firstNameText = StaffUI.Shrink(StaffUI.Line(p, "FirstName", "", 54f, UIColors.Ink, font, 32f, 89f, 300f, TextAlignmentOptions.MidlineLeft, true), 34f);
        s.lastNameText = StaffUI.Shrink(StaffUI.Line(p, "LastName", "", 54f, UIColors.Ink, font, 32f, 146f, 300f, TextAlignmentOptions.MidlineLeft, true), 34f);
        RectTransform stars = BlueprintUI.NewRect("GoldenStars", p);
        BlueprintUI.TopLeft(stars, 32f, 73f, 72f, 32f);
        for (int i = 0; i < 2; i++)
            StaffUI.Icon(stars, StaffIconKind.Star, i * 40f, 0f, 32f, UIColors.Ink, 1.8f).FillColor = UIColors.Gold;
        stars.gameObject.SetActive(false);
        s.goldenStars = stars;
        s.metaText = StaffUI.Shrink(StaffUI.Line(p, "Meta", "", 22f, UIColors.InkSoft, font, 32f, 195f, 310f), 16f);

        BlueprintBox photoBox = StaffUI.Box(p, "Photo", UIColors.PaperShade, Color.clear, 0f, Vector4.zero, 348f, 28f, 180f, 180f);
        photoBox.SetShadow(new Color32(29, 36, 51, 64), SmallShadow);
        photoBox.gameObject.AddComponent<RectMask2D>();
        RectTransform placeholder = BlueprintUI.Stretch(BlueprintUI.NewRect("Placeholder", photoBox.transform));
        StaffUI.Icon(placeholder, StaffIconKind.Person, 13f, 48f, 150f, UIColors.Pencil, 1.1f);
        Fixed(StaffUI.Caps(StaffUI.Line(placeholder, "Label", "", 19f, UIColors.Pencil, font, 0f, 23f, 176f, TextAlignmentOptions.Midline), 0.08f),
              "staff.file.photo");   // "PHOTO"
        s.photoPlaceholder = placeholder.gameObject;
        RectTransform photoRect = BlueprintUI.Stretch(BlueprintUI.NewRect("Portrait", photoBox.transform), 2f, 2f, 2f, 2f);
        Image photo = photoRect.gameObject.AddComponent<Image>();
        photo.preserveAspect = true;
        photo.raycastTarget = false;
        photo.enabled = false;
        s.photo = photo;
        StaffUI.Box(photoBox.transform, "Rim", Color.clear, UIColors.Ink, 2f, Vector4.zero, 0f, 0f, 180f, 180f);

        s.rule = StaffUI.Box(p, "Rule", Color.white, Color.clear, 0f, Vector4.zero, 32f, 225f, 496f, 4f);   // ink, gold for golden hires (.color)
        s.rule.color = UIColors.Ink;

        // CV body (the right 150 px of it are the margin for the red pen)
        Section(p, "staff.file.section.education", 260f, font);   // "EDUCATION"
        Label(p, "staff.file.label.degree", 295f, font);
        s.degreeText = Value(p, "Degree", 295f, 22f, font);
        Label(p, "staff.file.label.major", 328f, font);
        s.majorText = Value(p, "Major", 328f, 22f, font);
        Label(p, "staff.file.label.gpa", 361f, font);
        s.gpaText = Value(p, "GPA", 361f, 26f, font, true);
        StaffUI.Box(p, "Rule", UIColors.Ink, Color.clear, 0f, Vector4.zero, 32f, 390f, 346f, 2f);
        Section(p, "staff.file.section.work", 418f, font);   // "WORK"
        Label(p, "staff.file.label.experience", 454f, font);
        s.experienceText = Value(p, "Experience", 454f, 26f, font);
        Label(p, "staff.file.label.salary", 488f, font);
        s.salaryText = Value(p, "Salary", 488f, 26f, font);
        StaffUI.Box(p, "Rule", UIColors.Ink, Color.clear, 0f, Vector4.zero, 32f, 519f, 346f, 2f);
        Section(p, "staff.file.section.personality", 547f, font);   // "PERSONALITY"
        s.traitTexts = new TMP_Text[4];
        s.traitIcons = new StaffIcon[4];
        for (int i = 0; i < 4; i++)
        {
            float cy = 582f + i * 32f;
            s.traitIcons[i] = StaffUI.Icon(p, StaffIconKind.Check, 32f, cy - 12f, 24f, UIColors.Money, 3f);
            s.traitTexts[i] = StaffUI.Shrink(StaffUI.Line(p, "Trait", "", 22f, UIColors.Ink, font, 66f, cy, 312f), 15f);
        }
        s.fileNumberText = StaffUI.Shrink(StaffUI.Line(p, "FileNumber", "", 19f, UIColors.Pencil, font, 32f, 763f, 340f), 14f);

        // Margin: IQ graded in red pen, the record stamp, the golden seal
        TextMeshProUGUI iqLabel = StaffUI.Line(p, "IQLabel", "", 26f, UIColors.Loss, font, 386f, 259f, 60f, TextAlignmentOptions.MidlineLeft, true);
        iqLabel.rectTransform.localRotation = StaffUI.Rotation(-8f);
        L10nText.Attach(iqLabel, T, "staff.file.iq_label");   // "IQ"
        StaffIcon loop = StaffUI.Icon(p, StaffIconKind.PenLoop, 382f, 268f, 150f, UIColors.Loss, 3.5f);
        BlueprintUI.TopLeft(loop.rectTransform, 382f, 268f, 150f, 120f);
        s.iqText = StaffUI.Line(p, "IQ", "100", 58f, UIColors.Loss, font, 382f, 328f, 150f, TextAlignmentOptions.Midline, true);
        // The stamp keeps as wide as its words (the view writes clean or has-a-record in the current language)
        s.recordText = StaffUI.Stamp(p, "RecordStamp", L10n.Get(T, "staff.file.record.clean"), 24f, UIColors.Money, font, 465f, 488f, -8f,
                                     out BlueprintBox stamp, out BlueprintBox stampLine);
        s.recordInk = new Graphic[] { stamp, stampLine };
        s.goldenSeal = GoldenSeal(p, font, 404f, 556f).gameObject;

        // Performance: one row per green band (34 px)
        BlueprintBox card = StaffUI.Ledger(p, "Performance", 588f, 28f, 500f, 374f);
        Transform c = card.transform;
        Fixed(StaffUI.Shrink(StaffUI.Caps(StaffUI.Line(c, "Heading", "", 17f, UIColors.Ink, font, 46f, 17f, 240f, TextAlignmentOptions.MidlineLeft, true), 0.14f), 12f),
              "staff.file.performance");   // "PERFORMANCE"
        s.basisText = StaffUI.Shrink(StaffUI.Line(c, "Basis", "", 17f, UIColors.Pencil, font, 280f, 17f, 200f, TextAlignmentOptions.MidlineRight), 12f);
        s.hiredText = LedgerRow(c, "Hired", "staff.file.ledger.hired", 1, font);
        s.daysText = LedgerRow(c, "On staff", "staff.file.ledger.on_staff", 2, font);
        s.outputText = LedgerRow(c, "Output", "staff.file.ledger.output", 3, font);
        s.salaryDayText = LedgerRow(c, "Salary", "staff.file.ledger.salary", 4, font);
        s.overheadText = LedgerRow(c, "Overhead", "staff.file.ledger.overhead", 5, font);
        s.troubleText = LedgerRow(c, "Criminal-record trouble", "staff.file.ledger.trouble", 6, font);
        s.bonusText = LedgerRow(c, "Per-head skill bonus", "staff.file.ledger.bonus", 7, font);
        StaffUI.Box(c, "SumLine", UIColors.Ink, Color.clear, 0f, Vector4.zero, 46f, 8f * 34f - 1f, 434f, 2f);
        Fixed(StaffUI.Shrink(StaffUI.Line(c, "ProfitLabel", "", 22f, UIColors.Ink, font, 46f, 9f * 34f, 220f, TextAlignmentOptions.MidlineLeft, true), 15f),
              "staff.file.ledger.profit");   // "Profit / day"
        s.profitText = StaffUI.Shrink(StaffUI.Line(c, "Profit", "$0", 34f, UIColors.Money, font, 250f, 9f * 34f, 230f, TextAlignmentOptions.MidlineRight, true), 20f);
        Fixed(StaffUI.Shrink(StaffUI.Line(c, "LifetimeLabel", "", 19f, UIColors.Pencil, font, 46f, 10.5f * 34f, 250f), 13f),
              "staff.file.ledger.lifetime");   // "Lifetime profit"
        s.lifetimeText = StaffUI.Shrink(StaffUI.Line(c, "Lifetime", "$0", 22f, UIColors.Ink, font, 280f, 10.5f * 34f, 200f, TextAlignmentOptions.MidlineRight, true), 15f);

        // Longer languages shrink the notes rather than let them run under the buttons
        s.noteText = StaffUI.Shrink(StaffUI.Block(p, "Notes", "", 19f, UIColors.InkSoft, font, 588f, 420f, 500f, 270f, 1.35f), 14f);

        s.fireButton = StaffUI.PaperButton(p, "Fire", "", StaffIconKind.None, font, UIColors.Loss, UIColors.PaperHi,
                                           664f, 712f, 200f, 60f, 24f, true, ButtonShadow, BlueprintUI.RadiusLarge, out TextMeshProUGUI fire);
        L10nText.Attach(fire, T, "staff.file.button.fire");   // "Fire"
        s.closeButton = StaffUI.PaperButton(p, "CloseFile", "", StaffIconKind.Close, font, UIColors.PaperHi, UIColors.Ink,
                                            888f, 712f, 200f, 60f, 22f, false, ButtonShadow, BlueprintUI.RadiusLarge, out TextMeshProUGUI close);
        L10nText.Attach(close, T, "staff.file.button.close");   // "Close file"
        layer.gameObject.SetActive(false);
    }

    /// <summary>A fixed label kept on its String Table entry (and the language's font).</summary>
    static TextMeshProUGUI Fixed(TextMeshProUGUI text, string key)
    {
        L10nText.Attach(text, T, key);
        return text;
    }

    /// <summary>A section heading in spaced capitals (written in capitals in the String Table).</summary>
    static void Section(Transform parent, string key, float centerY, TMP_FontAsset font)
    {
        Fixed(StaffUI.Shrink(StaffUI.Caps(StaffUI.Line(parent, "Section", "", 17f, UIColors.Ink, font, 32f, centerY, 346f, TextAlignmentOptions.MidlineLeft, true), 0.14f), 12f),
              key);
    }

    static void Label(Transform parent, string key, float centerY, TMP_FontAsset font)
    {
        Fixed(StaffUI.Shrink(StaffUI.Line(parent, "Label", "", 19f, UIColors.Pencil, font, 32f, centerY, 112f), 13f), key);
    }

    static TMP_Text Value(Transform parent, string name, float centerY, float size, TMP_FontAsset font, bool bold = false)
    {
        return StaffUI.Shrink(StaffUI.Line(parent, name, "", size, UIColors.Ink, font, 156f, centerY, 222f, TextAlignmentOptions.MidlineLeft, bold), 15f);
    }

    static TMP_Text LedgerRow(Transform card, string name, string key, int band, TMP_FontAsset font)
    {
        float cy = band * 34f + 17f;
        Fixed(StaffUI.Shrink(StaffUI.Line(card, "Label", "", 19f, UIColors.Pencil, font, 46f, cy, 250f), 13f), key);
        return StaffUI.Shrink(StaffUI.Line(card, name, "$0", 22f, UIColors.Ink, font, 280f, cy, 200f, TextAlignmentOptions.MidlineRight), 15f);
    }

    /// <summary>The CV's "1 in 100" seal: two ribbons under a gold medallion with a star, turned 8°.</summary>
    static RectTransform GoldenSeal(Transform parent, TMP_FontAsset font, float x, float y)
    {
        RectTransform seal = BlueprintUI.NewRect("GoldenSeal", parent);
        seal.anchorMin = seal.anchorMax = new Vector2(0f, 1f);
        seal.pivot = new Vector2(0.5f, 0.5f);
        seal.sizeDelta = new Vector2(112f, 124f);
        seal.anchoredPosition = new Vector2(x + 56f, -(y + 62f));
        seal.localRotation = StaffUI.Rotation(8f);

        for (int i = 0; i < 2; i++)
        {
            BlueprintBox ribbon = BlueprintUI.Box(seal, "Ribbon", UIColors.GoldDark, UIColors.Ink, 2f, Vector4.zero);
            RectTransform r = ribbon.rectTransform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0.5f, 1f);
            r.sizeDelta = new Vector2(22f, 52f);
            r.anchoredPosition = new Vector2(i == 0 ? 41f : 71f, -70f);
            r.localRotation = StaffUI.Rotation(i == 0 ? 18f : -18f);
        }
        BlueprintBox medal = StaffUI.Box(seal, "Medal", UIColors.Gold, UIColors.Ink, 3f, BlueprintUI.Round(50f), 6f, 0f, 100f, 100f);
        medal.SetGradient(UIColors.Hex(0xF7DC87), new Vector2(0.38f, 0.32f));
        medal.SetShadow(new Color(0f, 0f, 0f, 0.25f), new Vector2(0f, 6f), 10f);
        StaffUI.Icon(medal.transform, StaffIconKind.Star, 33f, 18f, 34f, UIColors.Ink, 1.6f).FillColor = UIColors.PaperHi;
        Fixed(StaffUI.Shrink(StaffUI.Line(medal.transform, "Label", "", 17f, UIColors.Ink, font, 0f, 68f, 100f, TextAlignmentOptions.Midline, true), 11f),
              "staff.file.seal");   // "1 in 100"
        return seal;
    }

    // ------------------------------------------------------------------
    // Fire confirmation
    // ------------------------------------------------------------------

    static void Confirm(StaffFilesView view, Transform frame, TMP_FontAsset font)
    {
        StaffFireConfirm c = view.confirm;
        RectTransform layer = BlueprintUI.Stretch(BlueprintUI.NewRect("Confirm", frame));
        StaffUI.Blocker(layer, "Dim", UIColors.Dark(0.45f));
        BlueprintBox sheet = StaffUI.Box(layer, "Sheet", UIColors.PaperHi, UIColors.Ink, 3f, BlueprintUI.RadiusCard, 580f, 350f, 760f, 380f, true);
        sheet.SetShadow(StaffUI.SheetShadow, new Vector2(0f, 22f), 40f);
        Transform t = sheet.transform;

        BlueprintBox tape = BlueprintUI.Box(t, "Tape", UIColors.Tape, Color.clear, 0f, BlueprintUI.Round(2f));
        tape.SetShadow(new Color(0f, 0f, 0f, 0.18f), new Vector2(0f, 2f), 4f);
        RectTransform tapeRect = tape.rectTransform;
        tapeRect.anchorMin = tapeRect.anchorMax = new Vector2(0.5f, 1f);
        tapeRect.pivot = new Vector2(0.5f, 0.5f);
        tapeRect.sizeDelta = new Vector2(220f, 36f);
        tapeRect.anchoredPosition = new Vector2(0f, -2f);
        tapeRect.localRotation = StaffUI.Rotation(-2f);

        c.root = layer;
        c.titleText = StaffUI.Ellipsis(StaffUI.Shrink(StaffUI.Line(t, "Title", L10n.Get(T, "staff.confirm.title.plain"), 44f, UIColors.Ink, font, 40f, 78f, 680f,
                                                                   TextAlignmentOptions.Midline, true), 28f));
        c.detailText = StaffUI.Shrink(StaffUI.Line(t, "Detail", "", 26f, UIColors.Ink, font, 40f, 140f, 680f, TextAlignmentOptions.Midline), 18f);
        c.noteText = StaffUI.Shrink(StaffUI.Block(t, "Note", "", 19f, UIColors.InkSoft, font, 60f, 172f, 640f, 92f, 1.3f, TextAlignmentOptions.Top), 14f);   // a quick fire can need three lines
        c.fireButton = StaffUI.PaperButton(t, "Fire", "", StaffIconKind.None, font, UIColors.Loss, UIColors.PaperHi,
                                           140f, 282f, 220f, 64f, 26f, true, ButtonShadow, BlueprintUI.RadiusLarge, out TextMeshProUGUI fire);
        L10nText.Attach(fire, T, "staff.confirm.button.fire");   // "Fire"
        c.keepButton = StaffUI.PaperButton(t, "Keep", "", StaffIconKind.None, font, UIColors.PaperHi, UIColors.Ink,
                                           400f, 282f, 220f, 64f, 26f, false, ButtonShadow, BlueprintUI.RadiusLarge, out TextMeshProUGUI keep);
        L10nText.Attach(keep, T, "staff.confirm.button.keep");   // "Keep"
        layer.gameObject.SetActive(false);
    }
}
