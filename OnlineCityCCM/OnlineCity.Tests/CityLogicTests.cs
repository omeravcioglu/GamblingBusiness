namespace OnlineCity.Tests;

public class CityLogicTests
{
    const long B = 1_000_000_000L;
    const long M = 1_000_000L;

    // ---- Names, logos, money text --------------------------------------------------------------------------------

    [TestCase("Nova Holdings", "Nova Holdings")]
    [TestCase("  Kılıç   & Oğulları ", "Kılıç & Oğulları")]
    [TestCase("Grape Co", "Grape Co")]
    [TestCase("Nazik Ltd", "Nazik Ltd")]
    public void CompanyName_Accepted(string input, string expected)
    {
        Assert.That(CityRules.CleanCompanyName(input, out var error), Is.EqualTo(expected), error);
    }

    [TestCase("")]
    [TestCase("A")]
    [TestCase("F.U.C.K Holdings")]
    [TestCase("Sh1t Co")]
    [TestCase("Orospu Ltd")]
    [TestCase("This Company Name Is Far Too Long To Fit")]
    [TestCase("Bad<Tag>")]
    public void CompanyName_Rejected(string input)
    {
        Assert.That(CityRules.CleanCompanyName(input, out var error), Is.Null);
        Assert.That(error, Is.Not.Empty);
    }

    [Test]
    public void Logo_ValidatesAndRoundTrips()
    {
        Assert.That(CityLogo.Validate("1|shield|E2B13C|rocket|1D2433|NH|1", out _), Is.EqualTo("1|shield|E2B13C|rocket|1D2433|NH|1"));
        Assert.That(CityLogo.Validate("1|shield|123456|rocket|1D2433|NH|1", out _), Is.Null);
        Assert.That(CityLogo.Validate("1|blob|E2B13C|rocket|1D2433|NH|1", out _), Is.Null);
        Assert.That(CityLogo.Validate("1|circle|E2B13C|none|1D2433||1", out _), Is.Null);
        Assert.That(CityLogo.Validate("p|0123456789abcdef", out _), Is.Null, "a drawing reference isn't a preset");
        Assert.That(CityLogo.Decode("garbage").Encode(), Is.EqualTo(new CityLogo().Encode()));
        for (int i = 0; i < 50; i++)
            Assert.That(CityLogo.Validate(CityLogo.Suggest("Company " + i, i).Encode(), out var e), Is.Not.Null, e);
        foreach (string name in new[] { "Nova Holdings", "X9", "Ümit & Co" })
            Assert.That(CityLogo.Validate(CityPixelLogo.FallbackFor(name), out var e), Is.Not.Null, e);
    }

    static CityPixelLogo Smiley(byte colour = 12)
    {
        var logo = new CityPixelLogo();
        for (int y = 0; y < CityPixelLogo.Size; y++)
        {
            for (int x = 0; x < CityPixelLogo.Size; x++)
            {
                float dx = x - 15.5f, dy = y - 15.5f;
                if (dx * dx + dy * dy < 14 * 14) logo[x, y] = colour;
            }
        }
        logo[11, 11] = 1;
        logo[20, 11] = 1;
        for (int x = 10; x <= 21; x++) logo[x, 21] = 1;
        return logo;
    }

    [Test]
    public void PixelLogo_EncodesDecodesAndValidates()
    {
        var logo = Smiley();
        string code = logo.Encode();
        Assert.That(code, Does.StartWith(CityPixelLogo.CodePrefix));
        Assert.That(code.Length, Is.LessThan(900));
        var back = CityPixelLogo.Decode(code);
        Assert.That(back.Pixels, Is.EqualTo(logo.Pixels));
        Assert.That(CityPixelLogo.Validate(code, out _), Is.EqualTo(code));

        // Every palette colour survives the packing
        var all = new CityPixelLogo();
        for (int i = 0; i < all.Pixels.Length; i++) all.Pixels[i] = (byte)(i % CityPixelLogo.Palette.Length);
        Assert.That(CityPixelLogo.Decode(all.Encode()).Pixels, Is.EqualTo(all.Pixels));

        Assert.That(CityPixelLogo.Validate(new CityPixelLogo().Encode(), out var empty), Is.Null, "an empty drawing");
        Assert.That(empty, Is.Not.Empty);
        Assert.That(CityPixelLogo.Validate("px1:not base64!!", out _), Is.Null);
        Assert.That(CityPixelLogo.Validate("px1:" + Convert.ToBase64String(new byte[10]), out _), Is.Null, "wrong size");
        Assert.That(CityPixelLogo.Validate("1|circle|E2B13C|star|1D2433|AB|1", out _), Is.Null);
        Assert.That(CityPixelLogo.HashOfRef(CityPixelLogo.RefFor("abc")), Is.EqualTo("abc"));
    }

    [Test]
    public void Money_FormatsAndParses()
    {
        Assert.That(CityRules.Money(1_500_000_000), Is.EqualTo("$1.5B"));
        Assert.That(CityRules.Money(250_000_000), Is.EqualTo("$250M"));
        Assert.That(CityRules.Money(2_250_000), Is.EqualTo("$2.25M"));
        Assert.That(CityRules.Money(999), Is.EqualTo("$999"));
        Assert.That(CityRules.TryParseMoney("1.5b", out long a) && a == 1_500_000_000, Is.True);
        Assert.That(CityRules.TryParseMoney("$2,000,000", out long b) && b == 2_000_000, Is.True);
        Assert.That(CityRules.TryParseMoney("250M", out long c) && c == 250_000_000, Is.True);
        Assert.That(CityRules.TryParseMoney("-5", out _), Is.False);
        Assert.That(CityRules.TryParseMoney("abc", out _), Is.False);
    }

