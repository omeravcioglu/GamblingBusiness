using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Playing for a plot somebody owns: put the stake on the felt, play a hand of blackjack (the server deals, so
/// nobody can cheat). Win and the plot changes hands; lose and the stake goes to the owner.
/// </summary>
public partial class CityScreen
{
    VisualElement challengeBody;
    CityPlotDef challengeDef;
    bool gameBusy;

    void OpenChallengeDialog(CityPlotDef def)
    {
        if (city == null || !city.IsReady || !city.Joined || def == null) return;
        challengeDef = def;
        var dialog = CityUi.Dialog($"Play for plot #{def.Number}", 1080, CloseTopModal, out var body);
        challengeBody = body;
        var game = city.Account.Game;
        if (game != null && game.IsOpen && game.PlotId == def.Id) ShowHand(game);
        else ShowChallengeIntro();
        PushModal(dialog, () => challengeBody = null);
    }

    void ShowChallengeIntro()
    {
        var body = challengeBody;
        if (body == null) return;
        body.Clear();
        var def = challengeDef;
        var plot = city.PlotState(def);
        if (plot == null || city.IsMine(plot))
        {
            body.Add(CityUi.Text("There's nobody to play against for this plot.", 24, CityUi.InkSoft));
            return;
        }

        bool npc = CityRules.IsNpc(plot.OwnerId);
        long stake = CityRules.ChallengeStake(def, plot);
        long gets = CityRules.OwnerGets(stake);

        var top = CityUi.Row(32, Align.Center);
        var sign = CityUi.Column(8);
        sign.style.alignItems = Align.Center;
        sign.style.width = 300;
        sign.Add(new CityLogoView(plot.Logo, 130, def.CityId));
        sign.Add(CityUi.Text(plot.OwnerName, 20, CityUi.Ink, true));
        top.Add(sign);
        var ledger = CityUi.Column(0);
        ledger.style.flexGrow = 1;
        ledger.Add(CityUi.LedgerLine($"{plot.OwnerName} paid", CityRules.Money(CityRules.PlotValue(def, plot))));
        ledger.Add(CityUi.LedgerLine("Your stake (1.5x)", CityRules.Money(stake), CityUi.Red, 32));
        ledger.Add(CityUi.LedgerLine("If you win", $"plot #{def.Number} is yours", CityUi.Green));
        ledger.Add(CityUi.LedgerLine("If you lose", npc ? "the house keeps your stake" : $"{plot.OwnerName} gets {CityRules.Money(gets)}", CityUi.Red));
        top.Add(ledger);
        body.Add(top);

        body.Add(CityUi.Text("Beat the dealer at blackjack to take this plot. The dealer draws to 17. A push deals a new hand. " +
            $"A hand left alone for {CityRules.GameMinutes} minutes stands on its own.", 21));

        var table = FeltTable(150);
        table.style.flexDirection = FlexDirection.Row;
        table.style.alignItems = Align.Center;
        table.style.justifyContent = Justify.Center;
        CityUi.InkButton deal = null;
        deal = new CityUi.InkButton($"Stake {CityRules.Money(stake)} and deal", () => _ = Deal(deal), CityUi.ButtonKind.Gold, "cards", 72, 28);
        table.Add(deal);
        body.Add(table);

        string blocked = null;
        if (plot.ShieldUntil > city.Now) blocked = $"This plot is protected for {CityRules.Duration(plot.ShieldUntil - city.Now)}.";
        else if (city.Account.Balance < stake) blocked = $"You need {CityRules.Money(stake)} in your {mapView.Map.Name} bank (you have {CityRules.Money(city.Account.Balance)}).";
        if (blocked != null)
        {
            deal.SetEnabledLook(false);
            body.Add(CityUi.Text(blocked, 20, CityUi.Red));
        }

        var footer = CityUi.Row(16);
        footer.style.justifyContent = Justify.FlexEnd;
        footer.Add(new CityUi.InkButton("Not now", CloseTopModal, CityUi.ButtonKind.Paper));
        body.Add(footer);
    }

    static VisualElement FeltTable(float height)
    {
        var table = new VisualElement();
        table.style.minHeight = height;
        table.style.backgroundColor = CityUi.Felt;
        CityUi.Border(table, 10, CityUi.Wood);
        CityUi.Radius(table, 18, 26, 20, 30);
        CityUi.Padding(table, 24, 28);
        var line = new VisualElement();
        line.pickingMode = PickingMode.Ignore;
        line.style.position = Position.Absolute;
        line.style.left = 4;
        line.style.top = 4;
        line.style.right = 4;
        line.style.bottom = 4;
        CityUi.Border(line, 3, CityUi.Alpha(CityUi.Gold, 0.8f));
        CityUi.Radius(line, 10, 16, 12, 18);
        table.Add(line);
        return table;
    }

    async Task Deal(CityUi.InkButton button)
    {
        if (gameBusy) return;
        gameBusy = true;
        var result = await Run(() => city.StartChallenge(challengeDef.Id), button, "Dealing…");
        gameBusy = false;
        if (result != null && result.Ok && result.Game != null)
        {
            ShowHand(result.Game);
            AnnounceHand(result);
        }
    }

    void ShowHand(CityChallenge game)
    {
        var body = challengeBody;
        if (body == null || game == null) return;
        body.Clear();

        var message = CityUi.Text(game.Message ?? "", 30, CityUi.Ink, true);
        body.Add(message);

        var table = FeltTable(360);
        table.style.alignItems = Align.Center;
        table.style.justifyContent = Justify.Center;
        bool playing = game.Status == CityRules.GamePlaying;
        table.Add(Hand("Dealer", game.DealerCards, playing, 0));
        var gap = new VisualElement();
        gap.style.height = 18;
        table.Add(gap);
        table.Add(Hand("You", game.PlayerCards, false, 10));
        if (playing)
        {
            var row = CityUi.Row(24);
            row.style.marginTop = 22;
            CityUi.InkButton hit = null, stand = null;
            hit = new CityUi.InkButton("Hit", () => _ = Move("hit", hit, stand), CityUi.ButtonKind.Gold, "plus", 64, 28);
            stand = new CityUi.InkButton("Stand", () => _ = Move("stand", hit, stand), CityUi.ButtonKind.Gold, "hand", 64, 28);
            row.Add(hit);
            row.Add(stand);
            table.Add(row);
        }
        body.Add(table);

        var footer = CityUi.Row(16);
        footer.style.justifyContent = Justify.FlexEnd;
        if (playing)
        {
            footer.Add(CityUi.Text($"On the table: {CityRules.Money(game.Stake)} · round {game.Round} · {CityRules.Duration(game.ExpiresAt - city.Now)} left", 18, CityUi.Muted));
            footer.Add(CityUi.Spacer());
            footer.Add(new CityUi.InkButton("Close (the hand waits)", CloseTopModal, CityUi.ButtonKind.Paper, null, 56, 20));
        }
        else if (game.Status == CityRules.GameDone)
        {
            var def = challengeDef;
            footer.Add(new CityUi.InkButton($"Show plot #{def?.Number}", () =>
            {
                CloseAllModals();
                if (def != null)
                {
                    SelectPlot(def);
                    mapView.FocusOn(def);
                }
            }, CityUi.ButtonKind.Primary, "pin"));
        }
        else
        {
            footer.Add(new CityUi.InkButton("Close", CloseTopModal, CityUi.ButtonKind.Paper));
        }
        body.Add(footer);
    }

    void AnnounceHand(CityResult result)
    {
        var game = result.Game;
        if (game == null) return;
        if (game.Status == CityRules.GameDone) Toast(game.Message, true);
        else if (game.Status == CityRules.GameSettled || game.Status == CityRules.GameVoid) Toast(game.Message, game.Status == CityRules.GameVoid);
    }

    VisualElement Hand(string who, System.Collections.Generic.List<int> cards, bool hiddenCard, int seed)
    {
        var box = CityUi.Row(18);
        var label = CityUi.Column(0);
        label.style.width = 150;
        label.Add(CityUi.Text(who, 24, CityUi.Card, true));
        label.Add(CityUi.Text(cards != null && cards.Count > 0 ? CityCards.Score(cards).ToString() : "-", 40, CityUi.Gold, true));
        box.Add(label);
        var row = CityUi.Row(10);
        if (cards != null)
        {
            for (int i = 0; i < cards.Count; i++) row.Add(Card(cards[i], seed + i));
        }
        if (hiddenCard) row.Add(CardBack());
        box.Add(row);
        return box;
    }

    static VisualElement Card(int rank, int index)
    {
        bool red = index % 2 == 1;
        var card = new VisualElement();
        card.style.width = 84;
        card.style.height = 118;
        card.style.backgroundColor = CityUi.Card;
        CityUi.Border(card, 3, CityUi.Ink);
        CityUi.Radius(card, 8, 10, 8, 11);
        CityUi.Padding(card, 6, 8);
        card.Add(CityUi.Text(CityCards.RankName(rank), 30, red ? CityUi.Red : CityUi.Ink, true));
        var suit = new CityUi.IconView(red ? "heart" : "spade", 40, red ? CityUi.Red : CityUi.Ink);
        suit.style.alignSelf = Align.Center;
        suit.style.marginTop = 4;
        card.Add(suit);
        card.style.rotate = new Rotate((index % 3 - 1) * 2f);
        return card;
    }

    static VisualElement CardBack()
    {
        var card = new VisualElement();
        card.style.width = 84;
        card.style.height = 118;
        card.style.backgroundColor = CityUi.Red;
        CityUi.Border(card, 3, CityUi.Ink);
        CityUi.Radius(card, 8, 10, 8, 11);
        var inner = new VisualElement();
        inner.style.flexGrow = 1;
        CityUi.Margin(inner, 8, 8, 8, 8);
        CityUi.Border(inner, 2, CityUi.Card);
        CityUi.Radius(inner, 5);
        card.Add(inner);
        return card;
    }

    async Task Move(string move, CityUi.InkButton hit, CityUi.InkButton stand)
    {
        if (gameBusy) return;
        gameBusy = true;
        hit.SetEnabledLook(false);
        stand.SetEnabledLook(false);
        CityResult result;
        try
        {
            result = await city.ChallengeMove(move);
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            result = CityResult.Fail("Something went wrong. Try again.");
        }
        gameBusy = false;
        if (result == null || !result.Ok)
        {
            Toast(result?.Error ?? "Something went wrong.", false);
            hit.SetEnabledLook(true);
            stand.SetEnabledLook(true);
            return;
        }
        ShowHand(result.Game);
        AnnounceHand(result);
    }
}
