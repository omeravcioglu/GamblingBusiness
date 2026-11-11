using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// Every operation of one online city, written once: the Cloud Code module runs it against Cloud Save, and the game's
/// local test city runs it against a file. All money rules are enforced here, on the server. Each city is its own
/// world: a CityLogic works on one city's store only.
///
/// How plots change hands:
/// - Free plots are bought from the City at their base price.
/// - A plot somebody owns can only be won at the blackjack table: the challenger stakes 1.5x what the owner paid.
///   Win, and the plot is theirs and the owner is paid the stake (minus the city tax). Lose, and the owner is paid
///   the stake anyway. Rival companies the city started with keep nothing: their stakes go to the City.
///
/// How money stays correct without multi-item transactions:
/// - A player's own account is one item, saved with a write lock (retried on conflicts).
/// - Money a player spends on a district change is first set aside in their account (a hold or the challenge
///   stake), then the district is saved with a write lock. The district remembers the operation id, so money left
///   behind by a crash is later either settled (it went through) or refunded (it didn't).
/// - Money for other players is written into the district's outbox in the same save as the change, then delivered as
///   payout keys on those players. They apply each payout id once, so delivering twice never pays twice.
/// </summary>
public class CityLogic
{
    readonly ICityStore store;
    readonly ICityNotifier notifier;
    readonly Func<long> clock;
    readonly Func<int, int, int> random;
    readonly Action<string> log;
    readonly CityMap map;

    const int MaxAttempts = 6;

    public string CityId => map.CityId;
    public CityMap Map => map;

    public CityLogic(ICityStore store, string cityId, ICityNotifier notifier = null, Func<long> clock = null,
        Func<int, int, int> random = null, Action<string> log = null)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        map = CityLayout.Get(cityId) ?? throw new ArgumentException("Unknown city: " + cityId, nameof(cityId));
        this.notifier = notifier ?? new CityNullNotifier();
        this.clock = clock ?? (() => DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        if (random == null)
        {
            var rng = new Random();
            random = (min, max) => { lock (rng) return rng.Next(min, max); };
        }
        this.random = random;
        this.log = log ?? (_ => { });
    }

    long Now => clock();

    // ================================================================================================================
    // Dispatcher (the game's local test city calls operations by name, like the Cloud Code endpoints)
    // ================================================================================================================

