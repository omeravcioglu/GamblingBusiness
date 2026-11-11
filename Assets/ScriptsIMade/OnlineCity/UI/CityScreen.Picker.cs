using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>The city picker: six postcards pinned to the board, one per city (each city is its own world).</summary>
public partial class CityScreen
{
    readonly Dictionary<string, (VisualElement status, CityUi.InkButton enter)> postcards =
        new Dictionary<string, (VisualElement, CityUi.InkButton)>();
    Label pickerTest;

    VisualElement BuildPicker()
    {
        var layer = new VisualElement();
        CityUi.Fill(layer);
        layer.style.backgroundColor = CityUi.MapPaper;
        layer.style.alignItems = Align.Center;

        var header = CityUi.Row(18);
        header.style.alignSelf = Align.Stretch;
        header.style.height = 110;
        CityUi.Padding(header, 0, 44);
        header.Add(new CityUi.IconView("globe", 54));
        var titles = CityUi.Column(0);
        var titleRow = CityUi.Row(12);
        titleRow.Add(CityUi.Text("Pick a city", 50, CityUi.Ink, true));
        pickerTest = CityUi.Text("TEST CITIES", 16, CityUi.Card, true);
        pickerTest.style.backgroundColor = CityUi.Blue;
        CityUi.Radius(pickerTest, 6);
        CityUi.Padding(pickerTest, 2, 8);
        pickerTest.style.rotate = new Rotate(-4);
        titleRow.Add(pickerTest);
        titles.Add(titleRow);
        titles.Add(CityUi.Text("Each city is its own world: its own plots, bank and rich list. Your money in one city stays there.", 21, CityUi.InkSoft));
        header.Add(titles);
        header.Add(CityUi.Spacer());
        header.Add(new CityUi.InkButton("Office", Hide, CityUi.ButtonKind.Paper, "building", 60, 22));
        layer.Add(header);

        var grid = new VisualElement();
        grid.style.flexDirection = FlexDirection.Row;
        grid.style.flexWrap = Wrap.Wrap;
        grid.style.justifyContent = Justify.Center;
        grid.style.width = 1860;
        grid.style.marginTop = 6;
        int index = 0;
        foreach (var world in CityLayout.Worlds)
        {
            var card = Postcard(world, index++);
            card.style.marginLeft = 16;
            card.style.marginRight = 16;
            card.style.marginBottom = 30;
            grid.Add(card);
        }
        layer.Add(grid);
        return layer;
    }

    VisualElement Postcard(CityWorldDef world, int index)
    {
        var map = CityLayout.Get(world.Id);
        var accent = CityUi.Hex(world.Accent);

        var card = CityUi.CardBox(CityUi.Card, 3);
        card.style.width = 560;
        card.style.height = 432;
        CityUi.Radius(card, 14, 18, 12, 20);
        card.style.overflow = Overflow.Hidden;

        // Stamp band with the name
        var band = CityUi.Row(12);
        band.style.height = 70;
        band.style.backgroundColor = accent;
        band.style.borderBottomWidth = 3;
        band.style.borderBottomColor = CityUi.Ink;
        CityUi.Padding(band, 0, 20);
        bool darkBand = accent.grayscale < 0.55f;
        var name = CityUi.Text(world.Name, 38, darkBand ? CityUi.Card : CityUi.Ink, true);
        band.Add(name);
        band.Add(CityUi.Spacer());
        band.Add(CityUi.Text(CityUi.Upper(world.Country, world.Locale == "tr"), 15, darkBand ? CityUi.Alpha(CityUi.Card, 0.85f) : CityUi.InkSoft, true));
        card.Add(band);

        var picture = new CitySkyline(world.Id, accent);
        picture.style.height = 170;
        picture.style.marginTop = 10;
        picture.style.marginLeft = 16;
        picture.style.marginRight = 16;
        card.Add(picture);

        var body = CityUi.Column(6);
        CityUi.Padding(body, 4, 20);
        body.style.flexGrow = 1;
        body.Add(CityUi.Text(world.Tagline, 21, CityUi.InkSoft));
        var facts = CityUi.Row(8);
        facts.Add(CityUi.Chip($"{map.Plots.Count} plots", CityUi.PastelYellow, "pin", 15));
        facts.Add(CityUi.Chip($"{map.Districts.Count} districts", CityUi.PastelBlue, null, 15));
        facts.Add(CityUi.Chip($"{world.Landmarks.Length} landmarks", CityUi.Alpha(CityUi.Gold, 0.5f), "star", 15));
        body.Add(facts);

        var bottom = CityUi.Row(12);
        bottom.style.marginTop = 6;
        var status = CityUi.Row(10);
        status.style.flexGrow = 1;
        status.style.flexShrink = 1;
        bottom.Add(status);
        string id = world.Id;
        var enter = new CityUi.InkButton("Enter", () => _ = GoToCity(id), CityUi.ButtonKind.Primary, "pin", 52, 22);
        bottom.Add(enter);
        body.Add(bottom);
        card.Add(body);

        postcards[world.Id] = (status, enter);

        // Pinned slightly crooked, like postcards on a board
        var wrapper = CityUi.Inked(card);
        wrapper.style.rotate = new Rotate(index % 3 == 1 ? 0.8f : index % 2 == 0 ? -0.7f : 0.4f);
        var pin = new VisualElement();
        pin.pickingMode = PickingMode.Ignore;
        pin.style.position = Position.Absolute;
        pin.style.top = -12;
        pin.style.left = 262;
        pin.style.width = 30;
        pin.style.height = 30;
        pin.style.backgroundColor = CityUi.Red;
        CityUi.Border(pin, 3, CityUi.Ink);
        CityUi.Radius(pin, 15);
        wrapper.Add(pin);
        return wrapper;
    }

    void RefreshPicker()
    {
        if (!built || city == null) return;
        pickerTest.style.display = city.IsReady && !city.IsLive ? DisplayStyle.Flex : DisplayStyle.None;
        foreach (var world in CityLayout.Worlds)
        {
            if (!postcards.TryGetValue(world.Id, out var card)) continue;
            card.status.Clear();
            city.MyAccounts.TryGetValue(world.Id, out var account);
            if (account != null)
            {
                card.status.Add(new CityLogoView(account.Logo, 46, world.Id));
                var col = CityUi.Column(0);
                col.style.flexShrink = 1;
                col.Add(CityUi.OneLine(CityUi.Text(account.CompanyName, 20, CityUi.Ink, true)));
                col.Add(CityUi.Text($"{CityRules.Money(account.Balance)} in the bank", 16, CityUi.Green, true));
                card.status.Add(col);
                card.enter.SetText("Go");
            }
            else
            {
                card.status.Add(CityUi.Text(city.IsReady ? "You have no company here yet." : "Connecting…", 17, CityUi.Muted));
                card.enter.SetText("Enter");
            }
        }
    }
}
