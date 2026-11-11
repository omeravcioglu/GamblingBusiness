using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>The city's dialogs: company and logo (draw your own or pick a preset), bank, inbox, confirmations, test tools.</summary>
public partial class CityScreen
{
    // ---- Company and logo -------------------------------------------------------------------------------------

    void OpenCompanyDialog(bool joining, bool? drawTab = null)
    {
        if (city == null || !city.IsReady || ShownCity == null)
        {
            Toast("Still connecting…", false);
            return;
        }
        var map = mapView.Map;
        var account = city.Account;
        joining = joining || !city.Joined;
        string startName = joining ? StartingName(map.CityId) : account.CompanyName;

        // Presets start from the current logo (or a suggestion); the drawing from the current drawing (or a starter)
        bool hasDrawing = !joining && CityPixelLogo.IsRef(account.Logo);
        var draft = !joining && !hasDrawing ? CityLogo.Decode(account.Logo) : CityLogo.Suggest(startName);
        if (joining || hasDrawing) draft.Letters = CityLogo.InitialsOf(startName);
        var startPixels = hasDrawing ? CityLogoCache.Pixels(CityPixelLogo.HashOfRef(account.Logo)) : null;
        bool drawing = drawTab ?? (joining || hasDrawing);

        VisualElement scrim = null;
        var dialog = CityUi.Dialog(joining ? $"Set up your company in {map.Name}" : $"Your company in {map.Name}", 1560, CloseTopModal, out var body);
        body.Add(CityUi.Text(joining
            ? "This is how the city sees you: your name and logo go on every plot you own. Draw your own logo or pick a preset."
            : "Changes show on all your plots at once.", 21, CityUi.InkSoft));

        // Name, sample and tabs
        var top = CityUi.Row(20);
        top.Add(CityUi.Text("Company name", 20, CityUi.Muted));
        var nameField = CityUi.Field(startName, CityRules.NameMaxLength, 28);
        nameField.style.width = 380;
        top.Add(nameField);
        var nameError = CityUi.Text("", 17, CityUi.Red);
        top.Add(nameError);
        top.Add(CityUi.Spacer());
        CityUi.InkButton drawButton = null, presetButton = null;
        VisualElement drawPage = null, presetPage = null;
        drawButton = new CityUi.InkButton("Draw your logo", () => SwitchTab(true), CityUi.ButtonKind.Paper, "pencil", 52, 20);
        presetButton = new CityUi.InkButton("Pick a preset", () => SwitchTab(false), CityUi.ButtonKind.Paper, "star", 52, 20);
        top.Add(drawButton);
        top.Add(presetButton);
        body.Add(top);

        var pages = new VisualElement();
        body.Add(pages);

        // Draw page: the editor and a preview of how it looks on the map
        drawPage = CityUi.Row(24, Align.FlexStart);
        var editor = new CityPixelEditor(startPixels ?? StarterDrawing(startName));
        drawPage.Add(editor);
        var previewColumn = CityUi.Column(12);
        previewColumn.style.alignItems = Align.Center;
        previewColumn.style.width = 250;
        previewColumn.Add(CityUi.Text("How it looks", 20, CityUi.Ink, true));
        var bigPreview = new CityLogoView(null, 170);
        bigPreview.SetPixels(editor.Preview);
        previewColumn.Add(bigPreview);
        var mapSample = CityUi.Row(10);
        var smallPreview = new CityLogoView(null, 50);
        smallPreview.SetPixels(editor.Preview);
        mapSample.Add(smallPreview);
        var drawName = CityUi.OneLine(CityUi.Text("", 20, CityUi.Ink, true));
        drawName.style.maxWidth = 170;
        mapSample.Add(drawName);
        previewColumn.Add(mapSample);
        previewColumn.Add(CityUi.Text("Players can report rude drawings. A drawing reported by " + CityRules.ReportsToHide + " players is taken down.", 16, CityUi.Muted));
        drawPage.Add(previewColumn);
        editor.Changed += () =>
        {
            bigPreview.MarkDirtyRepaint();
            smallPreview.MarkDirtyRepaint();
        };
        pages.Add(drawPage);

        // Preset page: the logo maker
        presetPage = CityUi.Row(44, Align.FlexStart);
        var left = CityUi.Column(14);
        left.style.width = 380;
        left.style.alignItems = Align.Center;
        var preview = new CityLogoView(null, 230);
        left.Add(preview);
        var sample = CityUi.Row(10);
        var sampleLogo = new CityLogoView(null, 44);
        var sampleName = CityUi.OneLine(CityUi.Text("", 22, CityUi.Ink, true));
        sampleName.style.maxWidth = 260;
        sample.Add(sampleLogo);
        sample.Add(sampleName);
        left.Add(sample);
        presetPage.Add(left);

        var right = CityUi.Column(12);
        right.style.flexGrow = 1;
        right.style.flexShrink = 1;
        presetPage.Add(right);
        pages.Add(presetPage);

        var shapeTiles = new List<(VisualElement tile, string value)>();
        var bgTiles = new List<(VisualElement tile, string value)>();
        var fgTiles = new List<(VisualElement tile, string value)>();
        var iconTiles = new List<(VisualElement tile, string value)>();
        CityUi.InkButton ringButton = null;

        void Refresh()
        {
            preview.Set(draft);
            sampleLogo.Set(draft);
            string shown = string.IsNullOrWhiteSpace(nameField.value) ? "Your company" : nameField.value.Trim();
            sampleName.text = shown;
            drawName.text = shown;
            foreach (var t in shapeTiles) Mark(t.tile, t.value == draft.Shape);
            foreach (var t in bgTiles) Mark(t.tile, t.value == draft.Background);
            foreach (var t in fgTiles) Mark(t.tile, t.value == draft.Foreground);
            foreach (var t in iconTiles) Mark(t.tile, t.value == draft.Icon);
            ringButton?.SetText(draft.Ring ? "Inner ring: on" : "Inner ring: off");
            string clean = CityRules.CleanCompanyName(nameField.value, out string error);
            nameError.text = clean == null && !string.IsNullOrWhiteSpace(nameField.value) ? error : "";
        }

        void SwitchTab(bool draw)
        {
            drawing = draw;
            drawPage.style.display = draw ? DisplayStyle.Flex : DisplayStyle.None;
            presetPage.style.display = draw ? DisplayStyle.None : DisplayStyle.Flex;
            drawButton.Face.style.backgroundColor = draw ? CityUi.Highlight : CityUi.Card;
            presetButton.Face.style.backgroundColor = draw ? CityUi.Card : CityUi.Highlight;
        }

        right.Add(CityUi.Text("Shape", 20, CityUi.Ink, true));
        var shapes = CityUi.Row(10);
        foreach (string shape in CityLogo.Shapes)
        {
            var tileLogo = new CityLogoView(null, 56);
            tileLogo.Set(new CityLogo { Shape = shape, Background = "E2B13C", Icon = "none", Letters = "", Ring = false });
            var tile = Tile(tileLogo, () => { draft.Shape = shape; Refresh(); });
            shapeTiles.Add((tile, shape));
            shapes.Add(tile);
        }
        right.Add(shapes);

        right.Add(CityUi.Text("Background colour", 20, CityUi.Ink, true));
        right.Add(Swatches(bgTiles, v => { draft.Background = v; Refresh(); }));
        right.Add(CityUi.Text("Icon and letter colour", 20, CityUi.Ink, true));
        right.Add(Swatches(fgTiles, v => { draft.Foreground = v; Refresh(); }));

        right.Add(CityUi.Text("Icon", 20, CityUi.Ink, true));
        var iconScroll = new ScrollView(ScrollViewMode.Vertical);
        iconScroll.style.height = 196;
        iconScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        var iconGrid = new VisualElement();
        iconGrid.style.flexDirection = FlexDirection.Row;
        iconGrid.style.flexWrap = Wrap.Wrap;
        foreach (string icon in CityLogo.Icons)
        {
            VisualElement content;
            if (icon == "none") content = CityUi.Text("Aa", 24, CityUi.Ink, true);
            else content = new CityUi.IconView(icon, 36);
            var tile = Tile(content, () => { draft.Icon = icon; Refresh(); });
            tile.style.marginRight = 8;
            tile.style.marginBottom = 8;
            iconTiles.Add((tile, icon));
            iconGrid.Add(tile);
        }
        iconScroll.Add(iconGrid);
        right.Add(iconScroll);

        var lettersRow = CityUi.Row(14);
        lettersRow.Add(CityUi.Text("Letters", 20, CityUi.Ink, true));
        var lettersField = CityUi.Field(draft.Letters, CityLogo.MaxLetters, 26);
        lettersField.style.width = 130;
        lettersField.RegisterValueChangedCallback(e =>
        {
            draft.Letters = CityLogo.CleanLetters(e.newValue);
            Refresh();
        });
        lettersRow.Add(lettersField);
        ringButton = new CityUi.InkButton("Inner ring: on", () => { draft.Ring = !draft.Ring; Refresh(); }, CityUi.ButtonKind.Paper, null, 50, 20);
        lettersRow.Add(ringButton);
        lettersRow.Add(new CityUi.InkButton("Surprise me", () =>
        {
            string letters = draft.Letters;
            draft = CityLogo.Suggest(nameField.value, UnityEngine.Random.Range(1, 1000000));
            draft.Letters = letters;
            Refresh();
        }, CityUi.ButtonKind.Paper, "shuffle", 50, 20));
        right.Add(lettersRow);

        nameField.RegisterValueChangedCallback(_ => Refresh());

        // Save
        var footer = CityUi.Row(16);
        footer.style.marginTop = 10;
        CityUi.InkButton save = null;
        save = new CityUi.InkButton(joining ? "Open for business" : "Save", () => _ = Save(), CityUi.ButtonKind.Primary, "check");
        footer.Add(save);
        footer.Add(new CityUi.InkButton("Cancel", CloseTopModal, CityUi.ButtonKind.Paper));
        body.Add(footer);

        async Task Save()
        {
            string name = CityRules.CleanCompanyName(nameField.value, out string error);
            if (name == null)
            {
                nameError.text = error;
                return;
            }
            string preset = null, pixels = null;
            if (drawing)
            {
                if (editor.Logo.Painted < CityPixelLogo.MinPainted)
                {
                    Toast("Draw a bit more first - your logo is nearly empty.", false);
                    return;
                }
                pixels = editor.Logo.Encode();
            }
            else
            {
                if (draft.Icon == "none" && string.IsNullOrEmpty(draft.Letters))
                {
                    Toast("Pick an icon or type some letters.", false);
                    return;
                }
                preset = draft.Encode();
            }
            var result = await Run(() => joining ? city.Join(name, preset, pixels) : city.UpdateProfile(name, preset, pixels), save, "Saving…");
            if (result != null && result.Ok)
            {
                if (pixels != null) CityLogoCache.Put(pixels);
                CloseModal(scrim);
                RefreshEverything();
            }
        }

        scrim = PushModal(dialog);
        SwitchTab(drawing);
        Refresh();
    }