    [Test]
    public void Blackjack_Scores()
    {
        Assert.That(CityCards.Score(new List<int> { 1, 13 }), Is.EqualTo(21));
        Assert.That(CityCards.Score(new List<int> { 1, 1, 9 }), Is.EqualTo(21));
        Assert.That(CityCards.Score(new List<int> { 10, 12, 5 }), Is.EqualTo(25));
        Assert.That(CityCards.Score(new List<int> { 1, 5, 10 }), Is.EqualTo(16));
    }

    [Test]
    public void ChallengeStake_IsOneAndAHalfTimesWhatTheOwnerPaid()
    {
        var def = CityLayout.Get("ist").Plots.First(p => p.Size == CityPlotSize.Small);
        var plot = new CityPlot { PricePaid = 200 * M };
        Assert.That(CityRules.ChallengeStake(def, plot), Is.EqualTo(300 * M));
        Assert.That(CityRules.OwnerGets(300 * M), Is.EqualTo(285 * M));
        Assert.That(CityRules.ChallengeStake(def, new CityPlot { PricePaid = 0 }), Is.EqualTo(CityRules.RoundNice(def.BasePrice * 1.5)));
    }

    // ---- Account and bank ------------------------------------------------------------------------------------

    [Test]
    public async Task Join_CreatesAccount()
    {
        var w = new TestWorld();
        var before = await w.Logic.GetAccount("p1");
        Assert.That(before.Ok, Is.True);
        Assert.That(before.Account.Joined, Is.False);

        await w.Join("p1", "Nova Holdings");
        var account = await w.Account("p1");
        Assert.That(account.Joined, Is.True);
        Assert.That(account.CityId, Is.EqualTo("ist"));
        Assert.That(account.CompanyName, Is.EqualTo("Nova Holdings"));
        Assert.That(account.Balance, Is.EqualTo(0));
        Assert.That(account.Inbox, Has.Count.EqualTo(1));

        var bad = await w.Logic.Join("p2", "x", TestWorld.Logo);
        Assert.That(bad.Ok, Is.False);
    }

    [Test]
    public async Task Cities_AreSeparateWorlds()
    {
        // Same player, two cities: separate companies, banks and maps
        var store = new CityMemoryStore();
        var ist = new TestWorld("ist");
        var lon = new TestWorld("lon");
        await ist.JoinWith("p1", "Nova Holdings", 1 * B);
        Assert.That((await lon.Logic.GetAccount("p1")).Account.Joined, Is.False);
        await lon.JoinWith("p1", "Nova London", 300 * M);
        Assert.That(await ist.Balance("p1"), Is.EqualTo(1 * B));
        Assert.That(await lon.Balance("p1"), Is.EqualTo(300 * M));

        var istPlot = ist.FreeSmall();
        Assert.That((await lon.Logic.BuyPlot("p1", lon.Op(), istPlot.Id, istPlot.BasePrice)).Ok, Is.False, "an Istanbul plot doesn't exist in London");
        Assert.That(CityLayout.Get("lon").PlotsById.ContainsKey(istPlot.Id), Is.False);
        Assert.That(() => new CityLogic(store, "nowhere"), Throws.ArgumentException);
    }

    [Test]
    public async Task Deposit_HasADailyLimit()
    {
        var w = new TestWorld();
        await w.Join("p1", "Nova Holdings");
        Assert.That((await w.Logic.Deposit("p1", w.Op(), 600 * M)).Ok, Is.True);
        var tooMuch = await w.Logic.Deposit("p1", w.Op(), 500 * M);
        Assert.That(tooMuch.Ok, Is.False);
        Assert.That(tooMuch.Error, Does.Contain("$400M"));
        Assert.That((await w.Logic.Deposit("p1", w.Op(), 400 * M)).Ok, Is.True);
        Assert.That((await w.Logic.Deposit("p1", w.Op(), 1 * M)).Ok, Is.False);

        w.Advance(86401);
        Assert.That((await w.Logic.Deposit("p1", w.Op(), 1 * B)).Ok, Is.True);
        Assert.That(await w.Balance("p1"), Is.EqualTo(2 * B));
    }

    [Test]
    public async Task Transfers_AreSafeToRetry()
    {
        var w = new TestWorld();
        await w.Join("p1", "Nova Holdings");
        string dep = w.Op();
        Assert.That((await w.Logic.Deposit("p1", dep, 300 * M)).Ok, Is.True);
        var again = await w.Logic.Deposit("p1", dep, 300 * M);
        Assert.That(again.Ok, Is.True);
        Assert.That(await w.Balance("p1"), Is.EqualTo(300 * M));

        string wd = w.Op();
        var first = await w.Logic.Withdraw("p1", wd, 100 * M);
        var second = await w.Logic.Withdraw("p1", wd, 100 * M);
        Assert.That(first.Ok && second.Ok, Is.True);
        Assert.That(second.Amount, Is.EqualTo(100 * M), "a retry reports the same amount so the game adds it once");
        Assert.That(await w.Balance("p1"), Is.EqualTo(200 * M));

        Assert.That((await w.Logic.Withdraw("p1", w.Op(), 500 * M)).Ok, Is.False);
    }

