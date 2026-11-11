namespace GameSave.Tests;

/// <summary>SaveCalibration's numbers against hand calculations, and SaveSnapshot against a real JsonUtility save.</summary>
public class CalibrationTests
{
    [Test]
    public void EmployeeOutputMax_MatchesHandMaths()
    {
        // Best employee: productivity 1 + 0.7 + 0.5 + 2.25 = 4.45; $250 x 4.45 x (2.5 x 1.15 x 1.1875) + 70 x $62.5, then PhD x 1.1875
        double expected = (250 * 4.45 * (2.5 * 1.15 * 1.1875) + 70 * 62.5) * 1.1875;
        Assert.That(SaveCalibration.BestEmployeeOutputMax(), Is.EqualTo(expected).Within(1e-6));

        // Average employee: 1 + 0 + 0.125 + 0.25 = 1.375, no IQ synergy, no PhD
        Assert.That(SaveCalibration.EmployeeOutputMax(T.Emp("a", 100, 2.5, 10)), Is.EqualTo(250 * 1.375 * 3.4140625).Within(1e-6));

        // A weak hire still produces a little: 1 - 0.3 - 0.125 - 0.25 = 0.325
        Assert.That(SaveCalibration.EmployeeOutputMax(T.Emp("b", 70, 1.5, 0)), Is.EqualTo(250 * 0.325 * 3.4140625).Within(1e-6));
        // A bad hire loses money: the bound never goes below zero
        Assert.That(SaveCalibration.EmployeeOutputMax(T.Emp("c", 70, 0.0, 0)), Is.EqualTo(0));

        // Edited attributes are clamped to what the game can generate
        Assert.That(SaveCalibration.EmployeeOutputMax(new SaveEmployeeInfo { IQ = 100000, GPA = 99, Experience = 9999, PhD = true }),
                    Is.EqualTo(SaveCalibration.BestEmployeeOutputMax()).Within(1e-6));
    }

    [Test]
    public void HireImpactMax_MatchesCompanyManager()
    {
        // (170-100) x 15 + (4-2) x 200 + (50-5) x 30 - 300 = 2,500, doubled by Critical Success
        Assert.That(SaveCalibration.BestHireImpactMax(), Is.EqualTo(5000));
        Assert.That(SaveCalibration.HireImpactMax(T.Emp("a", 100, 2.0, 5)), Is.EqualTo(0));
        Assert.That(SaveCalibration.HireImpactMax(new SaveEmployeeInfo { IQ = 150, GPA = 4, Experience = 25, Criminal = true }),
                    Is.EqualTo(Math.Max(0, 50 * 15 + 400 + 600 - 300 - 500) * 2));
    }

    [Test]
    public void DailyTickBounds()
    {
        Assert.That(SaveCalibration.TeamSynergyMax(4), Is.EqualTo(1.0));
        Assert.That(SaveCalibration.TeamSynergyMax(200), Is.EqualTo(1.5).Within(1e-9));
        Assert.That(SaveCalibration.LuckBonusBase(10_000), Is.EqualTo(500));
        Assert.That(SaveCalibration.LuckBonusBase(1_000_000), Is.EqualTo(10_000));
        Assert.That(SaveCalibration.LuckBonusBase(1e12), Is.EqualTo(250_000));
        // Roulette 4x base + (2 + 10 + 2 + 3 + 1 + 2) x 1.25 x base = 29 x base
        Assert.That(SaveCalibration.DailyLuckMax(1_000_000, 0, false), Is.EqualTo(290_000).Within(1e-6));
        Assert.That(SaveCalibration.DailyLuckMax(1_000_000, 20, true), Is.EqualTo(290_000 + 12_500 * 2).Within(1e-6));
        Assert.That(SaveCalibration.IsKarmaDay(15), Is.True);
        Assert.That(SaveCalibration.IsKarmaDay(16), Is.False);
        Assert.That(SaveCalibration.RunSeconds(2), Is.EqualTo(135));
    }

    [Test]
    public void EventRewards()
    {
        Assert.That(SaveCalibration.MaxTaskReward, Is.EqualTo(90_000));
        Assert.That(SaveCalibration.MaxCrisisReward, Is.EqualTo(7_200));
        Assert.That(SaveCalibration.MaxSkipBonus, Is.EqualTo(625));
        Assert.That(SaveCalibration.HearingRestartMax(0), Is.EqualTo(51_000));
        Assert.That(SaveCalibration.HearingRestartMax(500_000_000), Is.EqualTo(50_001_000));
    }