    public Task<CityResult> Invoke(string playerId, string function, IDictionary<string, object> args)
    {
        string S(string key) => args != null && args.TryGetValue(key, out var v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : null;
        long L(string key) => long.TryParse(S(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0L;

        switch (function)
        {
            case "GetAccount": return GetAccount(playerId);
            case "Join": return Join(playerId, S("name"), S("logo"), S("pixels"));
            case "UpdateProfile": return UpdateProfile(playerId, S("name"), S("logo"), S("pixels"));
            case "Deposit": return Deposit(playerId, S("opId"), L("amount"));
            case "Withdraw": return Withdraw(playerId, S("opId"), L("amount"));
            case "CollectRent": return CollectRent(playerId);
            case "BuyPlot": return BuyPlot(playerId, S("opId"), S("plotId"), L("price"));
            case "StartChallenge": return StartChallenge(playerId, S("opId"), S("plotId"));
            case "ChallengeMove": return ChallengeMove(playerId, S("opId"), S("move"));
            case "ReportLogo": return ReportLogo(playerId, S("plotId"));
            case "MarkInboxRead": return MarkInboxRead(playerId);
            default: return Task.FromResult(CityResult.Fail("Unknown City operation: " + function));
        }
    }

    // ================================================================================================================
    // Account
    // ================================================================================================================

    public Task<CityResult> GetAccount(string playerId) => Run(async () =>
    {
        var s = await Load(playerId);
        return Ok(s);
    });

    /// <summary>Open a company in this city. logo: a CityLogo code, or pixels: a CityPixelLogo drawing (wins).</summary>
    public Task<CityResult> Join(string playerId, string companyName, string logo, string pixels = null) => Run(async () =>
    {
        string name = CityRules.CleanCompanyName(companyName, out string nameError) ?? throw Rule(nameError);
        string cleanLogo = await ResolveLogo(playerId, logo, pixels);

        var s = await Load(playerId);
        if (s.Account != null)
            return await UpdateProfileCore(s, name, cleanLogo);

        long now = Now;
        var account = new CityAccount
        {
            CompanyName = name,
            Logo = cleanLogo,
            JoinedAt = now,
            RentCollectedAt = now,
            ProfileChangedAt = now,
        };
        account.Inbox.Add(new CityMessage
        {
            Id = "welcome",
            CityId = CityId,
            Time = now,
            Kind = CityMessageKind.Info,
            Text = $"Welcome to {map.Name}! Wire money in from your company, buy plots from the City and collect their rent. Want a plot someone else owns? Beat the dealer for it.",
        });
        s.Lock = await store.SaveAccount(playerId, account, null);
        s.Account = account;
        return Ok(s, $"Welcome to {map.Name}, {name}!");
    });

    public Task<CityResult> UpdateProfile(string playerId, string companyName, string logo, string pixels = null) => Run(async () =>
    {
        string name = CityRules.CleanCompanyName(companyName, out string nameError) ?? throw Rule(nameError);
        var s = await LoadJoined(playerId);
        long wait = s.Account.ProfileChangedAt + CityRules.ProfileCooldownSeconds - Now;
        if (wait > 0) throw Rule($"You just changed your company. Try again in {CityRules.Duration(wait)}.");
        string cleanLogo = await ResolveLogo(playerId, logo, pixels);
        return await UpdateProfileCore(s, name, cleanLogo);
    });

    async Task<CityResult> UpdateProfileCore(Session s, string name, string logo)
    {
        s.Account.CompanyName = name;
        s.Account.Logo = logo;
        s.Account.ProfileChangedAt = Now;
        await SaveSession(s);

        // Repaint every plot of the company (names and logos are copied onto them for the map)
        var changes = new List<CityPlotChange>();
        var districts = await LoadAllDistricts();
        string op = "profile" + CityHash.Short(name + "|" + logo + "|" + Now, 10);
        foreach (var pair in districts)
        {
            var district = pair.Value.Value;
            if (!district.Plots.Values.Any(p => p != null && p.OwnerId == s.PlayerId)) continue;

            var commit = await CommitDistrict(pair.Key, OpKey(s.PlayerId, op), (d, b) =>
            {
                foreach (var plotPair in d.Plots.ToList())
                {
                    if (plotPair.Value == null || plotPair.Value.OwnerId != s.PlayerId) continue;
                    var copy = plotPair.Value.Clone();
                    copy.OwnerName = name;
                    copy.Logo = logo;
                    b.SetPlot(plotPair.Key, copy);
                }
            });
            if (commit.Changes != null) changes.AddRange(commit.Changes);
        }
        return Ok(s, "Company updated.", changes: changes);
    }

    /// <summary>The logo to save: a checked preset code, or a drawing stored under its hash (a taken-down one is refused).</summary>
    async Task<string> ResolveLogo(string playerId, string logo, string pixels)
    {
        if (string.IsNullOrEmpty(pixels))
            return CityLogo.Validate(logo, out string logoError) ?? throw Rule(logoError);

        string code = CityPixelLogo.Validate(pixels, out string pixelError) ?? throw Rule(pixelError);
        string hash = CityPixelLogo.HashOf(code);
        for (int attempt = 0; ; attempt++)
        {
            var existing = await store.LoadLogo(hash);
            if (existing?.Value != null)
            {
                if (existing.Value.Hidden)
                    throw Rule("That drawing was taken down after reports from other players. Draw something else.");
                return CityPixelLogo.RefFor(hash);
            }
            try
            {
                await store.SaveLogo(new CityLogoRecord { Hash = hash, Pixels = code, OwnerId = playerId, Time = Now }, null);
                return CityPixelLogo.RefFor(hash);
            }
            catch (CityConflictException)
            {
                if (attempt + 1 >= MaxAttempts) throw;
            }
        }
    }

    /// <summary>The account's logo, switched to a plain one first if its drawing was taken down (saved with the session).</summary>
    async Task UseUsableLogo(Session s)
    {
        string hash = CityPixelLogo.HashOfRef(s.Account.Logo);
        if (hash == null) return;
        var record = await store.LoadLogo(hash);
        if (record?.Value != null && !record.Value.Hidden) return;
        s.Account.Logo = CityPixelLogo.FallbackFor(s.Account.CompanyName);
    }

    public Task<CityResult> MarkInboxRead(string playerId) => Run(async () =>
    {
        var s = await LoadJoined(playerId);
        s.Account.InboxSeenAt = Now;
        await SaveSession(s);
        return Ok(s);
    });

    // ================================================================================================================
    // Reports: a drawing reported by enough different players is taken down everywhere
    // ================================================================================================================

    public Task<CityResult> ReportLogo(string playerId, string plotId) => Run(async () =>
    {
        var def = PlotDef(plotId);
        var s = await LoadJoined(playerId);
        var districts = await LoadDistrictsById(new[] { def.DistrictId });
        CityPlot plot = null;
        if (districts.TryGetValue(def.DistrictId, out var district)) district.Plots.TryGetValue(plotId, out plot);
        if (plot == null) throw Rule("Nobody owns this plot.");
        if (plot.OwnerId == playerId) throw Rule("That's your own logo.");
        string hash = CityPixelLogo.HashOfRef(plot.Logo) ?? throw Rule("Only drawn logos can be reported.");

        bool hide = false;
        for (int attempt = 0; ; attempt++)
        {
            var loaded = await store.LoadReport(hash);
            var report = loaded?.Value ?? new CityReportRecord { Hash = hash };
            if (report.Reporters == null) report.Reporters = new List<string>();
            if (report.Handled)
            {
                // Taken down already, but this plot still shows it (the take-down was interrupted): finish it
                var finished = await TakeDown(hash);
                return Ok(s, "That logo has already been taken down.", changes: finished);
            }
            if (report.Reporters.Contains(playerId)) return Ok(s, "You already reported this logo. Thanks!");

            report.Reporters.Add(playerId);
            hide = report.Reporters.Count >= CityRules.ReportsToHide;
            report.Handled = hide;
            try
            {
                await store.SaveReport(report, loaded?.Lock);
                break;
            }
            catch (CityConflictException)
            {
                if (attempt + 1 >= MaxAttempts) throw;
            }
        }

        if (!hide) return Ok(s, "Thanks, the logo was reported.");
        var changes = await TakeDown(hash);
        return Ok(s, "Thanks. That logo has been taken down.", changes: changes);
    });

    /// <summary>Hide a drawing for good: it can't be used again, every plot showing it gets a plain logo, its owners hear why.</summary>
    async Task<List<CityPlotChange>> TakeDown(string hash)
    {
        string reference = CityPixelLogo.RefFor(hash);
        var owners = new HashSet<string>();

        for (int attempt = 0; ; attempt++)
        {
            var loaded = await store.LoadLogo(hash);
            var record = loaded?.Value ?? new CityLogoRecord { Hash = hash, Time = Now };
            if (record.OwnerId != null) owners.Add(record.OwnerId);
            if (record.Hidden && loaded != null) break;
            record.Hidden = true;
            record.Pixels = null;
            try
            {
                await store.SaveLogo(record, loaded?.Lock);
                break;
            }
            catch (CityConflictException)
            {
                if (attempt + 1 >= MaxAttempts) throw;
            }
        }

        var changes = new List<CityPlotChange>();
        var districts = await LoadAllDistricts();
        foreach (var pair in districts)
        {
            if (!pair.Value.Value.Plots.Values.Any(p => p != null && p.Logo == reference)) continue;
            var commit = await CommitDistrict(pair.Key, "takedown:" + hash, (d, b) =>
            {
                foreach (var plotPair in d.Plots.ToList())
                {
                    if (plotPair.Value == null || plotPair.Value.Logo != reference) continue;
                    var copy = plotPair.Value.Clone();
                    copy.Logo = CityPixelLogo.FallbackFor(copy.OwnerName);
                    b.SetPlot(plotPair.Key, copy);
                    owners.Add(copy.OwnerId);
                }
            });
            changes.AddRange(commit.Changes);
        }

        // Owners switch their company to a plain logo when this arrives (same id for everyone: applied once each)
        foreach (string owner in owners)
        {
            if (string.IsNullOrEmpty(owner) || CityRules.IsNpc(owner)) continue;
            var payout = new CityPayout
            {
                Id = "hide" + hash,
                PlayerId = owner,
                Amount = 0,
                Kind = CityMessageKind.LogoHidden,
                Text = "Other players reported your drawn logo, so it was taken down. Your plots show a plain logo now - draw a new one from your company page.",
                Logo = reference,
                Time = Now,
            };
            await store.SavePayout(payout);
            await Announce(new List<CityPlotChange>(), new List<CityPayout> { payout });
        }
        return changes;
    }

    // ================================================================================================================
    // Bank
    // ================================================================================================================

    /// <summary>Money wired in from the single-player company (the game already took it from the company).</summary>
    public Task<CityResult> Deposit(string playerId, string opId, long amount) => Run(async () =>
    {
        CheckOp(opId);
        var s = await LoadJoined(playerId);
        var done = s.Account.RecentDeposits.FirstOrDefault(d => d.Id == opId);
        if (done != null || s.Account.RecentOps.Contains(opId))
            return Ok(s, "Already wired.", amount: done?.Amount ?? 0);

        if (amount < CityRules.MinTransfer)
            throw Rule($"Wire at least {CityRules.Money(CityRules.MinTransfer)}.");

        long now = Now;
        long left = DepositLeft(s.Account, now, out long resetAt);
        if (amount > left)
        {
            throw Rule(left <= 0
                ? $"You've wired the daily maximum of {CityRules.Money(CityRules.DepositCapPerDay)} into {map.Name}. More room opens in {CityRules.Duration(resetAt - now)}."
                : $"You can wire {CityRules.Money(left)} more into {map.Name} today (the limit is {CityRules.Money(CityRules.DepositCapPerDay)} a day).");
        }
        if (s.Account.Balance + amount > CityRules.MaxBalance)
            throw Rule("Your bank account here is full.");

        s.Account.Balance += amount;
        s.Account.RecentDeposits.Add(new CityStamp { Id = opId, Time = now, Amount = amount });
        s.Account.RecentOps.Add(opId);
        s.Account.Stats.Deposited += amount;
        await SaveSession(s);
        return Ok(s, $"{CityRules.Money(amount)} wired into your {map.Name} bank.", amount: amount);
    });

    /// <summary>Money wired back to the company (the game adds it to the company once this succeeds).</summary>
    public Task<CityResult> Withdraw(string playerId, string opId, long amount) => Run(async () =>
    {
        CheckOp(opId);
        var s = await LoadJoined(playerId);
        var done = s.Account.RecentWithdrawals.FirstOrDefault(w => w.Id == opId);
        if (done != null)
            return Ok(s, "Already wired.", amount: done.Amount);
        if (s.Account.RecentOps.Contains(opId))
            return Ok(s, "Already wired.", amount: 0);

        if (amount < CityRules.MinTransfer)
            throw Rule($"Wire at least {CityRules.Money(CityRules.MinTransfer)}.");
        if (amount > s.Account.Balance)
            throw Rule($"Your {map.Name} bank only has {CityRules.Money(s.Account.Balance)}.");

        s.Account.Balance -= amount;
        s.Account.RecentWithdrawals.Add(new CityStamp { Id = opId, Time = Now, Amount = amount });
        s.Account.RecentOps.Add(opId);
        s.Account.Stats.Withdrawn += amount;
        await SaveSession(s);
        return Ok(s, $"{CityRules.Money(amount)} wired to your company.", amount: amount);
    });

    public Task<CityResult> CollectRent(string playerId) => Run(async () =>
    {
        var s = await LoadJoined(playerId);
        var districts = await LoadAllDistricts();
        long now = Now;
        CityRules.RentSummary(map, Values(districts), playerId, s.Account.RentCollectedAt, now,
            out long ready, out _, out int plots, out _);
        if (plots == 0) throw Rule("Buy a plot first - plots earn rent every day.");
        if (ready <= 0) return Ok(s, "No rent to collect yet.");

        s.Account.Balance = CityRules.ClampBalance(s.Account.Balance + ready);
        s.Account.RentCollectedAt = now;
        s.Account.Stats.RentEarned += ready;
        await SaveSession(s);
        return Ok(s, $"Collected {CityRules.Money(ready)} in rent.", amount: ready);
    });

    // ================================================================================================================
    // Buying from the City
    // ================================================================================================================

    /// <summary>Buy a free plot from the City at its base price. expectedPrice guards against a price that changed.</summary>
    public Task<CityResult> BuyPlot(string playerId, string opId, string plotId, long expectedPrice) => Run(async () =>
    {
        CheckOp(opId);
        var def = PlotDef(plotId);
        var s = await LoadJoined(playerId);
        if (s.Account.RecentOps.Contains(opId)) return Ok(s, "Done.");

        // A retry of a call that died after setting the money aside buys at the amount already held
        var held = s.Account.Holds.FirstOrDefault(h => h.OpId == opId);
        long price;
        if (held != null)
        {
            price = held.Amount;
        }
        else
        {
            var districts = await LoadAllDistricts();
            districts[def.DistrictId].Value.Plots.TryGetValue(plotId, out var current);
            if (current != null && current.OwnerId == playerId) throw Rule("You already own this plot.");
            if (current != null) throw Rule($"{current.OwnerName} owns this plot. Challenge them for it at the blackjack table.");
            CheckPlotLimit(districts, playerId);
            price = def.BasePrice;
            if (expectedPrice > 0 && expectedPrice != price)
                throw Rule($"The price is {CityRules.Money(price)}. Take another look.");
        }

        await UseUsableLogo(s);
        var commit = await WithHold(s, opId, def.DistrictId, plotId, price, "buy", (d, b) =>
        {
            d.Plots.TryGetValue(plotId, out var now);
            if (now != null) throw Rule("Someone else just bought this plot. Take another look.");
            b.SetPlot(plotId, NewOwnerPlot(s, price));
        }, account => account.Stats.PlotsBought++);

        return Ok(s, $"Plot #{def.Number} is yours!", changes: commit.Changes);
    });

    // ================================================================================================================
    // Challenges: blackjack for somebody's plot
    // ================================================================================================================

    /// <summary>Put 1.5x what the owner paid on the table and get dealt a hand.</summary>
    public Task<CityResult> StartChallenge(string playerId, string opId, string plotId) => Run(async () =>
    {
        CheckOp(opId);
        var def = PlotDef(plotId);
        var s = await LoadJoined(playerId);
        var game = s.Account.Game;
        if (game != null && game.Id == opId) return await AfterMove(s);
        if (game != null && game.IsOpen)
        {
            string where = map.PlotsById.TryGetValue(game.PlotId ?? "", out var other) ? $" for plot #{other.Number}" : "";
            throw Rule($"Finish your hand{where} first.");
        }

        var districts = await LoadAllDistricts();
        if (!districts[def.DistrictId].Value.Plots.TryGetValue(plotId, out var plot) || plot == null)
            throw Rule("Nobody owns this plot yet - buy it straight from the City.");
        if (plot.OwnerId == playerId) throw Rule("That's your own plot.");

        long now = Now;
        if (plot.ShieldUntil > now)
            throw Rule($"This plot changed hands recently. It's protected for {CityRules.Duration(plot.ShieldUntil - now)}.");
        CheckPlotLimit(districts, playerId);

        long stake = CityRules.ChallengeStake(def, plot);
        if (s.Account.Balance < stake)
            throw Rule($"Challenging for this plot takes a {CityRules.Money(stake)} stake. Your {map.Name} bank has {CityRules.Money(s.Account.Balance)}.");

        s.Account.Balance -= stake;
        game = new CityChallenge
        {
            Id = opId,
            PlotId = plotId,
            DistrictId = def.DistrictId,
            Status = CityRules.GamePlaying,
            OwnerId = plot.OwnerId,
            OwnerName = plot.OwnerName,
            Stake = stake,
            OwnerGets = CityRules.OwnerGets(stake),
            StartedAt = now,
            ExpiresAt = now + CityRules.GameMinutes * 60L,
        };
        DealBlackjack(game);
        s.Account.Game = game;
        await SaveSession(s);
        return await AfterMove(s);
    });

    /// <summary>Play the hand: "hit" or "stand". The server deals, so nobody can cheat.</summary>
    public Task<CityResult> ChallengeMove(string playerId, string opId, string move) => Run(async () =>
    {
        CheckOp(opId);
        var s = await LoadJoined(playerId);
        var game = s.Account.Game;
        if (game == null || !game.IsOpen) return Ok(s, game?.Message, game: game);
        if (s.Account.RecentOps.Contains(opId) || game.Status != CityRules.GamePlaying) return await AfterMove(s);

        if (move == "hit")
        {
            game.PlayerCards.Add(Draw());
            int score = CityCards.Score(game.PlayerCards);
            if (score > 21)
            {
                game.Status = CityRules.GameLost;
                game.Message = $"Bust with {score}.";
            }
            else if (score == 21)
            {
                ResolveBlackjack(game, true);
            }
            else
            {
                game.Message = $"You have {score}. Hit or stand?";
            }
        }
        else if (move == "stand")
        {
            ResolveBlackjack(game, true);
        }
        else
        {
            throw Rule("Hit or stand?");
        }

        s.Account.RecentOps.Add(opId);
        await SaveSession(s);
        return await AfterMove(s);
    });

    async Task<CityResult> AfterMove(Session s)
    {
        var game = s.Account.Game;
        List<CityPlotChange> changes = null;
        if (game != null && game.Status == CityRules.GameWon) changes = await ClaimPlot(s);
        else if (game != null && game.Status == CityRules.GameLost) changes = await PayOwner(s);
        return Ok(s, s.Account.Game?.Message, game: s.Account.Game, changes: changes);
    }

    /// <summary>The challenger won: move the plot (if the same owner still has it) and pay the old owner.</summary>
    async Task<List<CityPlotChange>> ClaimPlot(Session s)
    {
        var game = s.Account.Game;
        var def = PlotDef(game.PlotId);
        await UseUsableLogo(s);
        CommitResult commit = null;
        bool voided = false;
        try
        {
            commit = await CommitDistrict(game.DistrictId, OpKey(s.PlayerId, game.Id), (d, b) =>
            {
                if (!d.Plots.TryGetValue(game.PlotId, out var plot) || plot == null || plot.OwnerId != game.OwnerId)
                    throw Rule("changed hands");
                b.SetPlot(game.PlotId, NewOwnerPlot(s, game.Stake));
                b.Pay(game.OwnerId, game.OwnerGets, CityMessageKind.TakenOver,
                    $"{s.Account.CompanyName} beat the dealer at your plot #{def.Number} and took it. You got {CityRules.Money(game.OwnerGets)} ({CityRules.Money(game.Stake)} stake minus the city tax).",
                    game.PlotId);
            });
        }
        catch (CityRuleException)
        {
            voided = true;
        }

        for (int attempt = 0; ; attempt++)
        {
            var g = s.Account.Game;
            if (g != null && g.Id == game.Id && g.Status == CityRules.GameWon)
            {
                if (voided)
                {
                    g.Status = CityRules.GameVoid;
                    s.Account.Balance = CityRules.ClampBalance(s.Account.Balance + g.Stake);
                    g.Message = "The plot changed hands during your hand, so the challenge was called off. Your stake came back.";
                }
                else
                {
                    g.Status = CityRules.GameDone;
                    s.Account.Stats.ChallengesWon++;
                    string paid = CityRules.IsNpc(g.OwnerId) ? "" : $" {g.OwnerName} was paid {CityRules.Money(g.OwnerGets)}.";
                    g.Message = $"{g.Message} You won plot #{def.Number}!{paid}";
                }
            }
            try
            {
                await SaveSession(s);
                break;
            }
            catch (CityConflictException)
            {
                if (attempt + 1 >= MaxAttempts) throw;
                await ReloadRaw(s);
            }
        }
        return commit?.Changes ?? new List<CityPlotChange>();
    }

    /// <summary>The challenger lost: the owner is paid the stake (the plot remembers it held out).</summary>
    async Task<List<CityPlotChange>> PayOwner(Session s)
    {
        var game = s.Account.Game;
        var def = PlotDef(game.PlotId);
        var commit = await CommitDistrict(game.DistrictId, OpKey(s.PlayerId, game.Id + ":lost"), (d, b) =>
        {
            if (d.Plots.TryGetValue(game.PlotId, out var plot) && plot != null && plot.OwnerId == game.OwnerId)
            {
                var copy = plot.Clone();
                copy.Defended++;
                b.SetPlot(game.PlotId, copy);
            }
            b.Pay(game.OwnerId, game.OwnerGets, CityMessageKind.Defended,
                $"{s.Account.CompanyName} lost a hand of blackjack for your plot #{def.Number}. Their {CityRules.Money(game.Stake)} stake is yours: {CityRules.Money(game.OwnerGets)} after the city tax.",
                game.PlotId);
        });

        for (int attempt = 0; ; attempt++)
        {
            var g = s.Account.Game;
            if (g != null && g.Id == game.Id && g.Status == CityRules.GameLost)
            {
                g.Status = CityRules.GameSettled;
                s.Account.Stats.ChallengesLost++;
                g.Message = CityRules.IsNpc(g.OwnerId)
                    ? $"{g.Message} The house keeps your {CityRules.Money(g.Stake)} stake."
                    : $"{g.Message} Your {CityRules.Money(g.Stake)} stake went to {g.OwnerName}.";
            }
            try
            {
                await SaveSession(s);
                break;
            }
            catch (CityConflictException)
            {
                if (attempt + 1 >= MaxAttempts) throw;
                await ReloadRaw(s);
            }
        }
        return commit.Changes;
    }

    int Draw() => random(1, 14);

    void DealBlackjack(CityChallenge game)
    {
        game.Round++;
        game.PlayerCards = new List<int> { Draw(), Draw() };
        game.DealerCards = new List<int> { Draw() };
        game.Message = $"You have {CityCards.Score(game.PlayerCards)}. Hit or stand?";
        if (CityCards.Score(game.PlayerCards) == 21) ResolveBlackjack(game, true); // A natural stands at once
    }

    /// <summary>The dealer draws to 17. A push deals a new hand (or, when the hand stood on its own, calls it off).</summary>
    void ResolveBlackjack(CityChallenge game, bool redealOnPush)
    {
        while (CityCards.Score(game.DealerCards) < 17) game.DealerCards.Add(Draw());
        int player = CityCards.Score(game.PlayerCards);
        int dealer = CityCards.Score(game.DealerCards);
        if (dealer > 21 || player > dealer)
        {
            game.Status = CityRules.GameWon;
            game.Message = dealer > 21 ? $"The dealer busts with {dealer}!" : $"{player} beats {dealer}!";
        }
        else if (player < dealer)
        {
            game.Status = CityRules.GameLost;
            game.Message = $"{dealer} beats {player}.";
        }
        else if (!redealOnPush)
        {
            game.Status = CityRules.GameVoid;
            game.Message = $"Push at {player}, and your time ran out. Your stake came back.";
        }
        else if (game.Round >= CityRules.MaxPushes)
        {
            game.Status = CityRules.GameLost;
            game.Message = "Push after push: the house wins this one.";
        }
        else
        {
            DealBlackjack(game);
            game.Message = $"Push at {player}. New hand: you have {CityCards.Score(game.PlayerCards)}.";
        }
    }

    // ================================================================================================================
    // Loading a player (and tidying their account)
    // ================================================================================================================

    class Session
    {
        public string PlayerId;
        public CityAccount Account;
        public string Lock;
    }

    async Task<Session> LoadJoined(string playerId)
    {
        var s = await Load(playerId);
        if (s.Account == null) throw Rule($"Set up your company in {map.Name} first.");
        return s;
    }

    /// <summary>
    /// Load a player's account and bring it up to date: apply payouts that arrived, settle holds and challenges left
    /// behind by failed calls, stand hands whose time ran out. Saves the account when anything changed.
    /// </summary>
    async Task<Session> Load(string playerId)
    {
        if (string.IsNullOrEmpty(playerId)) throw Rule("Not signed in.");

        for (int attempt = 0; ; attempt++)
        {
            var data = await store.LoadPlayer(playerId);
            var s = new Session { PlayerId = playerId, Account = data.Account, Lock = data.Lock };
            if (s.Account == null) return s;

            var account = s.Account;
            account.EnsureLists();
            long now = Now;
            bool changed = false;
            var handled = new HashSet<string>();

            // 1. Apply payouts
            foreach (var payout in (data.Payouts ?? new List<CityPayout>()).OrderBy(p => p.Time))
            {
                if (payout == null || string.IsNullOrEmpty(payout.Id) || !handled.Add(payout.Id)) continue;
                if (account.RecentOps.Contains(payout.Id)) continue;
                ApplyPayout(account, payout);
                changed = true;
            }

            // 2. Holds whose call never finished: dropped if the district change went through, refunded if not
            var staleHolds = account.Holds.Where(h => now - h.Time > CityRules.HoldRecoverySeconds).ToList();
            if (staleHolds.Count > 0)
            {
                var districts = await LoadDistrictsById(staleHolds.Select(h => h.DistrictId));
                foreach (var hold in staleHolds)
                {
                    districts.TryGetValue(hold.DistrictId, out var district);
                    account.Holds.Remove(hold);
                    if (district != null && district.RecentOps.Contains(OpKey(playerId, hold.OpId)))
                    {
                        account.RecentOps.Add(hold.OpId);
                        if (hold.Kind == "buy") account.Stats.PlotsBought++;
                    }
                    else
                    {
                        account.Balance = CityRules.ClampBalance(account.Balance + hold.Amount);
                    }
                    changed = true;
                }
            }

            // 3. A challenge left open: finished if the district shows it, stood on its own when time ran out
            bool claim = false, payOwner = false;
            var game = account.Game;
            if (game != null && game.IsOpen)
            {
                var districts = await LoadDistrictsById(new[] { game.DistrictId });
                districts.TryGetValue(game.DistrictId, out var district);
                if (district != null && district.RecentOps.Contains(OpKey(playerId, game.Id)))
                {
                    game.Status = CityRules.GameDone;
                    game.Message = "You won! The plot is yours.";
                    account.Stats.ChallengesWon++;
                    changed = true;
                }
                else if (district != null && district.RecentOps.Contains(OpKey(playerId, game.Id + ":lost")))
                {
                    game.Status = CityRules.GameSettled;
                    game.Message = $"The dealer won. Your {CityRules.Money(game.Stake)} stake went to {game.OwnerName}.";
                    account.Stats.ChallengesLost++;
                    changed = true;
                }
                else
                {
                    if (game.Status == CityRules.GamePlaying && now > game.ExpiresAt)
                    {
                        ResolveBlackjack(game, false);
                        game.Message = "Time ran out, so your hand stood. " + game.Message;
                        if (game.Status == CityRules.GameVoid)
                            account.Balance = CityRules.ClampBalance(account.Balance + game.Stake);
                        changed = true;
                    }
                    claim = game.Status == CityRules.GameWon;
                    payOwner = game.Status == CityRules.GameLost;
                }
            }

            changed |= Tidy(account, now);
            if (changed)
            {
                try
                {
                    s.Lock = await store.SaveAccount(playerId, account, s.Lock);
                }
                catch (CityConflictException)
                {
                    if (attempt + 1 >= MaxAttempts) throw;
                    continue;
                }
            }

            // Payout keys can go once they're recorded as applied
            foreach (string id in handled)
            {
                try { await store.DeletePayout(playerId, id); }
                catch (Exception e) { log($"[City] Couldn't delete payout {id} of {playerId}: {e.Message}"); }
            }

            if (claim) await ClaimPlot(s);
            else if (payOwner) await PayOwner(s);
            return s;
        }
    }

    void ApplyPayout(CityAccount account, CityPayout payout)
    {
        account.Balance = CityRules.ClampBalance(account.Balance + Math.Max(0L, payout.Amount));
        account.RecentOps.Add(payout.Id);

        switch (payout.Kind)
        {
            case CityMessageKind.TakenOver:
                account.Stats.PlotsTaken++;
                account.Stats.TakeoverIncome += Math.Max(0L, payout.Amount);
                break;
            case CityMessageKind.Defended:
                account.Stats.Defended++;
                account.Stats.DefenseIncome += Math.Max(0L, payout.Amount);
                break;
            case CityMessageKind.LogoHidden:
                if (payout.Logo != null && account.Logo == payout.Logo)
                    account.Logo = CityPixelLogo.FallbackFor(account.CompanyName);
                break;
        }

        if (!string.IsNullOrEmpty(payout.Text))
        {
            account.Inbox.Add(new CityMessage
            {
                Id = payout.Id,
                CityId = CityId,
                Time = payout.Time,
                Kind = payout.Kind,
                Text = payout.Text,
                PlotId = payout.PlotId,
                Amount = payout.Amount,
            });
        }
    }

    /// <summary>Keep the account's lists short. True when something was removed.</summary>
    static bool Tidy(CityAccount account, long now)
    {
        int before = account.RecentDeposits.Count + account.RecentWithdrawals.Count + account.RecentOps.Count + account.Inbox.Count;

        account.RecentDeposits.RemoveAll(d => d.Time <= now - 86400L);
        if (account.RecentWithdrawals.Count > 30)
            account.RecentWithdrawals.RemoveRange(0, account.RecentWithdrawals.Count - 30);
        Trim(account.RecentOps, CityRules.AccountOpsSize);
        if (account.Inbox.Count > CityRules.InboxSize)
            account.Inbox = account.Inbox.OrderByDescending(m => m.Time).Take(CityRules.InboxSize).OrderBy(m => m.Time).ToList();

        int after = account.RecentDeposits.Count + account.RecentWithdrawals.Count + account.RecentOps.Count + account.Inbox.Count;
        return after != before;
    }

    async Task SaveSession(Session s)
    {
        s.Lock = await store.SaveAccount(s.PlayerId, s.Account, s.Lock);
    }

    /// <summary>Read the account again after a conflict, without the tidy-up (it could settle what we're settling).</summary>
    async Task ReloadRaw(Session s)
    {
        var fresh = await store.LoadPlayer(s.PlayerId);
        if (fresh.Account == null) throw Rule($"Set up your company in {map.Name} first.");
        s.Account = fresh.Account;
        s.Account.EnsureLists();
        s.Lock = fresh.Lock;
    }

    static long DepositLeft(CityAccount account, long now, out long resetAt)
    {
        long used = 0;
        long oldest = long.MaxValue;
        foreach (var d in account.RecentDeposits)
        {
            if (d.Time <= now - 86400L) continue;
            used += d.Amount;
            oldest = Math.Min(oldest, d.Time);
        }
        resetAt = oldest == long.MaxValue ? now : oldest + 86400L;
        return Math.Max(0L, CityRules.DepositCapPerDay - used);
    }

    // ================================================================================================================
    // Holds and district commits
    // ================================================================================================================

    /// <summary>
    /// Set money aside in the player's account, commit the district change, then drop the hold. When the change
    /// breaks a rule the money comes straight back; when the call dies in between, Load settles the hold later.
    /// </summary>
    async Task<CommitResult> WithHold(Session s, string opId, string districtId, string plotId, long amount, string kind,
        Action<CityDistrict, CommitBuilder> mutate, Action<CityAccount> onSuccess)
    {
        var existing = s.Account.Holds.FirstOrDefault(h => h.OpId == opId);
        if (existing == null)
        {
            if (amount <= 0) throw Rule("Pick an amount.");
            if (s.Account.Balance < amount)
                throw Rule($"You need {CityRules.Money(amount)} in your {map.Name} bank (you have {CityRules.Money(s.Account.Balance)}). Wire more in from your company.");
            s.Account.Balance -= amount;
            s.Account.Holds.Add(new CityHold { OpId = opId, DistrictId = districtId, PlotId = plotId, Amount = amount, Kind = kind, Time = Now });
            await SaveSession(s);
        }

        CommitResult commit;
        try
        {
            commit = await CommitDistrict(districtId, OpKey(s.PlayerId, opId), mutate);
        }
        catch (CityRuleException)
        {
            await SettleHold(s, opId, true, null);
            throw;
        }

        await SettleHold(s, opId, false, onSuccess);
        return commit;
    }

    async Task SettleHold(Session s, string opId, bool refund, Action<CityAccount> onSuccess)
    {
        for (int attempt = 0; ; attempt++)
        {
            var hold = s.Account.Holds.FirstOrDefault(h => h.OpId == opId);
            if (hold == null) return; // Settled by someone else already
            s.Account.Holds.Remove(hold);
            if (refund)
            {
                s.Account.Balance = CityRules.ClampBalance(s.Account.Balance + hold.Amount);
            }
            else
            {
                onSuccess?.Invoke(s.Account);
                s.Account.RecentOps.Add(opId);
            }

            try
            {
                await SaveSession(s);
                return;
            }
            catch (CityConflictException)
            {
                if (attempt + 1 >= MaxAttempts) throw;
                await ReloadRaw(s);
            }
        }
    }

    class CommitResult
    {
        public CityDistrict District;
        public bool AlreadyDone;
        public List<CityPlotChange> Changes = new List<CityPlotChange>();
        public List<CityPayout> Payouts = new List<CityPayout>();
    }

    /// <summary>Collects what one district save changes and pays.</summary>
    class CommitBuilder
    {
        public readonly CityDistrict District;
        public readonly List<CityPayout> Payouts = new List<CityPayout>();
        public readonly HashSet<string> Touched = new HashSet<string>();
        readonly string payoutPrefix;
        readonly long now;
        int counter;

        public CommitBuilder(CityDistrict district, string opKey, long now)
        {
            District = district;
            payoutPrefix = CityHash.Short(opKey, 12) + CityHash.Short(opKey + "#", 4);
            this.now = now;
        }

        public void SetPlot(string plotId, CityPlot plot)
        {
            if (plot == null) District.Plots.Remove(plotId);
            else District.Plots[plotId] = plot;
            Touched.Add(plotId);
        }

        public void Pay(string playerId, long amount, string kind, string text, string plotId)
        {
            if (string.IsNullOrEmpty(playerId) || CityRules.IsNpc(playerId)) return;
            Payouts.Add(new CityPayout
            {
                Id = payoutPrefix + "-" + (counter++),
                PlayerId = playerId,
                Amount = Math.Max(0L, amount),
                Kind = kind,
                Text = text ?? "",
                PlotId = plotId,
                Time = now,
            });
        }

        public List<CityPlotChange> BuildChanges()
        {
            var list = new List<CityPlotChange>();
            foreach (string plotId in Touched)
            {
                District.Plots.TryGetValue(plotId, out var plot);
                list.Add(new CityPlotChange { DistrictId = District.Id, PlotId = plotId, Plot = plot });
            }
            return list;
        }
    }

    /// <summary>
    /// Change a district: read it (creating it on first use), deliver what earlier saves left in its outbox, apply the
    /// change, save with the write lock (retrying on conflicts), then deliver the new payouts and tell everyone.
    /// opKey makes a retried operation a no-op.
    /// </summary>
    async Task<CommitResult> CommitDistrict(string districtId, string opKey, Action<CityDistrict, CommitBuilder> mutate)
    {
        for (int attempt = 0; ; attempt++)
        {
            var loaded = await LoadDistrict(districtId);
            var district = loaded.Value;

            // Payouts an earlier save couldn't clear: deliver them again (harmless) and clear them in this save
            if (district.Outbox.Count > 0)
                await Deliver(district.Outbox);

            if (opKey != null && district.RecentOps.Contains(opKey))
                return new CommitResult { District = district, AlreadyDone = true };

            var builder = new CommitBuilder(district, opKey ?? ("tidy:" + districtId + ":" + Now + ":" + attempt), Now);
            district.Outbox = new List<CityPayout>();
            mutate?.Invoke(district, builder);

            district.Outbox = new List<CityPayout>(builder.Payouts);
            if (opKey != null)
            {
                district.RecentOps.Add(opKey);
                Trim(district.RecentOps, CityRules.DistrictOpsSize);
            }
            district.Version++;

            string newLock;
            try
            {
                newLock = await store.SaveDistrict(district, loaded.Lock);
            }
            catch (CityConflictException)
            {
                if (attempt + 1 >= MaxAttempts) throw;
                continue;
            }

            if (builder.Payouts.Count > 0)
            {
                await Deliver(builder.Payouts);
                district.Outbox = new List<CityPayout>();
                try
                {
                    await store.SaveDistrict(district, newLock);
                }
                catch (Exception e)
                {
                    // The next save of this district clears it (delivering twice is harmless)
                    if (!(e is CityConflictException)) log($"[City] Couldn't clear the outbox of {map.CityId}/{districtId}: {e.Message}");
                }
            }

            var changes = builder.BuildChanges();
            await Announce(changes, builder.Payouts);
            return new CommitResult { District = district, Changes = changes, Payouts = builder.Payouts };
        }
    }

    async Task Deliver(List<CityPayout> payouts)
    {
        foreach (var payout in payouts)
            await store.SavePayout(payout);
    }

    async Task Announce(List<CityPlotChange> changes, List<CityPayout> payouts)
    {
        try
        {
            if (changes.Count > 0) await notifier.PlotsChanged(changes);
            foreach (var payout in payouts)
            {
                if (string.IsNullOrEmpty(payout.Text)) continue;
                await notifier.PlayerNews(payout.PlayerId, new CityMessage
                {
                    Id = payout.Id, CityId = CityId, Time = payout.Time, Kind = payout.Kind, Text = payout.Text,
                    PlotId = payout.PlotId, Amount = payout.Amount,
                });
            }
        }
        catch (Exception e)
        {
            log($"[City] Couldn't send live updates: {e.Message}");
        }
    }

    /// <summary>A district, created the way the city opens when nobody has touched it yet.</summary>
    async Task<CityVersioned<CityDistrict>> LoadDistrict(string districtId)
    {
        if (!map.DistrictsById.TryGetValue(districtId ?? "", out var def))
            throw Rule("Unknown district.");

        for (int attempt = 0; ; attempt++)
        {
            var found = await store.LoadDistricts(new[] { districtId });
            if (found.TryGetValue(districtId, out var existing) && existing?.Value != null)
            {
                existing.Value.EnsureLists();
                return existing;
            }

            var seeded = CityNpcs.SeedDistrict(map, def);
            try
            {
                string writeLock = await store.SaveDistrict(seeded, null);
                return new CityVersioned<CityDistrict>(seeded, writeLock);
            }
            catch (CityConflictException)
            {
                if (attempt + 1 >= MaxAttempts) throw;
            }
        }
    }

    async Task<Dictionary<string, CityVersioned<CityDistrict>>> LoadAllDistricts()
    {
        var ids = map.DistrictIds.ToList();
        var found = await store.LoadDistricts(ids);
        foreach (string id in ids)
        {
            if (found.TryGetValue(id, out var d) && d?.Value != null)
                d.Value.EnsureLists();
            else
                found[id] = await LoadDistrict(id);
        }
        return found;
    }

    async Task<Dictionary<string, CityDistrict>> LoadDistrictsById(IEnumerable<string> ids)
    {
        var list = ids.Where(id => id != null && map.DistrictsById.ContainsKey(id)).Distinct().ToList();
        var result = new Dictionary<string, CityDistrict>();
        if (list.Count == 0) return result;
        var found = await store.LoadDistricts(list);
        foreach (var pair in found)
        {
            if (pair.Value?.Value == null) continue;
            pair.Value.Value.EnsureLists();
            result[pair.Key] = pair.Value.Value;
        }
        // Districts nobody changed yet look the way the city opens
        foreach (string id in list)
        {
            if (!result.ContainsKey(id)) result[id] = CityNpcs.SeedDistrict(map, map.DistrictsById[id]);
        }
        return result;
    }

    // ================================================================================================================
    // Helpers
    // ================================================================================================================

    async Task<CityResult> Run(Func<Task<CityResult>> body)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return await body();
            }
            catch (CityRuleException e)
            {
                return CityResult.Fail(e.Message);
            }
            catch (CityConflictException)
            {
                if (attempt + 1 >= MaxAttempts)
                    return CityResult.Fail("The city is very busy right now. Try again in a moment.");
            }
        }
    }