    [Test]
    public async Task UpdateProfile_RepaintsPlots_AndHasACooldown()
    {
        var w = new TestWorld();
        await w.JoinWith("p1", "Nova Holdings", 1 * B);
        var plot = w.FreeSmall();
        Assert.That((await w.Logic.BuyPlot("p1", w.Op(), plot.Id, plot.BasePrice)).Ok, Is.True);

        var tooSoon = await w.Logic.UpdateProfile("p1", "Nova Two", TestWorld.Logo);
        Assert.That(tooSoon.Ok, Is.False);
        w.Advance(CityRules.ProfileCooldownSeconds + 1);
        var changed = await w.Logic.UpdateProfile("p1", "Nova Two", "1|square|2B5BA8|rocket|FFFDF6|N2|0");
        Assert.That(changed.Ok, Is.True, changed.Error);
        Assert.That(changed.Changes.Single().Plot.OwnerName, Is.EqualTo("Nova Two"));
        Assert.That((await w.Plot(plot)).Logo, Is.EqualTo("1|square|2B5BA8|rocket|FFFDF6|N2|0"));
    }

    // ---- Buying from the City and rent -------------------------------------------------------------------------

    [Test]
    public async Task BuyFromCity_ThenCollectRent()
    {
        var w = new TestWorld();
        await w.JoinWith("p1", "Nova Holdings", 1 * B);
        var plot = w.FreeSmall();

        var bought = await w.Logic.BuyPlot("p1", w.Op(), plot.Id, plot.BasePrice);
        Assert.That(bought.Ok, Is.True, bought.Error);
        Assert.That(bought.Changes.Single().Plot.OwnerId, Is.EqualTo("p1"));
        Assert.That(bought.Account.Balance, Is.EqualTo(1 * B - plot.BasePrice));
        Assert.That((await w.Plot(plot)).OwnerId, Is.EqualTo("p1"));
        Assert.That((await w.Plot(plot)).PricePaid, Is.EqualTo(plot.BasePrice));

        w.Advance(86400);
        var rent = await w.Logic.CollectRent("p1");
        Assert.That(rent.Ok, Is.True, rent.Error);
        Assert.That(rent.Amount, Is.EqualTo(CityRules.RentPerDay(plot, false)));

        // Uncollected rent stops growing after 48 hours
        w.Advance(5 * 86400);
        var capped = await w.Logic.CollectRent("p1");
        Assert.That(capped.Amount, Is.EqualTo(CityRules.RentPerDay(plot, false) * 2));
        Assert.That((await w.Logic.CollectRent("p1")).Amount, Is.EqualTo(0));
    }

    [Test]
    public async Task BuyPlot_ChecksPriceMoneyAndOwner()
    {
        var w = new TestWorld();
        await w.JoinWith("p1", "Nova Holdings", 50 * M);
        var plot = w.FreeSmall();

        Assert.That((await w.Logic.BuyPlot("p1", w.Op(), plot.Id, plot.BasePrice + 1)).Ok, Is.False);
        Assert.That((await w.Logic.BuyPlot("p1", w.Op(), plot.Id, plot.BasePrice)).Ok, Is.False);
        Assert.That(await w.Balance("p1"), Is.EqualTo(50 * M));
        Assert.That(await w.Plot(plot), Is.Null);

        // Plots somebody owns can only be won at the table
        await w.JoinWith("p2", "Rich Co", 1 * B);
        var npc = w.NpcPlots().First();
        var refused = await w.Logic.BuyPlot("p2", w.Op(), npc.Id, npc.BasePrice);
        Assert.That(refused.Ok, Is.False);
        Assert.That(refused.Error, Does.Contain("blackjack"));
        Assert.That(await w.Balance("p2"), Is.EqualTo(1 * B));
    }

    [Test]
    public async Task BuyPlot_RetryDoesNotChargeTwice()
    {
        var w = new TestWorld();
        await w.JoinWith("p1", "Nova Holdings", 1 * B);
        var plot = w.FreeSmall();
        string op = w.Op();
        Assert.That((await w.Logic.BuyPlot("p1", op, plot.Id, plot.BasePrice)).Ok, Is.True);
        var retry = await w.Logic.BuyPlot("p1", op, plot.Id, plot.BasePrice);
        Assert.That(retry.Ok, Is.True);
        Assert.That(await w.Balance("p1"), Is.EqualTo(1 * B - plot.BasePrice));
    }

    [Test]
    public async Task WholeBlock_EarnsBonusRent()
    {
        var w = new TestWorld();
        // A block the rival companies don't own any of
        var free = new HashSet<string>(w.Map.Districts.SelectMany(d => w.FreePlots(d.Id)).Select(p => p.Id));
        var block = w.Map.Blocks.First(b => !b.IsLandmark && b.Plots.All(p => free.Contains(p.Id)));
        long cost = block.Plots.Sum(p => p.BasePrice);

        await w.JoinWith("p1", "Nova Holdings", cost);
        foreach (var plot in block.Plots)
        {
            var r = await w.Logic.BuyPlot("p1", w.Op(), plot.Id, plot.BasePrice);
            Assert.That(r.Ok, Is.True, r.Error);
        }
        var districts = await w.Districts();
        CityRules.RentSummary(w.Map, districts, "p1", 0, w.Now, out _, out long perDay, out int plots, out _);
        Assert.That(plots, Is.EqualTo(block.Plots.Count));
        Assert.That(perDay, Is.EqualTo(block.Plots.Sum(p => CityRules.RentPerDay(p, true))));
        Assert.That(perDay, Is.GreaterThan(block.Plots.Sum(p => CityRules.RentPerDay(p, false))));
    }

