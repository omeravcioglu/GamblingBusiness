using UnityEngine;
using UnityEngine.UIElements;

/// <summary>The plot panel on the right: who owns it, what it's worth and earns, and what you can do with it.</summary>
public partial class CityScreen
{
    Label panelTitle;
    ScrollView panelScroll;

    VisualElement BuildSidePanel()
    {
        var card = CityUi.CardBox();
        CityUi.Padding(card, 22, 26);

        var header = CityUi.Row(12);
        panelTitle = CityUi.Text("", 40, CityUi.Ink, true);
        panelTitle.style.flexGrow = 1;
        header.Add(panelTitle);
        header.Add(CityUi.CloseButton(() => SelectPlot(null)));
        card.Add(header);

        panelScroll = new ScrollView(ScrollViewMode.Vertical);
        panelScroll.style.flexGrow = 1;
        panelScroll.style.marginTop = 10;
        panelScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        sideBody = CityUi.Column(16);
        sideBody.style.paddingRight = 8;
        panelScroll.Add(sideBody);
        card.Add(panelScroll);
        card.style.flexDirection = FlexDirection.Column;

        var wrapper = CityUi.Lifted(card);
        wrapper.style.position = Position.Absolute;
        wrapper.style.right = 24;
        wrapper.style.top = 112;
        wrapper.style.bottom = 24;
        wrapper.style.width = 580;
        wrapper.style.display = DisplayStyle.None;
        // Clicks and scrolling inside the panel must not reach the map
        wrapper.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
        wrapper.RegisterCallback<WheelEvent>(e => e.StopPropagation());
        return wrapper;
    }

    void SelectPlot(CityPlotDef def)
    {
        selected = def;
        mapView?.Select(def?.Id);
        if (sidePanel != null) sidePanel.style.display = def != null ? DisplayStyle.Flex : DisplayStyle.None;
        RefreshSidePanel();
    }

    void RefreshSidePanel()
    {
        if (!built || selected == null || city == null || ShownCity == null) return;
        float scroll = panelScroll.scrollOffset.y;
        sideBody.Clear();
        BuildPlotDetails(selected, sideBody);
        panelScroll.schedule.Execute(() => panelScroll.scrollOffset = new Vector2(0, scroll));
    }

    void BuildPlotDetails(CityPlotDef def, VisualElement body)
    {
        var map = mapView.Map;
        var district = map.DistrictsById[def.DistrictId];
        var plot = city.PlotState(def);
        bool ready = city.IsReady;
        bool joined = ready && city.Joined;
        bool mine = city.IsMine(plot);
        long now = city.Now;

        panelTitle.text = $"Plot #{def.Number}";
        body.Add(CityUi.Text($"{district.Name} · {map.Name}", 22, CityUi.InkSoft));

        var chips = CityUi.Row(8);
        chips.style.flexWrap = Wrap.Wrap;
        chips.Add(CityUi.Chip(def.SizeName, CityUi.PastelYellow, null, 17));
        if (def.Waterfront) chips.Add(CityUi.Chip("Waterfront", CityUi.PastelBlue, null, 17));
        if (def.Name != null) chips.Add(CityUi.Chip(def.Name, CityUi.Gold, "star", 17));
        body.Add(chips);

        body.Add(BuildSign(def, plot, mine));

        // The numbers
        var ledger = CityUi.Column(0);
        if (plot == null)
        {
            ledger.Add(CityUi.LedgerLine("Price from the City", CityRules.Money(def.BasePrice), CityUi.Green, 30));
        }
        else
        {
            ledger.Add(CityUi.LedgerLine(mine ? "You paid" : $"{plot.OwnerName} paid", CityRules.Money(CityRules.PlotValue(def, plot))));
            if (!mine) ledger.Add(CityUi.LedgerLine("Stake to win it", CityRules.Money(CityRules.ChallengeStake(def, plot)), CityUi.Red, 28));
            if (plot.Defended > 0) ledger.Add(CityUi.LedgerLine("Challengers beaten", plot.Defended.ToString(), CityUi.Felt));
        }
        var block = map.BlocksById[def.BlockId];
        int ownedInBlock = joined ? CityRules.OwnedInBlock(city.Districts, block, city.PlayerId) : 0;
        bool wholeBlock = mine && ownedInBlock == block.Plots.Count;
        ledger.Add(CityUi.LedgerLine("Rent", CityRules.Money(CityRules.RentPerDay(def, wholeBlock)) + " a day" + (wholeBlock ? " (block bonus!)" : "")));
        if (plot != null && plot.ShieldUntil > now)
            ledger.Add(CityUi.LedgerLine("Protected for", CityRules.Duration(plot.ShieldUntil - now), CityUi.Blue));
        body.Add(ledger);

        if (joined && !block.IsIsland) body.Add(BuildBlockProgress(block, ownedInBlock));

        if (!ready)
        {
            body.Add(CityUi.Text("Connecting…", 22, CityUi.Muted));
            return;
        }
        if (!joined)
        {
            body.Add(CityUi.Text($"Set up your company in {map.Name} to buy plots and play for them.", 22, CityUi.InkSoft));
            body.Add(new CityUi.InkButton("Set up your company", () => OpenCompanyDialog(true), CityUi.ButtonKind.Primary, "building"));
            return;
        }

        if (plot == null) BuildBuy(def, body);
        else if (mine) BuildOwnerInfo(plot, body);
        else BuildChallenge(def, plot, body, now);
    }

