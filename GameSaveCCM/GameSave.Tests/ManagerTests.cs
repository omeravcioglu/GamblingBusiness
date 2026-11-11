using System.Globalization;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace GameSave.Tests;

/// <summary>
/// The manager's money (LedgerKinds.Manager entries and the "manager" gambles): the server's tables against the game's own
/// situations (Resources/Manager/Situations/*.json), investor gains (amount by cash tier, timing, the share in the save),
/// costs, and the gamble bounds.
/// </summary>
public class ManagerTests
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    static SubmitDecision Submit(SaveMeta meta, SaveBuilder before, SaveBuilder after, CityTotals city, long elapsed, params LedgerEntry[] ledger)
        => SaveRules.EvaluateSubmit(meta, T.Req(2, 1, after, ledger), city, T.T0 + elapsed, new SaveSubmitContext { PreviousSaveJson = before.Json() });

    static bool LedgerFlag(SubmitDecision d, string text = null)
        => T.HasFlag(d.Meta, SaveCodes.FlagLedger) && (text == null || d.Findings.Any(f => f.StartsWith(SaveCodes.FlagLedger) && f.Contains(text)));

    /// <summary>A company at <paramref name="value"/> on day 100 with its manager, no investor share yet.</summary>
    static SaveBuilder Company(double value, int day = 100) => new SaveBuilder { Value = value, Day = day, MeetingsHeld = 9, ManagerShares = new() };

    // ================================================================================================================
    // The server's tables against the game's
    // ================================================================================================================

    [Test]
    public void ServerTables_MatchTheSituations()
    {
        string situations = FindGameDir("Assets", "Resources", "Manager", "Situations");
        string engine = FindGameFile("Assets", "Resources", "Manager", "Engine.json");
        string system = FindGameFile("Assets", "ScriptsIMade", "Manager", "ManagerSystem.cs");
        string tiers = FindGameFile("Assets", "ScriptsIMade", "UI", "Kit", "StatusTier.cs");
        if (situations == null || engine == null || system == null || tiers == null) Assert.Ignore("The game's manager files aren't next to the module.");

        // Engine: a decision waits at most defaultExpiryDays; the gamble reserve; the cash tiers
        JObject engineJson = JObject.Parse(File.ReadAllText(engine));
        Assert.That((int)engineJson["engine"]["defaultExpiryDays"], Is.LessThanOrEqualTo(SaveCalibration.ManagerDecisionWindowDays));
        Assert.That((int)engineJson["engine"]["situationCooldown"], Is.GreaterThanOrEqualTo(SaveCalibration.ManagerDefaultCooldownDays));
        Assert.That(double.Parse(Regex.Match(File.ReadAllText(system), @"public const double Reserve = ([\d.]+)").Groups[1].Value, Inv), Is.EqualTo(SaveCalibration.ManagerReserve));
        string thresholds = Regex.Match(File.ReadAllText(tiers), @"Thresholds = \{([^}]*)\}").Groups[1].Value;
        Assert.That(thresholds.Split(',').Select(t => double.Parse(t.Trim(), NumberStyles.Float, Inv)), Is.EqualTo(SaveCalibration.CashTierThresholds));

        int gambles = 0, gains = 0;
        foreach (string file in Directory.GetFiles(situations, "*.json"))
        {
            foreach (JObject s in JObject.Parse(File.ReadAllText(file))["situations"].Cast<JObject>())
            {
                string id = (string)s["id"];
                int cooldown = s["cooldown"] != null ? (int)s["cooldown"] : (int)engineJson["engine"]["situationCooldown"];
                var prms = (JObject)s["params"] ?? new JObject();
                foreach (JObject choice in s["choices"].Cast<JObject>())
                {
                    string detail = id + "/" + (string)choice["id"];
                    foreach ((JObject effect, bool inOutcome) in Effects(choice))
                    {
                        string op = (string)effect["op"];
                        if (op == "gamble")
                        {
                            gambles++;
                            var outcomes = effect["outcomes"].Cast<JObject>().Select(o => (Chance: (double)o["chance"], Mult: (double)o["mult"])).ToList();
                            double total = outcomes.Sum(o => o.Chance);
                            SaveGambleRule rule = SaveCalibration.GambleRule("manager", detail);
                            Assert.That(rule.Key, Is.EqualTo("manager/" + detail), detail + ": a gamble the server doesn't know");
                            Assert.That(rule.MaxMultiplier, Is.GreaterThanOrEqualTo(outcomes.Max(o => o.Mult)), detail + ": largest multiple");
                            Assert.That(rule.WinChance, Is.GreaterThanOrEqualTo(outcomes.Where(o => o.Mult > 1).Sum(o => o.Chance) / total), detail + ": win chance");
                            Assert.That(rule.WinChance, Is.LessThanOrEqualTo(outcomes.Where(o => o.Mult > 1).Sum(o => o.Chance) / total + 0.02), detail + ": win chance tallied too high");
                            Assert.That(rule.PushMultiple, Is.GreaterThanOrEqualTo(outcomes.Where(o => o.Mult <= 1).Select(o => o.Mult).DefaultIfEmpty(0).Max()), detail + ": what a lost round pays");
                            Assert.That(rule.StakeReserve, Is.EqualTo(SaveCalibration.ManagerReserve));
                            long cap = SaveCalibration.ManagerGambleCap(id, 100);
                            Assert.That(cap, Is.GreaterThanOrEqualTo(SaveCalibration.ManagerGambleCapFor(cooldown, 100)), detail + ": the server's cooldown is longer than the game's " + cooldown);
                            Assert.That(s["expires"] == null && s["urgent"] == null, Is.True, id + ": a decision window other than defaultExpiryDays");
                            Assert.That(s["followupOnly"] == null, Is.True, id + ": follow-ups come up without the cooldown");
                        }
                        else if (op == "money")
                        {
                            string amount = (string)effect["amount"];
                            bool gain = amount != null && !amount.StartsWith("-");
                            if (!gain) continue;
                            gains++;
                            Assert.That(SaveCalibration.ManagerGainMax(id, (string)choice["id"], 0), Is.GreaterThan(0), detail + " pays money in: the server doesn't allow it");
                            // The offer's amount: capped by the tier table, the negotiated one 1.25 x it
                            JObject spec = (JObject)prms[amount];
                            JToken max = spec["max"] ?? prms[(string)spec["times"]]?["max"];
                            Assert.That(max, Is.Not.Null, detail + ": a gain without a max");
                            double times = spec["times"] != null ? (double)spec["value"] : 1.0;
                            double[] tierMax = max.Select(m => (double)m).ToArray();
                            for (int t = 0; t < tierMax.Length; t++)
                                Assert.That(SaveCalibration.ManagerGainMax(id, (string)choice["id"], t), Is.GreaterThanOrEqualTo(RoundMoney(times * tierMax[t])), detail + " at tier " + t);
                        }
                        else if (op == "revenue_share" && (string)effect["id"] == SaveCalibration.InvestorShareId)
                        {
                            Assert.That(id, Is.EqualTo(SaveCalibration.InvestorSituation));
                            Assert.That(prms[(string)effect["days"]]?["value"]?.Value<double>(), Is.EqualTo(SaveCalibration.InvestorShareDays), "the investor's share lasts 40 days");
                        }
                    }
                }
                if (id == SaveCalibration.InvestorSituation)
                    Assert.That(s["conditions"].Select(c => (string)c), Does.Contain("!share"), "the investor only comes while no share runs");
            }
        }
        Assert.That(gambles, Is.EqualTo(7), "the seven gambles of the spec");
        Assert.That(gains, Is.EqualTo(2), "accept and negotiate of investor_share");
    }

    /// <summary>A choice's effects and those of its outcomes (and of its gambles' outcomes), each with whether it sits in an outcome.</summary>
    static IEnumerable<(JObject Effect, bool InOutcome)> Effects(JObject choice)
    {
        IEnumerable<(JObject, bool)> Walk(JToken effects, bool inOutcome)
        {
            if (effects == null) yield break;
            foreach (JObject e in effects.Cast<JObject>())
            {
                yield return (e, inOutcome);
                if ((string)e["op"] == "gamble")
                    foreach (JObject o in e["outcomes"].Cast<JObject>())
                        foreach (var inner in Walk(o["effects"], true)) yield return inner;
            }
        }
        foreach (var e in Walk(choice["effects"], false)) yield return e;
        if (choice["outcomes"] != null)
            foreach (JObject o in choice["outcomes"].Cast<JObject>())
                foreach (var e in Walk(o["effects"], true)) yield return e;
    }

    /// <summary>ManagerParams.RoundMoney: 2 significant figures, halves away from zero.</summary>
    static double RoundMoney(double amount)
    {
        double a = Math.Abs(amount);
        if (a < 100) return Math.Round(amount, MidpointRounding.AwayFromZero);
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(a)) - 1);
        return Math.Round(amount / magnitude, MidpointRounding.AwayFromZero) * magnitude;
    }

    [Test]
    public void InvestorOffer_ByCashTier()
    {
        Assert.That(SaveCalibration.CashTier(999_999), Is.EqualTo(0));
        Assert.That(SaveCalibration.CashTier(1e6), Is.EqualTo(1));
        Assert.That(SaveCalibration.CashTier(6e7), Is.EqualTo(3));
        Assert.That(SaveCalibration.CashTier(5e12), Is.EqualTo(6));
        Assert.That(SaveCalibration.ManagerGainMax("investor_share", "accept", 0), Is.EqualTo(80_000));
        Assert.That(SaveCalibration.ManagerGainMax("investor_share", "negotiate", 1), Is.EqualTo(630_000), "1.25 x $500,000 rounded to 2 figures");
        Assert.That(SaveCalibration.ManagerGainMax("investor_share", "negotiate", 6), Is.EqualTo(630_000_000));
        Assert.That(SaveCalibration.ManagerGainMax("Investor_Share", " ACCEPT ", 9), Is.EqualTo(500_000_000), "past the top tier: the top");
        Assert.That(SaveCalibration.ManagerGainMax("investor_share", "reject", 3), Is.EqualTo(0));
        Assert.That(SaveCalibration.ManagerGainMax("lawsuit", "settle", 3), Is.EqualTo(0));
    }

    // ================================================================================================================
    // Investor gains
    // ================================================================================================================

    [Test]
    public void InvestorGain_WithinItsOffer_IsExplained()
    {
        // $60M (cash tier 3), the negotiated offer pays its most, $25M, and starts the investor's share. No staff: in one day
        // nothing else explains $25M, so without the entry the growth is flagged
        SaveBuilder before = Company(60_000_000);
        SaveBuilder after = before.With(s =>
        {
            s.Day = 101;
            s.Value = 85_000_000;
            s.ManagerShares = new() { ("investor", 101, 140) };
        });
        LedgerEntry gain = T.Manager(1, "investor_share", "negotiate", 25_000_000, 60_000_000, 101);

        SubmitDecision d = Submit(T.Meta(day: 100, value: 60_000_000), before, after, T.City(), 45, gain);
        Assert.That(d.Accepted, Is.True, T.Why(d));
        Assert.That(d.Meta.Flagged, Is.False, T.Why(d));
        Assert.That(d.Growth.LedgerNet, Is.EqualTo(25_000_000).Within(1));

        SubmitDecision without = Submit(T.Meta(day: 100, value: 60_000_000), before, after, T.City(), 45);
        Assert.That(T.HasFlag(without.Meta, SaveCodes.FlagGrowth), Is.True, "the gain is what explains it: " + T.Why(without));

        // Accepted at the tier's most ($20M)
        SubmitDecision accept = Submit(T.Meta(day: 100, value: 60_000_000), before, after.With(s => s.Value = 80_000_000), T.City(), 45,
                                       T.Manager(1, "investor_share", "accept", 20_000_000, 60_000_000, 101));
        Assert.That(accept.Meta.Flagged, Is.False, T.Why(accept));
    }

    [Test]
    public void InvestorGain_UsesTheTierItsOfferCameUpAt()
    {
        // The offer came up (at most 12 days before the decision, so maybe before the last accepted save) while the company
        // was at $1.2M, cash tier 1; it has fallen to $500,000 (tier 0) since. Negotiated: tier 1's $630,000, not tier 0's
        // $100,000. In one day the running bound stays below $1M: only the run's peak allows it
        SaveMeta meta = T.Meta(day: 100, value: 500_000);
        meta.Peak = 1_200_000;
        SaveBuilder before = Company(500_000).With(s => s.RunPeak = 1_200_000);
        SaveBuilder after = before.With(s =>
        {
            s.Day = 101;
            s.Value = 1_130_000;
            s.ManagerShares = new() { ("investor", 101, 140) };
        });
        LedgerEntry gain = T.Manager(1, "investor_share", "negotiate", 630_000, 500_000, 101);
        SubmitDecision d = Submit(meta, before, after, T.City(), 45, gain);
        Assert.That(d.Meta.Flagged, Is.False, T.Why(d));

        // A company that never reached tier 1: $100,000 at most
        SaveMeta small = T.Meta(day: 100, value: 500_000);
        SubmitDecision capped = Submit(small, Company(500_000), after, T.City(), 45, gain);
        Assert.That(LedgerFlag(capped, "pays at most $100,000 at cash tier 0"), Is.True, T.Why(capped));
    }

    [Test]
    public void FakeGains_AreFlagged()
    {
        SaveMeta Meta() => T.Meta(day: 100, value: 300_000);
        SaveBuilder before = Company(300_000);
        SaveBuilder After(double value, int day = 101, params (string, int, int)[] shares) => before.With(s =>
        {
            s.Day = day;
            s.Value = value;
            s.ManagerShares = shares.ToList();
        });
        var share = ("investor", 101, 140);

        // Money from a decision that only costs, or from the investor's "reject"
        Assert.That(LedgerFlag(Submit(Meta(), before, After(350_000, 101, share), T.City(), 45, T.Manager(1, "joint_marketing", "accept", 50_000, 300_000, 101)),
                               "only an investor's offer"), Is.True);
        Assert.That(LedgerFlag(Submit(Meta(), before, After(350_000, 101, share), T.City(), 45, T.Manager(1, "investor_share", "reject", 50_000, 300_000, 101)),
                               "only an investor's offer"), Is.True);

        // More than the offer pays at the company's tier (tier 0: $80,000 accepted, $100,000 negotiated)
        SubmitDecision tooBig = Submit(Meta(), before, After(400_000, 101, share), T.City(), 45, T.Manager(1, "investor_share", "accept", 100_000, 300_000, 101));
        Assert.That(LedgerFlag(tooBig, "pays at most $80,000 at cash tier 0"), Is.True, T.Why(tooBig));
        SubmitDecision negotiated = Submit(Meta(), before, After(400_000, 101, share), T.City(), 45, T.Manager(1, "investor_share", "negotiate", 100_000, 300_000, 101));
        Assert.That(negotiated.Meta.Flagged, Is.False, T.Why(negotiated));

        // Two payments 20 days apart in one upload (every payment starts a 40-day share, and no offer comes while it runs)
        SubmitDecision twice = Submit(Meta(), before, After(460_000, 125, ("investor", 101, 140), ("investor", 121, 160)), T.City(), 25 * 45,
                                      T.Manager(1, "investor_share", "accept", 80_000, 300_000, 101), T.Manager(2, "investor_share", "accept", 80_000, 380_000, 121));
        Assert.That(LedgerFlag(twice, "20 days after the investor's last payment"), Is.True, T.Why(twice));
        // A made-up day far outside the stretch counts as the stretch's last day (it can't jump the timing)
        SubmitDecision farDay = Submit(Meta(), before, After(460_000, 125, ("investor", 101, 140), ("investor", 125, 164)), T.City(), 25 * 45,
                                       T.Manager(1, "investor_share", "accept", 80_000, 300_000, 101), T.Manager(2, "investor_share", "accept", 80_000, 380_000, int.MaxValue));
        Assert.That(LedgerFlag(farDay, "24 days after the investor's last payment"), Is.True, T.Why(farDay));

        // A payment while the share the last accepted save lists still runs
        SaveBuilder sharing = before.With(s => s.ManagerShares = new() { ("investor", 90, 129) });
        SubmitDecision blocked = Submit(Meta(), sharing, sharing.With(s =>
        {
            s.Day = 110;
            s.Value = 380_000;
            s.ManagerShares = new() { ("investor", 90, 129), ("investor", 110, 149) };
        }), T.City(), 10 * 45, T.Manager(1, "investor_share", "accept", 80_000, 300_000, 110));
        Assert.That(LedgerFlag(blocked, "while the investor's share from day 90 still ran"), Is.True, T.Why(blocked));

        // A payment without the share it starts (the next upload couldn't see it)
        SubmitDecision noShare = Submit(Meta(), before, After(380_000), T.City(), 45, T.Manager(1, "investor_share", "accept", 80_000, 300_000, 101));
        Assert.That(LedgerFlag(noShare, "lists no investor share"), Is.True, T.Why(noShare));

        // A running share that disappears
        SubmitDecision vanished = Submit(Meta(), sharing, sharing.With(s =>
        {
            s.Day = 101;
            s.ManagerShares = new();
        }), T.City(), 45);
        Assert.That(LedgerFlag(vanished, "no longer lists it"), Is.True, T.Why(vanished));

        // The honest timing: a share that ended, then the next offer
        SaveBuilder ending = before.With(s => s.ManagerShares = new() { ("investor", 62, 101) });
        SubmitDecision next = Submit(Meta(), ending, ending.With(s =>
        {
            s.Day = 103;
            s.Value = 380_000;
            s.ManagerShares = new() { ("investor", 102, 141) };
        }), T.City(), 3 * 45, T.Manager(1, "investor_share", "accept", 80_000, 300_000, 102));
        Assert.That(next.Meta.Flagged, Is.False, T.Why(next));
    }

    // ================================================================================================================
    // Costs
    // ================================================================================================================

    [Test]
    public void Costs_CountAsTheyAre()
    {
        SaveBuilder before = Company(300_000);
        SubmitDecision d = Submit(T.Meta(day: 100, value: 300_000), before, before.With(s =>
        {
            s.Day = 101;
            s.Value = 250_000;
        }), T.City(), 45, T.Manager(1, "lawsuit", "settle", -50_000, 300_000, 101));
        Assert.That(d.Meta.Flagged, Is.False, T.Why(d));
        Assert.That(d.Growth.LedgerNet, Is.EqualTo(-50_000).Within(0.01));
    }

    [Test]
    public void ACostThatLeavesTheCompanyBroke_ShowsABankruptcy()
    {
        // Three coin-flip restarts need two busts in the ledger (the first can follow a daily loss): a casino bust and a
        // lawsuit that took the last of the money
        SaveBuilder before = Company(100_000).With(s => s.Restarts = 0);
        SaveBuilder after = before.With(s =>
        {
            s.Day = 104;
            s.Value = 51_000;
            s.Restarts = 3;
        });
        SubmitDecision d = Submit(T.Meta(day: 100, value: 100_000), before, after, T.City(), 4 * 45,
                                  T.Flip(1, 100_000, false, 100_000, 101), T.Manager(2, "lawsuit", "settle", -51_000, 51_000, 102));
        Assert.That(d.Growth.Hearings, Is.EqualTo(3), T.Why(d));
        Assert.That(d.Growth.HearingEvidence, Does.Contain("2 busts in the ledger"));
        Assert.That(d.Meta.Flagged, Is.False, T.Why(d));
    }

    // ================================================================================================================
    // Gambles
    // ================================================================================================================

    [Test]
    public void Gambles_PayAtMostTheirOutcome_AndLeaveTheReserve()
    {
        SaveBuilder before = Company(300_000);
        SubmitDecision Play(double valueAfter, params LedgerEntry[] ledger)
            => Submit(T.Meta(day: 100, value: 300_000), before, before.With(s =>
            {
                s.Day = 101;
                s.Value = valueAfter;
            }), T.NoCity, 45, ledger);

        Assert.That(Play(310_000, T.ManagerGamble(1, "joint_marketing/accept", 20_000, 30_000, 300_000, 101)).Meta.Flagged, Is.False);
        Assert.That(LedgerFlag(Play(312_000, T.ManagerGamble(1, "joint_marketing/accept", 20_000, 32_000, 300_000, 101)), "at most 1.5x"), Is.True);
        Assert.That(Play(312_000, T.ManagerGamble(1, "casino_night/real", 10_000, 22_000, 300_000, 101)).Meta.Flagged, Is.False);
        Assert.That(LedgerFlag(Play(312_000, T.ManagerGamble(1, "casino_night/real", 10_000, 23_000, 300_000, 101)), "at most 2.2x"), Is.True);
        Assert.That(Play(340_000, T.ManagerGamble(1, "side_project/fund", 20_000, 60_000, 300_000, 101)).Meta.Flagged, Is.False);

        // The last $1,000 is never staked
        Assert.That(Play(300_000, T.ManagerGamble(1, "idle_cash/gamble", 299_000, 299_000 * 2, 300_000, 101)).Meta.Flagged, Is.False);
        Assert.That(LedgerFlag(Play(300_000, T.ManagerGamble(1, "idle_cash/gamble", 299_500, 0, 300_000, 101)), "it always leaves $1,000"), Is.True);

        // A gamble this version doesn't know: at most 3x, no luck tally
        Assert.That(Play(320_000, T.ManagerGamble(1, "new_thing/go", 10_000, 30_000, 300_000, 101)).Meta.Flagged, Is.False);
        Assert.That(LedgerFlag(Play(320_000, T.ManagerGamble(1, "new_thing/go", 10_000, 35_000, 300_000, 101)), "at most 3x"), Is.True);
    }

    [Test]
    public void Gambles_GoIntoTheLuckTally_AtTheirOwnOdds()
    {
        SaveBuilder before = Company(300_000);
        SubmitDecision d = Submit(T.Meta(day: 100, value: 300_000), before, before.With(s =>
        {
            s.Day = 130;
            s.Value = 310_000;
        }), T.NoCity, 30 * 45,
            T.ManagerGamble(1, "joint_marketing/accept", 20_000, 30_000, 300_000, 101),
            T.ManagerGamble(2, "casino_night/real", 10_000, 0, 310_000, 105),
            T.ManagerGamble(3, "new_thing/go", 10_000, 30_000, 300_000, 110));
        LuckTally g = d.Meta.Luck.Gambles;
        Assert.That(g.Count, Is.EqualTo(2), "the unknown gamble isn't tallied");
        Assert.That(g.Wins, Is.EqualTo(1));
        Assert.That(g.Expected, Is.EqualTo(0.76 + 0.46).Within(1e-9));
    }

    [Test]
    public void Gambles_ComeAtMostOncePerCooldown()
    {
        SaveBuilder before = Company(1_000_000);
        SubmitDecision Stretch(int days, params LedgerEntry[] ledger)
            => Submit(T.Meta(day: 100, value: 1_000_000), before, before.With(s => s.Day = 100 + days), T.NoCity, days * 45L, ledger);
        LedgerEntry Joint(long i, int day) => T.ManagerGamble(i, "joint_marketing/accept", 10_000, 10_000 * (i % 2 == 0 ? 1.5 : 0.6), 1_000_000, day);

        // joint_marketing comes up at most once per 18 days and waits at most 12: two decisions in 5 days are possible, three aren't
        Assert.That(Stretch(5, Joint(1, 101), Joint(2, 104)).Meta.Flagged, Is.False);
        Assert.That(LedgerFlag(Stretch(5, Joint(1, 101), Joint(2, 103), Joint(3, 104)), "3 manager gambles of joint_marketing in 5 days (at most 2"), Is.True);
        // Folded ones count every round
        Assert.That(LedgerFlag(Stretch(30, T.Summary(1, "manager", "joint_marketing/aggressive", 5, 2, 50_000, 60_000)), "5 manager gambles of joint_marketing in 30 days"), Is.True);
        Assert.That(Stretch(30, T.Summary(1, "manager", "joint_marketing/aggressive", 4, 2, 40_000, 60_000)).Meta.Flagged, Is.False);

        // Situations this version doesn't know share one cap (the engine's default cooldown of 15 days)
        LedgerEntry Unknown(long i) => T.ManagerGamble(i, "mystery_" + i + "/go", 1_000, 0, 1_000_000, 101);
        Assert.That(Stretch(5, Unknown(1), Unknown(2), Unknown(3)).Meta.Flagged, Is.False);
        Assert.That(LedgerFlag(Stretch(5, Unknown(1), Unknown(2), Unknown(3), Unknown(4)), "situations this server doesn't know"), Is.True);
    }

    // ================================================================================================================
    // Helpers
    // ================================================================================================================

    static string FindGameFile(params string[] parts)
    {
        string relative = Path.Combine(parts);
        for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir != null; dir = dir.Parent)
        {
            string path = Path.Combine(dir.FullName, relative);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    static string FindGameDir(params string[] parts)
    {
        string relative = Path.Combine(parts);
        for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir != null; dir = dir.Parent)
        {
            string path = Path.Combine(dir.FullName, relative);
            if (Directory.Exists(path)) return path;
        }
        return null;
    }
}