    // ---- Challenges ----------------------------------------------------------------------------------------------

    [Test]
    public async Task Challenge_IsBlockedByTheShield()
    {
        var w = new TestWorld();
        await w.JoinWith("owner", "Nova Holdings", 1 * B);
        await w.JoinWith("rival", "Titan Two", 2 * B);
        var plot = w.FreeSmall();
        Assert.That((await w.Logic.BuyPlot("owner", w.Op(), plot.Id, plot.BasePrice)).Ok, Is.True);

        var shielded = await w.Logic.StartChallenge("rival", w.Op(), plot.Id);
        Assert.That(shielded.Ok, Is.False);
        Assert.That(shielded.Error, Does.Contain("protected"));
        Assert.That((await w.Logic.StartChallenge("owner", w.Op(), plot.Id)).Ok, Is.False, "not your own plot");
        Assert.That((await w.Logic.StartChallenge("rival", w.Op(), w.FreeSmall(1).Id)).Ok, Is.False, "free plots are bought, not won");
    }

    [Test]
    public async Task Challenge_Win_TakesThePlot_AndPaysTheOwner()
    {
        var w = new TestWorld();
        await w.JoinWith("owner", "Nova Holdings", 1 * B);
        await w.JoinWith("rival", "Titan Two", 2 * B);
        var plot = await w.OwnChallengeable("owner");
        long ownerBefore = await w.Balance("owner");
        long stake = CityRules.ChallengeStake(plot, await w.Plot(plot));
        Assert.That(stake, Is.EqualTo(CityRules.RoundNice(plot.BasePrice * 1.5)));

        w.Deal(10, 9, 10);         // you 19, dealer shows 10
        var start = await w.Logic.StartChallenge("rival", w.Op(), plot.Id);
        Assert.That(start.Ok, Is.True, start.Error);
        Assert.That(start.Game.Status, Is.EqualTo(CityRules.GamePlaying));
        Assert.That(start.Account.Balance, Is.EqualTo(2 * B - stake));
        Assert.That(start.Account.InPlay, Is.EqualTo(stake));

        w.Deal(7);                 // dealer draws to 17
        var stand = await w.Logic.ChallengeMove("rival", w.Op(), "stand");
        Assert.That(stand.Ok, Is.True, stand.Error);
        Assert.That(stand.Game.Status, Is.EqualTo(CityRules.GameDone), stand.Game.Message);
        Assert.That(stand.Changes.Single().Plot.OwnerId, Is.EqualTo("rival"));
        Assert.That(stand.Account.InPlay, Is.EqualTo(0));

        var taken = await w.Plot(plot);
        Assert.That(taken.OwnerId, Is.EqualTo("rival"));
        Assert.That(taken.PricePaid, Is.EqualTo(stake), "the next challenger stakes 1.5x this");
        Assert.That(taken.ShieldUntil, Is.GreaterThan(w.Now));

        var owner = await w.Account("owner");
        Assert.That(owner.Balance, Is.EqualTo(ownerBefore + CityRules.OwnerGets(stake)));
        Assert.That(owner.Stats.PlotsTaken, Is.EqualTo(1));
        Assert.That(owner.Inbox.First().Kind, Is.EqualTo(CityMessageKind.TakenOver));
        Assert.That((await w.Account("rival")).Stats.ChallengesWon, Is.EqualTo(1));
    }

    [Test]
    public async Task Challenge_Loss_PaysTheOwner_AndThePlotStays()
    {
        var w = new TestWorld();
        await w.JoinWith("owner", "Nova Holdings", 1 * B);
        await w.JoinWith("rival", "Titan Two", 2 * B);
        var plot = await w.OwnChallengeable("owner");
        long ownerBefore = await w.Balance("owner");
        long stake = CityRules.ChallengeStake(plot, await w.Plot(plot));

        w.Deal(10, 6, 10);         // you 16
        Assert.That((await w.Logic.StartChallenge("rival", w.Op(), plot.Id)).Ok, Is.True);
        w.Deal(10);                // hit: bust with 26
        var hit = await w.Logic.ChallengeMove("rival", w.Op(), "hit");
        Assert.That(hit.Ok, Is.True, hit.Error);
        Assert.That(hit.Game.Status, Is.EqualTo(CityRules.GameSettled));
        Assert.That(hit.Game.Message, Does.Contain("Bust"));
        Assert.That(hit.Account.Balance, Is.EqualTo(2 * B - stake));
        Assert.That(hit.Account.InPlay, Is.EqualTo(0));

        var kept = await w.Plot(plot);
        Assert.That(kept.OwnerId, Is.EqualTo("owner"));
        Assert.That(kept.Defended, Is.EqualTo(1));
        var owner = await w.Account("owner");
        Assert.That(owner.Balance, Is.EqualTo(ownerBefore + CityRules.OwnerGets(stake)));
        Assert.That(owner.Stats.Defended, Is.EqualTo(1));
        Assert.That(owner.Stats.DefenseIncome, Is.EqualTo(CityRules.OwnerGets(stake)));

        // Lose again by standing on a low hand: the plot keeps holding out
        w.Deal(10, 5, 10, 8);
        Assert.That((await w.Logic.StartChallenge("rival", w.Op(), plot.Id)).Ok, Is.True);
        var stand = await w.Logic.ChallengeMove("rival", w.Op(), "stand");
        Assert.That(stand.Game.Status, Is.EqualTo(CityRules.GameSettled), stand.Game.Message);
        Assert.That((await w.Plot(plot)).Defended, Is.EqualTo(2));
    }