    /// <summary>A name to start with: the company's name, or the one it uses in another city.</summary>
    string StartingName(string cityId)
    {
        string name = CompanyNameUI.GetCompanyName();
        if (string.IsNullOrWhiteSpace(name) || name == "New Company")
            name = city.MyAccounts.Values.FirstOrDefault(a => a != null)?.CompanyName ?? "";
        return name;
    }

    /// <summary>A gold coin with the company's initials: something nice to start drawing from.</summary>
    static CityPixelLogo StarterDrawing(string companyName)
    {
        var logo = new CityPixelLogo();
        byte gold = (byte)Array.IndexOf(CityPixelLogo.Palette, "E2B13C");
        byte ink = (byte)Array.IndexOf(CityPixelLogo.Palette, "1D2433");
        const int n = CityPixelLogo.Size;
        bool Inside(int x, int y)
        {
            float dx = x + 0.5f - n * 0.5f, dy = y + 0.5f - n * 0.5f;
            return x >= 0 && y >= 0 && x < n && y < n && dx * dx + dy * dy <= 14.5f * 14.5f;
        }
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                if (!Inside(x, y)) continue;
                bool edge = !Inside(x - 1, y) || !Inside(x + 1, y) || !Inside(x, y - 1) || !Inside(x, y + 1);
                logo[x, y] = edge ? ink : gold;
            }
        }
        CityPixelEditor.StampLetters(logo, CityLogo.InitialsOf(companyName), ink);
        return logo;
    }

    static VisualElement Swatches(List<(VisualElement tile, string value)> tiles, Action<string> pick)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.flexWrap = Wrap.Wrap;
        foreach (string hex in CityLogo.Palette)
        {
            var swatch = new VisualElement();
            swatch.style.width = 34;
            swatch.style.height = 34;
            swatch.style.backgroundColor = CityUi.Hex(hex);
            CityUi.Radius(swatch, 17);
            CityUi.Border(swatch, 2, CityUi.Ink);
            swatch.pickingMode = PickingMode.Ignore;
            var tile = Tile(swatch, () => pick(hex), 46);
            tile.style.marginRight = 6;
            tile.style.marginBottom = 6;
            tiles.Add((tile, hex));
            row.Add(tile);
        }
        return row;
    }

    /// <summary>A clickable square around some content (logo maker choices).</summary>
    static VisualElement Tile(VisualElement content, Action onClick, float size = 70)
    {
        var tile = new VisualElement();
        tile.style.width = size;
        tile.style.height = size;
        tile.style.alignItems = Align.Center;
        tile.style.justifyContent = Justify.Center;
        tile.style.backgroundColor = CityUi.Card;
        CityUi.Border(tile, 2, CityUi.Alpha(CityUi.Ink, 0.35f));
        CityUi.Radius(tile, 10, 14, 9, 15);
        content.pickingMode = PickingMode.Ignore;
        tile.Add(content);
        tile.RegisterCallback<ClickEvent>(_ => onClick());
        tile.RegisterCallback<PointerEnterEvent>(_ => tile.style.translate = new Translate(-1, -2));
        tile.RegisterCallback<PointerLeaveEvent>(_ => tile.style.translate = new Translate(0, 0));
        return tile;
    }

    static void Mark(VisualElement tile, bool on)
    {
        CityUi.Border(tile, on ? 4 : 2, on ? CityUi.Ink : CityUi.Alpha(CityUi.Ink, 0.35f));
        tile.style.backgroundColor = on ? CityUi.Highlight : CityUi.Card;
    }

    // ---- Bank --------------------------------------------------------------------------------------------------

    VisualElement bankBody;

    void OpenBankDialog()
    {
        if (city == null || !city.IsReady || !city.Joined || ShownCity == null)
        {
            Toast("Set up your company first.", false);
            return;
        }
        var dialog = CityUi.Dialog($"{mapView.Map.Name} bank", 1240, CloseTopModal, out var body);
        bankBody = body;
        BuildBankBody();
        PushModal(dialog, () => bankBody = null);
    }

    void BuildBankBody()
    {
        if (bankBody == null) return;
        bankBody.Clear();
        var account = city.Account;
        var company = CompanyManager.Instance;
        bool inOffice = company != null && SaveSystem.Instance != null && SaveSystem.Instance.IsGameLoaded;
        string cityName = mapView.Map.Name;

        bankBody.Add(CityUi.Text($"Money in your {cityName} bank buys plots and stakes in {cityName} only, and your rent here lands here. Wire profits back to your company whenever you like.", 21, CityUi.InkSoft));

        var columns = CityUi.Row(28, Align.Stretch);
        bankBody.Add(columns);

        // Wire in
        var inCard = BankCard("Wire in", $"From your company to {cityName}", CityUi.PastelYellow, "wireIn");
        long companyMoney = inOffice ? (long)Math.Floor(company.companyValue) : 0;
        inCard.Add(CityUi.LedgerLine("Your company has", inOffice ? CityRules.Money(companyMoney) : "-"));
        inCard.Add(CityUi.LedgerLine("You can wire today", CityRules.Money(account.DepositLeft)));
        if (account.DepositLeft < CityRules.DepositCapPerDay && account.DepositResetAt > city.Now)
            inCard.Add(CityUi.Text($"More room opens in {CityRules.Duration(account.DepositResetAt - city.Now)} (the limit is {CityRules.Money(CityRules.DepositCapPerDay)} a day per city).", 17, CityUi.Muted));
        long maxIn = Math.Min(account.DepositLeft, companyMoney);
        inCard.Add(TransferRow(maxIn, "Wire in", amount => city.Deposit(amount), inOffice));
        columns.Add(inCard);

        // Wire out
        var outCard = BankCard("Wire out", $"From {cityName} to your company", CityUi.PastelGreen, "wireOut");
        outCard.Add(CityUi.LedgerLine($"{cityName} bank", CityRules.Money(account.Balance)));
        if (account.InPlay > 0) outCard.Add(CityUi.LedgerLine("On the table", CityRules.Money(account.InPlay), CityUi.Muted));
        outCard.Add(CityUi.LedgerLine("Rent earned so far", CityRules.Money(account.Stats?.RentEarned ?? 0), CityUi.Green));
        if ((account.Stats?.DefenseIncome ?? 0) > 0)
            outCard.Add(CityUi.LedgerLine("Won from challengers", CityRules.Money(account.Stats.DefenseIncome), CityUi.Green));
        outCard.Add(TransferRow(account.Balance, "Wire out", amount => city.Withdraw(amount), inOffice));
        columns.Add(outCard);

        if (!inOffice)
            bankBody.Add(CityUi.Text("Open the cities from your office to move money.", 20, CityUi.Red));
        if (city.HasPendingTransfers)
            bankBody.Add(CityUi.Text("A transfer is still being confirmed. It finishes (or comes back) as soon as the city answers.", 19, CityUi.Blue));
    }

    static VisualElement BankCard(string title, string subtitle, Color colour, string icon)
    {
        var card = CityUi.CardBox(CityUi.Card, 3);
        card.style.flexGrow = 1;
        card.style.flexBasis = 0;
        CityUi.Padding(card, 22, 24);
        var header = CityUi.Row(14);
        var badge = new VisualElement();
        badge.style.width = 54;
        badge.style.height = 54;
        badge.style.alignItems = Align.Center;
        badge.style.justifyContent = Justify.Center;
        badge.style.backgroundColor = colour;
        CityUi.Border(badge, 2, CityUi.Ink);
        CityUi.Radius(badge, 27);
        badge.Add(new CityUi.IconView(icon, 30));
        header.Add(badge);
        var titles = CityUi.Column(0);
        titles.Add(CityUi.Text(title, 32, CityUi.Ink, true));
        titles.Add(CityUi.Text(subtitle, 18, CityUi.InkSoft));
        header.Add(titles);
        header.style.marginBottom = 10;
        card.Add(header);
        return card;
    }

    VisualElement TransferRow(long max, string buttonText, Func<long, Task<CityResult>> transfer, bool enabled)
    {
        var box = CityUi.Column(10);
        box.style.marginTop = 14;
        var fieldRow = CityUi.Row(10);
        var field = CityUi.Field("", 16, 26);
        field.style.width = 220;
        fieldRow.Add(field);
        var readBack = CityUi.Text("", 18, CityUi.InkSoft);
        fieldRow.Add(readBack);
        box.Add(fieldRow);

        var quick = CityUi.Row(8);
        foreach (var (label, share) in new[] { ("25%", 0.25), ("50%", 0.5), ("Max", 1.0) })
        {
            quick.Add(new CityUi.InkButton(label, () =>
            {
                long amount = (long)Math.Floor(max * share / 1_000_000.0) * 1_000_000L;
                field.value = amount > 0 ? CityRules.Money(amount).Replace("$", "") : "";
            }, CityUi.ButtonKind.Paper, null, 46, 18));
        }
        box.Add(quick);

        CityUi.InkButton go = null;
        go = new CityUi.InkButton(buttonText, () => _ = Go(), CityUi.ButtonKind.Primary, buttonText == "Wire in" ? "wireIn" : "wireOut");
        go.SetEnabledLook(enabled);
        box.Add(go);

        async Task Go()
        {
            if (!go.IsOn) return;
            if (!CityRules.TryParseMoney(field.value, out long amount))
            {
                Toast("Type an amount like 250M or 1.5B.", false);
                return;
            }
            var result = await Run(() => transfer(amount), go, "Wiring…");
            if (result != null && result.Ok) BuildBankBody();
        }

        field.RegisterValueChangedCallback(_ =>
        {
            bool ok = CityRules.TryParseMoney(field.value, out long amount);
            readBack.text = ok ? "= " + CityRules.MoneyFull(amount) : "";
        });
        return box;
    }

    // ---- Inbox -------------------------------------------------------------------------------------------------

    void OpenInboxDialog()
    {
        if (city == null || !city.IsReady || !city.Joined || ShownCity == null) return;
        var map = mapView.Map;
        var dialog = CityUi.Dialog($"Inbox · {map.Name}", 980, CloseTopModal, out var body);
        var inbox = city.Account.Inbox ?? new List<CityMessage>();
        if (inbox.Count == 0)
            body.Add(CityUi.Text("Nothing yet. Challenges for your plots will show up here.", 22, CityUi.Muted));

        var scroll = new ScrollView(ScrollViewMode.Vertical);
        scroll.style.maxHeight = 640;
        var list = CityUi.Column(10);
        scroll.Add(list);
        body.Add(scroll);
        int unread = city.Account.Unread;
        int index = 0;
        foreach (var message in inbox.OrderByDescending(m => m.Time))
        {
            var card = CityUi.CardBox(index < unread ? CityUi.Hex("FFF6C9") : CityUi.Card, 2);
            CityUi.Radius(card, 10, 14, 9, 15);
            CityUi.Padding(card, 12, 16);
            var row = CityUi.Row(14);
            var (icon, colour) = MessageIcon(message.Kind);
            row.Add(new CityUi.IconView(icon, 32, colour));
            var col = CityUi.Column(2);
            col.style.flexGrow = 1;
            col.style.flexShrink = 1;
            col.Add(CityUi.Text(message.Text, 20));
            col.Add(CityUi.Text(CityUi.Ago(city.Now - message.Time), 16, CityUi.Muted));
            row.Add(col);
            if (message.PlotId != null && map.PlotsById.TryGetValue(message.PlotId, out var def))
            {
                row.Add(new CityUi.InkButton("Show", () =>
                {
                    CloseAllModals();
                    SelectPlot(def);
                    mapView.FocusOn(def);
                }, CityUi.ButtonKind.Paper, "pin", 48, 18));
            }
            card.Add(row);
            list.Add(card);
            index++;
        }
        PushModal(dialog);
        if (unread > 0) _ = city.MarkInboxRead();
    }

    static (string icon, Color colour) MessageIcon(string kind)
    {
        switch (kind)
        {
            case CityMessageKind.TakenOver: return ("sword", CityUi.Red);
            case CityMessageKind.Defended: return ("shield", CityUi.Green);
            case CityMessageKind.LogoHidden: return ("flag", CityUi.Red);
            default: return ("info", CityUi.Ink);
        }
    }

    // ---- Confirm -----------------------------------------------------------------------------------------------

    void Confirm(string title, string text, string yes, Action onYes)
    {
        var dialog = CityUi.Dialog(title, 820, CloseTopModal, out var body);
        body.Add(CityUi.Text(text, 23, CityUi.InkSoft));
        var row = CityUi.Row(16);
        row.style.marginTop = 10;
        VisualElement scrim = null;
        row.Add(new CityUi.InkButton(yes, () =>
        {
            CloseModal(scrim);
            onYes();
        }, CityUi.ButtonKind.Primary, "check"));
        row.Add(new CityUi.InkButton("Cancel", CloseTopModal, CityUi.ButtonKind.Paper));
        body.Add(row);
        scrim = PushModal(dialog);
    }

    // ---- Local test city tools ---------------------------------------------------------------------------------

    void OpenTestTools()
    {
        if (city == null || city.IsLive || ShownCity == null) return;
        string cityName = mapView.Map.Name;
        var dialog = CityUi.Dialog("Test tools", 860, CloseTopModal, out var body);
        body.Add(CityUi.Text($"You're in the local test {cityName}: it lives only on this PC and nobody else sees it. Use these to try what other players can do to you.", 21, CityUi.InkSoft));

        void Tool(string label, string icon, string action)
        {
            CityUi.InkButton button = null;
            button = new CityUi.InkButton(label, () => _ = Do(), CityUi.ButtonKind.Paper, icon, 56, 21);
            body.Add(button);

            async Task Do()
            {
                button.SetEnabledLook(false);
                string message = await city.LocalTestAction(action);
                button.SetEnabledLook(true);
                Toast(message, true);
            }
        }

        Tool($"Add $5B of test money to my {cityName} bank", "coin", "money");
        Tool("A rival plays blackjack for one of my plots", "cards", "challenge");
        Tool($"{CityRules.ReportsToHide} players report my drawn logo", "flag", "report");

        var danger = CityUi.Row(16);
        danger.style.marginTop = 12;
        danger.Add(new CityUi.InkButton($"Wipe the test {cityName}", () => Confirm($"Wipe the test {cityName}?",
            $"Every plot, balance and drawing in the local test {cityName} is deleted. (The real cities aren't touched.)", "Wipe it", () =>
            {
                string target = mapView.Map.CityId;
                city.LocalBackend?.Wipe(target);
                CloseAllModals();
                _ = GoToCity(target);
            }), CityUi.ButtonKind.Danger, "warning", 56, 20));
        danger.Add(new CityUi.InkButton("Switch to the real cities", () =>
        {
            CloseAllModals();
            _ = Retry(true);
        }, CityUi.ButtonKind.Paper, "globe", 56, 20));
        body.Add(danger);
        PushModal(dialog);
    }
}