    CityResult Ok(Session s, string message = null, long amount = 0, CityChallenge game = null, List<CityPlotChange> changes = null)
    {
        return new CityResult
        {
            Ok = true,
            Message = message,
            Account = View(s),
            Amount = amount,
            Game = game,
            Changes = changes ?? new List<CityPlotChange>(),
        };
    }

    CityAccountView View(Session s)
    {
        long now = Now;
        var view = new CityAccountView { PlayerId = s.PlayerId, CityId = CityId, ServerTime = now };
        var a = s.Account;
        if (a == null) return view;

        view.Joined = true;
        view.CompanyName = a.CompanyName;
        view.Logo = a.Logo;
        view.Balance = a.Balance;
        view.InPlay = a.Holds.Sum(h => h.Amount) + (a.Game != null && a.Game.IsOpen ? a.Game.Stake : 0L);
        view.RentCollectedAt = a.RentCollectedAt;
        view.DepositLeft = DepositLeft(a, now, out long resetAt);
        view.DepositResetAt = resetAt;
        view.ProfileChangedAt = a.ProfileChangedAt;
        view.Inbox = a.Inbox.OrderByDescending(m => m.Time).Take(CityRules.InboxSize).ToList();
        view.Unread = a.Inbox.Count(m => m.Time > a.InboxSeenAt);
        view.Game = a.Game;
        view.Stats = a.Stats;
        return view;
    }