    [Test]
    public async Task Challenge_AgainstARivalCompany_TheHouseKeepsLostStakes()
    {
        var w = new TestWorld();
        await w.JoinWith("p1", "Nova Holdings", 3 * B);
        var npc = w.NpcPlots().First(p => p.Size != CityPlotSize.Landmark);
        long stake = CityRules.ChallengeStake(npc, await w.Plot(npc));

        w.Deal(10, 6, 10, 10);     // you 16, stand, dealer 20
        Assert.That((await w.Logic.StartChallenge("p1", w.Op(), npc.Id)).Ok, Is.True);
        var lost = await w.Logic.ChallengeMove("p1", w.Op(), "stand");
        Assert.That(lost.Game.Status, Is.EqualTo(CityRules.GameSettled));
        Assert.That(lost.Game.Message, Does.Contain("house keeps"));
        Assert.That(lost.Account.Balance, Is.EqualTo(3 * B - stake));

        w.Deal(1, 13);             // a natural 21 stands at once
        w.Deal(10, 10);            // dealer: 10, then 20 (dealer card drawn in the deal, then the draw)
        var won = await w.Logic.StartChallenge("p1", w.Op(), npc.Id);
        Assert.That(won.Ok, Is.True, won.Error);
        Assert.That(won.Game.Status, Is.EqualTo(CityRules.GameDone), won.Game.Message);
        Assert.That((await w.Plot(npc)).OwnerId, Is.EqualTo("p1"));
        Assert.That((await w.Plot(npc)).PricePaid, Is.EqualTo(stake));
    }

    [Test]
    public async Task Challenge_Push_DealsANewHand()
    {
        var w = new TestWorld();
        await w.JoinWith("owner", "Nova Holdings", 1 * B);
        await w.JoinWith("rival", "Titan Two", 2 * B);
        var plot = await w.OwnChallengeable("owner");

        w.Deal(10, 8, 10);         // you 18
        Assert.That((await w.Logic.StartChallenge("rival", w.Op(), plot.Id)).Ok, Is.True);
        w.Deal(8);                 // dealer 18: push
        w.Deal(10, 9, 10);         // new hand: you 19, dealer shows 10
        var push = await w.Logic.ChallengeMove("rival", w.Op(), "stand");
        Assert.That(push.Game.Status, Is.EqualTo(CityRules.GamePlaying));
        Assert.That(push.Game.Message, Does.Contain("Push"));
        Assert.That(push.Game.Round, Is.EqualTo(2));
        w.Deal(7);
        var win = await w.Logic.ChallengeMove("rival", w.Op(), "stand");
        Assert.That(win.Game.Status, Is.EqualTo(CityRules.GameDone), win.Game.Message);
    }

    [Test]
    public async Task Challenge_OneHandAtATime_AndMovesAreSafeToRetry()
    {
        var w = new TestWorld();
        await w.JoinWith("owner", "Nova Holdings", 1 * B);
        await w.JoinWith("rival", "Titan Two", 3 * B);
        var plot = await w.OwnChallengeable("owner");
        var other = w.NpcPlots().First(p => p.Size != CityPlotSize.Landmark);

        w.Deal(10, 2, 10);         // you 12
        string start = w.Op();
        Assert.That((await w.Logic.StartChallenge("rival", start, plot.Id)).Ok, Is.True);
        long afterStart = await w.Balance("rival");
        Assert.That((await w.Logic.StartChallenge("rival", start, plot.Id)).Ok, Is.True, "the same start again is fine");
        Assert.That(await w.Balance("rival"), Is.EqualTo(afterStart), "and doesn't take the stake twice");
        Assert.That((await w.Logic.StartChallenge("rival", w.Op(), other.Id)).Ok, Is.False, "one hand at a time");

        w.Deal(3);                 // hit: 15
        string hit = w.Op();
        var first = await w.Logic.ChallengeMove("rival", hit, "hit");
        var again = await w.Logic.ChallengeMove("rival", hit, "hit");
        Assert.That(first.Game.PlayerCards, Has.Count.EqualTo(3));
        Assert.That(again.Game.PlayerCards, Has.Count.EqualTo(3), "a retried move doesn't draw again");
    }

    [Test]
    public async Task Challenge_TimesOut_TheHandStands()
    {
        var w = new TestWorld();
        await w.JoinWith("owner", "Nova Holdings", 1 * B);
        await w.JoinWith("rival", "Titan Two", 2 * B);
        var plot = await w.OwnChallengeable("owner");

        w.Deal(10, 10, 10);        // you 20
        Assert.That((await w.Logic.StartChallenge("rival", w.Op(), plot.Id)).Ok, Is.True);
        w.Advance(CityRules.GameMinutes * 60 + 5);
        w.Deal(9);                 // dealer 19
        var account = await w.Account("rival");
        Assert.That(account.Game.Status, Is.EqualTo(CityRules.GameDone), account.Game.Message);
        Assert.That(account.Game.Message, Does.Contain("Time ran out"));
        Assert.That((await w.Plot(plot)).OwnerId, Is.EqualTo("rival"));
    }