    [Test]
    public void GambleRules()
    {
        Assert.That(SaveCalibration.GambleRule("casino", "slots").MaxMultiplier, Is.EqualTo(500));
        Assert.That(SaveCalibration.GambleRule("casino", "Slot Machine").WinChance, Is.EqualTo(SaveCalibration.SlotsWinChanceMax));
        Assert.That(SaveCalibration.GambleRule("casino", "blackjack").MaxMultiplier, Is.EqualTo(2.5));
        Assert.That(SaveCalibration.GambleRule("casino", "coin-flip").MaxMultiplier, Is.EqualTo(2.0));
        Assert.That(SaveCalibration.GambleRule("casino", null).WinChance, Is.EqualTo(0), "an unnamed casino game isn't tallied");
        Assert.That(SaveCalibration.GambleRule("tilt", null).WinChance, Is.EqualTo(SaveCalibration.CoinFlipWinChanceMax));
        Assert.That(SaveCalibration.GambleRule("upgrade-blackjack", null).MaxMultiplier, Is.EqualTo(1.0));
        Assert.That(SaveCalibration.GambleRule("upgrade-chance", null).MaxMultiplier, Is.EqualTo(2.0));
        Assert.That(SaveCalibration.GambleRule("build-gamble", null).MaxMultiplier, Is.EqualTo(1.5));
        Assert.That(SaveCalibration.GambleRule("furniture", null).MaxMultiplier, Is.EqualTo(3.0));
        Assert.That(SaveCalibration.GambleRule("furniture", null).StakeFromValue, Is.False);
        Assert.That(SaveCalibration.GambleRule("something-new", null).MaxMultiplier, Is.EqualTo(500));
        // Every tallied chance is an upper bound of the real one (max blackjack win chance 43.8%, slots 48.16%, flips 50%)
        Assert.That(SaveCalibration.BlackjackWinChanceMax, Is.GreaterThan(0.438));
        Assert.That(SaveCalibration.SlotsWinChanceMax, Is.GreaterThan(0.4817));
        Assert.That(SaveCalibration.CoinFlipWinChanceMax, Is.GreaterThanOrEqualTo(0.5));
    }

    [Test]
    public void GambleMoneyNet_FollowsWhereThePayoutGoes()
    {
        SaveGambleRule casino = SaveCalibration.GambleRule("casino", "coinflip");
        Assert.That(SaveCalibration.GambleMoneyNet(casino, 100, 195, false), Is.EqualTo(95));
        Assert.That(SaveCalibration.GambleMoneyNet(casino, 100, 0, false), Is.EqualTo(-100));

        SaveGambleRule build = SaveCalibration.GambleRule("build-gamble", null);
        Assert.That(SaveCalibration.GambleMoneyNet(build, 100, 150, false), Is.EqualTo(-100), "the win went into the build budget");
        Assert.That(SaveCalibration.GambleMoneyNet(build, 100, 100, false), Is.EqualTo(0), "a push refunds the company");
        Assert.That(SaveCalibration.GambleMoneyNet(build, 1000, 1500, true), Is.EqualTo(0), "sums: at best nothing lost");

        SaveGambleRule chance = SaveCalibration.GambleRule("upgrade-chance", null);
        Assert.That(SaveCalibration.GambleMoneyNet(chance, 100, 200, false), Is.EqualTo(0), "critical success: the money back");
        Assert.That(SaveCalibration.GambleMoneyNet(chance, 100, 100, false), Is.EqualTo(-100), "success: the upgrade, not money");
        Assert.That(SaveCalibration.GambleMoneyNet(chance, 100, 0, false), Is.EqualTo(-100));

        SaveGambleRule upgradeHand = SaveCalibration.GambleRule("upgrade-blackjack", null);
        Assert.That(SaveCalibration.GambleMoneyNet(upgradeHand, 100, 100, false), Is.EqualTo(0));
        Assert.That(SaveCalibration.GambleMoneyNet(upgradeHand, 100, 0, false), Is.EqualTo(-100));

        Assert.That(SaveCalibration.GambleMoneyNet(SaveCalibration.GambleRule("furniture", null), 100, 300, false), Is.EqualTo(0));
    }