    CityPlot NewOwnerPlot(Session s, long price)
    {
        long now = Now;
        return new CityPlot
        {
            OwnerId = s.PlayerId,
            OwnerName = s.Account.CompanyName,
            Logo = s.Account.Logo,
            PricePaid = price,
            AcquiredAt = now,
            ShieldUntil = now + CityRules.ShieldHours * 3600L,
        };
    }

    CityPlotDef PlotDef(string plotId)
    {
        if (string.IsNullOrEmpty(plotId) || !map.PlotsById.TryGetValue(plotId, out var def))
            throw Rule("That plot doesn't exist.");
        return def;
    }

    static void CheckOp(string opId)
    {
        if (!CityRules.IsValidOpId(opId)) throw Rule("Bad request.");
    }

    void CheckPlotLimit(Dictionary<string, CityVersioned<CityDistrict>> districts, string playerId)
    {
        if (CountPlots(Values(districts), playerId) >= CityRules.MaxPlotsPerPlayer)
            throw Rule($"A company can own at most {CityRules.MaxPlotsPerPlayer} plots in {map.Name}.");
    }

    static int CountPlots(IDictionary<string, CityDistrict> districts, string playerId)
    {
        int count = 0;
        foreach (var district in districts.Values)
        {
            if (district?.Plots == null) continue;
            foreach (var plot in district.Plots.Values)
                if (plot != null && plot.OwnerId == playerId) count++;
        }
        return count;
    }