    [Test]
    public async Task Challenge_IsCalledOff_WhenThePlotChangesHandsMidGame()
    {
        var w = new TestWorld();
        await w.JoinWith("owner", "Nova Holdings", 1 * B);
        await w.JoinWith("slow", "Slow Co", 2 * B);
        await w.JoinWith("fast", "Fast Co", 2 * B);
        var plot = await w.OwnChallengeable("owner");
        long stake = CityRules.ChallengeStake(plot, await w.Plot(plot));

        w.Deal(10, 9, 10);
        Assert.That((await w.Logic.StartChallenge("slow", w.Op(), plot.Id)).Ok, Is.True);
        w.Deal(1, 13, 10, 7);      // fast: natural 21 wins at once
        var fast = await w.Logic.StartChallenge("fast", w.Op(), plot.Id);
        Assert.That(fast.Game.Status, Is.EqualTo(CityRules.GameDone), fast.Game.Message);

        w.Deal(7);                 // slow stands on 19 and beats the dealer... but the plot is gone
        var slow = await w.Logic.ChallengeMove("slow", w.Op(), "stand");
        Assert.That(slow.Game.Status, Is.EqualTo(CityRules.GameVoid), slow.Game.Message);
        Assert.That(slow.Account.Balance, Is.EqualTo(2 * B), "the stake came back");
        Assert.That((await w.Plot(plot)).OwnerId, Is.EqualTo("fast"));
        Assert.That(await w.Balance("owner"), Is.EqualTo(1 * B - plot.BasePrice + CityRules.OwnerGets(stake)));
    }

    // ---- Player drawings and reports ---------------------------------------------------------------------------

    [Test]
    public async Task Drawing_IsStoredOnce_AndShownOnPlots()
    {
        var w = new TestWorld();
        string code = Smiley().Encode();
        var join = await w.Logic.Join("p1", "Nova Holdings", null, code);
        Assert.That(join.Ok, Is.True, join.Error);
        string reference = join.Account.Logo;
        Assert.That(CityPixelLogo.IsRef(reference), Is.True);
        string hash = CityPixelLogo.HashOfRef(reference);
        var record = await w.Store.LoadLogo(hash);
        Assert.That(record.Value.Pixels, Is.EqualTo(code));
        Assert.That(record.Value.OwnerId, Is.EqualTo("p1"));

        // The same drawing by someone else points at the same record
        var second = await w.Logic.Join("p2", "Copy Cat", null, code);
        Assert.That(second.Account.Logo, Is.EqualTo(reference));

        await w.Fund("p1", 1 * B);
        var plot = w.FreeSmall();
        Assert.That((await w.Logic.BuyPlot("p1", w.Op(), plot.Id, plot.BasePrice)).Ok, Is.True);
        Assert.That((await w.Plot(plot)).Logo, Is.EqualTo(reference));

        var empty = await w.Logic.Join("p3", "Empty Co", null, new CityPixelLogo().Encode());
        Assert.That(empty.Ok, Is.False);
    }

    [Test]
    public async Task Reports_TakeADrawingDown_AfterFifteenPlayers()
    {
        var w = new TestWorld();
        string code = Smiley(7).Encode();
        Assert.That((await w.Logic.Join("artist", "Rude Art Co", null, code)).Ok, Is.True);
        await w.Fund("artist", 1 * B);
        var plotA = w.FreeSmall(0);
        var plotB = w.FreeSmall(1);
        Assert.That((await w.Logic.BuyPlot("artist", w.Op(), plotA.Id, plotA.BasePrice)).Ok, Is.True);
        Assert.That((await w.Logic.BuyPlot("artist", w.Op(), plotB.Id, plotB.BasePrice)).Ok, Is.True);
        string reference = (await w.Plot(plotA)).Logo;

        Assert.That((await w.Logic.ReportLogo("artist", plotA.Id)).Ok, Is.False, "not your own");
        Assert.That((await w.Logic.ReportLogo("stranger", plotA.Id)).Ok, Is.False, "only companies in this city can report");

        for (int i = 1; i < CityRules.ReportsToHide; i++)
        {
            await w.Join("r" + i, "Reporter " + i);
            var r = await w.Logic.ReportLogo("r" + i, plotA.Id);
            Assert.That(r.Ok, Is.True, r.Error);
            var twice = await w.Logic.ReportLogo("r" + i, plotB.Id);
            Assert.That(twice.Message, Does.Contain("already"), "one report per player");
        }
        Assert.That((await w.Plot(plotA)).Logo, Is.EqualTo(reference), "14 reports: still up");

        await w.Join("last", "Reporter 15");
        var last = await w.Logic.ReportLogo("last", plotA.Id);
        Assert.That(last.Ok, Is.True, last.Error);
        Assert.That(last.Message, Does.Contain("taken down"));

        Assert.That((await w.Plot(plotA)).Logo, Is.EqualTo(CityPixelLogo.FallbackFor("Rude Art Co")));
        Assert.That((await w.Plot(plotB)).Logo, Is.EqualTo(CityPixelLogo.FallbackFor("Rude Art Co")));
        var record = await w.Store.LoadLogo(CityPixelLogo.HashOfRef(reference));
        Assert.That(record.Value.Hidden, Is.True);
        Assert.That(record.Value.Pixels, Is.Null, "the drawing itself is gone");

        var artist = await w.Account("artist");
        Assert.That(artist.Logo, Is.EqualTo(CityPixelLogo.FallbackFor("Rude Art Co")));
        Assert.That(artist.Inbox.Any(m => m.Kind == CityMessageKind.LogoHidden), Is.True);

        w.Advance(CityRules.ProfileCooldownSeconds + 1);
        var reuse = await w.Logic.UpdateProfile("artist", "Rude Art Co", null, code);
        Assert.That(reuse.Ok, Is.False, "a taken-down drawing can't come back");
    }