    [Test]
    public void CardBounds()
    {
        Assert.That(SaveCalibration.CardSkillMultiplierMax, Is.EqualTo(1.3125 * 1.5625 * 40).Within(1e-9));
        // Every card skill (unknown skills count as all), +5% margin
        Assert.That(SaveCalibration.CardSkillMultiplier(null, true), Is.EqualTo(SaveCalibration.CardSkillMultiplierMax * 1.05).Within(1e-9));
        SaveCardRule high = SaveCalibration.CardRule("high");
        double money = 55_000 * SaveCalibration.CardSkillMultiplier(null, true);
        Assert.That(SaveCalibration.CardGainMax(high, 1_000_000), Is.EqualTo(money + (1_000_000 + money) * 1.0 + 1).Within(1e-6));
        // Without the Jackpot (x10) the most is a tenth of the money part: more than that needed a Jackpot (a rare outcome)
        double noJackpot = 55_000 * SaveCalibration.CardSkillMultiplier(null, false);
        Assert.That(SaveCalibration.CardGainMax(high, 1_000_000, null, false), Is.EqualTo(noJackpot + (1_000_000 + noJackpot) * 1.0 + 1).Within(1e-6));
        Assert.That(SaveCalibration.CardJackpotChanceMax(null), Is.EqualTo(0.02 * 1.25 * 1.25).Within(1e-12), "2% x 1.25 (Corporate Enlightenment) +25%");
        Assert.That(SaveCalibration.CardJackpotChanceMax(new HashSet<string>()), Is.EqualTo(0), "no Jackpot skill, no jackpots");
        Assert.That(SaveCalibration.CardRule("HighRisk").Key, Is.EqualTo("high"));
        Assert.That(SaveCalibration.CardRule("low-risk").Key, Is.EqualTo("low"));
        Assert.That(SaveCalibration.CardRule("mystery").Tallied, Is.False);

        // No luck skills: base + 5 trust points + 3 margin
        var none = new HashSet<string>();
        Assert.That(SaveCalibration.CardGoodChanceMax(high, none), Is.EqualTo(0.33).Within(1e-9));
        Assert.That(SaveCalibration.CardGoodChanceMax(SaveCalibration.CardRule("low"), none), Is.EqualTo(0.83).Within(1e-9));
        // Unknown skills count as all of them: nearly always good
        Assert.That(SaveCalibration.CardGoodChanceMax(high, null), Is.GreaterThan(0.99));
        // Beginner's Luck alone: 0.25 + 0.15 + 0.05 + 0.03
        Assert.That(SaveCalibration.CardGoodChanceMax(high, new HashSet<string> { "chaos_1_luck" }), Is.EqualTo(0.48).Within(1e-9));
    }

    [Test]
    public void TradeRatio_GrowsWithTime()
    {
        Assert.That(SaveCalibration.TradeRatioMax(0), Is.EqualTo(Math.Exp(2.3)).Within(1e-9));
        Assert.That(SaveCalibration.TradeRatioMax(1), Is.EqualTo(Math.Exp(2.3 + 0.13 + 1.6)).Within(1e-9));
        Assert.That(SaveCalibration.TradeRatioMax(10), Is.GreaterThan(80), "above the simulated best trade of 10 hours");
        Assert.That(SaveCalibration.TradeRatioMax(1e9), Is.EqualTo(double.MaxValue));
    }

    [Test]
    public void MaxPlausibleValue_GenerousCurve_CappedAtTenBillion()
    {
        Assert.That(SaveRules.MaxPlausibleValue(0), Is.EqualTo(1e8).Within(1));
        Assert.That(SaveRules.MaxPlausibleValue(40), Is.EqualTo(2e9).Within(1e3));
        Assert.That(SaveRules.MaxPlausibleValue(280), Is.EqualTo(SaveCalibration.MaxPlausibleValueCap), "the curve would say $2T");
        Assert.That(SaveCalibration.MaxPlausibleValueCap, Is.EqualTo(1e10), "the run's win value");
        double last = 0;
        for (int day = 0; day <= 5000; day += 7)
        {
            double value = SaveRules.MaxPlausibleValue(day);
            if (last < SaveCalibration.MaxPlausibleValueCap) Assert.That(value, Is.GreaterThan(last));
            else Assert.That(value, Is.EqualTo(SaveCalibration.MaxPlausibleValueCap));
            last = value;
        }
        Assert.That(SaveRules.MaxPlausibleValue(int.MaxValue), Is.EqualTo(SaveCalibration.MaxPlausibleValueCap));
    }