    static Dictionary<string, CityDistrict> Values(Dictionary<string, CityVersioned<CityDistrict>> districts)
    {
        var result = new Dictionary<string, CityDistrict>();
        foreach (var pair in districts)
            if (pair.Value?.Value != null) result[pair.Key] = pair.Value.Value;
        return result;
    }

    static string OpKey(string playerId, string opId) => playerId + ":" + opId;

    static void Trim(List<string> list, int max)
    {
        if (list.Count > max) list.RemoveRange(0, list.Count - max);
    }

    static CityRuleException Rule(string message) => new CityRuleException(message);
}

/// <summary>Blackjack scoring (ranks 1..13, ace = 1).</summary>
public static class CityCards
{
    public static int Score(List<int> cards)
    {
        if (cards == null) return 0;
        int total = 0, aces = 0;
        foreach (int rank in cards)
        {
            if (rank == 1) { aces++; total += 11; }
            else total += Math.Min(10, rank);
        }
        while (total > 21 && aces > 0) { total -= 10; aces--; }
        return total;
    }

    public static string RankName(int rank)
    {
        switch (rank)
        {
            case 1: return "A";
            case 11: return "J";
            case 12: return "Q";
            case 13: return "K";
            default: return rank.ToString(CultureInfo.InvariantCulture);
        }
    }
}