    // ---- Failures and races ------------------------------------------------------------------------------------

    [Test]
    public async Task Crash_BeforeThePlotChanges_RefundsTheMoney()
    {
        FlakyStore flaky = null;
        var w = new TestWorld("ist", m => flaky = new FlakyStore(m));
        await w.JoinWith("p1", "Nova Holdings", 1 * B);
        await w.Logic.CollectRent("p1"); // creates the districts
        var plot = w.FreeSmall();

        flaky.FailNextDistrictSaves = 1;
        Assert.ThrowsAsync<IOException>(() => w.Logic.BuyPlot("p1", w.Op(), plot.Id, plot.BasePrice));
        var during = await w.Account("p1");
        Assert.That(during.InPlay, Is.EqualTo(plot.BasePrice));

        w.Advance(CityRules.HoldRecoverySeconds + 10);
        var after = await w.Account("p1");
        Assert.That(after.Balance, Is.EqualTo(1 * B));
        Assert.That(after.InPlay, Is.EqualTo(0));
        Assert.That(await w.Plot(plot), Is.Null);
    }

    [Test]
    public async Task Crash_AfterThePlotChanges_KeepsThePurchase()
    {
        FlakyStore flaky = null;
        var w = new TestWorld("ist", m => flaky = new FlakyStore(m));
        await w.JoinWith("p1", "Nova Holdings", 1 * B);
        await w.Logic.CollectRent("p1");
        var plot = w.FreeSmall();

        flaky.FailNextAccountSaves = 1;
        flaky.SkipAccountSavesBeforeFailing = 1; // the hold is saved, then the save after the purchase fails
        Assert.ThrowsAsync<IOException>(() => w.Logic.BuyPlot("p1", w.Op(), plot.Id, plot.BasePrice));
        Assert.That((await w.Plot(plot)).OwnerId, Is.EqualTo("p1"));

        w.Advance(CityRules.HoldRecoverySeconds + 10);
        var after = await w.Account("p1");
        Assert.That(after.Balance, Is.EqualTo(1 * B - plot.BasePrice));
        Assert.That(after.InPlay, Is.EqualTo(0));
        Assert.That(after.Stats.PlotsBought, Is.EqualTo(1));
    }

    [Test]
    public async Task Crash_AfterWinningOrLosing_FinishesOnTheNextCall()
    {
        FlakyStore flaky = null;
        var w = new TestWorld("ist", m => flaky = new FlakyStore(m));
        await w.JoinWith("owner", "Nova Holdings", 1 * B);
        await w.JoinWith("rival", "Titan Two", 3 * B);
        var plot = await w.OwnChallengeable("owner");
        long ownerStart = await w.Balance("owner");
        long stake = CityRules.ChallengeStake(plot, await w.Plot(plot));

        // A loss is saved, then the district save that pays the owner fails
        w.Deal(10, 6, 10, 10);
        Assert.That((await w.Logic.StartChallenge("rival", w.Op(), plot.Id)).Ok, Is.True);
        flaky.FailNextDistrictSaves = 1;
        Assert.ThrowsAsync<IOException>(() => w.Logic.ChallengeMove("rival", w.Op(), "stand"));
        var pending = await w.Account("rival");   // the next call pays the owner
        Assert.That(pending.Game.Status, Is.EqualTo(CityRules.GameSettled), pending.Game.Message);
        Assert.That(await w.Balance("owner"), Is.EqualTo(ownerStart + CityRules.OwnerGets(stake)));

        // A win is saved, then the district save that moves the plot fails
        w.Deal(10, 9, 10, 7);
        Assert.That((await w.Logic.StartChallenge("rival", w.Op(), plot.Id)).Ok, Is.True);
        flaky.FailNextDistrictSaves = 1;
        Assert.ThrowsAsync<IOException>(() => w.Logic.ChallengeMove("rival", w.Op(), "stand"));
        Assert.That((await w.Plot(plot)).OwnerId, Is.EqualTo("owner"));
        var won = await w.Account("rival");
        Assert.That(won.Game.Status, Is.EqualTo(CityRules.GameDone), won.Game.Message);
        Assert.That((await w.Plot(plot)).OwnerId, Is.EqualTo("rival"));
        Assert.That(won.Balance, Is.EqualTo(3 * B - 2 * stake));
        Assert.That(await w.Balance("owner"), Is.EqualTo(ownerStart + 2 * CityRules.OwnerGets(stake)));
    }

    [Test]
    public async Task Race_TwoBuyersForOnePlot_OnlyOneGetsIt()
    {
        FlakyStore flaky = null;
        var w = new TestWorld("ist", m => flaky = new FlakyStore(m));
        await w.JoinWith("a", "Nova Holdings", 1 * B);
        await w.JoinWith("b", "Titan Two", 1 * B);
        await w.Logic.CollectRent("a");
        flaky.Yield = true;

        for (int round = 0; round < 5; round++)
        {
            var target = w.FreeSmall(round);
            var results = await Task.WhenAll(
                Task.Run(() => w.Logic.BuyPlot("a", w.Op(), target.Id, target.BasePrice)),
                Task.Run(() => w.Logic.BuyPlot("b", w.Op(), target.Id, target.BasePrice)));
            Assert.That(results.Count(r => r.Ok), Is.EqualTo(1), string.Join(" / ", results.Select(r => r.Error)));
        }

        flaky.Yield = false;
        var districts = await w.Districts();
        CityRules.RentSummary(w.Map, districts, "a", 0, w.Now, out _, out _, out int plotsA, out _);
        CityRules.RentSummary(w.Map, districts, "b", 0, w.Now, out _, out _, out int plotsB, out _);
        Assert.That(plotsA + plotsB, Is.EqualTo(5));
        long spent = Enumerable.Range(0, 5).Sum(i => w.FreeSmall(i).BasePrice);
        Assert.That(await w.Balance("a") + await w.Balance("b"), Is.EqualTo(2 * B - spent));
    }