    [Test]
    public void SlotsRareTiers_MatchThePaytable()
    {
        // P(three of a kind of symbol i) = (w_i / 100)^3; a tier holds every symbol paying at least its multiple
        int[] weights = { 25, 22, 20, 15, 10, 5, 2, 1 };
        double[] pays = { 5, 6, 8, 12, 20, 50, 100, 500 };
        SaveGambleRule slots = SaveCalibration.GambleRule("casino", "slots");
        Assert.That(slots.RareMultiples, Is.EqualTo(SaveCalibration.SlotsRareMultiples));
        for (int t = 0; t < slots.RareMultiples.Length; t++)
        {
            double exact = 0;
            for (int i = 0; i < weights.Length; i++)
                if (pays[i] >= slots.RareMultiples[t]) exact += Math.Pow(weights[i] / 100.0, 3);
            Assert.That(slots.RareChances[t], Is.GreaterThan(exact * 1.2).And.LessThan(exact * 1.5), slots.RareMultiples[t] + "x");
        }
        Assert.That(SaveCalibration.GambleRule("casino", "blackjack").RareMultiples, Is.Empty);
        Assert.That(slots.PushMultiple, Is.EqualTo(0), "a slots spin that doesn't win pays nothing");
        Assert.That(SaveCalibration.GambleRule("casino", "blackjack").PushMultiple, Is.EqualTo(1), "a push gives the stake back");
    }

    [Test]
    public void Snapshot_ParsesARealJsonUtilitySave()
    {
        string json = File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "jsonutility_save.json"));
        SaveSnapshot save = SaveSnapshot.Parse(json);
        Assert.That(save.Ok, Is.True, save.Error);
        Assert.That(save.CompanyValue, Is.EqualTo(50632.45385456085));
        Assert.That(save.CurrentDay, Is.EqualTo(1));
        Assert.That(save.Employees.Count, Is.EqualTo(1));
        SaveEmployeeInfo e = save.Employees[0];
        Assert.That(e.Id, Is.EqualTo("0ce218400ea3437e83514ecdfcf825b6"));
        Assert.That(e.IQ, Is.EqualTo(100));
        Assert.That(e.Experience, Is.EqualTo(34));
        Assert.That(e.Age, Is.EqualTo(51));
        Assert.That(e.GPA, Is.EqualTo(2.344679594039917).Within(1e-12));
        Assert.That(e.PhD, Is.False);
        Assert.That(save.Fired, Is.EqualTo(0));
        Assert.That(save.RunPeak, Is.EqualTo(50638.93591308594).Within(1e-6));
        Assert.That(save.MeetingsHeld, Is.EqualTo(0));
        Assert.That(save.TowerIncomeMax, Is.EqualTo(0), "lobby and CEO office earn nothing");
        Assert.That(save.OfficeIncomeMax, Is.EqualTo(SaveCalibration.OfficeIncomeFallback));
        Assert.That(save.UnlockedSkills, Is.Not.Null.And.Empty);
    }

    [Test]
    public void Snapshot_TowerIncome()
    {
        var save = new SaveBuilder { Rooms = new() { ("trading_floor", new[] { 5, 5, 5, 5 }), ("casino_room", new[] { 0, 0 }), ("small_office", new[] { 2, 2 }), ("future_room", new[] { 1 }) } };
        SaveSnapshot parsed = SaveSnapshot.Parse(save.Json());
        double expected = 1500 * (1 + 0.25 * 20) + 20 * 150   // trading floor, furnished (format 3: room level 1)
                        + 2500                                  // casino room, bare
                        + 4 * 150                               // small office furniture
                        + 2500 * 1.25 * 2.3 + 150;              // an unknown room: the casino room's base at the top level multiplier
        Assert.That(parsed.TowerIncomeMax, Is.EqualTo(expected).Within(1e-6));
    }

    [Test]
    public void Snapshot_MissingSkillsListMeansUnknown()
    {
        SaveSnapshot parsed = SaveSnapshot.Parse(new SaveBuilder { Skills = null }.Json());
        Assert.That(parsed.Ok, Is.True);
        Assert.That(parsed.UnlockedSkills, Is.Null);
    }
}