    VisualElement BuildSign(CityPlotDef def, CityPlot plot, bool mine)
    {
        var sign = CityUi.CardBox(CityUi.Card, 3);
        CityUi.Radius(sign, 12, 16, 11, 17);
        CityUi.Padding(sign, 16, 18);
        var row = CityUi.Row(18);
        sign.Add(row);

        if (plot == null)
        {
            var dollar = new VisualElement();
            dollar.style.width = 86;
            dollar.style.height = 86;
            dollar.style.alignItems = Align.Center;
            dollar.style.justifyContent = Justify.Center;
            CityUi.Border(dollar, 4, CityUi.Green);
            CityUi.Radius(dollar, 43);
            dollar.Add(CityUi.Text("$", 48, CityUi.Green, true));
            row.Add(dollar);
            var col = CityUi.Column(4);
            col.style.flexShrink = 1;
            col.Add(CityUi.Text("Owned by the City", 28, CityUi.Ink, true));
            col.Add(CityUi.Text("Nobody owns this plot yet. Buy it and your logo goes up here for everyone to see.", 19, CityUi.InkSoft));
            row.Add(col);
        }
        else
        {
            row.Add(new CityLogoView(plot.Logo, 96, def.CityId));
            var col = CityUi.Column(4);
            col.style.flexShrink = 1;
            col.Add(CityUi.Text(plot.OwnerName, 30, CityUi.Ink, true));
            string who = mine ? "Your company" : CityRules.IsNpc(plot.OwnerId) ? "A rival company from the start of the city" : "Another player's company";
            col.Add(CityUi.Text(who, 19, mine ? CityUi.Green : CityUi.InkSoft, mine));
            if (plot.AcquiredAt > 0)
                col.Add(CityUi.Text($"Took it for {CityRules.Money(plot.PricePaid)}, {CityUi.Ago(city.Now - plot.AcquiredAt)}", 17, CityUi.Muted));
            // Report a drawing (players' own drawings only)
            if (!mine && CityPixelLogo.IsRef(plot.Logo))
            {
                var report = CityUi.Text("Report this logo", 16, CityUi.Red, true);
                report.style.marginTop = 4;
                report.RegisterCallback<ClickEvent>(_ => ConfirmReport(def));
                col.Add(report);
            }
            row.Add(col);
        }
        return CityUi.Inked(sign);
    }

    void ConfirmReport(CityPlotDef def)
    {
        Confirm("Report this logo?",
            $"Report it if it's rude or hurtful. When {CityRules.ReportsToHide} different players report a drawing, it's taken down everywhere and the company gets a plain logo.",
            "Report it", () => _ = Run(() => city.ReportLogo(def.Id)));
    }

    VisualElement BuildBlockProgress(CityBlockDef block, int owned)
    {
        var box = CityUi.Column(8);
        var squares = CityUi.Row(6);
        foreach (var plotDef in block.Plots)
        {
            var sq = new VisualElement();
            sq.style.width = 22;
            sq.style.height = 22;
            CityUi.Border(sq, 2, CityUi.Ink);
            CityUi.Radius(sq, 4);
            bool isMine = city.IsMine(city.PlotState(plotDef));
            sq.style.backgroundColor = isMine ? CityUi.Highlight : CityUi.Card;
            if (selected != null && plotDef.Id == selected.Id) CityUi.Border(sq, 3, CityUi.Blue);
            squares.Add(sq);
        }
        box.Add(CityUi.Text($"This block: you own {owned} of {block.Plots.Count}", 20, CityUi.Ink, true));
        box.Add(squares);
        box.Add(CityUi.Text($"Own the whole block and every plot in it earns +{Mathf.RoundToInt((float)CityRules.BlockBonus * 100)}% rent.", 17, CityUi.Muted));
        return box;
    }