    [Test]
    public async Task RandomPlay_NeverCreatesOrLosesMoney()
    {
        var w = new TestWorld("lon");
        var rng = new Random(42);
        string[] players = { "p1", "p2", "p3", "p4" };
        long expected = 0;   // money that should be in players' banks and stakes
        foreach (var p in players)
        {
            await w.JoinWith(p, "Company " + p, 3 * B);
            expected += 3 * B;
        }
        var allPlots = w.Map.Plots;
        int ok = 0, refused = 0, challenges = 0;
        var finished = new HashSet<string>();

        // A challenge that ended costs the city tax (or the whole stake when a rival company owned the plot)
        void Track(CityResult result)
        {
            var game = result?.Account?.Game;
            if (game == null || game.IsOpen || !finished.Add(game.Id)) return;
            if (game.Status == CityRules.GameDone || game.Status == CityRules.GameSettled)
            {
                challenges++;
                expected -= CityRules.IsNpc(game.OwnerId) ? game.Stake : game.Stake - game.OwnerGets;
            }
        }

        for (int step = 0; step < 600; step++)
        {
            string me = players[rng.Next(players.Length)];
            var districts = await w.Districts();
            CityPlot Get(CityPlotDef d)
            {
                if (districts.TryGetValue(d.DistrictId, out var dist)) return dist.Plots.TryGetValue(d.Id, out var p) ? p : null;
                CityNpcs.SeedDistrict(w.Map, w.Map.DistrictsById[d.DistrictId]).Plots.TryGetValue(d.Id, out var seeded);
                return seeded;
            }
            var plot = allPlots[rng.Next(allPlots.Count)];
            var state = Get(plot);
            CityResult r = null;

            switch (rng.Next(9))
            {
                case 0:
                case 1:
                case 2:
                    if (state == null)
                    {
                        r = await w.Logic.BuyPlot(me, w.Op(), plot.Id, plot.BasePrice);
                        if (r.Ok) expected -= plot.BasePrice;
                    }
                    break;
                case 3:
                case 4:
                case 5:
                    if (state != null && state.OwnerId != me)
                    {
                        r = await w.Logic.StartChallenge(me, w.Op(), plot.Id);
                        for (int moves = 0; r.Ok && r.Game != null && r.Game.Status == CityRules.GamePlaying && moves < 12; moves++)
                        {
                            Track(r);
                            r = await w.Logic.ChallengeMove(me, w.Op(), rng.Next(3) == 0 ? "hit" : "stand");
                        }
                    }
                    break;
                case 6:
                    r = await w.Logic.CollectRent(me);
                    if (r.Ok) expected += r.Amount;
                    break;
                case 7:
                    r = await w.Logic.Deposit(me, w.Op(), rng.Next(1, 400) * M);
                    if (r.Ok) expected += r.Amount;
                    break;
                case 8:
                    r = await w.Logic.Withdraw(me, w.Op(), rng.Next(1, 400) * M);
                    if (r.Ok) expected -= r.Amount;
                    break;
            }

            if (r != null && !r.Ok) Assert.That(r.Error, Is.Not.Empty);
            if (r != null) { if (r.Ok) ok++; else refused++; }
            Track(r);
            w.Advance(rng.Next(0, 4 * 3600));
        }

        // Hands still on the table stand on their own
        w.Advance(CityRules.GameMinutes * 60 + 1);
        foreach (var p in players)
            Track(await w.Logic.GetAccount(p));

        // Everyone settles up: balances add up exactly, nothing stays on the table or undelivered
        long total = 0;
        foreach (var p in players)
        {
            var view = await w.Account(p);
            Assert.That(view.Balance, Is.GreaterThanOrEqualTo(0), p);
            Assert.That(view.InPlay, Is.EqualTo(0), p);
            Assert.That(w.Memory.PendingPayouts(p), Is.EqualTo(0), p);
            total += view.Balance;
        }
        var final = await w.Districts();
        int playerPlots = final.Values.Sum(d => d.Plots.Values.Count(p => players.Contains(p.OwnerId)));
        TestContext.Out.WriteLine($"ok {ok}, refused {refused}, challenges {challenges}, player plots {playerPlots}, total {total}, expected {expected}");
        Assert.That(total, Is.EqualTo(expected), "money was created or lost");
        Assert.That(ok, Is.GreaterThan(200));
        Assert.That(challenges, Is.GreaterThan(20));
        Assert.That(playerPlots, Is.GreaterThan(10));
        foreach (var d in final.Values)
        {
            Assert.That(d.Outbox, Is.Empty, "delivered payouts leave the outbox");
            foreach (var p in d.Plots.Values)
                Assert.That(CityRules.IsNpc(p.OwnerId) || players.Contains(p.OwnerId), Is.True);
        }
    }
}