    void BuildBuy(CityPlotDef def, VisualElement body)
    {
        long balance = city.Account.Balance;
        body.Add(Section("Buy it from the City"));
        var button = new CityUi.InkButton($"Buy for {CityRules.Money(def.BasePrice)}", () => _ = BuyPlot(def), CityUi.ButtonKind.Primary, "tag");
        body.Add(button);
        body.Add(CityUi.Text($"Once it's yours, nobody can play for it for {CityRules.ShieldHours} hours.", 17, CityUi.Muted));
        if (balance < def.BasePrice)
        {
            button.SetEnabledLook(false);
            body.Add(NeedMoney(def.BasePrice, balance));
        }
    }

    async System.Threading.Tasks.Task BuyPlot(CityPlotDef def)
    {
        var result = await Run(() => city.BuyPlot(def.Id, def.BasePrice));
        if (result != null && result.Ok) RefreshSidePanel();
    }

    VisualElement NeedMoney(long price, long balance)
    {
        var box = CityUi.Column(8);
        box.Add(CityUi.Text($"Your {mapView.Map.Name} bank has {CityRules.Money(balance)}. Wire money in from your company (up to {CityRules.Money(CityRules.DepositCapPerDay)} a day).", 18, CityUi.Red));
        box.Add(new CityUi.InkButton("Open the bank", OpenBankDialog, CityUi.ButtonKind.Paper, "bank", 52, 20));
        return box;
    }

    static Label Section(string text)
    {
        var label = CityUi.Text(text, 26, CityUi.Ink, true);
        label.style.marginTop = 10;
        return label;
    }

    void BuildOwnerInfo(CityPlot plot, VisualElement body)
    {
        body.Add(Section("It's yours"));
        long stake = CityRules.ChallengeStake(selected, plot);
        body.Add(CityUi.Text($"Others can play blackjack for it with a {CityRules.Money(stake)} stake. Whether they win or lose, you're paid {CityRules.Money(CityRules.OwnerGets(stake))} ({CityRules.Money(stake)} minus the {Mathf.RoundToInt((float)CityRules.MarketTax * 100)}% city tax). If they win, they take the plot.", 18, CityUi.InkSoft));
        body.Add(Section("Rent"));
        body.Add(CityUi.Text("Rent builds up for up to 48 hours. Collect it from the top bar and wire it back to your company.", 18, CityUi.InkSoft));
    }

    void BuildChallenge(CityPlotDef def, CityPlot plot, VisualElement body, long now)
    {
        bool npc = CityRules.IsNpc(plot.OwnerId);
        long stake = CityRules.ChallengeStake(def, plot);
        body.Add(Section("Play for it"));
        body.Add(CityUi.Text(npc
            ? $"Put {CityRules.Money(stake)} on the table and play a hand of blackjack. Win and the plot is yours. Lose and the house keeps your stake."
            : $"Put {CityRules.Money(stake)} (1.5x what {plot.OwnerName} paid) on the table and play a hand of blackjack. Win and the plot is yours. Lose and your stake goes to {plot.OwnerName}.", 18, CityUi.InkSoft));

        var game = city.Account.Game;
        bool myHandHere = game != null && game.IsOpen && game.PlotId == def.Id;
        var play = new CityUi.InkButton(myHandHere ? "Back to the table" : $"Play for {CityRules.Money(stake)}", () => OpenChallengeDialog(def), CityUi.ButtonKind.Felt, "cards");
        body.Add(play);

        string blocked = null;
        if (myHandHere) blocked = null;
        else if (plot.ShieldUntil > now) blocked = $"Protected for {CityRules.Duration(plot.ShieldUntil - now)} (it changed hands recently).";
        else if (game != null && game.IsOpen) blocked = "Finish your other hand first.";
        else if (city.Account.Balance < stake) blocked = $"You need {CityRules.Money(stake)} in your {mapView.Map.Name} bank.";
        if (blocked != null)
        {
            play.SetEnabledLook(false);
            body.Add(CityUi.Text(blocked, 18, CityUi.Red));
            if (city.Account.Balance < stake && plot.ShieldUntil <= now)
                body.Add(new CityUi.InkButton("Open the bank", OpenBankDialog, CityUi.ButtonKind.Paper, "bank", 52, 20));
        }
    }
}
